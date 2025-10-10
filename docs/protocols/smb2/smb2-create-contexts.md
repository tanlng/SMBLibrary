# SMB2 Create 上下文（Create Contexts）详解

## 概述

SMB2 Create 上下文（Create Contexts，也称为 Extra Information）是 SMB2 Create 请求和响应中的扩展数据结构，用于传递额外的文件创建和访问信息。客户端可以在 Create 请求中包含多个上下文来请求特定功能，服务器则在响应中返回相应的上下文数据。

## 上下文结构

### CreateContext 基本结构

根据 [MS-SMB2] 2.2.13.2，每个 CreateContext 具有以下结构：

```
struct SMB2_CREATE_CONTEXT {
    uint   Next;           // 到下一个上下文的偏移（8字节对齐），最后一个为0
    ushort NameOffset;     // 名称的偏移
    ushort NameLength;     // 名称的长度
    ushort Reserved;       // 保留字段
    ushort DataOffset;     // 数据的偏移
    uint   DataLength;     // 数据的长度
    byte[] Name;           // 上下文名称（ASCII字符串）
    byte[] Data;           // 上下文数据（格式取决于上下文类型）
}
```

### 对齐要求

- 每个 CreateContext 必须 **8 字节对齐**
- Name 字段后需要填充到 8 字节边界
- 整个 CreateContext 链表必须 8 字节对齐

## 常见的 Create 上下文类型

### 1. MxAc (Maximum Access) - 最大访问权限

**名称**: `"MxAc"`  
**长度**: 8 字节  
**版本**: SMB 2.1+

#### 用途
查询打开文件时用户可以获得的最大访问权限，而不实际请求这些权限。

#### 数据结构
```
struct MxAc_Response {
    uint QueryStatus;    // 查询状态（NTStatus 值）
    uint MaximalAccess;  // 最大访问掩码（AccessMask）
}
```

#### 必要性
- ✅ **高**: Windows 资源管理器和许多应用程序依赖此上下文来确定文件权限
- ✅ 用于正确显示文件属性和可用操作
- ✅ 影响用户界面的右键菜单和文件操作按钮的可用性
- ❌ 如果不返回，客户端可能无法正确判断权限，导致操作失败

#### 示例值
```
QueryStatus: 0x00000000 (STATUS_SUCCESS)
MaximalAccess: 0x001F01FF (GENERIC_READ | GENERIC_WRITE | GENERIC_EXECUTE | DELETE | ...)
```

