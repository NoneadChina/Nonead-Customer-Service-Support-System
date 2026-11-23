import { useCallback, useMemo, memo } from 'react';
import { KpiCard } from '../components/KpiCard';
import { useCalendarStore } from '../store';
import type { CalendarEventDto } from '../../../api/types';
import {
  countCompletedThisWeek,
  countConflicts,
  countTodayEvents,
  countTomorrowEvents,
  formatDate,
  formatDateTime,
  toYMDKey,
} from '../calendarUtils';
import { getEventTypeStyle, getStatusStyle } from '../chips';
import { shallowEqual } from '../hooks';
import { useI18n } from '../../../context/I18nContext';
import type { TranslationKey } from '../../../locales/zh-Hans';
const shallow = shallowEqual;

const WEEK_LABEL_KEYS: TranslationKey[] = [
  'calendar.common.weekdayShort.mon',
  'calendar.common.weekdayShort.tue',
  'calendar.common.weekdayShort.wed',
  'calendar.common.weekdayShort.thu',
  'calendar.common.weekdayShort.fri',
  'calendar.common.weekdayShort.sat',
  'calendar.common.weekdayShort.sun',
];

function IconTodo() {
  return (
    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M9 11l3 3L22 4" />
      <path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11" />
    </svg>
  );
}
function IconWeek() {
  return (
    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="4" width="18" height="18" rx="2" ry="2" />
      <line x1="16" y1="2" x2="16" y2="6" />
      <line x1="8" y1="2" x2="8" y2="6" />
      <line x1="3" y1="10" x2="21" y2="10" />
    </svg>
  );
}
function IconCompleted() {
  return (
    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" />
      <polyline points="22 4 12 14.01 9 11.01" />
    </svg>
  );
}
function IconConflict() {
  return (
    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="13 17 18 12 13 7" />
      <polyline points="11 7 6 12 11 17" />
      <line x1="6" y1="12" x2="18" y2="12" strokeDasharray="2 2" />
    </svg>
  );
}

function ShimmerBar({ className = '' }: { className?: string }) {
  return <div className={`bg-gradient-to-r from-slate-100 via-slate-200 to-slate-100 dark:from-slate-800 dark:via-slate-700 dark:to-slate-800 animate-pulse rounded ${className}`} />;
}

interface WeekBarItem {
  date: string;
  rawDate: Date;
  count: number;
  heightPx: number;
  isToday: boolean;
  weekLabel: string;
}

const BarCell = memo(function BarCell({ bar, onClick }: { bar: WeekBarItem; onClick: (ymd: string) => void }) {
  const { t } = useI18n();
  const handle = useCallback(() => onClick(bar.date), [bar.date, onClick]);
  return (
    <div
      onClick={handle}
      className="flex-1 flex flex-col items-center gap-2 group cursor-pointer"
    >
      <div className="w-full flex items-end justify-center h-20">
        <div
          className={`w-full max-w-8 rounded-t-lg transition-all group-hover:opacity-80 group-hover:scale-105 ${bar.isToday ? 'bg-gradient-to-t from-sky-500 to-sky-400' : 'bg-gradient-to-t from-slate-300 to-slate-200 dark:from-slate-700 dark:to-slate-600'}`}
          style={{ height: `${bar.heightPx}px` }}
          title={t('calendar.views.overview.barTooltip', { date: formatDate(bar.rawDate, 'short'), count: bar.count })}
        />
      </div>
      <span className={`text-xs ${bar.isToday ? 'font-bold text-sky-600 dark:text-sky-400' : 'text-slate-500 dark:text-slate-400'}`}>
        {bar.weekLabel}
      </span>
    </div>
  );
});

