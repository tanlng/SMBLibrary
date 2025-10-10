# LeaseKey 详细分析（基于 JSON 抓包数据）

## 概述

基于 Windows 客户端 ↔ Windows 服务器的详细 JSON 抓包数据分析，验证 LeaseKey 的生成和使用机制。

## 关键发现

### ✅ LeaseKey 由客户端生成

从抓包数据中可以看到，每个租约都有唯一的 LeaseKey：

**示例 1**:
```json
"LEASE_V2": {
  "smb2.lease.lease_key": "8cba351e-a690-49aa-f8ea-483784989172",
  "smb2.lease.lease_state": "0x00000007",  // RWH (ReadCaching | WriteCaching | HandleCaching)
  "smb2.lease.lease_flags": "0x00000004",  // parent_lease_key_set
  "smb2.lease.lease_duration": "0x0000000000000000",
  "smb2.lease.parent_lease_key": "d02db798-2e30-a6e1-cd9b-e5fd56aa7a16"
}
```

**示例 2**:
```json
"LEASE_V2": {
  "smb2.lease.lease_key": "35885fe2-aad3-8951-86c3-9c11b75cb24a",
  "smb2.lease.lease_state": "0x00000007",  // RWH
  "smb2.lease.lease_flags": "0x00000000",
  "smb2.lease.lease_duration": "0x0000000000000000",
  "smb2.lease.parent_lease_key": "00000000-0000-0000-0000-000000000000"
}
```

**示例 3**:
```json
"LEASE_V2": {
  "smb2.lease.lease_key": "6bda087f-0724-bc1b-4f8b-4d73e55c4703",
  "smb2.lease.lease_state": "0x00000007",  // RWH
  "smb2.lease.lease_flags": "0x00000004",  // parent_lease_key_set
  "smb2.lease.parent_lease_key": "35885fe2-aad3-8951-86c3-9c11b75cb24a"
}
```

## 租约层次结构

从抓包数据可以看到 **租约层次**（Lease Hierarchy）：

### 层次关系

```
顶级租约: 35885fe2-aad3-8951-86c3-9c11b75cb24a
├── parent_lease_key: 00000000-0000-0000-0000-000000000000 (无父租约)
└── lease_flags: 0x00000000

子租约 1: 6bda087f-0724-bc1b-4f8b-4d73e55c4703
├── parent_lease_key: 35885fe2-aad3-8951-86c3-9c11b75cb24a (指向顶级租约)
├── lease_flags: 0x00000004 (parent_lease_key_set = 1)
└── 用于子目录/子文件

子租约 2: d02db798-2e30-a6e1-cd9b-e5fd56aa7a16
├── parent_lease_key: 6bda087f-0724-bc1b-4f8b-4d73e55c4703 (指向子租约 1)
├── lease_flags: 0x00000004 (parent_lease_key_set = 1)
└── 用于更深层的子文件

子租约 3: 8cba351e-a690-49aa-f8ea-483784989172
├── parent_lease_key: d02db798-2e30-a6e1-cd9b-e5fd56aa7a16 (指向子租约 2)
├── lease_flags: 0x00000004 (parent_lease_key_set = 1)
└── 用于更深层的子文件
```

### 租约层次的用途

1. **目录租约**: 顶级租约通常用于目录
2. **文件租约**: 子租约用于目录中的文件
3. **租约中断传播**: 当父租约中断时，所有子租约也会收到影响

## LeaseState 分析

### LeaseState 值

| 值 | 含义 | 二进制 | 能力 |
|----|------|--------|------|
| `0x00000007` | RWH | `111` | ReadCaching + WriteCaching + HandleCaching |
| `0x00000003` | RH | `011` | ReadCaching + HandleCaching |
| `0x00000001` | R | `001` | ReadCaching only |
| `0x00000000` | None | `000` | 无租约 |

### 租约降级示例

从抓包数据可以看到服务器降级租约的例子：

**客户端请求**:
```json
{
  "smb2.lease.lease_key": "35885fe2-aad3-8951-86c3-9c11b75cb24a",
  "smb2.lease.lease_state": "0x00000007"  // 请求 RWH
}
```

**服务器响应（可能降级）**:
```json
{
  "smb2.lease.lease_key": "35885fe2-aad3-8951-86c3-9c11b75cb24a",  // 相同的 LeaseKey
  "smb2.lease.lease_state": "0x00000003",  // 降级为 RH (没有写缓存)
  "smb2.lease.lease_oplock": "0x00000001"  // Epoch 增加
}
```

## LeaseFlags 分析

### Flags 值

| Flag | 值 | 含义 |
|------|----|----|
| `break_ack_required` | 0x00000001 | 需要租约中断确认 |
| `break_in_progress` | 0x00000002 | 租约中断进行中 |
| `parent_lease_key_set` | 0x00000004 | 已设置父租约键 |

### 示例

```json
{
  "smb2.lease.lease_flags": "0x00000004",
  "smb2.lease.lease_flags_tree": {
    "smb2.lease.lease_state.break_ack_required": "0",
    "smb2.lease.lease_state.break_in_progress": "0",
    "smb2.lease.lease_state.parent_lease_key_set": "1"  // ← 表示有父租约
  }
}
```

## 租约中断（Lease Break）

### 租约中断通知

从抓包数据中看到的租约中断：

```json
"Lease Break Notification (0x12)": {
  "smb2.lease.lease_oplock": "0x00000004",  // Epoch
  "smb2.lease.lease_flags": "0x00000001",  // break_ack_required
  "smb2.lease.lease_key": "d02db798-2e30-a6e1-cd9b-e5fd56aa7a16",
  "smb2.lease.lease_state": "0x00000003",  // 当前状态 (RH)
  "smb2.lease.lease_state": "0x00000000",  // 新状态 (None) - 完全中断
  "smb2.lease.lease_break_reason": "0x00000000"
}
```

