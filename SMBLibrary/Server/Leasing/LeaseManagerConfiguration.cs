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
        /// Default lease duration
        /// </summary>
        public TimeSpan DefaultLeaseDuration { get; set; } = TimeSpan.FromMinutes(30);
        
        /// <summary>
        /// Lease break timeout
        /// </summary>
        public TimeSpan LeaseBreakTimeout { get; set; } = TimeSpan.FromSeconds(30);
        
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
            if (DefaultLeaseDuration <= TimeSpan.Zero)
                throw new ArgumentException("DefaultLeaseDuration must be positive");
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
