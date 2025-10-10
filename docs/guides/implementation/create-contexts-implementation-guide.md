# SMB2 Create 上下文实现指南

## 概述

本指南提供了在 SMBLibrary 中实现 SMB2 Create 上下文（Create Contexts）的详细步骤和最佳实践。

## 为什么需要实现 Create 上下文

### 客户端兼容性问题

不实现 Create 上下文会导致以下问题：

1. **Windows 资源管理器无法正确显示文件属性**
   - 无法显示正确的权限信息
   - 右键菜单的操作可能不正确
   - 文件图标和状态显示可能异常

2. **Office 应用程序可能无法正常工作**
   - Word、Excel 等应用依赖 MxAc 上下文判断是否可以编辑
   - 缺少 QFid 可能导致保存失败

3. **租赁协议无法生效**
   - 即使协商时表明支持租赁，客户端请求租赁时如果不响应 RqLs 上下文，租赁也不会生效
   - 性能优化无法实现

4. **持久句柄无法使用**
   - 网络中断后无法恢复文件句柄
   - 影响网络不稳定环境下的可靠性

### 实际影响示例

| 场景 | 未实现 MxAc | 实现 MxAc |
|------|------------|-----------|
| Windows 资源管理器打开文件 | 文件属性显示"未知权限" | 正确显示读写权限 |
| Word 打开文档 | 可能以只读模式打开 | 根据实际权限打开 |
| 右键菜单 | "编辑"选项可能不可用 | 正确显示可用操作 |
| 文件复制 | 可能提示权限不足 | 正常复制 |

## 实现优先级

### 阶段 1: 核心功能（必须）

实现 **MxAc** 上下文以保证基本的 Windows 客户端兼容性。

**优先级**: ⭐⭐⭐⭐⭐  
**影响**: 不实现会严重影响 Windows 客户端体验  
**工作量**: 低（约 30 分钟）  
**收益**: 极高

### 阶段 2: 租赁支持（如果启用租赁）

实现 **RqLs** 上下文以支持租赁协议。

**优先级**: ⭐⭐⭐⭐⭐（如果启用租赁）  
**影响**: 租赁协议无法工作  
**工作量**: 中（已有 LeaseContextHandler）  
**收益**: 极高（性能提升）

### 阶段 3: 高级功能（推荐）

实现 **QFid** 上下文以支持高级文件操作。

**优先级**: ⭐⭐⭐  
**影响**: 某些高级功能受限  
**工作量**: 低（约 20 分钟）  
**收益**: 中

### 阶段 4: 扩展功能（可选）

实现 **DHnQ**、**AlSi**、**ExtA** 等上下文。

**优先级**: ⭐  
**影响**: 特定场景受限  
**工作量**: 中到高  
**收益**: 低到中

## 实现步骤

### 步骤 1: 实现 MxAc 上下文

#### 1.1 创建 MaximalAccessCalculator

```csharp
// SMBLibrary/Server/SMB2/MaximalAccessCalculator.cs
namespace SMBLibrary.Server.SMB2
{
    /// <summary>
    /// Calculate maximal access for files
    /// </summary>
    internal class MaximalAccessCalculator
    {
        /// <summary>
        /// Calculate maximal access for a file handle
        /// </summary>
        public static AccessMask CalculateMaximalAccess(
            object handle, 
            SecurityContext securityContext,
            ISMBFileStore fileStore)
        {
            AccessMask maximalAccess = 0;
            
            // Try each access right
            if (CanAccess(handle, FileAccessMask.FILE_READ_DATA, securityContext, fileStore))
                maximalAccess |= (AccessMask)FileAccessMask.FILE_READ_DATA;
                
            if (CanAccess(handle, FileAccessMask.FILE_WRITE_DATA, securityContext, fileStore))
                maximalAccess |= (AccessMask)FileAccessMask.FILE_WRITE_DATA;
                
            if (CanAccess(handle, FileAccessMask.FILE_APPEND_DATA, securityContext, fileStore))
                maximalAccess |= (AccessMask)FileAccessMask.FILE_APPEND_DATA;
                
            // ... 测试其他权限
            
            // 添加通用权限
            maximalAccess |= AccessMask.DELETE;
            maximalAccess |= AccessMask.READ_CONTROL;
            maximalAccess |= AccessMask.SYNCHRONIZE;
            
            return maximalAccess;
        }
        
        private static bool CanAccess(
            object handle, 
            FileAccessMask accessMask,
            SecurityContext securityContext,
            ISMBFileStore fileStore)
        {
            // 简化实现：基于已打开的句柄判断
            // 实际应该查询文件系统的ACL
            return true; // 暂时返回 true
        }
    }
}
```

