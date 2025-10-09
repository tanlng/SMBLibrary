using System;

namespace SMBLibrary.Server.Leasing
{
    /// <summary>
    /// SMB 2.0/2.1 Lease error code enumeration
    /// </summary>
    public enum LeaseErrorCode
    {
        /// <summary>
        /// No error
        /// </summary>
        None = 0,
        
        /// <summary>
        /// Lease not found
        /// </summary>
        LeaseNotFound = 1,
        
        /// <summary>
        /// Lease expired
        /// </summary>
        LeaseExpired = 2,
        
        /// <summary>
        /// Lease invalid
        /// </summary>
        LeaseInvalid = 3,
        
        /// <summary>
        /// Lease break in progress
        /// </summary>
        LeaseBreakInProgress = 4,
        
        /// <summary>
        /// Lease already exists
        /// </summary>
        LeaseAlreadyExists = 5,
        
        /// <summary>
        /// Lease permission denied
        /// </summary>
        LeasePermissionDenied = 6,
        
        /// <summary>
        /// Resource exhausted
        /// </summary>
        LeaseResourceExhausted = 7
    }
}
