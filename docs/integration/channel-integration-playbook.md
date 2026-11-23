# IM 渠道接入手册（Channel Integration Playbook）

本文件是 NoneadCSS 即时通讯（IM）渠道的接入手册与已交付清单。既有渠道：Email、Web、Phone、Chat、Social、WeChat、WhatsApp、Feishu、DingTalk。
本批四个渠道 **Messenger、Telegram、LINE、iMessage** 已于 2026-11 落地并完成接线（SourceType 11–14），当前状态：

| 渠道 | SourceType | Module.Name（默认关闭） | 实现 | 前端页面 | 测试 |
|---|---|---|---|---|---|
| Messenger | 11 | `messenger` | 四件套 + WebhookParser + Controller；24h 窗口 / message_tag | `frontend/src/pages/Messenger.tsx` | Parser / Service / Ingestion |
| Telegram | 12 | `telegram` | 四件套 + WebhookParser + Controller；Webhook 与长轮询互斥 | `Telegram.tsx` | Parser / Service / Ingestion / 轮询 / 限流 / 审计 |
| LINE | 13 | `line` | 四件套 + WebhookParser + Controller；reply（25s）/ push、附件直链 | `Line.tsx` | Parser / Service / Ingestion / 重复投递守卫 |
| iMessage | 14 | `imessage` | 四件套 + WebhookParser + Controller；bridge / amb_msp / amb_apple_direct | `IMessage.tsx` | Parser / Service / Ingestion / 身份 / 限流 / JWT |

> 迁移脚本：`NoneadCSS.Api/Migrations/2026_11_16_add_{messenger,telegram,line,imessage}_module.sql`。模块行同时由 `NoneadCSS.Api/DatabaseSeeder.cs` 的增量数组按 Name 补全（新建库与既有库都能拿到），默认 `IsActive = false`，由管理员在「模块管理」启用。
> 平台事实与未证实项见同目录 `messenger.md` / `telegram.md` / `line.md` / `imessage.md`；用户侧配置步骤见用户手册 11.10–11.13。

> 参照实现 = **WhatsApp**（结构最干净、最新）。任何新渠道都应逐条镜像下面列出的 WhatsApp 文件。

## 0. 命名与枚举约定（固定，不要自行改动）

| 渠道 | SourceType 值 | Module.Name | 路由 | 前端页面 | Options 节名 | 权限 |
|---|---|---|---|---|---|---|
| Messenger | 11 | `messenger` | `/api/messenger` | `frontend/src/pages/Messenger.tsx` | `Messenger` | `messenger.view` / `messenger.manage` |
| Telegram | 12 | `telegram` | `/api/telegram` | `Telegram.tsx` | `Telegram` | `telegram.view` / `telegram.manage` |
| LINE | 13 | `line` | `/api/line` | `Line.tsx` | `Line` | `line.view` / `line.manage` |
| iMessage | 14 | `imessage` | `/api/imessage` | `IMessage.tsx` | `IMessage` | `imessage.view` / `imessage.manage` |

标识符统一用 PascalCase 类名前缀（`Messenger…`、`Telegram…`、`Line…`、`IMessage…`），字符串键统一用小写渠道名（`messenger`/`telegram`/`line`/`imessage`）。
客户身份沿用既有模型：`Customer.Channel`（渠道字符串）+ `Customer.ChannelId`（平台外部 ID），会话查找走
`ConversationRepository.FindActiveImConversationByChannelAsync(channel, externalId, SourceType.X, within)`。

## 1. 每个渠道要新建的文件（照抄 WhatsApp 同名文件）

```
NoneadCSS.Application/Configuration/<X>Options.cs                     # SectionName、凭据属性、CallbackPath、IsConfigured
NoneadCSS.Application/Services/I<X>Client.cs                          # 出站发送抽象
NoneadCSS.Application/Services/I<X>Service.cs                         # 业务服务（设置、状态、回复、测试连通性）
NoneadCSS.Application/Services/I<X>IngestionService.cs                # 入站处理抽象
NoneadCSS.Application/Services/I<X>SettingsProvider.cs                # 有效配置提供器 + static class <X>OptionKeys（Category="<x>"，键名小写下划线）
NoneadCSS.Application/Services/<X>Service.cs
NoneadCSS.Application/Services/<X>IngestionService.cs
NoneadCSS.Application/Common/<X>WebhookParser.cs                      # 纯解析（无 IO），便于单测
NoneadCSS.Application/DTOs/Requests/<X>Requests.cs
NoneadCSS.Application/DTOs/Responses/<X>Dto.cs
NoneadCSS.Infrastructure/Services/<X>/<X>Client.cs                    # HttpClient 实现，端点常量化
NoneadCSS.Infrastructure/Services/<X>/<X>SettingsProvider.cs
NoneadCSS.Api/Controllers/<X>Controller.cs                            # GET verify + POST 回调 + settings/status/reply/test
NoneadCSS.Api/Migrations/<YYYY-MM-DD>_add_<x>_module.sql             # Modules 行 + RolePermissions（whatsapp 版见 2026_08_03_add_whatsapp_module.sql）
NoneadCSS.Tests/Services/<X>WebhookParserTests.cs
NoneadCSS.Tests/Services/<X>ServiceTests.cs
NoneadCSS.Tests/Services/<X>IngestionServiceTests.cs
frontend/src/pages/<X>.tsx                                            # 渠道设置/会话面板
frontend/src/components/<X>ReplyBox.tsx                               # 会话详情回复框
```

