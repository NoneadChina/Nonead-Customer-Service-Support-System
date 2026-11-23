# 渠道接线清单（Channel Registration Checklist）

实现一个新 IM 渠道时，除 `channel-integration-playbook.md` §1 的新建文件外，还必须落到下列**共享文件**登记点。
渠道实现者**不要直接改这些文件**（多人在跑，会互相覆盖），而是在交付报告的 `SHARED EDITS REQUIRED` 段落里逐条列出：文件路径 + 锚点行 + 要插入的确切代码。主理人（lead）按本清单统一串行接线。

> 本清单由 `arch-map` 全仓勘察得出，已按 WhatsApp / WeChat / 钉钉 / 飞书 四个现存渠道逐行核对。
> ⨸ = 只有该渠道具备对应能力时才需要（如 Stream 长连接、会过期的 access token）。

## A. 后端登记点（12 处）

| # | 文件:锚点 | 要做的事 | 漏掉的后果 |
|---|---|---|---|
| A1 | `NoneadCSS.Domain\Enums\SourceType.cs`（枚举体，`DingTalk = 10` 之后） | `<X> = 11/12/13/14`（Messenger 11、Telegram 12、Line 13、IMessage 14）——**已由 lead 写入** | 渠道无法标识 |
| A2 | `NoneadCSS.Application\Services\WorkflowActions\IChatChannelMessageSender.cs`：构造 :63-79、`SupportsChatChannel` :84-87、switch :132-139 | 注入 `I<X>Client`（必要时 + `I<X>SettingsProvider`）；`SupportsChatChannel` 加一个 `or`；switch 加一个分支；新增 `private async Task<ImMessageSendResult> Send<X>Async(string channelId, string text, int conversationId)`（模板 :181-193） | 自动化 / 队列消息发不出去 |
| A3 | `NoneadCSS.Domain\Repositories\IConversationRepository.cs` :190 / :199 附近 | `Task<Conversation?> FindActive<X>ConversationBy…Async(string externalId, TimeSpan within);` | 会话复用失效，每条消息建新会话 |
| A4 | `NoneadCSS.Infrastructure\Repositories\ConversationRepository.cs` :1057 / :1060 附近 | 一行包装：`=> FindActiveImConversationByChannelAsync("<channel>", id, Domain.Enums.SourceType.<X>, within)`（共用实现 :1034-1055） | 同上 |
| A5 | `NoneadCSS.Application\DependencyInjection.cs` :83-84（WhatsApp 段附近） | `AddScoped<I<X>Service, <X>Service>(); AddScoped<I<X>IngestionService, <X>IngestionService>();` | 控制器 500（无法解析） |
| A6 | `NoneadCSS.Infrastructure\DependencyInjection.cs` :330-333（WhatsApp 段附近） | `AddSingleton<I<X>SettingsProvider, <X>SettingsProvider>(); AddHttpClient<<X>Client>(); AddScoped<I<X>Client, <X>Client>();`。**若该客户端需要默认头/自定义 handler，必须用合并重载** `AddHttpClient<I<X>Client, <X>Client>().ConfigureHttpClient(...)`（:312-314、:354-358 的注释说明拆开写会静默丢掉 HttpClientFactory 配置） | 配置静默失效 |
| A6b ⨸ | 同上，飞书 :335-346 / 钉钉 :348-366 段 | 长连接渠道：`AddSingleton<I<X>StreamConnectionStatus, …>()`、命名客户端 `AddHttpClient("<X>Stream").ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30))`、`AddHostedService<<X>StreamHostedService>()` | 收不到消息 |
| A6c ⨸ | 同上，:337-338 | token 会过期的渠道：`AddHttpClient<<X>AccessTokenService>()` + `AddScoped<I…AccessTokenService, …>()` | token 不刷新 |
| A7 | `NoneadCSS.Api\Program.cs` :969-971 附近 | `builder.Services.Configure<<X>Options>(builder.Configuration.GetSection(<X>Options.SectionName));` | appsettings 段不绑定（钉钉例外：在 Infrastructure DI :349-350 绑定） |
| A8 | `NoneadCSS.Api\Program.cs` :389-417（WhatsApp 生产 fail-fast 块附近） | 复制该块：探测 `<X>__Enabled` → 收集缺失的密钥环境变量名 → `throw new InvalidOperationException("已启用 <X> 模块，生产环境必须设置以下环境变量：" + string.Join("、", missing));` | 生产带空密钥启动，运行期才报错 |
| **A9** | `NoneadCSS.Api\Middleware\CsrfProtectionMiddleware.cs` 豁免清单 :18-36（匹配器 :140：精确或 `path/` 前缀） | 加一行 `"/api/<x>",  // <X> Webhook 回调豁免` | **平台回调全部 400，代码根本不会执行**（最常见的接线事故） |
| A10 | `NoneadCSS.Api\Services\WeChatSettingsBootstrapService.cs`（`PreloadWhatsAppAsync` :143-172 模式） | 增加 `Preload<X>Async(sp)`，启动时预热 DB 覆盖后的配置 | 首次请求用旧配置 |
| A11 | `NoneadCSS.Api\DatabaseSeeder.cs`：**全量列表**（whatsapp :1958-1964）+ **增量数组** :2132-2247（四个新渠道在 :2190-2247；去重插入紧随其后）+ 角色权限 JSON 四处（agent :1004、viewer :1015，以及重播种副本 :1056 / :1063） | 加 Module 行（`SortOrder` 用 15-18，`IsActive = false`）；加 `"<x>.view"` 到四个 `PermissionsJson` | 老库里没有该模块；权限目录不认 |
| **A12** | `NoneadCSS.Application\Services\RoleService.cs` :511-512（通信权限组 :499-518） | `new() { Key = "<x>.view", Label = "查看 <X>", Description = "…" }, new() { Key = "<x>.manage", Label = "管理 <X>", Description = "…" },` | **权限目录缺项 ⇒ `[Permission("<x>.manage")]` 永远无法被授予**，页面只能超管访问 |
| A13 | `NoneadCSS.Api\Seeders\SystemOptionSeeder.cs` :87 | 增量元组 `("<x>.contact_collection_enabled", "true", "<x>")` | 默认行为开关缺失 |
| A14 | `NoneadCSS.Api\Seeders\WorkflowSeeder.cs` :177-184 与 :267-278 | 两条 IM 自动化流程的 `ConditionsJson` any-of 列表里加 `{"field":"source_type","operator":"equals","value":"<X>"}` | 关闭通知 / 好评感谢流程不覆盖新渠道 |
| A15 | `NoneadCSS.Application\Configuration\IngestionMatchOptions.cs` :9-21 | `public int <X>MatchWindowHours { get; set; } = 48;`（有回复窗口的话再加 `…WarningThresholdHours`） | 会话匹配窗口不可配 |
| A16 | `NoneadCSS.Application\DTOs\Responses\CustomFieldDto.cs` :129 与 :213；`NoneadCSS.Application\Services\CustomFieldService.cs` :23-25（显示名）、:32-34（启用键） | 两处渠道白名单加 `"<x>"`；显示名与启用键 `<x>.enabled`。注意这两处用的是 **SourceType 名小写**（钉钉写的是 `dingtalk`，不是模块名 `dingding`） | 自定义字段按渠道配置时选不到 |
| A17 | 测试：`NoneadCSS.Tests\Services\IngestionServiceCustomerFkTests.cs` :245 `[InlineData(SourceType.<X>)]` + :252-256 的 `<X> => "<x>"` 映射；`RoleServiceTests.cs` :821-822 风格的权限断言 | 补一行 | 新渠道的外键/去重回归属未覆盖 |

