# LINE（Messaging API）接入要点

> **状态（2026-11-23 复核，v2.3.0 工作树）**：本渠道已实现并交付——`SourceType.Line = 13`、模块名 `line`、前端页面 `Line.tsx`、回调 `/api/line/webhook`；默认值见 `docs/development.md` §60，用户步骤见用户手册 11.12。本文保留为平台事实与实现取舍的取证记录。

> 取证来源：LINE 官方文档镜像 `github.com/line/line-developers-docs-source`（= developers.line.biz 内容，该站在本机返回 403）与 LINE 官方 OpenAPI `github.com/line/line-openapi`（`messaging-api.yml` 67 个 path、`webhook.yml`、`channel-access-token.yml`、`module.yml`，Apache-2.0）。基址 **`https://api.line.me`**，媒体 **`https://api-data.line.me`**。⚠ = 未能从官方文本确认，落码前需在开发者控制台核对。

## 1. 凭据
| 项 | 事实 |
|---|---|
| 鉴权 | 每个 api.line.me 调用带 `Authorization: Bearer <channel access token>` |
| Channel Secret | 仅用于 webhook body 的 HMAC（以及签发/吊销令牌时作 `client_secret`） |
| 短期令牌 | `POST /v2/oauth/accessToken`（`grant_type=client_credentials`, `client_id`, `client_secret`）→ `expires_in: 2592000`（30 天）。规范原文：**“Valid for 30 days. Note: Channel access tokens cannot be refreshed.”** |
| 长期令牌 | 控制台签发，**不显示过期时间**（30 天只适用于短期令牌 API）。用 `POST /v2/oauth/verify` → `{client_id, expires_in, scope}`（scope 形如 `"P CM"`）核查；吊销 `POST /v2/oauth/revoke` |
| v2.1（JWT 断言） | `POST /oauth2/v2.1/token`，`client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer` + `client_assertion`（用控制台 Assertion Signing Key 自签）→ `{access_token, token_type:"Bearer", expires_in, key_id}`；配套 `GET /oauth2/v2.1/verify`、`POST /oauth2/v2.1/revoke`、`GET /oauth2/v2.1/tokens/kid`。最大有效期 ⚠ |
| 无状态令牌 | `POST /oauth2/v3/token` → 规范原文：“no max active token limit… only valid for 15 minutes but cannot be revoked until it naturally expires.” ⇒ 不适合常驻 webhook 桥接 |

## 2. Webhook
控制台 → Messaging API 渠道 → Callback URL + **Use webhook** 开关。程序化：`GET/PUT /v2/bot/channel/webhook/endpoint`、`POST /v2/bot/channel/webhook/test`。

签名：头 `X-Line-Signature: Base64(HMAC-SHA256(raw_body_bytes, ChannelSecret))`。必须对**原始字节**计算（不可先反序列化再重序列化），恒定时间比较；先快速回 2xx，再异步处理。LINE 的 webhook 响应超时与重试策略 ⚠（常见说法 10s）⇒ 一律按 `message.id`/`replyToken` 幂等。

入站：`{"destination":"U<机器人 userId>","events":[{...}]}`，事件 `type` ∈ `message`(子类型 `text/image/video/audio/file/location/sticker`)、`follow`、`unfollow`、`join`、`leave`、`memberJoined`、`memberLeft`、`postback`、`beacon`、`accountLink`、`unsend`、`videoPlayComplete`。
`mode` = `active` | `bot`（**`bot` 表示渠道处于停用/受限模式 ⇒ 不要回复**）。群/房间带 `groupId`/`roomId` 与 `source.type=group|room`。启用 Chat 时消息事件带 `markAsReadToken` ⇒ `POST /v2/bot/chat/markAsRead`。

```json
{"destination":"U…","events":[{"type":"message","replyToken":"nHmv…","userId":"U…",
  "timestamp":1763000000000,"mode":"active","source":{"type":"user","userId":"U…"},
  "message":{"id":"10987…","type":"text","text":"hi","quoteMessageId":"…"}}]}
```

## 3. 出站（全部 POST，`messages[]` 数组）
| 用途 | 路径 |
|---|---|
| 回复 | `/v2/bot/message/reply`（`{replyToken, messages[]}`） |
| 推送 | `/v2/bot/message/push`（`{to:"U…", messages[]}`） |
| 多播 | `/v2/bot/message/multicast`（`to` ≤500） |
| 定向 | `/v2/bot/message/narrowcast` + `GET /v2/bot/message/progress/narrowcast` |
| 广播 | `/v2/bot/message/broadcast` |
| 预检 | `/v2/bot/message/validate/{reply,push,multicast,narrowcast,broadcast}`（配置页「测试」用它） |
| 配额 | `GET /v2/bot/message/quota`、`GET /v2/bot/message/quota/consumption`、`GET /v2/bot/message/delivery/{reply,push,multicast,broadcast}` |

