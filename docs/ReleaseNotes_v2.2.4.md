# Release Notes — v2.2.4

> **历史文档（归档）**：本文件是 v2.2.4 发布当时的记录，后续版本见 [`CHANGELOG.md`](../CHANGELOG.md) 与 `docs/development.md` 的版本章节。当前工作树为 v2.3.0（2026-10-23，未打 tag），并含四 IM 渠道与全站中英双语的开源准备批次（见 CHANGELOG `[Unreleased]`）。

**版本**: v2.2.4
**发布日期**: 2026-08-25
**上一版本**: v2.2.3 (2026-08-17)
**代码仓库**: git@<git.example.com>/<org>/noneadcss.git
**分支**: master
**部署目录**: `C:\noneadcss\Production\`
**标签**: `v2.2.4`

---

## 版本概述

v2.2.4 是在 v2.2.3（乐观锁 + 生产 SOP 固化）基础上的**日历模块稳定性与类型安全加固版**，聚焦四条主线：

1. **日历多轮稳定性修复**：连续多轮回归修复色块重复 / 拖留、跨日拖拽偏移、删除冲突色块残留、undo/redo 回滚、SignalR 重连刷新、垃圾箱/订阅面板真实 API 对接、重复事件展开等问题。
2. **日历编辑器功能增强**：提醒类型选择（站内/邮件/短信）、重复规则中文描述实时预览、与会者邮箱格式校验与清空、自定义提醒时间换算提示、工作日快捷按钮。
3. **P3 前端类型安全加固**：消除 39 处 `as any` / `: any` 类型断言，新增错误处理助手函数，定义 ConflictItem 接口，引入 JSON.parse 类型守卫。
4. **缓存生命周期与显示修复**：MemoryCalendarQueryCache 由 Scoped 改为 Singleton 解决缓存失效问题；色块悬停弹窗显示创建者姓名。

累计：
- **前端组件**：日历 store / chips / Calendar / EditorPanel / ListView / MonthView / SubscriptionPanel / TrashPanel / calendarUtils / signalR
- **后端基础设施**：DependencyInjection.cs（MemoryCalendarQueryCache 生命周期）、CalendarController.cs（订阅 URL 端点）
- **类型安全**：新增 `getErrorMessage` / `getErrorStatus` / `getErrorData` 通用错误处理助手

---

## 变更内容汇总矩阵

| 优先级 | 模块 | 功能点 | 效果 |
|--------|------|--------|------|
| 🐛 P0 | 前端 · store | `optimisticUpsertEvent` 复合键修复 | 拖拽后不再出现重复色块 |
| 🐛 P0 | 前端 · store | `setAllEvents` 增量合并 + 乐观保护跳过删除修复 | 删除事件后色块立即消失 |
| 🐛 P0 | 前端 · store | `moveEventByDrop` 误用父事件 `startAt` 修复 | 跨日拖拽后事件位置正确 |
| 🐛 P0 | 前端 · TrashPanel | mock 实现改为调用真实后端 API | 垃圾箱功能可用 |
| 🐛 P0 | 前端 · SubscriptionPanel | mock URL fallback 改为拉取真实 URL | 订阅链接功能可用 |
| 🐛 P1 | 前端 · store | undo/redo 失败未回滚乐观修改 | 失败自动恢复到修改前 |
| 🐛 P1 | 前端 · Calendar | SignalR 重连后未触发数据刷新 | 重连期间消息不丢失 |
| 🐛 P1 | 前端 · store | resize 事件未处理 recurrence | 重复事件拖拽尺寸正确 |
| 🐛 P1 | 前端 · store | `PendingPatch` 中 `endAt` 比较错误 | 乐观更新偏移量正确 |
| 🐛 P1 | 前端 · Calendar | 删除分支未清理 `_optimisticMarkers` | 删除后保护位清理 |
| ✨ | 前端 · EditorPanel | 提醒类型选择（站内/邮件/短信） | 多通道提醒支持 |
| ✨ | 前端 · EditorPanel | RRULE 中文描述实时预览 | 重复规则可读性提升 |
| ✨ | 前端 · EditorPanel | 与会者邮箱格式校验 + 清空按钮 | 与会者管理体验提升 |
| ✨ | 前端 · EditorPanel | 工作日快捷按钮（工作日/每天/清除） | 重复规则配置便捷 |
| ✨ | 前端 · chips | 色块悬停弹窗显示创建者 | 色块信息完整 |
| 🔧 | 后端 · DI | `MemoryCalendarQueryCache` → Singleton | 缓存失效问题修复 |
| 🔧 | 后端 · CalendarController | 新增订阅 URL 端点 | 替代前端 mock URL |
| 🔧 P3 | 前端 · 多文件 | 39 处 `as any` / `: any` 消除 | TypeScript 严格类型安全 |
| 🔧 P3 | 前端 · store | 新增 `getErrorMessage` / `getErrorStatus` / `getErrorData` | 通用错误处理助手 |
| 🔧 P3 | 前端 · MonthView | 移除 2 处 `(c: any)` 类型断言 | 利用 TS 自动类型推断 |

---

## 详细修复清单

### 1. 日历模块多轮稳定性修复

#### 1.1 拖拽色块重复 + 原位置残留（P0）

**问题现象**：拖拽日历事件到新位置后，新位置出现两条相同色块，原位置色块残留，刷新后消失。

**根因**：[frontend/src/pages/calendar/store.ts](../frontend/src/pages/calendar/store.ts) `optimisticUpsertEvent` 使用复合键（`id#recurrenceOriginalStart` 或 `id`），但后端返回的父事件 DTO 中 `recurrenceOriginalStart` 为 null，导致键不匹配（`id` vs `id#originalStart`），新事件被追加而非替换。

