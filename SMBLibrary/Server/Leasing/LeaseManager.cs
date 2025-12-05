using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SMBLibrary.SMB2;
using Utilities;

namespace SMBLibrary.Server.Leasing
{
    /// <summary>
    /// SMB 2.0/2.1 Lease manager
    /// </summary>
    public class LeaseManager : IDisposable
    {
        /// <summary>
        /// Log handler delegate
        /// </summary>
        public Action<Severity, string> LogHandler { get; set; }

        private readonly ConcurrentDictionary<Guid, LeaseInfo> m_leaseRegistry;
        private readonly ConcurrentDictionary<FileID, List<Guid>> m_fileLeases;
        private readonly ConcurrentDictionary<ulong, List<Guid>> m_sessionLeases;
        private readonly SortedDictionary<DateTime, List<Guid>> m_expirationQueue;
        private readonly ReaderWriterLockSlim m_lock;
        private readonly Timer m_cleanupTimer;
        private readonly LeaseManagerConfiguration m_config;
        private bool m_disposed = false;

        /// <summary>
        /// Lease break requested event
        /// </summary>
        public event EventHandler<LeaseBreakEventArgs> LeaseBreakRequested;

        /// <summary>
        /// Lease expired event
        /// </summary>
        public event EventHandler<LeaseExpiredEventArgs> LeaseExpired;

        /// <summary>
        /// Lease created event
        /// </summary>
        public event EventHandler<LeaseCreatedEventArgs> LeaseCreated;

        /// <summary>
        /// Current active lease count
        /// </summary>
        public int ActiveLeaseCount => m_leaseRegistry.Count;

        /// <summary>
        /// Maximum lease count
        /// </summary>
        public int MaxLeases => m_config.MaxLeases;

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseManager(LeaseManagerConfiguration config = null)
        {
            m_config = config ?? new LeaseManagerConfiguration();
            m_config.Validate();

            m_leaseRegistry = new ConcurrentDictionary<Guid, LeaseInfo>();
            m_fileLeases = new ConcurrentDictionary<FileID, List<Guid>>();
            m_sessionLeases = new ConcurrentDictionary<ulong, List<Guid>>();
            m_expirationQueue = new SortedDictionary<DateTime, List<Guid>>();
            m_lock = new ReaderWriterLockSlim();

            // Start cleanup timer
            m_cleanupTimer = new Timer(CleanupExpiredLeasesCallback, null,
                0, (int)m_config.CleanupInterval.TotalMilliseconds);
        }

