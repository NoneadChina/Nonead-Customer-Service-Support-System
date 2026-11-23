import type { PagedResponse } from './common';

// ====== 全局搜索 ======
export interface ConversationSearchItem {
  id: number;
  subject: string | null;
  preview: string | null;
  customerEmail: string | null;
  status: number;
  priority: number;
  createdAt: string;
  lastReplyAt: string | null;
  mailboxId: number;
  snippet: string;
}

export interface CustomerSearchItem {
  id: number;
  fullName: string;
  email: string | null;
  phone: string | null;
  company: string | null;
  createdAt: string;
  snippet: string;
}

export interface KnowledgeSearchItem {
  id: number;
  title: string;
  excerpt: string;
  categoryId: number | null;
  viewCount: number;
  createdAt: string;
  updatedAt: string | null;
  snippet: string;
}

export interface GlobalSearchResponse {
  query: string;
  page: number;
  pageSize: number;
  total: number;
  conversationTotal: number;
  customerTotal: number;
  knowledgeTotal: number;
  conversations: ConversationSearchItem[];
  customers: CustomerSearchItem[];
  knowledge: KnowledgeSearchItem[];
}

export type SearchEntityType = 'all' | 'conversation' | 'customer' | 'knowledge';

export interface UserResponse {
  id: number;
  firstName: string;
  lastName: string;
  email: string;
  role: number;
  status: number;
  fullName: string;
  timezone?: string;
  locale?: string;
  avatar?: string | null;
  mailboxIds: number[];
  permissions: number[];
  /** 细粒度权限键列表（与后端 PermissionItem.Key 对应，如 conversations.assign） */
  stringPermissions?: string[];
  createdAt: string;
  deletedAt?: string | null;
}

export interface UpdateProfileRequest {
  firstName: string;
  lastName: string;
  timezone: string;
  locale: string;
  avatar?: string;
}

export interface UserNotificationSettingsResponse {
  userId: number;
  locale: string;
  timezone: string;
  emailOnNewConversation: boolean;
  emailOnAssignedToMe: boolean;
  emailOnNewReply: boolean;
  emailOnMention: boolean;
  browserOnNewConversation: boolean;
  browserOnAssignedToMe: boolean;
  browserOnNewReply: boolean;
  browserOnMention: boolean;
  defaultSignature?: string | null;
  useGlobalSignature: boolean;
}

export type UserNotificationSettingsRequest = Partial<UserNotificationSettingsResponse>;

export interface CreateUserRequest {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  role: number;
  mailboxIds: number[];
}

// ====== 仪表盘相关 ======
export interface DashboardResponse {
  // 会话核心指标
  totalConversations: number;
  activeConversations: number;
  pendingConversations: number;
  closedConversations: number;
  unassignedCount: number;
  totalUsers: number;
  totalCustomers: number;
  totalMailboxes: number;
  todayNewConversations: number;
  todayClosedConversations: number;
  avgFirstResponseTimeMinutes: number;
  avgResolutionTimeMinutes: number;
  slaWarningCount: number;
  slaBreachedCount: number;
  satisfactionAverageScore: number;
  satisfactionTotalCount: number;
  userWorkloads: UserWorkloadDto[];
  mailboxStats: MailboxStatsDto[];
  conversationTrend: ConversationTrendDto[];

  // 知识库模块统计
  knowledgeTotalArticles: number;
  knowledgePublishedArticles: number;
  knowledgeDraftArticles: number;
  knowledgeTotalCategories: number;
  knowledgeTotalViews: number;
  knowledgeTotalLikes: number;

  // 日历模块统计
  calendarTotalEvents: number;
  calendarTodayEvents: number;
  calendarUpcomingEvents: number;
  calendarPendingEvents: number;
  calendarOverdueEvents: number;

  // 工作流模块统计
  workflowActiveCount: number;
  workflowTodayExecutions: number;
  workflowTodaySuccess: number;
  workflowTodayFailed: number;

  // IM 队列统计
  imQueuePending: number;
  imQueueSending: number;
  imQueueSentToday: number;
  imQueueFailedToday: number;

  // 其他实体统计
  totalTags: number;
  totalWorkflows: number;
  totalCannedResponses: number;
  totalWebhooks: number;
}

export interface UserWorkloadDto {
  userId: number;
  userName: string;
  activeCount: number;
  closedToday: number;
  repliesToday: number;
}

export interface MailboxStatsDto {
  mailboxId: number;
  mailboxName: string;
  activeCount: number;
  pendingCount: number;
  closedCount: number;
}

export interface ConversationTrendDto {
  date: string;
  newCount: number;
  closedCount: number;
}

// ====== 会话相关 ======
export interface ConversationResponse {
  id: number;
  mailboxId: number;
  customerId: number | null;
  userId: number | null;
  folderId: number | null;
  type: number;
  sourceVia: number;
  /** 来源类型：1=Email、2=Web、3=Api、4=Phone、5=Chat、6=Social、7=WeChat */
  sourceType?: number;
  status: number;
  /** 优先级：0=Low、1=Normal、2=High、3=Urgent */
  priority: number;
  subject: string;
  customerEmail: string;
  /** 预览文本（最新回复摘要，纯文本，用于列表显示） */
  preview?: string;
  hasAttachments: boolean;
  lastReplyAt: string | null;
  closedAt: string | null;
  createdAt: string;
  updatedAt: string;
  /**
   * 当前用户是否有未读消息。
   * true 表示会话有新消息（LastReplyAt > 用户最后阅读时间或用户从未查看）。
   * 列表页用于展示"新消息"徽章，进入会话详情后由后端标记为已读，下次列表刷新时置为 false。
   */
  hasUnreadMessages?: boolean;
  followerIds: number[];
  customer?: CustomerResponse | null;
  user?: UserResponse | null;
  threads?: ThreadResponse[] | null;
  tags?: TagResponse[] | null;
}

