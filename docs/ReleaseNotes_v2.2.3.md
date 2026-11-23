# Release Notes — v2.2.3

> **历史文档（归档）**：本文件是 v2.2.3 发布当时的记录，后续版本见 [`CHANGELOG.md`](../CHANGELOG.md) 与 `docs/development.md` 的版本章节。当前工作树为 v2.3.0（2026-10-23，未打 tag），并含四 IM 渠道与全站中英双语的开源准备批次（见 CHANGELOG `[Unreleased]`）。

**版本**: v2.2.3
**发布日期**: 2026-08-16
**上一版本**: v2.2.2 (2026-08-16)
**代码仓库**: git@<git.example.com>/<org>/noneadcss.git
**分支**: master
**部署目录**: `C:\noneadcss\Production\`
**标签**: `v2.2.3`

---

## 版本概述

v2.2.3 是在 v2.2.2（月/周/日全量拖拽）基础上的**交互体验与交付质量加强版**，聚焦三条主线：

1. **日历三端双向乐观更新 + 失败自动回滚**：日历拖拽 ↔ 列表编辑 ↔ 概览 KPI 三者实时联动，任何一端操作即时在另两端可见；后端失败时按快照回滚，用户不会看到"改了又消失 / 状态不一致"。
2. **列表字段 + 筛选 + 持久化全链路完善**：时间列补时间、参与人/描述摘要/冲突标识/软删除状态新增列、显示列自定义；今日/本周/本月/自定义/关键字搜索/类型/状态全部防抖联动；刷新浏览器停在原页面原筛选原列配置（不再自动跳日历视图）。
3. **pending 视觉点 + 生产发布脚本固化**：拖拽后右上角同步灰点在后端成功后立即消失；发布脚本 stop/start 做了端口+同名残留进程双重清理、启动作业生命周期从 PowerShell Job 中解耦（不被父会话结束杀死），端口 5009 配置双保险。

累计：
- **前端组件**：日历 store / chips / ListView / Overview / Calendar / EditorPanel / calendarUtils / hooks / API engagement 类型与调用
- **后端服务**：CalendarController / CalendarMutationService / CalendarQueryService / ICalendarMutationService / ICalendarEventRepository / CalendarEventRepository / CalendarEventDto
- **基础设施**：`.gitignore` 补 PublishDrop 忽略、`deploy/nginx-production.template.conf` 纳管、`Production/stop-prod.ps1` 残留进程清理、`Production/start-prod.ps1` blocking=false + long_running 生命周期固定

---

### 变更内容汇总矩阵

| 优先级 | 模块 | 功能点 | 效果 |
|--------|------|--------|------|
| ✨ | 前端 · store | `optimisticUpsertEvent / optimisticRemoveEvent` | 统一入口，本地先更→等后端→失败回滚 |
| ✨ | 前端 · 日历+列表 | 拖拽 ↔ 列表编辑 双向即时同步 | 不再等 10–20 秒 SignalR 轮询 |
| ✨ | 前端 · 概览 | KPI 改为前端 useMemo 实时聚合 | 操作后 0ms 更新 |
| ✨ | 前端 · 列表 | 时间列补齐到"08/18 09:00 - 08/18 10:00" | 与日历视图时间一致 |
| ✨ | 前端 · 列表 | 新增参与人 / 描述摘要 / 冲突 / 软删状态列 | 首批字段落地，右上角可开关列 |
| ✨ | 前端 · 列表筛选 | 关键字 300ms 防抖 + 类型/状态/日期多因子 | 无刷屏式全量重算 |
| ✨ | 前端 · 持久化 | viewMode/calSubView/calCursor/筛选/显示列 全写 localStorage | 刷新后停留在原页面原配置 |
| 🎨 | 前端 · chips | pending 灰点 API 成功后立即消失 | 视觉语义与真实完成状态一致 |
| 🔧 | 后端 · 日历 | 垃圾箱恢复即时返回 DTO + 乐观移除/新增 | 垃圾箱 → 列表立即出现 |
| 🔧 | 后端 · 日历 | 复制事件 recurrence=null + id 重置 | 修复"重复日程父事件需单独操作"报错 |
| 🔧 | 后端 · DTO | CalendarEventDto 字段扩展 | 支持列表新列渲染 |
| 🚀 | 生产发布 | stop-prod 清理残留进程 + appsettings.Production.json Urls=5009 双保险 + start-prod blocking=false long_running 生命周期 | 6 步 SOP 化，端口基线稳定 |

---

## 详细修复清单

### 1. 日历 ↔ 列表 ↔ 概览 双向乐观更新

**统一入口**：[frontend/src/pages/calendar/store.ts](../frontend/src/pages/calendar/store.ts)

```typescript
// 新增统一乐观更新入口
optimisticUpsertEvent: (
  event: CalendarEvent,
  opts?: { skipSignalRRev?: boolean }
) => Promise<void>;

