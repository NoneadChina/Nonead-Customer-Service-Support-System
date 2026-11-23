import client from '../client';
import { sanitizeFileName } from '../../utils/format';
import type {
  DashboardResponse,
  ConversationResponse,
  ThreadResponse,
  AttachmentResponse,
  UserResponse,
  MailboxResponse,
  CreateMailboxRequest,
  CustomerResponse,
  CreateCustomerRequest,
  PagedResponse,
  TagResponse,
  CreateTagRequest,
  UpdateTagRequest,
  CreateUserRequest,
  BulkOperationResponse,
  UpdateProfileRequest,
  ChangePasswordRequest,
  UserNotificationSettingsResponse,
  UserNotificationSettingsRequest,
  FolderResponse,
  CreateFolderRequest,
  UpdateFolderRequest,
  GlobalSearchResponse,
  SearchEntityType,
} from '../types';

// ====== 仪表盘 API ======
export const dashboardApi = {
  getStats: () =>
    client.get<DashboardResponse>('/api/dashboard/stats').then((r) => r.data),
};

// ====== 会话 API ======
export const conversationApi = {
  list: (params?: { mailboxId?: number; folderId?: number; page?: number; pageSize?: number; search?: string; status?: number; priority?: number; starred?: boolean; customerId?: number; sourceType?: number }) =>
    client.get<PagedResponse<ConversationResponse>>('/api/conversations', { params }).then((r) => r.data),

  get: (id: number) =>
    client.get<ConversationResponse>(`/api/conversations/${id}`).then((r) => r.data),

  getThreads: (id: number) =>
    client.get<ThreadResponse[]>(`/api/conversations/${id}/threads`).then((r) => r.data ?? []),

  reply: (id: number, body: string, from: string, attachmentIds?: number[], cc?: string, bcc?: string) =>
    client.post<ThreadResponse>(`/api/conversations/${id}/reply`, { body, from, attachmentIds, cc, bcc }).then((r) => r.data),

  saveDraft: (id: number, body: string, from: string, attachmentIds?: number[], cc?: string, bcc?: string) =>
    client.post<ThreadResponse>(`/api/conversations/${id}/draft`, { body, from, attachmentIds, cc, bcc }).then((r) => r.data),

  getDraft: (id: number) =>
    client.get<ThreadResponse>(`/api/conversations/${id}/draft`).then((r) => r.data),

  deleteDraft: (id: number) =>
    client.delete<boolean>(`/api/conversations/${id}/draft`).then((r) => r.data),

  getMyDrafts: () =>
    client.get<ThreadResponse[]>('/api/conversations/drafts').then((r) => r.data ?? []),

  updateStatus: (id: number, status: number) =>
    client.put<ConversationResponse>(`/api/conversations/${id}/status`, { status }).then((r) => r.data),

  updatePriority: (id: number, priority: number) =>
    client.put<ConversationResponse>(`/api/conversations/${id}/priority`, { priority }).then((r) => r.data),

  assign: (id: number, userId: number) =>
    client.put<ConversationResponse>(`/api/conversations/${id}/assign`, { userId }).then((r) => r.data),

  toggleFollow: (id: number) =>
    client.post<ConversationResponse>(`/api/conversations/${id}/follow`).then((r) => r.data),

  getFollowers: (id: number) =>
    client.get<number[]>(`/api/conversations/${id}/followers`).then((r) => r.data ?? []),

  addNote: (id: number, body: string, attachmentIds?: number[]) =>
    client.post<ThreadResponse>(`/api/conversations/${id}/notes`, { body, attachmentIds }).then((r) => r.data),

  remove: (id: number) =>
    client.delete<boolean>(`/api/conversations/${id}`).then((r) => r.data),

  bulkAssign: (conversationIds: number[], userId: number) =>
    client.post<BulkOperationResponse>('/api/conversations/bulk/assign', { conversationIds, userId }).then((r) => r.data),

  bulkUpdateStatus: (conversationIds: number[], status: number) =>
    client.post<BulkOperationResponse>('/api/conversations/bulk/status', { conversationIds, status }).then((r) => r.data),

  bulkUpdatePriority: (conversationIds: number[], priority: number) =>
    client.post<BulkOperationResponse>('/api/conversations/bulk/priority', { conversationIds, priority }).then((r) => r.data),

  bulkRemove: (conversationIds: number[]) =>
    client.post<BulkOperationResponse>('/api/conversations/bulk/delete', { conversationIds }).then((r) => r.data),

  clone: (id: number) =>
    client.post<ConversationResponse>(`/api/conversations/${id}/clone`).then((r) => r.data),

  /** 标记会话为已读（清除列表页未读徽章） */
  markRead: (id: number) =>
    client.post<void>(`/api/conversations/${id}/mark-read`).then((r) => r.data),

  // 垃圾箱（仅针对具体邮箱）
  getTrash: (mailboxId: number, page = 1, pageSize = 20) =>
    client
      .get<PagedResponse<ConversationResponse>>('/api/conversations/trash', { params: { mailboxId, page, pageSize } })
      .then((r) => r.data),
  restore: (id: number) =>
    client.post<ConversationResponse>(`/api/conversations/${id}/restore`).then((r) => r.data),
  hardDelete: (id: number) =>
    client.delete<boolean>(`/api/conversations/${id}/hard`).then((r) => r.data),
  clearTrash: (mailboxId: number) =>
    client.delete<{ count: number }>('/api/conversations/trash', { params: { mailboxId } }).then((r) => r.data),

  // ====== 关注会话 ======
  /** 获取当前用户关注的会话 ID 列表 */
  getStarredIds: () =>
    client.get<number[]>('/api/conversations/starred-ids').then((r) => r.data ?? []),
};

