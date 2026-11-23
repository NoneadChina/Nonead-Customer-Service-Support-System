import { useMemo, useCallback, useState, useEffect, memo } from 'react';
import {
  CheckSquare as CheckSquareIcon,
  Square as SquareIcon,
  Settings as SettingsIcon,
  Pencil as PencilIcon,
  Check as CheckIcon,
  Trash2 as TrashIcon,
  Copy as CopyIcon,
  ArrowUp as ArrowUpIcon,
  ArrowDown as ArrowDownIcon,
  ArrowUpDown as ArrowUpDownIcon,
  MapPin as MapPinIcon,
  Bell as BellIcon,
  Repeat as RepeatIcon,
} from 'lucide-react';
import { useCalendarStore, getErrorStatus, getErrorData } from '../store';
import type { ApiErrorResponseBody, ConflictResponseData } from '../store';
import { EVENT_TYPE_STYLE, STATUS_STYLE, formatDateTimeShort, toYMDKey, getRecurrenceLabel } from '../calendarUtils';
import { _colorOverrideStyle, getEventTypeStyle, getStatusStyle } from '../chips';
import { EmptyState } from '../chips';
import type { CalendarEventDto } from '../../../api/types';
import { usePermission } from '../../../hooks/usePermission';
import { useI18n } from '../../../context/I18nContext';
import type { TranslationKey } from '../../../locales/zh-Hans';
import { Segmented } from '../components/Segmented';
import { BatchMultiSelectToolbar } from '../components/BatchMultiSelectToolbar';
import { calendarApi } from '../../../api';

type ListFilterKey = 'today' | 'week' | 'month' | 'custom';

const PRESETS: { value: ListFilterKey; labelKey: TranslationKey }[] = [
  { value: 'today', labelKey: 'calendar.views.list.presetToday' },
  { value: 'week', labelKey: 'calendar.views.list.presetWeek' },
  { value: 'month', labelKey: 'calendar.views.list.presetMonth' },
  { value: 'custom', labelKey: 'calendar.common.custom' },
];

const EVENT_TYPE_PRESETS: string[] = ['all', ...Object.keys(EVENT_TYPE_STYLE)];
const STATUS_PRESETS: string[] = ['all', ...Object.keys(STATUS_STYLE)];

/* =========================================================
   列定义 + 列设置（功能 A）
   ========================================================= */

/** 所有可显示的列 key */
type ColKey = 'title' | 'eventType' | 'status' | 'time' | 'assignee' | 'priority' | 'progress' | 'location' | 'recurrence' | 'reminder';

interface ColDef {
  key: ColKey;
  labelKey: TranslationKey;
  defaultVisible: boolean;
  sortable: boolean;
  width: string;
}

const ALL_COLUMNS: ColDef[] = [
  { key: 'title',      labelKey: 'calendar.common.title',    defaultVisible: true,  sortable: false, width: '' },
  { key: 'eventType',  labelKey: 'calendar.common.type',     defaultVisible: true,  sortable: false, width: 'w-24' },
  { key: 'status',     labelKey: 'calendar.common.status',   defaultVisible: true,  sortable: false, width: 'w-24' },
  { key: 'time',       labelKey: 'calendar.views.list.colTime', defaultVisible: true, sortable: true, width: 'w-64' },
  { key: 'assignee',   labelKey: 'calendar.common.assignee', defaultVisible: true,  sortable: false, width: 'w-28' },
  { key: 'priority',   labelKey: 'calendar.common.priority', defaultVisible: true,  sortable: true,  width: 'w-16' },
  { key: 'progress',   labelKey: 'calendar.common.progress', defaultVisible: true,  sortable: true,  width: 'w-20' },
  { key: 'location',   labelKey: 'calendar.common.location', defaultVisible: true,  sortable: false, width: 'w-32' },
  { key: 'recurrence', labelKey: 'calendar.views.list.recurrence', defaultVisible: true, sortable: false, width: 'w-20' },
  { key: 'reminder',   labelKey: 'calendar.common.reminder', defaultVisible: true,  sortable: false, width: 'w-24' },
];

const COL_SETTING_KEY = 'calendar:listView:columns';

function loadVisibleCols(): Set<ColKey> {
  try {
    const raw = localStorage.getItem(COL_SETTING_KEY);
    if (raw) {
      const arr = JSON.parse(raw);
      if (Array.isArray(arr) && arr.length > 0) {
        return new Set(arr.filter((k: unknown): k is ColKey => typeof k === 'string'));
      }
    }
  } catch { /* noop */ }
  return new Set(ALL_COLUMNS.filter((c) => c.defaultVisible).map((c) => c.key));
}

function saveVisibleCols(cols: Set<ColKey>) {
  try {
    localStorage.setItem(COL_SETTING_KEY, JSON.stringify(Array.from(cols)));
  } catch { /* noop */ }
}

/* =========================================================
   排序（功能 C）
   ========================================================= */
type SortField = 'time' | 'priority' | 'progress' | 'createdAt';
type SortDir = 'asc' | 'desc';

/** 词条取值函数类型（`useI18n().t`）。非组件函数按参数传入，避免用模块级 tBound
 *  而在语言切换时不重渲染——视图文案都走渲染路径，必须随 Provider 重新求值。 */
type TFn = ReturnType<typeof useI18n>['t'];

/* =========================================================
   提醒分钟 → 友好文案
   ========================================================= */
function reminderLabel(minutes: number | null | undefined, t: TFn): string {
  if (minutes == null || minutes <= 0) return '';
  if (minutes < 60) return t('calendar.views.list.reminderMinutes', { count: minutes });
  if (minutes < 1440) return t('calendar.views.list.reminderHours', { count: Math.floor(minutes / 60) });
  return t('calendar.views.list.reminderDays', { count: Math.floor(minutes / 1440) });
}

/* =========================================================
   列设置弹出面板
   ========================================================= */
function ColumnSettingsPopover({ visibleCols, onChange, onClose }: {
  visibleCols: Set<ColKey>;
  onChange: (next: Set<ColKey>) => void;
  onClose: () => void;
}) {
  const { t } = useI18n();
  const toggle = (key: ColKey) => {
    const next = new Set(visibleCols);
    if (next.has(key)) next.delete(key);
    else next.add(key);
    // 至少保留一列
    if (next.size === 0) return;
    onChange(next);
  };
  return (
    <div className="fixed inset-0 z-50" onClick={onClose}>
      <div
        className="absolute right-4 top-16 w-56 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 shadow-xl p-3"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="text-xs font-semibold text-slate-600 dark:text-slate-300 mb-2">{t('calendar.views.list.columnSettingsTitle')}</div>
        <div className="space-y-1">
          {ALL_COLUMNS.map((col) => (
            <label
              key={col.key}
              className="flex items-center gap-2 px-2 py-1.5 rounded hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer transition-colors"
            >
              <button
                type="button"
                onClick={() => toggle(col.key)}
                className="shrink-0"
              >
                {visibleCols.has(col.key) ? (
                  <CheckSquareIcon className="w-4 h-4 text-sky-500" />
                ) : (
                  <SquareIcon className="w-4 h-4 text-slate-400" />
                )}
              </button>
              <span className="text-sm text-slate-700 dark:text-slate-200">{t(col.labelKey)}</span>
            </label>
          ))}
        </div>
        <div className="mt-2 pt-2 border-t border-slate-200 dark:border-slate-700 flex justify-end">
          <button
            type="button"
            onClick={() => {
              const def = new Set(ALL_COLUMNS.filter((c) => c.defaultVisible).map((c) => c.key));
              onChange(def);
            }}
            className="text-xs text-sky-600 dark:text-sky-400 hover:underline"
          >
            {t('calendar.views.list.restoreDefaults')}
          </button>
        </div>
      </div>
    </div>
  );
}

