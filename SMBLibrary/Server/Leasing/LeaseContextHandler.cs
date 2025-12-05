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
        /// Validate lease request BEFORE opening file (Samba behavior: before_exec phase).
        /// Throws LeaseException if validation fails (expired, invalid, etc.).
        /// </summary>
        public void ValidateLeaseRequest(LeaseContext leaseContext, string filePath)
        {
            if (leaseContext == null)
            {
                throw new ArgumentNullException(nameof(leaseContext));
            }

            // Validate lease request parameters
            if (!ValidateLeaseRequestInternal(leaseContext))
            {
                throw new LeaseException("Invalid lease request", 
                    leaseContext.LeaseKey, LeaseErrorCode.LeaseInvalid);
            }

            // ⚠️ 屏蔽15秒过期逻辑 - 这个方案没走通，因为过期windows也不会重新刷新
            /*
            // Check if lease already exists and validate expiration (15 seconds from creation time)
            if (m_leaseManager.TryGetLease(leaseContext.LeaseKey, out var existingLease))
            {
                var leaseAge = DateTime.UtcNow - existingLease.CreatedTime;
                if (leaseAge.TotalSeconds > 15)
                {
                    // Lease has expired (> 15 seconds), remove it and throw exception
                    m_leaseManager.RemoveLease(leaseContext.LeaseKey);
                    throw new LeaseExpiredException(leaseContext.LeaseKey);
                }
                // If lease exists and is valid, update will happen in GrantLease phase
            }
            */
        }

        /// <summary>
        /// Grant lease AFTER file is opened (Samba behavior: after_exec phase).
        /// Creates or updates the lease and returns the response context.
        /// Validation must have been done before calling this method.
        /// </summary>
        public LeaseContext GrantLease(LeaseContext leaseContext, ulong sessionId, FileID fileId, string filePath)
        {
            if (leaseContext == null)
            {
                throw new ArgumentNullException(nameof(leaseContext));
            }

            // Create/update lease
            // Note: LeaseDuration is ignored per MS-SMB2 spec (client sends 0, server ignores)
            var request = new LeaseRequest
            {
                LeaseKey = leaseContext.LeaseKey,
                LeaseState = leaseContext.LeaseState,
                LeaseFlags = leaseContext.LeaseFlags,
                SessionId = sessionId,
                FileId = fileId,
                FilePath = filePath,
                Epoch = leaseContext.Epoch
            };

            var leaseInfo = m_leaseManager.CreateLease(request);

            // Generate response context
            return GenerateResponseContext(leaseInfo);
        }

        /// <summary>
        /// Process lease context in Create request (legacy single-phase method for backward compatibility).
        /// 
        /// WARNING: This method combines validation and granting in one step, which is NOT aligned with Samba behavior.
        /// New code should use ValidateLeaseRequest() before opening file, then GrantLease() after opening.
        /// 
        /// Implementation:
        /// - Validates lease request parameters (LeaseKey, LeaseState)
        /// - Checks if lease already exists:
        ///   * If exists and expired (> 15 seconds): Removes lease and throws LeaseExpiredException
        ///   * If exists and valid: Updates lease and returns existing lease context
        ///   * If not exists: Creates new lease via LeaseManager
        /// </summary>
        [Obsolete("Use ValidateLeaseRequest() before opening file, then GrantLease() after opening. This method is kept for backward compatibility.")]
        public LeaseContext ProcessCreateContext(CreateContext context, ulong sessionId, FileID fileId, string filePath)
        {
            if (!(context is LeaseContext leaseContext))
            {
                return null;
            }

            // Phase 1: Validate
            ValidateLeaseRequest(leaseContext, filePath);

            // Phase 2: Grant
            return GrantLease(leaseContext, sessionId, fileId, filePath);
        }

        /// <summary>
        /// Generate lease response context
        /// </summary>
        public LeaseContext GenerateResponseContext(LeaseInfo leaseInfo)
        {
            if (leaseInfo == null)
                throw new ArgumentNullException(nameof(leaseInfo));

            // Per MS-SMB2 spec: Server MUST return LeaseDuration = 0
            var context = new LeaseContext(
                leaseInfo.LeaseKey,
                leaseInfo.State,
                leaseInfo.Flags,
                0, // LeaseDuration MUST be 0 per MS-SMB2 specification
                Guid.Empty, // ParentLeaseKey (not used in response)
                leaseInfo.Epoch // Return the current Epoch (V2)
            );
            
            return context;
        }

        /// <summary>
        /// Internal validation of lease request parameters
        /// </summary>
        private bool ValidateLeaseRequestInternal(LeaseContext context)
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
