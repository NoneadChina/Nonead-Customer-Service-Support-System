# Messenger（Facebook Messenger Platform）接入要点

> **状态（2026-11-23 复核，v2.3.0 工作树）**：本渠道已实现并交付——`SourceType.Messenger = 11`、模块名 `messenger`、前端页面 `Messenger.tsx`、回调 `/api/messenger`；默认值见 `docs/development.md` §60，用户步骤见用户手册 11.10。本文保留为平台事实与实现取舍的取证记录。

> 来源：Meta 官方页在本机不可达，以下按可达镜像 + 既有实现推定，**标注「待证实」的项需在真实应用后台核对**。Graph API 当前版本 **v25.0**（2026-02-18 发布），版本号必须做成配置项（不要写死）。

## 1. 入站（Webhook）
- 服务器需**公网 HTTPS**（TLS1.2+，443）。
- **GET 校验**：query `hub.mode`(=`subscribe`)、`hub.verify_token`、`hub.challenge`；`mode==subscribe && verify_token==配置值` ⇒ 返回 200 且 body 为 `hub.challenge` **原文**（纯文本，不是 JSON），否则 403。
- **POST 事件**：body `{"object":"page","entry":[{"id":"<PAGE_ID>","time":..,"messaging":[…]}]}`，单次最多 **1000 条**，必须逐 entry、逐 messaging 处理。
- **签名**：头 `X-Hub-Signature-256: sha256=<hex>` = HMAC-SHA256(**原始 body 字节**, AppSecret)。必须在 JSON 绑定之前读原始流；恒定时间比较；旧版 `X-Hub-Signature`(SHA-1) 不用。签名不含时间戳 ⇒ 无防重放，靠 `message.mid` 去重。
- **必须快速 200**（先入队后处理）。非 200 会被重试（立即→降频→约 36 小时后丢弃），重复失败可能退订 webhook。
- 事件项：`sender.id`(PSID)、`recipient.id`(Page)、`timestamp`(ms)、`message.{mid,seq,text,attachments[],quick_reply,reply_to,is_echo}`、`postback.{title,payload,data}`、`optin.ref`、`read/delivery`。
- **`is_echo:true` 必须跳过**（否则自问自答死循环）；同时守卫 `sender.id == PageId`。

## 2. 出站（Send API）
- `POST https://graph.facebook.com/{ver}/me/messages`，`Authorization: Bearer <Page Access Token>`。
- body：`{"recipient":{"id":"<PSID>"},"message":{"text":".."} 或 {"attachment":{...}},"sender_action":"typing_on","notification_type":"REGULAR","message_tag":{"tag":"HUMAN_AGENT"}}`。
- **文本上限按 2000 字符切分**（5000/10000 说法未证实，不要依赖）。
- **24 小时客服窗口**：窗口内可自由回复；窗口外需 `message_tag`（客服场景用 `HUMAN_AGENT`）。会话模型需记录「最近用户消息时间」并在超窗时带 tag 或提示坐席。
- Page Access Token：**无刷新端点**；长期令牌约 60 天，Business/System User 授予的可不过期。到期需人工换发 ⇒ 状态接口要能报「令牌失效」并告警。
- 错误码：`551` 收件人不可达（拉黑/停用 ⇒ 标记不可投递、停止自动重试）、`613` 限流（指数退避 + 每 PSID 串行）、`190` 令牌失效（硬失败）、`10` 权限/参数、`2635` PSID 与 Page 不匹配。错误恒为 HTTP 400 + `{"error":{...,"code":..,"fbtrace_id":..}}`，`fbtrace_id` 需入库便于报障。

## 3. 媒体
- 入站 `message.attachments[]`：`{type:image|audio|video|file|location|fallback, payload:{url,title,attachment_id}}`；`payload.url` 是短 TTL 签名 CDN ⇒ **收到即下载转存**。
- 出站三种：URL 直发（`payload.url` + `is_public:true`）／multipart `filedata` 上传／Attachment Upload API `POST /{ver}/me/message_attachments` 取 `attachment_id`（需 `is_reusable:true`）后按 id 发送。

## 4. 身份与去重
- 外部 ID = **PSID**（`sender.id`，Page 作用域、稳定、非真实 FB UID）。映射键 `(channel="messenger", external_id=PSID, page_id)`；`entry.id`/`recipient.id` 为 Page。
- 幂等键 = `message.mid`（跨重试与 echo 稳定）。
- 客户资料：`GET /{ver}/me/{PSID}?fields=first_name,last_name,profile_pic,locale,timezone,gender`。

## 5. 需要订阅的字段
`messages, messaging_postbacks, messaging_optins, messaging_handovers, message_deliveries, message_reads, message_reactions, messaging_referrals, account_linking`（`message_echoes` 仅在需要同步坐席转录时订阅）。订阅：`POST /{ver}/me/subscribed_apps?subscribed_fields=…`（需 `pages_manage_metadata`）。应用处于 Development 模式只发测试通知。

## 6. 配置键
`ApiVersion`(默认 v25.0) · `AppId` · `AppSecret` · `VerifyToken` · `PageId` · `PageAccessToken`(加密、只写不回显) · `CallbackPath`(默认 `/api/messenger`) · `SubscribedFields` · `TextChunkChars`(2000) · `WindowHours`(24) · `MessageTagPolicy` · `TypingIndicator`/`MarkSeen` · `MediaDownloadMaxMB` · `RateLimitBackoffTries`。

## 7. 暂不实现（记录为后续）
Handover / 线程控制（`pass_thread_control` 等）单应用自建坐席台不需要；Page Inbox app id 各源冲突（263902037430500 与 …900），需要时用 `GET /me/secondary_receivers` 读取，**不要写死**。
