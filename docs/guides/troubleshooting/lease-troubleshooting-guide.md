# SMB 2.0/2.1 租赁协议故障排除指南

## 概述

本文档提供了 SMB 2.0/2.1 租赁协议实现中常见问题的诊断和解决方案。通过系统化的故障排除方法，可以快速定位和解决租赁协议相关的问题，确保系统的稳定运行。

## 故障排除流程

### 1. 问题识别

#### 1.1 问题分类
- **功能问题**: 租赁创建、中断、确认失败
- **性能问题**: 响应时间过长、吞吐量低
- **稳定性问题**: 内存泄漏、崩溃、死锁
- **兼容性问题**: 客户端连接失败、协议不匹配
- **安全问题**: 未授权访问、数据泄露

#### 1.2 常见错误代码
- **LeaseInvalid**: 租赁请求无效（通常是 LeaseDuration 验证错误）
- **LeaseAlreadyExists**: 租赁已存在（不同会话使用相同 LeaseKey）
- **LeaseNotFound**: 租赁不存在（已过期或被清理）
- **LeaseExpired**: 租赁已过期

#### 1.3 问题优先级
- **P0 - 严重**: 系统崩溃、数据丢失
- **P1 - 高**: 功能完全不可用
- **P2 - 中**: 功能部分不可用
- **P3 - 低**: 性能问题、用户体验问题

### 2. 信息收集

#### 2.1 系统信息
```csharp
public class SystemInfoCollector
{
    public SystemInfo CollectSystemInfo()
    {
        return new SystemInfo
        {
            OSVersion = Environment.OSVersion.ToString(),
            FrameworkVersion = Environment.Version.ToString(),
            MachineName = Environment.MachineName,
            ProcessorCount = Environment.ProcessorCount,
            WorkingSet = Environment.WorkingSet,
            TickCount = Environment.TickCount,
            CurrentTime = DateTime.UtcNow
        };
    }
}

public class SystemInfo
{
    public string OSVersion { get; set; }
    public string FrameworkVersion { get; set; }
    public string MachineName { get; set; }
    public int ProcessorCount { get; set; }
    public long WorkingSet { get; set; }
    public int TickCount { get; set; }
    public DateTime CurrentTime { get; set; }
}
```

#### 2.2 租赁状态信息
```csharp
public class LeaseStateCollector
{
    private readonly LeaseManager m_leaseManager;

    public LeaseStateCollector(LeaseManager leaseManager)
    {
        m_leaseManager = leaseManager;
    }

    public LeaseStateInfo CollectLeaseState()
    {
        var activeLeases = m_leaseManager.GetActiveLeases();
        return new LeaseStateInfo
        {
            TotalLeases = activeLeases.Count,
            LeasesByState = activeLeases.GroupBy(l => l.State).ToDictionary(g => g.Key, g => g.Count()),
            LeasesBySession = activeLeases.GroupBy(l => l.SessionId).ToDictionary(g => g.Key, g => g.Count()),
            OldestLease = activeLeases.OrderBy(l => l.CreatedTime).FirstOrDefault(),
            NewestLease = activeLeases.OrderByDescending(l => l.CreatedTime).FirstOrDefault(),
            ExpiredLeases = activeLeases.Count(l => l.IsExpired),
            BreakingLeases = activeLeases.Count(l => l.IsBreaking)
        };
    }
}

public class LeaseStateInfo
{
    public int TotalLeases { get; set; }
    public Dictionary<LeaseState, int> LeasesByState { get; set; }
    public Dictionary<ulong, int> LeasesBySession { get; set; }
    public LeaseInfo OldestLease { get; set; }
    public LeaseInfo NewestLease { get; set; }
    public int ExpiredLeases { get; set; }
    public int BreakingLeases { get; set; }
}
```

