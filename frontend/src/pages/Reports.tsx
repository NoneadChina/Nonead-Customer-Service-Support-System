import { useEffect, useState, useMemo, useCallback } from 'react';
import { reportApi, mailboxApi } from '../api';
import type {
  ReportResponse,
  MailboxResponse,
  ReportTrendPointDto,
  ReportStatusDistributionDto,
} from '../api/types';
import { formatDate } from '../utils/format';
import { useI18n } from '../context/I18nContext';
import { toIntlLocale } from '../i18n/locale';
import type { TranslationKey } from '../locales/zh-Hans';
import {
  BarChart3,
  Download,
  RefreshCw,
  Plus,
  Check,
  MessageSquare,
  Users,
  Clock,
  Shield,
  Mail,
  TrendingUp,
  PieChart,
  Server,
  Globe,
  Timer,
} from 'lucide-react';

// ====== 时间范围快捷选项 ======
type RangeKey = 'today' | 'week' | 'month' | '30d' | '90d' | 'all';

interface RangeOption {
  key: RangeKey;
  /** 词条 key（不是文案）：渲染时经 `t()` 取词，漏包 t() 会被 tsc 抓出来 */
  labelKey: TranslationKey;
  compute: () => { start: string; end: string };
}

const rangeOptions: RangeOption[] = [
  {
    key: 'today',
    labelKey: 'format.dateRange.today',
    compute: () => {
      const today = new Date();
      const iso = today.toISOString().slice(0, 10);
      return { start: iso, end: iso };
    },
  },
  {
    key: 'week',
    labelKey: 'format.dateRange.thisWeek',
    compute: () => {
      const today = new Date();
      const day = today.getDay() || 7; // 周日=0 -> 7
      const monday = new Date(today);
      monday.setDate(today.getDate() - day + 1);
      const iso = (d: Date) => d.toISOString().slice(0, 10);
      return { start: iso(monday), end: iso(today) };
    },
  },
  {
    key: 'month',
    labelKey: 'format.dateRange.thisMonth',
    compute: () => {
      const today = new Date();
      const first = new Date(today.getFullYear(), today.getMonth(), 1);
      const iso = (d: Date) => d.toISOString().slice(0, 10);
      return { start: iso(first), end: iso(today) };
    },
  },
  {
    key: '30d',
    labelKey: 'format.dateRange.last30Days',
    compute: () => {
      const today = new Date();
      const start = new Date(today);
      start.setDate(today.getDate() - 29);
      const iso = (d: Date) => d.toISOString().slice(0, 10);
      return { start: iso(start), end: iso(today) };
    },
  },
  {
    key: '90d',
    labelKey: 'format.dateRange.last90Days',
    compute: () => {
      const today = new Date();
      const start = new Date(today);
      start.setDate(today.getDate() - 89);
      const iso = (d: Date) => d.toISOString().slice(0, 10);
      return { start: iso(start), end: iso(today) };
    },
  },
  {
    key: 'all',
    labelKey: 'data.common.all',
    compute: () => {
      const today = new Date();
      const start = new Date(2020, 0, 1);
      const iso = (d: Date) => d.toISOString().slice(0, 10);
      return { start: iso(start), end: iso(today) };
    },
  },
];

// ====== 核心指标卡片 ======
interface KpiCardProps {
  title: string;
  value: string | number;
  subtitle?: string;
  icon: React.ReactNode;
  color: string;
  trend?: 'up' | 'down' | 'neutral';
  trendValue?: string;
}

function KpiCard({ title, value, subtitle, icon, color, trend, trendValue }: KpiCardProps) {
  return (
    <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 shadow-sm hover:shadow-md transition-all group">
      <div className="flex items-start justify-between gap-2 sm:gap-3">
        <div className="min-w-0 flex-1">
          <p className="text-[11px] sm:text-xs text-gray-500 dark:text-slate-400 uppercase tracking-wide">{title}</p>
          <p className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100 mt-1.5 truncate">{value}</p>
          {subtitle && <p className="text-[11px] sm:text-xs text-gray-500 dark:text-slate-400 mt-1 line-clamp-1">{subtitle}</p>}
          {trend && trendValue && (
            <div className={`flex items-center gap-1 mt-1.5 text-xs ${
              trend === 'up' ? 'text-green-500' : trend === 'down' ? 'text-red-500' : 'text-gray-400'
            }`}>
              <TrendingUp className={`w-3 h-3 ${trend === 'down' ? 'rotate-180' : ''}`} />
              {trendValue}
            </div>
          )}
        </div>
        <div className={`w-9 h-9 sm:w-11 sm:h-11 rounded-xl flex items-center justify-center flex-shrink-0 ${color} transition-transform group-hover:scale-110`}>
          {icon}
        </div>
      </div>
    </div>
  );
}

