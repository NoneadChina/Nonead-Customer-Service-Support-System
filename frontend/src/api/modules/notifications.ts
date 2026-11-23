import client from '../client';
import type {
  NotificationTemplateResponse,
  NotificationTemplateOverviewResponse,
  NotificationRuleResponse,
  NotificationRuleOverviewResponse,
  CreateNotificationTemplateRequest,
  UpdateNotificationTemplateRequest,
  CreateNotificationRuleRequest,
  UpdateNotificationRuleRequest,
  SendNotificationRequest,
  NotificationSendResultResponse,
  PreviewTemplateResponse,
  TemplateVariableValidateResponse,
  NotificationTemplatePreviewResponse,
} from '../types';

// ====== 通知 API ======
export const notificationApi = {
  // 模板
  getTemplateOverview: () =>
    client.get<NotificationTemplateOverviewResponse>('/api/notifications/templates/overview').then((r) => r.data),
  listTemplates: (params?: {
    page?: number;
    pageSize?: number;
    eventType?: string;
    isActive?: boolean;
    search?: string;
  }) =>
    client
      .get<{ data: NotificationTemplateResponse[]; totalItems: number; currentPage: number; pageSize: number; totalPages: number }>(
        '/api/notifications/templates',
        { params },
      )
      .then((r) => r.data),
  listAllTemplates: () =>
    client.get<NotificationTemplateResponse[]>('/api/notifications/templates/all').then((r) => r.data ?? []),
  getTemplate: (id: number) =>
    client.get<NotificationTemplateResponse>(`/api/notifications/templates/${id}`).then((r) => r.data),
  createTemplate: (data: CreateNotificationTemplateRequest) =>
    client.post<NotificationTemplateResponse>('/api/notifications/templates', data).then((r) => r.data),
  updateTemplate: (id: number, data: UpdateNotificationTemplateRequest) =>
    client.put<NotificationTemplateResponse>(`/api/notifications/templates/${id}`, data).then((r) => r.data),
  removeTemplate: (id: number) =>
    client.delete<boolean>(`/api/notifications/templates/${id}`).then((r) => r.data),
  toggleTemplateActive: (id: number) =>
    client.post<NotificationTemplateResponse>(`/api/notifications/templates/${id}/toggle`).then((r) => r.data),
  duplicateTemplate: (id: number, newName?: string) =>
    client.post<NotificationTemplateResponse>(`/api/notifications/templates/${id}/duplicate`, { newName }).then((r) => r.data),
  validateTemplateVariables: (subject: string, body: string) =>
    client.post<TemplateVariableValidateResponse>('/api/notifications/templates/validate-variables', { subject, body }).then((r) => r.data),
  // 模板 - 垃圾箱
  listTemplateTrash: (params?: { page?: number; pageSize?: number; search?: string }) =>
    client
      .get<{ data: NotificationTemplateResponse[]; totalItems: number; currentPage: number; pageSize: number; totalPages: number }>(
        '/api/notifications/templates/trash',
        { params },
      )
      .then((r) => r.data),
  getTemplateTrashCount: () =>
    client.get<{ count: number }>('/api/notifications/templates/trash/count').then((r) => r.data),
  restoreTemplate: (id: number) =>
    client.post<NotificationTemplateResponse>(`/api/notifications/templates/${id}/restore`).then((r) => r.data),
  hardDeleteTemplate: (id: number) =>
    client.delete<boolean>(`/api/notifications/templates/${id}/hard`).then((r) => r.data),
  clearTemplateTrash: () =>
    client.delete<{ count: number }>('/api/notifications/templates/trash').then((r) => r.data),
  // 规则
  getRuleOverview: () =>
    client.get<NotificationRuleOverviewResponse>('/api/notifications/rules/overview').then((r) => r.data),
  listRules: (params?: {
    page?: number;
    pageSize?: number;
    eventType?: string;
    mailboxId?: number;
    userId?: number;
    isActive?: boolean;
    search?: string;
  }) =>
    client
      .get<{ data: NotificationRuleResponse[]; totalItems: number; currentPage: number; pageSize: number; totalPages: number }>(
        '/api/notifications/rules',
        { params },
      )
      .then((r) => r.data),
  listAllRules: () =>
    client.get<NotificationRuleResponse[]>('/api/notifications/rules/all').then((r) => r.data ?? []),
  getRulesByEventType: (eventType: string) =>
    client.get<NotificationRuleResponse[]>(`/api/notifications/rules/type/${eventType}`).then((r) => r.data ?? []),
  getRule: (id: number) =>
    client.get<NotificationRuleResponse>(`/api/notifications/rules/${id}`).then((r) => r.data),
  createRule: (data: CreateNotificationRuleRequest) =>
    client.post<NotificationRuleResponse>('/api/notifications/rules', data).then((r) => r.data),
  updateRule: (id: number, data: UpdateNotificationRuleRequest) =>
    client.put<NotificationRuleResponse>(`/api/notifications/rules/${id}`, data).then((r) => r.data),
  removeRule: (id: number) =>
    client.delete<boolean>(`/api/notifications/rules/${id}`).then((r) => r.data),
  toggleRuleActive: (id: number) =>
    client.post<NotificationRuleResponse>(`/api/notifications/rules/${id}/toggle`).then((r) => r.data),
  duplicateRule: (id: number, newName?: string) =>
    client.post<NotificationRuleResponse>(`/api/notifications/rules/${id}/duplicate`, { newName }).then((r) => r.data),
  // 规则 - 垃圾箱
  listRuleTrash: (params?: { page?: number; pageSize?: number; search?: string }) =>
    client
      .get<{ data: NotificationRuleResponse[]; totalItems: number; currentPage: number; pageSize: number; totalPages: number }>(
        '/api/notifications/rules/trash',
        { params },
      )
      .then((r) => r.data),
  getRuleTrashCount: () =>
    client.get<{ count: number }>('/api/notifications/rules/trash/count').then((r) => r.data),
  restoreRule: (id: number) =>
    client.post<NotificationRuleResponse>(`/api/notifications/rules/${id}/restore`).then((r) => r.data),
  hardDeleteRule: (id: number) =>
    client.delete<boolean>(`/api/notifications/rules/${id}/hard`).then((r) => r.data),
  clearRuleTrash: () =>
    client.delete<{ count: number }>('/api/notifications/rules/trash').then((r) => r.data),
  // 发送与预览
  send: (data: SendNotificationRequest) =>
    client.post<NotificationSendResultResponse>('/api/notifications/send', data).then((r) => r.data),
  preview: (template: string, variables: Record<string, string>, subject?: string) =>
    client
      .post<PreviewTemplateResponse>('/api/notifications/preview', { template, variables, subject })
      .then((r) => r.data),
  previewTemplateById: (id: number, customVariables: Record<string, string> = {}) =>
    client
      .post<NotificationTemplatePreviewResponse>(`/api/notifications/templates/${id}/preview`, { customVariables })
      .then((r) => r.data),
};