#### 2.3 性能信息
```csharp
public class PerformanceInfoCollector
{
    private readonly LeasePerformanceCounters m_counters;

    public PerformanceInfoCollector(LeasePerformanceCounters counters)
    {
        m_counters = counters;
    }

    public PerformanceInfo CollectPerformanceInfo()
    {
        var stats = m_counters.GetAllStats();
        return new PerformanceInfo
        {
            LeaseCreationStats = stats.GetValueOrDefault("LeaseCreated_Timing"),
            LeaseBreakStats = stats.GetValueOrDefault("LeaseBreak_Timing"),
            CacheHitStats = stats.GetValueOrDefault("CacheHit"),
            CacheMissStats = stats.GetValueOrDefault("CacheMiss"),
            MemoryUsage = GC.GetTotalMemory(false),
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2)
        };
    }
}

public class PerformanceInfo
{
    public PerformanceStats LeaseCreationStats { get; set; }
    public PerformanceStats LeaseBreakStats { get; set; }
    public PerformanceStats CacheHitStats { get; set; }
    public PerformanceStats CacheMissStats { get; set; }
    public long MemoryUsage { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
}
```

## 常见问题诊断

### 1. 租赁创建失败

#### 1.1 问题症状
- 客户端无法创建租赁
- 返回 `STATUS_INSUFFICIENT_RESOURCES` 错误
- 租赁数量达到上限

#### 1.2 诊断步骤
```csharp
public class LeaseCreationDiagnostic
{
    public DiagnosticResult DiagnoseLeaseCreationFailure(LeaseRequest request, Exception exception)
    {
        var result = new DiagnosticResult
        {
            ProblemType = "LeaseCreationFailure",
            Timestamp = DateTime.UtcNow
        };

        // 检查租赁数量限制
        if (exception is LeaseException leaseEx && leaseEx.ErrorCode == LeaseErrorCode.LeaseResourceExhausted)
        {
            result.Issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.High,
                Category = "ResourceExhaustion",
                Description = "Maximum lease count exceeded",
                Recommendation = "Increase MaxLeases configuration or clean up expired leases"
            });
        }

        // 检查租赁键唯一性
        if (request.LeaseKey == Guid.Empty)
        {
            result.Issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Medium,
                Category = "InvalidRequest",
                Description = "Lease key is empty",
                Recommendation = "Generate a valid lease key"
            });
        }

        // 检查租赁状态
        if (request.LeaseState == LeaseState.None)
        {
            result.Issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Medium,
                Category = "InvalidRequest",
                Description = "Lease state is None",
                Recommendation = "Specify a valid lease state"
            });
        }

        return result;
    }
}
```

#### 1.3 解决方案
```csharp
public class LeaseCreationFixer
{
    public void FixLeaseCreationIssues(DiagnosticResult diagnostic)
    {
        foreach (var issue in diagnostic.Issues)
        {
            switch (issue.Category)
            {
                case "ResourceExhaustion":
                    FixResourceExhaustion();
                    break;
                case "InvalidRequest":
                    FixInvalidRequest(issue);
                    break;
            }
        }
    }

    private void FixResourceExhaustion()
    {
        // 清理过期租赁
        var leaseManager = GetLeaseManager();
        var cleanedCount = leaseManager.CleanupExpiredLeases();
        Console.WriteLine($"Cleaned up {cleanedCount} expired leases");

        // 如果仍然不足，增加最大租赁数量
        if (leaseManager.ActiveLeaseCount >= leaseManager.MaxLeases * 0.9)
        {
            Console.WriteLine("Consider increasing MaxLeases configuration");
        }
    }

    private void FixInvalidRequest(DiagnosticIssue issue)
    {
        Console.WriteLine($"Invalid request issue: {issue.Description}");
        Console.WriteLine($"Recommendation: {issue.Recommendation}");
    }
}
```

### 2. 租赁中断超时

#### 2.1 问题症状
- 租赁中断通知发送后客户端无响应
- 租赁状态一直处于 `BREAKING` 状态
- 系统资源被占用

