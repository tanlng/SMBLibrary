using System;
using Utilities;

namespace SMBLibrary.SMB2
{
    /// <summary>
    /// SMB 2.0/2.1 Lease break acknowledgment
    /// </summary>
    public class LeaseBreakAck : SMB2Command
    {
        public const int FixedLength = 24;

        private ushort StructureSize;
        public ushort Reserved;
        public Guid LeaseKey;
        public LeaseState CurrentLeaseState;
        public LeaseState NewLeaseState;
        public LeaseFlags LeaseFlags;
        public ulong LeaseDuration;

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseBreakAck() : base(SMB2CommandName.OplockBreak)
        {
            StructureSize = FixedLength;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseBreakAck(byte[] buffer, int offset) : base(buffer, offset)
        {
            StructureSize = LittleEndianConverter.ToUInt16(buffer, offset + SMB2Header.Length + 0);
            Reserved = LittleEndianConverter.ToUInt16(buffer, offset + SMB2Header.Length + 2);
            LeaseKey = LittleEndianConverter.ToGuid(buffer, offset + SMB2Header.Length + 4);
            CurrentLeaseState = (LeaseState)LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 20);
            NewLeaseState = (LeaseState)LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 24);
            LeaseFlags = (LeaseFlags)LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 28);
            LeaseDuration = LittleEndianConverter.ToUInt64(buffer, offset + SMB2Header.Length + 32);
        }

        /// <summary>
        /// Write command bytes
        /// </summary>
        public override void WriteCommandBytes(byte[] buffer, int offset)
        {
            LittleEndianWriter.WriteUInt16(buffer, offset + 0, StructureSize);
            LittleEndianWriter.WriteUInt16(buffer, offset + 2, Reserved);
            byte[] guidBytes = LittleEndianConverter.GetBytes(LeaseKey);
            Array.Copy(guidBytes, 0, buffer, offset + 4, 16);
            byte[] stateBytes = LittleEndianConverter.GetBytes((uint)CurrentLeaseState);
            Array.Copy(stateBytes, 0, buffer, offset + 20, 4);
            byte[] newStateBytes = LittleEndianConverter.GetBytes((uint)NewLeaseState);
            Array.Copy(newStateBytes, 0, buffer, offset + 24, 4);
            byte[] flagsBytes = LittleEndianConverter.GetBytes((uint)LeaseFlags);
            Array.Copy(flagsBytes, 0, buffer, offset + 28, 4);
            byte[] durationBytes = LittleEndianConverter.GetBytes(LeaseDuration);
            Array.Copy(durationBytes, 0, buffer, offset + 32, 8);
        }

        /// <summary>
        /// Command length
        /// </summary>
        public override int CommandLength => FixedLength;
    }
}
