// ====== 报表相关 ======
import type { PagedResponse } from './common';

export interface ReportSummaryDto {
  newConversations: number;
  closedConversations: number;
  totalReplies: number;
  newCustomers: number;
  averageFirstResponseHours: number;
  averageResolutionHours: number;
  slaAchievementRate: number;
}

export interface ReportTrendPointDto {
  date: string;
  newCount: number;
  closedCount: number;
  replyCount: number;
}

export interface ReportStatusDistributionDto {
  status: number;
  statusName: string;
  count: number;
  percentage: number;
}

export interface ReportUserPerformanceDto {
  userId: number;
  userName: string;
  email: string;
  assignedCount: number;
  replyCount: number;
  closedCount: number;
  averageFirstResponseHours: number;
}

export interface ReportMailboxLoadDto {
  mailboxId: number;
  mailboxName: string;
  email: string;
  newCount: number;
  closedCount: number;
  activeCount: number;
  percentage: number;
}

export interface ReportCustomerDomainDto {
  domain: string;
  conversationCount: number;
  uniqueCustomerCount: number;
  percentage: number;
}

export interface ReportResponse {
  startDate: string;
  endDate: string;
  mailboxId: number | null;
  summary: ReportSummaryDto;
  trend: ReportTrendPointDto[];
  statusDistribution: ReportStatusDistributionDto[];
  userPerformance: ReportUserPerformanceDto[];
  mailboxLoad: ReportMailboxLoadDto[];
  customerDomains: ReportCustomerDomainDto[];
}

// ====== 满意度评价 ======
export interface SatisfactionRatingDto {
  id: number;
  /** 数据来源：satisfaction_link = 通过满意度评价链接（邮件/IM 渠道）写入；chat_session = 在线聊天会话评分 */
  source?: 'satisfaction_link' | 'chat_session';
  /** 当 source = chat_session 时保存 ChatSession.Id，其他情况为 null */
  chatSessionId?: number | null;
  conversationId: number;
  conversationSubject: string | null;
  threadId: number | null;
  customerId: number;
  customerName: string | null;
  customerEmail: string | null;
  userId: number | null;
  userName: string | null;
  rating: number;
  ratingLabel: string;
  ratingCategory: string;
  comment: string | null;
  createdAt: string;
}

export interface SatisfactionRatingStatsDto {
  totalCount: number;
  excellentCount: number;
  goodCount: number;
  averageCount: number;
  poorCount: number;
  badCount: number;
  averageScore: number;
  goodRate: number;
}

export interface DailySatisfactionStatsDto {
  date: string;
  totalCount: number;
  excellentCount: number;
  goodCount: number;
  averageCount: number;
  poorCount: number;
  badCount: number;
  averageScore: number;
}

export interface MailboxSatisfactionStatsDto {
  mailboxId: number;
  mailboxName: string;
  email: string;
  totalCount: number;
  excellentCount: number;
  goodCount: number;
  averageCount: number;
  poorCount: number;
  badCount: number;
  averageScore: number;
  goodRate: number;
}

export interface UserSatisfactionStatsDto {
  userId: number;
  userName: string;
  email: string;
  totalCount: number;
  excellentCount: number;
  goodCount: number;
  averageCount: number;
  poorCount: number;
  badCount: number;
  averageScore: number;
  goodRate: number;
}

export interface SatisfactionOverviewResponse {
  stats: SatisfactionRatingStatsDto;
  dailyStats: DailySatisfactionStatsDto[];
  mailboxStats: MailboxSatisfactionStatsDto[];
  userStats: UserSatisfactionStatsDto[];
  recentRatings: SatisfactionRatingDto[];
}

export interface SatisfactionRatingQuery {
  page?: number;
  pageSize?: number;
  rating?: number | null;
  mailboxId?: number | null;
  userId?: number | null;
  startDate?: string | null;
  endDate?: string | null;
  keyword?: string | null;
}

