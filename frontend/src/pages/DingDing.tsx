import { useState, useEffect, useCallback } from 'react';
import { dingdingApi, mailboxApi } from '../api';
import type {
  DingDingSettingsDto,
  DingDingStatusDto,
  UpdateDingDingSettingsRequest,
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

/** 钉钉官方 logo 组件（蓝色圆形 + 白色闪电蜂鸟） */
function DingTalkLogo({ className = 'w-7 h-7' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="currentColor" xmlns="http://www.w3.org/2000/svg">
      <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm4.18 3.06l-4.2 6.3h3.06c.32 0 .52.36.34.64l-5.34 8.06c-.2.3-.66.12-.58-.22l1.16-4.92H7.6c-.32 0-.52-.34-.36-.62l5.3-8.9c.1-.16.28-.28.48-.28h2.76c.34 0 .54.38.4.66l-.78 1.28h2.78z" />
    </svg>
  );
}

/**
 * 支持的消息类型列表。
 *
 * 字段名带 `Key` 后缀并持有 `TranslationKey`（不持有文案）：漏包 `t()` 会被 `tsc` 以
 * TS2339 抓出，直接渲染则会把 key 本身漏到界面上。
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
  { icon: Link2, labelKey: 'channels.dingding.msgType.link', descKey: 'channels.dingding.msgType.linkDesc' },
];

/**
 * 消息接收模式定义。
 *
 * `value` 是发给后端的稳定码（`Stream` / `Http`，见 `DingDingOptions.cs:37`、
 * `DingDingService.cs:158`），**不随界面语言变化**，保持字面量；
 * 其余文案字段均持有 `TranslationKey`。
 */
type ReceiveModeDef = {
  value: string;
  labelKey: TranslationKey;
  icon: typeof Radio;
  badgeKey: TranslationKey | null;
  badgeClass: string;
  descKey: TranslationKey;
  featureKeys: TranslationKey[];
  requiredFields: string[];
  docUrl: string;
};

const RECEIVE_MODES: ReceiveModeDef[] = [
  {
    value: 'Stream',
    labelKey: 'channels.dingding.mode.streamLabel',
    icon: Radio,
    badgeKey: 'channels.common.recommended',
    badgeClass: 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300',
    descKey: 'channels.common.wsLongConnection',
    featureKeys: [
      'channels.common.noPublicAddress',
      'channels.dingding.mode.streamFeatureAes',
      'channels.dingding.mode.streamFeatureLatency',
      'channels.common.autoReconnect',
    ],
    requiredFields: [] as string[],
    docUrl: 'https://open.dingtalk.com/document/orgapp/configure-stream-push',
  },
  {
    value: 'Http',
    labelKey: 'channels.dingding.mode.httpLabel',
    icon: Wifi,
    badgeKey: null,
    badgeClass: '',
    descKey: 'channels.dingding.mode.httpDesc',
    featureKeys: [
      'channels.common.needPublicCallbackUrl',
      'channels.dingding.mode.httpFeatureAes',
      'channels.dingding.mode.httpFeatureGet',
      'channels.dingding.mode.httpFeatureWebhook',
    ],
    requiredFields: ['CallbackToken', 'EncodingAesKey'],
    docUrl: 'https://open.dingtalk.com/document/orgapp/configure-http-push',
  },
];

export default function DingDing() {
  const toast = useToast();
  const { t } = useI18n();
  const { isReadOnly } = usePermission();
  const [settings, setSettings] = useState<DingDingSettingsDto | null>(null);
  const [status, setStatus] = useState<DingDingStatusDto | null>(null);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [refreshLock, setRefreshLock] = useState(false);
  const isRefreshing = loading || refreshLock;

  // 敏感字段显示控制
  const [showCallbackToken, setShowCallbackToken] = useState(false);
  const [showEncodingAesKey, setShowEncodingAesKey] = useState(false);
  // 敏感字段编辑状态（空值表示不修改）
  const [editingAppSecret, setEditingAppSecret] = useState(false);
  const [editingCallbackToken, setEditingCallbackToken] = useState(false);
  const [editingEncodingAesKey, setEditingEncodingAesKey] = useState(false);

  // 配置表单状态
  const [form, setForm] = useState<UpdateDingDingSettingsRequest>({});

  // 当前选中的接收模式（form.receiveMode 优先，fallback 到 status）
  const currentMode = form.receiveMode ?? status?.receiveMode ?? 'Http';
  const currentModeDef = RECEIVE_MODES.find((m) => m.value === currentMode) ?? RECEIVE_MODES[0];
  const isHttpMode = currentMode === 'Http';
  const isStreamMode = currentMode === 'Stream';

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const [settingsData, statusData, mailboxData] = await Promise.all([
        dingdingApi.getSettings(),
        dingdingApi.getStatus(),
        mailboxApi.list(),
      ]);
      setSettings(settingsData);
      setStatus(statusData);
      setMailboxes(mailboxData);
      setForm({
        appKey: settingsData.appKey ?? '',
        agentId: settingsData.agentId ?? undefined,
        robotCode: settingsData.robotCode ?? '',
        receiveMode: settingsData.receiveMode ?? 'Http',
        mailboxId: settingsData.mailboxId ?? undefined,
        contactCollectionEnabled: settingsData.contactCollectionEnabled,
        autoReplyEnabled: settingsData.autoReplyEnabled,
        autoReplyMessage: settingsData.autoReplyMessage ?? '',
        enabled: settingsData.enabled,
      });
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : t('channels.common.loadFailed', { channel: t('format.sourceType.dingtalk') });
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
          dingdingApi.getSettings(),
          dingdingApi.getStatus(),
          mailboxApi.list(),
        ]);
        if (!destroyed) {
          setSettings(settingsData);
          setStatus(statusData);
          setMailboxes(mailboxData);
          setForm({
            appKey: settingsData.appKey ?? '',
            agentId: settingsData.agentId ?? undefined,
            robotCode: settingsData.robotCode ?? '',
            receiveMode: settingsData.receiveMode ?? 'Http',
            mailboxId: settingsData.mailboxId ?? undefined,
            contactCollectionEnabled: settingsData.contactCollectionEnabled,
            autoReplyEnabled: settingsData.autoReplyEnabled,
            autoReplyMessage: settingsData.autoReplyMessage ?? '',
            enabled: settingsData.enabled,
          });
        }
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : t('channels.common.loadFailed', { channel: t('format.sourceType.dingtalk') });
        toast.error(message);
      } finally {
        if (!destroyed) setLoading(false);
      }
    };
    void run();
    return () => { destroyed = true; };
  }, [toast, t]);

  const handleSave = async () => {
    if (!form.appKey?.trim()) {
      toast.error(t('channels.dingding.validation.appKeyRequired'));
      return;
    }

    // HTTP 模式下校验必填字段
    if (form.receiveMode === 'Http' && form.enabled) {
      if (!settings?.hasCallbackToken && !form.callbackToken?.trim()) {
        toast.error(t('channels.dingding.validation.callbackTokenRequired'));
        return;
      }
      if (!settings?.hasEncodingAesKey && !form.encodingAesKey?.trim()) {
        toast.error(t('channels.dingding.validation.encodingAesKeyRequired'));
        return;
      }
    }

    const payload: UpdateDingDingSettingsRequest = { ...form };
    // 敏感字段提交策略：
    // - 如果用户显式点击了「修改」(editing*=true)：无论是否已有旧值，都按当前输入框内容提交（空表示清空）
    // - 如果没有点过「修改」但输入框有值：说明是「新增配置」场景（数据库里还没存过，
    //   hasAppSecret=false 导致修改按钮都不显示），此时也要把用户输入的新值提交上去
    // - 如果没有点过「修改」且输入框为空：说明保留旧值（或本来就没值），提交 undefined 让后端忽略
    if (!editingAppSecret && !payload.appSecret?.trim()) payload.appSecret = undefined;
    if (!editingCallbackToken && !payload.callbackToken?.trim()) payload.callbackToken = undefined;
    if (!editingEncodingAesKey && !payload.encodingAesKey?.trim()) payload.encodingAesKey = undefined;

    try {
      setSaving(true);
      const updated = await dingdingApi.updateSettings(payload);
      setSettings(updated);
      // 清空敏感字段输入并退出编辑态
      setEditingAppSecret(false);
      setEditingCallbackToken(false);
      setEditingEncodingAesKey(false);
      setForm((prev) => ({
        ...prev,
        appSecret: undefined,
        callbackToken: undefined,
        encodingAesKey: undefined,
      }));
      toast.success(t('channels.common.saveSucceeded', { channel: t('format.sourceType.dingtalk') }));
      const newStatus = await dingdingApi.getStatus();
      setStatus(newStatus);
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : t('channels.common.saveFailed', { channel: t('format.sourceType.dingtalk') });
      toast.error(message);
    } finally {
      setSaving(false);
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

  if (loading && !settings) {
    return (
      <div className="max-w-4xl mx-auto p-4 sm:p-6">
        <div className="flex flex-col items-center justify-center min-h-[400px] gap-3">
          <div className="p-3 bg-gradient-to-br from-indigo-500 to-blue-600 rounded-2xl shadow-lg shadow-indigo-500/20">
            <DingTalkLogo className="w-7 h-7 text-white animate-pulse" />
          </div>
          <div className="flex items-center gap-2 text-gray-500 dark:text-slate-400">
            <Loader2 className="w-4 h-4 animate-spin" />
            <span className="text-sm">{t('channels.dingding.loading')}</span>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto p-4 sm:p-6 space-y-6">
      {/* 页面标题 - 钉钉品牌蓝渐变背景 */}
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-[#0089FF] via-blue-500 to-indigo-600 dark:from-blue-700 dark:via-blue-600 dark:to-indigo-800 shadow-lg shadow-blue-500/20">
        {/* 装饰性背景图案 - 闪电纹理 */}
        <div className="absolute inset-0 opacity-10 pointer-events-none">
          <svg className="w-full h-full" viewBox="0 0 400 200" preserveAspectRatio="none">
            <path d="M180 20 L140 90 L170 90 L130 180 L210 80 L180 80 L220 20 Z" fill="white" />
            <circle cx="50" cy="50" r="30" fill="white" />
            <circle cx="350" cy="150" r="50" fill="white" />
          </svg>
        </div>
        <div className="relative flex items-center justify-between p-4 sm:p-6">
          <div className="flex items-center gap-3 min-w-0">
            <div className="p-1.5 sm:p-2 bg-white/20 backdrop-blur-sm rounded-xl flex-shrink-0">
              <DingTalkLogo className="w-10 h-10 sm:w-12 sm:h-12 text-white" />
            </div>
            <div className="min-w-0">
              <h1 className="text-xl sm:text-2xl font-bold text-white truncate">{t('channels.dingding.title')}</h1>
              <p className="text-sm text-blue-50/90 truncate">{t('channels.dingding.subtitle')}</p>
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
      <div className={`rounded-xl border p-4 transition-all duration-300 ${
        configured
          ? 'bg-gradient-to-r from-indigo-50 to-blue-50/50 border-indigo-200 dark:from-indigo-900/20 dark:to-blue-900/10 dark:border-indigo-800/50 shadow-sm shadow-indigo-500/5'
          : status?.configured && !status?.enabled
          ? 'bg-gradient-to-r from-yellow-50 to-amber-50/50 border-yellow-200 dark:from-yellow-900/20 dark:to-amber-900/10 dark:border-yellow-800/50'
          : 'bg-gray-50 border-gray-200 dark:bg-slate-800 dark:border-slate-700'
      }`}>
        <div className="flex items-center gap-3 flex-wrap">
          {status?.configured && status?.enabled ? (
            <>
              <div className="flex-shrink-0 w-10 h-10 rounded-xl bg-indigo-100 dark:bg-indigo-900/40 flex items-center justify-center ring-2 ring-indigo-200 dark:ring-indigo-800/50">
                <CheckCircle2 className="w-5 h-5 text-indigo-600 dark:text-indigo-400" />
              </div>
              <div className="flex-1 min-w-0">
                <p className="font-semibold text-indigo-900 dark:text-indigo-200 flex items-center gap-1.5">
                  {t('channels.dingding.status.connected')}
                  <span className="inline-block w-2 h-2 rounded-full bg-indigo-500 animate-pulse" />
                </p>
                <p className="text-sm text-indigo-700 dark:text-indigo-400/80">{t('channels.common.receivesMessages', { channel: t('format.sourceType.dingtalk') })}</p>
              </div>
              {/* 接收模式标签 */}
              {status?.receiveMode && (
                <div className="flex items-center gap-1.5 px-3 py-1.5 bg-white/80 dark:bg-slate-900/50 rounded-full flex-shrink-0 ring-1 ring-indigo-200 dark:ring-indigo-800/50">
                  {status.receiveMode === 'Stream' ? (
                    <Radio className="w-3.5 h-3.5 text-green-600 dark:text-green-400" />
                  ) : (
                    <Wifi className="w-3.5 h-3.5 text-blue-600 dark:text-blue-400" />
                  )}
                  <span className="text-xs font-medium text-gray-700 dark:text-slate-300">{status.receiveMode === 'Stream' ? t('channels.dingding.status.modeStream') : t('channels.dingding.status.modeHttp')}</span>
                </div>
              )}
              {status?.apiReachable === true && (
                <div className="flex items-center gap-1.5 px-3 py-1.5 bg-indigo-100 dark:bg-indigo-900/40 rounded-full flex-shrink-0 ring-1 ring-indigo-200 dark:ring-indigo-800/50" title={t('channels.dingding.status.apiReachableTitle')}>
                  <CheckCircle2 className="w-3.5 h-3.5 text-indigo-600 dark:text-indigo-400" />
                  <span className="text-xs font-medium text-indigo-700 dark:text-indigo-300">{t('channels.common.apiReachable')}</span>
                </div>
              )}
              {status?.apiReachable === false && (
                <div className="flex items-center gap-1.5 px-3 py-1.5 bg-red-100 dark:bg-red-900/40 rounded-full flex-shrink-0 ring-1 ring-red-200 dark:ring-red-800/50" title={t('channels.dingding.status.apiUnreachableTitle')}>
                  <AlertCircle className="w-3.5 h-3.5 text-red-600 dark:text-red-400" />
                  <span className="text-xs font-medium text-red-700 dark:text-red-300">{t('channels.dingding.status.apiUnreachable')}</span>
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
                <p className="text-sm text-yellow-700 dark:text-yellow-400/80">{t('channels.dingding.status.disabledHint')}</p>
              </div>
            </>
          ) : (
            <>
              <div className="flex-shrink-0 w-10 h-10 rounded-xl bg-gray-100 dark:bg-slate-700 flex items-center justify-center ring-2 ring-gray-200 dark:ring-slate-600">
                <AlertCircle className="w-5 h-5 text-gray-500 dark:text-slate-400" />
              </div>
              <div className="flex-1 min-w-0">
                <p className="font-semibold text-gray-900 dark:text-slate-100">{t('channels.common.notConfigured')}</p>
                <p className="text-sm text-gray-600 dark:text-slate-400">{t('channels.dingding.status.notConfiguredHint')}</p>
              </div>
            </>
          )}
        </div>
      </div>

      {/* 支持的消息类型 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4 sm:p-6 shadow-sm">
        <h2 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2 mb-3">
          <Zap className="w-4 h-4 sm:w-5 sm:h-5 text-blue-500 dark:text-blue-400" />
          {t('channels.common.messageTypes')}
        </h2>
        <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
          {MESSAGE_TYPES.map((type) => {
            const Icon = type.icon;
            return (
              <div
                key={type.labelKey}
                className="flex items-center gap-2.5 p-3 rounded-lg bg-gradient-to-br from-blue-50/50 to-indigo-50/30 dark:from-slate-700/50 dark:to-slate-700/20 border border-blue-100/50 dark:border-slate-600/50 hover:from-blue-50 hover:to-indigo-50/50 dark:hover:from-slate-700 dark:hover:to-slate-700/30 transition-colors"
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
          <Settings className="w-4 h-4 sm:w-5 sm:h-5 text-indigo-500 dark:text-indigo-400" />
          {t('channels.common.appConfig')}
        </h2>

        {/* 启用开关 */}
        <div className="flex items-center justify-between py-2">
          <div>
            <label className="text-sm font-medium text-gray-900 dark:text-slate-100">{t('channels.common.enableChannel', { channel: t('format.sourceType.dingtalk') })}</label>
            <p className="text-xs text-gray-500 dark:text-slate-400">{t('channels.dingding.form.enableHint')}</p>
          </div>
          <button
            type="button"
            onClick={() => setForm((prev) => ({ ...prev, enabled: !prev.enabled }))}
            className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${form.enabled ? 'bg-indigo-600' : 'bg-gray-300 dark:bg-slate-600'}`}
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
              {t('channels.dingding.mode.hint')}
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
                      ? 'border-indigo-500 bg-indigo-50/50 dark:border-indigo-400 dark:bg-indigo-900/20 shadow-sm shadow-indigo-500/10'
                      : 'border-gray-200 dark:border-slate-600 hover:border-gray-300 dark:hover:border-slate-500 bg-white dark:bg-slate-900/40'
                  }`}
                >
                  {/* 选中标记 */}
                  {selected && (
                    <div className="absolute top-3 right-3 w-5 h-5 rounded-full bg-indigo-600 flex items-center justify-center">
                      <CheckCircle2 className="w-3.5 h-3.5 text-white" />
                    </div>
                  )}
                  <div className="flex items-center gap-2.5 mb-2">
                    <div className={`flex-shrink-0 w-9 h-9 rounded-lg flex items-center justify-center ${
                      selected
                        ? 'bg-indigo-100 dark:bg-indigo-900/40'
                        : 'bg-gray-100 dark:bg-slate-700'
                    }`}>
                      <Icon className={`w-4.5 h-4.5 ${selected ? 'text-indigo-600 dark:text-indigo-400' : 'text-gray-500 dark:text-slate-400'}`} />
                    </div>
                    <div className="flex items-center gap-2">
                      <span className={`text-sm font-semibold ${selected ? 'text-indigo-900 dark:text-indigo-200' : 'text-gray-900 dark:text-slate-100'}`}>
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
          {/* 模式对应官方文档链接 */}
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

        {/* ===== Stream 模式提示（仅 Stream 模式显示）===== */}
        {isStreamMode && (
          <div className="flex items-start gap-3 p-3 bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800/50 rounded-lg">
            <ShieldCheck className="w-4 h-4 text-green-600 dark:text-green-400 flex-shrink-0 mt-0.5" />
            <div className="text-xs text-green-700 dark:text-green-300 space-y-1">
              <p className="font-medium">{t('channels.dingding.mode.streamSelected')}</p>
              <p>{t('channels.dingding.mode.streamTip')}</p>
              <p>{t('channels.dingding.mode.streamTip2')}</p>
            </div>
          </div>
        )}

        {/* ===== HTTP 模式专属提示（仅 HTTP 模式显示）===== */}
        {isHttpMode && (
          <div className="flex items-start gap-3 p-3 bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800/50 rounded-lg">
            <ShieldCheck className="w-4 h-4 text-blue-600 dark:text-blue-400 flex-shrink-0 mt-0.5" />
            <div className="text-xs text-blue-700 dark:text-blue-300 space-y-1">
              <p className="font-medium">{t('channels.dingding.mode.httpSelected')}</p>
              <p>{t('channels.common.httpNeedTokenPrefix')}<strong>CallbackToken</strong>{t('channels.dingding.mode.httpTipMiddle')}<strong>EncodingAESKey</strong>{t('channels.dingding.mode.httpTipSuffix')}</p>
              <p>{t('channels.dingding.mode.httpTip2')}</p>
            </div>
          </div>
        )}

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
          {/* AppKey */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
              AppKey <span className="text-red-500">*</span>
            </label>
            <input
              type="text"
              value={form.appKey ?? ''}
              onChange={(e) => setForm((prev) => ({ ...prev, appKey: e.target.value }))}
              placeholder="dingxxxxxxxxxxxxxx"
              className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:focus:ring-indigo-400 focus:border-transparent transition-shadow"
            />
            <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('channels.dingding.form.appKeyHint')}</p>
          </div>

          {/* AppSecret */}
          <div>
            <label className="flex items-center justify-between text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
              <span>AppSecret</span>
              {settings?.hasAppSecret && !editingAppSecret && (
                <span className="text-xs text-indigo-600 dark:text-indigo-400 flex items-center gap-1">
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
                placeholder={settings?.hasAppSecret ? t('channels.common.maskedConfiguredEditable') : t('channels.dingding.form.appSecretPlaceholder')}
                className={`flex-1 px-3 py-2 bg-white dark:bg-slate-900 border rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:focus:ring-indigo-400 focus:border-transparent transition-shadow ${
                  settings?.hasAppSecret && !editingAppSecret
                    ? 'border-indigo-300 dark:border-indigo-700 bg-indigo-50/50 dark:bg-indigo-900/10'
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
          </div>

          {/* AgentId */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
              AgentId
            </label>
            <input
              type="number"
              value={form.agentId ?? ''}
              onChange={(e) => setForm((prev) => ({ ...prev, agentId: e.target.value === '' ? undefined : Number(e.target.value) }))}
              placeholder="123456789"
              className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:focus:ring-indigo-400 focus:border-transparent transition-shadow"
            />
            <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('channels.dingding.form.agentIdHint')}</p>
          </div>

          {/* RobotCode */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
              RobotCode
            </label>
            <input
              type="text"
              value={form.robotCode ?? ''}
              onChange={(e) => setForm((prev) => ({ ...prev, robotCode: e.target.value }))}
              placeholder={t('channels.dingding.form.robotCodePlaceholder')}
              className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:focus:ring-indigo-400 focus:border-transparent transition-shadow"
            />
            <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('channels.dingding.form.robotCodeHint')}</p>
          </div>

          {/* ===== HTTP 模式专属字段：CallbackToken ===== */}
          {isHttpMode && (
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
                CallbackToken <span className="text-red-500">*</span>
              </label>
              <div className="flex gap-2 items-center">
                <input
                  type={showCallbackToken ? 'text' : 'password'}
                  value={form.callbackToken ?? ''}
                  onChange={(e) => setForm((prev) => ({ ...prev, callbackToken: e.target.value }))}
                  disabled={settings?.hasCallbackToken && !editingCallbackToken}
                  placeholder={settings?.hasCallbackToken ? t('channels.common.maskedConfigured') : t('channels.dingding.form.callbackTokenPlaceholder')}
                  className="flex-1 px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:focus:ring-indigo-400 focus:border-transparent disabled:bg-gray-50 dark:disabled:bg-slate-800 dark:disabled:text-slate-500 transition-shadow"
                />
                {settings?.hasCallbackToken && !editingCallbackToken && (
                  <span className="text-xs text-gray-400 dark:text-slate-500 flex items-center gap-1 whitespace-nowrap">
                    <Lock className="w-3 h-3" /> {t('channels.common.configured')}
                  </span>
                )}
                {settings?.hasCallbackToken && (
                  <>
                    <button
                      type="button"
                      onClick={() => setShowCallbackToken((v) => !v)}
                      disabled={!editingCallbackToken}
                      className="px-3 py-2 text-sm text-gray-600 dark:text-slate-300 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors disabled:opacity-40"
                      title={showCallbackToken ? t('channels.common.hide') : t('channels.common.show')}
                    >
                      {showCallbackToken ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                    </button>
                    <button
                      type="button"
                      onClick={() => {
                        setEditingCallbackToken(!editingCallbackToken);
                        setForm((prev) => ({ ...prev, callbackToken: '' }));
                      }}
                      className="px-3 py-2 text-sm text-gray-600 dark:text-slate-300 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors"
                      title={editingCallbackToken ? t('channels.common.cancelEdit') : t('channels.common.editSecret')}
                    >
                      {editingCallbackToken ? <X className="w-4 h-4" /> : <Pencil className="w-4 h-4" />}
                    </button>
                  </>
                )}
              </div>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('channels.dingding.form.callbackTokenHint')}</p>
            </div>
          )}

          {/* ===== HTTP 模式专属字段：EncodingAesKey ===== */}
          {isHttpMode && (
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
                EncodingAESKey <span className="text-red-500">*</span>
              </label>
              <div className="flex gap-2 items-center">
                <input
                  type={showEncodingAesKey ? 'text' : 'password'}
                  value={form.encodingAesKey ?? ''}
                  onChange={(e) => setForm((prev) => ({ ...prev, encodingAesKey: e.target.value }))}
                  disabled={settings?.hasEncodingAesKey && !editingEncodingAesKey}
                  placeholder={settings?.hasEncodingAesKey ? t('channels.common.maskedConfigured') : t('channels.dingding.form.encodingAesKeyPlaceholder')}
                  className="flex-1 px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:focus:ring-indigo-400 focus:border-transparent disabled:bg-gray-50 dark:disabled:bg-slate-800 dark:disabled:text-slate-500 transition-shadow"
                />
                {settings?.hasEncodingAesKey && !editingEncodingAesKey && (
                  <span className="text-xs text-gray-400 dark:text-slate-500 flex items-center gap-1 whitespace-nowrap">
                    <Lock className="w-3 h-3" /> {t('channels.common.configured')}
                  </span>
                )}
                {settings?.hasEncodingAesKey && (
                  <>
                    <button
                      type="button"
                      onClick={() => setShowEncodingAesKey((v) => !v)}
                      disabled={!editingEncodingAesKey}
                      className="px-3 py-2 text-sm text-gray-600 dark:text-slate-300 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors disabled:opacity-40"
                      title={showEncodingAesKey ? t('channels.common.hide') : t('channels.common.show')}
                    >
                      {showEncodingAesKey ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                    </button>
                    <button
                      type="button"
                      onClick={() => {
                        setEditingEncodingAesKey(!editingEncodingAesKey);
                        setForm((prev) => ({ ...prev, encodingAesKey: '' }));
                      }}
                      className="px-3 py-2 text-sm text-gray-600 dark:text-slate-300 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors"
                      title={editingEncodingAesKey ? t('channels.common.cancelEdit') : t('channels.common.editSecret')}
                    >
                      {editingEncodingAesKey ? <X className="w-4 h-4" /> : <Pencil className="w-4 h-4" />}
                    </button>
                  </>
                )}
              </div>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('channels.dingding.form.encodingAesKeyHint')}</p>
            </div>
          )}

          {/* 关联邮箱 */}
          <div className={isHttpMode ? '' : 'md:col-span-2'}>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('channels.common.linkedMailbox')}</label>
            <select
              value={form.mailboxId ?? ''}
              onChange={(e) => setForm((prev) => ({ ...prev, mailboxId: e.target.value === '' ? undefined : Number(e.target.value) }))}
              className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:focus:ring-indigo-400 focus:border-transparent transition-shadow"
            >
              <option value="">{t('channels.common.autoAssignMailbox')}</option>
              {mailboxes.map((m) => (
                <option key={m.id} value={m.id} disabled={!m.isActive}>
                  {m.name} ({m.email}){m.isActive ? '' : t('channels.common.disabledSuffix')}
                </option>
              ))}
            </select>
            <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('channels.common.mailboxHint', { channel: t('format.sourceType.dingtalk') })}</p>
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
              {t('channels.dingding.form.callbackUrlHint')}
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
                <span>{t('channels.dingding.callback.localWarning')}</span>
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
                <p className="font-medium text-gray-600 dark:text-slate-300">{t('channels.dingding.callback.streamNoCallbackTitle')}</p>
                <p className="mt-0.5">{t('channels.common.streamNoCallbackHint')}</p>
              </div>
            </div>
          </div>
        )}

        {/* 客户信息及联系方式收集 */}
        <div className="border-t border-gray-100 dark:border-slate-700 pt-4">
          <div
            className={`relative overflow-hidden rounded-xl border p-4 transition-all duration-300 ${
              form.contactCollectionEnabled
                ? 'bg-gradient-to-r from-indigo-50/80 to-blue-50/50 border-indigo-200 dark:from-indigo-900/20 dark:to-blue-900/10 dark:border-indigo-800/50 shadow-sm shadow-indigo-500/5'
                : 'bg-gray-50/80 border-gray-200 dark:bg-slate-800/50 dark:border-slate-700'
            }`}
          >
            <div className="flex items-start gap-3">
              {/* 图标 */}
              <div
                className={`flex-shrink-0 w-10 h-10 rounded-xl flex items-center justify-center transition-all duration-300 ${
                  form.contactCollectionEnabled
                    ? 'bg-indigo-100 dark:bg-indigo-900/40 ring-2 ring-indigo-200 dark:ring-indigo-800/50'
                    : 'bg-gray-200 dark:bg-slate-700'
                }`}
              >
                <UserCheck
                  className={`w-5 h-5 transition-colors ${
                    form.contactCollectionEnabled
                      ? 'text-indigo-600 dark:text-indigo-400'
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
                        ? 'bg-indigo-100 text-indigo-700 dark:bg-indigo-900/40 dark:text-indigo-300'
                        : 'bg-gray-200 text-gray-600 dark:bg-slate-700 dark:text-slate-400'
                    }`}
                  >
                    <span
                      className={`inline-block w-1.5 h-1.5 rounded-full ${
                        form.contactCollectionEnabled ? 'bg-indigo-500 animate-pulse' : 'bg-gray-400'
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
                className={`relative inline-flex h-6 w-11 flex-shrink-0 items-center rounded-full transition-all duration-300 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 dark:focus:ring-offset-slate-900 ${
                  form.contactCollectionEnabled
                    ? 'bg-indigo-600 shadow-md shadow-indigo-500/30'
                    : 'bg-gray-300 dark:bg-slate-600'
                }`}
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
              className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${form.autoReplyEnabled ? 'bg-indigo-600' : 'bg-gray-300 dark:bg-slate-600'}`}
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
                className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:focus:ring-indigo-400 focus:border-transparent resize-none transition-shadow"
              />
              <div className="flex items-center justify-between mt-1">
                <p className="text-xs text-gray-400 dark:text-slate-500">{t('channels.common.robotAutoReplyHint')}</p>
                <span className={`text-xs ${
                  (form.autoReplyMessage?.length ?? 0) > 500 * 0.9 ? 'text-red-500 dark:text-red-400' : 'text-gray-400 dark:text-slate-500'
                }`} title={t('channels.common.charCountTitle')}>
                  {form.autoReplyMessage?.length ?? 0}/500
                </span>
              </div>
            </div>
          )}
        </div>

        {/* 保存按钮 */}
        <div className="flex justify-end pt-2">
          <button
            onClick={handleSave}
            disabled={saving || isReadOnly}
            className="flex items-center justify-center gap-2 w-full sm:w-auto px-5 py-2 bg-gradient-to-r from-indigo-600 to-blue-600 text-white rounded-lg text-xs sm:text-sm font-medium hover:from-indigo-700 hover:to-blue-700 disabled:from-gray-400 disabled:to-gray-400 shadow-sm transition-all"
          >
            {saving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
            {t('channels.common.saveConfig')}
          </button>
        </div>
      </div>

      {/* 配置指南 */}
      <div className="bg-gradient-to-br from-blue-50/80 to-indigo-50/50 dark:from-blue-900/20 dark:to-indigo-900/10 rounded-xl border border-blue-200 dark:border-blue-800/50 p-4 sm:p-6 space-y-4 shadow-sm">
        <div className="flex items-center gap-3 pb-1">
          <div className="p-1.5 bg-[#0089FF] rounded-lg shadow-sm shadow-blue-500/30">
            <DingTalkLogo className="w-5 h-5 sm:w-6 sm:h-6 text-white" />
          </div>
          <h2 className="text-sm sm:text-base font-semibold text-blue-900 dark:text-blue-200">{t('channels.common.configGuide')}</h2>
          <span className="ml-auto text-xs text-blue-600 dark:text-blue-400 font-medium">
            {t('channels.common.currentMode')}{t(currentModeDef.labelKey)}
          </span>
        </div>
        <ol className="space-y-2.5 text-sm text-blue-800 dark:text-blue-300">
          {(isStreamMode ? [
            <>{t('channels.dingding.guide.createApp')}</>,
            <>{t('channels.dingding.guide.getCredentials')}</>,
            <>{t('channels.dingding.guide.choosePushPrefix')}<strong>{t('channels.dingding.mode.streamLabel')}</strong>{t('channels.dingding.guide.streamModeSuffix')}</>,
            <>{t('channels.common.permissionsPrefix')}<code className="px-1 py-0.5 bg-blue-100 dark:bg-blue-900/40 rounded text-[11px] font-mono">qyapi_robot</code>{t('channels.common.permissionsSeparator')}<code className="px-1 py-0.5 bg-blue-100 dark:bg-blue-900/40 rounded text-[11px] font-mono">qyapi_send_message</code>{t('channels.dingding.guide.permissionsSuffix')}</>,
            <>{t('channels.common.releaseAndAwaitApproval')}</>,
            <>{t('channels.dingding.guide.streamSelect')}</>,
            <>{t('channels.dingding.guide.streamConnect')}</>,
          ] : [
            <>{t('channels.dingding.guide.createApp')}</>,
            <>{t('channels.dingding.guide.getCredentials')}</>,
            <>{t('channels.dingding.guide.choosePushPrefix')}<strong>{t('channels.dingding.guide.httpPushStrong')}</strong>{t('channels.dingding.guide.httpPushSuffix')}</>,
            <>{t('channels.dingding.guide.httpCallback')}</>,
            <>{t('channels.dingding.guide.httpRobotCode')}</>,
            <>{t('channels.common.permissionsPrefix')}<code className="px-1 py-0.5 bg-blue-100 dark:bg-blue-900/40 rounded text-[11px] font-mono">qyapi_robot</code>{t('channels.common.permissionsSeparator')}<code className="px-1 py-0.5 bg-blue-100 dark:bg-blue-900/40 rounded text-[11px] font-mono">qyapi_send_message</code>{t('channels.dingding.guide.permissionsSuffix')}</>,
            <>{t('channels.common.releaseAndAwaitApproval')}</>,
            <>{t('channels.dingding.guide.httpSelect')}</>,
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
          <span><strong>{t('channels.common.noteLabel')}</strong>{t('channels.dingding.guide.note')}</span>
        </div>
      </div>
    </div>
  );
}
