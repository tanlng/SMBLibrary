using System;

namespace SMBLibrary.Server.Leasing
{
    /// <summary>
    /// 租约自动过期范围（Auto-expire scope for directory leases）
    /// </summary>
    public enum LeaseAutoExpireScope
    {
        /// <summary>
        /// 仅中断当前路径的过期租约（Break expired leases for current path only）
        /// </summary>
        CurrentLease = 0,
        
        /// <summary>
        /// 中断当前会话的所有过期租约（Break all expired leases for current session）
        /// </summary>
        AllSessionLeases = 1
    }
    
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
        /// SC特殊功能：目录租约自动过期时间（秒）
        /// 
        /// Purpose:
        /// - Optimize directory listing consistency across multiple clients
        /// - Automatically break stale directory leases without FileSystemWatcher dependency
        /// 
        /// Behavior:
        /// - Only applies to DIRECTORY leases (files use traditional lease break mechanism)
        /// - When client sends Create request with existing lease key, server checks lease age
        /// - If age > LeaseAutoExpireSeconds, server sends standard Lease Break BEFORE opening directory
        /// - After Break acknowledgment, client can re-acquire fresh lease
        /// 
        /// Configuration:
        /// - Default: 0 seconds
        /// - Set to 0 or negative to disable auto-expire
        /// - Only effective when SupportDirectoryLeasing = true
        /// 
        /// Implementation Location:
        /// - Detection: CreateHelper.GetCreateResponse() before CreateFile()
        /// - Break: LeaseBreakCoordinator.BreakLeaseAsync()
        /// 
        /// Note: 文件租约不受此配置影响，继续使用高性能的传统 Break 机制
        /// </summary>
        public int LeaseAutoExpireSeconds { get; set; } = 0;
        
        /// <summary>
        /// SC特殊功能：租约自动过期范围（Auto-expire scope）
        /// 
        /// Purpose:
        /// - Control the scope of lease break when auto-expire is triggered
        /// 
        /// Modes:
        /// - CurrentLease (default): Only break expired leases for the CURRENT directory path
        ///   * Optimized for single directory refresh
        ///   * Minimal impact on other open directories
        /// 
        /// - AllSessionLeases: Break ALL expired leases for the CURRENT SESSION
        ///   * Comprehensive cleanup for user's all stale directory leases
        ///   * Use when client has multiple directories open and needs batch refresh
        /// 
        /// Configuration:
        /// - Default: CurrentLease (conservative, minimal impact)
        /// - Only effective when LeaseAutoExpireSeconds > 0
        /// 
        /// Implementation:
        /// - Detection: CreateHelper.CheckAndBreakExpiredDirectoryLeases()
        /// - Break: LeaseManager.BreakLeases() per path or session
        /// </summary>
        public LeaseAutoExpireScope LeaseAutoExpireScope { get; set; } = LeaseAutoExpireScope.CurrentLease;
        
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
