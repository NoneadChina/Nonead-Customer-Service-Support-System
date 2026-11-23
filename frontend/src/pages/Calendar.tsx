import { useEffect, useMemo, useCallback, useState, useRef } from 'react';
import {
  ChevronLeft,
  ChevronRight,
  Plus,
  CalendarDays,
  LayoutGrid,
  List as ListIcon,
  Trash2,
  Link2,
  CalendarSync,
  Mailbox as MailboxIcon,
  Check,
  XCircle as XIcon,
  ChevronDown,
} from 'lucide-react';
import { useCalendarStore, type MailboxDto } from './calendar/store';
import { useBootstrap, useKeyboardShortcuts, useRangeCache, useRefresh, useSwipeNavigation } from './calendar/hooks';
import type { ViewMode, CalSubView } from './calendar/calendarUtils';
import { safeDate, buildEventsByDate } from './calendar/calendarUtils';
import MonthView from './calendar/views/MonthView';
import WeekView from './calendar/views/WeekView';
import DayView from './calendar/views/DayView';
import ListView from './calendar/views/ListView';
import Overview from './calendar/views/Overview';
import { TrashView } from './calendar/views/TrashView';
import EditorPanel from './calendar/panels/EditorPanel';
import SidePanels from './calendar/panels/SidePanels';
import { TransientToast } from './calendar/panels/shared';
import { Segmented } from './calendar/components/Segmented';
import { signalRService, SignalREvents, type CalendarEventChangedNotification } from '../services/signalR';
import { calendarApi } from '../api/modules/engagement';
import type { CalendarEventDto } from '../api/types';
// T000090：提醒弹窗 + CalendarReminder 监听已迁至全局组件 GlobalCalendarReminderListener
import { Upload as UploadIcon, Download as DownloadIcon, MoreHorizontal, Rss, WifiOff as WifiOffIcon } from 'lucide-react';
import {
  CalendarA11yProvider,
  CalendarErrorBoundary,
  useOnlineStatus,
  useA11yAnnouncer,
} from './calendar/a11yReliability';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';

// TODO: 接入 zustand v5 正式 shallow API 后移除本内联实现
function shallow<T>(a: T, b: T): boolean {
  if (a === b) return true;
  if (typeof a !== 'object' || typeof b !== 'object' || a == null || b == null) return false;
  const ka = Object.keys(a as Record<string, unknown>);
  const kb = Object.keys(b as Record<string, unknown>);
  if (ka.length !== kb.length) return false;
  for (const k of ka) {
    if ((a as Record<string, unknown>)[k] !== (b as Record<string, unknown>)[k]) return false;
  }
  for (const k of kb) {
    if (!(k in (a as Record<string, unknown>))) return false;
  }
  return true;
}

/**
 * 星期缩写（日→六）对应的真源 key，与 `calendar.common.weekdayShort.*` 一一对应。
 * 存 key 而不是字面量：数组是模块级常量，存中文会让门禁红灯，存 `t()` 结果又会把语言
 * 冻结在模块加载那一刻。
 */
const WEEKDAY_KEYS = [
  'calendar.common.weekdayShort.sun',
  'calendar.common.weekdayShort.mon',
  'calendar.common.weekdayShort.tue',
  'calendar.common.weekdayShort.wed',
  'calendar.common.weekdayShort.thu',
  'calendar.common.weekdayShort.fri',
  'calendar.common.weekdayShort.sat',
] as const satisfies readonly TranslationKey[];

const VIEW_MODES: { key: ViewMode; labelKey: TranslationKey; Icon?: typeof Plus }[] = [
  { key: 'overview', labelKey: 'calendar.common.overview', Icon: CalendarDays },
  { key: 'calendar', labelKey: 'calendar.shell.page.viewCalendar', Icon: LayoutGrid },
  { key: 'list', labelKey: 'calendar.shell.page.viewList', Icon: ListIcon },
  { key: 'trash', labelKey: 'calendar.common.trash', Icon: Trash2 },
  { key: 'subscription', labelKey: 'calendar.shell.page.viewSubscription', Icon: Link2 },
  { key: 'outlook', labelKey: 'calendar.common.outlookSync', Icon: CalendarSync },
];

const CAL_SUB_TABS: CalSubView[] = ['month', 'week', 'day'];

/* P2-B#2: 屏幕阅读器实时公告组件（内部订阅 store，自动朗读状态变更）
   依赖 CalendarA11yProvider，必须写在 Provider 包裹的 JSX 内部。 */
function CalendarLiveAnnouncer() {
  const announce = useA11yAnnouncer();
  const { t } = useI18n();
  useEffect(() => {
    let lastSelectedSize = 0;
    let lastConflictCount = 0;
    let lastUndoMsg = '';
    const unsub = useCalendarStore.subscribe((state) => {
      const selSize = state.selectedEventIds.size;
      if (selSize !== lastSelectedSize) {
        lastSelectedSize = selSize;
        if (selSize === 0) announce(t('calendar.shell.page.announceCleared'));
        else if (selSize === 1) announce(t('calendar.shell.page.announceSelectedOne'));
        else announce(t('calendar.shell.page.announceSelectedMany', { count: selSize }));
      }
      // 冲突计数从概览 KPI 中取（Overview panel 用 stats.conflicts）
      const overviewConflicts = state.overview?.stats.conflicts ?? 0;
      if (overviewConflicts !== lastConflictCount) {
        lastConflictCount = overviewConflicts;
        if (overviewConflicts === 0) announce(t('calendar.shell.page.announceNoConflict'));
        else if (overviewConflicts === 1) announce(t('calendar.shell.page.announceConflictOne'));
        else announce(t('calendar.shell.page.announceConflictMany', { count: overviewConflicts }));
      }
      const um = state.undoToastMessage || '';
      if (um !== lastUndoMsg) {
        lastUndoMsg = um;
        if (um) announce(um);
      }
    });
    return unsub;
  }, [announce, t]);
  return null;
}

