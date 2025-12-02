using System;
using System.Collections.Generic;
using SMBLibrary.SMB2;

namespace SMBLibrary.Server.Leasing
{
    /// <summary>
    /// SMB 2.0/2.1 Lease context handler
    /// </summary>
    public class LeaseContextHandler
    {
        private readonly LeaseManager m_leaseManager;

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseContextHandler(LeaseManager leaseManager)
        {
            m_leaseManager = leaseManager ?? throw new ArgumentNullException(nameof(leaseManager));
        }

        /// <summary>
        /// Process lease context in Create request
        /// </summary>
        public LeaseContext ProcessCreateContext(CreateContext context, ulong sessionId, FileID fileId, string filePath)
        {
            if (!(context is LeaseContext leaseContext))
            {
                return null;
            }

            // Validate lease request
            if (!ValidateLeaseRequest(leaseContext))
            {
                throw new LeaseException("Invalid lease request", 
                    leaseContext.LeaseKey, LeaseErrorCode.LeaseInvalid);
            }

            // Create lease
            var request = new LeaseRequest
            {
                LeaseKey = leaseContext.LeaseKey,
                LeaseState = leaseContext.LeaseState,
                LeaseFlags = leaseContext.LeaseFlags,
                LeaseDuration = TimeSpan.FromMilliseconds(leaseContext.LeaseDuration),
                SessionId = sessionId,
                FileId = fileId,
                FilePath = filePath
            };

            var leaseInfo = m_leaseManager.CreateLease(request);

            // Generate response context
            return GenerateResponseContext(leaseInfo);
        }

        /// <summary>
        /// Generate lease response context
        /// </summary>
        public LeaseContext GenerateResponseContext(LeaseInfo leaseInfo)
        {
            if (leaseInfo == null)
                throw new ArgumentNullException(nameof(leaseInfo));

            var context = new LeaseContext(
                leaseInfo.LeaseKey,
                leaseInfo.State,
                leaseInfo.Flags,
                (ulong)Math.Max(0, leaseInfo.RemainingTime.TotalMilliseconds),
                Guid.Empty, // ParentLeaseKey (not used in response)
                leaseInfo.Epoch // Return the current Epoch (V2)
            );
            
            return context;
        }

        /// <summary>
        /// Validate lease request
        /// </summary>
        private bool ValidateLeaseRequest(LeaseContext context)
        {
            if (context == null)
                return false;

            // Validate lease key
            if (context.LeaseKey == Guid.Empty)
                return false;

            // Validate lease state
            if (context.LeaseState == LeaseState.None)
                return false;

            // Note: LeaseDuration is ignored per MS-SMB2 spec
            // Client must set LeaseDuration to 0, server determines actual duration

            return true;
        }
    }
}
