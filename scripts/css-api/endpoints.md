# NoneadCSS API 端点索引（自动生成）

- 服务: http://css.example.com:5173
- OpenAPI: /openapi/v1.json （title=NoneadCSS API - 拓德客户服务与支持系统，version=1.0.0）
- 文档 UI: /docs/ (Scalar)
- 操作总数: 594，分组数: 50
- 认证: POST /api/Auth/login {email,password,captchaId,captchaCode} → JWT，后续 Authorization: Bearer <token>
- 生成时点: 2026-10-23（v2.3.0 工作树直连生产环境导出）；OpenAPI `version=1.0.0` 是文档字段，与程序集 `Directory.Build.props` 的 2.3.0 解耦。端点随代码演进，重新导出请用 `scripts/css-api/` 下工具。

## ApiKeys (14)
- `GET /api/ApiKeys` — 获取 API 密钥列表（分页）
- `POST /api/ApiKeys` — 创建新的 API 密钥
- `DELETE /api/ApiKeys/{id}` — 删除 API 密钥
- `GET /api/ApiKeys/{id}` — 获取 API 密钥详情
- `PUT /api/ApiKeys/{id}` — 更新 API 密钥
- `DELETE /api/ApiKeys/{id}/hard` — 彻底删除 API 密钥（管理员）
- `GET /api/ApiKeys/{id}/logs` — 获取 API 密钥的调用日志
- `POST /api/ApiKeys/{id}/regenerate` — 重新生成 API 密钥
- `POST /api/ApiKeys/{id}/restore` — 恢复已删除的 API 密钥
- `POST /api/ApiKeys/{id}/toggle` — 切换 API 密钥启用状态
- `GET /api/ApiKeys/logs` — 获取所有 API 密钥调用日志（分页）
- `GET /api/ApiKeys/overview` — 获取 API 密钥概览统计
- `DELETE /api/ApiKeys/trash` — 清空垃圾箱（管理员）
- `GET /api/ApiKeys/trash` — 获取垃圾箱中的 API 密钥列表（分页）

## Attachments (15)
- `DELETE /api/Attachments/{id}` — 删除附件
- `GET /api/Attachments/{id}` — 获取附件详情
- `GET /api/Attachments/{id}/download` — 下载附件
- `GET /api/Attachments/{id}/preview` — 获取图片预览（缩略图）
- `GET /api/Attachments/{id}/public-preview` — 公开预览图片（用于头像等公开显示场景）
- `GET /api/Attachments/config` — 获取附件配置信息（允许的文件类型、大小限制等）
- `DELETE /api/Attachments/conversation/{conversationId}` — 删除会话的所有附件
- `GET /api/Attachments/conversation/{conversationId}` — 获取会话的所有附件
- `DELETE /api/Attachments/thread/{threadId}` — 删除消息线程的所有附件
- `GET /api/Attachments/thread/{threadId}` — 获取消息线程的所有附件
- `POST /api/Attachments/upload` — 上传单个附件
- `POST /api/Attachments/upload/base64` — 从 Base64 上传附件
- `POST /api/Attachments/upload/batch` — 批量上传附件
- `GET /api/Attachments/user/{userId}` — 获取用户上传的所有附件
- `POST /api/Attachments/validate` — 检查文件是否允许上传

## AuditLogs (9)
- `GET /api/AuditLogs` — 获取审计日志列表（支持多维过滤）
- `GET /api/AuditLogs/{entityType}/{entityId}` — 获取指定实体的操作日志
- `GET /api/AuditLogs/{id}` — 获取单条审计日志详情
- `GET /api/AuditLogs/{id}/related` — 获取关联日志（同实体的其他操作记录，排除自身）。
用于详情页快速查看该实体的完整操作链。
- `GET /api/AuditLogs/{id}/verify` — 校验单条审计日志的 HMAC 签名完整性。
- `GET /api/AuditLogs/export` — 导出符合条件的审计日志为 CSV（带 BOM，Excel 友好）
- `GET /api/AuditLogs/stats` — 获取审计日志全局统计（基于全量数据，受同一组过滤条件约束）
- `GET /api/AuditLogs/user/{userId}` — 获取指定用户的操作日志
- `GET /api/AuditLogs/verify-integrity` — 校验审计日志完整性（HMAC 签名验证），检测被篡改的记录。

## Auth (9)
- `GET /api/Auth/captcha` — 获取图形验证码
- `POST /api/Auth/forgot-password` — 忘记密码
- `POST /api/Auth/login` — 用户登录
- `POST /api/Auth/logout` — 用户登出
- `GET /api/Auth/password-policy` — 获取当前密码策略
- `GET /api/Auth/public-config` — 获取公开注册配置
- `POST /api/Auth/refresh-token` — 刷新 Access Token
- `POST /api/Auth/register` — 用户注册
- `POST /api/Auth/reset-password` — 重置密码

## Calendar (29)
- `GET /api/Calendar` — 获取日历事件列表（分页+筛选）
- `POST /api/Calendar` — 创建日历事件
- `DELETE /api/Calendar/{id}` — 删除日历事件（软删除）
- `GET /api/Calendar/{id}` — 获取事件详情
- `PUT /api/Calendar/{id}` — 更新日历事件
- `POST /api/Calendar/{id}/duplicate` — 复制日历事件（创建新事件，可选调整时间/标题）
- `GET /api/Calendar/{id}/export` — 导出单个事件为 .ics 文件
- `DELETE /api/Calendar/{id}/hard` — 彻底删除事件（不可恢复）
- `PATCH /api/Calendar/{id}/quick-update` — 快速更新事件（仅更新指定字段，其余保持不变）
- `POST /api/Calendar/{id}/reminder-confirm` — 用户在 SignalR 弹出卡片上点击"确认已收到"或自动关闭时调用本接口。
原子地把事件的 ReminderConfirmation 从 pending/fallback_in_progress/fallback_sent
更新为 confirmed，同时写入 ReminderConfirmedAt = now，避免 5 分钟 FallbackJob
再次走 EMAIL+IM 兜底通道。
- `POST /api/Calendar/{id}/restore` — 恢复已删除的事件
- `POST /api/Calendar/{id}/status` — 更新事件状态
- `POST /api/Calendar/batch` — 日历批量操作
- `GET /api/Calendar/export` — 按日期范围导出日历为 .ics 文件
- `POST /api/Calendar/import` — 从 .ics 文件导入事件
- `GET /api/Calendar/outlook/health` — P3-D: 获取 Outlook 同步健康仪表盘数据
路由：GET /api/calendar/outlook/health
若指定 mailboxId 则返回单个邮箱健康数据，否则返回当前用户可访问的所有邮箱列表
- `POST /api/Calendar/outlook/pull-delta` — P3-D: 手动触发指定邮箱的 Outlook Delta 增量同步
路由：POST /api/calendar/outlook/pull-delta?mailboxId=X
要求：Authorize + 用户有该邮箱管理权
- `GET /api/Calendar/overview` — 获取日历概览统计
- `GET /api/Calendar/subscription` — 获取当前用户的日历订阅 URL（用于 Outlook/Google Calendar 订阅）
- `POST /api/Calendar/subscription/regenerate` — 重新生成当前用户的日历订阅令牌（使旧链接立即失效）
- `GET /api/Calendar/templates` — 获取日历事件模板列表
- `POST /api/Calendar/templates` — 创建事件模板
- `DELETE /api/Calendar/templates/{id}` — 删除事件模板（软删除）
- `GET /api/Calendar/templates/{id}` — 获取单个事件模板
- `PUT /api/Calendar/templates/{id}` — 更新事件模板
- `GET /api/Calendar/trash` — 获取垃圾箱列表
- `POST /api/Calendar/trash/clear` — 清空垃圾箱（彻底删除所有软删除的日历事件，SuperAdmin 专用）。
- `GET /api/Calendar/trash/count` — 获取垃圾箱数量
- `POST /api/Calendar/trash/purge` — 按保留期清空过期回收站（SuperAdmin 专用）。

