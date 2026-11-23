import { useState, useEffect, useCallback } from 'react';
import { lineApi, mailboxApi } from '../api';
import type { LineSettingsDto, LineStatusDto, UpdateLineSettingsRequest, MailboxResponse } from '../api/types';
import { useToast } from '../context/ToastContext';
import { useI18n } from '../context/I18nContext';
import { usePermission } from '../hooks/usePermission';
import { formatDateTime } from '../utils/format';
import {
  Save, Loader2, CheckCircle2, AlertCircle, Copy, RefreshCw, Eye, EyeOff, Settings,
  MessageSquare, Image as ImageIcon, FileText, Mic, Video, MapPin, Zap, Info, ShieldAlert,
  Send, Link2, Gauge, BellOff,
} from 'lucide-react';

/**
 * LINE Messaging API 渠道设置页。
 *
 * 与其它渠道页的三个结构性差异（界面上都写明原因）：
 *  1) Channel Secret / Channel Access Token **永不回显**：后端 `LineSettingsDto`
 *     （NoneadCSS.Application/DTOs/Responses/LineDto.cs）只给 `hasChannelSecret` / `hasChannelAccessToken`
 *     两个布尔，所以这里是"只写不读"的输入框，留空 = 不修改。
 *  2) LINE **没有聊天媒体上传接口** ⇒ 出站附件必须由本部署的公网 HTTPS 签名直链交付
 *     （`AttachmentPublicBaseUrl` + GET /api/line/media/{id}?exp=&sig=）。
 *  3) 回复优先用一次性 replyToken（约 25s，不消耗月度配额），过期回落 push（耗配额、只对好友可达，
 *     404/403 会被判为不可达）⇒ 配额与不可达状态必须在状态卡里看得见。
 *
 * i18n 命名空间用 **小写** `channels.line.*`；后端模块名/权限名是 `line`
 * （`[RequireModule("line")]` + `line.view` / `line.manage`，见 NoneadCSS.Api/Controllers/LineController.cs）。
 */

function LineLogo({ className = 'w-7 h-7' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="currentColor" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <path d="M12 3c-4.97 0-9 3.13-9 7 0 3.45 3.02 6.35 7.03 6.87.27.06.65.18.74.41.09.23.06.6.03.84l-.12.74c-.03.24-.18.93.81.51 1-.49 5.28-3.48 7.2-5.95C20.14 12.72 21 11.4 21 10c0-3.87-4.03-7-9-7z" />
    </svg>
  );
}

/** 入站可识别的消息形状；文案字段一律持有 key（漏包 t() 会被 tsc 抓出） */
const MESSAGE_TYPES = [
  { icon: MessageSquare, labelKey: 'channels.common.msgType.text', descKey: 'channels.common.msgType.textDesc' },
  { icon: ImageIcon, labelKey: 'channels.common.msgType.image', descKey: 'channels.common.msgType.imageDesc' },
  { icon: Video, labelKey: 'channels.common.msgType.video', descKey: 'channels.common.msgType.videoDesc' },
  { icon: Mic, labelKey: 'channels.common.msgType.audio', descKey: 'channels.common.msgType.audioDesc' },
  { icon: FileText, labelKey: 'channels.common.msgType.file', descKey: 'channels.common.msgType.fileDesc' },
  { icon: MapPin, labelKey: 'channels.line.msgType.location', descKey: 'channels.line.msgType.locationDesc' },
  { icon: Zap, labelKey: 'channels.line.msgType.sticker', descKey: 'channels.line.msgType.stickerDesc' },
] as const;

/**
 * 数字参数：min/max 与 LineRequests.cs 里 UpdateLineSettingsRequest 的 [Range] 注解一致。
 * 审计 #12：后四项（状态检测超时 / 发送重试 / 入站与出站附件上限）后端一直可调，
 * 但没进 DTO 与表单，此前界面调不了；字节上限按 DTO 的 1–209715200（200 MB）收。
 */
const NUMBER_FIELDS = [
  { key: 'textChunkChars', labelKey: 'channels.line.fields.textChunkChars', hintKey: 'channels.line.fields.textChunkCharsHint', min: 500, max: 5000 },
  { key: 'maxMessagesPerRequest', labelKey: 'channels.line.fields.maxMessagesPerRequest', hintKey: 'channels.line.fields.maxMessagesPerRequestHint', min: 1, max: 5 },
  { key: 'replyTokenTtlSeconds', labelKey: 'channels.line.fields.replyTokenTtlSeconds', hintKey: 'channels.line.fields.replyTokenTtlSecondsHint', min: 1, max: 60 },
  { key: 'humanHandoverMuteMinutes', labelKey: 'channels.line.fields.humanHandoverMuteMinutes', hintKey: 'channels.line.fields.humanHandoverMuteMinutesHint', min: 0, max: 1440 },
  { key: 'quotaWarnPercent', labelKey: 'channels.line.fields.quotaWarnPercent', hintKey: 'channels.line.fields.quotaWarnPercentHint', min: 1, max: 100 },
  { key: 'attachmentPublicUrlTtlMinutes', labelKey: 'channels.line.fields.attachmentTtlMinutes', hintKey: 'channels.line.fields.attachmentTtlMinutesHint', min: 5, max: 100800 },
  { key: 'statusCheckTimeoutSeconds', labelKey: 'channels.line.fields.statusCheckTimeoutSeconds', hintKey: 'channels.line.fields.statusCheckTimeoutSecondsHint', min: 1, max: 60 },
  { key: 'retryMaxAttempts', labelKey: 'channels.line.fields.retryMaxAttempts', hintKey: 'channels.line.fields.retryMaxAttemptsHint', min: 0, max: 5 },
  { key: 'maxInboundMediaBytes', labelKey: 'channels.line.fields.maxInboundMediaBytes', hintKey: 'channels.line.fields.maxInboundMediaBytesHint', min: 1, max: 209715200 },
  { key: 'maxOutboundAttachmentBytes', labelKey: 'channels.line.fields.maxOutboundAttachmentBytes', hintKey: 'channels.line.fields.maxOutboundAttachmentBytesHint', min: 1, max: 209715200 },
] as const;

