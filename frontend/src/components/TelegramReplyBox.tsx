import { useRef, useEffect, useState, useCallback, useMemo } from 'react';
import { Send, Loader2, Paperclip, X, Image, Music, Video, FileText, Quote, ShieldAlert, RefreshCw } from 'lucide-react';
import { attachmentApi } from '../api';
import type { AttachmentResponse } from '../api/types';
import { useI18n } from '../context/I18nContext';
import { useToast } from '../context/ToastContext';
import SourceTypeIcon from './SourceTypeIcon';

/** 与后端 TelegramOptions.MaxMessageChars 一致：单条 sendMessage 文本上限 */
const TELEGRAM_TEXT_MAX_LENGTH = 4096;
/** 媒体消息 caption 上限（Telegram Bot API：caption 1024） */
const TELEGRAM_CAPTION_MAX_LENGTH = 1024;
/** 托管 Bot API 上传上限 50MB（自建 Local Bot API Server 为 2000MB，此处按托管口径限制前端） */
const TELEGRAM_MAX_FILE_SIZE = 50 * 1024 * 1024;
/** 入站下载上限 20MB：超出会落到"仅存 file_id"占位，回复时提醒坐席改用链接 */
const TELEGRAM_INBOUND_DOWNLOAD_MAX = 20 * 1024 * 1024;

/** Telegram 允许任意文档类型，仅大小受限；这里只挡明显无意义的 0 字节与超限文件 */
const TELEGRAM_ALLOWED_MIME_PREFIXES = ['image/', 'audio/', 'video/', 'text/', 'application/'];

type ParseMode = 'none' | 'HTML' | 'Markdown';

interface Props {
  value: string;
  onChange: (value: string) => void;
  onSubmit: (attachmentIds?: number[]) => Promise<void> | void;
  submitting?: boolean;
  readOnly?: boolean;
  maxLength?: number;
  /** 会话被标记为不可达（用户拉黑机器人 / 机器人被移出群）：403 后由后端写入内部标记 */
  unreachable?: boolean;
  /** 手动清除不可达标记（客户重新联系后可恢复发送） */
  onClearUnreachable?: () => Promise<void> | void;
  /** 格式化模式：none 纯文本 / HTML / Markdown（后端解析失败会自动降级重试） */
  parseMode?: ParseMode;
  onParseModeChange?: (mode: ParseMode) => void;
  /** 引用回复客户最后一条消息（reply_parameters） */
  quoteLastCustomerMessage?: boolean;
  onQuoteChange?: (value: boolean) => void;
}

/**
 * Telegram 会话专用回复框。
 * 与钉钉/WhatsApp 回复框的关键差异（务必让坐席看得见）：
 *  1) Telegram 没有 24 小时服务窗口，任何时候都能回复；但用户可能已拉黑机器人（403），
 *     此时会话被标记为"不可达"，继续发送只会重复失败，需坐席确认后清除标记。
 *  2) 单条文本上限 4096 字符，超出后端会自动按换行/空格切分多条发送（前端只做计数提示）。
 *  3) 带附件时正文变成 caption，上限降到 1024 字符。
 *  4) 无送达/已读回执，也没有第三方输入提示，发出去就是发出去了。
 */
