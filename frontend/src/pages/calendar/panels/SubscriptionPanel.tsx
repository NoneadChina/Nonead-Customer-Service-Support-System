import { useEffect, useCallback, useRef } from 'react';
import { useCalendarStore, getErrorMessage } from '../store';
import { calendarApi } from '../../../api/modules/engagement';
import { useI18n } from '../../../context/I18nContext';
import { Icon, Overlay, ModalHeader, shallowEq } from './shared';

/* =========================================================
   SubscriptionPanel —— 日历订阅面板
   功能：展示/复制/重新生成 iCal 订阅链接
   主题：蓝/青渐变装饰条
   ========================================================= */

export function SubscriptionPanel() {
  const { showSubscription, subscriptionUrl, subscriptionLoading, copied } = useCalendarStore(
    (st) => ({ showSubscription: st.showSubscription, subscriptionUrl: st.subscriptionUrl, subscriptionLoading: st.subscriptionLoading, copied: st.copied }),
    shallowEq
  );

  const { t } = useI18n();
  const timersRef = useRef<number[]>([]);
  useEffect(() => {
    return () => {
      for (const t of timersRef.current) window.clearTimeout(t);
      timersRef.current = [];
    };
  }, []);
  const safeSetTimeout = useCallback((fn: () => void, ms: number) => {
    const id = window.setTimeout(() => {
      timersRef.current = timersRef.current.filter((x) => x !== id);
      fn();
    }, ms);
    timersRef.current.push(id);
    return id;
  }, []);

  const close = useCallback(() => useCalendarStore.setState({ showSubscription: false }), []);
  // P0 修复：不再使用 mock URL fallback，避免用户复制到无效的占位链接。
  // 没有真实订阅链接时显示空字符串，由 UI 渲染"尚未生成"空状态卡片 + 引导按钮。
  const displayUrl = subscriptionUrl || '';
  const hasUrl = displayUrl.length > 0;

  const onCopy = useCallback(async () => {
    if (!hasUrl) return;
    const showToast = useCalendarStore.getState().showTransientToast;
    try {
      await navigator.clipboard.writeText(displayUrl);
      showToast(t('calendar.panels.subscription.copiedToast'), 'success', 2200);
    } catch {
      try {
        const ta = document.createElement('textarea'); ta.value = displayUrl; document.body.appendChild(ta); ta.select(); document.execCommand('copy'); document.body.removeChild(ta);
        showToast(t('calendar.panels.subscription.copiedFallbackToast'), 'success', 2200);
      } catch {
        showToast(t('calendar.panels.subscription.copyFailedToast'), 'error', 2800);
        return;
      }
    }
    useCalendarStore.setState({ copied: true });
    safeSetTimeout(() => useCalendarStore.setState({ copied: false }), 2000);
  }, [displayUrl, hasUrl, safeSetTimeout, t]);

  const onRegen = useCallback(async () => {
    useCalendarStore.setState({ subscriptionLoading: true });
    const showToast = useCalendarStore.getState().showTransientToast;
    try {
      const result = await calendarApi.regenerateSubscription();
      const newUrl = result?.url || '';
      useCalendarStore.setState({ subscriptionLoading: false, subscriptionUrl: newUrl });
      showToast(t('calendar.panels.subscription.regeneratedToast'), 'success', 2600);
    } catch (e: unknown) {
      useCalendarStore.setState({ subscriptionLoading: false });
      showToast(getErrorMessage(e) || t('calendar.panels.subscription.regenerateFailedToast'), 'error', 2800);
    }
  }, [t]);

  if (!showSubscription) return null;
  return (
    <Overlay onClose={close} w="max-w-2xl">
      <ModalHeader
        title={t('calendar.panels.subscription.title')}
        subtitle={t('calendar.panels.subscription.subtitle')}
        onClose={close}
        icon={<Icon name="link" className="w-5 h-5" />}
        theme="subscription"
      />
      <div className="flex-1 overflow-auto p-6 space-y-5">
        {/* 使用说明卡片 */}
        <div className="rounded-2xl border border-sky-100 dark:border-sky-900/30 bg-gradient-to-br from-sky-50/70 via-white to-emerald-50/50 dark:from-sky-950/20 dark:via-slate-800/30 dark:to-emerald-950/20 p-4 shadow-sm">
          <div className="flex items-start gap-3">
            <div className="w-10 h-10 rounded-xl bg-gradient-to-br from-sky-100 to-emerald-100 dark:from-sky-900/40 dark:to-emerald-900/30 flex items-center justify-center shrink-0 shadow-sm">
              <Icon name="info" className="w-5 h-5 text-sky-600 dark:text-sky-300" />
            </div>
            <div className="flex-1 space-y-1">
              <div className="text-sm font-bold text-slate-800 dark:text-slate-100 flex items-center gap-2">
                {t('calendar.panels.subscription.autoSyncTitle')}
                <span className="inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-semibold bg-sky-100 dark:bg-sky-900/40 text-sky-700 dark:text-sky-300">{t('calendar.panels.subscription.standardBadge')}</span>
              </div>
              <ul className="space-y-1 text-xs text-slate-600 dark:text-slate-400">
                <li className="flex items-start gap-1.5">
                  <Icon name="check" className="w-3 h-3 text-emerald-500 mt-0.5 shrink-0" />
                  <span>{t('calendar.panels.subscription.bullet1')}</span>
                </li>
                <li className="flex items-start gap-1.5">
                  <Icon name="check" className="w-3 h-3 text-emerald-500 mt-0.5 shrink-0" />
                  <span>{t('calendar.panels.subscription.bullet2')}</span>
                </li>
                <li className="flex items-start gap-1.5">
                  <Icon name="check" className="w-3 h-3 text-emerald-500 mt-0.5 shrink-0" />
                  <span>{t('calendar.panels.subscription.bullet3')}</span>
                </li>
              </ul>
            </div>
          </div>
        </div>

        {/* 订阅链接区域 */}
        <div className="rounded-2xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 p-4 shadow-md shadow-slate-900/5 space-y-3">
          <label className="flex items-center gap-2 text-sm font-bold text-slate-700 dark:text-slate-200">
            <div className="w-7 h-7 rounded-lg bg-gradient-to-br from-sky-100 to-emerald-100 dark:from-sky-900/40 dark:to-emerald-900/30 flex items-center justify-center">
              <Icon name="link" className="w-3.5 h-3.5 text-sky-600 dark:text-sky-300" />
            </div>
            {t('calendar.panels.subscription.linkLabel')}
          </label>
          {hasUrl ? (
            <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-2">
              <div className="relative flex-1 min-w-0">
                <div className="absolute inset-y-0 left-0 flex items-center pl-3 pointer-events-none opacity-40">
                  <Icon name="calendar" className="w-4 h-4 text-sky-600 dark:text-sky-300" />
                </div>
                <input
                  readOnly
                  value={displayUrl}
                  onClick={(e) => (e.target as HTMLInputElement).select()}
                  className="w-full pl-9 pr-10 py-3 text-sm border border-slate-200 dark:border-slate-600 bg-gradient-to-r from-slate-50 to-white dark:from-slate-900 dark:to-slate-800 text-slate-700 dark:text-slate-200 rounded-xl font-mono text-xs focus:outline-none focus:ring-2 focus:ring-sky-500/40 focus:border-sky-400 shadow-inner cursor-text"
                />
                {subscriptionLoading && (
                  <div className="absolute right-3 top-1/2 -translate-y-1/2">
                    <Icon name="spinner" className="w-4 h-4 text-sky-500" />
                  </div>
                )}
              </div>
              <div className="flex items-center gap-2 shrink-0">
                <button
                  onClick={onCopy}
                  disabled={subscriptionLoading}
                  className={`inline-flex items-center gap-1.5 px-4 py-3 text-sm font-semibold rounded-xl transition-all whitespace-nowrap disabled:opacity-50 shadow-md ${
                    copied
                      ? 'bg-gradient-to-r from-emerald-500 to-teal-600 text-white shadow-emerald-500/30 hover:shadow-lg hover:shadow-emerald-500/40'
                      : 'bg-gradient-to-r from-sky-500 to-blue-600 hover:from-sky-600 hover:to-blue-700 text-white shadow-sky-500/30 hover:shadow-lg hover:shadow-sky-500/40'
                  }`}
                >
                  {copied
                    ? <><Icon name="check" className="w-4 h-4" />{t('calendar.panels.subscription.copiedLabel')}</>
                    : <><Icon name="copy" className="w-4 h-4" />{t('calendar.panels.subscription.copyLink')}</>
                  }
                </button>
                <button
                  onClick={onRegen}
                  disabled={subscriptionLoading}
                  className="inline-flex items-center gap-1.5 px-4 py-3 text-sm font-semibold text-slate-700 dark:text-slate-200 bg-white dark:bg-slate-700 border border-slate-200 dark:border-slate-600 rounded-xl hover:bg-slate-50 dark:hover:bg-slate-600 hover:shadow-md transition-all disabled:opacity-50"
                >
                  <Icon name="refresh" className="w-4 h-4" />
                  {t('calendar.panels.subscription.regenerate')}
                </button>
              </div>
            </div>
          ) : (
            /* 空状态：尚未生成订阅链接，引导用户点击「立即生成」 */
            <div className="flex flex-col items-center justify-center py-8 px-4 text-center rounded-xl border border-dashed border-slate-200 dark:border-slate-700 bg-slate-50/40 dark:bg-slate-900/30">
              {subscriptionLoading ? (
                <div className="flex items-center gap-2 text-slate-500 dark:text-slate-400">
                  <Icon name="spinner" className="w-5 h-5 text-sky-500" />
                  <span className="text-sm">{t('calendar.panels.subscription.loadingLink')}</span>
                </div>
              ) : (
                <>
                  <div className="w-12 h-12 rounded-full bg-gradient-to-br from-sky-100 to-emerald-100 dark:from-sky-900/40 dark:to-emerald-900/30 flex items-center justify-center mb-3 shadow-sm">
                    <Icon name="link" className="w-6 h-6 text-sky-500 dark:text-sky-300" />
                  </div>
                  <div className="text-sm font-semibold text-slate-700 dark:text-slate-200">{t('calendar.panels.subscription.emptyTitle')}</div>
                  <p className="mt-1 text-xs text-slate-500 dark:text-slate-400 max-w-xs">
                    {t('calendar.panels.subscription.emptyHint')}
                  </p>
                  <button
                    onClick={onRegen}
                    disabled={subscriptionLoading}
                    className="mt-4 inline-flex items-center gap-1.5 px-5 py-2.5 text-sm font-semibold text-white bg-gradient-to-r from-sky-500 to-blue-600 hover:from-sky-600 hover:to-blue-700 rounded-xl shadow-md shadow-sky-500/30 hover:shadow-lg hover:shadow-sky-500/40 transition-all disabled:opacity-50"
                  >
                    <Icon name="refresh" className="w-4 h-4" />
                    {t('calendar.panels.subscription.generateLink')}
                  </button>
                </>
              )}
            </div>
          )}
        </div>
      </div>
    </Overlay>
  );
}