**修复**：在所有 `optimisticUpsertEvent` 调用处，用被操作实例的 `recurrenceOriginalStart` 覆盖后端 DTO 的对应字段，确保复合键一致。涉及 `store.ts`（drag 和 resize）、`ListView.tsx`（quick update）、`EditorPanel.tsx`（save）。

#### 1.2 删除冲突事件色块残留（P0）

**问题现象**：删除冲突事件后色块仍显示，刷新后才消失。

**根因**：`setAllEvents` 使用增量合并保留了旧事件；乐观保护窗口跳过了删除推送。

**修复**：删除分支在 `onChanged` 中直接更新 state，移除 2 秒延迟立即 `signalRRev + 1`；批量删除时手动过滤已删除事件 ID；undo/redo 的 `signalRRev` 同步移除 2 秒延迟。

#### 1.3 跨日期拖拽日程位置错误（P0）

**问题现象**：跨日期拖拽日程后事件出现在错误位置。

**根因**：[frontend/src/pages/calendar/store.ts](../frontend/src/pages/calendar/store.ts) `moveEventByDrop` 中两处使用父事件 DTO 的 `startAt` 而非被拖拽实例的 `newStartIso`：一是 `optimisticUpsertEvent(resWithKey)` 用父事件 `startAt` 覆盖被拖拽实例导致重叠；二是 `upsertPendingPatch` 中 `patch.startAt` 使用父事件起始时间导致 `deltaMs` 计算错误。

**修复**：保留乐观更新的 `startAt: newStartIso` 和 `endAt: newEndIso`，仅从 `res` 取元数据；将 `patch.startAt` 改为 `newStartIso` 以确保正确计算偏移量。

#### 1.4 TrashPanel 与 SubscriptionPanel mock 实现（P0）

**问题现象**：TrashPanel 的恢复/永久删除按钮为 mock 实现，SubscriptionPanel 的"重新生成链接"为 mock URL，均不调用后端 API。

**修复**：
- TrashPanel：改用 store 的 async 方法（`restoreEvent` / `purgeEvent` / `emptyTrash`）调用真实后端 API。
- SubscriptionPanel：新增后端订阅 URL 端点，store 的 `openSubscription` action 拉取真实 URL；移除 mock fallback，增加空状态和加载态。

#### 1.5 undo/redo 失败未回滚乐观修改（P1）

**问题现象**：undo/redo 操作失败时，乐观修改未回滚，导致 UI 与后端状态不一致。

**修复**：在 try 块开始处保存 `prevAllEvents / prevEventsByDate / prevList / prevOverview / prevTrashCount / prevMarkers`，catch 块中恢复这些快照。

#### 1.6 SignalR 重连后消息丢失（P1）

**问题现象**：SignalR 重连后未触发数据刷新，重连期间的消息推送丢失。

**修复**：在 `frontend/src/pages/calendar/signalR.ts` 的 `onreconnected` 回调中调用 `notifyListeners('_reconnected', null)`；在 `Calendar.tsx` 中注册 `_reconnected` listener 触发 `signalRRev + 1` 全量刷新。同时新增 `visibilitychange` listener，页面可见时若连接已断开则自动重连。

#### 1.7 重复事件展开（P0）

**问题现象**：9月16日创建每月重复、4 次后结束的日程，未显示 10/16、11/16、12/16 的重复色块。

**根因**：RRULE 生成不可靠，前端未自动从表单数据生成最新 RRULE。

**修复**：保存时自动从表单数据生成标准 RRULE；UI 预览分离为中文描述，通过 `generateRRuleDescription` 实时生成。

### 2. 日历编辑器功能增强

#### 2.1 提醒类型选择

新建/编辑事件时，可在「提醒设置」区域选择提醒渠道：

