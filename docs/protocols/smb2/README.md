# SMB2 协议文档

本目录包含 SMB2/SMB3 协议的详细文档和实现说明。

## 📚 文档导航

### 核心协议文档

#### SMB2 Create 相关
- **[smb2-create-contexts.md](smb2-create-contexts.md)** - SMB2 Create Contexts 完整参考
  - MxAc (Maximum Access)
  - QFid (Query File ID)
  - RqLs (Request Lease)
  - DH2Q (Durable Handle V2)
  - 等其他上下文

#### SMB2 协商
- **[smb2-negotiate-capabilities.md](smb2-negotiate-capabilities.md)** - SMB2 协商能力和版本

#### SMB2 租约（Leasing）
- **[smb2-lease-protocol.md](smb2-lease-protocol.md)** - SMB2 租约协议概述
- **[rqls-lease-context-explained.md](rqls-lease-context-explained.md)** - RqLs 租约上下文详解 ⭐
  - 租约请求/响应机制
  - LeaseKey 生成和使用
  - LeaseState 和 LeaseFlags
  - 实现指南和代码示例
  - **基于真实 Wireshark 抓包数据验证**

### 实现指南

#### FileID 相关
- **[file-id-persistent-volatile.md](file-id-persistent-volatile.md)** - FileID 的 Persistent 和 Volatile 字段
  - Persistent ID 实现建议
  - 文件重命名/覆盖场景分析
  - QFid 协议支持
  - LeaseManager 依赖分析

- **[file-index-number-explained.md](file-index-number-explained.md)** - Windows File Index Number 详解
  - IndexNumber 特性
  - 与 Persistent ID 的关系
  - 实现建议

### 抓包数据分析

这些文档基于真实的 Windows 客户端 ↔ Windows 服务器抓包数据，提供最可靠的协议行为验证。

- **[packet-captures/WIRESHARK_PACKET_ANALYSIS.md](packet-captures/WIRESHARK_PACKET_ANALYSIS.md)** - Wireshark 文本格式抓包分析
  - 租约请求/响应数据包结构
  - 客户端和服务器行为验证
  - 关键字段分析

- **[packet-captures/LEASE_KEY_ANALYSIS.md](packet-captures/LEASE_KEY_ANALYSIS.md)** - JSON 格式抓包深度分析 ⭐
  - LeaseKey 生成机制验证
  - 租约层次结构（LEASE_V2）
  - 租约降级示例
  - Epoch 版本控制
  - 租约中断流程

## 🎯 快速开始

### 如果你想了解...

**租约（Leasing）机制**：
1. 先阅读 [smb2-lease-protocol.md](smb2-lease-protocol.md) 了解概念
2. 然后阅读 [rqls-lease-context-explained.md](rqls-lease-context-explained.md) 了解实现
3. 最后参考 [packet-captures/LEASE_KEY_ANALYSIS.md](packet-captures/LEASE_KEY_ANALYSIS.md) 查看真实抓包数据

**FileID 实现**：
1. 阅读 [file-id-persistent-volatile.md](file-id-persistent-volatile.md)
2. 参考 [file-index-number-explained.md](file-index-number-explained.md) 了解 Windows 实现

**Create Contexts**：
1. 直接查看 [smb2-create-contexts.md](smb2-create-contexts.md)

## 🔬 验证方法

本项目的所有租约相关文档都基于真实的 **Wireshark 抓包数据**验证：

1. **文本格式抓包** (`windows.txt`) - 人类可读的抓包输出
2. **JSON 格式抓包** (`leasing/windows.json`) - 详细的字段分析

这确保了我们的实现**完全符合 Windows 的实际行为**，没有任何猜测。

## 📂 文档组织

```
docs/protocols/smb2/
├── README.md (本文件) ⭐ 从这里开始
│
├── 核心协议
│   ├── smb2-create-contexts.md
│   ├── smb2-negotiate-capabilities.md
│   └── smb2-lease-protocol.md
│
├── 租约详解
│   └── rqls-lease-context-explained.md ⭐
│
├── FileID 实现
│   ├── file-id-persistent-volatile.md
│   └── file-index-number-explained.md
│
└── packet-captures/ (抓包验证)
    ├── README.md
    ├── WIRESHARK_PACKET_ANALYSIS.md
    ├── LEASE_KEY_ANALYSIS.md ⭐
    └── leasing/
        └── windows.json
```

## 🆕 最新更新

- **2025-01-10**: 基于 Wireshark 抓包数据完全重写租约文档
- **2025-01-10**: 添加 LeaseKey 详细分析
- **2025-01-10**: 验证客户端生成 LeaseKey 的机制
- **2025-01-10**: 添加租约层次结构（LEASE_V2）分析
- **2025-01-10**: 添加租约降级和 Epoch 版本控制说明

## ⭐ 推荐阅读顺序

### 初学者
1. `smb2-lease-protocol.md` - 理解租约概念
2. `rqls-lease-context-explained.md` - 学习实现
3. `file-id-persistent-volatile.md` - 理解 FileID

### 开发者
1. `rqls-lease-context-explained.md` - 实现指南
2. `LEASE_KEY_ANALYSIS.md` - 查看真实抓包
3. `smb2-create-contexts.md` - 全面参考

### 调试问题
1. `packet-captures/WIRESHARK_PACKET_ANALYSIS.md` - 对比抓包
2. `packet-captures/LEASE_KEY_ANALYSIS.md` - 检查字段值
3. `rqls-lease-context-explained.md` - 查看常见问题

---

**维护者**: SMBLibrary 项目  
**最后更新**: 2025-01-10  
**基于**: Windows 10/11 真实抓包数据

