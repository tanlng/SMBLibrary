# SMB 2.0/2.1 租赁协议测试策略

## 概述

本文档详细描述了 SMB 2.0/2.1 租赁协议的测试策略，包括测试类型、测试用例、测试工具和测试环境配置。通过全面的测试确保租赁协议实现的正确性、性能和可靠性。

## 测试目标

### 主要目标
- **功能正确性**: 验证租赁协议的所有功能按规范正确实现
- **性能验证**: 确保租赁协议不会显著影响系统性能
- **可靠性测试**: 验证系统在各种异常情况下的稳定性
- **兼容性测试**: 确保与不同 SMB 客户端的兼容性
- **安全性测试**: 验证租赁协议的安全防护措施

### 测试范围
- 租赁创建和管理
- 租赁中断处理
- 租赁过期清理
- 客户端缓存机制
- 错误处理和恢复
- 性能和并发测试
- 安全性和权限验证

## 测试类型

### 1. 单元测试 (Unit Tests)

#### 1.1 租赁管理器测试

**测试文件**: `LeaseManagerTests.cs`

```csharp
[TestClass]
public class LeaseManagerTests
{
    [TestMethod]
    public void CreateLease_ValidRequest_ReturnsLeaseInfo()
    {
        // 测试租赁创建功能
    }

    [TestMethod]
    public void CreateLease_DuplicateLeaseKey_ThrowsException()
    {
        // 测试重复租赁键处理
    }

    [TestMethod]
    public void CreateLease_MaxLeasesExceeded_ThrowsException()
    {
        // 测试最大租赁数量限制
    }

    [TestMethod]
    public void BreakLease_ValidLease_UpdatesState()
    {
        // 测试租赁中断功能
    }

    [TestMethod]
    public void BreakLease_NonExistentLease_ThrowsException()
    {
        // 测试不存在的租赁中断
    }

    [TestMethod]
    public void AcknowledgeLeaseBreak_ValidLease_RemovesLease()
    {
        // 测试租赁中断确认
    }

    [TestMethod]
    public void GetLeasesBySession_ValidSession_ReturnsLeases()
    {
        // 测试按会话查询租赁
    }

    [TestMethod]
    public void GetLeasesByFile_ValidFile_ReturnsLeases()
    {
        // 测试按文件查询租赁
    }

    [TestMethod]
    public void CleanupExpiredLeases_ExpiredLeases_RemovesLeases()
    {
        // 测试过期租赁清理
    }
}
```

#### 1.2 租赁上下文处理器测试

**测试文件**: `LeaseContextHandlerTests.cs`

```csharp
[TestClass]
public class LeaseContextHandlerTests
{
    [TestMethod]
    public void ProcessCreateContext_ValidContext_ReturnsResponseContext()
    {
        // 测试租赁上下文处理
    }

    [TestMethod]
    public void ProcessCreateContext_InvalidLeaseKey_ThrowsException()
    {
        // 测试无效租赁键处理
    }

    [TestMethod]
    public void ProcessCreateContext_InvalidLeaseState_ThrowsException()
    {
        // 测试无效租赁状态处理
    }

    [TestMethod]
    public void GenerateResponseContext_ValidLeaseInfo_ReturnsContext()
    {
        // 测试响应上下文生成
    }

    [TestMethod]
    public void ValidateLeaseRequest_ValidRequest_ReturnsTrue()
    {
        // 测试租赁请求验证
    }

    [TestMethod]
    public void ValidateLeaseRequest_InvalidRequest_ReturnsFalse()
    {
        // 测试无效请求验证
    }
}
```

#### 1.3 客户端租赁缓存测试

**测试文件**: `LeaseCacheTests.cs`

```csharp
[TestClass]
public class LeaseCacheTests
{
    [TestMethod]
    public void CacheFileData_ValidData_CachesData()
    {
        // 测试文件数据缓存
    }

    [TestMethod]
    public void GetCachedData_ExistingData_ReturnsData()
    {
        // 测试缓存数据获取
    }

    [TestMethod]
    public void GetCachedData_NonExistentData_ReturnsNull()
    {
        // 测试不存在数据的获取
    }

    [TestMethod]
    public void InvalidateCache_ExistingData_RemovesData()
    {
        // 测试缓存失效
    }

    [TestMethod]
    public void HandleLeaseBreak_ValidNotification_InvalidatesCache()
    {
        // 测试租赁中断处理
    }

    [TestMethod]
    public void IsCached_ExistingData_ReturnsTrue()
    {
        // 测试缓存状态检查
    }

    [TestMethod]
    public void ClearCache_ExistingData_ClearsAllData()
    {
        // 测试缓存清理
    }
}
```

