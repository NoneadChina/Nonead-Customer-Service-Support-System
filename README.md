# NoneadCSS - 拓德客户服务与支持系统

> **版本 v2.3.0 · 2026-10-23**（当前开发批次：Messenger / Telegram / LINE / iMessage 四个新 IM 渠道 + 微信客服直连 + 全站中英双语 OpenAPI + 开源准备；批次细节见 `CHANGELOG.md` 的 Unreleased 段，尚未打 tag）

NoneadCSS (nCSS) 是一个基于 .NET 10 构建的现代化客户服务与支持系统，提供完整的工单管理、邮箱集成、工作流自动化等功能，覆盖 Email、在线聊天、微信公众号、WhatsApp、Messenger、Telegram、LINE、iMessage、钉钉、飞书等 10+ 客户渠道。

## 功能特性

### 核心功能
- **工单管理** - 完整的会话生命周期管理，支持创建、分配、回复、关闭、克隆等操作；批量分配、批量关闭、批量更新优先级、批量删除等批量操作；内部备注、关注、标签等协作功能
- **多渠道支持** - 支持 Email、Phone、Chat、微信公众号、WhatsApp、Facebook Messenger、Telegram、LINE、iMessage、钉钉、飞书等多种渠道的会话来源；各渠道客户与会话统一管理、统一工作流触发；快捷来源标签按模块启用状态动态显示
- **邮箱集成** - 自动收取和发送邮件，支持 SMTP/POP3/IMAP
- **微信公众号客服集成** - 微信消息推送到客服系统双向同步；文本/图片/语音/视频/位置/链接/事件全类型支持；临时素材自动上传（image≤2MB、voice≤2MB、video≤10MB，3天有效期）；客户档案自动建档（昵称/头像/OpenID）；48 小时客服消息窗口可视化（ok/warning/expired 三档颜色进度条 + 模板消息/订阅通知兜底）；关注欢迎语 XML 被动回复；订阅通知新模式（sendSubscribe API + 页面跳转 page 字段）；**通知消息 Tab**（模板消息旧版 + 订阅通知新版双模式切换、会话搜索选择器替代手输 ID、工单受理/已解决/回复提醒 3 个常用模板预设、实时 JSON 校验 + `color` 字段着色卡片预览、最近 10 条发送历史）；**自定义菜单 Tab**（可视化编辑器 + JSON 双向同步、表单式增删改一级≤3/二级≤5、按 `type` Schema 校验必填字段 click/view/scancode_*/pic_*/location_select/media_id/article_id/miniprogram、手机预览模拟微信底部菜单栏 hover 展开二级、线上菜单拉取 + 一键加载到编辑器修改 + 发布/删除）
- **WhatsApp Business Platform 集成** - 基于 Meta Cloud API 接入 WhatsApp Business；Webhook HMAC-SHA256 签名验证；文本/图片/音频/视频/文档/贴纸/位置/联系人/交互式按钮全类型消息接收；媒体回复（图片/音频/视频/文档）；24 小时客户服务窗口可视化（进度条 + 窗口外模板消息兜底）；wa_id 客户自动建档；模板消息发送（窗口外触达）；消息状态回执（sent/delivered/read）+ 已读回执；AES 加密存储敏感配置；定时器优化（chained setTimeout 替代 setInterval，消除泄漏/漂移）
- **钉钉集成** - 钉钉企业内部应用接入；配置项（AppKey、AppSecret、AgentId、RobotCode、CallbackToken、EncodingAESKey）+ 消息接收模式（HTTP 回调 / Stream WebSocket 长连接）+ 模块启动按钮一键接入；Stream 模式下自动注册连接凭证、心跳、断线重连、ACK 回执；模块注册（Modules 表 + 权限 dingding.view/dingding.manage）；侧边栏「钉钉聊天」入口按模块启用状态动态显示；双向消息同步：文本 / 表情 / 图片 / 语音 / 视频 / 文件全类型支持；表情自动双向转换（钉钉 `[微笑]` ↔ Unicode emoji）；客户发送图片自动下载并挂附件（DownloadCode → 下载链接 → 本地存储 → Attachment 记录）；客服回复支持文本 + 多附件（图片/文件），图片通过 oapi `media/upload` 上传为 `@media_id` 后，按 `sampleImageMsg = {"photoURL":"@media_id"}` 扁平结构发送到钉钉窗口，文件按 `sampleFile = {"mediaId":"@...","fileName":"xxx","fileType":"xlsx"}` 发送；客服头像/昵称按用户档案显示（创建线程时绑定 `UserId`）；`api.dingtalk.com` IPv6 网络不可达时，通过自定义 `SocketsHttpHandler.ConnectCallback` 强制解析 IPv4；配置使用 `IMemoryCache`（Singleton）缓存，避免跨请求 Scoped 实例重复读取 baseline；前端模式选择卡片化 UI + 敏感字段「已配置/锁图标」统一布局；保存配置时 `SystemOptionsRepository` 使用 tracked 查询避免 `SaveChanges` 丢失 UPDATE；前端 Vite 支持 `allowedHosts: true` 与自定义 hosts 域名绑定（如 `css.example.com`）
- **飞书集成** - 飞书开放平台接入；模块注册（Modules 表 + 权限 feishu.view/feishu.manage）；侧边栏「飞书聊天」入口按模块启用状态动态显示；模块标题统一为「飞书集成」（支持中文搜索）；**消息接收模式**：HTTP 回调 / Stream WebSocket 长连接（推荐，无需公网 IP/域名）；**Stream 长连接**：手动实现 Protobuf Frame 编解码（无需 protobuf 依赖），WebSocket 连接建立 + 30s 心跳 + 指数退避重连（1/2/4/8/16s，封顶 60s）+ ACK 回执 + 事件分发（支持 v1.0/v2.0 事件）；**配置管理**：App ID / App Secret / Verification Token / Encrypt Key / 关联邮箱 / 自动回复消息；**前端 3-Tab 架构**（快速接入 / 高级设置 / 使用指南），飞书品牌蓝 `#3370FF` + 辅助色 `#00D6B9`，玻璃拟态连接状态卡片，接收模式卡片切换、启用飞书接入开关、条件参数表单（按模式显示不同字段）；**双向消息同步**：文本 / 富文本（Post）/ 图片 / 文件 / 交互卡片；客服回复支持图片（base64+md5）/ 文件（media upload + send），上传失败时回退发送文本提示；**事件订阅**：`im.message.receive_v1`（消息接收）、`contact.user.updated_v3`（用户变更）；**鉴权**：`tenant_access_token` 自动缓存与刷新（2 小时有效期）；**安全**：事件签名/Token 强制校验，日志脱敏 token；**配置缓存**：`FeishuSettingsProvider` Singleton + IMemoryCache，缓存 miss 时从数据库重新加载，避免回退到 baseline 导致开关自动关闭
- **Facebook Messenger 集成** - 基于 Meta Graph API 接入 Messenger；Webhook 双校验（`hub.challenge` 订阅验证 + `X-Hub-Signature-256` HMAC-SHA256 常量时间比较）；文本/图片/音频/视频/文件/贴纸/位置全类型接收，`delivery`/`read` 回执入库；文本按 Graph 上限 2000 字符 UTF-8 自动分片；24 小时客户服务窗口可视化 + message tag 策略（`HUMAN_AGENT`/`ACCOUNT_UPDATE` 等）；入站媒体即收即存（`ResponseHeadersRead` + 增量字节上限，超限只留占位文本）；PSID 客户自动建档并按 `PageId` 过滤多 Page 事件、按 PSID 串行化避免重复会话；入站异常返回非 2xx 让 Meta 重投；`messenger.view` / `messenger.manage` 模块权限
- **Telegram 集成** - Telegram Bot API 接入；Webhook（`X-Telegram-Bot-Api-Secret-Token` 校验 + 1 MiB 体积上限，密钥先验后读体）与长轮询双模式（处理失败不推进 offset，避免丢更新）；文本/图片/文件/语音/视频全类型接收，入站媒体流式下载（getFile `file_size` 与 `Content-Length` 双预检 + 增量上限）；出站 4096 字符分片 + 全局 25/s 与单会话 1/s 限速（本地 429/420 退避，队列与坐席回复共用同一发送链路）；`parse_mode`（HTML/Markdown/MarkdownV2）端到端透传；论坛话题按 `message_thread_id` 回复落位；403 自动标记会话不可达；`telegram.view` / `telegram.manage`
- **LINE 集成** - LINE Messaging API 接入；`X-Line-Signature` HMAC-SHA256 校验；replyToken 一次性回复（发送成功后标记 `replyTokenUsed`，避免重复使用导致 409 与配额浪费）+ push 兜底；文本/图片/视频/音频/文件/位置/贴纸接收；配额按官方 `{"type":"limited"|"none","value":N}` 口径解析并与月度消耗一起可视化；`mode=standby`（频道待机，由其他系统应答）时跳过自动回复；follow/unfollow/join/leave 等事件备注按 `message.id` 去重；入站失败返回 500 让 LINE 重投；`line.view` / `line.manage`
- **iMessage 集成** - 双模式：BlueBubbles 桥接（`chatGuid` 收发、附件/表情/回执）与 Apple Messages for Business（AMB MSP）；BlueBubbles Webhook 共享密钥校验（`X-Webhook-Secret` 头或 `?secret=`，常量时间比较）；AMB 入站走 `/v1/preDownload` + AES-CTR 解密附件（无密钥时明确报“未保存”而不是假称已存）；AMB 出站为官方信封 `{v,id,sourceId,destinationId,locale,body}`，消息 `id` 由队列标识确定性派生（重试不重复投递）；发送结果按 HTTP 状态分类，400/403/404/410 判为终局不再重试；`imessage.view` / `imessage.manage`
- **客户管理** - 完整的客户档案管理，支持自定义字段、批量删除、批量导出 CSV；OpenID/UnionID 关联，多渠道合并同一客户
- **团队协作** - 支持多用户、团队管理、角色权限控制；SignalR 双 Hub 跨用户会话实时刷新（同邮箱组所有客服实时看到新回复；日历事件变更多端通过 SignalR 与本地乐观更新 TTL 保护位协同，避免乐观更新后"后端旧数据推送回弹"）
- **草稿箱** - 会话回复支持保存草稿、自动恢复、跨会话保留

