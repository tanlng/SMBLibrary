using System;

namespace SMBLibrary.Server.Leasing
{
    /// <summary>
    /// Base class for SMB 2.0/2.1 lease-related exceptions
    /// </summary>
    public class LeaseException : Exception
    {
        /// <summary>
        /// Lease key
        /// </summary>
        public Guid LeaseKey { get; set; }
        
        /// <summary>
        /// Error code
        /// </summary>
        public LeaseErrorCode ErrorCode { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseException(string message, Guid leaseKey, LeaseErrorCode errorCode)
            : base(message)
        {
            LeaseKey = leaseKey;
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseException(string message, Exception innerException, Guid leaseKey, LeaseErrorCode errorCode)
            : base(message, innerException)
        {
            LeaseKey = leaseKey;
            ErrorCode = errorCode;
        }
    }
}
