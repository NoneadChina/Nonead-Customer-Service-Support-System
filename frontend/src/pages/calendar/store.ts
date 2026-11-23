import { create } from 'zustand';
import type {
  CalendarEventDto,
  CalendarOverviewResponse,
  CalendarEventQuery,
  OutlookSyncStatusDto,
  OutlookSyncResultDto,
  CreateCalendarEventRequest,
} from '../../api/types';
import { calendarApi, outlookSyncApi } from '../../api/modules/engagement';
import type { TranslationKey } from '../../locales/zh-Hans';
import type { TranslateParams } from '../../i18n/types';
import { BUILT_IN_ENTRIES, FALLBACK_LOCALE, getBoundLocale, tBound } from '../../i18n/locale';
import { translate } from '../../i18n/translate';
import type { EditorForm, CalendarEventPriority } from './calendarUtils';
import type { YMD, ViewMode, CalSubView } from './calendarUtils';
import { buildEventsByDate, EMPTY_FORM, fromIsoToDateInput, fromIsoToLocalInput, toIsoString, REMINDER_OPTIONS, parseReminderTypes } from './calendarUtils';

/**
 * 带插值的取词：`tBound()` 不接受参数，带 `{name}` 的词条走 `translate()`。
 *
 * store 是**非组件模块**（无 React 上下文），语言由 `I18nProvider` 在渲染时绑定
 * （见 `i18n/locale.ts`）。⚠ 词条结果**不得**写进 zustand state / localStorage
 * （切语言后不会更新）——state 里存 code/key，toast 在**弹出那一刻**才取词。
 */
function tParams(key: TranslationKey, params?: TranslateParams): string {
  const locale = getBoundLocale();
  return translate(BUILT_IN_ENTRIES[locale] ?? BUILT_IN_ENTRIES[FALLBACK_LOCALE], locale, key, params);
}

/* =========================================================
   P3: 错误处理助手 —— 从 unknown 类型错误中安全提取信息
   ========================================================= */

/** 从 unknown 类型错误中安全提取错误消息（无匹配时返回 undefined，由调用方提供兜底文本） */
export function getErrorMessage(error: unknown): string | undefined {
  if (error instanceof Error) return error.message;
  if (typeof error === 'string') return error;
  if (error && typeof error === 'object' && 'message' in error) {
    const msg = (error as Record<string, unknown>).message;
    if (typeof msg === 'string') return msg;
  }
  return undefined;
}

/** 判断 unknown 是否为 axios 风格错误（含 response 字段） */
function isApiError(error: unknown): error is { response?: { status?: number; data?: unknown } } {
  return error != null && typeof error === 'object' && 'response' in error;
}

/** 从 axios 风格错误中提取 HTTP 状态码 */
export function getErrorStatus(error: unknown): number | undefined {
  return isApiError(error) ? error.response?.status : undefined;
}

/** 从 axios 风格错误中提取响应体 data 字段 */
export function getErrorData<T = unknown>(error: unknown): T | undefined {
  return isApiError(error) ? (error.response?.data as T | undefined) : undefined;
}

/** 冲突数据项类型（409 时间冲突响应中的单个冲突项） */
export interface ConflictItem {
  id?: number;
  title?: string;
  startAt?: string;
  endAt?: string;
}

/** axios 错误响应体结构 (e.response.data) */
export interface ApiErrorResponseBody {
  message?: string;
  latest?: CalendarEventDto;
  data?: ConflictResponseData;
}

/** 409 冲突响应体 (e.response.data.data) */
export interface ConflictResponseData {
  conflicts?: ConflictItem[];
  errorType?: string;
  latest?: CalendarEventDto;
}

/** 从 CalendarEventDto 中剥离只读/计算字段，返回创建请求所需的可写字段 */
function toCreateRequest(snap: CalendarEventDto): CreateCalendarEventRequest {
  const {
    id: _id, eventTypeLabel: _etl, conversationSubject: _cs, mailboxName: _mn,
    userName: _un, status: _st, statusLabel: _sl, deletedAt: _da,
    createdAt: _ca, updatedAt: _ua, createdBy: _cb, createdByName: _cbn,
    recurrenceParentId: _rpi, recurrenceOriginalStart: _ros,
    isReminderSent: _irs, taskOwnerName: _ton, rowVersion: _rv,
    ...createData
  } = snap;
  void _id; void _etl; void _cs; void _mn; void _un; void _st; void _sl;
  void _da; void _ca; void _ua; void _cb; void _cbn; void _rpi;
  void _ros; void _irs; void _ton; void _rv;
  return createData;
}

// P1-B#1: 结构化 DTO（替换原 any，防止污染整个 store 类型系统；字段可选以兼容 API 回包的不同形态）
export interface MailboxDto {
  id?: number;
  userId?: number;
  name?: string | null;
  email?: string | null;
  displayName?: string | null;
  isDefault?: boolean;
  createdAt?: string | null;
  updatedAt?: string | null;
  [key: string]: unknown;
}

export interface UserDto {
  id?: number;
  username?: string | null;
  nickname?: string | null;
  email?: string | null;
  phone?: string | null;
  avatar?: string | null;
  role?: number | null;
  departmentId?: number | null;
  status?: number | null;
  createdAt?: string | null;
  updatedAt?: string | null;
  [key: string]: unknown;
}

/* =========================================================
   P2P3 §3.3 #8: localStorage 持久化（每用户隔离，50 条上限）
   ========================================================= */
export type UndoActionType =
  | 'create'
  | 'update'
  | 'delete'
  | 'bulk_delete'
  | 'restore'
  | 'hard_delete'
  | 'clear_trash'
  | 'move';

const UNDO_REDO_VERSION = 1;
const UNDO_STORAGE_KEY_PREFIX = 'calendar:undo:';
const STACK_CAPACITY = 50;

/** 撤销/重做条目：记录操作前的完整事件状态，用于恢复。
 * 同时兼容 master 简单形状（type/eventId/单快照）与 P2 持久化形状（id/createdAt/action/数组快照）。 */
export interface UndoEntry {
  /** 兼容 master 简洁字段：'move'|'edit'|'delete'|'create'|'resize' 等 */
  type?: 'move' | 'edit' | 'delete' | 'create' | 'resize' | string;
  /** 兼容 P2 动作枚举：create/update/delete/bulk_delete/restore/hard_delete/clear_trash/move */
  action?: UndoActionType;
  description?: string;
  /** P2 持久化唯一键 */
  id?: string;
  createdAt?: string;
  /** master 简单模式：操作前/后的单个事件快照 */
  beforeSnapshot?: CalendarEventDto | null;
  afterSnapshot?: CalendarEventDto | null;
  /** P2 批量操作兼容：前后批量快照 */
  beforeSnapshots?: CalendarEventDto[];
  afterSnapshots?: CalendarEventDto[];
  /** master 简单模式：事件 ID */
  eventId?: number;
}

interface PersistedUndoRedo {
  undoStack: UndoEntry[];
  redoStack: UndoEntry[];
  version: number;
  savedAt: string;
}

function _getUserId(): number | undefined {
  try {
    const raw = localStorage.getItem('user');
    if (!raw) return undefined;
    const parsed = JSON.parse(raw) as { id?: number };
    return parsed.id;
  } catch {
    return undefined;
  }
}

/** 将当前 undo/redo 栈写入 localStorage（按当前登录用户隔离）。
 * 该导出由 store 内部调用，同时供单测/AuthContext 清理流程使用。 */
export function persistUndoRedo(getState: () => CalendarStoreState) {
  try {
    const userId = _getUserId();
    if (userId === undefined) return;
    const s = getState();
    const payload: PersistedUndoRedo = {
      version: UNDO_REDO_VERSION,
      savedAt: new Date().toISOString(),
      undoStack: s.undoStack.slice(-STACK_CAPACITY),
      redoStack: s.redoStack.slice(-STACK_CAPACITY),
    };
    localStorage.setItem(`${UNDO_STORAGE_KEY_PREFIX}${userId}`, JSON.stringify(payload));
  } catch (e) {
    console.warn('[calendar] undo persist failed:', e);
  }
}

// P3-3: toast 自动隐藏定时器管理
// 避免 setTimeout 叠加导致新 toast 被旧 timer 提前隐藏：每次显示新 toast 前先清除上一个 timer
let _undoToastTimer: number | null = null;
function scheduleUndoToastHide(set: (partial: Record<string, unknown>) => void, get: () => { showUndoToast: boolean }, ms: number) {
  if (_undoToastTimer != null) { window.clearTimeout(_undoToastTimer); _undoToastTimer = null; }
  _undoToastTimer = window.setTimeout(() => {
    if (get().showUndoToast) set({ showUndoToast: false, undoToastMessage: null });
    _undoToastTimer = null;
  }, ms);
}

/** 校验单个 UndoEntry 是否为合法持久化条目。
 *  P2P3 §3.3.3 规范：必须有 id + action + (beforeSnapshot/afterSnapshot 数组 或 beforeSnapshots/afterSnapshots 数组)。 */
function _isValidUndoEntry(e: unknown): e is UndoEntry {
  if (e == null || typeof e !== 'object') return false;
  const obj = e as Record<string, unknown>;
  if (typeof obj.id !== 'string' || obj.id.length === 0) return false;
  const hasSnapshots =
    (Array.isArray(obj.beforeSnapshot) && Array.isArray(obj.afterSnapshot)) ||
    (Array.isArray(obj.beforeSnapshots) && Array.isArray(obj.afterSnapshots));
  if (!hasSnapshots) return false;
  return true;
}

/** 从 localStorage 恢复当前用户的 undo/redo 栈（用于刷新后保留撤销历史）。 */
export function restoreUndoRedoFromStorage(): {
  undoStack: UndoEntry[];
  redoStack: UndoEntry[];
} | null {
  try {
    const userId = _getUserId();
    if (userId === undefined) return null;
    const raw = localStorage.getItem(`${UNDO_STORAGE_KEY_PREFIX}${userId}`);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as PersistedUndoRedo;
    if (parsed.version !== UNDO_REDO_VERSION) return null;
    if (!Array.isArray(parsed.undoStack) || !Array.isArray(parsed.redoStack)) return null;
    // P2P3 §3.3.3: 过滤无效 entry（缺 id / beforeSnapshot 不是数组 等）
    const filteredUndo = parsed.undoStack.filter(_isValidUndoEntry);
    const filteredRedo = parsed.redoStack.filter(_isValidUndoEntry);
    // P2P3 §3.3 #8: 容量裁剪——无论 localStorage 中写入多少条，仅保留最新 STACK_CAPACITY(50) 条
    return {
      undoStack: filteredUndo.slice(-STACK_CAPACITY),
      redoStack: filteredRedo.slice(-STACK_CAPACITY),
    };
  } catch (e) {
    console.warn('[calendar] undo restore failed:', e);
    return null;
  }
}

/** 登出/切换账号时清理所有 calendar:undo:* 前缀的本地键，避免跨用户串数据。 */
export function clearPersistedUndoRedoForLogout() {
  try {
    const keys: string[] = [];
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i);
      if (k && k.startsWith(UNDO_STORAGE_KEY_PREFIX)) keys.push(k);
    }
    for (const k of keys) localStorage.removeItem(k);
  } catch (e) {
    console.warn('[calendar] undo clear on logout failed:', e);
  }
}

/* =========================================================
   拖拽/调整时长的"挂起乐观补丁"本地持久化
   ---------------------------------------------------------
   背景：moveEventByDrop / resizeEvent 调 API 成功后，虽然 setAllEvents 有 5 秒内存乐观保护期，
   但一旦用户刷新页面，内存里的 allEvents 和 _optimisticMarkers 全部丢失，
   新的 API 拉全量时"刚改完的日程又跳回原位"——用户会感觉改完的结果"神秘消失"。
   机制：
   1) API 响应成功 → 把 { id, startAt, endAt, appliedAtMs } 写入 localStorage PENDING_PATCH
   2) setAllEvents 合并完后端数据后，再把 PENDING_PATCH 中仍未过期、未被后端同步的补丁 replay 到 allEvents
   3) 若 setAllEvents 后端数据已和 patch 值一致（后端已同步） → 自动从 patch 中移除
   4) 过期窗口 10 分钟（给后台写库/主从同步留足时间）；超过保留但给出 toast 提示
   ========================================================= */
const PENDING_PATCH_KEY_PREFIX = 'calendar:pendingPatch:';
const PENDING_PATCH_TTL_MS = 10 * 60 * 1000; // 10 分钟

interface PendingPatch {
  eventId: number;
  startAt?: string;     // move 时写入
  endAt?: string;       // move/resize 时写入
  operation: 'move' | 'resize';
  appliedAtMs: number;  // 本地时间戳，用于 TTL 过期判断
  // 跨天/重复日程拖拽：记录拖拽实例的原始 recurrenceOriginalStart（父事件展开实例的"原始起始时间"）
  // 用于 setAllEvents replay 时：
  //   1) sync 检测：后端重新展开后，oldStartAt 不再存在于任何实例的 recurrenceOriginalStart → 已同步
  //   2) 计算 delta = patch.startAt - patch.oldStartAt，把同父 ID 的所有展开实例 startAt/endAt 一并偏移
  // 注意：用 recurrenceOriginalStart 而非 ev.startAt，避免连续两次拖拽同一实例时 delta 累计错误
  oldStartAt?: string;
}

function _pendingPatchKeyForUser(): string {
  const uid = _getUserId();
  return `${PENDING_PATCH_KEY_PREFIX}${uid ?? 'anon'}`;
}

function readPendingPatches(): PendingPatch[] {
  try {
    const raw = localStorage.getItem(_pendingPatchKeyForUser());
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    const arr = parsed as unknown[];
    const nowMs = Date.now();
    // 过滤掉结构错误
    // P3-1: writePendingPatches 已按 TTL 过滤写入，读取时无需 10x 冗余窗口
    return arr.filter((p): p is PendingPatch => {
      if (!p || typeof p !== 'object') return false;
      const obj = p as Record<string, unknown>;
      return typeof obj.eventId === 'number'
        && typeof obj.appliedAtMs === 'number'
        && nowMs - obj.appliedAtMs <= PENDING_PATCH_TTL_MS;
    });
  } catch (e) {
    console.warn('[calendar] pendingPatch read failed:', e);
    return [];
  }
}

function writePendingPatches(patches: PendingPatch[]) {
  try {
    // P3-13: 写入时按 TTL（10 分钟）裁剪，与 setAllEvents 的过期判断一致
    // 避免过期 patch 被反复读出 → 触发重复 toast
    const nowMs = Date.now();
    const trimmed = patches.filter((p) => nowMs - p.appliedAtMs <= PENDING_PATCH_TTL_MS);
    localStorage.setItem(_pendingPatchKeyForUser(), JSON.stringify(trimmed));
  } catch (e) {
    console.warn('[calendar] pendingPatch write failed:', e);
  }
}

/** 合并新补丁到现有列表：同 eventId 覆盖旧值（以最新操作为准） */
function upsertPendingPatch(next: PendingPatch) {
  const list = readPendingPatches();
  const idx = list.findIndex((p) => p.eventId === next.eventId);
  if (idx >= 0) list[idx] = next; else list.push(next);
  writePendingPatches(list);
}

/** 从持久化移除一个事件的 pending patch（后端已同步成功时） */
function dropPendingPatch(eventId: number) {
  const list = readPendingPatches();
  const next = list.filter((p) => p.eventId !== eventId);
  if (next.length !== list.length) writePendingPatches(next);
}

export function clearPendingPatchesForLogout() {
  try {
    const keys: string[] = [];
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i);
      if (k && k.startsWith(PENDING_PATCH_KEY_PREFIX)) keys.push(k);
    }
    for (const k of keys) localStorage.removeItem(k);
  } catch (_e) { /* noop */ }
}

/* =========================================================
   视图状态持久化：viewMode + calSubView + calCursor + calMonth
   刷新浏览器后恢复到用户上次浏览的主视图（日历/列表/概览/订阅...）
   以及子视图（月/周/日）和日期位置
   ========================================================= */
const VIEW_STORAGE_KEY = 'calendar:viewState';
const VIEW_STATE_VERSION = 2;

interface PersistedViewState {
  viewMode: ViewMode;
  calSubView: CalSubView;
  /** ISO 字符串，恢复时 new Date() 解析 */
  calCursor: string;
  calMonth: string;
  version: number;
}

function persistViewState(state: { viewMode: ViewMode; calSubView: CalSubView; calCursor: Date; calMonth: Date }) {
  try {
    const payload: PersistedViewState = {
      viewMode: state.viewMode,
      calSubView: state.calSubView,
      calCursor: state.calCursor.toISOString(),
      calMonth: state.calMonth.toISOString(),
      version: VIEW_STATE_VERSION,
    };
    localStorage.setItem(VIEW_STORAGE_KEY, JSON.stringify(payload));
  } catch (e) {
    console.warn('[calendar] view state persist failed:', e);
  }
}

