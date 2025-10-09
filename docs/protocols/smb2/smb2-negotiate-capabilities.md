# SMB2 协商协议能力标志

## 概述

本文档描述 SMB2 协商协议（Negotiate Protocol）中的能力标志（Capabilities），特别是租赁协议相关的 `SMB2_GLOBAL_CAP_LEASING` 标志。

## SMB2 Capabilities 字段

### 定义

在 SMB2 Negotiate Response 中，`Capabilities` 字段是一个 32 位的位标志字段，用于告知客户端服务器支持哪些功能。

```csharp
[Flags]
public enum Capabilities : uint
{
    DFS = 0x00000001,               // SMB2_GLOBAL_CAP_DFS
    Leasing = 0x00000002,           // SMB2_GLOBAL_CAP_LEASING
    LargeMTU = 0x0000004,           // SMB2_GLOBAL_CAP_LARGE_MTU
    MultiChannel = 0x0000008,       // SMB2_GLOBAL_CAP_MULTI_CHANNEL
    PersistentHandles = 0x00000010, // SMB2_GLOBAL_CAP_PERSISTENT_HANDLES
    DirectoryLeasing = 0x00000020,  // SMB2_GLOBAL_CAP_DIRECTORY_LEASING
    Encryption = 0x00000040,        // SMB2_GLOBAL_CAP_ENCRYPTION (SMB 3.x)
}
```

### 各标志说明

| 标志 | 值 | 说明 |
|------|-------|------|
| `DFS` | 0x00000001 | 服务器支持分布式文件系统（DFS） |
| `Leasing` | 0x00000002 | 服务器支持租赁（Leasing）机制 |
| `LargeMTU` | 0x00000004 | 服务器支持大 MTU（多信用操作） |
| `MultiChannel` | 0x00000008 | 服务器支持多通道（SMB 3.0+） |
| `PersistentHandles` | 0x00000010 | 服务器支持持久句柄（SMB 3.0+） |
| `DirectoryLeasing` | 0x00000020 | 服务器支持目录租赁（SMB 3.0+） |
| `Encryption` | 0x00000040 | 服务器支持加密（SMB 3.0+） |

## SMB2_GLOBAL_CAP_LEASING 详解

### 标志值
- **值**: `0x00000002`
- **名称**: `SMB2_GLOBAL_CAP_LEASING`
- **最低版本**: SMB 2.1
- **用途**: 表明服务器支持文件租赁机制

### 作用

当服务器在 Negotiate Response 中设置此标志时：
- ✅ 客户端知道服务器支持租赁
- ✅ 客户端可以在 Create Request 中请求租赁（设置 `RequestedOplockLevel = OplockLevel.Lease`）
- ✅ 客户端可以在 CreateContexts 中包含 `LeaseContext`

当服务器不设置此标志时：
- ❌ 客户端知道服务器不支持租赁
- ❌ 客户端不会尝试请求租赁
- ✅ 兼容不支持租赁的旧版服务器

### 协议规范

根据 [MS-SMB2] 规范：

> **SMB2_GLOBAL_CAP_LEASING (0x00000002)**: When set, indicates that the server supports leasing.
> 
> The server MUST set this capability flag if Connection.Dialect is "2.100" or higher.
> 
> The client, upon receiving this capability flag, SHOULD interpret this as the server supporting leasing, and MAY request leases in the subsequent Create requests.

## 协商流程

### 启用租赁的协商流程

