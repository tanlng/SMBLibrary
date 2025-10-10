# Wireshark 抓包数据分析：SMB2 租约 RqLs 上下文

## 抓包数据来源

**环境**: Windows 客户端 ↔ Windows 服务器  
**协议**: SMB2/SMB3  
**捕获工具**: Wireshark

## 场景 1: 客户端请求租约（成功）

### Frame 5814 - CREATE Request (客户端 → 服务器)

```
Create Request (0x05)
    StructureSize: 0x0039
    Oplock: Lease (0xff)  ← RequestedOplockLevel = Lease ✅
    ...
    ExtraInfo:
        • SMB2_CREATE_DURABLE_HANDLE_REQUEST_V2 (DH2Q)
        • SMB2_CREATE_QUERY_MAXIMAL_ACCESS_REQUEST (MxAc)
        • SMB2_CREATE_QUERY_ON_DISK_ID (QFid)
        • SMB2_CREATE_REQUEST_LEASE (RqLs) ← 客户端发送了 RqLs！ ✅
    
    Chain Element: SMB2_CREATE_REQUEST_LEASE "RqLs"
        Tag: RqLs
        Blob Length: 52  ← LEASE_V2 (52 字节)
        Data: LEASE_V2
```

### Frame 5822 - CREATE Response (服务器 → 客户端)

```
Create Response (0x05)
    StructureSize: 0x0059
    Oplock: Lease (0xff)  ← OplockLevel = Lease ✅
    ...
    ExtraInfo:
        • SMB2_CREATE_QUERY_MAXIMAL_ACCESS_REQUEST (MxAc)
        • SMB2_CREATE_REQUEST_LEASE (RqLs) ← 服务器返回了 RqLs！ ✅
        • SMB2_CREATE_DURABLE_HANDLE_REQUEST_V2 (DH2Q)
        • SMB2_CREATE_QUERY_ON_DISK_ID (QFid)
    
    Chain Element: SMB2_CREATE_REQUEST_LEASE "RqLs"
        Tag: RqLs
        Blob Length: 52  ← LEASE_V2 (52 字节)
        Data: LEASE_V2
```

## 场景 2: 客户端不请求租约

### Frame 5852 - CREATE Request (无租约)

```
Create Request (0x05)
    Oplock: No oplock (0x00)  ← 没有请求租约 ❌
    ...
    ExtraInfo:
        • SMB2_CREATE_QUERY_MAXIMAL_ACCESS_REQUEST (MxAc)
        （没有 RqLs）
```

### Frame 5857 - CREATE Response (无租约)

```
Create Response (0x05)
    Oplock: No oplock (0x00)  ← 没有授予租约 ❌
    ...
    ExtraInfo:
        • SMB2_CREATE_QUERY_MAXIMAL_ACCESS_REQUEST (MxAc)
        （没有 RqLs）
```

## 场景 3: 另一个带租约的请求

### Frame 5884 - CREATE Request (客户端 → 服务器)

```
Create Request (0x05)
    Oplock: Lease (0xff)  ← RequestedOplockLevel = Lease ✅
    ...
    ExtraInfo:
        • SMB2_CREATE_QUERY_MAXIMAL_ACCESS_REQUEST (MxAc)
        • SMB2_CREATE_REQUEST_LEASE (RqLs) ← 客户端发送了 RqLs！ ✅
    
    Chain Element: SMB2_CREATE_REQUEST_LEASE "RqLs"
        Blob Length: 52  ← LEASE_V2
        Data: LEASE_V2
```

### Frame 5889 - CREATE Response (服务器 → 客户端)

```
Create Response (0x05)
    Oplock: Lease (0xff)  ← OplockLevel = Lease ✅
    ...
    ExtraInfo:
        • SMB2_CREATE_QUERY_MAXIMAL_ACCESS_REQUEST (MxAc)
        • SMB2_CREATE_REQUEST_LEASE (RqLs) ← 服务器返回了 RqLs！ ✅
    
    Chain Element: SMB2_CREATE_REQUEST_LEASE "RqLs"
        Blob Length: 52  ← LEASE_V2
        Data: LEASE_V2
```

## 关键发现

### ✅ 证实的事实

1. **客户端请求租约的方式**：
   - ✅ 设置 `Oplock: Lease (0xff)` (RequestedOplockLevel 字段)
   - ✅ 在 `CreateContexts` 中添加 `RqLs` 上下文
   - ✅ RqLs 包含 52 字节的 LEASE_V2 数据结构

2. **服务器响应租约的方式**：
   - ✅ 设置 `Oplock: Lease (0xff)` (OplockLevel 字段)
   - ✅ 在 `CreateContexts` 中返回 `RqLs` 上下文
   - ✅ RqLs 包含 52 字节的 LEASE_V2 数据结构

