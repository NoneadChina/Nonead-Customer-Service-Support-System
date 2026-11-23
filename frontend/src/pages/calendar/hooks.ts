import { useEffect, useMemo, useRef, useCallback } from 'react';
import { useCalendarStore } from './store';
import type { CalendarStore, MailboxDto } from './store';
import { toYMDKey } from './calendarUtils';
import type { ViewMode } from './calendarUtils';
import { tBound } from '../../i18n/locale';
import type { CalendarEventQuery, CalendarEventDto } from '../../api/types';
import { calendarApi } from '../../api/modules/engagement';
import { mailboxApi } from '../../api/modules/core';

/* =========================================================
   Pure helpers（导出给单测的纯函数）
   ========================================================= */

/** shallow 浅对比：用于 store selector 比较，减少无意义 rerender（P2P3 §3.4 #9 渲染性能）。
 *  统一入口：视图层的本地 shallow 函数均删除，统一从此处导入，避免 5 份维护。
 *  使用 Object.is + hasOwnProperty，语义与 EditorPanel 原本地实现保持一致。 */
export function shallowEqual<T>(a: T, b: T): boolean {
  if (Object.is(a, b)) return true;
  if (typeof a !== 'object' || typeof b !== 'object' || a == null || b == null) return false;
  const keysA = Object.keys(a as Record<string, unknown>) as (keyof T)[];
  const keysB = Object.keys(b as Record<string, unknown>) as (keyof T)[];
  if (keysA.length !== keysB.length) return false;
  for (const k of keysA) {
    if (!Object.prototype.hasOwnProperty.call(b as object, k as string | number | symbol)) return false;
    if (!Object.is((a as Record<string, unknown>)[k as string], (b as Record<string, unknown>)[k as string])) return false;
  }
  return true;
}

/** 构建 ±14 天窗口（若无显式 startDate/endDate 时使用） */
export function _buildRangeWindow(q: CalendarEventQuery, targetDate: Date): { start: string; end: string } {
  if (q.startDate && q.endDate) return { start: q.startDate, end: q.endDate };
  const d = (offset: number) => {
    const t = new Date(targetDate);
    t.setDate(t.getDate() + offset);
    return toYMDKey(t);
  };
  return { start: d(-14), end: d(14) };
}

export function _isRangeCovered(input: {
  cacheStart?: string; cacheEnd?: string; reqStart: string; reqEnd: string;
}): boolean {
  if (!input || !input.cacheStart || !input.cacheEnd) return false;
  return input.reqStart >= input.cacheStart && input.reqEnd <= input.cacheEnd;
}

/** Debounce 策略纯函数（返回 {shouldFire, scheduleNext, delayMs, newState} 让调用方执行） */
interface DebounceState { key: string; lastFireAt: number }
export function _debounceStore(
  prev: DebounceState | undefined, key: string, nowMs: number, windowMs = 120,
): { shouldFire: boolean; scheduleNext: boolean; delayMs: number; newState: DebounceState } {
  if (!prev || prev.key !== key) return { shouldFire: true, scheduleNext: false, delayMs: 0, newState: { key, lastFireAt: nowMs } };
  const since = nowMs - prev.lastFireAt;
  if (since >= windowMs) return { shouldFire: true, scheduleNext: false, delayMs: 0, newState: { key, lastFireAt: nowMs } };
  return { shouldFire: false, scheduleNext: true, delayMs: windowMs - since, newState: prev };
}

export function _normalizeDirection(dir: 'noneads_to_outlook' | 'outlook_to_noneads' | 'bidirectional'): 'both' | 'to_outlook' | 'from_outlook' {
  if (dir === 'bidirectional') return 'both';
  if (dir === 'noneads_to_outlook') return 'to_outlook';
  if (dir === 'outlook_to_noneads') return 'from_outlook';
  return 'both';
}

export type KeyComboResult = 'submit' | 'dismiss' | 'next' | 'prev' | 'create' | 'tabNext' | 'tabPrev' | 'undo' | 'redo' | null;

