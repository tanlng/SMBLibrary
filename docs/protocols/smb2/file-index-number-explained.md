# File Index Number (IndexNumber) 详解

## 什么是 IndexNumber？

**IndexNumber**（文件索引号）是文件系统为每个文件分配的**唯一标识符**，类似于 Unix/Linux 系统中的 **inode**。

## 核心特性

### 1. 唯一性

每个文件在文件系统中都有一个唯一的 IndexNumber：

```
C:\Users\file.txt        → IndexNumber = 12345
C:\Temp\another.txt      → IndexNumber = 67890
```

**关键点**：
- ✅ 在**整个卷（Volume）**内唯一
- ✅ 即使文件路径不同，IndexNumber 也不会重复
- ✅ 可以作为文件的"身份证号"

### 2. 稳定性（最重要的特性）

IndexNumber 在以下操作中**保持不变**：

| 操作 | IndexNumber | 说明 |
|------|------------|------|
| **文件重命名** | **不变** ✅ | 同一个文件，只是名字变了 |
| **文件移动** | **不变** ✅ | 同一个文件，只是位置变了 |
| **文件内容修改** | **不变** ✅ | 同一个文件，内容改变 |
| **文件追加** | **不变** ✅ | 同一个文件，大小增加 |
| **文件属性修改** | **不变** ✅ | 同一个文件，属性改变 |

IndexNumber 在以下操作中**会改变**：

| 操作 | IndexNumber | 说明 |
|------|------------|------|
| **删除后重建** | **改变** ✅ | 新文件，新 IndexNumber |
| **覆盖文件** | **改变** ✅ | 旧文件删除，新文件创建 |
| **复制文件** | **改变** ✅ | 新文件，不同 IndexNumber |

## 实际案例

### 案例 1：文件重命名

```
1. 创建文件 A.txt
   → IndexNumber = 12345
   
2. 写入内容 "Hello World"
   → IndexNumber = 12345 (不变)
   
3. 重命名 A.txt → B.txt
   → IndexNumber = 12345 (不变)
   
4. 移动 B.txt 到另一个文件夹
   → IndexNumber = 12345 (仍然不变)
```

**结论**：IndexNumber 标识**文件实体**，不是路径。

### 案例 2：文件覆盖

```
1. 创建文件 A.txt
   → IndexNumber = 12345
   
2. 用户上传新文件覆盖 A.txt
   过程：
   - 删除旧的 A.txt (IndexNumber = 12345)
   - 创建新的 A.txt (IndexNumber = 67890)
   
3. 新的 A.txt
   → IndexNumber = 67890 (改变了)
```

**结论**：覆盖 = 删除 + 创建，是不同的文件。

### 案例 3：硬链接（Hard Link）

```
1. 创建文件 original.txt
   → IndexNumber = 12345
   
2. 创建硬链接 link.txt → original.txt
   → IndexNumber = 12345 (相同！)
   
两个文件名指向同一个文件实体
```

**结论**：硬链接共享相同的 IndexNumber。

## 技术细节

### Windows 实现

在 Windows (NTFS) 中，IndexNumber 实际上是 **File Reference Number** 的一部分：

```
File Reference Number (8 bytes)
├── IndexNumber (6 bytes) ← 文件索引
└── Sequence Number (2 bytes) ← 重用检测
```

**获取方式**：

```csharp
// 使用 NtQueryInformationFile
FILE_INTERNAL_INFORMATION internalInfo;
NtQueryInformationFile(
    fileHandle,
    out ioStatusBlock,
    ref internalInfo,
    sizeof(FILE_INTERNAL_INFORMATION),
    FileInformationClass.FileInternalInformation);

// internalInfo.IndexNumber 就是 IndexNumber
ulong indexNumber = (ulong)internalInfo.IndexNumber;
```

**或者通过 SMBLibrary 接口**：

```csharp
FileInformation fileInfo;
fileStore.GetFileInformation(
    out fileInfo, 
    handle, 
    FileInformationClass.FileInternalInformation);

FileInternalInformation internalInfo = (FileInternalInformation)fileInfo;
long indexNumber = internalInfo.IndexNumber;
```

### Unix/Linux 对比

| 概念 | Windows | Unix/Linux |
|------|---------|-----------|
| 名称 | IndexNumber / File Index | inode (index node) |
| 大小 | 64 位 | 32 位或 64 位（取决于文件系统） |
| 唯一范围 | Volume（卷） | File System（文件系统） |
| 获取方式 | NtQueryInformationFile | stat().st_ino |
| 稳定性 | 重命名不变 | 重命名不变 |

### 其他文件系统