**注意 `Customer.Channel` 字符串必须与 A4/A17 里用的字面量完全一致**（钉钉先例：模块名 `dingding`，channel 字符串 `"dingtalk"`）。

数据库无需迁移：配置落 `SystemOptions`，客户身份落 `Customers.Channel/ChannelId`，正文 `ConversationThreads`，媒体 `Attachments`，出站队列 `ImMessageLogs`（`CK_ImMessageLog_Status IN (1,2,3,4)`，见 `NoneadCSSDbContext.cs:1990`）。只有新增实体字段时才要 EF 迁移，并且要同步放宽列出渠道枚举的检查约束。

## B. 前端登记点

| 文件:锚点 | 要做的事 |
|---|---|
| `frontend\src\routes\index.tsx` :43-47 / :106-109 | `const <X> = lazyWithRetry(...)` + `{ path: '/<x>', element: <X>, module: '<x>' }` |
| `frontend\src\config\moduleMenuMap.ts` :45-48 | `'/<x>': { moduleName: '<x>' }` |
| `frontend\src\components\Layout.tsx` :202（图标映射）/ :340-343（侧栏项） | `Icon.<x>` + `{ to: '/<x>', label: 'nav.<x>', icon: Icon.<x> }` |
| `frontend\src\utils\permissions.ts` :123-124 / :192 / :247 | `<X>View: '<x>.view'`、`<X>Manage: '<x>.manage'`、路由→权限、权限清单 |
| `frontend\src\api\types\system.ts` :337-405 模式 | `<X>` 的 DTO 接口（settings/status/reply/request） |
| `frontend\src\api\modules\system.ts` :29-36（import）/ :225-236（对象） | `export const <x>Api = { getSettings, updateSettings, getStatus, reply, test, … }` |
| `frontend\src\components\SourceTypeIcon.tsx` :3 / :46 | 图标分支 |
| `frontend\src\utils\format.ts` :354、:397、:413（三张映射表） | 数字 `sourceType` ↔ key |
| `frontend\src\pages\Conversations.tsx` :160（筛选）+ `isModuleActive('<x>')` 徽标段 :800-810 | 渠道筛选与徽标 |
| `frontend\src\pages\Roles.tsx` :146-147 / :241-242 | 权限标签 |
| `frontend\src\pages\AuditLogs.tsx` :283 | 实体类型标签 |
| `frontend\src\components\ThreadList.tsx` :494-495、配色 :544-587、后端类名变体 :665-676、徽标 :745-751、图集 :793-876 | 气泡样式/媒体类 |
| `frontend\src\pages\ConversationDetail.tsx` :227-232、回复态 :230-232、处理器 :549-580、分支 :1287、`<X>ReplyBox` :1565-1569 | 按数字 `sourceType` 选回复框 |
| i18n：**只有 en 与 zh-Hans 两套**，23 个并行命名空间文件，`locales/locales.test.ts` 强制对齐 | 每个渠道要加：`channels.<x>.*`（zh 微信段结构见 `channels.ts:324-483`）、`conversations.channel.<x>.*` 与 `conversations.detail.<x>*`（窗口文案）、`nav.<x>`、`format.sourceType.<x>`、`data.audit.entityType.<x>`、`teamPermissions.roles.perm.<x>.view/.manage` 及 `permDesc.*` |

