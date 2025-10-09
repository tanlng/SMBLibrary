using System;

namespace SMBLibrary.Server.Leasing
{
    /// <summary>
    /// Exception thrown when lease has expired
    /// </summary>
    public class LeaseExpiredException : LeaseException
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseExpiredException(Guid leaseKey)
            : base($"Lease expired: {leaseKey}", leaseKey, LeaseErrorCode.LeaseExpired)
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseExpiredException(Guid leaseKey, Exception innerException)
            : base($"Lease expired: {leaseKey}", innerException, leaseKey, LeaseErrorCode.LeaseExpired)
        {
        }
    }
}
