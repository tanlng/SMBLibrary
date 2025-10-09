# 租赁管理器 API 文档

## 概述

租赁管理器 (LeaseManager) 是 SMBLibrary 中负责管理 SMB 2.0/2.1 租赁协议的核心组件。它提供了完整的租赁生命周期管理功能，包括租赁创建、状态跟踪、中断处理和清理。

**重要**: 租赁协议是**可选功能**。默认情况下租赁协议是**禁用**的，只有在 `SMBServer.LeaseConfiguration` 属性被设置后才会启用租赁管理器。

## 命名空间

```csharp
namespace SMBLibrary.Server.Leasing
```

## 核心类

### LeaseManager

租赁管理器的主要类，负责管理所有租赁操作。

#### 构造函数

```csharp
public LeaseManager()
```

创建一个新的租赁管理器实例。

#### 属性

| 属性名 | 类型 | 描述 |
|--------|------|------|
| `ActiveLeaseCount` | `int` | 当前活跃租赁数量 |
| `MaxLeases` | `int` | 最大允许租赁数量 |
| `DefaultLeaseDuration` | `TimeSpan` | **租赁生命周期**: 从创建到自动过期的时间，控制客户端可以缓存数据的最长时间 |
| `LeaseBreakTimeout` | `TimeSpan` | **租赁中断超时**: 主动中断租赁时，等待客户端响应的最长时间，超时后强制中断 |

#### 方法

##### CreateLease

创建新的租赁。

```csharp
public LeaseInfo CreateLease(LeaseRequest request)
```

**参数:**
- `request` (LeaseRequest): 租赁请求信息

**返回值:**
- `LeaseInfo`: 创建的租赁信息

**异常:**
- `LeaseException`: 当租赁创建失败时抛出
- `ArgumentException`: 当请求参数无效时抛出

**示例:**
```csharp
var request = new LeaseRequest
{
    LeaseKey = Guid.NewGuid(),
    LeaseState = LeaseState.ReadCaching,
    FilePath = "/shared/document.txt",
    SessionId = sessionId,
    FileId = fileId
};

var lease = leaseManager.CreateLease(request);
```

##### BreakLease

中断指定的租赁。

```csharp
public void BreakLease(Guid leaseKey, LeaseBreakReason reason)
```

**参数:**
- `leaseKey` (Guid): 租赁键
- `reason` (LeaseBreakReason): 中断原因

**异常:**
- `LeaseNotFoundException`: 当租赁不存在时抛出
- `InvalidOperationException`: 当租赁状态不允许中断时抛出

**示例:**
```csharp
leaseManager.BreakLease(leaseKey, LeaseBreakReason.WriteRequest);
```

##### AcknowledgeLeaseBreak

确认租赁中断。

```csharp
public void AcknowledgeLeaseBreak(Guid leaseKey)
```

**参数:**
- `leaseKey` (Guid): 租赁键

**异常:**
- `LeaseNotFoundException`: 当租赁不存在时抛出
- `InvalidOperationException`: 当租赁状态不允许确认时抛出

##### GetLeaseInfo

获取指定租赁的信息。

```csharp
public LeaseInfo GetLeaseInfo(Guid leaseKey)
```

**参数:**
- `leaseKey` (Guid): 租赁键

**返回值:**
- `LeaseInfo`: 租赁信息，如果不存在则返回 null

##### GetActiveLeases

获取所有活跃租赁的列表。

```csharp
public List<LeaseInfo> GetActiveLeases()
```

**返回值:**
- `List<LeaseInfo>`: 活跃租赁列表

##### GetLeasesBySession

获取指定会话的所有租赁。

```csharp
public List<LeaseInfo> GetLeasesBySession(ulong sessionId)
```

**参数:**
- `sessionId` (ulong): 会话ID

**返回值:**
- `List<LeaseInfo>`: 该会话的租赁列表

##### GetLeasesByFile

获取指定文件的所有租赁。

```csharp
public List<LeaseInfo> GetLeasesByFile(FileID fileId)
```

**参数:**
- `fileId` (FileID): 文件ID

**返回值:**
- `List<LeaseInfo>`: 该文件的租赁列表

