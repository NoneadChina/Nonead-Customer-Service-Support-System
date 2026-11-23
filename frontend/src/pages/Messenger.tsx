import { useState, useEffect, useCallback, type ComponentType, type ReactNode } from 'react';
import { useSearchParams } from 'react-router-dom';
import { messengerApi } from '../api/modules/system';
import { mailboxApi } from '../api/modules/core';
import type {
  MessengerSettingsDto,
  MessengerStatusDto,
  UpdateMessengerSettingsRequest,
} from '../api/types/system';
import type { MailboxResponse } from '../api/types';
import { useToast } from '../context/ToastContext';
import { useI18n } from '../context/I18nContext';
import { usePermission } from '../hooks/usePermission';
import type { TranslationKey } from '../locales/zh-Hans';
import { formatDateTime } from '../utils/format';
import {
  Settings as SettingsIcon, Activity, RefreshCw, Copy, Check,
  AlertTriangle, Eye, EyeOff, Edit3, Lock, Zap, Tag,
  MessageSquare, Globe, Send, Rss, TestTube, Shield, Info, ChevronRight,
} from 'lucide-react';

type TabType = 'config' | 'subscribe' | 'status';

// Graph API 版本可配置（当前 v25.0；Meta 无 v2x 别名，禁止硬编码）——这里只做候选提示，
// 真实默认值来自后端 MessengerOptions.ApiVersion。
const API_VERSIONS = ['v25.0', 'v24.0', 'v23.0', 'v22.0'];

// MessengerSettingsDto.messageTagPolicy 在 C# 侧是无约束 string（NoneadCSS.Application/DTOs/Responses/MessengerDto.cs:13），
// 请求 DTO 也没有取值校验（NoneadCSS.Application/DTOs/Requests/MessengerRequests.cs:39），只有 MessengerOptions.cs:70 的
// RegularExpression 约定 auto/none/always；后端 ResolveMessageTag 对未知值按 auto 兜底（MessengerService.cs:692-708）。
// 因此这里按后端同口径做显式白名单归一，而不是把 DTO 类型直接收窄成联合类型。
const MESSAGE_TAG_POLICIES = ['auto', 'none', 'always'] as const;

function normalizeMessageTagPolicy(value: string | null | undefined): UpdateMessengerSettingsRequest['messageTagPolicy'] {
  const v = value?.trim().toLowerCase();
  return MESSAGE_TAG_POLICIES.find((p) => p === v) ?? 'auto';
}

