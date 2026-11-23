# Release Notes — v2.2.10

> **历史文档（归档）**：本文件是 v2.2.10 发布当时的记录，后续版本见 [`CHANGELOG.md`](../CHANGELOG.md) 与 `docs/development.md` 的版本章节。当前工作树为 v2.3.0（2026-10-23，未打 tag），并含四 IM 渠道与全站中英双语的开源准备批次（见 CHANGELOG `[Unreleased]`）。

**版本**: v2.2.10
**发布日期**: 2026-08-30
**上一版本**: v2.2.9 (2026-08-28)
**代码仓库**: git@<git.example.com>/<org>/noneadcss.git
**分支**: master
**部署目录**: `C:\noneadcss\Production\`
**标签**: `v2.2.10`

---

## 版本概述

v2.2.10 是 NoneadCSS（拓德客户服务与支持系统）在 v2.2.9（系统管理-审计日志完善与优化基础）之上推出的**审计日志完整性签名修复 + 前端中文标签全面补齐版**。本版本聚焦两个问题：

1. **审计日志 HMAC-SHA256 完整性签名失效修复（关键 BUG）**：由于写入时 `CreatedAt` 来自 `DateTime.UtcNow`（`Kind=Utc`，`ToString("O")` 带 `Z` 后缀），EF Core 从 SQLite 读回后 `Kind=Unspecified`（`ToString("O")` 不带 `Z` 后缀），导致写入与校验时生成的签名载荷不一致、HMAC 签名永远无法匹配，单条/批量校验结果始终为"疑似篡改"。通过在 `AuditHmacSigner.BuildSignaturePayload` 中统一按 `Utc` 渲染时间（`DateTime.SpecifyKind` 修正 `Kind`），确保签名与校验载荷一致，签名校验恢复正常；并已对存量 56887 条审计记录执行重签。
2. **审计日志前端统计界面中文标签全面补齐**：`AuditLogs.tsx` 的 `actionStyles` / `entityTypeStyles` 静态映射表覆盖不全，导致「操作分布（全局 Top）」与「实体类型占比」卡片中出现英文原文（如 `calendar.update`、`WorkflowExecutionLog`、`SystemOption` 等）。本次全面枚举后端全部 Action / EntityType 值，补齐映射，未命中时按 `calendar.` / `time_entry.` 前缀兜底，确保中文界面不再出现英文。

---

## 变更内容汇总矩阵

| 优先级 | 模块 | 功能点 | 效果 |
|--------|------|--------|------|
| 🔴 P0 | 后端 · AuditHmacSigner | `BuildSignaturePayload` 统一按 `Utc` 渲染 `CreatedAt` | 修复写入/校验载荷不一致导致 HMAC 永不通的彻底失败问题 |
| 🔴 P0 | 后端 · 数据 | 已存 56887 条审计日志执行体系化重签 | 存量记录签名恢复正常，可被校验通过 |
| ✨ P1 | 前端 · AuditLogs.tsx | `actionStyles` 补齐 50 个 Action 中文映射 | 操作分布卡片全部显示中文 |
| ✨ P1 | 前端 · AuditLogs.tsx | `entityTypeStyles` 补齐 61 个实体类型中文映射（含拼写修正 `calendaremindertattempt`） | 实体类型占比卡片全部显示中文 |
| ✨ P1 | 前端 · AuditLogs.tsx | `calendar.` / `time_entry.` 前缀兜底 + `calendar.batch_*` 精确映射 | 动态日历/时间追踪动作也能正确显示中文 |
| ✨ P1 | 前端 · AuditLogs.tsx | 新增 `Star` 等图标导入配合新增映射 | 每个标签带独立颜色 + 图标，视觉统一 |

---

## 详细变更清单

### 1. 审计日志 HMAC-SHA256 完整性签名修复

#### 1.1 根因

在 [AuditHmacSigner.cs](../NoneadCSS.Infrastructure/Services/AuditHmacSigner.cs) 的 `BuildSignaturePayload` 中，`CreatedAt` 采用 `log.CreatedAt.ToString("O")` 生成签名载荷：

```csharp
return string.Join("|",
    log.UserId?.ToString() ?? "null",
    log.Action,
    log.EntityType,
    log.EntityId?.ToString() ?? "null",
    log.CreatedAt.ToString("O"));
