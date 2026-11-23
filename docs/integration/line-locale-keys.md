# LINE — i18n 键清单（供 locale 文件登记；本 agent 不改 locale）

> **状态（2026-11-23）**：`channels.line.*` 与 `conversations.channel.line.*` 已登记进 `frontend/src/locales/{zh-Hans,en}`，`npm run i18n:check` 0 处缺口；本文保留作键清单存档。

组件用到的 `channels.line.*` / `conversations.channel.line.*` 键与现有 locale 的差异，由脚本
（按 `frontend/src/locales/zh-Hans/{channels,conversations,common,format}.ts` 扁平键比对）实测：

| 文件 | 引用的 key 命名空间 | 已定义可直接复用 | 需新增 |
|------|--------------------|------------------|--------|
| `frontend/src/pages/Line.tsx` | 128（channels.*） | 50（全部为 `channels.common.*`） | **78**（全部 `channels.line.*`） |
| `frontend/src/components/LineReplyBox.tsx` | 43（conversations.*） | 21（reply / channel 通用） | **22**（全部 `conversations.channel.line.*`） |

复用的 `channels.common.*`（已在 `locales/zh-Hans/channels.ts` 存在，**无需新增**）：
`refresh, copy, copyFailed, saveConfig, configGuide, configured, notConfigured, configuredButDisabled,
maskedConfigured, on, off, apiReachable, tokenValid, tokenInvalid, basicConfig, callbackAddress,
linkedMailbox, autoAssignMailbox, disabledSuffix, mailboxHint, enableAutoReply, autoReplyHint,
autoReplyPlaceholder, autoReplyContent, charCountTitle, enableChannel, loadFailed, saveFailed,
saveSucceeded, messageTypes, msgType.{text,textDesc,image,imageDesc,video,videoDesc,audio,audioDesc,file,fileDesc}`
（变量：`mailboxHint / enableChannel / loadFailed / saveFailed / saveSucceeded` 都收 `{channel}`）。

复用的 `conversations.reply.*` / `conversations.channel.*`（已存在）：
`uploadFailed, dropToUpload, quickReply1..6, quickReplies, quick, quickRepliesHint, remove,
removeImageAria, removeAttachmentAria, uploading, attachments, charCountTitle, sendingEllipsis, send,
textAndAttachments, remainingSuffix({remaining})`。

---

## 0. ⚠️ 已存在的 `channels.line.*` 块有错，需要**替换**而不是追加

`locales/zh-Hans/channels.ts:662-672` 与 `locales/en/channels.ts:662-672` 里已预置 11 条
`channels.line.*`，其中 `guide.step2..step5` 是**微信公众号**的文案（AppID/AppSecret、自定义 Token、
EncodingAESKey、服务器配置→URL），LINE 没有这些字段，照原样渲染会误导运维。可保留复用：
`channels.line.title / subtitle / loading / status.enabled / status.notConfiguredHint / test.failed / form.enableHint`。

需要替换的 4 条（zh-Hans）：
```ts
'channels.line.guide.step2': '在 LINE Developers 控制台的 Messaging API 频道里取 Channel ID 与 Channel secret',
'channels.line.guide.step3': '在「Messaging API」页签发 Channel access token（长期令牌），并确认 Key ID 与上方一致',
'channels.line.guide.step4': '在「Webhook settings」里把回调地址设为 https://<你的公网域名>/api/line/webhook 并打开 Use webhook',
'channels.line.guide.step5': '点「校验回调」让 LINE 侧 POST /v2/bot/channel/webhook/test 复检；LINE 无 GET hub.challenge 握手',
```
（en）：
```ts
'channels.line.guide.step2': 'In the LINE Developers console, open the Messaging API channel and copy the Channel ID and Channel secret',
'channels.line.guide.step3': 'Issue a long-lived Channel access token on the "Messaging API" tab and confirm its Key ID matches the one above',
'channels.line.guide.step4': 'Under "Webhook settings", set the callback URL to https://<your public host>/api/line/webhook and turn Use webhook on',
'channels.line.guide.step5': 'Press "Verify callback" to have LINE run POST /v2/bot/channel/webhook/test; LINE has no GET hub.challenge handshake',
```

