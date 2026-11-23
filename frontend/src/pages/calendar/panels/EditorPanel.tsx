import { useEffect, useCallback, useRef, useState, useMemo } from 'react';
import { useI18n } from '../../../context/I18nContext';
import type { TranslationKey } from '../../../locales/zh-Hans';
import {
  X,
  Trash2,
  Check,
  Loader2,
  ChevronDown,
  Repeat,
  Bell,
  Users,
  Palette,
  Calendar as CalendarIcon,
  Clock,
  MapPin,
  Type,
  AlignLeft,
  Tag,
  Sparkles,
  FileText as FileTextIcon,
  AlertTriangle,
  AlertCircle,
  Save as SaveIcon,
  Settings2 as Settings2Icon,
  Download as DownloadIcon,
} from 'lucide-react';
const Trash2Icon = Trash2;
import { useCalendarStore, getErrorMessage, getErrorStatus, getErrorData } from '../store';
import type { ConflictItem, ConflictResponseData, ApiErrorResponseBody } from '../store';
import { ModalHeader, Icon as SharedIcon } from './shared';
import {
  EVENT_TYPE_STYLE,
  STATUS_STYLE,
  STATUS_OPTIONS,
  COLOR_OPTIONS,
  REMINDER_OPTIONS,
  REMINDER_TYPE_OPTIONS,
  RECURRENCE_TYPE_OPTIONS,
  recurrenceUnitLabel,
  generateRRuleFromForm,
  generateRRuleDescription,
  parseReminderTypes,
  serializeReminderTypes,
  reminderTypesLabel,
  fromIsoToLocalInput,
  toIsoString,
} from '../calendarUtils';
import type { CalendarEventPriority, EditorForm } from '../calendarUtils';
import { calendarApi } from '../../../api/modules/engagement';
import { userApi } from '../../../api/modules/core';
import type { CreateCalendarEventRequest, CalendarEventDto, UserResponse } from '../../../api/types';
import { _checkConflictForRange, shallowEqual, type ConflictCheckResult } from '../hooks';
const shallow = shallowEqual;

/** 周几按钮：码（0=周日 … 6=周六）→ 词条 key。label 随语言变化，只能在渲染时取，不要预存成字符串。 */
const WEEKDAYS: { key: number; labelKey: TranslationKey }[] = [
  { key: 0, labelKey: 'calendar.common.weekdayShort.sun' },
  { key: 1, labelKey: 'calendar.common.weekdayShort.mon' },
  { key: 2, labelKey: 'calendar.common.weekdayShort.tue' },
  { key: 3, labelKey: 'calendar.common.weekdayShort.wed' },
  { key: 4, labelKey: 'calendar.common.weekdayShort.thu' },
  { key: 5, labelKey: 'calendar.common.weekdayShort.fri' },
  { key: 6, labelKey: 'calendar.common.weekdayShort.sat' },
];

/**
 * 任务优先级：码 → 标签 key。
 *
 * ⚠ 与 `format.priority.*`（会话优先级：低/普通/高/紧急）不是同一套；这里是编辑器
 * `CalendarEventPriority` 枚举（LOW/MEDIUM/HIGH/URGENT，落库为 priority 1–4）。
 * 映射写成 `TranslationKey | undefined`：本项目未开 noUncheckedIndexedAccess，
 * 写成 `Record<string, TranslationKey>` 是类型说谎。取用点用 `?? 兜底 key`。
 */
const PRIORITY_OPTIONS: { v: CalendarEventPriority }[] = [
  { v: 'LOW' },
  { v: 'MEDIUM' },
  { v: 'HIGH' },
  { v: 'URGENT' },
];
const PRIORITY_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  LOW: 'calendar.editor.priorityLow',
  MEDIUM: 'calendar.editor.priorityMedium',
  HIGH: 'calendar.editor.priorityHigh',
  URGENT: 'calendar.editor.priorityUrgent',
};

/** 重复结束方式：码（never/count/date，同时是 store 里 recurrenceEndMode 的字面量）→ 标题/描述 key */
const RECURRENCE_END_MODES: { v: 'never' | 'count' | 'date'; labelKey: TranslationKey; descKey: TranslationKey }[] = [
  { v: 'never', labelKey: 'calendar.editor.endModeNever', descKey: 'calendar.editor.endModeNeverDesc' },
  { v: 'count', labelKey: 'calendar.editor.endModeCount', descKey: 'calendar.editor.endModeCountDesc' },
  { v: 'date', labelKey: 'calendar.editor.endModeDate', descKey: 'calendar.editor.endModeDateDesc' },
];

// 注：RRULE 生成逻辑（含 BYDAY/WEEKDAY 映射）已统一迁移到 calendarUtils.ts 的
// generateRRuleFromForm / generateRRuleDescription 纯函数，便于 handleSave 中复用。