### 2. 集成测试 (Integration Tests)

#### 2.1 端到端租赁流程测试

**测试文件**: `LeaseIntegrationTests.cs`

```csharp
[TestClass]
public class LeaseIntegrationTests
{
    [TestMethod]
    public async Task CompleteLeaseFlow_CreateBreakAcknowledge_Success()
    {
        // 测试完整的租赁流程
        // 1. 创建租赁
        // 2. 中断租赁
        // 3. 确认中断
    }

    [TestMethod]
    public async Task LeaseExpiration_ExpiredLease_CleanedUp()
    {
        // 测试租赁过期处理
    }

    [TestMethod]
    public async Task MultipleClients_SameFile_LeaseBreakHandled()
    {
        // 测试多客户端场景
    }

    [TestMethod]
    public async Task NetworkInterruption_LeaseBreak_Recovered()
    {
        // 测试网络中断恢复
    }

    [TestMethod]
    public async Task SessionLogoff_ActiveLeases_CleanedUp()
    {
        // 测试会话注销时的租赁清理
    }
}
```

#### 2.2 SMB2 命令集成测试

**测试文件**: `SMB2LeaseCommandTests.cs`

```csharp
[TestClass]
public class SMB2LeaseCommandTests
{
    [TestMethod]
    public void CreateRequest_WithLeaseContext_ParsedCorrectly()
    {
        // 测试 Create 请求中的租赁上下文解析
    }

    [TestMethod]
    public void CreateResponse_WithLeaseContext_SerializedCorrectly()
    {
        // 测试 Create 响应中的租赁上下文序列化
    }

    [TestMethod]
    public void LeaseBreakRequest_ParsedCorrectly()
    {
        // 测试租赁中断请求解析
    }

    [TestMethod]
    public void LeaseBreakResponse_SerializedCorrectly()
    {
        // 测试租赁中断响应序列化
    }

    [TestMethod]
    public void LeaseBreakAck_ParsedCorrectly()
    {
        // 测试租赁中断确认解析
    }
}
```

### 3. 性能测试 (Performance Tests)

#### 3.1 租赁管理器性能测试

**测试文件**: `LeaseManagerPerformanceTests.cs`

```csharp
[TestClass]
public class LeaseManagerPerformanceTests
{
    [TestMethod]
    public void CreateLease_PerformanceTest()
    {
        // 测试租赁创建性能
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            var request = new LeaseRequest
            {
                LeaseKey = Guid.NewGuid(),
                LeaseState = LeaseState.ReadCaching,
                FilePath = $"/test/file{i}.txt",
                SessionId = (ulong)i,
                FileId = new FileID { Volatile = (ulong)i, Persistent = (ulong)i },
                LeaseDuration = TimeSpan.FromMinutes(30)
            };
            leaseManager.CreateLease(request);
        }
        stopwatch.Stop();
        
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000, 
            $"Creating 10000 leases took {stopwatch.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public void ConcurrentLeaseOperations_PerformanceTest()
    {
        // 测试并发租赁操作性能
        var tasks = new List<Task>();
        var stopwatch = Stopwatch.StartNew();
        
        for (int i = 0; i < 1000; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var request = new LeaseRequest
                {
                    LeaseKey = Guid.NewGuid(),
                    LeaseState = LeaseState.ReadCaching,
                    FilePath = $"/test/file{i}.txt",
                    SessionId = (ulong)i,
                    FileId = new FileID { Volatile = (ulong)i, Persistent = (ulong)i },
                    LeaseDuration = TimeSpan.FromMinutes(30)
                };
                leaseManager.CreateLease(request);
            }));
        }
        
        Task.WaitAll(tasks.ToArray());
        stopwatch.Stop();
        
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 2000, 
            $"Concurrent operations took {stopwatch.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public void MemoryUsage_LeaseManager_Monitored()
    {
        // 测试内存使用情况
        var initialMemory = GC.GetTotalMemory(true);
        
        for (int i = 0; i < 10000; i++)
        {
            var request = new LeaseRequest
            {
                LeaseKey = Guid.NewGuid(),
                LeaseState = LeaseState.ReadCaching,
                FilePath = $"/test/file{i}.txt",
                SessionId = (ulong)i,
                FileId = new FileID { Volatile = (ulong)i, Persistent = (ulong)i },
                LeaseDuration = TimeSpan.FromMinutes(30)
            };
            leaseManager.CreateLease(request);
        }
        
        var finalMemory = GC.GetTotalMemory(true);
        var memoryUsed = finalMemory - initialMemory;
        
        Assert.IsTrue(memoryUsed < 10 * 1024 * 1024, 
            $"Memory usage: {memoryUsed / 1024 / 1024}MB");
    }
}
```