// 统一乐观删除入口
optimisticRemoveEvent: (eventId: string) => Promise<void>;
```

工作流：
1. 调用方（日历 drag/resize 或列表 save/delete）先获取 `snapshotBefore = allEvents.find(...)`。
2. store 先把新值写入 `allEvents`（按 ID 覆盖，保留 `_optimisticMarkers[id]`），派生 `eventsByDate`、`list` 同步按时间分桶重建。
3. await 后端 API。
   - ✅ 成功：清理 `_optimisticMarkers[id]`，保留新值（SignalR 后续推送如果是旧版本则被 marker 拦截）。
   - ❌ 失败：把 `allEvents` 恢复到 `snapshotBefore`，同步重算派生数据，显示 toast："后端同步失败，已回滚到修改前的状态"。

**概览实时 KPI**：[frontend/src/pages/calendar/views/Overview.tsx](../frontend/src/pages/calendar/views/Overview.tsx) 不再从 `/calendar/kpi` 拉数据，改用：

```typescript
const todayStats = useMemo(() => {
  const today = todayYMD();
  const todays = allEvents.filter(e => coversDate(e, today));
  return {
    total: todays.length,
    conflicts: todays.filter(e => hasOverlap(e, todays)).length,
    completed: todays.filter(e => e.status === 'Completed').length,
    trash: allEvents.filter(e => e.isDeleted).length,
  };
}, [allEvents]);
```

### 2. 日历列表字段与筛选功能完善

时间列（原来只显示 `MM/DD`）按用户要求改为：
```
08/18 09:00 - 08/18 10:00
跨天：08/18 22:30 - 08/19 01:15
```

新增列（右上角「显示列自定义」可勾选）：
- 参与人（Avatar 堆叠 + 人数徽标）
- 描述摘要（纯文本首 60 字）
- 冲突标识（🔴 图标 + tooltip "与 N 条事件冲突"）
- 软删除状态（垃圾箱中的事件显示"已删除"标签）

筛选：
| 类别 | 实现 |
|------|------|
| 今日 / 本周 / 本月 / 自定义 | `dateRange` 过滤器直接作用 `allEvents` |
| 搜索（标题/描述/参与人/地点） | `useDebounce(300ms)` 后再进入过滤管线 |
| 类型下拉 | `enabledEventTypes` 多选白名单 |
| 状态下拉 | `enabledStatuses` 多选白名单 |
| 列自定义 | 列显隐写 `localStorage: calendar.listColumns.v1` |

### 3. 日历拖拽 pending 标记点及时消失

之前的 bug：API 成功之后仍 `pendingPatchIds.add(ev.id)` → 灰点一直留在事件右上角。

**修复**（store.ts moveEventByDrop 成功分支）：

```diff
  } finally {
    set({ isDragging: false });
-   // 旧代码：成功后仍然加入 pendingPatchIds
-   set({ pendingPatchIds: new Set([...(get().pendingPatchIds ?? []), ev.id]) });
  }
