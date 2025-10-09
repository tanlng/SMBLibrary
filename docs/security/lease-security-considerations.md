# SMB 2.0/2.1 租赁协议安全考虑

## 概述

本文档详细分析了 SMB 2.0/2.1 租赁协议实现中的安全风险和防护措施。租赁协议涉及客户端缓存、网络通信和状态管理，需要特别关注数据完整性、访问控制和隐私保护等安全方面。

## 安全威胁分析

### 1. 租赁键安全威胁

#### 1.1 租赁键泄露
**威胁描述**: 租赁键被恶意用户获取，可能导致未授权访问。

**风险等级**: 高

**攻击场景**:
- 网络嗅探获取租赁键
- 内存转储获取租赁键
- 日志文件泄露租赁键
- 客户端恶意软件获取租赁键

**防护措施**:
```csharp
public class SecureLeaseKeyGenerator
{
    private readonly RNGCryptoServiceProvider m_rng;
    
    public SecureLeaseKeyGenerator()
    {
        m_rng = new RNGCryptoServiceProvider();
    }
    
    public Guid GenerateSecureLeaseKey()
    {
        var bytes = new byte[16];
        m_rng.GetBytes(bytes);
        return new Guid(bytes);
    }
    
    public void Dispose()
    {
        m_rng?.Dispose();
    }
}
```

#### 1.2 租赁键重放攻击
**威胁描述**: 攻击者重放有效的租赁键来获取未授权访问。

**风险等级**: 中

**攻击场景**:
- 捕获网络数据包中的租赁键
- 重放租赁键到新的会话
- 利用过期的租赁键

**防护措施**:
```csharp
public class LeaseKeyValidator
{
    private readonly Dictionary<Guid, LeaseKeyInfo> m_activeKeys;
    private readonly TimeSpan m_keyLifetime;
    
    public LeaseKeyValidator(TimeSpan keyLifetime)
    {
        m_activeKeys = new Dictionary<Guid, LeaseKeyInfo>();
        m_keyLifetime = keyLifetime;
    }
    
    public bool ValidateLeaseKey(Guid leaseKey, ulong sessionId, string clientIP)
    {
        if (!m_activeKeys.TryGetValue(leaseKey, out var keyInfo))
        {
            return false;
        }
        
        // 检查租赁键是否过期
        if (DateTime.UtcNow > keyInfo.CreatedTime.Add(m_keyLifetime))
        {
            m_activeKeys.Remove(leaseKey);
            return false;
        }
        
        // 检查会话ID是否匹配
        if (keyInfo.SessionId != sessionId)
        {
            return false;
        }
        
        // 检查客户端IP是否匹配
        if (keyInfo.ClientIP != clientIP)
        {
            return false;
        }
        
        return true;
    }
    
    public void RegisterLeaseKey(Guid leaseKey, ulong sessionId, string clientIP)
    {
        m_activeKeys[leaseKey] = new LeaseKeyInfo
        {
            LeaseKey = leaseKey,
            SessionId = sessionId,
            ClientIP = clientIP,
            CreatedTime = DateTime.UtcNow
        };
    }
}

public class LeaseKeyInfo
{
    public Guid LeaseKey { get; set; }
    public ulong SessionId { get; set; }
    public string ClientIP { get; set; }
    public DateTime CreatedTime { get; set; }
}
```

### 2. 缓存数据安全威胁

#### 2.1 缓存数据泄露
**威胁描述**: 客户端缓存的数据被恶意访问或泄露。

**风险等级**: 高

**攻击场景**:
- 物理访问客户端设备
- 恶意软件访问缓存文件
- 内存转储获取缓存数据
- 共享设备上的数据泄露

