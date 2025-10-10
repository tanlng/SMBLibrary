# RqLs (Request Lease) 上下文详解

## 概述

**RqLs** (Request Lease) 是 SMB2 协议中用于租约（Lease）协商的 Create 上下文。租约是一种客户端缓存机制，允许客户端在本地缓存文件数据，从而减少网络往返，提高性能。

## 重要概念澄清

### ⚠️ 常见误解

**错误理解**：
- ❌ 客户端必须在 CREATE Request 中包含 RqLs 上下文才能获得租约
- ❌ 服务器只能响应客户端的 RqLs 请求
- ❌ RqLs 是一个独立的请求命令

**正确理解**：
- ✅ RqLs 是 CREATE 请求/响应中的一个**上下文**（Context），不是独立命令
- ✅ 客户端**可以**在 CREATE Request 中包含 RqLs 来**请求**租约
- ✅ 服务器**也可以**主动在 CREATE Response 中返回 RqLs，**即使客户端没有请求**
- ✅ 服务器根据文件状态、访问模式、策略等**自主决定**是否授予租约

### 核心原则

> **租约的授予是服务器的自主决定，不依赖于客户端是否发送 RqLs 请求。**

服务器可以：
1. 响应客户端的租约请求（客户端发送了 RqLs）
2. 主动授予租约（客户端未发送 RqLs，但服务器认为适合授予）
3. 拒绝客户端的租约请求（客户端发送了 RqLs，但服务器拒绝）

## RqLs 上下文结构

### SMB 2.0/2.1 租约（V1）

```
struct SMB2_CREATE_REQUEST_LEASE_V1 {
    Guid   LeaseKey;        // 16 字节 - 租约键
    uint   LeaseState;      // 4 字节 - 请求/授予的租约状态
    uint   LeaseFlags;      // 4 字节 - 租约标志
    ulong  LeaseDuration;   // 8 字节 - 租约持续时间（通常为 0）
}
// 总计：32 字节
```

### SMB 3.0+ 租约（V2）

```
struct SMB2_CREATE_REQUEST_LEASE_V2 {
    Guid   LeaseKey;        // 16 字节 - 租约键
    uint   LeaseState;      // 4 字节 - 请求/授予的租约状态
    uint   LeaseFlags;      // 4 字节 - 租约标志
    ulong  LeaseDuration;   // 8 字节 - 租约持续时间（通常为 0）
    Guid   ParentLeaseKey;  // 16 字节 - 父租约键
    ushort Epoch;           // 2 字节 - 租约纪元
    ushort Reserved;        // 2 字节 - 保留
}
// 总计：52 字节
```

## 租约状态（LeaseState）

租约状态是**位标志**，可以组合：

| 位标志 | 值 | 说明 | 允许客户端 |
|--------|----|----|-----------|
| `SMB2_LEASE_NONE` | 0x00 | 无租约 | 无特殊权限 |
| `SMB2_LEASE_READ_CACHING` | 0x01 | 读取缓存 | 缓存读取数据 |
| `SMB2_LEASE_HANDLE_CACHING` | 0x02 | 句柄缓存 | 缓存文件句柄（延迟关闭） |
| `SMB2_LEASE_WRITE_CACHING` | 0x04 | 写入缓存 | 缓存写入数据（本地延迟写入） |

### 常见组合

| 组合 | 值 | 说明 | 适用场景 |
|------|----|----|---------|
| `R` | 0x01 | 只读缓存 | 多个客户端读取同一文件 |
| `RH` | 0x03 | 读取+句柄 | 频繁打开/关闭文件读取 |
| `RW` | 0x05 | 读写缓存 | 单客户端读写 |
| `RWH` | 0x07 | 完全租约 | 独占访问，最高性能 |

## 工作流程

### 场景 1：客户端请求租约

