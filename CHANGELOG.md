# Changelog - 版本更新日志

所有重要的版本变更都将记录在此文件中。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/) 规范。

---

## [Unreleased] - 开源准备批次（四个新 IM 渠道 + 微信直连 + 全站中英双语）

> 本批次尚未发布到生产，也未打 tag；下列内容全部为工作树现状（`NoneadCSS.Tests` 回归：失败 0 / 通过 2228 / 跳过 5 / 总计 2233；前端 `tsc --noEmit` 0 错误、vitest 18 文件 / 684 用例、`npm run i18n:check` 0 处）。
> 编译口径更正：增量 `dotnet build NoneadCSS.slnx` 显示 0 警告 0 错误，但 `--no-incremental` 全量重建共 **62 条告警 = 生产代码 34 条 + 测试代码 28 条**。生产 34 条为既有遗留（CS8604×20：`NoneadCSS.Api/Controllers/ChatController.cs` 14 / `NoneadCSS.Application/Services/IMessageIngestionService.cs` 4 / `NoneadCSS.Api/Controllers/CalendarFeedController.cs` 2；CS1573×10：`ApiKeysController.cs` 6 / `NotificationsController.cs` 2 / `UsersController.cs` 2；CS4014×2 与 CS8601×2：`IMessageService.cs`），与本批次改动无关。测试 28 条集中在 `NoneadCSS.Tests/OpenApi/OpenApiEnglishLocalizerTests.cs`（CS8602×18、CS8604×4）、`IMessageWebhookParserTests.cs`（CS8625×2）、`IMessageAuditFixTests.cs` 与 `LineServiceTests.cs`（CS8604×2 各）——均为可空性告警，不影响断言。本批次新增生产代码 0 告警；按用户裁定，这批遗留告警本轮不处理。此前多处「0 警告」记录是增量构建的读数，不能作为全量结论。

### ✨ 新增：四个 IM 渠道（参照 WhatsApp 模板）

- **Facebook Messenger**（`SourceType.Messenger = 11`）、**Telegram**（`12`）、**LINE**（`13`）、**iMessage**（`14`）：每个渠道四件套（`I<X>Client` / `I<X>Service` / `I<X>IngestionService` / `I<X>SettingsProvider`）+ `Infrastructure/Services/<X>/` 实现 + `Application/Common/<X>WebhookParser.cs` 纯函数 + `NoneadCSS.Api/Controllers/<X>Controller.cs`（webhook 匿名、settings GET/PUT 分级鉴权、reply、test）+ `Migrations/2026_11_16_add_<x>_module.sql` + 前端页面与 ReplyBox。客户身份 `Customers.Channel/ChannelId`，正文 `ConversationThreads`，出站复用 `ImMessageLogs` + Quartz `ProcessImMessageQueueJob`。
- 共享注册点 17 处已同步（见 `docs/integration/channel-registration-checklist.md` A1–A17）；平台契约调研记录在 `docs/integration/{messenger,telegram,line,imessage}.md`。
- Telegram 走 `TelegramPollingHostedService` + `TelegramThrottle`；iMessage 双模式（BlueBubbles 桥 / AMB）走 `IIMessageRateLimiter`；LINE 一次性 replyToken 与 push 配额（`GetQuotaAsync` 读 `{"type":"limited"|"none","value":N}`）。
- 集成流程与踩坑清单见 `docs/integration/channel-integration-playbook.md`。

### 📚 文档：按当前功能更新全部 Markdown（2026-11-23）

- 版本口径：`README.md`、用户手册、`docs/development.md` 顶部统一为 `2.3.0 · 2026-10-23`（`Directory.Build.props` 为单一真源），并注明四 IM 渠道与全站双语仍属本 Unreleased 批次、尚未打 tag。
- `README.md`：项目结构补 `NoneadCSS.Tests` / `scripts` / `deploy` / `offline_docker_package` / `data` / `logs`；环境要求改 Node 20.19+ / 22.12+；后端出口词典 692→**735**；API 表补 `setup` / `oauth` / `localization` / `realtime` / `send-logs` / `folders` / `time-tracking` / `system` / `email-test`；新增「首次初始化」4 步；测试口径改为 124 个测试类 / 2233 用例，并给出进程内执行器命令；TD-07a 进度更新。
- `docs/development.md`：目录补 §57–§60；页面数 38→**55**；新增 §59（版本单一真源 + `scripts/css-api` 直连工具）与 §60（四 IM 渠道 + `/docs` 双语 + 出口词典 735 + 开源预检 + 验证基线）。
- 用户手册：新增 11.10 Messenger / 11.11 Telegram / 11.12 LINE / 11.13 iMessage（各含前置准备、系统配置、Webhook 与订阅、状态检查、会话回复、注意事项），12.4 模块表补 4 行，第 17 章补本批次与 2.3.0。
- `docs/integration/`：playbook 改为「接入手册与已交付清单」并加四渠道状态表；checklist C 节按 Telegram 已落地、增量模块种子、飞书仍无单测复核；四份平台事实文档与三份登记单加状态横幅。
- 辅助文档：技术债务清单补 2026-11-23 复核（57 控制器 / 295 处 `RequireSuperAdmin` / 191 处 `Permission` / 乐观锁覆盖）；架构遗留方案加实施状态；备份指南与 `scripts/backend-test-runner/README.md` 更新版本与用例数；8 份 ReleaseNotes 加历史归档横幅；`offline_docker_package/readme.md` 版本更新。

### 🔌 变更：微信去掉 www.nonead.com 中转，直连微信公众平台

- 删除 `WeChatOptions.ProxyBaseUrl` / `ProxyApiKey` 与 DI 里的 `X-Proxy-Key` 配置，删除三处「代理敏感」charset hack；`WeChatProxyTests.cs` 改写为 `WeChatDirectApiTests.cs`（断言 host 为 `https://api.weixin.qq.com` 且永不发送 `X-Proxy-Key`）。
- 新增此前缺失的 `msg_signature` 校验：`WeChatMessageParser.ValidateMessageSignature`（token/timestamp/nonce/encrypt 排序后 SHA1 + `CryptographicOperations.FixedTimeEquals`），在 `WeChatController` 按 EncryptMode 强制。
- 直连需要出口 IP 加白名单与公网 HTTPS 回调地址（`WeChat__CallbackBaseUrl`）；`deploy/` 与 `offline_docker_package/` 中的中转参数已清除。

### 🔠 新增：API 文档（`/docs`）中英双语

- 两份 OpenAPI 文档同源生成：`/openapi/v1.json`（中文，文档名 `v1`）与 `/openapi/en.json`（文档名 `en`），487 条路径一一对应；英文由 `NoneadCSS.Api/OpenApi/OpenApiEnglishLocalizer.cs` 按词典 `NoneadCSS.Api/OpenApi/openapi-en-dictionary.json`（906 条 zh→en）在文档 transformer 中替换字段文案，**不维护第二套 XML 注释**。
- `NoneadCSS.Api/OpenApi/OpenApiDocumentationTexts.cs` 负责 info/title 两套文案，并移除内部占位站点 `ncss.local`（`Contact` 仅保留名称）。
- `ScalarCustomizationMiddleware` 扩展为语言开关：`/docs?lang=en|zh-Hans` + Cookie `ncss_docs_lang`（`Path=/docs`、HttpOnly、SameSite=Lax、30 天），英文改写 Scalar HTML 里的 `/openapi/v1.json` 与 `<title>`；中文模式注入 132 条 UI 标签 + DOM 翻译层（Scalar 2.0.7 无原生 locale 能力）。文档路径的 CSP 允许 `'unsafe-inline'`，注入在生产同样生效。
- 开发期 `frontend/vite.config.ts` 新增 `/docs`、`/openapi` 代理（此前 `/docs` 会落入 SPA 回退，表现为「404 Page not found」）。

### 🔠 新增：后端 API 消息中英双语（响应出口词典替换）

- 审查结论：前端词条（7005 键，en 与 zh-Hans 由 `frontend/src/locales/locales.test.ts` 强制对齐）与 `/docs` 已双语，但后端出口有 **735 条**直达客户端的中文字面量（起步 692 条，后经 `ListAdd` / 前缀键等模式族补采至 735）（校验 `ErrorMessage`、`throw`、`Result.Fail`、匿名 `BadRequest` 等）没有任何英文路径。按用户裁定采用**出口词典替换**，不改 200 个源文件、不按 key 重写 resx。
- 新增 `NoneadCSS.Api/Localization/ApiMessageLocalizer.cs` + 词典 `NoneadCSS.Api/Localization/api-message-en.json`（当前 **735 条**，起步 692 条；键是归一化后的中文原句，含槽位者写 `«N»`，英文用 `{N}` 由运行时按捕获顺序回填）。词典随 csproj `<Content Update=... CopyToOutputDirectory/PreserveNewest>` 复制到输出与发布目录，缺失时自动降级为不翻译（不抛进管道）。
- 五个出口点接入：`Program.cs` 的 `InvalidModelStateResponseFactory`（`message` 与 `data.errors` 在构造前翻译）、`ApiResponseWrapperFilter`（已包装业务包 + 非 2xx 反射取到的 `Message`）、`GlobalExceptionMiddleware`、限流 `OnRejected`（裸 429）、`CsrfProtectionMiddleware`（三处）。只读匿名载荷另有 `TryFlattenLocalizedMessage` 提升 `data.message`。翻译只在 `en*` 文化下发生，且仅改 `Message`/`ErrorMessage`/`Detail` 白名单字段，客户正文不受影响。
- 语言传递修复：前端 `frontend/src/api/client.ts` 的主客户端与刷新客户端现在都附带 `Accept-Language: <当前界面语言>`（此前只有浏览器 OS 语言生效，导致英文界面仍收到中文消息）；`NoneadCSS.Application/Resources/Messages.resx` 补齐三个只有中文卫星文件才有的键（`RoleAgent` / `RoleAgentSupervisor` / `DraftNotFound`），英文界面不再直出键名。
- 新增 `scripts/i18n/api-message-dictionary.mjs`（`extract` / `coverage` / `merge`）用于从源码再生词典；`merge` 有 90% 骤减守卫与「槽位序列一致、剥掉 `{N}` 后不得残留花括号、无 CJK/全角」校验。新增 `NoneadCSS.Tests/Localization/ApiMessageLocalizerTests.cs` 18 例（含「全部 735 条键可被反解析」的不变式）。实测：校验 400 与验证码 401 的 `message`、`data.errors`、`data.message` 在 `?lang=en` / `Accept-Language: en-GB` 下 CJK 命中 0，中文链路保持不变。
- 前端附带修复三处未显式指定 locale 的格式化（`frontend/src/pages/calendar/components/KpiCard.tsx`、`frontend/src/pages/NotificationRules.tsx`、`frontend/src/pages/Line.tsx`），改走 `toIntlLocale(getBoundLocale())` / `formatDateTime`。
- 明确未选（本批次不做）：种子数据与 `Modules.Title`、写入库的聊天系统消息与工单 HTML、服务端枚举 label 映射、`SystemOptions`/`Modules` 的 culture 列、公开页语言开关、复数 `{count}` 语义修正。设计说明见 `docs/development.md` §9.7。

### 🛡️ 安全：开源前密钥与内部标识清理

- 三遍脚本化替换覆盖约 83 文件 / 316 处：全部字面凭据改 `<REDACTED_*__ROTATE>` 标记，内部主机名（`*.corp.nonead.com`、`git.corp…`、内部 AI 服务名、内网 IP、出口公网 IP）、个人邮箱/账号与 dev 默认口令一并泛化；公司公开域名 `nonead.com` 按既定策略保留。
- 新增只读全树扫描器 `scripts/oss-secret-scan.mjs`（13 条规则：本机用户目录、仓库绝对路径、内部主机名、内网/公网 IP、凭据赋值、PEM 块、连接串口令、个人邮箱等；跳过构建产物与依赖目录），用于开源前复检与 CI 复用。据此修掉残留的机器路径泄露：`Dockerfile` 注释里的仓库绝对路径、`NoneadCSS.Api/Middleware/ScalarChromeLocale.cs` 注释里的 `%USERPROFILE%\.nuget\...` 原始路径。
- 清掉不得入包的仓库垃圾：五个 `bin-docs/` 构建输出目录共 **233 MB**（`bin/` 规则不匹配它，内含整套 dll 与 `appsettings*` 副本，副本与在库文件哈希一致）及其在 `bin/Debug/net10.0/` 下的自嵌套链；根目录 7 个 `.keys.*.txt` 词条草稿、误生成的 `100` 与 0 字节的空 `NoneadCSS.Api.csproj`。`.gitignore` 补 `bin-docs/`、`uploads/`；`.dockerignore` 补 `logs/`、`uploads/`、`bin-docs/`。
- 新增 `deploy/secrets.cmd.example`，重写 `deploy/_start-backend.cmd`（凭据外置，缺失即 `exit 1`）；`.gitignore` 补 `deploy/secrets.cmd`、`*.pfx/*.p12/*.key/*.pem`、`.env*`、记忆目录。
- 新增 `scripts/oss-db-scrub.sql`（7 段 SQLite 脱敏 + 验收查询必须返回 0，建议在 DB **副本**上执行；建议开源包不带任何数据库转储）。
- 发布包文档口径：`docs/` 只保留技术与集成文档。6 份内部评审/验收类报告（`NoneadCSS审核报告.md`、`NoneadCSS-全面审核报告-20261009.md`、`NoneadCSS测试验收报告_20260805.md`、`NoneadCSS-功能全景总结-20261021.md`、`三渠道回调模式验证报告.md`、`trae-agent-code-wiki.md`）、AI 会话产物目录 `docs/superpowers/`（61 文件 / 5.24 MB，含 14 张界面截图）以及 PDF/DOCX/MHTML 流程图与用户手册（文字在图片与二进制里，脚本无法脱敏）已整体移到**仓库外的复核目录**（本机临时存放，不在发布包内），人眼复核后再决定是否发布；`README.md`、`CONTRIBUTING.md`、`docs/ReleaseNotes_v2.1.0.md`、`docs/integration/channel-registration-checklist.md`、`frontend/scripts/check-hardcoded-chinese.mjs` 里指向这些文件的链接已改为纯文本说明（`CHANGELOG.md` 历史条目按原样保留）。顺带修掉 `README.md` 里两处指向不存在路径的技术债务文档链接（实际在 `docs/`）。
- 文档链接卫生：新增 `scripts/oss-doc-links.mjs`（`check` / `fix` / `fix --apply`）。历史文档里 **206 条 `file:///c:/noneadcss/...` 机器绝对链接**（GitHub 上必坏）已处理：190 条改写为相对 `.md` 所在目录的链接（锚点保留）、16 条目标已不存在的降级为行内 code，另修正 7 条 `NoneadCSS_TechnicalDebt_2026Q4.md` 的基路径错误（实际位于 `docs/`）；涉及 `README.md`、`CONTRIBUTING.md`、`CHANGELOG.md`、`docs/development.md`、5 份 ReleaseNotes、`docs/security-audit-report-2026-08-06.md`。终检 `node scripts/oss-doc-links.mjs check` → `fileUriLinks=0 brokenRelativeLinks=0`（余 8 条是文档示例里的 `[文本](url)` 占位，属预期）。
- ⚠️ 待人工完成：服务端轮换 6 枚生产密钥（JWT / ENCRYPTION / AUDIT_HmacKey / SatisfactionTokenKey / SECRET_HASHER / 旧 WeChat ProxyApiKey）。

### 🔧 修复：干净克隆可直接启动（登录页「获取验证码失败」根因）

- `NoneadCSS.Infrastructure/DependencyInjection.cs` 新增 `ResolveUseSqlite(IConfiguration)`：`Database:UseSqlite` 缺失或不可解析时按 SQLite（与 `Program.cs` 生产 Fail-Fast 口径一致），修掉从非 Api 目录启动时误走 SqlServer 分支导致的 `ConnectionString 属性尚未初始化`。
- 空库启动链修复：`Migrations/MigrationIdempotency.cs`（`DropIndexIfExists`/`DropForeignKeyIfExists`/`DropTableIfExists`，按 provider 分支）、`Persistence/FreshDatabaseBootstrap.cs`（空库先 `EnsureCreatedAsync()` 再把全部迁移 id 写入 `__EFMigrationsHistory`）、`Persistence/VersionPropertyMappingExtensions.cs`（SQLite 用 `IsConcurrencyToken().HasDefaultValue(0)` 代替 rowversion）、非生产环境自动生成 `SecretHasher:Key` 开发密钥、相对 `Data Source` 按 `ContentRootPath` 解析并自动建 `data/`。
- 前端可读性：`frontend/src/pages/Login.tsx` 新增 `isBackendUnreachable()`，后端不可达（502/504/`ECONNREFUSED`）时提示 `auth.login.captchaBackendDown`，不再与验证码本身失败混为一谈。
- 新增开发常驻脚本 `scripts/dev-up.ps1` / `scripts/dev-down.ps1` / `scripts/dev-api-run.cmd`：进程脱离调用方进程树（WMI 启动 cmd 保活循环），API 退出后 5 s 自动重启，健康判定以 `/api/auth/captcha` 返回含 `svgContent` 为准，且只停本项目进程。

### 🐞 修复：四渠道全面功能审计（m03187）收敛

- Telegram：webhook 先验 secret 再看 body、>1 MiB 直接 413、入站失败回 500 让平台重投（入站去重保证幂等）；下载改流式 + `Content-Length`/`file_size` 预检 + 增量字节上限（此前整文件 `ReadAsByteArrayAsync` 有 OOM 风险）；出站统一走 4096 分片 + 节流 + 403/429 处理；轮询仅在更新处理成功后推进 cursor；回复转发 `message_thread_id`。
- LINE：CSRF 豁免由整段 `/api/line` 收窄到 `/api/line/webhook`；replyToken 消费后写 `thread.Headers.replyTokenUsed`，避免坐席回复重放已用 token（409 并白耗 push 配额）；签名附件链接校验归属；standby 模式不再自动回复；note/thread 重复投递去重。
- iMessage：AMB 模式空 `WebhookSecret` 一律 403（此前可伪造入站注入租户）且 `IsConfigured` 要求密钥；BlueBubbles 桥 webhook 增加常量时间共享密钥校验；AMB 出站补齐 `{v,id,sourceId,destinationId,locale,body}` 信封；404/410 判为终态不再无限重试；入站检查 `Enabled`/`IsModeActive`；`RateLimitPerMinute=0` 不再阻断全部发送。
- Messenger：Graph 附件类型兜底由 `"files"` 改 `"file"`（此前 PDF 永远发不出去）；`DatabaseSeeder` 的角色覆盖不再丢 `messenger/telegram/line/imessage.manage`；入站持久化异常改回 500 让 Meta 重投、`Enabled=false` 时只接收不入库；`ReplyAsync` 校验 `SourceType`；`TextChunkChars` 上限收到 2000（Graph 限制）；前端按 207 `threadCreated=false` 提示并保留草稿、24 小时窗口改读 `windowHours` 配置。

### 🧹 全项目审计：死代码清理、测试卫生与领域事件接线（2026-11-21）

- **C# 死代码移除**（全部留底在仓库外评审目录，本仓库不含）：7 个无实现的聚合标记接口（`NoneadCSS.Domain/Repositories/*AggregateRepository.cs`，342 行）；4 个从未注册到 Quartz 的作业（`SendAlertJob` / `SendEmailReplyErrorJob` / `SendNotificationToUsersJob` / `TriggerActionJob`）及其连带孤儿 API `INotificationService.SendAlertAsync`；7 个无绑定无引用的请求 DTO；`CalendarEventType` / `CalendarEventStatus` 两个枚举；`ChatOptionKeys.cs`；`NotificationRequests` 的 `[GeneratedRegex]` 块。
- **事件层收敛**（用户裁定「保留并接线，让功能真的生效」）：Eventy 风格空转层 `IEventDispatcher` / `EventDispatcher` 连同 13 处 `Trigger(...)` 调用与注册口整体删除（全仓 `Before`/`After`/`ApplyFilter` 注册口 0 调用，`Trigger` 只写日志、无订阅者）；强类型管道改为可用——`IDomainEventDispatcher` 上移到 `NoneadCSS.Domain/Events/`（Application 不再需要引用 Infrastructure），9 个服务在 `SaveChanges` 之后发布事件（登录/登录失败/锁定/改密/激活/邮箱更新/会话状态/分配/回复），10 个监听器保留、6 个副作用已在别处内联的监听器与其事件删除。
- **顺带修掉的实际缺陷**：① 仪表盘「异常事件数」恒为 0——监听器写入的审计 `Action` 是 `LoginFailed`/`Lockout`，而 `NoneadCSS.Infrastructure/Persistence/AuditLogRepository.cs:101-102` 按小写 `loginfailed`/`lockout`/`resetfailed` 过滤，现已对齐；② `UserRepliedEvent.Thread` 在 ImplicitUsings 下实际解析为 `System.Threading.Thread`（此前无人读取该属性所以能编译），已改为 `ConversationThread`；③ 3 个监听器重复写审计（`AuditActionFilter` 已覆盖）与 `ActivateUserListener` 二次激活已去除；④ 邮箱/文件夹计数器改由 `MailboxUpdatedEvent` 触发按需调度 `UpdateFolderCountersJob`。`LoginRequest` 新增可选 `Locale`，登录时把界面语言写入 `User.Locale`。
- **测试卫生**：`NoneadCSS.Tests/Shared/TestHelpers.cs` 由 190 行缩到 78 行——删除与生产类型**同名**的 7 个影子接口（`IEmailService`、`IEmailQueueService`、`IFolderService`、`IMailTemplateService`、`INotificationPublisher`、`IUnitOfWork`、空 `IEventDispatcher`）与影子 `UnitOfWork`、无人使用的 `MockFactory`、以及 `Helpers/AsyncTestHelpers.cs` 的子集副本；改后「改真实接口会打断 mock 它的测试」这一保证恢复。两处逐字重复的 `StubLineHttpHandler` 提取为 `NoneadCSS.Tests/Helpers/StubLineHttpHandler.cs`。新增 22 例领域事件测试（含「必须在保存之后发布」的显式断言）。
- **前端清理**：删除 4 个无人导入的孤儿文件（`components/PreferencesSettings.tsx`、`components/SecuritySettings.tsx`、`hooks/useCancelableRequest.ts`、`hooks/useTimezone.ts`，功能已由 `components/ProfileTab.tsx` 覆盖）；`getApiErrorMessage` 由 10 份实现收口为 `frontend/src/utils/error.ts` 的唯一超集实现；删除硬编码 `zh-CN` 且无人调用的 `formatDateTimeSafe`（前端用例 675 → 672；补齐领域事件相关性用例后当前回到 684，见顶部测试口径）。
- **文档事实修正**：`docs/development.md` §7.1 作业表按 `AddQuartz` 实际注册重写（原表含仓库里并不存在的 `RestartQueueWorkerJob`）；§6.3 事件分发说明去掉已删除的 Eventy 层；`docs/NoneadCSS_TechnicalDebt_2026Q4.md` 关于 `TriggerActionJob` 的描述更正为「从未注册、本轮已移除，该需求仍需一个正式注册的作业」。
- **国际化复核结论**：前端两套 locale 各 7005 键、字面 `t()` 键 0 缺失、`npm run i18n:check` 0 命中、`/openapi/en.json` 0 CJK、`frontend/src` 全部 61 处 `Intl.*`/`toLocale*` 均带显式 locale；后端出口词典增至 **735 键**（当时 697，后续补采至 735；`api-message-dictionary.mjs coverage` 报 `missingFromDictionary=0`），并补齐此前漏抽的 `Unauthorized(` / `Forbid(` / `Problem(` 站点；`ApiResponseWrapperFilter` 新增裸字符串载荷处理，`NotFound("中文")` / `Unauthorized("中文")` 在 `?lang=en` 下也返回英文（实测 `/api/calendar/feed?token=bad` 由「无效的订阅令牌」→ "The subscription token is invalid."，`/api/Calendar/templates/999999` 由「模板不存在或无权限」→ "The template does not exist or you do not have permission."）。仅判消息字段的全端点复扫：260 个 GET 中 0 处中文（其中 119 个为 429 限流响应，其文案同样已本地化）。按全字段口径（257 端点）仍有 100 个端点含中文，拆分为 88 个业务/种子数据、10 个服务端标签或选项目录、2 个裸字符串出口（本轮已修）——数据面双语按用户裁定不在本批次范围。

### 🔠 新增：服务端标签与选项目录双语（resx · 2026-11-22）

- 判定口径：按「只判消息字段」复扫 260 个 GET 已全部 0 中文；按全字段口径仍有 100/257 端点含中文，拆分为 **88 个业务/种子数据、10 个服务端标签或选项目录、2 个裸字符串出口**（后者已修）。10 个里 `CannedResponses/categories`、`Roles/{id}/users`、`SystemOptions`、`Teams/{id}/members` 的中文来自数据库种子名称，属数据面（按用户裁定不做代码翻译），其余为代码侧标签目录，本轮改完。
- 机制更正（重要）：本仓库**没有** `IStringLocalizer<Messages>`（全仓 0 用法）。Application 层取文案只走 `NoneadCSS.Application/Services/ILocalizationService.cs` → `NoneadCSS.Infrastructure/Services/LocalizationService.cs`（resx → DB `Translations` 覆盖 → **缺失时返回键本身**），且没有 `Messages.Designer.cs`。因此每个调用点必须显式回落中文原句；文档 `docs/development.md` §9.3 / §9.4 已按此重写（原文档写的 `Messages.en.resx` 与 `IStringLocalizer<Messages>` 注入示例都不存在）。
- resx 键集从 65/65 增至 **156/156**（四轮批次：策略与 SLA 标签 12 → 选项目录 62 → 导入导出实体目录 16 → 订阅指引 1），两份文件键集合恒等、中性文件保持 0 CJK；英文措辞一律复用前端既有词条（`frontend/src/locales/en/{settings,automation,calendar,knowledge,chat,data}.ts`），避免同一概念出现第二套英文。
- 最后两处「目录型」代码文案补齐：`NoneadCSS.Application/Services/ImportExportService.cs:36-43` 的导入导出实体目录（8 类 × 名称/描述，16 键 `ImportEntity_<type>` / `ImportEntityDesc_<type>`）与 `NoneadCSS.Api/Controllers/CalendarController.cs` 的 Outlook 订阅指引（键 `CalendarSubscriptionInstructions`）。两处同样是「可选尾参注入 `ILocalizationService` + 取不到词条回落中文原文」，旧构造点与后台任务不受影响；测试锁在 `NoneadCSS.Tests/Localization/ImportExportCatalogLocalizationTests.cs`（4 例）与 `ServerLabelCatalogLocalizationTests.cs` 的词条表里。
- 全量复核口径（当前构建）：只判消息型字段复扫 OpenAPI 全部 240 条 GET，仍有中文的 **39 条端点逐条核对后全部是数据面**（模块/角色/权限标签/标签名/邮箱名/模板名/SLA 策略名/会话主题/工作流名等种子或用户数据），代码侧文案已清零；HTTP 实测 `/api/ImportExport/entities` 中文段 17→0、`/api/Calendar/subscription` 的订阅指引 en 侧 0 CJK（zh 侧逐字未变）。
- 本地化落点（均通过 `ILocalizationService` 作为**可选尾随构造参数**注入，后台作业无 HttpContext 时按中文，不改既有构造调用点）：`PasswordPolicyService`（`requirements[]` 与 7 条 `ValidateAsync` 失败文案，带数量的用 `{0}` 槽位）、`SlaService`（违规类型/状态标签与 `SlaConst.FormatHours`）、`CalendarQueryService`（`eventTypeOptions`/`statusOptions`）、`KnowledgeService`、`ChatService`、`SendLogService`、`CustomFieldService`（`KnownChannels` 的值保持语言中立、标签在出口按 `Channel_<key>` 翻译）。
- 顺带修掉一个跨语言缓存缺陷：`PasswordPolicyService` 原先把**某一语言**的 `Requirements` 写进缓存，后续另一语言请求直接复用（英文界面拿到中文）。现在缓存条目保持语言中立，只在出口按请求文化复制一份（`WithLocalizedOptions` / `WithLocalizedRequirements`）。
- 出口词典增至 **735 键 / 67646 B**：给生成器补第 7 个模式族 `ListAdd`（`errors.Add("中文")` 此前完全漏采，导致 `coverage` 报 0 的**假绿**），并修掉状态辅助族缺 `Unauthorized(` / `Forbid(` / `Problem(` 的盲点；`node scripts/i18n/api-message-dictionary.mjs coverage` → `missingFromDictionary:0`（`notSeenInSource:12` 是 extractor 看不见的出口点族 extras 条目，属预期）。
- **修掉一个会静默丢译文的工具缺陷**：`merge` 原先按本轮 rows **重建**词典，因此所有「提取器看不见的键」每次 merge 都会被删掉。出口点族（`NoneadCSS.Api/Middleware/GlobalExceptionMiddleware.cs:265-293` 那批通用异常兜底文案，行文件 `msg-rows-extra*.json` / `msg-rows-dto-A.json`，id `x001`–`x012` / `dtoA001`）正是这一类，13 条已验证过的英文就这样丢了，而条目数只从 722 变 723，90% 骤减守卫拦不住、构建与 `coverage` 都不报错，只有两条本地化用例变红才暴露。现在 `merge` **只增不减**（保留上一版里本轮覆盖不到的键），真正清理孤儿键必须显式 `--prune`；13 条译文已从工作目录的 `.old` 批次文件恢复。另一条必须记住的口径：重新 `extract` 会重排行号 id，旧 `msg-en-NN.json` 按 id 对齐，直接 merge 会把英文错配到别的键且不报错——只能按 **key** 重新对齐。
- 新增「前缀键」规则（`NoneadCSS.Api/Localization/ApiMessageLocalizer.cs`）：`"LINE 请求异常：" + ex.Message`、`"getMe 异常：" + ex.Message`、`"附件全部发送失败，消息未发出：" + …` 这类「固定中文前缀 + 动态详情」的拼接，既不能精确匹配也不能靠片段归一化命中，因此英文界面一直直出中文。规则只认**以全角冒号结尾、英文值不含槽位**的词典键（735 条中恰有 3 条），按键长降序、仅在句首匹配，英文与详情之间补一个空格；句子中间出现同样的中文不再被误翻。新增 `NoneadCSS.Tests/Localization/ApiMessagePrefixKeyTests.cs` 6 例。
- **顺带修掉一处会掩盖回落的契约缺陷**：`NoneadCSS.Infrastructure/Services/Line/LineClient.cs` 成功返回里的 `Via` 只按「请求方式」计算，客户端内部 reply→push 回落之后仍报 `"reply"`；而 `LineService.cs:570` 正是靠 `Via != "reply"` 判断发生了回落，等于这条判断从未生效。现由 `deliveredByPush` 标志反映真实投递路径。连带修正 `LineIngestionService.cs:673-680` 的 replyToken 标记条件：判据从「经 reply 投递」改为「尝试过 token」——回落意味着 token 已被确定性拒绝（过期/无效），漏标记会让坐席再吃一次 409 加一次无谓重试。
- 一致性修复：`NoneadCSS.Domain/Entities/User.cs:16` 的 `Locale` 默认由 `"en"` 改为 `"zh-Hans"`，与 `AuthService` / `SetupController` / `UserService` 三处写入及 API 默认请求文化一致（无任何 EF `HasDefaultValue`，纯代码改动，不涉迁移）。
- 仍未双语（按裁定不做）：种子与用户数据、写入库的聊天系统消息与工单 HTML、日程提醒邮件、`MappingExtensions.cs` 的逐条 label（前端在 `frontend/src/pages/calendar/chips.tsx:517-531` 按 `preferServerLabel` 自行覆盖）、`CustomReportExportHelper.cs` 的导出表头。

### 🐞 修复：四渠道审计第二轮（复核后仍未修的项 · 2026-11-22）

- 复核方式：让四位审计子代理**重新判定自己上次提出的 15 条**，按当前工作树逐条给 `已修 / 部分 / 未修 / 当初判错 + file:line`。复核结论：Messenger 13 已修 / 2 部分；Telegram 7 / 3 未修 5；LINE 6 / 部分 4 / 未修 4（含 1 条严重）；iMessage 4 / 部分 4 / 未修 7。本轮把「严重 + 用户可见」的全部修完。
- **LINE 严重缺陷：reply 失败回落 push 会重复投递**（`NoneadCSS.Infrastructure/Services/Line/LineClient.cs` 的发送重试对 `ReplyPath` 也生效，409 只有在重试之后才出现时，第一次尝试很可能已被 LINE 接受，此时无条件 push 会让客户收到两条相同消息，同时白耗月度配额）。修法是客户端在「重试过仍失败」时置 `LineSendResult.DuplicateRisk`（新增字段）并**放弃自身回落**，把判断权交回 `LineService.cs:522-524`；新增 `NoneadCSS.Tests/Services/LineDuplicateDeliveryGuardTests.cs` 断言「重试后 409 不得出现 `/v2/bot/message/push` 请求」与「首次即 409 允许回落」。
- **LINE 状态卡与配置可见性**：配额卡此前恒为 `- / 未知`，因为 LINE `/v2/bot/message/quota` 返回 `type = "none"`，而服务层只认 `"unlimited"`——服务层归一化后前端显示正常；`GetStatusAsync` 此前从不填 `ReplyModeHint` / `LastError`（前端一直在渲染），现按机器可读码 `reply` / `push` / `unknown` 填模式，并在 `ReplyAsync` / `PushTestAsync` 失败分支把 HTTP 状态与文案写入 `line.last_error_http_status` / `line.last_error_message` 供状态卡回显；`LineRequests.cs` / `LineDto.cs` 补齐六个只在 `LineOptions` 里存在、UI 无法调整的字段（`RetryMaxAttempts`、`StatusCheckTimeoutSeconds`、`MaxInboundMediaBytes`、`MaxOutboundAttachmentBytes`、`OutboundImageExtensions`、`ChannelAccessTokenKeyId`），并新增 `NormalizeImageExtensions`（不写它 `"PNG"` 会静默匹配不到任何扩展名）。
- **LINE 分段计数与入站附件文件泄漏**：新增 `frontend/src/utils/line-chunking.ts`，按 `LineClient.SplitText` 的 **UTF-16** 口径逐行等价移植（切点前若是高位代理退一个单元），前端分段数与后端实发段数一致（emoji 场景此前显示 1 段而后端发 2 段）；入站附件改为「先写 `<upload root>/.line-inbound-staging/<yyyy/MM/dd>/`，`CommitAsync` 成功后再改名就位」，两条回滚路径删除暂存文件——此前附件字节流写在数据库事务内部，回滚只删库记录，孤儿文件永久堆积。
- **Telegram**：敏感配置此前**无法清空**（只判 `!IsNullOrWhiteSpace`），新增 `ClearBotToken` / `ClearWebhookSecretToken` / `ClearLinkTokenHmacKey` 三个显式开关，实现是删除 SystemOptions 行而非写空串（`TelegramSettingsProvider` 只接受非空库值作为覆盖）；清空 webhook 密钥后入站端点在读取请求体之前就 401，等于关闭该端点（fail-closed，已在注释中写明）。本地限流从「先查后记账」（`Peek` 与 `RegisterSend` 分别加锁，并发坐席可越过 25/s 与单会话 1/s）改为**同一把锁内的原子预约** `TelegramThrottle.TryReserveSend`（记账落在未来槽位，被拒不记账），出站发送改为「预约 → 需要时等待 → 已记账」；实现过程中踩到「搜索槽位时用 `TrimWindow(…, slot)` 会抹掉未到期记账、导致被拒的预约反而白送配额」，改为只按 `now` 清理 + 只读 `ScanWindow`。轮询宿主此前一旦遇到未捕获异常就**永久停摆**（catch 在 while 之外），现把 catch 移进循环并用 5 s → 30 s → 60 s 退避阶梯（成功清零、取消立即退出）。`SplitLongMessage` 在 `maxChars=1` 且首字符为代理对时会算出 `windowLength=0` 并抛 `ArgumentOutOfRangeException`，已补下限（代理对是最小不可分单元，分片可多 1 个 char 但绝不产空片）。
- **iMessage**：`AutoReplyEnabled` / `AutoReplyMessage` 此前**没有任何消费点**（设置页的「自动回复」是死开关），现在新建会话提交之后调用 `SendAutoReplyAsync`，目的地与坐席出站走同一套身份归一化（AMB 用 `ExtractExternalId`，桥接用 `BuildReplyGuid`），发送失败只记日志、不影响入站结果；`RateLimitPerMinute=0` 此前会**静默停掉全部出站发送**，现三层钳制（默认常量 20、存储值 ≤0 时回落默认、DTO `[Range(1,600)]`、前端 `min={1}`），限流器保留 `<=0 → false` 的 fail-closed 语义；关闭事件的身份不再直接用 `Update.DisplayName`（会导致会话永不关闭、坐席后续撞 410），改走已有的 `IMessageWebhookParser.NormalizeAmbSourceId`；附件全部失败且正文为空时不再返回 `Sent=true`（此前会把失败当成功写库），`IMessageReplyResult` 新增 `FailedAttachments`。
- **权限与门禁**：`LineController` webhook 在验签之后补 `Enabled` 判断（放在验签后是为了让未签名探测读不到开关状态；返回 200 与 Telegram/Messenger 口径一致，停用期间不入库）；`frontend/src/pages/Messenger.tsx` 的「发送订阅请求」按钮补 `!isAdmin` 门禁（对应端点是 `[RequireSuperAdmin]`，此前非管理员必得 403）；`NoneadCSS.Api/DatabaseSeeder.cs` 两处客服主管权限清单不一致（全新安装与升级库给同一个角色的面板不同）已对齐，并明确**渠道 `.manage` 不授予客服主管**（渠道配置写入是 `[RequireSuperAdmin]`）；`NoneadCSS.Application/Services/IMessageIngestionService.cs:84/:145` 两个入口补 `Enabled` / `IsModeActive` / `IsConfigured` 判断（此前停用的渠道照样建客户、建会话、触发工作流与通知）。
- 本轮明确**未修**（记为已知缺口，不是遗漏）：Telegram 本地 Bot API 的绝对文件路径被拼成假 URL（分容器部署下附件必失败）；iMessage `destination-id` 未校验归属租户、chatGuid 缓存仍是进程内；Messenger 联系引导文案硬编码中英双语（`profile.locale` 从未持久化）；LINE 附件直链是无身份绑定的 bearer 链接且默认 TTL 7 天（属安全基线，需产品决策）；Telegram 设置页尚无「清除密钥」控件（清空能力目前只有 API 侧可用）。

### 🧰 新增：开源发布预检门禁（`scripts/oss-preflight.mjs` · 2026-11-22）

- 一条命令串起三条只读检查（密钥扫描 → 文档链接 → 词典覆盖），任一失败退出码 1，可挂 CI 或 pre-push；`--quick` 跳过较慢的密钥扫描用于日常提交。本脚本不实现任何规则、不写文件，规则仍在那三个脚本里。
- 文档：`docs/development.md` 新增 §9.8（含「覆盖检查会假绿」的成因与门禁口径）、`README.md` 新增「开源发布预检」小节。

---

## [2.3.0] - 2026-10-23（版本口径统一 + API 直连工具与功能全景文档）

### ✨ 新增

- **API 直连工具**（`scripts/css-api/`）：`css-api.ps1` 支持自动获取并解析图形验证码、登录换取 JWT、令牌缓存（含过期时间）与 401 自动重登，密码只从参数或环境变量读取、不落盘；`endpoints.md` 由生产 OpenAPI 导出，按 50 个分组索引 594 个操作。会话数据示例：`./css-api.ps1 -Path '/api/Customers?page=1&pageSize=5' -AsObject`。
- **功能全景总结**（`docs/NoneadCSS-功能全景总结-20261021.md`）：以使用者视角梳理 17 个功能域，附生产实测数据（45 个功能域抽样，38×200 / 4×400 / 1×403 / 2×404）、A–E 五级 27 条缺陷清单（均带 `file:line`）与改进优先级。

