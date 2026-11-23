import { useCallback, useState, useEffect, useRef } from 'react';
import { slaApi, mailboxApi } from '../api';
import { useTimedToast } from '../hooks/useTimedToast';
import { useI18n } from '../context/I18nContext';
import { tBound } from '../i18n/locale';
import type { TranslationKey } from '../locales/zh-Hans';
import type { TranslateParams } from '../i18n/types';
import type {
  SlaPolicyDto,
  SlaViolationDto,
  SlaOverviewResponse,
  SlaPolicyQuery,
  SlaViolationQuery,
  CreateSlaPolicyRequest,
  MailboxResponse,
} from '../api/types';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import { formatDateTime } from '../utils/format';
import {
  Edit2,
  Trash2,
  Search,
  Plus,
  CheckCircle2,
  XCircle,
  Clock,
  Layers,
  AlertTriangle,
  Globe,
  Bell,
  RefreshCw,
  ChevronLeft,
  ChevronRight,
  RotateCcw,
} from 'lucide-react';

type TabKey = 'policies' | 'violations';

/** 与 `I18nContext` 的 `t` 签名一致；模块级纯函数需显式传入 `t` */
type TranslateFn = (key: TranslationKey, params?: TranslateParams) => string;

/** 违规类型筛选项：`value` 是后端稳定 code（`SlaStatuses.ViolationFirstResponse/Resolution`） */
const VIOLATION_TYPE_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: '', labelKey: 'automation.sla.violations.allTypes' },
  { value: 'first_response', labelKey: 'automation.sla.violationType.firstResponse' },
  { value: 'resolution', labelKey: 'automation.sla.violationType.resolution' },
];

/** 违规状态筛选项：`value` 是后端稳定 code（`SlaStatuses.ViolationWarning/Breached/Resolved`） */
const STATUS_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: '', labelKey: 'automation.common.allStatuses' },
  { value: 'warning', labelKey: 'automation.sla.status.warning' },
  { value: 'breached', labelKey: 'automation.sla.status.breached' },
  { value: 'resolved', labelKey: 'automation.sla.status.resolved' },
];

const STATUS_BADGE: Record<string, string> = {
  warning: 'bg-amber-100 text-amber-700',
  breached: 'bg-red-100 text-red-700',
  resolved: 'bg-emerald-100 text-emerald-700',
};

const VIOLATION_TYPE_BADGE: Record<string, string> = {
  first_response: 'bg-sky-100 text-sky-700',
  resolution: 'bg-violet-100 text-violet-700',
};

/**
 * 违规类型 code → 文案 key（后端 `SlaConst.GetViolationTypeLabel` 恰 2 码，无缺无余）。
 * 未知 code 回落原始 code，与原行为一致。
 */
const VIOLATION_TYPE_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  first_response: 'automation.sla.violationType.firstResponse',
  resolution: 'automation.sla.violationType.resolution',
};

/** 违规状态 code → 文案 key（后端 `SlaConst.GetViolationStatusLabel` 恰 3 码，无缺无余） */
const VIOLATION_STATUS_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  warning: 'automation.sla.status.warning',
  breached: 'automation.sla.status.breached',
  resolved: 'automation.sla.status.resolved',
};

const violationTypeLabel = (t: TranslateFn, code: string) => {
  const key = VIOLATION_TYPE_LABEL_KEYS[code];
  return key ? t(key) : code;
};

const violationStatusLabel = (t: TranslateFn, code: string) => {
  const key = VIOLATION_STATUS_LABEL_KEYS[code];
  return key ? t(key) : code;
};

/**
 * 小时数 → 友好文案。前端版，替代后端 `SlaConst.FormatHours`
 * （后端下发的中文 `firstResponseLabel`/`resolutionLabel` 不再渲染）。
 * 分支与后端逐条对齐：≤0 → 0 分钟；<1 → 分钟；<24 → 小时（+分钟）；≥24 → 天（+小时）。
 */
function formatDurationHours(t: TranslateFn, hours: number): string {
  if (hours <= 0) return t('automation.sla.duration.minutes', { minutes: 0 });
  if (hours < 1) {
    const minutes = Math.round(hours * 60);
    return t('automation.sla.duration.minutes', { minutes });
  }
  if (hours < 24) {
    const wholeHours = Math.trunc(hours);
    const remainingMinutes = Math.round((hours - wholeHours) * 60);
    return remainingMinutes > 0
      ? `${t('automation.sla.duration.hours', { hours: wholeHours })} ${t('automation.sla.duration.minutes', { minutes: remainingMinutes })}`
      : t('automation.sla.duration.hours', { hours: wholeHours });
  }
  const days = Math.trunc(hours / 24);
  const remainingHours = Math.trunc(hours % 24);
  return remainingHours > 0
    ? `${t('automation.sla.duration.days', { days })} ${t('automation.sla.duration.hours', { hours: remainingHours })}`
    : t('automation.sla.duration.days', { days });
}

function formatRemaining(t: TranslateFn, minutes: number): { text: string; color: string } {
  const sign = minutes >= 0 ? '' : `${t('automation.sla.remaining.overdue')} `;
  const abs = Math.abs(minutes);
  const color = minutes >= 0
    ? (abs < 60 ? 'text-amber-600' : 'text-emerald-600')
    : 'text-red-600';

  if (abs < 60) return { text: `${sign}${t('automation.sla.duration.minutes', { minutes: Math.round(abs) })}`, color };

  // 计算天数、小时、分钟，避免 60 分钟进位问题
  const totalMinutes = Math.floor(abs);
  const d = Math.floor(totalMinutes / 1440);
  const h = Math.floor((totalMinutes % 1440) / 60);
  const m = totalMinutes % 60;

  if (d > 0) {
    return { text: `${sign}${t('automation.sla.duration.days', { days: d })}${h > 0 ? ` ${t('automation.sla.duration.hours', { hours: h })}` : ''}`, color };
  }
  if (h > 0) {
    return { text: `${sign}${t('automation.sla.duration.hours', { hours: h })}${m > 0 ? ` ${t('automation.sla.duration.minutes', { minutes: m })}` : ''}`, color };
  }
  return { text: `${sign}${t('automation.sla.duration.minutes', { minutes: m })}`, color };
}