## CalendarFeed (1)
- `GET /api/calendar/feed` — ICS 订阅源 — Outlook/Google Calendar 可直接订阅此 URL

## CannedResponses (12)
- `GET /api/CannedResponses` — 获取模板列表
- `POST /api/CannedResponses` — 创建模板
- `DELETE /api/CannedResponses/{id}` — 删除模板（移至垃圾箱）
- `GET /api/CannedResponses/{id}` — 获取模板详情
- `PUT /api/CannedResponses/{id}` — 更新模板
- `DELETE /api/CannedResponses/{id}/hard` — 彻底删除模板（从垃圾箱永久删除）
- `POST /api/CannedResponses/{id}/restore` — 恢复垃圾箱中的模板
- `GET /api/CannedResponses/categories` — 获取所有分类
- `GET /api/CannedResponses/search` — 搜索模板
- `DELETE /api/CannedResponses/trash` — 清空垃圾箱（仅管理员可操作）
- `GET /api/CannedResponses/trash` — 获取垃圾箱列表（分页）
- `GET /api/CannedResponses/trash/count` — 获取垃圾箱中的模板数量

## Chat (30)
- `GET /api/Chat/active-sessions` — 
- `POST /api/Chat/initiate` — 
- `GET /api/Chat/overview` — 
- `GET /api/Chat/sessions` — 
- `DELETE /api/Chat/sessions/{id}` — 软删除单个会话（移至垃圾箱）
- `GET /api/Chat/sessions/{id}` — 
- `POST /api/Chat/sessions/{id}/assign` — 
- `POST /api/Chat/sessions/{id}/close` — 
- `DELETE /api/Chat/sessions/{id}/hard` — 彻底删除单个会话（不可恢复）
- `POST /api/Chat/sessions/{id}/mark-as-read` — 
- `GET /api/Chat/sessions/{id}/messages` — 
- `POST /api/Chat/sessions/{id}/messages` — 
- `PUT /api/Chat/sessions/{id}/messages/{messageId}` — 
- `POST /api/Chat/sessions/{id}/messages/{messageId}/recall` — 
- `POST /api/Chat/sessions/{id}/messages/{messageId}/recall-v2` — 
- `POST /api/Chat/sessions/{id}/restore` — 恢复垃圾箱中的会话
- `POST /api/Chat/sessions/{id}/transfer` — 
- `POST /api/Chat/sessions/{id}/typing` — 
- `GET /api/Chat/sessions/{id}/unread-count` — 
- `POST /api/Chat/sessions/bulk-delete` — 批量软删除会话
- `GET /api/Chat/sessions/by-conversation/{conversationId}` — 按工单 ConversationId 反查在线聊天会话（若该工单由聊天转工单生成，在工单详情页合并展示在线聊天评分）
- `DELETE /api/Chat/sessions/trash` — 清空垃圾箱
- `GET /api/Chat/sessions/trash` — 获取垃圾箱会话列表
- `POST /api/Chat/token/close` — 
- `POST /api/Chat/token/mark-as-read` — 
- `GET /api/Chat/token/messages` — 
- `POST /api/Chat/token/messages` — 
- `POST /api/Chat/token/rate` — 
- `GET /api/Chat/token/session` — 客户侧免登录获取当前会话的完整 DTO（用于轮询状态变更，例如
客服关闭会话后，状态从 active → awaiting_rating，客户 Widget 需据此
自动切换到评分 UI，并显示评分超时倒计时）。
- `POST /api/Chat/token/typing` — 

## Conversations (32)
- `GET /api/Conversations` — 获取会话列表
- `POST /api/Conversations` — 创建新会话
- `DELETE /api/Conversations/{id}` — 删除会话
- `GET /api/Conversations/{id}` — 获取会话详情
- `PUT /api/Conversations/{id}/assign` — 分配会话给指定用户
- `GET /api/Conversations/{id}/chat-mode` — 获取会话的聊天模式消息线程
- `POST /api/Conversations/{id}/clone` — 克隆会话
- `DELETE /api/Conversations/{id}/draft` — 删除草稿
- `GET /api/Conversations/{id}/draft` — 获取会话草稿
- `POST /api/Conversations/{id}/draft` — 保存草稿
- `POST /api/Conversations/{id}/follow` — 切换关注状态（关注/取消关注会话）
- `GET /api/Conversations/{id}/followers` — 获取会话关注者列表
- `DELETE /api/Conversations/{id}/hard` — 彻底删除会话（从数据库永久删除）
- `POST /api/Conversations/{id}/mark-read` — 标记会话为已读（清除列表页未读徽章）
- `POST /api/Conversations/{id}/notes` — 添加内部备注
- `PUT /api/Conversations/{id}/priority` — 更新会话优先级
- `POST /api/Conversations/{id}/reply` — 回复会话（添加消息线程）
- `POST /api/Conversations/{id}/restore` — 恢复指定会话（从垃圾箱还原为已发布状态）。
- `POST /api/Conversations/{id}/star` — 切换星标状态（星标/取消星标会话）
- `PUT /api/Conversations/{id}/status` — 更新会话状态
- `GET /api/Conversations/{id}/threads` — 获取会话的消息线程列表
- `GET /api/Conversations/advanced-search` — 高级搜索会话
- `POST /api/Conversations/bulk/assign` — 批量分配会话
- `POST /api/Conversations/bulk/delete` — 批量删除会话
- `POST /api/Conversations/bulk/priority` — 批量更新会话优先级
- `POST /api/Conversations/bulk/status` — 批量更新会话状态
- `GET /api/Conversations/drafts` — 获取当前用户的所有草稿
- `POST /api/Conversations/phone` — 创建电话会话
- `GET /api/Conversations/starred` — 获取当前用户星标的会话列表
- `GET /api/Conversations/starred-ids` — 获取当前用户星标的会话 ID 列表
- `DELETE /api/Conversations/trash` — 清空垃圾箱（彻底删除所有已软删除的会话）。
- `GET /api/Conversations/trash` — 获取垃圾箱列表（已软删除的会话）。