```

- `pendingPatchIds` 现在只表示**等待后端完成**的视觉语义（API 请求期间为 true，任何结果都为 false）。
- `localStorage: calendar.pendingPatches.v1`（startAt/endAt 的临时修正）**继续保留**，防止刷新浏览器时事件位置回到拖拽前；等后端真正持久化后下次刷新自然清除。
- `_optimisticMarkers`（SignalR 推送的旧数据保护位）与视觉 pending 点完全解耦：即使视觉点消失，保护位在 30 秒 TTL 窗口内仍拦截旧版本回写。

### 4. 后端接口配合修复

- **垃圾箱恢复即时可见**：`CalendarController.RestoreAsync(id)` 成功后返回 200 + 完整 `CalendarEventDto`（`isDeleted=false`），前端收到后立即从垃圾箱集合移除，加入列表与日历。
- **批量删除即时移除**：批量操作成功计数，前端对每个成功 ID 立即 `optimisticRemoveEvent`。
- **复制事件报错修复**：`duplicateEvent` 请求体中 `recurrenceSettings` 显式传 `null`，后端不再命中"父事件必须单独操作"分支；新 ID 由后端生成并用于返回值，前端立即乐观插入到对应日期。
- **DTO 扩展**：`CalendarEventDto` 增加 `attendees[]`、`typeName`、`isDeleted`、`summary` 字段，列表新列无需二次查询。

### 5. 生产发布脚本全流程固化

**停止脚本** `Production/stop-prod.ps1`：
```powershell
# 1) 按端口清理
Stop-ByPort 5009 ; Stop-ByPort 5173 ; Stop-ByPort 5172
# 2) 兜底：终止所有可能残留的同名进程（覆盖 nginx worker / node 孤儿）
Get-Process -Name "NoneadCSS","NoneadCSS.Api","nginx","node" `
  -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
```

**端口双保险**：`NoneadCSS.Api/appsettings.Production.json` 顶部
```json
{
  "Urls": "http://127.0.0.1:5009",
  ...
}
```
即使 `start-prod.ps1` 的 `--urls` 参数因意外未生效（例如旧版本覆盖了脚本），Kestrel 仍监听 5009，不与开发 5008 冲突。

**启动脚本生命周期**：由于 PowerShell 中 `Start-Process -PassThru` 在会话结束时可能将 Job 子组一并终止，此版本的标准启动方式为：
```
blocking=false + command_type=long_running_process + wait_ms_before_async=85000
```
让后端/前端/Nginx 进程独立于启动脚本所在的会话。

**发布 6 步 SOP**（本次实际应用）：
| 步骤 | 命令 | 检查点 |
|------|------|--------|
| 1 | `stop-prod.ps1` | netstat 确认 5009/5172/5173 释放 |
| 2 | `dotnet publish -c Release -o Production/backend` | NoneadCSS.Api.dll 存在 + 大小非 0 |
| 3 | `cd frontend ; npm run build` | Calendar chunk 生成（本例 Calendar-Ch4A5yOt.js） |
| 4 | 同步配置 + dist | appsettings.Production.json 含 Urls=5009；dist 同步到 backend/wwwroot、Production/frontend、Production/wwwroot |
| 5 | `start-prod.ps1`（后台启动） | 脚本内 3 端健康检查通过 |
| 6 | 独立健康检查 | `:5009/health`、`:5172/`、`:5173/`、`:5173/health` 全部 HTTP 200；`:5008` 未监听 |

---

## 验证结果

- **TypeScript `tsc --noEmit`**：0 错误 ✅
- **前端生产构建 `npm run build`**：成功，31 模块 / 31 chunk / 0 cycles；最大 chunk `ConversationDetail` 671.36 kB ✅
- **Calendar chunk HTTP 200（同时经由 vite preview 与 Nginx 反代）**：Calendar-Ch4A5yOt.js = 279,251 bytes（两端完全一致）✅
- **后端编译 `dotnet publish NoneadCSS.Api -c Release`**：成功 ✅
- **端口基线**：`:5008` 未监听 / `:5009` LISTENING / `:5172` LISTENING / `:5173` LISTENING ✅
- **健康检查 4 端点**：全部 HTTP 200 ✅
- **功能 8 项**（见 CHANGELOG）：全部通过 ✅

**升级建议**：所有使用 v2.2.2 及更早版本的生产环境建议直接升级到 v2.2.3。本次无破坏性变更，无需数据库 Schema 迁移。

---

## 继承自 v2.2.x 的版本要点（保留以作参照）

- v2.2.2：月/周/日三视图全量拖拽（drag & drop + resize）、天视图数据覆盖全面修复（事件不再被 setAllEvents 清空）、刷新持久化（calSubView/calCursor/undo 等写 localStorage）。
- v2.2.1：P0/P1 前端视图刷新 bug 修复 16 处（Workflows 列表 / Macros 列表 / Calendar 清理操作未刷新）。
- v2.2.0：审计后首个正式发布，含 2 项 P0 安全修复（EncryptedStringConverter 失败空返回 + HKDF→Legacy 告警）、数据库 N+1 优化、正式生产部署目录与脚本、Nginx CSP frame-ancestors 修复。

