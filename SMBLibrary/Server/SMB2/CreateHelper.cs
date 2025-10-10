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
using Newtonsoft.Json;
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
            FileID? fileID = session.AddOpenFile(request.Header.TreeID, share, path, handle, fileAccess);
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
                
                // Process Create Contexts
                if (request.CreateContexts != null && request.CreateContexts.Count > 0)
                {
                    ProcessCreateContexts(request, response, share, session, handle, fileID.Value, path, state);
                }
                return response;
            }
        }

        // 从 request.CreateContexts 提取信息或生成租赁密钥
        private static uint GenerateLeaseKey(CreateRequest request)
        {
            // 从 CreateContexts 中提取 "LeaseKey" (示例)
            var leaseKeyContext = request.CreateContexts.FirstOrDefault(c => c.Name == "LeaseKey");
            if (leaseKeyContext != null)
            {
                return BitConverter.ToUInt32(leaseKeyContext.Data, 0);
            }

            // 如果未找到，生成默认值
            return (uint)new Random().Next(0, int.MaxValue);
        }

        // 动态生成租赁状态
        private static ulong GenerateLeaseState(CreateRequest request)
        {
            // 示例：根据请求路径生成状态的哈希值
            return (ulong)request.Name.GetHashCode();
        }

        // 动态生成租赁序列号
        private static uint GenerateLeaseSequenceNumber(CreateRequest request)
        {
            // 示例：使用随机数生成
            return (uint)new Random().Next(1, 1000);
        }

        // 动态生成持久句柄 ID
        private static uint GenerateDurableHandleId(CreateRequest request)
        {
            // 示例：根据租赁密钥生成
            return (uint)GenerateLeaseKey(request) ^ 0x87654321; // XOR 操作
        }

        // 动态生成持久 GUID
        private static Guid GenerateDurableGuid(CreateRequest request)
        {
            // 示例：动态生成新 GUID
            return Guid.NewGuid();
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

        /// <summary>
        ///  猜测这个和Oplock机制有关
        /// </summary>
        /// <param name="fileID"></param>
        /// <param name="response"></param>
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


        private static void ProcessCreateContexts(
            CreateRequest request,
            CreateResponse response,
            ISMBShare share,
            SMB2Session session,
            object handle,
            FileID fileID,
            string path,
            SMB2ConnectionState state)
        {
            foreach (var context in request.CreateContexts)
            {
                try
                {
                    switch (context.Name)
                    {
                        case "MxAc":
                            ProcessMxAcContext(response, share, session, handle, path, state);
                            break;
                            
                        case "QFid":
                            ProcessQFidContext(response, fileID, state);
                            break;
                            
                        case "RqLs":
                            ProcessLeaseContext(context, response, session, fileID, path, request.Header.SessionID, state);
                            break;
                            
                        default:
                            state.LogToServer(Severity.Trace, "Unknown create context: {0} (ignored)", context.Name);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    state.LogToServer(Severity.Warning, "Error processing context '{0}': {1}", context.Name, ex.Message);
                }
            }
            
            if (response.CreateContexts.Count > 0)
            {
                state.LogToServer(Severity.Debug, "Create response contexts: {0}", 
                    string.Join(", ", response.CreateContexts.Select(c => c.Name)));
            }
        }

        private static void ProcessMxAcContext(
            CreateResponse response,
            ISMBShare share,
            SMB2Session session,
            object handle,
            string path,
            SMB2ConnectionState state)
        {
            AccessMask maximalAccess = 0;
            NTStatus status = NTStatus.STATUS_SUCCESS;
            
            // Calculate maximal access based on share-level permissions
            // Users define permissions via FileSystemShare.AccessRequested event
            if (share is FileSystemShare fileSystemShare)
            {
                bool hasReadAccess = fileSystemShare.HasReadAccess(session.SecurityContext, path);
                bool hasWriteAccess = fileSystemShare.HasWriteAccess(session.SecurityContext, path);
                
                maximalAccess = StandardAccessMasks.Build(
                    canRead: hasReadAccess,
                    canWrite: hasWriteAccess,
                    canDelete: hasWriteAccess,
                    canExecute: hasReadAccess);
                
                state.LogToServer(Severity.Debug, 
                    "MxAc: User '{0}', Path '{1}', Read: {2}, Write: {3}, Access: 0x{4:X8}",
                    session.UserName, path, hasReadAccess, hasWriteAccess, (uint)maximalAccess);
            }
            else
            {
                // For non-FileSystemShare types, return full control
                // Users should use FileSystemShare with AccessRequested event for custom permissions
                maximalAccess = StandardAccessMasks.FullControl;
                
                state.LogToServer(Severity.Debug, 
                    "MxAc for {0}: Returning FullControl (0x{1:X8})",
                    share.GetType().Name, (uint)maximalAccess);
            }
            
            // Create MxAc response (8 bytes: 4 bytes status + 4 bytes access mask)
            byte[] mxAcData = new byte[8];
            LittleEndianWriter.WriteUInt32(mxAcData, 0, (uint)status);
            LittleEndianWriter.WriteUInt32(mxAcData, 4, (uint)maximalAccess);
            
            response.CreateContexts.Add(new CreateContext
            {
                Name = "MxAc",
                Data = mxAcData
            });
        }

        private static void ProcessQFidContext(
            CreateResponse response,
            FileID fileID,
            SMB2ConnectionState state)
        {
            AddQFidContext(fileID, response);
            state.LogToServer(Severity.Debug, "QFid context added");
        }

        private static void ProcessLeaseContext(
            CreateContext requestContext,
            CreateResponse response,
            SMB2Session session,
            FileID fileID,
            string path,
            ulong sessionID,
            SMB2ConnectionState state)
        {
            // Check if session supports leasing
            if (!session.SupportsLeasing)
            {
                state.LogToServer(Severity.Debug, "Lease requested but leasing is not enabled");
                return;
            }
            
            try
            {
                var leaseContext = session.LeaseContextHandler.ProcessCreateContext(
                    requestContext,
                    sessionID,
                    fileID,
                    path);
                    
                if (leaseContext != null)
                {
                    response.CreateContexts.Add(leaseContext);
                    response.OplockLevel = OplockLevel.Lease;
                    
                    state.LogToServer(Severity.Information, 
                        "Lease granted for file: {0}, LeaseKey: {1}", 
                        path, ((SMBLibrary.SMB2.LeaseContext)leaseContext).LeaseKey);
                }
            }
            catch (SMBLibrary.Server.Leasing.LeaseException ex)
            {
                state.LogToServer(Severity.Warning, "Lease request failed: {0}, ErrorCode: {1}", 
                    ex.Message, ex.ErrorCode);
            }
            catch (Exception ex)
            {
                state.LogToServer(Severity.Error, "Unexpected error processing lease context: {0}", ex.Message);
            }
        }
    }
}
