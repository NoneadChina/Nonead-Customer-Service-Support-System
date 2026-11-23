import { useState, useEffect, useCallback, useMemo, memo } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { customReportApi, mailboxApi, teamApi } from '../api';
import { useToast } from '../context/ToastContext';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import type {
  CustomReportDto,
  CustomReportExecutionResultDto,
  ReportTrendPointDto,
  ReportStatusDistributionDto,
  ReportUserPerformanceDto,
  ReportMailboxLoadDto,
  ReportCustomerDomainDto,
  ReportConfigDto,
  DateRangeConfig,
  SectionConfig,
  MailboxResponse,
  TeamDto,
} from '../api/types';
import { formatDate } from '../utils/format';
import {
  ArrowLeft,
  Save,
  Eye,
  CheckCircle2,
  AlertCircle,
  Mail,
  Users,
  BarChart3,
  Plus,
  Check,
  TrendingUp,
  PieChart,
  Server,
  Globe,
  RefreshCw,
  Sparkles,
} from 'lucide-react';

// ——— 预览组件（复用 CustomReportView 里的 SVG 图表风格）———
// 日期范围预设复用真源 `format.dateRange.*`（中文逐字相同），不在本页命名空间里再造同义键
const PRESET_OPTIONS: { value: DateRangeConfig['preset']; labelKey: TranslationKey }[] = [
  { value: 'last7', labelKey: 'format.dateRange.last7Days' },
  { value: 'last30', labelKey: 'format.dateRange.last30Days' },
  { value: 'last90', labelKey: 'format.dateRange.last90Days' },
  { value: 'thisWeek', labelKey: 'format.dateRange.thisWeek' },
  { value: 'thisMonth', labelKey: 'format.dateRange.thisMonth' },
  { value: 'lastMonth', labelKey: 'format.dateRange.lastMonth' },
  { value: null, labelKey: 'format.dateRange.custom' },
];

/** 常量表一律持有 `labelKey`/`descKey`（而非已翻译的 `label`）：译文只能在组件内按当前语言取 */
const CATEGORY_OPTIONS: { value: string; labelKey: TranslationKey; descKey: TranslationKey }[] = [
  { value: 'performance', labelKey: 'data.customReportEditor.category.performance', descKey: 'data.customReportEditor.category.performanceDesc' },
  { value: 'efficiency', labelKey: 'data.customReportEditor.category.efficiency', descKey: 'data.customReportEditor.category.efficiencyDesc' },
  { value: 'customer', labelKey: 'data.customReportEditor.category.customer', descKey: 'data.customReportEditor.category.customerDesc' },
  { value: 'workload', labelKey: 'data.customReportEditor.category.workload', descKey: 'data.customReportEditor.category.workloadDesc' },
];

const VISIBILITY_OPTIONS: { value: string; labelKey: TranslationKey; icon: string; descKey: TranslationKey }[] = [
  { value: 'private', labelKey: 'data.customReportEditor.visibility.private', icon: '🔒', descKey: 'data.customReportEditor.visibility.privateDesc' },
  { value: 'team', labelKey: 'data.customReportEditor.visibility.team', icon: '👥', descKey: 'data.customReportEditor.visibility.teamDesc' },
  { value: 'public', labelKey: 'data.customReportEditor.visibility.public', icon: '🌐', descKey: 'data.customReportEditor.visibility.publicDesc' },
];

const AVAILABLE_SECTIONS: { key: SectionConfig['key']; labelKey: TranslationKey; descKey: TranslationKey; chartType: string; icon: React.ReactNode; color: string }[] = [
  { key: 'summary', labelKey: 'data.customReportEditor.module.summary', descKey: 'data.customReportEditor.module.summaryDesc', chartType: 'kpi', icon: <Sparkles size={18} />, color: 'from-indigo-500 to-blue-500' },
  { key: 'trend', labelKey: 'data.customReportEditor.module.trend', descKey: 'data.customReportEditor.module.trendDesc', chartType: 'line', icon: <TrendingUp size={18} />, color: 'from-blue-500 to-cyan-500' },
  { key: 'statusDistribution', labelKey: 'data.customReportEditor.module.statusDistribution', descKey: 'data.customReportEditor.module.statusDistributionDesc', chartType: 'pie', icon: <PieChart size={18} />, color: 'from-emerald-500 to-teal-500' },
  { key: 'userPerformance', labelKey: 'data.customReportEditor.module.userPerformance', descKey: 'data.customReportEditor.module.userPerformanceDesc', chartType: 'table', icon: <Users size={18} />, color: 'from-purple-500 to-fuchsia-500' },
  { key: 'mailboxLoad', labelKey: 'data.customReportEditor.module.mailboxLoad', descKey: 'data.customReportEditor.module.mailboxLoadDesc', chartType: 'table', icon: <Server size={18} />, color: 'from-sky-500 to-indigo-500' },
  { key: 'customerDomains', labelKey: 'data.customReportEditor.module.customerDomains', descKey: 'data.customReportEditor.module.customerDomainsDesc', chartType: 'table', icon: <Globe size={18} />, color: 'from-amber-500 to-orange-500' },
];

