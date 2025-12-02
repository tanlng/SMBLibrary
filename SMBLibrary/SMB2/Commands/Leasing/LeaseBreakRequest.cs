using System;
using Utilities;

namespace SMBLibrary.SMB2
{
    /// <summary>
    /// SMB 2.0/2.1 Lease break notification (Server -> Client)
    /// </summary>
    public class LeaseBreakRequest : SMB2Command
    {
        public const int FixedLength = 44;

        private ushort StructureSize;
        public ushort NewEpoch;
        public uint Flags;
        public Guid LeaseKey;
        public LeaseState CurrentLeaseState;
        public LeaseState NewLeaseState;
        public uint BreakReason;
        public uint AccessMaskHint;
        public uint ShareMaskHint;

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseBreakRequest() : base(SMB2CommandName.OplockBreak)
        {
            StructureSize = FixedLength;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseBreakRequest(byte[] buffer, int offset) : base(buffer, offset)
        {
            StructureSize = LittleEndianConverter.ToUInt16(buffer, offset + SMB2Header.Length + 0);
            NewEpoch = LittleEndianConverter.ToUInt16(buffer, offset + SMB2Header.Length + 2);
            Flags = LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 4);
            LeaseKey = LittleEndianConverter.ToGuid(buffer, offset + SMB2Header.Length + 8);
            CurrentLeaseState = (LeaseState)LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 24);
            NewLeaseState = (LeaseState)LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 28);
            BreakReason = LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 32);
            AccessMaskHint = LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 36);
            ShareMaskHint = LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 40);
        }

        /// <summary>
        /// Write command bytes
        /// </summary>
        public override void WriteCommandBytes(byte[] buffer, int offset)
        {
            LittleEndianWriter.WriteUInt16(buffer, offset + 0, StructureSize);
            LittleEndianWriter.WriteUInt16(buffer, offset + 2, NewEpoch);
            LittleEndianWriter.WriteUInt32(buffer, offset + 4, Flags);
            byte[] guidBytes = LittleEndianConverter.GetBytes(LeaseKey);
            Array.Copy(guidBytes, 0, buffer, offset + 8, 16);
            LittleEndianWriter.WriteUInt32(buffer, offset + 24, (uint)CurrentLeaseState);
            LittleEndianWriter.WriteUInt32(buffer, offset + 28, (uint)NewLeaseState);
            LittleEndianWriter.WriteUInt32(buffer, offset + 32, BreakReason);
            LittleEndianWriter.WriteUInt32(buffer, offset + 36, AccessMaskHint);
            LittleEndianWriter.WriteUInt32(buffer, offset + 40, ShareMaskHint);
        }

        /// <summary>
        /// Command length
        /// </summary>
        public override int CommandLength => FixedLength;
    }
}
