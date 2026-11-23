import { useState, useEffect, useRef, useCallback } from 'react';
import { notificationApi, mailboxApi } from '../api';
import { useTimedToast } from '../hooks/useTimedToast';
import type {
  NotificationRuleResponse,
  NotificationRuleOverviewResponse,
  CreateNotificationRuleRequest,
  NotificationTemplateResponse,
  MailboxResponse,
} from '../api/types';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import { formatDateTime } from '../utils/format';
import { getApiErrorMessage } from '../utils/error';
import { getBoundLocale, toIntlLocale } from '../i18n/locale';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import type { LucideIcon } from 'lucide-react';
import {
  Bell,
  CheckCircle2,
  XCircle,
  LayoutGrid,
  List,
  Plus,
  Search,
  Edit2,
  Trash2,
  ChevronLeft,
  ChevronRight,
  Tag,
  FileText,
  Mailbox,
  RotateCcw,
  Copy,
  Zap,
  User,
  Activity,
  TrendingUp,
  X,
  FilterX,
  AlertTriangle,
  Sparkles,
  Settings2,
  Info,
  Shield,
  Save,
  FileEdit,
  Hash,
  Eye,
  Archive,
  RefreshCw,
} from 'lucide-react';

/**
 * 事件类型下拉项。**只持有真源 key**，渲染时必须经 `t()` 解析——
 * 直接渲染表里的值会把 `notifications.eventType.*` 这样的原始 key 漏到界面上。
 */
const eventTypeOptions: { value: string; labelKey: TranslationKey }[] = [
  { value: 'new_conversation', labelKey: 'notifications.eventType.newConversation' },
  { value: 'new_reply', labelKey: 'notifications.eventType.newReply' },
  { value: 'conversation_assigned', labelKey: 'notifications.eventType.conversationAssigned' },
  { value: 'conversation_updated', labelKey: 'notifications.eventType.conversationUpdated' },
  { value: 'conversation_closed', labelKey: 'notifications.eventType.conversationClosed' },
  { value: 'customer_reply', labelKey: 'notifications.eventType.customerReply' },
  { value: 'mention', labelKey: 'notifications.eventType.mention' },
  { value: 'sla_violation', labelKey: 'notifications.eventType.slaViolation' },
  { value: 'sla_warning', labelKey: 'notifications.eventType.slaWarning' },
  { value: 'conversation_timeout', labelKey: 'notifications.eventType.conversationTimeout' },
  { value: 'ticket_escalated', labelKey: 'notifications.eventType.ticketEscalated' },
  { value: 'customer_satisfaction', labelKey: 'notifications.eventType.customerSatisfaction' },
  { value: 'auto_reply', labelKey: 'notifications.eventType.autoReply' },
  { value: 'user_welcome', labelKey: 'notifications.eventType.userWelcome' },
  { value: 'password_reset', labelKey: 'notifications.eventType.passwordReset' },
  { value: 'bulk_import_completed', labelKey: 'notifications.eventType.bulkImportCompleted' },
  { value: 'conversation_reopened', labelKey: 'notifications.eventType.conversationReopened' },
  { value: 'conversation_transferred', labelKey: 'notifications.eventType.conversationTransferred' },
  { value: 'satisfaction_submitted', labelKey: 'notifications.eventType.satisfactionSubmitted' },
  { value: 'daily_report', labelKey: 'notifications.eventType.dailyReport' },
  { value: 'account_locked', labelKey: 'notifications.eventType.accountLocked' },
  { value: 'system_maintenance', labelKey: 'notifications.eventType.systemMaintenance' },
];

/** 事件类型码 → 真源 key（未知码回落原码，见下方 helper） */
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

/** 事件类型码 → 界面标签；未知码回落原码（与原行为一致），绝不把 key 漏到界面 */
function getEventTypeLabel(t: (key: TranslationKey) => string, evt: string): string {
  const key = eventTypeLabels[evt];
  return key ? t(key) : evt;
}

/** 同上，但按 `eventTypeOptions` 取值（事件分布 / 垃圾箱两处原实现即如此） */
function getEventTypeOptionLabel(t: (key: TranslationKey) => string, evt: string): string {
  const opt = eventTypeOptions.find((e) => e.value === evt);
  return opt ? t(opt.labelKey) : evt;
}

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

  // 系统通知（stone/深色）
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
];

/**
 * 概览统计卡片。**只持有真源 key**，渲染时经 `t()` 解析；
 * 显式标注元素类型，否则 `label` / `sub` 会退化成 `string` 而绕过字面量联合校验。
 */
interface RuleStatCard {
  label: TranslationKey;
  value: number;
  icon: LucideIcon;
  sub: TranslationKey;
  palette: (typeof statPalette)[number];
  trendUp: boolean;
}

