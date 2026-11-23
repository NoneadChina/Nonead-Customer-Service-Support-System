/* eslint-disable react-refresh/only-export-components -- 文件同时导出组件与工具函数/hook/常量，fast refresh 非关注点 */
import React, { memo, useEffect, useState, useCallback, useRef } from 'react';
import { CheckCircle2 } from 'lucide-react';
import type { CalendarEventDto } from '../../api/types';
import type { YMD } from './calendarUtils';
import { EVENT_TYPE_STYLE, STATUS_STYLE, toYMDKey, formatDate, parseYMD } from './calendarUtils';
import { useCalendarStore } from './store';
// 本文件的组件与纯函数都会被单测**在不套 Provider 的情况下**直接渲染/调用
// （`src/__tests__/calendar-store.test.tsx`），故一律走 `tBound`/`tChip`——
// 语言由 `I18nProvider` 在渲染体内 `setBoundLocale()` 绑定，无 Provider 时回落 zh-Hans。
import { BUILT_IN_ENTRIES, FALLBACK_LOCALE, getBoundLocale, tBound } from '../../i18n/locale';
import { translate } from '../../i18n/translate';
import type { TranslateParams } from '../../i18n/types';
import type { TranslationKey } from '../../locales/zh-Hans';

/**
 * 带插值的取词：`tBound()` 只收 key（不支持 params），故插值词条直接走 `translate()`，
 * 语言仍取 `getBoundLocale()`（与 `components/PasswordPolicyHint.tsx` 的 `tPolicy` 同一手法）。
 */
function tChip(key: TranslationKey, params?: TranslateParams): string {
  const locale = getBoundLocale();
  return translate(BUILT_IN_ENTRIES[locale] ?? BUILT_IN_ENTRIES[FALLBACK_LOCALE], locale, key, params);
}

// 密度变量优先：运行时从 CSS 变量读取，回退到 56
const HOUR_HEIGHT_CSS = 'var(--cal-hour-h, 56px)';
const MIN_EVENT_HEIGHT_PX = 16;

/**
 * 运行时读取 --cal-hour-h 的实际像素值（56px 桌面 / 44px 移动端）。
 * 通过 matchMedia 监听断点切换，保证事件 top/height 计算与 CSS 容器高度同步。
 */
export function useHourHeight(): number {
  const [height, setHeight] = useState(() => {
    if (typeof window === 'undefined') return 56;
    return window.matchMedia('(max-width: 767.98px)').matches ? 44 : 56;
  });

  useEffect(() => {
    const mql = window.matchMedia('(max-width: 767.98px)');
    const handler = (e: MediaQueryListEvent) => setHeight(e.matches ? 44 : 56);
    mql.addEventListener('change', handler);
    return () => mql.removeEventListener('change', handler);
  }, []);

  return height;
}
// 时间列宽度：桌面 56px，平板/手机（<768px）44px，与 useHourHeight / WeekView gutterPx 保持一致
const TIME_COL_W_CLS = 'max-md:w-11 w-14';

