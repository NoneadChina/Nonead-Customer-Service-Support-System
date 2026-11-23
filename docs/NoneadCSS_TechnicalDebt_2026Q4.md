# NoneadCSS 技术债务清单 - 2026 Q4 专项迭代

## 概述

本清单涵盖 2026 Q3 审计中跳过的 7 项高风险/范围大问题，按修复优先级排序。每项包含具体文件路径、行号、问题描述、风险评估、修复方案和工作量评估。

**审计基线**:
- 审计日期: 2026-08-05
- 审计范围: 代码质量、安全、功能完整性、性能、测试
- 总问题数: 38 项（已修复 31 项，跳过 7 项收录本清单）

**2026-11-23 复核（v2.3.0 工作树）**: 7 项中 TD-01/02/03/05/06 已在 v2.2.x 各批次修复（见各节「已修复」）；TD-04 涉及的 `TriggerActionJob` 已于 2026-11-21 移除，需求本身仍需一个正式注册的作业；TD-07 的 7a（乐观锁）已完成，7b（`/v1/` API 版本控制）与 7c 未做。当前控制器 57 个（非文中的 48 个），`RequireSuperAdmin` 295 处、细粒度 `Permission` 191 处。

---

## TD-01 仓储层事务嵌套缺陷（架构级根因）

**优先级**: P0 | **工作量**: 中（3-5 天） | **类别**: 事务边界

### 问题位置