// ====== 用户 API ======
export const userApi = {
  list: (params?: { page?: number; pageSize?: number; keyword?: string; status?: number }) =>
    client
      .get<{ data: UserResponse[]; totalItems: number; totalPages: number; currentPage: number; pageSize: number }>('/api/users', { params })
      .then((r) => r.data),
  /**
   * 获取可被分配会话的活跃用户列表（不分页）。
   * 受 conversations.assign 权限保护，客服主管/客服无需 SuperAdmin 即可调用，
   * 用于会话/工单的"分配负责人"下拉框。
   */
  assignable: () =>
    client.get<UserResponse[]>('/api/users/assignable').then((r) => r.data ?? []),
  stats: () =>
    client.get<{ total: number; active: number; disabled: number; pending: number }>('/api/users/stats').then((r) => r.data),
  get: (id: number) => client.get<UserResponse>(`/api/users/${id}`).then((r) => r.data),
  create: (data: CreateUserRequest) =>
    client.post<UserResponse>('/api/users', data).then((r) => r.data),
  update: (id: number, data: CreateUserRequest) =>
    client.put<UserResponse>(`/api/users/${id}`, data).then((r) => r.data),
  remove: (id: number) => client.delete<boolean>(`/api/users/${id}`).then((r) => r.data),
  activate: (id: number) => client.post<boolean>(`/api/users/${id}/activate`).then((r) => r.data),
  updateStatus: (id: number, status: number) =>
    client.post<boolean>(`/api/users/${id}/status`, { status }).then((r) => r.data),
  getTrash: () => client.get<UserResponse[]>('/api/users/trash').then((r) => r.data ?? []),
  restore: (id: number) => client.post<UserResponse>(`/api/users/${id}/restore`).then((r) => r.data),
  hardDelete: (id: number) => client.delete<boolean>(`/api/users/${id}/hard`).then((r) => r.data),
  clearTrash: () => client.delete<{ count: number }>('/api/users/trash').then((r) => r.data),
};

// ====== 邮箱 API ======
export const mailboxApi = {
  list: () => client.get<MailboxResponse[]>('/api/mailboxes').then((r) => r.data ?? []),
  listAll: () => client.get<MailboxResponse[]>('/api/mailboxes/all').then((r) => r.data ?? []),
  get: (id: number) => client.get<MailboxResponse>(`/api/mailboxes/${id}`).then((r) => r.data),
  create: (data: CreateMailboxRequest) =>
    client.post<MailboxResponse>('/api/mailboxes', data).then((r) => r.data),
  update: (id: number, data: CreateMailboxRequest) =>
    client.put<MailboxResponse>(`/api/mailboxes/${id}`, data).then((r) => r.data),
  remove: (id: number) => client.delete<boolean>(`/api/mailboxes/${id}`).then((r) => r.data),
  testConnection: (id: number) =>
    client.get<{ success: boolean; message?: string }>(`/api/mailboxes/${id}/test-connection`).then((r) => r.data),
  getTrash: () => client.get<MailboxResponse[]>('/api/mailboxes/trash').then((r) => r.data ?? []),
  restore: (id: number) => client.post<MailboxResponse>(`/api/mailboxes/${id}/restore`).then((r) => r.data),
  hardDelete: (id: number) => client.delete<boolean>(`/api/mailboxes/${id}/hard`).then((r) => r.data),
  clearTrash: () => client.delete<{ count: number }>('/api/mailboxes/trash').then((r) => r.data),
  toggleActive: (id: number) => client.post<boolean>(`/api/mailboxes/${id}/toggle-active`).then((r) => r.data),
};