function formatDateYMDShort(ymd: YMD): string {
  const { y, m, d } = parseYMD(ymd);
  void y;
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(m)}/${pad(d)}`;
}

/* =========================================================
   大小写不敏感的 eventType / status 查找（双保险）
   修复"偶发日历颜色显示全部变灰"BUG：
   后端理论上 eventType/status 全小写返回，但若浏览器 localStorage patch replay
   或后端序列化意外返回大小写不一致（如 "Task" / "PENDING"），直接 EVENT_TYPE_STYLE[ev.eventType]
   会查不到键 → 落到 ?? other/pending 兜底，但仍会触发灰色 fallback 路径。
   此处统一做 toLowerCase 查找，作为大小写不敏感的双保险。
   ========================================================= */
export function getEventTypeStyle(eventType: string | undefined | null): typeof EVENT_TYPE_STYLE[keyof typeof EVENT_TYPE_STYLE] {
  if (!eventType) return EVENT_TYPE_STYLE.other;
  const lower = eventType.toLowerCase();
  return EVENT_TYPE_STYLE[lower] ?? EVENT_TYPE_STYLE.other;
}

export function getStatusStyle(status: string | undefined | null): typeof STATUS_STYLE[keyof typeof STATUS_STYLE] {
  if (!status) return STATUS_STYLE.pending;
  const lower = status.toLowerCase();
  return STATUS_STYLE[lower] ?? STATUS_STYLE.pending;
}

/* =========================================================
   自定义颜色辅助：若用户在编辑面板选择了自定义颜色，需要在所有渲染位置覆盖 EVENT_TYPE_STYLE
   ========================================================= */

/** 十六进制颜色是否偏亮（判断文字用深色还是浅色） */
function _isLightHex(hex: string): boolean {
  const clean = hex.replace('#', '').trim();
  if (clean.length === 3) {
    return _isLightHex(clean.split('').map(c => c + c).join(''));
  }
  if (clean.length !== 6) return true;
  const r = parseInt(clean.slice(0, 2), 16);
  const g = parseInt(clean.slice(2, 4), 16);
  const b = parseInt(clean.slice(4, 6), 16);
  const yiq = (r * 299 + g * 587 + b * 114) / 1000;
  return yiq >= 160;
}

/** 根据用户自定义颜色，给出覆盖 EVENT_TYPE_STYLE 的内联样式（bg + text + border） */
export function _colorOverrideStyle(ev: { color?: string | null }): React.CSSProperties | undefined {
  if (!ev.color) return undefined;
  const light = _isLightHex(ev.color);
  return {
    backgroundColor: ev.color,
    color: light ? '#1e293b' /* slate-800 */ : '#ffffff',
    borderColor: light ? `${ev.color}99` : `${ev.color}`,
  } as React.CSSProperties;
}

/** 当有自定义颜色时，替换 badgeClass（避免 class bg 与 inline bg 冲突的双保险） */
export function _colorOverrideRingStyle(ev: { color?: string | null }): React.CSSProperties | undefined {
  if (!ev.color) return undefined;
  return { boxShadow: `0 0 0 1px ${ev.color}66 inset` } as React.CSSProperties;
}

/** 事件类型 dotClass 对应的默认实色（用于时间网格事件块的背景色回退） */
const EVENT_TYPE_DOT_HEX: Record<string, string> = {
  due_date: '#f43f5e', // rose-500
  task:     '#3b82f6', // blue-500
  reminder: '#f59e0b', // amber-500
  meeting:  '#8b5cf6', // violet-500
  personal: '#ec4899', // pink-500
  travel:   '#10b981', // emerald-500
  work:     '#0ea5e9', // sky-500
  other:    '#64748b', // slate-500
};

/** 事件类型 dotClass 对应的默认实色（大小写不敏感查找，双保险） */
function _getEventTypeDotHex(eventType: string | undefined | null): string {
  if (!eventType) return EVENT_TYPE_DOT_HEX.other;
  const lower = eventType.toLowerCase();
  return EVENT_TYPE_DOT_HEX[lower] ?? EVENT_TYPE_DOT_HEX.other;
}

/** 时间网格事件块的最终背景色：自定义色优先，否则回退到事件类型默认色（大小写不敏感） */
export function _timegridBackgroundColor(ev: { color?: string | null; eventType?: string }): string {
  if (ev.color) return ev.color;
  return _getEventTypeDotHex(ev.eventType);
}

/* =========================================================
   Pure helpers（导出供单测，不改 props 接口）
   ========================================================= */

export function _chipTimeLabel(ev: CalendarEventDto): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  const s = new Date(ev.startAt);
  const sTime = `${pad(s.getHours())}:${pad(s.getMinutes())}`;
  if (!ev.endAt) return sTime;
  const e = new Date(ev.endAt);
  return `${sTime}-${pad(e.getHours())}:${pad(e.getMinutes())}`;
}

export function _chipDisplayLabel(ev: CalendarEventDto): string | null {
  // 修复"偶发日历颜色显示全部变灰"BUG：
  // 原实现使用 hasOwnProperty 严格检查，当 eventType/status 为 undefined/null/空字符串
  // 或字典未覆盖的值（如后端新增类型、localStorage patch replay 错误数据）时，
  // 强制返回灰色 fallback 样式，覆盖了 typeStyle/statusStyle 的 ?? other/pending 兜底。
  // 现在让此函数始终返回 null，由 EventChipImpl 中的 `?? EVENT_TYPE_STYLE.other`
  // 和 `?? STATUS_STYLE.pending` 统一兜底，确保未知值显示为 other/pending 颜色而非灰色。
  // 开发期诊断：如需发现未覆盖的取值，可在控制台筛选 eventType 不在 EVENT_TYPE_STYLE 的事件。
  void ev;
  return null;
}

export function _allDayTimeLabel(ev: CalendarEventDto): string {
  if (!ev.endAt) return tBound('calendar.common.allDay');
  const s = new Date(ev.startAt);
  const e = new Date(ev.endAt);
  const sYMD = toYMDKey(s);
  const eYMD = toYMDKey(e);
  if (sYMD === eYMD) return tBound('calendar.common.allDay');
  const days = Math.max(1, Math.round((+new Date(eYMD) - +new Date(sYMD)) / 86400000) + 1);
  return tChip('calendar.shell.chips.allDaySpan', { date: formatDate(s, 'short'), days, count: days });
}

/* =========================================================
   EventChip —— 月/周/日视图单元格里的小事件条
   Props 纯值化 → React.memo 友好
   ========================================================= */
export interface EventChipProps {
  ev: CalendarEventDto;
  ymdKey?: string;
  onOpen: (ev: CalendarEventDto) => void;
  onDragStart?: (ev: CalendarEventDto, e: React.DragEvent<HTMLDivElement>) => void;
  compact?: boolean;
  dimmed?: boolean;
  /** P1-1: 冲突标签 — 'none'=无冲突 / 'soft'=边缘重叠 / 'hard'=严重重叠 */
  conflictTag?: 'none' | 'soft' | 'hard';
  /** 冲突事件数量（>0 时渲染右上角 badge） */
  conflictCount?: number;
  /** 冲突事件明细用于 title，缺省时仅显示 "N 冲突" badge */
  conflictWith?: { title: string; startAt: string; endAt?: string | null }[];
  /** 第三梯队: 键盘导航焦点态（Tab 选中时高亮） */
  focused?: boolean;
  /** 多选模式下选中态：多层视觉叠加（粗描边+偏移+勾选徽章+抬升阴影），在任何自定义色背景上都明显 */
  selected?: boolean;
}

function EventChipImpl({ ev, ymdKey, onOpen, onDragStart, compact, dimmed, conflictTag, conflictCount, conflictWith, focused, selected }: EventChipProps) {
  void ymdKey;
  const [dragging, setDragging] = useState(false);
  // 源 chip 被拎起：半透明 + 轻微放大，视觉上明确"这块在被拖"
  const isSourceDragged = useCalendarStore((s) => s.draggedEvent?.id === ev.id);
  // 挂起同步标记：如果日程 id 存在于 store.pendingPatchIds（被 localStorage 补丁 replay），
  // 在右上角渲染小圆点提示用户"当前显示为本地暂存修改，后端确认中"
  const isPendingSync = useCalendarStore(
    (s) => typeof ev.id === 'number' && s.pendingPatchIds.has(ev.id)
  );
  // 交互增强-2: 悬浮预览卡片（延迟显示，避免快速划过频繁闪烁）
  const [hoverPreview, setHoverPreview] = useState(false);
  const hoverTimerRef = useRef<number | null>(null);
  const chipRef = useRef<HTMLDivElement>(null);
  const scrollerRef = useRef<HTMLDivElement | null>(null);
  const dayCellRef = useRef<HTMLDivElement | null>(null);
  const HOVER_DELAY_MS = 320;

  // Safety net: reset dragging state if global dragend fires (edge cases where onDragEnd on element wasn't reached)
  useEffect(() => {
    const reset = () => setDragging(false);
    window.addEventListener('dragend', reset);
    // Also reset on dragleave at document level if leaving the window (some browsers fire dragleave not dragend when dropped outside)
    window.addEventListener('drop', reset);
    return () => {
      window.removeEventListener('dragend', reset);
      window.removeEventListener('drop', reset);
    };
  }, []);

  // 清理 hover 计时器，防止组件卸载后 setState
  useEffect(() => {
    return () => {
      if (hoverTimerRef.current !== null) {
        window.clearTimeout(hoverTimerRef.current);
        hoverTimerRef.current = null;
      }
      // 兜底清理祖先 scroller / daycell 的临时 class
      if (scrollerRef.current) {
        scrollerRef.current.classList.remove('cal-scroller-hovering');
        scrollerRef.current = null;
      }
      if (dayCellRef.current) {
        dayCellRef.current.classList.remove('cal-daycell-hovering');
        dayCellRef.current = null;
      }
    };
  }, []);

  // hoverPreview 切换时：
  // 1) 临时让祖先 .cal-chips-scroller 允许溢出，避免 tooltip 被裁剪
  // 2) 临时让祖先 .cal-day-cell 提升 z-index > z-10（今日/光标格），避免被其他 DayCell 覆盖
  useEffect(() => {
    if (!chipRef.current) return;
    if (hoverPreview) {
      const scroller = chipRef.current.closest('.cal-chips-scroller') as HTMLDivElement | null;
      if (scroller) {
        scroller.classList.add('cal-scroller-hovering');
        scrollerRef.current = scroller;
      }
      const dayCell = chipRef.current.closest('.cal-day-cell') as HTMLDivElement | null;
      if (dayCell) {
        dayCell.classList.add('cal-daycell-hovering');
        dayCellRef.current = dayCell;
      }
    } else {
      if (scrollerRef.current) {
        scrollerRef.current.classList.remove('cal-scroller-hovering');
        scrollerRef.current = null;
      }
      if (dayCellRef.current) {
        dayCellRef.current.classList.remove('cal-daycell-hovering');
        dayCellRef.current = null;
      }
    }
  }, [hoverPreview]);

  const showHover = useCallback(() => {
    if (hoverTimerRef.current !== null) window.clearTimeout(hoverTimerRef.current);
    hoverTimerRef.current = window.setTimeout(() => setHoverPreview(true), HOVER_DELAY_MS);
  }, []);

  const hideHover = useCallback(() => {
    if (hoverTimerRef.current !== null) {
      window.clearTimeout(hoverTimerRef.current);
      hoverTimerRef.current = null;
    }
    setHoverPreview(false);
  }, []);

  // 第三梯队: 移动端长按拖拽
  const touchDragRef = useRef<{ startX: number; startY: number; longPressTimer: number | null; isDragging: boolean }>({
    startX: 0, startY: 0, longPressTimer: null, isDragging: false,
  });
  const LONG_PRESS_MS = 500;
  const DRAG_THRESHOLD_PX = 10;

  const handleTouchStart = useCallback((e: React.TouchEvent<HTMLDivElement>) => {
    if (!onDragStart) return;
    const touch = e.touches[0];
    touchDragRef.current.startX = touch.clientX;
    touchDragRef.current.startY = touch.clientY;
    touchDragRef.current.isDragging = false;
    // 长按 500ms 后开始拖拽
    touchDragRef.current.longPressTimer = window.setTimeout(() => {
      touchDragRef.current.isDragging = true;
      setDragging(true);
      const chipEl = chipRef.current;
      if (chipEl) {
        // 触觉反馈（如果支持）
        if (navigator.vibrate) navigator.vibrate(50);
        // 添加拖拽视觉 + 阻止后续手势触发滚动
        chipEl.style.opacity = '0.6';
        chipEl.style.transform = 'scale(1.03)';
        chipEl.style.touchAction = 'none';
      }
      // 调用 store 的 startDrag
      onDragStart(ev, e as unknown as React.DragEvent<HTMLDivElement>);
    }, LONG_PRESS_MS);
  }, [onDragStart, ev]);

  const handleTouchMove = useCallback((e: React.TouchEvent<HTMLDivElement>) => {
    const ref = touchDragRef.current;
    if (ref.longPressTimer === null) return;
    const touch = e.touches[0];
    const deltaX = Math.abs(touch.clientX - ref.startX);
    const deltaY = Math.abs(touch.clientY - ref.startY);
    // 如果在长按完成前移动超过阈值，取消长按
    if (!ref.isDragging && (deltaX > DRAG_THRESHOLD_PX || deltaY > DRAG_THRESHOLD_PX)) {
      window.clearTimeout(ref.longPressTimer);
      ref.longPressTimer = null;
    }
    // 长按完成后移动 → 高亮目标日格
    if (ref.isDragging) {
      e.preventDefault();
      const target = document.elementFromPoint(touch.clientX, touch.clientY);
      const dayCell = target?.closest('[data-ymd]') as HTMLElement | null;
      if (dayCell) {
        const ymd = dayCell.dataset.ymd;
        if (ymd) {
          useCalendarStore.getState().setDragOver(ymd);
        }
      }
    }
  }, []);

  const handleTouchEnd = useCallback(async (e: React.TouchEvent<HTMLDivElement>) => {
    const ref = touchDragRef.current;
    if (ref.longPressTimer !== null) {
      window.clearTimeout(ref.longPressTimer);
      ref.longPressTimer = null;
    }
    if (ref.isDragging) {
      const touch = e.changedTouches[0];
      const target = document.elementFromPoint(touch.clientX, touch.clientY);
      const dayCell = target?.closest('[data-ymd]') as HTMLElement | null;
      if (dayCell) {
        const ymd = dayCell.dataset.ymd;
        if (ymd) {
          // 修复：必须 await moveEventByDrop 完成后再 endDrag，
          // 否则 pendingSignalRBump 还未被设置为 true 就被 endDrag 读取（false），
          // 导致 signalRRev 不触发，后端数据无法同步到前端。
          // P2-3: EventChip 仅用于 MonthView（日期级拖拽），只传 ymd 不传 targetHourFloat。
          // WeekView/DayView 使用 HourRow（独立拖拽实现），不走此路径。
          await useCalendarStore.getState().moveEventByDrop(ymd);
        }
      }
      // 清理：moveEventByDrop 完成后（pendingSignalRBump 已设为 true）再 endDrag
      useCalendarStore.getState().endDrag();
      setDragging(false);
      const chipEl = chipRef.current;
      if (chipEl) {
        chipEl.style.opacity = '';
        chipEl.style.transform = '';
        chipEl.style.touchAction = '';
      }
    }
    ref.isDragging = false;
  }, []);

  const typeStyle = getEventTypeStyle(ev.eventType);
  const statusStyle = getStatusStyle(ev.status);
  // 修复"偶发日历颜色显示全部变灰"BUG：移除 _chipDisplayLabel 灰色 fallback 分支，
  // 直接使用 typeStyle + statusStyle 的 ?? other/pending 兜底，确保未知值显示为
  // other（绿色）+ pending（琥珀色）而非灰色
  const bgCls = `${typeStyle.badgeClass} ${statusStyle.badgeClass}`;

  const chipTitle = (() => {
    if (conflictWith && conflictWith.length) {
      const names = conflictWith
        .map(w => `"${w.title}"(${new Date(w.startAt).toTimeString().slice(0,5)}${w.endAt ? '-' + new Date(w.endAt).toTimeString().slice(0,5) : ''})`)
        .join(tBound('calendar.shell.chips.listSeparator'));
      return tChip('calendar.shell.chips.conflictWithTitle', { list: names });
    }
    return ev.title;
  })();

  return (
    <div
      ref={chipRef}
      role="button"
      tabIndex={0}
      draggable={!!onDragStart}
      onDragStart={(e) => { setDragging(true); hideHover(); onDragStart?.(ev, e); }}
      onDragEnd={() => setDragging(false)}
      onTouchStart={handleTouchStart}
      onTouchMove={handleTouchMove}
      onTouchEnd={handleTouchEnd}
      onMouseEnter={showHover}
      onMouseLeave={hideHover}
      onFocus={showHover}
      onBlur={hideHover}
      onClick={(e) => { e.stopPropagation(); hideHover(); onOpen(ev); }}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          hideHover();
          onOpen(ev);
        }
      }}
      title={hoverPreview ? undefined : chipTitle}
      data-event-id={ev.id}
      className={[
        'relative group rounded-md px-[length:var(--cal-chip-px)] py-[length:var(--cal-chip-py)] text-[length:var(--cal-chip-text)] leading-tight cursor-pointer select-none',
        'border transition-all shadow-sm hover:brightness-110 hover:scale-[1.02] hover:shadow-lg focus:outline-none focus:ring-2 focus:ring-sky-500/50',
        bgCls,
        focused ? 'ring-2 ring-sky-500 ring-offset-1 dark:ring-offset-slate-900' : '',
        dimmed ? 'opacity-40 grayscale' : '',
        conflictTag === 'hard' ? 'border-l-[3px] border-l-rose-500 shadow-rose-500/20 shadow-sm' : '',
        conflictTag === 'soft' ? 'border-l-[3px] border-l-amber-400 shadow-amber-400/20 shadow-sm' : '',
        dragging ? 'opacity-60 scale-[1.03] transition-all duration-180 z-20' : '',
        isSourceDragged ? 'opacity-35 scale-[0.99] ring-2 ring-white/50' : '',
        hoverPreview ? 'z-30' : '',
        selected
          ? [
              // 内描边方案：ring-inset 画在元素内部，不向外扩展 → 不会遮挡相邻行程
              // (1) 2px 内描边 sky-500 → 任何底色上都立刻出边界
              'ring-2 ring-inset ring-sky-500',
              // (2) 轻微抬升 + 蓝色投影光晕 → 浮起感但不放大
              'z-20 shadow-[0_4px_12px_-4px_rgba(14,165,233,0.7)]',
              // (3) 轻微降饱和，凸显蓝色调
              'saturate-95',
            ].join(' ')
          : '',
      ].filter(Boolean).join(' ')}
      style={_colorOverrideStyle(ev)}
    >
      {/* 多选选中徽章：元素内部右上角，不超出边界 → 不会被父容器裁剪 */}
      {selected && (
        <span
          className="cal-selected-badge absolute bottom-0 right-0 z-10"
          aria-hidden
        >
          <CheckCircle2
            className="w-4 h-4 text-sky-500 fill-white"
            strokeWidth={2.75}
          />
        </span>
      )}
      {/* 挂起同步标记：右上角灰色小圆点 + 淡描边 ring，hover 标题显示"本地修改待后端同步"
          刷新后如果后端尚未写库，会在 replay patch 的日程上出现此标记；下一次 refreshEvents
          确认到后端值已同步 → 自动从 pendingPatchIds 清除，点消失 */}
      {isPendingSync && (
        <span
          className="cal-sync-pending-dot absolute top-1 right-1 z-10 w-2 h-2 rounded-full bg-slate-400 ring-2 ring-white/70 dark:ring-slate-900/70"
          title={tBound('calendar.common.pendingSyncHint')}
          aria-label={tBound('calendar.common.pendingSync')}
        />
      )}
      <div className="flex items-center gap-1 min-w-0">
        {/* 圆点永远显示 eventType 的类型色（与顶部筛选按钮一致），不受自定义背景色影响 */}
        <span className={`shrink-0 w-1.5 h-1.5 rounded-full ring-1 ring-white/50 dark:ring-slate-900/50 ${typeStyle.dotClass}`} />
        <span className="truncate font-medium">{ev.title || tBound('calendar.common.untitled')}</span>
      </div>
      {!compact && !ev.isAllDay && (
        <div className="truncate mt-0.5 opacity-75">
          {_chipTimeLabel(ev)}
          {ev.createdByName && <span className="ml-1 opacity-90">· {ev.createdByName}</span>}
        </div>
      )}
      {!compact && ev.isAllDay && (
        <div className="truncate mt-0.5 opacity-75">
          {_allDayTimeLabel(ev)}
          {ev.createdByName && <span className="ml-1 opacity-90">· {ev.createdByName}</span>}
        </div>
      )}
      {conflictCount! > 0 && conflictTag && conflictTag !== 'none' && (
        <span className={`cal-conflict-badge ${conflictTag}`}>
          {conflictCount! > 1 ? conflictCount : conflictTag === 'hard' ? '⚠' : '○'}
        </span>
      )}
      {conflictCount! > 0 && conflictTag && conflictTag !== 'none' && (
        <span className="cal-chip-hoverhint pointer-events-none">
          {tChip('calendar.shell.chips.conflictCount', { count: conflictCount! })}
        </span>
      )}
      {hoverPreview && (
        <EventHoverCard
          ev={ev}
          conflictTag={conflictTag}
          conflictCount={conflictCount}
          conflictWith={conflictWith}
        />
      )}
    </div>
  );
}

/* =========================================================
   EventHoverCard —— 交互增强-2: 事件悬浮预览卡片
   悬停 ≥320ms 后显示，pointer-events-none 不干扰点击/拖拽
   ========================================================= */
function EventHoverCard({ ev, conflictTag, conflictCount, conflictWith }: {
  ev: CalendarEventDto;
  conflictTag?: 'none' | 'soft' | 'hard';
  conflictCount?: number;
  conflictWith?: { title: string; startAt: string; endAt?: string | null }[];
}) {
  const typeStyle = getEventTypeStyle(ev.eventType);
  const statusStyle = getStatusStyle(ev.status);
  /**
   * 后端下发的 `EventTypeLabel` / `StatusLabel` 是**硬编码中文**
   * （`NoneadCSS.Application/Mappings/MappingExtensions.cs:420-436`，非 resx），
   * 且 `statusLabels` 只覆盖 `pending`/`completed`/`cancelled` 三个码 ——
   * `in_progress` / `missed` 会回落成**裸码**（`MappingExtensions.cs:455` 的 `: evt.Status`）。
   *
   * 因此按语言分派：
   *   · 英文界面 → 一律走前端字典（`typeStyle.label` / `statusStyle.label`，由 `calendarUtils.ts`
   *     的读取时 getter 按绑定语言取词），既不漏中文也不露裸码；
   *   · 中文界面 → **保留"优先后端 label"的原有行为**。两个理由：① 后端 `task` = `计划任务`、
   *     前端 `EVENT_TYPE_STYLE.task.label` = `任务`，逐字不同，改成前端字典会让中文界面**回退**；
   *     ② 该优先级被 4 条既有用例写成契约（`src/__tests__/calendar-store.test.tsx:1861/1867/1873/1879`），
   *     改测试需队长授权（见交付报告「偏差与需裁定项」）。中文侧行为与改造前**逐字节相同**。
   */
  const preferServerLabel = !getBoundLocale().toLowerCase().startsWith('en');
  const pad = (n: number) => String(n).padStart(2, '0');
  const fmtTimeRange = (start: string, end?: string | null) => {
    const s = new Date(start);
    const sStr = `${pad(s.getHours())}:${pad(s.getMinutes())}`;
    if (!end) return sStr;
    const e = new Date(end);
    return `${sStr} - ${pad(e.getHours())}:${pad(e.getMinutes())}`;
  };
  const fmtDate = (start: string) => {
    const d = new Date(start);
    return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())}`;
  };

  return (
    <div
      role="tooltip"
      className="cal-hovercard pointer-events-none absolute left-1/2 -translate-x-1/2 top-full mt-1.5 z-50 w-64 max-w-[280px] rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 shadow-xl shadow-slate-900/10 dark:shadow-black/40 text-slate-700 dark:text-slate-200 overflow-hidden"
    >
      {/* 顶部色条：自定义颜色优先于事件类型色 */}
      <div
        className={`h-1 w-full ${ev.color ? '' : typeStyle.dotClass}`}
        style={ev.color ? { backgroundColor: ev.color } : undefined}
      />
      <div className="p-3 space-y-2 text-xs">
        {/* 标题（完整显示，最多 3 行） */}
        <div className="font-semibold text-sm leading-snug text-slate-900 dark:text-white line-clamp-3 break-words">
          {ev.title || tBound('calendar.common.untitled')}
        </div>

        {/* 时间 */}
        <div className="flex items-start gap-1.5 text-slate-500 dark:text-slate-400">
          <svg className="w-3.5 h-3.5 mt-0.5 shrink-0" viewBox="0 0 16 16" fill="currentColor" aria-hidden>
            <path d="M8 1.5A6.5 6.5 0 1 0 14.5 8 6.5 6.5 0 0 0 8 1.5Zm0 1.5a5 5 0 1 1-5 5 5 5 0 0 1 5-5Zm0 1.5a.75.75 0 0 1 .75.75V8l2.5 1.5a.75.75 0 1 1-.75 1.3l-3-1.8A.75.75 0 0 1 7.25 8V5.25A.75.75 0 0 1 8 4.5Z" />
          </svg>
          <div className="min-w-0">
            <div className="tabular-nums">{fmtDate(ev.startAt)}</div>
            <div className="tabular-nums text-slate-600 dark:text-slate-300">
              {ev.isAllDay ? tBound('calendar.common.allDayEvent') : fmtTimeRange(ev.startAt, ev.endAt)}
              {ev.createdByName ? tChip('calendar.shell.chips.creatorSuffix', { name: ev.createdByName }) : ''}
            </div>
          </div>
        </div>

        {/* 类型 + 状态 标签 */}
        <div className="flex flex-wrap gap-1.5">
          {/* badge 背景用自定义色覆盖，圆点永远显示 eventType 的类型色（与顶部筛选按钮一致） */}
          <span
            className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-medium ${ev.color ? '' : typeStyle.badgeClass}`}
            style={_colorOverrideStyle(ev)}
          >
            <span className={`w-1.5 h-1.5 rounded-full ${typeStyle.dotClass}`} />
            {/* 中文态沿用手术后端中文标签（见 `preferServerLabel` 的注释），英文态走前端字典 */}
            {preferServerLabel ? ev.eventTypeLabel || typeStyle.label : typeStyle.label}
          </span>
          <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-[11px] font-medium ${statusStyle.badgeClass}`}>
            {preferServerLabel ? ev.statusLabel || statusStyle.label : statusStyle.label}
          </span>
          {typeof ev.priority === 'number' && ev.priority > 0 && (
            <span className="inline-flex items-center rounded-full px-2 py-0.5 text-[11px] font-medium bg-orange-50 dark:bg-orange-900/30 text-orange-700 dark:text-orange-300 ring-1 ring-orange-200/60 dark:ring-orange-700/40">
              P{ev.priority}
            </span>
          )}
        </div>

        {/* 地点 */}
        {ev.location && (
          <div className="flex items-start gap-1.5 text-slate-500 dark:text-slate-400">
            <svg className="w-3.5 h-3.5 mt-0.5 shrink-0" viewBox="0 0 16 16" fill="currentColor" aria-hidden>
              <path d="M8 1a4 4 0 0 0-4 4c0 2.5 3.2 7.7 3.6 8.3a.5.5 0 0 0 .8 0C8.8 12.7 12 7.5 12 5a4 4 0 0 0-4-4Zm0 5.5A1.5 1.5 0 1 1 8 3.5a1.5 1.5 0 0 1 0 3Z" />
            </svg>
            <span className="truncate">{ev.location}</span>
          </div>
        )}

        {/* 会议链接 */}
        {ev.meetingUrl && (
          <div className="flex items-start gap-1.5 text-sky-600 dark:text-sky-400">
            <svg className="w-3.5 h-3.5 mt-0.5 shrink-0" viewBox="0 0 16 16" fill="currentColor" aria-hidden>
              <path d="M3.5 3.5h4v-1h-5v5h1v-2.5l3.4 3.4.7-.7L3.5 3.5Zm6 9h4v-4h-1v2.5l-3.4-3.4-.7.7 3.4 3.4h-2.3v1Z" />
            </svg>
            <span className="truncate">{ev.meetingUrl}</span>
          </div>
        )}

        {/* 描述（最多 3 行） */}
        {ev.description && (
          <div className="text-slate-500 dark:text-slate-400 line-clamp-3 break-words whitespace-pre-wrap border-t border-slate-100 dark:border-slate-800 pt-2">
            {ev.description}
          </div>
        )}

        {/* 冲突明细 */}
        {conflictCount! > 0 && conflictTag && conflictTag !== 'none' && (
          <div className={`border-t border-slate-100 dark:border-slate-800 pt-2 text-[11px] ${conflictTag === 'hard' ? 'text-rose-600 dark:text-rose-400' : 'text-amber-600 dark:text-amber-400'}`}>
            <div className="flex items-center gap-1.5 font-medium">
              <span aria-hidden>⚠</span>
              <span>{tChip('calendar.shell.chips.conflictDetail', {
                count: conflictCount!,
                kind: tBound(conflictTag === 'hard' ? 'calendar.shell.chips.severeOverlap' : 'calendar.shell.chips.edgeOverlap'),
              })}</span>
            </div>
            {conflictWith && conflictWith.length > 0 && (
              <ul className="mt-1 ml-5 list-disc space-y-0.5">
                {conflictWith.slice(0, 3).map((w, i) => (
                  <li key={i} className="truncate">
                    <span>{w.title}</span>
                    <span className="opacity-70 ml-1 tabular-nums">
                      ({new Date(w.startAt).toTimeString().slice(0, 5)}
                      {w.endAt ? '-' + new Date(w.endAt).toTimeString().slice(0, 5) : ''})
                    </span>
                  </li>
                ))}
                {conflictWith.length > 3 && <li className="opacity-60">{tChip('calendar.shell.chips.moreConflicts', { count: conflictWith.length - 3 })}</li>}
              </ul>
            )}
          </div>
        )}

        {/* 关联线索 */}
        {(ev.userName || ev.mailboxName || ev.conversationSubject) && (
          <div className="border-t border-slate-100 dark:border-slate-800 pt-2 text-[11px] text-slate-400 dark:text-slate-500 space-y-0.5">
            {ev.userName && (
              <div className="flex items-center gap-1">
                <span aria-hidden className="opacity-60">👤</span>
                <span className="truncate">{ev.userName}</span>
              </div>
            )}
            {ev.mailboxName && (
              <div className="flex items-center gap-1">
                <span aria-hidden className="opacity-60">📬</span>
                <span className="truncate">{ev.mailboxName}</span>
              </div>
            )}
            {ev.conversationSubject && (
              <div className="flex items-center gap-1">
                <span aria-hidden className="opacity-60">💬</span>
                <span className="truncate">{ev.conversationSubject}</span>
              </div>
            )}
          </div>
        )}

        {/* 底部提示 */}
        <div className="pt-1.5 mt-1 border-t border-slate-100 dark:border-slate-800 text-[10px] text-slate-400 dark:text-slate-500">
          {tBound('calendar.shell.chips.hoverHint')}
        </div>
      </div>
    </div>
  );
}