---

## 版本追加（2026-08-17：生产 SOP 补打 RowVersion DDL）

> 本小节记录 v2.2.3 在 2026-08-17 进入 Production 目录后追加的 P3 级变更。**代码基线仍标注 v2.2.3**；若需要独立版本号，建议把本节全部内容迁移到 v2.2.4。

### A. 日历编辑"保存后回弹"二次修复（v2.2.3 热修复）

**问题现象（2026-08-17 用户复测）**：周视图事件 A(09:00-17:00) → 双击打开 EditorPanel → 把结束时间改为 15:00 并保存 → 日历格子短暂变成 09:00-15:00，≈2 秒后又回弹回 09:00-17:00。

**根因链**：

1. 前端 `EditorPanel.save / store.moveEventByDrop / store.resizeEvent / 列表编辑` 4 处写操作，在 `await updateEvent(...)` 返回 200 之后，**未**使用后端返回的最新 DTO 再次覆盖本地乐观版本。
2. SignalR 的 `signalRRev` 刷新发生在写完成后 2 秒窗口内，此时本地 `_optimisticMarkers[id]` 已被成功分支清理，导致从后端拉到的旧值（API 持久化之前由中间状态缓存回的版本）把本地乐观更新覆盖回去。

**修复动作**：

- EditorPanel / moveEventByDrop / resizeEvent 成功分支均改为：`response.data` 作为参数再次调 `optimisticUpsertEvent`，确保 `rowVersion / updatedAt` 与后端一致。
- `optimisticUpsertEvent` 同时刷新 `allEvents / eventsByDate / list / overview.upcomingEvents` 四态，日历 / 列表 / 概览任意入口修改后三者完全一致。
- `_optimisticMarkers` TTL 从 30s 调整为 60s，兜底慢 SignalR 推送。

**端到端验证矩阵**：

| 操作入口 | 日历 | 列表 | 概览 KPI | 回滚正确性（模拟 500）|
|---|---|---|---|---|
| EditorPanel 保存 | ✅ | ✅ | ✅ | ✅ |
| drag 整块移动 | ✅ | ✅ | ✅ | ✅ |
| resize 拖底部改时长 | ✅ | ✅ | ✅ | ✅ |
| 列表 inline 编辑 / 批量删除 / 复制 | ✅ | ✅ | ✅ | ✅ |

### B. 后端 P3 多实体乐观锁（8 张表 RowVersion + ConcurrencyException 通用化）

自 v2.2.3 起，除已有的 CalendarEvents 之外，8 个高并发写实体启用 EF Core `.IsRowVersion().ValueGeneratedOnAddOrUpdate()`：

| 实体 | 对应表 | 典型并发场景 |
|------|--------|--------------|
| User | Users | 同账号多点登录并发写 FailedLoginAttempts/LockoutEnd |
| Customer | Customers | GetOrCreateByEmailAsync 并发；客服编辑画像 + 渠道同步回写 |
| ChatSession | ChatSessions | 客户/客服并发写 LastActivityAt/UnreadCount/评分/关闭 |
| ChatMessage | ChatMessages | 撤回 / 已读 / 编辑消息并发 |
| ApiKey | ApiKeys | UsageCount/LastUsedAt 高并发 API 计数 lost update |
| Webhook | Webhooks | TriggerCount/Success/Fail/LastTriggeredAt 高并发回调 |
| KnowledgeCategory | KnowledgeCategories | 分类树拖拽/改名/迁移冲突 |
| KnowledgeArticle | KnowledgeArticles | 多人同时编辑 + 浏览点赞计数 |

**通用异常体系升级**：

