import { useState, useEffect, useMemo, useRef, useCallback } from 'react';
import { sanitizeHtml } from '../utils/format';
import { getApiErrorMessage } from '../utils/error';
import { mailTemplateApi, mailboxApi } from '../api';
import { useCrudPage } from '../hooks/useCrudPage';
import { toIntlLocale } from '../i18n/locale';
import type { TranslateParams } from '../i18n/types';
import type { TranslationKey } from '../locales/zh-Hans';
import type {
  MailTemplateDto,
  MailTemplateOverviewResponse,
  MailTemplateTypeCount,
  MailTemplateVariable,
  CreateMailTemplateRequest,
  UpdateMailTemplateRequest,
  MailboxResponse,
} from '../api/types';
import ConfirmDialog from '../components/ConfirmDialog';
import MailTemplateTrashModal from '../components/MailTemplateTrashModal';
import { useI18n } from '../context/I18nContext';
import { useToast } from '../context/ToastContext';
import {
  Mail,
  FileText,
  CheckCircle2,
  XCircle,
  Plus,
  Search,
  Edit2,
  Trash2,
  Eye,
  X,
  Tag,
  Code2,
  MonitorPlay,
  Zap,
  Shield,
  RefreshCw,
  Layers,
  Sparkles,
  Hash,
  Database,
  Filter,
  AlertCircle,
} from 'lucide-react';

/** 模板类型：`value` 是提交给后端的稳定数值码；文案字段只持有真源 key，渲染处必须经 `t()` */
const TEMPLATE_TYPE_OPTIONS: {
  value: number;
  labelKey: TranslationKey;
  categoryKey: TranslationKey;
}[] = [
  { value: 1, labelKey: 'mail.templates.type.newConversation', categoryKey: 'mail.templates.typeCategory.notification' },
  { value: 2, labelKey: 'mail.templates.type.newReply', categoryKey: 'mail.templates.typeCategory.notification' },
  { value: 3, labelKey: 'mail.templates.type.conversationAssigned', categoryKey: 'mail.templates.typeCategory.notification' },
  { value: 4, labelKey: 'mail.templates.type.conversationClosed', categoryKey: 'mail.templates.typeCategory.notification' },
  { value: 5, labelKey: 'mail.templates.type.satisfactionSurvey', categoryKey: 'mail.templates.typeCategory.survey' },
  { value: 6, labelKey: 'mail.templates.type.passwordReset', categoryKey: 'mail.templates.typeCategory.system' },
  { value: 7, labelKey: 'mail.templates.type.welcome', categoryKey: 'mail.templates.typeCategory.system' },
  { value: 8, labelKey: 'mail.templates.type.calendarReminder', categoryKey: 'mail.templates.typeCategory.system' },
  { value: 10, labelKey: 'mail.templates.type.chatSessionClosed', categoryKey: 'mail.templates.typeCategory.notification' },
  { value: 99, labelKey: 'mail.templates.type.custom', categoryKey: 'mail.templates.typeCategory.custom' },
];

/**
 * 模板类型数值码 → 文案 key。由 `TEMPLATE_TYPE_OPTIONS` 派生，避免两处各写一份清单而漂移。
 *
 * 覆盖 `NoneadCSS.Domain/Enums/MailTemplateType.cs` 的**全部 10 个码**
 * （1/2/3/4/5/6/7/8/10/99）。后端 `MailTemplateService.GetTypeName` 只给了其中 8 个中文标签，
 * `CalendarReminder`(8) 与 `ChatSessionClosed`(10) 走 `_ => type.ToString()`，界面会漏出原始标识符；
 * 前端字典补全这两码后不再出现（实测开发库 `MailTemplates` 表 10 个码全部存在，真实可达）。
 * 未覆盖的码回落原始数字，与原行为等价的防御。
 */
const TEMPLATE_TYPE_LABEL_KEYS: Record<number, TranslationKey | undefined> = {};
for (const opt of TEMPLATE_TYPE_OPTIONS) TEMPLATE_TYPE_LABEL_KEYS[opt.value] = opt.labelKey;

/** 与 `I18nContext` 的 `t` 签名一致；模块级纯函数需显式传入 `t` */
type TranslateFn = (key: TranslationKey, params?: TranslateParams) => string;

/** 数值码 → 展示文案；未知码回落原始数字（后端 `typeName` 不再渲染） */
function templateTypeLabel(t: TranslateFn, type: number): string {
  const key = TEMPLATE_TYPE_LABEL_KEYS[type];
  return key ? t(key) : String(type);
}

/**
 * 模板变量：稳定 key（后端 `TemplateVariableRegistry` 的规范名）→ 标签文案 key，按分类分组。
 *
 * **分组顺序与分组键都由这里定义**：不再用后端下发的 `variable.category` 中文串当分组键
 * （中文不得当 key），也不用它做 `Object.keys` 排序依据——渲染时按本数组顺序展开。
 *
 * 覆盖后端 `GetBuiltInVariablesAsync`（`TemplateVariableRegistry.GetAll()`）的
 * **全部 67 个规范变量 / 13 个分类**。别名 `assigned_to` / `reply_from` / `closed_by` /
 * `reset_url` / `deadline` **不在 `GetAll()` 的返回里**（只在 `Expand()` 里复制取值），故不建键。
 */
const VARIABLE_GROUPS: {
  categoryKey: TranslationKey;
  variables: { key: string; labelKey: TranslationKey }[];
}[] = [
  {
    categoryKey: 'mail.templates.variableCategory.conversation',
    variables: [
      { key: 'subject', labelKey: 'mail.templates.variable.subject' },
      { key: 'conversation_id', labelKey: 'mail.templates.variable.conversationId' },
      { key: 'conversation_url', labelKey: 'mail.templates.variable.conversationUrl' },
      { key: 'priority', labelKey: 'mail.templates.variable.priority' },
      { key: 'update_content', labelKey: 'mail.templates.variable.updateContent' },
    ],
  },
  {
    categoryKey: 'mail.templates.variableCategory.customer',
    variables: [
      { key: 'customer_name', labelKey: 'mail.templates.variable.customerName' },
      { key: 'customer_email', labelKey: 'mail.templates.variable.customerEmail' },
      { key: 'customer_phone', labelKey: 'mail.templates.variable.customerPhone' },
    ],
  },
  {
    categoryKey: 'mail.templates.variableCategory.user',
    variables: [
      { key: 'user_name', labelKey: 'mail.templates.variable.userName' },
      { key: 'user_email', labelKey: 'mail.templates.variable.userEmail' },
      { key: 'user_role', labelKey: 'mail.templates.variable.userRole' },
    ],
  },
  {
    categoryKey: 'mail.templates.variableCategory.mailbox',
    variables: [
      { key: 'mailbox_name', labelKey: 'mail.templates.variable.mailboxName' },
      { key: 'mailbox_email', labelKey: 'mail.templates.variable.mailboxEmail' },
    ],
  },
  {
    categoryKey: 'mail.templates.variableCategory.message',
    variables: [
      { key: 'message_body', labelKey: 'mail.templates.variable.messageBody' },
      { key: 'first_message_body', labelKey: 'mail.templates.variable.firstMessageBody' },
    ],
  },
  {
    categoryKey: 'mail.templates.variableCategory.people',
    variables: [
      { key: 'assignee_name', labelKey: 'mail.templates.variable.assigneeName' },
      { key: 'assigner_name', labelKey: 'mail.templates.variable.assignerName' },
      { key: 'replier_name', labelKey: 'mail.templates.variable.replierName' },
      { key: 'closer_name', labelKey: 'mail.templates.variable.closerName' },
      { key: 'updater_name', labelKey: 'mail.templates.variable.updaterName' },
      { key: 'mentioner_name', labelKey: 'mail.templates.variable.mentionerName' },
      { key: 'escalated_by', labelKey: 'mail.templates.variable.escalatedBy' },
    ],
  },
  {
    categoryKey: 'mail.templates.variableCategory.sla',
    variables: [
      { key: 'sla_name', labelKey: 'mail.templates.variable.slaName' },
      { key: 'sla_level', labelKey: 'mail.templates.variable.slaLevel' },
      { key: 'sla_remaining', labelKey: 'mail.templates.variable.slaRemaining' },
      { key: 'sla_deadline', labelKey: 'mail.templates.variable.slaDeadline' },
      { key: 'sla_response_time', labelKey: 'mail.templates.variable.slaResponseTime' },
      { key: 'remaining_time', labelKey: 'mail.templates.variable.remainingTime' },
      { key: 'violation_type', labelKey: 'mail.templates.variable.violationType' },
    ],
  },
  {
    categoryKey: 'mail.templates.variableCategory.satisfaction',
    variables: [
      { key: 'satisfaction_url', labelKey: 'mail.templates.variable.satisfactionUrl' },
      { key: 'satisfaction_rating', labelKey: 'mail.templates.variable.satisfactionRating' },
      { key: 'satisfaction_rating_value', labelKey: 'mail.templates.variable.satisfactionRatingValue' },
      { key: 'satisfaction_comment', labelKey: 'mail.templates.variable.satisfactionComment' },
      { key: 'rating_url', labelKey: 'mail.templates.variable.ratingUrl' },
      { key: 'feedback_url', labelKey: 'mail.templates.variable.feedbackUrl' },
      { key: 'rating_stars', labelKey: 'mail.templates.variable.ratingStars' },
      { key: 'rating_label', labelKey: 'mail.templates.variable.ratingLabel' },
      { key: 'rating_comment', labelKey: 'mail.templates.variable.ratingComment' },
    ],
  },
  {
    categoryKey: 'mail.templates.variableCategory.escalation',
    variables: [
      { key: 'escalation_reason', labelKey: 'mail.templates.variable.escalationReason' },
      { key: 'escalated_at', labelKey: 'mail.templates.variable.escalatedAt' },
    ],
  },
  {
    categoryKey: 'mail.templates.variableCategory.chat',
    variables: [
      { key: 'chat_session_id', labelKey: 'mail.templates.variable.chatSessionId' },
      { key: 'chat_created_at', labelKey: 'mail.templates.variable.chatCreatedAt' },
      { key: 'agent_name', labelKey: 'mail.templates.variable.agentName' },
      { key: 'chat_subject', labelKey: 'mail.templates.variable.chatSubject' },
    ],
  },
  {
    categoryKey: 'mail.templates.variableCategory.calendar',
    variables: [
      { key: 'event_title', labelKey: 'mail.templates.variable.eventTitle' },
      { key: 'event_type', labelKey: 'mail.templates.variable.eventType' },
      { key: 'event_start_time', labelKey: 'mail.templates.variable.eventStartTime' },
      { key: 'event_end_time', labelKey: 'mail.templates.variable.eventEndTime' },
      { key: 'event_location', labelKey: 'mail.templates.variable.eventLocation' },
      { key: 'event_meeting_url', labelKey: 'mail.templates.variable.eventMeetingUrl' },
      { key: 'event_description', labelKey: 'mail.templates.variable.eventDescription' },
      { key: 'event_priority', labelKey: 'mail.templates.variable.eventPriority' },
      { key: 'event_url', labelKey: 'mail.templates.variable.eventUrl' },
    ],
  },
  {
    categoryKey: 'mail.templates.variableCategory.system',
    variables: [
      { key: 'created_at', labelKey: 'mail.templates.variable.createdAt' },
      { key: 'received_time', labelKey: 'mail.templates.variable.receivedTime' },
      { key: 'last_reply_time', labelKey: 'mail.templates.variable.lastReplyTime' },
      { key: 'waiting_time', labelKey: 'mail.templates.variable.waitingTime' },
      { key: 'timeout_minutes', labelKey: 'mail.templates.variable.timeoutMinutes' },
      { key: 'expire_time', labelKey: 'mail.templates.variable.expireTime' },
      { key: 'company_name', labelKey: 'mail.templates.variable.companyName' },
      { key: 'login_url', labelKey: 'mail.templates.variable.loginUrl' },
      { key: 'reset_password_url', labelKey: 'mail.templates.variable.resetPasswordUrl' },
      { key: 'activation_url', labelKey: 'mail.templates.variable.activationUrl' },
    ],
  },
  {
    categoryKey: 'mail.templates.variableCategory.import',
    variables: [
      { key: 'total_count', labelKey: 'mail.templates.variable.totalCount' },
      { key: 'success_count', labelKey: 'mail.templates.variable.successCount' },
      { key: 'failed_count', labelKey: 'mail.templates.variable.failedCount' },
      { key: 'error_report_url', labelKey: 'mail.templates.variable.errorReportUrl' },
    ],
  },
];

