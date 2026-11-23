# iMessage / Apple Messages 接入要点与路线判定

> **状态（2026-11-23 复核，v2.3.0 工作树）**：本渠道已实现并交付——`SourceType.IMessage = 14`、模块名 `imessage`、前端页面 `IMessage.tsx`、默认走 BlueBubbles 桥接（另有 AMB MSP / Apple Direct 两种接入方式）；固定回调 `/api/imessage/bluebubbles/webhook`，默认值见 `docs/development.md` §60，用户步骤见用户手册 11.13。本文保留为路线判定与平台事实的取证记录。

> 取证来源：Apple 官方 register.apple.com 文档（可达，逐字引用）、BlueBubbles 官方文档/发布记录、第三方集成商与 GitHub 议题。Apple 文档版本：**MSP REST API v4.3.0（2026-11-11）**、**AMB 文档 v3.1（2026-07-21）**。

## 0. 结论：三条路线，本仓库默认走 B
| 路线 | 现状 | 可用者 | 判定 |
|---|---|---|---|
| A1 直连 Apple `mspgw.push.apple.com` | 技术上活着（v4.3.0，2026-11-11 更新） | **仅 Apple 核准的 MSP** | ❌ 开源项目拿不到资格；Apple FAQ 原文：“Can I build my own bot instead of going through an MSP? **No.**” |
| A2 经核准 MSP 接入 AMB | 支持 | 已注册并选定 MSP 的企业 | ✅ 唯一官方许可路径，但**没有统一 API**（每家 MSP 一套接口） |
| B 自建 macOS 桥（BlueBubbles / imsg） | 活跃维护（BlueBubbles v1.9.10 2026-10-26，v1.9.12 2026-11-01） | 任何有一台常开 Mac 的人 | ✅ 自托管者唯一能端到端跑通的路线；**非官方、有 ToS 风险** |

⇒ 落码方式：`IMessageProvider` 一个抽象，两套传输 —— **`bridge`（默认，BlueBubbles REST）** 与 **`amb_msp` / `amb_apple_direct`（配置存在即启用，文档明确写清需要 Apple 资格）**。Apple-direct 客户端置于实验性开关后。

## 1. 名称与常见错误说法（必须纠正）
- 现名 **“Apple Messages for Business”（AMB）**；“Business Chat” 是旧名。面向客户的文案 Apple 要求写 **“Apple Messages”**；“iMessage” 只用于桥接模式。
- 鉴权是 **HS256，不是 ES256**：JWT 头 `{"alg":"HS256","typ":"JWT"}`，claims **只有** `iss`/`aud`（= MSP ID 或 business/CSP ID）与 `iat`；**无 kid、无 teamId、无 exp**；`iat` 超过 1 小时 Apple 直接拒 ⇒ 至少每小时重签。**不存在 Apple 下发的 EC 密钥对。**
- **`https://api.apple-chat.com` 不存在**；真实基址 `https://mspgw.push.apple.com`，路径 `/v1/message`、`/v1/preUpload`、`/v1/preDownload`。
- **没有 scope 概念**（`message:send` 等是臆造），也没有 “Service Token/订阅 id”。
- **没有 48 小时/7 天窗口**（那是 Meta 的模型）。AMB 是**会话制**：客户发起且会话未关闭期间可回，**永不允许冷启动私聊**；空闲会话自动结案（供应商常设 ~24h，是排队规则不是 Apple 时钟）。
- 门户是 **Apple Business Register（register.apple.com）**，不是 business.apple.com。
- 该渠道在**加码而非退场**：新增 Business Invitations（主动消息）、2026-11 刷新 API；iOS 26/macOS 26 取消了 wide logo 素材要求。Dynamics 365「AMB 渠道」于 2026-07-17 弃用是**微软退出 MSP 身份**，不是 Apple 停服。

