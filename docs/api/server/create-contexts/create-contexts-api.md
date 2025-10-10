# Create 上下文 API 文档

## 概述

本文档详细描述 SMB2 Create 上下文的 API 接口和数据格式。

## 命名空间

```csharp
namespace SMBLibrary.SMB2
```

## CreateContext 基类

### 类定义

```csharp
public class CreateContext
{
    public uint Next;           // 到下一个上下文的偏移（8字节对齐）
    public ushort Reserved;     // 保留字段
    public string Name;         // 上下文名称
    public byte[] Data;         // 上下文数据
    
    public int Length { get; }  // 计算的长度
}
```

### 构造函数

#### CreateContext()
创建空的上下文。

#### CreateContext(byte[] buffer, int offset)
从字节数组解析上下文。

### 静态方法

#### ReadCreateContextList(byte[] buffer, int offset)
从字节数组读取上下文链表。

**返回值**: `List<CreateContext>`

#### WriteCreateContextList(byte[] buffer, int offset, List<CreateContext> createContexts)
将上下文链表写入字节数组。

#### GetCreateContextListLength(List<CreateContext> createContexts)
计算上下文链表的总长度。

**返回值**: `int`

## MxAc 上下文

### 上下文名称
`"MxAc"` (Maximum Access)

### 数据格式

```
字节偏移  | 长度 | 字段名          | 类型   | 说明
----------|------|----------------|--------|------
0         | 4    | QueryStatus    | uint   | 查询状态（NTStatus）
4         | 4    | MaximalAccess  | uint   | 最大访问掩码（AccessMask）
```

### 数据长度
固定 8 字节

### 请求

客户端发送空数据（DataLength = 0）：

```csharp
var mxAcRequest = new CreateContext
{
    Name = "MxAc",
    Data = new byte[0]  // 空数据
};
```

### 响应

服务器返回 8 字节数据：

```csharp
byte[] mxAcData = new byte[8];

// QueryStatus: 0x00000000 (STATUS_SUCCESS)
LittleEndianWriter.WriteUInt32(mxAcData, 0, 0);

// MaximalAccess: 例如 0x001F01FF
LittleEndianWriter.WriteUInt32(mxAcData, 4, (uint)maximalAccess);

var mxAcResponse = new CreateContext
{
    Name = "MxAc",
    Data = mxAcData
};
```

### AccessMask 位标志

常见的 MaximalAccess 值：

| 访问权限 | 值 | 说明 |
|---------|-------|------|
| FILE_READ_DATA | 0x00000001 | 读取文件数据 |
| FILE_WRITE_DATA | 0x00000002 | 写入文件数据 |
| FILE_APPEND_DATA | 0x00000004 | 追加文件数据 |
| FILE_EXECUTE | 0x00000020 | 执行文件 |
| DELETE | 0x00010000 | 删除文件 |
| READ_CONTROL | 0x00020000 | 读取安全描述符 |
| WRITE_DAC | 0x00040000 | 修改安全描述符 |
| WRITE_OWNER | 0x00080000 | 修改所有者 |
| SYNCHRONIZE | 0x00100000 | 同步访问 |

组合示例：
- **完全控制**: `0x001F01FF`
- **读取和执行**: `0x001200A9`
- **只读**: `0x00120089`

### 必要性

- ⭐⭐⭐⭐⭐ **极高**
- Windows 客户端强依赖此上下文
- 影响文件属性对话框、右键菜单、操作权限判断
- **不实现会导致**: Windows 资源管理器无法正确显示权限，用户体验严重下降

## QFid 上下文

### 上下文名称
`"QFid"` (Query on-disk File ID)

### 数据格式

```
字节偏移  | 长度 | 字段名          | 类型      | 说明
----------|------|----------------|----------|------
0         | 32   | FileId         | byte[32] | 不透明的文件ID
```

### 数据长度
固定 32 字节

### 请求

客户端发送空数据（DataLength = 0）：

