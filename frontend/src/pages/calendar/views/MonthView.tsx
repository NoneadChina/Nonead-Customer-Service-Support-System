/* eslint-disable react-refresh/only-export-components -- 文件同时导出组件与工具函数，fast refresh 非关注点 */
import React, { memo, useMemo, useCallback, useState, useRef, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { useCalendarStore, useCalendarShallow } from '../store';
import type { CalendarEventDto } from '../../../api/types';
import type { YMD } from '../calendarUtils';
import { parseYMD } from '../calendarUtils';
import { EventChip, MoreEventsChip, EmptyState } from '../chips';
import { useCalendarDrag, _checkConflictForRange, type ConflictCheckResult, type ConflictTag } from '../hooks';
import { useAuth } from '../../../context/AuthContext';
import { useI18n } from '../../../context/I18nContext';
import { getBoundLocale } from '../../../i18n/locale';
import type { TranslationKey } from '../../../locales/zh-Hans';
import { usePermission } from '../../../hooks/usePermission';
import { Solar } from 'lunar-javascript';
import { Plus, CalendarDays, Copy, AlertTriangle } from 'lucide-react';
import { EventTypeFilterBar } from '../components/EventTypeFilterBar';
import { BatchMultiSelectToolbar } from '../components/BatchMultiSelectToolbar';

const _EMPTY_NUM_ARR: number[] = [];
const _EMPTY_ARR: CalendarEventDto[] = [];
Object.freeze(_EMPTY_NUM_ARR);
Object.freeze(_EMPTY_ARR);

/**
 * 比较两个 CalendarEventDto 是否在 EventChip 视觉渲染上等价。
 * 用于 DayCell.memo 比较函数，避免仅比较 id 指纹导致 eventType/status/color 等字段变化时
 * 跳过重渲染，进而使 EventChip 接收旧 ev 引用显示旧颜色（含 fallback 灰色样式）。
 *
 * 性能：仅比较影响 EventChip 视觉的关键标量字段，避免深度比较整个 DTO。
 */
function _isSameEventForRender(a: CalendarEventDto, b: CalendarEventDto): boolean {
  // 引用相同 → 一定等价（快速路径）
  if (a === b) return true;
  // 引用不同 → 逐字段比较影响渲染的关键字段
  return (
    a.id === b.id &&
    a.eventType === b.eventType &&
    a.status === b.status &&
    a.color === b.color &&
    a.title === b.title &&
    a.startAt === b.startAt &&
    a.endAt === b.endAt &&
    a.isAllDay === b.isAllDay &&
    a.deletedAt === b.deletedAt &&
    a.createdByName === b.createdByName &&
    a.eventTypeLabel === b.eventTypeLabel &&
    a.statusLabel === b.statusLabel &&
    a.priority === b.priority &&
    a.location === b.location &&
    a.meetingUrl === b.meetingUrl &&
    a.description === b.description &&
    a.conversationSubject === b.conversationSubject &&
    a.mailboxName === b.mailboxName &&
    a.userName === b.userName &&
    a.recurrenceOriginalStart === b.recurrenceOriginalStart &&
    a.reminderMinutes === b.reminderMinutes
  );
}

/** 冲突检测统一入口：基于 hooks._checkConflictForRange 的薄封装，返回 tag/id列表/重叠分钟/冲突摘要。
 * 供 DayCell 逐事件计算冲突；allEvents 当日所有事件（含全天，函数内部会过滤全天事件）。 */
function checkConflictForEvent(
  ev: Pick<CalendarEventDto, 'id' | 'startAt' | 'endAt' | 'isAllDay'>,
  allEvents: readonly CalendarEventDto[],
): ConflictCheckResult {
  if (ev.isAllDay) {
    return { tag: 'none' as ConflictTag, conflictIds: [], overlapMinutes: 0, conflictWith: [] };
  }
  return _checkConflictForRange({
    startAt: ev.startAt,
    endAt: ev.endAt ?? undefined,
    existingEvents: allEvents as CalendarEventDto[],
    excludeId: ev.id,
  });
}

/** 日格级冲突检测：返回当日级别是否存在任一硬/软冲突（用于日期角红点）。
 *  优化：一次 O(N^2) 两两比较即可，无需重复调用单事件检测函数。 */
function computeDayHasHardConflict(evts: readonly CalendarEventDto[]): { hasHard: boolean; hasSoft: boolean } {
  const timed = evts.filter(e => !e.isAllDay);
  let hasHard = false;
  let hasSoft = false;
  for (let i = 0; i < timed.length; i++) {
    for (let j = i + 1; j < timed.length; j++) {
      const a = timed[i];
      const b = timed[j];
      const aS = +new Date(a.startAt);
      const aE = a.endAt ? +new Date(a.endAt) : aS + 3600_000;
      const bS = +new Date(b.startAt);
      const bE = b.endAt ? +new Date(b.endAt) : bS + 3600_000;
      const overlap = Math.min(aE, bE) - Math.max(aS, bS);
      if (overlap > 0) {
        const min = overlap / 60_000;
        if (min > 15) hasHard = true;
        else hasSoft = true;
        if (hasHard && hasSoft) return { hasHard, hasSoft }; // 提前剪枝
      }
    }
  }
  return { hasHard, hasSoft };
}

/* =========================================================
   build42GridDays —— 以 MonthDayInfo 对象返回 42 天
   ========================================================= */
interface MonthDayInfo {
  date: Date;
  ymd: YMD;
  inMonth: boolean;
  isToday: boolean;
  isCursor: boolean;
  weekday06: number;
}

function _toMidnightYMD(y: number, m: number, d: number): string {
  return `${y}-${String(m + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
}

export function _build42GridDays(calMonth: Date, cursorDate: Date, todayDate: Date = new Date()): MonthDayInfo[] {
  const year = calMonth.getFullYear();
  const monthIdx = calMonth.getMonth();
  const firstOfMonth = new Date(year, monthIdx, 1);
  const start = new Date(firstOfMonth);
  start.setDate(start.getDate() - firstOfMonth.getDay());
  const todayYMD = _toMidnightYMD(todayDate.getFullYear(), todayDate.getMonth(), todayDate.getDate());
  const cursorYMD = _toMidnightYMD(cursorDate.getFullYear(), cursorDate.getMonth(), cursorDate.getDate());
  const out: MonthDayInfo[] = [];
  for (let i = 0; i < 42; i++) {
    const d = new Date(start);
    d.setDate(start.getDate() + i);
    const ymd = _toMidnightYMD(d.getFullYear(), d.getMonth(), d.getDate());
    out.push({
      date: d,
      ymd,
      inMonth: d.getMonth() === monthIdx,
      isToday: ymd === todayYMD,
      isCursor: ymd === cursorYMD,
      weekday06: d.getDay(),
    });
  }
  return out;
}

/* =========================================================
   DayCell（memo 友好：Props 纯值化，不传 events 数组）
   ========================================================= */
interface DayCellProps {
  day: MonthDayInfo;
  previewEvents: readonly CalendarEventDto[];
  moreCount: number;
  isDragOver: boolean;
  isDragging: boolean;
  todayLabel: string;
  onOpenEvent: (ev: CalendarEventDto) => void;
  onOpenMore: (ymd: YMD) => void;
  onChipDragStart: (ev: CalendarEventDto, e: React.DragEvent<HTMLDivElement>) => void;
  onDragOverDay: (e: React.DragEvent<HTMLDivElement>) => void;
  onDragLeaveDay: (e: React.DragEvent<HTMLDivElement>) => void;
  onDropDay: (e: React.DragEvent<HTMLDivElement>) => void;
  /** P2-1: 右键菜单占位回调（未来接入模板选择器） */
  onDayContextMenu?: (ymd: YMD, e: React.MouseEvent<HTMLDivElement>) => void;
  /** 交互增强-1: 双击日格快速新建事件（预填日期） */
  onDoubleClickDay?: (date: Date) => void;
  /** P1-2: 多选模式标记 */
  multiSelectMode: boolean;
  /** P1-2: 当前已选事件 id 列表（用于渲染选中态 ring） */
  selectedIds: number[];
  /** 该日是否存在硬冲突事件（用于日期右上角红色冲突点） */
  hasHardConflict?: boolean;
  /** 该日是否存在软冲突事件（用于日期右上角琥珀色冲突点，优先级低于硬冲突） */
  hasSoftConflict?: boolean;
  /** 当日全部事件（用于计算每个预览事件的冲突数，与 previewEvents 同源） */
  allDayEvents: readonly CalendarEventDto[];
  /** Task E: drop 成功/失败闪烁色调 */
  flashTone?: 'ok' | 'err' | null;
  /** 第三梯队: 键盘导航焦点事件 id（用于 chip ring 高亮） */
  focusedEventId?: number | null;
  /** P2-A#3: 拖拽至该日格时预测的冲突检测结果（仅 isDragOver && isDragging 时有效） */
  dragConflict?: ConflictCheckResult | null;
  /** P2-B#1: role=grid 行索引（1 开始） */
  ariaRowIndex?: number;
  /** P2-B#1: role=grid 列索引（1 开始） */
  ariaColIndex?: number;
}

function DayCellImpl(p: DayCellProps) {
  const {
    day, previewEvents, moreCount, isDragOver, isDragging, todayLabel,
    onOpenEvent, onOpenMore, onChipDragStart, onDragOverDay, onDragLeaveDay, onDropDay,
    onDayContextMenu, onDoubleClickDay, multiSelectMode, selectedIds, hasHardConflict, hasSoftConflict, allDayEvents,
    flashTone, focusedEventId, dragConflict, ariaRowIndex, ariaColIndex,
  } = p;
  // P2-A#3: 拖拽冲突时根据 hard/soft 改变拖拽悬浮高亮色（危险红/警告琥珀），与 flashTone 叠加
  const dragConflictHighlight = isDragOver && dragConflict
    ? dragConflict.tag === 'hard'
      ? '!bg-rose-50/70 dark:!bg-rose-950/50 !border-rose-400 dark:!border-rose-700 !ring-rose-400/40'
      : '!bg-amber-50/70 dark:!bg-amber-950/40 !border-amber-400 dark:!border-amber-700 !ring-amber-400/40'
    : '';
  const dimChips = isDragging && !isDragOver;
  const flashClass = flashTone
    ? flashTone === 'ok'
      ? 'bg-emerald-50 dark:bg-emerald-950/40 ring-2 ring-emerald-400/50 dark:ring-emerald-700/40 transition-colors duration-150'
      : 'bg-rose-50 dark:bg-rose-950/40 ring-2 ring-rose-400/50 dark:ring-rose-700/40 transition-colors duration-150'
    : '';

  const { t } = useI18n();

  // 计算公历 + 农历显示文本（平板/手机端通过 CSS max-md:hidden 隐藏农历）
  const { solarText, lunarText } = useMemo(() => {
    const date = day.date;
    const dayNum = date.getDate();
    const monthNum = date.getMonth() + 1;
    const isFirstDay = day.inMonth && dayNum === 1;
    const solarText = isFirstDay
      ? t('calendar.views.month.monthDay', { month: monthNum, day: dayNum })
      : t('calendar.views.shared.daySuffix', { day: dayNum });

    let lunarText = '';
    // 英文界面隐藏农历注记（队长修复 W13-V3，理由见 `calendarUtils.getLunarDayLabel` 的注释：
    // 该库只有中文数据集，英文界面下会渲染 `初二`/`廿一` 与混排的 `八 (lunar 1st)`）
    if (!getBoundLocale().toLowerCase().startsWith('en')) {
      try {
        const lunar = Solar.fromDate(date).getLunar();
        lunarText = lunar.getDayInChinese() ?? '';
        // 农历初一用"月名+初一"替代（更直观）
        // 判定用 getDay() === 1（库的天序号），与原 `=== '初一'` 等价但不引入中文字面量：
        // 对 2026 年 12 个月 × 28 天共 336 个样本实测两者 0 处不一致。
        if (lunar.getDay() === 1) {
          lunarText = t('calendar.views.month.lunarFirstDay', { month: lunar.getMonthInChinese() });
        }
      } catch (_e) {
        // lunar-javascript 范围外日期静默降级
      }
    }
    return { solarText, lunarText };
  }, [day.date, day.inMonth, t]);

  // 平板/手机（<768px）：更小的 badge、字号、padding；隐藏农历节省显示空间
  const dateBadgeBase = 'relative inline-flex items-center gap-1 max-md:h-5 max-md:px-1.5 h-7 px-2 rounded-full max-md:text-[10px] text-[11px] font-medium transition-transform group-hover:scale-[1.05]';
  const dateBadgeTone = day.isToday
    ? 'bg-gradient-to-br from-sky-500 to-blue-600 text-white shadow-md shadow-sky-500/30 ring-2 ring-sky-200 dark:ring-sky-800/50'
    : day.inMonth ? 'text-slate-700 dark:text-slate-300' : 'text-slate-400';
  const lunarTone = [
    day.isToday ? 'text-sky-100/90' : (day.inMonth ? 'text-slate-400 dark:text-slate-500' : 'text-slate-300 dark:text-slate-600'),
    'max-md:hidden', // 平板/手机浏览器不显示农历日期
  ].join(' ');

  // P2-B#1: cell 对外的无障碍标签（日期 + 是否今日 + 冲突信息 + 事件条数）
  const cellAriaLabel = useMemo(() => {
    const date = day.date;
    const parts: string[] = [];
    parts.push(t('calendar.views.month.ariaDate', {
      year: date.getFullYear(),
      month: date.getMonth() + 1,
      day: date.getDate(),
    }));
    if (!day.inMonth) parts.push(t('calendar.views.month.ariaOutsideMonth'));
    if (day.isToday) parts.push(t('calendar.common.today'));
    if (hasHardConflict) parts.push(t('calendar.views.month.ariaHardConflict', { count: previewEvents.length + moreCount }));
    else if (hasSoftConflict) parts.push(t('calendar.views.month.ariaSoftConflict'));
    if (previewEvents.length > 0 || moreCount > 0) {
      parts.push(t('calendar.views.month.ariaEventCount', { count: previewEvents.length + moreCount }));
    }
    return parts.join(t('calendar.views.month.ariaSeparator'));
  }, [day, hasHardConflict, hasSoftConflict, previewEvents.length, moreCount, t]);

  return (
    <div
      data-ymd={day.ymd}
      role="gridcell"
      tabIndex={0}
      aria-rowindex={ariaRowIndex}
      aria-colindex={ariaColIndex}
      aria-label={cellAriaLabel}
      aria-selected={selectedIds.length > 0 ? true : undefined}
      onDragOver={onDragOverDay}
      onDragLeave={onDragLeaveDay}
      onDrop={onDropDay}
      onContextMenu={(e) => { e.preventDefault(); onDayContextMenu?.(day.ymd, e); }}
      onDoubleClick={() => { if (day.inMonth) onDoubleClickDay?.(day.date); }}
      title={day.inMonth ? t('calendar.views.month.doubleClickHint') : undefined}
      className={[
        'cal-day-cell group relative flex flex-col h-full max-md:min-h-[64px] max-md:px-1 max-md:py-1 min-h-[96px] py-[length:var(--cal-cell-py)] px-2 border max-md:rounded-md rounded-lg overflow-visible transition-all outline-none focus-visible:ring-2 focus-visible:ring-sky-400 focus-visible:ring-offset-2 focus-visible:ring-offset-white dark:focus-visible:ring-offset-slate-900',
        day.inMonth
          ? 'bg-white dark:bg-slate-900 hover:shadow-md hover:border-sky-200 dark:hover:border-sky-800'
          : 'bg-slate-50/50 dark:bg-slate-950/50 opacity-50',
        day.isCursor ? 'ring-2 ring-sky-400/70 ring-offset-1 ring-offset-white dark:ring-offset-slate-900 z-10' : '',
        isDragOver ? 'bg-sky-50/80 dark:bg-sky-950/40 border-sky-300 dark:border-sky-700 ring-1 ring-sky-300/30 animate-[cal-pulse-ring_240ms_cubic-bezier(0,0,0,2,1)]' : 'border-slate-200 dark:border-slate-800',
        dragConflictHighlight,
        flashClass,
      ].filter(Boolean).join(' ')}
    >
      <div className="flex items-center justify-between mb-1">
        <div className={[dateBadgeBase, dateBadgeTone].join(' ')}>
          <span>{solarText}</span>
          {lunarText && (
            <>
              <span className={lunarTone}>·</span>
              <span className={[lunarTone, 'font-normal'].join(' ')}>{lunarText}</span>
            </>
          )}
          {hasHardConflict && <span className="cal-date-conflict-dot" aria-label={t('calendar.views.month.hardConflictDot')} />}
          {!hasHardConflict && hasSoftConflict && (
            <span
              className="cal-date-conflict-dot"
              style={{ background: '#f59e0b', boxShadow: '0 0 0 2px #ffffff, 0 1px 4px rgba(245,158,11,0.5)' }}
              aria-label={t('calendar.views.month.softConflictDot')}
            />
          )}
        </div>
        {day.isToday && <span className="text-[10px] text-sky-600 dark:text-sky-400 font-medium mr-1">{todayLabel}</span>}
      </div>
      <div className="cal-chips-scroller flex flex-col gap-1 min-h-0">
        {previewEvents.map((ev) => {
          const sel = multiSelectMode && selectedIds.includes(ev.id);
          const check = checkConflictForEvent(ev, allDayEvents);
          return (
            <EventChip
              key={ev.recurrenceOriginalStart ? `${ev.id}#${ev.recurrenceOriginalStart}` : `${ev.id}`}
              ev={ev}
              ymdKey={day.ymd}
              compact={previewEvents.length > 2}
              dimmed={dimChips}
              focused={ev.id === focusedEventId}
              selected={sel}
              onOpen={onOpenEvent}
              onDragStart={onChipDragStart}
              conflictCount={check.conflictIds.length}
              conflictTag={check.tag}
              conflictWith={check.conflictWith}
            />
          );
        })}
        {moreCount > 0 && (
          <MoreEventsChip count={moreCount} onOpen={() => onOpenMore(day.ymd)} />
        )}
      </div>
      {/* P2-A#3: 拖拽冲突徽章 —— 仅当拖拽悬浮于该日格且存在预测冲突时显示 */}
      {isDragOver && dragConflict && dragConflict.conflictIds.length > 0 && (
        <div
          className={[
            'absolute bottom-1.5 right-1.5 z-10 inline-flex items-center gap-1 px-1.5 py-0.5 rounded-md text-[10px] font-semibold shadow-sm ring-1 backdrop-blur-sm',
            dragConflict.tag === 'hard'
              ? 'bg-rose-600 text-white ring-rose-300/50 dark:ring-rose-500/40'
              : 'bg-amber-500 text-white ring-amber-300/50 dark:ring-amber-500/40',
          ].join(' ')}
          title={
            t('calendar.views.month.dragConflictTitle', {
              severity: dragConflict.tag === 'hard'
                ? t('calendar.views.month.severityHard')
                : t('calendar.views.month.severitySoft'),
              count: dragConflict.conflictIds.length,
              minutes: dragConflict.overlapMinutes,
            }) + (dragConflict.conflictWith?.length
              ? t('calendar.views.month.dragConflictWith', {
                  names: dragConflict.conflictWith.slice(0, 2).map((c) => c.title).filter(Boolean)
                    .join(t('calendar.views.shared.nameSeparator')),
                  etc: dragConflict.conflictWith.length > 2 ? t('calendar.views.month.dragMoreSuffix') : '',
                })
              : '')
          }
          aria-label={dragConflict.tag === 'hard' ? t('calendar.views.month.dragHardConflictAria') : t('calendar.views.month.dragSoftConflictAria')}
        >
          <AlertTriangle className="w-3 h-3 shrink-0" />
          {dragConflict.conflictIds.length > 1
            ? t('calendar.views.month.conflictCountBadge', { count: dragConflict.conflictIds.length })
            : t('calendar.views.month.conflictBadge')}
        </div>
      )}
    </div>
  );
}