export default function Calendar() {
  const { t } = useI18n();
  useBootstrap();
  const [animCounter, setAnimCounter] = useState(0);
  // P2-A#6：日历更多菜单（导入/导出 ICS）
  const [moreOpen, setMoreOpen] = useState(false);
  const moreAnchorRef = useRef<HTMLDivElement>(null);
  const icsFileInputRef = useRef<HTMLInputElement>(null);
  const [icsImporting, setIcsImporting] = useState(false);
  const [icsExportingRange, setIcsExportingRange] = useState(false);
  const { isRefreshing, setRefreshing, refreshViewMode } = useRefresh();
  void setRefreshing;
  const { debounceRefresh, cacheState, invalidateWrite } = useRangeCache(() => {
    void refreshViewMode(useCalendarStore.getState().viewMode);
  });
  void cacheState; void invalidateWrite;

  // P2-C#2: 在线/离线检测 —— 断网时顶部横幅 + 写操作阻断断言
  const { isOnline, OfflineBanner, assertOnlineForWrite } = useOnlineStatus();

  // P2-C#3: SignalR 连接状态横幅（自动重连策略在 signalR.ts 中已配置 0/2/4/6/8s 指数退避）
  const [srState, setSrState] = useState<'connected' | 'disconnected' | 'reconnecting'>('connected');
  useEffect(() => {
    const handler = (s: unknown) => setSrState(s as typeof srState);
    signalRService.on('_stateChange', handler);
    // 同步已初始化后的状态
    try {
      const cur = signalRService.getState();
      if (cur === 'connected' || cur === 'disconnected' || cur === 'reconnecting') setSrState(cur);
    } catch { /* noop */ }
    return () => { try { signalRService.off('_stateChange', handler); } catch { /* noop */ } };
  }, []);
  const SignalRBanner = () => {
    if (srState === 'connected') return null;
    const isReconnecting = srState === 'reconnecting';
    return (
      <div
        role="status"
        aria-live={isReconnecting ? 'polite' : 'assertive'}
        className="sticky top-0 z-[60] w-full text-sm font-medium py-2 px-4 flex items-center justify-center gap-2 text-white shadow-md"
        style={{ background: isReconnecting ? 'linear-gradient(to right, rgb(14 165 233), rgb(59 130 246))' : 'linear-gradient(to right, rgb(100 116 139), rgb(71 85 105))' }}
      >
        <WifiOffIcon className="w-4 h-4" />
        <span>
          {isReconnecting
            ? t('calendar.shell.page.signalRReconnecting')
            : t('calendar.shell.page.signalROffline')}
        </span>
      </div>
    );
  };

  // 存储 L143-146 打开 trash/subscription/outlook 后 setViewMode('calendar') 的定时器 id，
  // 防止组件在 setTimeout 触发前就卸载了，导致"已卸载组件后续副作用"或残留定时器。
  const viewModeResetTimerRef = useRef<number | null>(null);

  useKeyboardShortcuts({
    onPrev: () => { useCalendarStore.getState().prevRange(); setAnimCounter((c) => c + 1); },
    onNext: () => { useCalendarStore.getState().nextRange(); setAnimCounter((c) => c + 1); },
    onCreate: () => useCalendarStore.getState().openCreate(),
    onTabNext: () => { useCalendarStore.getState().focusNextEvent(1); },
    onTabPrev: () => { useCalendarStore.getState().focusNextEvent(-1); },
    onSubmit: () => {
      const { focusedEventId, allEvents, openEdit } = useCalendarStore.getState();
      if (focusedEventId !== null) {
        const ev = allEvents.find(e => e.id === focusedEventId);
        if (ev) openEdit(ev);
      }
    },
    onUndo: () => { void useCalendarStore.getState().undo(); },
    onRedo: () => { void useCalendarStore.getState().redo(); },
  });

  // T000090：全局提醒弹窗"查看详情"跳转带来的 ?focusEventId=N 解析。
  // 当 allEvents 加载完成且包含对应事件时自动 openEdit，完成后清除 URL 参数。
  const allEventsCount = useCalendarStore((s) => s.allEvents.length);
  useEffect(() => {
    if (typeof window === 'undefined' || !window.location?.search) return;
    const params = new URLSearchParams(window.location.search);
    const v = params.get('focusEventId');
    if (!v) return;
    const id = Number(v);
    if (!Number.isFinite(id)) return;
    const ev = useCalendarStore.getState().allEvents.find((e) => e.id === id);
    if (ev) {
      useCalendarStore.getState().openEdit(ev);
      try {
        const url = new URL(window.location.href);
        url.searchParams.delete('focusEventId');
        window.history.replaceState(null, '', url.toString());
      } catch { /* noop */ }
    }
  }, [allEventsCount]);

  // 第三梯队: 键盘导航焦点变化时滚动到对应事件
  const focusedEventId = useCalendarStore((s) => s.focusedEventId);
  useEffect(() => {
    if (focusedEventId === null) return;
    const el = document.querySelector(`[data-event-id="${focusedEventId}"]`) as HTMLElement | null;
    el?.scrollIntoView({ behavior: 'smooth', block: 'center' });
  }, [focusedEventId]);

  const { viewMode, calSubView, signalRRev, loading, calMonth, calCursor } = useCalendarStore(
    (s) => ({ viewMode: s.viewMode, calSubView: s.calSubView, signalRRev: s.signalRRev, loading: s.loading, calMonth: s.calMonth, calCursor: s.calCursor }),
    shallow,
  );
  const prevRange = useCalendarStore((s) => s.prevRange);
  const nextRange = useCalendarStore((s) => s.nextRange);
  const gotoToday = useCalendarStore((s) => s.gotoToday);
  const openCreate = useCalendarStore((s) => s.openCreate);
  const setCalSubView = useCalendarStore((s) => s.setCalSubView);
  const setViewMode = useCalendarStore((s) => s.setViewMode);
  const showUndoToast = useCalendarStore((s) => s.showUndoToast);
  const undoToastMessage = useCalendarStore((s) => s.undoToastMessage);
  const transientToast = useCalendarStore((s) => s.transientToast);
  const dismissTransientToast = useCalendarStore((s) => s.dismissTransientToast);
  const refreshTrashCount = useCalendarStore((s) => s.refreshTrashCount);
  const listSortBy = useCalendarStore((s) => s.query.sortBy);
  const listSortDir = useCalendarStore((s) => s.query.sortDir);
  const listStartDate = useCalendarStore((s) => s.query.startDate);
  const listEndDate = useCalendarStore((s) => s.query.endDate);
  const listEventType = useCalendarStore((s) => s.query.eventType);
  const listStatus = useCalendarStore((s) => s.query.status);
  const listKeyword = useCalendarStore((s) => s.query.keyword);
  const listPage = useCalendarStore((s) => s.query.page);
  const listPageSize = useCalendarStore((s) => s.query.pageSize);
  const trashCount = useCalendarStore((s) => s.trashCount);

  // keyword 防抖：搜索输入通常逐字符变化，300ms 防抖避免每打一个字就请求 API
  const [debouncedKeyword, setDebouncedKeyword] = useState<string | null | undefined>(listKeyword);
  useEffect(() => {
    const t = window.setTimeout(() => setDebouncedKeyword(listKeyword), 300);
    return () => window.clearTimeout(t);
  }, [listKeyword]);

  const onPrev = useCallback(() => { prevRange(); setAnimCounter((c) => c + 1); }, [prevRange]);
  const onNext = useCallback(() => { nextRange(); setAnimCounter((c) => c + 1); }, [nextRange]);

  const onSwipeLeft = useCallback(() => {
    onNext();
  }, [onNext]);

  const onSwipeRight = useCallback(() => {
    onPrev();
  }, [onPrev]);

  const swipeRef = useSwipeNavigation<HTMLDivElement>({
    onSwipeLeft,
    onSwipeRight,
    disabled: viewMode !== 'calendar',
  });
  const onToday = useCallback(() => gotoToday(), [gotoToday]);
  const onCreateNew = useCallback(() => {
    if (!assertOnlineForWrite()) return;
    openCreate();
  }, [assertOnlineForWrite, openCreate]);
  const onSubViewChange = useCallback((tab: CalSubView) => setCalSubView(tab), [setCalSubView]);
  const onViewModeChange = useCallback((mode: ViewMode) => setViewMode(mode), [setViewMode]);

  const calNavLabel = useMemo(() => {
    const m = safeDate(calMonth);
    const c = safeDate(calCursor);
    if (calSubView === 'month') return t('calendar.shell.page.navMonth', { year: m.getFullYear(), month: m.getMonth() + 1 });
    if (calSubView === 'week') {
      const ws = new Date(c.getFullYear(), c.getMonth(), c.getDate());
      ws.setDate(ws.getDate() - ws.getDay());
      const we = new Date(ws); we.setDate(ws.getDate() + 6);
      return t('calendar.shell.page.navWeek', {
        startYear: ws.getFullYear(),
        startMonth: ws.getMonth() + 1,
        startDay: ws.getDate(),
        endMonth: we.getMonth() + 1,
        endDay: we.getDate(),
      });
    }
    return t('calendar.shell.page.navDay', {
      year: c.getFullYear(),
      month: c.getMonth() + 1,
      day: c.getDate(),
      weekday: t(WEEKDAY_KEYS[c.getDay()]),
    });
  }, [calSubView, calMonth, calCursor, t]);

  // ===== 挂载时重置到当前月 + 强制首屏加载 =====
  // T000120: 需求为"日历默认显示当前日期的月份（Outlook 行为）"。
  // zustand store 是全局单例，SPA 侧边栏跳转（NavLink to /calendar）或 F5 刷新时，
  // store 内保留的 calMonth/calCursor 都是上次浏览的月份；这里无论何种方式进入，
  // 都强制 gotoToday() 回到当前月份，保证日历每次进入都默认显示当前月。
  useEffect(() => {
    const store = useCalendarStore.getState();
    if (store.viewMode === 'calendar' || store.viewMode === 'overview') {
      store.gotoToday();
    }
    // 无论什么视图，挂载后都无条件触发一次真实 API 拉取，保证首屏一定有数据
    void refreshViewMode(useCalendarStore.getState().viewMode);
    // 垃圾箱红点徽标：挂载 + SignalR 变化时刷新一次（删除/恢复等操作会在对应 store action 内同步 trashCount）
    void refreshTrashCount();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (signalRRev > 0) debounceRefresh(`signalR-${signalRRev}`);
  }, [signalRRev, debounceRefresh]);

  // P1-C#4: 日历页面挂载期间，捕获未处理的 JS 异常 & Promise rejection，用 toast 做温和提示
  // （最外层还有 ErrorBoundary 兜底整页崩溃，这里负责"抛错但没崩"时的用户体感。）
  useEffect(() => {
    const onUncaughtError = (ev: ErrorEvent) => {
      const msg = (ev.error?.message as unknown) ?? ev.message ?? t('calendar.shell.page.unexpectedError');
      const safeMsg = typeof msg === 'string' ? msg : String(msg).slice(0, 120);
      useCalendarStore.getState().showTransientToast(t('calendar.shell.page.uncaughtErrorToast', { message: safeMsg }), 'error', 3600);
      // 仍走浏览器默认打印到 console，方便排障（不 preventDefault）
    };
    const onUnhandledRejection = (ev: PromiseRejectionEvent) => {
      let msg: string;
      try {
        if (ev.reason == null) msg = t('calendar.shell.page.asyncTaskFailed');
        else if (typeof ev.reason === 'string') msg = ev.reason;
        else if (typeof (ev.reason as { message?: unknown }).message === 'string') msg = (ev.reason as { message: string }).message;
        else if (typeof (ev.reason as { response?: { data?: { message?: unknown } } }).response?.data?.message === 'string') {
          msg = (ev.reason as { response: { data: { message: string } } }).response.data.message;
        } else msg = String(ev.reason).slice(0, 120);
      } catch { msg = t('calendar.shell.page.asyncOperationFailed'); }
      useCalendarStore.getState().showTransientToast(t('calendar.shell.page.operationFailed', { message: msg }), 'error', 3200);
    };
    window.addEventListener('error', onUncaughtError);
    window.addEventListener('unhandledrejection', onUnhandledRejection);
    return () => {
      window.removeEventListener('error', onUncaughtError);
      window.removeEventListener('unhandledrejection', onUnhandledRejection);
    };
  }, [t]);

  // T000090：实时提醒弹窗与监听已迁至全局组件 GlobalCalendarReminderListener，
  // 见 frontend/src/components/GlobalCalendarReminderListener.tsx。
  // 任意页面（不仅 /calendar）都能收到 SignalR CalendarReminder 事件并展示弹窗。

  // ===== SignalR 实时同步：CalendarEventChanged / CalendarRefresh =====
  // 收到事件变更推送时，合并到本地 allEvents（跳过乐观保护期内的事件）。
  // P3: 多端并发门控
  //  - 乐观保护期内（5s）跳过推送覆盖，防止与本地刚做的修改冲突
  //  - 推送 rowVersion <= 本地当前 rowVersion 则丢弃，避免乱序/重复推送把“刚更新到最新”的事件覆盖回旧版本
  //    （SQL Server RowVersion 是 8 字节大端自增，base64 解码后按字节字典序就是单调序）
  useEffect(() => {
    const cmpRowVersion = (a: string | null | undefined, b: string | null | undefined): number => {
      // null/空串视为最旧
      if (!a && !b) return 0;
      if (!a) return -1;
      if (!b) return 1;
      let ab: Uint8Array, bb: Uint8Array;
      try { ab = Uint8Array.from(atob(a), (c) => c.charCodeAt(0)); } catch { ab = new Uint8Array(0); }
      try { bb = Uint8Array.from(atob(b), (c) => c.charCodeAt(0)); } catch { bb = new Uint8Array(0); }
      if (ab.length !== bb.length) return ab.length - bb.length;
      for (let i = 0; i < ab.length; i++) {
        if (ab[i] !== bb[i]) return ab[i] - bb[i];
      }
      return 0;
    };
    const onChanged = (raw: unknown) => {
      const payload = raw as CalendarEventChangedNotification;
      if (!payload || typeof payload.eventId !== 'number') return;
      const store = useCalendarStore.getState();
      if (payload.changeType === 'delete') {
        // 删除事件：直接从 allEvents state 移除（不调用 setAllEvents，因为后者是增量合并模式，不会移除 prevArr 中有但 incomingArr 中没有的事件）
        const prev = store.allEvents;
        const next = prev.filter((e) => e.id !== payload.eventId);
        // P2-8: 同步清理 list 和 overview.upcomingEvents，避免其他视图残留已删除事件
        const patch: Partial<{ allEvents: typeof next; eventsByDate: ReturnType<typeof buildEventsByDate>; list: typeof store.list; overview: typeof store.overview }> = {};
        let changed = false;
        if (next.length !== prev.length) {
          patch.allEvents = next;
          patch.eventsByDate = buildEventsByDate(next);
          changed = true;
        }
        if (store.list && Array.isArray(store.list.data)) {
          const newListData = store.list.data.filter((e) => e.id !== payload.eventId);
          if (newListData.length !== store.list.data.length) {
            patch.list = { ...store.list, data: newListData, totalItems: Math.max(0, store.list.totalItems - (store.list.data.length - newListData.length)) };
            changed = true;
          }
        }
        if (store.overview && Array.isArray(store.overview.upcomingEvents)) {
          const newUpcoming = store.overview.upcomingEvents.filter((e) => e.id !== payload.eventId);
          if (newUpcoming.length !== store.overview.upcomingEvents.length) {
            patch.overview = { ...store.overview, upcomingEvents: newUpcoming };
            changed = true;
          }
        }
        // P2-3: 清理乐观标记，防止乐观保护期内新建的同 ID 事件被错误保留
        store._unmarkOptimistic(payload.eventId);
        if (changed) useCalendarStore.setState(patch);
        return;
      }
      // 乐观保护期内（5s）跳过推送覆盖，防止与本地刚做的修改冲突
      if (store._optimisticMarkers.has(payload.eventId)) return;
      {
        // 创建/更新事件：合并到 allEvents（先做 rowVersion 门控）
        const dto = payload.data as CalendarEventDto | null;
        if (dto && typeof dto.id === 'number') {
          const localInstances = store.allEvents.filter((e) => e.id === dto.id);
          if (localInstances.length > 0) {
            const local = localInstances[0];
            const cmp = cmpRowVersion(dto.rowVersion, local.rowVersion);
            // 推送比本地旧，或与本地完全同版本 → 丢弃，避免乱序/重复推送覆盖新数据
            if (cmp <= 0) return;
            // 关键修复：后端推送的父事件 DTO 不带 recurrenceOriginalStart（null）。
            // 如果本地有多个相同 id 的展开实例（重复日程），直接 upsert 只会替换其中一个，
            // 其余实例残留导致色块重复/残留。此时应触发全量刷新，由 setAllEvents 统一处理。
            if (!dto.recurrenceOriginalStart && localInstances.some((e) => e.recurrenceOriginalStart)) {
              useCalendarStore.setState((s) => ({ signalRRev: s.signalRRev + 1 }));
              return;
            }
            // P2-2: 移除死代码——若 local.recurrenceOriginalStart 为真，
            //   则上方 some() 必为 true 已 return；若为假则本分支条件不满足。
          }
          store.optimisticUpsertEvent(dto);
        }
      }
    };
    const onRefresh = (_raw: unknown) => {
      // 其他用户触发的大范围变更（如批量操作），直接刷新
      useCalendarStore.setState((s) => ({ signalRRev: s.signalRRev + 1 }));
    };
    // P1-2: SignalR 重连成功后触发全量刷新，避免重连期间错过的推送消息丢失
    const onReconnected = () => {
      useCalendarStore.setState((s) => ({ signalRRev: s.signalRRev + 1 }));
    };
    signalRService.on(SignalREvents.CalendarEventChanged, onChanged);
    signalRService.on(SignalREvents.CalendarRefresh, onRefresh);
    signalRService.on('_reconnected', onReconnected);
    // P2-1: 页面从隐藏切换到可见时，若 SignalR 已断开则尝试重连，并触发全量刷新
    // 解决长时间挂起/切后台后 SignalR 永久断开、数据不更新的问题
    const onVisibilityChange = () => {
      if (document.visibilityState !== 'visible') return;
      // 若连接已断开（非 connected/reconnecting），尝试重连；
      // 重连成功后由 P1-2 的 onReconnected 回调统一触发 signalRRev + 1 全量刷新
      if (!signalRService.isConnected() && signalRService.getState() !== 'reconnecting') {
        void signalRService.connect().catch(() => { /* 静默失败，下次可见时再试 */ });
      }
    };
    document.addEventListener('visibilitychange', onVisibilityChange);
    return () => {
      signalRService.off(SignalREvents.CalendarEventChanged, onChanged);
      signalRService.off(SignalREvents.CalendarRefresh, onRefresh);
      signalRService.off('_reconnected', onReconnected);
      document.removeEventListener('visibilitychange', onVisibilityChange);
    };
  }, []);

  // ===== 关键修复：日期范围变化时必须重新拉取 =====
  // 背景：onPrev / onNext → prevRange/nextRange 只改了 calMonth / calCursor，
  //      但 _getCalendarRange() 完全依赖这几个字段计算 API 的 startDate/endDate；
  //      如果不监听它们，用户切月份 / 切周 / 切天后永远看不到新日期的事件。
  //      viewMode 变化保留原行为（切换 tab 拉对应视图数据 + 信号刷新）。
  useEffect(() => {
    void refreshViewMode(viewMode);
  }, [viewMode, calSubView, calMonth, calCursor, refreshViewMode]);

  // 列表筛选变化时重新查询后端（排序 / 日期 / 类型 / 状态 / 关键字 / 分页）
  // 注意：keyword 用 debouncedKeyword（300ms 防抖）避免每个字符都请求
  useEffect(() => {
    if (viewMode !== 'list') return;
    void refreshViewMode('list');
     
  }, [
    viewMode,
    listSortBy,
    listSortDir,
    listStartDate,
    listEndDate,
    listEventType,
    listStatus,
    debouncedKeyword,
    listPage,
    listPageSize,
    refreshViewMode,
  ]);

  useEffect(() => {
    const get = useCalendarStore.getState;
    if (viewMode === 'subscription') {
      get().openSubscription();
      viewModeResetTimerRef.current = window.setTimeout(() => get().setViewMode('calendar'), 0);
    } else if (viewMode === 'outlook') {
      get().openOutlookSync();
      viewModeResetTimerRef.current = window.setTimeout(() => get().setViewMode('calendar'), 0);
    }
    return () => {
      // viewMode 变化或组件卸载前 → 清理旧定时器，避免"切到 trash 后马上离开"导致残留的定时器乱改状态
      if (viewModeResetTimerRef.current !== null) {
        window.clearTimeout(viewModeResetTimerRef.current);
        viewModeResetTimerRef.current = null;
      }
    };
  }, [viewMode]);

  // P2-A#6：更多菜单（导入/导出 ICS）外部点击关闭
  useEffect(() => {
    if (!moreOpen) return;
    const handler = (e: MouseEvent) => {
      const el = moreAnchorRef.current;
      if (el && !el.contains(e.target as Node)) setMoreOpen(false);
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [moreOpen]);

  // P2-A#6：导入 ICS（上传 .ics 文件；默认当前启用的 mailbox 或第一个邮箱）
  const handleIcsImportClick = useCallback(() => {
    if (!assertOnlineForWrite()) return;
    setMoreOpen(false);
    if (icsFileInputRef.current) icsFileInputRef.current.click();
  }, [assertOnlineForWrite]);
  const handleIcsFileChange = useCallback(async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;
    setIcsImporting(true);
    const snap = useCalendarStore.getState();
    try {
      const enabledIds: Set<number> = snap.enabledMailboxIds;
      const mboxes: MailboxDto[] = snap.mailboxes ?? [];
      const firstEnabledMb = enabledIds.size > 0
        ? Array.from(enabledIds)[0]
        : mboxes[0]?.id ?? undefined;
      const res = await calendarApi.importEvents(file, firstEnabledMb);
      const imported = (res as unknown as { imported?: number })?.imported ?? 0;
      const skipped = (res as unknown as { skipped?: number })?.skipped ?? 0;
      const skippedSuffix = skipped > 0
        ? t('calendar.shell.page.icsImportSkipped', { skipped, count: skipped })
        : '';
      snap.showTransientToast(
        imported > 0
          ? t('calendar.shell.page.icsImportDone', { imported, count: imported }) + skippedSuffix
          : t('calendar.shell.page.icsImportNone') + skippedSuffix,
        imported > 0 ? 'success' : 'info',
        2800,
      );
      if (imported > 0) {
        // P2-A#6：使当前范围缓存失效，下次进入该视图重新拉取
        invalidateWrite();
      }
    } catch (_err) {
      snap.showTransientToast(t('calendar.shell.page.icsImportFailed'), 'error', 3200);
    } finally {
      setIcsImporting(false);
    }
  }, [invalidateWrite, t]);

  // P2-A#6：导出当前可见日期范围 ICS
  const handleIcsExportRange = useCallback(async () => {
    setMoreOpen(false);
    setIcsExportingRange(true);
    const snap = useCalendarStore.getState();
    try {
      const pad = (n: number) => String(n).padStart(2, '0');
      // 按当前视图的可见范围计算起止日期
      let startDate: string;
      let endDate: string;
      if (snap.viewMode === 'overview' || snap.viewMode === 'list' || snap.viewMode === 'trash') {
        const now = new Date();
        startDate = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-01`;
        const end = new Date(now.getFullYear(), now.getMonth() + 3, 0);
        endDate = `${end.getFullYear()}-${pad(end.getMonth() + 1)}-${pad(end.getDate())}`;
      } else if (snap.calSubView === 'month') {
        // 月视图：按当前月前后 7 天网格区间
        const cur = safeDate(snap.calCursor);
        const first = new Date(cur.getFullYear(), cur.getMonth(), 1);
        const startOffset = (first.getDay() + 6) % 7; // 以周一为起点
        const gridStart = new Date(first);
        gridStart.setDate(1 - startOffset);
        const gridEnd = new Date(gridStart);
        gridEnd.setDate(gridStart.getDate() + 41); // 6 周
        startDate = `${gridStart.getFullYear()}-${pad(gridStart.getMonth() + 1)}-${pad(gridStart.getDate())}`;
        endDate = `${gridEnd.getFullYear()}-${pad(gridEnd.getMonth() + 1)}-${pad(gridEnd.getDate())}`;
      } else if (snap.calSubView === 'week') {
        // 周视图：取 calCursor 所在周（周一~周日）
        const cur = safeDate(snap.calCursor);
        const dow = (cur.getDay() + 6) % 7;
        const monday = new Date(cur);
        monday.setDate(cur.getDate() - dow);
        const sunday = new Date(monday);
        sunday.setDate(monday.getDate() + 6);
        startDate = `${monday.getFullYear()}-${pad(monday.getMonth() + 1)}-${pad(monday.getDate())}`;
        endDate = `${sunday.getFullYear()}-${pad(sunday.getMonth() + 1)}-${pad(sunday.getDate())}`;
      } else {
        // 日视图：当天
        const d = safeDate(snap.calCursor);
        startDate = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
        endDate = startDate;
      }
      // mailboxId：启用的单邮箱才限定；否则后端将按角色自行裁剪
      const enabled: Set<number> = snap.enabledMailboxIds;
      let mailboxParam = '';
      if (enabled.size === 1) {
        mailboxParam = `&mailboxId=${Array.from(enabled)[0]}`;
      } else if (enabled.size > 1) {
        mailboxParam = ''; // 多邮箱：导出全部可见（按当前用户权限）
      }
      const url = `/api/calendar/export?startDate=${encodeURIComponent(startDate)}&endDate=${encodeURIComponent(endDate)}${mailboxParam}`;
      const a = document.createElement('a');
      a.href = url;
      a.download = `calendar-${startDate}-${endDate}.ics`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      snap.showTransientToast(t('calendar.shell.page.icsExporting'), 'success', 1800);
    } catch {
      snap.showTransientToast(t('calendar.common.exportIcsFailed'), 'error', 3000);
    } finally {
      setIcsExportingRange(false);
    }
  }, [t]);

  const safeCursor = safeDate(calCursor);
  const viewAnimKey = `${safeCursor.getFullYear()}-${safeCursor.getMonth()+1}-${safeCursor.getDate()}-${animCounter}`;
  const viewAnimClass = "animate-[cal-fade-in-up_180ms_ease-out_both] h-full min-h-0";

  // P1-5: 多邮箱筛选 pill
  const mbState = useCalendarStore((s) => ({
    mailboxes: s.mailboxes,
    enabledMailboxIds: s.enabledMailboxIds,
    toggle: s.toggleMailboxFilter,
    reset: s.setAllMailboxesEnabled,
  }), shallow);
  const [mbOpen, setMbOpen] = useState(false);
  const mbAnchorRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!mbOpen) return;
    const handler = (e: MouseEvent) => {
      const el = mbAnchorRef.current;
      if (el && !el.contains(e.target as Node)) setMbOpen(false);
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [mbOpen]);
  const mbLabel = useMemo((): string => {
    const n = mbState.enabledMailboxIds.size;
    if (n === 0) return t('calendar.shell.page.mailboxAll');
    if (n === 1 && mbState.mailboxes.length > 0) {
      const id = Array.from(mbState.enabledMailboxIds)[0];
      const mb = mbState.mailboxes.find((m) => m.id === id);
      const name = mb ? (mb['name'] ?? mb.email ?? null) : null;
      return (typeof name === 'string' && name) ? name : t('calendar.shell.page.mailboxOne');
    }
    return t('calendar.shell.page.mailboxCount', { count: n });
  }, [mbState.enabledMailboxIds, mbState.mailboxes, t]);

  return (
    <CalendarA11yProvider>
      {/* P2-B#2: 屏幕阅读器实时公告（store 订阅后发出 aria-live 区域文本朗读） */}
      <CalendarLiveAnnouncer />
      <div className="flex flex-col h-full bg-slate-50 dark:bg-slate-950 text-slate-900 dark:text-slate-100">
        {/* P2-C#2 / P2-C#3: 状态横幅 —— 离线 & SignalR 重连 */}
        <OfflineBanner />
        <SignalRBanner />
        <header className="sticky top-0 z-30 backdrop-blur shadow-sm bg-gradient-to-r from-white via-white to-sky-50/30 dark:from-slate-900 dark:via-slate-900 dark:to-slate-800/50 after:absolute after:inset-x-0 after:bottom-0 after:h-px after:bg-gradient-to-r after:from-transparent after:via-sky-200 after:to-transparent dark:after:via-sky-700/40">
        <div className="flex items-center justify-between gap-2 sm:gap-4 px-3 sm:px-6 py-3 flex-wrap">
          <div className="flex items-center gap-2 sm:gap-3">
            <button onClick={onPrev} className="p-1.5 text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700 rounded-lg transition-all hover:shadow-sm hover:scale-105 active:scale-95" aria-label={t('calendar.shell.page.prevPeriod')}>
              <ChevronLeft className="w-5 h-5" />
            </button>
            <button onClick={onToday} className="px-3 py-1.5 text-sm font-medium text-sky-700 dark:text-sky-300 border border-sky-200 dark:border-sky-700/60 bg-gradient-to-b from-sky-50 to-sky-100 dark:from-sky-900/30 dark:to-sky-800/20 rounded-lg hover:shadow-md transition-all">
              {t('calendar.common.today')}
            </button>
            <h2 className="text-base sm:text-lg font-bold min-w-[150px] sm:min-w-[200px] text-center tracking-tight bg-gradient-to-r from-slate-800 to-slate-600 dark:from-slate-100 dark:to-slate-300 bg-clip-text text-transparent">
              {calNavLabel}
            </h2>
            <button onClick={onNext} className="p-1.5 text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700 rounded-lg transition-all hover:shadow-sm hover:scale-105 active:scale-95" aria-label={t('calendar.shell.page.nextPeriod')}>
              <ChevronRight className="w-5 h-5" />
            </button>
          </div>

          <div className="flex items-center gap-2 flex-wrap">
            {viewMode === 'calendar' && (
              <Segmented
                value={calSubView}
                onChange={onSubViewChange}
                size="sm"
                options={CAL_SUB_TABS.map((sub) => ({
                  value: sub,
                  label: sub === 'month' ? t('calendar.shell.page.subViewMonth') : sub === 'week' ? t('calendar.shell.page.subViewWeek') : t('calendar.shell.page.subViewDay'),
                }))}
              />
            )}

            <Segmented
              value={viewMode}
              onChange={onViewModeChange}
              size="md"
              options={VIEW_MODES.map(({ key, labelKey, Icon }) => ({
                value: key,
                label: (
                  <span className="hidden sm:inline">{t(labelKey)}</span>
                ),
                // T000108: 移动端 select 降级时显示纯文本标签
                labelText: t(labelKey),
                icon: key === 'trash'
                  ? (
                    <span className="relative inline-flex items-center">
                      {Icon ? <Icon size={14} /> : null}
                      {trashCount > 0 && (
                        <span
                          className="absolute -top-1 -right-2 inline-flex items-center justify-center rounded-full bg-red-500 text-white"
                          style={{
                            minWidth: '14px',
                            height: '14px',
                            padding: '0 3px',
                            fontSize: '9px',
                            lineHeight: 1,
                            fontWeight: 700,
                          }}
                        >
                          {trashCount > 99 ? '…' : (trashCount > 9 ? '9+' : String(trashCount))}
                        </span>
                      )}
                    </span>
                  )
                  : Icon ? <Icon size={14} /> : undefined,
              }))}
            />

            {/* P1-5: 多邮箱筛选 pill（仅当有可用邮箱时显示） */}
            {mbState.mailboxes.length > 0 && (
              <div ref={mbAnchorRef} className="relative">
                <button
                  type="button"
                  onClick={() => setMbOpen((v) => !v)}
                  className={[
                    'inline-flex items-center gap-1.5 px-2.5 h-9 rounded-lg border text-xs font-medium transition-all',
                    mbState.enabledMailboxIds.size === 0
                      ? 'border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-800'
                      : 'border-sky-300 dark:border-sky-700/60 bg-sky-50 dark:bg-sky-950/40 text-sky-700 dark:text-sky-200 hover:bg-sky-100 dark:hover:bg-sky-900/40 shadow-sm',
                  ].join(' ')}
                >
                  <MailboxIcon className="w-3.5 h-3.5" />
                  <span className="max-w-[140px] truncate">{mbLabel}</span>
                  {mbState.enabledMailboxIds.size > 0 && (
                    <span
                      onClick={(e) => { e.stopPropagation(); mbState.reset(); }}
                      role="button"
                      tabIndex={0}
                      className="-mr-0.5 ml-0.5 p-0.5 rounded hover:bg-white/60 dark:hover:bg-slate-900/60 text-sky-600 dark:text-sky-300"
                      aria-label={t('calendar.shell.page.clearMailboxFilter')}
                    >
                      <XIcon className="w-3 h-3" />
                    </span>
                  )}
                  <ChevronDown className="w-3.5 h-3.5 text-slate-400" />
                </button>
                {mbOpen && (
                  <div className="absolute right-0 top-full mt-1.5 w-64 rounded-xl border border-slate-200 dark:border-slate-700/80 bg-white dark:bg-slate-900 shadow-xl z-40 overflow-hidden animate-[cal-pop-in_140ms_ease-out_both]">
                    <div className="px-3 py-2 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
                      <span className="text-xs font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wider">{t('calendar.shell.page.mailboxFilter')}</span>
                      <button
                        type="button"
                        onClick={() => mbState.reset()}
                        className="text-[11px] text-sky-600 dark:text-sky-400 hover:text-sky-700 dark:hover:text-sky-300 font-medium"
                      >
                        {t('calendar.shell.page.selectAll')}
                      </button>
                    </div>
                    <div className="max-h-80 overflow-auto py-1">
                      {mbState.mailboxes.map((mb: MailboxDto) => {
                        const mbId = mb.id;
                        if (mbId == null) return null;
                        const selected = mbState.enabledMailboxIds.size === 0
                          ? true
                          : mbState.enabledMailboxIds.has(mbId);
                        return (
                          <button
                            key={mbId}
                            type="button"
                            onClick={() => mbState.toggle(mbId)}
                            className="w-full flex items-center gap-2.5 px-3 py-2 text-left hover:bg-slate-50 dark:hover:bg-slate-800/60 transition-colors"
                          >
                            <span className={[
                              'shrink-0 w-4 h-4 rounded border flex items-center justify-center transition-colors',
                              selected
                                ? 'bg-sky-500 border-sky-500 text-white'
                                : 'border-slate-300 dark:border-slate-600',
                            ].join(' ')}>
                              {selected && <Check className="w-3 h-3" />}
                            </span>
                            <div className="flex-1 min-w-0">
                              <div className="text-sm font-medium truncate text-slate-800 dark:text-slate-100">{mb.name || mb.email || t('calendar.shell.page.mailboxIdFallback', { id: mbId })}</div>
                              {mb.email && mb.email !== mb.name && (
                                <div className="text-xs truncate text-slate-500 dark:text-slate-400">{mb.email}</div>
                              )}
                            </div>
                          </button>
                        );
                      })}
                    </div>
                  </div>
                )}
              </div>
            )}

            {/* P2-A#6：更多菜单 — ICS 导入 / 按范围导出 */}
            <div ref={moreAnchorRef} className="relative">
              <button
                type="button"
                onClick={() => setMoreOpen((v) => !v)}
                className="inline-flex items-center justify-center gap-1 w-9 h-9 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-800 transition-all"
                title={t('calendar.shell.page.importExportIcsTitle')}
                aria-label={t('calendar.shell.page.importExportIcsAria')}
              >
                <MoreHorizontal className="w-4 h-4" />
              </button>
              {moreOpen && (
                <div className="absolute right-0 top-full mt-1.5 w-60 rounded-xl border border-slate-200 dark:border-slate-700/80 bg-white dark:bg-slate-900 shadow-xl z-40 overflow-hidden animate-[cal-pop-in_140ms_ease-out_both]">
                  <div className="px-3 py-2 border-b border-slate-100 dark:border-slate-800">
                    <span className="text-xs font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wider">{t('calendar.shell.page.importExportSection')}</span>
                  </div>
                  <div className="py-1">
                    <button
                      type="button"
                      onClick={handleIcsImportClick}
                      disabled={icsImporting}
                      className="w-full flex items-center gap-2.5 px-3 py-2.5 text-left text-sm text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-800/60 disabled:opacity-60 disabled:cursor-not-allowed transition-colors"
                    >
                      <UploadIcon className="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
                      <span className="flex-1 min-w-0">{icsImporting ? t('calendar.shell.page.importing') : t('calendar.shell.page.importIcsFile')}</span>
                    </button>
                    <button
                      type="button"
                      onClick={handleIcsExportRange}
                      disabled={icsExportingRange}
                      className="w-full flex items-center gap-2.5 px-3 py-2.5 text-left text-sm text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-800/60 disabled:opacity-60 disabled:cursor-not-allowed transition-colors"
                    >
                      <DownloadIcon className="w-4 h-4 text-sky-600 dark:text-sky-400" />
                      <span className="flex-1 min-w-0">{icsExportingRange ? t('calendar.shell.page.preparingExport') : t('calendar.shell.page.exportRangeIcs')}</span>
                    </button>
                    <div className="my-1 h-px bg-slate-100 dark:bg-slate-800" />
                    <button
                      type="button"
                      onClick={() => { setMoreOpen(false); useCalendarStore.getState().openSubscription(); }}
                      className="w-full flex items-center gap-2.5 px-3 py-2.5 text-left text-sm text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-800/60 transition-colors"
                    >
                      <Rss className="w-4 h-4 text-amber-600 dark:text-amber-400" />
                      <span className="flex-1 min-w-0">{t('calendar.shell.page.subscribeWebcal')}</span>
                    </button>
                  </div>
                </div>
              )}
              {/* 隐藏的 file input，由导入菜单项触发 */}
              <input
                ref={icsFileInputRef}
                type="file"
                accept=".ics,text/calendar"
                className="hidden"
                onChange={handleIcsFileChange}
              />
            </div>

            <button
              onClick={onCreateNew}
              disabled={!isOnline}
              className={[
                'px-4 py-2 text-sm font-medium text-white rounded-lg flex items-center gap-2 shadow-lg transition-all',
                isOnline
                  ? 'bg-gradient-to-r from-sky-500 to-blue-600 hover:from-sky-600 hover:to-blue-700 shadow-sky-500/30 hover:shadow-xl hover:shadow-sky-500/40'
                  : 'bg-slate-400 cursor-not-allowed opacity-70 shadow-none',
              ].join(' ')}
              title={isOnline ? t('calendar.shell.page.shortcutN') : t('calendar.shell.page.newEventOffline')}
            >
              <Plus className="w-4 h-4" />
              <span className="hidden sm:inline">{t('calendar.common.newEvent')}</span>
              {isOnline ? (
                <kbd className="hidden sm:inline-block ml-1 px-1.5 py-0.5 text-[10px] font-semibold text-white bg-white/20 border border-white/30 backdrop-blur-sm rounded">N</kbd>
              ) : (
                <WifiOffIcon className="w-3.5 h-3.5 ml-1 opacity-80" />
              )}
            </button>
          </div>
        </div>
      </header>

      <main ref={swipeRef} className="flex-1 overflow-hidden min-h-0">
        {/* P2-C#1: 视图区错误边界 —— 单个视图渲染崩溃不影响整页 */}
        <CalendarErrorBoundary
          title={t('calendar.shell.page.viewLoadFailed')}
          description={t('calendar.shell.page.viewLoadFailedDesc')}
          onRecover={() => {
            // 错误边界恢复：直接刷新页面是最稳妥的"恢复且重新拉数据"手段（保证 zustand 状态重建 + API 重拉）
            window.location.reload();
          }}
        >
          <div className="h-full w-full min-h-0">
        {viewMode === 'calendar' && (
          calSubView === 'month' ? (
            <div key={viewAnimKey} className={viewAnimClass}>
              <MonthView />
            </div>
          ) :
          calSubView === 'week' ? (
            <div key={viewAnimKey} className={viewAnimClass}>
              <WeekView />
            </div>
          ) : (
            <div key={viewAnimKey} className={viewAnimClass}>
              <DayView />
            </div>
          )
        )}
        {viewMode === 'overview' && <Overview />}
        {viewMode === 'list'     && <ListView />}
        {viewMode === 'trash'    && <TrashView />}
          </div>
        </CalendarErrorBoundary>
      </main>

      {/* 第三梯队: 撤销/重做 Toast */}
      {showUndoToast && undoToastMessage && (
        <div className="fixed bottom-4 left-1/2 -translate-x-1/2 z-50 animate-[cal-pop-in_180ms_ease]">
          <div className="flex items-center gap-3 px-4 py-2.5 bg-slate-800 dark:bg-slate-700 text-white rounded-lg shadow-lg">
            <span className="text-sm font-medium">{undoToastMessage}</span>
            <button
              type="button"
              onClick={() => { void useCalendarStore.getState().undo(); }}
              className="px-2 py-0.5 text-xs font-medium text-sky-300 hover:text-sky-200 transition-colors"
            >
              {t('calendar.shell.page.undo')}
            </button>
            <button
              type="button"
              onClick={() => useCalendarStore.getState().dismissUndoToast()}
              className="text-slate-400 hover:text-white transition-colors"
            >
              ✕
            </button>
          </div>
        </div>
      )}

      {/* P1-C#1: 通用瞬态 toast（批量操作、剪贴板、alert 替换等） */}
      {transientToast && (
        <div className="fixed bottom-4 sm:bottom-6 left-0 right-0 z-[60] px-4 pointer-events-none flex justify-center">
          <TransientToast
            open={true}
            type={transientToast.type}
            message={transientToast.message}
            onClose={dismissTransientToast}
          />
        </div>
      )}

      {/* T000090：实时提醒弹窗已迁至全局组件 GlobalCalendarReminderListener，
          任意页面均可展示。此处不再重复渲染。 */}

      <EditorPanel />
      <SidePanels />

      {(loading || isRefreshing) && (
        <div className="fixed top-0 left-0 right-0 h-1 bg-sky-200 z-50">
          <div className="h-full bg-gradient-to-r from-sky-400 via-blue-500 to-sky-400 animate-pulse w-1/3" />
        </div>
      )}
    </div>
    </CalendarA11yProvider>
  );
}