export function _eventChipMemoComparator(prev: EventChipProps, next: EventChipProps): boolean {
  const cw = prev.conflictWith, ncw = next.conflictWith;
  let conflictWithSame = cw === ncw;
  if (!conflictWithSame && cw && ncw && cw.length === ncw.length) {
    conflictWithSame = true;
    for (let i = 0; i < cw.length; i++) {
      const a = cw[i], b = ncw[i];
      if (a.title !== b.title || a.startAt !== b.startAt || a.endAt !== b.endAt) {
        conflictWithSame = false;
        break;
      }
    }
  }
  return (
    prev.ev === next.ev &&
    prev.ymdKey === next.ymdKey &&
    prev.onOpen === next.onOpen &&
    prev.onDragStart === next.onDragStart &&
    prev.compact === next.compact &&
    prev.dimmed === next.dimmed &&
    prev.conflictTag === next.conflictTag &&
    prev.conflictCount === next.conflictCount &&
    prev.focused === next.focused &&
    prev.selected === next.selected &&
    conflictWithSame
  );
}
export const EventChip = memo(EventChipImpl, _eventChipMemoComparator);
EventChip.displayName = 'EventChip';

/* =========================================================
   MoreEventsChip —— 日格上限 >4 条时显示 "+N 更多"
   ========================================================= */
