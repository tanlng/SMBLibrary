# SMB 2.0/2.1 租约机制架构设计

## 概述

本文档描述了 SMBLibrary 中 SMB 2.0/2.1 租约机制的架构设计，包括服务器端和客户端的组件设计、数据流、状态管理以及关键设计决策。

## 架构目标

### 主要目标
- **性能优化**: 通过客户端缓存减少网络传输
- **一致性保证**: 确保缓存数据与服务器数据的一致性
- **可扩展性**: 支持多种租约类型和未来扩展
- **可靠性**: 优雅处理租约中断和超时情况
- **兼容性**: 与现有 SMBLibrary 架构无缝集成

### 设计原则
- **模块化设计**: 租约功能作为独立模块实现
- **向后兼容**: 不影响不支持租约的客户端
- **线程安全**: 支持多线程并发访问
- **资源管理**: 有效管理内存和网络资源

## 整体架构

### 架构层次

```
┌─────────────────────────────────────────────────────────┐
│                    应用层                                │
├─────────────────────────────────────────────────────────┤
│                  租约管理层                              │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐    │
│  │ 租约管理器   │  │ 租约缓存     │  │ 租约通知     │    │
│  └─────────────┘  └─────────────┘  └─────────────┘    │
├─────────────────────────────────────────────────────────┤
│                  SMB2 协议层                           │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐    │
│  │ Create 命令  │  │ LeaseBreak   │  │ 其他 SMB2    │    │
│  └─────────────┘  └─────────────┘  └─────────────┘    │
├─────────────────────────────────────────────────────────┤
│                  会话管理层                              │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐    │
│  │ SMB2Session │  │ 连接状态     │  │ 文件句柄     │    │
│  └─────────────┘  └─────────────┘  └─────────────┘    │
├─────────────────────────────────────────────────────────┤
│                  传输层                                 │
└─────────────────────────────────────────────────────────┘
```

## 核心组件设计

### 1. 租约管理器 (LeaseManager)

#### 职责
- 管理所有活跃租约的生命周期
- 处理租约中断通知
- 管理租约超时和清理
- 协调租约状态变更

#### 关键接口
```csharp
public class LeaseManager
{
    // 租约管理
    public LeaseInfo CreateLease(LeaseRequest request);
    public void BreakLease(Guid leaseKey, LeaseBreakReason reason);
    public void AcknowledgeLeaseBreak(Guid leaseKey);
    
    // 状态查询
    public LeaseInfo GetLeaseInfo(Guid leaseKey);
    public List<LeaseInfo> GetActiveLeases();
    
    // 事件通知
    public event EventHandler<LeaseBreakEventArgs> LeaseBreakRequested;
    public event EventHandler<LeaseExpiredEventArgs> LeaseExpired;
}
```

#### 数据结构
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
}
```

### 2. 租约上下文处理器 (LeaseContextHandler)

#### 职责
- 处理 Create 请求中的租约上下文
- 验证租约请求的有效性
- 生成租约响应上下文

#### 关键接口
```csharp
public class LeaseContextHandler
{
    public LeaseContext ProcessCreateContext(CreateContext context);
    public CreateContext GenerateResponseContext(LeaseInfo leaseInfo);
    public bool ValidateLeaseRequest(LeaseContext request);
}
```

### 3. 租约中断处理器 (LeaseBreakHandler)

#### 职责
- 检测需要中断租约的条件
- 发送租约中断通知
- 处理租约中断响应

#### 关键接口
```csharp
public class LeaseBreakHandler
{
    public void NotifyLeaseBreak(LeaseInfo leaseInfo, LeaseBreakReason reason);
    public void ProcessLeaseBreakAcknowledgment(LeaseBreakAck ack);
    public void HandleLeaseBreakTimeout(LeaseInfo leaseInfo);
}
```

### 4. 客户端租约缓存 (ClientLeaseCache)

#### 职责
- 管理客户端文件缓存
- 处理租约中断通知
- 维护缓存一致性

#### 关键接口
```csharp
public class ClientLeaseCache
{
    public void CacheFileData(FileID fileId, byte[] data);
    public byte[] GetCachedData(FileID fileId);
    public void InvalidateCache(FileID fileId);
    public void HandleLeaseBreak(LeaseBreakNotification notification);
}
```

## 数据流设计

### 1. 租约创建流程

```
客户端                    服务器
  │                         │
  │── Create Request ──────→│
  │   (with Lease Context)  │
  │                         │── LeaseContextHandler
  │                         │   .ProcessCreateContext()
  │                         │
  │                         │── LeaseManager
  │                         │   .CreateLease()
  │                         │
  │←── Create Response ─────│
  │   (with Lease Context)  │
  │                         │
  │── Lease Acknowledgment →│
  │                         │
