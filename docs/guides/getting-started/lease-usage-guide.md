# SMB2 租赁协议使用指南

## 概述

本指南介绍如何在 SMBServer 中启用和配置 SMB2 租赁协议。租赁协议允许客户端缓存文件数据和元数据，从而提高性能并减少网络流量。

**重要**: 租赁协议是**可选功能**。默认情况下租赁协议是**禁用**的，只有在显式配置 `LeaseConfiguration` 属性后才会启用。

## 快速开始

### 1. 启用和配置租赁协议

租赁协议默认是禁用的。要启用租赁协议，需要在启动服务器前配置 `LeaseConfiguration` 属性：

```csharp
GSSProvider securityProvider = new GSSProvider(authenticationMechanism);
m_server = new SMBLibrary.Server.SMBServer(shares, securityProvider);

// 启用并配置租赁协议（可选，不配置则租赁协议不启用）
m_server.LeaseConfiguration = new SMBLibrary.Server.Leasing.LeaseManagerConfiguration
{
    MaxLeases = 1000,                                    // 最大租赁数量
    DefaultLeaseDuration = TimeSpan.FromSeconds(3),      // 默认租赁持续时间：3秒
    LeaseBreakTimeout = TimeSpan.FromSeconds(30),        // 租赁中断超时：30秒
    CleanupInterval = TimeSpan.FromSeconds(3),           // 清理间隔：3秒
    EnableLeaseBreakNotifications = true,                // 启用租赁中断通知
    EnableLeaseExpirationEvents = true                   // 启用租赁过期事件
};

// 如果不配置 LeaseConfiguration，租赁协议将保持禁用状态
// m_server.LeaseConfiguration = null; // 租赁协议禁用（默认）
```

**注意**: 
- 如果不设置 `LeaseConfiguration` 属性，租赁协议将保持禁用状态
- 服务器在 SMB2 Negotiate 响应中**不会**包含 `SMB2_GLOBAL_CAP_LEASING` 能力标志
- 客户端看到服务器不支持租赁后，将不会发送租赁请求
- 这样可以避免不需要租赁功能时的额外开销

**工作原理**:
1. **启用租赁时**: 服务器在 Negotiate 响应中设置 `Capabilities.Leasing` 标志，客户端可以请求租赁
2. **禁用租赁时**: 服务器不设置该标志，客户端知道服务器不支持租赁，不会尝试请求

### 2. 配置参数说明

#### MaxLeases
- **类型**: `int`
- **默认值**: `1000`
- **说明**: 系统中允许的最大租赁数量。达到此限制后，新的租赁请求将被拒绝。

#### DefaultLeaseDuration
- **类型**: `TimeSpan`
- **默认值**: `TimeSpan.FromMinutes(30)`
- **当前配置**: `TimeSpan.FromSeconds(3)` (3秒)
- **说明**: **租赁的生命周期**。从租赁创建开始计时，到达此时间后租赁自动过期失效。
- **用途**: 控制客户端可以缓存文件数据的最长时间
- **触发条件**: 时间到达后自动触发
- **注意**: 
  - 较短的过期时间（如3秒）适用于测试和开发，可以快速看到租赁过期效果
  - 生产环境建议使用更长的时间（如30分钟）以减少租赁续约频率

#### LeaseBreakTimeout
- **类型**: `TimeSpan`
- **默认值**: `TimeSpan.FromSeconds(30)`
- **说明**: **租赁中断响应超时**。当服务器需要中断一个活跃的租赁时，等待客户端响应的最长时间。
- **用途**: 防止客户端无响应导致租赁中断流程卡住
- **触发条件**: 只在主动中断租赁时使用（例如另一个客户端请求写入同一文件）
- **超时后**: 服务器强制中断租赁，不再等待客户端确认

### 两者的关键区别