/* =========================================================
   排序表头单元格
   ========================================================= */
function SortableTh({
  label,
  field,
  sortField,
  sortDir,
  onSort,
  widthCls,
}: {
  label: string;
  field: SortField;
  sortField: SortField | null;
  sortDir: SortDir;
  onSort: (f: SortField) => void;
  widthCls?: string;
}) {
  const active = sortField === field;
  return (
    <th
      className={`px-3 py-2 border-b border-slate-200 dark:border-slate-800 cursor-pointer select-none hover:bg-slate-100/60 dark:hover:bg-slate-800/40 transition-colors ${widthCls ?? ''}`}
      onClick={() => onSort(field)}
    >
      <div className="flex items-center gap-1">
        <span>{label}</span>
        {active ? (
          sortDir === 'asc'
            ? <ArrowUpIcon className="w-3 h-3 text-sky-500" />
            : <ArrowDownIcon className="w-3 h-3 text-sky-500" />
        ) : (
          <ArrowUpDownIcon className="w-3 h-3 text-slate-300 dark:text-slate-600" />
        )}
      </div>
    </th>
  );
}

/* =========================================================
   行内快捷操作（功能 B）
   ========================================================= */
function RowActions({ ev, onEdit, onComplete, onDelete, onDuplicate }: {
  ev: CalendarEventDto;
  onEdit: () => void;
  onComplete: () => void;
  onDelete: () => void;
  onDuplicate: () => void;
}) {
  const { t } = useI18n();
  return (
    <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
      <button
        type="button"
        title={t('common.edit')}
        onClick={(e) => { e.stopPropagation(); onEdit(); }}
        className="p-1 rounded hover:bg-sky-100 dark:hover:bg-sky-900/40 text-slate-500 hover:text-sky-600 dark:hover:text-sky-400 transition-colors"
      >
        <PencilIcon className="w-3.5 h-3.5" />
      </button>
      {ev.status !== 'completed' && (
        <button
          type="button"
          title={t('calendar.views.list.markComplete')}
          onClick={(e) => { e.stopPropagation(); onComplete(); }}
          className="p-1 rounded hover:bg-emerald-100 dark:hover:bg-emerald-900/40 text-slate-500 hover:text-emerald-600 dark:hover:text-emerald-400 transition-colors"
        >
          <CheckIcon className="w-3.5 h-3.5" />
        </button>
      )}
      <button
        type="button"
        title={t('calendar.views.list.actionDuplicate')}
        onClick={(e) => { e.stopPropagation(); onDuplicate(); }}
        className="p-1 rounded hover:bg-violet-100 dark:hover:bg-violet-900/40 text-slate-500 hover:text-violet-600 dark:hover:text-violet-400 transition-colors"
      >
        <CopyIcon className="w-3.5 h-3.5" />
      </button>
      <button
        type="button"
        title={t('common.delete')}
        onClick={(e) => { e.stopPropagation(); onDelete(); }}
        className="p-1 rounded hover:bg-rose-100 dark:hover:bg-rose-900/40 text-slate-500 hover:text-rose-600 dark:hover:text-rose-400 transition-colors"
      >
        <TrashIcon className="w-3.5 h-3.5" />
      </button>
    </div>
  );
}

/* =========================================================
   ListRow 组件
   高9 修复：使用 React.memo + 自定义比较函数包装，避免父组件渲染时
   因 ev 引用变化或 visibleCols (Set) 引用变化导致的整张表行重渲染。
   比较函数对 Set 与 ev 做内容级别深比较，对回调做引用比较。
   ========================================================= */
interface ListRowProps {
  ev: CalendarEventDto;
  visibleCols: Set<ColKey>;
  sortField: SortField | null;
  onOpen: (ev: CalendarEventDto) => void;
  canManage: boolean;
  multiSelectMode: boolean;
  selected: boolean;
  onToggleSelect: (id: number) => void;
  onQuickComplete: (ev: CalendarEventDto) => void;
  onQuickDelete: (ev: CalendarEventDto) => void;
  onQuickDuplicate: (ev: CalendarEventDto) => void;
}

// 浅比较两个 Set 内容是否相等
function setsEqual<T>(a: Set<T>, b: Set<T>): boolean {
  if (a === b) return true;
  if (a.size !== b.size) return false;
  for (const v of a) if (!b.has(v)) return false;
  return true;
}

// ev 浅内容比较：比较渲染相关的关键字段
function evShallowEqual(a: CalendarEventDto, b: CalendarEventDto): boolean {
  if (a === b) return true;
  return (
    a.id === b.id &&
    a.title === b.title &&
    a.description === b.description &&
    a.eventType === b.eventType &&
    a.status === b.status &&
    a.startAt === b.startAt &&
    a.endAt === b.endAt &&
    a.isAllDay === b.isAllDay &&
    a.color === b.color &&
    a.priority === b.priority &&
    a.progress === b.progress &&
    a.location === b.location &&
    a.isRecurring === b.isRecurring &&
    a.reminderMinutes === b.reminderMinutes &&
    a.taskOwnerName === b.taskOwnerName &&
    a.userName === b.userName &&
    a.rowVersion === b.rowVersion &&
    a.recurrenceOriginalStart === b.recurrenceOriginalStart &&
    a.deletedAt === b.deletedAt
  );
}

