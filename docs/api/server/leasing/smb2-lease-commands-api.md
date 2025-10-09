# SMB2 Lease Commands API

## 概述

SMB 2.0/2.1 租赁协议定义了三个主要的 SMB2 命令来处理租赁操作：`LeaseBreakRequest`、`LeaseBreakResponse` 和 `LeaseBreakAck`。这些命令实现了租赁中断的完整流程。

## 命令结构

### LeaseBreakRequest

租赁中断请求命令，用于服务器向客户端发送租赁中断通知。

```csharp
public class LeaseBreakRequest : SMB2Command
```

**字段：**
- `StructureSize` (ushort): 结构大小，固定为 24
- `Reserved` (ushort): 保留字段
- `LeaseKey` (Guid): 租赁键
- `CurrentLeaseState` (LeaseState): 当前租赁状态
- `NewLeaseState` (LeaseState): 新租赁状态
- `LeaseFlags` (LeaseFlags): 租赁标志
- `LeaseDuration` (ulong): 租赁持续时间（毫秒）

**构造函数：**
```csharp
// 默认构造函数
public LeaseBreakRequest() : base(SMB2CommandName.OplockBreak)

// 从字节数组构造
public LeaseBreakRequest(byte[] buffer, int offset) : base(buffer, offset)
```

**方法：**
```csharp
// 写入命令字节
public override void WriteCommandBytes(byte[] buffer, int offset)

// 获取命令长度
public override int CommandLength => FixedLength
```

### LeaseBreakResponse

租赁中断响应命令，用于客户端向服务器确认租赁中断。

```csharp
public class LeaseBreakResponse : SMB2Command
```

**字段：**
- `StructureSize` (ushort): 结构大小，固定为 24
- `Reserved` (ushort): 保留字段
- `LeaseKey` (Guid): 租赁键
- `CurrentLeaseState` (LeaseState): 当前租赁状态
- `NewLeaseState` (LeaseState): 新租赁状态
- `LeaseFlags` (LeaseFlags): 租赁标志
- `LeaseDuration` (ulong): 租赁持续时间（毫秒）

**构造函数：**
```csharp
// 默认构造函数
public LeaseBreakResponse() : base(SMB2CommandName.OplockBreak)

// 从字节数组构造
public LeaseBreakResponse(byte[] buffer, int offset) : base(buffer, offset)
```

### LeaseBreakAck

租赁中断确认命令，用于客户端向服务器发送租赁中断确认。

```csharp
public class LeaseBreakAck : SMB2Command
```

**字段：**
- `StructureSize` (ushort): 结构大小，固定为 24
- `Reserved` (ushort): 保留字段
- `LeaseKey` (Guid): 租赁键
- `CurrentLeaseState` (LeaseState): 当前租赁状态
- `NewLeaseState` (LeaseState): 新租赁状态
- `LeaseFlags` (LeaseFlags): 租赁标志
- `LeaseDuration` (ulong): 租赁持续时间（毫秒）

## 使用示例

### 创建租赁中断请求

```csharp
// 创建租赁中断请求
var request = new LeaseBreakRequest();
request.LeaseKey = leaseKey;
request.CurrentLeaseState = LeaseState.ReadCaching | LeaseState.WriteCaching;
request.NewLeaseState = LeaseState.ReadCaching; // 降级为只读
request.LeaseFlags = LeaseFlags.BreakInProgress;
request.LeaseDuration = 0; // 中断时不设置持续时间

// 设置 SMB2 头部
request.Header.Command = SMB2CommandName.OplockBreak;
request.Header.SessionID = sessionId;
request.Header.TreeID = 0; // 租赁中断不绑定到特定树
request.Header.CreditCharge = 1;
request.Header.Credits = 1;
```

### 处理租赁中断响应

```csharp
// 处理客户端发送的租赁中断响应
public static void ProcessLeaseBreakResponse(LeaseBreakResponse response, SMB2ConnectionState state)
{
    var session = state.GetSession(response.Header.SessionID);
    
    // 验证租赁键
    var leaseInfo = session.LeaseManager.GetLeaseInfo(response.LeaseKey);
    if (leaseInfo == null)
    {
        // 租赁不存在，忽略响应
        return;
    }
    
    // 更新租赁状态
    leaseInfo.State = response.NewLeaseState;
    leaseInfo.Flags = response.LeaseFlags;
    
    // 记录日志
    session.LogToServer(Severity.Debug, 
        "Lease break response received: {0}, new state: {1}", 
        response.LeaseKey, response.NewLeaseState);
}
```

### 处理租赁中断确认

```csharp
// 处理客户端发送的租赁中断确认
public static void ProcessLeaseBreakAck(LeaseBreakAck ack, SMB2ConnectionState state)
{
    var session = state.GetSession(ack.Header.SessionID);
    
    try
    {
        // 处理确认
        session.LeaseBreakHandler.ProcessLeaseBreakAcknowledgment(ack);
        
        // 记录日志
        session.LogToServer(Severity.Debug, 
            "Lease break acknowledged: {0}", ack.LeaseKey);
    }
    catch (LeaseException ex)
    {
        // 记录错误
        session.LogToServer(Severity.Error, 
            "Failed to acknowledge lease break {0}: {1}", 
            ack.LeaseKey, ex.Message);
    }
}
```

## 网络发送

### 发送租赁中断请求