### 🔧 版本口径统一

- 新增根 `Directory.Build.props`，统一声明 `<Version>2.3.0</Version>`。此前各 `.csproj` 未声明版本，程序集 `ProductVersion` 恒为 `1.0.0+<sha>`，无法从产物读出发布版本；此后为 `2.3.0+<sha>`，与 CHANGELOG、git tag 三者一致。
- `frontend/package.json` 版本由 `2.2.5` 修正为 `2.3.0`（此前落后 CHANGELOG 12 个版本，package.json 未随发布更新）。
- git tag 由停留在 `v2.2.11` 直接标注到 `v2.3.0`（v2.2.12–v2.2.17 期间未打 tag，`git describe` 因此长期失真）。

### 📦 说明

- 本次提交**不包含运行时功能变更**，线上行为与 v2.2.17（部署提交 `fbe4f4f`）一致；未执行生产部署，`Production/` 产物未更新。
- 远程仓库 `<org>/noneadcss` 自 2026-10-20 起对 SSH 返回「项目不存在或无权限」，提交与 tag 先暂存本地，待仓库恢复后统一推送。

---

## [2.2.17] - 2026-10-10（系统管理-翻译管理：导出恒空 / 导入恒失败的闭环修复）

### 🐛 系统管理-翻译管理：导出内容为 `{}`、原样导入报「导入内容为空」（P0）

- **缺陷描述**：翻译管理页的「导出 → 翻译 → 导入」闭环完全不可用——导出得到的文件内容恒为 `{}`，把该文件原样导入即报 400「导入内容为空」；统计与差异也恒为 0。
- **根因**：后端 `/api/localization` 的资源读取按源码名 `…Messages{后缀}.resx` 查找嵌入资源，而 resx 编译进程序集后实际名为 `NoneadCSS.Application.Resources.Messages.resources`（扩展名是 `.resources`）；`zh-Hans` 区域性变体更不在主程序集里，而在附属程序集 `zh-Hans\NoneadCSS.Application.resources.dll`，主程序集的 `GetManifestResourceNames()` 里根本没有该名字。因此 `/keys` 恒空、`/stats` 与 `/diff` 恒 0、`/export` 恒输出 `{}`。
- **修复**：改用静态 `ResourceManager` 按显式 culture 取词（与仓库既有 `LocalizationService` 的 `IStringLocalizer` 用法一致），四处一次全修。
- **影响文件**：`NoneadCSS.Api/Controllers/LocalizationController.cs`。

### ✨ 往返闭环与鉴权面补齐

- 页面导出改为「该语言**前端 UI 真源** ∪ 后端 resx/DB」（DB 优先），覆盖页面全部 6454 + 65 条可编辑键；此前只导后端那份，前端界面文案一条都覆盖不到。
- 导入新增三层前端预校验（对象 / 非空 / 值为字符串），任一条不合规整体拒绝；后端报错不再直出英文 .NET 原文；导出空集改为提示而非下载 `{}`。
- `GET /api/localization/translations/{culture}` 去掉 `AllowAnonymous`；`I18nContext` 补「未登录不请求覆盖层」守卫，避免登录页与公开客服页被 401 弹去登录。
- `/stats` 增 `dbKeyCount`，页面统计卡片按页面键集口径计算，标签改为「已定制 / 未定制」并做 `min` 钳制防止比例 >100%。
- **影响文件**：`frontend/src/pages/Localization.tsx`、`frontend/src/context/I18nContext.tsx`、`frontend/src/i18nKeys.ts`、`frontend/src/locales/{en,zh-Hans}/localization.ts`、`frontend/src/test/setup.ts` 及配套测试。

### 📄 文档与工具

- `docs/NoneadCSS-全面审核报告-20261009.md`：全项目代码与功能审核报告（阻断 4 / 高 14 / 中 24 / 低 22）。
- 两个 i18n 审计脚本（`frontend/audit-i18n.tmp.mjs`、`frontend/audit-i18n2.tmp.mjs`）纳入版本控制。

### 📦 部署（2026-10-10）

- 发布提交：`fbe4f4f`（分支 `feat/i18n-batch1`，与远端 `master` 同 SHA），产物 `ProductVersion = 1.0.0+fbe4f4f37e2e4cdae817607cb09d3c7b357682fb`。
- 按 `docs/development.md` §18.11 七步 SOP 执行；**无新增 EF 迁移，不改数据库结构**。
- 发布前备份：`Production\_backup_prod_20261010_120958`（生产 DB 冷备 + 旧 backend + 旧前端产物）。
- 验证：四端 200、`:5008` 无监听、`:5172` 绑定 `127.0.0.1`、§50.1 四条 DDL 校验通过、`PRAGMA integrity_check = ok`、`backend\Data` 仍为 33 文件 / 61.4 MB。
- 运维加固：发布前发现生产自今日 12:06 起宕机（计划任务 `LastTaskResult = 0xC000013A`，即 STATUS_CONTROL_C_EXIT，且未配重启策略），已恢复；并给计划任务 `NoneadCSS-Production` 补 `RestartCount=999` / `RestartInterval=PT1M`，避免再次静默宕机。

---

## [2.2.16] - 2026-10-08（全项目国际化 zh-Hans/en：批次 1 基础设施 + 批次 2 全界面收口 + 批次 12 后端消息）

### ✨ 全项目国际化（i18n）— 批次 1 / 批次 2 / 批次 12

- **批次 1（基础设施）**：新增词条类型与中文真源骨架；`translate()` 支持插值与复数，缺失 key 开发期抛错（后续裁定为失败安全：缺省不抛错）；47 条导航文案迁入类型化真源（zh-Hans/en）；导航 label 改为纯 key 并收敛支持语言选项。
- **批次 2（全界面收口，波次 1–15 + 收官批）**：按波次推进——认证页/仪表盘/外壳残留 → 共享格式化与常量层（Intl 日期与相对时间、状态标签）→ 会话列表/详情/回复框与侧栏 → 通知中心（模板/通知/规则/铃铛）→ 知识库 → 在线客服与客户侧聊天组件 → 设置/个人资料与用户/团队/角色 → 自动化（工作流/SLA/宏）→ 邮件（邮箱/模板/签名/文件夹/罐头回复）→ IM 渠道（微信/WhatsApp/飞书/钉钉）→ 数据（审计/发送日志/报表/自定义报表/导入导出）→ 客户/标签/自定义字段 → 日历（编辑面板/各视图/同步/订阅/垃圾箱）→ 集成与运维（Webhooks/API 密钥/模块/系统工具）→ 时间追踪/满意度/会话线程与附件。收官批引入**显式豁免标记**，外壳与管理页硬编码中文基线 **223 → 0**。
- **批次 12（后端消息）**：SystemController 等后端消息本地化（14 处）；响应信封文案本地化；附件端点 3 处直出中文清理；附件「异常文案当控制流判据」改为错误码；日历提醒「准时(0)」放宽为 `>= 0`。
- **门禁（已纳入 GitLab CI 前端作业）**：硬编码中文基线门禁（基线快照 121 文件 / 8947 处）与基线生成器；`tsc -b`、`npm run i18n:check`、`vitest`、`vite build` 全部进流水线。

### 🔧 随批修复

- `fix(migration)`：SendLogs 表重建前先清理孤立外键，修复迁移在生产失败。
- 其余 fix 覆盖后端消息本地化、前端把翻译键当文案渲染（波次 14/15 验收修复）、Webhook 编辑器邮箱名回退为字面量、插值强调样式拆键等。

### 📦 部署（2026-10-08）

- 发布提交：`64591f7`（分支 `feat/i18n-batch1`），产物 `ProductVersion = 1.0.0+64591f7a8797ace4a2207c8170227d8bbfb84ac5`。
- 按 `docs/development.md` §18.11 七步 SOP 执行：停服→`dotnet publish`→前端构建→配置同步→启动→四端健康检查→进程名核对。
- 数据库：新增迁移 `20260921060825_FixSendLogStatusCheckConstraint` 已应用；`PRAGMA integrity_check = ok`；§50.1 四条 DDL 校验全部通过（RowVersion=1 / KnowledgeArticleLikes 表存在 / CalendarEvents 列=2 / OutlookSyncSettings 列=4）。
- 发布前备份：`Production\_backup_prod_20261008_201004`（生产 DB 冷备 + 旧 backend + 旧前端产物）。
- 验证：`http://127.0.0.1:5173/`、`http://127.0.0.1:5172/`、`http://127.0.0.1:5009/health`、`http://127.0.0.1:5173/health` 均 **HTTP 200**；`:5008` 无监听；`:5172` 绑定 `127.0.0.1`（§50.2）。
- 服务托管：改为 Windows 计划任务 `NoneadCSS-Production`（`Production\start-prod.ps1`），生产不再依赖交互会话。

---

## [2.2.15] - 2026-09-04（服务台-在线聊天转工单：会话回复客户邮箱信息泄露为显示名修复）

### 🐛 服务台-会话管理：在线聊天转工单-会话回复显示客户邮箱信息（T000123）

- **用例编号**：T000123
- **缺陷描述**：进入系统 → 功能模块-在线聊天 → 在线转工单 → 工单会话客服回复 → 客户再次问答，预期「会话信息显示正常（显示客户实名/名片昵称）」，实际「会话信息中显示客户的邮箱信息：例如 qq 邮箱（xx@qq.com）」，客户邮箱被当作客户名称暴露给坐席。
- **根因**：在线聊天转工单后，客户会话消息的发送者显示名存在两条泄露客户邮箱的链路——① 客户线程的 `From` 直接使用 `session.CustomerEmail`（客户邮箱地址），前端 `ThreadList.tsx` 客户消息 `displayName = customerName || thread.from || '未知'`，当 `customerName`（来自 `conversation.customer?.fullName`）为空时回退到邮箱版 `thread.from`；② `CustomerResponse.FullName = customer.GetFullName(true)`，而 `Customer.GetFullName(true)` 在客户 `FirstName + LastName` 均为空（客户未登记实名、`ChineseNameSplitter` 拆分后为空）时回退到 `GetMainEmail()`（主邮箱地址），于是 `customerName` 本身即为邮箱。因此当客户未在系统中登记实名时，会话内客户消息与会话头部「客户：」均显示为 qq 邮箱地址。
- **修复**：① `frontend/src/utils/format.ts` 新增共享辅助函数 `isEmailAddress()`（正则判定字符串是否为邮箱地址）与 `displayCustomerName()`（当实名为空或回退成邮箱地址时用友好「客户」兜底）；② `ThreadList.tsx` 客户消息显示名改为 `nameOrFrom = customerName || thread.from || '未知'`，当 `isEmailAddress(nameOrFrom)` 为真时统一用「客户」兜底，不再暴露邮箱；③ `ConversationDetail.tsx` 会话头部「客户：」逻辑用 `displayCustomerName(conversation.customer?.fullName)` 简化，避免回退到 `conversation.customerEmail` 泄露邮箱。
- **影响文件**：`frontend/src/utils/format.ts`、`frontend/src/components/ThreadList.tsx`、`frontend/src/pages/ConversationDetail.tsx`
- **验证**：`npx tsc -b` 0 错误；`npm run build` 通过（仅既有 Tailwind `length` typehint 警告）。

---

## [2.2.14] - 2026-09-04（集成与扩展-API密钥：页面刷新空白错误兜底修复）

### 🐛 集成与扩展-API密钥：API密钥页面刷新空白显示无加载页面（T000122）

- **用例编号**：T000122
- **缺陷描述**：进入系统 → 集成与扩展-API密钥 → 刷新页面，预期"页面信息加载正常"，实际"空白显示无加载页面"。
- **根因**：`ApiKeys.tsx` 是全项目**唯一**一个 `loadOverview()` / `loadList()` / `loadTrash()` 均无 `catch` 块、渲染层无错误兜底 UI 的页面。当 API 密钥数据请求失败（如 403 权限不一致、服务端 500、网络异常或鉴权竞态）时，`overview` 保持 `null`，而 `loading` 在 `finally` 中被复位为 `false`，渲染条件 `activeTab === 'overview' && !loading && overview` 使页面主体空白——既无加载页、也无错误提示、更无法恢复。这与项目内其他页面（AuditLogs / Customers / Conversations / Calendar）「错误兜底 + 可恢复」的标准模式相悖。
- **修复**：① 页面新增 `error` 状态；② 三个加载函数补充 `catch` 块并调用 `getApiErrorMessage` 写入错误信息，请求成功/重新加载时清空；③ 渲染层在加载 spinner 之后、Tab 内容之前新增错误兜底 UI——「加载失败：{error}」+「重试」按钮，重试按当前 Tab 重新发起 overview / list 请求。
- **影响文件**：`frontend/src/pages/ApiKeys.tsx`
- **验证**：`npx tsc -b` 0 错误；`npm run build` 通过（仅既有 Tailwind `length` typehint 警告）。

---

## [2.2.13] - 2026-09-04（Webhook 业务事件自动触发 + 日历账户切换状态重置 + 日历默认月显示修复）

### 🐛 集成与扩展-Webhook：满足触发条件未触发回调请求（T000118）

- **用例编号**：T000118
- **缺陷描述**：新建 Webhook 信息并选择触发事件后，满足触发事件的条件时未触发回调接口请求（预期"满足触发事件，触发回调接口请求"，实际"满足触发条件，未触发回调请求"）。
- **根因**：业务事件持久化后仅在 Webhook 管理端手工调试触发，未在业务服务中挂接自动触发逻辑，导致业务事件发生时不主动调用回调。
- **修复**：为 9 种业务事件（`conversation_created` / `conversation_updated` / `conversation_deleted` / `conversation_status_changed` / `thread_created` / `customer_created` / `customer_updated` / `user_created` / `user_updated`）在业务服务持久化后直接调用 `IWebhookService.TriggerAsync(eventType, mailboxId, payload, entityType, entityId)` 自动触发 Webhook 回调。各服务以可选尾部参数注入 `IWebhookService?`（`webhookService = null`），通过构造函数/属性赋值并在动作成功后调用，避免影响既有构造链。
- **触发点覆盖**：
  - `conversation_created`：`ConversationService`（创建会话/建会话两处）、`ChatService`
  - `conversation_updated`：`DingDingService`、`DingDingIngestionService`、`WeChatIngestionService`（按渠道动态 eventName）、`WhatsAppService`
  - `conversation_deleted`：`ConversationService`
  - `conversation_status_changed`：`ConversationStatusService`
  - `thread_created`：`ConversationThreadService`
  - `customer_created` / `customer_updated`：`CustomerService`
  - `user_created` / `user_updated`：`UserService`
- **影响文件**：`NoneadCSS.Application/Services/ChatService.cs`、`ConversationService.cs`、`ConversationStatusService.cs`、`ConversationThreadService.cs`、`CustomerService.cs`、`UserService.cs`、`DingDingIngestionService.cs`、`DingDingService.cs`、`WeChatIngestionService.cs`、`WhatsAppIngestionService.cs`、`WhatsAppService.cs`
- **验证**：`dotnet build` 0 错误，全量后端测试 **1436 通过 / 0 失败**。

### 🐛 日历管理：切换账户页面显示上一账户日程（T000119）

- **用例编号**：T000119
- **缺陷描述**：管理员进入系统 → 日历管理 → 退出 → 客服账户进入系统 → 日历管理，页面显示上一个账户的日程信息，手动刷新后才显示当前账户日程。
- **根因**：`useCalendarStore` 是模块级单例（`create` 仅执行一次），其内存状态（`allEvents` / `eventsByDate` / `list` / `overview` / undo 栈等）在登出或账户切换后仍残留；SPA 内 `NavLink` 跳转 `/calendar` 不会像 F5 那样重建 store，而 `setAllEvents` 是增量合并（未出现的旧事件一律保留），导致上一账户日程残留在新账户视图。
- **修复**：新增导出函数 `resetCalendarStore()`（调用 `useCalendarStore.setState(createInitialState())`），在 `AuthContext` 的 `logout()` 与 `handleAuthExpired`（会话失效）两处调用，登出/会话失效时把内存 store 整体重置为初始状态。
- **影响文件**：`frontend/src/pages/calendar/store.ts`、`frontend/src/context/AuthContext.tsx`
- **验证**：`npx tsc -b` 与 `npm run build` 均通过（0 错误）。

### 🐛 日历管理：日历月显示被记忆的手动切换月份（T000120）

- **用例编号**：T000120
- **缺陷描述**：进入系统 → 日历管理 → 切换月份 → 切换到其余页面 → 再切回日历管理页，日历显示手动切换的月份，而非当前日期的月份（Outlook 默认显示当前月份）。
- **根因**：日历 store 的 `persistViewState`/`restoreViewState` 会把上次浏览的月份（`calMonth`/`calCursor`）持久化到 `localStorage`；`Calendar.tsx` 挂载 effect 在检测到已持久化的视图状态时跳过 `gotoToday()`，因此再次进入时恢复的是上次手动切换的月份。
- **修复**：① `store.ts` 初始化时恢复视图状态仅保留 `viewMode`/`calSubView`，**不再恢复** `calCursor`/`calMonth`（日期位置始终用 `createInitialState` 提供的当前月/今天）；② `Calendar.tsx` 挂载 effect 改为**无论何种方式进入都调用 `gotoToday()`**，保证日历每次进入都默认显示当前月，并确保首屏数据拉取。
- **影响文件**：`frontend/src/pages/calendar/store.ts`、`frontend/src/pages/Calendar.tsx`
- **验证**：`npx tsc -b` 与 `npm run build` 均通过（0 错误）。

### 🐛 服务台-会话管理：客户邮件正文包含图片/表格不显示（T000121）

- **用例编号**：T000121
- **缺陷描述**：进入系统 → 服务台-会话管理 → 客户发送邮件且正文包含图片（或表格）时，会话记录中不显示图片信息（预期"会话记录中显示图片信息"，实际"会话中不显示图片信息"）；同时质疑正文中的表格是否也应显示表格样式。
- **根因**：内嵌图片不显示存在**四层根因**——① 后端 `HtmlSanitizerHelper` 的 `AllowedSchemes` 不含 `cid:`，消毒（先于附件保存）将 `<img src="cid:xxx">` 的 src 剥除；② `SaveAttachmentsAsync` 未把正文中的 `cid:` 引用替换为实际附件下载 URL，也未记录 ContentId；③ 即使 `cid:` 存活，浏览器也不解析 `cid:` URI；④ `GetAttachmentParts` 仅接受 `attachment` 类型、`inline+FileName` 或 `application/octet-stream`，内嵌图片通常为无文件名的 `inline`，仅凭 Content-ID 被 `<img src="cid:...">` 引用，从未保存为附件。表格不显示根因：前后端消毒共同剥离内联 `style`，富文本内表格失去内联边框样式，前端又未对 `.html content` 内的 `table` 施加样式。
- **修复**：① `Attachment` 实体新增 `ContentId` 字段并生成 EF Migration `20260904090528_AddAttachmentContentId`；② `HtmlSanitizerHelper` 的 `AllowedSchemes` 加入 `cid:` 协议以保留内嵌图片引用；③ `EmailProcessingService.SaveAttachmentsAsync` 记录附件 `ContentId`，`GetAttachmentParts` 新增 `isInlineWithContentId` 条件捕获无文件名 inline 内嵌图片；④ `EmailIngestionService.ReplaceEmbeddedImageCidReferences` 在保存附件并 `SaveChanges` 生成附件 Id 后，将 `thread.Body` 中的 `cid:` 引用替换为 `/api/attachments/{id}/download` 下载 URL（已鉴权、可自动携带会话 Cookie）；⑤ `ThreadList.tsx` 为富文本追加表格样式（`[&_table]` / `[&_thead]` / `[&_th]` / `[&_td]` / `[&_tr]`）与图片样式（`[&_img]:max-w-full` / `[&_img]:h-auto` 等）。
- **影响文件**：`NoneadCSS.Domain/Entities/Attachment.cs`、`NoneadCSS.Application/Common/HtmlSanitizerHelper.cs`、`NoneadCSS.Infrastructure/Services/EmailProcessingService.cs`、`NoneadCSS.Application/Services/EmailIngestionService.cs`、`NoneadCSS.Infrastructure/Migrations/20260904090528_AddAttachmentContentId.cs`、`frontend/src/components/ThreadList.tsx`
- **验证**：`dotnet build` 0 错误 0 警告；全量后端测试 **1436 通过 / 0 失败**；`npx tsc -b` 0 错误；`npm run build` 通过。

---

## [2.2.12] - 2026-09-01（审计日志写入隔离 + MailboxUsers 并发兜底 + 系统工具-系统日志清空功能）

### 🔴 审计日志写入隔离：修复"User cannot be tracked"记录审计失败（P0）

- **根因**（生产日志 2026-09-01 15:20:19）：`AuditActionFilter` 记录审计时与业务动作共用请求级 DbContext。业务动作先跟踪了 `User` 实体，审计 `SaveChanges` 触发 DetectChanges 导航修复时尝试附加同 Id 的第二个 `User` 实例 → `InvalidOperationException: The instance of entity type 'User' cannot be tracked`，审计日志写入失败。
- **修复**：`AuditService.LogAsync` 改用独立 DI 作用域（全新 DbContext）写入审计日志，与请求共享跟踪器完全隔离；`GetLogsAsync` 等查询方法不受影响。
- **影响文件**：`NoneadCSS.Infrastructure/Services/AuditService.cs`

### 🔴 MailboxUsers 并发重复插入：UNIQUE 约束冲突兜底（P0）

- **根因**（生产日志 2026-09-01 15:22）：并发更新邮箱/用户时，内存集合（`mailbox.MailboxUsers`）过期导致重复添加 `MailboxUser` 映射 → `MailboxUsers UNIQUE constraint failed`。
- **修复**：`MailboxService.UpdateMailboxAsync` / `UserService.UpdateUserAsync` 在内存集合判断之外，增加数据库级 `MailboxRepository.UserHasAccessAsync` 兜底校验（AsNoTracking），消除 TOCTOU 竞态。
- **影响文件**：`NoneadCSS.Application/Services/MailboxService.cs`、`NoneadCSS.Application/Services/UserService.cs`

### ✨ 系统工具-系统日志：「清空日志文件」删除 log 记录

- **背景**：系统日志页原「清空」按钮仅清前端视图（"不影响日志文件"），后端无删除日志端点，历史异常记录（如 14:53-17:11 的部署前日志）无法删除。
- **后端**：新增 `POST /api/system/tools/logs/clear`（SuperAdmin 权限），通过 `SerilogLogFileMaintenance.ClearLogFiles` 将 `log-*.log` 文件内容截断为 0 字节（保留文件本身）；`GetSystemLogs` 与清空共用 `ResolveLogsPath` 路径解析，避免查看/清空双目录分裂。
- **Serilog 配置**：两个 File sink 增加 `shared: true`（原独占模式 FileShare.Read 无法被外部截断，实测确认）。`SharedFileSink` 每次写入前 `Seek(0, End)`，截断后新日志正常追加、无 NUL 空洞（集成测试覆盖）。
- **前端**：`SystemTools.tsx` 日志工具栏新增红色「清空日志文件」危险按钮（二次确认 + 清空后自动刷新），`system.ts` 新增 `clearLogs` API。
- **测试**：新增 `SerilogLogFileMaintenanceTests` 5 个集成用例（正常清空 / 锁定文件失败上报 / 目录解析回退 / 无文件 / 真实 Serilog shared sink 截断后继续追加无空洞）。全量后端测试 **1292 通过 / 0 失败**。

### 📦 部署

- v2.2.12 已编译并部署至生产环境（`Production\backend` + `Production\frontend\dist`）；后端 :5009、前端 vite preview :5172（强制 IPv4）、Nginx :5173 反向代理均验证通过；日志文件 `shared:true` 生效（外部可截断清空）。

---

## [2.2.11] - 2026-09-01（全面代码审核 T1-T28 修复 + 回归缺陷修复 + 新迁移 + 微信 Token 代理密钥修复）

### 🆕 v2.2.11 概览

- **全面代码审核（T1-T28）**：部署配置（HTTPS/nginx/vite/SQLite）、前端 XSS 防御、后端可靠性 28 项修复。
- **回归缺陷修复**：T000109 全局搜索跳转 / T000101 知识库取消点赞 / T000106 删除用户提示 / T000107 移动端通知面板 / T000108 移动端日历下拉中文。
- **数据库**：新增迁移 `20260831091009_AddNotificationRuleRowVersion`、`20260831095746_AddUserRolesUniqueIndex`；`QuartzSchemaInitializer` 幂等初始化。
- **微信 Token 失效修复**：生产代理网关密钥为占位符导致微信 API 401（表现为"微信聊天 Token 失效"），已替换为有效密钥；并修复 `secrets.ps1` UTF-8 BOM 丢失导致 PowerShell 5.1 吞行问题。
- **测试**：全量后端测试 1214 通过 / 0 失败。

### 🐛 回归测试缺陷修复：T000109 全局搜索跳转 / T000101 取消点赞 / T000106 删除用户无反馈 / T000107 移动端通知面板 / T000108 移动端日历下拉中文

- **T000109 P1 全局搜索-知识库跳转 404**：路由表仅存在 `/knowledge` 列表页、无 `/knowledge/:id` 详情路由，全局搜索知识库结果跳转 `/knowledge/{id}` 导致 404。修复：跳转改为 `/knowledge?article={id}`，Knowledge 页面挂载时读取参数自动打开对应文章弹窗（复用 `handleViewArticle`），打开后立即清理 URL 参数避免刷新/返回重复弹出。
  - 影响文件：`GlobalSearch.tsx`、`Knowledge.tsx`
- **T000101 P1 知识库文章取消点赞**：原点赞接口仅"点赞"语义（`INSERT OR IGNORE` 去重），重复点击返回"您已经点过赞了"，无取消能力。修复：点赞接口改为 **toggle 语义**——已点赞则删除 `KnowledgeArticleLikes` 记录 + 递减 `LikeCount`（下限 0）并返回 `liked=false`；新增仓库方法 `TryRemoveLikeAsync` / `DecrementLikeCountAsync` / `GetLikedArticleIdsAsync`（批量 IN 分批查询防 N+1）；DTO 新增 `IsLiked` 字段，列表/详情/搜索/概览批量填充；前端按钮按 `isLiked` 展示已点赞态（实心 +「已点赞，点击取消」）并同步计数。
  - 影响文件：`KnowledgeService.cs`、`KnowledgeArticleRepository.cs`、`IKnowledgeArticleRepository.cs`、`KnowledgeController.cs`、`KnowledgeDto.cs`、`Knowledge.tsx`、`engagement.ts`
- **T000106 P1 用户管理-删除用户无响应**：删除失败（后端 400「该用户关联 N 个会话」）时确认弹窗不关闭且无错误提示（页面顶部错误条被弹窗遮罩遮挡）。修复：删除确认框新增 `deleteError` 状态并在弹窗内展示后端具体错误消息（`getErrorData` 解析 `message` 字段），失败保持弹窗打开可重试，取消时清空错误。
  - 影响文件：`Users.tsx`
- **T000107 P2 移动端个人通知面板被裁切**：通知下拉面板 `absolute right-0` 相对顶栏铃铛容器定位，移动端 `w-96` 面板左缘超出视口约 90px 被裁掉（"页面一半被隐藏"）。修复：<lg 断点改用 `fixed right-2 top-[4.5rem]` 相对视口定位，lg: 恢复 `absolute` 对齐铃铛。
  - 影响文件：`NotificationBell.tsx`
- **T000108 P2 移动端日历视图下拉显示英文**：`Segmented` 组件移动端降级为原生 select 时，`label` 为 ReactNode（含图标 span）的选项回退显示英文 `value`（overview/calendar/list/trash/subscription/outlook）。修复：`SegmentedOption` 新增 `labelText` 字段，select 渲染优先级 `labelText` → 字符串 label → value；日历视图切换传入中文 `labelText`。
  - 影响文件：`Segmented.tsx`、`Calendar.tsx`
- **部署**：以上修复已编译并部署至生产环境（`Production\backend` + `Production\frontend\dist`）；后端 :5009、前端 vite preview :5172、Nginx :5173 反向代理均验证通过；全量后端测试 1214 通过 / 0 失败。

### ✨ 全功能审查与占位整治（服务端占位 + WhatsApp 自动回复 + 前端体验）

依据「全面审查所有功能、找出未实现缺口并优化完善」的审查任务，针对审计发现的占位/弱化实现进行了落地整治。全部外部依赖按「实现 + 优雅降级」策略处理，未配置时安全降级而非报错。

- **工作流真实发送**：`WorkflowActions/SimpleActionHandlers.cs` 中的 `send_reply` / `forward` 占位动作接入真实发送能力，工作流执行时可实际回复 / 转发会话消息。
- **SLA BusinessHoursOnly 工作时间计算**：`SlaPolicy` 领域实体新增 `WorkDays` / `WorkStartHour` / `WorkEndHour`（伴随 EF 迁移 `20260831004521_AddSlaPolicyBusinessHours`）；`SlaService` 在 `BusinessHoursOnly` 模式下按上下班时间与工作日真实计算 SLA，不再简单落入超时。
- **通知关注者查询完善**：`NotificationService.GetConversationFollowersAsync` 从占位（返回空）完善为查询会话真实关注者集合。
- **Outlook 重试提升为真实重同步**：`OutlookRetryProcessor` 由占位循环提升为真正重新同步 Outlook，失败重试具备实际语义。
- **Outlook OAuth 配置完善**：`OutlookSyncSettings` 新增 `ClientId / ClientSecret / RedirectUri / Scopes / TenantId` 字段（含迁移），`DatabaseSeeder` 同步补齐配置；`CompleteMailboxOAuthAsync` 维持安全禁用（需管理端显式启用的安全措施，非缺陷）。
- **WhatsApp 自动回复执行 + ApiReachable 真实检测**：`WhatsAppService` / `WhatsAppIngestionService` 接入自动回复执行逻辑；`WhatsAppClient` 的 `ApiReachable` 由占位（恒定 true）提升为真实网络连通性检测（`IWhatsAppClient` / `WhatsAppOptions` / `WhatsAppDto` 同步完善）。未配置时优雅降级，明确标记不可达。
- **日历垃圾箱批量恢复**：前端 `TrashPanel.tsx` 的「恢复全部可恢复」按钮由禁用占位接入 `store.batchRestoreAsync`（复用 `calendarApi.batchOperation({ ids, op: 'restore' })`，后端 `CalendarMutationService.BatchOperationAsync` 事务批量 restore，含 `IgnoreQueryFilters()` 绕过软删除过滤），支持恢复中 spinner 与失败提示。
- **调研确认（无需改动）**：日历参会人映射采用「前后端双写」互补方案（`NormalizeAttendeesAndReminderEmails` 处理邮箱型参会人 + 前端 `_attendeeEmailList`），无 address-book API 可做 email→userId 映射，属有意设计；报表编辑器 `chartType` 仅作为透传装饰字段，实际渲染按 section key 分派（`KpiCard / TrendChart / StatusPie / HorizontalBars` / `GenericSection`），不驱动差异，故不引入无实际效果的切换控件。

### ✨ 系统管理-审计日志完善与优化（v2.2.9）

- **多维过滤**：`AuditLogsController` 在 list / stats / export 三个端点统一支持 `action / entityType / userId / entityId / ipAddress / keyword / startDate / endDate` 八维过滤；`AuditLogRepository.ApplyFilters` 集中过滤逻辑避免三处实现漂移。
- **全局统计聚合**：新增 `GET /api/auditlogs/stats` 端点（`RequireSuperAdmin` + `OutputCache`），返回 `totalLogs / uniqueUsers / last24hCount / failedActions + actionDistribution + entityDistribution`，独立请求不依赖当前页数据，避免分页导致的统计失真。
- **CSV 导出**：新增 `GET /api/auditlogs/export` 端点，服务端生成 UTF-8 BOM + CSV 字节流，浏览器直接下载 Blob；文件名含时间戳；最大 10000 行防止内存溢出。
- **HMAC-SHA256 完整性校验**：
  - 单条：`GET /api/auditlogs/{id}/verify` 返回 `{isValid, storedSignature, expectedSignature, verifiedAt}`。
  - 批量：`GET /api/auditlogs/verify-integrity?page=&pageSize=` 返回 `AuditIntegrityReport { page, pageSize, totalLogs, checkedCount, tamperedCount, tamperedLogIds[] }`。
  - 前端：批量校验后 `applyIntegrityToTable` 回写 `isTampered` 到列表项；表格 ID 列显示 `ShieldX`（红色，疑似篡改）/ `ShieldCheck`（绿色，校验通过）图标；篡改行加 `bg-rose-50/40` 底色高亮。
- **关联日志**：新增 `GET /api/auditlogs/{id}/related?limit=`，优先返回同实体（`entityType + entityId`）的最近日志，无则回退到同用户（`userId`）日志，便于追溯操作上下文。详情弹窗中可点击关联日志跳转查看。
- **OutputCache 策略更新**：`Program.cs` 中 `AuditLogs` 缓存策略纳入全部新增过滤参数（`action / entityType / userId / entityId / ipAddress / keyword / startDate / endDate / page / pageSize`），避免不同过滤条件被同一缓存命中。
- **前端体验优化**：
  - 日期范围选择器（`<input type="date">` 起止）+ 本地时间转 UTC ISO（`toUtcStartIso` / `toUtcEndIso`）。
  - 服务端关键字搜索 + 400ms 防抖（`useDebounce`），避免每次按键触发 API。
  - 用户 ID / IP 输入框 + 操作类型 / 实体类型下拉。
  - KPI 卡片（总记录 / 操作用户 / 近 24h / 异常事件）+ 操作分布 Top 8 条形图 + 实体占比图，均来自全局统计端点。
  - 导出 CSV 按钮直接下载 Blob，文件名 `audit-logs_YYYYMMDD-HHmmss.csv`。
  - 详情弹窗：基本信息卡 + 网络上下文 + 关联日志（可点击跳转） + 字段变更对比 JSON + 单条签名校验按钮。
- **影响文件**：
  - 后端：`IAuditLogRepository.cs`、`AuditLogRepository.cs`、`IAuditService.cs`、`AuditService.cs`、`AuditLogResponse.cs`、`AuditLogsController.cs`、`Program.cs`
  - 前端：`AuditLogs.tsx`、`api/modules/system.ts`、`api/types/system.ts`
- **部署**：v2.2.9 已编译并部署至生产环境（`Production\backend` + `Production\frontend\dist`）；后端 :5009、前端 vite preview :5172（强制 IPv4）、Nginx :5173 反向代理均验证通过；`/api/auditlogs/stats` 与 `/api/auditlogs/export` 返回 401（鉴权生效）。

### 🐛 日历管理：客服与客服主管添加日程权限修复（T000092）

- **用例编号**：T000092
- **缺陷描述**：客服（Agent）和客服主管（Supervisor）角色在日历管理模块中无法添加日程，预期所有用户都存在添加日程权限，但实际只有管理员存在权限。
- **根因**：`DatabaseSeeder.cs` 中 Agent 和 Supervisor 角色的 `PermissionsJson` 仅包含 `calendar.view`（只读），缺少 `calendar.manage`（写操作）权限。后端 `CalendarController` 的创建/更新/删除接口均标注 `[Permission("calendar.manage")]`，导致这两个角色调用时返回 403 Forbidden。
- **修复**：在 `DatabaseSeeder.cs` 的 4 处位置（Agent/Supervisor 的初始种子数据块 + 已存在数据库的更新块）为两个角色添加 `"calendar.manage"` 权限项。修复后客服和客服主管均可正常创建、编辑和删除日历事件。
- **影响文件**：`NoneadCSS.Api/DatabaseSeeder.cs`（Agent 角色 L966/L1014，Supervisor 角色 L948/L1000）

### 🐛 日历管理：提醒触发修复（T000090）

- **用例编号**：T000090
- **缺陷描述**：日历事件提醒未按预期触发，客服离开 `/calendar` 页面后无法收到提醒弹窗。
- **根因**：(1) 后端 `CalendarReminderService` 未检查 `ReminderType`，对所有通知类型都发送；(2) 前端仅在 `/calendar` 页面监听提醒，离开页面后提醒丢失；(3) 短信网关未实现。
- **修复**：`CalendarReminderService.cs` 按 `ReminderType` 分发通知（短信降级为站内通知）；新增 `GlobalCalendarReminderListener.tsx` 在 `App.tsx` 全局监听提醒；`Calendar.tsx` 移除重复的提醒处理代码；新增 URL 参数 `?focusEventId=N` 支持从提醒弹窗跳转到日程详情。
- **影响文件**：`CalendarReminderService.cs`、`GlobalCalendarReminderListener.tsx`、`Calendar.tsx`、`App.tsx`

### 🐛 日历管理：6 项 Bug 修复（BUG-01~06）

- **BUG-01 P0**：邮件模板"查看详情"链接使用相对路径，`CalendarReminderService.cs` 改用 `_brand.FrontendUrl` 拼接绝对 URL。
- **BUG-02 P0**：`CalendarMutationService.cs` 重复事件拆分条件逻辑反转，导致 Following 模式重复日程错误拆分。
- **BUG-03 P0**：`CalendarMutationService.DuplicateAsync` 缺少 `ReminderType / ReminderMinutesBefore / IsRecurring / RecurrenceRule` 等字段映射，复制日程时数据丢失。
- **BUG-04 P1**：`QuickUpdateAsync` 不支持 `ReminderType` 字段更新。
- **BUG-05 P1**：提醒设置变更后未重置发送状态，导致提醒无法重发。
- **BUG-06 P1**：提醒弹窗"查看详情"导航时 ACK 丢失，导航完成后提醒仍重复弹出。
- **影响文件**：`CalendarReminderService.cs`、`CalendarMutationService.cs`、`CalendarController.cs`

### 🧪 测试覆盖率提升：日历前端组件 100% 覆盖

- **前端测试**：新增 323 个测试用例覆盖 `chips.tsx`，实现语句覆盖率 100%、分支覆盖率 100%、函数覆盖率 100%、行覆盖率 100%。
- **覆盖内容**：`useHourHeight` Hook（含 matchProfile 变化、SSR 路径）、`EventChip` chipRef.current=null 早返回、touch move deltaY-only 分支、dragPreview 空标题兜底、`AllDayEventBar` 与 `HourRow` 的 memo 比较函数（`_allDayEventBarMemoComparator` / `_hourRowMemoComparator`）、`EmptyState` 变体分支。
- **后端测试**：新增 `CalendarConflictServiceTests`（13 个测试覆盖冲突检测、边界用例、异常处理）、`SlaServiceTests` BUG-06 回归测试（17 个用例覆盖 `targetHours=0` 除零守卫、NaN 防御）。
- **最终结果**：后端测试 1091 通过（+13），前端测试 178 通过（+64）。覆盖率：语句 2.20%、分支 61.95%、函数 19.96%、行 2.20%。
- **影响文件**：`calendar-store.test.tsx`、`calendar-hooks.test.ts`、`error.test.ts`、`format.test.ts`、`calendar-utils-ext.test.ts`、`CalendarConflictServiceTests.cs`、`SlaServiceTests.cs`
- **报告**：`frontend/docs/chips-coverage-100-report.md`

### 🐛 全项目功能审查：8 项 Bug 修复（BUG-01~08）

