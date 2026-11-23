import { useState, useEffect, useMemo, useRef, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { satisfactionApi, mailboxApi, userApi } from '../api';
import type {
  SatisfactionRatingDto,
  SatisfactionRatingStatsDto,
  DailySatisfactionStatsDto,
  MailboxSatisfactionStatsDto,
  UserSatisfactionStatsDto,
  SatisfactionRatingQuery,
  SatisfactionOverviewResponse,
} from '../api/types';
import UserAvatar from '../components/UserAvatar';
import { useToast } from '../context/ToastContext';
import { useSignalREvent } from '../hooks/useSignalR';
import { SignalREvents } from '../services/signalR';
import { useI18n } from '../context/I18nContext';
import { toIntlLocale } from '../i18n/locale';
import type { TranslationKey } from '../locales/zh-Hans';

/** 评分码 → key。后端 `SatisfactionRatingConst` 的码值恰为 1..5，与 `RatingCategory` 五态一一对应。 */
const RATING_CODE_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  '1': 'serviceOps.satisfaction.rating.bad',
  '2': 'serviceOps.satisfaction.rating.poor',
  '3': 'serviceOps.satisfaction.rating.average',
  '4': 'serviceOps.satisfaction.rating.good',
  '5': 'serviceOps.satisfaction.rating.excellent',
};

