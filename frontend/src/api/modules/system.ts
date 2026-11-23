import client from '../client';
import type {
  AuditLogListResponse,
  AuditLogResponse,
  AuditLogStatsResponse,
  AuditLogIntegrityResult,
  AuditLogRelatedResponse,
  AuditIntegrityReport,
  SystemOptionResponse,
  CreateSystemOptionRequest,
  UpdateSystemOptionRequest,
  SupportedLanguagesResponse,
  TranslateResponse,
  ModuleDto,
  ModuleOverviewResponse,
  CreateModuleRequest,
  UpdateModuleRequest,
  ModuleQuery,
  WeChatSettingsDto,
  UpdateWeChatSettingsRequest,
  WeChatStatusDto,
  WeChatReplyRequest,
  WeChatReplyResult,
  WeChatTemplateRequest,
  WeChatTemplateResult,
  WeChatSubscribeRequest,
  WeChatMenuRequest,
  WeChatMenuResult,
  WhatsAppSettingsDto,
  WhatsAppStatusDto,
  WhatsAppReplyResult,
  WhatsAppTemplateResult,
  WhatsAppTestResult,
  WhatsAppReplyRequest,
  WhatsAppTemplateRequest,
  UpdateWhatsAppSettingsRequest,
  FeishuSettingsDto,
  UpdateFeishuSettingsRequest,
  FeishuStatusDto,
  FeishuReplyRequest,
  FeishuReplyResult,
  DingDingSettingsDto,
  UpdateDingDingSettingsRequest,
  DingDingStatusDto,
  DingDingReplyRequest,
  DingDingReplyResult,
  MessengerSettingsDto,
  UpdateMessengerSettingsRequest,
  MessengerStatusDto,
  MessengerReplyRequest,
  MessengerReplyResult,
  MessengerSenderActionRequest,
  MessengerSubscribeRequest,
  MessengerSubscribeResult,
  MessengerTestResult,
  TelegramSettingsDto,
  UpdateTelegramSettingsRequest,
  TelegramStatusDto,
  TelegramReplyRequest,
  TelegramReplyResult,
  TelegramWebhookSetupRequest,
  TelegramUnreachableStateRequest,
  TelegramTestResult,
  LineSettingsDto,
  UpdateLineSettingsRequest,
  LineStatusDto,
  LineReplyRequest,
  LineReplyResult,
  LineSendResult,
  LineTakeoverRequest,
  LinePushTestRequest,
  LineWebhookCheckRequest,
  LineWebhookCheckResult,
  IMessageSettingsDto,
  UpdateIMessageSettingsRequest,
  IMessageStatusDto,
  IMessageReplyRequest,
  IMessageReplyResult,
  IMessageTestRequest,
  IMessageTestResult,
} from '../types';

/** 审计日志查询过滤参数 */
export interface AuditLogQuery {
  page?: number;
  pageSize?: number;
  action?: string;
  entityType?: string;
  userId?: number;
  entityId?: number;
  ipAddress?: string;
  keyword?: string;
  /** ISO 8601 起始时间（UTC） */
  startDate?: string;
  /** ISO 8601 结束时间（UTC） */
  endDate?: string;
}

// ====== 审计日志 API ======
export const auditLogApi = {
  list: (params?: AuditLogQuery) =>
    client
      .get<AuditLogListResponse>('/api/auditlogs', { params })
      .then((r) => r.data),
  stats: (params?: Omit<AuditLogQuery, 'page' | 'pageSize'>) =>
    client
      .get<AuditLogStatsResponse>('/api/auditlogs/stats', { params })
      .then((r) => r.data),
  exportCsv: (params?: Omit<AuditLogQuery, 'page' | 'pageSize'> & { maxRows?: number }) =>
    client
      .get<Blob>('/api/auditlogs/export', {
        params,
        responseType: 'blob',
      })
      .then((r) => r.data),
  getById: (id: number) =>
    client
      .get<AuditLogResponse>(`/api/auditlogs/${id}`)
      .then((r) => r.data),
  verifySingle: (id: number) =>
    client
      .get<AuditLogIntegrityResult>(`/api/auditlogs/${id}/verify`)
      .then((r) => r.data),
  getRelated: (id: number, limit = 10) =>
    client
      .get<AuditLogRelatedResponse>(`/api/auditlogs/${id}/related`, { params: { limit } })
      .then((r) => r.data),
  verifyIntegrity: (page = 1, pageSize = 100) =>
    client
      .get<AuditIntegrityReport>('/api/auditlogs/verify-integrity', { params: { page, pageSize } })
      .then((r) => r.data),
  getByUser: (userId: number, page = 1, pageSize = 20) =>
    client
      .get<AuditLogListResponse>(`/api/auditlogs/user/${userId}`, { params: { page, pageSize } })
      .then((r) => r.data),
  getByEntity: (entityType: string, entityId: number, page = 1, pageSize = 20) =>
    client
      .get<AuditLogListResponse>(`/api/auditlogs/${entityType}/${entityId}`, { params: { page, pageSize } })
      .then((r) => r.data),
};