##### RemoveLease

移除指定的租赁。

```csharp
public bool RemoveLease(Guid leaseKey)
```

**参数:**
- `leaseKey` (Guid): 租赁键

**返回值:**
- `bool`: 如果成功移除返回 true，否则返回 false

##### CleanupExpiredLeases

清理过期的租赁。

```csharp
public int CleanupExpiredLeases()
```

**返回值:**
- `int`: 清理的租赁数量

#### 事件

##### LeaseBreakRequested

当需要中断租赁时触发。

```csharp
public event EventHandler<LeaseBreakEventArgs> LeaseBreakRequested;
```

**事件参数:**
- `LeaseBreakEventArgs`: 包含租赁键和中断原因

##### LeaseExpired

当租赁过期时触发。

```csharp
public event EventHandler<LeaseExpiredEventArgs> LeaseExpired;
```

**事件参数:**
- `LeaseExpiredEventArgs`: 包含过期的租赁信息

##### LeaseCreated

当新租赁创建时触发。

```csharp
public event EventHandler<LeaseCreatedEventArgs> LeaseCreated;
```

**事件参数:**
- `LeaseCreatedEventArgs`: 包含新创建的租赁信息

## 数据结构

### LeaseInfo

租赁信息类，包含租赁的完整状态信息。

```csharp
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
}
```

### LeaseRequest

租赁请求类，用于创建新租赁。

```csharp
public class LeaseRequest
{
    public Guid LeaseKey { get; set; }
    public LeaseState LeaseState { get; set; }
    public LeaseFlags LeaseFlags { get; set; }
    public TimeSpan LeaseDuration { get; set; }
    public string FilePath { get; set; }
    public ulong SessionId { get; set; }
    public FileID FileId { get; set; }
    public AccessMask DesiredAccess { get; set; }
    public ShareAccess ShareAccess { get; set; }
}
```

### LeaseBreakEventArgs

租赁中断事件参数。

```csharp
public class LeaseBreakEventArgs : EventArgs
{
    public Guid LeaseKey { get; set; }
    public LeaseBreakReason Reason { get; set; }
    public DateTime BreakTime { get; set; }
}
```

### LeaseExpiredEventArgs

租赁过期事件参数。

```csharp
public class LeaseExpiredEventArgs : EventArgs
{
    public LeaseInfo LeaseInfo { get; set; }
    public DateTime ExpirationTime { get; set; }
}
```

## 枚举类型

### LeaseState

租赁状态枚举。

```csharp
[Flags]
public enum LeaseState : uint
{
    None = 0x00000000,
    ReadCaching = 0x00000001,
    HandleCaching = 0x00000002,
    WriteCaching = 0x00000004,
    DirectoryCaching = 0x00000008
}
```

### LeaseFlags

租赁标志枚举。

```csharp
[Flags]
public enum LeaseFlags : uint
{
    None = 0x00000000,
    BreakInProgress = 0x00000002,
    ParentLeaseKeySet = 0x00000004,
    DirectoryLease = 0x00000008
}
```

### LeaseBreakReason

租赁中断原因枚举。

```csharp
public enum LeaseBreakReason
{
    None = 0,
    WriteRequest = 1,
    HandleClose = 2,
    SessionLogoff = 3,
    FileDelete = 4,
    FileRename = 5,
    FileMove = 6,
    DirectoryRename = 7,
    DirectoryMove = 8,
    LeaseExpired = 9,
    ServerShutdown = 10
}
```

## 异常类型

### LeaseException

租赁相关异常的基类。

```csharp
public class LeaseException : Exception
{
    public Guid LeaseKey { get; set; }
    public LeaseErrorCode ErrorCode { get; set; }
    
    public LeaseException(string message, Guid leaseKey, LeaseErrorCode errorCode)
        : base(message)
    {
        LeaseKey = leaseKey;
        ErrorCode = errorCode;
    }
}
```

### LeaseNotFoundException

当租赁不存在时抛出的异常。

```csharp
public class LeaseNotFoundException : LeaseException
{
    public LeaseNotFoundException(Guid leaseKey)
        : base($"Lease not found: {leaseKey}", leaseKey, LeaseErrorCode.LeaseNotFound)
    {
    }
}
```

