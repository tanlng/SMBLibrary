# SMB 2.0/2.1 租赁协议性能优化指南

## 概述

本文档详细描述了 SMB 2.0/2.1 租赁协议的性能优化策略和实现方法。通过合理的性能优化，可以显著提高租赁协议的执行效率，减少内存使用，降低网络开销，提升整体系统性能。

## 性能目标

### 主要目标
- **低延迟**: 租赁操作响应时间 < 1ms
- **高吞吐量**: 支持 10,000+ 并发租赁
- **低内存使用**: 每个租赁 < 200 字节内存
- **高效网络**: 减少 50% 的网络传输
- **快速缓存**: 缓存命中率 > 90%

### 性能指标
- **租赁创建时间**: < 0.1ms
- **租赁中断时间**: < 0.5ms
- **缓存访问时间**: < 0.01ms
- **内存分配**: 最小化动态分配
- **CPU 使用率**: < 5% 额外开销

## 内存优化

### 1. 对象池模式

#### 1.1 租赁信息对象池

```csharp
public class LeaseInfoPool
{
    private readonly ConcurrentQueue<LeaseInfo> m_pool;
    private readonly int m_maxPoolSize;
    private readonly object m_lock;
    private int m_currentSize;

    public LeaseInfoPool(int maxPoolSize = 10000)
    {
        m_pool = new ConcurrentQueue<LeaseInfo>();
        m_maxPoolSize = maxPoolSize;
        m_lock = new object();
        m_currentSize = 0;
    }

    public LeaseInfo Rent()
    {
        if (m_pool.TryDequeue(out var leaseInfo))
        {
            return leaseInfo;
        }

        lock (m_lock)
        {
            if (m_currentSize < m_maxPoolSize)
            {
                m_currentSize++;
                return new LeaseInfo();
            }
        }

        return new LeaseInfo();
    }

    public void Return(LeaseInfo leaseInfo)
    {
        if (leaseInfo == null) return;

        // 重置对象状态
        ResetLeaseInfo(leaseInfo);

        if (m_pool.Count < m_maxPoolSize)
        {
            m_pool.Enqueue(leaseInfo);
        }
        else
        {
            lock (m_lock)
            {
                if (m_currentSize > 0)
                {
                    m_currentSize--;
                }
            }
        }
    }

    private void ResetLeaseInfo(LeaseInfo leaseInfo)
    {
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
    }
}
```

#### 1.2 字节数组池

```csharp
public class ByteArrayPool
{
    private readonly ConcurrentQueue<byte[]> m_pool;
    private readonly int m_bufferSize;
    private readonly int m_maxPoolSize;

    public ByteArrayPool(int bufferSize = 8192, int maxPoolSize = 1000)
    {
        m_pool = new ConcurrentQueue<byte[]>();
        m_bufferSize = bufferSize;
        m_maxPoolSize = maxPoolSize;
    }

    public byte[] Rent()
    {
        if (m_pool.TryDequeue(out var buffer))
        {
            return buffer;
        }
        return new byte[m_bufferSize];
    }

    public void Return(byte[] buffer)
    {
        if (buffer != null && buffer.Length == m_bufferSize && m_pool.Count < m_maxPoolSize)
        {
            Array.Clear(buffer, 0, buffer.Length);
            m_pool.Enqueue(buffer);
        }
    }
}
```

### 2. 内存预分配

#### 2.1 预分配集合

```csharp
public class PreAllocatedCollections
{
    private readonly ConcurrentQueue<List<LeaseInfo>> m_leaseLists;
    private readonly ConcurrentQueue<Dictionary<Guid, LeaseInfo>> m_leaseDictionaries;
    private readonly ConcurrentQueue<HashSet<Guid>> m_leaseHashSets;

    public PreAllocatedCollections()
    {
        m_leaseLists = new ConcurrentQueue<List<LeaseInfo>>();
        m_leaseDictionaries = new ConcurrentQueue<Dictionary<Guid, LeaseInfo>>();
        m_leaseHashSets = new ConcurrentQueue<HashSet<Guid>>();
    }

    public List<LeaseInfo> RentList()
    {
        if (m_leaseLists.TryDequeue(out var list))
        {
            list.Clear();
            return list;
        }
        return new List<LeaseInfo>();
    }

    public void ReturnList(List<LeaseInfo> list)
    {
        if (list != null && m_leaseLists.Count < 100)
        {
            m_leaseLists.Enqueue(list);
        }
    }

    public Dictionary<Guid, LeaseInfo> RentDictionary()
    {
        if (m_leaseDictionaries.TryDequeue(out var dict))
        {
            dict.Clear();
            return dict;
        }
        return new Dictionary<Guid, LeaseInfo>();
    }

    public void ReturnDictionary(Dictionary<Guid, LeaseInfo> dict)
    {
        if (dict != null && m_leaseDictionaries.Count < 100)
        {
            m_leaseDictionaries.Enqueue(dict);
        }
    }
}
```

### 3. 结构体优化

#### 3.1 使用结构体替代类

