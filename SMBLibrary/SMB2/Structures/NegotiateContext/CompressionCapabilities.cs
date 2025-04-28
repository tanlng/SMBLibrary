/* Copyright (C) 2017-2024 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System.Collections.Generic;
using Utilities;

namespace SMBLibrary.SMB2
{

    public enum CompressionAlgorithm
    {
        Pattern_V1 = 0x0004,
        LZ77 = 0x0002,
        LZ77_Huffman = 0x0003,
        LZNT1 = 0x0001
    }

    public class CompressionCapabilities : NegotiateContext
    {
        public ushort CompressionAlgorithmCount;
        public uint Flags;
        public List<CompressionAlgorithm> CompressionAlgorithmIds = new List<CompressionAlgorithm>();

        public CompressionCapabilities(byte[] buffer, int offset) : base(buffer, offset)
        {
            CompressionAlgorithmCount = LittleEndianConverter.ToUInt16(buffer, offset + 8);
            Flags = LittleEndianConverter.ToUInt32(buffer, offset + 10);
            int dataOffset = offset + 14;
            for (int i = 0; i < CompressionAlgorithmCount; i++)
            {
                CompressionAlgorithmIds.Add((CompressionAlgorithm)LittleEndianConverter.ToUInt16(buffer, dataOffset + i * 2));
            }
        }

        public override int DataLength
        {
            get
            {
                // 2字节算法数量 + 4字节Flags + 每个算法2字节
                return 2 + 4 + CompressionAlgorithmIds.Count * 2;
            }
        }

        public override void WriteData()
        {
            // 保证 CompressionAlgorithmCount 与实际一致
            CompressionAlgorithmCount = (ushort)CompressionAlgorithmIds.Count;
            Data = new byte[DataLength];
            LittleEndianWriter.WriteUInt16(Data, 0, CompressionAlgorithmCount);
            LittleEndianWriter.WriteUInt32(Data, 2, Flags);
            int offset = 6;
            for (int i = 0; i < CompressionAlgorithmIds.Count; i++)
            {
                LittleEndianWriter.WriteUInt16(Data, offset + i * 2, (ushort)CompressionAlgorithmIds[i]);
            }
        }
    }

}