## 2. 官方准入（绕不过的门槛）
企业侧：企业/Managed Apple Account + 接受 ToS + 建 Internal test account + 从 “Apple Authorized Commercial Messaging Platform” 下拉里选 MSP + 加测试账号验证链路；**只有品牌所有人**可申请；品牌名不得含门店与法律后缀；1024×1024 方图 ≤2MB sRGB/P3；通过 **Experience Review**（通常 1–3 个工作日）转商用账号。Apple **不收消息费**，由 MSP 计费。
排除品类：烟草/电子烟/武器/赌博/政治组织/色情，以及**当前以医疗/健康为主营的企业**。受制裁地区排除。
产品硬规则（对客服系统友好）：*“The business must provide access to a live agent during its regular business hours. A business must not provide a limited or bot-only solution.”* ⇒ 必须有真人坐席；机器人须自我披露并在 ~5 秒内欢迎；触发词标准：`agent/support/help`、`menu/list/?`、`unsubscribe/spam/stop/end`。
MSP 侧准入：需已有带意图路由、真人坐席台、bot flows、客户成功团队、对话设计师的消息平台，并经 Integration→Business Development（3 个真实品牌试点）→Launch 的现场评审 ⇒ 开源项目实际不可达；且获批前**不得公开宣传**与 Apple 的关系（影响 README 措辞）。
地区：v3.1 未公布国家清单。证据显示 2018 年由美国扩展至 AU/CA/JP/SG/UK/HK；**无中国大陆支持的证据**。上线前需在 register.apple.com/messages 核对。

## 3. 路线 A（AMB / Apple 直连）规格要点
- 出站：`POST https://mspgw.push.apple.com/v1/message`，成功 **200 且空 body**（除非交互 JSON >10KB）。必需头：`Authorization`、`destination-id`、`source-id`、`id`(uuid)、**`capability-list`**（客户设备能力：`QUICK,LIST,TIME,AUTH,AUTH2,FORM`；Mac 可能缺 `FORM`）；可选 `auto-reply: true`、`msp-agent`（Apple 只记前 64 字符 ⇒ 填本系统名）。
- body：`{"type":"text|interactive|typing_start|typing_end|close","v":1,"id":"<uuid>","sourceId":"<不透明客户 id>","destinationId":"urn:mbid:…","locale":"en_US","body":"…","group":…,"intent":…,"interactiveData":{…},"interactiveDataRef":"…(>10KB 时用)"}`；**只有用户可关闭会话，自己绝不能发 `close`**。
- 乱序风险：Apple 可能乱序投递 ⇒ 要顺序（先 typing 再正文）就**等 N 的 200 再发 N+1**。
- 入站：本系统须公网 `POST /message`，body 是 **gzip**（`Content-Encoding: gzip` 要解压），头含 `Source-Id: urn:mbid:…`、`Destination-Id`、`Id`、`Capability-List`、`Authorization`。校验 destination-id 属于本租户、验 JWT、要求头齐、解压再解析；先回状态码再下载附件。
- 状态码语义（写进重试策略）：400 头缺失/JSON 非法/`destination-id` 与 body 不一致；401 入站缺 `Authorization`；403 JWT/MSP ID 无效；**404 未知 destination-id 或用户暂时不可达（换机/SIM 周期常见），不会稍后重投 ⇒ 要标记未送达或重发**；**410 Gone 用户已关闭会话（绝不重试）**；5xx —— **Apple 只对 5xx 重试，4 次，退避从 100ms 起**。
- 富组件：`interactiveData` 支持 Quick Reply / List Picker / Time Picker / Form / Auth(OAuth2) / iMessage app / **rich link（发出去的每个 URL 都必须是 rich link，不能裸 URL）**。**Tapback 以纯文本 `type:"text"` 送达**，body 是本地化句子（如 `Liked "Thank you."`）⇒ 原样渲染，别试图合成对象。
- 附件 ≤100 MB：Apple 要求 **AES/CTR/NoPadding + 全零 16 字节 IV**，单次 256-bit key 由 CSPRNG 生成、hex 前缀 `00` 存储。** .NET 的 `Aes` 没有 CTR 模式**（只有 CBC/ECB/CTS）⇒ 要么用 AES-ECB 对递增计数器块 XOR 明文实现 CTR，要么引入加密库（与「不加包」约定冲突，需单独决策）。流程：`GET /v1/preUpload`(头 `size`) → `{upload-url, mmcs-url, mmcs-owner}` → `POST [upload-url]` 上传密文 → `{singleFile:{fileChecksum}}` → `signature-base64`；正文中每个附件必须含 **一个 U+FFFC** 且位置即渲染位置。入站：`GET /v1/preDownload`（`authorization/source-id/url/signature/owner`）→ `download-url` → GET 后解密；TLS SNI 取返回 URL 的 host。附件保留 30 天（重发会续期）。
- 同意/关闭：用户侧滑删除并确认“停止接收” ⇒ 收到 `type:"close"`，此后必须屏蔽所有坐席发送；只删除未选停止则**不会**发 close。已关闭记录要入库，后续发送会 **410**。close 的 `sourceId` **有两种形态**：有过活跃会话是不透明 id，只收到过邀请未接受则是原始号码 `tel:+1…` ⇒ 两种都要处理。
- **Business Invitations 是唯一主动消息**：需企业申请 + Apple 批准、**只有 Apple 编写的模板**（已见 `binaryChoice.engage.withImage`）、客户已 opt-in 且给过号码、禁止营销活动、该 Apple Account 所有设备需 **iOS 18.0 / macOS 15.0+**。发送仍走 `/v1/message` 但必需头 `message-type: notification`，`destinationId` 为 `tel:+…`。**Apple 明确要求实现 404 时回落到品牌下一渠道（短信）** ⇒ 设计里要有 `fallbackChannel`。
- 入口：站内/应用 “Message Us” 按钮、邮件、QR/NFC、Apple Maps 列表、Message Suggest、Wallet 卡券；裸链 `https://bcrw.apple.com/urn:biz:<business-id>`（可带 `biz-intent-id`、`groupID`、`body`）。裸链也可能带**数字 `intent`** ⇒ 不能假设 intent 来自自家按钮。

