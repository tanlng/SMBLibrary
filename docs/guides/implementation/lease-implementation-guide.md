# SMB 2.0/2.1 租赁协议实现指南

## 概述

本指南详细描述了如何在 SMBLibrary 中实现 SMB 2.0/2.1 租赁协议。指南包括实现步骤、代码示例、最佳实践和常见问题的解决方案。

## 实现阶段

### 阶段 1: 基础架构搭建

#### 1.1 创建项目结构

首先创建租赁协议相关的目录结构：

```
SMBLibrary/
├── Server/
│   ├── Leasing/                    # 新增
│   │   ├── LeaseManager.cs
│   │   ├── LeaseInfo.cs
│   │   ├── LeaseRequest.cs
│   │   ├── LeaseBreakHandler.cs
│   │   ├── LeaseContextHandler.cs
│   │   ├── LeaseTimeoutManager.cs
│   │   └── Exceptions/
│   │       ├── LeaseException.cs
│   │       ├── LeaseNotFoundException.cs
│   │       └── LeaseExpiredException.cs
│   └── SMB2/
│       ├── Commands/
│       │   ├── LeaseBreakRequest.cs    # 新增
│       │   ├── LeaseBreakResponse.cs   # 新增
│       │   └── LeaseBreakAck.cs         # 新增
│       └── LeaseHelper.cs              # 新增
├── Client/
│   ├── Leasing/                    # 新增
│   │   ├── LeaseCache.cs
│   │   ├── LeaseClientManager.cs
│   │   └── LeaseBreakHandler.cs
│   └── SMB2FileStore.cs            # 修改
└── SMB2/
    ├── Structures/
    │   ├── LeaseContext.cs         # 新增
    │   └── LeaseBreakContext.cs    # 新增
    └── Enums/
        └── LeaseState.cs           # 新增
```

#### 1.2 定义基础数据结构

**LeaseState.cs**
```csharp
using System;

namespace SMBLibrary.SMB2
{
    [Flags]
    public enum LeaseState : uint
    {
        None = 0x00000000,
        ReadCaching = 0x00000001,
        HandleCaching = 0x00000002,
        WriteCaching = 0x00000004,
        DirectoryCaching = 0x00000008
    }
}
```

**LeaseInfo.cs**
```csharp
using System;
using SMBLibrary.SMB2;

namespace SMBLibrary.Server.Leasing
{
    public class LeaseInfo
    {
        public Guid LeaseKey { get; set; }
        public LeaseState State { get; set; }
        public LeaseFlags Flags { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime ExpirationTime { get; set; }
        public ulong SessionId { get; set; }
        public FileID FileId { get; set; }
        public string FilePath { get; set; }
        public LeaseBreakReason? PendingBreakReason { get; set; }
        public DateTime LastAccessTime { get; set; }
        public int AccessCount { get; set; }

        public bool IsExpired => DateTime.UtcNow > ExpirationTime;
        public bool IsBreaking => PendingBreakReason.HasValue;
        public TimeSpan RemainingTime => ExpirationTime - DateTime.UtcNow;
    }
}
```

#### 1.3 实现租赁管理器

