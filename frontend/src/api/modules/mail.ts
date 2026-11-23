import client from '../client';
import type {
  MailSendResult,
  MailFetchResult,
  MailTemplateDto,
  MailTemplateVariable,
  MailTemplateOverviewResponse,
  MailTemplatePreviewResponse,
  CreateMailTemplateRequest,
  UpdateMailTemplateRequest,
  PreviewMailTemplateRequest,
  MailTemplateQuery,
  EmailSignatureDto,
  EmailSignatureOverviewResponse,
  CreateEmailSignatureRequest,
  UpdateEmailSignatureRequest,
  EmailSignatureQuery,
  CannedResponseDto,
  CreateCannedResponseRequest,
  UpdateCannedResponseRequest,
  SendLogDto,
  SendLogOverviewResponse,
  SendLogQuery,
  PagedResponse,
} from '../types';

// ====== 邮件 API ======
export const mailApi = {
  send: (mailboxId: number, to: string, subject: string, body: string) =>
    client
      .post<MailSendResult>('/api/mail/send', { mailboxId, to, subject, body })
      .then((r) => r.data),
  reply: (conversationId: number, body: string, isInternal = false) =>
    client
      .post<MailSendResult>(`/api/mail/reply/${conversationId}`, { body, isInternal })
      .then((r) => r.data),
  fetch: (mailboxId: number) =>
    client.post<MailFetchResult>(`/api/mail/fetch/${mailboxId}`).then((r) => r.data),
};

// ====== 邮件模板 API ======
export const mailTemplateApi = {
  list: (params?: MailTemplateQuery) =>
    client
      .get<{ data: MailTemplateDto[]; totalItems: number; totalPages: number }>('/api/mailtemplates', { params })
      .then((r) => r.data),
  get: (id: number) =>
    client.get<MailTemplateDto>(`/api/mailtemplates/${id}`).then((r) => r.data),
  getBySlug: (slug: string) =>
    client.get<MailTemplateDto>(`/api/mailtemplates/slug/${slug}`).then((r) => r.data),
  overview: () =>
    client.get<MailTemplateOverviewResponse>('/api/mailtemplates/overview').then((r) => r.data),
  create: (data: CreateMailTemplateRequest) =>
    client.post<MailTemplateDto>('/api/mailtemplates', data).then((r) => r.data),
  update: (id: number, data: UpdateMailTemplateRequest) =>
    client.put<MailTemplateDto>(`/api/mailtemplates/${id}`, data).then((r) => r.data),
  remove: (id: number) =>
    client.delete<boolean>(`/api/mailtemplates/${id}`).then((r) => r.data),
  getTrash: (page = 1, pageSize = 20, keyword = '') =>
    client
      .get<{ data: MailTemplateDto[]; totalItems: number; totalPages: number }>('/api/mailtemplates/trash', {
        params: { page, pageSize, keyword },
      })
      .then((r) => r.data),
  getTrashCount: () => client.get<{ count: number }>('/api/mailtemplates/trash/count').then((r) => r.data),
  restore: (id: number) => client.post<MailTemplateDto>(`/api/mailtemplates/${id}/restore`).then((r) => r.data),
  hardDelete: (id: number) => client.delete<boolean>(`/api/mailtemplates/${id}/hard`).then((r) => r.data),
  clearTrash: () => client.delete<{ count: number }>('/api/mailtemplates/trash').then((r) => r.data),
  preview: (data: PreviewMailTemplateRequest) =>
    client.post<MailTemplatePreviewResponse>('/api/mailtemplates/preview', data).then((r) => r.data),
  previewById: (id: number, customVariables: Record<string, string> = {}) =>
    client.post<MailTemplatePreviewResponse>(`/api/mailtemplates/${id}/preview`, { customVariables }).then((r) => r.data),
  builtInVariables: () =>
    client.get<MailTemplateVariable[]>('/api/mailtemplates/variables/built-in').then((r) => r.data ?? []),
};

