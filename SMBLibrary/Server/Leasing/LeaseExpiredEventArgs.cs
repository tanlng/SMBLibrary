using System;

namespace SMBLibrary.Server.Leasing
{
    /// <summary>
    /// Lease expired event arguments
    /// </summary>
    public class LeaseExpiredEventArgs : EventArgs
    {
        /// <summary>
        /// Lease information
        /// </summary>
        public LeaseInfo LeaseInfo { get; set; }
        
        /// <summary>
        /// Expiration time
        /// </summary>
        public DateTime ExpirationTime { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseExpiredEventArgs(LeaseInfo leaseInfo)
        {
            LeaseInfo = leaseInfo;
            ExpirationTime = leaseInfo.ExpirationTime;
        }
    }
}