const _EDITABLE_TAGS = new Set(['INPUT', 'TEXTAREA', 'SELECT']);
function _isEditable(target: EventTarget | null): boolean {
  if (!target) return false;
  const t = target as HTMLElement;
  const tag = t.tagName;
  if (_EDITABLE_TAGS.has(tag)) return true;
  if (t.isContentEditable) return true;
  return false;
}

export function _handleKeyCombo(e: KeyboardEvent | Pick<KeyboardEvent, 'key' | 'target' | 'ctrlKey' | 'metaKey' | 'shiftKey'>): KeyComboResult {
  const k = e.key;
  const isMod = (e as KeyboardEvent).ctrlKey || (e as KeyboardEvent).metaKey;
  if (_isEditable(e.target)) {
    // 只有 Escape 可以中断编辑态
    if (k === 'Escape') return 'dismiss';
    return null;
  }
  if (isMod && k === 'z') {
    if ((e as KeyboardEvent).shiftKey) return 'redo';
    return 'undo';
  }
  if (isMod && k === 'y') return 'redo';
  if (k === 'Enter') return 'submit';
  if (k === 'Escape') return 'dismiss';
  if (isMod && k === 'n') return 'create';
  if (k === 'Tab') {
    if (e.shiftKey) return 'tabPrev';
    return 'tabNext';
  }
  if (k === 'ArrowRight') return 'next';
  if (k === 'ArrowLeft') return 'prev';
  return null;
}

/* =========================================================
   冲突检测纯函数（P1-1）
   ========================================================= */

/** 冲突级别：none=无冲突 / soft=边缘重叠(≤15min) / hard=严重重叠(>15min) */
export type ConflictTag = 'none' | 'soft' | 'hard';

export interface ConflictCheckInput {
  /** 待检测的事件 startAt（ISO string 或 Date） */
  startAt: string | Date;
  /** 待检测的事件 endAt（ISO string / Date / null / undefined，null / undefined 视为缺失，默认 +1h） */
  endAt?: string | Date | null;
  /** 同日已有事件列表（已按 startAt 升序）。endAt 允许 null 以匹配 API DTO。 */
  existingEvents: { id: number; startAt: string | Date; endAt?: string | Date | null; isAllDay?: boolean }[];
  /** 当前事件的 id（排除自身） */
  excludeId?: number;
}

export interface ConflictCheckResult {
  tag: ConflictTag;
  /** 冲突的事件 id 列表 */
  conflictIds: number[];
  /** 冲突总分钟数（最大单一事件重叠分钟） */
  overlapMinutes: number;
  /** 冲突事件摘要（用于 tooltip 显示） */
  conflictWith: { title: string; startAt: string; endAt?: string | null }[];
}

/** 柔冲突阈值：≤15 分钟的边缘重叠算 soft */
const SOFT_THRESHOLD_MIN = 15;

/**
 * 检测一个时间范围与已有事件列表的冲突情况。
 *
 * 算法：
 * 1. 将 startAt/endAt 转为时间戳（endAt 缺失时默认 +1h）
 * 2. 过滤掉 excludeId 和全天事件（全天事件不参与时间冲突）
 * 3. 遍历 existingEvents，计算时间重叠
 * 4. 重叠 > 0 → 有冲突；重叠 ≤ 15min = soft，> 15min = hard
 * 5. 返回 { tag, conflictIds, overlapMinutes, conflictWith }
 */
