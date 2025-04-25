using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SMBLibrary.Adapters
{
    // 定义文件通知信息结构体
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct FILE_NOTIFY_INFORMATION
    {
        public uint NextEntryOffset;
        public uint Action;
        public uint FileNameLength;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FileName;
    }

    public class DirectoryChangeMonitor : IDisposable
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        private FileSystemWatcher _watcher;
        private ManualResetEvent _resetEvent;
        private byte[] _buffer;
        private bool _disposed = false;
        private bool _isBlueberry;

        public DirectoryChangeMonitor(bool isBlueberry)
        {
            _isBlueberry = isBlueberry;
            _watcher = new FileSystemWatcher();
            _resetEvent = new ManualResetEvent(false);
        }

        public NTStatus NotifyChangeDirectoryFile(string path, out byte[] buffer, NotifyChangeFilter filter, bool watchSubtree)
        {
            buffer = null;

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DirectoryChangeMonitor));
            }

            try
            {
                logger.Debug($"监听目录{path} 是否包含子目录{watchSubtree} filter{filter}");
                // 配置 FileSystemWatcher
                _watcher.Path = path;
                _watcher.IncludeSubdirectories = watchSubtree;

                // 根据 NotifyChangeFilter 配置监控的变化类型
                _watcher.NotifyFilter = 0;
                if ((filter & NotifyChangeFilter.FileName) != 0)
                {
                    _watcher.NotifyFilter |= NotifyFilters.FileName;
                }
                if ((filter & NotifyChangeFilter.DirName) != 0)
                {
                    _watcher.NotifyFilter |= NotifyFilters.DirectoryName;
                }
                if ((filter & NotifyChangeFilter.Attributes) != 0)
                {
                    _watcher.NotifyFilter |= NotifyFilters.Attributes;
                }
                if ((filter & NotifyChangeFilter.Size) != 0)
                {
                    _watcher.NotifyFilter |= NotifyFilters.Size;
                }
                if ((filter & NotifyChangeFilter.LastWrite) != 0)
                {
                    _watcher.NotifyFilter |= NotifyFilters.LastWrite;
                }
                if ((filter & NotifyChangeFilter.LastAccess) != 0)
                {
                    _watcher.NotifyFilter |= NotifyFilters.LastAccess;
                }
                if ((filter & NotifyChangeFilter.Creation) != 0)
                {
                    _watcher.NotifyFilter |= NotifyFilters.CreationTime;
                }
                if ((filter & NotifyChangeFilter.Security) != 0)
                {
                    _watcher.NotifyFilter |= NotifyFilters.Security;
                }

                // 订阅事件
                _watcher.Changed += OnChanged;
                _watcher.Created += OnCreated;
                _watcher.Deleted += OnDeleted;
                _watcher.Renamed += OnRenamed;

                // 启用监控
                _watcher.EnableRaisingEvents = true;

                // 同步等待目录变化
                _resetEvent.WaitOne();

                buffer = _buffer;

                return NTStatus.STATUS_SUCCESS;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "NotifyChangeDirectoryFile failed");
                return NTStatus.STATUS_CANCELLED;
            }
            finally
            {
                // 禁用监控并取消订阅事件
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnChanged;
                _watcher.Created -= OnCreated;
                _watcher.Deleted -= OnDeleted;
                _watcher.Renamed -= OnRenamed;
                _resetEvent.Reset();
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            logger.Debug($"改变 {e.FullPath}");
            OnCommonNotify(sender, e);
        }
        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            logger.Debug($"创建 {e.FullPath}");
            OnCommonNotify(sender, e);
        }
        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            logger.Debug($"临时文件删除 {e.FullPath}");
            OnCommonNotify(sender, e);
        }
        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            logger.Debug($"改名 {e.FullPath}");
            OnCommonNotify(sender, e);
        }

        private void OnCommonNotify(object sender, FileSystemEventArgs e)
        {
            if (_disposed) return;

            uint action = GetActionCode(e.ChangeType);

            string fileName = GetName(e.FullPath);
            _buffer = CreateFileNotifyInformationBuffer(action, fileName);
            _resetEvent.Set();
        }
        private string GetName(string localPath)
        {
            if (!_isBlueberry)
            {
                return Path.GetFileName(localPath);
            }
            if (Directory.Exists(localPath))
            {
                return Path.GetFileName(localPath);
            }
            else
            {
                var lastIndex = localPath.LastIndexOf(".-_-");
                if (lastIndex != -1)
                {
                    localPath = localPath.Substring(0, lastIndex);
                }
                return Path.GetFileName(localPath);
            }
        }


        private uint GetActionCode(WatcherChangeTypes changeType)
        {
            switch (changeType)
            {
                case WatcherChangeTypes.Created:
                    return 0x00000001;
                case WatcherChangeTypes.Deleted:
                    return 0x00000002;
                case WatcherChangeTypes.Changed:
                    return 0x00000003;
                case WatcherChangeTypes.Renamed:
                    return 0x00000004;
                default:
                    return 0;
            }
        }

        private byte[] CreateFileNotifyInformationBuffer(uint action, string fileName)
        {
            logger.Debug($"CreateFileNotifyInformationBuffer: action={action}, fileName={fileName}");
            FILE_NOTIFY_INFORMATION info = new FILE_NOTIFY_INFORMATION
            {
                NextEntryOffset = 0,
                Action = action,
                FileNameLength = (uint)(fileName.Length * 2),
                FileName = fileName
            };

            int size = Marshal.SizeOf(info);
            byte[] buffer = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, true);
                Marshal.Copy(ptr, buffer, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return buffer;
        }

        public void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_watcher != null)
                    {
                        logger.Debug($"取消监听 {_watcher.Path}");
                        // 释放托管资源
                        _watcher?.Dispose();
                    }
                    else
                    {

                        logger.Debug($"取消监听, 没有watch");
                    }
                    _resetEvent?.Dispose();
                }

                // 释放非托管资源

                _disposed = true;
            }
        }

        public void Dispose()
        {
            DirectoryChangeMonitorManager.ReleaseMonitor(this);
            GC.SuppressFinalize(this);
        }
    }
}