// 只保留配色；原先的 `label` 字段从未被任何渲染点消费（死字段），标签统一由上表按码取词
const RATING_STYLES: Record<string, { bg: string; text: string; badge: string }> = {
  excellent: { bg: 'bg-green-50 dark:bg-green-900/20', text: 'text-green-700 dark:text-green-300', badge: 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300' },
  good: { bg: 'bg-emerald-50 dark:bg-emerald-900/20', text: 'text-emerald-700 dark:text-emerald-300', badge: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-300' },
  average: { bg: 'bg-amber-50 dark:bg-amber-900/20', text: 'text-amber-700 dark:text-amber-300', badge: 'bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300' },
  poor: { bg: 'bg-orange-50 dark:bg-orange-900/20', text: 'text-orange-700 dark:text-orange-300', badge: 'bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-300' },
  bad: { bg: 'bg-red-50 dark:bg-red-900/20', text: 'text-red-700 dark:text-red-300', badge: 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300' },
};

function StatCard({
  label,
  value,
  subtitle,
  icon,
  colorClass,
  iconBgClass,
}: {
  label: string;
  value: string | number;
  subtitle?: string;
  icon: React.ReactNode;
  colorClass: string;
  iconBgClass: string;
}) {
  return (
    <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-5 shadow-sm hover:shadow-md transition-shadow">
      <div className="flex items-start justify-between">
        <div>
          <div className="text-sm text-gray-500 dark:text-slate-400">{label}</div>
          <div className={`text-3xl font-bold mt-1 ${colorClass}`}>{value}</div>
          {subtitle && <div className="text-xs text-gray-400 dark:text-slate-500 mt-1">{subtitle}</div>}
        </div>
        <div className={`w-10 h-10 rounded-lg flex items-center justify-center ${iconBgClass}`}>
          {icon}
        </div>
      </div>
    </div>
  );
}

function TrendChart({ data }: { data: DailySatisfactionStatsDto[] }) {
  const { t } = useI18n();
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [size, setSize] = useState<{ w: number; h: number }>({ w: 700, h: 320 });

  // 监听容器尺寸变化，动态适配图表宽高
  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;

    const measure = () => {
      const rect = el.getBoundingClientRect();
      if (rect.width > 0 && rect.height > 0) {
        setSize((prev) => {
          const w = Math.round(rect.width);
          const h = Math.round(rect.height);
          if (prev.w === w && prev.h === h) return prev;
          return { w, h };
        });
      }
    };
    measure();

    let frameId: number | null = null;
    const ro = new ResizeObserver(() => {
      if (frameId !== null) return;
      frameId = window.requestAnimationFrame(() => {
        frameId = null;
        measure();
      });
    });
    ro.observe(el);
    return () => {
      ro.disconnect();
      if (frameId !== null) window.cancelAnimationFrame(frameId);
    };
  }, []);

  if (data.length === 0) {
    return (
      <div
        ref={containerRef}
        className="h-[320px] w-full min-w-0 flex items-center justify-center text-gray-400 dark:text-slate-500 text-sm overflow-hidden"
      >{t('common.empty')}</div>
    );
  }

  const { w: width, h: height } = size;
  const padding = { top: 16, right: 40, bottom: 90, left: 40 };
  const chartW = Math.max(width - padding.left - padding.right, 200);
  const chartH = Math.max(height - padding.top - padding.bottom, 160);

  const maxCount = Math.max(...data.map((d) => d.totalCount), 1);

  // 每天最小水平间距（像素）：数据不够时按此间距绘制，右侧留空；数据足够撑满时压缩
  const MIN_DAY_WIDTH = 48;
  const spanCount = Math.max(data.length - 1, 1);
  const idealWidth = spanCount * MIN_DAY_WIDTH;
  const xStep: number =
    idealWidth < chartW ? MIN_DAY_WIDTH : chartW / spanCount;

  // 左轴：数量 0~maxCount；右轴：平均分 0~5
  const scoreMax = 5;

  const buildCountPath = (key: 'excellentCount' | 'goodCount' | 'averageCount' | 'poorCount' | 'badCount') =>
    data
      .map((d, i) => {
        const x = padding.left + i * xStep;
        const y = padding.top + chartH - ((d[key] ?? 0) / maxCount) * chartH;
        return `${i === 0 ? 'M' : 'L'} ${x.toFixed(1)} ${y.toFixed(1)}`;
      })
      .join(' ');

  const buildScorePath = () =>
    data
      .map((d, i) => {
        const x = padding.left + i * xStep;
        const y = padding.top + chartH - (Math.min(d.averageScore ?? 0, scoreMax) / scoreMax) * chartH;
        return `${i === 0 ? 'M' : 'L'} ${x.toFixed(1)} ${y.toFixed(1)}`;
      })
      .join(' ');

  const buildArea = (keys: ('excellentCount' | 'goodCount')[]) => {
    const path = data
      .map((d, i) => {
        const x = padding.left + i * xStep;
        const sum = keys.reduce((s, k) => s + (d[k] ?? 0), 0);
        const y = padding.top + chartH - (sum / maxCount) * chartH;
        return `${i === 0 ? 'M' : 'L'} ${x.toFixed(1)} ${y.toFixed(1)}`;
      })
      .join(' ');
    const lastX = padding.left + (data.length - 1) * xStep;
    return `${path} L ${lastX} ${padding.top + chartH} L ${padding.left} ${padding.top + chartH} Z`;
  };

  const yTicks = 4;

  const countLines = [
    { key: 'excellentCount' as const, labelKey: 'serviceOps.satisfaction.rating.excellent', color: '#16a34a', dash: '' },
    { key: 'goodCount' as const, labelKey: 'serviceOps.satisfaction.rating.good', color: '#059669', dash: '4 3' },
    { key: 'averageCount' as const, labelKey: 'serviceOps.satisfaction.rating.average', color: '#d97706', dash: '2 2' },
    { key: 'poorCount' as const, labelKey: 'serviceOps.satisfaction.rating.poor', color: '#ea580c', dash: '4 2' },
    { key: 'badCount' as const, labelKey: 'serviceOps.satisfaction.rating.bad', color: '#dc2626', dash: '' },
  ] satisfies { key: string; labelKey: TranslationKey; color: string; dash: string }[];

  // 网格线和右轴文字要延伸到容器右边界（chartW），与数据实际落点 usedChartW 解耦
  const gridRightX = padding.left + chartW;

  return (
    <div
      ref={containerRef}
      className="w-full min-w-0 h-[320px] overflow-hidden"
      style={{ contain: 'layout size' }}
    >
      <svg
        viewBox={`0 0 ${width} ${height}`}
        width="100%"
        height="100%"
        className="block"
        preserveAspectRatio="none"
        role="img"
        aria-label={t('serviceOps.satisfaction.trendChartAria')}
      >
        <defs>
          <linearGradient id="goodGrad" x1="0%" y1="0%" x2="0%" y2="100%">
            <stop offset="0%" stopColor="#10b981" stopOpacity="0.2" />
            <stop offset="100%" stopColor="#10b981" stopOpacity="0" />
          </linearGradient>
        </defs>

        {/* 左 Y 轴刻度（数量）— 网格线延伸至容器右边界 gridRightX */}
        {Array.from({ length: yTicks + 1 }).map((_, i) => {
          const y = padding.top + (chartH / yTicks) * i;
          const val = Math.round((maxCount / yTicks) * (yTicks - i));
          return (
            <g key={`l${i}`}>
              <line x1={padding.left} y1={y} x2={gridRightX} y2={y} stroke="#f3f4f6" />
              <text x={padding.left - 6} y={y + 4} fill="#9ca3af" fontSize="11" textAnchor="end">
                {val}
              </text>
            </g>
          );
        })}

        {/* 右 Y 轴刻度（平均分 0~5） */}
        {Array.from({ length: scoreMax + 1 }).map((_, i) => {
          const y = padding.top + chartH - (i / scoreMax) * chartH;
          return (
            <g key={`r${i}`}>
              <text x={gridRightX + 6} y={y + 4} fill="#6366f1" fontSize="11" textAnchor="start">
                {i}
              </text>
            </g>
          );
        })}
        {/* 右轴标题 */}
        <text x={gridRightX + 28} y={padding.top + 8} fill="#6366f1" fontSize="10" textAnchor="start">
          {t('serviceOps.satisfaction.axisScore')}
        </text>
        {/* 左轴标题 */}
        <text x={padding.left - 28} y={padding.top + 8} fill="#9ca3af" fontSize="10" textAnchor="start">
          {t('serviceOps.satisfaction.axisCount')}
        </text>

        <path d={buildArea(['excellentCount', 'goodCount'])} fill="url(#goodGrad)" />

        {/* 评分数量曲线 */}
        {countLines.map((line) => (
          <path
            key={line.key}
            d={buildCountPath(line.key)}
            fill="none"
            stroke={line.color}
            strokeWidth={line.dash === '' ? 2 : 1.5}
            strokeDasharray={line.dash}
          />
        ))}

        {/* 平均分曲线（右轴） */}
        <path
          d={buildScorePath()}
          fill="none"
          stroke="#6366f1"
          strokeWidth="2.5"
          strokeDasharray="6 2"
          opacity="0.8"
        />

        {/* 数据点 */}
        {data.map((d, i) => {
          const x = padding.left + i * xStep;
          return (
            <g key={i}>
              {countLines.map((line) => {
                const val = d[line.key] ?? 0;
                const y = padding.top + chartH - (val / maxCount) * chartH;
                return (
                  <circle
                    key={line.key}
                    cx={x}
                    cy={y}
                    r="2.5"
                    fill={line.color}
                    opacity={val > 0 ? 1 : 0}
                  />
                );
              })}
              {/* 平均分数据点 */}
              <circle
                cx={x}
                cy={padding.top + chartH - (Math.min(d.averageScore ?? 0, scoreMax) / scoreMax) * chartH}
                r="3"
                fill="#6366f1"
              />
              {(i === 0 || i === data.length - 1 || i % Math.ceil(data.length / 6) === 0) && (
                <text
                  x={x}
                  y={height - 68}
                  fill="#9ca3af"
                  fontSize="10"
                  textAnchor="middle"
                >
                  {d.date.slice(5)}
                </text>
              )}
            </g>
          );
        })}

        {/* 图例 - 两行布局 */}
        <g transform={`translate(${padding.left}, ${height - 50})`}>
          {countLines.map((line, i) => {
            const col = i % 3;
            const row = Math.floor(i / 3);
            const x = col * 130;
            const y = row * 18;
            return (
              <g key={line.key} transform={`translate(${x}, ${y})`}>
                <line x1="0" y1="6" x2="16" y2="6" stroke={line.color} strokeWidth="2" strokeDasharray={line.dash} />
                <text x="22" y="10" fill="#6b7280" fontSize="11">
                  {t(line.labelKey)}
                </text>
              </g>
            );
          })}
          {/* 平均分图例 */}
          <g transform={`translate(${(countLines.length % 3) * 130}, ${Math.floor(countLines.length / 3) * 18})`}>
            <line x1="0" y1="6" x2="16" y2="6" stroke="#6366f1" strokeWidth="2.5" strokeDasharray="6 2" />
            <text x="22" y="10" fill="#6b7280" fontSize="11">
              {t('serviceOps.satisfaction.stats.averageScore')}
            </text>
          </g>
        </g>
      </svg>
    </div>
  );
}

function RatingStars({ rating, size = 'md' }: { rating: number; size?: 'sm' | 'md' | 'lg' }) {
  const sizeMap = { sm: 'w-3.5 h-3.5', md: 'w-5 h-5', lg: 'w-7 h-7' };
  const color = rating >= 4 ? 'text-green-500' : rating === 3 ? 'text-amber-500' : 'text-red-500';

  return (
    <div className="flex items-center gap-0.5">
      {[1, 2, 3, 4, 5].map((star) => (
        <svg
          key={star}
          className={`${sizeMap[size]} ${star <= rating ? color : 'text-gray-300'}`}
          fill="currentColor"
          viewBox="0 0 20 20"
        >
          <path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z" />
        </svg>
      ))}
    </div>
  );
}

export default function Satisfaction() {
  const { t, language } = useI18n();
  /**
   * 评分码 → 文案。1..5 全部有词条；未命中（DTO 有 `[Range(1,5)]` 约束、库内不可达）
   * 回落后端下发的 `ratingLabel`，与"按码取词 + 未命中回落"的既有形态一致。
   */
  const ratingLabelOf = (code: number, fallback: string) => {
    const key = RATING_CODE_LABEL_KEYS[String(code)];
    return key ? t(key) : fallback;
  };
  const [query, setQuery] = useState<SatisfactionRatingQuery>({ page: 1, pageSize: 20 });
  const [list, setList] = useState<unknown>(null);
  const [overview, setOverview] = useState<SatisfactionOverviewResponse | null>(null);
  const toast = useToast();
  const navigate = useNavigate();
  const [loading, setLoading] = useState(false);
  const [overviewLoading, setOverviewLoading] = useState(false);
  const [mailboxes, setMailboxes] = useState<{ id: number; name: string }[]>([]);
  const [users, setUsers] = useState<{ id: number; fullName: string; email: string }[]>([]);
  const [ratingOptions, setRatingOptions] = useState<Record<number, string>>({});

  const defaultEndDate = useMemo(() => new Date().toISOString().slice(0, 10), []);
  const defaultStartDate = useMemo(
    () => new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10),
    []
  );
  const [startDate, setStartDate] = useState(defaultStartDate);
  const [endDate, setEndDate] = useState(defaultEndDate);

  useEffect(() => {
    let destroyed = false;
    mailboxApi.listAll().then((res) => {
      if (!destroyed) setMailboxes(Array.isArray(res) ? res : []);
    }).catch(() => {
      if (!destroyed) setMailboxes([]);
    });
    userApi.list().then((res) => {
      const data = res?.data ?? res;
      if (!destroyed) setUsers(Array.isArray(data) ? data : []);
    }).catch(() => {
      if (!destroyed) setUsers([]);
    });
    satisfactionApi.ratingOptions()
      .then((opts) => {
        if (!destroyed) setRatingOptions(typeof opts === 'object' && opts !== null ? opts : {});
      })
      .catch(() => {
        if (!destroyed) setRatingOptions({});
      });
    return () => { destroyed = true; };
  }, []);

  useEffect(() => {
    let destroyed = false;
    if (!destroyed) setOverviewLoading(true);
    satisfactionApi
      .overview(startDate, endDate, query.mailboxId ?? null)
      .then((res) => {
        if (!destroyed) setOverview(res);
      })
      .catch(() => {
        if (!destroyed) setOverview(null);
      })
      .finally(() => {
        if (!destroyed) setOverviewLoading(false);
      });
    return () => { destroyed = true; };
  }, [startDate, endDate, query.mailboxId]);

  useEffect(() => {
    let destroyed = false;
    if (!destroyed) setLoading(true);
    satisfactionApi
      .list(query)
      .then((res) => {
        if (!destroyed) setList(res);
      })
      .catch(() => {
        if (!destroyed) setList(null);
      })
      .finally(() => {
        if (!destroyed) setLoading(false);
      });
    return () => { destroyed = true; };
  }, [query]);

  // 订阅 SignalR SatisfactionUpdated 事件：用户提交评价后实时刷新统计与列表
  const refreshOverview = useCallback(() => {
    satisfactionApi
      .overview(startDate, endDate, query.mailboxId ?? null)
      .then((res) => setOverview(res))
      .catch(() => {});
  }, [startDate, endDate, query.mailboxId]);

  const refreshList = useCallback(() => {
    satisfactionApi
      .list(query)
      .then((res) => setList(res))
      .catch(() => {});
  }, [query]);

  useSignalREvent(SignalREvents.SatisfactionUpdated, () => {
    refreshOverview();
    refreshList();
  });

  const handlePageChange = (page: number) => {
    setQuery((q) => ({ ...q, page }));
  };

  const handleFilterChange = (key: keyof SatisfactionRatingQuery, value: unknown) => {
    setQuery((q) => ({ ...q, [key]: value, page: 1 }));
  };

  // 与 ApiKeys 一致：服务端搜索 650ms 输入防抖 + Enter 即时触发（缓冲 state，避免每次按键发请求）
  const keywordTimerRef = useRef<number | null>(null);
  const [keywordInput, setKeywordInput] = useState('');
  const applyKeyword = useCallback((value: string) => {
    handleFilterChange('keyword', value);
  }, [handleFilterChange]);
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
  // query.keyword 被其他入口改动时，同步输入框缓冲
  useEffect(() => {
    setKeywordInput(query.keyword ?? '');
  }, [query.keyword]);
  // 卸载时清理挂起的防抖定时器
  useEffect(() => {
    return () => {
      if (keywordTimerRef.current !== null) window.clearTimeout(keywordTimerRef.current);
    };
  }, []);

  // 兼容分页字段命名差异
  const listObj = typeof list === 'object' && list !== null ? list as Record<string, unknown> : {};
  const totalItems = Number(listObj['totalItems'] ?? listObj['totalCount'] ?? 0);
  const totalPages = Number(listObj['totalPages'] ?? 0);
  const currentPage = Number(listObj['currentPage'] ?? listObj['page'] ?? 1);
  const rawData = listObj['data'];
  const data = Array.isArray(rawData) ? rawData as SatisfactionRatingDto[] : [];

  const handleDelete = async (id: number) => {
    const item = data.find((r) => r.id === id);
    if (!item) return;
    if (item.source === 'chat_session') {
      toast.warning(t('serviceOps.satisfaction.chatRatingNotDeletable'));
      return;
    }
    if (!confirm(t('serviceOps.satisfaction.deleteConfirm'))) return;
    try {
      await satisfactionApi.remove(id);
      setQuery((q) => ({ ...q }));
    } catch (_e: unknown) {
      toast.error(t('serviceOps.common.deleteFailed'));
    }
  };

  const exportCSV = () => {
    if (data.length === 0) return;
    const headers = [
      'ID',
      t('serviceOps.satisfaction.table.rating'),
      t('serviceOps.satisfaction.table.comment'),
      t('serviceOps.satisfaction.table.customer'),
      t('serviceOps.satisfaction.csv.mailbox'),
      t('serviceOps.satisfaction.table.agent'),
      t('serviceOps.satisfaction.table.conversation'),
      t('serviceOps.satisfaction.csv.ratedAt'),
    ];
    const rows = data.map((r) => [
      r.id,
      ratingLabelOf(r.rating, r.ratingLabel),
      r.comment || '',
      r.customerName || '',
      r.customerEmail || '',
      r.userName || '',
      r.conversationSubject || `#${r.conversationId}`,
      new Date(r.createdAt).toLocaleString(toIntlLocale(language)),
    ]);
    const csv = '\uFEFF' + [headers, ...rows].map((row) => row.map((c) => `"${String(c).replace(/"/g, '""')}"`).join(',')).join('\n');
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8;' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = `satisfaction-${new Date().toISOString().slice(0, 10)}.csv`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  };

  const stats: SatisfactionRatingStatsDto | undefined = overview?.stats;

  return (
    <div className="space-y-5">
      {/* 页头 */}
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-semibold text-gray-900 dark:text-slate-100">{t('serviceOps.satisfaction.pageTitle')}</h1>
          <p className="text-sm text-gray-500 dark:text-slate-400 mt-1">{t('serviceOps.satisfaction.subtitle')}</p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={exportCSV}
            disabled={data.length === 0}
            className="inline-flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 disabled:cursor-not-allowed transition"
          >
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
            </svg>
            {t('serviceOps.common.exportCsv')}
          </button>
        </div>
      </div>

      {/* 时间范围筛选 */}
      <div className="bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-900 dark:to-slate-800 rounded-2xl border border-primary-100 dark:border-slate-700/50 p-4 shadow-sm">
        <div className="flex items-center gap-3 flex-wrap">
          <div className="flex items-center gap-2">
            <label className="text-sm text-gray-600 dark:text-slate-300 whitespace-nowrap">{t('serviceOps.satisfaction.filters.startDate')}</label>
            <input
              type="date"
              value={startDate}
              onChange={(e) => setStartDate(e.target.value)}
              className="bg-white dark:bg-slate-800 px-3 py-1.5 text-sm border border-gray-200 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent"
            />
          </div>
          <div className="flex items-center gap-2">
            <label className="text-sm text-gray-600 dark:text-slate-300 whitespace-nowrap">{t('serviceOps.satisfaction.filters.endDate')}</label>
            <input
              type="date"
              value={endDate}
              onChange={(e) => setEndDate(e.target.value)}
              className="bg-white dark:bg-slate-800 px-3 py-1.5 text-sm border border-gray-200 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent"
            />
          </div>
          <div className="flex items-center gap-2">
            <label className="text-sm text-gray-600 dark:text-slate-300 whitespace-nowrap">{t('serviceOps.satisfaction.filters.mailbox')}</label>
            <select
              value={query.mailboxId ?? ''}
              onChange={(e) => handleFilterChange('mailboxId', e.target.value ? Number(e.target.value) : null)}
              className="bg-white dark:bg-slate-800 px-3 py-1.5 text-sm border border-gray-200 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent"
            >
              <option value="">{t('serviceOps.satisfaction.filters.allMailboxes')}</option>
              {mailboxes.map((m) => (
                <option key={m.id} value={m.id}>{m.name}</option>
              ))}
            </select>
          </div>
        </div>
      </div>

      {/* 统计卡片 */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <StatCard
          label={t('serviceOps.satisfaction.stats.total')}
          value={stats?.totalCount ?? 0}
          icon={<svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M11.049 2.927c.3-.921 1.603-.921 1.902 0l1.519 4.674a1 1 0 00.95.69h4.915c.969 0 1.371 1.24.588 1.81l-3.976 2.888a1 1 0 00-.363 1.118l1.518 4.674c.3.922-.755 1.688-1.538 1.118l-3.976-2.888a1 1 0 00-1.176 0l-3.976 2.888c-.783.57-1.838-.197-1.538-1.118l1.518-4.674a1 1 0 00-.363-1.118l-3.976-2.888c-.784-.57-.38-1.81.588-1.81h4.914a1 1 0 00.951-.69l1.519-4.674z" /></svg>}
          colorClass="text-gray-700 dark:text-slate-200"
          iconBgClass="bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-slate-300"
          subtitle={overviewLoading ? t('common.shell.loading') : undefined}
        />
        <StatCard
          label={t('serviceOps.satisfaction.stats.goodRate')}
          value={stats ? `${stats.goodRate.toFixed(1)}%` : '-'}
          subtitle={stats ? t('serviceOps.satisfaction.stats.goodCount', { count: (stats.excellentCount ?? 0) + (stats.goodCount ?? 0) }) : undefined}
          icon={<svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M14 10h4.764a2 2 0 011.789 2.894l-3.5 7A2 2 0 0115.263 21h-4.017c-.163 0-.326-.02-.485-.06L7 20m7-10V5a2 2 0 00-2-2h-.095c-.5 0-.905.405-.905.905 0 .714-.211 1.412-.608 2.006L7 11v9m7-10h-2M7 20H5a2 2 0 01-2-2v-6a2 2 0 012-2h2.5" /></svg>}
          colorClass="text-green-600 dark:text-green-400"
          iconBgClass="bg-green-100 text-green-600 dark:bg-green-900/30 dark:text-green-300"
        />
        <StatCard
          label={t('serviceOps.satisfaction.stats.averageScore')}
          value={stats ? stats.averageScore.toFixed(2) : '-'}
          subtitle={t('serviceOps.satisfaction.stats.maxScoreHint')}
          icon={<svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M13 7h8m0 0v8m0-8l-8 8-4-4-6 6" /></svg>}
          colorClass="text-blue-600 dark:text-blue-400"
          iconBgClass="bg-blue-100 text-blue-600 dark:bg-blue-900/30 dark:text-blue-300"
        />
        <StatCard
          label={t('serviceOps.satisfaction.rating.bad')}
          value={stats?.badCount ?? 0}
          subtitle={stats && stats.totalCount > 0 ? t('serviceOps.satisfaction.stats.badShare', { percent: ((stats.badCount / stats.totalCount) * 100).toFixed(1) }) : undefined}
          icon={<svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" /></svg>}
          colorClass="text-red-600 dark:text-red-400"
          iconBgClass="bg-red-100 text-red-600 dark:bg-red-900/30 dark:text-red-300"
        />
      </div>

      {/* 趋势图 + 分布 */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-5">
        <div className="lg:col-span-2 bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-5 shadow-sm min-w-0">
          <div className="flex items-center justify-between mb-4">
            <h3 className="font-semibold text-gray-900 dark:text-slate-100">{t('serviceOps.satisfaction.trendTitle')}</h3>
          </div>
          {overviewLoading ? (
            <div className="h-56 flex flex-col items-center justify-center text-gray-400 dark:text-slate-500 text-sm">
              <div className="w-10 h-10 border-4 border-primary-200 dark:border-primary-700 border-t-primary-600 dark:border-t-primary-400 rounded-full animate-spin mb-3" />
              <span>{t('common.shell.loading')}</span>
            </div>
          ) : (
            <TrendChart data={overview?.dailyStats ?? []} />
          )}
        </div>

        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-5 shadow-sm">
          <h3 className="font-semibold text-gray-900 dark:text-slate-100 mb-4">{t('serviceOps.satisfaction.distributionTitle')}</h3>
          {overviewLoading ? (
            <div className="h-40 flex items-center justify-center text-gray-400 dark:text-slate-500 text-sm">{t('common.shell.loading')}</div>
          ) : stats ? (
            <div className="space-y-3">
              {([
                { key: 'excellent', labelKey: 'serviceOps.satisfaction.rating.excellent', count: stats.excellentCount, color: 'bg-green-500' },
                { key: 'good', labelKey: 'serviceOps.satisfaction.rating.good', count: stats.goodCount, color: 'bg-emerald-500' },
                { key: 'average', labelKey: 'serviceOps.satisfaction.rating.average', count: stats.averageCount, color: 'bg-amber-500' },
                { key: 'poor', labelKey: 'serviceOps.satisfaction.rating.poor', count: stats.poorCount, color: 'bg-orange-500' },
                { key: 'bad', labelKey: 'serviceOps.satisfaction.rating.bad', count: stats.badCount, color: 'bg-red-500' },
              ] satisfies { key: string; labelKey: TranslationKey; count: number | undefined; color: string }[]).map((item) => {
                const count = item.count ?? 0;
                const pct = stats.totalCount > 0 ? (count / stats.totalCount) * 100 : 0;
                return (
                  <div key={item.key}>
                    <div className="flex items-center justify-between text-sm mb-1">
                      <span className="text-gray-700 dark:text-slate-300">{t(item.labelKey)}</span>
                      <span className="text-gray-500 dark:text-slate-400">{count} ({pct.toFixed(1)}%)</span>
                    </div>
                    <div className="h-2 bg-gray-100 dark:bg-slate-700 rounded-full overflow-hidden">
                      <div className={`h-full ${item.color} rounded-full transition-all`} style={{ width: `${pct}%` }}></div>
                    </div>
                  </div>
                );
              })}
            </div>
          ) : null}
        </div>
      </div>

      {/* 客服排名 + 邮箱排名 */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-5 shadow-sm">
          <h3 className="font-semibold text-gray-900 dark:text-slate-100 mb-4">{t('serviceOps.satisfaction.agentRankingTitle')}</h3>
          {overviewLoading ? (
            <div className="h-40 flex items-center justify-center text-gray-400 dark:text-slate-500 text-sm">{t('common.shell.loading')}</div>
          ) : overview?.userStats?.length ? (
            <div className="space-y-2.5">
              {overview.userStats.slice(0, 5).map((u: UserSatisfactionStatsDto, i: number) => (
                <div key={u.userId} className="flex items-center gap-3">
                  <span className={`w-6 h-6 rounded-full flex items-center justify-center text-xs font-bold ${
                    i === 0 ? 'bg-yellow-100 text-yellow-700' :
                    i === 1 ? 'bg-gray-200 text-gray-600' :
                    i === 2 ? 'bg-orange-100 text-orange-700' :
                    'bg-gray-100 text-gray-500'
                  }`}>
                    {i + 1}
                  </span>
                  <UserAvatar userId={u.userId} name={u.userName} size="sm" />
                  <div className="flex-1 min-w-0">
                    <div className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate">{u.userName}</div>
                    <div className="text-xs text-gray-500 dark:text-slate-400 truncate">{u.email}</div>
                  </div>
                  <div className="text-right flex-shrink-0">
                    <div className={`text-sm font-semibold ${(u.averageScore / 5) * 100 >= 80 ? 'text-green-600' : (u.averageScore / 5) * 100 >= 60 ? 'text-amber-600' : 'text-red-600'}`}>
                      {((u.averageScore / 5) * 100).toFixed(0)}%
                    </div>
                    <div className="text-xs text-gray-400 dark:text-slate-500">{t('serviceOps.satisfaction.reviewCount', { count: u.totalCount })}</div>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <div className="h-40 flex items-center justify-center text-gray-400 dark:text-slate-500 text-sm">{t('common.empty')}</div>
          )}
        </div>

        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-5 shadow-sm">
          <h3 className="font-semibold text-gray-900 dark:text-slate-100 mb-4">{t('serviceOps.satisfaction.mailboxTitle')}</h3>
          {overviewLoading ? (
            <div className="h-40 flex items-center justify-center text-gray-400 dark:text-slate-500 text-sm">{t('common.shell.loading')}</div>
          ) : overview?.mailboxStats?.length ? (
            <div className="space-y-3">
              {overview.mailboxStats.map((m: MailboxSatisfactionStatsDto) => {
                const pct = (m.averageScore / 5) * 100;
                return (
                <div key={m.mailboxId} className="space-y-1">
                  <div className="flex items-center justify-between text-sm">
                    <span className="text-gray-700 dark:text-slate-300 font-medium">{m.mailboxName}</span>
                    <span className={`font-semibold ${pct >= 80 ? 'text-green-600' : pct >= 60 ? 'text-amber-600' : 'text-red-600'}`}>
                      {pct.toFixed(0)}%
                    </span>
                  </div>
                  <div className="h-2 bg-gray-100 dark:bg-slate-700 rounded-full overflow-hidden">
                    <div
                      className={`h-full rounded-full ${pct >= 80 ? 'bg-green-500' : pct >= 60 ? 'bg-amber-500' : 'bg-red-500'}`}
                      style={{ width: `${pct}%` }}
                    ></div>
                  </div>
                  <div className="text-xs text-gray-400 dark:text-slate-500">{t('serviceOps.satisfaction.mailboxReviewCount', { count: m.totalCount, email: m.email })}</div>
                </div>
                );
              })}
            </div>
          ) : (
            <div className="h-40 flex items-center justify-center text-gray-400 dark:text-slate-500 text-sm">{t('common.empty')}</div>
          )}
        </div>
      </div>

      {/* 评价列表筛选 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        <div className="p-3 sm:p-4 border-b border-gray-100 dark:border-slate-700/50 flex flex-col sm:flex-row items-stretch sm:items-center gap-3">
          <div className="flex-1 min-w-0">
            <div className="relative">
              <svg className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400 dark:text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
              </svg>
              <input
                type="text"
                placeholder={t('serviceOps.satisfaction.filters.searchPlaceholder')}
                value={keywordInput}
                onChange={(e) => handleKeywordInput(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') triggerKeywordSearch(); }}
                className="bg-white dark:bg-slate-900 w-full pl-9 pr-3 py-2 text-sm border border-gray-200 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent"
              />
            </div>
          </div>
          <div className="grid grid-cols-2 sm:flex items-center gap-2 sm:gap-3">
            <select
              value={query.rating ?? ''}
              onChange={(e) => handleFilterChange('rating', e.target.value ? Number(e.target.value) : null)}
              className="bg-white dark:bg-slate-900 px-3 py-2 text-sm border border-gray-200 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent"
            >
              <option value="">{t('serviceOps.satisfaction.filters.allRatings')}</option>
              {Object.keys(ratingOptions ?? {}).map((val) => {
                const key = RATING_CODE_LABEL_KEYS[val];
                return (
                  <option key={val} value={val}>{key ? t(key) : val}</option>
                );
              })}
            </select>
            <select
              value={query.userId ?? ''}
              onChange={(e) => handleFilterChange('userId', e.target.value ? Number(e.target.value) : null)}
              className="bg-white dark:bg-slate-900 px-3 py-2 text-sm border border-gray-200 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent"
            >
              <option value="">{t('serviceOps.satisfaction.filters.allAgents')}</option>
              {users.map((u) => (
                <option key={u.id} value={u.id}>{u.fullName}</option>
              ))}
            </select>
          </div>
        </div>

        {/* 评价列表 */}
        {/* 移动端卡片式列表 */}
        <div className="sm:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
          {loading ? (
            <div className="p-12 text-center">
              <div className="w-10 h-10 border-4 border-primary-200 dark:border-primary-700 border-t-primary-600 dark:border-t-primary-400 rounded-full animate-spin mx-auto mb-3" />
              <p className="text-gray-400 dark:text-slate-500 text-sm">{t('common.shell.loading')}</p>
            </div>
          ) : data.length === 0 ? (
            <div className="p-12 text-center">
              <div className="w-16 h-16 bg-gray-100 dark:bg-slate-800 rounded-full flex items-center justify-center mx-auto mb-4">
                <svg className="w-8 h-8 text-gray-400 dark:text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M11.049 2.927c.3-.921 1.603-.921 1.902 0l1.519 4.674a1 1 0 00.95.69h4.915c.969 0 1.371 1.24.588 1.81l-3.976 2.888a1 1 0 00-.363 1.118l1.518 4.674c.3.922-.755 1.688-1.538 1.118l-3.976-2.888a1 1 0 00-1.176 0l-3.976 2.888c-.783.57-1.838-.197-1.538-1.118l1.518-4.674a1 1 0 00-.363-1.118l-3.976-2.888c-.784-.57-.38-1.81.588-1.81h4.914a1 1 0 00.951-.69l1.519-4.674z" />
                </svg>
              </div>
              <p className="text-gray-400 dark:text-slate-500 text-sm">{t('serviceOps.satisfaction.list.empty')}</p>
            </div>
          ) : (
            data.map((r) => {
              const style = RATING_STYLES[r.ratingCategory] ?? RATING_STYLES.average;
              const isChat = r.source === 'chat_session';
              return (
                <div key={r.id} className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700/50 transition-colors">
                  <div className="flex items-center justify-between gap-2 mb-2">
                    {isChat ? (
                      <span className="inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full bg-indigo-50 text-indigo-700 dark:bg-indigo-900/30 dark:text-indigo-300">
                        <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
                        </svg>
                        {t('format.sourceType.liveChat')}
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full bg-purple-50 text-purple-700 dark:bg-purple-900/30 dark:text-purple-300">
                        <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
                        </svg>
                        {t('serviceOps.satisfaction.source.ratingLink')}
                      </span>
                    )}
                    <span className={`text-xs px-2 py-0.5 rounded-full ${style.badge}`}>{ratingLabelOf(r.rating, r.ratingLabel)}</span>
                  </div>
                  <div className="flex items-center gap-2 mb-2">
                    <RatingStars rating={r.rating} size="sm" />
                    <span className="text-xs text-gray-400 dark:text-slate-500">{new Date(r.createdAt).toLocaleDateString(toIntlLocale(language))}</span>
                  </div>
                  {r.comment && (
                    <div className="text-sm text-gray-700 dark:text-slate-300 line-clamp-2 mb-2">
                      {r.comment}
                    </div>
                  )}
                  <div className="grid grid-cols-2 gap-1 text-xs text-gray-500 dark:text-slate-400 mb-2">
                    <div className="truncate">
                      <span className="text-gray-400 dark:text-slate-500">{t('serviceOps.satisfaction.list.customerPrefix')}</span>
                      {r.customerName || t('format.unknown')}
                    </div>
                    <div className="truncate">
                      <span className="text-gray-400 dark:text-slate-500">{t('serviceOps.satisfaction.list.agentPrefix')}</span>
                      {r.userName || '-'}
                    </div>
                  </div>
                  <div className="flex items-center justify-between">
                    {r.conversationId ? (
                      <a
                        href={`/conversations/${r.conversationId}`}
                        className="text-xs text-primary-600 hover:text-primary-700 hover:underline cursor-pointer truncate flex-1 min-w-0"
                        onClick={(e) => {
                          e.preventDefault();
                          navigate(`/conversations/${r.conversationId}`);
                        }}
                      >
                        {r.conversationSubject || `#${r.conversationId}`}
                      </a>
                    ) : (
                      <span className="text-xs text-gray-400 dark:text-slate-500">{t('serviceOps.satisfaction.list.noConversation')}</span>
                    )}
                    {!isChat && (
                      <button
                        onClick={() => handleDelete(r.id)}
                        className="p-1.5 text-gray-500 dark:text-slate-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-md transition shrink-0"
                        title={t('common.delete')}
                      >
                        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                        </svg>
                      </button>
                    )}
                  </div>
                </div>
              );
            })
          )}
        </div>

        {/* PC 端表格 */}
        <div className="hidden sm:block overflow-x-auto">
          <table className="w-full">
            <thead className="bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-900 dark:to-slate-800 border-b border-gray-200 dark:border-slate-700">
              <tr>
                <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('serviceOps.satisfaction.table.source')}</th>
                <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('serviceOps.satisfaction.table.rating')}</th>
                <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('serviceOps.satisfaction.table.comment')}</th>
                <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('serviceOps.satisfaction.table.customer')}</th>
                <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('serviceOps.satisfaction.table.agent')}</th>
                <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('serviceOps.satisfaction.table.conversation')}</th>
                <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('serviceOps.satisfaction.table.time')}</th>
                <th className="px-4 py-3 text-right text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('serviceOps.satisfaction.table.actions')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 dark:divide-slate-700/50">
              {loading ? (
                <tr>
                  <td colSpan={8} className="px-4 py-12 text-center">
                    <div className="w-10 h-10 border-4 border-primary-200 dark:border-primary-700 border-t-primary-600 dark:border-t-primary-400 rounded-full animate-spin mx-auto mb-3" />
                    <p className="text-gray-400 dark:text-slate-500 text-sm">{t('common.shell.loading')}</p>
                  </td>
                </tr>
              ) : data.length === 0 ? (
                <tr>
                  <td colSpan={8} className="px-4 py-12 text-center">
                    <div className="w-16 h-16 bg-gray-100 dark:bg-slate-800 rounded-full flex items-center justify-center mx-auto mb-4">
                      <svg className="w-8 h-8 text-gray-400 dark:text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M11.049 2.927c.3-.921 1.603-.921 1.902 0l1.519 4.674a1 1 0 00.95.69h4.915c.969 0 1.371 1.24.588 1.81l-3.976 2.888a1 1 0 00-.363 1.118l1.518 4.674c.3.922-.755 1.688-1.538 1.118l-3.976-2.888a1 1 0 00-1.176 0l-3.976 2.888c-.783.57-1.838-.197-1.538-1.118l1.518-4.674a1 1 0 00-.363-1.118l-3.976-2.888c-.784-.57-.38-1.81.588-1.81h4.914a1 1 0 00.951-.69l1.519-4.674z" />
                      </svg>
                    </div>
                    <p className="text-gray-400 dark:text-slate-500 text-sm">{t('serviceOps.satisfaction.list.empty')}</p>
                  </td>
                </tr>
              ) : (
                data.map((r) => {
                  const style = RATING_STYLES[r.ratingCategory] ?? RATING_STYLES.average;
                  const isChat = r.source === 'chat_session';
                  return (
                    <tr key={r.id} className="hover:bg-gray-50 dark:hover:bg-slate-700/50 transition-colors">
                      <td className="px-4 py-3">
                        {isChat ? (
                          <span className="inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full bg-indigo-50 text-indigo-700 dark:bg-indigo-900/30 dark:text-indigo-300">
                            <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                              <path strokeLinecap="round" strokeLinejoin="round" d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
                            </svg>
                            {t('format.sourceType.liveChat')}
                          </span>
                        ) : (
                          <span className="inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full bg-purple-50 text-purple-700 dark:bg-purple-900/30 dark:text-purple-300">
                            <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                              <path strokeLinecap="round" strokeLinejoin="round" d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
                            </svg>
                            {t('serviceOps.satisfaction.source.ratingLink')}
                          </span>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-2">
                          <RatingStars rating={r.rating} size="sm" />
                          <span className={`text-xs px-2 py-0.5 rounded-full ${style.badge}`}>{ratingLabelOf(r.rating, r.ratingLabel)}</span>
                        </div>
                      </td>
                      <td className="px-4 py-3 max-w-xs">
                        <div className="text-sm text-gray-700 dark:text-slate-300 line-clamp-2">
                          {r.comment || <span className="text-gray-400 dark:text-slate-500 italic">{t('serviceOps.satisfaction.list.noCommentText')}</span>}
                        </div>
                      </td>
                      <td className="px-4 py-3">
                        <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{r.customerName || t('format.unknown')}</div>
                        <div className="text-xs text-gray-500 dark:text-slate-400">{r.customerEmail}</div>
                      </td>
                      <td className="px-4 py-3">
                        <div className="text-sm text-gray-700 dark:text-slate-300">{r.userName || '-'}</div>
                      </td>
                      <td className="px-4 py-3">
                        {r.conversationId ? (
                          <a
                            href={`/conversations/${r.conversationId}`}
                            className="text-sm text-primary-600 hover:text-primary-700 hover:underline cursor-pointer"
                            onClick={(e) => {
                              e.preventDefault();
                              navigate(`/conversations/${r.conversationId}`);
                            }}
                          >
                            {r.conversationSubject || `#${r.conversationId}`}
                          </a>
                        ) : (
                          <span className="text-sm text-gray-400 dark:text-slate-500">{t('serviceOps.satisfaction.list.noConversation')}</span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-sm text-gray-500 dark:text-slate-400">
                        {new Date(r.createdAt).toLocaleString(toIntlLocale(language))}
                      </td>
                      <td className="px-4 py-3 text-right">
                        {!isChat ? (
                          <button
                            onClick={() => handleDelete(r.id)}
                            className="p-1.5 text-gray-500 dark:text-slate-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-md transition"
                            title={t('common.delete')}
                          >
                            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                              <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                            </svg>
                          </button>
                        ) : (
                          <span className="text-xs text-gray-400 dark:text-slate-500">{t('serviceOps.satisfaction.list.notDeletable')}</span>
                        )}
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>

        {/* 分页 */}
        {totalPages > 0 && (
          <div className="px-3 sm:px-4 py-3 border-t border-gray-100 dark:border-slate-700/50 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
            <div className="text-xs sm:text-sm text-gray-500 dark:text-slate-400">
              {t('serviceOps.satisfaction.pagination.prefix')}<span className="font-medium text-gray-700 dark:text-slate-300">{currentPage}</span> / {totalPages}{t('serviceOps.satisfaction.pagination.middle')}<span className="font-medium text-gray-700 dark:text-slate-300">{totalItems}</span>{t('serviceOps.satisfaction.pagination.suffix')}
            </div>
            {/* 移动端简化分页 */}
            <div className="flex items-center gap-2 sm:hidden">
              <button
                onClick={() => handlePageChange(currentPage - 1)}
                disabled={currentPage <= 1}
                className="flex-1 px-3 py-1.5 text-sm text-gray-600 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition"
              >
                {t('serviceOps.common.previousPage')}
              </button>
              <button
                onClick={() => handlePageChange(currentPage + 1)}
                disabled={currentPage >= totalPages}
                className="flex-1 px-3 py-1.5 text-sm text-gray-600 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition"
              >
                {t('serviceOps.common.nextPage')}
              </button>
            </div>
            {/* PC 端完整分页 */}
            <div className="hidden sm:flex items-center gap-1.5 flex-wrap">
              <button
                onClick={() => handlePageChange(1)}
                disabled={currentPage <= 1}
                className="px-2.5 py-1 text-sm text-gray-600 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition"
              >
                {t('serviceOps.common.firstPage')}
              </button>
              <button
                onClick={() => handlePageChange(currentPage - 1)}
                disabled={currentPage <= 1}
                className="px-3 py-1 text-sm text-gray-600 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition"
              >
                {t('serviceOps.common.previousPage')}
              </button>
              {/* 页码按钮 */}
              {Array.from({ length: Math.min(5, totalPages) }).map((_, idx) => {
                let pageNum: number;
                if (totalPages <= 5) {
                  pageNum = idx + 1;
                } else if (currentPage <= 3) {
                  pageNum = idx + 1;
                } else if (currentPage >= totalPages - 2) {
                  pageNum = totalPages - 4 + idx;
                } else {
                  pageNum = currentPage - 2 + idx;
                }
                return (
                  <button
                    key={pageNum}
                    onClick={() => handlePageChange(pageNum)}
                    className={`min-w-[2rem] px-2 py-1 text-sm rounded-lg transition ${
                      currentPage === pageNum
                        ? 'bg-primary-600 text-white border border-primary-600 shadow-sm font-medium'
                        : 'text-gray-600 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 hover:bg-gray-50 dark:hover:bg-slate-700'
                    }`}
                  >
                    {pageNum}
                  </button>
                );
              })}
              <button
                onClick={() => handlePageChange(currentPage + 1)}
                disabled={currentPage >= totalPages}
                className="px-3 py-1 text-sm text-gray-600 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition"
              >
                {t('serviceOps.common.nextPage')}
              </button>
              <button
                onClick={() => handlePageChange(totalPages)}
                disabled={currentPage >= totalPages}
                className="px-2.5 py-1 text-sm text-gray-600 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition"
              >
                {t('serviceOps.common.lastPage')}
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
