# 文件访问架构：INTFileStore、IFileSystem、ISMBShare 和用户权限

## 概述

SMBLibrary 的文件访问架构采用分层设计，通过三个核心接口实现从 SMB 协议层到底层文件系统的权限控制和文件操作。本文档详细说明这些接口之间的关系以及用户权限检查的实现位置。

## 核心接口层次结构

```
┌─────────────────────────────────────────────────────────────┐
│                      SMB Client Request                      │
│                   (User Authentication)                      │
└───────────────────────────┬─────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                        ISMBShare                             │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  Share-Level Access Control (Settings.xml)           │  │
│  │  - HasAccess(SecurityContext, path, FileAccess)      │  │
│  │  - HasReadAccess(SecurityContext, path)              │  │
│  │  - HasWriteAccess(SecurityContext, path)             │  │
│  └──────────────────────────────────────────────────────┘  │
│                            │                                 │
│                  Contains: INTFileStore                      │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                      INTFileStore                            │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  File System Level Operations                        │  │
│  │  - CreateFile(...)                                    │  │
│  │  - ReadFile(...)                                      │  │
│  │  - WriteFile(...)                                     │  │
│  └──────────────────────────────────────────────────────┘  │
│                            │                                 │
│              Implementations:                                │
│              - NTFileSystemAdapter (uses IFileSystem)        │
│              - NTDirectoryFileSystem (Windows native)        │
│              - NamedPipeStore                                │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                       IFileSystem                            │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  Low-Level File System Operations                    │  │
│  │  - CreateFile(path, mode, access, share, options)    │  │
│  │  - OpenFile(...)                                      │  │
│  │  - Delete(path)                                       │  │
│  │  - GetEntry(path)                                     │  │
│  │    → Pure file system operations, no SMB logic       │  │
│  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

## 接口详细说明

### 1. ISMBShare - 共享级别访问控制

**位置**: `SMBLibrary/Server/Shares/ISMBShare.cs`

**职责**:
- SMB 共享的抽象表示
- 管理共享级别的访问控制（来自 Settings.xml 配置）
- 包含一个 `INTFileStore` 实例用于实际文件操作

**主要成员**:
```csharp
public interface ISMBShare
{
    string Name { get; }
    INTFileStore FileStore { get; }
    
    // 共享级别权限检查方法 - 这是第一层防护
    bool HasReadAccess(SecurityContext securityContext, string path);
    bool HasWriteAccess(SecurityContext securityContext, string path);
}
```

**实现类**:
- `FileSystemShare` - 文件系统共享（最常用）
- `NamedPipeShare` - 命名管道共享

#### FileSystemShare.HasAccess 方法详解

**签名**:
```csharp
public bool HasAccess(SecurityContext securityContext, string path, FileAccess requestedAccess)
```

**功能**:
- 检查用户是否有权限访问共享中的特定路径
- 基于 Settings.xml 中配置的 `<ReadAccess>` 和 `<WriteAccess>` 列表
- 这是权限检查的**第一层**（共享层）

**调用时机**:
1. 在 `CreateHelper.GetCreateResponse()` 中，**在调用文件系统操作之前**
2. 在所有文件操作之前都应该先检查共享级别权限

**实现逻辑**:
```csharp
public bool HasAccess(SecurityContext securityContext, string path, FileAccess requestedAccess)
{
    // 1. 检查读权限
    bool hasReadAccess = (requestedAccess & FileAccess.Read) == 0 || 
                         HasReadAccess(securityContext, path);
    
    // 2. 检查写权限
    bool hasWriteAccess = (requestedAccess & FileAccess.Write) == 0 || 
                          HasWriteAccess(securityContext, path);
    
    return hasReadAccess && hasWriteAccess;
}