**防护措施**:
```csharp
public class SecureLeaseCache
{
    private readonly Dictionary<FileID, EncryptedCacheData> m_encryptedCache;
    private readonly byte[] m_encryptionKey;
    private readonly byte[] m_iv;
    
    public SecureLeaseCache()
    {
        m_encryptedCache = new Dictionary<FileID, EncryptedCacheData>();
        
        // 生成加密密钥
        using (var rng = new RNGCryptoServiceProvider())
        {
            m_encryptionKey = new byte[32];
            m_iv = new byte[16];
            rng.GetBytes(m_encryptionKey);
            rng.GetBytes(m_iv);
        }
    }
    
    public void CacheFileData(FileID fileId, byte[] data, LeaseInfo leaseInfo)
    {
        var encryptedData = EncryptData(data);
        var encryptedLeaseInfo = EncryptLeaseInfo(leaseInfo);
        
        m_encryptedCache[fileId] = new EncryptedCacheData
        {
            EncryptedData = encryptedData,
            EncryptedLeaseInfo = encryptedLeaseInfo,
            CachedTime = DateTime.UtcNow
        };
    }
    
    public byte[] GetCachedData(FileID fileId)
    {
        if (m_encryptedCache.TryGetValue(fileId, out var encryptedData))
        {
            return DecryptData(encryptedData.EncryptedData);
        }
        return null;
    }
    
    private byte[] EncryptData(byte[] data)
    {
        using (var aes = new AesCryptoServiceProvider())
        {
            aes.Key = m_encryptionKey;
            aes.IV = m_iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            
            using (var encryptor = aes.CreateEncryptor())
            using (var msEncrypt = new MemoryStream())
            using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
            {
                csEncrypt.Write(data, 0, data.Length);
                csEncrypt.FlushFinalBlock();
                return msEncrypt.ToArray();
            }
        }
    }
    
    private byte[] DecryptData(byte[] encryptedData)
    {
        using (var aes = new AesCryptoServiceProvider())
        {
            aes.Key = m_encryptionKey;
            aes.IV = m_iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            
            using (var decryptor = aes.CreateDecryptor())
            using (var msDecrypt = new MemoryStream(encryptedData))
            using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
            using (var msPlain = new MemoryStream())
            {
                csDecrypt.CopyTo(msPlain);
                return msPlain.ToArray();
            }
        }
    }
    
    private byte[] EncryptLeaseInfo(LeaseInfo leaseInfo)
    {
        var json = JsonConvert.SerializeObject(leaseInfo);
        var bytes = Encoding.UTF8.GetBytes(json);
        return EncryptData(bytes);
    }
    
    private LeaseInfo DecryptLeaseInfo(byte[] encryptedLeaseInfo)
    {
        var decryptedBytes = DecryptData(encryptedLeaseInfo);
        var json = Encoding.UTF8.GetString(decryptedBytes);
        return JsonConvert.DeserializeObject<LeaseInfo>(json);
    }
}

public class EncryptedCacheData
{
    public byte[] EncryptedData { get; set; }
    public byte[] EncryptedLeaseInfo { get; set; }
    public DateTime CachedTime { get; set; }
}
```

#### 2.2 缓存投毒攻击
**威胁描述**: 攻击者向客户端缓存注入恶意数据。

**风险等级**: 中

**攻击场景**:
- 中间人攻击修改缓存数据
- 恶意软件修改缓存文件
- 利用缓存验证漏洞

**防护措施**:
```csharp
public class CacheIntegrityValidator
{
    private readonly Dictionary<FileID, string> m_cacheHashes;
    
    public CacheIntegrityValidator()
    {
        m_cacheHashes = new Dictionary<FileID, string>();
    }
    
    public bool ValidateCacheIntegrity(FileID fileId, byte[] cachedData)
    {
        if (!m_cacheHashes.TryGetValue(fileId, out var expectedHash))
        {
            return false;
        }
        
        var actualHash = ComputeHash(cachedData);
        return actualHash == expectedHash;
    }
    
    public void SetCacheHash(FileID fileId, byte[] data)
    {
        var hash = ComputeHash(data);
        m_cacheHashes[fileId] = hash;
    }
    
    private string ComputeHash(byte[] data)
    {
        using (var sha256 = SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(data);
            return Convert.ToBase64String(hashBytes);
        }
    }
}
```

### 3. 网络通信安全威胁

#### 3.1 租赁中断消息伪造
**威胁描述**: 攻击者伪造租赁中断消息，导致客户端清理缓存。

**风险等级**: 中

**攻击场景**:
- 伪造租赁中断通知
- 重放租赁中断消息
- 利用网络协议漏洞