```csharp
public struct LeaseInfoStruct
{
    public Guid LeaseKey;
    public uint State;
    public uint Flags;
    public long CreatedTimeTicks;
    public long ExpirationTimeTicks;
    public ulong SessionId;
    public ulong FileIdVolatile;
    public ulong FileIdPersistent;
    public IntPtr FilePathPtr;
    public uint PendingBreakReason;
    public long LastAccessTimeTicks;
    public int AccessCount;

    public DateTime CreatedTime
    {
        get => new DateTime(CreatedTimeTicks);
        set => CreatedTimeTicks = value.Ticks;
    }

    public DateTime ExpirationTime
    {
        get => new DateTime(ExpirationTimeTicks);
        set => ExpirationTimeTicks = value.Ticks;
    }

    public bool IsExpired => DateTime.UtcNow.Ticks > ExpirationTimeTicks;
}
```

## 并发优化

### 1. 无锁数据结构

#### 1.1 无锁租赁注册表

```csharp
public class LockFreeLeaseRegistry
{
    private readonly ConcurrentDictionary<Guid, LeaseInfo> m_leases;
    private readonly ConcurrentDictionary<FileID, ConcurrentBag<Guid>> m_fileLeases;
    private readonly ConcurrentDictionary<ulong, ConcurrentBag<Guid>> m_sessionLeases;

    public LockFreeLeaseRegistry()
    {
        m_leases = new ConcurrentDictionary<Guid, LeaseInfo>();
        m_fileLeases = new ConcurrentDictionary<FileID, ConcurrentBag<Guid>>();
        m_sessionLeases = new ConcurrentDictionary<ulong, ConcurrentBag<Guid>>();
    }

    public bool TryAddLease(LeaseInfo leaseInfo)
    {
        if (!m_leases.TryAdd(leaseInfo.LeaseKey, leaseInfo))
        {
            return false;
        }

        // 添加到文件索引
        m_fileLeases.AddOrUpdate(leaseInfo.FileId,
            new ConcurrentBag<Guid> { leaseInfo.LeaseKey },
            (key, existing) => { existing.Add(leaseInfo.LeaseKey); return existing; });

        // 添加到会话索引
        m_sessionLeases.AddOrUpdate(leaseInfo.SessionId,
            new ConcurrentBag<Guid> { leaseInfo.LeaseKey },
            (key, existing) => { existing.Add(leaseInfo.LeaseKey); return existing; });

        return true;
    }

    public bool TryGetLease(Guid leaseKey, out LeaseInfo leaseInfo)
    {
        return m_leases.TryGetValue(leaseKey, out leaseInfo);
    }

    public bool TryRemoveLease(Guid leaseKey)
    {
        if (!m_leases.TryRemove(leaseKey, out var leaseInfo))
        {
            return false;
        }

        // 从文件索引移除
        if (m_fileLeases.TryGetValue(leaseInfo.FileId, out var fileLeases))
        {
            var newFileLeases = new ConcurrentBag<Guid>();
            foreach (var key in fileLeases)
            {
                if (key != leaseKey)
                {
                    newFileLeases.Add(key);
                }
            }
            m_fileLeases.TryUpdate(leaseInfo.FileId, newFileLeases, fileLeases);
        }

        // 从会话索引移除
        if (m_sessionLeases.TryGetValue(leaseInfo.SessionId, out var sessionLeases))
        {
            var newSessionLeases = new ConcurrentBag<Guid>();
            foreach (var key in sessionLeases)
            {
                if (key != leaseKey)
                {
                    newSessionLeases.Add(key);
                }
            }
            m_sessionLeases.TryUpdate(leaseInfo.SessionId, newSessionLeases, sessionLeases);
        }

        return true;
    }
}
```

#### 1.2 无锁缓存

```csharp
public class LockFreeLeaseCache
{
    private readonly ConcurrentDictionary<FileID, CachedData> m_cache;
    private readonly ConcurrentDictionary<FileID, DateTime> m_accessTimes;
    private readonly long m_maxCacheSize;
    private long m_currentCacheSize;

    public LockFreeLeaseCache(long maxCacheSize = 100 * 1024 * 1024) // 100MB
    {
        m_cache = new ConcurrentDictionary<FileID, CachedData>();
        m_accessTimes = new ConcurrentDictionary<FileID, DateTime>();
        m_maxCacheSize = maxCacheSize;
        m_currentCacheSize = 0;
    }

    public bool TryGetCachedData(FileID fileId, out byte[] data)
    {
        data = null;
        if (m_cache.TryGetValue(fileId, out var cachedData))
        {
            data = cachedData.Data;
            m_accessTimes.TryUpdate(fileId, DateTime.UtcNow, cachedData.LastAccessTime);
            return true;
        }
        return false;
    }

    public bool TryCacheData(FileID fileId, byte[] data, LeaseInfo leaseInfo)
    {
        var dataSize = data.Length;
        if (dataSize > m_maxCacheSize)
        {
            return false;
        }

        // 检查缓存大小限制
        while (m_currentCacheSize + dataSize > m_maxCacheSize)
        {
            if (!EvictOldestEntry())
            {
                return false;
            }
        }

        var cachedData = new CachedData
        {
            Data = data,
            LeaseInfo = leaseInfo,
            LastAccessTime = DateTime.UtcNow,
            Size = dataSize
        };

        if (m_cache.TryAdd(fileId, cachedData))
        {
            m_accessTimes.TryAdd(fileId, DateTime.UtcNow);
            Interlocked.Add(ref m_currentCacheSize, dataSize);
            return true;
        }

        return false;
    }

    private bool EvictOldestEntry()
    {
        var oldestTime = DateTime.MaxValue;
        FileID? oldestFileId = null;

        foreach (var kvp in m_accessTimes)
        {
            if (kvp.Value < oldestTime)
            {
                oldestTime = kvp.Value;
                oldestFileId = kvp.Key;
            }
        }

        if (oldestFileId.HasValue)
        {
            if (m_cache.TryRemove(oldestFileId.Value, out var cachedData))
            {
                m_accessTimes.TryRemove(oldestFileId.Value, out _);
                Interlocked.Add(ref m_currentCacheSize, -cachedData.Size);
                return true;
            }
        }

        return false;
    }
}

public struct CachedData
{
    public byte[] Data;
    public LeaseInfo LeaseInfo;
    public DateTime LastAccessTime;
    public int Size;
}
```