#### 1.2 修改 CreateHelper

```csharp
// SMBLibrary/Server/SMB2/CreateHelper.cs
internal static SMB2Command GetCreateResponse(CreateRequest request, ...)
{
    // ... 现有代码：创建文件等 ...
    
    CreateResponse response = CreateResponseFromFileSystemEntry(...);
    
    // 处理 CreateContexts
    if (request.CreateContexts != null && request.CreateContexts.Count > 0)
    {
        foreach (var requestContext in request.CreateContexts)
        {
            if (requestContext.Name == "MxAc")
            {
                AccessMask maximalAccess = MaximalAccessCalculator.CalculateMaximalAccess(
                    handle, 
                    session.SecurityContext,
                    share.FileStore);
                AddMxAcContext(response, maximalAccess);
            }
        }
    }
    
    return response;
}

private static void AddMxAcContext(CreateResponse response, AccessMask maximalAccess)
{
    byte[] mxAcData = new byte[8];
    LittleEndianWriter.WriteUInt32(mxAcData, 0, 0); // QueryStatus: STATUS_SUCCESS
    LittleEndianWriter.WriteUInt32(mxAcData, 4, (uint)maximalAccess);
    
    var mxAcContext = new CreateContext
    {
        Name = "MxAc",
        Data = mxAcData,
        Next = 0
    };
    response.CreateContexts.Add(mxAcContext);
}
```

### 步骤 2: 实现 QFid 上下文

#### 2.1 修改 CreateHelper（已有实现，需启用）

```csharp
internal static SMB2Command GetCreateResponse(CreateRequest request, ...)
{
    // ... 现有代码 ...
    
    // 处理 CreateContexts
    if (request.CreateContexts != null && request.CreateContexts.Count > 0)
    {
        foreach (var requestContext in request.CreateContexts)
        {
            if (requestContext.Name == "MxAc")
            {
                AccessMask maximalAccess = MaximalAccessCalculator.CalculateMaximalAccess(...);
                AddMxAcContext(response, maximalAccess);
            }
            else if (requestContext.Name == "QFid")
            {
                AddQFidContext(fileID.Value, response);
            }
        }
    }
    
    return response;
}

// AddQFidContext 已经存在于 CreateHelper.cs 中
```

### 步骤 3: 实现 RqLs 上下文（租赁）

#### 3.1 集成 LeaseContextHandler

```csharp
internal static SMB2Command GetCreateResponse(CreateRequest request, ...)
{
    // ... 现有代码 ...
    
    // 处理 CreateContexts
    if (request.CreateContexts != null && request.CreateContexts.Count > 0)
    {
        foreach (var requestContext in request.CreateContexts)
        {
            if (requestContext.Name == "MxAc")
            {
                AddMxAcContext(response, ...);
            }
            else if (requestContext.Name == "QFid")
            {
                AddQFidContext(fileID.Value, response);
            }
            else if (requestContext.Name == "RqLs")
            {
                // 检查会话是否支持租赁
                if (session.SupportsLeasing) // 需要添加此属性
                {
                    try
                    {
                        var leaseContext = session.LeaseContextHandler.ProcessCreateContext(
                            requestContext,
                            request.Header.SessionID,
                            fileID.Value,
                            path);
                            
                        if (leaseContext != null)
                        {
                            response.CreateContexts.Add(leaseContext);
                            response.OplockLevel = OplockLevel.Lease;
                        }
                    }
                    catch (LeaseException ex)
                    {
                        // 租赁请求失败，记录日志但不影响文件打开
                        state.LogToServer(Severity.Warning, 
                            "Lease request failed: {0}", ex.Message);
                    }
                }
            }
        }
    }
    
    return response;
}
```