**防护措施**:
```csharp
public class LeaseBreakMessageValidator
{
    private readonly Dictionary<Guid, LeaseBreakSignature> m_signatures;
    private readonly byte[] m_signingKey;
    
    public LeaseBreakMessageValidator(byte[] signingKey)
    {
        m_signatures = new Dictionary<Guid, LeaseBreakSignature>();
        m_signingKey = signingKey;
    }
    
    public bool ValidateLeaseBreakMessage(LeaseBreakRequest request, byte[] signature)
    {
        var expectedSignature = ComputeSignature(request);
        return CryptographicOperations.FixedTimeEquals(signature, expectedSignature);
    }
    
    public byte[] SignLeaseBreakMessage(LeaseBreakRequest request)
    {
        return ComputeSignature(request);
    }
    
    private byte[] ComputeSignature(LeaseBreakRequest request)
    {
        var data = SerializeRequest(request);
        using (var hmac = new HMACSHA256(m_signingKey))
        {
            return hmac.ComputeHash(data);
        }
    }
    
    private byte[] SerializeRequest(LeaseBreakRequest request)
    {
        var buffer = new byte[64];
        var offset = 0;
        
        // 序列化关键字段
        var leaseKeyBytes = request.LeaseKey.ToByteArray();
        Array.Copy(leaseKeyBytes, 0, buffer, offset, leaseKeyBytes.Length);
        offset += leaseKeyBytes.Length;
        
        var stateBytes = BitConverter.GetBytes((uint)request.CurrentLeaseState);
        Array.Copy(stateBytes, 0, buffer, offset, stateBytes.Length);
        offset += stateBytes.Length;
        
        var flagsBytes = BitConverter.GetBytes((uint)request.LeaseFlags);
        Array.Copy(flagsBytes, 0, buffer, offset, flagsBytes.Length);
        
        return buffer;
    }
}

public class LeaseBreakSignature
{
    public Guid LeaseKey { get; set; }
    public byte[] Signature { get; set; }
    public DateTime Timestamp { get; set; }
}
```

#### 3.2 租赁上下文篡改
**威胁描述**: 攻击者篡改租赁上下文，获取未授权的租赁权限。

**风险等级**: 高

**攻击场景**:
- 修改租赁状态请求
- 篡改租赁持续时间
- 伪造租赁标志

**防护措施**:
```csharp
public class LeaseContextValidator
{
    private readonly Dictionary<string, LeaseContextPolicy> m_policies;
    
    public LeaseContextValidator()
    {
        m_policies = new Dictionary<string, LeaseContextPolicy>();
        InitializeDefaultPolicies();
    }
    
    public bool ValidateLeaseContext(LeaseContext context, string filePath, ulong sessionId)
    {
        var policy = GetPolicyForPath(filePath);
        if (policy == null)
        {
            return false;
        }
        
        // 验证租赁状态
        if (!policy.AllowedLeaseStates.HasFlag(context.LeaseState))
        {
            return false;
        }
        
        // 验证租赁持续时间
        if (context.LeaseDuration > policy.MaxLeaseDuration)
        {
            return false;
        }
        
        // 验证租赁标志
        if (!policy.AllowedLeaseFlags.HasFlag(context.LeaseFlags))
        {
            return false;
        }
        
        return true;
    }
    
    private LeaseContextPolicy GetPolicyForPath(string filePath)
    {
        foreach (var policy in m_policies.Values)
        {
            if (policy.PathPattern.IsMatch(filePath))
            {
                return policy;
            }
        }
        return null;
    }
    
    private void InitializeDefaultPolicies()
    {
        // 默认策略：只读文件允许读取缓存
        m_policies["readonly"] = new LeaseContextPolicy
        {
            PathPattern = new Regex(@"\.(txt|log|cfg)$", RegexOptions.IgnoreCase),
            AllowedLeaseStates = LeaseState.ReadCaching,
            AllowedLeaseFlags = LeaseFlags.None,
            MaxLeaseDuration = TimeSpan.FromHours(1)
        };
        
        // 默认策略：可写文件允许读写缓存
        m_policies["writable"] = new LeaseContextPolicy
        {
            PathPattern = new Regex(@"\.(tmp|temp)$", RegexOptions.IgnoreCase),
            AllowedLeaseStates = LeaseState.ReadCaching | LeaseState.WriteCaching,
            AllowedLeaseFlags = LeaseFlags.None,
            MaxLeaseDuration = TimeSpan.FromMinutes(30)
        };
    }
}

public class LeaseContextPolicy
{
    public Regex PathPattern { get; set; }
    public LeaseState AllowedLeaseStates { get; set; }
    public LeaseFlags AllowedLeaseFlags { get; set; }
    public TimeSpan MaxLeaseDuration { get; set; }
}
```

