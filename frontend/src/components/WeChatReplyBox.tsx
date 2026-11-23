import { useRef, useEffect, useState, useCallback, useMemo } from 'react';
import { Link } from 'react-router-dom';
import { Send, Loader2, Paperclip, X, Image, Music, Video, Zap, Smile, Keyboard, AlertTriangle, Clock, ExternalLink } from 'lucide-react';
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
  maxLength?: number;
  /** 48 小时客服消息窗口状态：'ok' | 'warning' | 'expired' | 'unknown' */
  windowStatus?: 'ok' | 'warning' | 'expired' | 'unknown';
}

// 微信客服消息支持的媒体类型
const WECHAT_ALLOWED_MIME_PREFIXES = ['image/', 'audio/', 'video/'];

/** 微信允许的文件扩展名白名单（用于 file.type 为空时的回退校验，避免 .amr/.spx 等浏览器识别不出 MIME 的格式）*/
const WECHAT_ALLOWED_EXT = [
  'jpg','jpeg','png','gif','bmp','webp','svg',
  'mp3','wav','amr','m4a','aac','spx','speex','ogg',
  'mp4','mov','avi','mkv','webm','3gp'
];
// 微信临时素材大小限制按类型区分（与微信官方 API 一致）
const WECHAT_MAX_FILE_SIZE_BY_TYPE: Record<string, number> = {
  image: 2 * 1024 * 1024,   // 图片 2MB
  audio: 2 * 1024 * 1024,   // 语音 2MB
  video: 10 * 1024 * 1024,  // 视频 10MB
};
// 默认上限（保守值，用于未知类型兜底）
const WECHAT_MAX_FILE_SIZE_DEFAULT = 2 * 1024 * 1024;

/** 根据 MIME 类型获取对应大小上限（字节） */
function getMaxSizeForMime(mimeType: string): number {
  if (mimeType.startsWith('image/')) return WECHAT_MAX_FILE_SIZE_BY_TYPE.image;
  if (mimeType.startsWith('audio/')) return WECHAT_MAX_FILE_SIZE_BY_TYPE.audio;
  if (mimeType.startsWith('video/')) return WECHAT_MAX_FILE_SIZE_BY_TYPE.video;
  return WECHAT_MAX_FILE_SIZE_DEFAULT;
}

/** 根据 MIME 类型获取可读的大小上限（MB） */
function getMaxSizeLabelForMime(mimeType: string): string {
  const bytes = getMaxSizeForMime(mimeType);
  return `${(bytes / 1024 / 1024).toFixed(0)}MB`;
}

/** 当浏览器返回空 file.type 时，通过扩展名推断 MIME，用于大小上限匹配 */
function inferMimeFromExt(ext: string): string {
  const imageExts = ['jpg','jpeg','png','gif','bmp','webp','svg'];
  const audioExts = ['mp3','wav','amr','m4a','aac','spx','speex','ogg'];
  const videoExts = ['mp4','mov','avi','mkv','webm','3gp'];
  if (imageExts.includes(ext)) return 'image/*';
  if (audioExts.includes(ext)) return 'audio/*';
  if (videoExts.includes(ext)) return 'video/*';
  return '';
}
// 微信客服文本消息内容上限 600 字节；中文字符 UTF-8 占 3 字节，保守限制 200 字符

// 常用 Emoji 短列表（客服场景精选表情，避免过大面板）
const WECHAT_EMOJIS: string[] = [
  '😀', '😊', '🙂', '😄', '🤝', '👍', '👌', '🙏',
  '💪', '🎉', '❤️', '🌸', '☀️', '⭐', '✨', '☑️',
  '📢', '💡', '🔔', '📅', '⏰', '📝', '📌', '🎁',
];
const WECHAT_TEXT_MAX_LENGTH = 200;

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
 * 微信会话专用回复框。
 * 支持文本回复 + 图片/语音/视频附件（通过微信客服消息接口发送）。
 * 48 小时客服消息窗口内有效，超出时限需通过模板消息发送。
 */
