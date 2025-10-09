# Lease Context Handler API

## 概述

`LeaseContextHandler` 是 SMB 2.0/2.1 租赁协议中负责处理租赁上下文的核心组件。它负责处理 Create 请求中的租赁上下文，验证租赁请求，并生成相应的响应上下文。

## 类定义

```csharp
public class LeaseContextHandler
```

## 构造函数

### LeaseContextHandler(LeaseManager leaseManager)

创建新的租赁上下文处理器实例。

**参数：**
- `leaseManager` (LeaseManager): 租赁管理器实例，不能为 null

**异常：**
- `ArgumentNullException`: 当 leaseManager 为 null 时抛出

## 公共方法

### ProcessCreateContext(CreateContext context, ulong sessionId, FileID fileId, string filePath)

处理 Create 请求中的租赁上下文。

**参数：**
- `context` (CreateContext): Create 请求的上下文
- `sessionId` (ulong): 会话 ID
- `fileId` (FileID): 文件 ID
- `filePath` (string): 文件路径

**返回值：**
- `LeaseContext`: 处理后的租赁上下文，如果输入不是租赁上下文则返回 null

**异常：**
- `LeaseException`: 当租赁请求无效时抛出

**示例：**
```csharp
var handler = new LeaseContextHandler(leaseManager);
var leaseContext = handler.ProcessCreateContext(context, sessionId, fileId, filePath);
```

### GenerateResponseContext(LeaseInfo leaseInfo)

生成租赁响应上下文。

**参数：**
- `leaseInfo` (LeaseInfo): 租赁信息

**返回值：**
- `LeaseContext`: 生成的响应上下文

**异常：**
- `ArgumentNullException`: 当 leaseInfo 为 null 时抛出

**示例：**
```csharp
var responseContext = handler.GenerateResponseContext(leaseInfo);
```

## 私有方法

### ValidateLeaseRequest(LeaseContext context)

验证租赁请求的有效性。

**参数：**
- `context` (LeaseContext): 要验证的租赁上下文

**返回值：**
- `bool`: 如果请求有效返回 true，否则返回 false

**验证规则：**
1. 上下文不能为 null
2. 租赁键不能为空 GUID
3. 租赁状态不能为 None
4. 租赁持续时间不能为 0

## 使用示例

### 基本用法

```csharp
// 创建租赁上下文处理器
var leaseManager = new LeaseManager();
var contextHandler = new LeaseContextHandler(leaseManager);

// 处理 Create 请求中的租赁上下文
var leaseContext = contextHandler.ProcessCreateContext(
    createContext, 
    sessionId, 
    fileId, 
    filePath
);

if (leaseContext != null)
{
    // 租赁上下文处理成功
    Console.WriteLine($"Lease created: {leaseContext.LeaseKey}");
}
```

### 错误处理

```csharp
try
{
    var leaseContext = contextHandler.ProcessCreateContext(
        createContext, 
        sessionId, 
        fileId, 
        filePath
    );
}
catch (LeaseException ex)
{
    Console.WriteLine($"Lease error: {ex.Message}, Code: {ex.ErrorCode}");
}
catch (ArgumentNullException ex)
{
    Console.WriteLine($"Invalid argument: {ex.Message}");
}
```

## 集成说明

### 与 SMB2Session 的集成

`LeaseContextHandler` 通常与 `SMB2Session` 一起使用：

```csharp
public class SMB2Session
{
    private LeaseContextHandler m_leaseContextHandler;
    
    public SMB2Session(SMB2ConnectionState connection, ...)
    {
        // 初始化租赁管理器
        var leaseManager = new LeaseManager(config);
        m_leaseContextHandler = new LeaseContextHandler(leaseManager);
    }
    
    public LeaseContext ProcessCreateRequest(CreateRequest request)
    {
        // 处理 Create 请求中的租赁上下文
        return m_leaseContextHandler.ProcessCreateContext(
            request.CreateContexts?.FirstOrDefault(c => c is LeaseContext),
            m_sessionID,
            request.FileId,
            request.Name
        );
    }
}
```

### 与 CreateHelper 的集成

在 SMB 服务器的 Create 命令处理中集成：

```csharp
public static SMB2Command GetCreateResponse(CreateRequest request, ISMBShare share, SMB2ConnectionState state)
{
    var session = state.GetSession(request.Header.SessionID);
    
    // 处理租赁上下文
    var leaseContext = session.LeaseContextHandler.ProcessCreateContext(
        request.CreateContexts?.FirstOrDefault(c => c is LeaseContext),
        request.Header.SessionID,
        request.FileId,
        request.Name
    );
    
    // 创建响应
    var response = new CreateResponse();
    if (leaseContext != null)
    {
        response.CreateContexts = new List<CreateContext> { leaseContext };
    }
    
    return response;
}
```

## 性能考虑

### 内存使用

- `LeaseContextHandler` 本身是轻量级的，主要内存消耗来自 `LeaseManager`
- 建议重用 `LeaseContextHandler` 实例，避免频繁创建和销毁

### 线程安全

- `LeaseContextHandler` 本身是线程安全的
- 底层的 `LeaseManager` 使用并发集合，支持多线程访问

## 最佳实践

1. **实例管理**: 为每个 SMB2Session 创建一个 `LeaseContextHandler` 实例
2. **错误处理**: 始终处理 `LeaseException` 和 `ArgumentNullException`
3. **验证**: 在处理租赁上下文之前验证输入参数
4. **资源清理**: 当会话关闭时，确保相关的租赁资源得到正确清理

## 相关类型

- `LeaseManager`: 租赁管理器
- `LeaseContext`: 租赁上下文
- `LeaseInfo`: 租赁信息
- `LeaseRequest`: 租赁请求
- `LeaseException`: 租赁异常