#### 3.2 在 SMB2Session 中添加 SupportsLeasing 属性

```csharp
// SMBLibrary/Server/ConnectionState/SMB2Session.cs
public class SMB2Session
{
    private LeaseManager m_leaseManager;
    private LeaseContextHandler m_leaseContextHandler;
    
    /// <summary>
    /// Indicates whether this session supports leasing
    /// </summary>
    public bool SupportsLeasing => m_leaseManager != null;
    
    /// <summary>
    /// Gets the lease context handler (null if leasing is disabled)
    /// </summary>
    public LeaseContextHandler LeaseContextHandler => m_leaseContextHandler;
    
    // ... 现有代码 ...
}
```

## 测试和验证

### 测试 MxAc 上下文

```csharp
[Test]
public void TestMxAcContext()
{
    // 1. 创建测试文件
    // 2. 发送包含 MxAc 上下文的 Create 请求
    // 3. 验证响应中包含 MxAc 上下文
    // 4. 验证 MaximalAccess 值正确
}
```

### 使用 Wireshark 验证

1. 启动服务器
2. 使用 Windows 客户端连接
3. 在 Wireshark 中过滤 SMB2 流量：`smb2.cmd == 5`（Create 命令）
4. 检查 Create Response 中的 CreateContexts 字段
5. 确认包含请求的上下文（MxAc、QFid、RqLs）

### 验证清单

- [ ] Create 响应包含客户端请求的 MxAc 上下文
- [ ] MxAc.QueryStatus = 0x00000000
- [ ] MxAc.MaximalAccess 包含正确的权限位
- [ ] Create 响应包含客户端请求的 QFid 上下文
- [ ] QFid.FileId 为 32 字节
- [ ] 如果启用租赁，Create 响应包含 RqLs 上下文
- [ ] RqLs 响应包含正确的租赁信息
- [ ] OplockLevel 设置为 Lease

## 常见问题和解决方案

### 问题 1: CreateContexts 为空或 null

**现象**: 客户端请求了上下文，但服务器响应中没有

**原因**: 
```csharp
// 错误：忘记初始化 CreateContexts
CreateResponse response = new CreateResponse();
// response.CreateContexts 为 null 或空
```

**解决**:
```csharp
// 正确：确保 CreateContexts 被初始化
CreateResponse response = new CreateResponse();
response.CreateContexts = new List<CreateContext>();

// 或在 CreateResponse 构造函数中初始化
```

### 问题 2: 上下文数据格式错误

**现象**: 客户端收到响应后断开连接或报错

**原因**: 
```csharp
// 错误：字节序错误或数据长度错误
byte[] data = new byte[8];
Array.Copy(BitConverter.GetBytes(value), 0, data, 0, 4); // 可能是大端序
```

**解决**:
```csharp
// 正确：使用 LittleEndianWriter
byte[] data = new byte[8];
LittleEndianWriter.WriteUInt32(data, 0, queryStatus);
LittleEndianWriter.WriteUInt32(data, 4, (uint)maximalAccess);
```

### 问题 3: 上下文对齐错误

**现象**: 客户端解析失败，可能断开连接

**原因**: CreateContext 链表未正确 8 字节对齐

**解决**: 使用 `CreateContext.WriteCreateContextList` 方法，它会自动处理对齐

## 性能考虑

### MxAc 性能影响

```
每次 Create 请求增加的开销：
- 权限计算: ~0.1-1ms（取决于文件系统）
- 响应数据: +16字节（8字节数据 + 8字节头部）
- 内存: 临时分配 ~50 字节

总体影响: 可忽略不计
```

### QFid 性能影响

```
每次 Create 请求增加的开销：
- FileID 转换: ~0.01ms
- 响应数据: +48字节（32字节数据 + 16字节头部）
- 内存: 临时分配 ~80 字节

总体影响: 可忽略不计
```

