using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SMBLibrary.SMB2;

namespace SMBLibrary.Server.Leasing
{
    /// <summary>
    /// SMB 2.0/2.1 Lease manager
    /// </summary>
    public class LeaseManager : IDisposable
    {
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
        /// Create new lease
        /// </summary>
        public LeaseInfo CreateLease(LeaseRequest request)
        {
            if (m_disposed)
                throw new ObjectDisposedException(nameof(LeaseManager));

            if (!request.IsValid())
                throw new LeaseException("Invalid lease request", 
                    request.LeaseKey, LeaseErrorCode.LeaseInvalid);

            if (m_leaseRegistry.Count >= m_config.MaxLeases)
                throw new LeaseException("Maximum lease count exceeded", 
                    Guid.Empty, LeaseErrorCode.LeaseResourceExhausted);

            // Use default lease duration if client sends 0 (per MS-SMB2 spec)
            TimeSpan effectiveDuration = request.LeaseDuration > TimeSpan.Zero 
                ? request.LeaseDuration 
                : m_config.DefaultLeaseDuration;

            var leaseInfo = new LeaseInfo
            {
                LeaseKey = request.LeaseKey,
                State = request.LeaseState,
                Flags = request.LeaseFlags,
                CreatedTime = DateTime.UtcNow,
                ExpirationTime = DateTime.UtcNow.Add(effectiveDuration),
                SessionId = request.SessionId,
                FileId = request.FileId,
                FilePath = request.FilePath,
                LastAccessTime = DateTime.UtcNow,
                AccessCount = 0
            };

            if (!m_leaseRegistry.TryAdd(leaseInfo.LeaseKey, leaseInfo))
                throw new LeaseException("Lease already exists", 
                    leaseInfo.LeaseKey, LeaseErrorCode.LeaseAlreadyExists);

            try
            {
                AddToIndexes(leaseInfo);
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
            if (m_disposed)
                throw new ObjectDisposedException(nameof(LeaseManager));

            if (!m_leaseRegistry.TryGetValue(leaseKey, out var leaseInfo))
                throw new LeaseNotFoundException(leaseKey);

            if (leaseInfo.IsExpired)
                throw new LeaseExpiredException(leaseKey);

            leaseInfo.PendingBreakReason = reason;
            
            if (m_config.EnableLeaseBreakNotifications)
            {
                LeaseBreakRequested?.Invoke(this, new LeaseBreakEventArgs(leaseKey, reason));
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
        /// Timer callback for cleanup
        /// </summary>
        private void CleanupExpiredLeasesCallback(object state)
        {
            CleanupExpiredLeases();
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
                    if (m_config.EnableLeaseExpirationEvents)
                    {
                        LeaseExpired?.Invoke(this, new LeaseExpiredEventArgs(leaseInfo));
                    }
                }
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