| 特性 | DefaultLeaseDuration | LeaseBreakTimeout |
|------|---------------------|-------------------|
| **含义** | 租赁的总生命周期 | 租赁中断的等待时间 |
| **计时起点** | 租赁创建时 | 发送租赁中断通知时 |
| **适用场景** | 所有租赁 | 仅当需要中断租赁时 |
| **到期效果** | 租赁自动过期失效 | 强制中断租赁 |
| **客户端感知** | 客户端需要重新请求租赁 | 客户端必须刷新缓存并确认 |
| **典型值** | 3秒-30分钟 | 10-60秒 |

### 工作流程示例

**场景 1: 租赁正常过期（DefaultLeaseDuration）**
```
时间 0秒:   客户端A请求租赁，服务器授予租赁（持续3秒）
时间 0-3秒: 客户端A可以自由缓存和读写文件
时间 3秒:   租赁自动过期，客户端A的缓存失效
时间 3秒+:  客户端A需要重新请求租赁才能继续缓存
```

**场景 2: 租赁主动中断（LeaseBreakTimeout）**
```
时间 0秒:    客户端A持有租赁（持续30分钟）
时间 10秒:   客户端B请求独占访问同一文件
时间 10秒:   服务器发送租赁中断通知给客户端A，启动30秒超时
时间 10-40秒: 等待客户端A刷新缓存并确认
时间 15秒:   客户端A确认完成，租赁成功中断
时间 15秒+:  客户端B获得访问权限
```

**场景 3: 租赁中断超时（LeaseBreakTimeout）**
```
时间 0秒:    客户端A持有租赁
时间 10秒:   客户端B请求独占访问，服务器发送中断通知
时间 10-40秒: 等待客户端A响应
时间 40秒:   超过LeaseBreakTimeout（30秒），服务器强制中断
时间 40秒+:  客户端B获得访问权限，客户端A的缓存被强制失效
```

#### CleanupInterval
- **类型**: `TimeSpan`
- **默认值**: `TimeSpan.FromMinutes(5)`
- **当前配置**: `TimeSpan.FromSeconds(3)` (3秒)
- **说明**: 清理过期租赁的检查间隔。较短的间隔可以更快地清理过期租赁，但会增加系统开销。

#### EnableLeaseBreakNotifications
- **类型**: `bool`
- **默认值**: `true`
- **说明**: 是否启用租赁中断通知。禁用后，服务器不会主动通知客户端租赁被中断。

#### EnableLeaseExpirationEvents
- **类型**: `bool`
- **默认值**: `true`
- **说明**: 是否启用租赁过期事件。禁用后，服务器不会触发租赁过期事件。

## 使用场景

### 禁用租赁协议（默认）
不配置 `LeaseConfiguration`，租赁协议保持禁用：

```csharp
GSSProvider securityProvider = new GSSProvider(authenticationMechanism);
m_server = new SMBLibrary.Server.SMBServer(shares, securityProvider);

// 不设置 LeaseConfiguration，租赁协议禁用（默认行为）
// 服务器将正常工作，但不支持租赁功能
// Negotiate 响应中不会包含 SMB2_GLOBAL_CAP_LEASING 标志

m_server.Start(serverAddress, transportType, chkSMB1.Checked, chkSMB2.Checked);
```

**效果**:
- ❌ Negotiate 响应中**不包含** `Capabilities.Leasing` 标志
- ❌ 客户端不会尝试请求租赁
- ✅ 服务器正常处理文件操作，没有租赁相关开销

**适用场景**:
- 简单的文件服务器，不需要高级缓存功能
- 资源受限的环境，希望减少内存和CPU开销
- 兼容性测试，需要测试不支持租赁的客户端行为

### 测试和开发环境
使用较短的租赁过期时间（如3秒）来快速测试租赁的创建、过期和中断行为：

```csharp
m_server.LeaseConfiguration = new SMBLibrary.Server.Leasing.LeaseManagerConfiguration
{
    MaxLeases = 1000,
    DefaultLeaseDuration = TimeSpan.FromSeconds(3),      // 3秒后租赁自动过期
    LeaseBreakTimeout = TimeSpan.FromSeconds(30),        // 等待客户端响应中断通知的超时时间
    CleanupInterval = TimeSpan.FromSeconds(3),           // 每3秒检查并清理过期租赁
    EnableLeaseBreakNotifications = true,
    EnableLeaseExpirationEvents = true
};
```