### RqLs 性能影响

```
每次 Create 请求增加的开销：
- 租赁创建: ~0.5-2ms（包括索引更新）
- 响应数据: +48字节（32字节数据 + 16字节头部）
- 内存: 持久分配 ~200 字节/租赁

总体影响: 
- 初始开销: 小幅增加
- 长期收益: 显著减少后续请求（50-90% 的网络流量减少）
```

## 完整实现示例

### CreateHelper 完整实现

```csharp
internal class CreateHelper
{
    internal static SMB2Command GetCreateResponse(
        CreateRequest request, 
        ISMBShare share, 
        SMB2ConnectionState state)
    {
        SMB2Session session = state.GetSession(request.Header.SessionID);
        string path = request.Name;
        
        // ... 权限检查、文件创建等现有代码 ...
        
        // 创建基本响应
        FileNetworkOpenInformation fileInfo = 
            NTFileStoreHelper.GetNetworkOpenInformation(share.FileStore, handle);
        CreateResponse response = CreateResponseFromFileSystemEntry(
            fileInfo, fileID.Value, fileStatus);
        
        // 处理 Create 上下文
        if (request.CreateContexts != null && request.CreateContexts.Count > 0)
        {
            ProcessCreateContexts(request, response, session, share, 
                handle, fileID.Value, path, state);
        }
        
        return response;
    }
    
    private static void ProcessCreateContexts(
        CreateRequest request,
        CreateResponse response,
        SMB2Session session,
        ISMBShare share,
        object handle,
        FileID fileID,
        string path,
        SMB2ConnectionState state)
    {
        foreach (var requestContext in request.CreateContexts)
        {
            switch (requestContext.Name)
            {
                case "MxAc":
                    ProcessMxAcContext(response, share, handle, session, state);
                    break;
                    
                case "QFid":
                    ProcessQFidContext(response, fileID, state);
                    break;
                    
                case "RqLs":
                    ProcessLeaseContext(requestContext, response, session, 
                        fileID, path, state);
                    break;
                    
                case "DHnQ":
                    // 持久句柄请求（可选实现）
                    state.LogToServer(Severity.Debug, 
                        "Durable handle request received but not supported");
                    break;
                    
                default:
                    state.LogToServer(Severity.Debug, 
                        "Unknown create context: {0}", requestContext.Name);
                    break;
            }
        }
    }
    
    private static void ProcessMxAcContext(
        CreateResponse response,
        ISMBShare share,
        object handle,
        SMB2Session session,
        SMB2ConnectionState state)
    {
        try
        {
            AccessMask maximalAccess = CalculateMaximalAccess(
                share, handle, session.SecurityContext);
                
            byte[] mxAcData = new byte[8];
            LittleEndianWriter.WriteUInt32(mxAcData, 0, 0); // STATUS_SUCCESS
            LittleEndianWriter.WriteUInt32(mxAcData, 4, (uint)maximalAccess);
            
            response.CreateContexts.Add(new CreateContext
            {
                Name = "MxAc",
                Data = mxAcData
            });
            
            state.LogToServer(Severity.Debug, 
                "MxAc context added, MaximalAccess: 0x{0:X8}", (uint)maximalAccess);
        }
        catch (Exception ex)
        {
            state.LogToServer(Severity.Warning, 
                "Failed to process MxAc context: {0}", ex.Message);
        }
    }
    
    private static void ProcessQFidContext(
        CreateResponse response,
        FileID fileID,
        SMB2ConnectionState state)
    {
        try
        {
            byte[] opaqueFileId = ConvertUlongTo32ByteOpaqueFileId(fileID.Persistent);
            
            response.CreateContexts.Add(new CreateContext
            {
                Name = "QFid",
                Data = opaqueFileId
            });
            
            state.LogToServer(Severity.Debug, "QFid context added");
        }
        catch (Exception ex)
        {
            state.LogToServer(Severity.Warning, 
                "Failed to process QFid context: {0}", ex.Message);
        }
    }
    
    private static void ProcessLeaseContext(
        CreateContext requestContext,
        CreateResponse response,
        SMB2Session session,
        FileID fileID,
        string path,
        SMB2ConnectionState state)
    {
        // 检查会话是否支持租赁
        if (!session.SupportsLeasing)
        {
            state.LogToServer(Severity.Debug, 
                "Lease requested but leasing is not enabled");
            return;
        }
        
        try
        {
            var leaseContext = session.LeaseContextHandler.ProcessCreateContext(
                requestContext,
                session.SessionID,
                fileID,
                path);
                
            if (leaseContext != null)
            {
                response.CreateContexts.Add(leaseContext);
                response.OplockLevel = OplockLevel.Lease;
                
                state.LogToServer(Severity.Information, 
                    "Lease granted for file: {0}, LeaseKey: {1}", 
                    path, ((LeaseContext)leaseContext).LeaseKey);
            }
        }
        catch (LeaseException ex)
        {
            state.LogToServer(Severity.Warning, 
                "Lease request failed: {0}, ErrorCode: {1}", 
                ex.Message, ex.ErrorCode);
            // 租赁失败不影响文件打开，继续处理
        }
        catch (Exception ex)
        {
            state.LogToServer(Severity.Error, 
                "Unexpected error processing lease context: {0}", ex.Message);
        }
    }
    
    private static AccessMask CalculateMaximalAccess(
        ISMBShare share,
        object handle,
        SecurityContext securityContext)
    {
        // 简化实现：返回所有基本权限
        AccessMask maximalAccess = 
            (AccessMask)FileAccessMask.FILE_READ_DATA |
            (AccessMask)FileAccessMask.FILE_WRITE_DATA |
            (AccessMask)FileAccessMask.FILE_APPEND_DATA |
            (AccessMask)FileAccessMask.FILE_READ_EA |
            (AccessMask)FileAccessMask.FILE_WRITE_EA |
            (AccessMask)FileAccessMask.FILE_EXECUTE |
            (AccessMask)FileAccessMask.FILE_READ_ATTRIBUTES |
            (AccessMask)FileAccessMask.FILE_WRITE_ATTRIBUTES |
            AccessMask.DELETE |
            AccessMask.READ_CONTROL |
            AccessMask.WRITE_DAC |
            AccessMask.WRITE_OWNER |
            AccessMask.SYNCHRONIZE;
            
        // 实际应该根据文件系统的 ACL 计算
        // 如果是 FileSystemShare，可以检查访问权限
        if (share is FileSystemShare fileShare)
        {
            // 检查各项权限
            if (!fileShare.HasAccess(securityContext, handle.ToString(), FileAccess.Write))
            {
                // 移除写权限
                maximalAccess &= ~(AccessMask)FileAccessMask.FILE_WRITE_DATA;
                maximalAccess &= ~(AccessMask)FileAccessMask.FILE_APPEND_DATA;
                maximalAccess &= ~(AccessMask)FileAccessMask.FILE_WRITE_EA;
                maximalAccess &= ~(AccessMask)FileAccessMask.FILE_WRITE_ATTRIBUTES;
                maximalAccess &= ~AccessMask.WRITE_DAC;
                maximalAccess &= ~AccessMask.DELETE;
            }
        }
        
        return maximalAccess;
    }
}
```