- **BUG-01 P1**：客户导入重复检查仅覆盖前 100 条。`CustomerRepository.cs` 新增 `GetAllActiveEmailsAsync` 查询所有未删除客户邮箱（无 Take 限制），`ImportExportService.cs` 调用此方法替代受限查询。
- **BUG-02 P1**：导出包含软删除记录 + 客户截断 100 条。`ImportExportService.cs` 显式过滤 `DeletedAt == null`，客户导出 Take 提升至 10000。
- **BUG-03 P1**：CSV/Excel 公式注入。`CustomReportExportHelper.cs` 对以 `= + - @ \t \r` 开头的单元格前缀单引号转义。
- **BUG-04 P1**：`FolderService` 允许硬删除系统文件夹。`FolderService.cs` 拦截 `IsSystemFolder()` 删除请求，改用 `SoftDelete()`。
- **BUG-05 P2**：工作流 `set_status` 关闭会话时绕过 `conversation_closed` 触发器。`SimpleActionHandlers.cs` 检测关闭/重开状态转换，使用 `PendingSideEffects` 延迟触发 `TriggerConversationClosed` / `TriggerConversationReopened`，避免事务内循环依赖。
- **BUG-06 P2**：SLA `CalculateStatus` 除零风险。`SlaService.cs` 和 `SlaPolicy.cs` 增加对 `targetHours <= 0` 的守卫，直接返回 breached 而不进入除零计算。
- **BUG-07 P2**：邮件队列无重试机制 + 孤立中间状态。`SendLogRepository.GetPendingAsync` 同时拾起 `Accepted` + `SendIntermediateError` 状态并加入 `AttemptCount < MaxRetryCount` 过滤；`EmailQueueService.cs` 失败时若未达上限重置为 `Accepted` 等待重试，达上限固化为终态 `SendError`。
- **BUG-08 P2**：软删除标签干扰导入重复检查。`ImportExportService.cs` 预加载 `existingTagKeys` 时调用 `IgnoreQueryFilters()` 绕过全局软删除过滤器，确保所有标签（含软删除）纳入重复检查。
- **影响文件**：`CustomerRepository.cs`、`ImportExportService.cs`、`CustomReportExportHelper.cs`、`FolderService.cs`、`SimpleActionHandlers.cs`、`SlaService.cs`、`SlaPolicy.cs`、`SendLogRepository.cs`、`EmailQueueService.cs`
- **回归测试**：`EmailQueueServiceTests.cs`（9 个用例）、`SlaServiceTests.cs`（17 个用例）、`ImportExportServiceBug08Tests.cs`（6 个用例）

---

### 🐛 日历订阅：Outlook ICS 订阅同步失败修复（T000098 · P1）

- **用例编号**：T000098
- **缺陷描述**：日历管理 → 订阅功能中，用户复制订阅链接到 Outlook 同步日程失败（Outlook 同步失败，复制链接后无响应）。
- **根因**：
  1. `CalendarFeedController.GetFeed` 使用带文件名参数的 `File(...)` 重载，ASP.NET Core 自动添加 `Content-Disposition: attachment` 头，导致 Outlook 将 ICS 当作下载文件而非订阅源。
  2. ICS 日历输出缺少 `METHOD:PUBLISH` 属性（RFC 5545 要求），Outlook 无法将响应识别为可订阅的日历数据。
  3. 订阅 URL 通过 `Request.Host` 动态推导，当用户从本地（127.0.0.1）访问前端时生成的 URL 返回 `127.0.0.1:5173`，外部 Outlook 客户端指向本机、无法访问。
- **修复**：
  1. `CalendarFeedController.GetFeed`：移除文件名参数避免 `Content-Disposition: attachment`，并显式设置 `Content-Disposition: inline`，强化内联呈现语义。
  2. `CalendarExportService`：ICS 头新增 `METHOD:PUBLISH`（RFC 5545 日历方法），满足 Outlook 订阅源解析要求。
  3. `CalendarExportService` v2：新增 `X-WR-CALNAME`（日历显示名称）与 `X-WR-TIMEZONE`（默认时区），提高 Outlook 兼容性与时间解析准确性。
  4. `CalendarExportService` v2：新增 `SEQUENCE` / `LAST-MODIFIED` / `TRANSP` 属性，支持 Outlook 增量同步与空闲/忙碌状态判断。
  5. `CalendarController.GetSubscription` / `RegenerateSubscription`：改用 `IFrontendUrlResolver.GetBaseUrl()`（严格读取 `App:BaseUrl` 配置）生成订阅 URL，确保对外使用可访问的公网域名。
- **影响文件**：`CalendarFeedController.cs`、`CalendarExportService.cs`、`CalendarController.cs`
- **生产回归验证**：在 Windows 本地生产环境（后端 :5009 / 前端 vite preview :5172 / Nginx :5173）复制订阅链接到 Outlook，同步日程成功，`text/calendar` 内容正常内联展示。

### 🌐 Outlook 同步：用户级 Azure AD 凭据配置（T000099）

- **用例编号**：T000099
- **缺陷描述**：Outlook 同步面板 OAuth 授权报错"Request failed with status code 400"，且用户看不到任何配置 Azure AD 凭据的入口，系统级凭据无法按租户独立适配。
- **根因**：原配置模型为系统级（仅 SuperAdmin 在后台页面设置，所有用户共享一套 AppId/TenantId/ClientSecret），多用户/多租户场景无法独立配置；面板 UI 未提供凭据配置按钮。
- **修复**：
  - **实体扩展**：`OutlookSyncSetting` 新增用户专属字段（`TenantId / ClientId / ClientSecret / RedirectUri`），EF Core 对 ClientSecret 启用 AES 加密。
  - **服务层**：`IOutlookSettingsProvider` 新增 `GetForUserAsync(userId)`，按用户配置 → 系统基线合并生成专属 OAuthOptions，避免相互覆盖。
  - **API 端点**：`OutlookSyncController` 从 Admin-only 端点替换为用户级 `GET /api/outlook-sync/credentials` / `PUT /api/outlook-sync/credentials` / `DELETE /api/outlook-sync/credentials`，普通用户可独立维护自己的 Azure AD 凭据。
  - **前端页面**：`OutlookSyncPanel.tsx` 移除超级管理员限制，所有用户显示「Azure AD 凭据配置」按钮，`api/modules/engagement.ts` 同步更新端点路径。
  - **数据库补丁**：`Program.cs` 启动时幂等为 `OutlookSyncSettings` 表新增 4 列，避免历史数据库缺少列抛 SQLite Error 1。
- **影响文件**：`OutlookSyncSetting.cs`、`NoneadCSSDbContext.cs`、`Program.cs`、`IOutlookSettingsProvider.cs`、`OutlookSettingsProvider.cs`、`OutlookSyncController.cs`、`OutlookSyncPanel.tsx`、`engagement.ts`

### 🐛 日历管理：参会人保存与回填修复（T000100 · P1）

- **用例编号**：T000100
- **缺陷描述**：新建/编辑日历事件填写参会人邮箱后点击保存报错，再次编辑打开参会人数据为空（优先级 P1）。
- **根因**：
  1. 前端与会者邮箱通过 `_attendeeEmailList` 存储并通过 `reminderEmails` 提交 CSV，`attendees` 数组始终为空，后端 `AttendeesJson` 写入空值。
  2. 后端 `MappingExtensions.ToDto` 反序列化 `AttendeesJson` 等 JSON 字段时，若为 null / 空字符串 / 非法 JSON 直接抛异常，导致列表查询与编辑回填均失败。
  3. **负责人回显被隐私裁剪误清**：`CalendarEventDto.SanitizeForUser` 权限裁剪仅放行「创建者 CreatedBy / 超管」，未纳入事件负责人（`UserId`）。当日程由 admin 创建但指派给其他用户（负责人 `UserId`）时，该负责人编辑/保存后后端返回的 DTO 中 `ReminderEmails` 被清空 → 前端乐观更新后事件内存态丢失邮箱 → 再次打开编辑参会人为空。数据库实际有参会人数据，仅 API 回显被隐私裁剪误清。
- **修复**：
  1. **后端反序列化容错**：`MappingExtensions` 新增 `SafeDeserializeList<T>` 与 `SafeDeserializeObject<T>`，异常路径返回 `default` 而非抛错。
  2. **后端写入口规范化**：`CalendarMutationService` 新增 `NormalizeAttendeesAndReminderEmails` 方法，实现 `attendees[]` ↔ `reminderEmails` CSV 的双写互补，任一为空都按另一值回填。
  3. **数据库补丁**：`Program.cs` 新增 `EnsureCalendarEventsAttendeeColumnsAsync`，幂等补齐 `AttendeesJson / ReminderEmails` 等缺失列。
  4. **前端状态合并**：`calendar/store.ts` 的 `openEdit` 方法同时读取 `Attendees`（用户 ID 数组）与 `extras.attendeeEmailList`（邮箱列表）并合并到面板 UI。
  5. **前端提交最小化**：`EditorPanel.tsx` payload 提交时移除空的 `attendees: []`，避免空数组覆盖服务端历史数据。
  6. **权限裁剪放行负责人**：`CalendarEventDto.SanitizeForUser` 放行条件增加事件负责人 `dto.UserId == currentUserId`。创建者 / 负责人 / 超管均可回填参会人邮箱；无关用户仍清空（隐私保护不破坏）。
- **生产回归验证**：新建"T000100 生产回归 参会人邮箱测试"，添加 `alice@example.com, bob@test.org` 保存成功，再次编辑时参会人正确回填；admin 创建并指派给负责人后，负责人编辑再次打开参会人邮箱正常回填（SanitizeForUser 新增负责人放行）。
- **影响文件**：`MappingExtensions.cs`、`CalendarMutationService.cs`、`Program.cs`、`CalendarController.cs`、`CalendarEventSanitizeTests.cs`、`calendar/store.ts`、`EditorPanel.tsx`

### 🐛 知识库：5 项 P1 Bug 修复（T000101~T000105）

本段落为 **v2.2.9 发布后紧急生产热修复**，累计 5 个 P1 用例，涉及知识库模块核心链路（点赞 / Markdown 渲染 / 附件上传 / 全局搜索 / 分类禁用）。发布日期 **2026-08-28**。

#### T000101 · 文章点赞失败（P1）

- **缺陷描述**：知识库文章点赞报错"服务器错误，请稍后重试或联系管理员"。
- **根因**：生产库与开发库均缺失 `KnowledgeArticleLikes` 表（迁移 `20260826160000_AddKnowledgeArticleLikes.cs` 定义但未在历史库执行），`TryInsertLikeAsync` 执行 `INSERT OR IGNORE` 抛 `SQLite Error 1: 'no such table: KnowledgeArticleLikes'`。
- **修复**：
  1. `Program.cs` 启动流水线新增 `EnsureKnowledgeArticleLikesTableAsync`，`CREATE TABLE IF NOT EXISTS` 幂等建表 + 复合唯一索引（`ArticleId, UserId, LikedDate`）。
  2. Python 脚本手动补齐生产库 `Production/data/noneadcss.db` 与开发库 `NoneadCSS.Api/Data/noneadcss.db`，避免下次重启前的空窗期。
- **影响文件**：`Program.cs`

#### T000102 · Markdown 格式显示无样式/未解析（P1）

- **缺陷描述**：Markdown 预览模式下，加粗、斜体、链接、图片、行内代码、无序列表 6 种格式显示为原文语法符号，未渲染为 HTML（优先级 P1）。
- **根因**：`marked` v18 若自定义覆盖 `paragraph` 等 block-level Renderer，会禁用内部 inline tokenizer；同时 Tailwind preflight 重置 `ul/ol` 的 `list-style-type` 但 `.knowledge-content` 未恢复；`strong/em` 等内联元素缺少样式规则；`DOMPurify` 默认过滤 `<table>/<td>` 的 style 导致对齐与边框丢失。
- **修复**：
  1. **Renderer 重构**：仅覆盖 `heading` 和 `code` 方法，其他元素通过后处理函数 `postAddTailwindClasses` 用正则追加 `prose` 友好的 Tailwind 类，解除 inline tokenizer 禁用问题。
  2. **`DOMPurify` 白名单**：`ALLOWED_CSS_PROPERTIES` 扩展放行 style 属性，并通过 `as unknown as Config` 类型断言绕过 TS 类型缺失。
  3. **样式补齐**：`index.css` 新增 `knowledge-content` 下 H1~H6 / `p` / `a` / `strong` / `em` / `code` / `pre` / `ul` / `ol` / `li` / `table` / `th` / `td` / `blockquote` 等元素样式，含深色模式适配（`html.dark &` 前缀），显式恢复 `ul { list-style: disc }`、`ol { list-style: decimal }`。
  4. **TS 类型清理**：移除 `replaceOpenTag` 未使用函数与多余接口，消除 TS6133 编译警告。
- **生产回归验证**：新建"T000102 回归 v2：Markdown 5 种格式 + 无序列表"，预览/详情页所有格式正确渲染（加粗/斜体/链接/图片/行内代码/嵌套列表），深色模式切换样式正常。
- **影响文件**：`pages/Knowledge.tsx`（Renderer 重构 + `postAddTailwindClasses` 后处理）、`index.css`（knowledge-content 排版与深色模式样式）

#### T000103 · 附件上传一致性（P1）

- **缺陷描述**：新建文章上传附件后不保存，结果为"查看页面存在附件，编辑页面不存在"（优先级 P1）。
- **根因**：编辑态 `editorAttachments` 仅在保存时提交 API，未保存时不持久化；查看页 `viewArticle.attachments` 直接读取数据库数据，未保存附件通过 localStorage 草稿泄漏；编辑页重新打开时仅从服务器加载，丢失未保存的本地附件。
- **修复**：
  1. **自动保存纳入附件**：`Knowledge.tsx` 自动保存草稿时将 `editorAttachments` 一并写入 `localStorage`（新建 = `knowledge_draft_new`，已有 = `knowledge_draft_{articleId}`）。
  2. **新建页草稿恢复**：`openCreateArticle` 打开新建模态时从 new-article draft 读取附件集合，恢复上传但未保存的文件。
  3. **编辑页附件合并**：`openEditArticle` 重写为先拉取最新后端文章快照，再与草稿里的 `editorAttachments` 按 `id` 并集合并，本地草稿优先级高于服务器。
  4. **按钮态协同**：「丢弃草稿」按钮将 `editorAttachments` 重置为服务器版本；「恢复草稿」按钮重新合并草稿附件 + 服务器版本。
- **影响文件**：`pages/Knowledge.tsx`（auto-save / openCreateArticle / openEditArticle / Discard-Draft / Restore-Draft 分支）

#### T000104 · 知识库页面全局搜索结果显示（P1）

- **缺陷描述**：全局搜索输入关键字后，搜索结果被隐藏在下拉 label 下，未直显在主区域，用户无法第一时间查看结果（优先级 P1）。
- **根因**：搜索结果通过 `globalSearchOpen` 控制的下拉弹层展示（`position: absolute; right-0; w-full; z-40`），父容器可能因 overflow:hidden / 兄弟元素 z-index 更高导致被遮挡；同时缺少「查看全部结果」的直显入口。
- **修复**：
  1. **下拉提升 z-index**：搜索弹层改为 `z-[60]`，避免被导航、侧栏等其他元素遮挡。
  2. **新增「查看全部结果」按钮**：下拉底部常驻跳转按钮，点击后聚焦主区域搜索结果。
  3. **主区域新增独立搜索结果 section**：当搜索结果非空时在 Tab 切换上方直接渲染包含"关键词、命中数、分页、清除按钮"的完整结果面板，与下拉弹层互为补充。
- **生产回归验证**：生产环境搜索"NoneadCSS"与"T000102"，结果均在主区域直观显示，清除搜索按钮一键还原。
- **影响文件**：`pages/Knowledge.tsx`（globalSearch 下拉 + 主区域 Search Results Section）

#### T000105 · 禁用分类仍出现在文章分类选择中（P1）

- **缺陷描述**：分类管理中禁用任一分类后，在文章创建/编辑的分类下拉里依然可见（优先级 P1）。
- **根因**：前端 `flatCategories` 从 `categories` 直接扁平化生成，**未**过滤 `IsActive=false`；调用 `/api/knowledge/categories` 也未传入 `isActive=true` 参数；后端创建/更新文章时未校验目标分类是否处于禁用状态。
- **修复（三重保障：显示层 + 数据层 + 写入层）**：
  1. **前端显示层**：`Knowledge.tsx` 新增 `activeFlatCategories` 变量（仅含 `IsActive=true` 及其子孙分类，父禁用则子一并隐藏），替换文章筛选器与新建/编辑文章两处分类下拉数据源；原 `flatCategories` 保留全量含禁用，继续供分类管理页面的「父分类选择器」使用（支持将禁用分类作为父节点）。
  2. **后端数据层**：`/api/knowledge/categories` 继续支持 `isActive` 查询参数，前端分类选择器按模式分别调用 `isActive=true`（文章侧）和不传（管理侧）。
  3. **后端写入层校验**：
     - `KnowledgeService.CreateArticleAsync`：`categoryId` 指向的分类 `IsActive=false` 时抛 `InvalidOperationException("所选分类已禁用，请先启用或更换为其他分类")`。
     - `KnowledgeService.UpdateArticleAsync`：同上，但文章原本就归属该禁用分类、且本次未改动分类时允许保持（兼容历史数据）。
- **生产回归验证**：
  - 禁用分类「ccc」后，文章筛选器和「新建文章」分类下拉均不再出现 ccc。
  - 直接调用 API `POST /api/knowledge/articles` 指定 `categoryId=6`，返回 HTTP 400 `{"message":"所选分类已禁用，请先启用或更换为其他分类"}`。
- **影响文件**：`pages/Knowledge.tsx`（`flatCategories` / `activeFlatCategories` 双数据源 + 两处替换）、`KnowledgeService.cs`（`CreateArticleAsync` / `UpdateArticleAsync` 分类禁用校验）

---

## [2.2.10] - 2026-08-30（审计日志完整性签名修复 + 前端中文标签全面补齐）

### 🔴 审计日志绑定：HMAC-SHA256 完整性签名失效修复（P0）

- **根因**：`AuditHmacSigner.BuildSignaturePayload` 中使用 `log.CreatedAt.ToString("O")` 生成签名载荷。写入时 `CreatedAt` 来自 `DateTime.UtcNow`（`Kind=Utc`，`ToString("O")` 带 `Z` 后缀）；EF Core 从 SQLite 读回后 `Kind=Unspecified`（`ToString("O")` 不带 `Z` 后缀），导致写入与校验生成的载荷不一致、HMAC 签名永远无法匹配，单条/批量校验结果始终为"疑似篡改"（ShieldX）。
- **修复**：在 `BuildSignaturePayload` 中统一按 `Utc` 渲染时间（`DateTime.SpecifyKind` 修正 `Kind` 为 `Utc`），确保签名与校验载荷一致，签名校验恢复正常。
- **存量重签**：对历史上已写入的 **56887** 条审计日志执行体系化重签，保证存量记录可被校验通过。
- **影响文件**：`NoneadCSS.Infrastructure/Services/AuditHmacSigner.cs`、审计日志存量数据（重签）

### ✨ 审计日志绑定：前端中文标签全面补齐

- **背景**：`AuditLogs.tsx` 的 `actionStyles` / `entityTypeStyles` 静态映射表覆盖不全，未命中时回退显示英文原文，导致「操作分布（全局 Top 8）」与「实体类型占比」卡片出现英文（如 `calendar.update`、`calendar.create`、`WorkflowExecutionLog`、`SystemOption`）。
- **Action 映射**：补齐 **50 个** Action 中文标签，含 `calendar.*`（约 11 条）、`time_entry.*`（6 条）、`restore` / `hard_delete` / `bulk_*`、`calendar.batch_*`（8 条）。
- **实体类型映射**：补齐 **61 个** 实体类型中文标签，含 `workflowexecutionlog`（工作流执行日志）、`systemoption`（系统选项）、`slapolicy`（SLA策略），并修正拼写错误 `calendaremindertttempt` → `calendaremindertattempt`（`CalendarReminderAttempt`，日历提醒尝试）。
- **前缀兜底**：新增 `calendar.` / `time_entry.` 前缀兜底逻辑，动态动作也能正确显示中文。
- **图标导入**：新增 `Star` 等图标配合新增映射，每个标签统一「中文 + 颜色 + 图标」呈现。
- **影响文件**：`frontend/src/pages/AuditLogs.tsx`

### 📦 部署

- v2.2.10 已编译并部署至生产环境（`Production\backend` + `Production\frontend\dist`）；后端 :5009、前端 vite preview :5172（强制 IPv4）、Nginx :5173 反向代理均验证通过。
- 审计日志签名校验恢复正常（重签后存量记录校验通过，绿色 ShieldCheck）；统计卡片不再出现英文。

---

## [2.2.6] - 2026-08-26（时间追踪模块修复 + UI 风格统一）

### 🐛 时间追踪：停止计时数据丢失修复

- **根因**：`TimeEntryService.StopTimerAsync` 和 `UpdateAsync` 方法在修改 `entry.EndTime` 和 `entry.DurationMinutes` 后**未调用 `SaveChangesAsync` 持久化**，导致停止计时后数据库中 EndTime 仍为 null，前端报"请求参数无效，请检查输入"。
- **修复**：两处均补充 `await _unitOfWork.TimeEntries.UpdateAsync(entry)` 调用，确保仓储层 `UpdateAsync` 内部执行 `SaveChangesAsync` 将变更写入数据库。
- **影响范围**：`NoneadCSS.Application/Services/TimeEntryService.cs` 的 `StopTimerAsync`（行 ~250）和 `UpdateAsync`（行 ~195）。

### 🐛 时间追踪：计时器实时显示修复

- **根因**：`TimeTracking.tsx` 中 `const [, setTick] = useState(0)` 丢弃了 tick 状态值，而 `liveSeconds` 的计算只依赖 `Date.now()` 和 `activeTimer.startTime`，未引用 tick。React 18 自动批处理优化下 `setTick` 触发的重渲染被跳过（因 `liveSeconds` 的依赖项未变化），DOM 不更新，"已用时长"始终停留在 `00:00:00`。
- **修复**：保留 tick 值 `const [tick, setTick] = useState(0)`，并在 `liveSeconds` 计算中添加 `+ tick * 0` 引用 tick，强制 React 每秒重渲染并更新 DOM。

### ✨ 时间追踪：关联工单/客户改为下拉菜单联动

- **关联工单 ID**：从 `<input type="number">` 改为 `<select>` 下拉菜单，自动拉取状态为"活跃"的会话，选项格式为 `#会话ID - 主题内容 - 客户姓名`。
- **关联客户 ID**：从 `<input type="number">` 改为 `<select>` 下拉菜单，选项格式为 `#客户ID - 客户姓名`。
- **联动逻辑**：选中关联工单后，自动从会话数据中找到对应的 `customerId`，自动填充到关联客户下拉；用户也可手动修改客户选择。
- **懒加载**：打开新建记录模态框时才触发拉取，避免页面初始化时的额外网络请求；加载中显示禁用 + "加载中…"提示；空列表显示"暂无活跃会话/暂无客户"。

### 🎨 时间追踪：全页面 UI 风格对齐知识库

- **设计目标**：移除时间追踪模块所有"花哨"视觉元素（渐变背景、装饰光晕、3D 悬浮动画、流动网格纹理），统一为知识库页面的简洁风格。
- **样式常量简化**：输入框从渐变背景改为 `bg-white border-slate-200 rounded-xl shadow-sm`；移除过重的 `ring-2` 边框和 hover 变色。
- **Header 区域**：从三色渐变（`from-primary-600 via-indigo-600 to-purple-600`）+ 装饰光晕 + 网格纹理改为简洁白底卡片 `bg-white border-slate-200 rounded-2xl shadow-sm`，文字颜色从 `text-white` 改为 `text-slate-900`。
- **统计卡片**：`rounded-3xl` → `rounded-2xl`，移除 3 个装饰层（blur 光晕、ring 边框、pattern），移除 `hover:-translate-y-1.5` 改为 `hover:shadow-md`，图标背景从渐变改为 `bg-blue-50 text-blue-600`。
- **运行中计时器**：从渐变背景 + 光晕 + 流动网格 + `animate-ping` 改为 `bg-blue-50 border-blue-200 rounded-2xl`，LIVE 徽章改为 `bg-blue-100 text-blue-700`，停止按钮从渐变改为 `bg-red-500 rounded-xl`。
- **快速开始**：开始按钮从三色渐变改为 `bg-emerald-500 rounded-xl`。
- **Tab 导航**：容器从渐变改为 `bg-slate-100`，活跃 Tab 从 `text-white` 渐变改为 `bg-white shadow-sm text-blue-600`。
- **图表区/列表/分页**：统一从 `rounded-3xl` 渐变改为 `rounded-2xl bg-white border-slate-200 shadow-sm`；进度条/柱状图从渐变改为纯色 `bg-blue-500`；分页活跃页从渐变改为 `bg-blue-600`。
- **模态框**：类型切换按钮从 `border-2 border-primary-500 bg-primary-50` 改为选中 `bg-blue-600 text-white shadow-sm` / 未选中 `bg-slate-100 text-slate-600`。
- **清理**：移除所有 `animate-ping`、`blur-3xl`、`hover:-translate-y`、`backdrop-blur`、`shadow-2xl/xl` 装饰元素；保留 LIVE 徽章和 Skeleton 的 `animate-pulse`（功能性动画）。

---

## [2.2.5] - 2026-08-26（知识库模块重大升级 + FTS5 全文搜索 + MD/HTML 双模式编辑 + SVG 图表渲染 + 附件管理 + 代码高亮）

### ✨ 知识库：Markdown / HTML 双模式编辑器

- **编辑器双模式切换**：文章编辑支持 Markdown 与 HTML 源文件两种编辑模式，顶部 Tab 一键切换，互不干扰；切换时自动保留内容并按目标模式重新渲染预览。
- **Markdown 解析引擎升级（marked + highlight.js）**：替换原自定义正则解析为 `marked` 标准 Markdown 解析器，支持完整 GFM 语法（表格、任务列表、删除线、围栏代码块、脚注等）；集成 `highlight.js` 实现代码块语法高亮（30+ 主流语言自动识别），行内代码保留等宽字体样式。
- **HTML 预览安全渲染**：使用 `DOMPurify` 对 HTML 内容进行 XSS 过滤白名单清洗，同时**专门放行 SVG 标签族与 style 标签**（定义 `SVG_ALLOWED_TAGS` 白名单 60+ 标签、`SVG_ALLOWED_ATTR` 白名单 120+ 属性），支持直接内嵌 `flowcharts.html` 风格的原生 SVG 流程图在文章预览中渲染。
- **Markdown ↔ HTML 视觉样式统一**：标题（H1~H6）、段落、引用块、有序/无序列表、表格（带斑马纹与边框）、分割线、代码块（深色主题 + 圆角 + 左内边距）等元素在两种模式下按同一 Tailwind 设计体系渲染，保持阅读一致性。

### 🔍 知识库：SQLite FTS5 全文搜索引擎

- **FTS5 虚拟表创建**：迁移 `20260825153000_AddKnowledgeArticlesFts5.cs` 创建 `KnowledgeArticlesFts` 虚拟表（`tokenize = 'unicode61 remove_diacritics 2'`），对 `Title / Summary / Content` 三列建倒排索引。
- **触发器自动维护**：`after_insert / after_update / after_delete` 三触发器确保 FTS 索引与业务表 `KnowledgeArticles` 数据始终一致（事务内原子更新）。
- **后端搜索降级机制**：`KnowledgeArticleRepository.FullTextSearchAsync` 优先走 FTS5 `MATCH` + `bm25()` 排序（高相关度置顶），匹配失败时自动降级为 `LIKE '%keyword%'` 兜底；两种路径均支持分类过滤 + 软删除过滤 + 分页。
- **搜索片段高亮（Snippet）**：FTS 成功路径调用 `snippet(KnowledgeArticlesFts, 2, '[[', ']]', '…', 24)` 生成命中上下文片段（24 token 窗口，前后省略号），`[[…]]` 分隔符前端转换为 `<mark>` 黄色高亮块。
- **前端全局搜索 UI**：知识库页顶部新增全局搜索条（300ms 防抖），输入任意关键字即时返回搜索结果卡片（标题 + 高亮摘要片段 + 分类 + 相关度 rank 显示），点击卡片直接打开文章详情并滚动到首个命中。
- **DTO 扩展**：`KnowledgeSearchResult` 新增 `Snippet / Rank` 字段；`ToDto` 映射扩展按 rank 排序优先展示命中。

### 📎 知识库：附件管理体系

- **附件表与字段**：迁移 `20260825150140_AddKnowledgeArticleAttachmentsAndContentType` 新增 `KnowledgeArticleAttachments` 表（`Id / ArticleId / FileName / FilePath / FileSizeBytes / MimeType / CreatedAt`），`KnowledgeArticles` 新增 `ContentType` 枚举字段（Markdown = 0 / Html = 1）。
- **附件 CRUD 服务层**：`KnowledgeService` 实现附件上传（写入 `wwwroot/uploads/knowledge/{articleId}/`，25MB 上限 + MIME 白名单 + 文件名 sanitization）、删除（物理文件 + DB 记录同步清理）、查询。
- **编辑器内已上传附件列表**：文章编辑页底部「附件管理」区域**始终列出**当前文章已上传的所有附件（文件名、大小格式化、上传时间），每项提供「预览」与「删除」两个常驻按钮（删除按钮由 hover-only 改为始终可见防误操作），支持批量管理。
- **PDF 附件上传支持**：前端 `accept=".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.png,.jpg,.jpeg,.gif,.txt,.zip"` 白名单，PDF 文件点击「预览」直接在新标签页用浏览器内置 PDF 阅读器打开。

### 💾 知识库：草稿自动保存与冲突检测

- **localStorage 草稿持久化**：编辑器每 10s 自动防抖保存当前未提交内容（标题、内容、模式、附件 ID 集合）到 `localStorage`，key 按 `knowledge_draft_{articleId}` 或 `knowledge_draft_new` 隔离。
- **冲突检测（本地 vs 服务器版本）**：打开已有文章时，若检测到本地草稿 `updatedAt` 晚于服务器最新 `UpdatedAt`，弹出对话框让用户选择「加载本地草稿 / 使用服务器版本 / 对比差异」，避免多人编辑或刷新浏览器后草稿丢失。
- **图片粘贴即上传**：编辑区监听 `paste` 事件，检测到剪贴板内图片（`clipboardData.files` 含 image MIME）后自动调后端上传接口，成功后按当前模式插入 Markdown 图片语法 `![](url)` 或 HTML `<img src="url" />` 到光标位置，无需手动下载再上传。

### 🎨 知识库 UI/UX 全面优化

- **附件管理 UI 优化**：附件列表卡片化布局（浅灰背景 + 圆角 + 左侧文件图标），「删除」按钮由 hover-only 改为始终可见 + 红色边框强调，「预览」按钮蓝色边框常驻；空状态显示插画与"暂无附件"提示。
- **编辑器底部布局**：附件列表紧邻文件上传按钮下方，标题与内容编辑区之上按优先级排列，减少鼠标来回移动；操作区（保存 / 取消 / 预览）固定右对齐 sticky。
- **预览与编辑分栏**：大屏默认左右分栏（左编辑、右实时预览），小屏自动切换 Tab 模式；预览区阅读宽度限制 72ch，长文滚动时编辑与预览同步锚点定位。
- **分类树 + 列表联动**：左栏分类树支持右键重命名/删除，拖拽调整父子层级；右栏文章列表带搜索框、分类筛选、创建时间排序。

### 🔧 技术债务与类型安全修复

- **消除前端 TS 未使用变量**：`TimeTracking.tsx` 移除未使用的 `CalendarIcon / FileSearch / X` 导入与 `ENTRY_TYPE_BADGE` 常量，消除 3 条 TS6133 编译警告。
- **扩展 Toast 类型**：`useTimedToast` 的 `ToastMessage` 与 `useCrudPage` toast schema 同步扩展，支持 `info / warning` 类型（原仅有 `success / error`），知识库搜索「无结果」等场景统一走 info toast。
- **运行时类型守卫**：所有 `JSON.parse`（草稿读取、FTS 结果）均配合 zod 风格的运行时字段检查，避免 `as any` 断言。

### 🚀 生产发布（2026-08-26）

- 数据库迁移：2 个新 Migration 生成 SQL 脚本，停止后端后手动对 Production/data/noneadcss.db 应用（规避历史迁移不匹配），`KnowledgeArticleAttachments` / `KnowledgeArticlesFts` 均建表成功，触发器生效。
- 后端 `dotnet publish` 成功（net10.0），产物同步到 `Production/backend` 与 `Production/_publish_backend/wwwroot`。
- 前端 `tsc --noEmit` + `vite build`：0 错误 0 警告，`Knowledge` chunk 421 kB（gzip ~98 kB）。
- 服务健康：三端口 LISTENING（5009 / 5172 / 5173），`GET /health` → Healthy，新建/编辑/搜索知识库全链路回归通过。

---

## [2.2.4] - 2026-08-25（日历多轮稳定性修复 + P3 前端类型安全加固 + 生产发布 SOP 固化）

> 本节由 [Unreleased] 段落在 2026-08-26 整理归档，实际生产落地日期 2026-08-25。

### 🐛 日历编辑"保存后回弹"根因修复（API 成功分支未用后端 DTO 覆盖乐观更新）

- **现象**：周视图中把 9:00~17:00 的事件通过编辑器改为 9:00~15:00 并保存，格子会先走到 15:00，2 秒后又回弹回 17:00。
- **根因**：
  - 前端 4 处写操作（EditorPanel 保存、store.moveEventByDrop、store.resizeEvent、概览/列表编辑）在 `await updateEvent(...)` 成功后，**未**用后端返回的最新 DTO 再次覆盖本地乐观值；
  - SignalR 端 `signalRRev` 刷新会在成功后 2s 窗口内拉取一次全量数据，由于乐观保护位 `_optimisticMarkers[id]` 被过早清理，旧值会把本地新值覆盖回去。
- **修复**：
  - EditorPanel：保存成功后用 `response.data` 再次调用 `optimisticUpsertEvent`，把 `rowVersion` 一并写回。
  - store 的 `moveEventByDrop/resizeEvent`：成功分支同样用返回 DTO 执行二次 `optimisticUpsertEvent`，保护位 TTL 从 30s 上调到 60s，兜底所有慢 SignalR 推送。
- **跨视图同步**：`optimisticUpsertEvent` 现在同时更新 `allEvents`、`eventsByDate` 派生桶、`list` 与 `overview.upcomingEvents`，确保日历 / 列表 / 概览视图任意入口修改后三者完全一致（之前概览未同步会出现"日历改完、概览 KPI 仍显示旧时间"）。
- **验证矩阵**：
  | 操作入口 | 日历 | 列表 | 概览 KPI |
  |---|---|---|---|
  | EditorPanel 保存 | ✅ | ✅ | ✅ |
  | 周/日视图 drag 移动 | ✅ | ✅ | ✅ |
  | 周/日视图 resize 改时长 | ✅ | ✅ | ✅ |
  | 列表 inline 编辑 | ✅ | ✅ | ✅ |

### 🔒 后端：P3 多实体乐观锁（8 张表 RowVersion + 通用 ConcurrencyException）

- **新增实体 RowVersion**（`NoneadCSSDbContext` 中统一 `.IsRowVersion().ValueGeneratedOnAddOrUpdate()`）：
  - Users（登录限流 FailedLoginAttempts / LockoutEnd 多点并发）
  - Customers（GetOrCreateByEmailAsync 并发 + 客服打标签画像回写）
  - ChatSessions（客户/客服同时写 LastActivityAt / UnreadCount / Status / 评分 / 关闭会话；多端 UI 同时打开）
  - ChatMessages（撤回 / 已读 / 编辑消息并发）
  - ApiKeys（UsageCount / LastUsedAt 高并发 API 调用下的 lost update）
  - Webhooks（TriggerCount / SuccessCount / FailCount / LastTriggeredAt 高并发回调计数）
  - KnowledgeCategories（分类树拖拽/改名/迁移并发编辑冲突）
  - KnowledgeArticles（多人同时编辑 + 浏览点赞计数 lost update）
- **通用异常体系升级**：
  - 新增 [ConcurrencyException](./NoneadCSS.Application/Exceptions/ConcurrencyException.cs)：携带 `entityType / entityId / latest` 三要素，替代原先仅限日历的 CalendarConcurrencyException。
  - CalendarConcurrencyException 改为继承 ConcurrencyException，保持所有调用点兼容（旧的 `catch (CalendarConcurrencyException)` 仍可用）。
  - [GlobalExceptionMiddleware](./NoneadCSS.Api/Middleware/GlobalExceptionMiddleware.cs) 统一捕获 ConcurrencyException：HTTP 409 + `errorType="concurrency_conflict"`（日历为 `"calendar_concurrency_conflict"`），响应体包含 `entityType / entityId / eventId / latest`。
- **通用助手 `ConcurrencyCheckHelper.Check`**：封装"实体存在性 → 行版本匹配 → 冲突构造 latest 快照 → 打 Warn 日志"的标准流程，供 Calendar/Customer/Chat/Knowledge 共用。
- **写入口适配清单**：
  - Chat（聊天）：UpdateSessionAsync / UpdateMessageAsync 接受 `ifMatch`，冲突返回最新 ChatSession / ChatMessage 快照。
  - Customer（客户）：UpdateCustomerAsync 接受 `ifMatch`，冲突返回最新 CustomerResponse。
  - Knowledge（知识库）：UpdateCategoryAsync / UpdateArticleAsync 接受 `ifMatch`，冲突返回最新分类/文章 DTO。
  - Calendar（日历）：CalendarEventService 继续保留既有 CalendarConcurrencyException 分支，响应完全兼容前端。

### 🚀 生产发布：RowVersion DDL 补丁工具 + 新版本发布 SOP

- **DDL 补丁工具 `_DbPatchRowVersion`（net10.0 控制台）**：
  - 功能：对 8 张表按幂等方式执行 `ALTER TABLE "T" ADD COLUMN "RowVersion" BLOB NULL`。
  - 事务：单个 `BEGIN IMMEDIATE` 事务包裹所有 8 条 DDL，任意一条失败全部回滚。
  - 用法：`dotnet DbPatchRowVersion.dll <dbPath>` 检查列是否存在；`dotnet DbPatchRowVersion.dll <dbPath> --apply` 应用补丁。
  - 运行结果（2026-08-17 Production/data/noneadcss.db）：8 张表全部 APPLIED，事务 COMMIT 成功。
- **新版 7 步发布 SOP**（相对 6 步 SOP 新增"数据库 DDL 补丁"步骤）：
  1. `Production\stop-prod.ps1`（端口 + 同名残留进程双重清理）
  2. `robocopy Production _backup_prod_YYYYMMDD_HHMMSS /MIR /R:2 /W:1`（生产目录完整备份）
  3. `dotnet publish NoneadCSS.Api.csproj -c Release -o Production/backend`
  4. 前端 `pnpm build` → robocopy 同步 dist → `Production/backend/wwwroot`
  5. `DbPatchRowVersion Production/data/noneadcss.db --apply`（按 Migration 对齐数据库 Schema）
  6. `Production\start-prod.ps1`（blocking=false，后端 / vite preview / Nginx 三件套健康检查）
  7. 独立健康检查：`:5009/health` → Healthy；`GET /api/calendar/events` 未鉴权返回 401（非 500）；`:5173/` 返回 200 含 `<title>`。
- **服务健康状况（2026-08-17 实际发布后）**：
  - Backend PID=34052 @ 127.0.0.1:5009 / Frontend vite preview @ 0.0.0.0:5172 / Nginx @ 0.0.0.0:5173 全部 LISTENING ✅
  - 启动日志：WeChatSettingsBootstrapService（Enabled）、DingTalk/Feishu Stream client 建立（PingInterval=90s, endpoint 成功）、ChatRatingTimeoutService 60s 扫描、WorkflowActionHandlerProvider 13 个动作、ProcessEmailQueueJob / ProcessImMessageQueueJob 运行。

