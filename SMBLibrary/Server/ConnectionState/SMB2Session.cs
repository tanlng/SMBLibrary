/* Copyright (C) 2014-2020 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SMBLibrary.SMB2;
using SMBLibrary.Server.Leasing;
using SMBLibrary.Utilities;
using SMBLibrary.NetBios;
using Utilities;

namespace SMBLibrary.Server
{
    internal class SMB2Session
    {
        private SMB2ConnectionState m_connection;
        private ulong m_sessionID;
        private byte[] m_sessionKey;
        private SecurityContext m_securityContext;
        private DateTime m_creationDT;
        private bool m_signingRequired;
        private byte[] m_signingKey;

        // Key is TreeID
        private Dictionary<uint, ISMBShare> m_connectedTrees = new Dictionary<uint, ISMBShare>();
        private uint m_nextTreeID = 1; // TreeID uniquely identifies a tree connect within the scope of the session

        // Key is the volatile portion of the FileID
        private Dictionary<ulong, OpenFileObject> m_openFiles = new Dictionary<ulong, OpenFileObject>();
        private ulong m_nextVolatileFileID = 1;

        // Key is the volatile portion of the FileID
        private Dictionary<ulong, OpenSearch> m_openSearches = new Dictionary<ulong, OpenSearch>();

        // 租赁管理
        private LeaseManager m_leaseManager;
        private LeaseContextHandler m_leaseContextHandler;
        private LeaseBreakHandler m_leaseBreakHandler;

        /// <summary>
        /// Indicates whether this session supports leasing
        /// </summary>
        public bool SupportsLeasing => m_leaseManager != null;

        /// <summary>
        /// Gets the lease context handler (null if leasing is disabled)
        /// </summary>
        public LeaseContextHandler LeaseContextHandler => m_leaseContextHandler;

        public SMB2Session(SMB2ConnectionState connection, ulong sessionID, string userName, string machineName, byte[] sessionKey, object accessToken, bool signingRequired, byte[] signingKey, LeaseManagerConfiguration leaseConfig = null, LeaseManager leaseManager = null)
        {
            m_connection = connection;
            m_sessionID = sessionID;
            m_sessionKey = sessionKey;
            m_securityContext = new SecurityContext(userName, machineName, connection.ClientEndPoint, connection.AuthenticationContext, accessToken);
            m_creationDT = DateTime.UtcNow;
            m_signingRequired = signingRequired;
            m_signingKey = signingKey;

            // 初始化租赁管理器
            InitializeLeaseManager(leaseConfig, leaseManager);
        }

        /// <summary>
        /// Initialize lease manager
        /// </summary>
        private void InitializeLeaseManager(LeaseManagerConfiguration leaseConfig, LeaseManager leaseManager)
        {
            // Lease protocol is optional - only initialize if configuration is provided or manager is provided
            if (leaseConfig == null && leaseManager == null)
            {
                m_leaseManager = null;
                m_leaseContextHandler = null;
                m_leaseBreakHandler = null;
                return;
            }

            if (leaseManager != null)
            {
                m_leaseManager = leaseManager;
            }
            else
            {
                m_leaseManager = new LeaseManager(leaseConfig);
            }
            
            m_leaseContextHandler = new LeaseContextHandler(m_leaseManager);
            m_leaseBreakHandler = new LeaseBreakHandler(m_leaseManager);

            // Subscribe to lease events
            m_leaseManager.LeaseBreakRequested += OnLeaseBreakRequested;
            m_leaseManager.LeaseExpired += OnLeaseExpired;
        }

        /// <summary>
        /// Handle lease break request
        /// </summary>
        private void OnLeaseBreakRequested(object sender, LeaseBreakEventArgs e)
        {
            LogToServer(Severity.Information, "[SMB2Session] ⚡ OnLeaseBreakRequested TRIGGERED for LeaseKey: {0}, SessionID: {1}, Thread: {2}", 
                e.LeaseKey, m_sessionID, System.Threading.Thread.CurrentThread.ManagedThreadId);
            if (m_leaseManager == null)
                return;

            // Check if this lease belongs to this session
            try
            {
                LeaseInfo leaseInfo = m_leaseManager.GetLeaseInfo(e.LeaseKey);
                if (leaseInfo == null)
                {
                    LogToServer(Severity.Warning, "[SMB2Session] LeaseInfo not found for Key: {0}", e.LeaseKey);
                    return;
                }
                
                if (leaseInfo.SessionId != m_sessionID)
                {
                    LogToServer(Severity.Debug, "[SMB2Session] Lease belongs to different session. Lease Session: {0}, Current Session: {1}", leaseInfo.SessionId, m_sessionID);
                    return;
                }
                
                LogToServer(Severity.Debug, "[SMB2Session] Lease match found! Sending break notification.");
            }
            catch (Exception ex)
            {
                LogToServer(Severity.Error, "[SMB2Session] Error checking lease info: {0}", ex.Message);
                // Lease might have been removed or other error
                return;
            }
                
            // Send lease break notification
            SendLeaseBreakNotification(e.LeaseKey, e.Reason);
        }

        /// <summary>
        /// Handle lease expiration
        /// </summary>
        private void OnLeaseExpired(object sender, LeaseExpiredEventArgs e)
        {
            if (m_leaseManager == null)
                return;
                
            // Log lease expiration
            LogToServer(Severity.Information, "Lease expired: {0}", e.LeaseInfo.LeaseKey);
        }

        /// <summary>
        /// Send lease break notification
        /// </summary>
        private void SendLeaseBreakNotification(Guid leaseKey, LeaseBreakReason reason)
        {
            try
            {
                // Log detailed key info
                byte[] keyBytes = leaseKey.ToByteArray();
                LogToServer(Severity.Information, "[SMB2Session] 📤 SendLeaseBreakNotification START. Key: {0}, Reason: {1}, Thread: {2}", 
                    leaseKey, reason, System.Threading.Thread.CurrentThread.ManagedThreadId);

                // Create lease break request
                var leaseBreakRequest = new LeaseBreakRequest
                {
                    LeaseKey = leaseKey,
                    CurrentLeaseState = LeaseState.None, // Will be set by lease manager
                    NewLeaseState = LeaseState.None,    // Will be set by lease manager
                    Flags = 0x01, // SMB2_NOTIFY_BREAK_LEASE_FLAG_ACK_REQUIRED
                    NewEpoch = 0,
                    BreakReason = 0,
                    AccessMaskHint = 0,
                    ShareMaskHint = 0
                };

                // Get lease info to set proper states
                var leaseInfo = m_leaseManager.GetLeaseInfo(leaseKey);
                if (leaseInfo != null)
                {
                    leaseBreakRequest.CurrentLeaseState = leaseInfo.State;
                    leaseBreakRequest.NewLeaseState = DetermineNewLeaseState(leaseInfo.State, reason);
                    leaseBreakRequest.NewEpoch = leaseInfo.Epoch; // Use the incremented Epoch from LeaseManager
                    
                    // Set BreakReason based on NewLeaseState
                    // 0 = Break to None, 1 = Break to Read
                    if (leaseBreakRequest.NewLeaseState == LeaseState.None)
                    {
                        leaseBreakRequest.BreakReason = 0; // SMB2_OPLOCK_BREAK_TO_NONE
                    }
                    else
                    {
                        leaseBreakRequest.BreakReason = 1; // SMB2_OPLOCK_BREAK_TO_LEVEL_II
                    }

                    LogToServer(Severity.Debug, "[SMB2Session] Using Epoch: {0}, BreakReason: {1} for LeaseBreak", 
                        leaseBreakRequest.NewEpoch, leaseBreakRequest.BreakReason);
                }

                // Create SMB2 command with proper header
                var command = new LeaseBreakRequest();
                command.Header.Command = SMB2CommandName.OplockBreak;
                command.Header.SessionID = m_sessionID;
                command.Header.TreeID = 0; // Lease breaks are not tied to specific trees
                command.Header.CreditCharge = 0; // Must be 0
                command.Header.Credits = 0; // Must be 0
                command.Header.Flags = SMB2PacketHeaderFlags.ServerToRedir;
                command.Header.NextCommand = 0;
                command.Header.MessageID = 0xFFFFFFFFFFFFFFFF; // Unsolicited response (server-initiated notification)
                // ProcessID and StructureSize are private fields, skip setting them

                // Set lease break specific fields
                command.LeaseKey = leaseKey;
                command.CurrentLeaseState = leaseBreakRequest.CurrentLeaseState;
                command.NewLeaseState = leaseBreakRequest.NewLeaseState;
                command.Flags = leaseBreakRequest.Flags;
                command.NewEpoch = leaseBreakRequest.NewEpoch;
                command.BreakReason = leaseBreakRequest.BreakReason;
                command.AccessMaskHint = leaseBreakRequest.AccessMaskHint;
                command.ShareMaskHint = leaseBreakRequest.ShareMaskHint;

                // Send the command using the connection's send mechanism
                // This integrates with the existing SMB server's network sending infrastructure
                var responseChain = new List<SMB2Command> { command };
                var packet = new SessionMessagePacket();
                packet.Trailer = SMB2Command.GetCommandChainBytes(responseChain, m_signingKey, SMB2Dialect.SMB2xx);
                
                // Send through connection state
                LogToServer(Severity.Information, "[SMB2Session] 🚀 m_connection.Send() BEFORE. Key: {0}", leaseKey);
                m_connection.Send(packet);
                LogToServer(Severity.Information, "[SMB2Session] ✅ m_connection.Send() AFTER. Lease Break sent for {0}, reason: {1}", leaseKey, reason);
            }
            catch (Exception ex)
            {
                LogToServer(Severity.Error, "Failed to send lease break notification for lease {0}: {1}", leaseKey, ex.Message);
            }
        }

        /// <summary>
        /// Process lease break response from client
        /// </summary>
        public SMB2Command ProcessLeaseBreakResponse(LeaseBreakResponse response)
        {
            if (m_leaseBreakHandler != null)
            {
                m_leaseBreakHandler.ProcessLeaseBreakAcknowledgment(response);
            }
            return null; // No response required
        }

        /// <summary>
        /// Determine new lease state based on break reason
        /// </summary>
        private LeaseState DetermineNewLeaseState(LeaseState currentState, LeaseBreakReason reason)
        {
            switch (reason)
            {
                case LeaseBreakReason.WriteRequest:
                    // Remove all caching if write access is requested (or file modified)
                    return LeaseState.None;
                    
                case LeaseBreakReason.HandleClose:
                    // Remove handle caching
                    return currentState & ~LeaseState.HandleCaching;
                    
                case LeaseBreakReason.SessionLogoff:
                case LeaseBreakReason.ServerShutdown:
                    // Remove all caching
                    return LeaseState.None;
                    
                default:
                    // Keep current state for other reasons
                    return currentState;
            }
        }

        /// <summary>
        /// Log message to server
        /// </summary>
        private void LogToServer(Severity severity, string message, params object[] args)
        {
            if (m_connection != null)
            {
                m_connection.LogToServer(severity, string.Format(message, args));
            }
        }

        private uint? AllocateTreeID()
        {
            for (uint offset = 0; offset < UInt32.MaxValue; offset++)
            {
                uint treeID = (uint)(m_nextTreeID + offset);
                if (treeID == 0 || treeID == 0xFFFFFFFF)
                {
                    continue;
                }
                if (!m_connectedTrees.ContainsKey(treeID))
                {
                    m_nextTreeID = (uint)(treeID + 1);
                    return treeID;
                }
            }
            return null;
        }

        public uint? AddConnectedTree(ISMBShare share)
        {
            lock (m_connectedTrees)
            {
                uint? treeID = AllocateTreeID();
                if (treeID.HasValue)
                {
                    m_connectedTrees.Add(treeID.Value, share);
                }
                return treeID;
            }
        }

        public ISMBShare GetConnectedTree(uint treeID)
        {
            ISMBShare result;
            m_connectedTrees.TryGetValue(treeID, out result);
            return result;
        }

        public void DisconnectTree(uint treeID)
        {
            ISMBShare share;
            m_connectedTrees.TryGetValue(treeID, out share);
            if (share != null)
            {
                lock (m_openFiles)
                {
                    List<ulong> fileIDList = new List<ulong>(m_openFiles.Keys);
                    foreach (ulong fileID in fileIDList)
                    {
                        OpenFileObject openFile = m_openFiles[fileID];
                        if (openFile.TreeID == treeID)
                        {
                            share.FileStore.CloseFile(openFile.Handle);
                            m_openFiles.Remove(fileID);
                        }
                    }
                }
                lock (m_connectedTrees)
                {
                    m_connectedTrees.Remove(treeID);
                }
            }
        }

        public bool IsTreeConnected(uint treeID)
        {
            return m_connectedTrees.ContainsKey(treeID);
        }

        // VolatileFileID MUST be unique for all volatile handles within the scope of a session
        private ulong? AllocateVolatileFileID()
        {
            for (ulong offset = 0; offset < UInt64.MaxValue; offset++)
            {
                ulong volatileFileID = (ulong)(m_nextVolatileFileID + offset);
                if (volatileFileID == 0 || volatileFileID == 0xFFFFFFFFFFFFFFFF)
                {
                    continue;
                }
                if (!m_openFiles.ContainsKey(volatileFileID))
                {
                    m_nextVolatileFileID = (ulong)(volatileFileID + 1);
                    return volatileFileID;
                }
            }
            return null;
        }

        public FileID? AddOpenFile(uint treeID, ISMBShare share, string relativePath, object handle, FileAccess fileAccess)
        {
            lock (m_openFiles)
            {
                ulong? volatileFileID = AllocateVolatileFileID();
                if (volatileFileID.HasValue)
                {
                    FileID fileID = new FileID();
                    fileID.Volatile = volatileFileID.Value;
                    
                    // [MS-SMB2] FileId.Persistent MUST be set to Open.DurableFileId for durable handles.
                    // For non-durable handles, we use the file's unique identifier from the file system.
                    fileID.Persistent = GetPersistentFileID(share, handle, volatileFileID.Value);
                    
                    m_openFiles.Add(volatileFileID.Value, new OpenFileObject(treeID, share.Name, relativePath, handle, fileAccess));
                    return fileID;
                }
            }
            return null;
        }
        
        /// <summary>
        /// Get persistent file ID for the handle.
        /// This should return a stable identifier for the file that doesn't change on rename or append,
        /// but does change when the file is replaced/overwritten.
        /// Uses FileInternalInformation.IndexNumber from the file system.
        /// </summary>
        private ulong GetPersistentFileID(ISMBShare share, object handle, ulong volatileFileID)
        {
            try
            {
                // Try to get FileInternalInformation from the file store
                FileInformation fileInfo;
                NTStatus status = share.FileStore.GetFileInformation(
                    out fileInfo, 
                    handle, 
                    FileInformationClass.FileInternalInformation);
                
                if (status == NTStatus.STATUS_SUCCESS && fileInfo is FileInternalInformation internalInfo)
                {
                    ulong indexNumber = (ulong)internalInfo.IndexNumber;
                    
                    // Ensure it's not a reserved value
                    if (indexNumber != 0 && indexNumber != 0xFFFFFFFFFFFFFFFF)
                    {
                        return indexNumber;
                    }
                }
            }
            catch
            {
                // Ignore errors and fall back to volatile
            }
            
            // Fallback: use Volatile as Persistent
            return volatileFileID;
        }

        public OpenFileObject GetOpenFileObject(FileID fileID)
        {
            OpenFileObject result;
            m_openFiles.TryGetValue(fileID.Volatile, out result);
            return result;
        }

        public void RemoveOpenFile(FileID fileID)
        {
            lock (m_openFiles)
            {
                m_openFiles.Remove(fileID.Volatile);
            }
            m_openSearches.Remove(fileID.Volatile);
        }

        public List<OpenFileInformation> GetOpenFilesInformation()
        {
            List<OpenFileInformation> result = new List<OpenFileInformation>();
            lock (m_openFiles)
            {
                foreach (OpenFileObject openFile in m_openFiles.Values)
                {
                    result.Add(new OpenFileInformation(openFile.ShareName, openFile.Path, openFile.FileAccess, openFile.OpenedDT));
                }
            }
            return result;
        }

        public OpenSearch AddOpenSearch(FileID fileID, List<QueryDirectoryFileInformation> entries, int enumerationLocation)
        {
            OpenSearch openSearch = new OpenSearch(entries, enumerationLocation);
            m_openSearches.Add(fileID.Volatile, openSearch);
            return openSearch;
        }

        public OpenSearch GetOpenSearch(FileID fileID)
        {
            OpenSearch openSearch;
            m_openSearches.TryGetValue(fileID.Volatile, out openSearch);
            return openSearch;
        }

        public void RemoveOpenSearch(FileID fileID)
        {
            m_openSearches.Remove(fileID.Volatile);
        }

        /// <summary>
        /// Free all resources used by this session
        /// </summary>
        public void Close()
        {
            List<uint> treeIDList = new List<uint>(m_connectedTrees.Keys);
            foreach (uint treeID in treeIDList)
            {
                DisconnectTree(treeID);
            }
        }

        public byte[] SessionKey
        {
            get
            {
                return m_sessionKey;
            }
        }

        public SecurityContext SecurityContext
        {
            get
            {
                return m_securityContext;
            }
        }

        public string UserName
        {
            get
            {
                return m_securityContext.UserName;
            }
        }

        public string MachineName
        {
            get
            {
                return m_securityContext.MachineName;
            }
        }

        public DateTime CreationDT
        {
            get
            {
                return m_creationDT;
            }
        }

        public bool SigningRequired
        {
            get
            {
                return m_signingRequired;
            }
        }

        public byte[] SigningKey
        {
            get
            {
                return m_signingKey;
            }
        }
    }
}
