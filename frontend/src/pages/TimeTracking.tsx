import { useState, useEffect, useRef, useCallback } from 'react';
import { timeTrackingApi, conversationApi, customerApi } from '../api';
import { useTimedToast } from '../hooks/useTimedToast';
import type {
  TimeEntryDto,
  TimeTrackingOverviewResponse,
  TimeTrackingReportResponse,
  CreateTimeEntryRequest,
  UpdateTimeEntryRequest,
  ConversationResponse,
  CustomerResponse,
} from '../api/types';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import { formatDateTime } from '../utils/format';
import { useI18n } from '../context/I18nContext';
import { BUILT_IN_ENTRIES, FALLBACK_LOCALE, getBoundLocale } from '../i18n/locale';
import { translate } from '../i18n/translate';
import type { TranslateParams } from '../i18n/types';
import type { TranslationKey } from '../locales/zh-Hans';
import {
  Clock,
  Play,
  Square,
  Plus,
  Search,
  Edit2,
  Trash2,
  CheckCircle2,
  XCircle,
  ChevronLeft,
  ChevronRight,
  Timer,
  FileText,
  Download,
  Activity,
  List as ListIcon,
  BarChart3,
  CalendarDays,
  TrendingUp,
  DollarSign,
  User,
  RotateCcw,
  LayoutList,
  Briefcase,
  Ticket,
} from 'lucide-react';

// ====== 辅助函数 ======

/**
 * 带插值的取词（缺 key 失败安全：`translate` 原样返回 key 而不抛错）。
 * `formatDuration` 与 `extractConcurrencyMessage` 是**模块级纯函数**（渲染期直接调用），
 * 语言由 `I18nProvider` 在渲染时绑定（见 `i18n/locale.ts`）；
 * 手法与 `pages/calendar/calendarUtils.ts` 的 `tParams` 一致。
 */
function tParams(key: TranslationKey, params?: TranslateParams): string {
  const locale = getBoundLocale();
  return translate(BUILT_IN_ENTRIES[locale] ?? BUILT_IN_ENTRIES[FALLBACK_LOCALE], locale, key, params);
}

/** 将分钟数格式化为 "X小时Y分钟" */
function formatDuration(minutes: number): string {
  const m = Math.max(0, Math.round(minutes));
  const h = Math.floor(m / 60);
  const min = m % 60;
  if (h === 0) return tParams('serviceOps.timeTracking.duration.minutes', { minutes: min });
  if (min === 0) return tParams('serviceOps.timeTracking.duration.hours', { hours: h });
  return tParams('serviceOps.timeTracking.duration.hoursMinutes', { hours: h, minutes: min });
}

