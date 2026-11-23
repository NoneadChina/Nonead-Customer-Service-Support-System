import client from '../client';
import type {
  KnowledgeCategoryDto,
  CreateKnowledgeCategoryRequest,
  UpdateKnowledgeCategoryRequest,
  KnowledgeCategoryQuery,
  KnowledgeArticleDto,
  KnowledgeArticleAttachmentDto,
  CreateKnowledgeArticleRequest,
  UpdateKnowledgeArticleRequest,
  KnowledgeArticleQuery,
  KnowledgeOverviewResponse,
  KnowledgeSearchResult,
  ChatSessionDto,
  ChatMessageDto,
  InitiateChatRequest,
  SendChatMessageRequest,
  TransferToTicketRequest,
  RateChatRequest,
  ChatSessionQuery,
  ChatOverviewResponse,
  InitiateChatResponse,
  AssignChatSessionRequest,
  UnreadCountResponse,
  CalendarEventDto,
  CalendarPagedResponse,
  CalendarOverviewResponse,
  CalendarEventQuery,
  CreateCalendarEventRequest,
  UpdateCalendarEventRequest,
  DuplicateCalendarEventRequest,
  QuickUpdateCalendarEventRequest,
  CalendarImportResult,
  CalendarTrashCountResponse,
  CalendarPurgeResult,
  CalendarEventTemplateDto,
  CalendarEventTemplateListResponse,
  CreateCalendarEventTemplateRequest,
  UpdateCalendarEventTemplateRequest,
  CalendarBatchOperationRequest,
  CalendarBatchOperationResponse,
  OutlookSyncStatusDto,
  OutlookSyncSettingDto,
  OutlookSyncResultDto,
  UpdateOutlookSyncSettingRequest,
  OutlookAdminSettingsDto,
  UpdateOutlookAdminSettingsRequest,
} from '../types';