## 日志和监控

### 推荐的日志记录

```csharp
// 请求到达
state.LogToServer(Severity.Debug, 
    "Create request for '{0}', Contexts: {1}", 
    path, 
    string.Join(", ", request.CreateContexts.Select(c => c.Name)));

// 上下文处理
state.LogToServer(Severity.Debug, "Processing MxAc context");
state.LogToServer(Severity.Debug, "Processing QFid context");
state.LogToServer(Severity.Information, "Processing RqLs context");

// 响应发送
state.LogToServer(Severity.Debug, 
    "Create response for '{0}', Contexts: {1}", 
    path, 
    string.Join(", ", response.CreateContexts.Select(c => c.Name)));
```

### 监控指标

```csharp
// 统计各上下文的使用频率
private static readonly ConcurrentDictionary<string, long> s_contextStats = 
    new ConcurrentDictionary<string, long>();

private static void RecordContextUsage(string contextName)
{
    s_contextStats.AddOrUpdate(contextName, 1, (key, count) => count + 1);
}

// 定期输出统计
public static void LogContextStatistics()
{
    foreach (var kvp in s_contextStats)
    {
        Console.WriteLine($"Context {kvp.Key}: {kvp.Value} requests");
    }
}
```

## 最佳实践

### 1. 优雅降级

