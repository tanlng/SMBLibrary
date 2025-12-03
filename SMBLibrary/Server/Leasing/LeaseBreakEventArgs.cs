using System;
using SMBLibrary.SMB2;

namespace SMBLibrary.Server.Leasing
{
    /// <summary>
    /// Lease break event arguments
    /// </summary>
    public class LeaseBreakEventArgs : EventArgs
    {
        /// <summary>
        /// Lease key
        /// </summary>
        public Guid LeaseKey { get; set; }
        
        /// <summary>
        /// Break reason
        /// </summary>
        public LeaseBreakReason Reason { get; set; }
        
        /// <summary>
        /// Break time
        /// </summary>
        public DateTime BreakTime { get; set; }

        /// <summary>
        /// New lease state after break (None for complete break, RH for downgrade, etc.)
        /// </summary>
        public LeaseState NewLeaseState { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseBreakEventArgs(Guid leaseKey, LeaseBreakReason reason)
            : this(leaseKey, reason, LeaseState.None)
        {
        }

        /// <summary>
        /// Constructor with new lease state
        /// </summary>
        public LeaseBreakEventArgs(Guid leaseKey, LeaseBreakReason reason, LeaseState newLeaseState)
        {
            LeaseKey = leaseKey;
            Reason = reason;
            NewLeaseState = newLeaseState;
            BreakTime = DateTime.UtcNow;
        }
    }
}