### 2. 读写锁优化

#### 2.1 分层读写锁

```csharp
public class HierarchicalReadWriteLock
{
    private readonly ReaderWriterLockSlim m_globalLock;
    private readonly Dictionary<Guid, ReaderWriterLockSlim> m_leaseLocks;
    private readonly object m_lockLock;

    public HierarchicalReadWriteLock()
    {
        m_globalLock = new ReaderWriterLockSlim();
        m_leaseLocks = new Dictionary<Guid, ReaderWriterLockSlim>();
        m_lockLock = new object();
    }

    public void EnterGlobalReadLock()
    {
        m_globalLock.EnterReadLock();
    }

    public void ExitGlobalReadLock()
    {
        m_globalLock.ExitReadLock();
    }

    public void EnterGlobalWriteLock()
    {
        m_globalLock.EnterWriteLock();
    }

    public void ExitGlobalWriteLock()
    {
        m_globalLock.ExitWriteLock();
    }

    public void EnterLeaseReadLock(Guid leaseKey)
    {
        m_globalLock.EnterReadLock();
        try
        {
            var leaseLock = GetOrCreateLeaseLock(leaseKey);
            leaseLock.EnterReadLock();
        }
        catch
        {
            m_globalLock.ExitReadLock();
            throw;
        }
    }

    public void ExitLeaseReadLock(Guid leaseKey)
    {
        var leaseLock = GetLeaseLock(leaseKey);
        if (leaseLock != null)
        {
            leaseLock.ExitReadLock();
        }
        m_globalLock.ExitReadLock();
    }

    private ReaderWriterLockSlim GetOrCreateLeaseLock(Guid leaseKey)
    {
        lock (m_lockLock)
        {
            if (!m_leaseLocks.TryGetValue(leaseKey, out var leaseLock))
            {
                leaseLock = new ReaderWriterLockSlim();
                m_leaseLocks[leaseKey] = leaseLock;
            }
            return leaseLock;
        }
    }

    private ReaderWriterLockSlim GetLeaseLock(Guid leaseKey)
    {
        lock (m_lockLock)
        {
            m_leaseLocks.TryGetValue(leaseKey, out var leaseLock);
            return leaseLock;
        }
    }
}
```

## 网络优化

### 1. 批量处理

#### 1.1 批量租赁中断

```csharp
public class BatchLeaseBreakProcessor
{
    private readonly Queue<LeaseBreakRequest> m_pendingBreaks;
    private readonly Timer m_batchTimer;
    private readonly int m_batchSize;
    private readonly TimeSpan m_batchTimeout;

    public BatchLeaseBreakProcessor(int batchSize = 100, TimeSpan batchTimeout = default)
    {
        m_pendingBreaks = new Queue<LeaseBreakRequest>();
        m_batchSize = batchSize;
        m_batchTimeout = batchTimeout == default ? TimeSpan.FromMilliseconds(50) : batchTimeout;
        m_batchTimer = new Timer(ProcessBatch, null, m_batchTimeout, m_batchTimeout);
    }

    public void QueueLeaseBreak(LeaseBreakRequest request)
    {
        lock (m_pendingBreaks)
        {
            m_pendingBreaks.Enqueue(request);
            
            if (m_pendingBreaks.Count >= m_batchSize)
            {
                ProcessBatch(null);
            }
        }
    }

    private void ProcessBatch(object state)
    {
        List<LeaseBreakRequest> batch;
        lock (m_pendingBreaks)
        {
            if (m_pendingBreaks.Count == 0)
            {
                return;
            }

            batch = new List<LeaseBreakRequest>();
            while (m_pendingBreaks.Count > 0 && batch.Count < m_batchSize)
            {
                batch.Add(m_pendingBreaks.Dequeue());
            }
        }

        if (batch.Count > 0)
        {
            SendBatchLeaseBreaks(batch);
        }
    }

    private void SendBatchLeaseBreaks(List<LeaseBreakRequest> batch)
    {
        // 将多个租赁中断合并为一个网络请求
        var batchRequest = new BatchLeaseBreakRequest
        {
            BreakRequests = batch
        };

        // 发送批量请求
        SendNetworkRequest(batchRequest);
    }
}
```