function restoreViewState(): { viewMode: ViewMode; calSubView: CalSubView; calCursor: Date; calMonth: Date } | null {
  try {
    const raw = localStorage.getItem(VIEW_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as PersistedViewState;
    if (parsed.version !== VIEW_STATE_VERSION) return null;
    if (!parsed.viewMode || !parsed.calSubView) return null;
    const calCursor = new Date(parsed.calCursor);
    const calMonth = new Date(parsed.calMonth);
    if (isNaN(calCursor.getTime()) || isNaN(calMonth.getTime())) return null;
    return { viewMode: parsed.viewMode, calSubView: parsed.calSubView, calCursor, calMonth };
  } catch (e) {
    console.warn('[calendar] view state restore failed:', e);
    return null;
  }
}

/** 登出时清理视图状态，避免跨用户串数据 */
export function clearPersistedViewState() {
  try { localStorage.removeItem(VIEW_STORAGE_KEY); } catch (_e) { /* noop */ }
}

/* =========================================================
   P2-1: 事件模板类型
   ========================================================= */
export interface EventTemplate {
  id: number;
  name: string;
  title: string;
  eventType: string;
  durationMin: number;       // 默认时长（分钟），0=全天
  isAllDay: boolean;
  description?: string;
  location?: string;
}

const _EMPTY_ARR = [] as unknown as CalendarEventDto[];

/* =========================================================
   P3: undo/redo 重复日程联动偏移公共函数
   ---------------------------------------------------------
   undo 和 redo 中对重复日程全系列实例的偏移逻辑几乎完全相同，
   仅偏移方向不同（undo 为负向，redo 为正向）。提取为公共函数避免 ~120 行重复代码。
   ========================================================= */

/**
 * 对重复日程的所有同父 ID 展开实例（排除被拖拽的实例本身）进行时间偏移。
 * 用于 undo/redo 联动恢复/重做 move/resize 操作的全系列实例。
 *
 * @param events        事件数组（allEvents / list.data / overview.upcomingEvents）
 * @param parentId      重复日程父事件 ID
 * @param excludeOrig   被拖拽实例的 recurrenceOriginalStart（跳过该实例，因其已由 snapshot 直接覆盖）
 * @param isMove        是否为 move 操作（move 偏移 startAt + endAt；resize 只偏移 endAt）
 * @param signedDeltaMs     move 的带符号偏移量（undo 为负，redo 为正）
 * @param signedDeltaEndMs  resize endAt 的带符号偏移量（undo 为负，redo 为正）
 * @returns { mutated, result } — mutated=true 表示有实例被偏移；result 为偏移后的新数组
 */
function shiftRecurringInstances<T extends CalendarEventDto>(
  events: readonly T[],
  parentId: number,
  excludeOrig: string | null | undefined,
  isMove: boolean,
  signedDeltaMs: number,
  signedDeltaEndMs: number,
): { mutated: boolean; result: T[] } {
  if (signedDeltaMs === 0 && signedDeltaEndMs === 0) {
    return { mutated: false, result: events as T[] };
  }
  let mutated = false;
  const result = events.map((e) => {
    if (e.id !== parentId || !e.recurrenceOriginalStart) return e;
    // 跳过被拖拽的实例本身（已由 snapshot 直接覆盖）
    if (excludeOrig && (e.recurrenceOriginalStart ?? '') === excludeOrig) return e;
    const eStart = new Date(e.startAt).getTime();
    const eEnd = e.endAt ? new Date(e.endAt).getTime() : null;
    if (!isFinite(eStart)) return e;
    mutated = true;
    return {
      ...e,
      // move: startAt 偏移 signedDeltaMs；resize: startAt 不变
      ...(isMove ? { startAt: new Date(eStart + signedDeltaMs).toISOString() } : {}),
      // move: endAt 偏移 signedDeltaMs；resize: endAt 偏移 signedDeltaEndMs
      endAt: eEnd != null && isFinite(eEnd)
        ? new Date(eEnd + (isMove ? signedDeltaMs : signedDeltaEndMs)).toISOString()
        : e.endAt,
    } as T;
  });
  return { mutated, result };
}

export interface CalendarStoreState {
  viewMode: ViewMode;
  calSubView: CalSubView;
  calMonth: Date;
  calCursor: Date;
  datePreset: string | null;
  query: CalendarEventQuery;

  allEvents: CalendarEventDto[];
  eventsByDate: Map<YMD, CalendarEventDto[]>;
  list: { data: CalendarEventDto[]; totalItems: number; totalPages: number; currentPage: number } | null;
  overview: CalendarOverviewResponse | null;
  signalRRev: number;

  showEditor: boolean; editing: CalendarEventDto | null;
  form: EditorForm; saving: boolean; formError: string | null;
  recurrencePanelOpen: boolean; reminderPanelOpen: boolean; attendeeDropdownOpen: boolean;
  recurrenceEndMode: 'count' | 'date' | 'never';
  deleteTarget: CalendarEventDto | null; deleting: boolean;
  /** P0 演示用：参会人邮箱字符串列表，P1 接入通讯录后映射为 attendees number[] */
  _attendeeEmailList: string[];
  viewTarget: CalendarEventDto | null; setViewTarget: (v: CalendarEventDto | null) => void;
  showTrash: boolean; trashLoading: boolean; trashKeyword: string; trashPage: number;
  trashList: CalendarEventDto[]; trashCount: number;
  restoreTarget: CalendarEventDto | null; restoring: boolean;
  batchRestoring: boolean;
  hardDeleteTarget: CalendarEventDto | null; hardDeleting: boolean;
  showClearTrashConfirm: boolean; clearingTrash: boolean;
  showPurgeTrashConfirm: boolean; purgeRetentionDays: number; purgingTrash: boolean;
  showSubscription: boolean; subscriptionUrl: string; subscriptionLoading: boolean; copied: boolean;
  showOutlookSync: boolean; outlookStatus: OutlookSyncStatusDto | null;
  outlookStatusLoading: boolean; outlookConnecting: boolean; outlookSyncing: boolean;
  outlookSaving: boolean; outlookDisconnecting: boolean;
  outlookDirection: 'noneads_to_outlook' | 'outlook_to_noneads' | 'bidirectional';
  outlookSyncEnabled: boolean; outlookSyncResult: OutlookSyncResultDto | null;

  loading: boolean; refreshLock: boolean;
  mailboxes: MailboxDto[]; users: UserDto[];
  draggedEvent: CalendarEventDto | null; dragOverDateKey: YMD | null;
  dragging: boolean; isDragging: boolean;
  /** 拖拽中幽灵预览的吸附位置：日期 + 小数小时（含分钟，如 10.25 = 10:15） */
  dragPreviewPos: { ymd: YMD; hourFloat: number } | null;
  pendingSignalRBump: boolean;
  /** 正在"本地挂起同步"的日程 ID 集合：API 已返回成功，但后端数据库尚未确认（或刷新读回时仍为旧值）
   *  存储于 localStorage PENDING_PATCH，刷新后仍会 replay 到视图；视图端据此渲染灰色小圆点。 */
  pendingPatchIds: Set<number>;
  selectedDateEvents: CalendarEventDto[] | null;
  // P1-2: 批量选择
  selectedEventIds: Set<number>;
  // 第三梯队: 键盘导航焦点
  focusedEventId: number | null;
  // P2-1: 事件模板
  eventTemplates: EventTemplate[];
  // 第三梯队: 事件类型筛选
  enabledEventTypes: Set<string>;
  // P1-5: 多邮箱筛选
  enabledMailboxIds: Set<number>;
  // 第三梯队: 撤销/重做
  undoStack: UndoEntry[];
  redoStack: UndoEntry[];
  showUndoToast: boolean;
  undoToastMessage: string | null;
  // P1-C#1: 通用瞬态 toast（success/error/info/warning，可由 alert 替换、剪贴板、批量操作、mock API 等复用）
  transientToast: { type: 'success' | 'error' | 'info' | 'warning'; message: string; key: number } | null;
  /** 乐观变更保护期：事件 id → 本地乐观修改的时间戳（ms）。保护期内 setAllEvents 不会用后端数据覆盖本地乐观结果。 */
  _optimisticMarkers: Map<number, number>;
}

export type TransientToastType = 'success' | 'error' | 'info' | 'warning';

export interface CalendarStoreActions {
  setViewMode: (m: ViewMode) => void;
  setCalSubView: (s: CalSubView) => void;
  nextRange: () => void; prevRange: () => void; gotoToday: () => void;
  setQueryField: <K extends keyof CalendarEventQuery>(key: K, val: CalendarEventQuery[K], resetPage?: boolean) => void;

  setAllEvents: (arr: CalendarEventDto[]) => void;
  /** 保存成功后立刻把 DTO 乐观插入 / 更新到本地 allEvents，避免等 signalRRev → API 刷新才出现在视图里。 */
  optimisticUpsertEvent: (ev: CalendarEventDto) => void;
  getEventsByYMD: (ymd: YMD) => readonly CalendarEventDto[];
  getEventsCountByYMD: (ymd: YMD) => number;

  openCreate: (date?: Date) => void;
  openEdit: (ev: CalendarEventDto) => void;
  closeEditor: () => void; resetForm: () => void;
  setFormField: <K extends keyof EditorForm>(k: K, v: EditorForm[K]) => void;
  openTrash: () => void; openSubscription: () => void; openOutlookSync: () => void;
  /** Outlook 同步：从后端获取同步状态和设置 */
  fetchOutlookStatus: () => Promise<void>;
  /** Outlook 同步：发起 OAuth 授权（重定向到微软登录页） */
  connectOutlook: () => Promise<void>;
  /** Outlook 同步：断开 Outlook 连接 */
  disconnectOutlook: () => Promise<void>;
  /** Outlook 同步：保存同步方向和自动同步开关 */
  saveOutlookSetting: () => Promise<void>;
  /** Outlook 同步：手动触发一次增量同步 */
  syncOutlookNow: () => Promise<void>;
  setShowTrash: (v: boolean) => void;
  setTrashFilter: (keyword?: string, page?: number, pageSize?: number) => void;
  setRestoreTarget: (ev: CalendarEventDto | null) => void;
  setHardDeleteTarget: (ev: CalendarEventDto | null) => void;
  setPurgeDialog: (open: boolean, retentionDays?: number) => void;
  loadTrashAsync: () => Promise<void>;
  /** 轻量刷新垃圾箱数量（独立于垃圾箱 UI，供日历导航页右上角红点徽标使用） */
  refreshTrashCount: () => Promise<void>;
  restoreAsync: (id: number) => Promise<void>;
  /** 批量恢复多个软删除事件（垃圾箱「恢复全部可恢复」等场景） */
  batchRestoreAsync: (ids: number[]) => Promise<void>;
  hardDeleteAsync: (id: number) => Promise<void>;
  clearTrashAsync: () => Promise<void>;
  purgeTrashAsync: (retentionDays?: number) => Promise<void>;

  setDragOver: (ymdKey: YMD | null) => void;
  /** 设置拖拽幽灵预览的吸附位置（15 分钟精度）；传 null 清除。拖动中位置变化会立即触发重新渲染。 */
  setDragPreviewPos: (pos: { ymd: YMD; hourFloat: number } | null) => void;
  startDrag: (ev: CalendarEventDto) => void;
  endDrag: () => void;
  moveEventByDrop: (targetYmd: YMD, targetHourFloat?: number) => Promise<void>;
  resizeEvent: (eventId: number, newEndAt: string) => Promise<void>;
  openSelectedDateEvents: (ymd: YMD) => void;
  // P1-2: 批量选择
  selectEvent: (id: number) => void;
  deselectEvent: (id: number) => void;
  clearSelection: () => void;
  selectAll: (ids: number[]) => void;
  isSelected: (id: number) => boolean;
  // 第三梯队: 键盘导航焦点
  setFocusedEventId: (id: number | null) => void;
  focusNextEvent: (direction: 1 | -1) => void;
  // P2-1: 模板
  loadTemplates: (templates: EventTemplate[]) => void;
  applyTemplate: (templateId: number, date?: Date) => void;
  fetchTemplates: () => Promise<void>;
  saveTemplate: (name: string) => Promise<EventTemplate | null>;
  deleteTemplate: (templateId: number) => Promise<boolean>;
  // 第三梯队: 事件类型筛选
  toggleEventTypeFilter: (eventType: string) => void;
  setAllEventTypesEnabled: () => void;
  // P1-5: 多邮箱筛选
  toggleMailboxFilter: (mailboxId: number) => void;
  setAllMailboxesEnabled: () => void;
  mailboxIsEnabled: (mailboxId: number | null | undefined) => boolean;
  // 第三梯队: 撤销/重做
  pushUndo: (entry: UndoEntry) => void;
  undo: () => Promise<void>;
  redo: () => Promise<void>;
  dismissUndoToast: () => void;
  // P1-C#1: 通用瞬态 toast
  showTransientToast: (message: string, type?: TransientToastType, durationMs?: number) => void;
  dismissTransientToast: () => void;
  /** 内部：给指定事件 id 打上乐观修改标记，保护期内 setAllEvents 不覆盖本地版本。 */
  _markOptimistic: (eventId: number) => void;
  /** 内部：移除指定事件 id 的乐观修改标记（用于失败回滚后恢复后端推送处理）。 */
  _unmarkOptimistic: (eventId: number) => void;
  /** 持久化 pending patch（刷新后 replay 保留本地未同步修改） */
  upsertPendingPatch: (patch: PendingPatch) => void;
  /** 删除指定事件的 pending patch */
  dropPendingPatch: (eventId: number) => void;
}

export type CalendarStore = CalendarStoreState & CalendarStoreActions;

function createInitialState(): CalendarStoreState {
  const now = new Date();
  return {
    viewMode: 'calendar', calSubView: 'month',
    calMonth: new Date(now.getFullYear(), now.getMonth(), 1),
    calCursor: new Date(now.getFullYear(), now.getMonth(), now.getDate()),
    datePreset: null, query: { page: 1, pageSize: 20 },
    allEvents: [], eventsByDate: new Map(), list: null, overview: null, signalRRev: 0,
    showEditor: false, editing: null,
    form: { ...EMPTY_FORM },
    saving: false, formError: null,
    recurrencePanelOpen: false, reminderPanelOpen: false, attendeeDropdownOpen: false, recurrenceEndMode: 'never',
    deleteTarget: null, deleting: false, _attendeeEmailList: [],
    viewTarget: null, setViewTarget: () => {},
    showTrash: false, trashLoading: false, trashKeyword: '', trashPage: 1, trashList: [], trashCount: 0,
    restoreTarget: null, restoring: false, batchRestoring: false, hardDeleteTarget: null, hardDeleting: false,
    showClearTrashConfirm: false, clearingTrash: false, showPurgeTrashConfirm: false, purgeRetentionDays: 30, purgingTrash: false,
    showSubscription: false, subscriptionUrl: '', subscriptionLoading: false, copied: false,
    showOutlookSync: false, outlookStatus: null, outlookStatusLoading: false,
    outlookConnecting: false, outlookSyncing: false, outlookSaving: false, outlookDisconnecting: false,
    outlookDirection: 'bidirectional', outlookSyncEnabled: false, outlookSyncResult: null,
    loading: false, refreshLock: false,
    mailboxes: [], users: [],
    draggedEvent: null, dragOverDateKey: null, dragging: false, isDragging: false, dragPreviewPos: null, pendingSignalRBump: false,
    pendingPatchIds: new Set<number>(),
    selectedDateEvents: null,
    selectedEventIds: new Set<number>(),
    focusedEventId: null,
    eventTemplates: [],
    enabledEventTypes: new Set<string>(),
    enabledMailboxIds: new Set<number>(),
    undoStack: [],
    redoStack: [],
    showUndoToast: false,
    undoToastMessage: null,
    transientToast: null,
    _optimisticMarkers: new Map<number, number>(),
  };
}

export const useCalendarStore = create<CalendarStore>()((set, get) => {
  // P2-A#4: 同步从 localStorage 恢复 undo/redo（按当前用户隔离）
  const restored = restoreUndoRedoFromStorage();
  const _baseInit = createInitialState();
  // T000120: 恢复视图状态仅保留主视图/子视图，不再恢复 calCursor/calMonth（日期位置）。
  // 日历每次进入都重置到当前月份（Outlook 行为），避免 F5 刷新时闪现上次浏览的月份。
  const restoredView = restoreViewState();
  const initialState: CalendarStoreState = restored
    ? {
        ..._baseInit,
        undoStack: Array.isArray(restored.undoStack) ? restored.undoStack.slice(-STACK_CAPACITY) : _baseInit.undoStack,
        redoStack: Array.isArray(restored.redoStack) ? restored.redoStack.slice(-STACK_CAPACITY) : _baseInit.redoStack,
      }
    : _baseInit;
  // 如果有恢复的视图状态，仅覆盖主视图 + 子视图；日期位置始终用当前月/今天（由 createInitialState 提供）
  if (restoredView) {
    initialState.viewMode = restoredView.viewMode;
    initialState.calSubView = restoredView.calSubView;
  }

  return {
    ...initialState,

  setViewMode: (m) => {
    set({ viewMode: m });
    const { calSubView, calCursor, calMonth } = get();
    persistViewState({ viewMode: m, calSubView, calCursor, calMonth });
  },
  setCalSubView: (s) => {
    set({ calSubView: s });
    const { viewMode, calCursor, calMonth } = get();
    persistViewState({ viewMode, calSubView: s, calCursor, calMonth });
  },
  nextRange: () => {
    const { viewMode, calSubView, calMonth, calCursor } = get();
    if (calSubView === 'month') { const m = new Date(calMonth); m.setMonth(m.getMonth() + 1); set({ calMonth: m }); persistViewState({ viewMode, calSubView, calCursor, calMonth: m }); }
    else if (calSubView === 'week') { const c = new Date(calCursor); c.setDate(c.getDate() + 7); set({ calCursor: c }); persistViewState({ viewMode, calSubView, calCursor: c, calMonth }); }
    else if (calSubView === 'day') { const c = new Date(calCursor); c.setDate(c.getDate() + 1); set({ calCursor: c }); persistViewState({ viewMode, calSubView, calCursor: c, calMonth }); }
  },
  prevRange: () => {
    const { viewMode, calSubView, calMonth, calCursor } = get();
    if (calSubView === 'month') { const m = new Date(calMonth); m.setMonth(m.getMonth() - 1); set({ calMonth: m }); persistViewState({ viewMode, calSubView, calCursor, calMonth: m }); }
    else if (calSubView === 'week') { const c = new Date(calCursor); c.setDate(c.getDate() - 7); set({ calCursor: c }); persistViewState({ viewMode, calSubView, calCursor: c, calMonth }); }
    else if (calSubView === 'day') { const c = new Date(calCursor); c.setDate(c.getDate() - 1); set({ calCursor: c }); persistViewState({ viewMode, calSubView, calCursor: c, calMonth }); }
  },
  gotoToday: () => {
    const n = new Date();
    const newCursor = new Date(n.getFullYear(), n.getMonth(), n.getDate());
    const newMonth = new Date(n.getFullYear(), n.getMonth(), 1);
    set({ calMonth: newMonth, calCursor: newCursor });
    const { viewMode, calSubView } = get();
    persistViewState({ viewMode, calSubView, calCursor: newCursor, calMonth: newMonth });
  },
  setQueryField: (k, v, resetPage = true) => set((s) => ({ query: { ...s.query, [k]: v, ...(resetPage ? { page: 1 } : {}) } })),

  _markOptimistic: (eventId) => {
    if (typeof eventId !== 'number' || eventId <= 0) return;
    const markers = new Map(get()._optimisticMarkers);
    markers.set(eventId, Date.now());
    set({ _optimisticMarkers: markers });
  },
  _unmarkOptimistic: (eventId) => {
    if (typeof eventId !== 'number' || eventId <= 0) return;
    const markers = new Map(get()._optimisticMarkers);
    if (markers.delete(eventId)) {
      set({ _optimisticMarkers: markers });
    }
  },
  setAllEvents: (arr) => {
    const OPTIMISTIC_GUARD_MS = 5000;
    const nowMs = Date.now();

    // 架构级修复：增量合并模式（增量 upsert），而非直接全量覆盖！
    // 背景：原实现 set({ allEvents: arr }) 一旦某次 API 拉的范围小（例如老代码 day 视图只拉 1 天），
    //       就会把月/周视图先前已加载到的其他日期事件全部清空，导致切视图后相邻日期空无事件。
    // 方案：把本次拉回的事件（incomingArr）和现有 allEvents（prevArr）按事件 id 取并集：
    //       - 相同 id：用 incomingArr 中的新版本覆盖（保证后端最新变更能同步到前端）
    //       - incomingArr 中没出现的旧事件：一律保留（避免范围拉取导致的"跨视图/跨日期数据被清空"）
    // 另外：软删除过滤——deletedAt 非空的日程不再进入 allEvents（视图不显示，仅垃圾箱可见）
    // ===== 乐观变更保护期 =====
    // 近期（5 秒内）做过乐观修改 / 乐观删除的事件 ID，若 API 拉回的很可能是后端写库 / 主从同步没跟上的旧值，
    // 保留本地版本，防止"刚改完/删完又被旧数据弹回来"。
    const prevArr = get().allEvents ?? [];
    const incomingArr = Array.isArray(arr) ? arr : [];
    const markers = get()._optimisticMarkers;
    // 清理过期标记（>5s），防止 Map 无限增长
    let markersChanged = false;
    const liveMarkers = new Map<number, number>();
    markers.forEach((ts, id) => {
      if (nowMs - ts <= OPTIMISTIC_GUARD_MS) liveMarkers.set(id, ts);
      else markersChanged = true;
    });
    // 用复合 key 去重：重复日程实例（有 recurrenceOriginalStart）用 "id#originalStart" 作为 key，
    // 避免后端展开的所有实例因相同父 id 而被合并成一个。
    const eventKey = (ev: CalendarEventDto): string =>
      ev.recurrenceOriginalStart ? `${ev.id}#${ev.recurrenceOriginalStart}` : `${ev.id}`;
    const byId = new Map<string, CalendarEventDto>();
    for (const ev of prevArr) if (ev && typeof ev.id === 'number') byId.set(eventKey(ev), ev);
    // 修复：当后端重新展开重复日程后（拖拽 updateMode='all' 改变了父事件时间），
    // 新展开实例的 recurrenceOriginalStart 会变化，导致复合 key（id#originalStart）与旧实例不匹配，
    // 旧实例残留在 byId 中不被覆盖，造成旧位置色块残留。
    // 解决：收集 incomingArr 中不在乐观保护期内的重复日程父事件 id，
    // 先移除 byId 中所有相同父 id 的旧展开实例，再添加新的展开实例。
    const recurringParentIdsToReplace = new Set<number>();
    for (const ev of incomingArr) {
      if (ev && typeof ev.id === 'number' && ev.recurrenceOriginalStart) {
        const markerTs = liveMarkers.get(ev.id);
        const inGuard = markerTs != null && nowMs - markerTs <= OPTIMISTIC_GUARD_MS;
        if (!inGuard) recurringParentIdsToReplace.add(ev.id);
      }
    }
    for (const parentId of recurringParentIdsToReplace) {
      const prefix = `${parentId}#`;
      for (const key of byId.keys()) {
        if (key.startsWith(prefix)) byId.delete(key);
      }
      // 关键修复：同时移除父事件 DTO（key = parentId，无 recurrenceOriginalStart）
      // 避免父事件 DTO 与展开实例共存导致同一日期格子出现 2 条色块
      // 场景：optimisticUpsertEvent 追加了父事件 DTO，之后 setAllEvents 拉取展开实例，
      // 两者共存导致 buildEventsByDate 在同一日期格子添加两次事件
      byId.delete(`${parentId}`);
    }
    for (const ev of incomingArr) {
      if (!ev || typeof ev.id !== 'number') continue;
      const id = ev.id;
      // 重复日程的展开实例（有 recurrenceOriginalStart）：乐观保护期内跳过，保留本地版本；
      // 否则用后端最新展开数据覆盖（旧展开实例已在上一步被清除）。
      if (ev.recurrenceOriginalStart) {
        const markerTs = liveMarkers.get(id);
        if (markerTs != null && nowMs - markerTs <= OPTIMISTIC_GUARD_MS) continue;
        byId.set(eventKey(ev), ev);
        continue;
      }
      // 保护期内：保留 prevArr 的本地版本，不用后端值覆盖
      const markerTs = liveMarkers.get(id);
      if (markerTs != null && nowMs - markerTs <= OPTIMISTIC_GUARD_MS) continue;
      byId.set(eventKey(ev), ev);
    }
    let nextArr = Array.from(byId.values()).filter((e) => e.deletedAt == null || e.deletedAt === '');

    // ====== 挂起补丁 replay + 自动清理（刷新后仍保留拖拽结果的核心） ======
    // 1) 读 localStorage pendingPatches（同用户隔离）
    // 2) 遍历：如果后端返回值已经匹配 patch → 说明写库成功并同步了 → drop 掉 patch
    //    否则：把 patch.startAt / endAt 覆盖到 nextArr 对应事件上（"本地暂时替后端记住修改"）
    // 3) 把仍未落地的 patch ID 放进 pendingPatchIds，视图端据此画灰点
    const pending = readPendingPatches();
    const expiredHintIds: number[] = [];
    const stillPendingIds = new Set<number>();
    if (pending.length > 0) {
      let anyPatchApplied = false;
      // 重复日程 sync 检测：incomingArr 中按父 ID 收集所有展开实例，
      // 旧 incomingById（只保留最后一个）会让 sync 检测对重复日程失效（永远 mismatch）。
      // 这里改为：incomingByParentId = 父 ID → 实例数组
      const incomingByParentId = new Map<number, CalendarEventDto[]>();
      for (const ev of incomingArr) {
        if (!ev?.id) continue;
        const arr = incomingByParentId.get(ev.id);
        if (arr) arr.push(ev); else incomingByParentId.set(ev.id, [ev]);
      }
      // 兼容旧逻辑：单事件场景使用 incomingById 取最后一个
      const incomingById = new Map<number, CalendarEventDto>();
      for (const ev of incomingArr) if (ev?.id) incomingById.set(ev.id, ev);

      const stillValidPatches: PendingPatch[] = [];
      for (const patch of pending) {
        // TTL 过期（>10 分钟）：给一个 toast 提示，不再 replay
        if (nowMs - patch.appliedAtMs > PENDING_PATCH_TTL_MS) {
          expiredHintIds.push(patch.eventId);
          continue;
        }

        // ============ 重复日程 patch replay（updateMode='all' 场景） ============
        // 重复日程的 patch.oldStartAt 记录拖拽实例的原始 recurrenceOriginalStart。
        // 后端 updateMode='all' 后会重新展开，所有实例的 recurrenceOriginalStart 都会变化，
        // 因此 oldStartAt 在新展开结果中将不再存在 → 据此判断是否已同步。
        if (patch.oldStartAt && patch.startAt) {
          const incomingInstances = incomingByParentId.get(patch.eventId) ?? [];
          // sync 检测：后端已重新展开 → 任何实例的 recurrenceOriginalStart 都不再等于 oldStartAt
          const stillHasOldInstance = incomingInstances.some(
            (e) => (e.recurrenceOriginalStart ?? '') === patch.oldStartAt,
          );
          if (!stillHasOldInstance && incomingInstances.length > 0) {
            // 后端已同步：所有新展开实例已就位 → drop patch
            continue;
          }
          stillValidPatches.push(patch);

          // 计算 delta = patch.startAt - patch.oldStartAt
          const oldStartMs = new Date(patch.oldStartAt).getTime();
          const newStartMs = new Date(patch.startAt).getTime();
          if (!isFinite(oldStartMs) || !isFinite(newStartMs)) {
            // 时间戳异常：跳过本次 replay，等待下一次
            continue;
          }
          const deltaMs = newStartMs - oldStartMs;

          // 把同父 ID 的所有展开实例一并偏移 delta
          // 关键：以 recurrenceOriginalStart 作为偏移基底（而非当前 startAt）
          //   - 后端返回的原始实例：startAt === recurrenceOriginalStart，偏移后 = originalStart + delta
          //   - 本地已乐观偏移过的实例：startAt !== recurrenceOriginalStart，但仍以 originalStart + delta 为准
          //     避免在 5s 乐观保护期内重复偏移（nextArr 保留了本地偏移版本，再次偏移会 double-shift）
          // - 拖拽实例（recurrenceOriginalStart === oldStartAt）：直接用 patch.startAt / patch.endAt
          // - 其他实例：startAt = originalStart + delta，endAt = originalEnd + delta
          //   其中 originalEnd = originalStart + (instance.endAt - instance.startAt) 即实例原始时长保持不变
          let touched = false;
          for (let i = 0; i < nextArr.length; i++) {
            const e = nextArr[i];
            if (e.id !== patch.eventId) continue;
            // 只处理有 recurrenceOriginalStart 的展开实例（非父事件 DTO）
            if (!e.recurrenceOriginalStart) continue;
            const eOrigStart = new Date(e.recurrenceOriginalStart).getTime();
            if (!isFinite(eOrigStart)) continue;
            // 计算实例原始时长（end - start），保留这个时长不变
            // 用 currentStartAt - recurrenceOriginalStart 反推原始 endAt：
            //   后端展开时 startAt === recurrenceOriginalStart，endAt = startAt + duration
            //   所以 duration = endAt - startAt（如果未被本地修改过）
            //   但本地可能已偏移过，所以用 recurrenceOriginalStart 反推：
            //   originalEnd = eOrigStart + (currentEnd - currentStart) = eOrigStart + duration
            const eCurStart = new Date(e.startAt).getTime();
            const eCurEnd = e.endAt ? new Date(e.endAt).getTime() : null;
            const durationMs = (isFinite(eCurStart) && eCurEnd != null && isFinite(eCurEnd))
              ? (eCurEnd - eCurStart)
              : 0;
            if ((e.recurrenceOriginalStart ?? '') === patch.oldStartAt) {
              // 拖拽实例：用 patch 的精确值
              nextArr[i] = {
                ...e,
                startAt: patch.startAt,
                ...(patch.endAt ? { endAt: patch.endAt } : {}),
              };
            } else {
              // 其他实例：以 originalStart 为基底偏移 delta
              // P2-1: 重命名为 instStartMs 避免遮蔽外层 newStartMs（= patch.startAt 时间戳）
              const instStartMs = eOrigStart + deltaMs;
              const instEndMs = instStartMs + durationMs;
              nextArr[i] = {
                ...e,
                startAt: new Date(instStartMs).toISOString(),
                endAt: e.endAt ? new Date(instEndMs).toISOString() : e.endAt,
              };
            }
            touched = true;
          }
          if (touched) {
            anyPatchApplied = true;
            stillPendingIds.add(patch.eventId);
          } else if (incomingInstances.length > 0) {
            // nextArr 里没有匹配实例（被清理过），用 incoming 实例作为基底再偏移
            for (const e of incomingInstances) {
              if (!e.recurrenceOriginalStart) continue;
              const eOrigStart = new Date(e.recurrenceOriginalStart).getTime();
              if (!isFinite(eOrigStart)) continue;
              const eCurStart = new Date(e.startAt).getTime();
              const eCurEnd = e.endAt ? new Date(e.endAt).getTime() : null;
              const durationMs = (isFinite(eCurStart) && eCurEnd != null && isFinite(eCurEnd))
                ? (eCurEnd - eCurStart)
                : 0;
              const isDragged = (e.recurrenceOriginalStart ?? '') === patch.oldStartAt;
              if (isDragged) {
                // 拖拽实例：用 patch 的精确值覆盖
                nextArr.push({
                  ...e,
                  startAt: patch.startAt,
                  endAt: patch.endAt ?? e.endAt,
                });
              } else {
                // 其他实例：以 originalStart 为基底偏移 delta
                const instStartMs = eOrigStart + deltaMs;
                const instEndMs = instStartMs + durationMs;
                nextArr.push({
                  ...e,
                  startAt: new Date(instStartMs).toISOString(),
                  endAt: e.endAt ? new Date(instEndMs).toISOString() : e.endAt,
                });
              }
            }
            anyPatchApplied = true;
            stillPendingIds.add(patch.eventId);
          }
          continue;
        }

        // ============ 普通事件 patch replay（原逻辑） ============
        const serverEv = incomingById.get(patch.eventId);
        // 后端数据里已能看到 patch 效果 = 已同步 → drop patch
        // P1-6: 用时间戳数值比较替代 ISO 字符串严格比较，避免毫秒精度/Z 后缀差异导致误判
        let synced = false;
        if (serverEv) {
          const startOk = patch.startAt
            ? Math.abs(new Date(serverEv.startAt ?? 0).getTime() - new Date(patch.startAt).getTime()) < 1000
            : true;
          const endOk = patch.endAt
            ? Math.abs(new Date(serverEv.endAt ?? 0).getTime() - new Date(patch.endAt).getTime()) < 1000
            : true;
          synced = startOk && endOk;
        }
        if (synced) {
          // 从持久化移除
          continue;
        }
        stillValidPatches.push(patch);

        // replay: 覆盖到 nextArr 的事件
        const idx = nextArr.findIndex((e) => e.id === patch.eventId);
        if (idx >= 0) {
          const old = nextArr[idx];
          nextArr[idx] = { ...old, ...(patch.startAt ? { startAt: patch.startAt } : {}), ...(patch.endAt ? { endAt: patch.endAt } : {}) };
          anyPatchApplied = true;
          stillPendingIds.add(patch.eventId);
        } else if (serverEv) {
          // nextArr 里被 deletedAt 过滤掉了，但服务器返回里有（比如软删除判断异常）→ 直接推入
          nextArr.push({ ...serverEv, ...(patch.startAt ? { startAt: patch.startAt } : {}), ...(patch.endAt ? { endAt: patch.endAt } : {}) });
          anyPatchApplied = true;
          stillPendingIds.add(patch.eventId);
        }
      }
      // 写回（清理掉 synced / expired 项）
      writePendingPatches(stillValidPatches);

      if (expiredHintIds.length > 0) {
        // 过期提示：只提示一次，避免反复骚扰
        const s = get();
        if (!s.transientToast || !s.transientToast.message.includes('pending')) {
          s.showTransientToast(
            tParams('calendar.store.toast.pendingPatchExpired', { count: expiredHintIds.length }),
            'warning',
            4500,
          );
        }
      }

      if (anyPatchApplied) nextArr = nextArr.filter((e) => e.deletedAt == null || e.deletedAt === '');
    }

    const newMap = buildEventsByDate(nextArr);
    if (markersChanged) {
      set({ allEvents: nextArr, eventsByDate: newMap, _optimisticMarkers: liveMarkers, pendingPatchIds: stillPendingIds });
    } else {
      set({ allEvents: nextArr, eventsByDate: newMap, pendingPatchIds: stillPendingIds });
    }
  },
  optimisticUpsertEvent: (ev) => {
    if (typeof ev.id === 'number' && ev.id > 0) get()._markOptimistic(ev.id);
    // 复合 key：重复日程的展开实例（有 recurrenceOriginalStart）用 id#originalStart，
    // 普通事件用 id。避免相同父 id 的多个展开实例只被替换第一个，导致旧位置色块残留。
    const eventKey = (e: CalendarEventDto): string =>
      e.recurrenceOriginalStart ? `${e.id}#${e.recurrenceOriginalStart}` : `${e.id}`;
    // 如果本次是软删除结果 (deletedAt 非空)，直接从 allEvents 移除，不再渲染到视图
    if (ev.deletedAt != null && ev.deletedAt !== '') {
      const prev = get().allEvents;
      // 软删除时移除所有相同 id 的实例（重复日程的多个展开实例一并清理）
      const next = prev.filter((x) => x.id !== ev.id);
      const map = buildEventsByDate(next);
      // 同步：从 list 中移除该事件（列表视图乐观更新）
      const prevList = get().list;
      const nextList = prevList
        ? { ...prevList, data: prevList.data.filter((x) => x.id !== ev.id), totalItems: Math.max(0, prevList.totalItems - 1) }
        : prevList;
      // 同步：从 overview.upcomingEvents 中移除（概览视图乐观更新）
      const prevOv = get().overview;
      const nextOv = prevOv
        ? { ...prevOv, upcomingEvents: prevOv.upcomingEvents.filter((x) => x.id !== ev.id) }
        : prevOv;
      set({ allEvents: next, eventsByDate: map, list: nextList, overview: nextOv });
      return;
    }
    const prev = get().allEvents;
    // 用复合 key 查找：精确匹配当前实例（避免误替换同 id 的其他展开实例）
    const targetKey = eventKey(ev);
    let idx = prev.findIndex((x) => eventKey(x) === targetKey);
    // 关键修复：如果精确 key 未匹配，检查同 id 的旧实例，替换而非追加，防止色块重复。
    // - 重复日程展开实例（有 recurrenceOriginalStart）：后端重新展开后 key 可能变化
    // - 父事件 DTO（无 recurrenceOriginalStart）：onChanged 应触发全量刷新，但作为兜底也替换
    // - 单次事件：找同 id 实例替换
    if (idx < 0) {
      idx = prev.findIndex((x) => x.id === ev.id);
    }
    const next = idx >= 0
      ? (() => { const c = [...prev]; c[idx] = ev; return c; })()
      : [...prev, ev];
    const map = buildEventsByDate(next);
    // 同步：更新 list 中的对应事件（如果存在），保持列表视图与日历视图一致
    const prevList = get().list;
    let nextList = prevList;
    if (prevList && Array.isArray(prevList.data)) {
      const listIdx = prevList.data.findIndex((x) => eventKey(x) === targetKey);
      if (listIdx >= 0) {
        const newData = [...prevList.data];
        newData[listIdx] = ev;
        nextList = { ...prevList, data: newData };
      } else {
        // P1-7: 追加前先检查同 id 的其他展开实例，避免重复日程实例在 list 中重复
        const sameIdIdx = prevList.data.findIndex((x) => x.id === ev.id);
        if (sameIdIdx >= 0) {
          const newData = [...prevList.data];
          newData[sameIdIdx] = ev;
          nextList = { ...prevList, data: newData };
        } else {
          nextList = { ...prevList, data: [ev, ...prevList.data], totalItems: prevList.totalItems + 1 };
        }
      }
    }
    // 同步：更新 overview.upcomingEvents 中的对应事件（概览视图乐观更新）
    const prevOv = get().overview;
    let nextOv = prevOv;
    if (prevOv && Array.isArray(prevOv.upcomingEvents)) {
      let ovIdx = prevOv.upcomingEvents.findIndex((x) => eventKey(x) === targetKey);
      if (ovIdx < 0 && ev.recurrenceOriginalStart) {
        ovIdx = prevOv.upcomingEvents.findIndex((x) => x.id === ev.id && x.recurrenceOriginalStart != null);
      }
      if (ovIdx >= 0) {
        const newOv = [...prevOv.upcomingEvents];
        newOv[ovIdx] = ev;
        nextOv = { ...prevOv, upcomingEvents: newOv };
      }
    }
    set({ allEvents: next, eventsByDate: map, list: nextList, overview: nextOv });
  },
  getEventsByYMD: (ymd) => {
    const raw = get().eventsByDate.get(ymd) ?? _EMPTY_ARR;
    const enabledTypes = get().enabledEventTypes;
    const enabledMb = get().enabledMailboxIds;
    const hasType = enabledTypes.size > 0;
    const hasMb = enabledMb.size > 0;
    if (!hasType && !hasMb) return raw;
    return raw.filter((ev) => {
      if (hasType && !enabledTypes.has(ev.eventType)) return false;
      if (hasMb && ev.mailboxId != null && !enabledMb.has(ev.mailboxId)) return false;
      return true;
    });
  },
  getEventsCountByYMD: (ymd) => get().getEventsByYMD(ymd).length,

  openCreate: (date) => {
    const pad = (n: number) => String(n).padStart(2, '0');
    const base = date ?? new Date();
    const startAt = new Date(base);
    // 无论是否传入具体日期（月/周/天视图双击格子或全局新建），都默认设为对应日期 09:00
    startAt.setHours(9, 0, 0, 0);
    const isoStart = `${startAt.getFullYear()}-${pad(startAt.getMonth() + 1)}-${pad(startAt.getDate())}T${pad(startAt.getHours())}:${pad(startAt.getMinutes())}`;
    const endAt = new Date(startAt);
    endAt.setHours(endAt.getHours() + 1);
    const isoEnd = `${endAt.getFullYear()}-${pad(endAt.getMonth() + 1)}-${pad(endAt.getDate())}T${pad(endAt.getHours())}:${pad(endAt.getMinutes())}`;
    const COLOR_PALETTE = ['#3b82f6', '#10b981', '#f59e0b', '#8b5cf6', '#ec4899', '#14b8a6', '#f97316', '#6366f1', '#ef4444', '#06b6d4'];
    const randomColor = COLOR_PALETTE[Math.floor(Math.random() * COLOR_PALETTE.length)];
    // 默认负责人：当前登录用户
    const currentUserId = _getUserId() ?? null;
    const baseForm: EditorForm = {
      ...EMPTY_FORM,
      eventType: 'task',
      startAt: isoStart,
      endAt: isoEnd,
      isAllDay: false,
      color: randomColor,
      status: 'pending',
      userId: currentUserId,
      location: '',
      attendees: [],
      reminderMinutesBefore: 15,
      progress: 0,
      isRecurring: false,
      recurrenceType: null,
    };
    if (baseForm.eventType === 'task') {
      baseForm.dueAt = toIsoString(isoEnd);
      baseForm.eventPriority = 'MEDIUM';
      // taskOwner 默认为登录用户的 ID 字符串
      baseForm.taskOwner = currentUserId != null ? String(currentUserId) : undefined;
    }
    if (baseForm.eventType === 'reminder') {
      // 统一使用扁平字段：底部手风琴为唯一提醒设置入口。
      // T000090：默认站内 + 邮件组合提醒（与 EditorPanel handleEventTypeChange 切换到 reminder 一致）
      baseForm.reminderMinutesBefore = 10;
      baseForm.reminderType = 'popup,email';
    }
    set({
      showEditor: true,
      editing: null,
      form: baseForm,
      saving: false,
      formError: null,
      _attendeeEmailList: [],
    });
  },
  openEdit: (ev) => {
    const evExtra = ev as unknown as { extras?: Record<string, unknown> | null };
    const extras: Record<string, unknown> = evExtra.extras ?? {};
    // 全天事件用 date 格式（YYYY-MM-DD），非全天事件用 datetime-local 格式（YYYY-MM-DDTHH:MM），
    // 必须与 <input type="date"|"datetime-local"> 期望的 value 格式匹配，否则浏览器显示异常。
    const isAllDay = ev.isAllDay ?? false;
    const startAtConverter = isAllDay ? fromIsoToDateInput : fromIsoToLocalInput;
    const baseForm: EditorForm = {
      ...EMPTY_FORM,
      title: ev.title ?? '',
      description: ev.description ?? '',
      eventType: ev.eventType ?? 'task',
      startAt: startAtConverter(ev.startAt),
      endAt: ev.endAt ? (isAllDay ? fromIsoToDateInput(ev.endAt) : fromIsoToLocalInput(ev.endAt)) : '',
      isAllDay,
      mailboxId: ev.mailboxId ?? null,
      userId: ev.userId ?? null,
      color: ev.color ?? '#3b82f6',
      status: ev.status ?? 'pending',
      priority: ev.priority ?? null,
      progress: ev.progress ?? null,
      location: ev.location ?? '',
      meetingUrl: ev.meetingUrl ?? '',
      attendees: ev.attendees ?? [],
      subTasks: ev.subTasks ?? [],
      relatedEventId: ev.relatedEventId ?? null,
      recurrenceExclusions: ev.recurrenceExclusions ?? null,
      isRecurring: ev.isRecurring ?? false,
      recurrenceType: ev.recurrenceType ?? null,
      recurrenceInterval: ev.recurrenceInterval ?? 1,
      recurrenceEndDate: ev.recurrenceEndDate ?? '',
      recurrenceCount: ev.recurrenceCount ?? null,
      reminderMinutesBefore: ev.reminderMinutes ?? null,
      reminderEmails: ev.reminderEmails ?? '',
      // T000097: 编辑现有事件时，根据 reminderMinutes 是否在预设列表中初始化自定义模式标志
      isCustomReminder:
        ev.reminderMinutes != null &&
        !REMINDER_OPTIONS.some((o) => o.value === ev.reminderMinutes),
    };
    const filledForm: EditorForm = {
      ...baseForm,
      meetingLink: typeof extras.meetingLink === 'string' ? extras.meetingLink : undefined,
      // ====== 2026-08-17：7 个事件类型专属字段从 DTO 直接回填 ======
      // 主持人用户 ID 数组（会议类型）：优先 DTO 字段，回退 extras 旧数据
      hostUserIds: Array.isArray(ev.hostUserIds)
        ? ev.hostUserIds.filter((v): v is number => typeof v === 'number')
        : Array.isArray(extras.hostUserIds)
          ? extras.hostUserIds.filter((v): v is number => typeof v === 'number')
          : undefined,
      // 议程 ID 数组（会议类型）
      agendaIds: Array.isArray(ev.agendaIds)
        ? ev.agendaIds.filter((v): v is number => typeof v === 'number')
        : Array.isArray(extras.agendaIds)
          ? extras.agendaIds.filter((v): v is number => typeof v === 'number')
          : undefined,
      // 任务负责人 ID（任务类型，DTO 是 number，UI 存字符串）
      taskOwner:
        (typeof ev.taskOwnerId === 'number' && ev.taskOwnerId > 0)
          ? String(ev.taskOwnerId)
          : typeof extras.taskOwner === 'string' ? extras.taskOwner : undefined,
      // 任务截止时间（任务类型）
      dueAt: typeof ev.dueAt === 'string'
        ? ev.dueAt
        : typeof extras.dueAt === 'string' ? extras.dueAt : undefined,
      // 每周哪几天（重复日程面板，0=周日..6=周六）
      weekdaysSelected: Array.isArray(ev.weekdaysSelected)
        ? ev.weekdaysSelected.filter((v): v is number => typeof v === 'number')
        : Array.isArray(extras.weekdaysSelected)
          ? extras.weekdaysSelected.filter((v): v is number => typeof v === 'number')
          : [],
      // RRULE 原始字符串（重复日程面板）
      recurrenceRule: typeof ev.recurrenceRule === 'string'
        ? ev.recurrenceRule
        : typeof extras.recurrenceRule === 'string' ? extras.recurrenceRule : '',
      eventPriority: (() => {
        // Bug fix: 后端返回的 priority 是数字（1=LOW,2=MEDIUM,3=HIGH,4=URGENT），
        // UI 的 eventPriority 是枚举字符串。这里做反向映射，否则重新打开编辑器时
        // 单选按钮恒显示默认 'MEDIUM'，看起来像"没有持久化"。
        const PRIORITY_NUM_TO_ENUM: Record<number, CalendarEventPriority> = {
          1: 'LOW',
          2: 'MEDIUM',
          3: 'HIGH',
          4: 'URGENT',
        };
        if (typeof ev.priority === 'number' && ev.priority in PRIORITY_NUM_TO_ENUM) {
          return PRIORITY_NUM_TO_ENUM[ev.priority];
        }
        // 兼容旧数据：extras.priority 曾以字符串形式存储
        if (
          typeof extras.priority === 'string' &&
          ['LOW', 'MEDIUM', 'HIGH', 'URGENT'].includes(extras.priority)
        ) {
          return extras.priority as CalendarEventPriority;
        }
        return undefined;
      })(),
      // 提醒方式（T000090：多选组合 popup,email,sms 逗号分隔）：从 DTO reminderType 字段回填
      reminderType: (() => {
        const parsed = parseReminderTypes(typeof ev.reminderType === 'string' ? ev.reminderType : null);
        if (parsed.length > 0) return parsed.join(',');
        // 兼容旧数据：从 extras.reminderSettings.type 回填单值
        const s = extras.reminderSettings;
        if (typeof s === 'object' && s !== null) {
          const raw = s as Record<string, unknown>;
          if (typeof raw.type === 'string') {
            const legacy = parseReminderTypes(raw.type);
            if (legacy.length > 0) return legacy[0];
          }
        }
        return null;
      })(),
      // 提醒方式（提醒类型）：优先 DTO reminderType 字段回填 reminderSettings.type（legacy 兼容展示，取组合首项）
      reminderSettings: (() => {
        const s = extras.reminderSettings;
        const dtoParsed = parseReminderTypes(typeof ev.reminderType === 'string' ? ev.reminderType : null);
        const dtoReminderType = dtoParsed.length > 0 ? dtoParsed[0] : null;
        if (typeof s === 'object' && s !== null) {
          const raw = s as Record<string, unknown>;
          const type = dtoReminderType
            ?? (typeof raw.type === 'string' && (parseReminderTypes(raw.type).length > 0)
                ? parseReminderTypes(raw.type)[0]
                : 'popup');
          const minutesAhead = Math.max(0, Math.min(20160, Number(raw.minutesAhead) || 0));
          return { type, minutesAhead };
        }
        if (dtoReminderType) {
          // 旧数据无 reminderSettings 但 DTO 有 reminderType，用 reminderMinutes 作 minutesAhead
          const minutesAhead = Math.max(0, Math.min(20160, ev.reminderMinutes ?? 10));
          return { type: dtoReminderType, minutesAhead };
        }
        return undefined;
      })(),
    };
    // openEdit 时：从 DTO reminderEmails 字段回填参会人邮箱列表（CSV 形式）。
    // 合并策略：前端内存态 extras._attendeeEmailList（存在且非空时）与后端持久化 ev.reminderEmails
    // 做去重并集，避免"用户刚点保存后立刻重打开（DTO 尚未刷新）看到空"或"仅靠 extras 不回填数据库字段"
    // 两类 round-trip 丢数据问题。
    const attendeeEmailsFromDto = typeof ev.reminderEmails === 'string' && ev.reminderEmails.trim()
      ? ev.reminderEmails.split(/[,;\s]+/).map((s) => s.trim()).filter((s) => s.length > 0 && /\S+@\S+\.\S+/.test(s))
      : [];
    const attendeeEmailsFromExtras = Array.isArray(extras._attendeeEmailList)
      ? extras._attendeeEmailList.filter((s: unknown): s is string => typeof s === 'string' && s.length > 0 && /\S+@\S+\.\S+/.test(s))
      : [];
    // 去重并集，稳定序：extras 在前（用户最近一次 UI 行为），DTO 补齐
    const merged: string[] = [];
    for (const e of [...attendeeEmailsFromExtras, ...attendeeEmailsFromDto]) {
      if (!merged.includes(e)) merged.push(e);
    }
    set({
      showEditor: true,
      editing: ev,
      saving: false,
      formError: null,
      form: filledForm,
      _attendeeEmailList: merged,
    });
  },
  closeEditor: () => set({
    showEditor: false, editing: null, saving: false, formError: null,
    _attendeeEmailList: [],
  }),
  resetForm: () => set({ saving: false, formError: null, form: { ...EMPTY_FORM }, _attendeeEmailList: [] }),
  setFormField: (k, v) => set((s) => ({ form: { ...s.form, [k]: v } })),
  setViewTarget: (v) => set({ viewTarget: v }),
  openTrash: () => set({ showTrash: true }),
  openSubscription: () => {
    set({ showSubscription: true, subscriptionLoading: true, subscriptionUrl: '' });
    // 打开面板时自动从后端拉取已存在的订阅链接（避免显示 mock URL 占位符）
    queueMicrotask(async () => {
      try {
        const result = await calendarApi.getSubscription();
        useCalendarStore.setState({
          subscriptionUrl: result?.url ?? '',
          subscriptionLoading: false,
        });
      } catch {
        useCalendarStore.setState({ subscriptionLoading: false });
      }
    });
  },
  openOutlookSync: () => {
    set({ showOutlookSync: true });
    // 打开面板时自动从后端拉取同步状态和设置
    queueMicrotask(() => get().fetchOutlookStatus());
  },
  fetchOutlookStatus: async () => {
    set({ outlookStatusLoading: true });
    try {
      const status = await outlookSyncApi.getStatus();
      // 同步设置可能 404（用户从未配置过），用 catch 兜底
      const setting = await outlookSyncApi.getSetting().catch(() => null);
      const dir = setting?.syncDirection === 'to_outlook'
        ? 'noneads_to_outlook'
        : setting?.syncDirection === 'from_outlook'
          ? 'outlook_to_noneads'
          : 'bidirectional';
      set({
        outlookStatus: status,
        outlookStatusLoading: false,
        outlookDirection: dir,
        outlookSyncEnabled: setting?.syncEnabled ?? false,
      });
    } catch {
      set({ outlookStatusLoading: false });
      get().showTransientToast?.(tBound('calendar.store.toast.outlookStatusFailed'), 'error', 2400);
    }
  },
  connectOutlook: async () => {
    set({ outlookConnecting: true });
    try {
      const { authorizeUrl } = await outlookSyncApi.getAuthorizeUrl();
      set({ outlookConnecting: false });
      // 重定向到微软 OAuth 授权页，授权后回调会带 code+state 返回
      window.location.href = authorizeUrl;
    } catch (e) {
      set({ outlookConnecting: false });
      // T000099: 后端在 Azure AD 未配置时返回 400 并附带具体缺失字段。
      // 优先展示后端返回的 message；若无则回退到通用错误提示。
      const status = getErrorStatus(e);
      const backendMsg = getErrorMessage(e) ?? getErrorData<{ message?: string }>(e)?.message;
      const toastMsg = status === 400
        ? (backendMsg ?? tBound('calendar.store.toast.outlookNotConfigured'))
        : (backendMsg ?? tBound('calendar.store.toast.outlookAuthorizeUrlFailed'));
      get().showTransientToast?.(toastMsg, 'error', status === 400 ? 4000 : 2400);
    }
  },
  disconnectOutlook: async () => {
    set({ outlookDisconnecting: true });
    try {
      await outlookSyncApi.disconnect();
      set({ outlookDisconnecting: false, outlookStatus: null, outlookSyncResult: null });
      get().showTransientToast?.(tBound('calendar.store.toast.outlookDisconnected'), 'info', 2000);
    } catch {
      set({ outlookDisconnecting: false });
      get().showTransientToast?.(tBound('calendar.store.toast.outlookDisconnectFailed'), 'error', 2400);
    }
  },
  saveOutlookSetting: async () => {
    set({ outlookSaving: true });
    try {
      const st = get();
      const direction = st.outlookDirection === 'noneads_to_outlook'
        ? 'to_outlook'
        : st.outlookDirection === 'outlook_to_noneads'
          ? 'from_outlook'
          : 'both';
      await outlookSyncApi.updateSetting({
        syncEnabled: st.outlookSyncEnabled,
        syncDirection: direction,
      });
      set({ outlookSaving: false });
      get().showTransientToast?.(tBound('calendar.store.toast.outlookSettingSaved'), 'success', 2000);
    } catch {
      set({ outlookSaving: false });
      get().showTransientToast?.(tBound('calendar.common.saveConfigFailed'), 'error', 2400);
    }
  },
  syncOutlookNow: async () => {
    set({ outlookSyncing: true, outlookSyncResult: null });
    try {
      const result = await outlookSyncApi.syncNow(false);
      const syncedAt = result.syncedAt || new Date().toISOString();
      const prev = get().outlookStatus;
      set({
        outlookSyncing: false,
        outlookSyncResult: result,
        outlookStatus: {
          isConfigured: prev?.isConfigured ?? true,
          isConnected: prev?.isConnected ?? true,
          syncEnabled: prev?.syncEnabled ?? false,
          syncDirection: prev?.syncDirection ?? 'both',
          outlookEmail: prev?.outlookEmail ?? null,
          lastSyncAt: syncedAt,
          lastSyncStatus: result.success ? 'success' : 'failed',
          lastSyncError: result.errorMessage,
          isSyncing: false,
        },
      });
      get().showTransientToast?.(
        tParams('calendar.store.toast.outlookSyncDone', {
          pushed: result.pushedToOutlook,
          pulled: result.pulledFromOutlook,
          conflicts: result.conflicts,
        }),
        result.success ? 'success' : 'error',
        3200,
      );
    } catch {
      set({ outlookSyncing: false });
      get().showTransientToast?.(tBound('calendar.common.syncFailed'), 'error', 2400);
    }
  },
  setShowTrash: (v) => set({ showTrash: v }),
  setTrashFilter: (keyword, page, _pageSize) => {
    set({ trashKeyword: keyword ?? get().trashKeyword, trashPage: page ?? 1 });
    // 触发实际加载
    queueMicrotask(() => get().loadTrashAsync());
  },
  setRestoreTarget: (ev) => set({ restoreTarget: ev }),
  setHardDeleteTarget: (ev) => set({ hardDeleteTarget: ev }),
  setPurgeDialog: (open, retentionDays) => set({ showPurgeTrashConfirm: open, purgeRetentionDays: retentionDays ?? get().purgeRetentionDays }),
  loadTrashAsync: async () => {
    const s = get();
    set({ trashLoading: true });
    try {
      const resp = await calendarApi.getTrash({ page: s.trashPage, keyword: s.trashKeyword || undefined });
      set({ trashList: resp.data ?? [], trashCount: resp.totalItems ?? 0, trashLoading: false });
    } catch (e: unknown) {
      set({ trashLoading: false, formError: getErrorMessage(e) || tBound('common.loadTrashFailed') });
    }
  },
  refreshTrashCount: async () => {
    try {
      const resp = await calendarApi.getTrashCount();
      const n = typeof resp === 'number' ? resp : (resp && typeof resp === 'object' && 'count' in resp ? resp.count : 0);
      set({ trashCount: Math.max(0, Number.isFinite(n) ? n : 0) });
    } catch (e: unknown) {
      // 垃圾箱红点徽标是辅助提示，失败静默即可，不打扰用户主流程
      void e;
    }
  },
  restoreAsync: async (id) => {
    set({ restoring: true });
    // 从 trashList / restoreTarget / hardDeleteTarget 中取存在事件的 rowVersion（如果有）
    const rowVersion = (() => {
      const fromTrash = get().trashList.find((e) => e.id === id);
      if (fromTrash?.rowVersion) return fromTrash.rowVersion;
      const restore = get().restoreTarget;
      if (restore?.id === id && restore.rowVersion) return restore.rowVersion;
      const hard = get().hardDeleteTarget;
      if (hard?.id === id && hard.rowVersion) return hard.rowVersion;
      return undefined;
    })();
    try {
      await calendarApi.restore(id, rowVersion ?? null);
      set((s) => ({
        restoring: false,
        restoreTarget: null,
        trashList: s.trashList.filter((e) => e.id !== id),
        trashCount: Math.max(0, s.trashCount - 1),
        // 立即 bump signalRRev：触发列表/日历视图刷新，让恢复的事件马上显示
        signalRRev: s.signalRRev + 1,
      }));
    } catch (e: unknown) {
      const rollbackData = getErrorData<ApiErrorResponseBody>(e);
      const conflictLatest = rollbackData?.latest ?? rollbackData?.data?.latest;
      // P3: 409 并发冲突 → 自动拉一次垃圾箱刷新最新 rowVersion，提示用户再点一次
      if (getErrorStatus(e) === 409) {
        if (conflictLatest) {
          get().showTransientToast(
            tBound('calendar.store.toast.staleRestoreRetry'),
            'warning',
            4200,
          );
        } else {
          get().showTransientToast(
            rollbackData?.message || tBound('calendar.store.toast.staleRestoreRetryShort'),
            'warning',
            4200,
          );
        }
        // 冲突时立刻刷新垃圾箱列表/计数，拿到最新 rowVersion
        await Promise.all([get().loadTrashAsync(), get().refreshTrashCount()]).catch(() => void 0);
      } else {
        set({ restoring: false, formError: getErrorMessage(e) || tBound('calendar.store.toast.restoreFailed') });
      }
      throw e;
    }
  },
  batchRestoreAsync: async (ids) => {
    const targetIds = Array.isArray(ids) ? ids : [ids];
    if (targetIds.length === 0) return;
    set({ batchRestoring: true });
    try {
      const res = await calendarApi.batchOperation({ ids: targetIds, op: 'restore' });
      const failedIds = new Set((res.failures ?? []).map((f) => f.id));
      const restoredIds = targetIds.filter((id) => !failedIds.has(id));
      set((s) => ({
        batchRestoring: false,
        trashList: s.trashList.filter((e) => !restoredIds.includes(e.id)),
        trashCount: Math.max(0, s.trashCount - restoredIds.length),
        // 立即 bump signalRRev：触发列表/日历视图刷新，让恢复的事件马上显示
        signalRRev: s.signalRRev + 1,
      }));
      const failed = res.failures ?? [];
      if (res.successCount > 0) {
        get().showTransientToast?.(tParams('calendar.store.toast.restoredCount', { count: res.successCount }), 'success', 2600);
      } else {
        get().showTransientToast?.(
          failed.length > 0
            ? tBound('calendar.store.toast.someEventsNotRestored')
            : tBound('calendar.store.toast.noRestorableEvents'),
          'warning',
          2400,
        );
      }
      if (failed.length > 0) {
        const first = failed[0]!;
        const reason = first.reason || first.message || tBound('calendar.store.toast.unknownReason');
        get().showTransientToast?.(
          tParams('calendar.store.toast.eventsNotRestored', { count: failed.length, reason }),
          'error',
          3800,
        );
        // 部分失败（如父事件已被删除）→ 刷新垃圾箱以同步最新状态
        await Promise.all([get().loadTrashAsync(), get().refreshTrashCount()]).catch(() => void 0);
      }
    } catch (e: unknown) {
      set({ batchRestoring: false, formError: getErrorMessage(e) || tBound('calendar.store.toast.batchRestoreFailed') });
      get().showTransientToast?.(getErrorMessage(e) || tBound('calendar.store.toast.batchRestoreFailed'), 'error', 2600);
      throw e;
    }
  },
  hardDeleteAsync: async (id) => {
    set({ hardDeleting: true });
    const rowVersion = (() => {
      const fromTrash = get().trashList.find((e) => e.id === id);
      if (fromTrash?.rowVersion) return fromTrash.rowVersion;
      const hard = get().hardDeleteTarget;
      if (hard?.id === id && hard.rowVersion) return hard.rowVersion;
      const restore = get().restoreTarget;
      if (restore?.id === id && restore.rowVersion) return restore.rowVersion;
      return undefined;
    })();
    try {
      await calendarApi.hardDelete(id, rowVersion ?? null);
      set((s) => ({
        hardDeleting: false,
        hardDeleteTarget: null,
        trashList: s.trashList.filter((e) => e.id !== id),
        trashCount: Math.max(0, s.trashCount - 1),
      }));
    } catch (e: unknown) {
      const rollbackData = getErrorData<ApiErrorResponseBody>(e);
      if (getErrorStatus(e) === 409) {
        get().showTransientToast(
          rollbackData?.message || tBound('calendar.store.toast.staleHardDeleteRetry'),
          'warning',
          4200,
        );
        await Promise.all([get().loadTrashAsync(), get().refreshTrashCount()]).catch(() => void 0);
        set({ hardDeleting: false });
      } else {
        set({ hardDeleting: false, formError: getErrorMessage(e) || tBound('calendar.store.toast.hardDeleteFailed') });
      }
      throw e;
    }
  },
  clearTrashAsync: async () => {
    set({ clearingTrash: true });
    try {
      await calendarApi.clearTrash();
      set((s) => ({
        clearingTrash: false,
        showClearTrashConfirm: false,
        trashList: [],
        trashCount: 0,
        // 立即 bump signalRRev：触发日历视图重新拉取事件列表，确保已物理删除的事件从 allEvents 中彻底清除。
        // 即使后端 CalendarRefresh SignalR 推送已覆盖此场景，本地 bump 作为双保险，防止 SignalR 连接异常时事件残留。
        signalRRev: s.signalRRev + 1,
      }));
    } catch (e: unknown) {
      set({ clearingTrash: false, formError: getErrorMessage(e) || tBound('calendar.store.toast.clearTrashFailed') });
      throw e;
    }
  },
  purgeTrashAsync: async (retentionDays) => {
    set({ purgingTrash: true });
    try {
      const days = retentionDays ?? get().purgeRetentionDays;
      await calendarApi.purgeTrash(days);
      set({ purgingTrash: false, showPurgeTrashConfirm: false });
      // 重新加载垃圾箱数据，因为 purge 只删除过期的
      await get().loadTrashAsync();
    } catch (e: unknown) {
      set({ purgingTrash: false, formError: getErrorMessage(e) || tBound('calendar.store.toast.purgeTrashFailed') });
      throw e;
    }
  },

  setDragOver: (k) => set({ dragOverDateKey: k }),
  setDragPreviewPos: (pos) => set({ dragPreviewPos: pos }),
  startDrag: (ev) => set({ draggedEvent: ev, dragging: true, isDragging: true, dragPreviewPos: null }),
  endDrag: () => {
    const { pendingSignalRBump } = get();
    set({ draggedEvent: null, dragOverDateKey: null, dragging: false, isDragging: false, dragPreviewPos: null,
      pendingSignalRBump: false });
    // 移动日程成功后：立即触发 API 刷新，确保后端数据同步到前端。
    // optimisticUpsertEvent 已先提供即时反馈，5 秒乐观保护期保护本地版本不被后端写库延迟覆盖。
    if (pendingSignalRBump) {
      useCalendarStore.setState((s) => ({ signalRRev: s.signalRRev + 1 }));
    }
  },
  moveEventByDrop: async (targetYmd, targetHourFloat) => {
    const ev = get().draggedEvent;
    if (!ev || !ev.id) return;
    const pad = (n: number) => String(n).padStart(2, '0');
    const oldStart = new Date(ev.startAt);
    if (isNaN(oldStart.getTime())) return;
    const oldEnd = ev.endAt ? new Date(ev.endAt) : null;
    const durationMs = oldEnd ? oldEnd.getTime() - oldStart.getTime() : 0;

    // 构造新 startAt
    let newStart: Date;
    if (ev.isAllDay) {
      newStart = new Date(`${targetYmd}T00:00:00`);
    } else if (typeof targetHourFloat === 'number') {
      // DayView/WeekView：小数小时（含分钟吸附，如 10.25 = 10:15）
      const h = Math.floor(targetHourFloat);
      const m = Math.round((targetHourFloat - h) * 60);
      newStart = new Date(`${targetYmd}T${pad(h)}:${pad(m)}:00`);
    } else {
      // MonthView：保留原 HH:MM，只换日期
      newStart = new Date(`${targetYmd}T${pad(oldStart.getHours())}:${pad(oldStart.getMinutes())}:00`);
    }
    if (isNaN(newStart.getTime())) return;
    const newStartIso = newStart.toISOString();
    const newEndIso = oldEnd ? new Date(newStart.getTime() + durationMs).toISOString() : null;
    const optimisticEvent: CalendarEventDto = { ...ev, startAt: newStartIso, endAt: newEndIso ?? ev.endAt };

    // ========== 乐观更新：先立即刷新本地状态，确保松手后位置立刻变化 ==========
    // 用 optimisticUpsertEvent 替代手动 map/set，除了立即更新视图还会自动打上乐观变更标记（_markOptimistic），
    // 后续 setAllEvents 在 5 秒保护期内不会用后端旧数据覆盖它。
    // 快照：allEvents + eventsByDate + list + overview（四视图数据源，失败时整体回滚）
    const prevAllEvents = get().allEvents;
    const prevEventsByDate = get().eventsByDate;
    const prevList = get().list;
    const prevOverview = get().overview;
    get().optimisticUpsertEvent(optimisticEvent);

    // ========== 重复日程 updateMode='all' 的乐观联动：拖拽父事件一个实例 = 全系列平移 ==========
    // 背景：后端 updateMode='all' 会把所有展开实例统一偏移 delta。但 optimisticUpsertEvent 只替换了
    // 被拖拽的那一个实例（按 id#originalStart 复合 key），其余实例仍停留在原位 → 视觉上
    // 新位置色块与原位置其他实例色块共存，呈现"2 条相同色块 + 冲突红点"的错误状态。
    // 修复：识别重复日程（isRecurring + 多个同 id 展开实例），把其余实例的 startAt/endAt 也按 delta 偏移。
    if (ev.isRecurring) {
      const deltaMs = newStart.getTime() - oldStart.getTime();
      if (deltaMs !== 0) {
        const curAll = get().allEvents;
        let mutated = false;
        const nextAll = curAll.map((e) => {
          // 仅处理同父 ID 的其他展开实例（排除当前被拖拽实例，因为已被 optimisticUpsertEvent 更新）
          if (e.id !== ev.id) return e;
          if (!e.recurrenceOriginalStart) return e;
          // 跳过被拖拽实例本身（recurrenceOriginalStart === ev.recurrenceOriginalStart）
          if (ev.recurrenceOriginalStart && (e.recurrenceOriginalStart ?? '') === (ev.recurrenceOriginalStart ?? '')) return e;
          // 其他实例：按 delta 偏移 startAt / endAt（保留 recurrenceOriginalStart 不变）
          const eStart = new Date(e.startAt).getTime();
          const eEnd = e.endAt ? new Date(e.endAt).getTime() : null;
          if (!isFinite(eStart)) return e;
          mutated = true;
          return {
            ...e,
            startAt: new Date(eStart + deltaMs).toISOString(),
            endAt: eEnd != null && isFinite(eEnd) ? new Date(eEnd + deltaMs).toISOString() : e.endAt,
          };
        });
        if (mutated) {
          const nextMap = buildEventsByDate(nextAll);
          // 同步：list 中其他实例也要联动偏移
          const curList = get().list;
          let nextList = curList;
          if (curList && Array.isArray(curList.data)) {
            let listMutated = false;
            const newListData = curList.data.map((e) => {
              if (e.id !== ev.id) return e;
              if (!e.recurrenceOriginalStart) return e;
              if (ev.recurrenceOriginalStart && (e.recurrenceOriginalStart ?? '') === (ev.recurrenceOriginalStart ?? '')) return e;
              const eStart = new Date(e.startAt).getTime();
              const eEnd = e.endAt ? new Date(e.endAt).getTime() : null;
              if (!isFinite(eStart)) return e;
              listMutated = true;
              return {
                ...e,
                startAt: new Date(eStart + deltaMs).toISOString(),
                endAt: eEnd != null && isFinite(eEnd) ? new Date(eEnd + deltaMs).toISOString() : e.endAt,
              };
            });
            if (listMutated) nextList = { ...curList, data: newListData };
          }
          // P1-2: 同步 overview.upcomingEvents 中其他实例
          const curOv = get().overview;
          let nextOv = curOv;
          if (curOv && Array.isArray(curOv.upcomingEvents)) {
            let ovMutated = false;
            const newOvEvents = curOv.upcomingEvents.map((e) => {
              if (e.id !== ev.id) return e;
              if (!e.recurrenceOriginalStart) return e;
              if (ev.recurrenceOriginalStart && (e.recurrenceOriginalStart ?? '') === (ev.recurrenceOriginalStart ?? '')) return e;
              const eStart = new Date(e.startAt).getTime();
              const eEnd = e.endAt ? new Date(e.endAt).getTime() : null;
              if (!isFinite(eStart)) return e;
              ovMutated = true;
              return {
                ...e,
                startAt: new Date(eStart + deltaMs).toISOString(),
                endAt: eEnd != null && isFinite(eEnd) ? new Date(eEnd + deltaMs).toISOString() : e.endAt,
              };
            });
            if (ovMutated) nextOv = { ...curOv, upcomingEvents: newOvEvents };
          }
          set({ allEvents: nextAll, eventsByDate: nextMap, list: nextList, overview: nextOv });
        }
      }
    }

    try {
      const res = await calendarApi.update(ev.id, {
        id: ev.id,
        title: ev.title,
        description: ev.description ?? null,
        eventType: ev.eventType,
        startAt: newStartIso,
        endAt: newEndIso,
        isAllDay: ev.isAllDay,
        mailboxId: ev.mailboxId ?? null,
        userId: ev.userId ?? null,
        color: ev.color ?? null,
        status: ev.status,
        isRecurring: ev.isRecurring ?? false,
        recurrenceType: ev.recurrenceType ?? null,
        recurrenceInterval: ev.recurrenceInterval ?? null,
        recurrenceEndDate: ev.recurrenceEndDate ?? null,
        recurrenceCount: ev.recurrenceCount ?? null,
        reminderMinutes: ev.reminderMinutes ?? null,
        reminderEmails: ev.reminderEmails ?? null,
        priority: ev.priority ?? null,
        progress: ev.progress ?? null,
        location: ev.location ?? null,
        meetingUrl: ev.meetingUrl ?? null,
        attendees: ev.attendees ?? [],
        subTasks: ev.subTasks ?? [],
        relatedEventId: ev.relatedEventId ?? null,
        recurrenceExclusions: ev.recurrenceExclusions ?? null,
        // P0-5: 重复日程拖拽时传 updateMode='all'，避免修改父事件时间只影响首实例
        updateMode: ev.isRecurring ? 'all' : undefined,
        // P3: 乐观锁 If-Match（重复 all 模式时也校验起始实例版本，其他实例由 DB 并发兜底）
        ifMatch: ev.rowVersion ?? undefined,
      });
      // 成功 → 用后端真实返回的 DTO（包含最新 rowVersion/落库时间）覆盖本地乐观补丁，
      // 避免 2s 后 signalRRev bump → setAllEvents 拉到旧值（写库延迟）把事件弹回去。
      // 关键：后端返回的父事件 DTO 不带 recurrenceOriginalStart（null），直接 upsert 会因复合 key
      // 不匹配（id vs id#originalStart）导致追加新事件而非替换，造成两条色块。
      // 用乐观事件原有的 recurrenceOriginalStart 覆盖 res，确保 key 一致。
      //
      // 重复日程 updateMode='all' 的关键修复：
      //   后端返回的 res 是父事件 DTO，其 startAt 是首实例的新起始时间（originalParentStart + delta），
      //   而非被拖拽实例的新起始时间（newStartIso）。如果直接用 res.startAt 覆盖被拖拽实例，
      //   会导致被拖拽实例出现在错误位置（首实例位置），与已偏移的首实例重叠 → 出现 2 条色块 + 冲突红点。
      //   修复：对重复日程，保留乐观更新的 startAt/endAt（= 被拖拽实例的正确新位置），
      //   只从 res 中取 rowVersion 等元数据。
      // P1-1: afterSnapshot 必须用 resWithKey（含被拖拽实例的正确 startAt/endAt），
      //   不能用 res（父事件 DTO，其 startAt 是首实例起始，非被拖拽实例），
      //   否则 undo/redo 计算 deltaMs 会出错。
      let afterSnapForUndo: CalendarEventDto = optimisticEvent;
      if (res) {
        const resWithKey: CalendarEventDto = {
          ...res,
          recurrenceOriginalStart: ev.recurrenceOriginalStart ?? res.recurrenceOriginalStart ?? null,
          ...(ev.isRecurring
            ? { startAt: newStartIso, endAt: newEndIso ?? ev.endAt }
            : {}),
        };
        get().optimisticUpsertEvent(resWithKey);
        afterSnapForUndo = resWithKey;
      }
      // 推入撤销栈（用含正确 startAt/endAt 的最终形态作 afterSnapshot）
      get().pushUndo({
        type: 'move',
        description: tBound('calendar.store.undo.move'),
        beforeSnapshot: { ...ev },
        afterSnapshot: afterSnapForUndo,
        eventId: ev.id,
      });
      // 成功 → 写入挂起 patch（持久化 localStorage），即使刷新也能 replay 保留位置，
      // 待 signalRRev bump 触发下一次 refreshEvents 时 setAllEvents 会检测到后端已同步 → 自动清理 patch
      //
      // 关键修复（重复日程 updateMode='all'）：
      //   patch.startAt 必须用 newStartIso（被拖拽实例的新起始），不能用 res?.startAt（父事件的新起始）。
      //   因为 patch replay 用 deltaMs = patch.startAt - patch.oldStartAt 计算偏移量，
      //   如果用父事件的 startAt，deltaMs 可能为 0（父事件新起始恰好等于被拖拽实例原始起始），
      //   导致其他实例不偏移，刷新后原位置色块残留。
      upsertPendingPatch({
        eventId: ev.id,
        startAt: newStartIso,
        endAt: newEndIso ?? undefined,
        operation: 'move',
        appliedAtMs: Date.now(),
        // 重复日程 updateMode='all' 场景：记录拖拽实例的原始 recurrenceOriginalStart
        // setAllEvents patch replay 时据此：
        //   1) sync 检测：后端重新展开后 oldStartAt 不再存在 → 已同步
        //   2) 计算 delta 并应用到同父 ID 的所有展开实例
        // 用 recurrenceOriginalStart 而非 ev.startAt，避免连续两次拖拽同一实例时 delta 累计错误
        oldStartAt: ev.isRecurring ? (ev.recurrenceOriginalStart ?? ev.startAt) : undefined,
      });
      // 成功 → 立即 bump signalRRev，触发事件列表刷新，确保后端数据同步到前端。
      // 不再延迟 2 秒（避免色块残留），optimisticUpsertEvent 已先提供即时反馈，
      // 5 秒乐观保护期保护本地版本不被后端写库延迟覆盖。
      set({ pendingSignalRBump: true });
    } catch (e: unknown) {
      // ========== 失败回滚：还原 allEvents + eventsByDate + list + overview（四视图同步回滚） ==========
      set({ allEvents: prevAllEvents, eventsByDate: prevEventsByDate, list: prevList, overview: prevOverview });
      // P2-9: 清理乐观标记，避免 5 秒保护期内后端推送的正确数据被跳过
      get()._unmarkOptimistic(ev.id);
      const errData = getErrorData<ApiErrorResponseBody>(e);
      const conflictData = errData?.data as ConflictResponseData | undefined;
      const status = getErrorStatus(e);
      // P0-4: 捕获 409 冲突错误并向用户展示冲突详情
      if (status === 409 && conflictData?.conflicts) {
        const conflicts = conflictData.conflicts as ConflictItem[];
        const titles = conflicts.map(c => c.title || tBound('calendar.common.untitled')).slice(0, 3).join(tBound('calendar.store.conflict.titleSeparator'));
        const extra = conflicts.length > 3 ? tParams('calendar.store.conflict.more', { count: conflicts.length }) : '';
        set({ showUndoToast: true, undoToastMessage: `${tBound('calendar.store.conflict.prefix')}${titles}${extra}` });
        scheduleUndoToastHide(set, get, 5000);
      } else if (
        status === 409 &&
        (conflictData?.errorType === 'calendar_concurrency_conflict' || conflictData?.latest)
      ) {
        // P3: 乐观锁并发冲突 → 用后端返回的 latest 覆盖本地，并 toast 提示
        const latest = conflictData?.latest as CalendarEventDto | undefined | null;
        if (latest) {
          get().optimisticUpsertEvent(latest);
        }
        const msg = errData?.message || tBound('calendar.common.staleDataRefreshed');
        set({ showUndoToast: true, undoToastMessage: msg });
        scheduleUndoToastHide(set, get, 5000);
      } else {
        console.error('[calendar] moveEventByDrop update failed:', e);
      }
      // 失败不 bump，保持原数据
    }
  },
  // 交互增强-3: 事件 resize 手柄 —— 拖拽底部边缘调整 endAt
  resizeEvent: async (eventId, newEndAt) => {
    const { allEvents } = get();
    const ev = allEvents.find((e) => e.id === eventId);
    if (!ev || !ev.id) return;
    const oldStart = new Date(ev.startAt);
    const newEnd = new Date(newEndAt);
    const oldEndAt = ev.endAt;
    const oldEndMs = oldEndAt ? new Date(oldEndAt).getTime() : NaN;
    if (isNaN(newEnd.getTime()) || newEnd <= oldStart) return;
    const newEndIso = newEnd.toISOString();

    // ========== 乐观更新：立刻应用新 endAt 到 allEvents + eventsByDate + list ==========
    // 避免"松手后事件弹回原时长 → 拖不动"的感知问题
    // 用 optimisticUpsertEvent：立即更新视图 + 自动打乐观变更标记，5 秒保护期内不被后端旧数据覆盖
    // 快照：四视图（日历/列表/概览/trashCount）全部保存，失败时整体回滚
    const prevAllEvents = get().allEvents;
    const prevEventsByDate = get().eventsByDate;
    const prevList = get().list;
    const prevOverview = get().overview;
    const optimisticEvent: CalendarEventDto = { ...ev, endAt: newEndIso };
    get().optimisticUpsertEvent(optimisticEvent);

    // ========== 重复日程 updateMode='all' 的乐观联动：调整一个实例时长 = 全系列同步 delta ==========
    // 背景：后端 updateMode='all' 会把所有展开实例的 endAt 统一偏移 deltaMs（=newEnd-oldEnd）。
    // 但 optimisticUpsertEvent 只替换了被拖拽实例（按 id#originalStart 复合 key），
    // 其余实例仍停留在原 endAt → 视觉上其他实例时长未变，与后端同步结果不一致。
    // 修复：识别重复日程（isRecurring + 多个同 id 展开实例），把其余实例的 endAt 也按 delta 偏移。
    if (ev.isRecurring && isFinite(oldEndMs)) {
      const deltaMs = newEnd.getTime() - oldEndMs;
      if (deltaMs !== 0) {
        const curAll = get().allEvents;
        let mutated = false;
        const nextAll = curAll.map((e) => {
          if (e.id !== ev.id) return e;
          if (!e.recurrenceOriginalStart) return e;
          // 跳过被拖拽实例本身（已被 optimisticUpsertEvent 更新）
          if (ev.recurrenceOriginalStart && (e.recurrenceOriginalStart ?? '') === (ev.recurrenceOriginalStart ?? '')) return e;
          const eEnd = e.endAt ? new Date(e.endAt).getTime() : null;
          if (eEnd == null || !isFinite(eEnd)) return e;
          mutated = true;
          return { ...e, endAt: new Date(eEnd + deltaMs).toISOString() };
        });
        if (mutated) {
          const nextMap = buildEventsByDate(nextAll);
          // 同步：list 中其他实例也要联动偏移
          const curList = get().list;
          let nextList = curList;
          if (curList && Array.isArray(curList.data)) {
            let listMutated = false;
            const newListData = curList.data.map((e) => {
              if (e.id !== ev.id) return e;
              if (!e.recurrenceOriginalStart) return e;
              if (ev.recurrenceOriginalStart && (e.recurrenceOriginalStart ?? '') === (ev.recurrenceOriginalStart ?? '')) return e;
              const eEnd = e.endAt ? new Date(e.endAt).getTime() : null;
              if (eEnd == null || !isFinite(eEnd)) return e;
              listMutated = true;
              return { ...e, endAt: new Date(eEnd + deltaMs).toISOString() };
            });
            if (listMutated) nextList = { ...curList, data: newListData };
          }
          // P1-2: 同步 overview.upcomingEvents 中其他实例
          const curOv = get().overview;
          let nextOv = curOv;
          if (curOv && Array.isArray(curOv.upcomingEvents)) {
            let ovMutated = false;
            const newOvEvents = curOv.upcomingEvents.map((e) => {
              if (e.id !== ev.id) return e;
              if (!e.recurrenceOriginalStart) return e;
              if (ev.recurrenceOriginalStart && (e.recurrenceOriginalStart ?? '') === (ev.recurrenceOriginalStart ?? '')) return e;
              const eEnd = e.endAt ? new Date(e.endAt).getTime() : null;
              if (eEnd == null || !isFinite(eEnd)) return e;
              ovMutated = true;
              return { ...e, endAt: new Date(eEnd + deltaMs).toISOString() };
            });
            if (ovMutated) nextOv = { ...curOv, upcomingEvents: newOvEvents };
          }
          set({ allEvents: nextAll, eventsByDate: nextMap, list: nextList, overview: nextOv });
        }
      }
    }

    let apiSucceeded = false;
    try {
      const res = await calendarApi.update(ev.id, {
        id: ev.id,
        title: ev.title,
        description: ev.description ?? null,
        eventType: ev.eventType,
        startAt: ev.startAt,
        endAt: newEndIso,
        isAllDay: ev.isAllDay,
        mailboxId: ev.mailboxId ?? null,
        userId: ev.userId ?? null,
        color: ev.color ?? null,
        status: ev.status,
        isRecurring: ev.isRecurring ?? false,
        recurrenceType: ev.recurrenceType ?? null,
        recurrenceInterval: ev.recurrenceInterval ?? null,
        recurrenceEndDate: ev.recurrenceEndDate ?? null,
        recurrenceCount: ev.recurrenceCount ?? null,
        reminderMinutes: ev.reminderMinutes ?? null,
        reminderEmails: ev.reminderEmails ?? null,
        priority: ev.priority ?? null,
        progress: ev.progress ?? null,
        location: ev.location ?? null,
        meetingUrl: ev.meetingUrl ?? null,
        attendees: ev.attendees ?? [],
        subTasks: ev.subTasks ?? [],
        relatedEventId: ev.relatedEventId ?? null,
        recurrenceExclusions: ev.recurrenceExclusions ?? null,
        // P1-4: 重复日程 resize 时传 updateMode='all'，与 moveEventByDrop 保持一致
        updateMode: ev.isRecurring ? 'all' : undefined,
        // P3: 乐观锁 If-Match
        ifMatch: ev.rowVersion ?? undefined,
      });
      apiSucceeded = true;
      // 成功 → 用后端真实返回的 DTO（包含最新 rowVersion/落库 endAt）覆盖本地乐观版本，
      // 防止 2s 后 signalRRev bump → setAllEvents 拉到旧数据把 endAt 弹回原值。
      // 关键：后端返回的父事件 DTO 不带 recurrenceOriginalStart（null），直接 upsert 会因复合 key
      // 不匹配（id vs id#originalStart）导致追加新事件而非替换，造成两条色块。
      // 重复日程 updateMode='all' 时 res.endAt 是父事件的新 endAt（首实例的 endAt），
      // 不是被拖拽实例的 endAt，因此对重复日程保留乐观更新的 endAt，只从 res 取 rowVersion 等元数据。
      // P1-1: afterSnapshot 必须用 resWithKey（含被拖拽实例的正确 endAt），
      //   不能用 res（父事件 DTO，其 endAt 是首实例的 endAt，非被拖拽实例），
      //   否则 undo/redo 计算 deltaEnd 会出错。
      let afterSnapForUndo: CalendarEventDto = optimisticEvent;
      if (res) {
        const resWithKey: CalendarEventDto = {
          ...res,
          recurrenceOriginalStart: ev.recurrenceOriginalStart ?? res.recurrenceOriginalStart ?? null,
          ...(ev.isRecurring ? { endAt: newEndIso } : {}),
        };
        get().optimisticUpsertEvent(resWithKey);
        afterSnapForUndo = resWithKey;
      }
      // 推入撤销栈（用含正确 endAt 的最终形态作 afterSnapshot）
      get().pushUndo({
        type: 'resize',
        description: tBound('calendar.store.undo.resize'),
        beforeSnapshot: { ...ev, endAt: oldEndAt },
        afterSnapshot: afterSnapForUndo,
        eventId: ev.id,
      });
      // 成功 → 写入挂起 patch（持久化 localStorage），刷新后仍保留 endAt，
      // 下次 setAllEvents 若后端已同步则自动清理 patch
      // P1-5: 重复日程记录 oldStartAt，支持 setAllEvents patch replay 时检测后端同步状态
      upsertPendingPatch({
        eventId: ev.id,
        endAt: newEndIso,
        operation: 'resize',
        appliedAtMs: Date.now(),
        oldStartAt: ev.isRecurring ? (ev.recurrenceOriginalStart ?? ev.startAt) : undefined,
      });
      // 成功 → 立即 bump signalRRev，触发事件列表刷新，确保后端数据同步到前端。
      // 不再延迟 2 秒（避免色块残留），optimisticUpsertEvent 已先提供即时反馈，
      // 5 秒乐观保护期保护本地版本不被后端写库延迟覆盖。
      set({ pendingSignalRBump: true });
    } catch (e: unknown) {
      // ========== 失败回滚：还原 allEvents + eventsByDate + list + overview（四视图同步回滚） ==========
      set({ allEvents: prevAllEvents, eventsByDate: prevEventsByDate, list: prevList, overview: prevOverview });
      // P2-9: 清理乐观标记，避免 5 秒保护期内后端推送的正确数据被跳过
      get()._unmarkOptimistic(ev.id);
      const errData = getErrorData<ApiErrorResponseBody>(e);
      const conflictData = errData?.data as ConflictResponseData | undefined;
      const status = getErrorStatus(e);
      // P0-4: 捕获 409 冲突错误并向用户展示冲突详情
      if (status === 409 && conflictData?.conflicts) {
        const conflicts = conflictData.conflicts as ConflictItem[];
        const titles = conflicts.map(c => c.title || tBound('calendar.common.untitled')).slice(0, 3).join(tBound('calendar.store.conflict.titleSeparator'));
        const extra = conflicts.length > 3 ? tParams('calendar.store.conflict.more', { count: conflicts.length }) : '';
        set({ showUndoToast: true, undoToastMessage: `${tBound('calendar.store.conflict.prefix')}${titles}${extra}` });
        scheduleUndoToastHide(set, get, 5000);
      } else if (
        status === 409 &&
        (conflictData?.errorType === 'calendar_concurrency_conflict' || conflictData?.latest)
      ) {
        // P3: 乐观锁并发冲突 → 用后端返回的 latest 覆盖本地，并 toast 提示
        const latest = conflictData?.latest as CalendarEventDto | undefined | null;
        if (latest) {
          get().optimisticUpsertEvent(latest);
        }
        const msg = errData?.message || tBound('calendar.common.staleDataRefreshed');
        set({ showUndoToast: true, undoToastMessage: msg });
        scheduleUndoToastHide(set, get, 5000);
      } else {
        console.error('[calendar] resizeEvent update failed:', e);
      }
    }
    void apiSucceeded;
  },
  // P2-2/P2-5: 暴露 pending patch 管理给外部组件
  upsertPendingPatch: (patch) => upsertPendingPatch(patch),
  dropPendingPatch: (eventId) => dropPendingPatch(eventId),
  // 第三梯队: 撤销/重做
  pushUndo: (entry) => {
    const cur = get().undoStack;
    // 最多保留 STACK_CAPACITY 条（持久化上限=50，内存栈保持一致=50，之前的20不再使用）
    const next = [...cur, entry].slice(-STACK_CAPACITY);
    set({ undoStack: next, redoStack: [], showUndoToast: true, undoToastMessage: entry.description });
    // P2-A#4: 持久化到 localStorage
    try { persistUndoRedo(get); } catch (_e) { /* noop */ }
    // 3秒后自动隐藏 toast
    scheduleUndoToastHide(set, get, 3000);
  },

  undo: async () => {
    const { undoStack } = get();
    if (undoStack.length === 0) return;
    const entry = undoStack[undoStack.length - 1];
    // 用于在 API 调用后更新 entry 中的 ID，保持栈中 ID 与实际事件 ID 一致
    // （create 会生成新 ID，若不更新会导致后续 redo/undo 调用旧 ID 失败）
    let updatedEntry = entry;
    // P1-1: 保存回滚快照——任何乐观修改前先保存，API 失败时恢复到操作前状态
    const prevAllEvents = get().allEvents;
    const prevEventsByDate = get().eventsByDate;
    const prevList = get().list;
    const prevOverview = get().overview;
    const prevTrashCount = get().trashCount;
    const prevMarkers = get()._optimisticMarkers;
    try {
      // P2-4: 批量删除撤销 → 逐个重新创建事件
      if (entry.type === 'batch_delete' && entry.beforeSnapshots?.length) {
        for (const snap of entry.beforeSnapshots) {
          try {
            const created = await calendarApi.create(toCreateRequest(snap));
            get().optimisticUpsertEvent(created);
          } catch { /* 单个失败不阻塞其他 */ }
        }
        useCalendarStore.setState((s) => ({ trashCount: Math.max(0, s.trashCount - entry.beforeSnapshots!.length) }));
      // P2-4: 批量更新撤销 → 逐个恢复 beforeSnapshot
      } else if (entry.type === 'batch_edit' && entry.beforeSnapshots?.length) {
        for (const snap of entry.beforeSnapshots) {
          try {
            get().optimisticUpsertEvent(snap);
            await calendarApi.update(snap.id, { ...snap, id: snap.id, ifMatch: snap.rowVersion ?? undefined });
            dropPendingPatch(snap.id);
          } catch { /* 单个失败不阻塞其他 */ }
        }
      } else if (entry.type === 'delete') {
        // 撤销删除 → 重新创建事件（会生成新 ID）
        if (entry.beforeSnapshot) {
          const created = await calendarApi.create(toCreateRequest(entry.beforeSnapshot));
          // 乐观插入到本地视图：事件立刻出现在日历上，不等 signalRRev → API 拉取
          get().optimisticUpsertEvent(created);
          // 垃圾箱红点徽标 -1（撤销删除意味着从垃圾箱又捞回来了）
          useCalendarStore.setState((s) => ({ trashCount: Math.max(0, s.trashCount - 1) }));
          // 用新 ID 更新 afterSnapshot，供后续 redo 使用
          updatedEntry = {
            ...entry,
            afterSnapshot: { ...entry.beforeSnapshot, id: created.id },
            eventId: created.id,
          };
        }
      } else if (entry.type === 'create') {
        // 撤销创建 → 删除事件（ID 不变）
        if (entry.afterSnapshot?.id) {
          const idToRemove = entry.afterSnapshot.id;
          const rv = entry.afterSnapshot.rowVersion ?? undefined;
          // 乐观删除：先从视图移除（deletedAt 非空 → optimisticUpsertEvent 内部会直接过滤掉）
          get().optimisticUpsertEvent({ ...entry.afterSnapshot, deletedAt: new Date().toISOString() });
          await calendarApi.remove(idToRemove, { rowVersion: rv ?? null });
        }
      } else {
        // 撤销 move/edit/resize → 恢复到 beforeSnapshot（ID 不变）
        if (entry.beforeSnapshot?.id) {
          // 关键修复：ifMatch 必须用 afterSnapshot.rowVersion（当前后端版本），
          // 而非 beforeSnapshot.rowVersion（操作前的旧版本，已被操作更新为过期）。
          // 否则后端 If-Match 校验失败 → 412/409 并发冲突 → "撤销失败"。
          const rv = entry.afterSnapshot?.rowVersion ?? undefined;
          // 乐观恢复：立刻把事件形态改回 beforeSnapshot
          get().optimisticUpsertEvent(entry.beforeSnapshot);
          // P1-1: 重复日程 undo 时联动恢复所有同父 ID 实例（反向偏移）
          const beforeSnap = entry.beforeSnapshot;
          const afterSnap = entry.afterSnapshot;
          if (beforeSnap.isRecurring && afterSnap) {
            const bStartMs = new Date(beforeSnap.startAt).getTime();
            const aStartMs = new Date(afterSnap.startAt).getTime();
            const isMove = entry.type === 'move';
            const deltaMs = (isMove && isFinite(bStartMs) && isFinite(aStartMs))
              ? (aStartMs - bStartMs)
              : 0;
            const bEndMs = beforeSnap.endAt ? new Date(beforeSnap.endAt).getTime() : NaN;
            const aEndMs = afterSnap.endAt ? new Date(afterSnap.endAt).getTime() : NaN;
            const deltaEndMs = (!isMove && isFinite(bEndMs) && isFinite(aEndMs))
              ? (aEndMs - bEndMs)
              : 0;
            // P3: 使用公共函数偏移（undo 为反向 = -delta）
            const allShift = shiftRecurringInstances(
              get().allEvents, beforeSnap.id, beforeSnap.recurrenceOriginalStart,
              isMove, -deltaMs, -deltaEndMs,
            );
            if (allShift.mutated) {
              const patch: Partial<Pick<CalendarStoreState, 'allEvents' | 'eventsByDate' | 'list' | 'overview'>> = {};
              patch.allEvents = allShift.result;
              patch.eventsByDate = buildEventsByDate(allShift.result);
              // 同步 list
              const curList = get().list;
              if (curList && Array.isArray(curList.data)) {
                const listShift = shiftRecurringInstances(
                  curList.data, beforeSnap.id, beforeSnap.recurrenceOriginalStart,
                  isMove, -deltaMs, -deltaEndMs,
                );
                if (listShift.mutated) patch.list = { ...curList, data: listShift.result };
              }
              // 同步 overview
              const curOv = get().overview;
              if (curOv && Array.isArray(curOv.upcomingEvents)) {
                const ovShift = shiftRecurringInstances(
                  curOv.upcomingEvents, beforeSnap.id, beforeSnap.recurrenceOriginalStart,
                  isMove, -deltaMs, -deltaEndMs,
                );
                if (ovShift.mutated) patch.overview = { ...curOv, upcomingEvents: ovShift.result };
              }
              set(patch);
            }
          }
          await calendarApi.update(entry.beforeSnapshot.id, {
            ...toCreateRequest(entry.beforeSnapshot),
            status: entry.beforeSnapshot.status,
            id: entry.beforeSnapshot.id,
            ifMatch: rv ?? undefined,
          });
          // 关键修复：撤销成功后必须清理 pending patch，否则 setAllEvents 会 replay
          // 原操作（move/resize/edit）的 patch，把事件位置弹回撤销前（后移动）的位置，
          // 导致"已撤销"但色块没回到原位的视觉错误。
          // P2-3: move/resize 的 API 调用在用户后续操作前已完成，pending patch 已 synced，
          // dropPendingPatch 是安全的（setAllEvents 也会自动 drop synced patch，这里只是提前清理）
          dropPendingPatch(entry.beforeSnapshot.id);
        }
      }
      // 移到 redo 栈（使用可能更新过 ID 的 entry）
      set({
        undoStack: undoStack.slice(0, -1),
        redoStack: [...get().redoStack, updatedEntry],
        showUndoToast: true,
        // `String(...)`：模板字面量 `${entry.description}` 对 undefined 会渲染成 "undefined"，
        // 这里逐字保留该语义（`TranslateParams` 只接受 string | number，不能直接传 undefined）
        undoToastMessage: tParams('calendar.store.undo.undoneToast', { description: String(entry.description) }),
      });
      // P2-A#4: 持久化到 localStorage
      try { persistUndoRedo(get); } catch (_e) { /* noop */ }
      // 立即 bump signalRRev：optimisticUpsertEvent 已先提供即时反馈，
      // 5 秒乐观保护期保护本地版本不被后端写库延迟覆盖。
      useCalendarStore.setState((s) => ({ signalRRev: s.signalRRev + 1 }));
      // 3秒后隐藏 toast
      scheduleUndoToastHide(set, get, 3000);
    } catch (e: unknown) {
      // P1-1: 回滚所有乐观修改——恢复到 undo 操作前的完整状态
      set({
        allEvents: prevAllEvents,
        eventsByDate: prevEventsByDate,
        list: prevList,
        overview: prevOverview,
        trashCount: prevTrashCount,
        _optimisticMarkers: prevMarkers,
      });
      // P0-7: 区分 409 冲突错误并展示冲突详情
      const errData = getErrorData<ApiErrorResponseBody>(e);
      const conflictData = errData?.data as ConflictResponseData | undefined;
      const status = getErrorStatus(e);
      if (status === 409 && conflictData?.conflicts) {
        const conflicts = conflictData.conflicts as ConflictItem[];
        const titles = conflicts.map(c => c.title || tBound('calendar.common.untitled')).slice(0, 3).join(tBound('calendar.store.conflict.titleSeparator'));
        const extra = conflicts.length > 3 ? tParams('calendar.store.conflict.more', { count: conflicts.length }) : '';
        set({ showUndoToast: true, undoToastMessage: `${tBound('calendar.store.undo.failPrefix')}${tBound('calendar.store.conflict.prefix')}${titles}${extra}` });
      } else {
        console.error('[calendar] undo failed:', e);
        set({ showUndoToast: true, undoToastMessage: tBound('calendar.store.undo.failed') });
      }
      scheduleUndoToastHide(set, get, 5000);
    }
  },

  redo: async () => {
    const { redoStack } = get();
    if (redoStack.length === 0) return;
    const entry = redoStack[redoStack.length - 1];
    // 用于在 API 调用后更新 entry 中的 ID，保持栈中 ID 与实际事件 ID 一致
    let updatedEntry = entry;
    // P1-1: 保存回滚快照——任何乐观修改前先保存，API 失败时恢复到操作前状态
    const prevAllEvents = get().allEvents;
    const prevEventsByDate = get().eventsByDate;
    const prevList = get().list;
    const prevOverview = get().overview;
    const prevTrashCount = get().trashCount;
    const prevMarkers = get()._optimisticMarkers;
    try {
      // P2-4: 批量删除重做 → 逐个删除（若 undo 创建了新 ID，需用新 ID 删除）
      if (entry.type === 'batch_delete' && entry.beforeSnapshots?.length) {
        for (const snap of entry.beforeSnapshots) {
          try {
            get().optimisticUpsertEvent({ ...snap, deletedAt: new Date().toISOString() });
            await calendarApi.remove(snap.id, { rowVersion: snap.rowVersion ?? null });
            dropPendingPatch(snap.id);
          } catch { /* 单个失败不阻塞其他 */ }
        }
        useCalendarStore.setState((s) => ({ trashCount: s.trashCount + entry.beforeSnapshots!.length }));
      // P2-4: 批量更新重做 → 无法精确恢复（无 afterSnapshots），触发 signalRRev 全量刷新
      } else if (entry.type === 'batch_edit') {
        // 批量更新重做无法精确恢复每个事件的 afterSnapshot（后端 batchOperation 不返回 DTO）
        // 只能触发全量刷新让后端数据覆盖本地
      } else if (entry.type === 'delete') {
        // 重做删除 → 用 undo 时创建的新 ID 删除
        if (entry.afterSnapshot?.id) {
          const idToRemove = entry.afterSnapshot.id;
          const rv = entry.afterSnapshot.rowVersion ?? undefined;
          // 乐观删除：先从视图移除，不等 API 返回 & 刷新
          get().optimisticUpsertEvent({ ...entry.afterSnapshot, deletedAt: new Date().toISOString() });
          useCalendarStore.setState((s) => ({ trashCount: s.trashCount + 1 }));
          await calendarApi.remove(idToRemove, { rowVersion: rv ?? null });
        }
      } else if (entry.type === 'create') {
        // 重做创建 → 重新创建事件（会生成新 ID）
        if (entry.afterSnapshot) {
          const created = await calendarApi.create(toCreateRequest(entry.afterSnapshot));
          // 乐观插入到本地视图：事件立刻出现
          get().optimisticUpsertEvent(created);
          // 用新 ID 更新 afterSnapshot，供后续 undo 使用
          updatedEntry = {
            ...entry,
            afterSnapshot: { ...entry.afterSnapshot, id: created.id },
            eventId: created.id,
          };
        }
      } else {
        // 重做 move/edit/resize → 恢复到 afterSnapshot（ID 不变）
        if (entry.afterSnapshot?.id) {
          const rv = entry.afterSnapshot.rowVersion ?? undefined;
          // 乐观更新：立刻把事件改到 afterSnapshot 状态
          get().optimisticUpsertEvent(entry.afterSnapshot);
          // P1-1: 重复日程 redo 时联动偏移所有同父 ID 实例（正向偏移）
          const afterSnap = entry.afterSnapshot;
          const beforeSnap = entry.beforeSnapshot;
          if (afterSnap.isRecurring && beforeSnap) {
            const bStartMs = new Date(beforeSnap.startAt).getTime();
            const aStartMs = new Date(afterSnap.startAt).getTime();
            const isMove = entry.type === 'move';
            const deltaMs = (isMove && isFinite(bStartMs) && isFinite(aStartMs))
              ? (aStartMs - bStartMs)
              : 0;
            const bEndMs = beforeSnap.endAt ? new Date(beforeSnap.endAt).getTime() : NaN;
            const aEndMs = afterSnap.endAt ? new Date(afterSnap.endAt).getTime() : NaN;
            const deltaEndMs = (!isMove && isFinite(bEndMs) && isFinite(aEndMs))
              ? (aEndMs - bEndMs)
              : 0;
            // P3: 使用公共函数偏移（redo 为正向 = +delta）
            const allShift = shiftRecurringInstances(
              get().allEvents, afterSnap.id, afterSnap.recurrenceOriginalStart,
              isMove, deltaMs, deltaEndMs,
            );
            if (allShift.mutated) {
              const patch: Partial<Pick<CalendarStoreState, 'allEvents' | 'eventsByDate' | 'list' | 'overview'>> = {};
              patch.allEvents = allShift.result;
              patch.eventsByDate = buildEventsByDate(allShift.result);
              const curList = get().list;
              if (curList && Array.isArray(curList.data)) {
                const listShift = shiftRecurringInstances(
                  curList.data, afterSnap.id, afterSnap.recurrenceOriginalStart,
                  isMove, deltaMs, deltaEndMs,
                );
                if (listShift.mutated) patch.list = { ...curList, data: listShift.result };
              }
              const curOv = get().overview;
              if (curOv && Array.isArray(curOv.upcomingEvents)) {
                const ovShift = shiftRecurringInstances(
                  curOv.upcomingEvents, afterSnap.id, afterSnap.recurrenceOriginalStart,
                  isMove, deltaMs, deltaEndMs,
                );
                if (ovShift.mutated) patch.overview = { ...curOv, upcomingEvents: ovShift.result };
              }
              set(patch);
            }
          }
          await calendarApi.update(entry.afterSnapshot.id, {
            ...toCreateRequest(entry.afterSnapshot),
            status: entry.afterSnapshot.status,
            id: entry.afterSnapshot.id,
            ifMatch: rv ?? undefined,
          });
          // 重做成功后更新 pending patch：记录重做后的位置（afterSnapshot），
          // 确保刷新后 replay 保留重做结果而非撤销前的位置。
          upsertPendingPatch({
            eventId: entry.afterSnapshot.id,
            startAt: entry.afterSnapshot.startAt,
            endAt: entry.afterSnapshot.endAt ?? undefined,
            operation: entry.type === 'resize' ? 'resize' : 'move',
            appliedAtMs: Date.now(),
            oldStartAt: entry.afterSnapshot.isRecurring
              ? (entry.afterSnapshot.recurrenceOriginalStart ?? entry.afterSnapshot.startAt)
              : undefined,
          });
        }
      }
      set({
        redoStack: redoStack.slice(0, -1),
        undoStack: [...get().undoStack, updatedEntry],
        showUndoToast: true,
        // 同上：模板字面量 `${entry.description}` 的 undefined → "undefined" 语义逐字保留
        undoToastMessage: tParams('calendar.store.redo.redoneToast', { description: String(entry.description) }),
      });
      // P2-A#4: 持久化到 localStorage
      try { persistUndoRedo(get); } catch (_e) { /* noop */ }
      // 立即 bump signalRRev：optimisticUpsertEvent 已先提供即时反馈，
      // 5 秒乐观保护期保护本地版本不被后端写库延迟覆盖。
      useCalendarStore.setState((s) => ({ signalRRev: s.signalRRev + 1 }));
      scheduleUndoToastHide(set, get, 3000);
    } catch (e: unknown) {
      // P1-1: 回滚所有乐观修改——恢复到 redo 操作前的完整状态
      set({
        allEvents: prevAllEvents,
        eventsByDate: prevEventsByDate,
        list: prevList,
        overview: prevOverview,
        trashCount: prevTrashCount,
        _optimisticMarkers: prevMarkers,
      });
      // P0-7: 区分 409 冲突错误并展示冲突详情
      const errData = getErrorData<ApiErrorResponseBody>(e);
      const conflictData = errData?.data as ConflictResponseData | undefined;
      const status = getErrorStatus(e);
      if (status === 409 && conflictData?.conflicts) {
        const conflicts = conflictData.conflicts as ConflictItem[];
        const titles = conflicts.map(c => c.title || tBound('calendar.common.untitled')).slice(0, 3).join(tBound('calendar.store.conflict.titleSeparator'));
        const extra = conflicts.length > 3 ? tParams('calendar.store.conflict.more', { count: conflicts.length }) : '';
        set({ showUndoToast: true, undoToastMessage: `${tBound('calendar.store.redo.failPrefix')}${tBound('calendar.store.conflict.prefix')}${titles}${extra}` });
      } else {
        console.error('[calendar] redo failed:', e);
        set({ showUndoToast: true, undoToastMessage: tBound('calendar.store.redo.failed') });
      }
      scheduleUndoToastHide(set, get, 5000);
    }
  },

  dismissUndoToast: () => set({ showUndoToast: false, undoToastMessage: null }),

  // P1-C#1: 通用瞬态 toast（type 缺省为 info；key 用递增数保证组件重挂载，持续时间默认 2500ms）
  showTransientToast: (message, type = 'info', durationMs = 2500) => {
    const toast = { type, message, key: Date.now() + Math.random() };
    set({ transientToast: toast });
    if (durationMs > 0) {
      window.setTimeout(() => {
        const cur = get().transientToast;
        if (cur && cur.key === toast.key) set({ transientToast: null });
      }, durationMs);
    }
  },
  dismissTransientToast: () => set({ transientToast: null }),

  openSelectedDateEvents: (ymd) => {
    const arr = get().eventsByDate.get(ymd);
    set({ selectedDateEvents: arr ? [...arr] : [] });
  },

  // P1-2: 批量选择
  selectEvent: (id) => set((s) => {
    if (s.selectedEventIds.has(id)) return s; // 已选 → no-op
    const next = new Set(s.selectedEventIds);
    next.add(id);
    return { selectedEventIds: next };
  }),
  deselectEvent: (id) => set((s) => {
    if (!s.selectedEventIds.has(id)) return s; // 未选 → no-op
    const next = new Set(s.selectedEventIds);
    next.delete(id);
    return { selectedEventIds: next };
  }),
  clearSelection: () => set({ selectedEventIds: new Set<number>() }),
  selectAll: (ids) => set({ selectedEventIds: new Set(ids) }),
  isSelected: (id) => get().selectedEventIds.has(id),

  // 第三梯队: 键盘导航焦点
  setFocusedEventId: (id) => set({ focusedEventId: id }),
  focusNextEvent: (direction) => {
    const { allEvents, focusedEventId } = get();
    if (allEvents.length === 0) return;
    // P3-14: 按 startAt 排序，重复日程的多个展开实例按 recurrenceOriginalStart 二级排序
    // 确保跳转方向稳定，避免 findIndex 命中错误实例
    const sorted = [...allEvents].sort((a, b) => {
      const ta = +new Date(a.startAt);
      const tb = +new Date(b.startAt);
      if (ta !== tb) return ta - tb;
      // 同 startAt 的重复日程实例按 recurrenceOriginalStart 排序
      const oa = a.recurrenceOriginalStart ?? '';
      const ob = b.recurrenceOriginalStart ?? '';
      return oa.localeCompare(ob);
    });
    if (focusedEventId === null) {
      set({ focusedEventId: direction === 1 ? sorted[0].id : sorted[sorted.length - 1].id });
      return;
    }
    // P3-2: findIndex 已返回第一个匹配项，无需内层再 findIndex（原 O(N²) → O(N)）
    const idx = sorted.findIndex((e) => e.id === focusedEventId);
    if (idx === -1) {
      set({ focusedEventId: direction === 1 ? sorted[0].id : sorted[sorted.length - 1].id });
      return;
    }
    const nextIdx = idx + direction;
    if (nextIdx < 0 || nextIdx >= sorted.length) return; // 到头/尾不循环
    set({ focusedEventId: sorted[nextIdx].id });
  },

  // P2-1: 模板
  loadTemplates: (templates) => set({ eventTemplates: templates }),
  fetchTemplates: async () => {
    try {
      const res = await calendarApi.listTemplates();
      const mapped: EventTemplate[] = (res.items ?? []).map((t) => ({
        id: t.id,
        name: t.name,
        title: t.title,
        eventType: t.eventType,
        durationMin: t.durationMin,
        isAllDay: t.isAllDay,
        description: t.description ?? undefined,
        location: t.location ?? undefined,
      }));
      set({ eventTemplates: mapped });
    } catch (e: unknown) {
      console.warn('[calendar] load templates failed:', e);
    }
  },
  applyTemplate: (templateId, date) => {
    const tpl = get().eventTemplates.find(t => t.id === templateId);
    if (!tpl) return;
    const existing = get().form;
    const pad = (n: number) => String(n).padStart(2, '0');
    // 基准日期优先级：参数 date > 现有 form.startAt 日期 > 今天
    let base: Date;
    if (date) {
      base = date;
    } else if (existing.startAt) {
      const parsed = new Date(existing.startAt.replace(' ', 'T'));
      base = Number.isNaN(parsed.getTime()) ? new Date() : parsed;
    } else {
      base = new Date();
    }
    const startAt = tpl.isAllDay
      ? `${base.getFullYear()}-${pad(base.getMonth() + 1)}-${pad(base.getDate())}T00:00`
      : `${base.getFullYear()}-${pad(base.getMonth() + 1)}-${pad(base.getDate())}T09:00`;
    const endAt = tpl.isAllDay
      ? undefined
      : `${base.getFullYear()}-${pad(base.getMonth() + 1)}-${pad(base.getDate())}T${pad(9 + Math.floor(tpl.durationMin / 60))}:${pad(tpl.durationMin % 60)}`;
    set({
      showEditor: true,
      editing: null,
      form: {
        ...EMPTY_FORM,
        // 优先保留原有的邮箱/用户设置
        mailboxId: existing.mailboxId,
        userId: existing.userId,
        attendees: existing.attendees,
        reminderMinutesBefore: existing.reminderMinutesBefore,
        recurrenceType: existing.recurrenceType,
        recurrenceInterval: existing.recurrenceInterval,
        // 模板字段覆盖
        title: tpl.title,
        eventType: tpl.eventType,
        startAt,
        endAt: endAt ?? '',
        isAllDay: tpl.isAllDay,
        description: tpl.description ?? '',
        location: tpl.location ?? '',
      },
      saving: false,
      formError: null,
    });
  },
  saveTemplate: async (name) => {
    const f = get().form;
    if (!f.title || !f.title.trim() || !name.trim()) return null;
    // 计算 durationMin（分钟），全天事件记为 0
    let durationMin = 0;
    if (!f.isAllDay && f.startAt && f.endAt) {
      const s = new Date(f.startAt.replace(' ', 'T'));
      const e = new Date(f.endAt.replace(' ', 'T'));
      if (!Number.isNaN(s.getTime()) && !Number.isNaN(e.getTime()) && e > s) {
        durationMin = Math.max(1, Math.round((e.getTime() - s.getTime()) / 60000));
      }
    }
    try {
      const created = await calendarApi.createTemplate({
        name: name.trim(),
        title: f.title.trim(),
        eventType: f.eventType,
        durationMin,
        isAllDay: f.isAllDay,
        description: f.description || null,
        location: f.location || null,
        mailboxId: f.mailboxId ?? undefined,
      });
      const mapped: EventTemplate = {
        id: created.id,
        name: created.name,
        title: created.title,
        eventType: created.eventType,
        durationMin: created.durationMin,
        isAllDay: created.isAllDay,
        description: created.description ?? undefined,
        location: created.location ?? undefined,
      };
      set((s) => ({ eventTemplates: [...s.eventTemplates, mapped] }));
      return mapped;
    } catch (e) {
      console.warn('[calendar] saveTemplate failed:', e);
      return null;
    }
  },
  deleteTemplate: async (templateId) => {
    if (!templateId || templateId <= 0) return false;
    try {
      await calendarApi.deleteTemplate(templateId);
      set((s) => ({ eventTemplates: s.eventTemplates.filter((t) => t.id !== templateId) }));
      return true;
    } catch (e) {
      console.warn('[calendar] deleteTemplate failed:', e);
      return false;
    }
  },

  // 第三梯队: 事件类型筛选
  toggleEventTypeFilter: (eventType) => {
    const cur = get().enabledEventTypes;
    if (cur.size === 0) {
      // 当前所有类型都启用（空集=全选），切换到只显示该类型
      set({ enabledEventTypes: new Set([eventType]) });
    } else if (cur.has(eventType)) {
      const next = new Set(cur);
      next.delete(eventType);
      // 如果删完所有，保持空集（=全选）
      set({ enabledEventTypes: next.size === 0 ? new Set() : next });
      // 特殊：如果删完后变成空集，实际上应该恢复全选
      if (next.size === 0) set({ enabledEventTypes: new Set() });
    } else {
      const next = new Set(cur);
      next.add(eventType);
      // 如果选中了所有已知类型，回到空集（=全选）
      set({ enabledEventTypes: next });
    }
  },
  setAllEventTypesEnabled: () => set({ enabledEventTypes: new Set() }),

  // P1-5: 多邮箱筛选
  toggleMailboxFilter: (mailboxId) => set((s) => {
    const cur = s.enabledMailboxIds;
    if (cur.size === 0) {
      // 空=全选：切换后只排除当前这个？ 与类型筛选保持一致——空=全选，首次点某邮箱→选该邮箱（其他都不选）
      return { enabledMailboxIds: new Set([mailboxId]) };
    }
    if (cur.has(mailboxId)) {
      const next = new Set(cur);
      next.delete(mailboxId);
      if (next.size === 0) return { enabledMailboxIds: new Set<number>() };
      return { enabledMailboxIds: next };
    }
    const next = new Set(cur);
    next.add(mailboxId);
    return { enabledMailboxIds: next };
  }),
  setAllMailboxesEnabled: () => set({ enabledMailboxIds: new Set<number>() }),
  mailboxIsEnabled: (mailboxId) => {
    const cur = get().enabledMailboxIds;
    if (cur.size === 0) return true;
    if (mailboxId == null) return true; // 未关联邮箱的事件按"显示所有"
    return cur.has(mailboxId);
  },
  };
});

/* =========================================================
   T000119: 切换账户页面显示异常修复
   ---------------------------------------------------------
   背景：useCalendarStore 是模块级单例（create 一次），其内存状态（allEvents / eventsByDate / list /
   overview / undo 栈等）在登出或账户切换后仍会残留。SPA 内 NavLink 跳转到 /calendar 时不会像 F5 那样
   重建 store，而 setAllEvents 是增量合并"没出现的旧事件一律保留"，导致上一账户的日程残留在新账户视图中。
   修复：登出/会话失效时调用本函数，把内存 store 整体重置为 createInitialState() 的初始状态。
   ========================================================= */
export function resetCalendarStore() {
  useCalendarStore.setState(createInitialState());
}

/** 检查某事件类型是否启用（空集 = 全选） */
export function isEventTypeEnabled(enabledTypes: Set<string>, eventType: string): boolean {
  if (enabledTypes.size === 0) return true;
  return enabledTypes.has(eventType);
}

/* =========================================================
   P3-E#2-1 性能: 内联 shallow equality + 预购 shallow 选择器钩子
   =========================================================
   说明：hooks.ts 依赖本文件，为避免 import cycle 在此重述 shallowEqual。
         useCalendarShallow 替换 MonthView 等组件的 useCalendarStore(sel, shallowEqual)
         写法，同时可在跨组件迁移时统一启用「浅相等」避免对象引用抖动引发的无效重绘。
*/
type ShallowEq = <T>(a: T, b: T) => boolean;
const _shallowEqual: ShallowEq = (a, b) => {
  if (Object.is(a, b)) return true;
  if (typeof a !== 'object' || typeof b !== 'object' || a === null || b === null) return false;
  const ka = Object.keys(a as Record<string, unknown>);
  const kb = Object.keys(b as Record<string, unknown>);
  if (ka.length !== kb.length) return false;
  const ra = a as Record<string, unknown>;
  const rb = b as Record<string, unknown>;
  for (let i = 0; i < ka.length; i++) {
    const k = ka[i];
    if (!Object.prototype.hasOwnProperty.call(rb, k)) return false;
    if (!Object.is(ra[k], rb[k])) return false;
  }
  return true;
};

/** useCalendarStore 的浅相等包装 —— 等价于 useCalendarStore(selector, shallowEqual) */
export function useCalendarShallow<T>(selector: (state: CalendarStore) => T): T {
  return useCalendarStore(selector, _shallowEqual as unknown as (a: T, b: T) => boolean);
}

/** 对外暴露与 hooks.ts 的 shallowEqual 等价实现（避免循环依赖，给子模块内联使用） */
export const calendarShallowEqual = _shallowEqual;

export default useCalendarStore;