3. **双向都有 RqLs**：
   - ✅ 客户端发送 `SMB2_CREATE_REQUEST_LEASE`
   - ✅ 服务器返回 `SMB2_CREATE_REQUEST_LEASE`
   - ⚠️ 注意：名称都是 "SMB2_CREATE_REQUEST_LEASE"，但一个是请求，一个是响应

### ❌ 纠正的误解

**误解 1**: 客户端只设置 RequestedOplockLevel，不发送 RqLs 上下文
- ❌ 错误！客户端**同时做了两件事**

**误解 2**: 服务器生成 LeaseKey
- ❓ 需要进一步查看 RqLs 的详细内容才能确定
- 可能客户端在 RqLs 中发送 LeaseKey

**误解 3**: RqLs 只是服务器的响应
- ❌ 错误！客户端**也发送** RqLs 上下文

## 正确的实现逻辑

### 服务器端处理

```csharp
private static void ProcessCreateContexts(
    CreateRequest request,
    CreateResponse response,
    ...)
{
    // 处理客户端的上下文
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
                
            case "RqLs":  ← 客户端会发送！需要处理！ ✅
                ProcessLeaseContext(context, response, ...);
                break;
        }
    }
}

private static void ProcessLeaseContext(
    CreateContext requestContext,  ← 客户端发送的 RqLs
    CreateResponse response,
    ...)
{
    // 1. 解析客户端的 RqLs 上下文
    LeaseContext leaseRequest = ParseLeaseContextFromData(requestContext);
    
    // 2. 提取客户端提供的信息
    //    - LeaseKey (客户端生成？)
    //    - LeaseState (客户端请求的级别)
    //    - LeaseFlags
    //    - LeaseDuration
    
    // 3. 服务器评估并决定授予的租约
    LeaseState grantedState = EvaluateLeaseGrant(leaseRequest.LeaseState, ...);
    
    // 4. 创建响应
    var leaseResponse = new LeaseContext(
        leaseRequest.LeaseKey,  // 使用客户端的 LeaseKey？
        grantedState,  // 服务器决定的级别
        LeaseFlags.None,
        0
    );
    
    // 5. 添加到响应
    response.CreateContexts.Add(leaseResponse);
    response.OplockLevel = OplockLevel.Lease;
}
```

## 待确认的问题

### ❓ LeaseKey 由谁生成？

抓包数据显示客户端的 RqLs 包含 52 字节数据，但没有显示详细内容。

**需要进一步查看**：
- LEASE_V2 的 52 字节中是否包含 LeaseKey？
- LeaseKey 是客户端生成还是服务器生成？
- 请求和响应的 LeaseKey 是否相同？

### ❓ RequestedOplockLevel 和 RqLs 的关系？

抓包数据显示：
- 客户端**同时设置**了两个
- `RequestedOplockLevel = Lease` + `CreateContexts 中的 RqLs`

**可能的关系**：
1. **必须同时存在**：两个都需要设置
2. **RqLs 包含详细参数**：LeaseKey, LeaseState, LeaseFlags
3. **RequestedOplockLevel 是标志**：表明这是租约而不是 Oplock

## 结论

### 真实的协议行为（基于抓包）

**客户端请求租约**：
```
RequestedOplockLevel = Lease (0xFF)  ✅ 必需
+
CreateContexts 中的 RqLs 上下文（52字节 LEASE_V2）✅ 必需
  ├─ LeaseKey
  ├─ LeaseState (请求的级别)
  ├─ LeaseFlags
  └─ LeaseDuration
```

**服务器响应租约**：
```
OplockLevel = Lease (0xFF)  ✅ 必需
+
CreateContexts 中的 RqLs 上下文（52字节 LEASE_V2）✅ 必需
  ├─ LeaseKey (相同？还是不同？)
  ├─ LeaseState (授予的级别)
  ├─ LeaseFlags
  └─ LeaseDuration
```

### 实现建议

1. ✅ **保留** `ParseLeaseContextFromData` 方法（用于解析客户端的 RqLs）
2. ✅ **保留** `ProcessLeaseContext` 方法（用于处理客户端的租约请求）
3. ✅ 在 `ProcessCreateContexts` 中添加 `case "RqLs"` 处理
4. ❌ **不需要**基于 `RequestedOplockLevel` 主动添加 RqLs（客户端会自己发送）
5. ✅ **可以**同时检查 `RequestedOplockLevel` 和 RqLs 的一致性

### 需要验证

- ❓ LeaseKey 的详细值（客户端生成还是服务器生成）
- ❓ 请求和响应的 LeaseKey 是否相同
- ❓ LeaseState 的详细值（客户端请求 vs 服务器授予）

---

**创建时间**: 2025-01-10  
**数据来源**: Windows 客户端 ↔ Windows 服务器抓包  
**状态**: ✅ 已证实