### 4. 访问控制安全威胁

#### 4.1 权限提升攻击
**威胁描述**: 攻击者利用租赁机制绕过正常的访问控制。

**风险等级**: 高

**攻击场景**:
- 利用租赁缓存绕过文件权限检查
- 通过租赁中断绕过访问控制
- 利用租赁状态转换漏洞

**防护措施**:
```csharp
public class LeaseAccessController
{
    private readonly Dictionary<Guid, AccessControlEntry> m_accessControls;
    private readonly IFileSystemAccessControl m_fileSystemAccessControl;
    
    public LeaseAccessController(IFileSystemAccessControl fileSystemAccessControl)
    {
        m_accessControls = new Dictionary<Guid, AccessControlEntry>();
        m_fileSystemAccessControl = fileSystemAccessControl;
    }
    
    public bool CheckLeaseAccess(Guid leaseKey, string filePath, AccessMask requestedAccess, ulong sessionId)
    {
        if (!m_accessControls.TryGetValue(leaseKey, out var accessControl))
        {
            return false;
        }
        
        // 检查会话ID是否匹配
        if (accessControl.SessionId != sessionId)
        {
            return false;
        }
        
        // 检查文件路径是否匹配
        if (accessControl.FilePath != filePath)
        {
            return false;
        }
        
        // 检查访问权限
        if (!accessControl.AllowedAccess.HasFlag(requestedAccess))
        {
            return false;
        }
        
        // 检查文件系统权限
        if (!m_fileSystemAccessControl.CheckAccess(filePath, requestedAccess, sessionId))
        {
            return false;
        }
        
        return true;
    }
    
    public void RegisterLeaseAccess(Guid leaseKey, string filePath, AccessMask allowedAccess, ulong sessionId)
    {
        m_accessControls[leaseKey] = new AccessControlEntry
        {
            LeaseKey = leaseKey,
            FilePath = filePath,
            AllowedAccess = allowedAccess,
            SessionId = sessionId,
            CreatedTime = DateTime.UtcNow
        };
    }
    
    public void RevokeLeaseAccess(Guid leaseKey)
    {
        m_accessControls.Remove(leaseKey);
    }
}

public class AccessControlEntry
{
    public Guid LeaseKey { get; set; }
    public string FilePath { get; set; }
    public AccessMask AllowedAccess { get; set; }
    public ulong SessionId { get; set; }
    public DateTime CreatedTime { get; set; }
}
```

#### 4.2 会话劫持攻击
**威胁描述**: 攻击者劫持有效的会话，利用其租赁权限。

**风险等级**: 高

**攻击场景**:
- 网络嗅探获取会话ID
- 利用会话管理漏洞
- 重放攻击获取会话

