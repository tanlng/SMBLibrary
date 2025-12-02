using System;
using SMBLibrary.SMB2;

namespace SMBLibrary.Server.Leasing
{
    /// <summary>
    /// SMB 2.0/2.1 Lease information class
    /// </summary>
    public class LeaseInfo
    {
        /// <summary>
        /// Lease key
        /// </summary>
        public Guid LeaseKey { get; set; }
        
        /// <summary>
        /// Lease state
        /// </summary>
        public LeaseState State { get; set; }
        
        /// <summary>
        /// Lease flags
        /// </summary>
        public LeaseFlags Flags { get; set; }

        /// <summary>
        /// Lease Epoch (Version)
        /// </summary>
        public ushort Epoch { get; set; }
        
        /// <summary>
        /// Creation time
        /// </summary>
        public DateTime CreatedTime { get; set; }
        
        /// <summary>
        /// Expiration time
        /// </summary>
        public DateTime ExpirationTime { get; set; }
        
        /// <summary>
        /// Session ID
        /// </summary>
        public ulong SessionId { get; set; }
        
        /// <summary>
        /// File ID
        /// </summary>
        public FileID FileId { get; set; }
        
        /// <summary>
        /// File path
        /// </summary>
        public string FilePath { get; set; }
        
        /// <summary>
        /// Pending break reason
        /// </summary>
        public LeaseBreakReason? PendingBreakReason { get; set; }

        /// <summary>
        /// Time when the break was requested
        /// </summary>
        public DateTime BreakStartTime { get; set; }
        
        /// <summary>
        /// Last access time
        /// </summary>
        public DateTime LastAccessTime { get; set; }
        
        /// <summary>
        /// Access count
        /// </summary>
        public int AccessCount { get; set; }

        /// <summary>
        /// Whether the lease is expired
        /// </summary>
        public bool IsExpired => DateTime.UtcNow > ExpirationTime;

        /// <summary>
        /// Whether the lease is breaking
        /// </summary>
        public bool IsBreaking => PendingBreakReason.HasValue;

        /// <summary>
        /// Remaining time
        /// </summary>
        public TimeSpan RemainingTime => ExpirationTime - DateTime.UtcNow;

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseInfo()
        {
            LeaseKey = Guid.Empty;
            State = LeaseState.None;
            Flags = LeaseFlags.None;
            CreatedTime = DateTime.MinValue;
            ExpirationTime = DateTime.MinValue;
            SessionId = 0;
            FileId = new FileID();
            FilePath = null;
            PendingBreakReason = null;
            LastAccessTime = DateTime.MinValue;
            AccessCount = 0;
        }

        /// <summary>
        /// Update access information
        /// </summary>
        public void UpdateAccess()
        {
            LastAccessTime = DateTime.UtcNow;
            AccessCount++;
        }

        /// <summary>
        /// Reset object state
        /// </summary>
        public void Reset()
        {
            LeaseKey = Guid.Empty;
            State = LeaseState.None;
            Flags = LeaseFlags.None;
            Epoch = 0;
            CreatedTime = DateTime.MinValue;
            ExpirationTime = DateTime.MinValue;
            SessionId = 0;
            FileId = new FileID();
            FilePath = null;
            PendingBreakReason = null;
            LastAccessTime = DateTime.MinValue;
            AccessCount = 0;
        }
    }
}