### 高级功能
- **工作流自动化** - 基于条件触发的自动化规则引擎（支持 12 种触发器 + 11 种动作，含 execute_macro 宏执行动作）
- **SLA 策略** - 服务等级协议管理与违规监控
- **宏命令** - 一键执行多步操作的快捷命令，支持通过工作流对批量会话执行
- **Webhook 集成** - 实时推送事件到外部系统
- **满意度评价** - 5 星制客户服务质量评估与反馈（含评价后感谢邮件工作流 + 双路径 Token 验证：邮箱 & ChannelId + 无邮箱客户聊天渠道回退推送）
- **病毒扫描** - 附件上传与邮件附件自动病毒扫描，拦截恶意文件和可执行文件
- **通知中心** - 统一的站内通知管理，支持模板、规则、回收站
- **自定义报表** - 可视化报表构建器，支持自定义数据源、字段、筛选条件、图表类型；定时调度（Quartz 触发）+ 邮件订阅推送；导出 PDF/Excel；**软删除 + 垃圾箱功能**（删除报表进入垃圾箱可恢复，垃圾箱 Tab 带数量角标，支持单条恢复/永久删除/清空垃圾箱，二级确认框防误操作；权限校验：普通用户仅操作自己创建的报表，SuperAdmin 可操作所有报表）