public bool HasReadAccess(SecurityContext securityContext, string path)
{
    // 检查 m_readAccessEntries 列表（从 Settings.xml <ReadAccess> 加载）
    // 格式: DOMAIN\Username 或 *（所有人）
    foreach (string entry in m_readAccessEntries)
    {
        if (entry == "*" || 
            entry.Equals(securityContext.UserName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }
    return false;
}

public bool HasWriteAccess(SecurityContext securityContext, string path)
{
    // 检查 m_writeAccessEntries 列表（从 Settings.xml <WriteAccess> 加载）
    foreach (string entry in m_writeAccessEntries)
    {
        if (entry == "*" || 
            entry.Equals(securityContext.UserName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }
    return false;
}
```

**配置示例** (Settings.xml):
```xml
<Shares>
  <Share>
    <Name>Public</Name>
    <Path>C:\Public</Path>
    <ReadAccess>*</ReadAccess>                    <!-- 所有人可读 -->
    <WriteAccess>DOMAIN\Admins,DOMAIN\Users</WriteAccess>  <!-- 特定用户可写 -->
  </Share>
  <Share>
    <Name>Private</Name>
    <Path>C:\Private</Path>
    <ReadAccess>DOMAIN\Manager</ReadAccess>       <!-- 仅 Manager 可读 -->
    <WriteAccess>DOMAIN\Manager</WriteAccess>     <!-- 仅 Manager 可写 -->
  </Share>
</Shares>
```

### 2. INTFileStore - 文件系统级别操作

**位置**: `SMBLibrary/NTFileStore/INTFileStore.cs`

**职责**:
- 提供 NT 风格的文件系统操作接口
- 管理文件系统级别的权限（NTFS ACL、Unix 权限等）
- 处理文件句柄、锁、通知等高级功能

**权限相关方法**:
```csharp
public interface INTFileStore
{
    // 创建/打开文件 - 会根据底层文件系统的 ACL 进行权限检查
    NTStatus CreateFile(
        out object handle, 
        out FileStatus fileStatus, 
        string path, 
        AccessMask desiredAccess,  // 请求的访问权限
        FileAttributes fileAttributes, 
        ShareAccess shareAccess, 
        CreateDisposition createDisposition, 
        CreateOptions createOptions, 
        SecurityContext securityContext);  // 用户上下文
    
    // 其他文件操作...
    NTStatus ReadFile(out byte[] data, object handle, long offset, int maxCount);
    NTStatus WriteFile(out int numberOfBytesWritten, object handle, long offset, byte[] data);
    // ...
}
```

**MxAc (Maximum Access) 权限计算**:

MxAc 上下文返回用户对文件的最大访问权限。在 SMBLibrary 中，这完全基于**共享层权限**，由用户通过 `FileSystemShare.AccessRequested` 事件定义。

**实现原理**:

```csharp
// CreateHelper.ProcessMxAcContext
private static void ProcessMxAcContext(...)
{
    if (share is FileSystemShare fileSystemShare)
    {
        // 查询共享层权限
        bool hasReadAccess = fileSystemShare.HasReadAccess(session.SecurityContext, path);
        bool hasWriteAccess = fileSystemShare.HasWriteAccess(session.SecurityContext, path);
        
        // 构建访问掩码
        maximalAccess = StandardAccessMasks.Build(
            canRead: hasReadAccess,
            canWrite: hasWriteAccess,
            canDelete: hasWriteAccess,
            canExecute: hasReadAccess);
    }
    else
    {
        // 非 FileSystemShare 类型返回完全控制
        maximalAccess = StandardAccessMasks.FullControl;
    }
}
```

**权限来源**: 
- ✅ `FileSystemShare.AccessRequested` 事件 - 用户自定义权限逻辑
- ✅ 直接反映共享层的读写权限设置
- ✅ 不依赖底层文件系统的 ACL（这不是一个文件系统，是 SMB 协议层）

### 3. IFileSystem - 底层文件系统抽象

**位置**: `DiskAccessLibrary.FileSystems.Abstractions` (外部库)

**职责**:
- 提供纯粹的文件系统操作（类似 .NET 的 File/Directory 类）
- 不包含 SMB 协议逻辑
- 不处理用户认证和权限（由上层处理）

**主要方法**:
```csharp
public interface IFileSystem
{
    // 纯文件系统操作，不涉及 SMB 权限逻辑
    Stream OpenFile(string path, FileMode mode, FileAccess access, FileShare share, FileOptions options);
    FileSystemEntry CreateFile(string path);
    FileSystemEntry CreateDirectory(string path);
    void Delete(string path);
    FileSystemEntry GetEntry(string path);
    List<FileSystemEntry> ListEntries(string path);
    
    // 文件系统属性
    bool SupportsNamedStreams { get; }
    long Size { get; }
    long FreeSpace { get; }
}
```

**注意**: IFileSystem 本身**不负责权限检查**，权限检查由 INTFileStore 和 ISMBShare 层处理。

## 权限检查流程

### 完整的双层权限检查

当客户端请求打开文件时，权限检查分为两层：

```
┌─────────────────────────────────────────────────────────────┐
│  1. 共享层权限检查 (ISMBShare.HasAccess)                     │
│     检查内容：Settings.xml 中的 ReadAccess/WriteAccess      │
│     检查时机：在调用 FileStore.CreateFile 之前               │
│     失败返回：NTStatus.STATUS_ACCESS_DENIED (SMB层拒绝)      │
└───────────────────────────┬─────────────────────────────────┘
                            │ 通过
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  2. 文件系统层权限检查 (INTFileStore.CreateFile)             │
│     检查内容：NTFS ACL, Unix 权限, Stream.CanRead/CanWrite │
│     检查时机：在实际打开文件时                               │
│     失败返回：NTStatus.STATUS_ACCESS_DENIED (文件系统拒绝)   │
└───────────────────────────┬─────────────────────────────────┘
                            │ 通过
                            ▼
                 ┌──────────────────────┐
                 │   文件成功打开        │
                 │   返回文件句柄        │
                 └──────────────────────┘
```

### 代码示例：CreateHelper.GetCreateResponse

```csharp
internal static SMB2Command GetCreateResponse(CreateRequest request, ISMBShare share, SMB2ConnectionState state)
{
    SMB2Session session = state.GetSession(request.Header.SessionID);
    string path = request.Name;
    
    // ========== 第一层：共享级别权限检查 ==========
    FileAccess createAccess = NTFileStoreHelper.ToCreateFileAccess(request.DesiredAccess, request.CreateDisposition);
    if (share is FileSystemShare)
    {
        if (!((FileSystemShare)share).HasAccess(session.SecurityContext, path, createAccess))
        {
            // 共享层拒绝访问
            state.LogToServer(Severity.Verbose, "Create: User '{0}' denied access to '{1}{2}'", 
                session.UserName, share.Name, path);
            return new ErrorResponse(request.CommandName, NTStatus.STATUS_ACCESS_DENIED);
        }
    }
    
    // ========== 第二层：文件系统级别权限检查 ==========
    object handle;
    FileStatus fileStatus;
    AccessMask desiredAccess = request.DesiredAccess | (AccessMask)FileAccessMask.FILE_READ_ATTRIBUTES;
    
    // CreateFile 内部会根据文件系统的 ACL/权限进行检查
    NTStatus createStatus = share.FileStore.CreateFile(
        out handle, 
        out fileStatus, 
        path, 
        desiredAccess, 
        request.FileAttributes, 
        request.ShareAccess, 
        request.CreateDisposition, 
        request.CreateOptions, 
        session.SecurityContext);  // 传递用户上下文
    
    if (createStatus != NTStatus.STATUS_SUCCESS)
    {
        // 文件系统层拒绝访问（或其他错误）
        state.LogToServer(Severity.Verbose, "Create: FileStore.CreateFile failed with {0}", createStatus);
        return new ErrorResponse(request.CommandName, createStatus);
    }
    
    // ========== 文件成功打开 ==========
    FileID? fileID = session.AddOpenFile(request.Header.TreeID, share, path, handle, fileAccess);
    
    // ========== 处理 MxAc 上下文（查询最大权限）==========
    if (request.CreateContexts?.Any(c => c.Name == "MxAc") == true)
    {
        // 对于 FileSystemShare，基于共享层权限计算 MxAc
        ProcessMxAcContext(response, share, session, handle, path, state);
    }
    
    return response;
}
```

### ProcessMxAcContext 的实现

对于 `FileSystemShare`，MxAc 上下文的处理直接基于共享层权限：

```csharp
private static void ProcessMxAcContext(
    CreateResponse response,
    ISMBShare share,
    SMB2Session session,
    object handle,
    string path,
    SMB2ConnectionState state)
{
    AccessMask maximalAccess = 0;
    NTStatus status = NTStatus.STATUS_SUCCESS;
    
    // For FileSystemShare, calculate maximal access based on share-level permissions
    if (share is FileSystemShare fileSystemShare)
    {
        bool hasReadAccess = fileSystemShare.HasReadAccess(session.SecurityContext, path);
        bool hasWriteAccess = fileSystemShare.HasWriteAccess(session.SecurityContext, path);
        
        // Build access mask from read/write permissions
        maximalAccess = StandardAccessMasks.Build(
            canRead: hasReadAccess,
            canWrite: hasWriteAccess,
            canDelete: hasWriteAccess,
            canExecute: hasReadAccess);
    }
    else
    {
        // 非 FileSystemShare 类型返回完全控制
        maximalAccess = StandardAccessMasks.FullControl;
    }
    
    // Create MxAc response
    byte[] mxAcData = new byte[8];
    LittleEndianWriter.WriteUInt32(mxAcData, 0, (uint)status);
    LittleEndianWriter.WriteUInt32(mxAcData, 4, (uint)maximalAccess);
    
    response.CreateContexts.Add(new CreateContext
    {
        Name = "MxAc",
        Data = mxAcData
    });
}
```

## 权限检查位置总结表

| 权限检查类型 | 实现位置 | 检查内容 | 调用时机 | 失败影响 |
|------------|---------|---------|---------|---------|
| **共享层权限** | `ISMBShare.HasAccess()` | AccessRequested 事件定义的逻辑 | 在 CreateFile 之前 | 拒绝 SMB 请求 |
| **文件系统层权限** | `INTFileStore.CreateFile()` | Stream 能力、文件是否存在等 | 实际打开文件时 | 返回错误状态码 |
| **MxAc 权限查询** | `FileSystemShare.HasAccess()` | 共享层权限（通过 AccessRequested 事件） | MxAc 上下文请求时 | 仅影响 MxAc 响应 |

## 最佳实践建议

### 1. 何时在哪个层实现权限检查

**共享层 (ISMBShare)**:
- ✅ 基于用户名/用户组的简单访问控制
- ✅ 全局共享策略（如"所有人只读"）
- ✅ 快速预检，避免不必要的文件系统操作
- ✅ 来自 Settings.xml 的配置

**文件系统层 (INTFileStore)**:
- ✅ 实际的文件操作（打开、读写、关闭）
- ✅ 不负责权限检查（权限在共享层处理）
- ✅ 专注于文件系统操作本身

**MxAc 权限查询**:
- ✅ 完全基于共享层权限（HasReadAccess/HasWriteAccess）
- ✅ 通过 `AccessRequested` 事件自定义权限逻辑
- ✅ 不依赖底层文件系统（这不是一个文件系统，是 SMB 协议）

### 2. 权限检查顺序

**推荐顺序**（从快到慢）:
1. 共享层检查 - 快速拒绝未授权用户
2. 文件系统层检查 - 实际验证文件权限（在 CreateFile 时）
3. MxAc 权限查询 - 仅在需要时（MxAc 上下文）
   - 完全基于共享层权限（HasReadAccess/HasWriteAccess）

### 3. MxAc 上下文的实现

对于 `FileSystemShare`，MxAc 直接基于共享层权限：

```
MxAc_Access = CalculateFromSharePermissions(HasReadAccess, HasWriteAccess)
```

示例：
- HasReadAccess = true, HasWriteAccess = false → MxAc = ReadAndExecute (0x001200A9)
- HasReadAccess = true, HasWriteAccess = true → MxAc = Modify (0x001301BF)
- HasReadAccess = false → MxAc = 0

### 4. 自定义权限控制

使用 `FileSystemShare.AccessRequested` 事件实现自定义权限逻辑：

```csharp
var adapter = new NTFileSystemAdapter(fileSystem);
var share = new FileSystemShare("MyShare", adapter);

share.AccessRequested += (sender, args) =>
{
    // 自定义权限逻辑
    if (args.UserName == "admin")
    {
        args.Allow = true;  // 管理员有完全权限
    }
    else if (args.Path.StartsWith(@"\ReadOnly\"))
    {
        args.Allow = (args.RequestedAccess & FileAccess.Write) == 0;  // 只读目录
    }
    else
    {
        // 调用外部权限系统
        args.Allow = MyPermissionSystem.CheckAccess(args.UserName, args.Path, args.RequestedAccess);
    }
};
```

这个事件会影响：
- ✅ 文件打开操作（CreateFile 之前）
- ✅ 读写操作权限检查
- ✅ MxAc 上下文返回的最大权限

## 典型使用场景

### 场景 1: 用户访问共享文件

```
1. 用户 "DOMAIN\User1" 尝试打开 "\\Server\Public\file.txt"
2. SMB 服务器调用 share.HasAccess(user1Context, "\file.txt", Read)
3. 检查 Settings.xml: <ReadAccess>*</ReadAccess> → 允许
4. 调用 fileStore.CreateFile(..., READ_DATA, ..., user1Context)
5. 文件系统检查 NTFS ACL → 允许
6. 返回文件句柄
7. 如果客户端请求 MxAc:
   - 调用 fileSystemShare.HasReadAccess() → true
   - 调用 fileSystemShare.HasWriteAccess() → false
   - 计算 MxAc = ReadAndExecute (0x001200A9)
   - 返回 MxAc 上下文
```

### 场景 2: 用户尝试写入只读共享

```
1. 用户 "DOMAIN\User1" 尝试写入 "\\Server\Public\file.txt"
2. SMB 服务器调用 share.HasAccess(user1Context, "\file.txt", Write)
3. 检查 Settings.xml: <WriteAccess>DOMAIN\Admins</WriteAccess> → User1 不在列表
4. 返回 false → 在共享层拒绝
5. 不调用文件系统层（快速失败）
6. 返回 STATUS_ACCESS_DENIED
```

### 场景 3: 共享允许但文件系统拒绝

```
1. 用户 "DOMAIN\User1" 尝试访问 "\\Server\Private\secret.txt"
2. SMB 服务器调用 share.HasAccess(user1Context, "\secret.txt", Read)
3. 检查 Settings.xml: <ReadAccess>*</ReadAccess> → 允许
4. 调用 fileStore.CreateFile(..., READ_DATA, ..., user1Context)
5. 文件系统检查 NTFS ACL → secret.txt 的 ACL 不允许 User1 读取
6. 返回 STATUS_ACCESS_DENIED（来自文件系统）
7. 用户被拒绝（但是在文件系统层拒绝）
```

## 相关文档

- [AccessMask 详细说明](../protocols/smb2/access-mask-detailed-explanation.md)
- [AccessMask 简化映射](../protocols/smb2/access-mask-mapping-and-simplification.md)
- [MxAc 上下文实现](../guides/implementation/create-contexts-implementation-guide.md)
- [Create Contexts API 文档](../api/server/create-contexts/create-contexts-api.md)

## 附录：接口继承和实现关系

```
ISMBShare (接口)
├── FileSystemShare (实现)
│   ├── 包含: INTFileStore 实例
│   ├── 包含: ReadAccess 列表
│   └── 包含: WriteAccess 列表
└── NamedPipeShare (实现)
    └── 包含: INTFileStore 实例 (NamedPipeStore)

INTFileStore (接口)
├── NTFileSystemAdapter (实现)
│   └── 包含: IFileSystem 实例
├── NTDirectoryFileSystem (实现)
│   └── 直接使用 Windows Native API
├── NamedPipeStore (实现)
│   └── 管道特定逻辑
├── SMB1FileStore (客户端实现)
└── SMB2FileStore (客户端实现)

IFileSystem (接口 - 外部库)
├── NTFS 实现
├── FAT 实现
└── 其他文件系统实现
```

## 总结

**判断用户是否有权限访问文件，应该在以下两个地方实现**:

1. **ISMBShare.HasAccess** - 共享层权限检查（第一层防护）
   - 快速检查，基于 Settings.xml 配置
   - 在调用文件系统操作之前
   - 实现简单的用户/用户组访问控制

2. **INTFileStore.CreateFile** - 文件系统层权限检查（第二层防护）
   - 精确检查，基于实际的文件系统权限（NTFS ACL、Unix 权限等）
   - 在实际打开文件时自动执行
   - 提供细粒度的文件级别权限控制

3. **MxAc 上下文** - 返回最大访问权限（完全基于共享层）
   - 通过 `FileSystemShare.HasReadAccess/HasWriteAccess` 计算
   - 用户通过 `AccessRequested` 事件定义权限逻辑
   - 不依赖底层文件系统的 ACL 或权限系统

这种双层设计提供了灵活性和安全性：共享层提供快速的粗粒度控制，文件系统层提供精确的细粒度控制。