#### 2.2 诊断步骤
```csharp
public class LeaseBreakTimeoutDiagnostic
{
    public DiagnosticResult DiagnoseLeaseBreakTimeout(Guid leaseKey, TimeSpan timeout)
    {
        var result = new DiagnosticResult
        {
            ProblemType = "LeaseBreakTimeout",
            Timestamp = DateTime.UtcNow
        };

        var leaseManager = GetLeaseManager();
        var leaseInfo = leaseManager.GetLeaseInfo(leaseKey);

        if (leaseInfo == null)
        {
            result.Issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.High,
                Category = "LeaseNotFound",
                Description = "Lease not found",
                Recommendation = "Check if lease was already removed"
            });
            return result;
        }

        if (leaseInfo.IsBreaking)
        {
            var breakDuration = DateTime.UtcNow - leaseInfo.CreatedTime;
            if (breakDuration > timeout)
            {
                result.Issues.Add(new DiagnosticIssue
                {
                    Severity = DiagnosticSeverity.High,
                    Category = "TimeoutExceeded",
                    Description = $"Lease break timeout exceeded: {breakDuration}",
                    Recommendation = "Force acknowledge lease break or increase timeout"
                });
            }
        }

        return result;
    }
}
```

#### 2.3 解决方案
```csharp
public class LeaseBreakTimeoutFixer
{
    public void FixLeaseBreakTimeout(Guid leaseKey)
    {
        var leaseManager = GetLeaseManager();
        var leaseInfo = leaseManager.GetLeaseInfo(leaseKey);

        if (leaseInfo != null && leaseInfo.IsBreaking)
        {
            // 强制确认租赁中断
            try
            {
                leaseManager.AcknowledgeLeaseBreak(leaseKey);
                Console.WriteLine($"Force acknowledged lease break for {leaseKey}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to acknowledge lease break: {ex.Message}");
                
                // 强制移除租赁
                leaseManager.RemoveLease(leaseKey);
                Console.WriteLine($"Force removed lease {leaseKey}");
            }
        }
    }
}
```

### 3. LeaseInvalid 错误

#### 3.1 问题症状
- 客户端请求租赁时收到 `LeaseInvalid` 错误
- 日志显示 "Invalid lease request"
- 所有租赁请求都被拒绝

#### 3.2 常见原因
- **错误验证 LeaseDuration**: 代码错误地验证 `LeaseDuration != 0`
- **根据 MS-SMB2 规范**: 客户端必须发送 `LeaseDuration = 0`，服务器应忽略此值

#### 3.3 诊断步骤
```csharp
public class LeaseInvalidDiagnostic
{
    public DiagnosticResult DiagnoseLeaseInvalid(LeaseRequest request)
    {
        var result = new DiagnosticResult
        {
            ProblemType = "LeaseInvalid",
            Timestamp = DateTime.UtcNow
        };

        // 检查 LeaseDuration 验证逻辑
        if (request.LeaseDuration == 0)
        {
            result.Issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.High,
                Category = "InvalidValidation",
                Description = "LeaseDuration validation incorrectly rejects 0 value",
                Recommendation = "Remove LeaseDuration validation - client must send 0 per MS-SMB2 spec"
            });
        }

        // 检查其他验证
        if (request.LeaseKey == Guid.Empty)
        {
            result.Issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.High,
                Category = "InvalidLeaseKey",
                Description = "LeaseKey is empty",
                Recommendation = "Ensure client generates valid GUID for LeaseKey"
            });
        }

        if (request.LeaseState == LeaseState.None)
        {
            result.Issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.High,
                Category = "InvalidLeaseState",
                Description = "LeaseState is None",
                Recommendation = "Client must request valid lease state (ReadCaching, WriteCaching, etc.)"
            });
        }

        return result;
    }
}
```

#### 3.4 解决方案
```csharp
public class LeaseInvalidFixer
{
    public void FixLeaseInvalidValidation()
    {
        Console.WriteLine("=== Fixing LeaseInvalid Error ===");
        Console.WriteLine("1. Remove LeaseDuration validation (client must send 0)");
        Console.WriteLine("2. Ensure LeaseKey is not empty");
        Console.WriteLine("3. Ensure LeaseState is not None");
        Console.WriteLine("4. Update validation logic to match MS-SMB2 spec");
        
        // 正确的验证逻辑示例
        Console.WriteLine("\nCorrect validation logic:");
        Console.WriteLine("if (context.LeaseKey == Guid.Empty) return false;");
        Console.WriteLine("if (context.LeaseState == LeaseState.None) return false;");
        Console.WriteLine("// Note: LeaseDuration validation removed per MS-SMB2 spec");
    }
}
```

