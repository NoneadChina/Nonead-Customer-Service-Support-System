import { useEffect, useState, useCallback, useMemo, useRef } from 'react';
import { Link } from 'react-router-dom';
import { sendLogApi, mailboxApi } from '../api';
import type {
  SendLogDto,
  SendLogOverviewResponse,
  SendLogQuery,
  MailboxResponse,
  PagedResponse,
} from '../api/types';
import { useI18n } from '../context/I18nContext';
import { toIntlLocale } from '../i18n/locale';
import type { TranslationKey } from '../locales/zh-Hans';
import { formatDateTime, relativeTime } from '../utils/format';
import Modal from '../components/Modal';
import UserAvatar from '../components/UserAvatar';
import {
  Send,
  Mail,
  CheckCircle2,
  XCircle,
  Clock,
  Download,
  RefreshCw,
  RotateCcw,
  AlertCircle,
  ExternalLink,
  TrendingUp,
  BarChart3,
  Activity,
  Hash,
  Search,
  X,
  Eye,
  ChevronLeft,
  ChevronRight,
  Calendar,
  Inbox,
  Type,
  User2,
  MessageSquare,
  AlertTriangle,
  type LucideIcon,
} from 'lucide-react';

// ====== 状态颜色映射（升级版，与审计日志风格一致）======
interface StatusStyle {
  color: string;
  ring: string;
  dot: string;
  icon: LucideIcon;
}

const STATUS_STYLES: Record<string, StatusStyle> = {
  success: {
    color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400',
    ring: 'ring-emerald-500/20',
    dot: 'bg-emerald-500',
    icon: CheckCircle2,
  },
  failed: {
    color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400',
    ring: 'ring-rose-500/20',
    dot: 'bg-rose-500',
    icon: XCircle,
  },
  pending: {
    color: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400',
    ring: 'ring-amber-500/20',
    dot: 'bg-amber-500',
    icon: Clock,
  },
  warning: {
    color: 'bg-violet-50 text-violet-700 dark:bg-violet-500/10 dark:text-violet-400',
    ring: 'ring-violet-500/20',
    dot: 'bg-violet-500',
    icon: AlertTriangle,
  },
  unknown: {
    color: 'bg-slate-50 text-slate-600 dark:bg-slate-500/10 dark:text-slate-400',
    ring: 'ring-slate-500/20',
    dot: 'bg-slate-400',
    icon: AlertCircle,
  },
};

const getStatusStyle = (category: string): StatusStyle =>
  STATUS_STYLES[category] ?? STATUS_STYLES.unknown;

/**
 * 发送状态码 → 展示文案 key。
 *
 * 后端 `SendLogService.ToDto` 往 DTO 里塞的是**中文标签**
 * （`SendLogService.cs:159` `StatusName = GetStatusLabel(entity.Status)`，标签表在同文件 `:169-181`），
 * 英文界面下会直出中文，因此改为按稳定 code 查前端字典。
 *
 * 码集合 = 后端标签表的 9 个码（`SendLogService.cs:169-181`）
 *        + 状态常量表 `SendLogStatus`（`SendLogRepository.cs:11-45`，其中 5 亦见于
 *          `EmailIngestionService.cs:16-19` 的 `SendLogStatusConst.DeliveryError`）
 *        + DB CHECK 约束允许的 3（`20260921060825_FixSendLogStatusCheckConstraint.cs:53`
 *          `[Status] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)`，旧约束为 `IN (1, 2, 3, 4, 5)` `:66`）。
 *
 * 码 `3` 是**历史状态码**：CHECK 约束允许，但后端 `GetStatusLabel` 没有对应分支，
 * 落到 `_ => "未知"`。这里显式映射到 `status.unknown`，与改造前的渲染**逐字一致**。
 * 用 `TranslationKey | undefined` 而不是 `TranslationKey`：后者在 strict 下会让 else 支
 * 类型上不可达、运行期却可达（类型谎言）。
 */
const STATUS_KEYS: Record<string, TranslationKey | undefined> = {
  '1': 'data.sendLogs.status.received',
  '2': 'data.sendLogs.status.error',
  '3': 'data.sendLogs.status.unknown', // 历史状态码：后端无标签，回落"未知"以保持逐字保真
  '4': 'data.sendLogs.status.delivered',
  '5': 'data.sendLogs.status.failed',
  '6': 'data.sendLogs.status.opened',
  '7': 'data.sendLogs.status.clicked',
  '8': 'data.sendLogs.status.unsubscribed',
  '9': 'data.sendLogs.status.complained',
  '10': 'data.sendLogs.status.interrupted',
};

/**
 * 邮件类型码 → 展示文案 key。
 *
 * 来源 `SendLogService.GetMailTypeLabel`（`SendLogService.cs:183-195`）与
 * `SendLogMailType.GetLabel`（`SendLogRepository.cs:51-76`），值域 **1,2,3,4,5,6,7,8,10（没有 9）**。
 * DTO 字段 `mailTypeName`（`SendLogService.cs:157`）同样是中文，改走前端字典。
 */
const MAIL_TYPE_KEYS: Record<string, TranslationKey | undefined> = {
  '1': 'data.sendLogs.mailType.customerReply',
  '2': 'data.sendLogs.mailType.userNotification',
  '3': 'data.sendLogs.mailType.autoReply',
  '4': 'data.sendLogs.mailType.invite',
  '5': 'data.sendLogs.mailType.passwordChanged',
  '6': 'data.sendLogs.mailType.wrongUserEmail',
  '7': 'data.sendLogs.mailType.test',
  '8': 'data.sendLogs.mailType.alert',
  '10': 'data.sendLogs.mailType.calendarReminder',
};

/**
 * 两个筛选下拉的码**顺序**（值仍是原样的码，只是 label 改由前端字典渲染）。
 *
 * 顺序取自后端字典的插入顺序：状态 `SendLogService.cs:113-124`（1,2,4,5,6,7,8,9,10）、
 * 类型 `SendLogService.cs:130-141`（1,2,3,4,5,6,7,8,10）。改用前端列表后，本页不再请求
 * 那两个**返回中文标签**的字典端点（`GetStatusOptionsAsync` `:111-126` /
 * `GetMailTypeOptionsAsync` `:128-143`），筛选值（码）与提交给 API 的参数都不变。
 */
const STATUS_FILTER_CODES = ['1', '2', '4', '5', '6', '7', '8', '9', '10'] as const;
const MAIL_TYPE_FILTER_CODES = ['1', '2', '3', '4', '5', '6', '7', '8', '10'] as const;

// ====== 通用样式常量 ======
const inputBase =
  'w-full px-3.5 py-2 text-sm bg-white dark:bg-slate-900/80 border border-gray-200 dark:border-slate-700 rounded-xl text-gray-800 dark:text-slate-200 placeholder:text-gray-400 dark:placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-400 dark:focus:border-primary-500 shadow-sm transition-all';
const selectBase =
  'px-3.5 py-2 text-sm bg-white dark:bg-slate-900/80 border border-gray-200 dark:border-slate-700 rounded-xl text-gray-800 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-400 dark:focus:border-primary-500 shadow-sm transition-all cursor-pointer';

