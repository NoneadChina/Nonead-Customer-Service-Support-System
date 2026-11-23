import { useEffect, useState, useCallback } from 'react';
import { notificationApi, mailboxApi, userApi } from '../api';
import type {
  NotificationTemplateResponse,
  NotificationRuleResponse,
  MailboxResponse,
  UserResponse,
  CreateNotificationTemplateRequest,
  CreateNotificationRuleRequest,
  SendNotificationRequest,
} from '../api/types';
import { formatDateTime, sanitizeHtml } from '../utils/format';
import { useI18n } from '../context/I18nContext';
import { toIntlLocale } from '../i18n/locale';
import type { TranslateParams } from '../i18n/types';
import type { TranslationKey } from '../locales/zh-Hans';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import {
  Bell,
  FileText,
  Send,
  Eye,
  Plus,
  Search,
  Edit2,
  Trash2,
  CheckCircle2,
  XCircle,
  X,
  AlertCircle,
  AlertTriangle,
  Copy,
  Code,
  User,
  Mailbox,
  Zap,
  Activity,
  Info,
  Sparkles,
  Settings2,
  FilterX,
  RefreshCw,
} from 'lucide-react';

/** 与 `I18nContext` 的 `t` 签名一致；模块级纯函数需显式传入 `t` */
type TranslateFn = (key: TranslationKey, params?: TranslateParams) => string;

type TabKey = 'templates' | 'rules' | 'send' | 'preview';

interface TabItem {
  key: TabKey;
  label: TranslationKey;
  icon: React.ReactNode;
  hint?: TranslationKey;
}

/**
 * 事件类型选项。**只持有真源 key**（`value` + key），渲染处一律 `t(opt.label)`——
 * 直接渲染表里的值会把 `notifications.eventType.…` 这样的原始 key 漏到界面上。
 * 文案真源是 `notifications.eventType.*`（三个通知页面共用），此处不重复造键。
 */
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

/**
 * 后端 `NotificationService.GetEventTypeLabel` 还识别 `ticket_created` / `ticket_updated`，
 * 但本页的筛选项刻意只暴露实际在用的 22 个码（全库无此类模板/规则）。
 * 这两条单独登记：筛选项不变，而万一数据里出现该码也不会把原始码渲染到界面。
 */
const extraEventTypeLabelKeys: Record<string, TranslationKey> = {
  ticket_created: 'notifications.eventType.ticketCreated',
  ticket_updated: 'notifications.eventType.ticketUpdated',
};

const eventTypeLabelKeys: Record<string, TranslationKey> = {
  ...Object.fromEntries(eventTypeOptions.map((o) => [o.value, o.label] as const)),
  ...extraEventTypeLabelKeys,
};

/** 未知事件码回落原码，与原行为一致（映射表只持有 key，渲染处经 t() 解析） */
const getEventTypeLabel = (t: TranslateFn, eventType: string) => {
  const key = eventTypeLabelKeys[eventType];
  return key ? t(key) : eventType;
};

// 事件类型 -> 配色（按语义分组：会话/SLA/系统/IM）
const eventTypeColorMap: Record<string, { bg: string; text: string; ring: string; accent: string }> = {
  // 会话类（蓝色系）
  new_conversation:     { bg: 'bg-blue-50 dark:bg-blue-900/25',     text: 'text-blue-700 dark:text-blue-400',     ring: 'ring-blue-600/20',       accent: 'bg-blue-500' },
  new_reply:            { bg: 'bg-sky-50 dark:bg-sky-900/25',       text: 'text-sky-700 dark:text-sky-400',       ring: 'ring-sky-600/20',        accent: 'bg-sky-500' },
  conversation_assigned:{ bg: 'bg-indigo-50 dark:bg-indigo-900/25', text: 'text-indigo-700 dark:text-indigo-400', ring: 'ring-indigo-600/20',     accent: 'bg-indigo-500' },
  conversation_updated: { bg: 'bg-indigo-50 dark:bg-indigo-900/25', text: 'text-indigo-700 dark:text-indigo-400', ring: 'ring-indigo-600/20',     accent: 'bg-indigo-500' },
  conversation_closed:   { bg: 'bg-slate-50 dark:bg-slate-800',      text: 'text-slate-700 dark:text-slate-300',   ring: 'ring-slate-500/20',      accent: 'bg-slate-500' },
  customer_reply:       { bg: 'bg-cyan-50 dark:bg-cyan-900/25',     text: 'text-cyan-700 dark:text-cyan-400',     ring: 'ring-cyan-600/20',       accent: 'bg-cyan-500' },
  mention:              { bg: 'bg-violet-50 dark:bg-violet-900/25', text: 'text-violet-700 dark:text-violet-400', ring: 'ring-violet-600/20',     accent: 'bg-violet-500' },
  conversation_reopened:{ bg: 'bg-indigo-50 dark:bg-indigo-900/25', text: 'text-indigo-700 dark:text-indigo-400', ring: 'ring-indigo-600/20',     accent: 'bg-indigo-500' },
  conversation_transferred:{ bg: 'bg-teal-50 dark:bg-teal-900/25',  text: 'text-teal-700 dark:text-teal-400',     ring: 'ring-teal-600/20',       accent: 'bg-teal-500' },
  // SLA 类（红/橙色）
  sla_violation:        { bg: 'bg-red-50 dark:bg-red-900/25',       text: 'text-red-700 dark:text-red-400',       ring: 'ring-red-600/20',        accent: 'bg-red-500' },
  sla_warning:          { bg: 'bg-amber-50 dark:bg-amber-900/25',   text: 'text-amber-700 dark:text-amber-400',   ring: 'ring-amber-600/20',      accent: 'bg-amber-500' },
  conversation_timeout: { bg: 'bg-orange-50 dark:bg-orange-900/25', text: 'text-orange-700 dark:text-orange-400', ring: 'ring-orange-600/20',     accent: 'bg-orange-500' },
  ticket_escalated:     { bg: 'bg-rose-50 dark:bg-rose-900/25',     text: 'text-rose-700 dark:text-rose-400',     ring: 'ring-rose-600/20',       accent: 'bg-rose-500' },
  // 满意度（绿色）
  customer_satisfaction:{ bg: 'bg-emerald-50 dark:bg-emerald-900/25', text: 'text-emerald-700 dark:text-emerald-400', ring: 'ring-emerald-600/20',   accent: 'bg-emerald-500' },
  satisfaction_submitted:{ bg: 'bg-emerald-50 dark:bg-emerald-900/25', text: 'text-emerald-700 dark:text-emerald-400', ring: 'ring-emerald-600/20',  accent: 'bg-emerald-500' },
  // 自动/账户（紫色/灰色）
  auto_reply:           { bg: 'bg-fuchsia-50 dark:bg-fuchsia-900/25', text: 'text-fuchsia-700 dark:text-fuchsia-400', ring: 'ring-fuchsia-600/20',   accent: 'bg-fuchsia-500' },
  user_welcome:         { bg: 'bg-purple-50 dark:bg-purple-900/25',   text: 'text-purple-700 dark:text-purple-400', ring: 'ring-purple-600/20',     accent: 'bg-purple-500' },
  password_reset:       { bg: 'bg-purple-50 dark:bg-purple-900/25',   text: 'text-purple-700 dark:text-purple-400', ring: 'ring-purple-600/20',     accent: 'bg-purple-500' },
  account_locked:       { bg: 'bg-rose-50 dark:bg-rose-900/25',       text: 'text-rose-700 dark:text-rose-400',     ring: 'ring-rose-600/20',       accent: 'bg-rose-500' },
  // 系统通知
  bulk_import_completed:{ bg: 'bg-lime-50 dark:bg-lime-900/25',     text: 'text-lime-700 dark:text-lime-400',     ring: 'ring-lime-600/20',       accent: 'bg-lime-500' },
  daily_report:         { bg: 'bg-stone-50 dark:bg-stone-800',      text: 'text-stone-700 dark:text-stone-300',   ring: 'ring-stone-500/20',      accent: 'bg-stone-500' },
  system_maintenance:   { bg: 'bg-slate-100 dark:bg-slate-800',     text: 'text-slate-800 dark:text-slate-200',   ring: 'ring-slate-500/20',      accent: 'bg-slate-500' },
};

function getEventTypeColor(evt: string) {
  return eventTypeColorMap[evt] || eventTypeColorMap.new_conversation;
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
    textLight: 'text-emerald-600',
    bgSoft: 'bg-emerald-50 dark:bg-emerald-900/30',
    ringSoft: 'ring-emerald-500/20',
  },
  {
    from: 'from-amber-500',
    to: 'to-orange-500',
    textLight: 'text-amber-600',
    bgSoft: 'bg-amber-50 dark:bg-amber-900/30',
    ringSoft: 'ring-amber-500/20',
  },
  {
    from: 'from-violet-500',
    to: 'to-purple-500',
    textLight: 'text-violet-600',
    bgSoft: 'bg-violet-50 dark:bg-violet-900/30',
    ringSoft: 'ring-violet-500/20',
  },
];

/**
 * 概览统计卡片。**必须给数组显式标注元素类型**：不标注时 `label` 会退化成 `string`，
 * `t(s.label)` 随即报类型错；用 `as StatCard[]` 断言又会绕过字面量校验，故写成
 * 有返回类型标注的构造器，让每条 key 都真正过一遍 `TranslationKey` 检查。
 */
interface StatCard {
  label: TranslationKey;
  value: number;
  icon: React.ComponentType<{ className?: string }>;
  sub: TranslationKey;
  palette: (typeof statPalette)[number];
}

/** 模板 Tab 的三张统计卡 */
const templateStatCards = (stats: { total: number; active: number }): StatCard[] => [
  { label: 'notifications.list.tplStatTotal', value: stats.total, icon: FileText, sub: 'notifications.list.tplStatTotalSub', palette: statPalette[0] },
  { label: 'notifications.list.tplStatActive', value: stats.active, icon: CheckCircle2, sub: 'notifications.list.tplStatActiveSub', palette: statPalette[1] },
  { label: 'notifications.list.tplStatInactive', value: stats.total - stats.active, icon: XCircle, sub: 'notifications.list.tplStatInactiveSub', palette: statPalette[2] },
];

/** 规则 Tab 的三张统计卡 */
const ruleStatCards = (stats: { total: number; active: number }): StatCard[] => [
  { label: 'notifications.list.ruleStatTotal', value: stats.total, icon: Bell, sub: 'notifications.list.ruleStatTotalSub', palette: statPalette[0] },
  { label: 'notifications.list.ruleStatActive', value: stats.active, icon: CheckCircle2, sub: 'notifications.list.ruleStatActiveSub', palette: statPalette[1] },
  { label: 'notifications.list.ruleStatInactive', value: stats.total - stats.active, icon: XCircle, sub: 'notifications.list.ruleStatInactiveSub', palette: statPalette[2] },
];

/** 「全部 / 已启用 / 已停用」分段筛选器（模板 Tab 与规则 Tab 共用同一份 key 表） */
const activeFilterOptions: { label: TranslationKey; value: boolean | null }[] = [
  { label: 'notifications.common.all', value: null },
  { label: 'notifications.common.enabled', value: true },
  { label: 'notifications.common.disabled', value: false },
];

// 常用变量提示
const commonVariables = [
  '{customer_name}',
  '{subject}',
  '{conversation_id}',
  '{user_name}',
  '{mailbox_name}',
  '{mailbox_email}',
];

const tabs: TabItem[] = [
  {
    key: 'templates',
    label: 'notifications.list.tab.templates',
    icon: <FileText className="w-4 h-4" />,
    hint: 'notifications.list.tab.templatesHint',
  },
  {
    key: 'rules',
    label: 'notifications.list.tab.rules',
    icon: <Bell className="w-4 h-4" />,
    hint: 'notifications.list.tab.rulesHint',
  },
  {
    key: 'send',
    label: 'notifications.list.tab.send',
    icon: <Send className="w-4 h-4" />,
    hint: 'notifications.list.tab.sendHint',
  },
  {
    key: 'preview',
    label: 'notifications.list.tab.preview',
    icon: <Eye className="w-4 h-4" />,
    hint: 'notifications.list.tab.previewHint',
  },
];

