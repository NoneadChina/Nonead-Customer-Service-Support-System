import { useState, useEffect, useCallback } from 'react';
import { feishuApi, mailboxApi } from '../api';
import type {
  FeishuSettingsDto,
  FeishuStatusDto,
  UpdateFeishuSettingsRequest,
  MailboxResponse,
} from '../api/types';
import { useI18n } from '../context/I18nContext';
import { useToast } from '../context/ToastContext';
import { usePermission } from '../hooks/usePermission';
import type { TranslationKey } from '../locales/zh-Hans';
import {
  Save,
  Loader2,
  CheckCircle2,
  AlertCircle,
  Copy,
  ExternalLink,
  RefreshCw,
  Lock,
  Unlock,
  Eye,
  EyeOff,
  Pencil,
  X,
  Settings,
  MessageSquare,
  Image,
  FileText,
  Mic,
  Video,
  Link2,
  Zap,
  Wifi,
  Radio,
  Info,
  ShieldCheck,
  UserCheck,
} from 'lucide-react';

// 飞书品牌色系
const FEISHU_PRIMARY = '#3370FF';
const FEISHU_ACCENT = '#00D6B9';

/** 飞书官方 logo 组件（纸飞机风格） */
function FeishuLogo({ className = 'w-7 h-7' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
      <path d="M3 11l18-8-8 18-2-8-8-2z" fill="currentColor" opacity="0.9" />
      <path d="M3 11l8 2 2 8" stroke="currentColor" strokeWidth="1.5" fill="none" />
    </svg>
  );
}

/**
 * 支持的消息类型列表。
 *
 * 字段名带 `Key` 后缀并持有 `TranslationKey`（不持有文案）：漏包 `t()` 会被 `tsc` 以
 * TS2339 抓出，直接渲染则会把 key 本身漏到界面上。
 * 飞书与钉钉共用的五类文案复用 `channels.common.msgType.*`。
 */
type MessageTypeDef = {
  icon: typeof MessageSquare;
  labelKey: TranslationKey;
  descKey: TranslationKey;
};

const MESSAGE_TYPES: MessageTypeDef[] = [
  { icon: MessageSquare, labelKey: 'channels.common.msgType.text', descKey: 'channels.common.msgType.textDesc' },
  { icon: Image, labelKey: 'channels.common.msgType.image', descKey: 'channels.common.msgType.imageDesc' },
  { icon: FileText, labelKey: 'channels.common.msgType.file', descKey: 'channels.common.msgType.fileDesc' },
  { icon: Mic, labelKey: 'channels.common.msgType.audio', descKey: 'channels.common.msgType.audioDesc' },
  { icon: Video, labelKey: 'channels.common.msgType.video', descKey: 'channels.common.msgType.videoDesc' },
  { icon: Link2, labelKey: 'channels.feishu.msgType.richText', descKey: 'channels.feishu.msgType.richTextDesc' },
];

/**
 * 消息接收模式定义。
 *
 * `value` 是发给后端的稳定码（`Stream` / `Http`，见 `FeishuOptions.cs:58`、
 * `FeishuService.cs:166`），**不随界面语言变化**，保持字面量；
 * 其余文案字段均持有 `TranslationKey`（`badgeKey = null` 表示该模式无徽标）。
 */
type ReceiveModeDef = {
  value: string;
  labelKey: TranslationKey;
  icon: typeof Radio;
  badgeKey: TranslationKey | null;
  badgeClass: string;
  descKey: TranslationKey;
  featureKeys: TranslationKey[];
  docUrl: string;
};

const RECEIVE_MODES: ReceiveModeDef[] = [
  {
    value: 'Stream',
    labelKey: 'channels.feishu.mode.streamLabel',
    icon: Radio,
    badgeKey: 'channels.common.recommended',
    badgeClass: 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300',
    descKey: 'channels.common.wsLongConnection',
    featureKeys: [
      'channels.common.noPublicAddress',
      'channels.feishu.mode.streamFeature.encrypted',
      'channels.feishu.mode.streamFeature.quickSetup',
      'channels.common.autoReconnect',
    ],
    docUrl: 'https://open.feishu.cn/document/server-docs/server-side-sdk',
  },
  {
    value: 'Http',
    labelKey: 'channels.feishu.mode.httpLabel',
    icon: Wifi,
    badgeKey: null,
    badgeClass: '',
    descKey: 'channels.feishu.mode.httpDesc',
    featureKeys: [
      'channels.common.needPublicCallbackUrl',
      'channels.feishu.mode.httpFeature.signature',
      'channels.feishu.mode.httpFeature.urlVerification',
      'channels.feishu.mode.httpFeature.eventFormat',
    ],
    docUrl: 'https://open.feishu.cn/document/uAjLw4CM/ukTMukTMukTM/reference/im-v1/message/events/receive',
  },
];

