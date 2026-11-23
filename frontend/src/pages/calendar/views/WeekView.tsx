/* eslint-disable react-refresh/only-export-components -- 文件同时导出组件与工具函数/常量，fast refresh 非关注点 */
import React, { useMemo, useCallback, useEffect, useState, useRef } from 'react';
import { useCalendarStore, useCalendarShallow } from '../store';
import type { CalendarEventDto } from '../../../api/types';
import type { YMD } from '../calendarUtils';
import { toYMDKey, parseYMD, getLunarDayLabel, _buildSingleDayColumnEvents, type HourRowEventByHour } from '../calendarUtils';
import { AllDayEventBar, HourRow, EmptyState, useHourHeight } from '../chips';
import type { HourRowColumnEvent } from '../chips';
import { useCalendarDrag, _checkConflictForRange } from '../hooks';
import { useI18n } from '../../../context/I18nContext';
import type { TranslationKey } from '../../../locales/zh-Hans';
import type { ConflictCheckResult } from '../hooks';
import { EventTypeFilterBar } from '../components/EventTypeFilterBar';
import { BatchMultiSelectToolbar } from '../components/BatchMultiSelectToolbar';

const WEEKDAY_LABEL_KEYS: TranslationKey[] = [
  'calendar.common.weekdayShort.sun',
  'calendar.common.weekdayShort.mon',
  'calendar.common.weekdayShort.tue',
  'calendar.common.weekdayShort.wed',
  'calendar.common.weekdayShort.thu',
  'calendar.common.weekdayShort.fri',
  'calendar.common.weekdayShort.sat',
];

export function _buildWeekColumnYMDs(cursorDate: Date): YMD[] {
  const cursor = new Date(cursorDate.getFullYear(), cursorDate.getMonth(), cursorDate.getDate());
  const jsDay = cursor.getDay();
  const sundayOffset = -jsDay;
  const sunday = new Date(cursor);
  sunday.setDate(sunday.getDate() + sundayOffset);
  const out: YMD[] = [];
  for (let i = 0; i < 7; i++) {
    const d = new Date(sunday);
    d.setDate(sunday.getDate() + i);
    out.push(toYMDKey(d));
  }
  return out;
}

/** P1-A#2: buildColumnEventsForWeek 现以 7 × _buildSingleDayColumnEvents 合成结果。
 *  原 ~80 行 per-day 分桶核心逻辑已统一收敛到 utils，不再与 DayView 双份维护。 */
export function buildColumnEventsForWeek(
  ymds: YMD[],
  eventsByDate: Map<YMD, readonly CalendarEventDto[]>,
): {
  allDayByColumn: CalendarEventDto[][];
  timedByColumnByHour: HourRowEventByHour[];
} {
  const allDayByColumn: CalendarEventDto[][] = [];
  const timedByColumnByHour: HourRowEventByHour[] = [];
  for (const ymd of ymds) {
    const col = _buildSingleDayColumnEvents(ymd, eventsByDate.get(ymd) ?? []);
    allDayByColumn.push(col.allDayEvents);
    timedByColumnByHour.push(col.timedByHour);
  }
  return { allDayByColumn, timedByColumnByHour };
}

const HOURS = Array.from({ length: 24 }, (_, i) => i);

