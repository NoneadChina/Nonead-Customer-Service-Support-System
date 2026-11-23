import { useEffect, useState, useRef, useCallback } from 'react';
import { signalRService, SignalREvents, type CalendarReminderNotification } from '../services/signalR';
import { calendarApi } from '../api/modules/engagement';
import { useAuth } from '../context/AuthContext';
import { useI18n } from '../context/I18nContext';
import { BellRing, CheckCircle2, CalendarCheck, XCircle as XIcon } from 'lucide-react';

/**
 * 全局日历提醒监听器（T000090 修复）
 *
 * 背景：原 Calendar.tsx 仅在 /calendar 页面监听 SignalR 的 CalendarReminder 事件，
 * 用户离开日历页后无法收到提醒弹窗。本组件挂载在全局 App 层级，
 * 任意页面登录后都能接收弹窗提醒，并通过 calendarApi.confirmReminder 完成 ACK。
 *
 * 行为：
 * - 收到 SignalR CalendarReminder 事件 → 右上角显示提醒弹窗
 * - 同一 eventId+sentAt 不重复弹（去重）
 * - 30 秒未交互 → 自动关闭并发送 timeout_auto_close ACK（满足"不触发兜底邮件/IM"但留痕来源）
 * - 用户点击"确认已收到" → 发 user_click ACK 并关闭
 * - 用户点击"查看详情" → 跳转到 /calendar?focusEventId={eventId} 并发 ACK
 *   （由 Calendar.tsx 解析 query string 自动 openEdit 指定事件）
 */
