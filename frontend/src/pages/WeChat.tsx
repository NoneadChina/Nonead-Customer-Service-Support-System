import { useEffect, useState, useCallback, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';
import { weChatApi, mailboxApi, conversationApi } from '../api';
import type {
  WeChatSettingsDto,
  UpdateWeChatSettingsRequest,
  WeChatStatusDto,
  MailboxResponse,
  ConversationResponse,
} from '../api/types';
import { useToast } from '../context/ToastContext';
import { useI18n } from '../context/I18nContext';
import { usePermission } from '../hooks/usePermission';
import { toIntlLocale } from '../i18n/locale';
import type { TranslationKey } from '../locales/zh-Hans';
import type { TranslateParams } from '../i18n/types';
import {
  MessageCircle,
  Save,
  Loader2,
  CheckCircle2,
  AlertCircle,
  Copy,
  ExternalLink,
  Info,
  RefreshCw,
  Lock,
  Unlock,
  Send,
  Layout,
  Trash2,
  Eye,
  Smartphone,
  Braces,
  Eraser,
  Settings,
  UserCheck,
  Search,
  Plus,
  History,
  X,
  ArrowDownToLine,
} from 'lucide-react';

type TabKey = 'config' | 'template' | 'menu';

/** 与 `I18nContext` 的 `t` 签名一致；模块级纯函数与映射表需显式传入 `t`（同 `pages/Macros.tsx`） */
type TranslateFn = (key: TranslationKey, params?: TranslateParams) => string;

export default function WeChat() {
  const { t } = useI18n();
  const toast = useToast();
  const { isReadOnly } = usePermission();
  const [searchParams, setSearchParams] = useSearchParams();
  // 支持通过 ?tab=template 深链定位 Tab（用于会话详情页 48 小时窗口过期时快捷跳转）
  const [activeTab, setActiveTab] = useState<TabKey>(() => {
    const tabParam = searchParams.get('tab');
    if (tabParam === 'template' || tabParam === 'menu' || tabParam === 'config') {
      return tabParam;
    }
    return 'config';
  });

  // Tab 切换时同步 URL（便于分享和浏览器前进/后退）
  const handleTabChange = useCallback((tab: TabKey) => {
    setActiveTab(tab);
    setSearchParams(tab === 'config' ? {} : { tab }, { replace: true });
  }, [setSearchParams]);

  const [settings, setSettings] = useState<WeChatSettingsDto | null>(null);
  const [status, setStatus] = useState<WeChatStatusDto | null>(null);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [refreshLock, setRefreshLock] = useState(false);
  const isRefreshing = loading || refreshLock;

  // 表单状态
  const [appId, setAppId] = useState('');
  const [appSecret, setAppSecret] = useState('');
  const [token, setToken] = useState('');
  const [encodingAESKey, setEncodingAESKey] = useState('');
  const [encryptMode, setEncryptMode] = useState('plain');
  const [mailboxId, setMailboxId] = useState<number | ''>('');
  const [contactCollectionEnabled, setContactCollectionEnabled] = useState(true);
  const [autoReplyEnabled, setAutoReplyEnabled] = useState(false);
  const [autoReplyMessage, setAutoReplyMessage] = useState('');
  const [enabled, setEnabled] = useState(false);

  // 标记 AppSecret/AESKey 是否正在修改（空值表示不修改）
  const [editingSecret, setEditingSecret] = useState(false);
  const [editingAESKey, setEditingAESKey] = useState(false);
  const [editingToken, setEditingToken] = useState(false);

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      const [settingsData, statusData, mailboxData] = await Promise.all([
        weChatApi.getSettings(),
        weChatApi.getStatus(),
        mailboxApi.list(),
      ]);
      setSettings(settingsData);
      setStatus(statusData);
      setMailboxes(mailboxData);
      setAppId(settingsData.appId ?? '');
      setToken('');
      setEditingToken(false);
      setEncryptMode(settingsData.encryptMode ?? 'plain');
      setMailboxId(settingsData.mailboxId ?? '');
      setContactCollectionEnabled(settingsData.contactCollectionEnabled);
      setAutoReplyEnabled(settingsData.autoReplyEnabled);
      setAutoReplyMessage(settingsData.autoReplyMessage ?? '');
      setEnabled(settingsData.enabled);
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : t('channels.common.loadFailed', { channel: t('format.sourceType.wechat') });
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
      try {
        if (!destroyed) setLoading(true);
        const [settingsData, statusData, mailboxData] = await Promise.all([
          weChatApi.getSettings(),
          weChatApi.getStatus(),
          mailboxApi.list(),
        ]);
        if (!destroyed) {
          setSettings(settingsData);
          setStatus(statusData);
          setMailboxes(mailboxData);
          setAppId(settingsData.appId ?? '');
          setToken('');
          setEditingToken(false);
          setEncryptMode(settingsData.encryptMode ?? 'plain');
          setMailboxId(settingsData.mailboxId ?? '');
          setContactCollectionEnabled(settingsData.contactCollectionEnabled);
          setAutoReplyEnabled(settingsData.autoReplyEnabled);
          setAutoReplyMessage(settingsData.autoReplyMessage ?? '');
          setEnabled(settingsData.enabled);
        }
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : t('channels.common.loadFailed', { channel: t('format.sourceType.wechat') });
        toast.error(message);
      } finally {
        if (!destroyed) setLoading(false);
      }
    };
    void run();
    return () => { destroyed = true; };
  }, [toast, t]);

  const handleSave = async () => {
    if (!appId.trim()) {
      toast.error(t('channels.wechat.form.appIdRequired'));
      return;
    }
    // Token 校验：已配置且未编辑时跳过，未配置或正在编辑时必填
    if (editingToken && !token.trim()) {
      toast.error(t('channels.wechat.form.tokenRequired'));
      return;
    }
    if (!settings?.hasToken && !token.trim()) {
      toast.error(t('channels.wechat.form.tokenRequired'));
      return;
    }

    const payload: UpdateWeChatSettingsRequest = {
      appId: appId.trim(),
      encryptMode,
      mailboxId: mailboxId === '' ? null : mailboxId,
      contactCollectionEnabled,
      autoReplyEnabled,
      autoReplyMessage: autoReplyEnabled ? autoReplyMessage : null,
      enabled,
    };

    // 密钥字段提交规则：
    // - 已配置且未解锁编辑时 → 跳过（不修改）
    // - 未配置（输入框本就可编辑）或已解锁编辑时 → 提交非空值
    if ((editingSecret || !settings?.hasAppSecret) && appSecret.trim()) {
      payload.appSecret = appSecret.trim();
    }
    if ((editingAESKey || !settings?.hasEncodingAESKey) && encodingAESKey.trim()) {
      payload.encodingAESKey = encodingAESKey.trim();
    }
    if ((editingToken || !settings?.hasToken) && token.trim()) {
      payload.token = token.trim();
    }

    try {
      setSaving(true);
      const updated = await weChatApi.updateSettings(payload);
      setSettings(updated);
      setAppSecret('');
      setEditingSecret(false);
      setEncodingAESKey('');
      setEditingAESKey(false);
      setToken('');
      setEditingToken(false);
      toast.success(t('channels.common.saveSucceeded', { channel: t('format.sourceType.wechat') }));

      // 刷新状态
      const newStatus = await weChatApi.getStatus();
      setStatus(newStatus);
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : t('channels.common.saveFailed', { channel: t('format.sourceType.wechat') });
      toast.error(message);
    } finally {
      setSaving(false);
    }
  };

  const handleCopyCallbackUrl = async () => {
    if (!settings?.callbackUrl) return;
    try {
      await navigator.clipboard.writeText(settings.callbackUrl);
      toast.success(t('channels.common.copyCallbackUrl'));
    } catch {
      toast.error(t('channels.common.copyFailed'));
    }
  };

  if (loading) {
    return (
      <div className="max-w-4xl mx-auto p-4 sm:p-6">
        <div className="flex flex-col items-center justify-center min-h-[400px] gap-3">
          <div className="p-3 bg-gradient-to-br from-green-500 to-emerald-600 rounded-2xl shadow-lg shadow-green-500/20">
            <MessageCircle className="w-7 h-7 text-white animate-pulse" />
          </div>
          <div className="flex items-center gap-2 text-gray-500 dark:text-slate-400">
            <Loader2 className="w-4 h-4 animate-spin" />
            <span className="text-sm">{t('channels.wechat.page.loading')}</span>
          </div>
        </div>
      </div>
    );
  }

  const configured = status?.configured && status?.enabled;

  return (
    <div className="max-w-4xl mx-auto p-4 sm:p-6 space-y-4 sm:space-y-6">
      {/* 页面标题 - 带渐变装饰背景 */}
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-green-500 via-emerald-500 to-teal-600 dark:from-green-700 dark:via-emerald-700 dark:to-teal-800 shadow-lg shadow-green-500/20">
        {/* 装饰性背景图案 */}
        <div className="absolute inset-0 opacity-10 pointer-events-none">
          <svg className="w-full h-full" viewBox="0 0 400 200" preserveAspectRatio="none">
            <circle cx="50" cy="50" r="40" fill="white" />
            <circle cx="350" cy="150" r="60" fill="white" />
            <circle cx="200" cy="100" r="30" fill="white" />
          </svg>
        </div>
        <div className="relative flex items-center justify-between p-4 sm:p-6">
          <div className="flex items-center gap-3 min-w-0">
            <div className="p-2.5 bg-white/20 backdrop-blur-sm rounded-xl flex-shrink-0">
              <MessageCircle className="w-7 h-7 text-white" />
            </div>
            <div className="min-w-0">
              <h1 className="text-xl sm:text-2xl font-bold text-white truncate">{t('nav.wechat')}</h1>
              <p className="text-sm text-green-50/90 truncate">{t('channels.wechat.page.subtitle')}</p>
            </div>
          </div>
          <button
            onClick={handleRefresh}
            disabled={isRefreshing}
            className="flex items-center gap-1.5 px-3 py-1.5 text-sm text-white bg-white/20 hover:bg-white/30 backdrop-blur-sm rounded-lg flex-shrink-0 disabled:opacity-60 disabled:cursor-not-allowed active:scale-[0.98] transition-all duration-200"
          >
            <RefreshCw className={`w-4 h-4 ${isRefreshing ? 'animate-spin' : ''}`} />
            <span className="hidden sm:inline">{t('channels.common.refresh')}</span>
          </button>
        </div>
      </div>

      {/* 连接状态卡片 */}
      <div className={`rounded-xl border p-4 transition-all duration-300 ${
        configured
          ? 'bg-gradient-to-r from-green-50 to-emerald-50/50 border-green-200 dark:from-green-900/20 dark:to-emerald-900/10 dark:border-green-800/50 shadow-sm shadow-green-500/5'
          : status?.configured && !status?.enabled
          ? 'bg-gradient-to-r from-yellow-50 to-amber-50/50 border-yellow-200 dark:from-yellow-900/20 dark:to-amber-900/10 dark:border-yellow-800/50'
          : 'bg-gray-50 border-gray-200 dark:bg-slate-800 dark:border-slate-700'
      }`}>
        <div className="flex items-center gap-3 flex-wrap">
          {status?.configured && status?.enabled ? (
            <>
              <div className="flex-shrink-0 w-10 h-10 rounded-xl bg-green-100 dark:bg-green-900/40 flex items-center justify-center ring-2 ring-green-200 dark:ring-green-800/50">
                <CheckCircle2 className="w-5 h-5 text-green-600 dark:text-green-400" />
              </div>
              <div className="flex-1 min-w-0">
                <p className="font-semibold text-green-900 dark:text-green-200 flex items-center gap-1.5">
                  {t('channels.wechat.status.connected')}
                  <span className="inline-block w-2 h-2 rounded-full bg-green-500 animate-pulse" />
                </p>
                <p className="text-sm text-green-700 dark:text-green-400/80">{t('channels.common.receivesMessages', { channel: t('format.sourceType.wechat') })}</p>
              </div>
              {status.tokenValid === false && (
                <div className="flex items-center gap-1.5 px-3 py-1.5 bg-red-100 dark:bg-red-900/40 rounded-full flex-shrink-0 ring-1 ring-red-200 dark:ring-red-800/50" title={t('channels.wechat.status.tokenInvalidHint')}>
                  <AlertCircle className="w-3.5 h-3.5 text-red-600 dark:text-red-400" />
                  <span className="text-xs font-medium text-red-700 dark:text-red-300">{t('channels.common.tokenInvalid')}</span>
                </div>
              )}
              {status.tokenValid === true && (
                <div className="flex items-center gap-1.5 px-3 py-1.5 bg-green-100 dark:bg-green-900/40 rounded-full flex-shrink-0 ring-1 ring-green-200 dark:ring-green-800/50" title={t('channels.wechat.status.tokenValidHint')}>
                  <CheckCircle2 className="w-3.5 h-3.5 text-green-600 dark:text-green-400" />
                  <span className="text-xs font-medium text-green-700 dark:text-green-300">{t('channels.common.tokenValid')}</span>
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
                <p className="text-sm text-yellow-700 dark:text-yellow-400/80">{t('channels.wechat.status.enableHint')}</p>
              </div>
            </>
          ) : (
            <>
              <div className="flex-shrink-0 w-10 h-10 rounded-xl bg-gray-100 dark:bg-slate-700 flex items-center justify-center ring-2 ring-gray-200 dark:ring-slate-600">
                <AlertCircle className="w-5 h-5 text-gray-500 dark:text-slate-400" />
              </div>
              <div className="flex-1 min-w-0">
                <p className="font-semibold text-gray-900 dark:text-slate-100">{t('channels.common.notConfigured')}</p>
                <p className="text-sm text-gray-600 dark:text-slate-400">{t('channels.wechat.status.configureHint')}</p>
              </div>
            </>
          )}
        </div>
      </div>

      {/* 标签栏 */}
      <div className="border-b border-gray-200 dark:border-slate-700 -mx-4 px-4 sm:mx-0 sm:px-0 overflow-x-auto no-scrollbar">
        <nav className="flex gap-1 sm:gap-6 min-w-max">
          <button
            onClick={() => handleTabChange('config')}
            className={`pb-3 px-2 sm:px-0 text-sm font-medium border-b-2 transition-all whitespace-nowrap flex-shrink-0 flex items-center gap-1.5 ${
              activeTab === 'config'
                ? 'border-green-600 text-green-700 dark:text-green-400 dark:border-green-500'
                : 'border-transparent text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-200'
            }`}
          >
            <Settings className="w-4 h-4" />
            {t('channels.common.basicConfig')}
          </button>
          <button
            onClick={() => handleTabChange('template')}
            disabled={!configured}
            className={`pb-3 px-2 sm:px-0 text-sm font-medium border-b-2 transition-all whitespace-nowrap disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-1.5 ${
              activeTab === 'template'
                ? 'border-green-600 text-green-700 dark:text-green-400 dark:border-green-500'
                : 'border-transparent text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-200'
            }`}
          >
            <Send className="w-4 h-4" />
            {t('channels.wechat.tabs.template')}
          </button>
          <button
            onClick={() => handleTabChange('menu')}
            disabled={!configured}
            className={`pb-3 px-2 sm:px-0 text-sm font-medium border-b-2 transition-all whitespace-nowrap disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-1.5 ${
              activeTab === 'menu'
                ? 'border-green-600 text-green-700 dark:text-green-400 dark:border-green-500'
                : 'border-transparent text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-200'
            }`}
          >
            <Layout className="w-4 h-4" />
            {t('channels.wechat.tabs.menu')}
          </button>
        </nav>
      </div>

      {activeTab === 'config' && (
        <ConfigTab
          settings={settings}
          status={status}
          mailboxes={mailboxes}
          appId={appId}
          setAppId={setAppId}
          appSecret={appSecret}
          setAppSecret={setAppSecret}
          token={token}
          setToken={setToken}
          encodingAESKey={encodingAESKey}
          setEncodingAESKey={setEncodingAESKey}
          encryptMode={encryptMode}
          setEncryptMode={setEncryptMode}
          mailboxId={mailboxId}
          setMailboxId={setMailboxId}
          contactCollectionEnabled={contactCollectionEnabled}
          setContactCollectionEnabled={setContactCollectionEnabled}
          autoReplyEnabled={autoReplyEnabled}
          setAutoReplyEnabled={setAutoReplyEnabled}
          autoReplyMessage={autoReplyMessage}
          setAutoReplyMessage={setAutoReplyMessage}
          enabled={enabled}
          setEnabled={setEnabled}
          editingSecret={editingSecret}
          setEditingSecret={setEditingSecret}
          editingAESKey={editingAESKey}
          setEditingAESKey={setEditingAESKey}
          editingToken={editingToken}
          setEditingToken={setEditingToken}
          saving={saving}
          onSave={handleSave}
          onCopyCallbackUrl={handleCopyCallbackUrl}
        />
      )}

      {activeTab === 'template' && configured && <TemplateMessageTab isReadOnly={isReadOnly} />}

      {activeTab === 'menu' && configured && <MenuManagementTab isReadOnly={isReadOnly} />}
    </div>
  );
}

// ====== 基础配置 Tab ======
interface ConfigTabProps {
  settings: WeChatSettingsDto | null;
  status: WeChatStatusDto | null;
  mailboxes: MailboxResponse[];
  appId: string;
  setAppId: (v: string) => void;
  appSecret: string;
  setAppSecret: (v: string) => void;
  token: string;
  setToken: (v: string) => void;
  encodingAESKey: string;
  setEncodingAESKey: (v: string) => void;
  encryptMode: string;
  setEncryptMode: (v: string) => void;
  mailboxId: number | '';
  setMailboxId: (v: number | '') => void;
  contactCollectionEnabled: boolean;
  setContactCollectionEnabled: (v: boolean) => void;
  autoReplyEnabled: boolean;
  setAutoReplyEnabled: (v: boolean) => void;
  autoReplyMessage: string;
  setAutoReplyMessage: (v: string) => void;
  enabled: boolean;
  setEnabled: (v: boolean) => void;
  editingSecret: boolean;
  setEditingSecret: (v: boolean) => void;
  editingAESKey: boolean;
  setEditingAESKey: (v: boolean) => void;
  editingToken: boolean;
  setEditingToken: (v: boolean) => void;
  saving: boolean;
  onSave: () => void;
  onCopyCallbackUrl: () => void;
}

function ConfigTab(props: ConfigTabProps) {
  const {
    settings,
    mailboxes,
    appId,
    setAppId,
    appSecret,
    setAppSecret,
    token,
    setToken,
    encodingAESKey,
    setEncodingAESKey,
    encryptMode,
    setEncryptMode,
    mailboxId,
    setMailboxId,
    contactCollectionEnabled,
    setContactCollectionEnabled,
    autoReplyEnabled,
    setAutoReplyEnabled,
    autoReplyMessage,
    setAutoReplyMessage,
    enabled,
    setEnabled,
    editingSecret,
    setEditingSecret,
    editingAESKey,
    setEditingAESKey,
    editingToken,
    setEditingToken,
    saving,
    onSave,
    onCopyCallbackUrl,
  } = props;

  const { t } = useI18n();

  return (
    <>
      {/* 回调 URL 配置 */}
      {settings?.callbackUrl && (
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4 sm:p-6 space-y-3 shadow-sm">
          <h2 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2">
            <ExternalLink className="w-4 h-4 text-gray-400 dark:text-slate-500" />
            {t('channels.common.callbackAddress')}
          </h2>
          <p className="text-sm text-gray-500 dark:text-slate-400">
            {t('channels.wechat.form.callbackInstruction')}
          </p>
          <div className="flex items-center gap-2">
            <code className="flex-1 px-3 py-2 bg-gray-50 dark:bg-slate-900/60 border border-gray-200 dark:border-slate-700 rounded-lg text-sm text-gray-700 dark:text-slate-300 break-all font-mono">
              {settings.callbackUrl}
            </code>
            <button
              onClick={onCopyCallbackUrl}
              className="flex-shrink-0 p-2 text-gray-500 dark:text-slate-400 hover:bg-gray-100 dark:hover:bg-slate-700 rounded-lg transition-colors"
              title={t('channels.common.copy')}
            >
              <Copy className="w-4 h-4" />
            </button>
          </div>
        </div>
      )}

      {/* 配置表单 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4 sm:p-6 space-y-4 sm:space-y-6 shadow-sm">
        <h2 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100">{t('channels.wechat.form.serviceAccount')}</h2>

        {/* 启用开关 */}
        <div className="flex items-center justify-between py-2">
          <div>
            <label className="text-sm font-medium text-gray-900 dark:text-slate-100">{t('channels.common.enableChannel', { channel: t('format.sourceType.wechat') })}</label>
            <p className="text-xs text-gray-500 dark:text-slate-400">{t('channels.wechat.form.enableHint')}</p>
          </div>
          <button
            type="button"
            onClick={() => setEnabled(!enabled)}
            className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${enabled ? 'bg-green-600' : 'bg-gray-300 dark:bg-slate-600'}`}
          >
            <span className={`inline-block h-4 w-4 transform rounded-full bg-white shadow-sm transition-transform ${enabled ? 'translate-x-6' : 'translate-x-1'}`} />
          </button>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
          {/* AppId */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
              AppID <span className="text-red-500">*</span>
            </label>
            <input
              type="text"
              value={appId}
              onChange={(e) => setAppId(e.target.value)}
              placeholder="wx1234567890abcdef"
              className={`w-full px-3 py-2 bg-white dark:bg-slate-900 border rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-green-500 dark:focus:ring-green-400 focus:border-transparent transition-shadow ${
                appId && !/^wx[a-z0-9]{16}$/i.test(appId)
                  ? 'border-amber-300 dark:border-amber-700'
                  : 'border-gray-300 dark:border-slate-600'
              }`}
            />
            {appId && !/^wx[a-z0-9]{16}$/i.test(appId) && (
              <p className="text-xs text-amber-600 dark:text-amber-400 mt-1 flex items-center gap-1">
                <AlertCircle className="w-3 h-3" />
                {t('channels.wechat.form.appIdHint')}
              </p>
            )}
          </div>

          {/* AppSecret */}
          <div>
            <label className="flex items-center justify-between text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
              <span>AppSecret</span>
              {settings?.hasAppSecret && !editingSecret && (
                <span className="text-xs text-green-600 dark:text-green-400 flex items-center gap-1">
                  <Lock className="w-3 h-3" /> {t('channels.common.configured')}
                </span>
              )}
            </label>
            <div className="flex gap-2">
              <input
                type="password"
                value={settings?.hasAppSecret && !editingSecret ? '••••••••••••' : appSecret}
                onChange={(e) => setAppSecret(e.target.value)}
                readOnly={settings?.hasAppSecret && !editingSecret}
                placeholder={settings?.hasAppSecret ? t('channels.common.maskedConfiguredEditable') : t('channels.wechat.form.appSecretPlaceholder')}
                className={`flex-1 px-3 py-2 bg-white dark:bg-slate-900 border rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-green-500 dark:focus:ring-green-400 focus:border-transparent transition-shadow ${
                  settings?.hasAppSecret && !editingSecret
                    ? 'border-green-300 dark:border-green-700 bg-green-50/50 dark:bg-green-900/10'
                    : 'border-gray-300 dark:border-slate-600'
                }`}
              />
              {settings?.hasAppSecret && (
                <button
                  type="button"
                  onClick={() => {
                    setEditingSecret(!editingSecret);
                    setAppSecret('');
                  }}
                  className="px-3 py-2 text-sm text-gray-600 dark:text-slate-300 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors"
                >
                  {editingSecret ? <Unlock className="w-4 h-4" /> : <Lock className="w-4 h-4" />}
                </button>
              )}
            </div>
          </div>

          {/* Token */}
          <div>
            <label className="flex items-center justify-between text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
              <span>Token <span className="text-red-500">*</span></span>
              {settings?.hasToken && !editingToken && (
                <span className="text-xs text-green-600 dark:text-green-400 flex items-center gap-1">
                  <Lock className="w-3 h-3" /> {t('channels.common.configured')}
                </span>
              )}
            </label>
            <div className="flex gap-2">
              <input
                type="password"
                value={settings?.hasToken && !editingToken ? '••••••••••••' : token}
                onChange={(e) => setToken(e.target.value)}
                readOnly={settings?.hasToken && !editingToken}
                placeholder={settings?.hasToken ? t('channels.common.maskedConfiguredEditable') : t('channels.wechat.form.tokenPlaceholder')}
                className={`flex-1 px-3 py-2 bg-white dark:bg-slate-900 border rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-green-500 dark:focus:ring-green-400 focus:border-transparent transition-shadow ${
                  settings?.hasToken && !editingToken
                    ? 'border-green-300 dark:border-green-700 bg-green-50/50 dark:bg-green-900/10'
                    : token && !/^[A-Za-z0-9]{3,32}$/.test(token)
                      ? 'border-amber-300 dark:border-amber-700'
                      : 'border-gray-300 dark:border-slate-600'
                }`}
              />
              {settings?.hasToken && (
                <button
                  type="button"
                  onClick={() => {
                    setEditingToken(!editingToken);
                    setToken('');
                  }}
                  className="px-3 py-2 text-sm text-gray-600 dark:text-slate-300 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors"
                >
                  {editingToken ? <Unlock className="w-4 h-4" /> : <Lock className="w-4 h-4" />}
                </button>
              )}
            </div>
            {token && !/^[A-Za-z0-9]{3,32}$/.test(token) && (
              <p className="text-xs text-amber-600 dark:text-amber-400 mt-1 flex items-center gap-1">
                <AlertCircle className="w-3 h-3" />
                {t('channels.wechat.form.tokenHint', { count: token.length })}
              </p>
            )}
          </div>

          {/* EncodingAESKey */}
          <div>
            <label className="flex items-center justify-between text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
              <span>EncodingAESKey</span>
              {settings?.hasEncodingAESKey && !editingAESKey && (
                <span className="text-xs text-green-600 dark:text-green-400 flex items-center gap-1">
                  <Lock className="w-3 h-3" /> {t('channels.common.configured')}
                </span>
              )}
            </label>
            <div className="flex gap-2">
              <input
                type="password"
                value={settings?.hasEncodingAESKey && !editingAESKey ? '••••••••••••' : encodingAESKey}
                onChange={(e) => setEncodingAESKey(e.target.value)}
                readOnly={settings?.hasEncodingAESKey && !editingAESKey}
                placeholder={settings?.hasEncodingAESKey ? t('channels.common.maskedConfiguredEditable') : t('channels.wechat.form.aesKeyPlaceholder')}
                className={`flex-1 px-3 py-2 bg-white dark:bg-slate-900 border rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-green-500 dark:focus:ring-green-400 focus:border-transparent transition-shadow ${
                  settings?.hasEncodingAESKey && !editingAESKey
                    ? 'border-green-300 dark:border-green-700 bg-green-50/50 dark:bg-green-900/10'
                    : editingAESKey && encodingAESKey && encodingAESKey.length !== 43
                      ? 'border-amber-300 dark:border-amber-700'
                      : 'border-gray-300 dark:border-slate-600'
                }`}
              />
              {settings?.hasEncodingAESKey && (
                <button
                  type="button"
                  onClick={() => {
                    setEditingAESKey(!editingAESKey);
                    setEncodingAESKey('');
                  }}
                  className="px-3 py-2 text-sm text-gray-600 dark:text-slate-300 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors"
                >
                  {editingAESKey ? <Unlock className="w-4 h-4" /> : <Lock className="w-4 h-4" />}
                </button>
              )}
            </div>
            {editingAESKey && encodingAESKey && encodingAESKey.length !== 43 && (
              <p className="text-xs text-amber-600 dark:text-amber-400 mt-1 flex items-center gap-1">
                <AlertCircle className="w-3 h-3" />
                {t('channels.wechat.form.aesKeyHint', { count: encodingAESKey.length })}
              </p>
            )}
          </div>

          {/* 消息加解密方式 */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('channels.wechat.form.encryptMode')}</label>
            <select
              value={encryptMode}
              onChange={(e) => setEncryptMode(e.target.value)}
              className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-green-500 dark:focus:ring-green-400 focus:border-transparent transition-shadow"
            >
              <option value="plain">{t('channels.wechat.form.encryptModePlain')}</option>
              <option value="compatible">{t('channels.wechat.form.encryptModeCompatible')}</option>
              <option value="safe">{t('channels.wechat.form.encryptModeSafe')}</option>
            </select>
          </div>

          {/* 关联邮箱 */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('channels.common.linkedMailbox')}</label>
            <select
              value={mailboxId}
              onChange={(e) => setMailboxId(e.target.value === '' ? '' : Number(e.target.value))}
              className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-green-500 dark:focus:ring-green-400 focus:border-transparent transition-shadow"
            >
              <option value="">{t('channels.common.autoAssignMailbox')}</option>
              {mailboxes.map((m) => (
                <option key={m.id} value={m.id} disabled={!m.isActive}>
                  {m.name} ({m.email}){m.isActive ? '' : t('channels.common.disabledSuffix')}
                </option>
              ))}
            </select>
            <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('channels.common.mailboxHint', { channel: t('format.sourceType.wechat') })}</p>
          </div>
        </div>

        {/* 客户信息及联系方式收集 */}
        <div className="border-t border-gray-100 dark:border-slate-700 pt-4">
          <div
            className={`relative overflow-hidden rounded-xl border p-4 transition-all duration-300 ${
              contactCollectionEnabled
                ? 'bg-gradient-to-r from-green-50/80 to-emerald-50/50 border-green-200 dark:from-green-900/20 dark:to-emerald-900/10 dark:border-green-800/50 shadow-sm shadow-green-500/5'
                : 'bg-gray-50/80 border-gray-200 dark:bg-slate-800/50 dark:border-slate-700'
            }`}
          >
            <div className="flex items-start gap-3">
              {/* 图标 */}
              <div
                className={`flex-shrink-0 w-10 h-10 rounded-xl flex items-center justify-center transition-all duration-300 ${
                  contactCollectionEnabled
                    ? 'bg-green-100 dark:bg-green-900/40 ring-2 ring-green-200 dark:ring-green-800/50'
                    : 'bg-gray-200 dark:bg-slate-700'
                }`}
              >
                <UserCheck
                  className={`w-5 h-5 transition-colors ${
                    contactCollectionEnabled
                      ? 'text-green-600 dark:text-green-400'
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
                      contactCollectionEnabled
                        ? 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300'
                        : 'bg-gray-200 text-gray-600 dark:bg-slate-700 dark:text-slate-400'
                    }`}
                  >
                    <span
                      className={`inline-block w-1.5 h-1.5 rounded-full ${
                        contactCollectionEnabled ? 'bg-green-500 animate-pulse' : 'bg-gray-400'
                      }`}
                    />
                    {contactCollectionEnabled ? t('channels.common.on') : t('channels.common.off')}
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
                aria-checked={contactCollectionEnabled}
                onClick={() => setContactCollectionEnabled(!contactCollectionEnabled)}
                className={`relative inline-flex h-6 w-11 flex-shrink-0 items-center rounded-full transition-all duration-300 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500 dark:focus:ring-offset-slate-900 ${
                  contactCollectionEnabled
                    ? 'bg-green-600 shadow-md shadow-green-500/30'
                    : 'bg-gray-300 dark:bg-slate-600'
                }`}
              >
                <span
                  className={`inline-block h-5 w-5 transform rounded-full bg-white shadow-sm transition-transform duration-300 ${
                    contactCollectionEnabled ? 'translate-x-5' : 'translate-x-0.5'
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
              onClick={() => setAutoReplyEnabled(!autoReplyEnabled)}
              className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${autoReplyEnabled ? 'bg-green-600' : 'bg-gray-300 dark:bg-slate-600'}`}
            >
              <span className={`inline-block h-4 w-4 transform rounded-full bg-white shadow-sm transition-transform ${autoReplyEnabled ? 'translate-x-6' : 'translate-x-1'}`} />
            </button>
          </div>
          {autoReplyEnabled && (
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('channels.common.autoReplyContent')}</label>
              <textarea
                value={autoReplyMessage}
                onChange={(e) => setAutoReplyMessage(e.target.value)}
                rows={3}
                maxLength={500}
                placeholder={t('channels.wechat.form.autoReplyPlaceholder')}
                className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-green-500 dark:focus:ring-green-400 focus:border-transparent resize-none transition-shadow"
              />
              <div className="flex items-center justify-between mt-1">
                <p className="text-xs text-gray-400 dark:text-slate-500">{t('channels.wechat.form.autoReplyHint')}</p>
                <div className="flex items-center gap-2">
                  <span className={`text-xs ${
                    autoReplyMessage.length > 500 * 0.9 ? 'text-red-500 dark:text-red-400' : 'text-gray-400 dark:text-slate-500'
                  }`} title={t('channels.common.charCountTitle')}>
                    {autoReplyMessage.length}/500
                  </span>
                  <span className={`text-xs ${
                    new Blob([autoReplyMessage]).size > 600 * 0.9
                      ? 'text-red-500 dark:text-red-400'
                      : new Blob([autoReplyMessage]).size > 600 * 0.7
                      ? 'text-amber-500 dark:text-amber-400'
                      : 'text-gray-400 dark:text-slate-500'
                  }`} title={t('channels.wechat.form.byteCountTitle')}>
                    {new Blob([autoReplyMessage]).size}B/600B
                  </span>
                </div>
              </div>
            </div>
          )}
        </div>

        {/* 保存按钮 */}
        <div className="flex flex-col sm:flex-row sm:justify-end pt-2">
          <button
            onClick={onSave}
            disabled={saving}
            className="flex items-center justify-center gap-2 w-full sm:w-auto px-5 py-2 bg-gradient-to-r from-green-600 to-emerald-600 text-white rounded-lg text-xs sm:text-sm font-medium hover:from-green-700 hover:to-emerald-700 disabled:from-gray-400 disabled:to-gray-400 shadow-sm transition-all"
          >
            {saving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
            {t('channels.common.saveConfig')}
          </button>
        </div>
      </div>

      {/* 配置指南 */}
      <div className="bg-gradient-to-br from-blue-50/80 to-indigo-50/50 dark:from-blue-900/20 dark:to-indigo-900/10 rounded-xl border border-blue-200 dark:border-blue-800/50 p-4 sm:p-6 space-y-4 shadow-sm">
        <h2 className="text-sm sm:text-base font-semibold text-blue-900 dark:text-blue-200 flex items-center gap-2">
          <div className="p-1 bg-blue-100 dark:bg-blue-900/40 rounded-lg">
            <Info className="w-4 h-4 text-blue-600 dark:text-blue-400" />
          </div>
          {t('channels.common.configGuide')}
        </h2>
        <ol className="space-y-2.5 text-sm text-blue-800 dark:text-blue-300">
          {[
            <>{t('channels.wechat.guide.loginPrefix')}<a href="https://mp.weixin.qq.com" target="_blank" rel="noopener noreferrer" className="underline font-medium hover:text-blue-600 dark:hover:text-blue-200">{t('channels.wechat.guide.platformName')}</a>{t('channels.wechat.guide.loginSuffix')}</>,
            <>{t('channels.wechat.guide.step2')}</>,
            <>{t('channels.wechat.guide.step3')}</>,
            <>{t('channels.wechat.guide.step4')}</>,
            <>{t('channels.wechat.guide.step5')}</>,
            <>{t('channels.wechat.guide.step6')}</>,
            <>{t('channels.wechat.guide.step7')}</>,
            <>{t('channels.wechat.guide.step8')}</>,
          ].map((step, idx) => (
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
          <span><strong>{t('channels.common.noteLabel')}</strong>{t('channels.wechat.guide.noteBody')}</span>
        </div>
      </div>
    </>
  );
}

// ====== 会话搜索选择器 ======
/** 微信渠道会话搜索下拉框，替代手动输入会话 ID */
function ConversationSearchInput({
  value,
  onSelect,
  disabled,
}: {
  value: number | '';
  onSelect: (id: number | '', subject: string, customerName: string) => void;
  disabled?: boolean;
}) {
  const [keyword, setKeyword] = useState('');
  const [results, setResults] = useState<ConversationResponse[]>([]);
  const [loading, setLoading] = useState(false);
  const [showDropdown, setShowDropdown] = useState(false);
  const [selectedLabel, setSelectedLabel] = useState('');
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  const { t } = useI18n();

  // 已选中时显示标签
  useEffect(() => {
    if (value && !selectedLabel) {
      setSelectedLabel(`#${value}`);
    }
    if (!value) {
      setSelectedLabel('');
      setKeyword('');
    }
  }, [value, selectedLabel]);

  // 点击外部关闭下拉
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setShowDropdown(false);
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);

  // 防抖搜索
  const handleSearch = (kw: string) => {
    setKeyword(kw);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(async () => {
      try {
        setLoading(true);
        const resp = await conversationApi.list({
          sourceType: 7, // 微信渠道
          search: kw.trim() || undefined,
          page: 1,
          pageSize: 20,
        });
        setResults(resp.data ?? []);
      } catch {
        setResults([]);
      } finally {
        setLoading(false);
      }
    }, 350);
  };

  const handleSelect = (conv: ConversationResponse) => {
    const name = conv.customer?.fullName || conv.customerEmail || t('channels.wechat.window.unknownCustomer');
    setSelectedLabel(`#${conv.id} · ${conv.subject || t('channels.wechat.window.noSubject')} · ${name}`);
    setKeyword('');
    setShowDropdown(false);
    onSelect(conv.id, conv.subject ?? '', name);
  };

  // 已选中状态：显示标签 + 清除按钮
  if (value && selectedLabel) {
    return (
      <div ref={containerRef} className="relative">
        <div className="flex items-center justify-between gap-2 px-3 py-2 bg-green-50 dark:bg-green-900/20 border border-green-300 dark:border-green-700 rounded-lg">
          <span className="text-sm text-green-700 dark:text-green-300 truncate">{selectedLabel}</span>
          {!disabled && (
            <button
              type="button"
              onClick={() => {
                setSelectedLabel('');
                onSelect('', '', '');
              }}
              className="flex-shrink-0 text-gray-400 hover:text-red-500 transition-colors"
              title={t('channels.wechat.window.clearSelection')}
            >
              <X className="w-4 h-4" />
            </button>
          )}
        </div>
      </div>
    );
  }

  // 未选中状态：搜索输入框 + 下拉结果
  return (
    <div ref={containerRef} className="relative">
      <div className="relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
        <input
          type="text"
          value={keyword}
          onChange={(e) => handleSearch(e.target.value)}
          onFocus={() => {
            setShowDropdown(true);
            if (results.length === 0 && !loading) handleSearch('');
          }}
          placeholder={t('channels.wechat.window.searchPlaceholder')}
          disabled={disabled}
          className="w-full pl-9 pr-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-green-500 dark:focus:ring-green-400 focus:border-transparent disabled:bg-gray-50 dark:disabled:bg-slate-800 transition-shadow"
        />
        {loading && <Loader2 className="absolute right-3 top-1/2 -translate-y-1/2 w-4 h-4 animate-spin text-gray-400" />}
      </div>
      {showDropdown && (
        <div className="absolute z-10 mt-1 w-full max-h-64 overflow-auto bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-lg shadow-lg">
          {results.length === 0 && !loading ? (
            <div className="px-3 py-4 text-center text-sm text-gray-400 dark:text-slate-500">
              {keyword ? t('channels.wechat.window.noResults') : t('channels.wechat.window.searchHint')}
            </div>
          ) : (
            results.map((conv) => (
              <button
                key={conv.id}
                type="button"
                onClick={() => handleSelect(conv)}
                className="w-full text-left px-3 py-2 hover:bg-green-50 dark:hover:bg-green-900/30 border-b border-gray-100 dark:border-slate-700 last:border-0 transition-colors"
              >
                <div className="flex items-center gap-2">
                  <span className="text-xs font-mono text-green-600 dark:text-green-400 flex-shrink-0">#{conv.id}</span>
                  <span className="text-sm text-gray-900 dark:text-slate-100 truncate flex-1">
                    {conv.subject || t('channels.wechat.window.noSubject')}
                  </span>
                </div>
                <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
                  {conv.customer?.fullName || conv.customerEmail || t('channels.wechat.window.unknownCustomer')}
                </div>
              </button>
            ))
          )}
        </div>
      )}
    </div>
  );
}

