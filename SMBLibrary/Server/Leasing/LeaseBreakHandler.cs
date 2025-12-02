using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SMBLibrary.SMB2;

namespace SMBLibrary.Server.Leasing
{
    /// <summary>
    /// SMB 2.0/2.1 Lease break handler
    /// </summary>
    public class LeaseBreakHandler
    {
        private readonly LeaseManager m_leaseManager;
        private readonly Dictionary<Guid, DateTime> m_pendingBreaks;
        private readonly Timer m_timeoutTimer;
        private readonly object m_lock;

        /// <summary>
        /// Lease break timeout event
        /// </summary>
        public event EventHandler<LeaseBreakTimeoutEventArgs> LeaseBreakTimeout;

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseBreakHandler(LeaseManager leaseManager)
        {
            m_leaseManager = leaseManager ?? throw new ArgumentNullException(nameof(leaseManager));
            m_pendingBreaks = new Dictionary<Guid, DateTime>();
            m_lock = new object();
            
            // Start timeout check timer
            m_timeoutTimer = new Timer(CheckTimeouts, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        /// <summary>
        /// Notify lease break
        /// </summary>
        public void NotifyLeaseBreak(LeaseInfo leaseInfo, LeaseBreakReason reason)
        {
            if (leaseInfo == null)
                throw new ArgumentNullException(nameof(leaseInfo));

            lock (m_lock)
            {
                m_pendingBreaks[leaseInfo.LeaseKey] = DateTime.UtcNow;
            }

            // Send lease break notification
            SendLeaseBreakNotification(leaseInfo, reason);
        }

        /// <summary>
        /// Process lease break acknowledgment
        /// </summary>
        public void ProcessLeaseBreakAcknowledgment(LeaseBreakResponse ack)
        {
            if (ack == null)
                throw new ArgumentNullException(nameof(ack));

            try
            {
                m_leaseManager.AcknowledgeLeaseBreak(ack.LeaseKey);
                
                lock (m_lock)
                {
                    m_pendingBreaks.Remove(ack.LeaseKey);
                }
            }
            catch (LeaseNotFoundException)
            {
                // Lease not found, ignore
            }
            catch (Exception ex)
            {
                throw new LeaseException($"Failed to acknowledge lease break: {ex.Message}", 
                    ack.LeaseKey, LeaseErrorCode.LeaseBreakInProgress);
            }
        }

        /// <summary>
        /// Handle lease break timeout
        /// </summary>
        public void HandleLeaseBreakTimeout(LeaseInfo leaseInfo)
        {
            if (leaseInfo == null)
                throw new ArgumentNullException(nameof(leaseInfo));

            try
            {
                // Force acknowledge lease break
                m_leaseManager.AcknowledgeLeaseBreak(leaseInfo.LeaseKey);
                
                lock (m_lock)
                {
                    m_pendingBreaks.Remove(leaseInfo.LeaseKey);
                }

                LeaseBreakTimeout?.Invoke(this, new LeaseBreakTimeoutEventArgs(leaseInfo.LeaseKey));
            }
            catch (Exception ex)
            {
                throw new LeaseException($"Failed to handle lease break timeout: {ex.Message}", 
                    leaseInfo.LeaseKey, LeaseErrorCode.LeaseBreakInProgress);
            }
        }

        /// <summary>
        /// Send lease break notification
        /// </summary>
        private void SendLeaseBreakNotification(LeaseInfo leaseInfo, LeaseBreakReason reason)
        {
            // This method will be implemented to send actual network notifications
            // For now, it's a placeholder that will be connected to the SMB server's
            // network sending mechanism in the SMB2Session class
        }

        /// <summary>
        /// Check timeouts
        /// </summary>
        private void CheckTimeouts(object state)
        {
            var timeoutLeases = new List<Guid>();
            var now = DateTime.UtcNow;

            lock (m_lock)
            {
                foreach (var kvp in m_pendingBreaks)
                {
                    if (now - kvp.Value > TimeSpan.FromMinutes(5)) // 5 minute timeout
                    {
                        timeoutLeases.Add(kvp.Key);
                    }
                }
            }

            foreach (var leaseKey in timeoutLeases)
            {
                try
                {
                    var leaseInfo = m_leaseManager.GetLeaseInfo(leaseKey);
                    if (leaseInfo != null)
                    {
                        HandleLeaseBreakTimeout(leaseInfo);
                    }
                }
                catch (Exception ex)
                {
                    // Error handling for lease break timeout
                    // This should be logged using the server's logging mechanism
                    // when integrated with SMB2Session
                }
            }
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            m_timeoutTimer?.Dispose();
        }
    }

    /// <summary>
    /// Lease break timeout event arguments
    /// </summary>
    public class LeaseBreakTimeoutEventArgs : EventArgs
    {
        /// <summary>
        /// Lease key
        /// </summary>
        public Guid LeaseKey { get; set; }

        /// <summary>
        /// Timeout time
        /// </summary>
        public DateTime TimeoutTime { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseBreakTimeoutEventArgs(Guid leaseKey)
        {
            LeaseKey = leaseKey;
            TimeoutTime = DateTime.UtcNow;
        }
    }
}