如果某个上下文处理失败，不应该影响文件打开操作：

```csharp
try
{
    ProcessLeaseContext(...);
}
catch (Exception ex)
{
    // 记录错误但继续
    state.LogToServer(Severity.Warning, "Lease failed: {0}", ex.Message);
    // 文件仍然可以打开，只是没有租赁
}
```

### 2. 条件响应

只响应客户端请求的上下文：

```csharp
// 不要盲目添加所有上下文
// 只添加客户端请求的上下文

var requestedContextNames = new HashSet<string>(
    request.CreateContexts.Select(c => c.Name));

if (requestedContextNames.Contains("MxAc"))
{
    AddMxAcContext(response, ...);
}
```

### 3. 上下文顺序

保持与请求相同的上下文顺序：

```csharp
// 按照客户端请求的顺序处理
foreach (var requestContext in request.CreateContexts)
{
    ProcessContext(requestContext, ...);
}
```

### 4. 错误处理

区分致命错误和非致命错误：

```csharp
// 非致命：上下文处理失败，继续
try { ProcessMxAc(...); } catch { /* 记录但继续 */ }

// 致命：文件创建失败，返回错误
NTStatus status = fileStore.CreateFile(...);
if (status != NTStatus.STATUS_SUCCESS)
{
    return new ErrorResponse(request.CommandName, status);
}
```

## 迁移路径

### 当前状态（CreateHelper.cs）

```csharp
// 现有代码中上下文处理被注释掉
//if (extraInfosKeys.Any(k => k == "MxAc"))
//{
//    AddMxAcContext(response);
//}
```

### 迁移步骤

**第一步：启用 MxAc**
1. 取消注释 MxAc 相关代码
2. 修改 `AddMxAcContext` 以接受实际的 `maximalAccess` 参数
3. 实现权限计算逻辑
4. 测试 Windows 客户端

**第二步：启用 QFid**
1. 取消注释 QFid 相关代码
2. 验证 `ConvertUlongTo32ByteOpaqueFileId` 实现正确
3. 测试高级文件操作

**第三步：集成租赁（RqLs）**
1. 取消注释租赁相关代码
2. 替换临时实现为 `LeaseContextHandler`
3. 添加 `session.SupportsLeasing` 检查
4. 测试租赁功能

## 相关文档

- [SMB2 Create 上下文详解](../../protocols/smb2/smb2-create-contexts.md)
- [租赁上下文处理器 API](../../api/server/leasing/lease-context-handler-api.md)
- [租赁使用指南](../getting-started/lease-usage-guide.md)
- [MS-SMB2] 2.2.13 SMB2 CREATE Request
- [MS-SMB2] 2.2.14 SMB2 CREATE Response
- [MS-SMB2] 2.2.13.2 SMB2_CREATE_CONTEXT

## 总结

Create 上下文的实现对于 SMB2 服务器的兼容性和性能至关重要：

### 必须实现
- ✅ **MxAc**: 保证 Windows 客户端基本兼容性
- ✅ **RqLs**: 如果启用租赁，必须实现

### 推荐实现
- ⚡ **QFid**: 提高高级功能兼容性

### 实现收益
- 🚀 **用户体验**: Windows 资源管理器正确显示文件属性
- 🚀 **性能**: 租赁协议显著减少网络流量
- 🚀 **兼容性**: 支持更多客户端和应用程序
- 🚀 **功能**: 解锁高级文件操作功能

**推荐**: 至少实现 MxAc 上下文，如果启用租赁则必须实现 RqLs 上下文。