### 4. LeaseAlreadyExists 错误

#### 4.1 问题症状
- 客户端重复请求租赁时收到 `LeaseAlreadyExists` 错误
- 日志显示 "Lease already exists"
- 同一会话的重复请求被错误拒绝

#### 4.2 常见原因
- **错误处理重复请求**: 代码没有正确处理同一会话的重复 LeaseKey
- **根据 MS-SMB2 规范**: 同一会话可以重用相同的 LeaseKey

#### 4.3 诊断步骤
```csharp
public class LeaseAlreadyExistsDiagnostic
{
    public DiagnosticResult DiagnoseLeaseAlreadyExists(Guid leaseKey, ulong sessionId)
    {
        var result = new DiagnosticResult
        {
            ProblemType = "LeaseAlreadyExists",
            Timestamp = DateTime.UtcNow
        };

        var leaseManager = GetLeaseManager();
        var existingLease = leaseManager.GetLeaseInfo(leaseKey);

        if (existingLease != null)
        {
            if (existingLease.SessionId == sessionId)
            {
                result.Issues.Add(new DiagnosticIssue
                {
                    Severity = DiagnosticSeverity.High,
                    Category = "SameSessionReuse",
                    Description = "Same session reusing LeaseKey should be allowed",
                    Recommendation = "Return existing lease instead of throwing exception"
                });
            }
            else
            {
                result.Issues.Add(new DiagnosticIssue
                {
                    Severity = DiagnosticSeverity.Medium,
                    Category = "DifferentSession",
                    Description = "Different session using same LeaseKey",
                    Recommendation = "This is correct behavior - different sessions cannot share LeaseKey"
                });
            }
        }

        return result;
    }
}
```

#### 4.4 解决方案
```csharp
public class LeaseAlreadyExistsFixer
{
    public void FixLeaseAlreadyExists()
    {
        Console.WriteLine("=== Fixing LeaseAlreadyExists Error ===");
        Console.WriteLine("1. Check if existing lease belongs to same session");
        Console.WriteLine("2. If same session, return existing lease");
        Console.WriteLine("3. If different session, throw exception (correct behavior)");
        Console.WriteLine("4. Handle expired leases by removing and creating new");
        
        Console.WriteLine("\nCorrect handling logic:");
        Console.WriteLine("if (existingLease.SessionId == request.SessionId)");
        Console.WriteLine("    return existingLease; // Same session reuse");
        Console.WriteLine("else");
        Console.WriteLine("    throw LeaseException; // Different session conflict");
    }
}
```

### 5. 内存泄漏

#### 5.1 问题症状
- 内存使用持续增长
- 垃圾回收频繁
- 系统响应变慢

#### 5.2 诊断步骤
```csharp
public class MemoryLeakDiagnostic
{
    public DiagnosticResult DiagnoseMemoryLeak()
    {
        var result = new DiagnosticResult
        {
            ProblemType = "MemoryLeak",
            Timestamp = DateTime.UtcNow
        };

        // 检查内存使用趋势
        var currentMemory = GC.GetTotalMemory(false);
        var gen2Collections = GC.CollectionCount(2);

        if (gen2Collections > 10)
        {
            result.Issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.High,
                Category = "FrequentGC",
                Description = $"Frequent Gen2 collections: {gen2Collections}",
                Recommendation = "Check for memory leaks in lease management"
            });
        }

        // 检查租赁数量
        var leaseManager = GetLeaseManager();
        var activeLeases = leaseManager.ActiveLeaseCount;
        var expiredLeases = leaseManager.GetActiveLeases().Count(l => l.IsExpired);

        if (expiredLeases > activeLeases * 0.1)
        {
            result.Issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Medium,
                Category = "ExpiredLeases",
                Description = $"High number of expired leases: {expiredLeases}",
                Recommendation = "Clean up expired leases"
            });
        }

        return result;
    }
}
```

#### 3.3 解决方案
```csharp
public class MemoryLeakFixer
{
    public void FixMemoryLeak()
    {
        var leaseManager = GetLeaseManager();
        
        // 清理过期租赁
        var cleanedCount = leaseManager.CleanupExpiredLeases();
        Console.WriteLine($"Cleaned up {cleanedCount} expired leases");

        // 强制垃圾回收
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Console.WriteLine($"Memory after cleanup: {GC.GetTotalMemory(false) / 1024 / 1024}MB");
    }
}
```

