# SMB 2.0/2.1 租赁协议架构设计

## 概述

本文档描述了 SMBLibrary 中 SMB 2.0/2.1 租赁协议的架构设计，包括服务器端和客户端的组件设计、数据流、状态管理以及关键设计决策。

## 架构目标

### 主要目标
- **性能优化**: 通过客户端缓存减少网络传输
- **一致性保证**: 确保缓存数据与服务器数据的一致性
- **可扩展性**: 支持多种租赁类型和未来扩展
- **可靠性**: 优雅处理租赁中断和超时情况
- **兼容性**: 与现有 SMBLibrary 架构无缝集成

### 设计原则
- **模块化设计**: 租赁功能作为独立模块实现
- **向后兼容**: 不影响不支持租赁的客户端
- **线程安全**: 支持多线程并发访问
- **资源管理**: 有效管理内存和网络资源

## 整体架构

### 架构层次

```
┌─────────────────────────────────────────────────────────┐
│                    应用层                                │
├─────────────────────────────────────────────────────────┤
│                  租赁管理层                              │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐    │
│  │ 租赁管理器   │  │ 租赁缓存     │  │ 租赁通知     │    │
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

### 1. 租赁管理器 (LeaseManager)

#### 职责
- 管理所有活跃租赁的生命周期
- 处理租赁中断通知
- 管理租赁超时和清理
- 协调租赁状态变更

#### 关键接口
```csharp
public class LeaseManager
{
    // 租赁管理
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

### 2. 租赁上下文处理器 (LeaseContextHandler)

#### 职责
- 处理 Create 请求中的租赁上下文
- 验证租赁请求的有效性
- 生成租赁响应上下文

#### 关键接口
```csharp
public class LeaseContextHandler
{
    public LeaseContext ProcessCreateContext(CreateContext context);
    public CreateContext GenerateResponseContext(LeaseInfo leaseInfo);
    public bool ValidateLeaseRequest(LeaseContext request);
}
```

### 3. 租赁中断处理器 (LeaseBreakHandler)

#### 职责
- 检测需要中断租赁的条件
- 发送租赁中断通知
- 处理租赁中断响应

#### 关键接口
```csharp
public class LeaseBreakHandler
{
    public void NotifyLeaseBreak(LeaseInfo leaseInfo, LeaseBreakReason reason);
    public void ProcessLeaseBreakAcknowledgment(LeaseBreakAck ack);
    public void HandleLeaseBreakTimeout(LeaseInfo leaseInfo);
}
```

### 4. 客户端租赁缓存 (ClientLeaseCache)

#### 职责
- 管理客户端文件缓存
- 处理租赁中断通知
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

### 1. 租赁创建流程

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

### 2. 租赁中断流程

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

### 1. 租赁状态机

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
| NONE | Create | ACTIVE | 创建租赁 |
| ACTIVE | Break | BREAKING | 发送中断通知 |
| BREAKING | Ack | BROKEN | 清理缓存 |
| BREAKING | Timeout | BROKEN | 强制清理 |
| ACTIVE | Expire | BROKEN | 自动过期 |

## 存储设计

### 1. 服务器端存储

#### 租赁注册表
```csharp
// 主索引：租赁键
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
- 定期清理过期租赁

### 2. 客户端存储

#### 缓存结构
```csharp
// 文件缓存
private Dictionary<FileID, CachedFileData> m_fileCache;

// 租赁信息
private Dictionary<FileID, LeaseInfo> m_fileLeases;

// 缓存元数据
private Dictionary<FileID, CacheMetadata> m_cacheMetadata;
```

## 错误处理设计

### 1. 错误类型

#### 租赁相关错误
- `STATUS_LEASE_NOT_FOUND`: 租赁不存在
- `STATUS_LEASE_BREAK_IN_PROGRESS`: 租赁中断进行中
- `STATUS_LEASE_EXPIRED`: 租赁已过期
- `STATUS_LEASE_INVALID`: 租赁无效

#### 网络相关错误
- `STATUS_NETWORK_ERROR`: 网络错误
- `STATUS_TIMEOUT`: 超时错误
- `STATUS_CONNECTION_LOST`: 连接丢失

### 2. 错误恢复策略

#### 服务器端
- 自动清理过期租赁
- 重试失败的租赁中断
- 记录错误日志

#### 客户端
- 降级到非租赁模式
- 重新请求租赁
- 清理无效缓存

## 性能考虑

### 1. 内存管理
- 限制最大租赁数量
- 定期清理过期租赁
- 使用对象池减少分配

### 2. 网络优化
- 批量处理租赁中断
- 压缩租赁上下文数据
- 异步处理租赁操作

### 3. 并发控制
- 使用读写锁保护租赁数据
- 异步处理租赁中断
- 避免死锁和竞态条件

## 安全考虑

### 1. 租赁键安全
- 使用加密安全的随机数生成
- 防止租赁键重放攻击
- 定期轮换租赁键

### 2. 访问控制
- 验证租赁权限
- 防止未授权租赁使用
- 实现审计日志

### 3. 数据保护
- 加密敏感租赁信息
- 防止缓存数据泄露
- 实现数据完整性检查

## 扩展性设计

### 1. 新租赁类型支持
- 插件化的租赁类型系统
- 可配置的租赁策略
- 自定义租赁行为

### 2. 多版本支持
- 向后兼容旧版本
- 渐进式功能升级
- 版本协商机制

## 测试策略

### 1. 单元测试
- 租赁管理器功能测试
- 状态转换测试
- 错误处理测试

### 2. 集成测试
- 端到端租赁流程测试
- 多客户端并发测试
- 网络异常测试

### 3. 性能测试
- 大量租赁性能测试
- 内存使用测试
- 网络吞吐量测试

## 部署考虑

### 1. 配置选项
- 租赁超时时间配置
- 最大租赁数量限制
- 租赁策略选择

### 2. 监控指标
- 活跃租赁数量
- 租赁中断频率
- 缓存命中率

### 3. 日志记录
- 租赁创建和销毁日志
- 租赁中断事件日志
- 性能指标日志

## 总结

本架构设计为 SMBLibrary 的租赁协议实现提供了完整的框架，包括：

- **模块化组件设计**: 便于维护和扩展
- **完整的状态管理**: 确保租赁生命周期正确管理
- **性能优化策略**: 提高系统整体性能
- **安全防护措施**: 保护系统安全
- **错误处理机制**: 提高系统可靠性

该架构设计为后续的具体实现提供了清晰的指导，确保租赁协议能够高效、安全、可靠地集成到 SMBLibrary 中。