| 文件系统 | 是否有类似概念 |
|---------|--------------|
| **NTFS** (Windows) | ✅ File Index / MFT Entry |
| **ext4** (Linux) | ✅ inode number |
| **HFS+** (macOS) | ✅ CNID (Catalog Node ID) |
| **APFS** (macOS) | ✅ File ID |
| **FAT32** | ❌ 没有（基于路径） |
| **exFAT** | ❌ 没有 |
| **内存文件系统** | ⚠️ 可能没有 |

## 为什么 SMB FileID.Persistent 需要 IndexNumber？

### 1. 文件重命名场景

```
客户端打开文件：
  → FileID(Persistent=12345, Volatile=100)

服务器端文件被重命名：
  A.txt → B.txt

客户端继续操作：
  → 使用 FileID(Persistent=12345, Volatile=100)
  → 服务器通过 IndexNumber=12345 找到文件
  → 即使路径变了，仍然能找到正确的文件 ✅
```

### 2. LeaseManager 文件跟踪

```csharp
// LeaseManager 使用 FileID 作为字典键
private ConcurrentDictionary<FileID, List<Guid>> m_fileLeases;

// 同一个文件的多个句柄
FileID(Persistent=12345, Volatile=100)  ← 句柄 1
FileID(Persistent=12345, Volatile=101)  ← 句柄 2

// 如果 Persistent 相同，租约管理器知道是同一个文件
```

**如果不使用 IndexNumber**（例如用路径哈希）：

```
文件 A.txt 打开两次：
  句柄 1: Persistent = Hash("A.txt") = AAA
  句柄 2: Persistent = Hash("A.txt") = AAA  ✅ 相同

文件重命名 A.txt → B.txt
  重新打开: Persistent = Hash("B.txt") = BBB  ❌ 不同了！

租约管理器认为是不同的文件 ❌
```

### 3. QFid (Query File ID) 上下文

QFid 上下文返回**文件的唯一标识**：

```csharp
// QFid 使用 Persistent 作为 Opaque File ID
byte[] opaqueFileId = ConvertTo32Bytes(fileID.Persistent);

// 客户端可以用这个 ID 来：
// 1. 检测文件是否被替换
// 2. 跨网络断开识别文件
// 3. 缓存文件元数据
```

## IndexNumber 的技术实现

### NTFS 内部结构

NTFS 使用 **MFT (Master File Table)** 存储文件元数据：

```
MFT (Master File Table)
├── Entry 0: $MFT (MFT 本身)
├── Entry 1: $MFTMirr (MFT 镜像)
├── Entry 5: . (根目录)
├── ...
├── Entry 12345: 你的文件 A.txt ← IndexNumber = 12345
├── Entry 12346: 你的文件 B.txt ← IndexNumber = 12346
└── ...

每个 MFT Entry 包含：
- 文件名
- 文件大小
- 时间戳
- 数据位置
- 安全描述符
- 等等...
```

**IndexNumber 就是 MFT Entry 的索引**。

### 获取 IndexNumber 的方法

#### 方法 1：Windows API

```csharp
// 使用 GetFileInformationByHandle
BY_HANDLE_FILE_INFORMATION fileInfo;
GetFileInformationByHandle(hFile, out fileInfo);

// IndexNumber = (nFileIndexHigh << 32) | nFileIndexLow
ulong indexNumber = ((ulong)fileInfo.nFileIndexHigh << 32) | fileInfo.nFileIndexLow;
```

#### 方法 2：Native API (SMBLibrary 使用的)

```csharp
// 使用 NtQueryInformationFile
FILE_INTERNAL_INFORMATION internalInfo;
NtQueryInformationFile(
    fileHandle,
    out ioStatusBlock,
    ref internalInfo,
    sizeof(FILE_INTERNAL_INFORMATION),
    FileInformationClass.FileInternalInformation);

ulong indexNumber = (ulong)internalInfo.IndexNumber;
```

#### 方法 3：SMBLibrary 统一接口（最佳）

```csharp
// 使用 INTFileStore.GetFileInformation
FileInformation fileInfo;
fileStore.GetFileInformation(
    out fileInfo, 
    handle, 
    FileInformationClass.FileInternalInformation);

FileInternalInformation internalInfo = (FileInternalInformation)fileInfo;
long indexNumber = internalInfo.IndexNumber;
```

## 使用场景

### 1. 文件去重

```csharp
// 检测是否是同一个文件（即使路径不同）
if (file1.IndexNumber == file2.IndexNumber)
{
    Console.WriteLine("这是同一个文件（可能是硬链接）");
}
```

### 2. 文件变更检测

```csharp
// 保存文件的 IndexNumber
ulong savedIndexNumber = GetIndexNumber(filePath);

// 稍后检查文件是否被替换
ulong currentIndexNumber = GetIndexNumber(filePath);

if (savedIndexNumber != currentIndexNumber)
{
    Console.WriteLine("文件已被替换（删除后重建）");
}
else
{
    Console.WriteLine("仍是原来的文件（可能被修改或重命名）");
}
```