## 4. 路线 B（BlueBubbles 桥）规格要点
- 鉴权：文档口径是 **query 参数 `guid`（别名 `password`/`token`）**：`/api/v1/ping?guid=<password>`；部署实例也接受 `Authorization: Bearer <password>`。⇒ 两种都支持，**优先头**，且**绝不把密钥写进会被日志记录的 URL**。响应封装 `{"status":int,"message":String,"data":any}`，错误另带 `error:{type,error}`。
- 发文本：`POST /api/v1/message/text` body `{"chatGuid":"iMessage;-;+15551234567","message":"…","tempGuid":"oc-<uuid>","method":"apple-script"}`；`method` ∈ `apple-script`（普通权限可用，**默认**）| `private-api`（功能全，**需关闭 SIP**）| `any`。`tempGuid` 是自生成 id，用于对账自己的出站回声、避免重复渲染。字段名以 `message` 为主（部分资料写 `text` ⇒ 按目标服务器版本核对）。
- 发附件：`POST /api/v1/message/attachment`（multipart：`chatGuid`、`attachment=@file`）。
- 其它常用端点：`POST /api/v1/chat/new`、`POST /api/v1/chat/{guid}/typing`（private API ⇒ 需关 SIP）、`POST /api/v1/message/react`（`chatGuid`、`selectedMessageGuid` 如 `p:0/<guid>`、`reaction:"love"`，private API）、`GET /api/v1/chat/query`、`GET /api/v1/chat/{guid}/message`（`after/before/limit`）、`GET /api/v1/contact`、`GET /api/v1/server/info`、`GET /api/v1/ping`。
- Webhook：`POST /api/v1/server/webhook`（等价 `POST /api/v1/push`，`GET /api/v1/push` 列表）body `{"url":"https://<本系统>/imessage/bluebubbles/webhook","password":"<pw>"}`。事件：`new-message`、`updated-message`（送达/已读）、`message-error`/`message-send-error`、`typing-indicator`、`read-receipt`、`chat-read-status`、`group-name-change`、`participant-added|removed|left`、`server-update`、`url-change`，另有 `Hello World` 测试与全事件订阅。REST API 需服务器 ≥0.2.0，**webhook 需 ≥1.0.0**。权威端点清单是部署服务器自身的 OpenAPI/Postman（路径随版本漂移 ⇒ 别写死）。
- 入站 `new-message` 关键字段：`data.guid`（**幂等键**）、`data.text`、`isFromMe`（true 一律忽略）、`data.chatGuid`、`data.chats[].{guid,chatIdentifier}`、`data.handle.address`、`dateCreated`、`attachments[]`。`handle.address` 视为不可信展示数据；群聊没有单一 handle。
- 主机前提（B 的真正成本）：一台**常开 macOS**（Mac mini / macOS VM / 云 Mac），Messages.app 登录**专用 Apple Account**（桥接即以该账号收发消息）；TCC 授权 **Full Disk Access**（读 `~/Library/Messages/chat.db`）与 **Accessibility/Automation**（AppleScript 发送被拒后要在系统设置→隐私与安全性→自动化重新授权）；禁止睡眠（`caffeinate -d` / `pmata -a sleep 0`）；服务绑 `127.0.0.1` 通过隧道暴露，**不要直接开放 1234 端口**；Private API 需**关闭 SIP**（表情、输入指示、编辑/撤回、已读回执），macOS 升级会破坏（Tahoe 需要修复，edit 目前在 Tahoe 不可用）。
- 2026 年的替代桥：`imsg`（`brew install steipete/tap/imsg`）—— OpenClaw 于 2026-09 弃用 BlueBubbles 通道，改为 spawn `imsg` 走 stdio JSON-RPC（无服务器/无 webhook/无端口）；基础收发/历史/媒体用普通权限，高级操作 `imsg launch` 需关 SIP。Linux/Windows 网关可用 **SSH 包装**指向已登录的 Mac。⇒ 本渠道把 BlueBubbles HTTP 作为主契约，可再加 `imsg-over-SSH` 传输实现同一接口。只读/简单桥另有 `greghughespdx/imessage-bridge`、`xbenng/imessage-api`、`imessage-chatdb`（都读 chat.db、都要 FDA）。