**效果**:
- ✅ Negotiate 响应中**包含** `Capabilities.Leasing` 标志
- ✅ 客户端看到服务器支持租赁，会在 Create 请求中包含租赁上下文
- ✅ 服务器创建租赁并在 3 秒后自动过期

### 生产环境
使用较长的租赁持续时间以最大化性能：

```csharp
m_server.LeaseConfiguration = new SMBLibrary.Server.Leasing.LeaseManagerConfiguration
{
    MaxLeases = 10000,
    DefaultLeaseDuration = TimeSpan.FromMinutes(30),
    LeaseBreakTimeout = TimeSpan.FromSeconds(30),
    CleanupInterval = TimeSpan.FromMinutes(5),
    EnableLeaseBreakNotifications = true,
    EnableLeaseExpirationEvents = true
};
```

## 协商协议中的租赁能力

### SMB2_GLOBAL_CAP_LEASING 标志

服务器在 SMB2 Negotiate 响应中使用 `Capabilities` 字段来告知客户端支持哪些功能。租赁协议通过 `SMB2_GLOBAL_CAP_LEASING` (0x00000002) 标志来表明支持。

### 协商流程

#### 启用租赁时的协商流程

```
客户端 -> 服务器: Negotiate Request
                  Dialects: [SMB 2.002, SMB 2.1, SMB 3.0]

服务器 -> 客户端: Negotiate Response
                  DialectRevision: SMB 2.1
                  Capabilities: 0x00000006 (Leasing | LargeMTU)
                                ↑ 包含 Leasing 标志

客户端 -> 服务器: Create Request (打开文件)
                  RequestedOplockLevel: Lease
                  CreateContexts: [LeaseContext]
                                  ↑ 客户端看到支持租赁，发送租赁请求

服务器 -> 客户端: Create Response
                  OplockLevel: Lease
                  CreateContexts: [LeaseContext]
                                  ↑ 服务器授予租赁
```

#### 禁用租赁时的协商流程

```
客户端 -> 服务器: Negotiate Request
                  Dialects: [SMB 2.002, SMB 2.1, SMB 3.0]

服务器 -> 客户端: Negotiate Response
                  DialectRevision: SMB 2.1
                  Capabilities: 0x00000004 (LargeMTU)
                                ↑ 不包含 Leasing 标志

客户端 -> 服务器: Create Request (打开文件)
                  RequestedOplockLevel: None
                                        ↑ 客户端看到不支持租赁，不请求

服务器 -> 客户端: Create Response
                  OplockLevel: None
                                ↑ 不授予租赁
```

### 实现细节

在 `NegotiateHelper.cs` 中，服务器根据 `LeaseConfiguration` 是否为 null 来决定是否设置 Leasing 标志：

```csharp
// 在 SMBServer 中
bool supportsLeasing = (m_leaseConfig != null);
response = NegotiateHelper.GetNegotiateResponse(..., supportsLeasing);

// 在 NegotiateHelper 中
if (supportsLeasing)
{
    response.Capabilities |= Capabilities.Leasing; // 设置 0x00000002 标志
}
```

## 监控和日志

租赁协议会生成以下类型的日志消息：

1. **租赁创建**: 当客户端成功获取租赁时
2. **租赁中断**: 当服务器需要中断租赁时
3. **租赁过期**: 当租赁自动过期时
4. **租赁清理**: 当过期的租赁被清理时

日志级别：
- `Information`: 租赁的创建、中断和过期
- `Debug`: 详细的租赁操作信息
- `Warning`: 租赁中断超时或其他异常情况
- `Error`: 租赁相关的错误

## 性能考虑

### DefaultLeaseDuration (租赁持续时间)
影响租赁的自动过期周期：

