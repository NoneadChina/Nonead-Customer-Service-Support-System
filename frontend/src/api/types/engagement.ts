// ====== 日历模块 ======
export interface SubTask {
  id: string;
  title: string;
  done: boolean;
}

export interface AddedRecurrence {
  date: string;
  startAt: string;
  endAt?: string;
}

export interface RecurrenceExclusions {
  excluded: string[];
  added: AddedRecurrence[];
}

export interface CalendarEventDto {
  id: number;
  title: string;
  description: string | null;
  eventType: string;
  eventTypeLabel: string;
  startAt: string;
  endAt: string | null;
  isAllDay: boolean;
  conversationId: number | null;
  conversationSubject: string | null;
  mailboxId: number | null;
  mailboxName: string | null;
  userId: number | null;
  userName: string | null;
  status: string;
  statusLabel: string;
  color: string | null;
  deletedAt?: string | null;
  createdAt: string;
  updatedAt: string;
  createdBy: number;
  /** 创建者用户姓名（始终为创建者，不随修改者变化；SuperAdmin 修改他人日程后仍显示原创建者） */
  createdByName?: string | null;
  priority?: number | null;
  progress?: number | null;
  location?: string | null;
  meetingUrl?: string | null;
  attendees?: number[];
  subTasks?: SubTask[];
  relatedEventId?: number | null;
  recurrenceExclusions?: RecurrenceExclusions | null;
  isRecurring?: boolean;
  recurrenceType?: number | null;
  recurrenceInterval?: number | null;
  recurrenceEndDate?: string | null;
  recurrenceCount?: number | null;
  recurrenceParentId?: number | null;
  recurrenceOriginalStart?: string | null;
  reminderMinutes?: number | null;
  isReminderSent?: boolean;
  reminderEmails?: string | null;
  /** ====== 事件类型专属字段（2026-08-17 持久化） ====== */
  /** 主持人用户 ID 数组（会议类型专属，前端多选下拉菜单） */
  hostUserIds?: number[];
  /** 议程 ID 数组（会议类型专属，关联外部议程条目） */
  agendaIds?: number[];
  /** 任务负责人用户 ID（任务类型专属，区别于 userId 事件归属者） */
  taskOwnerId?: number | null;
  /** 任务负责人姓名（任务类型专属，展示层使用；为空时表示未设置或非任务类型，前端兜底显示 userName 或 "-"） */
  taskOwnerName?: string | null;
  /** 任务截止时间（ISO 8601，任务类型专属，区别于 endAt 结束时间） */
  dueAt?: string | null;
  /** 提醒方式：popup / email / sms（提醒类型专属，配合 reminderMinutes 使用） */
  reminderType?: string | null;
  /** 每周哪几天（0=周日..6=周六，重复日程面板专属） */
  weekdaysSelected?: number[];
  /** RRULE 原始字符串（重复日程面板专属，区别于 recurrenceType 枚举） */
  recurrenceRule?: string | null;
  /** 乐观锁版本令牌（RowVersion base64）；更新请求用作 If-Match，接收推送/列表时用于丢弃旧版本 */
  rowVersion?: string | null;
}

export interface CalendarPagedResponse {
  data: CalendarEventDto[];
  currentPage: number;
  totalPages: number;
  totalItems: number;
  pageSize?: number;
}

export interface CalendarOverviewResponse {
  stats: Record<string, number>;
  upcomingEvents: CalendarEventDto[];
  eventTypeOptions: Record<string, string>;
  statusOptions: Record<string, string>;
}

export interface CalendarEventQuery {
  startDate?: string | null;
  endDate?: string | null;
  mailboxId?: number | null;
  userId?: number | null;
  eventType?: string | null;
  status?: string | null;
  keyword?: string | null;
  page?: number;
  pageSize?: number;
  expandRecurring?: boolean;
  sortBy?: string | null;
  sortDir?: string | null;
}

export interface DuplicateCalendarEventRequest {
  startAt?: string | null;
  endAt?: string | null;
  title?: string | null;
}

export interface QuickUpdateCalendarEventRequest {
  status?: string | null;
  priority?: number | null;
  progress?: number | null;
  location?: string | null;
  meetingUrl?: string | null;
  title?: string | null;
  description?: string | null;
  color?: string | null;
  reminderMinutes?: number | null;
  /** 乐观锁匹配令牌（rowVersion），后端返回 409 + latest 快照 */
  ifMatch?: string | null;
}