/** 变量稳定 key → 标签文案 key（由 `VARIABLE_GROUPS` 派生，未知 key 得 `undefined`） */
const VARIABLE_LABEL_KEYS: Record<string, TranslationKey | undefined> = {};
for (const group of VARIABLE_GROUPS) {
  for (const variable of group.variables) VARIABLE_LABEL_KEYS[variable.key] = variable.labelKey;
}

/** 变量面板的一个分组：分类文案 key + 该分类下实际可用的变量 */
interface VariableGroupView {
  categoryKey: TranslationKey;
  variables: { key: string; labelKey: TranslationKey | undefined }[];
}

interface MailTemplateListQuery {
  page: number;
  pageSize: number;
  keyword: string;
  type: number | null;
  isActive: boolean | null;
}

// 通用按钮样式
const btnPrimary =
  'inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold text-white rounded-xl bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 shadow-sm shadow-primary-500/20 hover:shadow hover:shadow-primary-500/30 transition-all disabled:opacity-50 ring-1 ring-primary-500/20';
const btnSecondary =
  'inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold text-gray-700 dark:text-slate-200 rounded-xl bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 hover:bg-gray-50 dark:hover:bg-slate-700/60 transition-all disabled:opacity-50 shadow-sm';

// 通用输入框样式
const inputBase =
  'w-full px-3.5 py-2.5 text-sm bg-white dark:bg-slate-900/80 border border-gray-200 dark:border-slate-700 rounded-xl text-gray-800 dark:text-slate-200 placeholder:text-gray-400 dark:placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-400 dark:focus:border-primary-500 shadow-sm transition-all';

// 概览统计 -> 配色（与通知模版/快捷回复一致）
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
    from: 'from-sky-500',
    to: 'to-blue-500',
    textLight: 'text-sky-600 dark:text-sky-400',
    bgSoft: 'bg-sky-50 dark:bg-sky-900/30',
    ringSoft: 'ring-sky-500/20',
  },
  {
    from: 'from-violet-500',
    to: 'to-fuchsia-500',
    textLight: 'text-violet-600 dark:text-violet-400',
    bgSoft: 'bg-violet-50 dark:bg-violet-900/30',
    ringSoft: 'ring-violet-500/20',
  },
];

// 模板类型 -> 渐变色板（基于类型值哈希，保证同类型始终相同配色）
function hashString(s: string): number {
  let h = 0;
  for (let i = 0; i < s.length; i++) {
    h = (h * 31 + s.charCodeAt(i)) | 0;
  }
  return Math.abs(h);
}
const typePalettes = [
  { from: 'from-sky-500', to: 'to-blue-500', accent: 'bg-sky-500', bar: 'bg-sky-500', text: 'text-sky-700 dark:text-sky-400', bg: 'bg-sky-50 dark:bg-sky-900/25', ring: 'ring-sky-500/20', ringSoft: 'ring-sky-500/20' },
  { from: 'from-emerald-500', to: 'to-teal-500', accent: 'bg-emerald-500', bar: 'bg-emerald-500', text: 'text-emerald-700 dark:text-emerald-400', bg: 'bg-emerald-50 dark:bg-emerald-900/25', ring: 'ring-emerald-500/20', ringSoft: 'ring-emerald-500/20' },
  { from: 'from-violet-500', to: 'to-indigo-500', accent: 'bg-violet-500', bar: 'bg-violet-500', text: 'text-violet-700 dark:text-violet-400', bg: 'bg-violet-50 dark:bg-violet-900/25', ring: 'ring-violet-500/20', ringSoft: 'ring-violet-500/20' },
  { from: 'from-rose-500', to: 'to-pink-500', accent: 'bg-rose-500', bar: 'bg-rose-500', text: 'text-rose-700 dark:text-rose-400', bg: 'bg-rose-50 dark:bg-rose-900/25', ring: 'ring-rose-500/20', ringSoft: 'ring-rose-500/20' },
  { from: 'from-amber-500', to: 'to-orange-500', accent: 'bg-amber-500', bar: 'bg-amber-500', text: 'text-amber-700 dark:text-amber-400', bg: 'bg-amber-50 dark:bg-amber-900/25', ring: 'ring-amber-500/20', ringSoft: 'ring-amber-500/20' },
  { from: 'from-fuchsia-500', to: 'to-purple-500', accent: 'bg-fuchsia-500', bar: 'bg-fuchsia-500', text: 'text-fuchsia-700 dark:text-fuchsia-400', bg: 'bg-fuchsia-50 dark:bg-fuchsia-900/25', ring: 'ring-fuchsia-500/20', ringSoft: 'ring-fuchsia-500/20' },
  { from: 'from-cyan-500', to: 'to-sky-500', accent: 'bg-cyan-500', bar: 'bg-cyan-500', text: 'text-cyan-700 dark:text-cyan-400', bg: 'bg-cyan-50 dark:bg-cyan-900/25', ring: 'ring-cyan-500/20', ringSoft: 'ring-cyan-500/20' },
];
/**
 * 模板类型 -> 渐变色板（基于类型值哈希，保证同类型始终相同配色）。
 *
 * 哈希种子是**数值码**，不再用后端 `typeName`：类型文案已改为"稳定数值码 + 前端字典"，
 * 再用 `typeName` 当种子等于把配色重新绑回后端中文标签；而类型筛选胶囊拿不到 `typeName`，
 * 用它会让胶囊与列表徽章不同色。改用数值码后二者必然同色
 * （`className` 字面量未改，仅哈希输入变化 → 各类型落到哪个色板可能变化）。
 */
function pickTypePalette(type: number) {
  if (!type) return { from: 'from-gray-400', to: 'to-gray-500', accent: 'bg-gray-400', bar: 'bg-gray-400', text: 'text-gray-600 dark:text-gray-400', bg: 'bg-gray-50 dark:bg-slate-700/40', ring: 'ring-gray-500/10', ringSoft: 'ring-gray-500/10' };
  return typePalettes[hashString(String(type)) % typePalettes.length];
}

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
    <div className="py-12 flex flex-col items-center text-gray-400">
      <div className="w-20 h-20 rounded-2xl bg-gradient-to-br from-gray-50 to-gray-100 dark:from-slate-800 dark:to-slate-900 border border-gray-100 dark:border-slate-700 flex items-center justify-center mb-4">
        <Icon className="w-10 h-10 opacity-30" />
      </div>
      <p className="text-sm font-semibold text-gray-500 dark:text-slate-400">{title}</p>
      <p className="text-xs mt-1 text-gray-400 dark:text-slate-500">{subtitle}</p>
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}