### 系统功能
- **实时通知** - SignalR 双 Hub 架构（NotificationHub 客服侧 + ChatHub 客户侧）实时推送会话更新、聊天消息、@提及、输入状态等通知
- **数据导入导出** - 支持数据的批量导入和导出
- **审计日志** - 完整的操作审计与追踪；**多维过滤**（操作类型 / 实体类型 / 用户 ID / 实体 ID / IP 地址 / 关键字搜索 / 日期范围）；**全局统计聚合**（独立 `/stats` 端点返回总记录数 / 独立用户数 / 24h 新增 / 异常事件数 + 操作分布 Top N + 实体分布，不依赖当前页数据）；**CSV 导出**（带 BOM，Excel 友好，最大 10000 行）；**HMAC-SHA256 完整性校验**（单条 + 批量，篡改检测，结果回写列表 `isTampered` 字段并在表格中以 ShieldX/ShieldCheck 图标 + 行底色高亮可视化；v2.2.10 修复 `CreatedAt` 时间 Kind 不一致导致的签名永不通问题并对存量 56887 条记录重签）；**关联日志**（同实体优先 → 同用户回退，便于追溯操作上下文）；**前端中文标签全面补齐**（50 个 Action + 61 个实体类型中文映射 + `calendar.`/`time_entry.` 前缀兜底，统计卡片不再出现英文）；OutputCache 策略纳入全部过滤参数避免缓存命中冲突
- **邮件模板** - 可自定义的邮件通知模板（系统模板 + 自定义模板，支持 14 种内置模板）
- **国际化** - 支持简体中文和英文
- **系统设置** - 系统信息、个人中心、模块管理、本地化
- **回收站** - 软删除数据可查看、恢复和永久删除，支持会话、客户、标签、邮件模板、自定义报表等 15 种实体的软删除机制
- **OutputCache** - 只读数据 API 响应缓存，提升性能
- **全局搜索** - 跨会话、客户、知识库的统一搜索接口；**知识库结果直达文章（T000109）**——点击搜索结果跳转 `/knowledge?article={id}` 自动打开对应文章弹窗（原 `/knowledge/{id}` 无路由导致 404 已修复）
- **速率限制** - 多级速率限制策略（全局/搜索/报表），防止滥用
- **角色权限系统** - 基于角色的细粒度权限控制（超级管理员/客服主管/客服专员/只读用户），支持邮箱级权限隔离
- **团队管理** - 团队成员管理与角色分配，支持按团队分配会话
- **日历管理** - 客服日程与事件管理，支持月/周/日/列表/概览多视图；事件类型（截止日期/任务/会议/提醒/个人/出行/工作/其他）+ 多邮箱筛选；状态机（待处理/已完成/已取消）；每日/每周/每月/每年重复日程 + 重复排除；多通道提醒（站内+邮件+短信）；ICS 导入导出（RFC 5545）；HMAC Token 日历订阅；**Outlook 用户级 Azure AD 凭据配置（T000099）**（每用户独立 AppId/TenantId/ClientSecret/RedirectUri，OAuth 授权弹窗 400 配置问题修复）；**参会人保存与回填修复（T000100）**（AttendeesJson 反序列化容错 SafeDeserializeList + NormalizeAttendeesAndReminderEmails 双写互补，前端 openEdit 合并与会者邮箱 + EditorPanel 清空空 attendees 数组）；**全局乐观锁并发控制**（RowVersion + If-Match / 409 latest 快照回写）；软删除回收站；**多选批量操作**（批量删除/恢复/更改类型/更改状态）；**月/周/日视图全量拖拽**（底部拖拽调整时长 + 整块拖拽移动 + 跨日切分显示）；**Undo/Redo 持久化**（localStorage 按用户隔离，50 条上限，失败自动回滚乐观修改）；**视图位置持久化**（刷新后停留在原月/周/天视图与日期位置，每周首日为周日）；**日视图数据覆盖全面修复**（增量合并 + ±45 天窗口，三视图事件完全一致）；**三视图双向乐观更新**（日历拖拽 ↔ 列表编辑 ↔ 概览 KPI 即时联动；失败自动按 snapshotBefore 回滚；API 成功后以最新 DTO 二次覆盖乐观值 + 概览 upcomingEvents 同步刷新，修复"保存后回弹"）；事件冲突检测；子任务；截止日期自动联动；每次新建事件随机分配颜色；**重复规则中文描述实时预览** + 工作日快捷按钮（工作日/每天/清除）；**提醒类型选择**（站内/邮件/短信）+ 自定义提醒时间换算提示；**与会者邮箱格式校验** + 清空按钮 + 添加后保持焦点；**色块悬停弹窗显示创建者**；**SignalR 重连自动全量刷新**（避免重连期间消息丢失）+ 页面可见时自动重连；**MemoryCalendarQueryCache Singleton** 生命周期修复
- **知识库** - 多级分类树（16 级深度保护，分类启用/禁用隔离）、三态状态机（草稿/已发布/已归档）、FTS5 全文搜索 + 主区域搜索结果直显（标题/摘要/正文/标签）、**点赞/取消点赞 toggle（T000101）**（KnowledgeArticleLikes 表原子去重 + `IsLiked` 状态回显，已点赞按钮变实心、再次点击取消并递减计数）、浏览统计、Markdown 编辑器（marked + highlight.js + Tailwind 样式恢复，列表/加粗/斜体/链接/图片/行内代码 6 种格式正确渲染）、附件一致性（未保存附件 localStorage 草稿 + 编辑态/查看态一致）、邮箱隔离（通用+专属分类）、禁用分类在文章筛选/新建/编辑中前端兜底过滤 + 后端写入层双重校验、HTML 消毒防 XSS、垃圾箱恢复
- **在线聊天** - 实时在线聊天会话；消息撤回（2 分钟时间窗）、消息编辑（已编辑标记）、已读回执（双勾/单勾）、输入状态指示（2 秒节流）、@提及（Toast 通知+跳转）、表情面板（24 个）、快捷回复、消息搜索、附件分享（25MB）、按日期分组、一键转为工单
- **客户聊天 Widget** - 可嵌入第三方网站的浮窗 Widget（360×560）+ 全屏模式；基于 sessionToken 匿名鉴权，客户无需登录；四阶段状态机（initiate→chatting→rating→completed）；SignalR 双向实时通信+降级轮询；乐观发送+失败回退；1-5 星满意度评分；滑动窗口限流（每 IP 每分钟 30 次）；**跨域嵌入支持**（Nginx CSP `frame-ancestors` 白名单，允许 www.example.com 等合作站点右下角嵌入人工客服）

### 安全特性
- **JWT + HttpOnly Cookie** - 令牌存储于 HttpOnly Cookie，防御 XSS 攻击；JWT 密钥使用 UTF8 编码，ClockSkew=Zero 消除令牌宽限期
- **Refresh Token 安全** - Token 轮换机制、重用检测（检测到已撤销 token 被使用时撤销所有 token）、事务原子轮换
- **登录失败锁定** - 连续 5 次登录失败自动锁定 15 分钟，防止暴力破解
- **密码重置安全** - 重置后失效所有未使用重置令牌 + 撤销所有 Refresh Token，防止会话劫持
- **密码策略增强** - 弱密码字典检查、常见模式检测（连续字符/键盘序列/重复字符）、BCrypt 哈希存储
- **API Key 认证** - 支持通过 X-Api-Key 头进行 API 密钥认证，支持 Scope 细粒度权限
- **AES-GCM 敏感数据加密** - 邮箱密码等敏感字段使用 AES-GCM + HKDF 加密存储；**解密失败保护**（密钥轮换后旧密文不再泄露到 API 响应，返回空字符串）；**旧密钥迁移告警**（启动日志 + 首次回退告警，督促完成 HKDF 密钥迁移）
- **安全响应头中间件** - Content-Security-Policy、X-Content-Type-Options、X-Frame-Options、Referrer-Policy、Permissions-Policy、X-XSS-Protection 六重安全头
- **生产端点分级保护** - 仅开发环境使用的测试端点（Tracking 旧版路由 / Feishu GET 回显 / WeChat 模拟注入）在生产环境自动返回 404
- **CSRF 防护中间件** - token 比较使用常量时间算法（CryptographicOperations.FixedTimeEquals），仅放行有效 Bearer token
- **SSRF 防护** - Webhook URL 白名单校验，禁止访问内网地址
- **CRLF 注入过滤** - 邮件头注入防护，过滤换行符等控制字符；EmailService 三处邮件构造路径均使用 `SanitizeHeaderInput` 深度防御清理（移除 C0 控制字符 + RFC 5322 长度限制）
- **XSS 防护** - 前端 DOMPurify HTML 内容消毒 + 后端邮件模板变量 WebUtility.HtmlEncode 编码
- **全局异常消息脱敏** - 生产环境不返回异常详情，按状态码返回安全通用提示
- **ForwardedHeaders 安全配置** - 从配置读取信任代理列表，不再清空 KnownProxies
- **Refresh Token Cookie SameSite=Strict** - 防御 CSRF 攻击
- **CSP nonce-based 防护** - 基于一次性 nonce 的内容安全策略
- **SecretHasher HMAC-SHA256 签名** - 密钥使用 HMAC-SHA256 签名存储
- **软删除机制** - 全局查询过滤器自动过滤已删除数据
- **事务管理** - 关键操作使用数据库事务保证原子性
- **附件病毒扫描** - 上传附件与邮件附件自动病毒扫描，拦截恶意文件和可执行文件
- **附件安全校验** - MIME 类型白名单、扩展名白名单、文件头签名检测、25MB 大小限制
- **路径遍历防护** - 附件文件路径严格校验，Path.GetFullPath 规范化 + StartsWith 边界检查
- **病毒扫描增强** - 扫描文件头部 64KB + 尾部 4KB，覆盖大多数恶意代码嵌入场景
- **敏感配置权限控制** - 系统选项等敏感配置仅超级管理员可读取
- **前端权限防篡改** - 权限数据从后端 API 动态获取，不存储在 localStorage
- **安全登出流程** - 异步调用后端登出 API 后清理本地状态，全面清除 localStorage
- **客户头像 URL 协议白名单** - 客户头像 URL 创建/更新时校验协议，仅允许 https:// 绝对路径、本地相对路径、data:image/* 内联图片；禁止 http://、javascript:、vbscript:、data:text/html、file://、ftp://、blob:、mailto: 等危险协议
- **前端 catch 块类型安全** - 统一将 catch(e) 的错误类型从 any 收紧为 unknown，使用 `getApiErrorMessage` 类型守卫函数提取错误信息，避免 `err.message` 直接访问导致的运行时错误