#### 实现
```csharp
public static void AddMxAcContext(CreateResponse response, AccessMask maximalAccess)
{
    uint queryStatus = 0x00000000; // STATUS_SUCCESS
    
    byte[] mxAcData = new byte[8];
    LittleEndianWriter.WriteUInt32(mxAcData, 0, queryStatus);
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

### 2. QFid (Query File ID) - 查询文件 ID

**名称**: `"QFid"`  
**长度**: 32 字节  
**版本**: SMB 2.1+

#### 用途
返回服务器端的文件标识符，用于后续的文件操作和引用。

#### 数据结构
```
struct QFid_Response {
    byte[32] FileId;    // 32字节的不透明文件ID
}
```

#### 必要性
- ⚠️ **中**: 某些高级文件操作和缓存机制需要此上下文
- ✅ 用于文件变更通知（Change Notify）
- ✅ 用于持久句柄（Persistent Handles）
- ✅ 用于租赁和 Oplock 机制的关联
- ⚡ 如果不返回，某些高级功能可能无法使用

#### 实现
```csharp
public static void AddQFidContext(FileID fileID, CreateResponse response)
{
    // 将 FileID 转换为 32 字节的不透明 ID
    byte[] opaqueFileId = new byte[32];
    byte[] persistentBytes = LittleEndianConverter.GetBytes(fileID.Persistent);
    Array.Copy(persistentBytes, 0, opaqueFileId, 0, 8);
    // 剩余 24 字节保持为 0
    
    var qfidContext = new CreateContext
    {
        Name = "QFid",
        Data = opaqueFileId,
        Next = 0
    };
    response.CreateContexts.Add(qfidContext);
}
```

### 3. RqLs (Request Lease) - 租约上下文

**名称**: `"RqLs"`  
**长度**: 32 字节 (V1) 或 52 字节 (V2)  
**版本**: SMB 2.1+ (V1), SMB 3.0+ (V2)

#### 用途
用于租约（Lease）协商的 Create 上下文。租约是客户端缓存机制，允许客户端本地缓存文件数据。

#### ⚠️ 重要概念

**RqLs 不是独立的请求命令，而是 CREATE 请求/响应中的上下文。**

- ✅ 客户端**可以**在 CREATE Request 中包含 RqLs 来**请求**租约
- ✅ 服务器**也可以**主动在 CREATE Response 中返回 RqLs，**即使客户端没有请求**
- ✅ 服务器根据文件状态、访问模式等**自主决定**是否授予租约
- ✅ 服务器可以降级或拒绝客户端的租约请求

> **租约的授予是服务器的自主决定，不依赖于客户端是否发送 RqLs 请求。**

#### 数据结构（V1）
```
struct RqLs_V1 {
    Guid   LeaseKey;        // 租约键（16字节）
    uint   LeaseState;      // 租约状态（位标志）
    uint   LeaseFlags;      // 租约标志
    ulong  LeaseDuration;   // 租约持续时间（通常为 0）
}
```

#### 租约状态（LeaseState）

| 位标志 | 值 | 说明 |
|--------|----|----|
| `SMB2_LEASE_NONE` | 0x00 | 无租约 |
| `SMB2_LEASE_READ_CACHING` | 0x01 | 读取缓存 |
| `SMB2_LEASE_HANDLE_CACHING` | 0x02 | 句柄缓存 |
| `SMB2_LEASE_WRITE_CACHING` | 0x04 | 写入缓存 |

常见组合：`R`(0x01), `RH`(0x03), `RW`(0x05), `RWH`(0x07)

#### 必要性
- ✅✅ **极高**: 租赁协议的核心，对性能影响显著
- ✅ 减少网络往返，提高文件访问速度（30-70% 性能提升）
- ✅ 支持客户端本地缓存
- ⚠️ 服务器可以选择不实现（返回 OplockLevel=None）

#### 实现
详细说明：[RqLs 租约上下文详解](rqls-lease-context-explained.md)  
API 文档：[租赁上下文处理器 API](../../api/server/leasing/lease-context-handler-api.md)

### 4. DHnQ (Durable Handle Request) - 持久句柄请求

**名称**: `"DHnQ"`  
**长度**: 16 字节  
**版本**: SMB 2.1+

#### 用途
请求持久句柄，允许客户端在网络中断后重新连接并恢复文件句柄。

#### 必要性
- ⚠️ **中**: 网络不稳定环境下的重要功能
- ✅ 提高网络中断后的恢复能力
- ✅ 减少因临时断网导致的文件操作失败
- ⚡ 大多数场景可选，但某些企业应用依赖此功能

### 5. DHnC (Durable Handle Reconnect) - 持久句柄重连

**名称**: `"DHnC"`  
**长度**: 可变  
**版本**: SMB 2.1+

#### 用途
客户端在网络中断后重新连接时，使用此上下文恢复之前的持久句柄。

#### 必要性
- ⚠️ **中**: 配合 DHnQ 使用
- ✅ 网络中断后恢复会话的关键
- ⚡ 只有请求了持久句柄的客户端才会使用

### 6. AlSi (Allocation Size) - 分配大小

**名称**: `"AlSi"`  
**长度**: 8 字节  
**版本**: SMB 2.1+

#### 用途
指定文件的初始分配大小。

#### 必要性
- ⚠️ **低**: 优化磁盘空间分配
- ⚡ 大多数情况下可选

### 7. ExtA (Extended Attributes) - 扩展属性

**名称**: `"ExtA"`  
**长度**: 可变  
**版本**: SMB 2.1+

#### 用途
设置或查询文件的扩展属性（如 NTFS 的备用数据流）。

#### 必要性
- ⚠️ **低**: 仅在需要扩展属性时使用
- ⚡ 普通文件操作不需要

## 上下文处理流程

### 客户端请求流程

```
1. 客户端打开文件
   ↓