const EventRow = memo(function EventRow({ ev, onClick }: { ev: CalendarEventDto; onClick: (ev: CalendarEventDto) => void }) {
  const { t } = useI18n();
  const handle = useCallback(() => onClick(ev), [ev, onClick]);
  const ts = getEventTypeStyle(ev.eventType);
  const ss = getStatusStyle(ev.status);
  // 挂起同步标记：拖拽后后端尚未确认时显示灰色小圆点
  const isPendingSync = useCalendarStore(
    (s) => typeof ev.id === 'number' && s.pendingPatchIds.has(ev.id)
  );
  return (
    <tr onClick={handle} className="cursor-pointer hover:bg-sky-50/30 dark:hover:bg-sky-950/20 active:scale-[0.995] transition-all">
      <td className="px-5 py-3">
        <div className="flex items-center gap-3 text-left">
          <span className={`shrink-0 w-2 h-2 rounded-full ring-2 ring-white dark:ring-slate-900 shadow-sm ${ts.dotClass}`} />
          <div className="flex-1 min-w-0">
            <div className="font-medium text-sm text-slate-900 dark:text-slate-100 truncate flex items-center gap-1.5">
              <span className="truncate">{ev.title || t('calendar.common.untitled')}</span>
              {isPendingSync && (
                <span
                  className="cal-sync-pending-dot shrink-0 w-2 h-2 rounded-full bg-slate-400 ring-2 ring-white/70 dark:ring-slate-900/70"
                  title={t('calendar.common.pendingSyncHint')}
                  aria-label={t('calendar.common.pendingSync')}
                />
              )}
            </div>
            <div className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
              {formatDateTime(ev.startAt, ev.isAllDay)}
              {ev.endAt && !ev.isAllDay ? ` — ${formatDateTime(ev.endAt, false).split(' ')[1] || ''}` : ''}
            </div>
          </div>
          <div className="flex shrink-0 items-center gap-1.5">
            <span className={`inline-flex items-center px-2 py-0.5 rounded text-[11px] shadow-sm ${ts.badgeClass}`}>{ts.label}</span>
            <span className={`inline-flex items-center px-2 py-0.5 rounded text-[11px] shadow-sm ${ss.badgeClass}`}>{ss.label}</span>
          </div>
        </div>
      </td>
    </tr>
  );
});

const EventMobileCard = memo(function EventMobileCard({ ev, onClick }: { ev: CalendarEventDto; onClick: (ev: CalendarEventDto) => void }) {
  const { t } = useI18n();
  const handle = useCallback(() => onClick(ev), [ev, onClick]);
  const ts = getEventTypeStyle(ev.eventType);
  const ss = getStatusStyle(ev.status);
  const isPendingSync = useCalendarStore(
    (s) => typeof ev.id === 'number' && s.pendingPatchIds.has(ev.id)
  );
  return (
    <div onClick={handle} className="p-3.5 cursor-pointer hover:bg-sky-50/30 dark:hover:bg-sky-950/20 active:scale-[0.995] transition-all">
      <div className="flex items-center gap-2 min-w-0 mb-1.5">
        <span className={`shrink-0 w-2 h-2 rounded-full ring-2 ring-white dark:ring-slate-900 shadow-sm ${ts.dotClass}`} />
        <span className="font-medium text-sm text-slate-900 dark:text-slate-100 truncate flex items-center gap-1.5">
          <span className="truncate">{ev.title || t('calendar.common.untitled')}</span>
          {isPendingSync && (
            <span
              className="cal-sync-pending-dot shrink-0 w-2 h-2 rounded-full bg-slate-400 ring-2 ring-white/70 dark:ring-slate-900/70"
              title={t('calendar.common.pendingSyncHint')}
              aria-label={t('calendar.common.pendingSync')}
            />
          )}
        </span>
      </div>
      <div className="text-xs text-slate-500 dark:text-slate-400 mb-1.5">
        {formatDateTime(ev.startAt, ev.isAllDay)}
        {ev.endAt && !ev.isAllDay ? ` — ${formatDateTime(ev.endAt, false).split(' ')[1] || ''}` : ''}
      </div>
      <div className="flex items-center gap-1.5">
        <span className={`inline-flex items-center px-2 py-0.5 rounded text-[11px] shadow-sm ${ts.badgeClass}`}>{ts.label}</span>
        <span className={`inline-flex items-center px-2 py-0.5 rounded text-[11px] shadow-sm ${ss.badgeClass}`}>{ss.label}</span>
      </div>
    </div>
  );
});