**防护措施**:
```csharp
public class SessionSecurityManager
{
    private readonly Dictionary<ulong, SessionSecurityInfo> m_sessionSecurity;
    private readonly TimeSpan m_sessionTimeout;
    
    public SessionSecurityManager(TimeSpan sessionTimeout)
    {
        m_sessionSecurity = new Dictionary<ulong, SessionSecurityInfo>();
        m_sessionTimeout = sessionTimeout;
    }
    
    public bool ValidateSession(ulong sessionId, string clientIP, string userAgent)
    {
        if (!m_sessionSecurity.TryGetValue(sessionId, out var securityInfo))
        {
            return false;
        }
        
        // 检查会话是否过期
        if (DateTime.UtcNow > securityInfo.LastActivity.Add(m_sessionTimeout))
        {
            m_sessionSecurity.Remove(sessionId);
            return false;
        }
        
        // 检查客户端IP是否匹配
        if (securityInfo.ClientIP != clientIP)
        {
            return false;
        }
        
        // 检查用户代理是否匹配
        if (securityInfo.UserAgent != userAgent)
        {
            return false;
        }
        
        // 更新最后活动时间
        securityInfo.LastActivity = DateTime.UtcNow;
        
        return true;
    }
    
    public void RegisterSession(ulong sessionId, string clientIP, string userAgent)
    {
        m_sessionSecurity[sessionId] = new SessionSecurityInfo
        {
            SessionId = sessionId,
            ClientIP = clientIP,
            UserAgent = userAgent,
            CreatedTime = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        };
    }
    
    public void RevokeSession(ulong sessionId)
    {
        m_sessionSecurity.Remove(sessionId);
    }
}

public class SessionSecurityInfo
{
    public ulong SessionId { get; set; }
    public string ClientIP { get; set; }
    public string UserAgent { get; set; }
    public DateTime CreatedTime { get; set; }
    public DateTime LastActivity { get; set; }
}
```

## 安全配置

### 1. 租赁安全配置

```csharp
public class LeaseSecurityConfiguration
{
    // 租赁键安全
    public bool UseSecureLeaseKeyGeneration { get; set; } = true;
    public TimeSpan LeaseKeyLifetime { get; set; } = TimeSpan.FromHours(1);
    public bool ValidateLeaseKeyBinding { get; set; } = true;
    
    // 缓存安全
    public bool EncryptCacheData { get; set; } = true;
    public bool ValidateCacheIntegrity { get; set; } = true;
    public TimeSpan CacheDataLifetime { get; set; } = TimeSpan.FromMinutes(30);
    
    // 网络通信安全
    public bool SignLeaseBreakMessages { get; set; } = true;
    public bool ValidateLeaseContext { get; set; } = true;
    public bool UseSecureTransport { get; set; } = true;
    
    // 访问控制
    public bool EnforceAccessControl { get; set; } = true;
    public bool ValidateSessionBinding { get; set; } = true;
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(15);
    
    // 审计日志
    public bool EnableAuditLogging { get; set; } = true;
    public LogLevel AuditLogLevel { get; set; } = LogLevel.Information;
    public bool LogSensitiveData { get; set; } = false;
    
    public void Validate()
    {
        if (LeaseKeyLifetime <= TimeSpan.Zero)
            throw new ArgumentException("LeaseKeyLifetime must be positive");
        if (CacheDataLifetime <= TimeSpan.Zero)
            throw new ArgumentException("CacheDataLifetime must be positive");
        if (SessionTimeout <= TimeSpan.Zero)
            throw new ArgumentException("SessionTimeout must be positive");
    }
}
```

### 2. 安全策略配置

```csharp
public class LeaseSecurityPolicy
{
    public Dictionary<string, LeaseContextPolicy> ContextPolicies { get; set; }
    public Dictionary<string, AccessControlPolicy> AccessPolicies { get; set; }
    public Dictionary<string, CacheSecurityPolicy> CachePolicies { get; set; }
    
    public LeaseSecurityPolicy()
    {
        ContextPolicies = new Dictionary<string, LeaseContextPolicy>();
        AccessPolicies = new Dictionary<string, AccessControlPolicy>();
        CachePolicies = new Dictionary<string, CacheSecurityPolicy>();
        
        InitializeDefaultPolicies();
    }
    
    private void InitializeDefaultPolicies()
    {
        // 默认上下文策略
        ContextPolicies["default"] = new LeaseContextPolicy
        {
            PathPattern = new Regex(".*"),
            AllowedLeaseStates = LeaseState.ReadCaching,
            AllowedLeaseFlags = LeaseFlags.None,
            MaxLeaseDuration = TimeSpan.FromMinutes(30)
        };
        
        // 默认访问控制策略
        AccessPolicies["default"] = new AccessControlPolicy
        {
            RequireAuthentication = true,
            RequireAuthorization = true,
            AllowAnonymousAccess = false,
            MaxConcurrentLeases = 100
        };
        
        // 默认缓存安全策略
        CachePolicies["default"] = new CacheSecurityPolicy
        {
            EncryptData = true,
            ValidateIntegrity = true,
            MaxCacheSize = 100 * 1024 * 1024, // 100MB
            CacheLifetime = TimeSpan.FromMinutes(30)
        };
    }
}

public class AccessControlPolicy
{
    public bool RequireAuthentication { get; set; }
    public bool RequireAuthorization { get; set; }
    public bool AllowAnonymousAccess { get; set; }
    public int MaxConcurrentLeases { get; set; }
}

public class CacheSecurityPolicy
{
    public bool EncryptData { get; set; }
    public bool ValidateIntegrity { get; set; }
    public long MaxCacheSize { get; set; }
    public TimeSpan CacheLifetime { get; set; }
}
```