// ============ 通用：空态插画 ============
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

// ============ 通用：加载中 ============
function LoadingState({ text }: { text?: string }) {
  const { t } = useI18n();
  return (
    <div className="py-16 text-center">
      <div className="inline-flex items-center gap-2 text-gray-400 text-sm">
        <Activity className="w-4 h-4 animate-pulse" />
        {text ?? t('common.shell.loading')}
      </div>
    </div>
  );
}

// ============ 通用：表单输入样式 ============
const inputCls =
  'w-full px-3.5 py-2.5 bg-gray-50 dark:bg-slate-900/60 border border-gray-200 dark:border-slate-700 rounded-xl text-sm focus:outline-none focus:bg-white dark:focus:bg-slate-900 focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:text-slate-200 transition-all';

const selectCls = inputCls + ' appearance-none pr-9';

const labelCls = 'block text-[13px] font-semibold text-gray-700 dark:text-slate-300 mb-1.5';

// ====== 模板 Tab ======
function TemplatesTab() {
  const { t, language } = useI18n();
  const [list, setList] = useState<NotificationTemplateResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<CreateNotificationTemplateRequest>({
    name: '',
    subject: '',
    body: '',
    eventType: eventTypeOptions[0].value,
    isActive: true,
  });
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState('');

  const [deleteTarget, setDeleteTarget] = useState<NotificationTemplateResponse | null>(null);
  const [deleting, setDeleting] = useState(false);

  const [search, setSearch] = useState('');
  const [eventTypeFilter, setEventTypeFilter] = useState<string | null>(null);
  const [activeFilter, setActiveFilter] = useState<boolean | null>(null);

  const fetch = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const resp = await notificationApi.listAllTemplates();
      setList(Array.isArray(resp) ? resp : []);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('notifications.list.loadFailed'));
    } finally {
      setLoading(false);
    }
    // `t` 只用于错误兜底文案。把它列进依赖数组会让 I18nProvider 的 DB 覆盖层落地后
    // 本回调重建、`useEffect(..., [fetch])` 随即**重复拉一次列表**（首屏多一次请求 +
    // 二次 loading 闪烁）。故此处刻意省略该依赖，保持"仅挂载时拉一次"的原行为。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    fetch();
  }, [fetch]);

  const filteredList = list.filter((tpl) => {
    if (search && !tpl.name.toLowerCase().includes(search.toLowerCase()) && !tpl.subject.toLowerCase().includes(search.toLowerCase())) {
      return false;
    }
    if (eventTypeFilter && tpl.eventType !== eventTypeFilter) return false;
    if (activeFilter !== null && tpl.isActive !== activeFilter) return false;
    return true;
  });

  const stats = {
    total: list.length,
    active: list.filter((tpl) => tpl.isActive).length,
  };

  const openCreate = () => {
    setEditingId(null);
    setForm({
      name: '',
      subject: '',
      body: '',
      eventType: eventTypeOptions[0].value,
      isActive: true,
    });
    setFormError('');
    setModalOpen(true);
  };

  const openEdit = (tpl: NotificationTemplateResponse) => {
    setEditingId(tpl.id);
    setForm({
      name: tpl.name,
      subject: tpl.subject,
      body: tpl.body,
      eventType: tpl.eventType,
      isActive: tpl.isActive,
    });
    setFormError('');
    setModalOpen(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!form.name.trim() || !form.subject.trim() || !form.body.trim()) {
      setFormError(t('notifications.list.tplFormRequired'));
      return;
    }
    setSubmitting(true);
    setFormError('');
    try {
      const payload: CreateNotificationTemplateRequest = {
        ...form,
        name: form.name.trim(),
        subject: form.subject.trim(),
        body: form.body.trim(),
      };
      if (editingId) {
        await notificationApi.updateTemplate(editingId, payload);
      } else {
        await notificationApi.createTemplate(payload);
      }
      setModalOpen(false);
      await fetch();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : t('notifications.list.opFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await notificationApi.removeTemplate(deleteTarget.id);
      setDeleteTarget(null);
      await fetch();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : t('notifications.common.deleteFailed'));
    } finally {
      setDeleting(false);
    }
  };

  return (
    <div className="space-y-5">
      {/* 顶部统计卡片 */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-5">
        {templateStatCards(stats).map((s) => (
          <div
            key={s.label}
            className="group relative bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/80 dark:border-slate-700/60 p-6 overflow-hidden shadow-sm hover:shadow-lg hover:-translate-y-0.5 transition-all duration-300"
          >
            <div className={`absolute left-0 top-0 bottom-0 w-1.5 bg-gradient-to-b ${s.palette.from} ${s.palette.to}`} />
            <div className={`absolute -right-8 -top-8 w-32 h-32 rounded-full ${s.palette.bgSoft} opacity-60 blur-2xl pointer-events-none`} />
            <div className="relative flex items-start justify-between">
              <div className="flex-1">
                <span className="text-xs font-medium text-gray-500 dark:text-slate-400 tracking-wider uppercase">
                  {t(s.label)}
                </span>
                <div className="mt-3 flex items-baseline gap-2">
                  <span className="text-4xl font-extrabold text-gray-900 dark:text-slate-50 tabular-nums tracking-tight">
                    {s.value.toLocaleString(toIntlLocale(language))}
                  </span>
                  <span className="text-sm text-gray-400 dark:text-slate-500">{t('notifications.list.statUnit', { count: s.value })}</span>
                </div>
                <div className="mt-2 text-xs text-gray-500 dark:text-slate-400 flex items-center gap-1">
                  <Info className="w-3 h-3 opacity-60" />
                  {t(s.sub)}
                </div>
              </div>
              <div className={`relative p-3 rounded-xl ${s.palette.bgSoft} ring-1 ${s.palette.ringSoft} group-hover:scale-110 transition-transform`}>
                <s.icon className={`w-6 h-6 ${s.palette.textLight}`} />
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* 筛选栏 + 操作 */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/80 dark:border-slate-700/60 shadow-sm overflow-hidden">
        <div className="p-4 border-b border-gray-100 dark:border-slate-700/60">
          <div className="flex flex-wrap items-center gap-3">
            <div className="flex-1 min-w-[160px] sm:min-w-[220px] max-w-md relative group">
              <Search className="w-4 h-4 absolute left-3.5 top-1/2 -translate-y-1/2 text-gray-400 group-focus-within:text-primary-500 transition-colors" />
              <input
                type="text"
                placeholder={t('notifications.list.tplSearchPlaceholder')}
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="w-full pl-10 pr-10 py-2.5 bg-gray-50 dark:bg-slate-900/60 border border-transparent dark:border-slate-700/60 rounded-xl text-sm focus:outline-none focus:bg-white dark:focus:bg-slate-900 focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:text-slate-200 transition-all"
              />
              {search && (
                <button
                  onClick={() => setSearch('')}
                  className="absolute right-2.5 top-1/2 -translate-y-1/2 p-1 rounded-md text-gray-400 hover:text-gray-600 hover:bg-gray-100 dark:hover:bg-slate-800 transition"
                  title={t('notifications.common.clearSearch')}
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              )}
            </div>

            <div className="inline-flex p-1 bg-gray-100 dark:bg-slate-900/60 rounded-xl border border-gray-200 dark:border-slate-700/60">
              {activeFilterOptions.map((opt) => {
                const active = activeFilter === opt.value;
                return (
                  <button
                    key={String(opt.label)}
                    onClick={() => setActiveFilter(opt.value)}
                    className={`relative px-3.5 py-1.5 text-xs font-semibold rounded-lg transition-all ${
                      active
                        ? 'bg-white dark:bg-slate-700 text-primary-700 dark:text-primary-300 shadow-sm ring-1 ring-black/5 dark:ring-white/5'
                        : 'text-gray-600 dark:text-slate-400 hover:text-gray-900 dark:hover:text-slate-200'
                    }`}
                  >
                    {t(opt.label)}
                  </button>
                );
              })}
            </div>

            <select
              value={eventTypeFilter || ''}
              onChange={(e) => setEventTypeFilter(e.target.value || null)}
              className={selectCls + ' min-w-[120px] sm:min-w-[140px]'}
            >
              <option value="">{t('notifications.common.allEvents')}</option>
              {eventTypeOptions.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {t(opt.label)}
                </option>
              ))}
            </select>

            {(search || eventTypeFilter || activeFilter !== null) && (
              <button
                onClick={() => { setSearch(''); setEventTypeFilter(null); setActiveFilter(null); }}
                className="inline-flex items-center gap-1 px-3 py-2.5 text-xs font-medium text-gray-500 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200 hover:bg-gray-100 dark:hover:bg-slate-700/50 rounded-xl transition"
              >
                <FilterX className="w-3.5 h-3.5" />
                <span className="hidden sm:inline">{t('notifications.common.resetFilters')}</span>
              </button>
            )}

            <div className="flex-1 hidden sm:block" />

            <button
              onClick={fetch}
              disabled={loading}
              className="inline-flex items-center gap-1.5 px-3 sm:px-4 py-2.5 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 hover:bg-gray-50 dark:hover:bg-slate-700 text-gray-700 dark:text-slate-200 text-sm font-semibold rounded-xl shadow-sm transition disabled:opacity-60 disabled:cursor-not-allowed"
              title={t('notifications.list.refreshList')}
            >
              <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
              <span className="hidden sm:inline">{t('notifications.common.refresh')}</span>
            </button>

            <button
              onClick={openCreate}
              className="inline-flex items-center gap-1.5 px-4 sm:px-5 py-2.5 bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 text-white text-sm font-semibold rounded-xl shadow-md shadow-primary-500/20 hover:shadow-lg hover:shadow-primary-500/30 transition-all active:scale-[0.98]"
            >
              <Plus className="w-4 h-4" />
              <span className="hidden sm:inline">{t('notifications.list.newTemplate')}</span>
              <span className="sm:hidden">{t('notifications.common.new')}</span>
            </button>
          </div>
        </div>

        {/* 活跃筛选摘要 */}
        {(eventTypeFilter || activeFilter !== null) && (
          <div className="px-4 py-2.5 bg-gray-50 dark:bg-slate-900/40 border-b border-gray-100 dark:border-slate-700/60 flex flex-wrap items-center gap-2 text-xs">
            <span className="text-gray-500 dark:text-slate-400 font-medium">{t('notifications.common.currentFilters')}</span>
            {eventTypeFilter && (
              <span className="inline-flex items-center gap-1 px-2 py-0.5 bg-white dark:bg-slate-800 rounded-md border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-300">
                <Zap className="w-3 h-3 text-primary-500" />
                {getEventTypeLabel(t, eventTypeFilter)}
                <button onClick={() => setEventTypeFilter(null)} className="ml-1 text-gray-400 hover:text-red-500">
                  <X className="w-3 h-3" />
                </button>
              </span>
            )}
            {activeFilter !== null && (
              <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md border ${activeFilter ? 'bg-emerald-50 dark:bg-emerald-900/20 border-emerald-200 dark:border-emerald-900/40 text-emerald-700 dark:text-emerald-400' : 'bg-slate-50 dark:bg-slate-800 border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-300'}`}>
                {activeFilter ? <CheckCircle2 className="w-3 h-3" /> : <XCircle className="w-3 h-3" />}
                {t(activeFilter ? 'notifications.common.onlyEnabled' : 'notifications.common.onlyDisabled')}
                <button onClick={() => setActiveFilter(null)} className={`ml-1 ${activeFilter ? 'text-emerald-400 hover:text-emerald-600' : 'text-slate-400 hover:text-red-500'}`}>
                  <X className="w-3 h-3" />
                </button>
              </span>
            )}
          </div>
        )}

        {/* 表格 */}
        <div className="relative">
          {error && (
            <div className="mx-4 mt-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-400 px-4 py-3 rounded-xl text-sm flex items-center gap-2">
              <AlertCircle className="w-4 h-4 flex-shrink-0" />
              {t('notifications.common.loadFailed', { message: error })}
            </div>
          )}

          {loading ? (
            <LoadingState />
          ) : filteredList.length === 0 ? (
            <EmptyState
              icon={FileText}
              title={list.length === 0 ? t('notifications.list.tplEmpty') : t('notifications.list.tplEmptyFiltered')}
              subtitle={list.length === 0 ? t('notifications.list.tplEmptyHint') : t('notifications.list.emptyFilteredHint')}
              action={
                list.length === 0 ? (
                  <button
                    onClick={openCreate}
                    className="inline-flex items-center gap-1.5 px-4 py-2 bg-gradient-to-br from-primary-600 to-indigo-600 text-white text-sm font-semibold rounded-xl shadow-md transition-all"
                  >
                    <Plus className="w-4 h-4" />
                    {t('notifications.list.newTemplate')}
                  </button>
                ) : undefined
              }
            />
          ) : (
            <>
              {/* PC 端表格视图 */}
              <div className="hidden md:block overflow-x-auto">
                <table className="min-w-full">
                  <thead>
                    <tr className="bg-gray-50/60 dark:bg-slate-900/40 border-y border-gray-100 dark:border-slate-700/50">
                      <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">ID</th>
                      <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em] w-[24%]">{t('notifications.common.name')}</th>
                      <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.subject')}</th>
                      <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.eventType')}</th>
                      <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.status')}</th>
                      <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em] whitespace-nowrap">{t('notifications.common.updatedAt')}</th>
                      <th className="text-right px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.actions')}</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-50 dark:divide-slate-800/80">
                    {filteredList.map((tpl, idx) => {
                      const colors = getEventTypeColor(tpl.eventType);
                      const zebra = idx % 2 === 0 ? 'bg-white dark:bg-slate-800' : 'bg-gray-50/40 dark:bg-slate-800/40';
                      return (
                        <tr
                          key={tpl.id}
                          className={`${zebra} hover:bg-primary-50/40 dark:hover:bg-primary-900/10 transition-colors group`}
                        >
                          <td className="px-5 py-4 text-[12px] text-gray-500 dark:text-slate-400 font-mono">
                            <span className="inline-flex items-center justify-center w-7 h-7 rounded-lg bg-gray-50 dark:bg-slate-900/60 border border-gray-100 dark:border-slate-700 text-[11px] font-bold">
                              #{tpl.id}
                            </span>
                          </td>
                          <td className="px-5 py-4">
                            <div className="flex items-center gap-3 min-w-0">
                              <div className={`w-1 h-10 rounded-full ${colors.accent} flex-shrink-0 group-hover:w-1.5 transition-all`} />
                              <div className="min-w-0">
                                <div className="text-sm font-semibold text-gray-900 dark:text-slate-100 truncate flex items-center gap-2">
                                  {tpl.name}
                                  {tpl.isActive && (
                                    <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 shadow-[0_0_0_3px_rgba(16,185,129,0.15)]" title={t('format.status.active')} />
                                  )}
                                </div>
                                <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5 flex items-center gap-1.5">
                                  <Settings2 className="w-3 h-3 opacity-60" />
                                  <span>{t('notifications.list.tplMeta')}</span>
                                </div>
                              </div>
                            </div>
                          </td>
                          <td className="px-5 py-4">
                            <div className="text-sm text-gray-700 dark:text-slate-300 max-w-xs truncate" title={tpl.subject}>
                              {tpl.subject}
                            </div>
                          </td>
                          <td className="px-5 py-4">
                            <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-bold ring-1 ${colors.ring} ${colors.bg} ${colors.text}`}>
                              <Zap className="w-3 h-3" />
                              {getEventTypeLabel(t, tpl.eventType)}
                            </span>
                          </td>
                          <td className="px-5 py-4">
                            <span className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-[11px] font-bold transition-all ${
                              tpl.isActive
                                ? 'bg-emerald-50 dark:bg-emerald-900/25 text-emerald-700 dark:text-emerald-400 ring-1 ring-emerald-500/30 shadow-sm'
                                : 'bg-slate-100 dark:bg-slate-700/60 text-slate-500 dark:text-slate-400 ring-1 ring-slate-300/60 dark:ring-slate-600/60'
                            }`}>
                              {tpl.isActive ? (
                                <span className="relative flex w-2 h-2">
                                  <span className="absolute inline-flex w-full h-full rounded-full bg-emerald-400 opacity-60 animate-ping" />
                                  <span className="relative inline-flex w-2 h-2 rounded-full bg-emerald-500" />
                                </span>
                              ) : (
                                <XCircle className="w-3 h-3" />
                              )}
                              {tpl.isActive ? t('notifications.common.enabled') : t('notifications.common.disabled')}
                            </span>
                          </td>
                          <td className="px-5 py-4 text-[12px] text-gray-500 dark:text-slate-400 whitespace-nowrap tabular-nums">
                            {tpl.updatedAt ? formatDateTime(tpl.updatedAt) : '—'}
                          </td>
                          <td className="px-5 py-4">
                            <div className="flex items-center justify-end gap-0.5 p-1 -m-1 rounded-xl bg-white/0 group-hover:bg-gray-50 dark:group-hover:bg-slate-700/40 transition-colors">
                              <button
                                onClick={() => openEdit(tpl)}
                                className="p-2 text-gray-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/30 dark:hover:text-primary-400 rounded-lg transition"
                                title={t('notifications.list.editTemplate')}
                              >
                                <Edit2 className="w-4 h-4" />
                              </button>
                              <button
                                onClick={() => setDeleteTarget(tpl)}
                                className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30 dark:hover:text-red-400 rounded-lg transition"
                                title={t('notifications.list.deleteTemplate')}
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
                {filteredList.map((tpl) => {
                  const colors = getEventTypeColor(tpl.eventType);
                  return (
                    <div key={tpl.id} className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700/30 space-y-3">
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex items-center gap-2.5 min-w-0 flex-1">
                          <div className={`shrink-0 w-9 h-9 rounded-xl ${colors.bg} ${colors.ring} ring-1 flex items-center justify-center`}>
                            <FileText className={`w-4 h-4 ${colors.text}`} />
                          </div>
                          <div className="min-w-0">
                            <div className="text-sm font-bold text-gray-900 dark:text-slate-100 truncate flex items-center gap-1.5">
                              {tpl.name}
                              {tpl.isActive && (
                                <span className="w-1.5 h-1.5 rounded-full bg-emerald-500" title={t('format.status.active')} />
                              )}
                            </div>
                            <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5">ID · #{tpl.id}</div>
                          </div>
                        </div>
                        <span className={`inline-flex items-center gap-1 px-2 py-1 rounded-full text-[11px] font-bold flex-shrink-0 ${
                          tpl.isActive
                            ? 'bg-emerald-50 dark:bg-emerald-900/25 text-emerald-700 dark:text-emerald-400 ring-1 ring-emerald-500/30'
                            : 'bg-slate-100 dark:bg-slate-700/60 text-slate-500 dark:text-slate-400 ring-1 ring-slate-300/60 dark:ring-slate-600/60'
                        }`}>
                          {tpl.isActive ? t('notifications.common.enabled') : t('notifications.common.disabled')}
                        </span>
                      </div>

                      <div className="flex flex-wrap items-center gap-1.5">
                        <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-xl text-[11px] font-bold ring-1 ${colors.bg} ${colors.text} ${colors.ring}`}>
                          <span className={`w-1.5 h-1.5 rounded-full ${colors.accent}`} />
                          {getEventTypeLabel(t, tpl.eventType)}
                        </span>
                      </div>

                      <div className="text-xs text-gray-600 dark:text-slate-400 line-clamp-2">
                        {tpl.subject || <span className="text-gray-400 dark:text-slate-500 italic">{t('notifications.common.unsetSubject')}</span>}
                      </div>

                      <div className="flex items-center justify-between pt-1">
                        <div className="text-[11px] text-gray-400 dark:text-slate-500 font-mono">
                          {tpl.updatedAt ? formatDateTime(tpl.updatedAt) : '—'}
                        </div>
                        <div className="flex items-center gap-1">
                          <button
                            onClick={() => openEdit(tpl)}
                            className="p-2 text-gray-500 dark:text-slate-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition"
                            title={t('notifications.list.editTemplate')}
                          >
                            <Edit2 className="w-4 h-4" />
                          </button>
                          <button
                            onClick={() => setDeleteTarget(tpl)}
                            className="p-2 text-gray-500 dark:text-slate-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-xl transition"
                            title={t('notifications.list.deleteTemplate')}
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
        </div>
      </div>

      {/* 编辑弹窗 */}
      <Modal
        open={modalOpen}
        title={editingId ? t('notifications.list.tplModalEditTitle') : t('notifications.list.tplModalNewTitle')}
        onClose={() => setModalOpen(false)}
        size="xl"
        footer={
          <>
            <button
              onClick={() => setModalOpen(false)}
              disabled={submitting}
              className="px-5 py-2.5 text-sm text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition font-semibold"
            >
              {t('common.cancel')}
            </button>
            <button
              type="submit"
              form="template-form"
              disabled={submitting}
              className="px-5 py-2.5 text-sm bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 disabled:opacity-50 text-white rounded-xl transition font-semibold shadow-md shadow-primary-500/20"
            >
              {submitting ? (
                <span className="inline-flex items-center gap-1.5">
                  <Activity className="w-4 h-4 animate-pulse" />
                  {t('notifications.common.saving')}
                </span>
              ) : (
                <span className="inline-flex items-center gap-1.5">
                  <CheckCircle2 className="w-4 h-4" />
                  {t('common.save')}
                </span>
              )}
            </button>
          </>
        }
      >
        <form id="template-form" onSubmit={handleSubmit} className="space-y-5">
          {/* 弹窗头部装饰 */}
          <div className="-m-6 mb-6 p-5 bg-gradient-to-r from-primary-50 via-indigo-50 to-violet-50 dark:from-primary-900/30 dark:via-indigo-900/30 dark:to-violet-900/30 border-b border-primary-100 dark:border-slate-700/60">
            <div className="flex items-center gap-3">
              <div className="p-2.5 bg-white dark:bg-slate-800 rounded-xl ring-1 ring-primary-500/20 shadow-sm">
                <FileText className="w-5 h-5 text-primary-600 dark:text-primary-400" />
              </div>
              <div>
                <h3 className="font-bold text-gray-900 dark:text-slate-100 text-lg">
                  {editingId ? t('notifications.list.tplFormEditHeading') : t('notifications.list.tplFormNewHeading')}
                </h3>
                <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
                  {t('notifications.list.tplFormSubtitle')}
                </p>
              </div>
            </div>
          </div>

          {formError && (
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-400 px-4 py-3 rounded-xl text-sm flex items-center gap-2">
              <AlertCircle className="w-4 h-4 flex-shrink-0" />
              {formError}
            </div>
          )}

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-5">
            <div>
              <label className={labelCls}>
                {t('notifications.list.tplNameLabel')} <span className="text-red-500">*</span>
              </label>
              <input
                type="text"
                required
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                className={inputCls}
                placeholder={t('notifications.list.tplNamePlaceholder')}
              />
            </div>
            <div>
              <label className={labelCls}>
                {t('notifications.common.eventType')} <span className="text-red-500">*</span>
              </label>
              <select
                value={form.eventType}
                onChange={(e) => setForm({ ...form, eventType: e.target.value })}
                className={selectCls}
              >
                {eventTypeOptions.map((o) => (
                  <option key={o.value} value={o.value}>{t(o.label)}</option>
                ))}
              </select>
            </div>
          </div>

          <div>
            <label className={labelCls}>
              {t('notifications.list.emailSubjectLabel')} <span className="text-red-500">*</span>
            </label>
            <input
              type="text"
              required
              value={form.subject}
              onChange={(e) => setForm({ ...form, subject: e.target.value })}
              className={inputCls + ' font-mono'}
              placeholder={t('notifications.list.emailSubjectPlaceholder')}
            />
          </div>

          <div>
            <label className={labelCls}>
              {t('notifications.list.emailBodyLabel')} <span className="text-red-500">*</span>
            </label>
            <textarea
              required
              value={form.body}
              onChange={(e) => setForm({ ...form, body: e.target.value })}
              rows={8}
              className={inputCls + ' font-mono leading-relaxed'}
              placeholder={t('notifications.list.emailBodyPlaceholder')}
            />
            <div className="mt-3">
              <div className="text-xs text-gray-500 dark:text-slate-400 mb-2 flex items-center gap-1.5">
                <Sparkles className="w-3.5 h-3.5" />
                {t('notifications.list.quickVariablesInsert')}
              </div>
              <div className="flex flex-wrap gap-1.5">
                {commonVariables.map((v) => (
                  <code
                    key={v}
                    className="text-xs px-2 py-1 bg-gray-50 dark:bg-slate-900/60 border border-gray-100 dark:border-slate-700 text-gray-700 dark:text-slate-300 rounded-lg cursor-pointer hover:bg-primary-50 hover:text-primary-700 hover:border-primary-200 dark:hover:bg-primary-900/30 dark:hover:text-primary-400 dark:hover:border-primary-900/50 font-semibold transition-all"
                    onClick={() => setForm({ ...form, body: form.body + ' ' + v })}
                    title={t('notifications.list.clickToInsertBody')}
                  >
                    {v}
                  </code>
                ))}
              </div>
            </div>
          </div>

          <div className="flex items-center gap-3 p-4 bg-gradient-to-r from-gray-50 to-slate-50 dark:from-slate-900/40 dark:to-slate-900/60 rounded-xl border border-gray-100 dark:border-slate-700/60">
            <div className="relative inline-flex items-center cursor-pointer">
              <input
                type="checkbox"
                id="template-active"
                checked={form.isActive}
                onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
                className="sr-only peer"
              />
              <div className="w-11 h-6 bg-gray-200 dark:bg-slate-700 peer-focus:outline-none peer-focus:ring-4 peer-focus:ring-primary-500/20 rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-0.5 after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-gradient-to-r peer-checked:from-primary-600 peer-checked:to-indigo-600 shadow-sm" />
            </div>
            <label htmlFor="template-active" className="text-sm text-gray-700 dark:text-slate-300 font-semibold cursor-pointer">
              {t('notifications.list.tplActiveLabel')}
            </label>
          </div>
        </form>
      </Modal>

      <ConfirmDialog
        open={!!deleteTarget}
        title={t('notifications.list.deleteTemplate')}
        message={t('notifications.list.confirmDeleteTemplate', { name: deleteTarget?.name ?? '' })}
        confirmText={t('common.delete')}
        variant="danger"
        loading={deleting}
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}

// ====== 规则 Tab ======
function RulesTab() {
  const { t, language } = useI18n();
  const [list, setList] = useState<NotificationRuleResponse[]>([]);
  const [templates, setTemplates] = useState<NotificationTemplateResponse[]>([]);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [users, setUsers] = useState<UserResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<CreateNotificationRuleRequest>({
    name: '',
    eventType: eventTypeOptions[0].value,
    mailboxId: null,
    userId: null,
    templateId: null,
    isActive: true,
  });
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState('');

  const [deleteTarget, setDeleteTarget] = useState<NotificationRuleResponse | null>(null);
  const [deleting, setDeleting] = useState(false);

  const [search, setSearch] = useState('');
  const [eventTypeFilter, setEventTypeFilter] = useState<string | null>(null);
  const [activeFilter, setActiveFilter] = useState<boolean | null>(null);

  const fetch = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const [rulesResp, tplResp, mbResp, userResp] = await Promise.all([
        notificationApi.listAllRules(),
        notificationApi.listAllTemplates(),
        mailboxApi.listAll(),
        userApi.list(),
      ]);
      setList(Array.isArray(rulesResp) ? rulesResp : []);
      setTemplates(Array.isArray(tplResp) ? tplResp : []);
      setMailboxes(Array.isArray(mbResp) ? mbResp : []);
      setUsers(Array.isArray(userResp?.data) ? userResp.data : []);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('notifications.list.loadFailed'));
    } finally {
      setLoading(false);
    }
    // `t` 只用于错误兜底文案。把它列进依赖数组会让 I18nProvider 的 DB 覆盖层落地后
    // 本回调重建、`useEffect(..., [fetch])` 随即**重复拉一次列表**（首屏多一次请求 +
    // 二次 loading 闪烁）。故此处刻意省略该依赖，保持"仅挂载时拉一次"的原行为。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    fetch();
  }, [fetch]);

  const filteredList = list.filter((r) => {
    if (search && !r.name.toLowerCase().includes(search.toLowerCase())) return false;
    if (eventTypeFilter && r.eventType !== eventTypeFilter) return false;
    if (activeFilter !== null && r.isActive !== activeFilter) return false;
    return true;
  });

  const stats = {
    total: list.length,
    active: list.filter((r) => r.isActive).length,
  };

  const openCreate = () => {
    setEditingId(null);
    setForm({
      name: '',
      eventType: eventTypeOptions[0].value,
      mailboxId: null,
      userId: null,
      templateId: templates.length > 0 ? templates[0].id : null,
      isActive: true,
    });
    setFormError('');
    setModalOpen(true);
  };

  const openEdit = (r: NotificationRuleResponse) => {
    setEditingId(r.id);
    setForm({
      name: r.name,
      eventType: r.eventType,
      mailboxId: r.mailboxId,
      userId: r.userId,
      templateId: r.templateId,
      isActive: r.isActive,
    });
    setFormError('');
    setModalOpen(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!form.name.trim()) {
      setFormError(t('notifications.list.ruleNameRequired'));
      return;
    }
    setSubmitting(true);
    setFormError('');
    try {
      const payload: CreateNotificationRuleRequest = {
        ...form,
        name: form.name.trim(),
      };
      if (editingId) {
        await notificationApi.updateRule(editingId, payload);
      } else {
        await notificationApi.createRule(payload);
      }
      setModalOpen(false);
      await fetch();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : t('notifications.list.opFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await notificationApi.removeRule(deleteTarget.id);
      setDeleteTarget(null);
      await fetch();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : t('notifications.common.deleteFailed'));
    } finally {
      setDeleting(false);
    }
  };

  const getMailboxName = (id: number | null) => {
    if (id === null) return t('notifications.common.allMailboxes');
    return mailboxes.find((m) => m.id === id)?.name ?? `#${id}`;
  };

  const getUserName = (id: number | null) => {
    if (id === null) return t('notifications.list.allUsers');
    return users.find((u) => u.id === id)?.fullName ?? `#${id}`;
  };

  const getTemplateName = (id: number | null) => {
    if (id === null) return null;
    return templates.find((tpl) => tpl.id === id)?.name ?? null;
  };

  return (
    <div className="space-y-5">
      {/* 顶部统计卡片 */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-5">
        {ruleStatCards(stats).map((s, idx) => (
          <div
            key={s.label}
            className="group relative bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/80 dark:border-slate-700/60 p-6 overflow-hidden shadow-sm hover:shadow-lg hover:-translate-y-0.5 transition-all duration-300"
          >
            <div className={`absolute left-0 top-0 bottom-0 w-1.5 bg-gradient-to-b ${s.palette.from} ${s.palette.to}`} />
            <div className={`absolute -right-8 -top-8 w-32 h-32 rounded-full ${s.palette.bgSoft} opacity-60 blur-2xl pointer-events-none`} />
            <div className="relative flex items-start justify-between">
              <div className="flex-1">
                <div className="flex items-center gap-2">
                  <span className="text-xs font-medium text-gray-500 dark:text-slate-400 tracking-wider uppercase">
                    {t(s.label)}
                  </span>
                </div>
                <div className="mt-3 flex items-baseline gap-2">
                  <span className="text-4xl font-extrabold text-gray-900 dark:text-slate-50 tabular-nums tracking-tight">
                    {s.value.toLocaleString(toIntlLocale(language))}
                  </span>
                  <span className="text-sm text-gray-400 dark:text-slate-500">{t('notifications.list.statUnit', { count: s.value })}</span>
                </div>
                <div className="mt-2 text-xs text-gray-500 dark:text-slate-400 flex items-center gap-1">
                  <Info className="w-3 h-3 opacity-60" />
                  {t(s.sub)}
                </div>
              </div>
              <div className={`relative p-3 rounded-xl ${s.palette.bgSoft} ring-1 ${s.palette.ringSoft} group-hover:scale-110 transition-transform`}>
                <s.icon className={`w-6 h-6 ${s.palette.textLight}`} />
              </div>
            </div>
            {idx === 0 && stats.total > 0 && (
              <div className="relative mt-5">
                <div className="flex items-center justify-between text-[11px] font-medium text-gray-500 dark:text-slate-400 mb-1.5">
                  <span>{t('notifications.list.enableRate')}</span>
                  <span className="tabular-nums">{Math.round((stats.active / stats.total) * 100)}%</span>
                </div>
                <div className="h-2 bg-gray-100 dark:bg-slate-700/50 rounded-full overflow-hidden">
                  <div
                    className="h-full bg-gradient-to-r from-emerald-500 to-teal-500 rounded-full transition-all duration-700"
                    style={{ width: `${(stats.active / stats.total) * 100}%` }}
                  />
                </div>
              </div>
            )}
          </div>
        ))}
      </div>

      {/* 规则列表容器 */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/80 dark:border-slate-700/60 shadow-sm overflow-hidden">
        <div className="p-4 border-b border-gray-100 dark:border-slate-700/60">
          <div className="flex flex-wrap items-center gap-3">
            <div className="flex-1 min-w-[160px] sm:min-w-[220px] max-w-md relative group">
              <Search className="w-4 h-4 absolute left-3.5 top-1/2 -translate-y-1/2 text-gray-400 group-focus-within:text-primary-500 transition-colors" />
              <input
                type="text"
                placeholder={t('notifications.list.ruleSearchPlaceholder')}
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="w-full pl-10 pr-10 py-2.5 bg-gray-50 dark:bg-slate-900/60 border border-transparent dark:border-slate-700/60 rounded-xl text-sm focus:outline-none focus:bg-white dark:focus:bg-slate-900 focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:text-slate-200 transition-all"
              />
              {search && (
                <button
                  onClick={() => setSearch('')}
                  className="absolute right-2.5 top-1/2 -translate-y-1/2 p-1 rounded-md text-gray-400 hover:text-gray-600 hover:bg-gray-100 dark:hover:bg-slate-800 transition"
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              )}
            </div>

            <div className="inline-flex p-1 bg-gray-100 dark:bg-slate-900/60 rounded-xl border border-gray-200 dark:border-slate-700/60">
              {activeFilterOptions.map((opt) => {
                const active = activeFilter === opt.value;
                return (
                  <button
                    key={String(opt.label)}
                    onClick={() => setActiveFilter(opt.value)}
                    className={`relative px-3.5 py-1.5 text-xs font-semibold rounded-lg transition-all ${
                      active
                        ? 'bg-white dark:bg-slate-700 text-primary-700 dark:text-primary-300 shadow-sm ring-1 ring-black/5 dark:ring-white/5'
                        : 'text-gray-600 dark:text-slate-400 hover:text-gray-900 dark:hover:text-slate-200'
                    }`}
                  >
                    {t(opt.label)}
                  </button>
                );
              })}
            </div>

            <select
              value={eventTypeFilter || ''}
              onChange={(e) => setEventTypeFilter(e.target.value || null)}
              className={selectCls + ' min-w-[120px] sm:min-w-[140px]'}
            >
              <option value="">{t('notifications.common.allEvents')}</option>
              {eventTypeOptions.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {t(opt.label)}
                </option>
              ))}
            </select>

            {(search || eventTypeFilter || activeFilter !== null) && (
              <button
                onClick={() => { setSearch(''); setEventTypeFilter(null); setActiveFilter(null); }}
                className="inline-flex items-center gap-1 px-3 py-2.5 text-xs font-medium text-gray-500 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200 hover:bg-gray-100 dark:hover:bg-slate-700/50 rounded-xl transition"
              >
                <FilterX className="w-3.5 h-3.5" />
                <span className="hidden sm:inline">{t('notifications.common.resetFilters')}</span>
              </button>
            )}

            <div className="flex-1 hidden sm:block" />

            <button
              onClick={fetch}
              disabled={loading}
              className="inline-flex items-center gap-1.5 px-3 sm:px-4 py-2.5 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 hover:bg-gray-50 dark:hover:bg-slate-700 text-gray-700 dark:text-slate-200 text-sm font-semibold rounded-xl shadow-sm transition disabled:opacity-60 disabled:cursor-not-allowed"
            >
              <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
              <span className="hidden sm:inline">{t('notifications.common.refresh')}</span>
            </button>

            <button
              onClick={openCreate}
              className="inline-flex items-center gap-1.5 px-4 sm:px-5 py-2.5 bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 text-white text-sm font-semibold rounded-xl shadow-md shadow-primary-500/20 hover:shadow-lg hover:shadow-primary-500/30 transition-all active:scale-[0.98]"
            >
              <Plus className="w-4 h-4" />
              <span className="hidden sm:inline">{t('notifications.list.newRule')}</span>
              <span className="sm:hidden">{t('notifications.common.new')}</span>
            </button>
          </div>
        </div>

        {(eventTypeFilter || activeFilter !== null) && (
          <div className="px-4 py-2.5 bg-gray-50 dark:bg-slate-900/40 border-b border-gray-100 dark:border-slate-700/60 flex flex-wrap items-center gap-2 text-xs">
            <span className="text-gray-500 dark:text-slate-400 font-medium">{t('notifications.common.currentFilters')}</span>
            {eventTypeFilter && (
              <span className="inline-flex items-center gap-1 px-2 py-0.5 bg-white dark:bg-slate-800 rounded-md border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-300">
                <Zap className="w-3 h-3 text-primary-500" />
                {getEventTypeLabel(t, eventTypeFilter)}
                <button onClick={() => setEventTypeFilter(null)} className="ml-1 text-gray-400 hover:text-red-500">
                  <X className="w-3 h-3" />
                </button>
              </span>
            )}
            {activeFilter !== null && (
              <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md border ${activeFilter ? 'bg-emerald-50 dark:bg-emerald-900/20 border-emerald-200 dark:border-emerald-900/40 text-emerald-700 dark:text-emerald-400' : 'bg-slate-50 dark:bg-slate-800 border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-300'}`}>
                {activeFilter ? <CheckCircle2 className="w-3 h-3" /> : <XCircle className="w-3 h-3" />}
                {t(activeFilter ? 'notifications.common.onlyEnabled' : 'notifications.common.onlyDisabled')}
                <button onClick={() => setActiveFilter(null)} className={`ml-1 ${activeFilter ? 'text-emerald-400 hover:text-emerald-600' : 'text-slate-400 hover:text-red-500'}`}>
                  <X className="w-3 h-3" />
                </button>
              </span>
            )}
          </div>
        )}

        <div className="relative">
          {error && (
            <div className="mx-4 mt-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-400 px-4 py-3 rounded-xl text-sm flex items-center gap-2">
              <AlertCircle className="w-4 h-4 flex-shrink-0" />
              {t('notifications.common.loadFailed', { message: error })}
            </div>
          )}

          {loading ? (
            <LoadingState />
          ) : filteredList.length === 0 ? (
            <EmptyState
              icon={Bell}
              title={list.length === 0 ? t('notifications.list.ruleEmpty') : t('notifications.list.ruleEmptyFiltered')}
              subtitle={list.length === 0 ? t('notifications.list.ruleEmptyHint') : t('notifications.list.emptyFilteredHint')}
              action={
                list.length === 0 ? (
                  <button
                    onClick={openCreate}
                    className="inline-flex items-center gap-1.5 px-4 py-2 bg-gradient-to-br from-primary-600 to-indigo-600 text-white text-sm font-semibold rounded-xl shadow-md transition-all"
                  >
                    <Plus className="w-4 h-4" />
                    {t('notifications.list.newRule')}
                  </button>
                ) : undefined
              }
            />
          ) : (
            <>
              {/* PC 端表格视图 */}
              <div className="hidden md:block overflow-x-auto">
                <table className="min-w-full">
                  <thead>
                    <tr className="bg-gray-50/60 dark:bg-slate-900/40 border-y border-gray-100 dark:border-slate-700/50">
                      <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">ID</th>
                      <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em] w-[22%]">{t('notifications.list.ruleNameLabel')}</th>
                      <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.eventType')}</th>
                      <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.list.applicableMailbox')}</th>
                      <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.list.notifyUser')}</th>
                      <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.list.useTemplate')}</th>
                      <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.status')}</th>
                      <th className="text-right px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.actions')}</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-50 dark:divide-slate-800/80">
                    {filteredList.map((r, idx) => {
                      const colors = getEventTypeColor(r.eventType);
                      const zebra = idx % 2 === 0 ? 'bg-white dark:bg-slate-800' : 'bg-gray-50/40 dark:bg-slate-800/40';
                      return (
                        <tr
                          key={r.id}
                          className={`${zebra} hover:bg-primary-50/40 dark:hover:bg-primary-900/10 transition-colors group`}
                        >
                          <td className="px-5 py-4">
                            <span className="inline-flex items-center justify-center w-7 h-7 rounded-lg bg-gray-50 dark:bg-slate-900/60 border border-gray-100 dark:border-slate-700 text-[11px] font-bold text-gray-500 dark:text-slate-400 font-mono">
                              #{r.id}
                            </span>
                          </td>
                          <td className="px-5 py-4">
                            <div className="flex items-center gap-3 min-w-0">
                              <div className={`w-1 h-10 rounded-full ${colors.accent} flex-shrink-0 group-hover:w-1.5 transition-all`} />
                              <div className="min-w-0">
                                <div className="text-sm font-semibold text-gray-900 dark:text-slate-100 truncate flex items-center gap-2">
                                  {r.name}
                                  {r.isActive && (
                                    <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 shadow-[0_0_0_3px_rgba(16,185,129,0.15)]" title={t('format.status.active')} />
                                  )}
                                </div>
                                <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5 flex items-center gap-1.5">
                                  <Settings2 className="w-3 h-3 opacity-60" />
                                  {t('notifications.list.ruleMeta')}
                                </div>
                              </div>
                            </div>
                          </td>
                          <td className="px-5 py-4">
                            <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-bold ring-1 ${colors.ring} ${colors.bg} ${colors.text}`}>
                              <Zap className="w-3 h-3" />
                              {getEventTypeLabel(t, r.eventType)}
                            </span>
                          </td>
                          <td className="px-5 py-4">
                            <div className="inline-flex items-center gap-1 text-[12px] text-gray-600 dark:text-slate-300 px-2.5 py-1 rounded-md bg-gray-50 dark:bg-slate-900/50 border border-gray-100 dark:border-slate-700/50">
                              <Mailbox className="w-3 h-3 text-gray-400" />
                              <span className="truncate max-w-[140px]">{getMailboxName(r.mailboxId)}</span>
                            </div>
                          </td>
                          <td className="px-5 py-4">
                            <div className="inline-flex items-center gap-1 text-[12px] text-gray-600 dark:text-slate-300 px-2.5 py-1 rounded-md bg-gray-50 dark:bg-slate-900/50 border border-gray-100 dark:border-slate-700/50">
                              <User className="w-3 h-3 text-gray-400" />
                              <span className="truncate max-w-[140px]">{getUserName(r.userId)}</span>
                            </div>
                          </td>
                          <td className="px-5 py-4">
                            {getTemplateName(r.templateId) ? (
                              <div className="inline-flex items-center gap-1.5 px-2 py-1 rounded-md bg-indigo-50 dark:bg-indigo-900/20 text-indigo-700 dark:text-indigo-400 border border-indigo-100 dark:border-indigo-900/40 text-[12px] font-medium max-w-[200px]">
                                <FileText className="w-3 h-3 flex-shrink-0" />
                                <span className="truncate">{getTemplateName(r.templateId)}</span>
                              </div>
                            ) : (
                              <div className="inline-flex items-center gap-1 text-[12px] text-gray-400 dark:text-slate-500">
                                <AlertTriangle className="w-3 h-3 opacity-60" />
                                {t('notifications.common.unbound')}
                              </div>
                            )}
                          </td>
                          <td className="px-5 py-4">
                            <span className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-[11px] font-bold transition-all ${
                              r.isActive
                                ? 'bg-emerald-50 dark:bg-emerald-900/25 text-emerald-700 dark:text-emerald-400 ring-1 ring-emerald-500/30 shadow-sm'
                                : 'bg-slate-100 dark:bg-slate-700/60 text-slate-500 dark:text-slate-400 ring-1 ring-slate-300/60 dark:ring-slate-600/60'
                            }`}>
                              {r.isActive ? (
                                <span className="relative flex w-2 h-2">
                                  <span className="absolute inline-flex w-full h-full rounded-full bg-emerald-400 opacity-60 animate-ping" />
                                  <span className="relative inline-flex w-2 h-2 rounded-full bg-emerald-500" />
                                </span>
                              ) : (
                                <XCircle className="w-3 h-3" />
                              )}
                              {r.isActive ? t('notifications.common.enabled') : t('notifications.common.disabled')}
                            </span>
                          </td>
                          <td className="px-5 py-4">
                            <div className="flex items-center justify-end gap-0.5 p-1 -m-1 rounded-xl bg-white/0 group-hover:bg-gray-50 dark:group-hover:bg-slate-700/40 transition-colors">
                              <button
                                onClick={() => openEdit(r)}
                                className="p-2 text-gray-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/30 dark:hover:text-primary-400 rounded-lg transition"
                                title={t('notifications.list.editRule')}
                              >
                                <Edit2 className="w-4 h-4" />
                              </button>
                              <button
                                onClick={() => setDeleteTarget(r)}
                                className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30 dark:hover:text-red-400 rounded-lg transition"
                                title={t('notifications.list.deleteRule')}
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
                {filteredList.map((r) => {
                  const colors = getEventTypeColor(r.eventType);
                  const tplName = getTemplateName(r.templateId);
                  return (
                    <div key={r.id} className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700/30 space-y-3">
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex items-center gap-2.5 min-w-0 flex-1">
                          <div className={`shrink-0 w-9 h-9 rounded-xl ${colors.bg} ${colors.ring} ring-1 flex items-center justify-center`}>
                            <Bell className={`w-4 h-4 ${colors.text}`} />
                          </div>
                          <div className="min-w-0">
                            <div className="text-sm font-bold text-gray-900 dark:text-slate-100 truncate flex items-center gap-1.5">
                              {r.name}
                              {r.isActive && (
                                <span className="w-1.5 h-1.5 rounded-full bg-emerald-500" title={t('format.status.active')} />
                              )}
                            </div>
                            <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5">ID · #{r.id}</div>
                          </div>
                        </div>
                        <span className={`inline-flex items-center gap-1 px-2 py-1 rounded-full text-[11px] font-bold flex-shrink-0 ${
                          r.isActive
                            ? 'bg-emerald-50 dark:bg-emerald-900/25 text-emerald-700 dark:text-emerald-400 ring-1 ring-emerald-500/30'
                            : 'bg-slate-100 dark:bg-slate-700/60 text-slate-500 dark:text-slate-400 ring-1 ring-slate-300/60 dark:ring-slate-600/60'
                        }`}>
                          {r.isActive ? t('notifications.common.enabled') : t('notifications.common.disabled')}
                        </span>
                      </div>

                      <div className="flex flex-wrap items-center gap-1.5">
                        <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-xl text-[11px] font-bold ring-1 ${colors.bg} ${colors.text} ${colors.ring}`}>
                          <span className={`w-1.5 h-1.5 rounded-full ${colors.accent}`} />
                          {getEventTypeLabel(t, r.eventType)}
                        </span>
                      </div>

                      <div className="grid grid-cols-2 gap-2 text-xs">
                        <div className="flex items-center gap-1.5 text-gray-600 dark:text-slate-300 px-2 py-1.5 rounded-md bg-gray-50 dark:bg-slate-900/50 border border-gray-100 dark:border-slate-700/50">
                          <Mailbox className="w-3 h-3 text-gray-400 flex-shrink-0" />
                          <span className="truncate">{getMailboxName(r.mailboxId)}</span>
                        </div>
                        <div className="flex items-center gap-1.5 text-gray-600 dark:text-slate-300 px-2 py-1.5 rounded-md bg-gray-50 dark:bg-slate-900/50 border border-gray-100 dark:border-slate-700/50">
                          <User className="w-3 h-3 text-gray-400 flex-shrink-0" />
                          <span className="truncate">{getUserName(r.userId)}</span>
                        </div>
                      </div>

                      <div className="text-xs">
                        {tplName ? (
                          <div className="inline-flex items-center gap-1.5 px-2 py-1 rounded-md bg-indigo-50 dark:bg-indigo-900/20 text-indigo-700 dark:text-indigo-400 border border-indigo-100 dark:border-indigo-900/40 font-medium">
                            <FileText className="w-3 h-3 flex-shrink-0" />
                            <span className="truncate">{tplName}</span>
                          </div>
                        ) : (
                          <div className="inline-flex items-center gap-1 text-gray-400 dark:text-slate-500">
                            <AlertTriangle className="w-3 h-3 opacity-60" />
                            {t('notifications.common.unboundTemplate')}
                          </div>
                        )}
                      </div>

                      <div className="flex items-center justify-end pt-1">
                        <div className="flex items-center gap-1">
                          <button
                            onClick={() => openEdit(r)}
                            className="p-2 text-gray-500 dark:text-slate-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition"
                            title={t('notifications.list.editRule')}
                          >
                            <Edit2 className="w-4 h-4" />
                          </button>
                          <button
                            onClick={() => setDeleteTarget(r)}
                            className="p-2 text-gray-500 dark:text-slate-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-xl transition"
                            title={t('notifications.list.deleteRule')}
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
        </div>
      </div>

      {/* 编辑弹窗 */}
      <Modal
        open={modalOpen}
        title={editingId ? t('notifications.list.ruleModalEditTitle') : t('notifications.list.ruleModalNewTitle')}
        onClose={() => setModalOpen(false)}
        size="xl"
        footer={
          <>
            <button
              onClick={() => setModalOpen(false)}
              disabled={submitting}
              className="px-5 py-2.5 text-sm text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition font-semibold"
            >
              {t('common.cancel')}
            </button>
            <button
              type="submit"
              form="rule-form"
              disabled={submitting}
              className="px-5 py-2.5 text-sm bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 disabled:opacity-50 text-white rounded-xl transition font-semibold shadow-md shadow-primary-500/20"
            >
              {submitting ? (
                <span className="inline-flex items-center gap-1.5">
                  <Activity className="w-4 h-4 animate-pulse" />
                  {t('notifications.common.saving')}
                </span>
              ) : (
                <span className="inline-flex items-center gap-1.5">
                  <CheckCircle2 className="w-4 h-4" />
                  {t('common.save')}
                </span>
              )}
            </button>
          </>
        }
      >
        <form id="rule-form" onSubmit={handleSubmit} className="space-y-5">
          <div className="-m-6 mb-6 p-5 bg-gradient-to-r from-primary-50 via-indigo-50 to-violet-50 dark:from-primary-900/30 dark:via-indigo-900/30 dark:to-violet-900/30 border-b border-primary-100 dark:border-slate-700/60">
            <div className="flex items-center gap-3">
              <div className="p-2.5 bg-white dark:bg-slate-800 rounded-xl ring-1 ring-primary-500/20 shadow-sm">
                <Bell className="w-5 h-5 text-primary-600 dark:text-primary-400" />
              </div>
              <div>
                <h3 className="font-bold text-gray-900 dark:text-slate-100 text-lg">
                  {editingId ? t('notifications.list.ruleFormEditHeading') : t('notifications.list.ruleFormNewHeading')}
                </h3>
                <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
                  {t('notifications.list.ruleFormSubtitle')}
                </p>
              </div>
            </div>
          </div>

          {formError && (
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-400 px-4 py-3 rounded-xl text-sm flex items-center gap-2">
              <AlertCircle className="w-4 h-4 flex-shrink-0" />
              {formError}
            </div>
          )}

          <div>
            <label className={labelCls}>
              {t('notifications.list.ruleNameLabel')} <span className="text-red-500">*</span>
            </label>
            <input
              type="text"
              required
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              className={inputCls}
              placeholder={t('notifications.list.ruleNamePlaceholder')}
            />
          </div>

          <div>
            <label className={labelCls}>
              {t('notifications.common.eventType')} <span className="text-red-500">*</span>
            </label>
            <select
              value={form.eventType}
              onChange={(e) => setForm({ ...form, eventType: e.target.value })}
              className={selectCls}
            >
              {eventTypeOptions.map((o) => (
                <option key={o.value} value={o.value}>{t(o.label)}</option>
              ))}
            </select>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-5">
            <div>
              <label className={labelCls}>{t('notifications.list.applicableMailbox')}</label>
              <select
                value={form.mailboxId ?? ''}
                onChange={(e) => setForm({ ...form, mailboxId: e.target.value === '' ? null : Number(e.target.value) })}
                className={selectCls}
              >
                <option value="">{t('notifications.common.allMailboxes')}</option>
                {mailboxes.map((m) => (
                  <option key={m.id} value={m.id}>{m.name}</option>
                ))}
              </select>
              <p className="text-[11px] text-gray-400 dark:text-slate-500 mt-1.5">
                {t('notifications.list.mailboxHint')}
              </p>
            </div>
            <div>
              <label className={labelCls}>{t('notifications.list.notifyUser')}</label>
              <select
                value={form.userId ?? ''}
                onChange={(e) => setForm({ ...form, userId: e.target.value === '' ? null : Number(e.target.value) })}
                className={selectCls}
              >
                <option value="">{t('notifications.list.allUsers')}</option>
                {users.map((u) => (
                  <option key={u.id} value={u.id}>{u.fullName}</option>
                ))}
              </select>
              <p className="text-[11px] text-gray-400 dark:text-slate-500 mt-1.5">
                {t('notifications.list.notifyUserHint')}
              </p>
            </div>
          </div>

          <div>
            <label className={labelCls}>{t('notifications.list.useTemplate')}</label>
            <select
              value={form.templateId ?? ''}
              onChange={(e) => setForm({ ...form, templateId: e.target.value === '' ? null : Number(e.target.value) })}
              className={selectCls}
            >
              <option value="">{t('notifications.list.noTemplate')}</option>
              {templates.map((tpl) => (
                <option key={tpl.id} value={tpl.id}>{tpl.name}</option>
              ))}
            </select>
          </div>

          <div className="flex items-center gap-3 p-4 bg-gradient-to-r from-gray-50 to-slate-50 dark:from-slate-900/40 dark:to-slate-900/60 rounded-xl border border-gray-100 dark:border-slate-700/60">
            <div className="relative inline-flex items-center cursor-pointer">
              <input
                type="checkbox"
                id="rule-active"
                checked={form.isActive}
                onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
                className="sr-only peer"
              />
              <div className="w-11 h-6 bg-gray-200 dark:bg-slate-700 peer-focus:outline-none peer-focus:ring-4 peer-focus:ring-primary-500/20 rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-0.5 after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-gradient-to-r peer-checked:from-primary-600 peer-checked:to-indigo-600 shadow-sm" />
            </div>
            <label htmlFor="rule-active" className="text-sm text-gray-700 dark:text-slate-300 font-semibold cursor-pointer">
              {t('notifications.list.ruleActiveLabel')}
            </label>
          </div>
        </form>
      </Modal>

      <ConfirmDialog
        open={!!deleteTarget}
        title={t('notifications.list.deleteRule')}
        message={t('notifications.list.confirmDeleteRule', { name: deleteTarget?.name ?? '' })}
        confirmText={t('common.delete')}
        variant="danger"
        loading={deleting}
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}

// ====== 发送通知 Tab ======
function SendTab() {
  const { t } = useI18n();
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [form, setForm] = useState<SendNotificationRequest>({
    eventType: eventTypeOptions[0].value,
    mailboxId: null,
    conversationId: null,
    variables: {},
  });
  const [varKey, setVarKey] = useState('');
  const [varValue, setVarValue] = useState('');
  const [sending, setSending] = useState(false);
  const [result, setResult] = useState<{ type: 'success' | 'warning' | 'error'; text: string } | null>(null);

  useEffect(() => {
    mailboxApi.listAll()
      .then((resp) => setMailboxes(Array.isArray(resp) ? resp : []))
      .catch((err: unknown) => console.error('加载邮箱列表失败:', err));
  }, []);

  const handleAddVariable = () => {
    if (!varKey.trim()) return;
    setForm({
      ...form,
      variables: { ...form.variables, [varKey.trim()]: varValue },
    });
    setVarKey('');
    setVarValue('');
  };

  const handleRemoveVariable = (key: string) => {
    const next = { ...form.variables };
    delete next[key];
    setForm({ ...form, variables: next });
  };

  const [formError, setFormError] = useState('');

  const handleSend = async () => {
    setFormError('');
    if (!form.eventType || !form.eventType.trim()) {
      setFormError(t('notifications.common.selectEventType'));
      return;
    }
    if (!form.mailboxId && !form.conversationId) {
      setFormError(t('notifications.list.sendScopeRequired'));
      return;
    }

    setSending(true);
    setResult(null);
    try {
      const r = await notificationApi.send(form);
      if (r.totalRules === 0) {
        setResult({
          type: 'warning',
          text: t('notifications.list.sendNoRules'),
        });
      } else if (r.sentCount === 0) {
        setResult({
          type: 'error',
          text:
            r.failedCount > 0
              ? t('notifications.list.sendAllFailedWithErrors', { rules: r.totalRules, failed: r.failedCount, errors: r.errors.join('; ') })
              : t('notifications.list.sendAllFailedNoErrors', { rules: r.totalRules }),
        });
      } else if (r.failedCount === 0) {
        setResult({
          type: 'success',
          text: t('notifications.list.sendResultSuccess', { rules: r.totalRules, sent: r.sentCount }),
        });
      } else {
        setResult({
          type: 'error',
          text: t('notifications.list.sendResultPartial', { rules: r.totalRules, sent: r.sentCount, failed: r.failedCount, errors: r.errors.join('; ') }),
        });
      }
    } catch (err: unknown) {
      setResult({ type: 'error', text: err instanceof Error ? err.message : t('notifications.list.sendFailed') });
    } finally {
      setSending(false);
    }
  };

  const colors = getEventTypeColor(form.eventType);

  return (
    <div className="grid grid-cols-1 lg:grid-cols-3 gap-5">
      {/* 左侧：表单区 */}
      <div className="lg:col-span-2 bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/80 dark:border-slate-700/60 shadow-sm overflow-hidden">
        {/* 头部 */}
        <div className="p-6 bg-gradient-to-r from-primary-50 via-indigo-50 to-violet-50 dark:from-primary-900/30 dark:via-indigo-900/30 dark:to-violet-900/30 border-b border-primary-100 dark:border-slate-700/60">
          <div className="flex items-start gap-4">
            <div className="p-3 bg-white dark:bg-slate-800 rounded-xl ring-1 ring-primary-500/20 shadow-sm flex-shrink-0">
              <Send className="w-6 h-6 text-primary-600 dark:text-primary-400" />
            </div>
            <div className="flex-1 min-w-0">
              <h2 className="text-xl font-bold text-gray-900 dark:text-slate-100">{t('notifications.list.sendTitle')}</h2>
              <p className="text-sm text-gray-600 dark:text-slate-400 mt-1">
                {t('notifications.list.sendSubtitle')}
              </p>
            </div>
          </div>
        </div>

        <div className="p-6 space-y-5">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-5">
            <div>
              <label className={labelCls}>{t('notifications.common.eventType')} <span className="text-red-500">*</span></label>
              <select
                value={form.eventType}
                onChange={(e) => setForm({ ...form, eventType: e.target.value })}
                className={selectCls}
              >
                {eventTypeOptions.map((o) => (
                  <option key={o.value} value={o.value}>{t(o.label)}</option>
                ))}
              </select>
              <div className="mt-2">
                <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-bold ring-1 ${colors.ring} ${colors.bg} ${colors.text}`}>
                  <Zap className="w-3 h-3" />
                  {getEventTypeLabel(t, form.eventType)}
                </span>
              </div>
            </div>
            <div>
              <label className={labelCls}>{t('notifications.list.applicableMailbox')}</label>
              <select
                value={form.mailboxId ?? ''}
                onChange={(e) => setForm({ ...form, mailboxId: e.target.value === '' ? null : Number(e.target.value) })}
                className={selectCls}
              >
                <option value="">{t('notifications.common.allMailboxes')}</option>
                {mailboxes.map((m) => (
                  <option key={m.id} value={m.id}>{m.name}</option>
                ))}
              </select>
            </div>
          </div>

          <div>
            <label className={labelCls}>{t('notifications.list.conversationIdOptional')}</label>
            <input
              type="number"
              value={form.conversationId ?? ''}
              onChange={(e) => setForm({ ...form, conversationId: e.target.value === '' ? null : Number(e.target.value) })}
              className={inputCls}
              placeholder={t('notifications.list.conversationIdPlaceholder')}
            />
            <p className="text-[11px] text-gray-400 dark:text-slate-500 mt-1.5">
              {t('notifications.list.conversationIdHint')}
            </p>
          </div>

          {/* 变量编辑器 */}
          <div>
            <label className={labelCls}>{t('notifications.list.customVariables')}</label>
            <div className="mb-3">
              <div className="text-xs text-gray-500 dark:text-slate-400 mb-2 flex items-center gap-1.5">
                <Sparkles className="w-3.5 h-3.5" />
                {t('notifications.list.quickAddVariables')}
              </div>
              <div className="flex flex-wrap gap-1.5">
                {commonVariables.map((v) => (
                  <button
                    key={v}
                    type="button"
                    onClick={() => {
                      if (!form.variables[v]) {
                        setForm({ ...form, variables: { ...form.variables, [v]: '' } });
                      }
                    }}
                    className="text-xs px-2.5 py-1.5 bg-gray-50 dark:bg-slate-900/60 border border-gray-100 dark:border-slate-700 text-gray-700 dark:text-slate-300 rounded-lg hover:bg-primary-50 hover:text-primary-700 hover:border-primary-200 dark:hover:bg-primary-900/30 dark:hover:text-primary-400 dark:hover:border-primary-900/50 font-semibold transition-all"
                  >
                    + {v}
                  </button>
                ))}
              </div>
            </div>
            <div className="flex gap-2 mb-3">
              <input
                type="text"
                value={varKey}
                onChange={(e) => setVarKey(e.target.value)}
                className={inputCls + ' flex-1 font-mono'}
                placeholder={t('notifications.list.varNamePlaceholderLong')}
              />
              <input
                type="text"
                value={varValue}
                onChange={(e) => setVarValue(e.target.value)}
                className={inputCls + ' flex-1'}
                placeholder={t('notifications.list.varValuePlaceholder')}
              />
              <button
                type="button"
                onClick={handleAddVariable}
                className="px-4 py-2.5 text-sm bg-gradient-to-br from-gray-100 to-slate-100 dark:from-slate-700 dark:to-slate-700/80 hover:from-gray-200 hover:to-slate-200 dark:hover:from-slate-600 dark:hover:to-slate-600/80 rounded-xl transition font-semibold text-gray-700 dark:text-slate-200 border border-gray-200 dark:border-slate-600"
              >
                <Plus className="w-4 h-4" />
              </button>
            </div>
            {Object.keys(form.variables).length > 0 && (
              <div className="space-y-2">
                {Object.entries(form.variables).map(([k, v]) => (
                  <div
                    key={k}
                    className="flex items-center gap-2 px-4 py-2.5 bg-gradient-to-r from-gray-50 to-slate-50 dark:from-slate-900/40 dark:to-slate-900/60 rounded-xl border border-gray-100 dark:border-slate-700/60 text-sm"
                  >
                    <code className="font-mono font-bold text-primary-700 dark:text-primary-400 bg-primary-50 dark:bg-primary-900/30 px-2 py-0.5 rounded-md text-xs">
                      {k}
                    </code>
                    <span className="text-gray-400">=</span>
                    <span className="text-gray-700 dark:text-slate-300 flex-1 truncate">{v || t('notifications.list.emptyValue')}</span>
                    <button
                      type="button"
                      onClick={() => handleRemoveVariable(k)}
                      className="p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-lg transition"
                      title={t('notifications.list.removeVariable')}
                    >
                      <X className="w-4 h-4" />
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          {formError && (
            <div className="px-4 py-3 rounded-xl text-sm flex items-center gap-2 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-400">
              <AlertCircle className="w-4 h-4 flex-shrink-0" />
              {formError}
            </div>
          )}

          {result && (
            <div className={`px-4 py-3 rounded-xl text-sm flex items-start gap-2 ${
              result.type === 'success'
                ? 'bg-emerald-50 dark:bg-emerald-900/20 border border-emerald-200 dark:border-emerald-800 text-emerald-700 dark:text-emerald-400'
                : result.type === 'warning'
                ? 'bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 text-amber-700 dark:text-amber-400'
                : 'bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-400'
            }`}>
              {result.type === 'success' ? (
                <CheckCircle2 className="w-5 h-5 flex-shrink-0 mt-0.5" />
              ) : result.type === 'warning' ? (
                <AlertTriangle className="w-5 h-5 flex-shrink-0 mt-0.5" />
              ) : (
                <AlertCircle className="w-5 h-5 flex-shrink-0 mt-0.5" />
              )}
              <div className="flex-1">{result.text}</div>
            </div>
          )}

          <div className="flex justify-end pt-2 border-t border-gray-100 dark:border-slate-700/60">
            <button
              onClick={handleSend}
              disabled={sending}
              className="inline-flex items-center gap-2 px-6 py-3 bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 disabled:opacity-50 text-white text-sm font-semibold rounded-xl shadow-lg shadow-primary-500/30 hover:shadow-xl hover:shadow-primary-500/40 transition-all active:scale-[0.98]"
            >
              {sending ? (
                <>
                  <Activity className="w-5 h-5 animate-pulse" />
                  {t('notifications.list.sending')}
                </>
              ) : (
                <>
                  <Send className="w-5 h-5" />
                  {t('notifications.list.sendNow')}
                </>
              )}
            </button>
          </div>
        </div>
      </div>

      {/* 右侧：配置预览卡片 */}
      <div className="space-y-5">
        <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/80 dark:border-slate-700/60 shadow-sm overflow-hidden">
          <div className="p-4 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50 to-slate-50 dark:from-slate-900/40 dark:to-slate-900/60">
            <div className="flex items-center gap-2">
              <Settings2 className="w-4 h-4 text-gray-500 dark:text-slate-400" />
              <h3 className="font-bold text-gray-900 dark:text-slate-100 text-sm">{t('notifications.list.currentConfig')}</h3>
            </div>
          </div>
          <div className="p-4 space-y-3.5 text-sm">
            <div className="flex items-start justify-between gap-2 py-1">
              <span className="text-gray-500 dark:text-slate-400 text-xs uppercase tracking-wider font-semibold">{t('notifications.common.eventType')}</span>
              <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[11px] font-bold ring-1 ${colors.ring} ${colors.bg} ${colors.text}`}>
                <Zap className="w-3 h-3" />
                {getEventTypeLabel(t, form.eventType)}
              </span>
            </div>
            <div className="h-px bg-gray-100 dark:bg-slate-700/60" />
            <div className="flex items-start justify-between gap-2 py-1">
              <span className="text-gray-500 dark:text-slate-400 text-xs uppercase tracking-wider font-semibold">{t('notifications.list.applicableMailbox')}</span>
              <span className="text-gray-700 dark:text-slate-300 text-right">
                {mailboxes.find((m) => m.id === form.mailboxId)?.name || t('notifications.common.allMailboxes')}
              </span>
            </div>
            <div className="h-px bg-gray-100 dark:bg-slate-700/60" />
            <div className="flex items-start justify-between gap-2 py-1">
              <span className="text-gray-500 dark:text-slate-400 text-xs uppercase tracking-wider font-semibold">{t('notifications.list.conversationId')}</span>
              <span className="text-gray-700 dark:text-slate-300 font-mono text-right">
                {form.conversationId ? `#${form.conversationId}` : t('notifications.list.unspecified')}
              </span>
            </div>
            <div className="h-px bg-gray-100 dark:bg-slate-700/60" />
            <div className="flex items-start justify-between gap-2 py-1">
              <span className="text-gray-500 dark:text-slate-400 text-xs uppercase tracking-wider font-semibold">{t('notifications.list.customVariables')}</span>
              <span className="inline-flex items-center justify-center min-w-[24px] h-6 px-2 rounded-full text-[11px] font-bold bg-gradient-to-br from-primary-600 to-indigo-600 text-white shadow-sm">
                {Object.keys(form.variables).length}
              </span>
            </div>
          </div>
        </div>

        <div className="bg-gradient-to-br from-indigo-50 via-violet-50 to-purple-50 dark:from-indigo-900/30 dark:via-violet-900/30 dark:to-purple-900/30 rounded-2xl border border-indigo-100 dark:border-indigo-900/40 p-5 shadow-sm">
          <div className="flex items-start gap-3">
            <div className="p-2.5 bg-white dark:bg-slate-800 rounded-xl shadow-sm flex-shrink-0">
              <Info className="w-5 h-5 text-indigo-600 dark:text-indigo-400" />
            </div>
            <div className="flex-1">
              <h4 className="font-bold text-indigo-900 dark:text-indigo-200 text-sm">{t('notifications.list.tips')}</h4>
              <ul className="mt-2 space-y-1.5 text-xs text-indigo-700/80 dark:text-indigo-300/80 leading-relaxed">
                <li className="flex items-start gap-1.5">
                  <span className="mt-0.5">•</span>
                  {t('notifications.list.tip1')}
                </li>
                <li className="flex items-start gap-1.5">
                  <span className="mt-0.5">•</span>
                  {t('notifications.list.tip2')}
                </li>
                <li className="flex items-start gap-1.5">
                  <span className="mt-0.5">•</span>
                  {t('notifications.list.tip3')}
                </li>
              </ul>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

// ====== 模板预览 Tab ======
function PreviewTab() {
  const { t } = useI18n();
  const [template, setTemplate] = useState(t('notifications.list.sampleTemplate'));
  const [variables, setVariables] = useState<Record<string, string>>({
    customer_name: t('notifications.list.sample.customerName'),
    subject: t('notifications.list.sample.subject'),
    conversation_id: '12345',
    user_name: t('notifications.list.sample.agentName'),
    mailbox_name: t('notifications.list.sample.mailboxName'),
    mailbox_email: 'support@example.com',
  });
  const [varKey, setVarKey] = useState('');
  const [varValue, setVarValue] = useState('');
  const [rendered, setRendered] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const handleAddVariable = () => {
    if (!varKey.trim()) return;
    setVariables({ ...variables, [varKey.trim()]: varValue });
    setVarKey('');
    setVarValue('');
  };

  const handleRemoveVariable = (key: string) => {
    const next = { ...variables };
    delete next[key];
    setVariables(next);
  };

  const handlePreview = async () => {
    setLoading(true);
    setError('');
    try {
      const r = await notificationApi.preview(template, variables);
      setRendered(r.rendered);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('notifications.common.previewFailed'));
      setRendered('');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
      {/* 输入区 */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/80 dark:border-slate-700/60 shadow-sm overflow-hidden flex flex-col">
        <div className="p-4 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50 to-slate-50 dark:from-slate-900/40 dark:to-slate-900/60">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-white dark:bg-slate-800 rounded-lg ring-1 ring-primary-500/20 shadow-sm">
              <Code className="w-[18px] h-[18px] text-primary-600 dark:text-primary-400" />
            </div>
            <div>
              <h3 className="font-bold text-gray-900 dark:text-slate-100">{t('notifications.list.previewInputTitle')}</h3>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
                {t('notifications.list.previewInputHint')}
              </p>
            </div>
          </div>
        </div>

        <div className="p-5 space-y-5 flex-1">
          <div>
            <label className={labelCls}>{t('notifications.list.templateContentLabel')}</label>
            <textarea
              value={template}
              onChange={(e) => setTemplate(e.target.value)}
              rows={10}
              className={inputCls + ' font-mono text-[13px] leading-relaxed resize-y'}
              placeholder={t('notifications.list.templateContentPlaceholder')}
            />
            <div className="mt-3">
              <div className="text-xs text-gray-500 dark:text-slate-400 mb-2 flex items-center gap-1.5">
                <Sparkles className="w-3.5 h-3.5" />
                {t('notifications.list.quickAddVariablesCreate')}
              </div>
              <div className="flex flex-wrap gap-1.5">
                {commonVariables.map((v) => (
                  <button
                    key={v}
                    type="button"
                    onClick={() => {
                      if (!variables[v]) {
                        setVariables({ ...variables, [v]: '' });
                      }
                    }}
                    className="text-xs px-2.5 py-1.5 bg-gray-50 dark:bg-slate-900/60 border border-gray-100 dark:border-slate-700 text-gray-700 dark:text-slate-300 rounded-lg hover:bg-primary-50 hover:text-primary-700 hover:border-primary-200 dark:hover:bg-primary-900/30 dark:hover:text-primary-400 dark:hover:border-primary-900/50 font-semibold transition-all"
                  >
                    + {v}
                  </button>
                ))}
              </div>
            </div>
          </div>

          <div>
            <label className={labelCls}>{t('notifications.list.variableDictionary')}</label>
            <div className="flex gap-2 mb-3">
              <input
                type="text"
                value={varKey}
                onChange={(e) => setVarKey(e.target.value)}
                className={inputCls + ' flex-1 font-mono'}
                placeholder={t('notifications.list.varNamePlaceholder')}
              />
              <input
                type="text"
                value={varValue}
                onChange={(e) => setVarValue(e.target.value)}
                className={inputCls + ' flex-1'}
                placeholder={t('notifications.list.varValuePlaceholder')}
              />
              <button
                type="button"
                onClick={handleAddVariable}
                className="px-4 py-2.5 text-sm bg-gradient-to-br from-gray-100 to-slate-100 dark:from-slate-700 dark:to-slate-700/80 hover:from-gray-200 hover:to-slate-200 dark:hover:from-slate-600 dark:hover:to-slate-600/80 rounded-xl transition font-semibold text-gray-700 dark:text-slate-200 border border-gray-200 dark:border-slate-600"
              >
                <Plus className="w-4 h-4" />
              </button>
            </div>
            <div className="space-y-2 max-h-56 overflow-y-auto pr-1">
              {Object.entries(variables).length === 0 ? (
                <div className="text-center py-6 text-xs text-gray-400 dark:text-slate-500">
                  {t('notifications.list.noVariables')}
                </div>
              ) : (
                Object.entries(variables).map(([k, v]) => (
                  <div
                    key={k}
                    className="flex items-center gap-2 px-3 py-2 bg-gradient-to-r from-gray-50 to-slate-50 dark:from-slate-900/40 dark:to-slate-900/60 rounded-xl border border-gray-100 dark:border-slate-700/60 text-sm"
                  >
                    <code className="font-mono font-bold text-primary-700 dark:text-primary-400 bg-primary-50 dark:bg-primary-900/30 px-2 py-0.5 rounded-md text-xs flex-shrink-0">
                      {k}
                    </code>
                    <span className="text-gray-400">=</span>
                    <input
                      type="text"
                      value={v}
                      onChange={(e) => setVariables({ ...variables, [k]: e.target.value })}
                      className="flex-1 px-2.5 py-1.5 text-sm bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-lg font-mono dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500"
                    />
                    <button
                      type="button"
                      onClick={() => handleRemoveVariable(k)}
                      className="p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-lg transition flex-shrink-0"
                    >
                      <X className="w-4 h-4" />
                    </button>
                  </div>
                ))
              )}
            </div>
          </div>

          <div className="flex justify-end pt-2 border-t border-gray-100 dark:border-slate-700/60">
            <button
              onClick={handlePreview}
              disabled={loading}
              className="inline-flex items-center gap-2 px-5 py-2.5 bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 disabled:opacity-50 text-white text-sm font-semibold rounded-xl shadow-md shadow-primary-500/20 hover:shadow-lg hover:shadow-primary-500/30 transition-all active:scale-[0.98]"
            >
              {loading ? (
                <>
                  <Activity className="w-4 h-4 animate-pulse" />
                  {t('notifications.list.rendering')}
                </>
              ) : (
                <>
                  <Eye className="w-4 h-4" />
                  {t('notifications.list.renderPreview')}
                </>
              )}
            </button>
          </div>
        </div>
      </div>

      {/* 预览结果 */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/80 dark:border-slate-700/60 shadow-sm overflow-hidden flex flex-col">
        <div className="p-4 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50 to-slate-50 dark:from-slate-900/40 dark:to-slate-900/60 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-white dark:bg-slate-800 rounded-lg ring-1 ring-emerald-500/20 shadow-sm">
              <Eye className="w-[18px] h-[18px] text-emerald-600 dark:text-emerald-400" />
            </div>
            <div>
              <h3 className="font-bold text-gray-900 dark:text-slate-100">{t('notifications.list.renderResult')}</h3>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
                {t('notifications.list.renderResultSubtitle')}
              </p>
            </div>
          </div>
          {rendered && (
            <button
              onClick={() => {
                const text = rendered;
                if (navigator.clipboard?.writeText) {
                  navigator.clipboard.writeText(text);
                } else {
                  const textarea = document.createElement('textarea');
                  textarea.value = text;
                  textarea.style.position = 'fixed';
                  textarea.style.opacity = '0';
                  document.body.appendChild(textarea);
                  textarea.select();
                  document.execCommand('copy');
                  document.body.removeChild(textarea);
                }
              }}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold text-gray-600 dark:text-slate-400 hover:text-primary-600 dark:hover:text-primary-400 hover:bg-gray-100 dark:hover:bg-slate-700/50 rounded-lg transition border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800"
            >
              <Copy className="w-3.5 h-3.5" />
              {t('notifications.list.copyResult')}
            </button>
          )}
        </div>

        <div className="p-5 flex-1 min-h-[420px]">
          {error ? (
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-400 px-4 py-3 rounded-xl text-sm flex items-center gap-2">
              <AlertCircle className="w-4 h-4 flex-shrink-0" />
              {error}
            </div>
          ) : rendered ? (
            <div className="space-y-4">
              {/* HTML 预览区 */}
              <div className="rounded-xl border border-gray-100 dark:border-slate-700/60 bg-gradient-to-br from-white to-gray-50 dark:from-slate-800 dark:to-slate-900/60 overflow-hidden">
                <div className="px-4 py-2 border-b border-gray-100 dark:border-slate-700/60 bg-gray-50 dark:bg-slate-900/40 flex items-center gap-2">
                  <div className="flex gap-1.5">
                    <span className="w-2.5 h-2.5 rounded-full bg-red-400" />
                    <span className="w-2.5 h-2.5 rounded-full bg-amber-400" />
                    <span className="w-2.5 h-2.5 rounded-full bg-emerald-400" />
                  </div>
                  <span className="text-[11px] font-semibold text-gray-500 dark:text-slate-400 ml-2">
                    {t('notifications.list.htmlPreview')}
                  </span>
                </div>
                <div
                  className="p-5 prose prose-sm dark:prose-invert max-w-none text-gray-800 dark:text-slate-200 min-h-[200px]"
                  dangerouslySetInnerHTML={{ __html: sanitizeHtml(rendered) }}
                />
              </div>

              {/* 原始文本 */}
              <div className="rounded-xl border border-gray-100 dark:border-slate-700/60 bg-gray-50 dark:bg-slate-900/60 overflow-hidden">
                <div className="px-4 py-2 border-b border-gray-100 dark:border-slate-700/60 flex items-center gap-2">
                  <Code className="w-3.5 h-3.5 text-gray-500" />
                  <span className="text-[11px] font-semibold text-gray-500 dark:text-slate-400">
                    {t('notifications.list.rawText')}
                  </span>
                </div>
                <pre className="p-4 text-sm text-gray-800 dark:text-slate-200 whitespace-pre-wrap break-words font-mono leading-relaxed max-h-40 overflow-auto">
                  {rendered}
                </pre>
              </div>
            </div>
          ) : (
            <div className="h-full flex flex-col items-center justify-center text-gray-400 py-16">
              <div className="w-20 h-20 rounded-2xl bg-gradient-to-br from-gray-50 to-gray-100 dark:from-slate-800 dark:to-slate-900 border border-gray-100 dark:border-slate-700 flex items-center justify-center mb-4">
                <Eye className="w-10 h-10 opacity-30" />
              </div>
              <p className="text-sm font-semibold text-gray-500 dark:text-slate-400">{t('notifications.list.waitingPreview')}</p>
              <p className="text-xs mt-1 text-gray-400 dark:text-slate-500">
                {t('notifications.list.waitingPreviewHint')}
              </p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// ====== 主组件 ======
export default function Notifications() {
  const { t } = useI18n();
  const [activeTab, setActiveTab] = useState<TabKey>('templates');

  return (
    <div className="space-y-5">
      {/* 页头 */}
      <div className="relative overflow-hidden bg-gradient-to-r from-primary-50 via-indigo-50 to-violet-50 dark:from-slate-800 dark:via-slate-800 dark:to-slate-900 rounded-2xl p-6 md:p-7 border border-primary-100 dark:border-slate-700/50 shadow-sm">
        {/* 装饰光斑 */}
        <div className="absolute -right-20 -top-20 w-72 h-72 rounded-full bg-primary-200/30 dark:bg-primary-500/10 blur-3xl pointer-events-none" />
        <div className="absolute right-20 -bottom-16 w-56 h-56 rounded-full bg-indigo-200/30 dark:bg-indigo-500/10 blur-3xl pointer-events-none" />

        <div className="relative flex items-start gap-4 md:gap-5">
          <div className="p-3.5 bg-white dark:bg-slate-800 rounded-2xl ring-1 ring-primary-500/20 shadow-lg shadow-primary-500/10 flex-shrink-0">
            <Bell className="w-7 h-7 text-primary-600 dark:text-primary-400" />
          </div>
          <div className="flex-1 min-w-0">
            <h1 className="text-2xl md:text-3xl font-extrabold text-gray-900 dark:text-slate-100 tracking-tight">
              {t('notifications.list.pageTitle')}
            </h1>
            <p className="text-sm md:text-base text-gray-600 dark:text-slate-400 mt-1.5 leading-relaxed">
              {t('notifications.list.pageSubtitle')}
            </p>
          </div>
        </div>
      </div>

      {/* Tab 切换 */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/80 dark:border-slate-700/60 p-2 inline-flex flex-wrap gap-1 shadow-sm">
        {tabs.map((tab) => (
          <button
            key={tab.key}
            onClick={() => setActiveTab(tab.key)}
            className={`group relative inline-flex items-center gap-2 px-3.5 sm:px-5 py-2 sm:py-2.5 text-[13px] sm:text-sm font-semibold rounded-xl transition-all ${
              activeTab === tab.key
                ? 'bg-gradient-to-br from-primary-600 to-indigo-600 text-white shadow-md shadow-primary-500/20'
                : 'text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700/50'
            }`}
            title={tab.hint ? t(tab.hint) : undefined}
          >
            <span className={activeTab === tab.key ? 'drop-shadow-sm' : ''}>
              {tab.icon}
            </span>
            <span>{t(tab.label)}</span>
            {activeTab === tab.key && (
              <span className="absolute -bottom-0.5 left-1/2 -translate-x-1/2 w-8 h-1 bg-white/40 rounded-full" />
            )}
          </button>
        ))}
      </div>

      <div>
        {activeTab === 'templates' && <TemplatesTab />}
        {activeTab === 'rules' && <RulesTab />}
        {activeTab === 'send' && <SendTab />}
        {activeTab === 'preview' && <PreviewTab />}
      </div>
    </div>
  );
}