### LeaseExpiredException

当租赁已过期时抛出的异常。

```csharp
public class LeaseExpiredException : LeaseException
{
    public LeaseExpiredException(Guid leaseKey)
        : base($"Lease expired: {leaseKey}", leaseKey, LeaseErrorCode.LeaseExpired)
    {
    }
}
```

### LeaseErrorCode

租赁错误代码枚举。

```csharp
public enum LeaseErrorCode
{
    None = 0,
    LeaseNotFound = 1,
    LeaseExpired = 2,
    LeaseInvalid = 3,
    LeaseBreakInProgress = 4,
    LeaseAlreadyExists = 5,
    LeasePermissionDenied = 6,
    LeaseResourceExhausted = 7
}
```

## 配置选项

### LeaseManagerConfiguration

租赁管理器配置类。

```csharp
public class LeaseManagerConfiguration
{
    public int MaxLeases { get; set; } = 10000;
    public TimeSpan DefaultLeaseDuration { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan LeaseBreakTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
    public bool EnableLeaseBreakNotifications { get; set; } = true;
    public bool EnableLeaseExpirationEvents { get; set; } = true;
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
}
```

#### 配置属性详解

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `MaxLeases` | `int` | `10000` | 系统允许的最大租赁数量 |
| `DefaultLeaseDuration` | `TimeSpan` | `30分钟` | **租赁生命周期**: 从租赁创建开始计时，到达此时间后租赁自动过期。这是租赁的**自然过期时间**。 |
| `LeaseBreakTimeout` | `TimeSpan` | `30秒` | **租赁中断超时**: 当需要主动中断租赁时（如文件冲突），等待客户端响应的最长时间。超时后服务器强制中断租赁。 |
| `CleanupInterval` | `TimeSpan` | `5分钟` | 清理过期租赁的检查间隔 |
| `EnableLeaseBreakNotifications` | `bool` | `true` | 是否启用租赁中断通知 |
| `EnableLeaseExpirationEvents` | `bool` | `true` | 是否启用租赁过期事件 |
| `LogLevel` | `LogLevel` | `Information` | 日志级别 |

#### DefaultLeaseDuration vs LeaseBreakTimeout

这两个参数容易混淆，但它们控制的是**完全不同的时间周期**：

**DefaultLeaseDuration（租赁生命周期）**
- 📅 **计时起点**: 租赁创建时
- ⏱️ **适用场景**: 所有租赁，无论是否发生冲突
- 🎯 **用途**: 控制客户端可以缓存文件数据的最长时间
- ⚡ **到期效果**: 租赁自动过期失效，客户端需要重新请求
- 📊 **典型值**: 3秒（测试）到 30分钟（生产）

**LeaseBreakTimeout（租赁中断超时）**
- 📅 **计时起点**: 发送租赁中断通知时
- ⏱️ **适用场景**: 仅当需要主动中断租赁时（如另一个客户端请求独占访问）
- 🎯 **用途**: 防止客户端无响应导致租赁中断流程卡住
- ⚡ **到期效果**: 强制中断租赁，不再等待客户端确认
- 📊 **典型值**: 10秒 到 60秒

**重要**: `DefaultLeaseDuration` **不影响**租赁中断速度。即使设置了 30 分钟的生命周期，服务器仍可以在任何时候立即中断租赁（由 `LeaseBreakTimeout` 控制中断流程的超时）。

## 使用示例

### 基本使用

```csharp
// 创建租赁管理器
var leaseManager = new LeaseManager();

// 订阅事件
leaseManager.LeaseBreakRequested += OnLeaseBreakRequested;
leaseManager.LeaseExpired += OnLeaseExpired;

// 创建租赁
var request = new LeaseRequest
{
    LeaseKey = Guid.NewGuid(),
    LeaseState = LeaseState.ReadCaching,
    FilePath = "/shared/document.txt",
    SessionId = 12345,
    FileId = new FileID { Volatile = 1, Persistent = 1 },
    LeaseDuration = TimeSpan.FromMinutes(30)
};

var lease = leaseManager.CreateLease(request);

// 中断租赁
leaseManager.BreakLease(lease.LeaseKey, LeaseBreakReason.WriteRequest);

// 确认中断
leaseManager.AcknowledgeLeaseBreak(lease.LeaseKey);
```