## 安全审计

### 1. 审计日志

```csharp
public class LeaseAuditLogger
{
    private readonly ILogger m_logger;
    private readonly bool m_logSensitiveData;
    
    public LeaseAuditLogger(ILogger logger, bool logSensitiveData = false)
    {
        m_logger = logger;
        m_logSensitiveData = logSensitiveData;
    }
    
    public void LogLeaseCreated(LeaseInfo leaseInfo, string clientIP)
    {
        var auditEvent = new LeaseAuditEvent
        {
            EventType = "LeaseCreated",
            LeaseKey = leaseInfo.LeaseKey,
            FilePath = leaseInfo.FilePath,
            SessionId = leaseInfo.SessionId,
            ClientIP = clientIP,
            Timestamp = DateTime.UtcNow
        };
        
        LogAuditEvent(auditEvent);
    }
    
    public void LogLeaseBreak(LeaseInfo leaseInfo, LeaseBreakReason reason, string clientIP)
    {
        var auditEvent = new LeaseAuditEvent
        {
            EventType = "LeaseBreak",
            LeaseKey = leaseInfo.LeaseKey,
            FilePath = leaseInfo.FilePath,
            SessionId = leaseInfo.SessionId,
            ClientIP = clientIP,
            Reason = reason.ToString(),
            Timestamp = DateTime.UtcNow
        };
        
        LogAuditEvent(auditEvent);
    }
    
    public void LogLeaseAccess(LeaseInfo leaseInfo, AccessMask accessMask, string clientIP)
    {
        var auditEvent = new LeaseAuditEvent
        {
            EventType = "LeaseAccess",
            LeaseKey = leaseInfo.LeaseKey,
            FilePath = leaseInfo.FilePath,
            SessionId = leaseInfo.SessionId,
            ClientIP = clientIP,
            AccessMask = accessMask.ToString(),
            Timestamp = DateTime.UtcNow
        };
        
        LogAuditEvent(auditEvent);
    }
    
    public void LogSecurityViolation(string violationType, string details, string clientIP)
    {
        var auditEvent = new LeaseAuditEvent
        {
            EventType = "SecurityViolation",
            ViolationType = violationType,
            Details = details,
            ClientIP = clientIP,
            Timestamp = DateTime.UtcNow
        };
        
        LogAuditEvent(auditEvent);
    }
    
    private void LogAuditEvent(LeaseAuditEvent auditEvent)
    {
        var message = JsonConvert.SerializeObject(auditEvent, Formatting.Indented);
        m_logger.LogInformation("AUDIT: {Message}", message);
    }
}

public class LeaseAuditEvent
{
    public string EventType { get; set; }
    public Guid LeaseKey { get; set; }
    public string FilePath { get; set; }
    public ulong SessionId { get; set; }
    public string ClientIP { get; set; }
    public string Reason { get; set; }
    public string AccessMask { get; set; }
    public string ViolationType { get; set; }
    public string Details { get; set; }
    public DateTime Timestamp { get; set; }
}
```

### 2. 安全监控