- **replyToken 一次性且极短命 ⚠（官方措辞见过 30 秒；不得假定 ≥60s）** ⇒ 收到事件后立刻回；来不及就用 `push` 兜底（消耗配额、且仅限已加好友用户）。单次请求 `messages[]` ≤5 ⚠。
- 消息对象：`text{text,emojis[],quoteMessageId}`、`image{originalContentUrl,previewImageUrl}`、`video(+duration ms)`、`audio{originalContentUrl,duration ms}`、`file{originalContentUrl,fileName,fileSize}`、`location`、`template{buttons|confirm|carousel|image_select}`、`imagemap`、`flex{altText,contents}`。

## 4. 媒体
入站：`GET https://api-data.line.me/v2/bot/message/{messageId}/content`（Bearer）→ 二进制；另有 `/content/preview`、`/content/transcoding`（视频 HLS）。保留时间很短 ⚠ ⇒ **收到即下载转存**。
出站：**没有聊天媒体的上传接口**（`messaging-api.yml` 只有富媒体菜单图 `PUT /v2/bot/richmenu/{richMenuId}/content`）⇒ 附件必须由本系统以 HTTPS 公开 URL 提供（自有域名、不可重定向/鉴权），⚠ 流传的 `upload.line.me` 在官方源里不存在，视为不可用。→ 实现上需要一个「附件外链签发」端点（带短期 token 的公开 URL），这是与其他渠道最大的差异点。

## 5. 身份
`userId` 为 **渠道作用域不透明 id**（`U` + 32 hex）⇒ 天然的外部客户键（同一人在两个 OA 里 id 不同）。群成员：`/v2/bot/group/{groupId}/members/ids`。资料：`GET /v2/bot/profile/{userId}` → `{displayName,userId,pictureUrl,statusMessage,language}`；**未加好友/已屏蔽返回 404**。粉丝列表 `GET /v2/bot/followers/ids`。绑定：`GET /v2/bot/user/{userId}/linkToken` → 作为 LINE Login 的 `state`。
⚠ **`GET /v2/bot/friendships/{userId}` 已从现行文档与官方 OpenAPI 消失**（2026-03-30 与 2026-11-09 两版均 0 命中）⇒ 用 `profile` 的 200/404 判断好友关系。

## 6. 人工接管
⚠ 旧的 “Human handover” `GET|PUT /v2/bot/chat/{chatId}/active` + `startedAsHuman` 在现行官方文档与 OpenAPI 中**不存在**；官方现存的 `/v2/bot/chat/*` 只有 `loading/start`（输入指示，需控制台开启）与 `markAsRead`。官方许可的接管机制是 module 渠道的 Chat Control（`POST /v2/bot/chat/{chatId}/control/acquire|release`，`ttl` 默认 3600s、上限 31536000s），但需 LINE module 合作方资格。
⇒ 自建桥接的接管要在本系统内实现：每会话 `humanTakeoverUntil`，接管期间机器人不自动回复（仍回 200），坐席用 `push` 发送，并调 `loading/start` + `markAsRead` 让 LINE 端显示「正在输入 / 已读」。

## 7. 限额与错误
文本 ≤5000 字符 ⚠。速率：`narrowcast/broadcast/insights/webhook test` 家族 **60 次/小时**；push/multicast 的每秒上限 ⚠（200/s、500/s）。月配额按套餐（Free 500 / Light 5,000 / Standard 30,000 + 加购包）⚠，但已确认：**reply 不消耗配额**，push/multicast/narrowcast/broadcast 按收件人消耗。错误体 `{message, details:[{message,property}]}`：400 参数、401 令牌、403 配额/权限、404 非好友、409 重复、429 限流（退避+抖动）、500/503 重试。

## 8. .NET
LINE 官方 SDK 只有 Java/Python/PHP/Node/Go/Ruby，**无官方 C# SDK**（NuGet `line-bot-sdk-csharp` 属社区）。⇒ 用 `HttpClient` + `System.Text.Json`，或从 `line-openapi` 生成客户端。

## 9. 配置键
`ChannelId`(只展示) · `ChannelSecret`(webhook HMAC) · `ChannelAccessToken`(Bearer；长期令牌粘贴，或 v2.1 断言密钥路径 + keyId) · `WebhookUrl`(公网 HTTPS) · `WebhookEnabled` · `RichMenuId` · `ApiBaseUrl`(https://api.line.me) · `DataApiBaseUrl`(https://api-data.line.me) · `AttachmentPublicBaseUrl`(出站附件外链) · `MarkAsReadEnabled` · `HumanHandoverMuteMinutes` · `QuotaWarnPercent` · `TextChunkChars`(5000)。

**硬规则**：按 `message.id` 去重；`mode=bot` 不处理；媒体在 webhook 处理内同步下载转存；不要依赖 `/friendships` 与 `/chat/{chatId}/active`；replyToken 秒级内使用，失败退化为 push。