#### 1.2 压缩网络数据

```csharp
public class CompressedLeaseContext
{
    public byte[] CompressLeaseContext(LeaseContext context)
    {
        var json = JsonConvert.SerializeObject(context);
        var bytes = Encoding.UTF8.GetBytes(json);
        
        using (var output = new MemoryStream())
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            gzip.Write(bytes, 0, bytes.Length);
            gzip.Close();
            return output.ToArray();
        }
    }

    public LeaseContext DecompressLeaseContext(byte[] compressedData)
    {
        using (var input = new MemoryStream(compressedData))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            gzip.CopyTo(output);
            var json = Encoding.UTF8.GetString(output.ToArray());
            return JsonConvert.DeserializeObject<LeaseContext>(json);
        }
    }
}
```

### 2. 异步处理

#### 2.1 异步租赁操作

```csharp
public class AsyncLeaseManager
{
    private readonly LeaseManager m_leaseManager;
    private readonly SemaphoreSlim m_semaphore;

    public AsyncLeaseManager(LeaseManager leaseManager, int maxConcurrency = 100)
    {
        m_leaseManager = leaseManager;
        m_semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public async Task<LeaseInfo> CreateLeaseAsync(LeaseRequest request)
    {
        await m_semaphore.WaitAsync();
        try
        {
            return await Task.Run(() => m_leaseManager.CreateLease(request));
        }
        finally
        {
            m_semaphore.Release();
        }
    }

    public async Task BreakLeaseAsync(Guid leaseKey, LeaseBreakReason reason)
    {
        await m_semaphore.WaitAsync();
        try
        {
            await Task.Run(() => m_leaseManager.BreakLease(leaseKey, reason));
        }
        finally
        {
            m_semaphore.Release();
        }
    }

    public async Task<List<LeaseInfo>> GetActiveLeasesAsync()
    {
        await m_semaphore.WaitAsync();
        try
        {
            return await Task.Run(() => m_leaseManager.GetActiveLeases());
        }
        finally
        {
            m_semaphore.Release();
        }
    }
}
```

## 缓存优化

### 1. 智能缓存策略

#### 1.1 LRU 缓存

```csharp
public class LRULeaseCache
{
    private readonly Dictionary<FileID, LinkedListNode<CacheEntry>> m_cache;
    private readonly LinkedList<CacheEntry> m_accessOrder;
    private readonly int m_maxSize;
    private readonly object m_lock;

    public LRULeaseCache(int maxSize = 10000)
    {
        m_cache = new Dictionary<FileID, LinkedListNode<CacheEntry>>();
        m_accessOrder = new LinkedList<CacheEntry>();
        m_maxSize = maxSize;
        m_lock = new object();
    }

    public bool TryGet(FileID fileId, out byte[] data)
    {
        lock (m_lock)
        {
            if (m_cache.TryGetValue(fileId, out var node))
            {
                // 移动到链表头部
                m_accessOrder.Remove(node);
                m_accessOrder.AddFirst(node);
                
                data = node.Value.Data;
                return true;
            }
        }

        data = null;
        return false;
    }

    public void Set(FileID fileId, byte[] data, LeaseInfo leaseInfo)
    {
        lock (m_lock)
        {
            if (m_cache.TryGetValue(fileId, out var existingNode))
            {
                // 更新现有条目
                existingNode.Value.Data = data;
                existingNode.Value.LeaseInfo = leaseInfo;
                existingNode.Value.LastAccessTime = DateTime.UtcNow;
                
                // 移动到链表头部
                m_accessOrder.Remove(existingNode);
                m_accessOrder.AddFirst(existingNode);
            }
            else
            {
                // 添加新条目
                if (m_cache.Count >= m_maxSize)
                {
                    // 移除最久未使用的条目
                    var lastNode = m_accessOrder.Last;
                    m_accessOrder.RemoveLast();
                    m_cache.Remove(lastNode.Value.FileId);
                }

                var newNode = new LinkedListNode<CacheEntry>(new CacheEntry
                {
                    FileId = fileId,
                    Data = data,
                    LeaseInfo = leaseInfo,
                    LastAccessTime = DateTime.UtcNow
                });

                m_cache[fileId] = newNode;
                m_accessOrder.AddFirst(newNode);
            }
        }
    }

    public void Remove(FileID fileId)
    {
        lock (m_lock)
        {
            if (m_cache.TryGetValue(fileId, out var node))
            {
                m_accessOrder.Remove(node);
                m_cache.Remove(fileId);
            }
        }
    }
}

public class CacheEntry
{
    public FileID FileId { get; set; }
    public byte[] Data { get; set; }
    public LeaseInfo LeaseInfo { get; set; }
    public DateTime LastAccessTime { get; set; }
}
```