export default function Overview() {
  const { t } = useI18n();
  const { overview, allEvents, calCursor, loading } = useCalendarStore(
    (s) => ({ overview: s.overview, allEvents: s.allEvents, calCursor: s.calCursor, loading: s.loading }),
    shallow
  );
  const setViewMode = useCalendarStore((s) => s.setViewMode);
  const setCalSubView = useCalendarStore((s) => s.setCalSubView);
  const gotoToday = useCalendarStore((s) => s.gotoToday);
  const openCreate = useCalendarStore((s) => s.openCreate);
  const openEdit = useCalendarStore((s) => s.openEdit);

  const stats = overview?.stats ?? {};

  // P2-A#1: KPI 卡片改为前端从 allEvents 实时派生，不再依赖后端 overview.stats 的固定字段。
  // 数据源：store.allEvents（已包含今日/本周/明日的事件，过滤已取消）。
  // 当 allEvents 为空（首次加载未完成）时回退到 overview.stats，避免首屏 0 闪烁。
  const todayCount = useMemo(
    () => (allEvents.length ? countTodayEvents(allEvents) : (stats.pending ?? 0)),
    [allEvents, stats.pending],
  );
  const completedThisWeek = useMemo(
    () => (allEvents.length ? countCompletedThisWeek(allEvents) : (stats.completed ?? 0)),
    [allEvents, stats.completed],
  );
  const conflictsCount = useMemo(
    () => (allEvents.length ? countConflicts(allEvents) : (stats.conflicts ?? 0)),
    [allEvents, stats.conflicts],
  );
  const tomorrowCount = useMemo(
    () => (allEvents.length ? countTomorrowEvents(allEvents) : (stats.upcoming ?? 0)),
    [allEvents, stats.upcoming],
  );

  const todayLabel = useMemo(() => {
    const now = new Date();
    const wd = t(WEEK_LABEL_KEYS[(now.getDay() + 6) % 7]);
    return t('calendar.views.overview.todayWithDate', {
      year: now.getFullYear(),
      month: now.getMonth() + 1,
      day: now.getDate(),
      weekday: wd,
    });
  }, [t]);

  // 高7 修复：weekData useMemo 直接从 allEvents 派生（不再调用 getState().getEventsCountByYMD），
  // 并把 allEvents 加入依赖数组（不只是 length），避免内容变化但长度相同时显示过期数据。
  const weekData = useMemo(() => {
    const base = new Date(calCursor);
    const dayIdx = (base.getDay() + 6) % 7;
    const monday = new Date(base);
    monday.setDate(base.getDate() - dayIdx);
    const days: { rawDate: Date; count: number; date: string; weekLabel: string; isToday: boolean }[] = [];
    const todayYMDInner = toYMDKey(new Date());
    // 预先按 YMD 建立索引，避免每天 O(N) 扫描 allEvents
    const countsByYMD = new Map<string, number>();
    for (const ev of allEvents) {
      if (!ev.startAt) continue;
      const t = +new Date(ev.startAt);
      if (isNaN(t)) continue;
      const ymd = toYMDKey(new Date(ev.startAt));
      countsByYMD.set(ymd, (countsByYMD.get(ymd) ?? 0) + 1);
    }
    for (let i = 0; i < 7; i++) {
      const rawDate = new Date(monday);
      rawDate.setDate(monday.getDate() + i);
      const date = toYMDKey(rawDate);
      const count = countsByYMD.get(date) ?? 0;
      days.push({
        rawDate,
        count,
        date,
        weekLabel: t('calendar.views.shared.weekdayHeader', { day: t(WEEK_LABEL_KEYS[i]) }),
        isToday: date === todayYMDInner,
      });
    }
    const max = Math.max(1, ...days.map((d) => d.count));
    return days.map((d) => ({ ...d, heightPx: Math.max(d.count === 0 ? 2 : (d.count / max) * 80, 4) }));
  }, [calCursor, allEvents, t]);

  // 高8 修复：从 allEvents 真实计算 sparkline（最近 7 天每日事件量）和 delta（vs 上个 7 天），
  // 替换原硬编码的 mock 数据 [30,50,20,70,40,60,30] 等。
  const kpiSparklines = useMemo(() => {
    const empty = { sparkline: [0, 0, 0, 0, 0, 0, 0], deltaPct: 0, totalThis: 0, totalPrev: 0 };
    if (!allEvents.length) {
      return {
        today: empty,
        completed: empty,
        conflicts: empty,
        tomorrow: empty,
      };
    }
    const now = new Date();
    const today0 = new Date(now); today0.setHours(0, 0, 0, 0);

    // 7 天分桶（含今日为最后一天），同时计算"上一周对应 7 天"作为对比基线
    const buckets: Record<'all' | 'completed' | 'conflicts' | 'tomorrow', number[]> = {
      all: [0, 0, 0, 0, 0, 0, 0],
      completed: [0, 0, 0, 0, 0, 0, 0],
      conflicts: [0, 0, 0, 0, 0, 0, 0],
      tomorrow: [0, 0, 0, 0, 0, 0, 0],
    };
    const prevBuckets: Record<'all' | 'completed' | 'conflicts' | 'tomorrow', number[]> = {
      all: [0, 0, 0, 0, 0, 0, 0],
      completed: [0, 0, 0, 0, 0, 0, 0],
      conflicts: [0, 0, 0, 0, 0, 0, 0],
      tomorrow: [0, 0, 0, 0, 0, 0, 0],
    };

    // 冲突检测辅助：O(N^2)，但仅用于概览 KPI（事件数较少时可接受）
    const conflictIdSet = new Set<number>();
    for (let i = 0; i < allEvents.length; i++) {
      const a = allEvents[i];
      if (!a || !a.startAt || a.isAllDay) continue;
      const aStart = +new Date(a.startAt);
      const aEnd = a.endAt ? +new Date(a.endAt) : aStart + 3600_000;
      if (isNaN(aStart)) continue;
      for (let j = i + 1; j < allEvents.length; j++) {
        const b = allEvents[j];
        if (!b || !b.startAt || b.isAllDay) continue;
        const bStart = +new Date(b.startAt);
        const bEnd = b.endAt ? +new Date(b.endAt) : bStart + 3600_000;
        if (isNaN(bStart)) continue;
        if (aStart < bEnd && bStart < aEnd) {
          conflictIdSet.add(a.id); conflictIdSet.add(b.id);
        }
      }
    }

    for (const ev of allEvents) {
      if (!ev.startAt) continue;
      const t = +new Date(ev.startAt);
      if (isNaN(t)) continue;
      // 当前 7 天分桶（today-6 ... today）
      const dayDiff = Math.floor((t - today0.getTime()) / 86400_000);
      if (dayDiff >= -6 && dayDiff <= 0) {
        const idx = dayDiff + 6;
        buckets.all[idx]++;
        if (ev.status === 'completed') buckets.completed[idx]++;
        if (conflictIdSet.has(ev.id)) buckets.conflicts[idx]++;
        // 明日日程：当前 7 天内、明天或更晚的 pending 事件
        if (dayDiff === -1 || dayDiff === 0) {
          // 计算"今天剩余 + 明日"的待办数变化
          if (ev.status === 'pending') buckets.tomorrow[idx]++;
        }
      }
      // 上一周对应 7 天分桶
      const prevDayDiff = Math.floor((t - (today0.getTime() - 7 * 86400_000)) / 86400_000);
      if (prevDayDiff >= -6 && prevDayDiff <= 0) {
        const idx = prevDayDiff + 6;
        prevBuckets.all[idx]++;
        if (ev.status === 'completed') prevBuckets.completed[idx]++;
      }
    }

    const compute = (
      cur: number[], prev: number[], better: 'up' | 'down'
    ): { sparkline: number[]; deltaPct: number; totalThis: number; totalPrev: number } => {
      const totalThis = cur.reduce((s, v) => s + v, 0);
      const totalPrev = prev.reduce((s, v) => s + v, 0);
      let deltaPct = 0;
      if (totalPrev > 0) {
        deltaPct = Math.round(((totalThis - totalPrev) / totalPrev) * 100);
      } else if (totalThis > 0) {
        deltaPct = 100; // 上期为 0，本期有数据，按 +100% 显示
      }
      void better;
      return { sparkline: cur, deltaPct, totalThis, totalPrev };
    };

    return {
      today: compute(buckets.all, prevBuckets.all, 'up'),
      completed: compute(buckets.completed, prevBuckets.completed, 'up'),
      conflicts: compute(buckets.conflicts, prevBuckets.all, 'down'), // 冲突数越少越好
      tomorrow: compute(buckets.tomorrow, prevBuckets.all, 'up'),
    };
  }, [allEvents]);

  const upcoming = useMemo<CalendarEventDto[]>(() => {
    if (!allEvents.length) return [];
    const today0 = new Date();
    today0.setHours(0, 0, 0, 0);
    return allEvents
      .filter((ev) => { const t = +new Date(ev.startAt); return !isNaN(t) && t >= today0.getTime(); })
      .sort((a, b) => +new Date(a.startAt) - +new Date(b.startAt))
      .slice(0, 5);
  }, [allEvents]);

  // TODO(perf): 合并为 store 复合动作，减少连续 setState 触发的多次 re-render
  const onGoToday = useCallback(() => {
    gotoToday();
    setCalSubView('month');
    setViewMode('calendar');
  }, [gotoToday, setCalSubView, setViewMode]);

  const onList = useCallback(() => setViewMode('list'), [setViewMode]);
  const onCreate = useCallback(() => openCreate(), [openCreate]);

  // TODO(perf): 合并为 store 复合动作，减少连续 setState 触发的多次 re-render
  const onCard1 = useCallback(() => {
    setViewMode('calendar');
    setCalSubView('day');
    gotoToday();
  }, [setViewMode, setCalSubView, gotoToday]);

  // TODO(perf): 合并为 store 复合动作，减少连续 setState 触发的多次 re-render
  const onCard2 = useCallback(() => {
    setCalSubView('week');
    setViewMode('calendar');
  }, [setCalSubView, setViewMode]);

  // TODO(perf): 合并为 store 复合动作，减少连续 setState 触发的多次 re-render
  // P2-A#1: onCard3 现在对应「冲突事件」，跳转到列表视图以便用户查看并处理冲突
  const onCard3 = useCallback(() => {
    setViewMode('list');
  }, [setViewMode]);

  // TODO(perf): 合并为 store 复合动作，减少连续 setState 触发的多次 re-render
  // P2-A#1: onCard4 现在对应「明日日程」，跳转到明日的 day 视图
  const onCard4 = useCallback(() => {
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    useCalendarStore.setState({ calCursor: tomorrow });
    setCalSubView('day');
    setViewMode('calendar');
  }, [setCalSubView, setViewMode]);

  // TODO(perf): 合并为 store 复合动作，减少连续 setState 触发的多次 re-render
  const onBarClick = useCallback(
    (ymd: string) => {
      const d = new Date(ymd);
      useCalendarStore.setState({ calCursor: d });
      setCalSubView('day');
      setViewMode('calendar');
    },
    [setCalSubView, setViewMode]
  );

  const onRowClick = useCallback((ev: CalendarEventDto) => openEdit(ev), [openEdit]);

  const cardLoading = loading;

  return (
    <div className="flex flex-col w-full h-full min-h-0 overflow-y-auto">
      <div className="sticky top-0 z-10 bg-slate-50/95 dark:bg-slate-950/95 backdrop-blur border-b border-slate-200 dark:border-slate-800 px-4 sm:px-6 py-3 sm:py-4 relative overflow-hidden">
        <div className="absolute -top-24 -left-24 w-72 h-72 rounded-full bg-gradient-to-br from-sky-400/10 via-sky-300/5 to-transparent pointer-events-none" />
        <div className="absolute -top-20 right-0 w-80 h-80 rounded-full bg-gradient-to-bl from-violet-400/10 via-violet-300/5 to-transparent pointer-events-none" />
        <div className="absolute bottom-0 left-0 right-0 h-[2px] bg-gradient-to-r from-transparent via-sky-400/60 to-transparent" />
        <div className="relative flex flex-wrap items-center justify-between gap-3">
          <div>
            <h1 className="text-xl sm:text-2xl font-bold bg-gradient-to-r from-slate-800 to-slate-600 dark:from-slate-100 dark:to-slate-300 bg-clip-text text-transparent flex items-center gap-2">
              <span className="inline-block w-1.5 h-5 sm:h-6 rounded-full bg-gradient-to-b from-sky-400 to-violet-500 shadow-[0_0_8px_rgba(14,165,233,0.5)]" />
              {t('calendar.common.overview')}
            </h1>
            <p className="inline-flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400 mt-1">
              <span className="w-1.5 h-1.5 rounded-full bg-sky-500 animate-pulse shadow-[0_0_6px_rgba(14,165,233,0.8)]" />
              {todayLabel}
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <button type="button" onClick={onGoToday} className="px-3 py-1.5 text-xs font-medium rounded-lg border border-slate-300 dark:border-slate-700 bg-white/80 dark:bg-slate-900/80 hover:bg-white dark:hover:bg-slate-800 active:scale-95 transition-all shadow-sm backdrop-blur">
              {t('calendar.common.today')}
            </button>
            <button type="button" onClick={onCreate} className="px-3 py-1.5 text-xs font-medium rounded-lg bg-gradient-to-r from-sky-500 to-blue-600 hover:from-sky-600 hover:to-blue-700 text-white active:scale-95 transition-all shadow-md shadow-sky-500/25 relative overflow-hidden group">
              <span className="absolute inset-0 bg-gradient-to-r from-white/0 via-white/20 to-white/0 -translate-x-full group-hover:translate-x-full transition-transform duration-700" />
              <span className="relative">{t('calendar.views.overview.createEvent')}</span>
            </button>
            <button type="button" onClick={onList} className="px-3 py-1.5 text-xs font-medium rounded-lg border border-slate-300 dark:border-slate-700 bg-white/80 dark:bg-slate-900/80 hover:bg-white dark:hover:bg-slate-800 active:scale-95 transition-all shadow-sm backdrop-blur">
              {t('calendar.views.overview.listView')}
            </button>
          </div>
        </div>
      </div>

      <div className="p-4 sm:p-6 space-y-4 sm:space-y-6">
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
          <KpiCard
            label={t('calendar.views.overview.kpiToday')}
            value={todayCount}
            loading={cardLoading}
            delta={{ pct: kpiSparklines.today.deltaPct, better: 'up' }}
            sparkline={kpiSparklines.today.sparkline}
            tone="amber"
            icon={<IconTodo />}
            onClick={onCard1}
            accent="left"
          />
          <KpiCard
            label={t('calendar.views.overview.kpiCompletedThisWeek')}
            value={completedThisWeek}
            loading={cardLoading}
            delta={{ pct: kpiSparklines.completed.deltaPct, better: 'up' }}
            sparkline={kpiSparklines.completed.sparkline}
            tone="emerald"
            icon={<IconCompleted />}
            onClick={onCard2}
            accent="left"
          />
          <KpiCard
            label={t('calendar.views.overview.kpiConflicts')}
            value={conflictsCount}
            loading={cardLoading}
            delta={{ pct: kpiSparklines.conflicts.deltaPct, better: 'down' }}
            sparkline={kpiSparklines.conflicts.sparkline}
            tone="rose"
            icon={<IconConflict />}
            onClick={onCard3}
            accent="left"
          />
          <KpiCard
            label={t('calendar.views.overview.kpiTomorrow')}
            value={tomorrowCount}
            loading={cardLoading}
            delta={{ pct: kpiSparklines.tomorrow.deltaPct, better: 'up' }}
            sparkline={kpiSparklines.tomorrow.sparkline}
            tone="sky"
            icon={<IconWeek />}
            onClick={onCard4}
            accent="left"
          />
        </div>

        <div className="bg-gradient-to-br from-white to-slate-50/30 dark:from-slate-900 dark:to-slate-800/30 border border-slate-200 dark:border-slate-800 rounded-2xl p-4 sm:p-5 shadow-sm relative overflow-hidden">
          <div className="absolute top-0 left-0 right-0 h-[3px] bg-gradient-to-r from-sky-400 via-violet-400 to-rose-400" />
          <div className="flex items-center justify-between mb-4">
            <div>
              <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-300">{t('calendar.views.overview.weekDistributionTitle')}</h2>
              <p className="text-xs text-slate-400 dark:text-slate-500 mt-0.5">{t('calendar.views.overview.weekDistributionHint')}</p>
            </div>
            <div className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-sky-50 dark:bg-sky-900/20 border border-sky-100 dark:border-sky-800/40">
              <span className="w-1.5 h-1.5 rounded-full bg-sky-500 animate-pulse" />
              <span className="text-[11px] font-medium text-sky-700 dark:text-sky-300">{t('calendar.views.overview.live')}</span>
            </div>
          </div>
          <div className="flex items-end justify-between gap-2 h-28 px-1">
            {weekData.map((b) => <BarCell key={b.date} bar={b} onClick={onBarClick} />)}
          </div>
        </div>

        <div
          data-sparkline-metric="events"
          className="rounded-2xl border border-slate-200 dark:border-slate-800 p-4 sm:p-5 bg-gradient-to-br from-white via-slate-50/40 to-violet-50/20 dark:from-slate-900 dark:via-slate-800/40 dark:to-violet-950/20 shadow-sm relative overflow-hidden"
        >
          <div className="absolute top-0 left-0 w-1 h-full bg-gradient-to-b from-violet-400 to-sky-400" />
          <div className="flex items-center justify-between mb-3">
            <div>
              <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-300 flex items-center gap-1.5">
                <span className="w-1.5 h-1.5 rounded-full bg-violet-500" />
                {t('calendar.views.overview.trendTitle')}
              </h2>
              <p className="text-xs text-slate-400 dark:text-slate-500 mt-0.5">{t('calendar.views.overview.trendHint')}</p>
            </div>
            <span className="text-[10px] font-medium text-slate-300 dark:text-slate-600 px-2 py-1 rounded-md border border-slate-200/60 dark:border-slate-700/60 bg-white/50 dark:bg-slate-800/50">TREND</span>
          </div>
          <div className="flex items-end gap-1 h-10">
            {kpiSparklines.today.sparkline.map((h, i) => {
              const maxV = Math.max(1, ...kpiSparklines.today.sparkline);
              return (
                <div
                  key={i}
                  className="flex-1 bg-gradient-to-t from-sky-400/60 to-violet-400/70 rounded-t-md hover:from-sky-500 hover:to-violet-500 hover:brightness-110 transition-all duration-200"
                  style={{ height: `${(h / maxV) * 100}%` }}
                  title={t('calendar.views.overview.sparklineTooltip', { day: i + 1, count: h })}
                />
              );
            })}
          </div>
        </div>

        <div className="bg-gradient-to-br from-white to-slate-50/30 dark:from-slate-900 dark:to-slate-800/30 border border-slate-200 dark:border-slate-800 rounded-2xl shadow-sm overflow-hidden relative">
          <div className="absolute top-0 left-0 right-0 h-[3px] bg-gradient-to-r from-emerald-400 via-sky-400 to-violet-400" />
          <div className="px-4 sm:px-5 py-3 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between bg-gradient-to-r from-slate-50/70 via-white to-emerald-50/40 dark:from-slate-900/70 dark:via-slate-900 dark:to-emerald-950/20">
            <div>
              <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-300 flex items-center gap-1.5">
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 shadow-[0_0_6px_rgba(16,185,129,0.7)]" />
                {t('calendar.views.overview.upcomingTitle')}
              </h2>
              <p className="text-xs text-slate-400 dark:text-slate-500 mt-0.5">{t('calendar.views.overview.upcomingHint')}</p>
            </div>
            <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold bg-emerald-50 dark:bg-emerald-900/20 text-emerald-700 dark:text-emerald-300 border border-emerald-100 dark:border-emerald-800/40">
              {t('calendar.views.overview.upcomingCount', { count: upcoming.length })}
            </span>
          </div>
          {cardLoading || upcoming.length === 0 ? (
            <div className="px-4 sm:px-5 py-12 text-center text-sm text-slate-500 dark:text-slate-400">
              {cardLoading ? (
                <div className="space-y-2 max-w-md mx-auto">
                  <ShimmerBar className="h-10 w-full" />
                  <ShimmerBar className="h-10 w-full" />
                  <ShimmerBar className="h-10 w-3/4 mx-auto" />
                </div>
              ) : (
                <div className="flex flex-col items-center gap-3">
                  <div className="w-16 h-16 rounded-2xl bg-gradient-to-br from-slate-100 to-slate-50 dark:from-slate-800 dark:to-slate-900 flex items-center justify-center ring-4 ring-white dark:ring-slate-900 shadow-inner">
                    <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" className="text-slate-400">
                      <rect x="3" y="4" width="18" height="18" rx="2" />
                      <line x1="16" y1="2" x2="16" y2="6" />
                      <line x1="8" y1="2" x2="8" y2="6" />
                      <line x1="3" y1="10" x2="21" y2="10" />
                      <line x1="12" y1="14" x2="12" y2="18" />
                      <line x1="10" y1="16" x2="14" y2="16" />
                    </svg>
                  </div>
                  <p className="text-sm font-medium text-slate-600 dark:text-slate-300">{t('calendar.views.overview.upcomingEmpty')}</p>
                  <button type="button" onClick={onCreate} className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-lg bg-gradient-to-r from-sky-500 to-blue-600 hover:from-sky-600 hover:to-blue-700 text-white shadow-md shadow-sky-500/25 active:scale-95 transition-all">
                    {t('calendar.views.overview.createFirstEvent')}
                  </button>
                </div>
              )}
            </div>
          ) : (
            <>
              {/* PC 表格视图 */}
              <div className="hidden md:block">
                <table className="w-full border-collapse">
                  <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
                    {upcoming.slice(0, 5).map((ev) => <EventRow key={ev.recurrenceOriginalStart ? `${ev.id}#${ev.recurrenceOriginalStart}` : `${ev.id}`} ev={ev} onClick={onRowClick} />)}
                  </tbody>
                </table>
              </div>
              {/* 移动端卡片视图 */}
              <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
                {upcoming.slice(0, 5).map((ev) => <EventMobileCard key={ev.recurrenceOriginalStart ? `${ev.id}#${ev.recurrenceOriginalStart}` : `${ev.id}`} ev={ev} onClick={onRowClick} />)}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