2. 构造 Create Request
   - RequestedOplockLevel: Lease
   - CreateContexts: [RqLs, MxAc, QFid]
   ↓
3. 发送到服务器
   ↓
4. 服务器处理请求
   - 检查每个上下文
   - 处理 RqLs: 创建租赁
   - 处理 MxAc: 计算最大访问权限
   - 处理 QFid: 生成文件 ID
   ↓
5. 构造 Create Response
   - OplockLevel: Lease
   - CreateContexts: [RqLs, MxAc, QFid]
   ↓
6. 返回给客户端
```

### 服务器处理流程

```csharp
public static SMB2Command GetCreateResponse(CreateRequest request, ...)
{
    // 1. 创建文件
    NTStatus status = share.FileStore.CreateFile(...);
    
    // 2. 创建基本响应
    CreateResponse response = new CreateResponse();
    response.FileId = fileID;
    
    // 3. 处理请求的上下文
    if (request.CreateContexts != null)
    {
        foreach (var context in request.CreateContexts)
        {
            switch (context.Name)
            {
                case "MxAc":
                    AddMxAcContext(response, CalculateMaximalAccess(...));
                    break;
                    
                case "QFid":
                    AddQFidContext(fileID, response);
                    break;
                    
                case "RqLs":
                    if (supportsLeasing)
                    {
                        var leaseContext = ProcessLeaseContext(context, ...);
                        response.CreateContexts.Add(leaseContext);
                    }
                    break;
                    
                case "DHnQ":
                    if (supportsPersistentHandles)
                    {
                        AddDurableHandleContext(response, ...);
                    }
                    break;
            }
        }
    }
    
    return response;
}
```

## 必要性优先级

### 高优先级（必须实现）

| 上下文 | 必要性 | 原因 |
|--------|--------|------|
| **MxAc** | ⭐⭐⭐⭐⭐ | Windows 客户端强依赖，不返回会影响文件属性显示和操作 |
| **RqLs** | ⭐⭐⭐⭐⭐ | 租赁协议的核心，启用租赁时必须实现 |

### 中优先级（建议实现）

| 上下文 | 必要性 | 原因 |
|--------|--------|------|
| **QFid** | ⭐⭐⭐ | 某些高级功能需要，建议实现以提高兼容性 |
| **DHnQ** | ⭐⭐⭐ | 网络不稳定环境下的重要功能 |

### 低优先级（可选实现）

| 上下文 | 必要性 | 原因 |
|--------|--------|------|
| **AlSi** | ⭐ | 优化功能，对基本操作无影响 |
| **ExtA** | ⭐ | 仅特殊场景需要 |

## 客户端兼容性

### Windows 客户端

Windows 客户端（尤其是 Windows 7+）通常会请求以下上下文：

```
Create Request:
  CreateContexts:
    - MxAc (几乎总是请求)
    - QFid (频繁请求)
    - RqLs (如果服务器支持租赁)
    - DHnQ (如果需要持久句柄)
```

### Linux/Samba 客户端

Linux 客户端通常更简单：

```
Create Request:
  CreateContexts:
    - RqLs (如果服务器支持租赁)
    - MxAc (可选)
```

## 性能影响

### MxAc 上下文

**启用时**:
- ✅ 客户端可以正确判断权限，减少失败的操作尝试
- ✅ 改善用户体验（正确的文件属性显示）
- ⚡ 服务器开销：每次 Create 需要计算权限（约 0.1-1ms）

**禁用时**:
- ❌ Windows 资源管理器可能显示不正确的文件属性
- ❌ 某些操作可能被错误地禁用或启用
- ✅ 略微减少服务器计算开销

### QFid 上下文

**启用时**:
- ✅ 支持文件变更通知和高级缓存机制
- ✅ 提高某些高级功能的兼容性
- ⚡ 服务器开销：需要维护文件 ID 映射（内存增加约 32 字节/文件）

**禁用时**:
- ❌ 某些高级功能可能无法使用
- ✅ 略微减少内存占用

### RqLs 上下文

**启用时**:
- ✅ 大幅提高文件访问性能（客户端缓存）
- ✅ 减少网络往返（可减少 50-90% 的网络请求）
- ⚡ 服务器开销：需要维护租赁状态（内存增加约 100-200 字节/租赁）

**禁用时**:
- ❌ 客户端无法缓存，每次访问都需要与服务器通信
- ❌ 性能显著下降
- ✅ 服务器零租赁开销

## 实现建议

### 最小实现（基本兼容性）

必须实现的上下文：

```csharp
// 1. MxAc - 最大访问权限
if (request.CreateContexts.Any(c => c.Name == "MxAc"))
{
    AddMxAcContext(response, CalculateMaximalAccess(handle, securityContext));
}
```

### 推荐实现（良好兼容性）

建议实现的上下文：

```csharp
// 1. MxAc - 最大访问权限（必须）
if (request.CreateContexts.Any(c => c.Name == "MxAc"))
{
    AddMxAcContext(response, CalculateMaximalAccess(handle, securityContext));
}

