/* Copyright (C) 2017 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace SMBLibrary.Win32
{
    public class PendingRequestCollection
    {
        // 字典，用于将文件句柄 (IntPtr) 映射到对应的待处理请求列表 (List<PendingRequest>)
        // 这样可以根据文件句柄快速找到与之关联的所有待处理请求
        private Dictionary<IntPtr, List<PendingRequest>> m_handleToNotifyChangeRequests = new Dictionary<IntPtr, List<PendingRequest>>();

        /// <summary>
        /// 向集合中添加一个待处理请求
        /// </summary>
        /// <param name="request">要添加的待处理请求</param>
        public void Add(PendingRequest request)
        {
            // 加锁以确保线程安全，避免多个线程同时修改字典
            lock (m_handleToNotifyChangeRequests)
            {
                List<PendingRequest> pendingRequests;
                // 尝试从字典中获取与请求的文件句柄关联的待处理请求列表
                bool containsKey = m_handleToNotifyChangeRequests.TryGetValue(request.FileHandle, out pendingRequests);
                if (containsKey)
                {
                    // 如果字典中已经存在该文件句柄的请求列表，则将新请求添加到该列表中
                    pendingRequests.Add(request);
                }
                else
                {
                    // 如果字典中不存在该文件句柄的请求列表，则创建一个新的列表
                    pendingRequests = new List<PendingRequest>();
                    // 将新请求添加到新列表中
                    pendingRequests.Add(request);
                    // 将新的文件句柄和对应的请求列表添加到字典中
                    m_handleToNotifyChangeRequests.Add(request.FileHandle, pendingRequests);
                }
            }
        }

        /// <summary>
        /// 从集合中移除指定文件句柄和线程 ID 的待处理请求
        /// </summary>
        /// <param name="handle">要移除请求的文件句柄</param>
        /// <param name="threadID">要移除请求的线程 ID</param>
        public void Remove(IntPtr handle, uint threadID)
        {
            // 加锁以确保线程安全，避免多个线程同时修改字典
            lock (m_handleToNotifyChangeRequests)
            {
                List<PendingRequest> pendingRequests;
                // 尝试从字典中获取与指定文件句柄关联的待处理请求列表
                bool containsKey = m_handleToNotifyChangeRequests.TryGetValue(handle, out pendingRequests);
                if (containsKey)
                {
                    // 遍历请求列表，查找与指定线程 ID 匹配的请求
                    for (int index = 0; index < pendingRequests.Count; index++)
                    {
                        if (pendingRequests[index].ThreadID == threadID)
                        {
                            // 如果找到匹配的请求，则将其从列表中移除
                            pendingRequests.RemoveAt(index);
                            // 由于移除了一个元素，列表长度减 1，需要将索引减 1 以继续正确遍历
                            index--;
                        }
                    }

                    // 如果移除请求后列表为空，则从字典中移除该文件句柄对应的条目
                    if (pendingRequests.Count == 0)
                    {
                        m_handleToNotifyChangeRequests.Remove(handle);
                    }
                }
            }
        }

        /// <summary>
        /// 根据文件句柄获取对应的待处理请求列表
        /// </summary>
        /// <param name="handle">要查找请求的文件句柄</param>
        /// <returns>与指定文件句柄关联的待处理请求列表，如果不存在则返回空列表</returns>
        public List<PendingRequest> GetRequestsByHandle(IntPtr handle)
        {
            List<PendingRequest> pendingRequests;
            // 尝试从字典中获取与指定文件句柄关联的待处理请求列表
            bool containsKey = m_handleToNotifyChangeRequests.TryGetValue((IntPtr)handle, out pendingRequests);
            if (containsKey)
            {
                // 如果找到匹配的请求列表，则返回该列表的一个副本
                return new List<PendingRequest>(pendingRequests);
            }
            // 如果未找到匹配的请求列表，则返回一个空列表
            return new List<PendingRequest>();
        }
    }
}