export interface MoreEventsChipProps {
  count: number;
  onOpen: () => void;
}

function MoreEventsChipImpl({ count, onOpen }: MoreEventsChipProps) {
  return (
    <button
      type="button"
      onClick={onOpen}
      className="self-start rounded-md px-[length:var(--cal-chip-px)] py-[length:var(--cal-chip-py)] text-[length:var(--cal-chip-text)] leading-tight font-medium tabular-nums
                 text-slate-500 hover:text-slate-700 bg-slate-100/50 dark:bg-slate-800/50 hover:bg-slate-200/70 dark:hover:bg-slate-700/50
                 focus:outline-none focus:ring-2 focus:ring-slate-400/40 transition-colors"
    >
      +{count}
    </button>
  );
}

export const MoreEventsChip = memo(MoreEventsChipImpl, (p, n) => p.count === n.count && p.onOpen === n.onOpen);

/* =========================================================
   AllDayEventBar —— 周/日视图顶部的全天条
   ========================================================= */
export interface AllDayEventBarProps {
  ev: CalendarEventDto;
  onOpen: (ev: CalendarEventDto) => void;
  onDragStart?: (ev: CalendarEventDto, e: React.DragEvent<HTMLDivElement>) => void;
  dimmed?: boolean;
  /** 多选模式下选中态：多层视觉叠加（粗描边+偏移+勾选徽章+抬升阴影），在任何自定义色背景上都明显 */
  selected?: boolean;
}

