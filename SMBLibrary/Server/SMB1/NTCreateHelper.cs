/* Copyright (C) 2014-2017 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SMBLibrary.Services;
using SMBLibrary.SMB1;
using Utilities;

namespace SMBLibrary.Server.SMB1
{
    public class NTCreateHelper
    {
        internal static SMB1Command GetNTCreateResponse(SMB1Header header, NTCreateAndXRequest request, ISMBShare share, SMB1ConnectionState state)
        {
            bool isExtended = (request.Flags & NTCreateFlags.NT_CREATE_REQUEST_EXTENDED_RESPONSE) > 0;
            string path = request.FileName;
            if (share is NamedPipeShare)
            {
                Stream pipeStream = ((NamedPipeShare)share).OpenPipe(path);
                if (pipeStream != null)
                {
                    ushort? fileID = state.AddOpenedFile(path, pipeStream);
                    if (!fileID.HasValue)
                    {
                        header.Status = NTStatus.STATUS_TOO_MANY_OPENED_FILES;
                        return new ErrorResponse(CommandName.SMB_COM_NT_CREATE_ANDX);
                    }
                    if (isExtended)
                    {
                        return CreateResponseExtendedForNamedPipe(fileID.Value);
                    }
                    else
                    {
                        return CreateResponseForNamedPipe(fileID.Value);
                    }
                }

                header.Status = NTStatus.STATUS_OBJECT_PATH_NOT_FOUND;
                return new ErrorResponse(CommandName.SMB_COM_NT_CREATE_ANDX);
            }
            else // FileSystemShare
            {
                FileSystemShare fileSystemShare = (FileSystemShare)share;
                string userName = state.GetConnectedUserName(header.UID);
                FileSystemEntry entry;
                NTStatus createStatus = CreateFile(out entry, fileSystemShare, userName, path, request.CreateDisposition, request.CreateOptions, request.DesiredAccess, state);
                if (createStatus != NTStatus.STATUS_SUCCESS)
                {
                    header.Status = createStatus;
                    return new ErrorResponse(CommandName.SMB_COM_NT_CREATE_ANDX);
                }

                IFileSystem fileSystem = fileSystemShare.FileSystem;
                FileAccess fileAccess = ToFileAccess(request.DesiredAccess);
                FileShare fileShare = ToFileShare(request.ShareAccess);

                Stream stream;
                bool deleteOnClose = false;
                if (fileAccess == (FileAccess)0 || entry.IsDirectory)
                {
                    stream = null;
                }
                else
                {
                    // When FILE_OPEN_REPARSE_POINT is specified, the operation should continue normally if the file is not a reparse point.
                    // FILE_OPEN_REPARSE_POINT is a hint that the caller does not intend to actually read the file, with the exception
                    // of a file copy operation (where the caller will attempt to simply copy the reparse point).
                    deleteOnClose = (request.CreateOptions & CreateOptions.FILE_DELETE_ON_CLOSE) > 0;
                    bool openReparsePoint = (request.CreateOptions & CreateOptions.FILE_OPEN_REPARSE_POINT) > 0;
                    bool disableBuffering = (request.CreateOptions & CreateOptions.FILE_NO_INTERMEDIATE_BUFFERING) > 0;
                    bool buffered = (request.CreateOptions & CreateOptions.FILE_SEQUENTIAL_ONLY) > 0 && !disableBuffering && !openReparsePoint;
                    state.LogToServer(Severity.Verbose, "NTCreate: Opening '{0}', Access={1}, Share={2}, Buffered={3}", path, fileAccess, fileShare, buffered);
                    try
                    {
                        stream = fileSystem.OpenFile(path, FileMode.Open, fileAccess, fileShare);
                    }
                    catch (IOException ex)
                    {
                        ushort errorCode = IOExceptionHelper.GetWin32ErrorCode(ex);
                        if (errorCode == (ushort)Win32Error.ERROR_SHARING_VIOLATION)
                        {
                            state.LogToServer(Severity.Debug, "NTCreate: Sharing violation opening '{0}'", path);
                            header.Status = NTStatus.STATUS_SHARING_VIOLATION;
                            return new ErrorResponse(CommandName.SMB_COM_NT_CREATE_ANDX);
                        }
                        else
                        {
                            state.LogToServer(Severity.Debug, "NTCreate: Sharing violation opening '{0}', Data Error", path);
                            header.Status = NTStatus.STATUS_DATA_ERROR;
                            return new ErrorResponse(CommandName.SMB_COM_NT_CREATE_ANDX);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        state.LogToServer(Severity.Debug, "NTCreate: Sharing violation opening '{0}', Access Denied", path);
                        header.Status = NTStatus.STATUS_ACCESS_DENIED;
                        return new ErrorResponse(CommandName.SMB_COM_NT_CREATE_ANDX);
                    }
        
                    if (buffered)
                    {
                        stream = new PrefetchedStream(stream);
                    }
                }

                ushort? fileID = state.AddOpenedFile(path, stream, deleteOnClose);
                if (!fileID.HasValue)
                {
                    header.Status = NTStatus.STATUS_TOO_MANY_OPENED_FILES;
                    return new ErrorResponse(CommandName.SMB_COM_NT_CREATE_ANDX);
                }
                if (isExtended)
                {
                    NTCreateAndXResponseExtended response = CreateResponseExtendedFromFileSystemEntry(entry, fileID.Value);
                    if ((request.Flags & NTCreateFlags.NT_CREATE_REQUEST_OPBATCH) > 0)
                    {
                        response.OpLockLevel = OpLockLevel.BatchOpLockGranted;
                    }
                    return response;
                }
                else
                {
                    NTCreateAndXResponse response = CreateResponseFromFileSystemEntry(entry, fileID.Value);
                    if ((request.Flags & NTCreateFlags.NT_CREATE_REQUEST_OPBATCH) > 0)
                    {
                        response.OpLockLevel = OpLockLevel.BatchOpLockGranted;
                    }
                    return response;
                }
            }
        }

        public static NTStatus CreateFile(out FileSystemEntry entry, FileSystemShare share, string userName, string path, CreateDisposition createDisposition, CreateOptions createOptions, AccessMask desiredAccess, ConnectionState state)
        {
            bool hasWriteAccess = share.HasWriteAccess(userName);
            IFileSystem fileSystem = share.FileSystem;

            bool forceDirectory = (createOptions & CreateOptions.FILE_DIRECTORY_FILE) > 0;
            bool forceFile = (createOptions & CreateOptions.FILE_NON_DIRECTORY_FILE) > 0;

            if (forceDirectory & (createDisposition != CreateDisposition.FILE_CREATE &&
                                  createDisposition != CreateDisposition.FILE_OPEN &&
                                  createDisposition != CreateDisposition.FILE_OPEN_IF))
            {
                entry = null;
                return NTStatus.STATUS_INVALID_PARAMETER;
            }

            // Windows will try to access named streams (alternate data streams) regardless of the FILE_NAMED_STREAMS flag, we need to prevent this behaviour.
            if (path.Contains(":"))
            {
                // Windows Server 2003 will return STATUS_OBJECT_NAME_NOT_FOUND
                entry = null;
                return NTStatus.STATUS_NO_SUCH_FILE;
            }

            entry = fileSystem.GetEntry(path);
            if (createDisposition == CreateDisposition.FILE_OPEN)
            {
                if (entry == null)
                {
                    return NTStatus.STATUS_OBJECT_PATH_NOT_FOUND;
                }

                if (entry.IsDirectory && forceFile)
                {
                    return NTStatus.STATUS_FILE_IS_A_DIRECTORY;
                }

                if (!entry.IsDirectory && forceDirectory)
                {
                    // Not sure if that's the correct response
                    return NTStatus.STATUS_OBJECT_NAME_COLLISION;
                }
            }
            else if (createDisposition == CreateDisposition.FILE_CREATE)
            {
                if (entry != null)
                {
                    // File already exists, fail the request
                    state.LogToServer(Severity.Debug, "NTCreate: File '{0}' already exist", path);
                    return NTStatus.STATUS_OBJECT_NAME_COLLISION;
                }

                if (!hasWriteAccess)
                {
                    return NTStatus.STATUS_ACCESS_DENIED;
                }

                try
                {
                    if (forceDirectory)
                    {
                        state.LogToServer(Severity.Information, "NTCreate: Creating directory '{0}'", path);
                        entry = fileSystem.CreateDirectory(path);
                    }
                    else
                    {
                        state.LogToServer(Severity.Information, "NTCreate: Creating file '{0}'", path);
                        entry = fileSystem.CreateFile(path);
                    }
                }
                catch (IOException ex)
                {
                    ushort errorCode = IOExceptionHelper.GetWin32ErrorCode(ex);
                    if (errorCode == (ushort)Win32Error.ERROR_SHARING_VIOLATION)
                    {
                        state.LogToServer(Severity.Debug, "NTCreate: Sharing violation creating '{0}'", path);
                        return NTStatus.STATUS_SHARING_VIOLATION;
                    }
                    else
                    {
                        state.LogToServer(Severity.Debug, "NTCreate: Error creating '{0}'", path);
                        return NTStatus.STATUS_DATA_ERROR;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    state.LogToServer(Severity.Debug, "NTCreate: Error creating '{0}', Access Denied", path);
                    return NTStatus.STATUS_ACCESS_DENIED;
                }
            }
            else if (createDisposition == CreateDisposition.FILE_OPEN_IF ||
                     createDisposition == CreateDisposition.FILE_OVERWRITE ||
                     createDisposition == CreateDisposition.FILE_OVERWRITE_IF ||
                     createDisposition == CreateDisposition.FILE_SUPERSEDE)
            {
                entry = fileSystem.GetEntry(path);
                if (entry == null)
                {
                    if (createDisposition == CreateDisposition.FILE_OVERWRITE)
                    {
                        return NTStatus.STATUS_OBJECT_PATH_NOT_FOUND;
                    }

                    if (!hasWriteAccess)
                    {
                        return NTStatus.STATUS_ACCESS_DENIED;
                    }

                    try
                    {
                        if (forceDirectory)
                        {
                            state.LogToServer(Severity.Information, "NTCreate: Creating directory '{0}'", path);
                            entry = fileSystem.CreateDirectory(path);
                        }
                        else
                        {
                            state.LogToServer(Severity.Information, "NTCreate: Creating file '{0}'", path);
                            entry = fileSystem.CreateFile(path);
                        }
                    }
                    catch (IOException ex)
                    {
                        ushort errorCode = IOExceptionHelper.GetWin32ErrorCode(ex);
                        if (errorCode == (ushort)Win32Error.ERROR_SHARING_VIOLATION)
                        {
                            return NTStatus.STATUS_SHARING_VIOLATION;
                        }
                        else
                        {
                            return NTStatus.STATUS_DATA_ERROR;
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return NTStatus.STATUS_ACCESS_DENIED;
                    }
                }
                else
                {
                    if (createDisposition == CreateDisposition.FILE_OVERWRITE ||
                        createDisposition == CreateDisposition.FILE_OVERWRITE_IF ||
                        createDisposition == CreateDisposition.FILE_SUPERSEDE)
                    {
                        if (!hasWriteAccess)
                        {
                            return NTStatus.STATUS_ACCESS_DENIED;
                        }

                        // Truncate the file
                        try
                        {
                            Stream temp = fileSystem.OpenFile(path, FileMode.Truncate, FileAccess.ReadWrite, FileShare.ReadWrite);
                            temp.Close();
                        }
                        catch (IOException ex)
                        {
                            ushort errorCode = IOExceptionHelper.GetWin32ErrorCode(ex);
                            if (errorCode == (ushort)Win32Error.ERROR_SHARING_VIOLATION)
                            {
                                return NTStatus.STATUS_SHARING_VIOLATION;
                            }
                            else
                            {
                                return NTStatus.STATUS_DATA_ERROR;
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            return NTStatus.STATUS_ACCESS_DENIED;
                        }
                    }
                }
            }
            else
            {
                return NTStatus.STATUS_INVALID_PARAMETER;
            }

            FileAccess fileAccess = ToFileAccess(desiredAccess.File);
            if (!hasWriteAccess && (fileAccess == FileAccess.Write || fileAccess == FileAccess.ReadWrite))
            {
                return NTStatus.STATUS_ACCESS_DENIED;
            }

            return NTStatus.STATUS_SUCCESS;
        }

        public static FileAccess ToFileAccess(FileAccessMask desiredAccess)
        {
            if ((desiredAccess & FileAccessMask.GENERIC_ALL) > 0 ||
                ((desiredAccess & FileAccessMask.FILE_READ_DATA) > 0 && (desiredAccess & FileAccessMask.FILE_WRITE_DATA) > 0) ||
                ((desiredAccess & FileAccessMask.FILE_READ_DATA) > 0 && (desiredAccess & FileAccessMask.FILE_APPEND_DATA) > 0))
            {
                return FileAccess.ReadWrite;
            }
            else if ((desiredAccess & FileAccessMask.GENERIC_WRITE) > 0 ||
                     (desiredAccess & FileAccessMask.FILE_WRITE_DATA) > 0 ||
                     (desiredAccess & FileAccessMask.FILE_APPEND_DATA) > 0)
            {
                return FileAccess.Write;
            }
            else if ((desiredAccess & FileAccessMask.FILE_READ_DATA) > 0)
            {
                return FileAccess.Read;
            }
            else
            {
                return (FileAccess)0;
            }
        }

        public static FileShare ToFileShare(ShareAccess shareAccess)
        {
            if ((shareAccess & ShareAccess.FILE_SHARE_READ) > 0 && (shareAccess & ShareAccess.FILE_SHARE_WRITE) > 0)
            {
                return FileShare.ReadWrite;
            }
            else if ((shareAccess & ShareAccess.FILE_SHARE_WRITE) > 0)
            {
                return FileShare.Write;
            }
            else if ((shareAccess & ShareAccess.FILE_SHARE_READ) > 0)
            {
                return FileShare.Read;
            }
            else if ((shareAccess & ShareAccess.FILE_SHARE_DELETE) > 0)
            {
                return FileShare.Delete;
            }
            else
            {
                return FileShare.None;
            }
        }

        private static NTCreateAndXResponse CreateResponseForNamedPipe(ushort fileID)
        {
            NTCreateAndXResponse response = new NTCreateAndXResponse();
            response.FID = fileID;
            response.CreateDisposition = CreateDisposition.FILE_OPEN;
            response.ExtFileAttributes = ExtendedFileAttributes.Normal;
            response.ResourceType = ResourceType.FileTypeMessageModePipe;
            response.NMPipeStatus.ICount = 255;
            response.NMPipeStatus.ReadMode = ReadMode.MessageMode;
            response.NMPipeStatus.NamedPipeType = NamedPipeType.MessageNodePipe;
            return response;
        }

        private static NTCreateAndXResponseExtended CreateResponseExtendedForNamedPipe(ushort fileID)
        {
            NTCreateAndXResponseExtended response = new NTCreateAndXResponseExtended();
            response.FID = fileID;
            response.CreateDisposition = CreateDisposition.FILE_OPEN;
            response.ExtFileAttributes = ExtendedFileAttributes.Normal;
            response.ResourceType = ResourceType.FileTypeMessageModePipe;
            NamedPipeStatus status = new NamedPipeStatus();
            status.ICount = 255;
            status.ReadMode = ReadMode.MessageMode;
            status.NamedPipeType = NamedPipeType.MessageNodePipe;
            response.NMPipeStatus = status;
            response.MaximalAccessRights.File = FileAccessMask.FILE_READ_DATA | FileAccessMask.FILE_WRITE_DATA | FileAccessMask.FILE_APPEND_DATA |
                                                FileAccessMask.FILE_READ_EA | FileAccessMask.FILE_WRITE_EA |
                                                FileAccessMask.FILE_EXECUTE |
                                                FileAccessMask.FILE_READ_ATTRIBUTES | FileAccessMask.FILE_WRITE_ATTRIBUTES |
                                                FileAccessMask.DELETE | FileAccessMask.READ_CONTROL | FileAccessMask.WRITE_DAC | FileAccessMask.WRITE_OWNER | FileAccessMask.SYNCHRONIZE;
            response.GuestMaximalAccessRights.File = FileAccessMask.FILE_READ_DATA | FileAccessMask.FILE_WRITE_DATA |
                                                    FileAccessMask.FILE_READ_EA | FileAccessMask.FILE_WRITE_EA |
                                                    FileAccessMask.FILE_READ_ATTRIBUTES | FileAccessMask.FILE_WRITE_ATTRIBUTES |
                                                    FileAccessMask.READ_CONTROL | FileAccessMask.SYNCHRONIZE;
            return response;
        }

        private static NTCreateAndXResponse CreateResponseFromFileSystemEntry(FileSystemEntry entry, ushort fileID)
        {
            NTCreateAndXResponse response = new NTCreateAndXResponse();
            if (entry.IsDirectory)
            {
                response.ExtFileAttributes = ExtendedFileAttributes.Directory;
                response.Directory = true;
            }
            else
            {
                response.ExtFileAttributes = ExtendedFileAttributes.Normal;
            }
            response.FID = fileID;
            response.CreateDisposition = CreateDisposition.FILE_OPEN;
            response.AllocationSize = InfoHelper.GetAllocationSize(entry.Size);
            response.EndOfFile = entry.Size;
            response.CreateTime = entry.CreationTime;
            response.LastAccessTime = entry.LastAccessTime;
            response.LastWriteTime = entry.LastWriteTime;
            response.LastChangeTime = entry.LastWriteTime;
            response.ResourceType = ResourceType.FileTypeDisk;
            return response;
        }

        private static NTCreateAndXResponseExtended CreateResponseExtendedFromFileSystemEntry(FileSystemEntry entry, ushort fileID)
        {
            NTCreateAndXResponseExtended response = new NTCreateAndXResponseExtended();
            if (entry.IsDirectory)
            {
                response.ExtFileAttributes = ExtendedFileAttributes.Directory;
                response.Directory = true;
            }
            else
            {
                response.ExtFileAttributes = ExtendedFileAttributes.Normal;
            }
            response.FID = fileID;
            response.CreateTime = entry.CreationTime;
            response.LastAccessTime = entry.LastAccessTime;
            response.LastWriteTime = entry.LastWriteTime;
            response.LastChangeTime = entry.LastWriteTime;
            response.CreateDisposition = CreateDisposition.FILE_OPEN;
            response.AllocationSize = InfoHelper.GetAllocationSize(entry.Size);
            response.EndOfFile = entry.Size;
            response.ResourceType = ResourceType.FileTypeDisk;
            response.FileStatus = FileStatus.NO_EAS | FileStatus.NO_SUBSTREAMS | FileStatus.NO_REPARSETAG;
            response.MaximalAccessRights.File = FileAccessMask.FILE_READ_DATA | FileAccessMask.FILE_WRITE_DATA | FileAccessMask.FILE_APPEND_DATA |
                                                FileAccessMask.FILE_READ_EA | FileAccessMask.FILE_WRITE_EA |
                                                FileAccessMask.FILE_EXECUTE |
                                                FileAccessMask.FILE_READ_ATTRIBUTES | FileAccessMask.FILE_WRITE_ATTRIBUTES |
                                                FileAccessMask.DELETE | FileAccessMask.READ_CONTROL | FileAccessMask.WRITE_DAC | FileAccessMask.WRITE_OWNER | FileAccessMask.SYNCHRONIZE;
            response.GuestMaximalAccessRights.File = FileAccessMask.FILE_READ_DATA | FileAccessMask.FILE_WRITE_DATA |
                                                    FileAccessMask.FILE_READ_EA | FileAccessMask.FILE_WRITE_EA |
                                                    FileAccessMask.FILE_READ_ATTRIBUTES | FileAccessMask.FILE_WRITE_ATTRIBUTES |
                                                    FileAccessMask.READ_CONTROL | FileAccessMask.SYNCHRONIZE;
            return response;
        }
    }
}