| 提醒类型 | 说明 | 适用场景 |
|---------|------|---------|
| 站内通知 | 推送给负责人和参会人 | 日常协作，无需打扰外部 |
| 邮件通知 | 发送到所有收件人邮箱 | 跨团队、跨时区的重要事项 |
| 短信通知 | 发送到预留手机号 | 关键 SLA、紧急任务到期 |

切换提醒类型时自动清空原有 `reminderSettings`，防止旧配置残留。选择邮件提醒但未添加与会者时显示警告提示。

#### 2.2 重复规则中文描述实时预览

[frontend/src/pages/calendar/panels/EditorPanel.tsx](../frontend/src/pages/calendar/panels/EditorPanel.tsx) 通过 `generateRRuleDescription` 实时生成中文描述，例如：
- 每日重复：`每 1 天重复`
- 每周重复 + 工作日：`每 1 周重复，周一至周五`
- 每月重复 4 次：`每 1 月重复，共 4 次`
- 每年重复到指定日期：`每 1 年重复，至 2026-12-31 结束`

保存时自动从表单数据生成标准 RRULE，UI 预览与数据生成分离。

#### 2.3 与与会者管理增强

- 邮箱格式校验：使用正则 `/^[^\s@]+@[^\s@]+\.[^\s@]+$/` 校验，区分"全部无效"和"部分无效"提示
- 清空按钮：显示"N 位与会者已添加"，一键清空
- 添加后保持焦点：通过 `attendeeInputRef` 和 `requestAnimationFrame` 实现连续添加体验
- 与会者邮箱列表与 `reminderEmails` 字段同步（CSV 序列化/反序列化）

#### 2.4 工作日快捷按钮

重复规则选择"每周"时显示快捷按钮：
- 工作日：选中周一至周五
- 每天：选中周一至周日
- 清除：清空所有星期选择

### 3. P3 前端类型安全加固

**范围**：[store.ts](../frontend/src/pages/calendar/store.ts)、[EditorPanel.tsx](../frontend/src/pages/calendar/panels/EditorPanel.tsx)、[ListView.tsx](../frontend/src/pages/calendar/views/ListView.tsx)、[SubscriptionPanel.tsx](../frontend/src/pages/calendar/panels/SubscriptionPanel.tsx)、[MonthView.tsx](../frontend/src/pages/calendar/views/MonthView.tsx) 共 39 处 `as any` / `: any`。

**新增错误处理助手**（store.ts）：

```ts
export function getErrorMessage(error: unknown): string | undefined {
  if (error instanceof Error) return error.message;
  if (typeof error === 'string') return error;
  if (error && typeof error === 'object' && 'message' in error) {
    const msg = (error as Record<string, unknown>).message;
    if (typeof msg === 'string') return msg;
  }
  return undefined;
}

export function getErrorStatus(error: unknown): number | undefined {
  if (error && typeof error === 'object' && 'status' in error) {
    const s = (error as Record<string, unknown>).status;
    if (typeof s === 'number') return s;
  }
  return undefined;
}

export function getErrorData<T = unknown>(error: unknown): T | undefined {
  if (error && typeof error === 'object' && 'data' in error) {
    return (error as Record<string, unknown>).data as T;
  }
  return undefined;
}
```

**其他类型安全改进**：
- 定义 `ConflictItem` 接口替代内联 `as any` 类型断言
- 新增 `toCreateRequest` 函数生成类型安全的请求数据
- `JSON.parse` 结果使用类型守卫校验
- `MonthView.tsx` 移除 2 处 `(c: any)` 类型断言，利用 `ConflictCheckResult` 自动类型推断

### 4. 缓存生命周期与显示修复

#### 4.1 MemoryCalendarQueryCache Singleton 修复

**问题现象**：删除事件后刷新页面色块重新出现。

**根因**：[NoneadCSS.Infrastructure/DependencyInjection.cs](../NoneadCSS.Infrastructure/DependencyInjection.cs) 中 `MemoryCalendarQueryCache` 注册为 Scoped，其 `_userVersions` 状态无法跨请求持久化，导致缓存失效失败。

**修复**：将注册改为 Singleton。

#### 4.2 色块悬停弹窗显示创建者

[frontend/src/pages/calendar/chips.tsx](../frontend/src/pages/calendar/chips.tsx) 色块悬停弹窗使用 `ev.createdByName` 字段显示创建者，格式：`14:00-15:00 · 创建者: 张三`。

#### 4.3 EditorPanel 删除后清理 pendingPatch

[frontend/src/pages/calendar/panels/EditorPanel.tsx](../frontend/src/pages/calendar/panels/EditorPanel.tsx) `confirmDelete` 方法增加 `dropPendingPatch(deleteId)`，防止删除后 localStorage patch 重放导致色块残留。

---

## 验证结果

