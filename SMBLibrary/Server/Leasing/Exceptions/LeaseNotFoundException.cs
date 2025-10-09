using System;

namespace SMBLibrary.Server.Leasing
{
    /// <summary>
    /// Exception thrown when lease is not found
    /// </summary>
    public class LeaseNotFoundException : LeaseException
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseNotFoundException(Guid leaseKey)
            : base($"Lease not found: {leaseKey}", leaseKey, LeaseErrorCode.LeaseNotFound)
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseNotFoundException(Guid leaseKey, Exception innerException)
            : base($"Lease not found: {leaseKey}", innerException, leaseKey, LeaseErrorCode.LeaseNotFound)
        {
        }
    }
}
