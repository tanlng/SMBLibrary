/* Copyright (C) 2017 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using SMBLibrary.Adapters;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace SMBLibrary.Win32
{
    public class AdapterPendingRequest
    {
        public FileHandle FileHandle;
        public uint ThreadID;
        public bool Cleanup;
        public DirectoryChangeMonitor monitor { get; set; }
    }
    public class AdapterPendingRequestCollection
    {
        private Dictionary<string, List<AdapterPendingRequest>> m_handleToNotifyChangeRequests = new Dictionary<string, List<AdapterPendingRequest>>();

        public string Count => $"{m_handleToNotifyChangeRequests.Count}/{m_handleToNotifyChangeRequests.Sum(c => c.Value.Count)}";
        public void Add(AdapterPendingRequest request)
        {
            lock (m_handleToNotifyChangeRequests)
            {
                List<AdapterPendingRequest> pendingRequests;
                bool containsKey = m_handleToNotifyChangeRequests.TryGetValue(request.FileHandle.GUID, out pendingRequests);
                if (containsKey)
                {
                    pendingRequests.Add(request);
                }
                else
                {
                    pendingRequests = new List<AdapterPendingRequest>();
                    pendingRequests.Add(request);
                    m_handleToNotifyChangeRequests.Add(request.FileHandle.GUID, pendingRequests);
                }
            }
        }

        public void Remove(FileHandle handle, uint threadID)
        {
            lock (m_handleToNotifyChangeRequests)
            {
                List<AdapterPendingRequest> pendingRequests;
                bool containsKey = m_handleToNotifyChangeRequests.TryGetValue(handle.GUID, out pendingRequests);
                if (containsKey)
                {
                    for (int index = 0; index < pendingRequests.Count; index++)
                    {
                        if (pendingRequests[index].ThreadID == threadID)
                        {
                            pendingRequests.RemoveAt(index);
                            index--;
                        }
                    }

                    if (pendingRequests.Count == 0)
                    {
                        m_handleToNotifyChangeRequests.Remove(handle.GUID);
                    }
                }
            }
        }

        public List<AdapterPendingRequest> GetRequestsByHandle(FileHandle handle)
        {
            List<AdapterPendingRequest> pendingRequests;
            bool containsKey = m_handleToNotifyChangeRequests.TryGetValue(handle.GUID, out pendingRequests);
            if (containsKey)
            {
                return new List<AdapterPendingRequest>(pendingRequests);
            }
            return new List<AdapterPendingRequest>();
        }
    }
}
