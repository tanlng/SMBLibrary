# SMBLibrary 文档

> 本文档是 SMBLibrary 项目的完整文档索引。

## 🚀 快速开始

### 我想实现权限控制
→ 查看 **[MxAc 完整指南](guides/implementation/mxac-guide.md)** ⭐

### 我想实现 SMB2 租赁
→ 查看 **[RqLs 租约详解](protocols/smb2/rqls-lease-context-explained.md)** ⭐  
→ 参考 **[抓包数据分析](protocols/smb2/packet-captures/LEASE_KEY_ANALYSIS.md)** 验证实现

### 我遇到问题需要调试
→ 查看 **[Wireshark 抓包分析](protocols/smb2/packet-captures/WIRESHARK_PACKET_ANALYSIS.md)**  
→ 对比 **[抓包数据](protocols/smb2/packet-captures/leasing/windows.json)** 检查字段值

---

## 📁 文档结构

```
docs/
├── README.md (本文档)
│
├── architecture/                       # 架构设计
│   └── file-access-architecture.md     # 文件访问架构
│
├── guides/                             # 使用指南
│   ├── getting-started/
│   │   └── lease-usage-guide.md        # Lease 入门
│   ├── implementation/
│   │   ├── mxac-guide.md ⭐            # MxAc 完整指南
│   │   ├── create-contexts-implementation-guide.md
│   │   └── lease-implementation-guide.md
│   ├── testing/
│   │   └── lease-testing-strategy.md
│   └── troubleshooting/
│       └── lease-troubleshooting-guide.md
│
├── protocols/                          # 协议规范
│   └── smb2/
│       ├── README.md                       # SMB2 文档导航 ⭐
│       ├── smb2-create-contexts.md         # Create Contexts 参考
│       ├── smb2-negotiate-capabilities.md  # 协商能力
│       ├── smb2-lease-protocol.md          # 租约协议概述
│       ├── rqls-lease-context-explained.md # RqLs 详解 ⭐
│       ├── file-id-persistent-volatile.md  # FileID 实现 ⭐
│       ├── file-index-number-explained.md  # File Index Number
│       ├── packet-captures/                # 抓包数据验证
│       │   ├── README.md
│       │   ├── WIRESHARK_PACKET_ANALYSIS.md
│       │   ├── LEASE_KEY_ANALYSIS.md ⭐
│       │   └── leasing/
│       │       └── windows.json            # 原始抓包数据
│       └── leasing/
│           └── smb2-lease-architecture.md
│
├── api/                                # API 文档
│   └── server/
│       ├── create-contexts/
│       │   └── create-contexts-api.md
│       └── leasing/
│           ├── lease-manager-api.md
│           ├── lease-context-handler-api.md
│           ├── lease-break-handler-api.md
│           └── smb2-lease-commands-api.md
│
├── performance/
│   └── lease-performance-optimization.md
│
└── security/
    └── lease-security-considerations.md
```

**总计**: 21 个文档

---

## 📚 按类型查找

### 架构文档
- **[文件访问架构](architecture/file-access-architecture.md)** - INTFileStore、IFileSystem、ISMBShare 接口说明

### 实现指南
- **[MxAc 完整指南](guides/implementation/mxac-guide.md)** ⭐ - MxAc 权限控制（含 7+ 示例）
- **[Create Contexts 实现指南](guides/implementation/create-contexts-implementation-guide.md)** - SMB2 Create Contexts
- **[Lease 实现指南](guides/implementation/lease-implementation-guide.md)** - SMB2/3 Lease 租赁

### 协议规范
- **[FileID: Persistent 和 Volatile](protocols/smb2/file-id-persistent-volatile.md)** ⭐ - FileID 实现指南
- **[IndexNumber 详解](protocols/smb2/file-index-number-explained.md)** ⭐ - 文件索引号（类似 Unix inode）
- **[RqLs 租约上下文详解](protocols/smb2/rqls-lease-context-explained.md)** ⭐ - RqLs 工作机制和实现
- **[SMB2 Create Contexts](protocols/smb2/smb2-create-contexts.md)** - Create Contexts 协议
- **[SMB2 Negotiate Capabilities](protocols/smb2/smb2-negotiate-capabilities.md)** - 协商能力
- **[SMB2 Lease Protocol](protocols/smb2/smb2-lease-protocol.md)** - 租赁协议
- **[SMB2 Lease Architecture](protocols/smb2/leasing/smb2-lease-architecture.md)** - 租赁架构

### API 文档
- **[Create Contexts API](api/server/create-contexts/create-contexts-api.md)** - Create Contexts API
- **[Lease Manager API](api/server/leasing/lease-manager-api.md)** - 租赁管理器 API
- **[Lease Context Handler API](api/server/leasing/lease-context-handler-api.md)** - 租赁上下文处理器
- **[Lease Break Handler API](api/server/leasing/lease-break-handler-api.md)** - 租赁中断处理器
- **[SMB2 Lease Commands API](api/server/leasing/smb2-lease-commands-api.md)** - SMB2 租赁命令

### 使用指南
- **[Lease 使用指南](guides/getting-started/lease-usage-guide.md)** - 租赁功能入门

### 测试和调试
- **[Lease 测试策略](guides/testing/lease-testing-strategy.md)** - 租赁功能测试
- **[Lease 故障排除](guides/troubleshooting/lease-troubleshooting-guide.md)** - 租赁问题诊断

### 性能和安全
- **[Lease 性能优化](performance/lease-performance-optimization.md)** - 性能优化指南
- **[Lease 安全考虑](security/lease-security-considerations.md)** - 安全注意事项

---

## 📝 文档说明

### 各目录用途

| 目录 | 用途 | 内容 |
|------|------|------|
| **architecture/** | 架构设计 | 系统整体架构、设计原则、组件关系 |
| **guides/implementation/** | 实现指南 | 详细实现步骤、代码示例、最佳实践 |
| **guides/getting-started/** | 入门指南 | 快速开始、基本用法、常见场景 |
| **protocols/smb2/** | 协议规范 | SMB2/3 协议细节、数据结构、流程 |
| **api/server/** | API 文档 | 接口定义、方法说明、参数返回值 |
| **performance/** | 性能优化 | 性能分析、优化建议、基准测试 |
| **security/** | 安全考虑 | 安全威胁、防护措施、最佳实践 |

### 文档约定

- **中文文档** - 所有 .md 文档使用中文
- **英文代码** - 代码示例和注释使用英文
- **清晰结构** - 使用标题、列表、代码块

### 维护原则

1. **不重复** - 每个主题只有一个权威文档
2. **清晰结构** - 按类型和用途组织
3. **及时更新** - 代码变更时同步更新文档
4. **示例丰富** - 提供实际可运行的代码

---

## 🔗 相关资源

- [SMBLibrary GitHub](https://github.com/TalAloni/SMBLibrary)
- [SMB2 协议规范](https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-smb2/)
- [项目根目录 README](../Readme.md)

---

**最后更新**: 2025-01-09