function ListRow({
  ev, visibleCols, onOpen, canManage, multiSelectMode, selected, onToggleSelect,
  onQuickComplete, onQuickDelete, onQuickDuplicate,
}: ListRowProps) {
  const { t } = useI18n();
  const ts = getEventTypeStyle(ev.eventType);
  const ss = getStatusStyle(ev.status);
  const isPendingSync = useCalendarStore(
    (s) => typeof ev.id === 'number' && s.pendingPatchIds.has(ev.id)
  );
  const handleClick = useCallback(() => {
    if (multiSelectMode) onToggleSelect(ev.id);
    else if (canManage) onOpen(ev);
  }, [multiSelectMode, onToggleSelect, canManage, onOpen, ev]);

  const recLabel = useMemo(() => getRecurrenceLabel(ev), [ev]);
  const remLabel = useMemo(() => reminderLabel(ev.reminderMinutes, t), [ev.reminderMinutes, t]);

  return (
    <tr
      onClick={handleClick}
      className={[
        'group',
        multiSelectMode || canManage ? 'cursor-pointer' : '',
        !multiSelectMode ? 'hover:bg-sky-50/40 dark:hover:bg-sky-950/20 transition-colors' : '',
        selected ? 'ring-2 ring-sky-400 ring-inset bg-sky-50 dark:bg-sky-950/30' : '',
      ].filter(Boolean).join(' ')}
    >
      {multiSelectMode && (
        <td className="px-3 py-2 md:py-3 max-md:py-2 align-top w-10">
          <div className="flex items-center justify-center">
            <button
              type="button"
              onClick={(e) => { e.stopPropagation(); onToggleSelect(ev.id); }}
              className="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors text-slate-600 dark:text-slate-300"
              aria-label={selected ? t('calendar.common.cancelSelection') : t('calendar.views.list.selectItem')}
            >
              {selected ? (
                <CheckSquareIcon className="w-4 h-4 text-sky-500" />
              ) : (
                <SquareIcon className="w-4 h-4" />
              )}
            </button>
          </div>
        </td>
      )}

      {/* 标题列 — 始终显示 */}
      {visibleCols.has('title') && (
        <td className="px-3 py-2 md:py-3 max-md:py-2 align-top">
          <div className="flex items-center gap-2">
            <span className={`shrink-0 w-2 h-2 rounded-full ring-2 ring-white dark:ring-slate-900 shadow-sm ${ts.dotClass}`} />
            <span className="font-medium truncate">{ev.title || t('calendar.common.untitled')}</span>
            {isPendingSync && (
              <span
                className="cal-sync-pending-dot shrink-0 w-2 h-2 rounded-full bg-slate-400 ring-2 ring-white/70 dark:ring-slate-900/70"
                title={t('calendar.common.pendingSyncHint')}
                aria-label={t('calendar.common.pendingSync')}
              />
            )}
          </div>
          {ev.description && <div className="mt-0.5 text-xs text-slate-500 dark:text-slate-400 line-clamp-1">{ev.description}</div>}
          {/* 行内快捷操作 — hover 时显示在标题行右侧 */}
          {canManage && !multiSelectMode && (
            <div className="mt-1">
              <RowActions
                ev={ev}
                onEdit={() => onOpen(ev)}
                onComplete={() => onQuickComplete(ev)}
                onDelete={() => onQuickDelete(ev)}
                onDuplicate={() => onQuickDuplicate(ev)}
              />
            </div>
          )}
        </td>
      )}

      {/* 类型 */}
      {visibleCols.has('eventType') && (
        <td className="px-3 py-2 md:py-3 max-md:py-2 whitespace-nowrap text-xs">
          <span
            className={`inline-flex items-center px-2 py-0.5 rounded shadow-sm ${ev.color ? '' : ts.badgeClass}`}
            style={_colorOverrideStyle(ev)}
          >
            {ts.label}
          </span>
        </td>
      )}

      {/* 状态 */}
      {visibleCols.has('status') && (
        <td className="px-3 py-2 md:py-3 max-md:py-2 whitespace-nowrap text-xs">
          <span className={`inline-flex items-center px-2 py-0.5 rounded shadow-sm ${ss.badgeClass}`}>
            {ss.label}
          </span>
        </td>
      )}

      {/* 时间 */}
      {visibleCols.has('time') && (
        <td className="px-3 py-2 md:py-3 max-md:py-2 whitespace-nowrap text-xs text-slate-500 dark:text-slate-400">
          {formatDateTimeShort(new Date(ev.startAt), !!ev.isAllDay)}
          {ev.endAt ? ` - ${formatDateTimeShort(new Date(ev.endAt), !!ev.isAllDay)}` : ''}
        </td>
      )}

      {/* 负责人 */}
      {visibleCols.has('assignee') && (
        <td className="px-3 py-2 md:py-3 max-md:py-2 whitespace-nowrap text-xs text-slate-500 dark:text-slate-400">
          {ev.taskOwnerName || ev.userName || '-'}
        </td>
      )}

      {/* 优先级 */}
      {visibleCols.has('priority') && (
        <td className="px-3 py-2 md:py-3 max-md:py-2 whitespace-nowrap text-xs">
          {typeof ev.priority === 'number' && ev.priority > 0 ? (
            <span className="inline-flex items-center px-1.5 py-0.5 rounded font-medium bg-orange-50 dark:bg-orange-900/30 text-orange-700 dark:text-orange-300 ring-1 ring-orange-200/60 dark:ring-orange-700/40">
              P{ev.priority}
            </span>
          ) : (
            <span className="text-slate-300 dark:text-slate-600">-</span>
          )}
        </td>
      )}

      {/* 进度 */}
      {visibleCols.has('progress') && (
        <td className="px-3 py-2 md:py-3 max-md:py-2 whitespace-nowrap text-xs">
          {typeof ev.progress === 'number' && ev.progress > 0 ? (
            <div className="flex items-center gap-1.5">
              <div className="w-12 h-1.5 rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden">
                <div
                  className="h-full rounded-full bg-gradient-to-r from-sky-400 to-sky-500"
                  style={{ width: `${Math.min(100, Math.max(0, ev.progress))}%` }}
                />
              </div>
              <span className="text-slate-500 dark:text-slate-400 tabular-nums">{ev.progress}%</span>
            </div>
          ) : (
            <span className="text-slate-300 dark:text-slate-600">-</span>
          )}
        </td>
      )}

      {/* 地点 */}
      {visibleCols.has('location') && (
        <td className="px-3 py-2 md:py-3 max-md:py-2 whitespace-nowrap text-xs text-slate-500 dark:text-slate-400">
          {ev.location ? (
            <div className="flex items-center gap-1">
              <MapPinIcon className="w-3 h-3 shrink-0 text-slate-400" />
              <span className="truncate max-w-[120px]">{ev.location}</span>
            </div>
          ) : (
            <span className="text-slate-300 dark:text-slate-600">-</span>
          )}
        </td>
      )}

      {/* 重复 */}
      {visibleCols.has('recurrence') && (
        <td className="px-3 py-2 md:py-3 max-md:py-2 whitespace-nowrap text-xs text-slate-500 dark:text-slate-400">
          {ev.isRecurring ? (
            <div className="flex items-center gap-1" title={recLabel}>
              <RepeatIcon className="w-3 h-3 shrink-0 text-violet-500" />
              <span className="truncate max-w-[80px]">{recLabel || t('calendar.views.list.recurrence')}</span>
            </div>
          ) : (
            <span className="text-slate-300 dark:text-slate-600">-</span>
          )}
        </td>
      )}

      {/* 提醒 */}
      {visibleCols.has('reminder') && (
        <td className="px-3 py-2 md:py-3 max-md:py-2 whitespace-nowrap text-xs text-slate-500 dark:text-slate-400">
          {ev.reminderMinutes != null && ev.reminderMinutes > 0 ? (
            <div className="flex items-center gap-1" title={remLabel}>
              <BellIcon className="w-3 h-3 shrink-0 text-amber-500" />
              <span>{remLabel}</span>
            </div>
          ) : (
            <span className="text-slate-300 dark:text-slate-600">-</span>
          )}
        </td>
      )}
    </tr>
  );
}