// ====== 趋势折线图（多线） ======
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
    return <div className="flex items-center justify-center h-48 text-sm text-gray-400 dark:text-slate-400">{t('data.common.noTrendData')}</div>;
  }

  const maxCount = Math.max(...data.flatMap((d) => [d.newCount, d.closedCount, d.replyCount]), 1);
  const stepX = data.length > 1 ? chartWidth / (data.length - 1) : 0;

  const buildPath = (key: 'newCount' | 'closedCount' | 'replyCount') =>
    data
      .map((d, i) => {
        const x = padding.left + i * stepX;
        const y = padding.top + chartHeight - (d[key] / maxCount) * chartHeight;
        return `${i === 0 ? 'M' : 'L'} ${x} ${y}`;
      })
      .join(' ');

  const newPath = buildPath('newCount');
  const closedPath = buildPath('closedCount');
  const replyPath = buildPath('replyCount');

  // Y 轴刻度
  const yTicks = 4;
  const yTickValues = Array.from({ length: yTicks + 1 }, (_, i) => Math.round((maxCount / yTicks) * i));

  // X 轴标签（自适应显示，避免重叠）
  const labelInterval = data.length > 30 ? Math.ceil(data.length / 10) : 1;

  return (
    <div className="w-full overflow-x-auto">
      <svg width={width} height={height} className="min-w-full" style={{ minWidth: 560 }}>
        {/* 网格线 */}
        {yTickValues.map((tick, i) => {
          const y = padding.top + chartHeight - (tick / maxCount) * chartHeight;
          return (
            <g key={i}>
              <line x1={padding.left} y1={y} x2={width - padding.right} y2={y} className="stroke-gray-200 dark:stroke-slate-700" strokeWidth={1} />
              <text x={padding.left - 8} y={y + 4} textAnchor="end" className="text-[10px] fill-gray-400 dark:fill-slate-400">
                {tick}
              </text>
            </g>
          );
        })}

        {/* X 轴标签 */}
        {data.map((d, i) => {
          if (i % labelInterval !== 0 && i !== data.length - 1) return null;
          const x = padding.left + i * stepX;
          const label = new Date(d.date).toLocaleDateString(intlLocale, { month: '2-digit', day: '2-digit' });
          return (
            <text key={i} x={x} y={height - 12} textAnchor="middle" className="text-[10px] fill-gray-500 dark:fill-slate-400">
              {label}
            </text>
          );
        })}

        {/* 折线 */}
        <path d={newPath} fill="none" stroke="#3b82f6" strokeWidth={2} />
        <path d={closedPath} fill="none" stroke="#10b981" strokeWidth={2} strokeDasharray="4 3" />
        <path d={replyPath} fill="none" stroke="#f59e0b" strokeWidth={2} strokeDasharray="2 2" />

        {/* 数据点 */}
        {data.map((d, i) => {
          const x = padding.left + i * stepX;
          const yNew = padding.top + chartHeight - (d.newCount / maxCount) * chartHeight;
          return <circle key={`n-${i}`} cx={x} cy={yNew} r={2.5} fill="#3b82f6" />;
        })}
      </svg>

      {/* 图例 */}
      <div className="flex items-center justify-center gap-5 mt-2 text-xs text-gray-600 dark:text-slate-400">
        <span className="flex items-center gap-1.5">
          <span className="w-3 h-0.5 bg-blue-500"></span>{t('data.common.metric.newConversations')}
        </span>
        <span className="flex items-center gap-1.5">
          <span className="w-3 h-0.5 bg-emerald-500" style={{ borderTop: '2px dashed #10b981' }}></span>{t('data.common.metric.closedConversations')}
        </span>
        <span className="flex items-center gap-1.5">
          <span className="w-3 h-0.5 bg-amber-500" style={{ borderTop: '2px dashed #f59e0b' }}></span>{t('data.common.metric.agentReplies')}
        </span>
      </div>
    </div>
  );
}

// ====== 状态分布饼图 ======
const STATUS_COLORS: Record<number, string> = {
  1: '#10b981', // Active - 绿
  2: '#f59e0b', // Pending - 黄
  3: '#6b7280', // Closed - 灰
  4: '#ef4444', // Spam - 红
};