- **较长的持续时间**（如30分钟）：
  - ✅ 优点：减少客户端租赁续约请求，降低网络流量和服务器负载
  - ✅ 优点：客户端可以长时间缓存数据，提高读写性能
  - ❌ 缺点：租赁自然过期需要更长时间（但不影响主动中断）
  - 💡 推荐：生产环境使用 10-30 分钟

- **较短的持续时间**（如3秒）：
  - ✅ 优点：快速看到租赁过期效果，便于测试
  - ✅ 优点：减少过期租赁占用的内存时间
  - ❌ 缺点：频繁的租赁续约请求，增加网络流量
  - ❌ 缺点：客户端缓存频繁失效，降低性能
  - 💡 推荐：测试环境使用 3-10 秒

### LeaseBreakTimeout (租赁中断超时)
影响租赁主动中断时的等待时间：

- **较长的超时**（如60秒）：
  - ✅ 优点：给客户端更多时间刷新缓存，避免数据丢失
  - ✅ 优点：适合网络延迟较高的环境
  - ❌ 缺点：等待客户端响应的时间更长，影响其他客户端访问
  - 💡 推荐：网络状况不佳时使用

- **较短的超时**（如10秒）：
  - ✅ 优点：快速中断租赁，减少其他客户端等待时间
  - ❌ 缺点：客户端可能来不及完成缓存刷新
  - ❌ 缺点：可能导致频繁的强制中断
  - 💡 推荐：本地网络或高速网络使用

**重要提示**: DefaultLeaseDuration 不影响租赁中断速度。即使设置了 30 分钟的持续时间，服务器仍可以在需要时立即中断租赁（受 LeaseBreakTimeout 控制）。

### CleanupInterval (清理间隔)
影响过期租赁的清理速度：

- **较短的间隔**（如1-3秒）：
  - ✅ 优点：快速清理过期租赁，及时释放内存和资源
  - ✅ 优点：配合短的 DefaultLeaseDuration 使用，快速看到清理效果
  - ❌ 缺点：频繁检查增加CPU使用率
  - 💡 推荐：测试环境或租赁持续时间较短时使用

- **较长的间隔**（如5分钟）：
  - ✅ 优点：降低CPU使用率和系统开销
  - ❌ 缺点：过期租赁在内存中保留更长时间
  - ❌ 缺点：资源释放延迟
  - 💡 推荐：生产环境且租赁持续时间较长时使用

**推荐配置**:
- CleanupInterval ≈ DefaultLeaseDuration（保证及时清理）
- CleanupInterval ≤ DefaultLeaseDuration / 2（更激进的清理）

## 故障排除

### 租赁未生效
1. 确认 SMB2 已启用（`chkSMB2.Checked = true`）
2. 确认客户端支持 SMB 2.0 或更高版本
3. 检查日志以确认租赁创建消息

### 租赁过快过期
1. 检查 `DefaultLeaseDuration` 配置
2. 确认系统时间正确
3. 检查 `CleanupInterval` 是否设置得太短

### 内存使用过高
1. 降低 `MaxLeases` 值
2. 增加 `CleanupInterval` 频率
3. 减少 `DefaultLeaseDuration` 以更快过期

## 最佳实践

1. **测试环境**: 使用短的租赁持续时间（3-10秒）来快速验证功能
2. **生产环境**: 使用较长的租赁持续时间（10-30分钟）来优化性能
3. **监控**: 定期监控租赁的创建、中断和过期事件
4. **调优**: 根据实际工作负载调整 `MaxLeases` 和清理间隔
5. **日志**: 在生产环境中启用适当的日志级别以便故障排除

## 相关文档

- [SMB2 租赁协议详解](../../protocols/smb2/smb2-lease-protocol.md)
- [租赁架构文档](../../protocols/smb2/leasing/smb2-lease-architecture.md)
- [租赁管理器 API](../../api/server/leasing/lease-manager-api.md)
- [性能优化指南](../../performance/lease-performance-optimization.md)
- [故障排除指南](../troubleshooting/lease-troubleshooting-guide.md)


