export interface TimeEntryDto {
  id: number;
  entryType: 'timer' | 'manual';
  description: string;
  conversationId: number | null;
  conversationNumber: number | null;
  conversationSubject: string | null;
  customerId: number | null;
  customerName: string | null;
  userId: number;
  userName: string;
  mailboxId: number | null;
  startTime: string;
  endTime: string | null;
  durationMinutes: number;
  isRunning: boolean;
  billable: boolean;
  createdAt: string;
  updatedAt: string;
  /** 乐观锁版本令牌（RowVersion base64） */
  rowVersion?: string | null;
}

export interface CreateTimeEntryRequest {
  entryType: 'timer' | 'manual';
  description: string;
  conversationId?: number | null;
  customerId?: number | null;
  mailboxId?: number | null;
  startTime: string;
  endTime?: string | null;
  durationMinutes?: number;
  billable?: boolean;
}

export interface UpdateTimeEntryRequest {
  description: string;
  startTime: string;
  endTime?: string | null;
  durationMinutes?: number;
  billable?: boolean;
  /** 乐观锁匹配令牌（rowVersion base64），后端返回 409 + latest 快照 */
  ifMatch?: string | null;
}

export interface StartTimerRequest {
  description?: string;
  conversationId?: number | null;
  customerId?: number | null;
  mailboxId?: number | null;
  billable?: boolean;
}

export interface TimeEntryQuery {
  page?: number;
  pageSize?: number;
  userId?: number | null;
  conversationId?: number | null;
  customerId?: number | null;
  entryType?: 'timer' | 'manual' | null;
  billable?: boolean | null;
  startDate?: string | null;
  endDate?: string | null;
  search?: string | null;
}

export interface TimeTrackingOverviewResponse {
  todayMinutes: number;
  weekMinutes: number;
  monthMinutes: number;
  totalMinutes: number;
  activeTimerId: number | null;
  activeTimerDescription: string | null;
  activeTimerStartTime: string | null;
  todayCount: number;
  weekCount: number;
  billableMinutes: number;
}

export interface TimeTrackingReportItem {
  label: string;
  totalMinutes: number;
  count: number;
  billableMinutes: number;
}

export interface TimeTrackingReportResponse {
  byUser: TimeTrackingReportItem[];
  byConversation: TimeTrackingReportItem[];
  byDate: TimeTrackingReportItem[];
  totalMinutes: number;
  totalCount: number;
}

export interface PagedTimeEntries {
  data: TimeEntryDto[];
  totalItems: number;
  currentPage: number;
  totalPages: number;
}