## Customers (14)
- `GET /api/Customers` — 搜索客户列表
- `POST /api/Customers` — 创建新客户
- `DELETE /api/Customers/{id}` — 删除客户（软删除）
- `GET /api/Customers/{id}` — 根据 ID 获取客户详情
- `PUT /api/Customers/{id}` — 更新客户信息
- `DELETE /api/Customers/{id}/hard` — 彻底删除客户
- `GET /api/Customers/{id}/hard-delete-status` — 查询指定客户彻底删除前的关联会话状态（活跃会话数、垃圾箱会话数）
- `POST /api/Customers/{id}/restore` — 恢复已删除的客户
- `POST /api/Customers/avatar` — 上传客户头像
- `POST /api/Customers/bulk-delete` — 批量删除客户（软删除，级联删除关联会话）
- `GET /api/Customers/export` — 导出客户数据（CSV）
- `DELETE /api/Customers/trash` — 清空垃圾箱（彻底删除所有已软删除的客户）
- `GET /api/Customers/trash` — 获取垃圾箱中的客户列表
- `GET /api/Customers/trash/clear-status` — 查询清空垃圾箱前的关联会话状态（垃圾箱客户数、活跃会话总数、垃圾箱会话总数）

## CustomFields (17)
- `GET /api/CustomFields` — 获取自定义字段列表（分页）
- `POST /api/CustomFields` — 创建自定义字段
- `DELETE /api/CustomFields/{id}` — 删除自定义字段
- `GET /api/CustomFields/{id}` — 获取自定义字段详情
- `PUT /api/CustomFields/{id}` — 更新自定义字段
- `DELETE /api/CustomFields/{id}/hard` — 彻底删除自定义字段（管理员）
- `POST /api/CustomFields/{id}/restore` — 恢复已删除的自定义字段
- `POST /api/CustomFields/{id}/toggle-active` — 切换启用/禁用状态
- `GET /api/CustomFields/channels` — 获取可用渠道列表（仅返回已启用渠道 + 全局选项），用于字段管理页"适用渠道"下拉。
- `GET /api/CustomFields/for-entity/{entityType}` — 获取指定对象类型可用的自定义字段（按渠道过滤）。
传入 sourceType（如 "wechat"）返回：全局字段 + 该渠道专属字段；
并按 HideWhenChannelDisabled 规则过滤已关闭渠道的字段。
- `GET /api/CustomFields/overview` — 获取自定义字段概览统计
- `DELETE /api/CustomFields/trash` — 清空垃圾箱（管理员）
- `GET /api/CustomFields/trash` — 获取垃圾箱中的自定义字段列表（分页）
- `DELETE /api/CustomFields/values/{entityType}/{entityId}` — 删除某个对象的所有自定义字段值
- `GET /api/CustomFields/values/{entityType}/{entityId}` — 获取某个对象的所有自定义字段值
- `PUT /api/CustomFields/values/{entityType}/{entityId}/{customFieldId}` — 设置某个对象的指定自定义字段值
- `POST /api/CustomFields/values/batch` — 批量设置某个对象的自定义字段值

## CustomReports (18)
- `GET /api/CustomReports` — 
- `POST /api/CustomReports` — 
- `DELETE /api/CustomReports/{id}` — 
- `GET /api/CustomReports/{id}` — 
- `PUT /api/CustomReports/{id}` — 
- `GET /api/CustomReports/{id}/execute` — 
- `GET /api/CustomReports/{id}/export` — 
- `DELETE /api/CustomReports/{id}/hard` — 永久删除（硬删除）垃圾箱中的报表
- `PATCH /api/CustomReports/{id}/pin` — 
- `POST /api/CustomReports/{id}/restore` — 恢复垃圾箱中的报表
- `GET /api/CustomReports/{id}/schedules` — 
- `POST /api/CustomReports/{id}/schedules` — 
- `POST /api/CustomReports/execute-preview` — 基于传入配置直接预览报表，不持久化到数据库。
用于编辑器中的"刷新预览"按钮，预览与保存解耦。
- `GET /api/CustomReports/pinned` — 
- `DELETE /api/CustomReports/schedules/{scheduleId}` — 
- `PUT /api/CustomReports/schedules/{scheduleId}` — 
- `GET /api/CustomReports/trash` — 获取已删除（垃圾箱）列表 - 支持分页与搜索
- `POST /api/CustomReports/trash/clear` — 清空垃圾箱（一次永久删除本人可见的所有已删除报表）

## Dashboard (1)
- `GET /api/Dashboard/stats` — 获取仪表盘统计数据

## DingDing (6)
- `GET /api/DingDing` — GET 验证回调 URL 有效性（钉钉事件订阅 HTTP 模式注册时触发）。
钉钉发送 GET 请求，query 含 signature/msg_signature、timestamp、nonce、encrypt，
服务端需校验签名 → AES 解密 encrypt → 返回明文 echostr 的加密 Map。
必须在 2500ms 内响应。
- `POST /api/DingDing` — POST 事件通知 - 接收钉钉推送的消息（兼容两种模式）。
模式1（事件订阅 HTTP 推送）：query 含 msg_signature/timestamp/nonce，body 含 encrypt 字段 → AES 解密后处理
模式2（机器人 outgoing Webhook）：header 含 timestamp/sign，body 为明文 JSON → 签名校验后处理
- `POST /api/DingDing/reply` — 回复客户消息
- `GET /api/DingDing/settings` — 获取钉钉配置（脱敏）
- `PUT /api/DingDing/settings` — 更新钉钉配置
- `GET /api/DingDing/status` — 获取钉钉连接状态

## EmailSignatures (15)
- `GET /api/EmailSignatures` — 获取签名列表（分页）
- `POST /api/EmailSignatures` — 创建签名
- `DELETE /api/EmailSignatures/{id}` — 删除签名
- `GET /api/EmailSignatures/{id}` — 获取签名详情
- `PUT /api/EmailSignatures/{id}` — 更新签名
- `DELETE /api/EmailSignatures/{id}/hard` — 彻底删除垃圾箱中的签名（物理删除）
- `POST /api/EmailSignatures/{id}/restore` — 恢复垃圾箱中的签名
- `POST /api/EmailSignatures/{id}/set-default` — 设置默认签名
- `GET /api/EmailSignatures/my` — 获取当前用户的签名列表
- `GET /api/EmailSignatures/my/default` — 获取当前用户的默认签名
- `GET /api/EmailSignatures/overview` — 获取概览统计
- `DELETE /api/EmailSignatures/trash` — 清空垃圾箱
- `GET /api/EmailSignatures/trash` — 获取垃圾箱中的签名列表（分页）
- `GET /api/EmailSignatures/trash/count` — 获取垃圾箱中的签名数量
- `GET /api/EmailSignatures/user/{userId}` — 获取指定用户的签名列表