### 事件处理

```csharp
private void OnLeaseBreakRequested(object sender, LeaseBreakEventArgs e)
{
    Console.WriteLine($"Lease break requested: {e.LeaseKey}, Reason: {e.Reason}");
    
    // 通知客户端租赁中断
    NotifyClientLeaseBreak(e.LeaseKey, e.Reason);
}

private void OnLeaseExpired(object sender, LeaseExpiredEventArgs e)
{
    Console.WriteLine($"Lease expired: {e.LeaseInfo.LeaseKey}");
    
    // 清理过期租赁
    CleanupExpiredLease(e.LeaseInfo);
}
```

### 批量操作

```csharp
// 获取会话的所有租赁
var sessionLeases = leaseManager.GetLeasesBySession(sessionId);

// 批量中断租赁
foreach (var lease in sessionLeases)
{
    leaseManager.BreaseLease(lease.LeaseKey, LeaseBreakReason.SessionLogoff);
}

// 清理过期租赁
var cleanedCount = leaseManager.CleanupExpiredLeases();
Console.WriteLine($"Cleaned up {cleanedCount} expired leases");
```

## 性能考虑

### 内存使用
- 每个租赁对象约占用 200-300 字节内存
- 建议设置合理的最大租赁数量限制
- 定期清理过期租赁以释放内存

### 并发性能
- 使用读写锁保护租赁数据
- 支持高并发读取操作
- 写入操作需要独占锁

### 网络性能
- 批量处理租赁中断通知
- 异步处理租赁操作
- 压缩租赁上下文数据

## 最佳实践

### 1. 租赁生命周期管理
- 及时清理过期租赁
- 正确处理租赁中断
- 避免租赁泄漏

### 2. 错误处理
- 捕获并处理所有租赁异常
- 提供适当的错误恢复机制
- 记录详细的错误日志

### 3. 性能优化
- 设置合理的租赁超时时间
- 限制最大租赁数量
- 使用异步操作处理租赁中断

### 4. 安全考虑
- 验证租赁权限
- 使用安全的租赁键生成
- 实现审计日志

## 故障排除

### 常见问题

#### 1. 租赁创建失败
**原因**: 达到最大租赁数量限制
**解决方案**: 增加 MaxLeases 配置或清理过期租赁

#### 2. 租赁中断超时
**原因**: 客户端未及时响应中断通知
**解决方案**: 调整 LeaseBreakTimeout 配置

#### 3. 内存使用过高
**原因**: 大量租赁未及时清理
**解决方案**: 减少 DefaultLeaseDuration 或增加清理频率

### 调试技巧

#### 1. 启用详细日志
```csharp
var config = new LeaseManagerConfiguration
{
    LogLevel = LogLevel.Debug
};
```

#### 2. 监控租赁状态
```csharp
var activeLeases = leaseManager.GetActiveLeases();
Console.WriteLine($"Active leases: {activeLeases.Count}");
```

#### 3. 检查租赁信息
```csharp
var lease = leaseManager.GetLeaseInfo(leaseKey);
if (lease != null)
{
    Console.WriteLine($"Lease state: {lease.State}");
    Console.WriteLine($"Expiration: {lease.ExpirationTime}");
}
```

## 版本历史

| 版本 | 日期 | 变更 |
|------|------|------|
| 1.0.0 | 2024-01-01 | 初始版本 |
| 1.1.0 | 2024-02-01 | 添加批量操作支持 |
| 1.2.0 | 2024-03-01 | 优化性能和内存使用 |

## 相关文档

- [SMB 2.0/2.1 租赁协议规范](smb2-lease-protocol.md)
- [租赁协议架构设计](smb2-lease-architecture.md)
- [租赁协议实现指南](../guides/implementation/lease-implementation-guide.md)
- [租赁协议测试策略](../guides/testing/lease-testing-strategy.md)