```csharp
var qfidRequest = new CreateContext
{
    Name = "QFid",
    Data = new byte[0]  // 空数据
};
```

### 响应

服务器返回 32 字节文件 ID：

```csharp
byte[] opaqueFileId = new byte[32];

// 前 8 字节：持久文件 ID（Persistent FileID）
byte[] persistentBytes = LittleEndianConverter.GetBytes(fileID.Persistent);
Array.Copy(persistentBytes, 0, opaqueFileId, 0, 8);

// 后 24 字节：保留（填充 0）
// 已经初始化为 0，无需额外操作

var qfidResponse = new CreateContext
{
    Name = "QFid",
    Data = opaqueFileId
};
```

### FileID 结构

```csharp
struct FileID
{
    ulong Persistent;  // 持久部分，用于 QFid
    ulong Volatile;    // 易失部分，用于会话内引用
}
```

### 必要性

- ⭐⭐⭐ **中到高**
- 用于文件变更通知（Change Notify）
- 用于持久句柄机制
- 某些应用程序依赖此上下文
- **不实现可能导致**: 某些高级功能无法使用，如实时文件监控

## RqLs 上下文（租赁）

### 上下文名称
`"RqLs"` (Request Lease)

### 数据格式（V1 - SMB 2.1）

```
字节偏移  | 长度 | 字段名          | 类型   | 说明
----------|------|----------------|--------|------
0         | 16   | LeaseKey       | Guid   | 租赁键
16        | 4    | LeaseState     | uint   | 请求的租赁状态
20        | 4    | LeaseFlags     | uint   | 租赁标志
24        | 8    | LeaseDuration  | ulong  | 租赁持续时间（毫秒）
```

### 数据格式（V2 - SMB 3.0+）

```
字节偏移  | 长度 | 字段名          | 类型   | 说明
----------|------|----------------|--------|------
0         | 16   | LeaseKey       | Guid   | 租赁键
16        | 4    | LeaseState     | uint   | 请求的租赁状态
20        | 4    | LeaseFlags     | uint   | 租赁标志
24        | 8    | LeaseDuration  | ulong  | 租赁持续时间（毫秒）
32        | 16   | ParentLeaseKey | Guid   | 父租赁键
48        | 2    | Epoch          | ushort | 租赁纪元
50        | 2    | Reserved       | ushort | 保留
```

### 请求

客户端发送租赁请求：

```csharp
var leaseRequest = new LeaseContext
{
    LeaseKey = Guid.NewGuid(),
    LeaseState = LeaseState.ReadCaching | LeaseState.WriteCaching | LeaseState.HandleCaching,
    LeaseFlags = LeaseFlags.None,
    LeaseDuration = 0  // 使用服务器默认值
};
```

### 响应

服务器返回授予的租赁：

```csharp
var leaseResponse = new LeaseContext
{
    LeaseKey = request.LeaseKey,           // 相同的租赁键
    LeaseState = LeaseState.ReadCaching,   // 实际授予的状态（可能少于请求）
    LeaseFlags = LeaseFlags.None,
    LeaseDuration = 3000  // 3秒（毫秒）
};
```

### LeaseState 位标志

| 标志 | 值 | 说明 |
|------|------|------|
| None | 0x00000000 | 无租赁 |
| ReadCaching | 0x00000001 | 读缓存 |
| HandleCaching | 0x00000002 | 句柄缓存 |
| WriteCaching | 0x00000004 | 写缓存 |

### 必要性

- ⭐⭐⭐⭐⭐ **极高**（如果启用租赁）
- 租赁协议的核心组件
- 直接影响性能优化效果
- **不实现会导致**: 即使协商时表明支持租赁，客户端也无法获得租赁

## 其他上下文

### DHnQ (Durable Handle Request)

**名称**: `"DHnQ"`  
**数据长度**: 16 字节  
**必要性**: ⭐⭐⭐ 中

