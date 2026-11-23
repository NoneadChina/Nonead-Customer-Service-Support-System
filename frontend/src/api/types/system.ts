// ====== 审计日志相关 ======
export interface AuditLogResponse {
  id: number;
  userId: number | null;
  userName: string;
  action: string;
  entityType: string;
  entityId: number | null;
  oldValues: string;
  newValues: string;
  ipAddress: string;
  userAgent: string;
  createdAt: string;
  /** HMAC 签名（Base64），仅超管可见 */
  hmacSignature?: string;
  /** 签名校验状态：true=被篡改，false=正常，null=未校验 */
  isTampered?: boolean | null;
}

export interface AuditLogListResponse {
  data: AuditLogResponse[];
  currentPage: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

/** 审计日志全局统计（基于全量数据，受同一组过滤条件约束） */
export interface AuditLogStatsResponse {
  totalLogs: number;
  uniqueUsers: number;
  last24hCount: number;
  failedActions: number;
  actionDistribution: AuditLogDistributionItem[];
  entityDistribution: AuditLogDistributionItem[];
}

export interface AuditLogDistributionItem {
  key: string;
  count: number;
}

/** 单条审计日志完整性校验结果 */
export interface AuditLogIntegrityResult {
  id: number;
  isValid: boolean;
  storedSignature: string;
  expectedSignature: string;
  verifiedAt: string;
}

/** 关联日志响应 */
export interface AuditLogRelatedResponse {
  source: 'entity' | 'user' | 'none';
  items: AuditLogResponse[];
}

/** 批量完整性校验报告 */
export interface AuditIntegrityReport {
  page: number;
  pageSize: number;
  totalLogs: number;
  checkedCount: number;
  tamperedCount: number;
  tamperedLogIds: number[];
}

// ====== 系统配置相关 ======
export interface SystemOptionResponse {
  id: number;
  name: string;
  value: string;
  category: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateSystemOptionRequest {
  name: string;
  value: string;
  category?: string | null;
}

export interface UpdateSystemOptionRequest {
  name: string;
  value: string;
  category?: string | null;
}

// ====== 本地化相关 ======
export interface SupportedLanguagesResponse {
  current: string;
  supported: string[];
}

export interface TranslateResponse {
  key: string;
  value: string;
  language: string;
}

// ====== 邮件测试相关 ======
export interface MailSendResult {
  success: boolean;
  message: string;
}

export interface MailFetchResult {
  success: boolean;
  count: number;
  message?: string;
}

// ====== 模块管理 ======
export interface ModuleDto {
  id: number;
  name: string;
  title: string;
  description: string;
  version: string;
  author: string;
  icon: string;
  /** 启用状态。对于核心模块（isSystemModule=true）永远为 true。 */
  isActive: boolean;
  category: string;
  settings: Record<string, unknown> | null;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
  /** 核心模块标识：true 表示不能被禁用/删除 */
  isSystemModule: boolean;
}

export interface ModuleCategoryStats {
  category: string;
  categoryLabel: string;
  count: number;
  activeCount: number;
}

export interface ModuleOverviewResponse {
  total: number;
  active: number;
  inactive: number;
  categories: ModuleCategoryStats[];
}

export interface CreateModuleRequest {
  name: string;
  title: string;
  description: string;
  version: string;
  author: string;
  icon: string;
  category: string;
  settings?: Record<string, unknown> | null;
  sortOrder: number;
}

export interface UpdateModuleRequest {
  title: string;
  description: string;
  version: string;
  author: string;
  icon: string;
  category: string;
  settings?: Record<string, unknown> | null;
  sortOrder: number;
}

export interface ModuleQuery {
  page?: number;
  pageSize?: number;
  category?: string | null;
  isActive?: boolean | null;
  search?: string | null;
}

// ====== API 密钥 ======
export interface ApiKeyDto {
  id: number;
  name: string;
  description: string | null;
  keyPrefix: string;
  key?: string | null;
  userId: number | null;
  userName: string | null;
  scopes: string[];
  isActive: boolean;
  expiresAt: string | null;
  lastUsedAt: string | null;
  lastUsedIp: string | null;
  usageCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface ApiKeyLogDto {
  id: number;
  apiKeyId: number;
  apiKeyName: string;
  endpoint: string;
  method: string;
  statusCode: number;
  ipAddress: string | null;
  userAgent: string | null;
  durationMs: number;
  errorMessage: string | null;
  createdAt: string;
}

export interface ApiKeyOverviewResponse {
  totalKeys: number;
  activeKeys: number;
  inactiveKeys: number;
  totalUsage: number;
  recentKeys: ApiKeyDto[];
  recentLogs: ApiKeyLogDto[];
  scopesCount: Record<string, number>;
}

export interface CreateApiKeyRequest {
  name: string;
  description?: string | null;
  userId?: number | null;
  scopes: string[];
  expiresAt?: string | null;
  isActive?: boolean;
}

export interface UpdateApiKeyRequest {
  name: string;
  description?: string | null;
  scopes: string[];
  expiresAt?: string | null;
  isActive: boolean;
}

export interface ApiKeyQuery {
  page?: number;
  pageSize?: number;
  userId?: number | null;
  isActive?: boolean | null;
  keyword?: string | null;
  scope?: string | null;
}

export interface ApiKeyLogQuery {
  page?: number;
  pageSize?: number;
  apiKeyId?: number | null;
  endpoint?: string | null;
  statusCode?: number | null;
}

// ====== 微信服务号接入 ======
export interface WeChatSettingsDto {
  appId: string | null;
  hasAppSecret: boolean;
  hasToken: boolean;
  hasEncodingAESKey: boolean;
  encryptMode: string;
  mailboxId: number | null;
  contactCollectionEnabled: boolean;
  autoReplyEnabled: boolean;
  autoReplyMessage: string | null;
  enabled: boolean;
  callbackUrl: string | null;
}

export interface UpdateWeChatSettingsRequest {
  appId?: string | null;
  appSecret?: string | null;
  token?: string | null;
  encodingAESKey?: string | null;
  encryptMode?: string;
  mailboxId?: number | null;
  contactCollectionEnabled?: boolean;
  autoReplyEnabled?: boolean;
  autoReplyMessage?: string | null;
  enabled?: boolean;
}

export interface WeChatStatusDto {
  configured: boolean;
  enabled: boolean;
  tokenValid: boolean;
  callbackUrl: string | null;
}

// 微信客服消息回复
export interface WeChatReplyRequest {
  conversationId: number;
  content: string;
  attachmentIds?: number[];
}

export interface WeChatReplyResult {
  sent: boolean;
  /** 线程记录是否创建成功（消息已发送但线程记录失败时为 false，可能造成会话数据不一致） */
  threadCreated?: boolean;
  threadId?: number | null;
  errorMessage?: string | null;
}

// 微信模板消息
export interface WeChatTemplateRequest {
  conversationId: number;
  templateId: string;
  dataJson: string;
  url?: string | null;
}

export interface WeChatTemplateResult {
  sent: boolean;
  errorMessage?: string | null;
}

// 微信订阅通知（新版，替代旧版模板消息）
export interface WeChatSubscribeRequest {
  conversationId: number;
  templateId: string;
  dataJson: string;
  page?: string | null;
}

// 微信自定义菜单
export interface WeChatMenuRequest {
  menuJson: string;
}

export interface WeChatMenuResult {
  success: boolean;
  errorMessage?: string | null;
}

// ===== WhatsApp Types =====

export interface WhatsAppSettingsDto {
  phoneNumberId: string | null;
  businessPhoneNumber: string | null;
  apiVersion: string | null;
  mailboxId: number | null;
  contactCollectionEnabled: boolean;
  autoReplyEnabled: boolean;
  autoReplyMessage: string | null;
  enabled: boolean;
  callbackUrl: string | null;
  hasAccessToken: boolean;
  hasAppSecret: boolean;
  hasVerifyToken: boolean;
}

export interface UpdateWhatsAppSettingsRequest {
  phoneNumberId?: string | null;
  accessToken?: string | null;
  appSecret?: string | null;
  verifyToken?: string | null;
  businessPhoneNumber?: string | null;
  apiVersion?: string | null;
  mailboxId?: number | null;
  contactCollectionEnabled?: boolean | null;
  autoReplyEnabled?: boolean | null;
  autoReplyMessage?: string | null;
  enabled?: boolean | null;
}

export interface WhatsAppStatusDto {
  configured: boolean;
  enabled: boolean;
  apiReachable: boolean;
  callbackUrl: string | null;
  windowStatus: 'ok' | 'warning' | 'expired' | 'unknown';
  windowRemainingHours: number;
  lastCustomerMessageAt: string | null;
}

export interface WhatsAppReplyRequest {
  conversationId: number;
  content?: string | null;
  attachmentIds?: number[];
}

export interface WhatsAppReplyResult {
  sent: boolean;
  /** 线程记录是否创建成功（消息已发送但线程记录失败时为 false，可能造成会话数据不一致） */
  threadCreated?: boolean;
  threadId?: number | null;
  errorMessage?: string | null;
}

export interface WhatsAppTemplateRequest {
  conversationId: number;
  templateName: string;
  language: string;
  componentsJson?: string | null;
}

export interface WhatsAppTemplateResult {
  sent: boolean;
  messageId: string | null;
  errorMessage: string | null;
}

export interface WhatsAppTestResult {
  success: boolean;
  messageId: string | null;
  errorMessage: string | null;
}

// ===== Feishu 飞书集成 =====
export interface FeishuSettingsDto {
  appId: string | null;
  hasAppSecret: boolean;
  hasVerificationToken: boolean;
  hasEncryptKey: boolean;
  encryptEnabled: boolean;
  mailboxId: number | null;
  contactCollectionEnabled: boolean;
  autoReplyEnabled: boolean;
  autoReplyMessage: string | null;
  enabled: boolean;
  callbackUrl: string | null;
  /** 事件接收模式："Http"（Webhook 推送）或 "Stream"（WebSocket 长连接） */
  receiveMode: string | null;
}

export interface UpdateFeishuSettingsRequest {
  appId?: string;
  appSecret?: string;
  verificationToken?: string;
  encryptKey?: string;
  encryptEnabled?: boolean;
  mailboxId?: number;
  contactCollectionEnabled?: boolean;
  autoReplyEnabled?: boolean;
  autoReplyMessage?: string;
  enabled?: boolean;
  /** 事件接收模式："Http" 或 "Stream"。不传表示不修改 */
  receiveMode?: string;
}

export interface FeishuStatusDto {
  configured: boolean;
  enabled: boolean;
  /** Token 是否有效。Stream 模式下为 null */
  tokenValid: boolean | null;
  callbackUrl: string | null;
  /** 事件接收模式 */
  receiveMode: string | null;
  /** Stream 长连接是否已建立（仅 Stream 模式有意义） */
  streamConnected: boolean;
  /** Stream 最近断开原因 */
  streamError: string | null;
}

export interface FeishuReplyRequest {
  conversationId: number;
  content?: string;
  attachmentIds?: number[];
}

export interface FeishuReplyResult {
  sent: boolean;
  threadCreated: boolean;
  threadId?: number | null;
  errorMessage?: string | null;
}

// ===== DingTalk 钉钉集成 =====
export interface DingDingSettingsDto {
  appKey: string | null;
  hasAppSecret: boolean;
  agentId: number | null;
  robotCode: string | null;
  hasCallbackToken: boolean;
  hasEncodingAesKey: boolean;
  receiveMode: string;
  mailboxId: number | null;
  contactCollectionEnabled: boolean;
  autoReplyEnabled: boolean;
  autoReplyMessage: string | null;
  enabled: boolean;
  callbackPath: string;
}

export interface UpdateDingDingSettingsRequest {
  appKey?: string;
  appSecret?: string;
  agentId?: number;
  robotCode?: string;
  callbackToken?: string;
  encodingAesKey?: string;
  receiveMode?: string;
  mailboxId?: number;
  contactCollectionEnabled?: boolean;
  autoReplyEnabled?: boolean;
  autoReplyMessage?: string;
  enabled?: boolean;
}

export interface DingDingStatusDto {
  configured: boolean;
  enabled: boolean;
  apiReachable: boolean;
  callbackUrl: string | null;
  receiveMode: string | null;
  robotCode: string | null;
}

export interface DingDingReplyRequest {
  conversationId: number;
  content?: string;
  attachmentIds?: number[];
}

export interface DingDingReplyResult {
  sent: boolean;
  threadCreated: boolean;
  threadId: number | null;
  errorMessage: string | null;
}

// ===== Messenger (Facebook Messenger Platform) 集成 =====
export interface MessengerApiError {
  code: number;
  message: string;
  subcode: number;
  fbtraceId?: string | null;
  httpStatus: number;
  /** 后端计算属性：551/190/2635 ⇒ 不应再重试 */
  shouldStopRetrying?: boolean;
  /** 后端计算属性：613 ⇒ 触发限流退避 */
  isRateLimited?: boolean;
}

export interface MessengerSettingsDto {
  appId: string | null;
  pageId: string | null;
  pageUsername: string | null;
  apiVersion: string | null;
  mailboxId: number | null;
  textChunkChars: number;
  windowHours: number;
  /** auto / none / always */
  messageTagPolicy: string;
  messageTag: string;
  typingIndicator: boolean;
  markSeen: boolean;
  contactCollectionEnabled: boolean;
  autoReplyEnabled: boolean;
  autoReplyMessage: string | null;
  enabled: boolean;
  subscribedFields: string | null;
  callbackUrl: string | null;
  hasPageAccessToken: boolean;
  hasAppSecret: boolean;
  hasVerifyToken: boolean;
}

export interface UpdateMessengerSettingsRequest {
  appId?: string | null;
  appSecret?: string | null;
  pageId?: string | null;
  pageAccessToken?: string | null;
  verifyToken?: string | null;
  pageUsername?: string | null;
  apiVersion?: string | null;
  mailboxId?: number | null;
  textChunkChars?: number | null;
  windowHours?: number | null;
  messageTagPolicy?: 'auto' | 'none' | 'always' | null;
  typingIndicator?: boolean | null;
  markSeen?: boolean | null;
  contactCollectionEnabled?: boolean | null;
  autoReplyEnabled?: boolean | null;
  autoReplyMessage?: string | null;
  enabled?: boolean | null;
  subscribedFields?: string | null;
}

export interface MessengerStatusDto {
  configured: boolean;
  enabled: boolean;
  apiReachable: boolean;
  callbackUrl: string | null;
  pageId: string | null;
  apiVersion: string | null;
  /** ok / warning / expired / unknown */
  windowStatus: 'ok' | 'warning' | 'expired' | 'unknown';
  windowRemainingHours: number;
  lastCustomerMessageAt: string | null;
  tokenInvalidAt: string | null;
  lastError: MessengerApiError | null;
}

export interface MessengerReplyRequest {
  conversationId: number;
  content?: string | null;
  attachmentIds?: number[] | null;
  replyToMessageId?: string | null;
}

export interface MessengerReplyResult {
  sent: boolean;
  /** 消息已发出但会话线程写入失败（HTTP 207 语义） */
  threadCreated: boolean;
  threadId?: number | null;
  messageId?: string | null;
  chunkCount: number;
  messageTagApplied: boolean;
  errorMessage?: string | null;
}

export interface MessengerSenderActionRequest {
  conversationId: number;
  action: 'typing_on' | 'typing_off' | 'mark_seen';
}

export interface MessengerSubscribeRequest {
  fields?: string | null;
}

export interface MessengerSubscribeResult {
  success: boolean;
  fields?: string | null;
  errorMessage?: string | null;
}

export interface MessengerTestResult {
  success: boolean;
  pageId?: string | null;
  apiVersion?: string | null;
  error?: MessengerApiError | null;
  errorMessage?: string | null;
}

// ===== Telegram 集成 =====
export interface TelegramSettingsDto {
  enabled: boolean;
  /** webhook / polling */
  mode: string;
  apiBaseUrl: string;
  useLocalBotApiServer: boolean;
  callbackUrl: string | null;
  botUsername: string | null;
  acceptGroupMessages: boolean;
  deleteWebhookOnSwitch: boolean;
  maxWebhookConnections: number;
  pollingTimeout: number;
  pollingLimit: number;
  allowedUpdates: string[];
  mailboxId: number | null;
  autoReplyEnabled: boolean;
  autoReplyMessage: string | null;
  conversationMatchWindowHours: number;
  maxMessageChars: number;
  rateLimitGlobalPerSeconds: number;
  rateLimitPerChatPerSeconds: number;
  deepLinkPrefix: string | null;
  hasBotToken: boolean;
  hasWebhookSecretToken: boolean;
  hasLinkTokenHmacKey: boolean;
}

export interface UpdateTelegramSettingsRequest {
  enabled?: boolean | null;
  mode?: 'webhook' | 'polling' | null;
  botToken?: string | null;
  apiBaseUrl?: string | null;
  useLocalBotApiServer?: boolean | null;
  webhookUrl?: string | null;
  webhookSecretToken?: string | null;
  webhookIpAddress?: string | null;
  maxWebhookConnections?: number | null;
  allowedUpdates?: string[] | null;
  pollingTimeout?: number | null;
  pollingLimit?: number | null;
  deleteWebhookOnSwitch?: boolean | null;
  botUsername?: string | null;
  acceptGroupMessages?: boolean | null;
  mailboxId?: number | null;
  autoReplyEnabled?: boolean | null;
  autoReplyMessage?: string | null;
  conversationMatchWindowHours?: number | null;
  maxMessageChars?: number | null;
  rateLimitGlobalPerSeconds?: number | null;
  rateLimitPerChatPerSeconds?: number | null;
  linkTokenHmacKey?: string | null;
  linkTokenTtlMinutes?: number | null;
}

export interface TelegramStatusDto {
  configured: boolean;
  enabled: boolean;
  mode: string;
  apiReachable: boolean;
  botUsername: string | null;
  canReadAllGroupMessages: boolean | null;
  webhookRegistered: boolean;
  webhookUrl: string | null;
  pendingUpdateCount: number | null;
  lastErrorMessage: string | null;
  lastErrorTime: number | null;
  pollingRunning: boolean;
  pollingLastActivityAt: string | null;
  pollingLastErrorMessage: string | null;
  pollingOffset: number | null;
  unreachableConversationCount: number;
  callbackUrl: string | null;
}

export interface TelegramReplyRequest {
  conversationId: number;
  content?: string | null;
  attachmentIds?: number[] | null;
  quoteLastCustomerMessage?: boolean;
  /** 允许值 none / HTML / Markdown；undefined 或 'none' 表示纯文本（不发送 parse_mode） */
  parseMode?: string | null;
}

export interface TelegramReplyResult {
  sent: boolean;
  /** 消息已发出但会话线程写入失败（HTTP 207 语义） */
  threadCreated: boolean;
  threadId?: number | null;
  messageId?: string | null;
  errorMessage?: string | null;
  markedUnreachable: boolean;
  retryAfterSeconds?: number | null;
  partsSent: number;
}

export interface TelegramWebhookSetupRequest {
  webhookUrl?: string | null;
  dropPendingUpdates?: boolean;
}

export interface TelegramUnreachableStateRequest {
  conversationId: number;
}

export interface TelegramTestResult {
  success: boolean;
  botId?: number | null;
  botUsername?: string | null;
  errorMessage?: string | null;
}

// ===== LINE 集成 =====
export interface LineApiError {
  httpStatus: number;
  message: string;
  details: string[];
  /** 后端计算属性（401 ⇒ token 失效） */
  isTokenInvalid?: boolean;
  /** 404 ⇒ 客户未添加机器人为好友 */
  isNotFriended?: boolean;
  /** 403 ⇒ 配额 / 权限 */
  isQuotaOrPermission?: boolean;
  /** 409 ⇒ 重复请求 */
  isDuplicate?: boolean;
  /** 429 ⇒ 限流 */
  isRateLimited?: boolean;
  /** 429/500/503 ⇒ 可重试 */
  isRetryable?: boolean;
}

export interface LineSettingsDto {
  channelId: string | null;
  apiBaseUrl: string | null;
  dataApiBaseUrl: string | null;
  attachmentPublicBaseUrl: string | null;
  attachmentPublicUrlTtlMinutes: number;
  richMenuId: string | null;
  webhookEnabled: boolean;
  markAsReadEnabled: boolean;
  loadingIndicatorEnabled: boolean;
  autoReplyEnabled: boolean;
  autoReplyMessage: string | null;
  profileEnrichEnabled: boolean;
  textChunkChars: number;
  maxMessagesPerRequest: number;
  replyTokenTtlSeconds: number;
  humanHandoverMuteMinutes: number;
  quotaWarnPercent: number;
  // 审计 #12：以下六项后端 LineOptions 一直存在但没暴露到 DTO，界面调不了
  /** 状态探测超时（秒），对应 [Range(1,60)] */
  statusCheckTimeoutSeconds: number;
  /** 出站可重试错误的重试次数（0 = 不重试），对应 [Range(0,5)] */
  retryMaxAttempts: number;
  /** 入站媒体落盘上限（字节），对应 [Range(1,209715200)] */
  maxInboundMediaBytes: number;
  /** 出站附件直链大小上限（字节），对应 [Range(1,209715200)] */
  maxOutboundAttachmentBytes: number;
  /** 出站按图片发送的扩展名列表（逗号分隔，小写） */
  outboundImageExtensions: string | null;
  mailboxId: number | null;
  enabled: boolean;
  webhookUrl: string | null;
  hasChannelAccessToken: boolean;
  hasChannelSecret: boolean;
  channelAccessTokenKeyId: string | null;
}

export interface UpdateLineSettingsRequest {
  channelId?: string | null;
  channelSecret?: string | null;
  channelAccessToken?: string | null;
  apiBaseUrl?: string | null;
  dataApiBaseUrl?: string | null;
  attachmentPublicBaseUrl?: string | null;
  attachmentPublicUrlTtlMinutes?: number | null;
  richMenuId?: string | null;
  webhookEnabled?: boolean | null;
  markAsReadEnabled?: boolean | null;
  loadingIndicatorEnabled?: boolean | null;
  profileEnrichEnabled?: boolean | null;
  autoReplyEnabled?: boolean | null;
  autoReplyMessage?: string | null;
  textChunkChars?: number | null;
  maxMessagesPerRequest?: number | null;
  replyTokenTtlSeconds?: number | null;
  humanHandoverMuteMinutes?: number | null;
  quotaWarnPercent?: number | null;
  // 审计 #12：与后端 UpdateLineSettingsRequest 新增的六项一一对应（不传 = 不改）
  statusCheckTimeoutSeconds?: number | null;
  retryMaxAttempts?: number | null;
  maxInboundMediaBytes?: number | null;
  maxOutboundAttachmentBytes?: number | null;
  outboundImageExtensions?: string | null;
  /** 令牌 key_id（非密钥，仅展示/轮换核对用）；传空串 = 清除 */
  channelAccessTokenKeyId?: string | null;
  mailboxId?: number | null;
  enabled?: boolean | null;
}

export interface LineStatusDto {
  configured: boolean;
  enabled: boolean;
  webhookEnabled: boolean;
  apiReachable: boolean;
  tokenValid: boolean;
  tokenScope: string | null;
  tokenExpiresAt: string | null;
  tokenInvalidAt: string | null;
  webhookUrl: string | null;
  webhookEndpointOnLine: string | null;
  webhookDeliveryOnLine: boolean | null;
  /** -1 ⇒ 未知 */
  quotaTotal: number;
  quotaConsumed: number;
  /** unknown / none / limited / unlimited */
  quotaType: string;
  quotaUsedPercent: number;
  quotaWarning: boolean;
  lastError: LineApiError | null;
  replyModeHint: string;
}

export interface LineReplyRequest {
  conversationId: number;
  content?: string | null;
  attachmentIds?: number[] | null;
  preferReplyToken?: boolean;
}

export interface LineReplyResult {
  sent: boolean;
  /** 消息已发出但会话线程写入失败（HTTP 207 语义） */
  threadCreated: boolean;
  threadId?: number | null;
  /** reply / push */
  via: string;
  chunkCount: number;
  /** 本次是否触发 replyToken → push 回落 */
  fallbackFromReplyToken: boolean;
  /** 目标不可达（未添加好友 / 已屏蔽 / 配额）⇒ 会话已被标记为不可达 */
  unreachable: boolean;
  mediaErrors: string[];
  errorMessage?: string | null;
}

export interface LineSendResult {
  success: boolean;
  /** reply / push */
  via: string;
  externalMessageId?: string | null;
  chunkCount: number;
  errorMessage?: string | null;
  unreachable: boolean;
  error?: LineApiError | null;
}

export interface LineTakeoverRequest {
  conversationId: number;
  minutes?: number | null;
}

export interface LinePushTestRequest {
  userId?: string | null;
  conversationId?: number | null;
  text?: string;
}

export interface LineWebhookCheckRequest {
  webhookUrl?: string | null;
}

export interface LineWebhookCheckResult {
  success: boolean;
  detail: string;
  webhookUrl?: string | null;
  errorMessage?: string | null;
}

// ===== iMessage / Apple Messages 集成 =====
export interface IMessageSettingsDto {
  /** bridge / amb_msp / amb_apple_direct */
  mode: string;
  modeActive: boolean;
  experimentalAppleDirect: boolean;
  enabled: boolean;
  configured: boolean;
  baseUrl: string | null;
  authStyle: string;
  hasPassword: boolean;
  webhookPath: string;
  webhookBaseUrl: string | null;
  sendMethod: string;
  allowSmsFallback: boolean;
  allowList: string | null;
  rateLimitPerMinute: number;
  verifyTls: boolean;
  healthEndpoint: string;
  minServerVersion: string;
  readHistoryOnStart: boolean;
  matchWindowHours: number;
  mailboxId: number;
  autoReplyEnabled: boolean;
  autoReplyMessage: string | null;
  callbackUrl: string | null;
  mspConnector: string | null;
  apiBaseUrl: string | null;
  hasApiKey: boolean;
  hasWebhookSecret: boolean;
  businessIdentifier: string | null;
  fallbackChannel: string;
  mspId: string | null;
  hasMspSharedSecret: boolean;
  hasBridgeWebhookSecret: boolean;
  tokenTtlSeconds: number;
  gatewayBaseUrl: string;
  businessId: string | null;
  inboundPath: string;
  attachmentKeySize: number;
  maxBodyBytes: number;
}

export interface UpdateIMessageSettingsRequest {
  mode?: 'bridge' | 'amb_msp' | 'amb_apple_direct' | null;
  enabled?: boolean | null;
  baseUrl?: string | null;
  authStyle?: string | null;
  password?: string | null;
  webhookPath?: string | null;
  webhookBaseUrl?: string | null;
  sendMethod?: string | null;
  allowSmsFallback?: boolean | null;
  allowList?: string | null;
  rateLimitPerMinute?: number | null;
  verifyTls?: boolean | null;
  healthEndpoint?: string | null;
  minServerVersion?: string | null;
  readHistoryOnStart?: boolean | null;
  matchWindowHours?: number | null;
  mailboxId?: number | null;
  autoReplyEnabled?: boolean | null;
  autoReplyMessage?: string | null;
  mspConnector?: string | null;
  apiBaseUrl?: string | null;
  apiKey?: string | null;
  webhookSecret?: string | null;
  businessIdentifier?: string | null;
  fallbackChannel?: string | null;
  mspId?: string | null;
  mspSharedSecret?: string | null;
  /** BlueBubbles 入站共享密钥；留空即不校验（仅告警日志） */
  bridgeWebhookSecret?: string | null;
  tokenTtlSeconds?: number | null;
  gatewayBaseUrl?: string | null;
  businessId?: string | null;
  inboundPath?: string | null;
  attachmentKeySize?: number | null;
  maxBodyBytes?: number | null;
  experimentalAppleDirect?: boolean | null;
}

export interface IMessageStatusDto {
  configured: boolean;
  enabled: boolean;
  mode: string;
  appleDirectAllowed: boolean;
  apiReachable: boolean;
  callbackUrl: string | null;
  /** ok / expired / unknown（后端无 warning 档） */
  windowStatus: 'ok' | 'expired' | 'unknown';
  hoursSinceCustomerMessage: number;
  lastCustomerMessageAt: string | null;
  serverVersion: string | null;
  registeredWebhookUrl: string | null;
}

export interface IMessageReplyRequest {
  conversationId: number;
  content?: string | null;
  attachmentIds?: number[] | null;
}

export interface IMessageReplyResult {
  sent: boolean;
  /** 消息已发出但会话线程写入失败（HTTP 207 语义） */
  threadCreated: boolean;
  threadId?: number | null;
  errorMessage?: string | null;
}

export interface IMessageTestRequest {
  testHandle?: string | null;
}

export interface IMessageTestResult {
  success: boolean;
  messageId?: string | null;
  errorMessage?: string | null;
  serverVersion?: string | null;
}