### 3. SMB 持久化句柄

```csharp
// FileID.Persistent 使用 IndexNumber
FileID fileID = new FileID
{
    Persistent = indexNumber,  // 文件实体标识
    Volatile = sessionUniqueID  // 句柄标识
};
```

## 常见问题

### Q1: IndexNumber 会用完吗？

A: 理论上会，但实际不会：
- NTFS 的 MFT 可以增长
- 64 位 IndexNumber 有 2^64 个可能值
- 即使每秒创建 100 万个文件，也需要 584,942 年才能用完

### Q2: IndexNumber 会被重用吗？

A: NTFS 会尽量避免重用，但在以下情况可能重用：
- 卷被格式化
- MFT 损坏后修复
- 使用特殊工具强制重用

**Sequence Number** 用于检测重用：
```
File Reference = IndexNumber (6 bytes) + Sequence (2 bytes)
```

### Q3: 不同卷的文件会有相同的 IndexNumber 吗？

A: **会！** IndexNumber 只在卷内唯一。

```
C:\file.txt → IndexNumber = 12345
D:\file.txt → IndexNumber = 12345 (可能相同)
```

因此在跨卷场景中，需要同时记录卷信息。

### Q4: FAT32/exFAT 有 IndexNumber 吗？

A: **没有**。这些文件系统不支持 FileInternalInformation。

SMBLibrary 的处理：
```csharp
NTStatus status = fileStore.GetFileInformation(
    out fileInfo, 
    handle, 
    FileInformationClass.FileInternalInformation);

if (status == NTStatus.STATUS_NOT_SUPPORTED)
{
    // FAT32/exFAT 等不支持，fallback 到 Volatile
    return volatileFileID;
}
```

### Q5: 目录也有 IndexNumber 吗？

A: **有！** 目录也是文件系统中的实体，也有 IndexNumber。

```
C:\Users\          → IndexNumber = 5
C:\Users\Alice\    → IndexNumber = 678
C:\Users\Alice\Documents\file.txt → IndexNumber = 12345
```

## 与 inode 的对比

### Unix/Linux inode

```c
struct stat {
    ino_t st_ino;      // inode number（类似 IndexNumber）
    mode_t st_mode;    // 文件类型和权限
    nlink_t st_nlink;  // 硬链接数
    uid_t st_uid;      // 所有者 UID
    gid_t st_gid;      // 所有者 GID
    off_t st_size;     // 文件大小
    // ...
};

// 获取 inode
struct stat st;
stat("/path/to/file", &st);
ino_t inode = st.st_ino;
```

### 相似点

| 特性 | Windows IndexNumber | Unix inode |
|------|-------------------|-----------|
| 唯一性 | Volume 内唯一 | File System 内唯一 |
| 重命名 | 不变 | 不变 |
| 移动 | 不变 | 不变（同文件系统） |
| 覆盖 | 改变 | 改变 |
| 硬链接 | 相同 | 相同 |
| 删除 | 当引用计数=0时回收 | 当链接数=0时回收 |

### 差异点

| 特性 | Windows | Unix |
|------|---------|------|
| **移动到其他卷** | 复制+删除，IndexNumber 改变 | 复制+删除，inode 改变 |
| **移动到同卷** | IndexNumber 不变 | inode 不变 |
| **软链接** | IndexNumber 不同 | inode 不同 |
| **查询方式** | NtQueryInformationFile | stat() |

## 在 SMBLibrary 中的应用

### FileID.Persistent 的正确实现

```csharp
private ulong GetPersistentFileID(ISMBShare share, object handle, ulong volatileFileID)
{
    // 查询 FileInternalInformation
    FileInformation fileInfo;
    NTStatus status = share.FileStore.GetFileInformation(
        out fileInfo, 
        handle, 
        FileInformationClass.FileInternalInformation);
    
    if (status == NTStatus.STATUS_SUCCESS && fileInfo is FileInternalInformation internalInfo)
    {
        // 返回 IndexNumber 作为 Persistent
        return (ulong)internalInfo.IndexNumber;
    }
    
    // Fallback：文件系统不支持，使用 Volatile
    return volatileFileID;
}
```

### 为什么这样做是正确的？

**场景对比**：

| 实现方式 | 重命名 A→B | 覆盖文件 A | 追加内容 | LeaseManager |
|---------|----------|----------|---------|-------------|
| **IndexNumber** | 不变 ✅ | 改变 ✅ | 不变 ✅ | 正确 ✅ |
| **路径哈希** | 改变 ❌ | 不变 ❌ | 不变 ⚠️ | 错误 ❌ |
| **缓存** | 不可预测 ❌ | 不可预测 ❌ | 不可预测 ❌ | 混乱 ❌ |

## 调试和验证