export default function Sla() {
  const { t } = useI18n();
  const [tab, setTab] = useState<TabKey>('policies');
  const [overview, setOverview] = useState<SlaOverviewResponse | null>(null);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);

  // 策略列表
  const [policyQuery, setPolicyQuery] = useState<SlaPolicyQuery>({ page: 1, pageSize: 20 });
  const [policyList, setPolicyList] = useState<{ data: SlaPolicyDto[]; totalItems: number; totalPages: number; currentPage: number } | null>(null);
  const [policyLoading, setPolicyLoading] = useState(false);

  // 违规列表
  const [violationQuery, setViolationQuery] = useState<SlaViolationQuery>({ page: 1, pageSize: 20 });
  const [violationList, setViolationList] = useState<{ data: SlaViolationDto[]; totalItems: number; totalPages: number; currentPage: number } | null>(null);
  const [violationLoading, setViolationLoading] = useState(false);

  // 编辑器
  const [editing, setEditing] = useState<SlaPolicyDto | null>(null);
  const [showEditor, setShowEditor] = useState(false);
  const [form, setForm] = useState<CreateSlaPolicyRequest>(emptyForm());
  const [saving, setSaving] = useState(false);

  // 删除
  const [deleteTarget, setDeleteTarget] = useState<SlaPolicyDto | null>(null);
  const [deleting, setDeleting] = useState(false);

  // 垃圾箱
  const [trashOpen, setTrashOpen] = useState(false);
  const [trashQuery, setTrashQuery] = useState<{ page: number; pageSize: number; keyword: string }>({ page: 1, pageSize: 10, keyword: '' });
  const [trashList, setTrashList] = useState<{ data: SlaPolicyDto[]; totalItems: number; totalPages: number; currentPage: number } | null>(null);
  const [trashLoading, setTrashLoading] = useState(false);
  const [trashCount, setTrashCount] = useState(0);
  const [restoreTarget, setRestoreTarget] = useState<SlaPolicyDto | null>(null);
  const [hardDeleteTarget, setHardDeleteTarget] = useState<SlaPolicyDto | null>(null);
  const [clearTrashOpen, setClearTrashOpen] = useState(false);
  const [trashActionLoading, setTrashActionLoading] = useState(false);

  // 扫描
  const [scanning, setScanning] = useState(false);
  const [scanResult, setScanResult] = useState<{ scanned: number; newViolations: number; breached: number } | null>(null);

  const { message: toast, show: showToast } = useTimedToast();

  // 策略关键词搜索：缓冲值即时回显 + 650ms 防抖自动搜索 + 回车即时触发（与 ApiKeys 一致）
  const [policyKeywordInput, setPolicyKeywordInput] = useState('');
  const policyKeywordTimerRef = useRef<number | null>(null);
  const applyPolicyKeyword = useCallback((value: string) => {
    setPolicyQuery((q) => ({ ...q, keyword: value, page: 1 }));
  }, []);

  const handlePolicyKeywordInput = useCallback((value: string) => {
    setPolicyKeywordInput(value);
    // 输入停顿 650ms 后自动搜索（期间继续输入会重置计时器）
    if (policyKeywordTimerRef.current !== null) window.clearTimeout(policyKeywordTimerRef.current);
    policyKeywordTimerRef.current = window.setTimeout(() => applyPolicyKeyword(value), 650);
  }, [applyPolicyKeyword]);

  const triggerPolicyKeywordNow = useCallback(() => {
    if (policyKeywordTimerRef.current !== null) {
      window.clearTimeout(policyKeywordTimerRef.current);
      policyKeywordTimerRef.current = null;
    }
    applyPolicyKeyword(policyKeywordInput);
  }, [applyPolicyKeyword, policyKeywordInput]);

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

  // 打开垃圾箱时重置关键词缓冲（query 会被重置为空，需同步避免回显残留）
  const resetTrashKeywordSearch = useCallback(() => {
    if (trashKeywordTimerRef.current !== null) {
      window.clearTimeout(trashKeywordTimerRef.current);
      trashKeywordTimerRef.current = null;
    }
    setTrashKeywordInput('');
  }, []);

  useEffect(() => {
    return () => {
      if (policyKeywordTimerRef.current !== null) window.clearTimeout(policyKeywordTimerRef.current);
      if (trashKeywordTimerRef.current !== null) window.clearTimeout(trashKeywordTimerRef.current);
    };
  }, []);

  function emptyForm(): CreateSlaPolicyRequest {
    return {
      name: '',
      description: '',
      mailboxId: null,
      firstResponseHours: 24,
      resolutionHours: 72,
      businessHoursOnly: false,
      priority: 0,
      isActive: true,
      sortOrder: 0,
    };
  }

  useEffect(() => {
    let destroyed = false;
    slaApi
      .overview()
      .then((r) => { if (!destroyed) setOverview(r); })
      .catch((err: unknown) => console.error('加载SLA概览失败:', err));
    slaApi
      .getTrashCount()
      .then((res) => {
        if (destroyed) return;
        const count = res?.count ?? res ?? 0;
        setTrashCount(typeof count === 'number' ? count : 0);
      })
      .catch(() => {});
    mailboxApi
      .listAll()
      .then((res) => { if (!destroyed) setMailboxes(Array.isArray(res) ? res : []); })
      .catch((err: unknown) => console.error('加载邮箱列表失败:', err));
    return () => { destroyed = true; };
  }, []);

  useEffect(() => {
    let destroyed = false;
    setPolicyLoading(true);
    slaApi
      .list(policyQuery)
      .then((res) => { if (!destroyed) setPolicyList(res); })
      // 用 `tBound`（读取当前绑定语言）而不是把 `t` 放进依赖数组：
      // 覆盖层加载完成会换掉 `t` 的身份，进依赖会让本 effect 重跑并重复请求一次列表。
      .catch(() => { if (!destroyed) showToast({ type: 'error', text: tBound('automation.sla.toast.loadPoliciesFailed') }); })
      .finally(() => { if (!destroyed) setPolicyLoading(false); });
    return () => { destroyed = true; };
  }, [policyQuery, showToast]);

  useEffect(() => {
    let destroyed = false;
    setViolationLoading(true);
    slaApi
      .violations(violationQuery)
      .then((res) => { if (!destroyed) setViolationList(res); })
      .catch(() => { if (!destroyed) showToast({ type: 'error', text: tBound('automation.sla.toast.loadViolationsFailed') }); })
      .finally(() => { if (!destroyed) setViolationLoading(false); });
    return () => { destroyed = true; };
  }, [violationQuery, showToast]);

  const refreshOverview = () => {
    slaApi.overview().then(setOverview).catch((err: unknown) => console.error('加载SLA概览失败:', err));
  };

  const openCreate = () => {
    setEditing(null);
    setForm(emptyForm());
    setShowEditor(true);
  };

  const openEdit = (policy: SlaPolicyDto) => {
    setEditing(policy);
    setForm({
      name: policy.name,
      description: policy.description ?? '',
      mailboxId: policy.mailboxId,
      firstResponseHours: policy.firstResponseHours,
      resolutionHours: policy.resolutionHours,
      businessHoursOnly: policy.businessHoursOnly,
      priority: policy.priority,
      isActive: policy.isActive,
      sortOrder: policy.sortOrder,
    });
    setShowEditor(true);
  };

  const handleSave = async () => {
    if (!form.name.trim()) {
      showToast({ type: 'error', text: t('automation.sla.toast.nameRequired') });
      return;
    }
    if (form.firstResponseHours <= 0 || form.resolutionHours <= 0) {
      showToast({ type: 'error', text: t('automation.sla.toast.durationInvalid') });
      return;
    }
    setSaving(true);
    try {
      if (editing) {
        await slaApi.update(editing.id, { ...form, id: editing.id });
        showToast({ type: 'success', text: t('automation.sla.toast.updated') });
      } else {
        await slaApi.create(form);
        showToast({ type: 'success', text: t('automation.sla.toast.created') });
      }
      setShowEditor(false);
      setPolicyQuery((q) => ({ ...q }));
      refreshOverview();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      showToast({ type: 'error', text: message ?? t('automation.common.saveFailed') });
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await slaApi.remove(deleteTarget.id);
      showToast({ type: 'success', text: t('automation.sla.toast.movedToTrash') });
      setDeleteTarget(null);
      setPolicyQuery((q) => ({ ...q }));
      refreshOverview();
      refreshTrashCount();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      showToast({ type: 'error', text: message ?? t('automation.common.deleteFailed') });
    } finally {
      setDeleting(false);
    }
  };

  // ====== 垃圾箱 ======
  const refreshTrashCount = () => {
    slaApi.getTrashCount().then((res) => {
      const count = res?.count ?? res ?? 0;
      setTrashCount(typeof count === 'number' ? count : 0);
    }).catch(() => {});
  };

  const refreshTrashList = useCallback(() => {
    setTrashLoading(true);
    slaApi.getTrash(trashQuery)
      .then((res) => setTrashList(res))
      // `tBound` 而非依赖 `t`：本回调是下面 effect 的依赖，换 `t` 身份会让垃圾箱列表多请求一次。
      .catch(() => showToast({ type: 'error', text: tBound('automation.common.loadTrashFailed') }))
      .finally(() => setTrashLoading(false));
  }, [trashQuery, showToast]);

  const openTrash = () => {
    setTrashOpen(true);
    resetTrashKeywordSearch();
    setTrashQuery({ page: 1, pageSize: 10, keyword: '' });
    refreshTrashCount();
  };

  useEffect(() => {
    refreshTrashCount();
  }, []);

  useEffect(() => {
    if (trashOpen) refreshTrashList();
  }, [trashOpen, trashQuery, refreshTrashList]);

  const handleRestore = async () => {
    if (!restoreTarget) return;
    setTrashActionLoading(true);
    try {
      await slaApi.restore(restoreTarget.id);
      showToast({ type: 'success', text: t('automation.sla.toast.restored', { name: restoreTarget.name }) });
      setRestoreTarget(null);
      setPolicyQuery((q) => ({ ...q }));
      refreshTrashList();
      refreshTrashCount();
      refreshOverview();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      showToast({ type: 'error', text: message ?? t('automation.common.restoreFailed') });
    } finally {
      setTrashActionLoading(false);
    }
  };

  const handleHardDelete = async () => {
    if (!hardDeleteTarget) return;
    setTrashActionLoading(true);
    try {
      await slaApi.hardDelete(hardDeleteTarget.id);
      showToast({ type: 'success', text: t('automation.sla.toast.hardDeleted', { name: hardDeleteTarget.name }) });
      setHardDeleteTarget(null);
      refreshTrashList();
      refreshTrashCount();
      refreshOverview();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      showToast({ type: 'error', text: message ?? t('automation.common.deleteFailed') });
    } finally {
      setTrashActionLoading(false);
    }
  };

  const handleClearTrash = async () => {
    setTrashActionLoading(true);
    try {
      const res = await slaApi.clearTrash() as { count?: number } | null;
      const count = res?.count ?? 0;
      showToast({ type: 'success', text: t('automation.common.trashCleared', { count }) });
      setClearTrashOpen(false);
      refreshTrashList();
      refreshTrashCount();
      refreshOverview();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      showToast({ type: 'error', text: message ?? t('automation.common.clearTrashFailed') });
    } finally {
      setTrashActionLoading(false);
    }
  };

  const handleToggle = async (policy: SlaPolicyDto) => {
    try {
      await slaApi.toggleActive(policy.id);
      setPolicyQuery((q) => ({ ...q }));
      refreshOverview();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      showToast({ type: 'error', text: message ?? t('automation.sla.toast.toggleFailed') });
    }
  };

  const handleScan = async () => {
    setScanning(true);
    try {
      const result = await slaApi.scan();
      setScanResult(result);
      showToast({ type: 'success', text: t('automation.sla.toast.scanDone', { scanned: result.scanned, newViolations: result.newViolations }) });
      setViolationQuery((q) => ({ ...q }));
      refreshOverview();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      showToast({ type: 'error', text: message ?? t('automation.sla.toast.scanFailed') });
    } finally {
      setScanning(false);
    }
  };

  const policyStats = overview?.policyStats ?? {};
  const violationStats = overview?.violationStats ?? {};

  return (
    <div className="space-y-6">
      {/* 头部 */}
      <div className="flex items-start justify-between gap-3 flex-wrap">
        <div className="min-w-0">
          <h1 className="text-xl md:text-2xl font-bold text-gray-900 dark:text-slate-100">{t('automation.sla.page.title')}</h1>
          <p className="text-sm text-gray-500 dark:text-slate-400 mt-1">
            {t('automation.sla.page.subtitle')}
          </p>
        </div>
        <div className="flex items-center gap-2 flex-wrap">
          <button
            onClick={openTrash}
            className="relative px-3 py-2 text-sm font-medium text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition flex items-center gap-1.5"
            title={t('automation.common.trash')}
          >
            <Trash2 className="w-4 h-4" />
            <span className="hidden sm:inline">{t('automation.common.trash')}</span>
            {trashCount > 0 && (
              <span className="absolute -top-1.5 -right-1.5 min-w-[20px] h-5 px-1.5 flex items-center justify-center text-xs font-bold text-white bg-red-500 rounded-full">
                {trashCount > 99 ? '99+' : trashCount}
              </span>
            )}
          </button>
          <button
            onClick={handleScan}
            disabled={scanning}
            className="px-3 py-2 text-sm font-medium text-white bg-amber-600 rounded-lg hover:bg-amber-700 transition disabled:opacity-50 flex items-center gap-1.5"
          >
            <RefreshCw className={`w-4 h-4 ${scanning ? 'animate-spin' : ''}`} />
            <span className="hidden sm:inline">{scanning ? t('automation.sla.page.scanning') : t('automation.sla.page.scan')}</span>
            <span className="sm:hidden">{t('automation.sla.page.scanShort')}</span>
          </button>
          <button
            onClick={openCreate}
            className="px-3 py-2 text-sm font-medium text-white bg-primary-600 rounded-lg hover:bg-primary-700 transition flex items-center gap-1.5"
          >
            <Plus className="w-4 h-4" />
            <span className="hidden sm:inline">{t('automation.sla.page.new')}</span>
            <span className="sm:hidden">{t('automation.common.newShort')}</span>
          </button>
        </div>
      </div>

      {/* 统计卡片 */}
      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-4">
        <StatCard labelKey="automation.sla.stats.total" value={policyStats.total ?? 0} icon={<Layers className="w-5 h-5" />} bg="bg-gray-50 dark:bg-slate-800/50" textColor="text-gray-700 dark:text-slate-200" />
        <StatCard labelKey="automation.common.statsActive" value={policyStats.active ?? 0} icon={<CheckCircle2 className="w-5 h-5" />} bg="bg-green-50" textColor="text-green-600" />
        <StatCard labelKey="automation.sla.stats.global" value={policyStats.global ?? 0} icon={<Globe className="w-5 h-5" />} bg="bg-blue-50" textColor="text-blue-600" />
        <StatCard labelKey="automation.sla.stats.violations" value={policyStats.violations ?? 0} icon={<AlertTriangle className="w-5 h-5" />} bg="bg-amber-50" textColor="text-amber-600" />
        <StatCard labelKey="automation.sla.status.breached" value={policyStats.breached ?? 0} icon={<XCircle className="w-5 h-5" />} bg="bg-red-50" textColor="text-red-600" />
        <StatCard labelKey="automation.sla.status.warning" value={violationStats.warning ?? 0} icon={<Bell className="w-5 h-5" />} bg="bg-orange-50" textColor="text-orange-600" />
      </div>

      {/* 扫描结果提示 */}
      {scanResult && (
        <div className="bg-sky-50 dark:bg-sky-900/20 border border-sky-200 dark:border-sky-800 rounded-lg p-4 flex items-start gap-3">
          <svg className="w-5 h-5 text-sky-600 flex-shrink-0 mt-0.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          <div className="flex-1 text-sm">
            <div className="font-medium text-sky-900">{t('automation.sla.page.scanResultTitle')}</div>
            <div className="text-sky-700 mt-1">
              {t('automation.sla.page.scanResultLine', { scanned: scanResult.scanned, newViolations: scanResult.newViolations, breached: scanResult.breached })}
            </div>
          </div>
          <button onClick={() => setScanResult(null)} className="text-sky-400 hover:text-sky-600">
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
      )}

      {/* 标签页 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 overflow-hidden">
        <div className="flex border-b border-gray-200 dark:border-slate-700">
          <button
            onClick={() => setTab('policies')}
            className={`px-5 py-3 text-sm font-medium transition ${
              tab === 'policies'
                ? 'text-primary-700 border-b-2 border-primary-600 bg-primary-50/50'
                : 'text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:text-slate-300'
            }`}
          >
            {t('automation.sla.page.tabPolicies', { count: policyStats.total ?? 0 })}
          </button>
          <button
            onClick={() => setTab('violations')}
            className={`px-5 py-3 text-sm font-medium transition ${
              tab === 'violations'
                ? 'text-primary-700 border-b-2 border-primary-600 bg-primary-50/50'
                : 'text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:text-slate-300'
            }`}
          >
            {t('automation.sla.page.tabViolations', { count: violationStats.total ?? 0 })}
          </button>
        </div>

        {/* 策略列表 */}
        {tab === 'policies' && (
          <div>
            {/* 筛选器 */}
            <div className="px-4 md:px-5 py-3 bg-gray-50 dark:bg-slate-900/50 border-b border-gray-100 dark:border-slate-700/50 flex flex-wrap items-center gap-2 md:gap-3">
              <input
                type="text"
                placeholder={t('automation.sla.list.searchPlaceholder')}
                value={policyKeywordInput}
                onChange={(e) => handlePolicyKeywordInput(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') triggerPolicyKeywordNow(); }}
                className="flex-1 min-w-[140px] px-3 py-1.5 text-sm border border-gray-300 dark:border-slate-600 rounded-md focus:outline-none focus:ring-1 focus:ring-primary-500"
              />
              <select
                value={policyQuery.mailboxId ?? ''}
                onChange={(e) => setPolicyQuery((q) => ({ ...q, mailboxId: e.target.value ? Number(e.target.value) : null, page: 1 }))}
                className="flex-1 min-w-[120px] px-3 py-1.5 text-sm border border-gray-300 dark:border-slate-600 rounded-md focus:outline-none focus:ring-1 focus:ring-primary-500"
              >
                <option value="">{t('automation.common.allMailboxes')}</option>
                {mailboxes.map((m) => (
                  <option key={m.id} value={m.id}>{m.name}</option>
                ))}
              </select>
              <select
                value={policyQuery.isActive === undefined ? '' : policyQuery.isActive ? '1' : '0'}
                onChange={(e) => setPolicyQuery((q) => ({ ...q, isActive: e.target.value === '' ? undefined : e.target.value === '1', page: 1 }))}
                className="flex-1 min-w-[100px] px-3 py-1.5 text-sm border border-gray-300 dark:border-slate-600 rounded-md focus:outline-none focus:ring-1 focus:ring-primary-500"
              >
                <option value="">{t('automation.common.allStatuses')}</option>
                <option value="1">{t('automation.common.enabled')}</option>
                <option value="0">{t('automation.common.disabled')}</option>
              </select>
            </div>

            {/* PC 端表格视图 */}
            <div className="hidden md:block overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-gray-50 dark:bg-slate-900 text-gray-600 text-xs uppercase">
                  <tr>
                    <th className="px-4 py-3 text-left font-medium">{t('automation.sla.list.columns.name')}</th>
                    <th className="px-4 py-3 text-left font-medium">{t('automation.common.mailboxColumn')}</th>
                    <th className="px-4 py-3 text-left font-medium">{t('automation.sla.list.columns.firstResponse')}</th>
                    <th className="px-4 py-3 text-left font-medium">{t('automation.sla.list.columns.resolution')}</th>
                    <th className="px-4 py-3 text-left font-medium">{t('automation.common.priority')}</th>
                    <th className="px-4 py-3 text-left font-medium">{t('automation.sla.list.columns.appliedViolations')}</th>
                    <th className="px-4 py-3 text-left font-medium">{t('automation.common.status')}</th>
                    <th className="px-4 py-3 text-right font-medium">{t('automation.common.actions')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {policyLoading ? (
                    <tr><td colSpan={8} className="px-4 py-10 text-center text-gray-400">{t('common.shell.loading')}</td></tr>
                  ) : !policyList?.data?.length ? (
                    <tr><td colSpan={8} className="px-4 py-10 text-center text-gray-400">{t('automation.sla.list.empty')}</td></tr>
                  ) : (
                    policyList.data.map((p) => (
                      <tr key={p.id} className="hover:bg-gray-50 dark:hover:bg-slate-700 dark:bg-slate-900/50">
                        <td className="px-4 py-3">
                          <div className="font-medium text-gray-900 dark:text-slate-100">{p.name}</div>
                          {p.description && <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-1">{p.description}</div>}
                        </td>
                        <td className="px-4 py-3">
                          {p.mailboxId === null || p.mailboxId === undefined ? (
                            <span className="text-xs px-2 py-0.5 rounded-full bg-sky-100 text-sky-700">{t('automation.common.allMailboxes')}</span>
                          ) : (
                            <span className="text-xs px-2 py-0.5 rounded-full bg-gray-100 dark:bg-slate-700/50 text-gray-700 dark:text-slate-300">{p.mailboxName ?? t('automation.common.mailboxNumber', { id: p.mailboxId })}</span>
                          )}
                        </td>
                        <td className="px-4 py-3 text-gray-700 dark:text-slate-300">{formatDurationHours(t, p.firstResponseHours)}</td>
                        <td className="px-4 py-3 text-gray-700 dark:text-slate-300">{formatDurationHours(t, p.resolutionHours)}</td>
                        <td className="px-4 py-3">
                          <span className={`text-xs px-2 py-0.5 rounded-full ${p.priority >= 10 ? 'bg-red-100 text-red-700' : p.priority > 0 ? 'bg-amber-100 text-amber-700' : 'bg-gray-100 dark:bg-slate-700/50 text-gray-600'}`}>
                            {p.priority}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-xs text-gray-600">
                          <div>{t('automation.sla.list.appliedCount', { count: p.appliedCount })}</div>
                          <div className={p.violationCount > 0 ? 'text-red-600' : ''}>{t('automation.sla.list.violationCount', { count: p.violationCount })}</div>
                        </td>
                        <td className="px-4 py-3">
                          <button
                            onClick={() => handleToggle(p)}
                            className={`relative inline-flex h-5 w-9 items-center rounded-full transition ${p.isActive ? 'bg-emerald-500' : 'bg-gray-300'}`}
                          >
                            <span className={`inline-block h-3.5 w-3.5 transform rounded-full bg-white dark:bg-slate-800 transition ${p.isActive ? 'translate-x-5' : 'translate-x-1'}`} />
                          </button>
                        </td>
                        <td className="px-4 py-3 text-right">
                          <div className="flex items-center justify-end gap-1">
                            <button onClick={() => openEdit(p)} className="p-2 text-blue-600 hover:bg-blue-50 rounded-lg transition-colors" title={t('common.edit')}>
                              <Edit2 className="w-4 h-4" />
                            </button>
                            <button onClick={() => setDeleteTarget(p)} className="p-2 text-red-600 hover:bg-red-50 rounded-lg transition-colors" title={t('common.delete')}>
                              <Trash2 className="w-4 h-4" />
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            {/* 移动端卡片视图 */}
            <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
              {policyLoading ? (
                <div className="p-8 text-center text-gray-400 text-sm">{t('common.shell.loading')}</div>
              ) : !policyList?.data?.length ? (
                <div className="p-8 text-center text-gray-400 text-sm">{t('automation.sla.list.empty')}</div>
              ) : (
                policyList.data.map((p) => (
                  <div key={p.id} className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700 space-y-3">
                    <div className="flex items-start justify-between gap-2">
                      <div className="flex-1 min-w-0">
                        <div className="text-sm font-semibold text-gray-900 dark:text-slate-100 truncate">{p.name}</div>
                        {p.description && <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-2">{p.description}</div>}
                      </div>
                      <button
                        onClick={() => handleToggle(p)}
                        className={`relative inline-flex h-5 w-9 flex-shrink-0 items-center rounded-full transition ${p.isActive ? 'bg-emerald-500' : 'bg-gray-300'}`}
                        title={p.isActive ? t('automation.common.clickToDisable') : t('automation.common.clickToEnable')}
                      >
                        <span className={`inline-block h-3.5 w-3.5 transform rounded-full bg-white dark:bg-slate-800 transition ${p.isActive ? 'translate-x-5' : 'translate-x-1'}`} />
                      </button>
                    </div>

                    <div className="flex flex-wrap items-center gap-1.5">
                      {p.mailboxId === null || p.mailboxId === undefined ? (
                        <span className="text-xs px-2 py-0.5 rounded-full bg-sky-100 text-sky-700">{t('automation.common.allMailboxes')}</span>
                      ) : (
                        <span className="text-xs px-2 py-0.5 rounded-full bg-gray-100 dark:bg-slate-700/50 text-gray-700 dark:text-slate-300">{p.mailboxName ?? t('automation.common.mailboxNumber', { id: p.mailboxId })}</span>
                      )}
                      <span className={`text-xs px-2 py-0.5 rounded-full ${p.priority >= 10 ? 'bg-red-100 text-red-700' : p.priority > 0 ? 'bg-amber-100 text-amber-700' : 'bg-gray-100 dark:bg-slate-700/50 text-gray-600'}`}>
                        {t('automation.sla.list.priorityBadge', { priority: p.priority })}
                      </span>
                    </div>

                    <div className="grid grid-cols-2 gap-2 text-xs">
                      <div className="bg-gray-50 dark:bg-slate-900/50 rounded-lg p-2">
                        <div className="text-gray-500 dark:text-slate-400">{t('automation.sla.violationType.firstResponse')}</div>
                        <div className="text-gray-900 dark:text-slate-100 font-medium mt-0.5">{formatDurationHours(t, p.firstResponseHours)}</div>
                      </div>
                      <div className="bg-gray-50 dark:bg-slate-900/50 rounded-lg p-2">
                        <div className="text-gray-500 dark:text-slate-400">{t('automation.sla.violationType.resolution')}</div>
                        <div className="text-gray-900 dark:text-slate-100 font-medium mt-0.5">{formatDurationHours(t, p.resolutionHours)}</div>
                      </div>
                    </div>

                    <div className="flex items-center justify-between pt-1">
                      <div className="flex items-center gap-3 text-xs text-gray-600 dark:text-slate-300">
                        <span>{t('automation.sla.list.appliedPrefix')}<span className="font-semibold text-gray-900 dark:text-slate-100">{p.appliedCount}</span></span>
                        <span className={p.violationCount > 0 ? 'text-red-600' : ''}>{t('automation.sla.list.violationPrefix')}<span className="font-semibold">{p.violationCount}</span></span>
                      </div>
                      <div className="flex items-center gap-1">
                        <button onClick={() => openEdit(p)} className="p-2 text-blue-600 hover:bg-blue-50 dark:hover:bg-blue-900/30 rounded-lg transition-colors" title={t('common.edit')}>
                          <Edit2 className="w-4 h-4" />
                        </button>
                        <button onClick={() => setDeleteTarget(p)} className="p-2 text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-lg transition-colors" title={t('common.delete')}>
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                    </div>
                  </div>
                ))
              )}
            </div>

            {/* 分页 */}
            {policyList && policyList.totalPages > 1 && (
              <Pagination current={policyList.currentPage} total={policyList.totalPages} onChange={(p) => setPolicyQuery((q) => ({ ...q, page: p }))} />
            )}
          </div>
        )}

        {/* 违规记录 */}
        {tab === 'violations' && (
          <div>
            {/* 筛选器 */}
            <div className="px-4 md:px-5 py-3 bg-gray-50 dark:bg-slate-900/50 border-b border-gray-100 dark:border-slate-700/50 flex flex-wrap items-center gap-2 md:gap-3">
              <select
                value={violationQuery.violationType ?? ''}
                onChange={(e) => setViolationQuery((q) => ({ ...q, violationType: e.target.value || null, page: 1 }))}
                className="flex-1 min-w-[100px] px-3 py-1.5 text-sm border border-gray-300 dark:border-slate-600 rounded-md focus:outline-none focus:ring-1 focus:ring-primary-500"
              >
                {VIOLATION_TYPE_OPTIONS.map((o) => <option key={o.value} value={o.value}>{t(o.labelKey)}</option>)}
              </select>
              <select
                value={violationQuery.status ?? ''}
                onChange={(e) => setViolationQuery((q) => ({ ...q, status: e.target.value || null, page: 1 }))}
                className="flex-1 min-w-[100px] px-3 py-1.5 text-sm border border-gray-300 dark:border-slate-600 rounded-md focus:outline-none focus:ring-1 focus:ring-primary-500"
              >
                {STATUS_OPTIONS.map((o) => <option key={o.value} value={o.value}>{t(o.labelKey)}</option>)}
              </select>
              <select
                value={violationQuery.policyId ?? ''}
                onChange={(e) => setViolationQuery((q) => ({ ...q, policyId: e.target.value ? Number(e.target.value) : null, page: 1 }))}
                className="flex-1 min-w-[120px] px-3 py-1.5 text-sm border border-gray-300 dark:border-slate-600 rounded-md focus:outline-none focus:ring-1 focus:ring-primary-500"
              >
                <option value="">{t('automation.sla.violations.allPolicies')}</option>
                {policyList?.data?.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
            </div>

            {/* 表格 */}
            <div className="hidden md:block overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-gray-50 dark:bg-slate-900 text-gray-600 text-xs uppercase">
                  <tr>
                    <th className="px-4 py-3 text-left font-medium">{t('automation.sla.violations.columns.type')}</th>
                    <th className="px-4 py-3 text-left font-medium">{t('automation.common.conversation')}</th>
                    <th className="px-4 py-3 text-left font-medium">{t('automation.sla.violations.columns.policy')}</th>
                    <th className="px-4 py-3 text-left font-medium">{t('automation.sla.violations.columns.dueAt')}</th>
                    <th className="px-4 py-3 text-left font-medium">{t('automation.sla.violations.columns.remaining')}</th>
                    <th className="px-4 py-3 text-left font-medium">{t('automation.common.status')}</th>
                    <th className="px-4 py-3 text-left font-medium">{t('automation.sla.violations.columns.detectedAt')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {violationLoading ? (
                    <tr><td colSpan={7} className="px-4 py-10 text-center text-gray-400">{t('common.shell.loading')}</td></tr>
                  ) : !violationList?.data?.length ? (
                    <tr><td colSpan={7} className="px-4 py-10 text-center text-gray-400">{t('automation.sla.violations.empty')}</td></tr>
                  ) : (
                    violationList.data.map((v) => {
                      const remaining = formatRemaining(t, v.remainingMinutes);
                      return (
                        <tr key={v.id} className="hover:bg-gray-50 dark:hover:bg-slate-700 dark:bg-slate-900/50">
                          <td className="px-4 py-3">
                            <span className={`text-xs px-2 py-0.5 rounded-full ${VIOLATION_TYPE_BADGE[v.violationType] ?? 'bg-gray-100 dark:bg-slate-700/50 text-gray-700 dark:text-slate-300'}`}>
                              {violationTypeLabel(t, v.violationType)}
                            </span>
                          </td>
                          <td className="px-4 py-3">
                            <a href={`/#/conversations/${v.conversationId}`} className="text-primary-600 hover:underline">
                              #{v.conversationId}
                            </a>
                            {v.conversationSubject && (
                              <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-1">{v.conversationSubject}</div>
                            )}
                          </td>
                          <td className="px-4 py-3 text-gray-700 dark:text-slate-300">{v.slaPolicyName}</td>
                          <td className="px-4 py-3 text-xs text-gray-600">{formatDateTime(v.dueAt)}</td>
                          <td className={`px-4 py-3 text-xs font-medium ${remaining.color}`}>{remaining.text}</td>
                          <td className="px-4 py-3">
                            <span className={`text-xs px-2 py-0.5 rounded-full ${STATUS_BADGE[v.status] ?? 'bg-gray-100 dark:bg-slate-700/50 text-gray-700 dark:text-slate-300'}`}>
                              {violationStatusLabel(t, v.status)}
                            </span>
                          </td>
                          <td className="px-4 py-3 text-xs text-gray-600">{formatDateTime(v.detectedAt)}</td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>

            {/* 移动端卡片视图 */}
            <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
              {violationLoading ? (
                <div className="p-8 text-center text-gray-400 text-sm">{t('common.shell.loading')}</div>
              ) : !violationList?.data?.length ? (
                <div className="p-8 text-center text-gray-400 text-sm">{t('automation.sla.violations.empty')}</div>
              ) : (
                violationList.data.map((v) => {
                  const remaining = formatRemaining(t, v.remainingMinutes);
                  return (
                    <div key={v.id} className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700 space-y-2.5">
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex items-center gap-1.5 flex-wrap">
                          <span className={`text-xs px-2 py-0.5 rounded-full ${VIOLATION_TYPE_BADGE[v.violationType] ?? 'bg-gray-100 dark:bg-slate-700/50 text-gray-700 dark:text-slate-300'}`}>
                            {violationTypeLabel(t, v.violationType)}
                          </span>
                          <span className={`text-xs px-2 py-0.5 rounded-full ${STATUS_BADGE[v.status] ?? 'bg-gray-100 dark:bg-slate-700/50 text-gray-700 dark:text-slate-300'}`}>
                            {violationStatusLabel(t, v.status)}
                          </span>
                        </div>
                        <a href={`/#/conversations/${v.conversationId}`} className="text-xs text-primary-600 hover:underline flex-shrink-0">
                          #{v.conversationId}
                        </a>
                      </div>

                      {v.conversationSubject && (
                        <div className="text-xs text-gray-600 dark:text-slate-300 line-clamp-2">{v.conversationSubject}</div>
                      )}

                      <div className="text-xs text-gray-500 dark:text-slate-400">
                        {t('automation.sla.violations.policyPrefix')}<span className="text-gray-700 dark:text-slate-300">{v.slaPolicyName}</span>
                      </div>

                      <div className="grid grid-cols-2 gap-2 text-xs">
                        <div className="bg-gray-50 dark:bg-slate-900/50 rounded-lg p-2">
                          <div className="text-gray-500 dark:text-slate-400">{t('automation.sla.violations.columns.dueAt')}</div>
                          <div className="text-gray-700 dark:text-slate-300 mt-0.5">{formatDateTime(v.dueAt)}</div>
                        </div>
                        <div className="bg-gray-50 dark:bg-slate-900/50 rounded-lg p-2">
                          <div className="text-gray-500 dark:text-slate-400">{t('automation.sla.violations.columns.remaining')}</div>
                          <div className={`font-medium mt-0.5 ${remaining.color}`}>{remaining.text}</div>
                        </div>
                      </div>

                      <div className="text-xs text-gray-500 dark:text-slate-400">
                        {t('automation.sla.violations.detectedLine', { value: formatDateTime(v.detectedAt) })}
                      </div>
                    </div>
                  );
                })
              )}
            </div>

            {violationList && violationList.totalPages > 1 && (
              <Pagination current={violationList.currentPage} total={violationList.totalPages} onChange={(p) => setViolationQuery((q) => ({ ...q, page: p }))} />
            )}
          </div>
        )}
      </div>

      {/* 编辑器 Modal */}
      <Modal
        open={showEditor}
        title={editing ? t('automation.sla.editor.editTitle') : t('automation.sla.editor.createTitle')}
        onClose={() => setShowEditor(false)}
        size="lg"
        footer={
          <>
            <button onClick={() => setShowEditor(false)} className="px-4 py-2 text-sm text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700">
              {t('common.cancel')}
            </button>
            <button
              onClick={handleSave}
              disabled={saving}
              className="px-4 py-2 text-sm text-white bg-primary-600 rounded-lg hover:bg-primary-700 disabled:opacity-50"
            >
              {saving ? t('automation.common.saving') : t('common.save')}
            </button>
          </>
        }
      >
        <div className="space-y-5">
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('automation.sla.list.columns.name')} <span className="text-red-500">*</span></label>
            <input
              type="text"
              value={form.name}
              onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
              placeholder={t('automation.sla.editor.namePlaceholder')}
              className="w-full px-3 py-2.5 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('automation.common.description')}</label>
            <textarea
              value={form.description ?? ''}
              onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))}
              rows={2}
              placeholder={t('automation.sla.editor.descriptionPlaceholder')}
              className="w-full px-3 py-2.5 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 resize-none"
            />
          </div>

          <div className="grid grid-cols-2 gap-5">
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('automation.common.mailboxColumn')}</label>
              <select
                value={form.mailboxId ?? 0}
                onChange={(e) => setForm((f) => ({ ...f, mailboxId: e.target.value === '0' ? null : Number(e.target.value) }))}
                className="w-full px-3 py-2.5 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800"
              >
                <option value={0}>{t('automation.common.allMailboxes')}</option>
                {mailboxes.map((m) => (
                  <option key={m.id} value={m.id}>{m.name}</option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('automation.common.priority')}</label>
              <input
                type="number"
                value={form.priority}
                onChange={(e) => setForm((f) => ({ ...f, priority: Number(e.target.value) }))}
                className="w-full px-3 py-2.5 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800"
              />
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('automation.sla.editor.priorityHint')}</p>
            </div>
          </div>

          <div className="bg-gradient-to-br from-blue-50 to-blue-100 dark:from-blue-900/30 dark:to-blue-800/20 rounded-xl p-4 border border-blue-200 dark:border-blue-700">
            <div className="flex items-center gap-2 mb-4">
              <Clock className="w-4 h-4 text-blue-500" />
              <h4 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('automation.sla.editor.targetsTitle')}</h4>
            </div>
            <div className="grid grid-cols-2 gap-5">
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('automation.sla.editor.firstResponseTarget')}</label>
                <input
                  type="number"
                  step="0.5"
                  min="0.5"
                  value={form.firstResponseHours}
                  onChange={(e) => setForm((f) => ({ ...f, firstResponseHours: Number(e.target.value) }))}
                  className="w-full px-3 py-2.5 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800"
                />
                <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('automation.sla.editor.firstResponseHint')}</p>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('automation.sla.editor.resolutionTarget')}</label>
                <input
                  type="number"
                  step="1"
                  min="1"
                  value={form.resolutionHours}
                  onChange={(e) => setForm((f) => ({ ...f, resolutionHours: Number(e.target.value) }))}
                  className="w-full px-3 py-2.5 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800"
                />
                <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('automation.sla.editor.resolutionHint')}</p>
              </div>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-5">
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('automation.common.sortOrder')}</label>
              <input
                type="number"
                value={form.sortOrder}
                onChange={(e) => setForm((f) => ({ ...f, sortOrder: Number(e.target.value) }))}
                className="w-full px-3 py-2.5 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800"
              />
            </div>
            <div className="flex items-end">
              <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-slate-300">
                <input
                  type="checkbox"
                  checked={form.isActive}
                  onChange={(e) => setForm((f) => ({ ...f, isActive: e.target.checked }))}
                  className="w-4 h-4 text-primary-600 rounded focus:ring-primary-500"
                />
                {t('automation.sla.editor.enablePolicy')}
              </label>
            </div>
          </div>
        </div>
      </Modal>

      {/* 删除确认（软删除到垃圾箱） */}
      <ConfirmDialog
        open={!!deleteTarget}
        title={t('automation.sla.delete.title')}
        message={t('automation.sla.delete.message', { name: deleteTarget?.name ?? '' })}
        confirmText={t('automation.common.moveToTrash')}
        cancelText={t('common.cancel')}
        variant="danger"
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
        loading={deleting}
      />

      {/* 垃圾箱 Modal */}
      <Modal
        open={trashOpen}
        onClose={() => setTrashOpen(false)}
        title={trashCount > 0 ? t('automation.sla.trash.titleWithCount', { count: trashCount }) : t('automation.sla.trash.title')}
        size="xl"
      >
        <div className="space-y-3">
          <div className="flex items-center gap-3 flex-wrap">
            <div className="flex-1 min-w-[220px] relative">
              <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
              <input
                type="text"
                placeholder={t('automation.sla.trash.searchPlaceholder')}
                value={trashKeywordInput}
                onChange={(e) => handleTrashKeywordInput(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') triggerTrashKeywordNow(); }}
                className="w-full pl-9 pr-3 py-2 text-sm bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 text-gray-900 dark:text-slate-100"
              />
            </div>
            <button
              onClick={() => setClearTrashOpen(true)}
              disabled={!trashList?.data?.length}
              className="px-3 py-2 text-sm font-medium text-white bg-red-600 rounded-lg hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-1.5"
            >
              <Trash2 className="w-4 h-4" />
              {t('automation.common.clearTrash')}
            </button>
          </div>

          <div className="hidden md:block overflow-x-auto border border-gray-200 dark:border-slate-700 rounded-lg">
            <table className="w-full text-sm">
              <thead className="bg-gray-50 dark:bg-slate-900 text-gray-600 text-xs uppercase">
                <tr>
                  <th className="px-4 py-3 text-left font-medium">{t('automation.sla.trash.columns.name')}</th>
                  <th className="px-4 py-3 text-left font-medium">{t('automation.common.mailboxColumn')}</th>
                  <th className="px-4 py-3 text-left font-medium">{t('automation.sla.trash.columns.firstResponse')}</th>
                  <th className="px-4 py-3 text-left font-medium">{t('automation.sla.trash.columns.resolution')}</th>
                  <th className="px-4 py-3 text-left font-medium">{t('automation.common.deletedAt')}</th>
                  <th className="px-4 py-3 text-right font-medium">{t('automation.common.actions')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 dark:divide-slate-700/50">
                {trashLoading ? (
                  <tr><td colSpan={6} className="px-4 py-10 text-center text-gray-400">{t('common.shell.loading')}</td></tr>
                ) : !trashList?.data?.length ? (
                  <tr><td colSpan={6} className="px-4 py-10 text-center text-gray-400">{t('automation.common.trashEmpty')}</td></tr>
                ) : (
                  trashList.data.map((p) => (
                    <tr key={p.id} className="hover:bg-gray-50 dark:hover:bg-slate-700">
                      <td className="px-4 py-3">
                        <div className="font-medium text-gray-900 dark:text-slate-100">{p.name}</div>
                        {p.description && <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-1">{p.description}</div>}
                      </td>
                      <td className="px-4 py-3">
                        {p.mailboxId === null || p.mailboxId === undefined ? (
                          <span className="text-xs px-2 py-0.5 rounded-full bg-sky-100 text-sky-700">{t('automation.common.allMailboxes')}</span>
                        ) : (
                          <span className="text-xs px-2 py-0.5 rounded-full bg-gray-100 dark:bg-slate-700/50 text-gray-700 dark:text-slate-300">{p.mailboxName ?? t('automation.common.mailboxNumber', { id: p.mailboxId })}</span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-gray-700 dark:text-slate-300">{formatDurationHours(t, p.firstResponseHours)}</td>
                      <td className="px-4 py-3 text-gray-700 dark:text-slate-300">{formatDurationHours(t, p.resolutionHours)}</td>
                      <td className="px-4 py-3 text-xs text-gray-500 dark:text-slate-400">{p.deletedAt ? formatDateTime(p.deletedAt) : '-'}</td>
                      <td className="px-4 py-3 text-right">
                        <div className="flex items-center justify-end gap-1">
                          <button
                            onClick={() => setRestoreTarget(p)}
                            className="p-2 text-emerald-600 hover:bg-emerald-50 rounded-lg transition-colors"
                            title={t('automation.common.restore')}
                          >
                            <RotateCcw className="w-4 h-4" />
                          </button>
                          <button
                            onClick={() => setHardDeleteTarget(p)}
                            className="p-2 text-red-600 hover:bg-red-50 rounded-lg transition-colors"
                            title={t('automation.common.hardDelete')}
                          >
                            <Trash2 className="w-4 h-4" />
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>

          {/* 移动端卡片视图 */}
          <div className="md:hidden border border-gray-200 dark:border-slate-700 rounded-lg divide-y divide-gray-100 dark:divide-slate-700/50">
            {trashLoading ? (
              <div className="p-8 text-center text-gray-400 text-sm">{t('common.shell.loading')}</div>
            ) : !trashList?.data?.length ? (
              <div className="p-8 text-center text-gray-400 text-sm">{t('automation.common.trashEmpty')}</div>
            ) : (
              trashList.data.map((p) => (
                <div key={p.id} className="p-4 space-y-2.5">
                  <div>
                    <div className="text-sm font-semibold text-gray-900 dark:text-slate-100">{p.name}</div>
                    {p.description && <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-2">{p.description}</div>}
                  </div>
                  <div className="flex flex-wrap items-center gap-1.5">
                    {p.mailboxId === null || p.mailboxId === undefined ? (
                      <span className="text-xs px-2 py-0.5 rounded-full bg-sky-100 text-sky-700">{t('automation.common.allMailboxes')}</span>
                    ) : (
                      <span className="text-xs px-2 py-0.5 rounded-full bg-gray-100 dark:bg-slate-700/50 text-gray-700 dark:text-slate-300">{p.mailboxName ?? t('automation.common.mailboxNumber', { id: p.mailboxId })}</span>
                    )}
                  </div>
                  <div className="grid grid-cols-2 gap-2 text-xs">
                    <div className="bg-gray-50 dark:bg-slate-900/50 rounded-lg p-2">
                      <div className="text-gray-500 dark:text-slate-400">{t('automation.sla.trash.columns.firstResponse')}</div>
                      <div className="text-gray-700 dark:text-slate-300 font-medium mt-0.5">{formatDurationHours(t, p.firstResponseHours)}</div>
                    </div>
                    <div className="bg-gray-50 dark:bg-slate-900/50 rounded-lg p-2">
                      <div className="text-gray-500 dark:text-slate-400">{t('automation.sla.trash.columns.resolution')}</div>
                      <div className="text-gray-700 dark:text-slate-300 font-medium mt-0.5">{formatDurationHours(t, p.resolutionHours)}</div>
                    </div>
                  </div>
                  <div className="flex items-center justify-between pt-1">
                    <div className="text-xs text-gray-500 dark:text-slate-400">
                      {p.deletedAt ? t('automation.sla.trash.deletedAtLine', { value: formatDateTime(p.deletedAt) }) : '—'}
                    </div>
                    <div className="flex items-center gap-1">
                      <button
                        onClick={() => setRestoreTarget(p)}
                        className="p-2 text-emerald-600 hover:bg-emerald-50 dark:hover:bg-emerald-900/30 rounded-lg transition-colors"
                        title={t('automation.common.restore')}
                      >
                        <RotateCcw className="w-4 h-4" />
                      </button>
                      <button
                        onClick={() => setHardDeleteTarget(p)}
                        className="p-2 text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-lg transition-colors"
                        title={t('automation.common.hardDelete')}
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </div>
                  </div>
                </div>
              ))
            )}
          </div>

          {trashList && trashList.totalPages > 1 && (
            <Pagination
              current={trashList.currentPage}
              total={trashList.totalPages}
              onChange={(p) => setTrashQuery((q) => ({ ...q, page: p }))}
            />
          )}
        </div>
      </Modal>

      {/* 恢复确认 */}
      <ConfirmDialog
        open={!!restoreTarget}
        title={t('automation.sla.restore.title')}
        message={t('automation.sla.restore.message', { name: restoreTarget?.name ?? '' })}
        confirmText={t('automation.common.restore')}
        cancelText={t('common.cancel')}
        variant="primary"
        onConfirm={handleRestore}
        onCancel={() => setRestoreTarget(null)}
        loading={trashActionLoading}
      />

      {/* 彻底删除确认 */}
      <ConfirmDialog
        open={!!hardDeleteTarget}
        title={t('automation.sla.hardDelete.title')}
        message={t('automation.sla.hardDelete.message', { name: hardDeleteTarget?.name ?? '' })}
        confirmText={t('automation.common.hardDelete')}
        cancelText={t('common.cancel')}
        variant="danger"
        onConfirm={handleHardDelete}
        onCancel={() => setHardDeleteTarget(null)}
        loading={trashActionLoading}
      />

      {/* 清空垃圾箱确认 */}
      <ConfirmDialog
        open={clearTrashOpen}
        title={t('automation.common.clearTrash')}
        message={t('automation.sla.clearTrash.message', { count: trashCount })}
        confirmText={t('automation.common.hardDeleteAll')}
        cancelText={t('common.cancel')}
        variant="danger"
        onConfirm={handleClearTrash}
        onCancel={() => setClearTrashOpen(false)}
        loading={trashActionLoading}
      />

      {/* Toast */}
      {toast && (
        <div className={`fixed bottom-6 right-6 px-4 py-2 rounded-lg shadow-lg text-sm z-50 ${toast.type === 'success' ? 'bg-emerald-600 text-white' : 'bg-red-600 text-white'}`}>
          {toast.text}
        </div>
      )}
    </div>
  );
}

/** 统计卡片：`labelKey` 只持有真源 key，渲染处经 `t()`（避免原始 key 漏到界面） */
function StatCard({ labelKey, value, icon, bg, textColor }: { labelKey: TranslationKey; value: number; icon: React.ReactNode; bg: string; textColor: string }) {
  const { t } = useI18n();
  return (
    <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4 shadow-sm hover:shadow-md transition-shadow">
      <div className="flex items-center justify-between">
        <span className="text-xs text-gray-500 dark:text-slate-400">{t(labelKey)}</span>
        <div className={`p-2 rounded-lg ${bg}`}>
          <span className={textColor}>{icon}</span>
        </div>
      </div>
      <div className={`text-2xl font-bold mt-3 ${textColor}`}>{value}</div>
    </div>
  );
}

function Pagination({ current, total, onChange }: { current: number; total: number; onChange: (p: number) => void }) {
  const { t } = useI18n();
  return (
    <div className="flex items-center justify-between px-4 py-3 border-t border-gray-100 dark:border-slate-700/50 text-sm">
      <span className="text-gray-500 dark:text-slate-400">{t('automation.common.paginationPageOf', { page: current, total })}</span>
      <div className="flex gap-1">
        <button
          onClick={() => onChange(Math.max(1, current - 1))}
          disabled={current <= 1}
          className="p-2 text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition-colors"
        >
          <ChevronLeft className="w-4 h-4" />
        </button>
        <button
          onClick={() => onChange(Math.min(total, current + 1))}
          disabled={current >= total}
          className="p-2 text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition-colors"
        >
          <ChevronRight className="w-4 h-4" />
        </button>
      </div>
    </div>
  );
}