```csharp
struct DHnQ_Request
{
    byte[16] DurableRequest;  // 保留，填充 0
}

struct DHnQ_Response
{
    byte[8] Reserved;  // 保留
}
```

### DHnC (Durable Handle Reconnect)

**名称**: `"DHnC"`  
**必要性**: ⭐⭐⭐ 中（配合 DHnQ）

用于重新连接持久句柄。

### DH2Q (Durable Handle v2 Request)

**名称**: `"DH2Q"`  
**数据长度**: 32 字节  
**必要性**: ⭐⭐ 低

SMB 3.0+ 的增强持久句柄。

### AlSi (Allocation Size)

**名称**: `"AlSi"`  
**数据长度**: 8 字节  
**必要性**: ⭐ 低

```csharp
struct AlSi_Request
{
    ulong AllocationSize;  // 请求的分配大小
}
```

### ExtA (Extended Attributes)

**名称**: `"ExtA"`  
**数据长度**: 可变  
**必要性**: ⭐ 低

用于 NTFS 扩展属性和备用数据流。

## 工具函数

### CreateContext 辅助方法

```csharp
public static class CreateContextHelper
{
    /// <summary>
    /// Check if a specific context is requested
    /// </summary>
    public static bool IsContextRequested(
        List<CreateContext> contexts, 
        string contextName)
    {
        return contexts != null && 
               contexts.Any(c => c.Name == contextName);
    }
    
    /// <summary>
    /// Get requested context by name
    /// </summary>
    public static CreateContext GetRequestedContext(
        List<CreateContext> contexts,
        string contextName)
    {
        return contexts?.FirstOrDefault(c => c.Name == contextName);
    }
    
    /// <summary>
    /// Create MxAc response context
    /// </summary>
    public static CreateContext CreateMxAcContext(AccessMask maximalAccess)
    {
        byte[] data = new byte[8];
        LittleEndianWriter.WriteUInt32(data, 0, 0); // STATUS_SUCCESS
        LittleEndianWriter.WriteUInt32(data, 4, (uint)maximalAccess);
        
        return new CreateContext
        {
            Name = "MxAc",
            Data = data
        };
    }
    
    /// <summary>
    /// Create QFid response context
    /// </summary>
    public static CreateContext CreateQFidContext(FileID fileID)
    {
        byte[] data = new byte[32];
        byte[] persistentBytes = LittleEndianConverter.GetBytes(fileID.Persistent);
        Array.Copy(persistentBytes, 0, data, 0, 8);
        
        return new CreateContext
        {
            Name = "QFid",
            Data = data
        };
    }
}
```

## 使用示例

### 示例 1: 处理所有常见上下文

```csharp
public static SMB2Command GetCreateResponse(
    CreateRequest request,
    ISMBShare share,
    SMB2ConnectionState state)
{
    // ... 文件创建代码 ...
    
    CreateResponse response = new CreateResponse();
    response.FileId = fileID;
    
    // 处理上下文
    if (request.CreateContexts != null)
    {
        // 1. MxAc - 最高优先级
        if (CreateContextHelper.IsContextRequested(request.CreateContexts, "MxAc"))
        {
            var maximalAccess = CalculateMaximalAccess(share, handle, session.SecurityContext);
            response.CreateContexts.Add(CreateContextHelper.CreateMxAcContext(maximalAccess));
        }
        
        // 2. QFid - 中优先级
        if (CreateContextHelper.IsContextRequested(request.CreateContexts, "QFid"))
        {
            response.CreateContexts.Add(CreateContextHelper.CreateQFidContext(fileID));
        }
        
        // 3. RqLs - 如果启用租赁
        var leaseRequest = CreateContextHelper.GetRequestedContext(request.CreateContexts, "RqLs");
        if (leaseRequest != null && session.SupportsLeasing)
        {
            try
            {
                var leaseContext = session.LeaseContextHandler.ProcessCreateContext(
                    leaseRequest, session.SessionID, fileID, path);
                    
                if (leaseContext != null)
                {
                    response.CreateContexts.Add(leaseContext);
                    response.OplockLevel = OplockLevel.Lease;
                }
            }
            catch (LeaseException ex)
            {
                state.LogToServer(Severity.Warning, "Lease failed: {0}", ex.Message);
            }
        }
    }
    
    return response;
}
```

