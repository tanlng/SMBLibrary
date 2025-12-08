/* Copyright (C) 2014-2024 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using SMBLibrary.SMB2;
using Utilities;

namespace SMBLibrary.Server
{
    internal class SMB2ConnectionState : ConnectionState
    {
        // Global Session ID counter (shared across all connections)
        private static long s_globalNextSessionID = 0;
        private static readonly object s_sessionIdLock = new object();
        
        // Key is SessionID
        private Dictionary<ulong, SMB2Session> m_sessions = new Dictionary<ulong, SMB2Session>();
        // Key is AsyncID
        private Dictionary<ulong, SMB2AsyncContext> m_pendingRequests = new Dictionary<ulong, SMB2AsyncContext>();
        private ulong m_nextAsyncID = 1;
        private SMBLibrary.Server.Leasing.LeaseManagerConfiguration m_leaseConfig;
        private SMBLibrary.Server.Leasing.LeaseManager m_leaseManager;

        public SMBLibrary.Server.Leasing.LeaseManager LeaseManager => m_leaseManager;
        public SMBLibrary.Server.Leasing.LeaseManagerConfiguration LeaseConfig => m_leaseConfig;

        public SMB2ConnectionState(ConnectionState state, SMBLibrary.Server.Leasing.LeaseManagerConfiguration leaseConfig = null, SMBLibrary.Server.Leasing.LeaseManager leaseManager = null) : base(state)
        {
            m_leaseConfig = leaseConfig;
            m_leaseManager = leaseManager;
            // Note: Do NOT set m_leaseManager.LogHandler here!
            // The global LeaseManager's LogHandler should be set only once in SMBServer,
            // not overwritten by each ConnectionState.
        }

        /// <summary>
        /// Check if server supports directory leasing (SMB3 Directory Leasing capability).
        /// Returns true only if LeaseConfiguration is set AND SupportDirectoryLeasing is enabled.
        /// </summary>
        public bool SupportsDirectoryLeasing()
        {
            return m_leaseConfig != null && m_leaseConfig.SupportDirectoryLeasing;
        }

        public ulong? AllocateSessionID()
        {
            lock (s_sessionIdLock)
            {
                long sessionID;
                do
                {
                    sessionID = System.Threading.Interlocked.Increment(ref s_globalNextSessionID);
                    // Skip invalid Session IDs (0 and 0xFFFFFFFF are reserved)
                    if (sessionID == 0 || sessionID == 0xFFFFFFFF)
                    {
                        continue;
                    }
                    // Check if already in use (should be rare with global counter)
                    if (!m_sessions.ContainsKey((ulong)sessionID))
                    {
                        LogToServer(Severity.Debug, $"[SMB2ConnectionState] Allocated SessionID: {sessionID}");
                        return (ulong)sessionID;
                    }
                } while (sessionID < Int64.MaxValue);
                
                return null;
            }
        }

        public SMB2Session CreateSession(ulong sessionID, string userName, string machineName, byte[] sessionKey, object accessToken, bool signingRequired, byte[] signingKey)
        {
            SMB2Session session = new SMB2Session(this, sessionID, userName, machineName, sessionKey, accessToken, signingRequired, signingKey, m_leaseConfig, m_leaseManager);
            lock (m_sessions)
            {
                m_sessions.TryAdd(sessionID, session);
            }
            if (sessionID == lastSessionId)
            {
                lastSession = session;
            }
            return session;
        }
        private ulong lastSessionId = 0;
        private SMB2Session lastSession = null;
        public SMB2Session GetSession(ulong sessionID)
        {
            if (lastSessionId == sessionID && lastSession != null)
            {
                return lastSession;
            }
            SMB2Session session;
            lock (m_sessions)
            {
                m_sessions.TryGetValue(sessionID, out session);
            }
            lastSessionId = sessionID;
            lastSession = session;
            return session;
        }

        public void RemoveSession(ulong sessionID)
        {
            SMB2Session session;
            lock (m_sessions)
            {
                m_sessions.TryGetValue(sessionID, out session);
            }
            if (session != null)
            {
                session.Close();
                lock (m_sessions)
                {
                    m_sessions.Remove(sessionID, out _);
                }
                if (sessionID == lastSessionId)
                {
                    lastSessionId = 0;
                    lastSession = null;
                }
            }
        }

        public override void CloseSessions()
        {
            lock (m_sessions)
            {
                foreach (SMB2Session session in m_sessions.Values)
                {
                    session.Close();
                }

                m_sessions.Clear();
            }
        }

        public override List<SessionInformation> GetSessionsInformation()
        {
            List<SessionInformation> result = new List<SessionInformation>();
            lock (m_sessions)
            {
                foreach (SMB2Session session in m_sessions.Values)
                {
                    result.Add(new SessionInformation(this.ClientEndPoint, this.Dialect, session.UserName, session.MachineName, session.GetOpenFilesInformation(), session.CreationDT));
                }
            }
            return result;
        }

        private ulong? AllocateAsyncID()
        {
            for (ulong offset = 0; offset < UInt64.MaxValue; offset++)
            {
                ulong asyncID = (ulong)(m_nextAsyncID + offset);
                if (asyncID == 0 || asyncID == 0xFFFFFFFF)
                {
                    continue;
                }
                if (!m_pendingRequests.ContainsKey(asyncID))
                {
                    m_nextAsyncID = (ulong)(asyncID + 1);
                    return asyncID;
                }
            }
            return null;
        }

        public SMB2AsyncContext CreateAsyncContext(FileID fileID, SMB2ConnectionState connection, ulong sessionID, uint treeID)
        {
            ulong? asyncID = AllocateAsyncID();
            if (asyncID == null)
            {
                return null;
            }
            SMB2AsyncContext context = new SMB2AsyncContext();
            context.AsyncID = asyncID.Value;
            context.FileID = fileID;
            context.Connection = connection;
            context.SessionID = sessionID;
            context.TreeID = treeID;
            lock (m_pendingRequests)
            {
                m_pendingRequests.Add(asyncID.Value, context);
            }
            return context;
        }

        public SMB2AsyncContext GetAsyncContext(ulong asyncID)
        {
            SMB2AsyncContext context;
            lock (m_pendingRequests)
            {
                m_pendingRequests.TryGetValue(asyncID, out context);
            }
            return context;
        }

        /// <summary>
        /// Get AsyncContext by MessageID, used for handling synchronous CANCEL requests
        /// </summary>
        /// <param name="messageID">Original request's MessageID</param>
        /// <param name="sessionID">Session ID for filtering</param>
        /// <returns>Found AsyncContext, or null if not found</returns>
        public SMB2AsyncContext GetAsyncContextByMessageID(ulong messageID, ulong sessionID)
        {
            lock (m_pendingRequests)
            {
                foreach (var context in m_pendingRequests.Values)
                {
                    if (context.MessageID == messageID && context.SessionID == sessionID)
                    {
                        return context;
                    }
                }
            }
            return null;
        }

        public void RemoveAsyncContext(SMB2AsyncContext context)
        {
            lock (m_pendingRequests)
            {
                m_pendingRequests.Remove(context.AsyncID);
            }
        }
    }
}
