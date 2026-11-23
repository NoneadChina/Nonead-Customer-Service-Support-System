import { useRef, useEffect, useState, useCallback } from 'react';
import {
  Send, Loader2, Paperclip, X, Image as ImageIcon, Music, Video, FileText,
  AlertTriangle, Info, MessageSquare,
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
  /**
   * 会话状态：'ok' | 'warning' | 'expired' | 'unknown'。
   * 注意 iMessage / Apple Messages **没有** Meta 那种 24 小时时间窗口（AMB 是会话制），
   * 这里只反映本系统里的会话空闲时长，不代表"能不能回"。
   */
  windowStatus?: 'ok' | 'warning' | 'expired' | 'unknown';
  /** 传输模式：bridge | amb_msp | amb_apple_direct（决定文案与限制） */
  mode?: string;
}

// 文本上限：桥接（BlueBubbles/Messages.app）实测可到 8000+，AMB 未公布硬上限。
// 这里取与后端 IMessageReplyRequest [MaxLength(4000)] 一致的保守值。
const IMESSAGE_TEXT_MAX_LENGTH = 4000;

// 附件上限：Apple MMCS 单附件 100MB；桥接按 Messages.app 限制（同样按 100MB 收）
const IMESSAGE_MAX_FILE_SIZE = 100 * 1024 * 1024;
const IMESSAGE_ALLOWED_MIME_PREFIXES = ['image/', 'audio/', 'video/'];
const IMESSAGE_DOCUMENT_EXTENSIONS = [
  '.txt', '.pdf', '.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx', '.zip', '.heic',
];
const MAX_ATTACHMENT_COUNT = 20;

function isAllowedFile(file: File): boolean {
  if (MESSAGE_TYPE_PREFIXES.some((prefix) => file.type.startsWith(prefix))) return true;
  const name = file.name.toLowerCase();
  return IMESSAGE_DOCUMENT_EXTENSIONS.some((ext) => name.endsWith(ext));
}
const MESSAGE_TYPE_PREFIXES = IMESSAGE_ALLOWED_MIME_PREFIXES;

function fileIcon(mime: string) {
  if (mime.startsWith('image/')) return <ImageIcon className="h-4 w-4" />;
  if (mime.startsWith('audio/')) return <Music className="h-4 w-4" />;
  if (mime.startsWith('video/')) return <Video className="h-4 w-4" />;
  return <FileText className="h-4 w-4" />;
}

const QUICK_REPLY_KEYS: TranslationKey[] = [
  'conversations.reply.quickReply1',
  'conversations.reply.quickReply2',
  'conversations.reply.quickReply3',
  'conversations.reply.quickReply4',
  'conversations.reply.quickReply5',
  'conversations.reply.quickReply6',
];

/**
 * iMessage / Apple Messages 回复框（镜像 WhatsAppReplyBox 的契约，
 * 便于 ConversationDetail 用同一组 props 接线）。
 */