### 示例 2: 只实现 MxAc（最小实现）

```csharp
// 最小实现：只处理 MxAc
if (request.CreateContexts != null)
{
    foreach (var context in request.CreateContexts)
    {
        if (context.Name == "MxAc")
        {
            // 简化：返回完全控制权限
            AccessMask maximalAccess = (AccessMask)0x001F01FF;
            
            byte[] data = new byte[8];
            LittleEndianWriter.WriteUInt32(data, 0, 0);
            LittleEndianWriter.WriteUInt32(data, 4, (uint)maximalAccess);
            
            response.CreateContexts.Add(new CreateContext
            {
                Name = "MxAc",
                Data = data
            });
            
            break; // 只处理第一个 MxAc
        }
    }
}
```

### 示例 3: 带详细日志

```csharp
if (request.CreateContexts != null && request.CreateContexts.Count > 0)
{
    state.LogToServer(Severity.Debug, 
        "Processing {0} create contexts for '{1}'", 
        request.CreateContexts.Count, path);
    
    foreach (var context in request.CreateContexts)
    {
        state.LogToServer(Severity.Trace, 
            "  Context: {0}, DataLength: {1}", 
            context.Name, context.Data.Length);
        
        switch (context.Name)
        {
            case "MxAc":
                var access = CalculateMaximalAccess(...);
                response.CreateContexts.Add(CreateMxAcContext(access));
                state.LogToServer(Severity.Debug, 
                    "  MxAc added: 0x{0:X8}", (uint)access);
                break;
                
            case "QFid":
                response.CreateContexts.Add(CreateQFidContext(fileID));
                state.LogToServer(Severity.Debug, "  QFid added");
                break;
                
            case "RqLs":
                if (session.SupportsLeasing)
                {
                    var lease = ProcessLeaseContext(...);
                    response.CreateContexts.Add(lease);
                    state.LogToServer(Severity.Information, 
                        "  Lease granted: {0}", ((LeaseContext)lease).LeaseKey);
                }
                else
                {
                    state.LogToServer(Severity.Debug, 
                        "  Lease requested but not supported");
                }
                break;
                
            default:
                state.LogToServer(Severity.Debug, 
                    "  Unknown context: {0} (ignored)", context.Name);
                break;
        }
    }
    
    state.LogToServer(Severity.Debug, 
        "Response contains {0} contexts", 
        response.CreateContexts.Count);
}
```

## 性能优化建议

### 1. 缓存常用值

```csharp
private static class MxAcCache
{
    public static readonly AccessMask FullControl = (AccessMask)0x001F01FF;
    public static readonly AccessMask ReadOnly = (AccessMask)0x00120089;
    public static readonly AccessMask ReadWrite = (AccessMask)0x001301BF;
    
    public static readonly byte[] FullControlData;
    public static readonly byte[] ReadOnlyData;
    public static readonly byte[] ReadWriteData;
    
    static MxAcCache()
    {
        FullControlData = CreateMxAcData(FullControl);
        ReadOnlyData = CreateMxAcData(ReadOnly);
        ReadWriteData = CreateMxAcData(ReadWrite);
    }
    
    private static byte[] CreateMxAcData(AccessMask access)
    {
        byte[] data = new byte[8];
        LittleEndianWriter.WriteUInt32(data, 0, 0);
        LittleEndianWriter.WriteUInt32(data, 4, (uint)access);
        return data;
    }
}

// 使用缓存
var data = readOnly ? MxAcCache.ReadOnlyData : MxAcCache.FullControlData;
response.CreateContexts.Add(new CreateContext { Name = "MxAc", Data = data });
```

### 2. 延迟处理