export const DayCell = memo(DayCellImpl, (a, b) => {
  if (a.day !== b.day) return false;
  // previewEvents 用关键字段比较（id 指纹不足以捕获 eventType/status/color 变化）
  // 修复"偶发日历颜色显示全部变灰"BUG：仅比较 id 时，修改事件的 eventType/status
  // 不会触发 DayCell 重渲染，导致 EventChip 接收旧 ev 引用，颜色走 fallback 灰色样式
  {
    const pa = a.previewEvents, pb = b.previewEvents;
    if (pa.length !== pb.length) return false;
    const L = pa.length;
    for (let i = 0; i < L; i++) if (!_isSameEventForRender(pa[i], pb[i])) return false;
  }
  if (a.moreCount !== b.moreCount) return false;
  if (a.isDragOver !== b.isDragOver) return false;
  if (a.isDragging !== b.isDragging) return false;
  if (a.todayLabel !== b.todayLabel) return false;
  if (a.onOpenEvent !== b.onOpenEvent) return false;
  if (a.onOpenMore !== b.onOpenMore) return false;
  if (a.onChipDragStart !== b.onChipDragStart) return false;
  if (a.onDragOverDay !== b.onDragOverDay) return false;
  if (a.onDragLeaveDay !== b.onDragLeaveDay) return false;
  if (a.onDropDay !== b.onDropDay) return false;
  // P2-1: 右键菜单回调
  if (a.onDayContextMenu !== b.onDayContextMenu) return false;
  // 交互增强-1: 双击新建回调
  if (a.onDoubleClickDay !== b.onDoubleClickDay) return false;
  // P1-2: 多选模式标记
  if (a.multiSelectMode !== b.multiSelectMode) return false;
  // P1-2: 已选 id 列表（逐元素比较，避免 Set 引用抖动导致 memo 失效）
  {
    const sa = a.selectedIds, sb = b.selectedIds;
    if (sa.length !== sb.length) return false;
    const M = sa.length;
    for (let i = 0; i < M; i++) if (sa[i] !== sb[i]) return false;
  }
  if (a.hasHardConflict !== b.hasHardConflict) return false;
  if (a.hasSoftConflict !== b.hasSoftConflict) return false;
  if (a.flashTone !== b.flashTone) return false;
  if (a.focusedEventId !== b.focusedEventId) return false;
  // P2-A#3: dragConflict 浅字段比较（如果引用不同则逐字段对比）
  {
    const ca = a.dragConflict; const cb = b.dragConflict;
    if (ca !== cb) {
      if (!ca || !cb) return false;
      if (ca.tag !== cb.tag) return false;
      if (ca.overlapMinutes !== cb.overlapMinutes) return false;
      const ia = ca.conflictIds; const ib = cb.conflictIds;
      if (ia.length !== ib.length) return false;
      for (let i = 0; i < ia.length; i++) if (ia[i] !== ib[i]) return false;
      // conflictWith: 简单长度 + 首条 title 比较
      const wa = ca.conflictWith; const wb = cb.conflictWith;
      if (wa.length !== wb.length) return false;
      for (let i = 0; i < wa.length; i++) if (wa[i].title !== wb[i].title) return false;
    }
  }
  // allDayEvents 用关键字段比较（与 previewEvents 同策略，捕获影响渲染的字段变化）
  {
    const aa = a.allDayEvents, ab = b.allDayEvents;
    if (aa.length !== ab.length) return false;
    const L = aa.length;
    for (let i = 0; i < L; i++) if (!_isSameEventForRender(aa[i], ab[i])) return false;
  }
  return true;
});

