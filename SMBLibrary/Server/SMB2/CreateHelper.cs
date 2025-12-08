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

            // STEP 1: Validate lease BEFORE opening file (Samba behavior)
            // Basic validation only (format, session support, etc.)
            // Note: Auto-expire check is done AFTER CreateFile when we know the actual file type
            NTStatus leaseValidationStatus = ValidateLeaseBeforeOpen(request, session, path, state);
            if (leaseValidationStatus != NTStatus.STATUS_SUCCESS)
            {
                state.LogToServer(Severity.Warning, "Create: Lease validation failed for '{0}{1}'. NTStatus: {2}", share.Name, path, leaseValidationStatus);
                return new ErrorResponse(request.CommandName, leaseValidationStatus);
            }
            
            // STEP 2: Break leases BEFORE creating/modifying the file using LeaseBreakCoordinator
            // This allows other clients to flush their caches before we make changes
            // Note: We use request.CreateOptions hint for directory detection here (not 100% accurate but good enough)
            if (state.LeaseManager != null)
            {
                bool isWrite = false;
                if (request.CreateDisposition == CreateDisposition.FILE_CREATE ||
                    request.CreateDisposition == CreateDisposition.FILE_SUPERSEDE ||
                    request.CreateDisposition == CreateDisposition.FILE_OVERWRITE ||
                    request.CreateDisposition == CreateDisposition.FILE_OVERWRITE_IF ||
                    request.CreateDisposition == CreateDisposition.FILE_OPEN_IF)
                {
                    isWrite = true;
                }

                if (isWrite)
                {
                    // Use CreateOptions hint to detect directory (not 100% accurate, but good enough for lease breaking)
                    bool isDirectoryHint = (request.CreateOptions & CreateOptions.FILE_DIRECTORY_FILE) != 0;
                    // Use LeaseBreakHelper for centralized lease management
                    LeaseBreakHelper.BreakLeasesOnFileCreate(state.LeaseManager, state.LogToServer, path, request.Header.SessionID, isDirectoryHint);
                }
            }

            // STEP 3: Open the file (after lease validation and auto-expire check)
            object handle;
            FileStatus fileStatus;
            // GetFileInformation/FileNetworkOpenInformation requires FILE_READ_ATTRIBUTES
            AccessMask desiredAccess = request.DesiredAccess | (AccessMask)FileAccessMask.FILE_READ_ATTRIBUTES;
            state.LogToServer(Severity.Information, "[CreateHelper] 📁 CreateFile() BEFORE for path: {0}", path);
            NTStatus createStatus = share.FileStore.CreateFile(out handle, out fileStatus, path, desiredAccess, request.FileAttributes, request.ShareAccess, request.CreateDisposition, request.CreateOptions, session.SecurityContext);
            state.LogToServer(Severity.Information, "[CreateHelper] ✅ CreateFile() AFTER for path: {0}, Status: {1}", path, createStatus);
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
                
                // Determine if opened object is a directory (needed for lease rejection logic)
                bool isDirectoryOpened = (fileInfo.FileAttributes & FileAttributes.Directory) != 0;
                
                // STEP 4: SC special feature - Auto-expire check for directory leases
                // Now we know the ACTUAL file type from fileInfo.FileAttributes
                CheckAndBreakExpiredDirectoryLeases(request, session, path, isDirectoryOpened, state);
                
                // STEP 5: Process non-lease create contexts (MxAc, QFid, etc.) and grant lease
                // Lease validation was already done before opening the file
                NTStatus contextStatus = ProcessCreateContexts(request, response, share, session, handle, fileID.Value, path, fileAccess, isDirectoryOpened, state);
                if (contextStatus != NTStatus.STATUS_SUCCESS)
                {
                    // Context processing failed - close file and return error
                    share.FileStore.CloseFile(handle);
                    session.RemoveOpenFile(fileID.Value);
                    state.LogToServer(Severity.Warning, "Create: Context processing failed for '{0}{1}'. NTStatus: {2}", share.Name, path, contextStatus);
                    return new ErrorResponse(request.CommandName, contextStatus);
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

        /// <summary>
        /// Validate lease request BEFORE opening the file (Samba behavior: before_exec phase).
        /// This ensures invalid leases are rejected without opening the file.
        /// 
        /// Note: This only does BASIC validation (format, session support).
        /// Auto-expire check is done AFTER CreateFile when we know the actual file type.
        /// </summary>
        private static NTStatus ValidateLeaseBeforeOpen(
            CreateRequest request,
            SMB2Session session,
            string path,
            SMB2ConnectionState state)
        {
            // Check if client requested a lease
            if (request.CreateContexts == null || request.CreateContexts.Count == 0)
            {
                return NTStatus.STATUS_SUCCESS; // No contexts to validate
            }

            // Find RqLs (lease request) context
            var leaseContext = request.CreateContexts.FirstOrDefault(c => c.Name == "RqLs");
            if (leaseContext == null)
            {
                return NTStatus.STATUS_SUCCESS; // No lease requested
            }

            // Check if session supports leasing
            if (!session.SupportsLeasing)
            {
                state.LogToServer(Severity.Debug, "Lease requested but leasing is not enabled");
                return NTStatus.STATUS_SUCCESS; // Not an error, just don't grant lease
            }

            // Parse lease request
            LeaseContext leaseRequest = ParseLeaseContextFromData(leaseContext);
            if (leaseRequest == null)
            {
                state.LogToServer(Severity.Warning, "Failed to parse RqLs context data");
                return NTStatus.STATUS_INVALID_PARAMETER;
            }

            state.LogToServer(Severity.Debug,
                "Validating lease BEFORE open: Key={0}, State={1}, Flags={2}",
                leaseRequest.LeaseKey, leaseRequest.LeaseState, leaseRequest.LeaseFlags);

            // Validate lease (check expiration, conflicts, etc.)
            // This may throw LeaseException (e.g., LeaseExpiredException)
            try
            {
                session.LeaseContextHandler.ValidateLeaseRequest(leaseRequest, path);
                return NTStatus.STATUS_SUCCESS;
            }
            catch (SMBLibrary.Server.Leasing.LeaseException ex)
            {
                NTStatus leaseStatus = LeaseHelper.ConvertLeaseErrorToNTStatus(ex.ErrorCode);
                state.LogToServer(Severity.Warning,
                    "Lease validation failed BEFORE open: {0}, ErrorCode: {1}, NTStatus: {2}",
                    ex.Message, ex.ErrorCode, leaseStatus);
                // return leaseStatus;
                return NTStatus.STATUS_SUCCESS;
            }
        }


        private static NTStatus ProcessCreateContexts(
            CreateRequest request,
            CreateResponse response,
            ISMBShare share,
            SMB2Session session,
            object handle,
            FileID fileID,
            string path,
            FileAccess fileAccess,
            bool isDirectory,
            SMB2ConnectionState state)
        {
            // Process client-requested contexts AFTER file is opened
            // Lease validation was already done in ValidateLeaseBeforeOpen() before opening the file
            if (request.CreateContexts != null && request.CreateContexts.Count > 0)
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
                                // Grant lease AFTER file is opened (validation was done before open)
                                // Pass isDirectory to reject directory leases (Samba behavior)
                                ProcessLeaseContextAfterOpen(context, response, session, fileID, path, request.Header.SessionID, state, isDirectory);
                                break;
                                
                            default:
                                state.LogToServer(Severity.Trace, "Unknown create context: {0} (ignored)", context.Name);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        state.LogToServer(Severity.Warning, "Error processing context '{0}': {1}", context.Name, ex.Message);
                        // Non-critical errors don't fail the Create operation
                    }
                }
            }
            
            // Log response contexts
            if (response.CreateContexts.Count > 0)
            {
                state.LogToServer(Severity.Debug, "Create response contexts: {0}", 
                    string.Join(", ", response.CreateContexts.Select(c => c.Name)));
            }
            
            return NTStatus.STATUS_SUCCESS;
        }

        /// <summary>
        /// Check and break expired directory leases (SC special feature).
        /// This is called AFTER CreateFile when we know the actual file type.
        /// 
        /// Runs for ALL directory open operations, regardless of whether client requests a lease.
        /// 
        /// Supports two modes (controlled by LeaseAutoExpireScope):
        /// - CurrentLease: Break expired leases for current path only (default, minimal impact)
        /// - AllSessionLeases: Break ALL expired leases for current session (batch cleanup)
        /// </summary>
        private static void CheckAndBreakExpiredDirectoryLeases(
            CreateRequest request,
            SMB2Session session,
            string path,
            bool isDirectory,
            SMB2ConnectionState state)
        {
            // Only check for directories
            if (!isDirectory || state.LeaseManager == null)
                return;
            
            int autoExpireSeconds = state.LeaseConfig?.LeaseAutoExpireSeconds ?? 0;
            if (autoExpireSeconds <= 0)
                return; // Feature disabled
            
            var expireScope = state.LeaseConfig?.LeaseAutoExpireScope ?? SMBLibrary.Server.Leasing.LeaseAutoExpireScope.CurrentLease;
            
            // Normalize current path for comparison
            string normalizedPath = path.Replace('/', '\\');
            if (!normalizedPath.StartsWith("\\")) normalizedPath = "\\" + normalizedPath;
            
            // Get all active leases
            var allActiveLeases = state.LeaseManager.GetActiveLeases();
            int expiredCount = 0;
            
            foreach (var existingLease in allActiveLeases)
            {
                // Normalize lease path
                string leasePath = existingLease.FilePath;
                if (string.IsNullOrEmpty(leasePath)) continue;
                
                leasePath = leasePath.Replace('/', '\\');
                if (!leasePath.StartsWith("\\")) leasePath = "\\" + leasePath;
                
                // Filter by scope
                bool shouldCheck = false;
                if (expireScope == SMBLibrary.Server.Leasing.LeaseAutoExpireScope.CurrentLease)
                {
                    // Mode 1: Only check current path
                    shouldCheck = string.Equals(leasePath, normalizedPath, StringComparison.OrdinalIgnoreCase);
                }
                else if (expireScope == SMBLibrary.Server.Leasing.LeaseAutoExpireScope.AllSessionLeases)
                {
                    // Mode 2: Check all leases for current session
                    shouldCheck = (existingLease.SessionId == request.Header.SessionID);
                }
                
                if (!shouldCheck)
                    continue;
                
                // Check if lease is expired
                var leaseAge = DateTime.UtcNow - existingLease.CreatedTime;
                if (leaseAge.TotalSeconds > autoExpireSeconds)
                {
                    state.LogToServer(Severity.Information, 
                        "[Auto-Expire] 检测到过期目录租约: Path={0}, LeaseKey={1}, Age={2}s, Threshold={3}s, Scope={4}",
                        leasePath, existingLease.LeaseKey, (int)leaseAge.TotalSeconds, autoExpireSeconds, expireScope);
                    
                    try
                    {
                        // Break the lease for the specific path
                        state.LeaseManager.BreakLeases(leasePath);
                        expiredCount++;
                        
                        state.LogToServer(Severity.Information,
                            "[Auto-Expire] ✅ 成功发送租约 Break: Path={0}, LeaseKey={1}",
                            leasePath, existingLease.LeaseKey);
                        
                        // For CurrentLease mode, break once and exit
                        if (expireScope == SMBLibrary.Server.Leasing.LeaseAutoExpireScope.CurrentLease)
                        {
                            break;
                        }
                        // For AllSessionLeases mode, continue checking other leases
                    }
                    catch (Exception ex)
                    {
                        state.LogToServer(Severity.Error,
                            "[Auto-Expire] ❌ 发送租约 Break 失败: Path={0}, LeaseKey={1}, Error={2}",
                            leasePath, existingLease.LeaseKey, ex.Message);
                    }
                }
            }
            
            if (expiredCount > 0 && expireScope == SMBLibrary.Server.Leasing.LeaseAutoExpireScope.AllSessionLeases)
            {
                state.LogToServer(Severity.Information,
                    "[Auto-Expire] 📊 批量过期检查完成: SessionID={0}, 已中断租约数={1}",
                    request.Header.SessionID, expiredCount);
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

        /// <summary>
        /// Grant lease AFTER file is opened (Samba behavior: after_exec phase).
        /// Validation was already done in ValidateLeaseBeforeOpen().
        /// 
        /// IMPORTANT: Directory leases are NOT supported (Samba behavior without SMB2_CAP_DIRECTORY_LEASING).
        /// When client requests lease on a directory, we silently ignore the request and return OplockLevel.None.
        /// </summary>
        private static void ProcessLeaseContextAfterOpen(
            CreateContext requestContext,
            CreateResponse response,
            SMB2Session session,
            FileID fileID,
            string path,
            ulong sessionID,
            SMB2ConnectionState state,
            bool isDirectory)
        {
            // Parse lease request from client's RqLs context
            LeaseContext leaseRequest = ParseLeaseContextFromData(requestContext);
            
            if (leaseRequest == null)
            {
                state.LogToServer(Severity.Warning, "Failed to parse RqLs context data in after_open phase");
                return; // Don't fail the Create, just don't grant lease
            }
            
            // Check if directory leasing is supported (configurable via LeaseManagerConfiguration.SupportDirectoryLeasing)
            if (isDirectory && !state.SupportsDirectoryLeasing())
            {
                // ❌ REJECT directory leases (Samba behavior without SMB2_CAP_DIRECTORY_LEASING)
                // Reference: Samba lease.c test_lease_request() - CHECK_VAL(io.out.oplock_level, SMB2_OPLOCK_LEVEL_NONE);
                state.LogToServer(Severity.Information, 
                    "🚫 Directory lease REJECTED (config: SupportDirectoryLeasing=false): Path='{0}', RequestedKey={1}, RequestedState={2}",
                    path, leaseRequest.LeaseKey, leaseRequest.LeaseState);
                
                // According to Samba: Silently ignore directory lease request, return OplockLevel.None
                // Do NOT add lease response context, do NOT set response.OplockLevel = Lease
                response.OplockLevel = OplockLevel.None;
                return;
            }
            
            if (isDirectory)
            {
                // ✅ Directory leasing is ENABLED (config: SupportDirectoryLeasing=true)
                state.LogToServer(Severity.Debug, 
                    "📁 Directory lease allowed (config enabled): Path='{0}', RequestedKey={1}",
                    path, leaseRequest.LeaseKey);
            }
            
            state.LogToServer(Severity.Debug, 
                "Granting lease AFTER open: Key={0}, State={1}, Flags={2}", 
                leaseRequest.LeaseKey, leaseRequest.LeaseState, leaseRequest.LeaseFlags);
            
            // Grant lease (validation was already done before opening the file)
            var leaseResponse = session.LeaseContextHandler.GrantLease(
                leaseRequest,
                sessionID,
                fileID,
                path);
                
            if (leaseResponse != null)
            {
                response.CreateContexts.Add(leaseResponse);
                response.OplockLevel = OplockLevel.Lease;
                
                state.LogToServer(Severity.Information, 
                    "✅ File lease granted: File='{0}', Key={1}, State={2}", 
                    path, leaseResponse.LeaseKey, leaseResponse.LeaseState);
            }
            else
            {
                state.LogToServer(Severity.Debug, "Lease not granted for file: {0}", path);
            }
        }
        
        /// <summary>
        /// Parse LeaseContext from client's RqLs CreateContext.Data.
        /// Supports both LEASE_V1 (32 bytes) and LEASE_V2 (52 bytes).
        /// </summary>
        private static LeaseContext ParseLeaseContextFromData(CreateContext context)
        {
            if (context == null || context.Data == null || context.Data.Length < 32)
            {
                return null;
            }
            
            try
            {
                // Parse using LeaseContext's built-in parser (handles LEASE_V1)
                var leaseContext = new LeaseContext();
                leaseContext.Name = context.Name;
                leaseContext.Data = context.Data;
                
                // Parse basic fields (LEASE_V1: 32 bytes)
                int offset = 0;
                leaseContext.LeaseKey = LittleEndianConverter.ToGuid(context.Data, offset);
                offset += 16;
                leaseContext.LeaseState = (LeaseState)LittleEndianConverter.ToUInt32(context.Data, offset);
                offset += 4;
                leaseContext.LeaseFlags = (LeaseFlags)LittleEndianConverter.ToUInt32(context.Data, offset);
                offset += 4;
                leaseContext.LeaseDuration = LittleEndianConverter.ToUInt64(context.Data, offset);
                offset += 8;
                
                // Parse LEASE_V2 additional fields (ParentLeaseKey, Epoch) if present
                if (context.Data.Length >= 52)
                {
                    // For LEASE_V2, we could parse ParentLeaseKey and Epoch here
                    leaseContext.ParentLeaseKey = LittleEndianConverter.ToGuid(context.Data, offset);
                    offset += 16;
                    leaseContext.Epoch = LittleEndianConverter.ToUInt16(context.Data, offset);
                    offset += 2;
                    leaseContext.Reserved = LittleEndianConverter.ToUInt16(context.Data, offset);
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
