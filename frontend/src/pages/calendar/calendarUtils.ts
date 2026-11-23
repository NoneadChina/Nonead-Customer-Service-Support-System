import type {
  CalendarEventDto,
  SubTask,
  RecurrenceExclusions,
} from '../../api/types';
import { Solar } from 'lunar-javascript';
import type { TranslationKey } from '../../locales/zh-Hans';
import type { TranslateParams } from '../../i18n/types';
import { BUILT_IN_ENTRIES, FALLBACK_LOCALE, getBoundLocale, tBound, toIntlLocale } from '../../i18n/locale';
import { translate } from '../../i18n/translate';

/* =========================================================
   非组件模块取词
   ---------------------------------------------------------
   本文件是纯模块（无 React 上下文），语言由 `I18nProvider` 在渲染时绑定
   （见 `i18n/locale.ts`），此处**只读** `getBoundLocale()`。
   `tBound()` 不接受插值参数，带 `{name}` 的词条走 `translate()`——
   与 `components/PasswordPolicyHint.tsx` / `utils/format.ts` 同一手法。
   未挂载 Provider（单测）时 `getBoundLocale()` 回落 `zh-Hans`。
   ========================================================= */
/** 带插值的取词（缺 key 失败安全：`translate` 原样返回 key 而不抛错） */
function tParams(key: TranslationKey, params?: TranslateParams): string {
  const locale = getBoundLocale();
  return translate(BUILT_IN_ENTRIES[locale] ?? BUILT_IN_ENTRIES[FALLBACK_LOCALE], locale, key, params);
}

// 视图模式：列表视图 | 日历视图
export type ViewMode = 'calendar' | 'overview' | 'list' | 'trash' | 'subscription' | 'outlook';
// 日历子视图：月 | 周 | 天
export type CalSubView = 'month' | 'week' | 'day';

/* =========================================================
   P2P3 §3.4.2: Week view partial virtualization 常量
   SLOT_HEIGHT_PX_DEFAULT = 36 (仅 SSR/测试环境兜底，实际渲染高度由 CSS var --cal-hour-h 驱动：桌面 56px / 移动 44px)
   ========================================================= */
/** @deprecated — use useHourHeight() hook or getHourHeightCSS utility; SLOT_HEIGHT_PX_DEFAULT is only a fallback for SSR/tests where CSS variables are unavailable. Actual rendered row height is responsive via CSS var (desktop:56px, mobile:44px). **/
export const SLOT_HEIGHT_PX_DEFAULT = 36;
export const ROW_HEIGHT_PX = SLOT_HEIGHT_PX_DEFAULT;
export const SLOT_MINUTES = 15;
export const VIEW_BUFFER_HOURS = 1;
export const ROWS_PER_HOUR = 60 / SLOT_MINUTES; // = 4
export const TOTAL_ROWS = 24 * ROWS_PER_HOUR; // = 96
export const STRESS_ID_START = 1_000_000 as const;

/**
 * Pure helper: 根据容器 scrollTop + viewportHeight + hourHeightPx 计算「可视小时范围」
 * 结果带 ±VIEW_BUFFER_HOURS 缓冲。输出纯数学，便于单元测试。
 * @param hourHeightPx - 实际渲染的每小时像素高度（CSS var --cal-hour-h：桌面 56 / 移动 44）
 */
export function calcVisibleHourRange(
  scrollTopPx: number,
  viewportHeightPx: number,
  hourHeightPx: number,
  totalRows: number = TOTAL_ROWS,
): { visibleStartHour: number; visibleEndHour: number } {
  if (
    !Number.isFinite(scrollTopPx) ||
    !Number.isFinite(viewportHeightPx) ||
    !Number.isFinite(hourHeightPx) ||
    hourHeightPx <= 0
  ) {
    return { visibleStartHour: 0, visibleEndHour: 24 };
  }
  const sPx = Math.max(0, scrollTopPx);
  const vPx = Math.max(0, viewportHeightPx);
  const slotHeightPx = hourHeightPx / ROWS_PER_HOUR;
  const topRow = Math.max(0, Math.floor(sPx / slotHeightPx) - VIEW_BUFFER_HOURS * ROWS_PER_HOUR);
  const bottomRow = Math.min(
    totalRows,
    Math.ceil((sPx + vPx) / slotHeightPx) + VIEW_BUFFER_HOURS * ROWS_PER_HOUR,
  );
  return {
    visibleStartHour: topRow / ROWS_PER_HOUR,
    visibleEndHour: bottomRow / ROWS_PER_HOUR,
  };
}

/**
 * Pure helper: 过滤事件到指定小时范围内（含缓冲，防止快速滚动空白）
 */
export function isHourInViewport(
  startHour: number,
  durationHours: number,
  visibleStartHour: number,
  visibleEndHour: number,
): boolean {
  const evStart = startHour - VIEW_BUFFER_HOURS;
  const evEnd = startHour + durationHours + VIEW_BUFFER_HOURS;
  return evEnd >= visibleStartHour && evStart <= visibleEndHour;
}

// Task G: 事件优先级（专属字段）
export type CalendarEventPriority = 'LOW' | 'MEDIUM' | 'HIGH' | 'URGENT';

// Task G: reminderSettings 类型
export interface ReminderSettings {
  type: 'popup' | 'email' | 'sms';
  minutesAhead: number;
}

// 事件类型颜色映射
export const EVENT_TYPE_COLORS: Record<string, string> = {
  due_date: 'bg-rose-50 dark:bg-rose-900/30 text-rose-700 dark:text-rose-300 ring-1 ring-rose-200/60 dark:ring-rose-700/40',
  task: 'bg-blue-50 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300 ring-1 ring-blue-200/60 dark:ring-blue-700/40',
  meeting: 'bg-violet-50 dark:bg-violet-900/30 text-violet-700 dark:text-violet-300 ring-1 ring-violet-200/60 dark:ring-violet-700/40',
  reminder: 'bg-amber-50 dark:bg-amber-900/30 text-amber-700 dark:text-amber-300 ring-1 ring-amber-200/60 dark:ring-amber-700/40',
};

/**
 * 防御性把任意值转换为合法 Date 对象。
 * 背景：zustand store 是单例，跨路由跳转 + 持久化 / SSR / 动态模块加载等极端场景下，
 *       calMonth / calCursor 有可能被写入了 null / undefined / 字符串 / Invalid Date，
 *       直接 .getFullYear() 就抛 TypeError，被 ErrorBoundary 捕获后整页显示"页面发生错误"。
 *       这种崩溃"偶尔出现，刷新又恢复"——刷新后 store 重新初始化，值重新变成合法 Date。
 */
export function safeDate(value: unknown, fallback: Date = new Date()): Date {
  if (value instanceof Date && Number.isFinite(value.getTime())) {
    return value;
  }
  if (typeof value === 'number' || typeof value === 'string') {
    const t = new Date(value);
    if (Number.isFinite(t.getTime())) return t;
  }
  // 所有路径都无法识别 → 用今天兜底。不抛异常，不打崩页面。
  const fb = fallback instanceof Date && Number.isFinite(fallback.getTime())
    ? fallback
    : new Date();
  return new Date(fb.getFullYear(), fb.getMonth(), fb.getDate(), fb.getHours(), fb.getMinutes(), fb.getSeconds(), fb.getMilliseconds());
}