- 新增 [ConcurrencyException](../NoneadCSS.Application/Exceptions/ConcurrencyException.cs)：`entityType / entityId / latest` 三件套，409 响应可被前端统一处理。
- [CalendarConcurrencyException](../NoneadCSS.Application/Exceptions/CalendarConcurrencyException.cs) 改为继承 ConcurrencyException，保留 `EventId` 属性，对日历模块调用点零破坏性。
- [GlobalExceptionMiddleware](../NoneadCSS.Api/Middleware/GlobalExceptionMiddleware.cs) 捕获 ConcurrencyException：返回 `errorType=calendar_concurrency_conflict | concurrency_conflict`，响应体内始终包含 `entityType / entityId / eventId / latest`。
- 新增 [ConcurrencyCheckHelper.Check](../NoneadCSS.Application/Services/ConcurrencyCheckHelper.cs)：封装行版本存在性 → 匹配 → 冲突构造 latest → Warn 日志的四步标准流程。

**写入口已加乐观锁（v2.2.3+）**：

| 模块 | Service 方法 | 说明 |
|------|--------------|------|
| Calendar | UpsertAsync / MoveAsync / ResizeAsync / DeleteAsync / RestoreAsync | 使用 CalendarConcurrencyException 保持兼容 |
| Chat | UpdateSessionAsync / UpdateMessageAsync | 接受 `ifMatch`；冲突返回最新快照 DTO |
| Customer | UpdateCustomerAsync | 接受 `ifMatch`；冲突返回最新 CustomerResponse |
| Knowledge | UpdateCategoryAsync / UpdateArticleAsync | 接受 `ifMatch`；冲突返回最新分类/文章 DTO |
| User / ApiKey / Webhook | 后续批次（计划 v2.2.4） | 数据库层已具备 RowVersion 字段，可随时补 Check 调用 |

### C. 生产 SQLite DDL 补丁工具（_DbPatchRowVersion）

**背景**：v2.2.3 代码把 8 张表 RowVersion 写入 EF Core snapshot，但从 v2.2.2 升级的现有生产库（`Production/data/noneadcss.db`）并未经过 Migration，直接启动会抛：

```
SQLite Error 1: 'no such column: u.RowVersion'
```

**补丁工具**：`_DbPatchRowVersion/`（net10.0 控制台，`Microsoft.EntityFrameworkCore.Sqlite 10.0.9`）。

```powershell
# 检查 8 张表的 RowVersion 列是否已存在
dotnet _DbPatchRowVersion\bin\Debug\net10.0\DbPatchRowVersion.dll `
  C:\noneadcss\Production\data\noneadcss.db

# 应用 DDL（幂等，已存在列自动 SKIPPED；失败整个事务回滚）
dotnet _DbPatchRowVersion\bin\Debug\net10.0\DbPatchRowVersion.dll `
  C:\noneadcss\Production\data\noneadcss.db --apply
```

2026-08-17 执行结果：Users / Customers / ChatSessions / ChatMessages / ApiKeys / Webhooks / KnowledgeCategories / KnowledgeArticles 全部 APPLIED，事务 COMMIT 成功。

### D. 生产环境发布 SOP（v2.2.3 起升级到 7 步）

在旧版 6 步 SOP 基础上新增"数据库 DDL 补丁对齐"步骤：

| 步骤 | 内容 | 检查点 |
|------|------|--------|
| 1 | `Production\stop-prod.ps1` | netstat 确认 5009/5172/5173 释放；无 NoneadCSS/nginx/node 残留 |
| 2 | robocopy 备份 Production → `_backup_prod_YYYYMMDD_HHMMSS` | 大小与源目录一致（1.9GB 量级） |
| 3 | `dotnet publish NoneadCSS.Api.csproj -c Release -o Production/backend` | NoneadCSS.Api.dll / appsettings.Production.json 哈希符合预期 |
| 4 | `pnpm build` → robocopy dist → `Production/backend/wwwroot` | Calendar chunk gzip ≈ 66 KB；72 个 assets 文件数对齐 |
| 5 | `DbPatchRowVersion <Production/data/noneadcss.db> --apply` | 8 张表 APPLIED 或 SKIPPED，事务 COMMIT |
| 6 | `Production\start-prod.ps1` (blocking=false) | 脚本内部健康检查通过；后端 healthy |
| 7 | 独立健康检查 | `:5009/health` 200 Healthy、`:5173/health` 200、`/api/calendar/events` 未鉴权返回 401（不是 500）、`:5173/` 200 且含 `<title>` |