export default function TelegramReplyBox({
  value,
  onChange,
  onSubmit,
  submitting = false,
  readOnly = false,
  maxLength = TELEGRAM_TEXT_MAX_LENGTH,
  unreachable = false,
  onClearUnreachable,
  parseMode = 'none',
  onParseModeChange,
  quoteLastCustomerMessage = false,
  onQuoteChange,
}: Props) {
  const toast = useToast();
  const { t } = useI18n();
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [attachments, setAttachments] = useState<AttachmentResponse[]>([]);
  const [uploading, setUploading] = useState(false);
  const [isDragOver, setIsDragOver] = useState(false);
  const [clearing, setClearing] = useState(false);

  // textarea 自增高
  useEffect(() => {
    const el = textareaRef.current;
    if (!el) return;
    el.style.height = 'auto';
    el.style.height = `${Math.min(el.scrollHeight, 220)}px`;
  }, [value]);

  const hasMedia = attachments.length > 0;
  const effectiveMax = hasMedia ? TELEGRAM_CAPTION_MAX_LENGTH : maxLength;

  // 预计分片数：仅提示，真实切分在后端（SplitLongMessage）
  const expectedParts = useMemo(() => {
    const len = value.length;
    if (len <= effectiveMax) return 1;
    return Math.ceil(len / effectiveMax);
  }, [value.length, effectiveMax]);

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
      e.preventDefault();
      handleSubmit();
    }
  };

  const handleSubmit = useCallback(() => {
    if (submitting || readOnly) return;
    if (!value.trim() && attachments.length === 0) return;
    const attIds = attachments.length > 0 ? attachments.map((a) => a.id) : undefined;
    void onSubmit(attIds);
  }, [attachments, value, submitting, readOnly, onSubmit]);

  const handleFilesUpload = async (fileArr: File[]) => {
    if (fileArr.length === 0) return;
    const validFiles: File[] = [];
    const errors: string[] = [];
    for (const file of fileArr) {
      const mimeOk = !file.type || TELEGRAM_ALLOWED_MIME_PREFIXES.some((p) => file.type.startsWith(p));
      if (!mimeOk) {
        errors.push(t('conversations.channel.telegram.unsupportedType', { name: file.name }));
        continue;
      }
      if (file.size > TELEGRAM_MAX_FILE_SIZE) {
        errors.push(t('conversations.channel.telegram.fileTooLarge', { name: file.name, limit: '50MB' }));
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

  const handleClearUnreachable = async () => {
    if (!onClearUnreachable) return;
    setClearing(true);
    try {
      await onClearUnreachable();
    } finally {
      setClearing(false);
    }
  };

  const attachmentIcon = (mimeType: string) => {
    if (mimeType.startsWith('image/')) return <Image className="w-3.5 h-3.5 text-blue-500" />;
    if (mimeType.startsWith('audio/')) return <Music className="w-3.5 h-3.5 text-purple-500" />;
    if (mimeType.startsWith('video/')) return <Video className="w-3.5 h-3.5 text-orange-500" />;
    if (mimeType.startsWith('text/') || mimeType === 'application/pdf') return <FileText className="w-3.5 h-3.5 text-teal-500" />;
    return <Paperclip className="w-3.5 h-3.5 text-gray-400" />;
  };

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes}B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)}MB`;
  };

  const canSubmit = !submitting && !uploading && !readOnly && (value.trim().length > 0 || attachments.length > 0);
  const charCountColor = value.length > effectiveMax ? 'text-amber-600 dark:text-amber-400'
    : value.length > effectiveMax * 0.9 ? 'text-red-500' : 'text-gray-400 dark:text-slate-500';

  return (
    <div
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
      className={`bg-white dark:bg-slate-800 rounded-xl border-2 border-dashed p-4 space-y-3 shadow-sm transition-colors relative ${
        isDragOver
          ? 'border-sky-500 dark:border-sky-400 bg-sky-50/50 dark:bg-sky-900/10'
          : 'border-sky-200 dark:border-sky-900/50'
      }`}
    >
      {isDragOver && (
        <div className="absolute inset-0 z-10 flex items-center justify-center pointer-events-none bg-sky-50/80 dark:bg-sky-900/20 rounded-xl">
          <div className="flex flex-col items-center gap-2 text-sky-600 dark:text-sky-400">
            <Paperclip className="w-8 h-8" />
            <span className="text-sm font-medium">{t('conversations.reply.dropToUpload')}</span>
          </div>
        </div>
      )}

      {/* 不可达（403）提示条：Telegram 无服务窗口概念，这里只表达"对方已无法接收" */}
      {unreachable && (
        <div className="flex items-center justify-between gap-2 text-xs bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg px-2.5 py-2">
          <span className="flex items-center gap-1.5 text-red-700 dark:text-red-300 min-w-0">
            <ShieldAlert className="w-3.5 h-3.5 flex-shrink-0" />
            <span className="min-w-0">{t('conversations.channel.telegram.unreachableHint')}</span>
          </span>
          {onClearUnreachable && (
            <button
              type="button"
              onClick={() => void handleClearUnreachable()}
              disabled={clearing || submitting || readOnly}
              className="inline-flex items-center gap-1 text-red-700 dark:text-red-300 hover:underline disabled:opacity-50 flex-shrink-0"
            >
              {clearing ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <RefreshCw className="w-3.5 h-3.5" />}
              {t('conversations.channel.telegram.clearUnreachable')}
            </button>
          )}
        </div>
      )}

      <div className="flex items-center justify-between gap-2 flex-wrap">
        <div className="flex items-center gap-2 text-sm font-medium text-sky-700 dark:text-sky-400 min-w-0">
          <div className="p-1 bg-sky-100 dark:bg-sky-900/40 rounded flex-shrink-0">
            <SourceTypeIcon sourceType={12} className="w-3.5 h-3.5 text-sky-600 dark:text-sky-400" title={t('conversations.channel.telegram.title')} />
          </div>
          <span className="truncate">{t('conversations.channel.telegram.title')}</span>
        </div>

        <div className="flex items-center gap-2 sm:gap-3 flex-shrink-0 flex-wrap">
          {/* 格式化模式：后端 parse_mode；HTML/Markdown 解析失败会自动去掉格式重试一次 */}
          {onParseModeChange && (
            <select
              value={parseMode}
              onChange={(e) => onParseModeChange(e.target.value as ParseMode)}
              disabled={readOnly || submitting}
              aria-label={t('conversations.channel.telegram.parseModeAria')}
              className="text-xs px-2 py-1 rounded-lg border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-gray-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-sky-500"
            >
              <option value="none">{t('conversations.channel.telegram.parseMode.none')}</option>
              <option value="HTML">{t('conversations.channel.telegram.parseMode.HTML')}</option>
              <option value="Markdown">{t('conversations.channel.telegram.parseMode.Markdown')}</option>
            </select>
          )}

          {onQuoteChange && (
            <label className="flex items-center gap-1 text-xs text-gray-500 dark:text-slate-400 cursor-pointer select-none">
              <input
                type="checkbox"
                checked={quoteLastCustomerMessage}
                onChange={(e) => onQuoteChange(e.target.checked)}
                disabled={readOnly || submitting}
              />
              <Quote className="w-3.5 h-3.5" />
              <span className="hidden sm:inline">{t('conversations.channel.telegram.quoteLast')}</span>
            </label>
          )}

          <button
            type="button"
            onClick={() => fileInputRef.current?.click()}
            disabled={readOnly || uploading || submitting}
            className="flex items-center gap-1 text-xs text-gray-500 dark:text-slate-400 hover:text-sky-600 dark:hover:text-sky-400 disabled:opacity-40 disabled:cursor-not-allowed transition-colors rounded px-1 py-0.5"
            title={t('conversations.channel.telegram.uploadTitle')}
            aria-label={t('conversations.channel.telegram.uploadAria')}
          >
            {uploading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Paperclip className="w-3.5 h-3.5" />}
            <span className="hidden sm:inline">{uploading ? t('conversations.reply.uploading') : t('conversations.reply.attachments')}</span>
          </button>

          <input
            ref={fileInputRef}
            type="file"
            multiple
            onChange={(e) => void handleFileSelect(e.target.files)}
            className="hidden"
          />

          <span className={`text-xs ${charCountColor} whitespace-nowrap`} title={t('conversations.reply.charCountTitle')}>
            {value.length}/{effectiveMax}
            {expectedParts > 1 && <span className="ml-1">({t('conversations.channel.telegram.splitInto', { count: expectedParts })})</span>}
          </span>
        </div>
      </div>

      <textarea
        ref={textareaRef}
        value={value}
        onChange={(e) => onChange(e.target.value.slice(0, maxLength))}
        onKeyDown={handleKeyDown}
        onPaste={handlePaste}
        disabled={readOnly || submitting}
        placeholder={hasMedia ? t('conversations.channel.telegram.placeholderCaption') : t('conversations.channel.telegram.placeholder')}
        rows={3}
        maxLength={maxLength}
        aria-label={t('conversations.channel.telegram.textareaAria')}
        className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-sky-500 text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 resize-none disabled:bg-gray-50 dark:disabled:bg-slate-800 transition-shadow"
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
                  {att.size > TELEGRAM_INBOUND_DOWNLOAD_MAX && (
                    <span className="text-amber-600 dark:text-amber-400 flex-shrink-0 hidden md:inline">{t('conversations.channel.telegram.largeFileHint')}</span>
                  )}
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
        </div>
      )}

      <div className="flex items-center justify-between gap-3">
        <p className="text-xs text-gray-500 dark:text-slate-400 flex-1 min-w-0">
          <span className="hidden sm:inline">{t('conversations.channel.telegram.footerHint')}</span>
          <span className="sm:hidden">{t('conversations.channel.textAndAttachments')}</span>
        </p>
        <button
          type="button"
          onClick={handleSubmit}
          disabled={!canSubmit}
          className="inline-flex items-center gap-1.5 px-3 sm:px-4 py-2 bg-gradient-to-r from-sky-600 to-blue-600 hover:from-sky-700 hover:to-blue-700 disabled:from-gray-300 dark:disabled:from-slate-700 disabled:cursor-not-allowed text-white rounded-lg text-sm font-medium shadow-sm transition-all flex-shrink-0"
          aria-label={t('conversations.channel.telegram.sendAria')}
        >
          {submitting ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
          {submitting ? t('conversations.reply.sendingEllipsis') : t('conversations.reply.send')}
        </button>
      </div>
    </div>
  );
}
