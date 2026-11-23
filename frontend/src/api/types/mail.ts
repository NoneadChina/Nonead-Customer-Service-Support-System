// ====== 快捷回复模板 ======
export interface CannedResponseDto {
  id: number;
  mailboxId: number | null;
  mailboxName: string | null;
  name: string;
  body: string;
  category: string | null;
  sortOrder: number;
  isShared: boolean;
  createdByUserId: number | null;
  createdByUserName: string | null;
  createdAt: string;
  updatedAt: string;
  deletedAt?: string | null;
}

export interface CreateCannedResponseRequest {
  mailboxId?: number | null;
  name: string;
  body: string;
  category?: string | null;
  sortOrder?: number;
  isShared?: boolean;
}

export interface UpdateCannedResponseRequest {
  name: string;
  body: string;
  category?: string | null;
  mailboxId?: number | null;
  sortOrder?: number;
  isShared?: boolean;
}

// ====== 邮件发送日志 ======
export interface SendLogDto {
  id: number;
  threadId: number | null;
  conversationId: number | null;
  conversationSubject: string | null;
  messageId: string;
  email: string;
  mailType: number;
  mailTypeName: string;
  status: number;
  statusName: string;
  statusCategory: string;
  customerId: number | null;
  customerEmail: string | null;
  userId: number | null;
  userName: string | null;
  createdAt: string;
}

export interface SendLogStatsDto {
  totalCount: number;
  successCount: number;
  failedCount: number;
  pendingCount: number;
  successRate: number;
}

export interface MailboxSendStatsDto {
  mailboxId: number;
  mailboxName: string;
  email: string;
  totalCount: number;
  successCount: number;
  failedCount: number;
  successRate: number;
}

export interface DailySendStatsDto {
  date: string;
  totalCount: number;
  successCount: number;
  failedCount: number;
}

export interface SendLogOverviewResponse {
  stats: SendLogStatsDto;
  mailboxStats: MailboxSendStatsDto[];
  dailyStats: DailySendStatsDto[];
  recentFailures: SendLogDto[];
}

export interface SendLogQuery {
  page?: number;
  pageSize?: number;
  status?: number | null;
  mailType?: number | null;
  mailboxId?: number | null;
  userId?: number | null;
  startDate?: string | null;
  endDate?: string | null;
  keyword?: string | null;
}

// ====== 邮件模板 ======
export interface MailTemplateVariable {
  key: string;
  label: string;
  category: string;
  defaultValue?: string;
}

export interface MailTemplateDto {
  id: number;
  name: string;
  slug: string;
  type: number;
  typeName: string;
  subject: string;
  bodyHtml: string;
  bodyText: string;
  variablesJson: string | null;
  variables: MailTemplateVariable[];
  isActive: boolean;
  isSystem: boolean;
  mailboxId: number | null;
  mailboxName: string | null;
  description: string | null;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
  deletedAt?: string | null;
}

export interface MailTemplateTypeCount {
  type: number;
  typeName: string;
  count: number;
}

export interface MailTemplateOverviewResponse {
  total: number;
  activeCount: number;
  systemCount: number;
  customCount: number;
  typeCounts: MailTemplateTypeCount[];
}

export interface MailTemplatePreviewResponse {
  subject: string;
  bodyHtml: string;
  bodyText: string;
}

export interface CreateMailTemplateRequest {
  name: string;
  slug: string;
  type: number;
  subject: string;
  bodyHtml: string;
  bodyText: string;
  variablesJson: string | null;
  isActive: boolean;
  mailboxId: number | null;
  description: string | null;
  sortOrder: number;
}

export interface UpdateMailTemplateRequest {
  name: string;
  slug: string;
  type: number;
  subject: string;
  bodyHtml: string;
  bodyText: string;
  variablesJson: string | null;
  isActive: boolean;
  mailboxId: number | null;
  description: string | null;
  sortOrder: number;
}

export interface PreviewMailTemplateRequest {
  subject: string;
  bodyHtml: string;
  bodyText: string;
  variables: Record<string, string>;
}

export interface MailTemplateQuery {
  page?: number;
  pageSize?: number;
  type?: number | null;
  mailboxId?: number | null;
  isActive?: boolean | null;
  keyword?: string | null;
}

// ====== 邮件签名 ======
export interface EmailSignatureDto {
  id: number;
  userId: number;
  userName: string;
  userEmail: string;
  mailboxId: number | null;
  mailboxName: string | null;
  name: string;
  body: string;
  isDefault: boolean;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
  deletedAt?: string | null;
}

export interface EmailSignatureOverviewResponse {
  totalSignatures: number;
  defaultSignatures: number;
  mailboxSignatures: number;
  globalSignatures: number;
  topSignatures: EmailSignatureDto[];
}

export interface CreateEmailSignatureRequest {
  name: string;
  body: string;
  mailboxId: number | null;
  isDefault: boolean;
  sortOrder: number;
}

export interface UpdateEmailSignatureRequest {
  name: string;
  body: string;
  mailboxId: number | null;
  isDefault: boolean;
  sortOrder: number;
}

export interface EmailSignatureQuery {
  page?: number;
  pageSize?: number;
  userId?: number | null;
  mailboxId?: number | null;
  isDefault?: boolean | null;
  keyword?: string | null;
}