#### 1.2 预取缓存

```csharp
public class PrefetchLeaseCache
{
    private readonly LRULeaseCache m_cache;
    private readonly Dictionary<FileID, List<FileID>> m_accessPatterns;
    private readonly object m_lock;

    public PrefetchLeaseCache(int maxSize = 10000)
    {
        m_cache = new LRULeaseCache(maxSize);
        m_accessPatterns = new Dictionary<FileID, List<FileID>>();
        m_lock = new object();
    }

    public bool TryGet(FileID fileId, out byte[] data)
    {
        if (m_cache.TryGet(fileId, out data))
        {
            // 记录访问模式
            RecordAccessPattern(fileId);
            
            // 预取相关文件
            PrefetchRelatedFiles(fileId);
            
            return true;
        }

        return false;
    }

    public void Set(FileID fileId, byte[] data, LeaseInfo leaseInfo)
    {
        m_cache.Set(fileId, data, leaseInfo);
    }

    private void RecordAccessPattern(FileID fileId)
    {
        lock (m_lock)
        {
            // 记录最近访问的文件
            var recentFiles = GetRecentAccessFiles();
            foreach (var recentFile in recentFiles)
            {
                if (recentFile != fileId)
                {
                    if (!m_accessPatterns.ContainsKey(recentFile))
                    {
                        m_accessPatterns[recentFile] = new List<FileID>();
                    }
                    
                    if (!m_accessPatterns[recentFile].Contains(fileId))
                    {
                        m_accessPatterns[recentFile].Add(fileId);
                    }
                }
            }
        }
    }

    private void PrefetchRelatedFiles(FileID fileId)
    {
        lock (m_lock)
        {
            if (m_accessPatterns.TryGetValue(fileId, out var relatedFiles))
            {
                foreach (var relatedFile in relatedFiles)
                {
                    if (!m_cache.Contains(relatedFile))
                    {
                        // 异步预取文件
                        Task.Run(() => PrefetchFile(relatedFile));
                    }
                }
            }
        }
    }

    private void PrefetchFile(FileID fileId)
    {
        // 从服务器预取文件数据
        // 这里需要实现具体的预取逻辑
    }

    private List<FileID> GetRecentAccessFiles()
    {
        // 获取最近访问的文件列表
        // 这里需要实现具体的逻辑
        return new List<FileID>();
    }
}
```

## 算法优化

### 1. 高效数据结构

#### 1.1 时间轮算法

```csharp
public class TimeWheelLeaseExpirationManager
{
    private readonly List<HashSet<Guid>> m_timeWheel;
    private readonly int m_wheelSize;
    private readonly TimeSpan m_tickDuration;
    private int m_currentTick;
    private readonly Timer m_timer;

    public TimeWheelLeaseExpirationManager(int wheelSize = 3600, TimeSpan tickDuration = default)
    {
        m_wheelSize = wheelSize;
        m_tickDuration = tickDuration == default ? TimeSpan.FromSeconds(1) : tickDuration;
        m_timeWheel = new List<HashSet<Guid>>(m_wheelSize);
        
        for (int i = 0; i < m_wheelSize; i++)
        {
            m_timeWheel.Add(new HashSet<Guid>());
        }
        
        m_currentTick = 0;
        m_timer = new Timer(Tick, null, m_tickDuration, m_tickDuration);
    }

    public void ScheduleExpiration(Guid leaseKey, TimeSpan delay)
    {
        var ticks = (int)(delay.TotalMilliseconds / m_tickDuration.TotalMilliseconds);
        var targetTick = (m_currentTick + ticks) % m_wheelSize;
        
        m_timeWheel[targetTick].Add(leaseKey);
    }

    public void CancelExpiration(Guid leaseKey)
    {
        foreach (var slot in m_timeWheel)
        {
            slot.Remove(leaseKey);
        }
    }

    private void Tick(object state)
    {
        var expiredLeases = m_timeWheel[m_currentTick];
        if (expiredLeases.Count > 0)
        {
            OnLeasesExpired(expiredLeases);
            expiredLeases.Clear();
        }
        
        m_currentTick = (m_currentTick + 1) % m_wheelSize;
    }

    private void OnLeasesExpired(HashSet<Guid> expiredLeases)
    {
        // 处理过期的租赁
        foreach (var leaseKey in expiredLeases)
        {
            // 触发租赁过期事件
            LeaseExpired?.Invoke(this, new LeaseExpiredEventArgs(leaseKey));
        }
    }

    public event EventHandler<LeaseExpiredEventArgs> LeaseExpired;
}
```

#### 1.2 布隆过滤器