### 4. 性能问题

#### 4.1 问题症状
- 租赁操作响应时间过长
- 系统吞吐量低
- CPU 使用率高

#### 4.2 诊断步骤
```csharp
public class PerformanceDiagnostic
{
    public DiagnosticResult DiagnosePerformanceIssues()
    {
        var result = new DiagnosticResult
        {
            ProblemType = "PerformanceIssue",
            Timestamp = DateTime.UtcNow
        };

        var counters = GetPerformanceCounters();
        var stats = counters.GetAllStats();

        // 检查租赁创建性能
        if (stats.TryGetValue("LeaseCreated_Timing", out var createStats))
        {
            if (createStats.AverageTime.TotalMilliseconds > 10)
            {
                result.Issues.Add(new DiagnosticIssue
                {
                    Severity = DiagnosticSeverity.Medium,
                    Category = "SlowLeaseCreation",
                    Description = $"Slow lease creation: {createStats.AverageTime.TotalMilliseconds}ms",
                    Recommendation = "Optimize lease creation process"
                });
            }
        }

        // 检查缓存命中率
        if (stats.TryGetValue("CacheHit", out var hitStats) && 
            stats.TryGetValue("CacheMiss", out var missStats))
        {
            var hitRate = (double)hitStats.Count / (hitStats.Count + missStats.Count);
            if (hitRate < 0.8)
            {
                result.Issues.Add(new DiagnosticIssue
                {
                    Severity = DiagnosticSeverity.Medium,
                    Category = "LowCacheHitRate",
                    Description = $"Low cache hit rate: {hitRate:P2}",
                    Recommendation = "Increase cache size or improve cache strategy"
                });
            }
        }

        return result;
    }
}
```

#### 4.3 解决方案
```csharp
public class PerformanceFixer
{
    public void FixPerformanceIssues(DiagnosticResult diagnostic)
    {
        foreach (var issue in diagnostic.Issues)
        {
            switch (issue.Category)
            {
                case "SlowLeaseCreation":
                    FixSlowLeaseCreation();
                    break;
                case "LowCacheHitRate":
                    FixLowCacheHitRate();
                    break;
            }
        }
    }

    private void FixSlowLeaseCreation()
    {
        // 启用批量处理
        var config = GetConfiguration();
        config.BatchSize = Math.Min(1000, config.BatchSize * 2);
        config.EnableAsyncProcessing = true;
        
        Console.WriteLine("Enabled batch processing and async processing");
    }

    private void FixLowCacheHitRate()
    {
        // 增加缓存大小
        var config = GetConfiguration();
        config.CacheSize = Math.Min(50000, config.CacheSize * 2);
        config.MaxCacheMemory = Math.Min(500 * 1024 * 1024, config.MaxCacheMemory * 2);
        
        Console.WriteLine("Increased cache size");
    }
}
```

### 5. 兼容性问题

#### 5.1 问题症状
- 客户端连接失败
- 协议版本不匹配
- 功能不可用

#### 5.2 诊断步骤
```csharp
public class CompatibilityDiagnostic
{
    public DiagnosticResult DiagnoseCompatibilityIssues(string clientInfo, string serverInfo)
    {
        var result = new DiagnosticResult
        {
            ProblemType = "CompatibilityIssue",
            Timestamp = DateTime.UtcNow
        };

        // 检查协议版本
        var clientVersion = ParseVersion(clientInfo);
        var serverVersion = ParseVersion(serverInfo);

        if (clientVersion < new Version(2, 0))
        {
            result.Issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.High,
                Category = "UnsupportedProtocol",
                Description = "Client does not support SMB 2.0+",
                Recommendation = "Upgrade client or enable SMB 1.0 support"
            });
        }

        // 检查租赁支持
        if (!clientVersion.SupportsLeasing())
        {
            result.Issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Medium,
                Category = "NoLeaseSupport",
                Description = "Client does not support leasing",
                Recommendation = "Disable leasing or upgrade client"
            });
        }

        return result;
    }

    private Version ParseVersion(string versionInfo)
    {
        // 解析版本信息的逻辑
        return new Version(2, 1);
    }
}
```