        /// <summary>
        /// Create new lease or return existing lease
        /// </summary>
        public LeaseInfo CreateLease(LeaseRequest request)
        {
            byte[] keyBytes = request.LeaseKey.ToByteArray();
            LogHandler?.Invoke(Severity.Debug, $"[LeaseManager] CreateLease requested. Key (Guid): {request.LeaseKey}, Key (Bytes): {BitConverter.ToString(keyBytes)}, Path: {request.FilePath}, State: {request.LeaseState.ToString()}");
            if (m_disposed)
                throw new ObjectDisposedException(nameof(LeaseManager));

            if (!request.IsValid())
                throw new LeaseException("Invalid lease request",
                    request.LeaseKey, LeaseErrorCode.LeaseInvalid);

            // Check if lease already exists
            if (m_leaseRegistry.TryGetValue(request.LeaseKey, out var existingLease))
            {
                // Per MS-SMB2 spec: If lease exists, validate and return it

                // Check if lease is expired
                if (existingLease.IsExpired)
                {
                    // Try to remove expired lease atomically
                    // If another thread already removed it, that's fine
                    if (m_leaseRegistry.TryRemove(request.LeaseKey, out var removedLease))
                    {
                        RemoveFromIndexes(removedLease);
                    }
                    // Fall through to create new lease below
                }
                else if (existingLease.SessionId != request.SessionId)
                {
                    // Different session trying to use same lease key - this is an error
                    throw new LeaseException("Lease already exists in different session",
                        request.LeaseKey, LeaseErrorCode.LeaseAlreadyExists);
                }
                else
                {
                    // Same session, same lease key - return existing lease
                    // Note: UpdateAccess() is not thread-safe, but the impact is minimal
                    // (just access tracking, not critical for correctness)
                    existingLease.UpdateAccess();

                    // [Phase 1 Fix]: If lease is breaking, reset the break state
                    // This happens when client acknowledges the break or renews the lease
                    if (existingLease.IsBreaking)
                    {
                        LogHandler?.Invoke(Severity.Information, 
                            $"[LeaseManager] 🔄 Lease {existingLease.LeaseKey} is breaking (Reason: {existingLease.PendingBreakReason}). " +
                            $"Client renewed/acknowledged. Resetting break state.");
                        existingLease.PendingBreakReason = null;
                    }

                    // [Phase 2 Fix]: Update Epoch if client sends a newer one
                    if (request.Epoch > existingLease.Epoch)
                    {
                        LogHandler?.Invoke(Severity.Information, 
                            $"[LeaseManager] 🆙 Lease {existingLease.LeaseKey} Epoch updated: {existingLease.Epoch} -> {request.Epoch}");
                        existingLease.Epoch = request.Epoch;
                    }
                    else if (request.Epoch > 0 && request.Epoch < existingLease.Epoch)
                    {
                         LogHandler?.Invoke(Severity.Warning, 
                            $"[LeaseManager] ⚠️ Client sent older Epoch {request.Epoch} for Lease {existingLease.LeaseKey} (Current: {existingLease.Epoch}). Ignoring.");
                    }

                    // Add new file to lease tracking if it's a different file
                    // Use struct comparison to properly compare FileID
                    bool isDifferentFile = (existingLease.FileId.Persistent != request.FileId.Persistent ||
                                           existingLease.FileId.Volatile != request.FileId.Volatile);

                    if (isDifferentFile)
                    {
                        // Per MS-SMB2: A lease can be associated with multiple files
                        // Add new file to tracking index
                        m_fileLeases.AddOrUpdate(request.FileId,
                            new List<Guid> { request.LeaseKey },
                            (key, existing) =>
                            {
                                lock (existing) // Thread-safe list modification
                                {
                                    if (!existing.Contains(request.LeaseKey))
                                        existing.Add(request.LeaseKey);
                                }
                                return existing;
                            });
                    }

                    return existingLease;
                }
            }

            // No existing lease or expired - create new lease
            if (m_leaseRegistry.Count >= m_config.MaxLeases)
                throw new LeaseException("Maximum lease count exceeded",
                    Guid.Empty, LeaseErrorCode.LeaseResourceExhausted);

            // Note: Per MS-SMB2 spec, LeaseDuration field is reserved and MUST be ignored.
            // Server does not use time-based lease expiration. Leases remain valid until
            // explicitly broken or released.

            var leaseInfo = new LeaseInfo
            {
                LeaseKey = request.LeaseKey,
                State = request.LeaseState,
                Flags = request.LeaseFlags,
                Epoch = 1, // Initialize Epoch to 1 for new Lease (V2)
                CreatedTime = DateTime.UtcNow,
                ExpirationTime = DateTime.MaxValue, // No expiration (MS-SMB2 compliant)
                SessionId = request.SessionId,
                FileId = request.FileId,
                FilePath = request.FilePath,
                LastAccessTime = DateTime.UtcNow,
                AccessCount = 0
            };

            if (!m_leaseRegistry.TryAdd(leaseInfo.LeaseKey, leaseInfo))
            {
                // Race condition: another thread created the lease between our check and add
                // Recursively call ourselves to handle the existing lease properly
                // This ensures we go through the same validation logic
                return CreateLease(request);
            }

            try
            {
                AddToIndexes(leaseInfo);
                
                // Detailed logging for lease creation
                // Note: Per MS-SMB2 spec, leases do not expire based on time
                LogHandler?.Invoke(Severity.Information, 
                    $"[LeaseManager] 🎁 Lease Created:\n" +
                    $"    Key: {leaseInfo.LeaseKey}\n" +
                    $"    Path: '{leaseInfo.FilePath}'\n" +
                    $"    State: {leaseInfo.State}\n" +
                    $"    Session: {leaseInfo.SessionId}\n" +
                    $"    Epoch: {leaseInfo.Epoch}\n" +
                    $"    Total Active Leases: {m_leaseRegistry.Count}");
                
                LeaseCreated?.Invoke(this, new LeaseCreatedEventArgs(leaseInfo));
                return leaseInfo;
            }
            catch
            {
                m_leaseRegistry.TryRemove(leaseInfo.LeaseKey, out _);
                throw;
            }
        }