## EmailTest (3)
- `GET /api/EmailTest/generate-message-id/{threadId}` — 测试生成 Message-ID
- `POST /api/EmailTest/parse` — 测试解析 EML 文件（支持 base64 编码以避免 JSON 转义问题）
- `POST /api/EmailTest/simulate-thread-match` — 测试线程匹配逻辑（内存模拟）

## Feishu (6)
- `GET /api/Feishu` — 飞书 URL 验证（GET）。
部分场景下飞书会通过 GET 携带 challenge 参数进行 URL 验证；
当 challenge 查询参数存在时原样回显，否则返回 403。
- `POST /api/Feishu` — 接收飞书推送的事件（POST）。
飞书事件订阅以 JSON 格式推送用户消息和事件，启用加密时需校验 X-Lark-Signature 并解密。
- `POST /api/Feishu/reply` — 通过飞书消息 API 回复客户。
- `GET /api/Feishu/settings` — 获取飞书配置（敏感字段脱敏）。
- `PUT /api/Feishu/settings` — 更新飞书配置。仅超级管理员可操作。
- `GET /api/Feishu/status` — 获取飞书连接状态。

## Folders (8)
- `POST /api/Folders` — 创建新文件夹
- `DELETE /api/Folders/{id}` — 删除文件夹
- `GET /api/Folders/{id}` — 根据 ID 获取文件夹详情
- `PUT /api/Folders/{id}` — 更新文件夹名称
- `GET /api/Folders/mailbox/{mailboxId}` — 获取邮箱下的文件夹列表
- `POST /api/Folders/mailbox/{mailboxId}/ensure-system` — 确保指定邮箱下存在所有系统文件夹（未分配/我的/星标/草稿/已分配/已关闭/已删除/垃圾邮件）
- `POST /api/Folders/mailbox/{mailboxId}/refresh-counters` — 重新计算并刷新指定邮箱下所有文件夹的活跃/总数计数
- `GET /api/Folders/user/{userId}` — 获取用户的文件夹列表

## ImportExport (4)
- `GET /api/ImportExport/entities` — 
- `POST /api/ImportExport/export` — 
- `POST /api/ImportExport/import` — 
- `POST /api/ImportExport/import/preview` — 

## Knowledge (25)
- `GET /api/Knowledge/articles` — 
- `POST /api/Knowledge/articles` — 
- `GET /api/Knowledge/articles/{articleId}/attachments` — 获取文章附件列表
- `POST /api/Knowledge/articles/{articleId}/attachments` — 上传文章附件（PDF/图片/文档）。仅超管可操作。
限制：25MB，扩展名白名单（pdf/jpg/png/gif/doc/docx/xls/xlsx/txt）。
- `DELETE /api/Knowledge/articles/{articleId}/attachments/{attachmentId}` — 删除文章附件（软删除 + 删除物理文件）。仅超管可操作。
- `GET /api/Knowledge/articles/{articleId}/attachments/{attachmentId}/download` — 下载文章附件。鉴权后返回文件流。
- `DELETE /api/Knowledge/articles/{id}` — 
- `GET /api/Knowledge/articles/{id}` — 
- `PUT /api/Knowledge/articles/{id}` — 
- `DELETE /api/Knowledge/articles/{id}/hard` — 
- `POST /api/Knowledge/articles/{id}/like` — 
- `POST /api/Knowledge/articles/{id}/restore` — 
- `DELETE /api/Knowledge/articles/trash` — 
- `GET /api/Knowledge/articles/trash` — 
- `GET /api/Knowledge/categories` — 
- `POST /api/Knowledge/categories` — 
- `DELETE /api/Knowledge/categories/{id}` — 
- `GET /api/Knowledge/categories/{id}` — 
- `PUT /api/Knowledge/categories/{id}` — 
- `DELETE /api/Knowledge/categories/{id}/hard` — 
- `POST /api/Knowledge/categories/{id}/restore` — 
- `DELETE /api/Knowledge/categories/trash` — 
- `GET /api/Knowledge/categories/trash` — 
- `GET /api/Knowledge/overview` — 
- `GET /api/Knowledge/search` — 

## Localization (11)
- `GET /api/Localization/diff/{culture}` — 获取翻译差异（未翻译的键）
- `GET /api/Localization/enums` — 获取常用枚举的本地化字典
- `GET /api/Localization/keys` — 获取所有翻译键列表（从资源文件）
- `GET /api/Localization/languages` — 获取支持的语言列表
- `GET /api/Localization/stats` — 获取翻译统计
- `GET /api/Localization/translate` — 获取指定键的本地化字符串
- `POST /api/Localization/translate-batch` — 批量获取多个键的本地化字符串
- `GET /api/Localization/translations/{culture}` — 获取指定语言的所有翻译（数据库优先）
- `PUT /api/Localization/translations/{culture}` — 更新或创建翻译
- `POST /api/Localization/translations/{culture}/export` — 导出翻译
- `POST /api/Localization/translations/{culture}/import` — 导入翻译

## Macros (17)
- `GET /api/Macros` — 获取宏列表（分页）
- `POST /api/Macros` — 创建宏
- `DELETE /api/Macros/{id}` — 删除宏（移入垃圾箱）
- `GET /api/Macros/{id}` — 获取宏详情
- `PUT /api/Macros/{id}` — 更新宏
- `POST /api/Macros/{id}/duplicate` — 复制宏（基于现有宏创建副本，UsageCount 重置为 0）
- `POST /api/Macros/{id}/execute` — 执行宏
- `DELETE /api/Macros/{id}/hard` — 彻底删除垃圾箱中的宏
- `POST /api/Macros/{id}/preview` — 预览宏执行（不实际执行，仅返回将要执行的动作描述）
- `POST /api/Macros/{id}/restore` — 恢复垃圾箱中的宏
- `POST /api/Macros/{id}/toggle-active` — 切换宏启用状态
- `GET /api/Macros/execution-logs` — 获取执行日志
- `GET /api/Macros/mailbox/{mailboxId}` — 获取指定邮箱可用的宏列表
- `GET /api/Macros/overview` — 获取宏概览统计
- `DELETE /api/Macros/trash` — 清空垃圾箱
- `GET /api/Macros/trash` — 获取垃圾箱中的宏列表
- `GET /api/Macros/trash/count` — 获取垃圾箱中的宏数量