export interface CreateSatisfactionRatingRequest {
  conversationId: number;
  rating: number;
  comment?: string | null;
  customerName?: string | null;
  customerEmail?: string | null;
  token?: string | null;
}

// ====== 自定义报表 DTOs — 与后端 CustomReportResponses.cs 严格对应 ======
export interface CustomReportDto {
  id: number;
  name: string;
  description?: string | null;
  category: string;
  visibility: 'private' | 'team' | 'public';
  ownerId: number;
  ownerName?: string | null;
  teamId?: number | null;
  teamName?: string | null;
  reportConfig: string;
  isDashboardPinned: boolean;
  dashboardSortOrder: number;
  createdAt: string;
  updatedAt: string;
  deletedAt?: string | null;
  canEdit: boolean;
  canDelete: boolean;
}

export interface CustomReportExecutionResultDto {
  reportMeta: ReportMetaDto;
  sections: SectionResultDto[];
}

export interface ReportMetaDto {
  id: number;
  name: string;
  startDate: string;
  endDate: string;
  mailboxId?: number | null;
  groupBy: 'none' | 'week' | 'month';
}

export interface ReportCell {
  value: string | number | null;
  formattedValue?: string;
}

export interface ReportRow {
  cells: ReportCell[] | Record<string, ReportCell>;
}

export interface ReportDataset {
  columns: string[];
  rows: ReportRow[];
}

export type SectionData =
  | ReportDataset
  | ReportSummaryDto
  | ReportTrendPointDto[]
  | ReportStatusDistributionDto[]
  | ReportUserPerformanceDto[]
  | ReportMailboxLoadDto[]
  | ReportCustomerDomainDto[]
  | Record<string, unknown>
  | unknown[];

export interface SectionResultDto {
  key: string;
  chartType: string;
  data?: SectionData;
}

export interface CustomReportScheduleDto {
  id: number;
  customReportId: number;
  recurrence: 'daily' | 'weekly' | 'monthly';
  weekDay?: number | null;
  sendTime: string;
  recipients: string[];
  subject: string;
  attachmentFormat: 'csv' | 'xlsx' | 'pdf';
  isActive: boolean;
  lastRunAt?: string | null;
  nextRunAt?: string | null;
}

// 内部报表配置 JSON（ReportConfigDto）
export interface ReportConfigDto {
  dateRange: DateRangeConfig;
  mailboxId?: number | null;
  groupBy: 'none' | 'week' | 'month';
  sections: SectionConfig[];
}

export interface DateRangeConfig {
  type: 'preset' | 'custom';
  preset?: 'last7' | 'last30' | 'last90' | 'thisWeek' | 'thisMonth' | 'lastMonth' | null;
  start?: string | null;
  end?: string | null;
}

export interface SectionConfig {
  key: string;
  chartType: string;
}

// 请求 DTOs — 与 CustomReportRequests.cs 严格对应
export interface CreateCustomReportRequest {
  name: string;
  description?: string | null;
  category: string;
  visibility: 'private' | 'team' | 'public';
  teamId?: number | null;
  reportConfig: string;
}

export interface UpdateCustomReportRequest {
  name: string;
  description?: string | null;
  category: string;
  visibility: 'private' | 'team' | 'public';
  teamId?: number | null;
  reportConfig: string;
}

export interface CreateScheduleRequest {
  recurrence: 'daily' | 'weekly' | 'monthly';
  weekDay?: number | null;
  sendTime: string;
  recipients: string;
  subject: string;
  attachmentFormat: 'csv' | 'xlsx' | 'pdf';
  isActive: boolean;
}

export type UpdateScheduleRequest = CreateScheduleRequest;

export interface PinRequest {
  pinned: boolean;
  sortOrder?: number | null;
}

// 分页列表响应（复用项目统一 PagedResponse 结构）
export type PagedCustomReportsResponse = PagedResponse<CustomReportDto>;