// 2. QFid - 查询文件 ID（推荐）
if (request.CreateContexts.Any(c => c.Name == "QFid"))
{
    AddQFidContext(fileID, response);
}

// 3. RqLs - 租赁请求（如果启用租赁）
if (supportsLeasing && request.CreateContexts.Any(c => c.Name == "RqLs"))
{
    var leaseContext = ProcessLeaseRequest(request, session, fileID);
    if (leaseContext != null)
    {
        response.CreateContexts.Add(leaseContext);
        response.OplockLevel = OplockLevel.Lease;
    }
}
```

### 完整实现（最佳兼容性）

包含所有常见上下文：

```csharp
// 遍历所有请求的上下文
foreach (var requestContext in request.CreateContexts)
{
    switch (requestContext.Name)
    {
        case "MxAc":
            AddMxAcContext(response, CalculateMaximalAccess(...));
            break;
            
        case "QFid":
            AddQFidContext(fileID, response);
            break;
            
        case "RqLs":
            if (supportsLeasing)
            {
                ProcessLeaseContext(requestContext, response, ...);
            }
            break;
            
        case "DHnQ":
            if (supportsPersistentHandles)
            {
                ProcessDurableHandleRequest(requestContext, response, ...);
            }
            break;
            
        case "AlSi":
            // 通常忽略，文件系统会自动处理分配大小
            break;
            
        case "ExtA":
            if (supportsExtendedAttributes)
            {
                ProcessExtendedAttributes(requestContext, response, ...);
            }
            break;
    }
}
```

## 错误处理

### 不支持的上下文

如果客户端请求了服务器不支持的上下文：

```csharp
// 方式 1: 忽略（推荐）
// 不返回该上下文，客户端会回退到基本功能

// 方式 2: 返回错误
return new ErrorResponse(request.CommandName, NTStatus.STATUS_NOT_SUPPORTED);

// 方式 3: 返回错误状态在上下文中
var errorContext = new CreateContext
{
    Name = requestContext.Name,
    Data = new byte[4] { /* STATUS_NOT_SUPPORTED */ }
};
```

### 上下文格式错误

```csharp
try
{
    ProcessLeaseContext(context, ...);
}
catch (LeaseException ex)
{
    // 返回错误响应
    return new ErrorResponse(request.CommandName, 
        ConvertLeaseErrorToNTStatus(ex.ErrorCode));
}
```

## 调试和测试

### 使用 Wireshark 查看上下文

```
SMB2 Create Request:
  CreateContexts:
    [0] SMB2_CREATE_CONTEXT
      Name: MxAc
      DataLength: 0
    [1] SMB2_CREATE_CONTEXT
      Name: QFid
      DataLength: 0
    [2] SMB2_CREATE_CONTEXT
      Name: RqLs
      DataLength: 32
      Data: [Lease Key + Lease State + ...]

SMB2 Create Response:
  CreateContexts:
    [0] SMB2_CREATE_CONTEXT
      Name: MxAc
      DataLength: 8
      Data: [QueryStatus=0 + MaximalAccess=0x001F01FF]
    [1] SMB2_CREATE_CONTEXT
      Name: QFid
      DataLength: 32
      Data: [32-byte File ID]
    [2] SMB2_CREATE_CONTEXT
      Name: RqLs
      DataLength: 32
      Data: [Lease Key + Granted State + ...]