// ====== 知识库 API ======
export const knowledgeApi = {
  // 分类
  listCategories: (params?: KnowledgeCategoryQuery) =>
    client
      .get<KnowledgeCategoryDto[]>('/api/knowledge/categories', {
        params: {
          parentId: params?.parentId ?? undefined,
          mailboxId: params?.mailboxId ?? undefined,
          isActive: params?.isActive ?? undefined,
          keyword: params?.keyword || undefined,
        },
      })
      .then((r) => r.data ?? []),
  getCategory: (id: number) =>
    client.get<KnowledgeCategoryDto>(`/api/knowledge/categories/${id}`).then((r) => r.data),
  createCategory: (data: CreateKnowledgeCategoryRequest) =>
    client.post<KnowledgeCategoryDto>('/api/knowledge/categories', data).then((r) => r.data),
  updateCategory: (id: number, data: UpdateKnowledgeCategoryRequest) =>
    client.put<KnowledgeCategoryDto>(`/api/knowledge/categories/${id}`, data).then((r) => r.data),
  removeCategory: (id: number) =>
    client.delete<boolean>(`/api/knowledge/categories/${id}`).then((r) => r.data),

  // 文章
  listArticles: (params?: KnowledgeArticleQuery) =>
    client
      .get<{
        data: KnowledgeArticleDto[];
        currentPage: number;
        totalPages: number;
        totalItems: number;
        pageSize: number;
      }>('/api/knowledge/articles', {
        params: {
          categoryId: params?.categoryId ?? undefined,
          status: params?.status || undefined,
          keyword: params?.keyword || undefined,
          authorId: params?.authorId ?? undefined,
          isPinned: params?.isPinned ?? undefined,
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 20,
        },
      })
      .then((r) => r.data),
  getArticle: (id: number, incrementView = false) =>
    client
      .get<KnowledgeArticleDto>(`/api/knowledge/articles/${id}`, {
        params: { incrementView },
      })
      .then((r) => r.data),
  createArticle: (data: CreateKnowledgeArticleRequest) =>
    client.post<KnowledgeArticleDto>('/api/knowledge/articles', data).then((r) => r.data),
  updateArticle: (id: number, data: UpdateKnowledgeArticleRequest) =>
    client.put<KnowledgeArticleDto>(`/api/knowledge/articles/${id}`, data, {
      headers: data?.ifMatch ? { 'If-Match': data.ifMatch } : undefined,
    }).then((r) => r.data),
  removeArticle: (id: number) =>
    client.delete<boolean>(`/api/knowledge/articles/${id}`).then((r) => r.data),
  likeArticle: (id: number) =>
    client.post<{ success: boolean; liked?: boolean; message?: string }>(`/api/knowledge/articles/${id}/like`).then((r) => r.data),

  // 文章附件
  getAttachments: (articleId: number) =>
    client
      .get<KnowledgeArticleAttachmentDto[]>(`/api/knowledge/articles/${articleId}/attachments`)
      .then((r) => r.data),
  uploadAttachment: (articleId: number, file: File) => {
    const formData = new FormData();
    formData.append('file', file);
    return client
      .post<KnowledgeArticleAttachmentDto>(`/api/knowledge/articles/${articleId}/attachments`, formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })
      .then((r) => r.data);
  },
  deleteAttachment: (articleId: number, attachmentId: number) =>
    client
      .delete<boolean>(`/api/knowledge/articles/${articleId}/attachments/${attachmentId}`)
      .then((r) => r.data),
  /** 获取附件下载 URL（前端直接用 a 标签或 iframe 访问） */
  getAttachmentDownloadUrl: (articleId: number, attachmentId: number) =>
    `/api/knowledge/articles/${articleId}/attachments/${attachmentId}/download`,

  // 文章垃圾箱
  getArticleTrash: (page = 1, pageSize = 10, keyword?: string) =>
    client
      .get<{
        data: KnowledgeArticleDto[];
        currentPage: number;
        totalPages: number;
        totalItems: number;
        pageSize: number;
      }>('/api/knowledge/articles/trash', { params: { page, pageSize, keyword } })
      .then((r) => r.data),
  restoreArticle: (id: number) =>
    client.post<KnowledgeArticleDto>(`/api/knowledge/articles/${id}/restore`).then((r) => r.data),
  hardDeleteArticle: (id: number) =>
    client.delete<boolean>(`/api/knowledge/articles/${id}/hard`).then((r) => r.data),
  clearArticleTrash: () =>
    client.delete<{ count: number }>('/api/knowledge/articles/trash').then((r) => r.data),

  // 分类垃圾箱
  getCategoryTrash: (page = 1, pageSize = 10, keyword?: string) =>
    client
      .get<{
        data: KnowledgeCategoryDto[];
        currentPage: number;
        totalPages: number;
        totalItems: number;
        pageSize: number;
      }>('/api/knowledge/categories/trash', { params: { page, pageSize, keyword } })
      .then((r) => r.data),
  restoreCategory: (id: number) =>
    client.post<KnowledgeCategoryDto>(`/api/knowledge/categories/${id}/restore`).then((r) => r.data),
  hardDeleteCategory: (id: number) =>
    client.delete<boolean>(`/api/knowledge/categories/${id}/hard`).then((r) => r.data),
  clearCategoryTrash: () =>
    client.delete<{ count: number }>('/api/knowledge/categories/trash').then((r) => r.data),

  // 搜索和概览
  search: (keyword: string, page = 1, pageSize = 20) =>
    client
      .get<KnowledgeSearchResult>('/api/knowledge/search', {
        params: { keyword, page, pageSize },
      })
      .then((r) => r.data),
  overview: () =>
    client.get<KnowledgeOverviewResponse>('/api/knowledge/overview').then((r) => r.data),
};

