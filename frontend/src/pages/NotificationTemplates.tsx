import { useCallback, useState, useEffect, useMemo, useRef } from 'react';
import { notificationApi } from '../api';
import { useTimedToast } from '../hooks/useTimedToast';
import type {
  NotificationTemplateResponse,
  NotificationTemplateOverviewResponse,
  CreateNotificationTemplateRequest,
  TemplateVariableValidateResponse,
  NotificationTemplatePreviewResponse,
} from '../api/types';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import { formatDateTime, sanitizeHtml } from '../utils/format';
import { useI18n } from '../context/I18nContext';
import { toIntlLocale } from '../i18n/locale';
import type { TranslationKey } from '../locales/zh-Hans';
import {
  FileText,
  CheckCircle2,
  XCircle,
  List,
  Plus,
  Search,
  Edit2,
  Trash2,
  ChevronLeft,
  ChevronRight,
  Tag,
  RotateCcw,
  Copy,
  AlertCircle,
  Eye,
  X,
  Columns3,
  Code2,
  MonitorPlay,
  Zap,
  Activity,
  Shield,
  Trash,
  Clock,
  RefreshCw,
  Mail,
  MessageSquare,
  Users,
  Filter,
} from 'lucide-react';

// 事件类型 -> 配色（按语义分组：会话/SLA/系统/IM）
const eventTypeColorMap: Record<string, { bg: string; text: string; ring: string; accent: string; bar: string; label: TranslationKey }> = {
  // 会话类（蓝色系）
  new_conversation:     { bg: 'bg-blue-50 dark:bg-blue-900/25',     text: 'text-blue-700 dark:text-blue-400',     ring: 'ring-blue-600/20',       accent: 'bg-blue-500',    bar: 'bg-blue-500', label: 'notifications.eventType.newConversation' },
  new_reply:            { bg: 'bg-sky-50 dark:bg-sky-900/25',       text: 'text-sky-700 dark:text-sky-400',       ring: 'ring-sky-600/20',        accent: 'bg-sky-500',     bar: 'bg-sky-500', label: 'notifications.eventType.newReply' },
  conversation_assigned:{ bg: 'bg-indigo-50 dark:bg-indigo-900/25', text: 'text-indigo-700 dark:text-indigo-400', ring: 'ring-indigo-600/20',     accent: 'bg-indigo-500',  bar: 'bg-indigo-500', label: 'notifications.eventType.conversationAssigned' },
  conversation_updated: { bg: 'bg-cyan-50 dark:bg-cyan-900/25',     text: 'text-cyan-700 dark:text-cyan-400',     ring: 'ring-cyan-600/20',       accent: 'bg-cyan-500',    bar: 'bg-cyan-500', label: 'notifications.eventType.conversationUpdated' },
  conversation_closed:  { bg: 'bg-violet-50 dark:bg-violet-900/25', text: 'text-violet-700 dark:text-violet-400', ring: 'ring-violet-600/20',     accent: 'bg-violet-500',  bar: 'bg-violet-500', label: 'notifications.eventType.conversationClosed' },
  customer_reply:       { bg: 'bg-teal-50 dark:bg-teal-900/25',     text: 'text-teal-700 dark:text-teal-400',     ring: 'ring-teal-600/20',       accent: 'bg-teal-500',    bar: 'bg-teal-500', label: 'notifications.eventType.customerReply' },
  mention:              { bg: 'bg-rose-50 dark:bg-rose-900/25',     text: 'text-rose-700 dark:text-rose-400',     ring: 'ring-rose-600/20',       accent: 'bg-rose-500',    bar: 'bg-rose-500', label: 'notifications.eventType.mention' },
  conversation_reopened:{ bg: 'bg-fuchsia-50 dark:bg-fuchsia-900/25', text: 'text-fuchsia-700 dark:text-fuchsia-400', ring: 'ring-fuchsia-600/20', accent: 'bg-fuchsia-500', bar: 'bg-fuchsia-500', label: 'notifications.eventType.conversationReopened' },
  conversation_transferred:{ bg: 'bg-blue-50 dark:bg-blue-900/25', text: 'text-blue-700 dark:text-blue-400',     ring: 'ring-blue-600/20',       accent: 'bg-blue-600',    bar: 'bg-blue-600', label: 'notifications.eventType.conversationTransferred' },
  conversation_timeout: { bg: 'bg-sky-50 dark:bg-sky-900/25',       text: 'text-sky-700 dark:text-sky-400',       ring: 'ring-sky-600/20',        accent: 'bg-sky-600',     bar: 'bg-sky-600', label: 'notifications.eventType.conversationTimeout' },
  // SLA（橙红系）
  sla_violation:        { bg: 'bg-red-50 dark:bg-red-900/25',       text: 'text-red-700 dark:text-red-400',       ring: 'ring-red-600/20',        accent: 'bg-red-500',     bar: 'bg-red-500', label: 'notifications.eventType.slaViolation' },
  sla_warning:          { bg: 'bg-orange-50 dark:bg-orange-900/25', text: 'text-orange-700 dark:text-orange-400', ring: 'ring-orange-600/20',     accent: 'bg-orange-500',  bar: 'bg-orange-500', label: 'notifications.eventType.slaWarning' },
  ticket_escalated:     { bg: 'bg-amber-50 dark:bg-amber-900/25',   text: 'text-amber-700 dark:text-amber-400',   ring: 'ring-amber-600/20',      accent: 'bg-amber-500',   bar: 'bg-amber-500', label: 'notifications.eventType.ticketEscalated' },
  // 满意度（青绿系）
  customer_satisfaction:{ bg: 'bg-emerald-50 dark:bg-emerald-900/25', text: 'text-emerald-700 dark:text-emerald-400', ring: 'ring-emerald-600/20', accent: 'bg-emerald-500', bar: 'bg-emerald-500', label: 'notifications.eventType.customerSatisfaction' },
  satisfaction_submitted:{ bg: 'bg-green-50 dark:bg-green-900/25',  text: 'text-green-700 dark:text-green-400',   ring: 'ring-green-600/20',      accent: 'bg-green-500',   bar: 'bg-green-500', label: 'notifications.eventType.satisfactionSubmitted' },
  // 自动
  auto_reply:           { bg: 'bg-lime-50 dark:bg-lime-900/25',     text: 'text-lime-700 dark:text-lime-400',     ring: 'ring-lime-600/20',       accent: 'bg-lime-500',    bar: 'bg-lime-500', label: 'notifications.eventType.autoReply' },
  // 用户/账户（紫色系）
  user_welcome:         { bg: 'bg-violet-50 dark:bg-violet-900/25', text: 'text-violet-700 dark:text-violet-400', ring: 'ring-violet-600/20',     accent: 'bg-violet-600',  bar: 'bg-violet-600', label: 'notifications.eventType.userWelcome' },
  password_reset:       { bg: 'bg-purple-50 dark:bg-purple-900/25', text: 'text-purple-700 dark:text-purple-400', ring: 'ring-purple-600/20',     accent: 'bg-purple-500',  bar: 'bg-purple-500', label: 'notifications.eventType.passwordReset' },
  account_locked:       { bg: 'bg-rose-50 dark:bg-rose-900/25',     text: 'text-rose-700 dark:text-rose-400',     ring: 'ring-rose-600/20',       accent: 'bg-rose-600',    bar: 'bg-rose-600', label: 'notifications.eventType.accountLocked' },
  // 系统（灰色系）
  bulk_import_completed:{ bg: 'bg-slate-50 dark:bg-slate-900/40',   text: 'text-slate-700 dark:text-slate-400',   ring: 'ring-slate-600/20',      accent: 'bg-slate-500',   bar: 'bg-slate-500', label: 'notifications.eventType.bulkImportCompleted' },
  daily_report:         { bg: 'bg-gray-50 dark:bg-gray-900/40',     text: 'text-gray-700 dark:text-gray-400',     ring: 'ring-gray-600/20',       accent: 'bg-gray-600',    bar: 'bg-gray-600', label: 'notifications.eventType.dailyReport' },
  system_maintenance:   { bg: 'bg-stone-50 dark:bg-stone-900/40',   text: 'text-stone-700 dark:text-stone-400',   ring: 'ring-stone-600/20',      accent: 'bg-stone-600',   bar: 'bg-stone-600', label: 'notifications.eventType.systemMaintenance' },
};

type EventTypeColor = {
  bg: string;
  text: string;
  ring: string;
  accent: string;
  bar: string;
  label?: TranslationKey;
};

function evtTypeColor(eventType: string): EventTypeColor {
  return eventTypeColorMap[eventType] ?? {
    bg: 'bg-gray-50 dark:bg-slate-900/40', text: 'text-gray-700 dark:text-slate-400',
    ring: 'ring-gray-500/20', accent: 'bg-gray-500', bar: 'bg-gray-500',
  };
}

/** 事件类型码 → 词条 key；未知码回落原码（与原行为一致） */
function getEventTypeLabel(t: (key: TranslationKey) => string, eventType: string): string {
  const k: TranslationKey | undefined = eventTypeLabels[eventType];
  return k ? t(k) : eventType;
}

/** 变量分类码 → 词条 key；未知分类回落原码 */
function getVariableCategoryLabel(t: (key: TranslationKey) => string, category: string): string {
  const k: TranslationKey | undefined = (variableCategoryKeys as Record<string, TranslationKey>)[category];
  return k ? t(k) : category;
}

// 概览统计 -> 配色
const statPalette = [
  {
    from: 'from-primary-500',
    to: 'to-indigo-500',
    textLight: 'text-primary-600 dark:text-primary-400',
    bgSoft: 'bg-primary-50 dark:bg-primary-900/30',
    ringSoft: 'ring-primary-500/20',
  },
  {
    from: 'from-emerald-500',
    to: 'to-teal-500',
    textLight: 'text-emerald-600 dark:text-emerald-400',
    bgSoft: 'bg-emerald-50 dark:bg-emerald-900/30',
    ringSoft: 'ring-emerald-500/20',
  },
  {
    from: 'from-amber-500',
    to: 'to-orange-500',
    textLight: 'text-amber-600 dark:text-amber-400',
    bgSoft: 'bg-amber-50 dark:bg-amber-900/30',
    ringSoft: 'ring-amber-500/20',
  },
];