// ====== 邮件签名 API ======
export const emailSignatureApi = {
  list: (params?: EmailSignatureQuery) =>
    client
      .get<{ data: EmailSignatureDto[]; totalItems: number; totalPages: number }>('/api/emailsignatures', { params })
      .then((r) => r.data),
  get: (id: number) =>
    client.get<EmailSignatureDto>(`/api/emailsignatures/${id}`).then((r) => r.data),
  overview: () =>
    client.get<EmailSignatureOverviewResponse>('/api/emailsignatures/overview').then((r) => r.data),
  mySignatures: (mailboxId?: number) =>
    client
      .get<{ data: EmailSignatureDto[] }>('/api/emailsignatures/my', { params: { mailboxId } })
      .then((r) => r.data),
  userSignatures: (userId: number, mailboxId?: number) =>
    client
      .get<{ data: EmailSignatureDto[] }>(`/api/emailsignatures/user/${userId}`, { params: { mailboxId } })
      .then((r) => r.data),
  myDefault: (mailboxId?: number) =>
    client
      .get<EmailSignatureDto>('/api/emailsignatures/my/default', { params: { mailboxId } })
      .then((r) => r.data),
  create: (data: CreateEmailSignatureRequest) =>
    client.post<EmailSignatureDto>('/api/emailsignatures', data).then((r) => r.data),
  update: (id: number, data: UpdateEmailSignatureRequest) =>
    client.put<EmailSignatureDto>(`/api/emailsignatures/${id}`, data).then((r) => r.data),
  remove: (id: number) =>
    client.delete<boolean>(`/api/emailsignatures/${id}`).then((r) => r.data),
  setDefault: (id: number) =>
    client.post<EmailSignatureDto>(`/api/emailsignatures/${id}/set-default`).then((r) => r.data),
  getTrash: (page = 1, pageSize = 20, keyword = '') =>
    client
      .get<{ data: EmailSignatureDto[]; totalItems: number; totalPages: number }>('/api/emailsignatures/trash', {
        params: { page, pageSize, keyword },
      })
      .then((r) => r.data),
  getTrashCount: () =>
    client.get<{ count: number }>('/api/emailsignatures/trash/count').then((r) => r.data),
  restore: (id: number) =>
    client.post<EmailSignatureDto>(`/api/emailsignatures/${id}/restore`).then((r) => r.data),
  hardDelete: (id: number) =>
    client.delete<boolean>(`/api/emailsignatures/${id}/hard`).then((r) => r.data),
  clearTrash: () =>
    client.delete<{ count: number }>('/api/emailsignatures/trash').then((r) => r.data),
};

// ====== 快捷回复模板 API ======
export const cannedResponseApi = {
  list: (mailboxId?: number | null, category?: string) =>
    client
      .get<CannedResponseDto[]>('/api/cannedresponses', {
        params: { mailboxId: mailboxId ?? undefined, category: category || undefined },
      })
      .then((r) => r.data ?? []),
  get: (id: number) =>
    client.get<CannedResponseDto>(`/api/cannedresponses/${id}`).then((r) => r.data),
  search: (keyword: string, mailboxId?: number | null) =>
    client
      .get<CannedResponseDto[]>('/api/cannedresponses/search', {
        params: { keyword, mailboxId: mailboxId ?? undefined },
      })
      .then((r) => r.data ?? []),
  categories: (mailboxId?: number | null) =>
    client
      .get<string[]>('/api/cannedresponses/categories', {
        params: { mailboxId: mailboxId ?? undefined },
      })
      .then((r) => r.data ?? []),
  create: (data: CreateCannedResponseRequest) =>
    client.post<CannedResponseDto>('/api/cannedresponses', data).then((r) => r.data),
  update: (id: number, data: UpdateCannedResponseRequest) =>
    client.put<CannedResponseDto>(`/api/cannedresponses/${id}`, data).then((r) => r.data),
  remove: (id: number) =>
    client.delete<boolean>(`/api/cannedresponses/${id}`).then((r) => r.data),
  getTrash: (page = 1, pageSize = 20, keyword = '') =>
    client.get<{ data: CannedResponseDto[]; totalItems: number; totalPages: number }>('/api/cannedresponses/trash', { params: { page, pageSize, keyword } }).then((r) => r.data),
  getTrashCount: () => client.get<{ count: number }>('/api/cannedresponses/trash/count').then((r) => r.data),
  restore: (id: number) => client.post<CannedResponseDto>(`/api/cannedresponses/${id}/restore`).then((r) => r.data),
  hardDelete: (id: number) => client.delete<boolean>(`/api/cannedresponses/${id}/hard`).then((r) => r.data),
  clearTrash: () => client.delete<{ count: number }>('/api/cannedresponses/trash').then((r) => r.data),
};

// ====== 邮件发送日志 API ======
export const sendLogApi = {
  list: (query: SendLogQuery = {}) =>
    client
      .get<PagedResponse<SendLogDto>>('/api/sendlogs', {
        params: {
          page: query.page ?? 1,
          pageSize: query.pageSize ?? 20,
          status: query.status ?? undefined,
          mailType: query.mailType ?? undefined,
          mailboxId: query.mailboxId ?? undefined,
          userId: query.userId ?? undefined,
          startDate: query.startDate || undefined,
          endDate: query.endDate || undefined,
          keyword: query.keyword || undefined,
        },
      })
      .then((r) => r.data),
  get: (id: number) =>
    client.get<SendLogDto>(`/api/sendlogs/${id}`).then((r) => r.data),
  overview: (startDate?: string, endDate?: string, mailboxId?: number | null) =>
    client
      .get<SendLogOverviewResponse>('/api/sendlogs/overview', {
        params: {
          startDate: startDate || undefined,
          endDate: endDate || undefined,
          mailboxId: mailboxId ?? undefined,
        },
      })
      .then((r) => r.data),
  recentFailures: (count = 10) =>
    client
      .get<SendLogDto[]>('/api/sendlogs/recent-failures', {
        params: { count },
      })
      .then((r) => r.data ?? []),
  statusOptions: () =>
    client
      .get<Record<number, string>>('/api/sendlogs/status-options')
      .then((r) => r.data ?? {}),
  mailTypeOptions: () =>
    client
      .get<Record<number, string>>('/api/sendlogs/mail-type-options')
      .then((r) => r.data ?? {}),
};