const MessengerPage = () => {
  const toast = useToast();
  const { t } = useI18n();
  const { isReadOnly, isAdmin } = usePermission();
  const [searchParams, setSearchParams] = useSearchParams();
  const initialTab = (searchParams.get('tab') as TabType) || 'config';

  const [activeTab, setActiveTab] = useState<TabType>(initialTab);
  const [settings, setSettings] = useState<MessengerSettingsDto | null>(null);
  const [status, setStatus] = useState<MessengerStatusDto | null>(null);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [formData, setFormData] = useState<UpdateMessengerSettingsRequest>({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [subscribing, setSubscribing] = useState(false);
  const [copied, setCopied] = useState(false);
  const [pageIdRequired, setPageIdRequired] = useState(false);

  // 密钥显示/编辑开关（后端只回 Has* 布尔，不回显密文）
  const [showAccessToken, setShowAccessToken] = useState(false);
  const [editingAccessToken, setEditingAccessToken] = useState(false);
  const [showAppSecret, setShowAppSecret] = useState(false);
  const [editingAppSecret, setEditingAppSecret] = useState(false);
  const [showVerifyToken, setShowVerifyToken] = useState(false);
  const [editingVerifyToken, setEditingVerifyToken] = useState(false);

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      const [settingsData, statusData, mailboxData] = await Promise.all([
        messengerApi.getSettings(),
        messengerApi.getStatus(),
        mailboxApi.list(),
      ]);
      if (settingsData) {
        setSettings(settingsData);
        setFormData({
          appId: settingsData.appId,
          pageId: settingsData.pageId,
          pageUsername: settingsData.pageUsername,
          apiVersion: settingsData.apiVersion,
          mailboxId: settingsData.mailboxId,
          textChunkChars: settingsData.textChunkChars,
          windowHours: settingsData.windowHours,
          messageTagPolicy: normalizeMessageTagPolicy(settingsData.messageTagPolicy),
          typingIndicator: settingsData.typingIndicator,
          markSeen: settingsData.markSeen,
          contactCollectionEnabled: settingsData.contactCollectionEnabled,
          autoReplyEnabled: settingsData.autoReplyEnabled,
          autoReplyMessage: settingsData.autoReplyMessage,
          enabled: settingsData.enabled,
          subscribedFields: settingsData.subscribedFields,
        });
      }
      if (statusData) setStatus(statusData);
      if (mailboxData) setMailboxes(mailboxData);
    } catch (error) {
      console.error('Failed to load Messenger settings:', error);
      toast.error(t('channels.common.loadFailed', { channel: t('format.sourceType.messenger') }));
    } finally {
      setLoading(false);
    }
  }, [t, toast]);

  useEffect(() => {
    let destroyed = false;
    const init = async () => {
      if (!destroyed) await loadData();
    };
    init();
    return () => { destroyed = true; };
  }, [loadData]);

  const handleTabChange = (tab: TabType) => {
    setActiveTab(tab);
    setSearchParams(tab === 'config' ? {} : { tab }, { replace: true });
  };

  const handleFieldChange = <K extends keyof UpdateMessengerSettingsRequest>(
    field: K,
    value: UpdateMessengerSettingsRequest[K],
  ) => {
    setFormData((prev) => ({ ...prev, [field]: value }));
    if (field === 'pageId') setPageIdRequired(false);
  };

  const handleSave = async () => {
    if (!formData.pageId?.trim()) {
      setPageIdRequired(true);
      toast.error(t('channels.messenger.config.pageIdRequired'));
      return;
    }
    setPageIdRequired(false);
    setSaving(true);
    try {
      const payload: UpdateMessengerSettingsRequest = { ...formData };
      // 未点「编辑」的密钥字段不提交，避免把空串覆盖已存密文
      if (!editingAccessToken) payload.pageAccessToken = undefined;
      if (!editingAppSecret) payload.appSecret = undefined;
      if (!editingVerifyToken) payload.verifyToken = undefined;

      const saved = await messengerApi.updateSettings(payload);
      if (saved) {
        setSettings(saved);
        toast.success(t('channels.common.saveSucceeded', { channel: t('format.sourceType.messenger') }));
        setEditingAccessToken(false);
        setEditingAppSecret(false);
        setEditingVerifyToken(false);
        const refreshed = await messengerApi.getStatus();
        if (refreshed) setStatus(refreshed);
      } else {
        toast.error(t('channels.common.saveFailed', { channel: t('format.sourceType.messenger') }));
      }
    } catch (error) {
      console.error('Failed to save Messenger settings:', error);
      toast.error(t('channels.common.saveFailed'));
    } finally {
      setSaving(false);
    }
  };

  const handleCopyCallbackUrl = async () => {
    if (!settings?.callbackUrl) return;
    try {
      await navigator.clipboard.writeText(settings.callbackUrl);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      toast.error(t('channels.common.copyFailed'));
    }
  };

  const handleTestConnection = async () => {
    setTesting(true);
    try {
      const result = await messengerApi.testConnection();
      if (result?.success) {
        toast.success(t('channels.messenger.test.ok', { page: result.pageId ?? settings?.pageId ?? '' }));
      } else {
        toast.error(result?.errorMessage || t('channels.messenger.test.failed'));
      }
      const refreshed = await messengerApi.getStatus();
      if (refreshed) setStatus(refreshed);
    } catch (error) {
      console.error('Messenger connection test failed:', error);
      toast.error(t('channels.common.testConnectionFailed'));
    } finally {
      setTesting(false);
    }
  };

  const handleSubscribe = async (fields: string) => {
    setSubscribing(true);
    try {
      const result = await messengerApi.subscribe({ fields: fields || undefined });
      if (result?.success) {
        toast.success(t('channels.messenger.subscribe.ok', { fields: result.fields ?? fields }));
      } else {
        toast.error(result?.errorMessage || t('channels.messenger.subscribe.failed'));
      }
    } catch (error) {
      console.error('Messenger subscribe failed:', error);
      toast.error(t('channels.messenger.subscribe.failed'));
    } finally {
      setSubscribing(false);
    }
  };

  if (loading) {
    return (
      <div className="h-full overflow-y-auto">
        <div className="max-w-5xl mx-auto p-6 space-y-6">
          <div className="h-8 w-64 bg-white dark:bg-white/[0.06] rounded-lg animate-pulse" />
          <div className="h-40 bg-white dark:bg-white/[0.04] rounded-xl animate-pulse" />
          <div className="h-96 bg-white dark:bg-white/[0.04] rounded-xl animate-pulse" />
        </div>
      </div>
    );
  }

  const configured = Boolean(status?.configured);
  const connected = Boolean(status?.apiReachable);

  return (
    <div className="h-full overflow-y-auto">
      <div className="max-w-5xl mx-auto p-6 space-y-6 pb-24">
        {/* 页头 */}
        <div className="relative overflow-hidden rounded-2xl border border-gray-200 dark:border-white/[0.08] bg-gradient-to-br from-blue-50 via-white to-white p-6 shadow-sm ring-1 ring-inset ring-blue-100/70 dark:from-blue-500/[0.12] dark:via-sky-500/[0.06] dark:to-transparent dark:shadow-[0_24px_70px_-40px_rgba(59,130,246,0.75)] dark:ring-0">
          {/* 装饰性光斑：纯视觉效果，不参与布局与交互 */}
          <div aria-hidden="true" className="pointer-events-none absolute -right-16 -top-20 h-48 w-48 rounded-full bg-blue-500/10 blur-3xl" />
          <div aria-hidden="true" className="pointer-events-none absolute -bottom-24 left-1/3 h-40 w-40 rounded-full bg-sky-400/[0.07] blur-3xl" />
          <div className="flex items-start justify-between gap-4 flex-wrap">
            <div className="flex items-center gap-4">
              <div className="p-3 rounded-lg bg-blue-500/20 border border-blue-500/30">
                <MessageSquare className="h-7 w-7 text-blue-400" />
              </div>
              <div>
                <h1 className="text-2xl font-bold text-gray-900 dark:text-white">{t('channels.messenger.header.title')}</h1>
                <p className="text-sm text-gray-700 dark:text-white/60 mt-1">{t('channels.messenger.header.subtitle')}</p>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={handleTestConnection}
                disabled={testing || isReadOnly || !isAdmin}
                title={isReadOnly ? t('common.readOnlyTooltip') : !isAdmin ? t('common.superAdminOnlyTooltip') : undefined}
                className="flex items-center gap-2 px-3 py-2 text-sm rounded-lg bg-white dark:bg-white/[0.06] border border-gray-200 dark:border-white/10 text-gray-700 dark:text-white/80 hover:bg-gray-100 dark:hover:bg-white/10 disabled:opacity-50"
              >
                {testing ? <RefreshCw className="h-4 w-4 animate-spin" /> : <TestTube className="h-4 w-4" />}
                {testing ? t('channels.common.testing') : t('channels.common.testConnection')}
              </button>
              <button
                type="button"
                onClick={loadData}
                className="flex items-center gap-2 px-3 py-2 text-sm rounded-lg bg-white dark:bg-white/[0.06] border border-gray-200 dark:border-white/10 text-gray-700 dark:text-white/80 hover:bg-gray-100 dark:hover:bg-white/10"
              >
                <RefreshCw className="h-4 w-4" />
                {t('channels.common.refresh')}
              </button>
            </div>
          </div>

          <div className="mt-4 flex items-center gap-2 flex-wrap">
            {connected ? (
              <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs bg-emerald-500/15 border border-emerald-500/25 text-emerald-300">
                <Check className="h-3.5 w-3.5" />
                {t('channels.messenger.connection.connected')}
              </span>
            ) : configured ? (
              <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs bg-amber-500/15 border border-amber-500/25 text-amber-300">
                <AlertTriangle className="h-3.5 w-3.5" />
                {t('channels.common.configuredButDisabled')}
              </span>
            ) : (
              <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs bg-white dark:bg-white/[0.06] border border-gray-200 dark:border-white/10 text-gray-700 dark:text-white/60">
                <Info className="h-3.5 w-3.5" />
                {t('channels.common.notConfigured')}
              </span>
            )}
          </div>
        </div>

        {/* 快速开始 */}
        {!configured && <QuickStartGuide callbackUrl={settings?.callbackUrl} onCopy={handleCopyCallbackUrl} />}

        {/* Tabs */}
        <div className="inline-flex flex-wrap items-center gap-1 rounded-xl border border-gray-200 dark:border-white/[0.08] bg-white dark:bg-white/[0.03] p-1">
          <TabButton active={activeTab === 'config'} onClick={() => handleTabChange('config')} icon={SettingsIcon}>
            {t('channels.common.basicConfig')}
          </TabButton>
          <TabButton active={activeTab === 'subscribe'} onClick={() => handleTabChange('subscribe')} icon={Rss}>
            {t('channels.messenger.tab.subscribe')}
          </TabButton>
          <TabButton active={activeTab === 'status'} onClick={() => handleTabChange('status')} icon={Activity}>
            {t('channels.messenger.tab.status')}
          </TabButton>
        </div>

        {activeTab === 'config' && (
          <ConfigTab
            formData={formData}
            onFieldChange={handleFieldChange}
            settings={settings}
            mailboxes={mailboxes}
            saving={saving}
            onSave={handleSave}
            showAccessToken={showAccessToken}
            setShowAccessToken={setShowAccessToken}
            editingAccessToken={editingAccessToken}
            setEditingAccessToken={setEditingAccessToken}
            showAppSecret={showAppSecret}
            setShowAppSecret={setShowAppSecret}
            editingAppSecret={editingAppSecret}
            setEditingAppSecret={setEditingAppSecret}
            showVerifyToken={showVerifyToken}
            setShowVerifyToken={setShowVerifyToken}
            editingVerifyToken={editingVerifyToken}
            setEditingVerifyToken={setEditingVerifyToken}
            pageIdRequired={pageIdRequired}
            onReset={loadData}
            onCopyCallbackUrl={handleCopyCallbackUrl}
            copied={copied}
          />
        )}

        {activeTab === 'subscribe' && (
          <SubscribePanel
            subscribedFields={formData.subscribedFields ?? ''}
            onChange={(v) => handleFieldChange('subscribedFields', v)}
            onSubscribe={handleSubscribe}
            subscribing={subscribing}
            saving={saving}
            isReadOnly={isReadOnly}
            isAdmin={isAdmin}
          />
        )}

        {activeTab === 'status' && <StatusPanel status={status} />}
      </div>
    </div>
  );
};

