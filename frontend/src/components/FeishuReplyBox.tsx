import { useRef, useEffect, useState, useCallback } from 'react';
import { Send, Loader2, Paperclip, X, Image, Music, Video, Zap, Smile, Keyboard } from 'lucide-react';
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
}

// 飞书消息支持的媒体类型
const FEISHU_ALLOWED_MIME_PREFIXES = ['image/', 'audio/', 'video/'];

/** 飞书允许的文件扩展名白名单（用于 file.type 为空时的回退校验）*/
const FEISHU_ALLOWED_EXT = [
  'jpg', 'jpeg', 'png', 'gif', 'bmp', 'webp', 'svg',
  'mp3', 'wav', 'm4a', 'aac', 'ogg',
  'mp4', 'mov', 'avi', 'mkv', 'webm', '3gp',
];
// 飞书文件大小限制（与系统附件上传一致，25MB）
const FEISHU_MAX_FILE_SIZE = 25 * 1024 * 1024;

// 飞书文本消息内容上限（保守限制 4000 字符）
const FEISHU_TEXT_MAX_LENGTH = 4000;

// 常用 Emoji 短列表（客服场景精选表情）
const FEISHU_EMOJIS: string[] = [
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
 * 飞书会话专用回复框。
 * 支持文本回复 + 图片/语音/视频附件。
 * 飞书机器人可在用户与机器人建立会话后自由发送消息。
 */
export default function FeishuReplyBox({
  value,
  onChange,
  onSubmit,
  submitting = false,
  readOnly = false,
  maxLength = FEISHU_TEXT_MAX_LENGTH,
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

  // 浮层按钮 ref，用来关闭浮层后把焦点还给触发按钮
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

  // 浮层统一 Esc 关闭 + 焦点恢复
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
      const mimeValid = !!file.type && FEISHU_ALLOWED_MIME_PREFIXES.some((p) => file.type.startsWith(p));
      const extName = file.name.split('.').pop()?.toLowerCase() ?? '';
      const extValid = !file.type && FEISHU_ALLOWED_EXT.includes(extName);
      if (!mimeValid && !extValid) {
        errors.push(t('conversations.channel.feishu.unsupportedType', { name: file.name }));
        continue;
      }
      if (file.size > FEISHU_MAX_FILE_SIZE) {
        errors.push(t('conversations.channel.feishu.fileTooLarge', { name: file.name }));
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

  // 拖拽上传支持
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

  const canSubmit = !submitting && !uploading && !readOnly && (value.trim().length > 0 || attachments.length > 0);

  const charCountColor = value.length > maxLength * 0.9 ? 'text-red-500' : 'text-gray-400 dark:text-slate-500';

  const handleQuickReply = (text: string) => {
    const sep = value && !value.endsWith('\n') ? '\n' : '';
    const next = (value + sep + text).slice(0, maxLength);
    onChange(next);
    setShowQuickReplies(false);
    requestAnimationFrame(() => textareaRef.current?.focus());
  };

  const handleEmojiInsert = (emoji: string) => {
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
          ? 'border-blue-500 dark:border-blue-400 bg-blue-50/50 dark:bg-blue-900/10'
          : 'border-blue-200 dark:border-blue-900/50'
      }`}
    >
      {/* 拖拽提示遮罩 */}
      {isDragOver && (
        <div className="absolute inset-0 z-10 flex items-center justify-center pointer-events-none bg-blue-50/80 dark:bg-blue-900/20 rounded-xl">
          <div className="flex flex-col items-center gap-2 text-blue-600 dark:text-blue-400">
            <Paperclip className="w-8 h-8" />
            <span className="text-sm font-medium">{t('conversations.reply.dropToUpload')}</span>
          </div>
        </div>
      )}
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-sm font-medium text-blue-700 dark:text-blue-400 min-w-0">
          <div className="p-1 bg-blue-100 dark:bg-blue-900/40 rounded flex-shrink-0">
            <SourceTypeIcon sourceType={9} className="w-3.5 h-3.5 text-blue-600 dark:text-blue-400" title={t('conversations.channel.feishu.title')} />
          </div>
          <span className="truncate">{t('conversations.channel.feishu.title')}</span>
        </div>
        <div className="flex items-center gap-2 sm:gap-3 flex-shrink-0">
          {/* 快捷回复按钮 */}
          <div className="relative">
            <button
              ref={quickReplyButtonRef}
              type="button"
              onClick={() => setShowQuickReplies((v) => !v)}
              disabled={readOnly || submitting}
              className="flex items-center gap-1 text-xs text-gray-500 dark:text-slate-400 hover:text-blue-600 dark:hover:text-blue-400 disabled:opacity-40 disabled:cursor-not-allowed transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400 focus-visible:ring-offset-1 rounded px-1 py-0.5"
              title={t('conversations.reply.quickReplies')}
              aria-label={t('conversations.reply.quickReplies')}
              aria-expanded={showQuickReplies}
            >
              <Zap className="w-3.5 h-3.5" />
              <span className="hidden sm:inline">{t('conversations.reply.quick')}</span>
            </button>
            {showQuickReplies && (
              <>
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
                          className="w-full text-left px-3 py-2 text-xs text-gray-700 dark:text-slate-200 hover:bg-blue-50 dark:hover:bg-blue-900/30 hover:text-blue-700 dark:hover:text-blue-400 disabled:opacity-50 transition-colors flex items-center gap-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-blue-400"
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
            className="flex items-center gap-1 text-xs text-gray-500 dark:text-slate-400 hover:text-blue-600 dark:hover:text-blue-400 disabled:opacity-40 disabled:cursor-not-allowed transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400 focus-visible:ring-offset-1 rounded px-1 py-0.5"
            title={t('conversations.channel.feishu.uploadTitle')}
            aria-label={t('conversations.channel.feishu.uploadAria')}
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
              className={`flex items-center gap-1 text-xs transition-colors disabled:opacity-40 disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400 focus-visible:ring-offset-1 rounded px-1 py-0.5 ${
                showEmojis
                  ? 'text-blue-600 dark:text-blue-400'
                  : 'text-gray-500 dark:text-slate-400 hover:text-blue-600 dark:hover:text-blue-400'
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
                  {FEISHU_EMOJIS.map((emoji, idx) => (
                    <button
                      key={idx}
                      type="button"
                      onClick={() => handleEmojiInsert(emoji)}
                      disabled={readOnly || submitting}
                      className="w-8 h-8 flex items-center justify-center text-lg rounded hover:bg-blue-50 dark:hover:bg-blue-900/30 disabled:opacity-50 disabled:cursor-not-allowed transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
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
          {/* 快捷键帮助 */}
          <div className="relative">
            <button
              ref={shortcutsButtonRef}
              type="button"
              onClick={() => setShowShortcuts((v) => !v)}
              disabled={readOnly || submitting}
              className="flex items-center text-xs text-gray-400 dark:text-slate-500 hover:text-blue-600 dark:hover:text-blue-400 disabled:opacity-40 disabled:cursor-not-allowed transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400 focus-visible:ring-offset-1 rounded px-1 py-0.5"
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
        disabled={readOnly || submitting}
        placeholder={t('conversations.channel.feishu.placeholder')}
        rows={3}
        maxLength={maxLength}
        aria-label={t('conversations.channel.feishu.textareaAria')}
        className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 dark:focus:ring-blue-400 focus:border-transparent text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 resize-none disabled:bg-gray-50 dark:disabled:bg-slate-800 transition-shadow"
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
                    loading="lazy"
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
          <span className="hidden sm:inline">{t('conversations.channel.feishu.footerHint')}</span>
          <span className="sm:hidden">{t('conversations.channel.textAndAttachments')}</span>
        </p>
        <button
          type="button"
          onClick={handleSubmit}
          disabled={!canSubmit}
          className="inline-flex items-center gap-1.5 px-3 sm:px-4 py-2 bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-700 hover:to-indigo-700 disabled:from-gray-300 dark:disabled:from-slate-700 dark:disabled:to-slate-700 disabled:cursor-not-allowed text-white rounded-lg text-sm font-medium shadow-sm transition-all flex-shrink-0 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400 focus-visible:ring-offset-2 dark:focus-visible:ring-offset-slate-800"
          aria-label={t('conversations.channel.feishu.sendAria')}
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