// 状态颜色映射
export const STATUS_COLORS: Record<string, string> = {
  pending: 'bg-amber-50 dark:bg-amber-900/30 text-amber-700 dark:text-amber-300 ring-1 ring-amber-200/60 dark:ring-amber-700/40',
  completed: 'bg-green-50 dark:bg-green-900/30 text-green-700 dark:text-green-300 ring-1 ring-green-200/60 dark:ring-green-700/40',
  cancelled: 'bg-gray-50 dark:bg-slate-700/40 text-gray-700 dark:text-slate-300 ring-1 ring-gray-200/60 dark:ring-slate-600/40',
};

// 事件类型完整样式（含 dotClass，用于 chip 小圆点 + badgeClass）
// label 是**读取时 getter**：形状与字段名保持不变，消费方（chips / views / panels）的
// `EVENT_TYPE_STYLE[x].label` 一行都不用改即跟随当前语言（先例：constants/timezones.ts）。
export const EVENT_TYPE_STYLE: Record<string, { label: string; badgeClass: string; dotClass: string }> = {
  due_date: { get label() { return tBound('calendar.store.eventType.dueDate'); }, badgeClass: EVENT_TYPE_COLORS.due_date, dotClass: 'bg-rose-500' },
  task:     { get label() { return tBound('calendar.store.eventType.task'); },     badgeClass: EVENT_TYPE_COLORS.task,     dotClass: 'bg-blue-500' },
  reminder: { get label() { return tBound('calendar.store.eventType.reminder'); }, badgeClass: EVENT_TYPE_COLORS.reminder, dotClass: 'bg-amber-500' },
  meeting:  { get label() { return tBound('calendar.store.eventType.meeting'); },  badgeClass: EVENT_TYPE_COLORS.meeting,  dotClass: 'bg-violet-500' },
  personal: { get label() { return tBound('calendar.store.eventType.personal'); }, badgeClass: 'bg-pink-50 dark:bg-pink-900/30 text-pink-700 dark:text-pink-300 ring-1 ring-pink-200/60 dark:ring-pink-700/40', dotClass: 'bg-pink-500' },
  travel:   { get label() { return tBound('calendar.store.eventType.travel'); },   badgeClass: 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-300 ring-1 ring-emerald-200/60 dark:ring-emerald-700/40', dotClass: 'bg-emerald-500' },
  work:     { get label() { return tBound('calendar.store.eventType.work'); },     badgeClass: 'bg-sky-50 dark:bg-sky-900/30 text-sky-700 dark:text-sky-300 ring-1 ring-sky-200/60 dark:ring-sky-700/40', dotClass: 'bg-sky-500' },
  other:    { get label() { return tBound('calendar.store.eventType.other'); },    badgeClass: 'bg-slate-50 dark:bg-slate-800/40 text-slate-700 dark:text-slate-300 ring-1 ring-slate-200/60 dark:ring-slate-600/40', dotClass: 'bg-slate-500' },
};

// 状态完整样式（chips 等组件使用；label 同为读取时 getter）
export const STATUS_STYLE: Record<string, { label: string; badgeClass: string }> = {
  pending:     { get label() { return tBound('calendar.store.status.pending'); },     badgeClass: STATUS_COLORS.pending },
  in_progress: { get label() { return tBound('calendar.store.status.inProgress'); },  badgeClass: 'bg-sky-50 dark:bg-sky-900/30 text-sky-700 dark:text-sky-300 ring-1 ring-sky-200/60 dark:ring-sky-700/40' },
  completed:   { get label() { return tBound('calendar.store.status.completed'); },   badgeClass: STATUS_COLORS.completed },
  cancelled:   { get label() { return tBound('calendar.store.status.cancelled'); },   badgeClass: STATUS_COLORS.cancelled },
  missed:      { get label() { return tBound('calendar.store.status.missed'); },      badgeClass: 'bg-rose-50 dark:bg-rose-900/30 text-rose-700 dark:text-rose-300 ring-1 ring-rose-200/60 dark:ring-rose-700/40' },
};

export const EVENT_TYPE_OPTIONS = [
  { value: 'due_date', get label() { return tBound('calendar.store.eventType.dueDate'); } },
  { value: 'task', get label() { return tBound('calendar.store.eventType.task'); } },
  { value: 'meeting', get label() { return tBound('calendar.store.eventType.meeting'); } },
  { value: 'reminder', get label() { return tBound('calendar.store.eventType.reminder'); } },
];

export const STATUS_OPTIONS = [
  // 「待处理」与 STATUS_STYLE 的「待办」逐字不同 → 独立键
  { value: 'pending', get label() { return tBound('calendar.store.status.pendingOption'); } },
  { value: 'in_progress', get label() { return tBound('calendar.store.status.inProgress'); } },
  { value: 'completed', get label() { return tBound('calendar.store.status.completed'); } },
  { value: 'cancelled', get label() { return tBound('calendar.store.status.cancelled'); } },
  { value: 'missed', get label() { return tBound('calendar.store.status.missed'); } },
];

// 颜色选项（纯前端调色板：后端 `CalendarEvent.Color` 是自由 hex 字符串、无枚举与白名单，
// 故这里的 value 不是"后端产出方枚举"，只有 label 需要进字典）
export const COLOR_OPTIONS = [
  // 红色系
  { value: '#ef4444', get label() { return tBound('calendar.store.color.red'); } },
  { value: '#f43f5e', get label() { return tBound('calendar.store.color.rose'); } },
  { value: '#e11d48', get label() { return tBound('calendar.store.color.crimson'); } },
  // 橙黄系
  { value: '#f97316', get label() { return tBound('calendar.store.color.orange'); } },
  { value: '#f59e0b', get label() { return tBound('calendar.store.color.amber'); } },
  { value: '#eab308', get label() { return tBound('calendar.store.color.yellow'); } },
  { value: '#ca8a04', get label() { return tBound('calendar.store.color.gold'); } },
  // 绿色系
  { value: '#84cc16', get label() { return tBound('calendar.store.color.lime'); } },
  { value: '#22c55e', get label() { return tBound('calendar.store.color.brightGreen'); } },
  { value: '#10b981', get label() { return tBound('calendar.store.color.emerald'); } },
  { value: '#059669', get label() { return tBound('calendar.store.color.darkGreen'); } },
  // 青蓝系
  { value: '#14b8a6', get label() { return tBound('calendar.store.color.teal'); } },
  { value: '#06b6d4', get label() { return tBound('calendar.store.color.sky'); } },
  { value: '#0ea5e9', get label() { return tBound('calendar.store.color.lightBlue'); } },
  { value: '#3b82f6', get label() { return tBound('calendar.store.color.blue'); } },
  { value: '#2563eb', get label() { return tBound('calendar.store.color.darkBlue'); } },
  // 紫粉系
  { value: '#6366f1', get label() { return tBound('calendar.store.color.indigo'); } },
  { value: '#8b5cf6', get label() { return tBound('calendar.store.color.purple'); } },
  { value: '#a855f7', get label() { return tBound('calendar.store.color.brightPurple'); } },
  { value: '#d946ef', get label() { return tBound('calendar.store.color.magenta'); } },
  { value: '#ec4899', get label() { return tBound('calendar.store.color.pink'); } },
  // 中性色
  { value: '#64748b', get label() { return tBound('calendar.store.color.slate'); } },
  { value: '#78716c', get label() { return tBound('calendar.store.color.warmGray'); } },
  // 拓展色：差异更大的色调
  { value: '#7c2d12', get label() { return tBound('calendar.store.color.coffee'); } },
  { value: '#a3e635', get label() { return tBound('calendar.store.color.neonLime'); } },
  { value: '#be185d', get label() { return tBound('calendar.store.color.wine'); } },
];

// 用户调色板：根据 userId 稳定映射，用于多人视图区分
const USER_PALETTE = [
  '#3b82f6', '#10b981', '#f59e0b', '#8b5cf6', '#ec4899',
  '#14b8a6', '#f97316', '#6366f1', '#ef4444', '#06b6d4',
  '#84cc16', '#d946ef', '#0ea5e9', '#a855f7', '#22c55e',
];

export const getUserColor = (userId?: number | null): string => {
  if (!userId) return '#94a3b8';
  return USER_PALETTE[userId % USER_PALETTE.length];
};

export const getInitials = (name?: string | null): string => {
  if (!name) return '?';
  const parts = name.trim().split(/\s+/);
  if (parts.length === 1) return parts[0].slice(0, 1).toUpperCase();
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
};

// 格式化日期时间显示
export function formatDateTime(s: string | null | undefined, isAllDay = false): string {
  if (!s) return '-';
  try {
    const d = new Date(s);
    if (isNaN(d.getTime())) return s;
    // 语言 → Intl 标签：`zh-Hans` 仍是 `zh-CN`（输出逐字不变），`en` 走 `en-US`
    const intlLocale = toIntlLocale(getBoundLocale());
    if (isAllDay) {
      return d.toLocaleDateString(intlLocale, { year: 'numeric', month: '2-digit', day: '2-digit' });
    }
    return d.toLocaleString(intlLocale, {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return s;
  }
}

// 优先级文本（1-5）
export function getPriorityLabel(p: number | null | undefined): string {
  if (p == null) return tBound('calendar.store.priority.unset');
  // 下标 0 刻意保留空串：后端校验允许 priority=0，改造前 `labels[0]` 即 '' 且
  // `?? ` 只在 null/undefined 时兜底 → 0 返回空文本。此处逐字保留该语义。
  const labels: (TranslationKey | '')[] = [
    '',
    'calendar.store.priority.low',
    'calendar.store.priority.lower',
    'calendar.store.priority.medium',
    'calendar.store.priority.higher',
    'calendar.store.priority.high',
  ];
  const key = labels[p];
  if (key === undefined) return `${p}`;
  return key === '' ? '' : tBound(key);
}

// 优先级颜色（用于详情视图中的徽章）
export function getPriorityBadgeClass(p: number | null | undefined): string {
  if (p == null) return 'bg-gray-100 text-gray-500 dark:bg-slate-700 dark:text-slate-400';
  if (p <= 1) return 'bg-slate-100 text-slate-600 dark:bg-slate-700 dark:text-slate-300';
  if (p <= 2) return 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300';
  if (p <= 3) return 'bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300';
  if (p <= 4) return 'bg-orange-100 text-orange-700 dark:bg-orange-900/40 dark:text-orange-300';
  return 'bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300';
}

// 提醒文本
export function getReminderLabel(minutes: number | null | undefined): string {
  if (minutes == null) return '';
  const opt = REMINDER_OPTIONS.find((o) => o.value === minutes);
  if (opt) return opt.label;
  if (minutes === 0) return tBound('calendar.store.reminder.onTime');
  if (minutes < 60) return tParams('calendar.store.reminder.minutesAgo', { minutes });
  if (minutes < 1440) return tParams('calendar.store.reminder.hoursAgo', { hours: Math.floor(minutes / 60) });
  return tParams('calendar.store.reminder.daysAgo', { days: Math.floor(minutes / 1440) });
}

// 重复规则文本
export function getRecurrenceLabel(ev: CalendarEventDto): string {
  if (!ev.isRecurring || ev.recurrenceType == null) return '';
  const interval = ev.recurrenceInterval ?? 1;
  // 单位按 count 取复数（英文 one/other）；中文词条是普通字符串，与改造前逐字一致
  const unit = recurrenceUnitLabel(ev.recurrenceType, interval);
  let text = tParams('calendar.store.recurrence.every', { interval, unit });
  if (ev.recurrenceCount) {
    text += tParams('calendar.store.recurrence.countSuffix', { count: ev.recurrenceCount });
  } else if (ev.recurrenceEndDate) {
    text += tParams('calendar.store.recurrence.untilSuffix', { date: formatDateTime(ev.recurrenceEndDate, true) });
  }
  return text;
}

// 计算事件时长（用于详情显示）
export function getDurationLabel(start: string, end: string | null, isAllDay: boolean): string {
  if (!end) return '';
  try {
    const s = new Date(start).getTime();
    const e = new Date(end).getTime();
    if (isNaN(s) || isNaN(e) || e <= s) return '';
    const diffMs = e - s;
    const days = Math.floor(diffMs / 86400000);
    const hours = Math.floor((diffMs % 86400000) / 3600000);
    const minutes = Math.floor((diffMs % 3600000) / 60000);
    if (isAllDay) {
      if (days > 0) return tParams('calendar.store.duration.allDayDays', { days: days + 1, count: days + 1 });
      return tBound('calendar.common.allDay');
    }
    if (days > 0) return tParams('calendar.store.duration.daysHours', { days, hours, count: days });
    if (hours > 0) {
      const hoursText = tParams('calendar.store.duration.hoursOnly', { hours, count: hours });
      // 分钟段仅在 minutes > 0 时追加（词条自带前导空格，逐字保留）
      return minutes > 0
        ? hoursText + tParams('calendar.store.duration.minutesSuffix', { minutes, count: minutes })
        : hoursText;
    }
    return tParams('calendar.store.duration.minutesOnly', { minutes, count: minutes });
  } catch {
    return '';
  }
}

// 转换本地时间为 ISO 8601（带 Z 后缀）
export function toIsoString(localDateTime: string): string {
  if (!localDateTime) return '';
  // localDateTime 格式: "2026-07-20T15:30"
  const d = new Date(localDateTime);
  if (isNaN(d.getTime())) return localDateTime;
  return d.toISOString();
}

// 仅日期格式 (用于 allDay 事件)
export function toIsoDateString(localDate: string): string {
  if (!localDate) return '';
  // 全天事件不进行时区转换，直接返回 UTC 午夜格式
  return localDate + 'T00:00:00Z';
}

// 将 Date 格式化为 YYYY-MM-DD
export function formatLocalDate(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

// 紧凑时间格式 HH:mm
export function formatHM(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '';
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
}

// 计算事件时长描述（紧凑格式；中文无空格，逐字保留）
export function getEventDuration(startIso: string, endIso: string): string | null {
  const s = new Date(startIso);
  const e = new Date(endIso);
  if (isNaN(s.getTime()) || isNaN(e.getTime()) || e <= s) return null;
  const diffMs = e.getTime() - s.getTime();
  const diffMin = Math.round(diffMs / 60000);
  if (diffMin < 60) return tParams('calendar.store.duration.compactMinutes', { minutes: diffMin });
  const hours = Math.floor(diffMin / 60);
  const mins = diffMin % 60;
  if (hours < 24) {
    return mins > 0
      ? tParams('calendar.store.duration.compactHoursMinutes', { hours, minutes: mins })
      : tParams('calendar.store.duration.compactHours', { hours });
  }
  const days = Math.floor(hours / 24);
  const remainHours = hours % 24;
  return remainHours > 0
    ? tParams('calendar.store.duration.compactDaysHours', { days, hours: remainHours })
    : tParams('calendar.store.duration.compactDays', { days });
}

// 从 ISO 字符提取 datetime-local 值
export function fromIsoToLocalInput(s: string | null | undefined): string {
  if (!s) return '';
  try {
    const d = new Date(s);
    if (isNaN(d.getTime())) return '';
    // 格式: YYYY-MM-DDTHH:MM
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  } catch {
    return '';
  }
}

export function fromIsoToDateInput(s: string | null | undefined): string {
  if (!s) return '';
  try {
    const d = new Date(s);
    if (isNaN(d.getTime())) return '';
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  } catch {
    return '';
  }
}

export const RECURRENCE_TYPE_OPTIONS = [
  { value: 0, get label() { return tBound('calendar.store.recurrenceType.daily'); } },
  { value: 1, get label() { return tBound('calendar.store.recurrenceType.weekly'); } },
  { value: 2, get label() { return tBound('calendar.store.recurrenceType.monthly'); } },
  { value: 3, get label() { return tBound('calendar.store.recurrenceType.yearly'); } },
];

/** 重复单位下标 → 词条 key（0=天 / 1=周 / 2=月 / 3=年） */
const RECURRENCE_UNIT_KEYS: Record<number, TranslationKey> = {
  0: 'calendar.store.recurrenceUnit.day',
  1: 'calendar.store.recurrenceUnit.week',
  2: 'calendar.store.recurrenceUnit.month',
  3: 'calendar.store.recurrenceUnit.year',
};

/** 周几序号（0=周日 .. 6=周六）→ `calendar.common.weekdayShort.*` 词条 key */
const WEEKDAY_SHORT_KEYS: Record<number, TranslationKey> = {
  0: 'calendar.common.weekdayShort.sun',
  1: 'calendar.common.weekdayShort.mon',
  2: 'calendar.common.weekdayShort.tue',
  3: 'calendar.common.weekdayShort.wed',
  4: 'calendar.common.weekdayShort.thu',
  5: 'calendar.common.weekdayShort.fri',
  6: 'calendar.common.weekdayShort.sat',
};

/** 重复单位文本：按 count 解析英文 one/other（中文词条是普通字符串，两态同值） */
export function recurrenceUnitLabel(type: number | null | undefined, count: number): string {
  const key = type == null ? undefined : RECURRENCE_UNIT_KEYS[type];
  return key ? tParams(key, { count }) : '';
}

/**
 * 重复单位的**静态**取值表。
 *
 * ⚠ 它用 `tBound` 取词，**拿不到 count** ⇒ 英文词条是 `{one,other}` 时只会落到 `other`
 * （渲染 "1 days"）。**已知数量的调用点必须改用 `recurrenceUnitLabel(type, count)`**
 * （如 `EditorPanel` 紧邻 interval 输入框的那处），本表只为"不显示数量"的场景保留。
 */
export const RECURRENCE_UNIT_LABELS: Record<number, string> = {
  get 0() { return tBound('calendar.store.recurrenceUnit.day'); },
  get 1() { return tBound('calendar.store.recurrenceUnit.week'); },
  get 2() { return tBound('calendar.store.recurrenceUnit.month'); },
  get 3() { return tBound('calendar.store.recurrenceUnit.year'); },
};

export const REMINDER_OPTIONS = [
  { value: 0, get label() { return tBound('calendar.store.reminderOption.onTime'); } },
  { value: 5, get label() { return tBound('calendar.store.reminderOption.minutes5'); } },
  { value: 15, get label() { return tBound('calendar.store.reminderOption.minutes15'); } },
  { value: 30, get label() { return tBound('calendar.store.reminderOption.minutes30'); } },
  { value: 60, get label() { return tBound('calendar.store.reminderOption.hours1'); } },
  { value: 1440, get label() { return tBound('calendar.store.reminderOption.days1'); } },
  { value: 10080, get label() { return tBound('calendar.store.reminderOption.weeks1'); } },
];

/** 提醒方式选项（popup/email/sms）。T000090：单选下拉 → 多选组合（可同时勾选，存逗号分隔如 "popup,email"）。 */
export const REMINDER_TYPE_OPTIONS: { value: 'popup' | 'email' | 'sms'; label: string }[] = [
  { value: 'popup', get label() { return tBound('calendar.store.reminderType.popup'); } },
  { value: 'email', get label() { return tBound('calendar.store.reminderType.email'); } },
  { value: 'sms', get label() { return tBound('calendar.store.reminderType.sms'); } },
];

const REMINDER_TYPE_LABELS: Record<string, TranslationKey> = {
  popup: 'calendar.store.reminderType.popup',
  email: 'calendar.store.reminderType.email',
  sms: 'calendar.store.reminderType.sms',
};

/** 解析逗号分隔的 reminderType（如 "popup,email"）为去重后的数组；空/null/非法片段自动忽略。 */
export function parseReminderTypes(v?: string | null): Array<'popup' | 'email' | 'sms'> {
  if (!v) return [];
  const seen = new Set<string>();
  const out: Array<'popup' | 'email' | 'sms'> = [];
  for (const raw of v.split(',')) {
    const t = raw.trim().toLowerCase();
    if ((t === 'popup' || t === 'email' || t === 'sms') && !seen.has(t)) {
      seen.add(t);
      out.push(t);
    }
  }
  return out;
}

/** 将提醒方式数组序列化为逗号分隔串；空数组返回 null（表示"默认"，后端按 站内+邮件 处理）。 */
export function serializeReminderTypes(types: Array<'popup' | 'email' | 'sms'>): string | null {
  const uniq = [...new Set(types)];
  return uniq.length ? uniq.join(',') : null;
}

/** 提醒方式的显示文本：组合用 ' + ' 连接；空/null 显示"默认（站内+邮件）"。 */
export function reminderTypesLabel(v?: string | null): string {
  const list = parseReminderTypes(v);
  if (list.length === 0) return tBound('calendar.store.reminderType.default');
  return list.map((t) => {
    const key = REMINDER_TYPE_LABELS[t];
    return key ? tBound(key) : t;
  }).join(' + ');
}

/** 周几序号 → RRULE BYDAY 缩写 */
export const WEEKDAY_TO_BYDAY: Record<number, string> = {
  0: 'SU', 1: 'MO', 2: 'TU', 3: 'WE', 4: 'TH', 5: 'FR', 6: 'SA',
};

/**
 * 纯函数：根据表单生成标准 RRULE 字符串（不含中文描述，便于后端解析）。
 * 返回值例：'FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR'；不重复或参数不足时返回 null。
 *
 * 用于：保存前自动生成最新 RRULE（避免用户忘记点击「生成 RRULE」按钮），
 *      以及生成显示用的混合字符串（RRULE | 描述）。
 */
export function generateRRuleFromForm(
  form: {
    recurrenceType?: number | null;
    recurrenceInterval?: number | null;
    weekdaysSelected?: number[];
    recurrenceCount?: number | null;
    recurrenceEndDate?: string;
  },
  endMode: 'never' | 'count' | 'date',
): string | null {
  if (form.recurrenceType == null || form.recurrenceType < 0) return null;
  const parts: string[] = [];
  const unit = ['DAILY', 'WEEKLY', 'MONTHLY', 'YEARLY'][form.recurrenceType];
  if (!unit) return null;
  const interval = form.recurrenceInterval ?? 1;
  parts.push(`FREQ=${unit}`);
  if (interval > 1) parts.push(`INTERVAL=${interval}`);
  if (form.recurrenceType === 1 && (form.weekdaysSelected?.length ?? 0) > 0) {
    const byday = form.weekdaysSelected!
      .slice()
      .sort()
      .map((d) => WEEKDAY_TO_BYDAY[d])
      .filter(Boolean)
      .join(',');
    if (byday) parts.push(`BYDAY=${byday}`);
  }
  if (endMode === 'count' && form.recurrenceCount) {
    parts.push(`COUNT=${form.recurrenceCount}`);
  }
  if (endMode === 'date' && form.recurrenceEndDate) {
    parts.push(`UNTIL=${form.recurrenceEndDate.replace(/-/g, '')}`);
  }
  return parts.length > 1 || parts[0] ? parts.join(';') : null;
}

/**
 * 纯函数：根据表单生成 RRULE 的可读描述（用于 UI 显示）。
 * 返回值例：'每 2 周周一、周三、周五，共 10 次'；不重复时返回空字符串。
 *
 * 拼接结构（前缀 / 中缀 / 单位 / 周几前缀 / 后缀）与改造前逐字一致，
 * 只是每段改为查字典——`parts.join('')` 空串连接，段间不留额外字符。
 */
export function generateRRuleDescription(
  form: {
    recurrenceType?: number | null;
    recurrenceInterval?: number | null;
    weekdaysSelected?: number[];
    recurrenceCount?: number | null;
    recurrenceEndDate?: string;
  },
  endMode: 'never' | 'count' | 'date',
): string {
  if (form.recurrenceType == null || form.recurrenceType < 0) return '';
  const interval = form.recurrenceInterval ?? 1;
  const unit = recurrenceUnitLabel(form.recurrenceType, interval);
  if (!unit) return '';
  const parts: string[] = [];
  parts.push(tBound('calendar.store.rrule.everyPrefix'));
  if (interval > 1) parts.push(tParams('calendar.store.rrule.intervalMiddle', { interval }));
  parts.push(unit);
  if (form.recurrenceType === 1 && (form.weekdaysSelected?.length ?? 0) > 0) {
    const wdStr = form.weekdaysSelected!
      .slice()
      .sort()
      .map((d) => (WEEKDAY_SHORT_KEYS[d] ? tBound(WEEKDAY_SHORT_KEYS[d]) : ''))
      .filter(Boolean)
      .join(tBound('calendar.store.rrule.weekdaySeparator'));
    if (wdStr) parts.push(tBound('calendar.store.rrule.weekdayPrefix') + wdStr);
  }
  if (endMode === 'count' && form.recurrenceCount) {
    parts.push(tParams('calendar.store.recurrence.countSuffix', { count: form.recurrenceCount }));
  }
  if (endMode === 'date' && form.recurrenceEndDate) {
    parts.push(tParams('calendar.store.recurrence.untilSuffix', { date: form.recurrenceEndDate }));
  }
  return parts.join('');
}

export interface EditorForm {
  title: string;
  description: string;
  eventType: string;
  startAt: string;
  endAt: string;
  isAllDay: boolean;
  mailboxId: number | null;
  userId: number | null;
  color: string;
  status: string;
  priority: number | null;
  progress: number | null;
  location: string;
  meetingUrl: string;
  attendees: number[];
  subTasks: SubTask[];
  relatedEventId: number | null;
  recurrenceExclusions: RecurrenceExclusions | null;
  isRecurring: boolean;
  recurrenceType: number | null;
  recurrenceInterval: number;
  recurrenceEndDate: string;
  recurrenceCount: number | null;
  recurrenceRule: string;
  weekdaysSelected: number[];
  reminderMinutesBefore: number | null;
  reminderEmails: string;
  // T000097: 自定义提醒时间模式标志位。
  //   true  → 用户在下拉菜单选择了"自定义..."，应显示自定义分钟输入框
  //   false/undefined → 选择了预设值（5/15/30/60...）或"不提醒"
  //   注意：不能仅靠"reminderMinutesBefore 是否在 REMINDER_OPTIONS 中"判断，
  //   因为用户从预设值(如30)切换到"自定义"时，原值 30 仍在预设列表里，
  //   会导致下拉框显示"30分钟前"而非"自定义"，自定义输入框也不显示，看起来"无响应"。
  isCustomReminder?: boolean;
  // 提醒方式：多选组合 popup/email/sms，存逗号分隔（如 "popup,email"）到 DTO reminderType 字段；
  // null/空 表示默认（站内 + 邮件，由后端兼容处理）
  reminderType?: string | null;
  // Task G: eventType 专属字段
  meetingLink?: string;
  hostUserIds?: number[];
  agendaIds?: number[];
  taskOwner?: string;
  dueAt?: string;
  eventPriority?: CalendarEventPriority;
  reminderSettings?: ReminderSettings;
}

export const EMPTY_FORM: EditorForm = {
  title: '',
  description: '',
  eventType: 'task',
  startAt: '',
  endAt: '',
  isAllDay: false,
  mailboxId: null,
  userId: null,
  color: '#3b82f6',
  status: 'pending',
  priority: null,
  progress: null,
  location: '',
  meetingUrl: '',
  attendees: [],
  subTasks: [],
  relatedEventId: null,
  recurrenceExclusions: null,
  isRecurring: false,
  recurrenceType: null,
  recurrenceInterval: 1,
  recurrenceEndDate: '',
  recurrenceCount: null,
  recurrenceRule: '',
  weekdaysSelected: [],
  reminderMinutesBefore: 15,
  reminderEmails: '',
  // Task G: eventType 专属字段默认值
  meetingLink: undefined,
  hostUserIds: undefined,
  agendaIds: undefined,
  taskOwner: undefined,
  dueAt: undefined,
  eventPriority: undefined,
  reminderSettings: undefined,
};

// ===================== P0 新增辅助函数 =====================
export type YMD = string;

export function toYMDKey(d: Date | string): YMD {
  const dt = typeof d === 'string' ? new Date(d) : d;
  const y = dt.getFullYear();
  const m = String(dt.getMonth() + 1).padStart(2, '0');
  const day = String(dt.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

/* =========================================================
   P1-A#2: HourRow 共享类型 + per-day 事件分桶公共纯函数
   (原 chips.tsx 定义 HourRowColumnEvent，DayView/WeekView 各写一份分桶逻辑，
   现统一收敛到此处：chips re-export，视图层调用 _buildSingleDayColumnEvents)
   ========================================================= */
/** 单个事件在小时格子内的渲染片段（跨小时会被拆成多条）。
 *  原定义在 chips.tsx，为避免循环依赖统一迁移至此，chips.tsx 通过 re-export 保持对外 API 不变。 */
export interface HourRowColumnEvent {
  chipKey: string;
  eventId: number;
  title: string;
  startOffsetMin: number;
  durationMin: number;
  color: string | null;
  eventType: string;
  isAllDay: boolean;
  /** 完整事件的 startAt（ISO string），用于 resize 时计算正确的 endAt */
  eventStartAt: string;
  /** 完整事件的 endAt（ISO string），用于 resize 时基于原结束时间计算新 endAt */
  eventEndAt: string;
  /** 是否为跨小时事件的最后一段（包含实际结束时间），resize 手柄仅在此段渲染 */
  isLastSegment: boolean;
  /** 创建者姓名（始终为创建者，不随修改者变化） */
  createdByName?: string | null;
}
/** 一天内按小时索引的 HourRowColumnEvent 桶 */
export interface HourRowEventByHour {
  [hour: number]: HourRowColumnEvent[];
}
/** _buildSingleDayColumnEvents 返回结构：全天事件 + 按小时分桶的定时事件 */
export interface SingleDayColumnEvents {
  allDayEvents: CalendarEventDto[];
  timedByHour: HourRowEventByHour;
}
/** 纯函数：对单天 YMD 的事件列表做分桶（全天 vs 按小时），并把跨小时事件拆成多条 chip。
 *  DayView.tsx 对单个 YMD 调用一次；WeekView.tsx 对 7 天各调用一次。
 *  这是 P1-A#2 统一抽取的核心逻辑，原先在 DayView.buildColumnEventsForDay 和
 *  WeekView.buildColumnEventsForWeek 中复制了两份 ~80 行同构代码。 */
export function _buildSingleDayColumnEvents(
  ymd: YMD,
  evts: readonly CalendarEventDto[],
): SingleDayColumnEvents {
  const allDayEvents: CalendarEventDto[] = [];
  const timedByHour: HourRowEventByHour = {};
  const { y: ymdY, m: ymdM, d: ymdD } = parseYMD(ymd);
  // 当日区间：当天 00:00:00 ~ 次日 00:00:00（本地时间构造，避免 UTC 日期偏移）
  const dayStart = new Date(ymdY, ymdM - 1, ymdD, 0, 0, 0, 0);
  const dayEnd = new Date(ymdY, ymdM - 1, ymdD + 1, 0, 0, 0, 0);
  const dayStartTs = +dayStart;
  const dayEndTs = +dayEnd;

  for (const ev of evts) {
    if (ev.isAllDay) {
      // iterateEventDates 已按全天事件覆盖日期分桶，进桶即算当天
      allDayEvents.push(ev);
      continue;
    }
    const startDt = new Date(ev.startAt);
    const startTs = +startDt;
    const endDt = ev.endAt ? new Date(ev.endAt) : new Date(startTs + 60 * 60 * 1000);
    const endTs = Math.max(startTs + 60 * 1000, +endDt); // 至少 1 分钟，避免 0 时长导致不渲染

    // 事件在「当前 YMD」的时间段切片：与当天求交集
    const segStartTs = Math.max(startTs, dayStartTs);
    const segEndTs = Math.min(endTs, dayEndTs);
    if (segStartTs >= segEndTs) continue; // 理论上 iterateEventDates 不会分空桶，这里做双重保险

    // 切片相对当天 00:00 的起始/结束分钟数（用于拆小时段）
    const segStartRelMin = Math.floor((segStartTs - dayStartTs) / 60000);
    const segEndRelMin = Math.ceil((segEndTs - dayStartTs) / 60000);
    const totalDurationMin = Math.max(1, Math.round((endTs - startTs) / 60000));
    void totalDurationMin;

    const segStartHour = Math.floor(segStartRelMin / 60);
    const segStartMin = segStartRelMin % 60;
    const segEndHour = Math.floor((segEndRelMin - 1) / 60); // 最后一分钟归属的小时
    const segEndMinInHour = segEndRelMin - segEndHour * 60;

    const eventStartAt = ev.startAt;
    const eventEndAt = ev.endAt || ev.startAt;
    // 是否为「完整事件」的最后一段（即当天切片结束 == 事件结束，且当天是事件最后一天）—— resize 手柄只挂在完整事件最后一段
    //  iterateEventDates 保证每天 bucket 都存在，因此最后一天 = dayEndTs >= endTs
    const isLastDayForEvent = dayEndTs >= endTs;

    let remainingMinInSeg = Math.max(1, Math.round((segEndTs - segStartTs) / 60000));
    const endHourNorm = segEndHour < segStartHour ? segEndHour + 24 : segEndHour;
    for (let hNorm = segStartHour; hNorm <= endHourNorm; hNorm++) {
      const h = hNorm % 24;
      const isFirstInSeg = hNorm === segStartHour;
      const isLastInSeg = hNorm === endHourNorm;
      const offsetMin = isFirstInSeg ? segStartMin : 0;
      let durMin: number;
      if (isFirstInSeg && isLastInSeg) {
        durMin = remainingMinInSeg;
      } else if (isFirstInSeg) {
        durMin = 60 - segStartMin;
      } else if (isLastInSeg) {
        durMin = segEndMinInHour;
      } else {
        durMin = 60;
      }
      durMin = Math.max(1, Math.min(durMin, remainingMinInSeg));
      if (durMin <= 0) break;
      // 完整事件的最后一段 = 当天是最后一天 AND 当前是当天切片的最后一段
      const isLastSegment = isLastDayForEvent && isLastInSeg;
      if (!timedByHour[h]) timedByHour[h] = [];
      timedByHour[h].push({
        chipKey: `${ev.id}-${ymd}-h${h}${ev.recurrenceOriginalStart ? '#' + ev.recurrenceOriginalStart : ''}`,
        eventId: ev.id,
        title: ev.title,
        startOffsetMin: offsetMin,
        durationMin: durMin,
        color: ev.color ?? null,
        eventType: ev.eventType,
        isAllDay: false,
        eventStartAt,
        eventEndAt,
        isLastSegment,
        createdByName: ev.createdByName ?? null,
      });
      remainingMinInSeg -= durMin;
      if (remainingMinInSeg <= 0) break;
    }
  }
  return { allDayEvents, timedByHour };
}

export function iterateEventDates(ev: CalendarEventDto): YMD[] {
  const start = new Date(ev.startAt);
  const end = ev.endAt ? new Date(ev.endAt) : start;
  const out: YMD[] = [];
  // P2-11: 全天事件使用 UTC 日期避免跨时区偏移导致日期错位
  // 例如 isAllDay=true, startAt=2026-08-25T00:00:00Z 在 UTC-5 会变成 2026-08-24 19:00 本地时间
  // 全天事件应固定显示在 UTC 8月25日格子，而非本地时间 8月24日
  if (ev.isAllDay) {
    const startUtc = Date.UTC(start.getUTCFullYear(), start.getUTCMonth(), start.getUTCDate());
    const endUtc = Date.UTC(end.getUTCFullYear(), end.getUTCMonth(), end.getUTCDate());
    let cursorUtc = startUtc;
    let count = 0;
    while (cursorUtc <= endUtc && count < 366) {
      const d = new Date(cursorUtc);
      // 直接用 UTC 日期构造 YMD，避免 toYMDKey 的本地时间转换导致日期错位
      const y = d.getUTCFullYear();
      const m = String(d.getUTCMonth() + 1).padStart(2, '0');
      const day = String(d.getUTCDate()).padStart(2, '0');
      out.push(`${y}-${m}-${day}` as YMD);
      cursorUtc += 24 * 60 * 60 * 1000;
      count++;
    }
    return out;
  }
  // 普通事件：使用本地时间日期（用户期望看到本地时间哪天有事件）
  const cursor = new Date(start.getFullYear(), start.getMonth(), start.getDate());
  const endDay = new Date(end.getFullYear(), end.getMonth(), end.getDate());
  while (cursor <= endDay) {
    out.push(toYMDKey(cursor));
    cursor.setDate(cursor.getDate() + 1);
    if (out.length > 366) break;
  }
  return out;
}

export function buildEventsByDate(events: CalendarEventDto[]): Map<YMD, CalendarEventDto[]> {
  const map = new Map<YMD, CalendarEventDto[]>();
  for (const ev of events) {
    // 软删除过滤：已删除日程不应出现在月/周/天/列表视图（仅垃圾箱显示）
    if (ev == null) continue;
    if (ev.deletedAt != null && ev.deletedAt !== '') continue;
    for (const ymd of iterateEventDates(ev)) {
      const arr = map.get(ymd);
      if (arr) arr.push(ev); else map.set(ymd, [ev]);
    }
  }
  for (const arr of map.values()) arr.sort((a, b) => +new Date(a.startAt) - +new Date(b.startAt));
  return map;
}

/* =========================================================
   P2P3 §3.4.1: Incremental eventsByDate rebuild
   仅重建受影响的 YMD bucket，未变化的 bucket 保留 SAME REFERENCE
   ========================================================= */

export function rebuildEventsByDateIncremental(
  allEvents: CalendarEventDto[],
  prevMap: Map<YMD, CalendarEventDto[]>,
): Map<YMD, CalendarEventDto[]> {
  if (!Array.isArray(allEvents)) allEvents = [];
  const currentBuckets = new Map<YMD, CalendarEventDto[]>();
  for (const ev of allEvents) {
    // 软删除过滤：已删除日程不应出现在月/周/天/列表视图（仅垃圾箱显示）
    if (ev == null) continue;
    if (ev.deletedAt != null && ev.deletedAt !== '') continue;
    for (const ymd of iterateEventDates(ev)) {
      const arr = currentBuckets.get(ymd);
      if (arr) arr.push(ev); else currentBuckets.set(ymd, [ev]);
    }
  }
  for (const arr of currentBuckets.values()) {
    arr.sort((a, b) => +new Date(a.startAt) - +new Date(b.startAt));
  }

  const allYMDs = new Set<YMD>();
  for (const k of prevMap.keys()) allYMDs.add(k);
  for (const k of currentBuckets.keys()) allYMDs.add(k);

  function isSameBucketRefs(
    a: CalendarEventDto[] | undefined,
    b: CalendarEventDto[] | undefined,
  ): boolean {
    if (!a && !b) return true;
    if (!a || !b) return false;
    if (a.length !== b.length) return false;
    const L = a.length;
    for (let i = 0; i < L; i++) {
      if (a[i] !== b[i]) return false;
    }
    return true;
  }

  const nextMap = new Map<YMD, CalendarEventDto[]>();
  for (const ymd of allYMDs) {
    const prevList = prevMap.get(ymd);
    const currList = currentBuckets.get(ymd);
    if (isSameBucketRefs(prevList, currList)) {
      nextMap.set(ymd, prevList ?? []);
    } else if (currList) {
      nextMap.set(ymd, currList);
    } else {
      nextMap.set(ymd, []);
    }
  }

  return nextMap;
}

// YYYY-MM-DD → { y, m, d }（m 从 1 开始）
export function parseYMD(ymd: YMD): { y: number; m: number; d: number } {
  const [y, m, d] = ymd.split('-').map(Number);
  return { y, m, d };
}

// formatDate：默认 'full' YYYY/MM/DD；'short' 输出 MM/DD
export function formatDate(d: Date, style: 'short' | 'full' = 'full'): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  if (style === 'short') return `${pad(d.getMonth() + 1)}/${pad(d.getDate())}`;
  return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())}`;
}

// formatDateTimeShort：列表视图紧凑格式。isAllDay=true 只显示 'MM/DD'，否则显示 'MM/DD HH:mm'。
// 跨天日程统一按"起始 - 结束"各自带时间展示，如：08/18 09:00 - 08/19 10:00
export function formatDateTimeShort(d: Date, isAllDay: boolean): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  const datePart = `${pad(d.getMonth() + 1)}/${pad(d.getDate())}`;
  if (isAllDay) return datePart;
  return `${datePart} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/**
 * 获取农历日的中文名称（如"十九"、"二十"、"初一"）。
 * 农历初一时返回"月名+初一"（如"七月初一"），便于直观标识农历月边界。
 * lunar-javascript 支持范围外的日期返回空字符串。
 *
 * ⚠ **英文界面返回空串**（队长修复 W13-V3）：`lunar-javascript` 只有中文数据集
 * （`getDayInChinese()` 取 `LunarUtil.DAY`），英文界面下每个日期格都会挂一个中文字
 * （`初二`/`廿一`…）以及混排的 `八 (lunar 1st)`，与"英文可用"直接冲突；而英文用户
 * 也不需要农历注记（西方日历惯例不显示）。故按绑定语言**隐藏**该注记。
 * 若将来希望英文界面也显示农历，可行做法是只显示**数值**农历日（不再用库的中文串）。
 */
export function getLunarDayLabel(date: Date): string {
  if (getBoundLocale().toLowerCase().startsWith('en')) return '';
  try {
    const lunar = Solar.fromDate(date).getLunar();
    const dayInChinese = lunar.getDayInChinese() ?? '';
    // 农历初一：用**数值日**判断（`lunar.getDay() === 1`），而不是把第三方库产出的
    // 中文常量 '初一' 写死在源码里。两者等价——`getDayInChinese()` 的实现就是
    // `LunarUtil.DAY[this._p.day]`（node_modules/lunar-javascript/lunar.js:975）。
    if (lunar.getDay() === 1) {
      return tParams('calendar.store.lunar.monthFirstDay', { month: lunar.getMonthInChinese() });
    }
    return dayInChinese;
  } catch {
    return '';
  }
}

/* =========================================================
   P2-A#1: Overview KPI 计算函数（纯函数，基于 allEvents 派生指标）
   - countTodayEvents:        今日事件数（startAt 落在今日 YMD，排除已取消）
   - countCompletedThisWeek:  本周已完成事件数（startAt 落在本周一~周日，且 status=completed）
   - countConflicts:          冲突事件数（同日定时事件存在时间区间重叠，按涉及事件去重计数）
   - countTomorrowEvents:     明日日程数（startAt 落在明日 YMD，排除已取消）
   所有函数对无效日期/空入参安全返回 0。
   ========================================================= */

/** 判断事件是否被视为「已取消」（兼容 cancelled / canceled 两种拼写） */
function isCancelledEvent(ev: CalendarEventDto): boolean {
  return ev.status === 'cancelled' || ev.status === 'canceled';
}

/** 返回本周一 00:00 的 Date（周一为一周第一天） */
function getMondayOfThisWeek(now: Date = new Date()): Date {
  const d = new Date(now);
  d.setHours(0, 0, 0, 0);
  const dayIdx = (d.getDay() + 6) % 7; // 0=周一 ... 6=周日
  d.setDate(d.getDate() - dayIdx);
  return d;
}

/** 返回本周日 23:59:59.999 的 Date */
function getSundayEndOfThisWeek(now: Date = new Date()): Date {
  const monday = getMondayOfThisWeek(now);
  const sun = new Date(monday);
  sun.setDate(monday.getDate() + 6);
  sun.setHours(23, 59, 59, 999);
  return sun;
}

/** 返回今日 00:00 的 Date */
function getTodayStart(now: Date = new Date()): Date {
  const d = new Date(now);
  d.setHours(0, 0, 0, 0);
  return d;
}

/** 返回今日 23:59:59.999 的 Date */
function getTodayEnd(now: Date = new Date()): Date {
  const d = getTodayStart(now);
  d.setHours(23, 59, 59, 999);
  return d;
}

/** 返回明日 00:00 的 Date */
function getTomorrowStart(now: Date = new Date()): Date {
  const d = getTodayStart(now);
  d.setDate(d.getDate() + 1);
  return d;
}

/** 返回明日 23:59:59.999 的 Date */
function getTomorrowEnd(now: Date = new Date()): Date {
  const d = getTomorrowStart(now);
  d.setHours(23, 59, 59, 999);
  return d;
}

/**
 * 今日事件数：startAt 落在今日 [00:00, 23:59:59.999]，排除已取消事件。
 * 全天事件按 startAt 的 YMD 判断归属日。
 */
export function countTodayEvents(events: readonly CalendarEventDto[], now: Date = new Date()): number {
  if (!events || events.length === 0) return 0;
  const start = +getTodayStart(now);
  const end = +getTodayEnd(now);
  let count = 0;
  for (const ev of events) {
    if (!ev || isCancelledEvent(ev)) continue;
    const t = +new Date(ev.startAt);
    if (!isNaN(t) && t >= start && t <= end) count++;
  }
  return count;
}

/**
 * 本周已完成事件数：startAt 落在本周一 00:00 ~ 周日 23:59:59.999，且 status === 'completed'。
 */
export function countCompletedThisWeek(events: readonly CalendarEventDto[], now: Date = new Date()): number {
  if (!events || events.length === 0) return 0;
  const start = +getMondayOfThisWeek(now);
  const end = +getSundayEndOfThisWeek(now);
  let count = 0;
  for (const ev of events) {
    if (!ev) continue;
    if (ev.status !== 'completed') continue;
    const t = +new Date(ev.startAt);
    if (!isNaN(t) && t >= start && t <= end) count++;
  }
  return count;
}

/**
 * 冲突事件数：同一天内的定时事件（非全天）存在时间区间重叠时，将所有参与冲突的事件（去重）计入。
 * 全天事件不参与冲突判定。
 * 返回的是「涉及冲突的事件数」（即至少与另一个事件冲突的事件数量）。
 */
export function countConflicts(events: readonly CalendarEventDto[]): number {
  if (!events || events.length < 2) return 0;
  // 仅保留有效定时事件（排除全天 + 已取消）
  const timed: { id: number; start: number; end: number; ymd: string }[] = [];
  for (const ev of events) {
    if (!ev || ev.isAllDay || isCancelledEvent(ev)) continue;
    const s = +new Date(ev.startAt);
    if (isNaN(s)) continue;
    let e: number;
    if (ev.endAt) {
      e = +new Date(ev.endAt);
      if (isNaN(e) || e < s) e = s;
    } else {
      e = s;
    }
    timed.push({ id: ev.id, start: s, end: e, ymd: toYMDKey(new Date(s)) });
  }
  if (timed.length < 2) return 0;

  // 按 YMD 分桶
  const byDay = new Map<string, typeof timed>();
  for (const t of timed) {
    const arr = byDay.get(t.ymd);
    if (arr) arr.push(t);
    else byDay.set(t.ymd, [t]);
  }

  const conflictIds = new Set<number>();
  for (const arr of byDay.values()) {
    if (arr.length < 2) continue;
    // 按开始时间排序后做 O(n^2) 区间重叠检测（单日事件数量通常很少）
    arr.sort((a, b) => a.start - b.start);
    for (let i = 0; i < arr.length; i++) {
      for (let j = i + 1; j < arr.length; j++) {
        // 区间重叠：a.start < b.end && b.start < a.end
        if (arr[i].start < arr[j].end && arr[j].start < arr[i].end) {
          conflictIds.add(arr[i].id);
          conflictIds.add(arr[j].id);
        }
      }
    }
  }
  return conflictIds.size;
}

/**
 * 明日日程数：startAt 落在明日 [00:00, 23:59:59.999]，排除已取消事件。
 */
export function countTomorrowEvents(events: readonly CalendarEventDto[], now: Date = new Date()): number {
  if (!events || events.length === 0) return 0;
  const start = +getTomorrowStart(now);
  const end = +getTomorrowEnd(now);
  let count = 0;
  for (const ev of events) {
    if (!ev || isCancelledEvent(ev)) continue;
    const t = +new Date(ev.startAt);
    if (!isNaN(t) && t >= start && t <= end) count++;
  }
  return count;
}