#### 5.3 解决方案
```csharp
public class CompatibilityFixer
{
    public void FixCompatibilityIssues(DiagnosticResult diagnostic)
    {
        foreach (var issue in diagnostic.Issues)
        {
            switch (issue.Category)
            {
                case "UnsupportedProtocol":
                    FixUnsupportedProtocol();
                    break;
                case "NoLeaseSupport":
                    FixNoLeaseSupport();
                    break;
            }
        }
    }

    private void FixUnsupportedProtocol()
    {
        // 启用 SMB 1.0 支持
        var config = GetConfiguration();
        config.EnableSMB1 = true;
        
        Console.WriteLine("Enabled SMB 1.0 support for compatibility");
    }

    private void FixNoLeaseSupport()
    {
        // 禁用租赁功能
        var config = GetConfiguration();
        config.EnableLeasing = false;
        
        Console.WriteLine("Disabled leasing for compatibility");
    }
}
```

## 故障排除工具

### 1. 诊断工具

```csharp
public class LeaseDiagnosticTool
{
    private readonly LeaseManager m_leaseManager;
    private readonly LeasePerformanceCounters m_counters;

    public LeaseDiagnosticTool(LeaseManager leaseManager, LeasePerformanceCounters counters)
    {
        m_leaseManager = leaseManager;
        m_counters = counters;
    }

    public DiagnosticReport RunFullDiagnostic()
    {
        var report = new DiagnosticReport
        {
            Timestamp = DateTime.UtcNow,
            SystemInfo = new SystemInfoCollector().CollectSystemInfo(),
            LeaseState = new LeaseStateCollector(m_leaseManager).CollectLeaseState(),
            PerformanceInfo = new PerformanceInfoCollector(m_counters).CollectPerformanceInfo()
        };

        // 运行各种诊断
        report.Issues.AddRange(RunLeaseCreationDiagnostic());
        report.Issues.AddRange(RunLeaseBreakDiagnostic());
        report.Issues.AddRange(RunMemoryLeakDiagnostic());
        report.Issues.AddRange(RunPerformanceDiagnostic());

        return report;
    }

    private List<DiagnosticIssue> RunLeaseCreationDiagnostic()
    {
        var issues = new List<DiagnosticIssue>();
        var activeLeases = m_leaseManager.ActiveLeaseCount;
        var maxLeases = m_leaseManager.MaxLeases;

        if (activeLeases >= maxLeases * 0.9)
        {
            issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.High,
                Category = "ResourceExhaustion",
                Description = $"High lease usage: {activeLeases}/{maxLeases}",
                Recommendation = "Consider increasing MaxLeases or cleaning up expired leases"
            });
        }

        return issues;
    }

    private List<DiagnosticIssue> RunLeaseBreakDiagnostic()
    {
        var issues = new List<DiagnosticIssue>();
        var activeLeases = m_leaseManager.GetActiveLeases();
        var breakingLeases = activeLeases.Where(l => l.IsBreaking).ToList();

        if (breakingLeases.Count > 0)
        {
            var timeoutLeases = breakingLeases.Where(l => 
                DateTime.UtcNow - l.CreatedTime > TimeSpan.FromMinutes(5)).ToList();

            if (timeoutLeases.Count > 0)
            {
                issues.Add(new DiagnosticIssue
                {
                    Severity = DiagnosticSeverity.High,
                    Category = "LeaseBreakTimeout",
                    Description = $"{timeoutLeases.Count} leases have been breaking for over 5 minutes",
                    Recommendation = "Force acknowledge timeout leases"
                });
            }
        }

        return issues;
    }

    private List<DiagnosticIssue> RunMemoryLeakDiagnostic()
    {
        var issues = new List<DiagnosticIssue>();
        var memoryUsage = GC.GetTotalMemory(false);
        var gen2Collections = GC.CollectionCount(2);

        if (gen2Collections > 10)
        {
            issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.High,
                Category = "MemoryLeak",
                Description = $"Frequent Gen2 collections: {gen2Collections}",
                Recommendation = "Check for memory leaks and clean up resources"
            });
        }

        return issues;
    }

    private List<DiagnosticIssue> RunPerformanceDiagnostic()
    {
        var issues = new List<DiagnosticIssue>();
        var stats = m_counters.GetAllStats();

        if (stats.TryGetValue("LeaseCreated_Timing", out var createStats))
        {
            if (createStats.AverageTime.TotalMilliseconds > 10)
            {
                issues.Add(new DiagnosticIssue
                {
                    Severity = DiagnosticSeverity.Medium,
                    Category = "Performance",
                    Description = $"Slow lease creation: {createStats.AverageTime.TotalMilliseconds}ms",
                    Recommendation = "Optimize lease creation process"
                });
            }
        }

        return issues;
    }
}
```