#### 3.2 客户端缓存性能测试

**测试文件**: `LeaseCachePerformanceTests.cs`

```csharp
[TestClass]
public class LeaseCachePerformanceTests
{
    [TestMethod]
    public void CacheHitRate_PerformanceTest()
    {
        // 测试缓存命中率
        var cache = new LeaseCache();
        var testData = new byte[1024];
        var fileId = new FileID { Volatile = 1, Persistent = 1 };
        
        // 缓存数据
        cache.CacheFileData(fileId, testData, new LeaseInfo());
        
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 100000; i++)
        {
            cache.GetCachedData(fileId);
        }
        stopwatch.Stop();
        
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 100, 
            $"Cache access took {stopwatch.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public void CacheSize_PerformanceTest()
    {
        // 测试缓存大小对性能的影响
        var cache = new LeaseCache();
        var testData = new byte[1024];
        
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            var fileId = new FileID { Volatile = (ulong)i, Persistent = (ulong)i };
            cache.CacheFileData(fileId, testData, new LeaseInfo());
        }
        stopwatch.Stop();
        
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 500, 
            $"Caching 10000 files took {stopwatch.ElapsedMilliseconds}ms");
    }
}
```

### 4. 压力测试 (Stress Tests)

#### 4.1 大量租赁测试

**测试文件**: `LeaseStressTests.cs`

```csharp
[TestClass]
public class LeaseStressTests
{
    [TestMethod]
    public void MaximumLeases_CreationTest()
    {
        // 测试最大租赁数量
        var config = new LeaseManagerConfiguration
        {
            MaxLeases = 100000
        };
        var leaseManager = new LeaseManager(config);
        
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < config.MaxLeases; i++)
        {
            var request = new LeaseRequest
            {
                LeaseKey = Guid.NewGuid(),
                LeaseState = LeaseState.ReadCaching,
                FilePath = $"/test/file{i}.txt",
                SessionId = (ulong)(i % 1000),
                FileId = new FileID { Volatile = (ulong)i, Persistent = (ulong)i },
                LeaseDuration = TimeSpan.FromMinutes(30)
            };
            leaseManager.CreateLease(request);
        }
        stopwatch.Stop();
        
        Assert.AreEqual(config.MaxLeases, leaseManager.ActiveLeaseCount);
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 30000, 
            $"Creating {config.MaxLeases} leases took {stopwatch.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public void RapidLeaseBreak_StressTest()
    {
        // 测试快速租赁中断
        var leaseManager = new LeaseManager();
        var leases = new List<LeaseInfo>();
        
        // 创建大量租赁
        for (int i = 0; i < 1000; i++)
        {
            var request = new LeaseRequest
            {
                LeaseKey = Guid.NewGuid(),
                LeaseState = LeaseState.ReadCaching,
                FilePath = $"/test/file{i}.txt",
                SessionId = (ulong)i,
                FileId = new FileID { Volatile = (ulong)i, Persistent = (ulong)i },
                LeaseDuration = TimeSpan.FromMinutes(30)
            };
            leases.Add(leaseManager.CreateLease(request));
        }
        
        // 快速中断所有租赁
        var stopwatch = Stopwatch.StartNew();
        foreach (var lease in leases)
        {
            leaseManager.BreakLease(lease.LeaseKey, LeaseBreakReason.WriteRequest);
        }
        stopwatch.Stop();
        
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 5000, 
            $"Breaking 1000 leases took {stopwatch.ElapsedMilliseconds}ms");
    }
}
```

### 5. 兼容性测试 (Compatibility Tests)

#### 5.1 Windows 客户端兼容性测试

**测试文件**: `WindowsClientCompatibilityTests.cs`

