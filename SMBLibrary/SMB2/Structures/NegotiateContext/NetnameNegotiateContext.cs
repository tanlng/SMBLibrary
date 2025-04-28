/* Copyright (C) 2017-2024 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Xml.Linq;
using Utilities;

namespace SMBLibrary.SMB2
{
    public class NetnameNegotiateContext : NegotiateContext
    {
        public string Netname;

        public NetnameNegotiateContext(byte[] buffer, int offset) : base(buffer, offset)
        {
            Netname = ByteReader.ReadUTF16String(Data, 0, (int)DataLength / 2);
        }


        public override void WriteData()
        {
            int netnameBytes = System.Text.Encoding.Unicode.GetByteCount(Netname);
            Data = new byte[netnameBytes + 2];
            ByteWriter.WriteNullTerminatedUTF16String(Data, 0, Netname);
        }
    }
}