/**
 * 会话状态码 → 前端词条 key。
 *
 * 后端 `NoneadCSS.Application/Services/ReportService.cs:209` 用 `GetStatusName`
 * （同文件 `:440-447`）把**中文标签**塞进 `ReportStatusDistributionDto.StatusName` 下发，
 * 英文界面下会直出中文，故这里不再渲染 `statusName`，改按稳定数字码查前端字典。
 *
 * 码表来源 `NoneadCSS.Domain/Enums/ConversationStatus.cs`：
 * 1 = Active、2 = Pending、3 = Closed、4 = Spam（枚举仅此四值）。
 *
 * 用 `Record<string, TranslationKey | undefined>` 而**不是** `Record<string, TranslationKey>`：
 * 后者在 strict 下会让回落分支类型上不可达，而非法码运行期确实可达
 * （后端 `_ => status.ToString()` 会下发枚举名或数字串）。
 */
const STATUS_KEYS_BY_CODE: Record<string, TranslationKey | undefined> = {
  '1': 'format.status.active',
  '2': 'format.status.pending',
  '3': 'format.status.closed',
  '4': 'format.status.spam',
};

function StatusPie({ data }: { data: ReportStatusDistributionDto[] }) {
  const { t } = useI18n();
  const total = data.reduce((sum, s) => sum + s.count, 0);
  const radius = 70;
  const cx = 90;
  const cy = 90;
  const strokeWidth = 28;

  /** 状态标签一律走字典；未知码显式回落 `format.unknown`（绝不渲染后端中文标签） */
  const statusLabel = (code: number) => {
    const key = STATUS_KEYS_BY_CODE[String(code)];
    return key ? t(key) : t('format.unknown');
  };

  if (total === 0) {
    return <div className="flex items-center justify-center h-48 text-sm text-gray-400 dark:text-slate-400">{t('common.empty')}</div>;
  }

  let cumulative = 0;
  const segments = data.map((slice) => {
    const percent = total > 0 ? slice.count / total : 0;
    const startAngle = cumulative * 2 * Math.PI - Math.PI / 2;
    cumulative += percent;
    const endAngle = cumulative * 2 * Math.PI - Math.PI / 2;
    const largeArc = percent > 0.5 ? 1 : 0;
    const x1 = cx + radius * Math.cos(startAngle);
    const y1 = cy + radius * Math.sin(startAngle);
    const x2 = cx + radius * Math.cos(endAngle);
    const y2 = cy + radius * Math.sin(endAngle);
    return {
      ...slice,
      percent,
      path: `M ${x1} ${y1} A ${radius} ${radius} 0 ${largeArc} 1 ${x2} ${y2}`,
    };
  });

  return (
    <div className="flex items-center gap-4 flex-wrap">
      <svg width="180" height="180" viewBox="0 0 180 180" className="flex-shrink-0">
        <circle cx={cx} cy={cy} r={radius} fill="none" className="stroke-gray-200 dark:stroke-slate-700" strokeWidth={strokeWidth} />
        {segments.map((seg, i) => {
          if (seg.percent === 0) return null;
          if (seg.percent === 1) {
            return (
              <circle key={i} cx={cx} cy={cy} r={radius} fill="none" stroke={STATUS_COLORS[seg.status] ?? '#9ca3af'} strokeWidth={strokeWidth} />
            );
          }
          return (
            <path
              key={i}
              d={seg.path}
              fill="none"
              stroke={STATUS_COLORS[seg.status] ?? '#9ca3af'}
              strokeWidth={strokeWidth}
              strokeLinecap="butt"
            />
          );
        })}
        <text x={cx} y={cy - 4} textAnchor="middle" className="text-xl font-bold fill-gray-900 dark:fill-slate-100">
          {total}
        </text>
        <text x={cx} y={cy + 14} textAnchor="middle" className="text-[10px] fill-gray-500 dark:fill-slate-400">
          {t('data.reports.statusPie.total')}
        </text>
      </svg>
      <div className="flex-1 min-w-[180px] space-y-2">
        {data.map((s) => (
          <div key={s.status} className="flex items-center gap-2 text-sm">
            <span
              className="w-3 h-3 rounded-sm flex-shrink-0"
              style={{ backgroundColor: STATUS_COLORS[s.status] ?? '#9ca3af' }}
            />
            <span className="text-gray-700 dark:text-slate-300 flex-1">{statusLabel(s.status)}</span>
            <span className="font-medium text-gray-900 dark:text-slate-100">{s.count}</span>
            <span className="text-xs text-gray-500 dark:text-slate-400 w-12 text-right">{s.percentage.toFixed(1)}%</span>
          </div>
        ))}
      </div>
    </div>
  );
}