/**
 * 后端 `ReportService.GetStatusName`（`ConversationStatus` 枚举）的数字码 → 前端词条 key。
 * 后端直接下发**中文标签** `statusName`，英文界面下会直出中文，因此按稳定数字码查前端字典。
 * 类型用 `| undefined` 而非纯 `TranslationKey`：未知码在运行期可达，类型上必须如实可空。
 */
const STATUS_KEY_BY_CODE: Record<string, TranslationKey | undefined> = {
  '1': 'format.status.active',
  '2': 'format.status.pending',
  '3': 'format.status.closed',
  '4': 'format.status.spam',
};

// 轻量预览渲染（避免循环依赖，独立封装一套简化版预览组件）
const PreviewRenderer = memo(function PreviewRenderer({ result }: { result: CustomReportExecutionResultDto | null }) {
  // 本组件被 memo 包裹，但它同时是 I18n context 的消费者：切语言时 context 变更会照常触发重渲染
  const { t } = useI18n();

  if (!result) {
    return (
      <div className="flex flex-col items-center justify-center py-20 text-slate-400 dark:text-slate-500 border-2 border-dashed rounded-2xl border-slate-200 dark:border-slate-700">
        <Sparkles className="w-12 h-12 mb-3 opacity-50" />
        <p className="text-sm">{t('data.customReportEditor.preview.emptyHint')}</p>
        <p className="text-xs mt-1 opacity-75">{t('data.customReportEditor.preview.emptyHintSub')}</p>
      </div>
    );
  }
  const sections = result.sections ?? [];
  const s = sections.find((x) => x.key === 'summary')?.data as Record<string, unknown> | undefined;
  const trend = sections.find((x) => x.key === 'trend')?.data as ReportTrendPointDto[] | undefined;
  const statusDist = sections.find((x) => x.key === 'statusDistribution')?.data as ReportStatusDistributionDto[] | undefined;
  const userPerf = sections.find((x) => x.key === 'userPerformance')?.data as ReportUserPerformanceDto[] | undefined;
  const mailboxLoad = sections.find((x) => x.key === 'mailboxLoad')?.data as ReportMailboxLoadDto[] | undefined;
  const domains = sections.find((x) => x.key === 'customerDomains')?.data as ReportCustomerDomainDto[] | undefined;
  const miniKpi = (title: string, value: unknown, iconBg: string, icon: React.ReactNode) => (
    <div className="bg-slate-50 dark:bg-slate-900/40 rounded-lg p-3">
      <div className="flex items-center justify-between">
        <div>
          <div className="text-[10px] text-slate-500 dark:text-slate-400 uppercase tracking-wide">{title}</div>
          <div className="text-lg font-bold text-slate-900 dark:text-slate-100 mt-1">{value != null && value !== '' ? String(value) : '-'}</div>
        </div>
        <div className={`w-8 h-8 rounded-lg flex items-center justify-center ${iconBg}`}>{icon}</div>
      </div>
    </div>
  );
  /** 后端下发的 `statusName` 是中文标签 → 按数字码查前端字典；未知码显式回落 `format.unknown` */
  const statusLabel = (code: number) => {
    const key = STATUS_KEY_BY_CODE[String(code)];
    return key ? t(key) : t('format.unknown');
  };
  return (
    <div className="space-y-4">
      {s && (
        <div className="grid grid-cols-2 gap-2">
          {miniKpi(t('data.common.metric.newConversations'), s.newConversations ?? s.newTickets, 'bg-blue-100 dark:bg-blue-900/40', <Plus size={14} className="text-blue-600 dark:text-blue-400" />)}
          {miniKpi(t('data.common.metric.closedConversations'), s.closedConversations ?? s.closedTickets, 'bg-emerald-100 dark:bg-emerald-900/40', <Check size={14} className="text-emerald-600 dark:text-emerald-400" />)}
          {miniKpi(t('data.common.metric.agentReplies'), s.totalReplies ?? s.replyCount, 'bg-amber-100 dark:bg-amber-900/40', <BarChart3 size={14} className="text-amber-600 dark:text-amber-400" />)}
          {miniKpi(t('data.common.metric.newCustomers'), s.newCustomers ?? s.newCustomerCount, 'bg-purple-100 dark:bg-purple-900/40', <Users size={14} className="text-purple-600 dark:text-purple-400" />)}
        </div>
      )}
      {trend && trend.length > 0 && (
        <div className="bg-slate-50 dark:bg-slate-900/40 rounded-lg p-3">
          <div className="text-xs font-medium text-slate-600 dark:text-slate-300 mb-2 flex items-center gap-1.5"><TrendingUp size={12} />{t('data.customReportEditor.preview.trendPoints', { count: trend.length })}</div>
          <div className="h-28 w-full flex items-end gap-0.5">
            {trend.map((p, i) => {
              const v = (p.newCount ?? 0) + 1;
              const max = Math.max(...trend.map((t) => t.newCount ?? 0), 1);
              return <div key={i} className="flex-1 rounded-t bg-gradient-to-t from-indigo-400 to-indigo-600 dark:from-indigo-500 dark:to-indigo-400" style={{ height: `${(v / max) * 100}%` }} />;
            })}
          </div>
        </div>
      )}
      {statusDist && statusDist.length > 0 && (
        <div className="bg-slate-50 dark:bg-slate-900/40 rounded-lg p-3">
          <div className="text-xs font-medium text-slate-600 dark:text-slate-300 mb-2 flex items-center gap-1.5"><PieChart size={12} />{t('data.customReportEditor.module.statusDistribution')}</div>
          <div className="space-y-1.5">
            {statusDist.map((d) => (
              <div key={d.status}>
                <div className="flex justify-between text-[11px] mb-0.5">
                  <span className="text-slate-600 dark:text-slate-400">{statusLabel(d.status)}</span>
                  <span className="font-medium text-slate-700 dark:text-slate-300">{d.count}</span>
                </div>
                <div className="h-1.5 rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden">
                  <div className="h-full rounded-full bg-emerald-500" style={{ width: `${d.percentage ?? 0}%` }} />
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
      {userPerf && userPerf.length > 0 && (
        <div className="bg-slate-50 dark:bg-slate-900/40 rounded-lg p-3">
          <div className="text-xs font-medium text-slate-600 dark:text-slate-300 mb-2 flex items-center gap-1.5"><Users size={12} />{t('data.customReportEditor.preview.userPerformanceTop')}</div>
          <div className="space-y-1">
            {userPerf.slice(0, 5).map((u) => (
              <div key={u.userId} className="flex items-center justify-between text-[11px] py-1">
                <span className="truncate text-slate-700 dark:text-slate-300 flex-1 min-w-0">{u.userName}</span>
                <span className="text-indigo-600 dark:text-indigo-400 font-medium ml-2">{t('data.customReportEditor.preview.replies', { count: u.replyCount ?? 0 })}</span>
              </div>
            ))}
          </div>
        </div>
      )}
      {mailboxLoad && mailboxLoad.length > 0 && (
        <div className="bg-slate-50 dark:bg-slate-900/40 rounded-lg p-3">
          <div className="text-xs font-medium text-slate-600 dark:text-slate-300 mb-2 flex items-center gap-1.5"><Server size={12} />{t('data.customReportEditor.module.mailboxLoad')}</div>
          <div className="space-y-1.5">
            {mailboxLoad.slice(0, 5).map((m) => (
              <div key={m.mailboxId}>
                <div className="flex justify-between text-[11px] mb-0.5">
                  <span className="truncate text-slate-600 dark:text-slate-400 max-w-[160px]">{m.mailboxName ?? m.email}</span>
                  <span className="font-medium text-slate-700 dark:text-slate-300">{m.newCount ?? 0}</span>
                </div>
                <div className="h-1.5 rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden">
                  <div className="h-full rounded-full bg-blue-500" style={{ width: `${m.percentage ?? 0}%` }} />
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
      {domains && domains.length > 0 && (
        <div className="bg-slate-50 dark:bg-slate-900/40 rounded-lg p-3">
          <div className="text-xs font-medium text-slate-600 dark:text-slate-300 mb-2 flex items-center gap-1.5"><Globe size={12} />{t('data.customReportEditor.preview.customerDomainsTop')}</div>
          <div className="flex flex-wrap gap-1">
            {domains.slice(0, 6).map((d, i) => (
              <span key={i} className="text-[10px] px-2 py-0.5 rounded-full bg-white dark:bg-slate-800 text-slate-600 dark:text-slate-400 border border-slate-200 dark:border-slate-700">
                {d.domain} ({d.conversationCount})
              </span>
            ))}
          </div>
        </div>
      )}
      {sections.length === 0 && (
        <div className="p-8 text-center text-xs text-slate-400 dark:text-slate-500 border border-dashed border-slate-200 dark:border-slate-700 rounded-lg">
          {t('data.customReportEditor.preview.emptySections')}
        </div>
      )}
    </div>
  );
});

interface FormState {
  name: string;
  description: string;
  category: string;
  visibility: 'private' | 'team' | 'public';
  teamId: number | null;
  dateRange: DateRangeConfig;
  mailboxId: number | null;
  groupBy: 'none' | 'week' | 'month';
  sectionKeys: string[];
}

const EMPTY_FORM: FormState = {
  name: '',
  description: '',
  category: 'performance',
  visibility: 'private',
  teamId: null,
  dateRange: { type: 'preset', preset: 'last30', start: null, end: null },
  mailboxId: null,
  groupBy: 'week',
  sectionKeys: ['summary', 'trend', 'statusDistribution'],
};

function resolveDateRangeConfig(r: DateRangeConfig): { start: string; end: string } {
  const now = new Date();
  const fmt = (d: Date) => d.toISOString().slice(0, 10);
  if (r.type === 'custom' && r.start && r.end) return { start: r.start, end: r.end };
  let start = new Date(now);
  const end = new Date(now);
  switch (r.preset) {
    case 'last7':
      start.setDate(start.getDate() - 7);
      break;
    case 'last30':
      start.setDate(start.getDate() - 30);
      break;
    case 'last90':
      start.setDate(start.getDate() - 90);
      break;
    case 'thisWeek': {
      const day = start.getDay() === 0 ? 6 : start.getDay() - 1;
      start.setDate(start.getDate() - day);
      break;
    }
    case 'thisMonth':
      start = new Date(now.getFullYear(), now.getMonth(), 1);
      break;
    case 'lastMonth': {
      start = new Date(now.getFullYear(), now.getMonth() - 1, 1);
      const endLast = new Date(now.getFullYear(), now.getMonth(), 0);
      return { start: fmt(start), end: fmt(endLast) };
    }
    default:
      start.setDate(start.getDate() - 30);
  }
  return { start: fmt(start), end: fmt(end) };
}

export default function CustomReportEditor() {
  const { id } = useParams();
  const navigate = useNavigate();
  const toast = useToast();
  const { t } = useI18n();

  const isNew = !id || id === 'new';
  const editingId = id && !isNew ? Number(id) : NaN;

  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [loading, setLoading] = useState(!isNew);
  const [saving, setSaving] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [previewResult, setPreviewResult] = useState<CustomReportExecutionResultDto | null>(null);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const [validationErrors, setValidationErrors] = useState<Record<string, string>>({});

  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [teams, setTeams] = useState<{ id: number; name: string }[]>([]);

  useEffect(() => {
    mailboxApi.listAll().then((r) => setMailboxes(Array.isArray(r) ? r : [])).catch(() => {});
    teamApi
      .list({ page: 1, pageSize: 200 })
      .then((r) => {
        const arr: TeamDto[] = Array.isArray(r) ? r : r.data ?? [];
        setTeams(
          arr
            .filter((t) => t && typeof t.id === 'number')
            .map((t) => ({ id: t.id, name: t.name ?? `Team ${t.id}` }))
        );
      })
      .catch(() => {});
  }, []);

  const loadExisting = useCallback(async () => {
    if (!editingId || isNaN(editingId)) return;
    setLoading(true);
    try {
      const r = await customReportApi.getById(editingId);
      let cfg: ReportConfigDto | null = null;
      try {
        cfg = r.reportConfig ? JSON.parse(r.reportConfig) : null;
      } catch {
        cfg = null;
      }
      const sections: SectionConfig[] = cfg?.sections ?? [];
      setForm({
        name: r.name,
        description: r.description ?? '',
        category: r.category,
        visibility: r.visibility,
        teamId: r.teamId ?? null,
        dateRange: cfg?.dateRange ?? { type: 'preset', preset: 'last30', start: null, end: null },
        mailboxId: cfg?.mailboxId ?? null,
        groupBy: cfg?.groupBy ?? 'week',
        sectionKeys: sections.map((s) => s.key),
      });
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.common.loadFailed');
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }, [editingId, toast, t]);

  useEffect(() => {
    if (!isNew) loadExisting();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editingId]);

  const validate = (): boolean => {
    const errs: Record<string, string> = {};
    if (!form.name.trim()) errs.name = t('data.customReportEditor.validation.nameRequired');
    else if (form.name.length > 100) errs.name = t('data.customReportEditor.validation.nameMaxLength', { max: 100 });
    if (!CATEGORY_OPTIONS.some((c) => c.value === form.category)) errs.category = t('data.customReportEditor.validation.categoryRequired');
    if (!['private', 'team', 'public'].includes(form.visibility)) errs.visibility = t('data.customReportEditor.validation.visibilityRequired');
    if (form.visibility === 'team' && !form.teamId) errs.teamId = t('data.customReportEditor.validation.teamRequired');
    if (form.dateRange.type === 'custom') {
      if (!form.dateRange.start) errs['dateRange.start'] = t('data.customReportEditor.validation.startRequired');
      if (!form.dateRange.end) errs['dateRange.end'] = t('data.customReportEditor.validation.endRequired');
      if (form.dateRange.start && form.dateRange.end && form.dateRange.start > form.dateRange.end)
        errs['dateRange.range'] = t('data.customReportEditor.validation.rangeInvalid');
    }
    if (form.sectionKeys.length === 0) errs.sections = t('data.customReportEditor.validation.sectionsRequired');
    setValidationErrors(errs);
    return Object.keys(errs).length === 0;
  };

  const buildReportConfigJson = (): string => {
    const sections: SectionConfig[] = form.sectionKeys.map((k) => {
      const def = AVAILABLE_SECTIONS.find((a) => a.key === k);
      return { key: k, chartType: def?.chartType ?? 'table' };
    });
    const cfg: ReportConfigDto = {
      dateRange: form.dateRange,
      mailboxId: form.mailboxId ?? undefined,
      groupBy: form.groupBy,
      sections,
    };
    return JSON.stringify(cfg);
  };

  const handleSave = async (andPreview = false): Promise<number | null> => {
    if (!validate()) {
      toast.error(t('data.customReportEditor.error.fixFormErrors'));
      return null;
    }
    setSaving(true);
    try {
      const reportConfig = buildReportConfigJson();
      const payload = {
        name: form.name.trim(),
        description: form.description.trim() || null,
        category: form.category,
        visibility: form.visibility,
        teamId: form.visibility === 'team' ? (form.teamId ?? undefined) : null,
        reportConfig,
      };
      let saved: CustomReportDto;
      if (isNew) {
        saved = await customReportApi.create(payload);
        toast.success(t('data.customReportEditor.toast.created'));
      } else {
        if (isNaN(editingId)) throw new Error(t('data.customReportEditor.error.missingEditId'));
        saved = await customReportApi.update(editingId, payload);
        toast.success(t('data.customReportEditor.toast.updated'));
      }
      if (isNew && !andPreview) {
        navigate(`/modules/custom-reports/view/${saved.id}`, { replace: true });
      }
      return saved.id;
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.customReportEditor.error.saveFailed');
      toast.error(msg);
      return null;
    } finally {
      setSaving(false);
    }
  };

  const handlePreview = async () => {
    if (!validate()) {
      toast.error(t('data.customReportEditor.error.fixFormErrorsFirst'));
      return;
    }
    setPreviewing(true);
    setPreviewError(null);
    try {
      // 预览不持久化：直接用当前表单配置调用 execute-preview 端点
      const payload = {
        name: form.name.trim() || t('data.customReportEditor.preview.fallbackName'),
        description: form.description.trim() || null,
        category: form.category,
        visibility: form.visibility,
        teamId: form.visibility === 'team' ? (form.teamId ?? undefined) : null,
        reportConfig: buildReportConfigJson(),
      };
      const r = await customReportApi.executePreview(payload);
      setPreviewResult(r);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.customReportEditor.error.previewFailed');
      setPreviewError(msg);
    } finally {
      setPreviewing(false);
    }
  };

  const toggleSection = (key: string) => {
    setForm((f) => ({
      ...f,
      sectionKeys: f.sectionKeys.includes(key)
        ? f.sectionKeys.filter((k) => k !== key)
        : [...f.sectionKeys, key],
    }));
  };

  const currentDateRangeText = useMemo(() => {
    if (form.dateRange.type === 'custom' && form.dateRange.start && form.dateRange.end)
      return `${formatDate(form.dateRange.start)} ~ ${formatDate(form.dateRange.end)}`;
    const found = PRESET_OPTIONS.find((p) => p.value === (form.dateRange.preset ?? null));
    if (found) return t(found.labelKey);
    const r = resolveDateRangeConfig(form.dateRange);
    return `${r.start} ~ ${r.end}`;
  }, [form.dateRange, t]);

  if (loading) {
    return (
      <div className="p-16 text-center">
        <RefreshCw className="w-6 h-6 animate-spin mx-auto mb-3 text-indigo-500" />
        <div className="text-slate-500 dark:text-slate-400 text-sm">{t('data.customReportEditor.loading.report')}</div>
      </div>
    );
  }

  return (
    <div className="space-y-5">
      {/* 顶部工具条 */}
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="space-y-2">
          <div className="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
            <Link to="/modules/custom-reports" className="inline-flex items-center gap-1 hover:text-indigo-600 dark:hover:text-indigo-400 transition-colors">
              <ArrowLeft size={12} />
              {t('data.customReportEditor.listLink')}
            </Link>
            <span>/</span>
            <span>{isNew ? t('data.customReportEditor.action.new') : t('common.edit')}</span>
          </div>
          <div className="flex items-center gap-3">
            <div className="w-12 h-12 rounded-xl bg-gradient-to-br from-indigo-100 to-violet-100 dark:from-indigo-900/40 dark:to-violet-900/40 flex items-center justify-center flex-shrink-0">
              <Sparkles className="w-6 h-6 text-indigo-600 dark:text-indigo-400" />
            </div>
            <div>
              <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">
                {isNew ? t('data.customReportEditor.createTitle') : t('data.customReportEditor.editTitle')}
              </h1>
              <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">
                {t('data.customReportEditor.subtitle')}
              </p>
            </div>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => navigate('/modules/custom-reports')}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-700 rounded-xl transition-all shadow-sm"
          >
            <ArrowLeft size={15} />
            {t('data.customReportEditor.backToList')}
          </button>
          <button
            onClick={() => handlePreview()}
            disabled={saving || previewing}
            className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-700 rounded-xl transition-all shadow-sm disabled:opacity-50"
          >
            <Eye size={15} className={previewing ? 'animate-pulse' : ''} />
            {previewing ? t('data.customReportEditor.action.executing') : t('data.customReportEditor.action.preview')}
          </button>
          <button
            onClick={() => handleSave(false)}
            disabled={saving || previewing}
            className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-gradient-to-r from-indigo-600 to-violet-600 hover:from-indigo-700 hover:to-violet-700 rounded-xl transition-all shadow-sm disabled:opacity-50"
          >
            <Save size={15} />
            {saving ? t('data.customReportEditor.action.saving') : t('common.save')}
          </button>
        </div>
      </div>

      {/* 主体 */}
      <div className="grid grid-cols-1 xl:grid-cols-5 gap-5 items-start">
        {/* 左侧：配置表单 */}
        <div className="xl:col-span-3 space-y-5">
          {/* 基础信息 */}
          <SectionCard title={t('data.customReportEditor.card.basicInfo')} desc={t('data.customReportEditor.card.basicInfoDesc')}>
            <div className="space-y-4">
              <div>
                <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1.5">
                  {t('data.customReportEditor.field.name')} <span className="text-rose-500">*</span>
                </label>
                <input
                  type="text"
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  placeholder={t('data.customReportEditor.placeholder.name')}
                  maxLength={100}
                  className={`w-full px-3.5 py-2.5 text-sm border rounded-xl bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 transition-all ${
                    validationErrors.name
                      ? 'border-rose-300 dark:border-rose-700 focus:ring-rose-200 dark:focus:ring-rose-900/50'
                      : 'border-slate-200 dark:border-slate-600 focus:ring-indigo-500/20'
                  }`}
                />
                {validationErrors.name && <p className="mt-1 text-xs text-rose-500 flex items-center gap-1"><AlertCircle size={11} />{validationErrors.name}</p>}
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1.5">{t('data.customReportEditor.field.description')}</label>
                <textarea
                  value={form.description}
                  onChange={(e) => setForm({ ...form, description: e.target.value })}
                  placeholder={t('data.customReportEditor.placeholder.description')}
                  rows={2}
                  maxLength={500}
                  className="w-full px-3.5 py-2.5 text-sm border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500/20 resize-none"
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-2">
                  {t('data.customReportEditor.field.category')} <span className="text-rose-500">*</span>
                </label>
                <div className="grid grid-cols-2 md:grid-cols-4 gap-2.5">
                  {CATEGORY_OPTIONS.map((c) => {
                    const active = form.category === c.value;
                    return (
                      <button
                        type="button"
                        key={c.value}
                        onClick={() => setForm({ ...form, category: c.value })}
                        className={`p-3 rounded-xl border text-left transition-all ${
                          active
                            ? 'border-indigo-500 bg-indigo-50 dark:bg-indigo-900/30 dark:border-indigo-600 shadow-sm'
                            : 'border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-800 hover:border-slate-300 dark:hover:border-slate-500'
                        }`}
                      >
                        <div className={`font-semibold text-sm flex items-center gap-1.5 ${active ? 'text-indigo-700 dark:text-indigo-400' : 'text-slate-800 dark:text-slate-200'}`}>
                          {active && <CheckCircle2 size={14} />}
                          {t(c.labelKey)}
                        </div>
                        <div className="text-[11px] text-slate-500 dark:text-slate-400 mt-1">{t(c.descKey)}</div>
                      </button>
                    );
                  })}
                </div>
                {validationErrors.category && <p className="mt-1 text-xs text-rose-500">{validationErrors.category}</p>}
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-2">
                  {t('data.customReportEditor.field.visibility')} <span className="text-rose-500">*</span>
                </label>
                <div className="grid grid-cols-1 md:grid-cols-3 gap-2.5">
                  {VISIBILITY_OPTIONS.map((v) => {
                    const active = form.visibility === v.value;
                    return (
                      <button
                        type="button"
                        key={v.value}
                        onClick={() => setForm({ ...form, visibility: v.value as FormState['visibility'] })}
                        className={`p-3 rounded-xl border text-left transition-all ${
                          active
                            ? 'border-indigo-500 bg-indigo-50 dark:bg-indigo-900/30 dark:border-indigo-600 shadow-sm'
                            : 'border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-800 hover:border-slate-300 dark:hover:border-slate-500'
                        }`}
                      >
                        <div className={`font-semibold text-sm flex items-center gap-1.5 ${active ? 'text-indigo-700 dark:text-indigo-400' : 'text-slate-800 dark:text-slate-200'}`}>
                          <span className="text-base">{v.icon}</span>
                          {t(v.labelKey)}
                          {active && <CheckCircle2 size={14} className="ml-auto" />}
                        </div>
                        <div className="text-[11px] text-slate-500 dark:text-slate-400 mt-1">{t(v.descKey)}</div>
                      </button>
                    );
                  })}
                </div>
                {validationErrors.visibility && <p className="mt-1 text-xs text-rose-500">{validationErrors.visibility}</p>}
              </div>

              {form.visibility === 'team' && (
                <div>
                  <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1.5">
                    {t('data.customReportEditor.field.team')} <span className="text-rose-500">*</span>
                  </label>
                  <select
                    value={form.teamId ?? ''}
                    onChange={(e) => setForm({ ...form, teamId: e.target.value ? Number(e.target.value) : null })}
                    className={`w-full px-3.5 py-2.5 text-sm border rounded-xl bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 transition-all ${
                      validationErrors.teamId
                        ? 'border-rose-300 dark:border-rose-700 focus:ring-rose-200 dark:focus:ring-rose-900/50'
                        : 'border-slate-200 dark:border-slate-600 focus:ring-indigo-500/20'
                    }`}
                  >
                    <option value="">{t('data.customReportEditor.option.selectTeam')}</option>
                    {teams.map((t) => (
                      <option key={t.id} value={t.id}>{t.name}</option>
                    ))}
                  </select>
                  {validationErrors.teamId && <p className="mt-1 text-xs text-rose-500">{validationErrors.teamId}</p>}
                </div>
              )}
            </div>
          </SectionCard>

          {/* 数据范围 */}
          <SectionCard title={t('data.customReportEditor.card.dataRange')} desc={t('data.customReportEditor.card.dataRangeDesc')} badge={currentDateRangeText}>
            <div className="space-y-4">
              <div>
                <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-2">{t('data.customReportEditor.field.dateRange')}</label>
                <div className="grid grid-cols-2 md:grid-cols-4 gap-2 mb-3">
                  {PRESET_OPTIONS.slice(0, -1).map((p) => {
                    const active = form.dateRange.type === 'preset' && form.dateRange.preset === p.value;
                    return (
                      <button
                        type="button"
                        key={p.value ?? 'null'}
                        onClick={() => setForm({ ...form, dateRange: { type: 'preset', preset: p.value, start: null, end: null } })}
                        className={`px-3 py-2 text-xs rounded-lg border transition-all ${
                          active
                            ? 'border-indigo-500 bg-indigo-50 dark:bg-indigo-900/30 dark:border-indigo-600 text-indigo-700 dark:text-indigo-400 font-medium'
                            : 'border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-800 text-slate-600 dark:text-slate-400 hover:border-slate-300'
                        }`}
                      >
                        {t(p.labelKey)}
                      </button>
                    );
                  })}
                  <button
                    type="button"
                    onClick={() => {
                      const { start, end } = resolveDateRangeConfig(form.dateRange);
                      setForm({
                        ...form,
                        dateRange: { type: 'custom', preset: null, start: form.dateRange.start ?? start, end: form.dateRange.end ?? end },
                      });
                    }}
                    className={`px-3 py-2 text-xs rounded-lg border transition-all ${
                      form.dateRange.type === 'custom'
                        ? 'border-indigo-500 bg-indigo-50 dark:bg-indigo-900/30 dark:border-indigo-600 text-indigo-700 dark:text-indigo-400 font-medium'
                        : 'border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-800 text-slate-600 dark:text-slate-400 hover:border-slate-300'
                    }`}
                  >
                    {t('format.dateRange.custom')}
                  </button>
                </div>
                {form.dateRange.type === 'custom' && (
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-[11px] text-slate-500 dark:text-slate-400 mb-1">{t('data.common.startDate')}</label>
                      <input
                        type="date"
                        value={form.dateRange.start ?? ''}
                        onChange={(e) => setForm({ ...form, dateRange: { ...form.dateRange, start: e.target.value || null } })}
                        className="w-full px-3 py-2 text-sm border border-slate-200 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500/20"
                      />
                      {validationErrors['dateRange.start'] && <p className="mt-1 text-xs text-rose-500">{validationErrors['dateRange.start']}</p>}
                    </div>
                    <div>
                      <label className="block text-[11px] text-slate-500 dark:text-slate-400 mb-1">{t('data.common.endDate')}</label>
                      <input
                        type="date"
                        value={form.dateRange.end ?? ''}
                        onChange={(e) => setForm({ ...form, dateRange: { ...form.dateRange, end: e.target.value || null } })}
                        className="w-full px-3 py-2 text-sm border border-slate-200 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500/20"
                      />
                      {validationErrors['dateRange.end'] && <p className="mt-1 text-xs text-rose-500">{validationErrors['dateRange.end']}</p>}
                    </div>
                  </div>
                )}
                {validationErrors['dateRange.range'] && <p className="text-xs text-rose-500 mt-1">{validationErrors['dateRange.range']}</p>}
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1.5">{t('data.customReportEditor.field.mailboxOptional')}</label>
                  <div className="relative">
                    <Mail size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
                    <select
                      value={form.mailboxId ?? ''}
                      onChange={(e) => setForm({ ...form, mailboxId: e.target.value ? Number(e.target.value) : null })}
                      className="w-full pl-9 pr-3 py-2.5 text-sm border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500/20"
                    >
                      <option value="">{t('data.common.allMailboxes')}</option>
                      {mailboxes.map((m) => (
                        <option key={m.id} value={m.id}>{m.name} &lt;{m.email}&gt;</option>
                      ))}
                    </select>
                  </div>
                </div>
                <div>
                  <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1.5">{t('data.customReportEditor.field.groupBy')}</label>
                  <select
                    value={form.groupBy}
                    onChange={(e) => setForm({ ...form, groupBy: e.target.value as FormState['groupBy'] })}
                    className="w-full px-3.5 py-2.5 text-sm border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500/20"
                  >
                    <option value="none">{t('data.customReportEditor.option.groupNone')}</option>
                    <option value="week">{t('data.customReportEditor.option.groupWeek')}</option>
                    <option value="month">{t('data.customReportEditor.option.groupMonth')}</option>
                  </select>
                </div>
              </div>
            </div>
          </SectionCard>

          {/* Sections 选择 */}
          <SectionCard title={t('data.customReportEditor.card.sections')} desc={t('data.customReportEditor.card.sectionsDesc')} badge={t('data.customReportEditor.card.selectedBadge', { selected: form.sectionKeys.length, total: AVAILABLE_SECTIONS.length })}>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
              {AVAILABLE_SECTIONS.map((s) => {
                const checked = form.sectionKeys.includes(s.key);
                return (
                  <label
                    key={s.key}
                    className={`group relative p-3.5 rounded-xl border cursor-pointer transition-all flex items-start gap-3 ${
                      checked
                        ? 'border-indigo-500 bg-gradient-to-br from-white dark:from-slate-800 to-indigo-50/50 dark:to-indigo-900/20 shadow-sm'
                        : 'border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-800 hover:border-slate-300 dark:hover:border-slate-500'
                    }`}
                  >
                    <input
                      type="checkbox"
                      checked={checked}
                      onChange={() => toggleSection(s.key)}
                      className="mt-0.5 rounded text-indigo-600 focus:ring-indigo-500/20"
                    />
                    <div className={`w-10 h-10 rounded-xl bg-gradient-to-br ${s.color} flex items-center justify-center text-white shadow-sm flex-shrink-0 transition-transform group-hover:scale-110`}>
                      {s.icon}
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className={`font-semibold text-sm ${checked ? 'text-indigo-700 dark:text-indigo-400' : 'text-slate-800 dark:text-slate-200'}`}>
                        {t(s.labelKey)}
                      </div>
                      <div className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">{t(s.descKey)}</div>
                    </div>
                    {checked && <Check size={16} className="text-indigo-500 mt-0.5 flex-shrink-0" />}
                  </label>
                );
              })}
            </div>
            {validationErrors.sections && (
              <p className="mt-3 text-xs text-rose-500 flex items-center gap-1"><AlertCircle size={11} />{validationErrors.sections}</p>
            )}
          </SectionCard>
        </div>

        {/* 右侧：预览面板 */}
        <div className="xl:col-span-2 space-y-4 sticky top-4">
          <div className="bg-white dark:bg-slate-800 rounded-2xl border border-slate-100 dark:border-slate-700 shadow-sm overflow-hidden">
            <div className="flex items-center justify-between px-5 py-3.5 border-b border-slate-100 dark:border-slate-700">
              <div className="flex items-center gap-2">
                <Eye size={16} className="text-indigo-500" />
                <h3 className="font-semibold text-slate-900 dark:text-slate-100 text-sm">{t('data.customReportEditor.preview.title')}</h3>
              </div>
              <button
                onClick={() => handlePreview()}
                disabled={saving || previewing}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-indigo-600 dark:text-indigo-400 hover:bg-indigo-50 dark:hover:bg-indigo-900/30 rounded-lg disabled:opacity-50 transition-colors"
              >
                <RefreshCw size={12} className={previewing ? 'animate-spin' : ''} />
                {previewing ? t('data.customReportEditor.action.executing') : t('data.customReportEditor.preview.refresh')}
              </button>
            </div>
            <div className="p-4 max-h-[calc(100vh-220px)] overflow-y-auto scrollbar-thin">
              {previewing && !previewResult && (
                <div className="flex items-center justify-center py-16 text-slate-400 dark:text-slate-500">
                  <RefreshCw className="w-6 h-6 animate-spin mr-2" />
                  {t('data.customReportEditor.preview.generating')}
                </div>
              )}
              {previewError && !previewing && (
                <div className="bg-rose-50 dark:bg-rose-900/30 border border-rose-200 dark:border-rose-800/60 text-rose-700 dark:text-rose-400 text-xs px-3 py-2 rounded-lg mb-3 flex items-start gap-2">
                  <AlertCircle size={14} className="mt-0.5 flex-shrink-0" />
                  <div>
                    <div className="font-medium">{t('data.customReportEditor.error.previewFailed')}</div>
                    <div className="opacity-80 mt-0.5">{previewError}</div>
                  </div>
                </div>
              )}
              <PreviewRenderer result={previewing ? null : previewResult} />
            </div>
          </div>

          <div className="bg-gradient-to-br from-indigo-50/70 to-violet-50/70 dark:from-indigo-900/20 dark:to-violet-900/20 border border-indigo-100 dark:border-indigo-800/40 rounded-xl p-4 text-xs space-y-1.5 text-slate-600 dark:text-slate-300">
            <p className="font-semibold text-indigo-700 dark:text-indigo-400 flex items-center gap-1.5">
              <Sparkles size={13} /> {t('data.customReportEditor.tip.title')}
            </p>
            <ul className="space-y-1 pl-4 list-disc opacity-90">
              <li>{t('data.customReportEditor.tip.previewNoSave')}</li>
              <li>{t('data.customReportEditor.tip.savePersists')}</li>
              <li>{t('data.customReportEditor.tip.dateRangeAdvice')}</li>
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}

// SectionCard 内部组件
interface SectionCardProps {
  title: string;
  desc?: string;
  badge?: string;
  children: React.ReactNode;
}
function SectionCard({ title, desc, badge, children }: SectionCardProps) {
  return (
    <div className="bg-white dark:bg-slate-800 rounded-2xl border border-slate-100 dark:border-slate-700/50 shadow-sm overflow-hidden">
      <div className="flex items-start justify-between gap-3 px-5 py-4 border-b border-slate-50 dark:border-slate-700/50">
        <div>
          <h2 className="font-semibold text-slate-900 dark:text-slate-100">{title}</h2>
          {desc && <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">{desc}</p>}
        </div>
        {badge && (
          <span className="text-[11px] px-2.5 py-1 rounded-full bg-indigo-50 text-indigo-700 dark:bg-indigo-900/40 dark:text-indigo-400 font-medium flex-shrink-0 whitespace-nowrap">
            {badge}
          </span>
        )}
      </div>
      <div className="p-5">{children}</div>
    </div>
  );
}