function TabButton({
  active, onClick, icon: Icon, children,
}: { active: boolean; onClick: () => void; icon: typeof SettingsIcon; children: ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-current={active ? 'page' : undefined}
      className={`group flex items-center gap-2 rounded-lg px-4 py-2 text-sm transition-all duration-200 ${
        active
          ? 'bg-gradient-to-b from-blue-50 to-white dark:from-blue-500/25 dark:to-blue-500/[0.08] font-medium text-gray-900 dark:text-white ring-1 ring-inset ring-blue-200 dark:ring-blue-400/40 shadow-[inset_0_1px_0_0_rgba(255,255,255,0.08)]'
          : 'text-gray-600 dark:text-white/55 hover:bg-gray-100 dark:hover:bg-white/[0.05] hover:text-gray-900 dark:hover:text-white/85'
      }`}
    >
      <Icon className={`h-4 w-4 transition-colors ${active ? 'text-blue-300' : 'text-gray-600 dark:text-white/35 group-hover:text-gray-900 dark:hover:text-white/60'}`} />
      {children}
    </button>
  );
}

// 输入框共用样式：所有文本框/密钥框/只读框都走这一份，避免各卡片样式漂移
function fieldClass(invalid?: boolean) {
  return [
    'w-full rounded-lg border bg-white dark:bg-white/[0.04] px-3 py-2 text-sm text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-white/30 transition-colors focus:outline-none focus:ring-2 disabled:opacity-50',
    invalid
      ? 'border-red-500/60 focus:border-red-400/70 focus:ring-red-400/20'
      : 'border-gray-200 dark:border-white/10 hover:border-gray-200 dark:border-white/20 focus:border-blue-400/60 focus:ring-blue-400/20',
  ].join(' ');
}

function SectionCard({
  title, icon: Icon, children, hint,
}: { title: string; icon: ComponentType<{ className?: string }>; children: ReactNode; hint?: string }) {
  return (
    <section className="group overflow-hidden rounded-2xl border border-gray-200 dark:border-white/[0.08] bg-white dark:bg-white/[0.025] transition-colors hover:border-white/[0.14]">
      <header className="flex items-start gap-3 border-b border-gray-200 dark:border-white/[0.06] bg-gradient-to-r from-white/[0.05] via-white/[0.015] to-transparent px-5 py-4">
        <span className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-blue-500/15 ring-1 ring-inset ring-blue-400/25">
          <Icon className="h-4 w-4 text-blue-300" />
        </span>
        <div className="min-w-0">
          <h3 className="text-sm font-semibold tracking-wide text-gray-900 dark:text-white">{title}</h3>
          {hint && <p className="mt-1 text-xs leading-relaxed text-gray-600 dark:text-white/50">{hint}</p>}
        </div>
      </header>
      <div className="space-y-5 p-5">{children}</div>
    </section>
  );
}

function FormField({
  icon: Icon, label, required, hint, children,
}: { icon?: ComponentType<{ className?: string }>; label: string; required?: boolean; hint?: string; children: ReactNode }) {
  return (
    <div className="space-y-2">
      <label className="flex items-center gap-1.5 text-[12px] font-medium tracking-wide text-gray-700 dark:text-white/75">
        {Icon && <Icon className="h-3.5 w-3.5 text-gray-600 dark:text-white/35" />}
        {label}
        {required && <span className="text-red-400">*</span>}
      </label>
      {children}
      {hint && <p className="text-[11px] leading-relaxed text-gray-600 dark:text-white/45">{hint}</p>}
    </div>
  );
}

function TextInput({
  value, onChange, placeholder, type = 'text', disabled, invalid, list,
}: {
  value?: string | number | null;
  onChange: (v: string) => void;
  placeholder?: string;
  type?: string;
  disabled?: boolean;
  invalid?: boolean;
  list?: string;
}) {
  return (
    <input
      type={type}
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
      disabled={disabled}
      list={list}
      className={fieldClass(invalid)}
    />
  );
}

