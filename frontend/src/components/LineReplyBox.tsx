import { useRef, useEffect, useState, useCallback, useMemo } from 'react';
import { Send, Loader2, Paperclip, X, Image, Music, Video, FileText, Clock, ShieldAlert, UserCheck, Zap, TimerReset } from 'lucide-react';
import { attachmentApi } from '../api';
import type { AttachmentResponse } from '../api/types';
import type { TranslationKey } from '../locales/zh-Hans';
import { useI18n } from '../context/I18nContext';
import { useToast } from '../context/ToastContext';
import SourceTypeIcon from './SourceTypeIcon';
// 审计 #13：分段计数与输入截断都走这里，口径与后端 LineClient.SplitText（UTF-16）一致
import { clampLineText, expectedLineChunkCount } from '../utils/line-chunking';

/**
 * 与后端 LineOptions.TextChunkChars 默认值一致：LINE 单条 text 消息 5000 字符上限，
 * 超出由 LineClient.SplitText 按换行/空格切分多条发送（前端只做计数提示，不自行切分）。
 */
const LINE_TEXT_MAX_LENGTH = 5000;
/** 与后端 LineOptions.MaxOutboundAttachmentBytes 默认值一致（10MB） */
const LINE_MAX_ATTACHMENT_BYTES = 10 * 1024 * 1024;
/** 与后端 LineOptions.ReplyTokenTtlSeconds 默认值一致（replyToken 一次性、约 25s） */
const LINE_REPLY_TOKEN_TTL_SECONDS = 25;
/** LINE image 消息只吃 JPG/PNG；其它图片按 file 发送（与 LineService.BuildAttachmentMessagesAsync 同口径） */
const LINE_IMAGE_NATIVE_EXTS = ['jpg', 'jpeg', 'png'];

type ReplyWindowStatus = 'ok' | 'expiring' | 'expired' | 'unknown';

interface Props {
  value: string;
  onChange: (value: string) => void;
  onSubmit: (attachmentIds?: number[]) => Promise<void> | void;
  submitting?: boolean;
  readOnly?: boolean;
  maxLength?: number;
  /**
   * replyToken 时效状态（默认约 25 秒）。
   * ok/expiring ⇒ 本次走 `/v2/bot/message/reply`，**不消耗月度配额**；
   * expired ⇒ 回落 `/v2/bot/message/push`，**消耗配额且只对已添加机器人为好友的客户可达**。
   */
  replyWindowStatus?: ReplyWindowStatus;
  /** 剩余时间描述（如 "18 秒"），由上层按线程 created_at + reply_token_ttl_seconds 计算 */
  replyWindowRemainingText?: string;
  /** 人工接管静音中：坐席回复后系统内静音 HumanHandoverMuteMinutes 分钟（LINE 无官方接管 API） */
  humanHandoverActive?: boolean;
  /** 静音剩余时间描述（如 "42 分钟"） */
  humanHandoverRemainingText?: string;
  /** 手动接管 / 结束接管（POST /api/line/takeover、DELETE /api/line/takeover/{id}） */
  onTakeover?: () => Promise<void> | void;
  onReleaseTakeover?: () => Promise<void> | void;
  /** 会话是否已被标记不可达（404 未加好友 / 403 拒投），由后端内部备注线程推导 */
  unreachable?: boolean;
}

/** 快捷回复短语真源 key（必须经 t() 解析，直接渲染会漏出 key 本身） */
const QUICK_REPLY_KEYS: TranslationKey[] = [
  'conversations.reply.quickReply1',
  'conversations.reply.quickReply2',
  'conversations.reply.quickReply3',
  'conversations.reply.quickReply4',
  'conversations.reply.quickReply5',
  'conversations.reply.quickReply6',
];

/**
 * LINE 会话专用回复框。
 * 与钉钉/WhatsApp 回复框的三个关键差异（都必须让坐席看得见）：
 *  1) 回复通道二义性：客户来消息后约 25 秒内可用一次性 replyToken 走 reply（不耗配额、可达性最好）；
 *     超时后只能 push——push 消耗月度配额，且客户未加机器人为好友（404）或被屏蔽（403）时根本不可达。
 *  2) 单条 5000 字符上限，长文由后端切分多条 ⇒ 这里显示"将拆成 N 条"计数。
 *  3) 附件没有上传接口：出站附件由本部署的公网 HTTPS 签名直链（/api/line/media/{id}）交付，
 *     因此附件有 10MB 上限，且只有 JPG/PNG 走 image 消息，其余按文件消息发送。
 * 没有 24 小时服务窗口概念，也没有第三方输入提示/已读回执（除非开通 Chat-enabled 功能包）。
 */