2026-08-17 实际发布后运行结果：
- Backend PID=34052（127.0.0.1:5009）、Frontend vite preview PID=36164（0.0.0.0:5172）、Nginx PID=31040（0.0.0.0:5173）全部 LISTENING。
- 启动日志：WeChat 微信/WhatsApp（Enabled=False）Bootstrap、钉钉 Stream（AppKey=dingbv5lvteago75ls13，endpoint wss://wss-open-connection.dingtalk.com:443/connect）、飞书 Stream（AppId=cli_aaf8162f2539dbdb，PingInterval=90s）均连接成功；WorkflowActionHandlerProvider 注册 13 个动作；ChatRatingTimeoutService 每 60 秒扫描；ProcessEmailQueueJob / ProcessImMessageQueueJob 启动即消费。

### E. 已知问题 / 后续计划（v2.2.3+）

1. **User / ApiKey / Webhook 的服务层乐观写入口未补 `ConcurrencyCheckHelper.Check` 调用**：数据库列 + EF 配置已到位，下次 v2.2.4 周期按模块逐一补 Check + DTO 字段 + Controller `If-Match` 绑定。
2. **SQLite ALTER TABLE ADD COLUMN 无法带 DEFAULT BLOB 字面量**：生产数据中新增的 8 个 `RowVersion` 初始均为 NULL；EF Core 在首次 `SaveChangesAsync` 时会为被更新的行自动生成正确的 8 字节 RowVersion，读路径返回 NULL 时与现有比较逻辑是安全的（ConcurrencyCheckHelper 已做 null 分支处理）。
3. **前端聊天 / 客户 / 知识库编辑弹窗尚未传 `ifMatch` + 处理 409 latest 快照**：目前仅日历模块完整做到"乐观更新 + 409 回滚 + latest 自动合入本地状态"；聊天、客户、知识库的前端适配排入 v2.2.4。
4. **Production 目录仍依赖旧的 `restart-prod.ps1` 历史路径**：README 的 18.8 节已同时标注 `restart-prod.ps1` 与 `Production/start-prod.ps1` 两条路径，建议新生产环境部署直接使用仓库内 `Production/start-prod.ps1` 与 `Production/stop-prod.ps1` 作为标准入口。

---

## 升级须知

### 破坏性变更

- **数据库 Schema 变更（非破坏性）**：8 张表新增 `RowVersion BLOB NULL` 列。对于从 v2.2.2 升级到 v2.2.3 的现有部署，需在"停服务 → publish → 启动服务"之间执行 §C 的 `DbPatchRowVersion --apply` 补丁（幂等）。未执行该步直接启动时会抛 `no such column: u.RowVersion`，但不会写入数据。
- 其余新增接口均为向后兼容：
  - REST 接口：新增字段、新增可选 `If-Match` header；旧字段语义不变。
  - 前端持久化：所有 `localStorage` key 均为**新增**（如 `calendar.listColumns.v1`、`overview.upcomingEvents.v1`），旧 key 仍有效。
  - 依赖项：未新增未使用的 NuGet / npm 包。

### 配置变更

| 配置项 | 文件 | 说明 |
|--------|------|------|
| `Urls` | `NoneadCSS.Api/appsettings.Production.json` | 显式声明 `http://127.0.0.1:5009`，与 Nginx upstream backend 对齐 |
| .gitignore | 仓库根 | 新增 `PublishDrop/`（临时合成发布产物目录，不入库） |
| 新文件入版控 | `deploy/nginx-production.template.conf` | 生产 Nginx 配置模板（和 `deploy/nginx.conf`、`deploy/restart-*.ps1` 一起作为基础设施脚本） |
| 生产脚本入口（推荐）| `Production/stop-prod.ps1` + `Production/start-prod.ps1` | 标准启动/停止脚本；`restart-prod.ps1` 保留用于兼容旧部署 |

### 升级步骤（推荐生产环境）

