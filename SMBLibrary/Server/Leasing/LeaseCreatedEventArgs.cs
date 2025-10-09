using System;

namespace SMBLibrary.Server.Leasing
{
    /// <summary>
    /// Lease created event arguments
    /// </summary>
    public class LeaseCreatedEventArgs : EventArgs
    {
        /// <summary>
        /// Lease information
        /// </summary>
        public LeaseInfo LeaseInfo { get; set; }
        
        /// <summary>
        /// Creation time
        /// </summary>
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseCreatedEventArgs(LeaseInfo leaseInfo)
        {
            LeaseInfo = leaseInfo;
            CreatedTime = leaseInfo.CreatedTime;
        }
    }
}