// 高9: 用 memo 包装 ListRow，自定义比较函数对 ev/visibleCols 做内容比较，
// 其余 prop 走引用比较（父组件均已 useCallback 稳定引用）。
const MemoizedListRow = memo(ListRow, (prev, next) => {
  return (
    prev.canManage === next.canManage &&
    prev.multiSelectMode === next.multiSelectMode &&
    prev.selected === next.selected &&
    prev.sortField === next.sortField &&
    prev.onOpen === next.onOpen &&
    prev.onToggleSelect === next.onToggleSelect &&
    prev.onQuickComplete === next.onQuickComplete &&
    prev.onQuickDelete === next.onQuickDelete &&
    prev.onQuickDuplicate === next.onQuickDuplicate &&
    setsEqual(prev.visibleCols, next.visibleCols) &&
    evShallowEqual(prev.ev, next.ev)
  );
});

/* =========================================================
   ListMobileCard —— 移动端卡片视图（行数据以卡片形式展示）
   ========================================================= */
const ListMobileCard = memo(function ListMobileCard({
  ev, visibleCols, onOpen, canManage, multiSelectMode, selected, onToggleSelect,
  onQuickComplete, onQuickDelete, onQuickDuplicate,
}: ListRowProps) {
  const { t } = useI18n();
  const ts = getEventTypeStyle(ev.eventType);
  const ss = getStatusStyle(ev.status);
  const isPendingSync = useCalendarStore(
    (s) => typeof ev.id === 'number' && s.pendingPatchIds.has(ev.id)
  );
  const handleClick = useCallback(() => {
    if (multiSelectMode) onToggleSelect(ev.id);
    else if (canManage) onOpen(ev);
  }, [multiSelectMode, onToggleSelect, canManage, onOpen, ev]);

  const recLabel = useMemo(() => getRecurrenceLabel(ev), [ev]);
  const remLabel = useMemo(() => reminderLabel(ev.reminderMinutes, t), [ev.reminderMinutes, t]);

  return (
    <div
      onClick={handleClick}
      className={[
        'p-3.5 transition-colors',
        multiSelectMode || canManage ? 'cursor-pointer' : '',
        selected ? 'ring-2 ring-sky-400 ring-inset bg-sky-50 dark:bg-sky-950/30' : '',
      ].filter(Boolean).join(' ')}
    >
      {/* 标题行 */}
      <div className="flex items-center gap-2 mb-2">
        {multiSelectMode && (
          <button
            type="button"
            onClick={(e) => { e.stopPropagation(); onToggleSelect(ev.id); }}
            className="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors text-slate-600 dark:text-slate-300 flex-shrink-0"
            aria-label={selected ? t('calendar.common.cancelSelection') : t('calendar.views.list.selectItem')}
          >
            {selected ? (
              <CheckSquareIcon className="w-4 h-4 text-sky-500" />
            ) : (
              <SquareIcon className="w-4 h-4" />
            )}
          </button>
        )}
        <span className={`shrink-0 w-2 h-2 rounded-full ring-2 ring-white dark:ring-slate-900 shadow-sm ${ts.dotClass}`} />
        <span className="font-medium text-sm truncate flex-1">{ev.title || t('calendar.common.untitled')}</span>
        {isPendingSync && (
          <span
            className="cal-sync-pending-dot shrink-0 w-2 h-2 rounded-full bg-slate-400 ring-2 ring-white/70 dark:ring-slate-900/70"
            title={t('calendar.common.pendingSyncHint')}
            aria-label={t('calendar.common.pendingSync')}
          />
        )}
      </div>

      {/* 时间 */}
      {visibleCols.has('time') && (
        <div className="text-xs text-slate-500 dark:text-slate-400 mb-1.5">
          {formatDateTimeShort(new Date(ev.startAt), !!ev.isAllDay)}
          {ev.endAt ? ` - ${formatDateTimeShort(new Date(ev.endAt), !!ev.isAllDay)}` : ''}
        </div>
      )}

      {/* 徽章组 */}
      <div className="flex items-center gap-1.5 mb-2 flex-wrap">
        {visibleCols.has('eventType') && (
          <span
            className={`inline-flex items-center px-2 py-0.5 rounded shadow-sm text-xs ${ev.color ? '' : ts.badgeClass}`}
            style={_colorOverrideStyle(ev)}
          >
            {ts.label}
          </span>
        )}
        {visibleCols.has('status') && (
          <span className={`inline-flex items-center px-2 py-0.5 rounded shadow-sm text-xs ${ss.badgeClass}`}>
            {ss.label}
          </span>
        )}
        {visibleCols.has('priority') && typeof ev.priority === 'number' && ev.priority > 0 && (
          <span className="inline-flex items-center px-1.5 py-0.5 rounded font-medium text-xs bg-orange-50 dark:bg-orange-900/30 text-orange-700 dark:text-orange-300 ring-1 ring-orange-200/60 dark:ring-orange-700/40">
            P{ev.priority}
          </span>
        )}
        {visibleCols.has('location') && ev.location && (
          <span className="inline-flex items-center gap-1 text-xs text-slate-500 dark:text-slate-400">
            <MapPinIcon className="w-3 h-3" />
            <span className="truncate max-w-[100px]">{ev.location}</span>
          </span>
        )}
      </div>

      {/* 其他信息 */}
      {(visibleCols.has('assignee') || visibleCols.has('recurrence') || visibleCols.has('reminder') || visibleCols.has('progress')) && (
        <div className="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400 mb-2 flex-wrap">
          {visibleCols.has('assignee') && (ev.taskOwnerName || ev.userName) && (
            <span>{ev.taskOwnerName || ev.userName}</span>
          )}
          {visibleCols.has('recurrence') && ev.isRecurring && (
            <span className="inline-flex items-center gap-1">
              <RepeatIcon className="w-3 h-3 text-violet-500" />
              {recLabel || t('calendar.views.list.recurrence')}
            </span>
          )}
          {visibleCols.has('reminder') && ev.reminderMinutes != null && ev.reminderMinutes > 0 && (
            <span className="inline-flex items-center gap-1">
              <BellIcon className="w-3 h-3 text-amber-500" />
              {remLabel}
            </span>
          )}
          {visibleCols.has('progress') && typeof ev.progress === 'number' && ev.progress > 0 && (
            <span className="inline-flex items-center gap-1">
              <span className="inline-block w-8 h-1.5 rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden">
                <span className="block h-full rounded-full bg-gradient-to-r from-sky-400 to-sky-500" style={{ width: `${Math.min(100, Math.max(0, ev.progress))}%` }} />
              </span>
              {ev.progress}%
            </span>
          )}
        </div>
      )}

      {/* 操作按钮 */}
      {canManage && !multiSelectMode && (
        <div className="flex items-center gap-1 mt-2">
          <button
            type="button"
            title={t('common.edit')}
            onClick={(e) => { e.stopPropagation(); onOpen(ev); }}
            className="flex-1 inline-flex items-center justify-center gap-1 py-1.5 text-xs text-slate-500 hover:text-sky-600 dark:hover:text-sky-400 hover:bg-sky-100 dark:hover:bg-sky-900/40 rounded transition-colors"
          >
            <PencilIcon className="w-3.5 h-3.5" />{t('common.edit')}
          </button>
          {ev.status !== 'completed' && (
            <button
              type="button"
              title={t('calendar.views.list.markComplete')}
              onClick={(e) => { e.stopPropagation(); onQuickComplete(ev); }}
              className="flex-1 inline-flex items-center justify-center gap-1 py-1.5 text-xs text-slate-500 hover:text-emerald-600 dark:hover:text-emerald-400 hover:bg-emerald-100 dark:hover:bg-emerald-900/40 rounded transition-colors"
            >
              <CheckIcon className="w-3.5 h-3.5" />{t('calendar.common.complete')}
            </button>
          )}
          <button
            type="button"
            title={t('calendar.views.list.actionDuplicate')}
            onClick={(e) => { e.stopPropagation(); onQuickDuplicate(ev); }}
            className="flex-1 inline-flex items-center justify-center gap-1 py-1.5 text-xs text-slate-500 hover:text-violet-600 dark:hover:text-violet-400 hover:bg-violet-100 dark:hover:bg-violet-900/40 rounded transition-colors"
          >
            <CopyIcon className="w-3.5 h-3.5" />{t('calendar.views.list.actionDuplicate')}
          </button>
          <button
            type="button"
            title={t('common.delete')}
            onClick={(e) => { e.stopPropagation(); onQuickDelete(ev); }}
            className="flex-1 inline-flex items-center justify-center gap-1 py-1.5 text-xs text-slate-500 hover:text-rose-600 dark:hover:text-rose-400 hover:bg-rose-100 dark:hover:bg-rose-900/40 rounded transition-colors"
          >
            <TrashIcon className="w-3.5 h-3.5" />{t('common.delete')}
          </button>
        </div>
      )}
    </div>
  );
}, (prev, next) => {
  return (
    prev.canManage === next.canManage &&
    prev.multiSelectMode === next.multiSelectMode &&
    prev.selected === next.selected &&
    prev.onOpen === next.onOpen &&
    prev.onToggleSelect === next.onToggleSelect &&
    prev.onQuickComplete === next.onQuickComplete &&
    prev.onQuickDelete === next.onQuickDelete &&
    prev.onQuickDuplicate === next.onQuickDuplicate &&
    setsEqual(prev.visibleCols, next.visibleCols) &&
    evShallowEqual(prev.ev, next.ev)
  );
});