### 2. 修复工具

```csharp
public class LeaseFixTool
{
    private readonly LeaseManager m_leaseManager;
    private readonly LeasePerformanceCounters m_counters;

    public LeaseFixTool(LeaseManager leaseManager, LeasePerformanceCounters counters)
    {
        m_leaseManager = leaseManager;
        m_counters = counters;
    }

    public void FixAllIssues(DiagnosticReport report)
    {
        foreach (var issue in report.Issues)
        {
            FixIssue(issue);
        }
    }

    private void FixIssue(DiagnosticIssue issue)
    {
        switch (issue.Category)
        {
            case "ResourceExhaustion":
                FixResourceExhaustion();
                break;
            case "LeaseBreakTimeout":
                FixLeaseBreakTimeout();
                break;
            case "MemoryLeak":
                FixMemoryLeak();
                break;
            case "Performance":
                FixPerformanceIssue();
                break;
        }
    }

    private void FixResourceExhaustion()
    {
        var cleanedCount = m_leaseManager.CleanupExpiredLeases();
        Console.WriteLine($"Cleaned up {cleanedCount} expired leases");
    }

    private void FixLeaseBreakTimeout()
    {
        var activeLeases = m_leaseManager.GetActiveLeases();
        var timeoutLeases = activeLeases.Where(l => 
            l.IsBreaking && DateTime.UtcNow - l.CreatedTime > TimeSpan.FromMinutes(5)).ToList();

        foreach (var lease in timeoutLeases)
        {
            try
            {
                m_leaseManager.AcknowledgeLeaseBreak(lease.LeaseKey);
                Console.WriteLine($"Force acknowledged timeout lease: {lease.LeaseKey}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to acknowledge lease {lease.LeaseKey}: {ex.Message}");
            }
        }
    }

    private void FixMemoryLeak()
    {
        // 清理过期租赁
        var cleanedCount = m_leaseManager.CleanupExpiredLeases();
        Console.WriteLine($"Cleaned up {cleanedCount} expired leases");

        // 强制垃圾回收
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Console.WriteLine($"Memory after cleanup: {GC.GetTotalMemory(false) / 1024 / 1024}MB");
    }

    private void FixPerformanceIssue()
    {
        // 优化配置
        var config = GetConfiguration();
        config.BatchSize = Math.Min(1000, config.BatchSize * 2);
        config.EnableAsyncProcessing = true;
        
        Console.WriteLine("Applied performance optimizations");
    }
}
```

### 3. 监控工具

```csharp
public class LeaseMonitoringTool
{
    private readonly LeaseManager m_leaseManager;
    private readonly LeasePerformanceCounters m_counters;
    private readonly Timer m_monitoringTimer;

    public LeaseMonitoringTool(LeaseManager leaseManager, LeasePerformanceCounters counters)
    {
        m_leaseManager = leaseManager;
        m_counters = counters;
        m_monitoringTimer = new Timer(MonitorLeaseSystem, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private void MonitorLeaseSystem(object state)
    {
        var activeLeases = m_leaseManager.ActiveLeaseCount;
        var expiredLeases = m_leaseManager.GetActiveLeases().Count(l => l.IsExpired);
        var breakingLeases = m_leaseManager.GetActiveLeases().Count(l => l.IsBreaking);
        var memoryUsage = GC.GetTotalMemory(false);

        Console.WriteLine($"=== Lease System Status ===");
        Console.WriteLine($"Active Leases: {activeLeases}");
        Console.WriteLine($"Expired Leases: {expiredLeases}");
        Console.WriteLine($"Breaking Leases: {breakingLeases}");
        Console.WriteLine($"Memory Usage: {memoryUsage / 1024 / 1024}MB");
        Console.WriteLine($"Timestamp: {DateTime.UtcNow}");
        Console.WriteLine();

        // 检查异常情况
        if (expiredLeases > activeLeases * 0.1)
        {
            Console.WriteLine("WARNING: High number of expired leases detected");
        }

        if (breakingLeases > 0)
        {
            Console.WriteLine("WARNING: Leases are in breaking state");
        }

        if (memoryUsage > 500 * 1024 * 1024) // 500MB
        {
            Console.WriteLine("WARNING: High memory usage detected");
        }
    }
}
```