```csharp
[TestClass]
public class WindowsClientCompatibilityTests
{
    [TestMethod]
    public void Windows10_LeaseSupport_Compatible()
    {
        // 测试与 Windows 10 的兼容性
    }

    [TestMethod]
    public void WindowsServer2019_LeaseSupport_Compatible()
    {
        // 测试与 Windows Server 2019 的兼容性
    }

    [TestMethod]
    public void Windows11_LeaseSupport_Compatible()
    {
        // 测试与 Windows 11 的兼容性
    }

    [TestMethod]
    public void LegacyWindows_NoLeaseSupport_GracefulDegradation()
    {
        // 测试与不支持租赁的旧版 Windows 的兼容性
    }
}
```

#### 5.2 第三方客户端兼容性测试

**测试文件**: `ThirdPartyClientCompatibilityTests.cs`

```csharp
[TestClass]
public class ThirdPartyClientCompatibilityTests
{
    [TestMethod]
    public void LinuxSamba_LeaseSupport_Compatible()
    {
        // 测试与 Linux Samba 的兼容性
    }

    [TestMethod]
    public void MacOS_LeaseSupport_Compatible()
    {
        // 测试与 macOS 的兼容性
    }

    [TestMethod]
    public void Android_LeaseSupport_Compatible()
    {
        // 测试与 Android 的兼容性
    }

    [TestMethod]
    public void iOS_LeaseSupport_Compatible()
    {
        // 测试与 iOS 的兼容性
    }
}
```

### 6. 安全测试 (Security Tests)

#### 6.1 租赁键安全测试

**测试文件**: `LeaseSecurityTests.cs`

```csharp
[TestClass]
public class LeaseSecurityTests
{
    [TestMethod]
    public void LeaseKey_Uniqueness_Verified()
    {
        // 测试租赁键的唯一性
        var leaseKeys = new HashSet<Guid>();
        var leaseManager = new LeaseManager();
        
        for (int i = 0; i < 10000; i++)
        {
            var request = new LeaseRequest
            {
                LeaseKey = Guid.NewGuid(),
                LeaseState = LeaseState.ReadCaching,
                FilePath = $"/test/file{i}.txt",
                SessionId = (ulong)i,
                FileId = new FileID { Volatile = (ulong)i, Persistent = (ulong)i },
                LeaseDuration = TimeSpan.FromMinutes(30)
            };
            var lease = leaseManager.CreateLease(request);
            Assert.IsTrue(leaseKeys.Add(lease.LeaseKey), 
                "Lease key must be unique");
        }
    }

    [TestMethod]
    public void LeaseKey_Predictability_Tested()
    {
        // 测试租赁键的不可预测性
        var leaseKeys = new List<Guid>();
        var leaseManager = new LeaseManager();
        
        for (int i = 0; i < 1000; i++)
        {
            var request = new LeaseRequest
            {
                LeaseKey = Guid.NewGuid(),
                LeaseState = LeaseState.ReadCaching,
                FilePath = $"/test/file{i}.txt",
                SessionId = (ulong)i,
                FileId = new FileID { Volatile = (ulong)i, Persistent = (ulong)i },
                LeaseDuration = TimeSpan.FromMinutes(30)
            };
            var lease = leaseManager.CreateLease(request);
            leaseKeys.Add(lease.LeaseKey);
        }
        
        // 检查租赁键的随机性
        Assert.IsTrue(IsRandom(leaseKeys), "Lease keys must be random");
    }

    [TestMethod]
    public void LeaseAccess_UnauthorizedAccess_Denied()
    {
        // 测试未授权访问租赁
        var leaseManager = new LeaseManager();
        var request = new LeaseRequest
        {
            LeaseKey = Guid.NewGuid(),
            LeaseState = LeaseState.ReadCaching,
            FilePath = "/test/file.txt",
            SessionId = 12345,
            FileId = new FileID { Volatile = 1, Persistent = 1 },
            LeaseDuration = TimeSpan.FromMinutes(30)
        };
        var lease = leaseManager.CreateLease(request);
        
        // 尝试使用不同的会话ID访问租赁
        var unauthorizedLeases = leaseManager.GetLeasesBySession(99999);
        Assert.IsFalse(unauthorizedLeases.Contains(lease), 
            "Unauthorized access should be denied");
    }

    private bool IsRandom(List<Guid> guids)
    {
        // 简单的随机性检查
        var bytes = guids.SelectMany(g => g.ToByteArray()).ToArray();
        var entropy = CalculateEntropy(bytes);
        return entropy > 7.5; // 高熵值表示随机性
    }

    private double CalculateEntropy(byte[] data)
    {
        var frequencies = new Dictionary<byte, int>();
        foreach (var b in data)
        {
            frequencies[b] = frequencies.GetValueOrDefault(b, 0) + 1;
        }
        
        double entropy = 0;
        foreach (var freq in frequencies.Values)
        {
            double probability = (double)freq / data.Length;
            entropy -= probability * Math.Log2(probability);
        }
        
        return entropy;
    }
}
```

