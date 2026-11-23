import client from '../client';
import type {
  WorkflowDto,
  WorkflowOverviewResponse,
  WorkflowQuery,
  CreateWorkflowRequest,
  UpdateWorkflowRequest,
  WorkflowTestRequest,
  WorkflowTestResponse,
  WorkflowExecutionLogQuery,
  SlaPolicyDto,
  SlaViolationDto,
  SlaOverviewResponse,
  CreateSlaPolicyRequest,
  UpdateSlaPolicyRequest,
  SlaConversationStatusResponse,
  SlaPolicyQuery,
  SlaViolationQuery,
  MacroDto,
  MacroOverviewResponse,
  CreateMacroRequest,
  UpdateMacroRequest,
  ExecuteMacroResponse,
  MacroQuery,
  MacroExecutionLogQuery,
  MacroPreviewResponse,
  CustomFieldDto,
  CustomFieldValueDto,
  CustomFieldOverviewResponse,
  CustomFieldChannelDto,
  CreateCustomFieldRequest,
  UpdateCustomFieldRequest,
  CustomFieldQuery,
  WebhookDto,
  WebhookLogDto,
  WebhookOverviewResponse,
  CreateWebhookRequest,
  UpdateWebhookRequest,
  TestWebhookRequest,
  TestWebhookResponse,
} from '../types';

// ====== 工作流自动化规则 API ======
export const workflowApi = {
  list: (params?: WorkflowQuery) =>
    client
      .get('/api/workflows', {
        params: {
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 20,
          mailboxId: params?.mailboxId ?? undefined,
          isActive: params?.isActive ?? undefined,
          triggerType: params?.triggerType || undefined,
          keyword: params?.keyword || undefined,
        },
      })
      .then((r) => r.data),
  get: (id: number) =>
    client.get<WorkflowDto>(`/api/workflows/${id}`).then((r) => r.data),
  overview: () =>
    client.get<WorkflowOverviewResponse>('/api/workflows/overview').then((r) => r.data),
  executionLogs: (params?: WorkflowExecutionLogQuery) =>
    client
      .get('/api/workflows/execution-logs', {
        params: {
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 20,
          workflowId: params?.workflowId ?? undefined,
          conversationId: params?.conversationId ?? undefined,
          status: params?.status || undefined,
          startDate: params?.startDate || undefined,
          endDate: params?.endDate || undefined,
        },
      })
      .then((r) => r.data),
  create: (data: CreateWorkflowRequest) =>
    client.post<WorkflowDto>('/api/workflows', data).then((r) => r.data),
  update: (id: number, data: UpdateWorkflowRequest) =>
    client.put<WorkflowDto>(`/api/workflows/${id}`, data).then((r) => r.data),
  remove: (id: number) =>
    client.delete<void>(`/api/workflows/${id}`).then((r) => r.data),
  toggleActive: (id: number) =>
    client.post<void>(`/api/workflows/${id}/toggle-active`).then((r) => r.data),
  test: (id: number, data: WorkflowTestRequest) =>
    client.post<WorkflowTestResponse>(`/api/workflows/${id}/test`, data).then((r) => r.data),
  execute: (conversationId: number, triggerType: string) =>
    client
      .post<void>(`/api/workflows/execute/${conversationId}/${triggerType}`)
      .then((r) => r.data),
  getTrash: (page = 1, pageSize = 20, mailboxId?: number, keyword?: string) =>
    client
      .get('/api/workflows/trash', {
        params: { page, pageSize, mailboxId: mailboxId ?? undefined, keyword: keyword || undefined },
      })
      .then((r) => r.data),
  getTrashCount: () =>
    client.get<{ count: number }>('/api/workflows/trash/count').then((r) => r.data),
  restore: (id: number) =>
    client.post<void>(`/api/workflows/${id}/restore`).then((r) => r.data),
  hardDelete: (id: number) =>
    client.delete<void>(`/api/workflows/${id}/hard`).then((r) => r.data),
  clearTrash: (mailboxId?: number) =>
    client.delete<{ count: number }>('/api/workflows/trash', { params: { mailboxId: mailboxId ?? undefined } }).then((r) => r.data),
};

