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
        /// Constructor
        /// </summary>
        public LeaseBreakEventArgs(Guid leaseKey, LeaseBreakReason reason)
        {
            LeaseKey = leaseKey;
            Reason = reason;
            BreakTime = DateTime.UtcNow;
        }
    }
}