export default function Feishu() {
  const toast = useToast();
  const { t } = useI18n();
  const { isReadOnly } = usePermission();
  const [settings, setSettings] = useState<FeishuSettingsDto | null>(null);
  const [status, setStatus] = useState<FeishuStatusDto | null>(null);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [refreshLock, setRefreshLock] = useState(false);
  const isRefreshing = loading || refreshLock;

  // 敏感字段显示控制
  const [showEncryptKey, setShowEncryptKey] = useState(false);
  // 敏感字段编辑状态
  const [editingAppSecret, setEditingAppSecret] = useState(false);
  const [editingEncryptKey, setEditingEncryptKey] = useState(false);

  // 配置表单
  const [form, setForm] = useState<UpdateFeishuSettingsRequest>({});

  // 当前选中的接收模式
  const currentMode = form.receiveMode ?? status?.receiveMode ?? 'Http';
  const currentModeDef = RECEIVE_MODES.find((m) => m.value === currentMode) ?? RECEIVE_MODES[0];
  const isHttpMode = currentMode === 'Http';
  const isStreamMode = currentMode === 'Stream';

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const [settingsData, statusData, mailboxData] = await Promise.all([
        feishuApi.getSettings(),
        feishuApi.getStatus(),
        mailboxApi.list(),
      ]);
      setSettings(settingsData);
      setStatus(statusData);
      setMailboxes(mailboxData);
      setForm({
        appId: settingsData.appId ?? '',
        verificationToken: settingsData.hasVerificationToken ? t('channels.feishu.form.maskedVerificationToken') : '',
        encryptEnabled: settingsData.encryptEnabled,
        receiveMode: settingsData.receiveMode ?? 'Http',
        mailboxId: settingsData.mailboxId ?? undefined,
        contactCollectionEnabled: settingsData.contactCollectionEnabled,
        autoReplyEnabled: settingsData.autoReplyEnabled,
        autoReplyMessage: settingsData.autoReplyMessage ?? '',
        enabled: settingsData.enabled,
      });
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : t('channels.common.loadFailed', { channel: t('format.sourceType.feishu') });
      toast.error(message);
    } finally {
      setLoading(false);
    }
  }, [toast, t]);

  const handleRefresh = useCallback(() => {
    if (isRefreshing) return;
    setRefreshLock(true);
    try {
      void loadData();
    } finally {
      setTimeout(() => setRefreshLock(false), 400);
    }
  }, [isRefreshing, loadData]);

  useEffect(() => {
    let destroyed = false;
    const run = async () => {
      if (!destroyed) setLoading(true);
      try {
        const [settingsData, statusData, mailboxData] = await Promise.all([
          feishuApi.getSettings(),
          feishuApi.getStatus(),
          mailboxApi.list(),
        ]);
        if (!destroyed) {
          setSettings(settingsData);
          setStatus(statusData);
          setMailboxes(mailboxData);
          setForm({
            appId: settingsData.appId ?? '',
            verificationToken: settingsData.hasVerificationToken ? t('channels.feishu.form.maskedVerificationToken') : '',
            encryptEnabled: settingsData.encryptEnabled,
            receiveMode: settingsData.receiveMode ?? 'Http',
            mailboxId: settingsData.mailboxId ?? undefined,
            contactCollectionEnabled: settingsData.contactCollectionEnabled,
            autoReplyEnabled: settingsData.autoReplyEnabled,
            autoReplyMessage: settingsData.autoReplyMessage ?? '',
            enabled: settingsData.enabled,
          });
        }
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : t('channels.common.loadFailed', { channel: t('format.sourceType.feishu') });
        toast.error(message);
      } finally {
        if (!destroyed) setLoading(false);
      }
    };
    void run();
    return () => { destroyed = true; };
  }, [toast, t]);

  const handleSave = async () => {
    if (!form.appId?.trim()) {
      toast.error(t('channels.feishu.validation.appIdRequired'));
      return;
    }
    if (form.appId && !form.appId.startsWith('cli_')) {
      toast.error(t('channels.feishu.validation.appIdPrefix'));
      return;
    }

    // HTTP 模式下校验必填字段
    if (form.receiveMode === 'Http' && form.enabled) {
      if (!settings?.hasAppSecret && !form.appSecret?.trim()) {
        toast.error(t('channels.feishu.validation.appSecretRequired'));
        return;
      }
    }
    // 启用加密时 Encrypt Key 必填
    if (form.encryptEnabled && !settings?.hasEncryptKey && !form.encryptKey?.trim()) {
      toast.error(t('channels.feishu.validation.encryptKeyRequired'));
      return;
    }

    const payload: UpdateFeishuSettingsRequest = { ...form };
    // 敏感字段提交策略
    if (!editingAppSecret && !payload.appSecret?.trim()) payload.appSecret = undefined;
    if (!editingEncryptKey && !payload.encryptKey?.trim()) payload.encryptKey = undefined;
    // VerificationToken：若为脱敏占位符则不提交（后端已存储，无需覆盖）
    if (payload.verificationToken && payload.verificationToken.includes('••••')) payload.verificationToken = undefined;

    try {
      setSaving(true);
      const updated = await feishuApi.updateSettings(payload);
      setSettings(updated);
      setEditingAppSecret(false);
      setEditingEncryptKey(false);
      setForm((prev) => ({
        ...prev,
        appSecret: undefined,
        encryptKey: undefined,
      }));
      toast.success(t('channels.common.saveSucceeded', { channel: t('format.sourceType.feishu') }));
      const newStatus = await feishuApi.getStatus();
      setStatus(newStatus);
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : t('channels.common.saveFailed', { channel: t('format.sourceType.feishu') });
      toast.error(message);
    } finally {
      setSaving(false);
    }
  };

  // 启用开关切换：立即保存 enabled 字段，打开=按配置接入，关闭=停止任何接入
  const handleToggleEnabled = async (next: boolean) => {
    if (isReadOnly) return;
    try {
      setSaving(true);
      const updated = await feishuApi.updateSettings({ enabled: next });
      setSettings(updated);
      setForm((prev) => ({ ...prev, enabled: next }));
      toast.success(next ? t('channels.feishu.toast.enabled') : t('channels.feishu.toast.disabled'));
      const newStatus = await feishuApi.getStatus();
      setStatus(newStatus);
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('channels.feishu.toast.toggleFailed'));
    } finally {
      setSaving(false);
    }
  };

  const handleTestConnection = async () => {
    try {
      setTesting(true);
      const result = await feishuApi.getStatus();
      setStatus(result);
      if (result.tokenValid === true) {
        toast.success(t('channels.feishu.toast.testTokenOk'));
      } else if (result.tokenValid === false) {
        toast.error(t('channels.feishu.toast.testCredentialInvalid'));
      } else if (result.streamConnected) {
        toast.success(t('channels.feishu.toast.testStreamOk'));
      } else if (result.streamError) {
        // 后端自由文本（FeishuStreamClient.SetDisconnected），只翻译前缀
        toast.error(`${t('channels.feishu.toast.testFailedPrefix')}${result.streamError}`);
      } else {
        toast.info(t('channels.feishu.toast.saveFirst'));
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('channels.common.testConnectionFailed'));
    } finally {
      setTesting(false);
    }
  };

  const handleCopyCallbackUrl = async () => {
    if (!status?.callbackUrl) return;
    try {
      await navigator.clipboard.writeText(status.callbackUrl);
      toast.success(t('channels.common.copyCallbackUrl'));
    } catch {
      toast.error(t('channels.common.copyFailed'));
    }
  };

  const configured = status?.configured && status?.enabled;
  // App ID 实时校验
  const appIdInvalid = form.appId && !form.appId.startsWith('cli_');

  if (loading && !settings) {
    return (
      <div className="max-w-4xl mx-auto p-4 sm:p-6">
        <div className="flex flex-col items-center justify-center min-h-[400px] gap-3">
          <div
            className="p-3 rounded-2xl shadow-lg"
            style={{ background: `linear-gradient(135deg, ${FEISHU_PRIMARY}, #1A5DFF)`, boxShadow: `0 10px 30px -5px ${FEISHU_PRIMARY}40` }}
          >
            <FeishuLogo className="w-7 h-7 text-white animate-pulse" />
          </div>
          <div className="flex items-center gap-2 text-gray-500 dark:text-slate-400">
            <Loader2 className="w-4 h-4 animate-spin" />
            <span className="text-sm">{t('channels.feishu.loading')}</span>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto p-4 sm:p-6 space-y-6">
      {/* 页面标题 - 飞书品牌蓝渐变 */}
      <div
        className="relative overflow-hidden rounded-2xl shadow-lg"
        style={{ background: `linear-gradient(135deg, ${FEISHU_PRIMARY} 0%, #1A5DFF 50%, #0052D9 100%)`, boxShadow: `0 10px 30px -5px ${FEISHU_PRIMARY}40` }}
      >
        {/* 装饰性背景图案 */}
        <div className="absolute inset-0 opacity-10 pointer-events-none">
          <svg className="w-full h-full" viewBox="0 0 400 200" preserveAspectRatio="none">
            <circle cx="50" cy="50" r="40" fill="white" />
            <circle cx="350" cy="150" r="60" fill="white" />
            <circle cx="200" cy="100" r="30" fill="white" />
            <path d="M0 150 Q100 100 200 130 T400 120" stroke="white" strokeWidth="2" fill="none" opacity="0.5" />
          </svg>
        </div>
        <div className="relative flex items-center justify-between p-4 sm:p-6">
          <div className="flex items-center gap-3 min-w-0">
            <div className="p-1.5 sm:p-2 bg-white/20 backdrop-blur-sm rounded-xl flex-shrink-0">
              <FeishuLogo className="w-10 h-10 sm:w-12 sm:h-12 text-white" />
            </div>
            <div className="min-w-0">
              <h1 className="text-xl sm:text-2xl font-bold text-white truncate">{t('nav.feishu')}</h1>
              <p className="text-sm text-blue-50/90 truncate">{t('channels.feishu.subtitle')}</p>
            </div>
          </div>
          <button
            onClick={handleRefresh}
            disabled={isRefreshing}
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs sm:text-sm text-white bg-white/20 hover:bg-white/30 backdrop-blur-sm rounded-lg flex-shrink-0 disabled:opacity-60 disabled:cursor-not-allowed active:scale-[0.98] transition-all duration-200"
          >
            <RefreshCw className={`w-4 h-4 ${isRefreshing ? 'animate-spin' : ''}`} />
            <span className="hidden sm:inline">{t('channels.common.refresh')}</span>
          </button>
        </div>
      </div>

      {/* 连接状态卡片 */}
      <div
        className={`rounded-xl border p-4 transition-all duration-300 ${
          configured
            ? 'bg-gradient-to-r from-blue-50/80 to-cyan-50/50 border-blue-200 dark:from-blue-900/20 dark:to-cyan-900/10 dark:border-blue-800/50 shadow-sm'
            : status?.configured && !status?.enabled
            ? 'bg-gradient-to-r from-yellow-50/80 to-amber-50/50 border-yellow-200 dark:from-yellow-900/20 dark:to-amber-900/10 dark:border-yellow-800/50'
            : 'bg-gray-50 border-gray-200 dark:bg-slate-800 dark:border-slate-700'
        }`}
      >
        <div className="flex items-center gap-3 flex-wrap">
          {status?.configured && status?.enabled ? (
            <>
              <div
                className="flex-shrink-0 w-10 h-10 rounded-xl bg-blue-100 dark:bg-blue-900/40 flex items-center justify-center ring-2 ring-blue-200 dark:ring-blue-800/50"
                style={{ boxShadow: status.tokenValid === true ? `0 0 12px ${FEISHU_ACCENT}60` : undefined }}
              >
                <CheckCircle2 className="w-5 h-5 text-blue-600 dark:text-blue-400" />
              </div>
              <div className="flex-1 min-w-0">
                <p className="font-semibold text-blue-900 dark:text-blue-200 flex items-center gap-1.5">
                  {t('channels.feishu.status.connected')}
                  <span className="inline-block w-2 h-2 rounded-full animate-pulse" style={{ backgroundColor: FEISHU_ACCENT }} />
                </p>
                <p className="text-sm text-blue-700 dark:text-blue-400/80">
                  {isStreamMode
                    ? status.streamConnected
                      ? t('channels.feishu.status.streamReady')
                      : t('channels.feishu.status.streamConnecting')
                    : t('channels.common.receivesMessages', { channel: t('format.sourceType.feishu') })}
                </p>
                {/* 断开原因来自后端自由文本（FeishuStreamClient.SetDisconnected），只翻译前缀 */}
                {isStreamMode && status.streamError && !status.streamConnected && (
                  <p className="text-xs text-red-600 dark:text-red-400 mt-0.5">{t('channels.feishu.status.disconnectReasonPrefix')}{status.streamError}</p>
                )}
              </div>
              {/* 接收模式标签 */}
              {status?.receiveMode && (
                <div className="flex items-center gap-1.5 px-3 py-1.5 bg-white/80 dark:bg-slate-900/50 rounded-full flex-shrink-0 ring-1 ring-blue-200 dark:ring-blue-800/50">
                  {status.receiveMode === 'Stream' ? (
                    <Radio className="w-3.5 h-3.5 text-green-600 dark:text-green-400" />
                  ) : (
                    <Wifi className="w-3.5 h-3.5 text-blue-600 dark:text-blue-400" />
                  )}
                  <span className="text-xs font-medium text-gray-700 dark:text-slate-300">
                    {status.receiveMode === 'Stream' ? t('channels.feishu.mode.streamLabel') : t('channels.feishu.mode.httpLabel')}
                  </span>
                </div>
              )}
              {/* Token 状态 */}
              {status.tokenValid === true && (
                <div className="flex items-center gap-1.5 px-3 py-1.5 bg-blue-100 dark:bg-blue-900/40 rounded-full flex-shrink-0 ring-1 ring-blue-200 dark:ring-blue-800/50" title={t('channels.feishu.status.tokenOkTooltip')}>
                  <CheckCircle2 className="w-3.5 h-3.5 text-blue-600 dark:text-blue-400" />
                  <span className="text-xs font-medium text-blue-700 dark:text-blue-300">{t('channels.common.tokenValid')}</span>
                </div>
              )}
              {status.tokenValid === false && (
                <div className="flex items-center gap-1.5 px-3 py-1.5 bg-red-100 dark:bg-red-900/40 rounded-full flex-shrink-0 ring-1 ring-red-200 dark:ring-red-800/50" title={t('channels.feishu.status.tokenFailTooltip')}>
                  <AlertCircle className="w-3.5 h-3.5 text-red-600 dark:text-red-400" />
                  <span className="text-xs font-medium text-red-700 dark:text-red-300">{t('channels.common.tokenInvalid')}</span>
                </div>
              )}
              {/* Stream 模式额外连接状态 */}
              {isStreamMode && (
                <div
                  className={`flex items-center gap-1.5 px-3 py-1.5 rounded-full flex-shrink-0 ring-1 ${
                    status.streamConnected
                      ? 'bg-green-100 dark:bg-green-900/40 ring-green-200 dark:ring-green-800/50'
                      : 'bg-amber-100 dark:bg-amber-900/40 ring-amber-200 dark:ring-amber-800/50'
                  }`}
                  title={status.streamConnected ? t('channels.feishu.status.wsConnectedTooltip') : t('channels.feishu.status.wsDisconnectedTooltip')}
                >
                  {status.streamConnected ? (
                    <CheckCircle2 className="w-3.5 h-3.5 text-green-600 dark:text-green-400" />
                  ) : (
                    <Loader2 className="w-3.5 h-3.5 text-amber-600 dark:text-amber-400 animate-spin" />
                  )}
                  <span className={`text-xs font-medium ${status.streamConnected ? 'text-green-700 dark:text-green-300' : 'text-amber-700 dark:text-amber-300'}`}>
                    {status.streamConnected ? t('channels.feishu.status.wsConnected') : t('channels.feishu.status.wsConnecting')}
                  </span>
                </div>
              )}
            </>
          ) : status?.configured && !status?.enabled ? (
            <>
              <div className="flex-shrink-0 w-10 h-10 rounded-xl bg-yellow-100 dark:bg-yellow-900/40 flex items-center justify-center ring-2 ring-yellow-200 dark:ring-yellow-800/50">
                <AlertCircle className="w-5 h-5 text-yellow-600 dark:text-yellow-400" />
              </div>
              <div className="flex-1 min-w-0">
                <p className="font-semibold text-yellow-900 dark:text-yellow-200">{t('channels.common.configuredButDisabled')}</p>
                <p className="text-sm text-yellow-700 dark:text-yellow-400/80">{t('channels.feishu.status.disabledHint')}</p>
              </div>
            </>
          ) : (
            <>
              <div className="flex-shrink-0 w-10 h-10 rounded-xl bg-gray-100 dark:bg-slate-700 flex items-center justify-center ring-2 ring-gray-200 dark:ring-slate-600">
                <AlertCircle className="w-5 h-5 text-gray-500 dark:text-slate-400" />
              </div>
              <div className="flex-1 min-w-0">
                <p className="font-semibold text-gray-900 dark:text-slate-100">{t('channels.common.notConfigured')}</p>
                <p className="text-sm text-gray-600 dark:text-slate-400">{t('channels.feishu.status.unconfiguredHint')}</p>
              </div>
            </>
          )}
        </div>
      </div>

      {/* 支持的消息类型 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4 sm:p-6 shadow-sm">
        <h2 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2 mb-3">
          <Zap className="w-4 h-4 sm:w-5 sm:h-5" style={{ color: FEISHU_PRIMARY }} />
          {t('channels.common.messageTypes')}
        </h2>
        <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
          {MESSAGE_TYPES.map((type) => {
            const Icon = type.icon;
            return (
              <div
                key={type.labelKey}
                className="flex items-center gap-2.5 p-3 rounded-lg bg-gradient-to-br from-blue-50/50 to-cyan-50/30 dark:from-slate-700/50 dark:to-slate-700/20 border border-blue-100/50 dark:border-slate-600/50 hover:from-blue-50 hover:to-cyan-50/50 dark:hover:from-slate-700 dark:hover:to-slate-700/30 transition-colors"
              >
                <div className="flex-shrink-0 w-8 h-8 rounded-lg bg-blue-100 dark:bg-blue-900/40 flex items-center justify-center">
                  <Icon className="w-4 h-4 text-blue-600 dark:text-blue-400" />
                </div>
                <div className="min-w-0">
                  <p className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate">{t(type.labelKey)}</p>
                  <p className="text-xs text-gray-400 dark:text-slate-500 truncate">{t(type.descKey)}</p>
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* 配置表单 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4 sm:p-6 space-y-5 shadow-sm">
        <h2 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2">
          <Settings className="w-4 h-4 sm:w-5 sm:h-5" style={{ color: FEISHU_PRIMARY }} />
          {t('channels.common.appConfig')}
        </h2>

        {/* 启用开关 */}
        <div className="flex items-center justify-between py-2">
          <div>
            <label className="text-sm font-medium text-gray-900 dark:text-slate-100">{t('channels.common.enableChannel', { channel: t('format.sourceType.feishu') })}</label>
            <p className="text-xs text-gray-500 dark:text-slate-400">{t('channels.feishu.form.enableHint')}</p>
          </div>
          <button
            type="button"
            onClick={() => void handleToggleEnabled(!form.enabled)}
            disabled={saving}
            className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors disabled:opacity-50 ${!form.enabled ? 'bg-gray-300 dark:bg-slate-600' : ''}`}
            style={{ backgroundColor: form.enabled ? FEISHU_PRIMARY : undefined }}
          >
            <span className={`inline-block h-4 w-4 transform rounded-full bg-white shadow-sm transition-transform ${form.enabled ? 'translate-x-6' : 'translate-x-1'}`} />
          </button>
        </div>

        {/* ===== 消息接收模式选择器（可视化卡片）===== */}
        <div className="space-y-3">
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
              {t('channels.common.receiveMode')}
            </label>
            <p className="text-xs text-gray-500 dark:text-slate-400 mb-3">
              {t('channels.feishu.form.receiveModeHint')}
            </p>
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
            {RECEIVE_MODES.map((mode) => {
              const Icon = mode.icon;
              const selected = currentMode === mode.value;
              return (
                <button
                  key={mode.value}
                  type="button"
                  onClick={() => setForm((prev) => ({ ...prev, receiveMode: mode.value }))}
                  className={`relative text-left p-4 rounded-xl border-2 transition-all ${
                    selected
                      ? 'border-blue-500 bg-blue-50/50 dark:border-blue-400 dark:bg-blue-900/20 shadow-sm shadow-blue-500/10'
                      : 'border-gray-200 dark:border-slate-600 hover:border-gray-300 dark:hover:border-slate-500 bg-white dark:bg-slate-900/40'
                  }`}
                >
                  {selected && (
                    <div className="absolute top-3 right-3 w-5 h-5 rounded-full bg-blue-600 flex items-center justify-center">
                      <CheckCircle2 className="w-3.5 h-3.5 text-white" />
                    </div>
                  )}
                  <div className="flex items-center gap-2.5 mb-2">
                    <div className={`flex-shrink-0 w-9 h-9 rounded-lg flex items-center justify-center ${
                      selected ? 'bg-blue-100 dark:bg-blue-900/40' : 'bg-gray-100 dark:bg-slate-700'
                    }`}>
                      <Icon className={`w-4.5 h-4.5 ${selected ? 'text-blue-600 dark:text-blue-400' : 'text-gray-500 dark:text-slate-400'}`} />
                    </div>
                    <div className="flex items-center gap-2">
                      <span className={`text-sm font-semibold ${selected ? 'text-blue-900 dark:text-blue-200' : 'text-gray-900 dark:text-slate-100'}`}>
                        {t(mode.labelKey)}
                      </span>
                      {mode.badgeKey && (
                        <span className={`px-1.5 py-0.5 text-[10px] font-bold rounded-full ${mode.badgeClass}`}>
                          {t(mode.badgeKey)}
                        </span>
                      )}
                    </div>
                  </div>
                  <p className="text-xs text-gray-500 dark:text-slate-400 mb-2">{t(mode.descKey)}</p>
                  <ul className="space-y-1">
                    {mode.featureKeys.map((f, i) => (
                      <li key={i} className="flex items-start gap-1.5 text-xs text-gray-600 dark:text-slate-400">
                        <CheckCircle2 className="w-3 h-3 text-green-500 dark:text-green-400 flex-shrink-0 mt-0.5" />
                        <span>{t(f)}</span>
                      </li>
                    ))}
                  </ul>
                </button>
              );
            })}
          </div>
          <a
            href={currentModeDef.docUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-1 text-xs text-blue-600 dark:text-blue-400 hover:underline"
          >
            <Info className="w-3 h-3" />
            {t('channels.common.docLinkPrefix')}
            {t(currentModeDef.labelKey)}
            {t('channels.common.docLinkSuffix')}
            <ExternalLink className="w-3 h-3" />
          </a>
        </div>

        {/* ===== Stream 模式提示 ===== */}
        {isStreamMode && (
          <div className="flex items-start gap-3 p-3 bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800/50 rounded-lg">
            <ShieldCheck className="w-4 h-4 text-green-600 dark:text-green-400 flex-shrink-0 mt-0.5" />
            <div className="text-xs text-green-700 dark:text-green-300 space-y-1">
              <p className="font-medium">{t('channels.feishu.guide.streamSelected')}</p>
              <p>{t('channels.feishu.guide.streamSelectedHint')}</p>
              <p>{t('channels.feishu.guide.subscribeEventPrefix')}<code className="px-1 py-0.5 bg-green-100 dark:bg-green-900/40 rounded text-[11px] font-mono">im.message.receive_v1</code>{t('channels.feishu.guide.subscribeEventSuffix')}</p>
            </div>
          </div>
        )}

        {/* ===== HTTP 模式专属提示 ===== */}
        {isHttpMode && (
          <div className="flex items-start gap-3 p-3 bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800/50 rounded-lg">
            <ShieldCheck className="w-4 h-4 text-blue-600 dark:text-blue-400 flex-shrink-0 mt-0.5" />
            <div className="text-xs text-blue-700 dark:text-blue-300 space-y-1">
              <p className="font-medium">{t('channels.feishu.guide.httpSelected')}</p>
              <p>
                {t('channels.common.httpNeedTokenPrefix')}<strong>Verification Token</strong>{t('channels.feishu.guide.httpNeedTokenSuffix')}
              </p>
              <p>{t('channels.feishu.guide.httpNeedEncryptPrefix')}<strong>Encrypt Key</strong>{t('channels.feishu.guide.httpNeedEncryptSuffix')}</p>
            </div>
          </div>
        )}

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
          {/* App ID */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
              App ID <span className="text-red-500">*</span>
            </label>
            <input
              type="text"
              value={form.appId ?? ''}
              onChange={(e) => setForm((prev) => ({ ...prev, appId: e.target.value }))}
              placeholder="cli_xxxxxxxxxxxxxxxx"
              className={`w-full px-3 py-2 bg-white dark:bg-slate-900 border rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:border-transparent transition-shadow ${
                appIdInvalid
                  ? 'border-amber-300 dark:border-amber-700 focus:ring-amber-400'
                  : 'border-gray-300 dark:border-slate-600 focus:ring-blue-500 dark:focus:ring-blue-400'
              }`}
            />
            {appIdInvalid ? (
              <p className="text-xs text-amber-600 dark:text-amber-400 mt-1 flex items-center gap-1">
                <AlertCircle className="w-3 h-3" />
                {t('channels.feishu.form.appIdInvalid')}
              </p>
            ) : (
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('channels.feishu.form.appIdHint')}</p>
            )}
          </div>

          {/* App Secret */}
          <div>
            <label className="flex items-center justify-between text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
              <span>App Secret</span>
              {settings?.hasAppSecret && !editingAppSecret && (
                <span className="text-xs text-blue-600 dark:text-blue-400 flex items-center gap-1">
                  <Lock className="w-3 h-3" /> {t('channels.common.configured')}
                </span>
              )}
            </label>
            <div className="flex gap-2">
              <input
                type="password"
                value={settings?.hasAppSecret && !editingAppSecret ? '••••••••••••' : (form.appSecret ?? '')}
                onChange={(e) => setForm((prev) => ({ ...prev, appSecret: e.target.value }))}
                readOnly={settings?.hasAppSecret && !editingAppSecret}
                placeholder={settings?.hasAppSecret ? t('channels.common.maskedConfiguredEditable') : t('channels.feishu.form.appSecretPlaceholder')}
                className={`flex-1 px-3 py-2 bg-white dark:bg-slate-900 border rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-blue-500 dark:focus:ring-blue-400 focus:border-transparent transition-shadow ${
                  settings?.hasAppSecret && !editingAppSecret
                    ? 'border-blue-300 dark:border-blue-700 bg-blue-50/50 dark:bg-blue-900/10'
                    : 'border-gray-300 dark:border-slate-600'
                }`}
              />
              {settings?.hasAppSecret && (
                <button
                  type="button"
                  onClick={() => {
                    setEditingAppSecret(!editingAppSecret);
                    setForm((prev) => ({ ...prev, appSecret: '' }));
                  }}
                  className="px-3 py-2 text-sm text-gray-600 dark:text-slate-300 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors"
                  title={editingAppSecret ? t('channels.common.lockSecret') : t('channels.common.unlockEdit')}
                >
                  {editingAppSecret ? <Unlock className="w-4 h-4" /> : <Lock className="w-4 h-4" />}
                </button>
              )}
            </div>
            <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('channels.feishu.form.appSecretHint')}</p>
          </div>

          {/* ===== HTTP 模式专属字段：Verification Token ===== */}
          {isHttpMode && (
            <div className="md:col-span-2">
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
                Verification Token <span className="text-red-500">*</span>
              </label>
              <input
                type="text"
                value={form.verificationToken ?? ''}
                onChange={(e) => setForm((prev) => ({ ...prev, verificationToken: e.target.value }))}
                placeholder={t('channels.feishu.form.verificationTokenPlaceholder')}
                className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-blue-500 dark:focus:ring-blue-400 focus:border-transparent transition-shadow"
              />
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('channels.feishu.form.verificationTokenHint')}</p>
            </div>
          )}

          {/* ===== 事件加密（仅 HTTP 模式显示）===== */}
          {isHttpMode && (
            <div className="md:col-span-2 border-t border-gray-100 dark:border-slate-700 pt-4 space-y-3">
              <div className="flex items-center justify-between">
                <div>
                  <label className="text-sm font-medium text-gray-900 dark:text-slate-100">{t('channels.feishu.form.encryptEnabledLabel')}</label>
                  <p className="text-xs text-gray-500 dark:text-slate-400">{t('channels.feishu.form.encryptEnabledHint')}</p>
                </div>
                <button
                  type="button"
                  onClick={() => setForm((prev) => ({ ...prev, encryptEnabled: !prev.encryptEnabled }))}
                  className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${!form.encryptEnabled ? 'bg-gray-300 dark:bg-slate-600' : ''}`}
                  style={{ backgroundColor: form.encryptEnabled ? FEISHU_PRIMARY : undefined }}
                >
                  <span className={`inline-block h-4 w-4 transform rounded-full bg-white shadow-sm transition-transform ${form.encryptEnabled ? 'translate-x-6' : 'translate-x-1'}`} />
                </button>
              </div>

              {form.encryptEnabled && (
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
                    Encrypt Key <span className="text-red-500">*</span>
                  </label>
                  <div className="flex gap-2 items-center">
                    <input
                      type={showEncryptKey ? 'text' : 'password'}
                      value={form.encryptKey ?? ''}
                      onChange={(e) => setForm((prev) => ({ ...prev, encryptKey: e.target.value }))}
                      disabled={settings?.hasEncryptKey && !editingEncryptKey}
                      placeholder={settings?.hasEncryptKey ? t('channels.common.maskedConfigured') : t('channels.feishu.form.encryptKeyPlaceholder')}
                      className="flex-1 px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-blue-500 dark:focus:ring-blue-400 focus:border-transparent disabled:bg-gray-50 dark:disabled:bg-slate-800 dark:disabled:text-slate-500 transition-shadow"
                    />
                    {settings?.hasEncryptKey && !editingEncryptKey && (
                      <span className="text-xs text-gray-400 dark:text-slate-500 flex items-center gap-1 whitespace-nowrap">
                        <Lock className="w-3 h-3" /> {t('channels.common.configured')}
                      </span>
                    )}
                    {settings?.hasEncryptKey && (
                      <>
                        <button
                          type="button"
                          onClick={() => setShowEncryptKey((v) => !v)}
                          disabled={!editingEncryptKey}
                          className="px-3 py-2 text-sm text-gray-600 dark:text-slate-300 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors disabled:opacity-40"
                          title={showEncryptKey ? t('channels.common.hide') : t('channels.common.show')}
                        >
                          {showEncryptKey ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                        </button>
                        <button
                          type="button"
                          onClick={() => {
                            setEditingEncryptKey(!editingEncryptKey);
                            setForm((prev) => ({ ...prev, encryptKey: '' }));
                          }}
                          className="px-3 py-2 text-sm text-gray-600 dark:text-slate-300 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors"
                          title={editingEncryptKey ? t('channels.common.cancelEdit') : t('channels.common.editSecret')}
                        >
                          {editingEncryptKey ? <X className="w-4 h-4" /> : <Pencil className="w-4 h-4" />}
                        </button>
                      </>
                    )}
                  </div>
                  <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('channels.feishu.form.encryptKeyHint')}</p>
                </div>
              )}
            </div>
          )}

          {/* 关联邮箱 */}
          <div className={isHttpMode ? '' : 'md:col-span-2'}>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('channels.common.linkedMailbox')}</label>
            <select
              value={form.mailboxId ?? ''}
              onChange={(e) => setForm((prev) => ({ ...prev, mailboxId: e.target.value === '' ? undefined : Number(e.target.value) }))}
              className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500 dark:focus:ring-blue-400 focus:border-transparent transition-shadow"
            >
              <option value="">{t('channels.common.autoAssignMailbox')}</option>
              {mailboxes.map((m) => (
                <option key={m.id} value={m.id} disabled={!m.isActive}>
                  {m.name} ({m.email}){m.isActive ? '' : t('channels.common.disabledSuffix')}
                </option>
              ))}
            </select>
            <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('channels.common.mailboxHint', { channel: t('format.sourceType.feishu') })}</p>
          </div>
        </div>

        {/* ===== 回调地址（仅 HTTP 模式显示）===== */}
        {isHttpMode && status?.callbackUrl && (
          <div className="border-t border-gray-100 dark:border-slate-700 pt-4 space-y-3">
            <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2">
              <ExternalLink className="w-4 h-4 text-gray-400 dark:text-slate-500" />
              {t('channels.common.callbackAddress')}
            </h3>
            <p className="text-sm text-gray-500 dark:text-slate-400">
              {t('channels.feishu.form.callbackHint')}
            </p>
            <div className="flex items-center gap-2">
              <code className="flex-1 px-3 py-2 bg-gray-50 dark:bg-slate-900/60 border border-gray-200 dark:border-slate-700 rounded-lg text-sm text-gray-700 dark:text-slate-300 break-all font-mono">
                {status.callbackUrl}
              </code>
              <button
                onClick={handleCopyCallbackUrl}
                className="flex-shrink-0 p-2 text-gray-500 dark:text-slate-400 hover:bg-gray-100 dark:hover:bg-slate-700 rounded-lg transition-colors"
                title={t('channels.common.copy')}
              >
                <Copy className="w-4 h-4" />
              </button>
            </div>
            {status.callbackUrl && (status.callbackUrl.includes('localhost') || status.callbackUrl.includes('127.0.0.1')) && (
              <div className="flex items-start gap-2 p-3 bg-amber-50 dark:bg-amber-900/30 border border-amber-200 dark:border-amber-800/50 rounded-lg text-xs text-amber-700 dark:text-amber-300">
                <AlertCircle className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
                <span>{t('channels.feishu.form.localCallbackWarning')}</span>
              </div>
            )}
          </div>
        )}

        {/* ===== Stream 模式无需回调地址提示 ===== */}
        {isStreamMode && (
          <div className="border-t border-gray-100 dark:border-slate-700 pt-4">
            <div className="flex items-start gap-3 p-3 bg-gray-50 dark:bg-slate-700/30 border border-gray-200 dark:border-slate-600/50 rounded-lg">
              <Radio className="w-4 h-4 text-gray-400 dark:text-slate-500 flex-shrink-0 mt-0.5" />
              <div className="text-xs text-gray-500 dark:text-slate-400">
                <p className="font-medium text-gray-600 dark:text-slate-300">{t('channels.feishu.form.streamNoCallbackTitle')}</p>
                <p className="mt-0.5">{t('channels.common.streamNoCallbackHint')}</p>
              </div>
            </div>
          </div>
        )}

        {/* 客户信息及联系方式收集功能 */}
        <div className="border-t border-gray-100 dark:border-slate-700 pt-4">
          <div
            className={`relative overflow-hidden rounded-xl border p-4 transition-all duration-300 ${
              form.contactCollectionEnabled
                ? 'bg-gradient-to-r from-blue-50/80 to-cyan-50/50 border-blue-200 dark:from-blue-900/20 dark:to-cyan-900/10 dark:border-blue-800/50 shadow-sm shadow-blue-500/5'
                : 'bg-gray-50/80 border-gray-200 dark:bg-slate-800/50 dark:border-slate-700'
            }`}
          >
            <div className="flex items-start gap-3">
              {/* 图标 */}
              <div
                className={`flex-shrink-0 w-10 h-10 rounded-xl flex items-center justify-center transition-all duration-300 ${
                  form.contactCollectionEnabled
                    ? 'bg-blue-100 dark:bg-blue-900/40 ring-2 ring-blue-200 dark:ring-blue-800/50'
                    : 'bg-gray-200 dark:bg-slate-700'
                }`}
              >
                <UserCheck
                  className={`w-5 h-5 transition-colors ${
                    form.contactCollectionEnabled
                      ? 'text-blue-600 dark:text-blue-400'
                      : 'text-gray-500 dark:text-slate-400'
                  }`}
                />
              </div>

              {/* 文案 */}
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 mb-0.5">
                  <label className="text-sm font-semibold text-gray-900 dark:text-slate-100">
                    {t('channels.common.contactCollection')}
                  </label>
                  <span
                    className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium transition-all ${
                      form.contactCollectionEnabled
                        ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300'
                        : 'bg-gray-200 text-gray-600 dark:bg-slate-700 dark:text-slate-400'
                    }`}
                  >
                    <span
                      className={`inline-block w-1.5 h-1.5 rounded-full ${
                        form.contactCollectionEnabled ? 'bg-blue-500 animate-pulse' : 'bg-gray-400'
                      }`}
                    />
                    {form.contactCollectionEnabled ? t('channels.common.on') : t('channels.common.off')}
                  </span>
                </div>
                <p className="text-xs text-gray-500 dark:text-slate-400 leading-relaxed">
                  {t('channels.common.contactCollectionHint')}
                </p>
              </div>

              {/* 开关 */}
              <button
                type="button"
                role="switch"
                aria-checked={form.contactCollectionEnabled}
                onClick={() => setForm((prev) => ({ ...prev, contactCollectionEnabled: !prev.contactCollectionEnabled }))}
                className={`relative inline-flex h-6 w-11 flex-shrink-0 items-center rounded-full transition-all duration-300 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 dark:focus:ring-offset-slate-900 ${
                  form.contactCollectionEnabled
                    ? 'bg-blue-600 shadow-md shadow-blue-500/30'
                    : 'bg-gray-300 dark:bg-slate-600'
                }`}
                style={{ backgroundColor: form.contactCollectionEnabled ? FEISHU_PRIMARY : undefined }}
              >
                <span
                  className={`inline-block h-5 w-5 transform rounded-full bg-white shadow-sm transition-transform duration-300 ${
                    form.contactCollectionEnabled ? 'translate-x-5' : 'translate-x-0.5'
                  }`}
                />
              </button>
            </div>
          </div>
        </div>

        {/* 自动回复 */}
        <div className="border-t border-gray-100 dark:border-slate-700 pt-4 space-y-3">
          <div className="flex items-center justify-between">
            <div>
              <label className="text-sm font-medium text-gray-900 dark:text-slate-100">{t('channels.common.enableAutoReply')}</label>
              <p className="text-xs text-gray-500 dark:text-slate-400">{t('channels.common.autoReplyHint')}</p>
            </div>
            <button
              type="button"
              onClick={() => setForm((prev) => ({ ...prev, autoReplyEnabled: !prev.autoReplyEnabled }))}
              className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${!form.autoReplyEnabled ? 'bg-gray-300 dark:bg-slate-600' : ''}`}
              style={{ backgroundColor: form.autoReplyEnabled ? FEISHU_PRIMARY : undefined }}
            >
              <span className={`inline-block h-4 w-4 transform rounded-full bg-white shadow-sm transition-transform ${form.autoReplyEnabled ? 'translate-x-6' : 'translate-x-1'}`} />
            </button>
          </div>
          {form.autoReplyEnabled && (
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('channels.common.autoReplyContent')}</label>
              <textarea
                value={form.autoReplyMessage ?? ''}
                onChange={(e) => setForm((prev) => ({ ...prev, autoReplyMessage: e.target.value }))}
                rows={3}
                maxLength={500}
                placeholder={t('channels.common.autoReplyPlaceholder')}
                className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-blue-500 dark:focus:ring-blue-400 focus:border-transparent resize-none transition-shadow"
              />
              <div className="flex items-center justify-between mt-1">
                <p className="text-xs text-gray-400 dark:text-slate-500">{t('channels.common.robotAutoReplyHint')}</p>
                <span className={`text-xs ${
                  (form.autoReplyMessage?.length ?? 0) > 500 * 0.9 ? 'text-red-500 dark:text-red-400' : 'text-gray-400 dark:text-slate-500'
                }`}>
                  {form.autoReplyMessage?.length ?? 0}/500
                </span>
              </div>
            </div>
          )}
        </div>

        {/* 动作区：测试连接 + 保存 */}
        <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-2 pt-2 border-t border-gray-100 dark:border-slate-700">
          <button
            onClick={handleTestConnection}
            disabled={testing || isReadOnly || !settings?.hasAppSecret}
            className="flex items-center justify-center gap-2 w-full sm:w-auto px-4 py-2 text-xs sm:text-sm font-medium text-blue-700 dark:text-blue-400 bg-blue-50 dark:bg-blue-900/30 hover:bg-blue-100 dark:hover:bg-blue-900/50 rounded-lg transition-colors disabled:opacity-40 disabled:cursor-not-allowed border border-blue-200 dark:border-blue-800/50"
            title={t('channels.feishu.form.testConnectionTooltip')}
          >
            {testing ? <Loader2 className="w-4 h-4 animate-spin" /> : <Zap className="w-4 h-4" />}
            {testing ? t('channels.common.testing') : t('channels.common.testConnection')}
          </button>
          <button
            onClick={handleSave}
            disabled={saving || isReadOnly}
            className="flex items-center justify-center gap-2 w-full sm:w-auto px-5 py-2 text-white rounded-lg text-xs sm:text-sm font-medium shadow-sm transition-all hover:shadow-md disabled:opacity-50"
            style={{ background: `linear-gradient(to right, ${FEISHU_PRIMARY}, #1A5DFF)` }}
          >
            {saving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
            {t('channels.common.saveConfig')}
          </button>
        </div>
      </div>

      {/* 配置指南 */}
      <div className="bg-gradient-to-br from-blue-50/80 to-cyan-50/50 dark:from-blue-900/20 dark:to-cyan-900/10 rounded-xl border border-blue-200 dark:border-blue-800/50 p-4 sm:p-6 space-y-4 shadow-sm">
        <div className="flex items-center gap-3 pb-1">
          <div className="p-1.5 rounded-lg shadow-sm" style={{ backgroundColor: FEISHU_PRIMARY }}>
            <FeishuLogo className="w-5 h-5 sm:w-6 sm:h-6 text-white" />
          </div>
          <h2 className="text-sm sm:text-base font-semibold text-blue-900 dark:text-blue-200">{t('channels.common.configGuide')}</h2>
          <span className="ml-auto text-xs text-blue-600 dark:text-blue-400 font-medium">
            {t('channels.common.currentMode')}{t(currentModeDef.labelKey)}
          </span>
        </div>
        <ol className="space-y-2.5 text-sm text-blue-800 dark:text-blue-300">
          {(isStreamMode ? [
            <>{t('channels.feishu.guide.stepLogin')}</>,
            <>{t('channels.feishu.guide.stepCredentials')}</>,
            <>{t('channels.feishu.guide.stepSubscribePrefix')}<strong>im.message.receive_v1</strong>{t('channels.feishu.guide.stepSubscribeSuffix')}</>,
            <>{t('channels.feishu.guide.stepPushModePrefix')}<strong>{t('channels.feishu.mode.streamLabel')}</strong>{t('channels.feishu.guide.stepPushModeSuffix')}</>,
            <>{t('channels.common.permissionsPrefix')}<code className="px-1 py-0.5 bg-blue-100 dark:bg-blue-900/40 rounded text-[11px] font-mono">im:message</code>{t('channels.common.permissionsSeparator')}<code className="px-1 py-0.5 bg-blue-100 dark:bg-blue-900/40 rounded text-[11px] font-mono">im:message:send_as_bot</code>{t('channels.feishu.guide.stepPermissionsSuffix')}</>,
            <>{t('channels.common.releaseAndAwaitApproval')}</>,
            <>{t('channels.feishu.guide.stepSelectStream')}</>,
            <>{t('channels.feishu.guide.stepStreamLog')}</>,
          ] : [
            <>{t('channels.feishu.guide.stepLogin')}</>,
            <>{t('channels.feishu.guide.stepCredentials')}</>,
            <>{t('channels.feishu.guide.stepTokenPrefix')}<strong>Verification Token</strong>{t('channels.feishu.guide.stepTokenSuffix')}</>,
            <>{t('channels.feishu.guide.stepEncryptPrefix')}<strong>Encrypt Key</strong>{t('channels.feishu.guide.stepEncryptSuffix')}</>,
            <>{t('channels.feishu.guide.stepCallbackUrl')}</>,
            <>{t('channels.feishu.guide.stepSubscribePrefix')}<strong>im.message.receive_v1</strong>{t('channels.feishu.guide.stepSubscribeSuffix')}</>,
            <>{t('channels.common.permissionsPrefix')}<code className="px-1 py-0.5 bg-blue-100 dark:bg-blue-900/40 rounded text-[11px] font-mono">im:message</code>{t('channels.common.permissionsSeparator')}<code className="px-1 py-0.5 bg-blue-100 dark:bg-blue-900/40 rounded text-[11px] font-mono">im:message:send_as_bot</code>{t('channels.feishu.guide.stepPermissionsSuffix')}</>,
            <>{t('channels.common.releaseAndAwaitApproval')}</>,
            <>{t('channels.feishu.guide.stepSelectHttp')}</>,
          ]).map((step, idx) => (
            <li key={idx} className="flex items-start gap-2.5">
              <span className="flex-shrink-0 w-5 h-5 rounded-full bg-blue-200 dark:bg-blue-800/60 text-blue-700 dark:text-blue-300 text-[11px] font-bold flex items-center justify-center mt-0.5">
                {idx + 1}
              </span>
              <span className="flex-1">{step}</span>
            </li>
          ))}
        </ol>
        <div className="mt-2 p-3 bg-amber-50 dark:bg-amber-900/30 border border-amber-200 dark:border-amber-800/50 rounded-lg text-xs text-amber-700 dark:text-amber-300 flex items-start gap-2">
          <AlertCircle className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
          <span><strong>{t('channels.common.noteLabel')}</strong>{t('channels.feishu.guide.noticeBody')}</span>
        </div>
      </div>
    </div>
  );
}