```csharp
public class LeaseSecurityMonitor
{
    private readonly Dictionary<string, SecurityMetric> m_metrics;
    private readonly TimeSpan m_monitoringWindow;
    
    public LeaseSecurityMonitor(TimeSpan monitoringWindow)
    {
        m_metrics = new Dictionary<string, SecurityMetric>();
        m_monitoringWindow = monitoringWindow;
    }
    
    public void RecordSecurityEvent(string eventType, string clientIP)
    {
        var key = $"{eventType}:{clientIP}";
        if (!m_metrics.ContainsKey(key))
        {
            m_metrics[key] = new SecurityMetric
            {
                EventType = eventType,
                ClientIP = clientIP,
                Count = 0,
                FirstOccurrence = DateTime.UtcNow,
                LastOccurrence = DateTime.UtcNow
            };
        }
        
        var metric = m_metrics[key];
        metric.Count++;
        metric.LastOccurrence = DateTime.UtcNow;
        
        // 检查是否超过阈值
        if (metric.Count > GetThreshold(eventType))
        {
            OnSecurityThresholdExceeded(metric);
        }
    }
    
    public List<SecurityAlert> GetSecurityAlerts()
    {
        var alerts = new List<SecurityAlert>();
        var now = DateTime.UtcNow;
        
        foreach (var metric in m_metrics.Values)
        {
            if (now - metric.FirstOccurrence < m_monitoringWindow)
            {
                if (metric.Count > GetThreshold(metric.EventType))
                {
                    alerts.Add(new SecurityAlert
                    {
                        EventType = metric.EventType,
                        ClientIP = metric.ClientIP,
                        Count = metric.Count,
                        Severity = GetSeverity(metric.EventType, metric.Count),
                        Timestamp = now
                    });
                }
            }
        }
        
        return alerts;
    }
    
    private int GetThreshold(string eventType)
    {
        return eventType switch
        {
            "LeaseCreated" => 100,
            "LeaseBreak" => 50,
            "LeaseAccess" => 200,
            "SecurityViolation" => 10,
            _ => 50
        };
    }
    
    private SecuritySeverity GetSeverity(string eventType, int count)
    {
        return eventType switch
        {
            "SecurityViolation" when count > 20 => SecuritySeverity.Critical,
            "SecurityViolation" when count > 10 => SecuritySeverity.High,
            "LeaseBreak" when count > 100 => SecuritySeverity.Medium,
            "LeaseAccess" when count > 500 => SecuritySeverity.Low,
            _ => SecuritySeverity.Low
        };
    }
    
    private void OnSecurityThresholdExceeded(SecurityMetric metric)
    {
        // 触发安全警报
        Console.WriteLine($"Security threshold exceeded: {metric.EventType} from {metric.ClientIP}");
    }
}

public class SecurityMetric
{
    public string EventType { get; set; }
    public string ClientIP { get; set; }
    public int Count { get; set; }
    public DateTime FirstOccurrence { get; set; }
    public DateTime LastOccurrence { get; set; }
}

public class SecurityAlert
{
    public string EventType { get; set; }
    public string ClientIP { get; set; }
    public int Count { get; set; }
    public SecuritySeverity Severity { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum SecuritySeverity
{
    Low,
    Medium,
    High,
    Critical
}
```

## 安全最佳实践

### 1. 开发阶段

#### 1.1 安全编码实践
- 使用安全的随机数生成器
- 验证所有输入参数
- 使用加密存储敏感数据
- 实现适当的错误处理
- 避免在日志中记录敏感信息

#### 1.2 代码审查
- 检查租赁键生成的安全性
- 验证访问控制实现
- 检查缓存数据保护
- 审查网络通信安全
- 验证错误处理机制

### 2. 部署阶段

#### 2.1 安全配置
- 启用所有安全功能
- 配置适当的超时时间
- 设置合理的访问控制策略
- 启用审计日志记录
- 配置安全监控

#### 2.2 环境安全
- 使用安全的网络传输
- 配置防火墙规则
- 启用入侵检测系统
- 定期更新安全补丁
- 监控系统日志

### 3. 运维阶段

#### 3.1 安全监控
- 监控租赁活动
- 检查异常访问模式
- 分析安全日志
- 响应安全警报
- 定期安全评估

#### 3.2 安全维护
- 定期更新安全策略
- 清理过期租赁
- 轮换加密密钥
- 备份安全配置
- 测试安全功能