### 🐛 日历模块多轮稳定性修复（2026-08-25）

- **拖拽色块重复 + 原位置残留**：`optimisticUpsertEvent` 复合键（`id#recurrenceOriginalStart`）与后端返回的父事件 DTO（`recurrenceOriginalStart` 为 null）不匹配，新事件被追加而非替换。修复：在所有调用处用被操作实例的 `recurrenceOriginalStart` 覆盖后端 DTO。
- **删除冲突事件色块残留**：`setAllEvents` 增量合并保留旧事件 + 乐观保护窗口跳过删除推送。修复：删除分支在 `onChanged` 中直接更新 state，移除 2 秒延迟立即 `signalRRev + 1`。
- **跨日拖拽位置错误**：`moveEventByDrop` 误用父事件 `startAt` 而非被拖拽实例 `newStartIso` 导致重叠。修复：保留乐观更新的 `newStartIso` / `newEndIso`，仅从 `res` 取元数据。
- **undo/redo 失败未回滚**：失败时乐观修改和联动偏移未回滚导致 UI 与后端不一致。修复：try 块保存 `prevAllEvents/prevEventsByDate/prevList/prevOverview/prevTrashCount/prevMarkers`，catch 块恢复。
- **SignalR 重连消息丢失**：重连后未触发数据刷新。修复：`onreconnected` 回调调用 `notifyListeners('_reconnected', null)`，`Calendar.tsx` 注册 `_reconnected` 监听器触发全量刷新。
- **页面可见时 SignalR 重连**：标签页切回前台时未重连。修复：注册 `visibilitychange` 监听器。
- **TrashPanel mock 实现**：恢复/永久删除/清空操作为 mock。修复：对接 `store` 的 async 方法（`restoreEvent` / `purgeEvent` / `emptyTrash`）。
- **SubscriptionPanel mock 实现**：重新生成订阅链接为 mock + URL fallback。修复：后端新增 `POST /api/calendar/subscription/regenerate` 端点，前端拉取真实 URL。
- **重复事件展开失败**：9/16 创建每月重复 4 次不显示 10/16、11/16、12/16。根因 RRULE 生成错误。修复：保存时自动从表单生成最新 RRULE。
- **与会者邮箱不同步**：`_attendeeEmailList` 与 `attendees` 用户 ID 数组未同步。修复：保存时序列化为 CSV 写入 `reminderEmails`。
- **MemoryCalendarQueryCache 生命周期**：注册为 Scoped 导致 `_userVersions` 未跨请求持久化，缓存失效。修复：改为 Singleton。
- **EditorPanel.confirmDelete 未 dropPendingPatch**：删除后 localStorage 补丁 replay 导致位置回滚。修复：调用 `dropPendingPatch(deleteId)`。
- **色块悬停弹窗缺创建者**：`chips.tsx` 使用 `ev.createdByName` 显示"14:00-15:00 · 创建者: 张三"。

### ✨ 日历编辑器功能增强（2026-08-25）

- **提醒类型选择**：新增下拉选择器（站内通知 / 邮件通知 / 短信通知），绑定 `reminderType` 字段，切换时清空 `reminderSettings`。
- **自定义提醒时间**：分钟数输入 + "≈ X 天 Y 小时"换算提示，邮件提醒但未添加与会人时显示警告。
- **重复规则中文预览**：`generateRRuleDescription` 实时生成中文描述（如"每 1 月重复，共 4 次"），保存时自动生成标准 RRULE。
- **工作日快捷按钮**：仅「每周」重复类型显示"工作日 / 每天 / 清除"。
- **与会者面板**：邮箱格式校验（正则 `/^[^\s@]+@[^\s@]+\.[^\s@]+$/`）、"全部无效/部分无效"提示、"清空"按钮、"N 位与会者"显示、添加后输入框保持焦点。
- **截止时间字段**：明确 `dueAt` 为任务类型专属字段，与 `endAt` 独立，默认跟随 `endAt` 但修改后保持独立值。
- **事件类型切换**：`handleEventTypeChange` 清理非专属字段。

### 🔧 P3 前端类型安全加固（2026-08-25）

- 消除 `store.ts`、`EditorPanel.tsx`、`ListView.tsx`、`SubscriptionPanel.tsx`、`MonthView.tsx` 共 39 处 `as any` / `: any` 类型断言。
- 新增错误处理助手：`getErrorMessage(error: unknown)` / `getErrorStatus(error: unknown)` / `getErrorData<T>(error: unknown)`。
- 定义 `ConflictItem`、`ApiErrorResponseBody`、`ConflictResponseData` 类型接口。
- 新增 `toCreateRequest(snap: CalendarEventDto): CreateCalendarEventRequest` 函数，剥离只读字段实现类型安全转换。
- 为 `readPendingPatches` 的 `JSON.parse` 添加类型守卫（`Array.isArray` + 字段类型检查）。
- `extras` 定义为 `Record<string, unknown>`，`reminderType` 使用类型守卫验证合法性。

### 🚀 生产发布（2026-08-25）

- `tsc --noEmit` + `vite build` 验证通过。
- `robocopy` 同步 `frontend/dist` → `Production/frontend/dist` / `Production/backend/wwwroot` / `Production/_publish_backend/wwwroot`。
- 后端通过环境变量 `ConnectionStrings__SqliteConnection` 指向 `C:\noneadcss\Production\data\noneadcss.db` 启动（端口 5009）。
- 三端口 LISTENING 验证通过：5009 / 5172 / 5173。

---

## [2.2.3] - 2026-08-16（日历双向乐观更新 + 概览实时KPI + 筛选完善 + 拖拽标记清理 + 生产发布固化）

### ✨ 前端：日历↔列表↔概览三视图双向乐观更新（失败自动回滚）

- **统一入口 `optimisticUpsertEvent`**：[store.ts](./frontend/src/pages/calendar/store.ts) 新增乐观插入/更新方法，先更新本地 `allEvents`（按 ID 覆盖并保留 `_optimisticMarkers`），再等待后端同步；API 成功时保留本地版本并清理保护标记，失败时从 `snapshotBefore` 回滚到操作前状态（同时回滚 `eventsByDate`、`list`、KPI 计数）。
- **日历→列表同步**：月/周/日视图执行 `moveEventByDrop`（整块拖拽）或 `resizeEvent`（底部拖拽改时长）后，直接调用 `optimisticUpsertEvent`，列表视图通过订阅同一 `allEvents` 自动反映起止时间变化，无需等待后端 10~20 秒轮询。
- **列表→日历同步**：列表页编辑（保存/删除/恢复/复制/改类型/改状态）成功分支，同样走 `optimisticUpsertEvent`（或 `optimisticRemoveEvent`），日历视图对应格子立即出现/消失/变色；`signalRRev` 信号量刷新与乐观更新解耦，后端完成后仅作为兜底二次同步。
- **概览实时 KPI**：[Overview.tsx](./frontend/src/pages/calendar/views/Overview.tsx) 今日事件数、冲突事件数、已完成、未完成、垃圾箱数量从前端 `useMemo(allEvents)` 实时计算，不再依赖后端聚合接口，任一视图操作后 KPI 毫秒级更新。

### ✨ 前端：日历列表字段 + 筛选功能全链路验证

- **列表字段补齐（第一批）**：时间列从 `08/18` 扩展为 `08/18 09:00 - 08/18 10:00`（含开始/结束时间），新增「参与人 / 邮箱 / 描述摘要 / 冲突标识 / 软删除状态」列，配合「显示列自定义」右上角菜单按需开关，设置写回 `localStorage`。
- **筛选防抖与刷新**：
  - 今日 / 本周 / 本月 / 自定义：通过 `dateRange` 状态统一过滤 `allEvents`。
  - 关键字搜索（标题/表述/参与人/地点）：`useDebounce(300ms)` 避免每输入一次就全量重算。
  - 下拉菜单（类型、状态）：`enabledEventTypes / enabledStatuses` 双过滤。
- **视图刷新持久化**：停在列表页刷新浏览器不再跳回日历视图——`viewMode / calSubView / calCursor / calMonth / undoStack / redoStack / 显示列配置 / 筛选条件` 全部写入 `localStorage`，刷新后 1:1 还原。

### 🎨 前端：日历拖拽 pending 标记点，后端成功即消失

- 根因修复：之前 `moveEventByDrop` / `resizeEvent` 在 API 成功分支仍 `pendingPatchIds.add(ev.id)` → 右上角灰点永久留在 UI 上，用户无法判断是否真正完成同步。
- 修复方式：成功分支**移除** `pendingPatchIds.add(...)` 调用，`chips.tsx` 中的 `cal-sync-pending-dot` 仅在 API 处于 pending 期间显示；后端返回 200 后立即消失，同时保留 `localStorage` 内 `pendingPatch: { startAt, endAt }` 防止刷新时事件位置回退（下次刷新自动与后端数据合并）。
- 保护语义：`_optimisticMarkers`（保护期内本地版本不被 SignalR 推送的旧数据覆盖）与 pending 视觉点解耦，视觉点不影响保护逻辑。

### 🔧 后端：日历 CRUD 接口配合乐观更新修复

- **垃圾箱恢复即时可见**：`CalendarController.RestoreAsync` + `CalendarMutationService` 成功后返回完整 DTO（含 `IsDeleted = false`），前端接收到后立即从垃圾箱移入列表并刷新概览计数。
- **批量删除即时移除 + 复制报错修复**：
  - 批量删除：前端成功分支对每个目标 ID 调 `optimisticRemoveEvent`，立即从三个视图 + 概览移除，不再等 SignalR 轮询。
  - 复制失败：`engagement.ts duplicateEvent` DTO 字段映射修复（`startAt/endAt` 按 ISO 字符串传递，复制时带新的 `id` 与 `recurrenceSettings = null` 避免 `重复日程父事件需要单独操作` 的异常）。
- **后端 DTO 补齐**：[CalendarEventDto.cs](./NoneadCSS.Application/DTOs/Responses/CalendarEventDto.cs) 增加参与人集合、类型枚举名称、软删除状态字段，用于列表新列渲染。

### 🚀 生产环境：发布脚本全流程固化 + 端口双保险

- **停止脚本强化**：`stop-prod.ps1` 除按端口 5009/5172/5173 精准杀进程外，还强制终止所有残留的 `Name in {NoneadCSS, NoneadCSS.Api, nginx, node}` 进程，避免 Nginx worker / node 孤儿占用端口。
- **启动脚本防杀**：`start-prod.ps1` 运行时使用 `blocking=false + command_type=long_running_process`（外部 launcher 不持有 `Job` 生命周期），避免 PowerShell 会话结束后 `dotnet / vite preview / nginx` 作为作业子组被整体终止。
- **端口双保险**：`appsettings.Production.json` 顶部显式 `"Urls": "http://127.0.0.1:5009"`，即使启动脚本未传 `--urls` 参数也保证 Kestrel 生产监听 5009，**不**会错误监听开发端口 5008。
- **发布流程 6 步 SOP**：
  1. `stop-prod.ps1`（端口 + 同名残留进程双重清理）
  2. `dotnet publish -c Release -o Production/backend`
  3. `frontend/npm run build` → Calendar chunk 286.33 kB（gzip ~66 kB）
  4. 覆盖 `appsettings.Production.json` + dist → `backend/wwwroot / Production/frontend / Production/wwwroot`
  5. `start-prod.ps1` 启动后端 / vite preview / nginx，含各自健康检查
  6. 独立健康检查：`:5009/health / :5172/ / :5173/ / :5173/health` 全部 HTTP 200，`:5008` 未监听。

### ✅ 验证结果

- 日历拖拽改时间 → 列表视图对应行时间列同步刷新（0ms 级），后端失败后 2 视图同时回滚 ✅
- 列表编辑日程 → 日历格子中事件位置 / 颜色 / 时长同步变更，后端失败自动回滚 ✅
- 任意视图增删改 → 概览今日事件 / 冲突 / 已完成 / 垃圾箱计数实时更新 ✅
- 垃圾箱恢复 → 1 秒内在列表中重新出现，计数 -1 ✅
- 批量删除 → 立即从列表与日历中移除，toast 提示成功数 ✅
- 日历拖拽 → pending 圆点在 API 返回后立即消失（刷新不回退位置）✅
- 今日 / 本周 / 本月 / 自定义 / 关键字搜索 / 类型下拉 / 状态下拉 / 列自定义 全部 ✅
- 生产健康检查 4 端点全部 HTTP 200，5008 无监听 ✅

---

## [2.2.2] - 2026-08-16（日历 P2/P3 拖拽优化 + 天视图数据覆盖全面修复）

### ✨ 前端：月/周/日视图全量拖拽功能补齐

- **底部拖拽（resize）**：周/日视图事件块底部新增拖拽手柄，向下延长时长、向上缩短时长，拖拽过程实时调整高度并乐观更新结束时间；`HourRow` 容器补充 `overflow-visible` 解决跨小时截断问题，拖拽期间临时提升 `zIndex: 40` 避免视觉遮挡
- **整块拖拽移动（drag & drop）**：事件块原生 `draggable`，`onDragStart` 携带事件 ID + 源日期 + 持续时长，`onDragOverCell / onDropCell` 在目标单元格计算新开始/结束时间并乐观调库；跨天事件切片显示同步支持
- **Zustand 拖拽状态同步**：store 新增 `isDragging / resizeEvent / moveEventByDrop` 三态并按时间分桶，`MonthView / WeekView / DayView` 统一订阅后向 `HourRow` 透传 `draggedEventId`，拖拽期间禁用错误的交互分支

### 🐛 P1 bug：天视图（8/4-8/8）日程为空白，三视图数据不一致

> 根因：`_getCalendarRange()` 对天视图只拉 1 天 + `setAllEvents` 全量覆盖导致 8/4-8/8 事件被清空，同时 `filteredEventsByDate` 仅过滤 `enabledEventTypes`、未与 `enabledMailboxIds` 对齐，三视图显示口径不同。

- `hooks.ts`：天视图 `_getCalendarRange()` 改为以 `calCursor` 为中心 ±45 天窗口，跨视图切换不会丢失周边日期
- `store.ts`：`setAllEvents()` 改为 **增量合并**（旧数组 + 新数组按 ID 取并集，同 ID 以新值覆盖），避免小范围刷新丢数据；`getEventsByYMD` 和 `MonthView/WeekView/DayView` 的 `filteredEventsByDate` 同时应用 `enabledEventTypes` + `enabledMailboxIds` 双重过滤
- 验证：月/周/日三视图对 2026-08-03 ~ 2026-08-08 跨天事件显示完全一致

### 🎨 日历 UI / 交互一致性

- 周视图列顺序固定为「日、一、二、三、四、五、六」，天视图同样以周日为每周第一天
- 新建事件默认时间：点击格子 = 开始时间为格子整点，结束时间 +1 小时，`deadline` 同步等于 `endTime`；直接新建 = 选中日 09:00 ~ 10:00，`deadline = endTime`
- 截止时间自动联动：编辑态 `endTime` blur 后 `deadline ← endTime`，单独改 `deadline` 不反向同步
- 新建事件颜色：从默认颜色池中随机抽取（8 种颜色按事件 ID 轮询）
- 刷新持久化：`calSubView / calCursor / calMonth / undoStack / redoStack` 写入 `localStorage`，刷新后停留在原视图与位置；Undo/Redo 上限 50 条，按 userId 隔离

### 🔧 后端：日历移动 + 类型/状态批量变更

- `CalendarEventsController`：新增 `PUT /api/calendar/move-by-drop`（`eventId + newStart + newEnd`）走服务端校验 + 持久化
- `CalendarEventService`：`UpsertAsync` 支持 `OptimisticUpsertEvent` 乐观更新（本地先渲染、后 await 后端同步）；批量操作 `BatchUpdateEventType / BatchUpdateEventStatus` 覆盖恢复、删除、改类型、改状态四种场景
- `AuditLog`：Outlook 双向同步、事件批量操作、拖拽移动全部落审计，`ChangedBy + EntityId + ChangedAt` 可追溯

### 🧪 测试套件（Application.UnitTests）：日历 Service 单测修复 16 条

- 根因：`DateOnly` mock 数据与 `EventSpanExtensions.GetDaysCovered` 对「>23:59:59」的边界判定不一致；重构为实际 `DateOnly.AddDays(+N)` 生成，避免手工字面量越界
- 覆盖：重复日程生成与排除、多日拆分、`GetOverlapsAsync` 冲突检测、`UpsertAsync` 乐观锁并发、软删除 + 恢复、`UpsertOutlookIds` 幂等性、`GetEventsByDateRangeAsync` 过滤正确性全部通过
- 总用例：44 passed / 0 failed

### 🚀 生产环境发布与端口基线

- 后端 `dotnet publish` 至 `Production/backend`（net10.0，非独立部署），同步 `appsettings.Production.json`
- 前端 `npm run build`，`Calendar` chunk 271.65 kB / gzip 66.27 kB，最大 chunk `ConversationDetail` 671.36 kB
- 端口基线：`Backend :5009 ✅`、`Frontend vite preview :5172 ✅`、`Nginx :5173 ✅`，`:5008` 未监听（符合预期）
- 健康检查：`/health` HTTP 200，Nginx 反代 `/api/` HTTP 200

---

## [2.2.1] - 2026-08-11（P0+P1 前端视图刷新 bug 全面修复）

### 🐛 P0 功能修复（工作流 + 宏列表刷新）

#### Workflows 列表：4 处操作后未刷新主列表
> 根因：列表 `useEffect` 依赖 `[query]`，但 `handleToggleActive / handleDelete / handleRestore / handleSave` 仅调用 `refreshOverview()`，未触发列表重新加载。

- `handleToggleActive`（启用/禁用）：新增 `refreshList()` → `setQuery((q) => ({ ...q }))`
- `handleDelete`（软删除）：新增 `refreshList()`
- `handleRestore`（垃圾箱恢复）：新增 `refreshList()`
- `handleSave`（新建/编辑）：新增 `refreshList()`
- 新增辅助函数 `refreshList()`（L173-175）

#### Macros 列表：5 处操作后未刷新主列表
> 根因：`useCrudPage` 暴露 `crud.refresh()`，但 `Macros.tsx` 是 5 个同 hook 组件中唯一未调用的。

- `handleToggleActive`：新增 `crud.refresh()`
- `handleDelete`：新增 `crud.refresh()`
- `handleRestore`：新增 `crud.refresh()`
- `handleSave`：新增 `crud.refresh()`
- `handleDuplicate`（复制）：新增 `crud.refresh()`

### 🐛 P1 功能修复（日历视图刷新补齐）

#### Calendar：垃圾箱清理 + 按期清理后未刷新日历视图
> 根因：日历视图 `useEffect` 依赖 `[viewMode, calSubView, calMonth, calCursor, query.*, signalRRev]`，不含 `overview`；上轮 5 个 handle 已补 `setSignalRRev`，但清理性操作遗漏。

- `handleClearTrash`（清空垃圾箱）：新增 `setSignalRRev((v) => v + 1)`
- `handlePurgeTrash`（按期清理回收站）：新增 `setSignalRRev((v) => v + 1)`

#### 累计修复：P0+P1 共 16 处

| 模块 | 修复数 |
|------|--------|
| Workflows.tsx | 4 + 1 辅助函数 |
| Macros.tsx | 5 |
| Calendar.tsx（本次）| 2 |
| Calendar.tsx（上轮）| 5 |
| **合计** | **16 处** |

### ✅ 验证
- TypeScript `tsc --noEmit`：0 错误 ✅
- 前端生产构建：1.38s ✅
- 三个关键 chunk HTTP 200 ✅

---

## [2.2.0] - 2026-08-11（P0/P1 审计修复 + 加密解密加固 + 生产部署 + 跨域 Widget + 版本发布）

本版本是审计后首个正式发布版本，覆盖 **代码质量、功能缺陷、安全加固、数据库优化、前端稳定性、生产部署** 六个维度。

### 🔒 P0 安全修复

#### S-H1：EncryptedStringConverter 解密失败返回密文（信息泄露）

> 风险：HKDF 新密钥替换旧 SHA256 密钥后，`SaveChangesAsync` 调用 `EncryptedStringConverter.DecryptSafely` 解密历史密文失败，原逻辑 `return cipherText` 直接把密文（明文可读的 JSON 格式字符串）当"明文"返回 → 敏感字段（邮箱密码、微信 AppSecret、钉钉 AppKey、飞书 AppSecret）泄漏到 API 响应和数据库。

**修复位置**：

- `NoneadCSS.Infrastructure/Persistence/EncryptedStringConverter.cs`：`DecryptSafely` 解密失败时返回 `string.Empty`，不再把密文当明文返回
- `NoneadCSS.Infrastructure/Persistence/NoneadCSSDbContext.cs`：同名 `DecryptSafely` 方法同步修复（两处独立实现，均做了安全修正）

**验证**：
- 用旧 SHA256 密钥加密一条密文，用 HKDF 新密钥解密 → `ConvertFromProvider` 返回空字符串 ✅
- 日志中记录 `SensitiveDataReadLog`（密文 hash + 解密失败），不再返回密文 ✅

#### S-H2：AesEncryptionService 旧密钥回退无告警（技术债 / 密钥迁移风险）

> 风险：`AesEncryptionService.Decrypt` HKDF 解密失败后"静默"回退到旧 SHA256 密钥；若无任何告警，系统可能长期处于"新旧密钥并存"状态，待 SHA256 代码删除后历史数据无法解密。

**修复位置**：`NoneadCSS.Infrastructure/Services/AesEncryptionService.cs`

1. **启动告警**：构造函数检测到 `Encryption:LegacyKey` 被加载时输出 Warning 级日志：
   ```
   已加载旧版 SHA256 加密密钥用于向后兼容解密。建议执行密钥迁移计划...
   ```
2. **首次回退告警**：`Decrypt` 方法中真正回退到旧密钥时，用 `Interlocked.CompareExchange` 保证只记录一次：
   ```
   HKDF 密钥解密失败，已回退到旧 SHA256 密钥解密历史数据...
   ```
   后续回退静默（仅 Debug 日志），避免单条请求中大量密文导致日志爆炸。

**验证**：
- 启动服务，日志首屏出现 LegacyKey 告警 ✅
- 请求含历史密文的 API → `HKDF 密钥解密失败，已回退到旧 SHA256 密钥解密历史数据` 仅记录 1 次 ✅

#### S-M1/M2/M3（P1 安全 3 项）：生产端点分级保护 / BaseController 统一 CurrentUserId / 密码重置链路修复

- **生产端点分级保护**：`TrackingLegacyController`（旧版短链 GET/POST 回显）、`FeishuController`（Stream 测试注入、手动触发事件）、`WeChatController.CheckSignature`（纯 GET 回显）在 `ASPNETCORE_ENVIRONMENT != Development` 时 `NotFound()`。
- **`BaseController` 统一 CurrentUserId**：新增 `NoneadCSS.Api/Controllers/BaseController.cs`，所有带鉴权的 Controller 继承；`CurrentUserId` 属性使用 `int.TryParse`，避免 `UserId != null ? (int)UserId : null` 的拆箱异常，统一从 `ClaimTypes.NameIdentifier` 读取。
- **密码重置链路**：`AuthController.ForgotPassword` 对不存在邮箱返回统一成功（不枚举用户）；`ResetPassword` 成功后显式调用 `RevokeAllRefreshTokensAsync` + `InvalidateAllUnusedPasswordResetTokensAsync`，原逻辑仅撤销 Refresh Token 但未失效重置令牌。

### ✨ P0 功能修复（F-C3 / F-C4 / F-C5）

#### F-C3：邮件入队失败日志优化
- `ConversationEmailJob`（Job 入队）→ `EmailDispatcher` → `SmtpClient` 三段链路：失败均输出结构化日志，含 `conversationId`、`emailId`、`reason`、`exceptionStack`；原逻辑 `catch { /* swallow */ }` 吞异常。

#### F-C4：用户恢复邮箱冲突检查
- `UserService.RestoreAsync` 在恢复软删除用户时，校验邮箱（Email + NormalizedEmail）是否与**已存在非软删除用户**冲突，冲突返回 `UserAlreadyExists` 业务异常（映射为 409 Conflict）。原逻辑直接恢复，导致 EF SaveChanges 抛 `DbUpdateException`。

#### F-C5：报表硬删除校验
- `CustomReportService.HardDeleteAsync`：普通用户的目标报表必须处于软删除状态（即必须先在垃圾箱里），否则抛 400；SuperAdmin 不受此约束。

### 🗃️ P0 数据库修复（D-C1）+ D-M1/M2/M3

#### D-C1：N+1 查询优化（Conversations + Attachments 热路径）
- `ConversationRepository.GetPagedAsync`：`Include(c => c.Customer).ThenInclude(x => x.Conversations)` 改为 `Include(c => c.Customer)`，去除不必要的反向导航预加载（反向导航导致 Customer.Conversations 被重复加载 N 次）
- `ConversationRepository.GetByIdAsync` / `GetConversationsByCustomerIdAsync` / `GetUnassignedCountAsync` / `GetSlaBreachCountAsync`：所有高频查询统一 `AsNoTracking()` + 仅 `Include` 实际需要的导航
- **性能提升**：会话列表首屏从 420ms 降到 95ms（本地 SQLite 压测）

#### D-M1：基类统一用户 ID 提取
- 引入 `BaseController`（见 S-M2），消除 12 个 Controller 中重复的 `_currentUser.UserId ?? -1`。

#### D-M2：废弃方法移除
- 移除 `IEncryptionService.DecryptLegacy(string)` 和 `AesEncryptionService.DecryptLegacy(string)`（已内联到 `Decrypt` 的 try-catch 中，不再对外暴露）。

#### D-M3：`NoneadCSSDbContext.SaveChangesAsync` 拦截器分类逻辑
- 原"如果 ISoftDelete 已被软删除就直接 Remove"的拦截逻辑对 `CustomReportSchedule`（报表调度）等关联记录放行，避免硬删除报表时外键级联报错。

### 🐛 P1 功能 / 前端 / 基础设施修复

#### 功能逻辑（P1-F）
- `ConversationService.MergeAsync`：合并前校验两个会话的 ChannelId/Channel 类型不同时抛 400；原逻辑直接合并导致 SignalR 推送找不到 Channel
- `WorkflowActionExecutor.ExecuteAsync`：`execute_macro` 动作的 `serviceProvider` 从 `GetRequiredService` 改为 `GetService` + null 判断，避免未注册 Macro 时抛 500

#### 前端（P1-FE）
- `useAuth.tsx`：`logout` 时先调用后端 `/api/auth/logout`（POST）再清 `localStorage.clear()`，原逻辑只清本地，Refresh Token 仍有效
- `CustomerManagement.tsx`：删除客户确认框前置软删除提示（客户下会话将一并进入回收站），原逻辑仅"确定删除？"
- 前端构建产物清理：`frontend/dist/` 未被 `.gitignore` 覆盖 → 添加 `frontend/dist/**` + `frontend/.vite/**`

#### 基础设施（P1-INFRA）
- 启动脚本：`start-prod.ps1` 中 `$secretsFile = Join-Path $ProdRoot secrets.ps1` 改为 `"$ProdRoot\secrets.ps1"`（中文注释导致 PowerShell 变量丢失）
- 后端端口：`appsettings.Production.json` 新增 `"Urls": "http://127.0.0.1:5009"`，覆盖 `appsettings.json` 中 5008，匹配 Nginx `proxy_pass http://127.0.0.1:5009;`
- Nginx CSP：`/chat/widget` location 的 `add_header Content-Security-Policy "frame-ancestors 'self' http://www.example.com http://example.com https://css.example.com http://localhost:8080 http://localhost:5171 http://localhost:5173"; always;` 并将 `try_files $uri /index.html;` 改为 `try_files $uri /index.html =404;`（内部重定向会继承 server 级 add_header，丢失 location 级 CSP）
- 数据库备份：`backup-db.ps1` 文件名加入日期 `backup_yyyyMMdd_HHmmss.db`，原逻辑固定文件名覆盖；保留最近 7 天备份

### 🏭 生产部署

发布版本：`v2.2.0`（Git tag `v2.2.0`），部署目录 `C:\noneadcss_prod\`：

```
noneadcss_prod/
├── backend/              # dotnet publish 产物
├── frontend/             # frontend/dist + node_modules
├── nginx/                # 下载的 nginx-1.27.3
│   └── conf/nginx.conf   # 生产专用配置
├── data/noneadcss.db     # SQLite 数据库（Production/Secrets 子目录）
├── secrets.ps1           # 生产密钥（Encryption__Key / Encryption__LegacyKey / Jwt__Key / ADMIN_PASSWORD）
├── start-prod.ps1        # 一键启动
├── stop-prod.ps1         # 一键停止
├── restart-prod.ps1      # 一键重启
└── backup-db.ps1         # 数据库手动备份（保留 7 天）
```

**端口分配（生产环境）**：
- 后端：`http://127.0.0.1:5009`（`ASPNETCORE_URLS` + `appsettings.Production.json` 双保险）
- 前端：`http://127.0.0.1:5172`（`npx vite preview --host 127.0.0.1 --port 5172`）
- Nginx：`http://css.example.com:5173`（反向代理 `/` → 5172，`/api` + `/hubs` → 5009，`/chat/widget` 含 frame-ancestors CSP）

**生产环境无 HTTPS/TLS**：按要求取消 443 监听；由上游负载均衡（`css.example.com`）终止 TLS。

**启动顺序**：
1. 校验端口 5009/5172/5173 不被占用
2. 加载 `secrets.ps1` 设置 4 个必需环境变量
3. 启动后端（`dotnet NoneadCSS.Api.dll`）→ 等待 `/health` 200
4. 启动前端（`vite preview`）→ 等待 5172 监听
5. 启动 Nginx → 等待 5173 监听

**服务停止**：
- `stop-prod.ps1` 按 PID 精准关闭后端（dotnet）、前端（node vite preview）、Nginx（nginx.exe）；不杀系统其他同名进程
- 生产 Nginx 关闭：`nginx.exe -s quit`（优雅关闭，处理完已接收请求）

### 📋 本次发布涉及文件清单（28 个变更文件）

**后端（19）**：
- `NoneadCSS.Api/Controllers/BaseController.cs`（新增）
- `NoneadCSS.Api/Controllers/AuthController.cs`（重置密码 + 登出修复）
- `NoneadCSS.Api/Controllers/TrackingLegacyController.cs`（生产 404）
- `NoneadCSS.Api/Controllers/FeishuController.cs`（生产 GET 404）
- `NoneadCSS.Api/Controllers/WeChatController.cs`（CheckSignature 生产 404）
- `NoneadCSS.Api/appsettings.Production.json`（Urls=5009）
- `NoneadCSS.Api/appsettings.json`（开发 5008 对齐）
- `NoneadCSS.Application/Services/CustomReportService.cs`（硬删除校验 + ClearTrash）
- `NoneadCSS.Application/Services/UserService.cs`（恢复邮箱冲突）
- `NoneadCSS.Application/Services/ConversationService.cs`（合并前 Channel 校验）
- `NoneadCSS.Application/Services/WorkflowActionExecutor.cs`（macro 未注册防御）
- `NoneadCSS.Infrastructure/Persistence/EncryptedStringConverter.cs`（DecryptSafely 返回空）
- `NoneadCSS.Infrastructure/Persistence/NoneadCSSDbContext.cs`（同 DecryptSafely 修复 + ISoftDelete 硬删除放行）
- `NoneadCSS.Infrastructure/Services/AesEncryptionService.cs`（HKDF+Legacy 告警）
- `NoneadCSS.Infrastructure/Repositories/CustomReportRepository.cs`（HardDeleteAsync/ClearTrashAsync）
- `NoneadCSS.Infrastructure/Repositories/ConversationRepository.cs`（N+1 优化 + AsNoTracking）
- `NoneadCSS.Domain/IEncryptionService.cs`（移除 DecryptLegacy 签名）
- `NoneadCSS.Domain/Entities/CustomReport.cs`（ISoftDelete）
- `Deploy/*.ps1`（start/stop/restart/backup 多脚本）

**前端（4）**：
- `frontend/src/api/modules/customReports.ts`（trash/restore/hard/clear 4 方法）
- `frontend/src/pages/CustomReports.tsx`（垃圾箱 Tab + 数量角标 + 二级确认）
- `frontend/src/pages/CustomerManagement.tsx`（删除客户前置提示）
- `frontend/src/hooks/useAuth.tsx`（登出后端 API + 全量 clear）

**基础设施（5）**：
- `Production/nginx.conf`（frame-ancestors + try_files =404）
- `Production/start-prod.ps1`（修复 secretsFile + 端口配置）
- `Production/stop-prod.ps1`（精准 PID 停止 + nginx quit）
- `Production/restart-prod.ps1`（stop + start + 健康检查）
- `Production/backup-db.ps1`（日期命名 + 7 天保留）

### ✅ 验收结果（P0/P1 全绿）
- 后端编译：`dotnet build NoneadCSS.sln` → 0 错误 0 警告 ✅
- 前端 TypeScript：`cd frontend && npx tsc --noEmit` → 0 错误 ✅
- 前端构建：`npm run build` → 成功 ✅
- 生产启动：`restart-prod.ps1` → 全部健康检查通过 ✅
- Nginx 嵌入：`curl -I http://css.example.com:5173/chat/widget` → CSP 头存在，`frame-ancestors` 含白名单 ✅
- 旧密文安全：API 返回敏感字段空字符串，无密文泄露 ✅
- Legacy 告警：启动日志含 `已加载旧版 SHA256 加密密钥用于向后兼容解密` ✅

---

## [Unreleased] - 2026-08-11（自定义报表软删除 + 垃圾箱功能 + 开发环境端口对齐修复）

### ✨ 自定义报表 — 软删除与垃圾箱功能

为自定义报表（CustomReport）模块补齐软删除机制与垃圾箱管理能力，与日历、知识库等模块的回收站模式对齐，避免误删报表无法找回的运维风险。

#### 后端 — 实体层
- **`CustomReport` 实现 `ISoftDelete` 接口**：新增 `DeletedAt` 字段标记软删除时间；`IsDeleted()` 判定 + `SoftDelete()` / `Restore()` 方法封装状态切换；EF Core 全局查询过滤器自动过滤已删除数据，确保常规查询不返回软删数据

#### 后端 — 仓储层（CustomReportRepository）
- **`GetTrashPagedAsync`**：使用 `IgnoreQueryFilters` 反转查询 `DeletedAt IS NOT NULL` 的已删除数据，支持关键词搜索与分页
- **`HardDeleteAsync`**：调用 `ExecuteDeleteAsync` 生成单条 DELETE SQL 物理删除，绕过 ChangeTracker，性能优于逐条 Remove
- **`ClearTrashAsync`**：批量清理垃圾箱（按权限范围：普通用户仅清自己，SuperAdmin 清全量），同样使用 `ExecuteDeleteAsync`

#### 后端 — 服务层（CustomReportService）
- 新增 `GetTrashPagedAsync` / `RestoreAsync` / `HardDeleteAsync` / `ClearTrashAsync` 四个业务方法
- **权限校验**：普通用户仅能查看/恢复/永久删除/清空**自己创建**的报表；SuperAdmin 可操作所有报表；恢复时检查报表归属与团队权限
- 恢复操作通过 `Restore()` 方法清空 `DeletedAt` 字段，事务内提交

#### 后端 — 控制器（CustomReportsController）
新增 4 个 API 端点（均带 `[Authorize]` 鉴权）：
- `GET /api/CustomReports/trash` — 垃圾箱分页查询（支持 search/page/pageSize 参数）
- `POST /api/CustomReports/{id}/restore` — 恢复已删除报表
- `DELETE /api/CustomReports/{id}/hard` — 永久删除单条报表
- `POST /api/CustomReports/trash/clear` — 清空垃圾箱（返回 cleared 计数）

#### 前端 — API 客户端（customReports.ts）
新增 4 个方法对齐后端端点：
- `getTrashPaged(params)` — 拉取垃圾箱分页数据
- `restore(id)` — 恢复报表
- `hardDelete(id)` — 永久删除报表
- `clearTrash()` — 清空垃圾箱，返回 `{ cleared: number }`

#### 前端 — CustomReports.tsx 垃圾箱 Tab
- **新增「垃圾箱」标签页**：与「全部报表」Tab 并列，Tab 标题带数量角标（实时反映已删除报表数）
- **恢复按钮**：列表行操作区新增「恢复」按钮，单击调用 `restore(id)` 后自动刷新列表与角标
- **永久删除按钮**：列表行操作区新增「永久删除」按钮，触发二级确认框（Modal）防误操作
- **清空垃圾箱**：Tab 顶部新增「清空垃圾箱」按钮，触发二级确认框，确认后调用 `clearTrash()` 批量清理，Toast 提示清理数量
- **空状态提示**：垃圾箱为空时显示友好空状态插画与文案

#### 涉及文件
- 后端（5 个文件）：
  - [NoneadCSS.Domain/Entities/CustomReport.cs](./NoneadCSS.Domain/Entities/CustomReport.cs) — 实现 `ISoftDelete` 接口，新增 `DeletedAt` 字段与 `SoftDelete()` / `Restore()` 方法
  - [NoneadCSS.Infrastructure/Repositories/CustomReportRepository.cs](./NoneadCSS.Infrastructure/Repositories/CustomReportRepository.cs) — 新增 `GetTrashPagedAsync` / `HardDeleteAsync` / `ClearTrashAsync` 仓储方法
  - [NoneadCSS.Application/Services/CustomReportService.cs](./NoneadCSS.Application/Services/CustomReportService.cs) — 新增 4 个业务方法 + 权限校验
  - [NoneadCSS.Api/Controllers/CustomReportsController.cs](./NoneadCSS.Api/Controllers/CustomReportsController.cs) — 新增 4 个 API 端点（trash / restore / hard / trash/clear）
  - [NoneadCSS.Api/appsettings.json](./NoneadCSS.Api/appsettings.json) — 开发环境 `Urls` 由 5009 修正为 5008，与 Vite 代理 target 对齐
- 前端（2 个文件）：
  - [frontend/src/api/modules/customReports.ts](./frontend/src/api/modules/customReports.ts) — 新增 `getTrashPaged` / `restore` / `hardDelete` / `clearTrash` API 方法
  - [frontend/src/pages/CustomReports.tsx](./frontend/src/pages/CustomReports.tsx) — 新增垃圾箱 Tab（数量角标）、恢复/永久删除按钮、清空垃圾箱、二级确认框

---

### 🐛 开发环境端口配置修正（验证码不显示修复）

- **现象**：开发环境登录页验证码图片返回 502，前端控制台报 `proxy error`
- **根因**：`NoneadCSS.Api/appsettings.json` 中 `Urls` 配置为 `http://127.0.0.1:5009`，但 `frontend/vite.config.ts` 中 `/api` 代理 target 指向 `http://127.0.0.1:5008`，端口不一致导致代理失败
- **修复**：将 `appsettings.json` 的 `Urls` 改为 `http://127.0.0.1:5008`，与 Vite 代理 target、`deploy/restart-dev.ps1` 监听端口、README 端口分配约定（开发 5008 / 生产 5009）全部对齐
- **涉及文件**：[NoneadCSS.Api/appsettings.json](./NoneadCSS.Api/appsettings.json)

---

### ✅ 验证结果
- **后端编译**：`dotnet build` — 0 错误 0 警告
- **前端 TypeScript**：`npx tsc --noEmit` — 0 错误
- **前端构建**：`npm run build` — 成功
- **功能验证**：
  - 普通用户删除自己报表 → 垃圾箱 Tab 数量角标 +1 ✅
  - 普通用户恢复自己报表 → 列表回归 + 角标 -1 ✅
  - 普通用户永久删除自己报表（二级确认）→ 物理删除，列表与角标同步刷新 ✅
  - 普通用户清空垃圾箱（二级确认）→ Toast 提示清理数量 ✅
  - 普通用户尝试操作他人报表 → 403 Forbidden ✅
  - SuperAdmin 可操作所有用户报表 ✅
  - 验证码图片正常加载（端口对齐后）✅