function AllDayEventBarImpl({ ev, onOpen, onDragStart, dimmed, selected }: AllDayEventBarProps) {
  const typeStyle = getEventTypeStyle(ev.eventType);
  const statusStyle = getStatusStyle(ev.status);
  const isSourceDragged = useCalendarStore((s) => s.draggedEvent?.id === ev.id);
  return (
    <div
      draggable={!!onDragStart}
      onDragStart={(e) => onDragStart?.(ev, e)}
      onClick={(e) => { e.stopPropagation(); onOpen(ev); }}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          onOpen(ev);
        }
      }}
      className={[
        'relative group flex items-center gap-1.5 w-full rounded-md px-2 py-1 text-left text-xs font-medium',
        'border transition-all shadow-sm hover:brightness-110 hover:scale-[1.01] hover:shadow-md focus:outline-none focus:ring-2 focus:ring-sky-500/50',
        typeStyle.badgeClass,
        statusStyle.badgeClass,
        dimmed ? 'opacity-40 grayscale' : '',
        isSourceDragged ? 'opacity-35 scale-[0.99] ring-2 ring-white/50' : '',
        'cursor-pointer select-none',
        selected
          ? [
              // 内描边方案：ring-inset 画在元素内部，不向外扩展 → 不会遮挡相邻行程
              'ring-2 ring-inset ring-sky-500',
              'z-20 shadow-[0_4px_12px_-4px_rgba(14,165,233,0.7)]',
              'saturate-95',
            ].join(' ')
          : '',
      ].filter(Boolean).join(' ')}
      style={_colorOverrideStyle(ev)}
    >
      {/* 多选选中徽章：元素内部右上角，不超出边界 */}
      {selected && (
        <span
          className="cal-selected-badge absolute bottom-0 right-0 z-10"
          aria-hidden
        >
          <CheckCircle2
            className="w-4 h-4 text-sky-500 fill-white"
            strokeWidth={2.75}
          />
        </span>
      )}
      {/* 圆点永远显示 eventType 的类型色（与顶部筛选按钮一致），不受自定义背景色影响 */}
      <span className={`shrink-0 w-1.5 h-1.5 rounded-full ring-1 ring-white/50 dark:ring-slate-900/50 ${typeStyle.dotClass}`} />
      <span className="truncate">{ev.title || tBound('calendar.common.untitled')}</span>
      <span className="shrink-0 opacity-70">{_allDayTimeLabel(ev)}</span>
    </div>
  );
}

/** AllDayEventBar memo 比较函数：导出以便单测直接验证 5 个 prop 的引用相等性 */
export function _allDayEventBarMemoComparator(p: AllDayEventBarProps, n: AllDayEventBarProps): boolean {
  return p.ev === n.ev && p.onOpen === n.onOpen && p.onDragStart === n.onDragStart && p.dimmed === n.dimmed && p.selected === n.selected;
}

export const AllDayEventBar = memo(AllDayEventBarImpl, _allDayEventBarMemoComparator);

EventChip.displayName = 'EventChip';
MoreEventsChip.displayName = 'MoreEventsChip';
AllDayEventBar.displayName = 'AllDayEventBar';

/* =========================================================
   HourRow —— 周/日视图的 1 小时行；Grid 60px 高；事件 block 用 left/width/top/height absolute 定位
   Props 纯值化 → React.memo 友好（与 EventChip 同构）
   ========================================================= */
/** P1-A#2: HourRowColumnEvent 已统一迁移到 calendarUtils.ts，
 *  此处 re-export 保持对外 import 路径不变，避免破坏依赖。 */
export type { HourRowColumnEvent } from './calendarUtils';
export type { HourRowEventByHour, SingleDayColumnEvents } from './calendarUtils';
import type { HourRowColumnEvent as HourRowColumnEventLocal } from './calendarUtils';

// ⚠️ 新增 HourRowProps 字段必须同步更新下方 HourRow memo 比较函数
export interface HourRowProps {
  hour: number;
  columnYMDs: YMD[];
  eventsByColumn: HourRowColumnEventLocal[][];
  nowLineVisible: boolean;
  nowLineMinute: number;
  /** 小时格实际像素高度（56 桌面 / 44 移动），用于分钟→像素缩放 */
  hourHeight: number;
  dragOverColumn: number | null;
  /** 是否处于全局事件拖拽中：移动时给非源 chip 加 pointer-events:none，避免 dragover 被其他 chip 吃掉导致跨列移动没反应 */
  isDragging?: boolean;
  onCellClick?: (ymd: YMD, hour: number) => void;
  /** 拖拽悬停在单元格内时触发，hourFloat 为 15 分钟吸附的小数小时（如 10.25=10:15） */
  onDragOverCell?: (ymd: YMD, hourFloat: number, e: React.DragEvent<HTMLDivElement>) => void;
  /** 拖拽松手时触发，hourFloat 为 15 分钟吸附的小数小时 */
  onDropCell?: (ymd: YMD, hourFloat: number, e: React.DragEvent<HTMLDivElement>) => void;
  /** 高6 修复：传 eventStartAt 用于重复事件实例的唯一识别，避免按 id find 时取到错误实例 */
  onEventClick?: (eventId: number, eventStartAt?: string) => void;
  onEventDragStart?: (eventId: number, e: React.DragEvent<HTMLDivElement>) => void;
  /** 交互增强-3: 事件 resize 回调 */
  onEventResize?: (eventId: number, newEndAt: string) => void;
  /** Task E: drop 成功/失败闪烁列索引 */
  flashColumnIdx?: number | null;
  /** Task E: drop 成功/失败闪烁色调 */
  flashTone?: 'ok' | 'err' | null;
  /** 第三梯队: 键盘导航焦点事件 id（用于 ring 高亮） */
  focusedEventId?: number | null;
  /** P1-2: 多选模式下已选事件 id 集合（用于渲染多层选中视觉） */
  selectedEventIds?: ReadonlySet<number>;
  /** 拖拽预览信息：被拖动事件源 + 当前吸附位置（ymd + hourFloat），传入即渲染幽灵预览块 */
  dragPreview?: {
    draggedEvent: { id: number; title: string; color: string | null; eventType: string; durationMin: number } | null;
    previewYmd: YMD | null;
    previewHourFloat: number | null;
  } | null;
  /** 被拖拽的事件 id：给"源 chip"加半透明 + 抬升感，视觉上明确"这块被拎起来了" */
  draggedEventId?: number | null;
}

