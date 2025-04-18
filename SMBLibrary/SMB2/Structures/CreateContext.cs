/* Copyright (C) 2017-2024 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Collections.Generic;
using Utilities;

namespace SMBLibrary.SMB2
{
    // 定义 DH2QCreateContextData 类
    public class DH2QCreateContextData
    {
        public uint Timeout { get; set; }
        public uint Flags { get; set; }
        public ulong Reserved { get; set; }
        public Guid CreateGuid { get; set; }

        // 将对象转换为 buffer 的方法
        public byte[] ToBuffer()
        {
            int bufferSize = 4 + 4 + 8 + 16;
            byte[] buffer = new byte[bufferSize];
            int offset = 0;

            // 写入 Timeout
            byte[] timeoutBytes = BitConverter.GetBytes(Timeout);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(timeoutBytes);
            }
            Array.Copy(timeoutBytes, 0, buffer, offset, 4);
            offset += 4;

            // 写入 Flags
            byte[] flagsBytes = BitConverter.GetBytes(Flags);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(flagsBytes);
            }
            Array.Copy(flagsBytes, 0, buffer, offset, 4);
            offset += 4;

            // 写入 Reserved
            byte[] reservedBytes = BitConverter.GetBytes(Reserved);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(reservedBytes);
            }
            Array.Copy(reservedBytes, 0, buffer, offset, 8);
            offset += 8;

            // 写入 Create Guid
            byte[] createGuidBytes = CreateGuid.ToByteArray();
            Array.Copy(createGuidBytes, 0, buffer, offset, 16);

            return buffer;
        }

        // 将 buffer 转换为对象的方法
        public static DH2QCreateContextData BufferToDH2QCreateContextData(byte[] buffer)
        {
            int offset = 0;

            // 读取 Timeout
            byte[] timeoutBytes = new byte[4];
            Array.Copy(buffer, offset, timeoutBytes, 0, 4);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(timeoutBytes);
            }
            uint timeout = BitConverter.ToUInt32(timeoutBytes, 0);
            offset += 4;

            // 读取 Flags
            byte[] flagsBytes = new byte[4];
            Array.Copy(buffer, offset, flagsBytes, 0, 4);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(flagsBytes);
            }
            uint flags = BitConverter.ToUInt32(flagsBytes, 0);
            offset += 4;

            // 读取 Reserved
            byte[] reservedBytes = new byte[8];
            Array.Copy(buffer, offset, reservedBytes, 0, 8);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(reservedBytes);
            }
            ulong reserved = BitConverter.ToUInt64(reservedBytes, 0);
            offset += 8;

            // 读取 Create Guid
            byte[] createGuidBytes = new byte[16];
            Array.Copy(buffer, offset, createGuidBytes, 0, 16);
            Guid createGuid = new Guid(createGuidBytes);

            // 创建并返回 DH2QCreateContextData 对象
            return new DH2QCreateContextData
            {
                Timeout = timeout,
                Flags = flags,
                Reserved = reserved,
                CreateGuid = createGuid
            };
        }
    }

    public class LeaseV2CreateContextData
    {
        public Guid LeaseKey { get; set; }
        public uint LeaseState { get; set; }
        public uint LeaseFlags { get; set; }
        public ulong LeaseDuration { get; set; }
        public Guid ParentLeaseKey { get; set; }
        public ushort LeaseEpoch { get; set; }
        public ushort LeaseReserved { get; set; }

        public byte[] ToBuffer()
        {
            // 计算 Buffer 大小
            int bufferSize = 16 + 4 + 4 + 8 + 16 + 2 + 2;
            byte[] buffer = new byte[bufferSize];

            int offset = 0;

            // 将 Lease Key 写入 Buffer
            byte[] leaseKeyBytes = LeaseKey.ToByteArray();
            Array.Copy(leaseKeyBytes, 0, buffer, offset, leaseKeyBytes.Length);
            offset += leaseKeyBytes.Length;

            // 将 Lease State 写入 Buffer
            LittleEndianWriter.WriteUInt32(buffer, offset, LeaseState);
            offset += 4;

            // 将 Lease Flags 写入 Buffer
            byte[] leaseFlagsBytes = BitConverter.GetBytes(LeaseFlags);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(leaseFlagsBytes);
            }
            Array.Copy(leaseFlagsBytes, 0, buffer, offset, leaseFlagsBytes.Length);
            offset += leaseFlagsBytes.Length;

            // 将 Lease Duration 写入 Buffer
            byte[] leaseDurationBytes = BitConverter.GetBytes(LeaseDuration);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(leaseDurationBytes);
            }
            Array.Copy(leaseDurationBytes, 0, buffer, offset, leaseDurationBytes.Length);
            offset += leaseDurationBytes.Length;

            // 将 Parent Lease Key 写入 Buffer
            byte[] parentLeaseKeyBytes = ParentLeaseKey.ToByteArray();
            Array.Copy(parentLeaseKeyBytes, 0, buffer, offset, parentLeaseKeyBytes.Length);
            offset += parentLeaseKeyBytes.Length;

            // 将 Lease Epoch 写入 Buffer
            LittleEndianWriter.WriteUInt16(buffer, offset, LeaseEpoch);
            offset += 2;

            // 将 Lease Reserved 写入 Buffer
            byte[] leaseReservedBytes = BitConverter.GetBytes(LeaseReserved);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(leaseReservedBytes);
            }
            Array.Copy(leaseReservedBytes, 0, buffer, offset, leaseReservedBytes.Length);

            return buffer;
        }
        // 将 buffer 转换为对象的方法
        public static LeaseV2CreateContextData BufferToLeaseV2CreateContextData(byte[] buffer)
        {
            int offset = 0;

            // 读取 Lease Key
            byte[] leaseKeyBytes = new byte[16];
            Array.Copy(buffer, offset, leaseKeyBytes, 0, 16);
            Guid leaseKey = new Guid(leaseKeyBytes);
            offset += 16;

            // 读取 Lease State
            uint leaseState = LittleEndianReader.ReadUInt32(buffer,ref offset);

            // 读取 Lease Flags
            byte[] leaseFlagsBytes = new byte[4];
            Array.Copy(buffer, offset, leaseFlagsBytes, 0, 4);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(leaseFlagsBytes);
            }
            uint leaseFlags = BitConverter.ToUInt32(leaseFlagsBytes, 0);
            offset += 4;

            // 读取 Lease Duration
            byte[] leaseDurationBytes = new byte[8];
            Array.Copy(buffer, offset, leaseDurationBytes, 0, 8);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(leaseDurationBytes);
            }
            ulong leaseDuration = BitConverter.ToUInt64(leaseDurationBytes, 0);
            offset += 8;

            // 读取 Parent Lease Key
            byte[] parentLeaseKeyBytes = new byte[16];
            Array.Copy(buffer, offset, parentLeaseKeyBytes, 0, 16);
            Guid parentLeaseKey = new Guid(parentLeaseKeyBytes);
            offset += 16;

            // 读取 Lease Epoch
            byte[] leaseEpochBytes = new byte[2];
            Array.Copy(buffer, offset, leaseEpochBytes, 0, 2);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(leaseEpochBytes);
            }
            ushort leaseEpoch = BitConverter.ToUInt16(leaseEpochBytes, 0);
            offset += 2;

            // 读取 Lease Reserved
            byte[] leaseReservedBytes = new byte[2];
            Array.Copy(buffer, offset, leaseReservedBytes, 0, 2);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(leaseReservedBytes);
            }
            ushort leaseReserved = BitConverter.ToUInt16(leaseReservedBytes, 0);

            // 创建并返回 LeaseV2CreateContextData 对象
            return new LeaseV2CreateContextData
            {
                LeaseKey = leaseKey,
                LeaseState = leaseState,
                LeaseFlags = leaseFlags,
                LeaseDuration = leaseDuration,
                ParentLeaseKey = parentLeaseKey,
                LeaseEpoch = leaseEpoch,
                LeaseReserved = leaseReserved
            };
        }
    }

    /// <summary>
    /// [MS-SMB2] 2.2.13.2 - SMB2_CREATE_CONTEXT
    /// </summary>
    public class CreateContext
    {
        public const int FixedLength = 16;

        /// <summary>
        /// The offset from the beginning of this Create Context to the beginning of a subsequent 8-byte aligned Create Context.
        /// This field MUST be set to 0 if there are no subsequent contexts.
        /// </summary>
        public uint Next;
        private ushort NameOffset; // The offset from the beginning of this structure to the 8-byte aligned name value
        private ushort NameLength;
        public ushort Reserved;
        private ushort DataOffset; // The offset from the beginning of this structure to the 8-byte aligned data payload
        private uint DataLength;
        public string Name = String.Empty;
        public byte[] Data = new byte[0];

        public CreateContext()
        {
        }

        public CreateContext(byte[] buffer, int offset)
        {
            Next = LittleEndianConverter.ToUInt32(buffer, offset + 0);
            NameOffset = LittleEndianConverter.ToUInt16(buffer, offset + 4);
            NameLength = LittleEndianConverter.ToUInt16(buffer, offset + 6);
            Reserved = LittleEndianConverter.ToUInt16(buffer, offset + 8);
            DataOffset = LittleEndianConverter.ToUInt16(buffer, offset + 10);
            DataLength = LittleEndianConverter.ToUInt32(buffer, offset + 12);
            if (NameLength > 0)
            {
                Name = ByteReader.ReadAnsiString(buffer, offset + NameOffset, NameLength);
            }
            if (DataLength > 0)
            {
                Data = ByteReader.ReadBytes(buffer, offset + DataOffset, (int)DataLength);
            }
        }

        private void WriteBytes(byte[] buffer, int offset)
        {
            LittleEndianWriter.WriteUInt32(buffer, offset + 0, Next);
            NameOffset = 0;
            NameLength = (ushort)Name.Length;
            if (Name.Length > 0)
            {
                NameOffset = FixedLength;
            }
            LittleEndianWriter.WriteUInt16(buffer, offset + 4, NameOffset);
            LittleEndianWriter.WriteUInt16(buffer, offset + 6, NameLength);
            LittleEndianWriter.WriteUInt16(buffer, offset + 8, Reserved);
            DataOffset = (ushort)(FixedLength + NameLength);
            DataLength = (uint)Data.Length;
            if (Data.Length > 0)
            {
                int paddedNameLength = (int)Math.Ceiling((double)Name.Length / 8) * 8;
                DataOffset = (ushort)(FixedLength + paddedNameLength);
            }
            LittleEndianWriter.WriteUInt16(buffer, offset + 10, DataOffset);
            LittleEndianWriter.WriteUInt32(buffer, offset + 12, DataLength);
            ByteWriter.WriteAnsiString(buffer, offset + NameOffset, Name);
            ByteWriter.WriteBytes(buffer, offset + DataOffset, Data);
        }

        public int Length
        {
            get
            {
                if (Data.Length > 0)
                {
                    int paddedNameLength = (int)Math.Ceiling((double)(Name.Length * 2) / 8) * 8;
                    return FixedLength + paddedNameLength + Data.Length;
                }
                else
                {
                    return FixedLength + Name.Length * 2;
                }
            }
        }

        public static List<CreateContext> ReadCreateContextList(byte[] buffer, int offset)
        {
            List<CreateContext> result = new List<CreateContext>();
            CreateContext createContext;
            do
            {
                createContext = new CreateContext(buffer, offset);
                result.Add(createContext);
                offset += (int)createContext.Next;
            }
            while (createContext.Next != 0);

            return result;
        }

        public static void WriteCreateContextList(byte[] buffer, int offset, List<CreateContext> createContexts)
        {
            for (int index = 0; index < createContexts.Count; index++)
            {
                CreateContext createContext = createContexts[index];
                int length = createContext.Length;
                int paddedLength = (int)Math.Ceiling((double)length / 8) * 8;
                if (index < createContexts.Count - 1)
                {
                    createContext.Next = (uint)paddedLength;
                }
                else
                {
                    createContext.Next = 0;
                }
                createContext.WriteBytes(buffer, offset);
                offset += paddedLength;
            }
        }

        public static int GetCreateContextListLength(List<CreateContext> createContexts)
        {
            int result = 0;
            for (int index = 0; index < createContexts.Count; index++)
            {
                CreateContext createContext = createContexts[index];
                int length = createContext.Length;
                if (index < createContexts.Count - 1)
                {
                    int paddedLength = (int)Math.Ceiling((double)length / 8) * 8;
                    result += paddedLength;
                }
                else
                {
                    result += length;
                }
            }
            return result;
        }
    }
}