// ====== 在线聊天 API ======
export const chatApi = {
  // 客服侧 - 会话管理
  listSessions: (params?: ChatSessionQuery) =>
    client
      .get<{
        data: ChatSessionDto[];
        currentPage: number;
        totalPages: number;
        totalItems: number;
        pageSize: number;
      }>('/api/chat/sessions', {
        params: {
          status: params?.status || undefined,
          mailboxId: params?.mailboxId ?? undefined,
          userId: params?.userId ?? undefined,
          customerId: params?.customerId ?? undefined,
          keyword: params?.keyword || undefined,
          startDate: params?.startDate || undefined,
          endDate: params?.endDate || undefined,
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 20,
        },
      })
      .then((r) => r.data),
  getSession: (id: number) =>
    client.get<ChatSessionDto>(`/api/chat/sessions/${id}`).then((r) => r.data),
  // 工单详情页按 ConversationId 反查在线聊天会话（若工单由聊天转工单生成），用于合并展示在线聊天评分
  getSessionByConversation: (conversationId: number) =>
    client
      .get<ChatSessionDto>(`/api/chat/sessions/by-conversation/${conversationId}`)
      .then((r) => r.data)
      .catch((e: unknown) => {
        // 404 表示该工单不是从聊天转来的（正常，返回 null）
        if (e && typeof e === 'object' && 'response' in e) {
          const resp = (e as { response?: { status?: number } }).response;
          if (resp?.status === 404) return null;
        }
        throw e;
      }),
  assignSession: (id: number, data: AssignChatSessionRequest) =>
    client.post<ChatSessionDto>(`/api/chat/sessions/${id}/assign`, data).then((r) => r.data),
  closeSession: (id: number) =>
    client.post<ChatSessionDto>(`/api/chat/sessions/${id}/close`).then((r) => r.data),
  getActiveSessions: (mailboxId?: number) =>
    client.get<ChatSessionDto[]>('/api/chat/active-sessions', { params: { mailboxId } }).then((r) => r.data),
  overview: () =>
    client.get<ChatOverviewResponse>('/api/chat/overview').then((r) => r.data),

  // 消息管理
  getMessages: (sessionId: number, pageSize = 100) =>
    client.get<ChatMessageDto[]>(`/api/chat/sessions/${sessionId}/messages`, { params: { pageSize } }).then((r) => r.data),
  sendMessage: (sessionId: number, data: SendChatMessageRequest) =>
    client.post<ChatMessageDto>(`/api/chat/sessions/${sessionId}/messages`, data).then((r) => r.data),
  markAsRead: (sessionId: number, readerType: string) =>
    client.post<boolean>(`/api/chat/sessions/${sessionId}/mark-as-read`, { readerType }).then((r) => r.data),
  getUnreadCount: (sessionId: number, readerType: string) =>
    client.get<UnreadCountResponse>(`/api/chat/sessions/${sessionId}/unread-count`, { params: { readerType } }).then((r) => r.data),

  // 消息撤回/编辑/输入状态
  recallMessage: (sessionId: number, messageId: number) =>
    client.post<ChatMessageDto>(`/api/chat/sessions/${sessionId}/messages/${messageId}/recall`).then((r) => r.data),
  editMessage: (sessionId: number, messageId: number, content: string) =>
    client.put<ChatMessageDto>(`/api/chat/sessions/${sessionId}/messages/${messageId}`, { content }).then((r) => r.data),
  notifyTyping: (sessionId: number, senderType: string) =>
    client.post<boolean>(`/api/chat/sessions/${sessionId}/typing`, { senderType }).then((r) => r.data),

  // 转工单
  transferToTicket: (sessionId: number, data: TransferToTicketRequest) =>
    client.post<ChatSessionDto>(`/api/chat/sessions/${sessionId}/transfer`, data).then((r) => r.data),

  // 客户端接口
  initiateChat: (data: InitiateChatRequest) =>
    client.post<InitiateChatResponse>('/api/chat/initiate', data).then((r) => r.data),
  // T7: sessionToken 经 X-Session-Token 请求头传递（不再进 URL，防访问日志/Referer 泄露）
  sendCustomerMessage: (sessionToken: string, data: SendChatMessageRequest) =>
    client.post<ChatMessageDto>('/api/chat/token/messages', data, { headers: { 'X-Session-Token': sessionToken } }).then((r) => r.data),
  getCustomerMessages: (sessionToken: string, pageSize = 100) =>
    client.get<ChatMessageDto[]>('/api/chat/token/messages', { headers: { 'X-Session-Token': sessionToken }, params: { pageSize } }).then((r) => r.data),
  closeSessionByToken: (sessionToken: string) =>
    client.post<ChatSessionDto>('/api/chat/token/close', undefined, { headers: { 'X-Session-Token': sessionToken } }).then((r) => r.data),
  rateChat: (sessionToken: string, data: RateChatRequest) =>
    client.post<ChatSessionDto>('/api/chat/token/rate', data, { headers: { 'X-Session-Token': sessionToken } }).then((r) => r.data),
  markAsReadByToken: (sessionToken: string) =>
    client.post<boolean>('/api/chat/token/mark-as-read', undefined, { headers: { 'X-Session-Token': sessionToken } }).then((r) => r.data),
  notifyTypingByToken: (sessionToken: string) =>
    client.post<boolean>('/api/chat/token/typing', undefined, { headers: { 'X-Session-Token': sessionToken } }).then((r) => r.data),
  /** 客户侧通过 sessionToken 免登录获取当前会话 DTO（用于轮询 status 变更，如 active → awaiting_rating） */
  getCustomerSession: (sessionToken: string) =>
    client.get<ChatSessionDto>('/api/chat/token/session', { headers: { 'X-Session-Token': sessionToken } }).then((r) => r.data),

  // 会话删除/垃圾箱（超管+客服主管）
  deleteSession: (id: number) =>
    client.delete(`/api/chat/sessions/${id}`).then((r) => r.data),
  bulkDeleteSessions: (ids: number[]) =>
    client.post(`/api/chat/sessions/bulk-delete`, { ids }).then((r) => r.data),
  getTrashSessions: (page = 1, pageSize = 20, keyword?: string) =>
    client
      .get<{
        data: ChatSessionDto[];
        currentPage: number;
        totalPages: number;
        totalItems: number;
        pageSize: number;
      }>('/api/chat/sessions/trash', { params: { page, pageSize, keyword } })
      .then((r) => r.data),
  restoreSession: (id: number) =>
    client.post<ChatSessionDto>(`/api/chat/sessions/${id}/restore`).then((r) => r.data),
  hardDeleteSession: (id: number) =>
    client.delete(`/api/chat/sessions/${id}/hard`).then((r) => r.data),
  clearTrash: () =>
    client.delete<{ count: number }>('/api/chat/sessions/trash').then((r) => r.data),
};

