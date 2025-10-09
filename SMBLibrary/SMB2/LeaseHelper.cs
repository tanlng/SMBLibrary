using System;
using SMBLibrary.Server.Leasing;

namespace SMBLibrary.SMB2
{
    /// <summary>
    /// SMB 2.0/2.1 Lease helper class
    /// </summary>
    public static class LeaseHelper
    {
        /// <summary>
        /// Convert lease error code to NTStatus
        /// </summary>
        public static NTStatus ConvertLeaseErrorToNTStatus(LeaseErrorCode errorCode)
        {
            return errorCode switch
            {
                LeaseErrorCode.LeaseNotFound => NTStatus.STATUS_OBJECT_NAME_NOT_FOUND,
                LeaseErrorCode.LeaseExpired => NTStatus.STATUS_OBJECT_NAME_NOT_FOUND,
                LeaseErrorCode.LeaseInvalid => NTStatus.STATUS_INVALID_PARAMETER,
                LeaseErrorCode.LeaseResourceExhausted => NTStatus.STATUS_INSUFFICIENT_RESOURCES,
                LeaseErrorCode.LeaseAlreadyExists => NTStatus.STATUS_OBJECT_NAME_COLLISION,
                LeaseErrorCode.LeasePermissionDenied => NTStatus.STATUS_ACCESS_DENIED,
                LeaseErrorCode.LeaseBreakInProgress => NTStatus.STATUS_PENDING,
                _ => NTStatus.STATUS_NOT_IMPLEMENTED
            };
        }

        /// <summary>
        /// Check if leasing is supported
        /// </summary>
        public static bool SupportsLeasing(Capabilities capabilities)
        {
            return capabilities.HasFlag(Capabilities.Leasing);
        }

        /// <summary>
        /// Check if lease state is valid
        /// </summary>
        public static bool IsValidLeaseState(LeaseState state)
        {
            return state != LeaseState.None && 
                   (state.HasFlag(LeaseState.ReadCaching) || 
                    state.HasFlag(LeaseState.HandleCaching) || 
                    state.HasFlag(LeaseState.WriteCaching) || 
                    state.HasFlag(LeaseState.DirectoryCaching));
        }

        /// <summary>
        /// Check if lease flags are valid
        /// </summary>
        public static bool IsValidLeaseFlags(LeaseFlags flags)
        {
            // Check for invalid flag combinations
            if (flags.HasFlag(LeaseFlags.BreakInProgress) && flags.HasFlag(LeaseFlags.ParentLeaseKeySet))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Generate secure lease key
        /// </summary>
        public static Guid GenerateSecureLeaseKey()
        {
            return Guid.NewGuid();
        }

        /// <summary>
        /// Validate lease key
        /// </summary>
        public static bool IsValidLeaseKey(Guid leaseKey)
        {
            return leaseKey != Guid.Empty;
        }

        /// <summary>
        /// Calculate lease duration
        /// </summary>
        public static TimeSpan CalculateLeaseDuration(ulong durationMilliseconds)
        {
            if (durationMilliseconds == 0)
                return TimeSpan.FromMinutes(30); // Default 30 minutes

            return TimeSpan.FromMilliseconds(durationMilliseconds);
        }

        /// <summary>
        /// Check if lease is expired
        /// </summary>
        public static bool IsLeaseExpired(DateTime expirationTime)
        {
            return DateTime.UtcNow > expirationTime;
        }

        /// <summary>
        /// Check if lease should be broken
        /// </summary>
        public static bool ShouldBreakLease(LeaseState currentState, LeaseState requestedState)
        {
            // If write access is requested but current only has read access, break is needed
            if (requestedState.HasFlag(LeaseState.WriteCaching) && 
                !currentState.HasFlag(LeaseState.WriteCaching))
            {
                return true;
            }

            // If exclusive access is requested but current is shared access, break is needed
            if (requestedState.HasFlag(LeaseState.HandleCaching) && 
                !currentState.HasFlag(LeaseState.HandleCaching))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get lease break reason
        /// </summary>
        public static LeaseBreakReason GetLeaseBreakReason(LeaseState currentState, LeaseState requestedState)
        {
            if (requestedState.HasFlag(LeaseState.WriteCaching) && 
                !currentState.HasFlag(LeaseState.WriteCaching))
            {
                return LeaseBreakReason.WriteRequest;
            }

            if (requestedState.HasFlag(LeaseState.HandleCaching) && 
                !currentState.HasFlag(LeaseState.HandleCaching))
            {
                return LeaseBreakReason.HandleClose;
            }

            return LeaseBreakReason.None;
        }
    }
}
