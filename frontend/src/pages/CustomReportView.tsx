import { useState, useEffect, useCallback, useMemo } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { customReportApi, mailboxApi } from '../api';
import { useToast } from '../context/ToastContext';
import type {
  CustomReportDto,
  CustomReportExecutionResultDto,
  SectionResultDto,
  ReportTrendPointDto,
  ReportStatusDistributionDto,
  ReportUserPerformanceDto,
  ReportMailboxLoadDto,
  ReportCustomerDomainDto,
  MailboxResponse,
} from '../api/types';
import CustomReportScheduleModal from '../components/CustomReportScheduleModal';
import { formatDate, formatDateTime } from '../utils/format';
import { useI18n } from '../context/I18nContext';
import { toIntlLocale } from '../i18n/locale';
import type { TranslationKey } from '../locales/zh-Hans';
import {
  ArrowLeft,
  RefreshCw,
  Download,
  Pin,
  PinOff,
  Edit2,
  Calendar,
  BarChart3,
  TrendingUp,
  PieChart,
  Users,
  Server,
  Globe,
  Mail,
  MessageSquare,
  Plus,
  Check,
  Clock,
  Shield,
  Timer,
  FileSpreadsheet,
  FileText,
  File,
} from 'lucide-react';

// —— KPI 卡片 ——
interface KpiCardProps {
  title: string;
  value: string | number;
  subtitle?: string;
  icon: React.ReactNode;
  color: string;
}
function KpiCard({ title, value, subtitle, icon, color }: KpiCardProps) {
  return (
    <div className="bg-white dark:bg-slate-800 rounded-xl border border-slate-100 dark:border-slate-700/50 p-5 shadow-sm hover:shadow-md transition-all group">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <p className="text-xs text-slate-500 dark:text-slate-400 uppercase tracking-wide">{title}</p>
          <p className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-1.5 truncate">{value}</p>
          {subtitle && <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">{subtitle}</p>}
        </div>
        <div className={`w-11 h-11 rounded-xl flex items-center justify-center flex-shrink-0 ${color} transition-transform group-hover:scale-110`}>
          {icon}
        </div>
      </div>
    </div>
  );
}

// —— 趋势折线图（复用 Reports.tsx 风格）——
function TrendChart({ data }: { data: ReportTrendPointDto[] }) {
  const { t, language } = useI18n();
  // 横轴日期跟随界面语言（en → en-US），不再固定 zh-CN
  const intlLocale = toIntlLocale(language);
  const width = 880;
  const height = 280;
  const padding = { top: 20, right: 24, bottom: 36, left: 44 };
  const chartWidth = width - padding.left - padding.right;
  const chartHeight = height - padding.top - padding.bottom;
  if (!data || data.length === 0) {
    return <div className="flex items-center justify-center h-48 text-sm text-slate-400 dark:text-slate-400">{t('data.common.noTrendData')}</div>;
  }
  const maxCount = Math.max(...data.flatMap((d) => [d.newCount ?? 0, d.closedCount ?? 0, d.replyCount ?? 0]), 1);
  const stepX = data.length > 1 ? chartWidth / (data.length - 1) : 0;
  const buildPath = (key: 'newCount' | 'closedCount' | 'replyCount') =>
    data
      .map((d, i) => {
        const x = padding.left + i * stepX;
        const v = d[key] ?? 0;
        const y = padding.top + chartHeight - (v / maxCount) * chartHeight;
        return `${i === 0 ? 'M' : 'L'} ${x} ${y}`;
      })
      .join(' ');
  const newPath = buildPath('newCount');
  const closedPath = buildPath('closedCount');
  const replyPath = buildPath('replyCount');
  const yTicks = 4;
  const yTickValues = Array.from({ length: yTicks + 1 }, (_, i) => Math.round((maxCount / yTicks) * i));
  const labelInterval = data.length > 30 ? Math.ceil(data.length / 10) : 1;
  return (
    <div className="w-full overflow-x-auto">
      <svg width={width} height={height} className="min-w-full" style={{ minWidth: 560 }}>
        {yTickValues.map((tick, i) => {
          const y = padding.top + chartHeight - (tick / maxCount) * chartHeight;
          return (
            <g key={i}>
              <line x1={padding.left} y1={y} x2={width - padding.right} y2={y} className="stroke-slate-200 dark:stroke-slate-700" strokeWidth={1} />
              <text x={padding.left - 8} y={y + 4} textAnchor="end" className="text-[10px] fill-slate-400 dark:fill-slate-400">{tick}</text>
            </g>
          );
        })}
        {data.map((d, i) => {
          if (i % labelInterval !== 0 && i !== data.length - 1) return null;
          const x = padding.left + i * stepX;
          const label = new Date(d.date).toLocaleDateString(intlLocale, { month: '2-digit', day: '2-digit' });
          return <text key={i} x={x} y={height - 12} textAnchor="middle" className="text-[10px] fill-slate-500 dark:fill-slate-400">{label}</text>;
        })}
        <path d={newPath} fill="none" stroke="#3b82f6" strokeWidth={2} />
        <path d={closedPath} fill="none" stroke="#10b981" strokeWidth={2} strokeDasharray="4 3" />
        <path d={replyPath} fill="none" stroke="#f59e0b" strokeWidth={2} strokeDasharray="2 2" />
        {data.map((d, i) => {
          const x = padding.left + i * stepX;
          const yNew = padding.top + chartHeight - ((d.newCount ?? 0) / maxCount) * chartHeight;
          return <circle key={`n-${i}`} cx={x} cy={yNew} r={2.5} fill="#3b82f6" />;
        })}
      </svg>
      <div className="flex items-center justify-center gap-5 mt-2 text-xs text-slate-600 dark:text-slate-400">
        <span className="flex items-center gap-1.5"><span className="w-3 h-0.5 bg-blue-500"></span>{t('data.common.metric.newConversations')}</span>
        <span className="flex items-center gap-1.5"><span className="w-3 h-0.5 bg-emerald-500" style={{ borderTop: '2px dashed #10b981' }}></span>{t('data.common.metric.closedConversations')}</span>
        <span className="flex items-center gap-1.5"><span className="w-3 h-0.5 bg-amber-500" style={{ borderTop: '2px dashed #f59e0b' }}></span>{t('data.common.metric.agentReplies')}</span>
      </div>
    </div>
  );
}

// —— 状态分布饼图 ——
const STATUS_COLORS: Record<number, string> = { 1: '#10b981', 2: '#f59e0b', 3: '#6b7280', 4: '#ef4444' };

/**
 * 会话状态码 → 文案 key。
 *
 * 后端 `NoneadCSS.Application/Services/ReportService.cs:209` 用 `GetStatusName`
 * （同文件 `:440-447`）把**中文标签**塞进 `ReportStatusDistributionDto.StatusName` 下发，
 * 英文界面下会直出中文，故这里不再渲染 `statusName`，改按稳定数字码查前端字典。
 *
 * 码表来源 `NoneadCSS.Domain/Enums/ConversationStatus.cs`：`Active = 1`、`Pending = 2`、
 * `Closed = 3`、`Spam = 4`。数据侧 `ConversationRepository.GetStatusDistributionAsync`
 * 按 `c.Status` 分组，可达值即这 4 个（后端 `_ => status.ToString()` 分支在枚举上不可达）。
 */
const STATUS_KEYS_BY_CODE: Record<string, TranslationKey | undefined> = {
  '1': 'format.status.active',
  '2': 'format.status.pending',
  '3': 'format.status.closed',
  '4': 'format.status.spam',
};

function StatusPie({ data }: { data: ReportStatusDistributionDto[] }) {
  const { t } = useI18n();
  /** 状态标签一律走字典；未知码显式回落 `format.unknown`，**绝不**渲染后端中文标签 */
  const statusLabel = (code: number) => {
    const key = STATUS_KEYS_BY_CODE[String(code)];
    return key ? t(key) : t('format.unknown');
  };
  const total = data.reduce((sum, s) => sum + (s.count ?? 0), 0);
  const radius = 70;
  const cx = 90;
  const cy = 90;
  const strokeWidth = 28;
  if (total === 0) return <div className="flex items-center justify-center h-48 text-sm text-slate-400">{t('common.empty')}</div>;
  let cumulative = 0;
  const segments = data.map((slice) => {
    const percent = total > 0 ? (slice.count ?? 0) / total : 0;
    const startAngle = cumulative * 2 * Math.PI - Math.PI / 2;
    cumulative += percent;
    const endAngle = cumulative * 2 * Math.PI - Math.PI / 2;
    const largeArc = percent > 0.5 ? 1 : 0;
    const x1 = cx + radius * Math.cos(startAngle);
    const y1 = cy + radius * Math.sin(startAngle);
    const x2 = cx + radius * Math.cos(endAngle);
    const y2 = cy + radius * Math.sin(endAngle);
    return { ...slice, percent, path: `M ${x1} ${y1} A ${radius} ${radius} 0 ${largeArc} 1 ${x2} ${y2}` };
  });
  return (
    <div className="flex items-center gap-4 flex-wrap">
      <svg width="180" height="180" viewBox="0 0 180 180" className="flex-shrink-0">
        <circle cx={cx} cy={cy} r={radius} fill="none" className="stroke-slate-200 dark:stroke-slate-700" strokeWidth={strokeWidth} />
        {segments.map((seg, i) => {
          if (seg.percent === 0) return null;
          if (seg.percent === 1) {
            return <circle key={i} cx={cx} cy={cy} r={radius} fill="none" stroke={STATUS_COLORS[seg.status] ?? '#9ca3af'} strokeWidth={strokeWidth} />;
          }
          return <path key={i} d={seg.path} fill="none" stroke={STATUS_COLORS[seg.status] ?? '#9ca3af'} strokeWidth={strokeWidth} strokeLinecap="butt" />;
        })}
        <text x={cx} y={cy - 4} textAnchor="middle" className="text-xl font-bold fill-slate-900 dark:fill-slate-100">{total}</text>
        <text x={cx} y={cy + 14} textAnchor="middle" className="text-[10px] fill-slate-500 dark:fill-slate-400">{t('data.customReports.view.chart.statusTotal')}</text>
      </svg>
      <div className="flex-1 min-w-[180px] space-y-2">
        {data.map((s) => (
          <div key={s.status} className="flex items-center gap-2 text-sm">
            <span className="w-3 h-3 rounded-sm flex-shrink-0" style={{ backgroundColor: STATUS_COLORS[s.status] ?? '#9ca3af' }} />
            <span className="text-slate-700 dark:text-slate-300 flex-1">{statusLabel(s.status)}</span>
            <span className="font-medium text-slate-900 dark:text-slate-100">{s.count}</span>
            <span className="text-xs text-slate-500 dark:text-slate-400 w-12 text-right">{(s.percentage ?? 0).toFixed(1)}%</span>
          </div>
        ))}
      </div>
    </div>
  );
}

// —— 横向柱状图（邮箱负载、客户域名通用）——
function HorizontalBars({
  data,
}: {
  data: { label: string; sublabel?: string; value: number; percentage: number; color?: string }[];
}) {
  const { t } = useI18n();
  if (!data || data.length === 0) return <div className="flex items-center justify-center h-32 text-sm text-slate-400">{t('common.empty')}</div>;
  const maxValue = Math.max(...data.map((d) => d.value), 1);
  return (
    <div className="space-y-2.5">
      {data.map((d, i) => (
        <div key={i}>
          <div className="flex items-center justify-between text-xs mb-1">
            <div className="flex items-center gap-1.5 min-w-0">
              <span className="text-slate-700 dark:text-slate-300 truncate">{d.label}</span>
              {d.sublabel && <span className="text-slate-400 dark:text-slate-500 truncate">· {d.sublabel}</span>}
            </div>
            <div className="flex items-center gap-2 flex-shrink-0">
              <span className="font-semibold text-slate-900 dark:text-slate-100">{d.value}</span>
              <span className="text-slate-500 dark:text-slate-400 w-12 text-right">{(d.percentage ?? 0).toFixed(1)}%</span>
            </div>
          </div>
          <div className="h-2 bg-slate-100 dark:bg-slate-700 rounded-full overflow-hidden">
            <div className="h-full rounded-full transition-all" style={{ width: `${(d.value / maxValue) * 100}%`, backgroundColor: d.color ?? '#3b82f6' }} />
          </div>
        </div>
      ))}
    </div>
  );
}

/** 可见性 code（后端 `CustomReportDto.visibility`，稳定 code）→ 文案 key */
const VISIBILITY_INFO: Record<string, { labelKey: TranslationKey; icon: string; color: string }> = {
  private: { labelKey: 'data.customReports.view.visibility.private', icon: '🔒', color: 'bg-slate-100 text-slate-700 dark:bg-slate-700 dark:text-slate-300' },
  team: { labelKey: 'data.customReports.view.visibility.team', icon: '👥', color: 'bg-indigo-100 text-indigo-700 dark:bg-indigo-900/40 dark:text-indigo-400' },
  public: { labelKey: 'data.customReports.view.visibility.public', icon: '🌐', color: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-400' },
};

/**
 * 分类 code → 文案 key。
 *
 * 取值清单来自后端白名单 `CustomReportService.ValidCategories`
 * （`NoneadCSS.Application/Services/CustomReportService.cs:34-37`）：4 个值，无缺无余。
 * 但 DTO 侧 `category` 的类型是 `string`（历史数据/直接改库仍可能带白名单外的值），
 * 故用 `TranslationKey | undefined` 而非 `Record<string, TranslationKey>`——
 * 后者会让"未知 code 原样显示"这条回落分支在类型上不可达，而运行期仍会走到。
 */
const CATEGORY_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  performance: 'data.customReports.view.category.performance',
  efficiency: 'data.customReports.view.category.efficiency',
  customer: 'data.customReports.view.category.customer',
  workload: 'data.customReports.view.category.workload',
};

export default function CustomReportView() {
  const { id } = useParams();
  const navigate = useNavigate();
  const toast = useToast();
  const { t } = useI18n();
  const reportId = id ? Number(id) : NaN;

  const [report, setReport] = useState<CustomReportDto | null>(null);
  const [result, setResult] = useState<CustomReportExecutionResultDto | null>(null);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [loadingReport, setLoadingReport] = useState(true);
  const [loadingExec, setLoadingExec] = useState(false);
  const [refreshLock, setRefreshLock] = useState(false);
  const isRefreshing = loadingExec || refreshLock;
  const [error, setError] = useState('');
  const [togglingPin, setTogglingPin] = useState(false);
  const [exportMenuOpen, setExportMenuOpen] = useState(false);
  const [exporting, setExporting] = useState(false);

  const [scheduleModal, setScheduleModal] = useState<{ open: boolean; reportId: number | null }>({
    open: false,
    reportId: null,
  });

  useEffect(() => {
    mailboxApi.listAll().then((r) => setMailboxes(Array.isArray(r) ? r : [])).catch(() => {});
  }, []);

  useEffect(() => {
    const onClick = () => setExportMenuOpen(false);
    document.addEventListener('click', onClick);
    return () => document.removeEventListener('click', onClick);
  }, []);

  const loadReport = useCallback(async () => {
    if (isNaN(reportId)) return;
    setLoadingReport(true);
    try {
      const r = await customReportApi.getById(reportId);
      setReport(r);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.customReports.view.toast.loadReportFailed');
      setError(msg);
      toast.error(msg);
    } finally {
      setLoadingReport(false);
    }
  }, [reportId, toast, t]);

  const executeReport = useCallback(async () => {
    if (isNaN(reportId)) return;
    setLoadingExec(true);
    setError('');
    try {
      const r = await customReportApi.execute(reportId);
      setResult(r);
      toast.success(t('data.customReports.view.toast.reportRefreshed'));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.customReports.view.toast.executeReportFailed');
      setError(msg);
      toast.error(msg);
    } finally {
      setLoadingExec(false);
    }
  }, [reportId, toast, t]);

  const handleRefresh = useCallback(() => {
    if (isRefreshing) return;
    setRefreshLock(true);
    try {
      void executeReport();
    } finally {
      setTimeout(() => setRefreshLock(false), 400);
    }
  }, [isRefreshing, executeReport]);

  useEffect(() => {
    loadReport();
  }, [loadReport]);

  useEffect(() => {
    if (report) executeReport();
  }, [report, executeReport]);

  const handleTogglePin = async () => {
    if (!report) return;
    setTogglingPin(true);
    try {
      const updated = await customReportApi.togglePin(report.id, {
        pinned: !report.isDashboardPinned,
        sortOrder: report.isDashboardPinned ? null : 0,
      });
      setReport({ ...report, isDashboardPinned: updated.isDashboardPinned });
      toast.success(updated.isDashboardPinned ? t('data.customReports.view.toast.pinned') : t('data.customReports.view.toast.unpinned'));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.customReports.view.toast.operationFailed');
      toast.error(msg);
    } finally {
      setTogglingPin(false);
    }
  };

  const handleExport = async (format: 'csv' | 'xlsx' | 'pdf') => {
    if (!report || exporting) return;
    setExporting(true);
    setExportMenuOpen(false);
    try {
      const blob = await customReportApi.exportFile(report.id, format);
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `custom_report_${report.id}_${new Date().toISOString().slice(0, 10)}.${format}`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);
      toast.success(t('data.common.exportSucceeded'));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.common.exportFailed');
      toast.error(msg);
    } finally {
      setExporting(false);
    }
  };

  const meta = result?.reportMeta;
  const sections = useMemo(() => result?.sections ?? [], [result]);

  const summarySection = useMemo(() => sections.find((s) => s.key === 'summary'), [sections]);
  const trendSection = useMemo(() => sections.find((s) => s.key === 'trend'), [sections]);
  const statusSection = useMemo(() => sections.find((s) => s.key === 'statusDistribution'), [sections]);
  const userPerfSection = useMemo(() => sections.find((s) => s.key === 'userPerformance'), [sections]);
  const mailboxLoadSection = useMemo(() => sections.find((s) => s.key === 'mailboxLoad'), [sections]);
  const customerDomainsSection = useMemo(() => sections.find((s) => s.key === 'customerDomains'), [sections]);

  const summary = summarySection?.data as Record<string, number | undefined> | undefined;
  const avgResolution = summary?.averageResolutionHours ?? summary?.avgResolutionHours;

  const mailboxName = useMemo(() => {
    if (!meta?.mailboxId) return null;
    return mailboxes.find((m) => m.id === meta.mailboxId)?.name ?? `#${meta.mailboxId}`;
  }, [meta, mailboxes]);

  const vis = report ? VISIBILITY_INFO[report.visibility] ?? VISIBILITY_INFO.private : null;
  /** 分类标签：白名单内查字典；白名单外（历史数据）原样显示，与改造前 `|| report.category` 一致 */
  const categoryKey = report ? CATEGORY_LABEL_KEYS[report.category] : undefined;
  const categoryLabel = categoryKey ? t(categoryKey) : report?.category ?? null;

  return (
    <div className="space-y-5">
      {/* 页头 */}
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="space-y-2">
          <div className="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
            <Link to="/modules/custom-reports" className="inline-flex items-center gap-1 hover:text-indigo-600 dark:hover:text-indigo-400 transition-colors">
              <ArrowLeft size={12} />
              {t('data.customReports.view.title')}
            </Link>
            <span>/</span>
            <span>{t('data.customReports.view.breadcrumbView')}</span>
          </div>
          <div className="flex items-start gap-3">
            <div className="w-12 h-12 rounded-xl bg-gradient-to-br from-indigo-100 to-violet-100 dark:from-indigo-900/40 dark:to-violet-900/40 flex items-center justify-center flex-shrink-0">
              <BarChart3 className="w-6 h-6 text-indigo-600 dark:text-indigo-400" />
            </div>
            <div className="min-w-0">
              <div className="flex items-center gap-2 flex-wrap">
                <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100 truncate">
                  {loadingReport ? t('common.shell.loading') : report?.name ?? t('data.customReports.view.reportNotFound')}
                </h1>
                {vis && (
                  <span className={`inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full font-medium ${vis.color}`}>
                    <span>{vis.icon}</span>
                    {t(vis.labelKey)}
                  </span>
                )}
                {categoryLabel && (
                  <span className="text-xs px-2 py-0.5 rounded-full bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400 font-medium">
                    {categoryLabel}
                  </span>
                )}
              </div>
              <div className="flex items-center gap-3 flex-wrap text-xs text-slate-500 dark:text-slate-400 mt-1.5">
                <span>Owner: {report?.ownerName ?? '-'}</span>
                {report?.teamName && <span>{t('data.customReports.view.teamLabel')} {report.teamName}</span>}
                <span>{t('data.customReports.view.updatedAtLabel')} {report?.updatedAt ? formatDateTime(report.updatedAt) : '-'}</span>
              </div>
              {meta && (
                <div className="flex items-center gap-3 flex-wrap text-xs mt-2 px-3 py-1.5 rounded-lg bg-indigo-50/60 dark:bg-indigo-900/20 text-indigo-700 dark:text-indigo-300 inline-flex">
                  <span>📅 {formatDate(meta.startDate)} ~ {formatDate(meta.endDate)}</span>
                  {mailboxName && <><span>·</span><span className="inline-flex items-center gap-1"><Mail size={11} />{mailboxName}</span></>}
                  {meta.groupBy && meta.groupBy !== 'none' && <><span>·</span><span>{t('data.customReports.view.groupByLabel')}{meta.groupBy === 'week' ? t('data.customReports.view.groupBy.week') : t('data.customReports.view.groupBy.month')}</span></>}
                </div>
              )}
              {report?.description && (
                <p className="text-sm text-slate-600 dark:text-slate-400 mt-2 max-w-2xl">{report.description}</p>
              )}
            </div>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <button
            onClick={() => navigate('/modules/custom-reports')}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-700 rounded-xl transition-all shadow-sm"
          >
            <ArrowLeft size={15} />
            {t('data.customReports.view.backToList')}
          </button>
          <button
            onClick={handleRefresh}
            disabled={isRefreshing || isNaN(reportId)}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-700 rounded-xl shadow-sm disabled:opacity-60 disabled:cursor-not-allowed active:scale-[0.98] transition-all duration-200"
          >
            <RefreshCw size={15} className={isRefreshing ? 'animate-spin' : ''} />
            {t('data.customReports.view.refreshData')}
          </button>
          <div className="relative" onClick={(e) => e.stopPropagation()}>
            <button
              onClick={() => setExportMenuOpen(!exportMenuOpen)}
              disabled={exporting}
              className="inline-flex items-center gap-1.5 px-3.5 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-700 rounded-xl transition-all shadow-sm disabled:opacity-50"
            >
              {exporting ? <RefreshCw size={15} className="animate-spin" /> : <Download size={15} />}
              {exporting ? t('data.common.exporting') : t('data.common.export')}
            </button>
            {exportMenuOpen && (
              <div className="absolute right-0 top-full mt-1 w-44 bg-white dark:bg-slate-800 rounded-xl shadow-lg border border-slate-100 dark:border-slate-700 py-1.5 z-20">
                <button onClick={() => { setExportMenuOpen(false); handleExport('csv'); }} className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700/60 transition-colors">
                  <FileText size={15} className="text-emerald-500" /> {t('data.customReports.view.exportFormat.csv')}
                </button>
                <button onClick={() => { setExportMenuOpen(false); handleExport('xlsx'); }} className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700/60 transition-colors">
                  <FileSpreadsheet size={15} className="text-blue-500" /> {t('data.customReports.view.exportFormat.excel')}
                </button>
                <button onClick={() => { setExportMenuOpen(false); handleExport('pdf'); }} className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700/60 transition-colors">
                  <File size={15} className="text-rose-500" /> {t('data.customReports.view.exportFormat.pdf')}
                </button>
              </div>
            )}
          </div>
          <button
            onClick={handleTogglePin}
            disabled={togglingPin || !report}
            title={report?.isDashboardPinned ? t('data.customReports.view.pinTooltip') : t('data.customReports.view.pinToDashboard')}
            className={`inline-flex items-center gap-1.5 px-3.5 py-2 text-sm font-medium rounded-xl transition-all shadow-sm disabled:opacity-50 ${
              report?.isDashboardPinned
                ? 'text-amber-700 bg-amber-50 border border-amber-200 dark:text-amber-400 dark:bg-amber-900/30 dark:border-amber-800/50 hover:bg-amber-100 dark:hover:bg-amber-900/50'
                : 'text-slate-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-700'
            }`}
          >
            {report?.isDashboardPinned ? <Pin size={15} /> : <PinOff size={15} />}
            {report?.isDashboardPinned ? t('data.customReports.view.pinned') : t('data.customReports.view.pin')}
          </button>
          {report?.canEdit && (
            <button
              onClick={() => navigate(`/modules/custom-reports/edit/${report.id}`)}
              className="inline-flex items-center gap-1.5 px-3.5 py-2 text-sm font-medium text-white bg-gradient-to-r from-indigo-600 to-violet-600 hover:from-indigo-700 hover:to-violet-700 rounded-xl transition-all shadow-sm"
            >
              <Edit2 size={15} />
              {t('common.edit')}
            </button>
          )}
          <button
            onClick={() => setScheduleModal({ open: true, reportId: isNaN(reportId) ? null : reportId })}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-700 rounded-xl transition-all shadow-sm"
          >
            <Calendar size={15} />
            {t('data.customReports.view.schedule')}
          </button>
        </div>
      </div>

      {error && (
        <div className="bg-rose-50 dark:bg-rose-900/30 border border-rose-200 dark:border-rose-800 text-rose-700 dark:text-rose-400 px-4 py-3 rounded-lg text-sm">
          {error}
        </div>
      )}

      {/* 主体内容 */}
      {loadingReport || (loadingExec && !result) ? (
        <div className="p-16 text-center bg-white dark:bg-slate-800 rounded-2xl border border-slate-100 dark:border-slate-700">
          <div className="flex items-center justify-center gap-2 text-slate-500 dark:text-slate-400">
            <RefreshCw className="w-5 h-5 animate-spin" />
            <span>{loadingReport ? t('data.customReports.view.loadingReport') : t('data.customReports.view.executingReport')}</span>
          </div>
        </div>
      ) : !report ? (
        <div className="p-16 text-center bg-white dark:bg-slate-800 rounded-2xl border border-slate-100 dark:border-slate-700">
          <h3 className="text-lg font-medium text-slate-700 dark:text-slate-300 mb-2">{t('data.customReports.view.reportNotFound')}</h3>
          <p className="text-sm text-slate-500 mb-4">{t('data.customReports.view.reportUnavailable')}</p>
          <button
            onClick={() => navigate('/modules/custom-reports')}
            className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg"
          >
            {t('data.customReports.view.backToList')}
          </button>
        </div>
      ) : !result ? (
        <div className="p-16 text-center bg-white dark:bg-slate-800 rounded-2xl border border-slate-100 dark:border-slate-700">
          <BarChart3 className="w-12 h-12 mx-auto mb-4 text-slate-300 dark:text-slate-600" />
          <h3 className="text-lg font-medium text-slate-700 dark:text-slate-300 mb-1">{t('data.customReports.view.notExecuted')}</h3>
          <p className="text-sm text-slate-500 mb-4">{t('data.customReports.view.notExecutedHint')}</p>
          <button
            onClick={executeReport}
            disabled={loadingExec}
            className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg disabled:opacity-60 disabled:cursor-not-allowed"
          >
            <RefreshCw size={15} className={loadingExec ? 'animate-spin' : ''} />
            {t('data.customReports.view.executeNow')}
          </button>
        </div>
      ) : (
        <div className="space-y-5">
          {/* Summary KPI 卡片 */}
          {summary && (
            <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-4">
              <KpiCard title={t('data.common.metric.newConversations')} value={summary.newConversations ?? summary.newTickets ?? '-'} icon={<Plus size={18} className="text-blue-600 dark:text-blue-400" />} color="bg-blue-50 dark:bg-blue-900/30" />
              <KpiCard title={t('data.common.metric.closedConversations')} value={summary.closedConversations ?? summary.closedTickets ?? '-'} icon={<Check size={18} className="text-emerald-600 dark:text-emerald-400" />} color="bg-emerald-50 dark:bg-emerald-900/30" />
              <KpiCard title={t('data.common.metric.agentReplies')} value={summary.totalReplies ?? summary.replyCount ?? '-'} icon={<MessageSquare size={18} className="text-amber-600 dark:text-amber-400" />} color="bg-amber-50 dark:bg-amber-900/30" />
              <KpiCard title={t('data.common.metric.newCustomers')} value={summary.newCustomers ?? summary.newCustomerCount ?? '-'} icon={<Users size={18} className="text-purple-600 dark:text-purple-400" />} color="bg-purple-50 dark:bg-purple-900/30" />
              <KpiCard
                title={t('data.customReports.view.kpi.averageFirstResponse')}
                value={summary.averageFirstResponseHours != null ? `${summary.averageFirstResponseHours}h` : '-'}
                subtitle={t('data.customReports.view.kpi.averageFirstResponseHint')}
                icon={<Clock size={18} className="text-cyan-600 dark:text-cyan-400" />}
                color="bg-cyan-50 dark:bg-cyan-900/30"
              />
              <KpiCard
                title={t('data.customReports.view.kpi.slaAchievement')}
                value={summary.slaAchievementRate != null ? `${summary.slaAchievementRate}%` : '-'}
                subtitle={t('data.customReports.view.kpi.slaTarget')}
                icon={<Shield size={18} className="text-rose-600 dark:text-rose-400" />}
                color="bg-rose-50 dark:bg-rose-900/30"
              />
            </div>
          )}

          {/* Trend */}
          {trendSection && trendSection.data && (
            <div className="bg-white dark:bg-slate-800 rounded-xl border border-slate-100 dark:border-slate-700/50 p-5 shadow-sm">
              <div className="flex items-center gap-3 mb-4">
                <div className="p-2 bg-blue-50 dark:bg-blue-900/30 rounded-lg">
                  <TrendingUp className="w-5 h-5 text-blue-600 dark:text-blue-400" />
                </div>
                <div>
                  <h3 className="font-semibold text-slate-900 dark:text-slate-100">{t('data.customReports.view.chart.trend')}</h3>
                  <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">{t('data.customReports.view.chart.trendSubtitle')}</p>
                </div>
              </div>
              <TrendChart data={trendSection.data as ReportTrendPointDto[]} />
            </div>
          )}

          {/* Status Distribution + Mailbox Load */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
            {statusSection && statusSection.data && (
              <div className="bg-white dark:bg-slate-800 rounded-xl border border-slate-100 dark:border-slate-700/50 p-5 shadow-sm">
                <div className="flex items-center gap-3 mb-4">
                  <div className="p-2 bg-emerald-50 dark:bg-emerald-900/30 rounded-lg">
                    <PieChart className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
                  </div>
                  <h3 className="font-semibold text-slate-900 dark:text-slate-100">{t('data.customReports.view.chart.statusDistribution')}</h3>
                </div>
                <StatusPie data={statusSection.data as ReportStatusDistributionDto[]} />
              </div>
            )}

            {mailboxLoadSection && mailboxLoadSection.data && (
              <div className="bg-white dark:bg-slate-800 rounded-xl border border-slate-100 dark:border-slate-700/50 p-5 shadow-sm">
                <div className="flex items-center gap-3 mb-4">
                  <div className="p-2 bg-indigo-50 dark:bg-indigo-900/30 rounded-lg">
                    <Server className="w-5 h-5 text-indigo-600 dark:text-indigo-400" />
                  </div>
                  <h3 className="font-semibold text-slate-900 dark:text-slate-100">{t('data.customReports.view.chart.mailboxLoad')}</h3>
                </div>
                <HorizontalBars
                  data={(mailboxLoadSection.data as ReportMailboxLoadDto[]).map((m) => ({
                    label: m.mailboxName ?? `#${m.mailboxId}`,
                    sublabel: m.email,
                    value: m.newCount ?? m.activeCount ?? 0,
                    percentage: m.percentage ?? 0,
                    color: '#3b82f6',
                  }))}
                />
              </div>
            )}
          </div>

          {/* User Performance + Customer Domains */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
            {userPerfSection && userPerfSection.data && (
              <div className="bg-white dark:bg-slate-800 rounded-xl border border-slate-100 dark:border-slate-700/50 p-5 shadow-sm">
                <div className="flex items-center gap-3 mb-4">
                  <div className="p-2 bg-purple-50 dark:bg-purple-900/30 rounded-lg">
                    <Users className="w-5 h-5 text-purple-600 dark:text-purple-400" />
                  </div>
                  <h3 className="font-semibold text-slate-900 dark:text-slate-100">{t('data.customReports.view.chart.userPerformance')}</h3>
                </div>
                {(userPerfSection.data as ReportUserPerformanceDto[]).length === 0 ? (
                  <div className="flex items-center justify-center h-32 text-sm text-slate-400">{t('common.empty')}</div>
                ) : (
                  <div className="overflow-x-auto -mx-2">
                    <table className="w-full text-sm">
                      <thead>
                        <tr className="text-left text-xs text-slate-500 dark:text-slate-400 border-b border-slate-100 dark:border-slate-700">
                          <th className="py-2 pr-3 pl-2">{t('data.customReports.view.table.agent')}</th>
                          <th className="py-2 px-3 text-right">{t('data.customReports.view.table.assigned')}</th>
                          <th className="py-2 px-3 text-right">{t('data.customReports.view.table.replied')}</th>
                          <th className="py-2 px-3 text-right">{t('data.customReports.view.table.closed')}</th>
                          <th className="py-2 pl-3 pr-2 text-right">{t('data.customReports.view.table.responseHours')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {(userPerfSection.data as ReportUserPerformanceDto[]).map((u) => (
                          <tr key={u.userId} className="border-b border-slate-50 dark:border-slate-700/50 last:border-0 hover:bg-slate-50/50 dark:hover:bg-slate-700/30 transition-colors">
                            <td className="py-2.5 pr-3 pl-2">
                              <div className="font-medium text-slate-900 dark:text-slate-100">{u.userName || '-'}</div>
                              <div className="text-xs text-slate-400 dark:text-slate-500 truncate max-w-[180px]">{u.email}</div>
                            </td>
                            <td className="py-2.5 px-3 text-right text-slate-700 dark:text-slate-300">{u.assignedCount ?? 0}</td>
                            <td className="py-2.5 px-3 text-right font-medium text-blue-600 dark:text-blue-400">{u.replyCount ?? 0}</td>
                            <td className="py-2.5 px-3 text-right font-medium text-emerald-600 dark:text-emerald-400">{u.closedCount ?? 0}</td>
                            <td className="py-2.5 pl-3 pr-2 text-right text-slate-700 dark:text-slate-300">{u.averageFirstResponseHours ?? '-'}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
            )}

            {customerDomainsSection && customerDomainsSection.data && (
              <div className="bg-white dark:bg-slate-800 rounded-xl border border-slate-100 dark:border-slate-700/50 p-5 shadow-sm">
                <div className="flex items-center gap-3 mb-4">
                  <div className="p-2 bg-amber-50 dark:bg-amber-900/30 rounded-lg">
                    <Globe className="w-5 h-5 text-amber-600 dark:text-amber-400" />
                  </div>
                  <h3 className="font-semibold text-slate-900 dark:text-slate-100">{t('data.customReports.view.chart.customerDomains')}</h3>
                </div>
                <HorizontalBars
                  data={(customerDomainsSection.data as ReportCustomerDomainDto[]).map((d) => ({
                    label: d.domain,
                    sublabel: t('data.customReports.view.customerCount', { count: d.uniqueCustomerCount ?? 0 }),
                    value: d.conversationCount ?? 0,
                    percentage: d.percentage ?? 0,
                    color: '#8b5cf6',
                  }))}
                />
              </div>
            )}
          </div>

          {/* 平均解决时长独立卡片 */}
          {avgResolution != null && (
            <div className="bg-white dark:bg-slate-800 rounded-xl border border-slate-100 dark:border-slate-700/50 p-5 shadow-sm">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div className="p-2 bg-rose-50 dark:bg-rose-900/30 rounded-lg">
                    <Timer className="w-5 h-5 text-rose-600 dark:text-rose-400" />
                  </div>
                  <div>
                    <h3 className="font-semibold text-slate-900 dark:text-slate-100">{t('data.customReports.view.avgResolution.title')}</h3>
                    <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">{t('data.customReports.view.avgResolution.subtitle')}</p>
                  </div>
                </div>
                <div className="text-right">
                  <p className="text-3xl font-bold text-slate-900 dark:text-slate-100">
                    {avgResolution}
                    <span className="text-base font-normal text-slate-500 dark:text-slate-400 ml-1">{t('data.customReports.view.avgResolution.unitHours')}</span>
                  </p>
                  <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                    {t('data.customReports.view.avgResolution.daysApprox', { days: (Number(avgResolution) / 24).toFixed(1) })}
                  </p>
                </div>
              </div>
            </div>
          )}

          {/* 未匹配到内置组件的通用 sections 兜底：表格化显示 JSON 数据 */}
          {sections
            .filter((s) => !['summary', 'trend', 'statusDistribution', 'userPerformance', 'mailboxLoad', 'customerDomains'].includes(s.key))
            .map((s, idx) => (
              <GenericSection key={`${s.key}-${idx}`} section={s} />
            ))}
        </div>
      )}

      <CustomReportScheduleModal
        isOpen={scheduleModal.open}
        onClose={() => setScheduleModal({ open: false, reportId: null })}
        reportId={scheduleModal.reportId}
      />
    </div>
  );
}

function GenericSection({ section }: { section: SectionResultDto }) {
  const { t } = useI18n();
  const data = section.data;
  const renderContent = () => {
    if (Array.isArray(data)) {
      if (data.length === 0) return <div className="p-6 text-center text-slate-400 text-sm">{t('common.empty')}</div>;
      const first = data[0];
      const keys = typeof first === 'object' && first ? Object.keys(first) : [];
      if (keys.length === 0) {
        return (
          <div className="p-4 text-sm text-slate-600 dark:text-slate-300">
            {data.map((v, i) => (
              <div key={`val-${i}-${typeof v === 'object' ? JSON.stringify(v) : String(v)}`.slice(0, 100)} className="py-1 border-b border-slate-100 dark:border-slate-700 last:border-0">
                {typeof v === 'object' ? JSON.stringify(v) : String(v)}
              </div>
            ))}
          </div>
        );
      }
      return (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-slate-500 dark:text-slate-400 border-b border-slate-100 dark:border-slate-700">
                {keys.map((k) => (
                  <th key={k} className="py-2 px-3 font-medium capitalize">{k}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {data.map((row, i) => (
                <tr key={`row-${i}-${keys.map((k) => (row as Record<string, unknown>)?.[k] ?? '').join('-')}`.slice(0, 200)} className="border-b border-slate-50 dark:border-slate-700/50 last:border-0 hover:bg-slate-50/50 dark:hover:bg-slate-700/30">
                  {keys.map((k) => {
                    const v = (row as Record<string, unknown>)?.[k];
                    const str = v == null ? '-' : typeof v === 'object' ? JSON.stringify(v) : String(v);
                    return <td key={k} className="py-2 px-3 text-slate-700 dark:text-slate-300">{str}</td>;
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      );
    }
    if (data && typeof data === 'object') {
      const entries = Object.entries(data);
      if (entries.length === 0) return <div className="p-6 text-center text-slate-400 text-sm">{t('common.empty')}</div>;
      return (
        <div className="divide-y divide-slate-100 dark:divide-slate-700">
          {entries.map(([k, v]) => (
            <div key={k} className="grid grid-cols-3 gap-4 py-2.5 text-sm">
              <div className="col-span-1 text-slate-500 dark:text-slate-400 capitalize">{k}</div>
              <div className="col-span-2 text-slate-800 dark:text-slate-200 font-mono text-xs">
                {v == null ? '-' : typeof v === 'object' ? JSON.stringify(v) : String(v)}
              </div>
            </div>
          ))}
        </div>
      );
    }
    return (
      <div className="p-4 text-sm text-slate-700 dark:text-slate-300 font-mono whitespace-pre-wrap break-all">
        {data == null ? '-' : String(data)}
      </div>
    );
  };
  return (
    <div className="bg-white dark:bg-slate-800 rounded-xl border border-slate-100 dark:border-slate-700/50 p-5 shadow-sm">
      <div className="flex items-center gap-3 mb-4">
        <div className="p-2 bg-slate-100 dark:bg-slate-700/50 rounded-lg">
          <BarChart3 className="w-5 h-5 text-slate-600 dark:text-slate-400" />
        </div>
        <div>
          <h3 className="font-semibold text-slate-900 dark:text-slate-100 capitalize">
            {section.key}
            {section.chartType && <span className="ml-2 text-xs font-normal text-slate-500 dark:text-slate-400">({section.chartType})</span>}
          </h3>
        </div>
      </div>
      {renderContent()}
    </div>
  );
}