### Windows 命令行工具

```cmd
# 使用 fsutil 查看文件索引
fsutil file queryfileid C:\path\to\file.txt

# 输出示例
文件 ID 为 0x00050000000152b3
```

解析：`0x00050000000152b3`
- 高 16 位：`0x0005` - Sequence Number
- 低 48 位：`0x0000000152b3` (86707) - IndexNumber

### PowerShell 查询

```powershell
# 获取文件的 FileIndex
$file = Get-Item "C:\path\to\file.txt"
$fileInfo = [System.IO.File]::GetAttributes($file.FullName)

# 或者使用 WMI
Get-WmiObject -Class CIM_DataFile -Filter "Name='C:\\path\\to\\file.txt'" | Select-Object FileIndex
```

### SMBLibrary 日志

启用日志后，可以看到 Persistent 值：

```
[Debug] Create: Opened 'Share\file.txt', FileId: Persistent=12345, Volatile=100
[Debug] MxAc: User 'alice', Path '\file.txt', Access: 0x001200A9
```

## 性能考虑

### 查询 IndexNumber 的开销

```
GetFileInformation(FileInternalInformation) 开销：
  - Windows API 调用：~0.001 ms
  - 比 GetFileInformation(FileBasicInformation) 稍快
  - 只读取 8 字节数据
  - 无磁盘 I/O（从内存中的 MFT 读取）
```

**结论**：性能开销可忽略不计。

### 缓存是否有必要？

**不需要！**
- ❌ IndexNumber 查询非常快（~0.001ms）
- ❌ 缓存会引入过期问题
- ❌ 缓存无法检测文件覆盖
- ✅ 每次查询才是正确的做法

## 实际应用示例

### 示例 1：检测文件是否被替换

```csharp
public class FileWatcher
{
    private Dictionary<string, ulong> m_fileIndexes = new Dictionary<string, ulong>();
    
    public void SaveFileIndex(string path, object handle, INTFileStore fileStore)
    {
        FileInformation fileInfo;
        if (fileStore.GetFileInformation(out fileInfo, handle, 
            FileInformationClass.FileInternalInformation) == NTStatus.STATUS_SUCCESS)
        {
            var internalInfo = (FileInternalInformation)fileInfo;
            m_fileIndexes[path] = (ulong)internalInfo.IndexNumber;
        }
    }
    
    public bool IsFileReplaced(string path, object handle, INTFileStore fileStore)
    {
        if (!m_fileIndexes.ContainsKey(path))
            return false;
        
        FileInformation fileInfo;
        if (fileStore.GetFileInformation(out fileInfo, handle,
            FileInformationClass.FileInternalInformation) == NTStatus.STATUS_SUCCESS)
        {
            var internalInfo = (FileInternalInformation)fileInfo;
            ulong currentIndex = (ulong)internalInfo.IndexNumber;
            
            return currentIndex != m_fileIndexes[path];  // 不同 = 被替换
        }
        
        return false;
    }
}
```

### 示例 2：硬链接检测

```csharp
public bool IsSameFile(object handle1, object handle2, INTFileStore fileStore)
{
    ulong? index1 = GetIndexNumber(handle1, fileStore);
    ulong? index2 = GetIndexNumber(handle2, fileStore);
    
    return index1.HasValue && index2.HasValue && index1 == index2;
}

private ulong? GetIndexNumber(object handle, INTFileStore fileStore)
{
    FileInformation fileInfo;
    if (fileStore.GetFileInformation(out fileInfo, handle,
        FileInformationClass.FileInternalInformation) == NTStatus.STATUS_SUCCESS)
    {
        return (ulong)((FileInternalInformation)fileInfo).IndexNumber;
    }
    return null;
}
```

## 总结

### IndexNumber 是什么？

**文件在文件系统中的唯一身份证号**

- 类似于：Unix inode, macOS CNID
- 特点：稳定、唯一、不随重命名改变
- 用途：文件识别、去重、变更检测

### 为什么 FileID.Persistent 使用 IndexNumber？

1. ✅ **稳定性** - 重命名后仍能识别文件
2. ✅ **正确性** - 文件覆盖后会改变
3. ✅ **性能** - 查询非常快
4. ✅ **兼容性** - 所有现代文件系统都有类似概念
5. ✅ **LeaseManager** - 正确跟踪文件租约

### 实现要点

```csharp
// ✅ 使用现有接口
fileStore.GetFileInformation(out fileInfo, handle, 
    FileInformationClass.FileInternalInformation);

// ✅ 返回 IndexNumber
return ((FileInternalInformation)fileInfo).IndexNumber;

// ❌ 不要用路径哈希（重命名会改变）
// ❌ 不要用缓存（会过期）
// ❌ 不要包含文件大小（追加会改变）
```

---

**最后更新**: 2025-01-09