## Mail (3)
- `POST /api/Mail/fetch/{mailboxId}` — 从邮箱收取邮件
- `POST /api/Mail/reply/{conversationId}` — 通过会话发送回复邮件
- `POST /api/Mail/send` — 发送邮件

## Mailboxes (20)
- `GET /api/Mailboxes` — 获取当前用户可访问的邮箱列表
- `POST /api/Mailboxes` — 创建新邮箱
- `DELETE /api/Mailboxes/{id}` — 删除邮箱（软删除，移入垃圾箱）
- `GET /api/Mailboxes/{id}` — 根据 ID 获取邮箱详情
- `PUT /api/Mailboxes/{id}` — 更新邮箱配置
- `GET /api/Mailboxes/{id}/auto-reply` — 获取邮箱自动回复
- `PUT /api/Mailboxes/{id}/auto-reply` — 更新邮箱自动回复
- `DELETE /api/Mailboxes/{id}/hard` — 彻底删除邮箱（从数据库永久删除）
- `GET /api/Mailboxes/{id}/imap-folders` — 获取IMAP文件夹列表
- `POST /api/Mailboxes/{id}/oauth/callback` — 完成OAuth授权（已弃用）
- `GET /api/Mailboxes/{id}/permissions` — 获取邮箱权限
- `PUT /api/Mailboxes/{id}/permissions` — 更新邮箱权限
- `POST /api/Mailboxes/{id}/restore` — 从垃圾箱恢复邮箱
- `GET /api/Mailboxes/{id}/signature` — 获取邮箱签名
- `PUT /api/Mailboxes/{id}/signature` — 更新邮箱签名
- `GET /api/Mailboxes/{id}/test-connection` — 测试邮箱连接
- `POST /api/Mailboxes/{id}/toggle-active` — 切换邮箱启用状态
- `GET /api/Mailboxes/all` — 获取所有邮箱列表
- `DELETE /api/Mailboxes/trash` — 清空垃圾箱（永久删除所有已软删除的邮箱）
- `GET /api/Mailboxes/trash` — 获取垃圾箱中的邮箱列表

## MailTemplates (15)
- `GET /api/MailTemplates` — 获取邮件模板列表（分页）
- `POST /api/MailTemplates` — 创建邮件模板
- `DELETE /api/MailTemplates/{id}` — 删除邮件模板（移至垃圾箱）
- `GET /api/MailTemplates/{id}` — 获取模板详情
- `PUT /api/MailTemplates/{id}` — 更新邮件模板
- `DELETE /api/MailTemplates/{id}/hard` — 彻底删除模板（从垃圾箱永久删除）
- `POST /api/MailTemplates/{id}/preview` — 预览指定邮件模板的渲染结果
- `POST /api/MailTemplates/{id}/restore` — 恢复垃圾箱中的模板
- `GET /api/MailTemplates/overview` — 获取模板概览统计
- `POST /api/MailTemplates/preview` — 预览模板渲染结果
- `GET /api/MailTemplates/slug/{slug}` — 根据标识获取模板
- `DELETE /api/MailTemplates/trash` — 清空垃圾箱
- `GET /api/MailTemplates/trash` — 获取垃圾箱模板列表（分页）
- `GET /api/MailTemplates/trash/count` — 获取垃圾箱模板数量
- `GET /api/MailTemplates/variables/built-in` — 获取内置变量列表

## Modules (10)
- `GET /api/Modules` — 获取所有模块（分页）
- `POST /api/Modules` — 创建/安装新模块
- `DELETE /api/Modules/{name}` — 卸载/删除模块
- `GET /api/Modules/{name}` — 按名称获取模块详情
- `PUT /api/Modules/{name}` — 更新模块
- `POST /api/Modules/{name}/toggle` — 切换模块启用状态
- `GET /api/Modules/active` — 获取已启用的模块
- `GET /api/Modules/all` — 获取所有模块（不分页）
- `GET /api/Modules/category/{category}` — 按分类获取模块
- `GET /api/Modules/overview` — 获取模块概览统计

## Notifications (33)
- `POST /api/Notifications/preview` — 预览模板渲染结果
- `GET /api/Notifications/rules` — 分页获取通知规则
- `POST /api/Notifications/rules` — 创建通知规则
- `DELETE /api/Notifications/rules/{id}` — 删除通知规则
- `GET /api/Notifications/rules/{id}` — 获取通知规则详情
- `PUT /api/Notifications/rules/{id}` — 更新通知规则
- `POST /api/Notifications/rules/{id}/duplicate` — 复制通知规则
- `DELETE /api/Notifications/rules/{id}/hard` — 彻底删除通知规则
- `POST /api/Notifications/rules/{id}/restore` — 恢复通知规则
- `POST /api/Notifications/rules/{id}/toggle` — 切换通知规则启用状态
- `GET /api/Notifications/rules/all` — 获取所有通知规则（不分页）
- `GET /api/Notifications/rules/overview` — 获取通知规则概览
- `DELETE /api/Notifications/rules/trash` — 清空通知规则垃圾箱
- `GET /api/Notifications/rules/trash` — 获取通知规则垃圾箱列表
- `GET /api/Notifications/rules/trash/count` — 获取通知规则垃圾箱数量
- `GET /api/Notifications/rules/type/{eventType}` — 按事件类型获取通知规则
- `POST /api/Notifications/send` — 发送通知（手动触发）
- `GET /api/Notifications/templates` — 分页获取通知模板
- `POST /api/Notifications/templates` — 创建通知模板
- `DELETE /api/Notifications/templates/{id}` — 删除通知模板
- `GET /api/Notifications/templates/{id}` — 获取通知模板详情
- `PUT /api/Notifications/templates/{id}` — 更新通知模板
- `POST /api/Notifications/templates/{id}/duplicate` — 复制通知模板
- `DELETE /api/Notifications/templates/{id}/hard` — 彻底删除通知模板
- `POST /api/Notifications/templates/{id}/preview` — 预览通知模板渲染结果
- `POST /api/Notifications/templates/{id}/restore` — 恢复通知模板
- `POST /api/Notifications/templates/{id}/toggle` — 切换通知模板启用状态
- `GET /api/Notifications/templates/all` — 获取所有通知模板（不分页）
- `GET /api/Notifications/templates/overview` — 获取通知模板概览
- `DELETE /api/Notifications/templates/trash` — 清空通知模板垃圾箱
- `GET /api/Notifications/templates/trash` — 获取通知模板垃圾箱列表
- `GET /api/Notifications/templates/trash/count` — 获取通知模板垃圾箱数量
- `POST /api/Notifications/templates/validate-variables` — 校验模板变量