export default function GlobalCalendarReminderListener() {
  const { t } = useI18n();
  const { isAuthenticated } = useAuth();
  const [activeReminder, setActiveReminder] = useState<CalendarReminderNotification | null>(null);
  const [reminderAcking, setReminderAcking] = useState(false);
  const reminderTimerRef = useRef<number | null>(null);

  const handleReminderConfirm = useCallback(
    async (rem: CalendarReminderNotification, mode: 'user_click' | 'timeout_auto_close') => {
      setReminderAcking(true);
      try {
        await calendarApi.confirmReminder(rem.eventId, mode);
      } catch {
        // 静默吞掉：用户已确认，404/重复 ack 不影响体验
      } finally {
        setActiveReminder(null);
        setReminderAcking(false);
      }
    },
    [],
  );

  useEffect(() => {
    if (!isAuthenticated) return;

    // 弹层展示的 key：同一 eventId+sentAt 不重复开卡
    const seenKey = (p: CalendarReminderNotification) => `${p.eventId}__${p.sentAt ?? ''}`;
    const shown = new Set<string>();
    const onReminder = (raw: unknown) => {
      const payload = raw as CalendarReminderNotification;
      if (!payload || typeof payload.eventId !== 'number') return;
      const k = seenKey(payload);
      if (shown.has(k)) return;
      shown.add(k);
      setActiveReminder(payload);
      // 30 秒自动关闭 → ACK timeout_auto_close
      // M14: 不在 setState updater 内发副作用（StrictMode 下 updater 可能执行两次导致重复 ACK），
      // 直接在定时器回调中基于闭包 payload 发起确认。
      if (reminderTimerRef.current !== null) window.clearTimeout(reminderTimerRef.current);
      reminderTimerRef.current = window.setTimeout(() => {
        reminderTimerRef.current = null;
        void handleReminderConfirm(payload, 'timeout_auto_close');
      }, 30_000);
    };
    signalRService.on(SignalREvents.CalendarReminder, onReminder);
    return () => {
      signalRService.off(SignalREvents.CalendarReminder, onReminder);
      if (reminderTimerRef.current !== null) {
        window.clearTimeout(reminderTimerRef.current);
        reminderTimerRef.current = null;
      }
    };
  }, [isAuthenticated, handleReminderConfirm]);

  const handleViewDetails = useCallback(
    async (rem: CalendarReminderNotification) => {
      const targetUrl = `/calendar?focusEventId=${rem.eventId}`;
      setReminderAcking(true);
      // 先发送 ACK，等待一小段时间后跳转，确保请求有机会发出。
      // 若 ACK 失败（例如网络/409），也不阻塞跳转——降级为兜底邮件/IM提醒（可接受）。
      try {
        await Promise.race([
          calendarApi.confirmReminder(rem.eventId, 'user_click'),
          new Promise<void>((resolve) => window.setTimeout(resolve, 800)),
        ]);
      } catch {
        /* noop */
      } finally {
        setReminderAcking(false);
        setActiveReminder(null);
        window.location.assign(targetUrl);
      }
    },
    [],
  );

  if (!activeReminder) return null;

  return (
    <div className="fixed top-4 sm:top-6 right-4 sm:right-6 z-[70] w-[min(92vw,420px)] pointer-events-auto animate-[cal-pop-in_180ms_ease]">
      <div className="rounded-2xl shadow-2xl ring-1 ring-amber-200/60 dark:ring-amber-500/30 bg-gradient-to-br from-amber-50 to-white dark:from-amber-950/60 dark:to-slate-900 border border-amber-200/70 dark:border-amber-500/30 overflow-hidden">
        <div className="flex items-start gap-3 p-4">
          <div className="shrink-0 w-10 h-10 rounded-xl bg-amber-500/15 dark:bg-amber-500/25 flex items-center justify-center text-amber-600 dark:text-amber-400">
            <BellRing className="w-5 h-5" />
          </div>
          <div className="min-w-0 flex-1">
            <p className="text-xs font-semibold uppercase tracking-wider text-amber-600 dark:text-amber-400 mb-1">
              {t('calendar.shell.reminder.title')}
              {activeReminder.reminderMinutes != null && activeReminder.reminderMinutes >= 0
                ? t('calendar.shell.reminder.minutesAhead', { minutes: activeReminder.reminderMinutes, count: activeReminder.reminderMinutes })
                : ''}
            </p>
            <h3 className="font-semibold text-slate-900 dark:text-slate-100 break-words line-clamp-2">
              {activeReminder.title || t('calendar.shell.reminder.untitled')}
            </h3>
            <p className="mt-1.5 text-sm text-slate-600 dark:text-slate-300">
              {(() => {
                const s = new Date(activeReminder.startAt);
                if (isNaN(s.getTime())) return activeReminder.startAt;
                const ymd = `${s.getFullYear()}-${String(s.getMonth() + 1).padStart(2, '0')}-${String(s.getDate()).padStart(2, '0')}`;
                if (activeReminder.isAllDay) {
                  let endStr = ymd;
                  if (activeReminder.endAt) {
                    const e = new Date(activeReminder.endAt);
                    if (!isNaN(e.getTime()) && (e.getFullYear() !== s.getFullYear() || e.getMonth() !== s.getMonth() || e.getDate() !== s.getDate())) {
                      endStr += ` ~ ${e.getFullYear()}-${String(e.getMonth() + 1).padStart(2, '0')}-${String(e.getDate()).padStart(2, '0')}`;
                    }
                  }
                  return `${t('calendar.common.allDay')} · ${ymd}${endStr !== ymd ? ' → ' + endStr : ''}`;
                }
                const hm = `${String(s.getHours()).padStart(2, '0')}:${String(s.getMinutes()).padStart(2, '0')}`;
                let endHm = '';
                if (activeReminder.endAt) {
                  const e = new Date(activeReminder.endAt);
                  if (!isNaN(e.getTime())) endHm = ` – ${String(e.getHours()).padStart(2, '0')}:${String(e.getMinutes()).padStart(2, '0')}`;
                }
                return `${ymd}  ${hm}${endHm}`;
              })()}
            </p>
          </div>
          <button
            type="button"
            aria-label={t('calendar.shell.reminder.close')}
            onClick={() => {
              handleReminderConfirm(activeReminder, 'timeout_auto_close').catch(() => {
                /* noop */
              });
            }}
            disabled={reminderAcking}
            className="shrink-0 -my-1 -mr-1 w-8 h-8 rounded-lg flex items-center justify-center text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 hover:bg-slate-500/10 disabled:opacity-50 transition"
          >
            <XIcon className="w-4 h-4" />
          </button>
        </div>
        <div className="flex items-stretch border-t border-amber-200/70 dark:border-amber-500/20 bg-white/60 dark:bg-slate-950/30">
          <button
            type="button"
            disabled={reminderAcking}
            onClick={() => {
              void handleReminderConfirm(activeReminder, 'user_click');
            }}
            className="flex-1 inline-flex items-center justify-center gap-1.5 px-4 py-2.5 text-sm font-medium text-emerald-700 dark:text-emerald-300 hover:bg-emerald-500/10 disabled:opacity-50 transition"
          >
            <CheckCircle2 className="w-4 h-4" />
            {reminderAcking ? t('calendar.shell.reminder.confirming') : t('calendar.shell.reminder.confirmReceived')}
          </button>
          <div className="w-px bg-amber-200/70 dark:bg-amber-500/20" />
          <button
            type="button"
            onClick={() => {
              handleViewDetails(activeReminder);
            }}
            className="flex-1 inline-flex items-center justify-center gap-1.5 px-4 py-2.5 text-sm font-medium text-sky-700 dark:text-sky-300 hover:bg-sky-500/10 disabled:opacity-50 transition"
          >
            <CalendarCheck className="w-4 h-4" />
            {t('calendar.shell.reminder.viewDetails')}
          </button>
        </div>
      </div>
    </div>
  );
}