export default function WeChatReplyBox({
  value,
  onChange,
  onSubmit,
  submitting = false,
  readOnly = false,
  maxLength = WECHAT_TEXT_MAX_LENGTH,
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

  // 三个浮层按钮的 ref，用来关闭浮层后把焦点还给触发按钮（符合 WCAG 焦点顺序）
  const quickReplyButtonRef = useRef<HTMLButtonElement>(null);
  const shortcutsButtonRef = useRef<HTMLButtonElement>(null);

  // 点击 emoji 面板外部时关闭
  const emojiPanelRef = useRef<HTMLDivElement>(null);
  const emojiButtonRef = useRef<HTMLButtonElement>(null);
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

  // 三个浮层的统一 Esc 关闭 + 焦点恢复到对应触发按钮
  useEffect(() => {
    if (!showEmojis && !showQuickReplies && !showShortcuts) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return;
      // 按浮层优先级依次关闭，一次 Esc 只关一个
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
    // Ctrl/Cmd+Enter 发送
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
      e.preventDefault();
      handleSubmit();
      return;
    }
    // 快捷回复面板打开时，按数字键 1-9 快速插入对应回复
    if (showQuickReplies && !e.ctrlKey && !e.metaKey && !e.altKey) {
      const num = parseInt(e.key, 10);
      if (!isNaN(num) && num >= 1 && num <= QUICK_REPLY_KEYS.length) {
        e.preventDefault();
        handleQuickReply(t(QUICK_REPLY_KEYS[num - 1]));
      }
      // Esc 关闭面板
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

  // 公共文件上传处理：校验大小/类型后批量上传
  const handleFilesUpload = async (fileArr: File[]) => {
    if (fileArr.length === 0) return;

    const validFiles: File[] = [];
    const errors: string[] = [];

    for (const file of fileArr) {
      // 类型校验：优先用 MIME，回退到文件扩展名（浏览器对 .amr/.spx 常返回空 file.type）
      const mimeValid = !!file.type && WECHAT_ALLOWED_MIME_PREFIXES.some((p) => file.type.startsWith(p));
      const extName = file.name.split('.').pop()?.toLowerCase() ?? '';
      const extValid = !file.type && WECHAT_ALLOWED_EXT.includes(extName);
      if (!mimeValid && !extValid) {
        errors.push(t('conversations.channel.wechat.unsupportedType', { name: file.name }));
        continue;
      }
      // 按类型获取大小上限：图片 2MB、语音 2MB、视频 10MB
      // 扩展名推断：即使 MIME 为空，也能用扩展名映射到正确的大小限制
      const inferredMime = file.type || inferMimeFromExt(extName);
      const maxSize = getMaxSizeForMime(inferredMime);
      if (file.size > maxSize) {
        errors.push(t('conversations.channel.fileTooLarge', { name: file.name, size: getMaxSizeLabelForMime(inferredMime) }));
        continue;
      }
      validFiles.push(file);
    }

    if (errors.length > 0) {
      toast.error(errors.join('; '));
    }
    if (validFiles.length === 0) {
      return;
    }

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

  // 粘贴图片支持：客服截屏后可直接 Ctrl+V 粘贴上传
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
          // 为粘贴的图片生成友好文件名
          const ext = file.type.split('/')[1] || 'png';
          const renamed = new File([file], `paste_${Date.now()}.${ext}`, { type: file.type });
          imageFiles.push(renamed);
        }
      }
    }
    if (imageFiles.length > 0) {
      e.preventDefault(); // 阻止粘贴图片到文本框（避免乱码）
      void handleFilesUpload(imageFiles);
    }
  };

  // 拖拽上传支持：客服可直接将图片/文件拖入回复框上传
  // 注意：即使 readOnly/submitting 也必须 preventDefault，否则浏览器会把文件当 URL 打开
  const handleDragOver = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    if (readOnly || submitting) return;
    if (e.dataTransfer.types.includes('Files')) {
      setIsDragOver(true);
    }
  };

  const handleDragLeave = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    // 仅当离开容器本身时才重置状态（避免经过子元素触发）
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
    if (mimeType.startsWith('image/')) return <Image className="w-3.5 h-3.5 text-blue-500" />;
    if (mimeType.startsWith('audio/')) return <Music className="w-3.5 h-3.5 text-purple-500" />;
    if (mimeType.startsWith('video/')) return <Video className="w-3.5 h-3.5 text-orange-500" />;
    return <Paperclip className="w-3.5 h-3.5 text-gray-400" />;
  };

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes}B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)}MB`;
  };

  // 窗口过期时禁用回复：微信客服消息仅在 48 小时窗口内可发送
  const windowExpired = windowStatus === 'expired';
  const canSubmit = !submitting && !uploading && !readOnly && !windowExpired && (value.trim().length > 0 || attachments.length > 0);

  // 微信客服文本消息上限 600 字节，UTF-8 下中文占 3 字节。
  // 计算当前内容实际字节占用，让客服直观了解剩余空间。
  const byteLength = useMemo(() => new Blob([value]).size, [value]);
  const WECHAT_TEXT_MAX_BYTES = 600;
  const byteCountColor = byteLength > WECHAT_TEXT_MAX_BYTES * 0.9
    ? 'text-red-500 dark:text-red-400'
    : byteLength > WECHAT_TEXT_MAX_BYTES * 0.7
    ? 'text-amber-500 dark:text-amber-400'
    : 'text-gray-400 dark:text-slate-500';
  const charCountColor = value.length > maxLength * 0.9 ? 'text-red-500' : 'text-gray-400 dark:text-slate-500';

  const handleQuickReply = (text: string) => {
    // 快捷短语追加到现有内容（用空行分隔），并限制总长度
    const sep = value && !value.endsWith('\n') ? '\n' : '';
    const next = (value + sep + text).slice(0, maxLength);
    onChange(next);
    setShowQuickReplies(false);
    // 回焦到 textarea
    requestAnimationFrame(() => textareaRef.current?.focus());
  };

  const handleEmojiInsert = (emoji: string) => {
    // Emoji 插入到光标位置，而非简单追加
    const el = textareaRef.current;
    if (!el) {
      const next = (value + emoji).slice(0, maxLength);
      onChange(next);
      return;
    }
    const start = el.selectionStart ?? value.length;
    const end = el.selectionEnd ?? value.length;
    const next = (value.slice(0, start) + emoji + value.slice(end)).slice(0, maxLength);
    onChange(next);
    // 恢复光标到 emoji 之后
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
      className={`bg-white dark:bg-slate-800 rounded-xl border-2 border-dashed p-4 space-y-3 shadow-sm transition-colors relative ${
        isDragOver
          ? 'border-green-500 dark:border-green-400 bg-green-50/50 dark:bg-green-900/10'
          : 'border-green-200 dark:border-green-900/50'
      }`}
    >
      {/* 拖拽提示遮罩 */}
      {isDragOver && (
        <div className="absolute inset-0 z-10 flex items-center justify-center pointer-events-none bg-green-50/80 dark:bg-green-900/20 rounded-xl">
          <div className="flex flex-col items-center gap-2 text-green-600 dark:text-green-400">
            <Paperclip className="w-8 h-8" />
            <span className="text-sm font-medium">{t('conversations.reply.dropToUpload')}</span>
          </div>
        </div>
      )}
      {/* 窗口过期警告 */}
      {windowExpired && (
        <div className="flex items-start gap-2 px-3 py-2.5 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800/50 rounded-lg">
          <AlertTriangle className="w-4 h-4 text-amber-500 dark:text-amber-400 flex-shrink-0 mt-0.5" />
          <div className="flex-1 min-w-0">
            <p className="text-xs font-medium text-amber-700 dark:text-amber-300">{t('conversations.channel.wechat.windowExpired')}</p>
            <p className="text-[11px] text-amber-600 dark:text-amber-400/80 mt-0.5">
              {t('conversations.channel.wechat.windowExpiredDesc')}
            </p>
            <Link
              to="/wechat?tab=template"
              className="inline-flex items-center gap-1 mt-1.5 px-2 py-1 rounded-md bg-amber-600 hover:bg-amber-700 text-white text-[11px] font-medium transition-colors"
              aria-label={t('conversations.channel.wechat.templateLinkAria')}
              title={t('conversations.channel.wechat.templateLinkTitle')}
            >
              <ExternalLink className="w-3 h-3" />
              {t('conversations.channel.goToTemplate')}
            </Link>
          </div>
        </div>
      )}
      {/* 窗口即将过期提醒 */}
      {windowStatus === 'warning' && (
        <div className="flex items-start gap-2 px-3 py-2 bg-yellow-50/70 dark:bg-yellow-900/10 border border-yellow-200/70 dark:border-yellow-800/40 rounded-lg">
          <Clock className="w-3.5 h-3.5 text-yellow-500 dark:text-yellow-400 flex-shrink-0 mt-0.5" />
          <p className="flex-1 text-[11px] text-yellow-700 dark:text-yellow-400/80">
            {t('conversations.channel.wechat.windowWarning')}
          </p>
        </div>
      )}
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-sm font-medium text-green-700 dark:text-green-400 min-w-0">
          <div className="p-1 bg-green-100 dark:bg-green-900/40 rounded flex-shrink-0">
            <SourceTypeIcon sourceType={7} className="w-3.5 h-3.5 text-green-600 dark:text-green-400" title={t('conversations.channel.wechat.title')} />
          </div>
          <span className="truncate">{t('conversations.channel.wechat.title')}</span>
        </div>
        <div className="flex items-center gap-2 sm:gap-3 flex-shrink-0">
          {/* 快捷回复按钮 */}
          <div className="relative">
            <button
              ref={quickReplyButtonRef}
              type="button"
              onClick={() => setShowQuickReplies((v) => !v)}
              disabled={readOnly || submitting}
              className="flex items-center gap-1 text-xs text-gray-500 dark:text-slate-400 hover:text-green-600 dark:hover:text-green-400 disabled:opacity-40 disabled:cursor-not-allowed transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-green-400 focus-visible:ring-offset-1 rounded px-1 py-0.5"
              title={t('conversations.reply.quickReplies')}
              aria-label={t('conversations.reply.quickReplies')}
              aria-expanded={showQuickReplies}
            >
              <Zap className="w-3.5 h-3.5" />
              <span className="hidden sm:inline">{t('conversations.reply.quick')}</span>
            </button>
            {showQuickReplies && (
              <>
                {/* 关闭层：点击外部关闭 */}
                <div
                  className="fixed inset-0 z-10"
                  onClick={() => setShowQuickReplies(false)}
                  aria-hidden="true"
                />
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
                          onClick={() => handleQuickReply(t(key))}
                          disabled={readOnly || submitting}
                          className="w-full text-left px-3 py-2 text-xs text-gray-700 dark:text-slate-200 hover:bg-green-50 dark:hover:bg-green-900/30 hover:text-green-700 dark:hover:text-green-400 disabled:opacity-50 transition-colors flex items-center gap-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-green-400"
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
          {/* 附件上传按钮 */}
          <button
            type="button"
            onClick={() => fileInputRef.current?.click()}
            disabled={readOnly || uploading || submitting}
            className="flex items-center gap-1 text-xs text-gray-500 dark:text-slate-400 hover:text-green-600 dark:hover:text-green-400 disabled:opacity-40 disabled:cursor-not-allowed transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-green-400 focus-visible:ring-offset-1 rounded px-1 py-0.5"
            title={t('conversations.channel.wechat.uploadTitle')}
            aria-label={t('conversations.channel.wechat.uploadAria')}
          >
            {uploading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Paperclip className="w-3.5 h-3.5" />}
            <span className="hidden sm:inline">{uploading ? t('conversations.reply.uploading') : t('conversations.reply.attachments')}</span>
          </button>
          {/* Emoji 表情按钮 */}
          <div className="relative">
            <button
              ref={emojiButtonRef}
              type="button"
              onClick={() => setShowEmojis((v) => !v)}
              disabled={readOnly || submitting}
              className={`flex items-center gap-1 text-xs transition-colors disabled:opacity-40 disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-green-400 focus-visible:ring-offset-1 rounded px-1 py-0.5 ${
                showEmojis
                  ? 'text-green-600 dark:text-green-400'
                  : 'text-gray-500 dark:text-slate-400 hover:text-green-600 dark:hover:text-green-400'
              }`}
              title={t('conversations.reply.insertEmoji')}
              aria-label={t('conversations.reply.insertEmoji')}
              aria-expanded={showEmojis}
            >
              <Smile className="w-3.5 h-3.5" />
              <span className="hidden sm:inline">{t('conversations.reply.emoji')}</span>
            </button>
            {showEmojis && (
              <div
                ref={emojiPanelRef}
                className="absolute right-0 top-full mt-1 z-20 w-72 max-w-[calc(100vw-2rem)] bg-white dark:bg-slate-700 border border-gray-200 dark:border-slate-600 rounded-lg shadow-lg overflow-hidden"
              >
                <div className="px-3 py-2 text-[11px] font-medium text-gray-500 dark:text-slate-400 bg-gray-50 dark:bg-slate-800 border-b border-gray-100 dark:border-slate-600">
                  {t('conversations.reply.commonEmojis')}
                </div>
                <div className="grid grid-cols-8 gap-0.5 p-2">
                  {WECHAT_EMOJIS.map((emoji, idx) => (
                    <button
                      key={idx}
                      type="button"
                      onClick={() => handleEmojiInsert(emoji)}
                      disabled={readOnly || submitting}
                      className="w-8 h-8 flex items-center justify-center text-lg rounded hover:bg-green-50 dark:hover:bg-green-900/30 disabled:opacity-50 disabled:cursor-not-allowed transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-green-400"
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
            accept="image/*,audio/*,video/*"
            onChange={(e) => void handleFileSelect(e.target.files)}
            className="hidden"
          />
          <span className={`text-xs ${charCountColor} whitespace-nowrap`} title={t('conversations.reply.charCountTitle')}>
            {value.length}/{maxLength}
          </span>
          <span className={`text-xs ${byteCountColor} whitespace-nowrap hidden sm:inline`} title={t('conversations.channel.wechat.byteCountTitle')}>
            {byteLength}B/600B
          </span>
          {/* 快捷键帮助 */}
          <div className="relative">
            <button
              ref={shortcutsButtonRef}
              type="button"
              onClick={() => setShowShortcuts((v) => !v)}
              disabled={readOnly || submitting}
              className="flex items-center text-xs text-gray-400 dark:text-slate-500 hover:text-green-600 dark:hover:text-green-400 disabled:opacity-40 disabled:cursor-not-allowed transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-green-400 focus-visible:ring-offset-1 rounded px-1 py-0.5"
              title={t('conversations.reply.shortcuts')}
              aria-label={t('conversations.reply.shortcutsAria')}
              aria-expanded={showShortcuts}
            >
              <Keyboard className="w-3.5 h-3.5" />
            </button>
            {showShortcuts && (
              <>
                <div
                  className="fixed inset-0 z-10"
                  onClick={() => setShowShortcuts(false)}
                  aria-hidden="true"
                />
                <div className="absolute right-0 top-full mt-1 z-20 w-64 max-w-[calc(100vw-2rem)] bg-white dark:bg-slate-700 border border-gray-200 dark:border-slate-600 rounded-lg shadow-lg overflow-hidden">
                  <div className="px-3 py-2 text-[11px] font-medium text-gray-500 dark:text-slate-400 bg-gray-50 dark:bg-slate-800 border-b border-gray-100 dark:border-slate-600">
                    {t('conversations.reply.shortcuts')}
                  </div>
                  <ul className="py-1 text-xs text-gray-700 dark:text-slate-200">
                    <li className="flex items-center justify-between px-3 py-1.5">
                      <span>{t('conversations.reply.shortcutSend')}</span>
                      <kbd className="px-1.5 py-0.5 bg-gray-100 dark:bg-slate-600 border border-gray-200 dark:border-slate-500 rounded text-[10px] font-mono">Ctrl+Enter</kbd>
                    </li>
                    <li className="flex items-center justify-between px-3 py-1.5">
                      <span>{t('conversations.reply.shortcutQuickReplies', { count: QUICK_REPLY_KEYS.length })}</span>
                      <kbd className="px-1.5 py-0.5 bg-gray-100 dark:bg-slate-600 border border-gray-200 dark:border-slate-500 rounded text-[10px] font-mono">{QUICK_REPLY_KEYS.length}</kbd>
                    </li>
                    <li className="flex items-center justify-between px-3 py-1.5">
                      <span>{t('conversations.reply.shortcutClosePanel')}</span>
                      <kbd className="px-1.5 py-0.5 bg-gray-100 dark:bg-slate-600 border border-gray-200 dark:border-slate-500 rounded text-[10px] font-mono">Esc</kbd>
                    </li>
                    <li className="flex items-center justify-between px-3 py-1.5">
                      <span>{t('conversations.reply.shortcutPasteImage')}</span>
                      <kbd className="px-1.5 py-0.5 bg-gray-100 dark:bg-slate-600 border border-gray-200 dark:border-slate-500 rounded text-[10px] font-mono">Ctrl+V</kbd>
                    </li>
                  </ul>
                </div>
              </>
            )}
          </div>
        </div>
      </div>

      <textarea
        ref={textareaRef}
        value={value}
        onChange={(e) => onChange(e.target.value.slice(0, maxLength))}
        onKeyDown={handleKeyDown}
        onPaste={handlePaste}
        disabled={readOnly || submitting || windowExpired}
        placeholder={
          windowExpired
            ? t('conversations.channel.wechat.placeholderExpired')
            : t('conversations.channel.wechat.placeholder')
        }
        rows={3}
        maxLength={maxLength}
        aria-label={t('conversations.channel.wechat.textareaAria')}
        className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-green-500 dark:focus:ring-green-400 focus:border-transparent text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 resize-none disabled:bg-gray-50 dark:disabled:bg-slate-800 transition-shadow"
      />

      {/* 附件列表 */}
      {attachments.length > 0 && (
        <div className="space-y-2">
          {/* 图片附件：网格缩略图预览 */}
          {attachments.filter((att) => att.mimeType.startsWith('image/')).length > 0 && (
            <div className="flex flex-wrap gap-2">
              {attachments.filter((att) => att.mimeType.startsWith('image/')).map((att) => (
                <div key={att.id} className="relative group w-16 h-16 sm:w-20 sm:h-20 rounded-lg overflow-hidden border border-gray-200 dark:border-slate-600">
                  <img
                    src={att.url}
                    alt={att.fileName}
                    className="w-full h-full object-cover"
                    title={`${att.fileName} (${formatFileSize(att.size)})`}
                  />
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
          {/* 非图片附件：列表样式 */}
          {attachments.filter((att) => !att.mimeType.startsWith('image/')).length > 0 && (
            <div className="space-y-1.5">
              {attachments.filter((att) => !att.mimeType.startsWith('image/')).map((att) => (
                <div
                  key={att.id}
                  className="flex items-center gap-2 px-2.5 py-1.5 bg-gray-50 dark:bg-slate-700/50 rounded-lg text-xs border border-gray-100 dark:border-slate-600/50 hover:border-gray-200 dark:hover:border-slate-600 transition-colors"
                >
                  <span className="flex-shrink-0">{getAttachmentIcon(att.mimeType)}</span>
                  <span className="flex-1 min-w-0 truncate text-gray-700 dark:text-slate-200">{att.fileName}</span>
                  <span className="text-gray-400 dark:text-slate-500 flex-shrink-0 hidden sm:inline">{formatFileSize(att.size)}</span>
                  <button
                    type="button"
                    onClick={() => removeAttachment(att.id)}
                    disabled={submitting}
                    className="flex-shrink-0 text-gray-400 hover:text-red-500 dark:hover:text-red-400 disabled:opacity-40 transition-colors"
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
          <span className="hidden sm:inline">{t('conversations.channel.wechat.footerHint')}</span>
          <span className="sm:hidden">{t('conversations.channel.wechat.footerHintShort')}</span>
        </p>
        <button
          type="button"
          onClick={handleSubmit}
          disabled={!canSubmit}
          className="inline-flex items-center gap-1.5 px-3 sm:px-4 py-2 bg-gradient-to-r from-green-600 to-emerald-600 hover:from-green-700 hover:to-emerald-700 disabled:from-gray-300 dark:disabled:from-slate-700 dark:disabled:to-slate-700 disabled:cursor-not-allowed text-white rounded-lg text-sm font-medium shadow-sm transition-all flex-shrink-0 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-green-400 focus-visible:ring-offset-2 dark:focus-visible:ring-offset-slate-800"
          aria-label={t('conversations.channel.wechat.sendAria')}
        >
          {submitting ? (
            <Loader2 className="w-4 h-4 animate-spin" />
          ) : (
            <Send className="w-4 h-4" />
          )}
          {submitting ? t('conversations.reply.sendingEllipsis') : t('conversations.reply.send')}
        </button>
      </div>
    </div>
  );
}