```
┌─────────┐                                    ┌─────────┐
│ Client  │                                    │ Server  │
└────┬────┘                                    └────┬────┘
     │                                              │
     │  CREATE Request                             │
     │  ├─ Name: "file.txt"                        │
     │  └─ CreateContexts:                         │
     │      └─ RqLs (Request Lease)                │
     │          ├─ LeaseKey: {GUID}                │
     │          └─ LeaseState: RWH (0x07)          │
     ├─────────────────────────────────────────────>│
     │                                              │
     │                         服务器评估：         │
     │                         • 文件是否被其他客户端打开？
     │                         • 当前访问模式是否允许租约？
     │                         • 策略是否允许？       │
     │                                              │
     │  CREATE Response                            │
     │  ├─ FileID: {Persistent, Volatile}          │
     │  ├─ OplockLevel: Lease (0xFF)  ✅           │
     │  └─ CreateContexts:                         │
     │      └─ RqLs (Response)                     │
     │          ├─ LeaseKey: {GUID}                │
     │          └─ LeaseState: RWH (0x07) ✅ 授予  │
     │<─────────────────────────────────────────────┤
     │                                              │
```

**关键点**：
- ✅ 客户端在 CREATE Request 中包含 RqLs，请求 RWH 租约
- ✅ 服务器评估后，在 CREATE Response 中返回 RqLs，授予 RWH 租约
- ✅ `OplockLevel` 字段设置为 `Lease` (0xFF)

### 场景 2：服务器主动授予租约（客户端未请求）

```
┌─────────┐                                    ┌─────────┐
│ Client  │                                    │ Server  │
└────┬────┘                                    └────┬────┘
     │                                              │
     │  CREATE Request                             │
     │  ├─ Name: "file.txt"                        │
     │  └─ CreateContexts:                         │
     │      ├─ MxAc (Maximum Access) ✅            │
     │      └─ QFid (Query File ID) ✅             │
     │         （没有 RqLs）                        │
     ├─────────────────────────────────────────────>│
     │                                              │
     │                         服务器判断：         │
     │                         • 文件适合缓存       │
     │                         • 单客户端访问       │
     │                         • 策略允许主动授予   │
     │                         → 决定授予读取租约   │
     │                                              │
     │  CREATE Response                            │
     │  ├─ FileID: {Persistent, Volatile}          │
     │  ├─ OplockLevel: Lease (0xFF)  ✅ 主动设置  │
     │  └─ CreateContexts:                         │
     │      ├─ MxAc (Response)                     │
     │      ├─ QFid (Response)                     │
     │      └─ RqLs (Response) ✅ 主动添加         │
     │          ├─ LeaseKey: {服务器生成}          │
     │          └─ LeaseState: R (0x01)            │
     │<─────────────────────────────────────────────┤
     │                                              │
```

**关键点**：
- ✅ 客户端**未**在 CREATE Request 中请求租约
- ✅ 服务器**主动**在 CREATE Response 中返回 RqLs，授予 R 租约
- ✅ 服务器生成租约键（LeaseKey）
- ✅ `OplockLevel` 字段设置为 `Lease` (0xFF)

### 场景 3：服务器拒绝租约请求

```
┌─────────┐                                    ┌─────────┐
│ Client  │                                    │ Server  │
└────┬────┘                                    └────┬────┘
     │                                              │
     │  CREATE Request                             │
     │  └─ CreateContexts:                         │
     │      └─ RqLs (Request Lease)                │
     │          └─ LeaseState: RWH (0x07)          │
     ├─────────────────────────────────────────────>│
     │                                              │
     │                         服务器评估：         │
     │                         • 文件被多个客户端打开
     │                         • 不允许写入租约     │
     │                         → 拒绝租约请求       │
     │                                              │
     │  CREATE Response                            │
     │  ├─ FileID: {Persistent, Volatile}          │
     │  ├─ OplockLevel: None (0x00)  ❌ 未授予     │
     │  └─ CreateContexts:                         │
     │      （不包含 RqLs）❌                       │
     │<─────────────────────────────────────────────┤
     │                                              │
```

**关键点**：
- ❌ 客户端请求了租约，但服务器拒绝
- ❌ CREATE Response 中**不包含** RqLs 上下文
- ❌ `OplockLevel` 字段保持 `None` (0x00)
- ✅ 文件仍然成功打开，只是没有租约

## 租约键（LeaseKey）

### 用途

- 唯一标识一个租约
- 允许同一客户端的多个句柄共享租约
- 用于租约中断通知