## Oauth (5)
- `GET /api/oauth/mailbox/{mailboxId}/authorize` — 
- `POST /api/oauth/mailbox/{mailboxId}/refresh` — 
- `DELETE /api/oauth/mailbox/{mailboxId}/token` — 
- `POST /api/oauth/mailbox/{mailboxId}/token` — 
- `GET /api/oauth/providers` — 

## OutlookSync (10)
- `GET /api/calendar/outlook/sync-logs` — 任务 D#2：查询 Outlook 同步审计日志（按 mailboxId 分页）。
路由：GET /api/calendar/outlook/sync-logs（覆盖默认前缀）。
- `GET /api/outlooksync/credentials` — T000099: 获取当前用户的 Outlook 同步凭据配置（Azure AD 应用注册凭据）。
每个用户可自配置自己的 Azure AD 应用注册凭据，空值回退到 appsettings 基线。
返回脱敏配置信息（ClientSecret 仅返回是否存在标识，不返回明文）。
路由：GET /api/outlooksync/credentials
- `PUT /api/outlooksync/credentials` — T000099: 更新当前用户的 Outlook 同步凭据配置（Azure AD 应用注册凭据）。
每个用户可自配置自己的凭据，支持部分更新（仅非 null 字段会被写入）。
ClientSecret 特殊值 "CLEAR" 表示清空已配置的密钥；空字符串/null 表示不更新。
ClientSecret 由 EF Core HasConversion 自动加密存储，无需手动加密。
路由：PUT /api/outlooksync/credentials
- `DELETE /api/outlooksync/disconnect` — 断开 Outlook 连接（清除令牌，保留设置记录）
- `GET /api/outlooksync/oauth/authorize` — 生成 OAuth 授权 URL（前端跳转到此 URL 进行授权）
T000099: 使用用户级 Azure AD 凭据生成授权 URL（每个用户自配置的 ClientId/RedirectUri）
- `GET /api/outlooksync/oauth/callback` — OAuth 回调端点（Azure AD 重定向到此 URL）
注意：此端点允许匿名访问，因为回调时浏览器未携带 JWT（access_token Cookie 为 SameSite=Strict）
H-O3: 通过 outlook_oauth_session Cookie（SameSite=Lax）校验登录态，确保回调发起者即授权发起者
H-06: state 参数抗 CSRF 已确认——BuildState 使用 IEncryptionService 加密
  {domain}|{userId}|{expiresAt(ISO)}|{nonce(128bit)}，回调时 TryParseStateAsync
  解密+验签+10 分钟有效期+分布式缓存 nonce 防重放；Controller 二次校验
  state.userId == sessionCookie.userId，三层防护防止账号绑定劫持。
- `GET /api/outlooksync/setting` — 获取当前用户的同步设置
- `PUT /api/outlooksync/setting` — 更新同步设置（启用/禁用、同步方向）
- `GET /api/outlooksync/status` — 获取当前用户的同步状态（前端轮询使用）
- `POST /api/outlooksync/sync` — 触发一次同步（用户主动点击同步按钮）

## Profile (6)
- `GET /api/Profile` — 获取当前用户个人资料
- `PUT /api/Profile` — 更新当前用户个人资料
- `DELETE /api/Profile/avatar` — 移除当前用户头像
- `POST /api/Profile/avatar` — 上传当前用户头像
- `POST /api/Profile/change-password` — 修改当前用户密码
- `GET /api/Profile/stats` — 获取当前用户的个人统计信息

## Realtime (4)
- `POST /api/realtime/conv-updated` — 通知会话已更新
对应 PHP: RealtimeController@convUpdated
- `POST /api/realtime/conv-view` — 通知其他用户当前用户正在查看某个会话
对应 PHP: RealtimeController@convView
- `POST /api/realtime/user-stop-typing` — 通知用户停止输入
- `POST /api/realtime/user-typing` — 用户上线通知
对应 PHP: RealtimeController@userTyping

## Reports (2)
- `GET /api/Reports` — 获取综合报表
- `GET /api/Reports/user-performance` — 分页获取客服绩效报表

## Roles (19)
- `GET /api/Roles` — 获取角色列表（分页）
- `POST /api/Roles` — 创建角色
- `DELETE /api/Roles/{id}` — 删除角色（移至垃圾箱）
- `GET /api/Roles/{id}` — 获取角色详情
- `PUT /api/Roles/{id}` — 更新角色
- `DELETE /api/Roles/{id}/hard` — 彻底删除角色（从垃圾箱永久删除）
- `POST /api/Roles/{id}/restore` — 恢复垃圾箱中的角色
- `GET /api/Roles/{id}/users` — 获取角色的用户列表
- `POST /api/Roles/assign` — 分配角色给用户
- `DELETE /api/Roles/assignments/{userRoleId}` — 移除用户角色分配
- `POST /api/Roles/batch-assign` — 批量分配角色
- `GET /api/Roles/overview` — 获取角色概览
- `GET /api/Roles/permissions` — 获取所有权限分组定义
- `DELETE /api/Roles/trash` — 清空垃圾箱
- `GET /api/Roles/trash` — 获取垃圾箱角色列表（分页）
- `GET /api/Roles/trash/count` — 获取垃圾箱角色数量
- `GET /api/Roles/user/{userId}` — 获取用户的角色列表
- `GET /api/Roles/user/{userId}/has-permission` — 检查用户是否拥有某权限
- `GET /api/Roles/user/{userId}/permissions` — 获取用户的权限列表

## SatisfactionRatings (9)
- `GET /api/SatisfactionRatings` — 
- `POST /api/SatisfactionRatings` — 
- `DELETE /api/SatisfactionRatings/{id}` — 
- `GET /api/SatisfactionRatings/{id}` — 
- `GET /api/SatisfactionRatings/conversation/{conversationId}` — 
- `GET /api/SatisfactionRatings/overview` — 
- `GET /api/SatisfactionRatings/rating-options` — 
- `GET /api/SatisfactionRatings/recent` — 
- `GET /api/SatisfactionRatings/validate/{token}` — 

## Search (1)
- `GET /api/Search` — 全局搜索（跨会话、客户、知识库等实体类型）

## SendLogs (6)
- `GET /api/SendLogs` — 分页查询发送日志
- `GET /api/SendLogs/{id}` — 获取日志详情
- `GET /api/SendLogs/mail-type-options` — 获取邮件类型选项
- `GET /api/SendLogs/overview` — 获取发送日志总览（统计、邮箱分布、趋势、最近失败）
- `GET /api/SendLogs/recent-failures` — 获取最近失败的发送记录
- `GET /api/SendLogs/status-options` — 获取发送状态选项

## Setup (4)
- `POST /api/Setup/admin` — 
- `POST /api/Setup/complete` — 
- `POST /api/Setup/mailbox` — 
- `GET /api/Setup/status` — 