export function _checkConflictForRange(input: ConflictCheckInput): ConflictCheckResult {
  const toTs = (v: string | Date) => +new Date(v);
  const startTs = toTs(input.startAt);
  const endTs = input.endAt ? toTs(input.endAt) : startTs + 3600_000; // 默认 1h

  let maxOverlap = 0;
  const conflictIds: number[] = [];
  const conflictWith: { title: string; startAt: string; endAt?: string | null }[] = [];

  for (const ev of input.existingEvents) {
    if (input.excludeId !== undefined && ev.id === input.excludeId) continue;
    if (ev.isAllDay) continue; // 全天事件不参与时间冲突

    const evStart = toTs(ev.startAt);
    const evEnd = ev.endAt ? toTs(ev.endAt) : evStart + 3600_000;

    // 计算重叠
    const overlapStart = Math.max(startTs, evStart);
    const overlapEnd = Math.min(endTs, evEnd);
    const overlapMs = overlapEnd - overlapStart;

    if (overlapMs > 0) {
      const overlapMin = overlapMs / 60_000;
      maxOverlap = Math.max(maxOverlap, overlapMin);
      conflictIds.push(ev.id);
      const startStr = typeof ev.startAt === 'string' ? ev.startAt : ev.startAt.toISOString();
      const endRaw = ev.endAt;
      const endStr = endRaw == null ? null : typeof endRaw === 'string' ? endRaw : endRaw.toISOString();
      conflictWith.push({
        title: ((ev as { title?: unknown }).title as string) || tBound('calendar.common.untitled'),
        startAt: startStr,
        endAt: endStr,
      });
    }
  }

  const tag: ConflictTag = maxOverlap === 0 ? 'none' : maxOverlap <= SOFT_THRESHOLD_MIN ? 'soft' : 'hard';
  return { tag, conflictIds, overlapMinutes: maxOverlap, conflictWith };
}

/* =========================================================
   useBootstrap —— 首次挂载时初始化 mailboxes 与事件模板
   ========================================================= */
export function useBootstrap() {
  useEffect(() => {
    // P1-5: 加载真实邮箱列表（接后端 /api/mailboxes/all）；失败则回退空数组
    (async () => {
      try {
        const [mbs, _] = await Promise.all([
          mailboxApi.listAll(),
          // P1-3: 启动时加载事件模板
          useCalendarStore.getState().fetchTemplates(),
        ]);
        useCalendarStore.setState({ mailboxes: (mbs ?? []) as unknown as MailboxDto[], users: [] });
      } catch (e) {
        console.warn('[calendar] bootstrap 加载失败:', e);
        useCalendarStore.setState({ mailboxes: [], users: [] });
      }
    })();
  }, []);
}

/* =========================================================
   useKeyboardShortcuts —— 挂载全局快捷键
   ========================================================= */
export function useKeyboardShortcuts(opts: { onNext?: () => void; onPrev?: () => void; onCreate?: () => void; onTabNext?: () => void; onTabPrev?: () => void; onSubmit?: () => void; onUndo?: () => void; onRedo?: () => void }) {
  const { onNext, onPrev, onCreate, onTabNext, onTabPrev, onSubmit, onUndo, onRedo } = opts;
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      const r = _handleKeyCombo(e);
      if (!r) return;
      if (r === 'next') { e.preventDefault(); onNext?.(); }
      else if (r === 'prev') { e.preventDefault(); onPrev?.(); }
      else if (r === 'create') { e.preventDefault(); onCreate?.(); }
      else if (r === 'tabNext') { e.preventDefault(); onTabNext?.(); }
      else if (r === 'tabPrev') { e.preventDefault(); onTabPrev?.(); }
      else if (r === 'submit') { e.preventDefault(); onSubmit?.(); }
      else if (r === 'undo') { e.preventDefault(); onUndo?.(); }
      else if (r === 'redo') { e.preventDefault(); onRedo?.(); }
      // dismiss 由具体面板各自处理（避免全局冲突）
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [onNext, onPrev, onCreate, onTabNext, onTabPrev, onSubmit, onUndo, onRedo]);
}

/* =========================================================
   useRangeCache —— 范围缓存 + 120ms debounce 刷新调度
   ========================================================= */
export interface RangeCacheApi {
  cacheState: { cacheStart?: string; cacheEnd?: string };
  invalidateWrite: () => void;
  debounceRefresh: (key: string) => { shouldFire: boolean; scheduleNext: boolean };
}

