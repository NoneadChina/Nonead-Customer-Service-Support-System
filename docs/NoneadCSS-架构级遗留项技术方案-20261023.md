# NoneadCSS 架构级遗留项技术方案（2026-10-23）

> 背景：2026-10-21《功能全景总结》（内部留存、不在发布包内）中标注为「架构级/需外部条件」的 6 项，经逐项代码核查后给出可执行方案。
> 这些项目**不适合在一次提交里硬改**：都涉及跨模块契约、数据迁移或全局行为。本文给出每项的根因、改法、工作量、风险与验收标准，供排期直接执行。

> **实施状态（2026-11-23 复核，v2.3.0 工作树）**：6 项均未按本方案实施。#14 的**导出**已部分落地（`NoneadCSS.Application/Services/ImportExportService.cs:50-51` 支持 conversations 与 knowledge_articles，`SupportsImport=false`）；#27 仍为纯特性替换、可随时开工；#25 无 `AddVersionColumn` 迁移、#5 无 `WorkflowRecoveryHostedService`、#11 群聊仍未处理；#21 维持「不做」。当前控制器 57 个，`RequireSuperAdmin` 295 处、细粒度 `Permission` 191 处（见下文 #27 的计数校正）。

---

## #25 SQLite 乐观锁：把「原值比较」升级为「版本号递增」

**现状与根因**
`NoneadCSSDbContext.cs:1428-1434`、`:2035-2054` 把关键实体的 `IsRowVersion()` 降级为 `IsConcurrencyToken()`。原因是 SQLite 不支持 SQL Server 那种由数据库自动生成的 `rowversion` 列，EF 只能退化为"比较原值"——两个并发请求只要改的不是同一字段，都能成功写入，等于乐观锁失效。

**推荐方案（应用层版本号，SQLite 标准做法）**
1. 给需要并发控制的实体（Conversation、CalendarEvent、Mailbox、User、SlaPolicy 等，即现在挂了 `IsConcurrencyToken` 的那批）统一加一个 `int Version` 属性，并在 DbContext 里配置为 `IsConcurrencyToken()`。
2. 加 `SaveChanges` 拦截器：任何 `EntityState.Modified` 的实体在保存前 `Version++`。
3. 新增 EF 迁移（`AddVersionColumn`，默认值 0），**迁移必须手工冷备生产 DB**。
4. 前端已有的 `If-Match` / 409 回写逻辑保持不变——它们的契约就是"版本号"，只是现在版本号真的会变了。

**工作量**：2–3 人天（实体改造 + 拦截器 + 迁移 + 并发单测）
**风险**：中。`Version++` 若漏在某个 SaveChanges 路径上会导致 409 误报；建议先只覆盖 CalendarEvent（已有完整 409 处理）验证，再推广。
**验收**：两个并发 PUT 同一实体，后到者返回 409；同一实体连续两次更新都成功且 Version 递增。

---

## #5 工作流任务持久化：用执行日志做「中断可见 + 可重跑」

**现状与根因**
工作流由事件同步触发（`WorkflowService.ExecuteForConversationAsync:852`），任务只在内存队列里，进程重启时**未执行完的直接消失且无任何痕迹**。

**推荐方案（不新建表，复用 WorkflowExecutionLogs）**
1. 每个工作流开始执行前先写一条 `Status="pending"` 的 `WorkflowExecutionLog`（含 WorkflowId、ConversationId、Trigger、ExecutedAt），立即 SaveChanges。
2. 执行结束把该记录更新为 `success` / `failed` / `partial`（现有逻辑不变，只是改为 Update 而非 Add）。
3. 新增启动期 `WorkflowRecoveryHostedService`（`BackgroundService`）：开机时把 `Status="pending"` 且 `ExecutedAt < 现在-10分钟` 的记录标记为 `Status="interrupted"`，并写一条站内通知给超管。
4. **不自动重跑**：动作可能已部分执行（发信、改状态），盲目重跑会重复副作用。UI 上提供"按日志手动重跑"按钮，由人判断。

**工作量**：1.5–2 人天（日志写入改造 + HostedService + 一个重跑端点 + 前端按钮）
**风险**：低。不改变执行语义，只是让"丢失"变得可见。
**验收**：执行中途 kill 进程，重启后日志里出现 `interrupted` 记录，超管收到通知；手动重跑能成功。

---

## #14 导入导出扩面：先做「导出」，导入分实体推进

**现状与根因**
`ImportExportService.cs:32-41` 只支持 customers / tags / canned_responses / mail_templates / email_signatures（users 仅导出）。工单、知识库、宏没有批量通道，"搬家"只能手写脚本。

**推荐方案（导出先行，导入逐实体评估）**
1. **导出**（风险低，先做）：在 `ImportExportService` 的实体注册表里增加
   - `conversations`：导出 CSV（id、subject、status、priority、customerEmail、assignee、createdAt、closedAt），**不导正文**（体积与隐私）
   - `knowledge_articles`：导出 Markdown 打包（标题、分类、状态、正文），因为知识库天然是文档
   - `macros`：导出 JSON（名称、动作数组），用于跨环境复制配置
2. **导入**：只增加 `conversations.tags`（给已有工单打标签）这种"低副作用"场景。**工单正文导入默认不做**——它会绕过邮件摄取与工作流触发，造成数据链路断裂。
3. 前端 `ImportExport.tsx` 的实体列表由后端 `/entities` 驱动，无需改页面。