```csharp
public class BloomFilterLeaseCache
{
    private readonly byte[] m_bitArray;
    private readonly int m_bitArraySize;
    private readonly int m_hashCount;
    private readonly HashSet<Guid> m_exactSet;

    public BloomFilterLeaseCache(int expectedItems = 10000, double falsePositiveRate = 0.01)
    {
        m_bitArraySize = CalculateBitArraySize(expectedItems, falsePositiveRate);
        m_hashCount = CalculateHashCount(expectedItems, m_bitArraySize);
        m_bitArray = new byte[m_bitArraySize];
        m_exactSet = new HashSet<Guid>();
    }

    public bool MightContain(Guid leaseKey)
    {
        var hash = leaseKey.GetHashCode();
        for (int i = 0; i < m_hashCount; i++)
        {
            var index = (hash + i * hash) % m_bitArraySize;
            if (m_bitArray[index] == 0)
            {
                return false;
            }
        }
        return true;
    }

    public void Add(Guid leaseKey)
    {
        var hash = leaseKey.GetHashCode();
        for (int i = 0; i < m_hashCount; i++)
        {
            var index = (hash + i * hash) % m_bitArraySize;
            m_bitArray[index] = 1;
        }
        m_exactSet.Add(leaseKey);
    }

    public bool Contains(Guid leaseKey)
    {
        return m_exactSet.Contains(leaseKey);
    }

    private int CalculateBitArraySize(int expectedItems, double falsePositiveRate)
    {
        return (int)(-expectedItems * Math.Log(falsePositiveRate) / (Math.Log(2) * Math.Log(2)));
    }

    private int CalculateHashCount(int expectedItems, int bitArraySize)
    {
        return (int)(bitArraySize / expectedItems * Math.Log(2));
    }
}
```

### 2. 批量操作优化

#### 2.1 批量租赁创建

```csharp
public class BatchLeaseCreator
{
    private readonly LeaseManager m_leaseManager;
    private readonly int m_batchSize;

    public BatchLeaseCreator(LeaseManager leaseManager, int batchSize = 100)
    {
        m_leaseManager = leaseManager;
        m_batchSize = batchSize;
    }

    public List<LeaseInfo> CreateLeasesBatch(List<LeaseRequest> requests)
    {
        var results = new List<LeaseInfo>();
        var batches = SplitIntoBatches(requests, m_batchSize);

        foreach (var batch in batches)
        {
            var batchResults = ProcessBatch(batch);
            results.AddRange(batchResults);
        }

        return results;
    }

    private List<List<LeaseRequest>> SplitIntoBatches(List<LeaseRequest> requests, int batchSize)
    {
        var batches = new List<List<LeaseRequest>>();
        for (int i = 0; i < requests.Count; i += batchSize)
        {
            var batch = requests.Skip(i).Take(batchSize).ToList();
            batches.Add(batch);
        }
        return batches;
    }

    private List<LeaseInfo> ProcessBatch(List<LeaseRequest> batch)
    {
        var results = new List<LeaseInfo>();
        
        // 并行处理批次
        var tasks = batch.Select(request => Task.Run(() => m_leaseManager.CreateLease(request)));
        var leaseInfos = Task.WhenAll(tasks).Result;
        
        results.AddRange(leaseInfos);
        return results;
    }
}
```

## 性能监控

### 1. 性能计数器

```csharp
public class LeasePerformanceCounters
{
    private readonly Dictionary<string, PerformanceCounter> m_counters;
    private readonly object m_lock;

    public LeasePerformanceCounters()
    {
        m_counters = new Dictionary<string, PerformanceCounter>();
        m_lock = new object();
    }

    public void IncrementCounter(string counterName)
    {
        lock (m_lock)
        {
            if (!m_counters.TryGetValue(counterName, out var counter))
            {
                counter = new PerformanceCounter(counterName);
                m_counters[counterName] = counter;
            }
            counter.Increment();
        }
    }

    public void RecordTiming(string operationName, TimeSpan duration)
    {
        lock (m_lock)
        {
            var timingCounterName = $"{operationName}_Timing";
            if (!m_counters.TryGetValue(timingCounterName, out var counter))
            {
                counter = new PerformanceCounter(timingCounterName);
                m_counters[timingCounterName] = counter;
            }
            counter.RecordTiming(duration);
        }
    }

    public PerformanceStats GetStats(string counterName)
    {
        lock (m_lock)
        {
            if (m_counters.TryGetValue(counterName, out var counter))
            {
                return counter.GetStats();
            }
            return null;
        }
    }

    public Dictionary<string, PerformanceStats> GetAllStats()
    {
        lock (m_lock)
        {
            var stats = new Dictionary<string, PerformanceStats>();
            foreach (var kvp in m_counters)
            {
                stats[kvp.Key] = kvp.Value.GetStats();
            }
            return stats;
        }
    }
}

public class PerformanceCounter
{
    private long m_count;
    private long m_totalTime;
    private long m_minTime;
    private long m_maxTime;
    private readonly object m_lock;

    public PerformanceCounter(string name)
    {
        Name = name;
        m_lock = new object();
        m_minTime = long.MaxValue;
        m_maxTime = long.MinValue;
    }

    public string Name { get; }

    public void Increment()
    {
        lock (m_lock)
        {
            Interlocked.Increment(ref m_count);
        }
    }

    public void RecordTiming(TimeSpan duration)
    {
        var ticks = duration.Ticks;
        lock (m_lock)
        {
            Interlocked.Increment(ref m_count);
            Interlocked.Add(ref m_totalTime, ticks);
            
            var currentMin = m_minTime;
            while (ticks < currentMin && Interlocked.CompareExchange(ref m_minTime, ticks, currentMin) != currentMin)
            {
                currentMin = m_minTime;
            }
            
            var currentMax = m_maxTime;
            while (ticks > currentMax && Interlocked.CompareExchange(ref m_maxTime, ticks, currentMax) != currentMax)
            {
                currentMax = m_maxTime;
            }
        }
    }

    public PerformanceStats GetStats()
    {
        lock (m_lock)
        {
            return new PerformanceStats
            {
                Count = m_count,
                TotalTime = TimeSpan.FromTicks(m_totalTime),
                AverageTime = m_count > 0 ? TimeSpan.FromTicks(m_totalTime / m_count) : TimeSpan.Zero,
                MinTime = m_minTime == long.MaxValue ? TimeSpan.Zero : TimeSpan.FromTicks(m_minTime),
                MaxTime = m_maxTime == long.MinValue ? TimeSpan.Zero : TimeSpan.FromTicks(m_maxTime)
            };
        }
    }
}

public class PerformanceStats
{
    public long Count { get; set; }
    public TimeSpan TotalTime { get; set; }
    public TimeSpan AverageTime { get; set; }
    public TimeSpan MinTime { get; set; }
    public TimeSpan MaxTime { get; set; }
}
```