// ====== 系统配置 API ======
export const systemOptionApi = {
  list: () =>
    client.get<SystemOptionResponse[]>('/api/systemoptions').then((r) => r.data ?? []),
  getByCategory: (category: string) =>
    client.get<SystemOptionResponse[]>(`/api/systemoptions/category/${category}`).then((r) => r.data ?? []),
  getByName: (name: string) =>
    client.get<SystemOptionResponse>(`/api/systemoptions/${name}`).then((r) => r.data),
  create: (data: CreateSystemOptionRequest) =>
    client.post<SystemOptionResponse>('/api/systemoptions', data).then((r) => r.data),
  update: (name: string, data: UpdateSystemOptionRequest) =>
    client.put<SystemOptionResponse>(`/api/systemoptions/${name}`, data).then((r) => r.data),
  delete: (name: string) => client.delete<boolean>(`/api/systemoptions/${name}`).then((r) => r.data),
  getDictionary: () =>
    client.get<Record<string, string>>('/api/systemoptions/dictionary').then((r) => r.data ?? {}),
  batchUpdate: (options: Record<string, string>) =>
    client.put<boolean>('/api/systemoptions/batch', { options }).then((r) => r.data),
};

// ====== 系统工具 API ======
export const systemToolsApi = {
  clearCache: () => client.post<{ success?: boolean; message?: string }>('/api/system/tools/clear-cache').then((r) => r.data),
  optimizeDatabase: () => client.post<{ success?: boolean; message?: string }>('/api/system/tools/optimize-db').then((r) => r.data),
  logoutAllUsers: () => client.post<{ success?: boolean; message?: string }>('/api/system/tools/logout-users').then((r) => r.data),
  getLogs: (lines = 100) => client.get<{ logs: string } | string>('/api/system/tools/logs', { params: { lines } }).then((r) => r.data),
  // 清空系统日志文件（删除 log-*.log 中的记录，截断为 0 字节）
  clearLogs: () =>
    client
      .post<{ success: boolean; cleared: number; freedBytes: number; failed: number; failedFiles?: string[]; message: string }>(
        '/api/system/tools/logs/clear',
      )
      .then((r) => r.data),
};

// ====== 系统信息 API ======
export interface SystemInfo {
  version: string;
  platform: string;
  oSVersion: string;
  framework: string;
  environment: string;
  machineName: string;
  processorCount: number;
  workingSet: number;
  startupTime: string;
  features: string[];
}

export const systemApi = {
  getInfo: () => client.get<SystemInfo>('/api/system/info').then((r) => r.data),
};

// ====== 本地化 API ======
export const localizationApi = {
  getLanguages: () =>
    client.get<SupportedLanguagesResponse>('/api/localization/languages').then((r) => r.data),
  translate: (key: string) =>
    client.get<TranslateResponse>(`/api/localization/translate?key=${encodeURIComponent(key)}`).then((r) => r.data),
  translateBatch: (keys: string[]) =>
    client.post<{ language: string; translations: Record<string, string> }>('/api/localization/translate-batch', keys).then((r) => r.data),
  getEnums: () => client.get<Record<string, Array<{ value: number; label: string }>>>('/api/localization/enums').then((r) => r.data),
  getTranslations: (lang: string) =>
    client.get<{ language: string; translations: Record<string, string> }>(`/api/localization/translations/${encodeURIComponent(lang)}`).then((r) => r.data),
};

// ====== 模块 API ======
export const moduleApi = {
  list: (params?: ModuleQuery) =>
    client
      .get<{ data: ModuleDto[]; totalItems: number; totalPages: number }>('/api/modules', { params })
      .then((r) => r.data),
  get: (name: string) =>
    client.get<ModuleDto>(`/api/modules/${name}`).then((r) => r.data),
  overview: () =>
    client.get<ModuleOverviewResponse>('/api/modules/overview').then((r) => r.data),
  all: () =>
    client.get<ModuleDto[]>('/api/modules/all').then((r) => r.data ?? []),
  byCategory: (category: string) =>
    client.get<ModuleDto[]>(`/api/modules/category/${category}`).then((r) => r.data ?? []),
  active: () =>
    client.get<ModuleDto[]>('/api/modules/active').then((r) => r.data ?? []),
  create: (data: CreateModuleRequest) =>
    client.post<ModuleDto>('/api/modules', data).then((r) => r.data),
  update: (name: string, data: UpdateModuleRequest) =>
    client.put<ModuleDto>(`/api/modules/${name}`, data).then((r) => r.data),
  remove: (name: string) =>
    client.delete<boolean>(`/api/modules/${name}`).then((r) => r.data),
  toggleActive: (name: string) =>
    client.post<ModuleDto>(`/api/modules/${name}/toggle`).then((r) => r.data),
};