**LeaseManager.cs**
```csharp
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using SMBLibrary.SMB2;

namespace SMBLibrary.Server.Leasing
{
    public class LeaseManager : IDisposable
    {
        private readonly ConcurrentDictionary<Guid, LeaseInfo> m_leaseRegistry;
        private readonly ConcurrentDictionary<FileID, List<Guid>> m_fileLeases;
        private readonly ConcurrentDictionary<ulong, List<Guid>> m_sessionLeases;
        private readonly SortedDictionary<DateTime, List<Guid>> m_expirationQueue;
        private readonly ReaderWriterLockSlim m_lock;
        private readonly Timer m_cleanupTimer;
        private readonly LeaseManagerConfiguration m_config;

        public event EventHandler<LeaseBreakEventArgs> LeaseBreakRequested;
        public event EventHandler<LeaseExpiredEventArgs> LeaseExpired;
        public event EventHandler<LeaseCreatedEventArgs> LeaseCreated;

        public LeaseManager(LeaseManagerConfiguration config = null)
        {
            m_config = config ?? new LeaseManagerConfiguration();
            m_leaseRegistry = new ConcurrentDictionary<Guid, LeaseInfo>();
            m_fileLeases = new ConcurrentDictionary<FileID, List<Guid>>();
            m_sessionLeases = new ConcurrentDictionary<ulong, List<Guid>>();
            m_expirationQueue = new SortedDictionary<DateTime, List<Guid>>();
            m_lock = new ReaderWriterLockSlim();
            
            // 启动清理定时器
            m_cleanupTimer = new Timer(CleanupExpiredLeases, null, 
                m_config.CleanupInterval, m_config.CleanupInterval);
        }

        public LeaseInfo CreateLease(LeaseRequest request)
        {
            if (m_leaseRegistry.Count >= m_config.MaxLeases)
            {
                throw new LeaseException("Maximum lease count exceeded", 
                    Guid.Empty, LeaseErrorCode.LeaseResourceExhausted);
            }

            var leaseInfo = new LeaseInfo
            {
                LeaseKey = request.LeaseKey,
                State = request.LeaseState,
                Flags = request.LeaseFlags,
                CreatedTime = DateTime.UtcNow,
                ExpirationTime = DateTime.UtcNow.Add(request.LeaseDuration),
                SessionId = request.SessionId,
                FileId = request.FileId,
                FilePath = request.FilePath,
                LastAccessTime = DateTime.UtcNow,
                AccessCount = 0
            };

            m_leaseRegistry.TryAdd(leaseInfo.LeaseKey, leaseInfo);
            AddToIndexes(leaseInfo);
            
            LeaseCreated?.Invoke(this, new LeaseCreatedEventArgs(leaseInfo));
            return leaseInfo;
        }

        public void BreakLease(Guid leaseKey, LeaseBreakReason reason)
        {
            if (!m_leaseRegistry.TryGetValue(leaseKey, out var leaseInfo))
            {
                throw new LeaseNotFoundException(leaseKey);
            }

            leaseInfo.PendingBreakReason = reason;
            LeaseBreakRequested?.Invoke(this, new LeaseBreakEventArgs(leaseKey, reason));
        }

        public void AcknowledgeLeaseBreak(Guid leaseKey)
        {
            if (!m_leaseRegistry.TryGetValue(leaseKey, out var leaseInfo))
            {
                throw new LeaseNotFoundException(leaseKey);
            }

            leaseInfo.PendingBreakReason = null;
            RemoveLease(leaseKey);
        }

        public LeaseInfo GetLeaseInfo(Guid leaseKey)
        {
            m_leaseRegistry.TryGetValue(leaseKey, out var leaseInfo);
            return leaseInfo;
        }

        public List<LeaseInfo> GetActiveLeases()
        {
            return new List<LeaseInfo>(m_leaseRegistry.Values);
        }

        public List<LeaseInfo> GetLeasesBySession(ulong sessionId)
        {
            var leases = new List<LeaseInfo>();
            if (m_sessionLeases.TryGetValue(sessionId, out var leaseKeys))
            {
                foreach (var key in leaseKeys)
                {
                    if (m_leaseRegistry.TryGetValue(key, out var lease))
                    {
                        leases.Add(lease);
                    }
                }
            }
            return leases;
        }

        public List<LeaseInfo> GetLeasesByFile(FileID fileId)
        {
            var leases = new List<LeaseInfo>();
            if (m_fileLeases.TryGetValue(fileId, out var leaseKeys))
            {
                foreach (var key in leaseKeys)
                {
                    if (m_leaseRegistry.TryGetValue(key, out var lease))
                    {
                        leases.Add(lease);
                    }
                }
            }
            return leases;
        }

        public bool RemoveLease(Guid leaseKey)
        {
            if (m_leaseRegistry.TryRemove(leaseKey, out var leaseInfo))
            {
                RemoveFromIndexes(leaseInfo);
                return true;
            }
            return false;
        }

        public int CleanupExpiredLeases()
        {
            var expiredLeases = new List<Guid>();
            
            m_lock.EnterReadLock();
            try
            {
                var now = DateTime.UtcNow;
                foreach (var kvp in m_expirationQueue)
                {
                    if (kvp.Key <= now)
                    {
                        expiredLeases.AddRange(kvp.Value);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            finally
            {
                m_lock.ExitReadLock();
            }

            foreach (var leaseKey in expiredLeases)
            {
                if (m_leaseRegistry.TryRemove(leaseKey, out var leaseInfo))
                {
                    RemoveFromIndexes(leaseInfo);
                    LeaseExpired?.Invoke(this, new LeaseExpiredEventArgs(leaseInfo));
                }
            }

            return expiredLeases.Count;
        }

        private void AddToIndexes(LeaseInfo leaseInfo)
        {
            // 添加到文件索引
            m_fileLeases.AddOrUpdate(leaseInfo.FileId, 
                new List<Guid> { leaseInfo.LeaseKey },
                (key, existing) => { existing.Add(leaseInfo.LeaseKey); return existing; });

            // 添加到会话索引
            m_sessionLeases.AddOrUpdate(leaseInfo.SessionId,
                new List<Guid> { leaseInfo.LeaseKey },
                (key, existing) => { existing.Add(leaseInfo.LeaseKey); return existing; });

            // 添加到过期队列
            m_lock.EnterWriteLock();
            try
            {
                if (!m_expirationQueue.ContainsKey(leaseInfo.ExpirationTime))
                {
                    m_expirationQueue[leaseInfo.ExpirationTime] = new List<Guid>();
                }
                m_expirationQueue[leaseInfo.ExpirationTime].Add(leaseInfo.LeaseKey);
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        private void RemoveFromIndexes(LeaseInfo leaseInfo)
        {
            // 从文件索引移除
            if (m_fileLeases.TryGetValue(leaseInfo.FileId, out var fileLeases))
            {
                fileLeases.Remove(leaseInfo.LeaseKey);
                if (fileLeases.Count == 0)
                {
                    m_fileLeases.TryRemove(leaseInfo.FileId, out _);
                }
            }

            // 从会话索引移除
            if (m_sessionLeases.TryGetValue(leaseInfo.SessionId, out var sessionLeases))
            {
                sessionLeases.Remove(leaseInfo.LeaseKey);
                if (sessionLeases.Count == 0)
                {
                    m_sessionLeases.TryRemove(leaseInfo.SessionId, out _);
                }
            }

            // 从过期队列移除
            m_lock.EnterWriteLock();
            try
            {
                if (m_expirationQueue.TryGetValue(leaseInfo.ExpirationTime, out var expiredLeases))
                {
                    expiredLeases.Remove(leaseInfo.LeaseKey);
                    if (expiredLeases.Count == 0)
                    {
                        m_expirationQueue.Remove(leaseInfo.ExpirationTime);
                    }
                }
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        public void Dispose()
        {
            m_cleanupTimer?.Dispose();
            m_lock?.Dispose();
        }
    }
}
```