## 技术架构

### 项目结构

```
NoneadCSS/
├── NoneadCSS.Api/              # Web API 层（57 个控制器）
│   ├── Controllers/            # REST API 控制器
│   ├── Filters/                # 过滤器（权限、审计等）
│   ├── Middleware/             # 中间件（安全头、CSRF、异常脱敏等）
│   ├── Authentication/         # 认证处理器（API Key）
│   ├── Documentation/          # OpenAPI 中英双语文档与词典
│   └── DatabaseSeeder.cs       # 数据库种子数据
├── NoneadCSS.Application/      # 应用服务层
│   ├── Configuration/          # 渠道与系统选项（Options）
│   ├── DTOs/                   # 数据传输对象
│   ├── Services/               # 业务服务接口与实现
│   └── Resources/              # 本地化资源
├── NoneadCSS.Domain/           # 领域模型层（56 个实体）
│   ├── Entities/               # 实体模型
│   ├── Enums/                  # 枚举定义
│   ├── Events/                 # 强类型领域事件
│   ├── Repositories/           # 仓储接口
│   ├── IUnitOfWork.cs          # 工作单元接口
│   └── ISoftDelete.cs          # 软删除接口
├── NoneadCSS.Infrastructure/   # 基础设施层
│   ├── Jobs/                   # Quartz 定时任务
│   ├── Listeners/              # 领域事件监听器
│   ├── Persistence/            # 数据库上下文与迁移
│   ├── Repositories/           # 仓储实现
│   ├── Services/               # 基础设施服务（渠道客户端等）
│   └── UnitOfWork.cs           # 工作单元实现
├── NoneadCSS.Tests/            # 后端测试（124 个测试类 / 2233 个用例）
├── frontend/                   # 前端应用（55 个页面）
│   ├── src/
│   │   ├── pages/              # 页面组件
│   │   ├── components/         # 公共组件
│   │   ├── context/            # 全局状态
│   │   ├── hooks/              # 自定义 Hooks（useAuth、useSignalR、useConversations 等 React Query Hooks）
│   │   ├── api/                # API 客户端
│   │   └── services/           # 前端服务
│   └── vite.config.ts          # Vite 配置
├── scripts/                    # 运维/开发脚本
│   ├── backend-test-runner/    # 本机后端测试执行器（绕过 dotnet test 的 Win32 限制）
│   ├── css-api/                # 直连生产 API 的脚本与端点清单
│   └── oss-preflight.mjs       # 开源发布预检（密钥扫描 / 链接 / i18n 覆盖）
├── deploy/                     # 部署脚本与 Nginx 配置
├── offline_docker_package/     # 离线 Docker 打包
├── data/                       # 运行时数据（SQLite、附件、日志）
├── logs/                       # 运行日志
└── docs/                       # 项目文档（开发、运维、渠道集成、发布说明）
```

### 后端技术栈


| 类别 | 技术 |
|------|------|
| 框架 | .NET 10 |
| 数据库 | SQLite / SQL Server |
| ORM | Entity Framework Core |
| 认证 | JWT Bearer + HttpOnly Cookie + API Key |
| 实时通信 | SignalR |
| 定时任务 | Quartz.NET |
| API文档 | Scalar |
| 日志 | Serilog |
| 邮件 | MailKit / MimeKit |
| 加密 | AES-GCM |

### 前端技术栈

| 类别 | 技术 |
|------|------|
| 框架 | React 18 + TypeScript 5.6 |
| 构建工具 | Vite 8 |
| UI 样式 | Tailwind CSS 3 |
| 路由 | React Router 6 |
| 数据获取 | React Query (@tanstack/react-query) |
| HTTP 客户端 | Axios |
| 实时通信 | SignalR Client |
| 国际化 | i18next（简体中文 / English） |
| 图标 | Lucide React |
| 富文本 | DOMPurify |
| 测试 | Vitest 3（18 个测试文件 / 684 个用例） |

## 快速开始

### 环境要求

- .NET 10 SDK
- Node.js >= 20.19（或 >= 22.12，Vite 8 要求；建议使用当前 LTS）
- Visual Studio 2022 / Rider
- SQLite（开发环境）或 SQL Server（生产环境）

### 后端启动

```bash
# 克隆项目
git clone <repository-url>
cd NoneadCSS

# 还原依赖
dotnet restore

# 运行 API
cd NoneadCSS.Api
dotnet run
```

### 前端启动

```bash
cd frontend

# 安装依赖
npm install

# 启动开发服务器
npm run dev
```

### 访问地址（按环境区分）

| 入口 | 开发环境（restart-dev.ps1） | 生产环境（Production/start-prod.ps1 + stop-prod.ps1） | 说明 |
|------|---------------------------|---------------------------|------|
| 前端应用（热更新） | http://localhost:5171 | - | Vite dev server；自动代理 `/api` → 后端 5008；**开发环境无需 Nginx** |
| 前端应用（静态托管）| - | http://localhost:5172 | `npx vite preview`；不代理 `/api`，由 Nginx 统一转发 |
| 统一入口（Nginx 反向代理） | - | http://localhost:5173 | 代理 `/` → 前端 5172；代理 `/api` + `/hubs` → 后端 5009；支持 WebSocket Upgrade |
| 后端 API（直连） | http://localhost:5008 | http://localhost:5009 | `/health`、`/health/ready`、`/docs`（Scalar）|
| API 文档 | http://localhost:5008/docs | http://localhost:5009/docs | Scalar 交互式 API 文档 |
| SignalR Hub（客服侧） | http://localhost:5008/hubs/notifications | http://localhost:5009/hubs/notifications | |
| SignalR Hub（客户侧） | http://localhost:5008/hubs/chat | http://localhost:5009/hubs/chat | |