// ====== 发送历史记录 ======
type NotifyMode = 'template' | 'subscribe';

interface SendHistoryEntry {
  id: number;
  timestamp: string;
  mode: NotifyMode;
  conversationId: number;
  templateId: string;
  success: boolean;
  errorMessage?: string;
}

// ====== 模板消息 Tab ======
interface TemplateMessageTabProps {
  isReadOnly: boolean;
}

/** 模板数据默认值（可编辑文本框）；中文串走词典，JSON 的括号与转义逐字保留 */
const defaultTemplateData = (t: TranslateFn) => `{
  "first": { "value": "${t('channels.wechat.template.sampleAcceptedBody')}", "color": "#173177" },
  "keyword1": { "value": "${t('channels.wechat.template.sampleTicket')}" },
  "keyword2": { "value": "${t('channels.wechat.template.sampleInProgress')}" },
  "remark": { "value": "${t('channels.wechat.template.sampleAcceptedRemark')}" }
}`;

// 订阅通知默认数据（结构与模板消息一致，但通常不含 color 字段）
const defaultSubscribeData = (t: TranslateFn) => `{
  "thing1": { "value": "${t('channels.wechat.template.sampleTicket')}" },
  "phrase2": { "value": "${t('channels.wechat.template.sampleInProgress')}" },
  "time3": { "value": "2026-08-04 10:00" }
}`;

