# Telegram（Bot API）接入要点

> **状态（2026-11-23 复核，v2.3.0 工作树）**：本渠道已实现并交付——`SourceType.Telegram = 12`、模块名 `telegram`、前端页面 `Telegram.tsx`；Webhook 与长轮询两种入站模式可切换，默认值见 `docs/development.md` §60，用户步骤见用户手册 11.11。本文保留为平台事实与实现取舍的取证记录。

> 版本口径：Bot API **10.4**（官方 `tdlib/telegram-bot-api` 2026-11-09 提交「Update version to 10.4」确认）。`core.telegram.org` 本机不可达，字段级事实取自官方服务器 README（逐字）与按 Bot API 10.3 镜像的类型定义；标「待证实」项需在真实机器人上核对。

## 1. 鉴权与基址
- 令牌来自 @BotFather。基址 `https://api.telegram.org/bot<TOKEN>/<method>`（方法名大小写不敏感，UTF-8）。**令牌在 URL 路径里**，无 OAuth、无 body 签名。
- 支持 `application/json` 与 `multipart/form-data`（上传文件必须 multipart）。
- **自建 Local Bot API Server**（可选）：默认 8081，需 `--api-id/--api-hash`；`--local` 模式下载无上限、上传上限 **2000 MB**（不是 5 GB）、webhook 可纯 HTTP、`getFile` 返回**本地绝对路径**。迁移到自建服务器前**必须先对 api.telegram.org 调 `logOut`**。服务器只说 HTTP，对外需 nginx 前置 TLS。

## 2. 入站：两种模式都要支持（对齐仓库里 DingTalk/Feishu 的 Stream 开关做法）
- **Webhook**（生产推荐）：`setWebhook(url, secret_token, max_connections 1–100, drop_pending_updates, ip_address, certificate)`。每次 POST 带 `X-Telegram-Bot-Api-Secret-Token`，**恒定时间比对，不匹配直接 401 丢弃**（无 body 签名可校验）。`getWebhookInfo` 供状态页展示 `pending_update_count/last_error_message/last_error_time`。持续报错会被重投并最终停用 webhook ⇒ 快速返回 200、异步处理。
- **轮询**：`getUpdates(offset, limit 1–100, timeout 0–50s, allowed_updates[])`，持久化 `last update_id+1`。webhook 生效时 `getUpdates` 返回 **409 conflict** ⇒ 切换模式要先 `deleteWebhook`。
- **隐私模式**：默认在群里只收到命令/@ 回复；要看全部群消息需把机器人设为群管理员或在 @BotFather 关闭 privacy（加群之前）。机器人收不到其他机器人的消息。
- `Update` 内**至多一个**可选字段存在；`update_id` 递增但**静默一周后会随机重置** ⇒ 只用于排序/去重游标，不能当永久键。聊天内唯一键是 `chat.id + message_id`。

## 3. 入站报文形状（关键字段）
`Update{update_id, message|edited_message|channel_post|callback_query|my_chat_member|…}`；
`Message{message_id, from{id,is_bot,first_name,last_name,username,language_code}, chat{id,type:"private"|"group"|"supergroup"|"channel",title}, date(unix秒), text, entities, photo[], document, audio, voice, video, sticker, contact, location, reply_to_message, message_thread_id}`。
群/频道 `chat.id` 为负（supergroup/channel 以 `-100` 开头）；1:1 的 `chat.id == from.id`。

## 4. 出站
- `sendMessage(chat_id, text 1–4096, parse_mode|entities, link_preview_options, disable_notification, reply_parameters{chat_id,message_id,allow_sending_without_reply,quote}, message_thread_id)`。
- 媒体：`sendPhoto/sendDocument/sendVideo/sendAudio/sendVoice/sendAnimation/sendSticker/sendMediaGroup(2–10)`；`sendChatAction(chat_id, action)`（typing 最长 5 秒，需重发）。
- 文件值三种形态：`file_id`（可复用、按机器人作用域、不过期）／HTTPS URL／multipart 上传（自建服务器可给本地路径）。
- `editMessageText/deleteMessage` 是「坐席改口」的唯一手段；已发消息不能静默撤回。
- `answerCallbackQuery(callback_query_id, text≤200)` **必须**为每个 callback_query 调用（id 仅 10 秒有效）。
- 响应恒为 `{"ok":true,"result":…}` 或 `{"ok":false,"error_code":..,"description":"..","parameters":{"retry_after":..,"migrate_to_chat_id":..}}`。发送成功要把 `result.message_id` 存成平台↔Telegram 映射。