### 2. 性能分析

```csharp
public class LeasePerformanceAnalyzer
{
    private readonly LeasePerformanceCounters m_counters;
    private readonly Timer m_analysisTimer;

    public LeasePerformanceAnalyzer(LeasePerformanceCounters counters)
    {
        m_counters = counters;
        m_analysisTimer = new Timer(AnalyzePerformance, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    private void AnalyzePerformance(object state)
    {
        var stats = m_counters.GetAllStats();
        var analysis = new PerformanceAnalysis();

        // 分析租赁创建性能
        if (stats.TryGetValue("LeaseCreated_Timing", out var createStats))
        {
            analysis.LeaseCreationPerformance = AnalyzeOperationPerformance(createStats);
        }

        // 分析租赁中断性能
        if (stats.TryGetValue("LeaseBreak_Timing", out var breakStats))
        {
            analysis.LeaseBreakPerformance = AnalyzeOperationPerformance(breakStats);
        }

        // 分析缓存性能
        if (stats.TryGetValue("CacheHit", out var hitStats) && 
            stats.TryGetValue("CacheMiss", out var missStats))
        {
            analysis.CacheHitRate = (double)hitStats.Count / (hitStats.Count + missStats.Count);
        }

        // 生成性能报告
        GeneratePerformanceReport(analysis);
    }

    private OperationPerformance AnalyzeOperationPerformance(PerformanceStats stats)
    {
        return new OperationPerformance
        {
            Count = stats.Count,
            AverageTime = stats.AverageTime,
            MinTime = stats.MinTime,
            MaxTime = stats.MaxTime,
            Throughput = stats.Count / stats.TotalTime.TotalSeconds
        };
    }

    private void GeneratePerformanceReport(PerformanceAnalysis analysis)
    {
        var report = new StringBuilder();
        report.AppendLine("=== 租赁协议性能分析报告 ===");
        report.AppendLine($"生成时间: {DateTime.Now}");
        report.AppendLine();

        report.AppendLine("租赁创建性能:");
        report.AppendLine($"  平均时间: {analysis.LeaseCreationPerformance.AverageTime.TotalMilliseconds:F2}ms");
        report.AppendLine($"  吞吐量: {analysis.LeaseCreationPerformance.Throughput:F2} ops/sec");
        report.AppendLine();

        report.AppendLine("租赁中断性能:");
        report.AppendLine($"  平均时间: {analysis.LeaseBreakPerformance.AverageTime.TotalMilliseconds:F2}ms");
        report.AppendLine($"  吞吐量: {analysis.LeaseBreakPerformance.Throughput:F2} ops/sec");
        report.AppendLine();

        report.AppendLine($"缓存命中率: {analysis.CacheHitRate:P2}");
        report.AppendLine();

        // 输出报告
        Console.WriteLine(report.ToString());
    }
}

public class PerformanceAnalysis
{
    public OperationPerformance LeaseCreationPerformance { get; set; }
    public OperationPerformance LeaseBreakPerformance { get; set; }
    public double CacheHitRate { get; set; }
}

public class OperationPerformance
{
    public long Count { get; set; }
    public TimeSpan AverageTime { get; set; }
    public TimeSpan MinTime { get; set; }
    public TimeSpan MaxTime { get; set; }
    public double Throughput { get; set; }
}
```

## 性能调优

### 1. 配置优化