- **根因**: [UnitOfWork.cs](../NoneadCSS.Infrastructure/UnitOfWork.cs#L169-L173) `BeginTransactionAsync` 不支持嵌套
- **触发点**: [MacroService.cs](../NoneadCSS.Application/Services/MacroService.cs#L299-L348) `ExecuteAsync` 开启事务后调用 `AddNoteAsync`/`AddReplyAsync`，后者在 [ConversationThreadService.cs:701](../NoneadCSS.Application/Services/ConversationThreadService.cs#L701) 再次开启事务
- **Workaround 现状**: `ConversationThreadService.BeginSuppressedScope()` 仅在 `ExecuteForWorkflowAsync` 中使用，`ExecuteAsync` 遗漏

### 问题描述

```csharp
// UnitOfWork.cs L169-173
public async Task<IUnitOfWorkTransaction> BeginTransactionAsync()
{
    var transaction = await _context.Database.BeginTransactionAsync();
    return new EfCoreUnitOfWorkTransaction(transaction);
}
```

直接调用 `Database.BeginTransactionAsync()`，EF Core 在已有活动事务时会抛出 `InvalidOperationException: A transaction is already in progress`。

`MacroService.ExecuteAsync`（从 `MacrosController.Execute` 直接调用）在 L299 开启事务，当宏包含 `add_note` 或 `send_reply` 动作时，会调用 `ConversationThreadService.AddNoteAsync`/`AddReplyAsync`，后者在 `IsSuppressed == false` 时会再次调用 `_unitOfWork.BeginTransactionAsync()`，导致异常。

对比 `MacroService.ExecuteForWorkflowAsync`（L409-L501）正确使用了 `_threadService.BeginSuppressedScope()`（L446）来抑制内层事务，`ExecuteAsync` 缺失这一保护。

### 风险评估

- **高**: 任何含 `add_note` 或 `send_reply` 动作的宏通过 API 调用必失败（抛 `InvalidOperationException`）
- 异常发生在宏执行过程中，外层 catch 调用 `SafeRollbackAsync`，用户只看到"宏执行异常"的笼统错误，难以定位根因
- 应用层 `AsyncLocal<int>` 计数 workaround 脆弱，异常路径下 `Dispose` 遗漏会导致抑制作用域泄漏到后续请求
- 单元测试若未覆盖含 `add_note`/`send_reply` 动作的宏，此缺陷不会被察觉

### 修复方案

1. 在 `UnitOfWork` 中维护 `_currentTransaction` 引用，已有事务时返回基于 savepoint 的嵌套事务包装器（推荐）
2. 或检测到已有事务时返回 no-op 事务包装器并记录警告日志
3. 修复后移除 `ConversationThreadService.BeginSuppressedScope` workaround
4. 测试用例：覆盖所有宏动作组合的执行路径

### 参考文件

- `c:\noneadcss\NoneadCSS.Infrastructure\UnitOfWork.cs`
- `c:\noneadcss\NoneadCSS.Domain\IUnitOfWork.cs`
- `c:\noneadcss\NoneadCSS.Application\Services\MacroService.cs`
- `c:\noneadcss\NoneadCSS.Application\Services\ConversationThreadService.cs`
- `c:\noneadcss\NoneadCSS.Domain\UnitOfWorkTransactionExtensions.cs`

---

## TD-02 仓储层普遍调用 SaveChangesAsync（架构债务）

**优先级**: P1 | **工作量**: 大（1-2 周） | **类别**: 分层契约

### 问题位置

**仓储层违规调用 `SaveChangesAsync` 共 30+ 处**:

| 仓储文件 | 行号 | 方法 |
|---|---|---|
| [CustomerRepository.cs](../NoneadCSS.Infrastructure/Repositories/CustomerRepository.cs#L278) | 278, 296, 320, 422, 436 | `GetOrCreateByEmailAsync` / `GetOrCreateByChannelAsync` |
| [MacroRepository.cs](../NoneadCSS.Infrastructure/Repositories/MacroRepository.cs#L117) | 117, 124, 133, 174, 286 | `AddAsync` / `UpdateAsync` / `DeleteAsync` / `SoftDeleteAsync` |
| [ModuleRepository.cs](../NoneadCSS.Infrastructure/Repositories/ModuleRepository.cs#L88) | 88, 108, 124, 143, 150, 161 | `AddAsync` / `UpdateAsync` / `DeleteAsync` / `ToggleActiveAsync` |
| [TimeEntryRepository.cs](../NoneadCSS.Infrastructure/Repositories/TimeEntryRepository.cs#L123) | 123, 130, 140, 149, 179, 189 | `AddAsync` / `UpdateAsync` / `DeleteAsync` / `RestoreAsync` |
| [EmailSignatureRepository.cs](../NoneadCSS.Infrastructure/Repositories/EmailSignatureRepository.cs#L122) | 122 | `SetDefaultAsync` |
| [NotificationRuleRepository.cs](../NoneadCSS.Infrastructure/Repositories/NotificationRuleRepository.cs#L150) | 150 | `SoftDeleteAsync` |
| [NotificationTemplateRepository.cs](../NoneadCSS.Infrastructure/Repositories/NotificationTemplateRepository.cs#L110) | 110 | `SoftDeleteAsync` |
| [PasswordResetTokenRepository.cs](../NoneadCSS.Infrastructure/Repositories/PasswordResetTokenRepository.cs#L43) | 43 | `InvalidateUserTokensAsync` |
| [RefreshTokenRepository.cs](../NoneadCSS.Infrastructure/Repositories/RefreshTokenRepository.cs#L41) | 41, 58 | `RevokeAsync` / `RevokeUserTokensAsync` |
| [SystemOptionRepository.cs](../NoneadCSS.Infrastructure/Repositories/SystemOptionRepository.cs#L66) | 66 | `SetAsync` |
| [SlaRepository.cs](../NoneadCSS.Infrastructure/Repositories/SlaRepository.cs#L170) | 170 | `SoftDeleteAsync` |

### 问题描述

典型示例（`CustomerRepository.cs` L253-296）:

```csharp
public async Task<Customer?> GetOrCreateByEmailAsync(string email, ...)
{
    // 事务由服务层统一管理（_unitOfWork.BeginTransactionAsync()），仓储层不得嵌套开启事务。
    // SaveChangesAsync 在已有事务内执行时会复用该事务，不会创建嵌套事务。
    ...
    if (softDeletedCustomer != null)
    {
        softDeletedCustomer.DeletedAt = null;
        ...
        await _context.SaveChangesAsync();   // L278: 仓储内 SaveChanges
        return softDeletedCustomer;
    }
    ...
    await _dbSet.AddAsync(customer);
    await _context.SaveChangesAsync();       // L296: 仓储内 SaveChanges
    return customer;
}
```

注释声称"事务由服务层统一管理"，但实际并非所有调用方都开启了事务（例如 `WeChatIngestionService.HandleEventAsync` L169、`FeishuIngestionService` 等都会在无显式事务的情况下调用 `GetOrCreateByChannelAsync`）。

### 风险评估

- **中**: 若调用方未开启事务，每次仓储内的 `SaveChangesAsync` 都会触发 EF Core 的隐式事务并立即提交，破坏多步操作的原子性
- `GetOrCreateByEmailAsync` 的"查软删除-恢复-保存"与"创建新客户-保存"两步之间存在窗口期，并发场景下可能产生重复客户
- 仓储内 `SaveChangesAsync` 会顺带提交 `DbContext` 中其他未提交的 tracked 实体变更，导致意外的部分提交
- 单元测试 mock 时难以察觉此问题，因为 mock 仓储通常不模拟 `SaveChangesAsync` 的副作用

### 修复方案

1. 仓储方法仅 `_dbSet.AddAsync` / 修改实体属性，不调用 `SaveChangesAsync`
2. 由服务层在事务内统一调用 `_unitOfWork.SaveChangesAsync()`
3. 对于需要返回自增主键的场景，调用方需显式调用 `SaveChangesAsync`
4. 分批迁移，优先处理高频调用路径（CustomerRepository、RefreshTokenRepository）
5. 编写架构测试禁止仓储层调用 `SaveChangesAsync`

### 影响范围

涉及 11 个仓储文件、30+ 处调用、以及所有调用这些仓储的 Service 层方法。建议分批迁移，每批修复后运行完整测试套件验证。

---

## TD-03 ReportService 报表全量内存化（含 SQLite 参数上限 Bug）

**优先级**: P0 | **工作量**: 大（1 周） | **类别**: 性能

### 问题位置

- [ReportService.cs:78-132](../NoneadCSS.Application/Services/ReportService.cs#L78) `CalculateFirstResponseStatsAsync` - 全量会话 ID + 全量线程加载到内存做循环计算
- [ReportService.cs:186-274](../NoneadCSS.Application/Services/ReportService.cs#L186) `BuildUserPerformanceAsync` - **无日期过滤**的活跃会话全量加载
- [ReportService.cs:197-207](../NoneadCSS.Application/Services/ReportService.cs#L197) - `activeConvIds` 无 `Take` 也无日期范围

### 问题描述

```csharp
// ReportService.cs L89-104
var convIds = await convQuery.Select(c => c.Id).ToListAsync();   // 全量会话 ID
if (convIds.Count == 0) return (0, 0);

var threads = await _unitOfWork.Threads.GetQueryable()
    .AsNoTracking()
    .Where(t => convIds.Contains(t.ConversationId))              // SQLite 999 参数上限！
    .Select(t => new { t.ConversationId, t.Type, t.CreatedAt })
    .ToListAsync();                                               // 全量线程加载到内存

foreach (var convId in convIds)                                  // O(N×M) 内存循环
{
    var convThreads = threads.Where(t => t.ConversationId == convId)
        .OrderBy(t => t.CreatedAt).ToList();
    var firstCustomer = convThreads.FirstOrDefault(t => t.Type == ThreadType.Customer);
    var firstReply = convThreads.FirstOrDefault(t => t.Type == ThreadType.Message);
    if (firstCustomer != null && firstReply != null) { ... responseTimes.Add(hours); }
}
```

`BuildUserPerformanceAsync` 更严重：`activeConvQuery` **完全没有日期范围过滤**，会加载系统中所有"活跃"会话（已累计数月的未关闭会话）。系统运行越久，活跃会话池越大，报表耗时线性增长。

### 风险评估

- **高 - SQLite 999 参数上限 Bug**: `Contains` 生成 `WHERE ConversationId IN (...)`，季度报表 convIds 可达数万，**直接抛 `SqliteException`**
- 内存复杂度 O(N×M)：每会话做 `Where + OrderBy + FirstOrDefault` 线性扫描
- 同一报表周期内 `BuildUserPerformanceAsync` 会再次执行类似逻辑，重复消耗
- 活跃会话池随系统运行时间线性增长，报表耗时不可预测

### 修复方案

将首次响应时间计算下推到 SQL 层：

```csharp
var stats = await _unitOfWork.Threads.GetQueryable()
    .Where(t => convIds.Contains(t.ConversationId))
    .GroupBy(t => t.ConversationId)
    .Select(g => new {
        ConversationId = g.Key,
        FirstCustomer = g.Where(t => t.Type == ThreadType.Customer).Min(t => t.CreatedAt),
        FirstReply = g.Where(t => t.Type == ThreadType.Message).Min(t => t.CreatedAt)
    })
    .ToListAsync();

// 在数据库侧计算时间差，避免内存遍历
```

- 增加日期范围过滤（如最近 90 天内创建的活跃会话）
- 复用同一聚合 SQL，避免 `CalculateFirstResponseStatsAsync` 和 `BuildUserPerformanceAsync` 重复扫描 Threads 表
- 验证修复后报表口径与现有报表一致

### 参考文件

- `c:\noneadcss\NoneadCSS.Application\Services\ReportService.cs`

---

## TD-04 ConversationService 批量操作工作流触发（架构调整）

**优先级**: P1 | **工作量**: 大（1-2 周） | **类别**: 性能架构

### 问题位置

- [ConversationService.cs:407-417](../NoneadCSS.Application/Services/ConversationService.cs#L407) `BulkUpdatePriorityAsync` 循环内逐会话触发工作流
- 同样模式存在于 `BulkUpdateStatusAsync`、`BulkDeleteAsync` 等所有批量操作
- 关联问题: [WorkflowService.cs:883](../NoneadCSS.Application/Services/WorkflowService.cs#L883) 循环内重复查询同一 Mailbox

### 问题描述

```csharp
// ConversationService.cs L407-417
// 工作流按会话逐个触发（工作流引擎内部设计为每会话独立执行，此处循环不涉及数据库事务）
foreach (var conversationId in toUpdateIds)
{
    try
    {
        await _workflowService.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerPriorityChanged);
    }
    catch (Exception ex) { _logger.LogError(ex, ...); }
}
```

`ExecuteForConversationAsync` 内部链路: `GetByIdTrackedAsync`（含 5 个 Include + AsSplitQuery）→ `GetActiveByMailboxAsync` → foreach workflow { `BuildContextFromConversation`（含 Mailbox 查询）+ per-action 执行 + 事务 }。

### 风险评估

- **高**: 批量更新 50 个会话 × 3 个工作流 ≈ **250+ 次 DB 往返 + 150 个事务**
- 阻塞主流程，用户批量操作超时
- `BuildContextFromConversation` 在工作流循环内被调用，同一会话的 `conversation.MailboxId` 不变，N 个工作流 → N 次相同的 Mailbox 查询

### 修复方案

1. **短期**: 工作流触发改为后台作业，批量操作仅入队、不阻塞主流程。注意：曾存在的 `TriggerActionJob` 类从未在 `AddQuartz` 注册、也没有任何调用方，已在 2026-11-21 的全项目清理中移除（留底在仓库外）；本条仍需按 §7.1 的注册方式重新实现一个真正接线的作业
2. **长期**: 工作流引擎支持批量上下文构建（一次加载多个会话 + 它们的 workflows，复用 Mailbox 缓存）
3. 同时修复 [WorkflowService.cs:883](../NoneadCSS.Application/Services/WorkflowService.cs#L883) 循环内重复查询同一 Mailbox - 在 `ExecuteWorkflowsForConversationAsync` 循环前查询一次 Mailbox，通过闭包或参数传入

### 参考文件

- `c:\noneadcss\NoneadCSS.Application\Services\ConversationService.cs`
- `c:\noneadcss\NoneadCSS.Application\Services\WorkflowService.cs`
- `c:\noneadcss\NoneadCSS.Application\Services\WorkflowActions\ComplexActionHandlers.cs`

---

## TD-05 UserRoleResolver 权限解析完全无缓存

**优先级**: P0 | **工作量**: 中（3-5 天） | **类别**: 缓存

### 问题位置

- [UserRoleResolver.cs](../NoneadCSS.Application/Services/UserRoleResolver.cs) 整个类
- `GetPermissionsAsync` (L128-160) 每次调用 4-5 次 DB 往返
- `CountUsersInRolesAsync` (L174-187) 循环内 N+1 查询
- `GetUsersInRoleAsync` (L94-124) 单角色 3 次 DB 往返

### 问题描述

```csharp
// UserRoleResolver.cs L128-160
public async Task<List<string>> GetPermissionsAsync(int userId, int? mailboxId = null)
{
    var user = await _unitOfWork.Users.GetByIdAsync(userId);      // 每次查 DB
    if (user == null || user.DeletedAt != null) return new List<string>();
    if (user.Role == UserRoleType.SuperAdmin) return new List<string> { "*" };

    var permissions = new HashSet<string>();
    var roleIds = await GetRoleIdsAsync(userId);                  // 内部 3 次 DB 查询
    if (roleIds.Count > 0)
    {
        var roles = await _unitOfWork.Roles.GetByIdsAsync(roleIds);  // 再查一次
        // ...
    }
    return permissions.ToList();
}

// L174-187 - N+1 查询
public async Task<Dictionary<int, int>> CountUsersInRolesAsync(IEnumerable<int> roleIds)
{
    foreach (var roleId in roleIdList)
    {
        var userIds = await GetUserIdsInRoleAsync(roleId);       // 每角色一次 DB 往返
        result[roleId] = userIds.Count;
    }
    return result;
}
```

### 风险评估

- **高**: `HasPermissionAsync` 被 `[Permission("xxx")]` 授权过滤器在**每个需要授权的 API 请求**上调用
- 单次 `GetPermissionsAsync` 涉及 4-5 次 DB 往返（Users + UserRoles + Roles + 可选 scoped）
- 典型页面加载触发 5-10 个 API 请求，每请求多次权限检查 → **每页面加载数十次相同用户的权限查询**
- 角色与权限变更频率极低（一天几次），但查询频率极高（每秒数十次），是缓存优化中收益最高的单项
- `CountUsersInRolesAsync` 被 `RoleService.GetPagedAsync` 调用，每页角色列表触发 N 次 DB 往返（实际 3N 次，因 `GetUserIdsInRoleAsync` 内部 3 次查询）

### 修复方案

1. 注入 `IMemoryCache`
2. 缓存 key: `perms:{userId}:{mailboxId ?? 0}`，TTL 5 分钟
3. 在 `RoleService.AssignRoleAsync` / `BatchAssignRoleAsync` / `UpdateAsync` / `DeleteAsync` 中主动失效对应用户的缓存
4. 在 `MailboxService.UpdateMailboxAsync`（修改 MailboxUsers）中失效相关用户缓存
5. 同时修复 `CountUsersInRolesAsync` 改为单次 SQL 聚合:

```csharp
var counts = await _unitOfWork.UserRoles.GetQueryable()
    .Where(ur => roleIdList.Contains(ur.RoleId))
    .GroupBy(ur => ur.RoleId)
    .Select(g => new { RoleId = g.Key, Count = g.Select(x => x.UserId).Distinct().Count() })
    .ToDictionaryAsync(x => x.RoleId, x => x.Count);
```

### 参考文件

- `c:\noneadcss\NoneadCSS.Application\Services\UserRoleResolver.cs`
- `c:\noneadcss\NoneadCSS.Application\Services\RoleService.cs`
- `c:\noneadcss\NoneadCSS.Application\Services\MailboxService.cs`

---

## TD-06 ConversationThread.MessageId 缺唯一约束（数据完整性）

**优先级**: P0 | **工作量**: 中（3-5 天） | **类别**: 数据库迁移

### 问题位置

- [NoneadCSSDbContext.cs:1472-1473](../NoneadCSS.Infrastructure/Persistence/NoneadCSSDbContext.cs#L1472) 仅普通索引非唯一
- 查询入口: [ThreadRepository.cs:22-32](../NoneadCSS.Infrastructure/Repositories/ThreadRepository.cs#L22) `FindByMessageIdAsync` 使用 `FirstOrDefaultAsync`
- 调用方: 所有 IngestionService（Email/Feishu/WeChat/WhatsApp/DingDing）

### 问题描述

```csharp
// NoneadCSSDbContext.cs L1472-1473
// ConversationThread.MessageId：支撑邮件线程关联查询（高频）
modelBuilder.Entity<ConversationThread>()
    .HasIndex(t => t.MessageId);  // ← 非唯一索引！
```

```csharp
// ThreadRepository.cs L22-32
public async Task<ConversationThread?> FindByMessageIdAsync(string messageId)
{
    // ...
    .FirstOrDefaultAsync(t => t.MessageId == messageId);
}
```

所有 IngestionService 通过 `FindByMessageIdAsync` 检查是否已存在，然后 INSERT。这是典型的 TOCTOU（Time-of-Check to Time-of-Use）竞态条件。

### 风险评估

- **高 - TOCTOU 竞态条件**: 检查存在性 → INSERT 之间存在时间窗口
- 并发 webhook 回调（如飞书/钉钉消息重试）可能导致两条相同 MessageId 的 Thread 入库
- 数据库层无唯一约束兜底
- 后续 `FindByMessageIdAsync` 会返回第一条，导致重复消息显示给客户

### 修复方案

1. **数据清理**: 先查找并合并重复 MessageId 的 Thread

```sql
-- 查找重复
SELECT "MessageId", COUNT(*) FROM "Threads"
WHERE "MessageId" != ''
GROUP BY "MessageId" HAVING COUNT(*) > 1;

-- 合并/删除重复（业务复杂，需评估每条 Thread 是否有 attachments）
```

2. **添加唯一过滤索引**:

```csharp
modelBuilder.Entity<ConversationThread>()
    .HasIndex(t => t.MessageId)
    .IsUnique()
    .HasFilter("\"MessageId\" != ''");  // 排除空 MessageId（如手动创建的 Note）
```

迁移脚本（SQLite 使用 partial index）:
```sql
CREATE UNIQUE INDEX "IX_Threads_MessageId_Unique" ON "Threads" ("MessageId") WHERE "MessageId" != '';
```

3. **业务代码**: 在 IngestionService 中捕获 `DbUpdateException`（唯一约束冲突）作为幂等性回退:

```csharp
catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
{
    // 已被并发插入，重新查询返回
    return await _threadRepository.FindByMessageIdAsync(messageId);
}
```

### 附带修复: DingDingClient Token 缓存

[DingDingClient.cs:24-27](../NoneadCSS.Infrastructure/Services/DingDing/DingDingClient.cs#L24-L27) 使用实例字段缓存 token（`_cachedAccessToken`、`_tokenExpiresAt`），多实例部署完全不共享，且 DingDingClient 通过 `IHttpClientFactory` 注入可能是 Transient 生命周期，每次调用都会重新获取 token。建议:

1. 将 DingDingClient 改为 Singleton 生命周期，或将 token 管理抽取到独立的 `IDingDingAccessTokenService`（与 Feishu/WeChat 模式一致）
2. 改用 `IMemoryCache` 至少达到 Feishu/WeChat 水平
3. 进一步实现数据库持久化（新建 `ExternalTokens` 表）

### 参考文件

- `c:\noneadcss\NoneadCSS.Infrastructure\Persistence\NoneadCSSDbContext.cs`
- `c:\noneadcss\NoneadCSS.Infrastructure\Repositories\ThreadRepository.cs`
- `c:\noneadcss\NoneadCSS.Application\Services\EmailIngestionService.cs`
- `c:\noneadcss\NoneadCSS.Application\Services\FeishuIngestionService.cs`
- `c:\noneadcss\NoneadCSS.Application\Services\WeChatIngestionService.cs`
- `c:\noneadcss\NoneadCSS.Application\Services\WhatsAppIngestionService.cs`
- `c:\noneadcss\NoneadCSS.Application\Services\DingDingIngestionService.cs`
- `c:\noneadcss\NoneadCSS.Infrastructure\Services\DingDing\DingDingClient.cs`

---

## TD-07 高并发表缺少 RowVersion + 全部 Controller 无 API 版本控制

**优先级**: P1 | **工作量**: 大（1-2 周） | **类别**: 破坏性变更  
**7a. 乐观锁进度**: ✅ **已完成（v2.2.3 起数据库/EF/异常体系落地；服务层与前端已覆盖，2026-11-23 复核）**；**7b/7c 未完成**

### 7a. 高并发表乐观锁（v2.2.3 已实施 — P3 热修复）

**完成日期**: 2026-08-17 | **关闭版本**: v2.2.3（P3 追加）| **落地范围**: DB + EF + 异常体系 + 日历/聊天/客户/知识库服务层入口

**位置**: [NoneadCSSDbContext.cs](../NoneadCSS.Infrastructure/Persistence/NoneadCSSDbContext.cs)

#### 已落地内容（P3 完成）

当前已配置乐观锁的实体从 5 个扩展到 **13 个**：
- 已有的（v2.2.0 基线）：`Conversation.Version`、`ConversationThread.Version`、`Mailbox.Version`、`CalendarEvent.RowVersion`、`TimeEntry.RowVersion`
- **新增 8 个（v2.2.3 P3）**，统一使用 `byte[]? RowVersion` + `.IsRowVersion().ValueGeneratedOnAddOrUpdate()`：

| 实体 | 对应表 | 高并发场景 | 服务层入口适配 |
|---|---|---|---|
| `User` | Users | 同账号多点登录并发写 FailedLoginAttempts/LockoutEnd | DB 列已具备；✅ 服务层 Check 已接入（`CryptoCheckHelper`，ifMatch + 409 最新快照） |
| `Customer` | Customers | GetOrCreateByEmailAsync 并发；客服编辑画像 + 渠道同步回写 | ✅ UpdateCustomerAsync 已接受 ifMatch + 返回 latest 快照 |
| `ChatSession` | ChatSessions | 客户/客服并发写 LastActivityAt/UnreadCount/评分/关闭 | ✅ UpdateSessionAsync 已接受 ifMatch + 返回 latest 快照 |
| `ChatMessage` | ChatMessages | 撤回 / 已读 / 编辑消息并发 | ✅ UpdateMessageAsync 已接受 ifMatch + 返回 latest 快照 |
| `ApiKey` | ApiKeys | UsageCount/LastUsedAt 高并发 API 计数 lost update | DB 列已具备；✅ 服务层 Check 已接入（`CryptoCheckHelper`，ifMatch + 409 最新快照） |
| `Webhook` | Webhooks | TriggerCount/Success/Fail/LastTriggeredAt 高并发回调 | DB 列已具备；✅ 服务层 Check 已接入（`CryptoCheckHelper`，ifMatch + 409 最新快照） |
| `KnowledgeCategory` | KnowledgeCategories | 分类树拖拽/改名/迁移冲突 | ✅ UpdateCategoryAsync 已接受 ifMatch + 返回 latest 快照 |
| `KnowledgeArticle` | KnowledgeArticles | 多人同时编辑 + 浏览点赞计数 | ✅ UpdateArticleAsync 已接受 ifMatch + 返回 latest 快照 |

**配套落地**：
- 通用异常体系：新增 `ConcurrencyException`（`entityType / entityId / latest` 三件套），`CalendarConcurrencyException` 改为派生类保持二进制兼容。
- `GlobalExceptionMiddleware` 捕获 ConcurrencyException → HTTP 409 + `errorType=concurrency_conflict`（日历模块保留 `calendar_concurrency_conflict` 别名）。
- `ConcurrencyCheckHelper.Check` 封装"存在性 → 行版本匹配 → 冲突构造 latest → Warn 日志"四步标准流程。
- 生产 DDL 补丁工具 `_DbPatchRowVersion`（net10.0 控制台）：对 < v2.2.3 的老库按事务幂等 `ALTER TABLE "T" ADD COLUMN "RowVersion" BLOB NULL`，8 张表一次 COMMIT。2026-08-17 已对 Production/data/noneadcss.db 执行 APPLIED ✅

#### v2.2.4 后续（仍列为技术债务）

1. **User / ApiKey / Webhook 服务层补 Check 调用**：数据库列 + EF 配置已到位，下次迭代为 UsersController.UpdateAsync / ApiKeys 相关接口 / WebhooksController 补 `ConcurrencyCheckHelper.Check` 调用 + DTO rowVersion / ifMatch 字段 + 前端适配。
2. **前端聊天 / 客户 / 知识库编辑弹窗补 ifMatch + 409 latest 自动合入**：✅ 已完成——前端 `frontend/src/api/modules/engagement.ts`（聊天/客户/知识库互动）、`frontend/src/api/modules/timeTracking.ts` 与 `frontend/src/pages/calendar/store.ts` 均发送 `If-Match`，409 时把服务端 latest 写回本地状态；日历仍是最完整的一处参考实现。
3. **RowVersion 初始为 NULL 的数据迁移**：SQLite ALTER TABLE 不能带 BLOB DEFAULT，首次 SaveChangesAsync 时 EF 自动生成 8 字节值；v2.2.4 可补一个后台 Job 一次性把所有 NULL RowVersion 回填为初始 GUID+时间戳字节，避免首次更新时的小概率竞态窗口。

### 7b. API 版本控制缺失

**位置**: 全部 57 个 Controller 使用 `[Route("api/[controller]")]` 无 `/v1/` 前缀

**当前配置**:
```csharp
[ApiController]
[Route("api/[controller]")]
public class ConversationsController : ControllerBase
```

**风险评估**:
- 路由无 `/v1/`、`/v2/` 前缀
- 项目未配置 `Microsoft.AspNetCore.Mvc.Versioning` 或 `Asp.Versioning.Mvc`
- 一旦 API 契约变更（如 DTO 字段重命名、返回结构变更），无法向后兼容
- 客户端（Web/移动 App/第三方集成）升级被迫同步

**修复方案**:

1. 引入 `Asp.Versioning.Mvc` NuGet 包
2. 在 `Program.cs` 配置:

```csharp
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
});
```

3. Controller 改为:

```csharp
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class ConversationsController : ControllerBase
```

4. 迁移策略: 保留 `api/[controller]` 旧路由（兼容）+ 新增 `api/v1/[controller]` 路由
5. 文档: Swagger 通过版本选择器区分

### 7c. 附带修复: 双迁移系统并存

**位置**:
- EF Core 迁移: `c:\noneadcss\NoneadCSS.Infrastructure\Migrations\` (33 个 C# 迁移)
- 原始 SQL 迁移: `c:\noneadcss\NoneadCSS.Api\Migrations\` (5 个 SQL 文件)

**问题**:
- `Api/Migrations` 下的 SQL Server 方言脚本在 SQLite 数据库上无法执行:
  - `2026_08_03_add_whatsapp_module.sql` 使用 `GETUTCDATE()`、`IF NOT EXISTS ... BEGIN...END`（SQL Server 语法）
  - `2026_08_04_add_dingding_module.sql` 同上
  - `2026_08_04_add_feishu_module.sql` 同上
  - `2026_07_14_add_email_thread_and_folder_fields.sql` 使用 SQLite 方言（双引号、ROWID）
- 同一份"模块种子"逻辑在 EF Core C# 迁移和原始 SQL 中都有，存在重复维护
- `Api/Migrations` 下的 SQL 不在 `__EFMigrationsHistory` 中跟踪，运行状态不可见

**修复方案**:
1. 将 `Api/Migrations` 下的种子数据迁移统一改为 EF Core C# 迁移（通过 `migrationBuilder.Sql()` 嵌入 SQLite 方言）
2. 或保留原始 SQL 但统一方言为 SQLite（用 `datetime('now')` 替换 `GETUTCDATE()`，用 `INSERT OR IGNORE` 替换 `IF NOT EXISTS ... BEGIN...END`）
3. 明确部署文档: 哪些由 `dotnet ef database update` 执行，哪些由人工 SQL 执行

### 参考文件

**实体定义**:
- `c:\noneadcss\NoneadCSS.Domain\Entities\ChatSession.cs`
- `c:\noneadcss\NoneadCSS.Domain\Entities\User.cs`
- `c:\noneadcss\NoneadCSS.Domain\Entities\Customer.cs`
- `c:\noneadcss\NoneadCSS.Domain\Entities\ApiKey.cs`
- `c:\noneadcss\NoneadCSS.Domain\Entities\Webhook.cs`

**DbContext**:
- `c:\noneadcss\NoneadCSS.Infrastructure\Persistence\NoneadCSSDbContext.cs`

** Controllers**:
- `c:\noneadcss\NoneadCSS.Api\Controllers\` (全部 57 个 Controller，2026-11-23 复核)

**迁移文件**:
- `c:\noneadcss\NoneadCSS.Infrastructure\Migrations\`
- `c:\noneadcss\NoneadCSS.Api\Migrations\`

---

## 修复优先级与排期建议

| 编号 | 优先级 | 工作量 | 建议排期 | 前置依赖 |
|---|---|---|---|---|
| TD-01 | P0 | 3-5 天 | 第 1-2 周 | 无 |
| TD-03 | P0 | 1 周 | 第 1-2 周 | 无 |
| TD-05 | P0 | 3-5 天 | 第 1 周 | 无 |
| TD-06 | P0 | 3-5 天 | 第 2-3 周 | 需数据清理窗口 |
| TD-02 | P1 | 1-2 周 | 第 3-5 周 | TD-01 完成后更安全 |
| TD-04 | P1 | 1-2 周 | 第 4-6 周 | 可与 TD-02 并行 |
| TD-07 | P1 | 1-2 周 | **7a DB+服务层 4 模块已落地 v2.2.3；7b API 版本控制 + 7c 双迁移系统合并 / User+ApiKey+Webhook 服务层 + 前端 3 模块适配 排入 v2.2.4+** | 需数据库迁移窗口（7a 已执行 2026-08-17 ✅） |

### 关键路径与并行性

- **关键路径**: TD-01 → TD-02（事务架构稳定后再做仓储层重构）
- **可并行**: TD-03 / TD-05 / TD-06 互不依赖，可分配给不同开发人员
- **风险最高**: TD-06（涉及生产数据清理，建议在低峰期执行）
- **回归测试重点**: TD-01（宏执行）、TD-02（所有调用仓储的 Service）、TD-06（所有 IngestionService）

### 验收标准

每项修复完成后需通过:
1. 后端 Release 编译 0 错误（允许既有可空警告）
2. 单元测试全部通过（当前基线: 895 项测试用例，891 通过，0 失败，4 跳过）
3. 前端 TypeScript 类型检查 0 错误
4. 前端 Vite 生产构建成功
5. 新增的测试用例覆盖修复点

---

## 附录: 相关联的已修复问题

以下问题已在 2026 Q3 审计中修复，列入此处供参考避免回归:

| 问题 | 修复文件 | 说明 |
|---|---|---|
| ChatService AsNoTracking + Update 反模式 | `ChatService.cs` | 改用 `GetByIdTrackedAsync` |
| FeishuSettingsProvider sync-over-async | `FeishuSettingsProvider.cs` 等 3 个 | 添加后台异步刷新 + 启动预热 |
| CustomReportService 全表加载邮箱 | `CustomReportService.cs` | 改为 `FirstOrDefaultAsync` |
| DashboardService 活跃邮箱无限制 | `DashboardService.cs` | 添加 `.Take(100)` |
| ImportExportService 全表导出 OOM | `ImportExportService.cs` | 添加 AsNoTracking |
| UserService 头像 URL 未校验 | `UserService.cs` | 调用 `ValidateAndNormalizeAvatarUrl` |
| SatisfactionTokenHelper 时序攻击 | `SatisfactionTokenHelper.cs` | 改用 `FixedTimeEquals` |
| 钉钉/飞书超时硬编码 | `DingDingOptions.cs` / `FeishuOptions.cs` | 配置化 `StatusCheckTimeoutSeconds` |
| KnowledgeArticleRepository 只读查询未 AsNoTracking | `KnowledgeArticleRepository.cs` | 添加 AsNoTracking |
| 飞书事件签名/Token 校验绕过 | `FeishuController.cs` | 强制校验 |
| 钉钉消息去重逻辑失效 | `DingDingIngestionService.cs` | 修复查询条件 |
| WhatsApp Webhook 签名空值绕过 | WhatsApp 相关 | 强制校验 |
| ConversationTagServiceTests 断言不一致 | `ConversationTagServiceTests.cs` | 修正 `Times.Exactly(1)` |
| WhatsAppServiceTests Mock 设置未生效 | `WhatsAppServiceTests.cs` | 调整 Mock Setup |
| 前端 6 处 useEffect/setTimeout cleanup | `Knowledge.tsx` 等 | 添加 cancelled 守卫和 cleanup |
| 前端 3 处冗余类型断言 | `Feishu.tsx` / `DingDing.tsx` / `WhatsApp.tsx` | 移除 `as unknown as` |
| Mailboxes.tsx 局部 fetch 遮蔽 | `Mailboxes.tsx` | 重命名为 `fetchMailboxes` |
| AuditLogs.tsx 死代码 | `AuditLogs.tsx` | 清理 |
| WeChatClient 硬编码代理 URL | `WeChatClient.cs` | 配置化 |
| FeishuAccessTokenService 日志泄露 token | `FeishuAccessTokenService.cs` | 移除 TokenPrefix |
| Stream Client Dispose sync-over-async | `DingTalkStreamClient.cs` / `FeishuStreamClient.cs` | 直接 `WebSocket.Dispose()` |

---

## 审计后补充修复（2026-08-06 已落地，不列入 Q4 待办）

> 以下问题在 2026-08-05 审计报告中未命中，是 2026-08-06 用户实际使用反馈 + 运行环境差异分析发现并已完成修复的问题，特此记录保持审计完整性。**均已修复，不再列为 Q4 技术债务。**

### Post-TD-01 联系方式提取后 Conversation.CustomerEmail 未回填（功能完整性）

**发现日期**: 2026-08-06 | **修复日期**: 2026-08-06 | **类别**: 数据流完整性

#### 问题位置（4 IngestionService × 3处 = 12 处）

| 渠道 | 文件 |
|------|------|
| 飞书 | `FeishuIngestionService.cs` L223 调用点 + L1115 方法签名 + L1207 回填 |
| WhatsApp | `WhatsAppIngestionService.cs` L244 + L619 + L711 |
| 微信 | `WeChatIngestionService.cs` L462 + L916 + L1008 |
| 钉钉 | `DingDingIngestionService.cs` L202 + L556 + L647 |

#### 问题描述

四渠道的 `TryExtractContactInfoFromMessageAsync` 方法提取到邮箱后，仅写入 `Customer.Emails` 集合，未同步更新 `Conversation.CustomerEmail` 字段。导致：
- 会话关闭时，`Conversation.CustomerEmail` 仍为空 → `ResolveRecipientsAsync` 返回空列表
- `send_email` 动作记录"未解析到有效收件人"并跳过，会话关闭通知 + 满意度调查邮件完全不发送
- 客户已在对话中明确提供了邮箱，但系统未使用，造成用户体验断层

#### 为何 8/5 审计未命中

- 审计主要做了静态代码扫描（字段写入路径 + DTO 映射链路），`CustomerEmail` 有邮件渠道、前端编辑、会话创建等多条写入路径，无法从静态分析中识别"某些写入路径缺失"
- 仅在特定业务场景（聊天渠道 + 客户在对话中提供邮箱 + 会话关闭）才能复现，属于端到端数据流完整性问题

#### 修复方案（已落地）

统一模式：**调用点传 conversation.Id → 方法签名新增 conversationId 参数 → 写入邮箱后用 GetByIdForUpdateAsync 查询空值回填**。与客户字段修改同事务持久化，空时才写，已有邮箱不覆盖。

---

### Post-TD-02 聊天渠道无邮箱会话关闭 → 满意度调查完全丢失（业务闭环）

**发现日期**: 2026-08-06 | **修复日期**: 2026-08-06 | **类别**: 业务闭环 / 服务质量数据完整性

#### 问题描述

微信 / 飞书 / WhatsApp / 钉钉四渠道创建的会话中，约 80% 客户没有真实邮箱（未在对话中提供，或企业内部用户未设置邮箱字段）。会话关闭时：
- 满意度调查链接仅通过 `send_email` 动作触达
- 无邮箱客户直接跳过 → 满意度样本偏差极大（只有留了邮箱的用户能参与）
- 服务质量数据（CSAT / NPS 基础）不可信

#### 为何 8/5 审计未命中

审计聚焦代码安全与性能问题，**业务闭环完整性**属于业务领域分析范畴，不在本次 5 维度审计范围内。用户实际关闭一个飞书/微信会话时才暴露出。

#### 修复方案（已落地）

1. **满意度 Token 双路径架构**：
   - `SatisfactionTokenHelper` 新增 `GenerateTokenByChannelId` / `ValidateTokenByChannelId`，用 `channel:{channelId}` 前缀隔离标识域
   - `ValidateTokenAsync` / `BuildMailVariablesAsync` 邮箱为空时自动回退 ChannelId 路径

2. **SendEmailActionHandler 聊天渠道回退**：
   - 新建 `IChatChannelMessageSender` 统一封装四渠道 Client
   - 收件人列表为空时，将模板渲染后的 `BodyText`（纯文本 + satisfaction_url）推送到对应聊天 App 单聊窗口
   - 失败仅记 Warn 日志，不阻断主流程

3. **双向闭环**：
   - 会话关闭通知（含满意度链接）→ 聊天渠道回退送达
   - 满意度提交后感谢通知（satisfaction-thanks 模板）→ 同样聊天渠道回退送达

---

### Post-TD-03 crypto.randomUUID() 非安全上下文兼容性 → 客户编辑按钮静默失效（P0 前端体验）

**发现日期**: 2026-08-06 | **修复日期**: 2026-08-06 | **类别**: 前端兼容性 / 错误静默丢失

#### 问题位置（5文件，14 调用点）

| 文件 | 调用点 |
|------|--------|
| `pages/Customers.tsx` | L251, L252, L1147, L1193（客户编辑 + 添加电话/网站） |
| `pages/CustomFields.tsx` | L640, L651（自定义字段选项） |
| `components/MacroEditor.tsx` | L148, L173, L190, L200（宏动作） |
| `pages/Workflows.tsx` | L1003, L1007, L1073, L1089（工作流条件/动作） |

#### 问题描述

- `crypto.randomUUID()` 仅在 [Secure Context](https://developer.mozilla.org/en-US/docs/Web/Security/Secure_Contexts)（HTTPS / localhost）下可用
- 大量用户通过局域网 IP（`http://192.168.x.x:5173`）或内网域名以 HTTP 方式访问 → `TypeError: crypto.randomUUID is not a function`
- React Error Boundary **明确不捕获事件处理器中的异常**（官方文档），用户仅感知"点击编辑按钮无反应"，Console 中静默报错
- **与客户 enrich 强相关**：客户 Phones / Websites 为空时 `[].map(() => crypto.randomUUID())` 返回空数组不触发调用，编辑反而正常；被 enrich 写入 phones 的客户（#63 #64 #65）才会触发

#### 为何 8/5 审计未命中

- 审计环境为 `http://localhost:5173`（安全上下文），该问题无法在审计默认环境复现
- 静态代码扫描很难将"调用 Web API → 非安全上下文不可用 → React 事件处理器静默失败 → 取决于运行时数据是否触发该调用点"整个链路关联起来

#### 修复方案（已落地）

1. **统一工具函数 `utils/id.ts` → `genId()`**：
   - 三层防护：`typeof crypto` → `typeof crypto.randomUUID === 'function'` → try-catch 包裹
   - Fallback：`Date.now().toString(36) + '-' + Math.random().toString(36).slice(2,11)`，碰撞概率在 React key 场景下可忽略

2. **全量替换 14 处调用点**：不仅仅是客户编辑页面，宏、工作流、自定义字段全部替换，避免相同模式在其他页面复现静默失败

---

### 已清理 Test Debts（3 编译 + 1 运行时）

| 问题 | 根因 | 修复 |
|------|------|------|
| WorkflowServiceTests / FolderServiceIntegrationTests / NotificationServiceTests 编译失败 | UnitOfWork 构造函数新增 `IMemoryCache` 参数，测试未同步 | 构造调用传 `new MemoryCache(new MemoryCacheOptions())` |
| DbQueryTest.QueryWeChatToken SQLite 运行时异常 | DataReader 未关闭即修改 `cmd.CommandText` | 修改前显式 `reader.Close()` |

---

## 部署与运行稳定性补充修复（2026-08-08 已落地，不列入 Q4 待办）

> 以下 4 项不来源于静态扫描，来源于 8/8 实际部署演练 + 生产目录重启 + 代码运行期观察；对可用性影响等同 P0/P1 安全问题，故记录并标记已修复。

### Post-TD-04 部署脚本全局杀同名进程 → 其他项目连带崩溃（运维安全 P0）

#### 问题描述
`deploy/restart-dev.ps1` 和 `C:\noneadcss_prod\restart-prod.ps1` 启动前用：
```powershell
Get-Process dotnet,node,nginx -ErrorAction SilentlyContinue | Stop-Process -Force
```
会把机器上**所有**同名进程一把杀掉：其他仓库的 dotnet 调试会话、VS Test host、Rider、系统级 nginx、其他前端 dev server 全被干掉。对多人共用的开发机或"单机器多项目"的单机部署场景等同于一次人为 DoS。

#### 修复方案（已落地）
新增 `Get-PidsByPort($Port)` + `Stop-ByPort($Port, $Name)`：
- `netstat -ano | Select-String "\s+LISTENING\s+"` 解析 LISTENING 行，正则匹配 `:$Port\s+.*LISTENING\s+(\d+)$` 拿到 Pid
- 仅杀这些 Pid（开发：5008/5171；生产：5009/5172/5173）
- Nginx 优先 `nginx -s stop -p <deploy>` 优雅关，等 3s 后再兜底
- 清理完后等 3s，再跑一次 `Get-PidsByPort`，若仍有残留再第二轮 Stop 并二次校验
- 最终用 `Test-NetConnection -ComputerName 127.0.0.1 -Port $P -InformationLevel Quiet -WarningAction SilentlyContinue` 确认端口释放

### Post-TD-05 生产重启脚本缺 Fail-Fast 密钥校验 → 启动 50s+ 才报错（运维可观察性 P0）

#### 问题描述
生产环境启动前不校验用户级环境变量 `JWT__Key` / `ENCRYPTION__Key` / `AUDIT__HmacKey` / `APP__SatisfactionTokenKey`；只要一项缺失，dotnet 要跑 10s 左右才在 Options validate 阶段抛异常，脚本等 40s HTTP 探测才报"5009 未就绪"，屏幕上错误信息笼统，排错需翻 `logs/backend-yyyyMMdd-HHmmss.log`，单次排错 50s+，而实际修复只需要一行 SetEnvironmentVariable。

#### 修复方案（已落地）
在 `restart-prod.ps1` 的 `[0/4]` 阶段 Fail-Fast 校验：
```powershell
foreach ($v in $requiredEnvVars) {
  $val = [Environment]::GetEnvironmentVariable($v.Name, "User")
  if ([string]::IsNullOrWhiteSpace($val)) { $missing += $v }
}
if ($missing.Count -gt 0) {
  Write-Host "缺失环境变量 $($missing.Count) 项，立即终止（不启动任何服务）。请运行以下 4 条命令后重试：" -ForegroundColor Red
  foreach ($m in $missing) {
    Write-Host ('  [Environment]::SetEnvironmentVariable("{0}", $env:{1}, "User")' -f $m.Name, $m.Name.Replace('__', '_')) -ForegroundColor Yellow
  }
  exit 1
}
```
同时 `$env:Xxx` 注入项从 6 项扩充至 12 项（补 JWT Issuer/Audience、APP__Url、WeChat 的 4 个 baseline 字段），避免 Options validate 抛"某字段为 null"。

### Post-TD-06 微信 Token 刷新 SemaphoreSlim 锁泄漏 → 运行一段时间后微信全失效（并发一致性 P1）

#### 问题位置
[WeChatAccessTokenService.cs](../NoneadCSS.Infrastructure/Services/WeChat/WeChatAccessTokenService.cs#L77-L204)

#### 问题描述
```csharp
await _refreshLock.WaitAsync(cancellationToken); // ← 被 OperationCanceledException / HttpRequestException 中断
try { /* ... */ }
finally { _refreshLock.Release(); }             // ← 不管拿没拿到，都会 Release → SemaphoreFullException / 计数错
```
首次刷新（正常网络、无取消）一切 OK；一旦某次刷新被取消或异常中断（比如 Host 停用时 IHostedService 的 CancellationToken 触发），锁计数器就错乱，后续并发请求就永远等在锁上。用户侧感知是"首次登录正常，微信用 1~2 天后全部 API 调用超时卡死"。

#### 修复方案（已落地）
引入 `bool lockAcquired` + Release 外层兜底 SemaphoreFullException：
```csharp
bool lockAcquired = false;
try {
    await _refreshLock.WaitAsync(cancellationToken);
    lockAcquired = true;
    // ... 双检缓存 + 刷新请求 ...
} finally {
    if (lockAcquired) {
        try { _refreshLock.Release(); }
        catch (SemaphoreFullException ex) {
            _logger.LogWarning(ex, "微信 Token 刷新互斥锁状态异常，已忽略，避免阻塞后续刷新。");
        }
    }
}
```

### Post-TD-07 ICurrentUserAccessor ↔ IMailboxService DI 循环依赖（DI 架构 P1，2026-08-07 已修，本轮复审确认 OK）

#### 问题描述
- `ICurrentUserAccessor` 构造需要 Scoped 内解析 IMailboxService（拿当前用户关联的 Mailbox）
- `IMailboxService` 的构造器里某些方法注入了 ICurrentUserAccessor
- 构造期双向引用 → 生产启用 DI 验证时启动抛 `AggregateException(A circular dependency was detected...)`；开发 Scoped 延迟解析表现为首次调用偶发死循环（Scoped GetRequiredService 互相嵌套）。

#### 修复方案（已落地，2026-08-07）
`MailboxService` 构造器注入 `IServiceProvider`，`ICurrentUserAccessor` 只在真正需要时 Scoped 内 `.GetRequiredService<ICurrentUserAccessor>()` 延迟取，彻底拆环。

---

## 自定义报表软删除与垃圾箱功能补充修复（2026-08-11 已落地，不列入 Q4 待办）

> 以下问题不在 8/5 审计 5 维度范围内，属于业务功能完整性缺口。2026-08-11 已完成修复，特此记录保持审计完整性。**已修复，不再列为 Q4 技术债务。**

### Post-TD-08 自定义报表缺少软删除和垃圾箱功能 → 删除操作物理删除不可恢复（功能完整性 / 数据安全）

**发现日期**: 2026-08-11 | **修复日期**: 2026-08-11 | **类别**: 功能完整性 / 数据安全

#### 问题描述

自定义报表（CustomReport）此前的删除操作为物理删除（`Remove` + `SaveChangesAsync` 直接从数据库移除记录），存在以下问题：

- **不可恢复**: 用户误删报表后无法找回，数据永久丢失
- **审计断层**: 删除后无法追溯曾存在过的报表配置与历史执行记录
- **与系统其他实体不一致**: `Macro` / `Module` / `TimeEntry` / `NotificationRule` / `NotificationTemplate` / `Sla` 等实体均已实现 `ISoftDelete` 接口，自定义报表是少数仍采用物理删除的实体
- **用户体验缺口**: 系统其他模块（如 `TimeEntry`）已有恢复/垃圾箱模式，自定义报表缺失同类能力

#### 为何 8/5 审计未命中

- 审计聚焦代码安全、性能、事务、并发、数据完整性等维度，"功能完整性缺口"（缺少软删除/垃圾箱能力）属于业务功能增强范畴，不在 5 维度审计范围内
- 物理删除路径在静态扫描中无异常模式，仅在与系统其他实体的软删除实现对比时才能识别差异

#### 修复方案（已落地）

1. **实现 `ISoftDelete` 接口**：
   - `CustomReport` 实体新增 `DeletedAt` 字段（`DateTime?`）
   - EF Core 查询过滤器自动排除 `DeletedAt != null` 的记录
   - 删除操作改为软删除（设置 `DeletedAt = DateTime.UtcNow`）

2. **垃圾箱功能（Trash / Recycle Bin）**：
   - **查询**: 列表接口新增 `includeDeleted` 参数，垃圾箱视图可查询 `DeletedAt != null` 的报表
   - **恢复**: `RestoreAsync` 将 `DeletedAt` 置为 `null`，报表重新出现在正常列表
   - **永久删除**: `HardDeleteAsync` 执行物理删除，仅对已在垃圾箱中的报表可用
   - **清空**: `EmptyTrashAsync` 批量永久删除垃圾箱中所有报表

3. **API 端点新增**：
   - `GET /api/custom-reports/trash` — 查询垃圾箱
   - `POST /api/custom-reports/{id}/restore` — 恢复报表
   - `DELETE /api/custom-reports/{id}/permanent` — 永久删除
   - `DELETE /api/custom-reports/trash` — 清空垃圾箱

#### 参考文件

- `c:\noneadcss\NoneadCSS.Domain\Entities\CustomReport.cs`
- `c:\noneadcss\NoneadCSS.Application\Services\CustomReportService.cs`
- `c:\noneadcss\NoneadCSS.Infrastructure\Repositories\CustomReportRepository.cs`
- `c:\noneadcss\NoneadCSS.Api\Controllers\CustomReportsController.cs`

---

---

## TD-09 已结项：P0/P1 审计项闭环（v2.2.0 发布）

> **关闭日期**：2026-08-11｜**关闭版本**：v2.2.0｜**类别**：批量闭环

本"TD 条目"实际是 **v2.2.0 版本的审计结项汇总**：2026-08-05 全面审计的 P0/P1 问题，全部在 v2.2.0 中修复并验收通过。

### 修复清单（与审计编号一一对应）

| 审计编号 | 级别 | 标题 | 已修复? | 主要变更文件 |
|----------|------|------|---------|--------------|
| S-H1 | P0-SECURITY | EncryptedStringConverter 解密失败返回密文（信息泄漏） | ✅ 已修复 | EncryptedStringConverter.cs / NoneadCSSDbContext.cs（DecryptSafely 返回空） |
| S-H2 | P0-SECURITY | AesEncryptionService 旧密钥回退无告警（迁移风险） | ✅ 已修复 | AesEncryptionService.cs（启动告警 + Interlocked 首次回退告警） |
| F-C3 | P0-FEATURE | 邮件入队失败日志结构化 | ✅ 已修复 | ConversationEmailJob.cs / EmailDispatcher.cs / EmailService.cs |
| F-C4 | P0-FEATURE | 用户恢复邮箱冲突检查 | ✅ 已修复 | UserService.cs（GetByEmailAsync 冲突 → 409） |
| F-C5 | P0-FEATURE | 报表硬删除校验（普通用户必须先软删） | ✅ 已修复 | CustomReportService.cs.HardDeleteAsync |
| D-C1 | P0-DATABASE | Conversation 热路径 N+1 优化 | ✅ 已修复 | ConversationRepository.cs（反向 Include 去除 + AsNoTracking 统一） |
| D-M1 | M-DATABASE | BaseController 统一 CurrentUserId 提取 | ✅ 已修复 | BaseController.cs（新增）|
| D-M2 | M-QUALITY | 移除 IEncryptionService.DecryptLegacy 对外签名 | ✅ 已修复 | IEncryptionService.cs / AesEncryptionService.cs |
| D-M3 | M-DATABASE | ISoftDelete 硬删除拦截器放行（关联记录不卡 FK） | ✅ 已修复 | NoneadCSSDbContext.SaveChangesAsync |
| S-M1 | P1-SECURITY | 生产端点分级保护（dev-only 路由生产 404） | ✅ 已修复 | TrackingLegacyController / FeishuController / WeChatController |
| S-M2 | P1-SECURITY | BaseController 拆箱安全 CurrentUserId（同 D-M1） | ✅ 已修复 | BaseController.cs |
| S-M3 | P1-SECURITY | ResetPassword 后失效重置 Token + 撤销 Refresh Token | ✅ 已修复 | AuthController.cs + UserService.cs |
| Q-M1 | P1-QUALITY | 移除 DecryptLegacy 对外 API（同 D-M2） | ✅ 已修复 | IEncryptionService.cs |
| Q-M2 | P1-QUALITY | WorkflowActionExecutor macro 未注册 500 → 400 | ✅ 已修复 | WorkflowActionExecutor.cs（GetService + null 校验） |
| F-M1 | P1-FEATURE | Conversation.Merge 前 Channel/ChannelId 一致性校验 | ✅ 已修复 | ConversationService.cs.MergeAsync |
| FE-M1 | P1-FRONTEND | useAuth.logout 先调后端 API 再 clear localStorage | ✅ 已修复 | useAuth.tsx |
| FE-M2 | P1-FRONTEND | 删除客户前置"会话也进回收站"提示 | ✅ 已修复 | CustomerManagement.tsx |
| FE-M3 | P1-FRONTEND | .gitignore 清理构建产物 | ✅ 已修复 | frontend/dist/** + frontend/.vite/** |
| I-M1 | P1-INFRA | start-prod.ps1 修复 secretsFile（中文注释 bug） | ✅ 已修复 | Production/start-prod.ps1 |
| I-M2 | P1-INFRA | appsettings.Production.json Urls = 5009 | ✅ 已修复 | NoneadCSS.Api/appsettings.Production.json |
| I-M3 | P1-INFRA | /chat/widget CSP frame-ancestors + try_files =404 | ✅ 已修复 | Production/nginx.conf |
| I-M4 | P1-INFRA | 数据库备份：日期命名 + 保留最近 7 天 | ✅ 已修复 | Production/backup-db.ps1 |

### 遗留技术债（未在 v2.2.0 / v2.2.1 修复，保留在本清单）

- **TD-01**：仓储层事务嵌套缺陷（架构级根因，需要 3-5 天改造 UnitOfWork）
- **TD-02**：仓储层普遍调用 SaveChangesAsync（30+ 处分层违约）
- **TD-03**：ConversationThreadService 巨型类拆分（>2500 行）
- **TD-04**：前端 Chat/ChatWidget 单组件逻辑爆炸
- **TD-05**：EmailService 三个 Send 方法重复逻辑
- **TD-06**：跨所有 Controller 的参数/异常处理复制粘贴
- **TD-07**：SignalR Hub 未做单元测试

### 版本发布与验证

- 发布标签：`v2.2.1`（v2.2.0 → 已更新至 v2.2.1）
- 部署目录：`C:\noneadcss_prod\`
- 健康检查：`/health`、`/health/ready` 全部 200
- 跨域嵌入：`css.example.com:5173/chat/widget` frame-ancestors 白名单生效 ✅
- Legacy 告警：启动日志首屏 Warning 出现 ✅
- 密文保护：敏感字段解密失败返回空字符串（无密文泄漏）✅
- v2.2.1 刷新触发器修复：Workflows/Macros/Calendar 共 16 处刷新触发器 bug 修复验证通过 ✅

---

**文档版本**: v1.6（TD-07 7a 乐观锁 8 实体 DB + 服务层 4 模块状态更新为 v2.2.3 已落地；v2.2.1 → v2.2.2 → v2.2.3 三版发布记录纳入；优先级排期表同步修正）  
**生成日期**: 2026-08-17  
**审计人**: NoneadCSS 项目审计  
**适用迭代**: 2026 Q4 专项迭代 + 2026-08 部署稳定性专项 + 2026-08 功能完整性补充 + v2.2.0 正式发布 + v2.2.1/v2.2.2 紧急修复发布 + v2.2.3（P3 乐观锁 + 日历回弹 + DDL 补丁 + 7 步 SOP 热修复发布）

---

## v2.2.1 更新（2026-08-11）

紧急修复版本，修复 Workflows / Macros / Calendar 三处前端"操作后列表/视图未刷新"的刷新触发器 bug（P0+P1 共 16 处）。

---

## Post-Review：2026-08-27 全项目功能审查 8 项 Bug 修复（BUG-01~08）

本次审查在 2026 Q3 审计基线之外补充发现并修复了 8 项功能 Bug，作为技术债务清单的后续跟进。所有修复已合并至 v2.2.8 发布，回归测试覆盖完整。

### 修复概览

| Bug 编号 | 优先级 | 模块 | 问题摘要 | 状态 |
|----------|--------|------|----------|------|
| BUG-01 | P1 | 客户导入 | 重复检查仅覆盖前 100 条 | ✅ 已修复 |
| BUG-02 | P1 | 导入导出 | 导出含软删除记录 + 客户截断 100 条 | ✅ 已修复 |
| BUG-03 | P1 | 报表导出 | CSV/Excel 公式注入 | ✅ 已修复 |
| BUG-04 | P1 | 文件夹 | 允许硬删除系统文件夹 | ✅ 已修复 |
| BUG-05 | P2 | 工作流 | `set_status` 关闭会话绕过 `conversation_closed` 触发器 | ✅ 已修复 |
| BUG-06 | P2 | SLA | `CalculateStatus` 除零风险 | ✅ 已修复 |
| BUG-07 | P2 | 邮件队列 | 无重试机制 + 孤立中间状态 | ✅ 已修复 |
| BUG-08 | P2 | 导入导出 | 软删除标签干扰重复检查 | ✅ 已修复 |

### 关键架构改进

- **BUG-05**：引入 `PendingSideEffects` 模式延迟触发工作流，解决事务内循环依赖问题，为后续工作流触发器解耦提供参考模式
- **BUG-07**：`SendLogRepository.GetPendingAsync` 同时拾起 `Accepted` + `SendIntermediateError` 状态并加入 `AttemptCount < MaxRetryCount` 过滤，邮件队列获得可重试能力
- **BUG-08**：`ImportExportService` 调用 `IgnoreQueryFilters()` 绕过全局软删除过滤器，为其他需要跨软删除边界查询的场景提供先例

### 影响文件

- `CustomerRepository.cs`、`ImportExportService.cs`、`CustomReportExportHelper.cs`、`FolderService.cs`
- `SimpleActionHandlers.cs`、`SlaService.cs`、`SlaPolicy.cs`
- `SendLogRepository.cs`、`EmailQueueService.cs`

### 回归测试

| 测试文件 | 用例数 | 覆盖 Bug |
|----------|--------|----------|
| `EmailQueueServiceTests.cs` | 9 | BUG-07（队列拾起 / 重试 / 终态 / 孤立态恢复） |
| `SlaServiceTests.cs` | 17 | BUG-06（除零守卫 / NaN 防御） |
| `ImportExportServiceBug08Tests.cs` | 6 | BUG-08（软删除标签干扰 / IgnoreQueryFilters 必要性） |

### 与技术债务清单的关系

本批 Bug 修复**不属于** TD-01 ~ TD-07 范畴（后者为架构级债务），而是功能完整性的补充审查。修复过程中沉淀的架构改进（如 `PendingSideEffects` 延迟触发模式、`IgnoreQueryFilters` 跨软删除边界查询先例）可作为后续 TD-01（事务嵌套缺陷）和 TD-02（仓储层 SaveChanges 调用）修复的参考实现。

---

## Post-Review v2：2026-08-27 系统管理-审计日志完善与优化（v2.2.9）

### 改进概览

在 v2.2.8 全项目 Bug 修复基础上，对系统管理-审计日志模块进行功能完善与体验优化，新增 5 个能力维度，无新增技术债务，反而消解了若干既有功能缺口：

| 维度 | 改进内容 | 消解的既有问题 |
|------|----------|----------------|
| 多维过滤 | list / stats / export 三端点统一支持 8 维过滤，`ApplyFilters` 集中实现 | 三处过滤逻辑各自实现易漂移；缺少 IP / 关键字 / 日期范围过滤 |
| 全局统计 | 独立 `/stats` 端点返回全量聚合，不依赖当前页 | 原统计依赖当前页数据，分页后失真 |
| CSV 导出 | `/export` 端点服务端生成 UTF-8 BOM CSV，最大 10000 行 | 无导出能力，运维只能截图 |
| HMAC 完整性 | 单条 + 批量校验，篡改检测 + 前端可视化回写 | 签名生成已有但无校验入口，篡改不可见 |
| 关联日志 | 同实体 → 同用户回退追溯 | 无上下文关联，难以定位操作链路 |

### 架构改进沉淀

1. **集中过滤模式**：`ApplyFilters` 私有方法被 list / stats / export 三处复用，避免实现漂移。该模式可推广至其他需要多维过滤的仓储（如 `ConversationRepository` / `CustomerRepository`）。
2. **OutputCache VaryByQuery 全参数覆盖**：`AuditLogs` 缓存策略纳入全部 10 个查询参数（含分页），避免不同过滤条件被同一缓存命中。该实践应作为所有带过滤的 OutputCache 策略标准。
3. **批量校验回写列表**：`applyIntegrityToTable` 在批量校验后回写 `isTampered` 到当前列表项，避免用户需要打开每条详情才能看到校验结果。该模式可推广至其他批量操作（如批量状态变更回写）。
4. **声明顺序约束**：`useCallback` 依赖项必须在声明顺序上先于依赖它的 `useCallback`，否则 TS2448/TS2454。该约束应作为 React Hooks 编码规范。

### 影响文件

| 文件 | 改动类型 | 说明 |
|------|----------|------|
| `IAuditLogRepository.cs` | 新增接口 | `GetPagedAsync` / `GetStatsAsync` / `ExportAsync` |
| `AuditLogRepository.cs` | 实现 | `ApplyFilters` 集中过滤 + 三方法实现 |
| `IAuditService.cs` / `AuditService.cs` | 新增方法 | `GetStatsAsync` / `ExportCsvAsync` / `VerifySingleAsync` / `VerifyIntegrityAsync` / `GetRelatedAsync` |
| `AuditLogsController.cs` | 新增端点 | `GetStats` / `Export` / `VerifySingle` / `GetRelated` / `VerifyIntegrity` |
| `Program.cs` | 配置 | OutputCache 策略纳入新增过滤参数 |
| `AuditLogResponse.cs` | DTO 扩展 | `HmacSignature` / `IsTampered` + 5 个新 DTO |
| `AuditLogs.tsx` | 前端重构 | 完整功能实现 |
| `api/modules/system.ts` | API 扩展 | 5 个新方法 |
| `api/types/system.ts` | 类型扩展 | 4 个新类型 + 2 个字段 |

### 与技术债务清单的关系

本次审计日志完善**不引入新的技术债务**，反而通过集中过滤模式（`ApplyFilters`）和 OutputCache 全参数覆盖两个实践，为后续 TD-01（事务嵌套缺陷）和 TD-02（仓储层 SaveChanges 调用）的修复提供了可参考的实现模式。所有新增端点均标注 `RequireSuperAdmin` 权限，符合最小权限原则。

### 验证与部署

- **后端编译**：`dotnet build NoneadCSS.Api -c Release` → 0 errors
- **前端类型检查**：`tsc -b` → 0 errors
- **前端 Lint**：`eslint` → 0 errors
- **生产部署**：2026-08-27 14:53 部署至生产环境，后端 :5009 + 前端 :5172 + Nginx :5173 全部验证通过
- **端点验证**：`/api/auditlogs/stats` 与 `/api/auditlogs/export` 返回 401（鉴权生效），`/assets/AuditLogs-BimPYkpd.js` 返回 200 OK（45369 bytes，含完整新功能）

---

---

## Post-Review v3：2026-08-28 生产热修复 v2.2.9 — Outlook 用户级凭据 + 日历参会人 + 知识库 6 项 P1 闭环（T000099 / T000100~T000105）

### 修复概览

本次为生产使用反馈驱动的**紧急热修复批次**（共 7 个用例，全部 P1），集中在 **Outlook 同步配置模型重构**（1 项）+ **日历参会人保存数据链路**（1 项）+ **知识库五大功能缺口**（5 项）。所有问题均为用户实际操作时发现的非 Happy Path 场景，代码静态扫描未覆盖（原因见每项的"为何未命中"）。

| 用例编号 | 优先级 | 模块 | 问题摘要 | 根因归类 | 状态 |
|----------|--------|------|----------|----------|------|
| T000099 | P1 | 日历-Outlook 同步 | 操作失败 400 + 不知何处配置 Azure AD 凭据 | 配置模型：仅超级管理员系统级，非用户级 | ✅ 已修复（v2.2.9） |
| T000100 | P1 | 日历-参会人 | 保存报错，再次编辑数据为空 | 前后端 3 处：空数组提交 + JSON 反序列化 crash + 回填不完整 | ✅ 已修复（v2.2.9） |
| T000101 | P1 | 知识库-点赞 | 点赞失败：服务器错误 | Schema 漂移：生产库缺失 `KnowledgeArticleLikes` 联合主键表 | ✅ 已修复（v2.2.9） |
| T000102 | P1 | 知识库-Markdown | 渲染失败，加粗/斜体/链接/图片/行内代码/列表全部无样式 | marked@18 Renderer 设计陷阱：覆盖 block-level 方法会禁用 inline tokenizer | ✅ 已修复（v2.2.9） |
| T000103 | P1 | 知识库-附件 | 未保存的附件：查看页泄漏、编辑页丢失 | 草稿持久化：`editorAttachments` 未同步 localStorage | ✅ 已修复（v2.2.9） |
| T000104 | P1 | 知识库-全局搜索 | 搜索结果被遮挡在其他 label 下，未直观显示 | 下拉弹层 z-index + 父容器 overflow 截断 | ✅ 已修复（v2.2.9） |
| T000105 | P1 | 知识库-分类 | 禁用分类后仍在文章新建/编辑的分类下拉显示 | 前后端双重缺失：前端未过滤 + 后端未校验 | ✅ 已修复（v2.2.9） |

### T000099：Outlook 同步 Azure AD 凭据模型从系统级 → 用户级

**为何 8/5 审计未命中**：审计扫描了权限/字段加密/鉴权链路，未覆盖"配置作用域"的业务合理性——系统级 vs 用户级属于业务领域建模选择，不在安全/代码质量维度。

**修复规模**：
- 实体：`OutlookSyncSetting` 新增 4 列用户级 Azure AD 字段（`UserClientId`/`UserClientSecretEnc`/`UserTenantId`/`UserScopes`）
- DbContext：EF Core 映射 + ClientSecret 加密 `ENC:` 前缀
- SettingsProvider：新增 `GetForUserAsync` 实现 appsettings baseline + SystemOptions 数据库覆盖 + IMemoryCache 10 分钟缓存
- Controller：`/api/outlooksync/credentials` GET/PUT 一对用户级端点（无需 super admin）
- 前端：`OutlookSyncPanel.tsx` 移除 super admin 守卫，每个用户可独立填凭据

### T000100：日历参会人保存链路（前端 × 后端 × 3 根因同时命中）

**为何 8/5 审计未命中**：审计关注 DTO 字段安全和数据库 schema，未覆盖"前端提交空数组 + 后端反序列化 crash + 回填漏合并"这一条端到端数据流。

**三处根因 + 修复**：
1. 后端 `MappingExtensions.ToDto`：`AttendeesJson`/`ReminderEmails` 空值或非法 JSON 直接抛异常 → 新增 `SafeDeserializeList<T>` / `SafeDeserializeObject<T>` 两个容错扩展方法
2. 后端 `CalendarMutationService`：参会人与 `reminderEmails` CSV 字段单向写，互不同步 → 新增 `NormalizeAttendeesAndReminderEmails` 双写互补
3. 前端 `store.ts openEdit`：回填只看 DTO `Attendees` 不看 `reminderEmails` 字段 → 合并 `_attendeeEmailList` 与 DTO extras 两处来源；`EditorPanel.tsx` 提交时若 `attendees` 为空数组则不发送 payload 字段

### T000101：知识库点赞表缺失（Schema 漂移 P1）

**为何 8/5 审计未命中**：审计做的是 EF Core 模型静态扫描，迁移 `20260826160000_AddKnowledgeArticleLikes.cs` 存在但未在生产库执行（生产库升级路径是 DDL 热补丁而非 `dotnet ef database update`），属于"模型-实际库漂移"。

**修复双保险**：
1. **即时热修复**：Python/sqlite3 在生产库 + 开发库手工执行 `CREATE TABLE IF NOT EXISTS "KnowledgeArticleLikes" ("UserId" INTEGER NOT NULL,"ArticleId" INTEGER NOT NULL,"CreatedAt" TEXT NOT NULL,PRIMARY KEY("UserId","ArticleId"));` + 索引（SQLite DDL 即时生效，不重启后端）
2. **长期幂等补丁**：`Program.cs` 新增 `EnsureKnowledgeArticleLikesTableAsync` 启动必跑，下次冷启动或新环境部署自动补齐

### T000102：Markdown 渲染样式消失（marked@18 Renderer 陷阱）

**为何 8/5 审计未命中**：前端 Markdown 渲染路径通过 DOMPurify 安全检查已命中；marked 版本升级后内部 inline tokenizer 行为变更是第三方库的语义变更，属于"库升级隐性回归"，静态分析无法识别。

**修复模式**：
- **错误做法（原实现）**：同时覆盖 `paragraph`、`codespan`、`link`、`image`、`list`、`listitem` 等 block-level Renderer 方法 → marked@18 **禁用 inline tokenizer**，`**粗**` / `*斜*` / `` `code` `` / `[](url)` / `![](url)` 等语法完全不解析，显示原文
- **正确做法（新实现）**：仅覆盖 `heading` 和 `code` 两个 block 方法；其他元素用 `postAddTailwindClasses` 正则后处理追加 Tailwind class（`ul.list-disc`、`code.bg-slate-100`、`strong.font-semibold`、`em.italic`、`a.text-blue-600 underline` 等）；同时 `index.css` 新增 `.knowledge-content` 下 160+ 行 Tailwind 风格排版（亮/暗双模式）

### T000103：附件上传逻辑"查看页泄漏 + 编辑页丢失"（草稿持久化）

**为何 8/5 审计未命中**：附件上传的文件安全性（大小/MIME/扩展名/路径遍历）已覆盖；"未保存的草稿附件在 view/edit 两页表现不一致"属于跨页面状态管理的数据流设计问题。

**修复**（`Knowledge.tsx` 三处联动）：
1. 自动保存草稿（useEffect 防抖 1s 写 localStorage `knowledge-editor-draft`）→ **纳入 `editorAttachments`**（原只保存标题/正文/分类）
2. `openCreateArticle`：从 new-article draft 恢复附件
3. `openEditArticle`：抓取后端最新文章快照 → 合并 draft 中未保存的附件到 `editorAttachments`
4. "丢弃草稿"按钮：附件回滚到后端保存版本；"恢复草稿"按钮：附件重新合并

### T000104：全局搜索结果被遮挡（z-index + overflow 双重问题）

**为何 8/5 审计未命中**：下拉弹层 z-index 被父容器 `overflow-hidden` 截断属于**CSS 层叠上下文问题**，需在真实浏览器交互下观察。

**修复双策略**：
1. **下拉弹层增强**：z-index 从 z-40 提升至 `z-[60]` + 新增"查看全部结果"按钮（点击跳到主区域搜索结果 section）
2. **主区域独立搜索结果 Section**：当 `globalSearchResult !== null` 且在「文章管理 / 非垃圾箱」Tab 时直接渲染：渐变头部 + 关键词高亮 + 命中数 + 分页 + 清除搜索按钮

### T000105：禁用分类双保险（前端过滤 + 后端校验）

**为何 8/5 审计未命中**：`IsActive` 字段存在 + 服务层 CRUD 都正确；仅在"文章新建/编辑的分类下拉"这个特定使用路径漏掉过滤，属于功能 UI 关联遗漏。

**修复**：
1. 前端：`flatCategories` 保留全量（管理员分类管理页面用）；**新增 `activeFlatCategories`**（`IsActive === true`）替换文章筛选器 + 新建/编辑文章的分类下拉数据源
2. 后端：`KnowledgeService.CreateArticleAsync` 和 `UpdateArticleAsync` 新增校验——**分配禁用分类 → 报错 "所选分类已禁用"**（已有文章保持分配不变，避免禁用分类导致数据回滚异常）

### 配套 SOP 固化（发布 Checklist 双项新增）

1. **DDL 热补丁矩阵**（Checklist 17 项）：`Program.cs` 启动幂等补丁必须覆盖 `KnowledgeArticleLikes` / `CalendarEvents AttendeesJson+ReminderEmails` / `OutlookSyncSettings 4 列用户级凭据` / `KnowledgeCategories RowVersion`
2. **Vite IPv4 强制**（Checklist 17 项）：生产 `vite preview` 必须带 `--host 127.0.0.1`，否则 Nginx 反向代理前端 IPv6 `[::1]:5172` 会 502 Bad Gateway

### 工程实践沉淀（可跨项目复用）

| 实践 | 说明 |
|------|------|
| SQLite DDL 幂等补丁矩阵 | Program.cs 启动阶段按表/列粒度跑 `CREATE TABLE IF NOT EXISTS` / `ALTER TABLE ADD COLUMN`；**即时热修复 SQL 可先手工在 Python/sqlite3 执行（SQLite DDL 即时生效，不重启后端）**，再回填 Program.cs 避免下次回归 |
| marked@18 Renderer 陷阱 | 覆盖 paragraph/list 等 block-level 方法会禁用内部 inline tokenizer；正确模式是只改写 heading/code + post-processing |
| marked@18 Renderer 陷阱 | 覆盖 paragraph/list 等 block-level 方法会禁用内部 inline tokenizer；正确模式是只改写 heading/code + post-processing 正则加样式类 |
| 草稿附件持久化 | localStorage 草稿必须包含编辑器当前态的非保存数据（如附件），否则 view/edit 两页数据不同步 |
| 禁用数据双保险 | 任何"禁用/启用"实体，必须前端展示过滤 + 后端写入校验两条路径同时落地，缺一不可 |
| Vite IPv4 强制 | Windows 下 Vite 默认绑定 IPv6，Nginx 反代 IPv4 502；`--host 127.0.0.1` 解决 |

### 验证与部署（2026-08-28 已完成）

- **后端编译**：`dotnet publish -c Release` → 0 errors
- **前端构建**：`npm run build` → 78 文件，0 errors
- **数据库 DDL 补丁**：生产 + 开发库均补齐 `KnowledgeArticleLikes` 表、`KnowledgeCategories RowVersion` 列、`CalendarEvents AttendeesJson+ReminderEmails` 列、`OutlookSyncSettings 4 列`
- **生产健康检查**：后端 http://127.0.0.1:5009/health → 200；前端 vite preview http://127.0.0.1:5172/ → 200；Nginx 反代 http://127.0.0.1:5173/ → 200
- **E2E 验证**：7 个用例在生产环境 UI + API 均通过（含参会人二次编辑回填、点赞重复、Markdown 6 种格式亮/暗、附件 view/edit 一致、搜索结果主区域显示、禁用分类下拉不出现）

### 与技术债务清单的关系

本批 7 个用例**不属于** TD-01 ~ TD-07 架构债务范畴，属于"功能完整性 + schema 漂移热修复 + 第三方库升级隐性回归"的混合场景。修复中沉淀的三个 SOP（**SQLite DDL 幂等补丁矩阵**、**禁用数据双保险模式**、**marked@18 Renderer 陷阱模式**）应纳入后续所有版本的发布检查清单，已同步写入 `CONTRIBUTING.md §发布清单` 第 17 项。

---

**文档版本**: v1.9（追加 2026-08-28 Post-Review v3 章节：v2.2.9 生产热修复 7 项 P1——Outlook 用户级凭据 + 日历参会人 + 知识库 6 项闭环；更新发布基线 + 配套 SOP 固化）
**更新日期**: 2026-08-28
**审计人**: NoneadCSS 项目审计
**适用迭代**: 2026 Q4 专项迭代 + v2.2.8（全项目功能审查 8 项 Bug）+ v2.2.9（审计日志完善 + Outlook 凭据重构 + 日历参会人修复 + 知识库 6 项 P1 热修复 + DDL 幂等补丁 + IPv4 SOP 固化）

---

## 2026-11-23 复核记录（v2.3.0 工作树）

- **TD-07a 状态修正**：原表把 User / ApiKey / Webhook 的「服务层 Check 调用」列为待办，复核确认 `UserService` / `ApiKeyService` / `WebhookService` 已调用 `CryptoCheckHelper`；前端 ifMatch 已覆盖日历、时间追踪与知识库互动（`engagement.ts`）。RowVersion 仍为 13 个实体。
- **TD-04 备注**：`TriggerActionJob` 从未注册到 Quartz，已于 2026-11-21 随死代码清理移除；「工作流动作触发」需求仍待正式作业实现，不应误读为已完成。
- **计数校正**：控制器 57 个（含 Setup/OAuth/Localization/Realtime/SendLogs/Folders/TimeTracking/System/EmailTest/UserProfile 等后续新增）；`RequireSuperAdmin` 295 处、`Permission` 191 处。
- 本次只做文档事实校正，不改变任何技术债务的排期。