```

### 日志记录

```csharp
state.LogToServer(Severity.Debug, 
    "Create request contexts: {0}", 
    string.Join(", ", request.CreateContexts.Select(c => c.Name)));

state.LogToServer(Severity.Debug, 
    "Create response contexts: {0}", 
    string.Join(", ", response.CreateContexts.Select(c => c.Name)));
```

## 常见问题

### Q1: 为什么 Windows 资源管理器无法正确显示文件属性？

**原因**: 未实现 MxAc 上下文  
**解决**: 在 Create 响应中添加 MxAc 上下文

### Q2: 为什么客户端请求了租赁但没有生效？

**原因**: 
1. Negotiate 响应中未设置 `Capabilities.Leasing` 标志
2. 未在 Create 响应中返回 RqLs 上下文

**解决**: 
1. 确保 `m_server.LeaseConfiguration` 已配置
2. 在 Create 处理中正确处理 RqLs 上下文

### Q3: 哪些上下文是必须实现的？

**答案**: 
- **必须**: MxAc（Windows 兼容性）、RqLs（如果启用租赁）
- **推荐**: QFid（高级功能兼容性）
- **可选**: DHnQ、AlSi、ExtA

### Q4: 如果客户端请求了多个上下文，必须都返回吗？

**答案**: 不一定。服务器可以选择性地返回支持的上下文。客户端会根据返回的上下文调整行为。但是，如果请求了 MxAc 或 RqLs，强烈建议返回以保证兼容性。

## 性能优化

### 上下文处理优化

```csharp
// 使用字典快速查找
var requestedContexts = request.CreateContexts.ToDictionary(c => c.Name);

// 只处理请求的上下文
if (requestedContexts.ContainsKey("MxAc"))
{
    AddMxAcContext(response, ...);
}

// 批量添加响应上下文
var responseContexts = new List<CreateContext>();
if (requestedContexts.ContainsKey("MxAc"))
    responseContexts.Add(CreateMxAcContext(...));
if (requestedContexts.ContainsKey("QFid"))
    responseContexts.Add(CreateQFidContext(...));
    
response.CreateContexts = responseContexts;
```

### 内存优化

```csharp
// 重用 byte[] 缓冲区
private static readonly byte[] s_mxAcTemplate = new byte[8];

// 避免重复分配
public static CreateContext CreateMxAcContext(AccessMask maximalAccess)
{
    byte[] data = new byte[8];
    LittleEndianWriter.WriteUInt32(data, 0, 0); // QueryStatus
    LittleEndianWriter.WriteUInt32(data, 4, (uint)maximalAccess);
    
    return new CreateContext { Name = "MxAc", Data = data };
}
```

## 相关规范

- **[MS-SMB2] 2.2.13**: SMB2 CREATE Request
- **[MS-SMB2] 2.2.14**: SMB2 CREATE Response
- **[MS-SMB2] 2.2.13.2**: SMB2_CREATE_CONTEXT
- **[MS-SMB2] 2.2.13.2.1**: SMB2_CREATE_EA_BUFFER
- **[MS-SMB2] 2.2.13.2.5**: SMB2_CREATE_QUERY_MAXIMAL_ACCESS_REQUEST
- **[MS-SMB2] 2.2.13.2.6**: SMB2_CREATE_QUERY_ON_DISK_ID
- **[MS-SMB2] 2.2.13.2.8**: SMB2_CREATE_REQUEST_LEASE

## 总结

Create 上下文是 SMB2 协议中的关键扩展机制：

### 核心上下文（必须实现）
- ✅ **MxAc**: Windows 兼容性的基础，影响文件属性显示
- ✅ **RqLs**: 租赁协议的核心，启用租赁时必须实现

### 推荐上下文（提高兼容性）
- ⚡ **QFid**: 支持高级文件操作和缓存

### 实现策略
1. **第一阶段**: 实现 MxAc（保证基本兼容性）
2. **第二阶段**: 实现 RqLs（如果启用租赁）
3. **第三阶段**: 实现 QFid（提高高级功能支持）
4. **第四阶段**: 根据需要实现其他上下文

正确实现这些上下文可以显著提高客户端兼容性和用户体验！