## Sla (17)
- `GET /api/Sla` — 分页获取 SLA 策略列表。
- `POST /api/Sla` — 创建 SLA 策略。
- `DELETE /api/Sla/{id}` — 
- `GET /api/Sla/{id}` — 
- `PUT /api/Sla/{id}` — 
- `DELETE /api/Sla/{id}/hard` — 彻底删除指定 SLA 策略（不可恢复）。
- `POST /api/Sla/{id}/restore` — 恢复软删除的 SLA 策略。
- `POST /api/Sla/{id}/toggle-active` — 切换 SLA 策略启用状态。
- `POST /api/Sla/apply/{conversationId}` — 为指定会话手动应用 SLA 策略（创建预警记录）。
- `GET /api/Sla/conversations/{conversationId}/status` — 获取指定会话的 SLA 状态（首次响应和解决时长）。
- `GET /api/Sla/conversations/{conversationId}/violations` — 获取指定会话的 SLA 违规记录。
- `GET /api/Sla/overview` — SLA 概览：策略统计、违规统计、最近违规、预警列表。
- `POST /api/Sla/scan` — 手动触发 SLA 违规扫描。扫描所有活跃会话，更新违规状态。
- `DELETE /api/Sla/trash` — 清空垃圾箱：彻底删除所有已软删除的 SLA 策略。
- `GET /api/Sla/trash` — 获取已软删除的 SLA 策略列表（垃圾箱）。
- `GET /api/Sla/trash/count` — 获取垃圾箱中的 SLA 策略数量。
- `GET /api/Sla/violations` — 分页获取 SLA 违规记录。

## System (8)
- `POST /api/system/health-check` — 
- `GET /api/system/info` — 
- `GET /api/system/status` — 
- `POST /api/system/tools/clear-cache` — 
- `POST /api/system/tools/logout-users` — 
- `GET /api/system/tools/logs` — 
- `POST /api/system/tools/logs/clear` — 删除系统日志中的 log 记录：清空日志目录下所有 log-*.log 文件内容（截断为 0 字节，保留文件本身）。
系统工具-系统日志「清空日志文件」按钮的后端载体，仅清理 Serilog 主日志（log-*.log），
与 GetSystemLogs 展示范围保持一致；Serilog 以 shared:true 追加写入，截断后新日志正常追加。
- `POST /api/system/tools/optimize-db` — 

## SystemOptions (8)
- `GET /api/SystemOptions` — 获取所有系统配置项
- `POST /api/SystemOptions` — 创建新的配置项
- `DELETE /api/SystemOptions/{name}` — 删除配置项
- `GET /api/SystemOptions/{name}` — 按名称获取配置项
- `PUT /api/SystemOptions/{name}` — 更新配置项
- `PUT /api/SystemOptions/batch` — 批量更新系统配置
- `GET /api/SystemOptions/category/{category}` — 按分类获取系统配置项
- `GET /api/SystemOptions/dictionary` — 获取所有配置项为字典格式

## Tags (15)
- `GET /api/Tags` — 获取标签列表
- `POST /api/Tags` — 创建新标签
- `DELETE /api/Tags/{id}` — 删除标签（软删除，移至垃圾箱）
- `GET /api/Tags/{id}` — 获取标签详情
- `PUT /api/Tags/{id}` — 更新标签
- `DELETE /api/Tags/{id}/hard` — 彻底删除标签
- `POST /api/Tags/{id}/restore` — 恢复已删除的标签
- `GET /api/Tags/conversations/{conversationId}` — 获取会话的标签列表
- `PUT /api/Tags/conversations/{conversationId}` — 更新会话的标签（全量替换）
- `DELETE /api/Tags/conversations/{conversationId}/tags/{tagId}` — 从会话移除单个标签
- `POST /api/Tags/conversations/{conversationId}/tags/{tagId}` — 给会话添加单个标签
- `POST /api/Tags/conversations/bulk` — 批量更新会话标签
- `DELETE /api/Tags/trash` — 清空垃圾箱（彻底删除所有已软删除的标签）
- `GET /api/Tags/trash` — 获取垃圾箱中的标签列表
- `GET /api/Tags/trash/count` — 获取垃圾箱中标签的数量

## Teams (17)
- `GET /api/Teams` — 获取团队列表（分页）
- `POST /api/Teams` — 创建团队
- `DELETE /api/Teams/{id}` — 删除团队（移至垃圾箱）
- `GET /api/Teams/{id}` — 获取团队详情
- `PUT /api/Teams/{id}` — 更新团队
- `DELETE /api/Teams/{id}/hard` — 彻底删除团队（从垃圾箱永久删除）
- `GET /api/Teams/{id}/members` — 获取团队成员列表
- `POST /api/Teams/{id}/members` — 添加团队成员
- `DELETE /api/Teams/{id}/members/{userId}` — 移除团队成员
- `PUT /api/Teams/{id}/members/{userId}` — 更新成员角色
- `POST /api/Teams/{id}/restore` — 恢复垃圾箱中的团队
- `GET /api/Teams/mailbox/{mailboxId}` — 按邮箱获取团队列表
- `GET /api/Teams/overview` — 获取团队概览统计
- `DELETE /api/Teams/trash` — 清空垃圾箱
- `GET /api/Teams/trash` — 获取垃圾箱团队列表（分页）
- `GET /api/Teams/trash/count` — 获取垃圾箱团队数量
- `GET /api/Teams/user/{userId}` — 获取用户所属的团队

## TimeTracking (15)
- `GET /api/time-tracking` — 获取时间记录列表
- `POST /api/time-tracking` — 创建手动时间记录
- `DELETE /api/time-tracking/{id}` — 删除时间记录（软删除）
- `GET /api/time-tracking/{id}` — 获取时间记录详情
- `PUT /api/time-tracking/{id}` — 更新时间记录
- `DELETE /api/time-tracking/{id}/hard` — 永久删除时间记录
- `POST /api/time-tracking/{id}/restore` — 恢复已删除的时间记录
- `GET /api/time-tracking/export` — 导出CSV
- `GET /api/time-tracking/overview` — 获取概览统计
- `GET /api/time-tracking/report` — 获取报表数据
- `POST /api/time-tracking/timer/{id}/stop` — 停止计时器
- `GET /api/time-tracking/timer/active` — 获取当前运行中的计时器
- `POST /api/time-tracking/timer/start` — 开始计时器
- `GET /api/time-tracking/trash` — 获取垃圾箱列表
- `POST /api/time-tracking/trash/clear` — 清空垃圾箱

