import { useState, useRef, useCallback, useEffect, useMemo } from 'react';
import { createPortal } from 'react-dom';
import { attachmentApi, cannedResponseApi } from '../api';
import type { AttachmentResponse, CannedResponseDto } from '../api/types';
import { formatFileSize, sanitizeHtml, sanitizeFileName } from '../utils/format';
import { useToast } from '../context/ToastContext';
import { useI18n } from '../context/I18nContext';

interface Props {
  mode: 'reply' | 'note';
  defaultFrom?: string;
  mailboxId?: number;
  onSubmit: (body: string, attachmentIds?: number[], cc?: string, bcc?: string) => Promise<void>;
  onSaveDraft?: (body: string, attachmentIds?: number[], cc?: string, bcc?: string) => Promise<void>;
  submitting?: boolean;
  initialBody?: string;
  initialCc?: string;
  initialBcc?: string;
  initialAttachments?: AttachmentResponse[];
  onDraftSaved?: () => void;
  readOnly?: boolean;
}

interface ToolbarBtn {
  title: string;
  icon: React.ReactNode;
  action: (wrapper: HTMLTextAreaElement) => void;
}

const UNDO_SECONDS = 15;

export default function ReplyBox({ mode, defaultFrom = '', onSubmit, onSaveDraft, submitting, initialBody, initialCc, initialBcc, initialAttachments, onDraftSaved, readOnly = false }: Props) {
  const toast = useToast();
  const { t } = useI18n();
  const [body, setBody] = useState(initialBody ?? '');
  const [attachments, setAttachments] = useState<AttachmentResponse[]>(initialAttachments ?? []);
  const [uploading, setUploading] = useState(false);
  const [savingDraft, setSavingDraft] = useState(false);
  const [fromEmail] = useState(defaultFrom);
  const [cc, setCc] = useState(initialCc ?? '');
  const [bcc, setBcc] = useState(initialBcc ?? '');
  const [canUndo, setCanUndo] = useState(false);
  const [undoCountdown, setUndoCountdown] = useState(UNDO_SECONDS);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const templateBtnRef = useRef<HTMLButtonElement>(null);
  const templatePanelRef = useRef<HTMLDivElement>(null);
  const undoTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const isNote = mode === 'note';

  // 草稿加载完成后回填到 state
  useEffect(() => {
    if (initialBody !== undefined) setBody(initialBody);
  }, [initialBody]);
  useEffect(() => {
    if (initialCc !== undefined) setCc(initialCc);
  }, [initialCc]);
  useEffect(() => {
    if (initialBcc !== undefined) setBcc(initialBcc);
  }, [initialBcc]);
  useEffect(() => {
    if (initialAttachments !== undefined) setAttachments(initialAttachments);
  }, [initialAttachments]);

  // ====== 快捷回复模板 ======
  const [templateOpen, setTemplateOpen] = useState(false);
  const [templateSearch, setTemplateSearch] = useState('');
  const [templates, setTemplates] = useState<CannedResponseDto[]>([]);
  const [templatesLoading, setTemplatesLoading] = useState(false);
  const [templatePos, setTemplatePos] = useState<{ top: number; left: number; width: number; flipUp: boolean } | null>(null);

  // 计算快捷回复模板下拉在 viewport 中的位置（用于 Portal 渲染到 body，避免被父容器 overflow 裁剪）
  const updateTemplatePos = useCallback(() => {
    if (!templateOpen) return;
    const btn = templateBtnRef.current;
    if (!btn) return;
    const r = btn.getBoundingClientRect();
    const panelW = 288; // w-72
    const panelMaxH = 256 + 45; // max-h-64 + 搜索框约 45px，留一点裕量
    const vh = window.innerHeight || document.documentElement.clientHeight;
    const below = r.bottom + 8 + panelMaxH;
    const flipUp = below > vh && r.top - 8 - panelMaxH > 0;
    const top = flipUp ? Math.max(4, r.top - 8 - panelMaxH) : r.bottom + 8;
    let left = r.left;
    const vw = window.innerWidth || document.documentElement.clientWidth;
    if (left + panelW > vw - 8) left = Math.max(4, vw - panelW - 8);
    setTemplatePos({ top, left, width: panelW, flipUp });
  }, [templateOpen]);

  useEffect(() => {
    if (!templateOpen) {
      setTemplatePos(null);
      return;
    }
    updateTemplatePos();
    const handlers = ['scroll', 'resize'].map((e) => {
      window.addEventListener(e, updateTemplatePos, true);
      return () => window.removeEventListener(e, updateTemplatePos, true);
    });
    return () => handlers.forEach((fn) => fn());
  }, [templateOpen, updateTemplatePos]);

  const loadTemplates = useCallback(async () => {
    setTemplatesLoading(true);
    try {
      const list = templateSearch.trim()
        ? await cannedResponseApi.search(templateSearch)
        : await cannedResponseApi.list();
      setTemplates(list);
    } catch (err: unknown) {
      console.error(err);
    } finally {
      setTemplatesLoading(false);
    }
  }, [templateSearch]);

  useEffect(() => {
    if (templateOpen) {
      loadTemplates();
    }
  }, [templateOpen, loadTemplates]);

  // 点击外部关闭模板面板
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (
        templatePanelRef.current &&
        !templatePanelRef.current.contains(e.target as Node) &&
        templateBtnRef.current &&
        !templateBtnRef.current.contains(e.target as Node)
      ) {
        setTemplateOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  // 撤销倒计时
  useEffect(() => {
    if (!canUndo || undoCountdown <= 0) return;
    const timer = setInterval(() => {
      setUndoCountdown((prev) => {
        if (prev <= 1) {
          clearInterval(timer);
          return 0;
        }
        return prev - 1;
      });
    }, 1000);
    return () => clearInterval(timer);
  }, [canUndo, undoCountdown]);

  // 清理定时器
  useEffect(() => {
    return () => {
      if (undoTimerRef.current) {
        clearTimeout(undoTimerRef.current);
      }
    };
  }, []);

  const insertTemplate = (template: CannedResponseDto) => {
    const ta = textareaRef.current;
    if (!ta) return;
    const start = ta.selectionStart;
    const end = ta.selectionEnd;
    const newText = body.substring(0, start) + template.body + body.substring(end);
    setBody(newText);
    setTemplateOpen(false);
    setTemplateSearch('');
    requestAnimationFrame(() => {
      ta.focus();
      const pos = start + template.body.length;
      ta.setSelectionRange(pos, pos);
    });
  };

  // 按分组组织模板
  const groupedTemplates = useMemo(() => {
    const groups: Record<string, CannedResponseDto[]> = {};
    templates.forEach((tpl) => {
      const key = tpl.category || t('conversations.reply.uncategorized');
      if (!groups[key]) groups[key] = [];
      groups[key].push(tpl);
    });
    return groups;
  }, [templates, t]);
  // ====== 富文本工具栏操作 ======
  const wrapSelection = useCallback((before: string, after: string, placeholder: string) => {
    const ta = textareaRef.current;
    if (!ta) return;
    const start = ta.selectionStart;
    const end = ta.selectionEnd;
    const selected = body.substring(start, end) || placeholder;
    const newText = body.substring(0, start) + before + selected + after + body.substring(end);
    setBody(newText);
    // 恢复光标位置
    requestAnimationFrame(() => {
      ta.focus();
      const newStart = start + before.length;
      const newEnd = newStart + selected.length;
      ta.setSelectionRange(newStart, newEnd);
    });
  }, [body]);

  const insertLinePrefix = useCallback((prefix: string) => {
    const ta = textareaRef.current;
    if (!ta) return;
    const start = ta.selectionStart;
    const end = ta.selectionEnd;
    // 找到当前行起始位置
    const lineStart = body.lastIndexOf('\n', start - 1) + 1;
    const lineEnd = body.indexOf('\n', end);
    const actualLineEnd = lineEnd === -1 ? body.length : lineEnd;
    const selectedLines = body.substring(lineStart, actualLineEnd);
    const prefixed = selectedLines.split('\n').map((line) => prefix + line).join('\n');
    const newText = body.substring(0, lineStart) + prefixed + body.substring(actualLineEnd);
    setBody(newText);
    requestAnimationFrame(() => {
      ta.focus();
      ta.setSelectionRange(lineStart, lineStart + prefixed.length);
    });
  }, [body]);

  const insertLink = useCallback(() => {
    const ta = textareaRef.current;
    if (!ta) return;
    const start = ta.selectionStart;
    const end = ta.selectionEnd;
    const selected = body.substring(start, end) || t('conversations.reply.linkPlaceholder');
    const newText = body.substring(0, start) + `<a href="https://">${selected}</a>` + body.substring(end);
    setBody(newText);
    requestAnimationFrame(() => {
      ta.focus();
      // 选中 href 中的 URL 便于编辑
      const hrefStart = start + 9;
      ta.setSelectionRange(hrefStart, hrefStart + 8);
    });
  }, [body, t]);

  const toolbarButtons: ToolbarBtn[] = [
    {
      title: t('conversations.reply.bold'),
      icon: (
        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M6 4h8a4 4 0 014 4 4 4 0 01-4 4H6V4zm0 8h9a4 4 0 014 4 4 4 0 01-4 4H6v-8z" />
        </svg>
      ),
      action: () => wrapSelection('<strong>', '</strong>', t('conversations.reply.boldPlaceholder')),
    },
    {
      title: t('conversations.reply.italic'),
      icon: (
        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M19 4h-6M14 20H8M15 4L9 20" />
        </svg>
      ),
      action: () => wrapSelection('<em>', '</em>', t('conversations.reply.italicPlaceholder')),
    },
    {
      title: t('conversations.reply.underline'),
      icon: (
        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M8 4v6a4 4 0 008 0V4M6 18h12" />
        </svg>
      ),
      action: () => wrapSelection('<u>', '</u>', t('conversations.reply.underlinePlaceholder')),
    },
    {
      title: t('conversations.reply.bulletList'),
      icon: (
        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M4 6h.01M4 12h.01M4 18h.01M8 6h13M8 12h13M8 18h13" />
        </svg>
      ),
      action: () => insertLinePrefix('• '),
    },
    {
      title: t('conversations.reply.numberList'),
      icon: (
        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M7 6h13M7 12h13M7 18h13M3 6h.01M3 12h.01M3 18h.01" />
        </svg>
      ),
      action: () => insertLinePrefix('1. '),
    },
    {
      title: t('conversations.reply.link'),
      icon: (
        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101m-.758-4.899a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.1 1.1" />
        </svg>
      ),
      action: insertLink,
    },
  ];

  const handleSubmit = async () => {
    if (!body.trim() && attachments.length === 0) return;
    const attIds = attachments.map((a) => a.id);
    
    if (isNote) {
      await onSubmit(body, attIds.length > 0 ? attIds : undefined, cc || undefined, bcc || undefined);
      setBody('');
      setAttachments([]);
      setCc('');
      setBcc('');
      return;
    }

    setCanUndo(true);
    setUndoCountdown(UNDO_SECONDS);

    if (undoTimerRef.current) {
      clearTimeout(undoTimerRef.current);
    }

    undoTimerRef.current = setTimeout(async () => {
      await onSubmit(body, attIds.length > 0 ? attIds : undefined, cc || undefined, bcc || undefined);
      setBody('');
      setAttachments([]);
      setCc('');
      setBcc('');
      setCanUndo(false);
      setUndoCountdown(UNDO_SECONDS);
    }, UNDO_SECONDS * 1000);
  };

  const handleUndo = () => {
    if (undoTimerRef.current) {
      clearTimeout(undoTimerRef.current);
      undoTimerRef.current = null;
    }
    setCanUndo(false);
    setUndoCountdown(UNDO_SECONDS);
  };

  const handleSaveDraft = async () => {
    if (!onSaveDraft || !body.trim()) return;
    setSavingDraft(true);
    try {
      const attIds = attachments.map((a) => a.id);
      await onSaveDraft(body, attIds.length > 0 ? attIds : undefined, cc || undefined, bcc || undefined);
      if (onDraftSaved) {
        onDraftSaved();
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('conversations.reply.draftSaveFailed'));
    } finally {
      setSavingDraft(false);
    }
  };

  const handleFileSelect = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    setUploading(true);
    try {
      const MAX_FILE_SIZE = 25 * 1024 * 1024; // 25MB
      const ALLOWED_MIME_PREFIXES = ['image/', 'application/pdf', 'text/plain', 'application/msword', 'application/vnd.openxmlformats-officedocument', 'application/zip', 'application/x-rar-compressed', 'application/x-7z-compressed'];

      const fileArr = Array.from(files);
      // 客户端校验：大小、类型、文件名
      const validFiles: File[] = [];
      const errors: string[] = [];
      for (const file of fileArr) {
        if (file.size > MAX_FILE_SIZE) {
          errors.push(t('conversations.reply.fileTooLarge25', { name: file.name }));
          continue;
        }
        if (file.type && !ALLOWED_MIME_PREFIXES.some(prefix => file.type.startsWith(prefix))) {
          errors.push(t('conversations.reply.fileTypeNotAllowed', { name: file.name }));
          continue;
        }
        // 净化文件名中的非法字符
        if (/[\\/:*?"<>|]/.test(file.name)) {
          const safeName = sanitizeFileName(file.name);
          // File.name 是只读的，需要通过新 File 实例替换
          const renamed = new File([file], safeName, { type: file.type, lastModified: file.lastModified });
          validFiles.push(renamed);
        } else {
          validFiles.push(file);
        }
      }
      if (errors.length > 0) {
        toast.error(errors.join('; '));
      }
      if (validFiles.length === 0) {
        return;
      }
      const result = await attachmentApi.uploadBatch(validFiles);
      // uploadBatch 返回 { data: AttachmentResponse[], count: number }，取 data 字段
      const newAtts = result?.data ?? [];
      setAttachments((prev) => [...prev, ...newAtts]);
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : t('conversations.reply.uploadFailed'));
    } finally {
      setUploading(false);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  };

  const removeAttachment = (id: number) => {
    setAttachments((prev) => prev.filter((a) => a.id !== id));
    attachmentApi.delete(id).catch((err: unknown) => console.error('删除附件失败:', err));
  };

  // Ctrl+Enter 提交
  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
      e.preventDefault();
      if (!submitting && (body.trim() || attachments.length > 0)) {
        handleSubmit();
      }
    }
  };

  if (readOnly) {
    return (
      <div className={`rounded-xl border overflow-hidden ${
        isNote ? 'border-amber-300 bg-amber-50/30 dark:border-amber-800/50 dark:bg-amber-900/20' : 'border-gray-200 bg-white dark:border-slate-600 dark:bg-slate-800'
      }`}>
        <div className={`px-4 py-2.5 flex items-center gap-2 text-sm font-medium ${
          isNote ? 'bg-amber-100/60 text-amber-800 border-b border-amber-200 dark:bg-amber-900/30 dark:text-amber-200 dark:border-amber-800/50'
                  : 'bg-gray-50 text-gray-700 border-b border-gray-200 dark:bg-slate-700/50 dark:text-slate-200 dark:border-slate-600'
        }`}>
          {isNote ? (
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
            </svg>
          ) : (
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
            </svg>
          )}
          {isNote ? t('conversations.reply.noteTitle') : t('conversations.reply.replyTitle')}
        </div>
        <div className="px-4 py-6 text-center">
          <svg className="w-10 h-10 mx-auto mb-2 text-gray-400 dark:text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
          </svg>
          <p className="text-sm text-gray-500 dark:text-slate-400">{t('conversations.reply.readOnlyHint')}</p>
        </div>
      </div>
    );
  }

  return (
    <>
      <div className={`rounded-xl border overflow-hidden ${
        isNote ? 'border-amber-300 bg-amber-50/30 dark:border-amber-800/50 dark:bg-amber-900/20' : 'border-gray-200 bg-white dark:border-slate-600 dark:bg-slate-800'
      }`}>
      {/* 头部标签 */}
      <div className={`px-4 py-2.5 flex items-center justify-between text-sm font-medium ${
        isNote ? 'bg-amber-100/60 text-amber-800 border-b border-amber-200 dark:bg-amber-900/30 dark:text-amber-200 dark:border-amber-800/50'
                : 'bg-gray-50 text-gray-700 border-b border-gray-200 dark:bg-slate-700/50 dark:text-slate-200 dark:border-slate-600'
      }`}>
        <div className="flex items-center gap-2">
          {isNote ? (
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
            </svg>
          ) : (
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
            </svg>
          )}
          {isNote ? t('conversations.reply.addNote') : t('conversations.reply.replyTitle')}
        </div>
        <div className="flex items-center gap-3 text-xs text-gray-500 font-normal">
          {!isNote && fromEmail && <span>{t('conversations.reply.from', { email: fromEmail })}</span>}
          <span className="hidden sm:inline text-gray-400">
            <kbd className="px-1 py-0.5 bg-white dark:bg-slate-700 border border-gray-200 dark:border-slate-600 rounded text-[10px] dark:text-slate-300">Ctrl</kbd>
            +
            <kbd className="px-1 py-0.5 bg-white dark:bg-slate-700 border border-gray-200 dark:border-slate-600 rounded text-[10px] dark:text-slate-300">↵</kbd>
            {t('conversations.reply.quickSend')}
          </span>
        </div>
      </div>

      {/* 富文本工具栏 */}
      <div className="flex items-center gap-0.5 px-2 py-1.5 border-b border-gray-100 dark:border-slate-600 bg-gray-50/50 dark:bg-slate-700/50">
        {toolbarButtons.map((btn, i) => (
          <button
            key={i}
            type="button"
            onClick={() => btn.action(textareaRef.current!)}
            title={btn.title}
            aria-label={btn.title}
            className="p-1.5 text-gray-600 hover:text-primary-600 hover:bg-white dark:text-slate-400 dark:hover:text-primary-400 dark:hover:bg-slate-600 rounded-md transition"
          >
            {btn.icon}
          </button>
        ))}
        <div className="w-px h-5 bg-gray-200 dark:bg-slate-600 mx-1" />
        <button
          type="button"
          onClick={() => wrapSelection('<h3>', '</h3>', t('conversations.reply.headingPlaceholder'))}
          title={t('conversations.reply.heading')}
          aria-label={t('conversations.reply.heading')}
          className="px-2 py-1 text-xs text-gray-600 hover:text-primary-600 hover:bg-white dark:text-slate-400 dark:hover:text-primary-400 dark:hover:bg-slate-600 rounded-md transition font-semibold"
        >
          H
        </button>
        <button
          type="button"
          onClick={() => wrapSelection('<blockquote>', '</blockquote>', t('conversations.reply.quotePlaceholder'))}
          title={t('conversations.reply.quote')}
          aria-label={t('conversations.reply.quote')}
          className="p-1.5 text-gray-600 hover:text-primary-600 hover:bg-white dark:text-slate-400 dark:hover:text-primary-400 dark:hover:bg-slate-600 rounded-md transition"
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M3 5a2 2 0 00-2 2v6h6V7H4a1 1 0 011-1V5H3zm10 0a2 2 0 00-2 2v6h6V7h-3a1 1 0 011-1V5h-2z" />
          </svg>
        </button>
        <button
          type="button"
          onClick={() => wrapSelection('<code>', '</code>', t('conversations.reply.codePlaceholder'))}
          title={t('conversations.reply.code')}
          aria-label={t('conversations.reply.code')}
          className="p-1.5 text-gray-600 hover:text-primary-600 hover:bg-white dark:text-slate-400 dark:hover:text-primary-400 dark:hover:bg-slate-600 rounded-md transition"
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
          </svg>
        </button>
        <div className="w-px h-5 bg-gray-200 dark:bg-slate-600 mx-1" />
        {/* 模板按钮 */}
        <div>
          <button
            ref={templateBtnRef}
            type="button"
            onClick={() => setTemplateOpen((v) => !v)}
            title={t('conversations.reply.templates')}
            aria-label={t('conversations.reply.templates')}
            aria-expanded={templateOpen}
            aria-haspopup="menu"
            className="p-1.5 text-gray-600 hover:text-primary-600 hover:bg-white dark:text-slate-400 dark:hover:text-primary-400 dark:hover:bg-slate-600 rounded-md transition"
          >
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
            </svg>
          </button>
        </div>
        <div className="flex-1" />
        <span className="text-xs text-gray-400 hidden sm:inline">
          {t('conversations.reply.htmlSupported')}
        </span>
      </div>

      {/* CC/BCC 输入 */}
      {!isNote && (
        <div className="flex gap-3 px-3 py-2 border-b border-gray-100 dark:border-slate-600">
          <div className="flex-1">
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1">{t('conversations.reply.cc')}</label>
            <input
              type="text"
              value={cc}
              onChange={(e) => setCc(e.target.value)}
              placeholder={t('conversations.reply.multipleEmails')}
              className="w-full px-2 py-1.5 text-sm border border-gray-200 dark:border-slate-600 dark:bg-slate-700 dark:text-slate-200 dark:placeholder-slate-500 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent"
            />
          </div>
          <div className="flex-1">
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1">{t('conversations.reply.bcc')}</label>
            <input
              type="text"
              value={bcc}
              onChange={(e) => setBcc(e.target.value)}
              placeholder={t('conversations.reply.multipleEmails')}
              className="w-full px-2 py-1.5 text-sm border border-gray-200 dark:border-slate-600 dark:bg-slate-700 dark:text-slate-200 dark:placeholder-slate-500 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent"
            />
          </div>
        </div>
      )}

      {/* 编辑区 */}
      <div className="p-3">
        <textarea
          ref={textareaRef}
          value={body}
          onChange={(e) => setBody(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder={
            isNote ? t('conversations.reply.notePlaceholder') : t('conversations.reply.replyPlaceholder')
          }
          aria-label={isNote ? t('conversations.reply.noteAria') : t('conversations.reply.replyAria')}
          rows={isNote ? 3 : 5}
          className="w-full px-3 py-2.5 border border-gray-200 dark:border-slate-600 rounded-lg resize-y focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent text-sm bg-white dark:bg-slate-700 dark:text-slate-200 dark:placeholder-slate-500"
        />

        {/* 附件列表 */}
        {attachments.length > 0 && (
          <div className="mt-2 flex flex-wrap gap-2">
            {attachments.map((att) => (
              <div
                key={att.id}
                className="inline-flex items-center gap-1.5 px-2 py-1 bg-white dark:bg-slate-700 border border-gray-200 dark:border-slate-600 rounded-md text-xs group"
              >
                <svg className="w-3.5 h-3.5 text-gray-500 dark:text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M15.172 7l-6.586 6.586a2 2 0 102.828 2.828l6.414-6.586a4 4 0 00-5.656-5.656l-6.415 6.585a6 6 0 108.486 8.486L20.5 13" />
                </svg>
                <span className="text-gray-700 dark:text-slate-200 max-w-[150px] truncate">{att.fileName}</span>
                <span className="text-gray-400 dark:text-slate-500">{formatFileSize(att.size)}</span>
                <button
                  onClick={() => removeAttachment(att.id)}
                  className="text-gray-400 hover:text-red-500 dark:text-slate-500 dark:hover:text-red-400 ml-0.5"
                  title={t('conversations.reply.remove')}
                  aria-label={t('conversations.reply.removeAttachmentAria', { name: att.fileName })}
                >
                  <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* 底部操作栏 */}
      <div className="px-3 py-2.5 border-t border-gray-100 flex items-center justify-between">
        <div className="flex items-center gap-1">
          <input
            ref={fileInputRef}
            type="file"
            multiple
            onChange={(e) => handleFileSelect(e.target.files)}
            className="hidden"
          />
          <button
            onClick={() => fileInputRef.current?.click()}
            disabled={uploading || submitting}
            className="p-1.5 text-gray-500 hover:text-primary-600 hover:bg-gray-100 rounded-md transition"
            title={t('conversations.reply.addAttachment')}
            aria-label={t('conversations.reply.addAttachment')}
          >
            {uploading ? (
              <svg className="w-5 h-5 animate-spin" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
              </svg>
            ) : (
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M15.172 7l-6.586 6.586a2 2 0 102.828 2.828l6.414-6.586a4 4 0 00-5.656-5.656l-6.415 6.585a6 6 0 108.486 8.486L20.5 13" />
              </svg>
            )}
          </button>
          <span className="text-xs text-gray-400 ml-1">{t('conversations.reply.attachmentHint')}</span>
        </div>

        <div className="flex items-center gap-2">
          {!isNote && onSaveDraft && (
            <button
              onClick={handleSaveDraft}
              disabled={savingDraft || !body.trim()}
              className="px-3 py-1.5 text-sm font-medium text-gray-600 hover:text-gray-800 hover:bg-gray-100 disabled:text-gray-400 disabled:bg-gray-50 rounded-lg transition"
            >
              {savingDraft ? t('conversations.reply.savingDraft') : t('conversations.reply.saveDraft')}
            </button>
          )}
          <button
            onClick={handleSubmit}
            disabled={submitting || uploading || (!body.trim() && attachments.length === 0) || canUndo}
            className={`px-4 py-1.5 text-sm font-medium rounded-lg transition ${
              isNote
                ? 'bg-amber-600 hover:bg-amber-700 disabled:bg-amber-300 text-white'
                : 'bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white'
            }`}
          >
            {submitting ? t('conversations.reply.sending') : isNote ? t('conversations.reply.saveNote') : t('conversations.reply.sendReply')}
          </button>
        </div>
      </div>

      {/* 撤销提示 */}
      {canUndo && (
        <div className="px-4 py-3 bg-amber-50 border-t border-amber-200 flex items-center justify-between animate-in slide-in-from-bottom-2 duration-200">
          <div className="flex items-center gap-2">
            <svg className="w-4 h-4 text-amber-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M13 10V3L4 14h7v7l9-11h-7z" />
            </svg>
            <span className="text-sm text-amber-800">
              {t('conversations.reply.undoCountdownPrefix')}
              {/* 拆键目的：让被强调的秒数重新拥有自己的 `<span>`（修订记录第 39 行） */}
              <span className="font-semibold">{undoCountdown}</span>
              {t('conversations.reply.undoCountdownSuffix', { count: undoCountdown })}
            </span>
          </div>
          <button
            onClick={handleUndo}
            className="px-3 py-1 text-sm font-medium text-amber-700 hover:text-amber-900 hover:bg-amber-100 rounded-md transition"
          >
            {t('conversations.reply.undo')}
          </button>
        </div>
      )}
    </div>
      {/* 快捷回复模板下拉（通过 Portal 渲染到 body，避免被父容器 overflow 裁剪） */}
      {templateOpen && templatePos && typeof document !== 'undefined' &&
        createPortal(
          <div
            ref={templatePanelRef}
            role="menu"
            aria-orientation="vertical"
            style={{ position: 'fixed', top: templatePos.top, left: templatePos.left, width: templatePos.width, zIndex: 60 }}
            className="bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-lg shadow-2xl overflow-hidden"
          >
            <div className="p-2 border-b border-gray-100 dark:border-slate-600">
              <input
                type="text"
                value={templateSearch}
                onChange={(e) => setTemplateSearch(e.target.value)}
                placeholder={t('conversations.reply.templateSearch')}
                className="w-full px-3 py-1.5 text-sm border border-gray-200 dark:border-slate-600 dark:bg-slate-700 dark:text-slate-200 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                autoFocus
              />
            </div>
            <div className="max-h-64 overflow-y-auto">
              {templatesLoading ? (
                <div className="p-4 text-center text-sm text-gray-400 dark:text-slate-500">{t('conversations.reply.templatesLoading')}</div>
              ) : templates.length === 0 ? (
                <div className="p-4 text-center text-sm text-gray-400 dark:text-slate-500">{t('conversations.reply.noTemplates')}</div>
              ) : (
                Object.entries(groupedTemplates).map(([category, items]) => (
                  <div key={category}>
                    <div className="px-3 py-1.5 text-xs font-medium text-gray-500 dark:text-slate-400 bg-gray-50/60 dark:bg-slate-700/50 border-t border-gray-100 dark:border-slate-600 first:border-t-0">
                      {category}
                    </div>
                    {items.map((tpl) => (
                      <button
                        key={tpl.id}
                        type="button"
                        onClick={() => insertTemplate(tpl)}
                        className="w-full px-3 py-2 text-left text-sm text-gray-700 dark:text-slate-200 hover:bg-primary-50 dark:hover:bg-primary-900/30 hover:text-primary-700 dark:hover:text-primary-400 transition"
                      >
                        <div className="font-medium truncate">{tpl.name}</div>
                        <div
                          className="text-xs text-gray-400 dark:text-slate-500 mt-0.5 truncate"
                          dangerouslySetInnerHTML={{ __html: sanitizeHtml(tpl.body).replace(/<[^>]*>/g, ' ').slice(0, 60) + '...' }}
                        />
                      </button>
                    ))}
                  </div>
                ))
              )}
            </div>
          </div>,
          document.body,
        )}
    </>
  );
}