function HourRowImpl(p: HourRowProps) {
  const { hour, columnYMDs, eventsByColumn, nowLineVisible, nowLineMinute, hourHeight, dragOverColumn, isDragging, onCellClick, onDragOverCell, onDropCell, onEventClick, onEventDragStart, onEventResize, flashColumnIdx, flashTone, focusedEventId, selectedEventIds, dragPreview, draggedEventId } = p;
  const timeLabel = `${String(hour).padStart(2, '0')}:00`;
  const pxPerMin = hourHeight / 60;
  const colCount = columnYMDs.length;
  const gridTemplate = `repeat(${colCount}, minmax(0, 1fr))`;

  // 基于鼠标 Y 计算 15 分钟吸附的小数小时（如 10.25 = 10:15）
  function calcSnappedHourFloat(
    cellEl: HTMLDivElement,
    clientY: number,
    baseHour: number,
  ): number {
    const rect = cellEl.getBoundingClientRect();
    const relY = Math.max(0, Math.min(rect.height, clientY - rect.top));
    const minuteInHour = (relY / rect.height) * 60;
    // 15 分钟吸附：向下取整到 15 的倍数
    const snappedMin = Math.floor(minuteInHour / 15) * 15;
    return baseHour + snappedMin / 60;
  }

  return (
    <div
      className="flex w-full border-b border-slate-200 dark:border-slate-800"
      style={{ height: HOUR_HEIGHT_CSS }}
    >
      <div className={`${TIME_COL_W_CLS} shrink-0 pr-2 pt-0 text-right text-[11px] font-mono tabular-nums text-slate-400 dark:text-slate-500 leading-none select-none`}>
        {timeLabel}
      </div>
      <div
        className="relative flex-1 grid"
        style={{ gridTemplateColumns: gridTemplate }}
      >
        {columnYMDs.map((ymd, colIdx) => {
          const isDragOver = dragOverColumn === colIdx;
          // 判断当前列是否需要渲染拖拽幽灵预览
          const doRenderGhost = !!(
            dragPreview &&
            dragPreview.draggedEvent &&
            dragPreview.previewYmd === ymd &&
            dragPreview.previewHourFloat !== null &&
            // 预览时间落在此行 hour 区间内：hour <= previewHourFloat < hour + 1
            dragPreview.previewHourFloat >= hour &&
            dragPreview.previewHourFloat < hour + 1
          );
          const isFlashCell = flashColumnIdx === colIdx && flashTone;
          const flashClass = isFlashCell
            ? flashTone === 'ok'
              ? 'bg-emerald-50 dark:bg-emerald-950/40 ring-2 ring-emerald-400/50 dark:ring-emerald-700/40 transition-colors duration-150'
              : 'bg-rose-50 dark:bg-rose-950/40 ring-2 ring-rose-400/50 dark:ring-rose-700/40 transition-colors duration-150'
            : '';
          return (
            <div
              key={`${ymd}-${hour}`}
              role="button"
              tabIndex={0}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  onCellClick?.(ymd, hour);
                }
              }}
              onClick={() => onCellClick?.(ymd, hour)}
              onDragOver={(e) => {
                e.preventDefault();
                try {
                  if (e.dataTransfer) e.dataTransfer.dropEffect = 'move';
                } catch { /* ignore */ }
                const hourFloat = calcSnappedHourFloat(e.currentTarget, e.clientY, hour);
                onDragOverCell?.(ymd, hourFloat, e);
              }}
              onDrop={(e) => {
                e.preventDefault();
                const hourFloat = calcSnappedHourFloat(e.currentTarget, e.clientY, hour);
                onDropCell?.(ymd, hourFloat, e);
              }}
              title={`${formatDateYMDShort(ymd)} ${String(hour).padStart(2, '0')}:00`}
              style={{ overflow: 'visible' }}
              className={[
                'relative border-l border-slate-200 dark:border-slate-800 min-h-0 overflow-visible',
                isDragOver ? 'bg-sky-50/80 dark:bg-sky-950/40 ring-1 ring-sky-300/50 dark:ring-sky-700/30' : '',
                flashClass,
                onCellClick ? 'cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-800/40' : '',
              ].filter(Boolean).join(' ')}
            >
              {/* ===== 拖拽幽灵预览（实时显示目标位置，15 分钟吸附） ===== */}
              {doRenderGhost && dragPreview?.draggedEvent && dragPreview.previewHourFloat !== null && (() => {
                const ghostDurationMin = dragPreview.draggedEvent.durationMin;
                const previewHourFloat = dragPreview.previewHourFloat;
                const previewMinInHour = Math.round((previewHourFloat - Math.floor(previewHourFloat)) * 60);
                const ghostTopPx = Math.max(0, Math.min(hourHeight - 1, previewMinInHour * pxPerMin));
                // 跨小时幽灵只渲染落在当前行内的部分高度（剩余部分由后续 HourRow 补渲染，此处简化为 max 当前行可见高度）
                const ghostHeightRaw = ghostDurationMin * pxPerMin;
                const maxVisibleInRow = hourHeight - ghostTopPx;
                const ghostHeightPx = Math.max(MIN_EVENT_HEIGHT_PX, Math.min(ghostHeightRaw, maxVisibleInRow));
                const ghostBg = _timegridBackgroundColor(dragPreview.draggedEvent);
                return (
                  <div
                    className="absolute left-0.5 right-0.5 rounded-sm px-1 text-[11px] leading-tight truncate pointer-events-none z-30 border border-dashed border-white/50 shadow-lg"
                    style={{
                      top: ghostTopPx,
                      height: ghostHeightPx,
                      backgroundColor: ghostBg,
                      opacity: 0.55,
                      lineHeight: `${Math.min(ghostHeightPx, 20)}px`,
                      filter: 'saturate-110',
                    }}
                  >
                    <span className="text-white font-medium drop-shadow">
                      {dragPreview.draggedEvent.title || tBound('calendar.common.untitled')}
                    </span>
                  </div>
                );
              })()}
              {eventsByColumn[colIdx]?.map((ev) => {
                const topPx = Math.max(0, Math.min(hourHeight - 1, ev.startOffsetMin * pxPerMin));
                const heightPx = Math.max(MIN_EVENT_HEIGHT_PX, Math.min(hourHeight - topPx, ev.durationMin * pxPerMin));
                const isEvSelected = !!selectedEventIds && selectedEventIds.has(ev.eventId);
                return (
                  <div
                    key={ev.chipKey}
                    draggable={!!onEventDragStart}
                    onDragStart={(e) => { e.stopPropagation(); onEventDragStart?.(ev.eventId, e); }}
                    onClick={(e) => {
                      e.stopPropagation();
                      onEventClick?.(ev.eventId, ev.eventStartAt);
                    }}
                    role="button"
                    tabIndex={0}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        e.stopPropagation();
                        onEventClick?.(ev.eventId, ev.eventStartAt);
                      }
                    }}
                    aria-label={`${ev.title || tBound('calendar.shell.chips.untitledEvent')}, ${tChip('calendar.shell.chips.eventAriaTime', { offset: ev.startOffsetMin, duration: ev.durationMin, count: ev.durationMin })}${isEvSelected ? tBound('calendar.shell.chips.eventAriaSelected') : ''}`}
                    className={[
                      'absolute left-0.5 right-0.5 rounded-sm px-1 text-[11px] leading-tight overflow-hidden cursor-pointer',
                      'border border-white/30 shadow-md ring-1 ring-white/20 hover:brightness-125 hover:shadow-lg focus:outline-none focus:ring-2 focus:ring-sky-500/50 transition-all',
                      'text-white font-medium',
                      // 全局 dragging 时：非源 chip 拦截鼠标事件，保证 dragover 命中空格子
                      // 源 chip 保持可拖但半透明（被拎起的视觉感）
                      isDragging && draggedEventId !== ev.eventId ? 'pointer-events-none select-none' : '',
                      draggedEventId === ev.eventId ? 'opacity-40 scale-[0.99] ring-2 ring-white/40' : '',
                      focusedEventId === ev.eventId ? 'ring-2 ring-sky-400 ring-offset-1' : '',
                      isEvSelected
                        ? [
                            // 内描边方案：ring-inset 画在元素内部，不向外扩展
                            'ring-2 ring-inset ring-sky-500',
                            'z-20 shadow-[0_4px_10px_-4px_rgba(14,165,233,0.7)]',
                            'saturate-95',
                          ].join(' ')
                        : '',
                    ].join(' ')}
                    style={{
                      top: topPx,
                      height: heightPx,
                      backgroundColor: _timegridBackgroundColor(ev),
                      lineHeight: `${Math.min(heightPx, 20)}px`,
                    }}
                    title={ev.title}
                    data-event-id={ev.eventId}
                    data-resize-event-id={ev.eventId}
                  >
                    {/* 选中徽章：元素内部右上角，不超出边界 */}
                    {isEvSelected && heightPx >= 18 && (
                      <span
                        className="cal-selected-badge absolute bottom-0 right-0 z-10"
                        aria-hidden
                      >
                        <CheckCircle2
                          className="w-3.5 h-3.5 text-sky-500 fill-white"
                          strokeWidth={2.75}
                        />
                      </span>
                    )}
                    <div className="truncate">{ev.title || tBound('calendar.common.untitled')}</div>
                    {/* 高度足够时（>=28px），在标题下方显示"时间段 · 创建者姓名" */}
                    {heightPx >= 28 && !ev.isAllDay && (() => {
                      const pad = (n: number) => String(n).padStart(2, '0');
                      const s = new Date(ev.eventStartAt);
                      const e = new Date(ev.eventEndAt);
                      const timeLabel = `${pad(s.getHours())}:${pad(s.getMinutes())}-${pad(e.getHours())}:${pad(e.getMinutes())}`;
                      return (
                        <div className="truncate text-[10px] leading-tight opacity-80 mt-0.5 font-normal">
                          {timeLabel}
                          {ev.createdByName && <span className="ml-1">· {ev.createdByName}</span>}
                        </div>
                      );
                    })()}
                    {onEventResize && heightPx >= 24 && ev.isLastSegment && (
                      <div
                        className="cal-resize-handle"
                        onMouseDown={(e) => {
                          e.stopPropagation();
                          e.preventDefault();
                          // 直接闭包捕获当前被拖的 chip 元素，避免跨天事件 querySelector 选错列
                          const handleEl = e.currentTarget as HTMLElement;
                          const chipEl = handleEl.parentElement as HTMLElement | null;
                          if (!chipEl) return;
                          const startY = e.clientY;
                          const startDurationMin = ev.durationMin;
                          const eventStart = new Date(ev.eventStartAt);
                          const eventEnd = new Date(ev.eventEndAt);
                          const originalTotalMin = Math.max(
                            15,
                            Math.round((+eventEnd - +eventStart) / 60000),
                          );
                          // 末段开始时间 = 当前列日期 T 当前小时:本 chip 的 startOffsetMin
                          // 用于向上缩短时正确计算「末段应有的视觉高度」，避免 (startDurationMin + delta) 变负后锁死在 MIN 高度
                          const pad2 = (n: number) => String(n).padStart(2, '0');
                          const segmentStart = new Date(
                            `${ymd}T${pad2(hour)}:${pad2(ev.startOffsetMin)}:00`,
                          );
                          const segmentStartTs = +segmentStart;
                          if (isNaN(segmentStartTs)) return;

                          const computeNewEnd = (totalMin: number) => {
                            const end = new Date(eventStart);
                            end.setMinutes(end.getMinutes() + totalMin);
                            return end;
                          };

                          // 保存原 inline zIndex，结束时恢复；拖拽期间置顶，避免与邻近 chip 视觉重叠
                          const origZIndex = chipEl.style.zIndex;
                          chipEl.style.zIndex = '40';

                          const handleMouseMove = (moveE: MouseEvent) => {
                            const deltaY = moveE.clientY - startY;
                            const deltaMin = Math.round((deltaY / pxPerMin) / 15) * 15;
                            const newTotalMin = Math.max(15, originalTotalMin + deltaMin);
                            const newEndTs = +computeNewEnd(newTotalMin);
                            const newSegmentMin = Math.max(
                              0,
                              Math.round((newEndTs - segmentStartTs) / 60000),
                            );
                            const newSegmentHeight = Math.max(
                              MIN_EVENT_HEIGHT_PX,
                              newSegmentMin * pxPerMin,
                            );
                            chipEl.style.height = `${newSegmentHeight}px`;
                            void startDurationMin;
                          };

                          const handleMouseUp = (upE: MouseEvent) => {
                            document.removeEventListener('mousemove', handleMouseMove);
                            document.removeEventListener('mouseup', handleMouseUp);
                            const deltaY = upE.clientY - startY;
                            const deltaMin = Math.round((deltaY / pxPerMin) / 15) * 15;
                            const newTotalMin = Math.max(15, originalTotalMin + deltaMin);
                            if (newTotalMin !== originalTotalMin) {
                              // 基于完整事件的 startAt 计算新 endAt，保证跨小时/跨日期 resize 结果正确
                              const newEnd = computeNewEnd(newTotalMin);
                              onEventResize(ev.eventId, newEnd.toISOString());
                            }
                            // 清理内联样式（之后乐观更新接管真实渲染）
                            chipEl.style.height = '';
                            chipEl.style.zIndex = origZIndex;
                            document.body.style.cursor = '';
                          };

                          document.addEventListener('mousemove', handleMouseMove);
                          document.addEventListener('mouseup', handleMouseUp);
                          document.body.style.cursor = 'ns-resize';
                        }}
                      />
                    )}
                  </div>
                );
              })}
            </div>
          );
        })}
        {nowLineVisible && (
          <div
            className="absolute left-0 right-0 z-10 h-[2px] bg-gradient-to-r from-rose-400 to-rose-500 pointer-events-none"
            style={{ top: Math.max(0, Math.min(hourHeight - 1, nowLineMinute * pxPerMin)), transition: 'top 0.5s ease' }}
          >
            <div className="absolute -left-1 -top-1 w-2 h-2 rounded-full bg-rose-500 ring-2 ring-white dark:ring-slate-900 cal-now-line-dot" />
          </div>
        )}
      </div>
    </div>
  );
}