        /// <summary>
        /// Break specified lease
        /// </summary>
        public void BreakLease(Guid leaseKey, LeaseBreakReason reason)
        {
            BreakLease(leaseKey, reason, LeaseState.None);
        }

        /// <summary>
        /// Break a lease with specified new lease state
        /// </summary>
        /// <param name="leaseKey">The lease key to break</param>
        /// <param name="reason">The reason for breaking the lease</param>
        /// <param name="newLeaseState">The new lease state (None for complete break, RH for downgrade, etc.)</param>
        public void BreakLease(Guid leaseKey, LeaseBreakReason reason, LeaseState newLeaseState)
        {
            Console.WriteLine($"[LeaseManager] BreakLease called for Key: {leaseKey}, Reason: {reason}, NewState: {newLeaseState}");
            if (m_disposed)
                throw new ObjectDisposedException(nameof(LeaseManager));

            if (!m_leaseRegistry.TryGetValue(leaseKey, out var leaseInfo))
                throw new LeaseNotFoundException(leaseKey);

            if (leaseInfo.IsExpired)
                throw new LeaseExpiredException(leaseKey);

            // Check if lease is already breaking - skip duplicate break
            if (leaseInfo.IsBreaking)
            {
                LogHandler?.Invoke(Severity.Information, 
                    $"[LeaseManager] ⚠️ Lease {leaseKey} is already breaking (Reason: {leaseInfo.PendingBreakReason}), skipping duplicate break request");
                return;
            }

            // Increment Epoch for Lease Break (V2 requirement)
            leaseInfo.Epoch++;
            Console.WriteLine($"[LeaseManager] Incremented Epoch to {leaseInfo.Epoch} for Lease {leaseKey}");

            leaseInfo.PendingBreakReason = reason;
            leaseInfo.BreakStartTime = DateTime.UtcNow;

            if (m_config.EnableLeaseBreakNotifications)
            {
                LeaseBreakRequested?.Invoke(this, new LeaseBreakEventArgs(leaseKey, reason, newLeaseState));
            }
        }

        /// <summary>
        /// Acknowledge lease break
        /// </summary>
        public void AcknowledgeLeaseBreak(Guid leaseKey)
        {
            if (m_disposed)
                throw new ObjectDisposedException(nameof(LeaseManager));

            if (!m_leaseRegistry.TryGetValue(leaseKey, out var leaseInfo))
                throw new LeaseNotFoundException(leaseKey);

            if (!leaseInfo.IsBreaking)
                throw new LeaseException("Lease is not in breaking state",
                    leaseKey, LeaseErrorCode.LeaseBreakInProgress);

            leaseInfo.PendingBreakReason = null;
            RemoveLease(leaseKey);
        }

        /// <summary>
        /// Get lease information by key
        /// </summary>
        public LeaseInfo GetLeaseInfo(Guid leaseKey)
        {
            if (m_disposed)
                throw new ObjectDisposedException(nameof(LeaseManager));

            m_leaseRegistry.TryGetValue(leaseKey, out var leaseInfo);
            return leaseInfo;
        }

        /// <summary>
        /// Get all active leases
        /// </summary>
        public List<LeaseInfo> GetActiveLeases()
        {
            if (m_disposed)
                throw new ObjectDisposedException(nameof(LeaseManager));

            return new List<LeaseInfo>(m_leaseRegistry.Values);
        }

        /// <summary>
        /// Get all leases for specified session
        /// </summary>
        public List<LeaseInfo> GetLeasesBySession(ulong sessionId)
        {
            if (m_disposed)
                throw new ObjectDisposedException(nameof(LeaseManager));

            var leases = new List<LeaseInfo>();
            if (m_sessionLeases.TryGetValue(sessionId, out var leaseKeys))
            {
                foreach (var key in leaseKeys)
                {
                    if (m_leaseRegistry.TryGetValue(key, out var lease))
                    {
                        leases.Add(lease);
                    }
                }
            }
            return leases;
        }