**工作量**：导出 1–1.5 人天；导入按需再评估
**风险**：低（导出只读）
**验收**：三类实体都能导出且能被 Excel/Pandas 正确解析；导出体积在 10 万行内不超时（需分片或流式写）。

---

## #27 授权粒度：把只读查询从超管降为 permission

**现状与根因**
594 个端点里 346 处 `[RequireSuperAdmin]`、只有 110 处细粒度 `[Permission]`（2026-10-23 生产 OpenAPI 快照口径）。结果是"只要不是超管，大量只读页面都进不去"——这与上一轮修的权限映射问题叠加，让非超管体验很差。
> 2026-11-23 代码实测：57 个控制器中 `RequireSuperAdmin` 295 处、`Permission` 191 处；端点总数以生产 OpenAPI（594）为参照，代码侧特性数量变化不影响本方案的替换思路。

**推荐方案（分批，按风险从低到高）**
1. **第 1 批（只读查询，零风险）**：`AuditLogs`（已有 `audit_logs.view`）、`SendLogs`（`send_logs.view`）、`SystemOptions` 的 GET（`settings.view`）、`Modules` 的 GET（`modules.view`）——这些在 RoleService 里都已有对应权限键，只是控制器还在用 `RequireSuperAdmin`。
2. **第 2 批（配置读取）**：`Webhooks`/`ApiKeys` 的 GET（`*_view`）。
3. **第 3 批（写操作）**：谨慎，逐个确认权限键语义后再改。
4. 每批改完跑一次角色权限回归：用"只读用户""客服专员"两个角色实测能进/不能进的端点清单。

**工作量**：第 1 批 0.5 人天（纯特性替换 + 回归），第 2–3 批各 1 人天
**风险**：中。改错会把敏感数据暴露给低权限角色——**只改 GET，不改写操作**是这条的硬约束。
**验收**：只读用户能查审计日志、发信日志、系统配置（只读），但仍不能改任何配置。

---

## #11 钉钉/飞书群聊接入

**现状与根因**
`DingDingIngestionService.cs:83-86` 明确「只处理单聊（群聊消息暂不处理）」；`FeishuIngestionService.cs:168-172` 只处理 p2p。客户在群里提问，系统收不到。

**推荐方案（分两步，先"不漏单"再"能回复"）**
1. **入站不漏**：识别群消息（钉钉 `conversationType=2` / 飞书 `chat_type=group`），把 `conversationId`（群 ID）作为客户标识的补充字段，建立/复用一条**以群为单位的会话**，客户名显示为「群名（N 人）」。
2. **区分内部群**：只处理机器人被 @ 的消息，且**跳过发送者是企业内部成员**的群消息（按 `staffId` 是否在 Users 表判断），避免内部讨论灌进工单。
3. **回复**：钉钉/飞书的群回复走 `sampleText` + `at` 结构，需要保持"回复给提问者"的语义，不能只发到群。

**工作量**：3–5 人天（两个渠道各 1.5–2 天 + 联调）
**风险**：**高**。群消息量远大于单聊，若不限流会冲垮工单列表；且"内部成员"判断一旦出错，会把内部聊天变成工单。**必须先在测试企业验证**。
**验收**：客户在群里 @ 机器人提问，系统生成一条会话并标记来源为群；客服回复能回到原群并 @ 提问者；内部成员在群里 @ 机器人不产生工单。

---

## #21 多租户 / 行级安全（RLS）

**现状与根因**
没有 TenantId、没有 RLS。数据隔离完全依赖每个 Controller/Service 主动调用 `ICurrentUserAccessor.CanAccessMailboxAsync`（如 `DashboardController.cs:36-38`、`SearchController.cs:89-163`）。新写一个端点漏调一次，就是一个越权洞——这也解释了审核报告 P2-05「疑似 IDOR」为何难以断言。

**结论：这是唯一一项建议「不做」的**
理由：当前是**单公司内部系统**，所有用户属于同一组织，多租户没有业务前提；而引入 TenantId 需要给 62 个 DbSet 里的绝大部分表加列 + 全量迁移 + 全局过滤器改造 + 所有既有数据回填，成本远超收益，且迁移失败风险高。

**替代方案（成本 1 人天，收益 80%）——「端点越权审计」**
写一个静态分析脚本：扫描所有 Controller 的 action，凡是**接收 mailboxId / conversationId / customerId 参数但方法体内没有调用 `accessor`** 的，列成清单人工复核。这比"上多租户"务实得多，也能把 P2-05 那种隐患一次性盘出来。

**工作量的诚实估计**：真做多租户 = 15–20 人天 + 高风险迁移；做审计脚本 = 1 人天。
**建议**：先做审计脚本；只有当出现"多个客户/多个子公司共用一套系统"的真实需求时，再启动多租户。

---

## 建议执行顺序

| 顺序 | 项目 | 理由 | 工作量 |
|---|---|---|---|
| 1 | #27 第 1 批 | 纯特性替换，立刻改善非超管体验 | 0.5 天 |
| 2 | #5 | 让"丢失"变可见，不改变执行语义 | 1.5 天 |
| 3 | #14 导出 | 只读、无副作用 | 1 天 |
| 4 | #25 | 先只覆盖 CalendarEvent，验证后再推广 | 2 天 |
| 5 | #21 审计脚本 | 替代多租户，盘出真实越权面 | 1 天 |
| 6 | #11 群聊 | 需要测试企业联调，最后做 | 4 天 |

**共性要求**：除 #27（纯属性替换）外，其余五项都涉及迁移或新后台服务，**每次上线都必须按 `docs/development.md` §18.11 手工冷备 + 四端健康检查**。