i18n 大小写陷阱：页面 key 小写（`whatsapp`），会话 key 驼峰（`whatsApp`），钉钉前端 key 用 `dingtalk` 而模块/权限用 `dingding`。两套语言不对齐会让 `locales.test.ts` 失败。

## C. 已处理的历史包袱

- Telegram 模块种子（`NoneadCSS.Api/DatabaseSeeder.cs`）现已随实现落地：全量列表与增量数组各有一条 `telegram`（`IsActive = false`，SortOrder 2），管理员需在「模块管理」手动启用；实现包含 Webhook 与长轮询两种入站方式，测试见 `NoneadCSS.Tests/Services/Telegram*Tests.cs`（6 个测试类）。
- 四个新渠道的模块行统一由增量数组补全（`DatabaseSeeder.cs:2190-2247`），默认关闭；迁移脚本见 `NoneadCSS.Api/Migrations/2026_11_16_add_*_module.sql`。
- `WhatsAppOptions.ApiVersion` 默认 `v24.0`，而 Graph API 已到 v25.0：版本号必须是配置项，不能改成常量（Messenger 默认 v25.0）。
- 飞书仍**没有任何单测**（2026-10-09 全面审核当时的结论，2026-11-23 复核仍无 `*Feishu*` 测试类）。新渠道必须带 parser/service 测试，别再复制这个缺口。

## D. 交付报告格式（playbook §5 的补充）

`SHARED EDITS REQUIRED` 段落请按本清单编号（A2、A9、A12、B-… ）逐条回答"需要/不需要 + 确切代码"，缺项视为未接线。