> 端口分配约定：后端 **开发 5008 / 生产 5009**，前端 **开发 5171 / 生产 5172**，Nginx **生产 5173**。详见 [docs/development.md 第 18.8 节](docs/development.md#188-端口分配开发-vs-生产)。

### 开发环境一键启动（后台常驻 + 自动重启）

手工 `dotnet run` / `npm run dev` 起的进程是**当前终端的子进程**：关掉终端、或从别的工具里以子进程方式拉起，进程会随之消失，登录页随即显示「无法连接后端服务」（Vite 代理 `/api` 拿到 502）。需要长时间开着开发环境时用下面两个脚本：

```powershell
# 启动（先构建，再拉起后端 5008 与前端 5171，并做健康检查）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev-up.ps1

# 只重启后端（跳过构建）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev-up.ps1 -SkipBuild -Force

# 停止（不传 -All 时保留前端 Vite）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev-down.ps1
```

- 后端由 `scripts/dev-api-run.cmd` 托管：进程退出后 **5 秒自动重启**，日志在 `%TEMP%\noneadcss-dev\api.log`。
- 两个进程都通过 WMI 创建，**不挂在调用者进程树上**，关闭终端或重启工具都不会带走它们。
- `dev-up.ps1` 的健康检查要求 `http://127.0.0.1:5008/api/auth/captcha` 返回 200 且响应体含 `svgContent`，不只看端口是否在听。
- 只会停止本项目自己的进程（按命令行是否匹配 `NoneadCSS.Api` 判定），不会误杀其他同名进程。

### 一键启动（推荐）

项目提供两个 PowerShell 一键重启脚本，已内置"按端口精准清理旧进程 → 注入环境变量 → 启动服务 → 健康检查等就绪"，全程不杀系统其他同名进程。

#### 开发环境（推荐日常开发）

```powershell
powershell -ExecutionPolicy Bypass -File deploy/restart-dev.ps1
```

脚本路径：`deploy/restart-dev.ps1`；监听端口：后端 5008、前端 5171；不使用 Nginx。

#### 生产环境（独立部署目录 · 推荐 Production/ 内脚本）

```powershell
# 停止 + 清理残留
powershell.exe -ExecutionPolicy Bypass -File C:\noneadcss\Production\stop-prod.ps1

# 启动（后端 :5009 / vite preview :5172 / Nginx :5173）
powershell.exe -ExecutionPolicy Bypass -File C:\noneadcss\Production\start-prod.ps1
```

标准脚本位置：
- 停止：`Production/stop-prod.ps1`（端口 5009/5172/5173 精准清理 + NoneadCSS/NoneadCSS.Api/nginx/node 同名残留兜底）
- 启动：`Production/start-prod.ps1`（内部自带 3 端健康检查 + 前端反代验证）

> 历史入口 `deploy/restart-prod.ps1` / `C:\noneadcss_prod\restart-prod.ps1` 保留用于兼容旧部署；新部署直接使用 `Production/` 目录脚本即可。

部署目录：`C:\noneadcss\Production\`（`backend/` 放发布 exe + dll + appsettings，`frontend/dist/` 放前端构建产物由 `vite preview` 提供 :5172 服务，`data/` 放 SQLite 数据库与 WAL 文件，`logs/` 放运行日志，`nginx/` 与 `nginx.conf` 在根目录）；监听端口：后端 5009、前端 vite preview 5172、Nginx 5173。**注意：真实 SQLite 数据库路径通过绝对路径注入到 `ConnectionStrings__SqliteConnection`，消除 WorkingDirectory vs ContentRoot 解析两套不同文件的陷阱（见 project_memory.md Lessons Learned）**。

**从旧版本升级到 v2.2.3+ 必须额外执行 DDL 补丁**：代码新增 8 张表 `RowVersion BLOB` 列（Users/Customers/ChatSessions/ChatMessages/ApiKeys/Webhooks/KnowledgeCategories/KnowledgeArticles）。启动前请参考 [ReleaseNotes_v2.2.3.md §C](./docs/ReleaseNotes_v2.2.3.md#c-生产-sqlite-ddl-补丁工具-_dbpatchrowversion) 执行 `_DbPatchRowVersion` 控制台工具（幂等事务），否则启动会抛出 `SQLite Error 1: 'no such column: u.RowVersion'`。完整 7 步发布 SOP 见 [docs/development.md §18.11](./docs/development.md#1811-生产发布-sopv223-固化--7-步强制流程--2026-08-14)。

> **注意**：`_DbPatchRowVersion` 是公司生产库上跑过的一次性内部补丁程序，**未随本仓库发布**。全新安装的数据库不需要它（`RowVersion` / `Version` 列由 EF 迁移链直接建出）；只有把 v2.2.3 之前的旧库带到新代码时才需要补列，逐表执行方式与探测 SQL 见 [docs/development.md §18.10](./docs/development.md#1810-旧库升级rowversion-ddl-补丁)。

必需环境变量清单见 [docs/development.md 第 18.12 节](./docs/development.md#1812-生产必需环境变量用户级--user-scope)。

### 健康检查

系统提供两个健康检查端点：

- `GET /health` - **Liveness 探针**：仅检测应用是否存活，不依赖外部服务
- `GET /health/ready` - **Readiness 探针**：检测应用是否就绪，包含数据库连接检查

适用于 Kubernetes 等容器编排平台的 liveness/readiness 探针配置。

### 默认账号

- 用户名: `admin@example.com`
- 密码: 通过环境变量 `ADMIN_PASSWORD` 配置；首次启动时如未配置则自动生成并输出到应用日志

### 首次初始化

全新数据库首次启动后，可用 Setup API 完成管理员与首个邮箱的初始化（当前没有独立的前端向导页面，请求体字段见 `/docs` 的 `Setup` 分组）：

| 步骤 | 端点 | 作用 |
|------|------|------|
| 1 | `GET /api/setup/status` | 查询是否仍需初始化、已完成哪些步骤 |
| 2 | `POST /api/setup/admin` | 创建首个管理员账号 |
| 3 | `POST /api/setup/mailbox` | 创建首个收件邮箱（可跳过） |
| 4 | `POST /api/setup/complete` | 标记初始化完成，之后 Setup 接口不再允许写入 |

生产部署也支持用 `ADMIN_EMAIL` / `ADMIN_PASSWORD` 环境变量在启动时直接创建管理员，两种方式选其一即可。

## API 文档

启动应用后，访问 `/docs` 路径即可查看完整的 API 文档，支持在线测试。

文档页面提供**简体中文 / English** 双语：

- 页面顶部语言开关，或直接访问 `/docs?lang=en`、`/docs?lang=zh-Hans`；选择结果记入 Cookie `ncss_docs_lang`（`Path=/docs`，30 天），无法识别的语言值回落简体中文。
- 两份 OpenAPI 文档同源生成：中文 `/openapi/v1.json`（默认文档 `v1`）与英文 `/openapi/en.json`（文档 `en`），487 条路径一一对应；英文文档由 `NoneadCSS.Api/OpenApi/openapi-en-dictionary.json`（906 条 zh→en 词典）在生成时替换字段文案，不额外维护一套注释。
- Scalar 自带的界面文案没有语言配置项，中文模式下由服务端注入翻译层（132 条 UI 标签 + DOM 翻译脚本）。细节见 [docs/development.md](docs/development.md) 第 9.6 节「OpenAPI / Scalar 文档双语」。

> 开发环境经 Vite 访问时，`/docs` 与 `/openapi/*` 已在 `frontend/vite.config.ts` 中代理到 `http://127.0.0.1:5008`，因此 `http://127.0.0.1:5171/docs?lang=en` 可直接打开英文文档。

### 认证方式

系统支持两种认证方式：

#### 方式一：JWT Bearer Token（推荐前端使用）

1. 调用 `POST /api/auth/login` 登录
2. 系统自动设置 HttpOnly Cookie 存储访问令牌
3. 后续请求浏览器自动携带 Cookie

#### 方式二：API Key（推荐第三方集成使用）

1. 在系统后台创建 API Key
2. 在请求头中添加 `X-Api-Key: <your-api-key>
3. 系统自动完成认证并记录调用日志

### 国际化

所有接口支持通过查询参数切换语言：

```
?lang=zh-Hans  # 简体中文（默认）
?lang=en       # 英文
```

同一语言也可用 Cookie `.AspNetCore.Culture` 或请求头 `Accept-Language: en` 指定；前端按当前界面语言自动附带 `Accept-Language`，无需手工设置。语言判定顺序为 `?lang=` → Cookie → `Accept-Language` → 默认简体中文。

界面文案（前端词条、`/docs` API 文档）两套语言都完整。后端返回的**校验/异常提示**通过 `NoneadCSS.Api/Localization/api-message-en.json`（**735 条** zh→en）在响应出口做精确匹配替换，只作用于错误响应的系统文案字段，不会改写客户自己的正文；词典随源码再生（`merge` 模式只增不减，避免硬编码提示被误删），改动了控制器或服务的中文提示后按 `docs/development.md` §9.7 重跑 `node scripts/i18n/api-message-dictionary.mjs`。种子数据、写入库的聊天系统消息与服务端枚举标签仍是简体中文（设计内限制，详见 §9.5）。

## 配置说明

主要配置项位于 `appsettings.json`：

```json
{
  "Database": {
    "UseSqlite": true
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=NoneadCSS;Trusted_Connection=True",
    "SqliteConnection": "Data Source=NoneadCSS.db"
  },
  "Jwt": {
    "SecretKey": "YourSuperSecretKeyForJWTTokenGeneration1234567890",
    "Issuer": "NoneadCSS",
    "Audience": "NoneadCSSApi",
    "AccessTokenExpireMinutes": "60"
  },
  "Email": {
    "MaxAttachmentSize": 20971520,
    "FetchEnabled": true,
    "Quartz": {
      "FetchEmail": "0 0/3 * * * ?",
      "ProcessEmailQueue": "0 * * * * ?"
    }
  },
  "Attachment": {
    "MaxSize": 20971520,
    "AllowedExtensions": [".jpg", ".jpeg", ".png", ".pdf", ...],
    "UploadPath": "wwwroot/attachments"
  },
  "App": {
    "FrontendUrl": "http://localhost:5173",
    "BaseUrl": "http://localhost:5173"
  },
  "Security": {
    "_comment": "HTTPS 重定向：生产/Docker 环境下建议通过反向代理处理 HTTPS，容器内部使用 HTTP",
    "EnableHttpsRedirection": false
  }
}
```

### JWT 环境变量配置

系统支持通过环境变量配置 JWT 参数，**生产环境必须使用环境变量**。

#### 配置优先级

环境变量 > appsettings.json 配置

#### 环境变量名称

| 配置项 | 环境变量（推荐） | 环境变量（兼容） |
|--------|------------------|------------------|
| JWT 密钥 | `JWT__Key` | `JWT_KEY` |
| JWT 签发者 | `JWT__Issuer` | `JWT_ISSUER` |
| JWT 受众 | `JWT__Audience` | `JWT_AUDIENCE` |
| 访问令牌有效期（分钟） | `JWT__AccessTokenExpireMinutes` | `JWT_ACCESSTOKENEXPIREMINUTES` |

#### 设置示例

**Windows (PowerShell):**
```powershell
$env:JWT__Key="YourSuperSecretKeyForJWTTokenGeneration1234567890"
$env:JWT__Issuer="NoneadCSS"
$env:JWT__Audience="NoneadCSSApi"
$env:JWT__AccessTokenExpireMinutes="60"
```

**Linux/macOS:**
```bash
export JWT__Key="YourSuperSecretKeyForJWTTokenGeneration1234567890"
export JWT__Issuer="NoneadCSS"
export JWT__Audience="NoneadCSSApi"
export JWT__AccessTokenExpireMinutes="60"
```

**Docker:**
```bash
docker run -d \
  -e JWT__Key="YourSuperSecretKeyForJWTTokenGeneration1234567890" \
  -e JWT__Issuer="NoneadCSS" \
  -e JWT__Audience="NoneadCSSApi" \
  -e JWT__AccessTokenExpireMinutes="60" \
  noneadcss:latest
```

#### 安全要求

- **密钥长度**：至少 32 个字符
- **生产环境**：必须通过环境变量 `JWT__Key` 或 `JWT_KEY` 配置密钥
- **开发环境**：可使用 `appsettings.json` 中的配置（不推荐提交到版本控制）

### 管理员密码环境变量配置

系统支持通过环境变量 `ADMIN_PASSWORD` 配置初始管理员密码，**生产环境必须使用环境变量**。

#### 配置说明

| 配置项 | 环境变量 | 说明 |
|--------|----------|------|
| 管理员密码 | `ADMIN_PASSWORD` | 初始管理员账户（`admin@example.com`）的密码 |

#### 行为说明

- **已配置环境变量**：使用环境变量值作为管理员密码
- **未配置环境变量**：首次启动时自动生成强随机密码，并输出到应用日志（仅输出一次，请妥善保存）
- 出于安全考虑，仓库中不再保留任何硬编码的默认密码

#### 设置示例

**Windows (PowerShell):**
```powershell
$env:ADMIN_PASSWORD="YourStrongPassword123"
```

**Linux/macOS:**
```bash
export ADMIN_PASSWORD="YourStrongPassword123"
```

**Docker:**
```bash
docker run -d \
  -e ADMIN_PASSWORD="YourStrongPassword123" \
  noneadcss:latest
```

## 数据库迁移

```bash
# 创建新迁移
cd NoneadCSS.Api
dotnet ef migrations add <MigrationName> --project ../NoneadCSS.Infrastructure

# 应用迁移
dotnet ef database update --project ../NoneadCSS.Infrastructure
```

## 主要模块 API

| 模块 | 路径 | 说明 |
|------|------|------|
| 认证 | `/api/auth` | 登录、注册、密码重置、刷新令牌 |
| 会话 | `/api/conversations` | 工单管理、回复、分配、草稿、批量操作 |
| 邮箱 | `/api/mailboxes` | 邮箱配置、收取邮件、连接测试 |
| 客户 | `/api/customers` | 客户档案管理、批量删除、批量导出 |
| 用户 | `/api/users` | 用户管理、权限配置 |
| 标签 | `/api/tags` | 标签管理 |
| 工作流 | `/api/workflows` | 自动化规则 |
| SLA | `/api/sla` | 服务等级协议 |
| 宏 | `/api/macros` | 快捷命令 |
| Webhook | `/api/webhooks` | 事件推送 |
| 报告 | `/api/reports` | 统计报表 |
| 搜索 | `/api/search` | 全局搜索（会话/客户/知识库） |
| 快捷回复 | `/api/canned-responses` | 预设回复模板 |
| 邮件模板 | `/api/mail-templates` | 邮件通知模板 |
| 团队 | `/api/teams` | 团队管理 |
| 满意度 | `/api/satisfaction-ratings` | 服务评价 |
| 审计日志 | `/api/audit-logs` | 操作记录 |
| API 密钥 | `/api/api-keys` | API Key 管理 |
| 数据导入导出 | `/api/import-export` | 数据迁移 |
| 通知 | `/api/notifications` | 站内通知、模板、规则 |
| 系统选项 | `/api/system-options` | 系统配置项 |
| 角色 | `/api/roles` | 角色与权限管理 |
| 邮箱签名 | `/api/email-signatures` | 邮件签名管理 |
| 自定义字段 | `/api/custom-fields` | 自定义字段管理 |
| 模块管理 | `/api/modules` | 功能模块启停 |
| 个人中心 | `/api/profile` | 个人资料、密码、统计、草稿 |
| 首次初始化 | `/api/setup` | 安装向导：状态查询、创建管理员、创建首个邮箱、完成初始化（无独立前端页面，走 API） |
| 第三方授权 | `/api/oauth` | Outlook 等第三方 OAuth 授权与回调 |
| 本地化 | `/api/localization` | 语言列表、界面词条查询与维护 |
| 实时 | `/api/realtime` | 在线状态与实时通道辅助接口 |
| 发信日志 | `/api/send-logs` | 邮件发送记录查询 |
| 文件夹 | `/api/folders` | 会话文件夹树与计数 |
| 时间追踪 | `/api/time-tracking` | 工时记录、统计与审批 |
| 系统 | `/api/system` | 系统信息、维护与诊断操作 |
| 邮件测试 | `/api/email-test` | 发信连通性测试 |
| 日历管理 | `/api/calendar` | 日历事件 CRUD、重复日程、提醒、ICS 导入导出、订阅、Outlook 同步 |
| 知识库 | `/api/knowledge` | 分类树、文章管理、全文搜索、点赞、概览统计 |
| 在线聊天 | `/api/chat` | 客服侧聊天会话、消息撤回/编辑、@提及、转工单 |
| 客户聊天 Widget | `/api/chat/token/{sessionToken}` | 客户侧匿名聊天（基于 sessionToken 鉴权） |
| 微信集成 | `/api/wechat` | 微信公众号消息推送/接收、签名校验、配置读写、连接状态、客服回复、模板消息/订阅通知双模式、自定义菜单 CRUD（10 个端点：含菜单发布/拉取/删除/草稿保存） |
| WhatsApp 集成 | `/api/whatsapp` | WhatsApp Business Platform Webhook 接入（HMAC-SHA256 签名验证）、配置管理、状态检查、会话回复（文本+媒体）、模板消息发送、24h 窗口控制 |
| 钉钉集成 | `/api/dingding` | 钉钉企业内部应用配置（AppKey/AppSecret/AgentId）、消息接收模式（HTTP/Stream）、状态监控、消息回复、模块权限控制 |
| 飞书集成 | `/api/feishu` | 飞书开放平台接入、配置管理、消息接收/回复、模块权限控制 |
| Messenger 集成 | `/api/messenger` | Meta Graph API Webhook 接入（`X-Hub-Signature-256` HMAC-SHA256 校验）、配置管理、状态检查、会话回复（文本 2000 字符分片 + 附件）、订阅字段管理、连接测试 |
| Telegram 集成 | `/api/telegram` | Telegram Bot Webhook（`X-Telegram-Bot-Api-Secret-Token` 校验）与长轮询双模式、配置管理、状态检查、会话回复（4096 分片 + `parse_mode` + 论坛话题）、Webhook 注册/删除、连接测试 |
| LINE 集成 | `/api/line` | LINE Messaging API Webhook（`X-Line-Signature` 校验）、配置管理、配额与状态、会话回复（replyToken→push 兜底 + 附件）、人工接管、连接测试 |
| iMessage 集成 | `/api/imessage` | BlueBubbles 桥接 Webhook（共享密钥校验）与 Apple Messages for Business Webhook（`WebhookSecret` 校验）、配置管理、状态检查、会话回复（文本 + 附件）、Webhook 注册、连接测试 |
| Outlook 同步 | `/api/outlook-sync` | OAuth 授权、日历双向同步 |

## 开发规范

### 代码风格

- 使用 C# 12 特性（顶层语句、文件范围命名空间）
- 使用 `Nullable` 引用类型
- 遵循 .NET 命名规范（PascalCase 类名、camelCase 参数）

### 架构原则

- 分层架构：API → Application → Domain → Infrastructure
- 依赖倒置：上层依赖抽象，下层实现
- 领域驱动设计：实体包含业务逻辑，避免贫血模型
  - 核心实体封装业务方法：`Conversation`、`ConversationThread`、`Webhook`、`Attachment`、`SendLog`、`SatisfactionRating` 等
  - 仓储模式：专用仓储用于复杂查询，泛型仓储用于基础 CRUD
- 数据库索引优化：高频查询路径添加复合索引（会话、线程、收藏、附件、通知规则等）
  - NotificationRule：新增 4 个复合索引覆盖 EventType+IsActive+MailboxId+UserId+DeletedAt 等组合查询路径
- EF Core 性能优化（P1）：
  - **AsNoTracking 全覆盖**：所有仓储类（WorkflowRepository、SlaRepository、NotificationRuleRepository、ChatSessionRepository、ChatMessageRepository 等 37 个仓储）的只读查询方法均添加 `.AsNoTracking()`，减少 ChangeTracker 内存开销
  - **批量操作优化**：ConversationService 的 `BulkUpdatePriorityAsync` / `BulkDeleteAsync` 改用 EF Core `ExecuteUpdateAsync` 生成单条 SQL，替代 N+1 次事务
  - **ChatSession/ChatMessage 仓储**：所有只读查询补充 AsNoTracking，适配在线聊天高并发场景
- 前端性能优化（P1）：
  - **Vite Bundle Splitting**：vite.config.ts 配置 `manualChunks`，将第三方库拆分为 vendor、query、signalr、lucide、utils 5 个独立 chunk，优化浏览器缓存命中率
  - **定时器优化**：微信/WhatsApp 48 小时/24 小时窗口刷新改用 chained setTimeout 模式替代 setInterval，消除定时器泄漏与漂移
  - **useMemo 反模式修复**：ConversationDetail 中 ref 写入从 useMemo 移至独立 useEffect，确保纯派生状态计算
- 配置化设计（P2-1）：硬编码 TimeSpan 通过 IOptions<T> 集中管理（ChatOptions / CacheOptions / OAuthOptions / IngestionMatchOptions / WebhookOptions / SignalROptions / RetryOptions 等多配置节），支持环境变量覆盖
- Docker 优化：Dockerfile 移除冗余 wwwroot/attachments 目录创建与 Volume 声明，Data 目录大小写统一

### 错误处理

- 使用 `GlobalExceptionMiddleware` 统一异常处理
- 返回标准化错误响应格式
- 记录详细的错误日志

### 测试
- **后端测试框架**：xUnit + Moq + FluentAssertions
- **后端测试规模**：124 个测试类 / 2233 个用例（2228 通过，0 失败，5 跳过；2026-11-23 用进程内执行器实测）
- **后端测试覆盖率**：行覆盖 5.1%+，分支覆盖 13.2%+，方法覆盖 18.4%+（2026-08-05 审计快照）
- **前端测试**：Vitest 18 个测试文件 / 684 个用例全部通过；硬编码中文门禁 `npm run i18n:check` 为 0 处
- **核心服务覆盖**：13+ 个服务单元测试覆盖率 ≥ 90%（API Key、Dashboard、草稿、分配、团队、模块、标签、邮件签名、快捷回复、系统配置、密码策略、角色、ChatService）
- **测试基础设施**：`TestAsyncQueryProvider` 支持 EF Core 异步 LINQ Mock；`InternalsVisibleTo` 暴露内部方法供测试
- **前端测试基础设施**（P2-3）：Vitest + @testing-library/react + happy-dom/jsdom；配置文件 `vitest.config.ts`、`src/test/setup.ts`；支持组件测试、工具函数测试
- 详细说明见 [开发文档 - 单元测试章节](docs/development.md#23-单元测试)

```bash
# 运行后端测试（标准方式）
dotnet test NoneadCSS.Tests/NoneadCSS.Tests.csproj

# 本机若因 testhost 权限限制无法执行（Win32Exception 5），改用进程内执行器：
dotnet build NoneadCSS.Tests/NoneadCSS.Tests.csproj -c Release
dotnet NoneadCSS.Tests/bin/Release/net10.0/BackendTestRunner.dll \
       NoneadCSS.Tests/bin/Release/net10.0/NoneadCSS.Tests.dll

# 生成覆盖率报告
dotnet test NoneadCSS.Tests/NoneadCSS.Tests.csproj --collect:"XPlat Code Coverage"
reportgenerator -reports:"TestResults-Coverage/*/coverage.cobertura.xml" -targetdir:"TestResults-Coverage/Report"

# 运行前端测试
cd frontend
npm run test
```

> 进程内执行器的用法与参数（`--list` / `--filter` / 结果文件）见 [scripts/backend-test-runner/README.md](scripts/backend-test-runner/README.md)。

### 开源发布预检（三条只读检查）
发布前跑一次聚合门禁，任何一项失败即退出码 1，可直接挂进 CI：

```bash
node scripts/oss-preflight.mjs          # 密钥扫描 + 文档链接 + 文案词典覆盖
node scripts/oss-preflight.mjs --quick  # 跳过较慢的密钥扫描，用于日常提交
```

三条检查也可单独运行：

```bash
node scripts/oss-secret-scan.mjs                              # 13 条规则：凭据、内网主机、盘符源码路径、用户目录、PEM 等
node scripts/oss-doc-links.mjs check                         # 文档死链与历史机器路径（file:///），有死链退出码 1
node scripts/i18n/api-message-dictionary.mjs coverage        # 响应出口词典是否覆盖源码里全部中文文案
```

注意：真正的密钥值不入库（`deploy/secrets.cmd` 已被忽略，仅 `deploy/secrets.cmd.example` 入库），数据库导出也不应随包发布；发布到新仓库前建议对数据库**副本**执行 `scripts/oss-db-scrub.sql`。详见 [开发文档 - 开源发布预检](docs/development.md#98-开源发布预检脚本)。

## 相关文档

- [开发文档](docs/development.md) - 详细的开发指南
- [用户手册](docs/拓德客户服务与支持系统用户使用手册.md) - 用户操作手册
- [版本更新日志](CHANGELOG.md) - 各版本变更记录
- [安全审计报告（2026-08-06）](docs/security-audit-report-2026-08-06.md) - 安全维度审计结论与修复记录
- [技术债务清单 2026 Q4](docs/NoneadCSS_TechnicalDebt_2026Q4.md) - 7 项高风险技术债务（仓储层事务嵌套、SaveChangesAsync 违规、报表全量内存化、批量操作工作流触发、权限解析无缓存、MessageId 缺唯一约束、乐观锁与 API 版本控制）
- [架构级遗留项技术方案（2026-10-23）](docs/NoneadCSS-架构级遗留项技术方案-20261023.md) - SQLite 乐观锁、工作流持久化、导入导出扩面、授权粒度、群聊、多租户 6 项方案
- [渠道集成手册](docs/integration/channel-integration-playbook.md) - 新增 IM 渠道的共享注册点、文件清单与验收清单（Messenger / Telegram / LINE / iMessage 已按此落地）
- [数据库备份与恢复指南](docs/database-backup-restore-guide.md) - 生产库备份、恢复与演练步骤
- [API 端点清单](scripts/css-api/endpoints.md) - 从生产 OpenAPI 导出的 594 个操作 / 50 个分组，供直连脚本与联调核对

## 技术债务

本项目于 2026-08-05 完成全面代码审计（代码质量、安全、功能完整性、性能、测试 5 个维度），共发现 38 个问题，修复 31 个，跳过 7 项高风险/范围大问题收录至 [docs/NoneadCSS_TechnicalDebt_2026Q4.md](docs/NoneadCSS_TechnicalDebt_2026Q4.md) 安排下季度专项迭代：

| 编号 | 问题 | 优先级 | 工作量 |
|------|------|--------|--------|
| TD-01 | 仓储层事务嵌套缺陷（UnitOfWork 不支持嵌套） | P0 | 3-5 天 |
| TD-02 | 仓储层普遍调用 SaveChangesAsync（30+ 处违规） | P1 | 1-2 周 |
| TD-03 | ReportService 报表全量内存化（含 SQLite 999 参数上限 Bug） | P0 | 1 周 |
| TD-04 | ConversationService 批量操作工作流触发（250+ DB 往返） | P1 | 1-2 周 |
| TD-05 | UserRoleResolver 权限解析完全无缓存（每页面数十次 DB 往返） | P0 | 3-5 天 |
| TD-06 | ConversationThread.MessageId 缺唯一约束（TOCTOU 竞态） | P0 | 3-5 天 |
| TD-07 | 高并发表缺乐观锁 + 全部 Controller 无 API 版本控制 | P1 | 1-2 周 |
| TD-07a 进度 | **多数已落地，仅 API 版本控制未做**：13 张表（Users / Customers / ChatSessions / ChatMessages / ApiKeys / Webhooks / KnowledgeCategories / KnowledgeArticles / CalendarEvents / TimeEntries / Conversations / ConversationThreads / Mailboxes）具备 RowVersion 并发令牌；服务层 `ConcurrencyCheckHelper` 已覆盖 Users / ApiKeys / Webhooks / NotificationRules / Chat / Customers / Knowledge / TimeEntries，前端在日历、时间追踪与知识库互动接口发送 `If-Match`，冲突返回 409 并回传最新快照。Controller 路由 API 版本控制（`/v1/` 前缀）仍待实施。 | | |

## 许可证

AGPL-3.0 License

---

**NoneadCSS** - 拓德客户服务与支持系统