## 测试环境配置

### 1. 开发环境

#### 1.1 单元测试环境
```xml
<!-- 测试配置文件 -->
<configuration>
  <appSettings>
    <add key="TestDatabase" value="TestSMBLibrary" />
    <add key="TestTimeout" value="30000" />
    <add key="TestMaxLeases" value="1000" />
    <add key="TestLeaseDuration" value="00:01:00" />
  </appSettings>
</configuration>
```

#### 1.2 集成测试环境
```xml
<!-- 集成测试配置 -->
<configuration>
  <appSettings>
    <add key="SMB2ServerPort" value="445" />
    <add key="SMB2ClientPort" value="445" />
    <add key="TestSharePath" value="C:\TestShare" />
    <add key="TestUserName" value="testuser" />
    <add key="TestPassword" value="testpass" />
  </appSettings>
</configuration>
```

### 2. 测试数据

#### 2.1 测试文件
```csharp
public class TestDataGenerator
{
    public static LeaseRequest GenerateLeaseRequest()
    {
        return new LeaseRequest
        {
            LeaseKey = Guid.NewGuid(),
            LeaseState = LeaseState.ReadCaching,
            LeaseFlags = LeaseFlags.None,
            LeaseDuration = TimeSpan.FromMinutes(30),
            FilePath = "/test/file.txt",
            SessionId = 12345,
            FileId = new FileID { Volatile = 1, Persistent = 1 },
            DesiredAccess = AccessMask.GENERIC_READ,
            ShareAccess = ShareAccess.FILE_SHARE_READ
        };
    }

    public static byte[] GenerateTestData(int size)
    {
        var random = new Random();
        var data = new byte[size];
        random.NextBytes(data);
        return data;
    }

    public static List<LeaseRequest> GenerateMultipleLeaseRequests(int count)
    {
        var requests = new List<LeaseRequest>();
        for (int i = 0; i < count; i++)
        {
            requests.Add(new LeaseRequest
            {
                LeaseKey = Guid.NewGuid(),
                LeaseState = LeaseState.ReadCaching,
                LeaseFlags = LeaseFlags.None,
                LeaseDuration = TimeSpan.FromMinutes(30),
                FilePath = $"/test/file{i}.txt",
                SessionId = (ulong)(i % 100),
                FileId = new FileID { Volatile = (ulong)i, Persistent = (ulong)i },
                DesiredAccess = AccessMask.GENERIC_READ,
                ShareAccess = ShareAccess.FILE_SHARE_READ
            });
        }
        return requests;
    }
}
```

### 3. 测试工具

#### 3.1 性能监控工具
```csharp
public class PerformanceMonitor
{
    private readonly Dictionary<string, List<long>> m_measurements;
    private readonly object m_lock;

    public PerformanceMonitor()
    {
        m_measurements = new Dictionary<string, List<long>>();
        m_lock = new object();
    }

    public void RecordMeasurement(string operation, long elapsedMilliseconds)
    {
        lock (m_lock)
        {
            if (!m_measurements.ContainsKey(operation))
            {
                m_measurements[operation] = new List<long>();
            }
            m_measurements[operation].Add(elapsedMilliseconds);
        }
    }

    public PerformanceStats GetStats(string operation)
    {
        lock (m_lock)
        {
            if (!m_measurements.ContainsKey(operation))
            {
                return null;
            }

            var measurements = m_measurements[operation];
            return new PerformanceStats
            {
                Count = measurements.Count,
                Average = measurements.Average(),
                Min = measurements.Min(),
                Max = measurements.Max(),
                Median = GetMedian(measurements)
            };
        }
    }

    private double GetMedian(List<long> values)
    {
        var sorted = values.OrderBy(x => x).ToList();
        int count = sorted.Count;
        if (count % 2 == 0)
        {
            return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
        }
        else
        {
            return sorted[count / 2];
        }
    }
}

public class PerformanceStats
{
    public int Count { get; set; }
    public double Average { get; set; }
    public long Min { get; set; }
    public long Max { get; set; }
    public double Median { get; set; }
}
```