export default function LineReplyBox({
  value,
  onChange,
  onSubmit,
  submitting = false,
  readOnly = false,
  maxLength = LINE_TEXT_MAX_LENGTH,
  replyWindowStatus = 'unknown',
  replyWindowRemainingText,
  humanHandoverActive = false,
  humanHandoverRemainingText,
  onTakeover,
  onReleaseTakeover,
  unreachable = false,
}: Props) {
  const toast = useToast();
  const { t } = useI18n();
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [attachments, setAttachments] = useState<AttachmentResponse[]>([]);
  const [uploading, setUploading] = useState(false);
  const [isDragOver, setIsDragOver] = useState(false);
  const [showQuickReplies, setShowQuickReplies] = useState(false);
  const [takeoverBusy, setTakeoverBusy] = useState(false);

  // textarea 自增高
  useEffect(() => {
    const el = textareaRef.current;
    if (!el) return;
    el.style.height = 'auto';
    el.style.height = `${Math.min(el.scrollHeight, 220)}px`;
  }, [value]);

  /**
   * 预计分片数：仅提示。真实切分在 LineClient.SplitText（LineClient.cs:822）。
   * 审计 #13：计数必须与后端同口径 —— 按 UTF-16 长度（C# string.Length），不是 Unicode 码点数。
   * 此前用 [...value].length：一个 emoji 只算 1，但它占 2 个甚至更多 UTF-16 单元，
   * 结果含 emoji 的长文本前端显示 1 段、后端实际发 2 段（本行提示与下方「单次请求最多 N 条」告警一起算错）。
   * 口径集中在 utils/line-chunking.ts（后端算法的逐行等价移植），一次请求最多
   * MaxMessagesPerRequest（默认 5）条消息 ⇒ 文本 + 附件的总数也要提示坐席。
   */
  const expectedChunks = useMemo(
    () => expectedLineChunkCount(value, maxLength),
    [value, maxLength],
  );

  const totalOutboundMessages = expectedChunks + attachments.length;

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
      e.preventDefault();
      handleSubmit();
      return;
    }
    if (showQuickReplies && !e.ctrlKey && !e.metaKey && !e.altKey) {
      const num = parseInt(e.key, 10);
      if (!Number.isNaN(num) && num >= 1 && num <= QUICK_REPLY_KEYS.length) {
        e.preventDefault();
        appendQuickReply(t(QUICK_REPLY_KEYS[num - 1]));
      }
      if (e.key === 'Escape') {
        e.preventDefault();
        setShowQuickReplies(false);
      }
    }
  };

  const handleSubmit = useCallback(() => {
    if (submitting || readOnly || uploading) return;
    if (!value.trim() && attachments.length === 0) return;
    const attIds = attachments.length > 0 ? attachments.map((a) => a.id) : undefined;
    void onSubmit(attIds);
  }, [attachments, value, submitting, readOnly, uploading, onSubmit]);

  const appendQuickReply = (text: string) => {
    const sep = value && !value.endsWith('\n') ? '\n' : '';
    // 审计 #13：截断走 clampLineText（UTF-16 口径、不拆代理项），不用裸 slice
    onChange(clampLineText(value + sep + text, maxLength));
    setShowQuickReplies(false);
    requestAnimationFrame(() => textareaRef.current?.focus());
  };

  const validateFile = (file: File): string | null => {
    if (file.size > LINE_MAX_ATTACHMENT_BYTES) {
      return t('conversations.channel.line.fileTooLarge', { name: file.name, limit: '10MB' });
    }
    return null;
  };

  const handleFilesUpload = async (fileArr: File[]) => {
    if (fileArr.length === 0) return;
    const validFiles: File[] = [];
    const errors: string[] = [];
    for (const file of fileArr) {
      const error = validateFile(file);
      if (error) {
        errors.push(error);
        continue;
      }
      validFiles.push(file);
    }
    if (errors.length > 0) toast.error(errors.join('; '));
    if (validFiles.length === 0) return;

    setUploading(true);
    try {
      const result = await attachmentApi.uploadBatch(validFiles);
      setAttachments((prev) => [...prev, ...(result?.data ?? [])]);
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('conversations.reply.uploadFailed'));
    } finally {
      setUploading(false);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  };

  const handleFileSelect = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    await handleFilesUpload(Array.from(files));
  };

  const handlePaste = (e: React.ClipboardEvent<HTMLTextAreaElement>) => {
    if (readOnly || submitting) return;
    const items = e.clipboardData?.items;
    if (!items) return;
    const files: File[] = [];
    for (let i = 0; i < items.length; i++) {
      const item = items[i];
      if (item.kind === 'file') {
        const file = item.getAsFile();
        if (file) {
          const ext = file.type.split('/')[1] || 'bin';
          files.push(new File([file], `paste_${Date.now()}.${ext}`, { type: file.type }));
        }
      }
    }
    if (files.length > 0) {
      e.preventDefault();
      void handleFilesUpload(files);
    }
  };

  const handleDragOver = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    if (readOnly || submitting) return;
    if (e.dataTransfer.types.includes('Files')) setIsDragOver(true);
  };

  const handleDragLeave = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    if (e.currentTarget === e.target) setIsDragOver(false);
  };

  const handleDrop = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    if (readOnly || submitting) return;
    setIsDragOver(false);
    const files = Array.from(e.dataTransfer.files || []);
    if (files.length === 0) return;
    void handleFilesUpload(files);
  };

  const removeAttachment = (id: number) => {
    setAttachments((prev) => prev.filter((a) => a.id !== id));
    attachmentApi.delete(id).catch((err: unknown) => console.error('删除附件失败:', err));
  };

  const handleToggleTakeover = async () => {
    const handler = humanHandoverActive ? onReleaseTakeover : onTakeover;
    if (!handler) return;
    setTakeoverBusy(true);
    try {
      await handler();
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('conversations.channel.line.takeoverFailed'));
    } finally {
      setTakeoverBusy(false);
    }
  };

  const attachmentIcon = (mimeType: string) => {
    if (mimeType.startsWith('image/')) return <Image className="w-3.5 h-3.5 text-green-600" />;
    if (mimeType.startsWith('audio/')) return <Music className="w-3.5 h-3.5 text-purple-500" />;
    if (mimeType.startsWith('video/')) return <Video className="w-3.5 h-3.5 text-orange-500" />;
    return <FileText className="w-3.5 h-3.5 text-gray-400" />;
  };

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes}B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)}MB`;
  };

  /** 附件是否会被当作 LINE image 消息（JPG/PNG），否则按文件消息发送 */
  const isNativeImage = (att: AttachmentResponse) => {
    if (!att.mimeType.startsWith('image/')) return false;
    const ext = att.fileName.split('.').pop()?.toLowerCase() ?? '';
    return LINE_IMAGE_NATIVE_EXTS.includes(ext);
  };

  const windowColor =
    replyWindowStatus === 'ok' ? 'text-green-600 dark:text-green-400'
    : replyWindowStatus === 'expiring' ? 'text-amber-600 dark:text-amber-400'
    : replyWindowStatus === 'expired' ? 'text-red-600 dark:text-red-400'
    : 'text-gray-500 dark:text-slate-400';
  const windowBarColor =
    replyWindowStatus === 'ok' ? 'bg-green-500'
    : replyWindowStatus === 'expiring' ? 'bg-amber-500'
    : replyWindowStatus === 'expired' ? 'bg-red-500'
    : 'bg-gray-400';
  const windowBarWidth =
    replyWindowStatus === 'ok' ? '100%'
    : replyWindowStatus === 'expiring' ? '20%'
    : replyWindowStatus === 'expired' ? '0%'
    : '100%';

  const remainingSuffix = replyWindowRemainingText
    ? t('conversations.channel.remainingSuffix', { remaining: replyWindowRemainingText })
    : '';
  const windowText =
    replyWindowStatus === 'ok'
      ? `${t('conversations.channel.line.windowOk')}${remainingSuffix}`
      : replyWindowStatus === 'expiring'
        ? `${t('conversations.channel.line.windowExpiring')}${remainingSuffix}`
        : replyWindowStatus === 'expired'
          ? t('conversations.channel.line.windowExpired')
          : t('conversations.channel.line.windowUnknown');

  // 审计 #13：字符计数与截断同口径 —— UTF-16 长度（后端 maxChars 就是按 UTF-16 比较的），
  // 不再用码点数，否则含 emoji 时"看起来还没满"却被截断。
  const charCount = value.length;
  const charCountColor = charCount > maxLength * 0.9 ? 'text-red-500 dark:text-red-400' : 'text-gray-400 dark:text-slate-500';
  const canSubmit = !submitting && !uploading && !readOnly && (value.trim().length > 0 || attachments.length > 0);

  return (
    <div
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
      className={`bg-white dark:bg-slate-800 rounded-xl border-2 border-dashed p-4 space-y-3 shadow-sm transition-colors relative ${
        isDragOver
          ? 'border-green-500 dark:border-green-400 bg-green-50/50 dark:bg-green-900/10'
          : 'border-green-200 dark:border-green-900/50'
      }`}
    >
      {isDragOver && (
        <div className="absolute inset-0 z-10 flex items-center justify-center pointer-events-none bg-green-50/80 dark:bg-green-900/20 rounded-xl">
          <div className="flex flex-col items-center gap-2 text-green-600 dark:text-green-400">
            <Paperclip className="w-8 h-8" />
            <span className="text-sm font-medium">{t('conversations.reply.dropToUpload')}</span>
          </div>
        </div>
      )}

      {/* replyToken 时效条：决定本次走 reply（免费）还是 push（耗配额、需好友关系） */}
      <div className="flex items-center gap-2 text-xs">
        <Clock className={`w-3.5 h-3.5 ${windowColor}`} />
        <span className={`min-w-0 ${windowColor}`} title={t('conversations.channel.line.windowTitle', { ttl: LINE_REPLY_TOKEN_TTL_SECONDS })}>
          {windowText}
        </span>
        <div className="flex-1 h-1.5 bg-gray-200 dark:bg-slate-700 rounded-full overflow-hidden min-w-[60px]">
          <div className={`h-full ${windowBarColor} transition-all duration-500`} style={{ width: windowBarWidth }} />
        </div>
      </div>

      {/* 人工接管静音提示（LINE 无官方接管 API ⇒ 静音只作用于本系统的自动回复） */}
      {humanHandoverActive && (
        <div className="flex items-center justify-between gap-2 text-xs bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 rounded-lg px-2.5 py-2">
          <span className="flex items-center gap-1.5 text-amber-700 dark:text-amber-300 min-w-0">
            <ShieldAlert className="w-3.5 h-3.5 flex-shrink-0" />
            <span className="min-w-0">
              {humanHandoverRemainingText
                ? t('conversations.channel.line.takeoverActiveWithRemaining', { remaining: humanHandoverRemainingText })
                : t('conversations.channel.line.takeoverActive')}
            </span>
          </span>
          {onReleaseTakeover && (
            <button
              type="button"
              onClick={() => void handleToggleTakeover()}
              disabled={takeoverBusy || submitting || readOnly}
              className="inline-flex items-center gap-1 text-amber-700 dark:text-amber-300 hover:underline disabled:opacity-50 flex-shrink-0"
            >
              {takeoverBusy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <TimerReset className="w-3.5 h-3.5" />}
              {t('conversations.channel.line.takeoverRelease')}
            </button>
          )}
        </div>
      )}

      {unreachable && (
        <div className="flex items-start gap-1.5 text-xs text-red-700 dark:text-red-300 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg px-2.5 py-2">
          <ShieldAlert className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" />
          <span className="min-w-0">{t('conversations.channel.line.unreachableHint')}</span>
        </div>
      )}

      <div className="flex items-center justify-between gap-2 flex-wrap">
        <div className="flex items-center gap-2 text-sm font-medium text-green-700 dark:text-green-400 min-w-0">
          <div className="p-1 bg-green-100 dark:bg-green-900/40 rounded flex-shrink-0">
            {/* SourceType.Line = 13 */}
            <SourceTypeIcon sourceType={13} className="w-3.5 h-3.5 text-green-600 dark:text-green-400" title={t('conversations.channel.line.title')} />
          </div>
          <span className="truncate">{t('conversations.channel.line.title')}</span>
        </div>

        <div className="flex items-center gap-2 sm:gap-3 flex-shrink-0 flex-wrap">
          {/* 快捷回复 */}
          <div className="relative">
            <button
              type="button"
              onClick={() => setShowQuickReplies((v) => !v)}
              disabled={readOnly || submitting}
              className="flex items-center gap-1 text-xs text-gray-500 dark:text-slate-400 hover:text-green-600 dark:hover:text-green-400 disabled:opacity-40 disabled:cursor-not-allowed transition-colors rounded px-1 py-0.5"
              title={t('conversations.reply.quickReplies')}
              aria-label={t('conversations.reply.quickReplies')}
              aria-expanded={showQuickReplies}
            >
              <Zap className="w-3.5 h-3.5" />
              <span className="hidden sm:inline">{t('conversations.reply.quick')}</span>
            </button>
            {showQuickReplies && (
              <>
                <div className="fixed inset-0 z-10" onClick={() => setShowQuickReplies(false)} aria-hidden="true" />
                <div className="absolute right-0 top-full mt-1 z-20 w-72 max-w-[calc(100vw-2rem)] bg-white dark:bg-slate-700 border border-gray-200 dark:border-slate-600 rounded-lg shadow-lg overflow-hidden">
                  <div className="px-3 py-2 text-[11px] font-medium text-gray-500 dark:text-slate-400 bg-gray-50 dark:bg-slate-800 border-b border-gray-100 dark:border-slate-600 flex items-center justify-between">
                    <span>{t('conversations.reply.quickReplies')}</span>
                    <span className="text-[10px] opacity-70">{t('conversations.reply.quickRepliesHint', { count: QUICK_REPLY_KEYS.length })}</span>
                  </div>
                  <ul className="max-h-60 overflow-y-auto">
                    {QUICK_REPLY_KEYS.map((key, idx) => (
                      <li key={idx}>
                        <button
                          type="button"
                          onClick={() => appendQuickReply(t(key))}
                          disabled={readOnly || submitting}
                          className="w-full text-left px-3 py-2 text-xs text-gray-700 dark:text-slate-200 hover:bg-green-50 dark:hover:bg-green-900/30 disabled:opacity-50 transition-colors flex items-center gap-2"
                        >
                          <span className="flex-shrink-0 w-4 h-4 rounded bg-gray-100 dark:bg-slate-600 text-[10px] font-medium text-gray-500 dark:text-slate-400 flex items-center justify-center">
                            {idx + 1}
                          </span>
                          <span className="flex-1 truncate">{t(key)}</span>
                        </button>
                      </li>
                    ))}
                  </ul>
                </div>
              </>
            )}
          </div>

          {/* 手动接管（无 replyToken 也能开启静音） */}
          {!humanHandoverActive && onTakeover && (
            <button
              type="button"
              onClick={() => void handleToggleTakeover()}
              disabled={takeoverBusy || submitting || readOnly}
              className="flex items-center gap-1 text-xs text-gray-500 dark:text-slate-400 hover:text-amber-600 dark:hover:text-amber-400 disabled:opacity-40 transition-colors rounded px-1 py-0.5"
              title={t('conversations.channel.line.takeoverStart')}
              aria-label={t('conversations.channel.line.takeoverStart')}
            >
              {takeoverBusy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <UserCheck className="w-3.5 h-3.5" />}
              <span className="hidden sm:inline">{t('conversations.channel.line.takeoverStart')}</span>
            </button>
          )}

          <button
            type="button"
            onClick={() => fileInputRef.current?.click()}
            disabled={readOnly || uploading || submitting}
            className="flex items-center gap-1 text-xs text-gray-500 dark:text-slate-400 hover:text-green-600 dark:hover:text-green-400 disabled:opacity-40 disabled:cursor-not-allowed transition-colors rounded px-1 py-0.5"
            title={t('conversations.channel.line.uploadTitle')}
            aria-label={t('conversations.channel.line.uploadAria')}
          >
            {uploading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Paperclip className="w-3.5 h-3.5" />}
            <span className="hidden sm:inline">{uploading ? t('conversations.reply.uploading') : t('conversations.reply.attachments')}</span>
          </button>

          <input ref={fileInputRef} type="file" multiple onChange={(e) => void handleFileSelect(e.target.files)} className="hidden" />

          <span className={`text-xs ${charCountColor} whitespace-nowrap`} title={t('conversations.reply.charCountTitle')}>
            {charCount}/{maxLength}
            {expectedChunks > 1 && (
              <span className="ml-1">({t('conversations.channel.line.splitInto', { count: expectedChunks })})</span>
            )}
          </span>
        </div>
      </div>

      <textarea
        ref={textareaRef}
        value={value}
        onChange={(e) => onChange(clampLineText(e.target.value, maxLength))}
        onKeyDown={handleKeyDown}
        onPaste={handlePaste}
        disabled={readOnly || submitting}
        placeholder={t('conversations.channel.line.placeholder')}
        rows={3}
        maxLength={maxLength}
        aria-label={t('conversations.channel.line.textareaAria')}
        className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-green-500 text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 resize-none disabled:bg-gray-50 dark:disabled:bg-slate-800 transition-shadow"
      />

      {attachments.length > 0 && (
        <div className="space-y-2">
          {attachments.filter((att) => att.mimeType.startsWith('image/')).length > 0 && (
            <div className="flex flex-wrap gap-2">
              {attachments.filter((att) => att.mimeType.startsWith('image/')).map((att) => (
                <div key={att.id} className="relative group w-16 h-16 sm:w-20 sm:h-20 rounded-lg overflow-hidden border border-gray-200 dark:border-slate-600">
                  <img src={att.url} alt={att.fileName} loading="lazy" className="w-full h-full object-cover" title={`${att.fileName} (${formatFileSize(att.size)})`} />
                  <button
                    type="button"
                    onClick={() => removeAttachment(att.id)}
                    disabled={submitting}
                    className="absolute top-0.5 right-0.5 w-5 h-5 bg-black/60 hover:bg-red-500 text-white rounded-full flex items-center justify-center opacity-0 group-hover:opacity-100 disabled:opacity-40 transition-all"
                    title={t('conversations.reply.remove')}
                    aria-label={t('conversations.reply.removeImageAria', { name: att.fileName })}
                  >
                    <X className="w-3 h-3" />
                  </button>
                </div>
              ))}
            </div>
          )}
          {attachments.filter((att) => !att.mimeType.startsWith('image/')).length > 0 && (
            <div className="space-y-1.5">
              {attachments.filter((att) => !att.mimeType.startsWith('image/')).map((att) => (
                <div key={att.id} className="flex items-center gap-2 px-2.5 py-1.5 bg-gray-50 dark:bg-slate-700/50 rounded-lg text-xs border border-gray-100 dark:border-slate-600/50">
                  <span className="flex-shrink-0">{attachmentIcon(att.mimeType)}</span>
                  <span className="flex-1 min-w-0 truncate text-gray-700 dark:text-slate-200">{att.fileName}</span>
                  <span className="text-gray-400 dark:text-slate-500 flex-shrink-0 hidden sm:inline">{formatFileSize(att.size)}</span>
                  <button
                    type="button"
                    onClick={() => removeAttachment(att.id)}
                    disabled={submitting}
                    className="flex-shrink-0 text-gray-400 hover:text-red-500 disabled:opacity-40 transition-colors"
                    title={t('conversations.reply.remove')}
                    aria-label={t('conversations.reply.removeAttachmentAria', { name: att.fileName })}
                  >
                    <X className="w-3.5 h-3.5" />
                  </button>
                </div>
              ))}
            </div>
          )}
          {/* 出站附件走签名直链，提示交付方式与总数上限 */}
          <p className="text-[11px] text-gray-500 dark:text-slate-400">
            {t('conversations.channel.line.attachmentLinkHint', {
              count: attachments.length,
              nonNative: attachments.filter((att) => !isNativeImage(att) && att.mimeType.startsWith('image/')).length,
            })}
          </p>
        </div>
      )}

      {totalOutboundMessages > 5 && (
        <p className="text-[11px] text-amber-700 dark:text-amber-400">
          {t('conversations.channel.line.tooManyMessages', { count: totalOutboundMessages })}
        </p>
      )}

      <div className="flex items-center justify-between gap-3">
        <p className="text-xs text-gray-500 dark:text-slate-400 flex-1 min-w-0">
          <span className="hidden sm:inline">{t('conversations.channel.line.footerHint')}</span>
          <span className="sm:hidden">{t('conversations.channel.textAndAttachments')}</span>
        </p>
        <button
          type="button"
          onClick={handleSubmit}
          disabled={!canSubmit}
          className="inline-flex items-center gap-1.5 px-3 sm:px-4 py-2 bg-gradient-to-r from-green-600 to-emerald-600 hover:from-green-700 hover:to-emerald-700 disabled:from-gray-300 dark:disabled:from-slate-700 disabled:cursor-not-allowed text-white rounded-lg text-sm font-medium shadow-sm transition-all flex-shrink-0"
          aria-label={t('conversations.channel.line.sendAria')}
        >
          {submitting ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
          {submitting ? t('conversations.reply.sendingEllipsis') : t('conversations.reply.send')}
        </button>
      </div>
    </div>
  );
}