// ====== 趋势图（升级版，带渐变填充和网格线）======
function TrendChart({
  data,
}: {
  data: { date: string; totalCount: number; successCount: number; failedCount: number }[];
}) {
  const { t, language } = useI18n();
  const width = 880;
  const height = 240;
  const padding = { top: 16, right: 24, bottom: 32, left: 36 };
  const chartWidth = width - padding.left - padding.right;
  const chartHeight = height - padding.top - padding.bottom;

  if (!data || data.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center h-40 text-sm text-gray-400 dark:text-slate-500">
        <TrendingUp className="w-8 h-8 mb-2 opacity-30" />
        {t('data.common.noTrendData')}
      </div>
    );
  }

  const maxCount = Math.max(...data.flatMap((d) => [d.totalCount, d.successCount, d.failedCount]), 1);
  const stepX = data.length > 1 ? chartWidth / (data.length - 1) : 0;

  const buildPath = (key: 'totalCount' | 'successCount' | 'failedCount') =>
    data
      .map((d, i) => {
        const x = padding.left + i * stepX;
        const y = padding.top + chartHeight - (d[key] / maxCount) * chartHeight;
        return `${i === 0 ? 'M' : 'L'} ${x} ${y}`;
      })
      .join(' ');

  // 渐变填充路径
  const buildAreaPath = (key: 'totalCount' | 'successCount' | 'failedCount') => {
    const line = data
      .map((d, i) => {
        const x = padding.left + i * stepX;
        const y = padding.top + chartHeight - (d[key] / maxCount) * chartHeight;
        return `${i === 0 ? 'M' : 'L'} ${x} ${y}`;
      })
      .join(' ');
    if (data.length === 0) return '';
    const lastX = padding.left + (data.length - 1) * stepX;
    const baseY = padding.top + chartHeight;
    return `${line} L ${lastX} ${baseY} L ${padding.left} ${baseY} Z`;
  };

  const totalPath = buildPath('totalCount');
  const successPath = buildPath('successCount');
  const failedPath = buildPath('failedCount');
  const totalArea = buildAreaPath('totalCount');

  const yTicks = 4;
  const yTickValues = Array.from({ length: yTicks + 1 }, (_, i) => Math.round((maxCount / yTicks) * i));
  const labelInterval = data.length > 30 ? Math.ceil(data.length / 10) : 1;

  return (
    <div className="w-full overflow-x-auto">
      <svg width={width} height={height} className="min-w-full" style={{ minWidth: 560 }}>
        <defs>
          <linearGradient id="totalArea" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#3b82f6" stopOpacity="0.25" />
            <stop offset="100%" stopColor="#3b82f6" stopOpacity="0" />
          </linearGradient>
        </defs>
        {yTickValues.map((tick, i) => {
          const y = padding.top + chartHeight - (tick / maxCount) * chartHeight;
          return (
            <g key={i}>
              <line
                x1={padding.left}
                y1={y}
                x2={width - padding.right}
                y2={y}
                stroke="currentColor"
                strokeWidth={1}
                className="text-gray-100 dark:text-slate-700/50"
              />
              <text x={padding.left - 8} y={y + 4} textAnchor="end" className="text-[10px] fill-gray-400 dark:fill-slate-500">
                {tick}
              </text>
            </g>
          );
        })}
        {data.map((d, i) => {
          if (i % labelInterval !== 0 && i !== data.length - 1) return null;
          const x = padding.left + i * stepX;
          const label = new Date(d.date).toLocaleDateString(toIntlLocale(language), { month: '2-digit', day: '2-digit' });
          return (
            <text key={i} x={x} y={height - 10} textAnchor="middle" className="text-[10px] fill-gray-500 dark:fill-slate-400">
              {label}
            </text>
          );
        })}
        <path d={totalArea} fill="url(#totalArea)" />
        <path d={totalPath} fill="none" stroke="#3b82f6" strokeWidth={2.5} strokeLinecap="round" strokeLinejoin="round" />
        <path d={successPath} fill="none" stroke="#10b981" strokeWidth={2} strokeDasharray="4 3" strokeLinecap="round" />
        <path d={failedPath} fill="none" stroke="#ef4444" strokeWidth={2} strokeDasharray="2 2" strokeLinecap="round" />
      </svg>
      <div className="flex items-center justify-center gap-5 mt-2 text-xs text-gray-600 dark:text-slate-400">
        <span className="flex items-center gap-1.5">
          <span className="w-3 h-0.5 bg-blue-500"></span>{t('data.sendLogs.trend.total')}
        </span>
        <span className="flex items-center gap-1.5">
          <span className="w-3 h-0.5 bg-emerald-500" style={{ borderTop: '2px dashed #10b981' }}></span>{t('data.sendLogs.trend.success')}
        </span>
        <span className="flex items-center gap-1.5">
          <span className="w-3 h-0.5 bg-red-500" style={{ borderTop: '2px dashed #ef4444' }}></span>{t('data.sendLogs.trend.failed')}
        </span>
      </div>
    </div>
  );
}

// ====== 邮箱负载柱状图（升级版，双色分段）======
function MailboxBars({
  data,
}: {
  data: { mailboxName: string; email: string; totalCount: number; successRate: number; failedCount: number }[];
}) {
  const { t } = useI18n();
  if (!data || data.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center h-32 text-sm text-gray-400 dark:text-slate-500">
        <BarChart3 className="w-8 h-8 mb-2 opacity-30" />
        {t('common.empty')}
      </div>
    );
  }
  const maxValue = Math.max(...data.map((d) => d.totalCount), 1);
  return (
    <div className="space-y-3">
      {data.map((d, i) => {
        const successCount = d.totalCount - d.failedCount;
        const successPct = d.totalCount > 0 ? (successCount / d.totalCount) * 100 : 0;
        const failedPct = d.totalCount > 0 ? (d.failedCount / d.totalCount) * 100 : 0;
        const widthPct = (d.totalCount / maxValue) * 100;
        return (
          <div key={i} className="group">
            <div className="flex items-center justify-between text-xs mb-1.5">
              <div className="flex items-center gap-1.5 min-w-0">
                <Mail className="w-3 h-3 text-gray-400 dark:text-slate-500 flex-shrink-0" />
                <span className="text-gray-700 dark:text-slate-300 truncate font-medium">{d.mailboxName}</span>
                <span className="text-gray-400 dark:text-slate-500 truncate font-mono text-[11px]">{d.email}</span>
              </div>
              <div className="flex items-center gap-2 flex-shrink-0">
                <span className="font-bold text-gray-900 dark:text-slate-100 tabular-nums">{d.totalCount}</span>
                {d.failedCount > 0 && (
                  <span className="text-rose-600 dark:text-rose-400 font-medium tabular-nums">{t('data.sendLogs.mailboxBars.failed', { count: d.failedCount })}</span>
                )}
                <span className="text-gray-500 dark:text-slate-400 w-12 text-right tabular-nums">{d.successRate.toFixed(1)}%</span>
              </div>
            </div>
            <div className="h-2 bg-gray-100 dark:bg-slate-700/50 rounded-full overflow-hidden flex">
              <div
                className="h-full bg-gradient-to-r from-emerald-500 to-teal-500 transition-all duration-500 group-hover:from-emerald-400 group-hover:to-teal-400"
                style={{ width: `${(widthPct * successPct) / 100}%` }}
              />
              <div
                className="h-full bg-gradient-to-r from-rose-500 to-red-500 transition-all duration-500 group-hover:from-rose-400 group-hover:to-red-400"
                style={{ width: `${(widthPct * failedPct) / 100}%` }}
              />
            </div>
          </div>
        );
      })}
    </div>
  );
}

// ====== 通用空态 ======
function EmptyState({ icon: Icon, title, subtitle }: { icon: LucideIcon; title: string; subtitle: string }) {
  return (
    <div className="p-14 text-center text-gray-500 dark:text-slate-400">
      <div className="mx-auto w-16 h-16 rounded-2xl bg-gray-50 dark:bg-slate-700/50 flex items-center justify-center mb-3">
        <Icon className="w-8 h-8 text-gray-300 dark:text-slate-600" />
      </div>
      <div className="font-medium text-gray-700 dark:text-slate-300">{title}</div>
      <div className="text-xs text-gray-400 dark:text-slate-500 mt-1">{subtitle}</div>
    </div>
  );
}

