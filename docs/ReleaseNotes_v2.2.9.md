# Release Notes — v2.2.9

> **历史文档（归档）**：本文件是 v2.2.9 发布当时的记录，后续版本见 [`CHANGELOG.md`](../CHANGELOG.md) 与 `docs/development.md` 的版本章节。当前工作树为 v2.3.0（2026-10-23，未打 tag），并含四 IM 渠道与全站中英双语的开源准备批次（见 CHANGELOG `[Unreleased]`）。

**版本**: v2.2.9
**发布日期**: 2026-08-28
**上一版本**: v2.2.8 (2026-08-27)
**代码仓库**: git@<git.example.com>/<org>/noneadcss.git
**分支**: master
**部署目录**: `C:\noneadcss\Production\`
**标签**: `v2.2.9`

---

## 版本概述

v2.2.9 是 NoneadCSS（拓德客户服务与支持系统）在 v2.2.8（日历提醒修复 + 全项目 Bug 修复稳定基线）基础上推出的**系统管理-审计日志完善与优化版**。本版本聚焦审计日志（AuditLogs）模块从「基础记录可用」提升到「完整运维追溯与合规审计平台」的目标，五大主线全面增强：

1. **多维过滤**：`AuditLogRepository.ApplyFilters` 私有方法集中过滤逻辑，被 list / stats / export 三处端点统一复用，支持 8 维过滤（操作类型 / 实体类型 / 用户 ID / 实体 ID / IP 地址 / 关键字 / 日期范围），消除三处实现漂移。
2. **全局统计聚合**：新增独立 `/api/auditlogs/stats` 端点，返回 `totalLogs / uniqueUsers / last24hCount / failedActions + actionDistribution + entityDistribution`，不依赖当前页数据，避免分页导致的统计失真。
3. **CSV 导出**：新增 `/api/auditlogs/export` 端点，服务端生成 UTF-8 BOM + CSV 字节流，浏览器直接下载 Blob；文件名含时间戳；最大 10000 行防止内存溢出。
4. **HMAC-SHA256 完整性校验**：审计日志写入时即生成签名，新增单条 + 批量校验端点；批量校验后 `applyIntegrityToTable` 回写 `isTampered` 字段，前端列表以 ShieldX（红色，疑似篡改）/ ShieldCheck（绿色，校验通过）图标 + 行底色高亮可视化。
5. **关联日志**：新增 `/api/auditlogs/{id}/related` 端点，优先返回同实体（`entityType + entityId`）的最近日志，无则回退到同用户（`userId`）日志，便于追溯操作上下文与操作链路。

累计：
- **后端核心控制器**：`AuditLogsController.cs`（5 个新端点 + 8 维过滤参数）
- **后端仓储**：`AuditLogRepository.cs`（ApplyFilters 集中过滤 + GetStatsAsync + ExportAsync + VerifyIntegrityAsync + GetRelatedLogsAsync）
- **后端服务**：`AuditService.cs` + `IAuditService.cs`（接口扩展与实现）
- **后端 DTO**：`AuditLogResponse.cs`（AuditLogStats / AuditIntegrityReport / VerifyResult / RelatedLogResponse）
- **后端缓存**：`Program.cs` OutputCache 策略纳入全部 10 个查询参数（含分页）
- **前端页面**：`AuditLogs.tsx`（KPI 卡片 + 操作分布 Top 8 条形图 + 实体占比图 + 日期范围选择器 + 关键字 400ms 防抖 + 批量校验回写 + 详情弹窗增强）
- **前端 API**：`api/modules/system.ts` + `api/types/system.ts`（扩展审计日志 API 客户端与类型定义）

---

## 变更内容汇总矩阵

| 优先级 | 模块 | 功能点 | 效果 |
|--------|------|--------|------|
| ✨ P1 | 后端 · Repository | `ApplyFilters` 私有方法集中 8 维过滤逻辑 | list / stats / export 三处统一复用，消除实现漂移 |
| ✨ P1 | 后端 · Controller | `GET /api/auditlogs/stats` 独立统计端点 | 全量聚合不依赖当前页，避免分页失真 |
| ✨ P1 | 后端 · Controller | `GET /api/auditlogs/export` CSV 导出端点 | UTF-8 BOM，Excel 友好，最大 10000 行 |
| ✨ P1 | 后端 · Controller | `GET /api/auditlogs/{id}/verify` 单条签名校验 | 返回 `{isValid, storedSignature, expectedSignature, verifiedAt}` |
| ✨ P1 | 后端 · Controller | `GET /api/auditlogs/verify-integrity` 批量校验端点 | 返回 `AuditIntegrityReport`，含 tamperedLogIds[] |
| ✨ P1 | 后端 · Controller | `GET /api/auditlogs/{id}/related` 关联日志端点 | 同实体优先 → 同用户回退，默认 5 条 |
| ✨ P1 | 后端 · Program.cs | OutputCache VaryByQueryKeys 纳入全部 10 个查询参数 | 避免不同过滤条件被同一缓存命中 |
| ✨ P1 | 前端 · AuditLogs.tsx | KPI 卡片（总记录 / 操作用户 / 近 24h / 异常事件） | 全局统计可视化，来自独立 stats 端点 |
| ✨ P1 | 前端 · AuditLogs.tsx | 操作分布 Top 8 条形图 + 实体占比图 | 直观展示操作类型与实体类型分布 |
| ✨ P1 | 前端 · AuditLogs.tsx | 日期范围选择器（`<input type="date">` 起止） | 本地日期 → UTC ISO（toUtcStartIso / toUtcEndIso） |
| ✨ P1 | 前端 · AuditLogs.tsx | 服务端关键字搜索 + 400ms 防抖（useDebounce） | 避免每次按键触发 API |
| ✨ P1 | 前端 · AuditLogs.tsx | 批量校验 + `applyIntegrityToTable` 回写 isTampered | 列表 ShieldX/ShieldCheck 图标 + 行底色高亮 |
| ✨ P1 | 前端 · AuditLogs.tsx | 详情弹窗增强（基本信息 + 网络上下文 + 关联日志 + 字段变更 + 单条签名校验） | 完整运维追溯视图 |
| 🔧 P2 | 前端 · API | system.ts 扩展审计日志 API 客户端 + 新增 AuditLogResponse 类型导入 | 支持新端点调用 |
| 🔧 P2 | 文档 | README.md 版本升至 v2.2.9 + 扩展审计日志功能描述 | 项目概览同步 |
| 🔧 P2 | 文档 | CHANGELOG.md 新增 v2.2.9 完整变更记录 | 版本历史同步 |
| 🔧 P2 | 文档 | docs/development.md 新增第 47 章 + 版本升至 2.2.9 | 开发文档同步 |
| 🔧 P2 | 文档 | docs/NoneadCSS_TechnicalDebt_2026Q4.md 新增 Post-Review v2 章节 | 技术债务同步 |
| 🔧 P2 | 文档 | docs/拓德客户服务与支持系统用户使用手册.md 14.1 节扩展为 8 个子章节 | 用户文档同步 |

---

## 详细变更清单

### 1. 多维过滤（统一 ApplyFilters）

#### 1.1 集中过滤逻辑

`AuditLogRepository.cs` 抽取 `ApplyFilters` 私有方法，被 `GetPagedAsync` / `GetStatsAsync` / `ExportAsync` 三处复用，避免实现漂移：

```csharp
private static IQueryable<AuditLog> ApplyFilters(
    IQueryable<AuditLog> query,
    string? action, string? entityType, int? userId, int? entityId,
    string? ipAddress, string? keyword,
    DateTime? startDate, DateTime? endDate)
{
    if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);
    if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(a => a.EntityType == entityType);
    if (userId.HasValue) query = query.Where(a => a.UserId == userId.Value);
    if (entityId.HasValue) query = query.Where(a => a.EntityId == entityId.Value);
    if (!string.IsNullOrWhiteSpace(ipAddress)) query = query.Where(a => a.IpAddress == ipAddress);
    if (!string.IsNullOrWhiteSpace(keyword))
        query = query.Where(a => (a.Action.Contains(keyword)) || (a.EntityType.Contains(keyword)));
    if (startDate.HasValue) query = query.Where(a => a.CreatedAt >= startDate.Value);
    if (endDate.HasValue) query = query.Where(a => a.CreatedAt <= endDate.Value);
    return query;
}
```

#### 1.2 三端点统一过滤参数

`AuditLogsController` 的 list / stats / export 三个端点签名完全一致：

```csharp
[HttpGet("list")] public async Task<IActionResult> GetAuditLogs(
    [FromQuery] string? action, [FromQuery] string? entityType,
    [FromQuery] int? userId, [FromQuery] int? entityId,
    [FromQuery] string? ipAddress, [FromQuery] string? keyword,
    [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
    [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
```

### 2. 全局统计聚合

#### 2.1 独立统计端点

`IAuditLogRepository.GetStatsAsync` 签名与过滤参数完全一致，返回 `AuditLogStats` DTO。实现要点：
- `totalLogs`：`baseQuery.CountAsync()` 全量计数（非当前页）。
- `uniqueUsers`：`baseQuery.Select(a => a.UserId).Distinct().CountAsync()`。
- `last24hCount`：`baseQuery.CountAsync(a => a.CreatedAt >= DateTime.UtcNow.AddHours(-24))`。
- `failedActions`：`baseQuery.CountAsync(a => a.Action.Contains("Delete") || a.Action.Contains("Failed"))`。
- `actionDistribution` / `entityDistribution`：`GroupBy` + `OrderByDescending(Count)` + `Take(10)`。

#### 2.2 鉴权与缓存

```csharp
[HttpGet("stats")]
[RequireSuperAdmin]  // 仅超级管理员可访问
[OutputCache(Tags = ["AuditLogs"])]
public async Task<ActionResult<AuditLogStats>> GetStats(...)
```

### 3. CSV 导出

#### 3.1 服务端生成

```csharp
[HttpGet("export")]
[RequireSuperAdmin]
public async Task<IActionResult> ExportCsv(...)
{
    var logs = await _auditLogRepository.ExportAsync(filters, 10000);
    var csv = new StringBuilder();
    csv.Append('\uFEFF');  // UTF-8 BOM
    csv.AppendLine("Id,UserId,UserName,Action,EntityType,EntityId,CreatedAt,IpAddress,Changes");
    foreach (var log in logs) csv.AppendLine(...);
    var bytes = Encoding.UTF8.GetBytes(csv.ToString());
    var fileName = $"audit-logs_{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
    return File(bytes, "text/csv", fileName);
}
```

#### 3.2 前端下载

```typescript
const blob = await auditLogApi.exportCsv(filters);
const url = window.URL.createObjectURL(blob);
const a = document.createElement('a');
a.href = url; a.download = `audit-logs_${formatDate()}.csv`;
a.click(); window.URL.revokeObjectURL(url);
```

### 4. HMAC-SHA256 完整性校验

#### 4.1 单条校验

```csharp
[HttpGet("{id}/verify")]
[RequireSuperAdmin]
public async Task<ActionResult<VerifyResult>> VerifySignature(long id)
{
    var result = await _auditLogRepository.VerifySignatureAsync(id);
    return Ok(result);  // { isValid, storedSignature, expectedSignature, verifiedAt }
}
```

#### 4.2 批量校验

```csharp
[HttpGet("verify-integrity")]
[RequireSuperAdmin]
public async Task<ActionResult<AuditIntegrityReport>> VerifyIntegrity(
    int page = 1, int pageSize = 50)
{
    var report = await _auditLogRepository.VerifyIntegrityAsync(page, pageSize);
    return Ok(report);
    // { page, pageSize, totalLogs, checkedCount, tamperedCount, tamperedLogIds[] }
}
```

#### 4.3 前端回写与可视化

```typescript
const applyIntegrityToTable = useCallback(
    (report: AuditIntegrityReport) => {
        setTable(prev => prev.map(item =>
            report.tamperedLogIds.includes(item.id)
                ? { ...item, isTampered: true }
                : { ...item, isTampered: false }
        ));
    },
    []
);

// 表格 ID 列渲染
{log.isTampered === true ? (
    <ShieldX className="text-rose-500" />
) : log.isTampered === false ? (
    <ShieldCheck className="text-emerald-500" />
) : null}

// 篡改行底色
className={log.isTampered ? 'bg-rose-50/40' : ''}
```

### 5. 关联日志

#### 5.1 同实体优先 → 同用户回退

```csharp
[HttpGet("{id}/related")]
[RequireSuperAdmin]
public async Task<ActionResult<List<RelatedLogResponse>>> GetRelated(
    long id, [FromQuery] int limit = 5)
{
    var related = await _auditLogRepository.GetRelatedLogsAsync(id, limit);
    return Ok(related);
}
```

仓储实现策略：
1. 先取当前日志的 `entityType + entityId + userId`。
2. 优先查询同 `entityType + entityId` 的最近日志（排除自身）。
3. 若同实体日志不足 `limit` 条，回退查询同 `userId` 的最近日志补齐。
4. 返回 `RelatedLogResponse` 列表。

#### 5.2 详情弹窗集成

详情弹窗底部「关联日志」区域展示返回列表，每条可点击跳转：

```typescript
{relatedLogs.map(related => (
    <button onClick={() => openDetail(related.id)}>
        {related.action} - {related.entityType}#{related.entityId}
        <span>{related.createdAt}</span>
    </button>
))}
```

### 6. OutputCache 策略更新

`Program.cs` 中 `AuditLogs` 缓存策略纳入全部 10 个查询参数（含分页）：

```csharp
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("AuditLogs", builder => builder
        .SetVaryByQuery(new[] {
            "action", "entityType", "userId", "entityId",
            "ipAddress", "keyword", "startDate", "endDate",
            "page", "pageSize"
        })
        .Tag("AuditLogs")
        .Expire(TimeSpan.FromMinutes(5)));
});
```

### 7. 前端体验优化

#### 7.1 日期范围选择器

```typescript
const toUtcStartIso = (dateStr: string) =>
    dateStr ? new Date(dateStr + 'T00:00:00').toISOString() : undefined;
const toUtcEndIso = (dateStr: string) =>
    dateStr ? new Date(dateStr + 'T23:59:59').toISOString() : undefined;
```

#### 7.2 关键字防抖

```typescript
const debouncedKeyword = useDebounce(keyword, 400);
useEffect(() => {
    setFilters(prev => ({ ...prev, keyword: debouncedKeyword }));
}, [debouncedKeyword]);
```

#### 7.3 KPI 卡片 + 分布图

```typescript
<div className="grid grid-cols-4 gap-4">
    <KpiCard label="总记录数" value={stats.totalLogs} />
    <KpiCard label="操作用户" value={stats.uniqueUsers} />
    <KpiCard label="近 24h" value={stats.last24hCount} />
    <KpiCard label="异常事件" value={stats.failedActions} tone="rose" />
</div>
<BarChart data={stats.actionDistribution.slice(0, 8)} />
<PieChart data={stats.entityDistribution} />
```

---

## 影响文件清单

### 后端（7）

- [NoneadCSS.Api/Controllers/AuditLogsController.cs](../NoneadCSS.Api/Controllers/AuditLogsController.cs)（新增 5 个端点 + 8 维过滤参数）
- [NoneadCSS.Api/Program.cs](../NoneadCSS.Api/Program.cs)（OutputCache VaryByQuery 全参数覆盖）
- [NoneadCSS.Application/DTOs/Responses/AuditLogResponse.cs](../NoneadCSS.Application/DTOs/Responses/AuditLogResponse.cs)（AuditLogStats / AuditIntegrityReport / VerifyResult / RelatedLogResponse）
- [NoneadCSS.Application/Services/IAuditService.cs](../NoneadCSS.Application/Services/IAuditService.cs)（接口扩展）
- [NoneadCSS.Domain/Repositories/IAuditLogRepository.cs](../NoneadCSS.Domain/Repositories/IAuditLogRepository.cs)（接口扩展）
- [NoneadCSS.Infrastructure/Repositories/AuditLogRepository.cs](../NoneadCSS.Infrastructure/Repositories/AuditLogRepository.cs)（ApplyFilters + 5 个新方法）
- [NoneadCSS.Infrastructure/Services/AuditService.cs](../NoneadCSS.Infrastructure/Services/AuditService.cs)（服务实现）

### 前端（3）

- [frontend/src/pages/AuditLogs.tsx](../frontend/src/pages/AuditLogs.tsx)（KPI 卡片 + 分布图 + 日期范围 + 关键字防抖 + 批量校验回写 + 详情弹窗增强）
- [frontend/src/api/modules/system.ts](../frontend/src/api/modules/system.ts)（扩展审计日志 API 客户端 + 新增 AuditLogResponse 类型导入）
- [frontend/src/api/types/system.ts](../frontend/src/api/types/system.ts)（扩展类型定义）

### 文档（5）

- [README.md](../README.md)（版本升至 v2.2.9 + 扩展审计日志功能描述）
- [CHANGELOG.md](../CHANGELOG.md)（新增 v2.2.9 完整变更记录）
- [docs/development.md](./development.md)（版本升至 2.2.9 + 新增第 47 章详述后端实现）
- [docs/NoneadCSS_TechnicalDebt_2026Q4.md](./NoneadCSS_TechnicalDebt_2026Q4.md)（新增 Post-Review v2 架构改进沉淀）
- [docs/拓德客户服务与支持系统用户使用手册.md](./拓德客户服务与支持系统用户使用手册.md)（14.1 节扩展为 8 个子章节）

---

## 架构改进沉淀

1. **集中过滤模式**：`ApplyFilters` 私有方法被 list / stats / export 三处复用，避免实现漂移。该模式可推广至其他需要多维过滤的仓储（如 `ConversationRepository` / `CustomerRepository`）。
2. **OutputCache VaryByQuery 全参数覆盖**：`AuditLogs` 缓存策略纳入全部 10 个查询参数（含分页），避免不同过滤条件被同一缓存命中。该实践应作为所有带过滤的 OutputCache 策略标准。
3. **批量校验回写列表**：`applyIntegrityToTable` 在批量校验后回写 `isTampered` 到当前列表项，避免用户需要打开每条详情才能看到校验结果。该模式可推广至其他批量操作（如批量状态变更回写）。
4. **声明顺序约束**：`useCallback` 依赖项必须在声明顺序上先于依赖它的 `useCallback`，否则 TS2448/TS2454。该约束应作为 React Hooks 编码规范。

---

## 部署验证

v2.2.9 已编译并部署至生产环境（`Production\backend` + `Production\frontend\dist`）：

- **后端**: `dotnet NoneadCSS.Api.dll --urls http://127.0.0.1:5009`（端口 5009）
- **前端**: `vite preview --port 5172 --host 127.0.0.1`（强制 IPv4）
- **Nginx**: 5173 反向代理（5173 → 5172, /api → :5009）
- **鉴权验证**: `/api/auditlogs/stats` 与 `/api/auditlogs/export` 返回 401（鉴权生效）
- **数据库**: SQLite `Production/data/noneadcss.db`，无 schema 变更

---

## 兼容性

- **数据库**: 无 schema 变更，v2.2.5+ 数据库可直接升级
- **API**: 新增端点不影响现有端点；现有端点新增的可选查询参数向后兼容
- **前端**: 新增 KPI 卡片、分布图、批量校验按钮等均为增量功能，不影响现有列表浏览
- **权限**: 新增端点均使用 `RequireSuperAdmin` 鉴权，与现有审计日志权限策略一致

---

*版本 v2.2.9 · 2026-08-27 · 系统管理-审计日志完善与优化（多维过滤 + 全局统计 + CSV 导出 + HMAC 完整性校验 + 关联日志 + 签名状态可视化）*
