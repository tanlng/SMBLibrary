# SMB2 租约（Lease）机制文档

本目录包含 SMB2/SMB3 租约机制的完整文档，包括协议规范、架构设计、实现指南和抓包分析。

## 📚 文档索引

### 📖 规范与协议

1. **[lease-protocol-specification.md](lease-protocol-specification.md)** - SMB 2.0/2.1 租赁协议规范
   - 租赁协议的基本概念
   - 租赁类型和状态
   - 数据结构定义
   - 工作流程说明

2. **[rqls-context-explained.md](rqls-context-explained.md)** - RqLs (Request Lease) 上下文详解
   - RqLs 上下文的作用和结构
   - 客户端如何请求租约（基于 Wireshark 抓包验证）
   - 服务器如何响应租约
   - 实现建议和检查清单

### 🏗️ 架构与设计

3. **[lease-architecture.md](lease-architecture.md)** - SMB 2.0/2.1 租赁协议架构设计
   - 整体架构和组件设计
   - 数据流和状态管理
   - 存储设计和性能优化
   - 安全考虑和扩展性设计

### 🔍 抓包分析

4. **[lease-key-detailed-analysis.md](lease-key-detailed-analysis.md)** - LeaseKey 详细分析
   - 基于 Windows 客户端 ↔ Windows 服务器的 JSON 抓包数据
   - LeaseKey 由客户端生成的证明
   - 租约层次结构分析
   - LeaseState 和 LeaseFlags 详解
   - 租约中断（Lease Break）流程

5. **[rqls-context-wireshark-analysis.md](rqls-context-wireshark-analysis.md)** - Wireshark 抓包数据分析
   - 真实抓包数据的协议行为验证
   - 客户端请求租约的完整流程
   - 服务器响应租约的详细分析
   - 纠正常见误解

## 🎯 文档导航

### 新手入门路径
1. 阅读 **lease-protocol-specification.md** 了解基本概念
2. 阅读 **rqls-context-explained.md** 理解 RqLs 上下文
3. 查看 **rqls-context-wireshark-analysis.md** 验证理解

### 实现开发路径
1. 阅读 **lease-architecture.md** 了解架构设计
2. 阅读 **rqls-context-explained.md** 的实现建议
3. 参考 **lease-key-detailed-analysis.md** 理解 LeaseKey 机制

### 问题排查路径
1. 查看 **rqls-context-wireshark-analysis.md** 对比抓包数据
2. 参考 **lease-key-detailed-analysis.md** 的租约中断分析
3. 查阅 **lease-protocol-specification.md** 的错误处理章节

## 🔑 核心概念速查

### 租约（Lease）
租约是服务器授予客户端的一种缓存权限，允许客户端在本地缓存文件数据以减少网络往返。

### 租约状态（LeaseState）
- **R** (0x01) - 读取缓存（Read Caching）
- **H** (0x02) - 句柄缓存（Handle Caching）
- **W** (0x04) - 写入缓存（Write Caching）
- **RH** (0x03) - 读取 + 句柄
- **RW** (0x05) - 读取 + 写入
- **RWH** (0x07) - 完全租约（最高性能）

### 租约键（LeaseKey）
- **由客户端生成**的 128 位 GUID
- 用于唯一标识一个租约
- 支持租约层次结构（通过 ParentLeaseKey）

### RqLs 上下文
- **双向**：客户端请求 + 服务器响应
- 客户端必须同时设置 `RequestedOplockLevel = Lease` 和发送 RqLs 上下文
- 服务器评估后返回 RqLs 响应（可以降级或拒绝）

## ⚠️ 关键发现（基于抓包验证）

1. ✅ **客户端生成 LeaseKey**，不是服务器生成
2. ✅ **客户端发送 RqLs 上下文**，不只是设置 RequestedOplockLevel
3. ✅ **服务器解析 RqLs**，评估后返回响应上下文
4. ✅ **租约可以降级**，服务器可以授予低于请求的级别
5. ✅ **支持租约层次**，通过 ParentLeaseKey 建立父子关系

## 📊 文档分类

### 按文档类型
- **规范文档**: lease-protocol-specification.md, rqls-context-explained.md
- **设计文档**: lease-architecture.md
- **分析文档**: lease-key-detailed-analysis.md, rqls-context-wireshark-analysis.md

### 按主题
- **协议层**: lease-protocol-specification.md, rqls-context-explained.md
- **架构层**: lease-architecture.md
- **验证层**: lease-key-detailed-analysis.md, rqls-context-wireshark-analysis.md

## 🔗 相关文档

### 项目文档
- [SMB2 协议文档](../README.md) - SMB2 协议总览
- [SMB2 Create 上下文](../smb2-create-contexts.md) - Create 上下文详解
- [File ID 说明](../file-id-persistent-volatile.md) - File ID 的持久化和易失性

### 外部参考
- [MS-SMB2] 2.2.13.2.8 - SMB2_CREATE_REQUEST_LEASE
- [MS-SMB2] 2.2.14.2.8 - SMB2_CREATE_RESPONSE_LEASE
- [MS-SMB2] 2.2.23.1 - SMB2_LEASE_BREAK_NOTIFICATION

## 📝 文档贡献

### 文档维护
- 所有文档使用中文编写
- 代码示例和注释使用英文
- 遵循项目文档规范

### 更新记录
- **2025-01-10**: 初始文档整理
- **2025-01-10**: 添加基于 Wireshark 抓包的验证分析
- **2025-01-10**: 纠正 LeaseKey 生成机制的误解

## 💡 常见问题

### Q: 客户端如何请求租约？
**A**: 客户端需要同时：
1. 设置 `RequestedOplockLevel = Lease (0xFF)`
2. 在 CreateContexts 中发送 RqLs 上下文（包含 LeaseKey、LeaseState 等）

### Q: LeaseKey 由谁生成？
**A**: **客户端**生成 LeaseKey（已通过 Wireshark 抓包验证）

### Q: 服务器可以拒绝租约吗？
**A**: 是的，服务器可以：
- 拒绝租约（不返回 RqLs 上下文，OplockLevel = None）
- 降级租约（授予低于客户端请求的级别）

### Q: 租约和 Oplock 有什么区别？
**A**: 
- **Oplock** 是传统的缓存机制（SMB 1.0+）
- **Lease** 是改进的缓存机制（SMB 2.1+），提供更细粒度的控制和更好的性能

---

**最后更新**: 2025-01-10  
**维护者**: SMBLibrary 项目团队