// ====== 横向柱状图 ======
function HorizontalBars({
  data,
}: {
  data: { label: string; sublabel?: string; value: number; percentage: number; color?: string }[];
}) {
  const { t } = useI18n();
  if (!data || data.length === 0) {
    return <div className="flex items-center justify-center h-32 text-sm text-gray-400 dark:text-slate-400">{t('common.empty')}</div>;
  }
  const maxValue = Math.max(...data.map((d) => d.value), 1);
  return (
    <div className="space-y-2.5">
      {data.map((d, i) => (
        <div key={i}>
          <div className="flex items-center justify-between text-xs mb-1">
            <div className="flex items-center gap-1.5 min-w-0">
              <span className="text-gray-700 dark:text-slate-300 truncate">{d.label}</span>
              {d.sublabel && <span className="text-gray-400 dark:text-slate-500 truncate">· {d.sublabel}</span>}
            </div>
            <div className="flex items-center gap-2 flex-shrink-0">
              <span className="font-semibold text-gray-900 dark:text-slate-100">{d.value}</span>
              <span className="text-gray-500 dark:text-slate-400 w-12 text-right">{d.percentage.toFixed(1)}%</span>
            </div>
          </div>
          <div className="h-2 bg-gray-100 dark:bg-slate-700 rounded-full overflow-hidden">
            <div
              className="h-full rounded-full transition-all"
              style={{
                width: `${(d.value / maxValue) * 100}%`,
                backgroundColor: d.color ?? '#3b82f6',
              }}
            />
          </div>
        </div>
      ))}
    </div>
  );
}