// 模板数据项结构：{ "value": "内容", "color": "#173177" }
interface TemplateDataItem {
  value?: string;
  color?: string;
}
type TemplateData = Record<string, TemplateDataItem>;

/** 安全解析模板数据 JSON；JSON.parse 自带的 e.message 是自由文本，仅作兜底 */
function parseTemplateData(json: string, t: TranslateFn): { data: TemplateData | null; error: string | null } {
  if (!json.trim()) return { data: null, error: t('channels.wechat.json.empty') };
  try {
    const parsed = JSON.parse(json);
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
      return { data: null, error: t('channels.wechat.json.parseNotObjectTemplate') };
    }
    return { data: parsed as TemplateData, error: null };
  } catch (e: unknown) {
    return { data: null, error: e instanceof Error ? e.message : t('channels.wechat.json.invalid') };
  }
}

// 常用模板预设：一键填充模板数据；名称与说明只持有 key，JSON 由 t() 拼装（结构逐字保留）
interface TemplatePreset {
  nameKey: TranslationKey;
  descriptionKey: TranslationKey;
  buildDataJson: (t: TranslateFn) => string;
}
const TEMPLATE_PRESETS: TemplatePreset[] = [
  {
    nameKey: 'channels.wechat.template.presetTicketAccepted',
    descriptionKey: 'channels.wechat.template.presetTicketAcceptedDesc',
    buildDataJson: (t) => `{
  "first": { "value": "${t('channels.wechat.template.sampleAcceptedBody')}", "color": "#173177" },
  "keyword1": { "value": "${t('channels.wechat.template.sampleTicket')}" },
  "keyword2": { "value": "${t('channels.wechat.template.sampleInProgress')}" },
  "remark": { "value": "${t('channels.wechat.template.sampleAcceptedRemark')}" }
}`,
  },
  {
    nameKey: 'channels.wechat.template.presetTicketResolved',
    descriptionKey: 'channels.wechat.template.presetTicketResolvedDesc',
    buildDataJson: (t) => `{
  "first": { "value": "${t('channels.wechat.template.sampleResolvedBody')}", "color": "#07C160" },
  "keyword1": { "value": "${t('channels.wechat.template.sampleTicket')}" },
  "keyword2": { "value": "${t('channels.wechat.template.sampleResolved')}" },
  "remark": { "value": "${t('channels.wechat.template.sampleResolvedRemark')}" }
}`,
  },
  {
    nameKey: 'channels.wechat.template.presetReplyAlert',
    descriptionKey: 'channels.wechat.template.presetReplyAlertDesc',
    buildDataJson: (t) => `{
  "first": { "value": "${t('channels.wechat.template.sampleReplyBody')}", "color": "#173177" },
  "keyword1": { "value": "${t('channels.wechat.template.sampleReplyFrom')}" },
  "keyword2": { "value": "${t('channels.wechat.template.sampleReplyCount')}" },
  "remark": { "value": "${t('channels.wechat.template.sampleReplyRemark')}" }
}`,
  },
];

