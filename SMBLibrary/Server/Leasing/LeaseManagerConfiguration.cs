using System;

namespace SMBLibrary.Server.Leasing
{
    /// <summary>
    /// SMB 2.0/2.1 Lease manager configuration class
    /// </summary>
    public class LeaseManagerConfiguration
    {
        /// <summary>
        /// Maximum number of leases
        /// </summary>
        public int MaxLeases { get; set; } = 10000;
        
        /// <summary>
        /// Lease break timeout (time to wait for client ACK after Break notification)
        /// Recommended: 30s (Samba) or 35s (Windows Server)
        /// </summary>
        public TimeSpan LeaseBreakTimeout { get; set; } = TimeSpan.FromSeconds(35);
        
        /// <summary>
        /// Cleanup interval
        /// </summary>
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
        
        /// <summary>
        /// Enable lease break notifications
        /// </summary>
        public bool EnableLeaseBreakNotifications { get; set; } = true;
        
        /// <summary>
        /// Enable lease expiration events
        /// </summary>
        public bool EnableLeaseExpirationEvents { get; set; } = true;
        
        /// <summary>
        /// Support directory leasing (SMB3 Directory Leasing)
        /// 
        /// When false (default):
        /// - Server behavior aligns with Samba without SMB2_CAP_DIRECTORY_LEASING
        /// - Client can request directory leases, but server silently rejects them
        /// - Returns OplockLevel.None for directory opens (no RpLs context)
        /// 
        /// When true:
        /// - Server grants directory leases (SMB 3.0+ feature)
        /// - Enables parent directory lease notifications on file create/delete
        /// - Requires client support for SMB 3.0+ with directory leasing capability
        /// 
        /// Reference: MS-SMB2 2.2.13, MS-FSA 2.1.5.18
        /// </summary>
        public bool SupportDirectoryLeasing { get; set; } = false;
        
        /// <summary>
        /// Log level
        /// </summary>
        public LogLevel LogLevel { get; set; } = LogLevel.Information;

        /// <summary>
        /// Validate configuration
        /// </summary>
        public void Validate()
        {
            if (MaxLeases <= 0)
                throw new ArgumentException("MaxLeases must be positive");
            if (LeaseBreakTimeout <= TimeSpan.Zero)
                throw new ArgumentException("LeaseBreakTimeout must be positive");
            if (CleanupInterval <= TimeSpan.Zero)
                throw new ArgumentException("CleanupInterval must be positive");
        }
    }

    /// <summary>
    /// Log level enumeration
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// Debug
        /// </summary>
        Debug = 0,
        
        /// <summary>
        /// Information
        /// </summary>
        Information = 1,
        
        /// <summary>
        /// Warning
        /// </summary>
        Warning = 2,
        
        /// <summary>
        /// Error
        /// </summary>
        Error = 3
    }
}
