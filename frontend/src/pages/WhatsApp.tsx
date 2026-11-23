import { useState, useEffect, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { whatsAppApi } from '../api/modules/system';
import { mailboxApi } from '../api/modules/core';
import type {
  WhatsAppSettingsDto,
  WhatsAppStatusDto,
  UpdateWhatsAppSettingsRequest,
  WhatsAppTestResult,
} from '../api/types/system';
import { useToast } from '../context/ToastContext';
import { useI18n } from '../context/I18nContext';
import { usePermission } from '../hooks/usePermission';
import type { TranslationKey } from '../locales/zh-Hans';
import { formatDateTime } from '../utils/format';
import type { MailboxResponse } from '../api/types';
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
  Settings,
  Shield,
  FileText,
  Sparkles,
  Globe,
  Key,
  Bell,
  Cloud,
  FileCheck,
  Smartphone,
  Braces,
  Eraser,
  Eye,
  EyeOff,
  Clock,
  Zap,
  BookOpen,
  HelpCircle,
  ChevronDown,
  ChevronRight,
  Phone,
  TestTube,
  ArrowRight,
} from 'lucide-react';

type TabType = 'config' | 'template' | 'status';

// 常用模板语言预设。
// 语言名一律写**语言自称（endonym）**：它与 Meta 模板的 language code 一一对应，不随界面语言变化
// （英文界面下 `zh_CN` 仍显示 `简体中文`），故刻意保留可读字面量、不进字典
// ——登记为波次 10 的"非文案中文/接受例外"（见交付报告第 5 节）。
const TEMPLATE_LANGUAGES = [
  { code: 'zh_CN', label: '简体中文' }, // i18n-exempt: 语言自称（Meta 模板语言码的自称），不随界面语言变化
  { code: 'zh_TW', label: '繁體中文' }, // i18n-exempt: 同上
  { code: 'en_US', label: 'English (US)' },
  { code: 'en_GB', label: 'English (UK)' },
  { code: 'ja_JP', label: '日本語' }, // i18n-exempt: 同上
  { code: 'ko_KR', label: '한국어' },
  { code: 'es_ES', label: 'Español' },
  { code: 'fr_FR', label: 'Français' },
  { code: 'de_DE', label: 'Deutsch' },
  { code: 'pt_BR', label: 'Português (BR)' },
  { code: 'ru_RU', label: 'Русский' },
  { code: 'ar_AR', label: 'العربية' },
];

// 常用 API 版本
const API_VERSIONS = ['v23.0', 'v22.0', 'v21.0', 'v20.0'];

