import { useRef, useEffect, useState, useCallback } from 'react';
import {
  Send, Loader2, Paperclip, X, Image as ImageIcon, Music, Video, FileText,
  Zap, Smile, Keyboard, AlertTriangle, Clock, Info, Tag, Scissors,
} from 'lucide-react';
import { attachmentApi } from '../api';
import { messengerApi } from '../api/modules/system';
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
  /** 会话 ID：用于 typing_on / mark_seen 输入态提示（Messenger sender_action） */
  conversationId?: number;
  /** 24 小时人工客服窗口状态：'ok' | 'warning' | 'expired' | 'unknown' */
  windowStatus?: 'ok' | 'warning' | 'expired' | 'unknown';
  /** 出站文本分段阈值（MessengerOptions.TextChunkChars），仅用于提示分段数 */
  chunkChars?: number;
  /** message_tag 策略：'auto' | 'always' | 'none'，仅用于窗口外提示文案 */
  messageTagPolicy?: 'auto' | 'always' | 'none';
}

// Messenger Platform 媒体限制（Graph API 文档标注值，真机待证实；超限由后端与 Graph 双重拦截）
// 图片 8MB / 音频 8MB / 视频 25MB / 文件 25MB（attachment 上传接口另有更严的上限）
const MESSENGER_MAX_FILE_SIZE_BY_TYPE: Record<string, number> = {
  image: 8 * 1024 * 1024,
  audio: 8 * 1024 * 1024,
  video: 25 * 1024 * 1024,
  file: 25 * 1024 * 1024,
};
const MESSENGER_MAX_FILE_SIZE_DEFAULT = 8 * 1024 * 1024;

// Messenger 支持的媒体 MIME 前缀
const MESSENGER_ALLOWED_MIME_PREFIXES = ['image/', 'audio/', 'video/'];
// Messenger 文件消息常见扩展名
const MESSENGER_FILE_EXTENSIONS = [
  '.txt', '.pdf', '.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx', '.zip', '.csv',
];

function getMaxSizeForFile(file: File): number {
  const name = file.name.toLowerCase();
  if (file.type.startsWith('image/')) return MESSENGER_MAX_FILE_SIZE_BY_TYPE.image;
  if (file.type.startsWith('audio/')) return MESSENGER_MAX_FILE_SIZE_BY_TYPE.audio;
  if (file.type.startsWith('video/')) return MESSENGER_MAX_FILE_SIZE_BY_TYPE.video;
  if (MESSENGER_FILE_EXTENSIONS.some((ext) => name.endsWith(ext))) return MESSENGER_MAX_FILE_SIZE_BY_TYPE.file;
  return MESSENGER_MAX_FILE_SIZE_DEFAULT;
}

