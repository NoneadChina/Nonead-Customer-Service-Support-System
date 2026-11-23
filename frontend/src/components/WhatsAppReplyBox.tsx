import { useRef, useEffect, useState, useCallback } from 'react';
import { Link } from 'react-router-dom';
import {
  Send, Loader2, Paperclip, X, Image as ImageIcon, Music, Video, FileText,
  Zap, Smile, Keyboard, AlertTriangle, Clock, ExternalLink, Info,
} from 'lucide-react';
import { attachmentApi } from '../api';
import type { AttachmentResponse } from '../api/types';
import type { TranslationKey } from '../locales/zh-Hans';
import { useI18n } from '../context/I18nContext';
import { useToast } from '../context/ToastContext';
import SourceTypeIcon from './SourceTypeIcon';

interface Props {
  value: string;
  onChange: (value: string) => void;
  onSubmit: (attachmentIds?: number[]) => Promise<void> | void;
  submitting?: boolean;
  readOnly?: boolean;
  /** 24 小时客户服务窗口状态：'ok' | 'warning' | 'expired' | 'unknown' */
  windowStatus?: 'ok' | 'warning' | 'expired' | 'unknown';
}

// WhatsApp Cloud API 媒体类型与大小限制
// 图片 5MB / 音频 16MB / 视频 16MB / 文档 100MB / 贴纸 500KB
const WHATSAPP_MAX_FILE_SIZE_BY_TYPE: Record<string, number> = {
  image: 5 * 1024 * 1024,
  audio: 16 * 1024 * 1024,
  video: 16 * 1024 * 1024,
  document: 100 * 1024 * 1024,
  sticker: 500 * 1024,
};
const WHATSAPP_MAX_FILE_SIZE_DEFAULT = 5 * 1024 * 1024;

// WhatsApp 允许的媒体 MIME 前缀（含 document）
const WHATSAPP_ALLOWED_MIME_PREFIXES = ['image/', 'audio/', 'video/'];
// WhatsApp 文档类型白名单
const WHATSAPP_DOCUMENT_EXTENSIONS = [
  '.txt', '.pdf', '.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx',
];
// WhatsApp 贴纸仅支持 webp
const WHATSAPP_STICKER_EXTENSIONS = ['.webp'];

function getMaxSizeForFile(file: File): number {
  const name = file.name.toLowerCase();
  if (name.endsWith('.webp')) return WHATSAPP_MAX_FILE_SIZE_BY_TYPE.sticker;
  if (WHATSAPP_DOCUMENT_EXTENSIONS.some((ext) => name.endsWith(ext))) {
    return WHATSAPP_MAX_FILE_SIZE_BY_TYPE.document;
  }
  if (file.type.startsWith('image/')) return WHATSAPP_MAX_FILE_SIZE_BY_TYPE.image;
  if (file.type.startsWith('audio/')) return WHATSAPP_MAX_FILE_SIZE_BY_TYPE.audio;
  if (file.type.startsWith('video/')) return WHATSAPP_MAX_FILE_SIZE_BY_TYPE.video;
  return WHATSAPP_MAX_FILE_SIZE_DEFAULT;
}