// ====== SLA 策略 API ======
export const slaApi = {
  list: (params?: SlaPolicyQuery) =>
    client
      .get<{ data: SlaPolicyDto[]; currentPage: number; totalPages: number; totalItems: number; pageSize: number }>('/api/sla', {
        params: {
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 20,
          mailboxId: params?.mailboxId ?? undefined,
          isActive: params?.isActive ?? undefined,
          keyword: params?.keyword || undefined,
        },
      })
      .then((r) => r.data),
  get: (id: number) =>
    client.get<SlaPolicyDto>(`/api/sla/${id}`).then((r) => r.data),
  overview: () =>
    client.get<SlaOverviewResponse>('/api/sla/overview').then((r) => r.data),
  violations: (params?: SlaViolationQuery) =>
    client
      .get<{ data: SlaViolationDto[]; currentPage: number; totalPages: number; totalItems: number; pageSize: number }>('/api/sla/violations', {
        params: {
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 20,
          policyId: params?.policyId ?? undefined,
          conversationId: params?.conversationId ?? undefined,
          violationType: params?.violationType || undefined,
          status: params?.status || undefined,
          startDate: params?.startDate || undefined,
          endDate: params?.endDate || undefined,
        },
      })
      .then((r) => r.data),
  conversationViolations: (conversationId: number) =>
    client
      .get<SlaViolationDto[]>(`/api/sla/conversations/${conversationId}/violations`)
      .then((r) => r.data),
  conversationStatus: (conversationId: number) =>
    client
      .get<SlaConversationStatusResponse>(`/api/sla/conversations/${conversationId}/status`)
      .then((r) => r.data),
  create: (data: CreateSlaPolicyRequest) =>
    client.post<SlaPolicyDto>('/api/sla', data).then((r) => r.data),
  update: (id: number, data: UpdateSlaPolicyRequest) =>
    client.put<SlaPolicyDto>(`/api/sla/${id}`, data).then((r) => r.data),
  remove: (id: number) =>
    client.delete<void>(`/api/sla/${id}`).then((r) => r.data),
  toggleActive: (id: number) =>
    client.post<void>(`/api/sla/${id}/toggle-active`).then((r) => r.data),
  scan: () =>
    client.post<{ scanned: number; newViolations: number; breached: number }>('/api/sla/scan').then((r) => r.data),
  applyToConversation: (conversationId: number) =>
    client.post<void>(`/api/sla/apply/${conversationId}`).then((r) => r.data),
  // 垃圾箱
  getTrash: (params?: { page?: number; pageSize?: number; keyword?: string }) =>
    client
      .get<{ data: SlaPolicyDto[]; currentPage: number; totalPages: number; totalItems: number; pageSize: number }>('/api/sla/trash', {
        params: {
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 20,
          keyword: params?.keyword || undefined,
        },
      })
      .then((r) => r.data),
  getTrashCount: () =>
    client.get<{ count: number }>('/api/sla/trash/count').then((r) => r.data),
  restore: (id: number) =>
    client.post<void>(`/api/sla/${id}/restore`).then((r) => r.data),
  hardDelete: (id: number) =>
    client.delete<void>(`/api/sla/${id}/hard`).then((r) => r.data),
  clearTrash: () =>
    client.delete<{ count: number }>('/api/sla/trash').then((r) => r.data),
};