export function useRangeCache(refresh: () => void): RangeCacheApi {
  const cacheRef = useRef<{ start?: string; end?: string }>({});
  const debRef = useRef<{ key: string; lastFireAt: number } | undefined>();
  const schedulerRef = useRef<number | null>(null);
  // 用 ref 持有最新的 refresh 回调，避免 debounceRefresh 因 [refresh] 依赖
  // 而每次渲染都重新创建（进而导致 Calendar signalRRev effect 每次渲染都跑一遍）。
  const refreshRef = useRef<() => void>(refresh);
  refreshRef.current = refresh;

  const invalidateWrite = useCallback(() => {
    cacheRef.current = {};
  }, []);

  const debounceRefresh = useCallback((key: string) => {
    const decision = _debounceStore(debRef.current, key, Date.now());
    debRef.current = decision.newState;
    if (decision.shouldFire) {
      refreshRef.current();
    } else if (decision.scheduleNext) {
      if (schedulerRef.current != null) window.clearTimeout(schedulerRef.current);
      schedulerRef.current = window.setTimeout(() => {
        schedulerRef.current = null;
        debRef.current = { key, lastFireAt: Date.now() };
        refreshRef.current();
      }, decision.delayMs);
    }
    return { shouldFire: decision.shouldFire, scheduleNext: decision.scheduleNext };
  }, []);

  return {
    cacheState: { cacheStart: cacheRef.current.start, cacheEnd: cacheRef.current.end },
    invalidateWrite,
    debounceRefresh,
  };
}

/* =========================================================
   useRefresh —— 根刷新流程（接入真实 calendarApi）
   ========================================================= */

/** 根据当前 calSubView/calMonth/calCursor 计算日历视图的日期范围。
 *  防御性：所有 Date 字段通过 safeDate 包装，避免 store 中存储非法 Date 时
 *  getFullYear()/getMonth() 直接抛 TypeError → 触发 ErrorBoundary。*/
function _getCalendarRange(): { startDate: string; endDate: string } {
  const raw = useCalendarStore.getState();
  const calSubView = raw.calSubView;
  const calMonth = safeDateFallback(raw.calMonth);
  const calCursor = safeDateFallback(raw.calCursor);
  const pad = (n: number) => String(n).padStart(2, '0');
  const fmt = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

  if (calSubView === 'month') {
    const start = new Date(calMonth.getFullYear(), calMonth.getMonth(), 1);
    start.setDate(start.getDate() - 7);
    const end = new Date(calMonth.getFullYear(), calMonth.getMonth() + 1, 0);
    end.setDate(end.getDate() + 7);
    return { startDate: fmt(start), endDate: fmt(end) };
  } else if (calSubView === 'week') {
    const start = new Date(calCursor);
    start.setDate(start.getDate() - start.getDay());
    const end = new Date(start);
    end.setDate(end.getDate() + 6);
    return { startDate: fmt(start), endDate: fmt(end) };
  } else {
    // calSubView === 'day' —— 不要只拉当天 1 天！
    // 原实现只拉当天，再经 setAllEvents 覆盖 eventsByDate 后，
    // 月/周视图已加载的其他日期（如 8/4-8/8）事件全部被清空 → 日视图仅当天有、相邻天空白。
    // 修复：以 calCursor 为中心前后各扩 45 天（约 1.5 个月），保证：
    //   1) 从月/周视图切到日视图后，原有相邻日期事件不被清空；
    //   2) 日视图按「前一天 / 后一天」翻页时，有充足缓存（45 天），不会频繁超出缓冲而触发重新覆盖；
    //   3) 拉 91 天的数据量对 pageSize=200 完全可控。
    const start = new Date(calCursor);
    start.setDate(start.getDate() - 45);
    const end = new Date(calCursor);
    end.setDate(end.getDate() + 45);
    return { startDate: fmt(start), endDate: fmt(end) };
  }
}

/** 与 calendarUtils.safeDate 等价的本地副本（避免 hooks.ts 引入循环依赖）。
 *  把任何可疑值转成合法 Date，失败时回退到 new Date()（今天）。 */