// ====== 日历 API ======
export const calendarApi = {
  list: (params?: CalendarEventQuery) =>
    client
      .get<CalendarPagedResponse>('/api/calendar', {
        params: {
          startDate: params?.startDate || undefined,
          endDate: params?.endDate || undefined,
          mailboxId: params?.mailboxId ?? undefined,
          userId: params?.userId ?? undefined,
          eventType: params?.eventType || undefined,
          status: params?.status || undefined,
          keyword: params?.keyword || undefined,
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 50,
          expandRecurring: params?.expandRecurring,
          sortBy: params?.sortBy || undefined,
          sortDir: params?.sortDir || undefined,
        },
      })
      .then((r) => r.data),
  get: (id: number) =>
    client.get<CalendarEventDto>(`/api/calendar/${id}`).then((r) => r.data),
  overview: () =>
    client.get<CalendarOverviewResponse>('/api/calendar/overview').then((r) => r.data),
  create: (data: CreateCalendarEventRequest) =>
    client.post<CalendarEventDto>('/api/calendar', data).then((r) => r.data),
  update: (id: number, data: UpdateCalendarEventRequest) =>
    client.put<CalendarEventDto>(`/api/calendar/${id}`, data, {
      headers: data?.ifMatch ? { 'If-Match': data.ifMatch } : undefined,
    }).then((r) => r.data),
  remove: (id: number, options?: { deleteMode?: 'single' | 'following' | 'all'; rowVersion?: string | null }) =>
    client.delete<boolean>(`/api/calendar/${id}`, {
      params: { deleteMode: options?.deleteMode ?? 'single' },
      headers: options?.rowVersion ? { 'If-Match': options.rowVersion } : undefined,
    }).then((r) => r.data),
  updateStatus: (id: number, status: string, rowVersion?: string | null) =>
    client.post<CalendarEventDto>(
      `/api/calendar/${id}/status?status=${encodeURIComponent(status)}`,
      {},
      { headers: rowVersion ? { 'If-Match': rowVersion } : undefined },
    ).then((r) => r.data),
  duplicate: (id: number, data?: DuplicateCalendarEventRequest) =>
    client.post<CalendarEventDto>(`/api/calendar/${id}/duplicate`, data ?? {}).then((r) => r.data),
  quickUpdate: (id: number, data: QuickUpdateCalendarEventRequest) =>
    client.patch<CalendarEventDto>(`/api/calendar/${id}/quick-update`, data, {
      headers: data?.ifMatch ? { 'If-Match': data.ifMatch } : undefined,
    }).then((r) => r.data),
  getTrash: (params?: { page?: number; pageSize?: number; keyword?: string }) =>
    client
      .get<CalendarPagedResponse>('/api/calendar/trash', {
        params: {
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 50,
          keyword: params?.keyword || undefined,
        },
      })
      .then((r) => r.data),
  getTrashCount: () =>
    client.get<CalendarTrashCountResponse>('/api/calendar/trash/count').then((r) => r.data),
  restore: (id: number, rowVersion?: string | null) =>
    client.post<CalendarEventDto>(
      `/api/calendar/${id}/restore`,
      {},
      { headers: rowVersion ? { 'If-Match': rowVersion } : undefined },
    ).then((r) => r.data),
  hardDelete: (id: number, rowVersion?: string | null) =>
    client.delete<boolean>(`/api/calendar/${id}/hard`, {
      headers: rowVersion ? { 'If-Match': rowVersion } : undefined,
    }).then((r) => r.data),
  clearTrash: () =>
    client.post<{ cleared: number }>('/api/calendar/trash/clear').then((r) => r.data),
  purgeTrash: (retentionDays = 30) =>
    client
      .post<CalendarPurgeResult>(`/api/calendar/trash/purge?retentionDays=${retentionDays}`)
      .then((r) => r.data),
  exportEvent: (id: number) =>
    client
      .get<Blob>(`/api/calendar/${id}/export`, { responseType: 'blob' })
      .then((r) => r.data),
  exportRange: (startDate: string, endDate: string, mailboxId?: number) =>
    client
      .get<Blob>('/api/calendar/export', {
        params: { startDate, endDate, mailboxId },
        responseType: 'blob',
      })
      .then((r) => r.data),
  importEvents: (file: File, mailboxId?: number) => {
    const formData = new FormData();
    formData.append('file', file);
    return client
      .post<CalendarImportResult | { data: CalendarImportResult }>(
        '/api/calendar/import',
        formData,
        {
          params: { mailboxId },
          headers: { 'Content-Type': 'multipart/form-data' },
        },
      )
      .then((r) => {
        const raw = r.data;
        if (raw && typeof raw === 'object' && 'data' in raw && raw.data && typeof raw.data === 'object') {
          return raw.data as CalendarImportResult;
        }
        return raw as CalendarImportResult;
      });
  },

  getSubscription: () =>
    client
      .get<{ url?: string } | { data?: { url?: string } }>('/api/calendar/subscription')
      .then((r) => {
        const d = r.data;
        const unwrapped = (d && typeof d === 'object' && 'data' in d && d.data) ? d.data : d;
        return unwrapped as { url?: string };
      }),

  // 重新生成订阅令牌（使旧链接立即失效）
  regenerateSubscription: () =>
    client
      .post<{ url?: string } | { data?: { url?: string } }>('/api/calendar/subscription/regenerate')
      .then((r) => {
        const d = r.data;
        const unwrapped = (d && typeof d === 'object' && 'data' in d && d.data) ? d.data : d;
        return unwrapped as { url?: string };
      }),

  // P1-3: 事件模板
  listTemplates: (mailboxId?: number) =>
    client
      .get<CalendarEventTemplateListResponse>('/api/calendar/templates', { params: { mailboxId: mailboxId ?? undefined } })
      .then((r) => r.data),
  getTemplate: (id: number) =>
    client.get<CalendarEventTemplateDto>(`/api/calendar/templates/${id}`).then((r) => r.data),
  createTemplate: (data: CreateCalendarEventTemplateRequest) =>
    client.post<CalendarEventTemplateDto>('/api/calendar/templates', data).then((r) => r.data),
  updateTemplate: (id: number, data: UpdateCalendarEventTemplateRequest) =>
    client.put<CalendarEventTemplateDto>(`/api/calendar/templates/${id}`, data).then((r) => r.data),
  deleteTemplate: (id: number) =>
    client.delete<boolean>(`/api/calendar/templates/${id}`).then((r) => r.data),

  // P1-2: 批量操作
  batchOperation: (data: CalendarBatchOperationRequest) =>
    client.post<CalendarBatchOperationResponse>('/api/calendar/batch', data).then((r) => r.data),

  // P2-A#5 #7.3：用户在提醒弹层上确认已收到，避免 5 分钟兜底邮件/IM
  confirmReminder: (id: number, mode: 'user_click' | 'timeout_auto_close' = 'user_click', rowVersion?: string | null) =>
    client.post<{ eventId: number; mode: string; confirmed: boolean }>(
      `/api/calendar/${id}/reminder-confirm?mode=${encodeURIComponent(mode)}`,
      {},
      { headers: rowVersion ? { 'If-Match': rowVersion } : undefined },
    ).then((r) => r.data),
};