### 阶段 2: SMB2 命令实现

#### 2.1 实现租赁中断请求

**LeaseBreakRequest.cs**
```csharp
using System;
using System.Collections.Generic;
using Utilities;

namespace SMBLibrary.SMB2
{
    public class LeaseBreakRequest : SMB2Command
    {
        public const int FixedLength = 24;

        private ushort StructureSize;
        public ushort Reserved;
        public Guid LeaseKey;
        public LeaseState CurrentLeaseState;
        public LeaseState NewLeaseState;
        public LeaseFlags LeaseFlags;
        public ulong LeaseDuration;

        public LeaseBreakRequest() : base(SMB2CommandName.OplockBreak)
        {
            StructureSize = FixedLength;
        }

        public LeaseBreakRequest(byte[] buffer, int offset) : base(buffer, offset)
        {
            StructureSize = LittleEndianConverter.ToUInt16(buffer, offset + SMB2Header.Length + 0);
            Reserved = LittleEndianConverter.ToUInt16(buffer, offset + SMB2Header.Length + 2);
            LeaseKey = ByteReader.ReadGuid(buffer, offset + SMB2Header.Length + 4);
            CurrentLeaseState = (LeaseState)LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 20);
            NewLeaseState = (LeaseState)LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 24);
            LeaseFlags = (LeaseFlags)LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 28);
            LeaseDuration = LittleEndianConverter.ToUInt64(buffer, offset + SMB2Header.Length + 32);
        }

        public override void WriteCommandBytes(byte[] buffer, int offset)
        {
            LittleEndianWriter.WriteUInt16(buffer, offset + 0, StructureSize);
            LittleEndianWriter.WriteUInt16(buffer, offset + 2, Reserved);
            ByteWriter.WriteGuid(buffer, offset + 4, LeaseKey);
            LittleEndianWriter.WriteUInt32(buffer, offset + 20, (uint)CurrentLeaseState);
            LittleEndianWriter.WriteUInt32(buffer, offset + 24, (uint)NewLeaseState);
            LittleEndianWriter.WriteUInt32(buffer, offset + 28, (uint)LeaseFlags);
            LittleEndianWriter.WriteUInt64(buffer, offset + 32, LeaseDuration);
        }

        public override int CommandLength => FixedLength;
    }
}
```

#### 2.2 实现租赁中断响应

