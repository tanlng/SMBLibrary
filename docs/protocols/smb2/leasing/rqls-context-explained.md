# RqLs (Request Lease) 上下文详解

## 概述

**RqLs** (Request Lease) 是 SMB2 协议中用于租约（Lease）协商的 Create 上下文。租约是一种客户端缓存机制，允许客户端在本地缓存文件数据，从而减少网络往返，提高性能。

## ⚠️ 重要概念澄清（基于 Wireshark 抓包验证）

### 租约请求机制

**客户端如何请求租约？（基于真实抓包数据）**

✅ **正确理解（已通过 Wireshark 验证）**：

客户端请求租约需要**同时做两件事**：

1. **设置 RequestedOplockLevel = Lease (0xFF)**
   ```
   Create Request
       Oplock: Lease (0xff)
   ```

2. **在 CreateContexts 中发送 RqLs 上下文**
   ```
   ExtraInfo:
       Chain Element: SMB2_CREATE_REQUEST_LEASE "RqLs"
           Blob Length: 52 (LEASE_V2)
           Data: LEASE_V2
               ├─ LeaseKey (16 字节，客户端生成)
               ├─ LeaseState (4 字节，客户端请求的级别)
               ├─ LeaseFlags (4 字节)
               ├─ LeaseDuration (8 字节)
               └─ (V2 附加字段: ParentLeaseKey, Epoch)
   ```

**服务器如何响应？**

✅ **正确理解（已通过 Wireshark 验证）**：

服务器响应租约也需要**同时做两件事**：

1. **设置 OplockLevel = Lease (0xFF)**
   ```
   Create Response
       Oplock: Lease (0xff)
   ```

2. **在 CreateContexts 中返回 RqLs 上下文**
   ```
   ExtraInfo:
       Chain Element: SMB2_CREATE_REQUEST_LEASE "RqLs"
           Blob Length: 52 (LEASE_V2)
           Data: LEASE_V2
               ├─ LeaseKey (与客户端请求的相同或不同)
               ├─ LeaseState (服务器授予的级别，可能降级)
               ├─ LeaseFlags
               └─ LeaseDuration
   ```

### 核心原则

> **客户端通过 RequestedOplockLevel 字段 + RqLs 上下文来请求租约。**
> 
> **服务器解析客户端的 RqLs，评估后返回 RqLs 响应上下文。**

**RqLs 是双向的：客户端请求 + 服务器响应！**

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

### 场景 1：客户端请求租约，服务器授予（基于真实抓包）

```
┌─────────┐                                    ┌─────────┐
│ Client  │                                    │ Server  │
└────┬────┘                                    └────┬────┘
     │                                              │
     │  CREATE Request                             │
     │  ├─ Name: "file.txt"                        │
     │  ├─ RequestedOplockLevel: Lease (0xFF) ✅   │
     │  └─ CreateContexts:                         │
     │      ├─ MxAc (Maximum Access)               │
     │      ├─ QFid (Query File ID)                │
     │      └─ RqLs (客户端发送) ✅                │
     │          ├─ LeaseKey: {客户端生成的GUID}    │
     │          ├─ LeaseState: RWH (0x07) 请求     │
     │          ├─ LeaseFlags: 0                   │
     │          └─ LeaseDuration: 0                │
     ├─────────────────────────────────────────────>│
     │                                              │
     │                         服务器处理：         │
     │                         • 解析客户端的 RqLs 上下文
     │                         • 提取 LeaseKey (客户端的)
     │                         • 评估 LeaseState    │
     │                         • 通过 LeaseManager 处理
     │                         • 创建 RqLs 响应     │
     │                                              │
     │  CREATE Response                            │
     │  ├─ FileID: {Persistent, Volatile}          │
     │  ├─ OplockLevel: Lease (0xFF)  ✅           │
     │  └─ CreateContexts:                         │
     │      ├─ MxAc (响应)                         │
     │      ├─ QFid (响应)                         │
     │      └─ RqLs (服务器响应) ✅                │
     │          ├─ LeaseKey: {客户端的GUID}        │
     │          ├─ LeaseState: RWH (0x07) 授予     │
     │          ├─ LeaseFlags: 0                   │
     │          └─ LeaseDuration: 0                │
     │<─────────────────────────────────────────────┤
     │                                              │
```