## 5. 身份映射与去重
| | 路线 A（AMB） | 路线 B（桥） |
|---|---|---|
| 企业身份 | Business ID `urn:biz:<uuid>`（Apple 注册时下发） | 登录 Messages.app 的 Apple Account（号码/邮箱 handle） |
| 客户身份 | **不透明 ID**（客户 Apple Account ↔ 本企业的配对唯一；跨会话稳定、不同品牌不同） | **原始 handle**：`+1555…` 或 `user@example.com` |
| 能否拿到号码 | **不能**（除非客户在会话里自己写） | 天然可得 |

⇒ 外部 id 必须**按模式作用域**：唯一索引 `(tenant, channel_mode, external_id)`，不能只用裸 id 列（AMB 不透明 id 与 BlueBubbles chatGuid 会撞）。
去重：入站按消息 `guid`（B）/Apple `Id` 头（A）幂等 upsert；出站按自生成 `id`(uuid) / `tempGuid` 匹配自己的回声（**不要按文本匹配**）。
**chat 身份归一化（真实事故点）**：同一 1:1 会话可能以 `iMessage;-;+1…`、`SMS;-;+1…`、`any;-;+1…` 或裸 handle 送达 ⇒ 把私聊各变体收敛到**裸 handle**可保持一个会话不裂开（上游同类修复见 hermes-agent #67105/#82548），但**群聊保留 GUID**（MMS 群与 iMessage 群确实是不同会话）。不要用 `GET /api/v1/chat/query` 分页去把 handle 解析成 GUID（单页有上限，漏了就**静默另起新会话**）⇒ 从每次入站预热 `chatIdentifier→chatGuid` 缓存，并持久化回复时用的 GUID；**保留服务前缀**（剥掉曾导致主动回复降级成 SMS，openclaw #35624）。