function getMaxSizeLabelForFile(file: File): string {
  const bytes = getMaxSizeForFile(file);
  if (bytes >= 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(0)}MB`;
  return `${(bytes / 1024).toFixed(0)}KB`;
}

// WhatsApp 文本消息上限 4096 字符
const WHATSAPP_TEXT_MAX_LENGTH = 4096;

const WHATSAPP_EMOJIS: string[] = [
  '😀', '😊', '🙂', '😄', '🤝', '👍', '👌', '🙏',
  '💪', '🎉', '❤️', '🌸', '☀️', '⭐', '✨', '☑️',
  '📢', '💡', '🔔', '📅', '⏰', '📝', '📌', '🎁',
];

/** 常用快捷回复短语的真源 key（渲染时必须经 t() 解析，直接渲染会漏出原始 key） */
const QUICK_REPLY_KEYS: TranslationKey[] = [
  'conversations.reply.quickReply1',
  'conversations.reply.quickReply2',
  'conversations.reply.quickReply3',
  'conversations.reply.quickReply4',
  'conversations.reply.quickReply5',
  'conversations.reply.quickReply6',
];

/**
 * WhatsApp 会话专用回复框。
 * 支持文本回复 + 图片/音频/视频/文档附件（通过 WhatsApp Cloud API 发送）。
 * 24 小时客户服务窗口内可自由回复，超出窗口需使用模板消息。
 */
export default function WhatsAppReplyBox({
  value,
  onChange,
  onSubmit,
  submitting = false,
  readOnly = false,
  windowStatus = 'ok',
}: Props) {
  const toast = useToast();
  const { t } = useI18n();
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [attachments, setAttachments] = useState<AttachmentResponse[]>([]);
  const [uploading, setUploading] = useState(false);
  const [showQuickReplies, setShowQuickReplies] = useState(false);
  const [showEmojis, setShowEmojis] = useState(false);
  const [showShortcuts, setShowShortcuts] = useState(false);
  const [isDragOver, setIsDragOver] = useState(false);

  const quickReplyButtonRef = useRef<HTMLButtonElement>(null);
  const shortcutsButtonRef = useRef<HTMLButtonElement>(null);
  const emojiPanelRef = useRef<HTMLDivElement>(null);
  const emojiButtonRef = useRef<HTMLButtonElement>(null);

  // 点击 emoji 面板外部时关闭
  useEffect(() => {
    if (!showEmojis) return;
    const handler = (e: MouseEvent) => {
      if (
        emojiPanelRef.current && !emojiPanelRef.current.contains(e.target as Node) &&
        emojiButtonRef.current && !emojiButtonRef.current.contains(e.target as Node)
      ) {
        setShowEmojis(false);
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [showEmojis]);

  // 三个浮层的统一 Esc 关闭 + 焦点恢复
  useEffect(() => {
    if (!showEmojis && !showQuickReplies && !showShortcuts) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return;
      if (showEmojis) {
        e.preventDefault();
        setShowEmojis(false);
        requestAnimationFrame(() => emojiButtonRef.current?.focus());
        return;
      }
      if (showQuickReplies) {
        e.preventDefault();
        setShowQuickReplies(false);
        requestAnimationFrame(() => quickReplyButtonRef.current?.focus());
        return;
      }
      if (showShortcuts) {
        e.preventDefault();
        setShowShortcuts(false);
        requestAnimationFrame(() => shortcutsButtonRef.current?.focus());
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [showEmojis, showQuickReplies, showShortcuts]);

  // 自动调整 textarea 高度
  useEffect(() => {
    const el = textareaRef.current;
    if (!el) return;
    el.style.height = 'auto';
    el.style.height = `${Math.min(el.scrollHeight, 200)}px`;
  }, [value]);

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
      e.preventDefault();
      handleSubmit();
      return;
    }
    if (showQuickReplies && !e.ctrlKey && !e.metaKey && !e.altKey) {
      const num = parseInt(e.key, 10);
      if (!isNaN(num) && num >= 1 && num <= QUICK_REPLY_KEYS.length) {
        e.preventDefault();
        handleQuickReply(t(QUICK_REPLY_KEYS[num - 1]));
      }
      if (e.key === 'Escape') {
        e.preventDefault();
        setShowQuickReplies(false);
      }
    }
  };

  const handleSubmit = useCallback(() => {
    const attIds = attachments.length > 0 ? attachments.map((a) => a.id) : undefined;
    if (!submitting && !readOnly && (value.trim() || attachments.length > 0)) {
      void onSubmit(attIds);
    }
  }, [attachments, value, submitting, readOnly, onSubmit]);

  const handleFileSelect = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    await handleFilesUpload(Array.from(files));
  };

  const handleFilesUpload = async (fileArr: File[]) => {
    if (fileArr.length === 0) return;

    const validFiles: File[] = [];
    const errors: string[] = [];

    for (const file of fileArr) {
      const name = file.name.toLowerCase();
      const isDocument = WHATSAPP_DOCUMENT_EXTENSIONS.some((ext) => name.endsWith(ext));
      const isSticker = WHATSAPP_STICKER_EXTENSIONS.some((ext) => name.endsWith(ext));
      const isMedia =
        file.type && WHATSAPP_ALLOWED_MIME_PREFIXES.some((prefix) => file.type.startsWith(prefix));

      if (!isDocument && !isSticker && !isMedia) {
        errors.push(t('conversations.channel.whatsApp.unsupportedType', { name: file.name }));
        continue;
      }
      const maxSize = getMaxSizeForFile(file);
      if (file.size > maxSize) {
        errors.push(
          t('conversations.channel.fileTooLarge', { name: file.name, size: getMaxSizeLabelForFile(file) })
        );
        continue;
      }
      validFiles.push(file);
    }

    if (errors.length > 0) {
      toast.error(errors.join('; '));
    }
    if (validFiles.length === 0) return;

    setUploading(true);
    try {
      const result = await attachmentApi.uploadBatch(validFiles);
      const newAtts = result?.data ?? [];
      setAttachments((prev) => [...prev, ...newAtts]);
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('conversations.reply.uploadFailed'));
    } finally {
      setUploading(false);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  };

  // 粘贴图片支持
  const handlePaste = (e: React.ClipboardEvent<HTMLTextAreaElement>) => {
    if (readOnly || submitting) return;
    const items = e.clipboardData?.items;
    if (!items) return;
    const imageFiles: File[] = [];
    for (let i = 0; i < items.length; i++) {
      const item = items[i];
      if (item.kind === 'file' && item.type.startsWith('image/')) {
        const file = item.getAsFile();
        if (file) {
          const ext = file.type.split('/')[1] || 'png';
          const renamed = new File([file], `paste_${Date.now()}.${ext}`, { type: file.type });
          imageFiles.push(renamed);
        }
      }
    }
    if (imageFiles.length > 0) {
      e.preventDefault();
      void handleFilesUpload(imageFiles);
    }
  };

  const handleDragOver = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    if (readOnly || submitting) return;
    if (e.dataTransfer.types.includes('Files')) {
      setIsDragOver(true);
    }
  };

  const handleDragLeave = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    if (e.currentTarget === e.target) {
      setIsDragOver(false);
    }
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

  const getAttachmentIcon = (mimeType: string) => {
    if (mimeType.startsWith('image/')) return <ImageIcon className="w-3.5 h-3.5 text-blue-500" />;
    if (mimeType.startsWith('audio/')) return <Music className="w-3.5 h-3.5 text-purple-500" />;
    if (mimeType.startsWith('video/')) return <Video className="w-3.5 h-3.5 text-orange-500" />;
    return <FileText className="w-3.5 h-3.5 text-gray-400" />;
  };

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes}B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)}MB`;
  };

  const windowExpired = windowStatus === 'expired';
  const canSubmit =
    !submitting && !uploading && !readOnly && !windowExpired &&
    (value.trim().length > 0 || attachments.length > 0);

  const charCountColor =
    value.length > WHATSAPP_TEXT_MAX_LENGTH * 0.9
      ? 'text-red-500 dark:text-red-400'
      : 'text-gray-400 dark:text-slate-500';

  const handleQuickReply = (text: string) => {
    const sep = value && !value.endsWith('\n') ? '\n' : '';
    const next = (value + sep + text).slice(0, WHATSAPP_TEXT_MAX_LENGTH);
    onChange(next);
    setShowQuickReplies(false);
    requestAnimationFrame(() => textareaRef.current?.focus());
  };

  const handleEmojiInsert = (emoji: string) => {
    const el = textareaRef.current;
    if (!el) {
      const next = (value + emoji).slice(0, WHATSAPP_TEXT_MAX_LENGTH);
      onChange(next);
      return;
    }
    const start = el.selectionStart ?? value.length;
    const end = el.selectionEnd ?? value.length;
    const next = (value.slice(0, start) + emoji + value.slice(end)).slice(0, WHATSAPP_TEXT_MAX_LENGTH);
    onChange(next);
    requestAnimationFrame(() => {
      if (textareaRef.current) {
        const pos = start + emoji.length;
        textareaRef.current.focus();
        textareaRef.current.setSelectionRange(pos, pos);
      }
    });
  };

  return (
    <div
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
      className={`relative rounded-2xl p-4 sm:p-5 space-y-3 sm:space-y-4 shadow-sm border-2 transition-all duration-300 ${
        isDragOver
          ? 'border-emerald-500 dark:border-emerald-400 bg-emerald-50/60 dark:bg-emerald-900/10 shadow-emerald-500/20 shadow-lg scale-[1.005]'
          : windowExpired
          ? 'border-amber-300/60 dark:border-amber-800/50 bg-gradient-to-br from-amber-50/60 via-white to-white dark:from-amber-900/10 dark:via-slate-800 dark:to-slate-800'
          : 'border-dashed border-emerald-200/70 dark:border-emerald-900/50 bg-gradient-to-br from-white via-white to-emerald-50/30 dark:from-slate-800 dark:via-slate-800 dark:to-emerald-900/10 hover:border-emerald-300/80 dark:hover:border-emerald-800/70'
      }`}
    >
      {isDragOver && (
        <div className="absolute inset-0 z-20 flex items-center justify-center pointer-events-none bg-emerald-50/85 dark:bg-emerald-900/25 rounded-2xl backdrop-blur-sm animate-pulse">
          <div className="flex flex-col items-center gap-2.5 text-emerald-700 dark:text-emerald-300">
            <div className="p-2 rounded-full bg-white dark:bg-slate-800 shadow-lg">
              <Paperclip className="w-8 h-8" />
            </div>
            <span className="text-sm font-semibold">{t('conversations.reply.dropToUpload')}</span>
            <span className="text-[11px] opacity-80">{t('conversations.channel.whatsApp.dropHint')}</span>
          </div>
        </div>
      )}

      {/* 24h 窗口过期警告 */}
      {windowExpired && (
        <div className="relative flex items-start gap-3 px-4 py-3.5 bg-gradient-to-r from-amber-50 to-orange-50 dark:from-amber-900/20 dark:to-orange-900/10 border border-amber-200 dark:border-amber-800/40 rounded-xl overflow-hidden">
          <div className="absolute top-0 left-0 right-0 h-0.5 bg-gradient-to-r from-amber-400 via-orange-400 to-amber-400 animate-pulse" />
          <div className="flex-shrink-0 w-9 h-9 rounded-xl bg-amber-100 dark:bg-amber-900/40 flex items-center justify-center ring-1 ring-amber-200 dark:ring-amber-800/40">
            <AlertTriangle className="w-4.5 h-4.5 text-amber-600 dark:text-amber-400" />
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-sm font-semibold text-amber-800 dark:text-amber-200 flex items-center gap-1.5">
              {t('conversations.channel.whatsApp.windowExpired')}
              <span className="inline-flex items-center px-1.5 py-0.5 rounded bg-amber-200/60 dark:bg-amber-800/30 text-[10px] font-semibold text-amber-700 dark:text-amber-300">
                {t('conversations.channel.whatsApp.cannotSendFree')}
              </span>
            </p>
            <p className="text-xs text-amber-700/90 dark:text-amber-400/80 mt-1 leading-relaxed">
              {t('conversations.channel.whatsApp.windowExpiredDesc')}
            </p>
            <Link
              to="/whatsapp?tab=template"
              className="inline-flex items-center gap-1.5 mt-2.5 px-3 py-1.5 rounded-lg bg-gradient-to-r from-amber-600 to-orange-600 hover:from-amber-700 hover:to-orange-700 text-white text-xs font-semibold transition-all shadow-md shadow-amber-500/20 active:scale-[0.98] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber-400 focus-visible:ring-offset-1 dark:focus-visible:ring-offset-slate-800"
              aria-label={t('conversations.channel.whatsApp.templateLinkAria')}
              title={t('conversations.channel.whatsApp.templateLinkTitle')}
            >
              <ExternalLink className="w-3 h-3" />
              {t('conversations.channel.goToTemplate')}
            </Link>
          </div>
        </div>
      )}

      {/* 窗口即将过期提醒 */}
      {windowStatus === 'warning' && (
        <div className="relative flex items-start gap-3 px-4 py-3 bg-gradient-to-r from-yellow-50 to-amber-50/70 dark:from-yellow-900/15 dark:to-amber-900/10 border border-yellow-200/80 dark:border-yellow-800/40 rounded-xl overflow-hidden">
          <div className="absolute top-0 left-0 right-0 h-0.5 bg-gradient-to-r from-yellow-400 to-amber-400" />
          <div className="flex-shrink-0 w-8 h-8 rounded-lg bg-yellow-100 dark:bg-yellow-900/40 flex items-center justify-center ring-1 ring-yellow-200 dark:ring-yellow-800/40">
            <Clock className="w-4 h-4 text-yellow-600 dark:text-yellow-400" />
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-xs font-semibold text-yellow-800 dark:text-yellow-200">
              {t('conversations.channel.whatsApp.windowWarning')}
            </p>
            <p className="text-[11px] text-yellow-700/90 dark:text-yellow-400/80 mt-0.5 leading-relaxed">
              {t('conversations.channel.whatsApp.windowWarningDesc')}
            </p>
          </div>
        </div>
      )}

      {/* 顶部工具栏 */}
      <div className="flex items-center justify-between gap-2 sm:gap-3">
        <div className="flex items-center gap-2 text-sm font-semibold min-w-0">
          <div className="relative">
            <div className="absolute inset-0 rounded-lg bg-emerald-400/20 blur-sm" />
            <div className="relative p-1.5 bg-gradient-to-br from-emerald-100 to-teal-100 dark:from-emerald-900/50 dark:to-teal-900/40 rounded-lg ring-1 ring-emerald-200 dark:ring-emerald-800/40">
              <SourceTypeIcon sourceType={8} className="w-4 h-4 text-emerald-700 dark:text-emerald-300" title={t('conversations.channel.whatsApp.title')} />
            </div>
          </div>
          <div className="flex flex-col min-w-0">
            <span className="text-emerald-800 dark:text-emerald-300 truncate leading-tight">{t('conversations.channel.whatsApp.title')}</span>
            <span className="text-[10px] text-emerald-600/70 dark:text-emerald-400/60 font-normal hidden sm:inline">
              {t('conversations.channel.whatsApp.subtitle')}
            </span>
          </div>
        </div>
        <div className="flex items-center gap-1 sm:gap-2 flex-shrink-0">
          {/* 快捷回复 */}
          <div className="relative">
            <button
              ref={quickReplyButtonRef}
              type="button"
              onClick={() => setShowQuickReplies((v) => !v)}
              disabled={readOnly || submitting || windowExpired}
              className={`group relative inline-flex items-center gap-1 px-2 sm:px-2.5 py-1.5 rounded-lg text-xs font-medium transition-all disabled:opacity-40 disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald-400 focus-visible:ring-offset-1 dark:focus-visible:ring-offset-slate-800 ${
                showQuickReplies
                  ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300 ring-1 ring-emerald-200 dark:ring-emerald-800/40'
                  : 'text-gray-500 dark:text-slate-400 hover:text-emerald-600 dark:hover:text-emerald-400 hover:bg-emerald-50 dark:hover:bg-emerald-900/20'
              }`}
              title={t('conversations.channel.whatsApp.quickRepliesTitle')}
              aria-label={t('conversations.reply.quickReplies')}
              aria-expanded={showQuickReplies}
            >
              <Zap className="w-3.5 h-3.5" />
              <span className="hidden sm:inline">{t('conversations.reply.quick')}</span>
            </button>
            {showQuickReplies && (
              <>
                <div className="fixed inset-0 z-30" onClick={() => setShowQuickReplies(false)} aria-hidden="true" />
                <div className="absolute right-0 top-full mt-2 z-40 w-80 sm:w-96 max-w-[calc(100vw-2rem)] bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl shadow-xl overflow-hidden animate-in fade-in slide-in-from-right-2 duration-150">
                  <div className="px-4 py-2.5 text-[11px] font-semibold text-gray-600 dark:text-slate-300 bg-gradient-to-r from-gray-50 to-white dark:from-slate-800 dark:to-slate-800 border-b border-gray-100 dark:border-slate-700/60 flex items-center justify-between">
                    <span className="flex items-center gap-1.5">
                      <Zap className="w-3 h-3 text-amber-500" />
                      {t('conversations.reply.quickReplies')}
                    </span>
                    <span className="text-[10px] opacity-70 font-mono">{t('conversations.channel.whatsApp.quickRepliesHint', { count: QUICK_REPLY_KEYS.length })}</span>
                  </div>
                  <ul className="max-h-72 overflow-y-auto py-1">
                    {QUICK_REPLY_KEYS.map((key, idx) => (
                      <li key={idx}>
                        <button
                          type="button"
                          onClick={() => handleQuickReply(t(key))}
                          disabled={readOnly || submitting}
                          className="group w-full text-left px-4 py-2.5 hover:bg-emerald-50 dark:hover:bg-emerald-900/20 disabled:opacity-50 transition-colors flex items-center gap-3 focus-visible:outline-none focus-visible:bg-emerald-50 dark:focus-visible:bg-emerald-900/20"
                        >
                          <span className="flex-shrink-0 w-6 h-6 rounded-md bg-gradient-to-br from-gray-100 to-gray-200 dark:from-slate-700 dark:to-slate-600 text-[11px] font-bold text-gray-600 dark:text-slate-300 flex items-center justify-center group-hover:from-emerald-100 group-hover:to-emerald-200 dark:group-hover:from-emerald-900/50 dark:group-hover:to-emerald-800/50 group-hover:text-emerald-700 dark:group-hover:text-emerald-300 transition-all">
                            {idx + 1}
                          </span>
                          <span className="flex-1 truncate text-xs sm:text-sm text-gray-700 dark:text-slate-200 group-hover:text-emerald-700 dark:group-hover:text-emerald-300">{t(key)}</span>
                        </button>
                      </li>
                    ))}
                  </ul>
                </div>
              </>
            )}
          </div>

          {/* 附件上传 */}
          <button
            type="button"
            onClick={() => fileInputRef.current?.click()}
            disabled={readOnly || uploading || submitting || windowExpired}
            className="group relative inline-flex items-center gap-1 px-2 sm:px-2.5 py-1.5 rounded-lg text-xs font-medium transition-all disabled:opacity-40 disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald-400 focus-visible:ring-offset-1 dark:focus-visible:ring-offset-slate-800 text-gray-500 dark:text-slate-400 hover:text-emerald-600 dark:hover:text-emerald-400 hover:bg-emerald-50 dark:hover:bg-emerald-900/20"
            title={t('conversations.channel.whatsApp.uploadTitle')}
            aria-label={t('conversations.channel.whatsApp.uploadAria')}
          >
            {uploading ? (
              <Loader2 className="w-3.5 h-3.5 animate-spin" />
            ) : (
              <Paperclip className="w-3.5 h-3.5 group-hover:-rotate-12 transition-transform" />
            )}
            <span className="hidden sm:inline">{uploading ? t('conversations.reply.uploading') : t('conversations.reply.attachments')}</span>
          </button>

          {/* Emoji */}
          <div className="relative">
            <button
              ref={emojiButtonRef}
              type="button"
              onClick={() => setShowEmojis((v) => !v)}
              disabled={readOnly || submitting || windowExpired}
              className={`group relative inline-flex items-center gap-1 px-2 sm:px-2.5 py-1.5 rounded-lg text-xs font-medium transition-all disabled:opacity-40 disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald-400 focus-visible:ring-offset-1 dark:focus-visible:ring-offset-slate-800 ${
                showEmojis
                  ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300 ring-1 ring-emerald-200 dark:ring-emerald-800/40'
                  : 'text-gray-500 dark:text-slate-400 hover:text-emerald-600 dark:hover:text-emerald-400 hover:bg-emerald-50 dark:hover:bg-emerald-900/20'
              }`}
              title={t('conversations.reply.insertEmoji')}
              aria-label={t('conversations.reply.insertEmoji')}
              aria-expanded={showEmojis}
            >
              <Smile className="w-3.5 h-3.5 group-hover:scale-110 transition-transform" />
              <span className="hidden sm:inline">{t('conversations.reply.emoji')}</span>
            </button>
            {showEmojis && (
              <div
                ref={emojiPanelRef}
                className="absolute right-0 top-full mt-2 z-40 w-72 max-w-[calc(100vw-2rem)] bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl shadow-xl overflow-hidden animate-in fade-in slide-in-from-right-2 duration-150"
              >
                <div className="px-4 py-2.5 text-[11px] font-semibold text-gray-600 dark:text-slate-300 bg-gradient-to-r from-gray-50 to-white dark:from-slate-800 dark:to-slate-800 border-b border-gray-100 dark:border-slate-700/60 flex items-center gap-1.5">
                  <Smile className="w-3 h-3 text-amber-500" />
                  {t('conversations.reply.commonEmojis')}
                </div>
                <div className="grid grid-cols-8 gap-0.5 p-2.5">
                  {WHATSAPP_EMOJIS.map((emoji, idx) => (
                    <button
                      key={idx}
                      type="button"
                      onClick={() => handleEmojiInsert(emoji)}
                      disabled={readOnly || submitting}
                      className="w-9 h-9 flex items-center justify-center text-xl rounded-lg hover:bg-emerald-50 dark:hover:bg-emerald-900/30 hover:scale-110 active:scale-95 disabled:opacity-50 disabled:hover:scale-100 transition-all duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald-400"
                      aria-label={t('conversations.reply.insertEmojiAria', { emoji })}
                    >
                      {emoji}
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>

          <input
            ref={fileInputRef}
            type="file"
            multiple
            accept="image/*,audio/*,video/*,.txt,.pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.webp"
            onChange={(e) => void handleFileSelect(e.target.files)}
            className="hidden"
          />

          {/* 字符计数 */}
          <div
            className={`hidden sm:inline-flex items-center gap-1 px-2 py-1 rounded-md text-[11px] font-mono transition-all ${
              value.length > WHATSAPP_TEXT_MAX_LENGTH * 0.9
                ? 'bg-red-50 text-red-600 dark:bg-red-900/20 dark:text-red-300 ring-1 ring-red-200 dark:ring-red-800/40'
                : value.length > WHATSAPP_TEXT_MAX_LENGTH * 0.7
                ? 'bg-amber-50 text-amber-600 dark:bg-amber-900/20 dark:text-amber-300'
                : 'bg-gray-50 text-gray-500 dark:bg-slate-700/60 dark:text-slate-400'
            }`}
            title={t('conversations.channel.whatsApp.charCountTitle')}
          >
            <span>{value.length}</span>
            <span className="opacity-40">/</span>
            <span className="opacity-70">{WHATSAPP_TEXT_MAX_LENGTH}</span>
          </div>
          <span className={`sm:hidden text-[11px] font-mono ${charCountColor}`}>
            {value.length}/{WHATSAPP_TEXT_MAX_LENGTH}
          </span>

          {/* 快捷键帮助 */}
          <div className="relative">
            <button
              ref={shortcutsButtonRef}
              type="button"
              onClick={() => setShowShortcuts((v) => !v)}
              disabled={readOnly || submitting}
              className={`group relative inline-flex items-center p-1.5 rounded-lg text-xs transition-all disabled:opacity-40 disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald-400 focus-visible:ring-offset-1 dark:focus-visible:ring-offset-slate-800 ${
                showShortcuts
                  ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300 ring-1 ring-emerald-200 dark:ring-emerald-800/40'
                  : 'text-gray-400 dark:text-slate-500 hover:text-emerald-600 dark:hover:text-emerald-400 hover:bg-emerald-50 dark:hover:bg-emerald-900/20'
              }`}
              title={t('conversations.reply.shortcuts')}
              aria-label={t('conversations.reply.shortcutsAria')}
              aria-expanded={showShortcuts}
            >
              <Keyboard className="w-3.5 h-3.5" />
            </button>
            {showShortcuts && (
              <>
                <div className="fixed inset-0 z-30" onClick={() => setShowShortcuts(false)} aria-hidden="true" />
                <div className="absolute right-0 top-full mt-2 z-40 w-72 max-w-[calc(100vw-2rem)] bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl shadow-xl overflow-hidden animate-in fade-in slide-in-from-right-2 duration-150">
                  <div className="px-4 py-2.5 text-[11px] font-semibold text-gray-600 dark:text-slate-300 bg-gradient-to-r from-gray-50 to-white dark:from-slate-800 dark:to-slate-800 border-b border-gray-100 dark:border-slate-700/60 flex items-center gap-1.5">
                    <Keyboard className="w-3 h-3 text-indigo-500" />
                    {t('conversations.reply.shortcuts')}
                  </div>
                  <ul className="py-1.5 px-2">
                    <li className="flex items-center justify-between px-2 py-1.5 rounded-md hover:bg-gray-50 dark:hover:bg-slate-700/40">
                      <span className="text-xs text-gray-700 dark:text-slate-200">{t('conversations.reply.shortcutSend')}</span>
                      <kbd className="px-2 py-0.5 bg-gradient-to-b from-gray-100 to-gray-200 dark:from-slate-700 dark:to-slate-600 border border-gray-200 dark:border-slate-500 rounded-md text-[10px] font-mono text-gray-600 dark:text-slate-200 shadow-sm">
                        Ctrl<span className="mx-0.5 opacity-40">+</span>Enter
                      </kbd>
                    </li>
                    <li className="flex items-center justify-between px-2 py-1.5 rounded-md hover:bg-gray-50 dark:hover:bg-slate-700/40">
                      <span className="text-xs text-gray-700 dark:text-slate-200">{t('conversations.reply.shortcutQuickRepliesPlain')}</span>
                      <kbd className="px-2 py-0.5 bg-gradient-to-b from-gray-100 to-gray-200 dark:from-slate-700 dark:to-slate-600 border border-gray-200 dark:border-slate-500 rounded-md text-[10px] font-mono text-gray-600 dark:text-slate-200 shadow-sm">
                        1~{QUICK_REPLY_KEYS.length}
                      </kbd>
                    </li>
                    <li className="flex items-center justify-between px-2 py-1.5 rounded-md hover:bg-gray-50 dark:hover:bg-slate-700/40">
                      <span className="text-xs text-gray-700 dark:text-slate-200">{t('conversations.reply.shortcutClosePanel')}</span>
                      <kbd className="px-2 py-0.5 bg-gradient-to-b from-gray-100 to-gray-200 dark:from-slate-700 dark:to-slate-600 border border-gray-200 dark:border-slate-500 rounded-md text-[10px] font-mono text-gray-600 dark:text-slate-200 shadow-sm">
                        Esc
                      </kbd>
                    </li>
                    <li className="flex items-center justify-between px-2 py-1.5 rounded-md hover:bg-gray-50 dark:hover:bg-slate-700/40">
                      <span className="text-xs text-gray-700 dark:text-slate-200">{t('conversations.reply.shortcutPasteImage')}</span>
                      <kbd className="px-2 py-0.5 bg-gradient-to-b from-gray-100 to-gray-200 dark:from-slate-700 dark:to-slate-600 border border-gray-200 dark:border-slate-500 rounded-md text-[10px] font-mono text-gray-600 dark:text-slate-200 shadow-sm">
                        Ctrl<span className="mx-0.5 opacity-40">+</span>V
                      </kbd>
                    </li>
                  </ul>
                </div>
              </>
            )}
          </div>
        </div>
      </div>

      {/* 文本输入区 */}
      <div className="relative group">
        <div className="absolute -inset-0.5 rounded-xl bg-gradient-to-r from-emerald-200 via-teal-200 to-emerald-200 dark:from-emerald-800/40 dark:via-teal-800/40 dark:to-emerald-800/40 opacity-0 group-focus-within:opacity-60 blur-[2px] transition-opacity duration-300 pointer-events-none" />
        <textarea
          ref={textareaRef}
          value={value}
          onChange={(e) => onChange(e.target.value.slice(0, WHATSAPP_TEXT_MAX_LENGTH))}
          onKeyDown={handleKeyDown}
          onPaste={handlePaste}
          disabled={readOnly || submitting || windowExpired}
          placeholder={
            windowExpired
              ? t('conversations.channel.whatsApp.placeholderExpired')
              : t('conversations.channel.whatsApp.placeholder')
          }
          rows={3}
          aria-label={t('conversations.channel.whatsApp.textareaAria')}
          className="relative w-full px-3.5 py-3 bg-white dark:bg-slate-900/80 border border-gray-200 dark:border-slate-700 rounded-xl text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 resize-none transition-all duration-200 shadow-inner disabled:bg-gray-50 dark:disabled:bg-slate-800/70 disabled:cursor-not-allowed focus:outline-none focus:ring-2 focus:ring-emerald-400/60 dark:focus:ring-emerald-500/50 focus:border-emerald-300 dark:focus:border-emerald-700 focus:shadow-lg focus:shadow-emerald-500/10"
        />
      </div>

      {/* 附件列表 */}
      {attachments.length > 0 && (
        <div className="space-y-2.5 p-3 rounded-xl bg-gray-50/60 dark:bg-slate-700/30 border border-gray-100 dark:border-slate-700/50">
          <div className="flex items-center justify-between px-1 mb-1">
            <span className="text-[11px] font-semibold text-gray-500 dark:text-slate-400 flex items-center gap-1.5">
              <Paperclip className="w-3 h-3" />
              {t('conversations.channel.whatsApp.pendingAttachments', { count: attachments.length })}
            </span>
          </div>
          {attachments.filter((att) => att.mimeType.startsWith('image/')).length > 0 && (
            <div className="flex flex-wrap gap-2.5">
              {attachments.filter((att) => att.mimeType.startsWith('image/')).map((att) => (
                <div key={att.id} className="relative group w-20 h-20 sm:w-24 sm:h-24 rounded-xl overflow-hidden border border-gray-200 dark:border-slate-600 shadow-sm hover:shadow-md hover:-translate-y-0.5 transition-all duration-200">
                  <div className="absolute inset-0 bg-gradient-to-t from-black/50 via-transparent to-transparent opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none z-10" />
                  <img
                    src={att.url}
                    alt={att.fileName}
                    className="w-full h-full object-cover transition-transform duration-300 group-hover:scale-105"
                    title={`${att.fileName} (${formatFileSize(att.size)})`}
                  />
                  <div className="absolute bottom-1 left-1 right-1 flex justify-between items-end z-20 opacity-0 group-hover:opacity-100 transition-opacity">
                    <span className="px-1.5 py-0.5 bg-black/60 text-white rounded text-[9px] font-mono truncate max-w-[75%]">
                      {formatFileSize(att.size)}
                    </span>
                  </div>
                  <button
                    type="button"
                    onClick={() => removeAttachment(att.id)}
                    disabled={submitting}
                    className="absolute top-1 right-1 z-30 w-6 h-6 bg-gradient-to-br from-red-500 to-rose-600 hover:from-red-600 hover:to-rose-700 text-white rounded-full flex items-center justify-center opacity-0 group-hover:opacity-100 disabled:opacity-40 transition-all shadow-md hover:scale-110 active:scale-95"
                    title={t('conversations.channel.whatsApp.removeNamed', { name: att.fileName })}
                    aria-label={t('conversations.reply.removeImageAria', { name: att.fileName })}
                  >
                    <X className="w-3.5 h-3.5" />
                  </button>
                </div>
              ))}
            </div>
          )}
          {attachments.filter((att) => !att.mimeType.startsWith('image/')).length > 0 && (
            <div className="grid sm:grid-cols-2 gap-2">
              {attachments.filter((att) => !att.mimeType.startsWith('image/')).map((att) => (
                <div
                  key={att.id}
                  className="group flex items-center gap-3 px-3 py-2.5 bg-white dark:bg-slate-800 rounded-xl border border-gray-100 dark:border-slate-700/60 hover:border-emerald-200 dark:hover:border-emerald-800/50 hover:shadow-sm transition-all"
                >
                  <div className={`w-10 h-10 rounded-lg flex items-center justify-center flex-shrink-0 ${
                    att.mimeType.startsWith('audio/')
                      ? 'bg-gradient-to-br from-purple-100 to-fuchsia-100 dark:from-purple-900/40 dark:to-fuchsia-900/30'
                      : att.mimeType.startsWith('video/')
                      ? 'bg-gradient-to-br from-orange-100 to-amber-100 dark:from-orange-900/40 dark:to-amber-900/30'
                      : 'bg-gradient-to-br from-slate-100 to-gray-100 dark:from-slate-700 dark:to-slate-600'
                  }`}>
                    {getAttachmentIcon(att.mimeType)}
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="text-xs font-medium text-gray-700 dark:text-slate-200 truncate">{att.fileName}</div>
                    <div className="text-[10px] text-gray-400 dark:text-slate-500 mt-0.5 font-mono">{formatFileSize(att.size)}</div>
                  </div>
                  <button
                    type="button"
                    onClick={() => removeAttachment(att.id)}
                    disabled={submitting}
                    className="flex-shrink-0 p-1.5 rounded-lg text-gray-400 hover:text-red-500 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/20 disabled:opacity-40 transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-red-400"
                    title={t('conversations.reply.remove')}
                    aria-label={t('conversations.reply.removeAttachmentAria', { name: att.fileName })}
                  >
                    <X className="w-4 h-4" />
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* 底部发送区 */}
      <div className="flex flex-col sm:flex-row sm:items-center gap-2.5 sm:gap-3 pt-2 sm:pt-1 border-t border-gray-100/60 dark:border-slate-700/40">
        <p className="text-[11px] text-gray-500 dark:text-slate-400 flex-1 min-w-0 leading-relaxed flex items-start sm:items-center gap-1.5">
          <Info className="w-3 h-3 flex-shrink-0 mt-0.5 sm:mt-0 opacity-70" />
          <span className="hidden sm:inline">
            {t('conversations.channel.whatsApp.footerHint')}
          </span>
          <span className="sm:hidden">
            {t('conversations.channel.whatsApp.footerHintShort')}
          </span>
        </p>
        <button
          type="button"
          onClick={handleSubmit}
          disabled={!canSubmit}
          className="group relative inline-flex items-center justify-center gap-2 px-5 py-2.5 bg-gradient-to-r from-emerald-600 via-teal-600 to-green-600 hover:from-emerald-700 hover:via-teal-700 hover:to-green-700 disabled:from-gray-300 dark:disabled:from-slate-700 dark:disabled:via-slate-700 dark:disabled:to-slate-700 disabled:cursor-not-allowed text-white rounded-xl text-sm font-semibold shadow-md shadow-emerald-500/25 hover:shadow-lg hover:shadow-emerald-500/30 disabled:shadow-none transition-all duration-200 active:scale-[0.98] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald-400 focus-visible:ring-offset-2 dark:focus-visible:ring-offset-slate-800 overflow-hidden"
          aria-label={t('conversations.channel.whatsApp.sendAria')}
        >
          <span className="absolute inset-0 bg-gradient-to-r from-white/0 via-white/15 to-white/0 opacity-0 group-hover:opacity-100 translate-x-[-100%] group-hover:translate-x-[100%] transition-transform duration-700" />
          {submitting ? (
            <Loader2 className="w-4.5 h-4.5 animate-spin" />
          ) : (
            <Send className="w-4.5 h-4.5 group-hover:translate-x-0.5 transition-transform" />
          )}
          {submitting
            ? t('conversations.reply.sendingEllipsis')
            : canSubmit
            ? t('conversations.channel.whatsApp.sendNow')
            : t('conversations.channel.whatsApp.sendReady')}
        </button>
      </div>
    </div>
  );
}