function safeDateFallback(value: unknown, fallback?: Date): Date {
  if (value instanceof Date && Number.isFinite(value.getTime())) return value;
  if (typeof value === 'number' || typeof value === 'string') {
    const t = new Date(value);
    if (Number.isFinite(t.getTime())) return t;
  }
  const fb = fallback instanceof Date && Number.isFinite(fallback.getTime())
    ? fallback
    : new Date();
  return new Date(fb.getFullYear(), fb.getMonth(), fb.getDate(), fb.getHours(), fb.getMinutes(), fb.getSeconds(), fb.getMilliseconds());
}

export function useRefresh() {
  const lock = useCalendarStore((s) => s.refreshLock);
  const set = useCalendarStore.setState;
  // 用 ref 存 in-flight 请求的「请求序号」，避免并发请求（mount effect + range effect 同时触发）
  // 因为网络后发先至，把新数据覆盖成旧数据。
  const inFlightRevRef = useRef(0);
  const lockRef = useRef(lock);
  lockRef.current = lock;

  const refreshViewMode = useCallback(async (mode: ViewMode, _extra?: unknown) => {
    void _extra;
    // 刷新锁：外部显式 setRefreshing(true) 时跳过本次调用（与其他页面保持一致）
    if (lockRef.current) return { ok: true as const, data: null };
    const store = useCalendarStore.getState();
    set({ loading: true });
    const myRev = ++inFlightRevRef.current;
    try {
      if (mode === 'overview') {
        const [ov, rangeRes] = await Promise.all([
          calendarApi.overview(),
          calendarApi.list({ ..._getCalendarRange(), pageSize: 200, expandRecurring: true }),
        ]);
        if (myRev !== inFlightRevRef.current) return { ok: true as const, data: null };
        const events = rangeRes.data ?? [];
        store.setAllEvents(events);
        set({ overview: ov, loading: false });
      } else if (mode === 'calendar') {
        const range = _getCalendarRange();
        const res = await calendarApi.list({ ...range, pageSize: 200, expandRecurring: true });
        if (myRev !== inFlightRevRef.current) return { ok: true as const, data: null };
        const events = res.data ?? [];
        store.setAllEvents(events);
        set({ loading: false });
      } else if (mode === 'list') {
        const q = store.query;
        const res = await calendarApi.list({
          startDate: q.startDate ?? undefined,
          endDate: q.endDate ?? undefined,
          mailboxId: q.mailboxId ?? undefined,
          userId: q.userId ?? undefined,
          eventType: q.eventType ?? undefined,
          status: q.status ?? undefined,
          keyword: q.keyword ?? undefined,
          page: q.page ?? 1,
          pageSize: q.pageSize ?? 20,
          sortBy: q.sortBy ?? undefined,
          sortDir: q.sortDir ?? undefined,
        });
        if (myRev !== inFlightRevRef.current) return { ok: true as const, data: null };
        set({
          list: {
            data: res.data ?? [],
            totalItems: res.totalItems,
            totalPages: res.totalPages,
            currentPage: res.currentPage,
          },
          loading: false,
        });
      } else if (mode === 'trash') {
        // 垃圾箱视图：通过 store action 加载，保证状态一致性
        await store.loadTrashAsync();
        if (myRev !== inFlightRevRef.current) return { ok: true as const, data: null };
        set({ loading: false });
      } else {
        if (myRev !== inFlightRevRef.current) return { ok: true as const, data: null };
        set({ loading: false });
      }
    } catch (e) {
      if (myRev === inFlightRevRef.current) set({ loading: false });
      console.error('[calendar] refreshViewMode error:', e);
    }
    return { ok: true as const, data: null };
  }, [set]);

  const setRefreshing = useCallback((v: boolean) => set({ refreshLock: v }), [set]);

  // 稳定引用（useMemo 仅用作对象形状的轻量包装，依赖全为稳定值）
  return useMemo(() => ({
    refreshViewMode,
    isRefreshing: lock,
    setRefreshing,
  } as const), [refreshViewMode, lock, setRefreshing]);
}

/* =========================================================
   useCalendarDrag —— 拖拽写操作（跨视图 chip 统一处理）
   ========================================================= */