## Tracking (2)
- `GET /api/Tracking/open/{id}` — 邮件打开追踪像素 - 旧版无 token 路由（已废弃，保留仅为向后兼容）
H-2 修复：新邮件应使用带 token 的路由 open/{id:int}/{token}。
旧路由保留以便存量已发送邮件的追踪像素链接不立即失效，后续版本将移除。
- `GET /api/Tracking/open/{id}/{token}` — 邮件打开追踪像素 - 返回 1x1 透明 GIF（带 token 校验，推荐路由）
H-2 修复：通过 HMAC token 校验防止任意用户伪造 id 触发计数/通知。
token 生成方：在邮件发送端生成像素链接时附加 token = SecretHasher.Hash($"email-track:{id}")。

## UserProfile (7)
- `GET /api/user-profile/{userId}/permissions` — 获取用户权限
对应 PHP: UsersController@permissions (GET)
- `PUT /api/user-profile/{userId}/permissions` — 更新用户权限
对应 PHP: UsersController@permissions (POST)
- `GET /api/user-profile/me/notifications` — 获取用户通知设置
对应 PHP: UsersController@notifications (GET)
- `PUT /api/user-profile/me/notifications` — 更新用户通知设置
对应 PHP: UsersController@notifications (POST)
- `POST /api/user-profile/me/password` — 修改密码
对应 PHP: UsersController@password (POST)
- `GET /api/user-profile/me/profile` — 获取个人资料
对应 PHP: UsersController@profile (GET)
- `PUT /api/user-profile/me/profile` — 更新个人资料
对应 PHP: UsersController@profile (POST)

## Users (13)
- `GET /api/Users` — 获取用户列表（分页）
- `POST /api/Users` — 创建新用户
- `DELETE /api/Users/{id}` — 删除用户（软删除，移入垃圾箱）
- `GET /api/Users/{id}` — 根据 ID 获取用户详情
- `PUT /api/Users/{id}` — 更新用户信息
- `POST /api/Users/{id}/activate` — 激活用户（将状态从待激活改为活跃）
- `DELETE /api/Users/{id}/hard` — 彻底删除用户（从数据库永久删除）
- `POST /api/Users/{id}/restore` — 从垃圾箱恢复用户
- `POST /api/Users/{id}/status` — 修改用户状态
- `GET /api/Users/assignable` — 获取可被分配会话的活跃用户列表（不分页）。
供会话/工单的"分配负责人"下拉框使用：拥有 conversations.assign 权限的客服主管/客服
无需 SuperAdmin 即可获取可分配的活跃客服列表，修复客服主管分配会话时下拉框为空的问题（T000061）。
仅返回 Status=Active 的用户，避免分配给已禁用/待激活账户。
- `GET /api/Users/stats` — 获取用户统计数据
- `DELETE /api/Users/trash` — 清空垃圾箱（永久删除所有已软删除的用户）
- `GET /api/Users/trash` — 获取垃圾箱中的用户列表

## Webhooks (14)
- `GET /api/Webhooks` — 获取 Webhook 列表（分页）
- `POST /api/Webhooks` — 创建 Webhook
- `DELETE /api/Webhooks/{id}` — 删除 Webhook
- `GET /api/Webhooks/{id}` — 获取 Webhook 详情
- `PUT /api/Webhooks/{id}` — 更新 Webhook
- `DELETE /api/Webhooks/{id}/hard` — 彻底删除 Webhook（管理员）
- `GET /api/Webhooks/{id}/logs` — 获取 Webhook 触发日志
- `POST /api/Webhooks/{id}/restore` — 恢复已删除的 Webhook
- `POST /api/Webhooks/{id}/test` — 测试 Webhook 连通性
- `POST /api/Webhooks/{id}/toggle-active` — 切换启用/禁用状态
- `GET /api/Webhooks/logs` — 获取所有 Webhook 触发日志（分页）
- `GET /api/Webhooks/overview` — 获取 Webhook 概览统计
- `DELETE /api/Webhooks/trash` — 清空垃圾箱（管理员）
- `GET /api/Webhooks/trash` — 获取垃圾箱中的 Webhook 列表（分页）

## WeChat (12)
- `GET /api/WeChat` — 微信服务器签名校验（GET）。
微信公众平台后台配置 URL 后，会发送 GET 请求校验签名。
校验通过返回 echostr 随机字符串。
- `POST /api/WeChat` — 接收微信推送的消息（POST）。
微信服务器以 XML 格式推送用户消息和事件。
- `DELETE /api/WeChat/menu` — 删除微信自定义菜单。
- `GET /api/WeChat/menu` — 查询当前微信自定义菜单。
- `POST /api/WeChat/menu` — 创建微信自定义菜单。
- `POST /api/WeChat/reply` — 通过微信客服消息接口回复客户。
- `GET /api/WeChat/settings` — 获取微信配置（敏感字段脱敏）。
- `PUT /api/WeChat/settings` — 更新微信配置。仅超级管理员可操作。
- `GET /api/WeChat/status` — 获取微信连接状态。
- `POST /api/WeChat/subscribe/send` — 发送微信订阅通知到指定会话的客户（新版，替代旧版模板消息）。
- `POST /api/WeChat/template/send` — 发送微信模板消息到指定会话的客户。
- `POST /api/WeChat/test/location` — 【测试】注入一条模拟的微信位置消息，用于本地验证前端点击跳转效果。
仅限超级管理员访问，绕过签名校验直接调用 ingestion 服务。

## WhatsApp (8)
- `GET /api/WhatsApp` — Webhook GET 验证 - Meta 配置 Webhook 时发送的订阅验证请求。
- `POST /api/WhatsApp` — Webhook POST 事件通知 - 接收 Meta 推送的消息/状态回执。
- `POST /api/WhatsApp/reply` — 回复客户消息
- `GET /api/WhatsApp/settings` — 获取 WhatsApp 配置（脱敏）
- `PUT /api/WhatsApp/settings` — 更新 WhatsApp 配置
- `GET /api/WhatsApp/status` — 获取 WhatsApp 连接状态
- `POST /api/WhatsApp/template/send` — 发送模板消息
- `POST /api/WhatsApp/test` — 测试连接 — 向商家号码发送测试消息验证配置是否正确

## Workflows (15)
- `GET /api/Workflows` — 
- `POST /api/Workflows` — 
- `DELETE /api/Workflows/{id}` — 
- `GET /api/Workflows/{id}` — 
- `PUT /api/Workflows/{id}` — 
- `DELETE /api/Workflows/{id}/hard` — 
- `POST /api/Workflows/{id}/restore` — 
- `POST /api/Workflows/{id}/test` — 
- `POST /api/Workflows/{id}/toggle-active` — 
- `POST /api/Workflows/execute/{conversationId}/{triggerType}` — 
- `GET /api/Workflows/execution-logs` — 
- `GET /api/Workflows/overview` — 
- `DELETE /api/Workflows/trash` — 
- `GET /api/Workflows/trash` — 
- `GET /api/Workflows/trash/count` — 