## 2. 共享文件（**不要编辑**，只回报需要的改动）

以下文件被多个渠道并发修改会互相覆盖，统一由 lead 串行改。实现方在交付回报里给出
「SHARED EDITS REQUIRED」清单（文件 + 位置 + 需要新增的确切代码行）即可。

- `NoneadCSS.Domain/Enums/SourceType.cs`
- `NoneadCSS.Infrastructure/DependencyInjection.cs`（SettingsProvider/HttpClient/Scoped 注册、需要 HostedService 的渠道）
- `NoneadCSS.Application/DependencyInjection.cs`（`I<X>Service` / `I<X>IngestionService` AddScoped）
- `NoneadCSS.Api/Program.cs`（`Configure<<X>Options>`、生产环境密钥校验、迁移幂等检查）
- `NoneadCSS.Application/Services/WorkflowActions/IChatChannelMessageSender.cs`（`ChatChannelMessageSender`：`SupportsChatChannel` 的 SourceType 列表 + switch 分支 + `Send<X>Async`）
- `NoneadCSS.Api/appsettings.json` / `appsettings.Production.json`（**只放占位符，绝不放真实密钥**）
- `NoneadCSS.Tests/Services/IngestionServiceCustomerFkTests.cs`（`[InlineData(SourceType.X)]`）
- 前端：`src/routes/index.tsx`、`src/config/moduleMenuMap.ts`、`src/components/Layout.tsx`（导航 + 图标）、
  `src/utils/permissions.ts`（view/manage 常量、路由映射、权限清单）、`src/api/types/system.ts`、
  `src/api/modules/system.ts`、`src/components/SourceTypeIcon.tsx`、`src/utils/format.ts`（SourceType→显示名）、
  `src/components/ThreadList.tsx`、`src/pages/ConversationDetail.tsx`（回复框选择）、
  `src/pages/Conversations.tsx`（渠道筛选）、`src/pages/Roles.tsx`、`src/pages/AuditLogs.tsx`、
  `src/locales/{en,zh-Hans}/{channels,nav,teamPermissions}.ts`

**通用做法**：`grep -n WhatsApp`（或 `WeChat`）在上述每个共享文件里的全部命中点，就是新渠道需要镜像的位置。逐条对齐，不要遗漏。

## 3. 代码约定

- 目标框架 **net10.0**，`<Nullable>enable</Nullable>`，`<ImplicitUsings>enable</ImplicitUsings>`。注释与用户可见文案：代码注释中文，UI 文案走 i18n（en + zh-Hans 两套都要给）。
- **不引入新的 NuGet 包**。出站调用统一 `HttpClient`（`IHttpClientFactory`）+ `System.Text.Json`；需要长连接的渠道参照 DingTalk/Feishu 的 `*StreamHostedService` 模式。
- 配置读取一律走 `<X>SettingsProvider.Current`（合并 appsettings 基线 + `SystemOptions` 数据库覆盖，密钥值以 `ENC:` 前缀加密存储，用 `IEncryptionService.TryDecrypt`），模式与 `WhatsAppSettingsProvider` 完全一致（Singleton + `IMemoryCache` + `IServiceScopeFactory` 后台刷新）。
- 回调接口必须**快速返回 2xx 并入队异步处理**（Quartz `ProcessImMessageQueueJob` 已有），且**按平台消息 ID 幂等去重**。
- 签名校验：读取**原始请求体字节**后再做 HMAC/签名比对（恒定时间比较），绝不能先反序列化再重序列化。
- 媒体：入站附件在收到回调时立即下载转存（平台 URL/file_id 有短 TTL）；出站媒体优先 URL，需要上传的走平台上传接口。
- **绝不硬编码任何密钥、内部域名、真实租户信息**；`appsettings` 里只写空串或 `<REPLACE_ME>`。日志不得输出 token / 密钥 / 完整签名。

## 4. 每个渠道必须自带测试

`<X>WebhookParserTests`（真实样例报文 → 断言解析结果，含签名/去重/异常报文）、`<X>ServiceTests`（回复窗口/未配置/错误码路径）、
`<X>IngestionServiceTests`（新客户建会、老客户复用、去重、媒体）。测试不得访问网络；用 `HttpMessageHandler` 桩（见 `WeChatProxyTests.cs` 的 `SpyHttpMessageHandler` 写法）。

## 5. 交付回报格式（必须）

```
NEW FILES: <路径清单>
SHARED EDITS REQUIRED:
  - 文件:行位置 → 需要新增/修改的确切代码
TESTS ADDED: <路径清单> + 用例数
OPEN ISSUES / UNVERIFIED PLATFORM DETAILS: <列表>
```