export default function EditorPanel() {
  const {
    showEditor,
    editing,
    form,
    saving,
    formError,
    recurrencePanelOpen,
    reminderPanelOpen,
    attendeeDropdownOpen,
    recurrenceEndMode,
    deleteTarget,
    deleting,
    setFormField,
    attendeeCount,
    eventTemplates,
    applyTemplate,
    saveTemplate,
    deleteTemplate,
    showTransientToast,
  } = useCalendarStore((s) => ({
    showEditor: s.showEditor,
    editing: s.editing,
    form: s.form,
    saving: s.saving,
    formError: s.formError,
    recurrencePanelOpen: s.recurrencePanelOpen,
    reminderPanelOpen: s.reminderPanelOpen,
    attendeeDropdownOpen: s.attendeeDropdownOpen,
    recurrenceEndMode: s.recurrenceEndMode,
    deleteTarget: s.deleteTarget,
    deleting: s.deleting,
    setFormField: s.setFormField,
    attendeeCount: s._attendeeEmailList.length, // P0 用邮箱列表数量显示计数
    eventTemplates: s.eventTemplates,
    applyTemplate: s.applyTemplate,
    saveTemplate: s.saveTemplate,
    deleteTemplate: s.deleteTemplate,
    showTransientToast: s.showTransientToast,
  }), shallow);

  const { t } = useI18n();

  const [conflictToast, setConflictToast] = useState<{ tag: 'hard' | 'soft'; count: number } | null>(null);
  const toastTimerRef = useRef<number | null>(null);
  const closeTimerRef = useRef<number | null>(null);
  const signalRRevTimerRef = useRef<number | null>(null);
  const [closing, setClosing] = useState(false);
  // P0 演示：参会人临时存邮箱字符串，显示/删除独立于 attendees number[]，等 P1 接通讯录时再映射
  const [attendeeInput, setAttendeeInput] = useState('');
  const [attendeeEmailList, setAttendeeEmailList] = useState<string[]>(
    () => useCalendarStore.getState()._attendeeEmailList ?? []
  );
  // r5: 参会人输入框 ref — 用于在添加邮箱后主动恢复焦点，避免 React 重渲染导致失焦
  const attendeeInputRef = useRef<HTMLInputElement | null>(null);
  // r5: 无效邮箱提示状态 — 当输入非空且无法识别为合法邮箱时显示提示
  const [attendeeInvalidHint, setAttendeeInvalidHint] = useState<string | null>(null);
  // 任务负责人：可分配用户列表
  const [assignableUsers, setAssignableUsers] = useState<UserResponse[]>([]);
  const [assignableLoading, setAssignableLoading] = useState(false);
  // P2-A#2: 保存为模板 / 管理模板 模态框状态
  const [showSaveTemplateModal, setShowSaveTemplateModal] = useState(false);
  const [showManageTemplatesModal, setShowManageTemplatesModal] = useState(false);
  const [templateNameInput, setTemplateNameInput] = useState('');
  const [saveTemplateLoading, setSaveTemplateLoading] = useState(false);
  const [manageDeletingId, setManageDeletingId] = useState<number | null>(null);
  // 主持人多选下拉菜单：开关 + 外部点击 ref
  const [hostDropdownOpen, setHostDropdownOpen] = useState(false);
  const hostDropdownRef = useRef<HTMLDivElement | null>(null);

  // P2-时间字段联动：标记 dueAt 是否被用户手动改过，true 时 endAt blur 不再自动覆盖 dueAt
  const dueAtDirtyRef = useRef(false);
  // 全天事件切换前保存的 {startAt, endAt}：开启全天时清空时间部分，关闭时恢复原值
  const preAllDayToggleRef = useRef<{ startAt: string; endAt: string } | null>(null);

  const setOptionalField = useCallback(
    <K extends keyof EditorForm>(key: K, value: EditorForm[K] | undefined) =>
      setFormField(key, value as EditorForm[K]),
    [setFormField],
  );

  // Reset transient UI state when editor opens — without this, closing=true / conflictToast
  // persist after the first close (component stays mounted via `if (!showEditor) return null`),
  // causing the content to render with cal-pop-out animation (invisible) while the
  // backdrop's cal-fade-in-up animation overrides opacity-0, resulting in
  // "blurred background with no content" on all subsequent opens.
  // Also re-sync the local attendee email list from store._attendeeEmailList, because
  // useState's lazy initializer only runs once at mount; if the user closes and reopens
  // the editor (e.g. editing a different event), the store has fresh data but the local
  // state would be stale without this explicit sync.
  useEffect(() => {
    if (showEditor) {
      setClosing(false);
      setConflictToast(null);
      setAttendeeEmailList(useCalendarStore.getState()._attendeeEmailList ?? []);
      setAttendeeInput('');
      // r5: 打开编辑器时清空上一次的无效邮箱提示
      setAttendeeInvalidHint(null);
      // 重置全天切换快照：每次打开编辑器时清空，避免上次会话残留
      preAllDayToggleRef.current = null;
      // 初始化 dueAtDirty：若 task 类型且 dueAt 为空 / 与 endAt 相等 → 视为默认跟随，dirty=false；
      // 否则（编辑旧任务时 dueAt 本来就不等 endAt）→ 用户以前改过，dirty=true
      const f = useCalendarStore.getState().form;
      if (f.eventType === 'task') {
        const endIso = f.endAt ? toIsoString(f.endAt) : undefined;
        dueAtDirtyRef.current = !!(f.dueAt && endIso && f.dueAt !== endIso);
      } else {
        dueAtDirtyRef.current = false;
      }
      // Bug fix: 编辑已有重复事件时，根据 recurrenceCount / recurrenceEndDate 初始化结束方式
      // 否则 UI 默认显示"永不"，保存时 generateRRuleFromForm 会丢失 COUNT/UNTIL 参数
      if (f.recurrenceCount != null && f.recurrenceCount > 0) {
        useCalendarStore.setState({ recurrenceEndMode: 'count' });
      } else if (f.recurrenceEndDate && f.recurrenceEndDate.trim()) {
        useCalendarStore.setState({ recurrenceEndMode: 'date' });
      } else {
        useCalendarStore.setState({ recurrenceEndMode: 'never' });
      }
    }
  }, [showEditor]);

  // 加载可分配用户列表（用于任务负责人下拉）
  useEffect(() => {
    if (!showEditor) return;
    let cancelled = false;
    setAssignableLoading(true);
    userApi
      .assignable()
      .then((list) => {
        if (cancelled) return;
        setAssignableUsers(list ?? []);
      })
      .catch(() => {
        if (cancelled) return;
        setAssignableUsers([]);
      })
      .finally(() => {
        if (!cancelled) setAssignableLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [showEditor]);

  // 主持人多选下拉菜单：点击外部关闭
  useEffect(() => {
    if (!hostDropdownOpen) return;
    const onDown = (e: MouseEvent) => {
      if (hostDropdownRef.current && !hostDropdownRef.current.contains(e.target as Node)) {
        setHostDropdownOpen(false);
      }
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setHostDropdownOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [hostDropdownOpen]);

  useEffect(() => {
    return () => {
      if (toastTimerRef.current !== null) {
        clearTimeout(toastTimerRef.current);
        toastTimerRef.current = null;
      }
      if (closeTimerRef.current !== null) {
        clearTimeout(closeTimerRef.current);
        closeTimerRef.current = null;
      }
      if (signalRRevTimerRef.current !== null) {
        clearTimeout(signalRRevTimerRef.current);
        signalRRevTimerRef.current = null;
      }
    };
  }, []);

  const handleOverlayClick = useCallback((e: React.MouseEvent) => {
    if (e.target !== e.currentTarget) return;
    if (closing) return;
    setClosing(true);
    if (closeTimerRef.current !== null) {
      clearTimeout(closeTimerRef.current);
    }
    closeTimerRef.current = window.setTimeout(() => {
      useCalendarStore.setState({ pendingSignalRBump: true });
      const s = useCalendarStore.getState();
      s.resetForm();
      s.closeEditor();
    }, 120);
  }, [closing]);

  const toggleRecurrencePanel = useCallback(() => {
    useCalendarStore.setState((s) => ({ recurrencePanelOpen: !s.recurrencePanelOpen }));
  }, []);

  const toggleReminderPanel = useCallback(() => {
    useCalendarStore.setState((s) => ({ reminderPanelOpen: !s.reminderPanelOpen }));
  }, []);

  const toggleAttendeeDropdown = useCallback(() => {
    useCalendarStore.setState((s) => ({ attendeeDropdownOpen: !s.attendeeDropdownOpen }));
  }, []);

  const setRecurrenceEndModeState = useCallback((v: 'count' | 'date' | 'never') => {
    useCalendarStore.setState({ recurrenceEndMode: v });
  }, []);

  const handleRecurrenceEndModeChange = useCallback((mode: 'never' | 'count' | 'date') => {
    setRecurrenceEndModeState(mode);
  }, [setRecurrenceEndModeState]);



  const setDeleteTargetState = useCallback((ev: CalendarEventDto | null) => {
    useCalendarStore.setState({ deleteTarget: ev });
  }, []);

  const handleAllDayToggle = useCallback(() => {
    const state = useCalendarStore.getState();
    const currentIsAllDay = state.form.isAllDay;
    const currentStartAt = state.form.startAt;
    const currentEndAt = state.form.endAt;

    if (!currentIsAllDay) {
      // 开启全天事件：保存当前 {startAt, endAt}，startAt 保留日期部分（去掉时间），endAt 清空
      preAllDayToggleRef.current = { startAt: currentStartAt, endAt: currentEndAt };
      const dateOnly = currentStartAt ? currentStartAt.split('T')[0] : currentStartAt;
      useCalendarStore.setState((s) => ({
        form: { ...s.form, isAllDay: true, startAt: dateOnly, endAt: '' },
      }));
    } else {
      // 关闭全天事件：恢复开启前保存的 {startAt, endAt}
      const saved = preAllDayToggleRef.current;
      if (saved) {
        useCalendarStore.setState((s) => ({
          form: { ...s.form, isAllDay: false, startAt: saved.startAt, endAt: saved.endAt },
        }));
        preAllDayToggleRef.current = null;
      } else {
        // 无保存状态（编辑已存在的全天事件）：开始时间设为当天 09:00，结束时间设为当天 10:00
        const pad = (n: number) => String(n).padStart(2, '0');
        const dateStr = currentStartAt ? currentStartAt.split('T')[0] : `${new Date().getFullYear()}-${pad(new Date().getMonth() + 1)}-${pad(new Date().getDate())}`;
        useCalendarStore.setState((s) => ({
          form: { ...s.form, isAllDay: false, startAt: `${dateStr}T09:00`, endAt: `${dateStr}T10:00` },
        }));
      }
    }
  }, []);

  // 主持人多选下拉菜单：显示名 + 勾选切换
  const getHostUserDisplayName = useCallback((u: UserResponse): string => {
    return (u.fullName || `${u.firstName} ${u.lastName}`.trim()) || t('calendar.editor.userFallback', { id: u.id });
  }, [t]);

  const toggleHost = useCallback((userId: number) => {
    const cur = useCalendarStore.getState().form.hostUserIds ?? [];
    const next = cur.includes(userId)
      ? cur.filter((id) => id !== userId)
      : [...cur, userId];
    setOptionalField('hostUserIds', next.length ? next : undefined);
  }, [setOptionalField]);

  const handleSave = useCallback(async () => {
    const state = useCalendarStore.getState();
    if (!state.form.title.trim()) {
      useCalendarStore.setState({ formError: t('calendar.editor.titleRequired') });
      return;
    }
    const formData = state.form;
    const editingId = state.editing?.id;
    // 第三梯队: 保存编辑前快照，用于撤销栈
    const beforeSnapshot = state.editing ? { ...state.editing } : null;

    // 🔧 字段归一化：解决 UI 字段与 API 字段不一致的问题
    // 1. 会议链接：UI 写 meetingLink (可选字段)，API 要 meetingUrl
    const resolvedMeetingUrl = (formData.meetingLink ?? formData.meetingUrl) || null;
    // 2. 优先级：UI 写 eventPriority (枚举字符串)，API 要 priority (数字)
    const PRIORITY_MAP: Record<string, number> = { LOW: 1, MEDIUM: 2, HIGH: 3, URGENT: 4 };
    const resolvedPriority: number | null = formData.eventPriority
      ? PRIORITY_MAP[formData.eventPriority] ?? formData.priority
      : formData.priority;
    // 3. 提醒提前分钟：统一使用扁平字段 reminderMinutesBefore（底部手风琴唯一入口）。
    //    reminderSettings 仅作旧数据兼容回退（编辑历史事件时可能存在）。
    const resolvedReminderMinutes: number | null =
      formData.reminderMinutesBefore != null
        ? formData.reminderMinutesBefore
        : formData.reminderSettings?.minutesAhead ?? null;
    // 4. isRecurring 自动联动：当 recurrenceType >= 0 时强制 true
    const resolvedIsRecurring =
      formData.recurrenceType != null && formData.recurrenceType >= 0
        ? true
        : formData.isRecurring;
    // 5. 参会人邮箱列表同步：UI 维护 _attendeeEmailList（string[]），后端 Attendees 是 number[] 不兼容，
    //    将其序列化为 CSV 字符串写入 reminderEmails 字段（同时作为提醒接收邮箱），保存后能持久化、重新打开能回填
    const currentEmailList = useCalendarStore.getState()._attendeeEmailList ?? [];
    const resolvedReminderEmails: string | null =
      currentEmailList.length > 0 ? currentEmailList.join(',') : null;
    // 6. 重复规则自动生成：用户可能修改频率/间隔后未点击「生成 RRULE」按钮，保存前补一次确保最新
    //    且仅保留纯 RRULE 字符串（去掉 ` | 中文描述` 部分），避免后端解析失败
    let resolvedRecurrenceRule: string | null = formData.recurrenceRule || null;
    if (resolvedIsRecurring && formData.recurrenceType != null && formData.recurrenceType >= 0) {
      const auto = generateRRuleFromForm(formData, useCalendarStore.getState().recurrenceEndMode);
      if (auto) resolvedRecurrenceRule = auto;
    } else {
      resolvedRecurrenceRule = null;
    }
    // 7. 提醒方式：统一使用扁平字段 reminderType（底部手风琴唯一入口）。
    //    reminderSettings 仅作旧数据兼容回退（编辑历史事件时可能存在）。
    const resolvedReminderType: string | null =
      formData.reminderType ?? formData.reminderSettings?.type ?? null;

    // 8. 参会人（int[] 用户 ID）：仅在 UI 真有"选择用户 ID 型参会人"时写入，
    //    避免把空数组 `[]` 强塞 payload，与"邮箱型参会人 + reminderEmails CSV"
    //    的语义混淆。邮箱型参会人由 resolvedReminderEmails 承载，后端会双写归一化。
    const resolvedAttendees: number[] | undefined =
      Array.isArray(formData.attendees) && formData.attendees.length > 0
        ? formData.attendees
        : undefined;

    // 构建 API payload
    const payload: CreateCalendarEventRequest = {
      title: formData.title,
      description: formData.description || null,
      eventType: formData.eventType,
      startAt: toIsoString(formData.startAt),
      endAt: formData.endAt ? toIsoString(formData.endAt) : null,
      isAllDay: formData.isAllDay,
      mailboxId: formData.mailboxId,
      userId: formData.userId,
      color: formData.color,
      isRecurring: resolvedIsRecurring,
      recurrenceType: formData.recurrenceType,
      recurrenceInterval: formData.recurrenceInterval,
      recurrenceEndDate: formData.recurrenceEndDate || null,
      recurrenceCount: formData.recurrenceCount,
      reminderMinutes: resolvedReminderMinutes,
      reminderEmails: resolvedReminderEmails,
      priority: resolvedPriority,
      progress: formData.progress,
      location: formData.location || null,
      meetingUrl: resolvedMeetingUrl,
      attendees: resolvedAttendees,
      subTasks: formData.subTasks,
      relatedEventId: formData.relatedEventId,
      recurrenceExclusions: formData.recurrenceExclusions,
      // ====== 事件类型专属字段（2026-08-17 持久化） ======
      // 主持人用户 ID 数组（会议类型）
      hostUserIds: formData.hostUserIds && formData.hostUserIds.length > 0
        ? formData.hostUserIds
        : undefined,
      // 议程 ID 数组（会议类型）
      agendaIds: formData.agendaIds && formData.agendaIds.length > 0
        ? formData.agendaIds
        : undefined,
      // 任务负责人 ID（任务类型，UI 存字符串需转 number）
      taskOwnerId: formData.taskOwner
        ? Number(formData.taskOwner) || null
        : null,
      // 任务截止时间（任务类型）
      dueAt: formData.dueAt || null,
      // 提醒方式（提醒类型，从 reminderSettings.type 取值；否则保留原 reminderType）
      reminderType: resolvedReminderType,
      // 每周哪几天（重复日程面板，0=周日..6=周六）
      weekdaysSelected: formData.weekdaysSelected && formData.weekdaysSelected.length > 0
        ? formData.weekdaysSelected
        : undefined,
      // RRULE 原始字符串（重复日程面板，已自动生成纯 RRULE，不含中文描述）
      recurrenceRule: resolvedRecurrenceRule,
    };

    // 关键体验修复：立即关闭模态窗（不等 await API 返回），满足用户"点击保存后马上关闭"需求。
    // 保存 editing 快照（closeEditor 后 editing 变 null，后续乐观更新需要 recurrenceOriginalStart/rowVersion）
    const editingSnapshot = state.editing ? { ...state.editing } : null;
    setClosing(true);
    if (toastTimerRef.current !== null) { clearTimeout(toastTimerRef.current); toastTimerRef.current = null; }
    if (closeTimerRef.current !== null) { clearTimeout(closeTimerRef.current); }
    setConflictToast(null);
    closeTimerRef.current = window.setTimeout(() => {
      useCalendarStore.setState({ pendingSignalRBump: true });
      const s = useCalendarStore.getState();
      s.resetForm();
      s.closeEditor();
    }, 120);

    // 异步执行 API 调用：模态窗已关闭，错误提示改用全局 showTransientToast（不再依赖模态窗内 formError）
    let savedEvent: CalendarEventDto | null = null;
    try {
      if (editingId && editingId > 0) {
        const ifMatch = editingSnapshot?.rowVersion ?? undefined;
        const res = await calendarApi.update(editingId, {
          ...payload,
          id: editingId,
          status: formData.status,
          ifMatch,
        });
        // 关键修复：用后端返回的 DTO 覆盖本地，确保 rowVersion/startAt/endAt 与数据库实际落库版本完全一致。
        savedEvent = res ?? null;
      } else {
        savedEvent = await calendarApi.create(payload);
      }
    } catch (e: unknown) {
      const errData = getErrorData<ApiErrorResponseBody>(e);
      const apiMsg = errData?.message;
      const conflictData = errData?.data as ConflictResponseData | undefined;
      const status = getErrorStatus(e);
      // P0: 服务端冲突检测返回 409 + 冲突事件列表
      if (status === 409 && conflictData?.conflicts) {
        const conflicts = conflictData.conflicts as ConflictItem[];
        const titles = conflicts
          .map(c => c.title || t('calendar.common.untitled'))
          .slice(0, 3)
          .join(t('calendar.editor.titleListSeparator'));
        const extra = conflicts.length > 3
          ? t('calendar.editor.conflictMore', { count: conflicts.length })
          : '';
        useCalendarStore.getState().showTransientToast(
          t('calendar.editor.saveConflictToast', {
            message: apiMsg || t('calendar.common.timeConflict'),
            titles,
            extra,
          }),
          'error',
          3500,
        );
        return;
      }
      // P3: 乐观锁并发冲突（rowVersion 不匹配/DB 并发）→ 用 latest 覆盖本地 + 提示
      if (status === 409 && (conflictData?.errorType === 'calendar_concurrency_conflict' || conflictData?.latest)) {
        const latest = conflictData?.latest as CalendarEventDto | undefined | null;
        if (latest) {
          useCalendarStore.getState().optimisticUpsertEvent(latest);
        }
        useCalendarStore.getState().showTransientToast(t('calendar.editor.staleSyncRefreshed'), 'warning', 3500);
        return;
      }
      // 其他 409（如 DbUpdateConcurrencyException 未附带 latest）：温和提示
      if (status === 409) {
        useCalendarStore.getState().showTransientToast(t('calendar.editor.staleSyncRetryHint'), 'warning', 3500);
        return;
      }
      const msg = apiMsg || getErrorMessage(e) || t('calendar.editor.saveFailed');
      useCalendarStore.getState().showTransientToast(typeof msg === 'string' ? msg : t('calendar.editor.saveFailed'), 'error', 3000);
      return;
    }

    // 保存成功：立刻把 DTO 乐观插入/更新到本地视图（避免等 signalRRev → API 拉取回来才能看到）
    // 关键：后端返回的父事件 DTO 不带 recurrenceOriginalStart（null），直接 upsert 会因复合 key
    // 不匹配（id vs id#originalStart）导致追加新事件而非替换，造成两条色块。
    // 用 editingSnapshot 的 recurrenceOriginalStart 覆盖 savedEvent，确保 key 一致。
    if (savedEvent) {
      const originalRS = editingSnapshot?.recurrenceOriginalStart ?? null;
      const upsertedEvent = {
        ...savedEvent,
        recurrenceOriginalStart: originalRS ?? savedEvent.recurrenceOriginalStart ?? null,
      };
      useCalendarStore.getState().optimisticUpsertEvent(upsertedEvent);

      // 关键修复：新建重复事件保存后，后端会展开为多个实例（各有不同的 recurrenceOriginalStart）。
      // optimisticUpsertEvent 打上的乐观标记（5 秒保护期）会让 setAllEvents 跳过后端展开的实例，
      // 导致重复色块不显示（只有父事件一个色块，需刷新页面才显示全部）。
      // 对新建的重复事件清除乐观标记，让 signalRRev bump → refreshViewMode → setAllEvents
      // 能正常接收并显示后端展开的所有重复实例。
      if (!editingId && (savedEvent.isRecurring || resolvedIsRecurring)) {
        useCalendarStore.getState()._unmarkOptimistic(upsertedEvent.id);
      }

      // P2-2: 若 startAt/endAt 变化，写入 pending patch，确保刷新后 replay 保留编辑结果
      if (beforeSnapshot) {
        const startChanged = beforeSnapshot.startAt !== upsertedEvent.startAt;
        const endChanged = (beforeSnapshot.endAt ?? null) !== (upsertedEvent.endAt ?? null);
        if (startChanged || endChanged) {
          useCalendarStore.getState().upsertPendingPatch({
            eventId: upsertedEvent.id,
            startAt: upsertedEvent.startAt,
            endAt: upsertedEvent.endAt ?? undefined,
            operation: 'move',
            appliedAtMs: Date.now(),
            oldStartAt: upsertedEvent.isRecurring
              ? (upsertedEvent.recurrenceOriginalStart ?? upsertedEvent.startAt)
              : undefined,
          });
        }
      }
    }

    // 第三梯队: 推入撤销栈（编辑/创建）
    if (savedEvent) {
      useCalendarStore.getState().pushUndo({
        type: beforeSnapshot ? 'edit' : 'create',
        description: beforeSnapshot ? t('calendar.editor.undoEdit') : t('calendar.common.newEvent'),
        beforeSnapshot,
        afterSnapshot: { ...savedEvent },
        eventId: savedEvent.id,
      });
    }

    // 立即 bump signalRRev 触发数据刷新：乐观保护期 5 秒会保留本地版本，避免后端写库延迟导致回弹。
    // 对于重复日程，刷新会重新拉取并展开所有实例，展示最新的重复色块。
    useCalendarStore.setState((s) => ({ signalRRev: s.signalRRev + 1 }));
  }, [t]);

  const handleDelete = useCallback(() => {
    const state = useCalendarStore.getState();
    if (!state.editing) return;
    setDeleteTargetState(state.editing);
  }, [setDeleteTargetState]);

  const confirmDelete = useCallback(async () => {
    const state = useCalendarStore.getState();
    if (!state.deleteTarget) return;
    const deleteId = state.deleteTarget.id;
    if (!deleteId || deleteId <= 0) {
      useCalendarStore.setState({ deleteTarget: null });
      return;
    }
    // 第三梯队: 保存删除前快照，用于撤销栈
    const beforeSnapshot = { ...state.deleteTarget };

    // ========== 乐观删除（零延迟体验）：不等 API 返回，立刻从视图中移除 ==========
    // 构造一个带 deletedAt 标记的 DTO 传给 optimisticUpsertEvent，
    // 该方法内部会直接把事件从 allEvents 过滤掉 → 日历视图上 <16ms 消失，
    // 不再依赖 signalRRev → debounceRefresh → API 拉取 → setAllEvents 过滤这条长链路。
    const deletedMarker: CalendarEventDto = {
      ...beforeSnapshot,
      deletedAt: new Date().toISOString(),
    };
    useCalendarStore.getState().optimisticUpsertEvent(deletedMarker);
    // 垃圾箱红点徽标立刻 +1（不再等 API 成功或下次刷新）
    useCalendarStore.setState((s) => ({ trashCount: s.trashCount + 1 }));

    useCalendarStore.setState({ deleting: true, formError: null });
    try {
      await calendarApi.remove(deleteId, { rowVersion: beforeSnapshot.rowVersion ?? null });
    } catch (e: unknown) {
      // ========== API 失败：回滚乐观删除（把事件重新加回 allEvents） ==========
      const rollbackEvent: CalendarEventDto = { ...beforeSnapshot, deletedAt: '' };
      useCalendarStore.getState().optimisticUpsertEvent(rollbackEvent);
      // 还原 trashCount
      useCalendarStore.setState((s) => ({ trashCount: Math.max(0, s.trashCount - 1) }));
      const errData = getErrorData<ApiErrorResponseBody>(e);
      const conflictData = errData?.data as ConflictResponseData | undefined;
      const status = getErrorStatus(e);
      if (
        status === 409 &&
        (conflictData?.errorType === 'calendar_concurrency_conflict' || conflictData?.latest)
      ) {
        const latest = conflictData?.latest as CalendarEventDto | undefined | null;
        if (latest) {
          useCalendarStore.getState().optimisticUpsertEvent({ ...latest, deletedAt: '' });
        }
        const msg = errData?.message || t('calendar.common.staleDataRefreshed');
        useCalendarStore.setState({
          deleting: false,
          formError: typeof msg === 'string' ? msg : t('calendar.editor.staleDataRetryHint'),
        });
        return;
      }
      const msg = errData?.message || getErrorMessage(e) || t('calendar.editor.deleteFailed');
      useCalendarStore.setState({ deleting: false, formError: typeof msg === 'string' ? msg : t('calendar.editor.deleteFailed') });
      return;
    }
    // 删除成功（视图上其实早就消失了），只清理状态
    useCalendarStore.setState({
      deleting: false,
      deleteTarget: null,
    });
    // P2-5 一致性补丁：删除成功后清理 localStorage 中该事件的 pending patch，
    // 避免残留补丁在 setAllEvents 的 patch replay 逻辑里误判后端未同步而再次回填事件。
    // 与 ListView.handleQuickDelete 保持一致。
    useCalendarStore.getState().dropPendingPatch(deleteId);
    // 第三梯队: 推入撤销栈（删除）
    useCalendarStore.getState().pushUndo({
      type: 'delete',
      description: t('calendar.editor.undoDelete'),
      beforeSnapshot,
      afterSnapshot: null,
      eventId: deleteId,
    });
    // 删除成功后立即触发 signalRRev 刷新，从后端重新拉取事件列表，
    // 确保已删除事件从日历界面消失（不再延迟 2 秒，避免色块残留）。
    // optimisticUpsertEvent 已先提供即时反馈（从 allEvents 移除），
    // signalRRev bump 确保后端数据同步后视图一致。
    useCalendarStore.setState((s) => ({ signalRRev: s.signalRRev + 1 }));
    if (closing) return;
    setClosing(true);
    if (closeTimerRef.current !== null) {
      clearTimeout(closeTimerRef.current);
    }
    closeTimerRef.current = window.setTimeout(() => {
      const s = useCalendarStore.getState();
      s.resetForm();
      s.closeEditor();
    }, 120);
  }, [closing, t]);

  const handleCancelDelete = useCallback(() => {
    useCalendarStore.setState({ deleteTarget: null });
  }, []);

  // P2-A#6：单事件 ICS 导出（浏览器直接下载 .ics 文件）
  const handleExportIcs = useCallback(async () => {
    const ev = useCalendarStore.getState().editing;
    if (!ev?.id || ev.id <= 0) return;
    try {
      const url = `/api/calendar/${ev.id}/export`;
      const a = document.createElement('a');
      a.href = url;
      a.download = `event-${ev.id}.ics`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      useCalendarStore.getState().showTransientToast(t('calendar.editor.icsDownloading'), 'success', 1800);
    } catch {
      useCalendarStore.getState().showTransientToast(t('calendar.common.exportIcsFailed'), 'error', 3000);
    }
  }, [t]);

  const handleTitleChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormField('title', e.target.value);
  }, [setFormField]);

  const handleDescriptionChange = useCallback((e: React.ChangeEvent<HTMLTextAreaElement>) => {
    setFormField('description', e.target.value);
  }, [setFormField]);

  const handleStartAtChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormField('startAt', e.target.value);
  }, [setFormField]);

  const handleEndAtChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormField('endAt', e.target.value);
    // 注意：截止时间 (dueAt) 的同步改在 onBlur 中触发，避免 onChange 时用户每改一个字符就覆盖 dueAt
  }, [setFormField]);

  const handleEndAtBlur = useCallback((e: React.FocusEvent<HTMLInputElement>) => {
    const newEndAt = e.target.value;
    const currentForm = useCalendarStore.getState().form;
    if (currentForm.eventType !== 'task') return;
    // 单独改过 dueAt → 不再自动覆盖
    if (dueAtDirtyRef.current) return;
    const newDueIso = newEndAt ? toIsoString(newEndAt) : undefined;
    setOptionalField('dueAt', newDueIso);
  }, [setOptionalField]);

  const handleDueAtChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    dueAtDirtyRef.current = true;
    setOptionalField('dueAt', e.target.value ? toIsoString(e.target.value) : undefined);
  }, [setOptionalField]);

  const handleEventTypeChange = useCallback((e: React.ChangeEvent<HTMLSelectElement>) => {
    const newType = e.target.value;
    const pad = (n: number) => String(n).padStart(2, '0');
    const cur = useCalendarStore.getState().form;
    const patch: Record<string, unknown> = { eventType: newType };

    // 先清除非本类型的专属字段（防止跨类型残留）
    if (newType !== 'meeting') {
      patch.meetingLink = undefined;
      patch.hostUserIds = undefined;
      patch.agendaIds = undefined;
    }
    if (newType !== 'task') {
      patch.taskOwner = undefined;
      patch.dueAt = undefined;
      patch.eventPriority = undefined;
      dueAtDirtyRef.current = false;
    }
    // 切换离开 reminder 类型时不清空扁平字段：reminderMinutesBefore/reminderType 为通用字段，
    // 所有事件类型均可有提醒（底部手风琴对所有类型可见）。reminderSettings 不再用于 UI。

    // === 类型选择：截止日期 (due_date) ===
    // 开始时间：保持不变；结束时间：清空
    // 颜色/重复/提醒/参会人：保持不变
    if (newType === 'due_date') {
      patch.endAt = '';
    }

    // === 类型选择：提醒 (reminder) ===
    // 开始时间：保持不变；结束时间：开始时间+1小时
    // 提醒方式：默认站内+邮件组合；提前（分钟）：10分钟（与 store.ts openNew 默认一致）
    // 颜色/重复/参会人：保持不变
    if (newType === 'reminder') {
      if (cur.startAt) {
        const startDt = new Date(cur.startAt.replace(' ', 'T'));
        if (!isNaN(startDt.getTime())) {
          const endDt = new Date(startDt);
          endDt.setHours(endDt.getHours() + 1);
          const isoEnd = `${endDt.getFullYear()}-${pad(endDt.getMonth() + 1)}-${pad(endDt.getDate())}T${pad(endDt.getHours())}:${pad(endDt.getMinutes())}`;
          patch.endAt = isoEnd;
        }
      }
      // 统一使用扁平字段（与底部手风琴一致）
      patch.reminderMinutesBefore = 10;
      patch.reminderType = 'popup,email';
    }

    // === 类型选择：会议 (meeting) ===
    // 开始时间：保持不变；结束时间：开始时间+1小时
    // 会议链接：空值；主持人ID：默认为登录用户（可多选）
    // 议程ID：空值；地点：空值
    // 颜色/重复/提醒/参会人：保持不变
    if (newType === 'meeting') {
      if (cur.startAt) {
        const startDt = new Date(cur.startAt.replace(' ', 'T'));
        if (!isNaN(startDt.getTime())) {
          const endDt = new Date(startDt);
          endDt.setHours(endDt.getHours() + 1);
          const isoEnd = `${endDt.getFullYear()}-${pad(endDt.getMonth() + 1)}-${pad(endDt.getDate())}T${pad(endDt.getHours())}:${pad(endDt.getMinutes())}`;
          patch.endAt = isoEnd;
        }
      }
      patch.meetingLink = '';
      patch.agendaIds = [];
      patch.location = '';
      // 主持人ID：默认为登录用户
      const currentUserId = (() => {
        try {
          const raw = localStorage.getItem('user');
          if (!raw) return undefined;
          const parsed = JSON.parse(raw) as { id?: number };
          return parsed.id;
        } catch {
          return undefined;
        }
      })();
      patch.hostUserIds = currentUserId != null ? [currentUserId] : [];
    }

    // === 类型选择：任务 (task) ===
    // 重新激活 task 专属默认值逻辑
    if (newType === 'task') {
      patch.eventPriority = patch.eventPriority ?? 'MEDIUM';
      // 切到 task 时，dueAtDirty 重置：若刚切过来 dueAt 为空或等于 endAt → 视为默认跟随
      const endIso = cur.endAt ? toIsoString(cur.endAt) : undefined;
      dueAtDirtyRef.current = !!(cur.dueAt && endIso && cur.dueAt !== endIso);
      // 截止时间跟随 endAt
      if (!dueAtDirtyRef.current && endIso) {
        patch.dueAt = endIso;
      }
      // taskOwner 默认为登录用户
      const currentUserId = (() => {
        try {
          const raw = localStorage.getItem('user');
          if (!raw) return undefined;
          const parsed = JSON.parse(raw) as { id?: number };
          return parsed.id;
        } catch {
          return undefined;
        }
      })();
      patch.taskOwner = currentUserId != null ? String(currentUserId) : undefined;
    }

    // === 类型选择：个人(personal)、出行(travel)、工作(work)、其他(other) ===
    // 开始时间：保持不变；结束时间：开始时间+1小时
    // 地点：空值
    // 颜色/重复/提醒/参会人：保持不变
    if (['personal', 'travel', 'work', 'other'].includes(newType)) {
      if (cur.startAt) {
        const startDt = new Date(cur.startAt.replace(' ', 'T'));
        if (!isNaN(startDt.getTime())) {
          const endDt = new Date(startDt);
          endDt.setHours(endDt.getHours() + 1);
          const isoEnd = `${endDt.getFullYear()}-${pad(endDt.getMonth() + 1)}-${pad(endDt.getDate())}T${pad(endDt.getHours())}:${pad(endDt.getMinutes())}`;
          patch.endAt = isoEnd;
        }
      }
      patch.location = '';
    }

    useCalendarStore.setState((s) => ({ form: { ...s.form, ...patch } }));
    setConflictToast(null);
  }, []);

  const handleStatusChange = useCallback((e: React.ChangeEvent<HTMLSelectElement>) => {
    setFormField('status', e.target.value);
  }, [setFormField]);

  const handleLocationChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormField('location', e.target.value);
  }, [setFormField]);

  const handleColorClick = useCallback((v: string) => {
    setFormField('color', v);
  }, [setFormField]);

  const handleColorGroupClick = useCallback((e: React.MouseEvent<HTMLDivElement>) => {
    const btn = (e.target as HTMLElement).closest('button[data-color]');
    if (!btn) return;
    const color = (btn as HTMLButtonElement).dataset.color;
    if (color) handleColorClick(color);
  }, [handleColorClick]);

  const handleColorInputChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormField('color', e.target.value);
  }, [setFormField]);

  const handleRecurrenceTypeChange = useCallback((e: React.ChangeEvent<HTMLSelectElement>) => {
    const typeNum = Number(e.target.value);
    const patch: Record<string, unknown> = { recurrenceType: typeNum };
    // 自动联动 isRecurring：选了频率(>=0) 就是重复事件，选了无(-1)就不是
    patch.isRecurring = typeNum >= 0;
    // 若类型切到「无」，顺带清掉重复相关的扩展字段
    if (typeNum < 0) {
      patch.recurrenceInterval = 1;
      patch.weekdaysSelected = [];
      patch.recurrenceCount = null;
      patch.recurrenceEndDate = '';
      patch.recurrenceRule = '';
    }
    useCalendarStore.setState((s) => ({ form: { ...s.form, ...patch } }));
  }, []);

  const handleIntervalChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormField('recurrenceInterval', Math.max(1, Number(e.target.value) || 1));
  }, [setFormField]);

  const handleWeekdayClick = useCallback((idx: number) => {
    const state = useCalendarStore.getState();
    const current = state.form.weekdaysSelected ?? [];
    const next = current.includes(idx)
      ? current.filter((x) => x !== idx)
      : [...current, idx];
    setFormField('weekdaysSelected', next);
  }, [setFormField]);

  const handleWeekdayGroupClick = useCallback((e: React.MouseEvent<HTMLDivElement>) => {
    const btn = (e.target as HTMLElement).closest('button[data-weekday-key]');
    if (!btn) return;
    const key = (btn as HTMLButtonElement).dataset.weekdayKey;
    if (key !== undefined) handleWeekdayClick(parseInt(key, 10));
  }, [handleWeekdayClick]);

  const handleRecurrenceCountChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormField('recurrenceCount', Math.max(1, Number(e.target.value) || 1));
  }, [setFormField]);

  const handleRecurrenceEndDateChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormField('recurrenceEndDate', e.target.value);
  }, [setFormField]);

  const handleReminderMinutesChange = useCallback((e: React.ChangeEvent<HTMLSelectElement>) => {
    const v = e.target.value;
    if (v === '') {
      // 不提醒：关闭自定义模式，清空分钟数
      setFormField('isCustomReminder', false);
      setFormField('reminderMinutesBefore', null);
    } else if (v === 'custom') {
      // T000097: 切换到自定义模式，必须设置一个不在预设列表中的值，
      // 否则下拉框会回退显示预设项（如"30分钟前"），看起来"无响应"。
      const current = useCalendarStore.getState().form.reminderMinutesBefore;
      // 若当前值已是自定义值（不在预设列表）则保留；否则用 7（非预设值）
      const isCurrentCustom =
        current != null && !REMINDER_OPTIONS.some((o) => o.value === current);
      setFormField('isCustomReminder', true);
      setFormField('reminderMinutesBefore', isCurrentCustom ? current : 7);
    } else {
      // 预设值：关闭自定义模式
      setFormField('isCustomReminder', false);
      setFormField('reminderMinutesBefore', Number(v));
    }
  }, [setFormField]);

  // T000090：提醒方式支持多选组合（站内 popup / 邮件 email / 短信 sms，可同时勾选）。
  // 存储为逗号分隔串（"popup,email"）；全部取消 → null（默认：站内 + 邮件，后端兼容）。
  const handleReminderTypeToggle = useCallback((t: 'popup' | 'email' | 'sms') => {
    const current = parseReminderTypes(useCalendarStore.getState().form.reminderType);
    const next = current.includes(t)
      ? current.filter((x) => x !== t)
      : [...current, t];
    setFormField('reminderType', serializeReminderTypes(next));
  }, [setFormField]);

  const addAttendeeEmail = useCallback((raw: string) => {
    const val = raw.trim();
    if (!val) return;
    // 支持一次性粘贴多个邮箱（逗号/分号/空格/回车分隔）
    // 邮箱正则：常见本地名 + 域名，要求域名段含至少一个点
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    const tokens = val
      .split(/[,;\s]+/)
      .map((s) => s.trim())
      .filter((s) => s.length > 0);
    const valid: string[] = [];
    const invalid: string[] = [];
    for (const t of tokens) {
      if (emailRegex.test(t)) {
        if (!valid.includes(t)) valid.push(t);
      } else {
        if (!invalid.includes(t)) invalid.push(t);
      }
    }
    if (valid.length === 0 && invalid.length > 0) {
      // 全部无法识别 — 给出提示，不进 chip 列表
      setAttendeeInvalidHint(
        invalid.length === 1
          ? t('calendar.editor.invalidEmailSingle', { email: invalid[0] })
          : t('calendar.editor.invalidEmailsMany', {
              count: invalid.length,
              list: invalid.join(t('calendar.editor.listSeparator')),
            })
      );
      return;
    }
    setAttendeeEmailList((prev) => {
      const merged = [...prev];
      for (const e of valid) if (!merged.includes(e)) merged.push(e);
      // 同步到 store（跨面板重建/重新打开时保留）
      useCalendarStore.setState({ _attendeeEmailList: merged });
      return merged;
    });
    if (invalid.length > 0) {
      // 部分识别成功 — 提示无效项
      setAttendeeInvalidHint(
        t('calendar.editor.invalidEmailsIgnored', {
          count: invalid.length,
          list: invalid.join(t('calendar.editor.listSeparator')),
        })
      );
    } else {
      setAttendeeInvalidHint(null);
    }
  }, [t]);

  // r5: 批量清空参会人邮箱
  const clearAllAttendees = useCallback(() => {
    setAttendeeEmailList([]);
    useCalendarStore.setState({ _attendeeEmailList: [] });
    setAttendeeInvalidHint(null);
    // 清空后主动聚焦输入框，便于重新录入
    requestAnimationFrame(() => {
      attendeeInputRef.current?.focus();
    });
  }, []);

  const removeAttendeeEmail = useCallback((email: string) => {
    setAttendeeEmailList((prev) => {
      const next = prev.filter((x) => x !== email);
      useCalendarStore.setState({ _attendeeEmailList: next });
      return next;
    });
  }, []);

  const handleAttendeeKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        const val = (e.currentTarget.value || '').trim();
        if (val) {
          addAttendeeEmail(val);
          setAttendeeInput('');
          // r5: 清空输入后主动恢复焦点（React 批处理+重渲染后 input 可能短暂失焦）
          requestAnimationFrame(() => {
            attendeeInputRef.current?.focus();
          });
        }
      } else if (e.key === 'Backspace' && attendeeInput === '' && attendeeEmailList.length > 0) {
        // 回退到空时按退格，删除最后一个 chip
        setAttendeeEmailList((prev) => {
          const next = prev.slice(0, -1);
          useCalendarStore.setState({ _attendeeEmailList: next });
          return next;
        });
      }
    },
    [addAttendeeEmail, attendeeInput, attendeeEmailList.length]
  );

  const handleStopPropagation = useCallback((e: React.MouseEvent) => {
    e.stopPropagation();
  }, []);

  // P2-A#2: 打开"保存为模板"模态框（自动预填模板名称为标题）
  const openSaveTemplateModal = useCallback(() => {
    setTemplateNameInput(form.title.trim() || '');
    setShowSaveTemplateModal(true);
  }, [form.title]);

  // P2-A#2: 关闭"保存为模板"模态框
  const closeSaveTemplateModal = useCallback(() => {
    setShowSaveTemplateModal(false);
    setTemplateNameInput('');
    setSaveTemplateLoading(false);
  }, []);

  // P2-A#2: 确认保存为模板
  const handleConfirmSaveTemplate = useCallback(async () => {
    if (!templateNameInput.trim() || !form.title.trim()) return;
    setSaveTemplateLoading(true);
    try {
      const created = await saveTemplate(templateNameInput.trim());
      if (created) {
        showTransientToast(t('calendar.editor.templateSaved', { name: created.name }), 'success', 2500);
        closeSaveTemplateModal();
      } else {
        showTransientToast(t('calendar.editor.saveTemplateFailed'), 'error', 3000);
      }
    } catch {
      showTransientToast(t('calendar.editor.saveTemplateFailed'), 'error', 3000);
    } finally {
      setSaveTemplateLoading(false);
    }
  }, [templateNameInput, form.title, saveTemplate, showTransientToast, closeSaveTemplateModal, t]);

  // P2-A#2: 删除模板
  const handleDeleteTemplate = useCallback(async (tplId: number) => {
    if (!tplId || tplId <= 0) return;
    setManageDeletingId(tplId);
    try {
      const ok = await deleteTemplate(tplId);
      if (ok) {
        showTransientToast(t('calendar.editor.templateDeleted'), 'success', 2000);
      } else {
        showTransientToast(t('calendar.editor.deleteTemplateFailed'), 'error', 3000);
      }
    } catch {
      showTransientToast(t('calendar.editor.deleteTemplateFailed'), 'error', 3000);
    } finally {
      setManageDeletingId(null);
    }
  }, [deleteTemplate, showTransientToast, t]);

  /** 共享关闭逻辑：动画 → resetForm → closeEditor */
  const handleCloseEditor = useCallback(() => {
    if (closing) return;
    setClosing(true);
    if (toastTimerRef.current !== null) { clearTimeout(toastTimerRef.current); toastTimerRef.current = null; }
    if (closeTimerRef.current !== null) { clearTimeout(closeTimerRef.current); }
    setConflictToast(null);
    closeTimerRef.current = window.setTimeout(() => {
      useCalendarStore.setState({ pendingSignalRBump: true });
      const s = useCalendarStore.getState();
      s.resetForm();
      s.closeEditor();
    }, 120);
  }, [closing]);

  useEffect(() => {
    if (!showEditor) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return;
      handleCloseEditor();
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [showEditor, handleCloseEditor]);

  /** 编辑时的**实时**冲突检测：只要 startAt/endAt/allEvents 变动就重算，
   *  在时间字段修改的瞬间即可反馈，无需等到保存后才看到提示。
   *  注意：必须放在 early return 之前，遵守 React Hooks 规则。 */
  const liveConflict: ConflictCheckResult = useMemo(() => {
    if (!showEditor || form.isAllDay || !form.startAt) {
      return { tag: 'none', conflictIds: [], overlapMinutes: 0, conflictWith: [] };
    }
    return _checkConflictForRange({
      startAt: form.startAt,
      endAt: form.endAt ?? undefined,
      existingEvents: useCalendarStore.getState().allEvents,
      excludeId: editing?.id,
    });
  }, [showEditor, form.isAllDay, form.startAt, form.endAt, editing?.id]);

  if (!showEditor) return null;

  const startInputType = form.isAllDay ? 'date' : 'datetime-local';
  const endInputType = form.isAllDay ? 'date' : 'datetime-local';

  const eventTypeKeys = Object.keys(EVENT_TYPE_STYLE);
  // 状态下拉只展示后端支持的三个状态（pending/completed/cancelled），
  // 不能直接用 Object.keys(STATUS_STYLE)，因为后者包含 in_progress/missed 等后端不接受的值。
  const statusOptions = STATUS_OPTIONS;
  // 精选 17 个色球（覆盖全部色系 + 拓展色）+ 自定义颜色 = 18 个
  const presetColors = COLOR_OPTIONS.slice(0, 17);

  const selectedEventTypeStyle = EVENT_TYPE_STYLE[form.eventType] ?? EVENT_TYPE_STYLE['task'];
  const selectedStatusStyle = STATUS_STYLE[form.status] ?? STATUS_STYLE['pending'];

  return (
    <div
      className={`fixed inset-0 z-50 bg-gradient-to-br from-slate-900/60 to-slate-800/50 backdrop-blur-md flex items-center justify-center p-4 transition-opacity duration-[120ms] ${closing ? 'opacity-0' : ''}`}
      onClick={handleOverlayClick}
      style={{ animation: 'cal-fade-in-up 180ms cubic-bezier(0.2,0,0,1) forwards' }}
    >
      <div
        className={`relative bg-white dark:bg-slate-900 rounded-2xl shadow-2xl shadow-slate-900/25 ring-1 ring-slate-200/60 dark:ring-slate-700/50 w-full max-w-2xl max-h-[92vh] overflow-y-auto ${closing ? 'animate-[cal-pop-out_120ms_ease-in_forwards]' : 'animate-[cal-pop-in_240ms_ease-out_both]'}`}
        onClick={handleStopPropagation}
        style={{ willChange: 'transform, opacity' }}
      >
        {conflictToast && (
          <div
            className={[
              'absolute top-3 right-3 max-w-sm rounded-lg shadow-lg p-3 pl-3.5 border-l-[3px] text-sm z-20',
              'animate-[cal-fade-in-up_180ms_cubic-bezier(0.2,0,0,1)_forwards]',
              conflictToast.tag === 'hard'
                ? 'bg-rose-50 dark:bg-rose-950/40 border-l-rose-500 text-rose-700 dark:text-rose-300'
                : 'bg-amber-50 dark:bg-amber-950/40 border-l-amber-400 text-amber-700 dark:text-amber-300',
            ].join(' ')}
          >
            <div className="flex items-start gap-2">
              <SharedIcon name={conflictToast.tag === 'hard' ? 'alertCircle' : 'bell'} className="w-4 h-4 mt-0.5 shrink-0" />
              <div className="flex-1">
                {t('calendar.editor.savedConflictNotice', {
                  count: conflictToast.count,
                  tag: conflictToast.tag === 'hard'
                    ? t('calendar.editor.conflictHard')
                    : t('calendar.editor.conflictSoft'),
                })}
              </div>
              <button
                type="button"
                onClick={() => {
                  if (toastTimerRef.current !== null) {
                    clearTimeout(toastTimerRef.current);
                    toastTimerRef.current = null;
                  }
                  if (closeTimerRef.current !== null) {
                    clearTimeout(closeTimerRef.current);
                    closeTimerRef.current = null;
                  }
                  setConflictToast(null);
                }}
                className="text-current opacity-60 hover:opacity-100"
                aria-label={t('calendar.common.closeHint')}
              >
                <X className="w-4 h-4" />
              </button>
            </div>
          </div>
        )}
        <ModalHeader
          title={editing ? t('calendar.editor.headerEdit') : t('calendar.editor.headerCreate')}
          subtitle={editing ? t('calendar.editor.subtitleEdit') : t('calendar.editor.subtitleCreate')}
          onClose={handleCloseEditor}
          icon={<CalendarIcon className="w-5 h-5" />}
          theme="editor"
          accentColor={form.color ?? '#0ea5e9'}
          rightActions={editing ? (
            <div className="flex items-center gap-1">
              <button
                type="button"
                onClick={handleExportIcs}
                className="p-2 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700/70 hover:text-slate-800 dark:hover:text-slate-100 rounded-lg transition-all"
                title={t('calendar.editor.exportIcsTitle')}
              >
                <DownloadIcon className="w-5 h-5" />
              </button>
              <button
                type="button"
                onClick={handleDelete}
                className="p-2 text-red-500 hover:bg-red-50 dark:hover:bg-red-900/30 hover:text-red-600 dark:hover:text-red-400 rounded-lg transition-all"
                title={t('common.delete')}
              >
                <Trash2 className="w-5 h-5" />
              </button>
            </div>
          ) : undefined}
        />

        <div className="px-5 py-4 space-y-4">
          {formError && (
            <div className="flex items-start gap-2 px-3 py-2.5 text-sm text-red-700 dark:text-red-300 bg-red-50 dark:bg-red-900/30 rounded-lg border border-red-200 dark:border-red-800 animate-[cal-fade-in-up_180ms_ease-out_both]">
              <span className="mt-0.5 shrink-0">⚠️</span>
              <span>{formError}</span>
            </div>
          )}

          {/* 实时冲突提示：编辑 startAt/endAt 时即时反馈（未保存前就能看到） */}
          {liveConflict.tag !== 'none' && (
            <div
              className={[
                'flex items-start gap-2.5 px-3.5 py-3 rounded-lg border text-sm animate-[cal-fade-in-up_180ms_ease-out_both]',
                liveConflict.tag === 'hard'
                  ? 'bg-rose-50/90 dark:bg-rose-950/30 border-rose-200 dark:border-rose-900/60 text-rose-800 dark:text-rose-200'
                  : 'bg-amber-50/90 dark:bg-amber-950/30 border-amber-200 dark:border-amber-900/60 text-amber-800 dark:text-amber-200',
              ].join(' ')}
            >
              <div className="shrink-0 mt-0.5">
                {liveConflict.tag === 'hard' ? (
                  <AlertCircle className="w-5 h-5 text-rose-500 dark:text-rose-400" />
                ) : (
                  <AlertTriangle className="w-5 h-5 text-amber-500 dark:text-amber-400" />
                )}
              </div>
              <div className="flex-1 min-w-0">
                <div className="font-semibold leading-5">
                  {liveConflict.tag === 'hard' ? t('calendar.editor.liveConflictHard') : t('calendar.editor.liveConflictSoft')}
                  <span className="ml-2 inline-flex items-center px-2 py-0.5 rounded-full text-[11px] font-bold bg-white/70 dark:bg-slate-900/50 border border-current/20">
                    {t('calendar.editor.overlapLabel')}{' '}
                    {liveConflict.overlapMinutes < 1
                      ? t('calendar.editor.overlapSeconds', { count: Math.round(liveConflict.overlapMinutes * 60) })
                      : t('calendar.editor.overlapMinutes', { count: liveConflict.overlapMinutes.toFixed(liveConflict.overlapMinutes % 1 === 0 ? 0 : 1) })}
                  </span>
                </div>
                <div className="mt-1 opacity-90 leading-5">
                  {t('calendar.editor.liveConflictPrefix')}{' '}
                  <b className="font-semibold">{liveConflict.conflictIds.length}</b>{' '}
                  {t('calendar.editor.liveConflictMiddle')}
                  {liveConflict.tag === 'hard'
                    ? t('calendar.editor.liveConflictHardHint')
                    : t('calendar.editor.liveConflictSoftHint')}
                </div>
                {liveConflict.conflictWith.length > 0 && (
                  <ul className="mt-1.5 space-y-1 text-xs opacity-90">
                    {liveConflict.conflictWith.slice(0, 5).map((w, idx) => {
                      const sHm = new Date(w.startAt).toTimeString().slice(0, 5);
                      const eHm = w.endAt ? new Date(w.endAt).toTimeString().slice(0, 5) : '';
                      return (
                        <li key={idx} className="flex items-start gap-1.5">
                          <span className="w-1 h-1 mt-1.5 rounded-full bg-current shrink-0 opacity-70" />
                          <span className="truncate">
                            <span className="font-medium">{w.title || t('calendar.common.untitled')}</span>
                            <span className="opacity-70"> · {sHm}{eHm ? `–${eHm}` : ''}</span>
                          </span>
                        </li>
                      );
                    })}
                    {liveConflict.conflictWith.length > 5 && (
                      <li className="opacity-70">{t('calendar.editor.moreItems', { count: liveConflict.conflictWith.length - 5 })}</li>
                    )}
                  </ul>
                )}
              </div>
            </div>
          )}

          {/* P1-3: 事件模板选择器（仅新建时显示） */}
          {!editing && (
        <div>
          <label className="flex items-center gap-1.5 text-sm font-medium text-slate-600 dark:text-slate-300 mb-1.5">
            <FileTextIcon className="w-3.5 h-3.5 text-slate-400" />
            {t('calendar.editor.templateLabel')}
          </label>
          {eventTemplates.length > 0 && (
            <div className="relative mb-2">
              <select
                value=""
                onChange={(e) => {
                  const tplId = Number(e.target.value);
                  if (!Number.isNaN(tplId) && tplId > 0) {
                    // 以表单当前 startAt（若有效）为基础，否则取当天
                    let base: Date | undefined;
                    if (form.startAt) {
                      const parsed = new Date(form.startAt.replace(' ', 'T'));
                      if (!Number.isNaN(parsed.getTime())) base = parsed;
                    }
                    applyTemplate(tplId, base);
                  }
                }}
                className="w-full px-3.5 py-2.5 text-sm rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50/50 dark:bg-slate-800/50 text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 focus:bg-white dark:focus:bg-slate-800 hover:border-slate-300 dark:hover:border-slate-600 transition-all appearance-none pr-10"
              >
                <option value="">{t('calendar.editor.templateQuickCreate')}</option>
                {eventTemplates.map((tpl) => (
                  <option key={tpl.id} value={tpl.id}>
                    {tpl.name} — {tpl.title}
                    {t('calendar.editor.templateMeta', {
                      duration: tpl.isAllDay
                        ? t('calendar.common.allDay')
                        : t('calendar.editor.templateDuration', { minutes: tpl.durationMin }),
                      type: EVENT_TYPE_STYLE[tpl.eventType]?.label ?? tpl.eventType,
                    })}
                  </option>
                ))}
              </select>
              <ChevronDown className="w-4 h-4 absolute right-3 top-1/2 -translate-y-1/2 pointer-events-none text-slate-400" />
            </div>
          )}
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={openSaveTemplateModal}
              disabled={!form.title || !form.title.trim()}
              className="flex-1 px-3 py-2 text-xs font-medium rounded-xl border border-sky-200 dark:border-sky-800 text-sky-700 dark:text-sky-300 bg-sky-50/60 dark:bg-sky-950/30 hover:bg-sky-100 dark:hover:bg-sky-900/40 disabled:opacity-40 disabled:cursor-not-allowed transition-all inline-flex items-center justify-center gap-1.5"
              title={t('calendar.editor.saveAsTemplateTitle')}
            >
              <SaveIcon className="w-3.5 h-3.5" />
              {t('calendar.editor.saveAsTemplate')}
            </button>
            {eventTemplates.length > 0 && (
              <button
                type="button"
                onClick={() => setShowManageTemplatesModal(true)}
                className="px-3 py-2 text-xs font-medium rounded-xl border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 bg-white dark:bg-slate-800/50 hover:border-slate-300 dark:hover:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-800 transition-all inline-flex items-center justify-center gap-1.5"
                title={t('calendar.editor.manageTemplatesTitle')}
              >
                <Settings2Icon className="w-3.5 h-3.5" />
                {t('calendar.editor.manage')}
              </button>
            )}
          </div>
        </div>
      )}

          {/* 标题 */}
          <div>
            <label className="flex items-center gap-1.5 text-sm font-medium text-slate-600 dark:text-slate-300 mb-1.5">
              <Type className="w-3.5 h-3.5 text-slate-400" />
              {t('calendar.common.title')} <span className="text-red-500 animate-pulse">*</span>
            </label>
            <input
              type="text"
              value={form.title}
              onChange={handleTitleChange}
              placeholder={t('calendar.editor.titlePlaceholder')}
              className="w-full px-3.5 py-2.5 text-sm rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50/50 dark:bg-slate-800/50 text-gray-900 dark:text-slate-100 placeholder:text-slate-400 dark:placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 focus:bg-white dark:focus:bg-slate-800 hover:border-slate-300 dark:hover:border-slate-600 transition-all"
            />
          </div>

          {/* 描述 */}
          <div>
            <label className="flex items-center gap-1.5 text-sm font-medium text-slate-600 dark:text-slate-300 mb-1.5">
              <AlignLeft className="w-3.5 h-3.5 text-slate-400" />
              {t('calendar.editor.descriptionLabel')}
            </label>
            <textarea
              value={form.description}
              onChange={handleDescriptionChange}
              rows={3}
              placeholder={t('calendar.editor.descriptionPlaceholder')}
              className="w-full px-3.5 py-2.5 text-sm rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50/50 dark:bg-slate-800/50 text-gray-900 dark:text-slate-100 placeholder:text-slate-400 dark:placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 focus:bg-white dark:focus:bg-slate-800 hover:border-slate-300 dark:hover:border-slate-600 transition-all resize-none"
            />
          </div>

          {/* 全天 + 时间 —— 卡片化 */}
          <div className="rounded-xl border border-slate-200 dark:border-slate-700/60 bg-slate-50/40 dark:bg-slate-800/20 p-3.5 space-y-3">
            <label className="flex items-center gap-2.5 cursor-pointer select-none w-fit group">
              <span className="relative inline-flex">
                <input
                  type="checkbox"
                  id="editor-isAllDay"
                  checked={form.isAllDay}
                  onChange={handleAllDayToggle}
                  className="peer sr-only"
                />
                <span className="w-9 h-5 rounded-full bg-slate-300 dark:bg-slate-600 peer-checked:bg-sky-500 transition-colors duration-200" />
                <span className="absolute left-0.5 top-0.5 w-4 h-4 rounded-full bg-white shadow-sm transition-transform duration-200 peer-checked:translate-x-4" />
              </span>
              <span className="flex items-center gap-1.5 text-sm font-medium text-slate-600 dark:text-slate-300 group-hover:text-slate-800 dark:group-hover:text-slate-100 transition-colors">
                <Clock className="w-3.5 h-3.5 text-slate-400" />
                {t('calendar.common.allDayEvent')}
              </span>
            </label>

            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">
                  {t('calendar.editor.startTime')}
                </label>
                <input
                  type={startInputType}
                  value={form.startAt}
                  onChange={handleStartAtChange}
                  className="w-full px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/70 text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all"
                />
              </div>
              <div>
                <label className="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">
                  {t('calendar.editor.endTime')}
                </label>
                <input
                  type={endInputType}
                  value={form.endAt}
                  onChange={handleEndAtChange}
                  onBlur={handleEndAtBlur}
                  className="w-full px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/70 text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all"
                />
              </div>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="flex items-center gap-1.5 text-sm font-medium text-slate-600 dark:text-slate-300 mb-1.5">
                <Tag className="w-3.5 h-3.5 text-slate-400" />
                {t('calendar.common.type')}
              </label>
              <select
                value={form.eventType}
                onChange={handleEventTypeChange}
                className="w-full px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/50 text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all cursor-pointer"
              >
                {eventTypeKeys.map((k) => (
                  <option key={k} value={k}>
                    {EVENT_TYPE_STYLE[k]?.label ?? k}
                  </option>
                ))}
              </select>
              <div className="mt-1.5">
                <span
                  className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md text-xs ${selectedEventTypeStyle.badgeClass}`}
                >
                  <span
                    className={`w-1.5 h-1.5 rounded-full ${selectedEventTypeStyle.dotClass}`}
                  />
                  {selectedEventTypeStyle.label}
                </span>
              </div>
            </div>
            <div>
              <label className="flex items-center gap-1.5 text-sm font-medium text-slate-600 dark:text-slate-300 mb-1.5">
                <Sparkles className="w-3.5 h-3.5 text-slate-400" />
                {t('calendar.common.status')}
              </label>
              <select
                value={form.status}
                onChange={handleStatusChange}
                className="w-full px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/50 text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all cursor-pointer"
              >
                {statusOptions.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
              <div className="mt-1.5">
                <span
                  className={`inline-flex items-center px-2 py-0.5 rounded-md text-xs ${selectedStatusStyle.badgeClass}`}
                >
                  {selectedStatusStyle.label}
                </span>
              </div>
            </div>
          </div>

          {/* Task G: eventType 专属字段面板（按类型切换） */}
          <div
            key={form.eventType}
            className="mt-4 pt-4 border-t border-gray-200/60 dark:border-slate-700/60"
            style={{ animation: 'cal-fade-in-up 220ms ease-out both' }}
          >
            {form.eventType === 'meeting' && (
              <div className="space-y-3">
                <div>
                  <label className="block text-sm font-medium text-slate-600 dark:text-slate-300 mb-1">
                    {t('calendar.editor.meetingLink')}
                  </label>
                  <input
                    type="url"
                    placeholder="https://meet.example.com/xxx"
                    value={form.meetingLink ?? ''}
                    onChange={(e) => setOptionalField('meetingLink', e.target.value || undefined)}
                    className="w-full px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/50 text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all font-sans"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-slate-600 dark:text-slate-300 mb-1">
                    {t('calendar.editor.hosts')}
                  </label>
                  <div className="relative" ref={hostDropdownRef}>
                    {/* 触发器：显示已选用户名 chips 或占位符（用 div+role=button 避免与 chip 内 button 嵌套） */}
                    <div
                      role="button"
                      tabIndex={0}
                      onClick={() => { if (!assignableLoading) setHostDropdownOpen((v) => !v); }}
                      onKeyDown={(e) => {
                        if (assignableLoading) return;
                        if (e.key === 'Enter' || e.key === ' ') {
                          e.preventDefault();
                          setHostDropdownOpen((v) => !v);
                        }
                      }}
                      aria-expanded={hostDropdownOpen}
                      aria-haspopup="listbox"
                      aria-disabled={assignableLoading}
                      className="w-full min-h-[42px] px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/50 text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all cursor-pointer flex items-center justify-between gap-2 data-[disabled=true]:opacity-60 data-[disabled=true]:cursor-not-allowed"
                      data-disabled={assignableLoading ? 'true' : 'false'}
                    >
                      <span className="flex flex-wrap items-center gap-1.5 flex-1 min-w-0">
                        {assignableLoading ? (
                          <span className="text-slate-400 dark:text-slate-500">{t('calendar.editor.loadingDots')}</span>
                        ) : (form.hostUserIds ?? []).length === 0 ? (
                          <span className="text-slate-400 dark:text-slate-500">{t('calendar.editor.selectHostsPlaceholder')}</span>
                        ) : (
                          (form.hostUserIds ?? []).map((uid) => {
                            const u = assignableUsers.find((x) => x.id === uid);
                            const name = u ? getHostUserDisplayName(u) : t('calendar.editor.userFallback', { id: uid });
                            return (
                              <span
                                key={uid}
                                className="inline-flex items-center gap-1 pl-2 pr-1 py-0.5 rounded-full text-xs bg-sky-50 dark:bg-sky-900/30 text-sky-700 dark:text-sky-300 border border-sky-200/60 dark:border-sky-700/40"
                              >
                                <span className="max-w-[140px] truncate font-medium">{name}</span>
                                <button
                                  type="button"
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    toggleHost(uid);
                                  }}
                                  className="w-4 h-4 rounded-full flex items-center justify-center text-sky-600 dark:text-sky-300 hover:bg-sky-200/80 dark:hover:bg-sky-800/70 transition-colors cursor-pointer"
                                  aria-label={t('calendar.editor.removeHost', { name })}
                                >
                                  <X className="w-2.5 h-2.5" strokeWidth={3} />
                                </button>
                              </span>
                            );
                          })
                        )}
                      </span>
                      <ChevronDown className={`w-4 h-4 text-slate-400 shrink-0 transition-transform duration-200 ${hostDropdownOpen ? 'rotate-180' : ''}`} />
                    </div>
                    {/* 下拉面板：用户姓名列表 + 复选框 */}
                    {hostDropdownOpen && (
                      <div
                        role="listbox"
                        aria-multiselectable="true"
                        className="absolute z-50 left-0 right-0 mt-1 max-h-56 overflow-y-auto rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 shadow-lg shadow-slate-500/10 dark:shadow-black/30"
                      >
                        {assignableUsers.length === 0 ? (
                          <div className="px-3 py-2.5 text-sm text-slate-400 dark:text-slate-500">
                            {t('calendar.editor.noAssignableUsers')}
                          </div>
                        ) : (
                          assignableUsers.map((u) => {
                            const checked = (form.hostUserIds ?? []).includes(u.id);
                            const name = getHostUserDisplayName(u);
                            return (
                              <button
                                key={u.id}
                                type="button"
                                role="option"
                                aria-selected={checked}
                                onClick={() => toggleHost(u.id)}
                                className={`w-full flex items-center gap-2.5 px-3 py-2 text-sm text-left transition-colors ${
                                  checked
                                    ? 'bg-sky-50 dark:bg-sky-900/30 text-sky-700 dark:text-sky-300'
                                    : 'text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700/50'
                                }`}
                              >
                                <span className={`flex items-center justify-center w-4 h-4 rounded border transition-all shrink-0 ${
                                  checked
                                    ? 'bg-sky-500 border-sky-500 text-white'
                                    : 'border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-900'
                                }`}>
                                  {checked && <Check className="w-3 h-3" strokeWidth={3} />}
                                </span>
                                <span className="flex-1 truncate font-medium">{name}</span>
                                {u.email && (
                                  <span className="text-xs text-slate-400 dark:text-slate-500 truncate max-w-[140px]">
                                    {u.email}
                                  </span>
                                )}
                              </button>
                            );
                          })
                        )}
                      </div>
                    )}
                  </div>
                </div>
                <div>
                  <label className="block text-sm font-medium text-slate-600 dark:text-slate-300 mb-1">
                    {t('calendar.editor.agendaIdsLabel')}
                  </label>
                  <input
                    type="text"
                    placeholder={t('calendar.editor.agendaIdsPlaceholder')}
                    value={(form.agendaIds ?? []).join(', ')}
                    onChange={(e) => {
                      const arr = e.target.value
                        .split(/[,，]/)
                        .map((s) => parseInt(s.trim(), 10))
                        .filter((n) => Number.isFinite(n));
                      setFormField('agendaIds', arr.length ? arr : undefined);
                    }}
                    className="w-full px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/50 text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all font-mono text-sm"
                  />
                </div>
              </div>
            )}

            {form.eventType === 'task' && (
              <div className="space-y-3">
                <div>
                  <label className="block text-sm font-medium text-slate-600 dark:text-slate-300 mb-1">
                    {t('calendar.common.assignee')}
                  </label>
                  <div className="relative">
                    <select
                      value={form.taskOwner ?? ''}
                      onChange={(e) => setOptionalField('taskOwner', e.target.value || undefined)}
                      disabled={assignableLoading}
                      className="w-full px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/50 text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all cursor-pointer appearance-none pr-9 disabled:opacity-60 disabled:cursor-not-allowed"
                    >
                      <option value="">{assignableLoading ? t('calendar.editor.loadingDots') : t('calendar.editor.selectAssigneePlaceholder')}</option>
                      {assignableUsers.map((u) => (
                        <option key={u.id} value={String(u.id)}>
                          {(u.fullName || `${u.firstName} ${u.lastName}`.trim()) + (u.email ? ` · ${u.email}` : '')}
                        </option>
                      ))}
                    </select>
                    <ChevronDown className="w-4 h-4 absolute right-3 top-1/2 -translate-y-1/2 pointer-events-none text-slate-400" />
                  </div>
                </div>
                <div>
                  <label className="block text-sm font-medium text-slate-600 dark:text-slate-300 mb-1">
                    {t('calendar.editor.dueAt')}
                  </label>
                  <input
                    type="datetime-local"
                    value={form.dueAt ? fromIsoToLocalInput(form.dueAt) : ''}
                    onChange={handleDueAtChange}
                    className="w-full px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/50 text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all font-sans"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-slate-600 dark:text-slate-300 mb-1">
                    {t('calendar.common.progress')}
                  </label>
                  <div className="flex items-center gap-3">
                    <input
                      type="range"
                      min={0}
                      max={100}
                      step={5}
                      value={form.progress ?? 0}
                      onChange={(e) => setFormField('progress', Number(e.target.value))}
                      className="flex-1 h-2 bg-slate-200 dark:bg-slate-700 rounded-lg appearance-none cursor-pointer accent-sky-500"
                      style={{ accentColor: form.color ?? '#0ea5e9' }}
                    />
                    <input
                      type="number"
                      min={0}
                      max={100}
                      step={1}
                      value={form.progress ?? 0}
                      onChange={(e) => {
                        const n = parseInt(e.target.value, 10);
                        const v = Number.isFinite(n) ? Math.max(0, Math.min(100, n)) : 0;
                        setFormField('progress', v);
                      }}
                      className="w-16 px-2 py-1.5 text-sm text-right font-mono rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/50 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 transition-all"
                    />
                    <span className="text-xs text-slate-400">%</span>
                  </div>
                </div>
                <div>
                  <label className="block text-sm font-medium text-slate-600 dark:text-slate-300 mb-1">
                    {t('calendar.common.priority')}
                  </label>
                  <div className="grid grid-cols-4 gap-2" role="radiogroup" aria-label={t('calendar.common.priority')}>
                    {PRIORITY_OPTIONS.map((opt) => {
                      const active = (form.eventPriority ?? 'MEDIUM') === opt.v;
                      return (
                        <button
                          key={opt.v}
                          type="button"
                          role="radio"
                          aria-checked={active}
                          tabIndex={active ? 0 : -1}
                          onClick={() => setFormField('eventPriority', opt.v)}
                          className={`px-3 py-1.5 text-xs font-medium rounded-lg border transition-all ${
                            active
                              ? opt.v === 'LOW'
                                ? 'bg-slate-100 dark:bg-slate-700 text-slate-700 dark:text-slate-200 border-slate-400 dark:border-slate-500'
                                : opt.v === 'MEDIUM'
                                  ? 'bg-blue-50 dark:bg-blue-900/40 text-blue-700 dark:text-blue-300 border-blue-400 dark:border-blue-600'
                                  : opt.v === 'HIGH'
                                    ? 'bg-orange-50 dark:bg-orange-900/40 text-orange-700 dark:text-orange-300 border-orange-400 dark:border-orange-600'
                                    : 'bg-red-50 dark:bg-red-900/40 text-red-700 dark:text-red-300 border-red-400 dark:border-red-600'
                              : 'bg-white dark:bg-slate-800/50 text-gray-500 dark:text-slate-400 border-gray-200 dark:border-slate-700 hover:border-gray-300 dark:hover:border-slate-600'
                          }`}
                        >
                          {t(PRIORITY_LABEL_KEYS[opt.v] ?? 'calendar.editor.priorityMedium')}
                        </button>
                      );
                    })}
                  </div>
                </div>
              </div>
            )}


          </div>

          <div>
            <label className="flex items-center gap-1.5 text-sm font-medium text-slate-600 dark:text-slate-300 mb-1.5">
              <MapPin className="w-3.5 h-3.5 text-slate-400" />
              {t('calendar.common.location')}
            </label>
            <input
              type="text"
              value={form.location}
              onChange={handleLocationChange}
              placeholder={t('calendar.editor.locationPlaceholder')}
              className="w-full px-3.5 py-2.5 text-sm rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50/50 dark:bg-slate-800/50 text-gray-900 dark:text-slate-100 placeholder:text-slate-400 dark:placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 focus:bg-white dark:focus:bg-slate-800 hover:border-slate-300 dark:hover:border-slate-600 transition-all"
            />
          </div>

          <div>
            <label className="flex items-center justify-between text-sm font-medium text-slate-600 dark:text-slate-300 mb-2">
              <span className="flex items-center gap-1.5">
                <Palette className="w-3.5 h-3.5 text-slate-400" />
                {t('calendar.editor.color')}
              </span>
              <span className="text-xs text-slate-400 dark:text-slate-500 font-normal">{t('calendar.editor.colorHint')}</span>
            </label>
            <div className="flex items-center gap-1.5 p-2 rounded-xl bg-slate-50/60 dark:bg-slate-800/40 border border-slate-100 dark:border-slate-700/50">
              <div onClick={handleColorGroupClick} role="group" aria-label={t('calendar.editor.presetColors')} className="flex items-center gap-1.5 flex-1">
                {presetColors.map((c, idx) => {
                  const active = form.color?.toLowerCase() === c.value.toLowerCase();
                  return (
                    <button
                      key={c.value}
                      type="button"
                      data-color={c.value}
                      className={`relative w-7 h-7 rounded-full shadow-sm transition-all duration-200 group/dot ${
                        active
                          ? 'scale-110 ring-2 ring-offset-2 ring-offset-slate-50 dark:ring-offset-slate-800 ring-slate-700 dark:ring-white shadow-md'
                          : 'hover:scale-110 hover:shadow-md'
                      }`}
                      style={{ backgroundColor: c.value }}
                      title={c.label}
                    >
                      {active && (
                        <Check className="absolute inset-0 m-auto w-3.5 h-3.5 text-white drop-shadow-md" strokeWidth={3} />
                      )}
                      {/* 色系分隔线：每 3 个色球后加间距（第 0,3,6,9,12 个前） */}
                      {idx > 0 && idx % 3 === 0 && (
                        <span className="absolute -left-1 top-1/2 -translate-y-1/2 w-px h-5 bg-slate-200 dark:bg-slate-600" aria-hidden="true" />
                      )}
                    </button>
                  );
                })}
              </div>
              {/* 自定义颜色 —— 最右边 */}
              <div className="flex items-center gap-1.5 pl-1.5 ml-1 border-l border-slate-200 dark:border-slate-600">
                <label
                  className={`relative w-7 h-7 rounded-full cursor-pointer border-2 border-dashed transition-all duration-200 flex items-center justify-center overflow-hidden ${
                    !presetColors.some(c => c.value.toLowerCase() === (form.color ?? '').toLowerCase())
                      ? 'border-sky-500 bg-sky-50 dark:bg-sky-900/30 scale-110 ring-2 ring-offset-2 ring-offset-slate-50 dark:ring-offset-slate-800 ring-sky-400/50'
                      : 'border-slate-300 dark:border-slate-500 hover:border-sky-400 hover:scale-105 bg-white dark:bg-slate-700'
                  }`}
                  title={t('calendar.editor.customColor')}
                >
                  <input
                    type="color"
                    value={form.color}
                    onChange={handleColorInputChange}
                    className="absolute inset-0 w-full h-full opacity-0 cursor-pointer"
                  />
                  <Palette className="w-3.5 h-3.5 text-slate-400 dark:text-slate-300 pointer-events-none" />
                </label>
              </div>
            </div>
          </div>

          <div className="pt-3 border-t border-slate-200/70 dark:border-slate-700/60 space-y-2.5">
            {/* ========== 重复规则 手风琴 ========== */}
            <div className="overflow-hidden rounded-2xl border border-slate-200/60 dark:border-slate-700/50 bg-white dark:bg-slate-900 transition-all hover:shadow-md hover:shadow-slate-500/5">
              <button
                type="button"
                onClick={toggleRecurrencePanel}
                aria-expanded={recurrencePanelOpen}
                aria-controls="panel-recurrence"
                className={`w-full flex items-center justify-between px-4 py-3 text-sm font-medium transition-all duration-200 ${
                  recurrencePanelOpen
                    ? 'bg-gradient-to-r from-sky-50 via-white to-indigo-50 dark:from-sky-950/30 dark:via-slate-900 dark:to-indigo-950/20'
                    : 'bg-slate-50/70 dark:bg-slate-800/50 hover:bg-slate-100 dark:hover:bg-slate-700/50'
                }`}
              >
                <span className="flex items-center gap-2.5">
                  <span className={`flex items-center justify-center w-7 h-7 rounded-lg shadow-sm transition-all duration-200 ${
                    recurrencePanelOpen
                      ? 'bg-gradient-to-br from-sky-500 to-indigo-500 text-white'
                      : 'bg-sky-100 dark:bg-sky-900/40 text-sky-600 dark:text-sky-400'
                  }`}>
                    <Repeat className="w-3.5 h-3.5" />
                  </span>
                  <span className="text-slate-700 dark:text-slate-200">{t('calendar.editor.recurrenceRule')}</span>
                  {form.recurrenceType != null && form.recurrenceType >= 0 && (
                    <span className="inline-flex items-center px-2 py-0.5 text-[10px] font-semibold rounded-full bg-sky-100 dark:bg-sky-900/40 text-sky-700 dark:text-sky-300 border border-sky-200/60 dark:border-sky-800/40">
                      {t('calendar.editor.recurrenceEnabled')}
                    </span>
                  )}
                </span>
                <ChevronDown className={`w-4 h-4 text-slate-400 transition-transform duration-200 ${recurrencePanelOpen ? 'rotate-180' : ''}`} />
              </button>
              {recurrencePanelOpen && (
                <div
                  id="panel-recurrence"
                  className="space-y-4 p-4 bg-gradient-to-br from-sky-50/40 via-white to-indigo-50/30 dark:from-sky-950/20 dark:via-slate-900/50 dark:to-indigo-950/15 border-t border-sky-100/60 dark:border-sky-900/30 animate-[cal-fade-in-up_160ms_ease-out_both]"
                >
                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className="block text-xs font-semibold text-slate-600 dark:text-slate-400 mb-1.5">
                        {t('calendar.editor.frequency')}
                      </label>
                      <select
                        value={form.recurrenceType ?? -1}
                        onChange={handleRecurrenceTypeChange}
                        className="w-full px-3 py-2 text-sm rounded-xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/70 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all shadow-sm"
                      >
                        <option value={-1}>{t('calendar.editor.recurrenceNone')}</option>
                        {RECURRENCE_TYPE_OPTIONS.map((o) => (
                          <option key={o.value} value={o.value}>
                            {o.label}
                          </option>
                        ))}
                      </select>
                    </div>
                    <div>
                      <label className="block text-xs font-semibold text-slate-600 dark:text-slate-400 mb-1.5">
                        {t('calendar.editor.interval')}
                      </label>
                      <div className="flex items-center gap-2">
                        <input
                          type="number"
                          min={1}
                          value={form.recurrenceInterval ?? 1}
                          onChange={handleIntervalChange}
                          className="w-20 px-3 py-2 text-sm rounded-xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/70 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all shadow-sm"
                        />
                        <span className="text-xs text-slate-500 dark:text-slate-400 font-medium">
                          {/* 单位必须与输入框里的数量一致：`RECURRENCE_UNIT_LABELS` 拿不到 count，
                              英文下会渲染 "1 days"（队长修复 W13-V2），故走带 count 的 helper */}
                          {form.recurrenceType != null && form.recurrenceType >= 0
                            ? recurrenceUnitLabel(form.recurrenceType, form.recurrenceInterval ?? 1)
                            : ''}
                        </span>
                      </div>
                    </div>
                  </div>

                  {/* 周几选择：仅在「每周」频率时显示，其他频率无意义 */}
                  {form.recurrenceType === 1 && (
                    <div>
                      <div className="flex items-center justify-between mb-2">
                        <label className="block text-xs font-semibold text-slate-600 dark:text-slate-400">
                          {t('calendar.editor.weekdaysLabel')}
                        </label>
                        <div className="flex gap-2">
                          <button
                            type="button"
                            onClick={() => setFormField('weekdaysSelected', [1, 2, 3, 4, 5])}
                            className="text-[10px] text-sky-600 dark:text-sky-400 font-semibold hover:underline"
                          >
                            {t('calendar.editor.weekdays')}
                          </button>
                          <span className="text-slate-300 dark:text-slate-600">|</span>
                          <button
                            type="button"
                            onClick={() => setFormField('weekdaysSelected', [0, 1, 2, 3, 4, 5, 6])}
                            className="text-[10px] text-sky-600 dark:text-sky-400 font-semibold hover:underline"
                          >
                            {t('calendar.editor.everyDay')}
                          </button>
                          <span className="text-slate-300 dark:text-slate-600">|</span>
                          <button
                            type="button"
                            onClick={() => setFormField('weekdaysSelected', [])}
                            className="text-[10px] text-slate-400 dark:text-slate-500 font-semibold hover:underline"
                          >
                            {t('calendar.editor.clear')}
                          </button>
                        </div>
                      </div>
                      <div onClick={handleWeekdayGroupClick} role="group" aria-label={t('calendar.editor.recurrenceWeekdays')} className="flex gap-1.5 flex-wrap">
                        {WEEKDAYS.map((w) => {
                          const on = (form.weekdaysSelected ?? []).includes(w.key);
                          return (
                            <button
                              key={w.key}
                              type="button"
                              data-weekday-key={w.key}
                              className={`w-9 h-9 rounded-xl text-xs font-semibold border transition-all duration-200 active:scale-95 ${
                                on
                                  ? 'bg-gradient-to-br from-sky-500 to-indigo-500 text-white border-transparent shadow-md shadow-sky-500/30 scale-105'
                                  : 'bg-white dark:bg-slate-800 text-slate-600 dark:text-slate-400 border-slate-200 dark:border-slate-700 hover:border-sky-400 hover:text-sky-600 hover:shadow-sm'
                              }`}
                            >
                              {t(w.labelKey)}
                            </button>
                          );
                        })}
                      </div>
                    </div>
                  )}

                  <div>
                    <label className="block text-xs font-semibold text-slate-600 dark:text-slate-400 mb-2">
                      {t('calendar.editor.recurrenceEndMode')}
                    </label>
                    <div className="grid grid-cols-3 gap-2 mb-2">
                      {RECURRENCE_END_MODES.map((opt) => {
                        const active = recurrenceEndMode === opt.v;
                        return (
                          <button
                            key={opt.v}
                            type="button"
                            onClick={() => handleRecurrenceEndModeChange(opt.v)}
                            className={`group px-2 py-2.5 rounded-xl border transition-all duration-200 ${
                              active
                                ? 'border-sky-500 bg-gradient-to-br from-sky-50 via-white to-indigo-50 dark:from-sky-900/30 dark:via-slate-800 dark:to-indigo-900/20 text-sky-700 dark:text-sky-300 ring-2 ring-sky-500/30 shadow-sm scale-[1.02]'
                                : 'border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/50 text-slate-600 dark:text-slate-400 hover:bg-slate-50 dark:hover:bg-slate-700/50'
                            }`}
                          >
                            <div className="text-xs font-bold">{t(opt.labelKey)}</div>
                            <div className="text-[10px] mt-0.5 opacity-70">{t(opt.descKey)}</div>
                          </button>
                        );
                      })}
                    </div>
                    {recurrenceEndMode === 'count' && (
                      <div className="flex items-center gap-2 animate-[cal-fade-in-up_150ms_ease-out_both]">
                        <span className="text-xs text-slate-500 dark:text-slate-400">{t('calendar.editor.recurrenceCountPrefix')}</span>
                        <input
                          type="number"
                          min={1}
                          value={form.recurrenceCount ?? 1}
                          onChange={handleRecurrenceCountChange}
                          className="w-20 px-3 py-2 text-sm rounded-xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/70 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all shadow-sm"
                        />
                        <span className="text-xs text-slate-500 dark:text-slate-400">{t('calendar.editor.recurrenceCountSuffix')}</span>
                      </div>
                    )}
                    {recurrenceEndMode === 'date' && (
                      <div className="flex items-center gap-2 animate-[cal-fade-in-up_150ms_ease-out_both]">
                        <span className="text-xs text-slate-500 dark:text-slate-400">{t('calendar.editor.recurrenceUntilPrefix')}</span>
                        <input
                          type="date"
                          value={form.recurrenceEndDate}
                          onChange={handleRecurrenceEndDateChange}
                          className="px-3 py-2 text-sm rounded-xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/70 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all shadow-sm"
                        />
                      </div>
                    )}
                  </div>

                  {/* 实时中文描述预览：用户修改频率/间隔/周几/结束方式后自动更新，无需手动点击 */}
                  {form.recurrenceType != null && form.recurrenceType >= 0 && (
                    <div className="flex items-start gap-2 p-2.5 rounded-xl bg-sky-50/60 dark:bg-sky-950/20 border border-sky-100 dark:border-sky-900/30 text-xs text-slate-600 dark:text-slate-400">
                      <SharedIcon name="info" className="w-3.5 h-3.5 mt-0.5 shrink-0 text-sky-500" />
                      <div className="flex-1">
                        <span className="font-medium text-sky-700 dark:text-sky-300">
                          {generateRRuleDescription(form, recurrenceEndMode) || t('calendar.editor.recurrenceActive')}
                        </span>
                      </div>
                    </div>
                  )}
                </div>
              )}
            </div>

            {/* ========== 提醒 手风琴 ========== */}
            <div className="overflow-hidden rounded-2xl border border-slate-200/60 dark:border-slate-700/50 bg-white dark:bg-slate-900 transition-all hover:shadow-md hover:shadow-slate-500/5">
              <button
                type="button"
                onClick={toggleReminderPanel}
                aria-expanded={reminderPanelOpen}
                aria-controls="panel-reminder"
                className={`w-full flex items-center justify-between px-4 py-3 text-sm font-medium transition-all duration-200 ${
                  reminderPanelOpen
                    ? 'bg-gradient-to-r from-amber-50 via-white to-orange-50 dark:from-amber-950/30 dark:via-slate-900 dark:to-orange-950/20'
                    : 'bg-slate-50/70 dark:bg-slate-800/50 hover:bg-slate-100 dark:hover:bg-slate-700/50'
                }`}
              >
                <span className="flex items-center gap-2.5">
                  <span className={`flex items-center justify-center w-7 h-7 rounded-lg shadow-sm transition-all duration-200 ${
                    reminderPanelOpen
                      ? 'bg-gradient-to-br from-amber-500 to-orange-500 text-white'
                      : 'bg-amber-100 dark:bg-amber-900/40 text-amber-600 dark:text-amber-400'
                  }`}>
                    <Bell className="w-3.5 h-3.5" />
                  </span>
                  <span className="text-slate-700 dark:text-slate-200">{t('calendar.common.reminder')}</span>
                  {form.reminderMinutesBefore != null && form.reminderMinutesBefore >= 0 && (
                    <span className="inline-flex items-center px-2 py-0.5 text-[10px] font-semibold rounded-full bg-amber-100 dark:bg-amber-900/40 text-amber-700 dark:text-amber-300 border border-amber-200/60 dark:border-amber-800/40">
                      {REMINDER_OPTIONS.find((o) => o.value === form.reminderMinutesBefore)?.label ?? t('calendar.common.custom')}
                      {form.reminderType ? ` · ${reminderTypesLabel(form.reminderType)}` : ''}
                    </span>
                  )}
                </span>
                <ChevronDown className={`w-4 h-4 text-slate-400 transition-transform duration-200 ${reminderPanelOpen ? 'rotate-180' : ''}`} />
              </button>
              {reminderPanelOpen && (
                <div
                  id="panel-reminder"
                  className="space-y-3 p-4 bg-gradient-to-br from-amber-50/40 via-white to-orange-50/30 dark:from-amber-950/20 dark:via-slate-900/50 dark:to-orange-950/15 border-t border-amber-100/60 dark:border-amber-900/30 animate-[cal-fade-in-up_160ms_ease-out_both]"
                >
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-xs font-semibold text-slate-600 dark:text-slate-400 mb-1.5">
                        {t('calendar.editor.reminderLeadTime')}
                      </label>
                      <select
                        value={
                          // T000097: 优先用 isCustomReminder 标志判断，
                          // 否则当 reminderMinutesBefore 恰好等于预设值（如 30）时，
                          // 下拉框会错误地显示"30分钟前"而非"自定义"。
                          form.isCustomReminder
                            ? 'custom'
                            : form.reminderMinutesBefore == null
                              ? ''
                              : REMINDER_OPTIONS.some((o) => o.value === form.reminderMinutesBefore)
                                ? String(form.reminderMinutesBefore)
                                : 'custom'
                        }
                        onChange={handleReminderMinutesChange}
                        className="w-full px-3 py-2.5 text-sm rounded-xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/70 focus:outline-none focus:ring-2 focus:ring-amber-500/30 focus:border-amber-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all shadow-sm"
                      >
                        <option value="">{t('calendar.editor.noReminder')}</option>
                        {REMINDER_OPTIONS.map((o) => (
                          <option key={o.value} value={o.value}>
                            {o.label}
                          </option>
                        ))}
                        <option value="custom">{t('calendar.editor.customDots')}</option>
                      </select>
                    </div>
                    <div>
                      <label className="block text-xs font-semibold text-slate-600 dark:text-slate-400 mb-1.5">
                        {t('calendar.editor.reminderTypes')}<span className="text-slate-400 dark:text-slate-500 font-normal">{t('calendar.editor.reminderTypesOption')}</span>
                      </label>
                      <div className="flex flex-wrap gap-1.5" role="group" aria-label={t('calendar.editor.reminderTypesAria')}>
                        {REMINDER_TYPE_OPTIONS.map((o) => {
                          const checked = parseReminderTypes(form.reminderType).includes(o.value);
                          return (
                            <button
                              key={o.value}
                              type="button"
                              role="checkbox"
                              aria-checked={checked}
                              onClick={() => handleReminderTypeToggle(o.value)}
                              className={`px-2.5 py-1.5 text-xs font-medium rounded-lg border transition-all ${
                                checked
                                  ? 'bg-amber-500/10 border-amber-400 text-amber-600 dark:bg-amber-400/10 dark:text-amber-300 shadow-sm'
                                  : 'bg-white dark:bg-slate-800/60 border-slate-200 dark:border-slate-700 text-slate-500 dark:text-slate-400 hover:border-slate-300'
                              }`}
                            >
                              {checked ? '☑ ' : '☐ '}
                              {o.label}
                            </button>
                          );
                        })}
                      </div>
                      {parseReminderTypes(form.reminderType).length === 0 && (
                        <p className="mt-1 text-[10px] text-slate-400 dark:text-slate-500">
                          {t('calendar.editor.reminderDefaultHint')}
                        </p>
                      )}
                    </div>
                  </div>
                  {/* T000097: 自定义提前时间输入
                      显示条件：reminderMinutesBefore 非 null 且
                      （用户点击过"自定义"或当前值不在预设列表中） */}
                  {form.reminderMinutesBefore != null &&
                    (form.isCustomReminder ||
                      !REMINDER_OPTIONS.some((o) => o.value === form.reminderMinutesBefore)) && (
                    <div className="flex items-center gap-2 animate-[cal-fade-in-up_150ms_ease-out_both]">
                      <span className="text-xs text-slate-500 dark:text-slate-400">{t('calendar.editor.reminderLeadShort')}</span>
                      <input
                        type="number"
                        min={1}
                        value={form.reminderMinutesBefore}
                        onChange={(e) => setFormField('reminderMinutesBefore', Math.max(1, Number(e.target.value) || 1))}
                        className="w-24 px-3 py-2 text-sm rounded-xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/70 focus:outline-none focus:ring-2 focus:ring-amber-500/30 focus:border-amber-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all shadow-sm"
                      />
                      <span className="text-xs text-slate-500 dark:text-slate-400">{t('calendar.editor.minutesBefore')}</span>
                      <span className="text-[10px] text-slate-400 dark:text-slate-500">
                        {form.reminderMinutesBefore >= 1440
                          ? t('calendar.editor.approxDays', { count: Math.floor(form.reminderMinutesBefore / 1440) })
                          : form.reminderMinutesBefore >= 60
                            ? t('calendar.editor.approxHours', { count: Math.floor(form.reminderMinutesBefore / 60) })
                            : ''}
                      </span>
                    </div>
                  )}
                  {form.reminderMinutesBefore != null && (
                    <div className="flex items-start gap-2 p-2.5 rounded-xl bg-amber-50/60 dark:bg-amber-950/20 border border-amber-100 dark:border-amber-900/30 text-xs text-slate-600 dark:text-slate-400">
                      <SharedIcon name="info" className="w-3.5 h-3.5 mt-0.5 shrink-0 text-amber-500" />
                      <div className="flex-1">
                        {t('calendar.editor.reminderTypesHelp')}
                        {parseReminderTypes(form.reminderType).includes('email') && attendeeCount === 0 && (
                          <span className="block mt-1 text-amber-600 dark:text-amber-400 font-medium">
                            {t('calendar.editor.emailReminderNeedsAttendee')}
                          </span>
                        )}
                      </div>
                    </div>
                  )}
                </div>
              )}
            </div>

            {/* ========== 参会人 手风琴 ========== */}
            <div className="overflow-hidden rounded-2xl border border-slate-200/60 dark:border-slate-700/50 bg-white dark:bg-slate-900 transition-all hover:shadow-md hover:shadow-slate-500/5">
              <button
                type="button"
                onClick={toggleAttendeeDropdown}
                aria-expanded={attendeeDropdownOpen}
                aria-controls="panel-attendees"
                className={`w-full flex items-center justify-between px-4 py-3 text-sm font-medium transition-all duration-200 ${
                  attendeeDropdownOpen
                    ? 'bg-gradient-to-r from-emerald-50 via-white to-teal-50 dark:from-emerald-950/30 dark:via-slate-900 dark:to-teal-950/20'
                    : 'bg-slate-50/70 dark:bg-slate-800/50 hover:bg-slate-100 dark:hover:bg-slate-700/50'
                }`}
              >
                <span className="flex items-center gap-2.5">
                  <span className={`flex items-center justify-center w-7 h-7 rounded-lg shadow-sm transition-all duration-200 ${
                    attendeeDropdownOpen
                      ? 'bg-gradient-to-br from-emerald-500 to-teal-500 text-white'
                      : 'bg-emerald-100 dark:bg-emerald-900/40 text-emerald-600 dark:text-emerald-400'
                  }`}>
                    <Users className="w-3.5 h-3.5" />
                  </span>
                  <span className="text-slate-700 dark:text-slate-200">{t('calendar.editor.attendees')}</span>
                  {attendeeCount > 0 && (
                    <span className="inline-flex items-center justify-center min-w-[22px] px-2 h-[22px] text-[10px] font-bold rounded-full bg-gradient-to-br from-emerald-500 to-teal-500 text-white shadow-sm shadow-emerald-500/30">
                      {attendeeCount}
                    </span>
                  )}
                </span>
                <ChevronDown className={`w-4 h-4 text-slate-400 transition-transform duration-200 ${attendeeDropdownOpen ? 'rotate-180' : ''}`} />
              </button>
              {attendeeDropdownOpen && (
                <div
                  id="panel-attendees"
                  className="space-y-3 p-4 bg-gradient-to-br from-emerald-50/40 via-white to-teal-50/30 dark:from-emerald-950/20 dark:via-slate-900/50 dark:to-teal-950/15 border-t border-emerald-100/60 dark:border-emerald-900/30 animate-[cal-fade-in-up_160ms_ease-out_both]"
                >
                  {/* 已添加的参会人 chips */}
                  {attendeeEmailList.length > 0 && (
                    <div className="rounded-xl bg-white/60 dark:bg-slate-800/40 border border-emerald-100/60 dark:border-emerald-900/20 overflow-hidden">
                      <div className="flex items-center justify-between px-2 py-1.5 bg-emerald-50/60 dark:bg-emerald-950/20 border-b border-emerald-100/60 dark:border-emerald-900/30">
                        <span className="text-[10px] font-semibold text-emerald-700 dark:text-emerald-300">
                          {t('calendar.editor.attendeesAdded', { count: attendeeEmailList.length })}
                        </span>
                        <button
                          type="button"
                          onClick={clearAllAttendees}
                          className="text-[10px] text-red-500 dark:text-red-400 font-semibold hover:underline hover:text-red-600 dark:hover:text-red-300 transition-colors"
                          aria-label={t('calendar.editor.clearAllAttendeesAria')}
                        >
                          {t('calendar.editor.clearAll')}
                        </button>
                      </div>
                      <div className="flex flex-wrap gap-1.5 p-2 max-h-32 overflow-y-auto">
                        {attendeeEmailList.map((email) => (
                          <span
                            key={email}
                            className="inline-flex items-center gap-1.5 pl-2.5 pr-1 py-1 rounded-full text-xs bg-gradient-to-r from-emerald-100 to-sky-100 dark:from-emerald-900/40 dark:to-sky-900/30 text-emerald-700 dark:text-emerald-300 border border-emerald-200/60 dark:border-emerald-800/40 shadow-sm"
                          >
                            <SharedIcon name="mail" className="w-3 h-3 opacity-80" />
                            <span className="max-w-[180px] truncate font-medium">{email}</span>
                            <button
                              type="button"
                              onClick={() => removeAttendeeEmail(email)}
                              className="w-4 h-4 rounded-full flex items-center justify-center text-emerald-600 dark:text-emerald-300 hover:bg-emerald-200/80 dark:hover:bg-emerald-800/70 transition-colors"
                              aria-label={t('calendar.editor.removeAttendee', { name: email })}
                            >
                              <X className="w-2.5 h-2.5" strokeWidth={3} />
                            </button>
                          </span>
                        ))}
                      </div>
                    </div>
                  )}
                  <div className="p-2.5 rounded-xl bg-amber-50/60 dark:bg-amber-950/20 border border-dashed border-amber-200/70 dark:border-amber-900/40 text-xs text-amber-700 dark:text-amber-300">
                    <div className="flex items-start gap-1.5">
                      <SharedIcon name="info" className="w-3.5 h-3.5 mt-0.5 shrink-0 text-amber-500" />
                      <div className="flex-1">
                        {t('calendar.editor.attendeeInputHint')}
                      </div>
                    </div>
                  </div>
                  <div className="relative">
                    <div className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400 dark:text-slate-500 pointer-events-none">
                      <SharedIcon name="mail" className="w-4 h-4" />
                    </div>
                    <input
                      ref={attendeeInputRef}
                      type="text"
                      value={attendeeInput}
                      onChange={(e) => {
                        setAttendeeInput(e.target.value);
                        // r5: 用户重新输入时清除上一次的无效邮箱提示
                        if (attendeeInvalidHint !== null) setAttendeeInvalidHint(null);
                      }}
                      placeholder={t('calendar.editor.attendeeInputPlaceholder')}
                      onKeyDown={handleAttendeeKeyDown}
                      className="w-full pl-9 pr-3 py-2.5 text-sm rounded-xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800/70 focus:outline-none focus:ring-2 focus:ring-emerald-500/30 focus:border-emerald-400 hover:border-slate-300 dark:hover:border-slate-600 transition-all shadow-sm"
                    />
                  </div>
                  {/* r5: 无效邮箱提示 — 当输入无法识别为合法邮箱时显示 */}
                  {attendeeInvalidHint && (
                    <div className="flex items-start gap-1.5 px-2.5 py-1.5 rounded-lg bg-red-50/80 dark:bg-red-950/20 border border-red-200/70 dark:border-red-900/40 text-xs text-red-600 dark:text-red-400 animate-[cal-fade-in-up_150ms_ease-out_both]">
                      <AlertCircle className="w-3.5 h-3.5 mt-0.5 shrink-0 text-red-500" />
                      <span className="flex-1">{attendeeInvalidHint}</span>
                      <button
                        type="button"
                        onClick={() => setAttendeeInvalidHint(null)}
                        className="text-red-400 hover:text-red-600 dark:hover:text-red-300 transition-colors shrink-0"
                        aria-label={t('calendar.common.closeHint')}
                      >
                        <X className="w-3 h-3" strokeWidth={2.5} />
                      </button>
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>
        </div>

        <div className="flex items-center justify-between px-5 py-4 border-t border-gray-200 dark:border-slate-700 bg-gradient-to-r from-slate-50 to-white dark:from-slate-800/30 dark:to-slate-900 rounded-b-2xl">
          <div className="flex items-center gap-3 min-h-[20px]">
            {formError && (
              <span className="text-sm text-red-600 dark:text-red-400">{formError}</span>
            )}
            {saving && (
              <span className="flex items-center gap-1.5 text-sm text-gray-500 dark:text-slate-400">
                <Loader2 className="w-4 h-4 animate-spin" />
                {t('calendar.common.saving')}
              </span>
            )}
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => {
                if (closing) return;
                setClosing(true);
                if (closeTimerRef.current !== null) {
                  clearTimeout(closeTimerRef.current);
                }
                closeTimerRef.current = window.setTimeout(() => {
                  useCalendarStore.setState({ pendingSignalRBump: true });
                  const s = useCalendarStore.getState();
                  s.resetForm();
                  s.closeEditor();
                }, 120);
              }}
              disabled={saving}
              className="px-4 py-2 text-sm font-medium text-gray-600 dark:text-slate-300 bg-white dark:bg-slate-700 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-slate-100 dark:hover:bg-slate-700 disabled:opacity-50 disabled:cursor-not-allowed transition-all"
            >
              {t('common.cancel')}
            </button>
            {editing ? (
              <button
                type="button"
                onClick={handleDelete}
                disabled={saving}
                className="px-4 py-2 text-sm font-medium text-white bg-gradient-to-r from-red-500 to-rose-600 hover:from-red-600 hover:to-rose-700 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed shadow-md shadow-red-500/25 transition-all"
              >
                {t('common.delete')}
              </button>
            ) : null}
            <button
              type="button"
              onClick={handleSave}
              disabled={saving || !form.title.trim()}
              title={!form.title.trim() ? t('calendar.editor.titleRequiredHint') : ''}
              className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-gradient-to-r from-sky-500 to-blue-600 hover:from-sky-600 hover:to-blue-700 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed shadow-md shadow-sky-500/25 transition-all"
            >
              {saving ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" />
                  {t('calendar.common.saving')}
                </>
              ) : (
                <>
                  <Check className="w-4 h-4" />
                  {t('common.save')}
                </>
              )}
            </button>
          </div>
        </div>
      </div>

      {deleteTarget && (
        <div
          className="fixed inset-0 z-[60] bg-gradient-to-br from-red-900/30 to-slate-900/60 flex items-center justify-center p-4"
          onClick={handleCancelDelete}
        >
          <div
            className="bg-white dark:bg-slate-900 rounded-xl shadow-2xl w-full max-w-md p-5 ring-1 ring-red-200/30 dark:ring-red-800/30"
            onClick={handleStopPropagation}
          >
            <div className="bg-red-50 dark:bg-red-900/20 rounded-full p-3 w-fit mb-3">
              <Trash2 className="w-6 h-6 text-red-600 dark:text-red-400" />
            </div>
            <h3 className="text-lg font-semibold text-gray-900 dark:text-slate-100 mb-2">
              {t('calendar.common.deleteEvent')}
            </h3>
            <p className="text-sm text-gray-600 dark:text-slate-300 mb-5">
              {t('calendar.editor.confirmDeleteMessage', { title: deleteTarget.title })}
            </p>
            <div className="flex items-center justify-end gap-2">
              <button
                type="button"
                onClick={handleCancelDelete}
                disabled={deleting}
                className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-slate-200 bg-gray-100 dark:bg-slate-700 rounded-lg hover:bg-gray-200 dark:hover:bg-slate-600 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                {t('common.cancel')}
              </button>
              <button
                type="button"
                onClick={confirmDelete}
                disabled={deleting}
                className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-red-600 rounded-lg hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                {deleting ? (
                  <>
                    <Loader2 className="w-4 h-4 animate-spin" />
                    {t('calendar.editor.deletingDots')}
                  </>
                ) : (
                  <>
                    <Trash2 className="w-4 h-4" />
                    {t('calendar.editor.confirmDelete')}
                  </>
                )}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* P2-A#2: 保存为模板 模态框 */}
      {showSaveTemplateModal && (
        <div
          className="fixed inset-0 z-[60] bg-slate-900/50 backdrop-blur-[2px] flex items-center justify-center p-4"
          onClick={closeSaveTemplateModal}
        >
          <div
            className="bg-white dark:bg-slate-900 rounded-2xl shadow-2xl w-full max-w-md p-5 ring-1 ring-slate-200 dark:ring-slate-800"
            onClick={handleStopPropagation}
          >
            <h3 className="text-lg font-semibold text-gray-900 dark:text-slate-100 mb-1 inline-flex items-center gap-2">
              <SaveIcon className="w-5 h-5 text-sky-600 dark:text-sky-400" />
              {t('calendar.editor.saveAsTemplate')}
            </h3>
            <p className="text-sm text-slate-500 dark:text-slate-400 mb-4">
              {t('calendar.editor.saveTemplateMessage', {
                title: form.title.trim() || t('calendar.editor.unnamedEvent'),
              })}
            </p>
            <div className="mb-5">
              <label className="block text-sm font-medium text-slate-600 dark:text-slate-300 mb-1.5">
                {t('calendar.editor.templateName')} <span className="text-red-500">*</span>
              </label>
              <input
                autoFocus
                type="text"
                value={templateNameInput}
                onChange={(e) => setTemplateNameInput(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' && templateNameInput.trim()) handleConfirmSaveTemplate();
                  if (e.key === 'Escape') closeSaveTemplateModal();
                }}
                placeholder={t('calendar.editor.templateNamePlaceholder')}
                className="w-full px-3.5 py-2.5 text-sm rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50/50 dark:bg-slate-800/50 text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 focus:bg-white dark:focus:bg-slate-800 hover:border-slate-300 dark:hover:border-slate-600 transition-all"
              />
            </div>
            <div className="flex items-center justify-end gap-2">
              <button
                type="button"
                onClick={closeSaveTemplateModal}
                disabled={saveTemplateLoading}
                className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-slate-200 bg-gray-100 dark:bg-slate-700 rounded-xl hover:bg-gray-200 dark:hover:bg-slate-600 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                {t('common.cancel')}
              </button>
              <button
                type="button"
                onClick={handleConfirmSaveTemplate}
                disabled={saveTemplateLoading || !templateNameInput.trim()}
                className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-sky-600 rounded-xl hover:bg-sky-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                {saveTemplateLoading ? (
                  <>
                    <Loader2 className="w-4 h-4 animate-spin" />
                    {t('calendar.common.saving')}
                  </>
                ) : (
                  <>
                    <Check className="w-4 h-4" />
                    {t('calendar.editor.confirmSave')}
                  </>
                )}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* P2-A#2: 管理模板 模态框 */}
      {showManageTemplatesModal && (
        <div
          className="fixed inset-0 z-[60] bg-slate-900/50 backdrop-blur-[2px] flex items-center justify-center p-4"
          onClick={() => setShowManageTemplatesModal(false)}
        >
          <div
            className="bg-white dark:bg-slate-900 rounded-2xl shadow-2xl w-full max-w-lg p-5 ring-1 ring-slate-200 dark:ring-slate-800"
            onClick={handleStopPropagation}
          >
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-lg font-semibold text-gray-900 dark:text-slate-100 inline-flex items-center gap-2">
                <Settings2Icon className="w-5 h-5 text-slate-600 dark:text-slate-400" />
                {t('calendar.editor.manageTemplates')}
              </h3>
              <button
                type="button"
                onClick={() => setShowManageTemplatesModal(false)}
                className="p-1.5 rounded-lg text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
                title={t('common.close')}
              >
                <X className="w-4 h-4" />
              </button>
            </div>
            {eventTemplates.length === 0 ? (
              <div className="py-10 text-center text-sm text-slate-500 dark:text-slate-400">
                {t('calendar.editor.noTemplates')}
              </div>
            ) : (
              <ul className="divide-y divide-slate-100 dark:divide-slate-800 max-h-[50vh] overflow-y-auto -mx-1">
                {eventTemplates.map((tpl) => {
                  const style = EVENT_TYPE_STYLE[tpl.eventType];
                  const isDeleting = manageDeletingId === tpl.id;
                  return (
                    <li
                      key={tpl.id}
                      className="flex items-start justify-between gap-3 py-3 px-1 first:pt-0 last:pb-0"
                    >
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 flex-wrap">
                          <span
                            className={`inline-flex items-center gap-1 px-2 py-0.5 text-[11px] font-medium rounded-md ${style?.badgeClass ?? EVENT_TYPE_STYLE.other.badgeClass}`}
                          >
                            {style?.label ?? tpl.eventType}
                          </span>
                          <span className="text-xs text-slate-500 dark:text-slate-400">
                            {tpl.isAllDay
                              ? t('calendar.common.allDay')
                              : t('calendar.editor.durationMinutes', { minutes: tpl.durationMin })}
                          </span>
                        </div>
                        <div className="mt-1.5 text-sm font-medium text-gray-900 dark:text-slate-100 truncate">
                          {tpl.name}
                        </div>
                        <div className="mt-0.5 text-xs text-slate-500 dark:text-slate-400 truncate">
                          {tpl.title}
                          {tpl.location ? ` · 📍 ${tpl.location}` : ''}
                        </div>
                      </div>
                      <button
                        type="button"
                        onClick={() => handleDeleteTemplate(tpl.id)}
                        disabled={isDeleting}
                        className="shrink-0 p-2 rounded-lg text-slate-500 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-950/30 dark:text-slate-400 dark:hover:text-red-400 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                        title={t('calendar.editor.deleteTemplate')}
                      >
                        {isDeleting ? (
                          <Loader2 className="w-4 h-4 animate-spin" />
                        ) : (
                          <Trash2Icon className="w-4 h-4" />
                        )}
                      </button>
                    </li>
                  );
                })}
              </ul>
            )}
            <div className="mt-5 flex items-center justify-end">
              <button
                type="button"
                onClick={() => setShowManageTemplatesModal(false)}
                className="px-4 py-2 text-sm font-medium text-white bg-slate-900 dark:bg-slate-100 dark:text-slate-900 rounded-xl hover:bg-slate-800 dark:hover:bg-white transition-colors"
              >
                {t('calendar.common.complete')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