// 通用：空态插画
function EmptyState({
  icon: Icon,
  title,
  subtitle,
  action,
}: {
  icon: React.ComponentType<{ className?: string }>;
  title: string;
  subtitle: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="py-16 flex flex-col items-center text-gray-400">
      <div className="w-20 h-20 rounded-2xl bg-gradient-to-br from-gray-50 to-gray-100 dark:from-slate-800 dark:to-slate-900 border border-gray-100 dark:border-slate-700 flex items-center justify-center mb-4">
        <Icon className="w-10 h-10 opacity-30" />
      </div>
      <p className="text-sm font-semibold text-gray-500 dark:text-slate-400">{title}</p>
      <p className="text-xs mt-1 text-gray-400 dark:text-slate-500">{subtitle}</p>
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}

// 通用：加载骨架
function LoadingState({ label = 'common.shell.loading' }: { label?: TranslationKey }) {
  const { t } = useI18n();
  return (
    <div className="py-16 flex flex-col items-center text-gray-400">
      <div className="w-10 h-10 mb-4 border-4 border-primary-200 dark:border-primary-800 border-t-primary-500 dark:border-t-primary-400 rounded-full animate-spin" />
      <p className="text-sm text-gray-500 dark:text-slate-400">{t(label)}</p>
    </div>
  );
}

// 通用 input 类
const inputBase =
  'w-full px-3.5 py-2.5 bg-white dark:bg-slate-900/60 border border-gray-200 dark:border-slate-700 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:text-slate-200 transition placeholder:text-gray-400 dark:placeholder:text-slate-500';
const btnSecondary =
  'px-4 py-2.5 text-sm font-semibold text-gray-700 dark:text-slate-200 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition shadow-sm disabled:opacity-50 disabled:cursor-not-allowed';
const btnPrimary =
  'inline-flex items-center gap-1.5 px-4.5 py-2.5 text-sm font-semibold text-white bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 rounded-xl shadow-md shadow-primary-500/20 hover:shadow-lg hover:shadow-primary-500/30 transition-all active:scale-[0.98] disabled:opacity-60 disabled:cursor-not-allowed disabled:shadow-none';
const pillTabBase =
  'inline-flex items-center gap-1.5 px-4 py-2 text-sm font-semibold rounded-xl transition-all';

const eventTypeOptions: { value: string; label: TranslationKey }[] = [
  { value: 'new_conversation', label: 'notifications.eventType.newConversation' },
  { value: 'new_reply', label: 'notifications.eventType.newReply' },
  { value: 'conversation_assigned', label: 'notifications.eventType.conversationAssigned' },
  { value: 'conversation_updated', label: 'notifications.eventType.conversationUpdated' },
  { value: 'conversation_closed', label: 'notifications.eventType.conversationClosed' },
  { value: 'customer_reply', label: 'notifications.eventType.customerReply' },
  { value: 'mention', label: 'notifications.eventType.mention' },
  { value: 'sla_violation', label: 'notifications.eventType.slaViolation' },
  { value: 'sla_warning', label: 'notifications.eventType.slaWarning' },
  { value: 'conversation_timeout', label: 'notifications.eventType.conversationTimeout' },
  { value: 'ticket_escalated', label: 'notifications.eventType.ticketEscalated' },
  { value: 'customer_satisfaction', label: 'notifications.eventType.customerSatisfaction' },
  { value: 'auto_reply', label: 'notifications.eventType.autoReply' },
  { value: 'user_welcome', label: 'notifications.eventType.userWelcome' },
  { value: 'password_reset', label: 'notifications.eventType.passwordReset' },
  { value: 'bulk_import_completed', label: 'notifications.eventType.bulkImportCompleted' },
  { value: 'conversation_reopened', label: 'notifications.eventType.conversationReopened' },
  { value: 'conversation_transferred', label: 'notifications.eventType.conversationTransferred' },
  { value: 'satisfaction_submitted', label: 'notifications.eventType.satisfactionSubmitted' },
  { value: 'daily_report', label: 'notifications.eventType.dailyReport' },
  { value: 'account_locked', label: 'notifications.eventType.accountLocked' },
  { value: 'system_maintenance', label: 'notifications.eventType.systemMaintenance' },
];

const eventTypeLabels: Record<string, TranslationKey> = {
  new_conversation: 'notifications.eventType.newConversation',
  new_reply: 'notifications.eventType.newReply',
  conversation_assigned: 'notifications.eventType.conversationAssigned',
  conversation_updated: 'notifications.eventType.conversationUpdated',
  conversation_closed: 'notifications.eventType.conversationClosed',
  customer_reply: 'notifications.eventType.customerReply',
  mention: 'notifications.eventType.mention',
  sla_violation: 'notifications.eventType.slaViolation',
  sla_warning: 'notifications.eventType.slaWarning',
  conversation_timeout: 'notifications.eventType.conversationTimeout',
  ticket_escalated: 'notifications.eventType.ticketEscalated',
  // 后端 switch 识别但 UI 不暴露的两个码（无产出方）：登记以免原始码落到界面
  ticket_created: 'notifications.eventType.ticketCreated',
  ticket_updated: 'notifications.eventType.ticketUpdated',
  customer_satisfaction: 'notifications.eventType.customerSatisfaction',
  auto_reply: 'notifications.eventType.autoReply',
  user_welcome: 'notifications.eventType.userWelcome',
  password_reset: 'notifications.eventType.passwordReset',
  bulk_import_completed: 'notifications.eventType.bulkImportCompleted',
  conversation_reopened: 'notifications.eventType.conversationReopened',
  conversation_transferred: 'notifications.eventType.conversationTransferred',
  satisfaction_submitted: 'notifications.eventType.satisfactionSubmitted',
  daily_report: 'notifications.eventType.dailyReport',
  account_locked: 'notifications.eventType.accountLocked',
  system_maintenance: 'notifications.eventType.systemMaintenance',
};

// 通知模板内置变量（按分类组织）
// 条目只持有**词条 key**（label）与**稳定分类码**（category），渲染与搜索处一律经 t() 解析；
// 直接存中文会导致渲染级 key 泄漏或漏译（本项目真实发生过的缺陷类别）。
type VariableCategory =
  | 'conversation'
  | 'customer'
  | 'agent'
  | 'mailbox'
  | 'message'
  | 'sla'
  | 'brand'
  | 'satisfaction'
  | 'account'
  | 'import'
  | 'maintenance';

interface NotificationTemplateVariable {
  key: string;
  label: TranslationKey;
  category: VariableCategory;
}

const variableCategoryKeys: Record<VariableCategory, TranslationKey> = {
  conversation: 'notifications.templates.varCategory.conversation',
  customer: 'notifications.templates.varCategory.customer',
  agent: 'notifications.templates.varCategory.agent',
  mailbox: 'notifications.templates.varCategory.mailbox',
  message: 'notifications.templates.varCategory.message',
  sla: 'notifications.templates.varCategory.sla',
  brand: 'notifications.templates.varCategory.brand',
  satisfaction: 'notifications.templates.varCategory.satisfaction',
  account: 'notifications.templates.varCategory.account',
  import: 'notifications.templates.varCategory.import',
  maintenance: 'notifications.templates.varCategory.maintenance',
};

const builtInNotificationVariables: NotificationTemplateVariable[] = [
  // 会话信息
  { key: 'conversation_id', label: 'notifications.templates.var.conversationId', category: 'conversation' },
  { key: 'conversation_number', label: 'notifications.templates.var.conversationNumber', category: 'conversation' },
  { key: 'subject', label: 'notifications.templates.var.conversationSubject', category: 'conversation' },
  { key: 'conversation_url', label: 'notifications.templates.var.conversationUrl', category: 'conversation' },
  { key: 'created_at', label: 'notifications.templates.var.createdAt', category: 'conversation' },
  { key: 'updated_at', label: 'notifications.templates.var.updatedAt', category: 'conversation' },
  { key: 'priority', label: 'notifications.templates.var.priority', category: 'conversation' },
  { key: 'status', label: 'notifications.templates.var.status', category: 'conversation' },
  { key: 'tags', label: 'notifications.templates.var.tags', category: 'conversation' },
  { key: 'folder_name', label: 'notifications.templates.var.folderName', category: 'conversation' },

  // 客户信息
  { key: 'customer_name', label: 'notifications.templates.var.customerName', category: 'customer' },
  { key: 'customer_email', label: 'notifications.templates.var.customerEmail', category: 'customer' },
  { key: 'customer_phone', label: 'notifications.templates.var.customerPhone', category: 'customer' },
  { key: 'customer_company', label: 'notifications.templates.var.customerCompany', category: 'customer' },

  // 客服/用户信息
  { key: 'user_name', label: 'notifications.templates.var.userName', category: 'agent' },
  { key: 'user_email', label: 'notifications.templates.var.userEmail', category: 'agent' },
  { key: 'user_role', label: 'notifications.templates.var.userRole', category: 'agent' },
  { key: 'assigned_user_name', label: 'notifications.templates.var.assignedUserName', category: 'agent' },
  { key: 'assigned_user_email', label: 'notifications.templates.var.assignedUserEmail', category: 'agent' },
  { key: 'closed_by', label: 'notifications.templates.var.closedBy', category: 'agent' },
  { key: 'reply_from', label: 'notifications.templates.var.replyFrom', category: 'agent' },

  // 邮箱信息
  { key: 'mailbox_name', label: 'notifications.templates.var.mailboxName', category: 'mailbox' },
  { key: 'mailbox_email', label: 'notifications.templates.var.mailboxEmail', category: 'mailbox' },

  // 消息内容
  { key: 'message_body', label: 'notifications.templates.var.messageBody', category: 'message' },
  { key: 'content', label: 'notifications.templates.var.content', category: 'message' },
  { key: 'reply_content', label: 'notifications.templates.var.replyContent', category: 'message' },

  // SLA 相关
  { key: 'sla_name', label: 'notifications.templates.var.slaName', category: 'sla' },
  { key: 'warning_time', label: 'notifications.templates.var.warningTime', category: 'sla' },
  { key: 'violation_time', label: 'notifications.templates.var.violationTime', category: 'sla' },
  { key: 'first_response_deadline', label: 'notifications.templates.var.firstResponseDeadline', category: 'sla' },
  { key: 'resolve_deadline', label: 'notifications.templates.var.resolveDeadline', category: 'sla' },

  // 系统/品牌
  { key: 'company_name', label: 'notifications.templates.var.companyName', category: 'brand' },
  { key: 'login_url', label: 'notifications.templates.var.loginUrl', category: 'brand' },
  { key: 'system_url', label: 'notifications.templates.var.systemUrl', category: 'brand' },
  { key: 'support_email', label: 'notifications.templates.var.supportEmail', category: 'brand' },

  // 满意度
  { key: 'satisfaction_url', label: 'notifications.templates.var.satisfactionUrl', category: 'satisfaction' },
  { key: 'satisfaction_score', label: 'notifications.templates.var.satisfactionScore', category: 'satisfaction' },
  { key: 'satisfaction_comment', label: 'notifications.templates.var.satisfactionComment', category: 'satisfaction' },

  // 密码/账户
  { key: 'reset_password_url', label: 'notifications.templates.var.resetPasswordUrl', category: 'account' },
  { key: 'account_lock_reason', label: 'notifications.templates.var.accountLockReason', category: 'account' },
  { key: 'lock_duration', label: 'notifications.templates.var.lockDuration', category: 'account' },

  // 导入/报表
  { key: 'import_total', label: 'notifications.templates.var.importTotal', category: 'import' },
  { key: 'import_success', label: 'notifications.templates.var.importSuccess', category: 'import' },
  { key: 'import_failed', label: 'notifications.templates.var.importFailed', category: 'import' },
  { key: 'report_date', label: 'notifications.templates.var.reportDate', category: 'import' },

  // 维护通知
  { key: 'maintenance_start', label: 'notifications.templates.var.maintenanceStart', category: 'maintenance' },
  { key: 'maintenance_end', label: 'notifications.templates.var.maintenanceEnd', category: 'maintenance' },
  { key: 'maintenance_content', label: 'notifications.templates.var.maintenanceContent', category: 'maintenance' },
];

type OverviewStat = {
  label: TranslationKey;
  value: number;
  icon: React.ComponentType<{ className?: string; strokeWidth?: string | number }>;
  hint: TranslationKey;
  palette: (typeof statPalette)[number];
};

type EditorViewModeOption = {
  mode: 'editor' | 'split' | 'preview';
  icon: React.ComponentType<{ className?: string }>;
  label: TranslationKey;
  hint: TranslationKey;
};

export default function NotificationTemplates() {
  const [activeTab, setActiveTab] = useState<'overview' | 'list'>('overview');
  const [overview, setOverview] = useState<NotificationTemplateOverviewResponse | null>(null);
  const [templates, setTemplates] = useState<NotificationTemplateResponse[]>([]);
  const [loading, setLoading] = useState(false);
  const [totalItems, setTotalItems] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [eventTypeFilter, setEventTypeFilter] = useState<string | null>(null);
  const [activeFilter, setActiveFilter] = useState<boolean | null>(null);

  const [showEditor, setShowEditor] = useState(false);
  const [editingTemplate, setEditingTemplate] = useState<NotificationTemplateResponse | null>(null);
  const [varValidateResult, setVarValidateResult] = useState<TemplateVariableValidateResponse | null>(null);

  const [formData, setFormData] = useState<CreateNotificationTemplateRequest>({
    name: '',
    subject: '',
    body: '',
    eventType: 'new_conversation',
    isActive: true,
  });
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);

  const [deleteTarget, setDeleteTarget] = useState<NotificationTemplateResponse | null>(null);
  const { message: toast, show: showToast } = useTimedToast();
  const { t, language } = useI18n();

  const [showPreview, setShowPreview] = useState(false);
  const [previewData, setPreviewData] = useState<NotificationTemplatePreviewResponse | null>(null);
  const [previewMode, setPreviewMode] = useState<'html' | 'text'>('html');
  const [previewLoading, setPreviewLoading] = useState(false);

  // 编辑器内视图模式：仅代码 | 左右分栏 | 仅预览
  const [editorViewMode, setEditorViewMode] = useState<'editor' | 'split' | 'preview'>('split');
  // 变量插入目标：主题 / 正文
  const [variableInsertTarget, setVariableInsertTarget] = useState<'subject' | 'body'>('body');
  // 右侧变量面板搜索关键词
  const [variableSearch, setVariableSearch] = useState('');
  // 本地实时预览（用示例数据简单替换 {xxx} 变量后显示，无需请求后端）
  const [localPreview, setLocalPreview] = useState<{ subject: string; body: string } | null>(null);
  const [localPreviewDebounceTimer, setLocalPreviewDebounceTimer] = useState<number | null>(null);

  // 垃圾箱
  const [trashOpen, setTrashOpen] = useState(false);
  const [trashQuery, setTrashQuery] = useState<{ page: number; pageSize: number; keyword: string }>({ page: 1, pageSize: 10, keyword: '' });
  const [trashList, setTrashList] = useState<{ data: NotificationTemplateResponse[]; totalItems: number; totalPages: number; currentPage: number } | null>(null);
  const [trashLoading, setTrashLoading] = useState(false);
  const [trashCount, setTrashCount] = useState(0);
  const [restoreTarget, setRestoreTarget] = useState<NotificationTemplateResponse | null>(null);
  const [hardDeleteTarget, setHardDeleteTarget] = useState<NotificationTemplateResponse | null>(null);
  const [clearTrashOpen, setClearTrashOpen] = useState(false);
  const [trashActionLoading, setTrashActionLoading] = useState(false);

  const pageSize = 10;
  const trashPageSize = 10;

  // 主列表搜索：缓冲值即时回显 + 650ms 防抖自动搜索 + 回车即时触发（与 ApiKeys 一致）
  const [searchInput, setSearchInput] = useState('');
  const searchTimerRef = useRef<number | null>(null);
  const applySearch = useCallback((value: string) => {
    setSearch(value);
    setPage(1);
  }, []);

  const handleSearchInput = useCallback((value: string) => {
    setSearchInput(value);
    // 输入停顿 650ms 后自动搜索（期间继续输入会重置计时器）
    if (searchTimerRef.current !== null) window.clearTimeout(searchTimerRef.current);
    searchTimerRef.current = window.setTimeout(() => applySearch(value), 650);
  }, [applySearch]);

  // 回车：立即按当前输入触发，并取消挂起的防抖
  const triggerSearchNow = useCallback(() => {
    if (searchTimerRef.current !== null) {
      window.clearTimeout(searchTimerRef.current);
      searchTimerRef.current = null;
    }
    applySearch(searchInput);
  }, [applySearch, searchInput]);

  // 清除搜索（chip X / 重置共用）：取消防抖并同步缓冲值
  const clearSearch = useCallback(() => {
    if (searchTimerRef.current !== null) {
      window.clearTimeout(searchTimerRef.current);
      searchTimerRef.current = null;
    }
    setSearchInput('');
    setSearch('');
    setPage(1);
  }, []);

  // 垃圾箱关键词搜索：同样 650ms 防抖 + 回车即时
  const [trashKeywordInput, setTrashKeywordInput] = useState('');
  const trashKeywordTimerRef = useRef<number | null>(null);
  const applyTrashKeyword = useCallback((value: string) => {
    setTrashQuery((q) => ({ ...q, keyword: value, page: 1 }));
  }, []);

  const handleTrashKeywordInput = useCallback((value: string) => {
    setTrashKeywordInput(value);
    if (trashKeywordTimerRef.current !== null) window.clearTimeout(trashKeywordTimerRef.current);
    trashKeywordTimerRef.current = window.setTimeout(() => applyTrashKeyword(value), 650);
  }, [applyTrashKeyword]);

  const triggerTrashKeywordNow = useCallback(() => {
    if (trashKeywordTimerRef.current !== null) {
      window.clearTimeout(trashKeywordTimerRef.current);
      trashKeywordTimerRef.current = null;
    }
    applyTrashKeyword(trashKeywordInput);
  }, [applyTrashKeyword, trashKeywordInput]);

  // 清空垃圾箱关键词（chip X / 打开垃圾箱共用）
  const clearTrashKeyword = useCallback(() => {
    if (trashKeywordTimerRef.current !== null) {
      window.clearTimeout(trashKeywordTimerRef.current);
      trashKeywordTimerRef.current = null;
    }
    setTrashKeywordInput('');
    setTrashQuery((q) => ({ ...q, keyword: '', page: 1 }));
  }, []);

  // 打开垃圾箱时 query 会被整体重置，仅需取消防抖并清空缓冲避免回显残留
  const resetTrashKeywordSearch = useCallback(() => {
    if (trashKeywordTimerRef.current !== null) {
      window.clearTimeout(trashKeywordTimerRef.current);
      trashKeywordTimerRef.current = null;
    }
    setTrashKeywordInput('');
  }, []);

  useEffect(() => {
    return () => {
      if (searchTimerRef.current !== null) window.clearTimeout(searchTimerRef.current);
      if (trashKeywordTimerRef.current !== null) window.clearTimeout(trashKeywordTimerRef.current);
    };
  }, []);

  // 本地示例数据（用于变量实时预览的简单替换）
  const sampleVariables: Record<string, string> = useMemo(
    () => ({
      subject: t('notifications.templates.sample.subject'),
      conversation_id: '123',
      conversation_number: 'CN-20260812-0001',
      conversation_url: `${window.location.origin}/conversations/123`,
      created_at: new Date().toLocaleString(toIntlLocale(language)),
      updated_at: new Date().toLocaleString(toIntlLocale(language)),
      priority: t('format.priority.high'),
      status: t('notifications.templates.sample.status'),
      tags: t('notifications.templates.sample.tags'),
      folder_name: t('notifications.templates.sample.folderName'),
      customer_name: t('notifications.templates.sample.customerName'),
      customer_email: 'customer@example.com',
      customer_phone: '138****8888',
      customer_company: t('notifications.templates.sample.customerCompany'),
      user_name: 'Admin User',
      user_email: 'admin@example.com',
      user_role: t('format.role.superAdmin'),
      assigned_user_name: t('notifications.templates.sample.assignedUserName'),
      assigned_user_email: 'zhangsan@example.com',
      closed_by: 'Admin User',
      reply_from: t('notifications.templates.sample.replyFrom'),
      mailbox_name: t('notifications.templates.sample.mailboxName'),
      mailbox_email: 'support@example.com',
      message_body: t('notifications.templates.sample.messageBody'),
      content: t('notifications.templates.sample.content'),
      reply_content: t('notifications.templates.sample.replyContent'),
      sla_name: t('notifications.templates.sample.slaName'),
      warning_time: t('notifications.templates.sample.warningTime'),
      violation_time: t('notifications.templates.sample.violationTime'),
      first_response_deadline: t('notifications.templates.sample.firstResponseDeadline'),
      resolve_deadline: t('notifications.templates.sample.resolveDeadline'),
      company_name: 'Nonead Customer Service & Support System',
      login_url: `${window.location.origin}/login`,
      system_url: window.location.origin,
      support_email: 'support@example.com',
      satisfaction_url: `${window.location.origin}/satisfaction/xxx`,
      satisfaction_score: t('notifications.templates.sample.satisfactionScore'),
      satisfaction_comment: t('notifications.templates.sample.satisfactionComment'),
      reset_password_url: `${window.location.origin}/reset-password?token=xxx`,
      account_lock_reason: t('notifications.templates.sample.accountLockReason'),
      lock_duration: t('notifications.templates.sample.lockDuration'),
      import_total: '1000',
      import_success: '986',
      import_failed: '14',
      report_date: new Date().toLocaleDateString(toIntlLocale(language)),
      maintenance_start: '2026-08-15 02:00',
      maintenance_end: '2026-08-15 04:00',
      maintenance_content: t('notifications.templates.sample.maintenanceContent'),
    }),
    [t, language],
  );

  // 变量分类（支持搜索过滤）
  const variablesByCategory = useMemo(() => {
    const keyword = variableSearch.trim().toLowerCase();
    const filtered = keyword
      ? builtInNotificationVariables.filter(
          (v) =>
            v.key.toLowerCase().includes(keyword) ||
            t(v.label).toLowerCase().includes(keyword) ||
            getVariableCategoryLabel(t, v.category).toLowerCase().includes(keyword),
        )
      : builtInNotificationVariables;

    return filtered.reduce((acc, v) => {
      if (!acc[v.category]) acc[v.category] = [];
      acc[v.category].push(v);
      return acc;
    }, {} as Record<string, NotificationTemplateVariable[]>);
  }, [variableSearch, t]);

  // 插入变量到主题或正文
  function insertVariable(key: string) {
    const token = `{${key}}`;
    if (variableInsertTarget === 'subject') {
      setFormData({ ...formData, subject: formData.subject + token });
    } else {
      setFormData({ ...formData, body: formData.body + token });
    }
  }

  // 本地简单变量替换预览（前端本地即可，避免每次输入都请求后端）
  function renderLocalPreview(template: string): string {
    if (!template) return '';
    let result = template;
    // 优先使用 {key} 格式，同时支持 {{key}} 格式
    for (const [k, v] of Object.entries(sampleVariables)) {
      result = result.split(`{${k}}`).join(v).split(`{{${k}}}`).join(v);
    }
    return result;
  }

  useEffect(() => {
    if (activeTab === 'overview') loadOverview();
    if (activeTab === 'list') loadList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab]);

  useEffect(() => {
    if (activeTab === 'list') loadList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, search, eventTypeFilter, activeFilter]);

  async function loadOverview() {
    setLoading(true);
    try {
      const data = await notificationApi.getTemplateOverview();
      setOverview(data);
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } }; message?: string };
      showToast({
        type: 'error',
        text: t('notifications.templates.overviewLoadFailed', {
          message: errResp?.response?.data?.message || errResp?.message || '',
        }),
      });
    } finally {
      setLoading(false);
    }
  }

  async function loadList() {
    setLoading(true);
    try {
      const data = await notificationApi.listTemplates({
        page,
        pageSize,
        eventType: eventTypeFilter || undefined,
        isActive: activeFilter ?? undefined,
        search: search || undefined,
      });
      setTemplates(data.data);
      setTotalItems(data.totalItems);
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } }; message?: string };
      showToast({
        type: 'error',
        text: t('notifications.templates.listLoadFailed', {
          message: errResp?.response?.data?.message || errResp?.message || '',
        }),
      });
    } finally {
      setLoading(false);
    }
  }

  function openCreate() {
    setEditingTemplate(null);
    const initial: CreateNotificationTemplateRequest = {
      name: '',
      subject: '',
      body: '',
      eventType: 'new_conversation',
      isActive: true,
    };
    setFormData(initial);
    setFormError('');
    setShowEditor(true);
    // 用初始数据初始化本地预览
    setTimeout(() => {
      setEditorViewMode('split');
      setVariableInsertTarget('body');
      setVariableSearch('');
      setLocalPreview({
        subject: renderLocalPreview(initial.subject),
        body: renderLocalPreview(initial.body),
      });
    }, 0);
  }

  async function openEdit(tpl: NotificationTemplateResponse) {
    try {
      const data = await notificationApi.getTemplate(tpl.id);
      setEditingTemplate(data);
      const initial: CreateNotificationTemplateRequest = {
        name: data.name,
        subject: data.subject,
        body: data.body,
        eventType: data.eventType,
        isActive: data.isActive,
      };
      setFormData(initial);
      setFormError('');
      setShowEditor(true);
      setTimeout(() => {
        setEditorViewMode('split');
        setVariableInsertTarget('body');
        setVariableSearch('');
        setLocalPreview({
          subject: renderLocalPreview(initial.subject),
          body: renderLocalPreview(initial.body),
        });
      }, 0);
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } }; message?: string };
      showToast({
        type: 'error',
        text: t('notifications.templates.templateLoadFailed', {
          message: errResp?.response?.data?.message || errResp?.message || '',
        }),
      });
    }
  }

  async function handleSave() {
    if (!formData.name.trim()) {
      setFormError(t('notifications.templates.nameRequired'));
      return;
    }
    if (!formData.subject.trim()) {
      setFormError(t('notifications.templates.subjectRequired'));
      return;
    }
    if (!formData.eventType) {
      setFormError(t('notifications.common.selectEventType'));
      return;
    }

    setSaving(true);
    setFormError('');
    try {
      if (editingTemplate) {
        await notificationApi.updateTemplate(editingTemplate.id, formData);
        showToast({ type: 'success', text: t('notifications.templates.templateUpdated') });
      } else {
        await notificationApi.createTemplate(formData);
        showToast({ type: 'success', text: t('notifications.templates.templateCreated') });
      }
      setShowEditor(false);
      if (activeTab === 'overview') loadOverview();
      if (activeTab === 'list') loadList();
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } } };
      setFormError(errResp?.response?.data?.message || t('notifications.common.saveFailed'));
    } finally {
      setSaving(false);
    }
  }

  async function handleToggleActive(tpl: NotificationTemplateResponse) {
    try {
      await notificationApi.toggleTemplateActive(tpl.id);
      showToast({
        type: 'success',
        text: t('notifications.templates.toggleSuccess', {
          state: tpl.isActive ? t('notifications.common.disabled') : t('notifications.common.enable'),
        }),
      });
      if (activeTab === 'overview') loadOverview();
      if (activeTab === 'list') loadList();
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } }; message?: string };
      showToast({
        type: 'error',
        text: t('notifications.common.opFailed', {
          message: errResp?.response?.data?.message || errResp?.message || '',
        }),
      });
    }
  }

  async function handleDelete(tpl: NotificationTemplateResponse) {
    try {
      await notificationApi.removeTemplate(tpl.id);
      setDeleteTarget(null);
      showToast({ type: 'success', text: t('notifications.templates.templateMovedToTrash') });
      if (activeTab === 'overview') loadOverview();
      if (activeTab === 'list') loadList();
      refreshTrashCount();
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } }; message?: string };
      showToast({
        type: 'error',
        text: t('notifications.templates.deleteFailedWithDetail', {
          message: errResp?.response?.data?.message || errResp?.message || '',
        }),
      });
    }
  }

  async function handleDuplicate(tpl: NotificationTemplateResponse) {
    try {
      const newTpl = await notificationApi.duplicateTemplate(tpl.id);
      showToast({ type: 'success', text: t('notifications.common.copiedAs', { name: newTpl.name }) });
      if (activeTab === 'overview') loadOverview();
      if (activeTab === 'list') loadList();
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } }; message?: string };
      showToast({
        type: 'error',
        text: t('notifications.common.copyFailed', {
          message: errResp?.response?.data?.message || errResp?.message || '',
        }),
      });
    }
  }

  const validateVariables = useCallback(async () => {
    if (!showEditor) return;
    try {
      const result = await notificationApi.validateTemplateVariables(formData.subject, formData.body);
      setVarValidateResult(result);
    } catch {
      // ignore
    }
  }, [formData.subject, formData.body, showEditor]);

  async function handlePreviewById(id: number) {
    setPreviewLoading(true);
    try {
      const result = await notificationApi.previewTemplateById(id, {});
      setPreviewData(result);
      setPreviewMode('html');
      setShowPreview(true);
    } catch (e: unknown) {
      console.error('预览失败', e);
      const errResp = e as { response?: { data?: { message?: string } } };
      showToast({ type: 'error', text: errResp?.response?.data?.message || t('notifications.common.previewFailed') });
    } finally {
      setPreviewLoading(false);
    }
  }

  async function handlePreviewFromEditor() {
    setPreviewLoading(true);
    try {
      // 复用组件顶部的 sampleVariables（同一份示例数据）
      const result = await notificationApi.preview(formData.body, sampleVariables, formData.subject);
      setPreviewData({ subject: result.renderedSubject ?? formData.subject, body: result.rendered || formData.body });
      setPreviewMode('html');
      setShowPreview(true);
    } catch (e: unknown) {
      console.error('预览失败', e);
      const errResp = e as { response?: { data?: { message?: string } } };
      showToast({ type: 'error', text: errResp?.response?.data?.message || t('notifications.common.previewFailed') });
    } finally {
      setPreviewLoading(false);
    }
  }

  useEffect(() => {
    if (!showEditor) {
      setVarValidateResult(null);
      return;
    }
    const timer = setTimeout(validateVariables, 500);
    return () => clearTimeout(timer);
  }, [formData.subject, formData.body, showEditor, validateVariables]);

  // 编辑器内本地实时预览（防抖 200ms，前端本地替换变量，无后端请求）
  useEffect(() => {
    if (!showEditor) return;
    if (localPreviewDebounceTimer) {
      window.clearTimeout(localPreviewDebounceTimer);
    }
    const timer = window.setTimeout(() => {
      setLocalPreview({
        subject: renderLocalPreview(formData.subject),
        body: renderLocalPreview(formData.body),
      });
    }, 200);
    setLocalPreviewDebounceTimer(timer);
    return () => {
      window.clearTimeout(timer);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formData.subject, formData.body, showEditor]);

  // ====== 垃圾箱 ======
  function refreshTrashCount() {
    notificationApi.getTemplateTrashCount().then((res) => {
      const count = res?.count ?? res ?? 0;
      setTrashCount(typeof count === 'number' ? count : 0);
    }).catch(() => {});
  }

  function refreshTrashList() {
    setTrashLoading(true);
    notificationApi.listTemplateTrash({
      page: trashQuery.page,
      pageSize: trashQuery.pageSize,
      search: trashQuery.keyword || undefined,
    })
      .then(setTrashList)
      .catch(() => showToast({ type: 'error', text: t('notifications.common.trashLoadFailed') }))
      .finally(() => setTrashLoading(false));
  }

  function openTrash() {
    setTrashOpen(true);
    resetTrashKeywordSearch();
    setTrashQuery({ page: 1, pageSize: trashPageSize, keyword: '' });
    refreshTrashCount();
  }

  useEffect(() => {
    refreshTrashCount();
  }, []);

  useEffect(() => {
    if (trashOpen) refreshTrashList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [trashOpen, trashQuery]);

  async function handleRestore() {
    if (!restoreTarget) return;
    setTrashActionLoading(true);
    try {
      await notificationApi.restoreTemplate(restoreTarget.id);
      showToast({ type: 'success', text: t('notifications.templates.templateRestored', { name: restoreTarget.name }) });
      setRestoreTarget(null);
      if (activeTab === 'overview') loadOverview();
      if (activeTab === 'list') loadList();
      refreshTrashList();
      refreshTrashCount();
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } } };
      showToast({ type: 'error', text: errResp?.response?.data?.message || t('notifications.common.restoreFailed') });
    } finally {
      setTrashActionLoading(false);
    }
  }

  async function handleHardDelete() {
    if (!hardDeleteTarget) return;
    setTrashActionLoading(true);
    try {
      await notificationApi.hardDeleteTemplate(hardDeleteTarget.id);
      showToast({ type: 'success', text: t('notifications.templates.templateHardDeleted', { name: hardDeleteTarget.name }) });
      setHardDeleteTarget(null);
      refreshTrashList();
      refreshTrashCount();
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } } };
      showToast({ type: 'error', text: errResp?.response?.data?.message || t('notifications.common.deleteFailed') });
    } finally {
      setTrashActionLoading(false);
    }
  }

  async function handleClearTrash() {
    setTrashActionLoading(true);
    try {
      const res = await notificationApi.clearTemplateTrash() as { count?: number } | null;
      const count = res?.count ?? 0;
      showToast({ type: 'success', text: t('notifications.templates.trashEmptied', { count }) });
      setClearTrashOpen(false);
      refreshTrashList();
      refreshTrashCount();
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } } };
      showToast({ type: 'error', text: errResp?.response?.data?.message || t('notifications.common.clearFailed') });
    } finally {
      setTrashActionLoading(false);
    }
  }

  const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));

  return (
    <div className="space-y-5">
      {/* 页头 Hero */}
      <div className="relative overflow-hidden rounded-2xl border border-primary-100/60 dark:border-slate-700/60 bg-gradient-to-br from-primary-50 via-white to-indigo-50 dark:from-slate-800 dark:via-slate-900 dark:to-indigo-900/20 p-6 md:p-7 shadow-sm">
        <div className="absolute -top-16 -right-14 w-64 h-64 rounded-full bg-primary-400/20 dark:bg-primary-500/20 blur-3xl pointer-events-none" />
        <div className="absolute -bottom-16 -left-10 w-72 h-72 rounded-full bg-indigo-400/20 dark:bg-indigo-500/15 blur-3xl pointer-events-none" />
        <div className="relative flex items-center gap-4 md:gap-5">
          <div className="relative w-14 h-14 md:w-16 md:h-16 shrink-0 rounded-2xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-lg shadow-primary-500/30 flex items-center justify-center ring-1 ring-white/40 dark:ring-white/10">
            <FileText className="w-7 h-7 md:w-8 md:h-8" strokeWidth={2.2} />
          </div>
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 mb-1">
              <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[11px] font-bold uppercase tracking-wider rounded-full bg-primary-100/80 dark:bg-primary-900/40 text-primary-700 dark:text-primary-300 ring-1 ring-primary-500/20">
                <Zap className="w-3 h-3" />
                Notification Center
              </span>
            </div>
            <h1 className="text-2xl md:text-[28px] font-extrabold tracking-tight text-gray-900 dark:text-slate-50 leading-tight">
              {t('notifications.templates.title')}
            </h1>
            <p className="mt-1 text-sm md:text-[15px] text-gray-600 dark:text-slate-400 leading-relaxed">
              {t('notifications.templates.subtitle')}
            </p>
          </div>
          <div className="hidden lg:flex items-center gap-3 shrink-0">
            <div className="flex items-center gap-2 px-4 py-2 rounded-xl bg-white/70 dark:bg-slate-800/60 backdrop-blur border border-white/50 dark:border-slate-700/60 shadow-sm">
              <div className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
              <span className="text-xs font-semibold text-gray-700 dark:text-slate-300">{t('notifications.templates.heroBadge')}</span>
            </div>
          </div>
        </div>
      </div>

      {/* Tab 切换（胶囊渐变） */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200 dark:border-slate-700/60 p-1.5 inline-flex flex-wrap gap-1 shadow-sm">
        <button
          className={`${pillTabBase} ${
            activeTab === 'overview'
              ? 'text-white bg-gradient-to-br from-primary-600 to-indigo-600 shadow-md shadow-primary-500/25 ring-1 ring-primary-400/30'
              : 'text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700/60'
          }`}
          onClick={() => setActiveTab('overview')}
        >
          <Activity className="w-4 h-4" />
          {t('notifications.templates.tabOverview')}
        </button>
        <button
          className={`${pillTabBase} ${
            activeTab === 'list'
              ? 'text-white bg-gradient-to-br from-primary-600 to-indigo-600 shadow-md shadow-primary-500/25 ring-1 ring-primary-400/30'
              : 'text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700/60'
          }`}
          onClick={() => setActiveTab('list')}
        >
          <List className="w-4 h-4" />
          {t('notifications.templates.tabList')}
        </button>
      </div>

      {/* 概览 Tab */}
      {activeTab === 'overview' && overview && (
        <div className="space-y-5">
          {/* 统计卡片 */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4 md:gap-5">
            {([
              { label: 'notifications.templates.statTotal', value: overview.total, icon: FileText, hint: 'notifications.templates.statTotalHint', palette: statPalette[0] },
              { label: 'notifications.common.enabled', value: overview.active, icon: CheckCircle2, hint: 'notifications.templates.statActiveHint', palette: statPalette[1] },
              { label: 'notifications.common.disabled', value: overview.inactive, icon: Shield, hint: 'notifications.templates.statInactiveHint', palette: statPalette[2] },
            ] satisfies OverviewStat[]).map((s) => (
              <div
                key={s.label}
                className="relative overflow-hidden bg-white dark:bg-slate-800 rounded-2xl border border-gray-200 dark:border-slate-700/60 p-5 md:p-6 shadow-sm hover:shadow-lg hover:-translate-y-0.5 transition-all duration-200"
              >
                <div className={`absolute top-0 left-0 w-1.5 h-full bg-gradient-to-b ${s.palette.from} ${s.palette.to}`} />
                <div className={`absolute -top-8 -right-8 w-28 h-28 rounded-full bg-gradient-to-br ${s.palette.from} ${s.palette.to} opacity-10 blur-2xl`} />
                <div className="relative flex items-start justify-between">
                  <div>
                    <p className="text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400">{t(s.label)}</p>
                    <div className="mt-3 flex items-baseline gap-2">
                      <span className={`text-4xl font-black bg-gradient-to-br ${s.palette.from} ${s.palette.to} bg-clip-text text-transparent tabular-nums`}>
                        {s.value}
                      </span>
                      <span className="text-xs text-gray-400 dark:text-slate-500">{t('notifications.templates.statUnit')}</span>
                    </div>
                    <p className="mt-2 text-xs text-gray-500 dark:text-slate-400">{t(s.hint)}</p>
                  </div>
                  <div className={`relative p-3 rounded-2xl ring-1 ${s.palette.bgSoft} ${s.palette.ringSoft} shadow-sm`}>
                    <s.icon className={`w-6 h-6 ${s.palette.textLight}`} strokeWidth={2} />
                  </div>
                </div>
              </div>
            ))}
          </div>

          {/* 按事件类型分布 */}
          <div className="relative overflow-hidden bg-white dark:bg-slate-800 rounded-2xl border border-gray-200 dark:border-slate-700/60 shadow-sm">
            <div className={`px-6 py-4 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50/80 to-white dark:from-slate-900/50 dark:to-slate-800/50`}>
              <div className="flex items-center justify-between flex-wrap gap-3">
                <div className="flex items-center gap-2.5">
                  <div className="p-2 rounded-xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-sm shadow-primary-500/30">
                    <Tag className="w-4 h-4" />
                  </div>
                  <div>
                    <h3 className="text-base font-bold text-gray-900 dark:text-slate-100">{t('notifications.templates.distributionTitle')}</h3>
                    <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{t('notifications.templates.distributionSubtitle')}</p>
                  </div>
                </div>
                <div className="flex items-center gap-3 text-xs">
                  <div className="flex items-center gap-1.5">
                    <span className="w-2.5 h-2.5 rounded-full bg-emerald-500" />
                    <span className="text-gray-500 dark:text-slate-400">{t('notifications.templates.activeRatio')}</span>
                  </div>
                  <div className="flex items-center gap-1.5">
                    <span className="w-2.5 h-2.5 rounded-full bg-amber-400" />
                    <span className="text-gray-500 dark:text-slate-400">{t('notifications.templates.inactiveRatio')}</span>
                  </div>
                </div>
              </div>
            </div>
            <div className="p-5 md:p-6 space-y-3.5">
              {overview.events.length === 0 ? (
                <EmptyState icon={FileText} title={t('notifications.templates.emptyNone')} subtitle={t('notifications.templates.emptyOverviewHint')} />
              ) : (
                overview.events.map((evt) => {
                  const c = evtTypeColor(evt.eventType);
                  const total = overview.total;
                  const ratio = total ? (evt.count / total) * 100 : 0;
                  const activeRatio = evt.count ? (evt.activeCount / evt.count) * 100 : 0;
                  const inactiveRatio = 100 - activeRatio;
                  return (
                    <div key={evt.eventType} className="group">
                      <div className="flex items-center justify-between mb-1.5">
                        <div className="flex items-center gap-2 min-w-0">
                          <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-semibold ring-1 ${c.bg} ${c.text} ${c.ring}`}>
                            <span className={`w-1.5 h-1.5 rounded-full ${c.accent}`} />
                            {getEventTypeLabel(t, evt.eventType)}
                          </span>
                        </div>
                        <div className="flex items-center gap-2">
                          <span className="text-[11px] text-gray-500 dark:text-slate-400">
                            {t('notifications.templates.ratioLabel')} <span className="font-bold text-gray-700 dark:text-slate-300 tabular-nums">{ratio.toFixed(0)}%</span>
                          </span>
                          <span className="text-xs font-mono font-semibold text-gray-600 dark:text-slate-400 bg-gray-100 dark:bg-slate-700/50 px-2 py-0.5 rounded-lg">
                            {evt.activeCount}/{evt.count}
                          </span>
                        </div>
                      </div>
                      {/* 外层：总体占总体比例 */}
                      <div className="h-5 w-full rounded-full bg-gray-100 dark:bg-slate-700/50 overflow-hidden ring-1 ring-inset ring-gray-200/60 dark:ring-slate-700/40">
                        <div
                          className={`h-full ${c.bg} rounded-full transition-all duration-700 relative overflow-hidden`}
                          style={{ width: `${Math.max(ratio, 2)}%` }}
                        >
                          {/* 内层：启用/停用分段双色 */}
                          <div className="absolute inset-0 flex">
                            <div
                              className={`${c.accent} opacity-95`}
                              style={{ width: `${activeRatio}%` }}
                            />
                            <div
                              className="bg-amber-400/80 dark:bg-amber-400/70"
                              style={{ width: `${inactiveRatio}%` }}
                            />
                          </div>
                        </div>
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>
        </div>
      )}

      {/* 列表 Tab */}
      {activeTab === 'list' && (
        <div className="space-y-4">
          {/* 筛选栏 */}
          <div className="relative bg-white dark:bg-slate-800 rounded-2xl border border-gray-200 dark:border-slate-700/60 p-4 shadow-sm">
            {/* 筛选摘要（显示激活的筛选条件） */}
            {(search || eventTypeFilter || activeFilter !== null) && (
              <div className="flex flex-wrap items-center gap-1.5 pb-3 mb-3 border-b border-dashed border-gray-200 dark:border-slate-700/60">
                <span className="text-xs font-bold text-gray-500 dark:text-slate-400 uppercase tracking-wider mr-1">{t('notifications.common.currentFilters')}</span>
                {search && (
                  <span className="inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-lg bg-primary-50 dark:bg-primary-900/30 text-primary-700 dark:text-primary-300 ring-1 ring-primary-500/20">
                    {t('notifications.templates.filterSearch')}{search}
                    <button onClick={clearSearch} className="hover:text-primary-900">
                      <X className="w-3 h-3" />
                    </button>
                  </span>
                )}
                {eventTypeFilter && (
                  <span className="inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-lg bg-indigo-50 dark:bg-indigo-900/30 text-indigo-700 dark:text-indigo-300 ring-1 ring-indigo-500/20">
                    {t('notifications.templates.filterEvent')}{getEventTypeLabel(t, eventTypeFilter)}
                    <button onClick={() => { setEventTypeFilter(null); setPage(1); }} className="hover:text-indigo-900">
                      <X className="w-3 h-3" />
                    </button>
                  </span>
                )}
                {activeFilter !== null && (
                  <span className={`inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-lg ring-1 ${
                    activeFilter
                      ? 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-300 ring-emerald-500/20'
                      : 'bg-gray-100 dark:bg-slate-700 text-gray-700 dark:text-slate-300 ring-gray-500/20'
                  }`}>
                    {t('notifications.templates.filterStatus')}{activeFilter ? t('notifications.common.enabled') : t('notifications.common.disabled')}
                    <button onClick={() => { setActiveFilter(null); setPage(1); }}>
                      <X className="w-3 h-3" />
                    </button>
                  </span>
                )}
                <button
                  onClick={() => { clearSearch(); setEventTypeFilter(null); setActiveFilter(null); }}
                  className="ml-auto inline-flex items-center gap-1 px-2 py-0.5 text-xs font-semibold text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-200"
                >
                  <RotateCcw className="w-3 h-3" />
                  {t('common.reset')}
                </button>
              </div>
            )}
            <div className="flex flex-wrap items-center gap-2 md:gap-3">
              <div className="flex-1 min-w-[140px] md:min-w-[220px] relative">
                <Search className="w-4 h-4 absolute left-3.5 top-1/2 -translate-y-1/2 text-gray-400" />
                <input
                  type="text"
                  placeholder={t('notifications.templates.searchPlaceholder')}
                  value={searchInput}
                  onChange={(e) => handleSearchInput(e.target.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter') triggerSearchNow(); }}
                  className={inputBase + ' pl-10'}
                />
              </div>
              <div className="flex items-center bg-gray-50 dark:bg-slate-900/50 rounded-xl border border-gray-200 dark:border-slate-700 p-1 shadow-inner">
                {([
                  { key: null as boolean | null, label: 'notifications.common.all' },
                  { key: true as boolean | null, label: 'notifications.common.enable' },
                  { key: false as boolean | null, label: 'notifications.common.disable' },
                ] satisfies { key: boolean | null; label: TranslationKey }[]).map((opt) => (
                  <button
                    key={String(opt.key)}
                    onClick={() => { setActiveFilter(opt.key); setPage(1); }}
                    className={`px-2.5 md:px-3 py-1.5 text-xs font-semibold rounded-lg transition-all ${
                      activeFilter === opt.key
                        ? 'bg-white dark:bg-slate-800 text-primary-700 dark:text-primary-300 shadow-sm ring-1 ring-primary-500/20'
                        : 'text-gray-600 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200'
                    }`}
                  >
                    {t(opt.label)}
                  </button>
                ))}
              </div>
              <select
                value={eventTypeFilter || ''}
                onChange={(e) => { setEventTypeFilter(e.target.value || null); setPage(1); }}
                className={inputBase + ' flex-1 min-w-[120px] md:!w-auto md:!py-2.5'}
              >
                <option value="">{t('notifications.templates.allEventTypes')}</option>
                {eventTypeOptions.map((opt) => (
                  <option key={opt.value} value={opt.value}>{t(opt.label)}</option>
                ))}
              </select>
              <button
                onClick={openTrash}
                className="relative inline-flex items-center gap-1.5 px-3 md:px-4 py-2.5 bg-gray-50 dark:bg-slate-900/60 border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-200 text-sm font-semibold rounded-xl hover:bg-gray-100 dark:hover:bg-slate-700 transition shadow-sm"
                title={t('notifications.common.trash')}
              >
                <Trash className="w-4 h-4 text-gray-500 dark:text-slate-400" />
                <span className="hidden sm:inline">{t('notifications.common.trash')}</span>
                {trashCount > 0 && (
                  <span className="absolute -top-1.5 -right-1.5 min-w-[22px] h-[22px] px-1.5 flex items-center justify-center text-[11px] font-black text-white bg-gradient-to-br from-red-500 to-rose-500 rounded-full shadow-md shadow-red-500/40 ring-2 ring-white dark:ring-slate-800">
                    {trashCount > 99 ? '99+' : trashCount}
                  </span>
                )}
              </button>
              <button
                onClick={openCreate}
                className={btnPrimary + ' !px-3.5 md:!px-4.5 !py-2.5'}
              >
                <Plus className="w-4 h-4" />
                <span className="hidden sm:inline">{t('notifications.templates.newTemplate')}</span>
                <span className="sm:hidden">{t('notifications.common.new')}</span>
              </button>
            </div>
          </div>

          {/* 列表表格 */}
          <div className="relative bg-white dark:bg-slate-800 rounded-2xl border border-gray-200 dark:border-slate-700/60 overflow-hidden shadow-sm">
            {loading ? (
              <LoadingState label="notifications.templates.loadingList" />
            ) : templates.length === 0 ? (
              <EmptyState
                icon={FileText}
                title={t('notifications.templates.emptyNone')}
                subtitle={search || eventTypeFilter || activeFilter !== null ? t('notifications.templates.emptyListFiltered') : t('notifications.templates.emptyListNone')}
                action={
                  (search || eventTypeFilter || activeFilter !== null) ? (
                    <button
                      onClick={() => { setSearch(''); setEventTypeFilter(null); setActiveFilter(null); setPage(1); }}
                      className={btnSecondary}
                    >
                      <RotateCcw className="w-4 h-4" />
                      {t('notifications.templates.clearAllFilters')}
                    </button>
                  ) : (
                    <button onClick={openCreate} className={btnPrimary}>
                      <Plus className="w-4 h-4" />
                      {t('notifications.templates.newTemplate')}
                    </button>
                  )
                }
              />
            ) : (
              <>
              {/* PC 端表格视图 */}
              <div className="hidden md:block overflow-x-auto">
                <table className="min-w-full divide-y divide-gray-200 dark:divide-slate-700/50">
                  <thead className="bg-gradient-to-r from-gray-50 via-white to-gray-50 dark:from-slate-900 dark:via-slate-800/50 dark:to-slate-900 border-b border-gray-200 dark:border-slate-700/60">
                    <tr>
                      <th className="text-left px-5 py-3.5 text-xs font-extrabold text-gray-600 dark:text-slate-400 uppercase tracking-[0.08em] w-10" />
                      <th className="text-left px-5 py-3.5 text-xs font-extrabold text-gray-600 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.templates.templateName')}</th>
                      <th className="text-left px-5 py-3.5 text-xs font-extrabold text-gray-600 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.eventType')}</th>
                      <th className="text-left px-5 py-3.5 text-xs font-extrabold text-gray-600 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.subject')}</th>
                      <th className="text-left px-5 py-3.5 text-xs font-extrabold text-gray-600 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.status')}</th>
                      <th className="text-left px-5 py-3.5 text-xs font-extrabold text-gray-600 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.updatedAt')}</th>
                      <th className="text-right px-5 py-3.5 text-xs font-extrabold text-gray-600 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.actions')}</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100 dark:divide-slate-700/50">
                    {templates.map((tpl, idx) => {
                      const ec = evtTypeColor(tpl.eventType);
                      return (
                        <tr
                          key={tpl.id}
                          className={`group transition-colors ${
                            idx % 2 === 0
                              ? 'bg-white dark:bg-slate-800 hover:bg-gray-50 dark:hover:bg-slate-700/30'
                              : 'bg-gray-50/40 dark:bg-slate-900/30 hover:bg-gray-50 dark:hover:bg-slate-700/40'
                          }`}
                        >
                          {/* 左侧事件彩条 */}
                          <td className="relative pl-5 pr-0 py-4 w-10">
                            <div className={`w-1 h-10 rounded-full ${ec.accent} shadow-[0_0_8px_rgba(0,0,0,0.12)]`} />
                          </td>
                          <td className="px-5 py-4">
                            <div className="flex items-center gap-2.5 min-w-0">
                              <div className={`shrink-0 w-9 h-9 rounded-xl ${ec.bg} ${ec.ring} ring-1 flex items-center justify-center`}>
                                <Mail className={`w-4 h-4 ${ec.text}`} />
                              </div>
                              <div className="min-w-0">
                                <div className="text-sm font-bold text-gray-900 dark:text-slate-100 truncate">{tpl.name}</div>
                                <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5 flex items-center gap-1">
                                  <Clock className="w-3 h-3" />
                                  {t('notifications.templates.createdId', { id: tpl.id })}
                                </div>
                              </div>
                            </div>
                          </td>
                          <td className="px-5 py-4">
                            <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-xl text-[11px] font-bold ring-1 ${ec.bg} ${ec.text} ${ec.ring}`}>
                              <span className={`w-1.5 h-1.5 rounded-full ${ec.accent}`} />
                              {getEventTypeLabel(t, tpl.eventType)}
                            </span>
                          </td>
                          <td className="px-5 py-4 max-w-xs">
                            <div className="group/td relative">
                              <div className="text-sm text-gray-600 dark:text-slate-400 truncate pr-6" title={tpl.subject}>{tpl.subject || <span className="text-gray-400 dark:text-slate-500 italic">{t('notifications.templates.subjectNotSet')}</span>}</div>
                              {tpl.subject && (
                                <button
                                  onClick={() => navigator.clipboard.writeText(tpl.subject).then(() => showToast({ type: 'success', text: t('notifications.templates.subjectCopied') })).catch(() => {})}
                                  className="absolute right-0 top-1/2 -translate-y-1/2 opacity-0 group-hover/td:opacity-100 p-1 rounded-md hover:bg-gray-100 dark:hover:bg-slate-700 transition"
                                  title={t('notifications.templates.copySubject')}
                                >
                                  <Copy className="w-3.5 h-3.5 text-gray-400" />
                                </button>
                              )}
                            </div>
                          </td>
                          <td className="px-5 py-4">
                            <button
                              onClick={() => handleToggleActive(tpl)}
                              className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-xl text-xs font-bold transition-all hover:shadow-sm ${
                                tpl.isActive
                                  ? 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-400 ring-1 ring-emerald-500/20 hover:bg-emerald-100 dark:hover:bg-emerald-900/50'
                                  : 'bg-gray-100 dark:bg-slate-700/50 text-gray-500 dark:text-slate-400 ring-1 ring-gray-500/10 hover:bg-gray-200 dark:hover:bg-slate-700'
                              }`}
                              title={tpl.isActive ? t('notifications.templates.clickToDisable') : t('notifications.templates.clickToEnable')}
                            >
                              {tpl.isActive ? (
                                <span className="relative flex w-1.5 h-1.5">
                                  <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75" />
                                  <span className="relative inline-flex rounded-full h-1.5 w-1.5 bg-emerald-500" />
                                </span>
                              ) : (
                                <XCircle className="w-3 h-3" />
                              )}
                              {tpl.isActive ? t('notifications.common.enabled') : t('notifications.common.disabled')}
                            </button>
                          </td>
                          <td className="px-5 py-4 text-xs text-gray-500 dark:text-slate-400 whitespace-nowrap font-mono tabular-nums">{formatDateTime(tpl.updatedAt)}</td>
                          <td className="px-5 py-4 text-right">
                            <div className="flex items-center justify-end gap-0.5 opacity-80 group-hover:opacity-100 transition">
                              <button
                                onClick={() => handlePreviewById(tpl.id)}
                                className="p-2 text-gray-500 dark:text-slate-400 hover:text-sky-600 hover:bg-sky-50 dark:hover:bg-sky-900/30 rounded-xl transition"
                                title={t('notifications.templates.previewTemplate')}
                              >
                                <Eye className="w-4 h-4" />
                              </button>
                              <button
                                onClick={() => handleDuplicate(tpl)}
                                className="p-2 text-gray-500 dark:text-slate-400 hover:text-violet-600 hover:bg-violet-50 dark:hover:bg-violet-900/30 rounded-xl transition"
                                title={t('notifications.templates.duplicateTemplate')}
                              >
                                <Copy className="w-4 h-4" />
                              </button>
                              <button
                                onClick={() => openEdit(tpl)}
                                className="p-2 text-gray-500 dark:text-slate-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition"
                                title={t('notifications.templates.editTemplate')}
                              >
                                <Edit2 className="w-4 h-4" />
                              </button>
                              <button
                                onClick={() => setDeleteTarget(tpl)}
                                className="p-2 text-gray-500 dark:text-slate-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-xl transition"
                                title={t('notifications.common.moveToTrash')}
                              >
                                <Trash2 className="w-4 h-4" />
                              </button>
                            </div>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>

              {/* 移动端卡片视图 */}
              <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
                {templates.map((tpl) => {
                  const ec = evtTypeColor(tpl.eventType);
                  return (
                    <div key={tpl.id} className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700/30 space-y-3">
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex items-center gap-2.5 min-w-0 flex-1">
                          <div className={`shrink-0 w-9 h-9 rounded-xl ${ec.bg} ${ec.ring} ring-1 flex items-center justify-center`}>
                            <Mail className={`w-4 h-4 ${ec.text}`} />
                          </div>
                          <div className="min-w-0">
                            <div className="text-sm font-bold text-gray-900 dark:text-slate-100 truncate">{tpl.name}</div>
                            <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5">ID · {tpl.id}</div>
                          </div>
                        </div>
                        <button
                          onClick={() => handleToggleActive(tpl)}
                          className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-xl text-xs font-bold transition-all flex-shrink-0 ${
                            tpl.isActive
                              ? 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-400 ring-1 ring-emerald-500/20'
                              : 'bg-gray-100 dark:bg-slate-700/50 text-gray-500 dark:text-slate-400 ring-1 ring-gray-500/10'
                          }`}
                          title={tpl.isActive ? t('notifications.templates.clickToDisable') : t('notifications.templates.clickToEnable')}
                        >
                          {tpl.isActive ? t('notifications.common.enabled') : t('notifications.common.disabled')}
                        </button>
                      </div>

                      <div className="flex flex-wrap items-center gap-1.5">
                        <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-xl text-[11px] font-bold ring-1 ${ec.bg} ${ec.text} ${ec.ring}`}>
                          <span className={`w-1.5 h-1.5 rounded-full ${ec.accent}`} />
                          {getEventTypeLabel(t, tpl.eventType)}
                        </span>
                      </div>

                      <div className="text-xs text-gray-600 dark:text-slate-400 line-clamp-2">
                        {tpl.subject || <span className="text-gray-400 dark:text-slate-500 italic">{t('notifications.common.unsetSubject')}</span>}
                      </div>

                      <div className="flex items-center justify-between pt-1">
                        <div className="text-[11px] text-gray-400 dark:text-slate-500 font-mono">
                          {formatDateTime(tpl.updatedAt)}
                        </div>
                        <div className="flex items-center gap-1">
                          <button
                            onClick={() => handlePreviewById(tpl.id)}
                            className="p-2 text-gray-500 dark:text-slate-400 hover:text-sky-600 hover:bg-sky-50 dark:hover:bg-sky-900/30 rounded-xl transition"
                            title={t('notifications.templates.previewTemplate')}
                          >
                            <Eye className="w-4 h-4" />
                          </button>
                          <button
                            onClick={() => handleDuplicate(tpl)}
                            className="p-2 text-gray-500 dark:text-slate-400 hover:text-violet-600 hover:bg-violet-50 dark:hover:bg-violet-900/30 rounded-xl transition"
                            title={t('notifications.templates.duplicateTemplate')}
                          >
                            <Copy className="w-4 h-4" />
                          </button>
                          <button
                            onClick={() => openEdit(tpl)}
                            className="p-2 text-gray-500 dark:text-slate-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition"
                            title={t('notifications.templates.editTemplate')}
                          >
                            <Edit2 className="w-4 h-4" />
                          </button>
                          <button
                            onClick={() => setDeleteTarget(tpl)}
                            className="p-2 text-gray-500 dark:text-slate-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-xl transition"
                            title={t('notifications.common.moveToTrash')}
                          >
                            <Trash2 className="w-4 h-4" />
                          </button>
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
              </>
            )}

            {totalItems > 0 && (
              <div className="px-5 py-3.5 border-t border-gray-100 dark:border-slate-700/50 flex items-center justify-between flex-wrap gap-3 bg-gradient-to-r from-gray-50/50 via-white to-gray-50/50 dark:from-slate-900/30 dark:via-slate-800 dark:to-slate-900/30">
                <div className="text-xs text-gray-500 dark:text-slate-400 flex items-center gap-1.5">
                  <FileText className="w-3.5 h-3.5" />
                  {t('notifications.common.paginationPrefix')}
                  <span className="font-bold text-gray-700 dark:text-slate-300 tabular-nums">{totalItems}</span>
                  {t('notifications.common.paginationMiddle')}
                  <span className="font-bold text-gray-700 dark:text-slate-300 tabular-nums">{page}/{totalPages}</span>
                  {t('notifications.common.paginationSuffix')}
                </div>
                <div className="flex items-center gap-1.5">
                  <button
                    onClick={() => setPage(Math.max(1, page - 1))}
                    disabled={page <= 1}
                    className={`inline-flex items-center gap-1 px-3.5 py-2 rounded-xl text-xs font-bold transition shadow-sm ${
                      page <= 1
                        ? 'bg-gray-100 dark:bg-slate-700/40 text-gray-400 dark:text-slate-500 cursor-not-allowed'
                        : 'bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-200 hover:bg-gray-50 dark:hover:bg-slate-700'
                    }`}
                  >
                    <ChevronLeft className="w-4 h-4" />
                    {t('notifications.common.previousPage')}
                  </button>
                  <button
                    onClick={() => setPage(Math.min(totalPages, page + 1))}
                    disabled={page >= totalPages}
                    className={`inline-flex items-center gap-1 px-3.5 py-2 rounded-xl text-xs font-bold transition shadow-sm ${
                      page >= totalPages
                        ? 'bg-gray-100 dark:bg-slate-700/40 text-gray-400 dark:text-slate-500 cursor-not-allowed'
                        : 'bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-200 hover:bg-gray-50 dark:hover:bg-slate-700'
                    }`}
                  >
                    {t('notifications.common.nextPage')}
                    <ChevronRight className="w-4 h-4" />
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      {/* 编辑器弹窗（自定义全屏沉浸式样式） */}
      {showEditor && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-2 md:p-5 bg-slate-900/60 backdrop-blur-sm animate-in fade-in duration-200">
          <div className="relative bg-white dark:bg-slate-900 rounded-2xl shadow-2xl w-full max-w-[1400px] h-[94vh] max-h-[94vh] flex flex-col ring-1 ring-black/5 dark:ring-white/10 overflow-hidden">
            {/* 装饰光斑 */}
            <div className="pointer-events-none absolute -top-24 -right-24 w-72 h-72 rounded-full bg-primary-500/20 blur-3xl" />
            <div className="pointer-events-none absolute top-20 -left-20 w-60 h-60 rounded-full bg-indigo-500/15 blur-3xl" />
            {/* 标题栏（渐变头部） */}
            <div className="relative px-6 py-4 md:px-7 md:py-5 border-b border-gray-200 dark:border-slate-800 flex items-center gap-4 flex-shrink-0 bg-gradient-to-br from-primary-50/70 via-white to-indigo-50/70 dark:from-slate-900 dark:via-slate-900 dark:to-indigo-900/20">
              <div className={`shrink-0 w-12 h-12 md:w-13 md:h-13 rounded-2xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-md shadow-primary-500/30 flex items-center justify-center ring-1 ring-white/40 dark:ring-white/10`}>
                <FileText className="w-6 h-6" strokeWidth={2.2} />
              </div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <h2 className="text-lg md:text-xl font-extrabold tracking-tight text-gray-900 dark:text-slate-50">
                    {editingTemplate ? t('notifications.templates.editorTitleEdit') : t('notifications.templates.editorTitleCreate')}
                  </h2>
                  <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider rounded-full bg-gradient-to-r from-emerald-100 to-teal-100 dark:from-emerald-900/40 dark:to-teal-900/40 text-emerald-700 dark:text-emerald-300 ring-1 ring-emerald-500/20">
                    <CheckCircle2 className="w-3 h-3" />
                    {t('notifications.templates.editorBadge')}
                  </span>
                </div>
                <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 leading-relaxed">
                  {t('notifications.templates.editorSubtitle')}
                </p>
              </div>
              <div className="flex items-center gap-2 shrink-0">
                <button
                  onClick={() => setShowEditor(false)}
                  className="p-2 text-gray-400 dark:text-slate-500 hover:text-gray-700 dark:hover:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-800 rounded-xl transition border border-transparent hover:border-gray-200 dark:hover:border-slate-700"
                  title={t('notifications.templates.closeEditor')}
                >
                  <X className="w-5 h-5" strokeWidth={2} />
                </button>
              </div>
            </div>

            {/* 主体（可滚动） */}
            <div className="flex-1 overflow-y-auto p-5 md:p-6 lg:p-7">
              {formError && (
                <div className="mb-5 p-4 rounded-2xl bg-gradient-to-br from-red-50 to-rose-50 dark:from-red-900/30 dark:to-rose-900/20 border border-red-100 dark:border-red-900/40 text-red-700 dark:text-red-300 text-sm flex items-start gap-2.5 shadow-sm">
                  <div className="p-1 rounded-lg bg-red-100 dark:bg-red-900/40 text-red-600 dark:text-red-400 shrink-0">
                    <AlertCircle className="w-4 h-4" />
                  </div>
                  <div className="flex-1 pt-0.5 font-medium">{formError}</div>
                </div>
              )}

              {/* 基础字段卡片 */}
              <div className="relative mb-6 rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm overflow-hidden">
                <div className="px-5 py-3.5 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50/60 via-white to-gray-50/60 dark:from-slate-900/50 dark:via-slate-800/60 dark:to-slate-900/50 flex items-center justify-between flex-wrap gap-2">
                  <div className="flex items-center gap-2">
                    <div className="p-1.5 rounded-lg bg-gradient-to-br from-primary-500 to-indigo-500 text-white">
                      <FileText className="w-3.5 h-3.5" />
                    </div>
                    <h3 className="text-sm font-extrabold text-gray-800 dark:text-slate-100 tracking-tight">{t('notifications.templates.basicInfo')}</h3>
                  </div>
                  <div className="flex items-center gap-1 text-[11px] text-gray-500 dark:text-slate-400">
                    <Users className="w-3 h-3" />
                    <span>{t('notifications.templates.requiredHint')}</span>
                  </div>
                </div>
                <div className="p-5 space-y-5">
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
                    <div>
                      <label className="block text-xs font-bold text-gray-600 dark:text-slate-400 uppercase tracking-wider mb-2">
                        {t('notifications.templates.templateName')} <span className="text-red-500 ml-0.5">*</span>
                      </label>
                      <input
                        type="text"
                        value={formData.name}
                        onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                        className={inputBase}
                        placeholder={t('notifications.templates.namePlaceholder')}
                      />
                    </div>
                    <div>
                      <label className="block text-xs font-bold text-gray-600 dark:text-slate-400 uppercase tracking-wider mb-2">
                        {t('notifications.common.eventType')} <span className="text-red-500 ml-0.5">*</span>
                      </label>
                      <select
                        value={formData.eventType}
                        onChange={(e) => setFormData({ ...formData, eventType: e.target.value })}
                        className={inputBase}
                      >
                        {eventTypeOptions.map((opt) => (
                          <option key={opt.value} value={opt.value}>{t(opt.label)}</option>
                        ))}
                      </select>
                    </div>
                  </div>

                  <div>
                    <div className="flex items-center justify-between mb-2 flex-wrap gap-2">
                      <label className="block text-xs font-bold text-gray-600 dark:text-slate-400 uppercase tracking-wider">
                        {t('notifications.templates.fieldSubject')} <span className="text-red-500 ml-0.5">*</span>
                      </label>
                      <button
                        onClick={() => setVariableInsertTarget('subject')}
                        className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-[11px] font-bold transition-all ${
                          variableInsertTarget === 'subject'
                            ? 'bg-gradient-to-br from-primary-600 to-indigo-600 text-white shadow-sm ring-1 ring-primary-500/30'
                            : 'bg-gray-50 dark:bg-slate-900/50 text-gray-500 dark:text-slate-400 hover:bg-gray-100 dark:hover:bg-slate-800 border border-gray-200 dark:border-slate-700'
                        }`}
                        title={t('notifications.templates.switchInsertTargetSubjectTitle')}
                      >
                        {variableInsertTarget === 'subject' ? <CheckCircle2 className="w-3 h-3" /> : <Plus className="w-3 h-3" />}
                        {variableInsertTarget === 'subject' ? t('notifications.templates.insertTargetSubject') : t('notifications.templates.switchToSubject')}
                      </button>
                    </div>
                    <input
                      type="text"
                      value={formData.subject}
                      onFocus={() => setVariableInsertTarget('subject')}
                      onChange={(e) => setFormData({ ...formData, subject: e.target.value })}
                      className={inputBase + ' font-mono tracking-tight'}
                      placeholder={t('notifications.templates.subjectPlaceholder')}
                    />
                    {localPreview && editorViewMode !== 'editor' && (
                      <div className="mt-2 flex items-start gap-2 px-3.5 py-2.5 rounded-xl bg-gradient-to-r from-emerald-50 to-teal-50 dark:from-emerald-900/25 dark:to-teal-900/20 border border-emerald-100 dark:border-emerald-900/40">
                        <div className="p-1 rounded-lg bg-emerald-100 dark:bg-emerald-900/40 text-emerald-600 dark:text-emerald-400 shrink-0 mt-0.5">
                          <Eye className="w-3 h-3" />
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="text-[10px] font-bold uppercase tracking-wider text-emerald-600 dark:text-emerald-400 mb-0.5">{t('notifications.templates.subjectPreview')}</div>
                          <div className="text-sm font-semibold text-gray-800 dark:text-slate-200 break-words leading-snug">
                            {localPreview.subject || <em className="text-gray-400 dark:text-slate-500 font-normal">{t('notifications.templates.emptyValue')}</em>}
                          </div>
                        </div>
                      </div>
                    )}
                  </div>
                </div>
              </div>

              {/* 主工作区：左编辑预览区 + 右变量面板 */}
              <div className="grid grid-cols-1 lg:grid-cols-4 gap-5">
                {/* 左：编辑/预览区（col-span-3） */}
                <div className="lg:col-span-3 space-y-4">
                  {/* 视图模式切换 tab + 插入目标提示 */}
                  <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 p-3 shadow-sm flex flex-wrap items-center justify-between gap-3">
                    <div className="inline-flex p-1 bg-gradient-to-r from-gray-50 to-gray-100 dark:from-slate-900 dark:to-slate-800 rounded-xl border border-gray-200 dark:border-slate-700">
                      {([
                        { mode: 'editor' as const, icon: Code2, label: 'notifications.templates.viewModeEditor', hint: 'notifications.templates.viewModeEditorHint' },
                        { mode: 'split' as const, icon: Columns3, label: 'notifications.templates.viewModeSplit', hint: 'notifications.templates.viewModeSplitHint' },
                        { mode: 'preview' as const, icon: MonitorPlay, label: 'notifications.templates.viewModePreview', hint: 'notifications.templates.viewModePreviewHint' },
                      ] satisfies EditorViewModeOption[]).map((modeOpt) => (
                        <button
                          key={modeOpt.mode}
                          onClick={() => setEditorViewMode(modeOpt.mode)}
                          className={`inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-bold rounded-lg transition-all ${
                            editorViewMode === modeOpt.mode
                              ? 'bg-white dark:bg-slate-700 text-primary-700 dark:text-primary-300 shadow-sm ring-1 ring-primary-500/25 scale-[1.01]'
                              : 'text-gray-600 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200 hover:bg-white/60 dark:hover:bg-slate-800/60'
                          }`}
                        >
                          <modeOpt.icon className="w-4 h-4" />
                          {t(modeOpt.label)}
                        </button>
                      ))}
                    </div>
                    <div className="inline-flex items-center gap-2 px-3 py-1.5 rounded-xl bg-gray-50 dark:bg-slate-900/50 border border-gray-200 dark:border-slate-700">
                      {variableInsertTarget === 'body' ? (
                        <MessageSquare className="w-3.5 h-3.5 text-primary-500" />
                      ) : (
                        <Mail className="w-3.5 h-3.5 text-indigo-500" />
                      )}
                      <span className="text-[11px] font-bold text-gray-600 dark:text-slate-300">
                        {t('notifications.templates.insertTargetLabel')}
                        <span className={variableInsertTarget === 'body' ? 'text-primary-600 dark:text-primary-400' : 'text-indigo-600 dark:text-indigo-400'}>
                          {variableInsertTarget === 'body' ? t('notifications.templates.htmlBody') : t('notifications.templates.subjectLine')}
                        </span>
                      </span>
                    </div>
                  </div>

                  {/* 编辑器/预览内容 */}
                  {editorViewMode === 'editor' && (
                    <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm overflow-hidden">
                      <div className="px-4 py-3 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50/60 via-white to-gray-50/60 dark:from-slate-900/50 dark:via-slate-800/60 dark:to-slate-900/50 flex items-center justify-between flex-wrap gap-2">
                        <div className="flex items-center gap-2">
                          <div className="flex gap-1.5">
                            <span className="w-3 h-3 rounded-full bg-red-400" />
                            <span className="w-3 h-3 rounded-full bg-amber-400" />
                            <span className="w-3 h-3 rounded-full bg-emerald-400" />
                          </div>
                          <span className="text-[11px] font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 ml-1">
                            {t('notifications.templates.htmlCodeEditor')}
                          </span>
                        </div>
                        <span className="text-[11px] text-gray-400 dark:text-slate-500 font-mono">
                          {formData.body.length} chars
                        </span>
                      </div>
                      <textarea
                        value={formData.body}
                        onFocus={() => setVariableInsertTarget('body')}
                        onChange={(e) => setFormData({ ...formData, body: e.target.value })}
                        rows={22}
                        spellCheck={false}
                        className="w-full px-4 py-3 bg-slate-50 dark:bg-slate-900/80 text-sm font-mono leading-[1.75] focus:outline-none dark:text-slate-200 placeholder:text-slate-400 tabular-nums"
                        placeholder={t('notifications.templates.bodyPlaceholderEditor')}
                      />
                    </div>
                  )}

                  {editorViewMode === 'preview' && (
                    <div className="rounded-2xl border border-gray-200 dark:border-slate-700/60 overflow-hidden bg-white dark:bg-slate-900 shadow-sm">
                      {/* 浏览器顶栏 */}
                      <div className="px-4 py-3 border-b border-gray-100 dark:border-slate-800 bg-gradient-to-r from-gray-50 via-white to-gray-50 dark:from-slate-900 dark:via-slate-800 dark:to-slate-900 flex items-center gap-3 flex-wrap">
                        <div className="flex gap-1.5">
                          <span className="w-3 h-3 rounded-full bg-red-400 shadow-inner" />
                          <span className="w-3 h-3 rounded-full bg-amber-400 shadow-inner" />
                          <span className="w-3 h-3 rounded-full bg-emerald-400 shadow-inner" />
                        </div>
                        <div className="flex-1 min-w-[180px] px-3 py-1 rounded-lg bg-gray-100 dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-[11px] font-mono text-gray-500 dark:text-slate-400 truncate">
                          {t('notifications.templates.mailtoPreview')}
                        </div>
                        <div className="inline-flex items-center gap-1 px-2 py-1 rounded-lg bg-emerald-50 dark:bg-emerald-900/30 text-emerald-600 dark:text-emerald-400 ring-1 ring-emerald-500/20">
                          <Eye className="w-3.5 h-3.5" />
                          <span className="text-[11px] font-bold">{t('notifications.templates.localLivePreview')}</span>
                        </div>
                      </div>
                      <div className="p-5 md:p-7 min-h-[460px] bg-gradient-to-br from-white via-gray-50/40 to-white dark:from-slate-900 dark:via-slate-800/30 dark:to-slate-900">
                        <div className="prose prose-sm prose-slate dark:prose-invert max-w-none prose-headings:font-bold prose-a:text-primary-600 prose-a:no-underline hover:prose-a:underline">
                          <div
                            dangerouslySetInnerHTML={{ __html: sanitizeHtml(localPreview?.body ?? '') }}
                          />
                        </div>
                        {!localPreview?.body && (
                          <EmptyState
                            icon={FileText}
                            title={t('notifications.templates.htmlPreviewArea')}
                            subtitle={t('notifications.templates.htmlPreviewAreaHint')}
                          />
                        )}
                      </div>
                    </div>
                  )}

                  {editorViewMode === 'split' && (
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      {/* 左：代码编辑器 */}
                      <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm overflow-hidden flex flex-col">
                        <div className="px-3.5 py-2.5 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50/60 via-white to-gray-50/60 dark:from-slate-900/50 dark:via-slate-800/60 dark:to-slate-900/50 flex items-center justify-between flex-wrap gap-2">
                          <div className="flex items-center gap-2">
                            <div className="p-1.5 rounded-lg bg-gradient-to-br from-slate-700 to-slate-900 text-slate-100 shadow-sm">
                              <Code2 className="w-3.5 h-3.5" />
                            </div>
                            <span className="text-[11px] font-extrabold uppercase tracking-wider text-gray-600 dark:text-slate-300">{t('notifications.templates.codeLabel')}</span>
                          </div>
                          <span className="text-[10px] text-gray-400 dark:text-slate-500 font-mono">{formData.body.length} {t('notifications.templates.charUnit')}</span>
                        </div>
                        <textarea
                          value={formData.body}
                          onFocus={() => setVariableInsertTarget('body')}
                          onChange={(e) => setFormData({ ...formData, body: e.target.value })}
                          rows={22}
                          spellCheck={false}
                          className="flex-1 px-3.5 py-3 bg-slate-50 dark:bg-slate-900/80 text-xs font-mono leading-[1.75] focus:outline-none dark:text-slate-200 placeholder:text-slate-400 tabular-nums resize-none"
                          placeholder={t('notifications.templates.bodyPlaceholderSplit')}
                        />
                      </div>
                      {/* 右：实时预览 */}
                      <div className="rounded-2xl border border-gray-200 dark:border-slate-700/60 overflow-hidden bg-white dark:bg-slate-900 shadow-sm flex flex-col">
                        <div className="px-3.5 py-2.5 border-b border-gray-100 dark:border-slate-800 bg-gradient-to-r from-primary-50 via-white to-indigo-50 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-900/20 flex items-center justify-between flex-wrap gap-2">
                          <div className="flex items-center gap-2">
                            <div className="p-1.5 rounded-lg bg-gradient-to-br from-emerald-500 to-teal-500 text-white shadow-sm">
                              <Eye className="w-3.5 h-3.5" />
                            </div>
                            <span className="text-[11px] font-extrabold uppercase tracking-wider text-emerald-700 dark:text-emerald-300">{t('notifications.templates.livePreview')}</span>
                          </div>
                          <span className="flex items-center gap-1 text-[10px] text-gray-400 dark:text-slate-500">
                            <RefreshCw className="w-3 h-3" />
                            {t('notifications.templates.debounce200')}
                          </span>
                        </div>
                        <div className="flex-1 overflow-y-auto p-4 min-h-[460px] bg-gradient-to-br from-white via-gray-50/40 to-white dark:from-slate-900 dark:via-slate-800/30 dark:to-slate-900">
                          <div className="prose prose-xs prose-slate dark:prose-invert max-w-none prose-headings:font-bold">
                            <div
                              dangerouslySetInnerHTML={{ __html: sanitizeHtml(localPreview?.body ?? '') }}
                            />
                          </div>
                          {!localPreview?.body && (
                            <div className="flex flex-col items-center justify-center py-12 text-gray-400 dark:text-slate-500 text-center">
                              <div className="w-14 h-14 mb-3 rounded-2xl bg-gradient-to-br from-gray-50 to-gray-100 dark:from-slate-800 dark:to-slate-900 border border-gray-100 dark:border-slate-700 flex items-center justify-center">
                                <MonitorPlay className="w-6 h-6 opacity-30" />
                              </div>
                              <p className="text-xs font-semibold">{t('notifications.templates.previewEmptyTitle')}</p>
                              <p className="text-[11px] mt-0.5 opacity-70">{t('notifications.templates.previewEmptyHint')}</p>
                            </div>
                          )}
                        </div>
                      </div>
                    </div>
                  )}

                  {/* 变量检测结果 */}
                  {varValidateResult && (
                    <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm overflow-hidden">
                      <div className="px-5 py-3.5 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-primary-50/50 via-white to-indigo-50/50 dark:from-slate-900/40 dark:via-slate-800/60 dark:to-slate-900/40 flex items-center gap-2.5">
                        <div className="p-1.5 rounded-lg bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-sm">
                          <AlertCircle className="w-4 h-4" />
                        </div>
                        <div className="flex-1">
                          <div className="text-sm font-extrabold text-gray-800 dark:text-slate-100">{t('notifications.templates.variableCheck')}</div>
                          <div className="text-[11px] text-gray-500 dark:text-slate-400 mt-0.5">{t('notifications.templates.variableCheckHint')}</div>
                        </div>
                        <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-xl bg-primary-50 dark:bg-primary-900/30 text-primary-700 dark:text-primary-300 ring-1 ring-primary-500/20">
                          <Tag className="w-3.5 h-3.5" />
                          <span className="text-xs font-bold">{t('notifications.templates.variablesCount', { count: varValidateResult.allVariables.length })}</span>
                        </div>
                      </div>
                      <div className="p-5 grid grid-cols-1 md:grid-cols-2 gap-4">
                        {varValidateResult.commonVariables.length > 0 && (
                          <div className="rounded-xl p-4 bg-gradient-to-br from-emerald-50 to-teal-50 dark:from-emerald-900/25 dark:to-teal-900/20 border border-emerald-100 dark:border-emerald-900/40">
                            <div className="flex items-center gap-2 mb-2.5">
                              <CheckCircle2 className="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
                              <span className="text-xs font-bold uppercase tracking-wider text-emerald-700 dark:text-emerald-400">
                                {t('notifications.templates.commonVariablesCount', { count: varValidateResult.commonVariables.length })}
                              </span>
                            </div>
                            <div className="flex flex-wrap gap-1.5">
                              {varValidateResult.commonVariables.map((v) => (
                                <code
                                  key={v}
                                  className="text-[11px] px-2 py-0.5 rounded-lg bg-white dark:bg-slate-800 text-emerald-700 dark:text-emerald-400 font-mono font-semibold ring-1 ring-emerald-500/20 shadow-sm"
                                >
                                  {'{' + v + '}'}
                                </code>
                              ))}
                            </div>
                          </div>
                        )}
                        {varValidateResult.uncommonVariables.length > 0 && (
                          <div className="rounded-xl p-4 bg-gradient-to-br from-amber-50 to-orange-50 dark:from-amber-900/25 dark:to-orange-900/20 border border-amber-100 dark:border-amber-900/40">
                            <div className="flex items-center gap-2 mb-2.5">
                              <AlertCircle className="w-4 h-4 text-amber-600 dark:text-amber-400" />
                              <span className="text-xs font-bold uppercase tracking-wider text-amber-700 dark:text-amber-400">
                                {t('notifications.templates.uncommonVariablesCount', { count: varValidateResult.uncommonVariables.length })}
                              </span>
                            </div>
                            <div className="flex flex-wrap gap-1.5">
                              {varValidateResult.uncommonVariables.map((v) => (
                                <code
                                  key={v}
                                  className="text-[11px] px-2 py-0.5 rounded-lg bg-white dark:bg-slate-800 text-amber-700 dark:text-amber-400 font-mono font-semibold ring-1 ring-amber-500/20 shadow-sm"
                                >
                                  {'{' + v + '}'}
                                </code>
                              ))}
                            </div>
                          </div>
                        )}
                        {varValidateResult.commonVariables.length === 0 && varValidateResult.uncommonVariables.length === 0 && (
                          <div className="md:col-span-2 flex items-center justify-center py-6 text-gray-400 dark:text-slate-500 text-sm">
                            <div className="flex items-center gap-2">
                              <Activity className="w-4 h-4 opacity-60" />
                              <span>{t('notifications.templates.noVariablesDetected')}</span>
                            </div>
                          </div>
                        )}
                      </div>
                    </div>
                  )}

                  {/* 启用大开关卡片 */}
                  <div
                    className={`relative rounded-2xl border shadow-sm overflow-hidden transition ${
                      formData.isActive
                        ? 'bg-gradient-to-br from-emerald-50 via-white to-teal-50 dark:from-emerald-900/20 dark:via-slate-800 dark:to-teal-900/20 border-emerald-100 dark:border-emerald-800/40'
                        : 'bg-white dark:bg-slate-800/60 border-gray-200 dark:border-slate-700/60'
                    }`}
                  >
                    <label className="flex items-center gap-4 px-5 py-4 cursor-pointer select-none">
                      {/* 大开关 */}
                      <div className="shrink-0 relative">
                        <input
                          type="checkbox"
                          checked={formData.isActive}
                          onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                          className="peer sr-only"
                        />
                        <div className={`w-14 h-8 rounded-full transition-all shadow-inner ring-1 ${
                          formData.isActive
                            ? 'bg-gradient-to-br from-emerald-500 to-teal-500 ring-emerald-500/30'
                            : 'bg-gray-200 dark:bg-slate-700 ring-gray-500/10'
                        }`} />
                        <div className={`absolute top-0.5 left-0.5 w-7 h-7 rounded-full bg-white dark:bg-slate-100 shadow-md transition-all flex items-center justify-center ${
                          formData.isActive ? 'translate-x-6' : 'translate-x-0'
                        }`}>
                          {formData.isActive ? (
                            <CheckCircle2 className="w-4 h-4 text-emerald-500" strokeWidth={2.5} />
                          ) : (
                            <XCircle className="w-4 h-4 text-gray-400" strokeWidth={2} />
                          )}
                        </div>
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 flex-wrap">
                          <span className={`text-sm font-extrabold ${formData.isActive ? 'text-emerald-700 dark:text-emerald-400' : 'text-gray-700 dark:text-slate-200'}`}>
                            {formData.isActive ? t('notifications.templates.templateActive') : t('notifications.templates.templateInactive')}
                          </span>
                          {formData.isActive && (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-emerald-100 dark:bg-emerald-900/40 text-emerald-700 dark:text-emerald-300 text-[10px] font-bold uppercase tracking-wider ring-1 ring-emerald-500/20">
                              <span className="relative flex w-1.5 h-1.5">
                                <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75" />
                                <span className="relative inline-flex rounded-full h-1.5 w-1.5 bg-emerald-500" />
                              </span>
                              Live
                            </span>
                          )}
                        </div>
                        <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 leading-relaxed">
                          {formData.isActive
                            ? t('notifications.templates.templateActiveHint')
                            : t('notifications.templates.templateInactiveHint')}
                        </p>
                      </div>
                    </label>
                  </div>
                </div>

                {/* 右：变量面板（col-span-1） */}
                <div className="space-y-3">
                  <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm overflow-hidden">
                    <div className="px-4 py-3.5 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-violet-50/60 via-white to-indigo-50/60 dark:from-slate-900/50 dark:via-slate-800/60 dark:to-indigo-900/20">
                      <div className="flex items-center gap-2">
                        <div className="p-1.5 rounded-lg bg-gradient-to-br from-violet-500 to-indigo-500 text-white shadow-sm">
                          <Tag className="w-4 h-4" />
                        </div>
                        <div className="flex-1 min-w-0">
                          <h3 className="text-sm font-extrabold text-gray-800 dark:text-slate-100 tracking-tight">{t('notifications.templates.availableVariables')}</h3>
                          <p className="text-[11px] text-gray-500 dark:text-slate-400 mt-0.5">
                            {t('notifications.templates.availableCountPrefix')}
                            <span className="font-bold text-indigo-600 dark:text-indigo-400">{Object.values(variablesByCategory).reduce((s, arr) => s + arr.length, 0)}</span>
                            {t('notifications.templates.availableCountSuffix')}
                          </p>
                        </div>
                      </div>
                    </div>
                    <div className="p-3.5 space-y-3">
                      {/* 变量搜索 */}
                      <div className="relative">
                        <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
                        <input
                          type="text"
                          value={variableSearch}
                          onChange={(e) => setVariableSearch(e.target.value)}
                          placeholder={t('notifications.templates.variableSearchPlaceholder')}
                          className={inputBase + ' pl-10 !py-2'}
                        />
                      </div>

                      {/* 插入目标选择（大卡片切换） */}
                      <div className="grid grid-cols-2 gap-2">
                        <button
                          onClick={() => setVariableInsertTarget('subject')}
                          className={`relative flex flex-col items-start gap-1 px-3 py-2.5 rounded-xl text-left transition-all border ${
                            variableInsertTarget === 'subject'
                              ? 'bg-gradient-to-br from-indigo-500 to-violet-500 text-white border-transparent shadow-md shadow-indigo-500/30 scale-[1.01]'
                              : 'bg-white dark:bg-slate-800 text-gray-700 dark:text-slate-300 border-gray-200 dark:border-slate-700 hover:border-indigo-300 dark:hover:border-indigo-700'
                          }`}
                        >
                          <Mail className={`w-4 h-4 ${variableInsertTarget === 'subject' ? 'text-white' : 'text-indigo-500'}`} />
                          <span className="text-xs font-bold">{t('notifications.templates.subjectLine')}</span>
                          <span className={`text-[10px] ${variableInsertTarget === 'subject' ? 'text-indigo-100' : 'text-gray-400 dark:text-slate-500'}`}>
                            {t('notifications.templates.emailTitle')}
                          </span>
                          {variableInsertTarget === 'subject' && (
                            <CheckCircle2 className="absolute top-1.5 right-1.5 w-3.5 h-3.5 text-white" />
                          )}
                        </button>
                        <button
                          onClick={() => setVariableInsertTarget('body')}
                          className={`relative flex flex-col items-start gap-1 px-3 py-2.5 rounded-xl text-left transition-all border ${
                            variableInsertTarget === 'body'
                              ? 'bg-gradient-to-br from-primary-500 to-indigo-500 text-white border-transparent shadow-md shadow-primary-500/30 scale-[1.01]'
                              : 'bg-white dark:bg-slate-800 text-gray-700 dark:text-slate-300 border-gray-200 dark:border-slate-700 hover:border-primary-300 dark:hover:border-primary-700'
                          }`}
                        >
                          <MessageSquare className={`w-4 h-4 ${variableInsertTarget === 'body' ? 'text-white' : 'text-primary-500'}`} />
                          <span className="text-xs font-bold">{t('notifications.templates.htmlBody')}</span>
                          <span className={`text-[10px] ${variableInsertTarget === 'body' ? 'text-primary-100' : 'text-gray-400 dark:text-slate-500'}`}>
                            {t('notifications.templates.emailContent')}
                          </span>
                          {variableInsertTarget === 'body' && (
                            <CheckCircle2 className="absolute top-1.5 right-1.5 w-3.5 h-3.5 text-white" />
                          )}
                        </button>
                      </div>
                    </div>

                    {/* 分类变量列表 */}
                    <div className="border-t border-gray-100 dark:border-slate-700/60 max-h-[600px] overflow-y-auto">
                      {Object.keys(variablesByCategory).length === 0 ? (
                        <EmptyState icon={Search} title={t('notifications.templates.noMatchingVariables')} subtitle={t('notifications.templates.noMatchingVariablesHint')} />
                      ) : (
                        Object.entries(variablesByCategory).map(([category, vars]) => (
                          <div key={category} className="border-b border-gray-50 dark:border-slate-700/40 last:border-b-0">
                            <div className="px-4 py-2 bg-gradient-to-r from-gray-50 via-white to-gray-50 dark:from-slate-900/40 dark:via-slate-800/50 dark:to-slate-900/40 sticky top-0 backdrop-blur-sm flex items-center justify-between">
                              <h4 className="text-[11px] font-extrabold uppercase tracking-[0.06em] text-gray-500 dark:text-slate-400 flex items-center gap-1.5">
                                <span className="inline-block w-1 h-3 rounded-full bg-gradient-to-b from-primary-400 to-indigo-500" />
                                {getVariableCategoryLabel(t, category)}
                              </h4>
                              <span className="text-[10px] font-bold text-gray-400 dark:text-slate-500 bg-white dark:bg-slate-800 px-1.5 py-0.5 rounded-lg ring-1 ring-gray-200 dark:ring-slate-700">
                                {vars.length}
                              </span>
                            </div>
                            <div className="py-0.5">
                              {vars.map((v) => (
                                <button
                                  key={v.key}
                                  onClick={() => insertVariable(v.key)}
                                  className="w-full text-left px-4 py-2 hover:bg-gradient-to-r hover:from-primary-50 hover:to-indigo-50 dark:hover:from-primary-900/20 dark:hover:to-indigo-900/20 transition-all flex items-center justify-between group border-l-2 border-transparent hover:border-primary-400 dark:hover:border-primary-600"
                                  title={t('notifications.templates.insertVariableTooltip', {
                                    label: t(v.label),
                                    variable: `{${v.key}}`,
                                    target: variableInsertTarget === 'subject' ? t('notifications.templates.insertTargetSubjectEnd') : t('notifications.templates.insertTargetBodyEnd'),
                                  })}
                                >
                                  <div className="flex items-center gap-2.5 min-w-0">
                                    <div className="p-1 rounded-md bg-gray-100 dark:bg-slate-800 group-hover:bg-primary-100 dark:group-hover:bg-primary-900/40 transition">
                                      <Plus className="w-3 h-3 text-gray-400 group-hover:text-primary-600 dark:group-hover:text-primary-400 transition" />
                                    </div>
                                    <code className="text-[11px] font-bold text-primary-700 dark:text-primary-400 group-hover:font-black tracking-tight font-mono truncate">
                                      {'{' + v.key + '}'}
                                    </code>
                                  </div>
                                  <span className="text-[11px] text-gray-500 dark:text-slate-400 truncate max-w-[140px] ml-2 group-hover:text-gray-700 dark:group-hover:text-slate-300 group-hover:font-semibold transition">
                                    {t(v.label)}
                                  </span>
                                </button>
                              ))}
                            </div>
                          </div>
                        ))
                      )}
                    </div>
                    <div className="px-4 py-3 border-t border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50/50 to-white dark:from-slate-900/30 dark:to-slate-800">
                      <p className="text-[11px] text-gray-500 dark:text-slate-400 leading-relaxed flex items-start gap-1.5">
                        <CheckCircle2 className="w-3 h-3 mt-0.5 text-emerald-500 shrink-0" />
                        <span>
                          {t('notifications.templates.clickVariableToInsert')}
                          <span className="font-bold mx-0.5 text-primary-600 dark:text-primary-400">
                            {variableInsertTarget === 'subject' ? t('notifications.templates.insertTargetSubjectEnd') : t('notifications.templates.insertTargetBodyEnd')}
                          </span>
                        </span>
                      </p>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            {/* 底部按钮栏 */}
            <div className="relative px-5 md:px-7 py-4 border-t border-gray-200 dark:border-slate-800 flex items-center gap-3 flex-wrap flex-shrink-0 bg-gradient-to-br from-gray-50/60 via-white to-gray-50/60 dark:from-slate-900 dark:via-slate-900 dark:to-indigo-900/10">
              <button
                onClick={handlePreviewFromEditor}
                disabled={saving || previewLoading}
                className="inline-flex items-center gap-2 px-4 py-2.5 text-sm font-bold rounded-xl text-sky-700 dark:text-sky-300 bg-sky-50 dark:bg-sky-900/25 border border-sky-200 dark:border-sky-800/40 hover:bg-sky-100 dark:hover:bg-sky-900/40 transition disabled:opacity-50 shadow-sm"
              >
                <MonitorPlay className="w-4 h-4" />
                {previewLoading ? t('notifications.templates.backendRendering') : t('notifications.templates.backendFullPreview')}
              </button>
              <div className="flex-1" />
              <button
                onClick={() => setShowEditor(false)}
                disabled={saving}
                className={btnSecondary + ' !px-4.5'}
              >
                {t('common.cancel')}
              </button>
              <button
                onClick={handleSave}
                disabled={saving}
                className={btnPrimary + ' min-w-[110px] justify-center !px-6'}
              >
                {saving ? (
                  <>
                    <RefreshCw className="w-4 h-4 animate-spin" />
                    {t('notifications.common.saving')}
                  </>
                ) : (
                  <>
                    <CheckCircle2 className="w-4 h-4" />
                    {t('notifications.templates.saveTemplate')}
                  </>
                )}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 删除确认 */}
      <ConfirmDialog
        open={!!deleteTarget}
        title={t('notifications.common.moveToTrash')}
        message={t('notifications.templates.confirmMoveToTrash', { name: deleteTarget?.name ?? '' })}
        confirmText={t('notifications.common.moveToTrash')}
        cancelText={t('common.cancel')}
        variant="danger"
        onConfirm={() => {
          if (deleteTarget) handleDelete(deleteTarget);
        }}
        onCancel={() => setDeleteTarget(null)}
      />

      {/* 垃圾箱 Modal */}
      {trashOpen && (
        <Modal
          open
          onClose={() => setTrashOpen(false)}
          title={t('notifications.templates.trashModalTitle')}
          size="xl"
          footer={
            <div className="flex items-center justify-between w-full gap-3 flex-wrap">
              <div className="inline-flex items-center gap-2 px-3 py-1.5 rounded-xl bg-rose-50 dark:bg-rose-900/25 border border-rose-100 dark:border-rose-900/40 text-rose-700 dark:text-rose-300 text-xs font-bold">
                <Trash2 className="w-3.5 h-3.5" />
                <span>
                  {t('notifications.templates.trashCountPrefix')}
                  <b>{trashCount}</b>
                  {t('notifications.templates.trashCountSuffix')}
                </span>
              </div>
              <div className="flex items-center gap-2">
                <button
                  onClick={() => setClearTrashOpen(true)}
                  disabled={trashActionLoading || trashCount === 0}
                  className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-bold rounded-xl text-red-700 dark:text-red-300 bg-gradient-to-br from-red-50 to-rose-50 dark:from-red-900/30 dark:to-rose-900/20 border border-red-200 dark:border-red-900/40 hover:from-red-100 hover:to-rose-100 disabled:opacity-50 transition shadow-sm"
                >
                  <Trash2 className="w-4 h-4" />
                  {t('notifications.common.emptyTrash')}
                </button>
                <button
                  onClick={() => setTrashOpen(false)}
                  className={btnSecondary}
                >
                  {t('common.close')}
                </button>
              </div>
            </div>
          }
        >
          <div className="space-y-4">
            {/* 搜索 + 汇总 */}
            <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 p-3.5 shadow-sm flex flex-wrap items-center gap-3">
              <div className="relative flex-1 min-w-[220px]">
                <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
                <input
                  type="text"
                  placeholder={t('notifications.templates.trashSearchPlaceholder')}
                  value={trashKeywordInput}
                  onChange={(e) => handleTrashKeywordInput(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') triggerTrashKeywordNow();
                  }}
                  className={inputBase + ' pl-10 !py-2'}
                />
              </div>
              {trashKeywordInput && (
                <div className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg bg-primary-50 dark:bg-primary-900/30 text-primary-700 dark:text-primary-300 ring-1 ring-primary-500/20">
                  <Filter className="w-3 h-3" />
                  <span className="text-[11px] font-bold">{t('notifications.templates.keywordChip', { keyword: trashKeywordInput })}</span>
                  <button
                    onClick={clearTrashKeyword}
                    className="ml-1 text-primary-600 hover:text-primary-800 dark:hover:text-primary-200"
                  >
                    <X className="w-3 h-3" />
                  </button>
                </div>
              )}
            </div>

            {/* 表格 */}
            <div className="hidden md:block overflow-x-auto rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm">
              <table className="w-full">
                <thead className="bg-gradient-to-r from-rose-50/70 via-rose-50/40 to-white dark:from-slate-900 dark:via-rose-900/10 dark:to-slate-900 border-b border-gray-100 dark:border-slate-700/60">
                  <tr>
                    <th className="px-5 py-3.5 text-left text-[11px] font-extrabold uppercase tracking-wider text-gray-500 dark:text-slate-400">{t('notifications.templates.templateName')}</th>
                    <th className="px-5 py-3.5 text-left text-[11px] font-extrabold uppercase tracking-wider text-gray-500 dark:text-slate-400">{t('notifications.common.eventType')}</th>
                    <th className="px-5 py-3.5 text-left text-[11px] font-extrabold uppercase tracking-wider text-gray-500 dark:text-slate-400">{t('notifications.common.deletedAt')}</th>
                    <th className="px-5 py-3.5 text-right text-[11px] font-extrabold uppercase tracking-wider text-gray-500 dark:text-slate-400">{t('notifications.common.actions')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-50 dark:divide-slate-700/60">
                  {trashLoading ? (
                    <tr><td colSpan={4} className="px-5 py-14">
                      <LoadingState />
                    </td></tr>
                  ) : (trashList?.data ?? []).length === 0 ? (
                    <tr><td colSpan={4} className="px-5 py-14">
                      <EmptyState
                        icon={Trash2}
                        title={t('notifications.templates.trashEmpty')}
                        subtitle={trashQuery.keyword ? t('notifications.templates.trashNoKeywordMatch') : t('notifications.templates.trashEmptyHint')}
                      />
                    </td></tr>
                  ) : (
                    (trashList?.data ?? []).map((tpl, idx) => {
                      const ec = eventTypeColorMap[tpl.eventType];
                      return (
                        <tr key={tpl.id} className="group hover:bg-rose-50/40 dark:hover:bg-rose-900/10 transition">
                          <td className="px-5 py-3.5">
                            <div className="flex items-start gap-3">
                              <div className="mt-0.5 shrink-0 w-8 h-8 rounded-xl bg-gradient-to-br from-rose-100 to-rose-50 dark:from-rose-900/30 dark:to-rose-900/10 text-rose-600 dark:text-rose-400 flex items-center justify-center ring-1 ring-rose-500/10 text-[11px] font-black tabular-nums">
                                {(trashList?.currentPage ? (trashList.currentPage - 1) * (trashQuery.pageSize ?? 10) : 0) + idx + 1}
                              </div>
                              <div className="min-w-0 flex-1">
                                <div className="text-sm font-bold text-gray-800 dark:text-slate-100 truncate">{tpl.name}</div>
                                <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-1">{tpl.subject}</div>
                              </div>
                            </div>
                          </td>
                          <td className="px-5 py-3.5">
                            <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-xs font-bold ${ec?.bg ?? 'bg-gray-100 dark:bg-slate-800'} ${ec?.text ?? 'text-gray-700 dark:text-slate-300'} ring-1 ${ec?.ring ?? 'ring-gray-500/10'}`}>
                              <span className={`w-1.5 h-1.5 rounded-full ${ec?.accent ?? 'bg-gray-400'}`} />
                              {ec?.label ? t(ec.label) : getEventTypeLabel(t, tpl.eventType)}
                            </span>
                          </td>
                          <td className="px-5 py-3.5">
                            <div className="inline-flex items-center gap-1.5 text-xs text-gray-500 dark:text-slate-400">
                              <Clock className="w-3.5 h-3.5 opacity-70" />
                              {tpl.deletedAt ? formatDateTime(tpl.deletedAt) : '—'}
                            </div>
                          </td>
                          <td className="px-5 py-3.5 text-right">
                            <div className="flex items-center justify-end gap-1.5">
                              <button
                                onClick={() => setRestoreTarget(tpl)}
                                className="inline-flex items-center gap-1 px-2.5 py-1.5 rounded-lg text-xs font-bold text-emerald-700 dark:text-emerald-300 bg-emerald-50 dark:bg-emerald-900/25 border border-emerald-100 dark:border-emerald-900/40 hover:bg-emerald-100 dark:hover:bg-emerald-900/40 transition"
                                title={t('notifications.templates.restoreToList')}
                              >
                                <RotateCcw className="w-3.5 h-3.5" />
                                {t('notifications.common.restore')}
                              </button>
                              <button
                                onClick={() => setHardDeleteTarget(tpl)}
                                className="inline-flex items-center gap-1 px-2.5 py-1.5 rounded-lg text-xs font-bold text-red-700 dark:text-red-300 bg-red-50 dark:bg-red-900/25 border border-red-100 dark:border-red-900/40 hover:bg-red-100 dark:hover:bg-red-900/40 transition"
                                title={t('notifications.templates.hardDeleteWarning')}
                              >
                                <Trash2 className="w-3.5 h-3.5" />
                                {t('notifications.common.deleteForever')}
                              </button>
                            </div>
                          </td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>

            {/* 移动端卡片视图 */}
            <div className="md:hidden rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm divide-y divide-gray-50 dark:divide-slate-700/60">
              {trashLoading ? (
                <div className="px-5 py-14">
                  <LoadingState />
                </div>
              ) : (trashList?.data ?? []).length === 0 ? (
                <div className="px-5 py-14">
                  <EmptyState
                    icon={Trash2}
                    title={t('notifications.templates.trashEmpty')}
                    subtitle={trashQuery.keyword ? t('notifications.templates.trashNoKeywordMatch') : t('notifications.templates.trashEmptyHint')}
                  />
                </div>
              ) : (
                (trashList?.data ?? []).map((tpl) => {
                  const ec = eventTypeColorMap[tpl.eventType];
                  return (
                    <div key={tpl.id} className="p-4 space-y-2.5">
                      <div className="text-sm font-bold text-gray-800 dark:text-slate-100 truncate">{tpl.name}</div>
                      <div className="text-xs text-gray-500 dark:text-slate-400 line-clamp-2">{tpl.subject}</div>
                      <div className="flex flex-wrap items-center gap-1.5">
                        <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-xs font-bold ${ec?.bg ?? 'bg-gray-100 dark:bg-slate-800'} ${ec?.text ?? 'text-gray-700 dark:text-slate-300'} ring-1 ${ec?.ring ?? 'ring-gray-500/10'}`}>
                          <span className={`w-1.5 h-1.5 rounded-full ${ec?.accent ?? 'bg-gray-400'}`} />
                          {ec?.label ? t(ec.label) : getEventTypeLabel(t, tpl.eventType)}
                        </span>
                      </div>
                      <div className="flex items-center justify-between pt-1">
                        <div className="inline-flex items-center gap-1.5 text-xs text-gray-500 dark:text-slate-400">
                          <Clock className="w-3.5 h-3.5 opacity-70" />
                          {tpl.deletedAt ? formatDateTime(tpl.deletedAt) : '—'}
                        </div>
                        <div className="flex items-center gap-1.5">
                          <button
                            onClick={() => setRestoreTarget(tpl)}
                            className="inline-flex items-center gap-1 px-2.5 py-1.5 rounded-lg text-xs font-bold text-emerald-700 dark:text-emerald-300 bg-emerald-50 dark:bg-emerald-900/25 border border-emerald-100 dark:border-emerald-900/40 hover:bg-emerald-100 dark:hover:bg-emerald-900/40 transition"
                            title={t('notifications.templates.restoreToList')}
                          >
                            <RotateCcw className="w-3.5 h-3.5" />
                            {t('notifications.common.restore')}
                          </button>
                          <button
                            onClick={() => setHardDeleteTarget(tpl)}
                            className="inline-flex items-center gap-1 px-2.5 py-1.5 rounded-lg text-xs font-bold text-red-700 dark:text-red-300 bg-red-50 dark:bg-red-900/25 border border-red-100 dark:border-red-900/40 hover:bg-red-100 dark:hover:bg-red-900/40 transition"
                            title={t('notifications.templates.hardDeleteWarning')}
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                          </button>
                        </div>
                      </div>
                    </div>
                  );
                })
              )}
            </div>

            {/* 分页 */}
            {trashList && trashList.totalPages > 1 && (
              <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm px-4 py-3 flex items-center justify-between flex-wrap gap-2">
                <div className="inline-flex items-center gap-2 text-xs text-gray-500 dark:text-slate-400">
                  <FileText className="w-3.5 h-3.5 opacity-70" />
                  <span>
                    {t('notifications.common.paginationPrefix')}
                    <b className="text-gray-700 dark:text-slate-200">{trashList.totalItems}</b>
                    {t('notifications.common.paginationMiddle')}
                    <b className="text-gray-700 dark:text-slate-200">{trashList.currentPage}/{trashList.totalPages}</b>
                    {t('notifications.common.paginationSuffix')}
                  </span>
                </div>
                <div className="inline-flex items-center gap-1.5">
                  <button
                    onClick={() => setTrashQuery((q) => ({ ...q, page: q.page - 1 }))}
                    disabled={trashList.currentPage <= 1}
                    className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs font-bold text-gray-600 dark:text-slate-400 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition"
                  >
                    <ChevronLeft className="w-3.5 h-3.5" />
                    {t('notifications.common.previousPage')}
                  </button>
                  <button
                    onClick={() => setTrashQuery((q) => ({ ...q, page: q.page + 1 }))}
                    disabled={trashList.currentPage >= trashList.totalPages}
                    className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs font-bold text-gray-600 dark:text-slate-400 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition"
                  >
                    {t('notifications.common.nextPage')}
                    <ChevronRight className="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>
            )}
          </div>
        </Modal>
      )}

      {/* 恢复确认 */}
      <ConfirmDialog
        open={!!restoreTarget}
        title={t('notifications.templates.restoreTemplate')}
        message={t('notifications.templates.confirmRestore', { name: restoreTarget?.name ?? '' })}
        confirmText={t('notifications.common.restore')}
        cancelText={t('common.cancel')}
        variant="primary"
        onConfirm={handleRestore}
        onCancel={() => setRestoreTarget(null)}
        loading={trashActionLoading}
      />

      {/* 彻底删除确认 */}
      <ConfirmDialog
        open={!!hardDeleteTarget}
        title={t('notifications.common.deleteForever')}
        message={t('notifications.templates.confirmHardDelete', { name: hardDeleteTarget?.name ?? '' })}
        confirmText={t('notifications.common.deleteForever')}
        cancelText={t('common.cancel')}
        variant="danger"
        onConfirm={handleHardDelete}
        onCancel={() => setHardDeleteTarget(null)}
        loading={trashActionLoading}
      />

      {/* 清空垃圾箱确认 */}
      <ConfirmDialog
        open={clearTrashOpen}
        title={t('notifications.common.emptyTrash')}
        message={t('notifications.templates.confirmEmptyTrash')}
        confirmText={t('notifications.templates.emptyTrashConfirm')}
        cancelText={t('common.cancel')}
        variant="danger"
        onConfirm={handleClearTrash}
        onCancel={() => setClearTrashOpen(false)}
        loading={trashActionLoading}
      />

      {/* 预览弹窗 */}
      <Modal
        open={showPreview}
        onClose={() => setShowPreview(false)}
        title={t('notifications.templates.templatePreview')}
        size="xl"
      >
        {previewLoading ? (
          <div className="py-20">
            <LoadingState />
          </div>
        ) : previewData ? (
          <div className="space-y-4">
            {/* 主题卡片 */}
            <div className="rounded-2xl bg-gradient-to-br from-primary-50 via-white to-indigo-50 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-900/20 p-5 border border-primary-100 dark:border-slate-700 shadow-sm">
              <div className="flex items-start gap-3">
                <div className="shrink-0 w-10 h-10 rounded-2xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-md shadow-primary-500/30 flex items-center justify-center">
                  <Mail className="w-5 h-5" strokeWidth={2} />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="text-[11px] font-bold uppercase tracking-wider text-primary-600 dark:text-primary-400 mb-1 flex items-center gap-1.5">
                    <span className="relative flex w-1.5 h-1.5">
                      <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-primary-400 opacity-60" />
                      <span className="relative inline-flex rounded-full h-1.5 w-1.5 bg-primary-500" />
                    </span>
                    {t('notifications.templates.emailSubject')}
                  </div>
                  <h3 className="text-base md:text-lg font-extrabold text-gray-900 dark:text-slate-100 tracking-tight break-words leading-snug">
                    {previewData.subject || <em className="text-gray-400 dark:text-slate-500 font-normal">{t('notifications.templates.emptySubject')}</em>}
                  </h3>
                  {editingTemplate && (
                    <div className="mt-2 flex flex-wrap items-center gap-2 text-[11px]">
                      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-white dark:bg-slate-900/60 border border-gray-200 dark:border-slate-700 text-gray-600 dark:text-slate-400">
                        <FileText className="w-3 h-3" />
                        {editingTemplate.name}
                      </span>
                      <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md ${eventTypeColorMap[editingTemplate.eventType]?.bg ?? 'bg-gray-100 dark:bg-slate-800'} ${eventTypeColorMap[editingTemplate.eventType]?.text ?? 'text-gray-600 dark:text-slate-400'} ring-1 ${eventTypeColorMap[editingTemplate.eventType]?.ring ?? 'ring-gray-500/10'}`}>
                        {getEventTypeLabel(t, editingTemplate.eventType)}
                      </span>
                    </div>
                  )}
                </div>
              </div>
            </div>

            {/* Tab 切换（胶囊） */}
            <div className="inline-flex p-1 bg-gray-100 dark:bg-slate-900/50 rounded-xl border border-gray-200 dark:border-slate-700 w-full sm:w-auto">
              <button
                onClick={() => setPreviewMode('html')}
                className={`flex-1 sm:flex-none inline-flex items-center gap-1.5 px-4 py-2 rounded-lg text-xs font-bold transition-all ${
                  previewMode === 'html'
                    ? 'bg-white dark:bg-slate-800 text-primary-700 dark:text-primary-300 shadow-sm ring-1 ring-primary-500/25 scale-[1.01]'
                    : 'text-gray-600 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200 hover:bg-white/60 dark:hover:bg-slate-800/60'
                }`}
              >
                <MonitorPlay className="w-4 h-4" />
                {t('notifications.templates.htmlPreviewTab')}
              </button>
              <button
                onClick={() => setPreviewMode('text')}
                className={`flex-1 sm:flex-none inline-flex items-center gap-1.5 px-4 py-2 rounded-lg text-xs font-bold transition-all ${
                  previewMode === 'text'
                    ? 'bg-white dark:bg-slate-800 text-primary-700 dark:text-primary-300 shadow-sm ring-1 ring-primary-500/25 scale-[1.01]'
                    : 'text-gray-600 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200 hover:bg-white/60 dark:hover:bg-slate-800/60'
                }`}
              >
                <Code2 className="w-4 h-4" />
                {t('notifications.templates.htmlSourceTab')}
              </button>
            </div>

            {/* 内容 */}
            {previewMode === 'html' ? (
              <div className="rounded-2xl border border-gray-200 dark:border-slate-700/60 overflow-hidden shadow-sm">
                <div className="px-4 py-2.5 border-b border-gray-100 dark:border-slate-800 bg-gradient-to-r from-gray-50 via-white to-gray-50 dark:from-slate-900 dark:via-slate-800 dark:to-slate-900 flex items-center gap-2 flex-wrap">
                  <div className="flex gap-1.5">
                    <span className="w-3 h-3 rounded-full bg-red-400" />
                    <span className="w-3 h-3 rounded-full bg-amber-400" />
                    <span className="w-3 h-3 rounded-full bg-emerald-400" />
                  </div>
                  <span className="ml-1 text-[11px] font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400">{t('notifications.templates.htmlRenderResult')}</span>
                  <div className="ml-auto inline-flex items-center gap-1 px-2 py-0.5 rounded-lg bg-emerald-50 dark:bg-emerald-900/30 text-emerald-600 dark:text-emerald-400 ring-1 ring-emerald-500/20">
                    <CheckCircle2 className="w-3.5 h-3.5" />
                    <span className="text-[10px] font-bold">{t('notifications.templates.renderComplete')}</span>
                  </div>
                </div>
                <div className="p-5 md:p-7 min-h-[340px] bg-gradient-to-br from-white via-gray-50/30 to-white dark:from-slate-900 dark:via-slate-800/30 dark:to-slate-900">
                  <div className="prose prose-sm prose-slate dark:prose-invert max-w-none prose-headings:font-bold prose-a:text-primary-600 prose-a:no-underline hover:prose-a:underline">
                    <div
                      dangerouslySetInnerHTML={{ __html: sanitizeHtml(previewData.body) }}
                    />
                  </div>
                  {!previewData.body && (
                    <EmptyState
                      icon={FileText}
                      title={t('notifications.templates.bodyEmpty')}
                      subtitle={t('notifications.templates.bodyEmptyHintHtml')}
                    />
                  )}
                </div>
              </div>
            ) : (
              <div className="rounded-2xl border border-gray-200 dark:border-slate-700/60 overflow-hidden shadow-sm">
                <div className="px-4 py-2.5 border-b border-gray-100 dark:border-slate-800 bg-gradient-to-r from-slate-50 via-white to-slate-50 dark:from-slate-900 dark:via-slate-800 dark:to-slate-900 flex items-center gap-2 flex-wrap">
                  <div className="p-1.5 rounded-lg bg-gradient-to-br from-slate-700 to-slate-900 text-slate-100 shadow-sm">
                    <Code2 className="w-3.5 h-3.5" />
                  </div>
                  <span className="ml-1 text-[11px] font-bold uppercase tracking-wider text-gray-600 dark:text-slate-300">{t('notifications.templates.htmlSourceCode')}</span>
                  <div className="ml-auto inline-flex items-center gap-1 px-2 py-0.5 rounded-lg bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 ring-1 ring-slate-500/10 font-mono text-[10px]">
                    {previewData.body.length} chars
                  </div>
                </div>
                <div className="p-4 md:p-5 min-h-[340px] bg-slate-50 dark:bg-slate-900/80 overflow-x-auto">
                  <pre className="text-xs font-mono leading-[1.8] text-slate-700 dark:text-slate-300 whitespace-pre-wrap break-words">
{previewData.body}
                  </pre>
                  {!previewData.body && (
                    <EmptyState
                      icon={Code2}
                      title={t('notifications.templates.bodyEmpty')}
                      subtitle={t('notifications.templates.bodyEmptyHintSource')}
                    />
                  )}
                </div>
              </div>
            )}
          </div>
        ) : null}
      </Modal>

      {/* Toast */}
      {toast && (
        <div className="fixed bottom-6 right-6 z-[60] animate-in slide-in-from-bottom-6 fade-in duration-300">
          <div
            className={`inline-flex items-center gap-2.5 pl-3 pr-4 py-3 rounded-2xl shadow-2xl text-sm font-semibold border backdrop-blur-md ${
              toast.type === 'success'
                ? 'bg-gradient-to-br from-emerald-500/95 to-teal-500/95 text-white border-emerald-400/40 shadow-emerald-500/25'
                : 'bg-gradient-to-br from-red-500/95 to-rose-500/95 text-white border-red-400/40 shadow-red-500/25'
            }`}
          >
            <div className={`w-7 h-7 rounded-xl flex items-center justify-center ${
              toast.type === 'success' ? 'bg-white/25' : 'bg-white/25'
            }`}>
              {toast.type === 'success' ? <CheckCircle2 className="w-4 h-4" strokeWidth={2.4} /> : <XCircle className="w-4 h-4" strokeWidth={2.4} />}
            </div>
            <span className="pr-0.5">{toast.text}</span>
          </div>
        </div>
      )}
    </div>
  );
}
