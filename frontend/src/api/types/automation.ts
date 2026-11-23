// ====== 工作流自动化规则 ======
export interface WorkflowCondition {
  field: string;
  operator: string;
  value: string;
}

export interface WorkflowConditionGroup {
  matchType: string;
  conditions: WorkflowCondition[];
}

export interface WorkflowActionDto {
  type: string;
  value?: string | null;
  extra?: string | null;
}

export interface WorkflowDto {
  id: number;
  name: string;
  description: string | null;
  mailboxId: number | null;
  mailboxName: string | null;
  isActive: boolean;
  isSystem: boolean;
  sortOrder: number;
  triggerType: string;
  triggerLabel: string | null;
  conditions: WorkflowConditionGroup | null;
  actions: WorkflowActionDto[];
  matchCount: number;
  lastMatchedAt: string | null;
  createdAt: string;
  updatedAt: string;
  deletedAt: string | null;
}

export interface WorkflowExecutionLogDto {
  id: number;
  workflowId: number;
  workflowName: string;
  conversationId: number;
  conversationSubject: string | null;
  status: string;
  statusLabel: string;
  actionsExecuted: number;
  errorMessage: string | null;
  executedAt: string;
}

export interface WorkflowOverviewResponse {
  stats: Record<string, number>;
  recentWorkflows: WorkflowDto[];
  recentExecutions: WorkflowExecutionLogDto[];
  triggerOptions: Record<string, string>;
  fieldOptions: Record<string, string>;
  operatorOptions: Record<string, string>;
  actionTypeOptions: Record<string, string>;
}

export interface WorkflowQuery {
  page?: number;
  pageSize?: number;
  mailboxId?: number | null;
  isActive?: boolean | null;
  triggerType?: string | null;
  keyword?: string | null;
}

export interface CreateWorkflowRequest {
  name: string;
  description?: string | null;
  mailboxId: number | null;
  isActive: boolean;
  sortOrder: number;
  triggerType: string;
  conditions: WorkflowConditionGroup | null;
  actions: WorkflowActionDto[];
}

export interface UpdateWorkflowRequest extends CreateWorkflowRequest {
  id: number;
}

export interface WorkflowTestRequest {
  subject?: string | null;
  fromEmail?: string | null;
  body?: string | null;
  toEmail?: string | null;
  mailboxId?: number;
}

export interface WorkflowTestActionResult {
  type: string;
  typeLabel: string;
  executed: boolean;
  success: boolean;
  message: string;
}

export interface WorkflowTestResponse {
  matched: boolean;
  matchedConditions: string[];
  actionsToExecute: string[];
  actionResults: WorkflowTestActionResult[];
}

export interface WorkflowExecutionLogQuery {
  page?: number;
  pageSize?: number;
  workflowId?: number | null;
  conversationId?: number | null;
  status?: string | null;
  startDate?: string | null;
  endDate?: string | null;
}

// ====== SLA 策略 ======
export interface SlaPolicyDto {
  id: number;
  name: string;
  description: string | null;
  mailboxId: number | null;
  mailboxName: string | null;
  firstResponseHours: number;
  resolutionHours: number;
  businessHoursOnly: boolean;
  priority: number;
  isActive: boolean;
  sortOrder: number;
  appliedCount: number;
  violationCount: number;
  lastAppliedAt: string | null;
  createdAt: string;
  updatedAt: string;
  deletedAt: string | null;
  firstResponseLabel: string;
  resolutionLabel: string;
}

export interface SlaViolationDto {
  id: number;
  slaPolicyId: number;
  slaPolicyName: string;
  conversationId: number;
  conversationSubject: string | null;
  violationType: string;
  violationTypeLabel: string;
  dueAt: string;
  detectedAt: string;
  resolvedAt: string | null;
  status: string;
  statusLabel: string;
  remainingMinutes: number;
  createdAt: string;
}

export interface SlaOverviewResponse {
  policyStats: Record<string, number>;
  violationStats: Record<string, number>;
  recentViolations: SlaViolationDto[];
  activeWarnings: SlaViolationDto[];
  topPolicies: SlaPolicyDto[];
}

export interface CreateSlaPolicyRequest {
  name: string;
  description?: string | null;
  mailboxId: number | null;
  firstResponseHours: number;
  resolutionHours: number;
  businessHoursOnly: boolean;
  priority: number;
  isActive: boolean;
  sortOrder: number;
}