## 5. 媒体下载
`getFile(file_id)` → `{file_id,file_unique_id,file_size,file_path}`，再 `GET https://api.telegram.org/file/bot<TOKEN>/<file_path>`。托管 API：**下载 20 MB / 上传 50 MB**；自建服务器下载无上限、上传 2000 MB。`file_unique_id` 是跨机器人稳定 id ⇒ 与 `file_id` 一起入库避免重复下载。照片是尺寸阶梯数组，取最大档。

## 6. 身份与链接
- 外部 ID = **`from.id`（64 位整数！.NET 用 `long`，JS 用 bigint）**，同一用户永久稳定、跨机器人一致；`username` 可选且可改，只作展示。
- 会话键：1:1 用 `chat.id`；群用 `chat.id + message_thread_id`。
- 账号绑定：深链 `https://t.me/<bot_username>?start=<≤64 字符一次性签名令牌>`，用户首次消息即 `/start <PARAM>` ⇒ 校验令牌后建立 `telegram_user_id ↔ 客户` 绑定。
- 测试连通性：`getMe` → `{id,is_bot,first_name,username,can_join_groups,can_read_all_group_messages,supports_inline_queries}`；`can_read_all_group_messages:false` 即隐私模式开着。

## 7. 硬约束（UI 必须体现）
- **机器人不能主动发起私聊**：未联系过用户发送会 403 `bot can't initiate conversation with a user` ⇒ 旅程必须用户先触发（深链/二维码/站内 t.me 链接）。
- **没有 24 小时窗口**（与 Messenger/WhatsApp 不同），任何时间可回；但用户可 block（403 `bot was blocked by the user`，终态直到解除）。
- 无已读/送达回执；无第三方输入指示（只有 `sendChatAction` 的「机器人正在输入」）。
- 限流：整体 ≤30 条/秒、每聊天约 1 条/秒、同群 ≤20 条/分钟；文本 ≤4096 ⇒ 超长坐席回复必须切分或转成文档。

## 8. 错误与重试
| 码 | 含义 | 动作 |
|---|---|---|
| 429 / 420 | 限流，`parameters.retry_after` 秒 | 按 retry_after 退避 + 抖动，每机器人共享令牌桶；420 只退避该聊天 |
| 400 | chat not found / message not found / can't parse entities / BUTTON_URL_INVALID | 不重试；实体解析失败可去掉 parse_mode 重试一次 |
| 403 | 用户屏蔽 / 不在群里 | 不重试，标记会话 blocked |
| 401 | 令牌无效 | 硬失败，配置页告警 |
| 404 | 方法路径错 | 硬失败 |
| 409 | webhook 与轮询冲突 | `deleteWebhook` 后继续 |
| 5xx/超时 | Telegram 侧或网络 | 指数退避重试（可能重复发送 ⇒ 用自己的关联 id 去重）；`migrate_to_chat_id` 要改写 chat_id |

## 9. 配置键
`Enabled · Mode(webhook|polling) · BotToken(加密) · ApiBaseUrl(默认 https://api.telegram.org) · UseLocalBotApiServer · WebhookUrl · WebhookSecretToken · WebhookCertificate · WebhookIpAddress · MaxWebhookConnections · AllowedUpdates[] · PollingTimeout · PollingLimit · BotUsername · LinkTokenHmacKey + LinkTokenTtlMinutes · RateLimitGlobalPerSec/PerChatPerSec/GroupPerMin · MaxMessageChars(4096) · AcceptGroupMessages · DeleteWebhookOnSwitch`。