```powershell
# 1) 停服务 + 清残留
powershell.exe -ExecutionPolicy Bypass -File C:\noneadcss\Production\stop-prod.ps1

# 2) 可选：备份数据库 / 备份整个生产目录
# Copy-Item Production\data\noneadcss.db "backup_$(Get-Date -Format yyyyMMdd_HHmmss).db"
# robocopy C:\noneadcss\Production C:\noneadcss\_backup_prod_$(Get-Date -Format yyyyMMdd_HHmmss) /MIR /R:2 /W:1

# 3) 构建后端
cd C:\noneadcss
dotnet publish NoneadCSS.Api/NoneadCSS.Api.csproj -c Release -o Production/backend --self-contained false

# 4) 构建前端
cd frontend ; npm install ; npm run build ; cd ..

# 5) 同步配置（SOP Critical：千万不要用 publish 输出的 appsettings.json 覆盖生产配置）
Copy-Item -Force NoneadCSS.Api/appsettings.Production.json Production/backend/appsettings.Production.json

# 6) 同步 dist → 三处（backend/wwwroot / Production/frontend / Production/wwwroot）
#    （详见 CHANGELOG §2.2.3 发布 SOP 步骤 4）

# 7) 启动（建议作为后台任务，不要作为 PowerShell Job 子组）
#    blocking=false + long_running_process
C:\noneadcss\Production\start-prod.ps1

# 8) 独立健康检查
#    :5009/health、:5172/、:5173/、:5173/health 全部 HTTP 200；:5008 未监听
```

---

## 📦 本次发布涉及文件清单（摘要）

**后端（7）**：
- [NoneadCSS.Api/Controllers/CalendarController.cs](../NoneadCSS.Api/Controllers/CalendarController.cs)
- [NoneadCSS.Application/DTOs/Responses/CalendarEventDto.cs](../NoneadCSS.Application/DTOs/Responses/CalendarEventDto.cs)
- [NoneadCSS.Application/Services/Calendar/CalendarMutationService.cs](../NoneadCSS.Application/Services/Calendar/CalendarMutationService.cs)
- [NoneadCSS.Application/Services/Calendar/CalendarQueryService.cs](../NoneadCSS.Application/Services/Calendar/CalendarQueryService.cs)
- [NoneadCSS.Application/Services/Calendar/ICalendarMutationService.cs](../NoneadCSS.Application/Services/Calendar/ICalendarMutationService.cs)
- [NoneadCSS.Domain/Repositories/ICalendarEventRepository.cs](../NoneadCSS.Domain/Repositories/ICalendarEventRepository.cs)
- [NoneadCSS.Infrastructure/Repositories/CalendarEventRepository.cs](../NoneadCSS.Infrastructure/Repositories/CalendarEventRepository.cs)

**前端（11）**：
- [frontend/src/api/modules/engagement.ts](../frontend/src/api/modules/engagement.ts)
- [frontend/src/api/types/engagement.ts](../frontend/src/api/types/engagement.ts)
- [frontend/src/context/AuthContext.tsx](../frontend/src/context/AuthContext.tsx)
- [frontend/src/pages/Calendar.tsx](../frontend/src/pages/Calendar.tsx)
- [frontend/src/pages/calendar/calendarUtils.ts](../frontend/src/pages/calendar/calendarUtils.ts)
- [frontend/src/pages/calendar/chips.tsx](../frontend/src/pages/calendar/chips.tsx)
- [frontend/src/pages/calendar/hooks.ts](../frontend/src/pages/calendar/hooks.ts)
- [frontend/src/pages/calendar/panels/EditorPanel.tsx](../frontend/src/pages/calendar/panels/EditorPanel.tsx)
- [frontend/src/pages/calendar/store.ts](../frontend/src/pages/calendar/store.ts)
- [frontend/src/pages/calendar/views/ListView.tsx](../frontend/src/pages/calendar/views/ListView.tsx)
- [frontend/src/pages/calendar/views/Overview.tsx](../frontend/src/pages/calendar/views/Overview.tsx)

**基础设施（3 + 1 新增纳管）**：
- `.gitignore`（新增 PublishDrop/）
- `Production/stop-prod.ps1`（残留进程清理强化）
- `Production/start-prod.ps1`（启动生命周期 + 健康检查等待参数固化）
- 新增纳管：`deploy/nginx-production.template.conf`

**版本文档（2）**：
- `CHANGELOG.md`（新增 v2.2.3 顶部节）
- 本文件 `docs/ReleaseNotes_v2.2.3.md`（新增）

---

**发布版本**：v2.2.3  
**发布人**：Trae AI 自动化流水线  
**发布状态**：Production Ready
