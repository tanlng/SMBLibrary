/* Copyright (C) 2017 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using SMBLibrary.SMB2;
using Utilities;

namespace SMBLibrary.Server.SMB2
{
    internal class CreateHelper
    {
        internal static SMB2Command GetCreateResponse(CreateRequest request, ISMBShare share, SMB2ConnectionState state)
        {
            SMB2Session session = state.GetSession(request.Header.SessionID);
            string path = request.Name;
            if (!path.StartsWith(@"\"))
            {
                path = @"\" + path;
            }

            FileAccess createAccess = NTFileStoreHelper.ToCreateFileAccess(request.DesiredAccess, request.CreateDisposition);
            if (share is FileSystemShare)
            {
                if (!((FileSystemShare)share).HasAccess(session.SecurityContext, path, createAccess))
                {
                    state.LogToServer(Severity.Verbose, "Create: Opening '{0}{1}' failed. User '{2}' was denied access.", share.Name, path, session.UserName);
                    return new ErrorResponse(request.CommandName, NTStatus.STATUS_ACCESS_DENIED);
                }
            }

            object handle;
            FileStatus fileStatus;
            // GetFileInformation/FileNetworkOpenInformation requires FILE_READ_ATTRIBUTES
            AccessMask desiredAccess = request.DesiredAccess | (AccessMask)FileAccessMask.FILE_READ_ATTRIBUTES;
            NTStatus createStatus = share.FileStore.CreateFile(out handle, out fileStatus, path, desiredAccess, request.FileAttributes, request.ShareAccess, request.CreateDisposition, request.CreateOptions, session.SecurityContext);
            if (createStatus != NTStatus.STATUS_SUCCESS)
            {
                state.LogToServer(Severity.Verbose, "Create: Opening '{0}{1}' failed. NTStatus: {2}.", share.Name, path, createStatus);
                return new ErrorResponse(request.CommandName, createStatus);
            }

            FileAccess fileAccess = NTFileStoreHelper.ToFileAccess(desiredAccess);
            FileID? fileID = session.AddOpenFile(request.Header.TreeID, share.Name, path, handle, fileAccess);
            if (fileID == null)
            {
                share.FileStore.CloseFile(handle);
                state.LogToServer(Severity.Verbose, "Create: Opening '{0}{1}' failed. Too many open files.", share.Name, path);
                return new ErrorResponse(request.CommandName, NTStatus.STATUS_TOO_MANY_OPENED_FILES);
            }

            string fileAccessString = fileAccess.ToString().Replace(", ", "|");
            string shareAccessString = request.ShareAccess.ToString().Replace(", ", "|");
            state.LogToServer(Severity.Verbose, "Create: Opened '{0}{1}', FileAccess: {2}, ShareAccess: {3}. (SessionID: {4}, TreeID: {5}, FileId: {6})", share.Name, path, fileAccessString, shareAccessString, request.Header.SessionID, request.Header.TreeID, fileID.Value.Volatile);
            if (share is NamedPipeShare)
            {
                return CreateResponseForNamedPipe(fileID.Value, FileStatus.FILE_OPENED);
            }
            else
            {
                FileNetworkOpenInformation fileInfo = NTFileStoreHelper.GetNetworkOpenInformation(share.FileStore, handle);
                CreateResponse response = CreateResponseFromFileSystemEntry(fileInfo, fileID.Value, fileStatus);
                var extraInfosKeys = request.CreateContexts.Select(c => c.Name).ToArray();
                if (extraInfosKeys.Any(k => k == "QFid"))
                {
                    AddQFidContext(fileID.Value, response);
                }
                return response;
            }
        }

        private static CreateResponse CreateResponseForNamedPipe(FileID fileID, FileStatus fileStatus)
        {
            CreateResponse response = new CreateResponse();
            response.CreateAction = (CreateAction)fileStatus;
            response.FileAttributes = FileAttributes.Normal;
            response.FileId = fileID;
            return response;
        }
        /// <summary>
        /// 将ulong值转换为符合SMB2 QFid上下文要求的32字节不透明文件ID（小端字节序，前8字节为ulong值，剩余24字节补零）
        /// </summary>
        /// <param name="value">需要转换的ulong值（将作为文件ID的前8字节）</param>
        /// <returns>32字节的Opaque File ID字节数组（小端序，前8字节为输入值，后24字节补零）</returns>
        public static byte[] ConvertUlongTo32ByteOpaqueFileId(ulong value)
        {
            byte[] opaqueFileId = new byte[32]; // 初始化32字节数组（默认值为0，无需手动清零剩余字节）

            // 获取ulong的小端字节（BitConverter在小端系统返回小端序，大端系统返回大端序，需根据SMB2规范调整）
            // SMB2使用小端序，因此无论系统如何，强制转换为小端字节
            byte[] valueBytes = BitConverter.IsLittleEndian ?
                BitConverter.GetBytes(value) : // 小端系统直接获取小端字节
                BitConverter.GetBytes(value).Reverse().ToArray(); // 大端系统反转字节

            // 写入前8字节（确保只写入有效长度，处理ulong的8字节）
            valueBytes.CopyTo(opaqueFileId, 0); // 等效于Array.Copy(valueBytes, opaqueFileId, 8)

            // 无需显式填充剩余24字节，数组初始化时已为0

            return opaqueFileId;
        }
        private static CreateResponse CreateResponseFromFileSystemEntry(FileNetworkOpenInformation fileInfo, FileID fileID, FileStatus fileStatus)
        {
            CreateResponse response = new CreateResponse();
            response.CreateAction = (CreateAction)fileStatus;
            response.CreationTime = fileInfo.CreationTime;
            response.LastWriteTime = fileInfo.LastWriteTime;
            response.ChangeTime = fileInfo.LastWriteTime;
            response.LastAccessTime = fileInfo.LastAccessTime;
            response.AllocationSize = fileInfo.AllocationSize;
            response.EndofFile = fileInfo.EndOfFile;
            response.FileAttributes = fileInfo.FileAttributes;
            response.FileId = fileID;
            return response;
        }

        public static void AddMxAcContext(CreateResponse response)
        {
            // 添加最大访问权限上下文（MxAc）
            uint queryStatus = 0x00000000; // STATUS_SUCCESS
            AccessMask accessMask = (AccessMask)0x001f01ff; // 按报文设置访问掩码

            byte[] mxAcData = new byte[8];
            LittleEndianWriter.WriteUInt32(mxAcData, 0, queryStatus);
            LittleEndianWriter.WriteUInt32(mxAcData, 4, (uint)accessMask);

            var mxAcContext = new CreateContext
            {
                Name = "MxAc",                // 4字节标签
                Data = mxAcData,              // 8字节数据（状态+掩码）
                Next = 0                      // 假设后续处理链式结构时自动设置，或由WriteCreateContextList处理
            };
            response.CreateContexts.Add(mxAcContext);
        }

        public static void AddQFidContext(FileID fileID, CreateResponse response)
        {
            // 添加磁盘文件ID上下文（QFid）
            byte[] opaqueFileId = ConvertUlongTo32ByteOpaqueFileId(fileID.Persistent); // 获取32字节文件ID（需确保长度正确）
            var qfidContext = new CreateContext
            {
                Name = "QFid",                // 4字节标签
                Data = opaqueFileId,          // 32字节数据
                Next = 0                      // 最后一个元素时Next=0，或由链式处理设置
            };
            response.CreateContexts.Add(qfidContext);
        }
    }
}