// ====== 主页面 ======
export default function Reports() {
  const { t } = useI18n();
  const [report, setReport] = useState<ReportResponse | null>(null);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshLock, setRefreshLock] = useState(false);
  const isRefreshing = loading || refreshLock;
  const [error, setError] = useState('');
  const [rangeKey, setRangeKey] = useState<RangeKey>('30d');
  const [mailboxId, setMailboxId] = useState<number | null>(null);

  useEffect(() => {
    mailboxApi
      .listAll()
      .then((resp) => setMailboxes(Array.isArray(resp) ? resp : []))
      .catch((err: unknown) => console.error('加载邮箱列表失败:', err));
  }, []);

  const fetchReport = useCallback(async () => {
    const opt = rangeOptions.find((o) => o.key === rangeKey)!;
    const { start, end } = opt.compute();
    setLoading(true);
    setError('');
    try {
      const data = await reportApi.getReport(start, end, mailboxId);
      setReport(data);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('data.reports.loadReportFailed'));
    } finally {
      setLoading(false);
    }
  }, [rangeKey, mailboxId, t]);

  useEffect(() => {
    fetchReport();
  }, [fetchReport]);

  const handleRefresh = useCallback(() => {
    if (isRefreshing) return;
    setRefreshLock(true);
    try {
      void fetchReport();
    } finally {
      setTimeout(() => setRefreshLock(false), 400);
    }
  }, [isRefreshing, fetchReport]);

  // 导出 CSV
  const handleExportCsv = useCallback(() => {
    if (!report) return;
    const lines: string[] = [];
    // 概要
    lines.push(t('data.reports.csv.summaryHeading'));
    lines.push([t('data.reports.csv.dateRangeCol'), `${formatDate(report.startDate)} ~ ${formatDate(report.endDate)}`].join(','));
    lines.push([
      t('data.reports.csv.mailboxFilterCol'),
      report.mailboxId ? mailboxes.find((m) => m.id === report.mailboxId)?.name ?? report.mailboxId : t('data.common.all'),
    ].join(','));
    lines.push('');
    lines.push([t('data.reports.csv.metricCol'), t('data.reports.csv.valueCol')].join(','));
    lines.push([t('data.common.metric.newConversations'), report.summary.newConversations].join(','));
    lines.push([t('data.common.metric.closedConversations'), report.summary.closedConversations].join(','));
    lines.push([t('data.common.metric.agentReplies'), report.summary.totalReplies].join(','));
    lines.push([t('data.common.metric.newCustomers'), report.summary.newCustomers].join(','));
    lines.push([t('data.reports.csv.avgFirstResponseHours'), report.summary.averageFirstResponseHours].join(','));
    lines.push([t('data.reports.csv.avgResolutionHours'), report.summary.averageResolutionHours].join(','));
    lines.push([t('data.reports.csv.slaRate'), report.summary.slaAchievementRate].join(','));
    lines.push('');

    // 趋势
    lines.push(t('data.reports.csv.trendHeading'));
    lines.push([
      t('data.reports.csv.dateCol'),
      t('data.reports.csv.newCol'),
      t('data.reports.csv.closedCol'),
      t('data.reports.csv.replyCol'),
    ].join(','));
    report.trend.forEach((point) => {
      lines.push(`${formatDate(point.date)},${point.newCount},${point.closedCount},${point.replyCount}`);
    });
    lines.push('');

    // 客服绩效
    lines.push(t('data.reports.csv.performanceHeading'));
    lines.push([
      t('data.reports.csv.nameCol'),
      t('data.reports.csv.emailCol'),
      t('data.reports.csv.currentAssignedCol'),
      t('data.reports.csv.repliesCol'),
      t('data.reports.csv.closedCountCol'),
      t('data.reports.csv.avgFirstResponseHours'),
    ].join(','));
    report.userPerformance.forEach((u) => {
      lines.push(`${u.userName},${u.email},${u.assignedCount},${u.replyCount},${u.closedCount},${u.averageFirstResponseHours}`);
    });
    lines.push('');

    // 邮箱负载
    lines.push(t('data.reports.csv.mailboxLoadHeading'));
    lines.push([
      t('data.reports.csv.mailboxCol'),
      t('data.reports.csv.newCol'),
      t('data.reports.csv.closedCol'),
      t('format.status.active'),
      t('data.reports.csv.percentageCol'),
    ].join(','));
    report.mailboxLoad.forEach((m) => {
      lines.push(`${m.mailboxName} (${m.email}),${m.newCount},${m.closedCount},${m.activeCount},${m.percentage}`);
    });
    lines.push('');

    // 客户域名
    lines.push(t('data.reports.csv.customerDomainsHeading'));
    lines.push([
      t('data.reports.csv.domainCol'),
      t('data.reports.csv.conversationsCol'),
      t('data.reports.csv.uniqueCustomersCol'),
      t('data.reports.csv.percentageCol'),
    ].join(','));
    report.customerDomains.forEach((d) => {
      lines.push(`${d.domain},${d.conversationCount},${d.uniqueCustomerCount},${d.percentage}`);
    });

    const csv = '\uFEFF' + lines.join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `report-${formatDate(report.startDate)}-${formatDate(report.endDate)}.csv`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }, [report, mailboxes, t]);

  // 状态分布数据
  const statusPieData = useMemo(() => report?.statusDistribution ?? [], [report]);

  // 邮箱负载柱状图数据
  const mailboxBars = useMemo(
    () =>
      (report?.mailboxLoad ?? [])
        .filter((m) => m.newCount > 0)
        .map((m) => ({
          label: m.mailboxName,
          sublabel: m.email,
          value: m.newCount,
          percentage: m.percentage,
          color: '#3b82f6',
        })),
    [report],
  );

  // 客户域名柱状图数据
  const domainBars = useMemo(
    () =>
      (report?.customerDomains ?? []).map((d) => ({
        label: d.domain,
        sublabel: t('data.reports.domains.customers', { count: d.uniqueCustomerCount }),
        value: d.conversationCount,
        percentage: d.percentage,
        color: '#8b5cf6',
      })),
    [report, t],
  );

  const summary = report?.summary;

  return (
    <div className="space-y-5">
      {/* 页头 */}
      <div className="relative overflow-hidden bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-800 dark:to-slate-900 rounded-2xl border border-primary-100 dark:border-slate-700/50 p-5 sm:p-6">
        <div className="absolute top-0 right-0 w-64 h-64 bg-primary-200/30 dark:bg-primary-500/10 rounded-full -translate-y-1/2 translate-x-1/2 blur-3xl" />
        <div className="absolute bottom-0 left-0 w-48 h-48 bg-indigo-200/30 dark:bg-indigo-500/10 rounded-full translate-y-1/2 -translate-x-1/2 blur-3xl" />
        <div className="relative flex items-start justify-between gap-3 flex-wrap">
          <div className="flex items-start gap-3 min-w-0 flex-1">
            <div className="p-2.5 sm:p-3 bg-white dark:bg-slate-800 rounded-xl shadow-md shrink-0">
              <BarChart3 className="w-6 h-6 sm:w-7 sm:h-7 text-primary-600 dark:text-primary-400" />
            </div>
            <div className="min-w-0">
              <h1 className="text-lg sm:text-xl font-bold text-gray-900 dark:text-slate-100">{t('data.reports.title')}</h1>
              <p className="text-xs sm:text-sm text-gray-600 dark:text-slate-400 mt-1">
                {report ? (
                  <>
                    {t('data.reports.header.dataRangePrefix')}<span className="font-semibold text-gray-700 dark:text-slate-300">{formatDate(report.startDate)}</span>
                    {' ~ '}
                    <span className="font-semibold text-gray-700 dark:text-slate-300">{formatDate(report.endDate)}</span>
                  </>
                ) : (
                  t('common.shell.loading')
                )}
              </p>
            </div>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            <button
              onClick={handleExportCsv}
              disabled={!report || loading}
              className="inline-flex items-center gap-1.5 sm:gap-2 px-3 sm:px-4 py-2 sm:py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 hover:bg-gray-50 dark:hover:bg-slate-700 disabled:bg-gray-100 dark:disabled:bg-slate-700 disabled:text-gray-400 dark:disabled:text-slate-500 text-gray-700 dark:text-slate-300 rounded-xl shadow-sm hover:shadow transition-all active:scale-[0.98]"
            >
              <Download className="w-4 h-4" />
              <span className="hidden sm:inline">{t('data.common.exportCsv')}</span>
              <span className="sm:hidden">CSV</span>
            </button>
            <button
              onClick={handleRefresh}
              disabled={isRefreshing}
              className="inline-flex items-center gap-1.5 sm:gap-2 px-3 sm:px-4 py-2 sm:py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white rounded-xl shadow-sm hover:shadow transition-all active:scale-[0.98] disabled:cursor-not-allowed"
            >
              <RefreshCw className={`w-4 h-4 ${isRefreshing ? 'animate-spin' : ''}`} />
              <span className="hidden sm:inline">{t('data.common.refresh')}</span>
            </button>
          </div>
        </div>
      </div>

      {/* 时间范围和邮箱筛选 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-100 dark:border-slate-700/50 p-3 sm:p-4 shadow-sm">
        <div className="flex items-center gap-3 flex-wrap">
          <div className="flex items-center gap-1 p-1 bg-gray-100 dark:bg-slate-700 rounded-xl overflow-x-auto max-w-full">
            {rangeOptions.map((opt) => (
              <button
                key={opt.key}
                onClick={() => setRangeKey(opt.key)}
                className={`px-3 py-2 text-xs font-medium rounded-lg transition-all whitespace-nowrap ${
                  rangeKey === opt.key
                    ? 'bg-white dark:bg-slate-800 text-primary-700 dark:text-primary-400 shadow-sm'
                    : 'text-gray-600 dark:text-slate-400 hover:text-gray-900 dark:hover:text-slate-200'
                }`}
              >
                {t(opt.labelKey)}
              </button>
            ))}
          </div>

          <div className="flex items-center gap-2 ml-auto shrink-0">
            <Mail className="w-4 h-4 text-gray-400 hidden sm:block" />
            <select
              value={mailboxId ?? ''}
              onChange={(e) => setMailboxId(e.target.value === '' ? null : Number(e.target.value))}
              className="px-3 py-2 text-sm border border-gray-200 dark:border-slate-600 bg-white dark:bg-slate-900 text-gray-900 dark:text-slate-100 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition-all"
            >
              <option value="">{t('data.common.allMailboxes')}</option>
              {mailboxes.map((m) => (
                <option key={m.id} value={m.id}>
                  {m.name}
                </option>
              ))}
            </select>
          </div>
        </div>
      </div>

      {error && (
        <div className="bg-red-50 dark:bg-red-900/30 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-400 px-4 py-3 rounded-lg text-sm">
          {t('data.common.loadFailedPrefix')}{error}
        </div>
      )}

      {/* 核心指标卡片 */}
      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-3 sm:gap-4">
        <KpiCard
          title={t('data.common.metric.newConversations')}
          value={summary?.newConversations ?? '-'}
          icon={<Plus className="w-5 h-5 text-blue-600 dark:text-blue-400" />}
          color="bg-blue-50 dark:bg-blue-900/30"
        />
        <KpiCard
          title={t('data.common.metric.closedConversations')}
          value={summary?.closedConversations ?? '-'}
          icon={<Check className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />}
          color="bg-emerald-50 dark:bg-emerald-900/30"
        />
        <KpiCard
          title={t('data.common.metric.agentReplies')}
          value={summary?.totalReplies ?? '-'}
          icon={<MessageSquare className="w-5 h-5 text-amber-600 dark:text-amber-400" />}
          color="bg-amber-50 dark:bg-amber-900/30"
        />
        <KpiCard
          title={t('data.common.metric.newCustomers')}
          value={summary?.newCustomers ?? '-'}
          icon={<Users className="w-5 h-5 text-purple-600 dark:text-purple-400" />}
          color="bg-purple-50 dark:bg-purple-900/30"
        />
        <KpiCard
          title={t('data.reports.kpi.avgFirstResponse')}
          value={summary ? `${summary.averageFirstResponseHours}h` : '-'}
          subtitle={t('data.reports.kpi.avgFirstResponseHint')}
          icon={<Clock className="w-5 h-5 text-cyan-600 dark:text-cyan-400" />}
          color="bg-cyan-50 dark:bg-cyan-900/30"
        />
        <KpiCard
          title={t('data.reports.kpi.slaAchievement')}
          value={summary ? `${summary.slaAchievementRate}%` : '-'}
          subtitle={t('data.reports.kpi.slaTarget')}
          icon={<Shield className="w-5 h-5 text-rose-600 dark:text-rose-400" />}
          color="bg-rose-50 dark:bg-rose-900/30"
        />
      </div>

      {/* 趋势图 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 shadow-sm">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-blue-50 dark:bg-blue-900/30 rounded-lg">
              <TrendingUp className="w-5 h-5 text-blue-600 dark:text-blue-400" />
            </div>
            <div>
              <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base">{t('data.reports.trend.title')}</h3>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{t('data.reports.trend.subtitle')}</p>
            </div>
          </div>
        </div>
        {loading ? (
          <div className="flex items-center justify-center h-48 text-sm text-gray-400 dark:text-slate-400">{t('common.shell.loading')}</div>
        ) : (
          <TrendChart data={report?.trend ?? []} />
        )}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 sm:gap-5">
        {/* 状态分布 */}
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 shadow-sm">
          <div className="flex items-center gap-3 mb-4">
            <div className="p-2 bg-emerald-50 dark:bg-emerald-900/30 rounded-lg">
              <PieChart className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
            </div>
            <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base">{t('data.reports.statusDistribution.title')}</h3>
          </div>
          {loading ? (
            <div className="flex items-center justify-center h-48 text-sm text-gray-400 dark:text-slate-400">{t('common.shell.loading')}</div>
          ) : (
            <StatusPie data={statusPieData} />
          )}
        </div>

        {/* 邮箱负载 */}
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 shadow-sm">
          <div className="flex items-center gap-3 mb-4">
            <div className="p-2 bg-indigo-50 dark:bg-indigo-900/30 rounded-lg">
              <Server className="w-5 h-5 text-indigo-600 dark:text-indigo-400" />
            </div>
            <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base">{t('data.reports.mailboxLoad.title')}</h3>
          </div>
          {loading ? (
            <div className="flex items-center justify-center h-48 text-sm text-gray-400 dark:text-slate-400">{t('common.shell.loading')}</div>
          ) : (
            <HorizontalBars data={mailboxBars} />
          )}
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 sm:gap-5">
        {/* 客服绩效 */}
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 shadow-sm">
          <div className="flex items-center gap-3 mb-4">
            <div className="p-2 bg-purple-50 dark:bg-purple-900/30 rounded-lg">
              <Users className="w-5 h-5 text-purple-600 dark:text-purple-400" />
            </div>
            <h3 className="font-semibold text-gray-900 dark:text-slate-100">{t('data.reports.performance.title')}</h3>
          </div>
          {loading ? (
            <div className="flex items-center justify-center h-32 text-sm text-gray-400 dark:text-slate-400">{t('common.shell.loading')}</div>
          ) : (report?.userPerformance ?? []).length === 0 ? (
            <div className="flex items-center justify-center h-32 text-sm text-gray-400 dark:text-slate-400">{t('common.empty')}</div>
          ) : (
            <>
              {/* PC 端表格视图 */}
              <div className="hidden md:block overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-left text-xs text-gray-500 dark:text-slate-400 border-b border-gray-100 dark:border-slate-700">
                      <th className="py-2 pr-3">{t('data.reports.performance.colAgent')}</th>
                      <th className="py-2 px-3 text-right">{t('data.reports.performance.colAssigned')}</th>
                      <th className="py-2 px-3 text-right">{t('data.reports.performance.colReplies')}</th>
                      <th className="py-2 px-3 text-right">{t('data.reports.performance.colClosed')}</th>
                      <th className="py-2 pl-3 text-right">{t('data.reports.performance.colResponseHours')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(report?.userPerformance ?? []).map((u) => (
                      <tr key={u.userId} className="border-b border-gray-50 dark:border-slate-700/50 last:border-0 hover:bg-gray-50/50 dark:hover:bg-slate-700/30 transition-colors">
                        <td className="py-2.5 pr-3">
                          <div className="font-medium text-gray-900 dark:text-slate-100">{u.userName || '-'}</div>
                          <div className="text-xs text-gray-400 dark:text-slate-500 truncate max-w-[180px]">{u.email}</div>
                        </td>
                        <td className="py-2.5 px-3 text-right text-gray-700 dark:text-slate-300">{u.assignedCount}</td>
                        <td className="py-2.5 px-3 text-right font-medium text-blue-600 dark:text-blue-400">{u.replyCount}</td>
                        <td className="py-2.5 px-3 text-right font-medium text-emerald-600 dark:text-emerald-400">{u.closedCount}</td>
                        <td className="py-2.5 pl-3 text-right text-gray-700 dark:text-slate-300">{u.averageFirstResponseHours || '-'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {/* 移动端卡片视图 */}
              <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
                {(report?.userPerformance ?? []).map((u) => (
                  <div key={u.userId} className="py-3 first:pt-0 last:pb-0 space-y-2">
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <div className="font-medium text-gray-900 dark:text-slate-100 truncate">{u.userName || '-'}</div>
                        <div className="text-[11px] text-gray-400 dark:text-slate-500 truncate">{u.email}</div>
                      </div>
                      <div className="text-[11px] text-gray-400 dark:text-slate-500 shrink-0">
                        {t('data.reports.performance.responsePrefix')}<span className="font-semibold text-gray-900 dark:text-slate-100">{u.averageFirstResponseHours || '-'}</span>h
                      </div>
                    </div>
                    <div className="grid grid-cols-3 gap-2">
                      <div className="bg-gray-50 dark:bg-slate-700/40 rounded-lg px-2 py-1.5 text-center">
                        <div className="text-[10px] text-gray-400 dark:text-slate-500">{t('data.reports.performance.colAssigned')}</div>
                        <div className="text-sm font-semibold text-gray-900 dark:text-slate-100">{u.assignedCount}</div>
                      </div>
                      <div className="bg-blue-50 dark:bg-blue-900/20 rounded-lg px-2 py-1.5 text-center">
                        <div className="text-[10px] text-blue-500 dark:text-blue-400">{t('data.reports.performance.colReplies')}</div>
                        <div className="text-sm font-semibold text-blue-600 dark:text-blue-400">{u.replyCount}</div>
                      </div>
                      <div className="bg-emerald-50 dark:bg-emerald-900/20 rounded-lg px-2 py-1.5 text-center">
                        <div className="text-[10px] text-emerald-500 dark:text-emerald-400">{t('data.reports.performance.colClosed')}</div>
                        <div className="text-sm font-semibold text-emerald-600 dark:text-emerald-400">{u.closedCount}</div>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </>
          )}
        </div>

        {/* 客户域名 Top 10 */}
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 shadow-sm">
          <div className="flex items-center gap-3 mb-4">
            <div className="p-2 bg-amber-50 dark:bg-amber-900/30 rounded-lg">
              <Globe className="w-5 h-5 text-amber-600 dark:text-amber-400" />
            </div>
            <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base">{t('data.reports.domains.title')}</h3>
          </div>
          {loading ? (
            <div className="flex items-center justify-center h-32 text-sm text-gray-400 dark:text-slate-400">{t('common.shell.loading')}</div>
          ) : (
            <HorizontalBars data={domainBars} />
          )}
        </div>
      </div>

      {/* 平均解决时长 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 shadow-sm">
        <div className="flex items-center justify-between gap-3">
          <div className="flex items-center gap-3 min-w-0">
            <div className="p-2 bg-rose-50 dark:bg-rose-900/30 rounded-lg shrink-0">
              <Timer className="w-5 h-5 text-rose-600 dark:text-rose-400" />
            </div>
            <div className="min-w-0">
              <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base">{t('data.reports.resolution.title')}</h3>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-1">{t('data.reports.resolution.subtitle')}</p>
            </div>
          </div>
          <div className="text-right shrink-0">
            <p className="text-2xl sm:text-3xl font-bold text-gray-900 dark:text-slate-100">
              {summary?.averageResolutionHours ?? '-'}
              <span className="text-sm sm:text-base font-normal text-gray-500 dark:text-slate-400 ml-1">{t('data.reports.resolution.unitHours')}</span>
            </p>
            <p className="text-[11px] sm:text-xs text-gray-500 dark:text-slate-400 mt-0.5">
              ≈ {summary ? (summary.averageResolutionHours / 24).toFixed(1) : '-'} {t('data.reports.resolution.unitDays')}
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
