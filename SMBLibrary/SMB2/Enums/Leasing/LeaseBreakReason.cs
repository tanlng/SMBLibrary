using System;

namespace SMBLibrary.SMB2
{
    /// <summary>
    /// SMB 2.0/2.1 Lease Break Reason enumeration
    /// </summary>
    public enum LeaseBreakReason
    {
        /// <summary>
        /// No reason
        /// </summary>
        None = 0,
        
        /// <summary>
        /// Write request
        /// </summary>
        WriteRequest = 1,
        
        /// <summary>
        /// Handle close
        /// </summary>
        HandleClose = 2,
        
        /// <summary>
        /// Session logoff
        /// </summary>
        SessionLogoff = 3,
        
        /// <summary>
        /// File delete
        /// </summary>
        FileDelete = 4,
        
        /// <summary>
        /// File rename
        /// </summary>
        FileRename = 5,
        
        /// <summary>
        /// File move
        /// </summary>
        FileMove = 6,
        
        /// <summary>
        /// Directory rename
        /// </summary>
        DirectoryRename = 7,
        
        /// <summary>
        /// Directory move
        /// </summary>
        DirectoryMove = 8,
        
        /// <summary>
        /// Lease expired
        /// </summary>
        LeaseExpired = 9,
        
        /// <summary>
        /// Server shutdown
        /// </summary>
        ServerShutdown = 10,
    }
}