### 生成规则

| 场景 | LeaseKey 生成方 | 说明 |
|------|---------------|------|
| 客户端请求租约 | **客户端** | 客户端生成并在 RqLs 中发送 |
| 服务器主动授予 | **服务器** | 服务器生成并在 RqLs 中返回 |

### 租约共享

同一客户端的多个文件句柄可以共享租约，只需使用**相同的 LeaseKey**：

```
Client → Server: CREATE file.txt, RqLs(LeaseKey=AAAA, State=RWH)
Server → Client: Response, RqLs(LeaseKey=AAAA, State=RWH) ✅

Client → Server: CREATE file.txt (again), RqLs(LeaseKey=AAAA, State=RWH)
Server → Client: Response, RqLs(LeaseKey=AAAA, State=RWH) ✅ 共享租约
                 第二个句柄复用了同一个租约
```

## 租约标志（LeaseFlags）

| 标志 | 值 | 说明 |
|------|----|----|
| None | 0x00 | 无标志 |
| `SMB2_LEASE_FLAG_BREAK_IN_PROGRESS` | 0x02 | 租约中断进行中 |
| `SMB2_LEASE_FLAG_PARENT_LEASE_KEY_SET` | 0x04 | 父租约键已设置（V2） |

## 服务器实现建议

### 何时授予租约？

服务器应该在以下情况考虑授予租约：

✅ **适合授予租约**：
- 文件只被单个客户端访问
- 文件不是频繁修改的
- 客户端支持租约协议（SMB 2.1+）
- 文件不是关键系统文件
- 策略允许租约

❌ **不适合授予租约**：
- 文件被多个客户端同时打开
- 客户端请求了写入租约，但其他客户端持有读取租约
- 文件系统不支持租约通知
- 管理员策略禁用租约

### 租约降级

服务器可以授予**低于**客户端请求的租约级别：

```
客户端请求: RWH (0x07)
服务器授予: RH  (0x03)  ← 降级（移除了 W）
```

原因可能是：
- 其他客户端持有读取租约
- 策略限制写入租约
- 文件属性不允许缓存写入

### 实现示例

```csharp
// 评估是否授予租约
private LeaseState EvaluateLeaseGrant(
    CreateRequest request, 
    string filePath,
    LeaseState requestedState)
{
    // 1. 检查是否有其他客户端打开此文件
    if (HasOtherClientsOpened(filePath))
    {
        // 如果请求写入租约，拒绝
        if ((requestedState & LeaseState.WriteCaching) != 0)
            return LeaseState.None;
            
        // 可以授予读取租约
        return LeaseState.ReadCaching;
    }
    
    // 2. 检查策略
    if (!m_config.AllowLeases)
        return LeaseState.None;
    
    // 3. 单客户端访问，可以授予完整租约
    return requestedState;
}

// 在 CreateHelper 中处理
public static SMB2Command GetCreateResponse(...)
{
    // ... 创建文件 ...
    
    CreateResponse response = new CreateResponse();
    response.OplockLevel = OplockLevel.None; // 默认无租约
    
    // 检查客户端是否请求租约
    var leaseRequest = request.CreateContexts
        .FirstOrDefault(c => c.Name == "RqLs");
    
    LeaseState grantedState = LeaseState.None;
    Guid leaseKey = Guid.Empty;
    
    if (leaseRequest != null)
    {
        // 客户端请求了租约
        var leaseContext = ParseLeaseContext(leaseRequest);
        grantedState = EvaluateLeaseGrant(request, path, leaseContext.LeaseState);
        leaseKey = leaseContext.LeaseKey;
    }
    else
    {
        // 客户端未请求，服务器可以主动授予
        if (ShouldProactivelyGrantLease(path))
        {
            grantedState = LeaseState.ReadCaching; // 授予读取租约
            leaseKey = Guid.NewGuid(); // 服务器生成租约键
        }
    }
    
    // 如果授予了租约，添加 RqLs 响应上下文
    if (grantedState != LeaseState.None)
    {
        response.OplockLevel = OplockLevel.Lease;
        
        var leaseResponse = new LeaseContext(
            leaseKey,
            grantedState,
            LeaseFlags.None,
            0 // LeaseDuration 通常为 0
        );
        response.CreateContexts.Add(leaseResponse);
        
        // 注册到 LeaseManager
        m_leaseManager.CreateLease(new LeaseRequest
        {
            LeaseKey = leaseKey,
            LeaseState = grantedState,
            SessionId = session.SessionID,
            FileId = fileID,
            FilePath = path
        });
    }
    
    return response;
}
```