---

## 1. 变量名（组件实际传的插值参数）

| key | 变量 |
|-----|------|
| `channels.line.quota.usedPercent` | `{percent}` `{warn}` |
| `channels.line.push.sent` | `{via}` |
| `conversations.channel.line.fileTooLarge` | `{name}` `{limit}` |
| `conversations.channel.line.splitInto` | `{count}` |
| `conversations.channel.line.tooManyMessages` | `{count}` |
| `conversations.channel.line.attachmentLinkHint` | `{count}` `{nonNative}` |
| `conversations.channel.line.windowTitle` | `{ttl}` |
| `conversations.channel.line.takeoverActiveWithRemaining` | `{remaining}` |

---

## 2. zh-Hans：需新增的 78 个 `channels.line.*`

```ts
'channels.line.name': 'LINE',
'channels.line.readOnly': '当前账号只有查看权限，配置为只读',
'channels.line.sections.credentials': '频道凭据',
'channels.line.sections.attachments': '附件直链',
'channels.line.sections.runtime': '运行参数',
'channels.line.fields.webhookEnabled': '启用 Webhook 入站',
'channels.line.fields.channelIdHint': 'LINE Developers 控制台的 Channel ID（显示在客户的加好友链接里）',
'channels.line.fields.accessTokenPlaceholder': '粘贴 Channel access token（保存后不再回显）',
'channels.line.fields.accessTokenHint': '长期令牌，等同凭据：只写入本机密文，接口只返回是否已配置',
'channels.line.fields.secretPlaceholder': '粘贴 Channel secret（用于 Webhook 验签）',
'channels.line.fields.secretHint': 'Channel secret 只用来校验 X-Line-Signature 与签发附件直链，不会回显',
'channels.line.fields.secretStoredEncrypted': '已配置（密文存储），留空表示保持原值',
'channels.line.fields.accessTokenKeyId': '令牌 Key ID',
'channels.line.fields.accessTokenKeyIdHint': '未记录 Key ID；可在 LINE 控制台核对令牌来源',
'channels.line.fields.accessTokenKeyIdValueHint': '令牌被吊销后轮换：先撤销旧 Key，再粘贴新令牌',
'channels.line.fields.attachmentBaseUrlHint': '出站附件对外可访问的公网 HTTPS 根地址（留空 = 取 App:BaseUrl）',
'channels.line.fields.richMenuIdHint': 'Rich Menu ID（在 LINE 控制台发布后粘贴；本系统只展示，不生成）',
'channels.line.fields.markAsReadEnabled': '自动标记已读',
'channels.line.fields.markAsReadEnabledHint': '用载荷里的 markAsReadToken 调 markAsRead，客户侧不再显示未读角标',
'channels.line.fields.loadingIndicatorEnabled': '发送前显示加载中',
'channels.line.fields.loadingIndicatorEnabledHint': '调 startLoadingIndicator，客户看到「加载中…」气泡（仅 reply 场景有效）',
'channels.line.fields.profileEnrichEnabled': '补全客户资料',
'channels.line.fields.profileEnrichEnabledHint': '调 LINE Profile API 取昵称/头像写回客户（额外请求，不占消息配额）',
'channels.line.fields.humanHandoverTitle': '人工接管时静默机器人',
'channels.line.fields.humanHandoverHint': '坐席回复后写接管备注，窗口内机器人不再自动回复；填 0 表示关闭',
'channels.line.fields.textChunkChars': '单条文本上限（字符）',
'channels.line.fields.textChunkCharsHint': 'LINE 单条 text 消息上限 5000 字符，超出会自动分片',
'channels.line.fields.maxMessagesPerRequest': '单次请求最多消息数',
'channels.line.fields.maxMessagesPerRequestHint': '1-5；一次 reply/push 调用最多携带几条消息（含附件消息）',
'channels.line.fields.replyTokenTtlSeconds': 'replyToken 有效秒数',
'channels.line.fields.replyTokenTtlSecondsHint': 'replyToken 一次性且约 25 秒过期；过期后改走 push 并消耗配额',
'channels.line.fields.humanHandoverMuteMinutes': '人工接管静默分钟数',
'channels.line.fields.humanHandoverMuteMinutesHint': '0 = 不静默；坐席回复后这段时间内抑制自动回复',
'channels.line.fields.quotaWarnPercent': '配额告警阈值（%）',
'channels.line.fields.quotaWarnPercentHint': '已消耗 / 月度配额超过该百分比时状态卡显示告警徽标',
'channels.line.fields.attachmentTtlMinutes': '附件直链有效分钟数',
'channels.line.fields.attachmentTtlMinutesHint': '签名直链的 exp 时限（默认 7 天）；过期后需重新发送',
'channels.line.status.channelTitle': 'LINE 频道状态',
'channels.line.status.online': 'API 可达',
'channels.line.status.configured': '已配置凭据',
'channels.line.status.tokenExpiresAt': '令牌到期',
'channels.line.status.tokenNonExpiring': '长期令牌（不过期）',
'channels.line.status.webhookOnLine': 'LINE 侧回调',
'channels.line.status.replyMode': '当前回复方式',
'channels.line.status.replyModeReply': 'reply（免费、25 秒窗口内）',
'channels.line.status.replyModePush': 'push（消耗月度配额）',
'channels.line.status.replyModeUnknown': '未知（尚无出站记录）',
'channels.line.quota.title': '消息配额',
'channels.line.quota.warning': '接近配额上限',
'channels.line.quota.unlimited': '无上限',
'channels.line.quota.unknown': '未知',
'channels.line.quota.monthly': '月度 push 配额',
'channels.line.quota.usedPercent': '已用 {percent}%（阈值 {warn}%）',
'channels.line.quota.replyFreeHint': '只有 push 计费；客户来消息 25 秒内走 reply 不占配额',
'channels.line.push.userId': 'LINE userId（可选）',
'channels.line.push.button': '发送 push 探活',
'channels.line.push.sent': '已发送（{via}）',
'channels.line.push.failed': '发送失败',
'channels.line.push.quotaWarning': '探活会真发一条消息并消耗配额；留空 userId 时取首个活跃 LINE 会话',
'channels.line.webhook.checkButton': '校验回调',
'channels.line.webhook.checkSuccess': 'LINE 已能收到 Webhook',
'channels.line.webhook.checkFailed': '校验未通过：检查回调地址与 Use webhook 开关',
'channels.line.webhook.applyButton': '写入 LINE 端点',
'channels.line.webhook.applySuccess': '已把回调地址写入 LINE 频道',
'channels.line.webhook.applyFailed': '写入失败：确认令牌具备 Channel management 权限',
'channels.line.webhook.copied': '回调地址已复制',
'channels.line.webhook.endpointMismatch': '本系统回调地址与 LINE 登记的端点不一致，请点击「写入 LINE 端点」',
'channels.line.webhook.missingBaseUrl': '缺少 App:BaseUrl，无法推导回调地址；请用环境变量配置生产域名',
'channels.line.webhook.noGetHandshakeHint': 'LINE 不用 GET hub.challenge 握手，只靠上面的「校验回调」验证',
'channels.line.notice.secretNeverEchoed': '接口只返回 hasChannelAccessToken / hasChannelSecret，明文密钥不会回显',
'channels.line.notice.attachmentNoUploadApi': 'LINE 没有聊天媒体上传接口：出站图片/文件必须来自本系统公网 HTTPS 直链（不可重定向、不可需登录），故需配置 AttachmentPublicBaseUrl',
'channels.line.notice.autoReplyFirstOnly': '自动回复只对新会话首次客户消息生效（走 replyToken，不占配额）',
'channels.line.msgType.location': '位置',
'channels.line.msgType.locationDesc': '带经纬度与地址文本',
'channels.line.msgType.sticker': '贴图',
'channels.line.msgType.stickerDesc': '系统贴图，入库为占位文本',
'channels.line.guide.step1': '在 LINE Developers 创建 Provider 与 Messaging API 频道',
'channels.line.guide.docNote': '详细口径与未证实项见 docs/integration/line.md',
```