// 订阅通知预设：键名对应订阅模板中的占位符（如 thing1/phrase2/time3）
const SUBSCRIBE_PRESETS: TemplatePreset[] = [
  {
    nameKey: 'channels.wechat.template.presetTicketStatus',
    descriptionKey: 'channels.wechat.template.presetTicketStatusDesc',
    buildDataJson: (t) => `{
  "thing1": { "value": "${t('channels.wechat.template.sampleTicket')}" },
  "phrase2": { "value": "${t('channels.wechat.template.sampleInProgress')}" },
  "time3": { "value": "2026-08-04 10:00" }
}`,
  },
  {
    nameKey: 'channels.wechat.template.presetTicketResolved',
    descriptionKey: 'channels.wechat.template.presetTicketResolvedSubscribeDesc',
    buildDataJson: (t) => `{
  "thing1": { "value": "${t('channels.wechat.template.sampleTicket')}" },
  "phrase2": { "value": "${t('channels.wechat.template.sampleResolved')}" },
  "time3": { "value": "2026-08-04 12:30" }
}`,
  },
];

/** 微信模板消息卡片预览 */
interface TemplatePreviewProps {
  data: TemplateData | null;
  /** 跳转链接：模板消息为 url，订阅通知为 page 路径 */
  link: string;
  /** 卡片头部标题，区分"模板消息"与"订阅通知"；缺省为「模板消息」 */
  title?: string;
}

function TemplatePreview({ data, link, title }: TemplatePreviewProps) {
  const { t } = useI18n();
  const cardTitle = title ?? t('channels.common.templateMessage');
  if (!data) {
    return (
      <div className="text-center py-12 text-xs text-gray-400 dark:text-slate-500">
        <div className="w-12 h-12 mx-auto mb-3 rounded-xl bg-gray-100 dark:bg-slate-700 flex items-center justify-center">
          <Send className="w-6 h-6 text-gray-300 dark:text-slate-600" />
        </div>
        {t('channels.wechat.template.emptyData')}
      </div>
    );
  }

  const entries = Object.entries(data);
  if (entries.length === 0) {
    return (
      <div className="text-center py-12 text-xs text-gray-400 dark:text-slate-500">
        <div className="w-12 h-12 mx-auto mb-3 rounded-xl bg-gray-100 dark:bg-slate-700 flex items-center justify-center">
          <Send className="w-6 h-6 text-gray-300 dark:text-slate-600" />
        </div>
        {t('channels.wechat.template.dataEmpty')}
      </div>
    );
  }

  return (
    <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 overflow-hidden shadow-md shadow-gray-200/50 dark:shadow-black/20">
      {/* 卡片头部 - 微信风格绿色渐变 */}
      <div className="px-4 py-3 bg-gradient-to-r from-green-500 to-emerald-500 dark:from-green-700 dark:to-emerald-700 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <div className="w-8 h-8 rounded-lg bg-white/20 backdrop-blur-sm flex items-center justify-center">
            <MessageCircle className="w-4 h-4 text-white" />
          </div>
          <div>
            <div className="text-sm font-medium text-white">{cardTitle}</div>
            <div className="text-[10px] text-green-50/80">{t('channels.wechat.template.notice')}</div>
          </div>
        </div>
        <span className="text-[10px] text-green-50/80">{t('format.relativeJustNow')}</span>
      </div>
      {/* 卡片内容 */}
      <div className="px-4 py-3 space-y-2">
        {entries.map(([key, item]) => {
          const value = item?.value ?? '';
          const color = item?.color && /^#[0-9A-Fa-f]{3,8}$/.test(item.color) ? item.color : undefined;
          const isFirst = key === 'first';
          const isRemark = key === 'remark';
          return (
            <div key={key} className="text-xs flex items-start gap-2">
              <span className={`flex-shrink-0 inline-block px-1.5 py-0.5 rounded text-[10px] font-mono font-medium ${
                isFirst
                  ? 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300'
                  : isRemark
                  ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300'
                  : 'bg-gray-100 text-gray-500 dark:bg-slate-700 dark:text-slate-400'
              }`}>
                {key}
              </span>
              <span
                className={`flex-1 break-words ${isFirst || isRemark ? 'font-medium' : ''}`}
                style={color ? { color } : undefined}
              >
                {value || t('channels.wechat.template.emptyValue')}
              </span>
            </div>
          );
        })}
      </div>
      {/* 跳转链接提示 */}
      {link && (
        <div className="px-4 py-2 border-t border-gray-100 dark:border-slate-700 text-[10px] text-blue-500 flex items-center gap-1">
          <ExternalLink className="w-3 h-3" />
          <span className="truncate">{t('channels.wechat.template.viewLink', { link })}</span>
        </div>
      )}
    </div>
  );
}

