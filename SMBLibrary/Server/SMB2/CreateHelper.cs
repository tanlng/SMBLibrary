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
                
                // Check if server should proactively grant a lease (even if client didn't request)
                // Only if: 1) Client didn't request a lease, 2) Session supports leasing, 3) No lease was already granted
                bool clientRequestedLease = request.CreateContexts?.Any(c => c.Name == "RqLs") ?? false;
                bool leaseAlreadyGranted = response.OplockLevel == OplockLevel.Lease;
                
                if (!clientRequestedLease && !leaseAlreadyGranted && session.SupportsLeasing)
                {
                    TryProactivelyGrantLease(response, session, fileID.Value, path, fileAccess, request.Header.SessionID, state);
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
        /// Convert ulong value to 32-byte opaque file ID for SMB2 QFid context (little-endian, first 8 bytes from value, remaining 24 bytes zero-padded).
        /// </summary>
        /// <param name="value">The ulong value to convert (will be written to the first 8 bytes)</param>
        /// <returns>32-byte opaque file ID byte array (little-endian, first 8 bytes from input, last 24 bytes zero)</returns>
        public static byte[] ConvertUlongTo32ByteOpaqueFileId(ulong value)
        {
            byte[] opaqueFileId = new byte[32]; // Initialize 32-byte array (default zeros)

            // Get little-endian bytes (SMB2 requires little-endian)
            byte[] valueBytes = BitConverter.IsLittleEndian ?
                BitConverter.GetBytes(value) : // Little-endian system
                BitConverter.GetBytes(value).Reverse().ToArray(); // Big-endian system, reverse bytes

            // Write first 8 bytes
            valueBytes.CopyTo(opaqueFileId, 0);

            // Remaining 24 bytes are already zero from array initialization

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
        /// Add QFid (Query File ID) context to Create response.
        /// Returns the file's unique identifier for client caching and tracking.
        /// Note: QFid is independent of Oplock/Lease mechanisms and does not affect OplockLevel.
        /// </summary>
        /// <param name="fileID">File ID containing Persistent and Volatile parts</param>
        /// <param name="response">Create response to add the context to</param>
        public static void AddQFidContext(FileID fileID, CreateResponse response)
        {
            // Convert FileID.Persistent to 32-byte opaque file ID for QFid context
            byte[] opaqueFileId = ConvertUlongTo32ByteOpaqueFileId(fileID.Persistent);
            var qfidContext = new CreateContext
            {
                Name = "QFid",
                Data = opaqueFileId,
                Next = 0
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
                // Parse lease request from CreateContext.Data
                LeaseContext leaseRequest = ParseLeaseContextFromData(requestContext);
                
                if (leaseRequest == null)
                {
                    state.LogToServer(Severity.Warning, "Failed to parse RqLs context data");
                    return;
                }
                
                state.LogToServer(Severity.Debug, 
                    "Lease request: Key={0}, State={1}, Flags={2}", 
                    leaseRequest.LeaseKey, leaseRequest.LeaseState, leaseRequest.LeaseFlags);
                
                // Process lease request through LeaseContextHandler
                var leaseResponse = session.LeaseContextHandler.ProcessCreateContext(
                    leaseRequest,
                    sessionID,
                    fileID,
                    path);
                    
                if (leaseResponse != null)
                {
                    response.CreateContexts.Add(leaseResponse);
                    response.OplockLevel = OplockLevel.Lease;
                    
                    state.LogToServer(Severity.Information, 
                        "Lease granted: File='{0}', Key={1}, State={2}", 
                        path, leaseResponse.LeaseKey, leaseResponse.LeaseState);
                }
                else
                {
                    state.LogToServer(Severity.Debug, "Lease not granted for file: {0}", path);
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
        
        /// <summary>
        /// Try to proactively grant a lease (when client didn't request one)
        /// </summary>
        private static void TryProactivelyGrantLease(
            CreateResponse response,
            SMB2Session session,
            FileID fileID,
            string path,
            FileAccess fileAccess,
            ulong sessionID,
            SMB2ConnectionState state)
        {
            try
            {
                // Evaluate if proactive lease grant is appropriate
                LeaseState grantedState = EvaluateProactiveLeaseGrant(fileAccess, path);
                
                if (grantedState == LeaseState.None)
                {
                    // No lease to grant
                    return;
                }
                
                // Generate a new lease key (server-generated)
                Guid leaseKey = Guid.NewGuid();
                
                state.LogToServer(Severity.Debug, 
                    "Proactively granting lease: File='{0}', Key={1}, State={2}", 
                    path, leaseKey, grantedState);
                
                // Create lease request for LeaseManager
                var leaseRequest = new LeaseContext(
                    leaseKey,
                    grantedState,
                    LeaseFlags.None,
                    0 // LeaseDuration is typically 0 for SMB2
                );
                
                // Process through LeaseContextHandler
                var leaseResponse = session.LeaseContextHandler.ProcessCreateContext(
                    leaseRequest,
                    sessionID,
                    fileID,
                    path);
                
                if (leaseResponse != null)
                {
                    response.CreateContexts.Add(leaseResponse);
                    response.OplockLevel = OplockLevel.Lease;
                    
                    state.LogToServer(Severity.Information, 
                        "Proactive lease granted: File='{0}', Key={1}, State={2}", 
                        path, leaseResponse.LeaseKey, leaseResponse.LeaseState);
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail the Create operation
                state.LogToServer(Severity.Debug, 
                    "Failed to proactively grant lease for '{0}': {1}", path, ex.Message);
            }
        }
        
        /// <summary>
        /// Evaluate whether to proactively grant a lease and what lease state to grant
        /// </summary>
        private static LeaseState EvaluateProactiveLeaseGrant(FileAccess fileAccess, string path)
        {
            // Simple heuristic: Grant read lease for read-only access
            // Grant read+handle lease for read-write access
            // This is a conservative approach - can be made more sophisticated
            
            bool hasRead = (fileAccess & FileAccess.Read) != 0;
            bool hasWrite = (fileAccess & FileAccess.Write) != 0;
            
            if (hasRead && hasWrite)
            {
                // Read-write access: grant RH (read + handle caching)
                // We don't grant write caching proactively as it's more aggressive
                return LeaseState.ReadCaching | LeaseState.HandleCaching;
            }
            else if (hasRead)
            {
                // Read-only access: grant R (read caching)
                return LeaseState.ReadCaching;
            }
            else
            {
                // No read/write access (e.g., delete, attributes only): no lease
                return LeaseState.None;
            }
        }
        
        /// <summary>
        /// Parse LeaseContext from CreateContext.Data
        /// </summary>
        private static LeaseContext ParseLeaseContextFromData(CreateContext context)
        {
            if (context == null || context.Data == null || context.Data.Length < 32)
            {
                return null;
            }
            
            try
            {
                // LeaseContext expects Data to contain the 32-byte lease structure
                // Create a temporary LeaseContext and let it parse the data
                var leaseContext = new LeaseContext();
                leaseContext.Name = context.Name;
                leaseContext.Data = context.Data;
                
                // Parse the lease data (LeaseContext will parse from Data field)
                // Use reflection or create a new instance with proper parsing
                if (context.Data.Length >= 32)
                {
                    int offset = 0;
                    leaseContext.LeaseKey = LittleEndianConverter.ToGuid(context.Data, offset);
                    offset += 16;
                    leaseContext.LeaseState = (LeaseState)LittleEndianConverter.ToUInt32(context.Data, offset);
                    offset += 4;
                    leaseContext.LeaseFlags = (LeaseFlags)LittleEndianConverter.ToUInt32(context.Data, offset);
                    offset += 4;
                    leaseContext.LeaseDuration = LittleEndianConverter.ToUInt64(context.Data, offset);
                }
                
                return leaseContext;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