---

## 3. en：需新增的同样 78 个 `channels.line.*`

```ts
'channels.line.name': 'LINE',
'channels.line.readOnly': 'Your role can view this configuration only',
'channels.line.sections.credentials': 'Channel credentials',
'channels.line.sections.attachments': 'Attachment links',
'channels.line.sections.runtime': 'Runtime parameters',
'channels.line.fields.webhookEnabled': 'Enable webhook ingestion',
'channels.line.fields.channelIdHint': 'Channel ID from the LINE Developers console (also part of the add-friend link)',
'channels.line.fields.accessTokenPlaceholder': 'Paste the Channel access token (never shown again after saving)',
'channels.line.fields.accessTokenHint': 'Long-lived token, treated as a secret: the API only reports whether it is set',
'channels.line.fields.secretPlaceholder': 'Paste the Channel secret (used to verify webhook signatures)',
'channels.line.fields.secretHint': 'The Channel secret only verifies X-Line-Signature and signs attachment links; it is never returned',
'channels.line.fields.secretStoredEncrypted': 'Configured (stored encrypted); leave blank to keep the current value',
'channels.line.fields.accessTokenKeyId': 'Token key ID',
'channels.line.fields.accessTokenKeyIdHint': 'No key ID recorded; check the token source in the LINE console',
'channels.line.fields.accessTokenKeyIdValueHint': 'Rotate after revocation: revoke the old key first, then paste the new token',
'channels.line.fields.attachmentBaseUrlHint': 'Public HTTPS root that serves outbound attachments (blank = App:BaseUrl)',
'channels.line.fields.richMenuIdHint': 'Rich Menu ID published in the LINE console (displayed only, not created here)',
'channels.line.fields.markAsReadEnabled': 'Mark messages as read',
'channels.line.fields.markAsReadEnabledHint': 'Uses the payload markAsReadToken so the customer sees no unread badge',
'channels.line.fields.loadingIndicatorEnabled': 'Show loading indicator',
'channels.line.fields.loadingIndicatorEnabledHint': 'Calls startLoadingIndicator so the customer sees a typing bubble (reply path only)',
'channels.line.fields.profileEnrichEnabled': 'Enrich customer profile',
'channels.line.fields.profileEnrichEnabledHint': 'Calls the LINE Profile API for display name and picture (extra requests, no message quota)',
'channels.line.fields.humanHandoverTitle': 'Mute the bot during human handover',
'channels.line.fields.humanHandoverHint': 'After an agent replies the bot stays silent for this window; 0 disables it',
'channels.line.fields.textChunkChars': 'Text chunk size (chars)',
'channels.line.fields.textChunkCharsHint': 'A LINE text message carries at most 5000 chars; longer replies are split automatically',
'channels.line.fields.maxMessagesPerRequest': 'Max messages per request',
'channels.line.fields.maxMessagesPerRequestHint': '1-5; how many messages one reply/push call may carry (attachments included)',
'channels.line.fields.replyTokenTtlSeconds': 'replyToken lifetime (seconds)',
'channels.line.fields.replyTokenTtlSecondsHint': 'replyToken is one-shot and expires in ~25s; afterwards push is used and consumes quota',
'channels.line.fields.humanHandoverMuteMinutes': 'Handover mute minutes',
'channels.line.fields.humanHandoverMuteMinutesHint': '0 = never mute; suppresses auto-reply for this long after an agent replies',
'channels.line.fields.quotaWarnPercent': 'Quota warning threshold (%)',
'channels.line.fields.quotaWarnPercentHint': 'Shows a warning badge once consumed / monthly quota passes this percentage',
'channels.line.fields.attachmentTtlMinutes': 'Attachment link lifetime (minutes)',
'channels.line.fields.attachmentTtlMinutesHint': 'The exp window of the signed link (default 7 days); resend after it lapses',
'channels.line.status.channelTitle': 'LINE channel status',
'channels.line.status.online': 'API reachable',
'channels.line.status.configured': 'Credentials configured',
'channels.line.status.tokenExpiresAt': 'Token expires',
'channels.line.status.tokenNonExpiring': 'Long-lived token (no expiry)',
'channels.line.status.webhookOnLine': 'Webhook on LINE',
'channels.line.status.replyMode': 'Current reply mode',
'channels.line.status.replyModeReply': 'reply (free, within the 25s window)',
'channels.line.status.replyModePush': 'push (consumes monthly quota)',
'channels.line.status.replyModeUnknown': 'Unknown (no outbound traffic yet)',
'channels.line.quota.title': 'Message quota',
'channels.line.quota.warning': 'Close to the quota limit',
'channels.line.quota.unlimited': 'Unlimited',
'channels.line.quota.unknown': 'Unknown',
'channels.line.quota.monthly': 'Monthly push quota',
'channels.line.quota.usedPercent': 'Used {percent}% (threshold {warn}%)',
'channels.line.quota.replyFreeHint': 'Only push is metered; replies within 25s of an inbound message cost nothing',
'channels.line.push.userId': 'LINE userId (optional)',
'channels.line.push.button': 'Send push probe',
'channels.line.push.sent': 'Sent via {via}',
'channels.line.push.failed': 'Send failed',
'channels.line.push.quotaWarning': 'The probe sends a real message and consumes quota; blank userId falls back to the first active LINE conversation',
'channels.line.webhook.checkButton': 'Verify callback',
'channels.line.webhook.checkSuccess': 'LINE can deliver webhooks',
'channels.line.webhook.checkFailed': 'Verification failed: check the callback URL and the Use webhook switch',
'channels.line.webhook.applyButton': 'Write endpoint to LINE',
'channels.line.webhook.applySuccess': 'Callback URL registered on the LINE channel',
'channels.line.webhook.applyFailed': 'Failed to register: the token needs Channel management permission',
'channels.line.webhook.copied': 'Callback URL copied',
'channels.line.webhook.endpointMismatch': 'This deployment URL differs from the endpoint registered on LINE; press "Write endpoint to LINE"',
'channels.line.webhook.missingBaseUrl': 'App:BaseUrl is missing, so the callback URL cannot be derived; set the public host via environment variable',
'channels.line.webhook.noGetHandshakeHint': 'LINE does not use the GET hub.challenge handshake — verification runs through "Verify callback" above',
'channels.line.notice.secretNeverEchoed': 'The API only returns hasChannelAccessToken / hasChannelSecret; plaintext secrets are never echoed',
'channels.line.notice.attachmentNoUploadApi': 'LINE has no chat media upload API: outbound images and files must come from this system’s public HTTPS direct links (no redirects, no auth), so AttachmentPublicBaseUrl must be set',
'channels.line.notice.autoReplyFirstOnly': 'Auto-reply fires only for the first inbound message of a new conversation (through replyToken, no quota used)',
'channels.line.msgType.location': 'Location',
'channels.line.msgType.locationDesc': 'Coordinates plus the address text',
'channels.line.msgType.sticker': 'Sticker',
'channels.line.msgType.stickerDesc': 'System sticker, stored as placeholder text',
'channels.line.guide.step1': 'Create a provider and a Messaging API channel in the LINE Developers console',
'channels.line.guide.docNote': 'Full details and unverified items: docs/integration/line.md',
```

