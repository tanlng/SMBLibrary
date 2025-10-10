# SMB2 FileID 实现指南（QFid 上下文）

## 快速理解

**FileID** = **Persistent** (8字节) + **Volatile** (8字节)

```csharp
public struct FileID
{
    public ulong Persistent;  // 文件的唯一标识（稳定）
    public ulong Volatile;    // 句柄的唯一标识（会话内唯一）
}
```

## 协议要求

### Persistent 字段

**必须是文件的唯一标识**，在文件重命名时不变，在文件覆盖时改变。

| 场景 | 要求 |
|------|------|
| 文件重命名 | Persistent **不变**（同一个文件） |
| 文件覆盖 | Persistent **改变**（不同文件） |
| 文件删除后重建 | Persistent **改变**（不同文件） |
| 同一文件多次打开 | Persistent **相同**（稳定性） |

### Volatile 字段

| 要求 | 说明 |
|------|------|
| **会话级唯一** | 同一 Session 内所有打开文件的 Volatile 不能重复 |
| **禁用值** | 不能是 `0xFFFFFFFFFFFFFFFF` |
| **查找键** | 服务器用它查找 OpenFileObject |

## 当前实现的问题

```csharp
// ❌ 错误：使用缓存，5分钟后过期
fileID.Persistent = CacheHelper.TryGet<ulong>($"fileID_{shareName}/{relativePath}", 
    () => volatileFileID.Value, 5);
```

**问题**：
1. ❌ 缓存过期后，同一文件的 Persistent 会改变
2. ❌ 基于**路径**而不是**文件实体**
3. ❌ 文件重命名后，Persistent 会改变（错误）
4. ❌ 文件覆盖后，Persistent 不会改变（错误）

## 正确实现：使用文件系统唯一 ID

### 实现代码

```csharp
public FileID? AddOpenFile(uint treeID, string shareName, string relativePath, 
                          object handle, FileAccess fileAccess)
{
    ulong? volatileFileID = AllocateVolatileFileID();
    if (!volatileFileID.HasValue)
        return null;
    
    FileID fileID = new FileID();
    fileID.Volatile = volatileFileID.Value;
    
    // ✅ 使用文件系统的唯一 ID
    fileID.Persistent = GetFileUniqueID(handle);
    
    m_openFiles.Add(volatileFileID.Value, new OpenFileObject(...));
    return fileID;
}

private ulong GetFileUniqueID(object handle)
{
    // 方式 1: Windows - 使用 File Index (推荐)
    if (handle is IntPtr fileHandle)
    {
        return GetWindowsFileIndex(fileHandle);
    }
    
    // 方式 2: .NET Stream - 生成稳定 ID
    if (handle is FileHandle fileHandleObj)
    {
        return GenerateStableFileID(fileHandleObj);
    }
    
    // Fallback: 使用 Volatile 作为 Persistent
    return fileID.Volatile;
}
```

### Windows 实现（推荐）

```csharp
// NTDirectoryFileSystem.cs
private ulong GetWindowsFileIndex(IntPtr fileHandle)
{
    IO_STATUS_BLOCK ioStatusBlock;
    FILE_INTERNAL_INFORMATION internalInfo = new FILE_INTERNAL_INFORMATION();
    
    NTStatus status = NtQueryInformationFile(
        fileHandle, 
        out ioStatusBlock, 
        ref internalInfo, 
        (uint)Marshal.SizeOf(internalInfo), 
        (uint)FileInformationClass.FileInternalInformation);
    
    if (status == NTStatus.STATUS_SUCCESS)
    {
        // File Index (类似 Unix inode)
        return (ulong)internalInfo.IndexNumber;
    }
    
    // Fallback
    return 0;
}

[StructLayout(LayoutKind.Sequential)]
struct FILE_INTERNAL_INFORMATION
{
    public long IndexNumber;  // 文件的唯一索引
}
```

**特性**：
- ✅ 文件重命名：IndexNumber **不变**
- ✅ 文件覆盖：IndexNumber **改变**（新文件）
- ✅ 同一文件：总是返回**相同**的 IndexNumber
- ✅ 完全符合 Persistent 的语义

### .NET Stream 实现（次优）

对于基于 IFileSystem 的实现，无法获取文件系统 ID 时：

