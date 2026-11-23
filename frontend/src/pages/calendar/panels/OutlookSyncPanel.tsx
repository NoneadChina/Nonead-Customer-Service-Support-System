import { useCallback, useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { useCalendarStore } from '../store';
import { Icon, Overlay, ModalHeader, ToggleSwitch, shallowEq } from './shared';
import { outlookSyncApi } from '../../../api/modules/engagement';
import { useI18n } from '../../../context/I18nContext';
import { toIntlLocale } from '../../../i18n/locale';
import type { TranslationKey } from '../../../locales/zh-Hans';
import type {
  OutlookAdminSettingsDto,
  UpdateOutlookAdminSettingsRequest,
} from '../../../api/types';

/* =========================================================
   OutlookSyncPanel —— Outlook 同步面板
   功能：OAuth 连接/断开、同步方向、自动同步开关、立即同步、结果展示
   T000099: 每个用户可在面板内自配置自己的 Azure AD 凭据（ClientId/ClientSecret/TenantId/RedirectUri/Scopes）
   主题：蓝/靛渐变装饰条
   ========================================================= */

type OutlookSyncDirT = 'noneads_to_outlook' | 'outlook_to_noneads' | 'bidirectional';

/** 同步方向选项。字段持 key 而非文案：漏包 t() 的使用点会以 TS2339 暴露（labelKey / descKey） */
const DIR_OPTIONS: { value: OutlookSyncDirT; labelKey: TranslationKey; descKey: TranslationKey; icon: 'upload' | 'download' | 'sync' }[] = [
  { value: 'noneads_to_outlook', labelKey: 'calendar.panels.outlook.dirPushOnlyLabel', descKey: 'calendar.panels.outlook.dirPushOnlyDesc', icon: 'upload' },
  { value: 'outlook_to_noneads', labelKey: 'calendar.panels.outlook.dirPullOnlyLabel', descKey: 'calendar.panels.outlook.dirPullOnlyDesc', icon: 'download' },
  { value: 'bidirectional',       labelKey: 'calendar.panels.outlook.dirBothLabel',    descKey: 'calendar.panels.outlook.dirBothDesc',    icon: 'sync' },
];

/**
 * 后端 `OutlookAdminSettingsDto.Source` 的产出码（`OutlookSyncController.cs:382` 产出
 * `user` / `appsettings`）→ 前端词条。**按码取词，不直出后端值**。
 *
 * 用 `TranslationKey | undefined` 而非 `TranslationKey`：本仓未开 `noUncheckedIndexedAccess`，
 * 后者会让"未知码"被类型系统谎称为一定有值。
 */
const SOURCE_LABEL: Record<string, TranslationKey | undefined> = {
  user: 'calendar.panels.outlook.sourceUser',
  appsettings: 'calendar.panels.outlook.sourceAppsettings',
};

export function OutlookSyncPanel() {
  const s = useCalendarStore((st) => ({
    showOutlookSync: st.showOutlookSync, outlookStatus: st.outlookStatus, outlookStatusLoading: st.outlookStatusLoading,
    outlookConnecting: st.outlookConnecting, outlookSyncing: st.outlookSyncing, outlookSaving: st.outlookSaving,
    outlookDisconnecting: st.outlookDisconnecting, outlookDirection: st.outlookDirection, outlookSyncEnabled: st.outlookSyncEnabled,
    outlookSyncResult: st.outlookSyncResult,
    // P0 修复：从 mock 改为调用真实后端 API
    fetchOutlookStatus: st.fetchOutlookStatus, connectOutlook: st.connectOutlook,
    disconnectOutlook: st.disconnectOutlook, saveOutlookSetting: st.saveOutlookSetting,
    syncOutlookNow: st.syncOutlookNow,
  }), shallowEq);

  const { t, language } = useI18n();

  const close = useCallback(() => useCalendarStore.setState({ showOutlookSync: false }), []);

  // P0 修复：不再使用 mock 数据，直接调用后端 API
  const onConnect = useCallback(() => { void s.connectOutlook(); }, [s]);
  const onDisconnect = useCallback(() => { void s.disconnectOutlook(); }, [s]);
  const onDirNoneadsToOutlook = useCallback(() => useCalendarStore.setState({ outlookDirection: 'noneads_to_outlook' }), []);
  const onDirOutlookToNoneads = useCallback(() => useCalendarStore.setState({ outlookDirection: 'outlook_to_noneads' }), []);
  const onDirBidirectional = useCallback(() => useCalendarStore.setState({ outlookDirection: 'bidirectional' }), []);
  const onToggle = useCallback(() => useCalendarStore.setState((st) => ({ outlookSyncEnabled: !st.outlookSyncEnabled })), []);
  const onSave = useCallback(() => { void s.saveOutlookSetting(); }, [s]);
  const onSync = useCallback(() => { void s.syncOutlookNow(); }, [s]);

  // T000099: Azure AD 凭据配置模态窗状态
  const [showAdminConfig, setShowAdminConfig] = useState(false);

  if (!s.showOutlookSync) return null;
  const connected = !!s.outlookStatus?.isConnected;
  const lastSyncAt = s.outlookStatus?.lastSyncAt;
  // T000099: 后端通过 IsConfigured 标识 Azure AD 凭据是否已配置。
  // 未配置时禁用连接按钮并提示用户配置自己的 Azure AD 凭据，避免点击后返回 400 错误。
  const isConfigured = s.outlookStatus?.isConfigured !== false;

  const dirHandlers: Record<OutlookSyncDirT, () => void> = {
    noneads_to_outlook: onDirNoneadsToOutlook,
    outlook_to_noneads: onDirOutlookToNoneads,
    bidirectional: onDirBidirectional,
  };

  return (
    <Overlay onClose={close} w="max-w-2xl">
      <ModalHeader
        title={t('calendar.common.outlookSync')}
        subtitle={connected ? `${t('calendar.panels.outlook.connectedPrefix')}${s.outlookStatus?.outlookEmail ?? ''}` : t('calendar.panels.outlook.notConnected')}
        onClose={close}
        icon={<Icon name="mail" className="w-5 h-5" />}
        theme="outlook"
      />
      <div className="flex-1 overflow-auto p-6 space-y-5">
        {s.outlookStatusLoading ? (
          <div className="flex items-center justify-center py-12 text-slate-500 dark:text-slate-400">
            <Icon name="spinner" className="w-5 h-5 mr-2" />{t('calendar.panels.outlook.loadingStatus')}
          </div>
        ) : (
          <>
            {/* 连接状态卡片 */}
            <div className={`rounded-2xl border p-5 shadow-md ${connected
              ? 'border-emerald-200/60 dark:border-emerald-800/40 bg-gradient-to-br from-emerald-50/70 via-white to-teal-50/60 dark:from-emerald-950/20 dark:via-slate-800/30 dark:to-teal-950/20'
              : 'border-slate-200 dark:border-slate-700 bg-gradient-to-br from-slate-50/60 via-white to-slate-100/50 dark:from-slate-800/30 dark:via-slate-900 dark:to-slate-800/30'
            }`}>
              <div className="flex items-start justify-between gap-3">
                <div className="flex items-start gap-3">
                  <div className={`flex-shrink-0 w-12 h-12 rounded-2xl flex items-center justify-center shadow-md ring-2 ring-white dark:ring-slate-800 ${connected
                    ? 'bg-gradient-to-br from-emerald-400 to-teal-500 text-white'
                    : 'bg-gradient-to-br from-slate-200 to-slate-300 dark:from-slate-600 dark:to-slate-700 text-slate-500 dark:text-slate-300'
                  }`}>
                    {connected ? <Icon name="checkCircle" className="w-6 h-6" /> : <Icon name="mail" className="w-6 h-6" />}
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="text-base font-bold text-slate-800 dark:text-slate-100 truncate">
                      {connected ? `${t('calendar.panels.outlook.connectedPrefix')}${s.outlookStatus?.outlookEmail ?? ''}` : t('calendar.panels.outlook.notConnected')}
                    </div>
                    <div className="text-xs text-slate-500 dark:text-slate-400 mt-1 flex items-center gap-1.5">
                      {connected ? (
                        <>
                          <span className="inline-block w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse" />
                          {t('calendar.panels.outlook.connectedHint')}
                        </>
                      ) : (
                        <>
                          <Icon name="info" className="w-3.5 h-3.5 opacity-70" />
                          {t('calendar.panels.outlook.notConnectedHint')}
                        </>
                      )}
                    </div>
                  </div>
                </div>
                {connected && s.outlookStatus?.isSyncing && (
                  <span className="flex items-center gap-1.5 text-xs font-semibold text-blue-600 dark:text-blue-400 px-2.5 py-1 rounded-lg bg-blue-50 dark:bg-blue-900/20 border border-blue-100 dark:border-blue-800/40">
                    <Icon name="spinner" className="w-3 h-3" />{t('calendar.panels.outlook.syncing')}
                  </span>
                )}
              </div>
            </div>

            {/* T000099: 用户级 Azure AD 凭据配置入口（未连接状态下显示，所有用户可见） */}
            {!connected && (
              <div className="flex justify-end">
                <button
                  onClick={() => setShowAdminConfig(true)}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold text-indigo-600 dark:text-indigo-300 bg-indigo-50 dark:bg-indigo-900/20 border border-indigo-200 dark:border-indigo-800/40 rounded-lg hover:bg-indigo-100 dark:hover:bg-indigo-900/30 transition-colors"
                >
                  <Icon name="shield" className="w-3.5 h-3.5" />{t('calendar.panels.outlook.configureCredentials')}
                </button>
              </div>
            )}

            {!connected ? (
              <div className="space-y-4">
                {/* 授权说明卡片 */}
                <div className="rounded-2xl bg-gradient-to-br from-blue-50/70 via-white to-indigo-50/60 dark:from-blue-950/20 dark:via-slate-800/30 dark:to-indigo-950/20 border border-blue-200 dark:border-blue-900/40 p-5 space-y-3 shadow-sm">
                  <div className="flex items-center gap-2 text-sm font-bold text-blue-700 dark:text-blue-300">
                    <div className="w-8 h-8 rounded-xl bg-gradient-to-br from-blue-100 to-indigo-100 dark:from-blue-900/40 dark:to-indigo-900/30 flex items-center justify-center">
                      <Icon name="zap" className="w-4 h-4 text-blue-600 dark:text-blue-300" />
                    </div>
                    {t('calendar.panels.outlook.capabilitiesTitle')}
                  </div>
                  <ul className="space-y-2 text-sm text-slate-600 dark:text-slate-300">
                    <li className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded-md bg-blue-100 dark:bg-blue-900/40 text-blue-600 dark:text-blue-300 flex items-center justify-center shrink-0 mt-0.5">
                        <Icon name="upload" className="w-3 h-3" />
                      </div>
                      <span><b className="text-slate-800 dark:text-slate-100">{t('calendar.panels.outlook.pushBold')}</b>{t('calendar.panels.outlook.pushDesc')}</span>
                    </li>
                    <li className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded-md bg-blue-100 dark:bg-blue-900/40 text-blue-600 dark:text-blue-300 flex items-center justify-center shrink-0 mt-0.5">
                        <Icon name="download" className="w-3 h-3" />
                      </div>
                      <span><b className="text-slate-800 dark:text-slate-100">{t('calendar.panels.outlook.pullBold')}</b>{t('calendar.panels.outlook.pullDesc')}</span>
                    </li>
                    <li className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded-md bg-blue-100 dark:bg-blue-900/40 text-blue-600 dark:text-blue-300 flex items-center justify-center shrink-0 mt-0.5">
                        <Icon name="shield" className="w-3 h-3" />
                      </div>
                      <span><b className="text-slate-800 dark:text-slate-100">{t('calendar.panels.outlook.conflictBold')}</b>{t('calendar.panels.outlook.conflictDesc')}</span>
                    </li>
                  </ul>
                </div>
                {/* T000099: Azure AD 未配置时显示警告并禁用连接按钮 */}
                {!isConfigured && (
                  <div className="rounded-2xl border border-amber-200 dark:border-amber-800/40 bg-gradient-to-br from-amber-50/80 via-white to-orange-50/70 dark:from-amber-950/20 dark:via-slate-800/30 dark:to-orange-950/20 p-4 shadow-sm">
                    <div className="flex items-start gap-3">
                      <div className="w-9 h-9 rounded-xl bg-gradient-to-br from-amber-400 to-orange-500 text-white flex items-center justify-center shrink-0 shadow-md">
                        <Icon name="alertCircle" className="w-5 h-5" />
                      </div>
                      <div className="min-w-0 flex-1">
                        <div className="text-sm font-bold text-amber-700 dark:text-amber-300">{t('calendar.panels.outlook.notEnabledTitle')}</div>
                        <div className="text-xs text-amber-600/90 dark:text-amber-400/80 mt-1 leading-relaxed">
                          {t('calendar.panels.outlook.azureAdHint')}
                          <code className="mx-1 px-1.5 py-0.5 rounded bg-amber-100 dark:bg-amber-900/40 text-amber-700 dark:text-amber-300 text-[11px] font-mono">AzureAd__ClientId</code>{t('calendar.panels.outlook.codeSeparator')}
                          <code className="mx-1 px-1.5 py-0.5 rounded bg-amber-100 dark:bg-amber-900/40 text-amber-700 dark:text-amber-300 text-[11px] font-mono">AzureAd__ClientSecret</code>{t('calendar.panels.outlook.codeSeparator')}
                          <code className="mx-1 px-1.5 py-0.5 rounded bg-amber-100 dark:bg-amber-900/40 text-amber-700 dark:text-amber-300 text-[11px] font-mono">AzureAd__RedirectUri</code>{t('calendar.panels.outlook.codeTerminator')}
                        </div>
                      </div>
                    </div>
                  </div>
                )}
                <button
                  onClick={onConnect}
                  disabled={s.outlookConnecting || !isConfigured}
                  className="w-full inline-flex items-center justify-center gap-2 px-5 py-3.5 text-sm font-semibold text-white bg-gradient-to-r from-blue-500 via-indigo-500 to-violet-600 hover:from-blue-600 hover:via-indigo-600 hover:to-violet-700 rounded-2xl shadow-xl shadow-blue-500/30 hover:shadow-2xl hover:shadow-blue-500/40 transition-all disabled:opacity-60 disabled:cursor-not-allowed"
                  title={isConfigured ? '' : t('calendar.panels.outlook.connectDisabledTitle')}
                >
                  {s.outlookConnecting
                    ? <><Icon name="spinner" className="w-4 h-4" />{t('calendar.panels.outlook.connecting')}</>
                    : <><Icon name="mail" className="w-4 h-4" />{isConfigured ? t('calendar.panels.outlook.connectOAuth') : t('calendar.panels.outlook.notConfiguredShort')}</>
                  }
                </button>
              </div>
            ) : (
              <div className="space-y-4">
                {/* 同步方向 */}
                <div className="rounded-2xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 p-4 shadow-sm space-y-3">
                  <label className="flex items-center gap-2 text-sm font-bold text-slate-700 dark:text-slate-200">
                    <div className="w-7 h-7 rounded-lg bg-gradient-to-br from-blue-100 to-indigo-100 dark:from-blue-900/40 dark:to-indigo-900/30 flex items-center justify-center">
                      <Icon name="arrows" className="w-3.5 h-3.5 text-blue-600 dark:text-blue-300" />
                    </div>
                    {t('calendar.panels.outlook.directionLabel')}
                  </label>
                  <div className="grid grid-cols-3 gap-2">
                    {DIR_OPTIONS.map((opt) => {
                      const active = s.outlookDirection === opt.value;
                      return (
                        <button
                          key={opt.value}
                          onClick={dirHandlers[opt.value]}
                          className={`group relative px-3 py-3 text-sm rounded-2xl border transition-all duration-200 ${active
                            ? 'border-blue-500 bg-gradient-to-br from-blue-50 via-white to-indigo-50 dark:from-blue-900/30 dark:via-slate-800 dark:to-indigo-900/20 text-blue-700 dark:text-blue-300 ring-2 ring-blue-500/30 shadow-md shadow-blue-500/10 scale-[1.02]'
                            : 'border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-800 text-slate-600 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700 hover:shadow-sm'
                          }`}
                        >
                          <div className="flex flex-col items-center gap-1.5">
                            <div className={`w-9 h-9 rounded-xl flex items-center justify-center transition-all ${active
                              ? 'bg-gradient-to-br from-blue-500 to-indigo-500 text-white shadow-md shadow-blue-500/30'
                              : 'bg-slate-100 dark:bg-slate-700 text-slate-500 dark:text-slate-300 group-hover:bg-slate-200 dark:group-hover:bg-slate-600'
                            }`}>
                              <Icon name={opt.icon} className="w-4 h-4" />
                            </div>
                            <div className="font-bold">{t(opt.labelKey)}</div>
                            <div className={`text-[10px] ${active ? 'text-blue-600/80 dark:text-blue-300/80' : 'text-slate-400 dark:text-slate-500'}`}>{t(opt.descKey)}</div>
                          </div>
                        </button>
                      );
                    })}
                  </div>
                </div>

                {/* 自动同步开关 */}
                <label className="flex items-center justify-between rounded-2xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 px-5 py-4 cursor-pointer shadow-sm hover:shadow-md transition-shadow">
                  <div className="flex items-start gap-3">
                    <div className="w-9 h-9 rounded-xl bg-gradient-to-br from-slate-100 to-slate-200 dark:from-slate-700 dark:to-slate-600 flex items-center justify-center shrink-0">
                      <Icon name="bell" className="w-4 h-4 text-slate-600 dark:text-slate-300" />
                    </div>
                    <div className="min-w-0">
                      <div className="text-sm font-bold text-slate-700 dark:text-slate-200">{t('calendar.panels.outlook.autoSyncTitle')}</div>
                      <div className="text-xs text-slate-400 dark:text-slate-500 mt-0.5">{t('calendar.panels.outlook.autoSyncHint')}</div>
                    </div>
                  </div>
                  <ToggleSwitch
                    checked={s.outlookSyncEnabled}
                    onChange={onToggle}
                    ariaLabel={t('calendar.panels.outlook.autoSyncTitle')}
                  />
                </label>

                {/* 上次同步时间 */}
                <div className="flex items-center gap-3 rounded-2xl bg-slate-50 dark:bg-slate-800/50 border border-slate-200 dark:border-slate-700/60 px-5 py-3 text-sm shadow-sm">
                  <div className="w-8 h-8 rounded-lg bg-gradient-to-br from-slate-100 to-slate-200 dark:from-slate-700 dark:to-slate-600 flex items-center justify-center shrink-0">
                    <Icon name="clock" className="w-4 h-4 text-slate-500 dark:text-slate-300" />
                  </div>
                  <div className="flex-1 text-slate-600 dark:text-slate-400">
                    {t('calendar.panels.outlook.lastSyncLabel')}
                    {lastSyncAt
                      ? <span className="font-bold text-slate-800 dark:text-slate-200 ml-1">{new Date(lastSyncAt).toLocaleString(toIntlLocale(language))}</span>
                      : <span className="text-slate-400 dark:text-slate-500 ml-1">{t('calendar.panels.outlook.neverSynced')}</span>
                    }
                  </div>
                </div>

                {/* 操作按钮 */}
                <div className="flex flex-wrap items-center gap-2">
                  <button
                    onClick={onSave}
                    disabled={s.outlookSaving}
                    className="inline-flex items-center gap-1.5 px-4 py-2.5 text-sm font-semibold text-white bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-700 hover:to-indigo-700 rounded-xl shadow-md shadow-blue-500/25 disabled:opacity-50 transition-all"
                  >
                    {s.outlookSaving ? <Icon name="spinner" className="w-4 h-4" /> : <Icon name="check" className="w-4 h-4" />}{t('calendar.panels.outlook.saveSettings')}
                  </button>
                  <button
                    onClick={onSync}
                    disabled={s.outlookSyncing}
                    className="inline-flex items-center gap-1.5 px-4 py-2.5 text-sm font-semibold text-white bg-gradient-to-r from-emerald-500 via-teal-500 to-green-600 hover:from-emerald-600 hover:via-teal-600 hover:to-green-700 rounded-xl shadow-lg shadow-emerald-500/30 hover:shadow-xl hover:shadow-emerald-500/40 disabled:opacity-50 transition-all"
                  >
                    {s.outlookSyncing ? <Icon name="spinner" className="w-4 h-4" /> : <Icon name="refresh" className="w-4 h-4" />}{t('calendar.panels.outlook.syncNow')}
                  </button>
                  {/* T000099: 用户级 Azure AD 凭据配置入口（已连接状态下显示，所有用户可见） */}
                  <button
                    onClick={() => setShowAdminConfig(true)}
                    className="inline-flex items-center gap-1.5 px-4 py-2.5 text-sm font-semibold text-indigo-600 dark:text-indigo-300 bg-white dark:bg-slate-800 border border-indigo-200 dark:border-indigo-900/40 rounded-xl hover:bg-indigo-50 dark:hover:bg-indigo-900/20 transition-all"
                  >
                    <Icon name="shield" className="w-4 h-4" />{t('calendar.panels.outlook.configureCredentials')}
                  </button>
                </div>

                {/* 同步结果面板 */}
                {s.outlookSyncResult && (
                  <div className={`rounded-2xl border p-5 shadow-md ${s.outlookSyncResult.success
                    ? 'border-emerald-200 dark:border-emerald-800/40 bg-gradient-to-br from-emerald-50/70 via-white to-teal-50/50 dark:from-emerald-950/20 dark:via-slate-800/30 dark:to-teal-950/20'
                    : 'border-red-200 dark:border-red-800/40 bg-gradient-to-br from-red-50/70 via-white to-rose-50/50 dark:from-red-950/20 dark:via-slate-800/30 dark:to-rose-950/20'
                  }`}>
                    <div className="flex items-center gap-2 mb-3">
                      <div className={`w-9 h-9 rounded-xl flex items-center justify-center shadow-sm ${s.outlookSyncResult.success
                        ? 'bg-gradient-to-br from-emerald-500 to-teal-500 text-white'
                        : 'bg-gradient-to-br from-red-500 to-rose-500 text-white'
                      }`}>
                        {s.outlookSyncResult.success ? <Icon name="checkCircle" className="w-5 h-5" /> : <Icon name="alertCircle" className="w-5 h-5" />}
                      </div>
                      <div className="text-base font-bold text-slate-800 dark:text-slate-100">
                        {s.outlookSyncResult.success ? t('calendar.panels.outlook.syncComplete') : t('calendar.common.syncFailed')}
                      </div>
                      {s.outlookSyncResult.success && (
                        <span className="ml-auto text-xs text-slate-500 dark:text-slate-400 inline-flex items-center gap-1">
                          <Icon name="clock" className="w-3 h-3" />
                          {t('calendar.panels.outlook.durationLabel')}{(s.outlookSyncResult.durationMs ?? 0) / 1000} s
                        </span>
                      )}
                    </div>
                    {s.outlookSyncResult.success ? (
                      <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
                        {([
                          { labelKey: 'calendar.panels.outlook.metricPushed', val: s.outlookSyncResult.pushedToOutlook ?? 0, tint: 'from-sky-500 to-blue-600' },
                          { labelKey: 'calendar.panels.outlook.metricPulled', val: s.outlookSyncResult.pulledFromOutlook ?? 0, tint: 'from-violet-500 to-indigo-600' },
                          { labelKey: 'calendar.panels.outlook.metricUpdated', val: s.outlookSyncResult.updated ?? 0, tint: 'from-emerald-500 to-teal-600' },
                          { labelKey: 'calendar.panels.outlook.metricConflicts', val: `${s.outlookSyncResult.conflicts ?? 0} / ${s.outlookSyncResult.skipped ?? 0}`, tint: 'from-amber-500 to-orange-600' },
                        ] satisfies { labelKey: TranslationKey; val: string | number; tint: string }[]).map((m, i) => (
                          <div key={i} className="rounded-xl bg-white/70 dark:bg-slate-800/60 border border-slate-100 dark:border-slate-700 p-3 shadow-sm">
                            <div className="text-[11px] text-slate-500 dark:text-slate-400 font-medium">{t(m.labelKey)}</div>
                            <div className={`mt-1 text-lg font-extrabold bg-gradient-to-r ${m.tint} bg-clip-text text-transparent tabular-nums`}>{m.val}</div>
                          </div>
                        ))}
                      </div>
                    ) : (
                      <div className="text-xs break-all text-red-600 dark:text-red-400 font-semibold bg-red-50 dark:bg-red-900/20 border border-red-100 dark:border-red-900/40 rounded-xl p-3">
                        {s.outlookSyncResult.errorMessage ?? t('calendar.panels.outlook.unknownError')}
                      </div>
                    )}
                  </div>
                )}

                {/* 断开连接 */}
                <div className="pt-3 border-t border-slate-100 dark:border-slate-700/60">
                  <button
                    onClick={onDisconnect}
                    disabled={s.outlookDisconnecting}
                    className="inline-flex items-center gap-1.5 px-4 py-2.5 text-sm font-semibold text-rose-600 dark:text-rose-400 bg-white dark:bg-slate-800 border border-rose-200 dark:border-rose-900/40 rounded-xl hover:bg-rose-50 dark:hover:bg-rose-900/20 hover:shadow-md transition-all disabled:opacity-50"
                  >
                    {s.outlookDisconnecting ? <Icon name="spinner" className="w-4 h-4" /> : <Icon name="unlink" className="w-4 h-4" />}{t('calendar.panels.outlook.disconnect')}
                  </button>
                </div>
              </div>
            )}
          </>
        )}
      </div>

      {/* T000099: Azure AD 凭据配置模态窗（每个用户可自配置自己的凭据）
          使用 createPortal 渲染到 document.body，逃逸父级 Overlay 的 cal-fade-in-up 动画
          transform 包含块约束 —— 否则子模态窗的 fixed 定位会被父级 overflow-hidden 裁剪 */}
      {showAdminConfig && createPortal(
        <OutlookAdminConfigModal
          onClose={() => setShowAdminConfig(false)}
          onSaved={() => {
            setShowAdminConfig(false);
            // 配置保存后刷新 Outlook 同步状态，使 IsConfigured 标识即时更新
            void s.fetchOutlookStatus();
          }}
        />,
        document.body,
      )}
    </Overlay>
  );
}

/* =========================================================
   T000099: OutlookAdminConfigModal —— Azure AD 凭据配置模态窗
   每个用户可访问，配置自己的 ClientId/ClientSecret/TenantId/RedirectUri/Scopes。
   ClientSecret 不回显明文，仅显示「已配置/未配置」状态，留空保存表示不更新。
   ========================================================= */

interface AdminConfigModalProps {
  onClose: () => void;
  onSaved: () => void;
}

interface ConfigFormState {
  clientId: string;
  clientSecret: string; // 仅在用户输入新值时非空
  tenantId: string;
  redirectUri: string;
  scopes: string;
}

function OutlookAdminConfigModal({ onClose, onSaved }: AdminConfigModalProps) {
  const { t, language } = useI18n();
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [settings, setSettings] = useState<OutlookAdminSettingsDto | null>(null);
  const [form, setForm] = useState<ConfigFormState>({
    clientId: '', clientSecret: '', tenantId: '', redirectUri: '', scopes: '',
  });

  // 加载当前配置
  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const data = await outlookSyncApi.getCredentials();
        if (cancelled) return;
        setSettings(data);
        setForm({
          clientId: data.clientId ?? '',
          // ClientSecret 不回显明文，留空表示不更新
          clientSecret: '',
          tenantId: data.tenantId ?? '',
          redirectUri: data.redirectUri ?? '',
          scopes: data.scopes ?? '',
        });
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : t('calendar.panels.outlook.loadCredentialsFailed'));
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [t]);

  const handleSave = useCallback(async () => {
    setSaving(true);
    setError(null);
    setSuccess(null);
    try {
      // 仅提交非 null 字段（部分更新）。空字符串表示清空，未改动字段不提交。
      const payload: UpdateOutlookAdminSettingsRequest = {};
      // ClientId：仅在表单值与原值不同时提交
      if (form.clientId !== (settings?.clientId ?? '')) {
        payload.clientId = form.clientId;
      }
      // ClientSecret：用户输入了新值才提交（留空表示不更新）
      if (form.clientSecret !== '') {
        payload.clientSecret = form.clientSecret;
      }
      // TenantId
      if (form.tenantId !== (settings?.tenantId ?? '')) {
        payload.tenantId = form.tenantId;
      }
      // RedirectUri
      if (form.redirectUri !== (settings?.redirectUri ?? '')) {
        payload.redirectUri = form.redirectUri;
      }
      // Scopes
      if (form.scopes !== (settings?.scopes ?? '')) {
        payload.scopes = form.scopes;
      }

      // 如果没有任何字段变更，提示并退出
      const hasChange = Object.keys(payload).length > 0;
      if (!hasChange) {
        setSuccess(t('calendar.panels.outlook.noChanges'));
        return;
      }

      const updated = await outlookSyncApi.updateCredentials(payload);
      setSettings(updated);
      // 重置 ClientSecret 表单字段（不回显明文）
      setForm((prev) => ({ ...prev, clientSecret: '' }));
      setSuccess(t('calendar.panels.outlook.credentialsSaved'));
    } catch (err) {
      setError(err instanceof Error ? err.message : t('calendar.common.saveConfigFailed'));
    } finally {
      setSaving(false);
    }
  }, [form, settings, t]);

  const handleClearSecret = useCallback(async () => {
    if (!confirm(t('calendar.panels.outlook.clearSecretConfirm'))) return;
    setSaving(true);
    setError(null);
    setSuccess(null);
    try {
      const updated = await outlookSyncApi.updateCredentials({ clientSecret: 'CLEAR' });
      setSettings(updated);
      setForm((prev) => ({ ...prev, clientSecret: '' }));
      setSuccess(t('calendar.panels.outlook.secretCleared'));
    } catch (err) {
      setError(err instanceof Error ? err.message : t('calendar.panels.outlook.clearSecretFailed'));
    } finally {
      setSaving(false);
    }
  }, [t]);

  return (
    <Overlay onClose={onClose} w="max-w-xl">
      <ModalHeader
        title={t('calendar.panels.outlook.configureCredentials')}
        subtitle={t('calendar.panels.outlook.credentialsSubtitle')}
        onClose={onClose}
        icon={<Icon name="shield" className="w-5 h-5" />}
        theme="outlook"
      />
      <div className="flex-1 overflow-auto p-6 space-y-4">
        {loading ? (
          <div className="flex items-center justify-center py-12 text-slate-500 dark:text-slate-400">
            <Icon name="spinner" className="w-5 h-5 mr-2" />{t('calendar.panels.outlook.loadingCredentials')}
          </div>
        ) : (
          <>
            {/* 配置状态概览 */}
            <div className={`rounded-2xl border p-4 shadow-sm ${settings?.isConfigured
              ? 'border-emerald-200 dark:border-emerald-800/40 bg-gradient-to-br from-emerald-50/70 via-white to-teal-50/50 dark:from-emerald-950/20 dark:via-slate-800/30 dark:to-teal-950/20'
              : 'border-amber-200 dark:border-amber-800/40 bg-gradient-to-br from-amber-50/70 via-white to-orange-50/50 dark:from-amber-950/20 dark:via-slate-800/30 dark:to-orange-950/20'
            }`}>
              <div className="flex items-center gap-2 text-sm">
                <div className={`w-8 h-8 rounded-lg flex items-center justify-center ${settings?.isConfigured
                  ? 'bg-gradient-to-br from-emerald-500 to-teal-500 text-white'
                  : 'bg-gradient-to-br from-amber-400 to-orange-500 text-white'
                }`}>
                  <Icon name={settings?.isConfigured ? 'checkCircle' : 'alertCircle'} className="w-4 h-4" />
                </div>
                <div className="flex-1">
                  <div className={`font-bold ${settings?.isConfigured ? 'text-emerald-700 dark:text-emerald-300' : 'text-amber-700 dark:text-amber-300'}`}>
                    {settings?.isConfigured ? t('calendar.panels.outlook.credentialsConfigured') : t('calendar.panels.outlook.credentialsNotConfigured')}
                  </div>
                  <div className="text-xs text-slate-500 dark:text-slate-400">
                    {t('calendar.panels.outlook.sourceLabel')}{t(SOURCE_LABEL[String(settings?.source)] ?? 'calendar.panels.outlook.sourceAppsettings')}
                    {settings?.updatedAt && `${t('calendar.panels.outlook.updatedAtPrefix')}${new Date(settings.updatedAt).toLocaleString(toIntlLocale(language))}`}
                  </div>
                </div>
              </div>
            </div>

            {/* 配置表单 */}
            <div className="space-y-3">
              <div>
                <label className="block text-xs font-bold text-slate-600 dark:text-slate-300 mb-1.5">
                  {t('calendar.panels.outlook.clientIdLabel')}
                </label>
                <input
                  type="text"
                  value={form.clientId}
                  onChange={(e) => setForm((prev) => ({ ...prev, clientId: e.target.value }))}
                  placeholder={t('calendar.panels.outlook.clientIdPlaceholder')}
                  className="w-full px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-blue-500/30 focus:border-blue-400"
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-slate-600 dark:text-slate-300 mb-1.5">
                  {t('calendar.panels.outlook.clientSecretLabel')}
                </label>
                <input
                  type="password"
                  value={form.clientSecret}
                  onChange={(e) => setForm((prev) => ({ ...prev, clientSecret: e.target.value }))}
                  placeholder={settings?.hasClientSecret ? t('calendar.panels.outlook.clientSecretConfiguredPlaceholder') : t('calendar.panels.outlook.clientSecretPlaceholder')}
                  className="w-full px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-blue-500/30 focus:border-blue-400"
                />
                {settings?.hasClientSecret && (
                  <button
                    onClick={handleClearSecret}
                    disabled={saving}
                    className="mt-1.5 text-xs text-rose-600 dark:text-rose-400 hover:underline disabled:opacity-50"
                  >
                    {t('calendar.panels.outlook.clearSecret')}
                  </button>
                )}
              </div>

              <div>
                <label className="block text-xs font-bold text-slate-600 dark:text-slate-300 mb-1.5">
                  {t('calendar.panels.outlook.tenantIdLabel')}
                </label>
                <input
                  type="text"
                  value={form.tenantId}
                  onChange={(e) => setForm((prev) => ({ ...prev, tenantId: e.target.value }))}
                  placeholder={t('calendar.panels.outlook.tenantIdPlaceholder')}
                  className="w-full px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-blue-500/30 focus:border-blue-400"
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-slate-600 dark:text-slate-300 mb-1.5">
                  {t('calendar.panels.outlook.redirectUriLabel')}
                </label>
                <input
                  type="text"
                  value={form.redirectUri}
                  onChange={(e) => setForm((prev) => ({ ...prev, redirectUri: e.target.value }))}
                  placeholder={t('calendar.panels.outlook.redirectUriPlaceholder')}
                  className="w-full px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-blue-500/30 focus:border-blue-400"
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-slate-600 dark:text-slate-300 mb-1.5">
                  {t('calendar.panels.outlook.scopesLabel')}
                </label>
                <input
                  type="text"
                  value={form.scopes}
                  onChange={(e) => setForm((prev) => ({ ...prev, scopes: e.target.value }))}
                  placeholder={t('calendar.panels.outlook.scopesPlaceholder')}
                  className="w-full px-3 py-2 text-sm rounded-lg border border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-blue-500/30 focus:border-blue-400"
                />
              </div>
            </div>

            {/* 提示信息 */}
            {error && (
              <div className="rounded-xl border border-red-200 dark:border-red-800/40 bg-red-50 dark:bg-red-900/20 p-3 text-xs text-red-700 dark:text-red-300 font-semibold break-all">
                {error}
              </div>
            )}
            {success && (
              <div className="rounded-xl border border-emerald-200 dark:border-emerald-800/40 bg-emerald-50 dark:bg-emerald-900/20 p-3 text-xs text-emerald-700 dark:text-emerald-300 font-semibold">
                {success}
              </div>
            )}

            {/* Azure 门户配置指引 */}
            <div className="rounded-xl bg-slate-50 dark:bg-slate-800/50 border border-slate-200 dark:border-slate-700/60 p-3 text-xs text-slate-500 dark:text-slate-400 space-y-1">
              <div className="font-bold text-slate-600 dark:text-slate-300">{t('calendar.panels.outlook.guideTitle')}</div>
              <div>{t('calendar.panels.outlook.guideStep1Prefix')}<code className="px-1 py-0.5 rounded bg-slate-100 dark:bg-slate-700 text-slate-700 dark:text-slate-200">portal.azure.com</code>{t('calendar.panels.outlook.guideStep1Suffix')}</div>
              <div>{t('calendar.panels.outlook.guideStep2')}</div>
              <div>{t('calendar.panels.outlook.guideStep3')}</div>
              <div>{t('calendar.panels.outlook.guideStep4Prefix')}<code className="px-1 py-0.5 rounded bg-slate-100 dark:bg-slate-700 text-slate-700 dark:text-slate-200">Calendars.ReadWrite</code>{t('calendar.panels.outlook.guideStep4Suffix')}</div>
              <div>{t('calendar.panels.outlook.guideStep5')}</div>
            </div>

            {/* 操作按钮 */}
            <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-100 dark:border-slate-700/60">
              <button
                onClick={onClose}
                disabled={saving}
                className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-semibold text-slate-600 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-600 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-700 disabled:opacity-50 transition-colors"
              >
                {saving ? t('calendar.common.saving') : t('common.close')}
              </button>
              <button
                onClick={handleSave}
                disabled={saving}
                className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-semibold text-white bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-700 hover:to-indigo-700 rounded-lg shadow-md disabled:opacity-50 transition-all"
              >
                {saving ? <Icon name="spinner" className="w-4 h-4" /> : <Icon name="check" className="w-4 h-4" />}{t('calendar.panels.outlook.saveConfiguration')}
              </button>
            </div>

            {success && (
              <div className="flex justify-end">
                <button
                  onClick={onSaved}
                  className="text-xs text-blue-600 dark:text-blue-400 hover:underline"
                >
                  {t('calendar.panels.outlook.doneBack')}
                </button>
              </div>
            )}
          </>
        )}
      </div>
    </Overlay>
  );
}