/* =========================================================
   MonthView 根（42 grid）
   ========================================================= */
const WEEK_LABEL_KEYS: TranslationKey[] = [
  'calendar.common.weekdayShort.sun',
  'calendar.common.weekdayShort.mon',
  'calendar.common.weekdayShort.tue',
  'calendar.common.weekdayShort.wed',
  'calendar.common.weekdayShort.thu',
  'calendar.common.weekdayShort.fri',
  'calendar.common.weekdayShort.sat',
];

export default function MonthView() {
  const { t } = useI18n();
  const { user } = useAuth();
  const { canManageCalendar } = usePermission();
  const isMultiUserView = user ? new Set([2, 3]).has(user.role) : false;
  void isMultiUserView;
  void canManageCalendar;

  // P1-2: 多选模式 local state（视图自身维护，切换/退出时清空选择）
  const [multiSelectMode, setMultiSelectMode] = useState(false);

  // Task E: drop 成功/失败闪烁态
  const [flashCell, setFlashCell] = useState<{ ymd: YMD; tone: 'ok' | 'err' } | null>(null);
  const flashTimerRef = useRef<number | null>(null);

  // P2-1: 右键上下文菜单状态
  const [contextMenu, setContextMenu] = useState<{ x: number; y: number; ymd: YMD; date: Date } | null>(null);

  // T000101: 今日蓝框跨天刷新 —— 月视图与周/日视图对齐，用 nowTick 驱动“今天”高亮随时间更新，
  // 避免页面跨午夜仍停留在昨天（“今天蓝框显示在前一天”）。
  const [nowTick, setNowTick] = useState(() => Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setNowTick(Date.now()), 30000);
    return () => window.clearInterval(id);
  }, []);

  const selector = useCallback((s: ReturnType<typeof useCalendarStore.getState>) => ({
    calMonth: s.calMonth,
    calCursor: s.calCursor,
    eventsByDate: s.eventsByDate,
    dragOverDateKey: s.dragOverDateKey,
    draggedEvent: s.draggedEvent,
    // P1-2: 批量选择
    selectedEventIds: s.selectedEventIds,
    // P2-2: 空状态判断
    allEvents: s.allEvents,
    // 第三梯队: 键盘导航焦点
    focusedEventId: s.focusedEventId,
  }), []);

  const selected = useCalendarShallow(selector);
  const { calMonth, calCursor, eventsByDate, dragOverDateKey, draggedEvent, selectedEventIds, allEvents, focusedEventId } = selected;

  const openEdit = useCalendarStore((s) => s.openEdit);
  const openCreate = useCalendarStore((s) => s.openCreate);
  const openSelectedDateEvents = useCalendarStore((s) => s.openSelectedDateEvents);
  // 第三梯队: 事件类型筛选（供 filteredEventsByDate 过滤使用，筛选条组件内部也会自行订阅）
  const enabledEventTypes = useCalendarStore((s) => s.enabledEventTypes);
  // 重要：邮箱筛选必须与 store.getEventsByYMD 逻辑一致，否则月/周/日视图显示结果不同
  const enabledMailboxIds = useCalendarStore((s) => s.enabledMailboxIds);
  const setDragOver = useCalendarStore((s) => s.setDragOver);
  // P1-2: 批量选择 actions
  const selectEvent = useCalendarStore((s) => s.selectEvent);
  const deselectEvent = useCalendarStore((s) => s.deselectEvent);
  const clearSelection = useCalendarStore((s) => s.clearSelection);
  const selectAll = useCalendarStore((s) => s.selectAll);
  const isSelected = useCalendarStore((s) => s.isSelected);
  // P2-1: 切换日历子视图（用于右键菜单跳转日视图）
  const setCalSubView = useCalendarStore((s) => s.setCalSubView);
  const drag = useCalendarDrag();

  useEffect(() => {
    return () => {
      if (flashTimerRef.current !== null) {
        window.clearTimeout(flashTimerRef.current);
        flashTimerRef.current = null;
      }
    };
  }, []);

  // P2-1: 右键菜单打开时，监听 document click / Escape 关闭菜单
  useEffect(() => {
    if (!contextMenu) return;
    const onDocClick = () => setContextMenu(null);
    const onKeydown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setContextMenu(null);
    };
    document.addEventListener('click', onDocClick);
    document.addEventListener('keydown', onKeydown);
    return () => {
      document.removeEventListener('click', onDocClick);
      document.removeEventListener('keydown', onKeydown);
    };
  }, [contextMenu]);

  const grid = useMemo(() => _build42GridDays(calMonth, calCursor, new Date(nowTick)), [calMonth, calCursor, nowTick]);

  // 第三梯队: 按启用的事件类型 + 启用的邮箱过滤 eventsByDate（空集 = 全选）
  // 必须与 store.getEventsByYMD 的过滤逻辑保持一致，否则三个视图显示结果不一致
  const filteredEventsByDate = useMemo(() => {
    const hasType = enabledEventTypes.size > 0;
    const hasMb = enabledMailboxIds.size > 0;
    if (!hasType && !hasMb) return eventsByDate;
    const filtered = new Map<YMD, CalendarEventDto[]>();
    eventsByDate.forEach((events, ymd) => {
      const filteredEvents = events.filter((ev) => {
        if (hasType && !enabledEventTypes.has(ev.eventType)) return false;
        if (hasMb && ev.mailboxId != null && !enabledMailboxIds.has(ev.mailboxId)) return false;
        return true;
      });
      if (filteredEvents.length > 0) {
        filtered.set(ymd, filteredEvents);
      }
    });
    return filtered;
  }, [eventsByDate, enabledEventTypes, enabledMailboxIds]);

  // P1-2: 当前 42 格网格中所有事件 id（供"全选"按钮使用）
  const allGridEventIds = useMemo(() => {
    const ids: number[] = [];
    for (const d of grid) {
      const evts = filteredEventsByDate.get(d.ymd);
      if (evts) for (const ev of evts) ids.push(ev.id);
    }
    return ids;
  }, [grid, filteredEventsByDate]);

  // P1-2: 已选 id 数组（memo 友好：内容比较，避免 Set 引用抖动）
  const selectedIdsArr = useMemo(() => {
    if (!multiSelectMode) return _EMPTY_NUM_ARR;
    return Array.from(selectedEventIds).sort((a, b) => a - b);
  }, [multiSelectMode, selectedEventIds]);

  const onOpenMore = openSelectedDateEvents;
  const onChipDragStart = drag.onChipDragStart;

  // P1-2: toggle 单个事件选中状态
  const handleToggleSelect = useCallback((id: number) => {
    if (isSelected(id)) deselectEvent(id);
    else selectEvent(id);
  }, [isSelected, selectEvent, deselectEvent]);

  // P1-2: chip 点击行为 —— 多选模式下切换选中，否则打开编辑器（原有行为不变）
  const handleChipClick = useCallback((ev: CalendarEventDto) => {
    if (multiSelectMode) {
      handleToggleSelect(ev.id);
    } else {
      openEdit(ev);
    }
  }, [multiSelectMode, handleToggleSelect, openEdit]);

  const onOpenEvent = handleChipClick;

  // P1-A#4: 多选进入/退出（退出时若有选择则先清空）
  const handleEnterMultiSelect = useCallback(() => setMultiSelectMode(true), []);
  const handleExitMultiSelect = useCallback(() => {
    clearSelection();
    setMultiSelectMode(false);
  }, [clearSelection]);

  // P1-A#4: 全选行为（月视图特有：限定当月 DayCell 中展示的事件 ID）
  const handleSelectAllVisible = useCallback(() => {
    selectAll(allGridEventIds);
  }, [selectAll, allGridEventIds]);

  // P2-1: 右键日格弹出上下文菜单（含边界检测，防止菜单溢出视口）
  const onDayContextMenu = useCallback((ymd: YMD, e: React.MouseEvent<HTMLDivElement>) => {
    e.preventDefault();
    const { y, m, d } = parseYMD(ymd);
    const date = new Date(y, m - 1, d);
    const MENU_WIDTH = 180;
    const MENU_HEIGHT = 140;
    let x = e.clientX;
    let yy = e.clientY;
    if (x + MENU_WIDTH > window.innerWidth) x -= MENU_WIDTH;
    if (yy + MENU_HEIGHT > window.innerHeight) yy -= MENU_HEIGHT;
    setContextMenu({ x, y: yy, ymd, date });
  }, []);

  // P2-1: 右键菜单项 —— 新建事件
  const handleCtxNewEvent = useCallback(() => {
    if (!contextMenu) return;
    openCreate(contextMenu.date);
    setContextMenu(null);
  }, [contextMenu, openCreate]);

  // P2-1: 右键菜单项 —— 跳转到日视图
  const handleCtxGotoDay = useCallback(() => {
    if (!contextMenu) return;
    useCalendarStore.setState({ calCursor: contextMenu.date });
    setCalSubView('day');
    setContextMenu(null);
  }, [contextMenu, setCalSubView]);

  // P2-1: 右键菜单项 —— 复制日期 YYYY-MM-DD 到剪贴板
  const handleCtxCopyDate = useCallback(async () => {
    if (!contextMenu) return;
    const text = contextMenu.ymd;
    const showToast = useCalendarStore.getState().showTransientToast;
    try {
      await navigator.clipboard.writeText(text);
      showToast(t('calendar.views.month.dateCopied', { date: text }), 'success', 2000);
    } catch {
      try {
        const ta = document.createElement('textarea'); ta.value = text; document.body.appendChild(ta); ta.select(); document.execCommand('copy'); document.body.removeChild(ta);
        showToast(t('calendar.views.month.dateCopiedFallback', { date: text }), 'success', 2000);
      } catch {
        showToast(t('calendar.views.month.copyFailed'), 'error', 2600);
      }
    }
    setContextMenu(null);
  }, [contextMenu, t]);

  // 交互增强-1: 双击日格快速新建事件（预填日期，默认 09:00）
  const onDoubleClickDay = useCallback((date: Date) => {
    openCreate(date);
  }, [openCreate]);

  const onDragOverDay = useCallback((e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    try {
      if (e.dataTransfer) e.dataTransfer.dropEffect = 'move';
    } catch { /* ignore */ }
    const ymd = (e.currentTarget as HTMLDivElement).dataset.ymd as YMD | undefined;
    if (ymd) setDragOver(ymd);
  }, [setDragOver]);

  const onDragLeaveDay = useCallback((e: React.DragEvent<HTMLDivElement>) => {
    const rect = (e.currentTarget as HTMLDivElement).getBoundingClientRect();
    const outside =
      e.clientX < rect.left || e.clientX >= rect.right ||
      e.clientY < rect.top || e.clientY >= rect.bottom;
    if (outside) setDragOver(null);
  }, [setDragOver]);
  // P3-E#2-3 优化：拖拽冲突检测改为「按 YMD bucket 候选集」O(Σday_evts) 而非 O(42*N)
  //         依赖 filteredEventsByDate：跨日事件已被 iterateEventDates 分到每一天的 bucket，
  //         因此针对「预测落点只在 d.ymd 当天」做冲突检测，仅需传该日的候选事件集即可。
  const dragConflictMap = useMemo<Record<string, ConflictCheckResult | null>>(() => {
    if (!draggedEvent || !allEvents.length) return {};
    const pad = (n: number) => String(n).padStart(2, '0');
    const oldStart = new Date(draggedEvent.startAt);
    if (isNaN(oldStart.getTime())) return {};
    const oldEnd = draggedEvent.endAt ? new Date(draggedEvent.endAt) : null;
    const durationMs = oldEnd ? Math.max(0, oldEnd.getTime() - oldStart.getTime()) : 0;
    const HH = pad(oldStart.getHours());
    const MM = pad(oldStart.getMinutes());
    const m: Record<string, ConflictCheckResult | null> = {};
    for (const d of grid) {
      // 全天事件：放到该日 00:00 ~ 24:00；非全天：保留原 HH:MM，仅换日期
      let startAt: Date; let endAt: Date | undefined;
      if (draggedEvent.isAllDay) {
        startAt = new Date(`${d.ymd}T00:00:00`);
        endAt = durationMs > 0 ? new Date(startAt.getTime() + durationMs) : new Date(`${d.ymd}T23:59:59.999`);
      } else {
        startAt = new Date(`${d.ymd}T${HH}:${MM}:00`);
        endAt = durationMs > 0 ? new Date(startAt.getTime() + durationMs) : new Date(startAt.getTime() + 60 * 60_000);
      }
      // ✅ 用 YMD bucket 取候选（跨日事件已被 buildEventsByDate/iterateEventDates 展开）
      const candidates = (filteredEventsByDate.get(d.ymd) ?? _EMPTY_ARR) as CalendarEventDto[];
      const check = _checkConflictForRange({
        startAt,
        endAt: endAt ?? undefined,
        existingEvents: candidates,
        excludeId: draggedEvent.id, // 排除事件自身（移动场景）
      });
      m[d.ymd] = check.conflictIds.length > 0 ? check : null;
    }
    return m;
    // 注意：filteredEventsByDate 已经按 enabledEventTypes 过滤，
    //       拖拽硬冲突仍用筛选后的候选（避免被隐藏事件造成"伪冲突"阻止放置）
  }, [draggedEvent, filteredEventsByDate, grid, allEvents]);

  // P3-E#2-2 回调引用稳定化：单例 handler 通过 data-ymd 定位 cell，避免 42 闭包工厂破坏 memo
  const onDropDay = useCallback((e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    const ymd = (e.currentTarget as HTMLDivElement).dataset.ymd as YMD | undefined;
    if (!ymd) return;
    // P2-A#3: 若落点存在硬冲突 → 阻止放置，闪烁 err + toast 提示
    const predicted = dragConflictMap[ymd];
    if (predicted && predicted.tag === 'hard') {
      const showToast = useCalendarStore.getState().showTransientToast;
      setFlashCell({ ymd, tone: 'err' });
      if (flashTimerRef.current !== null) window.clearTimeout(flashTimerRef.current);
      flashTimerRef.current = window.setTimeout(() => {
        setFlashCell((cur) => (cur && cur.ymd === ymd ? null : cur));
        if (flashTimerRef.current) flashTimerRef.current = null;
      }, 500);
      const titles = predicted.conflictWith?.map((c) => c.title).filter(Boolean).slice(0, 2) ?? [];
      const summary = titles.length
        ? t('calendar.views.shared.dropConflictWithNames', {
            names: titles.join(t('calendar.views.shared.nameSeparator')),
            etc: predicted.conflictWith.length > titles.length ? t('calendar.views.shared.etc') : '',
            minutes: predicted.overlapMinutes,
          })
        : t('calendar.views.shared.dropConflictNoNamesWithOverlap', {
            count: predicted.conflictIds.length,
            minutes: predicted.overlapMinutes,
          });
      showToast(t('calendar.views.shared.dropBlocked', { summary }), 'error', 3200);
      // T000091 修复：硬冲突阻止放置时必须清理拖拽状态（draggedEvent/isDragging），
      // 否则 dimChips = isDragging && !isDragOver 持续为 true，
      // 所有非悬浮日格的事件持续应用 'opacity-40 grayscale' → 日历颜色全部变灰。
      drag.endDrag();
      return;
    }
    void drag.handleDrop(ymd);
    // Task E: drop 成功 emerald 160ms 闪烁（预留 tone='err' 接口）
    setFlashCell({ ymd, tone: 'ok' });
    if (flashTimerRef.current !== null) {
      window.clearTimeout(flashTimerRef.current);
    }
    flashTimerRef.current = window.setTimeout(() => {
      setFlashCell((cur) => (cur && cur.ymd === ymd ? null : cur));
      if (flashTimerRef.current) {
        flashTimerRef.current = null;
      }
    }, 160);
  }, [drag, dragConflictMap, t]);

  const isDraggingAny = !!draggedEvent;

  return (
    <div className="flex flex-col w-full h-full min-h-0">
      {/* P1-A#3/#4: 类型筛选 + 多选工具栏（月视图 variant="full"） */}
      <div className="flex items-center gap-1.5 px-3 py-1 border-b border-slate-200 dark:border-slate-800 flex-wrap">
        <EventTypeFilterBar />
        <BatchMultiSelectToolbar
          variant="full"
          multiSelectMode={multiSelectMode}
          selectedCount={selectedEventIds.size}
          onEnterMultiSelect={handleEnterMultiSelect}
          onExitMultiSelect={handleExitMultiSelect}
          onClearSelection={clearSelection}
          onSelectAllOverride={handleSelectAllVisible}
        />
      </div>
      {/* P2-B#1: 表头列（周标题）语义化为 columnheader —— 行默认 row；移动端缩小 padding / 字号 */}
      <div role="row" className="grid grid-cols-7 border border-slate-200 dark:border-slate-800 border-b-0 bg-slate-50/50 dark:bg-slate-900/50">
        {WEEK_LABEL_KEYS.map((key, idx) => {
          const isSunday = idx === 0;
          const isSaturday = idx === 6;
          const isWeekend = isSunday || isSaturday;
          return (
            <div
              key={key}
              role="columnheader"
              aria-colindex={idx + 1}
              className={[
                'max-md:px-1 max-md:py-1 max-md:text-[11px] px-3 py-2 text-center text-xs font-semibold uppercase tracking-wide border-b border-slate-200 dark:border-slate-800',
                isWeekend
                  ? (isSunday ? 'text-rose-500 dark:text-rose-400' : 'text-sky-600 dark:text-sky-400')
                  : 'text-slate-500 dark:text-slate-400',
              ].join(' ')}>
              {t('calendar.views.shared.weekdayHeader', { day: t(key) })}
            </div>
          );
        })}
      </div>
      {/* P2-B#1: 月视图主体标记为 role=grid；DayCell 本身为 gridcell + 可 tab 聚焦；移动端收紧间距 */}
      <div
        role="grid"
        aria-label={t('calendar.views.month.gridAriaLabel')}
        className="grid grid-cols-7 auto-rows-fr grid-flow-row max-md:gap-px gap-[length:var(--cal-grid-gap)] max-md:p-0.5 p-1 border border-slate-200 dark:border-slate-800"
      >
        {grid.map((d, cellIndex) => {
          const evts = filteredEventsByDate.get(d.ymd) ?? [];
          const preview = evts.slice(0, 3);
          const more = Math.max(0, evts.length - 3);
          // 统一冲突检测：同时识别硬冲突(>15min)与软冲突(≤15min)
          const { hasHard, hasSoft } = computeDayHasHardConflict(evts);
          const rowIndex = Math.floor(cellIndex / 7) + 1;
          const colIndex = (cellIndex % 7) + 1;
          return (
            <DayCell
              key={d.ymd}
              day={d}
              previewEvents={preview}
              moreCount={more}
              isDragOver={dragOverDateKey === d.ymd}
              isDragging={isDraggingAny}
              todayLabel={t('calendar.common.today')}
              ariaRowIndex={rowIndex}
              ariaColIndex={colIndex}
              onOpenEvent={onOpenEvent}
              onOpenMore={onOpenMore}
              onChipDragStart={onChipDragStart}
              onDragOverDay={onDragOverDay}
              onDragLeaveDay={onDragLeaveDay}
              onDropDay={onDropDay}
              onDayContextMenu={onDayContextMenu}
              onDoubleClickDay={onDoubleClickDay}
              multiSelectMode={multiSelectMode}
              selectedIds={selectedIdsArr}
              hasHardConflict={hasHard}
              hasSoftConflict={hasSoft}
              allDayEvents={evts}
              flashTone={flashCell && flashCell.ymd === d.ymd ? flashCell.tone : null}
              focusedEventId={focusedEventId}
              dragConflict={dragConflictMap[d.ymd] ?? null}
            />
          );
        })}
      </div>
      {/* P2-2: 空状态壳 */}
      {allEvents.length === 0 && (
        <EmptyState
          variant="month"
          title={t('calendar.views.month.emptyTitle')}
          onPrimary={() => openCreate()}
        />
      )}
      {/* P2-1: 右键上下文菜单 — Portal 到 body，避免父级 transform 动画导致 fixed 定位失效 */}
      {contextMenu && createPortal(
        <div
          className="fixed z-50 min-w-[180px] bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg shadow-xl overflow-hidden animate-[cal-pop-in_120ms_ease]"
          style={{ left: contextMenu.x, top: contextMenu.y }}
        >
          <button
            type="button"
            onClick={handleCtxNewEvent}
            className="flex items-center gap-2 w-full px-3 py-2 text-sm text-slate-700 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors"
          >
            <Plus className="w-4 h-4" />
            {t('calendar.common.newEvent')}
          </button>
          <div className="border-t border-slate-200 dark:border-slate-700" />
          <button
            type="button"
            onClick={handleCtxGotoDay}
            className="flex items-center gap-2 w-full px-3 py-2 text-sm text-slate-700 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors"
          >
            <CalendarDays className="w-4 h-4" />
            {t('calendar.views.month.gotoDayView')}
          </button>
          <div className="border-t border-slate-200 dark:border-slate-700" />
          <button
            type="button"
            onClick={handleCtxCopyDate}
            className="flex items-center gap-2 w-full px-3 py-2 text-sm text-slate-700 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors"
          >
            <Copy className="w-4 h-4" />
            {t('calendar.views.month.copyDate')}
          </button>
        </div>,
        document.body
      )}
    </div>
  );
}