// ====== 主页面 ======
export default function SendLogs() {
  const { t } = useI18n();
  const [overview, setOverview] = useState<SendLogOverviewResponse | null>(null);
  const [list, setList] = useState<PagedResponse<SendLogDto> | null>(null);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [listLoading, setListLoading] = useState(false);
  const [refreshLock, setRefreshLock] = useState(false);
  const isRefreshing = loading || listLoading || refreshLock;
  const [error, setError] = useState('');

  const [query, setQuery] = useState<SendLogQuery>(() => {
    const end = new Date();
    const start = new Date();
    start.setDate(start.getDate() - 29);
    const fmt = (d: Date) => d.toISOString().slice(0, 10);
    return { page: 1, pageSize: 20, startDate: fmt(start), endDate: fmt(end) };
  });

  // 关键词搜索：缓冲值即时回显 + 650ms 防抖自动搜索 + 回车即时触发（与 ApiKeys 一致）
  const [keywordInput, setKeywordInput] = useState('');
  const keywordTimerRef = useRef<number | null>(null);
  const applyKeyword = useCallback((value: string) => {
    setQuery((q) => ({ ...q, page: 1, keyword: value || null }));
  }, []);

  const handleKeywordInput = useCallback((value: string) => {
    setKeywordInput(value);
    // 输入停顿 650ms 后自动搜索（期间继续输入会重置计时器）
    if (keywordTimerRef.current !== null) window.clearTimeout(keywordTimerRef.current);
    keywordTimerRef.current = window.setTimeout(() => applyKeyword(value), 650);
  }, [applyKeyword]);

  // 回车：立即按当前输入触发，并取消挂起的防抖
  const triggerKeywordNow = useCallback(() => {
    if (keywordTimerRef.current !== null) {
      window.clearTimeout(keywordTimerRef.current);
      keywordTimerRef.current = null;
    }
    applyKeyword(keywordInput);
  }, [applyKeyword, keywordInput]);

  // 清空关键词（输入框 X / 重置共用）：取消防抖并同步缓冲值
  const clearKeyword = useCallback(() => {
    if (keywordTimerRef.current !== null) {
      window.clearTimeout(keywordTimerRef.current);
      keywordTimerRef.current = null;
    }
    setKeywordInput('');
    setQuery((q) => ({ ...q, page: 1, keyword: null }));
  }, []);

  useEffect(() => {
    return () => {
      if (keywordTimerRef.current !== null) window.clearTimeout(keywordTimerRef.current);
    };
  }, []);

  /**
   * 状态/类型标签一律按**稳定 code** 查前端字典——后端 `statusName` / `mailTypeName`
   * 是中文标签，直接渲染会在英文界面直出中文（字段名改叫 …Keys 后，漏包 `t()` 的使用点
   * 会被 tsc 以 TS2339 抓出来）。未覆盖的码回落 `…unknown`（"未知"），
   * 与后端 `GetStatusLabel` / `GetMailTypeLabel` 的 `_ => "未知"` 支同义。
   */
  const statusLabel = (code: number): string => {
    const key = STATUS_KEYS[String(code)];
    return key ? t(key) : t('data.sendLogs.status.unknown');
  };
  const mailTypeLabel = (code: number): string => {
    const key = MAIL_TYPE_KEYS[String(code)];
    return key ? t(key) : t('data.sendLogs.mailType.unknown');
  };

  const [detailTarget, setDetailTarget] = useState<SendLogDto | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);

  useEffect(() => {
    mailboxApi
      .listAll()
      .then((resp) => setMailboxes((Array.isArray(resp) ? resp : []) as MailboxResponse[]))
      // 非文案：console 诊断日志，不随界面语言变化（硬编码中文门禁亦豁免 console.* 子树）
      .catch((err: unknown) => console.error('加载邮箱列表失败:', err));
    // 状态/类型下拉原先拉后端字典端点（GetStatusOptionsAsync / GetMailTypeOptionsAsync），
    // 那两个字典返回的是**中文标签**；现改由 STATUS_KEYS / MAIL_TYPE_KEYS 按码渲染，
    // 故不再请求——选项集合与顺序均与后端一致，提交给 API 的仍是同一个码。
  }, []);

  const loadOverview = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const data = await sendLogApi.overview(query.startDate ?? undefined, query.endDate ?? undefined, query.mailboxId);
      setOverview(data);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('data.sendLogs.loadOverviewFailed'));
    } finally {
      setLoading(false);
    }
  }, [query.startDate, query.endDate, query.mailboxId, t]);

  const loadList = useCallback(async () => {
    setListLoading(true);
    try {
      const data = await sendLogApi.list(query);
      setList(data);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('data.sendLogs.loadListFailed'));
    } finally {
      setListLoading(false);
    }
  }, [query, t]);

  useEffect(() => {
    loadOverview();
  }, [loadOverview]);

  useEffect(() => {
    loadList();
  }, [loadList]);

  const handleRefresh = useCallback(() => {
    if (isRefreshing) return;
    setRefreshLock(true);
    try {
      void loadOverview();
      void loadList();
    } finally {
      setTimeout(() => setRefreshLock(false), 400);
    }
  }, [isRefreshing, loadOverview, loadList]);

  const handlePageChange = (page: number) => {
    setQuery((q) => ({ ...q, page }));
  };

  const handleReset = () => {
    // 重置时同步清空关键词缓冲并取消防抖
    if (keywordTimerRef.current !== null) {
      window.clearTimeout(keywordTimerRef.current);
      keywordTimerRef.current = null;
    }
    setKeywordInput('');
    setQuery({ page: 1, pageSize: 20 });
  };

  const handleViewDetail = async (log: SendLogDto) => {
    setDetailTarget(log);
    setDetailLoading(true);
    try {
      const detail = await sendLogApi.get(log.id);
      setDetailTarget(detail);
    } catch {
      // 保留列表数据作为兜底
    } finally {
      setDetailLoading(false);
    }
  };

  const stats = overview?.stats;
  const recentFailures = overview?.recentFailures ?? [];

  // 状态徽章
  const renderStatusBadge = (log: SendLogDto) => {
    const style = getStatusStyle(log.statusCategory);
    const Icon = style.icon;
    return (
      <span
        className={`inline-flex items-center gap-1 px-2 py-1 text-xs font-semibold rounded-lg ring-1 ring-inset ${style.color} ${style.ring} ring-black/5 dark:ring-white/10`}
      >
        <Icon className="w-3.5 h-3.5" />
        {statusLabel(log.status)}
      </span>
    );
  };

  // 导出 CSV
  const handleExportCsv = () => {
    if (!list?.data || list.data.length === 0) return;
    // CSV 单元格统一加引号并转义内部引号：en-US 的 formatDateTime 结果含逗号
    // （如 "9/24/2026, 1:49:40 AM"），不加引号会被拆成额外一列；同理文案标签、
    // 主题与消息 ID 都可能含逗号或引号。表头与数据行使用同一套规则。
    const csvCell = (value: string | number): string => `"${String(value).replace(/"/g, '""')}"`;
    const lines: string[] = [];
    // 表头：每列一个键，join(',') 还原改造前的 'ID,消息ID,收件邮箱,邮件类型,状态,客服,会话主题,发送时间'
    lines.push(
      [
        t('data.sendLogs.csv.id'),
        t('data.sendLogs.csv.messageId'),
        t('data.sendLogs.csv.email'),
        t('data.sendLogs.csv.mailType'),
        t('data.sendLogs.csv.status'),
        t('data.sendLogs.csv.agent'),
        t('data.sendLogs.csv.subject'),
        t('data.sendLogs.csv.sentAt'),
      ]
        .map(csvCell)
        .join(','),
    );
    list.data.forEach((log) => {
      lines.push(
        [
          log.id,
          log.messageId,
          log.email,
          mailTypeLabel(log.mailType),
          statusLabel(log.status),
          log.userName ?? '-',
          log.conversationSubject ?? '-',
          formatDateTime(log.createdAt),
        ]
          .map(csvCell)
          .join(','),
      );
    });
    const csv = '\uFEFF' + lines.join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `sendlogs-${new Date().toISOString().slice(0, 10)}.csv`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  };

  const listAny = list as ({ totalItems?: number; currentPage?: number; totalPages?: number; totalCount?: number; page?: number } | undefined);
  const totalItems = listAny?.totalItems ?? listAny?.totalCount ?? 0;
  const totalPages = listAny?.totalPages ?? 0;
  const currentPage = listAny?.currentPage ?? listAny?.page ?? 1;

  const startDateValue = useMemo(() => query.startDate ?? '', [query.startDate]);
  const endDateValue = useMemo(() => query.endDate ?? '', [query.endDate]);

  // KPI 统计数据
  const kpiData = useMemo(() => {
    return [
      {
        label: t('data.sendLogs.kpi.total'),
        value: stats?.totalCount ?? '-',
        icon: Mail,
        color: 'from-sky-500/15 to-sky-500/0 text-sky-600 dark:text-sky-400',
        sub: t('data.sendLogs.kpi.totalSub'),
      },
      {
        label: t('data.sendLogs.kpi.success'),
        value: stats?.successCount ?? '-',
        icon: CheckCircle2,
        color: 'from-emerald-500/15 to-emerald-500/0 text-emerald-600 dark:text-emerald-400',
        sub: stats ? t('data.sendLogs.kpi.successSub', { rate: stats.successRate }) : undefined,
      },
      {
        label: t('data.sendLogs.kpi.failed'),
        value: stats?.failedCount ?? '-',
        icon: XCircle,
        color: stats && stats.failedCount > 0
          ? 'from-rose-500/15 to-rose-500/0 text-rose-600 dark:text-rose-400'
          : 'from-slate-500/15 to-slate-500/0 text-slate-600 dark:text-slate-400',
        sub: stats && stats.totalCount > 0
          ? t('data.sendLogs.kpi.failedSub', { rate: (100 - stats.successRate).toFixed(1) })
          : undefined,
      },
      {
        label: t('data.sendLogs.kpi.pending'),
        value: stats?.pendingCount ?? '-',
        icon: Clock,
        color: 'from-amber-500/15 to-amber-500/0 text-amber-600 dark:text-amber-400',
        sub: t('data.sendLogs.kpi.pendingSub'),
      },
    ];
  }, [stats, t]);

  return (
    <div className="space-y-5">
      {/* ========== 页头 Hero + KPI ========== */}
      <div className="relative overflow-hidden rounded-2xl border border-gray-200/60 dark:border-slate-700/50 bg-white dark:bg-slate-800/60 shadow-sm">
        <div className="absolute inset-0 bg-gradient-to-br from-primary-500/5 via-transparent to-indigo-500/5 dark:from-primary-500/10 dark:to-indigo-500/5 pointer-events-none" />
        <div className="absolute -top-20 -right-16 w-64 h-64 bg-primary-400/10 dark:bg-primary-500/10 rounded-full blur-3xl pointer-events-none" />
        <div className="absolute -bottom-16 -left-16 w-56 h-56 bg-indigo-400/10 dark:bg-indigo-500/10 rounded-full blur-3xl pointer-events-none" />
        <div className="relative p-4 sm:p-6 flex items-start justify-between gap-4 flex-wrap">
          <div className="flex items-start gap-3 sm:gap-4 min-w-0">
            <div className="relative w-10 h-10 sm:w-12 sm:h-12 shrink-0">
              <div className="absolute inset-0 bg-gradient-to-br from-primary-500 to-indigo-600 rounded-xl shadow-lg shadow-primary-500/25" />
              <div className="absolute inset-0 rounded-xl flex items-center justify-center text-white">
                <Send className="w-5 h-5 sm:w-6 sm:h-6" strokeWidth={2.25} />
              </div>
            </div>
            <div className="min-w-0">
              <div className="flex items-center gap-2 flex-wrap">
                <h1 className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100 tracking-tight">{t('data.sendLogs.title')}</h1>
                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-medium bg-cyan-50 text-cyan-700 dark:bg-cyan-500/10 dark:text-cyan-400 ring-1 ring-inset ring-cyan-500/20">
                  <Activity className="w-3 h-3" /> {t('data.sendLogs.breadcrumb')}
                </span>
              </div>
              <p className="text-xs sm:text-sm text-gray-500 dark:text-slate-400 mt-0.5">{t('data.sendLogs.description')}</p>
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <button
              onClick={handleExportCsv}
              disabled={!list?.data || list.data.length === 0}
              className="inline-flex items-center gap-1.5 px-3 sm:px-4 py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-xl shadow-sm hover:bg-gray-50 dark:hover:bg-slate-700 hover:shadow disabled:opacity-60 disabled:cursor-not-allowed active:scale-[0.98] transition-all duration-200 text-gray-700 dark:text-slate-200"
            >
              <Download className="w-4 h-4" />
              <span className="hidden sm:inline">{t('data.common.exportCsv')}</span>
              <span className="sm:hidden">{t('data.common.export')}</span>
            </button>
            <button
              onClick={handleRefresh}
              disabled={isRefreshing}
              className="inline-flex items-center gap-1.5 px-3 sm:px-4 py-2.5 text-sm font-medium bg-gradient-to-b from-primary-500 to-primary-600 hover:from-primary-400 hover:to-primary-500 text-white rounded-xl shadow-sm shadow-primary-500/20 hover:shadow disabled:opacity-60 disabled:cursor-not-allowed active:scale-[0.98] transition-all duration-200"
            >
              <RefreshCw className={`w-4 h-4 ${isRefreshing ? 'animate-spin' : ''}`} />
              <span className="hidden sm:inline">{t('data.common.refresh')}</span>
            </button>
          </div>
        </div>

        {/* KPI 统计卡片 */}
        <div className="relative grid grid-cols-2 md:grid-cols-4 gap-3 px-4 sm:px-6 pb-4 sm:pb-6">
          {kpiData.map((c, i) => (
            <div
              key={i}
              className={`relative overflow-hidden rounded-xl border border-gray-200/60 dark:border-slate-700/50 bg-gradient-to-br ${c.color} dark:bg-slate-900/40 p-3 sm:p-4 transition hover:shadow-md hover:-translate-y-0.5`}
            >
              <div className="flex items-start justify-between">
                <div className="min-w-0">
                  <div className="text-[10px] sm:text-xs text-gray-500 dark:text-slate-400 font-medium truncate">{c.label}</div>
                  <div className="mt-1 text-lg sm:text-2xl font-bold text-gray-900 dark:text-slate-100 tracking-tight tabular-nums">{c.value}</div>
                  {c.sub && <div className="mt-0.5 text-[10px] sm:text-[11px] text-gray-400 dark:text-slate-500 truncate">{c.sub}</div>}
                </div>
                <div className="p-1.5 sm:p-2 rounded-lg bg-white/80 dark:bg-slate-800/80 ring-1 ring-black/5 dark:ring-white/10 flex-shrink-0">
                  <c.icon className="w-3.5 h-3.5 sm:w-4 sm:h-4" />
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* ========== 趋势图 + 邮箱分布 ========== */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <div className="lg:col-span-2 bg-white dark:bg-slate-800/60 rounded-xl border border-gray-200/60 dark:border-slate-700/50 shadow-sm hover:shadow-md transition p-5">
          <div className="flex items-center justify-between mb-4">
            <div className="flex items-start gap-3">
              <div className="w-9 h-9 rounded-lg bg-blue-50 dark:bg-blue-500/10 flex items-center justify-center flex-shrink-0 ring-1 ring-blue-500/20">
                <TrendingUp className="w-5 h-5 text-blue-600 dark:text-blue-400" />
              </div>
              <div>
                <h3 className="font-semibold text-gray-900 dark:text-slate-100">{t('data.sendLogs.trend.title')}</h3>
                <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{t('data.sendLogs.trend.subtitle')}</p>
              </div>
            </div>
          </div>
          {loading ? (
            <div className="flex items-center justify-center h-40 text-sm text-gray-400 dark:text-slate-500">
              <RefreshCw className="w-5 h-5 mr-2 animate-spin text-primary-400" />
              {t('common.shell.loading')}
            </div>
          ) : (
            <TrendChart data={overview?.dailyStats ?? []} />
          )}
        </div>

        <div className="bg-white dark:bg-slate-800/60 rounded-xl border border-gray-200/60 dark:border-slate-700/50 shadow-sm hover:shadow-md transition p-5">
          <div className="flex items-start gap-3 mb-4">
            <div className="w-9 h-9 rounded-lg bg-indigo-50 dark:bg-indigo-500/10 flex items-center justify-center flex-shrink-0 ring-1 ring-indigo-500/20">
              <BarChart3 className="w-5 h-5 text-indigo-600 dark:text-indigo-400" />
            </div>
            <div>
              <h3 className="font-semibold text-gray-900 dark:text-slate-100">{t('data.sendLogs.mailboxStats.title')}</h3>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{t('data.sendLogs.mailboxStats.subtitle')}</p>
            </div>
          </div>
          {loading ? (
            <div className="flex items-center justify-center h-32 text-sm text-gray-400 dark:text-slate-500">
              <RefreshCw className="w-5 h-5 mr-2 animate-spin text-primary-400" />
              {t('common.shell.loading')}
            </div>
          ) : (
            <MailboxBars data={overview?.mailboxStats ?? []} />
          )}
        </div>
      </div>

      {/* ========== 最近失败记录 ========== */}
      {recentFailures.length > 0 && (
        <div className="bg-white dark:bg-slate-800/60 rounded-xl border border-rose-200/60 dark:border-rose-500/20 shadow-sm p-5">
          <div className="flex items-center justify-between mb-4">
            <div className="flex items-start gap-3">
              <div className="w-9 h-9 rounded-lg bg-rose-50 dark:bg-rose-500/10 flex items-center justify-center flex-shrink-0 ring-1 ring-rose-500/20">
                <AlertCircle className="w-5 h-5 text-rose-600 dark:text-rose-400" />
              </div>
              <div>
                <h3 className="font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2">
                  <span className="w-2 h-2 bg-rose-500 rounded-full animate-pulse"></span>
                  {t('data.sendLogs.recentFailures.title')}
                </h3>
                <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{t('data.sendLogs.recentFailures.subtitle')}</p>
              </div>
            </div>
          </div>
          <div className="space-y-2">
            {recentFailures.slice(0, 5).map((log) => (
              <div
                key={log.id}
                className="flex items-center gap-3 p-3 rounded-xl bg-rose-50/40 dark:bg-rose-500/5 hover:bg-rose-50 dark:hover:bg-rose-500/10 transition border border-rose-100/50 dark:border-rose-500/10"
              >
                {renderStatusBadge(log)}
                <div className="flex-1 min-w-0">
                  <div className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate font-mono">{log.email}</div>
                  <div className="text-xs text-gray-500 dark:text-slate-400 truncate mt-0.5">
                    {log.conversationSubject ?? t('data.sendLogs.noSubject')} · {mailTypeLabel(log.mailType)}
                  </div>
                </div>
                <div className="text-xs text-gray-500 dark:text-slate-400 flex-shrink-0 whitespace-nowrap" title={formatDateTime(log.createdAt)}>
                  {relativeTime(log.createdAt)}
                </div>
                <button
                  onClick={() => handleViewDetail(log)}
                  className="inline-flex items-center gap-1 text-xs font-medium text-rose-600 hover:text-rose-700 dark:text-rose-400 dark:hover:text-rose-300 hover:underline underline-offset-2 flex-shrink-0 transition"
                >
                  <Eye className="w-3.5 h-3.5" />
                  {t('data.sendLogs.rowAction.details')}
                </button>
                {log.conversationId && (
                  <Link
                    to={`/conversations/${log.conversationId}`}
                    className="inline-flex items-center gap-1 text-xs text-primary-600 hover:text-primary-700 dark:text-primary-400 dark:hover:text-primary-300 flex-shrink-0"
                  >
                    <ExternalLink className="w-3.5 h-3.5" />
                    {t('data.sendLogs.rowAction.conversation')}
                  </Link>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ========== 筛选条 ========== */}
      <div className="bg-white dark:bg-slate-800/60 rounded-xl border border-gray-200/60 dark:border-slate-700/50 shadow-sm p-4">
        <div className="grid grid-cols-2 md:flex md:flex-wrap items-end gap-3">
          <div className="min-w-0">
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1 flex items-center gap-1">
              <Calendar className="w-3 h-3" />{t('data.common.startDate')}
            </label>
            <input
              type="date"
              value={startDateValue}
              onChange={(e) => setQuery((q) => ({ ...q, page: 1, startDate: e.target.value || null }))}
              className={`${selectBase} w-full`}
            />
          </div>
          <div className="min-w-0">
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1 flex items-center gap-1">
              <Calendar className="w-3 h-3" />{t('data.common.endDate')}
            </label>
            <input
              type="date"
              value={endDateValue}
              onChange={(e) => setQuery((q) => ({ ...q, page: 1, endDate: e.target.value || null }))}
              className={`${selectBase} w-full`}
            />
          </div>
          <div className="min-w-0">
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1 flex items-center gap-1">
              <Inbox className="w-3 h-3" />{t('data.sendLogs.filter.mailbox')}
            </label>
            <select
              value={query.mailboxId ?? ''}
              onChange={(e) => setQuery((q) => ({ ...q, page: 1, mailboxId: e.target.value === '' ? null : Number(e.target.value) }))}
              className={`${selectBase} w-full`}
            >
              <option value="">{t('data.common.allMailboxes')}</option>
              {mailboxes.map((m) => (
                <option key={m.id} value={m.id}>{m.name}</option>
              ))}
            </select>
          </div>
          <div className="min-w-0">
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1 flex items-center gap-1">
              <Activity className="w-3 h-3" />{t('data.sendLogs.field.status')}
            </label>
            <select
              value={query.status ?? ''}
              onChange={(e) => setQuery((q) => ({ ...q, page: 1, status: e.target.value === '' ? null : Number(e.target.value) }))}
              className={`${selectBase} w-full`}
            >
              <option value="">{t('data.sendLogs.filter.allStatuses')}</option>
              {STATUS_FILTER_CODES.map((code) => (
                <option key={code} value={code}>{statusLabel(Number(code))}</option>
              ))}
            </select>
          </div>
          <div className="min-w-0">
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1 flex items-center gap-1">
              <Type className="w-3 h-3" />{t('data.sendLogs.field.type')}
            </label>
            <select
              value={query.mailType ?? ''}
              onChange={(e) => setQuery((q) => ({ ...q, page: 1, mailType: e.target.value === '' ? null : Number(e.target.value) }))}
              className={`${selectBase} w-full`}
            >
              <option value="">{t('data.common.allTypes')}</option>
              {MAIL_TYPE_FILTER_CODES.map((code) => (
                <option key={code} value={code}>{mailTypeLabel(Number(code))}</option>
              ))}
            </select>
          </div>
          <div className="flex-1 min-w-0 md:min-w-[200px] col-span-2 md:col-span-1">
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1 flex items-center gap-1">
              <Search className="w-3 h-3" />{t('data.sendLogs.filter.keyword')}
            </label>
            <div className="relative">
              <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400 dark:text-slate-500 pointer-events-none" />
              <input
                type="text"
                value={keywordInput}
                onChange={(e) => handleKeywordInput(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') triggerKeywordNow(); }}
                placeholder={t('data.sendLogs.filter.keywordPlaceholder')}
                className={`${inputBase} pl-9`}
              />
              {query.keyword && (
                <button
                  onClick={clearKeyword}
                  className="absolute right-2 top-1/2 -translate-y-1/2 p-1 rounded-md hover:bg-gray-100 dark:hover:bg-slate-700 text-gray-400 dark:text-slate-500"
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              )}
            </div>
          </div>
          <button
            onClick={handleReset}
            className="inline-flex items-center justify-center gap-1.5 px-3 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-200 rounded-xl shadow-sm hover:bg-gray-50 dark:hover:bg-slate-700 active:scale-[0.98] transition col-span-2 md:col-span-1"
          >
            <RotateCcw className="w-4 h-4" />
            {t('common.reset')}
          </button>
        </div>
      </div>

      {error && (
        <div className="bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/20 text-rose-700 dark:text-rose-400 px-4 py-3 rounded-xl text-sm flex items-start gap-2">
          <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" />
          <div>{t('data.common.loadFailedPrefix')}{error}</div>
        </div>
      )}

      {/* ========== 日志列表表格 ========== */}
      <div className="bg-white dark:bg-slate-800/60 rounded-xl border border-gray-200/60 dark:border-slate-700/50 shadow-sm overflow-hidden">
        <div className="px-5 py-3 border-b border-gray-100 dark:border-slate-700/50 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <h3 className="font-semibold text-gray-900 dark:text-slate-100">{t('data.sendLogs.listTitle')}</h3>
            <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[11px] font-bold rounded-full bg-primary-50 dark:bg-primary-500/10 text-primary-700 dark:text-primary-400 ring-1 ring-inset ring-primary-500/20 tabular-nums">
              {totalItems}
            </span>
          </div>
        </div>
        {listLoading ? (
          <div className="p-10 text-center text-gray-500 dark:text-slate-400">
            <RefreshCw className="w-8 h-8 mx-auto mb-2 animate-spin text-primary-400" />
            <div>{t('common.shell.loading')}</div>
          </div>
        ) : !list?.data || list.data.length === 0 ? (
          <EmptyState
            icon={Mail}
            title={t('data.sendLogs.listEmptyTitle')}
            subtitle={t('data.sendLogs.listEmptySubtitle')}
          />
        ) : (
          <>
            {/* PC 表格视图 */}
            <div className="hidden md:block overflow-x-auto">
              <table className="min-w-full divide-y divide-gray-100 dark:divide-slate-700/50">
                <thead className="bg-gray-50/70 dark:bg-slate-900/60 backdrop-blur-sm">
                  <tr>
                    <th className="px-4 py-2.5 text-left text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">ID</th>
                    <th className="px-4 py-2.5 text-left text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('data.sendLogs.field.status')}</th>
                    <th className="px-4 py-2.5 text-left text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('data.sendLogs.field.email')}</th>
                    <th className="px-4 py-2.5 text-left text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('data.sendLogs.field.type')}</th>
                    <th className="px-4 py-2.5 text-left text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('data.sendLogs.field.conversationSubject')}</th>
                    <th className="px-4 py-2.5 text-left text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('data.sendLogs.field.agent')}</th>
                    <th className="px-4 py-2.5 text-left text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('data.sendLogs.field.sentAt')}</th>
                    <th className="px-4 py-2.5 text-left text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('data.sendLogs.field.actions')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100 dark:divide-slate-700/50">
                  {list.data.map((log) => {
                    const style = getStatusStyle(log.statusCategory);
                    const StatusIcon = style.icon;
                    return (
                      <tr key={log.id} className="group hover:bg-gray-50/70 dark:hover:bg-slate-700/30 transition">
                        <td className="px-4 py-3 text-xs text-gray-500 dark:text-slate-400 font-mono tabular-nums">
                          <span className="text-gray-400 dark:text-slate-500">#</span>{log.id}
                        </td>
                        <td className="px-4 py-3">
                          <span className={`inline-flex items-center gap-1 px-2 py-1 text-xs font-semibold rounded-lg ring-1 ring-inset ${style.color} ${style.ring} ring-black/5 dark:ring-white/10`}>
                            <StatusIcon className="w-3.5 h-3.5" />
                            {statusLabel(log.status)}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-sm text-gray-900 dark:text-slate-100 font-mono">
                          <div className="truncate max-w-[180px]" title={log.email}>{log.email}</div>
                        </td>
                        <td className="px-4 py-3 text-sm">
                          <span className="inline-flex items-center gap-1 px-2 py-1 text-xs font-medium rounded-lg bg-slate-50 dark:bg-slate-700/50 text-slate-700 dark:text-slate-300 ring-1 ring-inset ring-slate-500/10">
                            {mailTypeLabel(log.mailType)}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-sm text-gray-700 dark:text-slate-300 max-w-[220px] truncate" title={log.conversationSubject ?? ''}>
                          {log.conversationSubject ?? '-'}
                        </td>
                        <td className="px-4 py-3 text-sm text-gray-700 dark:text-slate-300">
                          {log.userName ?? <span className="text-gray-400 dark:text-slate-500">-</span>}
                        </td>
                        <td className="px-4 py-3 whitespace-nowrap">
                          <div className="text-sm text-gray-800 dark:text-slate-200 font-medium" title={formatDateTime(log.createdAt)}>
                            {relativeTime(log.createdAt)}
                          </div>
                          <div className="text-[11px] text-gray-400 dark:text-slate-500">{formatDateTime(log.createdAt)}</div>
                        </td>
                        <td className="px-4 py-3 text-sm">
                          <div className="flex items-center gap-1">
                            <button
                              onClick={() => handleViewDetail(log)}
                              className="inline-flex items-center justify-center w-8 h-8 rounded-lg text-primary-600 hover:text-primary-700 hover:bg-primary-50 dark:text-primary-400 dark:hover:text-primary-300 dark:hover:bg-primary-500/10 transition"
                              title={t('data.common.viewDetails')}
                            >
                              <Eye className="w-4 h-4" />
                            </button>
                            {log.conversationId && (
                              <Link
                                to={`/conversations/${log.conversationId}`}
                                className="inline-flex items-center justify-center w-8 h-8 rounded-lg text-cyan-600 hover:text-cyan-700 hover:bg-cyan-50 dark:text-cyan-400 dark:hover:text-cyan-300 dark:hover:bg-cyan-500/10 transition"
                                title={t('data.sendLogs.viewConversation')}
                              >
                                <ExternalLink className="w-4 h-4" />
                              </Link>
                            )}
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
              {list.data.map((log) => {
                const style = getStatusStyle(log.statusCategory);
                const StatusIcon = style.icon;
                return (
                  <div
                    key={log.id}
                    className="p-3.5 hover:bg-gray-50/70 dark:hover:bg-slate-700/30 transition"
                  >
                    {/* 卡片头部：ID + 状态 + 时间 */}
                    <div className="flex items-center justify-between gap-2 mb-2.5">
                      <div className="flex items-center gap-2 min-w-0">
                        <span className="text-xs text-gray-400 dark:text-slate-500 font-mono">#{log.id}</span>
                        <span className={`inline-flex items-center gap-1 px-1.5 py-0.5 text-[10px] font-semibold rounded ring-1 ring-inset ${style.color} ${style.ring} ring-black/5 dark:ring-white/10`}>
                          <StatusIcon className="w-3 h-3" />
                          {statusLabel(log.status)}
                        </span>
                      </div>
                      <div className="text-right flex-shrink-0">
                        <div className="text-xs text-gray-700 dark:text-slate-200 font-medium">{relativeTime(log.createdAt)}</div>
                        <div className="text-[10px] text-gray-400 dark:text-slate-500">{formatDateTime(log.createdAt)}</div>
                      </div>
                    </div>
                    {/* 收件邮箱 */}
                    <div className="flex items-center gap-1.5 mb-2 min-w-0">
                      <Mail className="w-3.5 h-3.5 text-gray-400 dark:text-slate-500 flex-shrink-0" />
                      <span className="text-sm text-gray-900 dark:text-slate-100 font-mono truncate" title={log.email}>{log.email}</span>
                    </div>
                    {/* 类型 + 客服 */}
                    <div className="flex flex-wrap items-center gap-1.5 mb-2">
                      <span className="inline-flex items-center gap-1 px-1.5 py-0.5 text-[10px] font-medium rounded bg-slate-50 dark:bg-slate-700/50 text-slate-700 dark:text-slate-300 ring-1 ring-inset ring-slate-500/10">
                        <Type className="w-3 h-3" />
                        {mailTypeLabel(log.mailType)}
                      </span>
                      {log.userName && (
                        <span className="inline-flex items-center gap-1 text-[11px] text-gray-700 dark:text-slate-300">
                          <User2 className="w-3 h-3 text-gray-400 dark:text-slate-500" />
                          {log.userName}
                        </span>
                      )}
                    </div>
                    {/* 会话主题 */}
                    {log.conversationSubject && (
                      <div className="flex items-center gap-1.5 mb-2.5 text-[11px] text-gray-500 dark:text-slate-400 min-w-0">
                        <MessageSquare className="w-3 h-3 flex-shrink-0" />
                        <span className="truncate" title={log.conversationSubject}>{log.conversationSubject}</span>
                      </div>
                    )}
                    {/* 操作按钮 */}
                    <div className="flex items-center gap-2">
                      <button
                        onClick={() => handleViewDetail(log)}
                        className="flex-1 inline-flex items-center justify-center gap-1 text-xs font-medium text-primary-600 dark:text-primary-400 bg-primary-50 dark:bg-primary-500/10 hover:bg-primary-100 dark:hover:bg-primary-500/20 py-1.5 rounded-lg transition"
                      >
                        <Eye className="w-3.5 h-3.5" />
                        {t('data.common.viewDetails')}
                      </button>
                      {log.conversationId && (
                        <Link
                          to={`/conversations/${log.conversationId}`}
                          className="flex-1 inline-flex items-center justify-center gap-1 text-xs font-medium text-cyan-600 dark:text-cyan-400 bg-cyan-50 dark:bg-cyan-500/10 hover:bg-cyan-100 dark:hover:bg-cyan-500/20 py-1.5 rounded-lg transition"
                        >
                          <ExternalLink className="w-3.5 h-3.5" />
                          {t('data.sendLogs.viewConversation')}
                        </Link>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>

            {/* 分页 */}
            {totalPages > 1 && (
              <div className="px-3 sm:px-5 py-3 border-t border-gray-100 dark:border-slate-700/50 flex flex-wrap items-center justify-between gap-2 text-sm">
                <div className="text-gray-500 dark:text-slate-400 flex items-center gap-2 flex-wrap text-xs sm:text-sm">
                  <span>
                    {t('data.sendLogs.pagination.pagePrefix')}<span className="font-semibold text-gray-700 dark:text-slate-300 tabular-nums">{currentPage}</span>
                    {totalPages > 0
                      ? t('data.sendLogs.pagination.pageTotal', { total: totalPages })
                      : t('data.sendLogs.pagination.pageSuffix')}
                  </span>
                  {totalItems > 0 && (
                    <span>{t('data.sendLogs.pagination.totalPrefix')}<span className="font-semibold tabular-nums text-gray-700 dark:text-slate-300">{totalItems}</span>{t('data.sendLogs.pagination.totalSuffix')}</span>
                  )}
                </div>
                <div className="flex items-center gap-1">
                  <button
                    onClick={() => handlePageChange(1)}
                    disabled={currentPage <= 1}
                    className="px-2 py-1.5 text-xs sm:text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-200 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 active:scale-[0.97] disabled:opacity-40 disabled:cursor-not-allowed transition"
                  >
                    {t('data.sendLogs.pagination.first')}
                  </button>
                  <button
                    onClick={() => handlePageChange(currentPage - 1)}
                    disabled={currentPage <= 1}
                    className="inline-flex items-center gap-1 px-2 py-1.5 text-xs sm:text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-200 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 active:scale-[0.97] disabled:opacity-40 disabled:cursor-not-allowed transition"
                  >
                    <ChevronLeft className="w-4 h-4" />
                    <span className="hidden sm:inline">{t('data.common.previousPage')}</span>
                  </button>
                  <span className="px-2.5 py-1.5 text-xs sm:text-sm font-medium text-gray-700 dark:text-slate-200 tabular-nums">{currentPage}</span>
                  <button
                    onClick={() => handlePageChange(currentPage + 1)}
                    disabled={currentPage >= totalPages}
                    className="inline-flex items-center gap-1 px-2 py-1.5 text-xs sm:text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-200 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 active:scale-[0.97] disabled:opacity-40 disabled:cursor-not-allowed transition"
                  >
                    <span className="hidden sm:inline">{t('data.common.nextPage')}</span>
                    <ChevronRight className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => handlePageChange(totalPages)}
                    disabled={currentPage >= totalPages}
                    className="px-2 py-1.5 text-xs sm:text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-200 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 active:scale-[0.97] disabled:opacity-40 disabled:cursor-not-allowed transition"
                  >
                    {t('data.sendLogs.pagination.last')}
                  </button>
                </div>
              </div>
            )}
          </>
        )}
      </div>

      {/* ========== 详情弹窗 ========== */}
      <Modal
        open={!!detailTarget}
        title={`${t('data.sendLogs.detailTitlePrefix')}${detailTarget?.id ?? ''}`}
        onClose={() => setDetailTarget(null)}
        size="xl"
        footer={
          <div className="flex items-center gap-2 justify-end">
            {detailTarget && (
              <button
                onClick={() => {
                  const blob = new Blob(
                    [JSON.stringify(detailTarget, null, 2)],
                    { type: 'application/json' },
                  );
                  const url = URL.createObjectURL(blob);
                  const a = document.createElement('a');
                  a.href = url;
                  a.download = `sendlog-${detailTarget.id}.json`;
                  a.click();
                  URL.revokeObjectURL(url);
                }}
                className="inline-flex items-center gap-1.5 px-3 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-200 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 active:scale-[0.98] transition"
              >
                <Download className="w-4 h-4" />
                {t('data.sendLogs.exportJson')}
              </button>
            )}
            <button
              onClick={() => setDetailTarget(null)}
              className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium bg-gradient-to-b from-primary-500 to-primary-600 hover:from-primary-400 hover:to-primary-500 text-white rounded-xl shadow-sm shadow-primary-500/20 active:scale-[0.98] transition"
            >
              {t('common.close')}
            </button>
          </div>
        }
      >
        {detailTarget && (
          <div className="space-y-5">
            {detailLoading && (
              <div className="absolute inset-0 flex items-center justify-center bg-white/60 dark:bg-slate-800/60 backdrop-blur-sm z-10 rounded-2xl">
                <RefreshCw className="w-6 h-6 animate-spin text-primary-500" />
              </div>
            )}
            {/* 基本信息卡片组 */}
            <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
              <div className="p-4 rounded-xl bg-gradient-to-br from-gray-50 to-slate-50 dark:from-slate-900/70 dark:to-slate-800 border border-gray-200/50 dark:border-slate-700/50">
                <div className="text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wider mb-1.5 flex items-center gap-1">
                  <Clock className="w-3 h-3" />{t('data.sendLogs.field.sentAt')}
                </div>
                <div className="text-base font-semibold text-gray-900 dark:text-slate-100">{formatDateTime(detailTarget.createdAt)}</div>
                <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{relativeTime(detailTarget.createdAt)}</div>
              </div>
              <div className="p-4 rounded-xl bg-gradient-to-br from-gray-50 to-slate-50 dark:from-slate-900/70 dark:to-slate-800 border border-gray-200/50 dark:border-slate-700/50">
                <div className="text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wider mb-1.5 flex items-center gap-1">
                  <Activity className="w-3 h-3" />{t('data.sendLogs.detail.status')}
                </div>
                {(() => {
                  const style = getStatusStyle(detailTarget.statusCategory);
                  const Icon = style.icon;
                  return (
                    <span className={`inline-flex items-center gap-1.5 px-2.5 py-1.5 text-sm font-semibold rounded-xl ring-1 ring-inset ${style.color} ${style.ring} ring-black/5 dark:ring-white/10`}>
                      <Icon className="w-4 h-4" />
                      {statusLabel(detailTarget.status)}
                    </span>
                  );
                })()}
              </div>
              <div className="p-4 rounded-xl bg-gradient-to-br from-gray-50 to-slate-50 dark:from-slate-900/70 dark:to-slate-800 border border-gray-200/50 dark:border-slate-700/50">
                <div className="text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wider mb-1.5 flex items-center gap-1">
                  <Mail className="w-3 h-3" />{t('data.sendLogs.field.email')}
                </div>
                <div className="text-base font-semibold text-gray-900 dark:text-slate-100 font-mono break-all">{detailTarget.email}</div>
                {detailTarget.customerEmail && detailTarget.customerEmail !== detailTarget.email && (
                  <div className="text-xs text-gray-500 dark:text-slate-400 mt-1 font-mono break-all">{t('data.sendLogs.detail.customer', { email: detailTarget.customerEmail })}</div>
                )}
              </div>
              <div className="p-4 rounded-xl bg-gradient-to-br from-gray-50 to-slate-50 dark:from-slate-900/70 dark:to-slate-800 border border-gray-200/50 dark:border-slate-700/50">
                <div className="text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wider mb-1.5 flex items-center gap-1">
                  <Type className="w-3 h-3" />{t('data.sendLogs.field.mailType')}
                </div>
                <span className="inline-flex items-center gap-1 px-2.5 py-1 text-sm font-semibold rounded-xl bg-slate-100 dark:bg-slate-700/50 text-slate-700 dark:text-slate-300 ring-1 ring-inset ring-slate-500/10">
                  {mailTypeLabel(detailTarget.mailType)}
                </span>
              </div>
            </div>

            {/* 会话与客服上下文 */}
            <div className="p-4 rounded-xl bg-gradient-to-br from-gray-50 to-slate-50 dark:from-slate-900/70 dark:to-slate-800 border border-gray-200/50 dark:border-slate-700/50">
              <div className="text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wider mb-3 flex items-center gap-1">
                <MessageSquare className="w-3 h-3" />{t('data.sendLogs.detail.context')}
              </div>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-sm">
                <div>
                  <div className="text-[11px] text-gray-500 dark:text-slate-400 mb-1">{t('data.sendLogs.field.conversationSubject')}</div>
                  <div className="font-medium text-gray-900 dark:text-slate-100 break-words">
                    {detailTarget.conversationSubject || <span className="text-gray-400 dark:text-slate-500">{t('data.sendLogs.detail.noSubject')}</span>}
                  </div>
                </div>
                <div>
                  <div className="text-[11px] text-gray-500 dark:text-slate-400 mb-1">{t('data.sendLogs.field.agent')}</div>
                  {detailTarget.userId ? (
                    <div className="flex items-center gap-2">
                      <UserAvatar userId={detailTarget.userId} name={detailTarget.userName ?? undefined} size="sm" />
                      <div className="min-w-0">
                        <div className="font-medium text-gray-900 dark:text-slate-100 truncate">
                          {detailTarget.userName || `${t('data.sendLogs.detail.userFallbackPrefix')}${detailTarget.userId}`}
                        </div>
                        <div className="text-xs text-gray-500 dark:text-slate-400 font-mono">ID: {detailTarget.userId}</div>
                      </div>
                    </div>
                  ) : (
                    <div className="flex items-center gap-2">
                      <div className="w-8 h-8 rounded-lg bg-slate-100 dark:bg-slate-700/60 flex items-center justify-center text-slate-500">
                        <User2 className="w-4 h-4" />
                      </div>
                      <div>
                        <div className="font-medium text-gray-900 dark:text-slate-100">{t('data.common.system')}</div>
                        <div className="text-xs text-gray-500 dark:text-slate-400">{t('data.sendLogs.detail.automated')}</div>
                      </div>
                    </div>
                  )}
                </div>
                <div>
                  <div className="text-[11px] text-gray-500 dark:text-slate-400 mb-1">{t('data.sendLogs.detail.conversationId')}</div>
                  {detailTarget.conversationId ? (
                    <Link
                      to={`/conversations/${detailTarget.conversationId}`}
                      onClick={() => setDetailTarget(null)}
                      className="inline-flex items-center gap-1.5 font-medium text-primary-600 hover:text-primary-700 dark:text-primary-400 dark:hover:text-primary-300 hover:underline underline-offset-2 transition"
                    >
                      <ExternalLink className="w-3.5 h-3.5" />
                      #{detailTarget.conversationId}
                    </Link>
                  ) : (
                    <span className="text-gray-400 dark:text-slate-500">-</span>
                  )}
                </div>
                <div>
                  <div className="text-[11px] text-gray-500 dark:text-slate-400 mb-1">{t('data.sendLogs.detail.threadId')}</div>
                  <span className="font-mono text-gray-900 dark:text-slate-100">
                    {detailTarget.threadId ?? <span className="text-gray-400 dark:text-slate-500">-</span>}
                  </span>
                </div>
              </div>
            </div>

            {/* 消息 ID */}
            <div className="p-4 rounded-xl bg-gradient-to-br from-gray-50 to-slate-50 dark:from-slate-900/70 dark:to-slate-800 border border-gray-200/50 dark:border-slate-700/50">
              <div className="text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wider mb-1.5 flex items-center gap-1">
                <Hash className="w-3 h-3" />{t('data.sendLogs.detail.messageId')}
              </div>
              <div className="font-mono text-[11px] text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-900 px-3 py-2 rounded-lg border border-gray-200/60 dark:border-slate-700/50 break-all">
                {detailTarget.messageId}
              </div>
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
}