**关键点（基于 Wireshark 抓包）**：
- ✅ 客户端设置 `RequestedOplockLevel = Lease (0xFF)`
- ✅ 客户端**也发送** RqLs 上下文（包含 LeaseKey 等）
- ✅ LeaseKey 由**客户端**生成
- ✅ 服务器解析客户端的 RqLs
- ✅ 服务器评估并返回 RqLs 响应
- ✅ 服务器设置 `OplockLevel = Lease (0xFF)`

### 场景 2：服务器拒绝租约请求

```
┌─────────┐                                    ┌─────────┐
│ Client  │                                    │ Server  │
└────┬────┘                                    └────┬────┘
     │                                              │
     │  CREATE Request                             │
     │  ├─ Name: "file.txt"                        │
     │  ├─ RequestedOplockLevel: Lease (0xFF) ✅   │
     │  └─ CreateContexts: [MxAc, QFid]            │
     ├─────────────────────────────────────────────>│
     │                                              │
     │                         服务器评估：         │
     │                         • 文件被多个客户端打开
     │                         • 不允许授予租约     │
     │                         → 拒绝租约请求 ❌    │
     │                                              │
     │  CREATE Response                            │
     │  ├─ FileID: {Persistent, Volatile}          │
     │  ├─ OplockLevel: None (0x00)  ❌ 拒绝       │
     │  └─ CreateContexts:                         │
     │      ├─ MxAc (响应)                         │
     │      ├─ QFid (响应)                         │
     │      └─ （不包含 RqLs）❌                   │
     │<─────────────────────────────────────────────┤
     │                                              │
```

**关键点**：
- ✅ 客户端通过 `RequestedOplockLevel = Lease` 请求租约
- ❌ 服务器评估后决定拒绝
- ❌ CREATE Response 中**不包含** RqLs 上下文
- ❌ `OplockLevel` 字段设置为 `None` (0x00)
- ✅ 文件仍然成功打开，只是没有租约

## 租约键（LeaseKey）

### 用途

- 唯一标识一个租约
- 允许同一客户端的多个句柄共享租约（SMB 3.0+）
- 用于租约中断通知

### 生成规则（基于 Wireshark 抓包验证）

✅ **SMB 2.0/2.1**: 
- **客户端**生成 LeaseKey（使用 `Guid.NewGuid()` 或类似方法）
- 客户端在 RqLs 上下文中发送 LeaseKey
- 服务器使用客户端提供的 LeaseKey
- 每次 CREATE 请求客户端生成新的租约键

✅ **SMB 3.0+**:
- 支持租约共享（同一客户端的多个句柄可以共享租约）
- 客户端可以在多个请求中使用相同的 LeaseKey
- 通过 ParentLeaseKey 字段支持目录租约

### 抓包数据示例

```
Frame 5814: Client → Server
  CREATE Request
    Oplock: Lease (0xff)
    CreateContexts:
      RqLs:
        LeaseKey: {客户端生成的 GUID}
        LeaseState: RWH (0x07)

Frame 5822: Server → Client
  CREATE Response
    Oplock: Lease (0xff)
    CreateContexts:
      RqLs:
        LeaseKey: {与客户端相同的 GUID}
        LeaseState: RWH (0x07) (服务器授予)
```

## 租约标志（LeaseFlags）

| 标志 | 值 | 说明 |
|------|----|----|
| None | 0x00 | 无标志 |
| `SMB2_LEASE_FLAG_BREAK_IN_PROGRESS` | 0x02 | 租约中断进行中 |
| `SMB2_LEASE_FLAG_PARENT_LEASE_KEY_SET` | 0x04 | 父租约键已设置（V2） |

## 服务器实现建议

### 检测租约请求

基于 Wireshark 抓包分析，服务器需要处理客户端的 RqLs 上下文：