/* =========================================================
   分页
   ========================================================= */
function Pagination({ totalItems, currentPage, totalPages, pageSize, gotoPage }: {
  totalItems: number; currentPage: number; totalPages: number; pageSize: number;
  gotoPage: (page: number) => void;
}) {
  const { t } = useI18n();
  const start = totalItems === 0 ? 0 : (currentPage - 1) * pageSize + 1;
  const end = Math.min(totalItems, currentPage * pageSize);
  return (
    <div className="flex items-center justify-between gap-4 px-4 md:py-3 max-md:py-2 border-t border-slate-200 dark:border-slate-800">
      <div className="text-xs text-slate-500 dark:text-slate-400">
        {t('calendar.views.list.paginationRange', { start, end, total: totalItems, count: totalItems })}
      </div>
      <div className="flex items-center gap-1">
        <button
          type="button"
          disabled={currentPage <= 1}
          onClick={() => gotoPage(currentPage - 1)}
          className="px-2 py-1 rounded text-xs border border-slate-300 dark:border-slate-700 disabled:opacity-40 disabled:cursor-not-allowed hover:bg-slate-50 dark:hover:bg-slate-900 transition-colors focus:outline-none focus:ring-2 focus:ring-sky-500/30"
        >
          {t('calendar.common.previousPage')}
        </button>
        <div className="px-2.5 py-1 rounded text-xs font-medium bg-gradient-to-b from-sky-500 to-sky-600 text-white shadow-sm">
          {t('calendar.views.list.pageIndicator', { page: currentPage, total: totalPages })}
        </div>
        <button
          type="button"
          disabled={currentPage >= totalPages}
          onClick={() => gotoPage(currentPage + 1)}
          className="px-2 py-1 rounded text-xs border border-slate-300 dark:border-slate-700 disabled:opacity-40 disabled:cursor-not-allowed hover:bg-slate-50 dark:hover:bg-slate-900 transition-colors focus:outline-none focus:ring-2 focus:ring-sky-500/30"
        >
          {t('calendar.common.nextPage')}
        </button>
      </div>
    </div>
  );
}

/* =========================================================
   主组件
   ========================================================= */