```csharp
// 只在客户端请求时才计算
if (request.CreateContexts != null && request.CreateContexts.Count > 0)
{
    // 创建快速查找字典
    var requestedContexts = new HashSet<string>(
        request.CreateContexts.Select(c => c.Name));
    
    // 批量处理
    if (requestedContexts.Contains("MxAc"))
    {
        // 只在请求时才计算权限
        var access = CalculateMaximalAccess(...);
        AddMxAcContext(response, access);
    }
}
```

### 3. 避免重复分配

```csharp
// 重用缓冲区
private static readonly ThreadLocal<byte[]> s_contextBuffer = 
    new ThreadLocal<byte[]>(() => new byte[32]);

public static CreateContext CreateQFidContext(FileID fileID)
{
    byte[] buffer = s_contextBuffer.Value;
    Array.Clear(buffer, 0, 32);
    
    byte[] persistentBytes = LittleEndianConverter.GetBytes(fileID.Persistent);
    Array.Copy(persistentBytes, 0, buffer, 0, 8);
    
    // 创建新数组用于响应（不能共享）
    byte[] data = new byte[32];
    Array.Copy(buffer, data, 32);
    
    return new CreateContext { Name = "QFid", Data = data };
}
```

## 错误处理

### 上下文处理异常

```csharp
public static void ProcessCreateContexts(
    CreateRequest request,
    CreateResponse response,
    SMB2Session session,
    ISMBShare share,
    object handle,
    FileID fileID,
    string path,
    SMB2ConnectionState state)
{
    if (request.CreateContexts == null || request.CreateContexts.Count == 0)
        return;
    
    foreach (var context in request.CreateContexts)
    {
        try
        {
            switch (context.Name)
            {
                case "MxAc":
                    ProcessMxAcContext(response, share, handle, session);
                    break;
                    
                case "QFid":
                    ProcessQFidContext(response, fileID);
                    break;
                    
                case "RqLs":
                    if (session.SupportsLeasing)
                    {
                        ProcessLeaseContext(context, response, session, 
                            fileID, path, state);
                    }
                    break;
                    
                default:
                    // 未知上下文，忽略
                    state.LogToServer(Severity.Trace, 
                        "Unknown context '{0}' ignored", context.Name);
                    break;
            }
        }
        catch (Exception ex)
        {
            // 记录错误但继续处理其他上下文
            state.LogToServer(Severity.Warning, 
                "Error processing context '{0}': {1}", 
                context.Name, ex.Message);
        }
    }
}
```

## 测试用例

### 测试 MxAc 上下文

```csharp
[Test]
public void TestMxAcContext_FullControl()
{
    // 创建请求
    var request = new CreateRequest
    {
        Name = "test.txt",
        DesiredAccess = AccessMask.GENERIC_ALL,
        CreateContexts = new List<CreateContext>
        {
            new CreateContext { Name = "MxAc", Data = new byte[0] }
        }
    };
    
    // 处理请求
    var response = CreateHelper.GetCreateResponse(request, share, state);
    
    // 验证
    Assert.IsNotNull(response.CreateContexts);
    Assert.AreEqual(1, response.CreateContexts.Count);
    Assert.AreEqual("MxAc", response.CreateContexts[0].Name);
    Assert.AreEqual(8, response.CreateContexts[0].Data.Length);
    
    // 验证数据
    uint queryStatus = LittleEndianConverter.ToUInt32(
        response.CreateContexts[0].Data, 0);
    uint maximalAccess = LittleEndianConverter.ToUInt32(
        response.CreateContexts[0].Data, 4);
        
    Assert.AreEqual(0u, queryStatus); // STATUS_SUCCESS
    Assert.AreNotEqual(0u, maximalAccess); // 应该有权限
}
```

### 测试 QFid 上下文