export interface UpdateSlaPolicyRequest extends CreateSlaPolicyRequest {
  id: number;
}

export interface SlaConversationStatusResponse {
  conversationId: number;
  policyName: string | null;
  firstResponseHours: number | null;
  resolutionHours: number | null;
  firstResponseDueAt: string | null;
  resolutionDueAt: string | null;
  firstResponseAt: string | null;
  resolvedAt: string | null;
  firstResponseStatus: string;
  resolutionStatus: string;
  firstResponseRemainingMinutes: number | null;
  resolutionRemainingMinutes: number | null;
}

export interface SlaPolicyQuery {
  page?: number;
  pageSize?: number;
  mailboxId?: number | null;
  isActive?: boolean | null;
  keyword?: string | null;
}

export interface SlaViolationQuery {
  page?: number;
  pageSize?: number;
  policyId?: number | null;
  conversationId?: number | null;
  violationType?: string | null;
  status?: string | null;
  startDate?: string | null;
  endDate?: string | null;
}

// ====== 宏 (Macros) ======
export interface MacroActionDto {
  type: string;
  value: unknown;
  note?: string | null;
}

export interface MacroDto {
  id: number;
  name: string;
  description: string | null;
  mailboxId: number | null;
  mailboxName: string | null;
  isActive: boolean;
  sortOrder: number;
  usageCount: number;
  lastUsedAt: string | null;
  createdAt: string;
  updatedAt: string;
  deletedAt: string | null;
  actions: MacroActionDto[];
  /** 动作的可读化描述（如「将状态改为 已关闭 → 添加标签 已完成」） */
  actionsDescription?: string;
}

export interface MacroExecutionLogDto {
  id: number;
  macroId: number;
  macroName: string;
  conversationId: number;
  conversationSubject: string | null;
  executedByUserId: number;
  executedByUserName: string | null;
  status: string;
  actionsExecuted: number;
  errorMessage: string | null;
  executedAt: string;
}

export interface MacroOverviewResponse {
  totalMacros: number;
  activeMacros: number;
  inactiveMacros: number;
  totalExecutions: number;
  todayExecutions: number;
  actionTypes: Record<string, string>;
  recentExecutions: MacroExecutionLogDto[];
  topMacros: MacroDto[];
}

export interface CreateMacroRequest {
  name: string;
  description?: string | null;
  mailboxId: number | null;
  isActive: boolean;
  sortOrder: number;
  actions: MacroActionDto[];
}

export interface UpdateMacroRequest extends CreateMacroRequest {
  id: number;
}

export interface ExecuteMacroRequest {
  conversationId: number;
}

export interface ExecuteMacroResponse {
  success: boolean;
  message: string;
  actionsExecuted: number;
  executedActions: string[];
}

export interface MacroQuery {
  page?: number;
  pageSize?: number;
  mailboxId?: number | null;
  isActive?: boolean | null;
  keyword?: string | null;
}

export interface MacroExecutionLogQuery {
  page?: number;
  pageSize?: number;
  macroId?: number | null;
  conversationId?: number | null;
}

export interface MacroActionPreviewItem {
  type: string;
  typeLabel: string;
  value: unknown;
  description: string;
}

export interface MacroPreviewResponse {
  success: boolean;
  message: string;
  macroId: number;
  macroName: string;
  conversationId: number;
  conversationSubject: string | null;
  actionCount: number;
  actions: MacroActionPreviewItem[];
}

export interface DuplicateMacroRequest {
  newName?: string | null;
}

// ====== 自定义字段相关 ======
export interface CustomFieldDto {
  id: number;
  name: string;
  type: string;
  entityType: string;
  mailboxId: number | null;
  mailboxName: string | null;
  /** 适用渠道列表（小写枚举名，如 ["wechat","feishu"]）；空数组或 null 表示全局可用 */
  sourceTypes: string[];
  /** 渠道关闭后是否自动隐藏该字段（仅 conversation 字段有意义） */
  hideWhenChannelDisabled: boolean;
  /** 已禁用的渠道列表（运行时计算，仅字段管理接口返回）：用于列表显示"渠道已关闭"警告 */
  disabledChannels: string[];
  isRequired: boolean;
  isActive: boolean;
  sortOrder: number;
  options: string[];
  defaultValue: string | null;
  placeholder: string | null;
  helpText: string | null;
  createdAt: string;
  updatedAt: string;
}

