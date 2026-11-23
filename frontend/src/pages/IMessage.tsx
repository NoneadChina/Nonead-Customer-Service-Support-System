import { useState, useEffect, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { imessageApi } from '../api/modules/system';
import { mailboxApi } from '../api/modules/core';
import type {
  IMessageSettingsDto,
  IMessageStatusDto,
  UpdateIMessageSettingsRequest,
  IMessageTestResult,
} from '../api/types/system';
import type { MailboxResponse } from '../api/types';
import { useToast } from '../context/ToastContext';
import { useI18n } from '../context/I18nContext';
import { usePermission } from '../hooks/usePermission';
import type { TranslationKey } from '../locales/zh-Hans';
import { formatDateTime } from '../utils/format';
import {
  Save, Loader2, CheckCircle2, AlertCircle, Copy, Info, RefreshCw, Lock, Unlock,
  Send, Shield, Smartphone, Server, Key, Bell, Gauge, Eye, EyeOff, Clock,
  MessageSquare, AlertTriangle, ChevronDown, ChevronRight, Globe, TestTube,
} from 'lucide-react';

type TabType = 'config' | 'status';

/**
 * IMessageOptions 的三个模式码。后端 DTO（IMessageSettingsDto.mode / IMessageStatusDto.mode）都是裸 string，
 * 这里收窄成联合类型：未知值按「未设置」处理 ⇒ 不写进 form，保存时也就不会覆盖后端已有值。
 */
const IMESSAGE_MODES = ['bridge', 'amb_msp', 'amb_apple_direct'] as const;
type IMessageMode = (typeof IMESSAGE_MODES)[number];
const normalizeMode = (mode: string | null | undefined): IMessageMode | undefined =>
  IMESSAGE_MODES.find((m) => m === mode);

/**
 * iMessage / Apple Messages 渠道设置页。
 *
 * 一个抽象、两套传输（见 docs/integration/imessage.md）：
 *  - bridge（默认）：自建 macOS 上常开的 BlueBubbles REST 服务器 —— **该 macOS 主机不在本仓库内**；
 *  - amb_msp / amb_apple_direct：Apple Messages for Business，只有 Apple 核准的 MSP 才拿得到接入资格，
 *    本连接器不授予任何资格，默认关闭。
 */
export default function IMessagePage() {
  const { t } = useI18n();
  const toast = useToast();
  const { isReadOnly } = usePermission();
  const [searchParams, setSearchParams] = useSearchParams();

  const activeTab = (searchParams.get('tab') as TabType) || 'config';
  const handleTabChange = (tab: TabType) => setSearchParams({ tab });

  const [settings, setSettings] = useState<IMessageSettingsDto | null>(null);
  const [status, setStatus] = useState<IMessageStatusDto | null>(null);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<IMessageTestResult | null>(null);
  const [testHandle, setTestHandle] = useState('');
  const [registering, setRegistering] = useState(false);

  // ===== 表单状态（全部可空：只有改动过的字段才提交）=====
  const [form, setForm] = useState<UpdateIMessageSettingsRequest>({});
  const [editingPassword, setEditingPassword] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [editingApiKey, setEditingApiKey] = useState(false);
  const [showApiKey, setShowApiKey] = useState(false);
  const [editingWebhookSecret, setEditingWebhookSecret] = useState(false);
  const [showWebhookSecret, setShowWebhookSecret] = useState(false);
  const [editingBridgeWebhookSecret, setEditingBridgeWebhookSecret] = useState(false);
  const [showBridgeWebhookSecret, setShowBridgeWebhookSecret] = useState(false);
  const [editingMspSecret, setEditingMspSecret] = useState(false);
  const [showMspSecret, setShowMspSecret] = useState(false);
  const [advancedOpen, setAdvancedOpen] = useState(false);

  const loadAll = useCallback(async () => {
    setLoading(true);
    try {
      // api 模块内部已 .then(r => r.data)，这里直接拿到 DTO（与 WhatsApp.tsx 一致）
      const [loaded, statusData, mailboxData] = await Promise.all([
        imessageApi.getSettings(),
        imessageApi.getStatus(),
        mailboxApi.list().catch(() => [] as MailboxResponse[]),
      ]);
      setSettings(loaded);
      setStatus(statusData);
      setMailboxes(mailboxData ?? []);
      setForm({
        mode: normalizeMode(loaded.mode),
        enabled: loaded.enabled,
        baseUrl: loaded.baseUrl ?? '',
        authStyle: loaded.authStyle ?? 'bearer',
        webhookPath: loaded.webhookPath ?? '',
        webhookBaseUrl: loaded.webhookBaseUrl ?? '',
        sendMethod: loaded.sendMethod ?? 'apple-script',
        allowSmsFallback: loaded.allowSmsFallback,
        allowList: loaded.allowList ?? '',
        rateLimitPerMinute: loaded.rateLimitPerMinute,
        verifyTls: loaded.verifyTls,
        healthEndpoint: loaded.healthEndpoint ?? '',
        minServerVersion: loaded.minServerVersion ?? '',
        readHistoryOnStart: loaded.readHistoryOnStart,
        matchWindowHours: loaded.matchWindowHours,
        mailboxId: loaded.mailboxId,
        autoReplyEnabled: loaded.autoReplyEnabled,
        autoReplyMessage: loaded.autoReplyMessage ?? '',
        mspConnector: loaded.mspConnector ?? '',
        apiBaseUrl: loaded.apiBaseUrl ?? '',
        businessIdentifier: loaded.businessIdentifier ?? '',
        fallbackChannel: loaded.fallbackChannel ?? 'none',
        mspId: loaded.mspId ?? '',
        tokenTtlSeconds: loaded.tokenTtlSeconds,
        gatewayBaseUrl: loaded.gatewayBaseUrl ?? '',
        businessId: loaded.businessId ?? '',
        inboundPath: loaded.inboundPath ?? '',
        experimentalAppleDirect: loaded.experimentalAppleDirect,
        bridgeWebhookSecret: '',
      });
      setEditingPassword(false);
      setEditingApiKey(false);
      setEditingWebhookSecret(false);
      setEditingMspSecret(false);
      setEditingBridgeWebhookSecret(false);
    } catch {
      toast.error(t('channels.common.loadFailed', { channel: t('channels.imessage.channelName') }));
    } finally {
      setLoading(false);
    }
  }, [toast, t]);

  useEffect(() => {
    void loadAll();
  }, [loadAll]);

  const patch = (values: UpdateIMessageSettingsRequest) =>
    setForm((prev) => ({ ...prev, ...values }));

  const handleSave = async () => {
    setSaving(true);
    try {
      const payload: UpdateIMessageSettingsRequest = { ...form };
      // 密钥只在"确实编辑过"时提交，否则用 undefined 保留后端已有值
      payload.password = editingPassword ? form.password : undefined;
      payload.apiKey = editingApiKey ? form.apiKey : undefined;
      payload.webhookSecret = editingWebhookSecret ? form.webhookSecret : undefined;
      payload.mspSharedSecret = editingMspSecret ? form.mspSharedSecret : undefined;
      payload.bridgeWebhookSecret = editingBridgeWebhookSecret ? form.bridgeWebhookSecret : undefined;
      const saved = await imessageApi.updateSettings(payload);
      setSettings(saved);
      toast.success(t('channels.common.saveSucceeded', { channel: t('channels.imessage.channelName') }));
      await loadAll();
    } catch {
      toast.error(t('channels.common.saveFailed', { channel: t('channels.imessage.channelName') }));
    } finally {
      setSaving(false);
    }
  };

  const handleTest = async () => {
    setTesting(true);
    setTestResult(null);
    try {
      const res = await imessageApi.testConnection(testHandle ? { testHandle } : {});
      setTestResult(res);
    } catch {
      setTestResult({ success: false, errorMessage: t('channels.common.testConnectionFailed') });
    } finally {
      setTesting(false);
    }
  };

  const handleRegisterWebhook = async () => {
    setRegistering(true);
    try {
      const res = await imessageApi.registerWebhook();
      if (res.success) {
        toast.success(t('channels.imessage.registerWebhookSucceeded'));
        await loadAll();
      } else {
        toast.error(res.errorMessage ?? t('channels.imessage.registerWebhookFailed'));
      }
    } catch {
      toast.error(t('channels.imessage.registerWebhookFailed'));
    } finally {
      setRegistering(false);
    }
  };

  const handleCopyCallback = async () => {
    const url = settings?.callbackUrl ?? '';
    if (!url) {
      toast.error(t('channels.imessage.needBaseUrl'));
      return;
    }
    try {
      await navigator.clipboard.writeText(url);
      toast.success(t('channels.common.copyCallbackUrl'));
    } catch {
      toast.error(t('channels.common.copyFailed'));
    }
  };

  const mode: IMessageMode = form.mode ?? normalizeMode(settings?.mode) ?? 'bridge';
  const isBridge = mode === 'bridge';
  const isAmbMsp = mode === 'amb_msp';
  const isAmbDirect = mode === 'amb_apple_direct';

  if (loading) {
    return (
      <div className="flex h-64 items-center justify-center">
        <Loader2 className="h-6 w-6 animate-spin text-slate-400" />
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-5xl space-y-4 p-4">
      {/* ===== 头部 ===== */}
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-blue-600 via-sky-600 to-indigo-600 p-6 text-white shadow-[0_20px_60px_-35px_rgba(37,99,235,0.9)]">
        <div className="flex items-start justify-between gap-4">
          <div>
            <h1 className="flex items-center gap-2 text-2xl font-bold">
              <MessageSquare className="h-6 w-6" />
              {t('channels.imessage.title')}
            </h1>
            <p className="mt-1 max-w-2xl text-sm text-blue-50">{t('channels.imessage.description')}</p>
          </div>
          <div className="flex shrink-0 flex-col items-end gap-2 text-sm">
            <span className={`rounded-full px-3 py-1 ${settings?.configured ? 'bg-white/20' : 'bg-amber-400/30'}`}>
              {settings?.configured ? t('channels.common.configured') : t('channels.common.notConfigured')}
            </span>
            <span className="rounded-full bg-white/10 px-3 py-1">
              {t('channels.common.currentMode')}{t(modeKey(mode))}
            </span>
          </div>
        </div>
      </div>

      {/* ===== 诚实的前提告知（不要粉饰）===== */}
      <div className="flex items-start gap-2 rounded-2xl border border-amber-300/80 bg-amber-50/90 p-4 text-sm text-amber-900 dark:border-amber-600/40 dark:bg-amber-500/10 dark:text-amber-100">
        <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0" />
        <div className="space-y-1">
          <p className="font-semibold">{t('channels.imessage.prerequisitesTitle')}</p>
          <p>{t('channels.imessage.bridgeMacHostNotice')}</p>
          <p>{t('channels.imessage.bridgeUnofficialNotice')}</p>
          <p>{t('channels.imessage.ambEligibilityNotice')}</p>
        </div>
      </div>

      {/* ===== Tabs ===== */}
      <div className="flex gap-1 border-b border-slate-200 dark:border-slate-700">
        <TabButton active={activeTab === 'config'} onClick={() => handleTabChange('config')} icon={<Server className="h-4 w-4" />} label="channels.imessage.tabConfig" />
        <TabButton active={activeTab === 'status'} onClick={() => handleTabChange('status')} icon={<CheckCircle2 className="h-4 w-4" />} label="channels.imessage.tabStatus" />
      </div>

      {activeTab === 'config' && (
        <div className="space-y-4">
          {/* 模式选择 */}
          <Section titleKey="channels.imessage.modeSectionTitle" icon={<Gauge className="h-4 w-4" />}>
            <p className="mb-3 text-sm text-slate-500 dark:text-slate-400">{t('channels.imessage.modeHint')}</p>
            <div className="grid gap-3 sm:grid-cols-3">
              <ModeCard
                selected={isBridge}
                disabled={isReadOnly}
                onClick={() => patch({ mode: 'bridge' })}
                titleKey="channels.imessage.modeBridge"
                descKey="channels.imessage.modeBridgeDesc"
                recommended
              />
              <ModeCard
                selected={isAmbMsp}
                disabled={isReadOnly}
                onClick={() => patch({ mode: 'amb_msp' })}
                titleKey="channels.imessage.modeAmbMsp"
                descKey="channels.imessage.modeAmbMspDesc"
              />
              <ModeCard
                selected={isAmbDirect}
                disabled={isReadOnly}
                onClick={() => patch({ mode: 'amb_apple_direct' })}
                titleKey="channels.imessage.modeAmbDirect"
                descKey="channels.imessage.modeAmbDirectDesc"
                warning
              />
            </div>
          </Section>

          {/* 通用开关 */}
          <Section titleKey="channels.common.basicConfig" icon={<Shield className="h-4 w-4" />}>
            <ToggleSwitch
              checked={form.enabled ?? false}
              onChange={(v) => patch({ enabled: v })}
              disabled={isReadOnly}
              icon={<CheckCircle2 className="h-4 w-4" />}
              titleKey="channels.common.enableChannel"
              descriptionKey="channels.imessage.enableHint"
            />
            <div className="mt-4 grid gap-4 sm:grid-cols-2">
              <FormField labelKey="channels.common.linkedMailbox" hintKey="channels.imessage.mailboxHint">
                <select
                  value={form.mailboxId ?? ''}
                  disabled={isReadOnly}
                  onChange={(e) => patch({ mailboxId: e.target.value ? Number(e.target.value) : undefined })}
                  className={inputClass}
                >
                  <option value="">{t('channels.imessage.autoAssignMailbox')}</option>
                  {mailboxes.map((m) => (
                    <option key={m.id} value={m.id}>{m.name || m.email || m.id}</option>
                  ))}
                </select>
              </FormField>
              <FormField labelKey="channels.imessage.matchWindowHours" hintKey="channels.imessage.matchWindowHint">
                <input
                  type="number"
                  min={1}
                  max={8760}
                  value={form.matchWindowHours ?? ''}
                  disabled={isReadOnly}
                  onChange={(e) => patch({ matchWindowHours: e.target.value ? Number(e.target.value) : undefined })}
                  className={inputClass}
                />
              </FormField>
            </div>
            <p className="mt-3 flex items-start gap-1.5 text-xs text-slate-500 dark:text-slate-400">
              <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
              <span>{t('channels.imessage.sessionSemanticsNotice')}</span>
            </p>
          </Section>

          {/* bridge 参数 */}
          {isBridge && (
            <Section titleKey="channels.imessage.bridgeSection" icon={<Smartphone className="h-4 w-4" />}>
              <div className="grid gap-4 sm:grid-cols-2">
                <FormField labelKey="channels.imessage.baseUrl" hintKey="channels.imessage.baseUrlHint" required>
                  <input
                    type="text"
                    placeholder="http://127.0.0.1:1234"
                    value={form.baseUrl ?? ''}
                    disabled={isReadOnly}
                    onChange={(e) => patch({ baseUrl: e.target.value })}
                    className={inputClass}
                  />
                </FormField>
                <FormField labelKey="channels.imessage.authStyle" hintKey="channels.imessage.authStyleHint">
                  <select
                    value={form.authStyle ?? 'bearer'}
                    disabled={isReadOnly}
                    onChange={(e) => patch({ authStyle: e.target.value })}
                    className={inputClass}
                  >
                    <option value="bearer">{t('channels.imessage.authBearer')}</option>
                    <option value="query">{t('channels.imessage.authQuery')}</option>
                  </select>
                </FormField>
                <FormField labelKey="channels.imessage.password" hintKey="channels.imessage.passwordHint" required>
                  <SecretInput
                    value={form.password ?? ''}
                    hasExisting={!!settings?.hasPassword}
                    editing={editingPassword}
                    setEditing={setEditingPassword}
                    show={showPassword}
                    setShow={setShowPassword}
                    onChange={(v) => patch({ password: v })}
                    disabled={isReadOnly}
                    placeholder="••••••••"
                  />
                </FormField>
                <FormField
                  labelKey="channels.imessage.bridgeWebhookSecret"
                  hintKey="channels.imessage.bridgeWebhookSecretHint"
                >
                  <SecretInput
                    value={form.bridgeWebhookSecret ?? ''}
                    hasExisting={!!settings?.hasBridgeWebhookSecret}
                    editing={editingBridgeWebhookSecret}
                    setEditing={setEditingBridgeWebhookSecret}
                    show={showBridgeWebhookSecret}
                    setShow={setShowBridgeWebhookSecret}
                    onChange={(v) => patch({ bridgeWebhookSecret: v })}
                    disabled={isReadOnly}
                    placeholder="••••••••"
                  />
                </FormField>
                <FormField labelKey="channels.imessage.sendMethod" hintKey="channels.imessage.sendMethodHint">
                  <select
                    value={form.sendMethod ?? 'apple-script'}
                    disabled={isReadOnly}
                    onChange={(e) => patch({ sendMethod: e.target.value })}
                    className={inputClass}
                  >
                    <option value="apple-script">{t('channels.imessage.sendAppleScript')}</option>
                    <option value="private-api">{t('channels.imessage.sendPrivateApi')}</option>
                    <option value="any">{t('channels.imessage.sendAny')}</option>
                  </select>
                </FormField>
                <FormField labelKey="channels.imessage.webhookPath" hintKey="channels.imessage.webhookPathHint">
                  <div className="flex gap-2">
                    <input
                      type="text"
                      value={form.webhookPath ?? ''}
                      disabled={isReadOnly}
                      onChange={(e) => patch({ webhookPath: e.target.value })}
                      className={inputClass}
                    />
                    <button type="button" onClick={() => void handleCopyCallback()} disabled={isReadOnly}
                      title={t('channels.common.copyCallbackUrl')}
                      className="rounded-lg border border-slate-300 bg-white p-2 text-slate-500 shadow-sm transition-colors hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500/40 dark:border-slate-600 dark:bg-slate-800 dark:shadow-none dark:hover:bg-slate-700">
                      <Copy className="h-4 w-4" />
                    </button>
                  </div>
                </FormField>
                <FormField labelKey="channels.imessage.webhookBaseUrl" hintKey="channels.imessage.webhookBaseUrlHint">
                  <input
                    type="text"
                    placeholder="https://css.example.com"
                    value={form.webhookBaseUrl ?? ''}
                    disabled={isReadOnly}
                    onChange={(e) => patch({ webhookBaseUrl: e.target.value })}
                    className={inputClass}
                  />
                </FormField>
                <FormField labelKey="channels.imessage.healthEndpoint">
                  <input type="text" value={form.healthEndpoint ?? ''} disabled={isReadOnly}
                    onChange={(e) => patch({ healthEndpoint: e.target.value })} className={inputClass} />
                </FormField>
                <FormField labelKey="channels.imessage.minServerVersion" hintKey="channels.imessage.minServerVersionHint">
                  <input type="text" value={form.minServerVersion ?? ''} disabled={isReadOnly}
                    onChange={(e) => patch({ minServerVersion: e.target.value })} className={inputClass} />
                </FormField>
                <FormField labelKey="channels.imessage.rateLimitPerMinute" hintKey="channels.imessage.rateLimitHint">
                  <input type="number" min={1} max={1000} value={form.rateLimitPerMinute ?? ''} disabled={isReadOnly}
                    onChange={(e) => patch({ rateLimitPerMinute: e.target.value ? Number(e.target.value) : undefined })}
                    className={inputClass} />
                </FormField>
                <FormField labelKey="channels.imessage.allowList" hintKey="channels.imessage.allowListHint">
                  <input type="text" placeholder="+15551234567, user@example.com" value={form.allowList ?? ''} disabled={isReadOnly}
                    onChange={(e) => patch({ allowList: e.target.value })} className={inputClass} />
                </FormField>
              </div>

              <div className="mt-4 space-y-3">
                <ToggleSwitch checked={form.allowSmsFallback ?? false} onChange={(v) => patch({ allowSmsFallback: v })}
                  disabled={isReadOnly} icon={<AlertTriangle className="h-4 w-4" />}
                  titleKey="channels.imessage.allowSmsFallback" descriptionKey="channels.imessage.allowSmsFallbackHint" />
                <ToggleSwitch checked={form.verifyTls ?? true} onChange={(v) => patch({ verifyTls: v })}
                  disabled={isReadOnly} icon={<Shield className="h-4 w-4" />}
                  titleKey="channels.imessage.verifyTls" descriptionKey="channels.imessage.verifyTlsHint" />
                <ToggleSwitch checked={form.readHistoryOnStart ?? false} onChange={(v) => patch({ readHistoryOnStart: v })}
                  disabled={isReadOnly} icon={<Clock className="h-4 w-4" />}
                  titleKey="channels.imessage.readHistoryOnStart" descriptionKey="channels.imessage.readHistoryHint" />
              </div>

              <button
                type="button"
                onClick={() => setAdvancedOpen((v) => !v)}
                className="mt-4 flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700"
              >
                {advancedOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                {t('channels.imessage.webhookRegistration')}
              </button>
              {advancedOpen && (
                <div className="mt-2 rounded-xl border border-slate-200 bg-slate-50/60 p-3 dark:border-slate-700 dark:bg-slate-800/60">
                  <p className="mb-2 text-sm text-slate-500 dark:text-slate-400">{t('channels.imessage.webhookRegistrationHint')}</p>
                  <button
                    type="button"
                    onClick={() => void handleRegisterWebhook()}
                    disabled={isReadOnly || registering || !settings?.configured}
                    className="flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm shadow-sm transition-colors hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500/40 dark:border-slate-600 dark:bg-slate-800 dark:shadow-none dark:hover:bg-slate-700 disabled:opacity-50"
                  >
                    {registering ? <Loader2 className="h-4 w-4 animate-spin" /> : <Bell className="h-4 w-4" />}
                    {t('channels.imessage.registerWebhook')}
                  </button>
                </div>
              )}
            </Section>
          )}

          {/* amb_msp 参数 */}
          {isAmbMsp && (
            <Section titleKey="channels.imessage.ambMspSection" icon={<Globe className="h-4 w-4" />}>
              <p className="mb-3 flex items-start gap-1.5 text-sm text-amber-700 dark:text-amber-300">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                <span>{t('channels.imessage.ambMspHint')}</span>
              </p>
              <div className="grid gap-4 sm:grid-cols-2">
                <FormField labelKey="channels.imessage.mspConnector" hintKey="channels.imessage.mspConnectorHint">
                  <input type="text" placeholder="your-msp-name" value={form.mspConnector ?? ''} disabled={isReadOnly}
                    onChange={(e) => patch({ mspConnector: e.target.value })} className={inputClass} />
                </FormField>
                <FormField labelKey="channels.imessage.apiBaseUrl" required>
                  <input type="text" placeholder="https://msp.example.com/api" value={form.apiBaseUrl ?? ''} disabled={isReadOnly}
                    onChange={(e) => patch({ apiBaseUrl: e.target.value })} className={inputClass} />
                </FormField>
                <FormField labelKey="channels.imessage.businessIdentifier" hintKey="channels.imessage.businessIdentifierHint" required>
                  <input type="text" placeholder="urn:biz:…" value={form.businessIdentifier ?? ''} disabled={isReadOnly}
                    onChange={(e) => patch({ businessIdentifier: e.target.value })} className={inputClass} />
                </FormField>
                <FormField labelKey="channels.imessage.fallbackChannel" hintKey="channels.imessage.fallbackChannelHint">
                  <select value={form.fallbackChannel ?? 'none'} disabled={isReadOnly}
                    onChange={(e) => patch({ fallbackChannel: e.target.value })} className={inputClass}>
                    <option value="none">{t('channels.imessage.fallbackNone')}</option>
                    <option value="sms">{t('channels.imessage.fallbackSms')}</option>
                  </select>
                </FormField>
                <FormField labelKey="channels.imessage.apiKey" hintKey="channels.imessage.apiKeyHint">
                  <SecretInput value={form.apiKey ?? ''} hasExisting={!!settings?.hasApiKey} editing={editingApiKey}
                    setEditing={setEditingApiKey} show={showApiKey} setShow={setShowApiKey}
                    onChange={(v) => patch({ apiKey: v })} disabled={isReadOnly} placeholder="••••••••" />
                </FormField>
                <FormField labelKey="channels.imessage.webhookSecret" hintKey="channels.imessage.webhookSecretHint">
                  <SecretInput value={form.webhookSecret ?? ''} hasExisting={!!settings?.hasWebhookSecret}
                    editing={editingWebhookSecret} setEditing={setEditingWebhookSecret} show={showWebhookSecret}
                    setShow={setShowWebhookSecret} onChange={(v) => patch({ webhookSecret: v })}
                    disabled={isReadOnly} placeholder="••••••••" />
                </FormField>
              </div>
            </Section>
          )}

          {/* amb_apple_direct 参数 */}
          {isAmbDirect && (
            <Section titleKey="channels.imessage.ambDirectSection" icon={<Key className="h-4 w-4" />}>
              <p className="mb-3 flex items-start gap-1.5 text-sm text-red-700 dark:text-red-300">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                <span>{t('channels.imessage.ambDirectWarning')}</span>
              </p>
              <ToggleSwitch checked={form.experimentalAppleDirect ?? false}
                onChange={(v) => patch({ experimentalAppleDirect: v })} disabled={isReadOnly}
                icon={<AlertTriangle className="h-4 w-4" />}
                titleKey="channels.imessage.experimentalAppleDirect"
                descriptionKey="channels.imessage.experimentalAppleDirectHint" />
              <div className="mt-4 grid gap-4 sm:grid-cols-2">
                <FormField labelKey="channels.imessage.mspId" hintKey="channels.imessage.mspIdHint" required>
                  <input type="text" value={form.mspId ?? ''} disabled={isReadOnly}
                    onChange={(e) => patch({ mspId: e.target.value })} className={inputClass} />
                </FormField>
                <FormField labelKey="channels.imessage.mspSharedSecret" hintKey="channels.imessage.mspSharedSecretHint" required>
                  <SecretInput value={form.mspSharedSecret ?? ''} hasExisting={!!settings?.hasMspSharedSecret}
                    editing={editingMspSecret} setEditing={setEditingMspSecret} show={showMspSecret}
                    setShow={setShowMspSecret} onChange={(v) => patch({ mspSharedSecret: v })}
                    disabled={isReadOnly} placeholder="••••••••" />
                </FormField>
                <FormField labelKey="channels.imessage.gatewayBaseUrl" hintKey="channels.imessage.gatewayBaseUrlHint">
                  <input type="text" placeholder="https://mspgw.push.apple.com" value={form.gatewayBaseUrl ?? ''} disabled={isReadOnly}
                    onChange={(e) => patch({ gatewayBaseUrl: e.target.value })} className={inputClass} />
                </FormField>
                <FormField labelKey="channels.imessage.businessId" hintKey="channels.imessage.businessIdHint" required>
                  <input type="text" placeholder="urn:biz:…" value={form.businessId ?? ''} disabled={isReadOnly}
                    onChange={(e) => patch({ businessId: e.target.value })} className={inputClass} />
                </FormField>
                <FormField labelKey="channels.imessage.tokenTtlSeconds" hintKey="channels.imessage.tokenTtlHint">
                  <input type="number" min={60} max={3600} value={form.tokenTtlSeconds ?? ''} disabled={isReadOnly}
                    onChange={(e) => patch({ tokenTtlSeconds: e.target.value ? Number(e.target.value) : undefined })}
                    className={inputClass} />
                </FormField>
                <FormField labelKey="channels.imessage.inboundPath">
                  <input type="text" placeholder="/message" value={form.inboundPath ?? ''} disabled={isReadOnly}
                    onChange={(e) => patch({ inboundPath: e.target.value })} className={inputClass} />
                </FormField>
              </div>
              <p className="mt-3 flex items-start gap-1.5 text-xs text-slate-500 dark:text-slate-400">
                <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                <span>{t('channels.imessage.attachmentCryptoNotice')}</span>
              </p>
            </Section>
          )}

          {/* 自动回复 */}
          <Section titleKey="channels.imessage.autoReplySection" icon={<MessageSquare className="h-4 w-4" />}>
            <ToggleSwitch checked={form.autoReplyEnabled ?? false} onChange={(v) => patch({ autoReplyEnabled: v })}
              disabled={isReadOnly} icon={<Send className="h-4 w-4" />}
              titleKey="channels.common.enableAutoReply" descriptionKey="channels.imessage.autoReplyHint" />
            <div className="mt-3">
              <textarea
                rows={3}
                maxLength={500}
                placeholder={t('channels.common.autoReplyPlaceholder')}
                value={form.autoReplyMessage ?? ''}
                disabled={isReadOnly}
                onChange={(e) => patch({ autoReplyMessage: e.target.value })}
                className={inputClass}
              />
              <p className="mt-1 text-right text-xs text-slate-400" title={t('channels.common.charCountTitle')}>
                {(form.autoReplyMessage ?? '').length}/500
              </p>
            </div>
          </Section>

          {/* 保存条 */}
          <div className="sticky bottom-0 flex items-center gap-2 rounded-xl border border-slate-200 bg-white/95 p-3 shadow-sm backdrop-blur dark:border-slate-700 dark:bg-slate-900/95">
            <button
              type="button"
              onClick={() => void handleSave()}
              disabled={isReadOnly || saving}
              className="flex items-center gap-1.5 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-blue-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500/40 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
              {t('channels.common.saveConfig')}
            </button>
            <button
              type="button"
              onClick={() => void loadAll()}
              className="flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm shadow-sm transition-colors hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500/40 dark:border-slate-600 dark:bg-slate-800 dark:shadow-none dark:hover:bg-slate-700"
            >
              <RefreshCw className="h-4 w-4" />
              {t('channels.common.refresh')}
            </button>
            {isReadOnly && <span className="ml-auto text-xs text-slate-400">{t('channels.imessage.readOnlyHint')}</span>}
          </div>
        </div>
      )}

      {activeTab === 'status' && (
        <div className="space-y-4">
          <Section titleKey="channels.imessage.statusSection" icon={<CheckCircle2 className="h-4 w-4" />}>
            <div className="grid gap-3 sm:grid-cols-2">
              <StatusRow labelKey="channels.common.configured" value={status?.configured ? t('channels.common.configured') : t('channels.common.notConfigured')} ok={!!status?.configured} />
              <StatusRow labelKey="channels.imessage.modeActive" value={t(modeKey(status?.mode ?? mode))} ok={!!(status?.configured && status?.enabled)} />
              <StatusRow labelKey="channels.common.apiReachable" value={status?.apiReachable ? t('channels.common.on') : t('channels.common.off')} ok={!!status?.apiReachable} />
              <StatusRow labelKey="channels.imessage.serverVersion" value={status?.serverVersion ?? (isBridge ? t('channels.imessage.unknown') : t('channels.imessage.notApplicable'))} ok={!!status?.serverVersion} />
              <StatusRow labelKey="channels.imessage.registeredWebhook" value={status?.registeredWebhookUrl ?? (isBridge ? t('channels.imessage.none') : t('channels.imessage.notApplicable'))} ok={!!status?.registeredWebhookUrl} />
              <StatusRow labelKey="channels.imessage.lastCustomerMessage" value={status?.lastCustomerMessageAt ? formatDateTime(status.lastCustomerMessageAt) : t('channels.imessage.none')} ok={!!status?.lastCustomerMessageAt} />
            </div>
            <div className="mt-3">
              <FormField labelKey="channels.common.callbackAddress" hintKey="channels.common.needPublicCallbackUrl">
                <div className="flex gap-2">
                  <input type="text" readOnly value={status?.callbackUrl ?? settings?.callbackUrl ?? ''} className={inputClass} />
                  <button type="button" onClick={() => void handleCopyCallback()} title={t('channels.common.copyCallbackUrl')}
                    className="rounded-lg border border-slate-300 bg-white p-2 text-slate-500 shadow-sm transition-colors hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500/40 dark:border-slate-600 dark:bg-slate-800 dark:shadow-none dark:hover:bg-slate-700">
                    <Copy className="h-4 w-4" />
                  </button>
                </div>
              </FormField>
            </div>
          </Section>

          <Section titleKey="channels.imessage.testSection" icon={<TestTube className="h-4 w-4" />}>
            <div className="flex flex-wrap items-end gap-3">
              <FormField labelKey="channels.imessage.testHandle" hintKey="channels.imessage.testHandleHint">
                <input type="text" placeholder="+15551234567" value={testHandle} disabled={isReadOnly || !isBridge}
                  onChange={(e) => setTestHandle(e.target.value)} className={inputClass} />
              </FormField>
              <button
                type="button"
                onClick={() => void handleTest()}
                disabled={testing}
                className="flex items-center gap-1.5 rounded-lg bg-slate-800 px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-slate-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-slate-500/40 disabled:opacity-50 dark:bg-slate-700 dark:hover:bg-slate-600"
              >
                {testing ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
                {testing ? t('channels.common.testing') : t('channels.common.testConnection')}
              </button>
            </div>
            {testResult && (
              <div className={`mt-3 flex items-start gap-2 rounded-md p-3 text-sm ${testResult.success ? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200' : 'bg-red-50 text-red-800 dark:bg-red-950 dark:text-red-200'}`}>
                {testResult.success ? <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" /> : <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />}
                <div>
                  <p>{testResult.success ? t('channels.imessage.testSucceeded') : (testResult.errorMessage ?? t('channels.common.testConnectionFailed'))}</p>
                  {testResult.messageId && <p className="mt-1 text-xs opacity-70">id: {testResult.messageId}</p>}
                  {testResult.serverVersion && <p className="mt-1 text-xs opacity-70">server: {testResult.serverVersion}</p>}
                </div>
              </div>
            )}
            {!isBridge && (
              <p className="mt-2 text-xs text-slate-500 dark:text-slate-400">{t('channels.imessage.ambTestNotice')}</p>
            )}
          </Section>

          <Section titleKey="channels.imessage.risksSection" icon={<Shield className="h-4 w-4" />}>
            <ul className="list-disc space-y-2 pl-5 text-sm text-slate-600 dark:text-slate-300">
              <li>{t('channels.imessage.riskE2E')}</li>
              <li>{t('channels.imessage.riskCompliance')}</li>
              <li>{t('channels.imessage.riskRateLimit')}</li>
              <li>{t('channels.imessage.riskNaming')}</li>
            </ul>
          </Section>
        </div>
      )}
    </div>
  );
}

// ===== 文件内小组件（与 WhatsApp.tsx 同构，未导出：新页面各自声明）=====

const inputClass =
  'w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 disabled:bg-slate-50 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100 dark:disabled:bg-slate-900';

function modeKey(mode: string): TranslationKey {
  if (mode === 'amb_msp') return 'channels.imessage.modeAmbMsp';
  if (mode === 'amb_apple_direct') return 'channels.imessage.modeAmbDirect';
  return 'channels.imessage.modeBridge';
}

function TabButton({ active, onClick, icon, label }: { active: boolean; onClick: () => void; icon: React.ReactNode; label: TranslationKey }) {
  const { t } = useI18n();
  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex items-center gap-1.5 border-b-2 px-4 py-2 text-sm font-medium ${
        active
          ? 'border-blue-600 text-blue-600'
          : 'border-transparent text-slate-500 hover:text-slate-700 dark:text-slate-400'
      }`}
    >
      {icon}
      {t(label)}
    </button>
  );
}

function Section({ titleKey, icon, children }: { titleKey: TranslationKey; icon: React.ReactNode; children: React.ReactNode }) {
  const { t } = useI18n();
  return (
    <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition-colors hover:border-slate-300 dark:border-slate-700 dark:bg-slate-900 dark:shadow-none dark:hover:border-slate-600">
      <h2 className="mb-3 flex items-center gap-2 text-base font-semibold text-slate-800 dark:text-slate-100">
        {icon}
        {t(titleKey)}
      </h2>
      {children}
    </section>
  );
}

function FormField({ labelKey, hintKey, required, children }: { labelKey: TranslationKey; hintKey?: TranslationKey; required?: boolean; children: React.ReactNode }) {
  const { t } = useI18n();
  return (
    <div>
      <label className="mb-1 block text-sm font-medium text-slate-700 dark:text-slate-200">
        {t(labelKey)}
        {required && <span className="ml-1 text-red-500">*</span>}
      </label>
      {children}
      {hintKey && <p className="mt-1 text-xs text-slate-500 dark:text-slate-400">{t(hintKey)}</p>}
    </div>
  );
}

function SecretInput(props: {
  value: string;
  placeholder?: string;
  hasExisting: boolean;
  editing: boolean;
  setEditing: (v: boolean) => void;
  show: boolean;
  setShow: (v: boolean) => void;
  onChange: (v: string) => void;
  disabled?: boolean;
}) {
  const { t } = useI18n();
  const { value, placeholder, hasExisting, editing, setEditing, show, setShow, onChange, disabled } = props;
  if (hasExisting && !editing) {
    return (
      <div className="flex items-center gap-2 rounded-md border border-slate-300 bg-slate-50 px-3 py-2 dark:border-slate-600 dark:bg-slate-800">
        <Lock className="h-4 w-4 text-slate-400" />
        <span className="flex-1 text-sm text-slate-500">{t('channels.common.maskedConfigured')}</span>
        <button type="button" onClick={() => setEditing(true)} disabled={disabled}
          className="flex items-center gap-1 text-xs text-blue-600 hover:underline">
          <Unlock className="h-3.5 w-3.5" />
          {t('channels.common.editSecret')}
        </button>
      </div>
    );
  }
  return (
    <div className="flex items-center gap-2">
      <input
        type={show ? 'text' : 'password'}
        value={value}
        placeholder={placeholder}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
        className={inputClass}
      />
      <button type="button" onClick={() => setShow(!show)} disabled={disabled}
        title={show ? t('channels.common.hide') : t('channels.common.show')}
        className="rounded-lg border border-slate-300 bg-white p-2 text-slate-500 shadow-sm transition-colors hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500/40 dark:border-slate-600 dark:bg-slate-800 dark:shadow-none dark:hover:bg-slate-700">
        {show ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
      </button>
      {hasExisting && (
        <button type="button" onClick={() => setEditing(false)} disabled={disabled}
          className="rounded-lg border border-slate-300 bg-white p-2 text-slate-500 shadow-sm transition-colors hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500/40 dark:border-slate-600 dark:bg-slate-800 dark:shadow-none dark:hover:bg-slate-700"
          title={t('channels.common.lockSecret')}>
          <Lock className="h-4 w-4" />
        </button>
      )}
    </div>
  );
}

function ToggleSwitch(props: {
  checked: boolean;
  onChange: (v: boolean) => void;
  disabled?: boolean;
  icon?: React.ReactNode;
  titleKey: TranslationKey;
  descriptionKey: TranslationKey;
}) {
  const { t } = useI18n();
  const { checked, onChange, disabled, icon, titleKey, descriptionKey } = props;
  return (
    <label className="flex cursor-pointer items-start gap-3">
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className={`relative mt-0.5 h-6 w-11 shrink-0 rounded-full transition-colors ${
          checked ? 'bg-blue-600' : 'bg-slate-300 dark:bg-slate-600'
        } disabled:opacity-50`}
      >
        <span className={`absolute left-1 top-1 h-4 w-4 rounded-full bg-white transition-transform ${
          checked ? 'translate-x-5' : 'translate-x-0'
        }`} />
      </button>
      <span className="text-sm">
        <span className="flex items-center gap-1.5 font-medium text-slate-700 dark:text-slate-200">
          {icon}
          {t(titleKey, { channel: t('channels.imessage.channelName') })}
        </span>
        <span className="mt-0.5 block text-xs text-slate-500 dark:text-slate-400">{t(descriptionKey)}</span>
      </span>
    </label>
  );
}

function ModeCard(props: {
  selected: boolean;
  disabled?: boolean;
  onClick: () => void;
  titleKey: TranslationKey;
  descKey: TranslationKey;
  recommended?: boolean;
  warning?: boolean;
}) {
  const { t } = useI18n();
  const { selected, disabled, onClick, titleKey, descKey, recommended, warning } = props;
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={`rounded-lg border p-3 text-left transition-colors ${
        selected
          ? 'border-blue-500 bg-blue-50 ring-1 ring-blue-500 dark:bg-blue-950'
          : 'border-slate-200 hover:border-slate-300 dark:border-slate-700 dark:hover:border-slate-600'
      } disabled:opacity-60`}
    >
      <div className="flex items-center gap-2">
        <span className="text-sm font-semibold text-slate-800 dark:text-slate-100">{t(titleKey)}</span>
        {recommended && <span className="rounded bg-emerald-100 px-1.5 py-0.5 text-[10px] font-medium text-emerald-700 dark:bg-emerald-900 dark:text-emerald-200">{t('channels.imessage.recommended')}</span>}
        {warning && <AlertTriangle className="h-3.5 w-3.5 text-amber-500" />}
      </div>
      <p className="mt-1 text-xs text-slate-500 dark:text-slate-400">{t(descKey)}</p>
    </button>
  );
}

function StatusRow({ labelKey, value, ok }: { labelKey: TranslationKey; value: string; ok: boolean }) {
  const { t } = useI18n();
  return (
    <div className="flex items-center justify-between rounded-xl border border-slate-200 bg-slate-50/60 px-3 py-2 text-sm dark:border-slate-700 dark:bg-slate-800/60">
      <span className="text-slate-500 dark:text-slate-400">{t(labelKey)}</span>
      <span className={`flex items-center gap-1.5 font-medium ${ok ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-600 dark:text-slate-300'}`}>
        {ok && <CheckCircle2 className="h-4 w-4" />}
        {value}
      </span>
    </div>
  );
}
