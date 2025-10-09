using System;
using System.Collections.Generic;
using Utilities;

namespace SMBLibrary.SMB2
{
    /// <summary>
    /// SMB 2.0/2.1 Lease context
    /// </summary>
    public class LeaseContext : CreateContext
    {
        public const string ContextName = "RqLs";

        /// <summary>
        /// Lease key
        /// </summary>
        public Guid LeaseKey;
        
        /// <summary>
        /// Lease state
        /// </summary>
        public LeaseState LeaseState;
        
        /// <summary>
        /// Lease flags
        /// </summary>
        public LeaseFlags LeaseFlags;
        
        /// <summary>
        /// Lease duration in milliseconds
        /// </summary>
        public ulong LeaseDuration;

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseContext()
        {
            Name = ContextName;
            Data = new byte[32]; // 32 bytes of lease data
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseContext(byte[] buffer, int offset) : base(buffer, offset)
        {
            Name = ContextName;
            ParseLeaseData();
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseContext(Guid leaseKey, LeaseState leaseState, LeaseFlags leaseFlags, ulong leaseDuration)
        {
            Name = ContextName;
            LeaseKey = leaseKey;
            LeaseState = leaseState;
            LeaseFlags = leaseFlags;
            LeaseDuration = leaseDuration;
            Data = new byte[32];
            WriteLeaseData();
        }

        /// <summary>
        /// Parse lease data
        /// </summary>
        private void ParseLeaseData()
        {
            if (Data.Length >= 32)
            {
                int offset = 0;
                LeaseKey = LittleEndianConverter.ToGuid(Data, offset);
                offset += 16;
                LeaseState = (LeaseState)LittleEndianConverter.ToUInt32(Data, offset);
                offset += 4;
                LeaseFlags = (LeaseFlags)LittleEndianConverter.ToUInt32(Data, offset);
                offset += 4;
                LeaseDuration = LittleEndianConverter.ToUInt64(Data, offset);
            }
        }

        /// <summary>
        /// Write lease data
        /// </summary>
        private void WriteLeaseData()
        {
            if (Data.Length < 32)
            {
                Data = new byte[32];
            }

            int offset = 0;
            byte[] guidBytes = LittleEndianConverter.GetBytes(LeaseKey);
            Array.Copy(guidBytes, 0, Data, offset, 16);
            offset += 16;
            byte[] stateBytes = LittleEndianConverter.GetBytes((uint)LeaseState);
            Array.Copy(stateBytes, 0, Data, offset, 4);
            offset += 4;
            byte[] flagsBytes = LittleEndianConverter.GetBytes((uint)LeaseFlags);
            Array.Copy(flagsBytes, 0, Data, offset, 4);
            offset += 4;
            byte[] durationBytes = LittleEndianConverter.GetBytes(LeaseDuration);
            Array.Copy(durationBytes, 0, Data, offset, 8);
        }

        /// <summary>
        /// Update lease data
        /// </summary>
        public void UpdateLeaseData()
        {
            WriteLeaseData();
        }
    }
}