- **TypeScript `tsc --noEmit`**：0 错误 ✅
- **前端生产构建 `npm run build`**：成功，Calendar bundle 302.55 kB（`Calendar-Cl_owleV.js`）✅
- **后端编译 `dotnet build`**：成功 ✅
- **端口基线**：`:5008` 未监听 / `:5009` LISTENING / `:5172` LISTENING / `:5173` LISTENING ✅
- **健康检查 4 端点**：全部 HTTP 200 ✅
- **功能验证**：
  - 拖拽事件后无重复色块 ✅
  - 删除事件后色块立即消失 ✅
  - 跨日拖拽后位置正确 ✅
  - undo/redo 失败自动回滚 ✅
  - SignalR 重连后全量刷新 ✅
  - 重复事件展开正确 ✅
  - 提醒类型选择可用 ✅
  - RRULE 中文预览实时更新 ✅
  - 与会者邮箱校验生效 ✅

**升级建议**：所有使用 v2.2.3 及更早版本的生产环境建议直接升级到 v2.2.4。本次无破坏性变更，无需数据库 Schema 迁移。

---

## 升级须知

### 破坏性变更

无破坏性变更。所有修复均为向后兼容：
- 前端 localStorage key 新增/复用，旧 key 仍有效
- 后端接口新增端点和字段，旧端点语义不变
- 未新增未使用的 NuGet / npm 包

### 升级步骤（推荐生产环境）

```powershell
# 1) 停服务 + 清残留
powershell.exe -ExecutionPolicy Bypass -File C:\noneadcss\Production\stop-prod.ps1

# 2) 构建后端
cd C:\noneadcss
dotnet publish NoneadCSS.Api/NoneadCSS.Api.csproj -c Release -o Production/backend --self-contained false

# 3) 构建前端
cd frontend ; npm install ; npm run build ; cd ..

# 4) 同步配置 + dist
Copy-Item -Force NoneadCSS.Api/appsettings.Production.json Production/backend/appsettings.Production.json
# 同步 dist → Production/frontend/dist、Production/backend/wwwroot、Production/_publish_backend/wwwroot

# 5) 启动（后台任务，blocking=false + long_running_process）
C:\noneadcss\Production\start-prod.ps1

# 6) 独立健康检查
# :5009/health、:5172/、:5173/、:5173/health 全部 HTTP 200；:5008 未监听
```

---

## 📦 本次发布涉及文件清单（摘要）

**后端（2）**：
- [NoneadCSS.Infrastructure/DependencyInjection.cs](../NoneadCSS.Infrastructure/DependencyInjection.cs)（MemoryCalendarQueryCache → Singleton）
- [NoneadCSS.Api/Controllers/CalendarController.cs](../NoneadCSS.Api/Controllers/CalendarController.cs)（新增订阅 URL 端点）

**前端（9）**：
- [frontend/src/pages/calendar/store.ts](../frontend/src/pages/calendar/store.ts)（多轮修复 + 类型安全 + 错误处理助手）
- [frontend/src/pages/calendar/panels/EditorPanel.tsx](../frontend/src/pages/calendar/panels/EditorPanel.tsx)（功能增强 + 类型安全）
- [frontend/src/pages/calendar/panels/TrashPanel.tsx](../frontend/src/pages/calendar/panels/TrashPanel.tsx)（mock → 真实 API）
- [frontend/src/pages/calendar/panels/SubscriptionPanel.tsx](../frontend/src/pages/calendar/panels/SubscriptionPanel.tsx)（mock → 真实 API + 类型安全）
- [frontend/src/pages/calendar/views/ListView.tsx](../frontend/src/pages/calendar/views/ListView.tsx)（类型安全）
- [frontend/src/pages/calendar/views/MonthView.tsx](../frontend/src/pages/calendar/views/MonthView.tsx)（类型安全）
- [frontend/src/pages/calendar/chips.tsx](../frontend/src/pages/calendar/chips.tsx)（创建者显示）
- [frontend/src/pages/calendar/calendarUtils.ts](../frontend/src/pages/calendar/calendarUtils.ts)（instEndMs 逻辑简化）
- [frontend/src/pages/Calendar.tsx](../frontend/src/pages/Calendar.tsx)（SignalR 重连刷新 + visibilitychange + 删除分支清理）

**版本文档（5）**：
- `CHANGELOG.md`（追加 2026-08-25 Unreleased 条目）
- `README.md`（版本号 v2.2.4 + 功能特性更新）
- `CONTRIBUTING.md`（末尾版本号更新）
- `docs/development.md`（新增 §40 章节 + 版本号 2.2.4）
- `docs/拓德客户服务与支持系统用户使用手册.md`（11.1 日历管理章节 + 版本历史 1.8.2）
- 本文件 `docs/ReleaseNotes_v2.2.4.md`（新增）

---

**发布版本**：v2.2.4  
**发布人**：Trae AI 自动化流水线  
**发布状态**：Production Ready