**LeaseBreakResponse.cs**
```csharp
using System;
using Utilities;

namespace SMBLibrary.SMB2
{
    public class LeaseBreakResponse : SMB2Command
    {
        public const int FixedLength = 24;

        private ushort StructureSize;
        public ushort Reserved;
        public Guid LeaseKey;
        public LeaseState CurrentLeaseState;
        public LeaseState NewLeaseState;
        public LeaseFlags LeaseFlags;
        public ulong LeaseDuration;

        public LeaseBreakResponse() : base(SMB2CommandName.OplockBreak)
        {
            StructureSize = FixedLength;
        }

        public LeaseBreakResponse(byte[] buffer, int offset) : base(buffer, offset)
        {
            StructureSize = LittleEndianConverter.ToUInt16(buffer, offset + SMB2Header.Length + 0);
            Reserved = LittleEndianConverter.ToUInt16(buffer, offset + SMB2Header.Length + 2);
            LeaseKey = ByteReader.ReadGuid(buffer, offset + SMB2Header.Length + 4);
            CurrentLeaseState = (LeaseState)LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 20);
            NewLeaseState = (LeaseState)LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 24);
            LeaseFlags = (LeaseFlags)LittleEndianConverter.ToUInt32(buffer, offset + SMB2Header.Length + 28);
            LeaseDuration = LittleEndianConverter.ToUInt64(buffer, offset + SMB2Header.Length + 32);
        }

        public override void WriteCommandBytes(byte[] buffer, int offset)
        {
            LittleEndianWriter.WriteUInt16(buffer, offset + 0, StructureSize);
            LittleEndianWriter.WriteUInt16(buffer, offset + 2, Reserved);
            ByteWriter.WriteGuid(buffer, offset + 4, LeaseKey);
            LittleEndianWriter.WriteUInt32(buffer, offset + 20, (uint)CurrentLeaseState);
            LittleEndianWriter.WriteUInt32(buffer, offset + 24, (uint)NewLeaseState);
            LittleEndianWriter.WriteUInt32(buffer, offset + 28, (uint)LeaseFlags);
            LittleEndianWriter.WriteUInt64(buffer, offset + 32, LeaseDuration);
        }

        public override int CommandLength => FixedLength;
    }
}
```

### 阶段 3: 租赁上下文处理

#### 3.1 实现租赁上下文

**LeaseContext.cs**
```csharp
using System;
using System.Collections.Generic;
using Utilities;

namespace SMBLibrary.SMB2
{
    public class LeaseContext : CreateContext
    {
        public const string ContextName = "RqLs";

        public Guid LeaseKey;
        public LeaseState LeaseState;
        public LeaseFlags LeaseFlags;
        public ulong LeaseDuration;

        public LeaseContext()
        {
            Name = ContextName;
        }

        public LeaseContext(byte[] buffer, int offset) : base(buffer, offset)
        {
            Name = ContextName;
            LeaseKey = ByteReader.ReadGuid(buffer, offset + 8);
            LeaseState = (LeaseState)LittleEndianConverter.ToUInt32(buffer, offset + 24);
            LeaseFlags = (LeaseFlags)LittleEndianConverter.ToUInt32(buffer, offset + 28);
            LeaseDuration = LittleEndianConverter.ToUInt64(buffer, offset + 32);
        }

        public override void WriteCreateContextBytes(byte[] buffer, int offset)
        {
            base.WriteCreateContextBytes(buffer, offset);
            ByteWriter.WriteGuid(buffer, offset + 8, LeaseKey);
            LittleEndianWriter.WriteUInt32(buffer, offset + 24, (uint)LeaseState);
            LittleEndianWriter.WriteUInt32(buffer, offset + 28, (uint)LeaseFlags);
            LittleEndianWriter.WriteUInt64(buffer, offset + 32, LeaseDuration);
        }

        public override int CreateContextLength => 40;
    }
}
```

#### 3.2 实现租赁上下文处理器

**LeaseContextHandler.cs**
```csharp
using System;
using System.Collections.Generic;
using SMBLibrary.SMB2;

namespace SMBLibrary.Server.Leasing
{
    public class LeaseContextHandler
    {
        private readonly LeaseManager m_leaseManager;

        public LeaseContextHandler(LeaseManager leaseManager)
        {
            m_leaseManager = leaseManager;
        }

        public LeaseContext ProcessCreateContext(CreateContext context, ulong sessionId, FileID fileId, string filePath)
        {
            if (!(context is LeaseContext leaseContext))
            {
                return null;
            }

            // 验证租赁请求
            if (!ValidateLeaseRequest(leaseContext))
            {
                throw new LeaseException("Invalid lease request", 
                    leaseContext.LeaseKey, LeaseErrorCode.LeaseInvalid);
            }

            // 创建租赁
            var request = new LeaseRequest
            {
                LeaseKey = leaseContext.LeaseKey,
                LeaseState = leaseContext.LeaseState,
                LeaseFlags = leaseContext.LeaseFlags,
                LeaseDuration = TimeSpan.FromMilliseconds(leaseContext.LeaseDuration),
                SessionId = sessionId,
                FileId = fileId,
                FilePath = filePath
            };

            var leaseInfo = m_leaseManager.CreateLease(request);

            // 生成响应上下文
            return GenerateResponseContext(leaseInfo);
        }

        public LeaseContext GenerateResponseContext(LeaseInfo leaseInfo)
        {
            return new LeaseContext
            {
                LeaseKey = leaseInfo.LeaseKey,
                LeaseState = leaseInfo.State,
                LeaseFlags = leaseInfo.Flags,
                LeaseDuration = (ulong)leaseInfo.RemainingTime.TotalMilliseconds
            };
        }

        private bool ValidateLeaseRequest(LeaseContext context)
        {
            // 验证租赁键
            if (context.LeaseKey == Guid.Empty)
            {
                return false;
            }

            // 验证租赁状态
            if (context.LeaseState == LeaseState.None)
            {
                return false;
            }

            // 注意：根据 MS-SMB2 规范，客户端必须发送 LeaseDuration = 0
            // 服务器应该忽略客户端发送的值，使用自己的默认持续时间
            // 因此这里不验证 LeaseDuration

            return true;
        }
    }
}
```

