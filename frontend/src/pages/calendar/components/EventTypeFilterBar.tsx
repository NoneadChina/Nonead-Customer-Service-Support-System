import { useCallback } from 'react';
import { EVENT_TYPE_STYLE } from '../calendarUtils';
import { useCalendarStore } from '../store';
import { useI18n } from '../../../context/I18nContext';

/** P1-A#3: 统一的「事件类型筛选」按钮行。
 *
 * 完全受控式 Props（enabledEventTypes/onToggleEventType/onResetAll）
 * 留空则内部订阅 CalendarStore，月/周/天 三视图零参即可使用。
 */
export interface EventTypeFilterBarProps {
  enabledEventTypes?: ReadonlySet<string>;
  onToggleEventType?: (typeKey: string) => void;
  onResetAll?: () => void;
  className?: string;
}

export function EventTypeFilterBar({
  enabledEventTypes,
  onToggleEventType,
  onResetAll,
  className,
}: EventTypeFilterBarProps) {
  const { t } = useI18n();
  const storeEnabled = useCalendarStore((s) => s.enabledEventTypes);
  const storeToggle = useCalendarStore((s) => s.toggleEventTypeFilter);
  const storeReset = useCallback(() => useCalendarStore.getState().setAllEventTypesEnabled(), []);

  const enabled = enabledEventTypes ?? storeEnabled;
  const toggle = onToggleEventType ?? storeToggle;
  const reset = onResetAll ?? storeReset;

  return (
    <div className={['flex items-center gap-1.5 flex-wrap', className ?? ''].join(' ').trim()}>
      {Object.entries(EVENT_TYPE_STYLE).map(([key, style]) => {
        const isEnabled = enabled.size === 0 || enabled.has(key);
        return (
          <button
            key={key}
            type="button"
            onClick={() => toggle(key)}
            className={[
              'flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium transition-all',
              isEnabled
                ? `${style.badgeClass} opacity-100`
                : 'bg-slate-100 dark:bg-slate-800 text-slate-400 dark:text-slate-500 opacity-50 hover:opacity-70',
            ].join(' ')}
          >
            <span className={`w-2 h-2 rounded-full ${style.dotClass}`} />
            {style.label}
          </button>
        );
      })}
      {enabled.size > 0 && (
        <button
          type="button"
          onClick={reset}
          className="px-2 py-0.5 rounded-full text-[11px] text-sky-600 dark:text-sky-400 hover:bg-sky-50 dark:hover:bg-sky-950/40 transition-colors"
        >
          {t('calendar.shell.filter.all')}
        </button>
      )}
    </div>
  );
}