```csharp
// 在 SMB2Session 中发送租赁中断通知
private void SendLeaseBreakNotification(Guid leaseKey, LeaseBreakReason reason)
{
    try
    {
        // 创建租赁中断请求
        var request = new LeaseBreakRequest();
        request.LeaseKey = leaseKey;
        request.CurrentLeaseState = leaseInfo.State;
        request.NewLeaseState = DetermineNewLeaseState(leaseInfo.State, reason);
        request.LeaseFlags = LeaseFlags.BreakInProgress;
        request.LeaseDuration = 0;

        // 设置 SMB2 头部
        request.Header.Command = SMB2CommandName.OplockBreak;
        request.Header.SessionID = m_sessionID;
        request.Header.TreeID = 0;
        request.Header.CreditCharge = 1;
        request.Header.Credits = 1;
        request.Header.Flags = SMB2HeaderFlags.None;
        request.Header.NextCommand = 0;
        request.Header.MessageID = 0; // 由服务器设置
        request.Header.ProcessID = 0;
        request.Header.StructureSize = 64;

        // 发送命令
        var responseChain = new List<SMB2Command> { request };
        var packet = new SessionMessagePacket();
        packet.Trailer = SMB2Command.GetCommandChainBytes(responseChain, m_signingKey, SMB2Dialect.SMB2xx);
        
        m_connection.Send(packet);
        
        LogToServer(Severity.Debug, 
            "Sent lease break notification for lease {0}, reason: {1}", 
            leaseKey, reason);
    }
    catch (Exception ex)
    {
        LogToServer(Severity.Error, 
            "Failed to send lease break notification for lease {0}: {1}", 
            leaseKey, ex.Message);
    }
}
```

## 协议集成

### 在 SMB 服务器中集成

```csharp
// 在 SMBServer.SMB2.cs 中添加租赁中断命令处理
private SMB2Command ProcessSMB2Command(SMB2Command command, SMB2ConnectionState state)
{
    // ... 其他命令处理 ...
    
    else if (command is LeaseBreakAck)
    {
        return ProcessLeaseBreakAck((LeaseBreakAck)command, state);
    }
    else if (command is LeaseBreakResponse)
    {
        ProcessLeaseBreakResponse((LeaseBreakResponse)command, state);
        return null; // 响应命令不需要返回
    }
    
    // ... 其他命令处理 ...
}

private SMB2Command ProcessLeaseBreakAck(LeaseBreakAck ack, SMB2ConnectionState state)
{
    var session = state.GetSession(ack.Header.SessionID);
    if (session == null)
    {
        return new ErrorResponse(SMB2CommandName.OplockBreak, 
            NTStatus.STATUS_USER_SESSION_DELETED);
    }

    try
    {
        session.LeaseBreakHandler.ProcessLeaseBreakAcknowledgment(ack);
        return new LeaseBreakResponse(); // 返回确认响应
    }
    catch (LeaseException ex)
    {
        return new ErrorResponse(SMB2CommandName.OplockBreak, 
            LeaseHelper.ConvertLeaseErrorToNTStatus(ex.ErrorCode));
    }
}
```

## 错误处理

### 常见错误情况

1. **租赁不存在**
   - 错误代码: `LeaseErrorCode.LeaseNotFound`
   - NT状态: `NTStatus.STATUS_OBJECT_NAME_NOT_FOUND`
   - 处理: 忽略请求或返回错误

2. **租赁已过期**
   - 错误代码: `LeaseErrorCode.LeaseExpired`
   - NT状态: `NTStatus.STATUS_OBJECT_NAME_NOT_FOUND`
   - 处理: 清理过期租赁

3. **租赁中断进行中**
   - 错误代码: `LeaseErrorCode.LeaseBreakInProgress`
   - NT状态: `NTStatus.STATUS_OPLOCK_BREAK_IN_PROGRESS`
   - 处理: 等待当前中断完成

### 错误处理示例

```csharp
public static NTStatus HandleLeaseBreakError(LeaseException ex)
{
    return ex.ErrorCode switch
    {
        LeaseErrorCode.LeaseNotFound => NTStatus.STATUS_OBJECT_NAME_NOT_FOUND,
        LeaseErrorCode.LeaseExpired => NTStatus.STATUS_OBJECT_NAME_NOT_FOUND,
        LeaseErrorCode.LeaseInvalid => NTStatus.STATUS_INVALID_PARAMETER,
        LeaseErrorCode.LeaseResourceExhausted => NTStatus.STATUS_INSUFFICIENT_RESOURCES,
        LeaseErrorCode.LeaseAlreadyExists => NTStatus.STATUS_OBJECT_NAME_COLLISION,
        LeaseErrorCode.LeasePermissionDenied => NTStatus.STATUS_ACCESS_DENIED,
        LeaseErrorCode.LeaseBreakInProgress => NTStatus.STATUS_OPLOCK_BREAK_IN_PROGRESS,
        _ => NTStatus.STATUS_UNSUCCESSFUL
    };
}
```

## 性能考虑

### 内存使用

- 每个命令实例占用约 64 字节内存
- 命令序列化/反序列化使用字节数组缓冲区
- 建议重用命令实例以减少内存分配

### 网络效率

- 租赁中断命令是异步的，不会阻塞其他操作
- 使用 SMB2 命令链可以减少网络往返次数
- 适当的超时机制避免资源泄漏

## 最佳实践

1. **命令验证**: 在处理命令前验证所有字段
2. **错误处理**: 始终处理可能的异常情况
3. **日志记录**: 记录重要的租赁中断事件
4. **超时管理**: 实现适当的超时机制
5. **资源清理**: 确保租赁资源得到正确清理

## 相关类型

- `SMB2Command`: SMB2 命令基类
- `LeaseState`: 租赁状态枚举
- `LeaseFlags`: 租赁标志枚举
- `LeaseBreakReason`: 租赁中断原因枚举
- `SMB2CommandName`: SMB2 命令名称枚举
- `NTStatus`: NT 状态代码枚举
