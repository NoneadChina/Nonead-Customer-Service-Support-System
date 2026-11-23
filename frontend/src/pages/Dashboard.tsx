import { useEffect, useState, useCallback, useRef } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { dashboardApi, conversationApi, customReportApi } from '../api';
import type { DashboardResponse, ConversationResponse, CustomReportDto } from '../api/types';
import { formatDateTime, relativeTime, safeUrl } from '../utils/format';
import UserAvatar from '../components/UserAvatar';
import { useSignalREvent } from '../hooks/useSignalR';
import { useAutoRefresh } from '../hooks/useAutoRefresh';
import { SignalREvents } from '../services/signalR';
import { useToast } from '../context/ToastContext';
import { useI18n } from '../context/I18nContext';
import { toIntlLocale } from '../i18n/locale';
import type { TranslateParams } from '../i18n/types';
import type { TranslationKey } from '../locales/zh-Hans';

/** 与 `I18nContext` 的 `t` 签名一致；`formatDuration` 是模块级纯函数，需显式传入 `t` */
type TranslateFn = (key: TranslationKey, params?: TranslateParams) => string;

function formatDuration(minutes: number, t: TranslateFn): string {
  if (minutes <= 0) return '-';
  if (minutes < 1) return t('dashboard.duration.lessThanOneMinute');
  if (minutes < 60) return t('dashboard.duration.minutes', { count: Math.round(minutes) });
  const hours = Math.floor(minutes / 60);
  const mins = Math.round(minutes % 60);
  if (hours < 24) {
    return (
      t('dashboard.duration.hours', { count: hours }) +
      (mins > 0 ? ` ${t('dashboard.duration.minutes', { count: mins })}` : '')
    );
  }
  const days = Math.floor(hours / 24);
  const remainingHours = hours % 24;
  return (
    t('dashboard.duration.days', { count: days }) +
    (remainingHours > 0 ? ` ${t('dashboard.duration.hours', { count: remainingHours })}` : '')
  );
}

interface StatCardProps {
  title: string;
  value: number | string | undefined;
  icon: React.ReactNode;
  color: string;
  bgColor: string;
  trend?: { value: number; label: string };
  subtitle?: string;
}

function StatCard({ title, value, icon, color, bgColor, trend, subtitle }: StatCardProps) {
  return (
    <div className="group relative bg-white dark:bg-slate-800 rounded-2xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 hover:shadow-md transition-all duration-300 hover:-translate-y-0.5">
      <div className="flex items-start justify-between">
        <div className="min-w-0">
          <p className="text-xs font-medium text-gray-500 dark:text-slate-400 uppercase tracking-wider">{title}</p>
          <p className="text-2xl sm:text-3xl font-bold text-gray-900 dark:text-slate-100 mt-1 sm:mt-2 truncate">{value ?? '-'}</p>
          {subtitle && <p className="text-xs text-gray-400 dark:text-slate-400 mt-1 truncate">{subtitle}</p>}
          {trend && (
            <div className={`inline-flex items-center gap-1 text-xs mt-2 px-2 py-1 rounded-full ${trend.value >= 0 ? 'bg-green-50 dark:bg-green-900/30 text-green-600 dark:text-green-400' : 'bg-red-50 dark:bg-red-900/30 text-red-600 dark:text-red-400'}`}>
              <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                {trend.value >= 0 ? (
                  <path strokeLinecap="round" strokeLinejoin="round" d="M5 10l7-7m0 0l7 7m-7-7v18" />
                ) : (
                  <path strokeLinecap="round" strokeLinejoin="round" d="M19 14l-7 7m0 0l-7-7m7 7V3" />
                )}
              </svg>
              {Math.abs(trend.value)} {trend.label}
            </div>
          )}
        </div>
        <div className={`w-10 h-10 sm:w-12 sm:h-12 rounded-xl flex items-center justify-center flex-shrink-0 ${bgColor} group-hover:scale-110 transition-transform duration-300`}>
          <div className={color}>{icon}</div>
        </div>
      </div>
    </div>
  );
}

interface PieSlice {
  label: string;
  value: number;
  color: string;
}