```csharp
private static void ProcessCreateContexts(...)
{
    foreach (var context in request.CreateContexts)
    {
        switch (context.Name)
        {
            case "MxAc":
                ProcessMxAcContext(...);
                break;
                
            case "QFid":
                ProcessQFidContext(...);
                break;
                
            case "RqLs":
                // 客户端发送了 RqLs 上下文（Wireshark 验证）
                ProcessLeaseContext(context, response, ...);
                break;
        }
    }
}
```

### 何时授予租约？

服务器应该在以下情况考虑授予租约：

✅ **适合授予租约**：
- 文件只被单个客户端访问
- 文件不是频繁被其他客户端修改
- 会话支持租约协议（session.SupportsLeasing = true）
- 文件不是关键系统文件
- 策略允许租约

❌ **不适合授予租约**：
- 文件被多个客户端同时打开
- 其他客户端持有冲突的租约
- 文件系统不支持租约通知
- 管理员策略禁用租约

### 租约级别评估

服务器根据文件访问模式评估授予的租约级别：

| FileAccess | 建议授予的 LeaseState | 值 | 说明 |
|-----------|---------------------|-----|------|
| Read + Write | RH (ReadCaching + HandleCaching) | 0x03 | 保守策略，不主动授予写入缓存 |
| Read only | R (ReadCaching) | 0x01 | 只读访问，授予读取缓存 |
| No R/W | None | 0x00 | 不授予租约 |

**注意**：服务器可以授予**低于**客户端期望的租约级别，但不应超过。

### 实现示例（基于抓包验证）

```csharp
// 在 CreateHelper.cs 中处理客户端上下文
private static void ProcessCreateContexts(
    CreateRequest request,
    CreateResponse response,
    ...)
{
    foreach (var context in request.CreateContexts)
    {
        switch (context.Name)
        {
            case "MxAc":
                ProcessMxAcContext(...);
                break;
                
            case "QFid":
                ProcessQFidContext(...);
                break;
                
            case "RqLs":
                // 客户端发送了 RqLs 上下文（包含 LeaseKey 等）
                ProcessLeaseContext(context, response, ...);
                break;
        }
    }
}

// 处理客户端的租约请求
private static void ProcessLeaseContext(
    CreateContext requestContext,  // 客户端的 RqLs 上下文
    CreateResponse response,
    ...)
{
    // 1. 解析客户端的 RqLs 上下文
    LeaseContext leaseRequest = ParseLeaseContextFromData(requestContext);
    
    if (leaseRequest == null)
    {
        return;  // 解析失败
    }
    
    // 2. 提取客户端提供的信息
    Guid clientLeaseKey = leaseRequest.LeaseKey;  // 客户端生成的
    LeaseState requestedState = leaseRequest.LeaseState;  // 客户端请求的级别
    
    // 3. 服务器评估是否授予（可以降级）
    LeaseState grantedState = EvaluateLeaseGrant(requestedState, fileAccess, ...);
    
    if (grantedState == LeaseState.None)
    {
        return;  // 服务器拒绝
    }
    
    // 4. 通过 LeaseManager 创建租约
    var leaseResponse = session.LeaseContextHandler.ProcessCreateContext(
        leaseRequest,  // 包含客户端的 LeaseKey
        sessionID,
        fileID,
        path);
    
    // 5. 添加 RqLs 响应上下文
    if (leaseResponse != null)
    {
        response.CreateContexts.Add(leaseResponse);
        response.OplockLevel = OplockLevel.Lease;
    }
}

// 解析客户端的 RqLs 上下文
private static LeaseContext ParseLeaseContextFromData(CreateContext context)
{
    if (context == null || context.Data == null || context.Data.Length < 32)
    {
        return null;
    }
    
    // 解析 LEASE_V1 (32 字节) 或 LEASE_V2 (52 字节)
    var leaseContext = new LeaseContext();
    int offset = 0;
    leaseContext.LeaseKey = LittleEndianConverter.ToGuid(context.Data, offset);
    offset += 16;
    leaseContext.LeaseState = (LeaseState)LittleEndianConverter.ToUInt32(context.Data, offset);
    offset += 4;
    leaseContext.LeaseFlags = (LeaseFlags)LittleEndianConverter.ToUInt32(context.Data, offset);
    offset += 4;
    leaseContext.LeaseDuration = LittleEndianConverter.ToUInt64(context.Data, offset);
    
    return leaseContext;
}
```