function StatusRow({ label, ok, value }: { label: string; ok?: boolean | null; value?: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3 py-1.5 text-sm">
      <span className="text-gray-600 dark:text-slate-300 truncate">{label}</span>
      <span className="flex items-center gap-1.5 flex-shrink-0 min-w-0">
        {ok === true && <CheckCircle2 className="w-4 h-4 text-green-500 flex-shrink-0" />}
        {ok === false && <AlertCircle className="w-4 h-4 text-red-500 flex-shrink-0" />}
        {value}
      </span>
    </div>
  );
}

function ToggleRow({ label, hint, checked, onChange, disabled }: { label: string; hint?: string; checked: boolean; onChange: (v: boolean) => void; disabled?: boolean }) {
  return (
    <div className="flex items-center justify-between gap-3 py-1.5">
      <div className="min-w-0">
        <span className="text-sm text-gray-800 dark:text-slate-200">{label}</span>
        {hint && <p className="text-[11px] text-gray-500 dark:text-slate-400">{hint}</p>}
      </div>
      <button
        type="button" role="switch" aria-checked={checked} onClick={() => onChange(!checked)} disabled={disabled}
        className={`relative inline-flex h-6 w-11 flex-shrink-0 items-center rounded-full transition-colors disabled:opacity-50 ${checked ? 'bg-green-600' : 'bg-gray-300 dark:bg-slate-600'}`}
      >
        <span className={`inline-block h-4 w-4 transform rounded-full bg-white shadow-sm transition-transform ${checked ? 'translate-x-6' : 'translate-x-1'}`} />
      </button>
    </div>
  );
}

const inputClass = 'mt-1 w-full px-3 py-2 text-sm rounded-xl border border-gray-300 bg-white text-gray-900 shadow-sm dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 dark:shadow-none placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-green-500';
const cardClass = 'rounded-2xl border border-gray-200 bg-white p-5 shadow-sm transition-colors hover:border-gray-300 dark:border-slate-700 dark:bg-slate-800 dark:shadow-none dark:hover:border-slate-600';
const hintClass = 'text-[11px] text-gray-500 dark:text-slate-400';
const sectionTitleClass = 'text-sm font-semibold text-gray-900 dark:text-slate-100';
const ghostBtnClass = 'inline-flex items-center gap-1.5 px-3 py-2 text-sm rounded-xl border border-gray-300 bg-white text-gray-700 shadow-sm hover:bg-gray-50 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-200 dark:shadow-none dark:hover:bg-slate-700/50 disabled:opacity-50 transition-colors';