function getMaxSizeLabelForFile(file: File): string {
  const bytes = getMaxSizeForFile(file);
  if (bytes >= 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(0)}MB`;
  return `${(bytes / 1024).toFixed(0)}KB`;
}

// Messenger 单条文本消息实际上限 5000 字符；超过 TextChunkChars 时后端自动分段发送
const MESSENGER_TEXT_MAX_LENGTH = 5000;
const DEFAULT_CHUNK_CHARS = 2000;

const MESSENGER_EMOJIS: string[] = [
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
 * Messenger 会话专用回复框。
 * 支持文本回复 + 图片/音频/视频/文件附件（经 Graph API 附件上传接口发送），
 * 并在输入时下发 typing_on sender_action。
 * 24 小时人工客服窗口内自由发送；窗口外由后端按 message_tag 策略（默认 auto → HUMAN_AGENT）处理，
 * 因此这里不禁用发送，只作提示。
 */
export default function MessengerReplyBox({
  value,
  onChange,
  onSubmit,
  submitting = false,
  readOnly = false,
  conversationId,
  windowStatus = 'ok',
  chunkChars = DEFAULT_CHUNK_CHARS,
  messageTagPolicy = 'auto',
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

  // typing_on 节流：仅在输入内容变化且距上次 >=8s 时下发一次（Meta 对 sender_action 有频控）
  const lastTypingAtRef = useRef(0);
  const typingTimerRef = useRef<number | null>(null);

  const pushTypingIndicator = useCallback(() => {
    if (!conversationId || readOnly || submitting) return;
    const now = Date.now();
    if (now - lastTypingAtRef.current < 8000) return;
    if (typingTimerRef.current) window.clearTimeout(typingTimerRef.current);
    typingTimerRef.current = window.setTimeout(() => {
      lastTypingAtRef.current = now;
      // sender_action 失败不影响回复流程，静默忽略
      messengerApi.senderAction({ conversationId, action: 'typing_on' }).catch(() => undefined);
    }, 1200);
  }, [conversationId, readOnly, submitting]);

  useEffect(() => () => {
    if (typingTimerRef.current) window.clearTimeout(typingTimerRef.current);
  }, []);

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
      const isFile = MESSENGER_FILE_EXTENSIONS.some((ext) => name.endsWith(ext));
      const isMedia =
        file.type && MESSENGER_ALLOWED_MIME_PREFIXES.some((prefix) => file.type.startsWith(prefix));

      if (!isFile && !isMedia) {
        errors.push(t('conversations.channel.messenger.unsupportedType', { name: file.name }));
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
  // Messenger 窗口外仍可发送（带 message_tag），故不用 windowExpired 拦发送
  const canSubmit =
    !submitting && !uploading && !readOnly &&
    (value.trim().length > 0 || attachments.length > 0);

  const charCountColor =
    value.length > MESSENGER_TEXT_MAX_LENGTH * 0.9
      ? 'text-red-500 dark:text-red-400'
      : 'text-gray-400 dark:text-slate-500';

  // 分段预览：后端按 TextChunkChars 自动拆分长文本
  const chunkLimit = chunkChars > 0 ? chunkChars : DEFAULT_CHUNK_CHARS;
  const chunkCount = value.length > chunkLimit ? Math.ceil(value.length / chunkLimit) : 1;

  const handleQuickReply = (text: string) => {
    const sep = value && !value.endsWith('\n') ? '\n' : '';
    const next = (value + sep + text).slice(0, MESSENGER_TEXT_MAX_LENGTH);
    onChange(next);
    setShowQuickReplies(false);
    pushTypingIndicator();
    requestAnimationFrame(() => textareaRef.current?.focus());
  };

  const handleEmojiInsert = (emoji: string) => {
    const el = textareaRef.current;
    if (!el) {
      const next = (value + emoji).slice(0, MESSENGER_TEXT_MAX_LENGTH);
      onChange(next);
      return;
    }
    const start = el.selectionStart ?? value.length;
    const end = el.selectionEnd ?? value.length;
    const next = (value.slice(0, start) + emoji + value.slice(end)).slice(0, MESSENGER_TEXT_MAX_LENGTH);
    onChange(next);
    pushTypingIndicator();
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
          ? 'border-blue-500 dark:border-blue-400 bg-blue-50/60 dark:bg-blue-900/10 shadow-blue-500/20 shadow-lg scale-[1.005]'
          : windowExpired
          ? 'border-amber-300/60 dark:border-amber-800/50 bg-gradient-to-br from-amber-50/60 via-white to-white dark:from-amber-900/10 dark:via-slate-800 dark:to-slate-800'
          : 'border-dashed border-blue-200/70 dark:border-blue-900/50 bg-gradient-to-br from-white via-white to-blue-50/30 dark:from-slate-800 dark:via-slate-800 dark:to-blue-900/10 hover:border-blue-300/80 dark:hover:border-blue-800/70'
      }`}
    >
      {isDragOver && (
        <div className="absolute inset-0 z-20 flex items-center justify-center pointer-events-none bg-blue-50/85 dark:bg-blue-900/25 rounded-2xl backdrop-blur-sm animate-pulse">
          <div className="flex flex-col items-center gap-2.5 text-blue-700 dark:text-blue-300">
            <div className="p-2 rounded-full bg-white dark:bg-slate-800 shadow-lg">
              <Paperclip className="w-8 h-8" />
            </div>
            <span className="text-sm font-semibold">{t('conversations.reply.dropToUpload')}</span>
            <span className="text-[11px] opacity-80">{t('conversations.channel.messenger.dropHint')}</span>
          </div>
        </div>
      )}

      {/* 24h 窗口过期提示：Messenger 允许带 message_tag 继续发送，故仅提示不禁用 */}
      {windowExpired && (
        <div className="relative flex items-start gap-3 px-4 py-3.5 bg-gradient-to-r from-amber-50 to-orange-50 dark:from-amber-900/20 dark:to-orange-900/10 border border-amber-200 dark:border-amber-800/40 rounded-xl overflow-hidden">
          <div className="absolute top-0 left-0 right-0 h-0.5 bg-gradient-to-r from-amber-400 via-orange-400 to-amber-400 animate-pulse" />
          <div className="flex-shrink-0 w-9 h-9 rounded-xl bg-amber-100 dark:bg-amber-900/40 flex items-center justify-center ring-1 ring-amber-200 dark:ring-amber-800/40">
            <AlertTriangle className="w-4.5 h-4.5 text-amber-600 dark:text-amber-400" />
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-sm font-semibold text-amber-800 dark:text-amber-200 flex items-center gap-1.5 flex-wrap">
              {t('conversations.channel.messenger.windowExpired')}
              <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-amber-200/60 dark:bg-amber-800/30 text-[10px] font-semibold text-amber-700 dark:text-amber-300">
                <Tag className="w-2.5 h-2.5" />
                {messageTagPolicy === 'none'
                  ? t('conversations.channel.messenger.tagNone')
                  : t('conversations.channel.messenger.tagApplied')}
              </span>
            </p>
            <p className="text-xs text-amber-700/90 dark:text-amber-400/80 mt-1 leading-relaxed">
              {t('conversations.channel.messenger.windowExpiredDesc')}
            </p>
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
              {t('conversations.channel.messenger.windowWarning')}
            </p>
            <p className="text-[11px] text-yellow-700/90 dark:text-yellow-400/80 mt-0.5 leading-relaxed">
              {t('conversations.channel.messenger.windowWarningDesc')}
            </p>
          </div>
        </div>
      )}

      {/* 顶部工具栏 */}
      <div className="flex items-center justify-between gap-2 sm:gap-3">
        <div className="flex items-center gap-2 text-sm font-semibold min-w-0">
          <div className="relative">
            <div className="absolute inset-0 rounded-lg bg-blue-400/20 blur-sm" />
            <div className="relative p-1.5 bg-gradient-to-br from-blue-100 to-sky-100 dark:from-blue-900/50 dark:to-sky-900/40 rounded-lg ring-1 ring-blue-200 dark:ring-blue-800/40">
              <SourceTypeIcon sourceType={11} className="w-4 h-4 text-blue-700 dark:text-blue-300" title={t('conversations.channel.messenger.title')} />
            </div>
          </div>
          <div className="flex flex-col min-w-0">
            <span className="text-blue-800 dark:text-blue-300 truncate leading-tight">{t('conversations.channel.messenger.title')}</span>
            <span className="text-[10px] text-blue-600/70 dark:text-blue-400/60 font-normal hidden sm:inline">
              {t('conversations.channel.messenger.subtitle')}
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
              disabled={readOnly || submitting}
              className={`group relative inline-flex items-center gap-1 px-2 sm:px-2.5 py-1.5 rounded-lg text-xs font-medium transition-all disabled:opacity-40 disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400 focus-visible:ring-offset-1 dark:focus-visible:ring-offset-slate-800 ${
                showQuickReplies
                  ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300 ring-1 ring-blue-200 dark:ring-blue-800/40'
                  : 'text-gray-500 dark:text-slate-400 hover:text-blue-600 dark:hover:text-blue-400 hover:bg-blue-50 dark:hover:bg-blue-900/20'
              }`}
              title={t('conversations.channel.messenger.quickRepliesTitle')}
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
                    <span className="text-[10px] opacity-70 font-mono">{t('conversations.channel.messenger.quickRepliesHint', { count: QUICK_REPLY_KEYS.length })}</span>
                  </div>
                  <ul className="max-h-72 overflow-y-auto py-1">
                    {QUICK_REPLY_KEYS.map((key, idx) => (
                      <li key={idx}>
                        <button
                          type="button"
                          onClick={() => handleQuickReply(t(key))}
                          disabled={readOnly || submitting}
                          className="group w-full text-left px-4 py-2.5 hover:bg-blue-50 dark:hover:bg-blue-900/20 disabled:opacity-50 transition-colors flex items-center gap-3 focus-visible:outline-none focus-visible:bg-blue-50 dark:focus-visible:bg-blue-900/20"
                        >
                          <span className="flex-shrink-0 w-6 h-6 rounded-md bg-gradient-to-br from-gray-100 to-gray-200 dark:from-slate-700 dark:to-slate-600 text-[11px] font-bold text-gray-600 dark:text-slate-300 flex items-center justify-center group-hover:from-blue-100 group-hover:to-blue-200 dark:group-hover:from-blue-900/50 dark:group-hover:to-blue-800/50 group-hover:text-blue-700 dark:group-hover:text-blue-300 transition-all">
                            {idx + 1}
                          </span>
                          <span className="flex-1 truncate text-xs sm:text-sm text-gray-700 dark:text-slate-200 group-hover:text-blue-700 dark:group-hover:text-blue-300">{t(key)}</span>
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
            disabled={readOnly || uploading || submitting}
            className="group relative inline-flex items-center gap-1 px-2 sm:px-2.5 py-1.5 rounded-lg text-xs font-medium transition-all disabled:opacity-40 disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400 focus-visible:ring-offset-1 dark:focus-visible:ring-offset-slate-800 text-gray-500 dark:text-slate-400 hover:text-blue-600 dark:hover:text-blue-400 hover:bg-blue-50 dark:hover:bg-blue-900/20"
            title={t('conversations.channel.messenger.uploadTitle')}
            aria-label={t('conversations.channel.messenger.uploadAria')}
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
              disabled={readOnly || submitting}
              className={`group relative inline-flex items-center gap-1 px-2 sm:px-2.5 py-1.5 rounded-lg text-xs font-medium transition-all disabled:opacity-40 disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400 focus-visible:ring-offset-1 dark:focus-visible:ring-offset-slate-800 ${
                showEmojis
                  ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300 ring-1 ring-blue-200 dark:ring-blue-800/40'
                  : 'text-gray-500 dark:text-slate-400 hover:text-blue-600 dark:hover:text-blue-400 hover:bg-blue-50 dark:hover:bg-blue-900/20'
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
                  {MESSENGER_EMOJIS.map((emoji, idx) => (
                    <button
                      key={idx}
                      type="button"
                      onClick={() => handleEmojiInsert(emoji)}
                      disabled={readOnly || submitting}
                      className="w-9 h-9 flex items-center justify-center text-xl rounded-lg hover:bg-blue-50 dark:hover:bg-blue-900/30 hover:scale-110 active:scale-95 disabled:opacity-50 disabled:hover:scale-100 transition-all duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
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
            accept="image/*,audio/*,video/*,.txt,.pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.zip,.csv"
            onChange={(e) => void handleFileSelect(e.target.files)}
            className="hidden"
          />

          {/* 字符计数 */}
          <div
            className={`hidden sm:inline-flex items-center gap-1 px-2 py-1 rounded-md text-[11px] font-mono transition-all ${
              value.length > MESSENGER_TEXT_MAX_LENGTH * 0.9
                ? 'bg-red-50 text-red-600 dark:bg-red-900/20 dark:text-red-300 ring-1 ring-red-200 dark:ring-red-800/40'
                : value.length > MESSENGER_TEXT_MAX_LENGTH * 0.7
                ? 'bg-amber-50 text-amber-600 dark:bg-amber-900/20 dark:text-amber-300'
                : 'bg-gray-50 text-gray-500 dark:bg-slate-700/60 dark:text-slate-400'
            }`}
            title={t('conversations.channel.messenger.charCountTitle')}
          >
            <span>{value.length}</span>
            <span className="opacity-40">/</span>
            <span className="opacity-70">{MESSENGER_TEXT_MAX_LENGTH}</span>
          </div>
          <span className={`sm:hidden text-[11px] font-mono ${charCountColor}`}>
            {value.length}/{MESSENGER_TEXT_MAX_LENGTH}
          </span>

          {/* 快捷键帮助 */}
          <div className="relative">
            <button
              ref={shortcutsButtonRef}
              type="button"
              onClick={() => setShowShortcuts((v) => !v)}
              disabled={readOnly || submitting}
              className={`group relative inline-flex items-center p-1.5 rounded-lg text-xs transition-all disabled:opacity-40 disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400 focus-visible:ring-offset-1 dark:focus-visible:ring-offset-slate-800 ${
                showShortcuts
                  ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300 ring-1 ring-blue-200 dark:ring-blue-800/40'
                  : 'text-gray-400 dark:text-slate-500 hover:text-blue-600 dark:hover:text-blue-400 hover:bg-blue-50 dark:hover:bg-blue-900/20'
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
        <div className="absolute -inset-0.5 rounded-xl bg-gradient-to-r from-blue-200 via-sky-200 to-blue-200 dark:from-blue-800/40 dark:via-sky-800/40 dark:to-blue-800/40 opacity-0 group-focus-within:opacity-60 blur-[2px] transition-opacity duration-300 pointer-events-none" />
        <textarea
          ref={textareaRef}
          value={value}
          onChange={(e) => {
            onChange(e.target.value.slice(0, MESSENGER_TEXT_MAX_LENGTH));
            pushTypingIndicator();
          }}
          onKeyDown={handleKeyDown}
          onPaste={handlePaste}
          disabled={readOnly || submitting}
          placeholder={
            windowExpired
              ? t('conversations.channel.messenger.placeholderExpired')
              : t('conversations.channel.messenger.placeholder')
          }
          rows={3}
          aria-label={t('conversations.channel.messenger.textareaAria')}
          className="relative w-full px-3.5 py-3 bg-white dark:bg-slate-900/80 border border-gray-200 dark:border-slate-700 rounded-xl text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 resize-none transition-all duration-200 shadow-inner disabled:bg-gray-50 dark:disabled:bg-slate-800/70 disabled:cursor-not-allowed focus:outline-none focus:ring-2 focus:ring-blue-400/60 dark:focus:ring-blue-500/50 focus:border-blue-300 dark:focus:border-blue-700 focus:shadow-lg focus:shadow-blue-500/10"
        />
      </div>

      {/* 长文本自动分段提示 */}
      {chunkCount > 1 && (
        <div className="flex items-center gap-2 text-[11px] text-gray-500 dark:text-slate-400 px-1">
          <Scissors className="w-3 h-3 flex-shrink-0 text-blue-500" />
          <span>{t('conversations.channel.messenger.chunkHint', { count: chunkCount, chars: chunkLimit })}</span>
        </div>
      )}

      {/* 附件列表 */}
      {attachments.length > 0 && (
        <div className="space-y-2.5 p-3 rounded-xl bg-gray-50/60 dark:bg-slate-700/30 border border-gray-100 dark:border-slate-700/50">
          <div className="flex items-center justify-between px-1 mb-1">
            <span className="text-[11px] font-semibold text-gray-500 dark:text-slate-400 flex items-center gap-1.5">
              <Paperclip className="w-3 h-3" />
              {t('conversations.channel.messenger.pendingAttachments', { count: attachments.length })}
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
                    title={t('conversations.channel.messenger.removeNamed', { name: att.fileName })}
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
                  className="group flex items-center gap-3 px-3 py-2.5 bg-white dark:bg-slate-800 rounded-xl border border-gray-100 dark:border-slate-700/60 hover:border-blue-200 dark:hover:border-blue-800/50 hover:shadow-sm transition-all"
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
            {t('conversations.channel.messenger.footerHint')}
          </span>
          <span className="sm:hidden">
            {t('conversations.channel.messenger.footerHintShort')}
          </span>
        </p>
        <button
          type="button"
          onClick={handleSubmit}
          disabled={!canSubmit}
          className="group relative inline-flex items-center justify-center gap-2 px-5 py-2.5 bg-gradient-to-r from-blue-600 via-sky-600 to-blue-700 hover:from-blue-700 hover:via-sky-700 hover:to-blue-800 disabled:from-gray-300 dark:disabled:from-slate-700 dark:disabled:via-slate-700 dark:disabled:to-slate-700 disabled:cursor-not-allowed text-white rounded-xl text-sm font-semibold shadow-md shadow-blue-500/25 hover:shadow-lg hover:shadow-blue-500/30 disabled:shadow-none transition-all duration-200 active:scale-[0.98] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400 focus-visible:ring-offset-2 dark:focus-visible:ring-offset-slate-800 overflow-hidden"
          aria-label={t('conversations.channel.messenger.sendAria')}
        >
          <span className="absolute inset-0 bg-gradient-to-r from-white/0 via-white/15 to-white/0 opacity-0 group-hover:opacity-100 translate-x-[-100%] group-hover:translate-x-[100%] transition-transform duration-700" />
          {submitting ? (
            <Loader2 className="w-4.5 h-4.5 animate-spin" />
          ) : windowExpired ? (
            <Tag className="w-4.5 h-4.5" />
          ) : (
            <Send className="w-4.5 h-4.5 group-hover:translate-x-0.5 transition-transform" />
          )}
          {submitting
            ? t('conversations.reply.sendingEllipsis')
            : canSubmit
            ? t('conversations.channel.messenger.sendNow')
            : t('conversations.channel.messenger.sendReady')}
        </button>
      </div>
    </div>
  );
}