## 6. 配置键
共享：`Provider=imessage`、`Mode = bridge | amb_msp | amb_apple_direct`。
- **bridge**：`BaseUrl` · `AuthStyle(query|bearer，默认 bearer)` · `Password`(加密、只写不回显) · `BridgeWebhookSecret`(入站共享密钥，落库键 `imessage.bridge_webhook_secret`，密文 `ENC:` 存储；桥接调用时须带 `X-Webhook-Secret` 头或 `?secret=`，常量时间比较；留空则仅记一次告警、不校验) · `WebhookPath`(**固定** `/api/imessage/bluebubbles/webhook`：控制器路由是编译期常量，提交别的值会被归一化回该常量并告警) + `WebhookBaseUrl` · `SendMethod(apple-script 默认/private-api/any)` · `AllowSmsFallback`(默认 **false**：客服场景把蓝泡泡静默降级成 SMS 是质量事故) · `AllowList`(入站 handle/邮箱/群) · `RateLimitPerMinute` · `VerifyTls` · `HealthEndpoint`(`/api/v1/ping`) · `MinServerVersion`("1.0.0") · `ReadHistoryOnStart`。
- **amb_apple_direct**：`MspId`、`MspSharedSecret`(HS256)、`TokenTtlSeconds`(默认 3600，硬上限 3600)、`GatewayBaseUrl`(`https://mspgw.push.apple.com`)、`BusinessId`(`urn:biz:…`)、`InboundPath`(`/message`，**必须接受 gzip**)、`AttachmentKeySize`(256)、`MaxBodyBytes`。
- **amb_msp**：`MspConnector`、`ApiBaseUrl`、`ApiKey/Bearer`、`WebhookSecret`(校验 MSP 的 HMAC)、`BusinessIdentifier`、`FallbackChannel(sms|rcs|none)`（Apple 对邀请强制要求）。
（AMB 没有 Team ID / Key ID / 私钥文件 / Service Token —— UI 里出现这些就是给不存在的 API 建模，删掉。）

## 7. README 必须携带的告警
1. **没有 Apple 核准就用不了 AMB**：企业须在 Apple Business Register 注册、须是品牌所有人、须选核准 MSP、须过 Experience Review；一个开源连接器**不授予**访问资格；获批前还不得公开宣传与 Apple 的关系。
2. **桥接是非官方的、Apple 不支持**：macOS 升级会破坏（Tahoe 需修复；edit 当前不可用），Private API 要关 SIP，chat.db 要 Full Disk Access，自动化或批量发送会触发 **Apple ID 锁定**；有厂商称营销式发送仅容忍每天数百条 ⇒ 持续大出站量按锁定风险对待。
3. **端到端加密止于你那台 Mac**：设备间 E2E（iOS 17.4+ 为 PQ3），但桥接读的是 chat.db 里**已解密**内容；Mac 失守＝历史全泄，桥接口令失守＝可冒充品牌。上线要求：专用 Apple ID、FileVault、只绑 localhost + 隧道、轮换口令、allowlist + 限流。
4. **合规不对称**：AMB 给的是假名化客户（Opaque ID）且有 ISO/IEC 27001+27018 覆盖；桥接给的是真实号码与邮箱 ⇒ GDPR/PIPL 个人数据且无 Apple 侧治理，必须提供数据留存开关。
5. 文案：面向用户写 “Apple Messages / Apple Messages for Business”，只有桥接模式写 “iMessage”；不要叫 Business Chat。
6. AMB 内部没有短信/MMS 回落；桥接的 SMS 回落是**另一条法律渠道**，转录里要标注。
7. 不要提交凭据；BlueBubbles 的 query 参数鉴权会把密钥泄进访问日志 ⇒ 日志要清洗。

## 8. 架构建议（一句话/层）
`IMessageProvider` 抽象 + 两套传输：`BridgeTransport`（默认，`HttpClient` 打 BlueBubbles `/api/v1`，并在 `webhookPath` 暴露接收端点：`new-message/updated-message/message-error/typing-indicator`，按消息 `guid` 去重、按 §5 归一化 chat 身份）与 `AmbTransport`（出站 `POST /v1/message` + 每小时重签的 HS256 JWT 处理器，绝不跨小时缓存；入站 `POST /message` 带接受 gzip 的中间件、头/体一致性校验、只重试 5xx 的策略）。两者经 `(tenant, mode, externalId)` 身份表映射到会话模型；Apple 要求处强制 `fallbackChannel`；Apple 直连客户端置于 `experimental` 开关后（需 MSP 资格）。