```csharp
// NTFileSystemAdapter.cs
private ulong GenerateStableFileID(FileHandle fileHandle)
{
    FileSystemEntry entry = m_fileSystem.GetEntry(fileHandle.Path);
    if (entry != null)
    {
        // ✅ 只使用创建时间 + 路径（不包含大小）
        string uniqueKey = $"{entry.CreationTime.Ticks}_{fileHandle.Path.ToLowerInvariant()}";
        
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(uniqueKey));
            ulong id = BitConverter.ToUInt64(hash, 0);
            
            if (id == 0 || id == 0xFFFFFFFFFFFFFFFF)
            {
                id = BitConverter.ToUInt64(hash, 8);
            }
            
            return id;
        }
    }
    
    return 0;
}
```

**特性**：
- ⚠️ 文件重命名：ID 会改变（因为包含路径）
- ✅ 文件覆盖：ID 会改变（创建时间不同）
- ✅ 文件追加：ID **不会**改变（不包含大小）

**注意**：这个方案在文件重命名时 ID 会改变，是一个已知限制。

## 案例分析

### 案例 1：文件重命名 (A → B)

**使用 Windows File Index（推荐）**：
```
1. 打开文件 A
   → FileID(Persistent=12345, Volatile=100)
   
2. 将 A 重命名为 B
   → 文件句柄仍然有效
   → FileID 不变: (Persistent=12345, Volatile=100)
   
3. 关闭文件

4. 重新打开文件 B
   → FileID(Persistent=12345, Volatile=101)
   → Persistent 相同（同一个文件）✅
   → Volatile 不同（新句柄）
```

**使用路径哈希（不推荐）**：
```
1. 打开文件 A
   → FileID(Persistent=Hash("A"), Volatile=100)
   
2. 将 A 重命名为 B
   
3. 重新打开文件 B
   → FileID(Persistent=Hash("B"), Volatile=101)
   → Persistent 改变 ❌（错误，应该不变）
```

### 案例 2：文件覆盖（新文件替换 A）

**使用 Windows File Index**：
```
1. 打开文件 A (旧内容)
   → FileID(Persistent=12345, Volatile=100)
   
2. 上传新文件覆盖 A
   → 文件系统创建新文件，File Index = 67890
   → 已打开的句柄仍指向旧文件（Persistent=12345）
   
3. 关闭旧句柄

4. 重新打开文件 A (新内容)
   → FileID(Persistent=67890, Volatile=101)
   → Persistent 改变 ✅（正确，新文件）
```

**结论**：
- ✅ 文件覆盖后，Persistent **应该改变**（因为是不同的文件）
- ✅ Windows File Index 会自动处理（新文件 = 新 Index）

### 案例 3：文件追加（Append）

**使用 Windows File Index**：
```
1. 打开文件 A (大小 100 KB)
   → FileID(Persistent=12345, Volatile=100)
   
2. 追加内容到文件 A (大小变为 200 KB)
   → File Index 不变 = 12345
   → 已打开的句柄仍有效
   
3. 关闭文件

4. 重新打开文件 A
   → FileID(Persistent=12345, Volatile=101)
   → Persistent 不变 ✅（正确，同一个文件）
```

**使用创建时间+大小+路径哈希**：
```
1. 打开文件 A (大小 100 KB)
   → FileID(Persistent=Hash("2025-01-01_100KB_A"), Volatile=100)
   
2. 追加内容到文件 A (大小变为 200 KB)
   
3. 重新打开文件 A
   → FileID(Persistent=Hash("2025-01-01_200KB_A"), Volatile=101)
   → Persistent 改变 ❌（错误，仍是同一个文件）
```

**结论**：
- ✅ 文件追加后，Persistent **不应该改变**（同一个文件）
- ✅ Windows File Index 正确（不会改变）
- ❌ 包含文件大小的哈希不正确（会改变）

## QFid 上下文

QFid (Query File ID) 使用 Persistent 字段：

```csharp
public static void AddQFidContext(FileID fileID, CreateResponse response)
{
    // QFid 使用 Persistent 作为文件唯一标识
    byte[] opaqueFileId = ConvertTo32Bytes(fileID.Persistent);
    
    response.CreateContexts.Add(new CreateContext
    {
        Name = "QFid",
        Data = opaqueFileId
    });
}
```