export interface CreateCalendarEventRequest {
  title: string;
  description?: string | null;
  eventType: string;
  startAt: string;
  endAt?: string | null;
  isAllDay: boolean;
  conversationId?: number | null;
  mailboxId?: number | null;
  userId?: number | null;
  color?: string | null;
  isRecurring?: boolean;
  recurrenceType?: number | null;
  recurrenceInterval?: number | null;
  recurrenceEndDate?: string | null;
  recurrenceCount?: number | null;
  reminderMinutes?: number | null;
  reminderEmails?: string | null;
  priority?: number | null;
  progress?: number | null;
  location?: string | null;
  meetingUrl?: string | null;
  attendees?: number[];
  subTasks?: SubTask[];
  relatedEventId?: number | null;
  recurrenceExclusions?: RecurrenceExclusions | null;
  /** ====== 事件类型专属字段（2026-08-17 持久化） ====== */
  /** 主持人用户 ID 数组（会议类型专属） */
  hostUserIds?: number[];
  /** 议程 ID 数组（会议类型专属） */
  agendaIds?: number[];
  /** 任务负责人用户 ID（任务类型专属） */
  taskOwnerId?: number | null;
  /** 任务截止时间（ISO 8601，任务类型专属） */
  dueAt?: string | null;
  /** 提醒方式：popup / email / sms（提醒类型专属） */
  reminderType?: string | null;
  /** 每周哪几天（0=周日..6=周六，重复日程面板专属） */
  weekdaysSelected?: number[];
  /** RRULE 原始字符串（重复日程面板专属） */
  recurrenceRule?: string | null;
}

export interface UpdateCalendarEventRequest extends CreateCalendarEventRequest {
  id: number;
  status: string;
  updateMode?: string; // 'single' | 'all' | 'following'
  /** 乐观锁匹配令牌（rowVersion），后端返回 409 + latest 快照 */
  ifMatch?: string | null;
}

export interface CalendarImportResult {
  imported: number;
  skipped: number;
  failed?: number;
}

export type CalendarTrashCountResponse = { count: number } | number;

export interface CalendarPurgeResult {
  purged?: number;
  retentionDays?: number;
  count?: number;
  cleared?: number;
}

// P1-3: 日历事件模板
export interface CalendarEventTemplateDto {
  id: number;
  name: string;
  title: string;
  eventType: string;
  durationMin: number;
  isAllDay: boolean;
  description: string | null;
  location: string | null;
  sortOrder: number;
  isShared: boolean;
  mailboxId: number | null;
  mailboxName: string | null;
  createdByUserId: number | null;
  createdByUserName: string | null;
  createdAt: string;
  updatedAt: string;
  deletedAt: string | null;
}

export interface CreateCalendarEventTemplateRequest {
  name: string;
  title: string;
  eventType: string;
  durationMin?: number;
  isAllDay?: boolean;
  description?: string | null;
  location?: string | null;
  sortOrder?: number;
  isShared?: boolean;
  mailboxId?: number | null;
}

export type UpdateCalendarEventTemplateRequest = CreateCalendarEventTemplateRequest;

export interface CalendarEventTemplateListResponse {
  items: CalendarEventTemplateDto[];
  total: number;
}

// P1-2: 日历批量操作
export interface CalendarBatchOperationRequest {
  ids: number[];
  op: 'delete' | 'restore' | 'setStatus' | 'setEventType' | 'moveOffset' | string;
  status?: string;
  eventType?: string;
  daysOffset?: number;
  minutesOffset?: number;
}

export interface CalendarBatchFailedItem {
  id: number;
  reason: string;
  /** 兼容后端部分场景返回的 message 字段（优先级同 reason） */
  message?: string;
}

export interface CalendarBatchOperationResponse {
  successCount: number;
  failedCount: number;
  failures?: CalendarBatchFailedItem[];
}