### 租约中断确认

客户端的确认：

```json
"Lease Break Acknowledgment (0x12)": {
  "smb2.lease.lease_flags": "0x00000000",
  "smb2.lease.lease_key": "d02db798-2e30-a6e1-cd9b-e5fd56aa7a16",  // 相同的 LeaseKey
  "smb2.lease.lease_state": "0x00000000"  // 确认降级到 None
}
```

## lease_oplock 字段（Epoch）

### Epoch 的作用

`lease_oplock` 字段实际上是租约的 **Epoch**（版本号）：

| Epoch 值 | 含义 |
|----------|------|
| `0x00000000` | 初始租约（CREATE Request） |
| `0x00000001` | 第一次更新/降级 |
| `0x00000002` | 第二次更新/降级 |
| `0x00000003` | 第三次更新/降级 |
| `0x00000004` | 第四次更新/降级（租约中断） |

### 示例

```json
// 初始 CREATE Request
{
  "smb2.lease.lease_oplock": "0x00000000"
}

// 第一次降级后的 Response
{
  "smb2.lease.lease_oplock": "0x00000001"
}

// 第二次降级
{
  "smb2.lease.lease_oplock": "0x00000002"
}

// 租约中断通知
{
  "smb2.lease.lease_oplock": "0x00000004"
}
```

## 关键结论

### 1. LeaseKey 由客户端生成

✅ **确认**: 客户端生成唯一的 GUID 作为 LeaseKey  
✅ **确认**: 客户端在 RqLs CREATE Request 中发送 LeaseKey  
✅ **确认**: 服务器使用客户端提供的 LeaseKey（不修改）

### 2. LeaseKey 在请求和响应中保持一致

✅ **确认**: CREATE Request 和 CREATE Response 中的 LeaseKey 相同  
✅ **确认**: Lease Break Notification 和 Acknowledgment 中的 LeaseKey 相同

### 3. 租约层次结构

✅ **确认**: SMB3 支持租约层次（通过 parent_lease_key）  
✅ **确认**: 目录和子文件可以形成租约树  
✅ **确认**: parent_lease_key_set flag 指示是否有父租约

### 4. 服务器可以降级租约

✅ **确认**: 服务器可以授予低于请求的 LeaseState  
✅ **确认**: RWH → RH → R → None 的降级路径  
✅ **确认**: Epoch（lease_oplock）跟踪租约版本

## SMBLibrary 实现建议

### 解析客户端的 LeaseKey

```csharp
private static LeaseContext ParseLeaseContextFromData(CreateContext context)
{
    if (context == null || context.Data == null || context.Data.Length < 32)
    {
        return null;
    }
    
    var leaseContext = new LeaseContext();
    int offset = 0;
    
    // 1. LeaseKey (16 字节) - 客户端生成
    leaseContext.LeaseKey = LittleEndianConverter.ToGuid(context.Data, offset);
    offset += 16;
    
    // 2. LeaseState (4 字节) - 客户端请求的级别
    leaseContext.LeaseState = (LeaseState)LittleEndianConverter.ToUInt32(context.Data, offset);
    offset += 4;
    
    // 3. LeaseFlags (4 字节)
    leaseContext.LeaseFlags = (LeaseFlags)LittleEndianConverter.ToUInt32(context.Data, offset);
    offset += 4;
    
    // 4. LeaseDuration (8 字节)
    leaseContext.LeaseDuration = LittleEndianConverter.ToUInt64(context.Data, offset);
    offset += 8;
    
    // 5. LEASE_V2: ParentLeaseKey (16 字节)
    if (context.Data.Length >= 52)
    {
        leaseContext.ParentLeaseKey = LittleEndianConverter.ToGuid(context.Data, offset);
        offset += 16;
        
        // 6. LEASE_V2: Epoch (2 字节)
        leaseContext.Epoch = LittleEndianConverter.ToUInt16(context.Data, offset);
        offset += 2;
        
        // 7. LEASE_V2: Reserved (2 字节)
        // offset += 2;
    }
    
    return leaseContext;
}
```

### 使用客户端的 LeaseKey

```csharp
private static void ProcessLeaseContext(
    CreateContext requestContext,
    CreateResponse response,
    ...)
{
    // 解析客户端的租约请求
    LeaseContext leaseRequest = ParseLeaseContextFromData(requestContext);
    
    // 使用客户端提供的 LeaseKey（不要重新生成）
    Guid clientLeaseKey = leaseRequest.LeaseKey;
    
    // 服务器评估并可能降级
    LeaseState grantedState = EvaluateLeaseGrant(
        leaseRequest.LeaseState,  // 客户端请求的
        fileAccess,
        ...);
    
    // 创建响应（使用客户端的 LeaseKey）
    var leaseResponse = new LeaseContext(
        clientLeaseKey,  // ← 使用客户端的 LeaseKey
        grantedState,    // ← 服务器授予的 State（可降级）
        LeaseFlags.None,
        0);
    
    response.CreateContexts.Add(leaseResponse);
    response.OplockLevel = OplockLevel.Lease;
}
```

## 参考数据

**抓包数据来源**: `docs/protocols/smb2/leasing/windows.json`  
**抓包环境**: Windows 客户端 ↔ Windows 服务器  
**协议版本**: SMB 3.x (支持 LEASE_V2)

---

**创建时间**: 2025-01-10  
**基于**: 真实 Wireshark JSON 抓包数据