/** 渠道启用状态选项（用于字段管理页"适用渠道"下拉） */
export interface CustomFieldChannelDto {
  /** 渠道标识（小写枚举名，如 "wechat"）；"global" 表示全局 */
  value: string;
  /** 渠道显示名（如 "微信"、"全局（所有渠道）"） */
  label: string;
  /** 是否当前已启用（全局始终为 true） */
  isEnabled: boolean;
}

export interface CustomFieldValueDto {
  id: number;
  customFieldId: number;
  fieldName: string;
  fieldType: string;
  entityId: number;
  entityType: string;
  value: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CustomFieldOverviewResponse {
  totalFields: number;
  activeFields: number;
  inactiveFields: number;
  conversationFields: number;
  customerFields: number;
  globalFields: number;
  mailboxSpecificFields: number;
  fieldsByType: Record<string, number>;
}

export interface CreateCustomFieldRequest {
  name: string;
  type: string;
  entityType: string;
  mailboxId: number | null;
  /** 适用渠道列表（小写枚举名）；空数组表示全局。customer 类型字段忽略此字段 */
  sourceTypes: string[];
  /** 渠道关闭后是否自动隐藏（仅 conversation 字段有意义） */
  hideWhenChannelDisabled: boolean;
  isRequired: boolean;
  isActive: boolean;
  sortOrder: number;
  options: string[];
  defaultValue: string | null;
  placeholder: string | null;
  helpText: string | null;
}

export interface UpdateCustomFieldRequest {
  id: number;
  name: string;
  type: string;
  entityType: string;
  mailboxId: number | null;
  sourceTypes: string[];
  hideWhenChannelDisabled: boolean;
  isRequired: boolean;
  isActive: boolean;
  sortOrder: number;
  options: string[];
  defaultValue: string | null;
  placeholder: string | null;
  helpText: string | null;
}

export interface CustomFieldQuery {
  page?: number;
  pageSize?: number;
  entityType?: string | null;
  mailboxId?: number | null;
  isActive?: boolean | null;
  keyword?: string | null;
  /** 按渠道过滤（传入会话 sourceType 字符串，如 "wechat"） */
  sourceType?: string | null;
}

// ====== Webhook 相关 ======
export interface WebhookDto {
  id: number;
  name: string;
  url: string;
  method: string;
  events: string[];
  headers: Record<string, string>;
  hasSecret: boolean;
  isActive: boolean;
  mailboxId: number | null;
  mailboxName: string | null;
  triggerCount: number;
  successCount: number;
  failCount: number;
  lastTriggeredAt: string | null;
  lastError: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface WebhookLogDto {
  id: number;
  webhookId: number;
  webhookName: string;
  eventType: string;
  entityType: string | null;
  entityId: number | null;
  url: string;
  method: string;
  responseStatusCode: number | null;
  isSuccess: boolean;
  errorMessage: string | null;
  durationMs: number;
  createdAt: string;
}

export interface WebhookOverviewResponse {
  totalWebhooks: number;
  activeWebhooks: number;
  inactiveWebhooks: number;
  globalWebhooks: number;
  mailboxWebhooks: number;
  totalTriggers: number;
  totalSuccess: number;
  totalFail: number;
  successRate: number;
  recentLogs: WebhookLogDto[];
  topWebhooks: WebhookDto[];
  eventsCount: Record<string, number>;
}

export interface CreateWebhookRequest {
  name: string;
  url: string;
  method: string;
  events: string[];
  headers: Record<string, string>;
  secret: string | null;
  isActive: boolean;
  mailboxId: number | null;
}

export interface UpdateWebhookRequest {
  id: number;
  name: string;
  url: string;
  method: string;
  events: string[];
  headers: Record<string, string>;
  secret: string | null;
  isActive: boolean;
  mailboxId: number | null;
}

export interface TestWebhookRequest {
  eventType: string;
}

export interface TestWebhookResponse {
  success: boolean;
  message: string;
  statusCode: number | null;
  responseBody: string | null;
  durationMs: number;
}