### 阶段 4: 服务器集成

#### 4.1 修改 SMB2Session

在 `SMB2Session.cs` 中添加租赁支持：

```csharp
// 在 SMB2Session 类中添加
private LeaseManager m_leaseManager;
private LeaseContextHandler m_leaseContextHandler;

public SMB2Session(SMB2ConnectionState connection, ulong sessionID, string userName, string machineName, byte[] sessionKey, object accessToken, bool signingRequired, byte[] signingKey)
{
    // ... 现有代码 ...
    
    // 初始化租赁管理器
    var config = new LeaseManagerConfiguration
    {
        MaxLeases = 1000,
        DefaultLeaseDuration = TimeSpan.FromMinutes(30),
        LeaseBreakTimeout = TimeSpan.FromSeconds(30)
    };
    
    m_leaseManager = new LeaseManager(config);
    m_leaseContextHandler = new LeaseContextHandler(m_leaseManager);
    
    // 订阅租赁事件
    m_leaseManager.LeaseBreakRequested += OnLeaseBreakRequested;
    m_leaseManager.LeaseExpired += OnLeaseExpired;
}

public LeaseManager LeaseManager => m_leaseManager;
public LeaseContextHandler LeaseContextHandler => m_leaseContextHandler;

private void OnLeaseBreakRequested(object sender, LeaseBreakEventArgs e)
{
    // 发送租赁中断通知
    SendLeaseBreakNotification(e.LeaseKey, e.Reason);
}

private void OnLeaseExpired(object sender, LeaseExpiredEventArgs e)
{
    // 处理租赁过期
    Console.WriteLine($"Lease expired: {e.LeaseInfo.LeaseKey}");
}

private void SendLeaseBreakNotification(Guid leaseKey, LeaseBreakReason reason)
{
    var notification = new LeaseBreakRequest
    {
        LeaseKey = leaseKey,
        CurrentLeaseState = LeaseState.ReadCaching, // 根据实际状态设置
        NewLeaseState = LeaseState.None,
        LeaseFlags = LeaseFlags.BreakInProgress,
        LeaseDuration = 0
    };
    
    // 发送通知到客户端
    // 这里需要根据实际的网络发送机制实现
}
```

#### 4.2 修改 Create 命令处理

在 `SMBServer.SMB2.cs` 中修改 Create 命令处理：

```csharp
// 在 Create 命令处理中添加租赁支持
private void HandleCreateRequest(SMB2ConnectionState connectionState, SMB2Session session, CreateRequest request, CreateResponse response)
{
    // ... 现有代码 ...
    
    // 处理租赁上下文
    LeaseContext leaseResponseContext = null;
    foreach (var context in request.CreateContexts)
    {
        if (context is LeaseContext leaseContext)
        {
            try
            {
                leaseResponseContext = session.LeaseContextHandler.ProcessCreateContext(
                    leaseContext, session.SessionID, fileID, request.Name);
            }
            catch (LeaseException ex)
            {
                response.Header.Status = ConvertLeaseErrorToNTStatus(ex.ErrorCode);
                return;
            }
        }
    }
    
    // 添加租赁响应上下文
    if (leaseResponseContext != null)
    {
        response.CreateContexts.Add(leaseResponseContext);
    }
    
    // ... 现有代码 ...
}

private NTStatus ConvertLeaseErrorToNTStatus(LeaseErrorCode errorCode)
{
    return errorCode switch
    {
        LeaseErrorCode.LeaseNotFound => NTStatus.STATUS_OBJECT_NAME_NOT_FOUND,
        LeaseErrorCode.LeaseExpired => NTStatus.STATUS_OBJECT_NAME_NOT_FOUND,
        LeaseErrorCode.LeaseInvalid => NTStatus.STATUS_INVALID_PARAMETER,
        LeaseErrorCode.LeaseResourceExhausted => NTStatus.STATUS_INSUFFICIENT_RESOURCES,
        _ => NTStatus.STATUS_UNSUCCESSFUL
    };
}
```

### 阶段 5: 客户端实现

#### 5.1 实现客户端租赁缓存