export interface CustomerPhoneDto {
  value: string;
  type: number;
}

export interface CustomerSocialProfileDto {
  value: string;
  type: number;
}

export interface CustomerResponse {
  id: number;
  firstName: string;
  lastName: string;
  fullName: string;
  mainEmail: string | null;
  address?: string | null;
  city?: string | null;
  state?: string | null;
  zip?: string | null;
  country?: string | null;
  company?: string | null;
  jobTitle?: string | null;
  notes?: string | null;
  /** 客户来源渠道（如 wechat、email、phone） */
  channel?: string | null;
  /** 渠道对应的外部标识（微信 OpenId 等） */
  channelId?: string | null;
  phones?: CustomerPhoneDto[];
  socialProfiles?: CustomerSocialProfileDto[];
  websites?: string[];
  conversationsCount: number;
  avatar?: string | null;
  createdAt: string;
  updatedAt?: string;
  deletedAt?: string | null;
}

export interface CreateCustomerRequest {
  firstName: string;
  lastName: string;
  email: string;
  address?: string | null;
  city?: string | null;
  state?: string | null;
  zip?: string | null;
  country?: string | null;
  company?: string | null;
  jobTitle?: string | null;
  notes?: string | null;
  phones?: CustomerPhoneDto[];
  socialProfiles?: CustomerSocialProfileDto[];
  websites?: string[];
  avatar?: string | null;
}

export interface ThreadResponse {
  id: number;
  conversationId: number;
  userId: number | null;
  customerId: number | null;
  type: number;
  /** 来源类型：1=Email、2=Web、3=Api、4=Phone、5=Chat、6=Social、7=WeChat */
  sourceType?: number;
  body: string;
  from: string;
  to: string;
  cc?: string | null;
  bcc?: string | null;
  subject?: string | null;
  state?: number;
  isInternal: boolean;
  hasAttachments: boolean;
  createdAt: string;
  user?: UserResponse | null;
  attachments?: AttachmentResponse[] | null;
}

export interface AttachmentResponse {
  id: number;
  threadId: number;
  fileName: string;
  mimeType: string;
  type: number;
  size: number;
  embedded: boolean;
  url: string;
  createdAt: string;
}

export const ThreadType = {
  Customer: 1,
  Message: 2,
  Note: 3,
  LineItem: 4,
} as const;

export const ThreadState = {
  Active: 1,
  Draft: 2,
  Deleted: 3,
} as const;

// ====== 邮箱相关 ======
export interface MailboxResponse {
  id: number;
  name: string;
  email: string;
  inHost: string;
  inPort: number;
  inEncryption: number;
  inValidateCert: boolean;
  inUsername: string;
  hasInPassword: boolean;
  imapSentFolder: string;
  outHost: string;
  outPort: number;
  outEncryption: number;
  outUsername: string;
  hasOutPassword: boolean;
  aliases: string;
  aliasesReply: string;
  autoBcc: string;
  autoReplyEnabled: boolean;
  autoReplySubject: string;
  autoReplyMessage: string;
  beforeReply: string;
  signature: string;
  oauthEnabled: boolean;
  oauthProvider: string;
  oauthTokenExpiresAt: string | null;
  isActive: boolean;
  userIds: number[];
  fetchFrequency: number;
  lastFetchAt?: string | null;
  createdAt: string;
  deletedAt?: string | null;
  conversationCount?: number;
  openConversationCount?: number;
  todaySendCount?: number;
  todayReceiveCount?: number;
}

export interface CreateMailboxRequest {
  name: string;
  email: string;
  inHost: string;
  inPort: number;
  inEncryption: number;
  inValidateCert: boolean;
  inUsername: string;
  inPassword: string;
  imapSentFolder: string;
  outHost: string;
  outPort: number;
  outEncryption: number;
  outUsername: string;
  outPassword: string;
  aliases: string;
  aliasesReply: string;
  autoBcc: string;
  autoReplyEnabled: boolean;
  autoReplySubject: string;
  autoReplyMessage: string;
  beforeReply: string;
  signature: string;
  isActive: boolean;
  userIds: number[];
  oauthEnabled: boolean;
  oauthProvider: string;
  oauthAccessToken: string;
  oauthRefreshToken: string;
  oauthTokenExpiresAt: string | null;
  fetchFrequency: number;
}

// ====== 标签相关 ======
export interface TagResponse {
  id: number;
  name: string;
  color: string;
  mailboxId: number | null;
  conversationCount?: number;
  createdAt: string;
  updatedAt?: string;
  deletedAt?: string | null;
}

export interface CreateTagRequest {
  name: string;
  color: string;
  mailboxId?: number | null;
}

export interface UpdateTagRequest {
  name: string;
  color: string;
}

// ====== 文件夹相关 ======
export interface FolderResponse {
  id: number;
  mailboxId: number;
  userId: number | null;
  type: number;
  name: string;
  activeCount: number;
  totalCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreateFolderRequest {
  mailboxId: number;
  name: string;
  type: number;
}

export interface UpdateFolderRequest {
  name: string;
}

export type { PagedResponse };