```csharp
[Test]
public void TestQFidContext()
{
    var request = new CreateRequest
    {
        Name = "test.txt",
        CreateContexts = new List<CreateContext>
        {
            new CreateContext { Name = "QFid", Data = new byte[0] }
        }
    };
    
    var response = CreateHelper.GetCreateResponse(request, share, state);
    
    Assert.IsTrue(response.CreateContexts.Any(c => c.Name == "QFid"));
    var qfidContext = response.CreateContexts.First(c => c.Name == "QFid");
    Assert.AreEqual(32, qfidContext.Data.Length);
}
```

### 测试多个上下文

```csharp
[Test]
public void TestMultipleContexts()
{
    var request = new CreateRequest
    {
        Name = "test.txt",
        CreateContexts = new List<CreateContext>
        {
            new CreateContext { Name = "MxAc", Data = new byte[0] },
            new CreateContext { Name = "QFid", Data = new byte[0] },
            new CreateContext { Name = "RqLs", Data = CreateLeaseRequestData() }
        }
    };
    
    var response = CreateHelper.GetCreateResponse(request, share, state);
    
    // 验证所有上下文都被响应
    Assert.AreEqual(3, response.CreateContexts.Count);
    Assert.IsTrue(response.CreateContexts.Any(c => c.Name == "MxAc"));
    Assert.IsTrue(response.CreateContexts.Any(c => c.Name == "QFid"));
    Assert.IsTrue(response.CreateContexts.Any(c => c.Name == "RqLs"));
}
```

## 调试技巧

### 1. 日志级别设置

```csharp
// 开发期间使用 Debug 或 Trace 级别
state.LogToServer(Severity.Trace, 
    "Context '{0}': DataLength={1}, Next={2}", 
    context.Name, context.Data.Length, context.Next);
```

### 2. Wireshark 过滤器

```
# 只看 Create 请求
smb2.cmd == 5 && !smb2.flags.response

# 只看 Create 响应
smb2.cmd == 5 && smb2.flags.response

# 只看包含 CreateContexts 的请求
smb2.cmd == 5 && smb2.create_contexts
```

### 3. 验证数据格式

```csharp
private static void ValidateCreateContext(CreateContext context)
{
    if (string.IsNullOrEmpty(context.Name))
        throw new ArgumentException("Context name cannot be empty");
        
    if (context.Data == null)
        throw new ArgumentException("Context data cannot be null");
        
    // 验证已知上下文的数据长度
    switch (context.Name)
    {
        case "MxAc":
            if (context.Data.Length != 8)
                throw new ArgumentException("MxAc data must be 8 bytes");
            break;
            
        case "QFid":
            if (context.Data.Length != 32)
                throw new ArgumentException("QFid data must be 32 bytes");
            break;
            
        case "RqLs":
            if (context.Data.Length != 32 && context.Data.Length != 52)
                throw new ArgumentException("RqLs data must be 32 or 52 bytes");
            break;
    }
}
```

## 相关文档

- [SMB2 Create 上下文详解](../../protocols/smb2/smb2-create-contexts.md)
- [租赁上下文处理器 API](../leasing/lease-context-handler-api.md)
- [CreateHelper API](create-helper-api.md)
- [MS-SMB2] 2.2.13.2 SMB2_CREATE_CONTEXT

## 总结

Create 上下文实现的关键要点：

### 必须做
1. ✅ 实现 MxAc 上下文（Windows 兼容性必须）
2. ✅ 如果启用租赁，实现 RqLs 上下文
3. ✅ 正确处理字节序（使用 LittleEndianWriter）
4. ✅ 正确处理 8 字节对齐
5. ✅ 添加适当的错误处理和日志

### 推荐做
6. ⚡ 实现 QFid 上下文（高级功能支持）
7. ⚡ 使用辅助方法简化代码
8. ⚡ 添加性能优化（缓存、快速查找）

### 验证
9. 🔍 使用 Windows 客户端测试
10. 🔍 使用 Wireshark 验证数据格式
11. 🔍 检查日志确认上下文正确处理

实现这些上下文将显著提高服务器的兼容性和性能！