// ====== 客户 API ======
export const customerApi = {
  list: (params?: { search?: string; page?: number; pageSize?: number }) =>
    client
      .get<PagedResponse<CustomerResponse>>('/api/customers', { params })
      .then((r) => r.data),
  get: (id: number) => client.get<CustomerResponse>(`/api/customers/${id}`).then((r) => r.data),
  search: (keyword: string, page = 1, pageSize = 20) =>
    client
      .get<PagedResponse<CustomerResponse>>('/api/customers', { params: { search: keyword, page, pageSize } })
      .then((r) => r.data),
  create: (data: CreateCustomerRequest) =>
    client.post<CustomerResponse>('/api/customers', data).then((r) => r.data),
  update: (id: number, data: CreateCustomerRequest) =>
    client.put<CustomerResponse>(`/api/customers/${id}`, data).then((r) => r.data),
  remove: (id: number) => client.delete<boolean>(`/api/customers/${id}`).then((r) => r.data),
  getTrash: (page = 1, pageSize = 20, search = '') =>
    client
      .get<PagedResponse<CustomerResponse>>('/api/customers/trash', { params: { page, pageSize, search } })
      .then((r) => r.data),
  restore: (id: number) => client.post<CustomerResponse>(`/api/customers/${id}/restore`).then((r) => r.data),
  getHardDeleteStatus: (id: number) =>
    client.get<{ activeCount: number; trashCount: number }>(`/api/customers/${id}/hard-delete-status`).then((r) => r.data),
  hardDelete: (id: number) => client.delete<boolean>(`/api/customers/${id}/hard`).then((r) => r.data),
  getClearTrashStatus: () =>
    client
      .get<{ customerCount: number; totalActiveCount: number; totalTrashCount: number }>(
        '/api/customers/trash/clear-status'
      )
      .then((r) => r.data),
  clearTrash: () => client.delete<{ count: number }>('/api/customers/trash').then((r) => r.data),
  bulkRemove: (ids: number[]) =>
    client.post<BulkOperationResponse>('/api/customers/bulk-delete', { ids }).then((r) => r.data),
  exportCsv: (ids?: number[]) =>
    client.get<Blob>('/api/customers/export', {
      params: ids && ids.length > 0 ? { ids: ids.join(',') } : {},
      responseType: 'blob',
    }).then((r) => r.data),
};

// ====== 标签 API ======
export const tagApi = {
  list: (mailboxId?: number) =>
    client
      .get<TagResponse[]>('/api/tags', { params: mailboxId ? { mailboxId } : {} })
      .then((r) => r.data ?? []),
  get: (id: number) => client.get<TagResponse>(`/api/tags/${id}`).then((r) => r.data),
  create: (data: CreateTagRequest) =>
    client.post<TagResponse>('/api/tags', data).then((r) => r.data),
  update: (id: number, data: UpdateTagRequest) =>
    client.put<TagResponse>(`/api/tags/${id}`, data).then((r) => r.data),
  remove: (id: number) => client.delete<boolean>(`/api/tags/${id}`).then((r) => r.data),
  getTrash: (page = 1, pageSize = 20, search = '') =>
    client
      .get<PagedResponse<TagResponse>>('/api/tags/trash', { params: { page, pageSize, search } })
      .then((r) => r.data),
  getTrashCount: () => client.get<{ count: number }>('/api/tags/trash/count').then((r) => r.data),
  restore: (id: number) => client.post<TagResponse>(`/api/tags/${id}/restore`).then((r) => r.data),
  hardDelete: (id: number) => client.delete<boolean>(`/api/tags/${id}/hard`).then((r) => r.data),
  clearTrash: () => client.delete<{ count: number }>('/api/tags/trash').then((r) => r.data),
  getConversationTags: (conversationId: number) =>
    client.get<TagResponse[]>(`/api/tags/conversations/${conversationId}`).then((r) => r.data ?? []),
  updateConversationTags: (conversationId: number, tagIds: number[]) =>
    client.put<TagResponse[]>(`/api/tags/conversations/${conversationId}`, { tagIds }).then((r) => r.data ?? []),
  addTagToConversation: (conversationId: number, tagId: number) =>
    client.post<TagResponse[]>(`/api/tags/conversations/${conversationId}/tags/${tagId}`).then((r) => r.data ?? []),
  removeTagFromConversation: (conversationId: number, tagId: number) =>
    client.delete<TagResponse[]>(`/api/tags/conversations/${conversationId}/tags/${tagId}`).then((r) => r.data ?? []),
  bulkUpdateConversationTags: (conversationIds: number[], tagIds: number[]) =>
    client.post<BulkOperationResponse>('/api/tags/conversations/bulk', { conversationIds, tagIds }).then((r) => r.data),
};

// ====== 个人资料 API ======
export interface ProfileStats {
  conversationCount: number;
  replyCount: number;
  onlineMinutes: number;
}

