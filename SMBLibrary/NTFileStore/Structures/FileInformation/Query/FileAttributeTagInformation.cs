/* Copyright (C) 2017 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Collections.Generic;
using Utilities;

namespace SMBLibrary
{
    /// <summary>
    /// [MS-FSCC] 2.4.14 - FileAttributeTagInformation
    /// </summary>
    public class FileAttributeTagInformation : FileInformation
    {
        // 固定长度为 8 字节
        public const int FixedLength = 8;

        // 文件属性
        public FileAttributes FileAttributes;
        // 重解析点标签
        public uint ReparsePointTag;

        public FileAttributeTagInformation()
        {
        }

        // 从字节数组中读取信息
        public FileAttributeTagInformation(byte[] buffer, int offset)
        {
            // 读取文件属性
            FileAttributes = (FileAttributes)LittleEndianConverter.ToUInt32(buffer, offset + 0);
            // 读取重解析点标签
            ReparsePointTag = LittleEndianConverter.ToUInt32(buffer, offset + 4);
        }

        // 将信息写入字节数组
        public override void WriteBytes(byte[] buffer, int offset)
        {
            // 写入文件属性
            LittleEndianWriter.WriteUInt32(buffer, offset + 0, (uint)FileAttributes);
            // 写入重解析点标签
            LittleEndianWriter.WriteUInt32(buffer, offset + 4, ReparsePointTag);
        }

        // 获取文件信息类
        public override FileInformationClass FileInformationClass
        {
            get
            {
                return FileInformationClass.FileAttributeTagInformation;
            }
        }

        // 获取信息长度
        public override int Length
        {
            get
            {
                return FixedLength;
            }
        }
    }
}