export default function ListView() {
  const { t } = useI18n();
  const { canManageCalendar } = usePermission();
  const list = useCalendarStore((s) => s.list);
  const query = useCalendarStore((s) => s.query);
  const setQueryField = useCalendarStore((s) => s.setQueryField);
  const openEdit = useCalendarStore((s) => s.openEdit);
  const openCreate = useCalendarStore((s) => s.openCreate);
  const gotoToday = useCalendarStore((s) => s.gotoToday);
  const signalRRev = useCalendarStore((s) => s.signalRRev);
  const showTransientToast = useCalendarStore((s) => s.showTransientToast);

  // 多选
  const selectedEventIds = useCalendarStore((s) => s.selectedEventIds);
  const selectEvent = useCalendarStore((s) => s.selectEvent);
  const deselectEvent = useCalendarStore((s) => s.deselectEvent);
  const isSelectedFn = useCalendarStore((s) => s.isSelected);
  const clearSelection = useCalendarStore((s) => s.clearSelection);

  const [multiSelectMode, setMultiSelectMode] = useState(false);

  // 根据当前 query.startDate/endDate 初始化快捷日期选中态，避免刷新浏览器后按钮显示"自定义"但实际是今日/本周范围的视觉不一致
  const initialFilterKey = useMemo<ListFilterKey>(() => {
    const s = query.startDate;
    const e = query.endDate;
    if (!s || !e) return 'custom';
    const now = new Date();
    const ymd = toYMDKey(now);
    if (s === ymd && e === ymd) return 'today';
    const wStart = new Date(now); wStart.setDate(now.getDate() - now.getDay());
    const wEnd = new Date(wStart); wEnd.setDate(wStart.getDate() + 6);
    if (s === toYMDKey(wStart) && e === toYMDKey(wEnd)) return 'week';
    const mStart = new Date(now.getFullYear(), now.getMonth(), 1);
    const mEnd = new Date(now.getFullYear(), now.getMonth() + 1, 0);
    if (s === toYMDKey(mStart) && e === toYMDKey(mEnd)) return 'month';
    return 'custom';
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const [filterKey, setFilterKey] = useState<ListFilterKey>(initialFilterKey);

  // 列设置（功能 A）
  const [visibleCols, setVisibleCols] = useState<Set<ColKey>>(() => loadVisibleCols());
  const [showColSettings, setShowColSettings] = useState(false);

  // 排序（功能 C）
  const [sortField, setSortField] = useState<SortField | null>(null);
  const [sortDir, setSortDir] = useState<SortDir>('asc');

  useEffect(() => {
    saveVisibleCols(visibleCols);
  }, [visibleCols]);

  // 当用户直接改起止日期（custom 模式）时，自动切到 custom 高亮，避免 preset 仍高亮"今日"但日期范围已被改成自定义
  useEffect(() => {
    const s = query.startDate;
    const e = query.endDate;
    if (!s || !e) { setFilterKey('custom'); return; }
    const now = new Date();
    const ymd = toYMDKey(now);
    if (s === ymd && e === ymd) { setFilterKey('today'); return; }
    const wStart = new Date(now); wStart.setDate(now.getDate() - now.getDay());
    const wEnd = new Date(wStart); wEnd.setDate(wStart.getDate() + 6);
    if (s === toYMDKey(wStart) && e === toYMDKey(wEnd)) { setFilterKey('week'); return; }
    const mStart = new Date(now.getFullYear(), now.getMonth(), 1);
    const mEnd = new Date(now.getFullYear(), now.getMonth() + 1, 0);
    if (s === toYMDKey(mStart) && e === toYMDKey(mEnd)) { setFilterKey('month'); return; }
    setFilterKey('custom');
  }, [query.startDate, query.endDate]);

  const rows = useMemo(() => list?.data ?? [], [list]);

  const handleSort = useCallback((field: SortField) => {
    if (sortField === field) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortField(field);
      setSortDir('asc');
    }
  }, [sortField]);

  // 排序变化时同步到 store.query，触发后端重新查询（后端排序支持分页正确性）
  useEffect(() => {
    if (!sortField) return;
    useCalendarStore.setState((s) => ({
      query: { ...s.query, sortBy: sortField, sortDir, page: 1 },
    }));
  }, [sortField, sortDir]);

  const visibleIds = useMemo(() => rows.map((e) => e.id), [rows]);
  const allVisibleSelected = useMemo(
    () => visibleIds.length > 0 && visibleIds.every((id) => isSelectedFn(id)),
    [visibleIds, isSelectedFn],
  );
  const someVisibleSelected = useMemo(
    () => visibleIds.some((id) => isSelectedFn(id)),
    [visibleIds, isSelectedFn],
  );

  const toggleSelect = useCallback((id: number) => {
    if (isSelectedFn(id)) deselectEvent(id);
    else selectEvent(id);
  }, [isSelectedFn, selectEvent, deselectEvent]);

  const toggleSelectAllVisible = useCallback(() => {
    if (allVisibleSelected) {
      const next = new Set(selectedEventIds);
      visibleIds.forEach((id) => next.delete(id));
      useCalendarStore.setState({ selectedEventIds: next });
    } else {
      const next = new Set(selectedEventIds);
      visibleIds.forEach((id) => next.add(id));
      useCalendarStore.setState({ selectedEventIds: next });
    }
  }, [allVisibleSelected, selectedEventIds, visibleIds]);

  const handleEnterMultiSelect = useCallback(() => {
    setMultiSelectMode(true);
  }, []);

  const handleExitMultiSelect = useCallback(() => {
    clearSelection();
    setMultiSelectMode(false);
  }, [clearSelection]);

  // 行内快捷操作（功能 B）
  // 乐观更新：完成
  // 先把本地 status 改为 completed，调 API 失败则回滚 list + allEvents + eventsByDate + overview
  const handleQuickComplete = useCallback(async (ev: CalendarEventDto) => {
    const store = useCalendarStore.getState();
    const prevList = store.list;
    const prevAllEvents = store.allEvents;
    const prevEventsByDate = store.eventsByDate;
    const prevOverview = store.overview;
    const updatedEv = { ...ev, status: 'completed' };

    // 乐观更新 list（列表视图数据源）
    useCalendarStore.setState((s) => ({
      list: s.list ? { ...s.list, data: s.list.data.map((x) => (x.id === ev.id ? updatedEv : x)) } : s.list,
    }));
    // 同步更新 allEvents + eventsByDate + overview（all/三视图同步）
    store.optimisticUpsertEvent(updatedEv);

    try {
      // P3: 乐观锁 If-Match 校验，避免并发修改时已完成状态被覆盖回原值
      const latest = await calendarApi.quickUpdate(ev.id, { status: 'completed', ifMatch: ev.rowVersion ?? undefined });
      // API 成功：用后端实际返回的最新快照再覆盖一次本地，保证 rowVersion 与数据库一致
      // 关键：后端返回的父事件 DTO 不带 recurrenceOriginalStart（null），直接 upsert 会因复合 key
      // 不匹配（id vs id#originalStart）导致追加新事件而非替换，造成两条色块。
      const afterSnapshot = latest
        ? { ...latest, recurrenceOriginalStart: ev.recurrenceOriginalStart ?? latest.recurrenceOriginalStart ?? null }
        : updatedEv;
      if (latest) store.optimisticUpsertEvent(afterSnapshot);
      // P1-3: 推入 undo 栈，支持撤销
      store.pushUndo({
        type: 'edit',
        description: t('calendar.views.list.undoComplete', { title: ev.title || t('calendar.common.untitled') }),
        beforeSnapshot: ev,
        afterSnapshot,
        eventId: ev.id,
      });
      // 状态更新成功后立即 bump signalRRev，触发事件列表刷新，确保后端数据同步。
      // 不依赖 SignalR 推送（可能在乐观保护期内被跳过），本地 bump 作为双保险。
      useCalendarStore.setState((s) => ({ signalRRev: s.signalRRev + 1 }));
      showTransientToast(t('calendar.views.list.toastCompleted'), 'success', 2000);
    } catch (e: unknown) {
      useCalendarStore.setState({ list: prevList, allEvents: prevAllEvents, eventsByDate: prevEventsByDate, overview: prevOverview });
      const errData = getErrorData<ApiErrorResponseBody>(e);
      const conflictData = errData?.data as ConflictResponseData | undefined;
      if (getErrorStatus(e) === 409 && (conflictData?.errorType === 'calendar_concurrency_conflict' || conflictData?.latest)) {
        const latest = conflictData?.latest;
        if (latest) useCalendarStore.getState().optimisticUpsertEvent(latest);
        showTransientToast(
          errData?.message || t('calendar.common.staleDataRefreshed'),
          'warning',
          4500,
        );
      } else {
        showTransientToast(t('calendar.views.list.toastCompleteFailed'), 'error', 3000);
      }
    }
  }, [showTransientToast, t]);

  // 乐观更新：删除
  // 先从本地 list + allEvents + overview 移除，调 API 失败则回滚
  const handleQuickDelete = useCallback(async (ev: CalendarEventDto) => {
    if (!window.confirm(t('calendar.views.list.confirmDelete', { title: ev.title || t('calendar.common.untitled') }))) return;

    const store = useCalendarStore.getState();
    const prevList = store.list;
    const prevAllEvents = store.allEvents;
    const prevEventsByDate = store.eventsByDate;
    const prevOverview = store.overview;

    // 乐观移除 list
    useCalendarStore.setState((s) => ({
      list: s.list
        ? { ...s.list, data: s.list.data.filter((x) => x.id !== ev.id), totalItems: Math.max(0, s.list.totalItems - 1) }
        : s.list,
    }));
    // 同步移除 allEvents + 重建 eventsByDate + overview（optimisticUpsertEvent 检测到 deletedAt 非空会走移除分支）
    store.optimisticUpsertEvent({ ...ev, deletedAt: new Date().toISOString() });

    try {
      await calendarApi.remove(ev.id, { rowVersion: ev.rowVersion ?? null });
      // P2-5: 删除成功后清理 pending patch，避免 localStorage 中残留过期补丁
      store.dropPendingPatch(ev.id);
      // P1-3: 推入 undo 栈，支持撤销（undo 会重新 create 该事件）
      store.pushUndo({
        type: 'delete',
        description: t('calendar.views.list.undoDelete', { title: ev.title || t('calendar.common.untitled') }),
        beforeSnapshot: ev,
        afterSnapshot: null,
        eventId: ev.id,
      });
      // 删除成功后立即 bump signalRRev，触发事件列表刷新，确保已删除事件从 allEvents 彻底清除。
      // 不依赖 SignalR 推送（可能在乐观保护期内被跳过），本地 bump 作为双保险。
      useCalendarStore.setState((s) => ({ signalRRev: s.signalRRev + 1 }));
      showTransientToast(t('calendar.views.list.toastDeleted'), 'success', 2000);
    } catch (e: unknown) {
      useCalendarStore.setState({ list: prevList, allEvents: prevAllEvents, eventsByDate: prevEventsByDate, overview: prevOverview });
      const errData = getErrorData<ApiErrorResponseBody>(e);
      const conflictData = errData?.data as ConflictResponseData | undefined;
      if (getErrorStatus(e) === 409 && (conflictData?.errorType === 'calendar_concurrency_conflict' || conflictData?.latest)) {
        const latest = conflictData?.latest;
        if (latest) useCalendarStore.getState().optimisticUpsertEvent(latest);
        showTransientToast(
          errData?.message || t('calendar.common.staleDataRefreshed'),
          'warning',
          4500,
        );
      } else {
        showTransientToast(t('calendar.views.list.toastDeleteFailed'), 'error', 3000);
      }
    }
  }, [showTransientToast, t]);

  // 乐观更新：复制
  // API 成功返回新事件 DTO 后，插入到 list 头部 + allEvents，失败则提示（无需回滚，API 失败时本地无变更）
  const handleQuickDuplicate = useCallback(async (ev: CalendarEventDto) => {
    try {
      const created = await calendarApi.duplicate(ev.id);
      // 乐观插入 list（插入到列表头部，立刻可见）
      useCalendarStore.setState((s) => ({
        list: s.list
          ? { ...s.list, data: [created, ...s.list.data], totalItems: s.list.totalItems + 1 }
          : s.list,
      }));
      // 同步插入 allEvents + eventsByDate
      useCalendarStore.getState().optimisticUpsertEvent(created);
      showTransientToast(t('calendar.views.list.toastDuplicated'), 'success', 2000);
    } catch {
      showTransientToast(t('calendar.views.list.toastDuplicateFailed'), 'error', 3000);
    }
  }, [showTransientToast, t]);

  const isEmpty = !list || list.data.length === 0;

  // 当多选模式切换或 signalRRev 变化时退出多选模式
  void signalRRev;

  const applyPreset = (p: ListFilterKey) => {
    const now = new Date();
    if (p === 'today') {
      const ymd = toYMDKey(now);
      setQueryField('startDate', ymd, true);
      setQueryField('endDate', ymd, false);
      gotoToday();
    } else if (p === 'week') {
      const s = new Date(now);
      s.setDate(now.getDate() - now.getDay());
      const e = new Date(s);
      e.setDate(s.getDate() + 6);
      setQueryField('startDate', toYMDKey(s), true);
      setQueryField('endDate', toYMDKey(e), false);
    } else if (p === 'month') {
      const s = new Date(now.getFullYear(), now.getMonth(), 1);
      const e = new Date(now.getFullYear(), now.getMonth() + 1, 0);
      setQueryField('startDate', toYMDKey(s), true);
      setQueryField('endDate', toYMDKey(e), false);
    }
  };

  // 渲染表头
  const renderHeader = () => {
    return (
      <tr className="text-left text-xs font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wide">
        {multiSelectMode && (
          <th className="px-3 py-2 border-b border-slate-200 dark:border-slate-800 w-10">
            <div className="flex items-center justify-center">
              <button
                type="button"
                onClick={toggleSelectAllVisible}
                className="p-1 rounded hover:bg-slate-200/60 dark:hover:bg-slate-800 transition-colors text-slate-600 dark:text-slate-300"
                aria-label={allVisibleSelected ? t('calendar.views.list.deselectPage') : t('calendar.views.list.selectPage')}
                title={allVisibleSelected ? t('calendar.views.list.deselectPage') : t('calendar.views.list.selectPage') + (someVisibleSelected ? t('calendar.views.list.partiallySelected') : '')}
              >
                {allVisibleSelected ? (
                  <CheckSquareIcon className="w-4 h-4 text-sky-500" />
                ) : someVisibleSelected ? (
                  <CheckSquareIcon className="w-4 h-4 text-sky-400/60" />
                ) : (
                  <SquareIcon className="w-4 h-4" />
                )}
              </button>
            </div>
          </th>
        )}

        {visibleCols.has('title') && (
          <th className="px-3 py-2 border-b border-slate-200 dark:border-slate-800">{t('calendar.common.title')}</th>
        )}
        {visibleCols.has('eventType') && (
          <th className="px-3 py-2 border-b border-slate-200 dark:border-slate-800 w-24">{t('calendar.common.type')}</th>
        )}
        {visibleCols.has('status') && (
          <th className="px-3 py-2 border-b border-slate-200 dark:border-slate-800 w-24">{t('calendar.common.status')}</th>
        )}
        {visibleCols.has('time') && (
          <SortableTh label={t('calendar.views.list.colTime')} field="time" sortField={sortField} sortDir={sortDir} onSort={handleSort} widthCls="w-64" />
        )}
        {visibleCols.has('assignee') && (
          <th className="px-3 py-2 border-b border-slate-200 dark:border-slate-800 w-28">{t('calendar.common.assignee')}</th>
        )}
        {visibleCols.has('priority') && (
          <SortableTh label={t('calendar.common.priority')} field="priority" sortField={sortField} sortDir={sortDir} onSort={handleSort} widthCls="w-16" />
        )}
        {visibleCols.has('progress') && (
          <SortableTh label={t('calendar.common.progress')} field="progress" sortField={sortField} sortDir={sortDir} onSort={handleSort} widthCls="w-20" />
        )}
        {visibleCols.has('location') && (
          <th className="px-3 py-2 border-b border-slate-200 dark:border-slate-800 w-32">{t('calendar.common.location')}</th>
        )}
        {visibleCols.has('recurrence') && (
          <th className="px-3 py-2 border-b border-slate-200 dark:border-slate-800 w-20">{t('calendar.views.list.recurrence')}</th>
        )}
        {visibleCols.has('reminder') && (
          <th className="px-3 py-2 border-b border-slate-200 dark:border-slate-800 w-24">{t('calendar.common.reminder')}</th>
        )}
      </tr>
    );
  };

  return (
    <div className="flex flex-col w-full h-full min-h-0 bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-800 overflow-hidden shadow-sm">
      <div className="p-4 max-md:p-2 border-b border-slate-200 dark:border-slate-800 flex items-center max-md:flex-wrap gap-3 bg-gradient-to-r from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
        <Segmented
          size="sm"
          value={filterKey}
          options={PRESETS.map((p) => ({ value: p.value, label: t(p.labelKey) }))}
          onChange={(v) => {
            setFilterKey(v);
            applyPreset(v);
          }}
        />
        {/* 自定义日期模式下显示起止日期选择器 */}
        {filterKey === 'custom' && (
          <>
            <div className="h-6 w-px bg-gradient-to-b from-transparent via-slate-300 to-transparent dark:via-slate-700 mx-1" />
            <div className="flex items-center gap-1.5 text-xs">
              <input
                type="date"
                value={query.startDate ?? ''}
                onChange={(e) => setQueryField('startDate', e.target.value || null, true)}
                className="h-8 px-2 rounded border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 transition-all"
              />
              <span className="text-slate-400">{t('calendar.views.list.dateRangeSeparator')}</span>
              <input
                type="date"
                value={query.endDate ?? ''}
                onChange={(e) => setQueryField('endDate', e.target.value || null, true)}
                className="h-8 px-2 rounded border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 transition-all"
              />
            </div>
          </>
        )}
        <div className="h-6 w-px bg-gradient-to-b from-transparent via-slate-300 to-transparent dark:via-slate-700 mx-1" />
        <input
          type="search"
          placeholder={t('calendar.views.list.searchPlaceholder')}
          value={query.keyword ?? ''}
          onChange={(e) => setQueryField('keyword', e.target.value, true)}
          className="h-8 px-3 w-full sm:w-64 rounded border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-sm focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 transition-all"
        />
        <select
          value={query.eventType ?? 'all'}
          onChange={(e) => setQueryField('eventType', e.target.value === 'all' ? null : e.target.value, true)}
          className="h-8 px-2 rounded border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 transition-all"
        >
          {EVENT_TYPE_PRESETS.map((v) => (
            <option key={v} value={v}>{v === 'all' ? t('calendar.views.list.allTypes') : EVENT_TYPE_STYLE[v]?.label ?? v}</option>
          ))}
        </select>
        <select
          value={query.status ?? 'all'}
          onChange={(e) => setQueryField('status', e.target.value === 'all' ? null : e.target.value, true)}
          className="h-8 px-2 rounded border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 transition-all"
        >
          {STATUS_PRESETS.map((v) => (
            <option key={v} value={v}>{v === 'all' ? t('calendar.views.list.allStatuses') : STATUS_STYLE[v]?.label ?? v}</option>
          ))}
        </select>
        {/* 列设置按钮（功能 A） */}
        <button
          type="button"
          title={t('calendar.views.list.columnSettings')}
          onClick={() => setShowColSettings((v) => !v)}
          className={`h-8 w-8 flex items-center justify-center rounded border transition-all ${showColSettings ? 'border-sky-400 bg-sky-50 text-sky-600 dark:bg-sky-900/30 dark:text-sky-400' : 'border-slate-300 dark:border-slate-700 text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800'}`}
        >
          <SettingsIcon className="w-4 h-4" />
        </button>
        <BatchMultiSelectToolbar
          variant="full"
          multiSelectMode={multiSelectMode}
          selectedCount={selectedEventIds.size}
          totalVisibleCount={visibleIds.length}
          onEnterMultiSelect={handleEnterMultiSelect}
          onExitMultiSelect={handleExitMultiSelect}
          onClearSelection={clearSelection}
          onSelectAllOverride={toggleSelectAllVisible}
        />
      </div>

      {/* 列设置弹出面板 */}
      {showColSettings && (
        <ColumnSettingsPopover
          visibleCols={visibleCols}
          onChange={(next) => { setVisibleCols(next); saveVisibleCols(next); }}
          onClose={() => setShowColSettings(false)}
        />
      )}

      <div className="flex-1 min-h-0 overflow-auto">
        {isEmpty ? (
          <EmptyState
            variant="list"
            title={t('calendar.views.list.emptyTitle')}
            onPrimary={() => openCreate()}
          />
        ) : (
          <>
            {/* PC 表格视图 */}
            <div className="hidden md:block">
              <table className="w-full text-sm border-separate border-spacing-0">
                <thead className="sticky top-0 bg-gradient-to-r from-slate-50 to-slate-100/50 dark:from-slate-950 dark:to-slate-900/50 z-10">
                  {renderHeader()}
                </thead>
                <tbody>
                  {rows.map((ev) => (
                    <MemoizedListRow
                      key={ev.recurrenceOriginalStart ? `${ev.id}#${ev.recurrenceOriginalStart}` : `${ev.id}`}
                      ev={ev}
                      visibleCols={visibleCols}
                      sortField={sortField}
                      onOpen={openEdit}
                      canManage={canManageCalendar}
                      multiSelectMode={multiSelectMode}
                      selected={isSelectedFn(ev.id)}
                      onToggleSelect={toggleSelect}
                      onQuickComplete={handleQuickComplete}
                      onQuickDelete={handleQuickDelete}
                      onQuickDuplicate={handleQuickDuplicate}
                    />
                  ))}
                </tbody>
              </table>
            </div>
            {/* 移动端卡片视图 */}
            <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
              {rows.map((ev) => (
                <ListMobileCard
                  key={ev.recurrenceOriginalStart ? `${ev.id}#${ev.recurrenceOriginalStart}` : `${ev.id}`}
                  ev={ev}
                  visibleCols={visibleCols}
                  sortField={sortField}
                  onOpen={openEdit}
                  canManage={canManageCalendar}
                  multiSelectMode={multiSelectMode}
                  selected={isSelectedFn(ev.id)}
                  onToggleSelect={toggleSelect}
                  onQuickComplete={handleQuickComplete}
                  onQuickDelete={handleQuickDelete}
                  onQuickDuplicate={handleQuickDuplicate}
                />
              ))}
            </div>
          </>
        )}
      </div>

      <Pagination
        totalItems={list?.totalItems ?? 0}
        currentPage={list?.currentPage ?? query.page ?? 1}
        totalPages={list?.totalPages ?? 1}
        pageSize={query.pageSize ?? 20}
        gotoPage={(p) => setQueryField('page', p, false)}
      />
    </div>
  );
}