// ====== 微信服务号接入 API ======
export const weChatApi = {
  getSettings: () =>
    client.get<WeChatSettingsDto>('/api/wechat/settings').then((r) => r.data),
  updateSettings: (data: UpdateWeChatSettingsRequest) =>
    client.put<WeChatSettingsDto>('/api/wechat/settings', data).then((r) => r.data),
  getStatus: () =>
    client.get<WeChatStatusDto>('/api/wechat/status').then((r) => r.data),
  // 客服消息回复
  reply: (data: WeChatReplyRequest) =>
    client.post<WeChatReplyResult>('/api/wechat/reply', data).then((r) => r.data),
  // 模板消息（旧版）
  sendTemplate: (data: WeChatTemplateRequest) =>
    client.post<WeChatTemplateResult>('/api/wechat/template/send', data).then((r) => r.data),
  // 订阅通知（新版）
  sendSubscribe: (data: WeChatSubscribeRequest) =>
    client.post<WeChatTemplateResult>('/api/wechat/subscribe/send', data).then((r) => r.data),
  // 自定义菜单
  createMenu: (data: WeChatMenuRequest) =>
    client.post<WeChatMenuResult>('/api/wechat/menu', data).then((r) => r.data),
  getMenu: () =>
    client.get<{ menu: string | null }>('/api/wechat/menu').then((r) => r.data),
  deleteMenu: () =>
    client.delete<WeChatMenuResult>('/api/wechat/menu').then((r) => r.data),
};

// ===== WhatsApp API =====
export const whatsAppApi = {
  getSettings: () => client.get<WhatsAppSettingsDto>('/api/whatsapp/settings').then((r) => r.data),
  updateSettings: (data: UpdateWhatsAppSettingsRequest) =>
    client.put<WhatsAppSettingsDto>('/api/whatsapp/settings', data).then((r) => r.data),
  getStatus: () => client.get<WhatsAppStatusDto>('/api/whatsapp/status').then((r) => r.data),
  reply: (data: WhatsAppReplyRequest) =>
    client.post<WhatsAppReplyResult>('/api/whatsapp/reply', data).then((r) => r.data),
  sendTemplate: (data: WhatsAppTemplateRequest) =>
    client.post<WhatsAppTemplateResult>('/api/whatsapp/template/send', data).then((r) => r.data),
  testConnection: () =>
    client.post<WhatsAppTestResult>('/api/whatsapp/test').then((r) => r.data),
};

// ===== Feishu API =====
export const feishuApi = {
  getSettings: () => client.get<FeishuSettingsDto>('/api/feishu/settings').then((r) => r.data),
  updateSettings: (data: UpdateFeishuSettingsRequest) =>
    client.put<FeishuSettingsDto>('/api/feishu/settings', data).then((r) => r.data),
  getStatus: () => client.get<FeishuStatusDto>('/api/feishu/status').then((r) => r.data),
  reply: (data: FeishuReplyRequest) =>
    client.post<FeishuReplyResult>('/api/feishu/reply', data).then((r) => r.data),
};

// ===== DingTalk API =====
export const dingdingApi = {
  getSettings: () => client.get<DingDingSettingsDto>('/api/dingding/settings').then((r) => r.data),
  updateSettings: (data: UpdateDingDingSettingsRequest) =>
    client.put<DingDingSettingsDto>('/api/dingding/settings', data).then((r) => r.data),
  getStatus: () => client.get<DingDingStatusDto>('/api/dingding/status').then((r) => r.data),
  reply: (data: DingDingReplyRequest) =>
    client.post<DingDingReplyResult>('/api/dingding/reply', data).then((r) => r.data),
};

// ===== Messenger API =====
export const messengerApi = {
  getSettings: () => client.get<MessengerSettingsDto>('/api/messenger/settings').then((r) => r.data),
  updateSettings: (data: UpdateMessengerSettingsRequest) =>
    client.put<MessengerSettingsDto>('/api/messenger/settings', data).then((r) => r.data),
  getStatus: () => client.get<MessengerStatusDto>('/api/messenger/status').then((r) => r.data),
  reply: (data: MessengerReplyRequest) =>
    client.post<MessengerReplyResult>('/api/messenger/reply', data).then((r) => r.data),
  /** 打字指示器 / 已读标记（Send API sender action） */
  senderAction: (data: MessengerSenderActionRequest) =>
    client.post<boolean>('/api/messenger/sender-action', data).then((r) => r.data),
  /** 向 Page 订阅 webhook 字段（需 pages_manage_metadata） */
  subscribe: (data?: MessengerSubscribeRequest) =>
    client.post<MessengerSubscribeResult>('/api/messenger/subscribe', data ?? {}).then((r) => r.data),
  testConnection: () =>
    client.post<MessengerTestResult>('/api/messenger/test').then((r) => r.data),
};