**要求**：Persistent 必须稳定，同一个文件应该返回相同的值。

## LeaseManager 的依赖（重要）

**关键发现**：LeaseManager 使用 **FileID 作为字典键**来跟踪文件租约：

```csharp
// SMBLibrary/Server/Leasing/LeaseManager.cs
private readonly ConcurrentDictionary<FileID, List<Guid>> m_fileLeases;
```

### 问题

FileID 是 struct，包含 **Persistent + Volatile**。作为字典键时会比较两者：

```
FileID(Persistent=1, Volatile=100) ≠ FileID(Persistent=1, Volatile=101)
```

**导致的问题**：
```
文件 A 被打开两次：
  句柄 1: FileID(Persistent=1, Volatile=100)
  句柄 2: FileID(Persistent=1, Volatile=101)

LeaseManager 认为: 两个不同的文件 ❌
实际情况: 同一个文件的两个句柄 ✅
```

### 解决方案

**Persistent 必须基于文件实体**（不是路径）：

- ✅ 使用 File Index / inode → 重命名时不变，覆盖时改变
- ❌ 使用路径哈希 → 重命名时改变（租约失效 ❌）
- ❌ 使用缓存 → 不稳定（租约混乱 ❌）

## 实现对比

| 实现方式 | Persistent 值 | 重命名 | 覆盖 | 追加 | 推荐度 |
|---------|--------------|--------|------|------|--------|
| **Windows File Index** | 文件索引号 | 不变 ✅ | 改变 ✅ | 不变 ✅ | ⭐⭐⭐⭐⭐ |
| **.NET (创建时间+路径)** | 哈希值 | 改变 ❌ | 改变 ✅ | 不变 ✅ | ⭐⭐⭐ |
| **创建时间+大小+路径** | 哈希值 | 改变 ❌ | 改变 ✅ | 改变 ❌ | ⭐ |
| **路径哈希** | Hash(路径) | 改变 ❌ | 不变 ❌ | 不变 ⚠️ | ❌ |
| **缓存（当前）** | 过期会变 | 不可预测 ❌ | 不可预测 ❌ | 不可预测 ❌ | ❌ |

### 语义说明

| 操作 | 文件实体 | Persistent 应该 |
|------|---------|----------------|
| **重命名** | 同一个文件 | **不变** |
| **覆盖** | 不同文件 | **改变** |
| **追加** | 同一个文件 | **不变** |
| **修改** | 同一个文件 | **不变** |

## 最终推荐

**唯一正确的实现**：

```csharp
// 使用文件系统的唯一 ID
fileID.Persistent = GetFileSystemUniqueID(handle);
```

### Windows 平台（NTDirectoryFileSystem）

```csharp
fileID.Persistent = GetWindowsFileIndex(fileHandle);  // ✅ 推荐
```

### 跨平台的困境（NTFileSystemAdapter）

由于 IFileSystem 接口**没有提供文件唯一 ID**，只能：

1. **扩展 IFileSystem 接口**，添加 `GetFileUniqueID()` 方法
2. **或者**直接使用 Volatile 作为 Persistent（简化但不完美）

```csharp
// 临时方案：使用 Volatile
fileID.Persistent = volatileFileID.Value;
```

**注意**：这会导致同一文件的不同句柄有不同的 Persistent，影响 LeaseManager 的文件跟踪。

## 总结

### Persistent 的关键要求

1. **必须基于文件实体**（不能基于路径）
2. **必须稳定**（重命名、追加、修改时不变）
3. **必须改变**（文件覆盖时）
4. **LeaseManager 依赖它**来跟踪文件租约

### 最佳实践

```csharp
// ✅ 推荐：使用文件系统唯一 ID
fileID.Persistent = GetFileIndex(handle);  // Windows: File Index, Unix: inode

// ❌ 不要使用路径哈希（重命名时会改变）
// ❌ 不要使用缓存（会过期）
// ❌ 不要包含文件大小（追加时会改变）
```

### 实现检查清单

- [ ] Persistent 基于文件实体（不是路径）
- [ ] 文件重命名后 Persistent 不变
- [ ] 文件覆盖后 Persistent 改变
- [ ] 文件追加后 Persistent 不变
- [ ] LeaseManager 能正确跟踪文件

---

**最后更新**: 2025-01-09
