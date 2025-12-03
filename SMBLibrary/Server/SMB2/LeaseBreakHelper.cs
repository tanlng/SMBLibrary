/* Copyright (C) 2025. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using SMBLibrary.SMB2;
using SMBLibrary.Server.Leasing;
using Utilities;

namespace SMBLibrary.Server.SMB2
{
    /// <summary>
    /// 租约中断辅助类 - 统一管理所有 Lease Break 场景
    /// 
    /// Lease Break Helper - Centralized management for all Lease Break scenarios
    /// 
    /// 核心原则 (Core Principles):
    /// 1. 永远不中断自己的 Lease (Never break own session's lease)
    /// 2. 只在真正冲突时中断 (Break only on real conflicts)
    /// 3. 区分文件操作和目录操作 (Distinguish file vs directory operations)
    /// 4. 保持与 Windows Server 行为一致 (Maintain Windows Server compatibility)
    /// </summary>
    internal static class LeaseBreakHelper
    {
        #region File Operations (文件操作)

        /// <summary>
        /// Break leases when creating a new file
        /// 创建新文件时中断租约
        /// 
        /// 场景 (Scenario):
        /// - 客户端 A 创建文件 /dir/file.txt (Client A creates file)
        /// - 需要中断其他客户端对该文件的 Lease (Break other clients' leases on file)
        /// - 需要中断其他客户端对父目录的 Lease (Break other clients' leases on parent dir)
        /// - 不中断客户端 A 自己的 Lease (Don't break Client A's own lease)
        /// </summary>
        /// <param name="leaseManager">Lease manager instance</param>
        /// <param name="logger">Logging delegate</param>
        /// <param name="filePath">File path being created</param>
        /// <param name="currentSessionId">Current session ID (to exclude from breaking)</param>
        /// <param name="isDirectory">Whether this is a directory creation (directories don't break leases)</param>
        public static void BreakLeasesOnFileCreate(LeaseManager leaseManager, LogDelegate logger, string filePath, ulong currentSessionId, bool isDirectory)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            try
            {
                Log(logger, Severity.Information, 
                    "[LeaseBreakHelper] Breaking leases for FILE CREATE: {0}, SessionId: {1}", 
                    filePath, currentSessionId);

                // Break leases on the target file path
                // 中断目标文件路径上的 Lease (除了当前会话)
                leaseManager?.BreakLeases(filePath, LeaseState.None, currentSessionId);
                leaseManager?.BreakParentDirectoryLeases(filePath, LeaseState.None, currentSessionId);

                Log(logger, Severity.Information, 
                    "[LeaseBreakHelper] Completed lease break for file create: {0}", filePath);
            }
            catch (Exception ex)
            {
                Log(logger, Severity.Error, 
                    "[LeaseBreakHelper] Failed to break leases for file create: {0}. Error: {1}", 
                    filePath, ex.Message);
            }
        }

        /// <summary>
        /// Break leases when writing to an existing file
        /// 写入现有文件时中断租约
        /// 
        /// 场景 (Scenario):
        /// - 客户端 A 写入文件 /dir/file.txt (Client A writes to file)
        /// - 需要中断其他客户端的 Read/Write Lease (Break other clients' R/W leases)
        /// - 客户端 A 自己的 Lease 保持不变 (Client A's own lease remains intact)
        /// </summary>
        /// <param name="leaseManager">Lease manager instance</param>
        /// <param name="logger">Logging delegate</param>
        /// <param name="filePath">File path being written</param>
        /// <param name="currentSessionId">Current session ID</param>
        public static void BreakLeasesOnFileWrite(LeaseManager leaseManager, LogDelegate logger, string filePath, ulong currentSessionId)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            try
            {
                Log(logger, Severity.Information, 
                    "[LeaseBreakHelper] Breaking leases for FILE WRITE: {0}, SessionId: {1}", 
                    filePath, currentSessionId);

                // Break leases on the file being written
                // 中断正在写入的文件的 Lease (除了当前会话)
                leaseManager?.BreakLeases(filePath, LeaseState.None, currentSessionId);

                Log(logger, Severity.Information, 
                    "[LeaseBreakHelper] Completed lease break for file write: {0}", filePath);
            }
            catch (Exception ex)
            {
                Log(logger, Severity.Error, 
                    "[LeaseBreakHelper] Failed to break leases for file write: {0}. Error: {1}", 
                    filePath, ex.Message);
            }
        }

        #endregion

        #region File Metadata Operations (文件元数据操作)

        /// <summary>
        /// Break leases when renaming a file
        /// 重命名文件时中断租约
        /// 
        /// 场景 (Scenario):
        /// - 客户端 A 重命名 /dir/old.txt -> /dir/new.txt (Client A renames file)
        /// - 需要中断源文件和目标文件的 Lease (Break leases on source and destination)
        /// - 需要中断父目录的 Lease (Break leases on parent directory)
        /// - 不中断客户端 A 自己的 Lease (Don't break Client A's own lease)
        /// </summary>
        /// <param name="leaseManager">Lease manager instance</param>
        /// <param name="logger">Logging delegate</param>
        /// <param name="sourcePath">Source file path</param>
        /// <param name="destinationPath">Destination file path</param>
        /// <param name="currentSessionId">Current session ID</param>
        public static void BreakLeasesOnFileRename(LeaseManager leaseManager, LogDelegate logger, string sourcePath, string destinationPath, ulong currentSessionId)
        {
            if (string.IsNullOrEmpty(sourcePath))
                return;

            try
            {
                Log(logger, Severity.Information, 
                    "[LeaseBreakHelper] Breaking leases for FILE RENAME: '{0}' -> '{1}', SessionId: {2}", 
                    sourcePath, destinationPath, currentSessionId);

                // Break lease on source path
                // 中断源路径的 Lease
                leaseManager?.BreakLeases(sourcePath, LeaseState.None, currentSessionId);

                // Break lease on destination path (if different)
                // 如果目标路径不同,也中断目标路径的 Lease
                if (!string.IsNullOrEmpty(destinationPath) && 
                    !string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                {
                    leaseManager?.BreakLeases(destinationPath, LeaseState.None, currentSessionId);
                    leaseManager?.BreakParentDirectoryLeases(destinationPath, LeaseState.None, currentSessionId);
                }

                // TODO: 在这里应该触发 Notify Change 通知
                // 这样可以实现正确的时序: Target Break → Notify → Parent Break
                // 但目前的架构中 Notify 是在 SyncNewServerProcesser 中自动触发的
                // 所以暂时只能先中断父目录租约,时序还是不对
                
                // Break parent directory leases (after Notify ideally)
                // 中断父目录租约 (理想情况下应该在 Notify 之后)
                // if (!string.IsNullOrEmpty(destinationPath))
                // {
                //     Log(logger, Severity.Information, 
                //         "[LeaseBreakHelper] Breaking parent directory leases for destination: {0}", destinationPath);
                //     
                // }

                Log(logger, Severity.Information, 
                    "[LeaseBreakHelper] Completed lease break for file rename: {0}", sourcePath);
            }
            catch (Exception ex)
            {
                Log(logger, Severity.Error, 
                    "[LeaseBreakHelper] Failed to break leases for file rename: {0}. Error: {1}", 
                    sourcePath, ex.Message);
            }
        }

        /// <summary>
        /// Break leases when deleting a file
        /// 删除文件时中断租约
        /// 
        /// 场景 (Scenario):
        /// - 客户端 A 删除文件 /dir/file.txt (Client A deletes file)
        /// - 必须中断所有其他客户端的 Lease (Must break all other clients' leases)
        /// - 必须中断父目录的 Lease (Must break parent directory leases)
        /// - 不中断客户端 A 自己的 Lease (Don't break Client A's own lease)
        /// </summary>
        /// <param name="leaseManager">Lease manager instance</param>
        /// <param name="logger">Logging delegate</param>
        /// <param name="filePath">File path being deleted</param>
        /// <param name="currentSessionId">Current session ID</param>
        public static void BreakLeasesOnFileDelete(LeaseManager leaseManager, LogDelegate logger, string filePath, ulong currentSessionId)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            try
            {
                Log(logger, Severity.Information, 
                    "[LeaseBreakHelper] Breaking leases for FILE DELETE: {0}, SessionId: {1}", 
                    filePath, currentSessionId);

                // Break all leases on the file being deleted
                // 中断被删除文件的所有 Lease (除了当前会话)
                leaseManager?.BreakLeases(filePath, LeaseState.None, currentSessionId);

                Log(logger, Severity.Information, 
                    "[LeaseBreakHelper] Completed lease break for file delete: {0}", filePath);
            }
            catch (Exception ex)
            {
                Log(logger, Severity.Error, 
                    "[LeaseBreakHelper] Failed to break leases for file delete: {0}. Error: {1}", 
                    filePath, ex.Message);
            }
        }

        /// <summary>
        /// Check if a SetFileInformation operation requires lease breaking
        /// 检查 SetFileInformation 操作是否需要中断租约
        /// 
        /// 规则 (Rules):
        /// - Rename: 需要中断 (Requires break)
        /// - Delete: 需要中断 (Requires break)
        /// - Metadata changes (FileBasicInfo, FileAllocationInfo, etc.): 不需要中断 (No break needed)
        /// </summary>
        /// <param name="leaseManager">Lease manager instance</param>
        /// <param name="logger">Logging delegate</param>
        /// <param name="information">File information being set</param>
        /// <param name="filePath">File path</param>
        /// <param name="currentSessionId">Current session ID</param>
        public static void BreakLeasesOnSetFileInformation(LeaseManager leaseManager, LogDelegate logger, FileInformation information, string filePath, ulong currentSessionId)
        {
            if (information == null || string.IsNullOrEmpty(filePath))
                return;

            if (information is FileRenameInformationType2 renameInfo)
            {
                // Rename operation
                string destinationPath = renameInfo.FileName;
                if (!destinationPath.StartsWith(@"\"))
                {
                    destinationPath = @"\" + destinationPath;
                }
                BreakLeasesOnFileRename(leaseManager, logger, filePath, destinationPath, currentSessionId);
            }
            else if (information is FileDispositionInformation dispositionInfo && dispositionInfo.DeletePending)
            {
                // Delete operation
                BreakLeasesOnFileDelete(leaseManager, logger, filePath, currentSessionId);
            }
            else
            {
                // Metadata changes (FileBasicInformation, FileAllocationInformation, etc.)
                // These do NOT affect directory contents, so no lease break needed
                // 元数据变更不影响目录内容，不需要中断 Lease
                Log(logger, Severity.Verbose, 
                    "[LeaseBreakHelper] Skipping lease break for metadata change: {0} on '{1}'", 
                    information.GetType().Name, filePath);
            }
        }

        #endregion

        #region Directory Operations (目录操作)

        /// <summary>
        /// Break leases when creating a directory
        /// 创建目录时中断租约
        /// 
        /// 注意 (Note): Windows Server does NOT break leases on directory creation
        /// Windows Server 在创建目录时不中断 Lease，只发送 NotifyChange
        /// </summary>
        /// <param name="logger">Logging delegate</param>
        /// <param name="directoryPath">Directory path being created</param>
        /// <param name="currentSessionId">Current session ID</param>
        public static void BreakLeasesOnDirectoryCreate(LogDelegate logger, string directoryPath, ulong currentSessionId)
        {
            // This method intentionally does nothing
            // Windows Server behavior: Directory creation only sends NotifyChange, no Lease Break
            Log(logger, Severity.Verbose, 
                "[LeaseBreakHelper] Skipping lease break for directory creation: {0} (Windows behavior)", 
                directoryPath);
        }

        #endregion

        #region Helper Methods (辅助方法)

        /// <summary>
        /// Log message with formatting
        /// </summary>
        private static void Log(LogDelegate logger, Severity severity, string format, params object[] args)
        {
            if (logger == null) return;
            
            string message = args.Length > 0 
                ? string.Format(format, args) 
                : format;
            
            logger.Invoke(severity, message);
        }

        #endregion
    }
}