## 常见问题

### Q1: 客户端必须请求租约才能获得吗？

**A**: 不是。服务器可以主动授予租约，即使客户端没有在 CREATE Request 中包含 RqLs 上下文。

### Q2: 服务器必须响应客户端的租约请求吗？

**A**: 不是。服务器可以拒绝租约请求，返回的 CREATE Response 中不包含 RqLs 上下文。

### Q3: RqLs 是一个独立的命令吗？

**A**: 不是。RqLs 是 CREATE 请求/响应中的一个**上下文**（Context），不是独立的 SMB2 命令。

### Q4: OplockLevel 和 RqLs 的关系？

**A**: 
- 如果授予了租约，`OplockLevel` 必须设置为 `Lease` (0xFF)
- 同时 `CreateContexts` 中必须包含 RqLs 上下文
- 两者必须同时存在

### Q5: 租约和 Oplock 的区别？

**A**:
- **Oplock**（Opportunistic Lock）：传统的缓存机制，SMB 1.0+
- **Lease**（租约）：改进的缓存机制，SMB 2.1+
- Lease 提供更细粒度的控制和更好的性能

### Q6: 如何判断服务器是否支持租约？

**A**: 检查 SMB2 Negotiate Response 中的 `Capabilities` 字段是否包含 `SMB2_GLOBAL_CAP_LEASING` (0x00000002)。

## 性能影响

### 授予租约的好处

✅ **减少网络往返**：
- 读取租约：本地缓存读取数据，减少 Read 请求
- 写入租约：本地缓存写入，批量提交
- 句柄租约：延迟关闭句柄，减少 Open/Close 循环

✅ **提高吞吐量**：
- 减少服务器负载
- 减少网络带宽使用
- 提高客户端响应速度

### 租约中断的开销

❌ **性能损失**：
- 租约中断通知需要时间
- 客户端需要刷新缓存
- 可能导致操作暂停

⚠️ **适用场景**：
- 单客户端访问：租约效果最佳
- 多客户端只读：读取租约仍有益
- 多客户端读写：租约频繁中断，效果差

## 安全考虑

1. **租约键验证**：
   - 服务器应验证租约键的唯一性
   - 防止客户端使用其他客户端的租约键

2. **权限检查**：
   - 授予租约前检查客户端权限
   - 租约不应绕过文件系统权限

3. **租约中断**：
   - 当文件权限改变时，应中断相关租约
   - 防止客户端使用过期的缓存数据

## 总结

### 关键要点

1. ✅ **RqLs 是 CREATE 上下文**，不是独立命令
2. ✅ **服务器可以主动授予租约**，不依赖客户端请求
3. ✅ **OplockLevel = Lease** 表示授予了租约
4. ✅ **租约通过 CREATE Response 中的 RqLs 上下文返回**
5. ✅ **服务器可以降级或拒绝租约请求**

### 实现检查清单

- [ ] 解析 CREATE Request 中的 RqLs 上下文
- [ ] 评估是否授予租约（考虑文件状态、访问模式）
- [ ] 支持主动授予租约（即使客户端未请求）
- [ ] 在 CREATE Response 中返回 RqLs 上下文
- [ ] 设置 `OplockLevel = Lease`
- [ ] 注册租约到 LeaseManager
- [ ] 实现租约中断通知机制
- [ ] 处理租约超时和过期

---

**参考文档**：
- [MS-SMB2] 2.2.13.2.8 - SMB2_CREATE_REQUEST_LEASE
- [MS-SMB2] 2.2.14.2.8 - SMB2_CREATE_RESPONSE_LEASE
- [MS-SMB2] 2.2.23.1 - SMB2_LEASE_BREAK_NOTIFICATION

**最后更新**: 2025-01-10