export default function Line() {
  const toast = useToast();
  const { t } = useI18n();
  const { isReadOnly } = usePermission();

  const [settings, setSettings] = useState<LineSettingsDto | null>(null);
  const [status, setStatus] = useState<LineStatusDto | null>(null);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [busy, setBusy] = useState<'webhookCheck' | 'webhookApply' | 'test' | 'push' | null>(null);
  const [webhookCheck, setWebhookCheck] = useState<{ success: boolean; detail?: string; errorMessage?: string | null } | null>(null);

  // 密钥只写入、永不回显（后端只暴露 has* 布尔）
  const [channelAccessToken, setChannelAccessToken] = useState('');
  const [channelSecret, setChannelSecret] = useState('');
  const [showAccessToken, setShowAccessToken] = useState(false);
  const [showSecret, setShowSecret] = useState(false);
  const [pushUserId, setPushUserId] = useState('');
  const [form, setForm] = useState<UpdateLineSettingsRequest>({});

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const [settingsData, statusData, mailboxData] = await Promise.all([
        lineApi.getSettings(),
        lineApi.getStatus(),
        mailboxApi.list(),
      ]);
      setSettings(settingsData);
      setStatus(statusData);
      setMailboxes(mailboxData);
      setForm({
        channelId: settingsData.channelId ?? '',
        apiBaseUrl: settingsData.apiBaseUrl ?? '',
        dataApiBaseUrl: settingsData.dataApiBaseUrl ?? '',
        attachmentPublicBaseUrl: settingsData.attachmentPublicBaseUrl ?? '',
        attachmentPublicUrlTtlMinutes: settingsData.attachmentPublicUrlTtlMinutes,
        richMenuId: settingsData.richMenuId ?? '',
        webhookEnabled: settingsData.webhookEnabled,
        markAsReadEnabled: settingsData.markAsReadEnabled,
        loadingIndicatorEnabled: settingsData.loadingIndicatorEnabled,
        profileEnrichEnabled: settingsData.profileEnrichEnabled,
        autoReplyEnabled: settingsData.autoReplyEnabled,
        autoReplyMessage: settingsData.autoReplyMessage ?? '',
        textChunkChars: settingsData.textChunkChars,
        maxMessagesPerRequest: settingsData.maxMessagesPerRequest,
        replyTokenTtlSeconds: settingsData.replyTokenTtlSeconds,
        humanHandoverMuteMinutes: settingsData.humanHandoverMuteMinutes,
        quotaWarnPercent: settingsData.quotaWarnPercent,
        // 审计 #12：后端已有的六个可调参数（DTO 已暴露），表单必须带上默认值才不会提交成空
        statusCheckTimeoutSeconds: settingsData.statusCheckTimeoutSeconds,
        retryMaxAttempts: settingsData.retryMaxAttempts,
        maxInboundMediaBytes: settingsData.maxInboundMediaBytes,
        maxOutboundAttachmentBytes: settingsData.maxOutboundAttachmentBytes,
        outboundImageExtensions: settingsData.outboundImageExtensions ?? '',
        channelAccessTokenKeyId: settingsData.channelAccessTokenKeyId ?? '',
        mailboxId: settingsData.mailboxId ?? undefined,
        enabled: settingsData.enabled,
      });
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('channels.common.loadFailed', { channel: t('channels.line.name') }));
    } finally {
      setLoading(false);
    }
  }, [toast, t]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const patch = (key: keyof UpdateLineSettingsRequest, value: unknown) => setForm((prev) => ({ ...prev, [key]: value }));

  const handleSave = async () => {
    setSaving(true);
    try {
      const payload: UpdateLineSettingsRequest = { ...form };
      // 留空 = 保持原值：后端 UpdateSettingsAsync 只在非空白时写 line.channel_secret / line.channel_access_token
      if (channelSecret.trim()) payload.channelSecret = channelSecret.trim();
      else delete payload.channelSecret;
      if (channelAccessToken.trim()) payload.channelAccessToken = channelAccessToken.trim();
      else delete payload.channelAccessToken;

      setSettings(await lineApi.updateSettings(payload));
      setChannelSecret('');
      setChannelAccessToken('');
      setShowSecret(false);
      setShowAccessToken(false);
      toast.success(t('channels.common.saveSucceeded', { channel: t('channels.line.name') }));
      setStatus(await lineApi.getStatus());
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('channels.common.saveFailed', { channel: t('channels.line.name') }));
    } finally {
      setSaving(false);
    }
  };

  /**
   * 统一跑一个后端动作，跑完刷新状态卡。
   * checkWebhook → POST /api/line/webhook-check（LINE 官方 /v2/bot/channel/webhook/test，只验证连通性、不发消息）
   * applyWebhookEndpoint → POST /api/line/webhook-endpoint?enabled=（把本部署地址写回 LINE 侧）
   * pushTest → POST /api/line/test（会真发一条消息并消耗月度配额 ⇒ 只保留在配额卡的探活区，页头不放按钮）
   * 注：后端 LineService.TestConnectionAsync 目前没有被 LineController 暴露成独立端点，
   *     所以这里不提供「免费测连接」按钮，避免误耗配额。
   */
  const runAction = async (
    kind: 'webhookCheck' | 'webhookApply' | 'push',
    call: () => Promise<{ success: boolean; detail?: string; errorMessage?: string | null }>,
    labels: { ok: string; fail: string },
  ) => {
    setBusy(kind);
    try {
      const result = await call();
      if (kind === 'webhookCheck' || kind === 'webhookApply') setWebhookCheck(result);
      if (result.success) toast.success(result.detail ?? labels.ok);
      else toast.error(result.errorMessage ?? labels.fail);
      if (kind !== 'push') setStatus(await lineApi.getStatus());
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : labels.fail);
    } finally {
      setBusy(null);
    }
  };

  const copyText = async (value: string) => {
    try {
      await navigator.clipboard.writeText(value);
      toast.success(t('channels.line.webhook.copied'));
    } catch {
      toast.error(t('channels.common.copyFailed'));
    }
  };

  if (loading && !settings) {
    return (
      <div className="flex items-center justify-center h-64 gap-2 text-gray-500 dark:text-slate-400">
        <Loader2 className="w-5 h-5 animate-spin" />
        <span>{t('channels.line.loading')}</span>
      </div>
    );
  }

  const configured = status?.configured === true;
  const quotaUnlimited = (status?.quotaType ?? '').toLowerCase() === 'unlimited';
  const endpointMismatch = !!settings?.webhookUrl && !!status?.webhookEndpointOnLine && settings.webhookUrl !== status.webhookEndpointOnLine;
  const warnAt = settings?.quotaWarnPercent ?? 80;
  const channelName = t('channels.line.name');

  return (
    <div className="max-w-5xl mx-auto p-4 sm:p-6 space-y-5">
      {/* 页头 */}
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div className="flex items-center gap-3">
          <div className="flex h-11 w-11 items-center justify-center rounded-2xl bg-green-50 text-green-600 ring-1 ring-inset ring-green-200/80 dark:bg-green-500/15 dark:text-green-300 dark:ring-green-400/25">
            <LineLogo />
          </div>
          <div className="min-w-0">
            <h1 className="text-xl font-semibold tracking-tight text-gray-900 dark:text-slate-100">{t('channels.line.title')}</h1>
            <p className="text-sm text-gray-500 dark:text-slate-400">{t('channels.line.subtitle')}</p>
          </div>
        </div>
        <div className="flex items-center gap-2 flex-wrap">
          <button type="button" onClick={() => void loadData()} disabled={loading} className={ghostBtnClass}>
            <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
            {t('channels.common.refresh')}
          </button>
        </div>
      </div>

      {/* 状态卡 + 配额卡 */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <div className={cardClass}>
          <div className="flex items-center justify-between mb-2">
            <h2 className={sectionTitleClass}>{t('channels.line.status.channelTitle')}</h2>
            {configured && status?.enabled ? (
              <span className="inline-flex items-center gap-1 text-[11px] px-2 py-0.5 rounded-full bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300">
                <span className="inline-block w-1.5 h-1.5 rounded-full bg-green-500 animate-pulse" />
                {t('channels.line.status.online')}
              </span>
            ) : (
              <span className="text-[11px] px-2 py-0.5 rounded-full bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-slate-300">
                {configured ? t('channels.common.configuredButDisabled') : t('channels.common.notConfigured')}
              </span>
            )}
          </div>
          <StatusRow label={t('channels.line.status.configured')} ok={status?.configured} />
          <StatusRow label={t('channels.line.status.enabled')} ok={status?.enabled} />
          <StatusRow label={t('channels.common.apiReachable')} ok={status?.apiReachable ?? null} />
          <StatusRow
            label={t('channels.common.tokenValid')}
            ok={status?.tokenValid ?? null}
            value={status?.tokenScope ? <span className="text-xs font-mono text-gray-600 dark:text-slate-300">{status.tokenScope}</span> : undefined}
          />
          <StatusRow
            label={t('channels.line.status.tokenExpiresAt')}
            value={<span className="text-xs text-gray-700 dark:text-slate-200">{status?.tokenExpiresAt ? formatDateTime(status.tokenExpiresAt) : t('channels.line.status.tokenNonExpiring')}</span>}
          />
          <StatusRow
            label={t('channels.line.status.webhookOnLine')}
            ok={status?.webhookDeliveryOnLine ?? null}
            value={<span className="text-xs font-mono text-gray-600 dark:text-slate-300 truncate max-w-[12rem]" title={status?.webhookEndpointOnLine ?? ''}>{status?.webhookEndpointOnLine || t('channels.common.notConfigured')}</span>}
          />
          <StatusRow
            label={t('channels.line.status.replyMode')}
            value={<span className="text-xs text-gray-700 dark:text-slate-200">{status?.replyModeHint === 'reply' ? t('channels.line.status.replyModeReply') : status?.replyModeHint === 'push' ? t('channels.line.status.replyModePush') : t('channels.line.status.replyModeUnknown')}</span>}
          />
          {status?.lastError && (
            <div className="mt-2 flex items-start gap-2 text-xs text-red-700 dark:text-red-300 rounded-xl border border-red-200/80 bg-red-50/80 p-3 dark:border-red-500/25 dark:bg-red-500/10">
              <ShieldAlert className="w-4 h-4 flex-shrink-0" />
              <span className="break-all min-w-0">HTTP {status.lastError.httpStatus}{status.lastError.message ? ` · ${status.lastError.message}` : ''}</span>
            </div>
          )}
          {!configured && <p className="mt-2 text-xs text-gray-500 dark:text-slate-400">{t('channels.line.status.notConfiguredHint')}</p>}
        </div>

        <div className={cardClass}>
          <div className="flex items-center justify-between mb-2">
            <h2 className={`${sectionTitleClass} flex items-center gap-1.5`}>
              <Gauge className="w-4 h-4 text-green-600 dark:text-green-400" />
              {t('channels.line.quota.title')}
            </h2>
            {status?.quotaWarning && (
              <span className="text-[11px] px-2 py-0.5 rounded-full bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300">{t('channels.line.quota.warning')}</span>
            )}
          </div>
          {quotaUnlimited ? (
            <p className="text-sm text-gray-700 dark:text-slate-200">{t('channels.line.quota.unlimited')}</p>
          ) : (
            <>
              <div className="flex items-baseline gap-2 flex-wrap">
                <span className="text-lg font-semibold text-gray-900 dark:text-slate-100">{(status?.quotaConsumed ?? -1) >= 0 ? status?.quotaConsumed : '-'}</span>
                <span className="text-sm text-gray-500 dark:text-slate-400">/ {(status?.quotaTotal ?? -1) > 0 ? status?.quotaTotal : t('channels.line.quota.unknown')}</span>
                <span className="text-xs text-gray-500 dark:text-slate-400">{t('channels.line.quota.monthly')}</span>
              </div>
              <div className="mt-2 h-2 rounded-full bg-gray-200 dark:bg-slate-700 overflow-hidden">
                <div className={`h-full transition-all ${(status?.quotaUsedPercent ?? 0) >= warnAt ? 'bg-amber-500' : 'bg-green-500'}`} style={{ width: `${Math.min(100, Math.max(0, status?.quotaUsedPercent ?? 0))}%` }} />
              </div>
              <p className={`mt-1 ${hintClass}`}>{t('channels.line.quota.usedPercent', { percent: status?.quotaUsedPercent ?? 0, warn: warnAt })}</p>
            </>
          )}
          <p className="mt-2 text-[11px] text-gray-500 dark:text-slate-400">{t('channels.line.quota.replyFreeHint')}</p>
          {/* push 探活：真发一条会消耗月度配额，且只对已加机器人为好友的客户可达 */}
          <div className="mt-3 pt-3 border-t border-gray-100 dark:border-slate-700">
            <label className="text-xs font-medium text-gray-700 dark:text-slate-200">{t('channels.line.push.userId')}</label>
            <div className="flex gap-2 mt-1 flex-wrap">
              <input
                type="text" value={pushUserId} onChange={(e) => setPushUserId(e.target.value)}
                placeholder="Uxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" disabled={isReadOnly}
                className="flex-1 min-w-[12rem] px-3 py-2 text-sm rounded-xl border border-gray-300 bg-white text-gray-900 shadow-sm dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 dark:shadow-none font-mono focus:outline-none focus:ring-2 focus:ring-green-500"
              />
              <button
                type="button"
                onClick={() => void runAction('push', () => lineApi.pushTest({ userId: pushUserId.trim() || undefined }), { ok: t('channels.line.push.sent', { via: 'push' }), fail: t('channels.line.push.failed') })}
                disabled={busy === 'push' || !settings?.hasChannelAccessToken}
                className={ghostBtnClass}
              >
                {busy === 'push' ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
                {t('channels.line.push.button')}
              </button>
            </div>
            <p className="mt-1 text-[11px] text-amber-700 dark:text-amber-400">{t('channels.line.push.quotaWarning')}</p>
          </div>
        </div>
      </div>

      {/* 回调地址：显示 + 校验 + 写回 LINE 侧 */}
      <div className={`${cardClass} space-y-3`}>
        <h2 className={`${sectionTitleClass} flex items-center gap-2`}>
          <Link2 className="w-4 h-4 text-green-600 dark:text-green-400" />
          {t('channels.common.callbackAddress')}
        </h2>
        {settings?.webhookUrl ? (
          <div className="flex items-center gap-2">
            <code className="flex-1 px-3 py-2 bg-gray-50 dark:bg-slate-900/60 border border-gray-200 dark:border-slate-700 rounded-lg text-sm text-gray-700 dark:text-slate-300 break-all font-mono">
              {settings.webhookUrl}
            </code>
            <button type="button" onClick={() => void copyText(settings.webhookUrl ?? '')} className="flex-shrink-0 p-2 text-gray-500 dark:text-slate-400 hover:bg-gray-100 dark:hover:bg-slate-700 rounded-lg transition-colors" title={t('channels.common.copy')}>
              <Copy className="w-4 h-4" />
            </button>
          </div>
        ) : (
          <p className="text-sm text-amber-700 dark:text-amber-400">{t('channels.line.webhook.missingBaseUrl')}</p>
        )}
        <div className="flex items-center gap-2 flex-wrap">
          <button
            type="button"
            onClick={() => void runAction('webhookCheck', () => lineApi.checkWebhook({ webhookUrl: settings?.webhookUrl }), { ok: t('channels.line.webhook.checkSuccess'), fail: t('channels.line.webhook.checkFailed') })}
            disabled={busy === 'webhookCheck' || isReadOnly || !settings?.hasChannelSecret}
            className="inline-flex items-center gap-1.5 px-3 py-2 text-sm rounded-lg bg-green-600 hover:bg-green-700 text-white disabled:bg-gray-300 disabled:opacity-60 transition-colors"
          >
            {busy === 'webhookCheck' ? <Loader2 className="w-4 h-4 animate-spin" /> : <CheckCircle2 className="w-4 h-4" />}
            {t('channels.line.webhook.checkButton')}
          </button>
          <button
            type="button"
            onClick={() => void runAction('webhookApply', () => lineApi.applyWebhookEndpoint(true), { ok: t('channels.line.webhook.applySuccess'), fail: t('channels.line.webhook.applyFailed') })}
            disabled={busy === 'webhookApply' || isReadOnly || !settings?.hasChannelAccessToken}
            className={ghostBtnClass}
          >
            {busy === 'webhookApply' ? <Loader2 className="w-4 h-4 animate-spin" /> : <Settings className="w-4 h-4" />}
            {t('channels.line.webhook.applyButton')}
          </button>
          <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-slate-200 ml-1">
            <input type="checkbox" checked={form.webhookEnabled ?? false} onChange={(e) => patch('webhookEnabled', e.target.checked)} disabled={isReadOnly} />
            {t('channels.line.fields.webhookEnabled')}
          </label>
        </div>
        {webhookCheck && (
          <div className={`text-xs rounded-lg p-2 border ${webhookCheck.success ? 'text-green-700 dark:text-green-300 bg-green-50 dark:bg-green-900/20 border-green-200 dark:border-green-800' : 'text-red-700 dark:text-red-300 bg-red-50 dark:bg-red-900/20 border-red-200 dark:border-red-800'}`}>
            {webhookCheck.detail || webhookCheck.errorMessage || (webhookCheck.success ? t('channels.line.webhook.checkSuccess') : t('channels.line.webhook.checkFailed'))}
          </div>
        )}
        {endpointMismatch && (
          <div className="flex items-start gap-2 text-xs text-amber-700 dark:text-amber-400 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 rounded-lg p-2">
            <AlertCircle className="w-4 h-4 flex-shrink-0 mt-0.5" />
            <span>{t('channels.line.webhook.endpointMismatch')}</span>
          </div>
        )}
        <p className={hintClass}>{t('channels.line.webhook.noGetHandshakeHint')}</p>
      </div>

      {/* 凭据（只写不读） */}
      <div className={`${cardClass} space-y-4`}>
        <h2 className={sectionTitleClass}>{t('channels.line.sections.credentials')}</h2>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <label className="block">
            <span className="text-xs font-medium text-gray-700 dark:text-slate-200">Channel ID <span className="text-red-500">*</span></span>
            <input type="text" value={form.channelId ?? ''} onChange={(e) => patch('channelId', e.target.value)} placeholder="@271abcde" disabled={isReadOnly} className={inputClass} />
            <span className={hintClass}>{t('channels.line.fields.channelIdHint')}</span>
          </label>
          <label className="block">
            <span className="text-xs font-medium text-gray-700 dark:text-slate-200">Channel Access Token <span className="text-red-500">*</span></span>
            <div className="relative">
              <input
                type={showAccessToken ? 'text' : 'password'} value={channelAccessToken} onChange={(e) => setChannelAccessToken(e.target.value)}
                placeholder={settings?.hasChannelAccessToken ? t('channels.common.maskedConfigured') : t('channels.line.fields.accessTokenPlaceholder')}
                autoComplete="off" spellCheck={false} disabled={isReadOnly} className={`${inputClass} pr-9`}
              />
              <button type="button" onClick={() => setShowAccessToken((v) => !v)} className="absolute right-2 top-[1.4rem] text-gray-400 hover:text-gray-600 dark:hover:text-slate-300" title={showAccessToken ? t('channels.common.hide') : t('channels.common.show')}>
                {showAccessToken ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
              </button>
            </div>
            <span className={hintClass}>{settings?.hasChannelAccessToken ? t('channels.line.fields.secretStoredEncrypted') : t('channels.line.fields.accessTokenHint')}</span>
          </label>
          <label className="block">
            <span className="text-xs font-medium text-gray-700 dark:text-slate-200">Channel Secret <span className="text-red-500">*</span></span>
            <div className="relative">
              <input
                type={showSecret ? 'text' : 'password'} value={channelSecret} onChange={(e) => setChannelSecret(e.target.value)}
                placeholder={settings?.hasChannelSecret ? t('channels.common.maskedConfigured') : t('channels.line.fields.secretPlaceholder')}
                autoComplete="off" spellCheck={false} disabled={isReadOnly} className={`${inputClass} pr-9`}
              />
              <button type="button" onClick={() => setShowSecret((v) => !v)} className="absolute right-2 top-[1.4rem] text-gray-400 hover:text-gray-600 dark:hover:text-slate-300" title={showSecret ? t('channels.common.hide') : t('channels.common.show')}>
                {showSecret ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
              </button>
            </div>
            <span className={hintClass}>{t('channels.line.fields.secretHint')}</span>
          </label>
          <label className="block min-w-0">
            <span className="text-xs font-medium text-gray-700 dark:text-slate-200">{t('channels.line.fields.accessTokenKeyId')}</span>
            {/* 审计 #12：KeyId 不是密钥（只是 v2.1 签发接口返回的 key_id，用于轮换时告诉 LINE 撤哪把），
                此前只读展示 ⇒ 界面调不了；现在可编辑，留空保存即清除该行、回落到 appsettings 基线。 */}
            <input type="text" value={form.channelAccessTokenKeyId ?? ''} onChange={(e) => patch('channelAccessTokenKeyId', e.target.value)} placeholder="kid-xxxxxxxx-xxxx" disabled={isReadOnly} className={inputClass} />
            <span className={hintClass}>{t('channels.line.fields.accessTokenKeyIdValueHint')}</span>
          </label>
        </div>
        <div className="flex items-start gap-2 text-xs text-green-800 dark:text-green-300 bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800 rounded-lg p-2.5">
          <Info className="w-4 h-4 flex-shrink-0 mt-0.5" />
          <span>{t('channels.line.notice.secretNeverEchoed')}</span>
        </div>
      </div>

      {/* 附件公网直链（LINE 无上传接口） */}
      <div className={`${cardClass} space-y-3`}>
        <h2 className={`${sectionTitleClass} flex items-center gap-2`}>
          <ImageIcon className="w-4 h-4 text-green-600 dark:text-green-400" />
          {t('channels.line.sections.attachments')}
        </h2>
        <label className="block">
          <span className="text-xs font-medium text-gray-700 dark:text-slate-200">AttachmentPublicBaseUrl</span>
          <input type="text" value={form.attachmentPublicBaseUrl ?? ''} onChange={(e) => patch('attachmentPublicBaseUrl', e.target.value)} placeholder="https://css.example.com" disabled={isReadOnly} className={inputClass} />
          <span className={hintClass}>{t('channels.line.fields.attachmentBaseUrlHint')}</span>
        </label>
        <div className="flex items-start gap-2 text-xs text-amber-800 dark:text-amber-300 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 rounded-lg p-2.5">
          <ShieldAlert className="w-4 h-4 flex-shrink-0 mt-0.5" />
          <span className="min-w-0">{t('channels.line.notice.attachmentNoUploadApi')}</span>
        </div>
      </div>

      {/* 运行参数 */}
      <div className={`${cardClass} space-y-4`}>
        <h2 className={sectionTitleClass}>{t('channels.line.sections.runtime')}</h2>
        <div className="grid grid-cols-2 sm:grid-cols-3 gap-4">
          {NUMBER_FIELDS.map(({ key, labelKey, hintKey, min, max }) => (
            <label key={key} className="block">
              <span className="text-xs font-medium text-gray-700 dark:text-slate-200">{t(labelKey)}</span>
              <input
                type="number"
                min={min}
                max={max}
                step={key === 'maxInboundMediaBytes' || key === 'maxOutboundAttachmentBytes' ? 1048576 : 1}
                value={Number(form[key] ?? 0)}
                onChange={(e) => patch(key, Number(e.target.value))}
                disabled={isReadOnly}
                className={inputClass}
              />
              <span className={hintClass}>{t(hintKey)}</span>
            </label>
          ))}
          {/* 审计 #12：出站图片扩展名白名单（保存时后端统一转小写、去重） */}
          <label className="block">
            <span className="text-xs font-medium text-gray-700 dark:text-slate-200">{t('channels.line.fields.outboundImageExtensions')}</span>
            <input type="text" value={form.outboundImageExtensions ?? ''} onChange={(e) => patch('outboundImageExtensions', e.target.value)} placeholder="jpg,jpeg,png" disabled={isReadOnly} className={inputClass} />
            <span className={hintClass}>{t('channels.line.fields.outboundImageExtensionsHint')}</span>
          </label>
          <label className="block">
            <span className="text-xs font-medium text-gray-700 dark:text-slate-200">RichMenuId</span>
            <input type="text" value={form.richMenuId ?? ''} onChange={(e) => patch('richMenuId', e.target.value)} placeholder="richmenu-xxxxxxxxxxxxxxxxxxxxxx" disabled={isReadOnly} className={inputClass} />
            <span className={hintClass}>{t('channels.line.fields.richMenuIdHint')}</span>
          </label>
          <label className="block">
            <span className="text-xs font-medium text-gray-700 dark:text-slate-200">{t('channels.common.linkedMailbox')}</span>
            <select value={form.mailboxId ?? ''} onChange={(e) => patch('mailboxId', e.target.value ? Number(e.target.value) : undefined)} disabled={isReadOnly} className={inputClass}>
              <option value="">{t('channels.common.autoAssignMailbox')}</option>
              {mailboxes.map((m) => (
                <option key={m.id} value={m.id} disabled={!m.isActive}>{m.name} ({m.email}){m.isActive ? '' : t('channels.common.disabledSuffix')}</option>
              ))}
            </select>
            <span className={hintClass}>{t('channels.common.mailboxHint', { channel: channelName })}</span>
          </label>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-6">
          <ToggleRow label={t('channels.line.fields.markAsReadEnabled')} hint={t('channels.line.fields.markAsReadEnabledHint')} checked={form.markAsReadEnabled ?? false} onChange={(v) => patch('markAsReadEnabled', v)} disabled={isReadOnly} />
          <ToggleRow label={t('channels.line.fields.loadingIndicatorEnabled')} hint={t('channels.line.fields.loadingIndicatorEnabledHint')} checked={form.loadingIndicatorEnabled ?? false} onChange={(v) => patch('loadingIndicatorEnabled', v)} disabled={isReadOnly} />
          <ToggleRow label={t('channels.line.fields.profileEnrichEnabled')} hint={t('channels.line.fields.profileEnrichEnabledHint')} checked={form.profileEnrichEnabled ?? false} onChange={(v) => patch('profileEnrichEnabled', v)} disabled={isReadOnly} />
          <ToggleRow
            label={t('channels.line.fields.humanHandoverTitle')}
            hint={t('channels.line.fields.humanHandoverHint')}
            checked={(form.humanHandoverMuteMinutes ?? 0) > 0}
            onChange={(v) => patch('humanHandoverMuteMinutes', v ? (settings?.humanHandoverMuteMinutes ?? 60) : 0)}
            disabled={isReadOnly}
          />
        </div>
      </div>

      {/* 自动回复 / 启用 / 保存 */}
      <div className={`${cardClass} space-y-3`}>
        <h2 className={sectionTitleClass}>{t('channels.common.basicConfig')}</h2>
        <ToggleRow label={t('channels.common.enableAutoReply')} hint={t('channels.common.autoReplyHint')} checked={form.autoReplyEnabled ?? false} onChange={(v) => patch('autoReplyEnabled', v)} disabled={isReadOnly} />
        {form.autoReplyEnabled && (
          <label className="block">
            <span className="text-xs font-medium text-gray-700 dark:text-slate-200">{t('channels.common.autoReplyContent')}</span>
            <textarea
              value={form.autoReplyMessage ?? ''} rows={3} maxLength={1000} onChange={(e) => patch('autoReplyMessage', e.target.value)}
              placeholder={t('channels.common.autoReplyPlaceholder')} disabled={isReadOnly} className={`${inputClass} resize-none`}
            />
            <div className="flex items-center justify-between mt-1">
              <span className={`flex items-center gap-1 ${hintClass}`}>
                <BellOff className="w-3 h-3" />
                {t('channels.line.notice.autoReplyFirstOnly')}
              </span>
              <span className={`text-xs ${(form.autoReplyMessage?.length ?? 0) > 900 ? 'text-red-500' : 'text-gray-400 dark:text-slate-500'}`} title={t('channels.common.charCountTitle')}>
                {form.autoReplyMessage?.length ?? 0}/1000
              </span>
            </div>
          </label>
        )}
        <div className="flex items-center justify-between gap-4 flex-wrap pt-2 border-t border-gray-100 dark:border-slate-700">
          <ToggleRow label={t('channels.common.enableChannel', { channel: channelName })} hint={t('channels.line.form.enableHint')} checked={form.enabled ?? false} onChange={(v) => patch('enabled', v)} disabled={isReadOnly} />
          <div className="flex items-center gap-2">
            {isReadOnly && <span className="text-xs text-gray-500 dark:text-slate-400">{t('channels.line.readOnly')}</span>}
            <button
              type="button" onClick={() => void handleSave()} disabled={saving || isReadOnly}
              className="inline-flex items-center gap-1.5 px-4 py-2 bg-gradient-to-r from-green-600 to-emerald-600 hover:from-green-700 hover:to-emerald-700 disabled:bg-gray-300 text-white rounded-lg text-sm font-medium shadow-sm transition-all"
            >
              {saving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
              {t('channels.common.saveConfig')}
            </button>
          </div>
        </div>
      </div>

      {/* 入站消息形状 */}
      <div className={cardClass}>
        <h2 className={`${sectionTitleClass} flex items-center gap-2 mb-3`}>
          <MessageSquare className="w-4 h-4 text-green-600 dark:text-green-400" />
          {t('channels.common.messageTypes')}
        </h2>
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-2.5">
          {MESSAGE_TYPES.map(({ icon: Icon, labelKey, descKey }) => (
            <div key={labelKey} className="flex items-center gap-2.5 p-2.5 rounded-lg bg-gray-50/70 dark:bg-slate-700/40 border border-gray-100 dark:border-slate-600/50">
              <div className="flex-shrink-0 w-8 h-8 rounded-lg bg-green-100 dark:bg-green-900/40 flex items-center justify-center">
                <Icon className="w-4 h-4 text-green-700 dark:text-green-300" />
              </div>
              <div className="min-w-0">
                <p className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate">{t(labelKey)}</p>
                <p className={`truncate ${hintClass}`}>{t(descKey)}</p>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* 配置指南 */}
      <div className="bg-gradient-to-br from-green-50/80 to-emerald-50/50 dark:from-green-900/20 dark:to-emerald-900/10 rounded-xl border border-green-200 dark:border-green-800/50 p-4 space-y-2.5">
        <div className="flex items-center gap-2">
          <div className="p-1.5 bg-green-600 rounded-lg">
            <LineLogo className="w-5 h-5 text-white" />
          </div>
          <h2 className="text-sm font-semibold text-green-900 dark:text-green-200">{t('channels.common.configGuide')}</h2>
        </div>
        <ol className="space-y-1.5 text-sm text-green-900 dark:text-green-200 list-decimal list-inside">
          <li>{t('channels.line.guide.step1')}</li>
          <li>{t('channels.line.guide.step2')}</li>
          <li>{t('channels.line.guide.step3')}</li>
          <li>{t('channels.line.guide.step4')}</li>
          <li>{t('channels.line.guide.step5')}</li>
        </ol>
        <p className="text-xs text-green-800 dark:text-green-300">{t('channels.line.guide.docNote')}</p>
      </div>
    </div>
  );
}