export const profileApi = {
  get: () => client.get<UserResponse>('/api/profile').then((r) => r.data),
  update: (data: UpdateProfileRequest) =>
    client.put<UserResponse>('/api/profile', data).then((r) => r.data),
  changePassword: (data: ChangePasswordRequest) =>
    client.post<boolean>('/api/profile/change-password', data).then((r) => r.data),
  uploadAvatar: (file: File) => {
    const fd = new FormData();
    fd.append('file', file);
    return client.post<{ url: string; user: UserResponse }>('/api/profile/avatar', fd, {
      headers: { 'Content-Type': 'multipart/form-data' },
    }).then((r) => r.data);
  },
  deleteAvatar: () =>
    client.delete<{ user: UserResponse }>('/api/profile/avatar').then((r) => r.data),
  getStats: () => client.get<ProfileStats>('/api/profile/stats').then((r) => r.data),
  getNotificationSettings: () =>
    client.get<UserNotificationSettingsResponse>('/api/user-profile/me/notifications').then((r) => r.data),
  updateNotificationSettings: (data: UserNotificationSettingsRequest) =>
    client.put<UserNotificationSettingsResponse>('/api/user-profile/me/notifications', data).then((r) => r.data),
};

// ====== 附件 API ======
export const attachmentApi = {
  downloadUrl: (id: number) => `/api/attachments/${id}/download`,
  previewUrl: (id: number, maxWidth = 300, maxHeight = 300) =>
    `/api/attachments/${id}/preview?maxWidth=${maxWidth}&maxHeight=${maxHeight}`,
  get: (id: number) =>
    client.get<AttachmentResponse>(`/api/attachments/${id}`).then((r) => r.data),
  getByConversation: (conversationId: number) =>
    client.get<AttachmentResponse[]>(`/api/attachments/conversation/${conversationId}`).then((r) => r.data ?? []),
  getByThread: (threadId: number) =>
    client.get<AttachmentResponse[]>(`/api/attachments/thread/${threadId}`).then((r) => r.data ?? []),
  upload: (file: File, threadId?: number) => {
    const fd = new FormData();
    // M-FE6：统一净化文件名，防止非法字符导致后端路径问题
    const safeFile = /[\\/:*?"<>|]/.test(file.name)
      ? new File([file], sanitizeFileName(file.name), { type: file.type, lastModified: file.lastModified })
      : file;
    fd.append('file', safeFile);
    const params: Record<string, unknown> = {};
    if (threadId) params.threadId = threadId;
    return client.post<AttachmentResponse>('/api/attachments/upload', fd, {
      params,
      headers: { 'Content-Type': 'multipart/form-data' },
    }).then((r) => r.data);
  },
  uploadBatch: (files: File[], threadId?: number) => {
    const fd = new FormData();
    // M-FE6：统一净化每个文件名
    files.forEach((f) => {
      const safeFile = /[\\/:*?"<>|]/.test(f.name)
        ? new File([f], sanitizeFileName(f.name), { type: f.type, lastModified: f.lastModified })
        : f;
      fd.append('files', safeFile);
    });
    const params: Record<string, unknown> = {};
    if (threadId) params.threadId = threadId;
    return client.post<{ data: AttachmentResponse[]; count: number }>(
      '/api/attachments/upload/batch',
      fd,
      { params, headers: { 'Content-Type': 'multipart/form-data' } }
    ).then((r) => r.data);
  },
  delete: (id: number) => client.delete<boolean>(`/api/attachments/${id}`).then((r) => r.data),
};

// ====== 文件夹 API ======
export const folderApi = {
  getByMailbox: (mailboxId: number) =>
    client.get<FolderResponse[]>(`/api/folders/mailbox/${mailboxId}`).then((r) => r.data ?? []),
  getByUser: (userId: number) =>
    client.get<FolderResponse[]>(`/api/folders/user/${userId}`).then((r) => r.data ?? []),
  get: (id: number) =>
    client.get<FolderResponse>(`/api/folders/${id}`).then((r) => r.data),
  create: (data: CreateFolderRequest) =>
    client.post<FolderResponse>('/api/folders', data).then((r) => r.data),
  update: (id: number, data: UpdateFolderRequest) =>
    client.put<FolderResponse>(`/api/folders/${id}`, data).then((r) => r.data),
  delete: (id: number) =>
    client.delete<boolean>(`/api/folders/${id}`).then((r) => r.data),
  refreshCounters: (mailboxId: number) =>
    client.post<{ affected: number; folders: FolderResponse[] }>(`/api/folders/mailbox/${mailboxId}/refresh-counters`).then((r) => r.data),
  ensureSystem: (mailboxId: number) =>
    client.post<{ created: number; total: number }>(`/api/folders/mailbox/${mailboxId}/ensure-system`).then((r) => r.data),
};

// ====== 全局搜索 API ======
export const searchApi = {
  global: (q: string, entity: SearchEntityType = 'all', page = 1, pageSize = 20) =>
    client.get<GlobalSearchResponse>('/api/search', { params: { q, entity, page, pageSize } }).then((r) => r.data),
};