// ====== Outlook 双向同步 API ======
export const outlookSyncApi = {
  getStatus: () =>
    client.get<OutlookSyncStatusDto>('/api/outlooksync/status').then((r) => r.data),
  getSetting: () =>
    client.get<OutlookSyncSettingDto>('/api/outlooksync/setting').then((r) => r.data),
  updateSetting: (data: UpdateOutlookSyncSettingRequest) =>
    client.put<OutlookSyncSettingDto>('/api/outlooksync/setting', data).then((r) => r.data),
  getAuthorizeUrl: () =>
    client
      .get<{ authorizeUrl: string; state: string }>('/api/outlooksync/oauth/authorize')
      .then((r) => r.data),
  syncNow: (force = false) =>
    client
      .post<OutlookSyncResultDto>(`/api/outlooksync/sync?force=${force}`)
      .then((r) => r.data),
  disconnect: () =>
    client.delete<boolean>('/api/outlooksync/disconnect').then((r) => r.data),
  // T000099: 用户级 Azure AD 凭据管理（每个用户可自配置自己的凭据，空值回退到 appsettings 基线）
  getCredentials: () =>
    client.get<OutlookAdminSettingsDto>('/api/outlooksync/credentials').then((r) => r.data),
  updateCredentials: (data: UpdateOutlookAdminSettingsRequest) =>
    client.put<OutlookAdminSettingsDto>('/api/outlooksync/credentials', data).then((r) => r.data),
};