// 通用：加载态
function LoadingState({ text }: { text?: string }) {
  const { t } = useI18n();
  return (
    <div className="flex items-center justify-center gap-3 py-12">
      <div className="w-9 h-9 border-4 border-primary-200 dark:border-primary-900 border-t-primary-600 dark:border-t-primary-400 rounded-full animate-spin shadow-sm" />
      <span className="text-sm font-medium text-gray-500 dark:text-slate-400">
        {text ?? t('common.shell.loading')}
      </span>
    </div>
  );
}

export default function MailTemplates() {
  const { t, language } = useI18n();
  const toast = useToast();
  const [activeTab, setActiveTab] = useState<'overview' | 'list'>('overview');
  const [overview, setOverview] = useState<MailTemplateOverviewResponse | null>(null);
  const [overviewLoading, setOverviewLoading] = useState(false);

  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [builtInVariables, setBuiltInVariables] = useState<MailTemplateVariable[]>([]);

  // 编辑表单
  const [formData, setFormData] = useState<CreateMailTemplateRequest>({
    name: '',
    slug: '',
    type: 1,
    subject: '',
    bodyHtml: '',
    bodyText: '',
    variablesJson: null,
    isActive: true,
    mailboxId: null,
    description: '',
    sortOrder: 0,
  });
  const [saving, setSaving] = useState(false);
  const [deleting, setDeleting] = useState(false);

  // 预览
  const [showPreview, setShowPreview] = useState(false);
  const [previewData, setPreviewData] = useState<{ subject: string; bodyHtml: string; bodyText: string } | null>(null);
  const [previewMode, setPreviewMode] = useState<'html' | 'text'>('html');
  const [editorMode, setEditorMode] = useState<'html' | 'text'>('html');

  // 垃圾箱操作目标
  const [restoreTarget, setRestoreTarget] = useState<MailTemplateDto | null>(null);
  const [hardDeleteTarget, setHardDeleteTarget] = useState<MailTemplateDto | null>(null);
  const [clearTrashOpen, setClearTrashOpen] = useState(false);

  const crud = useCrudPage<MailTemplateDto, MailTemplateListQuery>({
    defaultQuery: { page: 1, pageSize: 10, keyword: '', type: null, isActive: null },
    fetchList: async (q) => {
      const data = await mailTemplateApi.list({
        page: q.page,
        pageSize: q.pageSize,
        keyword: q.keyword || null,
        type: q.type,
        isActive: q.isActive,
      });
      return {
        data: data.data,
        totalItems: data.totalItems,
        totalPages: data.totalPages,
        currentPage: q.page,
      };
    },
    fetchTrashList: async (q) => {
      const res = await mailTemplateApi.getTrash(q.page, q.pageSize, q.keyword);
      return { data: res.data ?? [], totalItems: res.totalItems ?? 0, totalPages: res.totalPages ?? 0, currentPage: q.page };
    },
    fetchTrashCount: mailTemplateApi.getTrashCount,
  });

  // 与 ApiKeys 一致：服务端搜索 650ms 输入防抖 + Enter 即时触发（缓冲 state，避免每次按键发请求）
  const keywordTimerRef = useRef<number | null>(null);
  const [keywordInput, setKeywordInput] = useState('');
  const applyKeyword = useCallback((value: string) => {
    crud.handleFilterChange('keyword', value);
  }, [crud.handleFilterChange]);
  const handleKeywordInput = useCallback((value: string) => {
    setKeywordInput(value);
    if (keywordTimerRef.current !== null) window.clearTimeout(keywordTimerRef.current);
    keywordTimerRef.current = window.setTimeout(() => applyKeyword(value), 650);
  }, [applyKeyword]);
  const triggerKeywordSearch = useCallback(() => {
    if (keywordTimerRef.current !== null) {
      window.clearTimeout(keywordTimerRef.current);
      keywordTimerRef.current = null;
    }
    applyKeyword(keywordInput);
  }, [applyKeyword, keywordInput]);
  // crud.query.keyword 被其他入口（筛选胶囊移除/重置等）改动时，同步输入框缓冲
  useEffect(() => {
    setKeywordInput(crud.query.keyword ?? '');
  }, [crud.query.keyword]);
  // 卸载时清理挂起的防抖定时器
  useEffect(() => {
    return () => {
      if (keywordTimerRef.current !== null) window.clearTimeout(keywordTimerRef.current);
    };
  }, []);

  // 加载邮箱和内置变量
  useEffect(() => {
    mailboxApi
      .listAll()
      .then((res: MailboxResponse[]) => setMailboxes(Array.isArray(res) ? res : []))
      .catch((err: unknown) => console.error('加载邮箱列表失败:', err));
    mailTemplateApi
      .builtInVariables()
      .then((res: MailTemplateVariable[]) => setBuiltInVariables(Array.isArray(res) ? res : []))
      .catch((err: unknown) => console.error('加载内置变量失败:', err));
  }, []);

  // 加载概览数据
  const loadOverview = async () => {
    setOverviewLoading(true);
    try {
      const [data, trashCountResp] = await Promise.all([mailTemplateApi.overview(), mailTemplateApi.getTrashCount()]);
      setOverview(data);
      if (trashCountResp?.count !== undefined && trashCountResp.count !== crud.trash.count) {
        crud.trash.refreshCount();
      }
    } finally {
      setOverviewLoading(false);
    }
  };

  useEffect(() => {
    if (activeTab === 'overview') loadOverview();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab]);

  const openTrash = () => {
    crud.trash.setOpen(true);
    crud.trash.setQuery({ page: 1, pageSize: 10, keyword: '' });
    crud.trash.refreshCount();
  };

  const handleRestore = async () => {
    if (!restoreTarget) return;
    crud.trash.setActionLoading(true);
    try {
      await mailTemplateApi.restore(restoreTarget.id);
      toast.success(t('mail.templates.toast.restored', { name: restoreTarget.name }));
      setRestoreTarget(null);
      crud.trash.refresh();
      crud.trash.refreshCount();
      crud.refresh();
      if (activeTab === 'overview') await loadOverview();
    } catch (e: unknown) {
      toast.error(getApiErrorMessage(e, t('mail.common.restoreFailed')));
    } finally {
      crud.trash.setActionLoading(false);
    }
  };

  const handleHardDelete = async () => {
    if (!hardDeleteTarget) return;
    crud.trash.setActionLoading(true);
    try {
      await mailTemplateApi.hardDelete(hardDeleteTarget.id);
      toast.success(t('mail.templates.toast.hardDeleted', { name: hardDeleteTarget.name }));
      setHardDeleteTarget(null);
      crud.trash.refresh();
      crud.trash.refreshCount();
    } catch (e: unknown) {
      toast.error(getApiErrorMessage(e, t('mail.common.hardDeleteFailed')));
    } finally {
      crud.trash.setActionLoading(false);
    }
  };

  const handleClearTrash = async () => {
    crud.trash.setActionLoading(true);
    try {
      const res: { count?: number } = await mailTemplateApi.clearTrash();
      const count = res?.count ?? 0;
      toast.success(t('mail.common.trashCleared', { count }));
      setClearTrashOpen(false);
      crud.trash.refresh();
      crud.trash.refreshCount();
    } catch (e: unknown) {
      toast.error(getApiErrorMessage(e, t('mail.common.clearTrashFailed')));
    } finally {
      crud.trash.setActionLoading(false);
    }
  };

  const openCreate = () => {
    setFormData({
      name: '',
      slug: '',
      type: 1,
      subject: '',
      bodyHtml: '',
      bodyText: '',
      variablesJson: null,
      isActive: true,
      mailboxId: null,
      description: '',
      sortOrder: 0,
    });
    crud.openEditor();
  };

  const openEdit = (template: MailTemplateDto) => {
    setFormData({
      name: template.name,
      slug: template.slug,
      type: template.type,
      subject: template.subject,
      bodyHtml: template.bodyHtml,
      bodyText: template.bodyText,
      variablesJson: template.variablesJson,
      isActive: template.isActive,
      mailboxId: template.mailboxId,
      description: template.description || '',
      sortOrder: template.sortOrder,
    });
    crud.openEditor(template);
  };

  const handleSave = async () => {
    if (!formData.name || !formData.slug || !formData.subject) {
      toast.error(t('mail.templates.toast.requiredFields'));
      return;
    }

    setSaving(true);
    try {
      if (crud.editing) {
        await mailTemplateApi.update(crud.editing.id, formData as UpdateMailTemplateRequest);
        toast.success(t('mail.templates.toast.updated'));
      } else {
        await mailTemplateApi.create(formData);
        toast.success(t('mail.templates.toast.created'));
      }
      crud.closeEditor();
      crud.refresh();
      if (activeTab === 'overview') await loadOverview();
    } catch (e: unknown) {
      toast.error(getApiErrorMessage(e, t('mail.common.saveFailed')));
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    if (!crud.deleteTarget) return;
    setDeleting(true);
    try {
      await mailTemplateApi.remove(crud.deleteTarget.id);
      toast.success(t('mail.templates.toast.movedToTrash', { name: crud.deleteTarget.name }));
      crud.setDeleteTarget(null);
      crud.trash.refreshCount();
      crud.refresh();
      if (activeTab === 'overview') await loadOverview();
    } catch (e: unknown) {
      toast.error(getApiErrorMessage(e, t('mail.common.deleteFailed')));
    } finally {
      setDeleting(false);
    }
  };

  const handlePreview = async () => {
    try {
      const sampleVariables: Record<string, string> = {
        subject: t('mail.templates.preview.sampleSubject'),
        conversation_id: '123',
        conversation_url: `${window.location.origin}/conversations/123`,
        customer_name: t('mail.templates.preview.sampleCustomerName'),
        customer_email: 'customer@example.com',
        user_name: 'Admin User',
        user_email: 'admin@example.com',
        mailbox_name: t('mail.templates.preview.sampleMailboxName'),
        mailbox_email: 'support@example.com',
        message_body: t('mail.templates.preview.sampleMessageBody'),
        created_at: new Date().toLocaleString(toIntlLocale(language)),
        company_name: 'Nonead Customer Service & Support System',
        login_url: `${window.location.origin}/login`,
        reset_password_url: `${window.location.origin}/reset-password?token=xxx`,
        satisfaction_url: `${window.location.origin}/satisfaction/xxx`,
      };

      const result = await mailTemplateApi.preview({
        subject: formData.subject,
        bodyHtml: formData.bodyHtml,
        bodyText: formData.bodyText,
        variables: sampleVariables,
      });
      setPreviewData(result);
      setShowPreview(true);
    } catch (e: unknown) {
      console.error('预览失败', e);
      toast.error(getApiErrorMessage(e, t('mail.templates.toast.previewFailed')));
    }
  };

  const handlePreviewById = async (id: number) => {
    try {
      const result = await mailTemplateApi.previewById(id, {});
      setPreviewData(result);
      setShowPreview(true);
    } catch (e: unknown) {
      console.error('预览失败', e);
      toast.error(getApiErrorMessage(e, t('mail.templates.toast.previewFailed')));
    }
  };

  const insertVariable = (key: string) => {
    const variable = `{{${key}}}`;
    if (editorMode === 'html') {
      setFormData({ ...formData, bodyHtml: formData.bodyHtml + variable });
    } else {
      setFormData({ ...formData, bodyText: formData.bodyText + variable });
    }
  };

  // 列表数据派生
  const templates = crud.list?.data ?? [];
  const totalItems = crud.list?.totalItems ?? 0;
  const totalPages = crud.list?.totalPages ?? 0;
  const page = crud.query.page;
  const keyword = crud.query.keyword;
  const typeFilter = crud.query.type;
  const activeFilter = crud.query.isActive;

  // 变量面板：分组顺序与分组键都取前端字典（`VARIABLE_GROUPS`），不再用后端 `v.category` 中文串。
  // 后端新增、前端字典尚未覆盖的变量回落原始 key，并归入末尾的防御分组。
  const variableGroups = useMemo<VariableGroupView[]>(() => {
    const available = new Set((builtInVariables ?? []).map(v => v.key));
    const groups: VariableGroupView[] = VARIABLE_GROUPS
      .map(group => ({
        categoryKey: group.categoryKey,
        variables: group.variables.filter(v => available.has(v.key)),
      }))
      .filter(group => group.variables.length > 0);
    const unknown = (builtInVariables ?? [])
      .filter(v => !VARIABLE_LABEL_KEYS[v.key])
      .map(v => ({ key: v.key, labelKey: undefined }));
    if (unknown.length > 0) {
      groups.push({ categoryKey: 'mail.templates.variableCategory.other', variables: unknown });
    }
    return groups;
  }, [builtInVariables]);

  // 概览统计项
  const statItems = useMemo(() => {
    if (!overview) return [];
    return [
      { labelKey: 'mail.templates.page.statsTotal' as TranslationKey, value: overview.total, icon: FileText, palette: statPalette[0], hintKey: 'mail.templates.page.statsTotalHint' as TranslationKey },
      { labelKey: 'mail.templates.page.statusEnabled' as TranslationKey, value: overview.activeCount, icon: CheckCircle2, palette: statPalette[1], hintKey: 'mail.templates.page.statsActiveHint' as TranslationKey },
      { labelKey: 'mail.templates.page.statsSystem' as TranslationKey, value: overview.systemCount, icon: Shield, palette: statPalette[2], hintKey: 'mail.templates.page.statsSystemHint' as TranslationKey },
      { labelKey: 'mail.templates.page.statsCustom' as TranslationKey, value: overview.customCount, icon: Sparkles, palette: statPalette[3], hintKey: 'mail.templates.page.statsCustomHint' as TranslationKey },
    ];
  }, [overview]);

  // 当前激活的筛选标签
  const activeTypeLabel = typeFilter !== null ? templateTypeLabel(t, typeFilter) : null;
  // 配色与列表徽章同源（都取数值码）；`typeFilter` 为 null 时的灰色色板不会被渲染
  const activeTypePalette = pickTypePalette(typeFilter ?? 0);

  return (
    <div className="p-6 space-y-5">
      {/* 页头 Hero 区 */}
      <div className="relative overflow-hidden rounded-3xl border border-gray-200 dark:border-slate-700/60 bg-gradient-to-br from-primary-50 via-white to-indigo-50 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-900/20 shadow-sm">
        {/* 装饰光斑 */}
        <div className="pointer-events-none absolute -top-24 -right-24 w-72 h-72 rounded-full bg-primary-500/20 blur-3xl" />
        <div className="pointer-events-none absolute top-16 -left-16 w-60 h-60 rounded-full bg-indigo-500/15 blur-3xl" />
        {/* 内容 */}
        <div className="relative p-6 md:p-7">
          <div className="flex items-start justify-between gap-4 flex-wrap">
            {/* 标题部分 */}
            <div className="flex items-start gap-4">
              <div className="shrink-0 w-14 h-14 rounded-2xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-lg shadow-primary-500/30 flex items-center justify-center ring-1 ring-white/40 dark:ring-white/10">
                <Mail className="w-7 h-7" strokeWidth={2} />
              </div>
              <div className="min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <h1 className="text-2xl md:text-3xl font-extrabold tracking-tight text-gray-900 dark:text-slate-50">{t('mail.templates.page.title')}</h1>
                  <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider rounded-full bg-gradient-to-r from-sky-100 to-blue-100 dark:from-sky-900/40 dark:to-blue-900/40 text-sky-700 dark:text-sky-300 ring-1 ring-sky-500/20">
                    <Mail className="w-3 h-3" />
                    {t('mail.templates.page.badge')}
                  </span>
                </div>
                <p className="text-sm md:text-base text-gray-500 dark:text-slate-400 mt-1.5 leading-relaxed">
                  {t('mail.templates.page.subtitle')}
                </p>
              </div>
            </div>
            {/* 操作按钮 */}
            <div className="flex items-center gap-2.5 flex-wrap">
              <button onClick={openTrash} className={btnSecondary}>
                <Trash2 className="w-4 h-4" />
                {t('mail.common.trash')}
                {crud.trash.count > 0 && (
                  <span className="px-1.5 py-0.5 text-[10px] font-black rounded-full bg-rose-100 dark:bg-rose-900/40 text-rose-600 dark:text-rose-400">
                    {crud.trash.count}
                  </span>
                )}
              </button>
              <button onClick={openCreate} className={btnPrimary}>
                <Plus className="w-4 h-4" />
                {t('mail.templates.page.create')}
              </button>
            </div>
          </div>

          {/* 统计卡片区域（概览模式才显示） */}
          {activeTab === 'overview' && overview && !overviewLoading && (
            <div className="grid grid-cols-2 md:grid-cols-4 gap-3 md:gap-4 relative mt-6">
              {statItems.map((stat, idx) => (
                <div
                  key={idx}
                  className={`group relative overflow-hidden rounded-2xl border border-gray-200 dark:border-slate-700/60 bg-white dark:bg-slate-800/60 p-4 shadow-sm hover:shadow-md transition-all ${stat.palette.bgSoft} ring-1 ${stat.palette.ringSoft}`}
                >
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="text-xs font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t(stat.labelKey)}</p>
                      <p className={`text-2xl md:text-3xl font-extrabold mt-1 ${stat.palette.textLight}`}>{stat.value}</p>
                      <p className="text-[10px] text-gray-400 dark:text-slate-500 mt-0.5">{t(stat.hintKey)}</p>
                    </div>
                    <div className={`shrink-0 w-10 h-10 rounded-xl bg-gradient-to-br ${stat.palette.from} ${stat.palette.to} text-white shadow-sm flex items-center justify-center`}>
                      <stat.icon className="w-5 h-5" strokeWidth={2} />
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Tab 切换条 */}
      <div className="bg-white dark:bg-slate-800/70 rounded-2xl border border-gray-200 dark:border-slate-700/60 shadow-sm p-1.5 inline-flex gap-1">
        <button
          className={`px-5 py-2.5 text-sm font-bold rounded-xl transition-all ${
            activeTab === 'overview'
              ? 'bg-gradient-to-r from-primary-600 to-indigo-600 text-white shadow-sm shadow-primary-500/30 ring-1 ring-primary-500/20'
              : 'text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700/60'
          }`}
          onClick={() => setActiveTab('overview')}
        >
          <span className="inline-flex items-center gap-1.5">
            <Layers className="w-4 h-4" />
            {t('mail.common.overview')}
          </span>
        </button>
        <button
          className={`px-5 py-2.5 text-sm font-bold rounded-xl transition-all ${
            activeTab === 'list'
              ? 'bg-gradient-to-r from-primary-600 to-indigo-600 text-white shadow-sm shadow-primary-500/30 ring-1 ring-primary-500/20'
              : 'text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700/60'
          }`}
          onClick={() => setActiveTab('list')}
        >
          <span className="inline-flex items-center gap-1.5">
            <FileText className="w-4 h-4" />
            {t('mail.templates.page.tabList')}
          </span>
        </button>
      </div>

      {activeTab === 'overview' && (
        <div className="space-y-5">
          {overviewLoading ? (
            <div className="bg-white dark:bg-slate-800/70 rounded-2xl border border-gray-200 dark:border-slate-700/60 shadow-sm">
              <LoadingState text={t('mail.templates.page.loadingOverview')} />
            </div>
          ) : overview && (
            <>
              {/* 类型分布 */}
              <div className="bg-white dark:bg-slate-800/70 rounded-2xl border border-gray-200 dark:border-slate-700/60 shadow-sm p-6">
                <div className="flex items-center justify-between mb-5">
                  <h3 className="text-base font-extrabold text-gray-800 dark:text-slate-100 flex items-center gap-2">
                    <div className="w-8 h-8 rounded-lg bg-gradient-to-br from-primary-500 to-indigo-500 text-white flex items-center justify-center shadow-sm">
                      <Database className="w-4 h-4" strokeWidth={2} />
                    </div>
                    {t('mail.templates.page.byType')}
                  </h3>
                  <span className="text-xs text-gray-400 dark:text-slate-500">{t('mail.templates.page.totalTemplates', { count: overview.total })}</span>
                </div>
                <div className="space-y-3">
                  {overview.typeCounts?.map((tc: MailTemplateTypeCount) => {
                    const palette = pickTypePalette(tc.type);
                    const typeLabel = templateTypeLabel(t, tc.type);
                    const percent = overview.total > 0 ? Math.max((tc.count / overview.total) * 100, 5) : 0;
                    return (
                      <div key={tc.type} className="flex items-center gap-3">
                        <span className="w-32 text-sm font-medium text-gray-700 dark:text-slate-300 truncate flex items-center gap-1.5" title={typeLabel}>
                          <span className={`w-2 h-2 rounded-full ${palette.accent}`} />
                          {typeLabel}
                        </span>
                        <div className="flex-1 bg-gray-100 dark:bg-slate-700/50 rounded-full h-8 relative overflow-hidden">
                          <div
                            className={`bg-gradient-to-r ${palette.from} ${palette.to} h-8 rounded-full transition-all duration-500 flex items-center justify-end pr-2`}
                            style={{ width: `${percent}%` }}
                          >
                            <span className="text-[11px] font-black text-white drop-shadow">
                              {tc.count}
                            </span>
                          </div>
                        </div>
                        <span className="w-12 text-right text-xs text-gray-400 dark:text-slate-500 font-mono">
                          {overview.total > 0 ? ((tc.count / overview.total) * 100).toFixed(0) : 0}%
                        </span>
                      </div>
                    );
                  })}
                </div>
              </div>
            </>
          )}
        </div>
      )}

      {activeTab === 'list' && (
        <div className="space-y-4">
          {/* 筛选栏 */}
          <div className="bg-white dark:bg-slate-800/70 rounded-2xl border border-gray-200 dark:border-slate-700/60 shadow-sm p-4">
            <div className="flex flex-wrap gap-3 items-center">
              <div className="flex-1 min-w-[200px] relative">
                <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400 dark:text-slate-500" />
                <input
                  type="text"
                  placeholder={t('mail.templates.page.searchPlaceholder')}
                  value={keywordInput}
                  onChange={(e) => handleKeywordInput(e.target.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter') triggerKeywordSearch(); }}
                  className={inputBase + ' pl-10 !py-2'}
                />
              </div>
              <select
                value={typeFilter ?? ''}
                onChange={(e) => crud.handleFilterChange('type', e.target.value ? Number(e.target.value) : null)}
                className={inputBase + ' !py-2 !w-auto'}
              >
                <option value="">{t('mail.templates.page.allTypes')}</option>
                {TEMPLATE_TYPE_OPTIONS.map(opt => (
                  <option key={opt.value} value={opt.value}>{t(opt.labelKey)}</option>
                ))}
              </select>
              <select
                value={activeFilter === null ? '' : String(activeFilter)}
                onChange={(e) => {
                  const val = e.target.value;
                  crud.handleFilterChange('isActive', val === '' ? null : val === 'true');
                }}
                className={inputBase + ' !py-2 !w-auto'}
              >
                <option value="">{t('mail.common.allStatuses')}</option>
                <option value="true">{t('mail.templates.page.statusEnabled')}</option>
                <option value="false">{t('mail.templates.page.statusDisabled')}</option>
              </select>
            </div>

            {/* 筛选摘要 */}
            {(activeTypeLabel || activeFilter !== null || keyword) && (
              <div className="mt-3 pt-3 border-t border-gray-100 dark:border-slate-700/60 flex flex-wrap items-center gap-2">
                <span className="inline-flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mr-1">
                  <Filter className="w-3.5 h-3.5" />
                  {t('mail.common.currentFilters')}
                </span>
                {keyword && (
                  <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-xs font-bold bg-gray-100 dark:bg-slate-700/60 text-gray-700 dark:text-slate-300 ring-1 ring-gray-500/10">
                    <Search className="w-3 h-3" />
                    {keyword}
                    <button
                      onClick={() => crud.handleFilterChange('keyword', '')}
                      className="ml-1 opacity-70 hover:opacity-100"
                    >
                      <X className="w-3 h-3" />
                    </button>
                  </span>
                )}
                {activeTypeLabel && (
                  <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-xs font-bold ${activeTypePalette.bg} ${activeTypePalette.text} ring-1 ${activeTypePalette.ringSoft}`}>
                    <Tag className="w-3 h-3" />
                    {activeTypeLabel}
                    <button
                      onClick={() => crud.handleFilterChange('type', null)}
                      className="ml-1 opacity-70 hover:opacity-100"
                    >
                      <X className="w-3 h-3" />
                    </button>
                  </span>
                )}
                {activeFilter !== null && (
                  <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-xs font-bold ${activeFilter ? 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-400 ring-1 ring-emerald-500/20' : 'bg-gray-100 dark:bg-slate-700/60 text-gray-600 dark:text-slate-400 ring-1 ring-gray-500/10'}`}>
                    {activeFilter ? <CheckCircle2 className="w-3 h-3" /> : <XCircle className="w-3 h-3" />}
                    {activeFilter ? t('mail.templates.page.statusEnabled') : t('mail.templates.page.statusDisabled')}
                    <button
                      onClick={() => crud.handleFilterChange('isActive', null)}
                      className="ml-1 opacity-70 hover:opacity-100"
                    >
                      <X className="w-3 h-3" />
                    </button>
                  </span>
                )}
              </div>
            )}
          </div>

          {/* 表格 */}
          {crud.loading ? (
            <div className="bg-white dark:bg-slate-800/70 rounded-2xl border border-gray-200 dark:border-slate-700/60 shadow-sm">
              <LoadingState />
            </div>
          ) : templates.length === 0 ? (
            <div className="rounded-3xl border border-dashed border-gray-200 dark:border-slate-700 bg-white/60 dark:bg-slate-800/40 shadow-sm">
              <EmptyState
                icon={FileText}
                title={keyword || activeTypeLabel || activeFilter !== null ? t('mail.templates.list.emptyFilteredTitle') : t('mail.templates.list.emptyTitle')}
                subtitle={keyword || activeTypeLabel || activeFilter !== null ? t('mail.templates.list.emptyFilteredSubtitle') : t('mail.templates.list.emptySubtitle')}
                action={
                  !keyword && !activeTypeLabel && activeFilter === null && (
                    <button onClick={openCreate} className={btnPrimary}>
                      <Plus className="w-4 h-4" />
                      {t('mail.templates.list.createFirst')}
                    </button>
                  )
                }
              />
            </div>
          ) : (
            <div className="bg-white dark:bg-slate-800/70 rounded-2xl shadow-sm border border-gray-200 dark:border-slate-700/60 overflow-hidden">
              {/* PC 端表格视图 */}
              <div className="hidden md:block overflow-x-auto">
                <table className="w-full">
                  <thead className="bg-gradient-to-r from-primary-50/80 to-indigo-50/80 dark:from-slate-900/60 dark:to-slate-800/60 border-b border-gray-200 dark:border-slate-700/60">
                    <tr>
                      <th className="px-4 md:px-5 py-3.5 text-left text-xs font-extrabold text-slate-600 dark:text-slate-300 uppercase tracking-wider">{t('mail.templates.list.colName')}</th>
                      <th className="px-4 md:px-5 py-3.5 text-left text-xs font-extrabold text-slate-600 dark:text-slate-300 uppercase tracking-wider">{t('mail.templates.list.colSlug')}</th>
                      <th className="px-4 md:px-5 py-3.5 text-left text-xs font-extrabold text-slate-600 dark:text-slate-300 uppercase tracking-wider">{t('mail.common.type')}</th>
                      <th className="px-4 md:px-5 py-3.5 text-left text-xs font-extrabold text-slate-600 dark:text-slate-300 uppercase tracking-wider">{t('mail.common.status')}</th>
                      <th className="px-4 md:px-5 py-3.5 text-left text-xs font-extrabold text-slate-600 dark:text-slate-300 uppercase tracking-wider">{t('mail.templates.list.colSource')}</th>
                      <th className="px-4 md:px-5 py-3.5 text-right text-xs font-extrabold text-slate-600 dark:text-slate-300 uppercase tracking-wider">{t('mail.common.actions')}</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100 dark:divide-slate-700/50">
                    {templates.map(tpl => {
                      const palette = pickTypePalette(tpl.type);
                      return (
                        <tr key={tpl.id} className="hover:bg-gradient-to-r hover:from-primary-50/30 hover:via-white hover:to-indigo-50/30 dark:hover:from-slate-800/60 dark:hover:via-slate-800/30 dark:hover:to-slate-800/60 transition-colors">
                          <td className="px-4 md:px-5 py-3.5">
                            <div className="flex items-center gap-2">
                              <div className={`w-1 h-10 rounded-full bg-gradient-to-b ${palette.from} ${palette.to}`} />
                              <div className="min-w-0">
                                <div className="font-bold text-gray-800 dark:text-slate-100 tracking-tight truncate">{tpl.name}</div>
                                {tpl.description && <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-1">{tpl.description}</div>}
                              </div>
                            </div>
                          </td>
                          <td className="px-4 md:px-5 py-3.5">
                            <span className="inline-flex items-center gap-1 px-2 py-1 rounded-md bg-gray-50 dark:bg-slate-700/40 text-gray-600 dark:text-slate-300 text-xs font-mono ring-1 ring-gray-500/10">
                              <Hash className="w-3 h-3 opacity-50" />
                              {tpl.slug}
                            </span>
                          </td>
                          <td className="px-4 md:px-5 py-3.5">
                            <span className={`inline-flex items-center gap-1 px-2 py-1 rounded-md text-xs font-bold ${palette.bg} ${palette.text} ring-1 ${palette.ringSoft}`}>
                              {templateTypeLabel(t, tpl.type)}
                            </span>
                          </td>
                          <td className="px-4 md:px-5 py-3.5">
                            <span className={`inline-flex items-center gap-1.5 px-2 py-1 text-xs font-bold rounded-full ${
                              tpl.isActive
                                ? 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-400 ring-1 ring-emerald-500/20'
                                : 'bg-gray-100 dark:bg-slate-700/60 text-gray-600 dark:text-slate-400 ring-1 ring-gray-500/10'
                            }`}>
                              <span className={`w-1.5 h-1.5 rounded-full ${tpl.isActive ? 'bg-emerald-500 animate-pulse' : 'bg-gray-400'}`} />
                              {tpl.isActive ? t('mail.common.enabled') : t('mail.common.disabled')}
                            </span>
                          </td>
                          <td className="px-4 md:px-5 py-3.5">
                            <span className={`inline-flex items-center gap-1 px-2 py-1 text-xs font-bold rounded-full ${
                              tpl.isSystem
                                ? 'bg-sky-50 dark:bg-sky-900/30 text-sky-700 dark:text-sky-400 ring-1 ring-sky-500/20'
                                : 'bg-violet-50 dark:bg-violet-900/30 text-violet-700 dark:text-violet-400 ring-1 ring-violet-500/20'
                            }`}>
                              {tpl.isSystem ? <Shield className="w-3 h-3" /> : <Sparkles className="w-3 h-3" />}
                              {tpl.isSystem ? t('mail.common.system') : t('mail.common.custom')}
                            </span>
                          </td>
                          <td className="px-4 md:px-5 py-3.5 text-right whitespace-nowrap">
                            <div className="inline-flex items-center gap-1">
                              <button
                                onClick={() => handlePreviewById(tpl.id)}
                                className="p-2 text-gray-400 dark:text-slate-500 hover:text-sky-600 dark:hover:text-sky-400 hover:bg-sky-50 dark:hover:bg-sky-900/30 rounded-xl transition-all"
                                title={t('mail.common.preview')}
                              >
                                <Eye className="w-[18px] h-[18px]" strokeWidth={2} />
                              </button>
                              <button
                                onClick={() => openEdit(tpl)}
                                className="p-2 text-gray-400 dark:text-slate-500 hover:text-primary-600 dark:hover:text-primary-400 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition-all"
                                title={t('common.edit')}
                              >
                                <Edit2 className="w-[18px] h-[18px]" strokeWidth={2} />
                              </button>
                              <button
                                onClick={() => {
                                  if (tpl.isSystem) {
                                    toast.error(t('mail.templates.list.systemNotDeletable'));
                                    return;
                                  }
                                  crud.setDeleteTarget(tpl);
                                }}
                                className={`p-2 rounded-xl transition-all ${
                                  tpl.isSystem
                                    ? 'text-gray-300 dark:text-slate-600 cursor-not-allowed'
                                    : 'text-gray-400 dark:text-slate-500 hover:text-rose-600 dark:hover:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-900/30'
                                }`}
                                disabled={tpl.isSystem}
                                title={tpl.isSystem ? t('mail.templates.list.systemNotDeletable') : t('common.delete')}
                              >
                                <Trash2 className="w-[18px] h-[18px]" strokeWidth={2} />
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
                {templates.map(tpl => {
                  const palette = pickTypePalette(tpl.type);
                  return (
                    <div key={tpl.id} className="p-4 hover:bg-gradient-to-r hover:from-primary-50/30 hover:via-white hover:to-indigo-50/30 dark:hover:from-slate-800/60 dark:hover:via-slate-800/30 dark:hover:to-slate-800/60 transition-colors">
                      {/* 顶部：名称 + 操作按钮 */}
                      <div className="flex items-start justify-between gap-3">
                        <div className="flex items-start gap-2 min-w-0 flex-1">
                          <div className={`shrink-0 w-1 self-stretch rounded-full bg-gradient-to-b ${palette.from} ${palette.to}`} />
                          <div className="min-w-0 flex-1">
                            <div className="font-bold text-gray-800 dark:text-slate-100 tracking-tight truncate">{tpl.name}</div>
                            {tpl.description && <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-1">{tpl.description}</div>}
                          </div>
                        </div>
                        <div className="shrink-0 inline-flex items-center gap-1">
                          <button
                            onClick={() => handlePreviewById(tpl.id)}
                            className="p-2 text-gray-400 dark:text-slate-500 hover:text-sky-600 dark:hover:text-sky-400 hover:bg-sky-50 dark:hover:bg-sky-900/30 rounded-xl transition-all"
                            title={t('mail.common.preview')}
                          >
                            <Eye className="w-[18px] h-[18px]" strokeWidth={2} />
                          </button>
                          <button
                            onClick={() => openEdit(tpl)}
                            className="p-2 text-gray-400 dark:text-slate-500 hover:text-primary-600 dark:hover:text-primary-400 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition-all"
                            title={t('common.edit')}
                          >
                            <Edit2 className="w-[18px] h-[18px]" strokeWidth={2} />
                          </button>
                          <button
                            onClick={() => {
                              if (tpl.isSystem) {
                                toast.error(t('mail.templates.list.systemNotDeletable'));
                                return;
                              }
                              crud.setDeleteTarget(tpl);
                            }}
                            className={`p-2 rounded-xl transition-all ${
                              tpl.isSystem
                                ? 'text-gray-300 dark:text-slate-600 cursor-not-allowed'
                                : 'text-gray-400 dark:text-slate-500 hover:text-rose-600 dark:hover:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-900/30'
                            }`}
                            disabled={tpl.isSystem}
                            title={tpl.isSystem ? t('mail.templates.list.systemNotDeletable') : t('common.delete')}
                          >
                            <Trash2 className="w-[18px] h-[18px]" strokeWidth={2} />
                          </button>
                        </div>
                      </div>
                      {/* 标识 */}
                      <div className="mt-2.5">
                        <span className="inline-flex items-center gap-1 px-2 py-1 rounded-md bg-gray-50 dark:bg-slate-700/40 text-gray-600 dark:text-slate-300 text-xs font-mono ring-1 ring-gray-500/10">
                          <Hash className="w-3 h-3 opacity-50" />
                          {tpl.slug}
                        </span>
                      </div>
                      {/* 标签组：类型 / 状态 / 来源 */}
                      <div className="mt-2.5 flex items-center gap-1.5 flex-wrap">
                        <span className={`inline-flex items-center gap-1 px-2 py-1 rounded-md text-xs font-bold ${palette.bg} ${palette.text} ring-1 ${palette.ringSoft}`}>
                          {templateTypeLabel(t, tpl.type)}
                        </span>
                        <span className={`inline-flex items-center gap-1.5 px-2 py-1 text-xs font-bold rounded-full ${
                          tpl.isActive
                            ? 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-400 ring-1 ring-emerald-500/20'
                            : 'bg-gray-100 dark:bg-slate-700/60 text-gray-600 dark:text-slate-400 ring-1 ring-gray-500/10'
                        }`}>
                          <span className={`w-1.5 h-1.5 rounded-full ${tpl.isActive ? 'bg-emerald-500 animate-pulse' : 'bg-gray-400'}`} />
                          {tpl.isActive ? t('mail.common.enabled') : t('mail.common.disabled')}
                        </span>
                        <span className={`inline-flex items-center gap-1 px-2 py-1 text-xs font-bold rounded-full ${
                          tpl.isSystem
                            ? 'bg-sky-50 dark:bg-sky-900/30 text-sky-700 dark:text-sky-400 ring-1 ring-sky-500/20'
                            : 'bg-violet-50 dark:bg-violet-900/30 text-violet-700 dark:text-violet-400 ring-1 ring-violet-500/20'
                        }`}>
                          {tpl.isSystem ? <Shield className="w-3 h-3" /> : <Sparkles className="w-3 h-3" />}
                          {tpl.isSystem ? t('mail.common.system') : t('mail.common.custom')}
                        </span>
                      </div>
                    </div>
                  );
                })}
              </div>

              {totalPages > 1 && (
                <div className="px-4 md:px-5 py-3.5 border-t border-gray-100 dark:border-slate-700/50 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
                  <span className="text-sm text-gray-500 dark:text-slate-400">
                    {t('mail.templates.list.footerTotalPrefix')}<span className="font-bold text-gray-700 dark:text-slate-300">{totalItems}</span>{t('mail.templates.list.footerPagePrefix')}<span className="font-bold text-gray-700 dark:text-slate-300">{page}</span>{t('mail.templates.list.footerPageSuffix', { total: totalPages })}
                  </span>
                  <div className="flex items-center gap-1.5">
                    <button
                      onClick={() => crud.handlePageChange(Math.max(1, page - 1))}
                      disabled={page === 1}
                      className="px-3 py-1.5 text-sm font-medium text-gray-600 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700/60 disabled:opacity-40 disabled:cursor-not-allowed transition"
                    >
                      {t('mail.templates.list.prevPage')}
                    </button>
                    {Array.from({ length: Math.min(5, totalPages) }).map((_, idx) => {
                      let pageNum: number;
                      if (totalPages <= 5) pageNum = idx + 1;
                      else if (page <= 3) pageNum = idx + 1;
                      else if (page >= totalPages - 2) pageNum = totalPages - 4 + idx;
                      else pageNum = page - 2 + idx;
                      return (
                        <button
                          key={pageNum}
                          onClick={() => crud.handlePageChange(pageNum)}
                          className={`min-w-[2rem] px-2.5 py-1.5 text-sm font-bold rounded-lg transition ${
                            page === pageNum
                              ? 'bg-gradient-to-br from-primary-600 to-indigo-600 text-white border border-primary-600 shadow-sm shadow-primary-500/20'
                              : 'text-gray-600 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 hover:bg-gray-50 dark:hover:bg-slate-700/60'
                          }`}
                        >
                          {pageNum}
                        </button>
                      );
                    })}
                    <button
                      onClick={() => crud.handlePageChange(Math.min(totalPages, page + 1))}
                      disabled={page === totalPages}
                      className="px-3 py-1.5 text-sm font-medium text-gray-600 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700/60 disabled:opacity-40 disabled:cursor-not-allowed transition"
                    >
                      {t('mail.templates.list.nextPage')}
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* 编辑/新建 Modal */}
      {crud.showEditor && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
          <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-2xl w-full max-w-6xl max-h-[92vh] flex flex-col overflow-hidden">
            {/* Modal 头部（渐变装饰） */}
            <div className="relative px-6 py-5 border-b border-gray-200 dark:border-slate-700 bg-gradient-to-r from-primary-50/80 via-white to-indigo-50/80 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-900/20">
              <div className="pointer-events-none absolute -top-12 -right-12 w-40 h-40 rounded-full bg-primary-500/10 blur-2xl" />
              <div className="relative flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div className="shrink-0 w-10 h-10 rounded-xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-md shadow-primary-500/30 flex items-center justify-center">
                    {crud.editing ? <Edit2 className="w-5 h-5" /> : <Plus className="w-5 h-5" />}
                  </div>
                  <div>
                    <h2 className="text-lg font-extrabold text-gray-800 dark:text-slate-100">
                      {crud.editing ? t('mail.templates.editor.titleEdit') : t('mail.templates.editor.titleCreate')}
                    </h2>
                    <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
                      {crud.editing ? t('mail.templates.editor.editing', { name: crud.editing.name }) : t('mail.templates.editor.createHint')}
                    </p>
                  </div>
                </div>
                <button onClick={crud.closeEditor} className="p-2 text-gray-400 hover:text-gray-600 dark:hover:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700 rounded-xl transition">
                  <X className="w-5 h-5" />
                </button>
              </div>
            </div>

            <div className="flex-1 overflow-y-auto p-6 space-y-4">
              {/* 基本信息卡片 */}
              <div className="rounded-2xl border border-gray-100 dark:border-slate-700/60 bg-gray-50/50 dark:bg-slate-900/40 p-4 space-y-4">
                <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400">
                  <FileText className="w-3.5 h-3.5" />
                  {t('mail.templates.editor.basicInfo')}
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-bold text-gray-700 dark:text-slate-300 mb-1.5">{t('mail.templates.editor.nameLabel')} <span className="text-rose-500">*</span></label>
                    <input
                      type="text"
                      value={formData.name}
                      onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                      className={inputBase}
                      placeholder={t('mail.templates.editor.namePlaceholder')}
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-bold text-gray-700 dark:text-slate-300 mb-1.5">{t('mail.templates.editor.slugLabel')} <span className="text-rose-500">*</span></label>
                    <input
                      type="text"
                      value={formData.slug}
                      onChange={(e) => setFormData({ ...formData, slug: e.target.value })}
                      disabled={crud.editing?.isSystem}
                      className={inputBase + ' disabled:bg-gray-100 dark:disabled:bg-slate-800/60 disabled:cursor-not-allowed font-mono'}
                      placeholder={t('mail.templates.editor.slugPlaceholder')}
                    />
                  </div>
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-bold text-gray-700 dark:text-slate-300 mb-1.5">{t('mail.templates.editor.typeLabel')}</label>
                    <select
                      value={formData.type}
                      onChange={(e) => setFormData({ ...formData, type: Number(e.target.value) })}
                      className={inputBase}
                    >
                      {TEMPLATE_TYPE_OPTIONS.map(opt => (
                        <option key={opt.value} value={opt.value}>{t(opt.labelKey)}</option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="block text-sm font-bold text-gray-700 dark:text-slate-300 mb-1.5">{t('mail.common.applicableMailbox')}</label>
                    <select
                      value={formData.mailboxId ?? ''}
                      onChange={(e) => setFormData({ ...formData, mailboxId: e.target.value ? Number(e.target.value) : null })}
                      disabled={crud.editing?.isSystem}
                      className={inputBase + ' disabled:bg-gray-100 dark:disabled:bg-slate-800/60 disabled:cursor-not-allowed'}
                    >
                      <option value="">{t('mail.common.globalAllMailboxes')}</option>
                      {mailboxes.map(m => (
                        <option key={m.id} value={m.id}>{m.name}</option>
                      ))}
                    </select>
                  </div>
                </div>
                <div>
                  <label className="block text-sm font-bold text-gray-700 dark:text-slate-300 mb-1.5">{t('mail.templates.editor.subjectLabel')} <span className="text-rose-500">*</span></label>
                  <input
                    type="text"
                    value={formData.subject}
                    onChange={(e) => setFormData({ ...formData, subject: e.target.value })}
                    className={inputBase}
                    placeholder={t('mail.templates.editor.subjectPlaceholder')}
                  />
                </div>
                <div>
                  <label className="block text-sm font-bold text-gray-700 dark:text-slate-300 mb-1.5">{t('mail.templates.editor.descriptionLabel')}</label>
                  <textarea
                    value={formData.description || ''}
                    onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                    rows={2}
                    className={inputBase}
                    placeholder={t('mail.templates.editor.descriptionPlaceholder')}
                  />
                </div>
              </div>

              {/* 编辑器 + 变量面板 */}
              <div className="grid grid-cols-1 lg:grid-cols-4 gap-4">
                <div className="lg:col-span-3">
                  <div className="flex items-center justify-between mb-2">
                    <div className="flex gap-1.5 bg-gray-100 dark:bg-slate-700/40 p-1 rounded-xl">
                      <button
                        onClick={() => setEditorMode('html')}
                        className={`px-3 py-1.5 text-sm font-bold rounded-lg transition flex items-center gap-1.5 ${
                          editorMode === 'html'
                            ? 'bg-white dark:bg-slate-800 text-primary-700 dark:text-primary-400 shadow-sm'
                            : 'text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-300'
                        }`}
                      >
                        <Code2 className="w-3.5 h-3.5" />
                        {t('mail.templates.editor.modeHtml')}
                      </button>
                      <button
                        onClick={() => setEditorMode('text')}
                        className={`px-3 py-1.5 text-sm font-bold rounded-lg transition flex items-center gap-1.5 ${
                          editorMode === 'text'
                            ? 'bg-white dark:bg-slate-800 text-primary-700 dark:text-primary-400 shadow-sm'
                            : 'text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-300'
                        }`}
                      >
                        <FileText className="w-3.5 h-3.5" />
                        {t('mail.templates.editor.modeText')}
                      </button>
                    </div>
                    <button
                      onClick={handlePreview}
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 text-sm font-bold text-primary-700 dark:text-primary-400 hover:text-primary-800 dark:hover:text-primary-300 bg-primary-50 dark:bg-primary-900/30 hover:bg-primary-100 dark:hover:bg-primary-900/50 rounded-lg transition ring-1 ring-primary-500/20"
                    >
                      <MonitorPlay className="w-4 h-4" />
                      {t('mail.templates.editor.preview')}
                    </button>
                  </div>
                  {editorMode === 'html' ? (
                    <textarea
                      value={formData.bodyHtml}
                      onChange={(e) => setFormData({ ...formData, bodyHtml: e.target.value })}
                      rows={15}
                      className={inputBase + ' font-mono text-sm !rounded-xl'}
                      placeholder={t('mail.templates.editor.htmlPlaceholder')}
                    />
                  ) : (
                    <textarea
                      value={formData.bodyText}
                      onChange={(e) => setFormData({ ...formData, bodyText: e.target.value })}
                      rows={15}
                      className={inputBase + ' font-mono text-sm !rounded-xl'}
                      placeholder={t('mail.templates.editor.textPlaceholder')}
                    />
                  )}
                </div>

                {/* 右侧变量面板 */}
                <div>
                  <div className="flex items-center justify-between mb-2">
                    <label className="text-sm font-bold text-gray-700 dark:text-slate-300 flex items-center gap-1.5">
                      <Zap className="w-3.5 h-3.5 text-amber-500" />
                      {t('mail.templates.editor.availableVariables')}
                    </label>
                    <span className="text-[10px] text-gray-400 dark:text-slate-500 font-mono">{t('mail.templates.editor.variableCount', { count: builtInVariables.length })}</span>
                  </div>
                  <div className="border border-gray-200 dark:border-slate-700/60 rounded-xl p-3 max-h-[420px] overflow-y-auto bg-white dark:bg-slate-900/60 shadow-sm">
                    {variableGroups.map((group) => (
                      <div key={group.categoryKey} className="mb-3 last:mb-0">
                        <p className="text-[10px] font-extrabold text-gray-500 dark:text-slate-400 mb-1.5 uppercase tracking-wider flex items-center gap-1">
                          <Tag className="w-2.5 h-2.5 opacity-60" />
                          {t(group.categoryKey)}
                        </p>
                        <div className="space-y-1">
                          {group.variables.map(v => (
                            <button
                              key={v.key}
                              onClick={() => insertVariable(v.key)}
                              className="w-full text-left text-xs px-2 py-1.5 hover:bg-primary-50 dark:hover:bg-slate-700/60 rounded-lg text-gray-700 dark:text-slate-300 flex justify-between items-center transition group ring-1 ring-transparent hover:ring-primary-500/20"
                              title={v.labelKey ? t(v.labelKey) : v.key}
                            >
                              <span className="font-mono text-primary-600 dark:text-primary-400 font-bold">{'{{' + v.key + '}}'}</span>
                              <span className="text-gray-500 dark:text-slate-400 truncate ml-2 text-[10px]">{v.labelKey ? t(v.labelKey) : v.key}</span>
                            </button>
                          ))}
                        </div>
                      </div>
                    ))}
                  </div>
                  <p className="text-[11px] text-gray-400 dark:text-slate-500 mt-2 flex items-center gap-1">
                    <AlertCircle className="w-3 h-3" />
                    {t('mail.templates.editor.clickToInsert')}
                  </p>
                </div>
              </div>

              {/* 启用开关 + 排序 */}
              <div className="flex items-center gap-6 flex-wrap rounded-2xl border border-gray-100 dark:border-slate-700/60 bg-gray-50/50 dark:bg-slate-900/40 p-4">
                <label className="flex items-center gap-3 cursor-pointer group">
                  <div className="relative">
                    <input
                      type="checkbox"
                      checked={formData.isActive}
                      onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                      className="sr-only peer"
                    />
                    <div className="w-11 h-6 bg-gray-200 dark:bg-slate-700 rounded-full peer peer-checked:bg-gradient-to-r peer-checked:from-primary-500 peer-checked:to-indigo-500 transition-all" />
                    <div className="absolute left-0.5 top-0.5 w-5 h-5 bg-white rounded-full shadow-sm transition-all peer-checked:translate-x-5" />
                  </div>
                  <div>
                    <p className="text-sm font-bold text-gray-800 dark:text-slate-100">{t('mail.templates.editor.enableTemplate')}</p>
                    <p className="text-[11px] text-gray-500 dark:text-slate-400">{t('mail.templates.editor.enableHint')}</p>
                  </div>
                </label>
                <div className="flex items-center gap-2">
                  <label className="text-sm font-bold text-gray-700 dark:text-slate-300">{t('mail.templates.editor.sortOrderLabel')}</label>
                  <input
                    type="number"
                    value={formData.sortOrder}
                    onChange={(e) => setFormData({ ...formData, sortOrder: Number(e.target.value) })}
                    className="w-24 px-3 py-2 text-sm bg-white dark:bg-slate-900 border border-gray-200 dark:border-slate-700 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 dark:text-slate-200 shadow-sm"
                  />
                </div>
              </div>
            </div>

            {/* Modal 底部 */}
            <div className="px-6 py-4 border-t border-gray-200 dark:border-slate-700 flex justify-end gap-3 bg-gray-50/50 dark:bg-slate-900/40">
              <button
                onClick={crud.closeEditor}
                className={btnSecondary}
              >
                {t('common.cancel')}
              </button>
              <button
                onClick={handleSave}
                disabled={saving}
                className={btnPrimary}
              >
                {saving ? (
                  <>
                    <RefreshCw className="w-4 h-4 animate-spin" />
                    {t('mail.templates.editor.saving')}
                  </>
                ) : (
                  <>
                    <CheckCircle2 className="w-4 h-4" />
                    {t('common.save')}
                  </>
                )}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 预览 Modal */}
      {showPreview && previewData && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
          <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-2xl w-full max-w-3xl max-h-[92vh] flex flex-col overflow-hidden">
            {/* Modal 头部 */}
            <div className="relative px-6 py-5 border-b border-gray-200 dark:border-slate-700 bg-gradient-to-r from-sky-50/80 via-white to-indigo-50/80 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-900/20">
              <div className="pointer-events-none absolute -top-12 -right-12 w-40 h-40 rounded-full bg-sky-500/10 blur-2xl" />
              <div className="relative flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div className="shrink-0 w-10 h-10 rounded-xl bg-gradient-to-br from-sky-500 to-indigo-500 text-white shadow-md shadow-sky-500/30 flex items-center justify-center">
                    <MonitorPlay className="w-5 h-5" />
                  </div>
                  <div>
                    <h2 className="text-lg font-extrabold text-gray-800 dark:text-slate-100">{t('mail.templates.preview.title')}</h2>
                    <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{t('mail.templates.preview.subtitle')}</p>
                  </div>
                </div>
                <button onClick={() => setShowPreview(false)} className="p-2 text-gray-400 hover:text-gray-600 dark:hover:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700 rounded-xl transition">
                  <X className="w-5 h-5" />
                </button>
              </div>
            </div>
            <div className="flex-1 overflow-y-auto p-6 space-y-4">
              {/* 主题展示 */}
              <div className="bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-900 dark:to-slate-800 p-4 rounded-xl border border-primary-100 dark:border-slate-700/60">
                <p className="text-[10px] font-extrabold text-gray-500 dark:text-slate-400 mb-1 uppercase tracking-wider flex items-center gap-1">
                  <Mail className="w-3 h-3" />
                  {t('mail.templates.preview.subject')}
                </p>
                <p className="font-bold text-gray-800 dark:text-slate-100">{previewData.subject}</p>
              </div>

              {/* 切换 Tab */}
              <div className="flex gap-1.5 bg-gray-100 dark:bg-slate-700/40 p-1 rounded-xl w-fit">
                <button
                  onClick={() => setPreviewMode('html')}
                  className={`px-4 py-1.5 text-sm font-bold rounded-lg transition flex items-center gap-1.5 ${
                    previewMode === 'html'
                      ? 'bg-white dark:bg-slate-800 text-primary-700 dark:text-primary-400 shadow-sm'
                      : 'text-gray-500 dark:text-slate-400'
                  }`}
                >
                  <MonitorPlay className="w-3.5 h-3.5" />
                  {t('mail.templates.preview.html')}
                </button>
                <button
                  onClick={() => setPreviewMode('text')}
                  className={`px-4 py-1.5 text-sm font-bold rounded-lg transition flex items-center gap-1.5 ${
                    previewMode === 'text'
                      ? 'bg-white dark:bg-slate-800 text-primary-700 dark:text-primary-400 shadow-sm'
                      : 'text-gray-500 dark:text-slate-400'
                  }`}
                >
                  <Code2 className="w-3.5 h-3.5" />
                  {t('mail.templates.preview.raw')}
                </button>
              </div>

              {previewMode === 'html' ? (
                <div className="border border-gray-200 dark:border-slate-700/60 rounded-xl overflow-hidden bg-white dark:bg-slate-900/40 shadow-sm">
                  {/* 浏览器窗口样式装饰 */}
                  <div className="flex items-center gap-1.5 px-4 py-2.5 bg-gray-50 dark:bg-slate-800/80 border-b border-gray-200 dark:border-slate-700/60">
                    <div className="w-2.5 h-2.5 rounded-full bg-rose-400" />
                    <div className="w-2.5 h-2.5 rounded-full bg-amber-400" />
                    <div className="w-2.5 h-2.5 rounded-full bg-emerald-400" />
                    <div className="flex-1 ml-3 px-3 py-1 bg-white dark:bg-slate-900/60 rounded-md text-[10px] text-gray-400 dark:text-slate-500 font-mono truncate">
                      preview://email-template
                    </div>
                  </div>
                  <div className="p-5 min-h-[320px]">
                    <div
                      className="prose prose-sm max-w-none dark:prose-invert"
                      dangerouslySetInnerHTML={{ __html: sanitizeHtml(previewData.bodyHtml) }}
                    />
                  </div>
                </div>
              ) : (
                <div className="border border-gray-200 dark:border-slate-700/60 rounded-xl p-4 min-h-[320px] bg-gray-50 dark:bg-slate-900/40 shadow-sm">
                  <pre className="text-sm text-gray-700 dark:text-slate-300 whitespace-pre-wrap font-mono">{previewData.bodyText}</pre>
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* 删除确认 */}
      <ConfirmDialog
        open={!!crud.deleteTarget}
        title={t('mail.templates.delete.title')}
        message={t('mail.templates.delete.message', { name: crud.deleteTarget?.name ?? '' })}
        confirmText={t('common.delete')}
        variant="danger"
        loading={deleting}
        onConfirm={handleDelete}
        onCancel={() => crud.setDeleteTarget(null)}
      />

      {/* 垃圾箱弹窗 */}
      <MailTemplateTrashModal
        open={crud.trash.open}
        onClose={() => crud.trash.setOpen(false)}
        query={crud.trash.query}
        onQueryChange={crud.trash.setQuery}
        list={crud.trash.list}
        loading={crud.trash.loading}
        count={crud.trash.count}
        actionLoading={crud.trash.actionLoading}
        restoreTarget={restoreTarget}
        onSetRestoreTarget={setRestoreTarget}
        hardDeleteTarget={hardDeleteTarget}
        onSetHardDeleteTarget={setHardDeleteTarget}
        clearTrashOpen={clearTrashOpen}
        onSetClearTrashOpen={setClearTrashOpen}
        onRestore={handleRestore}
        onHardDelete={handleHardDelete}
        onClearTrash={handleClearTrash}
        mailboxes={mailboxes}
      />
    </div>
  );
}