// ===== Telegram API =====
export const telegramApi = {
  getSettings: () => client.get<TelegramSettingsDto>('/api/telegram/settings').then((r) => r.data),
  updateSettings: (data: UpdateTelegramSettingsRequest) =>
    client.put<TelegramSettingsDto>('/api/telegram/settings', data).then((r) => r.data),
  getStatus: () => client.get<TelegramStatusDto>('/api/telegram/status').then((r) => r.data),
  reply: (data: TelegramReplyRequest) =>
    client.post<TelegramReplyResult>('/api/telegram/reply', data).then((r) => r.data),
  /** 把本部署回调地址注册到 Bot API（webhook 模式） */
  setupWebhook: (data?: TelegramWebhookSetupRequest) =>
    client.post<TelegramTestResult>('/api/telegram/webhook/setup', data ?? {}).then((r) => r.data),
  deleteWebhook: () =>
    client.post<TelegramTestResult>('/api/telegram/webhook/delete').then((r) => r.data),
  /** 清除会话的「不可达」标记（客户重新可达后放行出站消息） */
  clearUnreachable: (data: TelegramUnreachableStateRequest) =>
    client.post<TelegramReplyResult>('/api/telegram/unreachable/clear', data).then((r) => r.data),
  testConnection: () =>
    client.post<TelegramTestResult>('/api/telegram/test').then((r) => r.data),
};

// ===== LINE API =====
export const lineApi = {
  getSettings: () => client.get<LineSettingsDto>('/api/line/settings').then((r) => r.data),
  updateSettings: (data: UpdateLineSettingsRequest) =>
    client.put<LineSettingsDto>('/api/line/settings', data).then((r) => r.data),
  getStatus: () => client.get<LineStatusDto>('/api/line/status').then((r) => r.data),
  reply: (data: LineReplyRequest) =>
    client.post<LineReplyResult>('/api/line/reply', data).then((r) => r.data),
  /** 人工接管：静音机器人自动回复 minutes 分钟（缺省取设置里的 humanHandoverMuteMinutes） */
  takeover: (data: LineTakeoverRequest) =>
    client.post<boolean>('/api/line/takeover', data).then((r) => r.data),
  releaseTakeover: (conversationId: number) =>
    client.delete<boolean>(`/api/line/takeover/${conversationId}`).then((r) => r.data),
  /** push 探活（POST /api/line/test；消耗月度配额，空 body ⇒ 后端取默认文本/最近会话） */
  testConnection: () =>
    client.post<LineSendResult>('/api/line/test', {}).then((r) => r.data),
  pushTest: (data: LinePushTestRequest) =>
    client.post<LineSendResult>('/api/line/test', data).then((r) => r.data),
  /** 触发 LINE 官方 webhook/test 校验回调地址连通性（60 次/小时限流） */
  checkWebhook: (data?: LineWebhookCheckRequest) =>
    client.post<LineWebhookCheckResult>('/api/line/webhook-check', data ?? {}).then((r) => r.data),
  /** 把本部署回调地址写回 LINE 侧 */
  applyWebhookEndpoint: (enabled = true) =>
    client
      .post<LineWebhookCheckResult>(`/api/line/webhook-endpoint?enabled=${enabled}`)
      .then((r) => r.data),
};

// ===== iMessage / Apple Messages API =====
export const imessageApi = {
  getSettings: () => client.get<IMessageSettingsDto>('/api/imessage/settings').then((r) => r.data),
  updateSettings: (data: UpdateIMessageSettingsRequest) =>
    client.put<IMessageSettingsDto>('/api/imessage/settings', data).then((r) => r.data),
  getStatus: () => client.get<IMessageStatusDto>('/api/imessage/status').then((r) => r.data),
  reply: (data: IMessageReplyRequest) =>
    client.post<IMessageReplyResult>('/api/imessage/reply', data).then((r) => r.data),
  testConnection: (data?: IMessageTestRequest) =>
    client.post<IMessageTestResult>('/api/imessage/test', data ?? {}).then((r) => r.data),
  /** 把本部署回调地址注册到 BlueBubbles / MSP 侧 */
  registerWebhook: () =>
    client.post<IMessageTestResult>('/api/imessage/webhook-register').then((r) => r.data),
};