```
┌─────────┐                                    ┌─────────┐
│ 客户端  │                                    │ 服务器  │
└─────────┘                                    └─────────┘
     │                                               │
     │  1. Negotiate Request                         │
     │  ─────────────────────────────────────────>   │
     │     Dialects: [2.002, 2.1, 3.0]              │
     │                                               │
     │                   2. 检查租赁配置              │
     │                      (m_leaseConfig != null)  │
     │                   3. 设置 Leasing 标志         │
     │                                               │
     │  4. Negotiate Response                        │
     │  <─────────────────────────────────────────   │
     │     DialectRevision: 2.1                     │
     │     Capabilities: 0x00000006                 │
     │                   (Leasing | LargeMTU)       │
     │                                               │
     │  5. 客户端看到支持租赁                         │
     │                                               │
     │  6. Create Request                            │
     │  ─────────────────────────────────────────>   │
     │     RequestedOplockLevel: Lease              │
     │     CreateContexts: [LeaseContext]           │
     │                                               │
     │  7. Create Response                           │
     │  <─────────────────────────────────────────   │
     │     OplockLevel: Lease                       │
     │     CreateContexts: [LeaseContext]           │
     │                                               │
```

### 禁用租赁的协商流程

```
┌─────────┐                                    ┌─────────┐
│ 客户端  │                                    │ 服务器  │
└─────────┘                                    └─────────┘
     │                                               │
     │  1. Negotiate Request                         │
     │  ─────────────────────────────────────────>   │
     │     Dialects: [2.002, 2.1, 3.0]              │
     │                                               │
     │                   2. 检查租赁配置              │
     │                      (m_leaseConfig == null)  │
     │                   3. 不设置 Leasing 标志       │
     │                                               │
     │  4. Negotiate Response                        │
     │  <─────────────────────────────────────────   │
     │     DialectRevision: 2.1                     │
     │     Capabilities: 0x00000004                 │
     │                   (LargeMTU only)            │
     │                                               │
     │  5. 客户端看到不支持租赁                       │
     │                                               │
     │  6. Create Request                            │
     │  ─────────────────────────────────────────>   │
     │     RequestedOplockLevel: None               │
     │     CreateContexts: []                       │
     │                                               │
     │  7. Create Response                           │
     │  <─────────────────────────────────────────   │
     │     OplockLevel: None                        │
     │     CreateContexts: []                       │
     │                                               │
```

## 实现细节

### 代码实现

在 `SMBServer` 中：

```csharp
// 检查是否配置了租赁
bool supportsLeasing = (m_leaseConfig != null);

// 传递给 NegotiateHelper
SMB2Command response = NegotiateHelper.GetNegotiateResponse(
    smb2Dialects, 
    m_securityProvider, 
    state, 
    m_transport, 
    m_serverGuid, 
    m_serverStartTime, 
    supportsLeasing  // ← 传递租赁支持标志
);
```

在 `NegotiateHelper` 中：

```csharp
internal static SMB2Command GetNegotiateResponse(
    List<string> smb2Dialects, 
    GSSProvider securityProvider, 
    ConnectionState state, 
    SMBTransportType transportType, 
    Guid serverGuid, 
    DateTime serverStartTime, 
    bool supportsLeasing = false)  // ← 接收租赁支持标志
{
    NegotiateResponse response = new NegotiateResponse();
    
    // 设置基础能力
    response.Capabilities = 0;
    
    // 如果支持租赁，添加 Leasing 标志
    if (supportsLeasing)
    {
        response.Capabilities |= Capabilities.Leasing;
    }
    
    // 添加其他能力标志
    if (state.Dialect != SMBDialect.SMB202 && transportType == SMBTransportType.DirectTCPTransport)
    {
        response.Capabilities |= Capabilities.LargeMTU;
    }
    
    // ...
}
```

### 日志输出

服务器会在日志中记录协商响应的能力标志：

```
[协商响应] DialectRevision: SMB210
[协商响应] Capabilities: Leasing, LargeMTU  ← 启用租赁时
[协商响应] Capabilities: LargeMTU          ← 禁用租赁时
```

## 配置控制

### 启用租赁

```csharp
// 在启动服务器前配置
m_server.LeaseConfiguration = new LeaseManagerConfiguration
{
    DefaultLeaseDuration = TimeSpan.FromSeconds(3),
    // ... 其他配置
};

// Negotiate 响应将包含: Capabilities.Leasing | Capabilities.LargeMTU
```

### 禁用租赁（默认）