// ====== 宏 API ======
export const macroApi = {
  list: (params?: MacroQuery) =>
    client
      .get('/api/macros', {
        params: {
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 20,
          mailboxId: params?.mailboxId ?? undefined,
          isActive: params?.isActive ?? undefined,
          keyword: params?.keyword || undefined,
        },
      })
      .then((r) => r.data),
  get: (id: number) =>
    client.get<MacroDto>(`/api/macros/${id}`).then((r) => r.data),
  overview: () =>
    client.get<MacroOverviewResponse>('/api/macros/overview').then((r) => r.data),
  getByMailbox: (mailboxId: number) =>
    client.get<MacroDto[]>(`/api/macros/mailbox/${mailboxId}`).then((r) => r.data),
  create: (data: CreateMacroRequest) =>
    client.post<MacroDto>('/api/macros', data).then((r) => r.data),
  update: (id: number, data: UpdateMacroRequest) =>
    client.put<MacroDto>(`/api/macros/${id}`, data).then((r) => r.data),
  remove: (id: number) =>
    client.delete<void>(`/api/macros/${id}`).then((r) => r.data),
  getTrash: (params?: { page?: number; pageSize?: number; keyword?: string }) =>
    client
      .get('/api/macros/trash', {
        params: {
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 10,
          keyword: params?.keyword || undefined,
        },
      })
      .then((r) => r.data),
  getTrashCount: () =>
    client.get<{ count: number }>('/api/macros/trash/count').then((r) => r.data),
  restore: (id: number) =>
    client.post<void>(`/api/macros/${id}/restore`).then((r) => r.data),
  hardDelete: (id: number) =>
    client.delete<void>(`/api/macros/${id}/hard`).then((r) => r.data),
  clearTrash: () =>
    client.delete<{ count: number }>('/api/macros/trash').then((r) => r.data),
  toggleActive: (id: number) =>
    client.post<void>(`/api/macros/${id}/toggle-active`).then((r) => r.data),
  duplicate: (id: number, newName?: string) =>
    client
      .post<MacroDto>(`/api/macros/${id}/duplicate`, { newName: newName ?? null })
      .then((r) => r.data),
  preview: (id: number, conversationId: number) =>
    client
      .post<MacroPreviewResponse>(`/api/macros/${id}/preview`, { conversationId })
      .then((r) => r.data),
  execute: (id: number, conversationId: number) =>
    client
      .post<ExecuteMacroResponse>(`/api/macros/${id}/execute`, { conversationId })
      .then((r) => r.data),
  executionLogs: (params?: MacroExecutionLogQuery) =>
    client
      .get('/api/macros/execution-logs', {
        params: {
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 20,
          macroId: params?.macroId ?? undefined,
          conversationId: params?.conversationId ?? undefined,
        },
      })
      .then((r) => r.data),
};

// ====== 自定义字段 API ======
export const customFieldApi = {
  list: (params?: CustomFieldQuery) =>
    client
      .get('/api/customfields', {
        params: {
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 20,
          entityType: params?.entityType || undefined,
          mailboxId: params?.mailboxId ?? undefined,
          isActive: params?.isActive ?? undefined,
          keyword: params?.keyword || undefined,
        },
      })
      .then((r) => r.data),
  get: (id: number) =>
    client.get<CustomFieldDto>(`/api/customfields/${id}`).then((r) => r.data),
  overview: () =>
    client.get<CustomFieldOverviewResponse>('/api/customfields/overview').then((r) => r.data),
  getForEntity: (entityType: string, sourceType?: string | null) =>
    client
      .get<CustomFieldDto[]>(`/api/customfields/for-entity/${entityType}`, {
        params: sourceType ? { sourceType } : undefined,
      })
      .then((r) => r.data),
  /** 获取可用渠道列表（仅返回已启用渠道 + 全局选项），用于字段管理页"适用渠道"下拉 */
  getChannels: () =>
    client.get<CustomFieldChannelDto[]>('/api/customfields/channels').then((r) => r.data ?? []),
  create: (data: CreateCustomFieldRequest) =>
    client.post<CustomFieldDto>('/api/customfields', data).then((r) => r.data),
  update: (id: number, data: UpdateCustomFieldRequest) =>
    client.put<CustomFieldDto>(`/api/customfields/${id}`, data).then((r) => r.data),
  remove: (id: number) =>
    client.delete<void>(`/api/customfields/${id}`).then((r) => r.data),
  toggleActive: (id: number) =>
    client.post<void>(`/api/customfields/${id}/toggle-active`).then((r) => r.data),
  getEntityValues: (entityType: string, entityId: number) =>
    client.get<CustomFieldValueDto[]>(`/api/customfields/values/${entityType}/${entityId}`).then((r) => r.data),
  setEntityValue: (entityType: string, entityId: number, customFieldId: number, value: string | null) =>
    client
      .put<CustomFieldValueDto>(`/api/customfields/values/${entityType}/${entityId}/${customFieldId}`, { value })
      .then((r) => r.data),
  batchSetEntityValues: (entityType: string, entityId: number, values: Record<number, string | null>) =>
    client
      .post<CustomFieldValueDto[]>('/api/customfields/values/batch', { entityType, entityId, values })
      .then((r) => r.data),
  deleteEntityValues: (entityType: string, entityId: number) =>
    client.delete<void>(`/api/customfields/values/${entityType}/${entityId}`).then((r) => r.data),
  getTrash: (page = 1, pageSize = 10, keyword = '') =>
    client
      .get<{ data: CustomFieldDto[]; totalItems: number; totalPages: number }>('/api/customfields/trash', {
        params: { page, pageSize, keyword },
      })
      .then((r) => r.data),
  restore: (id: number) => client.post<void>(`/api/customfields/${id}/restore`).then((r) => r.data),
  hardDelete: (id: number) => client.delete<void>(`/api/customfields/${id}/hard`).then((r) => r.data),
  clearTrash: () => client.delete<{ count: number }>('/api/customfields/trash').then((r) => r.data),
};