```csharp
public class LeasePerformanceConfiguration
{
    // 内存优化
    public int MaxLeases { get; set; } = 10000;
    public int LeaseInfoPoolSize { get; set; } = 1000;
    public int ByteArrayPoolSize { get; set; } = 1000;
    public int ByteArraySize { get; set; } = 8192;

    // 并发优化
    public int MaxConcurrency { get; set; } = 100;
    public bool UseLockFreeDataStructures { get; set; } = true;
    public bool UseHierarchicalLocks { get; set; } = true;

    // 网络优化
    public int BatchSize { get; set; } = 100;
    public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromMilliseconds(50);
    public bool EnableCompression { get; set; } = true;
    public bool EnableAsyncProcessing { get; set; } = true;

    // 缓存优化
    public int CacheSize { get; set; } = 10000;
    public long MaxCacheMemory { get; set; } = 100 * 1024 * 1024; // 100MB
    public bool EnablePrefetch { get; set; } = true;
    public bool EnableLRU { get; set; } = true;

    // 算法优化
    public int TimeWheelSize { get; set; } = 3600;
    public TimeSpan TimeWheelTickDuration { get; set; } = TimeSpan.FromSeconds(1);
    public bool UseBloomFilter { get; set; } = true;
    public double BloomFilterFalsePositiveRate { get; set; } = 0.01;

    public void Validate()
    {
        if (MaxLeases <= 0)
            throw new ArgumentException("MaxLeases must be positive");
        if (MaxConcurrency <= 0)
            throw new ArgumentException("MaxConcurrency must be positive");
        if (BatchSize <= 0)
            throw new ArgumentException("BatchSize must be positive");
        if (CacheSize <= 0)
            throw new ArgumentException("CacheSize must be positive");
        if (MaxCacheMemory <= 0)
            throw new ArgumentException("MaxCacheMemory must be positive");
    }
}
```

### 2. 动态调优

```csharp
public class DynamicPerformanceTuner
{
    private readonly LeasePerformanceConfiguration m_config;
    private readonly LeasePerformanceCounters m_counters;
    private readonly Timer m_tuningTimer;

    public DynamicPerformanceTuner(LeasePerformanceConfiguration config, LeasePerformanceCounters counters)
    {
        m_config = config;
        m_counters = counters;
        m_tuningTimer = new Timer(TunePerformance, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    private void TunePerformance(object state)
    {
        var stats = m_counters.GetAllStats();
        
        // 根据性能指标调整配置
        TuneBatchSize(stats);
        TuneCacheSize(stats);
        TuneConcurrency(stats);
    }

    private void TuneBatchSize(Dictionary<string, PerformanceStats> stats)
    {
        if (stats.TryGetValue("BatchProcessing_Timing", out var batchStats))
        {
            if (batchStats.AverageTime.TotalMilliseconds > 100)
            {
                // 批次处理时间过长，减少批次大小
                m_config.BatchSize = Math.Max(10, m_config.BatchSize / 2);
            }
            else if (batchStats.AverageTime.TotalMilliseconds < 10)
            {
                // 批次处理时间过短，增加批次大小
                m_config.BatchSize = Math.Min(1000, m_config.BatchSize * 2);
            }
        }
    }

    private void TuneCacheSize(Dictionary<string, PerformanceStats> stats)
    {
        if (stats.TryGetValue("CacheHit", out var hitStats) && 
            stats.TryGetValue("CacheMiss", out var missStats))
        {
            var hitRate = (double)hitStats.Count / (hitStats.Count + missStats.Count);
            
            if (hitRate < 0.8)
            {
                // 缓存命中率过低，增加缓存大小
                m_config.CacheSize = Math.Min(50000, m_config.CacheSize * 2);
            }
            else if (hitRate > 0.95)
            {
                // 缓存命中率过高，减少缓存大小
                m_config.CacheSize = Math.Max(1000, m_config.CacheSize / 2);
            }
        }
    }

    private void TuneConcurrency(Dictionary<string, PerformanceStats> stats)
    {
        if (stats.TryGetValue("ConcurrentOperations_Timing", out var concurrencyStats))
        {
            if (concurrencyStats.AverageTime.TotalMilliseconds > 50)
            {
                // 并发操作时间过长，减少并发数
                m_config.MaxConcurrency = Math.Max(10, m_config.MaxConcurrency / 2);
            }
            else if (concurrencyStats.AverageTime.TotalMilliseconds < 5)
            {
                // 并发操作时间过短，增加并发数
                m_config.MaxConcurrency = Math.Min(500, m_config.MaxConcurrency * 2);
            }
        }
    }
}
```

## 总结

本性能优化指南提供了全面的 SMB 2.0/2.1 租赁协议性能优化方案，包括：

1. **内存优化**: 对象池、预分配、结构体优化
2. **并发优化**: 无锁数据结构、分层读写锁
3. **网络优化**: 批量处理、压缩、异步处理
4. **缓存优化**: LRU缓存、预取缓存
5. **算法优化**: 时间轮、布隆过滤器
6. **性能监控**: 性能计数器、性能分析
7. **性能调优**: 配置优化、动态调优

通过实施这些优化策略，可以显著提高租赁协议的性能，实现低延迟、高吞吐量、低内存使用的目标，为 SMBLibrary 提供高性能的租赁协议支持。
