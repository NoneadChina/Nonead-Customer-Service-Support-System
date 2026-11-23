import { useState, useEffect, useCallback } from 'react';
import { telegramApi, mailboxApi } from '../api';
import type {
  TelegramSettingsDto,
  TelegramStatusDto,
  UpdateTelegramSettingsRequest,
  MailboxResponse,
} from '../api/types';
import { useToast } from '../context/ToastContext';
import { useI18n } from '../context/I18nContext';
import { usePermission } from '../hooks/usePermission';
import type { TranslationKey } from '../locales/zh-Hans';
import {
  Save,
  Loader2,
  CheckCircle2,
  AlertCircle,
  Copy,
  RefreshCw,
  Eye,
  EyeOff,
  Zap,
  Radio,
  Wifi,
  Info,
  ShieldAlert,
  Unlink,
  Send,
} from 'lucide-react';

/** Telegram 纸飞机 logo（单色 currentColor，跟随主题） */
function TelegramLogo({ className = 'w-7 h-7' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="currentColor" xmlns="http://www.w3.org/2000/svg">
      <path d="M9.78 18.65l.28-4.23 7.68-6.92c.34-.31-.07-.46-.52-.19L7.74 13.26 3.64 11.9c-.88-.26-.89-.86.2-1.3l15.97-6.16c.73-.33 1.43.18 1.15 1.3l-2.2 9.46c-.18.75-.6 1.01-1.24.67l-3.5-2.58-3.04 2.94c-.33.33-.61.33-.87 0z" />
    </svg>
  );
}

/**
 * 入站模式定义。
 * `value` 是发给后端的稳定码（TelegramOptions.ModeWebhook / ModePolling），不随界面语言变化；
 * 文案字段一律持有 TranslationKey，漏包 t() 会被 tsc 以 TS2339 抓出。
 */
type InboundModeDef = {
  value: 'webhook' | 'polling';
  labelKey: TranslationKey;
  icon: typeof Wifi;
  badgeKey: TranslationKey | null;
  badgeClass: string;
  descKey: TranslationKey;
  featureKeys: TranslationKey[];
};

const INBOUND_MODES: InboundModeDef[] = [
  {
    value: 'webhook',
    labelKey: 'channels.telegram.mode.webhookLabel',
    icon: Wifi,
    badgeKey: 'channels.common.recommended',
    badgeClass: 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300',
    descKey: 'channels.telegram.mode.webhookDesc',
    featureKeys: [
      'channels.common.needPublicCallbackUrl',
      'channels.telegram.mode.webhookFeatureSecret',
      'channels.telegram.mode.webhookFeatureFast',
      'channels.telegram.mode.webhookFeatureNoCursor',
    ],
  },
  {
    value: 'polling',
    labelKey: 'channels.telegram.mode.pollingLabel',
    icon: Radio,
    badgeKey: null,
    badgeClass: '',
    descKey: 'channels.telegram.mode.pollingDesc',
    featureKeys: [
      'channels.common.noPublicAddress',
      'channels.telegram.mode.pollingFeatureConflict',
      'channels.telegram.mode.pollingFeatureCursor',
    ],
  },
];

/** 状态行：左标签 + 右值（值可为徽章） */
function StatusRow({ label, ok, children }: { label: string; ok?: boolean | null; children?: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3 py-1.5 text-sm">
      <span className="text-gray-600 dark:text-slate-300 truncate">{label}</span>
      <span className="flex items-center gap-1.5 flex-shrink-0">
        {ok === true && <CheckCircle2 className="w-4 h-4 text-green-500" />}
        {ok === false && <AlertCircle className="w-4 h-4 text-red-500" />}
        {children}
      </span>
    </div>
  );
}