```

问题在于：

- **写入时**：`CreatedAt` 由 `DateTime.UtcNow` 生成，`Kind = Utc`，`ToString("O")` 输出形如 `2026-08-28T12:34:56.7891234Z`（带 `Z` 后缀）。
- **读取时**：EF Core 从 SQLite 读出后 `Kind = Unspecified`，`ToString("O")` 输出形如 `2026-08-28T12:34:56.7891234`（不带 `Z` 后缀）。

由于两个字符串不一致，写入签名与校验签名永远不匹配，导致单条 / 批量校验结果始终显示"疑似篡改"（`ShieldX`），完整性校验功能实质上完全失效。

#### 1.2 修复

统一按 `Utc` 渲染时间，在构建载荷前将 `Kind` 修正为 `Utc`（`DateTime.SpecifyKind` 不偏移时间，只修正 `Kind` 标记）：

```csharp
var createdAt = log.CreatedAt.Kind == DateTimeKind.Utc
    ? log.CreatedAt
    : DateTime.SpecifyKind(log.CreatedAt, DateTimeKind.Utc);
return string.Join("|",
    log.UserId?.ToString() ?? "null",
    log.Action,
    log.EntityType,
    log.EntityId?.ToString() ?? "null",
    createdAt.ToString("O"));
```

修复后写入与校验生成的载荷完全一致，HMAC 签名校验恢复正常，单条 / 批量校验均可正确区分"校验通过"与"疑似篡改"。

#### 1.3 存量数据重签

对历史存量审计日志执行体系化重签（`audit_resign` 工具），共 **56887** 条记录完成重签，签名均基于修复后的载荷重新生成，保证存量记录可被校验通过（绿色 ShieldCheck）。

#### 1.4 影响文件

- `NoneadCSS.Infrastructure/Services/AuditHmacSigner.cs`（`BuildSignaturePayload` 统一 Utc 渲染）
- 审计日志存量数据（重签）

---

### 2. 审计日志前端中文标签全面补齐

#### 2.1 背景

`AuditLogs.tsx` 中 `actionStyles` / `entityTypeStyles` 为静态映射表，按小写 key 匹配中文标签 + 颜色 + 图标；未命中时通过 `?? { label: action, ... }` 兜底回退显示原始英文字符串。由于映射表覆盖不全，「操作分布（全局 Top 8）」与「实体类型占比」卡片会出现英文原文，例如：

- 操作分布：`calendar.update`（667）、`calendar.create`（105）等。
- 实体类型占比：`WorkflowExecutionLog`（12%）、`SystemOption`（5%）等。

这违背了中文界面不出现英文的预期。

#### 2.2 修复内容

对后端全部 Action / EntityType 值做全面审计（覆盖 EF Core 拦截器、MVC ActionFilter、显式服务/监听器三条写入路径），补齐映射：

- **Action 映射（共 50 个）**：
  - 新增 `calendar.*`（约 11 条，含 `calendar.create` / `calendar.update` / `calendar.delete` 等）。
  - 新增 `time_entry.*`（6 条）。
  - 新增 `restore` / `hard_delete` / `bulk_*` 等动作。
  - 新增 `calendar.batch_*`（8 条）精确映射。
  - 新增 `calendar.` / `time_entry.` 前缀兜底逻辑，动态动作也能正确显示中文。
- **实体类型映射（共 61 个）**：
  - 修正拼写错误 `calendaremindertttempt` → `calendaremindertattempt`（`CalendarReminderAttempt`，标签"日历提醒尝试"）。
  - 新增 `workflowexecutionlog`（工作流执行日志）、`systemoption`（系统选项）、`slapolicy`（SLA策略）等。
  - 涵盖日历、时间追踪、知识库、报表、IM 渠道等全实体类型。
- **图标导入**：新增 `Star` 等图标配合新增映射，每个标签统一「中文 + 颜色 + 图标」呈现。

#### 2.3 影响文件

- `frontend/src/pages/AuditLogs.tsx`（`actionStyles` / `entityTypeStyles` 映射表 + 前缀兜底 + 图标导入）

---

## 影响文件清单

### 后端（1）

- [NoneadCSS.Infrastructure/Services/AuditHmacSigner.cs](../NoneadCSS.Infrastructure/Services/AuditHmacSigner.cs)（`BuildSignaturePayload` 统一 Utc 渲染）

### 前端（1）

- [frontend/src/pages/AuditLogs.tsx](../frontend/src/pages/AuditLogs.tsx)（补齐 50 个 Action + 61 个实体类型中文映射 + 前缀兜底 + 图标导入）

### 文档（5）

- [README.md](../README.md)（版本升至 v2.2.10 + 补充完整性签名修复与中文翻译描述）
- [CHANGELOG.md](../CHANGELOG.md)（新增 v2.2.10 完整变更记录）
- [docs/development.md](../docs/development.md)（版本升至 2.2.10 + 新增审计日志签名修复与前端标签补齐章节）
- [docs/拓德客户服务与支持系统用户使用手册.md](../docs/拓德客户服务与支持系统用户使用手册.md)（版本升至 2.2.10 + 14.1 节补充签名修复说明与中文界面说明）
- [docs/ReleaseNotes_v2.2.10.md](../docs/ReleaseNotes_v2.2.10.md)（本文档）

---

## 架构改进沉淀

1. **时间 `Kind` 一致性约束**：凡参与 HMAC/哈希签名或幂等键构建的 `DateTime` 字段，必须统一 `Kind`（建议统一为 `Utc`），避免写入（`UtcNow`）与读取（`Unspecified`）时 `ToString("O")` 输出不一致导致签名永不通。该约束应作为所有签名类功能的编码规范。
2. **前端 → 后端值域全量映射**：凡前端以静态映射表展示后端枚举/字符串值的场景，必须以后端可见值域全集为准补齐映射，并保留前缀/兜底逻辑，避免新旧数据出现英文回退。
3. **历史数据体系化重签**：修复签名算法后，需对存量数据执行体系化重签，而非仅修复新写入路径，保证新旧记录行为一致。

---

## 部署验证

v2.2.10 已编译并部署至生产环境（`Production\backend` + `Production\frontend\dist`）：

- **后端**: `dotnet NoneadCSS.Api.dll --urls http://127.0.0.1:5009`（端口 5009）
- **前端**: `vite preview --port 5172 --host 127.0.0.1`（强制 IPv4）
- **Nginx**: 5173 反向代理（5173 → 5172, /api → :5009）
- **验证**: 审计日志 `/api/auditlogs/{id}/verify` 与 `/api/auditlogs/verify-integrity` 返回正常；重签后存量记录校验通过（绿色 ShieldCheck）；审计日志页面操作分布 / 实体占比栏目不再出现英文。
- **数据库**: SQLite `Production/data/noneadcss.db`（审计日志记录重签，无 schema 变更）。

---

## 兼容性

- **数据库**: 无 schema 变更，v2.2.5+ 数据库可直接升级；审计日志历史记录已重签。
- **API**: 无新增/变更端点，现有端点完全向后兼容。
- **前端**: 仅扩展映射表与图标导入，不影响现有列表浏览与交互。
- **权限**: 继承 v2.2.9 的 `RequireSuperAdmin` 鉴权策略，审计日志仅超级管理员可访问。

---

*版本 v2.2.10 · 2026-08-30 · 审计日志完整性签名修复（CreatedAt Utc 渲染 + 56887 条重签）+ 前端中文标签全面补齐（50 Action / 61 实体类型 + 前缀兜底）*