export function useCalendarDrag() {
  const startDrag = useCalendarStore((s) => s.startDrag);
  const endDrag = useCalendarStore((s) => s.endDrag);
  const setDragOver = useCalendarStore((s) => s.setDragOver);
  const moveEventByDrop = useCalendarStore((s) => s.moveEventByDrop);

  const handleDrop = useCallback(
    async (targetYmd: Parameters<CalendarStore['openSelectedDateEvents']>[0], targetHour?: number) => {
      await moveEventByDrop(targetYmd, targetHour);
      endDrag();
    },
    [moveEventByDrop, endDrag],
  );

  return {
    startDrag,
    endDrag,
    setDragOver,
    handleDrop,
    onChipDragStart: useCallback(
      (ev: CalendarEventDto, e: React.DragEvent<HTMLDivElement>) => {
        // HTML5 DnD 强制要求在 dragstart 中至少 setData 一次，
        // 否则部分浏览器会取消本次拖拽（无 ghost、dragover/drop 不触发），
        // 造成用户感觉"拖了没反应"。三视图 chip 的拖拽统一走这里，一处修复全部生效。
        try {
          const dt = e.dataTransfer;
          if (dt) {
            dt.effectAllowed = 'move';
            dt.setData('text/calendar-event-id', String(ev.id));
            dt.setData('text/plain', String(ev.id));
            // 用被拖 chip 作为 ghost（浏览器默认），视觉上保留原 chip 轮廓；
            // 源 chip 的半透明效果由组件渲染层按 draggedEvent.id 判断（避免 setDragImage 克隆不一致）
          }
        } catch {
          /* ignore — 极端环境下 dataTransfer 不可用 */
        }
        startDrag(ev);
      },
      [startDrag],
    ),
  };
}

/* =========================================================
   useSwipeNavigation —— 移动端左右 swipe 切日期
   ========================================================= */
export interface SwipeOptions {
  threshold?: number;
  maxVertical?: number;
  maxDurationMs?: number;
  onSwipeLeft?: () => void;
  onSwipeRight?: () => void;
  disabled?: boolean;
}

export function useSwipeNavigation<T extends HTMLElement>(opts: SwipeOptions): React.RefCallback<T> {
  const { threshold = 64, maxVertical = 40, maxDurationMs = 800, onSwipeLeft, onSwipeRight, disabled } = opts;
  const ref = useRef<T | null>(null);

  useEffect(() => {
    if (disabled) return;
    const el = ref.current;
    if (!el) return;
    // 桌面浏览器无 touch 事件，直接 no-op
    if (!('ontouchstart' in window)) return;

    let startX = 0, startY = 0, startTime = 0, cancelled = false;

    const onStart = (e: TouchEvent) => {
      const t = e.touches[0];
      startX = t.clientX;
      startY = t.clientY;
      startTime = Date.now();
      cancelled = false;
      // 边缘 24px 禁用（避免系统侧滑返回）
      const vw = window.innerWidth;
      if (startX < 24 || startX > vw - 24) cancelled = true;
    };

    const onMove = (e: TouchEvent) => {
      if (cancelled) return;
      const t = e.touches[0];
      const dy = Math.abs(t.clientY - startY);
      if (dy > maxVertical) cancelled = true;
    };

    const onEnd = (e: TouchEvent) => {
      if (cancelled) return;
      const dt = Date.now() - startTime;
      if (dt > maxDurationMs) return;
      const t = e.changedTouches[0];
      const dx = t.clientX - startX;
      if (Math.abs(dx) < threshold) return;
      if (dx < 0) onSwipeLeft?.();
      else onSwipeRight?.();
    };

    el.addEventListener('touchstart', onStart, { passive: true });
    el.addEventListener('touchmove', onMove, { passive: true });
    el.addEventListener('touchend', onEnd, { passive: true });
    return () => {
      el.removeEventListener('touchstart', onStart);
      el.removeEventListener('touchmove', onMove);
      el.removeEventListener('touchend', onEnd);
    };
  }, [threshold, maxVertical, maxDurationMs, onSwipeLeft, onSwipeRight, disabled]);

  return (el: T | null) => { ref.current = el; };
}