```

### 2. 租约中断流程

```
服务器                    客户端
  │                         │
  │── Lease Break ────────→│
  │   Notification          │
  │                         │── ClientLeaseCache
  │                         │   .HandleLeaseBreak()
  │                         │
  │←── Lease Break ────────│
  │   Acknowledgment        │
  │                         │
  │── Lease Break ────────→│
  │   Response              │
  │                         │
```

## 状态管理设计

### 1. 租约状态机

```
┌─────────────┐    Create     ┌─────────────┐
│   NONE      │─────────────→│   ACTIVE     │
└─────────────┘              └─────────────┘
                                     │
                                     │ Break
                                     ▼
                              ┌─────────────┐
                              │ BREAKING    │
                              └─────────────┘
                                     │
                                     │ Ack
                                     ▼
                              ┌─────────────┐
                              │ BROKEN      │
                              └─────────────┘
```

### 2. 状态转换规则

| 当前状态 | 事件 | 新状态 | 动作 |
|---------|------|--------|------|
| NONE | Create | ACTIVE | 创建租约 |
| ACTIVE | Break | BREAKING | 发送中断通知 |
| BREAKING | Ack | BROKEN | 清理缓存 |
| BREAKING | Timeout | BROKEN | 强制清理 |
| ACTIVE | Expire | BROKEN | 自动过期 |

## 存储设计

### 1. 服务器端存储

#### 租约注册表
```csharp
// 主索引：租约键
private Dictionary<Guid, LeaseInfo> m_leaseRegistry;

// 辅助索引：文件ID
private Dictionary<FileID, List<Guid>> m_fileLeases;

// 辅助索引：会话ID
private Dictionary<ulong, List<Guid>> m_sessionLeases;

// 超时管理
private SortedDictionary<DateTime, List<Guid>> m_expirationQueue;
```

#### 性能优化
- 使用哈希表进行快速查找
- 使用有序字典管理超时
- 定期清理过期租约

### 2. 客户端存储

#### 缓存结构
```csharp
// 文件缓存
private Dictionary<FileID, CachedFileData> m_fileCache;

// 租约信息
private Dictionary<FileID, LeaseInfo> m_fileLeases;