**LeaseCache.cs**
```csharp
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using SMBLibrary.SMB2;

namespace SMBLibrary.Client.Leasing
{
    public class LeaseCache
    {
        private readonly ConcurrentDictionary<FileID, CachedFileData> m_fileCache;
        private readonly ConcurrentDictionary<FileID, LeaseInfo> m_fileLeases;
        private readonly ConcurrentDictionary<FileID, CacheMetadata> m_cacheMetadata;

        public LeaseCache()
        {
            m_fileCache = new ConcurrentDictionary<FileID, CachedFileData>();
            m_fileLeases = new ConcurrentDictionary<FileID, LeaseInfo>();
            m_cacheMetadata = new ConcurrentDictionary<FileID, CacheMetadata>();
        }

        public void CacheFileData(FileID fileId, byte[] data, LeaseInfo leaseInfo)
        {
            var cachedData = new CachedFileData
            {
                Data = data,
                CachedTime = DateTime.UtcNow,
                LeaseInfo = leaseInfo
            };

            m_fileCache.TryAdd(fileId, cachedData);
            m_fileLeases.TryAdd(fileId, leaseInfo);
            
            var metadata = new CacheMetadata
            {
                FileId = fileId,
                CachedTime = DateTime.UtcNow,
                AccessCount = 0,
                LastAccessTime = DateTime.UtcNow
            };
            m_cacheMetadata.TryAdd(fileId, metadata);
        }

        public byte[] GetCachedData(FileID fileId)
        {
            if (m_fileCache.TryGetValue(fileId, out var cachedData))
            {
                if (m_cacheMetadata.TryGetValue(fileId, out var metadata))
                {
                    metadata.AccessCount++;
                    metadata.LastAccessTime = DateTime.UtcNow;
                }
                return cachedData.Data;
            }
            return null;
        }

        public bool IsCached(FileID fileId)
        {
            return m_fileCache.ContainsKey(fileId);
        }

        public void InvalidateCache(FileID fileId)
        {
            m_fileCache.TryRemove(fileId, out _);
            m_fileLeases.TryRemove(fileId, out _);
            m_cacheMetadata.TryRemove(fileId, out _);
        }

        public void HandleLeaseBreak(LeaseBreakNotification notification)
        {
            if (m_fileLeases.TryGetValue(notification.FileId, out var leaseInfo))
            {
                if (leaseInfo.LeaseKey == notification.LeaseKey)
                {
                    InvalidateCache(notification.FileId);
                }
            }
        }

        public List<FileID> GetCachedFiles()
        {
            return new List<FileID>(m_fileCache.Keys);
        }

        public void ClearCache()
        {
            m_fileCache.Clear();
            m_fileLeases.Clear();
            m_cacheMetadata.Clear();
        }
    }

    public class CachedFileData
    {
        public byte[] Data { get; set; }
        public DateTime CachedTime { get; set; }
        public LeaseInfo LeaseInfo { get; set; }
    }

    public class CacheMetadata
    {
        public FileID FileId { get; set; }
        public DateTime CachedTime { get; set; }
        public int AccessCount { get; set; }
        public DateTime LastAccessTime { get; set; }
    }
}
```

#### 5.2 修改 SMB2FileStore

在 `SMB2FileStore.cs` 中添加租赁支持：

```csharp
// 在 SMB2FileStore 类中添加
private LeaseCache m_leaseCache;
private LeaseClientManager m_leaseManager;

public SMB2FileStore(SMB2Client client, uint treeID, bool encryptShareData)
{
    // ... 现有代码 ...
    
    m_leaseCache = new LeaseCache();
    m_leaseManager = new LeaseClientManager(client, m_leaseCache);
}

public NTStatus ReadFile(out byte[] data, object handle, long offset, int maxCount)
{
    var fileId = (FileID)handle;
    
    // 检查是否有缓存数据
    if (m_leaseCache.IsCached(fileId))
    {
        var cachedData = m_leaseCache.GetCachedData(fileId);
        if (cachedData != null && offset == 0 && maxCount <= cachedData.Length)
        {
            data = new byte[maxCount];
            Array.Copy(cachedData, offset, data, 0, maxCount);
            return NTStatus.STATUS_SUCCESS;
        }
    }
    
    // 从服务器读取数据
    data = null;
    ReadRequest request = new ReadRequest();
    request.Header.CreditCharge = (ushort)Math.Ceiling((double)maxCount / BytesPerCredit);
    request.FileId = fileId;
    request.Offset = (ulong)offset;
    request.ReadLength = (uint)maxCount;
    
    TrySendCommand(request);
    SMB2Command response = m_client.WaitForCommand(request.MessageID, out bool connectionTerminated);
    if (response != null)
    {
        if (response.Header.Status == NTStatus.STATUS_SUCCESS && response is ReadResponse)
        {
            data = ((ReadResponse)response).Data;
            
            // 缓存数据（如果支持租赁）
            if (m_leaseManager.HasLease(fileId))
            {
                m_leaseCache.CacheFileData(fileId, data, m_leaseManager.GetLeaseInfo(fileId));
            }
        }
        return response.Header.Status;
    }

    return connectionTerminated ? NTStatus.STATUS_INVALID_SMB : NTStatus.STATUS_IO_TIMEOUT;
}
```