#### 3.2 内存监控工具
```csharp
public class MemoryMonitor
{
    public static MemoryInfo GetMemoryInfo()
    {
        var process = Process.GetCurrentProcess();
        return new MemoryInfo
        {
            WorkingSet = process.WorkingSet64,
            PrivateMemory = process.PrivateMemorySize64,
            VirtualMemory = process.VirtualMemorySize64,
            PagedMemory = process.PagedMemorySize64,
            NonPagedMemory = process.NonpagedSystemMemorySize64
        };
    }

    public static void ForceGarbageCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

public class MemoryInfo
{
    public long WorkingSet { get; set; }
    public long PrivateMemory { get; set; }
    public long VirtualMemory { get; set; }
    public long PagedMemory { get; set; }
    public long NonPagedMemory { get; set; }
}
```

## 测试执行策略

### 1. 测试执行顺序

1. **单元测试**: 首先执行所有单元测试
2. **集成测试**: 然后执行集成测试
3. **性能测试**: 接着执行性能测试
4. **压力测试**: 然后执行压力测试
5. **兼容性测试**: 最后执行兼容性测试

### 2. 测试环境隔离

```csharp
[TestClass]
public class LeaseTestBase
{
    protected LeaseManager m_leaseManager;
    protected LeaseCache m_leaseCache;
    protected PerformanceMonitor m_performanceMonitor;

    [TestInitialize]
    public void Setup()
    {
        // 创建独立的测试环境
        var config = new LeaseManagerConfiguration
        {
            MaxLeases = 1000,
            DefaultLeaseDuration = TimeSpan.FromMinutes(1),
            LeaseBreakTimeout = TimeSpan.FromSeconds(10),
            CleanupInterval = TimeSpan.FromSeconds(30)
        };
        
        m_leaseManager = new LeaseManager(config);
        m_leaseCache = new LeaseCache();
        m_performanceMonitor = new PerformanceMonitor();
    }

    [TestCleanup]
    public void Cleanup()
    {
        // 清理测试环境
        m_leaseManager?.Dispose();
        m_leaseCache?.ClearCache();
        MemoryMonitor.ForceGarbageCollection();
    }
}
```

### 3. 测试数据管理

```csharp
public class TestDataManager
{
    private readonly string m_testDataPath;
    private readonly List<string> m_createdFiles;

    public TestDataManager()
    {
        m_testDataPath = Path.Combine(Path.GetTempPath(), "SMBLibraryTest");
        Directory.CreateDirectory(m_testDataPath);
        m_createdFiles = new List<string>();
    }

    public string CreateTestFile(string fileName, int size = 1024)
    {
        var filePath = Path.Combine(m_testDataPath, fileName);
        var data = TestDataGenerator.GenerateTestData(size);
        File.WriteAllBytes(filePath, data);
        m_createdFiles.Add(filePath);
        return filePath;
    }

    public void Cleanup()
    {
        foreach (var file in m_createdFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // 忽略删除失败
            }
        }
        
        try
        {
            Directory.Delete(m_testDataPath, true);
        }
        catch
        {
            // 忽略删除失败
        }
    }
}
```

## 测试报告

### 1. 测试结果格式

```csharp
public class TestResult
{
    public string TestName { get; set; }
    public TestStatus Status { get; set; }
    public TimeSpan Duration { get; set; }
    public string ErrorMessage { get; set; }
    public Dictionary<string, object> Metrics { get; set; }
    public List<string> Logs { get; set; }
}

public enum TestStatus
{
    Passed,
    Failed,
    Skipped,
    Inconclusive
}
```

### 2. 测试报告生成