```csharp
// 不设置 LeaseConfiguration
m_server.LeaseConfiguration = null; // 或者不设置（默认为 null）

// Negotiate 响应将只包含: Capabilities.LargeMTU（不包含 Leasing）
```

## 客户端行为

### 支持租赁的客户端

当客户端支持租赁时：

1. **服务器设置 Leasing 标志**:
   - 客户端在 Create 请求中设置 `RequestedOplockLevel = OplockLevel.Lease`
   - 客户端在 `CreateContexts` 中包含 `LeaseContext`
   - 服务器处理租赁请求并授予租赁

2. **服务器未设置 Leasing 标志**:
   - 客户端不请求租赁
   - 使用传统的文件访问方式
   - 性能略低但完全兼容

### 不支持租赁的客户端

不支持租赁的客户端会忽略 Leasing 标志，使用传统的文件访问方式。

## 性能影响

### 启用租赁标志
- ✅ 客户端可以缓存文件数据，提高性能
- ✅ 减少网络往返次数
- ❌ 服务器需要维护租赁状态，增加内存和CPU开销

### 禁用租赁标志
- ✅ 服务器无需维护租赁状态，零开销
- ✅ 内存占用更低
- ❌ 客户端无法缓存，每次访问都需要与服务器通信

## 兼容性

### SMB 版本兼容性

| SMB 版本 | 支持租赁 | 说明 |
|----------|---------|------|
| SMB 1.0/CIFS | ❌ | 不支持租赁，只支持 Oplock |
| SMB 2.0 | ❌ | 不支持租赁 |
| SMB 2.1 | ✅ | 首次引入租赁支持 |
| SMB 3.0+ | ✅ | 增强的租赁支持（V2 租赁） |

### 客户端兼容性

- **Windows 7/Server 2008 R2+**: 支持租赁
- **Windows Vista/Server 2008**: 不支持租赁
- **Linux (Samba 4.0+)**: 支持租赁
- **macOS**: 根据版本而定

## 最佳实践

1. **按需启用**: 只有在需要租赁功能时才配置 `LeaseConfiguration`
2. **日志监控**: 通过日志确认 Capabilities 字段是否包含预期的标志
3. **客户端测试**: 使用支持租赁的客户端（如 Windows 10）测试租赁功能
4. **向后兼容**: 禁用租赁不会影响与旧版客户端的兼容性

## 调试技巧

### 检查 Negotiate 响应

使用网络抓包工具（如 Wireshark）查看 Negotiate Response：

```
SMB2 Header
  Command: Negotiate (0x0000)
  Status: 0x00000000 (Success)

Negotiate Response
  StructureSize: 65
  SecurityMode: 0x01 (Signing Enabled)
  DialectRevision: 0x0210 (SMB 2.1)
  Capabilities: 0x00000006 (0x02 | 0x04)
    .... .... .... ...0 = DFS: Not set
    .... .... .... ..1. = Leasing: Set          ← 租赁已启用
    .... .... .... .1.. = Large MTU: Set
    .... .... .... 0... = Multi Channel: Not set
```

### 查看日志

服务器日志会显示协商响应的能力标志：

```
[Information] [协商响应] Capabilities: Leasing, LargeMTU
```

如果没有看到 "Leasing"，说明租赁配置未正确设置。

## 相关文档

- [SMB2 租赁协议详解](smb2-lease-protocol.md)
- [租赁使用指南](../../guides/getting-started/lease-usage-guide.md)
- [租赁管理器 API](../../api/server/leasing/lease-manager-api.md)
- [MS-SMB2] 2.2.3 SMB2 NEGOTIATE Response

## 总结

`SMB2_GLOBAL_CAP_LEASING` 标志是租赁协议的关键入口点：

- ✅ **设置此标志**: 客户端知道可以请求租赁，启用高性能缓存
- ❌ **不设置此标志**: 客户端使用传统访问方式，服务器零开销
- 🔧 **配置控制**: 通过 `SMBServer.LeaseConfiguration` 属性动态控制

这种设计使得租赁协议成为一个完全可选的功能，可以根据实际需求灵活启用或禁用。

