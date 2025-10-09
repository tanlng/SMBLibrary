# Lease Break Handler API

## 概述

`LeaseBreakHandler` 是 SMB 2.0/2.1 租赁协议中负责处理租赁中断的核心组件。它管理租赁中断通知、处理中断确认、监控超时，并确保租赁中断流程的正确执行。

## 类定义

```csharp
public class LeaseBreakHandler
```

## 构造函数

### LeaseBreakHandler(LeaseManager leaseManager)

创建新的租赁中断处理器实例。

**参数：**
- `leaseManager` (LeaseManager): 租赁管理器实例，不能为 null

**异常：**
- `ArgumentNullException`: 当 leaseManager 为 null 时抛出

## 事件

### LeaseBreakTimeout

租赁中断超时事件。

**事件类型：**
```csharp
public event EventHandler<LeaseBreakTimeoutEventArgs> LeaseBreakTimeout;
```

**事件参数：**
- `LeaseBreakTimeoutEventArgs`: 包含租赁键和超时时间

## 公共方法

### NotifyLeaseBreak(LeaseInfo leaseInfo, LeaseBreakReason reason)

通知租赁中断。

**参数：**
- `leaseInfo` (LeaseInfo): 要中断的租赁信息
- `reason` (LeaseBreakReason): 中断原因

**异常：**
- `ArgumentNullException`: 当 leaseInfo 为 null 时抛出

**示例：**
```csharp
var handler = new LeaseBreakHandler(leaseManager);
handler.NotifyLeaseBreak(leaseInfo, LeaseBreakReason.WriteRequest);
```

### ProcessLeaseBreakAcknowledgment(LeaseBreakAck ack)

处理租赁中断确认。

**参数：**
- `ack` (LeaseBreakAck): 租赁中断确认命令

**异常：**
- `ArgumentNullException`: 当 ack 为 null 时抛出
- `LeaseException`: 当处理确认失败时抛出

**示例：**
```csharp
var ack = new LeaseBreakAck();
ack.LeaseKey = leaseKey;
handler.ProcessLeaseBreakAcknowledgment(ack);
```

### HandleLeaseBreakTimeout(LeaseInfo leaseInfo)

处理租赁中断超时。

**参数：**
- `leaseInfo` (LeaseInfo): 超时的租赁信息

**异常：**
- `ArgumentNullException`: 当 leaseInfo 为 null 时抛出
- `LeaseException`: 当处理超时失败时抛出

**示例：**
```csharp
handler.HandleLeaseBreakTimeout(leaseInfo);
```

### Dispose()

释放资源。

**说明：**
- 停止超时检查定时器
- 清理相关资源

## 私有方法

### SendLeaseBreakNotification(LeaseInfo leaseInfo, LeaseBreakReason reason)

发送租赁中断通知。

**参数：**
- `leaseInfo` (LeaseInfo): 租赁信息
- `reason` (LeaseBreakReason): 中断原因

**说明：**
- 此方法是一个占位符，实际实现将连接到 SMB 服务器的网络发送机制
- 在 SMB2Session 类中集成时，将实现实际的网络发送逻辑

### CheckTimeouts(object state)

检查超时。

**参数：**
- `state` (object): 定时器状态（未使用）

**说明：**
- 每 30 秒执行一次
- 检查所有待处理的租赁中断是否超时（5分钟）
- 超时的租赁将被强制确认中断

## 使用示例

### 基本用法

```csharp
// 创建租赁中断处理器
var leaseManager = new LeaseManager();
var breakHandler = new LeaseBreakHandler(leaseManager);

// 订阅超时事件
breakHandler.LeaseBreakTimeout += (sender, e) =>
{
    Console.WriteLine($"Lease {e.LeaseKey} timed out");
};

// 通知租赁中断
breakHandler.NotifyLeaseBreak(leaseInfo, LeaseBreakReason.WriteRequest);
```

### 处理中断确认

