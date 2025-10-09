using System;

namespace SMBLibrary.SMB2
{
    /// <summary>
    /// SMB 2.0/2.1 Lease Flags enumeration
    /// </summary>
    [Flags]
    public enum LeaseFlags : uint
    {
        /// <summary>
        /// No flags
        /// </summary>
        None = 0x00000000,
        
        /// <summary>
        /// Lease break in progress
        /// </summary>
        BreakInProgress = 0x00000002,
        
        /// <summary>
        /// Parent lease key is set
        /// </summary>
        ParentLeaseKeySet = 0x00000004,
        
        /// <summary>
        /// Directory lease
        /// </summary>
        DirectoryLease = 0x00000008
    }
}