export default function Telegram() {
  const toast = useToast();
  const { t } = useI18n();
  const { isReadOnly } = usePermission();
  const [settings, setSettings] = useState<TelegramSettingsDto | null>(null);
  const [status, setStatus] = useState<TelegramStatusDto | null>(null);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [webhookBusy, setWebhookBusy] = useState(false);

  // 敏感字段只做"写入"，永不回显：留空 = 不修改
  const [botToken, setBotToken] = useState('');
  const [webhookSecret, setWebhookSecret] = useState('');
  const [showBotToken, setShowBotToken] = useState(false);

  const [form, setForm] = useState<UpdateTelegramSettingsRequest>({});

  const currentMode = (form.mode ?? settings?.mode ?? 'webhook') as 'webhook' | 'polling';
  const modeDef = INBOUND_MODES.find((m) => m.value === currentMode) ?? INBOUND_MODES[0];

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const [settingsData, statusData, mailboxData] = await Promise.all([
        telegramApi.getSettings(),
        telegramApi.getStatus(),
        mailboxApi.list(),
      ]);
      setSettings(settingsData);
      setStatus(statusData);
      setMailboxes(mailboxData);
      setForm({
        mode: INBOUND_MODES.find((m) => m.value === settingsData.mode)?.value ?? 'webhook',
        apiBaseUrl: settingsData.apiBaseUrl ?? '',
        useLocalBotApiServer: settingsData.useLocalBotApiServer,
        webhookUrl: settingsData.callbackUrl ?? '',
        botUsername: settingsData.botUsername ?? '',
        acceptGroupMessages: settingsData.acceptGroupMessages,
        deleteWebhookOnSwitch: settingsData.deleteWebhookOnSwitch,
        maxWebhookConnections: settingsData.maxWebhookConnections,
        pollingTimeout: settingsData.pollingTimeout,
        pollingLimit: settingsData.pollingLimit,
        mailboxId: settingsData.mailboxId ?? undefined,
        autoReplyEnabled: settingsData.autoReplyEnabled,
        autoReplyMessage: settingsData.autoReplyMessage ?? '',
        conversationMatchWindowHours: settingsData.conversationMatchWindowHours,
        maxMessageChars: settingsData.maxMessageChars,
        rateLimitGlobalPerSeconds: settingsData.rateLimitGlobalPerSeconds,
        rateLimitPerChatPerSeconds: settingsData.rateLimitPerChatPerSeconds,
        enabled: settingsData.enabled,
      });
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('channels.telegram.loadFailed'));
    } finally {
      setLoading(false);
    }
  }, [toast, t]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const patch = (key: keyof UpdateTelegramSettingsRequest, value: unknown) =>
    setForm((prev) => ({ ...prev, [key]: value }));

  const handleSave = async () => {
    setSaving(true);
    try {
      const payload: UpdateTelegramSettingsRequest = { ...form };
      // 只有填写了值才提交敏感字段，空字符串会被后端理解为"清除"
      if (botToken.trim()) payload.botToken = botToken.trim();
      else delete payload.botToken;
      if (webhookSecret.trim()) payload.webhookSecretToken = webhookSecret.trim();
      else delete payload.webhookSecretToken;

      const updated = await telegramApi.updateSettings(payload);
      setSettings(updated);
      setBotToken('');
      setWebhookSecret('');
      toast.success(t('channels.telegram.saveSuccess'));
      const newStatus = await telegramApi.getStatus();
      setStatus(newStatus);
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('channels.telegram.saveFailed'));
    } finally {
      setSaving(false);
    }
  };

  const handleTest = async () => {
    setTesting(true);
    try {
      const result = await telegramApi.testConnection();
      if (result.success) {
        toast.success(t('channels.telegram.testSuccess', { username: result.botUsername ?? '' }));
      } else {
        toast.error(result.errorMessage ?? t('channels.telegram.testFailed'));
      }
      setStatus(await telegramApi.getStatus());
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('channels.telegram.testFailed'));
    } finally {
      setTesting(false);
    }
  };

  const handleSetupWebhook = async () => {
    setWebhookBusy(true);
    try {
      const result = await telegramApi.setupWebhook({ webhookUrl: form.webhookUrl || null, dropPendingUpdates: false });
      if (result.success) {
        toast.success(t('channels.telegram.webhook.setupSuccess'));
        const refreshed = await telegramApi.getStatus();
        setStatus(refreshed);
        setSettings(await telegramApi.getSettings());
      } else {
        toast.error(result.errorMessage ?? t('channels.telegram.webhook.setupFailed'));
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('channels.telegram.webhook.setupFailed'));
    } finally {
      setWebhookBusy(false);
    }
  };

  const handleDeleteWebhook = async () => {
    setWebhookBusy(true);
    try {
      const result = await telegramApi.deleteWebhook();
      if (result.success) {
        toast.success(t('channels.telegram.webhook.deleteSuccess'));
        setStatus(await telegramApi.getStatus());
      } else {
        toast.error(result.errorMessage ?? t('channels.telegram.webhook.deleteFailed'));
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('channels.telegram.webhook.deleteFailed'));
    } finally {
      setWebhookBusy(false);
    }
  };

  const copyText = async (value: string, okKey: TranslationKey) => {
    try {
      await navigator.clipboard.writeText(value);
      toast.success(t(okKey));
    } catch {
      toast.error(t('channels.common.copyFailed'));
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64 gap-2 text-gray-500 dark:text-slate-400">
        <Loader2 className="w-5 h-5 animate-spin" />
        <span>{t('channels.common.loading')}</span>
      </div>
    );
  }

  return (
    <div className="max-w-5xl mx-auto space-y-6">
      {/* 页头 */}
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div className="flex items-center gap-3">
          <div className="flex h-11 w-11 items-center justify-center rounded-2xl bg-sky-50 text-sky-600 ring-1 ring-inset ring-sky-200/80 dark:bg-sky-500/15 dark:text-sky-300 dark:ring-sky-400/25">
            <TelegramLogo />
          </div>
          <div>
            <h1 className="text-xl font-semibold tracking-tight text-gray-900 dark:text-slate-100">{t('channels.telegram.title')}</h1>
            <p className="text-sm text-gray-500 dark:text-slate-400">{t('channels.telegram.subtitle')}</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => void loadData()}
            className="inline-flex items-center gap-1.5 px-3 py-2 text-sm rounded-xl border border-gray-300 bg-white text-gray-700 shadow-sm hover:bg-gray-50 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-200 dark:shadow-none dark:hover:bg-slate-700/50 transition-colors"
          >
            <RefreshCw className="w-4 h-4" />
            {t('channels.common.refreshStatus')}
          </button>
          <button
            type="button"
            onClick={() => void handleTest()}
            disabled={testing || !settings?.hasBotToken}
            className="inline-flex items-center gap-1.5 px-3 py-2 text-sm rounded-lg border border-sky-300 dark:border-sky-700 text-sky-700 dark:text-sky-300 hover:bg-sky-50 dark:hover:bg-sky-900/30 disabled:opacity-50 transition-colors"
          >
            {testing ? <Loader2 className="w-4 h-4 animate-spin" /> : <Zap className="w-4 h-4" />}
            {t('channels.telegram.testButton')}
          </button>
        </div>
      </div>

      {/* 状态卡片 */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div className="rounded-2xl border border-gray-200 bg-white p-5 shadow-sm transition-colors hover:border-gray-300 dark:border-slate-700 dark:bg-slate-800 dark:shadow-none dark:hover:border-slate-600">
          <h2 className="text-sm font-semibold text-gray-900 dark:text-slate-100 mb-2">{t('channels.telegram.status.botTitle')}</h2>
          <StatusRow label={t('channels.telegram.status.configured')} ok={status?.configured} />
          <StatusRow label={t('channels.telegram.status.enabled')} ok={status?.enabled} />
          <StatusRow label={t('channels.telegram.status.apiReachable')} ok={status ? status.apiReachable : null} />
          <StatusRow label={t('channels.telegram.status.botUsername')}>
            <span className="text-gray-900 dark:text-slate-100 font-mono text-xs">
              {status?.botUsername ? `@${status.botUsername}` : t('channels.common.notDetected')}
            </span>
          </StatusRow>
          <StatusRow
            label={t('channels.telegram.status.privacyMode')}
            ok={status?.canReadAllGroupMessages === true}
          >
            <span className="text-xs text-gray-600 dark:text-slate-300">
              {status?.canReadAllGroupMessages === true
                ? t('channels.telegram.status.privacyOff')
                : t('channels.telegram.status.privacyOn')}
            </span>
          </StatusRow>
          {(status?.unreachableConversationCount ?? 0) > 0 && (
            <div className="mt-2 flex items-start gap-2 text-xs text-red-600 dark:text-red-400 rounded-xl border border-red-200/80 bg-red-50/80 p-3 dark:border-red-500/25 dark:bg-red-500/10">
              <ShieldAlert className="w-4 h-4 flex-shrink-0" />
              <span>{t('channels.telegram.status.unreachableCount', { count: status?.unreachableConversationCount ?? 0 })}</span>
            </div>
          )}
        </div>

        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4">
          <h2 className="text-sm font-semibold text-gray-900 dark:text-slate-100 mb-2">{t('channels.telegram.status.inboundTitle')}</h2>
          <StatusRow label={t('channels.telegram.status.mode')}>
            <span className="text-xs text-gray-900 dark:text-slate-100 font-mono">{status?.mode ?? currentMode}</span>
          </StatusRow>
          {currentMode === 'webhook' ? (
            <>
              <StatusRow label={t('channels.telegram.status.webhookRegistered')} ok={status?.webhookRegistered} />
              <StatusRow label={t('channels.telegram.status.webhookUrl')}>
                <span className="text-xs text-gray-600 dark:text-slate-300 font-mono truncate max-w-[14rem]">
                  {status?.webhookUrl || t('channels.common.notDetected')}
                </span>
              </StatusRow>
              <StatusRow label={t('channels.telegram.status.pendingUpdates')}>
                <span className="text-xs text-gray-900 dark:text-slate-100">{status?.pendingUpdateCount ?? '-'}</span>
              </StatusRow>
              {status?.lastErrorMessage && (
                <div className="mt-2 flex items-start gap-2 text-xs text-amber-700 dark:text-amber-400 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 rounded-lg p-2">
                  <AlertCircle className="w-4 h-4 flex-shrink-0" />
                  <span className="break-all">{t('channels.telegram.status.lastError', { message: status.lastErrorMessage })}</span>
                </div>
              )}
            </>
          ) : (
            <>
              <StatusRow label={t('channels.telegram.status.pollingRunning')} ok={status?.pollingRunning} />
              <StatusRow label={t('channels.telegram.status.pollingOffset')}>
                <span className="text-xs text-gray-900 dark:text-slate-100 font-mono">{status?.pollingOffset ?? '-'}</span>
              </StatusRow>
              {status?.pollingLastErrorMessage && (
                <div className="mt-2 flex items-start gap-2 text-xs text-amber-700 dark:text-amber-400 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 rounded-lg p-2">
                  <AlertCircle className="w-4 h-4 flex-shrink-0" />
                  <span className="break-all">{status.pollingLastErrorMessage}</span>
                </div>
              )}
            </>
          )}
          <div className="mt-3 flex items-center gap-2 flex-wrap">
            <button
              type="button"
              onClick={() => void handleSetupWebhook()}
              disabled={webhookBusy || isReadOnly || currentMode !== 'webhook'}
              className="inline-flex items-center gap-1.5 px-2.5 py-1.5 text-xs rounded-xl border border-gray-300 bg-white text-gray-700 shadow-sm hover:bg-gray-50 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-200 dark:shadow-none dark:hover:bg-slate-700/50 disabled:opacity-50 transition-colors"
            >
              {webhookBusy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Send className="w-3.5 h-3.5" />}
              {t('channels.telegram.webhook.setupButton')}
            </button>
            <button
              type="button"
              onClick={() => void handleDeleteWebhook()}
              disabled={webhookBusy || isReadOnly}
              className="inline-flex items-center gap-1.5 px-2.5 py-1.5 text-xs rounded-xl border border-gray-300 bg-white text-gray-700 shadow-sm hover:bg-gray-50 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-200 dark:shadow-none dark:hover:bg-slate-700/50 disabled:opacity-50 transition-colors"
            >
              <Unlink className="w-3.5 h-3.5" />
              {t('channels.telegram.webhook.deleteButton')}
            </button>
          </div>
        </div>
      </div>

      {/* 关键约束提示（Telegram 特有：机器人不能主动发起私聊） */}
      <div className="flex items-start gap-2 text-sm text-sky-800 dark:text-sky-300 bg-sky-50 dark:bg-sky-900/20 border border-sky-200 dark:border-sky-800 rounded-xl p-3">
        <Info className="w-4 h-4 flex-shrink-0 mt-0.5" />
        <div className="space-y-1">
          <p>{t('channels.telegram.notice.noInitiate')}</p>
          {settings?.deepLinkPrefix && (
            <div className="flex items-center gap-2 flex-wrap">
              <code className="text-xs bg-white dark:bg-slate-800 border border-sky-200 dark:border-sky-800 rounded px-2 py-1 break-all">
                {settings.deepLinkPrefix}
              </code>
              <button
                type="button"
                onClick={() => void copyText(settings.deepLinkPrefix ?? '', 'channels.common.copySuccess')}
                className="inline-flex items-center gap-1 text-xs text-sky-700 dark:text-sky-300 hover:underline"
              >
                <Copy className="w-3.5 h-3.5" />
                {t('channels.common.copy')}
              </button>
            </div>
          )}
          <p className="text-xs opacity-80">{t('channels.telegram.notice.rateLimits')}</p>
        </div>
      </div>

      {/* 入站模式选择 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4">
        <h2 className="text-sm font-semibold text-gray-900 dark:text-slate-100 mb-3">{t('channels.telegram.sections.inbound')}</h2>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          {INBOUND_MODES.map((m) => {
            const active = currentMode === m.value;
            return (
              <button
                key={m.value}
                type="button"
                disabled={isReadOnly}
                onClick={() => patch('mode', m.value)}
                className={`text-left rounded-xl border p-3 transition-colors ${
                  active
                    ? 'border-sky-500 dark:border-sky-400 bg-sky-50/60 dark:bg-sky-900/20'
                    : 'border-gray-200 dark:border-slate-700 hover:border-gray-300 dark:hover:border-slate-600'
                }`}
              >
                <div className="flex items-center gap-2 mb-1">
                  <m.icon className={`w-4 h-4 ${active ? 'text-sky-600 dark:text-sky-400' : 'text-gray-500 dark:text-slate-400'}`} />
                  <span className="text-sm font-medium text-gray-900 dark:text-slate-100">{t(m.labelKey)}</span>
                  {m.badgeKey && (
                    <span className={`text-[10px] px-1.5 py-0.5 rounded ${m.badgeClass}`}>{t(m.badgeKey)}</span>
                  )}
                </div>
                <p className="text-xs text-gray-600 dark:text-slate-300 mb-2">{t(m.descKey)}</p>
                <ul className="space-y-0.5">
                  {m.featureKeys.map((k) => (
                    <li key={k} className="text-[11px] text-gray-500 dark:text-slate-400">· {t(k)}</li>
                  ))}
                </ul>
              </button>
            );
          })}
        </div>
        <p className="mt-2 text-xs text-amber-700 dark:text-amber-400">{t('channels.telegram.notice.modeConflict')}</p>
      </div>

      {/* 凭据与回调 */}
      <div className="rounded-2xl border border-gray-200 bg-white p-5 shadow-sm space-y-4 transition-colors hover:border-gray-300 dark:border-slate-700 dark:bg-slate-800 dark:shadow-none dark:hover:border-slate-600">
        <h2 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('channels.telegram.sections.credentials')}</h2>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <label className="block">
            <span className="text-xs font-medium text-gray-700 dark:text-slate-200">{t('channels.telegram.fields.botToken')}</span>
            <div className="relative mt-1">
              <input
                type={showBotToken ? 'text' : 'password'}
                value={botToken}
                onChange={(e) => setBotToken(e.target.value)}
                placeholder={settings?.hasBotToken ? t('channels.common.configuredKeepBlank') : t('channels.telegram.botTokenPlaceholder')}
                autoComplete="off"
                spellCheck={false}
                disabled={isReadOnly}
                className="w-full px-3 py-2 pr-9 text-sm rounded-xl border border-gray-300 bg-white text-gray-900 shadow-sm dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 dark:shadow-none focus:outline-none focus:ring-2 focus:ring-sky-500"
              />
              <button
                type="button"
                onClick={() => setShowBotToken((v) => !v)}
                className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600 dark:hover:text-slate-300"
                aria-label={t('channels.common.toggleVisibility')}
              >
                {showBotToken ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
              </button>
            </div>
            <span className="text-[11px] text-gray-500 dark:text-slate-400">{t('channels.telegram.fields.botTokenHint')}</span>
          </label>

          <label className="block">
            <span className="text-xs font-medium text-gray-700 dark:text-slate-200">{t('channels.telegram.fields.apiBaseUrl')}</span>
            <input
              type="text"
              value={form.apiBaseUrl ?? ''}
              onChange={(e) => patch('apiBaseUrl', e.target.value)}
              placeholder="https://api.telegram.org"
              disabled={isReadOnly}
              className="mt-1 w-full px-3 py-2 text-sm rounded-xl border border-gray-300 bg-white text-gray-900 shadow-sm dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 dark:shadow-none focus:outline-none focus:ring-2 focus:ring-sky-500"
            />
            <span className="text-[11px] text-gray-500 dark:text-slate-400">{t('channels.telegram.fields.apiBaseUrlHint')}</span>
          </label>

          <label className="block">
            <span className="text-xs font-medium text-gray-700 dark:text-slate-200">{t('channels.telegram.fields.webhookUrl')}</span>
            <input
              type="text"
              value={form.webhookUrl ?? ''}
              onChange={(e) => patch('webhookUrl', e.target.value)}
              placeholder={settings?.callbackUrl ?? ''}
              disabled={isReadOnly || currentMode !== 'webhook'}
              className="mt-1 w-full px-3 py-2 text-sm rounded-xl border border-gray-300 bg-white text-gray-900 shadow-sm dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 dark:shadow-none focus:outline-none focus:ring-2 focus:ring-sky-500"
            />
            <span className="text-[11px] text-gray-500 dark:text-slate-400">{t('channels.telegram.fields.webhookUrlHint')}</span>
          </label>

          <label className="block">
            <span className="text-xs font-medium text-gray-700 dark:text-slate-200">{t('channels.telegram.fields.webhookSecret')}</span>
            <input
              type="password"
              value={webhookSecret}
              onChange={(e) => setWebhookSecret(e.target.value)}
              placeholder={settings?.hasWebhookSecretToken ? t('channels.common.configuredKeepBlank') : t('channels.telegram.webhook.secretAutoGenerate')}
              autoComplete="off"
              disabled={isReadOnly || currentMode !== 'webhook'}
              className="mt-1 w-full px-3 py-2 text-sm rounded-xl border border-gray-300 bg-white text-gray-900 shadow-sm dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 dark:shadow-none focus:outline-none focus:ring-2 focus:ring-sky-500"
            />
            <span className="text-[11px] text-gray-500 dark:text-slate-400">{t('channels.telegram.fields.webhookSecretHint')}</span>
          </label>
        </div>

        <div className="flex flex-wrap gap-4">
          <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-slate-200">
            <input
              type="checkbox"
              checked={form.useLocalBotApiServer ?? false}
              onChange={(e) => patch('useLocalBotApiServer', e.target.checked)}
              disabled={isReadOnly}
            />
            {t('channels.telegram.fields.useLocalBotApiServer')}
          </label>
          <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-slate-200">
            <input
              type="checkbox"
              checked={form.acceptGroupMessages ?? false}
              onChange={(e) => patch('acceptGroupMessages', e.target.checked)}
              disabled={isReadOnly}
            />
            {t('channels.telegram.fields.acceptGroupMessages')}
          </label>
          <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-slate-200">
            <input
              type="checkbox"
              checked={form.deleteWebhookOnSwitch ?? true}
              onChange={(e) => patch('deleteWebhookOnSwitch', e.target.checked)}
              disabled={isReadOnly}
            />
            {t('channels.telegram.fields.deleteWebhookOnSwitch')}
          </label>
        </div>
      </div>

      {/* 运行参数 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4 space-y-4">
        <h2 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('channels.telegram.sections.runtime')}</h2>
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
          {([
            ['maxWebhookConnections', 'channels.telegram.fields.maxWebhookConnections'],
            ['pollingTimeout', 'channels.telegram.fields.pollingTimeout'],
            ['pollingLimit', 'channels.telegram.fields.pollingLimit'],
            ['maxMessageChars', 'channels.telegram.fields.maxMessageChars'],
            ['rateLimitGlobalPerSeconds', 'channels.telegram.fields.rateLimitGlobal'],
            ['rateLimitPerChatPerSeconds', 'channels.telegram.fields.rateLimitPerChat'],
            ['conversationMatchWindowHours', 'channels.telegram.fields.matchWindowHours'],
          ] as [keyof UpdateTelegramSettingsRequest, TranslationKey][]).map(([key, labelKey]) => (
            <label key={String(key)} className="block">
              <span className="text-xs font-medium text-gray-700 dark:text-slate-200">{t(labelKey)}</span>
              <input
                type="number"
                value={Number(form[key] ?? 0)}
                onChange={(e) => patch(key, Number(e.target.value))}
                disabled={isReadOnly}
                className="mt-1 w-full px-3 py-2 text-sm rounded-xl border border-gray-300 bg-white text-gray-900 shadow-sm dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 dark:shadow-none focus:outline-none focus:ring-2 focus:ring-sky-500"
              />
            </label>
          ))}

          <label className="block">
            <span className="text-xs font-medium text-gray-700 dark:text-slate-200">{t('channels.common.mailbox')}</span>
            <select
              value={form.mailboxId ?? ''}
              onChange={(e) => patch('mailboxId', e.target.value ? Number(e.target.value) : null)}
              disabled={isReadOnly}
              className="mt-1 w-full px-3 py-2 text-sm rounded-xl border border-gray-300 bg-white text-gray-900 shadow-sm dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 dark:shadow-none focus:outline-none focus:ring-2 focus:ring-sky-500"
            >
              <option value="">{t('channels.common.mailboxAuto')}</option>
              {mailboxes.map((m) => (
                <option key={m.id} value={m.id}>{m.name}</option>
              ))}
            </select>
          </label>
        </div>

        <div className="flex flex-wrap gap-4">
          <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-slate-200">
            <input
              type="checkbox"
              checked={form.autoReplyEnabled ?? false}
              onChange={(e) => patch('autoReplyEnabled', e.target.checked)}
              disabled={isReadOnly}
            />
            {t('channels.common.autoReplyEnabled')}
          </label>
          <label className="flex-1 min-w-[16rem] block">
            <span className="text-xs font-medium text-gray-700 dark:text-slate-200">{t('channels.common.autoReplyMessage')}</span>
            <input
              type="text"
              value={form.autoReplyMessage ?? ''}
              maxLength={500}
              onChange={(e) => patch('autoReplyMessage', e.target.value)}
              disabled={isReadOnly || !(form.autoReplyEnabled ?? false)}
              className="mt-1 w-full px-3 py-2 text-sm rounded-xl border border-gray-300 bg-white text-gray-900 shadow-sm dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 dark:shadow-none focus:outline-none focus:ring-2 focus:ring-sky-500"
            />
          </label>
        </div>
      </div>

      {/* 保存 / 启用 */}
      <div className="flex items-center justify-between gap-4 flex-wrap bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4">
        <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-slate-200">
          <input
            type="checkbox"
            checked={form.enabled ?? false}
            onChange={(e) => patch('enabled', e.target.checked)}
            disabled={isReadOnly}
          />
          {t('channels.common.enableChannel', { channel: t('nav.telegram') })}
        </label>
        <div className="flex items-center gap-2">
          {isReadOnly && <span className="text-xs text-gray-500 dark:text-slate-400">{t('channels.common.readOnly')}</span>}
          <button
            type="button"
            onClick={() => void handleSave()}
            disabled={saving || isReadOnly}
            className="inline-flex items-center gap-1.5 px-4 py-2 bg-gradient-to-r from-sky-600 to-blue-600 hover:from-sky-700 hover:to-blue-700 disabled:from-gray-300 dark:disabled:from-slate-700 text-white rounded-lg text-sm font-medium shadow-sm transition-all"
          >
            {saving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
            {t('channels.common.save')}
          </button>
        </div>
      </div>

      <p className="text-xs text-gray-500 dark:text-slate-400">
        {modeDef.labelKey && t('channels.telegram.footer', { mode: t(modeDef.labelKey) })}
      </p>
    </div>
  );
}