## 测试实现

### 单元测试示例

**LeaseManagerTests.cs**
```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SMBLibrary.Server.Leasing;
using SMBLibrary.SMB2;
using System;
using System.Threading;

namespace SMBLibrary.Tests.Leasing
{
    [TestClass]
    public class LeaseManagerTests
    {
        private LeaseManager m_leaseManager;

        [TestInitialize]
        public void Setup()
        {
            var config = new LeaseManagerConfiguration
            {
                MaxLeases = 100,
                DefaultLeaseDuration = TimeSpan.FromMinutes(1),
                LeaseBreakTimeout = TimeSpan.FromSeconds(10)
            };
            m_leaseManager = new LeaseManager(config);
        }

        [TestCleanup]
        public void Cleanup()
        {
            m_leaseManager?.Dispose();
        }

        [TestMethod]
        public void CreateLease_Success()
        {
            // Arrange
            var request = new LeaseRequest
            {
                LeaseKey = Guid.NewGuid(),
                LeaseState = LeaseState.ReadCaching,
                FilePath = "/test/file.txt",
                SessionId = 12345,
                FileId = new FileID { Volatile = 1, Persistent = 1 },
                LeaseDuration = TimeSpan.FromMinutes(30)
            };

            // Act
            var lease = m_leaseManager.CreateLease(request);

            // Assert
            Assert.IsNotNull(lease);
            Assert.AreEqual(request.LeaseKey, lease.LeaseKey);
            Assert.AreEqual(request.LeaseState, lease.State);
            Assert.AreEqual(request.FilePath, lease.FilePath);
        }

        [TestMethod]
        public void BreakLease_Success()
        {
            // Arrange
            var request = new LeaseRequest
            {
                LeaseKey = Guid.NewGuid(),
                LeaseState = LeaseState.ReadCaching,
                FilePath = "/test/file.txt",
                SessionId = 12345,
                FileId = new FileID { Volatile = 1, Persistent = 1 },
                LeaseDuration = TimeSpan.FromMinutes(30)
            };
            var lease = m_leaseManager.CreateLease(request);

            // Act
            m_leaseManager.BreakLease(lease.LeaseKey, LeaseBreakReason.WriteRequest);

            // Assert
            var updatedLease = m_leaseManager.GetLeaseInfo(lease.LeaseKey);
            Assert.IsNotNull(updatedLease);
            Assert.AreEqual(LeaseBreakReason.WriteRequest, updatedLease.PendingBreakReason);
        }

        [TestMethod]
        public void GetLeasesBySession_Success()
        {
            // Arrange
            var sessionId = 12345UL;
            var request1 = new LeaseRequest
            {
                LeaseKey = Guid.NewGuid(),
                LeaseState = LeaseState.ReadCaching,
                FilePath = "/test/file1.txt",
                SessionId = sessionId,
                FileId = new FileID { Volatile = 1, Persistent = 1 },
                LeaseDuration = TimeSpan.FromMinutes(30)
            };
            var request2 = new LeaseRequest
            {
                LeaseKey = Guid.NewGuid(),
                LeaseState = LeaseState.ReadCaching,
                FilePath = "/test/file2.txt",
                SessionId = sessionId,
                FileId = new FileID { Volatile = 2, Persistent = 2 },
                LeaseDuration = TimeSpan.FromMinutes(30)
            };
            m_leaseManager.CreateLease(request1);
            m_leaseManager.CreateLease(request2);

            // Act
            var leases = m_leaseManager.GetLeasesBySession(sessionId);

            // Assert
            Assert.AreEqual(2, leases.Count);
        }
    }
}
```

## 性能优化

### 1. 内存优化

```csharp
// 使用对象池减少分配
public class LeaseInfoPool
{
    private readonly ConcurrentQueue<LeaseInfo> m_pool;
    private readonly int m_maxPoolSize;

    public LeaseInfoPool(int maxPoolSize = 1000)
    {
        m_pool = new ConcurrentQueue<LeaseInfo>();
        m_maxPoolSize = maxPoolSize;
    }

    public LeaseInfo Rent()
    {
        if (m_pool.TryDequeue(out var leaseInfo))
        {
            return leaseInfo;
        }
        return new LeaseInfo();
    }

    public void Return(LeaseInfo leaseInfo)
    {
        if (m_pool.Count < m_maxPoolSize)
        {
            // 重置对象状态
            leaseInfo.LeaseKey = Guid.Empty;
            leaseInfo.State = LeaseState.None;
            leaseInfo.Flags = LeaseFlags.None;
            leaseInfo.CreatedTime = DateTime.MinValue;
            leaseInfo.ExpirationTime = DateTime.MinValue;
            leaseInfo.SessionId = 0;
            leaseInfo.FileId = new FileID();
            leaseInfo.FilePath = null;
            leaseInfo.PendingBreakReason = null;
            leaseInfo.LastAccessTime = DateTime.MinValue;
            leaseInfo.AccessCount = 0;

            m_pool.Enqueue(leaseInfo);
        }
    }
}
```

