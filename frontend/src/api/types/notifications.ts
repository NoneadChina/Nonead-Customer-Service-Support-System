import type { BulkOperationResponse } from './common';

export interface NotificationTemplateResponse {
  id: number;
  name: string;
  subject: string;
  body: string;
  eventType: string;
  eventTypeLabel?: string;
  isActive: boolean;
  createdAt?: string;
  updatedAt: string;
  deletedAt: string | null;
}

export interface NotificationTemplateEventStats {
  eventType: string;
  eventTypeLabel: string;
  count: number;
  activeCount: number;
}

export interface NotificationTemplateOverviewResponse {
  total: number;
  active: number;
  inactive: number;
  events: NotificationTemplateEventStats[];
}

export interface NotificationRuleResponse {
  id: number;
  name: string;
  eventType: string;
  eventTypeLabel?: string;
  mailboxId: number | null;
  mailboxName?: string | null;
  userId: number | null;
  userName?: string | null;
  templateId: number | null;
  templateName: string | null;
  isActive: boolean;
  createdAt?: string;
  updatedAt: string;
  deletedAt: string | null;
}

export interface NotificationRuleEventStats {
  eventType: string;
  eventTypeLabel: string;
  count: number;
  activeCount: number;
}

export interface NotificationRuleOverviewResponse {
  total: number;
  active: number;
  inactive: number;
  events: NotificationRuleEventStats[];
}

export interface CreateNotificationTemplateRequest {
  name: string;
  subject: string;
  body: string;
  eventType: string;
  isActive: boolean;
}

export type UpdateNotificationTemplateRequest = CreateNotificationTemplateRequest;

export interface CreateNotificationRuleRequest {
  name: string;
  eventType: string;
  mailboxId?: number | null;
  userId?: number | null;
  templateId?: number | null;
  isActive: boolean;
}

export type UpdateNotificationRuleRequest = CreateNotificationRuleRequest;

export interface SendNotificationRequest {
  eventType: string;
  mailboxId?: number | null;
  conversationId?: number | null;
  variables: Record<string, string>;
}

export interface NotificationSendResultResponse {
  totalRules: number;
  sentCount: number;
  failedCount: number;
  errors: string[];
}

export interface PreviewTemplateRequest {
  template: string;
  variables: Record<string, string>;
}

export interface PreviewTemplateResponse {
  rendered: string;
  renderedSubject?: string;
}

export interface TemplateVariableValidateResponse {
  subjectVariables: string[];
  bodyVariables: string[];
  allVariables: string[];
  commonVariables: string[];
  uncommonVariables: string[];
}

export interface NotificationTemplatePreviewResponse {
  subject: string;
  body: string;
}

export interface DuplicateNotificationTemplateRequest {
  newName?: string;
}

export interface DuplicateNotificationRuleRequest {
  newName?: string;
}

export type { BulkOperationResponse };