/** 将秒数格式化为 "HH:MM:SS" 用于实时计时器显示 */
function formatStopwatch(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(totalSeconds));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(h)}:${pad(m)}:${pad(sec)}`;
}

/**
 * 从错误中提取乐观锁并发冲突（HTTP 409）的提示消息。
 * 后端返回 { message, entityType, entityId, latest }；返回非空字符串表示是并发冲突。
 */
function extractConcurrencyMessage(error: unknown): string | null {
  if (!error || typeof error !== 'object') return null;
  const err = error as { status?: number; response?: { data?: { message?: string; latest?: unknown } }; message?: string };
  if (err.status !== 409) return null;
  const dataMsg = err.response?.data?.message;
  return dataMsg ?? err.message ?? tParams('serviceOps.timeTracking.concurrencyConflict');
}

/** ISO 时间转 datetime-local 输入框格式 (YYYY-MM-DDTHH:mm) */
function toDatetimeLocal(iso: string): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** datetime-local 值转 ISO 字符串 */
function fromDatetimeLocal(value: string): string {
  if (!value) return new Date().toISOString();
  return new Date(value).toISOString();
}

/** 获取最近 7 天日期范围 (YYYY-MM-DD) */
function getLast7DaysRange(): { startDate: string; endDate: string } {
  const end = new Date();
  const start = new Date();
  start.setDate(start.getDate() - 6);
  const fmt = (d: Date) => {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  };
  return { startDate: fmt(start), endDate: fmt(end) };
}

/** 从用户名提取首字母（最多2个字符） */
function getInitials(name: string): string {
  if (!name) return '?';
  const parts = name.trim().split(/\s+/);
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
  return name.slice(0, 2).toUpperCase();
}

/** 根据字符串生成稳定的颜色 */
function colorFromString(str: string): string {
  const colors = [
    'bg-indigo-500', 'bg-emerald-500', 'bg-amber-500', 'bg-rose-500',
    'bg-cyan-500', 'bg-violet-500', 'bg-orange-500', 'bg-teal-500',
  ];
  let hash = 0;
  for (let i = 0; i < str.length; i++) hash = str.charCodeAt(i) + ((hash << 5) - hash);
  return colors[Math.abs(hash) % colors.length];
}

// ====== 常量 ======

// 条目类型码 → key；渲染统一走组件内的 `entryTypeLabel()`（未命中码渲染空串，与改造前同义）
const ENTRY_TYPE_LABEL: Record<string, TranslationKey | undefined> = {
  timer: 'serviceOps.timeTracking.entryType.timer',
  manual: 'serviceOps.timeTracking.entryType.manual',
};

const ENTRY_TYPE_ICON: Record<string, typeof Timer> = {
  timer: Timer,
  manual: FileText,
};

const DURATION_PRESETS = [15, 30, 60, 90, 120, 240];

const tabs = [
  { key: 'overview', labelKey: 'serviceOps.timeTracking.tab.overview', icon: <Activity className="w-4 h-4" /> },
  { key: 'list', labelKey: 'serviceOps.timeTracking.tab.list', icon: <ListIcon className="w-4 h-4" /> },
] as const satisfies readonly { key: 'overview' | 'list'; labelKey: TranslationKey; icon: unknown }[];

interface FormData {
  entryType: 'timer' | 'manual';
  description: string;
  conversationId: string;
  customerId: string;
  startTime: string;
  durationMinutes: string;
  billable: boolean;
}

function defaultFormData(): FormData {
  return {
    entryType: 'manual',
    description: '',
    conversationId: '',
    customerId: '',
    startTime: toDatetimeLocal(new Date().toISOString()),
    durationMinutes: '',
    billable: true,
  };
}

// ====== 骨架屏组件 ======

function OverviewSkeleton() {
  return (
    <div className="space-y-4 sm:space-y-5">
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3 sm:gap-4">
        {[...Array(4)].map((_, i) => (
          <div key={i} className="h-28 bg-gray-200 dark:bg-slate-700 rounded-2xl animate-pulse" />
        ))}
      </div>
      <div className="h-20 bg-gray-200 dark:bg-slate-700 rounded-2xl animate-pulse" />
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
        <div className="h-64 bg-gray-200 dark:bg-slate-700 rounded-2xl animate-pulse" />
        <div className="h-64 bg-gray-200 dark:bg-slate-700 rounded-2xl animate-pulse" />
      </div>
    </div>
  );
}

function ListSkeleton() {
  return (
    <div className="space-y-4">
      <div className="h-20 bg-gray-200 dark:bg-slate-700 rounded-2xl animate-pulse" />
      <div className="h-16 bg-gray-200 dark:bg-slate-700 rounded-2xl animate-pulse" />
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 overflow-hidden">
        {[...Array(6)].map((_, i) => (
          <div key={i} className="px-5 py-4 border-b border-gray-100 dark:border-slate-700/50 flex items-center gap-4">
            <div className="w-11 h-11 bg-gray-200 dark:bg-slate-700 rounded-xl animate-pulse" />
            <div className="flex-1 space-y-2">
              <div className="h-4 w-1/3 bg-gray-200 dark:bg-slate-700 rounded animate-pulse" />
              <div className="h-3 w-1/4 bg-gray-200 dark:bg-slate-700 rounded animate-pulse" />
            </div>
            <div className="h-6 w-20 bg-gray-200 dark:bg-slate-700 rounded animate-pulse" />
          </div>
        ))}
      </div>
    </div>
  );
}

// ====== 空状态组件 ======

function EmptyState({ icon: Icon, title, description }: { icon: typeof Clock; title: string; description: string }) {
  return (
    <div className="py-16 text-center">
      <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-gray-100 dark:bg-slate-700/50 mb-4">
        <Icon className="w-8 h-8 text-gray-400 dark:text-slate-500" />
      </div>
      <h3 className="text-lg font-medium text-gray-900 dark:text-slate-100 mb-1">{title}</h3>
      <p className="text-sm text-gray-500 dark:text-slate-400">{description}</p>
    </div>
  );
}

// ====== 主组件 ======

export default function TimeTracking() {
  const { t } = useI18n();
  /** 条目类型码 → 文案（未命中码渲染空串，与改造前 `ENTRY_TYPE_LABEL[code]` 为 undefined 同义） */
  const entryTypeLabel = (code: string) => {
    const key = ENTRY_TYPE_LABEL[code];
    return key ? t(key) : '';
  };
  const [activeTab, setActiveTab] = useState<'overview' | 'list'>('overview');
  const [overview, setOverview] = useState<TimeTrackingOverviewResponse | null>(null);
  const [report, setReport] = useState<TimeTrackingReportResponse | null>(null);
  const [entries, setEntries] = useState<TimeEntryDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [totalItems, setTotalItems] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [searchInput, setSearchInput] = useState('');
  const searchDebounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // loadList 请求序号：快速切换筛选/翻页时丢弃过期响应，避免旧响应覆盖新结果
  const listReqSeqRef = useRef(0);
  const [entryTypeFilter, setEntryTypeFilter] = useState<'timer' | 'manual' | null>(null);
  const [billableFilter, setBillableFilter] = useState<boolean | null>(null);
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');

  const [activeTimer, setActiveTimer] = useState<TimeEntryDto | null>(null);
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  const [startTimerDesc, setStartTimerDesc] = useState('');
  const [timerLoading, setTimerLoading] = useState(false);

  const [showEditor, setShowEditor] = useState(false);
  const [editingEntry, setEditingEntry] = useState<TimeEntryDto | null>(null);
  const [formData, setFormData] = useState<FormData>(defaultFormData());
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);
  // 关联工单/客户下拉数据（新建记录时按需懒加载）
  const [activeConversations, setActiveConversations] = useState<ConversationResponse[]>([]);
  const [customers, setCustomers] = useState<CustomerResponse[]>([]);
  const [optionsLoading, setOptionsLoading] = useState(false);

  const [deleteId, setDeleteId] = useState<number | null>(null);
  const [deleteLoading, setDeleteLoading] = useState(false);
  const { message: toast, show: showToast } = useTimedToast();

  const pageSize = 10;

  // ====== 搜索防抖 ======
  const applySearch = useCallback((value: string) => {
    if (searchDebounceRef.current) clearTimeout(searchDebounceRef.current);
    searchDebounceRef.current = setTimeout(() => {
      setSearch(value);
      setPage(1);
    }, 400);
  }, []);

  const triggerSearchNow = useCallback(() => {
    if (searchDebounceRef.current) clearTimeout(searchDebounceRef.current);
    setSearch(searchInput);
    setPage(1);
  }, [searchInput]);

  const clearSearch = useCallback(() => {
    setSearchInput('');
    setSearch('');
    setPage(1);
  }, []);

  useEffect(() => {
    return () => {
      if (searchDebounceRef.current) clearTimeout(searchDebounceRef.current);
    };
  }, []);

  // ====== 实时计时器 ======
  // 计时器的 start 时间来源：优先用 activeTimer.startTime，回退到 overview.activeTimerStartTime
  // 这样即使 state.activeTimer 还没刷新，只要 overview 返回了活跃计时器信息也能正常走字
  const activeStartTime = activeTimer?.startTime ?? overview?.activeTimerStartTime ?? null;

  useEffect(() => {
    if (!activeStartTime) {
      setElapsedSeconds(0);
      return;
    }
    const startMs = new Date(activeStartTime).getTime();
    const update = () => {
      const secs = Math.floor((Date.now() - startMs) / 1000);
      setElapsedSeconds(secs);
    };
    update();
    const interval = setInterval(update, 1000);
    return () => clearInterval(interval);
  }, [activeStartTime]);

  // ====== 数据加载 ======
  useEffect(() => {
    loadActiveTimer();
  }, []);

  useEffect(() => {
    if (activeTab === 'overview') {
      loadOverview();
      loadReport();
    }
    if (activeTab === 'list') {
      loadList();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab]);

  useEffect(() => {
    if (activeTab === 'list') loadList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, search, entryTypeFilter, billableFilter, startDate, endDate]);

  async function loadActiveTimer() {
    try {
      const data = await timeTrackingApi.getActiveTimer();
      setActiveTimer(data ?? null);
    } catch {
      // 静默处理
    }
  }

  async function loadOverview() {
    setLoading(true);
    try {
      const data = await timeTrackingApi.overview();
      setOverview(data);
    } finally {
      setLoading(false);
    }
  }

  async function loadReport() {
    try {
      const range = getLast7DaysRange();
      const data = await timeTrackingApi.report(range);
      setReport(data);
    } catch {
      // 静默处理
    }
  }

  async function loadList() {
    const seq = ++listReqSeqRef.current;
    setLoading(true);
    try {
      const data = await timeTrackingApi.list({
        page,
        pageSize,
        search: search || null,
        entryType: entryTypeFilter,
        billable: billableFilter,
        startDate: startDate || null,
        endDate: endDate || null,
      });
      if (seq !== listReqSeqRef.current) return;
      setEntries(data?.data ?? []);
      setTotalItems(data?.totalItems ?? 0);
    } finally {
      if (seq === listReqSeqRef.current) setLoading(false);
    }
  }

  // ====== 计时器操作 ======
  async function handleStartTimer() {
    setTimerLoading(true);
    try {
      await timeTrackingApi.startTimer({
        description: startTimerDesc || undefined,
        billable: true,
      });
      showToast({ type: 'success', text: t('serviceOps.timeTracking.toast.started') });
      setStartTimerDesc('');
      await refreshAfterTimerChange();
    } catch (e: unknown) {
      showToast({ type: 'error', text: e instanceof Error ? e.message : t('serviceOps.timeTracking.toast.startFailed') });
    } finally {
      setTimerLoading(false);
    }
  }

  async function handleStopTimer() {
    // 优先用 overview.activeTimerId（概览页可能有计时器但 state.activeTimer 还没同步），
    // 其次回退到 activeTimer?.id，避免静默失败
    const timerId = overview?.activeTimerId ?? activeTimer?.id;
    if (!timerId) return;
    setTimerLoading(true);
    try {
      await timeTrackingApi.stopTimer(timerId, activeTimer?.rowVersion ?? null);
      showToast({ type: 'success', text: t('serviceOps.timeTracking.toast.stopped') });
      await refreshAfterTimerChange();
    } catch (e: unknown) {
      const conflictMsg = extractConcurrencyMessage(e);
      if (conflictMsg) {
        showToast({ type: 'warning', text: conflictMsg });
        await refreshAfterTimerChange();
      } else {
        showToast({ type: 'error', text: e instanceof Error ? e.message : t('serviceOps.timeTracking.toast.stopFailed') });
      }
    } finally {
      setTimerLoading(false);
    }
  }

  async function refreshAfterTimerChange() {
    await loadActiveTimer();
    if (activeTab === 'overview') {
      await loadOverview();
      await loadReport();
    }
    if (activeTab === 'list') {
      await loadList();
    }
  }

  // ====== 新建/编辑 ======
  // 懒加载活跃会话 + 客户列表，供新建记录下拉选择
  async function loadRelationOptions() {
    setOptionsLoading(true);
    try {
      const [convRes, custRes] = await Promise.all([
        conversationApi.list({ status: 1, page: 1, pageSize: 100 }),
        customerApi.list({ page: 1, pageSize: 100 }),
      ]);
      setActiveConversations(convRes.data ?? []);
      setCustomers(custRes.data ?? []);
    } catch (e) {
      // 加载失败不阻塞表单提交，仅在控制台提示
      console.warn('加载活跃会话/客户列表失败', e);
    } finally {
      setOptionsLoading(false);
    }
  }

  function openCreate() {
    setEditingEntry(null);
    setFormData(defaultFormData());
    setFormError('');
    setShowEditor(true);
    loadRelationOptions();
  }

  function openEdit(entry: TimeEntryDto) {
    setEditingEntry(entry);
    setFormData({
      entryType: entry.entryType,
      description: entry.description,
      conversationId: entry.conversationId ? String(entry.conversationId) : '',
      customerId: entry.customerId ? String(entry.customerId) : '',
      startTime: toDatetimeLocal(entry.startTime),
      durationMinutes: entry.durationMinutes ? String(entry.durationMinutes) : '',
      billable: entry.billable,
    });
    setFormError('');
    setShowEditor(true);
  }

  async function handleSave() {
    if (!formData.description.trim()) {
      setFormError(t('serviceOps.timeTracking.form.descriptionRequired'));
      return;
    }
    if (!formData.startTime) {
      setFormError(t('serviceOps.timeTracking.form.startTimeRequired'));
      return;
    }
    if (formData.entryType === 'manual' && !formData.durationMinutes) {
      setFormError(t('serviceOps.timeTracking.form.durationRequired'));
      return;
    }

    setSaving(true);
    setFormError('');
    try {
      if (editingEntry) {
        const req: UpdateTimeEntryRequest = {
          description: formData.description,
          startTime: fromDatetimeLocal(formData.startTime),
          billable: formData.billable,
        };
        if (formData.durationMinutes) {
          req.durationMinutes = Number(formData.durationMinutes);
        }
        // 编辑已停止的记录时，根据 StartTime + Duration 推导 EndTime 一并传给后端，
        // 让后端能正确联动，避免 Duration 变化但 EndTime 不变的不一致
        if (editingEntry.endTime && formData.durationMinutes) {
          const start = new Date(fromDatetimeLocal(formData.startTime)).getTime();
          const end = new Date(start + Number(formData.durationMinutes) * 60 * 1000);
          req.endTime = end.toISOString();
        } else if (editingEntry.endTime) {
          // 已有 EndTime 但没改 Duration，保持原值
          req.endTime = editingEntry.endTime;
        }
        await timeTrackingApi.update(editingEntry.id, { ...req, ifMatch: editingEntry.rowVersion ?? null });
        showToast({ type: 'success', text: t('serviceOps.timeTracking.toast.updated') });
      } else {
        // 新建 timer 类型记录：后端会检查是否已有活跃计时器（TT-002 防绕过）
        if (formData.entryType === 'timer') {
          try {
            const active = await timeTrackingApi.getActiveTimer();
            if (active) {
              setFormError(t('serviceOps.timeTracking.timerAlreadyRunning'));
              return;
            }
          } catch {
            // 检查失败不阻塞创建流程，后端会兜底校验
          }
        }
        const req: CreateTimeEntryRequest = {
          entryType: formData.entryType,
          description: formData.description,
          startTime: fromDatetimeLocal(formData.startTime),
          billable: formData.billable,
        };
        if (formData.conversationId) req.conversationId = Number(formData.conversationId);
        if (formData.customerId) req.customerId = Number(formData.customerId);
        if (formData.entryType === 'manual' && formData.durationMinutes) {
          req.durationMinutes = Number(formData.durationMinutes);
        }
        await timeTrackingApi.create(req);
        showToast({ type: 'success', text: t('serviceOps.timeTracking.toast.created') });
      }
      setShowEditor(false);
      await refreshAfterTimerChange();
    } catch (e: unknown) {
      const conflictMsg = extractConcurrencyMessage(e);
      if (conflictMsg) {
        setFormError(conflictMsg);
        await refreshAfterTimerChange();
      } else {
        setFormError(e instanceof Error ? e.message : t('serviceOps.common.saveFailed'));
      }
    } finally {
      setSaving(false);
    }
  }

  // ====== 删除 ======
  async function executeDelete() {
    if (!deleteId) return;
    setDeleteLoading(true);
    try {
      await timeTrackingApi.remove(deleteId);
      showToast({ type: 'success', text: t('serviceOps.timeTracking.toast.deleted') });
      setDeleteId(null);
      // 如果删除的可能是活跃计时器，刷新 activeTimer 避免 UI 残留
      if (activeTimer?.id === deleteId || overview?.activeTimerId === deleteId) {
        setActiveTimer(null);
      }
      await refreshAfterTimerChange();
    } catch (e: unknown) {
      showToast({ type: 'error', text: e instanceof Error ? e.message : t('serviceOps.common.deleteFailed') });
    } finally {
      setDeleteLoading(false);
    }
  }

  // ====== 导出 ======
  async function handleExport() {
    try {
      setLoading(true);
      const blob = await timeTrackingApi.export({
        search: search || null,
        entryType: entryTypeFilter,
        billable: billableFilter,
        startDate: startDate || null,
        endDate: endDate || null,
      });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `time-entries-${new Date().toISOString().split('T')[0]}.csv`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      showToast({ type: 'success', text: t('serviceOps.timeTracking.toast.exportSucceeded') });
    } catch (e: unknown) {
      showToast({ type: 'error', text: e instanceof Error ? e.message : t('serviceOps.timeTracking.toast.exportFailed') });
    } finally {
      setLoading(false);
    }
  }

  // ====== 计算值 ======
  const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));

  // elapsedSeconds 由 useEffect 每秒更新，直接用于显示
  const liveSeconds = elapsedSeconds;

  const maxUserMinutes = report
    ? Math.max(...report.byUser.map((u) => u.totalMinutes), 1)
    : 1;
  const maxDateMinutes = report
    ? Math.max(...report.byDate.map((d) => d.totalMinutes), 1)
    : 1;

  const hasActiveFilters = !!(search || entryTypeFilter || billableFilter !== null || startDate || endDate);

  // 生成页码数组
  const pageNumbers: number[] = [];
  const maxVisiblePages = 5;
  let startPage = Math.max(1, page - Math.floor(maxVisiblePages / 2));
  const endPage = Math.min(totalPages, startPage + maxVisiblePages - 1);
  if (endPage - startPage + 1 < maxVisiblePages) {
    startPage = Math.max(1, endPage - maxVisiblePages + 1);
  }
  for (let i = startPage; i <= endPage; i++) pageNumbers.push(i);

  // 样式常量（简洁风格，对齐知识库页面）
  const inputClass =
    'w-full px-4 py-2.5 text-sm bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500/70 transition-all duration-200 shadow-sm placeholder:text-slate-400';
  const labelClass = 'block text-[11px] font-semibold text-slate-500 dark:text-slate-400 mb-1.5 uppercase tracking-wider';
  const sectionTitleClass = 'flex items-center gap-2.5 pb-3 mb-2 border-b border-slate-100 dark:border-slate-700/50';
  const selectClass = (hasValue: boolean) =>
    `w-full px-4 py-2.5 text-sm bg-white dark:bg-slate-800 border rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500/70 transition-all duration-200 shadow-sm cursor-pointer ${
      hasValue
        ? 'border-blue-400/60 dark:border-blue-600/60 text-blue-700 dark:text-blue-300 font-semibold'
        : 'border-slate-200 dark:border-slate-600 text-slate-700 dark:text-slate-200'
    }`;

  return (
    <div className="space-y-4 sm:space-y-5">
      {/* ====== Header - 简洁白底 ====== */}
      <div className="bg-white dark:bg-slate-800 p-4 sm:p-6 rounded-2xl border border-slate-200 dark:border-slate-700 shadow-sm">
        <div className="flex items-start justify-between gap-5 flex-wrap">
          <div className="flex items-start gap-5">
            <div className="p-4 bg-blue-50 dark:bg-blue-950/30 rounded-2xl border border-blue-200 dark:border-blue-800 text-blue-600 dark:text-blue-400">
              <Clock className="w-8 h-8" strokeWidth={2.2} />
            </div>
            <div className="pt-1">
              <div className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-emerald-50 dark:bg-emerald-950/30 border border-emerald-200 dark:border-emerald-800 text-[11px] font-semibold text-emerald-700 dark:text-emerald-400 mb-2">
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse" />
                {t('serviceOps.timeTracking.header.liveTracking')}
              </div>
              <h1 className="text-xl sm:text-2xl font-black text-slate-900 dark:text-slate-100 tracking-tight leading-none">{t('serviceOps.timeTracking.pageTitle')}</h1>
              <p className="text-xs sm:text-sm text-slate-500 dark:text-slate-400 mt-2 max-w-xl leading-relaxed">
                {t('serviceOps.timeTracking.subtitle')}
              </p>
            </div>
          </div>
          <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-2.5 w-full sm:w-auto">
            <button
              onClick={handleExport}
              disabled={loading}
              className="group inline-flex items-center justify-center gap-2 w-full sm:w-auto px-4 py-2.5 text-xs sm:text-sm font-semibold bg-slate-100 hover:bg-slate-200 dark:bg-slate-700 dark:hover:bg-slate-600 text-slate-700 dark:text-slate-200 rounded-xl border border-slate-200 dark:border-slate-600 disabled:opacity-50 transition-all duration-200 hover:shadow-md"
            >
              <Download className="w-4 h-4" />
              {t('serviceOps.common.exportCsv')}
            </button>
            {activeTab === 'list' && (
              <button
                onClick={openCreate}
                className="group inline-flex items-center justify-center gap-2 w-full sm:w-auto px-5 py-2.5 text-xs sm:text-sm font-bold text-white bg-blue-600 hover:bg-blue-700 rounded-xl shadow-sm transition-all duration-200 hover:shadow-md active:scale-[0.97]"
              >
                <Plus className="w-4 h-4" strokeWidth={2.4} />
                {t('serviceOps.timeTracking.newEntry')}
              </button>
            )}
          </div>
        </div>
      </div>

      {/* ====== Tab 切换 - 简洁分段式 ====== */}
      <div className="relative p-1.5 bg-slate-100 dark:bg-slate-800/60 rounded-xl border border-slate-200 dark:border-slate-700">
        <div className="flex gap-1 relative">
          {/* 滑块指示器 */}
          <div
            className={`absolute top-1 bottom-1 rounded-lg bg-white dark:bg-slate-700 shadow-sm transition-all duration-300 ease-[cubic-bezier(0.34,1.56,0.64,1)] z-0`}
            style={{
              width: `calc(50% - 4px)`,
              left: activeTab === 'overview' ? '4px' : 'calc(50% + 0px)',
            }}
          />
          {tabs.map((tab) => (
            <button
              key={tab.key}
              onClick={() => {
                setActiveTab(tab.key);
                setPage(1);
              }}
              className={`relative z-10 flex-1 inline-flex items-center justify-center gap-2 px-5 py-2.5 text-sm font-bold rounded-lg transition-all duration-200 ${
                activeTab === tab.key
                  ? 'text-blue-600 dark:text-blue-400'
                  : 'text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200'
              }`}
            >
              <span>{tab.icon}</span>
              <span className="tracking-wide">{t(tab.labelKey)}</span>
            </button>
          ))}
        </div>
      </div>

      {/* ====== 概览 Tab ====== */}
      {activeTab === 'overview' && (
        loading ? <OverviewSkeleton /> :
        overview ? (
          <div className="space-y-4 sm:space-y-5">
            {/* 统计卡片 - 简洁风格 */}
            <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3 sm:gap-4">
              {[
                {
                  label: t('serviceOps.timeTracking.stats.today'),
                  value: formatDuration(overview.todayMinutes),
                  count: t('serviceOps.timeTracking.stats.recordCount', { count: overview.todayCount }),
                  color: 'text-primary-600 dark:text-primary-300',
                  valueGradient: 'from-primary-600 to-indigo-600 dark:from-primary-400 dark:to-indigo-400',
                  bg: 'bg-gradient-to-br from-primary-500/15 via-primary-400/10 to-transparent dark:from-primary-500/20 dark:via-primary-400/10 dark:to-transparent',
                  border: 'ring-2 ring-primary-500/20 shadow-[0_0_0_1px_rgba(99,102,241,0.1)]',
                  iconBg: 'bg-blue-50 dark:bg-blue-950/30 text-blue-600 dark:text-blue-400',
                  icon: Clock,
                  pattern: 'from-primary-400/20 to-transparent',
                },
                {
                  label: t('serviceOps.timeTracking.stats.week'),
                  value: formatDuration(overview.weekMinutes),
                  count: t('serviceOps.timeTracking.stats.recordCount', { count: overview.weekCount }),
                  color: 'text-emerald-600 dark:text-emerald-300',
                  valueGradient: 'from-emerald-600 to-teal-600 dark:from-emerald-400 dark:to-teal-400',
                  bg: 'bg-gradient-to-br from-emerald-500/15 via-emerald-400/10 to-transparent dark:from-emerald-500/20 dark:via-emerald-400/10 dark:to-transparent',
                  border: 'ring-2 ring-emerald-500/20 shadow-[0_0_0_1px_rgba(16,185,129,0.1)]',
                  iconBg: 'bg-emerald-50 dark:bg-emerald-950/30 text-emerald-600 dark:text-emerald-400',
                  icon: TrendingUp,
                  pattern: 'from-emerald-400/20 to-transparent',
                },
                {
                  label: t('serviceOps.timeTracking.stats.month'),
                  value: formatDuration(overview.monthMinutes),
                  count: t('serviceOps.timeTracking.stats.totalPrefix', { value: formatDuration(overview.totalMinutes) }),
                  color: 'text-blue-600 dark:text-blue-300',
                  valueGradient: 'from-blue-600 to-sky-600 dark:from-blue-400 dark:to-sky-400',
                  bg: 'bg-gradient-to-br from-blue-500/15 via-blue-400/10 to-transparent dark:from-blue-500/20 dark:via-blue-400/10 dark:to-transparent',
                  border: 'ring-2 ring-blue-500/20 shadow-[0_0_0_1px_rgba(59,130,246,0.1)]',
                  iconBg: 'bg-blue-50 dark:bg-blue-950/30 text-blue-600 dark:text-blue-400',
                  icon: BarChart3,
                  pattern: 'from-blue-400/20 to-transparent',
                },
                {
                  label: t('serviceOps.timeTracking.stats.billable'),
                  value: formatDuration(overview.billableMinutes),
                  count: t('serviceOps.timeTracking.stats.grandTotalPrefix', { value: formatDuration(overview.totalMinutes) }),
                  color: 'text-amber-600 dark:text-amber-300',
                  valueGradient: 'from-amber-600 to-orange-600 dark:from-amber-400 dark:to-orange-400',
                  bg: 'bg-gradient-to-br from-amber-500/15 via-amber-400/10 to-transparent dark:from-amber-500/20 dark:via-amber-400/10 dark:to-transparent',
                  border: 'ring-2 ring-amber-500/20 shadow-[0_0_0_1px_rgba(245,158,11,0.1)]',
                  iconBg: 'bg-amber-50 dark:bg-amber-950/30 text-amber-600 dark:text-amber-400',
                  icon: DollarSign,
                  pattern: 'from-amber-400/20 to-transparent',
                },
              ].map((s, idx) => (
                <div
                  key={s.label}
                  className="group relative bg-white dark:bg-slate-800 rounded-2xl p-4 sm:p-6 border border-slate-200 dark:border-slate-700 shadow-sm transition-shadow duration-200 hover:shadow-md animate-[fadeIn_0.4s_ease-out]"
                  style={{ animationDelay: `${idx * 60}ms` }}
                >
                  <div className="relative z-10 flex items-start justify-between gap-3">
                    <div className="min-w-0 flex-1">
                      <p className="text-[11px] font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider">{s.label}</p>
                      <p className={`mt-3 text-xl sm:text-2xl lg:text-[1.7rem] font-black leading-none ${s.color}`}>
                        {s.value}
                      </p>
                      <div className="flex items-center gap-1.5 mt-2.5">
                        <span className="text-[11px] font-medium text-slate-400 dark:text-slate-500">{s.count}</span>
                      </div>
                    </div>
                    <div className={`relative w-14 h-14 rounded-2xl flex items-center justify-center flex-shrink-0 ${s.iconBg}`}>
                      <s.icon className="relative z-10 w-7 h-7" strokeWidth={2.1} />
                    </div>
                  </div>
                </div>
              ))}
            </div>

            {/* 运行中计时器提示 - 简洁蓝色边框卡片 */}
            {overview.activeTimerId ? (
              <div className="bg-blue-50 dark:bg-blue-950/20 p-4 sm:p-6 rounded-2xl border border-blue-200 dark:border-blue-800 animate-[fadeIn_0.4s_ease-out]">
                <div className="flex items-center gap-4 sm:gap-5 flex-wrap">
                  <div className="relative p-4 bg-blue-100 dark:bg-blue-900/40 rounded-2xl border border-blue-200 dark:border-blue-800 text-blue-600 dark:text-blue-400">
                    <Timer className="w-7 h-7" strokeWidth={2.2} />
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="inline-flex items-center gap-2 px-2.5 py-0.5 rounded-full bg-blue-100 dark:bg-blue-900/40 border border-blue-200 dark:border-blue-800 text-[11px] font-bold text-blue-700 dark:text-blue-300 mb-2">
                      <span className="w-1.5 h-1.5 rounded-full bg-blue-500 animate-pulse" />
                      {t('serviceOps.timeTracking.activeTimer.badge')}
                    </div>
                    <div className="text-lg font-bold text-slate-900 dark:text-slate-100 tracking-tight truncate">
                      {overview.activeTimerDescription || t('serviceOps.common.noDescription')}
                    </div>
                    <div className="flex items-center gap-3 mt-1.5 text-xs text-slate-500 dark:text-slate-400 flex-wrap">
                      <span className="inline-flex items-center gap-1.5">
                        <Clock className="w-3.5 h-3.5" />
                        {t('serviceOps.timeTracking.activeTimer.startedAt', { time: formatDateTime(overview.activeTimerStartTime) })}
                      </span>
                      <span className="opacity-50">·</span>
                      <div className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-xl bg-blue-100 dark:bg-blue-900/40 border border-blue-200 dark:border-blue-800">
                        <span className="text-blue-700 dark:text-blue-300 text-[10.5px] font-bold uppercase tracking-wider">{t('serviceOps.timeTracking.activeTimer.elapsed')}</span>
                        <span className="text-blue-700 dark:text-blue-300 font-black font-mono text-lg tracking-wider">
                          {formatStopwatch(liveSeconds)}
                        </span>
                      </div>
                    </div>
                  </div>
                  <button
                    onClick={handleStopTimer}
                    disabled={timerLoading}
                    className="group inline-flex items-center justify-center gap-2 w-full sm:w-auto px-5 py-3 text-xs sm:text-sm font-bold text-white bg-red-500 hover:bg-red-600 rounded-xl disabled:opacity-60 transition-all duration-200 shadow-sm hover:shadow-md active:scale-[0.97]"
                  >
                    <Square className="w-4 h-4" strokeWidth={2.4} fill="currentColor" />
                    {t('serviceOps.timeTracking.activeTimer.stop')}
                  </button>
                </div>
              </div>
            ) : (
              <div className="bg-white dark:bg-slate-800 rounded-2xl border border-slate-200 dark:border-slate-700 p-4 sm:p-6 shadow-sm">
                <div className="flex items-center gap-4 flex-wrap">
                  <div className="p-3.5 bg-emerald-50 dark:bg-emerald-950/30 rounded-xl text-emerald-600 dark:text-emerald-400">
                    <Play className="w-6 h-6" fill="currentColor" strokeWidth={0} />
                  </div>
                  <div className="flex-1 min-w-[200px]">
                    <div className="text-[11px] font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wider mb-1.5">{t('serviceOps.timeTracking.activeTimer.quickStart')}</div>
                    <input
                      type="text"
                      placeholder={t('serviceOps.timeTracking.activeTimer.descriptionPlaceholder')}
                      value={startTimerDesc}
                      onChange={(e) => setStartTimerDesc(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' && !timerLoading) handleStartTimer();
                      }}
                      className="w-full bg-white dark:bg-slate-800 px-4 py-2.5 text-sm border border-slate-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-emerald-500/20 focus:border-emerald-500/70 transition-all duration-200 shadow-sm placeholder:text-slate-400"
                    />
                  </div>
                  <button
                    onClick={handleStartTimer}
                    disabled={timerLoading}
                    className="group inline-flex items-center justify-center gap-2 w-full sm:w-auto px-6 py-3 text-xs sm:text-sm font-bold text-white bg-emerald-500 hover:bg-emerald-600 rounded-xl disabled:opacity-60 transition-all duration-200 shadow-sm hover:shadow-md active:scale-[0.97]"
                  >
                    <Play className="w-4 h-4" fill="currentColor" strokeWidth={0} />
                    {t('serviceOps.timeTracking.activeTimer.start')}
                  </button>
                </div>
              </div>
            )}

            {/* 图表区 - 简洁卡片容器 */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 sm:gap-5">
              {/* 按用户工时分布 - 简洁排行榜 */}
              <div className="rounded-2xl bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shadow-sm overflow-hidden">
                <div className="px-4 sm:px-6 py-4 sm:py-5 border-b border-slate-100 dark:border-slate-700/40">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2.5">
                      <div className="p-2 bg-blue-50 dark:bg-blue-950/30 rounded-xl text-blue-600 dark:text-blue-400">
                        <User className="w-4 h-4" strokeWidth={2.2} />
                      </div>
                      <div>
                        <h3 className="text-sm font-bold text-slate-800 dark:text-slate-100 tracking-tight">{t('serviceOps.timeTracking.report.byUserTitle')}</h3>
                        <p className="text-[11px] text-slate-400 dark:text-slate-500 font-medium mt-0.5">{t('serviceOps.timeTracking.report.byUserSubtitle')}</p>
                      </div>
                    </div>
                  </div>
                </div>

                <div className="p-4 sm:p-6 space-y-3.5 custom-scrollbar max-h-[28rem] overflow-y-auto">
                  {report?.byUser.length ? (
                    report.byUser
                      .sort((a, b) => b.totalMinutes - a.totalMinutes)
                      .slice(0, 8)
                      .map((item, rankIdx) => {
                        const percent = Math.max(4, (item.totalMinutes / maxUserMinutes) * 100);
                        const initials = getInitials(item.label);
                        const avatarColor = colorFromString(item.label);
                        const rankBadge = rankIdx === 0 ? 'from-amber-400 to-yellow-500 text-white shadow-md shadow-amber-400/40'
                          : rankIdx === 1 ? 'from-slate-300 to-slate-400 text-white shadow-md shadow-slate-400/30'
                          : rankIdx === 2 ? 'from-orange-400 to-amber-500 text-white shadow-md shadow-orange-400/30'
                          : 'from-gray-100 to-gray-200 text-gray-500 dark:from-slate-700 dark:to-slate-600 dark:text-slate-400';
                        return (
                          <div key={item.label} className="flex items-center gap-3.5 group/item p-3 -mx-2 rounded-2xl hover:bg-slate-50 dark:hover:bg-slate-700/40 transition-all duration-200">
                            {/* 排名徽章 */}
                            <div className={`flex-shrink-0 w-7 h-7 rounded-lg bg-gradient-to-br ${rankBadge} flex items-center justify-center text-[11px] font-black`}>
                              {rankIdx + 1}
                            </div>
                            {/* 头像 */}
                            <div className={`w-10 h-10 rounded-xl ${avatarColor} text-white text-xs font-black flex items-center justify-center flex-shrink-0 shadow-md ring-2 ring-white dark:ring-slate-800`}>
                              {initials}
                            </div>
                            <div className="flex-1 min-w-0">
                              <div className="flex items-center justify-between gap-2 mb-1.5">
                                <span className="text-sm font-bold text-slate-800 dark:text-slate-200 truncate">
                                  {item.label}
                                </span>
                                <span className="text-[11px] font-semibold text-slate-400 dark:text-slate-500 flex-shrink-0">
                                  {t('serviceOps.timeTracking.report.countSuffix', { count: item.count })}
                                </span>
                              </div>
                              {/* 进度条 */}
                              <div className="w-full bg-slate-100 dark:bg-slate-700/60 rounded-full h-8 overflow-hidden relative">
                                <div
                                  className="relative h-8 rounded-full flex items-center justify-end pr-3 transition-all duration-700 ease-out bg-blue-500"
                                  style={{ width: `${percent}%` }}
                                >
                                  <span className="relative z-10 text-[11px] text-white font-black whitespace-nowrap">
                                    {formatDuration(item.totalMinutes)}
                                  </span>
                                </div>
                              </div>
                            </div>
                          </div>
                        );
                      })
                  ) : (
                    <EmptyState icon={BarChart3} title={t('common.empty')} description={t('serviceOps.timeTracking.report.emptyHint')} />
                  )}
                </div>
              </div>

              {/* 按日期工时分布 - 简洁柱状图 */}
              <div className="rounded-2xl bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shadow-sm overflow-hidden">
                <div className="px-4 sm:px-6 py-4 sm:py-5 border-b border-slate-100 dark:border-slate-700/40">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2.5">
                      <div className="p-2 bg-blue-50 dark:bg-blue-950/30 rounded-xl text-blue-600 dark:text-blue-400">
                        <CalendarDays className="w-4 h-4" strokeWidth={2.2} />
                      </div>
                      <div>
                        <h3 className="text-sm font-bold text-slate-800 dark:text-slate-100 tracking-tight">{t('serviceOps.timeTracking.report.byDateTitle')}</h3>
                        <p className="text-[11px] text-slate-400 dark:text-slate-500 font-medium mt-0.5">{t('serviceOps.timeTracking.report.byDateSubtitle')}</p>
                      </div>
                    </div>
                  </div>
                </div>

                <div className="p-4 sm:p-6 pt-8 pb-6">
                  {report?.byDate.length ? (
                    <div className="flex items-end justify-between gap-2.5 h-52 relative">
                      {/* 背景网格参考线 */}
                      <div className="absolute inset-x-6 top-0 bottom-8 flex flex-col justify-between pointer-events-none opacity-50">
                        {[...Array(4)].map((_, i) => (
                          <div key={i} className="border-t border-dashed border-slate-200 dark:border-slate-700/60" />
                        ))}
                      </div>

                      {report.byDate
                        .slice()
                        .reverse()
                        .map((item) => {
                          const heightPercent = (item.totalMinutes / maxDateMinutes) * 100;
                          const isToday = item.label === new Date().toISOString().slice(0, 10);
                          return (
                            <div key={item.label} className="relative flex-1 flex flex-col items-center gap-3 group/bar">
                              {/* Tooltip */}
                              <div className={`absolute -top-1.5 left-1/2 -translate-x-1/2 -translate-y-full px-3 py-1.5 rounded-xl text-[11px] font-black whitespace-nowrap transition-all duration-200 pointer-events-none z-20 shadow-md ${
                                item.totalMinutes > 0
                                  ? 'bg-slate-800 dark:bg-slate-700 text-white opacity-0 group-hover/bar:opacity-100'
                                  : 'opacity-0'
                              }`}>
                                {formatDuration(item.totalMinutes)}
                                <div className="absolute left-1/2 -translate-x-1/2 bottom-0 translate-y-1.5 w-2.5 h-2.5 rotate-45 bg-slate-800 dark:bg-slate-700 rounded-[2px]" />
                              </div>

                              {/* 柱子容器 */}
                              <div className="w-full flex-1 flex items-end relative px-1">
                                {/* 基座 */}
                                <div className="absolute bottom-0 inset-x-1 h-1 rounded-full bg-slate-200/60 dark:bg-slate-700/50" />
                                {/* 柱体 */}
                                <div
                                  className={`relative w-full rounded-t-xl transition-all duration-700 ease-out ${
                                    isToday
                                      ? 'bg-blue-500'
                                      : 'bg-blue-300 dark:bg-blue-400/60'
                                  }`}
                                  style={{ height: `${Math.max(heightPercent >= 2 ? 2 : 0, heightPercent)}%`, minHeight: item.totalMinutes > 0 ? '8px' : '0' }}
                                />
                              </div>

                              {/* 日期标签 */}
                              <div className={`text-[11px] whitespace-nowrap px-2 py-1 rounded-lg font-bold transition-all ${
                                isToday
                                  ? 'bg-blue-600 text-white'
                                  : 'text-slate-400 dark:text-slate-500 group-hover/bar:text-slate-700 dark:group-hover/bar:text-slate-300'
                              }`}>
                                {item.label.slice(5)}
                              </div>
                            </div>
                          );
                        })}
                    </div>
                  ) : (
                    <EmptyState icon={Activity} title={t('common.empty')} description={t('serviceOps.timeTracking.report.emptyHint')} />
                  )}
                </div>
              </div>
            </div>
          </div>
        ) : null
      )}

      {/* ====== 列表 Tab ====== */}
      {activeTab === 'list' && (
        loading && entries.length === 0 ? <ListSkeleton /> :
        <div className="space-y-4 sm:space-y-5">
          {/* 筛选栏 - 简洁工具栏 */}
          <div className="rounded-2xl bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shadow-sm p-4 sm:p-6">
            <div className="flex flex-col sm:flex-row sm:items-end gap-4 sm:gap-5">
              {/* 搜索框 */}
              <div className="flex-1 min-w-0 sm:max-w-xs">
                <label className={`${labelClass} !text-[10.5px] !font-bold !tracking-wider !uppercase`}>{t('serviceOps.timeTracking.filters.keyword')}</label>
                <div className="relative group">
                  <div className="absolute inset-y-0 left-0 pl-3.5 flex items-center pointer-events-none">
                    <Search className="w-4 h-4 text-slate-400 group-focus-within:text-blue-500 transition-colors" strokeWidth={2} />
                  </div>
                  <input
                    type="text"
                    placeholder={t('serviceOps.timeTracking.filters.keywordPlaceholder')}
                    value={searchInput}
                    onChange={(e) => {
                      setSearchInput(e.target.value);
                      applySearch(e.target.value);
                    }}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') triggerSearchNow();
                    }}
                    onBlur={triggerSearchNow}
                    className="w-full pl-11 pr-11 py-2.5 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-600 rounded-xl text-sm placeholder:text-slate-400 text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500/70 transition-all duration-200 shadow-sm"
                  />
                  {searchInput && (
                    <button
                      onClick={clearSearch}
                      className="absolute right-2.5 top-1/2 -translate-y-1/2 p-1.5 text-slate-400 hover:text-rose-500 bg-slate-50 hover:bg-rose-50 dark:bg-slate-700 dark:hover:bg-rose-500/10 rounded-lg transition-all duration-200"
                      aria-label={t('serviceOps.timeTracking.filters.clearSearch')}
                    >
                      <XCircle className="w-4 h-4" strokeWidth={2.2} />
                    </button>
                  )}
                </div>
              </div>

              {/* 类型筛选 */}
              <div className="min-w-0 sm:w-36">
                <label className={`${labelClass} !text-[10.5px] !font-bold !tracking-wider !uppercase`}>{t('serviceOps.timeTracking.filters.entryType')}</label>
                <select
                  value={entryTypeFilter ?? ''}
                  onChange={(e) => {
                    setEntryTypeFilter(e.target.value === '' ? null : (e.target.value as 'timer' | 'manual'));
                    setPage(1);
                  }}
                  className={`${selectClass(!!entryTypeFilter)} !py-2.5 !rounded-xl !shadow-sm font-semibold`}
                >
                  <option value="">{t('serviceOps.timeTracking.filters.allTypes')}</option>
                  <option value="timer">{t('serviceOps.timeTracking.filters.timerOption')}</option>
                  <option value="manual">{t('serviceOps.timeTracking.filters.manualOption')}</option>
                </select>
              </div>

              {/* 计费筛选 */}
              <div className="min-w-0 sm:w-36">
                <label className={`${labelClass} !text-[10.5px] !font-bold !tracking-wider !uppercase`}>{t('serviceOps.timeTracking.filters.billableStatus')}</label>
                <select
                  value={billableFilter === null ? '' : String(billableFilter)}
                  onChange={(e) => {
                    const v = e.target.value;
                    setBillableFilter(v === '' ? null : v === 'true');
                    setPage(1);
                  }}
                  className={`${selectClass(billableFilter !== null)} !py-2.5 !rounded-xl !shadow-sm font-semibold`}
                >
                  <option value="">{t('serviceOps.timeTracking.filters.allBillable')}</option>
                  <option value="true">{t('serviceOps.timeTracking.filters.billableOption')}</option>
                  <option value="false">{t('serviceOps.timeTracking.filters.nonBillableOption')}</option>
                </select>
              </div>

              {/* 日期范围 */}
              <div className="min-w-0 sm:w-40">
                <label className={`${labelClass} !text-[10.5px] !font-bold !tracking-wider !uppercase`}>{t('serviceOps.timeTracking.filters.startDate')}</label>
                <input
                  type="date"
                  value={startDate}
                  onChange={(e) => {
                    setStartDate(e.target.value);
                    setPage(1);
                  }}
                  className={`${selectClass(!!startDate)} !py-2.5 !rounded-xl !shadow-sm font-semibold`}
                />
              </div>
              <div className="min-w-0 sm:w-40">
                <label className={`${labelClass} !text-[10.5px] !font-bold !tracking-wider !uppercase`}>{t('serviceOps.timeTracking.filters.endDate')}</label>
                <input
                  type="date"
                  value={endDate}
                  onChange={(e) => {
                    setEndDate(e.target.value);
                    setPage(1);
                  }}
                  className={`${selectClass(!!endDate)} !py-2.5 !rounded-xl !shadow-sm font-semibold`}
                />
              </div>

              {/* 重置按钮 */}
              {hasActiveFilters && (
                <button
                  onClick={() => {
                    setSearchInput('');
                    setSearch('');
                    setEntryTypeFilter(null);
                    setBillableFilter(null);
                    setStartDate('');
                    setEndDate('');
                    setPage(1);
                  }}
                  className="group inline-flex items-center gap-2 px-4 py-2.5 text-sm font-bold text-slate-600 dark:text-slate-400 hover:text-rose-600 dark:hover:text-rose-400 bg-slate-100 hover:bg-slate-200 dark:bg-slate-700 dark:hover:bg-slate-600 border border-slate-200 dark:border-slate-600 hover:border-rose-200 dark:hover:border-rose-500/30 rounded-xl transition-all duration-200 shadow-sm hover:shadow-md whitespace-nowrap"
                >
                  <RotateCcw className="w-4 h-4 transition-transform group-hover:-rotate-180 duration-500" strokeWidth={2.2} />
                  {t('serviceOps.timeTracking.filters.reset')}
                </button>
              )}
            </div>

            {/* 搜索结果统计 */}
            {hasActiveFilters && (
              <div className="mt-4 pt-4 border-t border-slate-100 dark:border-slate-700/40 flex items-center gap-2.5">
                <div className="p-1.5 rounded-lg bg-blue-50 dark:bg-blue-950/30 text-blue-600 dark:text-blue-400">
                  <Search className="w-3.5 h-3.5" strokeWidth={2.3} />
                </div>
                <span className="text-sm text-slate-500 dark:text-slate-400">
                  {t('serviceOps.timeTracking.filters.foundPrefix')}<span className="font-bold text-blue-600 dark:text-blue-400 text-base">{totalItems}</span>{t('serviceOps.timeTracking.filters.foundSuffix')}
                </span>
              </div>
            )}
          </div>

          {/* 计时器区域 - 简洁蓝色边框卡片 */}
          {activeTimer && (
            <div className="bg-blue-50 dark:bg-blue-950/20 p-4 sm:p-6 rounded-2xl border border-blue-200 dark:border-blue-800 animate-[fadeIn_0.4s_ease-out]">
              <div className="flex items-center gap-4 sm:gap-5 flex-wrap">
                <div className="relative p-4 bg-blue-100 dark:bg-blue-900/40 rounded-2xl border border-blue-200 dark:border-blue-800 text-blue-600 dark:text-blue-400">
                  <Timer className="w-7 h-7" strokeWidth={2.2} />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="inline-flex items-center gap-2 px-2.5 py-0.5 rounded-full bg-blue-100 dark:bg-blue-900/40 border border-blue-200 dark:border-blue-800 text-[11px] font-bold text-blue-700 dark:text-blue-300 mb-2">
                    <span className="w-1.5 h-1.5 rounded-full bg-blue-500 animate-pulse" />
                    {t('serviceOps.timeTracking.activeTimer.badge')}
                  </div>
                  <div className="text-lg font-bold text-slate-900 dark:text-slate-100 tracking-tight truncate">
                    {activeTimer.description || t('serviceOps.common.noDescription')}
                  </div>
                  <div className="flex items-center gap-3 mt-1.5 text-xs text-slate-500 dark:text-slate-400 flex-wrap">
                    <span className="inline-flex items-center gap-1.5">
                      <Clock className="w-3.5 h-3.5" />
                      {formatDateTime(activeTimer.startTime)}
                    </span>
                    <span className="opacity-50">·</span>
                    <div className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-xl bg-blue-100 dark:bg-blue-900/40 border border-blue-200 dark:border-blue-800">
                      <span className="text-blue-700 dark:text-blue-300 text-[10.5px] font-bold uppercase tracking-wider">{t('serviceOps.timeTracking.activeTimer.elapsed')}</span>
                      <span className="text-blue-700 dark:text-blue-300 font-black font-mono text-lg tracking-wider">
                        {formatStopwatch(liveSeconds)}
                      </span>
                    </div>
                  </div>
                </div>
                <button
                  onClick={handleStopTimer}
                  disabled={timerLoading}
                  className="group inline-flex items-center gap-2 px-5 py-3 text-sm font-bold text-white bg-red-500 hover:bg-red-600 rounded-xl disabled:opacity-60 transition-all duration-200 shadow-sm hover:shadow-md active:scale-[0.97]"
                >
                  <Square className="w-4 h-4" strokeWidth={2.4} fill="currentColor" />
                  {t('serviceOps.timeTracking.activeTimer.stop')}
                </button>
              </div>
            </div>
          )}

          {/* 时间记录列表 - 简洁卡片容器 */}
          <div className="rounded-2xl bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shadow-sm overflow-hidden">
            {entries.length === 0 ? (
              <EmptyState
                icon={Clock}
                title={hasActiveFilters ? t('serviceOps.timeTracking.list.emptyFiltered') : t('serviceOps.timeTracking.list.emptyTitle')}
                description={hasActiveFilters ? t('serviceOps.timeTracking.list.emptyFilteredHint') : t('serviceOps.timeTracking.list.emptyHint')}
              />
            ) : (
              <>
                {/* 表头 */}
                <div className="relative hidden md:flex items-center gap-4 px-6 py-4 bg-slate-50 dark:bg-slate-700/30 border-b border-slate-100 dark:border-slate-700/40">
                  <div className="w-14 flex-shrink-0" />
                  <div className="flex-1 inline-flex items-center gap-2 text-[11px] font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider">
                    <FileText className="w-3.5 h-3.5" />
                    {t('serviceOps.timeTracking.list.colDescription')}
                  </div>
                  <div className="inline-flex items-center gap-2 text-[11px] font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider w-28 text-right justify-end">
                    <Timer className="w-3.5 h-3.5" />
                    {t('serviceOps.timeTracking.list.colDuration')}
                  </div>
                  <div className="w-24 flex-shrink-0" />
                </div>

                <div className="hidden md:block divide-y divide-slate-100 dark:divide-slate-700/40 custom-scrollbar">
                  {entries.map((entry, rowIdx) => {
                    const TypeIcon = ENTRY_TYPE_ICON[entry.entryType] ?? FileText;
                    const typeIconBg = entry.entryType === 'timer'
                      ? 'bg-sky-50 dark:bg-sky-950/30 text-sky-600 dark:text-sky-400'
                      : 'bg-purple-50 dark:bg-purple-950/30 text-purple-600 dark:text-purple-400';
                    const typeRing = entry.entryType === 'timer'
                      ? 'ring-2 ring-sky-500/20'
                      : 'ring-2 ring-purple-500/20';
                    return (
                      <div
                        key={entry.id}
                        className="group relative px-6 py-4 flex items-center gap-4 transition-colors duration-200 hover:bg-slate-50 dark:hover:bg-slate-700/30 animate-[fadeIn_0.3s_ease-out]"
                        style={{ animationDelay: `${rowIdx * 20}ms` }}
                      >
                        {/* 图标 */}
                        <div className={`relative w-14 h-14 rounded-2xl flex items-center justify-center flex-shrink-0 ${typeRing} ${typeIconBg}`}>
                          <TypeIcon className="relative z-10 w-5 h-5" strokeWidth={2.2} />
                        </div>

                        {/* 内容 */}
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2 flex-wrap mb-1.5">
                            <h4 className="text-sm font-bold text-slate-800 dark:text-slate-100 truncate leading-tight">
                              {entry.description || t('serviceOps.common.noDescription')}
                            </h4>
                            {/* 类型徽章 */}
                            <span
                              className={`inline-flex items-center gap-1 px-2.5 py-0.5 text-[10.5px] font-bold rounded-lg ${
                                entry.entryType === 'timer'
                                  ? 'bg-sky-50 dark:bg-sky-950/30 text-sky-700 dark:text-sky-300 ring-1 ring-sky-500/20'
                                  : 'bg-purple-50 dark:bg-purple-950/30 text-purple-700 dark:text-purple-300 ring-1 ring-purple-500/20'
                              }`}
                            >
                              {entryTypeLabel(entry.entryType)}
                            </span>
                            {/* 计费徽章 */}
                            {entry.billable ? (
                              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 text-[10.5px] font-bold rounded-lg bg-emerald-50 dark:bg-emerald-950/30 text-emerald-700 dark:text-emerald-300 ring-1 ring-emerald-500/20">
                                <CheckCircle2 className="w-3.5 h-3.5" strokeWidth={2.5} />
                                {t('serviceOps.timeTracking.list.billable')}
                              </span>
                            ) : (
                              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 text-[10.5px] font-bold rounded-lg bg-slate-100 dark:bg-slate-700/50 text-slate-500 dark:text-slate-400 ring-1 ring-slate-200 dark:ring-slate-600/60">
                                <XCircle className="w-3.5 h-3.5" strokeWidth={2.2} />
                                {t('serviceOps.timeTracking.list.nonBillable')}
                              </span>
                            )}
                            {/* 运行中徽章 */}
                            {entry.isRunning && (
                              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 text-[10.5px] font-bold rounded-lg bg-sky-50 dark:bg-sky-950/30 text-sky-700 dark:text-sky-300 ring-1 ring-sky-500/20">
                                <Timer className="w-3.5 h-3.5 animate-pulse" strokeWidth={2.3} />
                                {t('serviceOps.timeTracking.list.inProgress')}
                              </span>
                            )}
                          </div>
                          {/* 次级元数据 */}
                          <div className="text-[11.5px] text-slate-500 dark:text-slate-400 flex items-center gap-2.5 flex-wrap font-medium">
                            {entry.conversationNumber && (
                              <span className="inline-flex items-center gap-1.5 px-2 py-0.5 bg-slate-50 dark:bg-slate-700/40 rounded-lg">
                                <Ticket className="w-3 h-3 text-blue-500" strokeWidth={2.2} />
                                <span className="font-bold text-slate-700 dark:text-slate-300">#{entry.conversationNumber}</span>
                                {entry.conversationSubject && <span className="truncate max-w-[180px]">· {entry.conversationSubject}</span>}
                              </span>
                            )}
                            {entry.customerName && (
                              <span className="inline-flex items-center gap-1.5 px-2 py-0.5 bg-slate-50 dark:bg-slate-700/40 rounded-lg">
                                <Briefcase className="w-3 h-3 text-blue-500" strokeWidth={2.2} />
                                <span className="truncate max-w-[140px]">{entry.customerName}</span>
                              </span>
                            )}
                            <span className="inline-flex items-center gap-1.5 px-2 py-0.5 bg-slate-50 dark:bg-slate-700/40 rounded-lg">
                              <User className="w-3 h-3 text-indigo-500" strokeWidth={2.2} />
                              <span className="truncate max-w-[100px]">{entry.userName}</span>
                            </span>
                          </div>
                          <div className="text-[11px] text-slate-400 dark:text-slate-500 mt-1.5 flex items-center gap-2.5 font-medium">
                            <span className="inline-flex items-center gap-1">
                              <Clock className="w-3 h-3" strokeWidth={2} />
                              {t('serviceOps.timeTracking.list.startedAt', { time: formatDateTime(entry.startTime) })}
                            </span>
                            {entry.endTime && (
                              <>
                                <span className="opacity-40">→</span>
                                <span className="inline-flex items-center gap-1">
                                  <Clock className="w-3 h-3" strokeWidth={2} />
                                  {t('serviceOps.timeTracking.list.endedAt', { time: formatDateTime(entry.endTime) })}
                                </span>
                              </>
                            )}
                          </div>
                        </div>

                        {/* 时长 */}
                        <div className="flex flex-col items-end gap-1 flex-shrink-0 w-28">
                          <div className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl ring-1 ${
                            entry.isRunning
                              ? 'bg-sky-50 dark:bg-sky-950/30 text-sky-700 dark:text-sky-300 ring-sky-500/30'
                              : 'bg-blue-50 dark:bg-blue-950/30 text-blue-700 dark:text-blue-300 ring-blue-500/20'
                          }`}>
                            <Timer className="w-3.5 h-3.5" strokeWidth={2.3} />
                            <span className="text-base font-bold tracking-tight whitespace-nowrap">
                              {formatDuration(entry.durationMinutes)}
                            </span>
                          </div>
                        </div>

                        {/* 操作按钮 */}
                        <div className="flex items-center gap-1.5 flex-shrink-0 w-24 justify-end">
                          <button
                            onClick={() => openEdit(entry)}
                            className="group/edit relative p-2.5 text-slate-400 hover:text-blue-600 dark:text-slate-400 dark:hover:text-blue-400 bg-white dark:bg-slate-800 hover:bg-blue-50 dark:hover:bg-blue-950/30 rounded-xl ring-1 ring-slate-200 dark:ring-slate-700 hover:ring-blue-400/40 transition-all duration-200 hover:shadow-sm"
                            title={t('common.edit')}
                          >
                            <Edit2 className="w-4 h-4" strokeWidth={2.1} />
                          </button>
                          <button
                            onClick={() => setDeleteId(entry.id)}
                            className="group/del relative p-2.5 text-slate-400 hover:text-rose-600 dark:text-slate-400 dark:hover:text-rose-400 bg-white dark:bg-slate-800 hover:bg-rose-50 dark:hover:bg-rose-950/30 rounded-xl ring-1 ring-slate-200 dark:ring-slate-700 hover:ring-rose-400/40 transition-all duration-200 hover:shadow-sm"
                            title={t('common.delete')}
                          >
                            <Trash2 className="w-4 h-4" strokeWidth={2.1} />
                          </button>
                        </div>
                      </div>
                    );
                  })}
                </div>

                {/* 移动端卡片视图 */}
                <div className="md:hidden divide-y divide-slate-100 dark:divide-slate-700/40">
                  {entries.map((entry) => {
                    const TypeIcon = ENTRY_TYPE_ICON[entry.entryType] ?? FileText;
                    return (
                      <div key={entry.id} className="p-3.5 hover:bg-slate-50 dark:hover:bg-slate-700/30 transition-colors duration-200">
                        {/* 顶部：图标 + 描述 + 时长 */}
                        <div className="flex items-start gap-3 mb-2.5">
                          <div className={`relative w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0 ${
                            entry.entryType === 'timer'
                              ? 'bg-sky-50 dark:bg-sky-950/30 text-sky-600 dark:text-sky-400 ring-2 ring-sky-500/20'
                              : 'bg-purple-50 dark:bg-purple-950/30 text-purple-600 dark:text-purple-400 ring-2 ring-purple-500/20'
                          }`}>
                            <TypeIcon className="w-4 h-4" strokeWidth={2.2} />
                          </div>
                          <div className="flex-1 min-w-0">
                            <h4 className="text-sm font-bold text-slate-800 dark:text-slate-100 truncate leading-tight">
                              {entry.description || t('serviceOps.common.noDescription')}
                            </h4>
                            <div className={`inline-flex items-center gap-1.5 px-2.5 py-1 mt-1.5 rounded-lg text-[10.5px] font-bold ${
                              entry.isRunning
                                ? 'bg-sky-50 dark:bg-sky-950/30 text-sky-700 dark:text-sky-300 ring-1 ring-sky-500/20'
                                : 'bg-blue-50 dark:bg-blue-950/30 text-blue-700 dark:text-blue-300 ring-1 ring-blue-500/20'
                            }`}>
                              <Timer className="w-3.5 h-3.5" strokeWidth={2.3} />
                              <span className="text-sm font-bold whitespace-nowrap">
                                {formatDuration(entry.durationMinutes)}
                              </span>
                            </div>
                          </div>
                        </div>
                        {/* 徽章 */}
                        <div className="flex items-center gap-1.5 flex-wrap mb-2.5">
                          <span className={`inline-flex items-center gap-1 px-2 py-0.5 text-[10.5px] font-bold rounded-lg ${
                            entry.entryType === 'timer'
                              ? 'bg-sky-50 dark:bg-sky-950/30 text-sky-700 dark:text-sky-300 ring-1 ring-sky-500/20'
                              : 'bg-purple-50 dark:bg-purple-950/30 text-purple-700 dark:text-purple-300 ring-1 ring-purple-500/20'
                          }`}>
                            {entryTypeLabel(entry.entryType)}
                          </span>
                          {entry.billable ? (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[10.5px] font-bold rounded-lg bg-emerald-50 dark:bg-emerald-950/30 text-emerald-700 dark:text-emerald-300 ring-1 ring-emerald-500/20">
                              <CheckCircle2 className="w-3 h-3" strokeWidth={2.5} />
                              {t('serviceOps.timeTracking.list.billable')}
                            </span>
                          ) : (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[10.5px] font-bold rounded-lg bg-slate-100 dark:bg-slate-700/50 text-slate-500 dark:text-slate-400 ring-1 ring-slate-200 dark:ring-slate-600/60">
                              <XCircle className="w-3 h-3" strokeWidth={2.2} />
                              {t('serviceOps.timeTracking.list.nonBillable')}
                            </span>
                          )}
                          {entry.isRunning && (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[10.5px] font-bold rounded-lg bg-sky-50 dark:bg-sky-950/30 text-sky-700 dark:text-sky-300 ring-1 ring-sky-500/20">
                              <Timer className="w-3 h-3 animate-pulse" strokeWidth={2.3} />
                              {t('serviceOps.timeTracking.list.inProgress')}
                            </span>
                          )}
                        </div>
                        {/* 关联信息 */}
                        <div className="flex items-center gap-1.5 flex-wrap mb-2">
                          {entry.conversationNumber && (
                            <span className="inline-flex items-center gap-1.5 px-2 py-0.5 bg-slate-50 dark:bg-slate-700/40 rounded-lg text-[11px] text-slate-500 dark:text-slate-400">
                              <Ticket className="w-3 h-3 text-blue-500" strokeWidth={2.2} />
                              <span className="font-bold text-slate-700 dark:text-slate-300">#{entry.conversationNumber}</span>
                            </span>
                          )}
                          {entry.customerName && (
                            <span className="inline-flex items-center gap-1.5 px-2 py-0.5 bg-slate-50 dark:bg-slate-700/40 rounded-lg text-[11px] text-slate-500 dark:text-slate-400">
                              <Briefcase className="w-3 h-3 text-blue-500" strokeWidth={2.2} />
                              <span className="truncate max-w-[100px]">{entry.customerName}</span>
                            </span>
                          )}
                          <span className="inline-flex items-center gap-1.5 px-2 py-0.5 bg-slate-50 dark:bg-slate-700/40 rounded-lg text-[11px] text-slate-500 dark:text-slate-400">
                            <User className="w-3 h-3 text-indigo-500" strokeWidth={2.2} />
                            <span className="truncate max-w-[80px]">{entry.userName}</span>
                          </span>
                        </div>
                        {/* 时间 */}
                        <div className="text-[11px] text-slate-400 dark:text-slate-500 mb-2.5 flex items-center gap-1.5 flex-wrap">
                          <span className="inline-flex items-center gap-1">
                            <Clock className="w-3 h-3" strokeWidth={2} />
                            {formatDateTime(entry.startTime)}
                          </span>
                          {entry.endTime && (
                            <>
                              <span className="opacity-40">→</span>
                              <span className="inline-flex items-center gap-1">
                                <Clock className="w-3 h-3" strokeWidth={2} />
                                {formatDateTime(entry.endTime)}
                              </span>
                            </>
                          )}
                        </div>
                        {/* 操作按钮 */}
                        <div className="flex items-center gap-2">
                          <button
                            onClick={() => openEdit(entry)}
                            className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-semibold text-slate-600 dark:text-slate-300 bg-white dark:bg-slate-800 hover:bg-blue-50 dark:hover:bg-blue-950/30 border border-slate-200 dark:border-slate-700 hover:border-blue-400/40 rounded-lg transition-all"
                          >
                            <Edit2 className="w-3.5 h-3.5" strokeWidth={2.1} />
                            {t('common.edit')}
                          </button>
                          <button
                            onClick={() => setDeleteId(entry.id)}
                            className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-semibold text-slate-600 dark:text-slate-300 bg-white dark:bg-slate-800 hover:bg-rose-50 dark:hover:bg-rose-950/30 border border-slate-200 dark:border-slate-700 hover:border-rose-400/40 rounded-lg transition-all"
                          >
                            <Trash2 className="w-3.5 h-3.5" strokeWidth={2.1} />
                            {t('common.delete')}
                          </button>
                        </div>
                      </div>
                    );
                  })}
                </div>

                {/* 分页 - 简洁控制栏 */}
                {totalPages > 1 && (
                  <div className="flex flex-wrap items-center justify-between gap-3 px-4 sm:px-6 py-4 sm:py-5 border-t border-slate-100 dark:border-slate-700/40 bg-slate-50 dark:bg-slate-700/20">
                    <div className="flex items-center gap-2.5">
                      <div className="p-2 rounded-xl bg-blue-50 dark:bg-blue-950/30 text-blue-600 dark:text-blue-400">
                        <LayoutList className="w-4 h-4" strokeWidth={2.2} />
                      </div>
                      <div className="text-sm">
                        <span className="text-slate-500 dark:text-slate-400">{t('serviceOps.timeTracking.pagination.totalPrefix')}</span>
                        <span className="font-bold text-slate-800 dark:text-slate-100">{totalItems}</span>
                        <span className="text-slate-500 dark:text-slate-400">{t('serviceOps.timeTracking.pagination.totalMiddle')}</span>
                        <span className="font-bold text-blue-600 dark:text-blue-400">{page}</span>
                        <span className="text-slate-400 dark:text-slate-500"> / </span>
                        <span className="font-bold text-slate-600 dark:text-slate-400">{totalPages}</span>
                        <span className="text-slate-500 dark:text-slate-400">{t('serviceOps.timeTracking.pagination.totalSuffix')}</span>
                      </div>
                    </div>

                    <div className="flex items-center gap-2">
                      <button
                        onClick={() => setPage((p) => Math.max(1, p - 1))}
                        disabled={page <= 1}
                        className="group/prev inline-flex items-center gap-1.5 px-4 py-2.5 text-sm font-bold text-slate-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-600 rounded-xl hover:bg-slate-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition-all duration-200 hover:shadow-sm"
                      >
                        <ChevronLeft className="w-4 h-4" strokeWidth={2.3} />
                        {t('serviceOps.common.previousPage')}
                      </button>

                      <div className="flex items-center gap-1 p-1 bg-slate-100 dark:bg-slate-700/40 rounded-xl border border-slate-200 dark:border-slate-600">
                        {pageNumbers.map((pn) => (
                          <button
                            key={pn}
                            onClick={() => setPage(pn)}
                            className={`relative inline-flex items-center justify-center min-w-[38px] h-9 px-2.5 text-sm font-bold rounded-lg transition-all duration-200 ${
                              pn === page
                                ? 'bg-blue-600 text-white shadow-sm'
                                : 'text-slate-600 dark:text-slate-400 hover:bg-white dark:hover:bg-slate-600 hover:text-slate-900 dark:hover:text-slate-200'
                            }`}
                          >
                            {pn}
                          </button>
                        ))}
                      </div>

                      <button
                        onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                        disabled={page >= totalPages}
                        className="group/next inline-flex items-center gap-1.5 px-4 py-2.5 text-sm font-bold text-slate-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-600 rounded-xl hover:bg-slate-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition-all duration-200 hover:shadow-sm"
                      >
                        {t('serviceOps.common.nextPage')}
                        <ChevronRight className="w-4 h-4" strokeWidth={2.3} />
                      </button>
                    </div>
                  </div>
                )}
              </>
            )}
          </div>
        </div>
      )}

      {/* ====== 新建/编辑模态框 ====== */}
      <Modal
        open={showEditor}
        title={
          <div className="flex items-center gap-3">
            <div className="p-2 bg-primary-100 dark:bg-primary-900/30 rounded-lg">
              <Clock className="w-5 h-5 text-primary-600 dark:text-primary-400" />
            </div>
            <span>{editingEntry ? t('serviceOps.timeTracking.form.editorTitleEdit') : t('serviceOps.timeTracking.form.editorTitleCreate')}</span>
          </div>
        }
        onClose={() => setShowEditor(false)}
        size="lg"
        footer={
          <>
            <button
              onClick={() => setShowEditor(false)}
              className="w-full sm:w-auto px-5 py-2.5 text-xs sm:text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition-all"
            >
              {t('common.cancel')}
            </button>
            <button
              onClick={handleSave}
              disabled={saving}
              className="w-full sm:w-auto px-5 py-2.5 text-xs sm:text-sm font-medium text-white bg-primary-600 rounded-xl hover:bg-primary-700 disabled:opacity-50 transition-all shadow-sm hover:shadow-md active:scale-[0.98]"
            >
              {saving ? t('serviceOps.timeTracking.form.saving') : t('common.save')}
            </button>
          </>
        }
      >
        <div className="space-y-6">
          {formError && (
            <div className="p-4 text-sm text-red-700 dark:text-red-300 bg-red-50 dark:bg-red-900/20 rounded-xl border border-red-200 dark:border-red-800/50 flex items-start gap-3">
              <XCircle className="w-5 h-5 text-red-500 flex-shrink-0 mt-0.5" />
              <span>{formError}</span>
            </div>
          )}

          {/* 类型选择 */}
          <div className="space-y-4">
            <div className={sectionTitleClass}>
              <Activity className="w-4 h-4 text-primary-600 dark:text-primary-400" />
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('serviceOps.timeTracking.form.entryTypeSection')}</h3>
            </div>
            <div className="grid grid-cols-2 gap-3">
              {(['timer', 'manual'] as const).map((entryType) => {
                const TypeIcon = ENTRY_TYPE_ICON[entryType];
                return (
                  <button
                    key={entryType}
                    type="button"
                    onClick={() => setFormData({ ...formData, entryType })}
                    disabled={!!editingEntry}
                    className={`flex items-center gap-3 px-4 py-3 text-sm font-medium rounded-xl transition-all disabled:opacity-60 disabled:cursor-not-allowed ${
                      formData.entryType === entryType
                        ? 'bg-blue-600 hover:bg-blue-700 text-white shadow-sm'
                        : 'bg-slate-100 hover:bg-slate-200 text-slate-600 dark:bg-slate-700 dark:hover:bg-slate-600 dark:text-slate-300'
                    }`}
                  >
                    <TypeIcon className="w-5 h-5" />
                    <div className="text-left">
                      <div>{entryTypeLabel(entryType)}</div>
                      <div className={`text-xs font-normal mt-0.5 ${formData.entryType === entryType ? 'text-white/80' : 'text-slate-400 dark:text-slate-500'}`}>
                        {entryType === 'timer' ? t('serviceOps.timeTracking.form.entryTypeTimerHint') : t('serviceOps.timeTracking.form.entryTypeManualHint')}
                      </div>
                    </div>
                  </button>
                );
              })}
            </div>
          </div>

          {/* 基本信息 */}
          <div className="space-y-4">
            <div className={sectionTitleClass}>
              <FileText className="w-4 h-4 text-primary-600 dark:text-primary-400" />
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('serviceOps.timeTracking.form.basicSection')}</h3>
            </div>

            {/* 描述 */}
            <div>
              <label className={labelClass}>
                {t('serviceOps.timeTracking.form.descriptionLabel')}<span className="text-red-500 normal-case">*</span>
              </label>
              <textarea
                value={formData.description}
                onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                rows={3}
                className={`${inputClass} resize-none`}
                placeholder={t('serviceOps.timeTracking.form.descriptionPlaceholder')}
              />
            </div>

            {/* 关联工单 / 客户 */}
            {!editingEntry && (
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
                <div>
                  <label className={labelClass}>{t('serviceOps.timeTracking.form.conversationLabel')}</label>
                  <select
                    value={formData.conversationId}
                    onChange={(e) => {
                      const convId = e.target.value;
                      // 选中工单后自动联动填充关联客户
                      const conv = convId ? activeConversations.find((c) => String(c.id) === convId) : null;
                      setFormData({
                        ...formData,
                        conversationId: convId,
                        customerId: conv?.customerId ? String(conv.customerId) : formData.customerId,
                      });
                    }}
                    disabled={optionsLoading}
                    className={`${selectClass(!!formData.conversationId)} !py-2.5 !rounded-xl !shadow-sm font-semibold`}
                  >
                    <option value="">{optionsLoading ? t('common.loading') : t('serviceOps.timeTracking.form.noConversation')}</option>
                    {activeConversations.map((c) => {
                      const custName = c.customer?.fullName || c.customerEmail || t('serviceOps.timeTracking.form.unknownCustomer');
                      const subjectText = c.subject || t('serviceOps.timeTracking.form.noSubject');
                      return (
                        <option key={c.id} value={String(c.id)}>
                          #{c.id} - {subjectText} - {custName}
                        </option>
                      );
                    })}
                  </select>
                  {activeConversations.length === 0 && !optionsLoading && (
                    <p className="mt-1 text-[10px] text-gray-400 dark:text-slate-500">{t('serviceOps.timeTracking.form.noActiveConversations')}</p>
                  )}
                </div>
                <div>
                  <label className={labelClass}>{t('serviceOps.timeTracking.form.customerLabel')}</label>
                  <select
                    value={formData.customerId}
                    onChange={(e) => setFormData({ ...formData, customerId: e.target.value })}
                    disabled={optionsLoading}
                    className={`${selectClass(!!formData.customerId)} !py-2.5 !rounded-xl !shadow-sm font-semibold`}
                  >
                    <option value="">{optionsLoading ? t('common.loading') : t('serviceOps.timeTracking.form.noCustomer')}</option>
                    {customers.map((c) => (
                      <option key={c.id} value={String(c.id)}>
                        #{c.id} - {c.fullName}
                      </option>
                    ))}
                  </select>
                  {customers.length === 0 && !optionsLoading && (
                    <p className="mt-1 text-[10px] text-gray-400 dark:text-slate-500">{t('serviceOps.timeTracking.form.noCustomers')}</p>
                  )}
                </div>
              </div>
            )}
          </div>

          {/* 时间信息 */}
          <div className="space-y-4">
            <div className={sectionTitleClass}>
              <Clock className="w-4 h-4 text-primary-600 dark:text-primary-400" />
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('serviceOps.timeTracking.form.timeSection')}</h3>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
              <div>
                <label className={labelClass}>
                  {t('serviceOps.timeTracking.form.startTimeLabel')}<span className="text-red-500 normal-case">*</span>
                </label>
                <input
                  type="datetime-local"
                  value={formData.startTime}
                  onChange={(e) => setFormData({ ...formData, startTime: e.target.value })}
                  className={inputClass}
                />
              </div>
              {(formData.entryType === 'manual' || editingEntry) && (
                <div>
                  <label className={labelClass}>
                    {t('serviceOps.timeTracking.form.durationLabel')}{formData.entryType === 'manual' && <span className="text-red-500 normal-case"> *</span>}
                  </label>
                  <input
                    type="number"
                    value={formData.durationMinutes}
                    onChange={(e) => setFormData({ ...formData, durationMinutes: e.target.value })}
                    className={inputClass}
                    placeholder={t('serviceOps.timeTracking.form.durationPlaceholder')}
                    min="0"
                  />
                </div>
              )}
            </div>

            {/* 快速时长预设 */}
            {(formData.entryType === 'manual' || editingEntry) && (
              <div className="flex items-center gap-2 flex-wrap">
                <span className="text-xs text-gray-400 dark:text-slate-500">{t('serviceOps.timeTracking.form.quickSelect')}</span>
                {DURATION_PRESETS.map((min) => (
                  <button
                    key={min}
                    type="button"
                    onClick={() => setFormData({ ...formData, durationMinutes: String(min) })}
                    className={`px-3 py-1 text-xs font-medium rounded-lg transition-all ${
                      formData.durationMinutes === String(min)
                        ? 'bg-primary-600 text-white shadow-sm'
                        : 'bg-gray-50 dark:bg-slate-900/50 text-gray-600 dark:text-slate-400 border border-gray-200 dark:border-slate-600 hover:bg-gray-100 dark:hover:bg-slate-700'
                    }`}
                  >
                    {min < 60 ? t('serviceOps.timeTracking.duration.minutes', { minutes: min }) : t('serviceOps.timeTracking.duration.hours', { hours: min / 60 })}
                  </button>
                ))}
              </div>
            )}
          </div>

          {/* 计费选项 */}
          <div className="space-y-4">
            <div className={sectionTitleClass}>
              <DollarSign className="w-4 h-4 text-primary-600 dark:text-primary-400" />
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('serviceOps.timeTracking.form.billingSection')}</h3>
            </div>

            {/* Billable 开关 */}
            <button
              type="button"
              onClick={() => setFormData({ ...formData, billable: !formData.billable })}
              className="w-full flex items-center justify-between p-4 bg-gray-50 dark:bg-slate-900/50 border border-gray-200 dark:border-slate-600 rounded-xl hover:bg-gray-100 dark:hover:bg-slate-900 transition-all"
            >
              <div className="flex items-center gap-3">
                <div className={`p-2 rounded-lg ${formData.billable ? 'bg-emerald-50 dark:bg-emerald-900/30' : 'bg-gray-100 dark:bg-slate-700'}`}>
                  <DollarSign className={`w-4 h-4 ${formData.billable ? 'text-emerald-600 dark:text-emerald-400' : 'text-gray-400 dark:text-slate-500'}`} />
                </div>
                <div className="text-left">
                  <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{t('serviceOps.timeTracking.list.billable')}</div>
                  <div className="text-xs text-gray-400 dark:text-slate-500">
                    {formData.billable ? t('serviceOps.timeTracking.form.billableHint') : t('serviceOps.timeTracking.form.nonBillableHint')}
                  </div>
                </div>
              </div>
              {/* Toggle Switch */}
              <div className={`relative w-11 h-6 rounded-full transition-colors duration-200 flex-shrink-0 ${formData.billable ? 'bg-emerald-500' : 'bg-gray-300 dark:bg-slate-600'}`}>
                <div className={`absolute top-0.5 w-5 h-5 bg-white rounded-full shadow-sm transition-transform duration-200 ${formData.billable ? 'translate-x-5' : 'translate-x-0.5'}`} />
              </div>
            </button>
          </div>
        </div>
      </Modal>

      {/* ====== 删除确认 ====== */}
      <ConfirmDialog
        open={!!deleteId}
        title={t('serviceOps.common.confirmDelete')}
        message={t('serviceOps.timeTracking.form.deleteConfirmMessage')}
        onCancel={() => setDeleteId(null)}
        onConfirm={executeDelete}
        variant="danger"
        loading={deleteLoading}
      />

      {/* ====== Toast ====== */}
      {toast && (
        <div className="fixed top-4 right-4 z-50 animate-in slide-in-from-top duration-300">
          <div
            className={`px-4 py-3 rounded-xl shadow-lg text-sm flex items-center gap-2 ${
              toast.type === 'success'
                ? 'bg-emerald-500 text-white'
                : 'bg-red-500 text-white'
            }`}
          >
            {toast.type === 'success' ? (
              <CheckCircle2 className="w-4 h-4" />
            ) : (
              <XCircle className="w-4 h-4" />
            )}
            {toast.text}
          </div>
        </div>
      )}
    </div>
  );
}