## 安全测试

### 1. 安全测试用例

```csharp
[TestClass]
public class LeaseSecurityTests
{
    [TestMethod]
    public void LeaseKey_Uniqueness_Secure()
    {
        // 测试租赁键的唯一性
        var generator = new SecureLeaseKeyGenerator();
        var keys = new HashSet<Guid>();
        
        for (int i = 0; i < 10000; i++)
        {
            var key = generator.GenerateSecureLeaseKey();
            Assert.IsTrue(keys.Add(key), "Lease key must be unique");
        }
    }
    
    [TestMethod]
    public void LeaseKey_Predictability_Secure()
    {
        // 测试租赁键的不可预测性
        var generator = new SecureLeaseKeyGenerator();
        var keys = new List<Guid>();
        
        for (int i = 0; i < 1000; i++)
        {
            keys.Add(generator.GenerateSecureLeaseKey());
        }
        
        Assert.IsTrue(IsRandom(keys), "Lease keys must be random");
    }
    
    [TestMethod]
    public void CacheData_Encryption_Secure()
    {
        // 测试缓存数据加密
        var cache = new SecureLeaseCache();
        var testData = new byte[] { 1, 2, 3, 4, 5 };
        var fileId = new FileID { Volatile = 1, Persistent = 1 };
        
        cache.CacheFileData(fileId, testData, new LeaseInfo());
        var cachedData = cache.GetCachedData(fileId);
        
        Assert.AreEqual(testData, cachedData, "Cached data must match original");
    }
    
    [TestMethod]
    public void AccessControl_UnauthorizedAccess_Denied()
    {
        // 测试未授权访问控制
        var controller = new LeaseAccessController(new MockFileSystemAccessControl());
        var leaseKey = Guid.NewGuid();
        var filePath = "/test/file.txt";
        var sessionId = 12345UL;
        
        controller.RegisterLeaseAccess(leaseKey, filePath, AccessMask.GENERIC_READ, sessionId);
        
        // 尝试使用不同的会话ID访问
        var hasAccess = controller.CheckLeaseAccess(leaseKey, filePath, AccessMask.GENERIC_READ, 99999);
        Assert.IsFalse(hasAccess, "Unauthorized access should be denied");
    }
    
    private bool IsRandom(List<Guid> guids)
    {
        // 简单的随机性检查
        var bytes = guids.SelectMany(g => g.ToByteArray()).ToArray();
        var entropy = CalculateEntropy(bytes);
        return entropy > 7.5; // 高熵值表示随机性
    }
    
    private double CalculateEntropy(byte[] data)
    {
        var frequencies = new Dictionary<byte, int>();
        foreach (var b in data)
        {
            frequencies[b] = frequencies.GetValueOrDefault(b, 0) + 1;
        }
        
        double entropy = 0;
        foreach (var freq in frequencies.Values)
        {
            double probability = (double)freq / data.Length;
            entropy -= probability * Math.Log2(probability);
        }
        
        return entropy;
    }
}
```

### 2. 渗透测试

#### 2.1 租赁键攻击测试
- 尝试预测租赁键
- 测试租赁键重放攻击
- 验证租赁键绑定检查

#### 2.2 缓存攻击测试
- 测试缓存数据泄露
- 验证缓存完整性检查
- 测试缓存投毒攻击

#### 2.3 网络攻击测试
- 测试租赁中断消息伪造
- 验证租赁上下文篡改
- 测试会话劫持攻击

## 总结

本文档详细分析了 SMB 2.0/2.1 租赁协议实现中的安全风险和防护措施，包括：

1. **全面的威胁分析**: 识别了租赁键、缓存数据、网络通信和访问控制等方面的安全威胁
2. **具体的防护措施**: 提供了针对各种威胁的具体防护实现
3. **安全配置**: 详细的安全配置选项和策略
4. **安全审计**: 完整的审计日志和监控机制
5. **最佳实践**: 开发、部署和运维阶段的安全最佳实践
6. **安全测试**: 全面的安全测试用例和渗透测试方案

通过遵循本文档的安全建议，可以确保租赁协议实现的安全性，保护系统免受各种安全威胁的攻击。