export default function WhatsApp() {
  const toast = useToast();
  const { t } = useI18n();
  const { isReadOnly } = usePermission();
  const [searchParams, setSearchParams] = useSearchParams();
  const [activeTab, setActiveTab] = useState<TabType>(() => {
    const tabParam = searchParams.get('tab');
    if (tabParam === 'template' || tabParam === 'status' || tabParam === 'config') {
      return tabParam;
    }
    return 'config';
  });
  const [settings, setSettings] = useState<WhatsAppSettingsDto | null>(null);
  const [status, setStatus] = useState<WhatsAppStatusDto | null>(null);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [refreshLock, setRefreshLock] = useState(false);
  const isRefreshing = loading || refreshLock;

  // 敏感字段显示控制
  const [showAccessToken, setShowAccessToken] = useState(false);
  const [showAppSecret, setShowAppSecret] = useState(false);
  const [showVerifyToken, setShowVerifyToken] = useState(false);
  // 敏感字段编辑状态（空值表示不修改）
  const [editingAccessToken, setEditingAccessToken] = useState(false);
  const [editingAppSecret, setEditingAppSecret] = useState(false);
  const [editingVerifyToken, setEditingVerifyToken] = useState(false);

  // 配置表单状态
  const [form, setForm] = useState<UpdateWhatsAppSettingsRequest>({});

  const handleTabChange = useCallback((tab: TabType) => {
    setActiveTab(tab);
    setSearchParams(tab === 'config' ? {} : { tab }, { replace: true });
  }, [setSearchParams]);

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const [settingsData, statusData, mailboxData] = await Promise.all([
        whatsAppApi.getSettings(),
        whatsAppApi.getStatus(),
        mailboxApi.list(),
      ]);
      setSettings(settingsData);
      setStatus(statusData);
      setMailboxes(mailboxData);
      setForm({
        phoneNumberId: settingsData.phoneNumberId,
        businessPhoneNumber: settingsData.businessPhoneNumber,
        apiVersion: settingsData.apiVersion,
        mailboxId: settingsData.mailboxId,
        contactCollectionEnabled: settingsData.contactCollectionEnabled,
        autoReplyEnabled: settingsData.autoReplyEnabled,
        autoReplyMessage: settingsData.autoReplyMessage,
        enabled: settingsData.enabled,
      });
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : t('channels.common.loadFailed', { channel: t('format.sourceType.whatsapp') });
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
          whatsAppApi.getSettings(),
          whatsAppApi.getStatus(),
          mailboxApi.list(),
        ]);
        if (!destroyed) {
          setSettings(settingsData);
          setStatus(statusData);
          setMailboxes(mailboxData);
          setForm({
            phoneNumberId: settingsData.phoneNumberId,
            businessPhoneNumber: settingsData.businessPhoneNumber,
            apiVersion: settingsData.apiVersion,
            mailboxId: settingsData.mailboxId,
            contactCollectionEnabled: settingsData.contactCollectionEnabled,
            autoReplyEnabled: settingsData.autoReplyEnabled,
            autoReplyMessage: settingsData.autoReplyMessage,
            enabled: settingsData.enabled,
          });
        }
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : t('channels.common.loadFailed', { channel: t('format.sourceType.whatsapp') });
        toast.error(message);
      } finally {
        if (!destroyed) setLoading(false);
      }
    };
    void run();
    return () => { destroyed = true; };
  }, [toast, t]);

  const handleSave = async () => {
    if (!form.phoneNumberId?.trim()) {
      toast.error(t('channels.whatsapp.config.phoneNumberIdRequired'));
      return;
    }
    const payload: UpdateWhatsAppSettingsRequest = { ...form };
    // 仅在用户显式编辑了敏感字段时才提交新值
    if (!editingAccessToken) payload.accessToken = undefined;
    if (!editingAppSecret) payload.appSecret = undefined;
    if (!editingVerifyToken) payload.verifyToken = undefined;

    try {
      setSaving(true);
      const updated = await whatsAppApi.updateSettings(payload);
      setSettings(updated);
      // 清空敏感字段输入并退出编辑态
      setEditingAccessToken(false);
      setEditingAppSecret(false);
      setEditingVerifyToken(false);
      setForm((prev) => ({ ...prev, accessToken: undefined, appSecret: undefined, verifyToken: undefined }));
      toast.success(t('channels.common.saveSucceeded', { channel: t('format.sourceType.whatsapp') }));
      const newStatus = await whatsAppApi.getStatus();
      setStatus(newStatus);
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : t('channels.common.saveFailed', { channel: t('format.sourceType.whatsapp') });
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

  const handleTestConnection = async () => {
    setTesting(true);
    try {
      const result: WhatsAppTestResult = await whatsAppApi.testConnection();
      if (result.success) {
        toast.success(result.messageId
          ? t('channels.whatsapp.test.sentWithId', { id: result.messageId })
          : t('channels.whatsapp.test.sent'));
      } else {
        toast.error(result.errorMessage || t('channels.whatsapp.test.failed'));
      }
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : t('channels.common.testConnectionFailed');
      toast.error(message);
    } finally {
      setTesting(false);
    }
  };

  const configured = status?.configured && status?.enabled;

  if (loading && !settings) {
    return (
      <div className="max-w-4xl mx-auto p-4 sm:p-6">
        <div className="flex flex-col items-center justify-center min-h-[400px] gap-3">
          <div className="p-3 bg-gradient-to-br from-emerald-500 to-teal-600 rounded-2xl shadow-lg shadow-emerald-500/20">
            <MessageCircle className="w-7 h-7 text-white animate-pulse" />
          </div>
          <div className="flex items-center gap-2 text-gray-500 dark:text-slate-400">
            <Loader2 className="w-4 h-4 animate-spin" />
            <span className="text-sm">{t('channels.whatsapp.loadingConfig')}</span>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto p-4 sm:p-6 space-y-4 sm:space-y-6">
      {/* 页面标题 - 带渐变装饰背景 */}
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-emerald-500 via-green-500 to-teal-600 dark:from-emerald-700 dark:via-green-700 dark:to-teal-800 shadow-lg shadow-emerald-500/20">
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
              <h1 className="text-xl sm:text-2xl font-bold text-white truncate">{t('channels.whatsapp.header.title')}</h1>
              <p className="text-sm text-emerald-50/90 truncate">{t('channels.whatsapp.header.subtitle')}</p>
            </div>
          </div>
          <div className="flex items-center gap-2 flex-shrink-0">
            {configured && (
              <button
                onClick={() => void handleTestConnection()}
                disabled={testing}
                className="flex items-center gap-1.5 px-3 py-1.5 text-sm text-emerald-700 bg-white hover:bg-emerald-50 rounded-lg transition-colors disabled:opacity-50 shadow-sm font-medium"
              >
                {testing ? <Loader2 className="w-4 h-4 animate-spin" /> : <TestTube className="w-4 h-4" />}
                <span className="hidden sm:inline">{t('channels.common.testConnection')}</span>
              </button>
            )}
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
      </div>

      {/* 连接状态卡片 */}
      <div className={`rounded-xl border p-4 transition-all duration-300 ${
        configured
          ? 'bg-gradient-to-r from-emerald-50 to-teal-50/50 border-emerald-200 dark:from-emerald-900/20 dark:to-teal-900/10 dark:border-emerald-800/50 shadow-sm shadow-emerald-500/5'
          : status?.configured && !status?.enabled
          ? 'bg-gradient-to-r from-yellow-50 to-amber-50/50 border-yellow-200 dark:from-yellow-900/20 dark:to-amber-900/10 dark:border-yellow-800/50'
          : 'bg-gray-50 border-gray-200 dark:bg-slate-800 dark:border-slate-700'
      }`}>
        <div className="flex items-center gap-3 flex-wrap">
          {status?.configured && status?.enabled ? (
            <>
              <div className="flex-shrink-0 w-10 h-10 rounded-xl bg-emerald-100 dark:bg-emerald-900/40 flex items-center justify-center ring-2 ring-emerald-200 dark:ring-emerald-800/50">
                <CheckCircle2 className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
              </div>
              <div className="flex-1 min-w-0">
                <p className="font-semibold text-emerald-900 dark:text-emerald-200 flex items-center gap-1.5">
                  {t('channels.whatsapp.connection.connected')}
                  <span className="inline-block w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
                </p>
                <p className="text-sm text-emerald-700 dark:text-emerald-400/80">{t('channels.common.receivesMessages', { channel: t('format.sourceType.whatsapp') })}</p>
              </div>
              {status?.apiReachable && (
                <div className="flex items-center gap-1.5 px-3 py-1.5 bg-emerald-100 dark:bg-emerald-900/40 rounded-full flex-shrink-0 ring-1 ring-emerald-200 dark:ring-emerald-800/50">
                  <Cloud className="w-3.5 h-3.5 text-emerald-600 dark:text-emerald-400" />
                  <span className="text-xs font-medium text-emerald-700 dark:text-emerald-300">{t('channels.common.apiReachable')}</span>
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
                <p className="text-sm text-yellow-700 dark:text-yellow-400/80">{t('channels.whatsapp.connection.enableHint')}</p>
              </div>
            </>
          ) : (
            <>
              <div className="flex-shrink-0 w-10 h-10 rounded-xl bg-gray-100 dark:bg-slate-700 flex items-center justify-center ring-2 ring-gray-200 dark:ring-slate-600">
                <AlertCircle className="w-5 h-5 text-gray-500 dark:text-slate-400" />
              </div>
              <div className="flex-1 min-w-0">
                <p className="font-semibold text-gray-900 dark:text-slate-100">{t('channels.common.notConfigured')}</p>
                <p className="text-sm text-gray-600 dark:text-slate-400">{t('channels.whatsapp.connection.notConfiguredHint')}</p>
              </div>
            </>
          )}
        </div>
      </div>

      {/* 快速入门指南（未配置时显示） */}
      {!status?.configured && (
        <QuickStartGuide callbackUrl={settings?.callbackUrl} onCopyCallbackUrl={handleCopyCallbackUrl} />
      )}

      {/* 标签栏 */}
      <div className="border-b border-gray-200 dark:border-slate-700 -mx-4 px-4 sm:mx-0 sm:px-0 overflow-x-auto no-scrollbar">
        <nav className="flex gap-1 sm:gap-6 min-w-max">
          <button
            onClick={() => handleTabChange('config')}
            className={`pb-3 px-2 sm:px-0 text-sm font-medium border-b-2 transition-all whitespace-nowrap flex-shrink-0 flex items-center gap-1.5 ${
              activeTab === 'config'
                ? 'border-emerald-600 text-emerald-700 dark:text-emerald-400 dark:border-emerald-500'
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
                ? 'border-emerald-600 text-emerald-700 dark:text-emerald-400 dark:border-emerald-500'
                : 'border-transparent text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-200'
            }`}
          >
            <Send className="w-4 h-4" />
            {t('channels.common.templateMessage')}
          </button>
          <button
            onClick={() => handleTabChange('status')}
            disabled={!status?.configured}
            className={`pb-3 px-2 sm:px-0 text-sm font-medium border-b-2 transition-all whitespace-nowrap disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-1.5 ${
              activeTab === 'status'
                ? 'border-emerald-600 text-emerald-700 dark:text-emerald-400 dark:border-emerald-500'
                : 'border-transparent text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-200'
            }`}
          >
            <FileCheck className="w-4 h-4" />
            {t('channels.whatsapp.tab.status')}
          </button>
        </nav>
      </div>

      {/* Config Tab */}
      {activeTab === 'config' && settings && (
        <ConfigTab
          settings={settings}
          status={status}
          mailboxes={mailboxes}
          form={form}
          setForm={setForm}
          showAccessToken={showAccessToken}
          setShowAccessToken={setShowAccessToken}
          showAppSecret={showAppSecret}
          setShowAppSecret={setShowAppSecret}
          showVerifyToken={showVerifyToken}
          setShowVerifyToken={setShowVerifyToken}
          editingAccessToken={editingAccessToken}
          setEditingAccessToken={setEditingAccessToken}
          editingAppSecret={editingAppSecret}
          setEditingAppSecret={setEditingAppSecret}
          editingVerifyToken={editingVerifyToken}
          setEditingVerifyToken={setEditingVerifyToken}
          saving={saving}
          onSave={handleSave}
          onCopyCallbackUrl={handleCopyCallbackUrl}
          onTestConnection={handleTestConnection}
          testing={testing}
          isReadOnly={isReadOnly}
        />
      )}

      {/* Template Tab */}
      {activeTab === 'template' && configured && (
        <TemplateSendForm isReadOnly={isReadOnly} />
      )}

      {/* Status Tab */}
      {activeTab === 'status' && status?.configured && (
        <StatusPanel status={status} onTestConnection={handleTestConnection} testing={testing} />
      )}

      {/* FAQ 常见问题 */}
      <FAQSection />
    </div>
  );
}

// ====== 快速入门指南 ======
function QuickStartGuide({ callbackUrl, onCopyCallbackUrl }: { callbackUrl?: string | null; onCopyCallbackUrl: () => void }) {
  const { t } = useI18n();
  // 表里只放真源 key（渲染处一律经 t()）：字段名带 Key 后缀，漏包 t() 由 TS2339 抓出
  const steps: {
    num: number;
    titleKey: TranslationKey;
    descKey: TranslationKey;
    link?: string;
    linkTextKey?: TranslationKey;
  }[] = [
    {
      num: 1,
      titleKey: 'channels.whatsapp.guide.createApp.title',
      descKey: 'channels.whatsapp.guide.createApp.desc',
      link: 'https://developers.facebook.com/apps/',
      linkTextKey: 'channels.whatsapp.guide.metaForDevelopers',
    },
    {
      num: 2,
      titleKey: 'channels.whatsapp.guide.phoneNumberId.title',
      descKey: 'channels.whatsapp.guide.phoneNumberId.desc',
      link: 'https://developers.facebook.com/docs/whatsapp/cloud-api/get-started',
      linkTextKey: 'channels.whatsapp.guide.cloudApiGuide',
    },
    {
      num: 3,
      titleKey: 'channels.whatsapp.guide.webhook.title',
      descKey: 'channels.whatsapp.guide.webhook.desc',
      link: 'https://developers.facebook.com/docs/whatsapp/cloud-api/guides/set-up-webhooks',
      linkTextKey: 'channels.whatsapp.guide.webhookGuide',
    },
    {
      num: 4,
      titleKey: 'channels.whatsapp.guide.save.title',
      descKey: 'channels.whatsapp.guide.save.desc',
    },
  ];

  return (
    <section className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
      <div className="flex items-center gap-2 px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-emerald-50/80 to-white dark:from-emerald-900/20 dark:to-slate-800">
        <div className="p-1.5 rounded-lg bg-emerald-100 text-emerald-600 dark:bg-emerald-900/40 dark:text-emerald-400">
          <BookOpen className="w-4 h-4" />
        </div>
        <h2 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100">{t('channels.whatsapp.guide.title')}</h2>
        <span className="ml-auto text-xs text-gray-400 dark:text-slate-500">{t('channels.whatsapp.guide.subtitle')}</span>
      </div>
      <div className="p-4 sm:p-6">
        <div className="space-y-3">
          {steps.map((step) => (
            <div key={step.num} className="flex items-start gap-3 sm:gap-4">
              <div className="flex-shrink-0 w-7 h-7 sm:w-8 sm:h-8 rounded-full bg-gradient-to-br from-emerald-500 to-green-600 text-white text-xs sm:text-sm font-bold flex items-center justify-center shadow-sm">
                {step.num}
              </div>
              <div className="flex-1 min-w-0 pt-0.5">
                <h3 className="text-sm font-semibold text-gray-800 dark:text-slate-200">{t(step.titleKey)}</h3>
                <p className="text-xs sm:text-sm text-gray-500 dark:text-slate-400 mt-0.5">{t(step.descKey)}</p>
                {step.link && (
                  <a
                    href={step.link}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center gap-1 mt-1.5 text-xs text-emerald-600 dark:text-emerald-400 hover:underline"
                  >
                    {step.linkTextKey ? t(step.linkTextKey) : null}
                    <ExternalLink className="w-3 h-3" />
                  </a>
                )}
                {step.num === 3 && callbackUrl && (
                  <div className="mt-2 flex items-center gap-2 p-2 bg-gray-50 dark:bg-slate-700/50 rounded-lg">
                    <Globe className="w-3.5 h-3.5 text-gray-400 flex-shrink-0" />
                    <code className="text-xs text-gray-600 dark:text-slate-300 truncate font-mono flex-1">{callbackUrl}</code>
                    <button
                      onClick={onCopyCallbackUrl}
                      className="flex-shrink-0 p-1 text-gray-400 hover:text-emerald-600 dark:hover:text-emerald-400 transition-colors"
                      title={t('channels.common.copy')}
                    >
                      <Copy className="w-3.5 h-3.5" />
                    </button>
                  </div>
                )}
              </div>
              {step.num < steps.length && (
                <div className="hidden sm:flex flex-shrink-0 pt-2">
                  <ArrowRight className="w-4 h-4 text-gray-300 dark:text-slate-600" />
                </div>
              )}
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

// ====== 基础配置 Tab ======
interface ConfigTabProps {
  settings: WhatsAppSettingsDto;
  status: WhatsAppStatusDto | null;
  mailboxes: MailboxResponse[];
  form: UpdateWhatsAppSettingsRequest;
  setForm: (f: UpdateWhatsAppSettingsRequest) => void;
  showAccessToken: boolean;
  setShowAccessToken: (v: boolean) => void;
  showAppSecret: boolean;
  setShowAppSecret: (v: boolean) => void;
  showVerifyToken: boolean;
  setShowVerifyToken: (v: boolean) => void;
  editingAccessToken: boolean;
  setEditingAccessToken: (v: boolean) => void;
  editingAppSecret: boolean;
  setEditingAppSecret: (v: boolean) => void;
  editingVerifyToken: boolean;
  setEditingVerifyToken: (v: boolean) => void;
  saving: boolean;
  onSave: () => void;
  onCopyCallbackUrl: () => void;
  onTestConnection: () => void;
  testing: boolean;
  isReadOnly: boolean;
}

function ConfigTab(p: ConfigTabProps) {
  const {
    settings, mailboxes, form, setForm,
    showAccessToken, setShowAccessToken,
    showAppSecret, setShowAppSecret,
    showVerifyToken, setShowVerifyToken,
    editingAccessToken, setEditingAccessToken,
    editingAppSecret, setEditingAppSecret,
    editingVerifyToken, setEditingVerifyToken,
    saving, onSave, onCopyCallbackUrl, onTestConnection, testing, isReadOnly,
  } = p;
  const { t } = useI18n();

  return (
    <div className="space-y-4 sm:space-y-6">
      {/* 基本接入信息 */}
      <section className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        <div className="flex items-center gap-2 px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50/80 to-white dark:from-slate-800/80 dark:to-slate-800">
          <div className="p-1.5 rounded-lg bg-emerald-100 text-emerald-600 dark:bg-emerald-900/40 dark:text-emerald-400">
            <Smartphone className="w-4 h-4" />
          </div>
          <h2 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100">{t('channels.whatsapp.config.basicInfo')}</h2>
          <div className="ml-auto text-xs text-gray-400 dark:text-slate-500 hidden sm:flex items-center gap-1">
            <Info className="w-3 h-3" />
            {t('channels.whatsapp.config.basicInfoHint')}
          </div>
        </div>
        <div className="p-4 sm:p-6 grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
          <FormField icon={<Globe className="w-4 h-4 text-emerald-500" />} label="Phone Number ID" required hint={t('channels.whatsapp.config.phoneNumberIdHint')}>
            <input
              type="text"
              value={form.phoneNumberId ?? ''}
              onChange={(e) => setForm({ ...form, phoneNumberId: e.target.value })}
              disabled={isReadOnly || saving}
              placeholder={t('channels.whatsapp.config.phoneNumberIdPlaceholder')}
              className="form-input"
            />
          </FormField>
          <FormField icon={<Phone className="w-4 h-4 text-emerald-500" />} label={t('channels.whatsapp.config.businessPhone')} hint={t('channels.whatsapp.config.businessPhoneHint')}>
            <input
              type="tel"
              value={form.businessPhoneNumber ?? ''}
              onChange={(e) => setForm({ ...form, businessPhoneNumber: e.target.value })}
              disabled={isReadOnly || saving}
              placeholder="+8613800138000"
              className="form-input"
            />
          </FormField>
          <FormField icon={<Key className="w-4 h-4 text-emerald-500" />} label={t('channels.whatsapp.config.apiVersion')} hint={t('channels.whatsapp.config.apiVersionHint')}>
            <div className="relative">
              <input
                type="text"
                value={form.apiVersion ?? 'v23.0'}
                onChange={(e) => setForm({ ...form, apiVersion: e.target.value })}
                disabled={isReadOnly || saving}
                className="form-input"
                list="wa-api-versions"
              />
              <datalist id="wa-api-versions">
                {API_VERSIONS.map((v) => (
                  <option key={v} value={v} />
                ))}
              </datalist>
            </div>
          </FormField>
          <FormField icon={<Mailbox className="w-4 h-4 text-emerald-500" />} label={t('channels.common.linkedMailbox')}>
            <select
              value={form.mailboxId ?? ''}
              onChange={(e) => setForm({ ...form, mailboxId: e.target.value ? parseInt(e.target.value) : undefined })}
              disabled={isReadOnly || saving}
              className="form-input"
            >
              <option value="">{t('channels.whatsapp.config.noMailbox')}</option>
              {mailboxes.map((m) => (
                <option key={m.id} value={m.id}>#{m.id} {m.name} &lt;{m.email}&gt;</option>
              ))}
            </select>
          </FormField>
        </div>
      </section>

      {/* 鉴权配置 */}
      <section className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        <div className="flex items-center gap-2 px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50/80 to-white dark:from-slate-800/80 dark:to-slate-800">
          <div className="p-1.5 rounded-lg bg-indigo-100 text-indigo-600 dark:bg-indigo-900/40 dark:text-indigo-400">
            <Shield className="w-4 h-4" />
          </div>
          <h2 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100">{t('channels.whatsapp.auth.title')}</h2>
          <div className="ml-auto text-xs text-gray-400 dark:text-slate-500 hidden sm:flex items-center gap-1">
            <Lock className="w-3 h-3" />
            {t('channels.whatsapp.auth.encryptedHint')}
          </div>
        </div>
        <div className="p-4 sm:p-6 grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
          {/* Access Token */}
          <FormField icon={<Key className="w-4 h-4 text-indigo-500" />} label={t('channels.whatsapp.auth.accessTokenLabel')} required>
            <SecretInput
              value={editingAccessToken ? (form.accessToken ?? '') : ''}
              placeholder={settings.hasAccessToken && !editingAccessToken ? t('channels.whatsapp.auth.configuredPlaceholder') : t('channels.whatsapp.auth.accessTokenPlaceholder')}
              hasExisting={settings.hasAccessToken}
              editing={editingAccessToken}
              setEditing={setEditingAccessToken}
              show={showAccessToken}
              setShow={setShowAccessToken}
              onChange={(v) => setForm({ ...form, accessToken: v })}
              disabled={isReadOnly || saving}
            />
          </FormField>
          {/* App Secret */}
          <FormField icon={<Lock className="w-4 h-4 text-indigo-500" />} label={t('channels.whatsapp.auth.appSecretLabel')}>
            <SecretInput
              value={editingAppSecret ? (form.appSecret ?? '') : ''}
              placeholder={settings.hasAppSecret && !editingAppSecret ? t('channels.whatsapp.auth.configuredPlaceholder') : t('channels.whatsapp.auth.appSecretPlaceholder')}
              hasExisting={settings.hasAppSecret}
              editing={editingAppSecret}
              setEditing={setEditingAppSecret}
              show={showAppSecret}
              setShow={setShowAppSecret}
              onChange={(v) => setForm({ ...form, appSecret: v })}
              disabled={isReadOnly || saving}
            />
          </FormField>
          {/* Verify Token */}
          <div className="sm:col-span-2">
            <FormField icon={<Braces className="w-4 h-4 text-indigo-500" />} label={t('channels.whatsapp.auth.verifyTokenLabel')} hint={t('channels.whatsapp.auth.verifyTokenHint')}>
              <SecretInput
                value={editingVerifyToken ? (form.verifyToken ?? '') : ''}
                placeholder={settings.hasVerifyToken && !editingVerifyToken ? t('channels.whatsapp.auth.configuredPlaceholder') : t('channels.whatsapp.auth.verifyTokenPlaceholder')}
                hasExisting={settings.hasVerifyToken}
                editing={editingVerifyToken}
                setEditing={setEditingVerifyToken}
                show={showVerifyToken}
                setShow={setShowVerifyToken}
                onChange={(v) => setForm({ ...form, verifyToken: v })}
                disabled={isReadOnly || saving}
              />
            </FormField>
          </div>
        </div>
      </section>

      {/* 行为设置 */}
      <section className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        <div className="flex items-center gap-2 px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50/80 to-white dark:from-slate-800/80 dark:to-slate-800">
          <div className="p-1.5 rounded-lg bg-amber-100 text-amber-600 dark:bg-amber-900/40 dark:text-amber-400">
            <Bell className="w-4 h-4" />
          </div>
          <h2 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100">{t('channels.whatsapp.behavior.title')}</h2>
        </div>
        <div className="p-4 sm:p-6 space-y-4">
          <ToggleSwitch
            checked={form.contactCollectionEnabled ?? false}
            onChange={(v) => setForm({ ...form, contactCollectionEnabled: v })}
            disabled={isReadOnly || saving}
            title={t('channels.whatsapp.behavior.contactCollection')}
            description={t('channels.whatsapp.behavior.contactCollectionDesc')}
          />
          <ToggleSwitch
            checked={form.autoReplyEnabled ?? false}
            onChange={(v) => setForm({ ...form, autoReplyEnabled: v })}
            disabled={isReadOnly || saving}
            icon={<Sparkles className="w-4 h-4 text-amber-500" />}
            title={t('channels.whatsapp.behavior.autoReplyTitle')}
            description={t('channels.whatsapp.behavior.autoReplyDesc')}
          />
          {form.autoReplyEnabled && (
            <div className="pl-10 space-y-2">
              <textarea
                value={form.autoReplyMessage ?? ''}
                onChange={(e) => setForm({ ...form, autoReplyMessage: e.target.value })}
                disabled={isReadOnly || saving}
                placeholder={t('channels.whatsapp.behavior.autoReplyPlaceholder')}
                rows={2}
                className="form-input resize-none"
              />
            </div>
          )}
          <ToggleSwitch
            checked={form.enabled ?? false}
            onChange={(v) => setForm({ ...form, enabled: v })}
            disabled={isReadOnly || saving}
            icon={<Cloud className="w-4 h-4 text-emerald-500" />}
            title={t('channels.common.enableChannel', { channel: t('format.sourceType.whatsapp') })}
            description={t('channels.whatsapp.behavior.enableHint')}
          />
        </div>
      </section>

      {/* 回调 URL 区域 */}
      <section className="bg-gradient-to-br from-emerald-50 to-teal-50 dark:from-emerald-900/20 dark:to-teal-900/10 rounded-xl border border-emerald-200 dark:border-emerald-800/40 shadow-sm overflow-hidden">
        <div className="flex items-center gap-2 px-4 sm:px-6 py-3 sm:py-4 border-b border-emerald-100 dark:border-emerald-800/30">
          <div className="p-1.5 rounded-lg bg-white dark:bg-slate-800 text-emerald-600 dark:text-emerald-400 shadow-sm">
            <ExternalLink className="w-4 h-4" />
          </div>
          <h2 className="text-sm sm:text-base font-semibold text-emerald-900 dark:text-emerald-200">{t('channels.whatsapp.webhook.title')}</h2>
          <div className="ml-auto text-xs text-emerald-600 dark:text-emerald-400 flex items-center gap-1">
            <Info className="w-3 h-3" />
            {t('channels.whatsapp.webhook.requiredHint')}
          </div>
        </div>
        <div className="p-4 sm:p-6 space-y-3">
          <div className="flex flex-col sm:flex-row sm:items-stretch gap-3">
            <div className="flex-1 bg-white dark:bg-slate-800 border border-emerald-200 dark:border-emerald-800/40 rounded-lg px-3 py-2.5 flex items-center min-w-0 focus-within:ring-2 focus-within:ring-emerald-400/40">
              <Globe className="w-4 h-4 text-emerald-500 flex-shrink-0" />
              <code className="ml-2 text-xs sm:text-sm text-gray-800 dark:text-slate-200 truncate font-mono">
                {settings.callbackUrl}
              </code>
            </div>
            <button
              type="button"
              onClick={onCopyCallbackUrl}
              disabled={isReadOnly}
              className="inline-flex items-center justify-center gap-1.5 px-4 py-2.5 bg-white dark:bg-slate-800 hover:bg-emerald-50 dark:hover:bg-emerald-900/30 border border-emerald-200 dark:border-emerald-800/40 text-emerald-700 dark:text-emerald-300 rounded-lg font-medium text-sm transition-all disabled:opacity-40 shadow-sm hover:shadow active:scale-[0.98]"
            >
              <Copy className="w-4 h-4" />
              {t('channels.whatsapp.webhook.copyUrl')}
            </button>
          </div>
          <div className="grid sm:grid-cols-2 gap-3 text-xs text-emerald-800 dark:text-emerald-300/90">
            <div className="flex items-start gap-1.5 p-2.5 bg-white/60 dark:bg-slate-800/40 rounded-lg">
              <CheckCircle2 className="w-3.5 h-3.5 mt-0.5 flex-shrink-0 text-emerald-500" />
              <div>
                <div className="font-semibold mb-0.5">{t('channels.whatsapp.webhook.fieldTitle')}</div>
                <div className="opacity-90">{t('channels.whatsapp.webhook.fieldMessages')}</div>
              </div>
            </div>
            <div className="flex items-start gap-1.5 p-2.5 bg-white/60 dark:bg-slate-800/40 rounded-lg">
              <CheckCircle2 className="w-3.5 h-3.5 mt-0.5 flex-shrink-0 text-emerald-500" />
              <div>
                <div className="font-semibold mb-0.5">Verify Token</div>
                <div className="opacity-90">{t('channels.whatsapp.webhook.verifyTokenHint')}</div>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* 保存按钮区 */}
      <div className="sticky bottom-0 -mx-4 sm:mx-0 px-4 sm:px-0 py-3 bg-gradient-to-t from-white via-white to-transparent dark:from-slate-900 dark:via-slate-900 dark:to-transparent backdrop-blur-sm pointer-events-none">
        <div className="flex flex-col sm:flex-row sm:justify-end gap-2 sm:gap-3 pointer-events-auto">
          {settings.hasAccessToken && (
            <button
              type="button"
              onClick={onTestConnection}
              disabled={testing || saving || isReadOnly}
              className="inline-flex items-center justify-center gap-2 w-full sm:w-auto px-4 py-2 text-xs sm:text-sm font-medium text-emerald-700 dark:text-emerald-300 bg-emerald-50 dark:bg-emerald-900/20 hover:bg-emerald-100 dark:hover:bg-emerald-900/40 border border-emerald-200 dark:border-emerald-800/40 rounded-lg transition-all disabled:opacity-40 active:scale-[0.98]"
            >
              {testing ? <Loader2 className="w-4 h-4 animate-spin" /> : <Zap className="w-4 h-4" />}
              {testing ? t('channels.common.testing') : t('channels.common.testConnection')}
            </button>
          )}
          <button
            type="button"
            onClick={() => window.location.reload()}
            disabled={saving}
            className="w-full sm:w-auto px-4 py-2 text-xs sm:text-sm font-medium text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-800 rounded-lg transition-colors disabled:opacity-40 border border-gray-200 dark:border-slate-700"
          >
            <Eraser className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            {t('common.reset')}
          </button>
          <button
            type="button"
            onClick={onSave}
            disabled={saving || isReadOnly}
            className="inline-flex items-center justify-center gap-2 w-full sm:w-auto px-5 sm:px-6 py-2 bg-gradient-to-r from-emerald-600 to-green-600 hover:from-emerald-700 hover:to-green-700 disabled:from-gray-300 dark:disabled:from-slate-700 dark:disabled:to-slate-700 disabled:cursor-not-allowed text-white rounded-lg text-xs sm:text-sm font-semibold shadow-md shadow-emerald-500/20 hover:shadow-lg hover:shadow-emerald-500/30 active:scale-[0.98] transition-all disabled:shadow-none"
          >
            {saving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
            {saving ? t('channels.whatsapp.actions.saving') : t('channels.common.saveConfig')}
          </button>
        </div>
      </div>
    </div>
  );
}

// ====== 模板消息 Tab ======
function TemplateSendForm({ isReadOnly }: { isReadOnly: boolean }) {
  const { t } = useI18n();
  const [conversationId, setConversationId] = useState('');
  const [templateName, setTemplateName] = useState('');
  const [language, setLanguage] = useState('zh_CN');
  const [componentsJson, setComponentsJson] = useState('');
  const [sending, setSending] = useState(false);
  const [result, setResult] = useState<string | null>(null);
  const toast = useToast();

  const handleSend = async () => {
    const id = parseInt(conversationId);
    if (!Number.isFinite(id) || id <= 0) {
      toast.error(t('channels.whatsapp.template.invalidConversationId'));
      return;
    }
    if (!templateName.trim()) {
      toast.error(t('channels.whatsapp.template.nameRequired'));
      return;
    }
    setSending(true);
    setResult(null);
    try {
      const res = await whatsAppApi.sendTemplate({
        conversationId: id,
        templateName: templateName.trim(),
        language,
        componentsJson: componentsJson || undefined,
      });
      if (res.sent) {
        setResult('✓ ' + (res.messageId
          ? t('channels.whatsapp.template.sentWithId', { id: res.messageId })
          : t('channels.whatsapp.template.sent')));
        toast.success(t('channels.common.templateSent'));
      } else {
        setResult(t('channels.whatsapp.template.failedPrefix') + (res.errorMessage || t('channels.whatsapp.template.unknownError')));
        toast.error(res.errorMessage || t('channels.whatsapp.template.failed'));
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : t('channels.whatsapp.template.failed');
      setResult('✗ ' + msg);
      toast.error(msg);
    } finally {
      setSending(false);
    }
  };

  // 模板示例里的样例人名是面向用户的示例文案、随界面语言变化，故经词典注入 JSON
  const exampleName = t('channels.whatsapp.template.exampleName');
  const exampleAppointmentName = t('channels.whatsapp.template.exampleAppointmentName');
  // 模板示例
  const templateExamples = [
    { name: 'welcome_message', lang: 'zh_CN', components: `[\n  {"type": "body", "parameters": [\n    {"type": "text", "text": "${exampleName}"}\n  ]}\n]` },
    { name: 'order_confirmation', lang: 'zh_CN', components: '[\n  {"type": "body", "parameters": [\n    {"type": "text", "text": "ORD-2026-001"},\n    {"type": "text", "text": "2026-08-05"}\n  ]}\n]' },
    { name: 'appointment_reminder', lang: 'zh_CN', components: `[\n  {"type": "body", "parameters": [\n    {"type": "text", "text": "${exampleAppointmentName}"},\n    {"type": "text", "text": "2026-08-10 14:00"}\n  ]}\n]` },
  ];

  const applyExample = (ex: typeof templateExamples[0]) => {
    setTemplateName(ex.name);
    setLanguage(ex.lang);
    setComponentsJson(ex.components);
  };

  return (
    <div className="space-y-4 sm:space-y-6">
      <section className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        <div className="flex items-center gap-2 px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50/80 to-white dark:from-slate-800/80 dark:to-slate-800">
          <div className="p-1.5 rounded-lg bg-purple-100 text-purple-600 dark:bg-purple-900/40 dark:text-purple-400">
            <Send className="w-4 h-4" />
          </div>
          <h2 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100">{t('channels.common.sendTemplateMessage')}</h2>
          <div className="ml-auto text-xs text-gray-400 dark:text-slate-500 hidden sm:flex items-center gap-1">
            <Info className="w-3 h-3" />
            {t('channels.whatsapp.template.windowFreeHint')}
          </div>
        </div>
        <div className="p-4 sm:p-6 space-y-4 sm:space-y-5">
          <div className="flex items-start gap-2 p-3 bg-purple-50 dark:bg-purple-900/10 rounded-lg border border-purple-100 dark:border-purple-800/30">
            <Info className="w-4 h-4 text-purple-500 flex-shrink-0 mt-0.5" />
            <p className="text-xs sm:text-sm text-purple-700 dark:text-purple-300/90">
              {t('channels.whatsapp.template.descPrefix')}{' '}
              <a href="https://business.facebook.com/wa/manage/message-templates/" target="_blank" rel="noopener noreferrer" className="underline font-medium inline-flex items-center gap-0.5">
                Meta Message Templates <ExternalLink className="w-3 h-3" />
              </a>
              {' '}{t('channels.whatsapp.template.descSuffix')}
            </p>
          </div>

          {/* 快速填充示例 */}
          <div>
            <div className="text-xs font-medium text-gray-500 dark:text-slate-400 mb-2 flex items-center gap-1">
              <Sparkles className="w-3 h-3" />
              {t('channels.whatsapp.template.quickFill')}
            </div>
            <div className="flex flex-wrap gap-2">
              {templateExamples.map((ex) => (
                <button
                  key={ex.name}
                  type="button"
                  onClick={() => applyExample(ex)}
                  disabled={isReadOnly || sending}
                  className="px-3 py-1.5 text-xs font-mono bg-gray-100 dark:bg-slate-700 hover:bg-purple-50 dark:hover:bg-purple-900/30 text-gray-600 dark:text-slate-300 hover:text-purple-700 dark:hover:text-purple-300 rounded-lg border border-gray-200 dark:border-slate-600 hover:border-purple-200 dark:hover:border-purple-800/40 transition-all disabled:opacity-40"
                >
                  {ex.name}
                </button>
              ))}
            </div>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
            <FormField icon={<FileText className="w-4 h-4 text-purple-500" />} label={t('channels.whatsapp.template.conversationId')} required hint={t('channels.whatsapp.template.conversationIdHint')}>
              <input
                type="number"
                value={conversationId}
                onChange={(e) => setConversationId(e.target.value)}
                disabled={isReadOnly || sending}
                placeholder={t('channels.whatsapp.template.conversationIdPlaceholder')}
                className="form-input"
              />
            </FormField>
            <FormField icon={<FileText className="w-4 h-4 text-purple-500" />} label={t('channels.whatsapp.template.name')} required hint={t('channels.whatsapp.template.nameHint')}>
              <input
                type="text"
                value={templateName}
                onChange={(e) => setTemplateName(e.target.value)}
                disabled={isReadOnly || sending}
                placeholder={t('channels.whatsapp.template.namePlaceholder')}
                className="form-input"
              />
            </FormField>
            <FormField icon={<Globe className="w-4 h-4 text-purple-500" />} label={t('channels.whatsapp.template.languageCode')} hint={t('channels.whatsapp.template.languageCodeHint')}>
              <select
                value={language}
                onChange={(e) => setLanguage(e.target.value)}
                disabled={isReadOnly || sending}
                className="form-input"
              >
                {TEMPLATE_LANGUAGES.map((lang) => (
                  <option key={lang.code} value={lang.code}>
                    {lang.label} ({lang.code})
                  </option>
                ))}
              </select>
            </FormField>
          </div>
          <FormField icon={<Braces className="w-4 h-4 text-purple-500" />} label={t('channels.whatsapp.template.components')} hint={t('channels.whatsapp.template.componentsHint')}>
            <textarea
              value={componentsJson}
              onChange={(e) => setComponentsJson(e.target.value)}
              disabled={isReadOnly || sending}
              placeholder={`[\n  {"type": "body", "parameters": [\n    { "type": "text", "text": "${exampleName}" },\n    { "type": "text", "text": "2026-08-01" }\n  ]}\n]`}
              rows={5}
              className="form-input resize-y font-mono text-xs leading-relaxed"
            />
          </FormField>
          <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
            <p className="text-xs text-gray-400 dark:text-slate-500 flex items-center gap-1">
              <Info className="w-3 h-3" />
              {t('channels.whatsapp.template.threadHint')}
            </p>
            <button
              type="button"
              onClick={handleSend}
              disabled={sending || isReadOnly || !conversationId || !templateName}
              className="inline-flex items-center justify-center gap-2 w-full sm:w-auto px-5 py-2.5 bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-700 hover:to-indigo-700 disabled:from-gray-300 dark:disabled:from-slate-700 dark:disabled:to-slate-700 disabled:cursor-not-allowed text-white rounded-lg text-xs sm:text-sm font-semibold shadow-md shadow-purple-500/20 hover:shadow-lg hover:shadow-purple-500/30 active:scale-[0.98] transition-all disabled:shadow-none"
            >
              {sending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
              {sending ? t('channels.common.sending') : t('channels.whatsapp.template.sendNow')}
            </button>
          </div>
          {result && (
            <div className={`flex items-start gap-2 px-3 py-2.5 rounded-lg text-xs sm:text-sm border ${
              result.startsWith('✓')
                ? 'bg-emerald-50 text-emerald-700 border-emerald-200 dark:bg-emerald-900/20 dark:text-emerald-300 dark:border-emerald-800/40'
                : 'bg-red-50 text-red-700 border-red-200 dark:bg-red-900/20 dark:text-red-300 dark:border-red-800/40'
            }`}>
              {result.startsWith('✓')
                ? <CheckCircle2 className="w-4 h-4 flex-shrink-0 mt-0.5" />
                : <AlertCircle className="w-4 h-4 flex-shrink-0 mt-0.5" />}
              <span className="flex-1">{result.replace(/^[✓✗]\s*/, '')}</span>
            </div>
          )}
        </div>
      </section>
    </div>
  );
}

// ====== 状态面板 Tab ======
function StatusPanel({ status, onTestConnection, testing }: { status: WhatsAppStatusDto; onTestConnection: () => void; testing: boolean }) {
  const { t } = useI18n();
  // 表里只放真源 key（渲染处一律经 t()）：字段名带 Key 后缀，漏包 t() 由 TS2339 抓出
  const items: {
    labelKey: TranslationKey;
    icon: React.ReactNode;
    valueKey: TranslationKey;
    ok: boolean;
    hintKey: TranslationKey;
  }[] = [
    {
      labelKey: 'channels.whatsapp.status.integrity',
      icon: <FileCheck className="w-4 h-4" />,
      valueKey: status.configured ? 'channels.whatsapp.status.integrityOk' : 'channels.whatsapp.status.integrityMissing',
      ok: status.configured,
      hintKey: status.configured ? 'channels.whatsapp.status.integrityOkHint' : 'channels.whatsapp.status.integrityMissingHint',
    },
    {
      labelKey: 'channels.whatsapp.status.moduleSwitch',
      icon: <Cloud className="w-4 h-4" />,
      valueKey: status.enabled ? 'channels.whatsapp.status.enabled' : 'channels.whatsapp.status.disabled',
      ok: status.enabled,
      hintKey: status.enabled ? 'channels.whatsapp.status.enabledHint' : 'channels.whatsapp.status.disabledHint',
    },
    {
      labelKey: 'channels.whatsapp.status.apiReachable',
      icon: <Globe className="w-4 h-4" />,
      valueKey: status.apiReachable ? 'channels.whatsapp.status.apiOk' : 'channels.whatsapp.status.apiDown',
      ok: status.apiReachable,
      hintKey: status.apiReachable ? 'channels.whatsapp.status.apiOkHint' : 'channels.whatsapp.status.apiDownHint',
    },
  ];

  return (
    <section className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
      <div className="flex items-center gap-2 px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50/80 to-white dark:from-slate-800/80 dark:to-slate-800">
        <div className="p-1.5 rounded-lg bg-cyan-100 text-cyan-600 dark:bg-cyan-900/40 dark:text-cyan-400">
          <FileCheck className="w-4 h-4" />
        </div>
        <h2 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100">{t('channels.whatsapp.status.overviewTitle')}</h2>
        <div className="ml-auto flex items-center gap-2">
          <button
            onClick={onTestConnection}
            disabled={testing}
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-emerald-700 dark:text-emerald-300 bg-emerald-50 dark:bg-emerald-900/20 hover:bg-emerald-100 dark:hover:bg-emerald-900/40 border border-emerald-200 dark:border-emerald-800/40 rounded-lg transition-colors disabled:opacity-50"
          >
            {testing ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Zap className="w-3.5 h-3.5" />}
            {t('channels.common.testConnection')}
          </button>
          <span className="text-xs text-gray-400 dark:text-slate-500 hidden sm:inline">{t('channels.whatsapp.status.liveData')}</span>
        </div>
      </div>
      <div className="p-4 sm:p-6 grid grid-cols-2 sm:grid-cols-3 gap-3 sm:gap-4">
        {items.map((it) => (
          <div
            key={it.labelKey}
            className={`relative rounded-xl p-4 sm:p-6 border transition-all ${
              it.ok
                ? 'bg-gradient-to-br from-emerald-50 to-white dark:from-emerald-900/10 dark:to-slate-800 border-emerald-200 dark:border-emerald-800/40'
                : 'bg-gradient-to-br from-gray-50 to-white dark:from-slate-800/50 dark:to-slate-800 border-gray-200 dark:border-slate-700'
            }`}
          >
            <div className={`w-10 h-10 rounded-xl flex items-center justify-center mb-3 ${
              it.ok
                ? 'bg-emerald-100 text-emerald-600 dark:bg-emerald-900/40 dark:text-emerald-400 ring-2 ring-emerald-200 dark:ring-emerald-800/40'
                : 'bg-gray-100 text-gray-500 dark:bg-slate-700 dark:text-slate-400 ring-2 ring-gray-200 dark:ring-slate-600'
            }`}>
              {it.icon}
            </div>
            <div className="text-xs sm:text-sm text-gray-500 dark:text-slate-400 mb-1">{t(it.labelKey)}</div>
            <div className={`text-lg sm:text-xl font-bold mb-2 ${
              it.ok ? 'text-emerald-700 dark:text-emerald-300' : 'text-gray-500 dark:text-slate-400'
            }`}>
              {it.ok ? <span className="flex items-center gap-1.5">{t(it.valueKey)}<CheckCircle2 className="w-4 h-4" /></span> : t(it.valueKey)}
            </div>
            <p className="text-xs text-gray-400 dark:text-slate-500 leading-relaxed">{t(it.hintKey)}</p>
          </div>
        ))}
      </div>
      {/* 24h 窗口详情 */}
      <div className="px-4 sm:px-6 pb-5 sm:pb-6">
        <div className={`rounded-xl border overflow-hidden ${
          status.windowStatus === 'ok'
            ? 'bg-emerald-50 border-emerald-200 dark:bg-emerald-900/10 dark:border-emerald-800/40'
            : status.windowStatus === 'warning'
            ? 'bg-amber-50 border-amber-200 dark:bg-amber-900/10 dark:border-amber-800/40'
            : status.windowStatus === 'expired'
            ? 'bg-red-50 border-red-200 dark:bg-red-900/10 dark:border-red-800/40'
            : 'bg-gray-50 border-gray-200 dark:bg-slate-800/50 dark:border-slate-700'
        }`}>
          <div className="px-4 sm:px-6 py-3 sm:py-4 border-b border-white/40 dark:border-slate-700/40 flex items-center justify-between gap-3 flex-wrap">
            <div className="flex items-center gap-2">
              <Clock className="w-4 h-4 text-gray-500 dark:text-slate-400" />
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('channels.whatsapp.window.title')}</h3>
            </div>
            <WindowStatusBadge status={status.windowStatus} hours={status.windowRemainingHours} />
          </div>
          <div className="px-4 sm:px-6 py-3 sm:py-4 space-y-3">
            {status.windowStatus !== 'unknown' ? (
              <>
                <div>
                  <div className="sr-only">{t('channels.whatsapp.window.progressLabel', { percent: status.windowStatus === 'expired' ? 100 : Math.min(100, Math.max(0, 100 - status.windowRemainingHours / 24 * 100)) })}</div>
                  <div
                    className="h-2 rounded-full overflow-hidden bg-white/70 dark:bg-slate-900/40"
                    role="progressbar"
                    aria-valuenow={status.windowRemainingHours}
                    aria-valuemin={0}
                    aria-valuemax={24}
                    aria-label={t('channels.whatsapp.window.progressAria', { hours: status.windowRemainingHours })}
                  >
                    <div
                      className={`h-full transition-all duration-700 ease-out rounded-full ${
                        status.windowStatus === 'ok' ? 'bg-gradient-to-r from-emerald-400 to-green-500'
                          : status.windowStatus === 'warning' ? 'bg-gradient-to-r from-amber-400 to-orange-500'
                          : 'bg-gradient-to-r from-red-400 to-rose-500'
                      }`}
                      style={{
                        width: `${status.windowStatus === 'expired' ? 100 : Math.min(100, Math.max(0, 100 - status.windowRemainingHours / 24 * 100))}%`,
                      }}
                    />
                  </div>
                  <div className="flex justify-between mt-1.5 text-[10px] text-gray-500 dark:text-slate-500">
                    <span>0h</span>
                    <span>{
                      status.windowStatus === 'expired'
                        ? t('channels.whatsapp.window.expired')
                        : t('channels.whatsapp.window.usage', { used: Math.max(0, 24 - status.windowRemainingHours), remaining: status.windowRemainingHours })
                    }</span>
                    <span>24h</span>
                  </div>
                </div>
                {status.lastCustomerMessageAt && (
                  <div className="flex items-center gap-2 text-xs text-gray-500 dark:text-slate-400">
                    <Clock className="w-3 h-3" />
                    <span>{t('channels.whatsapp.window.lastInteraction', { time: formatDateTime(status.lastCustomerMessageAt) })}</span>
                  </div>
                )}
              </>
            ) : (
              <p className="text-sm text-gray-500 dark:text-slate-400 py-2 flex items-center gap-2">
                <Info className="w-4 h-4" />
                {t('channels.whatsapp.window.empty')}
              </p>
            )}
          </div>
        </div>
      </div>
    </section>
  );
}

// ====== FAQ 常见问题 ======
function FAQSection() {
  const { t } = useI18n();
  const [openIndex, setOpenIndex] = useState<number | null>(0);
  // 表里只放真源 key（渲染处一律经 t()）：字段名带 Key 后缀，漏包 t() 由 TS2339 抓出
  const faqs: { questionKey: TranslationKey; answerKey: TranslationKey }[] = [
    {
      questionKey: 'channels.whatsapp.faq.accessToken.question',
      answerKey: 'channels.whatsapp.faq.accessToken.answer',
    },
    {
      questionKey: 'channels.whatsapp.faq.webhookVerification.question',
      answerKey: 'channels.whatsapp.faq.webhookVerification.answer',
    },
    {
      questionKey: 'channels.whatsapp.faq.windowExpired.question',
      answerKey: 'channels.whatsapp.faq.windowExpired.answer',
    },
    {
      questionKey: 'channels.whatsapp.faq.mediaFiles.question',
      answerKey: 'channels.whatsapp.faq.mediaFiles.answer',
    },
    {
      questionKey: 'channels.whatsapp.faq.apiUnreachable.question',
      answerKey: 'channels.whatsapp.faq.apiUnreachable.answer',
    },
    {
      questionKey: 'channels.whatsapp.faq.messageTemplates.question',
      answerKey: 'channels.whatsapp.faq.messageTemplates.answer',
    },
  ];

  return (
    <section className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
      <div className="flex items-center gap-2 px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50/80 to-white dark:from-slate-800/80 dark:to-slate-800">
        <div className="p-1.5 rounded-lg bg-slate-100 text-slate-600 dark:bg-slate-700 dark:text-slate-300">
          <HelpCircle className="w-4 h-4" />
        </div>
        <h2 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100">{t('channels.whatsapp.faq.title')}</h2>
      </div>
      <div className="divide-y divide-gray-100 dark:divide-slate-700/60">
        {faqs.map((faq, i) => (
          <div key={i}>
            <button
              type="button"
              onClick={() => setOpenIndex(openIndex === i ? null : i)}
              className="w-full flex items-center justify-between gap-3 px-4 sm:px-5 py-3 text-left hover:bg-gray-50 dark:hover:bg-slate-700/30 transition-colors"
            >
              <span className="text-sm font-medium text-gray-700 dark:text-slate-200">{t(faq.questionKey)}</span>
              {openIndex === i
                ? <ChevronDown className="w-4 h-4 text-gray-400 flex-shrink-0" />
                : <ChevronRight className="w-4 h-4 text-gray-400 flex-shrink-0" />}
            </button>
            {openIndex === i && (
              <div className="px-4 sm:px-5 pb-3">
                <p className="text-sm text-gray-500 dark:text-slate-400 leading-relaxed">{t(faq.answerKey)}</p>
              </div>
            )}
          </div>
        ))}
      </div>
    </section>
  );
}

// ====== 通用组件 ======
function WindowStatusBadge({ status, hours }: { status: string; hours: number }) {
  const { t } = useI18n();
  if (status === 'ok') {
    return (
      <span className="inline-flex items-center gap-1 text-[11px] px-2.5 py-1 rounded-full font-semibold bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300 ring-1 ring-emerald-200 dark:ring-emerald-800/40">
        <CheckCircle2 className="w-3 h-3" /> {t('channels.whatsapp.window.badgeOk', { hours })}
      </span>
    );
  }
  if (status === 'warning') {
    return (
      <span className="inline-flex items-center gap-1 text-[11px] px-2.5 py-1 rounded-full font-semibold bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300 ring-1 ring-amber-200 dark:ring-amber-800/40">
        <AlertCircle className="w-3 h-3" /> {t('channels.whatsapp.window.badgeWarning', { hours })}
      </span>
    );
  }
  if (status === 'expired') {
    return (
      <span className="inline-flex items-center gap-1 text-[11px] px-2.5 py-1 rounded-full font-semibold bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300 ring-1 ring-red-200 dark:ring-red-800/40">
        <AlertCircle className="w-3 h-3" /> {t('channels.whatsapp.window.badgeExpired')}
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 text-[11px] px-2.5 py-1 rounded-full font-medium bg-gray-100 text-gray-500 dark:bg-slate-700 dark:text-slate-400 ring-1 ring-gray-200 dark:ring-slate-600">
      <Info className="w-3 h-3" /> {t('channels.whatsapp.window.badgeUnknown')}
    </span>
  );
}

function Mailbox({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className={className}>
      <path d="M22 17a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V9.5C2 7 4 5 6.5 5H18c2.2 0 4 1.8 4 4v8Z"/>
      <polyline points="15,9 18,9 18,11"/>
      <path d="M6 10v4M10 10v4M14 10v4"/>
    </svg>
  );
}

interface FormFieldProps {
  icon?: React.ReactNode;
  label: string;
  required?: boolean;
  hint?: string;
  children: React.ReactNode;
}

function FormField({ icon, label, required, hint, children }: FormFieldProps) {
  return (
    <label className="block space-y-1.5">
      <div className="flex items-center gap-1.5 text-xs sm:text-sm font-medium text-gray-700 dark:text-slate-300">
        {icon && <span className="inline-flex">{icon}</span>}
        <span>{label}</span>
        {required && <span className="text-red-500 font-semibold">*</span>}
      </div>
      {children}
      {hint && (
        <p className="text-[11px] text-gray-400 dark:text-slate-500 leading-relaxed">{hint}</p>
      )}
    </label>
  );
}

interface SecretInputProps {
  value: string;
  placeholder: string;
  hasExisting: boolean;
  editing: boolean;
  setEditing: (v: boolean) => void;
  show: boolean;
  setShow: (v: boolean) => void;
  onChange: (v: string) => void;
  disabled?: boolean;
}

function SecretInput({ value, placeholder, hasExisting, editing, setEditing, show, setShow, onChange, disabled }: SecretInputProps) {
  const { t } = useI18n();
  const EyeComp = show ? EyeOff : Eye;
  const ToggleComp = editing ? Lock : Unlock;
  return (
    <div className="space-y-1.5">
      <div className="flex items-stretch gap-1.5">
        <div className="flex-1 relative">
          <input
            type={show ? 'text' : 'password'}
            value={value}
            placeholder={placeholder}
            onChange={(e) => onChange(e.target.value)}
            disabled={disabled || (hasExisting && !editing)}
            className="form-input pr-20 disabled:cursor-not-allowed disabled:bg-gray-50 dark:disabled:bg-slate-800/60"
            readOnly={hasExisting && !editing}
          />
          <button
            type="button"
            onClick={() => setShow(!show)}
            disabled={disabled || !editing}
            tabIndex={-1}
            className="absolute right-1 top-1/2 -translate-y-1/2 p-1.5 text-gray-400 hover:text-gray-600 dark:hover:text-slate-300 rounded-md disabled:opacity-30 transition-colors"
            title={show ? t('channels.common.hide') : t('channels.common.show')}
            aria-label={show ? t('channels.whatsapp.secret.hideAria') : t('channels.whatsapp.secret.showAria')}
          >
            <EyeComp className="w-4 h-4" />
          </button>
        </div>
        {hasExisting && (
          <button
            type="button"
            onClick={() => {
              setEditing(!editing);
              setShow(editing ? false : show);
            }}
            disabled={disabled}
            className={`inline-flex items-center justify-center gap-1 px-3 rounded-lg text-xs font-medium border transition-colors disabled:opacity-40 ${
              editing
                ? 'bg-indigo-50 text-indigo-700 border-indigo-200 hover:bg-indigo-100 dark:bg-indigo-900/20 dark:text-indigo-300 dark:border-indigo-800/40'
                : 'bg-white dark:bg-slate-800 text-gray-600 dark:text-slate-300 border-gray-200 dark:border-slate-600 hover:bg-gray-50 dark:hover:bg-slate-700'
            }`}
          >
            <ToggleComp className="w-3.5 h-3.5" />
            <span className="hidden sm:inline">{editing ? t('channels.common.lockSecret') : t('channels.common.editSecret')}</span>
          </button>
        )}
      </div>
      {hasExisting && !editing && (
        <p className="text-[11px] text-emerald-600 dark:text-emerald-400 flex items-center gap-1">
          <CheckCircle2 className="w-3 h-3" />
          {t('channels.whatsapp.auth.storedHint')}
        </p>
      )}
    </div>
  );
}

interface ToggleSwitchProps {
  checked: boolean;
  onChange: (v: boolean) => void;
  disabled?: boolean;
  icon?: React.ReactNode;
  title: string;
  description?: string;
}

function ToggleSwitch({ checked, onChange, disabled, icon, title, description }: ToggleSwitchProps) {
  return (
    <div className="flex items-start gap-3 select-none">
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className={`relative mt-0.5 inline-flex h-6 w-11 shrink-0 cursor-pointer items-center rounded-full border-2 border-transparent transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald-400 focus-visible:ring-offset-2 dark:focus-visible:ring-offset-slate-900 disabled:cursor-not-allowed disabled:opacity-50 ${
          checked ? 'bg-emerald-500' : 'bg-gray-200 dark:bg-slate-600'
        }`}
      >
        <span
          className={`pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow-sm ring-0 transition-transform ${
            checked ? 'translate-x-5' : 'translate-x-0'
          }`}
        />
      </button>
      <label className="flex-1 min-w-0 cursor-pointer" onClick={() => !disabled && onChange(!checked)}>
        <div className="flex items-center gap-1.5 text-sm font-semibold text-gray-800 dark:text-slate-200">
          {icon && <span className="inline-flex">{icon}</span>}
          <span>{title}</span>
        </div>
        {description && (
          <p className="mt-0.5 text-xs text-gray-500 dark:text-slate-400 leading-relaxed">{description}</p>
        )}
      </label>
    </div>
  );
}