function StatusPieChart({ slices }: { slices: PieSlice[] }) {
  const { t } = useI18n();
  const total = slices.reduce((sum, s) => sum + s.value, 0);
  const radius = 60;
  const cx = 80;
  const cy = 80;
  const strokeWidth = 24;

  if (total === 0) {
    return (
      <div className="flex items-center justify-center h-40 text-sm text-gray-400 dark:text-slate-400">
        {t('common.empty')}
      </div>
    );
  }

  let cumulativePercent = 0;
  const segments = slices.map((slice) => {
    const percent = total > 0 ? slice.value / total : 0;
    const startAngle = cumulativePercent * 2 * Math.PI - Math.PI / 2;
    cumulativePercent += percent;
    const endAngle = cumulativePercent * 2 * Math.PI - Math.PI / 2;
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
    <div className="flex flex-col sm:flex-row items-center gap-4">
      <svg width="140" height="140" viewBox="0 0 160 160" className="flex-shrink-0 w-[140px] h-[140px] sm:w-[160px] sm:h-[160px]">
        <circle cx={cx} cy={cy} r={radius} fill="none" stroke="#f3f4f6" className="dark:stroke-slate-700" strokeWidth={strokeWidth} />
        {segments.map((seg, i) => {
          if (seg.percent === 0) return null;
          if (seg.percent === 1) {
            return (
              <circle
                key={i}
                cx={cx}
                cy={cy}
                r={radius}
                fill="none"
                stroke={seg.color}
                strokeWidth={strokeWidth}
              />
            );
          }
          return (
            <path
              key={i}
              d={seg.path}
              fill="none"
              stroke={seg.color}
              strokeWidth={strokeWidth}
              strokeLinecap="butt"
            />
          );
        })}
        <text x={cx} y={cy - 4} textAnchor="middle" className="text-xl font-bold fill-gray-900 dark:fill-slate-100">
          {total}
        </text>
        <text x={cx} y={cy + 12} textAnchor="middle" className="text-[10px] fill-gray-500 dark:fill-slate-400">
          {t('dashboard.charts.totalConversations')}
        </text>
      </svg>
      <div className="flex-1 w-full space-y-2">
        {slices.map((s) => {
          const pct = total > 0 ? ((s.value / total) * 100).toFixed(1) : '0';
          return (
            <div key={s.label} className="flex items-center gap-2 text-sm">
              <span className="w-3 h-3 rounded-full flex-shrink-0" style={{ backgroundColor: s.color }} />
              <span className="text-gray-700 dark:text-slate-300 flex-1">{s.label}</span>
              <span className="font-semibold text-gray-900 dark:text-slate-100">{s.value}</span>
              <span className="text-xs text-gray-500 dark:text-slate-400 w-12 text-right">{pct}%</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

interface TrendPoint {
  date: string;
  newCount: number;
  closedCount: number;
}

function TrendLineChart({ data }: { data: TrendPoint[] }) {
  const { t, language } = useI18n();
  // 趋势图横轴/提示里的日期跟随界面语言（en → en-US），不再固定 zh-CN
  const intlLocale = toIntlLocale(language);
  const width = 720;
  const height = 200;
  const padding = { top: 20, right: 20, bottom: 30, left: 40 };
  const chartWidth = width - padding.left - padding.right;
  const chartHeight = height - padding.top - padding.bottom;

  if (data.length === 0) {
    return <div className="flex items-center justify-center h-40 text-sm text-gray-400 dark:text-slate-400">{t('common.empty')}</div>;
  }

  const maxCount = Math.max(...data.flatMap((d) => [d.newCount, d.closedCount]), 1);
  const stepX = data.length > 1 ? chartWidth / (data.length - 1) : 0;

  const buildPath = (key: 'newCount' | 'closedCount') => {
    return data
      .map((d, i) => {
        const x = padding.left + i * stepX;
        const y = padding.top + chartHeight - (d[key] / maxCount) * chartHeight;
        return `${i === 0 ? 'M' : 'L'} ${x} ${y}`;
      })
      .join(' ');
  };

  const newPath = buildPath('newCount');
  const closedPath = buildPath('closedCount');

  const yTicks = 4;
  const yTickValues = Array.from({ length: yTicks + 1 }, (_, i) => Math.round((maxCount / yTicks) * i));

  return (
    <div className="w-full overflow-x-auto">
      <svg width="100%" height={height} viewBox="0 0 720 200" preserveAspectRatio="xMidYMid meet" className="min-w-[320px]">
        {yTickValues.map((tick, i) => {
          const y = padding.top + chartHeight - (tick / maxCount) * chartHeight;
          return (
            <g key={i}>
              <line
                x1={padding.left}
                y1={y}
                x2={width - padding.right}
                y2={y}
                stroke="#f3f4f6"
                className="dark:stroke-slate-700"
                strokeWidth={1}
                strokeDasharray="2 2"
              />
              <text x={padding.left - 6} y={y + 4} textAnchor="end" className="text-[10px] fill-gray-400 dark:fill-slate-400">
                {tick}
              </text>
            </g>
          );
        })}

        {data.map((d, i) => {
          const x = padding.left + i * stepX;
          const label = new Date(d.date).toLocaleDateString(intlLocale, { month: '2-digit', day: '2-digit' });
          return (
            <text key={i} x={x} y={height - 8} textAnchor="middle" className="text-[10px] fill-gray-500 dark:fill-slate-400">
              {label}
            </text>
          );
        })}

        <defs>
          <linearGradient id="newGradient" x1="0%" y1="0%" x2="0%" y2="100%">
            <stop offset="0%" stopColor="#6366f1" stopOpacity={0.2} />
            <stop offset="100%" stopColor="#6366f1" stopOpacity={0} />
          </linearGradient>
        </defs>

        <path
          d={`${newPath} L ${padding.left + (data.length - 1) * stepX} ${padding.top + chartHeight} L ${padding.left} ${padding.top + chartHeight} Z`}
          fill="url(#newGradient)"
        />
        <path d={newPath} fill="none" stroke="#6366f1" strokeWidth={2} strokeLinejoin="round" />
        <path d={closedPath} fill="none" stroke="#9ca3af" strokeWidth={2} strokeDasharray="4 2" strokeLinejoin="round" />

        {data.map((d, i) => {
          const x = padding.left + i * stepX;
          const yNew = padding.top + chartHeight - (d.newCount / maxCount) * chartHeight;
          const yClosed = padding.top + chartHeight - (d.closedCount / maxCount) * chartHeight;
          return (
            <g key={i}>
              <circle cx={x} cy={yNew} r={4} fill="white" stroke="#6366f1" strokeWidth={2} />
              <circle cx={x} cy={yClosed} r={4} fill="white" stroke="#9ca3af" strokeWidth={2} />
              <title>
                {t('dashboard.charts.trendTooltip', {
                  date: new Date(d.date).toLocaleDateString(intlLocale),
                  newCount: d.newCount,
                  closedCount: d.closedCount,
                })}
              </title>
            </g>
          );
        })}
      </svg>
    </div>
  );
}

function LoadingSkeleton() {
  return (
    <div className="space-y-4 sm:space-y-6">
      <div className="flex items-center justify-between">
        <div className="space-y-2">
          <div className="h-8 w-48 bg-gray-200 dark:bg-slate-700 rounded animate-pulse" />
          <div className="h-4 w-32 bg-gray-200 dark:bg-slate-700 rounded animate-pulse" />
        </div>
        <div className="h-4 w-40 bg-gray-200 dark:bg-slate-700 rounded animate-pulse" />
      </div>
      <div className="h-24 bg-gradient-to-r from-primary-200 to-indigo-200 dark:from-primary-900/50 dark:to-indigo-900/50 rounded-xl animate-pulse" />
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        {[...Array(8)].map((_, i) => (
          <div key={i} className="h-28 bg-gray-200 dark:bg-slate-700 rounded-xl animate-pulse" />
        ))}
      </div>
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4 sm:gap-6">
        <div className="h-48 bg-gray-200 dark:bg-slate-700 rounded-xl animate-pulse" />
        <div className="h-48 bg-gray-200 dark:bg-slate-700 rounded-xl animate-pulse" />
        <div className="h-48 bg-gray-200 dark:bg-slate-700 rounded-xl animate-pulse" />
      </div>
      <div className="h-64 bg-gray-200 dark:bg-slate-700 rounded-xl animate-pulse" />
      <div className="h-48 bg-gray-200 dark:bg-slate-700 rounded-xl animate-pulse" />
    </div>
  );
}

const CATEGORY_LABELS_DASH: Partial<Record<string, TranslationKey>> = {
  performance: 'dashboard.reportCategory.performance',
  efficiency: 'dashboard.reportCategory.efficiency',
  customer: 'dashboard.reportCategory.customer',
  workload: 'dashboard.reportCategory.workload',
};

const CATEGORY_COLORS_DASH: Record<string, string> = {
  performance: 'bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400 ring-1 ring-blue-200/60 dark:ring-blue-700/40',
  efficiency: 'bg-green-50 text-green-700 dark:bg-green-900/30 dark:text-green-400 ring-1 ring-green-200/60 dark:ring-green-700/40',
  customer: 'bg-purple-50 text-purple-700 dark:bg-purple-900/30 dark:text-purple-400 ring-1 ring-purple-200/60 dark:ring-purple-700/40',
  workload: 'bg-amber-50 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400 ring-1 ring-amber-200/60 dark:ring-amber-700/40',
};

const VISIBILITY_LABELS_DASH: Record<string, { label: TranslationKey; color: string; icon: string }> = {
  private: { label: 'dashboard.reportVisibility.private', icon: '🔒', color: 'bg-slate-100 text-slate-600 dark:bg-slate-700 dark:text-slate-300' },
  team: { label: 'dashboard.reportVisibility.team', icon: '👥', color: 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-400' },
  public: { label: 'dashboard.reportVisibility.public', icon: '🌐', color: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-400' },
};

export default function Dashboard() {
  const [data, setData] = useState<DashboardResponse | null>(null);
  const [recentConversations, setRecentConversations] = useState<ConversationResponse[]>([]);
  const [unassignedConversations, setUnassignedConversations] = useState<ConversationResponse[]>([]);
  const [pinnedReports, setPinnedReports] = useState<CustomReportDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const navigate = useNavigate();
  const toast = useToast();
  const { t, language } = useI18n();

  const fetchDataCancelled = useRef(false);
  const fetchData = useCallback(() => {
    fetchDataCancelled.current = false;
    Promise.all([
      dashboardApi.getStats(),
      conversationApi.list({ pageSize: 50 }),
    ])
      .then(([stats, convResp]) => {
        if (fetchDataCancelled.current) return;
        // dashboardApi.getStats 已通过泛型 client.get<DashboardResponse> 返回正确类型
        setData(stats);
        // conversationApi.list 返回 PagedResponse<ConversationResponse>，convResp.data 已是 ConversationResponse[]
        const arr: ConversationResponse[] = Array.isArray(convResp?.data) ? convResp.data : [];
        const sorted = [...arr].sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
        setRecentConversations(sorted.slice(0, 5));
        setUnassignedConversations(
          arr.filter((c) => c.userId === null && c.status === 1).slice(0, 5)
        );
      })
      .catch((err: unknown) => { if (!fetchDataCancelled.current) setError(err instanceof Error ? err.message : t('dashboard.loadFailed')); })
      .finally(() => { if (!fetchDataCancelled.current) setLoading(false); });
  }, [t]);

  useEffect(() => {
    fetchData();
    return () => { fetchDataCancelled.current = true; };
  }, [fetchData]);

  useEffect(() => {
    let cancelled = false;
    customReportApi.getPinned(6)
      .then((res) => { if (!cancelled) setPinnedReports(res ?? []); })
      .catch((err: unknown) => { if (!cancelled) toast.error(t('dashboard.pinnedReports.loadFailed', { message: err instanceof Error ? err.message : t('dashboard.unknownError') })); });
    return () => { cancelled = true; };
  }, [toast, t]);

  // SignalR 实时刷新：会话变更时自动刷新仪表盘
  useSignalREvent(SignalREvents.RefreshConversations, () => {
    fetchData();
  });

  useSignalREvent(SignalREvents.NewConversation, () => {
    fetchData();
  });

  useSignalREvent(SignalREvents.NewReply, () => {
    fetchData();
  });

  // 根据用户偏好自动刷新仪表盘
  useAutoRefresh(fetchData, [fetchData]);

  if (loading) {
    return <LoadingSkeleton />;
  }

  if (error) {
    return (
      <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-400 px-4 py-3 rounded-xl">
        <div className="flex items-center gap-2">
          <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
          </svg>
          <span>{t('dashboard.loadFailedWithReason', { message: error })}</span>
          <button
            onClick={() => window.location.reload()}
            className="ml-auto text-xs bg-red-100 dark:bg-red-900/40 hover:bg-red-200 dark:hover:bg-red-900/60 px-2 py-1 rounded-md transition"
          >
            {t('common.retry')}
          </button>
        </div>
      </div>
    );
  }

  if (!data) return null;

  // 其他状态会话数 = 总会话数 - 活跃 - 待处理 - 已关闭
  const otherConversations = Math.max(
    0,
    data.totalConversations - data.activeConversations - data.pendingConversations - data.closedConversations
  );

  const pieSlices: PieSlice[] = [
    { label: t('dashboard.status.active'), value: data.activeConversations, color: '#10b981' },
    { label: t('dashboard.status.pending'), value: data.pendingConversations, color: '#f59e0b' },
    { label: t('dashboard.status.closed'), value: data.closedConversations, color: '#9ca3af' },
    ...(otherConversations > 0 ? [{ label: t('dashboard.status.other'), value: otherConversations, color: '#6366f1' }] : []),
  ];

  const totalNewIn7Days = data.conversationTrend.reduce((sum, t) => sum + t.newCount, 0);
  const totalClosedIn7Days = data.conversationTrend.reduce((sum, t) => sum + t.closedCount, 0);

  return (
    <div className="space-y-4 sm:space-y-6">
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl sm:text-3xl font-bold text-gray-900 dark:text-slate-100">{t('dashboard.title')}</h1>
          <p className="text-sm text-gray-500 dark:text-slate-400 mt-1">{t('dashboard.subtitle')}</p>
        </div>
        <div className="flex items-center gap-2 text-sm text-gray-500 dark:text-slate-400 bg-gray-50 dark:bg-slate-900 px-3 py-2 rounded-lg">
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          {new Date().toLocaleString(toIntlLocale(language), { year: 'numeric', month: 'long', day: 'numeric', weekday: 'long' })}
        </div>
      </div>

      <div className="bg-gradient-to-r from-primary-600 to-indigo-600 rounded-2xl p-4 sm:p-5 sm:p-6 text-white relative overflow-hidden">
        <div className="absolute top-0 right-0 w-64 h-64 bg-white/10 rounded-full blur-3xl -translate-y-1/2 translate-x-1/2" />
        <div className="absolute bottom-0 left-0 w-48 h-48 bg-white/5 rounded-full blur-2xl translate-y-1/2 -translate-x-1/2" />
        <div className="relative z-10 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
          <div>
            <h2 className="text-lg sm:text-xl font-semibold">{t('dashboard.getStarted.title')}</h2>
            <p className="text-sm text-primary-100 mt-1">{t('dashboard.getStarted.subtitle')}</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Link
              to="/conversations"
              className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-white/15 hover:bg-white/25 rounded-lg text-sm backdrop-blur-sm transition"
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
              </svg>
              {t('dashboard.getStarted.viewConversations')}
            </Link>
            <Link
              to="/customers"
              className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-white/15 hover:bg-white/25 rounded-lg text-sm backdrop-blur-sm transition"
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
              </svg>
              {t('dashboard.getStarted.manageCustomers')}
            </Link>
            <Link
              to="/audit-logs"
              className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-white/15 hover:bg-white/25 rounded-lg text-sm backdrop-blur-sm transition"
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M9 17v-2m3 2v-4m3 4v-6m2 10H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
              </svg>
              {t('dashboard.getStarted.auditLog')}
            </Link>
            <Link
              to="/settings"
              className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-white/15 hover:bg-white/25 rounded-lg text-sm backdrop-blur-sm transition"
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
                <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
              </svg>
              {t('dashboard.getStarted.settings')}
            </Link>
          </div>
        </div>
      </div>

      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <StatCard
          title={t('dashboard.stats.totalConversations')}
          value={data.totalConversations}
          color="text-blue-600"
          bgColor="bg-blue-50"
          icon={
            <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
            </svg>
          }
        />
        <StatCard
          title={t('dashboard.stats.activeConversations')}
          value={data.activeConversations}
          color="text-green-600"
          bgColor="bg-green-50"
          icon={
            <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M13 10V3L4 14h7v7l9-11h-7z" />
            </svg>
          }
        />
        <StatCard
          title={t('dashboard.stats.newToday')}
          value={data.todayNewConversations}
          color="text-purple-600"
          bgColor="bg-purple-50"
          trend={{ value: totalNewIn7Days, label: t('dashboard.stats.last7Days') }}
          icon={
            <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m8-8H4" />
            </svg>
          }
        />
        <StatCard
          title={t('dashboard.stats.closedToday')}
          value={data.todayClosedConversations}
          color="text-amber-600"
          bgColor="bg-amber-50"
          trend={{ value: totalClosedIn7Days, label: t('dashboard.stats.last7Days') }}
          icon={
            <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          }
        />
        <StatCard
          title={t('dashboard.stats.unassignedConversations')}
          value={data.unassignedCount}
          color="text-red-600"
          bgColor="bg-red-50"
          icon={
            <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
            </svg>
          }
        />
        <StatCard
          title={t('dashboard.stats.avgFirstResponse')}
          value={formatDuration(data.avgFirstResponseTimeMinutes, t)}
          color="text-cyan-600"
          bgColor="bg-cyan-50"
          subtitle={t('dashboard.duration.minutes', { count: Math.round(data.avgFirstResponseTimeMinutes) })}
          icon={
            <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          }
        />
        <StatCard
          title={t('dashboard.stats.avgResolution')}
          value={formatDuration(data.avgResolutionTimeMinutes, t)}
          color="text-indigo-600"
          bgColor="bg-indigo-50"
          subtitle={t('dashboard.duration.minutes', { count: Math.round(data.avgResolutionTimeMinutes) })}
          icon={
            <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          }
        />
        <StatCard
          title={t('dashboard.stats.satisfactionScore')}
          value={data.satisfactionTotalCount > 0 ? `${data.satisfactionAverageScore.toFixed(1)} / 5.0` : '-'}
          color="text-pink-600"
          bgColor="bg-pink-50"
          subtitle={t('dashboard.stats.ratingCount', { count: data.satisfactionTotalCount })}
          icon={
            <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M14.828 14.828a4 4 0 01-5.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          }
        />
      </div>

      {(data.slaWarningCount > 0 || data.slaBreachedCount > 0) && (
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 sm:gap-4">
          {data.slaBreachedCount > 0 && (
            <div className="bg-gradient-to-r from-red-50 to-red-100/50 dark:from-red-900/20 dark:to-red-900/10 border border-red-200 dark:border-red-800 rounded-xl p-3 sm:p-4 hover:shadow-sm transition-shadow">
              <div className="flex items-start gap-3">
                <div className="w-9 h-9 sm:w-10 sm:h-10 bg-red-100 dark:bg-red-900/40 text-red-600 dark:text-red-400 rounded-full flex items-center justify-center flex-shrink-0">
                  <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                  </svg>
                </div>
                <div className="flex-1 min-w-0">
                  <h3 className="text-sm font-semibold text-red-900 dark:text-red-300">
                    {t('dashboard.sla.breached', { count: data.slaBreachedCount })}
                  </h3>
                  <p className="text-xs text-red-700 dark:text-red-400 mt-0.5">{t('dashboard.sla.breachedHint')}</p>
                </div>
                <Link
                  to="/sla"
                  className="ml-auto text-xs text-red-700 dark:text-red-400 hover:text-red-800 dark:hover:text-red-300 font-medium px-2 py-1 bg-red-100 dark:bg-red-900/40 rounded-md"
                >
                  {t('dashboard.viewDetails')}
                </Link>
              </div>
            </div>
          )}
          {data.slaWarningCount > 0 && (
            <div className="bg-gradient-to-r from-orange-50 to-orange-100/50 dark:from-orange-900/20 dark:to-orange-900/10 border border-orange-200 dark:border-orange-800 rounded-xl p-3 sm:p-4 hover:shadow-sm transition-shadow">
              <div className="flex items-start gap-3">
                <div className="w-9 h-9 sm:w-10 sm:h-10 bg-orange-100 dark:bg-orange-900/40 text-orange-600 dark:text-orange-400 rounded-full flex items-center justify-center flex-shrink-0">
                  <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                  </svg>
                </div>
                <div className="flex-1 min-w-0">
                  <h3 className="text-sm font-semibold text-orange-900 dark:text-orange-300">
                    {t('dashboard.sla.warning', { count: data.slaWarningCount })}
                  </h3>
                  <p className="text-xs text-orange-700 dark:text-orange-400 mt-0.5">{t('dashboard.sla.warningHint')}</p>
                </div>
                <Link
                  to="/sla"
                  className="ml-auto text-xs text-orange-700 dark:text-orange-400 hover:text-orange-800 dark:hover:text-orange-300 font-medium px-2 py-1 bg-orange-100 dark:bg-orange-900/40 rounded-md"
                >
                  {t('dashboard.viewDetails')}
                </Link>
              </div>
            </div>
          )}
        </div>
      )}

      {unassignedConversations.length > 0 && (
        <div className="bg-gradient-to-r from-amber-50 to-amber-100/50 dark:from-amber-900/20 dark:to-amber-900/10 border border-amber-200 dark:border-amber-800 rounded-xl p-3 sm:p-4 hover:shadow-sm transition-shadow">
          <div className="flex items-start gap-3">
            <div className="w-9 h-9 sm:w-10 sm:h-10 bg-amber-100 dark:bg-amber-900/40 text-amber-700 dark:text-amber-400 rounded-full flex items-center justify-center flex-shrink-0">
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
              </svg>
            </div>
            <div className="flex-1 min-w-0">
              <h3 className="text-sm font-semibold text-amber-900 dark:text-amber-300">
                {t('dashboard.unassigned.count', { count: unassignedConversations.length })}
              </h3>
              <p className="text-xs text-amber-700 dark:text-amber-400 mt-0.5">{t('dashboard.unassigned.hint')}</p>
              <div className="mt-3 space-y-1.5">
                {unassignedConversations.map((c) => (
                  <Link
                    key={c.id}
                    to={`/conversations/${c.id}`}
                    className="flex items-center justify-between gap-3 p-2.5 bg-white dark:bg-slate-800 rounded-lg hover:bg-amber-50 dark:hover:bg-amber-900/20 transition text-sm group border border-transparent hover:border-amber-200 dark:hover:border-amber-700"
                  >
                    <div className="flex items-center gap-2 min-w-0 flex-1">
                      <span className="text-xs font-mono text-amber-600 dark:text-amber-400 font-semibold">#{c.id}</span>
                      <span className="text-gray-900 dark:text-slate-100 truncate group-hover:text-amber-900 dark:group-hover:text-amber-300">
                        {c.subject || t('dashboard.noSubject')}
                      </span>
                    </div>
                    <span className="text-xs text-gray-500 dark:text-slate-400 whitespace-nowrap">
                      {relativeTime(c.createdAt)}
                    </span>
                  </Link>
                ))}
              </div>
            </div>
          </div>
        </div>
      )}

      {/* 功能模块概览 */}
      <div>
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-base font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2">
            <svg className="w-5 h-5 text-gray-400 dark:text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M4 6a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2H6a2 2 0 01-2-2V6zM14 6a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2V6zM4 16a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2H6a2 2 0 01-2-2v-2zM14 16a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2v-2z" />
            </svg>
            {t('dashboard.modules.title')}
          </h2>
        </div>
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
          {/* 知识库模块 */}
          <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 hover:shadow-md transition-all duration-300 hover:-translate-y-0.5">
            <div className="flex items-start justify-between mb-3">
              <div className="min-w-0">
                <p className="text-xs font-medium text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('dashboard.modules.knowledgeBase')}</p>
                <p className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100 mt-1">{data.knowledgeTotalArticles}</p>
                <p className="text-xs text-gray-400 dark:text-slate-400 mt-0.5 truncate">{t('dashboard.modules.articlesAndCategories', { count: data.knowledgeTotalCategories })}</p>
              </div>
              <div className="w-9 h-9 sm:w-10 sm:h-10 rounded-xl flex items-center justify-center bg-teal-50 dark:bg-teal-900/30 text-teal-600 dark:text-teal-400 flex-shrink-0">
                <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.747 0 3.332.477 4.5 1.253v13C19.832 18.477 18.247 18 16.5 18c-1.746 0-3.332.477-4.5 1.253" />
                </svg>
              </div>
            </div>
            <div className="grid grid-cols-3 gap-1 sm:gap-2 text-xs pt-3 border-t border-gray-50 dark:border-slate-700/50">
              <div className="text-center">
                <div className="font-semibold text-teal-600 dark:text-teal-400">{data.knowledgePublishedArticles}</div>
                <div className="text-gray-500 dark:text-slate-400">{t('dashboard.modules.published')}</div>
              </div>
              <div className="text-center">
                <div className="font-semibold text-amber-600 dark:text-amber-400">{data.knowledgeDraftArticles}</div>
                <div className="text-gray-500 dark:text-slate-400">{t('dashboard.modules.draft')}</div>
              </div>
              <div className="text-center">
                <div className="font-semibold text-blue-600 dark:text-blue-400">{data.knowledgeTotalViews}</div>
                <div className="text-gray-500 dark:text-slate-400">{t('dashboard.modules.views')}</div>
              </div>
            </div>
          </div>

          {/* 日历模块 */}
          <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 hover:shadow-md transition-all duration-300 hover:-translate-y-0.5">
            <div className="flex items-start justify-between mb-3">
              <div className="min-w-0">
                <p className="text-xs font-medium text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('dashboard.modules.calendar')}</p>
                <p className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100 mt-1">{data.calendarTodayEvents}</p>
                <p className="text-xs text-gray-400 dark:text-slate-400 mt-0.5 truncate">{t('dashboard.modules.todayEvents', { count: data.calendarTotalEvents })}</p>
              </div>
              <div className="w-9 h-9 sm:w-10 sm:h-10 rounded-xl flex items-center justify-center bg-indigo-50 dark:bg-indigo-900/30 text-indigo-600 dark:text-indigo-400 flex-shrink-0">
                <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
                </svg>
              </div>
            </div>
            <div className="grid grid-cols-3 gap-1 sm:gap-2 text-xs pt-3 border-t border-gray-50 dark:border-slate-700/50">
              <div className="text-center">
                <div className="font-semibold text-blue-600 dark:text-blue-400">{data.calendarUpcomingEvents}</div>
                <div className="text-gray-500 dark:text-slate-400">{t('dashboard.modules.upcoming')}</div>
              </div>
              <div className="text-center">
                <div className="font-semibold text-amber-600 dark:text-amber-400">{data.calendarPendingEvents}</div>
                <div className="text-gray-500 dark:text-slate-400">{t('dashboard.status.pending')}</div>
              </div>
              <div className="text-center">
                <div className="font-semibold text-red-600 dark:text-red-400">{data.calendarOverdueEvents}</div>
                <div className="text-gray-500 dark:text-slate-400">{t('dashboard.modules.overdue')}</div>
              </div>
            </div>
          </div>

          {/* 工作流模块 */}
          <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 hover:shadow-md transition-all duration-300 hover:-translate-y-0.5">
            <div className="flex items-start justify-between mb-3">
              <div className="min-w-0">
                <p className="text-xs font-medium text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('dashboard.modules.workflows')}</p>
                <p className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100 mt-1">{data.workflowActiveCount}</p>
                <p className="text-xs text-gray-400 dark:text-slate-400 mt-0.5 truncate">{t('dashboard.modules.activeRules', { count: data.totalWorkflows })}</p>
              </div>
              <div className="w-9 h-9 sm:w-10 sm:h-10 rounded-xl flex items-center justify-center bg-purple-50 dark:bg-purple-900/30 text-purple-600 dark:text-purple-400 flex-shrink-0">
                <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M13 10V3L4 14h7v7l9-11h-7z" />
                </svg>
              </div>
            </div>
            <div className="grid grid-cols-3 gap-1 sm:gap-2 text-xs pt-3 border-t border-gray-50 dark:border-slate-700/50">
              <div className="text-center">
                <div className="font-semibold text-gray-900 dark:text-slate-100">{data.workflowTodayExecutions}</div>
                <div className="text-gray-500 dark:text-slate-400">{t('dashboard.modules.executionsToday')}</div>
              </div>
              <div className="text-center">
                <div className="font-semibold text-green-600 dark:text-green-400">{data.workflowTodaySuccess}</div>
                <div className="text-gray-500 dark:text-slate-400">{t('dashboard.modules.succeeded')}</div>
              </div>
              <div className="text-center">
                <div className="font-semibold text-red-600 dark:text-red-400">{data.workflowTodayFailed}</div>
                <div className="text-gray-500 dark:text-slate-400">{t('dashboard.modules.failed')}</div>
              </div>
            </div>
          </div>

          {/* IM 队列模块 */}
          <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 hover:shadow-md transition-all duration-300 hover:-translate-y-0.5">
            <div className="flex items-start justify-between mb-3">
              <div className="min-w-0">
                <p className="text-xs font-medium text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('dashboard.modules.imQueue')}</p>
                <p className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100 mt-1">{data.imQueuePending + data.imQueueSending}</p>
                <p className="text-xs text-gray-400 dark:text-slate-400 mt-0.5 truncate">{t('dashboard.modules.pendingAndSentToday', { count: data.imQueueSentToday })}</p>
              </div>
              <div className="w-9 h-9 sm:w-10 sm:h-10 rounded-xl flex items-center justify-center bg-cyan-50 dark:bg-cyan-900/30 text-cyan-600 dark:text-cyan-400 flex-shrink-0">
                <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
                </svg>
              </div>
            </div>
            <div className="grid grid-cols-3 gap-1 sm:gap-2 text-xs pt-3 border-t border-gray-50 dark:border-slate-700/50">
              <div className="text-center">
                <div className="font-semibold text-amber-600 dark:text-amber-400">{data.imQueuePending}</div>
                <div className="text-gray-500 dark:text-slate-400">{t('dashboard.modules.pendingSend')}</div>
              </div>
              <div className="text-center">
                <div className="font-semibold text-blue-600 dark:text-blue-400">{data.imQueueSending}</div>
                <div className="text-gray-500 dark:text-slate-400">{t('dashboard.modules.sending')}</div>
              </div>
              <div className="text-center">
                <div className="font-semibold text-green-600 dark:text-green-400">{data.imQueueSentToday}</div>
                <div className="text-gray-500 dark:text-slate-400">{t('dashboard.modules.sentToday')}</div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4 sm:gap-6">
        <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 hover:shadow-md transition-shadow">
          <h3 className="font-semibold text-gray-900 dark:text-slate-100 mb-4 flex items-center gap-2">
            <svg className="w-5 h-5 text-gray-400 dark:text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M11 3.055A9.001 9.001 0 1020.945 13H11V3.055z" />
            </svg>
            {t('dashboard.charts.statusDistribution')}
          </h3>
          <StatusPieChart slices={pieSlices} />
        </div>

        <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 hover:shadow-md transition-shadow">
          <h3 className="font-semibold text-gray-900 dark:text-slate-100 mb-4 flex items-center gap-2">
            <svg className="w-5 h-5 text-gray-400 dark:text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z" />
            </svg>
            {t('dashboard.charts.userWorkload')}
          </h3>
          <div className="space-y-3">
            {data.userWorkloads.length === 0 ? (
              <p className="text-sm text-gray-500 dark:text-slate-400 text-center py-4">{t('common.empty')}</p>
            ) : (
              data.userWorkloads.map((w) => (
                <div key={w.userId} className="flex items-center justify-between py-2.5 border-b border-gray-50 dark:border-slate-700/50 last:border-0 hover:bg-gray-50 dark:hover:bg-slate-700 rounded-lg px-2 -mx-2 transition">
                  <div className="flex items-center gap-3 min-w-0">
                    <UserAvatar userId={w.userId} name={w.userName} size="sm" />
                    <span className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate">{w.userName}</span>
                  </div>
                  <div className="flex gap-3 text-xs flex-shrink-0">
                    <div className="text-center min-w-[48px]">
                      <div className="font-semibold text-gray-900 dark:text-slate-100">{w.activeCount}</div>
                      <div className="text-gray-500 dark:text-slate-400">{t('dashboard.status.inProgress')}</div>
                    </div>
                    <div className="text-center min-w-[48px]">
                      <div className="font-semibold text-green-600 dark:text-green-400">{w.closedToday}</div>
                      <div className="text-gray-500 dark:text-slate-400">{t('dashboard.status.closed')}</div>
                    </div>
                    <div className="text-center min-w-[48px]">
                      <div className="font-semibold text-blue-600 dark:text-blue-400">{w.repliesToday}</div>
                      <div className="text-gray-500 dark:text-slate-400">{t('dashboard.status.replies')}</div>
                    </div>
                  </div>
                </div>
              ))
            )}
          </div>
        </div>

        <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 hover:shadow-md transition-shadow">
          <h3 className="font-semibold text-gray-900 dark:text-slate-100 mb-4 flex items-center gap-2">
            <svg className="w-5 h-5 text-gray-400 dark:text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
            </svg>
            {t('dashboard.charts.mailboxStats')}
          </h3>
          <div className="space-y-3">
            {data.mailboxStats.length === 0 ? (
              <p className="text-sm text-gray-500 dark:text-slate-400 text-center py-4">{t('common.empty')}</p>
            ) : (
              data.mailboxStats.map((m) => (
                <div key={m.mailboxId} className="flex items-center justify-between py-2.5 border-b border-gray-50 dark:border-slate-700/50 last:border-0 hover:bg-gray-50 dark:hover:bg-slate-700 rounded-lg px-2 -mx-2 transition">
                  <div className="flex items-center gap-2 min-w-0">
                    <svg className="w-4 h-4 text-gray-400 dark:text-slate-400 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
                    </svg>
                    <span className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate">{m.mailboxName}</span>
                  </div>
                  <div className="flex gap-3 text-xs flex-shrink-0">
                    <div className="text-center min-w-[40px]">
                      <div className="font-semibold text-gray-900 dark:text-slate-100">{m.activeCount}</div>
                      <div className="text-gray-500 dark:text-slate-400">{t('dashboard.status.inProgress')}</div>
                    </div>
                    <div className="text-center min-w-[40px]">
                      <div className="font-semibold text-amber-600 dark:text-amber-400">{m.pendingCount}</div>
                      <div className="text-gray-500 dark:text-slate-400">{t('dashboard.status.pending')}</div>
                    </div>
                    <div className="text-center min-w-[40px]">
                      <div className="font-semibold text-gray-500 dark:text-slate-400">{m.closedCount}</div>
                      <div className="text-gray-500 dark:text-slate-400">{t('dashboard.status.closed')}</div>
                    </div>
                  </div>
                </div>
              ))
            )}
          </div>
        </div>
      </div>

      <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 hover:shadow-md transition-shadow">
        <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4 mb-4">
          <h3 className="font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2">
            <svg className="w-5 h-5 text-gray-400 dark:text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M13 7h8m0 0v8m0-8l-8 8-4-4-6 6" />
            </svg>
            {t('dashboard.charts.conversationTrend')}
          </h3>
          <div className="flex gap-4 text-xs text-gray-600 dark:text-slate-300">
            <div className="flex items-center gap-1.5">
              <div className="w-3 h-3 bg-indigo-500 rounded-full" />
              <span>{t('dashboard.charts.newConversations', { count: totalNewIn7Days })}</span>
            </div>
            <div className="flex items-center gap-1.5">
              <div className="w-3 h-3 bg-gray-400 rounded-full" />
              <span>{t('dashboard.charts.closedConversations', { count: totalClosedIn7Days })}</span>
            </div>
          </div>
        </div>
        <TrendLineChart data={data.conversationTrend} />
      </div>

      <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 hover:shadow-md transition-shadow">
        <div className="flex items-center justify-between mb-4">
          <h3 className="font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2">
            <svg className="w-5 h-5 text-gray-400 dark:text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M8 10h.01M12 10h.01M16 10h.01M9 16H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-5l-5 5v-5z" />
            </svg>
            {t('dashboard.recent.title')}
          </h3>
          <Link to="/conversations" className="text-sm text-primary-600 dark:text-primary-400 hover:text-primary-700 dark:hover:text-primary-300 font-medium flex items-center gap-1">
            {t('dashboard.viewAll')}
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M17 8l4 4m0 0l-4 4m4-4H3" />
            </svg>
          </Link>
        </div>
        {recentConversations.length === 0 ? (
          <p className="text-sm text-gray-500 dark:text-slate-400 text-center py-8">{t('dashboard.recent.empty')}</p>
        ) : (
          <div className="space-y-2">
            {recentConversations.map((c) => (
              <Link
                key={c.id}
                to={`/conversations/${c.id}`}
                className="flex items-center gap-3 p-2 sm:p-3 hover:bg-gray-50 dark:hover:bg-slate-700 rounded-xl transition group border border-transparent hover:border-gray-100 dark:hover:border-slate-700"
              >
                {c.customer?.avatar ? (
                  <img
                    src={safeUrl(c.customer.avatar)}
                    alt={c.customer?.fullName || t('dashboard.customerAvatarAlt')}
                    className="w-8 h-8 sm:w-9 sm:h-9 rounded-full object-cover flex-shrink-0 border border-gray-200 dark:border-slate-600"
                  />
                ) : (
                  <div className="w-8 h-8 sm:w-9 sm:h-9 bg-gray-100 dark:bg-slate-700 text-gray-600 dark:text-slate-300 rounded-full flex items-center justify-center text-sm font-semibold flex-shrink-0 group-hover:bg-primary-100 dark:group-hover:bg-primary-900/30 group-hover:text-primary-700 dark:group-hover:text-primary-400 transition">
                    {c.customer?.fullName?.charAt(0) || c.customerEmail?.charAt(0) || '?'}
                  </div>
                )}
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate group-hover:text-primary-700 dark:group-hover:text-primary-400">
                      {c.subject || t('dashboard.noSubject')}
                    </span>
                    {c.hasAttachments && (
                      <span className="text-xs text-gray-400 dark:text-slate-500">📎</span>
                    )}
                  </div>
                  <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 truncate">
                    #{c.id} · {c.customerEmail || c.customer?.fullName || '-'}
                  </div>
                </div>
                <div className="text-right flex-shrink-0">
                  <div className="text-xs text-gray-500 dark:text-slate-400">{relativeTime(c.createdAt)}</div>
                  <div className="text-xs text-gray-400 dark:text-slate-500 mt-0.5 hidden sm:block">{formatDateTime(c.createdAt)}</div>
                </div>
              </Link>
            ))}
          </div>
        )}
      </div>

      <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-4 sm:p-5 hover:shadow-md transition-shadow">
        <div className="flex items-center justify-between mb-4">
          <h3 className="font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2">
            <svg className="w-5 h-5 text-gray-400 dark:text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M5 5a2 2 0 012-2h10a2 2 0 012 2v16l-7-3.5L5 21V5z" />
            </svg>
            {t('dashboard.pinnedReports.title')}
          </h3>
          <Link to="/modules/custom-reports" className="text-sm text-indigo-600 hover:text-indigo-800 dark:text-indigo-400 dark:hover:text-indigo-300 font-medium flex items-center gap-1">
            {t('dashboard.viewAll')}
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M17 8l4 4m0 0l-4 4m4-4H3" />
            </svg>
          </Link>
        </div>

        {pinnedReports.length === 0 ? (
          <div className="border-2 border-dashed border-slate-200 dark:border-slate-700 rounded-lg p-8 text-center text-slate-500 dark:text-slate-400">
            <div className="mb-2">
              <svg className="w-12 h-12 mx-auto text-slate-300 dark:text-slate-600 mb-2" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
              </svg>
            </div>
            <p className="text-sm mb-3">{t('dashboard.pinnedReports.empty')}</p>
            <Link
              to="/modules/custom-reports/new"
              className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg transition-colors"
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m8-8H4" />
              </svg>
              {t('dashboard.pinnedReports.create')}
            </Link>
          </div>
        ) : (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            {pinnedReports.map((r) => {
              const vis = VISIBILITY_LABELS_DASH[r.visibility];
              const categoryKey = CATEGORY_LABELS_DASH[r.category];
              return (
                <div
                  key={r.id}
                  onClick={() => navigate(`/modules/custom-reports/view/${r.id}`)}
                  className="bg-white dark:bg-slate-800 rounded-lg border border-slate-100 dark:border-slate-700 p-4 hover:shadow-md hover:border-indigo-200 dark:hover:border-indigo-700 transition-all cursor-pointer flex flex-col min-h-[180px]"
                >
                  <div className="flex items-start justify-between gap-2 mb-2">
                    <div className="flex items-center gap-1.5 min-w-0 flex-1">
                      <span className="text-slate-500 dark:text-slate-400 flex-shrink-0">📊</span>
                      <h4 className="font-semibold text-sm text-slate-900 dark:text-slate-100 truncate">{r.name}</h4>
                    </div>
                    <span className={`inline-flex items-center gap-1 px-1.5 py-0.5 text-[10px] rounded-full font-medium flex-shrink-0 ${vis.color}`}>
                      <span>{vis.icon}</span>
                      <span className="hidden sm:inline">{t(vis.label)}</span>
                    </span>
                  </div>

                  {r.description && (
                    <p className="text-xs text-slate-500 dark:text-slate-400 line-clamp-2 mb-3 min-h-[2rem] flex-shrink-0">
                      {r.description}
                    </p>
                  )}

                  <div className="flex flex-wrap items-center gap-1.5 mb-auto">
                    <span className={`inline-flex items-center gap-1 px-2 py-0.5 text-[10px] rounded-full font-medium ${CATEGORY_COLORS_DASH[r.category] || 'bg-slate-50 text-slate-600 dark:bg-slate-700 dark:text-slate-300'}`}>
                      {categoryKey ? t(categoryKey) : r.category}
                    </span>
                  </div>

                  <div className="flex items-center justify-between pt-3 mt-3 border-t border-slate-100 dark:border-slate-700/50 text-[11px] text-slate-500 dark:text-slate-400">
                    <span className="truncate max-w-[55%]">{r.ownerName || '-'}</span>
                    <span className="flex-shrink-0 ml-2">{formatDateTime(r.updatedAt)}</span>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      <div>
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-base font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2">
            <svg className="w-5 h-5 text-gray-400 dark:text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
            </svg>
            {t('dashboard.resources.title')}
          </h2>
        </div>
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3 sm:gap-4">
          <div className="bg-white dark:bg-slate-800 rounded-xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-3 sm:p-4 text-center hover:shadow-md transition-shadow">
            <div className="w-9 h-9 sm:w-10 sm:h-10 bg-gray-50 dark:bg-slate-700 text-gray-600 dark:text-slate-300 rounded-full flex items-center justify-center mx-auto mb-2 sm:mb-3">
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z" />
              </svg>
            </div>
            <p className="text-xs text-gray-500 dark:text-slate-400">{t('dashboard.resources.totalUsers')}</p>
            <p className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100 mt-1">{data.totalUsers}</p>
          </div>
          <div className="bg-white dark:bg-slate-800 rounded-xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-3 sm:p-4 text-center hover:shadow-md transition-shadow">
            <div className="w-9 h-9 sm:w-10 sm:h-10 bg-gray-50 dark:bg-slate-700 text-gray-600 dark:text-slate-300 rounded-full flex items-center justify-center mx-auto mb-2 sm:mb-3">
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
              </svg>
            </div>
            <p className="text-xs text-gray-500 dark:text-slate-400">{t('dashboard.resources.totalCustomers')}</p>
            <p className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100 mt-1">{data.totalCustomers}</p>
          </div>
          <div className="bg-white dark:bg-slate-800 rounded-xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-3 sm:p-4 text-center hover:shadow-md transition-shadow">
            <div className="w-9 h-9 sm:w-10 sm:h-10 bg-gray-50 dark:bg-slate-700 text-gray-600 dark:text-slate-300 rounded-full flex items-center justify-center mx-auto mb-2 sm:mb-3">
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
              </svg>
            </div>
            <p className="text-xs text-gray-500 dark:text-slate-400">{t('dashboard.resources.totalMailboxes')}</p>
            <p className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100 mt-1">{data.totalMailboxes}</p>
          </div>
          <div className="bg-white dark:bg-slate-800 rounded-xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-3 sm:p-4 text-center hover:shadow-md transition-shadow">
            <div className="w-9 h-9 sm:w-10 sm:h-10 bg-amber-50 dark:bg-amber-900/30 text-amber-600 dark:text-amber-400 rounded-full flex items-center justify-center mx-auto mb-2 sm:mb-3">
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
              </svg>
            </div>
            <p className="text-xs text-gray-500 dark:text-slate-400">{t('dashboard.resources.pendingConversations')}</p>
            <p className="text-xl sm:text-2xl font-bold text-amber-600 dark:text-amber-400 mt-1">{data.pendingConversations}</p>
          </div>
          <div className="bg-white dark:bg-slate-800 rounded-xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-3 sm:p-4 text-center hover:shadow-md transition-shadow">
            <div className="w-9 h-9 sm:w-10 sm:h-10 bg-pink-50 dark:bg-pink-900/30 text-pink-600 dark:text-pink-400 rounded-full flex items-center justify-center mx-auto mb-2 sm:mb-3">
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V5a2 2 0 012-2z" />
              </svg>
            </div>
            <p className="text-xs text-gray-500 dark:text-slate-400">{t('dashboard.resources.totalTags')}</p>
            <p className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100 mt-1">{data.totalTags}</p>
          </div>
          <div className="bg-white dark:bg-slate-800 rounded-xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-3 sm:p-4 text-center hover:shadow-md transition-shadow">
            <div className="w-9 h-9 sm:w-10 sm:h-10 bg-purple-50 dark:bg-purple-900/30 text-purple-600 dark:text-purple-400 rounded-full flex items-center justify-center mx-auto mb-2 sm:mb-3">
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M9 17v-2m3 2v-4m3 4v-6m2 10H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
              </svg>
            </div>
            <p className="text-xs text-gray-500 dark:text-slate-400">{t('dashboard.resources.totalCannedResponses')}</p>
            <p className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100 mt-1">{data.totalCannedResponses}</p>
          </div>
          <div className="bg-white dark:bg-slate-800 rounded-xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-3 sm:p-4 text-center hover:shadow-md transition-shadow">
            <div className="w-9 h-9 sm:w-10 sm:h-10 bg-cyan-50 dark:bg-cyan-900/30 text-cyan-600 dark:text-cyan-400 rounded-full flex items-center justify-center mx-auto mb-2 sm:mb-3">
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101m-.758-4.899a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.1 1.1" />
              </svg>
            </div>
            <p className="text-xs text-gray-500 dark:text-slate-400">Webhook</p>
            <p className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100 mt-1">{data.totalWebhooks}</p>
          </div>
          <div className="bg-white dark:bg-slate-800 rounded-xl shadow-sm border border-gray-100 dark:border-slate-700/50 p-3 sm:p-4 text-center hover:shadow-md transition-shadow">
            <div className="w-9 h-9 sm:w-10 sm:h-10 bg-teal-50 dark:bg-teal-900/30 text-teal-600 dark:text-teal-400 rounded-full flex items-center justify-center mx-auto mb-2 sm:mb-3">
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M4.318 6.318a4.5 4.5 0 000 6.364L12 20.364l7.682-7.682a4.5 4.5 0 00-6.364-6.364L12 7.636l-1.318-1.318a4.5 4.5 0 00-6.364 0z" />
              </svg>
            </div>
            <p className="text-xs text-gray-500 dark:text-slate-400">{t('dashboard.resources.knowledgeLikes')}</p>
            <p className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100 mt-1">{data.knowledgeTotalLikes}</p>
          </div>
        </div>
      </div>
    </div>
  );
}
