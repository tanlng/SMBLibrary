using System;
using Utilities;

namespace SMBLibrary.SMB2
{
    /// <summary>
    /// SMB 2.0/2.1 Lease break response (Client -> Server)
    /// </summary>
    public class LeaseBreakResponse : SMB2Command
    {
        public const int FixedLength = 36;

        private ushort StructureSize;
        public ushort Reserved;
        public uint Flags;
        public Guid LeaseKey;
        public LeaseState LeaseState;
        public ulong LeaseDuration;

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseBreakResponse() : base(SMB2CommandName.OplockBreak)
        {
            StructureSize = FixedLength;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public LeaseBreakResponse(byte[] buffer, int offset) : base(buffer, offset)
        {
            StructureSize = LittleEndianConverter.ToUInt16(buffer, offset + SMB2Header.Length + 0);
            Reserved = LittleEndianConverter.ToUInt16(buffer, offset + SMB2Header.Length + 2);
            Flags = LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 4);
            LeaseKey = LittleEndianConverter.ToGuid(buffer, offset + SMB2Header.Length + 8);
            LeaseState = (LeaseState)LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 24);
            LeaseDuration = LittleEndianConverter.ToUInt64(buffer, offset + SMB2Header.Length + 28);
        }

        /// <summary>
        /// Write command bytes
        /// </summary>
        public override void WriteCommandBytes(byte[] buffer, int offset)
        {
            LittleEndianWriter.WriteUInt16(buffer, offset + 0, StructureSize);
            LittleEndianWriter.WriteUInt16(buffer, offset + 2, Reserved);
            LittleEndianWriter.WriteUInt32(buffer, offset + 4, Flags);
            byte[] guidBytes = LittleEndianConverter.GetBytes(LeaseKey);
            Array.Copy(guidBytes, 0, buffer, offset + 8, 16);
            LittleEndianWriter.WriteUInt32(buffer, offset + 24, (uint)LeaseState);
            LittleEndianWriter.WriteUInt64(buffer, offset + 28, LeaseDuration);
        }

        /// <summary>
        /// Command length
        /// </summary>
        public override int CommandLength => FixedLength;
    }
}