export default function WeekView() {
  const { t } = useI18n();
  // 平板/手机（<768px）：时间列宽度 44px，桌面 56px
  // 与 chips.TIME_COL_W_CLS 和 useHourHeight() 的断点保持一致
  const [isSmall, setIsSmall] = useState(() => {
    if (typeof window === 'undefined') return false;
    return window.matchMedia('(max-width: 767.98px)').matches;
  });
  useEffect(() => {
    if (typeof window === 'undefined') return;
    const mq = window.matchMedia('(max-width: 767.98px)');
    const handler = (e: MediaQueryListEvent) => setIsSmall(e.matches);
    mq.addEventListener('change', handler);
    return () => mq.removeEventListener('change', handler);
  }, []);
  const gutterPx = isSmall ? 44 : 56;

  const selector = useCallback((s: ReturnType<typeof useCalendarStore.getState>) => ({
    calCursor: s.calCursor,
    eventsByDate: s.eventsByDate,
    dragOverDateKey: s.dragOverDateKey,
    draggedEvent: s.draggedEvent,
    isDragging: s.isDragging,
    dragPreviewPos: s.dragPreviewPos,
    focusedEventId: s.focusedEventId,
  }), []);
  const selected = useCalendarShallow(selector);
  const { calCursor, eventsByDate, dragOverDateKey, draggedEvent, isDragging, dragPreviewPos, focusedEventId } = selected;
  const hourHeight = useHourHeight();

  const getEventsByYMD = useCalendarStore((s) => s.getEventsByYMD);
  const setDragOver = useCalendarStore((s) => s.setDragOver);
  const setDragPreviewPos = useCalendarStore((s) => s.setDragPreviewPos);
  const openCreate = useCalendarStore((s) => s.openCreate);
  const resizeEvent = useCalendarStore((s) => s.resizeEvent);
  void getEventsByYMD;

  // P1-2: 多选订阅（Set 用默认 Object.is 订阅，不走 shallow）
  const selectedEventIds = useCalendarStore((s) => s.selectedEventIds);
  const selectEvent = useCalendarStore((s) => s.selectEvent);
  const deselectEvent = useCalendarStore((s) => s.deselectEvent);
  const isSelectedFn = useCalendarStore((s) => s.isSelected);
  const clearSelection = useCalendarStore((s) => s.clearSelection);
  // 第三梯队: 事件类型筛选（filteredEventsByDate 内部过滤用）
  const enabledEventTypes = useCalendarStore((s) => s.enabledEventTypes);
  // 重要：邮箱筛选必须与 store.getEventsByYMD 逻辑一致，否则月/周/日视图显示结果不同
  const enabledMailboxIds = useCalendarStore((s) => s.enabledMailboxIds);

  const [multiSelectMode, setMultiSelectMode] = useState(false);

  // Task E: drop 成功/失败闪烁态
  const [flashCell, setFlashCell] = useState<{ colIdx: number; hour: number; tone: 'ok' | 'err' } | null>(null);
  const flashTimerRef = useRef<number | null>(null);
  const scrollRef = useRef<HTMLDivElement>(null);

  const toggleSelect = useCallback((id: number) => {
    if (isSelectedFn(id)) deselectEvent(id);
    else selectEvent(id);
  }, [isSelectedFn, selectEvent, deselectEvent]);

  const handleEnterMultiSelect = useCallback(() => setMultiSelectMode(true), []);
  const handleExitMultiSelect = useCallback(() => {
    clearSelection();
    setMultiSelectMode(false);
  }, [clearSelection]);

  const drag = useCalendarDrag();

  const [nowTick, setNowTick] = useState(() => Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setNowTick(Date.now()), 30000);
    return () => window.clearInterval(id);
  }, []);

  // 交互增强-2: 首次加载时自动滚动到当前时间附近
  useEffect(() => {
    const scrollContainer = scrollRef.current;
    if (!scrollContainer) return;
    const now = new Date();
    const hourHeightNum = typeof hourHeight === 'number' ? hourHeight : 56;
    // 滚动到当前小时上方 2 小时的位置，避免紧贴顶部
    const targetHour = Math.max(0, now.getHours() - 2);
    scrollContainer.scrollTo({ top: targetHour * hourHeightNum, behavior: 'smooth' });
  }, [hourHeight]); // 挂载及 hourHeight 变化时定位到当前时间

  useEffect(() => {
    return () => {
      if (flashTimerRef.current !== null) {
        window.clearTimeout(flashTimerRef.current);
        flashTimerRef.current = null;
      }
    };
  }, []);

  const columnYMDs = useMemo(() => _buildWeekColumnYMDs(calCursor), [calCursor]);

  const todayYMD = useMemo(() => toYMDKey(new Date(nowTick)), [nowTick]);
  const nowHour = useMemo(() => new Date(nowTick).getHours(), [nowTick]);
  const nowMinute = useMemo(() => new Date(nowTick).getMinutes(), [nowTick]);

  const dragOverColumnIdx = useMemo(() => {
    if (!dragOverDateKey) return null;
    const idx = columnYMDs.indexOf(dragOverDateKey);
    return idx >= 0 ? idx : null;
  }, [dragOverDateKey, columnYMDs]);

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

  const { allDayByColumn, timedByColumnByHour } = useMemo(
    () => buildColumnEventsForWeek(columnYMDs, filteredEventsByDate),
    [columnYMDs, filteredEventsByDate],
  );

  // P2-2: EmptyState 判断 —— 当前 7 天无任何事件
  const hasAnyEvent = useMemo(() => {
    return columnYMDs.some(ymd => (filteredEventsByDate.get(ymd)?.length ?? 0) > 0);
  }, [columnYMDs, filteredEventsByDate]);

  // P1-2: 多选模式下 onEventClick → toggleSelect；否则维持原 openEdit 行为
  // 高6 修复：传 eventStartAt 用于重复事件实例的唯一识别，避免按 id find 时取到第一个实例导致打开错误实例
  const onEventClick = useCallback((eventId: number, eventStartAt?: string) => {
    if (multiSelectMode) {
      toggleSelect(eventId);
      return;
    }
    const { allEvents, openEdit } = useCalendarStore.getState();
    const ev = allEvents.find((e) => e.id === eventId && (!eventStartAt || e.startAt === eventStartAt))
      ?? allEvents.find((e) => e.id === eventId);
    if (ev) openEdit(ev);
  }, [multiSelectMode, toggleSelect]);

  const onEventDragStart = useCallback((eventId: number, e: React.DragEvent<HTMLDivElement>) => {
    const { allEvents } = useCalendarStore.getState();
    const ev = allEvents.find((x) => x.id === eventId);
    if (ev) drag.onChipDragStart(ev, e);
  }, [drag]);

  const onEventResize = useCallback((eventId: number, newEndAt: string) => {
    void resizeEvent(eventId, newEndAt);
  }, [resizeEvent]);

  // P1-2: 全天事件 bar 点击 —— 多选模式下 toggleSelect
  const onOpenAllDay = useCallback((ev: CalendarEventDto) => {
    if (multiSelectMode) {
      toggleSelect(ev.id);
      return;
    }
    const { openEdit } = useCalendarStore.getState();
    openEdit(ev);
  }, [multiSelectMode, toggleSelect]);

  const onCellClick = useCallback(
    (ymd: YMD, hour: number) => {
      const { y, m, d } = parseYMD(ymd);
      const pad = (n: number) => String(n).padStart(2, '0');
      const dt = new Date(`${y}-${pad(m)}-${pad(d)}T${pad(hour)}:00:00`);
      openCreate(dt);
    },
    [openCreate],
  );

  const onDragOverCell = useCallback(
    (ymd: YMD, hourFloat: number, _e: React.DragEvent<HTMLDivElement>) => {
      setDragOver(ymd);
      setDragPreviewPos({ ymd, hourFloat });
    },
    [setDragOver, setDragPreviewPos],
  );

  const onDropCell = useCallback(
    (ymd: YMD, hourFloat: number, e: React.DragEvent<HTMLDivElement>) => {
      e.preventDefault();
      const pad = (n: number) => String(n).padStart(2, '0');
      // P2-A#3: 周视图硬冲突放置拦截（通过读取 store 即时状态，避免依赖膨胀）
      const snap = useCalendarStore.getState();
      const dragged = snap.draggedEvent;
      let predicted: ConflictCheckResult | null = null;
      if (dragged) {
        const HH = pad(Math.floor(hourFloat));
        const MM = pad(Math.round((hourFloat - Math.floor(hourFloat)) * 60));
        const startAt = new Date(`${ymd}T${HH}:${MM}:00`);
        const oldStart = new Date(dragged.startAt);
        const oldEnd = dragged.endAt ? new Date(dragged.endAt) : null;
        const durationMs = oldEnd ? Math.max(0, oldEnd.getTime() - oldStart.getTime()) : 60 * 60_000;
        const endAt = new Date(startAt.getTime() + durationMs);
        predicted = _checkConflictForRange({
          startAt,
          endAt,
          existingEvents: snap.allEvents as CalendarEventDto[],
          excludeId: dragged.id,
        });
      }
      const colIdx = columnYMDs.indexOf(ymd);
      // 闪烁基小时：取 hourFloat 的整数部分（精确到小时格）
      const flashHour = Math.floor(hourFloat);
      if (predicted && predicted.tag === 'hard') {
        if (colIdx >= 0) {
          setFlashCell({ colIdx, hour: flashHour, tone: 'err' });
          if (flashTimerRef.current !== null) window.clearTimeout(flashTimerRef.current);
          flashTimerRef.current = window.setTimeout(() => {
            setFlashCell((cur) => (cur && cur.colIdx === colIdx && cur.hour === flashHour ? null : cur));
            if (flashTimerRef.current) flashTimerRef.current = null;
          }, 500);
        }
        const titles = predicted.conflictWith?.map((c) => c.title).filter(Boolean).slice(0, 2) ?? [];
        const summary = titles.length
          ? t('calendar.views.shared.dropConflictWithNames', {
              names: titles.join(t('calendar.views.shared.nameSeparator')),
              etc: predicted.conflictWith.length > titles.length ? t('calendar.views.shared.etc') : '',
              minutes: predicted.overlapMinutes,
            })
          : t('calendar.views.shared.dropConflictNoNames', { count: predicted.conflictIds.length });
        snap.showTransientToast(t('calendar.views.shared.dropBlocked', { summary }), 'error', 3200);
        // T000091 修复：硬冲突阻止放置时必须清理拖拽状态（draggedEvent/isDragging），
        // 否则 dimmed={isDraggingAny && dragOverColumnIdx !== colIdx} 持续为 true，
        // 所有非目标列事件持续应用 'opacity-40 grayscale' → 日历颜色全部变灰。
        drag.endDrag();
        return;
      }
      void drag.handleDrop(ymd, hourFloat);
      // Task E: drop 成功 emerald 160ms 闪烁（预留 tone='err' 接口）
      if (colIdx >= 0) {
        setFlashCell({ colIdx, hour: flashHour, tone: 'ok' });
        if (flashTimerRef.current !== null) {
          window.clearTimeout(flashTimerRef.current);
        }
        flashTimerRef.current = window.setTimeout(() => {
          setFlashCell((cur) => (cur && cur.colIdx === colIdx && cur.hour === flashHour ? null : cur));
          if (flashTimerRef.current) {
            flashTimerRef.current = null;
          }
        }, 160);
      }
    },
    [drag, columnYMDs, t],
  );

  const isDraggingAny = !!draggedEvent;

  const colCount = columnYMDs.length;
  const gridTemplate = `repeat(${colCount}, minmax(0, 1fr))`;

  return (
    <div className="flex flex-col w-full h-full min-h-0 overflow-hidden">
      <div className="sticky top-0 z-20 bg-white dark:bg-slate-900 border-b border-slate-200 dark:border-slate-800">
        {/* P1-A#3/#4: 类型筛选 + 多选工具栏（周视图 variant="compact"） */}
        <div className="flex items-center gap-1.5 px-3 py-1 border-b border-slate-200 dark:border-slate-800 flex-wrap">
          <EventTypeFilterBar />
          <BatchMultiSelectToolbar
            variant="compact"
            multiSelectMode={multiSelectMode}
            selectedCount={selectedEventIds.size}
            onEnterMultiSelect={handleEnterMultiSelect}
            onExitMultiSelect={handleExitMultiSelect}
            onClearSelection={clearSelection}
          />
        </div>
        <div
          className="grid"
          style={{ gridTemplateColumns: `${gutterPx}px ${gridTemplate}` }}
        >
          <div
            className="shrink-0"
            style={{ width: `${gutterPx}px` }}
          />
          {columnYMDs.map((ymd, idx) => {
            const { y, m, d } = parseYMD(ymd);
            const dt = new Date(y, m - 1, d);
            const isToday = ymd === todayYMD;
            const weekdayKey = WEEKDAY_LABEL_KEYS[idx];
            const isSunday = idx === 0;
            const isSaturday = idx === 6;
            const isWeekend = isSaturday || isSunday;
            const lunarText = getLunarDayLabel(dt);
            return (
              <div
                key={ymd}
                className={[
                  'flex flex-col items-center justify-center max-md:py-1 max-md:px-0.5 py-2 px-1 border-l border-slate-200 dark:border-slate-800',
                  isToday ? 'bg-gradient-to-b from-sky-50 to-transparent dark:from-sky-950/40 dark:to-transparent' : '',
                ].filter(Boolean).join(' ')}
              >
                <div className={[
                  'max-md:text-[10px] text-[11px] font-medium',
                  isToday
                    ? 'text-sky-600 dark:text-sky-400'
                    : isWeekend
                      ? (isSunday ? 'text-rose-500 dark:text-rose-400' : 'text-sky-600 dark:text-sky-400')
                      : 'text-slate-500 dark:text-slate-400',
                ].join(' ')}>
                  {t('calendar.views.shared.weekdayHeader', { day: t(weekdayKey) })}
                </div>
                <div className={[
                  'inline-flex items-center gap-1 max-md:gap-0.5 max-md:mt-0 max-md:h-5 max-md:px-1.5 max-md:text-[10px] mt-0.5 h-7 px-2 rounded-full text-[11px] font-medium transition-transform hover:scale-[1.05]',
                  isToday
                    ? 'bg-gradient-to-br from-sky-500 to-blue-600 text-white shadow-md shadow-sky-500/30 ring-2 ring-sky-200/50 dark:ring-sky-800/50'
                    : 'text-slate-700 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800',
                ].join(' ')}>
                  <span>{t('calendar.views.shared.daySuffix', { day: d })}</span>
                  {lunarText && (
                    <>
                      <span className={[isToday ? 'text-sky-100/90' : 'text-slate-400 dark:text-slate-500', 'max-md:hidden'].join(' ')}>·</span>
                      <span className={[isToday ? 'text-sky-100/90' : 'text-slate-400 dark:text-slate-500', 'font-normal max-md:hidden'].join(' ')}>{lunarText}</span>
                    </>
                  )}
                </div>
                <span className={[
                  'max-md:hidden text-[10px] font-medium mt-0.5',
                  isToday
                    ? 'text-sky-600 dark:text-sky-400'
                    : 'text-slate-400 dark:text-slate-500',
                ].join(' ')}>
                  {isToday ? t('calendar.common.today') : `${m}/${d}`}
                </span>
              </div>
            );
          })}
        </div>

        <div
          className="grid border-t border-slate-200 dark:border-slate-800 bg-slate-50/30 dark:bg-slate-900/30"
          style={{ gridTemplateColumns: `${gutterPx}px ${gridTemplate}` }}
        >
          <div
            className="shrink-0 border-l-0 border-r border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-950/40"
            style={{ width: `${gutterPx}px` }}
          />
          {allDayByColumn.map((arr, colIdx) => (
            <div
              key={`allday-${columnYMDs[colIdx]}`}
              className="flex flex-col gap-1 p-1.5 min-h-[40px] border-l border-slate-100 dark:border-slate-800/50"
            >
              {arr.map((ev) => (
                <AllDayEventBar
                  key={`allday-${ev.id}-${columnYMDs[colIdx]}`}
                  ev={ev}
                  dimmed={isDraggingAny && dragOverColumnIdx !== colIdx}
                  onOpen={onOpenAllDay}
                  onDragStart={drag.onChipDragStart}
                  selected={multiSelectMode && isSelectedFn(ev.id)}
                />
              ))}
            </div>
          ))}
        </div>
      </div>

      <div ref={scrollRef} className="flex-1 overflow-y-auto min-h-0">
        {/* P2-2: EmptyState —— 本周无事件时展示 */}
        {!hasAnyEvent ? (
          <EmptyState
            variant="week"
            title={t('calendar.views.week.emptyTitle')}
            onPrimary={() => openCreate()}
          />
        ) : (
          <>
            {HOURS.map((h) => {
          const eventsByColumn: HourRowColumnEvent[][] = columnYMDs.map((_ymd, colIdx) => {
            return timedByColumnByHour[colIdx]?.[h] ?? [];
          });
          const isWorkHour = h >= 9 && h < 18;
          return (
            <div
              key={`hour-${h}`}
              className={[
                'transition-colors hover:bg-slate-50/50 dark:hover:bg-slate-800/20',
                isWorkHour ? 'bg-slate-50/20 dark:bg-slate-900/20' : '',
              ].filter(Boolean).join(' ')}
            >
              <HourRow
                hour={h}
                columnYMDs={columnYMDs}
                eventsByColumn={eventsByColumn}
                nowLineVisible={h === nowHour}
                nowLineMinute={h === nowHour ? nowMinute : 0}
                hourHeight={hourHeight}
                dragOverColumn={dragOverColumnIdx}
                isDragging={isDragging}
                onCellClick={onCellClick}
                onDragOverCell={onDragOverCell}
                onDropCell={onDropCell}
                onEventClick={onEventClick}
                onEventDragStart={onEventDragStart}
                onEventResize={onEventResize}
                flashColumnIdx={flashCell && flashCell.hour === h ? flashCell.colIdx : null}
                flashTone={flashCell && flashCell.hour === h ? flashCell.tone : null}
                focusedEventId={focusedEventId}
                selectedEventIds={selectedEventIds}
                draggedEventId={draggedEvent?.id ?? null}
                dragPreview={{
                  draggedEvent: draggedEvent ? {
                    id: draggedEvent.id,
                    title: draggedEvent.title,
                    color: draggedEvent.color ?? null,
                    eventType: draggedEvent.eventType,
                    durationMin: draggedEvent.endAt
                      ? Math.max(15, Math.round((new Date(draggedEvent.endAt).getTime() - new Date(draggedEvent.startAt).getTime()) / 60000))
                      : 60,
                  } : null,
                  previewYmd: dragPreviewPos?.ymd ?? null,
                  previewHourFloat: dragPreviewPos?.hourFloat ?? null,
                }}
              />
            </div>
          );
        })}
        <div style={{ height: 1 }} />
          </>
        )}
      </div>
    </div>
  );
}