```csharp
public class TestReportGenerator
{
    public void GenerateReport(List<TestResult> results, string outputPath)
    {
        var report = new StringBuilder();
        report.AppendLine("# SMB 2.0/2.1 租赁协议测试报告");
        report.AppendLine($"生成时间: {DateTime.Now}");
        report.AppendLine();

        // 测试概览
        var passed = results.Count(r => r.Status == TestStatus.Passed);
        var failed = results.Count(r => r.Status == TestStatus.Failed);
        var skipped = results.Count(r => r.Status == TestStatus.Skipped);
        
        report.AppendLine("## 测试概览");
        report.AppendLine($"- 总测试数: {results.Count}");
        report.AppendLine($"- 通过: {passed}");
        report.AppendLine($"- 失败: {failed}");
        report.AppendLine($"- 跳过: {skipped}");
        report.AppendLine($"- 通过率: {(double)passed / results.Count * 100:F2}%");
        report.AppendLine();

        // 详细结果
        report.AppendLine("## 详细结果");
        foreach (var result in results)
        {
            report.AppendLine($"### {result.TestName}");
            report.AppendLine($"- 状态: {result.Status}");
            report.AppendLine($"- 耗时: {result.Duration}");
            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                report.AppendLine($"- 错误: {result.ErrorMessage}");
            }
            if (result.Metrics != null && result.Metrics.Count > 0)
            {
                report.AppendLine("- 指标:");
                foreach (var metric in result.Metrics)
                {
                    report.AppendLine($"  - {metric.Key}: {metric.Value}");
                }
            }
            report.AppendLine();
        }

        File.WriteAllText(outputPath, report.ToString());
    }
}
```

## 持续集成

### 1. CI/CD 配置

```yaml
# Azure DevOps Pipeline
trigger:
- main
- develop

pool:
  vmImage: 'windows-latest'

variables:
  buildConfiguration: 'Release'
  testResultsFormat: 'VSTest'
  testResultsFiles: '**/*.trx'

stages:
- stage: Build
  displayName: 'Build and Test'
  jobs:
  - job: Build
    displayName: 'Build'
    steps:
    - task: DotNetCoreCLI@2
      displayName: 'Restore packages'
      inputs:
        command: 'restore'
        projects: '**/*.csproj'
        
    - task: DotNetCoreCLI@2
      displayName: 'Build solution'
      inputs:
        command: 'build'
        projects: '**/*.csproj'
        arguments: '--configuration $(buildConfiguration)'
        
    - task: DotNetCoreCLI@2
      displayName: 'Run unit tests'
      inputs:
        command: 'test'
        projects: '**/*Tests.csproj'
        arguments: '--configuration $(buildConfiguration) --collect:"Code coverage" --logger trx --results-directory $(Agent.TempDirectory)'
        
    - task: PublishTestResults@2
      displayName: 'Publish test results'
      inputs:
        testResultsFormat: '$(testResultsFormat)'
        testResultsFiles: '$(testResultsFiles)'
        searchFolder: '$(Agent.TempDirectory)'
        mergeTestResults: true
        
    - task: PublishCodeCoverageResults@1
      displayName: 'Publish code coverage'
      inputs:
        codeCoverageTool: 'Cobertura'
        summaryFileLocation: '$(Agent.TempDirectory)/**/coverage.cobertura.xml'
```

### 2. 测试自动化

```csharp
public class AutomatedTestRunner
{
    public async Task<TestResult> RunAllTests()
    {
        var results = new List<TestResult>();
        
        // 运行单元测试
        results.AddRange(await RunUnitTests());
        
        // 运行集成测试
        results.AddRange(await RunIntegrationTests());
        
        // 运行性能测试
        results.AddRange(await RunPerformanceTests());
        
        // 运行压力测试
        results.AddRange(await RunStressTests());
        
        // 运行兼容性测试
        results.AddRange(await RunCompatibilityTests());
        
        return new TestResult
        {
            TestName = "All Tests",
            Status = results.All(r => r.Status == TestStatus.Passed) ? 
                TestStatus.Passed : TestStatus.Failed,
            Duration = TimeSpan.FromMilliseconds(results.Sum(r => r.Duration.TotalMilliseconds)),
            Metrics = new Dictionary<string, object>
            {
                ["TotalTests"] = results.Count,
                ["PassedTests"] = results.Count(r => r.Status == TestStatus.Passed),
                ["FailedTests"] = results.Count(r => r.Status == TestStatus.Failed)
            }
        };
    }
}
```

## 总结

本测试策略文档提供了完整的 SMB 2.0/2.1 租赁协议测试方案，包括：

1. **全面的测试类型**: 单元测试、集成测试、性能测试、压力测试、兼容性测试和安全测试
2. **详细的测试用例**: 覆盖所有租赁协议功能和边界情况
3. **完整的测试环境**: 包括测试数据、测试工具和测试配置
4. **自动化测试**: 支持持续集成和自动化测试执行
5. **测试报告**: 提供详细的测试结果和性能指标

通过遵循本测试策略，可以确保租赁协议实现的质量、性能和可靠性，为 SMBLibrary 提供高质量的租赁协议支持。