---

## 4. zh-Hans：需新增的 22 个 `conversations.channel.line.*`

在 `locales/zh-Hans/conversations.ts` 里 `conversations.channel.dingtalk.*` 的同级位置追加：

```ts
'conversations.channel.line.title': 'LINE 回复（reply 优先，过期自动转 push）',
'conversations.channel.line.windowOk': '回复窗口内',
'conversations.channel.line.windowExpiring': '窗口即将过期',
'conversations.channel.line.windowExpired': '窗口已过期',
'conversations.channel.line.windowUnknown': '无回复窗口',
'conversations.channel.line.windowTitle': '客户最后一条消息在 {ttl} 秒内可直接回复：走 replyToken 免费且必达；过期后改用 push，会消耗月度配额',
'conversations.channel.line.takeoverActive': '人工接管中：机器人不会自动回复',
'conversations.channel.line.takeoverActiveWithRemaining': '人工接管中（剩余 {remaining}）：机器人不会自动回复',
'conversations.channel.line.takeoverStart': '开始接管',
'conversations.channel.line.takeoverRelease': '结束接管',
'conversations.channel.line.takeoverFailed': '接管状态更新失败',
'conversations.channel.line.unreachableHint': '该客户未添加机器人为好友（或已屏蔽），消息无法送达',
'conversations.channel.line.fileTooLarge': '附件「{name}」超过 LINE 直链上限（{limit}）',
'conversations.channel.line.uploadTitle': 'LINE 附件会转成本系统公网 HTTPS 签名直链发送（需配置 AttachmentPublicBaseUrl）',
'conversations.channel.line.uploadAria': '上传 LINE 附件',
'conversations.channel.line.splitInto': '将分 {count} 条发送',
'conversations.channel.line.placeholder': '输入要发送给该 LINE 客户的回复…',
'conversations.channel.line.textareaAria': 'LINE 回复内容',
'conversations.channel.line.attachmentLinkHint': '{count} 个附件走签名直链（{nonNative} 个非原生图片格式会按文件发送）',
'conversations.channel.line.tooManyMessages': '单次最多 5 条消息：当前预计 {count} 条，请减少内容或附件',
'conversations.channel.line.footerHint': '5000 字符/条 · 图片直链 · 过期后转 push 耗配额',
'conversations.channel.line.sendAria': '发送 LINE 回复',
```

