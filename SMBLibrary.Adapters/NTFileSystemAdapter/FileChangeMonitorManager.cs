using System.Collections.Generic;

namespace SMBLibrary.Adapters
{
    public static class FileChangeMonitorManager
    {
        private static List<FileChangeMonitor> _monitors = new List<FileChangeMonitor>();

        // 获取当前活动的监听数量
        public static int MonitorCount
        {
            get { return _monitors.Count; }
        }

        // 创建一个新的 DirectoryChangeMonitor 实例并开始监听
        public static FileChangeMonitor CreateMonitor(bool isBlueberry)
        {
            FileChangeMonitor monitor = new FileChangeMonitor(isBlueberry);
            _monitors.Add(monitor);
            return monitor;
        }

        // 释放指定的 DirectoryChangeMonitor 实例
        public static void ReleaseMonitor(FileChangeMonitor monitor)
        {
            if (monitor != null)
            {
                monitor.Dispose(true);
                _monitors.Remove(monitor);
            }
        }

        // 释放所有的 DirectoryChangeMonitor 实例
        public static void ReleaseAllMonitors()
        {
            foreach (var monitor in _monitors)
            {
                monitor.Dispose(true);
            }
            _monitors.Clear();
        }
    }
}