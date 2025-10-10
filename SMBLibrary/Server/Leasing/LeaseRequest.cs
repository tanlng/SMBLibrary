using System;
using SMBLibrary.SMB2;

namespace SMBLibrary.Server.Leasing
{
    /// <summary>
    /// SMB 2.0/2.1 Lease request class
    /// </summary>
    public class LeaseRequest
    {
        /// <summary>
        /// Lease key
        /// </summary>
        public Guid LeaseKey { get; set; }
        
        /// <summary>
        /// Lease state
        /// </summary>
        public LeaseState LeaseState { get; set; }
        
        /// <summary>
        /// Lease flags
        /// </summary>
        public LeaseFlags LeaseFlags { get; set; }
        
        /// <summary>
        /// Lease duration
        /// </summary>
        public TimeSpan LeaseDuration { get; set; }
        
        /// <summary>
        /// File path
        /// </summary>
        public string FilePath { get; set; }
        
        /// <summary>
        /// Session ID
        /// </summary>
        public ulong SessionId { get; set; }
        
        /// <summary>
        /// File ID
        /// </summary>
        public FileID FileId { get; set; }
        
        /// <summary>
        /// Desired access rights
        /// </summary>
        public AccessMask DesiredAccess { get; set; }
        
        /// <summary>
        /// Share access rights
        /// </summary>
        public ShareAccess ShareAccess { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseRequest()
        {
            LeaseKey = Guid.Empty;
            LeaseState = LeaseState.None;
            LeaseFlags = LeaseFlags.None;
            LeaseDuration = TimeSpan.Zero;
            FilePath = null;
            SessionId = 0;
            FileId = new FileID();
            DesiredAccess = AccessMask.GENERIC_READ | AccessMask.GENERIC_WRITE;
            ShareAccess = ShareAccess.None;
        }

        /// <summary>
        /// Validate if the request is valid
        /// </summary>
        public bool IsValid()
        {
            // Note: LeaseDuration is not validated here per MS-SMB2 spec
            // Client sends LeaseDuration as 0, server determines actual duration
            return LeaseKey != Guid.Empty &&
                   LeaseState != LeaseState.None &&
                   !string.IsNullOrEmpty(FilePath) &&
                   SessionId != 0;
        }
    }
}