function SecretInput({
  value, placeholder, hasExisting, editing, setEditing, show, setShow, onChange, disabled,
}: {
  value?: string | null;
  placeholder: string;
  hasExisting?: boolean;
  editing: boolean;
  setEditing: (v: boolean) => void;
  show: boolean;
  setShow: (v: boolean) => void;
  onChange: (v: string) => void;
  disabled?: boolean;
}) {
  const { t } = useI18n();
  const canEdit = !disabled && (!hasExisting || editing);
  return (
    <div className="flex gap-2">
      <div className="relative flex-1">
        <input
          type={show ? 'text' : 'password'}
          value={editing ? (value ?? '') : hasExisting ? '••••••••••••' : ''}
          onChange={(e) => onChange(e.target.value)}
          placeholder={hasExisting && !editing ? t('channels.messenger.auth.configuredPlaceholder') : placeholder}
          disabled={!canEdit}
          readOnly={hasExisting && !editing}
          className={`${fieldClass()} pr-10 font-mono tracking-tight`}
        />
        {value !== undefined && (
          <button
            type="button"
            onClick={() => setShow(!show)}
            aria-label={show ? t('channels.messenger.secret.hideAria') : t('channels.messenger.secret.showAria')}
            className="absolute right-1.5 top-1/2 -translate-y-1/2 rounded-md p-1 text-gray-600 dark:text-white/40 transition-colors hover:bg-gray-100 dark:hover:bg-white/[0.06] hover:text-gray-900 dark:hover:text-white/80"
          >
            {show ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
          </button>
        )}
      </div>
      {hasExisting && (
        <button
          type="button"
          onClick={() => setEditing(!editing)}
          title={editing ? t('channels.common.lockSecret') : t('channels.common.editSecret')}
          className={`shrink-0 rounded-lg border px-3 py-2 transition-colors ${
            editing
              ? 'border-amber-500/30 bg-amber-500/15 text-amber-300'
              : 'border-gray-200 dark:border-white/10 bg-white dark:bg-white/[0.06] text-gray-700 dark:text-white/70 hover:bg-gray-100 dark:hover:bg-white/10'
          }`}
        >
          {editing ? <Lock className="h-4 w-4" /> : <Edit3 className="h-4 w-4" />}
        </button>
      )}
    </div>
  );
}

function ToggleSwitch({
  checked, onChange, disabled, icon: Icon, title, description,
}: {
  checked?: boolean | null;
  onChange: (v: boolean) => void;
  disabled?: boolean;
  icon?: typeof SettingsIcon;
  title: string;
  description?: string;
}) {
  return (
    <div className="-mx-2 flex items-start justify-between gap-4 rounded-xl px-2 py-2.5 transition-colors hover:bg-gray-100 dark:hover:bg-white/[0.03]">
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-1.5 text-sm text-gray-700 dark:text-white/85">
          {Icon && <Icon className="h-4 w-4 text-gray-600 dark:text-white/40" />}
          {title}
        </div>
        {description && <p className="mt-0.5 text-xs leading-relaxed text-gray-600 dark:text-white/45">{description}</p>}
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={Boolean(checked)}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className={`relative h-6 w-11 shrink-0 rounded-full border transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400/40 disabled:opacity-50 ${
          checked
            ? 'border-blue-300/40 bg-gradient-to-r from-blue-500 to-sky-400'
            : 'border-gray-200 dark:border-white/15 bg-white dark:bg-white/[0.08]'
        }`}
      >
        <span
          className={`absolute left-0.5 top-0.5 h-5 w-5 rounded-full bg-white shadow transition-transform duration-200 ${
            checked ? 'translate-x-5' : 'translate-x-0'
          }`}
        />
      </button>
    </div>
  );
}

function QuickStartGuide({ callbackUrl, onCopy }: { callbackUrl?: string | null; onCopy: () => void }) {
  const { t } = useI18n();
  const steps: { num: number; titleKey: TranslationKey; descKey: TranslationKey; link?: string; linkTextKey?: TranslationKey }[] = [
    {
      num: 1,
      titleKey: 'channels.messenger.guide.createApp.title',
      descKey: 'channels.messenger.guide.createApp.desc',
      link: 'https://developers.facebook.com/apps/',
      linkTextKey: 'channels.messenger.guide.metaForDevelopers',
    },
    {
      num: 2,
      titleKey: 'channels.messenger.guide.pageToken.title',
      descKey: 'channels.messenger.guide.pageToken.desc',
    },
    {
      num: 3,
      titleKey: 'channels.messenger.guide.webhook.title',
      descKey: 'channels.messenger.guide.webhook.desc',
      link: 'https://developers.facebook.com/docs/messenger-platform/getting-started/webhook-setup',
      linkTextKey: 'channels.messenger.guide.webhookGuide',
    },
    {
      num: 4,
      titleKey: 'channels.messenger.guide.subscribe.title',
      descKey: 'channels.messenger.guide.subscribe.desc',
    },
    {
      num: 5,
      titleKey: 'channels.messenger.guide.save.title',
      descKey: 'channels.messenger.guide.save.desc',
    },
  ];

  return (
    <div className="bg-white dark:bg-white/[0.03] border border-gray-200 dark:border-white/[0.08] rounded-xl p-6 space-y-5">
      <div className="flex items-center gap-2">
        <Zap className="h-5 w-5 text-amber-400" />
        <h2 className="text-base font-semibold text-gray-900 dark:text-white">{t('channels.messenger.guide.title')}</h2>
      </div>
      <p className="text-sm text-gray-600 dark:text-white/50">{t('channels.messenger.guide.subtitle')}</p>
      <div className="space-y-3">
        {steps.map((step) => (
          <div key={step.num} className="flex gap-3">
            <div className="shrink-0 w-7 h-7 rounded-full bg-blue-500/20 border border-blue-500/30 text-blue-300 text-sm font-semibold flex items-center justify-center">
              {step.num}
            </div>
            <div className="flex-1 min-w-0">
              <div className="text-sm text-gray-700 dark:text-white/85">{t(step.titleKey)}</div>
              <div className="text-xs text-gray-600 dark:text-white/50 mt-0.5 leading-relaxed">{t(step.descKey)}</div>
              {step.num === 3 && callbackUrl && (
                <button
                  type="button"
                  onClick={onCopy}
                  className="mt-2 inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-md bg-white dark:bg-white/[0.06] border border-gray-200 dark:border-white/10 text-xs text-gray-700 dark:text-white/80 hover:bg-gray-100 dark:hover:bg-white/10"
                >
                  <Copy className="h-3.5 w-3.5" />
                  {t('channels.common.copyCallbackUrl')}
                </button>
              )}
            </div>
            {step.link && (
              <a
                href={step.link}
                target="_blank"
                rel="noreferrer"
                className="shrink-0 inline-flex items-center gap-1 text-xs text-blue-300 hover:text-blue-200"
              >
                {step.linkTextKey ? t(step.linkTextKey) : step.link}
                <ChevronRight className="h-3 w-3" />
              </a>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

function ConfigTab(props: {
  formData: UpdateMessengerSettingsRequest;
  onFieldChange: <K extends keyof UpdateMessengerSettingsRequest>(field: K, value: UpdateMessengerSettingsRequest[K]) => void;
  settings: MessengerSettingsDto | null;
  mailboxes: MailboxResponse[];
  saving: boolean;
  onSave: () => void;
  showAccessToken: boolean;
  setShowAccessToken: (v: boolean) => void;
  editingAccessToken: boolean;
  setEditingAccessToken: (v: boolean) => void;
  showAppSecret: boolean;
  setShowAppSecret: (v: boolean) => void;
  editingAppSecret: boolean;
  setEditingAppSecret: (v: boolean) => void;
  showVerifyToken: boolean;
  setShowVerifyToken: (v: boolean) => void;
  editingVerifyToken: boolean;
  setEditingVerifyToken: (v: boolean) => void;
  pageIdRequired: boolean;
  onReset: () => void;
  onCopyCallbackUrl: () => void;
  copied: boolean;
}) {
  const { t } = useI18n();
  const { isReadOnly, isAdmin } = usePermission();
  const {
    formData, onFieldChange, settings, mailboxes, saving, onSave,
    showAccessToken, setShowAccessToken, editingAccessToken, setEditingAccessToken,
    showAppSecret, setShowAppSecret, editingAppSecret, setEditingAppSecret,
    showVerifyToken, setShowVerifyToken, editingVerifyToken, setEditingVerifyToken,
    pageIdRequired, onReset, onCopyCallbackUrl, copied,
  } = props;

  return (
    <div className="space-y-6">
      <SectionCard title={t('channels.messenger.config.basicInfo')} icon={IdCard} hint={t('channels.messenger.config.basicInfoHint')}>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <FormField
            icon={IdCard}
            label={t('channels.messenger.config.pageIdLabel')}
            required
            hint={t('channels.messenger.config.pageIdHint')}
          >
            <TextInput
              value={formData.pageId}
              onChange={(v) => onFieldChange('pageId', v)}
              placeholder={t('channels.messenger.config.pageIdPlaceholder')}
              invalid={pageIdRequired}
            />
          </FormField>
          <FormField
            icon={AtSign}
            label={t('channels.messenger.config.pageUsernameLabel')}
            hint={t('channels.messenger.config.pageUsernameHint')}
          >
            <TextInput
              value={formData.pageUsername}
              onChange={(v) => onFieldChange('pageUsername', v)}
              placeholder="@yourpage"
            />
          </FormField>
          <FormField icon={Globe} label={t('channels.messenger.config.apiVersion')} hint={t('channels.messenger.config.apiVersionHint')}>
            <>
              <TextInput
                value={formData.apiVersion}
                onChange={(v) => onFieldChange('apiVersion', v)}
                placeholder="v25.0"
                list="messenger-api-versions"
              />
              <datalist id="messenger-api-versions">
                {API_VERSIONS.map((v) => (
                  <option key={v} value={v} />
                ))}
              </datalist>
            </>
          </FormField>
          <FormField icon={MailboxIcon} label={t('channels.common.linkedMailbox')}>
            <select
              value={formData.mailboxId ?? ''}
              onChange={(e) => onFieldChange('mailboxId', e.target.value ? Number(e.target.value) : undefined)}
              className="w-full px-3 py-2 text-sm rounded-lg bg-gray-50 dark:bg-black/30 border border-gray-200 dark:border-white/10 text-gray-900 dark:text-white focus:outline-none focus:ring-1 focus:ring-blue-400/40"
            >
              <option value="">{t('channels.messenger.config.noMailbox')}</option>
              {mailboxes.map((mb) => (
                <option key={mb.id} value={mb.id}>
                  {mb.name || mb.email || mb.id}
                </option>
              ))}
            </select>
          </FormField>
          <FormField
            icon={MessageSquare}
            label={t('channels.messenger.config.textChunkChars')}
            hint={t('channels.messenger.config.textChunkCharsHint')}
          >
            <TextInput
              type="number"
              value={formData.textChunkChars}
              onChange={(v) => onFieldChange('textChunkChars', v ? Number(v) : undefined)}
              placeholder="2000"
            />
          </FormField>
          <FormField
            icon={Activity}
            label={t('channels.messenger.config.windowHours')}
            hint={t('channels.messenger.config.windowHoursHint')}
          >
            <TextInput
              type="number"
              value={formData.windowHours}
              onChange={(v) => onFieldChange('windowHours', v ? Number(v) : undefined)}
              placeholder="24"
            />
          </FormField>
          <FormField icon={SettingsIcon} label={t('channels.common.basicConfig')}>
            <TextInput
              value={formData.appId}
              onChange={(v) => onFieldChange('appId', v)}
              placeholder={t('channels.messenger.config.appIdPlaceholder')}
            />
          </FormField>
        </div>
      </SectionCard>

      <SectionCard title={t('channels.messenger.auth.title')} icon={Shield} hint={t('channels.messenger.auth.encryptedHint')}>
        <FormField
          icon={Lock}
          label={t('channels.messenger.auth.pageAccessTokenLabel')}
          required
          hint={t('channels.messenger.auth.pageAccessTokenHint')}
        >
          <SecretInput
            value={formData.pageAccessToken}
            placeholder={t('channels.messenger.auth.pageAccessTokenPlaceholder')}
            hasExisting={settings?.hasPageAccessToken}
            editing={editingAccessToken}
            setEditing={setEditingAccessToken}
            show={showAccessToken}
            setShow={setShowAccessToken}
            onChange={(v) => onFieldChange('pageAccessToken', v)}
          />
        </FormField>
        <FormField
          icon={Lock}
          label={t('channels.messenger.auth.appSecretLabel')}
          required
          hint={t('channels.messenger.auth.appSecretHint')}
        >
          <SecretInput
            value={formData.appSecret}
            placeholder={t('channels.messenger.auth.appSecretPlaceholder')}
            hasExisting={settings?.hasAppSecret}
            editing={editingAppSecret}
            setEditing={setEditingAppSecret}
            show={showAppSecret}
            setShow={setShowAppSecret}
            onChange={(v) => onFieldChange('appSecret', v)}
          />
        </FormField>
        <FormField
          icon={Lock}
          label={t('channels.messenger.auth.verifyTokenLabel')}
          required
          hint={t('channels.messenger.auth.verifyTokenHint')}
        >
          <SecretInput
            value={formData.verifyToken}
            placeholder={t('channels.messenger.auth.verifyTokenPlaceholder')}
            hasExisting={settings?.hasVerifyToken}
            editing={editingVerifyToken}
            setEditing={setEditingVerifyToken}
            show={showVerifyToken}
            setShow={setShowVerifyToken}
            onChange={(v) => onFieldChange('verifyToken', v)}
          />
        </FormField>
        <p className="text-[11px] text-gray-600 dark:text-white/40">{t('channels.messenger.auth.storedHint')}</p>
      </SectionCard>

      <SectionCard title={t('channels.messenger.behavior.title')} icon={Tag}>
        <FormField label={t('channels.messenger.behavior.messageTagPolicy')} hint={t('channels.messenger.behavior.messageTagPolicyHint')}>
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-2">
            {(['auto', 'always', 'none'] as const).map((policy) => (
              <button
                key={policy}
                type="button"
                onClick={() => onFieldChange('messageTagPolicy', policy)}
                className={`px-3 py-2 rounded-lg border text-sm ${
                  formData.messageTagPolicy === policy
                    ? 'bg-blue-500/15 border-blue-500/35 text-blue-200'
                    : 'bg-white dark:bg-white/[0.04] border-gray-200 dark:border-white/10 text-gray-700 dark:text-white/65 hover:bg-gray-100 dark:hover:bg-white/[0.08]'
                }`}
              >
                {policy === 'auto' && t('channels.messenger.behavior.policyAuto')}
                {policy === 'always' && t('channels.messenger.behavior.policyAlways')}
                {policy === 'none' && t('channels.messenger.behavior.policyNone')}
              </button>
            ))}
          </div>
        </FormField>
        <div className="divide-y divide-white/[0.06]">
          <ToggleSwitch
            icon={MessageSquare}
            title={t('channels.messenger.behavior.typingIndicator')}
            description={t('channels.messenger.behavior.typingIndicatorDesc')}
            checked={formData.typingIndicator}
            onChange={(v) => onFieldChange('typingIndicator', v)}
          />
          <ToggleSwitch
            icon={Check}
            title={t('channels.messenger.behavior.markSeen')}
            description={t('channels.messenger.behavior.markSeenDesc')}
            checked={formData.markSeen}
            onChange={(v) => onFieldChange('markSeen', v)}
          />
          <ToggleSwitch
            icon={Info}
            title={t('channels.messenger.behavior.contactCollection')}
            description={t('channels.messenger.behavior.contactCollectionDesc')}
            checked={formData.contactCollectionEnabled}
            onChange={(v) => onFieldChange('contactCollectionEnabled', v)}
          />
          <ToggleSwitch
            icon={Zap}
            title={t('channels.messenger.behavior.autoReplyTitle')}
            description={t('channels.messenger.behavior.autoReplyDesc')}
            checked={formData.autoReplyEnabled}
            onChange={(v) => onFieldChange('autoReplyEnabled', v)}
          />
        </div>
        {formData.autoReplyEnabled && (
          <FormField label={t('channels.messenger.behavior.autoReplyTitle')}>
            <textarea
              value={formData.autoReplyMessage ?? ''}
              onChange={(e) => onFieldChange('autoReplyMessage', e.target.value)}
              placeholder={t('channels.messenger.behavior.autoReplyPlaceholder')}
              rows={3}
              className="w-full px-3 py-2 text-sm rounded-lg bg-gray-50 dark:bg-black/30 border border-gray-200 dark:border-white/10 text-gray-900 dark:text-white placeholder-gray-400 dark:placeholder-white/30 focus:outline-none focus:ring-1 focus:ring-blue-400/40"
            />
          </FormField>
        )}
        <div className="pt-1 border-t border-gray-200 dark:border-white/[0.06]">
          <ToggleSwitch
            icon={Check}
            title={t('channels.common.enableChannel', { channel: t('format.sourceType.messenger') })}
            description={t('channels.messenger.behavior.enableHint')}
            checked={formData.enabled}
            onChange={(v) => onFieldChange('enabled', v)}
          />
        </div>
      </SectionCard>

      <SectionCard title={t('channels.messenger.webhook.title')} icon={Rss} hint={t('channels.messenger.webhook.requiredHint')}>
        <FormField label={t('channels.messenger.webhook.callbackUrl')}>
          <div className="flex gap-2">
            <input
              readOnly
              value={settings?.callbackUrl ?? t('channels.messenger.webhook.pendingHint')}
              className={`${fieldClass()} flex-1 font-mono text-[13px]`}
            />
            <button
              type="button"
              onClick={onCopyCallbackUrl}
              disabled={!settings?.callbackUrl}
              className="flex items-center gap-1.5 px-3 py-2 rounded-lg bg-white dark:bg-white/[0.06] border border-gray-200 dark:border-white/10 text-sm text-gray-700 dark:text-white/80 hover:bg-gray-100 dark:hover:bg-white/10 disabled:opacity-50"
            >
              {copied ? <Check className="h-4 w-4 text-emerald-400" /> : <Copy className="h-4 w-4" />}
              {t('channels.messenger.webhook.copyUrl')}
            </button>
          </div>
        </FormField>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <FormField label={t('channels.messenger.webhook.fieldTitle')} hint={t('channels.messenger.webhook.fieldMessages')}>
            <TextInput value={formData.subscribedFields} onChange={(v) => onFieldChange('subscribedFields', v)} />
          </FormField>
          <FormField label={t('channels.messenger.webhook.verifyTokenHint')}>
            <p className="text-xs text-gray-600 dark:text-white/50 leading-relaxed">{t('channels.messenger.webhook.signatureHint')}</p>
          </FormField>
        </div>
      </SectionCard>

      <div className="sticky bottom-0 flex items-center justify-end gap-3 px-5 py-4 rounded-xl bg-gray-50 dark:bg-black/50 border border-gray-200 dark:border-white/10 backdrop-blur">
        <button
          type="button"
          onClick={onReset}
          disabled={saving}
          className="px-4 py-2.5 rounded-lg bg-white dark:bg-white/[0.06] border border-gray-200 dark:border-white/10 text-sm text-gray-700 dark:text-white/70 hover:bg-gray-100 dark:hover:bg-white/10 disabled:opacity-60"
        >
          {t('common.reset')}
        </button>
        <button
          type="button"
          onClick={onSave}
          disabled={saving || isReadOnly || !isAdmin}
          title={isReadOnly ? t('common.readOnlyTooltip') : !isAdmin ? t('common.superAdminOnlyTooltip') : undefined}
          className="flex items-center gap-2 px-5 py-2.5 rounded-lg bg-blue-500 text-gray-900 dark:text-white text-sm font-medium hover:bg-blue-400 disabled:opacity-60"
        >
          {saving ? <RefreshCw className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
          {saving ? t('channels.messenger.actions.saving') : t('channels.common.saveConfig')}
        </button>
      </div>
    </div>
  );
}

function SubscribePanel({
  subscribedFields, onChange, onSubscribe, subscribing, saving, isReadOnly, isAdmin,
}: {
  subscribedFields: string;
  onChange: (v: string) => void;
  onSubscribe: (fields: string) => void;
  subscribing: boolean;
  saving: boolean;
  isReadOnly: boolean;
  isAdmin: boolean;
}) {
  const { t } = useI18n();
  const [draft, setDraft] = useState(subscribedFields);
  useEffect(() => setDraft(subscribedFields), [subscribedFields]);

  return (
    <div className="space-y-5">
      <SectionCard title={t('channels.messenger.subscribe.title')} icon={Rss} hint={t('channels.messenger.subscribe.hint')}>
        <p className="text-sm text-gray-700 dark:text-white/60 leading-relaxed">
          {t('channels.messenger.subscribe.descPrefix')}
          <code className="px-1.5 py-0.5 rounded bg-gray-50 dark:bg-black/40 text-gray-700 dark:text-white/80">POST /{'{'}version{'}'}/me/subscribed_apps</code>
          {t('channels.messenger.subscribe.descSuffix')}
        </p>
        <FormField label={t('channels.messenger.subscribe.fieldsLabel')} hint={t('channels.messenger.subscribe.fieldsHint')}>
          <textarea
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            rows={4}
            className="w-full px-3 py-2 text-sm rounded-lg bg-gray-50 dark:bg-black/30 border border-gray-200 dark:border-white/10 text-gray-900 dark:text-white focus:outline-none focus:ring-1 focus:ring-blue-400/40"
          />
        </FormField>
        <div className="flex flex-wrap gap-2 text-[11px] text-gray-600 dark:text-white/45">
          <span>{t('channels.messenger.subscribe.devModeHint')}</span>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => onChange(draft)}
            disabled={saving || draft === subscribedFields || isReadOnly || !isAdmin}
            title={isReadOnly ? t('common.readOnlyTooltip') : !isAdmin ? t('common.superAdminOnlyTooltip') : undefined}
            className="px-3 py-2 text-sm rounded-lg bg-white dark:bg-white/[0.06] border border-gray-200 dark:border-white/10 text-gray-700 dark:text-white/80 hover:bg-gray-100 dark:hover:bg-white/10 disabled:opacity-50"
          >
            {t('channels.common.saveConfig')}
          </button>
          <button
            type="button"
            onClick={() => onSubscribe(draft)}
            disabled={subscribing || isReadOnly || !isAdmin}
            title={isReadOnly ? t('common.readOnlyTooltip') : !isAdmin ? t('common.superAdminOnlyTooltip') : undefined}
            className="flex items-center gap-2 px-4 py-2 text-sm rounded-lg bg-blue-500 text-gray-900 dark:text-white hover:bg-blue-400 disabled:opacity-60"
          >
            {subscribing ? <RefreshCw className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
            {t('channels.messenger.subscribe.sendNow')}
          </button>
        </div>
        <p className="text-[11px] text-gray-600 dark:text-white/40">{t('channels.messenger.subscribe.threadHint')}</p>
      </SectionCard>
    </div>
  );
}

function StatusPanel({ status }: { status: MessengerStatusDto | null }) {
  const { t } = useI18n();
  const windowStatus = status?.windowStatus ?? 'unknown';
  const remaining = status?.windowRemainingHours;
  const windowHours = 24;

  const items: { labelKey: TranslationKey; icon: typeof SettingsIcon; valueKey: TranslationKey; ok: boolean; hintKey?: TranslationKey }[] = [
    {
      labelKey: 'channels.messenger.status.integrity',
      icon: Shield,
      valueKey: status?.configured ? 'channels.messenger.status.integrityOk' : 'channels.messenger.status.integrityMissing',
      ok: Boolean(status?.configured),
      hintKey: status?.configured ? 'channels.messenger.status.integrityOkHint' : 'channels.messenger.status.integrityMissingHint',
    },
    {
      labelKey: 'channels.messenger.status.moduleSwitch',
      icon: Check,
      valueKey: status?.enabled ? 'channels.messenger.status.enabled' : 'channels.messenger.status.disabled',
      ok: Boolean(status?.enabled),
      hintKey: status?.enabled ? 'channels.messenger.status.enabledHint' : 'channels.messenger.status.disabledHint',
    },
    {
      labelKey: 'channels.messenger.status.apiReachable',
      icon: Globe,
      valueKey: status?.apiReachable ? 'channels.messenger.status.apiOk' : 'channels.messenger.status.apiDown',
      ok: Boolean(status?.apiReachable),
      hintKey: status?.apiReachable ? 'channels.messenger.status.apiOkHint' : 'channels.messenger.status.apiDownHint',
    },
  ];

  return (
    <div className="space-y-5">
      <SectionCard title={t('channels.messenger.status.overviewTitle')} icon={Activity} hint={t('channels.messenger.status.liveData')}>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
          {items.map((item) => (
            <div key={item.labelKey} className="rounded-lg border border-gray-200 dark:border-white/[0.08] bg-white dark:bg-white/[0.03] p-4 space-y-2">
              <div className="flex items-center gap-2 text-xs text-gray-600 dark:text-white/55">
                <item.icon className="h-4 w-4 text-gray-600 dark:text-white/40" />
                {t(item.labelKey)}
              </div>
              <div className={`text-sm font-medium ${item.ok ? 'text-emerald-300' : 'text-amber-300'}`}>{t(item.valueKey)}</div>
              {item.hintKey && <div className="text-[11px] text-gray-600 dark:text-white/45 leading-relaxed">{t(item.hintKey)}</div>}
            </div>
          ))}
        </div>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3 text-xs text-gray-700 dark:text-white/60">
          <div className="rounded-lg border border-gray-200 dark:border-white/[0.06] bg-gray-50 dark:bg-black/20 p-3">
            <div className="text-gray-600 dark:text-white/40">{t('channels.messenger.status.pageId')}</div>
            <div className="mt-1 text-gray-700 dark:text-white/85 break-all">{status?.pageId || '—'}</div>
          </div>
          <div className="rounded-lg border border-gray-200 dark:border-white/[0.06] bg-gray-50 dark:bg-black/20 p-3">
            <div className="text-gray-600 dark:text-white/40">{t('channels.messenger.status.apiVersion')}</div>
            <div className="mt-1 text-gray-700 dark:text-white/85">{status?.apiVersion || '—'}</div>
          </div>
          <div className="rounded-lg border border-gray-200 dark:border-white/[0.06] bg-gray-50 dark:bg-black/20 p-3">
            <div className="text-gray-600 dark:text-white/40">{t('channels.messenger.status.callbackUrl')}</div>
            <div className="mt-1 text-gray-700 dark:text-white/85 break-all">{status?.callbackUrl || '—'}</div>
          </div>
        </div>
      </SectionCard>

      <SectionCard title={t('channels.messenger.status.tokenTitle')} icon={Shield}>
        <div className="flex items-start gap-3">
          {status?.tokenInvalidAt ? (
            <>
              <AlertTriangle className="h-5 w-5 text-red-400 shrink-0 mt-0.5" />
              <div className="text-sm text-gray-700 dark:text-white/75 leading-relaxed">
                <div className="text-red-300 font-medium">{t('channels.messenger.status.tokenInvalid')}</div>
                <div className="text-xs text-gray-600 dark:text-white/50 mt-1">
                  {t('channels.messenger.status.tokenInvalidAt', { time: formatDateTime(status.tokenInvalidAt) })}
                </div>
                <div className="text-[11px] text-gray-600 dark:text-white/45 mt-2">{t('channels.messenger.status.tokenNoRefreshHint')}</div>
                {status.lastError && (
                  <div className="text-[11px] text-gray-600 dark:text-white/45 mt-1">
                    {t('channels.messenger.status.lastError')}: code {status.lastError.code}
                    {status.lastError.fbtraceId ? ` · fbtrace_id ${status.lastError.fbtraceId}` : ''}
                  </div>
                )}
              </div>
            </>
          ) : (
            <>
              <Check className="h-5 w-5 text-emerald-400 shrink-0 mt-0.5" />
              <div className="text-sm text-gray-700 dark:text-white/75">
                {t('channels.messenger.status.tokenOk')}
                <div className="text-[11px] text-gray-600 dark:text-white/45 mt-1">{t('channels.messenger.status.tokenNoRefreshHint')}</div>
              </div>
            </>
          )}
        </div>
      </SectionCard>

      <SectionCard title={t('channels.messenger.window.title')} icon={Activity} hint={t('channels.messenger.window.tagHint')}>
        <div className="flex items-center justify-between gap-3 flex-wrap">
          <WindowStatusBadge status={windowStatus} remaining={remaining} />
          <span className="text-xs text-gray-600 dark:text-white/45">{t('channels.messenger.window.progressLabel', { percent: windowHours })}</span>
        </div>
        <div className="h-2 rounded-full bg-white dark:bg-white/[0.08] overflow-hidden" role="progressbar" aria-valuemin={0} aria-valuemax={windowHours} aria-valuenow={remaining ?? 0}>
          <div
            className={`h-full transition-all ${
              windowStatus === 'ok' ? 'bg-emerald-400' : windowStatus === 'warning' ? 'bg-amber-400' : 'bg-red-400'
            }`}
            style={{ width: `${Math.max(0, Math.min(100, ((remaining ?? 0) / windowHours) * 100))}%` }}
          />
        </div>
        {status?.lastCustomerMessageAt ? (
          <div className="text-xs text-gray-600 dark:text-white/55">
            {t('channels.messenger.window.usage', {
              used: windowHours - (remaining ?? 0),
              remaining: remaining ?? 0,
            })}
            <span className="ml-2 text-gray-600 dark:text-white/40">
              {t('channels.messenger.window.lastInteraction', { time: formatDateTime(status.lastCustomerMessageAt) })}
            </span>
          </div>
        ) : (
          <div className="text-xs text-gray-600 dark:text-white/45">{t('channels.messenger.window.empty')}</div>
        )}
      </SectionCard>

      <FAQSection />
    </div>
  );
}

function WindowStatusBadge({ status, remaining }: { status: string; remaining?: number | null }) {
  const { t } = useI18n();
  const map: Record<string, { cls: string; key: TranslationKey }> = {
    ok: { cls: 'bg-emerald-500/15 border-emerald-500/25 text-emerald-300', key: 'channels.messenger.window.badgeOk' },
    warning: { cls: 'bg-amber-500/15 border-amber-500/25 text-amber-300', key: 'channels.messenger.window.badgeWarning' },
    expired: { cls: 'bg-red-500/15 border-red-500/25 text-red-300', key: 'channels.messenger.window.badgeExpired' },
    unknown: { cls: 'bg-white dark:bg-white/[0.06] border-gray-200 dark:border-white/10 text-gray-700 dark:text-white/60', key: 'channels.messenger.window.badgeUnknown' },
  };
  const cfg = map[status] ?? map.unknown;
  return (
    <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs border ${cfg.cls}`}>
      {status === 'ok' || status === 'warning'
        ? t(cfg.key, { hours: remaining ?? 0 })
        : t(cfg.key)}
    </span>
  );
}

function FAQSection() {
  const { t } = useI18n();
  const faqs: { questionKey: TranslationKey; answerKey: TranslationKey }[] = [
    { questionKey: 'channels.messenger.faq.token.question', answerKey: 'channels.messenger.faq.token.answer' },
    { questionKey: 'channels.messenger.faq.signature.question', answerKey: 'channels.messenger.faq.signature.answer' },
    { questionKey: 'channels.messenger.faq.window.question', answerKey: 'channels.messenger.faq.window.answer' },
    { questionKey: 'channels.messenger.faq.psid.question', answerKey: 'channels.messenger.faq.psid.answer' },
    { questionKey: 'channels.messenger.faq.media.question', answerKey: 'channels.messenger.faq.media.answer' },
    { questionKey: 'channels.messenger.faq.devmode.question', answerKey: 'channels.messenger.faq.devmode.answer' },
  ];
  return (
    <SectionCard title={t('channels.messenger.faq.title')} icon={Info}>
      <div className="space-y-3">
        {faqs.map((faq) => (
          <details key={faq.questionKey} className="group rounded-lg border border-gray-200 dark:border-white/[0.06] bg-gray-50 dark:bg-black/20">
            <summary className="cursor-pointer list-none px-4 py-3 text-sm text-gray-700 dark:text-white/80 flex items-center justify-between gap-3">
              {t(faq.questionKey)}
              <ChevronRight className="h-4 w-4 text-gray-600 dark:text-white/40 transition-transform group-open:rotate-90" />
            </summary>
            <div className="px-4 pb-3 text-xs text-gray-600 dark:text-white/55 leading-relaxed">{t(faq.answerKey)}</div>
          </details>
        ))}
      </div>
    </SectionCard>
  );
}

function IdCard({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <rect x="2" y="5" width="20" height="14" rx="2" />
      <circle cx="8" cy="12" r="2" />
      <path d="M13 10h5M13 14h5M5 16c.8-1.4 2-2 3-2s2.2.6 3 2" />
    </svg>
  );
}

function AtSign({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="4" />
      <path d="M16 8v5a3 3 0 0 0 6 0v-1a10 10 0 1 0-4 8" />
    </svg>
  );
}

function MailboxIcon({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M22 17a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h10v2" />
      <path d="M2 9h12v5H2z" />
      <path d="M17 9h3l2 3v5h-5" />
      <circle cx="7" cy="17" r="2" />
      <circle cx="17" cy="17" r="2" />
    </svg>
  );
}

export default MessengerPage;