export default function IMessageReplyBox({
  value,
  onChange,
  onSubmit,
  submitting,
  readOnly,
  windowStatus = 'unknown',
  mode = 'bridge',
}: Props) {
  const { t } = useI18n();
  const toast = useToast();
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [pendingFiles, setPendingFiles] = useState<File[]>([]);
  const [uploadedAttachments, setUploadedAttachments] = useState<AttachmentResponse[]>([]);
  const [uploading, setUploading] = useState(false);
  const [dragActive, setDragActive] = useState(false);
  const [showQuickReplies, setShowQuickReplies] = useState(false);

  const isAmb = mode.startsWith('amb');

  useEffect(() => {
    const el = textareaRef.current;
    if (!el) return;
    el.style.height = 'auto';
    el.style.height = `${Math.min(el.scrollHeight, 200)}px`;
  }, [value]);

  const addFiles = useCallback((files: File[]) => {
    const accepted: File[] = [];
    for (const file of files) {
      if (!isAllowedFile(file)) {
        toast.error(t('conversations.channel.iMessage.unsupportedType'));
        continue;
      }
      if (file.size > IMESSAGE_MAX_FILE_SIZE) {
        toast.error(`${file.name} > 100MB`);
        continue;
      }
      accepted.push(file);
    }
    if (accepted.length === 0) return;
    setPendingFiles((prev) => {
      const merged = [...prev, ...accepted];
      if (merged.length > MAX_ATTACHMENT_COUNT) {
        toast.error(t('conversations.channel.iMessage.tooManyAttachments'));
        return merged.slice(0, MAX_ATTACHMENT_COUNT);
      }
      return merged;
    });
  }, [toast, t]);

  const handleFiles = (event: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(event.target.files ?? []);
    if (files.length > 0) addFiles(files);
    event.target.value = '';
  };

  const handleDrop = (event: React.DragEvent) => {
    event.preventDefault();
    setDragActive(false);
    const files = Array.from(event.dataTransfer.files ?? []);
    if (files.length > 0) addFiles(files);
  };

  const handlePaste = (event: React.ClipboardEvent) => {
    const files = Array.from(event.clipboardData.files ?? []);
    if (files.length > 0) {
      event.preventDefault();
      addFiles(files);
    }
  };

  const removePending = (index: number) => {
    setPendingFiles((prev) => prev.filter((_, i) => i !== index));
  };

  const removeUploaded = async (id: number) => {
    setUploadedAttachments((prev) => prev.filter((a) => a.id !== id));
    try {
      await attachmentApi.delete(id);
    } catch {
      // 后端清理失败不影响前端移除；下次进入会话仍在
    }
  };

  const handleSubmit = async () => {
    if (readOnly) return;
    const hasContent = value.trim().length > 0 || pendingFiles.length > 0 || uploadedAttachments.length > 0;
    if (!hasContent || submitting || uploading) return;

    let attachmentIds: number[] | undefined;
    if (pendingFiles.length > 0) {
      setUploading(true);
      try {
        const result = await attachmentApi.uploadBatch(pendingFiles);
        const uploaded = result?.data ?? [];
        setUploadedAttachments((prev) => [...prev, ...uploaded]);
        attachmentIds = [...uploadedAttachments.map((a) => a.id), ...uploaded.map((a) => a.id)];
        setPendingFiles([]);
      } catch {
        toast.error(t('conversations.channel.iMessage.uploadFailed'));
        setUploading(false);
        return;
      }
      setUploading(false);
    } else if (uploadedAttachments.length > 0) {
      attachmentIds = uploadedAttachments.map((a) => a.id);
    }

    await onSubmit(attachmentIds);
    setUploadedAttachments([]);
  };

  const handleKeyDown = (event: React.KeyboardEvent) => {
    if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
      event.preventDefault();
      void handleSubmit();
    }
  };

  const remaining = IMESSAGE_TEXT_MAX_LENGTH - value.length;

  return (
    <div className="rounded-lg border border-slate-200 bg-white p-4 dark:border-slate-700 dark:bg-slate-900">
      <div className="mb-3 flex items-center gap-2">
        <SourceTypeIcon sourceType={14} className="h-4 w-4" />
        <span className="text-sm font-medium text-slate-700 dark:text-slate-200">
          {t('conversations.channel.iMessage.title')}
        </span>
        <span className="text-xs text-slate-500 dark:text-slate-400">
          {t('conversations.channel.iMessage.subtitle')}
        </span>
      </div>

      {isAmb && (
        <div className="mb-3 flex items-start gap-2 rounded-md border border-blue-200 bg-blue-50 p-2 text-xs text-blue-800 dark:border-blue-800 dark:bg-blue-950 dark:text-blue-200">
          <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
          <span>{t('conversations.channel.iMessage.ambSessionNotice')}</span>
        </div>
      )}

      {windowStatus === 'expired' && (
        <div className="mb-3 flex items-start gap-2 rounded-md border border-amber-200 bg-amber-50 p-2 text-xs text-amber-800 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200">
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
          <span>{t('conversations.channel.iMessage.sessionIdle')}</span>
        </div>
      )}

      <textarea
        ref={textareaRef}
        value={value}
        onChange={(e) => {
          if (e.target.value.length <= IMESSAGE_TEXT_MAX_LENGTH) onChange(e.target.value);
        }}
        onKeyDown={handleKeyDown}
        onPaste={handlePaste}
        disabled={readOnly}
        rows={3}
        placeholder={t('conversations.channel.iMessage.placeholder')}
        aria-label={t('conversations.channel.iMessage.textareaAria')}
        className="w-full resize-none rounded-md border border-slate-300 bg-white p-3 text-sm text-slate-900 placeholder:text-slate-400 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 disabled:bg-slate-50 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100 dark:disabled:bg-slate-900"
      />

      {(pendingFiles.length > 0 || uploadedAttachments.length > 0) && (
        <div className="mt-2 flex flex-wrap gap-2">
          {pendingFiles.map((file, index) => (
            <span
              key={`pending-${file.name}-${index}`}
              className="flex items-center gap-1 rounded bg-slate-100 px-2 py-1 text-xs text-slate-600 dark:bg-slate-800 dark:text-slate-300"
            >
              {fileIcon(file.type)}
              <span className="max-w-[12rem] truncate">{file.name}</span>
              <button
                type="button"
                onClick={() => removePending(index)}
                aria-label={t('conversations.channel.iMessage.removeAttachment')}
                className="text-slate-400 hover:text-slate-600"
              >
                <X className="h-3 w-3" />
              </button>
            </span>
          ))}
          {uploadedAttachments.map((attachment) => (
            <span
              key={attachment.id}
              className="flex items-center gap-1 rounded bg-emerald-50 px-2 py-1 text-xs text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300"
            >
              {fileIcon(attachment.mimeType ?? '')}
              <span className="max-w-[12rem] truncate">{attachment.fileName}</span>
              <button
                type="button"
                onClick={() => void removeUploaded(attachment.id)}
                aria-label={t('conversations.channel.iMessage.removeAttachment')}
                className="text-emerald-400 hover:text-emerald-600"
              >
                <X className="h-3 w-3" />
              </button>
            </span>
          ))}
        </div>
      )}

      <div
        className="mt-3 flex flex-wrap items-center gap-2"
        onDragOver={(e) => { e.preventDefault(); setDragActive(true); }}
        onDragLeave={() => setDragActive(false)}
        onDrop={handleDrop}
      >
        <input
          ref={fileInputRef}
          type="file"
          multiple
          accept={MESSAGE_TYPE_PREFIXES.map((p) => `${p}*`).join(',') + ',' + IMESSAGE_DOCUMENT_EXTENSIONS.join(',')}
          onChange={handleFiles}
          className="hidden"
        />
        <button
          type="button"
          onClick={() => fileInputRef.current?.click()}
          disabled={readOnly}
          aria-label={t('conversations.channel.iMessage.uploadAria')}
          title={t('conversations.channel.iMessage.uploadTitle')}
          className="rounded-md p-2 text-slate-500 hover:bg-slate-100 hover:text-slate-700 disabled:opacity-50 dark:hover:bg-slate-800"
        >
          <Paperclip className="h-4 w-4" />
        </button>
        <button
          type="button"
          onClick={() => setShowQuickReplies((v) => !v)}
          disabled={readOnly}
          title={t('conversations.channel.iMessage.quickRepliesTitle')}
          className="rounded-md p-2 text-slate-500 hover:bg-slate-100 hover:text-slate-700 disabled:opacity-50 dark:hover:bg-slate-800"
        >
          <MessageSquare className="h-4 w-4" />
        </button>

        <span className="ml-auto text-xs text-slate-400" title={t('channels.common.charCountTitle')}>
          {value.length}/{IMESSAGE_TEXT_MAX_LENGTH}{remaining < 200 ? ` (${remaining})` : ''}
        </span>

        <button
          type="button"
          onClick={() => void handleSubmit()}
          disabled={readOnly || submitting || uploading || (!value.trim() && pendingFiles.length === 0 && uploadedAttachments.length === 0)}
          className="flex items-center gap-1.5 rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {submitting || uploading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
          {t('conversations.channel.iMessage.sendNow')}
        </button>
      </div>

      {showQuickReplies && (
        <div className="mt-2 flex flex-wrap gap-1.5">
          {QUICK_REPLY_KEYS.map((key) => (
            <button
              key={key}
              type="button"
              onClick={() => { onChange(t(key)); setShowQuickReplies(false); }}
              className="rounded border border-slate-200 px-2 py-1 text-xs text-slate-600 hover:bg-slate-50 dark:border-slate-700 dark:text-slate-300 dark:hover:bg-slate-800"
            >
              {t(key)}
            </button>
          ))}
        </div>
      )}

      <p className="mt-2 text-xs text-slate-400">
        {dragActive ? t('conversations.channel.iMessage.dropHint') : t('conversations.channel.iMessage.footerHint')}
      </p>
    </div>
  );
}