export default function NotificationRules() {
  const { t } = useI18n();
  const [activeTab, setActiveTab] = useState<'overview' | 'list'>('overview');
  const [overview, setOverview] = useState<NotificationRuleOverviewResponse | null>(null);
  const [rules, setRules] = useState<NotificationRuleResponse[]>([]);
  const [templates, setTemplates] = useState<NotificationTemplateResponse[]>([]);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [loading, setLoading] = useState(false);
  const [totalItems, setTotalItems] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [eventTypeFilter, setEventTypeFilter] = useState<string | null>(null);
  const [activeFilter, setActiveFilter] = useState<boolean | null>(null);

  const [showEditor, setShowEditor] = useState(false);
  const [editingRule, setEditingRule] = useState<NotificationRuleResponse | null>(null);

  const [formData, setFormData] = useState<CreateNotificationRuleRequest>({
    name: '',
    eventType: 'new_conversation',
    mailboxId: null,
    userId: null,
    templateId: null,
    isActive: true,
  });
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);

  const [deleteTarget, setDeleteTarget] = useState<NotificationRuleResponse | null>(null);
  const { message: toast, show: showToast } = useTimedToast();

  // 垃圾箱
  const [trashOpen, setTrashOpen] = useState(false);
  const [trashQuery, setTrashQuery] = useState<{ page: number; pageSize: number; keyword: string }>({ page: 1, pageSize: 10, keyword: '' });
  const [trashList, setTrashList] = useState<{ data: NotificationRuleResponse[]; totalItems: number; totalPages: number; currentPage: number } | null>(null);
  const [trashLoading, setTrashLoading] = useState(false);
  const [trashCount, setTrashCount] = useState(0);
  const [restoreTarget, setRestoreTarget] = useState<NotificationRuleResponse | null>(null);
  const [hardDeleteTarget, setHardDeleteTarget] = useState<NotificationRuleResponse | null>(null);
  const [clearTrashOpen, setClearTrashOpen] = useState(false);
  const [trashActionLoading, setTrashActionLoading] = useState(false);

  const pageSize = 10;
  const trashPageSize = 10;

  // ---- 搜索防抖（与 ApiKeys 交互一致：650ms 输入停顿自动搜索，Enter 立即触发）----
  // 主列表搜索：searchInput 为输入缓冲（即时回显），650ms 后才写入 search + page 1
  const [searchInput, setSearchInput] = useState('');
  const searchTimerRef = useRef<number | null>(null);
  const applySearch = useCallback((value: string) => {
    setSearch(value);
    setPage(1);
  }, []);
  const handleSearchInput = useCallback((value: string) => {
    setSearchInput(value);
    if (searchTimerRef.current !== null) window.clearTimeout(searchTimerRef.current);
    searchTimerRef.current = window.setTimeout(() => applySearch(value), 650);
  }, [applySearch]);
  const triggerSearchNow = useCallback(() => {
    if (searchTimerRef.current !== null) {
      window.clearTimeout(searchTimerRef.current);
      searchTimerRef.current = null;
    }
    applySearch(searchInput);
  }, [applySearch, searchInput]);
  const clearSearch = useCallback(() => {
    if (searchTimerRef.current !== null) {
      window.clearTimeout(searchTimerRef.current);
      searchTimerRef.current = null;
    }
    setSearchInput('');
    setSearch('');
    setPage(1);
  }, []);

  // 回收站搜索：trashKeywordInput 为输入缓冲（即时回显），650ms 后才写入 trashQuery.keyword + page 1
  const [trashKeywordInput, setTrashKeywordInput] = useState('');
  const trashSearchTimerRef = useRef<number | null>(null);
  const applyTrashSearch = useCallback((value: string) => {
    setTrashQuery((q) => ({ ...q, keyword: value, page: 1 }));
  }, []);
  const handleTrashSearchInput = useCallback((value: string) => {
    setTrashKeywordInput(value);
    if (trashSearchTimerRef.current !== null) window.clearTimeout(trashSearchTimerRef.current);
    trashSearchTimerRef.current = window.setTimeout(() => applyTrashSearch(value), 650);
  }, [applyTrashSearch]);
  const triggerTrashSearchNow = useCallback(() => {
    if (trashSearchTimerRef.current !== null) {
      window.clearTimeout(trashSearchTimerRef.current);
      trashSearchTimerRef.current = null;
    }
    applyTrashSearch(trashKeywordInput);
  }, [applyTrashSearch, trashKeywordInput]);
  const clearTrashSearch = useCallback(() => {
    if (trashSearchTimerRef.current !== null) {
      window.clearTimeout(trashSearchTimerRef.current);
      trashSearchTimerRef.current = null;
    }
    setTrashKeywordInput('');
    setTrashQuery((q) => ({ ...q, keyword: '', page: 1 }));
  }, []);

  // 卸载时清理挂起的搜索防抖计时器
  useEffect(() => {
    return () => {
      if (searchTimerRef.current !== null) window.clearTimeout(searchTimerRef.current);
      if (trashSearchTimerRef.current !== null) window.clearTimeout(trashSearchTimerRef.current);
    };
  }, []);

  useEffect(() => {
    loadOptions();
  }, []);

  useEffect(() => {
    if (activeTab === 'overview') loadOverview();
    if (activeTab === 'list') loadList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab]);

  useEffect(() => {
    if (activeTab === 'list') loadList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, search, eventTypeFilter, activeFilter]);

  async function loadOptions() {
    try {
      const [tplRes, mbRes] = await Promise.all([
        notificationApi.listAllTemplates(),
        mailboxApi.list(),
      ]);
      setTemplates(tplRes);
      setMailboxes(mbRes);
    } catch (e: unknown) {
      console.error('加载选项失败', e);
    }
  }

  async function loadOverview() {
    setLoading(true);
    try {
      const data = await notificationApi.getRuleOverview();
      setOverview(data);
    } catch (e: unknown) {
      showToast({ type: 'error', text: t('notifications.common.loadFailed', { message: getApiErrorMessage(e, t('notifications.common.unknownError')) }) });
    } finally {
      setLoading(false);
    }
  }

  async function loadList() {
    setLoading(true);
    try {
      const data = await notificationApi.listRules({
        page,
        pageSize,
        eventType: eventTypeFilter || undefined,
        isActive: activeFilter ?? undefined,
        search: search || undefined,
      });
      setRules(data.data);
      setTotalItems(data.totalItems);
    } catch (e: unknown) {
      showToast({ type: 'error', text: t('notifications.common.loadFailed', { message: getApiErrorMessage(e, t('notifications.common.unknownError')) }) });
    } finally {
      setLoading(false);
    }
  }

  function openCreate() {
    setEditingRule(null);
    setFormData({
      name: '',
      eventType: 'new_conversation',
      mailboxId: null,
      userId: null,
      templateId: null,
      isActive: true,
    });
    setFormError('');
    setShowEditor(true);
  }

  async function openEdit(rule: NotificationRuleResponse) {
    try {
      const data = await notificationApi.getRule(rule.id);
      setEditingRule(data);
      setFormData({
        name: data.name,
        eventType: data.eventType,
        mailboxId: data.mailboxId ?? null,
        userId: data.userId ?? null,
        templateId: data.templateId ?? null,
        isActive: data.isActive,
      });
      setFormError('');
      setShowEditor(true);
    } catch (e: unknown) {
      showToast({ type: 'error', text: t('notifications.common.loadFailed', { message: getApiErrorMessage(e, t('notifications.common.unknownError')) }) });
    }
  }

  async function handleSave() {
    if (!formData.name.trim()) {
      setFormError(t('notifications.rules.nameRequired'));
      return;
    }
    if (!formData.eventType) {
      setFormError(t('notifications.common.selectEventType'));
      return;
    }

    setSaving(true);
    setFormError('');
    try {
      if (editingRule) {
        await notificationApi.updateRule(editingRule.id, formData);
        showToast({ type: 'success', text: t('notifications.rules.updateSuccess') });
      } else {
        await notificationApi.createRule(formData);
        showToast({ type: 'success', text: t('notifications.rules.createSuccess') });
      }
      setShowEditor(false);
      if (activeTab === 'overview') loadOverview();
      if (activeTab === 'list') loadList();
    } catch (e: unknown) {
      setFormError(getApiErrorMessage(e, t('notifications.common.saveFailed')));
    } finally {
      setSaving(false);
    }
  }

  async function handleToggleActive(rule: NotificationRuleResponse) {
    try {
      await notificationApi.toggleRuleActive(rule.id);
      showToast({ type: 'success', text: rule.isActive ? t('notifications.rules.disabledSuccess') : t('notifications.rules.enabledSuccess') });
      if (activeTab === 'overview') loadOverview();
      if (activeTab === 'list') loadList();
    } catch (e: unknown) {
      showToast({ type: 'error', text: t('notifications.common.opFailed', { message: getApiErrorMessage(e, t('notifications.common.unknownError')) }) });
    }
  }

  async function handleDelete(rule: NotificationRuleResponse) {
    try {
      await notificationApi.removeRule(rule.id);
      setDeleteTarget(null);
      showToast({ type: 'success', text: t('notifications.rules.moveToTrashSuccess') });
      if (activeTab === 'overview') loadOverview();
      if (activeTab === 'list') loadList();
      refreshTrashCount();
    } catch (e: unknown) {
      showToast({ type: 'error', text: t('notifications.rules.deleteFailedWithReason', { message: getApiErrorMessage(e, t('notifications.common.unknownError')) }) });
    }
  }

  async function handleDuplicate(rule: NotificationRuleResponse) {
    try {
      const newRule = await notificationApi.duplicateRule(rule.id);
      showToast({ type: 'success', text: t('notifications.common.copiedAs', { name: newRule.name }) });
      if (activeTab === 'overview') loadOverview();
      if (activeTab === 'list') loadList();
    } catch (e: unknown) {
      showToast({ type: 'error', text: t('notifications.common.copyFailed', { message: getApiErrorMessage(e, t('notifications.common.unknownError')) }) });
    }
  }

  // ====== 垃圾箱 ======
  function refreshTrashCount() {
    notificationApi.getRuleTrashCount().then((res) => {
      const count = res?.count ?? 0;
      setTrashCount(typeof count === 'number' ? count : 0);
    }).catch(() => {});
  }

  function refreshTrashList() {
    setTrashLoading(true);
    notificationApi.listRuleTrash({
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
    if (trashSearchTimerRef.current !== null) {
      window.clearTimeout(trashSearchTimerRef.current);
      trashSearchTimerRef.current = null;
    }
    setTrashKeywordInput('');
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
      await notificationApi.restoreRule(restoreTarget.id);
      showToast({ type: 'success', text: t('notifications.rules.restoreSuccess', { name: restoreTarget.name }) });
      setRestoreTarget(null);
      if (activeTab === 'overview') loadOverview();
      if (activeTab === 'list') loadList();
      refreshTrashList();
      refreshTrashCount();
    } catch (e: unknown) {
      showToast({ type: 'error', text: getApiErrorMessage(e, t('notifications.common.restoreFailed')) });
    } finally {
      setTrashActionLoading(false);
    }
  }

  async function handleHardDelete() {
    if (!hardDeleteTarget) return;
    setTrashActionLoading(true);
    try {
      await notificationApi.hardDeleteRule(hardDeleteTarget.id);
      showToast({ type: 'success', text: t('notifications.rules.hardDeleteSuccess', { name: hardDeleteTarget.name }) });
      setHardDeleteTarget(null);
      refreshTrashList();
      refreshTrashCount();
    } catch (e: unknown) {
      showToast({ type: 'error', text: getApiErrorMessage(e, t('notifications.common.deleteFailed')) });
    } finally {
      setTrashActionLoading(false);
    }
  }

  async function handleClearTrash() {
    setTrashActionLoading(true);
    try {
      const res = await notificationApi.clearRuleTrash();
      const count = res?.count ?? 0;
      showToast({ type: 'success', text: t('notifications.rules.clearTrashSuccess', { count }) });
      setClearTrashOpen(false);
      refreshTrashList();
      refreshTrashCount();
    } catch (e: unknown) {
      showToast({ type: 'error', text: getApiErrorMessage(e, t('notifications.common.clearFailed')) });
    } finally {
      setTrashActionLoading(false);
    }
  }

  const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));

  return (
    <div className="space-y-5">
      {/* 页头 */}
      <div className="bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-800 dark:to-slate-900 rounded-xl p-6 border border-primary-100 dark:border-slate-700/50">
        <div className="flex items-center gap-3">
          <div className="p-2.5 bg-primary-100 dark:bg-primary-900/30 rounded-lg">
            <Bell className="w-6 h-6 text-primary-600 dark:text-primary-400" />
          </div>
          <div>
            <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100">{t('notifications.rules.title')}</h1>
            <p className="text-sm text-gray-600 dark:text-slate-400 mt-0.5">{t('notifications.rules.subtitle')}</p>
          </div>
        </div>
      </div>

      {/* Tab 切换 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 p-1.5 inline-flex flex-wrap gap-1">
        <button
          className={`inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium rounded-lg transition ${
            activeTab === 'overview'
              ? 'bg-primary-600 text-white shadow-sm'
              : 'text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700/50'
          }`}
          onClick={() => setActiveTab('overview')}
        >
          <LayoutGrid className="w-4 h-4" />
          {t('notifications.rules.tabOverview')}
        </button>
        <button
          className={`inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium rounded-lg transition ${
            activeTab === 'list'
              ? 'bg-primary-600 text-white shadow-sm'
              : 'text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700/50'
          }`}
          onClick={() => setActiveTab('list')}
        >
          <List className="w-4 h-4" />
          {t('notifications.rules.tabList')}
        </button>
      </div>

      {/* 概览 Tab */}
      {activeTab === 'overview' && overview && (
        <div className="space-y-6">
          {/* 统计卡片 */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-5">
            {([
              { label: 'notifications.rules.statTotal', value: overview.total, icon: Bell, sub: 'notifications.rules.statTotalHint', palette: statPalette[0], trendUp: true },
              { label: 'notifications.rules.statActive', value: overview.active, icon: CheckCircle2, sub: 'notifications.rules.statActiveHint', palette: statPalette[1], trendUp: true },
              { label: 'notifications.rules.statInactive', value: overview.inactive, icon: XCircle, sub: 'notifications.rules.statInactiveHint', palette: statPalette[2], trendUp: false },
            ] satisfies RuleStatCard[]).map((s, idx) => (
              <div
                key={s.label}
                className="group relative bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/80 dark:border-slate-700/60 p-6 overflow-hidden shadow-sm hover:shadow-lg hover:-translate-y-0.5 transition-all duration-300"
              >
                {/* 左侧渐变竖条 */}
                <div className={`absolute left-0 top-0 bottom-0 w-1.5 bg-gradient-to-b ${s.palette.from} ${s.palette.to}`} />
                {/* 背景柔光 */}
                <div className={`absolute -right-8 -top-8 w-32 h-32 rounded-full ${s.palette.bgSoft} opacity-60 blur-2xl pointer-events-none`} />
                <div className="relative flex items-start justify-between">
                  <div className="flex-1">
                    <div className="flex items-center gap-2">
                      <span className="text-xs font-medium text-gray-500 dark:text-slate-400 tracking-wider uppercase">
                        {t(s.label)}
                      </span>
                      <span className={`inline-flex items-center gap-0.5 text-[10px] font-semibold px-1.5 py-0.5 rounded-full ${s.trendUp ? 'text-emerald-600 bg-emerald-50 dark:text-emerald-400 dark:bg-emerald-900/30' : 'text-amber-600 bg-amber-50 dark:text-amber-400 dark:bg-amber-900/30'}`}>
                        <Activity className="w-3 h-3" />
                        {s.trendUp ? 'LIVE' : 'PAUSED'}
                      </span>
                    </div>
                    <div className="mt-3 flex items-baseline gap-2">
                      <span className="text-4xl font-extrabold text-gray-900 dark:text-slate-50 tabular-nums tracking-tight">
                        {new Intl.NumberFormat(toIntlLocale(getBoundLocale())).format(s.value)}
                      </span>
                      <span className="text-sm text-gray-400 dark:text-slate-500">{t('notifications.rules.countUnit', { count: s.value })}</span>
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
                {/* 底部活跃比例 */}
                {idx === 0 && overview.total > 0 && (
                  <div className="relative mt-5">
                    <div className="flex items-center justify-between text-[11px] font-medium text-gray-500 dark:text-slate-400 mb-1.5">
                      <span>{t('notifications.rules.enableRate')}</span>
                      <span className="tabular-nums">{Math.round((overview.active / overview.total) * 100)}%</span>
                    </div>
                    <div className="h-2 bg-gray-100 dark:bg-slate-700/50 rounded-full overflow-hidden">
                      <div
                        className="h-full bg-gradient-to-r from-emerald-500 to-teal-500 rounded-full transition-all duration-700"
                        style={{ width: `${(overview.active / overview.total) * 100}%` }}
                      />
                    </div>
                  </div>
                )}
              </div>
            ))}
          </div>

          {/* 按事件类型分布 */}
          <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/80 dark:border-slate-700/60 p-6 shadow-sm">
            <div className="flex items-center justify-between mb-5 flex-wrap gap-3">
              <div className="flex items-center gap-3">
                <div className="p-2.5 rounded-xl bg-gradient-to-br from-primary-50 to-indigo-50 dark:from-primary-900/40 dark:to-indigo-900/40 ring-1 ring-primary-500/20">
                  <Tag className="w-5 h-5 text-primary-600 dark:text-primary-400" />
                </div>
                <div>
                  <h3 className="text-base font-semibold text-gray-900 dark:text-slate-100 leading-tight">{t('notifications.rules.eventDistribution')}</h3>
                  <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
                    {t('notifications.rules.eventSummary', {
                      rules: overview.events.reduce((acc, e) => acc + e.count, 0),
                      types: overview.events.length,
                    })}
                  </p>
                </div>
              </div>
              {overview.events.length > 0 && (
                <div className="flex items-center gap-2 text-xs text-gray-500 dark:text-slate-400">
                  <span className="inline-flex items-center gap-1">
                    <span className="w-2.5 h-2.5 rounded-sm bg-gradient-to-br from-emerald-500 to-teal-500" />
                    {t('notifications.common.enable')}
                  </span>
                  <span className="inline-flex items-center gap-1">
                    <span className="w-2.5 h-2.5 rounded-sm bg-gray-200 dark:bg-slate-700" />
                    {t('notifications.common.disable')}
                  </span>
                </div>
              )}
            </div>

            {overview.events.length === 0 ? (
              <div className="py-14 flex flex-col items-center justify-center text-gray-400">
                <div className="w-16 h-16 rounded-2xl bg-gray-50 dark:bg-slate-900 flex items-center justify-center mb-4">
                  <Sparkles className="w-8 h-8 opacity-40" />
                </div>
                <p className="text-sm font-medium">{t('notifications.rules.emptyTitle')}</p>
                <p className="text-xs mt-1">{t('notifications.rules.emptyHint')}</p>
              </div>
            ) : (
              <div className="space-y-3.5">
                {overview.events.map((evt) => {
                  const colors = getEventTypeColor(evt.eventType);
                  const ratio = overview.total ? (evt.count / overview.total) * 100 : 0;
                  const activeRatio = evt.count > 0 ? (evt.activeCount / evt.count) * 100 : 0;
                  return (
                    <div
                      key={evt.eventType}
                      className="group grid grid-cols-12 items-center gap-4 p-3 -mx-3 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700/30 transition-colors"
                    >
                      {/* 事件名 + 徽章 */}
                      <div className="col-span-12 sm:col-span-3 flex items-center gap-2.5 min-w-0">
                        <div className={`w-1.5 h-10 rounded-full ${colors.accent} flex-shrink-0`} />
                        <div className="min-w-0">
                          <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[11px] font-semibold ring-1 ${colors.ring} ${colors.bg} ${colors.text}`}>
                            <Zap className="w-3 h-3" />
                            {getEventTypeLabel(t, evt.eventType)}
                          </span>
                          <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-1">
                            {t('notifications.rules.eventKeyLabel')}<code className="font-mono opacity-70">{evt.eventType}</code>
                          </div>
                        </div>
                      </div>

                      {/* 分段进度条 */}
                      <div className="col-span-12 sm:col-span-7">
                        <div className="h-8 bg-gray-50 dark:bg-slate-900/60 rounded-lg border border-gray-100 dark:border-slate-700 overflow-hidden flex items-center px-2">
                          {/* 启用段 */}
                          <div
                            className={`h-6 rounded-md ${colors.accent} opacity-90 transition-all duration-700 relative overflow-hidden`}
                            style={{ width: `${ratio * (activeRatio / 100)}%` }}
                          >
                            <div className="absolute inset-0 bg-gradient-to-r from-white/30 to-transparent" />
                          </div>
                          {/* 停用段（灰色） */}
                          <div
                            className="h-6 rounded-md bg-gray-200 dark:bg-slate-700 ml-0.5 transition-all duration-700"
                            style={{ width: `${ratio * ((100 - activeRatio) / 100)}%` }}
                          />
                          <div className="ml-3 text-xs font-medium text-gray-500 dark:text-slate-400 tabular-nums">
                            {t('notifications.rules.ratio', { percent: ratio.toFixed(1) })}
                          </div>
                        </div>
                      </div>

                      {/* 计数 */}
                      <div className="col-span-12 sm:col-span-2 flex items-center justify-end gap-3">
                        <div className="text-right">
                          <div className="text-sm font-bold text-gray-900 dark:text-slate-100 tabular-nums leading-tight">
                            {evt.activeCount}
                            <span className="text-gray-400 dark:text-slate-500 font-normal">/{evt.count}</span>
                          </div>
                          <div className="text-[10px] text-gray-400 dark:text-slate-500 uppercase tracking-wider mt-0.5">
                            Active / Total
                          </div>
                        </div>
                        <div className={`p-2 rounded-lg ${colors.bg} ring-1 ${colors.ring}`}>
                          <TrendingUp className={`w-4 h-4 ${colors.text}`} />
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        </div>
      )}

      {/* 列表 Tab */}
      {activeTab === 'list' && (
        <div className="space-y-5">
          {/* 筛选栏 */}
          <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/80 dark:border-slate-700/60 shadow-sm overflow-hidden">
            {/* 顶部：搜索 + 动作按钮 */}
            <div className="p-4 border-b border-gray-100 dark:border-slate-700/60">
              <div className="flex flex-wrap items-center gap-3">
                <div className="flex-1 min-w-[160px] sm:min-w-[220px] max-w-md relative group">
                  <Search className="w-4 h-4 absolute left-3.5 top-1/2 -translate-y-1/2 text-gray-400 group-focus-within:text-primary-500 transition-colors" />
                  <input
                    type="text"
                    placeholder={t('notifications.rules.searchPlaceholder')}
                    value={searchInput}
                    onChange={(e) => handleSearchInput(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') triggerSearchNow();
                    }}
                    className="w-full pl-10 pr-10 py-2.5 bg-gray-50 dark:bg-slate-900/60 border border-transparent dark:border-slate-700/60 rounded-xl text-sm focus:outline-none focus:bg-white dark:focus:bg-slate-900 focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:text-slate-200 transition-all"
                  />
                  {searchInput && (
                    <button
                      onClick={clearSearch}
                      className="absolute right-2.5 top-1/2 -translate-y-1/2 p-1 rounded-md text-gray-400 hover:text-gray-600 hover:bg-gray-100 dark:hover:bg-slate-800 transition"
                      title={t('notifications.common.clearSearch')}
                    >
                      <X className="w-3.5 h-3.5" />
                    </button>
                  )}
                </div>

                {/* 状态胶囊选择 */}
                <div className="inline-flex p-1 bg-gray-100 dark:bg-slate-900/60 rounded-xl border border-gray-200 dark:border-slate-700/60">
                  {([
                    { labelKey: 'notifications.common.all', value: null },
                    { labelKey: 'notifications.common.enabled', value: true },
                    { labelKey: 'notifications.common.disabled', value: false },
                  ] satisfies { labelKey: TranslationKey; value: boolean | null }[]).map((opt) => {
                    const active = activeFilter === opt.value;
                    return (
                      <button
                        key={String(opt.value)}
                        onClick={() => { setActiveFilter(opt.value); setPage(1); }}
                        className={`relative px-3.5 py-1.5 text-xs font-semibold rounded-lg transition-all ${
                          active
                            ? 'bg-white dark:bg-slate-700 text-primary-700 dark:text-primary-300 shadow-sm ring-1 ring-black/5 dark:ring-white/5'
                            : 'text-gray-600 dark:text-slate-400 hover:text-gray-900 dark:hover:text-slate-200'
                        }`}
                      >
                        {t(opt.labelKey)}
                      </button>
                    );
                  })}
                </div>

                {/* 事件类型筛选 */}
                <div className="relative">
                  <select
                    value={eventTypeFilter || ''}
                    onChange={(e) => {
                      setEventTypeFilter(e.target.value || null);
                      setPage(1);
                    }}
                    className="appearance-none pl-3.5 pr-9 py-2.5 bg-gray-50 dark:bg-slate-900/60 border border-gray-200 dark:border-slate-700/60 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:text-slate-200 min-w-[120px] sm:min-w-[140px]"
                  >
                    <option value="">{t('notifications.common.allEvents')}</option>
                    {eventTypeOptions.map((opt) => (
                      <option key={opt.value} value={opt.value}>
                        {t(opt.labelKey)}
                      </option>
                    ))}
                  </select>
                </div>

                {/* 重置 */}
                {(search || eventTypeFilter || activeFilter !== null) && (
                  <button
                    onClick={() => { clearSearch(); setEventTypeFilter(null); setActiveFilter(null); setPage(1); }}
                    className="inline-flex items-center gap-1 px-3 py-2.5 text-xs font-medium text-gray-500 dark:text-slate-400 hover:text-gray-800 dark:hover:text-slate-200 hover:bg-gray-100 dark:hover:bg-slate-700/50 rounded-xl transition"
                  >
                    <FilterX className="w-3.5 h-3.5" />
                    <span className="hidden sm:inline">{t('notifications.common.resetFilters')}</span>
                  </button>
                )}

                <div className="flex-1 hidden sm:block" />

                <button
                  onClick={openTrash}
                  className="relative inline-flex items-center gap-1.5 px-3 sm:px-4 py-2.5 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 hover:bg-gray-50 dark:hover:bg-slate-700 text-gray-700 dark:text-slate-200 text-sm font-semibold rounded-xl shadow-sm transition"
                  title={t('notifications.common.trash')}
                >
                  <Trash2 className="w-4 h-4" />
                  <span className="hidden sm:inline">{t('notifications.common.trash')}</span>
                  {trashCount > 0 && (
                    <span className="ml-1 min-w-[20px] h-5 px-1.5 inline-flex items-center justify-center text-[11px] font-extrabold text-white bg-gradient-to-br from-red-500 to-rose-600 rounded-full shadow-sm">
                      {trashCount > 99 ? '99+' : trashCount}
                    </span>
                  )}
                </button>

                <button
                  onClick={openCreate}
                  className="inline-flex items-center gap-1.5 px-4 sm:px-5 py-2.5 bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 text-white text-sm font-semibold rounded-xl shadow-md shadow-primary-500/20 hover:shadow-lg hover:shadow-primary-500/30 transition-all active:scale-[0.98]"
                >
                  <Plus className="w-4 h-4" />
                  <span className="hidden sm:inline">{t('notifications.rules.newRule')}</span>
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
                    <button onClick={() => { setEventTypeFilter(null); setPage(1); }} className="ml-1 text-gray-400 hover:text-red-500">
                      <X className="w-3 h-3" />
                    </button>
                  </span>
                )}
                {activeFilter !== null && (
                  <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md border ${activeFilter ? 'bg-emerald-50 dark:bg-emerald-900/20 border-emerald-200 dark:border-emerald-900/40 text-emerald-700 dark:text-emerald-400' : 'bg-slate-50 dark:bg-slate-800 border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-300'}`}>
                    {activeFilter ? <CheckCircle2 className="w-3 h-3" /> : <XCircle className="w-3 h-3" />}
                    {activeFilter ? t('notifications.common.onlyEnabled') : t('notifications.common.onlyDisabled')}
                    <button onClick={() => { setActiveFilter(null); setPage(1); }} className={`ml-1 ${activeFilter ? 'text-emerald-400 hover:text-emerald-600' : 'text-slate-400 hover:text-red-500'}`}>
                      <X className="w-3 h-3" />
                    </button>
                  </span>
                )}
              </div>
            )}

            {/* 列表表格 */}
            <div className="relative">
              {loading ? (
                <div className="py-16 text-center">
                  <div className="inline-flex items-center gap-2 text-gray-400 text-sm">
                    <Activity className="w-4 h-4 animate-pulse" />
                    {t('common.shell.loading')}
                  </div>
                </div>
              ) : rules.length === 0 ? (
                <div className="py-20 flex flex-col items-center text-gray-400">
                  <div className="w-20 h-20 rounded-2xl bg-gradient-to-br from-gray-50 to-gray-100 dark:from-slate-800 dark:to-slate-900 border border-gray-100 dark:border-slate-700 flex items-center justify-center mb-4">
                    <Bell className="w-10 h-10 opacity-30" />
                  </div>
                  <p className="text-sm font-semibold text-gray-500 dark:text-slate-400">{t('notifications.rules.emptyListTitle')}</p>
                  <p className="text-xs mt-1">{t('notifications.rules.emptyListHint')}</p>
                </div>
              ) : (
                <>
                  {/* PC 端表格视图 */}
                  <div className="hidden md:block overflow-x-auto">
                    <table className="min-w-full">
                      <thead>
                        <tr className="bg-gray-50/60 dark:bg-slate-900/40 border-y border-gray-100 dark:border-slate-700/50">
                          <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em] w-[26%]">{t('notifications.rules.ruleName')}</th>
                          <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.eventType')}</th>
                          <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.rules.scope')}</th>
                          <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.rules.linkedTemplate')}</th>
                          <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.status')}</th>
                          <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em] whitespace-nowrap">{t('notifications.common.updatedAt')}</th>
                          <th className="text-right px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.actions')}</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-50 dark:divide-slate-800/80">
                        {rules.map((rule, idx) => {
                          const colors = getEventTypeColor(rule.eventType);
                          const zebra = idx % 2 === 0 ? 'bg-white dark:bg-slate-800' : 'bg-gray-50/40 dark:bg-slate-800/40';
                          return (
                            <tr
                              key={rule.id}
                              className={`${zebra} hover:bg-primary-50/40 dark:hover:bg-primary-900/10 transition-colors group`}
                            >
                              {/* 名称 */}
                              <td className="px-5 py-4">
                                <div className="flex items-center gap-3 min-w-0">
                                  <div className={`w-1 h-10 rounded-full ${colors.accent} flex-shrink-0 group-hover:w-1.5 transition-all`} />
                                  <div className="min-w-0">
                                    <div className="text-sm font-semibold text-gray-900 dark:text-slate-100 truncate flex items-center gap-2">
                                      {rule.name}
                                      {rule.isActive && (
                                        <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 shadow-[0_0_0_3px_rgba(16,185,129,0.15)]" title={t('format.status.active')} />
                                      )}
                                    </div>
                                    <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5 flex items-center gap-1.5">
                                      <Settings2 className="w-3 h-3 opacity-60" />
                                      ID: <code className="font-mono">#{rule.id}</code>
                                      {rule.userId && (
                                        <>
                                          <span className="opacity-40">·</span>
                                          <User className="w-3 h-3 opacity-60" />
                                          {t('notifications.rules.assignedUser')}
                                        </>
                                      )}
                                    </div>
                                  </div>
                                </div>
                              </td>

                              {/* 事件类型 */}
                              <td className="px-5 py-4">
                                <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-bold ring-1 ${colors.ring} ${colors.bg} ${colors.text}`}>
                                  <Zap className="w-3 h-3" />
                                  {getEventTypeLabel(t, rule.eventType)}
                                </span>
                              </td>

                              {/* 适用范围 */}
                              <td className="px-5 py-4">
                                <div className="flex flex-wrap gap-1.5">
                                  <div className="inline-flex items-center gap-1 text-[12px] text-gray-600 dark:text-slate-300 px-2 py-0.5 rounded-md bg-gray-50 dark:bg-slate-900/50 border border-gray-100 dark:border-slate-700/50">
                                    <Mailbox className="w-3 h-3 text-gray-400" />
                                    <span className="truncate max-w-[140px]">{rule.mailboxName || t('notifications.common.allMailboxes')}</span>
                                  </div>
                                </div>
                              </td>

                              {/* 关联模板 */}
                              <td className="px-5 py-4">
                                {rule.templateName ? (
                                  <div className="inline-flex items-center gap-1.5 px-2 py-1 rounded-md bg-indigo-50 dark:bg-indigo-900/20 text-indigo-700 dark:text-indigo-400 border border-indigo-100 dark:border-indigo-900/40 text-[12px] font-medium max-w-[200px]">
                                    <FileText className="w-3 h-3 flex-shrink-0" />
                                    <span className="truncate">{rule.templateName}</span>
                                  </div>
                                ) : (
                                  <div className="inline-flex items-center gap-1 text-[12px] text-gray-400 dark:text-slate-500">
                                    <AlertTriangle className="w-3 h-3 opacity-60" />
                                    {t('notifications.common.unbound')}
                                  </div>
                                )}
                              </td>

                              {/* 状态 */}
                              <td className="px-5 py-4">
                                <button
                                  onClick={() => handleToggleActive(rule)}
                                  className={`relative inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-[11px] font-bold transition-all ${
                                    rule.isActive
                                      ? 'bg-emerald-50 dark:bg-emerald-900/25 text-emerald-700 dark:text-emerald-400 ring-1 ring-emerald-500/30 hover:bg-emerald-100 dark:hover:bg-emerald-900/40 shadow-sm'
                                      : 'bg-slate-100 dark:bg-slate-700/60 text-slate-500 dark:text-slate-400 ring-1 ring-slate-300/60 dark:ring-slate-600/60 hover:bg-slate-200 dark:hover:bg-slate-700'
                                  }`}
                                  title={rule.isActive ? t('notifications.rules.toggleToDisable') : t('notifications.rules.toggleToEnable')}
                                >
                                  {rule.isActive ? (
                                    <span className="relative flex w-2 h-2">
                                      <span className="absolute inline-flex w-full h-full rounded-full bg-emerald-400 opacity-60 animate-ping" />
                                      <span className="relative inline-flex w-2 h-2 rounded-full bg-emerald-500" />
                                    </span>
                                  ) : (
                                    <XCircle className="w-3 h-3" />
                                  )}
                                  {rule.isActive ? t('notifications.common.enabled') : t('notifications.common.disabled')}
                                </button>
                              </td>

                              {/* 更新时间 */}
                              <td className="px-5 py-4 text-[12px] text-gray-500 dark:text-slate-400 whitespace-nowrap tabular-nums">
                                {rule.updatedAt ? formatDateTime(rule.updatedAt) : '—'}
                              </td>

                              {/* 操作 */}
                              <td className="px-5 py-4">
                                <div className="flex items-center justify-end gap-0.5 p-1 -m-1 rounded-xl bg-white/0 group-hover:bg-gray-50 dark:group-hover:bg-slate-700/40 transition-colors">
                                  <button
                                    onClick={() => handleDuplicate(rule)}
                                    className="p-2 text-gray-400 hover:text-purple-600 hover:bg-purple-50 dark:hover:bg-purple-900/30 dark:hover:text-purple-400 rounded-lg transition"
                                    title={t('notifications.rules.copyRule')}
                                  >
                                    <Copy className="w-4 h-4" />
                                  </button>
                                  <button
                                    onClick={() => openEdit(rule)}
                                    className="p-2 text-gray-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/30 dark:hover:text-primary-400 rounded-lg transition"
                                    title={t('notifications.rules.editRule')}
                                  >
                                    <Edit2 className="w-4 h-4" />
                                  </button>
                                  <button
                                    onClick={() => setDeleteTarget(rule)}
                                    className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30 dark:hover:text-red-400 rounded-lg transition"
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
                    {rules.map((rule) => {
                      const colors = getEventTypeColor(rule.eventType);
                      return (
                        <div key={rule.id} className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700/30 space-y-3">
                          <div className="flex items-start justify-between gap-2">
                            <div className="flex items-center gap-2.5 min-w-0 flex-1">
                              <div className={`shrink-0 w-9 h-9 rounded-xl ${colors.bg} ${colors.ring} ring-1 flex items-center justify-center`}>
                                <Bell className={`w-4 h-4 ${colors.text}`} />
                              </div>
                              <div className="min-w-0">
                                <div className="text-sm font-bold text-gray-900 dark:text-slate-100 truncate flex items-center gap-1.5">
                                  {rule.name}
                                  {rule.isActive && (
                                    <span className="w-1.5 h-1.5 rounded-full bg-emerald-500" title={t('format.status.active')} />
                                  )}
                                </div>
                                <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5 flex items-center gap-1.5">
                                  <span>ID · #{rule.id}</span>
                                  {rule.userId && (
                                    <>
                                      <span className="opacity-40">·</span>
                                      <User className="w-3 h-3 opacity-60" />
                                      <span>{t('notifications.rules.assignedUser')}</span>
                                    </>
                                  )}
                                </div>
                              </div>
                            </div>
                            <button
                              onClick={() => handleToggleActive(rule)}
                              className={`inline-flex items-center gap-1 px-2 py-1 rounded-full text-[11px] font-bold flex-shrink-0 ${
                                rule.isActive
                                  ? 'bg-emerald-50 dark:bg-emerald-900/25 text-emerald-700 dark:text-emerald-400 ring-1 ring-emerald-500/30'
                                  : 'bg-slate-100 dark:bg-slate-700/60 text-slate-500 dark:text-slate-400 ring-1 ring-slate-300/60 dark:ring-slate-600/60'
                              }`}
                              title={rule.isActive ? t('notifications.rules.toggleToDisable') : t('notifications.rules.toggleToEnable')}
                            >
                              {rule.isActive ? t('notifications.common.enabled') : t('notifications.common.disabled')}
                            </button>
                          </div>

                          <div className="flex flex-wrap items-center gap-1.5">
                            <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-xl text-[11px] font-bold ring-1 ${colors.bg} ${colors.text} ${colors.ring}`}>
                              <span className={`w-1.5 h-1.5 rounded-full ${colors.accent}`} />
                              {getEventTypeLabel(t, rule.eventType)}
                            </span>
                          </div>

                          <div className="grid grid-cols-1 gap-2 text-xs">
                            <div className="flex items-center gap-1.5 text-gray-600 dark:text-slate-300 px-2 py-1.5 rounded-md bg-gray-50 dark:bg-slate-900/50 border border-gray-100 dark:border-slate-700/50">
                              <Mailbox className="w-3 h-3 text-gray-400 flex-shrink-0" />
                              <span className="truncate">{rule.mailboxName || t('notifications.common.allMailboxes')}</span>
                            </div>
                            <div>
                              {rule.templateName ? (
                                <div className="inline-flex items-center gap-1.5 px-2 py-1.5 rounded-md bg-indigo-50 dark:bg-indigo-900/20 text-indigo-700 dark:text-indigo-400 border border-indigo-100 dark:border-indigo-900/40 font-medium w-full">
                                  <FileText className="w-3 h-3 flex-shrink-0" />
                                  <span className="truncate">{rule.templateName}</span>
                                </div>
                              ) : (
                                <div className="inline-flex items-center gap-1 text-gray-400 dark:text-slate-500 px-2 py-1.5">
                                  <AlertTriangle className="w-3 h-3 opacity-60" />
                                  {t('notifications.common.unboundTemplate')}
                                </div>
                              )}
                            </div>
                          </div>

                          <div className="flex items-center justify-between pt-1">
                            <div className="text-[11px] text-gray-400 dark:text-slate-500 font-mono">
                              {rule.updatedAt ? formatDateTime(rule.updatedAt) : '—'}
                            </div>
                            <div className="flex items-center gap-1">
                              <button
                                onClick={() => handleDuplicate(rule)}
                                className="p-2 text-gray-500 dark:text-slate-400 hover:text-purple-600 hover:bg-purple-50 dark:hover:bg-purple-900/30 rounded-xl transition"
                                title={t('notifications.rules.copyRule')}
                              >
                                <Copy className="w-4 h-4" />
                              </button>
                              <button
                                onClick={() => openEdit(rule)}
                                className="p-2 text-gray-500 dark:text-slate-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition"
                                title={t('notifications.rules.editRule')}
                              >
                                <Edit2 className="w-4 h-4" />
                              </button>
                              <button
                                onClick={() => setDeleteTarget(rule)}
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

              {/* 分页 */}
              {totalItems > 0 && (
                <div className="px-5 py-4 border-t border-gray-100 dark:border-slate-700/60 flex items-center justify-between flex-wrap gap-3 bg-gray-50/40 dark:bg-slate-900/20">
                  <div className="text-xs text-gray-500 dark:text-slate-400 flex items-center gap-1.5">
                    {t('notifications.common.paginationPrefix')}
                    <span className="inline-flex items-center justify-center w-5 h-5 rounded-md bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-[11px] font-bold">
                      {totalItems}
                    </span>
                    {t('notifications.common.paginationMiddle')}
                    <b className="text-gray-700 dark:text-slate-200 tabular-nums">{page}</b> / {totalPages}
                    {t('notifications.common.paginationSuffix')}
                  </div>
                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => setPage(Math.max(1, page - 1))}
                      disabled={page <= 1}
                      className="inline-flex items-center gap-1 px-3.5 py-2 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl text-xs font-semibold disabled:opacity-40 disabled:cursor-not-allowed hover:bg-gray-50 dark:hover:bg-slate-700 dark:text-slate-200 shadow-sm transition"
                    >
                      <ChevronLeft className="w-4 h-4" />
                      {t('notifications.common.previousPage')}
                    </button>
                    <button
                      onClick={() => setPage(Math.min(totalPages, page + 1))}
                      disabled={page >= totalPages}
                      className="inline-flex items-center gap-1 px-3.5 py-2 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl text-xs font-semibold disabled:opacity-40 disabled:cursor-not-allowed hover:bg-gray-50 dark:hover:bg-slate-700 dark:text-slate-200 shadow-sm transition"
                    >
                      {t('notifications.common.nextPage')}
                      <ChevronRight className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* 编辑器弹窗 */}
      <Modal
        open={showEditor}
        title={
          <div className="flex items-center gap-3 -mx-4 px-6 py-3 rounded-b-xl bg-gradient-to-r from-primary-600 via-indigo-600 to-purple-600 text-white shadow-md">
            <div className="relative p-2 rounded-lg bg-white/15 ring-1 ring-white/20 backdrop-blur-sm">
              <Bell className="w-5 h-5 text-white" />
              <span className="absolute -top-1 -right-1 w-3 h-3 rounded-full bg-white animate-ping opacity-50" />
            </div>
            <div className="flex-1 min-w-0">
              <h2 className="text-base font-extrabold tracking-tight">
                {editingRule ? t('notifications.rules.modalEditTitle') : t('notifications.rules.modalCreateTitle')}
              </h2>
              <p className="text-[11px] text-white/75 mt-0.5">
                {editingRule ? t('notifications.rules.modalEditSubtitle') : t('notifications.rules.modalCreateSubtitle')}
              </p>
            </div>
            {formData.isActive && (
              <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-full bg-emerald-400/20 text-emerald-200 border border-emerald-300/30 text-[11px] font-bold">
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-300 animate-pulse" />
                {t('notifications.rules.activeOnSave')}
              </span>
            )}
          </div>
        }
        onClose={() => setShowEditor(false)}
        size="lg"
        footer={
          <div className="-mx-4 px-6 py-4 -mb-4 mt-4 bg-gray-50/70 dark:bg-slate-800/50 border-t border-gray-100 dark:border-slate-700/60 flex items-center justify-between gap-3">
            <div className="flex items-center gap-1.5 text-[11px] text-gray-500 dark:text-slate-400">
              <Shield className="w-3.5 h-3.5 text-primary-500" />
              {t('notifications.rules.auditNotice')}
            </div>
            <div className="flex items-center gap-2">
              <button
                onClick={() => setShowEditor(false)}
                disabled={saving}
                className="px-4.5 py-2.5 text-sm font-semibold text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 shadow-sm transition-all"
              >
                <span className="inline-flex items-center gap-1.5">
                  <X className="w-4 h-4" />
                  {t('common.cancel')}
                </span>
              </button>
              <button
                onClick={handleSave}
                disabled={saving}
                className="relative px-6 py-2.5 text-sm font-bold text-white bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 rounded-xl shadow-md shadow-primary-500/30 hover:shadow-lg hover:shadow-primary-500/40 transition-all disabled:opacity-60 disabled:cursor-not-allowed active:scale-[0.98]"
              >
                {saving ? (
                  <span className="inline-flex items-center gap-2">
                    <Activity className="w-4 h-4 animate-pulse" />
                    {t('notifications.common.saving')}
                  </span>
                ) : (
                  <span className="inline-flex items-center gap-1.5">
                    <Save className="w-4 h-4" />
                    {editingRule ? t('notifications.rules.saveChanges') : t('notifications.rules.createRule')}
                  </span>
                )}
              </button>
            </div>
          </div>
        }
      >
        <div className="space-y-5 pt-1">
          {formError && (
            <div className="relative p-4 rounded-xl bg-gradient-to-br from-red-50 to-rose-50 dark:from-red-950/40 dark:to-rose-950/20 border border-red-200 dark:border-red-900/40 text-red-700 dark:text-red-400 text-sm flex items-start gap-3 overflow-hidden">
              <div className="absolute -top-10 -right-10 w-28 h-28 rounded-full bg-red-400/10 blur-2xl" />
              <AlertTriangle className="w-5 h-5 text-red-500 flex-shrink-0 mt-0.5" />
              <div className="flex-1">
                <div className="text-sm font-bold mb-0.5">{t('notifications.rules.formErrorTitle')}</div>
                <div className="text-[13px] opacity-90">{formError}</div>
              </div>
            </div>
          )}

          {/* 基础信息卡片 */}
          <div className="relative bg-gradient-to-br from-gray-50/70 to-white dark:from-slate-800/60 dark:to-slate-800 rounded-2xl border border-gray-200 dark:border-slate-700/60 p-5 shadow-sm overflow-hidden">
            <div className="absolute top-0 right-0 w-40 h-40 rounded-full bg-gradient-to-br from-primary-200/30 to-indigo-200/30 dark:from-primary-500/5 dark:to-indigo-500/5 blur-3xl pointer-events-none" />
            <div className="relative">
              <div className="flex items-center gap-2.5 mb-4">
                <div className="p-2 rounded-xl bg-white dark:bg-slate-900 shadow-sm ring-1 ring-gray-200 dark:ring-slate-700">
                  <FileEdit className="w-4 h-4 text-primary-600 dark:text-primary-400" />
                </div>
                <div>
                  <h3 className="text-sm font-bold text-gray-900 dark:text-slate-100">{t('notifications.rules.sectionBasic')}</h3>
                  <p className="text-[11px] text-gray-500 dark:text-slate-400">{t('notifications.rules.sectionBasicHint')}</p>
                </div>
              </div>

              {/* 规则名称 */}
              <div className="mb-4">
                <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1.5 flex items-center gap-1">
                   {t('notifications.rules.ruleName')} <span className="text-red-500 text-base leading-none">*</span>
                  <span className="text-[10px] text-gray-400 dark:text-slate-500 font-normal ml-1">{t('notifications.rules.nameHint')}</span>
                </label>
                <div className="relative group">
                  <Hash className="w-4 h-4 absolute left-3.5 top-1/2 -translate-y-1/2 text-gray-400 group-focus-within:text-primary-500 transition" />
                  <input
                    type="text"
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    className="w-full pl-10 pr-4 py-2.5 bg-white dark:bg-slate-900/60 border border-gray-200 dark:border-slate-700 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-500 dark:text-slate-200 placeholder:text-gray-400 transition-all"
                    placeholder={t('notifications.rules.namePlaceholder')}
                  />
                </div>
              </div>

              {/* 事件类型 - 胶囊选择 */}
              <div>
                <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-2.5 flex items-center gap-1">
                  <Zap className="w-3.5 h-3.5 text-amber-500" />
                  {t('notifications.common.eventType')} <span className="text-red-500 text-base leading-none">*</span>
                  <span className="text-[10px] text-gray-400 dark:text-slate-500 font-normal ml-1">{t('notifications.rules.eventTypeHint')}</span>
                </label>
                <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
                  {eventTypeOptions.map((opt) => {
                    const colors = getEventTypeColor(opt.value);
                    const selected = formData.eventType === opt.value;
                    return (
                      <button
                        key={opt.value}
                        type="button"
                        onClick={() => setFormData({ ...formData, eventType: opt.value })}
                        className={`relative group flex items-start gap-2.5 p-3 rounded-xl border-2 transition-all text-left ${
                          selected
                            ? `${colors.bg} ${colors.text} ${colors.ring.replace('ring-', 'border-').replace('/20', '')} bg-opacity-100 border-current shadow-md scale-[1.01]`
                            : 'bg-white dark:bg-slate-900/40 border-gray-200 dark:border-slate-700/60 text-gray-700 dark:text-slate-300 hover:border-gray-300 dark:hover:border-slate-600 hover:bg-gray-50 dark:hover:bg-slate-800/60'
                        }`}
                      >
                        <div className={`relative flex-shrink-0 mt-0.5 w-6 h-6 rounded-lg flex items-center justify-center ${selected ? '' : 'bg-gray-100 dark:bg-slate-700/80'}`}>
                          <div className={`absolute inset-0 rounded-lg ${colors.accent} ${selected ? 'opacity-100' : 'opacity-0 group-hover:opacity-20'} transition-opacity`} />
                          <Zap className={`w-3.5 h-3.5 relative ${selected ? 'text-white' : colors.text}`} />
                        </div>
                        <div className="min-w-0 flex-1">
                          <div className={`text-[12.5px] font-bold truncate ${selected ? colors.text : ''}`}>
                            {t(opt.labelKey)}
                          </div>
                          <div className={`text-[10.5px] mt-0.5 truncate ${selected ? colors.text + ' opacity-80' : 'text-gray-400 dark:text-slate-500'}`}>
                            {opt.value}
                          </div>
                        </div>
                        {selected && (
                          <div className="absolute top-1.5 right-1.5 w-5 h-5 rounded-full bg-white/80 dark:bg-slate-900/80 flex items-center justify-center shadow-sm">
                            <CheckCircle2 className={`w-3.5 h-3.5 ${colors.text}`} />
                          </div>
                        )}
                      </button>
                    );
                  })}
                </div>
              </div>
            </div>
          </div>

          {/* 配置范围卡片 */}
          <div className="relative bg-white dark:bg-slate-800 rounded-2xl border border-gray-200 dark:border-slate-700/60 p-5 shadow-sm overflow-hidden">
            <div className="absolute top-0 left-0 w-32 h-32 rounded-full bg-gradient-to-br from-sky-200/20 to-emerald-200/20 dark:from-sky-500/5 dark:to-emerald-500/5 blur-3xl pointer-events-none" />
            <div className="relative">
              <div className="flex items-center gap-2.5 mb-4">
                <div className="p-2 rounded-xl bg-gradient-to-br from-sky-50 to-emerald-50 dark:from-sky-900/40 dark:to-emerald-900/40 shadow-sm ring-1 ring-sky-500/20">
                  <Settings2 className="w-4 h-4 text-sky-600 dark:text-sky-400" />
                </div>
                <div>
                  <h3 className="text-sm font-bold text-gray-900 dark:text-slate-100">{t('notifications.rules.scope')}</h3>
                  <p className="text-[11px] text-gray-500 dark:text-slate-400">{t('notifications.rules.sectionScopeHint')}</p>
                </div>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-5">
                {/* 适用邮箱 */}
                <div>
                  <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1.5 flex items-center gap-1">
                    <Mailbox className="w-3.5 h-3.5 text-gray-400" />
                    {t('notifications.rules.mailboxLabel')}
                    <span className="text-[10px] text-gray-400 dark:text-slate-500 font-normal ml-1">{t('notifications.rules.mailboxHint')}</span>
                  </label>
                  <div className="relative">
                    <select
                      value={formData.mailboxId || ''}
                      onChange={(e) =>
                        setFormData({ ...formData, mailboxId: e.target.value ? Number(e.target.value) : null })
                      }
                      className="w-full appearance-none pl-10 pr-10 py-2.5 bg-gray-50 dark:bg-slate-900/50 border border-gray-200 dark:border-slate-700/60 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-500 dark:text-slate-200 transition-all"
                    >
                      <option value="">{t('notifications.rules.allMailboxesOption')}</option>
                      {mailboxes.map((mb) => (
                        <option key={mb.id} value={mb.id}>
                          📬 {mb.name}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>

                {/* 通知模板 */}
                <div>
                  <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1.5 flex items-center gap-1">
                    <FileText className="w-3.5 h-3.5 text-gray-400" />
                    {t('notifications.rules.templateLabel')}
                    <span className="text-[10px] text-gray-400 dark:text-slate-500 font-normal ml-1">{t('notifications.rules.templateHint')}</span>
                  </label>
                  <div className="relative">
                    <select
                      value={formData.templateId || ''}
                      onChange={(e) =>
                        setFormData({ ...formData, templateId: e.target.value ? Number(e.target.value) : null })
                      }
                      className="w-full appearance-none pl-10 pr-10 py-2.5 bg-gray-50 dark:bg-slate-900/50 border border-gray-200 dark:border-slate-700/60 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500/30 focus:border-indigo-500 dark:text-slate-200 transition-all"
                    >
                      <option value="">{t('notifications.rules.noTemplateOption')}</option>
                      {templates.map((tpl) => (
                        <option key={tpl.id} value={tpl.id}>
                          📄 {tpl.name}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>
              </div>
            </div>
          </div>

          {/* 开关区：启用规则 */}
          <div
            className={`relative p-4 rounded-2xl border-2 cursor-pointer transition-all ${
              formData.isActive
                ? 'bg-gradient-to-br from-emerald-50 to-teal-50 dark:from-emerald-950/30 dark:to-teal-950/20 border-emerald-200 dark:border-emerald-800/50'
                : 'bg-gray-50 dark:bg-slate-800/60 border-gray-200 dark:border-slate-700/60 hover:border-gray-300 dark:hover:border-slate-600'
            }`}
            onClick={() => setFormData({ ...formData, isActive: !formData.isActive })}
          >
            <div className="flex items-center justify-between gap-4">
              <div className="flex items-center gap-3 min-w-0">
                <div className={`relative w-14 h-8 rounded-full transition-all duration-300 ${
                  formData.isActive
                    ? 'bg-gradient-to-r from-emerald-500 to-teal-500 shadow-lg shadow-emerald-500/30'
                    : 'bg-gray-300 dark:bg-slate-600'
                }`}>
                  <div className={`absolute top-0.5 w-7 h-7 rounded-full bg-white shadow-md transition-all duration-300 ${
                    formData.isActive ? 'left-[calc(100%-2.25rem+0.125rem)]' : 'left-0.5'
                  }`} />
                </div>
                <div className="min-w-0">
                  <div className={`text-sm font-bold ${formData.isActive ? 'text-emerald-800 dark:text-emerald-300' : 'text-gray-700 dark:text-slate-300'}`}>
                    {t('notifications.rules.enableRule')}
                  </div>
                  <p className="text-[11px] text-gray-500 dark:text-slate-400 mt-0.5">
                    {formData.isActive ? t('notifications.rules.enableRuleOn') : t('notifications.rules.enableRuleOff')}
                  </p>
                </div>
              </div>
              <div className={`relative flex-shrink-0 w-10 h-10 rounded-xl flex items-center justify-center ${
                formData.isActive
                  ? 'bg-emerald-500 text-white shadow-lg shadow-emerald-500/30'
                  : 'bg-gray-200 dark:bg-slate-700 text-gray-400 dark:text-slate-500'
              }`}>
                {formData.isActive ? (
                  <CheckCircle2 className="w-5 h-5" />
                ) : (
                  <XCircle className="w-5 h-5" />
                )}
              </div>
            </div>
          </div>

          {/* 预览小卡片：如果已选事件类型 */}
          {formData.eventType && (
            <div className="relative p-4 rounded-2xl bg-gradient-to-r from-gray-50 to-slate-50 dark:from-slate-800 dark:to-slate-800/70 border border-gray-200 dark:border-slate-700/60 overflow-hidden">
              <div className="absolute inset-0 opacity-[0.04] bg-[radial-gradient(circle_at_10%_20%,_#6366f1_0%,_transparent_40%),radial-gradient(circle_at_90%_80%,_#06b6d4_0%,_transparent_40%)]" />
              <div className="relative flex items-start gap-3">
                <div className="p-2 rounded-lg bg-white dark:bg-slate-900 border border-gray-100 dark:border-slate-700 shadow-sm">
                  <Eye className="w-4 h-4 text-indigo-500" />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="text-xs font-bold text-gray-700 dark:text-slate-300 mb-1.5 uppercase tracking-wider">{t('notifications.rules.previewTitle')}</div>
                  <div className="grid grid-cols-2 sm:grid-cols-4 gap-2.5 text-[11.5px]">
                    <div className="p-2 rounded-lg bg-white dark:bg-slate-900/60 border border-gray-100 dark:border-slate-700/50">
                      <div className="text-[10px] text-gray-400 dark:text-slate-500 uppercase tracking-wider mb-1">{t('notifications.common.name')}</div>
                      <div className="font-bold text-gray-800 dark:text-slate-200 truncate">{formData.name || t('notifications.rules.notSet')}</div>
                    </div>
                    <div className={`p-2 rounded-lg border ${getEventTypeColor(formData.eventType).bg} ${getEventTypeColor(formData.eventType).ring} border-transparent`}>
                      <div className="text-[10px] text-gray-500 dark:text-slate-400 uppercase tracking-wider mb-1">{t('notifications.rules.previewEvent')}</div>
                      <div className={`font-bold ${getEventTypeColor(formData.eventType).text} truncate`}>
                        {getEventTypeLabel(t, formData.eventType)}
                      </div>
                    </div>
                    <div className="p-2 rounded-lg bg-white dark:bg-slate-900/60 border border-gray-100 dark:border-slate-700/50">
                      <div className="text-[10px] text-gray-400 dark:text-slate-500 uppercase tracking-wider mb-1">{t('notifications.rules.scope')}</div>
                      <div className="font-bold text-gray-800 dark:text-slate-200 truncate">
                        {mailboxes.find((mb) => mb.id === formData.mailboxId)?.name || t('notifications.common.allMailboxes')}
                      </div>
                    </div>
                    <div className={`p-2 rounded-lg border ${formData.isActive ? 'bg-emerald-50 dark:bg-emerald-950/30 border-emerald-100 dark:border-emerald-900/40' : 'bg-slate-100 dark:bg-slate-700/60 border-slate-200 dark:border-slate-600/60'}`}>
                      <div className="text-[10px] text-gray-400 dark:text-slate-500 uppercase tracking-wider mb-1">{t('notifications.rules.previewStatus')}</div>
                      <div className={`font-bold ${formData.isActive ? 'text-emerald-700 dark:text-emerald-400' : 'text-slate-600 dark:text-slate-400'}`}>
                        {formData.isActive ? t('notifications.rules.previewStatusActive') : t('notifications.common.disabled')}
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>
      </Modal>

      {/* 删除确认 */}
      <ConfirmDialog
        open={!!deleteTarget}
        title={t('notifications.common.moveToTrash')}
        message={deleteTarget ? t('notifications.rules.deleteMessage', { name: deleteTarget.name }) : ''}
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
          title={
            <div className="flex items-center gap-3 -mx-4 px-6 py-3 rounded-b-xl bg-gradient-to-r from-rose-600 via-red-600 to-orange-600 text-white shadow-md">
              <div className="relative p-2 rounded-lg bg-white/15 ring-1 ring-white/20 backdrop-blur-sm">
                <Archive className="w-5 h-5 text-white" />
              </div>
              <div className="flex-1 min-w-0">
                <h2 className="text-base font-extrabold tracking-tight flex items-center gap-2">
                  {t('notifications.rules.trashTitle')}
                  {trashCount > 0 && (
                    <span className="inline-flex items-center justify-center min-w-[24px] h-6 px-2 rounded-full bg-white/20 border border-white/20 text-[11px] font-extrabold tabular-nums">
                      {trashCount}
                    </span>
                  )}
                </h2>
                <p className="text-[11px] text-white/75 mt-0.5">
                  {t('notifications.rules.trashSubtitle')}
                </p>
              </div>
              {trashCount > 0 && (
                <button
                  onClick={clearTrashSearch}
                  className="inline-flex items-center gap-1 px-2.5 py-1 rounded-full bg-white/10 hover:bg-white/20 border border-white/15 text-[11px] font-semibold text-white/90 transition"
                >
                  <RefreshCw className="w-3.5 h-3.5" />
                  {t('notifications.common.refresh')}
                </button>
              )}
            </div>
          }
          size="lg"
          footer={
            <div className="-mx-4 px-6 py-4 -mb-4 mt-4 bg-gray-50/70 dark:bg-slate-800/50 border-t border-gray-100 dark:border-slate-700/60 flex items-center justify-between w-full gap-3">
              <button
                onClick={() => setClearTrashOpen(true)}
                disabled={trashActionLoading || trashCount === 0}
                className="group relative inline-flex items-center gap-2 px-4 py-2.5 text-sm font-bold text-white bg-gradient-to-br from-red-600 to-rose-600 hover:from-red-700 hover:to-rose-700 rounded-xl shadow-md shadow-red-500/20 hover:shadow-lg hover:shadow-red-500/30 disabled:opacity-40 disabled:cursor-not-allowed disabled:shadow-none transition-all"
              >
                <Trash2 className="w-4 h-4 group-hover:scale-110 transition-transform" />
                {t('notifications.common.emptyTrash')}
                {trashCount > 0 && (
                  <span className="ml-0.5 inline-flex items-center justify-center min-w-[20px] h-5 px-1.5 rounded-full bg-white/20 text-[11px] font-extrabold tabular-nums">
                    {trashCount}
                  </span>
                )}
              </button>
              <div className="flex items-center gap-2">
                <div className="text-[11px] text-gray-400 dark:text-slate-500 hidden sm:inline-flex items-center gap-1">
                  <Shield className="w-3.5 h-3.5 opacity-60" />
                  {t('notifications.rules.usedSpace', { count: trashCount })}
                </div>
                <button
                  onClick={() => setTrashOpen(false)}
                  className="inline-flex items-center gap-1.5 px-4.5 py-2.5 text-sm font-semibold text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 shadow-sm transition-all"
                >
                  <X className="w-4 h-4" />
                  {t('common.close')}
                </button>
              </div>
            </div>
          }
        >
          <div className="space-y-4 pt-1">
            {/* 搜索框 */}
            <div className="relative group">
              <Search className="w-4 h-4 absolute left-4 top-1/2 -translate-y-1/2 text-gray-400 group-focus-within:text-primary-500 transition" />
              <input
                type="text"
                placeholder={t('notifications.rules.trashSearchPlaceholder')}
                value={trashKeywordInput}
                onChange={(e) => handleTrashSearchInput(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') triggerTrashSearchNow();
                }}
                className="w-full pl-11 pr-4 py-3 bg-gray-50 dark:bg-slate-900/50 border border-gray-200 dark:border-slate-700/60 rounded-2xl text-sm focus:outline-none focus:ring-2 focus:ring-primary-500/25 focus:border-primary-500 dark:text-slate-200 placeholder:text-gray-400 transition-all"
              />
              {trashKeywordInput && (
                <button
                  onClick={clearTrashSearch}
                  className="absolute right-3 top-1/2 -translate-y-1/2 p-1 rounded-lg text-gray-400 hover:text-gray-700 hover:bg-gray-200 dark:hover:bg-slate-700 dark:hover:text-slate-300 transition"
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              )}
            </div>

            {/* 内容列表 */}
            <div className="relative rounded-2xl border border-gray-200 dark:border-slate-700/60 overflow-hidden shadow-sm">
              {trashLoading ? (
                <div className="py-16 flex flex-col items-center text-gray-400">
                  <Activity className="w-6 h-6 animate-pulse mb-2" />
                  <div className="text-sm font-medium">{t('notifications.rules.trashLoading')}</div>
                </div>
              ) : (trashList?.data ?? []).length === 0 ? (
                <div className="py-20 flex flex-col items-center text-center px-4">
                  <div className="relative mb-5">
                    <div className="w-24 h-24 rounded-3xl bg-gradient-to-br from-rose-50 to-orange-50 dark:from-rose-950/30 dark:to-orange-950/20 border border-rose-100 dark:border-rose-900/40 flex items-center justify-center">
                      <Sparkles className="w-10 h-10 text-rose-400/80" />
                    </div>
                    <div className="absolute -bottom-2 -right-2 w-10 h-10 rounded-2xl bg-white dark:bg-slate-800 border border-gray-100 dark:border-slate-700 flex items-center justify-center shadow-md">
                      <CheckCircle2 className="w-5 h-5 text-emerald-500" />
                    </div>
                  </div>
                  <h3 className="text-base font-bold text-gray-800 dark:text-slate-200">{t('notifications.rules.trashEmptyTitle')}</h3>
                  <p className="text-xs text-gray-500 dark:text-slate-400 mt-2 max-w-xs">
                    {trashQuery.keyword ? t('notifications.rules.trashEmptySearch') : t('notifications.rules.trashEmptyAllCleared')}
                  </p>
                </div>
              ) : (
                <>
                  {/* PC 端表格视图 */}
                  <div className="hidden md:block overflow-x-auto">
                    <table className="min-w-full">
                      <thead>
                        <tr className="bg-gradient-to-r from-gray-50/80 to-rose-50/30 dark:from-slate-800 dark:to-rose-950/20 border-b border-gray-100 dark:border-slate-700/50">
                          <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.rules.ruleName')}</th>
                          <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.eventType')}</th>
                          <th className="text-left px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em] whitespace-nowrap">{t('notifications.common.deletedAt')}</th>
                          <th className="text-right px-5 py-3 text-[11px] font-bold text-gray-500 dark:text-slate-400 uppercase tracking-[0.08em]">{t('notifications.common.actions')}</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-50 dark:divide-slate-800/80">
                        {(trashList?.data ?? []).map((rule, idx) => {
                          const colors = getEventTypeColor(rule.eventType);
                          const zebra = idx % 2 === 0 ? 'bg-white dark:bg-slate-800' : 'bg-gray-50/40 dark:bg-slate-800/40';
                          return (
                            <tr
                              key={rule.id}
                              className={`${zebra} hover:bg-rose-50/40 dark:hover:bg-rose-900/10 transition-colors group`}
                            >
                              <td className="px-5 py-4">
                                <div className="flex items-center gap-3 min-w-0">
                                  <div className={`w-1 h-10 rounded-full ${colors.accent} opacity-70 group-hover:opacity-100 flex-shrink-0`} />
                                  <div className="min-w-0">
                                    <div className="flex items-center gap-2">
                                      <div className="text-sm font-semibold text-gray-900 dark:text-slate-100 truncate line-through decoration-1 decoration-rose-300/60 dark:decoration-rose-800/50">
                                        {rule.name}
                                      </div>
                                    </div>
                                    {rule.templateName && (
                                      <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5 flex items-center gap-1.5">
                                        <FileText className="w-3 h-3 opacity-60" />
                                        {t('notifications.rules.templatePrefix')}<span className="truncate">{rule.templateName}</span>
                                      </div>
                                    )}
                                  </div>
                                </div>
                              </td>
                              <td className="px-5 py-4">
                                <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[11px] font-bold ring-1 opacity-85 ${colors.ring} ${colors.bg} ${colors.text}`}>
                                  <Zap className="w-3 h-3" />
                                  {getEventTypeOptionLabel(t, rule.eventType)}
                                </span>
                              </td>
                              <td className="px-5 py-4 whitespace-nowrap">
                                <div className="text-[12px] tabular-nums">
                                  <div className="font-medium text-gray-700 dark:text-slate-300">
                                    {rule.deletedAt ? formatDateTime(rule.deletedAt).split(' ')[0] : '—'}
                                  </div>
                                  <div className="text-[10.5px] text-gray-400 dark:text-slate-500 mt-0.5">
                                    {rule.deletedAt ? formatDateTime(rule.deletedAt).split(' ')[1] || '' : ''}
                                  </div>
                                </div>
                              </td>
                              <td className="px-5 py-4">
                                <div className="flex items-center justify-end gap-1 p-1 -m-1 rounded-xl group-hover:bg-white/60 dark:group-hover:bg-slate-700/40 transition-colors">
                                  <button
                                    onClick={() => setRestoreTarget(rule)}
                                    className="group/btn relative inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11.5px] font-bold bg-emerald-50 dark:bg-emerald-900/20 text-emerald-700 dark:text-emerald-400 border border-emerald-100 dark:border-emerald-900/40 hover:bg-emerald-100 dark:hover:bg-emerald-900/40 hover:shadow-md hover:shadow-emerald-500/10 transition-all"
                                    title={t('notifications.rules.restoreToMainList')}
                                  >
                                    <RotateCcw className="w-3.5 h-3.5 group-hover/btn:-rotate-45 transition-transform" />
                                    {t('notifications.common.restore')}
                                  </button>
                                  <button
                                    onClick={() => setHardDeleteTarget(rule)}
                                    className="group/btn2 relative inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11.5px] font-bold bg-red-50 dark:bg-red-900/20 text-red-700 dark:text-red-400 border border-red-100 dark:border-red-900/40 hover:bg-red-100 dark:hover:bg-red-900/40 hover:shadow-md hover:shadow-red-500/10 transition-all"
                                    title={t('notifications.rules.hardDeleteHint')}
                                  >
                                    <Trash2 className="w-3.5 h-3.5 group-hover/btn2:scale-110 transition-transform" />
                                    {t('notifications.common.deleteForever')}
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
                    {(trashList?.data ?? []).map((rule) => {
                      const colors = getEventTypeColor(rule.eventType);
                      return (
                        <div key={rule.id} className="p-4 hover:bg-rose-50/40 dark:hover:bg-rose-900/10 space-y-3">
                          <div className="flex items-start gap-2.5">
                            <div className={`shrink-0 w-9 h-9 rounded-xl ${colors.bg} ${colors.ring} ring-1 flex items-center justify-center opacity-85`}>
                              <Bell className={`w-4 h-4 ${colors.text}`} />
                            </div>
                            <div className="min-w-0 flex-1">
                              <div className="text-sm font-bold text-gray-900 dark:text-slate-100 truncate line-through decoration-1 decoration-rose-300/60 dark:decoration-rose-800/50">
                                {rule.name}
                              </div>
                              <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5">
                                ID · #{rule.id}
                              </div>
                            </div>
                          </div>

                          <div className="flex flex-wrap items-center gap-1.5">
                            <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-xl text-[11px] font-bold ring-1 opacity-85 ${colors.bg} ${colors.text} ${colors.ring}`}>
                              <span className={`w-1.5 h-1.5 rounded-full ${colors.accent}`} />
                              {getEventTypeOptionLabel(t, rule.eventType)}
                            </span>
                          </div>

                          {rule.templateName && (
                            <div className="text-xs text-gray-600 dark:text-slate-300 flex items-center gap-1.5">
                              <FileText className="w-3 h-3 text-gray-400 flex-shrink-0" />
                              <span className="truncate">{rule.templateName}</span>
                            </div>
                          )}

                          <div className="text-[11px] text-gray-400 dark:text-slate-500 font-mono">
                            {rule.deletedAt ? formatDateTime(rule.deletedAt) : '—'}
                          </div>

                          <div className="flex items-center gap-2 pt-1">
                            <button
                              onClick={() => setRestoreTarget(rule)}
                              className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-xl text-xs font-bold bg-emerald-50 dark:bg-emerald-900/20 text-emerald-700 dark:text-emerald-400 border border-emerald-100 dark:border-emerald-900/40 hover:bg-emerald-100 dark:hover:bg-emerald-900/40 transition"
                            >
                              <RotateCcw className="w-3.5 h-3.5" />
                              {t('notifications.common.restore')}
                            </button>
                            <button
                              onClick={() => setHardDeleteTarget(rule)}
                              className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-xl text-xs font-bold bg-red-50 dark:bg-red-900/20 text-red-700 dark:text-red-400 border border-red-100 dark:border-red-900/40 hover:bg-red-100 dark:hover:bg-red-900/40 transition"
                            >
                              <Trash2 className="w-3.5 h-3.5" />
                              {t('notifications.common.deleteForever')}
                            </button>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </>
              )}
            </div>

            {/* 分页 */}
            {trashList && trashList.totalPages > 1 && (
              <div className="px-1 py-2 flex items-center justify-between flex-wrap gap-3">
                <div className="text-xs text-gray-500 dark:text-slate-400 flex items-center gap-1.5">
                  {t('notifications.common.paginationPrefix')}
                  <span className="inline-flex items-center justify-center w-5 h-5 rounded-md bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-[11px] font-bold">
                    {trashList.totalItems}
                  </span>
                  {t('notifications.common.paginationMiddle')}
                  <b className="text-gray-700 dark:text-slate-200 tabular-nums">{trashList.currentPage}</b> / {trashList.totalPages}
                  {t('notifications.common.paginationSuffix')}
                </div>
                <div className="flex items-center gap-1.5">
                  <button
                    onClick={() => setTrashQuery((q) => ({ ...q, page: q.page - 1 }))}
                    disabled={trashList.currentPage <= 1}
                    className="inline-flex items-center gap-1 px-3 py-2 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl text-xs font-semibold disabled:opacity-40 disabled:cursor-not-allowed hover:bg-gray-50 dark:hover:bg-slate-700 dark:text-slate-200 shadow-sm transition"
                  >
                    <ChevronLeft className="w-4 h-4" />
                    {t('notifications.common.previousPage')}
                  </button>
                  <button
                    onClick={() => setTrashQuery((q) => ({ ...q, page: q.page + 1 }))}
                    disabled={trashList.currentPage >= trashList.totalPages}
                    className="inline-flex items-center gap-1 px-3 py-2 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl text-xs font-semibold disabled:opacity-40 disabled:cursor-not-allowed hover:bg-gray-50 dark:hover:bg-slate-700 dark:text-slate-200 shadow-sm transition"
                  >
                    {t('notifications.common.nextPage')}
                    <ChevronRight className="w-4 h-4" />
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
        title={t('notifications.rules.restoreTitle')}
        message={restoreTarget ? t('notifications.rules.restoreMessage', { name: restoreTarget.name }) : ''}
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
        message={hardDeleteTarget ? t('notifications.rules.hardDeleteMessage', { name: hardDeleteTarget.name }) : ''}
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
        message={t('notifications.rules.clearTrashMessage')}
        confirmText={t('notifications.rules.confirmClear')}
        cancelText={t('common.cancel')}
        variant="danger"
        onConfirm={handleClearTrash}
        onCancel={() => setClearTrashOpen(false)}
        loading={trashActionLoading}
      />

      {/* Toast */}
      {toast && (
        <div className="fixed bottom-6 right-6 z-50 animate-[slideIn_0.25s_ease-out]">
          <div
            className={`relative overflow-hidden px-5 py-3.5 rounded-2xl text-sm font-bold flex items-center gap-3 shadow-2xl border border-white/10 backdrop-blur-sm min-w-[240px] max-w-[420px] ${
              toast.type === 'success'
                ? 'bg-gradient-to-br from-emerald-500 via-green-600 to-teal-600 text-white'
                : 'bg-gradient-to-br from-rose-500 via-red-600 to-orange-600 text-white'
            }`}
          >
            {/* 顶部高亮条 */}
            <div className="absolute top-0 left-0 right-0 h-0.5 bg-white/30" />
            {/* 背景柔光 */}
            <div className={`absolute -top-8 -right-8 w-24 h-24 rounded-full opacity-30 blur-2xl ${
              toast.type === 'success' ? 'bg-emerald-300' : 'bg-rose-300'
            }`} />
            {/* 图标卡片 */}
            <div className={`relative flex-shrink-0 w-9 h-9 rounded-xl flex items-center justify-center bg-white/20 ring-1 ring-white/30 ${
              toast.type === 'success' ? 'shadow-[0_0_0_4px_rgba(16,185,129,0.15)]' : 'shadow-[0_0_0_4px_rgba(244,63,94,0.15)]'
            }`}>
              {toast.type === 'success' ? (
                <CheckCircle2 className="w-5 h-5 drop-shadow-[0_1px_1px_rgba(0,0,0,0.15)]" />
              ) : (
                <XCircle className="w-5 h-5 drop-shadow-[0_1px_1px_rgba(0,0,0,0.15)]" />
              )}
            </div>
            {/* 内容 */}
            <div className="relative flex-1 min-w-0">
              <div className="text-[11px] uppercase tracking-[0.12em] text-white/80 font-extrabold mb-0.5">
                {toast.type === 'success' ? t('notifications.rules.toastSuccess') : t('notifications.rules.toastError')}
              </div>
              <div className="text-[13px] font-bold text-white leading-snug truncate">
                {toast.text}
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