// HourRow memo 比较策略：
// 1) 基础标量（hour/nowLine/dragOver）用 === 比较
// 2) columnYMDs 按字符串逐个比较（YMD 是稳定字符串 key）
// 3) eventsByColumn 需深度比较字段：只比较 chipKey 不够，
//    因为编辑事件（改 title/color/时长等）但 id 不变 → chipKey 也不变，
//    会导致旧数据停留在渲染层不更新。
//    注意：buildColumnEventsForWeek 每次都重建对象，引用天然不稳定，不能直接用 ===。
export function _hourRowMemoComparator(a: HourRowProps, b: HourRowProps): boolean {
  if (a.hour !== b.hour) return false;
  if (a.nowLineVisible !== b.nowLineVisible || a.nowLineMinute !== b.nowLineMinute) return false;
  if (a.hourHeight !== b.hourHeight) return false;
  if (a.dragOverColumn !== b.dragOverColumn) return false;
  if (a.isDragging !== b.isDragging) return false;
  if (a.flashColumnIdx !== b.flashColumnIdx) return false;
  if (a.flashTone !== b.flashTone) return false;
  if (a.focusedEventId !== b.focusedEventId) return false;
  // dragPreview 按字段逐项比较
  {
    const pa = a.dragPreview, pb = b.dragPreview;
    if (pa !== pb) {
      if (!pa || !pb) return false;
      if (pa.previewYmd !== pb.previewYmd || pa.previewHourFloat !== pb.previewHourFloat) return false;
      const da = pa.draggedEvent, db = pb.draggedEvent;
      if (da !== db) {
        if (!da || !db) return false;
        if (da.id !== db.id || da.title !== db.title || da.color !== db.color
          || da.eventType !== db.eventType || da.durationMin !== db.durationMin) return false;
      }
    }
  }
  // P1-2: selectedEventIds 按值比较（Set 引用每次都新建，不能用 ===）
  {
    const sa = a.selectedEventIds, sb = b.selectedEventIds;
    if (sa !== sb) {
      if (!sa || !sb) return false;
      if (sa.size !== sb.size) return false;
      // 遍历任一方向均可，此处遍历 sa 检查 sb 是否全包含
      for (const id of sa) if (!sb.has(id)) return false;
    }
  }
  if (a.columnYMDs.length !== b.columnYMDs.length) return false;
  for (let i = 0; i < a.columnYMDs.length; i++) {
    if (a.columnYMDs[i] !== b.columnYMDs[i]) return false;
    const ea = a.eventsByColumn[i] ?? [];
    const eb = b.eventsByColumn[i] ?? [];
    if (ea.length !== eb.length) return false;
    for (let j = 0; j < ea.length; j++) {
      const aj = ea[j], bj = eb[j];
      if (aj.chipKey !== bj.chipKey
        || aj.title !== bj.title
        || aj.color !== bj.color
        || aj.startOffsetMin !== bj.startOffsetMin
        || aj.durationMin !== bj.durationMin) return false;
    }
  }
  return a.onCellClick === b.onCellClick && a.onDragOverCell === b.onDragOverCell && a.onDropCell === b.onDropCell && a.onEventClick === b.onEventClick && a.onEventDragStart === b.onEventDragStart && a.onEventResize === b.onEventResize;
}

export const HourRow = memo(HourRowImpl, _hourRowMemoComparator);

HourRow.displayName = 'HourRow';

/* =========================================================
   EmptyState —— 空状态占位（P2-2 Phase 2：SVG 插画 + variant + CTA）
   ========================================================= */
export type EmptyStateVariant = 'month' | 'week' | 'day' | 'list' | 'overview' | 'search';

export interface EmptyStateProps {
  variant?: EmptyStateVariant;
  icon?: React.ReactNode;
  title: string;
  description?: string;
  action?: React.ReactNode;
  /** 搜索态专用：当前关键词 */
  keyword?: string;
  /** CTA 按钮回调，传入则显示「+ 新建事件」按钮 */
  onPrimary?: () => void;
}

/**
 * day 变体插画里的水印文字。**必须是组件**：`VARIANT_SVG` 是模块级常量，
 * 直接写 `{tBound(...)}` 会在模块加载那一刻求值，把语言冻死（切语言后不跟随）。
 */
function DayVariantWatermark() {
  return <>{tBound('calendar.common.today')}</>;
}