## 常见问题

### Q1: 客户端如何请求租约？

**A（基于 Wireshark 抓包验证）**: 客户端需要**同时做两件事**：
1. 设置 `RequestedOplockLevel = Lease (0xFF)`
2. 在 CreateContexts 中发送 RqLs 上下文（包含 LeaseKey, LeaseState等）

### Q2: RqLs 上下文由谁发送？

**A**: **双向都发送**：
- 客户端在 CREATE Request 中发送 RqLs 上下文（请求）
- 服务器在 CREATE Response 中发送 RqLs 上下文（响应）

### Q3: LeaseKey 由谁生成？

**A（基于 Wireshark 抓包验证）**: **客户端**生成 LeaseKey：
- 客户端使用 `Guid.NewGuid()` 或类似方法生成
- 客户端在 RqLs 请求中发送 LeaseKey
- 服务器使用客户端提供的 LeaseKey（或根据策略修改）

### Q4: 服务器必须授予客户端请求的租约吗？

**A**: 不是。服务器可以：
- 授予租约（返回 RqLs 上下文）
- 拒绝租约（不返回 RqLs 上下文，OplockLevel = None）
- 降级租约（授予低于客户端期望的级别）

### Q5: OplockLevel 和 RqLs 的关系？

**A**: 
- 如果授予了租约，`OplockLevel` 必须设置为 `Lease` (0xFF)
- 同时 `CreateContexts` 中必须包含 RqLs 上下文（服务器生成）
- 两者必须同时存在

### Q6: 租约和 Oplock 的区别？

**A**:
- **Oplock**（Opportunistic Lock）：传统的缓存机制，SMB 1.0+
- **Lease**（租约）：改进的缓存机制，SMB 2.1+
- Lease 提供更细粒度的控制（R/RH/RW/RWH）和更好的性能

### Q7: 如何判断服务器是否支持租约？

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

### 关键要点（基于 Wireshark 抓包验证）

1. ✅ **客户端同时设置 RequestedOplockLevel 和发送 RqLs 上下文**
2. ✅ **客户端生成 LeaseKey** 并在 RqLs 中发送
3. ✅ **服务器解析客户端的 RqLs 上下文**
4. ✅ **服务器评估并返回 RqLs 响应上下文**
5. ✅ **OplockLevel = Lease** 表示授予了租约（请求和响应都需要）
6. ✅ **服务器可以拒绝或降级租约请求**
7. ✅ **RqLs 是双向的：客户端请求 + 服务器响应**

### 实现检查清单

- [ ] 在 `ProcessCreateContexts` 中添加 `case "RqLs"` 处理
- [ ] 实现 `ParseLeaseContextFromData` 解析客户端的 RqLs
- [ ] 提取客户端提供的 `LeaseKey`（不要自己生成）
- [ ] 提取客户端请求的 `LeaseState`
- [ ] 评估租约级别（考虑文件状态、访问模式，可以降级）
- [ ] 通过 `LeaseManager` 注册租约
- [ ] 创建并返回 RqLs 响应上下文
- [ ] 设置 `response.OplockLevel = OplockLevel.Lease`
- [ ] 实现租约中断通知机制
- [ ] 处理租约超时和过期

### 抓包数据来源

本文档基于真实的 Windows 客户端 ↔ Windows 服务器的 Wireshark 抓包数据验证。详见：[rqls-context-wireshark-analysis.md](rqls-context-wireshark-analysis.md)

---

**参考文档**：
- [MS-SMB2] 2.2.13.2.8 - SMB2_CREATE_REQUEST_LEASE
- [MS-SMB2] 2.2.14.2.8 - SMB2_CREATE_RESPONSE_LEASE
- [MS-SMB2] 2.2.23.1 - SMB2_LEASE_BREAK_NOTIFICATION

**最后更新**: 2025-01-10