## 5. en：同样 22 个 `conversations.channel.line.*`

```ts
'conversations.channel.line.title': 'LINE reply (reply first, push after the window lapses)',
'conversations.channel.line.windowOk': 'Inside reply window',
'conversations.channel.line.windowExpiring': 'Window about to expire',
'conversations.channel.line.windowExpired': 'Window expired',
'conversations.channel.line.windowUnknown': 'No reply window',
'conversations.channel.line.windowTitle': 'Replies within {ttl}s of the last customer message use the free one-shot replyToken; afterwards push is used and consumes monthly quota',
'conversations.channel.line.takeoverActive': 'Human handover active: the bot will not auto-reply',
'conversations.channel.line.takeoverActiveWithRemaining': 'Human handover active ({remaining} left): the bot will not auto-reply',
'conversations.channel.line.takeoverStart': 'Start handover',
'conversations.channel.line.takeoverRelease': 'End handover',
'conversations.channel.line.takeoverFailed': 'Failed to update the handover state',
'conversations.channel.line.unreachableHint': 'This customer has not added the bot as a friend (or blocked it), so messages cannot be delivered',
'conversations.channel.line.fileTooLarge': 'Attachment “{name}” exceeds the LINE direct-link limit ({limit})',
'conversations.channel.line.uploadTitle': 'LINE attachments are sent as signed public HTTPS links from this system (AttachmentPublicBaseUrl must be set)',
'conversations.channel.line.uploadAria': 'Upload LINE attachment',
'conversations.channel.line.splitInto': 'Will be sent as {count} messages',
'conversations.channel.line.placeholder': 'Type the reply to this LINE customer…',
'conversations.channel.line.textareaAria': 'LINE reply content',
'conversations.channel.line.attachmentLinkHint': '{count} attachment(s) sent as signed links ({nonNative} are not natively displayed image formats and go as files)',
'conversations.channel.line.tooManyMessages': 'At most 5 messages per call: this would be {count}, shorten the text or drop attachments',
'conversations.channel.line.footerHint': '5000 chars per message · image direct links · push after expiry consumes quota',
'conversations.channel.line.sendAria': 'Send LINE reply',
```

---

## 6. 顺带缺口（本 agent 未引用，但渠道页族共同需要）

`locales/{en,zh-Hans}/format.ts` 的 `format.sourceType.*` 只到 dingtalk，缺
`messenger / telegram / line / imessage` 四键；而 `utils/format.ts:357-360,424-427`、
`ThreadList.tsx:789`、`Messenger.tsx:87`（以及新增的 `Line.tsx`）已引用 ⇒ `tsc` 的 `TranslationKey`
会报错，运行期标签显示原始 key。建议与上面几批一并补。

另：`channels.common.*` 里 Telegram/Messenger 页也在等的
`loading / save / mailbox / mailboxAuto / readOnly / toggleVisibility / configuredKeepBlank / refreshStatus / notDetected`
仍缺（与 LINE 无关，仅登记备注）。