### 2. 并发优化

```csharp
// 使用读写锁优化并发性能
public class ConcurrentLeaseManager
{
    private readonly ReaderWriterLockSlim m_lock;
    private readonly Dictionary<Guid, LeaseInfo> m_leases;

    public ConcurrentLeaseManager()
    {
        m_lock = new ReaderWriterLockSlim();
        m_leases = new Dictionary<Guid, LeaseInfo>();
    }

    public LeaseInfo GetLeaseInfo(Guid leaseKey)
    {
        m_lock.EnterReadLock();
        try
        {
            m_leases.TryGetValue(leaseKey, out var leaseInfo);
            return leaseInfo;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    public void CreateLease(LeaseInfo leaseInfo)
    {
        m_lock.EnterWriteLock();
        try
        {
            m_leases[leaseInfo.LeaseKey] = leaseInfo;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }
}
```

## 最佳实践

### 1. 错误处理

```csharp
public class LeaseOperationResult<T>
{
    public bool Success { get; set; }
    public T Result { get; set; }
    public string ErrorMessage { get; set; }
    public LeaseErrorCode ErrorCode { get; set; }

    public static LeaseOperationResult<T> CreateSuccess(T result)
    {
        return new LeaseOperationResult<T>
        {
            Success = true,
            Result = result
        };
    }

    public static LeaseOperationResult<T> CreateError(string message, LeaseErrorCode errorCode)
    {
        return new LeaseOperationResult<T>
        {
            Success = false,
            ErrorMessage = message,
            ErrorCode = errorCode
        };
    }
}
```

### 2. 配置管理

```csharp
public class LeaseConfiguration
{
    public int MaxLeases { get; set; } = 10000;
    public TimeSpan DefaultLeaseDuration { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan LeaseBreakTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
    public bool EnableLeaseBreakNotifications { get; set; } = true;
    public bool EnableLeaseExpirationEvents { get; set; } = true;
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    public void Validate()
    {
        if (MaxLeases <= 0)
            throw new ArgumentException("MaxLeases must be positive");
        if (DefaultLeaseDuration <= TimeSpan.Zero)
            throw new ArgumentException("DefaultLeaseDuration must be positive");
        if (LeaseBreakTimeout <= TimeSpan.Zero)
            throw new ArgumentException("LeaseBreakTimeout must be positive");
    }
}
```

## 关键机制说明：过期与中断

### 1. 自然过期 (Expiration) vs 强制中断 (Lease Break)

理解这两者的区别对于正确实现和调试至关重要：

| 特性 | 自然过期 (Expiration) | 强制中断 (Lease Break) |
|------|----------------------|-----------------------|
| **触发条件** | 租赁时间到达（例如 5秒后） | 发生冲突（例如其他客户端写入文件） |
| **服务端行为** | 静默移除租赁，**不发送任何网络包** | **必须**主动发送 `Oplock/Lease Break` 通知 |
| **客户端行为** | 内部计时器到期，自动失效缓存，下次访问发起新请求 | 收到通知后，立即失效缓存或降级租赁 |
| **调试现象** | Wireshark 中看不到服务端发包，但能看到客户端在过期后发起新请求 | Wireshark 中能看到服务端发送 `OplockBreak` 通知 |

### 2. 常见误区

*   **误区**：认为租赁到期时，服务端应该发送通知告诉客户端。
*   **事实**：SMB 协议设计中，过期时间是在建立租赁时协商好的（`LeaseDuration`）。客户端和服务端各自维护计时器。时间一到，双方自动解除契约，无需网络通信。

*   **误区**：设置了过期时间，但客户端一直不刷新。
*   **原因**：可能是服务端返回的 `LeaseDuration` 计算错误（例如负数溢出导致返回了极大的值），导致客户端认为租赁永不过期。务必确保 `LeaseDuration` 不会溢出。

## 总结

本实现指南提供了完整的 SMB 2.0/2.1 租赁协议实现方案，包括：

1. **基础架构**: 租赁管理器和相关数据结构
2. **SMB2 命令**: 租赁中断请求和响应
3. **上下文处理**: 租赁上下文的创建和处理
4. **服务器集成**: 与现有 SMBLibrary 架构的集成
5. **客户端实现**: 租赁缓存和客户端支持
6. **测试策略**: 单元测试和集成测试
7. **性能优化**: 内存和并发优化
8. **最佳实践**: 错误处理和配置管理

通过遵循本指南，可以成功实现完整的 SMB 2.0/2.1 租赁协议支持，为 SMBLibrary 提供高性能的客户端缓存功能。
