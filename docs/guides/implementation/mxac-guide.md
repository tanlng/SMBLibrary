# MxAc (Maximum Access) 完整指南

## 📖 目录

- [概述](#概述)
- [设计原则](#设计原则)
- [实现方式](#实现方式)
- [使用示例](#使用示例)
- [最佳实践](#最佳实践)
- [故障排除](#故障排除)

## 概述

### 什么是 MxAc？

MxAc (Maximum Access) 是 SMB2/SMB3 协议中的 Create Context，用于返回**用户对文件的最大访问权限**。

**重要性**：
- Windows 资源管理器几乎总是请求此上下文
- 决定右键菜单中哪些操作可用（删除、重命名等）
- 影响 Office 应用判断文件是否可编辑

### SMBLibrary 的实现方式

**核心理念**：SMBLibrary 不是文件系统，而是 SMB 协议实现。

因此：
- ❌ 不使用 Windows NTFS ACL
- ❌ 不使用 Unix 文件系统权限
- ❌ 不依赖底层文件系统
- ✅ **完全由用户通过 `AccessRequested` 事件定义权限**

## 设计原则

### 权限来源

MxAc 权限**完全基于共享层**，通过 `FileSystemShare.AccessRequested` 事件定义：

```csharp
// CreateHelper.ProcessMxAcContext
if (share is FileSystemShare fileSystemShare)
{
    bool hasReadAccess = fileSystemShare.HasReadAccess(session.SecurityContext, path);
    bool hasWriteAccess = fileSystemShare.HasWriteAccess(session.SecurityContext, path);
    
    maximalAccess = StandardAccessMasks.Build(
        canRead: hasReadAccess,
        canWrite: hasWriteAccess,
        canDelete: hasWriteAccess,
        canExecute: hasReadAccess);
}
```

### 为什么这样设计？

1. **灵活性** - 可以集成任何权限系统（数据库、LDAP、自定义逻辑）
2. **简单性** - 不需要处理复杂的文件系统权限
3. **一致性** - 所有共享类型使用相同的权限模型
4. **可控性** - 用户完全控制权限逻辑

## 实现方式

### 基本用法

```csharp
var adapter = new NTFileSystemAdapter(fileSystem);
var share = new FileSystemShare("MyShare", adapter);

// 定义权限逻辑
share.AccessRequested += (sender, args) =>
{
    // args.UserName - 用户名
    // args.Path - 文件路径
    // args.RequestedAccess - 请求的访问类型（Read/Write）
    
    if (args.UserName == "admin")
    {
        args.Allow = true;  // 管理员有完全权限
    }
    else if (args.RequestedAccess == FileAccess.Read)
    {
        args.Allow = true;  // 所有人可读
    }
    else
    {
        args.Allow = false;  // 其他情况拒绝
    }
};

// MxAc 自动反映这些权限
```

### 权限映射

| HasReadAccess | HasWriteAccess | MxAc 值 | 权限级别 |
|--------------|---------------|---------|---------|
| false | false | 0x00000000 | No Access |
| true | false | 0x001200A9 | ReadAndExecute |
| true | true | 0x001301BF | Modify |

## 使用示例

### 示例 1：基于用户名的权限

```csharp
share.AccessRequested += (sender, args) =>
{
    if (args.UserName == "admin" || args.UserName == "DOMAIN\\admin")
    {
        args.Allow = true;  // 管理员完全权限
        return;
    }
    
    if (args.UserName == "guest")
    {
        args.Allow = (args.RequestedAccess & FileAccess.Write) == 0;  // 访客只读
        return;
    }
    
    args.Allow = true;  // 其他用户完全权限
};
```

### 示例 2：基于路径的权限

```csharp
share.AccessRequested += (sender, args) =>
{
    // Public 文件夹：所有人可读
    if (args.Path.StartsWith(@"\Public\", StringComparison.OrdinalIgnoreCase))
    {
        args.Allow = args.RequestedAccess == FileAccess.Read;
        return;
    }
    
    // Private 文件夹：只有所有者可访问
    if (args.Path.StartsWith(@"\Private\", StringComparison.OrdinalIgnoreCase))
    {
        string ownerPath = $@"\Private\{args.UserName}\";
        args.Allow = args.Path.StartsWith(ownerPath, StringComparison.OrdinalIgnoreCase) 
                     || args.UserName == "admin";
        return;
    }
    
    args.Allow = true;  // 默认允许
};
```

### 示例 3：集成外部权限系统

```csharp
public class PermissionService
{
    private readonly string _connectionString;
    
    public bool CheckAccess(string userName, string path, FileAccess requestedAccess)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            connection.Open();
            var command = new SqlCommand(
                "SELECT CanRead, CanWrite FROM Permissions WHERE UserName = @UserName AND Path = @Path",
                connection);
            
            command.Parameters.AddWithValue("@UserName", userName);
            command.Parameters.AddWithValue("@Path", path);
            
            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    bool canRead = reader.GetBoolean(0);
                    bool canWrite = reader.GetBoolean(1);
                    
                    if (requestedAccess.HasFlag(FileAccess.Write))
                        return canWrite;
                    if (requestedAccess.HasFlag(FileAccess.Read))
                        return canRead;
                }
            }
        }
        return false;
    }
}

// 使用
var permissionService = new PermissionService(connectionString);

share.AccessRequested += (sender, args) =>
{
    try
    {
        args.Allow = permissionService.CheckAccess(
            args.UserName, 
            args.Path, 
            args.RequestedAccess);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Permission check failed: {ex.Message}");
        args.Allow = false;  // 出错时拒绝访问
    }
};
```

### 示例 4：基于时间的访问控制

```csharp
share.AccessRequested += (sender, args) =>
{
    var now = DateTime.Now;
    bool isBusinessHours = now.DayOfWeek >= DayOfWeek.Monday &&
                          now.DayOfWeek <= DayOfWeek.Friday &&
                          now.Hour >= 9 && now.Hour < 18;
    
    if (!isBusinessHours)
    {
        // 非工作时间：只有管理员可以写入
        if (args.RequestedAccess.HasFlag(FileAccess.Write))
        {
            args.Allow = args.UserName == "admin";
        }
        else
        {
            args.Allow = true;  // 所有人可读
        }
    }
    else
    {
        args.Allow = true;  // 工作时间：正常权限
    }
};
```

### 示例 5：文件扩展名限制

```csharp
private static readonly HashSet<string> AllowedExtensions = new HashSet<string>(
    StringComparer.OrdinalIgnoreCase)
{
    ".txt", ".doc", ".docx", ".pdf", ".jpg", ".png"
};

share.AccessRequested += (sender, args) =>
{
    string extension = Path.GetExtension(args.Path);
    
    // 读操作总是允许
    if (args.RequestedAccess == FileAccess.Read)
    {
        args.Allow = true;
        return;
    }
    
    // 写操作：检查扩展名
    if (args.RequestedAccess.HasFlag(FileAccess.Write))
    {
        args.Allow = AllowedExtensions.Contains(extension);
        if (!args.Allow)
        {
            Console.WriteLine($"Write denied: extension {extension} not allowed");
        }
    }
};
```

### 示例 6：审计日志

```csharp
public class AuditLogger
{
    private readonly string _logPath;
    
    public void LogAccess(string userName, string path, FileAccess access, bool allowed)
    {
        string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | " +
                         $"User: {userName} | Path: {path} | " +
                         $"Access: {access} | Result: {(allowed ? "ALLOWED" : "DENIED")}";
        
        File.AppendAllText(_logPath, logEntry + Environment.NewLine);
    }
}

var auditLogger = new AuditLogger("access.log");

share.AccessRequested += (sender, args) =>
{
    bool allowed = DetermineAccess(args);
    auditLogger.LogAccess(args.UserName, args.Path, args.RequestedAccess, allowed);
    args.Allow = allowed;
};
```

### 示例 7：组权限管理

```csharp
public class GroupPermissionManager
{
    private readonly Dictionary<string, HashSet<string>> _userGroups;
    private readonly Dictionary<string, HashSet<string>> _groupPermissions;
    
    public GroupPermissionManager()
    {
        _userGroups = new Dictionary<string, HashSet<string>>
        {
            ["alice"] = new HashSet<string> { "Administrators", "Developers" },
            ["bob"] = new HashSet<string> { "Developers" },
            ["charlie"] = new HashSet<string> { "Guests" }
        };
        
        _groupPermissions = new Dictionary<string, HashSet<string>>
        {
            ["Administrators"] = new HashSet<string> { "*" },
            ["Developers"] = new HashSet<string> { @"\Projects", @"\Source" },
            ["Guests"] = new HashSet<string> { @"\Public" }
        };
    }
    
    public bool CheckAccess(string userName, string path, FileAccess requestedAccess)
    {
        if (!_userGroups.TryGetValue(userName, out var groups))
            return false;
        
        foreach (var group in groups)
        {
            if (!_groupPermissions.TryGetValue(group, out var allowedPaths))
                continue;
            
            foreach (var allowedPath in allowedPaths)
            {
                if (allowedPath == "*" || 
                    path.StartsWith(allowedPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (group == "Guests" && requestedAccess.HasFlag(FileAccess.Write))
                        continue;
                    
                    return true;
                }
            }
        }
        return false;
    }
}

var groupManager = new GroupPermissionManager();

share.AccessRequested += (sender, args) =>
{
    args.Allow = groupManager.CheckAccess(args.UserName, args.Path, args.RequestedAccess);
};
```

## 最佳实践

### 1. 性能优化 - 使用缓存

```csharp
public class CachedPermissionChecker
{
    private readonly ConcurrentDictionary<string, (bool allowed, DateTime expires)> _cache;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);
    
    public bool CheckAccess(string userName, string path, FileAccess requestedAccess)
    {
        string key = $"{userName}|{path}|{requestedAccess}";
        
        if (_cache.TryGetValue(key, out var cached) && cached.expires > DateTime.Now)
        {
            return cached.allowed;
        }
        
        bool allowed = PerformExpensiveCheck(userName, path, requestedAccess);
        _cache[key] = (allowed, DateTime.Now + _cacheExpiration);
        return allowed;
    }
    
    private bool PerformExpensiveCheck(string userName, string path, FileAccess requestedAccess)
    {
        // 实际的权限检查逻辑
        return true;
    }
}
```

### 2. 异常处理

```csharp
share.AccessRequested += (sender, args) =>
{
    try
    {
        args.Allow = CheckPermission(args);
    }
    catch (Exception ex)
    {
        // 记录错误
        Console.WriteLine($"Permission check error: {ex.Message}");
        
        // 默认拒绝（安全优先）
        args.Allow = false;
    }
};
```

### 3. 日志记录

```csharp
share.AccessRequested += (sender, args) =>
{
    Console.WriteLine($"[AccessRequest] User: {args.UserName}, Path: {args.Path}, Access: {args.RequestedAccess}");
    
    bool allowed = DetermineAccess(args);
    
    if (!allowed)
    {
        Console.WriteLine($"[AccessDenied] User '{args.UserName}' denied access to '{args.Path}'");
    }
    
    args.Allow = allowed;
};
```

### 4. 清晰的逻辑结构

```csharp
share.AccessRequested += (sender, args) =>
{
    // 1. 检查管理员
    if (IsAdmin(args.UserName))
    {
        args.Allow = true;
        return;
    }
    
    // 2. 检查特殊路径
    if (IsRestrictedPath(args.Path))
    {
        args.Allow = false;
        return;
    }
    
    // 3. 检查普通权限
    args.Allow = CheckNormalPermission(args);
};
```

## 故障排除

### 问题 1：MxAc 返回 0（无权限）

**原因**：`AccessRequested` 事件未设置或返回 false

**解决**：
```csharp
// 检查事件是否订阅
share.AccessRequested += (sender, args) =>
{
    Console.WriteLine($"Event fired for {args.UserName}");
    args.Allow = true;  // 确保设置
};
```

### 问题 2：所有文件显示只读

**原因**：`HasWriteAccess` 总是返回 false

**解决**：
```csharp
share.AccessRequested += (sender, args) =>
{
    if (args.RequestedAccess.HasFlag(FileAccess.Write))
    {
        Console.WriteLine($"Write requested by {args.UserName}");
        args.Allow = true;  // 允许写入
    }
};
```

### 问题 3：权限检查太慢

**原因**：频繁的外部调用

**解决**：使用缓存（见性能优化示例）

### 调试技巧

```csharp
share.AccessRequested += (sender, args) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    
    bool allowed = CheckPermission(args);
    
    sw.Stop();
    Console.WriteLine($"Permission check took {sw.ElapsedMilliseconds}ms");
    
    args.Allow = allowed;
};
```

## 相关文档

- [文件访问架构](../../architecture/file-access-architecture.md) - 完整的架构说明
- [Create Contexts 实现指南](create-contexts-implementation-guide.md) - Create Contexts 详解

## 总结

### 核心要点

1. ✅ MxAc 完全基于 `AccessRequested` 事件
2. ✅ 不依赖文件系统权限
3. ✅ 用户完全控制权限逻辑
4. ✅ 灵活且易于集成外部系统

### 快速开始

```csharp
// 1. 创建共享
var share = new FileSystemShare("MyShare", adapter);

// 2. 定义权限
share.AccessRequested += (sender, args) =>
{
    args.Allow = YourPermissionLogic(args.UserName, args.Path, args.RequestedAccess);
};

// 3. 添加到服务器
server.AddShare(share);

// MxAc 自动工作！
```

---

**最后更新**: 2025-01-09