function TemplateMessageTab({ isReadOnly }: TemplateMessageTabProps) {
  const toast = useToast();
  const { t, language } = useI18n();
  const [mode, setMode] = useState<NotifyMode>('template');
  const [conversationId, setConversationId] = useState<number | ''>('');
  const [templateId, setTemplateId] = useState('');
  const [dataJson, setDataJson] = useState(() => defaultTemplateData(t));
  // 模板消息使用 url，订阅通知使用 page（小程序页面路径）
  const [url, setUrl] = useState('');
  const [page, setPage] = useState('');
  const [sending, setSending] = useState(false);
  const [history, setHistory] = useState<SendHistoryEntry[]>([]);

  // 切换模式时重置对应字段，避免上一种模式的数据残留造成混淆
  const handleModeChange = (next: NotifyMode) => {
    if (next === mode || isReadOnly) return;
    setMode(next);
    setTemplateId('');
    setDataJson(next === 'template' ? defaultTemplateData(t) : defaultSubscribeData(t));
    setUrl('');
    setPage('');
  };

  // 实时解析模板数据用于校验和预览
  const parsed = parseTemplateData(dataJson, t);
  const fieldCount = parsed.data ? Object.keys(parsed.data).length : 0;

  const isSubscribe = mode === 'subscribe';
  // 当前模式的跳转链接值（模板→url，订阅→page），用于预览
  const currentLink = isSubscribe ? page.trim() : url.trim();
  // 当前模式对应的预设列表
  const currentPresets = isSubscribe ? SUBSCRIBE_PRESETS : TEMPLATE_PRESETS;
  // 当前模式标题与按钮文案
  const modeTitle = isSubscribe ? t('channels.wechat.template.sendSubscribe') : t('channels.common.sendTemplateMessage');
  const modeButton = isSubscribe ? t('channels.wechat.template.sendSubscribe') : t('channels.common.sendTemplateMessage');
  const modeSuccess = isSubscribe ? t('channels.wechat.template.sentSubscribe') : t('channels.common.templateSent');
  const modeFailure = isSubscribe ? t('channels.wechat.template.sendSubscribeFailed') : t('channels.wechat.template.sendTemplateFailed');

  const handleSend = async () => {
    if (!conversationId) {
      toast.error(t('channels.wechat.template.selectConversation'));
      return;
    }
    if (!templateId.trim()) {
      toast.error(t('channels.wechat.template.enterTemplateId'));
      return;
    }
    if (parsed.error) {
      toast.error(t('channels.wechat.template.jsonInvalidPrefix', { error: parsed.error }));
      return;
    }

    const entry: SendHistoryEntry = {
      id: Date.now(),
      timestamp: new Date().toLocaleString(toIntlLocale(language)),
      mode,
      conversationId: conversationId as number,
      templateId: templateId.trim(),
      success: false,
    };

    try {
      setSending(true);
      if (isSubscribe) {
        const result = await weChatApi.sendSubscribe({
          conversationId,
          templateId: templateId.trim(),
          dataJson,
          page: page.trim() || null,
        });
        if (result.sent) {
          toast.success(modeSuccess);
          entry.success = true;
          setTemplateId('');
          setPage('');
        } else {
          entry.errorMessage = result.errorMessage ?? modeFailure;
          toast.error(result.errorMessage ?? modeFailure);
        }
      } else {
        const result = await weChatApi.sendTemplate({
          conversationId,
          templateId: templateId.trim(),
          dataJson,
          url: url.trim() || null,
        });
        if (result.sent) {
          toast.success(modeSuccess);
          entry.success = true;
          setTemplateId('');
          setUrl('');
        } else {
          entry.errorMessage = result.errorMessage ?? modeFailure;
          toast.error(result.errorMessage ?? modeFailure);
        }
      }
    } catch (err: unknown) {
      entry.errorMessage = err instanceof Error ? err.message : modeFailure;
      toast.error(err instanceof Error ? err.message : modeFailure);
    } finally {
      setSending(false);
      setHistory((prev) => [entry, ...prev].slice(0, 10));
    }
  };

  return (
    <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4 sm:p-6 space-y-4 sm:space-y-6 shadow-sm">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <div className="p-1.5 bg-green-100 dark:bg-green-900/40 rounded-lg">
            <Send className="w-4 h-4 text-green-600 dark:text-green-400" />
          </div>
          <h2 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100">{modeTitle}</h2>
        </div>
        {/* 字段统计 */}
        {fieldCount > 0 && (
          <span className="text-xs px-2 py-0.5 rounded-full font-medium bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-slate-300">
            {t('channels.wechat.template.fieldCount', { count: fieldCount })}
          </span>
        )}
      </div>

      {/* 模式切换：模板消息（旧版）/ 订阅通知（新版） */}
      <div className="flex gap-1 p-1 bg-gray-100 dark:bg-slate-900 rounded-lg w-full sm:w-fit">
        <button
          type="button"
          onClick={() => handleModeChange('template')}
          disabled={isReadOnly || sending}
          className={`flex-1 sm:flex-none px-3 py-1.5 text-xs font-medium rounded-md transition-colors disabled:opacity-40 disabled:cursor-not-allowed ${
            mode === 'template'
              ? 'bg-white dark:bg-slate-700 text-green-700 dark:text-green-400 shadow-sm'
              : 'text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-200'
          }`}
        >
          {t('channels.wechat.template.modeTemplateTab')}
        </button>
        <button
          type="button"
          onClick={() => handleModeChange('subscribe')}
          disabled={isReadOnly || sending}
          className={`flex-1 sm:flex-none px-3 py-1.5 text-xs font-medium rounded-md transition-colors disabled:opacity-40 disabled:cursor-not-allowed ${
            mode === 'subscribe'
              ? 'bg-white dark:bg-slate-700 text-green-700 dark:text-green-400 shadow-sm'
              : 'text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-200'
          }`}
        >
          {t('channels.wechat.template.modeSubscribeTab')}
        </button>
      </div>

      <div className="bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800/50 rounded-lg p-3 text-xs text-amber-800 dark:text-amber-300">
        {isSubscribe ? (
          <>
            <strong>{t('channels.wechat.tipLabel')}</strong>{t('channels.wechat.template.subscribeTipBody')}
          </>
        ) : (
          <>
            <strong>{t('channels.wechat.tipLabel')}</strong>{t('channels.wechat.template.templateTipBody')}
          </>
        )}
      </div>

      {/* 常用模板预设 */}
      <div>
        <label className="block text-xs font-medium text-gray-500 dark:text-slate-400 mb-2">{t('channels.wechat.template.presetLabel')}</label>
        <div className="flex flex-wrap gap-2">
          {currentPresets.map((preset) => (
            <button
              key={preset.nameKey}
              type="button"
              onClick={() => setDataJson(preset.buildDataJson(t))}
              disabled={isReadOnly || sending}
              title={t(preset.descriptionKey)}
              className="px-3 py-1.5 text-xs bg-gray-50 dark:bg-slate-700 border border-gray-200 dark:border-slate-600 rounded-lg text-gray-700 dark:text-slate-200 hover:bg-green-50 dark:hover:bg-green-900/30 hover:border-green-300 dark:hover:border-green-700 hover:text-green-700 dark:hover:text-green-400 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
            >
              {t(preset.nameKey)}
            </button>
          ))}
        </div>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
        <div>
          <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
            {t('channels.wechat.template.targetConversation')} <span className="text-red-500">*</span>
          </label>
          <ConversationSearchInput
            value={conversationId}
            disabled={isReadOnly || sending}
            onSelect={(id) => setConversationId(id)}
          />
          <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('channels.wechat.template.targetConversationHint')}</p>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">
            {t('channels.wechat.template.templateId')} <span className="text-red-500">*</span>
          </label>
          <input
            type="text"
            value={templateId}
            onChange={(e) => setTemplateId(e.target.value)}
            placeholder={isSubscribe ? t('channels.wechat.template.subscribeTemplateIdPlaceholder') : t('channels.wechat.template.templateIdPlaceholder')}
            maxLength={64}
            disabled={isReadOnly || sending}
            className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-green-500 dark:focus:ring-green-400 focus:border-transparent disabled:bg-gray-50 dark:disabled:bg-slate-800 transition-shadow"
          />
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {/* JSON 编辑器 */}
        <div>
          <div className="flex items-center justify-between mb-1">
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300">
              {t('channels.wechat.template.dataLabel')} <span className="text-red-500">*</span>
            </label>
            <div className="flex items-center gap-1">
              <button
                type="button"
                onClick={() => {
                  try {
                    const formatted = JSON.stringify(JSON.parse(dataJson), null, 2);
                    setDataJson(formatted);
                  } catch {
                    toast.error(t('channels.wechat.json.formatFailed'));
                  }
                }}
                disabled={isReadOnly || sending}
                title={t('channels.wechat.json.formatTooltip')}
                className="flex items-center gap-1 px-2 py-1 text-[11px] text-gray-500 dark:text-slate-400 hover:text-green-600 dark:hover:text-green-400 hover:bg-green-50 dark:hover:bg-green-900/30 rounded disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                <Braces className="w-3 h-3" />
                {t('channels.wechat.json.format')}
              </button>
              <button
                type="button"
                onClick={() => setDataJson('{}')}
                disabled={isReadOnly || sending}
                title={t('channels.wechat.json.clearObjectTooltip')}
                className="flex items-center gap-1 px-2 py-1 text-[11px] text-gray-500 dark:text-slate-400 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                <Eraser className="w-3 h-3" />
                {t('channels.wechat.json.clear')}
              </button>
            </div>
          </div>
          <textarea
            value={dataJson}
            onChange={(e) => setDataJson(e.target.value)}
            rows={10}
            disabled={isReadOnly || sending}
            className={`w-full px-3 py-2 bg-gray-50 dark:bg-slate-900 border rounded-lg text-sm font-mono text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-green-500 dark:focus:ring-green-400 focus:border-transparent disabled:bg-gray-50 dark:disabled:bg-slate-800 resize-y transition-shadow ${
              parsed.error
                ? 'border-red-300 dark:border-red-800'
                : 'border-gray-300 dark:border-slate-600'
            }`}
            placeholder={isSubscribe ? defaultSubscribeData(t) : defaultTemplateData(t)}
          />
          <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">
            {t('channels.wechat.template.dataStructurePrefix')}<code className="px-1 py-0.5 bg-gray-100 dark:bg-slate-700 rounded text-pink-600 dark:text-pink-400">{t('channels.wechat.template.dataStructureSample')}</code>{t('channels.wechat.template.dataStructureSuffix')}
          </p>
        </div>

        {/* 卡片预览 */}
        <div>
          <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('channels.wechat.template.previewLabel')}</label>
          <TemplatePreview
            data={parsed.data}
            link={currentLink}
            title={isSubscribe ? t('channels.wechat.template.subscribeTitle') : t('channels.common.templateMessage')}
          />
        </div>
      </div>

      {/* 实时校验状态条 */}
      {parsed.error ? (
        <div className="flex items-start gap-2 px-3 py-2 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800/50 rounded-lg text-xs text-red-700 dark:text-red-300">
          <AlertCircle className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
          <span className="break-all">{t('channels.wechat.json.parseError', { error: parsed.error })}</span>
        </div>
      ) : fieldCount > 0 ? (
        <div className="flex items-center gap-1.5 px-3 py-1.5 bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800/50 rounded-lg text-xs text-green-700 dark:text-green-300">
          <CheckCircle2 className="w-3.5 h-3.5 flex-shrink-0" />
          <span>{t('channels.wechat.template.jsonValid', { count: fieldCount })}</span>
        </div>
      ) : null}

      {isSubscribe ? (
        // 订阅通知：跳转小程序页面路径
        <div>
          <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('channels.wechat.template.pagePath')}</label>
          <input
            type="text"
            value={page}
            onChange={(e) => setPage(e.target.value)}
            placeholder="pages/detail/index?id=123"
            maxLength={500}
            disabled={isReadOnly || sending}
            className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-green-500 dark:focus:ring-green-400 focus:border-transparent disabled:bg-gray-50 dark:disabled:bg-slate-800 transition-shadow"
          />
          <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">
            {t('channels.wechat.template.pagePathHint')}
          </p>
        </div>
      ) : (
        // 模板消息：跳转网页 URL
        <div>
          <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('channels.wechat.template.url')}</label>
          <input
            type="url"
            value={url}
            onChange={(e) => setUrl(e.target.value)}
            placeholder="https://example.com/detail/123"
            maxLength={500}
            disabled={isReadOnly || sending}
            className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-green-500 dark:focus:ring-green-400 focus:border-transparent disabled:bg-gray-50 dark:disabled:bg-slate-800 transition-shadow"
          />
          <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('channels.wechat.template.urlHint')}</p>
        </div>
      )}

      <div className="flex flex-col sm:flex-row sm:justify-end">
        <button
          onClick={() => void handleSend()}
          disabled={isReadOnly || sending || !!parsed.error}
          className="inline-flex items-center justify-center gap-2 w-full sm:w-auto px-5 py-2 bg-gradient-to-r from-green-600 to-emerald-600 text-white rounded-lg text-xs sm:text-sm font-medium hover:from-green-700 hover:to-emerald-700 disabled:from-gray-400 disabled:to-gray-400 shadow-sm transition-all"
        >
          {sending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
          {sending ? t('channels.common.sending') : modeButton}
        </button>
      </div>

      {/* 发送历史记录 */}
      {history.length > 0 && (
        <div className="border-t border-gray-200 dark:border-slate-700 pt-4">
          <div className="flex items-center justify-between mb-2">
            <label className="flex items-center gap-1.5 text-xs font-medium text-gray-500 dark:text-slate-400">
              <History className="w-3.5 h-3.5" />
              {t('channels.wechat.history.title', { count: history.length })}
            </label>
            <button
              type="button"
              onClick={() => setHistory([])}
              className="text-xs text-gray-400 hover:text-red-500 transition-colors"
            >
              {t('channels.wechat.json.clear')}
            </button>
          </div>
          <div className="space-y-1.5 max-h-48 overflow-auto">
            {history.map((h) => (
              <div
                key={h.id}
                className="flex items-center gap-2 px-3 py-2 bg-gray-50 dark:bg-slate-900/40 rounded-lg text-xs"
              >
                <span className={`flex-shrink-0 w-1.5 h-1.5 rounded-full ${h.success ? 'bg-green-500' : 'bg-red-500'}`} />
                <span className="text-gray-400 dark:text-slate-500 flex-shrink-0 font-mono">{h.timestamp}</span>
                <span className="text-gray-600 dark:text-slate-300 flex-shrink-0">
                  {h.mode === 'subscribe' ? t('channels.wechat.history.modeSubscribe') : t('channels.wechat.history.modeTemplate')}
                </span>
                <span className="text-green-600 dark:text-green-400 flex-shrink-0 font-mono">#{h.conversationId}</span>
                <span className="text-gray-500 dark:text-slate-400 truncate flex-1 font-mono" title={h.templateId}>
                  {h.templateId}
                </span>
                {h.errorMessage && (
                  <span className="text-red-500 truncate max-w-[200px]" title={h.errorMessage}>
                    {h.errorMessage}
                  </span>
                )}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

// ====== 自定义菜单 Tab ======
interface MenuManagementTabProps {
  isReadOnly: boolean;
}

/** 菜单 JSON 默认值（可编辑文本框）；中文串走词典，JSON 结构逐字保留 */
const defaultMenuJson = (t: TranslateFn) => `{
  "button": [
    {
      "type": "click",
      "name": "${t('channels.wechat.menu.defaultJsonContact')}",
      "key": "CONTACT_SERVICE"
    },
    {
      "type": "view",
      "name": "${t('channels.wechat.menu.defaultJsonAbout')}",
      "url": "https://example.com/about"
    }
  ]
}`;

// ====== 菜单预览类型与组件 ======
interface WeChatMenuItem {
  type?: string;
  name?: string;
  key?: string;
  url?: string;
  media_id?: string;
  article_id?: string;
  sub_button?: WeChatMenuItem[];
}

interface WeChatMenuSchema {
  button?: WeChatMenuItem[];
}

/** 菜单类型标签、说明与颜色；`labelKey` / `descKey` 只持有 key，渲染处必须经 `t()`（改字段名后 tsc 以 TS2339 抓漏包） */
const MENU_TYPE_META: Record<string, { labelKey: TranslationKey; descKey: TranslationKey; color: string }> = {
  click: { labelKey: 'channels.wechat.menu.type.click', descKey: 'channels.wechat.menu.desc.click', color: 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300' },
  view: { labelKey: 'channels.wechat.menu.type.view', descKey: 'channels.wechat.menu.desc.view', color: 'bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300' },
  scancode_push: { labelKey: 'channels.wechat.menu.type.scancodePush', descKey: 'channels.wechat.menu.desc.scancodePush', color: 'bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300' },
  pic_sysphoto: { labelKey: 'channels.wechat.menu.type.picSysphoto', descKey: 'channels.wechat.menu.desc.picSysphoto', color: 'bg-pink-100 text-pink-700 dark:bg-pink-900/40 dark:text-pink-300' },
  location_select: { labelKey: 'channels.wechat.menu.type.locationSelect', descKey: 'channels.wechat.menu.desc.locationSelect', color: 'bg-cyan-100 text-cyan-700 dark:bg-cyan-900/40 dark:text-cyan-300' },
  media_id: { labelKey: 'channels.wechat.menu.type.mediaId', descKey: 'channels.wechat.menu.desc.mediaId', color: 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300' },
  article_id: { labelKey: 'channels.wechat.menu.type.articleId', descKey: 'channels.wechat.menu.desc.articleId', color: 'bg-indigo-100 text-indigo-700 dark:bg-indigo-900/40 dark:text-indigo-300' },
};

/** 安全解析菜单 JSON，失败返回 null 和错误信息；JSON.parse 自带的 e.message 是自由文本，仅作兜底 */
function parseMenuJson(json: string, t: TranslateFn): { menu: WeChatMenuSchema | null; error: string | null } {
  if (!json.trim()) return { menu: null, error: t('channels.wechat.json.empty') };
  try {
    const parsed = JSON.parse(json) as WeChatMenuSchema;
    if (!parsed || typeof parsed !== 'object') return { menu: null, error: t('channels.wechat.json.parseNotObjectMenu') };
    return { menu: parsed, error: null };
  } catch (e: unknown) {
    return { menu: null, error: e instanceof Error ? e.message : t('channels.wechat.json.invalid') };
  }
}

/** 菜单按钮所需的字段校验：每种 type 对应不同的必填字段 */
const MENU_REQUIRED_FIELDS: Record<string, string[]> = {
  click: ['key'],
  view: ['url'],
  scancode_push: ['key'],
  pic_sysphoto: ['key'],
  location_select: ['key'],
  media_id: ['media_id'],
  article_id: ['article_id'],
};

/** 校验菜单结构，返回警告列表（不阻断提交，但提醒用户修复） */
function validateMenuSchema(menu: WeChatMenuSchema | null, t: TranslateFn): string[] {
  if (!menu?.button) return [];
  const warnings: string[] = [];
  menu.button.forEach((btn, i) => {
    const label = t('channels.wechat.menu.warnTopLabel', { name: btn.name || `#${i + 1}` });
    if (!btn.name) warnings.push(t('channels.wechat.menu.warnMissingName', { label }));
    // 有子菜单的一级菜单不需要 type/key/url
    if (btn.sub_button && btn.sub_button.length > 0) {
      btn.sub_button.forEach((sub, j) => {
        const subLabel = t('channels.wechat.menu.warnSubLabel', { parent: btn.name ?? '', name: sub.name || `#${j + 1}` });
        if (!sub.name) warnings.push(t('channels.wechat.menu.warnMissingName', { label: subLabel }));
        if (!sub.type) warnings.push(t('channels.wechat.menu.warnMissingType', { label: subLabel }));
        const required = sub.type ? MENU_REQUIRED_FIELDS[sub.type] : null;
        if (required) {
          required.forEach((field) => {
            if (!sub[field as keyof WeChatMenuItem]) {
              warnings.push(t('channels.wechat.menu.warnMissingField', { label: subLabel, type: sub.type ?? '', field }));
            }
          });
        }
      });
    } else {
      if (!btn.type) warnings.push(t('channels.wechat.menu.warnMissingType', { label }));
      const required = btn.type ? MENU_REQUIRED_FIELDS[btn.type] : null;
      if (required) {
        required.forEach((field) => {
          if (!btn[field as keyof WeChatMenuItem]) {
            warnings.push(t('channels.wechat.menu.warnMissingField', { label, type: btn.type ?? '', field }));
          }
        });
      }
    }
  });
  return warnings;
}

/** 可视化菜单编辑器：表单式增删改菜单按钮 */
function MenuButtonEditor({
  menu,
  onChange,
  isReadOnly,
}: {
  menu: WeChatMenuSchema | null;
  onChange: (menu: WeChatMenuSchema) => void;
  isReadOnly: boolean;
}) {
  const { t } = useI18n();
  const buttons = menu?.button ?? [];

  /** 更新指定一级菜单 */
  const updateButton = (index: number, patch: Partial<WeChatMenuItem>) => {
    const next = [...buttons];
    next[index] = { ...next[index], ...patch };
    onChange({ button: next });
  };

  /** 添加一级菜单 */
  const addButton = () => {
    if (buttons.length >= 3) return;
    onChange({ button: [...buttons, { type: 'click', name: t('channels.wechat.menu.newMenu'), key: 'NEW_KEY' }] });
  };

  /** 删除一级菜单 */
  const deleteButton = (index: number) => {
    const next = buttons.filter((_, i) => i !== index);
    onChange({ button: next });
  };

  /** 添加二级菜单 */
  const addSubButton = (parentIndex: number) => {
    const next = [...buttons];
    const sub = next[parentIndex].sub_button ?? [];
    if (sub.length >= 5) return;
    // 有子菜单的一级菜单清除自身 type/key/url（微信规范：有子菜单时一级菜单仅作分组）
    next[parentIndex] = {
      ...next[parentIndex],
      type: undefined,
      key: undefined,
      url: undefined,
      media_id: undefined,
      sub_button: [...sub, { type: 'click', name: t('channels.wechat.menu.newSubMenu'), key: 'SUB_KEY' }],
    };
    onChange({ button: next });
  };

  /** 更新二级菜单 */
  const updateSubButton = (parentIndex: number, subIndex: number, patch: Partial<WeChatMenuItem>) => {
    const next = [...buttons];
    const sub = [...(next[parentIndex].sub_button ?? [])];
    sub[subIndex] = { ...sub[subIndex], ...patch };
    next[parentIndex] = { ...next[parentIndex], sub_button: sub };
    onChange({ button: next });
  };

  /** 删除二级菜单 */
  const deleteSubButton = (parentIndex: number, subIndex: number) => {
    const next = [...buttons];
    const sub = (next[parentIndex].sub_button ?? []).filter((_, i) => i !== subIndex);
    next[parentIndex] = { ...next[parentIndex], sub_button: sub.length > 0 ? sub : undefined };
    onChange({ button: next });
  };

  return (
    <div className="space-y-3">
      {buttons.map((btn, i) => {
        const hasSubs = (btn.sub_button?.length ?? 0) > 0;
        const required = btn.type ? MENU_REQUIRED_FIELDS[btn.type] ?? [] : [];
        return (
          <div key={i} className="border border-gray-200 dark:border-slate-600 rounded-lg p-3 space-y-2.5">
            {/* 一级菜单头部 */}
            <div className="flex items-center gap-2">
              <span className="text-xs font-mono text-gray-400 flex-shrink-0">L1-{i + 1}</span>
              <input
                type="text"
                value={btn.name ?? ''}
                onChange={(e) => updateButton(i, { name: e.target.value })}
                placeholder={t('channels.wechat.menu.namePlaceholder')}
                maxLength={16}
                disabled={isReadOnly}
                className="flex-1 min-w-0 px-2.5 py-1.5 text-sm bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-1 focus:ring-green-500 disabled:bg-gray-50 dark:disabled:bg-slate-800"
              />
              <button
                type="button"
                onClick={() => deleteButton(i)}
                disabled={isReadOnly}
                className="flex-shrink-0 p-1.5 text-red-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded disabled:opacity-30 transition-colors"
                title={t('channels.wechat.menu.deleteMenuTooltip')}
              >
                <Trash2 className="w-3.5 h-3.5" />
              </button>
            </div>

            {/* 一级菜单类型和参数（仅无子菜单时显示） */}
            {!hasSubs && (
              <div className="flex flex-wrap items-center gap-2 pl-9">
                <select
                  value={btn.type ?? ''}
                  onChange={(e) => {
                    const type = e.target.value;
                    const patch: Partial<WeChatMenuItem> = { type };
                    // 切换类型时清除非当前类型需要的字段
                    patch.key = undefined;
                    patch.url = undefined;
                    patch.media_id = undefined;
                    updateButton(i, patch);
                  }}
                  disabled={isReadOnly}
                  className="text-xs px-2 py-1 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-1 focus:ring-green-500 disabled:bg-gray-50"
                >
                  {Object.entries(MENU_TYPE_META).map(([typeCode, meta]) => (
                    <option key={typeCode} value={typeCode}>{t(meta.labelKey)}</option>
                  ))}
                </select>
                {/* 根据类型显示对应的字段输入 */}
                {required.includes('key') && (
                  <input
                    type="text"
                    value={btn.key ?? ''}
                    onChange={(e) => updateButton(i, { key: e.target.value })}
                    placeholder="EventKey"
                    disabled={isReadOnly}
                    className="flex-1 min-w-[100px] text-xs px-2 py-1 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded font-mono text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-1 focus:ring-green-500 disabled:bg-gray-50"
                  />
                )}
                {required.includes('url') && (
                  <input
                    type="url"
                    value={btn.url ?? ''}
                    onChange={(e) => updateButton(i, { url: e.target.value })}
                    placeholder="https://…"
                    disabled={isReadOnly}
                    className="flex-1 min-w-[150px] text-xs px-2 py-1 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-1 focus:ring-green-500 disabled:bg-gray-50"
                  />
                )}
                {required.includes('media_id') && (
                  <input
                    type="text"
                    value={btn.media_id ?? ''}
                    onChange={(e) => updateButton(i, { media_id: e.target.value })}
                    placeholder="media_id"
                    disabled={isReadOnly}
                    className="flex-1 min-w-[100px] text-xs px-2 py-1 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded font-mono text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-1 focus:ring-green-500 disabled:bg-gray-50"
                  />
                )}
              </div>
            )}

            {/* 二级菜单列表 */}
            {hasSubs && (
              <div className="pl-9 space-y-1.5">
                {btn.sub_button!.map((sub, j) => {
                  const subRequired = sub.type ? MENU_REQUIRED_FIELDS[sub.type] ?? [] : [];
                  return (
                    <div key={j} className="space-y-1.5">
                      <div className="flex items-center gap-2">
                        <span className="text-[10px] font-mono text-gray-400 flex-shrink-0">L2-{j + 1}</span>
                        <input
                          type="text"
                          value={sub.name ?? ''}
                          onChange={(e) => updateSubButton(i, j, { name: e.target.value })}
                          placeholder={t('channels.wechat.menu.subNamePlaceholder')}
                          maxLength={40}
                          disabled={isReadOnly}
                          className="flex-1 min-w-0 px-2 py-1 text-xs bg-white dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-1 focus:ring-green-500 disabled:bg-gray-50"
                        />
                        <button
                          type="button"
                          onClick={() => deleteSubButton(i, j)}
                          disabled={isReadOnly}
                          className="flex-shrink-0 p-1 text-red-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded disabled:opacity-30 transition-colors"
                          title={t('channels.wechat.menu.deleteSubMenuTooltip')}
                        >
                          <X className="w-3 h-3" />
                        </button>
                      </div>
                      <div className="flex flex-wrap items-center gap-2 pl-7">
                        <select
                          value={sub.type ?? ''}
                          onChange={(e) => {
                            updateSubButton(i, j, {
                              type: e.target.value,
                              key: undefined,
                              url: undefined,
                              media_id: undefined,
                            });
                          }}
                          disabled={isReadOnly}
                          className="text-[11px] px-1.5 py-0.5 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-1 focus:ring-green-500 disabled:bg-gray-50"
                        >
                          {Object.entries(MENU_TYPE_META).map(([typeCode, meta]) => (
                            <option key={typeCode} value={typeCode}>{t(meta.labelKey)}</option>
                          ))}
                        </select>
                        {subRequired.includes('key') && (
                          <input
                            type="text"
                            value={sub.key ?? ''}
                            onChange={(e) => updateSubButton(i, j, { key: e.target.value })}
                            placeholder="EventKey"
                            disabled={isReadOnly}
                            className="flex-1 min-w-[80px] text-[11px] px-1.5 py-0.5 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded font-mono text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-1 focus:ring-green-500 disabled:bg-gray-50"
                          />
                        )}
                        {subRequired.includes('url') && (
                          <input
                            type="url"
                            value={sub.url ?? ''}
                            onChange={(e) => updateSubButton(i, j, { url: e.target.value })}
                            placeholder="https://…"
                            disabled={isReadOnly}
                            className="flex-1 min-w-[120px] text-[11px] px-1.5 py-0.5 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-1 focus:ring-green-500 disabled:bg-gray-50"
                          />
                        )}
                        {subRequired.includes('media_id') && (
                          <input
                            type="text"
                            value={sub.media_id ?? ''}
                            onChange={(e) => updateSubButton(i, j, { media_id: e.target.value })}
                            placeholder="media_id"
                            disabled={isReadOnly}
                            className="flex-1 min-w-[80px] text-[11px] px-1.5 py-0.5 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded font-mono text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-1 focus:ring-green-500 disabled:bg-gray-50"
                          />
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            )}

            {/* 添加子菜单按钮 */}
            {(btn.sub_button?.length ?? 0) < 5 && (
              <div className="pl-9">
                <button
                  type="button"
                  onClick={() => addSubButton(i)}
                  disabled={isReadOnly}
                  className="inline-flex items-center gap-1 text-xs text-green-600 dark:text-green-400 hover:bg-green-50 dark:hover:bg-green-900/30 px-2 py-1 rounded disabled:opacity-30 transition-colors"
                >
                  <Plus className="w-3 h-3" />
                  {t('channels.wechat.menu.addSubMenu')}
                </button>
              </div>
            )}
          </div>
        );
      })}

      {/* 添加一级菜单按钮 */}
      {buttons.length < 3 && (
        <button
          type="button"
          onClick={addButton}
          disabled={isReadOnly}
          className="w-full flex items-center justify-center gap-1.5 py-2 border-2 border-dashed border-gray-300 dark:border-slate-600 rounded-lg text-sm text-gray-500 dark:text-slate-400 hover:border-green-400 hover:text-green-600 dark:hover:border-green-700 dark:hover:text-green-400 disabled:opacity-30 transition-colors"
        >
          <Plus className="w-4 h-4" />
          {t('channels.wechat.menu.addTopMenu', { count: buttons.length })}
        </button>
      )}
    </div>
  );
}

/** 菜单类型图标映射 - 用于手机预览中的视觉区分 */
function MenuTypeIcon({ type, className }: { type?: string; className?: string }) {
  // 简单 SVG 图标，避免引入新依赖
  if (type === 'view') {
    // 跳转网页 - 外链图标
    return (
      <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
        <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" />
        <polyline points="15 3 21 3 21 9" />
        <line x1="10" y1="14" x2="21" y2="3" />
      </svg>
    );
  }
  if (type === 'scancode_push') {
    // 扫码 - 二维码图标
    return (
      <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
        <path d="M3 7V5a2 2 0 0 1 2-2h2" />
        <path d="M17 3h2a2 2 0 0 1 2 2v2" />
        <path d="M21 17v2a2 2 0 0 1-2 2h-2" />
        <path d="M7 21H5a2 2 0 0 1-2-2v-2" />
        <rect x="7" y="7" width="3" height="3" />
        <rect x="14" y="7" width="3" height="3" />
        <rect x="7" y="14" width="3" height="3" />
      </svg>
    );
  }
  if (type === 'pic_sysphoto') {
    // 拍照 - 相机图标
    return (
      <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
        <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z" />
        <circle cx="12" cy="13" r="4" />
      </svg>
    );
  }
  if (type === 'location_select') {
    // 位置 - 定位图标
    return (
      <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
        <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z" />
        <circle cx="12" cy="10" r="3" />
      </svg>
    );
  }
  if (type === 'media_id') {
    // 媒体 - 消息图标
    return (
      <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
        <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
      </svg>
    );
  }
  // click / article_id / 默认 - 点击图标
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
      <polyline points="14 2 14 8 20 8" />
      <line x1="9" y1="15" x2="15" y2="15" />
    </svg>
  );
}

/** 手机样式菜单预览 - 模拟微信底部菜单栏 */
function MenuPreview({ menu }: { menu: WeChatMenuSchema | null }) {
  const { t } = useI18n();
  const buttons = menu?.button ?? [];

  if (!buttons.length) {
    return (
      <div className="text-center py-6 text-xs text-gray-400 dark:text-slate-500">
        <div className="w-10 h-10 mx-auto mb-2 rounded-lg bg-gray-100 dark:bg-slate-700 flex items-center justify-center">
          <Layout className="w-5 h-5 text-gray-300 dark:text-slate-600" />
        </div>
        {t('channels.wechat.menu.previewEmpty')}
      </div>
    );
  }

  return (
    <div className="flex border-t border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800 relative">
      {buttons.map((btn, idx) => {
        const subButtons = btn.sub_button ?? [];
        const hasSub = subButtons.length > 0;
        const meta = btn.type ? MENU_TYPE_META[btn.type] : null;
        return (
          <div key={`${idx}-${btn.name ?? btn.type ?? ''}`} className="relative group flex-1 text-center">
            <button
              type="button"
              className={`w-full py-2.5 px-1 text-xs transition-colors flex flex-col items-center gap-0.5 ${
                hasSub
                  ? 'text-gray-700 dark:text-slate-200 hover:bg-gray-50 dark:hover:bg-slate-700/50'
                  : 'text-gray-700 dark:text-slate-200 hover:bg-gray-50 dark:hover:bg-slate-700/50'
              }`}
            >
              <span className={`flex-shrink-0 ${meta ? meta.color.split(' ')[1] : 'text-gray-400 dark:text-slate-500'}`}>
                <MenuTypeIcon type={btn.type} className="w-3.5 h-3.5" />
              </span>
              <span className="truncate w-full leading-tight">{btn.name || t('channels.wechat.menu.unnamed')}</span>
              {hasSub && (
                <span className="absolute top-1 right-1 text-gray-300 dark:text-slate-600">
                  <svg className="w-2.5 h-2.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={3} strokeLinecap="round" strokeLinejoin="round">
                    <polyline points="18 15 12 9 6 15" />
                  </svg>
                </span>
              )}
            </button>
            {hasSub && (
              <div className="absolute bottom-full left-1/2 -translate-x-1/2 mb-1 hidden group-hover:block z-20 w-44 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-lg shadow-xl overflow-hidden">
                {/* 三角形指示器 */}
                <div className="absolute -bottom-1 left-1/2 -translate-x-1/2 w-2 h-2 bg-white dark:bg-slate-800 border-r border-b border-gray-200 dark:border-slate-600 rotate-45" />
                <div className="px-3 py-1.5 text-[10px] font-medium text-gray-500 dark:text-slate-400 bg-gray-50 dark:bg-slate-900/60 border-b border-gray-100 dark:border-slate-700 flex items-center gap-1.5">
                  <MenuTypeIcon type={btn.type} className="w-3 h-3" />
                  {btn.name}{t('channels.wechat.menu.subMenuSuffix')}
                </div>
                <ul className="max-h-52 overflow-y-auto">
                  {subButtons.map((sub, sIdx) => {
                    const subMeta = sub.type ? MENU_TYPE_META[sub.type] : null;
                    return (
                      <li
                        key={`${sIdx}-${sub.name ?? sub.type ?? ''}`}
                        className="px-3 py-2 text-xs text-gray-700 dark:text-slate-200 hover:bg-green-50 dark:hover:bg-green-900/30 hover:text-green-700 dark:hover:text-green-400 truncate flex items-center gap-1.5 transition-colors"
                      >
                        <span className={`flex-shrink-0 ${subMeta ? subMeta.color.split(' ')[1] : 'text-gray-400 dark:text-slate-500'}`}>
                          <MenuTypeIcon type={sub.type} className="w-3 h-3" />
                        </span>
                        <span className="flex-1 truncate">{sub.name || t('channels.wechat.menu.unnamed')}</span>
                      </li>
                    );
                  })}
                </ul>
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

/** 菜单结构详情列表 - 卡片式展示，带类型图标 */
function MenuDetailsList({ menu }: { menu: WeChatMenuSchema | null }) {
  const { t } = useI18n();
  const buttons = menu?.button ?? [];
  if (!buttons.length) return null;

  return (
    <div className="space-y-2">
      {buttons.map((btn, idx) => {
        const meta = btn.type ? MENU_TYPE_META[btn.type] : null;
        const subButtons = btn.sub_button ?? [];
        return (
          <div key={`${idx}-${btn.name ?? btn.type ?? ''}`} className="rounded-lg border border-gray-200 dark:border-slate-700 overflow-hidden shadow-sm hover:shadow transition-shadow">
            <div className="flex items-center gap-2.5 px-3 py-2.5 bg-gradient-to-r from-gray-50 to-gray-50/50 dark:from-slate-900/60 dark:to-slate-900/30">
              <span className={`flex-shrink-0 ${meta ? meta.color.split(' ')[1] : 'text-gray-400 dark:text-slate-500'}`}>
                <MenuTypeIcon type={btn.type} className="w-4 h-4" />
              </span>
              <span className="text-sm font-medium text-gray-900 dark:text-slate-100 flex-1 truncate">
                <span className="text-gray-400 dark:text-slate-500 mr-1">{idx + 1}.</span>
                {btn.name || t('channels.wechat.menu.unnamed')}
              </span>
              {btn.type && (
                <span className={`text-[10px] px-1.5 py-0.5 rounded font-medium ${meta?.color ?? 'bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-slate-300'}`}>
                  {meta ? t(meta.labelKey) : btn.type}
                </span>
              )}
              {subButtons.length > 0 && (
                <span className="text-[10px] text-gray-400 dark:text-slate-500 flex items-center gap-0.5">
                  <svg className="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
                    <polyline points="6 9 12 15 18 9" />
                  </svg>
                  {t('channels.wechat.menu.subMenuCount', { count: subButtons.length })}
                </span>
              )}
            </div>
            {/* 一级菜单详情 */}
            {!subButtons.length && (btn.key || btn.url || btn.media_id) && (
              <div className="px-3 py-2 text-[11px] text-gray-500 dark:text-slate-400 bg-white dark:bg-slate-800 border-t border-gray-100 dark:border-slate-700 space-y-1">
                {btn.key && (
                  <div className="flex items-center gap-1.5">
                    <span className="text-gray-400 dark:text-slate-500 flex-shrink-0">key:</span>
                    <code className="font-mono px-1.5 py-0.5 bg-gray-100 dark:bg-slate-700 rounded text-pink-600 dark:text-pink-400 text-[10px] break-all">{btn.key}</code>
                  </div>
                )}
                {btn.url && (
                  <div className="flex items-center gap-1.5 min-w-0">
                    <span className="text-gray-400 dark:text-slate-500 flex-shrink-0">url:</span>
                    <code className="font-mono px-1.5 py-0.5 bg-gray-100 dark:bg-slate-700 rounded text-blue-600 dark:text-blue-400 text-[10px] truncate flex-1">{btn.url}</code>
                  </div>
                )}
                {btn.media_id && (
                  <div className="flex items-center gap-1.5 min-w-0">
                    <span className="text-gray-400 dark:text-slate-500 flex-shrink-0">media_id:</span>
                    <code className="font-mono px-1.5 py-0.5 bg-gray-100 dark:bg-slate-700 rounded text-green-600 dark:text-green-400 text-[10px] truncate flex-1">{btn.media_id}</code>
                  </div>
                )}
              </div>
            )}
            {/* 二级菜单列表 */}
            {subButtons.length > 0 && (
              <ul className="bg-white dark:bg-slate-800 border-t border-gray-100 dark:border-slate-700">
                {subButtons.map((sub, sIdx) => {
                  const subMeta = sub.type ? MENU_TYPE_META[sub.type] : null;
                  return (
                    <li key={sIdx} className="flex items-center gap-2 px-3 py-2 text-xs border-b last:border-b-0 border-gray-100 dark:border-slate-700 hover:bg-gray-50 dark:hover:bg-slate-700/30 transition-colors">
                      <span className="text-gray-300 dark:text-slate-600 flex-shrink-0">└</span>
                      <span className={`flex-shrink-0 ${subMeta ? subMeta.color.split(' ')[1] : 'text-gray-400 dark:text-slate-500'}`}>
                        <MenuTypeIcon type={sub.type} className="w-3 h-3" />
                      </span>
                      <span className="text-gray-700 dark:text-slate-200 flex-1 truncate">{sub.name || t('channels.wechat.menu.unnamed')}</span>
                      {sub.type && (
                        <span className={`text-[10px] px-1.5 py-0.5 rounded font-medium ${subMeta?.color ?? 'bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-slate-300'}`}>
                          {subMeta ? t(subMeta.labelKey) : sub.type}
                        </span>
                      )}
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        );
      })}
    </div>
  );
}

function MenuManagementTab({ isReadOnly }: MenuManagementTabProps) {
  const toast = useToast();
  const { t } = useI18n();
  const [menuJson, setMenuJson] = useState(() => defaultMenuJson(t));
  const [currentMenu, setCurrentMenu] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [deleting, setDeleting] = useState(false);
  // 可视化编辑器与 JSON 编辑器双向同步：showEditor 切换显示模式
  const [showEditor, setShowEditor] = useState(true);

  // 实时解析编辑中的 JSON，用于预览和校验
  const parsed = parseMenuJson(menuJson, t);
  const buttons = parsed.menu?.button ?? [];
  const totalSubButtons = buttons.reduce((sum, b) => sum + (b.sub_button?.length ?? 0), 0);
  const overLimit = buttons.length > 3 || buttons.some((b) => (b.sub_button?.length ?? 0) > 5);
  // Schema 校验警告（字段缺失等）
  const schemaWarnings = parsed.menu ? validateMenuSchema(parsed.menu, t) : [];

  const handleLoadCurrent = async () => {
    try {
      setLoading(true);
      const result = await weChatApi.getMenu();
      const menu = result.menu;
      if (menu) {
        // 格式化 JSON 显示
        try {
          setCurrentMenu(JSON.stringify(JSON.parse(menu), null, 2));
        } catch {
          setCurrentMenu(menu);
        }
      } else {
        setCurrentMenu(null);
        toast.info(t('channels.wechat.menu.notConfigured'));
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('channels.wechat.menu.queryFailed'));
    } finally {
      setLoading(false);
    }
  };

  const handleCreate = async () => {
    if (parsed.error) {
      toast.error(t('channels.wechat.json.invalidPrefix', { error: parsed.error }));
      return;
    }
    if (overLimit) {
      toast.error(t('channels.wechat.menu.limitExceeded'));
      return;
    }

    try {
      setSaving(true);
      const result = await weChatApi.createMenu({ menuJson });
      if (result.success) {
        toast.success(t('channels.wechat.menu.createSucceeded'));
        // 刷新当前菜单显示
        await handleLoadCurrent();
      } else {
        toast.error(result.errorMessage ?? t('channels.wechat.menu.createFailed'));
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('channels.wechat.menu.createFailed'));
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    if (!window.confirm(t('channels.wechat.menu.deleteConfirm'))) {
      return;
    }
    try {
      setDeleting(true);
      const result = await weChatApi.deleteMenu();
      if (result.success) {
        toast.success(t('channels.wechat.menu.deleted'));
        setCurrentMenu(null);
      } else {
        toast.error(result.errorMessage ?? t('channels.wechat.menu.deleteFailed'));
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('channels.wechat.menu.deleteFailed'));
    } finally {
      setDeleting(false);
    }
  };

  return (
    <div className="space-y-4 sm:space-y-6">
      {/* 当前菜单查询 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4 sm:p-6 space-y-3 shadow-sm">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <div className="p-1.5 bg-green-100 dark:bg-green-900/40 rounded-lg">
              <Eye className="w-4 h-4 text-green-600 dark:text-green-400" />
            </div>
            <h2 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100">{t('channels.wechat.menu.currentTitle')}</h2>
          </div>
          <button
            onClick={() => void handleLoadCurrent()}
            disabled={loading}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 text-sm text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700 rounded-lg transition-colors"
          >
            <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
            {t('channels.wechat.menu.query')}
          </button>
        </div>
        {currentMenu ? (
          <div className="space-y-2">
            <pre className="px-3 py-2 bg-gray-50 dark:bg-slate-900/60 border border-gray-200 dark:border-slate-700 rounded-lg text-xs font-mono text-gray-700 dark:text-slate-300 overflow-auto max-h-60 leading-relaxed">
              {currentMenu}
            </pre>
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-1.5 text-[11px] text-green-600 dark:text-green-400">
                <CheckCircle2 className="w-3 h-3" />
                <span>{t('channels.wechat.menu.loadedCurrent')}</span>
              </div>
              <button
                type="button"
                onClick={() => {
                  setMenuJson(currentMenu);
                  setShowEditor(true);
                  toast.success(t('channels.wechat.menu.loadedToEditor'));
                }}
                disabled={isReadOnly}
                className="inline-flex items-center gap-1 px-2.5 py-1 text-xs text-green-600 dark:text-green-400 hover:bg-green-50 dark:hover:bg-green-900/30 rounded-lg disabled:opacity-30 transition-colors"
                title={t('channels.wechat.menu.loadToEditorTooltip')}
              >
                <ArrowDownToLine className="w-3.5 h-3.5" />
                {t('channels.wechat.menu.loadToEditor')}
              </button>
            </div>
          </div>
        ) : (
          <div className="flex flex-col items-center justify-center py-8 text-center">
            <div className="w-12 h-12 rounded-xl bg-gray-100 dark:bg-slate-700 flex items-center justify-center mb-2">
              <Eye className="w-6 h-6 text-gray-300 dark:text-slate-600" />
            </div>
            <p className="text-sm text-gray-400 dark:text-slate-500">{t('channels.wechat.menu.queryHint')}</p>
          </div>
        )}
        {currentMenu && (
          <button
            onClick={() => void handleDelete()}
            disabled={isReadOnly || deleting}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 text-sm text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg transition-colors disabled:opacity-50"
          >
            {deleting ? <Loader2 className="w-4 h-4 animate-spin" /> : <Trash2 className="w-4 h-4" />}
            {t('channels.wechat.menu.deleteAll')}
          </button>
        )}
      </div>

      {/* 创建/更新菜单 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4 sm:p-6 space-y-4 shadow-sm">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <div className="p-1.5 bg-green-100 dark:bg-green-900/40 rounded-lg">
              <Layout className="w-4 h-4 text-green-600 dark:text-green-400" />
            </div>
            <h2 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100">{t('channels.wechat.menu.createOrUpdate')}</h2>
          </div>
          {/* 实时统计 */}
          <div className="flex items-center gap-2 text-xs">
            <span className={`px-2 py-0.5 rounded-full font-medium ${
              buttons.length > 3
                ? 'bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300'
                : 'bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-slate-300'
            }`}>
              {t('channels.wechat.menu.level1Count', { count: buttons.length })}
            </span>
            <span className="px-2 py-0.5 rounded-full font-medium bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-slate-300">
              {t('channels.wechat.menu.level2Count', { count: totalSubButtons })}
            </span>
          </div>
        </div>
        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2">
          <p className="text-sm text-gray-500 dark:text-slate-400">
            {t('channels.wechat.menu.editorHint')}
          </p>
          {/* 可视化 / JSON 切换 */}
          <div className="flex items-center gap-1 bg-gray-100 dark:bg-slate-700 rounded-lg p-0.5 w-full sm:w-fit">
            <button
              type="button"
              onClick={() => setShowEditor(true)}
              className={`flex-1 sm:flex-none px-2.5 py-1 text-xs rounded-md transition-colors ${
                showEditor
                  ? 'bg-white dark:bg-slate-900 text-green-600 dark:text-green-400 shadow-sm'
                  : 'text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-200'
              }`}
            >
              {t('channels.wechat.menu.visualEditor')}
            </button>
            <button
              type="button"
              onClick={() => setShowEditor(false)}
              className={`flex-1 sm:flex-none px-2.5 py-1 text-xs rounded-md transition-colors ${
                !showEditor
                  ? 'bg-white dark:bg-slate-900 text-green-600 dark:text-green-400 shadow-sm'
                  : 'text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-200'
              }`}
            >
              {t('channels.wechat.menu.jsonEditor')}
            </button>
          </div>
        </div>

        {/* 实时校验状态条 */}
        {parsed.error ? (
          <div className="flex items-start gap-2 px-3 py-2 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800/50 rounded-lg text-xs text-red-700 dark:text-red-300">
            <AlertCircle className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
            <span className="break-all">{t('channels.wechat.json.parseError', { error: parsed.error })}</span>
          </div>
        ) : overLimit ? (
          <div className="flex items-start gap-2 px-3 py-2 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800/50 rounded-lg text-xs text-amber-700 dark:text-amber-300">
            <AlertCircle className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
            <span>{t('channels.wechat.menu.limitExceeded')}</span>
          </div>
        ) : schemaWarnings.length > 0 ? (
          <div className="space-y-1">
            <div className="flex items-start gap-2 px-3 py-2 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800/50 rounded-lg text-xs text-amber-700 dark:text-amber-300">
              <AlertCircle className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
              <div>
                <div>{t('channels.wechat.menu.issueCount', { count: schemaWarnings.length })}</div>
                <ul className="mt-1 ml-4 list-disc space-y-0.5">
                  {schemaWarnings.slice(0, 5).map((w, idx) => (
                    <li key={idx}>{w}</li>
                  ))}
                  {schemaWarnings.length > 5 && <li>{t('channels.wechat.menu.moreIssues', { count: schemaWarnings.length - 5 })}</li>}
                </ul>
              </div>
            </div>
          </div>
        ) : buttons.length > 0 ? (
          <div className="flex items-center gap-1.5 px-3 py-1.5 bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800/50 rounded-lg text-xs text-green-700 dark:text-green-300">
            <CheckCircle2 className="w-3.5 h-3.5 flex-shrink-0" />
            <span>{t('channels.wechat.menu.validSummary', { count: buttons.length })}{totalSubButtons > 0 ? t('channels.wechat.menu.validSummarySub', { count: totalSubButtons }) : ''}</span>
          </div>
        ) : null}

        {/* 可视化编辑器模式 */}
        {showEditor && !parsed.error && (
          <MenuButtonEditor
            menu={parsed.menu}
            isReadOnly={isReadOnly || saving}
            onChange={(newMenu) => setMenuJson(JSON.stringify(newMenu, null, 2))}
          />
        )}

        {/* JSON 编辑器 + 手机预览（JSON 模式下显示） */}
        {!showEditor && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          {/* JSON 编辑器 */}
          <div>
            <div className="flex items-center justify-between mb-1.5">
              <label className="block text-xs font-medium text-gray-500 dark:text-slate-400">{t('channels.wechat.menu.jsonLabel')}</label>
              <div className="flex items-center gap-1">
                <button
                  type="button"
                  onClick={() => {
                    try {
                      const formatted = JSON.stringify(JSON.parse(menuJson), null, 2);
                      setMenuJson(formatted);
                    } catch {
                      toast.error(t('channels.wechat.json.formatFailed'));
                    }
                  }}
                  disabled={isReadOnly || saving}
                  title={t('channels.wechat.json.formatTooltip')}
                  className="flex items-center gap-1 px-2 py-1 text-[11px] text-gray-500 dark:text-slate-400 hover:text-green-600 dark:hover:text-green-400 hover:bg-green-50 dark:hover:bg-green-900/30 rounded disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
                >
                  <Braces className="w-3 h-3" />
                  {t('channels.wechat.json.format')}
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setMenuJson('{\n  "button": []\n}');
                  }}
                  disabled={isReadOnly || saving}
                  title={t('channels.wechat.json.clearMenuTooltip')}
                  className="flex items-center gap-1 px-2 py-1 text-[11px] text-gray-500 dark:text-slate-400 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
                >
                  <Eraser className="w-3 h-3" />
                  {t('channels.wechat.json.clear')}
                </button>
              </div>
            </div>
            <textarea
              value={menuJson}
              onChange={(e) => setMenuJson(e.target.value)}
              rows={14}
              disabled={isReadOnly || saving}
              className={`w-full px-3 py-2 bg-gray-50 dark:bg-slate-900 border rounded-lg text-sm font-mono text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-green-500 dark:focus:ring-green-400 focus:border-transparent disabled:bg-gray-50 dark:disabled:bg-slate-800 resize-y transition-shadow ${
                parsed.error
                  ? 'border-red-300 dark:border-red-800'
                  : 'border-gray-300 dark:border-slate-600'
              }`}
              placeholder={defaultMenuJson(t)}
            />
          </div>

          {/* 手机预览 - 模拟真实手机外观 */}
          <div>
            <label className="block text-xs font-medium text-gray-500 dark:text-slate-400 mb-1.5 flex items-center gap-1">
              <Smartphone className="w-3.5 h-3.5" />
              {t('channels.wechat.menu.phonePreview')}
            </label>
            <div className="flex justify-center">
              {/* 手机外壳 */}
              <div className="w-full max-w-[280px] bg-gray-900 dark:bg-black rounded-[2rem] p-2 shadow-xl shadow-gray-400/30 dark:shadow-black/40">
                {/* 手机屏幕 */}
                <div className="bg-white dark:bg-slate-800 rounded-[1.5rem] overflow-hidden relative">
                  {/* 状态栏 - 模拟 iOS/Android 顶部 */}
                  <div className="bg-green-600 dark:bg-green-700 px-4 pt-2 pb-1 flex items-center justify-between text-white text-[10px] font-medium">
                    <span className="flex items-center gap-1">
                      {/* 信号 */}
                      <svg className="w-3 h-3" viewBox="0 0 24 24" fill="currentColor">
                        <rect x="1" y="14" width="3" height="6" rx="0.5" />
                        <rect x="6" y="10" width="3" height="10" rx="0.5" />
                        <rect x="11" y="6" width="3" height="14" rx="0.5" />
                        <rect x="16" y="2" width="3" height="18" rx="0.5" opacity="0.4" />
                      </svg>
                      {/* WiFi */}
                      <svg className="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
                        <path d="M5 12.55a11 11 0 0 1 14.08 0" />
                        <path d="M1.42 9a16 16 0 0 1 21.16 0" />
                        <path d="M8.53 16.11a6 6 0 0 1 6.95 0" />
                        <line x1="12" y1="20" x2="12.01" y2="20" />
                      </svg>
                    </span>
                    <span className="font-semibold">9:41</span>
                    <span className="flex items-center gap-0.5">
                      {/* 电池 */}
                      <svg className="w-4 h-3" viewBox="0 0 24 12" fill="none">
                        <rect x="1" y="1" width="20" height="10" rx="2" stroke="currentColor" strokeWidth="1" fill="none" />
                        <rect x="3" y="3" width="14" height="6" rx="1" fill="currentColor" />
                        <rect x="22" y="4" width="1.5" height="4" rx="0.5" fill="currentColor" />
                      </svg>
                    </span>
                  </div>
                  {/* 公众号头部 */}
                  <div className="bg-green-600 dark:bg-green-700 px-4 py-2.5 flex items-center justify-between">
                    <div className="flex items-center gap-2 min-w-0">
                      <div className="w-7 h-7 rounded-lg bg-white/20 backdrop-blur-sm flex items-center justify-center flex-shrink-0">
                        <MessageCircle className="w-4 h-4 text-white" />
                      </div>
                      <div className="min-w-0">
                        <div className="text-sm font-medium text-white truncate">{t('channels.wechat.phone.accountName')}</div>
                        <div className="text-[9px] text-green-50/80">{t('channels.wechat.phone.accountType')}</div>
                      </div>
                    </div>
                    <button type="button" className="text-white/80 hover:text-white flex-shrink-0" title={t('channels.wechat.phone.more')}>
                      <svg className="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
                        <circle cx="12" cy="12" r="1" />
                        <circle cx="19" cy="12" r="1" />
                        <circle cx="5" cy="12" r="1" />
                      </svg>
                    </button>
                  </div>
                  {/* 模拟内容区 - 微信对话风格 */}
                  <div className="px-3 py-4 bg-gray-50 dark:bg-slate-900/40 min-h-[160px] space-y-3">
                    {/* 系统提示 */}
                    <div className="text-center">
                      <span className="inline-block px-2 py-0.5 bg-gray-200 dark:bg-slate-700 rounded text-[9px] text-gray-500 dark:text-slate-400">
                        {t('channels.wechat.phone.followed')}
                      </span>
                    </div>
                    {/* 客户消息气泡 */}
                    <div className="flex items-start gap-1.5">
                      <div className="w-6 h-6 rounded-full bg-gradient-to-br from-green-400 to-emerald-500 flex-shrink-0 flex items-center justify-center text-[9px] text-white font-bold">
                        {t('channels.wechat.phone.customerAvatar')}
                      </div>
                      <div className="bg-white dark:bg-slate-700 rounded-lg rounded-tl-sm px-2.5 py-1.5 text-[10px] text-gray-700 dark:text-slate-200 shadow-sm max-w-[70%]">
                        {t('channels.wechat.phone.customerMessage')}
                      </div>
                    </div>
                    {/* 自动回复消息 */}
                    <div className="flex items-start gap-1.5 flex-row-reverse">
                      <div className="w-6 h-6 rounded-full bg-gradient-to-br from-blue-400 to-indigo-500 flex-shrink-0 flex items-center justify-center text-[9px] text-white font-bold">
                        {t('channels.wechat.phone.agentAvatar')}
                      </div>
                      <div className="bg-green-500 text-white rounded-lg rounded-tr-sm px-2.5 py-1.5 text-[10px] shadow-sm max-w-[70%]">
                        {t('channels.wechat.phone.autoReplyMessage')}
                      </div>
                    </div>
                  </div>
                  {/* 底部菜单栏 */}
                  <MenuPreview menu={parsed.menu} />
                  {/* Home 指示器 */}
                  <div className="bg-white dark:bg-slate-800 py-1 flex justify-center">
                    <div className="w-24 h-1 rounded-full bg-gray-300 dark:bg-slate-600" />
                  </div>
                </div>
              </div>
            </div>
            <p className="text-[10px] text-gray-400 dark:text-slate-500 mt-2 text-center">
              {t('channels.wechat.phone.hoverHint')}
            </p>
          </div>
        </div>
        )}

        {/* 可视化模式下的手机预览（紧凑显示） */}
        {showEditor && !parsed.error && buttons.length > 0 && (
          <div className="flex justify-center">
            <div className="w-full max-w-[280px]">
              <div className="bg-gray-900 dark:bg-black rounded-[2rem] p-2 shadow-xl">
                <div className="bg-white dark:bg-slate-800 rounded-[1.5rem] overflow-hidden">
                  <div className="bg-green-600 dark:bg-green-700 px-4 py-2 flex items-center justify-between text-white">
                    <span className="text-xs font-medium">{t('channels.wechat.phone.accountName')}</span>
                    <span className="text-[9px] text-green-50/80">{t('channels.wechat.phone.accountType')}</span>
                  </div>
                  <div className="px-3 py-4 bg-gray-50 dark:bg-slate-900/40 min-h-[80px]">
                    <div className="bg-green-500 text-white rounded-lg rounded-tr-sm px-2.5 py-1.5 text-[10px] inline-block">
                      {t('channels.wechat.phone.autoReplyMessageCompact')}
                    </div>
                  </div>
                  <MenuPreview menu={parsed.menu} />
                  <div className="bg-white dark:bg-slate-800 py-1 flex justify-center">
                    <div className="w-24 h-1 rounded-full bg-gray-300 dark:bg-slate-600" />
                  </div>
                </div>
              </div>
            </div>
          </div>
        )}

        {/* 菜单结构详情 */}
        {buttons.length > 0 && !parsed.error && (
          <div>
            <label className="block text-xs font-medium text-gray-500 dark:text-slate-400 mb-2">{t('channels.wechat.menu.structureDetails')}</label>
            <MenuDetailsList menu={parsed.menu} />
          </div>
        )}

        <div className="flex flex-col sm:flex-row sm:justify-end">
          <button
            onClick={() => void handleCreate()}
            disabled={isReadOnly || saving || !!parsed.error || overLimit}
            className="inline-flex items-center justify-center gap-2 w-full sm:w-auto px-5 py-2 bg-gradient-to-r from-green-600 to-emerald-600 text-white rounded-lg text-xs sm:text-sm font-medium hover:from-green-700 hover:to-emerald-700 disabled:from-gray-400 disabled:to-gray-400 shadow-sm transition-all"
          >
            {saving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Layout className="w-4 h-4" />}
            {saving ? t('channels.wechat.menu.publishing') : t('channels.wechat.menu.publish')}
          </button>
        </div>
      </div>

      {/* 菜单说明 - 卡片网格布局 */}
      <div className="bg-gradient-to-br from-blue-50/80 to-indigo-50/50 dark:from-blue-900/20 dark:to-indigo-900/10 rounded-xl border border-blue-200 dark:border-blue-800/50 p-4 sm:p-6 space-y-4 shadow-sm">
        <h3 className="text-sm font-semibold text-blue-900 dark:text-blue-200 flex items-center gap-2">
          <div className="p-1 bg-blue-100 dark:bg-blue-900/40 rounded-lg">
            <Info className="w-4 h-4 text-blue-600 dark:text-blue-400" />
          </div>
          {t('channels.wechat.menu.typeGuideTitle')}
        </h3>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
          {Object.entries(MENU_TYPE_META).map(([type, meta]) => (
            <div
              key={type}
              className="flex items-start gap-2.5 p-2.5 bg-white/60 dark:bg-slate-800/40 rounded-lg border border-blue-100 dark:border-blue-900/30 hover:border-blue-200 dark:hover:border-blue-800/50 transition-colors"
            >
              <span className="flex-shrink-0 mt-0.5 text-blue-500 dark:text-blue-400">
                <MenuTypeIcon type={type} className="w-3.5 h-3.5" />
              </span>
              <div className="flex-1 min-w-0">
                <code className={`text-[10px] px-1.5 py-0.5 rounded font-mono font-medium ${meta.color}`}>
                  {type}
                </code>
                <p className="text-xs text-blue-800 dark:text-blue-300 mt-1 leading-relaxed">{t(meta.descKey)}</p>
              </div>
            </div>
          ))}
        </div>
        <div className="flex items-start gap-2 p-3 bg-amber-50 dark:bg-amber-900/30 border border-amber-200 dark:border-amber-800/50 rounded-lg text-xs text-amber-700 dark:text-amber-300">
          <AlertCircle className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
          <span><strong>{t('channels.wechat.tipLabel')}</strong>{t('channels.wechat.menu.typeGuideTip')}</span>
        </div>
      </div>
    </div>
  );
}