## 预防措施

### 1. 健康检查

```csharp
public class LeaseHealthCheck
{
    private readonly LeaseManager m_leaseManager;
    private readonly LeasePerformanceCounters m_counters;

    public LeaseHealthCheck(LeaseManager leaseManager, LeasePerformanceCounters counters)
    {
        m_leaseManager = leaseManager;
        m_counters = counters;
    }

    public HealthStatus CheckHealth()
    {
        var status = new HealthStatus
        {
            IsHealthy = true,
            Issues = new List<string>(),
            Timestamp = DateTime.UtcNow
        };

        // 检查租赁数量
        var activeLeases = m_leaseManager.ActiveLeaseCount;
        var maxLeases = m_leaseManager.MaxLeases;
        if (activeLeases >= maxLeases * 0.9)
        {
            status.IsHealthy = false;
            status.Issues.Add($"High lease usage: {activeLeases}/{maxLeases}");
        }

        // 检查过期租赁
        var expiredLeases = m_leaseManager.GetActiveLeases().Count(l => l.IsExpired);
        if (expiredLeases > activeLeases * 0.1)
        {
            status.IsHealthy = false;
            status.Issues.Add($"High number of expired leases: {expiredLeases}");
        }

        // 检查内存使用
        var memoryUsage = GC.GetTotalMemory(false);
        if (memoryUsage > 500 * 1024 * 1024) // 500MB
        {
            status.IsHealthy = false;
            status.Issues.Add($"High memory usage: {memoryUsage / 1024 / 1024}MB");
        }

        return status;
    }
}

public class HealthStatus
{
    public bool IsHealthy { get; set; }
    public List<string> Issues { get; set; }
    public DateTime Timestamp { get; set; }
}
```

### 2. 自动修复

```csharp
public class AutoFixManager
{
    private readonly LeaseManager m_leaseManager;
    private readonly Timer m_autoFixTimer;

    public AutoFixManager(LeaseManager leaseManager)
    {
        m_leaseManager = leaseManager;
        m_autoFixTimer = new Timer(AutoFix, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    private void AutoFix(object state)
    {
        try
        {
            // 自动清理过期租赁
            var cleanedCount = m_leaseManager.CleanupExpiredLeases();
            if (cleanedCount > 0)
            {
                Console.WriteLine($"Auto-cleaned {cleanedCount} expired leases");
            }

            // 自动垃圾回收
            if (GC.GetTotalMemory(false) > 200 * 1024 * 1024) // 200MB
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Console.WriteLine("Auto-performed garbage collection");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Auto-fix failed: {ex.Message}");
        }
    }
}
```

## 总结

本故障排除指南提供了全面的 SMB 2.0/2.1 租赁协议问题诊断和解决方案，包括：

1. **系统化的故障排除流程**: 问题识别、信息收集、诊断、解决
2. **常见问题诊断**: 租赁创建失败、中断超时、内存泄漏、性能问题、兼容性问题
3. **诊断工具**: 系统信息收集、状态检查、性能分析
4. **修复工具**: 自动修复、手动修复、配置优化
5. **监控工具**: 实时监控、健康检查、预警机制
6. **预防措施**: 健康检查、自动修复、最佳实践

通过遵循本指南，可以快速定位和解决租赁协议相关的问题，确保系统的稳定运行和高可用性。