        /// <summary>
        /// Get all leases for specified file
        /// </summary>
        public List<LeaseInfo> GetLeasesByFile(FileID fileId)
        {
            if (m_disposed)
                throw new ObjectDisposedException(nameof(LeaseManager));

            var leases = new List<LeaseInfo>();
            if (m_fileLeases.TryGetValue(fileId, out var leaseKeys))
            {
                foreach (var key in leaseKeys)
                {
                    if (m_leaseRegistry.TryGetValue(key, out var lease))
                    {
                        leases.Add(lease);
                    }
                }
            }
            return leases;
        }

        /// <summary>
        /// Try to get lease information by key
        /// </summary>
        /// <param name="leaseKey">Lease key to lookup</param>
        /// <param name="leaseInfo">Output lease information if found</param>
        /// <returns>True if lease exists, false otherwise</returns>
        public bool TryGetLease(Guid leaseKey, out LeaseInfo leaseInfo)
        {
            if (m_disposed)
                throw new ObjectDisposedException(nameof(LeaseManager));

            return m_leaseRegistry.TryGetValue(leaseKey, out leaseInfo);
        }

        /// <summary>
        /// Remove specified lease
        /// </summary>
        public bool RemoveLease(Guid leaseKey)
        {
            if (m_disposed)
                throw new ObjectDisposedException(nameof(LeaseManager));

            if (m_leaseRegistry.TryRemove(leaseKey, out var leaseInfo))
            {
                RemoveFromIndexes(leaseInfo);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Break leases for a specific path (file or directory)
        /// This handles breaking the lease on the file itself AND the parent directory
        /// </summary>
        public void BreakLeases(string path)
        {
            BreakLeases(path, LeaseState.None, null);
        }

        /// <summary>
        /// Break leases for a specific path with specified new lease state
        /// </summary>
        /// <param name="path">The file or directory path</param>
        /// <param name="newLeaseState">The new lease state to downgrade to (None for complete break)</param>
        /// <param name="excludeSessionId">Optional: Session ID to exclude from breaking (for same-client operations)</param>
        public void BreakLeases(string path, LeaseState newLeaseState, ulong? excludeSessionId = null)
        {
            LogHandler?.Invoke(Severity.Debug, $"[LeaseManager] BreakLeases requested for: {path}, NewState: {newLeaseState}, ExcludeSession: {excludeSessionId}");
            if (string.IsNullOrEmpty(path)) return;

            // Normalize path separators
            path = path.Replace('/', '\\');
            if (!path.StartsWith("\\")) path = "\\" + path;

            string parentPath = null;
            int lastSlash = path.LastIndexOf('\\');
            if (lastSlash > 0) // Not root
            {
                parentPath = path.Substring(0, lastSlash);
            }
            else if (lastSlash == 0 && path.Length > 1) // File in root
            {
                parentPath = "\\";
            }

            var activeLeases = GetActiveLeases();
            LogHandler?.Invoke(Severity.Information, $"[LeaseManager] 🔍 BreakLeases: Checking {activeLeases.Count} active leases");
            LogHandler?.Invoke(Severity.Information, $"[LeaseManager] 🔍 Target path: '{path}'");
            LogHandler?.Invoke(Severity.Information, $"[LeaseManager] 🔍 Parent path: '{parentPath}'");
            
            // Log all active leases for debugging
            if (activeLeases.Count > 0)
            {
                LogHandler?.Invoke(Severity.Information, $"[LeaseManager] 📋 Active Leases List:");
                for (int i = 0; i < activeLeases.Count; i++)
                {
                    var lease = activeLeases[i];
                    LogHandler?.Invoke(Severity.Information, 
                        $"[LeaseManager]   [{i+1}] Key={lease.LeaseKey}, Path='{lease.FilePath}', " +
                        $"State={lease.State}, Session={lease.SessionId}, " +
                        $"Expired={lease.IsExpired}, ExpiresAt={lease.ExpirationTime:HH:mm:ss.fff}");
                }
            }

            foreach (var lease in activeLeases)
            {
                bool shouldBreak = false;
                string leasePath = lease.FilePath;
                
                if (string.IsNullOrEmpty(leasePath))
                {
                    LogHandler?.Invoke(Severity.Warning, $"[LeaseManager] ⚠️ Skipping lease {lease.LeaseKey} - FilePath is null/empty");
                    continue;
                }

                // Normalize lease path
                string originalLeasePath = leasePath;
                leasePath = leasePath.Replace('/', '\\');
                if (!leasePath.StartsWith("\\")) leasePath = "\\" + leasePath;

                LogHandler?.Invoke(Severity.Information, 
                    $"[LeaseManager] 🔎 Comparing Lease {lease.LeaseKey}:\n" +
                    $"    Original: '{originalLeasePath}'\n" +
                    $"    Normalized: '{leasePath}'\n" +
                    $"    Target: '{path}'\n" +
                    $"    Parent: '{parentPath}'\n" +
                    $"    Session: {lease.SessionId}, ExcludeSession: {excludeSessionId}");

                // Skip if this is the same session (client's own operation)
                if (excludeSessionId.HasValue && lease.SessionId == excludeSessionId.Value)
                {
                    LogHandler?.Invoke(Severity.Information, 
                        $"[LeaseManager] ⏭️ Skipping lease {lease.LeaseKey} - Same session (client's own operation)");
                    continue;
                }

                // 1. Exact match (File Lease or Directory Lease on the target itself)
                if (string.Equals(leasePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        LogHandler?.Invoke(Severity.Information, 
                            $"[LeaseManager] 🔨 Breaking lease {lease.LeaseKey.ToString()} for path {leasePath}, " +
                            $"Session: {lease.SessionId.ToString()}, NewState: {newLeaseState}");
                        // Break the lease with specified new state
                        BreakLease(lease.LeaseKey, LeaseBreakReason.WriteRequest, newLeaseState);
                    }
                    catch (Exception ex)
                    {
                        LogHandler?.Invoke(Severity.Error, $"[LeaseManager] ❌ Error breaking lease: {ex.Message}");
                    }
                }
            }
            
            LogHandler?.Invoke(Severity.Information, $"[LeaseManager] ✅ BreakLeases completed for path: {path}");
        }

        /// <summary>
        /// Break leases for the parent directory of the specified path
        /// 中断指定路径的父目录租约
        /// </summary>
        /// <param name="path">File or directory path</param>
        /// <param name="newLeaseState">New lease state after breaking</param>
        /// <param name="excludeSessionId">Session ID to exclude from breaking (optional)</param>
        public void BreakParentDirectoryLeases(string path, LeaseState newLeaseState, ulong? excludeSessionId = null)
        {
            if (string.IsNullOrEmpty(path)) return;

            // Normalize path
            path = path.Replace('/', '\\');
            if (!path.StartsWith("\\")) path = "\\" + path;

            // Calculate parent path
            string parentPath = null;
            int lastSlash = path.LastIndexOf('\\');
            if (lastSlash > 0) // Not root
            {
                parentPath = path.Substring(0, lastSlash);
            }
            else if (lastSlash == 0 && path.Length > 1) // File in root
            {
                parentPath = "\\";
            }

            if (string.IsNullOrEmpty(parentPath))
            {
                LogHandler?.Invoke(Severity.Debug, $"[LeaseManager] BreakParentDirectoryLeases: No parent directory for path: {path}");
                return;
            }

            LogHandler?.Invoke(Severity.Information, $"[LeaseManager] 📁 Breaking parent directory leases for: {parentPath} (child: {path})");
            
            // Break leases on the parent directory path
            // Note: breakParentDirectory=false to avoid recursive parent breaking
            BreakLeases(parentPath, newLeaseState, excludeSessionId);
        }

        /// <summary>
        /// Timer callback for cleanup
        /// </summary>
        private void CleanupExpiredLeasesCallback(object state)
        {
            CleanupExpiredLeases();
            ProcessLeaseBreakTimeouts();
        }

        /// <summary>
        /// Process lease break timeouts
        /// </summary>
        public void ProcessLeaseBreakTimeouts()
        {
            if (m_disposed) return;

            var activeLeases = GetActiveLeases();
            var now = DateTime.UtcNow;
            var timeout = m_config.LeaseBreakTimeout;

            foreach (var lease in activeLeases)
            {
                if (lease.IsBreaking && (now - lease.BreakStartTime) > timeout)
                {
                    Console.WriteLine($"[LeaseManager] Lease break timeout for Key: {lease.LeaseKey}. Forcing break acknowledgment.");
                    try
                    {
                        // Force acknowledge
                        AcknowledgeLeaseBreak(lease.LeaseKey);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[LeaseManager] Error processing lease break timeout: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Cleanup expired leases
        /// </summary>
        public int CleanupExpiredLeases()
        {
            if (m_disposed)
                return 0;

            var expiredLeases = new List<Guid>();

            m_lock.EnterReadLock();
            try
            {
                var now = DateTime.UtcNow;
                foreach (var kvp in m_expirationQueue)
                {
                    if (kvp.Key <= now)
                    {
                        expiredLeases.AddRange(kvp.Value);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            finally
            {
                m_lock.ExitReadLock();
            }

            foreach (var leaseKey in expiredLeases)
            {
                if (m_leaseRegistry.TryRemove(leaseKey, out var leaseInfo))
                {
                    RemoveFromIndexes(leaseInfo);
                    
                    // Detailed logging for expired leases
                    LogHandler?.Invoke(Severity.Information, 
                        $"[LeaseManager] ⏰ Lease Expired and Removed:\n" +
                        $"    Key: {leaseInfo.LeaseKey}\n" +
                        $"    Path: '{leaseInfo.FilePath}'\n" +
                        $"    Session: {leaseInfo.SessionId}\n" +
                        $"    Expired at: {leaseInfo.ExpirationTime:HH:mm:ss.fff}\n" +
                        $"    Age: {(DateTime.UtcNow - leaseInfo.CreatedTime).TotalSeconds:F1}s\n" +
                        $"    Remaining Active Leases: {m_leaseRegistry.Count}");
                    
                    if (m_config.EnableLeaseExpirationEvents)
                    {
                        LeaseExpired?.Invoke(this, new LeaseExpiredEventArgs(leaseInfo));
                    }
                }
            }

            if (expiredLeases.Count > 0)
            {
                LogHandler?.Invoke(Severity.Information, 
                    $"[LeaseManager] 🧹 Cleanup: Removed {expiredLeases.Count} expired lease(s), {m_leaseRegistry.Count} remain active");
            }

            return expiredLeases.Count;
        }

        /// <summary>
        /// Add to indexes
        /// </summary>
        private void AddToIndexes(LeaseInfo leaseInfo)
        {
            // Add to file index
            m_fileLeases.AddOrUpdate(leaseInfo.FileId,
                new List<Guid> { leaseInfo.LeaseKey },
                (key, existing) => { existing.Add(leaseInfo.LeaseKey); return existing; });

            // Add to session index
            m_sessionLeases.AddOrUpdate(leaseInfo.SessionId,
                new List<Guid> { leaseInfo.LeaseKey },
                (key, existing) => { existing.Add(leaseInfo.LeaseKey); return existing; });

            // Add to expiration queue
            m_lock.EnterWriteLock();
            try
            {
                if (!m_expirationQueue.ContainsKey(leaseInfo.ExpirationTime))
                {
                    m_expirationQueue[leaseInfo.ExpirationTime] = new List<Guid>();
                }
                m_expirationQueue[leaseInfo.ExpirationTime].Add(leaseInfo.LeaseKey);
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Remove from indexes
        /// </summary>
        private void RemoveFromIndexes(LeaseInfo leaseInfo)
        {
            // Remove from file index
            if (m_fileLeases.TryGetValue(leaseInfo.FileId, out var fileLeases))
            {
                fileLeases.Remove(leaseInfo.LeaseKey);
                if (fileLeases.Count == 0)
                {
                    m_fileLeases.TryRemove(leaseInfo.FileId, out _);
                }
            }

            // Remove from session index
            if (m_sessionLeases.TryGetValue(leaseInfo.SessionId, out var sessionLeases))
            {
                sessionLeases.Remove(leaseInfo.LeaseKey);
                if (sessionLeases.Count == 0)
                {
                    m_sessionLeases.TryRemove(leaseInfo.SessionId, out _);
                }
            }

            // Remove from expiration queue
            m_lock.EnterWriteLock();
            try
            {
                if (m_expirationQueue.TryGetValue(leaseInfo.ExpirationTime, out var expiredLeases))
                {
                    expiredLeases.Remove(leaseInfo.LeaseKey);
                    if (expiredLeases.Count == 0)
                    {
                        m_expirationQueue.Remove(leaseInfo.ExpirationTime);
                    }
                }
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_cleanupTimer?.Dispose();
                m_lock?.Dispose();
                m_disposed = true;
            }
        }
    }
}