// 缓存元数据
private Dictionary<FileID, CacheMetadata> m_cacheMetadata;
```

## 错误处理设计

### 1. 错误类型

#### 租约相关错误
- `STATUS_LEASE_NOT_FOUND`: 租约不存在
- `STATUS_LEASE_BREAK_IN_PROGRESS`: 租约中断进行中
- `STATUS_LEASE_EXPIRED`: 租约已过期
- `STATUS_LEASE_INVALID`: 租约无效

#### 网络相关错误
- `STATUS_NETWORK_ERROR`: 网络错误
- `STATUS_TIMEOUT`: 超时错误
- `STATUS_CONNECTION_LOST`: 连接丢失

### 2. 错误恢复策略

#### 服务器端
- 自动清理过期租约
- 重试失败的租约中断
- 记录错误日志

#### 客户端
- 降级到非租约模式
- 重新请求租约
- 清理无效缓存

## 性能考虑

### 1. 内存管理
- 限制最大租约数量
- 定期清理过期租约
- 使用对象池减少分配

### 2. 网络优化
- 批量处理租约中断
- 压缩租约上下文数据
- 异步处理租约操作

### 3. 并发控制
- 使用读写锁保护租约数据
- 异步处理租约中断
- 避免死锁和竞态条件

## 安全考虑

### 1. 租约键安全
- 使用加密安全的随机数生成
- 防止租约键重放攻击
- 定期轮换租约键

### 2. 访问控制
- 验证租约权限
- 防止未授权租约使用
- 实现审计日志

### 3. 数据保护
- 加密敏感租约信息
- 防止缓存数据泄露
- 实现数据完整性检查

## 扩展性设计

### 1. 新租约类型支持
- 插件化的租约类型系统
- 可配置的租约策略
- 自定义租约行为

### 2. 多版本支持
- 向后兼容旧版本
- 渐进式功能升级
- 版本协商机制

## 测试策略

### 1. 单元测试
- 租约管理器功能测试
- 状态转换测试
- 错误处理测试

### 2. 集成测试
- 端到端租约流程测试
- 多客户端并发测试
- 网络异常测试

### 3. 性能测试
- 大量租约性能测试
- 内存使用测试
- 网络吞吐量测试

## 部署考虑

### 1. 配置选项
- 租约超时时间配置
- 最大租约数量限制
- 租约策略选择

### 2. 监控指标
- 活跃租约数量
- 租约中断频率
- 缓存命中率

### 3. 日志记录
- 租约创建和销毁日志
- 租约中断事件日志
- 性能指标日志

## 总结

本架构设计为 SMBLibrary 的租约协议实现提供了完整的框架，包括：

- **模块化组件设计**: 便于维护和扩展
- **完整的状态管理**: 确保租约生命周期正确管理
- **性能优化策略**: 提高系统整体性能
- **安全防护措施**: 保护系统安全
- **错误处理机制**: 提高系统可靠性

该架构设计为后续的具体实现提供了清晰的指导，确保租约协议能够高效、安全、可靠地集成到 SMBLibrary 中。

---

## ⚠️ 关键架构约束 (2025-12-03 更新)

### LeaseManager 必须全局共享

**架构要求**: ⭐ **LeaseManager 必须是全局单例，所有 Session 共享同一个实例**

#### 正确的实现

```csharp
// SMBServer.cs - 创建全局 LeaseManager
public class SMBServer
{
    private LeaseManager m_leaseManager;  // 全局单例
    
    public void Start(...)
    {
        if (m_leaseConfig != null)
        {
            m_leaseManager = new LeaseManager(m_leaseConfig);
            m_leaseManager.LogHandler = (severity, message) => Log(severity, message);
        }
    }
    
    private void ConnectRequestCallback(...)
    {
        // ✅ 传入全局 LeaseManager
        state = new SMB2ConnectionState(state, m_leaseConfig, m_leaseManager);
    }
}
```

```csharp
// SMB2ConnectionState.cs - 接收并持有全局引用
public SMB2ConnectionState(ConnectionState state, 
    LeaseManagerConfiguration leaseConfig = null, 
    LeaseManager leaseManager = null) : base(state)
{
    m_leaseConfig = leaseConfig;
    m_leaseManager = leaseManager;  // 接收全局实例
    
    // ⚠️ 不要在这里设置 LogHandler！
    // 多个 ConnectionState 会覆盖全局 LogHandler
}
```

```csharp
// SMB2Session.cs - 使用传入的全局 LeaseManager
private void InitializeLeaseManager(
    LeaseManagerConfiguration leaseConfig, 
    LeaseManager leaseManager)
{
    if (leaseManager != null)
    {
        // ✅ 使用传入的全局 LeaseManager
        m_leaseManager = leaseManager;
    }
    else
    {
        // ❌ 不应该走这个分支（会导致隔离问题）
        m_leaseManager = new LeaseManager(leaseConfig);
    }
    
    // 订阅全局事件
    m_leaseManager.LeaseBreakRequested += OnLeaseBreakRequested;
    m_leaseManager.LeaseExpired += OnLeaseExpired;
}
```

#### 错误的实现（已修复）

```csharp
// ❌ 错误：每个 Session 创建独立 LeaseManager
private void InitializeLeaseManager(LeaseManagerConfiguration leaseConfig, 
    LeaseManager leaseManager)
{
    // 问题：当 leaseManager == null 时创建新实例
    if (leaseManager != null)
    {
        m_leaseManager = leaseManager;
    }
    else
    {
        m_leaseManager = new LeaseManager(leaseConfig);  // ❌ 导致 Lease 隔离
    }
}