// ====== Outlook 双向同步 ======
export interface OutlookSyncSettingDto {
  id: number;
  userId: number;
  outlookEmail: string | null;
  outlookUserId: string | null;
  isConnected: boolean;
  isTokenExpired: boolean;
  syncEnabled: boolean;
  syncDirection: 'both' | 'to_outlook' | 'from_outlook' | string;
  lastSyncAt: string | null;
  lastSyncStatus: string | null;
  lastSyncError: string | null;
  tokenExpiresAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface OutlookSyncStatusDto {
  isConfigured: boolean;
  isConnected: boolean;
  syncEnabled: boolean;
  syncDirection: string;
  outlookEmail: string | null;
  lastSyncAt: string | null;
  lastSyncStatus: string | null;
  lastSyncError: string | null;
  isSyncing: boolean;
}

export interface OutlookSyncResultDto {
  success: boolean;
  pushedToOutlook: number;
  pulledFromOutlook: number;
  updated: number;
  deleted: number;
  conflicts: number;
  skipped: number;
  errorMessage: string | null;
  durationMs: number;
  syncedAt: string;
}

export interface UpdateOutlookSyncSettingRequest {
  syncEnabled: boolean;
  syncDirection: 'both' | 'to_outlook' | 'from_outlook' | string;
}

// T000099: Outlook 同步 Azure AD 凭据管理（脱敏响应，仅超管可访问）
export interface OutlookAdminSettingsDto {
  isConfigured: boolean;
  clientId: string | null;
  hasClientSecret: boolean;
  tenantId: string | null;
  redirectUri: string | null;
  scopes: string | null;
  source: 'database' | 'appsettings' | string;
  updatedAt: string | null;
}

// T000099: 更新 Outlook Azure AD 凭据请求（部分更新，仅非 null 字段写入）
// clientSecret 传 "CLEAR" 表示清空已配置的密钥
export interface UpdateOutlookAdminSettingsRequest {
  clientId?: string | null;
  clientSecret?: string | null;
  tenantId?: string | null;
  redirectUri?: string | null;
  scopes?: string | null;
}

// ====== 知识库模块 ======
export interface KnowledgeCategoryDto {
  id: number;
  name: string;
  description: string | null;
  parentId: number | null;
  parentName: string | null;
  mailboxId: number | null;
  mailboxName: string | null;
  sortOrder: number;
  icon: string | null;
  isActive: boolean;
  articleCount: number;
  createdAt: string;
  updatedAt: string;
  children: KnowledgeCategoryDto[];
}

export interface CreateKnowledgeCategoryRequest {
  name: string;
  description?: string | null;
  parentId?: number | null;
  mailboxId?: number | null;
  sortOrder: number;
  icon?: string | null;
  isActive: boolean;
}

export interface UpdateKnowledgeCategoryRequest extends CreateKnowledgeCategoryRequest {
  id: number;
}

export interface KnowledgeCategoryQuery {
  parentId?: number | null;
  mailboxId?: number | null;
  isActive?: boolean | null;
  keyword?: string | null;
}

export interface KnowledgeArticleAttachmentDto {
  id: number;
  articleId: number;
  fileName: string;
  url: string;
  mimeType: string;
  fileSize: number;
  extension: string;
  createdAt: string;
  isPdf: boolean;
  isImage: boolean;
  fileSizeDisplay: string;
}

export interface KnowledgeArticleDto {
  id: number;
  title: string;
  content: string;
  /** 内容格式：markdown / html */
  contentType: string;
  summary: string | null;
  categoryId: number | null;
  categoryName: string | null;
  authorId: number | null;
  authorName: string | null;
  tags: string | null;
  tagList: string[];
  /** 文章级附件列表（PDF/图片/文档） */
  attachments: KnowledgeArticleAttachmentDto[];
  status: string;
  statusLabel: string;
  viewCount: number;
  likeCount: number;
  /** 当前用户是否已点赞（用于展示点赞/取消点赞状态） */
  isLiked?: boolean;
  sortOrder: number;
  isPinned: boolean;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
  rowVersion?: string | null;
  /** 搜索结果中的高亮片段（来自 FTS5 snippet；若未命中 FTS 则为 null） */
  snippet?: string | null;
  /** FTS5 bm25 排序值（越小越相关；LIKE 回退时为 null） */
  rank?: number | null;
}

export interface CreateKnowledgeArticleRequest {
  title: string;
  content: string;
  /** 内容格式：markdown / html（默认 markdown） */
  contentType: string;
  summary?: string | null;
  categoryId?: number | null;
  authorId?: number | null;
  tags?: string | null;
  status: string;
  sortOrder: number;
  isPinned: boolean;
}

export interface UpdateKnowledgeArticleRequest extends CreateKnowledgeArticleRequest {
  id: number;
  /** 乐观锁匹配令牌（rowVersion），后端返回 409 + latest 快照 */
  ifMatch?: string | null;
}

export interface KnowledgeArticleQuery {
  categoryId?: number | null;
  status?: string | null;
  keyword?: string | null;
  authorId?: number | null;
  isPinned?: boolean | null;
  page: number;
  pageSize: number;
}

export interface KnowledgeOverviewResponse {
  stats: Record<string, number>;
  recentArticles: KnowledgeArticleDto[];
  popularArticles: KnowledgeArticleDto[];
  topCategories: KnowledgeCategoryDto[];
  statusOptions: Record<string, string>;
}

export interface KnowledgeSearchResult {
  data: KnowledgeArticleDto[];
  total: number;
  page: number;
  pageSize: number;
  /** 本次搜索是否命中 FTS5（true=使用虚拟表 snippet 片段，false=使用 LIKE 回退或无结果） */
  isFts?: boolean;
}

// ====== 在线聊天模块 ======

/** 在线聊天会话状态 */
export type ChatSessionStatus =
  | 'waiting'       // 等待分配
  | 'active'        // 进行中
  | 'awaiting_rating' // 客服已发起关闭，等待客户评分（两阶段关闭：阶段 1）
  | 'closed'        // 已真正关闭（两阶段关闭：阶段 2 / 客户主动关闭 / 超时自动关闭）
  | 'transferred';  // 已转工单

// 曾经这里有一份 `CHAT_SESSION_STATUS_LABEL`（会话状态 → 中文展示标签的"前端兜底"）。
// 2026-09-24 收官批核对：**全仓（含测试）零引用**，属死代码——界面上的状态标签一律由
// 后端 `statusLabel` 或各自模块的字典提供，故删除，不为不存在的界面建词条。

/** 是否为"终止态"（不可再发送消息 / 不可再转工单） */
export function isTerminalChatStatus(status: ChatSessionStatus | string): boolean {
  return status === 'closed' || status === 'transferred';
}

/** 是否处于"等待评分"只读态：客服已无法再发消息，但会话尚未真关闭 */
export function isAwaitingRatingStatus(status: ChatSessionStatus | string): boolean {
  return status === 'awaiting_rating';
}

export interface ChatSessionDto {
  id: number;
  sessionToken: string;
  customerName: string;
  customerEmail: string;
  /** 客户手机号（规范化后如 13812345678 或 +8613812345678） */
  customerPhone: string;
  customerIp: string | null;
  customerId: number | null;
  mailboxId: number | null;
  mailboxName: string | null;
  userId: number | null;
  userName: string | null;
  conversationId: number | null;
  status: ChatSessionStatus;
  statusLabel: string;
  subject: string | null;
  customerLastSeenAt: string | null;
  agentLastSeenAt: string | null;
  customerUnreadCount: number;
  agentUnreadCount: number;
  rating: number | null;
  ratingComment: string | null;
  /** 客服主动发起关闭、向客户推送满意度调查请求的时间（UTC ISO，null=未发起） */
  ratingRequestedAt: string | null;
  /** 等待客户评分的超时时间（UTC ISO，通常=ratingRequestedAt+3 分钟；超时后后台自动转为 closed） */
  ratingTimeoutAt: string | null;
  /** 客户提交评分的时间（UTC ISO，null=未评分；RateChatAsync 调用 ReallyClose 后填写） */
  ratingSubmittedAt: string | null;
  messageCount: number;
  createdAt: string;
  updatedAt: string;
  closedAt: string | null;
  /** 软删除时间（仅垃圾箱列表接口返回） */
  deletedAt?: string | null;
}

export interface ChatMessageDto {
  id: number;
  sessionId: number;
  senderType: string;
  senderId: number | null;
  senderName: string;
  messageType: string;
  content: string;
  attachmentUrl: string | null;
  attachmentName: string | null;
  isRead: boolean;
  readAt: string | null;
  isRecalled: boolean;
  recalledAt: string | null;
  editedAt: string | null;
  createdAt: string;
}

export interface EditChatMessageRequest {
  content: string;
}

export interface TypingRequest {
  senderType: string;
}

export interface InitiateChatRequest {
  customerName: string;
  customerEmail: string;
  /** 客户手机号（必填，规范化后如 13812345678 或 +8613812345678） */
  customerPhone: string;
  mailboxId?: number | null;
  subject?: string | null;
  initialMessage?: string | null;
}

export interface SendChatMessageRequest {
  content: string;
  messageType: string;
  attachmentUrl?: string | null;
  attachmentName?: string | null;
}

export interface TransferToTicketRequest {
  mailboxId?: number | null;
  userId?: number | null;
  subject?: string | null;
}

export interface RateChatRequest {
  rating: number;
  comment?: string | null;
}

export interface ChatSessionQuery {
  status?: string | null;
  mailboxId?: number | null;
  userId?: number | null;
  customerId?: number | null;
  keyword?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  page: number;
  pageSize: number;
}

export interface ChatOverviewResponse {
  stats: Record<string, number>;
  activeSessions: ChatSessionDto[];
  waitingSessions: ChatSessionDto[];
  statusOptions: Record<string, string>;
}

export interface InitiateChatResponse {
  session: ChatSessionDto;
  welcomeMessage: ChatMessageDto | null;
}

export interface AssignChatSessionRequest {
  userId: number;
}

export interface MarkAsReadRequest {
  readerType: string;
}

export interface UnreadCountResponse {
  count: number;
}