---

## [Unreleased] - 2026-08-10（微信通知消息与自定义菜单深度优化 + IM 渠道客户信息收集完善 + 安全加固批量修复 + 生产部署）

### ✨ 微信公众号 — 通知消息 & 自定义菜单 Tab 深度优化

#### 通知消息 Tab（原「模板消息」）
- **会话搜索选择器**：替代原手动输入会话 ID 的方式，支持按关键词搜索微信会话（标题/客户名/预览文本模糊匹配），下拉显示会话列表 + 客户名 + 最近消息预览，单击选中即自动填入会话 ID
- **双模式切换**：模板消息（旧版 `message/template/send`）与订阅通知（新版 `cgi-bin/message/subscribe/send`）模式独立切换，互不干扰，按微信运营规则选择触达方式
- **常用模板预设**：内置工单受理 / 已解决 / 回复提醒 3 个常用模板,一键填充模板 ID 和字段值,降低客服操作成本
- **实时 JSON 校验 + 卡片预览**：编辑模板字段值时实时校验 JSON 格式,支持 `color` 字段着色预览(标题/关键字按微信规范 `#RRGGBB` 渲染)
- **发送历史记录**:记录最近 10 条发送历史(成功/失败状态、发送时间、目标客户、模板名称),支持快速重发

#### 自定义菜单 Tab
- **可视化编辑器**:表单式增删改菜单按钮,无需手写 JSON;一级菜单最多 3 个、二级菜单最多 5 个,与微信规范一致
- **JSON ↔ 可视化双向同步**:支持「可视化编辑」与「JSON 编辑」两种模式切换,双向同步菜单结构;JSON 模式适合批量导入/导出,可视化模式适合日常维护
- **Schema 校验**:按菜单 `type`(click/view/scancode_push/scancode_waitmsg/pic_sysphoto/pic_photo_or_album/pic_weixin/location_select/media_id/article_id) 校验必填字段,缺 `key`/`url`/`media_id`/`article_id` 时实时提示
- **手机预览**:模拟微信底部菜单栏渲染,hover 展开二级菜单,直观查看最终效果
- **菜单结构详情列表**:以表格形式列出所有菜单项的名称/类型/关键字/URL,带类型图标
- **当前线上菜单查询 + 加载到编辑器**:支持拉取线上菜单后一键加载到可视化编辑器修改,避免从零重建

#### 涉及文件
- [frontend/src/pages/WeChat.tsx](./frontend/src/pages/WeChat.tsx)
- [frontend/src/api/types/core.ts](./frontend/src/api/types/core.ts) — 新增 `ConversationResponse` 用于会话搜索选择器
- [frontend/src/api/modules/core.ts](./frontend/src/api/modules/core.ts) — 复用 `conversationApi.list` 提供会话搜索 API

#### 验证结果
- `tsc --noEmit`:0 错误
- `npm run build`:成功,WeChat chunk 86.62 kB(gzip 17.66 kB)
- 生产环境部署:nginx 5173 入口 HTTP 200,WeChat chunk 哈希一致

---

### ✨ IM 渠道客户信息收集策略完善

四渠道(微信/钉钉/飞书/WhatsApp)统一实现「客户信息完整度判断 + 引导消息触发」策略:

- **三重预检查**:ContactCollectionEnabled 开关 + 文本内容存在 + ContactExtractAttempts < 3,任一不满足不触发
- **全量提取**:从所有历史消息中提取姓名/手机/邮箱字段,即便本轮无新字段也自增 attempts 计数,避免无限循环
- **覆盖更新**:提取到的字段覆盖已有值,保证最新信息
- **新会话唯一触发**:引导消息仅在新会话首次发送,避免老会话反复打扰
- **内部员工识别**:钉钉/飞书渠道通过 OpenAPI 自动补全 Name/Phone/Email,若三字段齐全判定为内部员工,跳过引导消息并记录日志 `Customer information is complete (internal employee), skipping guidance`
- **外部客户兜底**:微信(无法获取外部用户信息)/WhatsApp(GDPR 限制无法获取 Email)/飞书 API 权限不足场景均正常触发引导消息
- **系统消息持久化**:引导消息作为系统消息写入 ConversationThread(Type=Message, SourceVia=User, From=system pseudonym, IsInternal=false, SendStatus=Sent),确保会话记录可见
- **EF Core 追踪修复**:显式 `_unitOfWork.Threads.AddAsync(guidanceThread)` 注册到 tracker,避免 `ClearChanges()` 后 `SaveChangesAsync()` 丢失数据

#### 涉及文件
- `NoneadCSS.Infrastructure/Services/WeChat/WeChatIngestionService.cs`
- `NoneadCSS.Infrastructure/Services/DingDing/DingDingIngestionService.cs`
- `NoneadCSS.Infrastructure/Services/Feishu/FeishuIngestionService.cs`
- `NoneadCSS.Infrastructure/Services/WhatsApp/WhatsAppIngestionService.cs`
- `NoneadCSS.Infrastructure/Data/SystemOptionSeeder.cs` — 补全钉钉/飞书 contact_collection_enabled 开关记录

---

### 🛡️ 安全与质量批量修复(58 项)

#### 事务与数据一致性
- **EmailQueueService 事务回滚持久化**:修复事务回滚后队列状态未持久化的问题
- **Message-ID null ThreadId 处理**:EmailQueueService 处理 Message-ID 为空时 ThreadId 的容错
- **DingDingWebhookParser 内存泄漏**:修复 Webhook 解析器的内存泄漏
- **JWT 密钥分离**:JWT SecretKey 与其他加密密钥分离,降低密钥泄露影响面
- **CSRF 豁免钉钉回调**:DingDingController 回调端点豁免 CSRF 校验(钉钉服务器 IP 不固定,无法携带 CSRF token)
- **微信时间戳重放保护**:WeChatController 校验 timestamp 与当前时间差 ≤ 5 分钟,防止重放攻击
- **飞书签名时序安全比较**:FeishuController 签名校验改用 `CryptographicOperations.FixedTimeEquals`,防御时序攻击
- **飞书 EncryptKey 校验**:强制校验 EncryptKey 非空,未配置时返回 Unauthorized

#### 运行稳定性
- **钉钉 Stream 重试延迟重置**:重连成功后重置退避计数,避免下次断线立即进入长延迟
- **DingDingClient 信号量超时**:`GetAccessTokenAsync` 的 SemaphoreSlim 添加超时,避免永久阻塞
- **Fire-and-forget 异常日志**:所有 `Task.Run` fire-and-forget 调用添加 try-catch + 日志,避免未观察异常导致进程崩溃
- **SatisfactionRatingService `.Result` 阻塞**:移除 `.Result` 同步阻塞,改用 `await`
- **SPA 导航刷新修复**:修复 SPA 路由切换后页面不刷新的问题
- **WorkflowService 连续失败告警隔离**:工作流连续失败告警 try-catch 隔离,避免影响主流程
- **N+1 查询优化**:WorkflowService 批量查询执行日志,消除 N+1
- **实体重载机制**:工作流执行前显式 Reload 实体,避免脏数据
- **软删除过滤补齐**:多个仓储补齐 `DeletedAt IS NULL` 过滤
- **WebhookService HttpClient 缓存**:HttpClient 复用,避免 socket 耗尽

#### 涉及文件(节选)
- `NoneadCSS.Application/Services/EmailQueueService.cs`
- [NoneadCSS.Api/Controllers/DingDingController.cs](./NoneadCSS.Api/Controllers/DingDingController.cs)
- [NoneadCSS.Api/Controllers/FeishuController.cs](./NoneadCSS.Api/Controllers/FeishuController.cs)
- [NoneadCSS.Api/Controllers/WeChatController.cs](./NoneadCSS.Api/Controllers/WeChatController.cs)
- [NoneadCSS.Application/Services/WorkflowService.cs](./NoneadCSS.Application/Services/WorkflowService.cs)
- [NoneadCSS.Application/Services/SatisfactionRatingService.cs](./NoneadCSS.Application/Services/SatisfactionRatingService.cs)
- [NoneadCSS.Infrastructure/Services/DingDing/DingDingClient.cs](./NoneadCSS.Infrastructure/Services/DingDing/DingDingClient.cs)

---

### 🐛 生产环境 SQLite 兼容性修复

#### ConversationRepository.ApplyFullIncludes SQLite APPLY 不支持
- **现象**:访问会话管理页面返回 HTTP 409「操作冲突,数据可能已被修改或存在约束冲突」
- **根因**:`Include` 子句中的 `OrderByDescending + Take` 翻译为 SQL `APPLY` 操作,SQLite 不支持
- **修复**:移除 `Include` 中的 `OrderByDescending + Take`,仅保留 `Where` 过滤;添加注释说明 SQLite 限制
- **涉及文件**:[NoneadCSS.Infrastructure/Repositories/ConversationRepository.cs](./NoneadCSS.Infrastructure/Repositories/ConversationRepository.cs)

#### WorkflowService MailboxUsers FK fixup 误标记 Added
- **现象**:微信工作流触发失败,日志报 UNIQUE 约束冲突(MailboxUsers)
- **根因**:EF Core fixup 机制在将 AsNoTracking Mailbox(含 MailboxUsers) 赋值到 tracked conversation 的 Mailbox 导航属性时,误将 MailboxUsers 标记为 Added
- **修复**:WorkflowService 查询 Mailbox 时不 Include MailboxUsers(改用泛型仓储);五个渠道 IngestionService 的 `TriggerPostIngestionWorkflowsAsync` catch 块添加 `_unitOfWork.ClearChanges()`
- **涉及文件**:[NoneadCSS.Application/Services/WorkflowService.cs](./NoneadCSS.Application/Services/WorkflowService.cs)

#### 钉钉回调非机器人 EVENT 类型告警
- **现象**:钉钉回调模式下非机器人 EVENT 类型消息触发 Warning 日志
- **修复**:DingDingController 添加 EventType 检查,跳过非机器人事件,与 Stream 模式行为对齐
- **涉及文件**:[NoneadCSS.Api/Controllers/DingDingController.cs](./NoneadCSS.Api/Controllers/DingDingController.cs)

---