const VARIANT_SVG: Record<EmptyStateVariant, React.ReactNode> = {
  month: (
    <svg width="180" height="120" viewBox="0 0 240 160" fill="none" xmlns="http://www.w3.org/2000/svg">
      <rect x="30" y="20" width="180" height="120" rx="12" stroke="currentColor" strokeWidth="2" opacity="0.3" />
      <line x1="30" y1="48" x2="210" y2="48" stroke="currentColor" strokeWidth="2" opacity="0.3" />
      <line x1="80" y1="20" x2="80" y2="140" stroke="currentColor" strokeWidth="1" opacity="0.15" />
      <line x1="130" y1="20" x2="130" y2="140" stroke="currentColor" strokeWidth="1" opacity="0.15" />
      <line x1="180" y1="20" x2="180" y2="140" stroke="currentColor" strokeWidth="1" opacity="0.15" />
      <line x1="30" y1="80" x2="210" y2="80" stroke="currentColor" strokeWidth="1" opacity="0.15" />
      <line x1="30" y1="110" x2="210" y2="110" stroke="currentColor" strokeWidth="1" opacity="0.15" />
      <circle cx="195" cy="10" r="10" fill="#0ea5e9" opacity="0.6" />
      <circle cx="175" cy="6" r="6" fill="#8b5cf6" opacity="0.5" />
      <circle cx="210" cy="22" r="5" fill="#10b981" opacity="0.5" />
      <path d="M200 2 L215 2 L215 17 L207 17 L200 10 Z" fill="currentColor" opacity="0.2" />
    </svg>
  ),
  week: (
    <svg width="180" height="120" viewBox="0 0 240 160" fill="none" xmlns="http://www.w3.org/2000/svg">
      <line x1="20" y1="20" x2="20" y2="140" stroke="currentColor" strokeWidth="2" opacity="0.2" />
      {[0, 1, 2, 3, 4, 5, 6].map((i) => (
        <line key={i} x1={50 + i * 27} y1="20" x2={50 + i * 27} y2="140" stroke="currentColor" strokeWidth="1" opacity="0.15" />
      ))}
      <rect x="55" y="35" width="22" height="14" rx="3" fill="#0ea5e9" opacity="0.15" />
      <rect x="82" y="55" width="22" height="20" rx="3" fill="#8b5cf6" opacity="0.15" />
      <rect x="136" y="40" width="22" height="16" rx="3" fill="#10b981" opacity="0.15" />
      <circle cx="200" cy="130" r="14" fill="currentColor" opacity="0.08" />
      <path d="M200 125 L200 135 M195 130 L205 130" stroke="currentColor" strokeWidth="2" strokeLinecap="round" opacity="0.4" />
    </svg>
  ),
  day: (
    <svg width="180" height="120" viewBox="0 0 240 160" fill="none" xmlns="http://www.w3.org/2000/svg">
      <text x="120" y="70" textAnchor="middle" fontSize="48" fontWeight="bold" fill="currentColor" opacity="0.12"><DayVariantWatermark /></text>
      <path d="M170 95 Q170 85 178 85 Q186 85 186 95 L186 100 L190 100 L190 110 L166 110 L166 100 L170 100 Z" fill="currentColor" stroke="currentColor" strokeWidth="1.5" opacity="0.3" />
      <path d="M178 85 Q178 78 182 78 Q186 78 186 85" fill="none" stroke="currentColor" strokeWidth="1.5" opacity="0.3" />
    </svg>
  ),
  list: (
    <svg width="180" height="120" viewBox="0 0 240 160" fill="none" xmlns="http://www.w3.org/2000/svg">
      {[0, 1, 2].map((i) => (
        <g key={i} opacity="0.2">
          <circle cx="40" cy={40 + i * 30} r="4" fill="currentColor" />
          <rect x="55" y={35 + i * 30} width="140" height="10" rx="5" fill="currentColor" />
        </g>
      ))}
      <circle cx="190" cy="120" r="18" fill="none" stroke="currentColor" strokeWidth="2" opacity="0.25" />
      <line x1="190" y1="120" x2="205" y2="120" stroke="currentColor" strokeWidth="2" opacity="0.25" strokeLinecap="round" />
      <line x1="190" y1="120" x2="190" y2="105" stroke="currentColor" strokeWidth="2" opacity="0.25" strokeLinecap="round" />
    </svg>
  ),
  overview: (
    <svg width="180" height="120" viewBox="0 0 240 160" fill="none" xmlns="http://www.w3.org/2000/svg">
      {[0, 1, 2, 3].map((i) => (
        <rect key={i} x={20 + i * 55} y="40" width="45" height="55" rx="6" stroke="currentColor" strokeWidth="1.5" opacity="0.2" />
      ))}
      <polyline points="25,110 70,95 115,100 160,85 205,70" fill="none" stroke="#0ea5e9" strokeWidth="2" strokeLinecap="round" opacity="0.4" />
      <polyline points="25,110 70,95 115,100 160,85 205,70 205,130 25,130" fill="#0ea5e9" opacity="0.05" />
    </svg>
  ),
  search: (
    <svg width="180" height="120" viewBox="0 0 240 160" fill="none" xmlns="http://www.w3.org/2000/svg">
      <circle cx="105" cy="65" r="28" stroke="currentColor" strokeWidth="3" opacity="0.25" fill="none" />
      <line x1="125" y1="85" x2="145" y2="105" stroke="currentColor" strokeWidth="3" opacity="0.25" strokeLinecap="round" />
      <text x="105" y="72" textAnchor="middle" fontSize="28" fontWeight="bold" fill="currentColor" opacity="0.15">?</text>
    </svg>
  ),
};

const VARIANT_TITLE: Partial<Record<EmptyStateVariant, { title: TranslationKey; desc: TranslationKey; cta: TranslationKey }>> = {
  month: {
    title: 'calendar.shell.chips.empty.month.title',
    desc: 'calendar.shell.chips.empty.month.desc',
    cta: 'calendar.shell.chips.empty.month.cta',
  },
  week: {
    title: 'calendar.shell.chips.empty.week.title',
    desc: 'calendar.shell.chips.empty.week.desc',
    cta: 'calendar.shell.chips.empty.week.cta',
  },
  day: {
    title: 'calendar.shell.chips.empty.day.title',
    desc: 'calendar.shell.chips.empty.day.desc',
    cta: 'calendar.shell.chips.empty.day.cta',
  },
  list: {
    title: 'calendar.shell.chips.empty.list.title',
    desc: 'calendar.shell.chips.empty.list.desc',
    cta: 'calendar.shell.chips.empty.newEventCta',
  },
  overview: {
    title: 'calendar.shell.chips.empty.overview.title',
    desc: 'calendar.shell.chips.empty.overview.desc',
    cta: 'calendar.shell.chips.empty.overview.cta',
  },
  search: {
    title: 'calendar.shell.chips.empty.search.title',
    desc: 'calendar.shell.chips.empty.search.desc',
    cta: 'calendar.shell.chips.empty.search.cta',
  },
};

function EmptyStateImpl({ variant, icon, title, description, action, keyword, onPrimary }: EmptyStateProps) {
  const useVariant = variant && VARIANT_SVG[variant];
  const defaults = variant ? VARIANT_TITLE[variant] : undefined;
  const finalTitle = defaults ? tBound(defaults.title) : title;
  const finalDesc = defaults ? tBound(defaults.desc) : description;
  const finalCta = defaults ? tBound(defaults.cta) : tBound('calendar.shell.chips.empty.newEventCta');
  const isSearch = variant === 'search';

  return (
    <div className="flex flex-col items-center justify-center py-16 px-4 text-center select-none rounded-xl border border-dashed border-slate-200 dark:border-slate-800 bg-gradient-to-b from-transparent to-slate-50/50 dark:to-slate-900/30">
      {useVariant ? (
        <div className="mb-4 text-slate-400 dark:text-slate-600" style={{ animation: 'cal-fade-in-up 180ms cubic-bezier(0.2,0,0,1) forwards' }}>
          {VARIANT_SVG[variant!]}
        </div>
      ) : icon ? (
        <div className="mb-4 text-slate-300 dark:text-slate-600">{icon}</div>
      ) : (
        <div className="mb-4 text-slate-300 dark:text-slate-600">
          <svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
            <rect x="3" y="4" width="18" height="18" rx="2" ry="2" />
            <line x1="16" y1="2" x2="16" y2="6" />
            <line x1="8" y1="2" x2="8" y2="6" />
            <line x1="3" y1="10" x2="21" y2="10" />
          </svg>
        </div>
      )}
      <h3 className="text-lg font-medium text-slate-500 dark:text-slate-400">{finalTitle}</h3>
      {finalDesc && <p className="mt-1.5 text-sm text-slate-400 dark:text-slate-500 max-w-xs">{finalDesc}</p>}
      {action && <div className="mt-4">{action}</div>}
      {!action && onPrimary && (
        <button
          type="button"
          onClick={onPrimary}
          className="mt-4 px-4 py-2 text-sm font-medium text-white bg-gradient-to-r from-sky-500 to-blue-600 hover:from-sky-600 hover:to-blue-700 rounded-lg shadow-md shadow-sky-500/25 transition-all"
        >
          {finalCta}
        </button>
      )}
      {isSearch && keyword && (
        <p className="mt-2 text-xs text-slate-400 dark:text-slate-500">
          {tChip('calendar.shell.chips.searchCurrent', { keyword })}
        </p>
      )}
    </div>
  );
}

export const EmptyState = memo(EmptyStateImpl, (a, b) =>
  a.variant === b.variant &&
  a.title === b.title &&
  a.description === b.description &&
  a.icon === b.icon &&
  a.action === b.action &&
  a.keyword === b.keyword &&
  a.onPrimary === b.onPrimary
);
EmptyState.displayName = 'EmptyState';