```csharp
// 处理客户端发送的中断确认
var ack = new LeaseBreakAck();
ack.LeaseKey = leaseKey;
ack.CurrentLeaseState = currentState;
ack.NewLeaseState = newState;

try
{
    breakHandler.ProcessLeaseBreakAcknowledgment(ack);
    Console.WriteLine("Lease break acknowledged successfully");
}
catch (LeaseException ex)
{
    Console.WriteLine($"Failed to acknowledge lease break: {ex.Message}");
}
```

### 错误处理

```csharp
try
{
    breakHandler.NotifyLeaseBreak(leaseInfo, LeaseBreakReason.WriteRequest);
}
catch (ArgumentNullException ex)
{
    Console.WriteLine($"Invalid argument: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"Unexpected error: {ex.Message}");
}
```

## 集成说明

### 与 SMB2Session 的集成

`LeaseBreakHandler` 通常与 `SMB2Session` 一起使用：

```csharp
public class SMB2Session
{
    private LeaseBreakHandler m_leaseBreakHandler;
    
    public SMB2Session(SMB2ConnectionState connection, ...)
    {
        // 初始化租赁中断处理器
        var leaseManager = new LeaseManager(config);
        m_leaseBreakHandler = new LeaseBreakHandler(leaseManager);
        
        // 订阅超时事件
        m_leaseBreakHandler.LeaseBreakTimeout += OnLeaseBreakTimeout;
    }
    
    private void OnLeaseBreakTimeout(object sender, LeaseBreakTimeoutEventArgs e)
    {
        LogToServer(Severity.Warning, "Lease {0} break timed out", e.LeaseKey);
    }
    
    public void ProcessLeaseBreakAck(LeaseBreakAck ack)
    {
        m_leaseBreakHandler.ProcessLeaseBreakAcknowledgment(ack);
    }
}
```

### 与 SMB 服务器的集成

在 SMB 服务器的命令处理中集成：

```csharp
public static SMB2Command ProcessLeaseBreakAck(LeaseBreakAck request, SMB2ConnectionState state)
{
    var session = state.GetSession(request.Header.SessionID);
    
    try
    {
        // 处理租赁中断确认
        session.LeaseBreakHandler.ProcessLeaseBreakAcknowledgment(request);
        
        // 返回成功响应
        return new LeaseBreakResponse();
    }
    catch (LeaseException ex)
    {
        // 返回错误响应
        return new ErrorResponse(SMB2CommandName.OplockBreak, 
            LeaseHelper.ConvertLeaseErrorToNTStatus(ex.ErrorCode));
    }
}
```

## 超时机制

### 超时检查

- **检查间隔**: 每 30 秒检查一次
- **超时时间**: 5 分钟
- **超时处理**: 强制确认租赁中断，触发超时事件

### 超时事件

```csharp
public class LeaseBreakTimeoutEventArgs : EventArgs
{
    public Guid LeaseKey { get; set; }
    public DateTime TimeoutTime { get; set; }
    
    public LeaseBreakTimeoutEventArgs(Guid leaseKey)
    {
        LeaseKey = leaseKey;
        TimeoutTime = DateTime.UtcNow;
    }
}
```

## 性能考虑

### 内存使用

- `LeaseBreakHandler` 维护一个待处理中断的字典
- 内存使用量与同时进行的租赁中断数量成正比
- 超时检查使用定时器，不会阻塞主线程

### 线程安全

- `LeaseBreakHandler` 使用锁保护待处理中断字典
- 定时器回调在后台线程中执行
- 支持多线程访问

## 最佳实践

1. **资源管理**: 确保在会话关闭时调用 `Dispose()` 方法
2. **事件处理**: 订阅 `LeaseBreakTimeout` 事件以处理超时情况
3. **错误处理**: 始终处理可能的异常
4. **超时配置**: 根据网络环境调整超时时间
5. **日志记录**: 记录重要的租赁中断事件

## 相关类型

- `LeaseManager`: 租赁管理器
- `LeaseInfo`: 租赁信息
- `LeaseBreakAck`: 租赁中断确认命令
- `LeaseBreakReason`: 租赁中断原因枚举
- `LeaseBreakTimeoutEventArgs`: 租赁中断超时事件参数
- `LeaseException`: 租赁异常
