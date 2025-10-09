using System;

namespace SMBLibrary.SMB2
{
    /// <summary>
    /// SMB 2.0/2.1 Lease State enumeration
    /// </summary>
    [Flags]
    public enum LeaseState : uint
    {
        /// <summary>
        /// No lease
        /// </summary>
        None = 0x00000000,
        
        /// <summary>
        /// Read caching lease
        /// </summary>
        ReadCaching = 0x00000001,
        
        /// <summary>
        /// Handle caching lease
        /// </summary>
        HandleCaching = 0x00000002,
        
        /// <summary>
        /// Write caching lease
        /// </summary>
        WriteCaching = 0x00000004,
        
        /// <summary>
        /// Directory caching lease
        /// </summary>
        DirectoryCaching = 0x00000008
    }
}