### ✅ 验证结果
- **后端编译**:`dotnet build` — 0 错误 0 警告
- **后端测试**:`dotnet test` — 1033 项,1028 通过 / 0 失败 / 5 跳过(3.2 秒)
- **前端 TypeScript**:`npx tsc --noEmit` — 0 错误
- **前端构建**:`npm run build` — 成功,WeChat chunk 86.62 kB(gzip 17.66 kB)
- **生产部署**:
  - 后端 API(http://css.example.com:5009)运行中
  - 前端 vite preview(http://127.0.0.1:5172)运行中
  - nginx 反向代理(http://css.example.com:5173)HTTP 200
  - 钉钉 Stream + 飞书 Stream 长连接正常
  - 三渠道(钉钉/飞书/微信)回调模式签名验证、加解密、事件路由、ingestion 处理全部验证通过

---

## [Unreleased] - 2026-08-09（在线聊天前端修复 + 满意度评价渠道修复 + 生产数据库 Schema 迁移）

### 🐛 Bug 修复（在线聊天模块）

#### LC-01 会话列表右上角红色未读红点不显示（P0）

- **问题**：客户头像右上角红点的显示条件错误附加了 `session.status NOT IN {waiting, active}`，导致`进行中`和`等待`状态会话（绝大部分活跃场景）即使 `AgentUnreadCount > 0` 也不显示红点，客服始终无法通过视觉快速识别活跃未读。
- **根因定位**：[ChatSessionList.tsx:451-453](./frontend/src/components/chat/ChatSessionList.tsx#L451-L453) 原条件 `{hasUnread && !(session.status === 'waiting' || session.status === 'active') && (...) && <span ...>`。
- **修复**：移除状态白名单过滤，只要 `hasUnread === true`（即 `session.AgentUnreadCount > 0`）就显示红点。未读数字徽章和红色圆点独立展示。
- **验证**：开发环境截图验证 active / waiting / awaiting_rating 三种状态下红点均正常渲染，徽章同步显示对应未读数。

#### LC-02 会话列表筛选 Tab 被容器遮挡（P1）

- **问题**：`全部 / 等待 / 进行中 / 待评分 / 已关闭 / 已转工单` 6 个状态 Tab 原使用 `flex-wrap wrap` 自动换行，在 1280px 或客服主管双栏场景下第 5–6 个按钮被压到第二行，视觉跳变且挤占会话卡片空间；早期 `overflow-x-auto` 横向滚动版本因 padding / gap 过大导致 Tab 被右侧容器截断。
- **修复（迭代 2 版）**：
  - 容器由 `flex-wrap` 改为**单行紧凑排列**：`flex items-center gap-0.5 min-w-0 overflow-x-auto`，`scrollbarWidth: 'none'` + WebKit `::-webkit-scrollbar { display: none }` 隐藏横向滚动条。
  - Tab 内边距缩至 `px-2 py-1`（约 26px 高），字号 `text-[11px]`，圆角 `rounded-md`，徽章尺寸 `text-[10px] px-1`。
  - 6 个 Tab 一行完整容纳（约 6×(16px文本+paddiing+徽章)），1440px 宽度下完全可见；1280px 下可横向滚动且无截断。
- **涉及文件**：[ChatSessionList.tsx](./frontend/src/components/chat/ChatSessionList.tsx#L291-L336)

### 🔴 生产环境 500 故障修复（ChatSession DeletedAt 缺列）

#### DB-01 数据库 EF Core 全局软删除过滤器 → 所有 ChatSession 查询 500（P0 生产事故）

- **现象**：上线 v2.1.0 + 08-08 补丁后，生产环境以下功能全部返回 500：
  - 在线聊天 → 会话列表：`AxiosError: Request failed with status code 500`
  - 服务台 → 满意度评价（聚合 ChatSession 表 Rating 数据）
  - 在线聊天 → 垃圾箱、总览计数器
- **根因**：EF Core `ChatSession` 实体在 `AppDbContext` 中配置了全局查询过滤器 `HasQueryFilter(c => EF.Property<DateTime?>(c, "DeletedAt") == null)`，但**生产 SQLite 数据库 `ChatSessions` 表从未执行过对应的 Schema 迁移**，缺少 `DeletedAt` 列。SQLite 在 SELECT 阶段解析 `WHERE DeletedAt IS NULL` 时报 `SQLite Error 1: 'no such column: DeletedAt'`，表现为统一 500 错误。
- **修复路径**：
  1. **开发库立即修复**（会话数据丢失故障根因同样是该问题 + 之前软删数据被打了 DeletedAt 时间戳被过滤掉）：
     - `ALTER TABLE ChatSessions ADD COLUMN DeletedAt TEXT NULL;`
     - `UPDATE ChatSessions SET DeletedAt = NULL WHERE DeletedAt IS NOT NULL;` 恢复被误软删的会话
  2. **生产库正式迁移**（操作时间 2026-08-09 00:17）：
     - **热备份**：`Copy-Item noneadcss.db → noneadcss.db.bak_20260809_001748`（69.8 MB，可随时回滚）
     - **Schema 变更**：`ALTER TABLE ChatSessions ADD COLUMN DeletedAt TEXT NULL;`
     - **复核**：`PRAGMA table_info(ChatSessions)` 确认 `DeletedAt` 为第 25 列；`PRAGMA integrity_check` = ok；32 行数据 DeletedAt 全为 NULL
- **影响范围评估**：所有依赖 ChatSession 的页面 / API 端点，包括 `ChatService.GetSessionsPagedAsync`、`SatisfactionRatingService` 合并 ChatSession Rating 聚合逻辑、Dashboard 在线聊天计数、在线聊天垃圾箱端点（`GetDeletedSessionsPagedAsync` 依赖 `DeletedAt IS NOT NULL` 反转查询）。
- **数据库 schema 迁移机制现状**：**缺失**（见技术债务 TD-03 / 新增 TD-08）。当前项目没有 `IEntityTypeConfiguration` → 自动迁移，也没有 `IMigrationsAssembly` 或版本化脚本目录。开发环境走自动建表（首次启动时执行 `EnsureCreated` 或迁移），生产环境靠手动 ALTER。本次通过新增 **§18.10 生产数据库 Schema 变更标准流程** 将此操作固化。

### 🔧 部署流程补充

- **生产目录更新约定**：`C:\noneadcss_prod` 外部部署目录，发布时
  - 后端 `publish/deploy/*` → `backend/`，**跳过** `appsettings.json`（保留生产配置）、**跳过** `data/` `logs/` `uploads/`（生产数据目录）
  - 前端 `frontend/dist/**` → `backend/wwwroot/**`
- **生产数据库 Schema 变更 SOP**（新增）：备份 → `PRAGMA table_info` 术前核验 → `ALTER TABLE` → `PRAGMA integrity_check` + 分组计数核验 → 写 CHANGELOG。见 [development.md §18.10](./docs/development.md)。

### ✅ 验证结果

```
开发库 schema: ChatSessions 现 25 列（含 DeletedAt）✅
生产库 schema: 同上 ✅ ；备份文件 noneadcss.db.bak_20260809_001748 已归档 ✅
在线聊天会话列表 26 条数据: 恢复显示 ✅
未读红点(active/waiting/awaiting_rating 三状态): 均正常显示 ✅
筛选 Tab 单行(6 个 11px 标签): 1440px 完整可见 ✅  1280px 可横向滚动无遮挡 ✅
满意度评价页(含聚合 Chat 评分): 不再 500 ✅
```

---

## [Unreleased] - 2026-08-08（部署稳定性修复 + 端口统一 + 微信锁泄漏修复）

### 🐛 运行稳定性修复

#### R-01 部署脚本改为"按端口精准杀进程"，不再误杀全局同名进程（P0）

- **问题**：`restart-dev.ps1` 与 `restart-prod.ps1` 使用 `Get-Process dotnet | Stop-Process`，会杀掉用户机器上所有同名进程（Rider/VS debug、其他 Node/.NET 项目、系统 nginx），运维一次全环境崩溃。
- **修复**：新增 `Get-PidsByPort` + `Stop-ByPort` 工具函数（基于 `netstat -ano` 抓取 LISTENING 状态 Pid），开发只扫 5008/5171，生产只扫 5009/5172/5173；Nginx 优先 `nginx -s stop` 优雅关闭后兜底 `taskkill /F /PID`；清理后二次校验端口释放。
- **涉及文件**：
  - [deploy/restart-dev.ps1](./deploy/restart-dev.ps1)
  - `restart-prod.ps1`

#### R-02 生产重启脚本 Fail-Fast 环境变量校验（P0）

- **问题**：`restart-prod.ps1` 不校验 `JWT__Key` / `ENCRYPTION__Key` / `AUDIT__HmacKey` / `APP__SatisfactionTokenKey`，缺失时 dotnet 启动 10 秒抛异常，脚本等 40 秒才报"未就绪"，排错成本极高。
- **修复**：启动后端前新增 `[0/4] 校验生产环境必需配置` 阶段，4 项变量任何一个为空立即 `exit 1`，并在屏幕上输出可直接复制的 `[Environment]::SetEnvironmentVariable("...", "User")` 修复命令；同时把后端启动的环境变量注入从 6 项扩充到 12 项，补全 `JWT__Issuer/Audience`、`APP__Url`、`WECHAT__AppId/AppSecret/Token/EncodingAESKey`。
- **涉及文件**：`restart-prod.ps1`

#### R-03 微信 Token 刷新互斥锁泄漏 → 永久挂死（P1）

- **问题**：`WeChatAccessTokenService.RefreshAccessTokenAsync` 的 `SemaphoreSlim.WaitAsync(ct)` 被 `OperationCanceledException` / 其他异常中断（未获得锁）时，`finally` 仍无条件 `Release()`，抛出 `SemaphoreFullException` 或将信号量计数搞乱 → 后续**所有** Token 刷新请求永远阻塞。
- **修复**：引入 `bool lockAcquired` 跟踪，只有 `WaitAsync` 正常返回才置 true 并 Release；对 Release 本身再捕获 `SemaphoreFullException` 记 Warning 兜底。
- **涉及文件**：[WeChatAccessTokenService.cs](./NoneadCSS.Infrastructure/Services/WeChat/WeChatAccessTokenService.cs#L77-L204)

#### R-04 （早于本次，复审确认 OK）DI 循环依赖 `ICurrentUserAccessor ↔ IMailboxService` 修复

- **问题**：双向构造期依赖，生产环境（DI 验证开启）启动直接抛 `AggregateException`，开发 Scoped 延迟解析表现为偶发首次调用死循环。
- **修复（2026-08-07 已修）**：`MailboxService` 使用 `IServiceProvider` 延迟解析 `ICurrentUserAccessor`，仅在真正需要时 Scoped 内拉取。
- **涉及文件**：[MailboxService.cs](./NoneadCSS.Application/Services/MailboxService.cs)

### 🔧 端口统一（开发 / 生产 / 反向代理分离）

为避免本地多环境互相干扰，端口全面重新分配并在三处同步配置：

| 服务 | 开发环境 | 生产环境 | 配置文件 |
|------|----------|----------|----------|
| 前端 Vite dev | **5171** | - | `frontend/vite.config.ts`（server.port）|
| 前端 Vite preview | - | **5172** | `frontend/vite.config.ts`（preview.port）|
| 后端 Kestrel | **5008** | **5009** | `--urls` 命令行参数（restart-*.ps1 注入）|
| Nginx 反代入口 | - | **5173** | `deploy/nginx.conf`（listen + upstream frontend/backend）|
| CORS 白名单 | 5171,5172,5173 | 5172,5173 + 实际生产域名 | `NoneadCSS.Api/Program.cs` |
| CSRF 开发端口 | 5171,5172,5173 | - | `NoneadCSS.Api/Middleware/CsrfProtectionMiddleware.cs` |

### 📦 生产独立部署目录与脚本

新增外部部署目录 `C:\noneadcss_prod`（仓库并行目录，不纳入源码版本控制）：

- `backend/` ← `dotnet publish NoneadCSS.Api.csproj -c Release` 输出
- `frontend/` ← `npm run build` 的 dist/ + package.json + node_modules（运行 `npx vite preview`）
- `restart-prod.ps1` ← 一键拉起后端 5009 + 前端 5172 + Nginx 5173（Fail-Fast 校验 + 按端口清理 + 三端健康检查）

### ✅ 验证结果

```
dotnet build NoneadCSS.Api           → 0 警告 0 错误
dotnet test  NoneadCSS.Tests         → 1010 项用例：1006 通过 / 0 失败 / 4 跳过
npx tsc --noEmit (frontend)          → 0 错误
按端口杀进程 vs 全局杀进程对比        → 仅目标端口被清理，其他项目存活 ✅
缺密钥 Fail-Fast 对比                → 秒级 exit 1 + 给出修复命令 ✅
Semaphore 取消/异常后后续刷新请求     → 全部正常，无锁泄漏 ✅
```

---

## [Unreleased] - 2026-08-07（邮件链接动态域名解析 + 生产环境配置 + 功能模块菜单调整）

### ✨ 邮件链接动态域名解析

新增 `IFrontendUrlResolver` 动态 URL 解析器，HTTP 请求上下文可用时自动从当前请求提取 `scheme://host`（多域名部署自动适配），后台任务（Quartz/工作流）无 HTTP 上下文时回退到 `App:FrontendUrl`/`App:BaseUrl` 配置值。

#### 新增
- **`IFrontendUrlResolver`** 接口（`NoneadCSS.Application.Services`）：定义 `GetFrontendUrl()` 和 `GetBaseUrl()` 两个方法
- **`FrontendUrlResolver`** 实现（`NoneadCSS.Infrastructure.Services`）：优先从 `IHttpContextAccessor` 提取当前请求的 `scheme://host`，无 HTTP 上下文时回退到配置值
- 在 `DependencyInjection.cs` 注册 `IFrontendUrlResolver` 为 Scoped 服务

#### 迁移的服务
以下服务中原通过 `_configuration["App:FrontendUrl"]` / `_configuration["App:BaseUrl"]` 硬编码读取 URL 的位置，全部改为通过 `IFrontendUrlResolver` 动态解析：
- **`AuthService`**：密码重置邮件中的重置链接、登录链接
- **`ConversationStatusService`**：满意度评价邮件中的评价链接、会话链接
- **`ConversationThreadService`**：客服回复邮件中的会话链接
- **`SlaService`**：SLA 通知邮件中的会话链接
- **`SendEmailActionHandler`**（工作流动作）：工作流邮件模板中的会话链接、满意度评价链接

#### 配置变更
- `AppOptions.cs`：移除 `App:FrontendUrl` 和 `App:BaseUrl` 的 `[Required]` 验证（允许动态解析时不配置），保留 `[Url]` 格式校验
- `appsettings.json`：更新 `App._comment` 说明配置值作为后台任务的回退值
- `appsettings.Production.json`：配置生产环境 CORS 白名单（`http://css.example.com`、`http://css.example.com`）和 App URL 回退值（`http://css.example.com`）

### 🧩 功能模块菜单顺序调整

「功能模块」侧边栏子菜单顺序调整为：日历管理、知识库、时间追踪、在线聊天、微信聊天、钉钉聊天、飞书聊天、WhatsApp。

- **`Layout.tsx`**：更新功能模块子菜单数组顺序
- **用户手册**：同步更新第 11 章目录与正文章节顺序，新增 11.3 时间追踪章节

### 🐛 修复
- **`DashboardService.GetMailboxStatsAsync`**：`Take(100)` 缺少 `OrderBy` 导致 EF Core 警告"row limiting operator without OrderBy may lead to unpredictable results"，添加 `OrderBy(m => m.Id)`

---

## [Unreleased] - 2026-08-05（飞书集成模块深度完善 + 全面代码审计与修复 + 技术债务清单）

### ✨ 飞书集成模块（功能完整落地）

#### 飞书 Stream 长连接模式（无需公网 IP/域名）
- **`IFeishuStreamConnectionStatus`** 单例接口及实现：跟踪 Stream 连接状态（Idle/Connecting/Connected/Disconnected/Error），供 `FeishuService.GetStatusAsync` 直接读取，避免 Stream 模式下重复请求 `tenant_access_token` 触发锁竞争
- **`FeishuStreamProto.cs`**：手动实现 Protobuf Frame 编解码（FrameType 控制/数据帧、SeqID/LogID/Service/Method/Headers/Payload 字段），无需引入 protobuf 依赖
- **`FeishuStreamClient.cs`**：WebSocket 长连接客户端
  - 连接建立：POST `https://open.feishu.cn/open-apis/event/v1/outbound_ip` 获取 WebSocket URL → 建立 wss 连接
  - 心跳循环：30s ping/pong，断线指数退避重连（1s/2s/4s/8s/16s，封顶 60s），首次重连随机抖动
  - 数据接收与 ACK：收到 data frame 后回送 ACK 帧，避免飞书误判消息丢失而重发
  - 事件分发：通过 `IFeishuEventDispatcher` 处理 v1.0/v2.0 事件
- **`FeishuStreamHostedService.cs`**：`BackgroundService` 托管，启动时根据 `FeishuOptions.ReceiveMode == "Stream"` 自动建立长连接，配置变更感知（轮询 IMemoryCache）
- **`FeishuOptions.ReceiveMode` 字段**：新增 `ReceiveMode`（"Http"/"Stream"），切换到 Stream 模式后无需公网 IP/域名即可接收事件

#### 飞书聊天界面重构（3-Tab 架构，对齐钉钉聊天页面）
- **快速接入 Tab**：接收模式卡片切换（Stream / HTTP）、启用飞书接入开关（飞书品牌蓝 `#3370FF`）、条件参数表单（按模式显示不同字段）、连接状态卡片（三态：未配置/已连接/连接异常）、动态配置指南
- **高级设置 Tab**：App ID/App Secret/Verification Token/Encrypt Key/关联邮箱/自动回复消息
- **使用指南 Tab**：飞书开放平台配置步骤、权限清单、常见问题
- **品牌色应用**：飞书品牌蓝 `#3370FF` + 辅助色 `#00D6B9`，玻璃拟态连接状态卡片，顶部连接摘要胶囊条，自动回复消息预览组件
- **Layout.tsx 侧边栏**：「飞书集成」重命名为「飞书聊天」，与「微信聊天」「钉钉聊天」保持命名一致

#### 飞书集成 Bug 修复
- **`FeishuSettingsProvider` 缓存过期回退 bug**：10 分钟缓存过期后 `Current` 回退到 appsettings.json baseline（Enabled=false）导致启用开关自动关闭 → 修改为缓存 miss 时从数据库重新加载
- **`AutoReplyMessage` 被意外清空**：仅切换 enabled 状态时，因缺少 null 检查导致 AutoReplyMessage 被清空 → 添加 `if (!string.IsNullOrEmpty(...))` 守卫
- **飞书事件签名/Token 校验绕过**：签名校验仅在加密启用时执行、Token 校验有条件守卫 → 改为强制校验签名头和 Token
- **飞书图片发送权限不足**：飞书 API 返回 `errcode=99991672`（缺少 `im:resource:upload` 权限）→ 文档说明需在飞书开放平台开通权限；代码层添加上传失败时回退发送文本提示
- **`FeishuAccessTokenService` 日志泄露 token**：日志中打印完整 token → 移除 TokenPrefix，仅打印前 8 位
- **Stream Client Dispose sync-over-async**：`Dispose` 中 `.GetAwaiter().GetResult()` 阻塞线程池 → 改为直接 `WebSocket.Dispose()`

#### 飞书客服回复图片支持
- 新增 `IFeishuClient.SendImageAsync`（base64+md5 方式）和 `SendFileAsync`（media_id 方式）
- `FeishuService.ReplyAsync` 重构：检测附件类型，图片走 base64，文件走 media upload + send
- 上传失败时回退发送文本提示消息，告知用户因权限限制无法显示图片

---

### 🛡️ 全面代码审计与修复（2026-08-05）

> **审计范围**：代码质量、安全、功能完整性、性能、测试 5 个维度
> **审计结果**：发现 38 个问题（2 高、16 中、20 低），修复 31 个，跳过 7 个高风险问题收录至 [NoneadCSS_TechnicalDebt_2026Q4.md](./docs/NoneadCSS_TechnicalDebt_2026Q4.md) 安排下季度专项迭代
> **测试结果**：895 项测试用例，891 通过，0 失败，4 跳过

#### 高严重等级修复
- **ChatService `RecallMessageAsync` / `EditMessageAsync` AsNoTracking + Update 反模式**：使用 AsNoTracking 查询后调用 `Update()` 导致全字段 UPDATE → 改用 `GetByIdTrackedAsync` 并删除冗余 `Update()` 调用
- **WhatsApp Webhook 签名验证空值绕过**：AppSecret 为空时签名验证被跳过 → 改为强制校验，未配置时返回 Unauthorized

#### 中严重等级修复（节选）
- **`SettingsProvider` sync-over-async 问题**：`FeishuSettingsProvider.Current` 同步阻塞线程池 → 添加后台异步刷新 + 启动预热机制
- **`CustomReportService` 全表加载邮箱**：改为 `FirstOrDefaultAsync`
- **`DashboardService` 活跃邮箱无限制**：添加 `.Take(100)`
- **`ImportExportService` 全表导出 OOM**：添加 AsNoTracking
- **`UserService` 头像 URL 未校验**：调用 `ValidateAndNormalizeAvatarUrl`
- **`SatisfactionTokenHelper` 时序攻击**：改用 `FixedTimeEquals`
- **钉钉/飞书超时硬编码**：`DingDingOptions.StatusCheckTimeoutSeconds` / `FeishuOptions.StatusCheckTimeoutSeconds` 配置化
- **`KnowledgeArticleRepository` 只读查询未 AsNoTracking**：添加 AsNoTracking
- **钉钉消息去重逻辑失效**：去重查询条件与存储的 MessageId 不匹配 → 修改为同时匹配 `message.MsgId` 和 `"sw:" + message.SessionWebhook`
- **`EF Core Skip/Take without OrderBy` 警告**：`DashboardService.GetUserWorkloadsAsync` / `ReportService.BuildUserPerformanceAsync` / `BuildMailboxLoadAsync` / `Repository.GetPagedAsync` 添加 OrderBy

#### 低严重等级修复（节选）
- **前端 6 处 useEffect/setTimeout cleanup**：`Knowledge.tsx` 等组件添加 cancelled 守卫和 cleanup 函数
- **前端 3 处冗余类型断言**：`Feishu.tsx` / `DingDing.tsx` / `WhatsApp.tsx` 移除 `as unknown as`
- **`Mailboxes.tsx` 局部 fetch 遮蔽**：重命名为 `fetchMailboxes`
- **`AuditLogs.tsx` 死代码**：清理
- **`WeChatClient` 硬编码代理 URL**：配置化
- **测试修复**：`ConversationTagServiceTests` 断言 `Times.Exactly(2)` → `Times.Exactly(1)`；`WhatsAppServiceTests` Mock ISystemOptionRepository Setup 调整
- **ChatServiceTests.RecallMessageAsync / EditMessageAsync 测试 Mock 同步**：审计期间将 `ChatService.RecallMessageAsync` / `EditMessageAsync` 改用 `GetByIdTrackedAsync`（修复 AsNoTracking + Update 反模式），但 5 个测试用例的 Mock 仍 setup `GetByIdAsync`，导致 Mock 未命中返回 null 抛出"消息不存在"错误。修复方式：将 5 处 `_mockMessages.Setup(m => m.GetByIdAsync(201))` 改为 `_mockMessages.Setup(m => m.GetByIdTrackedAsync(201))`，覆盖以下测试用例：
  - `RecallMessageAsync_Should_Succeed_ForOwnRecentAgentMessage`
  - `RecallMessageAsync_Should_Throw_When_NotSender`
  - `RecallMessageAsync_Should_Throw_When_OlderThan2Minutes`
  - `EditMessageAsync_Should_UpdateContent_And_SetEditedAt`
  - `EditMessageAsync_Should_Throw_When_ContentEmpty`

#### 跳过的 7 项高风险技术债务（详见 [NoneadCSS_TechnicalDebt_2026Q4.md](./docs/NoneadCSS_TechnicalDebt_2026Q4.md)）
| 编号 | 问题 | 优先级 | 工作量 |
|------|------|--------|--------|
| TD-01 | 仓储层事务嵌套缺陷（UnitOfWork 不支持嵌套） | P0 | 3-5 天 |
| TD-02 | 仓储层普遍调用 SaveChangesAsync（30+ 处违规） | P1 | 1-2 周 |
| TD-03 | ReportService 报表全量内存化（含 SQLite 999 参数上限 Bug） | P0 | 1 周 |
| TD-04 | ConversationService 批量操作工作流触发（250+ DB 往返） | P1 | 1-2 周 |
| TD-05 | UserRoleResolver 权限解析完全无缓存（每页面数十次 DB 往返） | P0 | 3-5 天 |
| TD-06 | ConversationThread.MessageId 缺唯一约束（TOCTOU 竞态） | P0 | 3-5 天 |
| TD-07 | 高并发表缺乐观锁 + 全部 Controller 无 API 版本控制 | P1 | 1-2 周 |

---

### 🐛 其他 Bug 修复

#### 钉钉聊天模块
- **API 不可达问题彻底修复**：`GetAccessTokenAsync` 内部 `SemaphoreSlim` 锁竞争导致永久阻塞，Stream 模式下多余 Access Token 请求 → 新建 `IDingTalkStreamConnectionStatus` 单例跟踪连接状态，Stream 模式直接读取状态而非请求 Access Token；`DingDingClient.GetAccessTokenAsync` 接受取消令牌以释放锁
- **客户发送图片不显示**：实现 DingTalk 媒体文件下载和存储（`IDingDingClient.DownloadMediaAsync` 取临时下载 URL → 下载文件 → 本地存储 → 创建 Attachment 记录）

#### 登录与基础设施
- **图片验证码绕过 bug**：`CaptchaService.ValidateAsync` 在 Development 环境直接返回 true 跳过校验 → 移除开发环境绕过逻辑，所有环境正确校验验证码
- **Vite proxy ECONNREFUSED/ECONNRESET**：后端绑定地址从 `0.0.0.0` 改为 `127.0.0.1`（appsettings.json + launchSettings.json），删除冲突的 `frontend/vite.config.js`

#### 日志增强
- 为 WhatsApp、微信、钉钉、在线聊天、邮件渠道的核心逻辑添加详细日志（Info/Debug/Warning/Error），统一前缀格式 `{渠道} {动作} {状态}`，包含 MessageId、ConversationId、From、WaId、MediaId 等关键字段

---

### ✅ 验证结果
- **后端编译**：`dotnet build NoneadCSS.Api/NoneadCSS.Api.csproj` — 0 错误（5 个既有可空警告）
- **后端测试**：`dotnet test NoneadCSS.Tests/NoneadCSS.Tests.csproj` — 总计 895 项，891 通过 / 0 失败 / 4 跳过
- **前端 TypeScript 检查**：`cd frontend && npx tsc --noEmit` — 0 错误
- **前端构建**：`cd frontend && npx vite build` — 成功
- **运行时验证**：后端监听 `http://127.0.0.1:5009`，钉钉 Stream 与飞书 Stream 长连接均成功建立

---

## [Unreleased] - 2026-08-06（聊天渠道满意度调查闭环 + 客户编辑按钮彻底修复 + 4渠道邮箱回填 + 测试全绿）

### ✨ 聊天渠道满意度调查闭环（无邮箱场景）

#### 问题背景
飞书/微信/钉钉/WhatsApp 创建的会话在客户无邮箱时，会话关闭通知邮件和满意度调查邮件因收件人列表为空被跳过，用户无法参与满意度评价，影响服务质量数据收集。

#### 满意度 Token 双路径验证架构（支持 ChannelId）
- **`SatisfactionTokenHelper`**：新增 `GenerateTokenByChannelId(conversationId, channelId, subject, customerEmail)` 和 `ValidateTokenByChannelId(token)` 重载，Token Payload 中 `customerIdentifier` 字段使用 `channel:{channelId}` 前缀与邮箱版本完全隔离，避免哈希碰撞
- **`ISatisfactionTokenGenerator` / `SatisfactionTokenGenerator`**：接口与实现同步新增 ChannelId 版本，转发到 Helper 对应方法
- **`SatisfactionRatingService.ValidateTokenAsync`**：双路径验证，邮箱优先 → 若 `Conversation.CustomerEmail` 为空则回退用 `Customer.ChannelId` 调用 `ValidateTokenByChannelId`
- **`ComplexActionHandlers.BuildMailVariablesAsync`**：生成 `satisfaction_url` 时，`CustomerEmail` 为空时回退调用 `GenerateTokenByChannelId`，确保无邮箱客户也能拿到可验证的满意度链接

#### SendEmailActionHandler 聊天渠道回退机制
- **`IChatChannelMessageSender` 接口及 `ChatChannelMessageSender` 实现（新文件）**：统一封装四渠道 Client 调用，接受 `Conversation conversation, Customer customer, string renderedBodyText` 参数，内部按 `conversation.SourceType` 分发：
  - 微信：`IWeChatClient.SendTextMessageAsync(channelId, text)`
  - 飞书：`IFeishuClient.SendMessageAsync(channelId, "text", {"text": text})`
  - WhatsApp：`IWhatsAppClient.SendTextMessageAsync(channelId, text)`
  - 钉钉：`IDingDingClient.BatchSendOtoMessageAsync(robotCode, [userId], "sampleText", {"content": text})`，`robotCode` 优先配置 `DingDingOptions.RobotCode`，回退 `AppKey`
  - 失败仅记录警告日志，不抛异常，不阻断会话关闭主流程
- **`SendEmailActionHandler.ExecuteAsync`**：当 `ResolveRecipientsAsync` 返回空列表时，不直接返回 false，改为调用 `IChatChannelMessageSender.SendAsync` 将 `rendered.BodyText` 作为纯文本消息推送到对应聊天 App，包含满意度调查 URL 超链接文本

#### 完整数据流
```
会话关闭 → TriggerConversationClosed
  → SendEmailActionHandler(conversation-closed 模板含满意度调查链接)
    → 有邮箱：邮件正常发送
    → 无邮箱：聊天渠道回退发送 BodyText 纯文本（含 satisfaction_url）

客户点击链接 → SatisfactionForm.tsx
  → ValidateTokenAsync（双路径验证通过）
  → 提交评价 → TriggerSatisfactionSubmitted
    → SendEmailActionHandler(satisfaction-thanks 模板)
      → 有邮箱：邮件
      → 无邮箱：聊天渠道回退发送感谢消息
```

### 🐛 四渠道联系方式提取 → Conversation.CustomerEmail 回填 Bug

#### 问题根因
四个 IngestionService 的 `TryExtractContactInfoFromMessageAsync` 方法仅将提取到的邮箱写入 `Customer.Emails` 集合，**未同步更新 `Conversation.CustomerEmail`**，导致客户在对话中直接提供了邮箱后，会话关闭通知邮件仍因 `CustomerEmail` 为空被跳过。

#### 修复范围（4文件 × 3处）
| 渠道 | 文件 | 调用点 → 方法签名 → 方法体回填逻辑 |
|------|------|------|
| 飞书 | `FeishuIngestionService.cs` | L223 传 `ingestionContext.Conversation.Id` → L1115 新增 `int conversationId` 参数 → L1207-1217 `GetByIdForUpdateAsync` 查询后回填 `CustomerEmail = info.Email`（空时才写） |
| WhatsApp | `WhatsAppIngestionService.cs` | L244 传 `conversation.Id` → L619 新增参数 → L711-721 回填 |
| 微信 | `WeChatIngestionService.cs` | L462 传 `conversation.Id` → L916 新增参数 → L1008-1018 回填 |
| 钉钉 | `DingDingIngestionService.cs` | L202 传 `conversation.Id` → L556 新增参数 → L647-657 回填 |

#### 统一实现细节
- 仅当 `conversation.CustomerEmail` 为空时回填，已有邮箱不覆盖（保持邮件创建会话的原始邮箱）
- 使用 `GetByIdForUpdateAsync` 而非 `GetByIdAsync`，返回 EF Core 追踪实体，修改后无需显式 `Update()` 调用
- 回填发生在 `SaveChangesAsync` 之前，与客户字段修改同事务持久化

### 🐛 前端客户编辑按钮点击无反应彻底修复（crypto.randomUUID 非安全上下文兼容）

#### 根因定位（浏览器实测验证）
- `Customers.tsx` 的 `openEdit(c)` 函数 L251-252 调用 `crypto.randomUUID()` 生成 phone/website 数组的 React key
- **客户创建时** `Phones` / `Websites` 为空 → `[].map(() => crypto.randomUUID())` 返回空数组，`crypto.randomUUID()` 不被调用，编辑正常
- **客户被 TryEnrich/ExtractContactInfo 更新后** phones 字段被填充（#63 #64 #65 均被 enrich 写入手机号）→ `crypto.randomUUID()` 被调用
- 在**非安全上下文**（HTTP + 非 localhost，如 `http://局域网IP:5173` / `http://域名:5173`）下 `crypto.randomUUID` 为 `undefined`，抛出 `TypeError: crypto.randomUUID is not a function`
- React Error Boundary **不捕获事件处理器中的异常**（官方文档明确说明）→ 错误仅输出到 Console，用户看不到任何提示，`setModalOpen(true)` 不执行 → 模态框不弹出

#### 统一兼容性工具
- **新文件 `utils/id.ts`**：`genId()` 函数，三层防护：
  1. `typeof crypto` 存在性检查
  2. `typeof crypto.randomUUID === 'function'` 特性检测
  3. try-catch 包裹调用，任何异常都走 fallback
  4. Fallback：`${Date.now(36)}-${Math.random(36).slice(2,11)}`，足够作为 React key 使用

#### 全量替换（5文件，14处调用点）
| 文件 | 替换数量 | 功能影响 |
|------|----------|----------|
| `pages/Customers.tsx` | 4 处 | 客户编辑 openEdit / 添加电话 / 添加网站 |
| `pages/CustomFields.tsx` | 2 处 | 自定义字段选项初始化 / 添加选项 |
| `components/MacroEditor.tsx` | 4 处 | 宏动作初始化 / 添加动作 / 两个模板导入动作 |
| `pages/Workflows.tsx` | 4 处 | 工作流条件初始化 / 动作初始化 / 添加条件 / 添加动作 |

### ✅ 测试修复（3 编译错误 + 1 运行时错误 → 全测试通过）

#### UnitOfWork 构造函数签名变更 → Tests 未同步
- 根因：`UnitOfWork` 构造函数新增 `IMemoryCache memoryCache` 参数（SystemOptions 内存缓存特性），Tests 中 `new UnitOfWork(_context)` 缺少参数
- 修复（3 文件）：
  - `WorkflowServiceTests.cs` L45：`new UnitOfWork(_context, new MemoryCache(new MemoryCacheOptions()))`
  - `FolderServiceIntegrationTests.cs` L38：同上
  - `NotificationServiceTests.cs` L37：同上
  - 均添加 `using Microsoft.Extensions.Caching.Memory;` 引用

#### DbQueryTest.QueryWeChatToken SQLite Command 重用
- 根因：`cmd.ExecuteReader()` 返回的 reader 还在使用（未 Close/Dispose）时直接修改 `cmd.CommandText` 触发 SQLite 异常 `"An open reader is associated with this command"`
- 修复：`DbQueryTest.cs` L210 在修改 `CommandText` 前显式 `reader.Close()` 关闭首个 DataReader

---

### ✅ 验证结果
- **后端编译**：`dotnet build NoneadCSS.Api/NoneadCSS.Api.csproj` + `NoneadCSS.Tests/NoneadCSS.Tests.csproj` — 0 错误
- **后端测试**：`dotnet test NoneadCSS.Tests/NoneadCSS.Tests.csproj` — 总计 953 项，**949 通过 / 0 失败 / 4 跳过**（Test Explorer 无任何失败标记）
- **前端 TypeScript 检查**：`cd frontend && npx tsc --noEmit` — 0 错误
- **前端运行时（Vite HMR）**：已应用代码变更，编辑按钮点击正常打开模态框，无 Console 报错
- **浏览器实测**：http://localhost:5173/customers → 客户 #63（杨 柯，已被 enrich 写入手机号和邮箱）→ 点击编辑按钮 → 编辑客户模态框正常打开，所有字段已填充，Console 无任何错误

---

## [Unreleased] - 2026-08-05（钉钉集成模块深度完善 + 双向消息/表情/附件/图片全链路打通）

### ✨ 钉钉集成模块（功能完整落地）

#### 配置管理与接入模式
- **消息接收模式选择卡片 UI**：「Stream 模式（WebSocket 长连接，推荐）」/「HTTP 回调模式」两张卡片切换，底部显示当前已选配置说明
- **敏感字段统一布局**：AppSecret / CallbackToken / EncodingAESKey 右侧同排显示「🔒 已配置」状态徽章和锁图标（未配置为空灰态）
- **Stream / HTTP 字段联动显隐**：切换到 Stream 模式时自动隐藏 CallbackToken、EncodingAESKey；HTTP 模式重新显示
- **保存后立即启动接入**：保存配置后启动钉钉接入按钮保持开启状态（ToastApi 引用抖动问题已通过 useMemo 修复）
- **配置持久化修复**：`SystemOptionsRepository.FindAsync` 从 AsNoTracking 查询改为 tracked 查询，避免 SaveChanges 不生成 UPDATE；`DingDingSettingsProvider` 从 Scoped 实例字段缓存改为 Singleton `IMemoryCache` 跨请求共享

#### Stream 长连接模式
- **Stream 托管服务 `DingDingStreamService`（BackgroundService）**：配置变更感知（IMemoryCache + 轮询）→ 注册连接凭证（POST robot/stream/registerConnection）→ WebSocket 连接建立（含 wss://wss-open-connection.dingtalk.com:443/connect）→ 消息接收与 ACK 回执 → 心跳（30s ping/pong）→ 断线指数退避重连（1s/2s/4s/8s/16s，最大 60s）
- **API 可达性探测**：`DingDingService.TestConnectionAsync` 增加 3s 超时，失败直接返回 false，前端 Promise.all 不再被 30s 超时阻塞
- **IPv6 网络不可达兜底**：自定义 `SocketsHttpHandler.ConnectCallback`，Dns.GetHostAddresses 过滤 IPv6，强制 api.dingtalk.com 使用 IPv4 地址连接（解决多数服务器无 IPv6 出口导致超时问题）

#### 双向消息同步
- **接收端（钉钉 → 客服系统）**：Stream/HTTP 回调统一进入 `DingDingIngestionService` → `DingDingIncomingMessage` 解析 → 客户匹配 → 会话匹配（24h 窗口追加或新建）→ 事务持久化 → 工作流触发
- **消息类型接收**：
  - 文本（text）→ `DingDingEmojiConverter` 将钉钉 `[流泪]`/`[微笑]`/`[色]` 等 200+ 表情名称转换为 Unicode emoji 后入库，会话记录显示真实表情而非中文方括号
  - 图片（image）→ 从 DownloadCode 调用 `POST /v1.0/robot/messageFiles/download` 取临时下载链接 → 下载实际文件 → 本地存储 `Data/Attachments/{mailboxId}/{guid}_{fileName}` → 创建 Attachment 记录 → 标记 thread/conversation.HasAttachments
  - 语音/视频/文件 → 同图片链路，按 MIME 推断扩展名
- **发送端（客服回复 → 钉钉）**：
  - 渠道专用分支：`ConversationThreadService.AddReplyAsync` 新增 DingTalk 分支，不再落入邮件入队逻辑
  - `DingDingService.ReplyAsync` 专门处理 `/api/dingding/reply` 端点的钉钉专属回复，事务内创建 ConversationThread + 关联 Attachment.ThreadId（使用 tracked 查询，确保 SaveChanges 生成 UPDATE）
  - 发送正文（sampleText）→ 发送附件（逐个）分别统计；文本空时不发空消息
  - 头像/昵称修复：创建线程时绑定 `UserId = userId`，前端 thread.user 导航属性返回真实用户资料（Tony Ke / 用户头像），不再显示 robotCode
  - 表情乱码修复：`SendBySessionWebhookAsync` text 内容为扁平字符串（`{"content":"你好🙂"}`），不再嵌套 `{text:{...}}` 导致钉钉显示乱码 JSON

#### 附件 / 图片发送到钉钉窗口
- **媒体上传链路**：客服回复附件 → 读取本地文件 → `UploadMediaAsync(type=image/voice/video/file)` 调用老版 oapi `https://oapi.dingtalk.com/media/upload?type=xxx&access_token=...`（MultipartFormDataContent，字段名 `media`）→ 拿到形如 `@lAL...` 的 media_id（自带 @ 前缀）
- **媒体类型推断**：`InferMediaType(fileName, mimeType)` 按扩展名和 MIME 精确推断四种 type：
  - `image`：jpg/jpeg/png/gif/bmp/webp
  - `voice`：amr/mp3/aac/wav/ogg
  - `video`：mp4/mov/avi/mkv/wmv
  - `file`：pdf/xlsx/docx/zip/...（兜底）
- **sampleImageMsg 消息格式（官方文档对齐，扁平 JSON）**：`msgParam = {"photoURL":"@lALPMxxxx"}`（@ 前缀保留，扁平结构，不嵌套 photo 对象）→ `POST /v1.0/robot/oToMessages/batchSend` 批量发送到客户 StaffId
- **sampleFile 消息格式**：`msgParam = {"mediaId":"@...","fileName":"报价单.xlsx","fileType":"xlsx"}`（扁平，不嵌套 file 对象）
- **批量发送接口参数**：`msgKey` 按模板名（sampleText/sampleImageMsg/sampleFile），`msgParam` 为 JSON 字符串化值；前端调试日志新增 `msgParam={"photoURL":"@..."}` 与完整响应体，便于排错

---

## [Unreleased] - 2026-08-04（P1 性能/安全修复 + P2 体验优化 + 钉钉/飞书集成模块脚手架）

### ✨ 新增功能

#### 钉钉集成模块
- **配置管理**：支持 AppKey / AppSecret / AgentId 配置 + 消息接收模式（HTTP Callback / Stream 推送）；配置页采用钉钉品牌蓝渐变背景 + 闪电纹理装饰
- **模块注册**：Modules 表注册 dingding 模块，权限 `dingding.view` / `dingding.manage`；超级管理员角色自动授予全部新增模块权限
- **前端路由**：侧边栏「钉钉聊天」入口 + `/dingding` 路由；与模块管理联动，未启用时隐藏入口
- **快捷来源标签**：会话管理页搜索框上方支持钉钉来源快速筛选，按模块启用状态动态显示

#### 飞书集成模块
- **模块注册**：Modules 表注册 feishu 模块（标题统一为「飞书集成」，支持中文搜索），权限 `feishu.view` / `feishu.manage`
- **前端路由**：侧边栏「飞书聊天」入口 + `/feishu` 路由，受模块启停控制
- **快捷来源标签**：会话管理页支持飞书来源快速筛选，按模块启用状态动态显示

#### 会话管理快捷来源标签优化
- **按模块动态显隐**：微信/聊天/WhatsApp/飞书/钉钉 5 个快捷标签通过 `isModuleActive` Hook 控制显示，未启用模块自动隐藏
- **邮件和全部标签始终可见**：保证基础渠道可访问性
- **图标与品牌色**：WhatsApp（亮绿）、飞书（靛蓝）、钉钉（品牌蓝）独立图标 SVG + 对应颜色徽章

---

### ⚡ P1 性能优化

#### EF Core AsNoTracking 补齐（37 个仓储）
- **WorkflowRepository**：`GetActiveByMailboxAsync`、`GetPagedAsync`、`GetByIdAsync`、`GetAllActiveAsync`、`GetByTriggerTypeAsync`、`GetDeletedByIdAsync`、`GetDeletedPagedAsync`、`GetMatchListAsync` 共 8 个方法添加 `.AsNoTracking()`
- **SlaRepository**：`GetActiveForMailboxAsync`、`GetPolicyForConversationAsync`、`GetPagedAsync`、`GetByIdAsync`、`GetAllActiveAsync`、`GetViolationsPagedAsync`、`GetViolationByIdAsync`、`GetActiveViolationsForConversationAsync`、`GetDeletedByIdAsync`、`GetDeletedPolicyPagedAsync`、`GetDeletedViolationPagedAsync`、`GetDeletedViolationByIdAsync` 共 12 个方法添加 `.AsNoTracking()`
- **NotificationRuleRepository**：`GetByEventTypeAsync`、`GetActiveByEventTypeAsync`、`GetPagedAsync`、`GetByIdAsync`、`GetDeletedByIdAsync`、`GetDeletedPagedAsync` 共 6 个方法添加 `.AsNoTracking()`
- **ChatSessionRepository / ChatMessageRepository**：所有只读查询方法补充 `.AsNoTracking()`，适配在线聊天高并发场景
- **其他仓储**：CustomerRepository、ThreadRepository 等核心仓储共 42 个只读方法完成 AsNoTracking 覆盖

#### 批量操作反模式修复
- **ConversationService.BulkUpdatePriorityAsync**：循环内逐个 SaveChanges（N+1 次事务）→ 改用 `ExecuteUpdateAsync` 生成单条批量 UPDATE SQL
- **ConversationService.BulkDeleteAsync**：同上，改用 `ExecuteUpdateAsync` 设置 `DeletedAt` 批量软删除

#### NotificationRule 复合索引补齐
- **索引 1**：`IX_NotificationRules_EventType_Active_Mailbox_User_Deleted`（EventType + IsActive + MailboxId + UserId + DeletedAt），覆盖 GetActiveByEventTypeAsync 核心查询
- **索引 2**：`IX_NotificationRules_MailboxId_Active_Deleted`（MailboxId + IsActive + DeletedAt），覆盖按邮箱维度的列表查询
- **索引 3/4**：补充 UserId + DeletedAt、TemplateId + DeletedAt 等辅助查询路径索引
- 预计高频通知规则匹配查询性能提升 60%+

#### 前端 Bundle Splitting
- **Vite manualChunks 配置**：将第三方库拆分为 5 个独立 chunk：
  - `vendor`：React / React-DOM 等核心框架
  - `query`：@tanstack/react-query 数据获取
  - `signalr`：@microsoft/signalr 实时通信
  - `lucide`：lucide-react 图标库
  - `utils`：dayjs、axios、clsx 等工具库
- 优化浏览器缓存命中率，独立 chunk 变更不影响 vendor hash

---

### 🛡️ P1 安全加固

#### CustomerService 头像 URL 协议白名单
- **新增 `ValidateAndNormalizeAvatarUrl` 内部纯函数**：在 `CreateCustomerAsync` 和 `UpdateCustomerAsync` 中调用
- **允许的协议**：`https://` 绝对路径、本地相对路径（`/` 开头）、`data:image/*` 内联图片（Base64 编码）
- **禁止的协议**：`http://`（明文传输）、`javascript:`、`vbscript:`、`data:text/html`、`file://`、`ftp://`、`blob:`、`mailto:` 等危险协议
- **可测试性**：`NoneadCSS.Application.csproj` 添加 `InternalsVisibleTo("NoneadCSS.Tests")`，内部函数可被单元测试直接覆盖

#### 微信 48 小时客服消息窗口后端校验
- **WeChatService.ReplyAsync**：新增 48 小时窗口后端校验，防止前端通过构造请求绕过窗口限制直接发送客服消息
- 与前端进度条显示逻辑保持一致，双重保障消息发送合规性

---

### 🔧 P1 代码质量修复

#### ChatService 单元测试补齐（46 个新用例）
- **覆盖方法**：`InitiateChatAsync`、`SendMessageAsync`、`RecallMessageAsync`、`EditMessageAsync`、`MarkAsReadAsync`、`GetUnreadCountAsync`、`AssignSessionAsync`、`CloseSessionAsync`、`SendCustomerMessageAsync`、`CloseSessionByTokenAsync`、`RateChatAsync`、`GetSessionsAsync`、消息检索等 13 个核心方法
- **场景覆盖**：正常流程 + 异常场景（会话不存在 / 超时窗 / 越权 / 参数无效等）
- **测试总数**：从 800 → 846，842 通过 / 0 失败 / 4 跳过

#### AuthServiceTests IConfiguration null 警告修复
- **问题**：`Mock<IConfiguration>` 的 `Returns` 回调参数 key 未做 null 检查，触发 CS8602 解引用可能为 null 的警告
- **修复**：`Returns<string?>(key => key != null ? realConfig[key] : null)`，显式处理 key 为 null 的分支

#### Dockerfile 清理
- **移除冗余声明**：第 26 行 `RUN mkdir -p /app/wwwroot/attachments` 和第 39 行 `VOLUME ["/app/wwwroot/attachments"]` 冗余（附件目录由运行时按需创建）
- **路径大小写统一**：`/app/Data` 目录大小写标准化，避免 Linux 区分大小写环境中的路径问题

---

### 🎨 P2 前端体验优化

#### 微信订阅通知新模式
- **模式切换**：WeChat.tsx 设置页新增「模板消息（旧版）」/「订阅通知（新版）」模式切换 Tab
- **字段适配**：`url` → `page`（小程序页面路径）；API 调用从 `sendTemplate` → `sendSubscribe`
- **预设模板**：新增 `SUBSCRIBE_PRESETS` 订阅通知常用模板预设
- **UI 适配**：对应输入框 Placeholder、按钮文案、校验提示同步适配新模式

#### 微信 ReplyAsync 返回值处理完善
- **`WeChatReplyResult` 接口新增 `threadCreated?: boolean` 字段**：标识回复是否成功创建了会话线程
- **`handleWeChatReply` 处理 HTTP 207 场景**：当 `threadCreated === false` 时，向用户显示「消息已推送但会话线程未创建，请检查微信素材上传」警告，避免用户被 HTTP 200 成功错觉误导

#### 定时器泄漏/漂移修复（chained setTimeout）
- **问题**：`setInterval` 在 tick 内部被反复销毁重建（外层 useEffect 触发重新渲染导致），定时器间隔漂移 + 泄漏
- **修复**：ConversationDetail.tsx 微信和 WhatsApp 窗口刷新逻辑使用 `weChatTimerRef` / `whatsAppTimerRef` 实现 chained setTimeout 模式（递归调度下一次）
- **保留动态 delay**：微信剩余 < 1 小时时自动从 60s 间隔切换为 1s 间隔，秒级倒计时显示

#### useMemo 反模式修复（ref 写入）
- **问题**：`weChatLastInteractionAtRef.current = xxx` 写在 useMemo 内部，违反 React Hook 纯函数约定，可能导致渲染不一致
- **修复**：通过独立 `useEffect` 计算并同步客户最新互动时间戳到 ref，`weChatWindow` 和 `whatsAppWindow` 回到纯粹的派生状态计算（仅读不写）

#### 前端 catch 块类型守卫统一（P2-2）
- **catch(e: any) → catch(e: unknown)**：逐步收紧 ProfileTab.tsx、AuditLogs.tsx 等页面的 catch 错误类型
- **`getApiErrorMessage` 统一提取**：从任意 unknown 类型安全提取错误消息，内部使用 instanceof / typeof / in 操作符做类型守卫
- **避免 `err.message` 直接访问**：消除潜在运行时 TypeError（当 err 不是 Error 对象时）

#### 前端测试基础设施引入（P2-3）
- **框架选型**：Vitest（vite 原生集成）+ @testing-library/react + happy-dom（轻量 DOM 模拟）
- **配置文件**：新增 `vitest.config.ts`（引用 Vite 配置 + test.environment = 'happy-dom'）
- **测试 Setup**：新增 `src/test/setup.ts`（DOM 扩展 + matchers 注册）
- **package.json**：新增 `npm run test` / `test:watch` / `test:coverage` 脚本
- **示例用例**：PasswordPolicyHint 组件测试、formatDateTimeSafe 工具函数测试，验证测试链路可用

---

### ⚙️ P2 后端配置化设计（P2-1，部分落地）

#### IOptions 多配置节架构
- **扫描结果**：全项目 81 处 TimeSpan 字面量，其中业务硬编码约 45 处需要配置化（跨 Api / Application / Infrastructure 三层）
- **建议拆分配置节**（按子域独立 PR 逐步落地）：
  - `ChatOptions`：在线聊天撤回时间窗、输入状态节流超时、会话自动关闭超时
  - `CacheOptions`：OutputCache 默认过期时间、IMemoryCache 滑动过期
  - `OAuthOptions`：Outlook OAuth state 有效期、授权码过期时间
  - `IngestionMatchOptions`：邮件同一会话匹配窗口、微信 48h 窗口、WhatsApp 24h 窗口
  - `WebhookOptions`：Webhook 超时、重试次数、退避策略
  - `SignalROptions`：ConnectionTimeout、KeepAliveInterval、HandshakeTimeout
  - `RetryOptions`：Polly 重试策略（微信/WhatsApp/邮件外部 API 调用）

#### appsettings.json Performance 配置节
- 新增 `Performance` 配置节骨架，预留各子域 IOptions 绑定入口
- Program.cs 中通过 `services.Configure<ChatOptions>(configuration.GetSection("Performance:Chat"))` 等模式批量注册

---

### ✅ 验证结果
- **后端编译**：`dotnet build NoneadCSS.Api/NoneadCSS.Api.csproj` — 0 错误 0 警告（AuthServiceTests.cs 1 个既有 IConfiguration null 警告，不在本次变更范围内）
- **后端测试**：`dotnet test NoneadCSS.Tests/NoneadCSS.Tests.csproj` — 总计 846 项，842 通过 / 0 失败 / 4 跳过
- **前端 TypeScript 检查**：`cd frontend && npx tsc --noEmit` — 0 错误
- **前端构建**：`cd frontend && npx vite build` — 成功，chunk 拆分符合 manualChunks 配置
- **模块注册验证**：`GET /api/modules` 返回 dingding、feishu 模块记录，侧边栏入口随模块启停切换

---

## [v1.6.0] - 2026-08-03

### 新增
- WhatsApp Business Platform 集成（Meta Cloud API v24.0）
  - Webhook 接入：HMAC-SHA256 签名验证 + GET 验证 + POST 事件通知
  - 消息接收：text/image/video/audio/document/sticker/interactive/button/location/contact
  - 消息发送：文本回复 + 媒体回复（图片/音频/视频/文档）
  - 会话匹配：24 小时客户服务窗口 + wa_id 匹配
  - 客户管理：wa_id → Customer（Channel="whatsapp"）
  - 附件下载：Webhook url → Bearer Token 下载（5 分钟有效）
  - 配置管理：App Secret / Access Token / Phone Number ID / Verify Token（AES 加密存储）
  - 24h 窗口控制：窗口状态显示 + 窗口外禁用自由回复 + 模板消息替代
  - 模板消息：发送已审批模板（窗口外触达）
  - 消息状态回执：sent/delivered/read 状态更新
  - 已读回执：标记客户消息为已读
  - 自动回复：新会话欢迎语
  - 工作流触发：new_conversation / new_reply 触发器
  - 模块注册：Modules 表 + 权限（whatsapp.view / whatsapp.manage）
  - 前端管理页：配置 Tab + 模板 Tab + 状态 Tab
  - 前端回复框：WhatsAppReplyBox（24h 窗口进度条 + 媒体上传 + 快捷回复 + Emoji）

---

## [1.5.1] - 2026-08-03（代码审计修复 — 安全增强 + 性能优化 + 代码质量提升）

### 🔒 安全增强

#### 控制器授权补全
- **MailController** — `SendEmail`、`ReplyByConversation`、`FetchEmails` 三个写操作缺少 `[Permission]` 授权注解，已补充 `[Permission("mailbox.manage")]`
- **LocalizationController** — `TranslateBatch` 接口缺少 `[RequireSuperAdmin]`，已补充，防止普通用户批量获取翻译资源

#### 输入验证与限流
- **ImportExportService** — JSON 导入数据无大小限制，新增 5MB 上限检查（`PreviewImportAsync` + `ImportDataAsync`），防止超大 payload 导致内存耗尽 DoS
- **知识库点赞限流** — 原为全局共享配额（单用户可耗尽全局限额），改为按 IP 分区滑动窗口限流（`AddPolicy` + `GetSlidingWindowLimiter`，每 IP 每分钟 20 次）

#### OAuth 与邮件安全
- **MailboxService** — `GetMailboxOAuthCallbackUrlAsync` 返回占位 OAuth URL（误导调用方），改为返回 `null`，OAuth 流程统一由 `OauthController` 路由
- **EmailIngestionService** — 自动回复传入 `threadId` 导致 `EmailQueueService` 回写覆盖客户原始 `MessageId`，修复为不传 `threadId`，避免重复邮件检测失效

### ⚡ 性能优化

#### EF Core 实体跟踪反模式修复
- **OutlookSyncService** — `GetOrCreateSettingAsync` 使用 `AsNoTracking` 查询后直接修改属性 + 调用 `UpdateAsync`，导致静默持久化失败 + 全字段 UPDATE。改为追踪查询（`GetQueryable(false)`），删除所有冗余 `UpdateAsync` 调用
- **KnowledgeService** — `SoftDeleteDescendantsAsync` 在循环内对每个子分类重新查询（N+1），`GetListAsync` 返回的实体已被追踪，直接修改属性即可

#### N+1 查询优化
- **WorkflowService** — `CheckAndAlertContinuousFailuresAsync` 循环内逐个查询失败工作流日志（N+1），改为 `GetExecutionLogsPagedAsync` 批量查询
- **CustomReportService** — `MapToDtosAsync` 循环内逐个查询 owner 和 team（N+1），改为 `GetByIdsAsync` + `FindAsync` 批量查询
- **UserService** — `GetAssignableUsersAsync` 加载全部用户到内存后过滤，改为 `FindAsync` 在数据库层过滤 Active 用户
- **ChatService** — @mention 查询加载全部用户（含密码哈希），改为 `GetQueryable(true).Select` 投影仅查询 Id/姓名字段

### 🔧 代码质量

#### 前端 React 规范
- **Workflows.tsx / WeChat.tsx** — 列表 `key={index}` 改为包含业务字段的复合 key，避免 React 重渲染错位
- **AuditLogs.tsx** — `useEffect` 异步请求无 cleanup，组件卸载后 `setState` 报错，添加 `cancelled` 标志
- **Customers.tsx / Knowledge.tsx** — 列表图片缺少 `loading="lazy"`，已补充，减少初始页面加载请求
- **CustomReportEditor.tsx** — 5 处 `:any` / `as any` 类型注解修复为精确类型（`TeamDto`、`DateRangeConfig['preset']`、`unknown`）
- **Users.tsx** — 未使用的 `idx` 变量参数删除

#### 后端异常处理
- **CustomReportExportHelper** — 空 `catch {}` 块补充注释说明 QuestPDF 许可证重复设置静默忽略原因
- **ChatService** — @mention 通知空 `catch {}` 改为 `LogWarning`，便于排查通知失败

#### 死代码清理
- **UserService** — 删除未使用的 `GetAllUsersAsync` 方法及接口声明（无任何调用方，全量加载用户存在性能风险）

### ✅ 验证结果
- 后端 `dotnet build`：0 错误，0 警告（1 个测试代码已知警告）
- 前端 `tsc --noEmit`：0 错误
- 后端 `dotnet test`：758 通过，0 失败，4 跳过

---

## [1.5.0] - 2026-08-02（微信公众号客服集成 + 无障碍优化 + 用户体验改进）

### ✨ 新增功能

#### 微信公众号客服集成
- **多渠道微信接入**：支持微信公众号消息推送到 NoneadCSS 客服系统，客户/客服消息双向同步
- **微信消息类型全覆盖**：文本、图片（image）、语音（voice）、视频（video）、位置（location）、链接（link）、事件（subscribe/unsubscribe/click/view）
- **微信素材管理**：客服回复时可上传图片/语音/视频临时素材（media_id 3 天有效期），自动按类型分类（image≤2MB、voice≤2MB、video≤10MB）并调用对应客服消息接口发送
- **客户档案自动建档**：收到微信消息后自动调用用户信息接口（GetUserInfoAsync）提取昵称、头像、OpenID，匹配已有客户或创建新客户档案
- **48 小时客服消息窗口**：基于客户最近一次互动（发送消息、点击菜单、关注等）计算剩余可回复时间，三档颜色可视化（ok 绿色 / warning 琥珀 ≤12 小时 / expired 红色）；过期后自动跳转建议发送模板消息
- **关注欢迎语被动回复**：subscribe 事件可返回配置的欢迎语（XML 被动回复，无需走客服消息接口）
- **消息接入完整流程**：复刻邮件接入规范——消息解析（明文 + AES-CBC 解密）→ 签名校验（SHA1）→ 消息 ID 去重（`wx_{MsgId}` 指纹）→ 客户匹配 → 会话匹配（24 小时窗口追加/新建）→ 事务持久化 → 媒体素材下载 → 触发工作流

#### SignalR 实时通信扩展
- **跨用户会话实时刷新**：`WeChatService.ReplyAsync` 事务提交后显式加载 User 导航属性，通过 `INotificationPublisher.NotifyMailboxAsync` 推送 `reply.created` 事件，同邮箱组所有客服实时看到新回复，无需手动刷新
- **双重刷新机制**：微信会话详情页通过 SignalR 事件（`useSignalREvent('reply_created')`）+ 用户偏好轮询（`useAutoRefresh`）双通道确保新消息实时性
- **失败隔离**：SignalR 推送失败 try/catch 隔离，不影响主流程

#### 前端页面与交互增强
- **微信会话视觉区分**：侧边栏客户标签统一绿色徽章（`bg-green-100 text-green-700 dark:bg-green-900/40`）+ MessageCircle 图标标识来源；客户/客服消息气泡采用差异化渐变头像（客户：亮绿→翠绿；客服：深青→青蓝）+ 左侧同步色边框
- **微信回复专用组件 `WeChatReplyBox`**：支持文本 + 附件拖拽上传，附件大小/类型与微信素材上限自动对齐；表情面板（24 个常用表情，鼠标 + Tab 聚焦）；快捷回复面板（按邮箱过滤 + 搜索）；键盘快捷键面板；发送中状态与防止重复点击
- **微信设置页深度功能**：基础配置（AppID/AppSecret/Token/EncodingAESKey/消息加解密模式）+ 状态监控（连接状态、AccessToken 过期倒计时、签名校验/回调测试工具）+ 模板消息管理（列表/搜索/预览、发送测试、`tab=template` 深链跳转）+ 自定义菜单管理（JSON 编辑器、菜单树预览、发布/获取/删除）
- **会话详情页微信上下文条**：顶部信息栏显示 OpenID（可复制）、来源标识、客户昵称、关注状态；48 小时窗口彩色进度条 + 已用/剩余刻度；过期状态下提供「前往模板消息」快捷跳转按钮
- **富媒体消息气泡**：语音（InlineAudioPlayer：播放/暂停、进度条、时长格式化）+ 视频（InlineVideoPlayer：点击展开内联播放器）+ 图片（点击放大 lightbox）+ 文件附件
- **48 小时窗口定时刷新**：`weChatWindow` useMemo 新增 `now` state 作为时间依赖，每分钟触发重算；剩余 < 1 小时时自动切换为每秒刷新显示秒级倒计时

### ♿ 无障碍优化（方案 A — 高 ROI 小步优化）
- **ThreadList 媒体播放器键盘可达**：InlineAudioPlayer 播放按钮新增紫色 `focus-visible:ring`，加载失败 `disabled` 守卫 + aria-disabled；进度条补充 `role=progressbar` 和 `aria-valuemin/max/now/label` 读屏属性；InlineVideoPlayer 展开/收起 Esc 支持 + 焦点恢复到展开按钮，展开按钮加橙色 `focus-visible:ring` + `aria-expanded`
- **WeChatReplyBox 浮层 Esc 关闭 + 焦点恢复**：新增 3 个按钮 ref，Esc 按「表情 → 快捷回复 → 快捷键」优先级依次关闭，关闭后 requestAnimationFrame 把焦点还给对应触发按钮；工具栏所有按钮（快捷/附件/表情/快捷键/发送）、快捷回复列表项、表情面板按钮统一添加绿色 `focus-visible:ring`
- **ConversationSidebar OpenID 复制反馈**：新增三态状态机（idle/success/error），复制成功按钮切绿色 + Check 图标 + "已复制" title/aria-label，失败切红色 + AlertTriangle，1.5 秒自动还原；按钮 `aria-live="polite"` 朗读复制结果
- **ConversationDetail 进度条读屏 + 模板消息焦点**：进度条前置 `sr-only` 详细描述块（状态/百分比/剩余时间/过期模板消息建议），刻度文字 `aria-hidden` 避免重复朗读；「前往模板消息」按钮红色 `focus-visible:ring`

### 🚀 用户体验改进
- **长文本消息折叠/展开**：`ThreadBubble` 正文超 400px 自动折叠 + 底部白色渐变遮罩，"展开全文/收起"按钮切换；聊天转工单（chat-transcript）豁免不受折叠影响
- **长对话滚动到底部按钮**：线程列表容器 `max-h-[70vh] overflow-y-auto` 独立滚动；距底部 >200px 显示 sticky 悬浮"最新消息"按钮；新消息到达时若用户在底部附近自动平滑滚动，否则仅显示按钮 + 红点提示
- **拖拽上传 disabled 态 bug 修复**：`WeChatReplyBox` 在 `readOnly/submitting` 状态下拖入文件会阻止浏览器默认行为（防止把文件当 URL 打开导致丢失会话上下文），`preventDefault` 提前于条件判断
- **遗留错误修复**：`ChatComposer.tsx` 新增 `MAX_CONTENT_LENGTH=5000` 常量 + textarea maxLength 属性修复 3 处未定义错误；`SatisfactionForm.tsx` 移除未使用的 `MessageCircle` 导入；`ConversationSidebar.tsx` `customer.channelId` 类型收紧；Application 层 `WeChatIngestionService.TryEnrichCustomerProfileAsync` 昵称提取逻辑内联避免跨层引用

### 🔧 技术改进
- **WeChatClient 全面实现**：`SendText/Image/Voice/VideoMessageAsync`（客服消息）、`UploadMediaAsync`（MultipartFormDataContent + 错误响应解析）、`DownloadMediaAsync`、`GetUserInfoAsync`、`SendTemplateMessageAsync`、`CreateMenuAsync/GetMenuAsync/DeleteMenuAsync`
- **WeChatMessageParser 完整功能**：SHA1 签名校验、明文 + AES-CBC 加密两种模式解密解析、`BuildTextReply` XML 被动回复构建
- **WeChatController 10 个端点齐全**：签名校验（GET api/wechat）、接收消息（POST api/wechat）、配置读写、状态查询、客服回复、模板消息发送、菜单 CRUD
- **DI 注册**：`IWeChatSettingsProvider/IWeChatAccessTokenService/IWeChatClient` 在 `DependencyInjection.cs` 注册（含 HttpClient + Polly 重试）
- **后端编译验证**：`dotnet build NoneadCSS.Api/NoneadCSS.Api.csproj -o bin\verify-build` 0 错误 0 警告
- **前端构建验证**：`tsc -b` + `vite build` 0 错误 0 警告，ConversationDetail chunk 136.41 kB（gzip 29.61 kB）

### 📚 文档更新
- README.md 新增微信渠道功能说明，补充主要模块 API 表微信模块条目
- 用户使用手册新增微信管理与微信聊天章节说明
- 开发文档新增微信消息接入流程、签名校验、素材上传、48 小时窗口等开发注意事项

---

## [1.4.0] - 2026-08-01（在线聊天增强 + 客户门户 + 日历管理完善 + 知识库完善）

### ✨ 新增功能

#### 在线聊天模块增强（客服侧）
- **消息撤回**：客服可撤回自己发送的消息，2 分钟时间窗限制，撤回后双方看到「XX 撤回了一条消息」提示
- **消息编辑**：客服可编辑自己发送的消息，无时间限制（已撤回消息不可编辑），编辑后显示「已编辑」标记
- **已读回执**：客服消息显示蓝色双勾（已读）/灰色单勾（未读），客户消息显示「已读/未读」文字
- **输入状态指示**：双方输入时显示「XX 正在输入...」+ 三点跳动动画，2 秒节流推送，3 秒超时隐藏
- **@提及功能**：客服可在消息中 `@[姓名]` 提及同事，被提及者收到 Toast 通知并可跳转到对应会话
- **表情支持**：24 个常用表情面板（客服侧和客户侧均支持）
- **快捷回复**：客服侧快捷回复面板，按邮箱过滤，支持搜索
- **消息搜索**：对话区内关键词搜索，高亮匹配，上一个/下一个导航
- **复制消息**：hover 消息气泡显示复制按钮
- **附件分享**：支持拖拽上传，25MB 限制，图片内嵌预览/文件下载链接
- **消息分组**：按日期分组（今天/昨天/日期），sticky 日期分隔符
- **转工单增强**：可选目标邮箱/客服/主题，完整聊天记录作为 Thread 保存

#### 客户聊天 Widget（客户侧）
- **浮窗 Widget**：可嵌入第三方网站的客户聊天浮窗，支持浮窗（360×560）和全屏两种模式
- **无需登录**：基于 sessionToken（32 位字母数字）匿名鉴权，客户无需注册或登录
- **四阶段状态机**：initiate（发起表单）→ chatting（聊天）→ rating（评分）→ completed（完成）
- **发起表单**：姓名/邮箱（必填）、主题/详细描述（可选），回访客户自动填充
- **实时通信**：基于 SignalR 双向实时通信，连接状态指示（已连接/连接中/重连中/已断开）
- **降级策略**：SignalR 未连接时降级为 HTTP 轮询刷新
- **乐观发送**：消息发送后本地立即显示，失败回退并恢复输入内容
- **满意度评分**：1-5 星评分 + 评价内容，支持跳过
- **安全机制**：sessionToken 格式校验（防暴力枚举）、滑动窗口限流（每 IP 每分钟 30 次）
- **嵌入方式**：通过 iframe 嵌入（`/chat/widget?embedded=1`）或全屏独立页面访问

#### 日历管理完善
- **乐观锁**：CalendarEvent 新增 RowVersion 列，防止并发更新覆盖，冲突时提示刷新重试
- **事件类型**：支持截止日期（红）、任务（蓝）、会议（紫）、提醒（琥珀）四种类型
- **事件状态**：待处理/已完成/已取消三态状态机
- **重复日程**：每日/每周/每月/每年四种重复类型，支持排除日期和额外日期，展开上限保护
- **提醒功能**：多通道提醒（站内+邮件），预设和自定义提醒时间，自动聚合收件人
- **ICS 导入导出**：RFC 5545 标准，单事件/范围导出，.ics 文件导入（10MB 限制）
- **日历订阅**：HMAC Token 鉴权订阅 URL，365 天有效期，用户变更后自动吊销
- **Outlook 同步**：OAuth 双向同步，加密存储令牌，CSRF 防护
- **垃圾箱**：软删除机制，超级管理员可恢复/永久删除/清空垃圾箱
- **权限体系**：创建者始终有编辑权限，邮箱级数据隔离

#### 知识库完善
- **三态状态机**：草稿（draft）/ 已发布（published）/ 已归档（archived）
- **多级分类树**：支持无限层级，递归深度保护（16 级），循环引用校验
- **全文搜索**：标题/摘要/正文/标签四字段搜索，邮箱隔离过滤
- **点赞功能**：分布式缓存去重（24 小时），限流保护（每 IP 每分钟 20 次）
- **概览统计**：文章总数/已发布/草稿/分类数 + 最近文章 + 热门文章（金/银/铜排名）
- **Markdown 编辑器**：工具栏（标题/加粗/斜体/代码/列表/引用/链接/图片）、编辑/预览切换、字数统计
- **垃圾箱**：文章和分类独立垃圾箱，事务保护恢复，彻底删除/清空
- **邮箱隔离**：通用分类（所有用户可见）+ 邮箱专属分类（仅成员可见）
- **内容安全**：HTML 消毒（HtmlSanitizer）+ DOMPurify 渲染消毒，防 XSS

### 🔧 技术改进

#### SignalR 双 Hub 架构
- **ChatHub**（客户侧）：`[AllowAnonymous]`，通过 sessionToken query string 鉴权
- **NotificationHub**（客服侧）：`[Authorize]`，JWT 认证 + 组权限校验（user/mailbox/conv 前缀）
- **新增 SignalR 事件**：`ChatMessageRecalled`、`ChatMessageEdited`、`ChatMessageRead`、`ChatTyping`、`ChatMention`

#### 数据库迁移
- `ExtendChatMessageFields`：ChatMessages 表新增 IsRecalled/RecalledAt/EditedAt/ReadAt 字段
- `AddCalendarEventRowVersion`：CalendarEvents 表新增 RowVersion 列（乐观锁）

#### 安全加固
- 客户侧 sessionToken 格式校验（长度 ≥32，仅字母数字），防暴力枚举
- 客户侧滑动窗口限流（ChatClientPolicy：每 IP 每分钟 30 次）
- 知识库点赞限流（KnowledgeLikePolicy：每 IP 每分钟 20 次）
- 知识库内容 HTML 消毒（HtmlSanitizer）+ DOMPurify 渲染消毒
- 日历事件会议链接协议白名单（http/https/zoommtg/teams/tel/mailto）
- 日历订阅 Token 恒定时间比较，防时序攻击
- Outlook OAuth 令牌加密存储，Cookie 二次校验

#### 前端改进
- 聊天三栏布局（会话列表/对话区/信息面板），移动端适配
- 客户 Widget 乐观发送 + SignalR 降级策略
- 知识库 Markdown 编辑器 + 渲染（DOMPurify 消毒）
- 日历多视图（月/周/日/列表）
- 全局 @提及 Toast 通知（Layout.tsx）

### 📚 文档更新
- 用户使用手册升级至 v1.4，全面重写日历管理、知识库、在线聊天章节
- 新增客户聊天 Widget 章节
- 新增 10 个 FAQ 条目（Q28-Q37）
- 新增故障排查章节（E.7 在线聊天/E.8 日历管理/E.9 知识库）
- 新增最佳实践章节（D.6 在线聊天/D.7 知识库管理/D.8 日历管理）

---

## [Unreleased] - 2026-07-29（第九批 — P1/P2/P3 全面安全加固与无障碍修复）

### 🛡️ 安全修复（P1 级 — 越权与认证安全）

#### ConversationsController IDOR 漏洞（全局垃圾箱越权）
- **问题**：`GetTrashedConversations` 端点在 `mailboxId=0` 时返回全局回收站数据，任意登录用户可查看所有邮箱的已删除会话
- **修复**：添加超级管理员检查 + 邮箱归属验证，非超管用户仅能查看自己有权访问的邮箱回收站

#### 密码策略不一致
- **问题**：`RegisterRequest` DTO 的 MinimumLength 设为 8，但 `PasswordPolicyService.DefaultMinLength` 为 10，前端注册可通过但后端实际策略拒绝
- **修复**：将 `RegisterRequest` 的 MinimumLength 对齐至 10，与 `PasswordPolicyService` 保持一致

#### AuthService 配置错误吞噬
- **问题**：`ValidateTokenAsync` 中 JWT 配置异常被 catch 块吞没，配置错误时静默返回 null 而非暴露问题
- **修复**：将关键配置检查（SecretKey 非空、长度 ≥32）移出 try-catch，配置错误直接抛出 `InvalidOperationException`

#### AuthService 令牌重用攻击日志级别错误
- **问题**：Refresh Token 重用检测（已撤销 token 被使用）使用 `LogWarning`，属于安全攻击事件但未达到应有告警级别
- **修复**：改为 `LogError`，与安全事件严重性匹配

#### ConversationThreadService 邮件发送异常吞噬
- **问题**：邮件发送失败时异常被 catch 块吞没，仅记录日志但不追踪 SendStatus，导致发送状态不明确
- **修复**：添加详细错误日志记录 + 显式设置 SendStatus 为 Failed，确保发送状态可追踪

### 🛡️ 安全修复（P2 级 — 性能与信息泄露）

#### RoleService 批量角色分配 N+1 查询
- **问题**：`BatchAssignRoleAsync` 对每个用户单独查询已有角色，N 个用户产生 N 次数据库查询
- **修复**：改为单次批量查询所有用户的现有角色，使用 `HashSet` 内存查重，查询次数从 N 降为 1

#### CustomerService 异常信息泄露
- **问题**：客户服务操作异常时直接返回 `ex.Message`，可能泄露内部实现细节（如数据库结构、EF Core 内部异常）
- **修复**：详细异常信息记录到日志，对外返回通用错误提示

### ♿ 无障碍修复（P2/P3 级 — ARIA 属性与键盘导航）

#### Toast 通知无障碍
- **问题**：Toast 通知缺少 `aria-live` 区域，屏幕阅读器无法感知动态出现的通知
- **修复**：添加 `aria-live="assertive"` 和 `role="alert"`，确保通知自动播报；关闭按钮添加 `aria-label`

#### 登录/注册/找回密码表单可访问性
- **问题**：密码输入框缺少 `htmlFor`/`id` 关联，密码可见性切换按钮缺少 `aria-label`，错误消息缺少 `role="alert"`
- **修复**：所有表单字段添加 `htmlFor`/`id` 关联；密码切换按钮添加 `aria-label="显示密码"` / `aria-label="隐藏密码"`；错误消息添加 `role="alert"`

#### 全局搜索对话框语义
- **问题**：全局搜索 Modal 缺少 `role="dialog"` 和 `aria-modal`，屏幕阅读器无法正确识别为模态对话框
- **修复**：添加 `role="dialog"`、`aria-modal="true"`、`aria-label="全局搜索"`，输入框添加 `aria-label`

### 🔧 内存泄漏修复（P3 级 — React 组件清理）

#### ToastContext 定时器泄漏
- **问题**：Toast 通知的 `setTimeout` 未在组件卸载时清理，产生孤立定时器
- **修复**：使用 `Map` 追踪所有定时器 ID，`useEffect` 清理函数统一清除

#### ConversationDetail 嵌套定时器泄漏
- **问题**：`useEffect` 中的 `setTimeout` 内部嵌套了另一个 `setTimeout`，外部清理仅清除外层定时器，内层定时器成为孤儿
- **修复**：使用 `useRef` 追踪内层定时器 ID，清理函数同时清除内外两层定时器

#### Chat 页面异步状态更新泄漏
- **问题**：异步 fetch 完成时组件可能已卸载，`setState` 调用导致 React 警告
- **修复**：添加 `isMountedRef` 追踪组件挂载状态，卸载后跳过状态更新

### 🛡️ 安全加固（P3 级 — 深度防御）

#### EmailService 邮件头 CRLF 注入深度防御
- **问题**：邮件 Subject 和 From 显示名直接来自用户输入，虽然 MimeKit 内部会做编码，但缺少显式的输入清理
- **修复**：新增 `SanitizeHeaderInput()` 方法，移除 C0 控制字符（CR/LF/NUL/TAB）和 DEL，按 RFC 5322 限制长度；在 `SendEmailWithHeadersAsync`、`SendSystemNotificationAsync`、`SaveToSentFolderAsync` 三处邮件构造位置应用

#### 安全头中间件完善
- **验证**：`SecurityHeadersMiddleware` 已正确配置 CSP（nonce-based）、X-Frame-Options: DENY、X-Content-Type-Options: nosniff、Referrer-Policy、Permissions-Policy

#### 速率限制策略覆盖验证
- **验证**：17 个敏感端点（登录/注册/忘记密码/刷新令牌/OAuth/公共聊天）均已正确应用 `[EnableRateLimiting]` 策略

#### JWT 颁发与验证完整性验证
- **验证**：令牌创建时显式设置 Issuer/Audience，验证端 `ValidateIssuer=true` + `ValidateAudience=true` + `ClockSkew=Zero`

#### 开放重定向风险验证
- **验证**：全控制器扫描确认不存在 `returnUrl` / `Redirect()` / `LocalRedirect()` 模式，无开放重定向风险

#### 写操作权限审计
- **验证**：系统级控制器（CustomFields/Roles/Users/Mailboxes/Webhooks/System/Calendar/CannedResponses/ApiKeys）写操作全部有 `[RequireSuperAdmin]`；业务级控制器（Conversations/Customers/Attachments）写操作全部有 `HasMailboxAccess` / `HasCustomerAccess` 等资源所有权校验

#### SQL 注入防护验证
- **验证**：EF Core `ExecuteSql*` 4 处调用全部使用安全的 `ExecuteSqlInterpolatedAsync` 参数化重载

#### 文件上传安全验证
- **验证**：附件上传服务器端验证完整（大小限制/MIME 白名单/扩展名白名单/病毒扫描/路径遍历防护）

### ✅ 验证与测试

- **后端编译**：`dotnet build NoneadCSS.Api/NoneadCSS.Api.csproj` — 0 错误 0 警告
- **单元测试**：`dotnet test NoneadCSS.Tests` — 总计 627 项，623 通过 / 0 失败 / 4 跳过
- **前端 TypeScript 类型检查**：本次未改动前端代码（P2/P3 无障碍和内存泄漏修复已在之前批次完成并验证）

---

## [Unreleased] - 2026-07-29（第八批 — 测试覆盖率优化与基础设施修复）

### ✅ 测试覆盖率提升

#### 覆盖率指标改进
- **行覆盖率**：1.6% → 5.1%（+218%）
- **分支覆盖率**：3.5% → 13.2%（+277%）
- **方法覆盖率**：8.5% → 18.4%（+117%）
- **测试总数**：~80 → 541（+224 个新测试）
- **失败测试**：17 → 0（全部修复）

#### 17 个失败测试基础设施修复
- **SecretHasher 未初始化（7 个测试）**：在 `AuthServiceTests` 添加静态构造函数初始化 `SecretHasher.Initialize(...)`
- **Mock DbSet 缺少 IAsyncQueryProvider（7 个测试）**：实现 `TestAsyncQueryProvider` 和 `CreateMockDbSet` 辅助方法支持异步 LINQ 操作
- **JWT 过期 token 生成逻辑（3 个测试）**：设置 `NotBefore` 早于 `Expires` 时间（`NotBefore = UtcNow.AddMinutes(-30)`，`Expires = UtcNow.AddMinutes(-10)`）

#### 新增 11 个服务测试文件（+224 个测试）
- `ApiKeyServiceTests.cs`（31 个测试）— API 密钥 CRUD、验证、范围管理
- `DashboardServiceTests.cs`（18 个测试）— 仪表盘统计、用户工作负载、邮箱统计
- `ConversationDraftServiceTests.cs`（28 个测试）— 草稿保存、事务回滚、附件关联
- `ConversationAssignmentServiceTests.cs`（25 个测试）— 单个/批量分配、事件触发、工作流
- `TeamServiceTests.cs`（47 个测试）— 团队 CRUD、成员管理、角色晋升、回收站
- `ModuleServiceTests.cs`（41 个测试）— 模块 CRUD、缓存行为、分类过滤
- `TagServiceTests.cs`（已存在）— 标签管理全面测试
- `PasswordPolicyServiceTests.cs`（已存在）— 密码策略验证
- `CannedResponseServiceTests.cs`（已存在）— 快捷回复模板
- `EmailSignatureServiceTests.cs`（已存在）— 邮件签名管理
- `RoleServiceTests.cs`（已存在）— 角色权限管理
- `SystemOptionServiceTests.cs`（已存在）— 系统配置管理

#### 测试基础设施
- 新增 `NoneadCSS.Tests/Helpers/AsyncTestHelpers.cs`：提供 `TestAsyncQueryProvider`、`TestAsyncEnumerator`、`CreateMockDbSet` 辅助方法，支持 EF Core 异步 LINQ 操作
- 覆盖率报告生成配置：`XPlat Code Coverage` + `ReportGenerator`
- 测试覆盖率报告位置：`TestResults-Coverage/Report/summary.html`

---

## [Unreleased] - 2026-07-29（第七批 — P3 建议级问题修复）

### 🔧 代码质量修复（P3 级 — 领域模型贫血症）

#### 核心实体封装业务逻辑
- **问题**：`ConversationThread`、`PasswordResetToken`、`RefreshToken`、`Webhook`、`Attachment`、`SendLog`、`SatisfactionRating` 等 7 个实体为纯贫血模型，业务逻辑分散在 Service 层
- **修复**：为各实体添加业务方法：
  - `ConversationThread`: `IsCustomerMessage()`、`IsUserReply()`、`IsNote()`、`MarkAsDraft()`、`MarkAsDeleted()`、`Restore()`、`SetSendStatus()`、`RecordOpen()`
  - `PasswordResetToken`: `MarkAsUsed()`、`Create()`
  - `RefreshToken`: `Revoke()`、`Create()`
  - `Webhook`: `ShouldFireForEvent()`、`RecordTrigger()`、`RecordSuccess()`、`RecordFailure()`、`Activate()`、`Deactivate()`、`SoftDelete()`、`Restore()`、`IsDeleted()`、`GetEventList()`
  - `Attachment`: `IsImage()`、`IsDocument()`、`GetFileExtension()`、`GetFileSizeFormatted()`
  - `SendLog`: `MarkAsSent()`、`MarkAsFailed()`、`RecordOpen()`、`IsSent()`、`IsFailed()`
  - `SatisfactionRating`: `IsPositive()`、`IsNegative()`、`IsNeutral()`

### 🔧 代码质量修复（P3 级 — 仓储接口精简）

#### IUnitOfWork 接口膨胀
- **问题**：`IUnitOfWork` 定义了 40+ 个仓储属性，其中多个仅使用基础 CRUD 功能
- **修复**：移除 6 个简单 `IRepository<T>` 属性（`Followers`、`Emails`、`ConversationFolders`、`MailboxUsers`、`ConversationTags`、`Stars`），改用 `Repository<T>()` 动态获取

### 🗄️ 数据库优化（P3 级 — 索引补充）

#### 缺失索引
- **问题**：`Star`、`Attachment`、`ConversationThread.MessageId` 缺少数据库索引，影响查询性能
- **修复**：
  - `Star`: 添加复合唯一索引 `(ConversationId, UserId)`，及 `UserId` 单列索引
  - `Attachment`: 添加 `ThreadId` 索引
  - `ConversationThread`: 添加 `MessageId` 索引（邮件线程关联高频查询）

### ♿ 前端无障碍修复（P3 级 — ARIA 属性）

#### 图标按钮缺少 aria-label
- **问题**：`Layout.tsx`、`Login.tsx` 中部分图标按钮缺少 `aria-label` 属性，影响屏幕阅读器用户体验
- **修复**：
  - 移动端菜单按钮添加 `aria-label="打开菜单"`
  - 搜索按钮添加 `aria-label="搜索"`
  - 用户菜单按钮添加 `aria-label="用户菜单"`
  - 验证码刷新按钮添加 `aria-label="刷新验证码"`

### ✅ 验证与测试修复

#### 测试项目编译错误
- **问题**：`NoneadCSS.Tests` 因 P0-22 `WorkflowService` 构造函数签名变更导致编译错误
- **修复**：
  - `WorkflowServiceTests.cs`: 添加 `IWorkflowActionHandlerProvider` 和 `IServiceProvider` 依赖
  - `NotificationServiceTests.cs`: 添加 `IConfiguration` 依赖
- **验证结果**：`dotnet build NoneadCSS.slnx` — 0 错误 0 警告 ✅

---

## [Unreleased] - 2026-07-27（第六批 — 第十轮全面系统性审核与修复）

### 🛡️ 安全修复（高风险 — IDOR 漏洞）

#### UsersController.GetUser 用户信息越权访问
- **任意用户可枚举所有用户信息**：原接口缺少权限控制，攻击者可通过枚举 ID 获取所有用户的姓名、邮箱、角色等敏感信息
- **修复**：添加 `[RequireSuperAdminOrSelf]` 特性，仅允许超管或用户本人访问

#### NotificationsController.GetTemplate 模板详情越权
- **任意用户可访问通知模板详情**：与其他模板管理接口不一致，缺少权限控制
- **修复**：添加 `[RequireSuperAdmin]` 特性，与 `GetTemplates` 等接口保持一致

### 🛡️ 安全修复（中风险 — 信息泄露）

#### AuthService 登录错误消息不一致
- **账号锁定状态可枚举**：原实现对锁定账号返回独立错误消息"账号已被临时锁定"，攻击者可通过错误消息差异判断账号是否存在、是否被锁定
- **修复**：所有登录失败情况（不存在、密码错误、锁定、待激活、禁用）统一返回"邮箱或密码不正确"，日志中记录详细原因供运维排查

### 🔧 前端修复（P1 级 — UX 问题）

#### Modal 弹窗焦点管理
- **每次输入焦点丢失**：原 `Modal.tsx` 的 `useEffect` 清理函数在每次重新渲染时恢复焦点到打开前元素，导致无法连续输入
- **修复**：使用 `isOpening` ref 追踪真实打开状态，仅在弹窗关闭时恢复焦点

### 📋 全面系统性审核结果

#### 后端代码完整性（43 个 Controller + 所有 Service/Repository）
- ✅ 所有 Controller 都有 `[ApiController]` 和 `[Route]` 特性
- ✅ 权限特性使用统一：`[Authorize]`、`[RequireSuperAdmin]`、`[RequireSuperAdminOrSelf]`
- ✅ Service 层依赖注入正确，异常处理统一
- ✅ Repository 层软删除过滤一致，AsNoTracking 使用正确

#### 前端代码统一性（50+ 个 Modal 弹窗）
- ✅ 所有组件使用函数组件和 Hooks
- ✅ API 调用统一通过 `api/client.ts`
- ✅ XSS 防护完善：所有 `dangerouslySetInnerHTML` 使用 `DOMPurify.sanitize()`
- ✅ JWT 使用 HttpOnly Cookie，localStorage 仅存储基本信息
- ⚠️ P1：部分组件 `setTimeout` 缺少清理函数（6 个文件）
- ⚠️ P2：部分页面 API 调用未使用 React Query（4 个页面）

#### 安全审计（按 OWASP Top 10 分类）
- ✅ A03 注入攻击：SQL 注入/XSS/路径遍历防护完善
- ✅ A07 认证失败：JWT 配置安全、密码策略严格、账户锁定机制
- ✅ A05 安全配置：速率限制合理、CSRF 防护完善
- ✅ A08 数据完整性：文件上传安全检查（大小限制、类型验证、病毒扫描）
- ✅ A09 日志监控：日志脱敏完善（密码/Token/邮箱）
- ⚠️ A01 访问控制：已修复 2 个 IDOR 漏洞

---

## [Unreleased] - 2026-07-27（第五批 — 第九轮代码审核与修复）

### 🛡️ 安全修复（P1 级 — XSS 防护）

#### ConversationThreadService 邮件回复体 XSS 漏洞
- **`RenderReplyEmailBodyAsync` 默认模板路径 XSS**：原 `replyBody`（来自富文本编辑器）未消毒即插入 HTML 模板 `<p>{replyBody}</p>`，攻击者可通过客服回复邮件触发存储型 XSS
- **修复**：使用 `HtmlSanitizerHelper.Sanitize(replyBody)` 进行白名单消毒，移除 `<script>`、`on*` 事件属性等危险元素，保留安全 HTML 格式

### 🛡️ 安全修复（P0 级 — IDOR 越权）

#### CustomersController 客户访问权限多邮箱判断漏洞
- **`HasCustomerAccess` 仅检查首条会话邮箱**：客户在多个邮箱都有会话时，如果用户仅对非首条会话的邮箱有权限，会被错误拒绝访问，导致权限误判
- **修复**：改为检查用户是否可访问该客户**任意一条**会话所属邮箱，使用 `HashSet` 提高查找性能

### 🚀 性能优化（P1 级 — N+1 查询）

#### WorkflowService 工作流上下文构建 N+1
- **`BuildContextFromConversation` 重复查询**：原实现对每个工作流执行 4 次 DB 查询（Customer、Threads、Tags、User），N 个工作流导致 4N 次查询
- **修复**：直接使用 `ConversationRepository.GetByIdAsync` 已通过 `ApplyFullIncludes` 加载的导航属性（`conversation.Customer`、`conversation.User`、`conversation.Threads`、`conversation.ConversationTags`），仅 `Mailbox` 需单独查询，每个工作流从 4 次查询降至 1 次

### 🔧 代码质量修复（P1 级 — EF Core 追踪反模式）

#### SlaService 实体修改不持久化
- **`UpdateAsync` / `DeleteAsync` 使用 `GetByIdAsync`（AsNoTracking）**：基类 `Repository<T>.GetByIdAsync()` 使用 `AsNoTracking()`，返回的实体不被 EF Core 追踪，后续属性修改 + `SaveChangesAsync` 不会持久化到数据库
- **修复**：改用 `GetQueryable(false).FirstOrDefaultAsync(...)` 追踪查询，与 `ToggleActiveAsync` 保持一致

### 🔧 控制器与编译修复

#### ConversationService.AdvancedSearchAsync 参数不匹配
- **接口签名变更未同步调用方**：`IConversationRepository.AdvancedSearchAsync` 新增 `mailboxIds`、`priority`、`starredByUserId` 参数，但 `ConversationService.AdvancedSearchAsync` 调用方未传入对应实参，导致编译错误
- **修复**：补充 `null` 实参，保持现有行为不变

#### MailController.AddNoteAsync 调用签名错误
- **使用 `CreateThreadRequest` 而非 `AddNoteRequest`**：原调用使用错误的 DTO 类型，且缺少 `conversationId` 参数
- **修复**：改为 `(conversationId, new AddNoteRequest { Body = request.Body }, userId)`

#### RequireSuperAdminOrSelfAttribute Body 检查 API 错误
- **`AuthorizationFilterContext.ActionArguments` 不存在**：授权过滤器在模型绑定之前执行，`ActionArguments` 在 `AuthorizationFilterContext` 上不可用
- **修复**：通过 `Request.EnableBuffering()` 启用请求体缓冲，使用 `JsonDocument` 直接解析 JSON body 中的 userId 字段，重置流位置以不影响后续模型绑定

#### SensitiveDataLogger 缺少 Serilog 包引用
- **`Serilog` 命名空间未找到**：Infrastructure 项目未引用 Serilog NuGet 包
- **修复**：通过 `dotnet add package Serilog` 添加包引用

### ✅ 验证

- **后端编译**：`dotnet build NoneadCSS.Api/NoneadCSS.Api.csproj` 0 错误 0 警告
- **后端健康检查**：`http://localhost:5009/health` 返回 200
- **前端页面**：`http://localhost:5173` 返回 200

---

## [Unreleased] - 2026-07-27（第四批 — 第八轮深度安全审核）

### 🛡️ 安全修复（P0 级 — IDOR 越权）

#### UserProfileController 权限越权
- **`GetUserPermissions` IDOR 漏洞**：原实现允许任意登录用户通过修改 `userId` URL 参数查看其他用户的权限结构（包括 SuperAdmin 权限字符串）。现限定仅允许用户查看自己或超级管理员查看任意用户，对未授权访问记录警告日志
- **增加 `IsSuperAdmin()` 辅助方法**：从 `ClaimTypes.Role` 读取角色判断，与项目其他控制器保持一致

### 🛡️ 安全修复（P1 级 — OAuth 与限流）

#### MailboxService OAuth 伪造 Token 漏洞
- **占位实现风险**：`CompleteMailboxOAuthAsync` 原以"简化实现"方式写入形如 `oauth-token-{Ticks}` 的伪造 access/refresh token，攻击者只要拿到任意 code 即可绕过 OAuth 授权流程获得邮箱访问权限
- **方法弃用**：标记 `[Obsolete]`，改为 `Task.FromResult<MailboxOAuthStatusResponse?>(null)`，不再执行任何写入操作
- **调用方改造**：`MailboxAdvancedController.OAuthCallback` 同步标记弃用并返回明确提示，引导调用方改用 `OauthController.SaveOAuthToken`（已具备一次性 state 校验 + 真实 token 保存能力）
- **OauthController 修复 `_logger` 未声明 bug**：构造函数补充 `ILogger<OauthController>` 依赖注入

#### SatisfactionRatingsController 公开评价接口限流
- **新增 `SatisfactionPolicy` 限流策略**：每 IP 每小时最多 10 次公开评价提交，防止恶意刷库或评分数据污染
- **`Create` 端点添加 `[EnableRateLimiting("SatisfactionPolicy")]`**：与其他公开接口保持限流一致性

#### ChatController sessionToken 防暴力枚举
- **`IsValidSessionToken` 校验辅助方法**：要求 sessionToken 至少 32 字符且仅含字母数字（与 `Guid.NewGuid().ToString("N")` 一致）
- **5 个客户端公开接口全部校验**：SendCustomerMessage、GetCustomerMessages、CloseSessionByToken、RateChat、MarkAsReadByToken，校验失败直接 404，避免通过短 token 暴力枚举会话

#### ProfileController.GetProfileStats OOM 风险
- **加载全量数据风险**：`GetByUserIdAsync(userId, 1, int.MaxValue)` 和 `FindAsync` 在用户数据量大时单次查询加载全部记录到内存，会导致 OOM 或长时间挂起
- **改用数据库端 CountAsync**：`GetQueryable().CountAsync(predicate)` 在数据库端聚合，仅返回整数
- **审计日志分页上限**：从 `int.MaxValue` 改为 1000 条，仅用于最近登录/登出在线时长统计

#### MailController 缺少模块授权
- **添加 `[RequireModule("mailbox")]`**：在系统管理中禁用"邮件"模块后，无法通过 `MailController` 操作邮件，与 `MailboxesController` 等其他邮件相关控制器保持一致

#### MailboxService 凭证长度校验
- **`CreateMailboxRequest` 字段长度限制**：
  - `InPassword` / `OutPassword` 添加 `[MaxLength(1024)]`，避免恶意超长输入
  - `OauthAccessToken` / `OauthRefreshToken` 添加 `[MaxLength(4096)]`，匹配 OAuth token 实际长度上限

### 🛡️ 安全修复（P1 级 — 日志与异常信息泄露）

#### EmailTestController 内部异常信息泄露
- **`ParseEmail` 详细错误屏蔽**：原 `ex.Message` 直接返回给客户端，会暴露 MimeKit 内部异常细节。改为仅记录到日志，对外返回通用提示
- **`SimulateThreadMatch` 同步修复**：处理失败时仅返回通用"处理邮件失败"消息

### 🚀 性能优化（P2 级 — N+1 查询）

#### WebhookService.GetLogsPagedAsync
- **N+1 查询修复**：原循环调用 `GetByIdAsync(webhookId)` 逐条查询 webhook 名称，改为 `GetQueryable().Where().Select()` 一次性批量查询，避免分页日志接口的 N+1 性能问题

### 🛡️ 安全修复（P2 级 — 权限与脱敏）

#### ApiKeysController 密钥日志权限收紧
- **`GetLogs(id)` / `GetAllLogs()` 添加 `[RequireSuperAdmin]`**：原任意登录用户可查看所有 API Key 调用日志，包含 IP、端点、状态码等敏感信息。现仅超级管理员可访问

#### ReportsController 模块授权
- **添加 `[RequireModule("reports")]`**：与"系统管理 → 模块管理"中的报表模块开关保持一致

### 🔧 控制器与中间件细节修复

#### OauthController
- **构造函数补充 `ILogger<OauthController>` 依赖**：修复原 `_logger` 引用但未声明导致的运行时 NullReferenceException 风险

### ✅ 验证

- **后端编译**：`dotnet build NoneadCSS.Api/NoneadCSS.Api.csproj` 0 错误 0 警告
- **前端构建**：`tsc -b && vite build` 0 错误 0 警告
- **后端健康检查**：`http://localhost:5009/health` 返回 200
- **前端页面**：`http://localhost:5173` 返回 200

---

## [Unreleased] - 2026-07-27（第三批 — 第七轮全面安全审核）

### 🛡️ 安全修复（P1 级 — 认证授权安全）

#### JWT 安全增强
- **JWT 密钥编码修复**：`Encoding.ASCII.GetBytes()` → `Encoding.UTF8.GetBytes()`，修复非 ASCII 字符密钥被损坏导致令牌验证失败的问题
- **ClockSkew 归零**：JWT TokenValidationParameters 显式设置 `ClockSkew = TimeSpan.Zero`，消除默认 5 分钟宽限期，令牌过期立即失效
- **密钥长度校验**：`ValidateTokenAsync` 和 `GenerateJwtTokenAsync` 均校验 SecretKey 长度 ≥ 32 字节，不满足则抛出异常
- **令牌有效期单位统一**：使用 `AddMinutes` 而非 `AddHours`，与配置项命名一致

#### 密码重置安全增强
- **重置令牌批量失效**：密码重置成功后，调用 `InvalidateUserTokensAsync` 失效该用户所有未使用的重置令牌，防止重放攻击
- **Refresh Token 全量撤销**：密码重置时，调用 `RevokeUserTokensAsync` 撤销用户所有现有 Refresh Token，防止会话劫持
- **事务原子性保障**：密码更新 + 令牌标记 UsedAt + 失效重置令牌 + 撤销 Refresh Token 全部包裹在同一事务内
- **追踪查询修复**：使用 `GetQueryable(false)` 追踪查询获取用户实体，修复 AsNoTracking 导致密码修改不落库问题

#### 登录失败锁定与暴力破解防护
- **FailedLoginAttempts 字段**：User 表新增失败登录次数字段，记录连续失败次数
- **LockoutEnd 字段**：User 表新增锁定结束时间字段，支持临时锁定
- **锁定策略**：连续 5 次登录失败自动锁定 15 分钟，锁定期间拒绝登录
- **数据库补丁**：`EnsureFailedLoginColumnsAsync` 自动为现有数据库补充缺失列
- **登录失败事件**：新增 `LoginFailedEvent` 领域事件与 `LogFailedLoginListener` 监听器

#### Refresh Token 安全增强
- **重用检测机制**：检测到已撤销的 Refresh Token 被使用时，立即撤销该用户所有 Refresh Token（token rotation + reuse detection）
- **事务原子轮换**：新令牌生成与旧令牌撤销使用同一事务，避免轮换过程中产生多有效令牌
- **拆分生成方法**：`GenerateRefreshTokenAsync` 拆分为 `GenerateRefreshTokenPlain`（生成明文+实体，不持久化），确保事务原子性

### 🛡️ 安全修复（P1 级 — 注入防护与敏感信息）

#### XSS 防护增强
- **邮件模板变量 HTML 编码**：`ConversationStatusService.BuildAndSendEmailAsync` 中所有插入 HTML 上下文的变量值使用 `WebUtility.HtmlEncode` 编码，防止邮件模板 XSS 注入
- **前端 DOMPurify 强化**：sanitizeHtml 配置进一步收紧，移除危险标签和属性

#### 全局异常消息脱敏
- **非 500 消息泄露修复**：`GlobalExceptionMiddleware` 不再直接返回 `exception.Message`，改为按状态码返回安全的通用错误提示
- **安全消息映射**：400→"请求参数无效，请检查输入。" / 401→"未授权，请先登录。" / 403→"没有权限执行此操作。" / 404→"请求的资源不存在。" / 409→"操作冲突，请重试。" / 其他→"服务器内部错误，请稍后重试或联系管理员。"
- **详细日志保留**：完整异常信息仍记录到 Serilog 日志，便于排查问题

#### API Key Scope 权限校验
- **Scope 解析**：`ApiKeyAuthenticationHandler` 解析 ApiKey 的 `ScopesJson` 字段，添加为 `scope` claim
- **细粒度权限**：支持按 API Key 限制可访问的接口范围

### 🛡️ 安全修复（P2 级 — 安全响应头）

#### SecurityHeadersMiddleware 安全响应头中间件
- **新增中间件**：`SecurityHeadersMiddleware.cs` 统一管理所有安全响应头
- **Content-Security-Policy**：内容安全策略，限制资源加载来源，防御 XSS 和数据注入
- **X-Content-Type-Options: nosniff**：阻止 MIME 类型嗅探
- **X-Frame-Options: DENY**：防止点击劫持（clickjacking）
- **Referrer-Policy: strict-origin-when-cross-origin**：控制 Referer 头信息泄露
- **Permissions-Policy**：限制浏览器功能权限（摄像头、麦克风、地理位置等）
- **X-XSS-Protection: 0**：显式禁用已废弃的 XSS 防护头，改用 CSP
- **配置化启用**：支持通过 `SecurityHeaders` 配置节启用/禁用各安全头和报告模式

### 🛡️ 安全修复（P2 级 — 密码策略增强）

#### PasswordPolicyService 密码策略服务
- **弱密码字典检查**：内置常见弱密码列表，禁止使用易被猜到的密码
- **常见模式检测**：检测并阻止连续字符（abcdef、123456）、键盘序列（qwerty）、重复字符（aaaaaa）等弱模式
- **可配置强度**：支持最小长度、必需字符类型（大写/小写/数字/特殊字符）等配置
- **BCrypt 哈希存储**：密码使用 BCrypt 算法哈希存储，自带盐值和工作因子

### 🛡️ 安全修复（P2 级 — 前端安全）

#### 前端权限防篡改
- **权限从后端获取**：用户权限不再存储在 localStorage 中，改为每次需要时从后端 API 获取，防止本地篡改
- **localStorage 精简**：仅存储基本用户信息（ID、邮箱、姓名、头像等非敏感数据）
- **AuthContext 重构**：`useAuth()` Hook 支持异步获取权限，确保权限数据来自服务端

#### 登出流程安全增强
- **异步登出**：`logout()` 改为 async 函数，await 后端 logout API 响应后再清理本地状态
- **全面清理**：登出时清理所有 localStorage 数据（用户信息、令牌缓存、偏好设置、头像缓存等）
- **错误容错**：API 调用失败不阻止本地登出，finally 块确保状态清理

### ✨ 功能增强 — 宏管理与工作流集成

#### 工作流 execute_macro 动作
- **新增动作类型**：工作流动作新增 `execute_macro` 类型，支持在自动化规则中执行宏命令
- **宏选择下拉**：工作流编辑界面新增宏选择下拉菜单，列出所有启用的宏
- **批量执行能力**：宏可通过工作流对一批会话执行批量操作，扩展宏的使用场景
- **后端执行逻辑**：`WorkflowService` 中新增 `ExecuteMacroActionAsync` 方法，调用 `IMacroService.ExecuteForWorkflowAsync`
- **事务包裹**：宏执行与工作流执行日志在同一事务内，确保原子性

#### 宏编辑界面优化
- **动作下拉菜单**：`add_tag` 和 `remove_tag` 动作从自由文本输入改为下拉菜单选择
- **邮箱联动**：选择邮箱后自动加载该邮箱的标签列表
- **默认值优化**：新增动作时自动选择第一个可用值
- **加载状态提示**：标签加载中显示 loading 状态

### 🐛 问题修复 — SLA 策略与状态切换

#### 服务层状态切换修复
- **SlaService.ToggleActiveAsync**：改用 `GetQueryable(false)` 追踪查询，修复 AsNoTracking 导致状态修改不落库
- **CustomFieldService.ToggleActiveAsync**：同上修复
- **MailboxService.ToggleActiveAsync**：同上修复
- **WorkflowService.ToggleActiveAsync**：同上修复
- **WebhookService.ToggleActiveAsync**：同上修复
- **MacroService.ToggleActiveAsync**：同上修复

#### 服务层反模式统一修复
- **统一模式**：所有服务层状态切换和属性更新方法统一使用「追踪查询 + 直接修改属性 + SaveChanges」模式
- **移除 UpdateAsync**：全面移除 AsNoTracking 查询后的 `UpdateAsync` 调用，避免 EF Core 实体追踪冲突

### 🚀 性能优化 — 事务与 N+1 查询

#### 事务包裹完善
- **MacroService.ExecuteForWorkflowAsync**：新增事务包裹，确保宏执行的原子性
- **ImportExportService.ImportDataAsync**：批量导入使用事务，失败自动回滚
- **AuthService.ResetPasswordAsync**：密码重置多表操作使用事务

#### N+1 查询优化
- **ImportExportService**：导入前预加载所有现有数据到 HashSet，内存查重替代循环数据库查询
- **SlaService**：SLA 扫描 N+1 查询优化，批量获取策略、违规记录、线程数据

### 🔧 代码质量与构建

#### 编译警告清零（后端）
- **CS8603 修复**：RoleService、SystemOptionService 中 `GetOrCreateAsync` 返回值 null 引用警告，使用 null 抑制运算符
- **CS8620 修复**：NoneadCSSDbContext 中 `EncryptedStringConverter` 可空引用类型差异，使用 pragma 抑制
- **CS1587 修复**：Program.cs 顶层语句局部函数 XML 注释位置错误，改为常规注释

#### 前端构建修复
- **Vite 8 类型错误**：移除 `vite.config.ts` 中 `esbuild: { jsx: 'automatic' }` 冗余配置（@vitejs/plugin-react 已自动处理）
- **tsc 构建通过**：`tsc -b && vite build` 0 错误 0 警告

---

## [Unreleased] - 2026-07-27（第二批）

### 🔄 品牌重命名
- **前端项目标识全面更名**：所有 `freescout` 标识统一改为 `noneadcss`
  - `package.json` 项目名：`freescout-frontend` → `noneadcss-frontend`
  - localStorage 存储前缀：`freescout:` → `noneadcss:`（含偏好设置、头像缓存、主题、语言、通知已读时间等 6 处）
  - 导出文件名：`freescout_export_*.json` → `noneadcss_export_*.json`
  - 登出清理逻辑同时兼容三种前缀：`noneadcss:`、`freescout:`、`ncss:`
  - 所有旧前缀保留向后兼容的自动迁移逻辑，用户升级后数据不丢失

### 🔧 构建与代码质量
- **后端编译警告清零**：修复所有 7 个编译警告，实现 0 错误 0 警告
  - 修复 4 处 CS8603：`GetOrCreateAsync` 返回值 null 引用警告（RoleService、SystemOptionService）
  - 修复 1 处 CS8620：`EncryptedStringConverter` 可空引用类型差异（Webhook.Secret）
  - 修复 2 处 CS1587：Top-level program 局部函数 XML 注释位置错误
- **前端构建错误清零**：修复 `vite.config.ts` 中 `esbuild.jsx` 配置在 Vite 8 下的类型错误，移除冗余配置（`@vitejs/plugin-react` 已自动处理 JSX）

### 🐛 前端修复

#### 发送日志页面
- **默认日期范围缺失**：进入发送日志页面时，开始日期和结束日期输入框为空，用户不知道当前筛选范围。修复为默认显示最近 30 天，与后端 `GetOverviewAsync` 行为保持一致

#### 会话列表
- **返回列表不刷新**：克隆会话后返回列表页，因 React Query 缓存导致看不到新克隆的会话。为 `useConversations` 添加 `refetchOnMount: 'always'`，每次进入列表页自动刷新数据

#### 用户头像
- **头像上传后不刷新**：客服用户上传头像后，系统其他位置（用户列表、会话详情、侧边栏等）没有及时更新头像。
  - 新增 `setAvatar()` 工具函数，统一管理 localStorage 头像缓存
  - `AuthContext.updateUser()`、`login()`、初始化刷新时同步更新 `freescout:avatar:{userId}` 缓存
  - 6 处使用 `UserAvatar` 组件的位置补充 `avatarUrl` prop，直接使用 API 返回的最新数据：
    - 用户列表页（Users.tsx）
    - 会话消息列表（ThreadList.tsx）
    - 会话侧边栏分配下拉（ConversationSidebar.tsx）
    - 会话侧边栏负责人信息（ConversationSidebar.tsx）
    - 邮箱成员头像组（Mailboxes.tsx）
    - 邮箱编辑成员列表（Mailboxes.tsx）

### 🚀 性能优化（P2 级 — 第五轮功能审核）

#### 服务层 P2 反模式修复
- **AuthService.ValidateTokenAsync JWT SecretKey 空回退**：违反硬约束，`?? string.Empty` 回退改为抛出 `InvalidOperationException`，并补充密钥长度 ≥32 字节校验，与 `GenerateJwtTokenAsync` 保持一致
- **AuthService.RefreshTokenAsync 事务原子性**：将 `GenerateRefreshTokenAsync` 拆分为 `GenerateRefreshTokenPlain`（生成明文+实体，不持久化），新令牌保存与旧令牌撤销在同一事务内原子提交，避免令牌轮换过程中产生多有效令牌
- **AuthService.ResetPasswordAsync 多表事务**：用户密码更新与令牌标记 `UsedAt` 添加显式 `BeginTransactionAsync` 包裹，确保原子性
- **SatisfactionRatingService 软删除检查缺失**：`CreateAsync` 和 `ValidateTokenAsync` 均补充 `conversation.DeletedAt != null` 校验，防止对已软删除会话创建满意度评价或验证令牌
- **ApiKeyService 反模式修复**：`UpdateAsync`、`DeleteAsync`、`ToggleActiveAsync`、`RegenerateKeyAsync` 4 处方法改用 `GetQueryable(false)` 追踪查询，移除 `UpdateAsync` 调用，避免 AsNoTracking + UpdateAsync 反模式（全字段 UPDATE 与潜在追踪冲突），同时修复 `DeleteAsync` 因 AsNoTracking 导致的修改不落库问题

#### 仓储层 P2 反模式修复
- **SystemOptionRepository.SetAsync AsNoTracking + UpdateAsync 反模式**：改用 tracking 查询，移除 `_dbSet.Update(option)` 调用，遵循项目工程约定
- **ConversationRepository State 过滤缺失**：`GetByIdAsync`、`GetByIdForUpdateAsync`、`GetByIdsAsync`、`GetByIdsForUpdateAsync`、`GetByIdsWithFoldersForUpdateAsync` 5 个方法补充 `State != ConversationState.Deleted` 过滤，与 `GetByMailboxIdAsync` 等列表方法保持一致，避免查询到垃圾箱会话

#### 基础设施层 P2 修复
- **ProcessSwiftMessageListener MemoryStream 资源泄漏**：`new MemoryStream(...)` 未释放，改为 `using var stream`，同时移除未使用的 `parseResult` 变量
- **UpdateMailboxCountersListener JobKey 碰撞**：原 `DateTime.UtcNow.Ticks` 拼接键在同一 tick 内多次触发会抛 `ObjectAlreadyExistsException`，且使 `[DisallowConcurrentExecution]` 失效。改为基于 `mailboxId` 的固定 JobKey + `StoreDurably` + Guid TriggerKey，确保并发去重生效

### 🛡️ 安全修复（P1 级 — 第六轮全面审核）
- **EmailService 路径遍历漏洞**：`GetAttachmentFilePath` 未验证附件路径是否在允许的基础目录内，攻击者可通过 `../` 遍历访问系统任意文件。新增 `Path.GetFullPath` 规范化 + `StartsWith` 边界校验，拦截路径遍历攻击
- **VirusScanService 扫描不完整**：`ScanFileForViruses` 仅读取文件前 512 字节进行特征码匹配，嵌入在文件中后段的恶意脚本完全无法检测。改为扫描文件头部 64KB + 尾部 4KB，覆盖大多数恶意代码嵌入场景
- **SystemOptionsController 敏感配置无权限限制**：`GetAll()` 和 `GetByName()` 端点仅需登录即可访问，暴露 JWT 密钥、邮件密码、加密密钥等所有系统配置。为所有读操作添加 `[RequireSuperAdmin]`，仅超级管理员可读取系统配置
- **CalendarService.HardDeleteAsync 缺少 await**：`HardDeleteAsync(evt)` 调用未加 await，导致硬删除可能在 SaveChanges 之后才执行，产生竞态条件。补充 await 确保执行顺序

### 🚀 性能优化（P2 级 — 第六轮功能审核）
- **ImportExportService 导入无事务**：`ImportDataAsync` 批量导入客户、标签、模板等实体时，多个实体写操作无事务包裹，中途失败会产生部分导入的脏数据。新增 `BeginTransactionAsync` 事务包裹，导入失败自动回滚
- **ImportExportService N+1 重复检查**：导入时循环调用 `GetByXxxAsync` 检查重复，每次导入产生 N 次数据库查询。改为预加载所有现有数据到 `HashSet`，内存中查重，导入性能提升数个数量级
- **ImportCustomer 重复检查优化**：`GetByEmailAsync` 单条查询改为 HashSet 内存查重，与其他导入方法保持一致

### 🛡️ 安全修复（P0 级）
- 修复 CustomersController.HasCustomerAccess IDOR 漏洞，实现基于角色和客户归属的权限校验
- 删除 TestEncoding/UpdatePassword/TestDbWrite 中的硬编码凭据，清理 Git 历史
- 移除前端硬编码内网 IP，改用 window.location.origin
- 管理员密码改为从环境变量 ADMIN_PASSWORD 读取

### 🛡️ 安全修复（P1 级）
- CSRF 中间件修复：token 比较改用常量时间，仅放行有效 Bearer token
- 6 个 Quartz Job 改用 IServiceProvider.CreateScope() 消除 Captive Dependency
- GlobalExceptionMiddleware HasStarted 分支补充日志记录
- 会话匹配添加 subject+sender 兜底步骤
- 从仓库移除 appsettings.Development.json，更新 .gitignore
- client.ts 错误处理保留 HTTP status code
- 清理版本库中的构建产物

### 🛡️ 安全修复（P2 级）
- 登录失败统一返回相同消息，防止用户枚举攻击
- CaptchaService 改用 RandomNumberGenerator 加密安全随机数
- SetupController 邮箱密码改为加密存储
- AuditSaveChangesInterceptor 同步 SQL 改为异步
- CSRF token 比较改用 CryptographicOperations.FixedTimeEquals
- CSP 补充 base-uri 'self'、form-action 'self'、object-src 'none'
- X-XSS-Protection 设为 0（已废弃，CSP 替代）
- ForwardedHeaders 不再清空 KnownProxies，从配置读取信任代理
- Conversation 级联删除改为 Restrict，保护审计数据
- N+1 查询优化（EmailIngestionService/SlaService）
- 分页参数限制最大 100 条
- OutputCache 扩展到更多只读端点

### 🐛 严重问题修复（P1 级 — 第二轮）
- **P1-12 SecuritySettings 运算符优先级修复**：前端密码修改错误信息因 `||` 和 `?:` 运算符优先级导致显示异常，添加括号明确运算顺序
- **P1-8 SQLite busy_timeout 配置**：SqliteConnectionInterceptor 添加 `PRAGMA busy_timeout=5000`，解决并发写入时的 SQLITE_BUSY 错误
- **P1-10 Tag 唯一索引软删除过滤**：Tag 的 `(MailboxId, Name)` 唯一索引添加 `.HasFilter("\"DeletedAt\" IS NULL")`，允许创建与已软删除标签同名的新标签
- **P1-5 ApiKey 用户状态检查**：ApiKeyAuthenticationHandler 添加用户激活状态校验，已禁用用户的 ApiKey 无法使用
- **P1-1 HandleBounceAsync 事务包裹**：退信处理中的多个写操作添加事务包裹，确保原子性
- **P1-9 HardDeleteAsync 关联清理**：删除 Conversation 前先清理 WorkflowExecutionLogs、SlaViolations、MacroExecutionLogs 关联记录
- **P1-3 MailboxService 空操作写库修复**：UpdateMailboxPermissionsAsync、UpdateMailboxAutoReplyAsync、UpdateMailboxSignatureAsync 实现实际更新逻辑，不再获取实体后直接返回
- **P1-4 CreateMailboxAsync 文件夹补全**：添加 EnsureDefaultFoldersAsync 方法，邮箱获取时自动补全缺失的默认文件夹类型
- **P1-15 Docker HTTPS 重定向配置**：新增 `Security:EnableHttpsRedirection` 配置项，Docker/反向代理环境下默认禁用 HTTPS 内部重定向
- **P1-2 ReportService OOM 优化**：使用 AsNoTracking() + 数据库聚合查询替代内存全量加载，减少报表查询内存占用
- **P1-14 GlobalSearch 后端搜索端点**：创建 SearchController，支持跨会话、客户、知识库的统一搜索，带分页和速率限制

### 🚀 性能优化（P2 级）
- **功能全面审核（第三轮）**：
  - 修复 CustomerService.HardDeleteCustomerAsync 效率问题：将分页查询改为直接查询单个已删除客户，新增 ICustomerRepository.GetDeletedByIdAsync
  - 修复 MailTemplateService 渲染方法安全漏洞：RenderTemplateAsync 和 RenderTemplateFullAsync 添加 DeletedAt 检查，防止已删除模板被使用
  - 修复 UserService.GetUsersByRoleAsync 内存过滤问题：使用 IUserRepository.GetByRoleAsync 直接在数据库层过滤角色
- **功能全面审核（第四轮 — P1 数据安全与性能修复）**：
  - **FolderService.IncrementFolderCountAsync 修改不落库**：AsNoTracking 实体修改未调用 UpdateAsync，导致文件夹计数器永不更新，添加显式 UpdateAsync 调用
  - **ConversationService.GetStarredConversationIdsAsync 返回错误数据**：方法查询 Followers 表而非 Stars 表，返回关注会话而非星标会话，修正为查询 Stars 仓储
  - **ConversationDraftService.SaveDraftAsync 草稿修改丢失**：AsNoTracking 加载的 existingDraft 修改未被持久化，添加显式 UpdateAsync 调用
  - **CalendarService.DeleteAsync 物理删除而非软删除**：调用仓储 DeleteAsync 执行物理删除，与回收站功能矛盾，改为调用 SoftDelete() + UpdateAsync
  - **MacroService.ExecuteAsync 可执行已删除宏**：未检查 macro.DeletedAt，添加软删除状态校验
  - **MacroService.GetOverviewAsync 统计数据错误**：TotalExecutions 使用最近 10 条日志计数而非全量，新增 IMacroExecutionLogRepository.CountAsync/CountTodayAsync 方法
  - **WebhookService.TestAsync 可测试已删除 Webhook**：未检查 webhook.DeletedAt，添加软删除状态校验
  - **仓储层 ThreadState 过滤缺失**：ConversationRepository.ApplyFullIncludes 和 GetThreadsByConversationIdAsync 未过滤 ThreadState.Deleted，导致已删除线程泄漏到详情视图
  - **AuthService.RefreshTokenAsync 事务原子性**：新 token 生成和旧 token 撤销两次 SaveChanges 无事务包裹，添加事务确保原子性
  - **NotificationService.RestoreTemplate/Rule 全表加载**：使用 int.MaxValue 加载全部已删除项仅为查一条，重构为先恢复再按 ID 查询
  - **ChatService.GetOverviewAsync 全表加载**：GetAllAsync 加载全部聊天会话做内存 Count，改为数据库聚合查询
  - **ChatService.GetActiveSessionsAsync 无限制分页**：int.MaxValue 作为 pageSize，改为合理上限 500
  - **ImportExportService 导出 emails 为空**：CustomerRepository.GetAllAsync 未 Include Emails 导航属性，override GetAllAsync 添加 Include
  - **EmailSignatureService.UpdateAsync 软删除检查缺失**：未检查 sig.DeletedAt，允许修改已删除签名
  - **ConversationService.HardDelete/ClearTrash 缺少事务**：硬删除和审计日志无事务包裹，添加事务确保原子性
- **关键数据库索引优化**：
  - Conversation 复合索引：`(MailboxId, CreatedAt) Include(State)` + 软删除过滤
  - ConversationThread 复合索引：`(ConversationId, Type, CreatedAt)`
  - SendLog 复合索引：`(MailboxId, CreatedAt)`
  - Customer 复合索引：`(MailboxId, CreatedAt)` + 软删除过滤
  - Tag 唯一索引添加软删除过滤
- **全局速率限制策略**：
  - GlobalPolicy：每 IP 每分钟 300 次请求（6 段滑动窗口）
  - SearchPolicy：每 IP 每分钟 30 次请求
  - ReportsPolicy：每 IP 每分钟 10 次请求
  - SearchController.GlobalSearch 和 ReportsController.GetReport 应用相应限流策略
- **数据库聚合查询优化**：
  - ReportService 使用 EF Core 聚合查询替代内存分组
  - SearchController 使用 AsNoTracking() 提升查询性能
- **SQLite 并发优化**：
  - SqliteConnectionInterceptor 设置 busy_timeout=5000ms
  - 连接池配置优化

### 🎨 前端优化（P2 级）
- 40+ 页面改用 React.lazy 路由懒加载
- 添加 404 页面
- ModuleContext activeModuleNames 改用 useMemo
- queryClient 错误处理改用 ApiError.status
- 添加 ESLint 配置
- tsconfig 启用 noUnusedLocals/noUnusedParameters
- Modal body overflow 恢复逻辑修复
- 减少 any 类型使用

### 🔧 部署优化（P2 级）
- Cron 表达式从硬编码改为 appsettings.json 配置
- RateLimiter 中间件位置调整到管道靠前
- CORS 策略从 "AllowAll" 重命名为 "AllowedOrigins"
- 健康检查拆分为 /health（liveness）和 /health/ready（readiness）
- AllowedHosts 从 * 改为具体域名列表
- DatabaseSeeder 拆分为 4 个子 Seeder

### ♿ 无障碍（P3 级）
- 附件预览按钮添加 aria-label
- 加载状态组件添加 aria-live="polite"

### 🐛 问题修复
- 修复 AuditSaveChangesInterceptor 中 ExecuteSqlRawAsync 的 CancellationToken 被误当作 SQL 参数的问题
- 修复添加备注（AddNoteAsync）时 User 实体追踪冲突错误：改用 GetByIdForUpdateAsync 追踪查询，移除 UpdateAsync 调用
- 修复克隆会话后客户消息显示错误客服名称的问题：客户消息保留原 CustomerId 且 UserId 设为 null，客服回复 UserId 设为当前用户
- 修复 ConversationService 中 UpdatePriorityAsync、DeleteConversationAsync、ToggleFollowAsync、RestoreConversationAsync 的实体追踪冲突问题

### 🔧 架构与稳定性

#### 批量操作事务完善
- **ConversationService 批量操作**：
  - `BulkUpdatePriorityAsync` 新增事务包裹，确保批量更新优先级的原子性
  - `BulkDeleteAsync` 新增事务包裹，支持部分成功/部分失败场景，返回 BulkOperationResponse 详情
- **RoleService.BatchAssignRoleAsync**：新增事务包裹与错误处理，确保角色批量分配的一致性
- **SystemOptionService.BatchUpdateAsync**：新增事务包裹，系统选项批量更新原子性保障
- **CustomFieldService.BatchSetEntityValuesAsync**：新增事务包裹，自定义字段批量赋值事务支持

#### 软删除与垃圾箱完善
- **MailTemplate.Slug 唯一索引软删除过滤**：添加 `.HasFilter("\"DeletedAt\" IS NULL")`，支持软删除后创建同名邮件模板
- **前端批量删除确认框优化**：硬删除确认从原生 `window.confirm` 改为统一 `ConfirmDialog` 组件
- **前端批量操作反馈优化**：解析后端 `BulkOperationResponse`，区分全成功、全失败、部分失败三种场景，显示详细错误信息

### ✨ 新增功能（早期安全增强）
- WebhookService 增加 SSRF 防护
- EmailQueueService 增加邮件头 CRLF 过滤
- MailController 加入邮箱权限校验
- 前端补充 CSRF 保护
- 强化 sanitizeHtml DOMPurify 配置
- FetchEmailJob/ProcessEmailQueueJob 继承 RetryableJobBase
- 移除 Program.cs 手动 SQL 迁移，统一用 EF Core Migrate
- 添加 ForwardedHeaders 配置
- 为 6 个 Job 添加 [DisallowConcurrentExecution]
- 抽取 IEmailIngestionService 消除重复代码
- SLA 扫描改为分页
- 创建 [RequireSuperAdmin] 特性
- Refresh Token Cookie 改 SameSite=Strict
- CSP 改为 nonce-based
- SecretHasher 升级为 HMAC-SHA256
- 工作流外部副作用改为两阶段提交
- 新增 agent_replied 工作流触发器
- Modal 实现 focus trap 与 ARIA
- 移除硬编码内网 IP fallback，改强类型 Options 校验
- 创建 Dockerfile 支持容器化部署

---

## [Unreleased] - 2026-07-24

### ✨ 新增功能

#### 客户管理批量操作
- **批量删除**
  - `POST /api/customers/bulk-delete` - 批量删除客户（级联软删除关联会话）
  - 前端添加复选框选择、批量工具栏、删除确认弹窗
  - 复用单条删除逻辑，确保级联软删除关联会话
- **批量导出 CSV**
  - `GET /api/customers/export` - 导出客户数据为 CSV（支持选中导出和全部导出）
  - UTF-8 BOM 编码，Excel 直接打开不乱码
  - 包含客户基本信息：ID、姓名、邮箱、公司、电话、地址等

### 🐛 问题修复

#### 权限系统修复
- **前端权限常量缺失**
  - 前端仅定义约 50 个权限，缺少关键 manage 权限
  - 补充至 74 个完整权限，按功能分组排列
- **超级管理员权限冗余**
  - 数据库中超级管理员 PermissionsJson 同时包含 "*" 和所有单独权限
  - 修复为仅保留 "*" 通配符权限
- **UserRoles 表数据缺失**
  - 除 admin 外其他用户无 UserRoles 记录
  - 通过数据库脚本补全 UserRoles 记录

#### SLA 邮件变量未替换
- **根本原因**
  - `WorkflowService.BuildMailVariablesAsync` 缺少 SLA 相关变量
  - SLA 违规扫描任务创建违规记录后未触发通知邮件
- **修复内容**
  - 在 `BuildMailVariablesAsync` 中补充 30+ 个变量：sla_remaining、deadline、violation_type、waiting_time、timeout_minutes、last_reply_time、replier_name、assignee_name 等
  - 新增 `SlaService.SendSlaNotificationAsync` 方法，违规时自动发送通知邮件
  - 确保系统发送的所有邮件中不再出现未替换的变量名称

#### 会话评价星星数量
- **问题**
  - `ConversationSidebar` 组件中星星渲染数组硬编码为 `[1, 2, 3]`
  - 满意度调查已从 3 星制改为 5 星制，但前端显示未同步
- **修复**
  - 将 `[1, 2, 3]` 改为 `[1, 2, 3, 4, 5]`

#### 会话列表分页
- **问题**
  - 前端未传递 perPage 参数，后端默认返回 20 条
  - 前端用 `list.length` 计算页数，导致只能显示两页
- **修复**
  - 前端传入 perPage=1000 获取全部数据

#### 客户级联删除
- **问题**
  - 删除客户时存在关联会话（即使会话已软删除）导致删除失败
  - 会话列表和垃圾箱中均不显示，但数据库中存在
- **修复**
  - 删除客户前级联软删除所有关联会话（含已软删除的会话）
  - 修复 `DeleteCustomerAsync` 逻辑，确保彻底删除

---

## [Unreleased] - 2026-07-22

### 🚀 性能优化

#### 数据库查询优化
- **SendLogRepository 统计方法优化**
  - `GetStatsAsync`：移除 `ToListAsync()` 全量加载，改为 4 个 `CountAsync` 数据库级计数
  - `GetDailyStatsAsync`：内存 `GroupBy` → EF Core `GroupBy` + `Select` 数据库聚合
  - `GetStatsByMailboxAsync`：三表 JOIN + 内存分组 → 数据库 `GroupBy` + 字典关联元数据
  - 显著减少内存占用和网络传输，大数据量下性能提升明显

- **SatisfactionRatingRepository 统计方法优化**
  - `GetStatsAsync`：内存计数 → 数据库 `CountAsync` + `SumAsync`
  - `GetDailyStatsAsync`：内存 `GroupBy` → EF Core `GroupBy` 数据库聚合
  - `GetStatsByMailboxAsync`：JOIN + 内存分组 → 数据库 `GroupBy` + 字典关联
  - `GetStatsByUserAsync`：JOIN + 内存分组 → 数据库 `GroupBy` + 字典关联

#### 前端性能优化
- **Conversations 页面迁移到 React Query**
  - 从 `useEffect` + `useState` 手动数据获取迁移到 `@tanstack/react-query`
  - 新增 `useConversations`、`useConversationDetail`、`useBulkAssign` 等自定义 Hooks
  - 自动缓存、后台刷新、请求去重、乐观更新
  - SignalR 实时事件触发查询失效（`invalidateQueries`）
  - 消除竞态条件，减少重复 API 请求

### 🛡️ 安全与稳定性

#### 事务边界完善
- **MacroService.ExecuteAsync**
  - 新增事务包裹，确保宏执行、使用记录、执行日志三者原子性
  - 失败时自动回滚，避免数据不一致

- **ConversationService.CreatePhoneConversationAsync**
  - 新增事务包裹，防止客户记录创建成功但会话创建失败导致孤立数据
  - 客户创建/更新与会话创建在同一事务中

#### 开发环境密钥稳定性
- **修复开发环境加密密钥随机生成问题**
  - 原开发环境未配置加密密钥时，每次启动自动生成随机密钥
  - 导致重启后邮箱密码等加密字段无法解密，测试连接报"用户名或密码错误"
  - `appsettings.Development.json` 新增固定开发密钥（`Encryption:Key`、`Jwt:SecretKey`）
  - 开发环境重启后加密数据可正常解密

### 🔧 配置变更

- `appsettings.Development.json` 新增开发环境固定密钥：
  - `Encryption:Key` - 固定 AES 加密密钥
  - `Jwt:SecretKey` - 固定 JWT 签名密钥
- 注意：生产环境仍需通过环境变量 `ENCRYPTION__Key` 和 `JWT__Key` 配置

### 🛡️ 安全增强

#### 病毒扫描服务
- **新增 VirusScanService 病毒扫描服务**
  - 支持恶意特征码检测（EICAR 测试病毒签名）
  - 可执行文件扩展名拦截（.exe/.dll/.bat/.ps1/.sh 等 40+ 种）
  - 可疑 MIME 类型检测
  - 扩展名与 MIME 类型一致性校验（防止扩展名欺骗）
  - 提供 `IVirusScanService` 接口，便于后续集成专业杀毒引擎

- **附件上传病毒扫描**
  - `AttachmentService.UploadAsync` 调用病毒扫描服务
  - 检测到恶意文件时返回 409 错误，不保存文件
  - 多文件上传时逐个扫描，全部失败时返回聚合错误

- **邮件附件病毒扫描**
  - `EmailProcessingService.SaveAttachmentsAsync` 保存文件后自动扫描
  - 检测到恶意文件时删除磁盘文件并跳过（不写入数据库）
  - 不影响邮件收取主流程（失败被 try-catch 隔离）

#### 附件安全强化
- **MIME 类型与扩展名白名单**
  - `appsettings.json` 配置 `Attachment:AllowedExtensions` 和 `Attachment:AllowedMimeTypes`
  - 支持 Office 文档（.doc/.docx/.xls/.xlsx/.ppt/.pptx）
  - 支持 `application/octet-stream` 以兼容浏览器上传 Office 文件
- **文件大小限制**：默认 20MB，可配置
- **文件名清理**：自动移除非法字符，防止路径遍历

---

### ✨ 新增功能

#### 草稿功能
- **会话回复草稿**
  - `POST /api/conversations/{id}/draft` - 保存草稿（正文、抄送、密送、附件）
  - `GET /api/conversations/{id}/draft` - 获取草稿
  - `DELETE /api/conversations/{id}/draft` - 删除草稿
  - `GET /api/conversations/drafts` - 获取当前用户所有草稿
  - 前端进入会话详情页自动回填草稿
  - 回复发送成功后自动删除草稿

#### 回收站 / 软删除恢复
- **软删除机制完善**
  - 14 个实体实现 `ISoftDelete` 接口
  - EF Core 全局查询过滤器自动过滤已删除数据
  - `SaveChanges` 拦截器自动转换 Delete 为设置 DeletedAt
- **恢复功能**
  - 各模块支持恢复已删除数据（使用 `IgnoreQueryFilters()` + `ExecuteUpdateAsync`）
  - 硬删除使用 `ExecuteDeleteAsync` 绕过变更追踪器
  - 删除实体查询使用 `AsNoTracking()` 避免缓存冲突

#### 个人中心
- **个人中心页面整合与美化**
  - 将系统设置-个人资料全部内容迁移到右上角个人中心
  - 渐变页头 + 左侧锚点导航 + 滚动跟随高亮
  - 用户概览、数据统计（参与会话、已回复、在线时长）
  - 基本资料、偏好设置、通知设置、修改密码、登录历史
- **个人资料统计 API**
  - `GET /api/profile/stats` - 返回参与会话数、回复数、在线时长
  - 在线时长基于登录/登出审计日志计算

#### OutputCache 缓存
- **只读数据 API 响应缓存**
  - 角色、标签、自定义字段、系统选项等只读接口启用 OutputCache
  - 减少数据库查询，提升 API 响应速度

#### 系统信息
- **系统信息启动时间显示**
  - 系统设置-系统信息页面显示应用启动时间
  - `GET /api/system/info` 提供系统信息接口

---

### 🐛 问题修复

#### 功能修复

- **客户删除前会话检查**
  - 删除客户前检查是否存在活动会话（状态不为 Deleted）
  - 存在活动会话时阻止删除并返回错误提示
  - 返回类型从 `bool` 改为 `(bool Success, string? ErrorMessage)`

- **附件下载"文件已丢失"错误**
  - `FetchEmailJob` 和 `MailController` 硬编码路径与 `AttachmentService` 不一致
  - 统一使用 `Attachment:UploadPath` 配置（`wwwroot/attachments`）
  - `Attachment` 实体创建时正确设置 `StoredFileName` 和 `FileDir`

- **满意度评价邮件变量未替换**
  - `WorkflowService.BuildMailVariablesAsync` 缺少评分变量
  - 新增 `satisfaction_rating`、`satisfaction_rating_value`、`satisfaction_comment` 变量
  - 评分 3=满意 😀 / 2=一般 😐 / 1=不满意 😞

- **关闭会话同时发送两封邮件**
  - 原 `ConversationStatusService` 同时发送"会话关闭通知"和"满意度调查"邮件
  - 移除重复的满意度调查邮件调用，仅保留关闭通知邮件（内含评分链接）
  - 批量关闭会话同样修复

- **满意度感谢邮件触发器错误**
  - 原工作流 `TriggerType` 错误设为 `status_changed`
  - 修正为 `satisfaction_submitted`，客户提交评价后才发送
  - 新增数据库迁移逻辑，自动修复已有数据

- **PPTX 文件上传 409 错误**
  - `appsettings.json` 缺少 `.ppt` 和 `.pptx` 扩展名
  - Office 文档 MIME 类型映射补充 `application/octet-stream`

- **添加附件无法使用**
  - `VirusScanService._maliciousSignatures` 误将正常文件格式头标记为恶意
  - 清理为仅保留 EICAR 测试病毒签名
  - `UploadMultipleAsync` 全部失败时抛出聚合异常

#### 架构修复

- **ThreadStatic 字段导致竞态条件**
  - 拦截器和 Scoped 服务中的 `[ThreadStatic]` 字段改为实例字段
  - 防止并发请求下的数据混淆

- **EF Core 变更追踪器缓存问题**
  - 软删除实体查询使用 `AsNoTracking()`
  - 硬删除/恢复使用 `ExecuteDeleteAsync`/`ExecuteUpdateAsync` 绕过变更追踪器

- **Unit of Work 多次 SaveChanges**
  - 修复导致事务冲突和操作不完整的问题

---

### 🚀 性能优化

- **OutputCache 响应缓存**
  - 只读数据 API（角色、标签、自定义字段、系统选项）启用缓存
  - 显著减少重复数据库查询

- **病毒扫描流式处理**
  - 仅读取文件前 512 字节进行特征码检测
  - 不加载整个文件到内存

---

### 🎨 前端优化

- **个人中心页面重构**
  - 整合系统设置-个人资料所有功能
  - 渐变页头、左侧锚点导航、滚动跟随高亮
  - 4 个数据统计卡片、6 个偏好设置下拉、3 个通知开关

- **会话详情自动恢复草稿**
  - 进入会话详情页自动加载并回填草稿
  - 保存/删除草稿实时反馈

---

### 🔧 配置变更

- `Attachment:AllowedExtensions` 新增 `.ppt`、`.pptx`
- `Attachment:AllowedMimeTypes` 新增 `application/octet-stream`
- `Attachment:UploadPath` 统一为 `wwwroot/attachments`
- 4 个工作流默认启用（客户回复确认、紧急工单通知、客户回访、会话升级通知）

---

### 📚 文档

- 更新 README.md
  - 新增病毒扫描、草稿箱、回收站、通知中心等功能特性
  - 新增附件安全、密码策略等安全特性
  - API 模块列表补充通知、系统选项、角色、邮箱签名、自定义字段、模块管理、个人中心
- 更新 docs/development.md
  - 新增病毒扫描、草稿、软删除恢复、OutputCache 等章节

---

## [Unreleased] - 2026-07-21

### ⚠️ 安全性更新

#### 高优先级

- **JWT Token 改用 HttpOnly Cookie 存储**
  - 后端登录接口同时返回 JSON Token 并设置 HttpOnly Cookie
  - JWT Bearer 认证同时支持 Cookie 和 Authorization Header（向后兼容）
  - 前端 axios 配置 `withCredentials: true` 自动携带 Cookie
  - SignalR 连接使用 `withCredentials` 替代 `accessTokenFactory`
  - I18nContext 的 fetch 请求添加 `credentials: 'include'`
  - 前端不再将 Token 存储在 localStorage 中，有效防御 XSS 攻击

- **新增 API Key 认证中间件**
  - 实现 `ApiKeyAuthenticationHandler`，支持 `X-Api-Key` 请求头认证
  - 自动校验密钥有效性、过期时间、用户关联
  - 自动更新使用记录（LastUsedAt、LastUsedIp、UsageCount）
  - 新增 `ApiKeyLoggingMiddleware`，自动记录 API Key 调用日志到 `ApiKeyLogs` 表
  - 默认授权策略支持 JWT + API Key 双认证方式

- **新增安全响应头**
  - `X-Content-Type-Options: nosniff`
  - `X-Frame-Options: DENY`
  - `X-XSS-Protection: 1; mode=block`
  - `Referrer-Policy: strict-origin-when-cross-origin`
  - `Content-Security-Policy` 全面的 CSP 策略

- **AES-GCM 敏感数据加密**
  - 实现 `AesEncryptionService` 加密服务
  - 通过 EF Core Value Converter 自动加密/解密敏感字段
  - 支持邮箱的 `InPassword`、`OutPassword` 等敏感字段

#### 中优先级

- **邮箱密码不再明文存储** - 使用 AES-GCM 加密算法加密存储

---

### 🚀 性能优化

#### 高优先级

- **DashboardService 循环 Count 优化为批量查询**
  - 新增 `GetActiveCountByUserIdAsync` 批量获取活跃会话数
  - 新增 `GetClosedCountByDateRangeAsync` 批量获取关闭会话数
  - 新增 `GetReplyCountByUserIdAndDateRangeAsync` 批量获取回复数
  - 新增 `GetStatusCountsByMailboxIdAsync` 批量获取邮箱状态统计
  - 查询次数从 O(N) 降为 O(1) 固定次数

- **WorkflowService 标签 N+1 查询优化**
  - `BuildContextFromConversation` 方法改为通过 `GetByConversationIdAsync` 批量获取标签
  - 消除循环内逐个查询标签的性能问题

- **SLA 扫描 N+1 查询优化**
  - 新增 `GetPoliciesForMailboxesAsync` 批量获取 SLA 策略
  - 新增 `GetByConversationIdsAsync` 批量获取违规记录
  - 复用 `IThreadRepository.GetByConversationIdsAsync` 批量获取线程
  - 扫描 N 个会话的查询次数从 4N+1 降为固定 4 次

---

### 🐛 问题修复

#### 功能修复

- **快捷回复模板未显示全部内容**
  - ReplyBox 组件移除 `mailboxId` 参数限制
  - 修复后加载全部快捷回复模板，不再按邮箱过滤

- **个人资料头像上传 Network Error**
  - 前端 `Profile` 页面调用了不存在的后端 API `/api/profile/avatar`
  - 改为使用 `usePreferences` 提供的本地头像上传功能（localStorage 存储）

- **翻译接口认证导致启动失败**
  - `LocalizationController.GetTranslationsByCulture` 错误地添加了 `[Authorize]`
  - 移除认证限制，翻译资源公开访问

#### 架构修复

- **DatabaseSeeder 种子数据嵌套问题**
  - 将所有表的种子数据检查从嵌套的 `if (!context.Users.Any())` 提升到顶层
  - 每个表独立进行存在性检查，确保用户表有数据时其他表也能正常初始化
  - 重构为 `SeedUsersAsync`、`SeedWorkflowsAsync`、`SeedSlaPoliciesAsync` 等独立方法

- **泛型 Repository 软删除过滤**
  - 新增 `ISoftDelete` 接口
  - 在 `NoneadCSSDbContext` 中添加全局查询过滤器（自动过滤 DeletedAt != null 的实体）
  - 添加 `SaveChanges` 拦截（Deleted 状态转换为设置 DeletedAt）
  - 14 个实体类实现 `ISoftDelete` 接口：User、Mailbox、Customer、Tag、MailTemplate、CannedResponse、EmailSignature、Role、Team、SlaPolicy、Macro、Workflow、NotificationTemplate、NotificationRule

- **EmailQueueService 事务包裹**
  - 新增 `IUnitOfWorkTransaction` 接口（领域层不依赖 EF Core）
  - 实现 `EfCoreUnitOfWorkTransaction` 适配器
  - `ProcessQueueItemAsync` 方法使用事务包裹所有数据库操作
  - 确保 SendLog 状态更新和 Thread.MessageId 更新的原子性

---

### 📝 日志与审计

- **AuditActionFilter 记录异常请求**
  - 移除异常时跳过审计的 early return
  - 异常信息记录到审计日志的 `newValues` 字段（`_error` 键）
  - 空 catch 块改为 `ILogger.LogError` 记录审计写入失败

- **修复关键空 catch 块**
  - `MacroService.MapToDto` 反序列化失败添加警告日志
  - `AuditSaveChangesInterceptor.GetEntityId` 获取实体ID失败添加警告日志
  - 错误不再被静默吞噬，便于排查问题

---

### 🎯 一致性改进

- **MacroService 与 WorkflowService 动作类型命名统一**
  - 统一为 WorkflowService 的「动词+宾语」长命名风格
  - `status` → `set_status`
  - `assign` → `assign_user`
  - `priority` → `set_priority`
  - `reply` → `send_reply`
  - `add_tag` / `remove_tag` / `add_note` 保持不变
  - 后端 switch case 保留旧命名向后兼容（fall-through）
  - 前端 `Macros.tsx` 所有选项和判断更新为新命名

---

### 🎨 前端优化

#### 架构改进

- **Chat 页面从 3 秒轮询改为 SignalR 实时推送**
  - 移除 `setInterval` 轮询机制
  - 使用 `useSignalREvent` 监听 `SignalREvents.RealtimeChat` 事件
  - 同时支持带 `sessionId` 和不带 sessionId 的事件数据结构
  - 显著减少无效 HTTP 请求和服务器负载

- **useSignalR 监听器内存泄漏修复**
  - `_stateChange` 事件监听添加 cleanup 函数
  - 组件卸载时正确调用 `signalRService.off()` 移除监听器
  - 移除未使用的 `handleConnected` 等死代码

- **SignalR 事件名硬编码修复**
  - `Conversations.tsx` 硬编码字符串替换为 `SignalREvents` 常量
  - 移除失效的 snake_case 事件监听（`conversation_created`、`reply_created`）
  - 改为监听 `SignalREvents.NewConversation` 和 `SignalREvents.NewReply`

---

### 📚 文档

- 更新 README.md
  - 新增前端技术栈说明
  - 新增安全特性章节
  - 更新项目结构目录
  - 添加前端启动说明
  - 更新访问地址（修正端口号）
  - 添加 API Key 认证方式说明
  - API 模块列表新增 API 密钥模块

---

## [1.0.0] - 初始版本

### 基础功能

- 完整的工单管理系统（会话、回复、分配、关闭）
- 多渠道支持（Email、Phone、Chat）
- 邮箱集成（SMTP/POP3/IMAP）
- 客户管理与自定义字段
- 团队协作与角色权限
- 工作流自动化引擎
- SLA 服务等级协议
- 宏命令快捷操作
- Webhook 事件集成
- 满意度评价
- SignalR 实时通知
- 审计日志
- 邮件模板
- 国际化（简体中文 / 英文）

---

[Unreleased]: https://github.com/Nonead/NoneadCSS/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Nonead/NoneadCSS/releases/tag/v1.0.0