// ====== Webhook API ======
export const webhookApi = {
  list: (params?: {
    page?: number;
    pageSize?: number;
    mailboxId?: number | null;
    isActive?: boolean | null;
    keyword?: string | null;
    eventType?: string | null;
  }) =>
    client
      .get<{ data: WebhookDto[]; totalItems: number; totalPages: number }>('/api/webhooks', { params })
      .then((r) => r.data),
  get: (id: number) =>
    client.get<WebhookDto>(`/api/webhooks/${id}`).then((r) => r.data),
  overview: () =>
    client.get<WebhookOverviewResponse>('/api/webhooks/overview').then((r) => r.data),
  create: (data: CreateWebhookRequest) =>
    client.post<WebhookDto>('/api/webhooks', data).then((r) => r.data),
  update: (id: number, data: UpdateWebhookRequest) =>
    client.put<WebhookDto>(`/api/webhooks/${id}`, data).then((r) => r.data),
  remove: (id: number) =>
    client.delete<void>(`/api/webhooks/${id}`).then((r) => r.data),
  toggleActive: (id: number) =>
    client.post<void>(`/api/webhooks/${id}/toggle-active`).then((r) => r.data),
  test: (id: number, data: TestWebhookRequest) =>
    client.post<TestWebhookResponse>(`/api/webhooks/${id}/test`, data).then((r) => r.data),
  logs: (id: number, params?: { page?: number; pageSize?: number; eventType?: string; isSuccess?: boolean | null }) =>
    client
      .get<{ data: WebhookLogDto[]; totalItems: number; totalPages: number }>(
        `/api/webhooks/${id}/logs`,
        { params },
      )
      .then((r) => r.data),
  allLogs: (params?: {
    page?: number;
    pageSize?: number;
    webhookId?: number | null;
    eventType?: string;
    isSuccess?: boolean | null;
  }) =>
    client
      .get<{ data: WebhookLogDto[]; totalItems: number; totalPages: number }>('/api/webhooks/logs', { params })
      .then((r) => r.data),
  getTrash: (page = 1, pageSize = 10, keyword = '') =>
    client
      .get<{ data: WebhookDto[]; totalItems: number; totalPages: number }>('/api/webhooks/trash', {
        params: { page, pageSize, keyword },
      })
      .then((r) => r.data),
  restore: (id: number) => client.post<void>(`/api/webhooks/${id}/restore`).then((r) => r.data),
  hardDelete: (id: number) => client.delete<void>(`/api/webhooks/${id}/hard`).then((r) => r.data),
  clearTrash: () => client.delete<{ count: number }>('/api/webhooks/trash').then((r) => r.data),
};