// ❌ 错误：未传入全局 LeaseManager
state = new SMB2ConnectionState(state, m_leaseConfig);  // 缺少第三个参数

// ❌ 错误：覆盖全局 LogHandler
if (m_leaseManager != null)
{
    m_leaseManager.LogHandler = (severity, message) => LogToServer(severity, message);
    // 问题：后创建的连接会覆盖之前的 LogHandler
}
```

#### 问题影响

如果 LeaseManager 不是全局共享的，会导致：

1. **Lease 注册隔离**:
   - 客户端 A 在 Session 1 的 LeaseManager 中注册 Lease
   - 客户端 B 在 Session 2 的 LeaseManager 中注册 Lease
   - 两个 LeaseManager 互不知道对方的 Lease

2. **Lease Break 失效**:
   - 客户端 B 创建文件，触发 Session 2 的 LeaseManager.BreakLeases()
   - Session 2 的 LeaseManager 只检查自己的 Registry
   - **不知道** Session 1 有相关 Lease
   - 客户端 A **收不到** Lease Break 通知

3. **目录不自动刷新**:
   - 客户端 A 的缓存不失效
   - 客户端 A 看不到新文件
   - 用户必须手动刷新 (F5)

#### 修复历史

**修复日期**: 2025-12-03  
**问题**: Win10 客户端无法自动刷新目录  
**详细文档**: `docs/analysis/messages/【同步2】win10，另外一台服务器新建子文件夹，win10无法自动更新/3. 修复总结.md`

**修复要点**:
1. `SMBServer.cs` (Line 336): 传入 `m_leaseManager` 给 `SMB2ConnectionState`
2. `SMB2ConnectionState.cs`: 移除 `LogHandler` 覆盖
3. `SMB2Session.cs`: 使用传入的全局 LeaseManager（逻辑已正确）

#### 验证清单

实现 LeaseManager 时，请确认：

- ✅ SMBServer 创建唯一的 LeaseManager 实例
- ✅ 所有 SMB2ConnectionState 接收相同的 LeaseManager 引用
- ✅ 所有 SMB2Session 接收相同的 LeaseManager 引用
- ✅ LogHandler 只在 SMBServer 层设置一次
- ✅ 所有 Session 订阅同一个 LeaseManager 的事件
- ✅ Lease Registry 是全局唯一的 ConcurrentDictionary

#### 测试建议

```csharp
[Test]
public void TestGlobalLeaseManagerSharing()
{
    var server = new SMBServer();
    server.Start(...);
    
    // 模拟两个客户端连接
    var state1 = CreateConnectionState(clientA);
    var state2 = CreateConnectionState(clientB);
    
    // 验证共享同一个 LeaseManager
    Assert.AreSame(state1.LeaseManager, state2.LeaseManager);
    Assert.AreSame(state1.LeaseManager, server.LeaseManager);
    
    // 验证 Lease 跨 Session 可见
    var session1 = state1.CreateSession(...);
    var session2 = state2.CreateSession(...);
    
    var lease1 = session1.CreateLease(...);
    
    // Session 2 应该能看到 Session 1 的 Lease
    var allLeases = session2.LeaseManager.GetAllLeases();
    Assert.Contains(lease1, allLeases);
}
```

---

## 参考资料

- **修复案例详解**: `../../../../../../docs/analysis/messages/【同步2】win10，另外一台服务器新建子文件夹，win10无法自动更新/`
- **SMB2 协议规范**: [MS-SMB2] Section 3.3.1.4 - Per Server Lease Table
- **实现指南**: `../../../guides/implementation/lease-implementation-guide.md`
- **故障排查**: `../../../guides/troubleshooting/lease-troubleshooting-guide.md`

