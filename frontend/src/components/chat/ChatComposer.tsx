import {
  useState,
  useRef,
  useCallback,
  useEffect,
  ChangeEvent,
  KeyboardEvent,
} from 'react';
import type { CannedResponseDto, UserResponse } from '../../api/types';
import { cannedResponseApi, attachmentApi, chatApi, userApi } from '../../api';
import { useToast } from '../../context/ToastContext';
import { useI18n } from '../../context/I18nContext';
import {
  Send,
  Paperclip,
  Zap,
  X,
  Search,
  FileText,
  Loader2,
  Image as ImageIcon,
  Smile,
  AtSign,
} from 'lucide-react';

interface ChatComposerProps {
  /** 会话已关闭时禁用输入 */
  disabled?: boolean;
  /** 发送消息回调,返回 Promise 以便组件处理 loading 与清空 */
  onSend: (
    content: string,
    attachment?: { url: string; name: string } | null,
  ) => Promise<void>;
  /** 当前会话的邮箱 ID,用于筛选快捷回复(客服侧) */
  mailboxId?: number | null;
  /** 当前会话 ID,用于推送输入状态(为空则不推送,客服侧使用) */
  sessionId?: number | null;
  /** 客户侧模式:使用 sessionToken 调用 notifyTyping 接口,同时禁用 @提及 / 快捷回复 */
  isCustomer?: boolean;
  /** 客户侧 sessionToken,isCustomer 为 true 时必填 */
  sessionToken?: string | null;
}

/** 文件大小上限:25MB(与项目全局约束一致) */
const MAX_FILE_SIZE = 25 * 1024 * 1024;

/** 消息内容长度上限(与 ChatMessageBubble 编辑器一致) */
const MAX_CONTENT_LENGTH = 5000;

/** 常用表情(轻量级,无需第三方库) */
const COMMON_EMOJIS = [
  '😀', '😃', '😄', '😁', '🙂', '😊', '😉', '😍',
  '🤔', '😎', '😅', '😂', '😭', '😱', '😡', '👍',
  '👎', '👌', '👏', '🙏', '💪', '🎉', '🎁', '❤️',
  '🔥', '✨', '⭐', '💯', '📢', '📝', '✅', '❌',
];

/** 输入状态推送节流间隔(毫秒),避免频繁调用 API */
const TYPING_NOTIFY_INTERVAL_MS = 2000;

/** @提及 自动完成最大显示条数 */
const MENTION_MAX_RESULTS = 8;

/** @提及 查询最小长度(达到后才开始筛选) */
const MENTION_QUERY_MIN_LENGTH = 0;

/** @提及 触发字符 */
const MENTION_TRIGGER = '@';

/** @提及 查询文本允许的字符(字母数字中文下划线点连字符空格) */
const MENTION_QUERY_CHARS = /[\w\u4e00-\u9fa5.\-\s]/;

/**
 * 允许的 MIME 类型白名单。
 *
 * 原值是中文类型标签，但**只在 `ALLOWED_MIME[mime]` 的真值判断里用过一次、
 * 从未渲染**（改造前实测：全仓库仅 `ALLOWED_MIME` 的定义与这一处真值判断）。
 * 因此这里收敛为纯白名单集合，不再保留不可达的展示文案。
 */
const ALLOWED_MIME = new Set<string>([
  'image/jpeg',
  'image/png',
  'image/gif',
  'image/webp',
  'image/bmp',
  'image/svg+xml',
  'application/pdf',
  'application/msword',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  'application/vnd.ms-excel',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  'application/vnd.ms-powerpoint',
  'application/vnd.openxmlformats-officedocument.presentationml.presentation',
  'text/plain',
  'text/csv',
  'application/zip',
  'application/x-zip-compressed',
  'application/x-rar-compressed',
]);

/** 通过扩展名兜底识别(部分系统对某些类型不返回标准 MIME) */
function guessMimeByExt(fileName: string): string | null {
  const ext = fileName.split('.').pop()?.toLowerCase() ?? '';
  const map: Record<string, string> = {
    jpg: 'image/jpeg',
    jpeg: 'image/jpeg',
    png: 'image/png',
    gif: 'image/gif',
    webp: 'image/webp',
    bmp: 'image/bmp',
    svg: 'image/svg+xml',
    pdf: 'application/pdf',
    doc: 'application/msword',
    docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    xls: 'application/vnd.ms-excel',
    xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    ppt: 'application/vnd.ms-powerpoint',
    pptx: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
    txt: 'text/plain',
    csv: 'text/csv',
    zip: 'application/zip',
    rar: 'application/x-rar-compressed',
  };
  return map[ext] ?? null;
}

function isImageFile(fileName: string, mime: string): boolean {
  return mime.startsWith('image/') || /\.(png|jpe?g|gif|webp|bmp|svg)$/i.test(fileName);
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

interface PendingAttachment {
  file: File;
  url: string;
  name: string;
  isImage: boolean;
}

export default function ChatComposer({
  disabled,
  onSend,
  mailboxId,
  sessionId,
  isCustomer = false,
  sessionToken = null,
}: ChatComposerProps) {
  const toast = useToast();
  const { t } = useI18n();
  const [content, setContent] = useState('');
  const [sending, setSending] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [pendingAttachment, setPendingAttachment] = useState<PendingAttachment | null>(null);

  // 快捷回复弹出层
  const [showCanned, setShowCanned] = useState(false);
  const [cannedList, setCannedList] = useState<CannedResponseDto[]>([]);
  const [cannedLoading, setCannedLoading] = useState(false);
  const [cannedKeyword, setCannedKeyword] = useState('');

  // 表情面板
  const [showEmoji, setShowEmoji] = useState(false);

  // @提及自动完成
  const [mentionOpen, setMentionOpen] = useState(false);
  const [mentionQuery, setMentionQuery] = useState('');
  const [mentionUsers, setMentionUsers] = useState<UserResponse[]>([]);
  const [mentionIndex, setMentionIndex] = useState(0);
  const [mentionLoading, setMentionLoading] = useState(false);

  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const cannedBoxRef = useRef<HTMLDivElement>(null);
  const emojiBoxRef = useRef<HTMLDivElement>(null);
  const mentionBoxRef = useRef<HTMLDivElement>(null);
  const isMountedRef = useRef(true);

  // 输入状态推送节流:记录上次推送时间,避免每次按键都调用 API
  const lastTypingNotifyRef = useRef<number>(0);

  // @提及:触发字符 @ 在内容中的起始位置(光标位置 - query 长度 - 1)
  // null 表示当前不在 @提及 输入模式
  const mentionStartRef = useRef<number | null>(null);
  // 已加载的全部用户(懒加载,首次输入 @ 时获取)
  const allUsersRef = useRef<UserResponse[] | null>(null);

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
    };
  }, []);

  /** 校验文件大小与类型 */
  const validateFile = useCallback((file: File): string | null => {
    if (file.size > MAX_FILE_SIZE) {
      return t('chat.composer.fileTooLarge', { size: formatFileSize(file.size) });
    }
    let mime = file.type;
    if (!mime) {
      mime = guessMimeByExt(file.name) ?? '';
    }
    if (!mime || !ALLOWED_MIME.has(mime)) {
      return t('chat.composer.unsupportedFileType');
    }
    return null;
  }, [t]);

  /** 处理文件选择与上传 */
  const handleFileSelect = useCallback(
    async (file: File) => {
      const err = validateFile(file);
      if (err) {
        toast.error(err);
        return;
      }

      try {
        setUploading(true);
        const resp = await attachmentApi.upload(file);
        if (!isMountedRef.current) return;
        setPendingAttachment({
          file,
          url: resp.url,
          name: file.name,
          isImage: isImageFile(file.name, file.type || guessMimeByExt(file.name) || ''),
        });
      } catch (e: unknown) {
        if (!isMountedRef.current) return;
        const msg = e instanceof Error ? e.message : t('chat.composer.uploadFailed');
        toast.error(msg);
      } finally {
        if (isMountedRef.current) setUploading(false);
      }
    },
    [validateFile, toast, t],
  );

  const handleFileInputChange = useCallback(
    (e: ChangeEvent<HTMLInputElement>) => {
      const file = e.target.files?.[0];
      if (file) handleFileSelect(file);
      // 清空 input,便于重复选择同一文件
      e.target.value = '';
    },
    [handleFileSelect],
  );

  /** 拖拽上传 */
  const handleDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      if (disabled || uploading) return;
      const file = e.dataTransfer.files?.[0];
      if (file) handleFileSelect(file);
    },
    [disabled, uploading, handleFileSelect],
  );

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
  }, []);

  /** 加载快捷回复列表 */
  const loadCanned = useCallback(
    async (keyword: string) => {
      try {
        setCannedLoading(true);
        const list = keyword
          ? await cannedResponseApi.search(keyword, mailboxId)
          : await cannedResponseApi.list(mailboxId);
        if (!isMountedRef.current) return;
        setCannedList(list);
      } catch {
        if (!isMountedRef.current) return;
        setCannedList([]);
      } finally {
        if (isMountedRef.current) setCannedLoading(false);
      }
    },
    [mailboxId],
  );

  const handleToggleCanned = useCallback(() => {
    setShowCanned((prev) => {
      const next = !prev;
      if (next && cannedList.length === 0) {
        loadCanned('');
      }
      // 互斥:打开快捷回复时关闭表情面板
      if (next && showEmoji) setShowEmoji(false);
      return next;
    });
  }, [cannedList.length, loadCanned, showEmoji]);

  const handleCannedSearch = useCallback(
    (value: string) => {
      setCannedKeyword(value);
      loadCanned(value);
    },
    [loadCanned],
  );

  const handlePickCanned = useCallback(
    (item: CannedResponseDto) => {
      setContent((prev) => (prev ? `${prev}\n${item.body}` : item.body));
      setShowCanned(false);
      setCannedKeyword('');
      requestAnimationFrame(() => textareaRef.current?.focus());
    },
    [],
  );

  /** 发送消息 */
  const canSend = (content.trim().length > 0 || pendingAttachment !== null) && !sending && !uploading;

  const handleSend = useCallback(async () => {
    if (!canSend) return;
    try {
      setSending(true);
      await onSend(
        content.trim(),
        pendingAttachment
          ? { url: pendingAttachment.url, name: pendingAttachment.name }
          : null,
      );
      if (!isMountedRef.current) return;
      setContent('');
      setPendingAttachment(null);
      requestAnimationFrame(() => textareaRef.current?.focus());
    } catch (e: unknown) {
      if (!isMountedRef.current) return;
      const msg = e instanceof Error ? e.message : t('chat.composer.sendFailed');
      toast.error(msg);
    } finally {
      if (isMountedRef.current) setSending(false);
    }
  }, [canSend, content, pendingAttachment, onSend, toast, t]);

  // 加载全部用户(懒加载,首次输入 @ 时触发)
  const ensureUsersLoaded = useCallback(async () => {
    if (allUsersRef.current) return allUsersRef.current;
    try {
      setMentionLoading(true);
      const resp = await userApi.list({ pageSize: 200 });
      if (!isMountedRef.current) return null;
      const list = Array.isArray(resp?.data) ? resp.data : [];
      // 仅保留未删除且姓名有效的用户
      const filtered = list.filter((u) => !u.deletedAt && u.fullName);
      allUsersRef.current = filtered;
      return filtered;
    } catch {
      if (!isMountedRef.current) return null;
      allUsersRef.current = [];
      return [];
    } finally {
      if (isMountedRef.current) setMentionLoading(false);
    }
  }, []);

  /** 检测当前光标位置是否处于 @提及 输入上下文,返回查询文本或 null */
  const detectMentionContext = useCallback(
    (value: string, cursorPos: number): { start: number; query: string } | null => {
      // 从光标位置向前查找最近的未闭合 @
      let i = cursorPos - 1;
      while (i >= 0) {
        const ch = value[i];
        if (ch === MENTION_TRIGGER) {
          // 检查 @ 前是否为行首或空白(避免误匹配邮箱地址中的 @)
          if (i === 0 || /\s/.test(value[i - 1])) {
            const query = value.substring(i + 1, cursorPos);
            // 查询文本必须全部为允许字符
            if (query.length >= MENTION_QUERY_MIN_LENGTH && /^[^\n]*$/.test(query)) {
              for (const q of query) {
                if (!MENTION_QUERY_CHARS.test(q)) return null;
              }
              return { start: i, query };
            }
          }
          return null;
        }
        // 遇到换行或空格(且非查询文本内部)则中止
        if (ch === '\n' || (/\s/.test(ch) && i < cursorPos - 1)) {
          // 空格在查询文本中是允许的(支持姓名中含空格),但若 @ 后已出现空格则继续向前查找
          // 仅当遇到换行或第二个空格时才中止
          if (ch === '\n') return null;
        }
        i--;
      }
      return null;
    },
    [],
  );

  /** 更新 @提及 候选用户列表 */
  const updateMentionList = useCallback(
    (query: string) => {
      const all = allUsersRef.current ?? [];
      if (!all.length) {
        setMentionUsers([]);
        return;
      }
      const q = query.trim().toLowerCase();
      const filtered = q
        ? all.filter(
            (u) =>
              u.fullName.toLowerCase().includes(q) ||
              u.email.toLowerCase().includes(q),
          )
        : all;
      setMentionUsers(filtered.slice(0, MENTION_MAX_RESULTS));
      setMentionIndex(0);
    },
    [],
  );

  /** 关闭 @提及 面板 */
  const closeMention = useCallback(() => {
    setMentionOpen(false);
    setMentionQuery('');
    setMentionUsers([]);
    setMentionIndex(0);
    mentionStartRef.current = null;
  }, []);

  /** 选中某个用户后,将 @query 替换为 @[fullName] 并追加一个空格 */
  const handlePickMention = useCallback(
    (user: UserResponse) => {
      const textarea = textareaRef.current;
      if (!textarea) {
        closeMention();
        return;
      }
      const start = mentionStartRef.current;
      if (start == null) {
        closeMention();
        return;
      }
      const cursorPos = textarea.selectionStart ?? content.length;
      const before = content.substring(0, start);
      const after = content.substring(cursorPos);
      const insert = `${MENTION_TRIGGER}[${user.fullName}] `;
      const next = before + insert + after;
      setContent(next);
      closeMention();
      // 将光标定位到插入内容之后
      requestAnimationFrame(() => {
        const el = textareaRef.current;
        if (!el) return;
        const pos = before.length + insert.length;
        el.focus();
        el.setSelectionRange(pos, pos);
        el.style.height = 'auto';
        el.style.height = `${Math.min(el.scrollHeight, 160)}px`;
      });
    },
    [content, closeMention],
  );

  const handleKeyDown = useCallback(
    (e: KeyboardEvent<HTMLTextAreaElement>) => {
      // @提及 键盘导航:仅当面板打开且有候选时拦截
      if (mentionOpen && mentionUsers.length > 0) {
        if (e.key === 'ArrowDown') {
          e.preventDefault();
          setMentionIndex((prev) => (prev + 1) % mentionUsers.length);
          return;
        }
        if (e.key === 'ArrowUp') {
          e.preventDefault();
          setMentionIndex((prev) => (prev - 1 + mentionUsers.length) % mentionUsers.length);
          return;
        }
        if (e.key === 'Enter' || e.key === 'Tab') {
          e.preventDefault();
          const target = mentionUsers[mentionIndex];
          if (target) handlePickMention(target);
          return;
        }
        if (e.key === 'Escape') {
          e.preventDefault();
          closeMention();
          return;
        }
      }
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        handleSend();
      }
    },
    [handleSend, mentionOpen, mentionUsers, mentionIndex, handlePickMention, closeMention],
  );

  // 自适应高度 + 推送输入状态(节流) + @提及 检测
  const handleContentChange = useCallback(
    (e: ChangeEvent<HTMLTextAreaElement>) => {
      const value = e.target.value;
      setContent(value);
      const el = e.target;
      el.style.height = 'auto';
      el.style.height = `${Math.min(el.scrollHeight, 160)}px`;

      // @提及 上下文检测(仅客服侧启用,客户侧禁用)
      if (!isCustomer) {
        const cursorPos = el.selectionStart ?? value.length;
        const ctx = detectMentionContext(value, cursorPos);
        if (ctx) {
          // 确保 @ 前面没有已闭合的 ] (避免在已完成的 @[xxx] 后再次触发)
          if (ctx.start > 0 && value[ctx.start - 1] === ']') {
            closeMention();
          } else {
            mentionStartRef.current = ctx.start;
            setMentionQuery(ctx.query);
            if (!mentionOpen) setMentionOpen(true);
            // 懒加载用户列表
            if (!allUsersRef.current) {
              void ensureUsersLoaded().then((list) => {
                if (list) updateMentionList(ctx.query);
              });
            } else {
              updateMentionList(ctx.query);
            }
          }
        } else if (mentionOpen) {
          closeMention();
        }
      }

      // 节流推送输入状态
      if (!disabled) {
        const now = Date.now();
        if (now - lastTypingNotifyRef.current > TYPING_NOTIFY_INTERVAL_MS) {
          lastTypingNotifyRef.current = now;
          if (isCustomer && sessionToken) {
            // 客户侧:使用 token 路由推送(无需认证)
            chatApi.notifyTypingByToken(sessionToken).catch(() => {
              // 输入状态推送失败不影响主流程
            });
          } else if (sessionId) {
            // 客服侧:使用已认证 sessionId 路由
            chatApi.notifyTyping(sessionId, 'agent').catch(() => {
              // 输入状态推送失败不影响主流程
            });
          }
        }
      }
    },
    [sessionId, disabled, isCustomer, sessionToken, mentionOpen, detectMentionContext, updateMentionList, ensureUsersLoaded, closeMention],
  );

  // 表情选择
  const handleEmojiPick = useCallback((emoji: string) => {
    setContent((prev) => prev + emoji);
    requestAnimationFrame(() => textareaRef.current?.focus());
  }, []);

  const handleToggleEmoji = useCallback(() => {
    setShowEmoji((v) => !v);
    // 关闭表情面板时关闭快捷回复(互斥)
    if (showCanned) setShowCanned(false);
  }, [showCanned]);

  // 点击外部关闭快捷回复弹出层
  useEffect(() => {
    if (!showCanned) return;
    const handler = (e: MouseEvent) => {
      if (cannedBoxRef.current && !cannedBoxRef.current.contains(e.target as Node)) {
        setShowCanned(false);
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [showCanned]);

  // 点击外部关闭表情面板
  useEffect(() => {
    if (!showEmoji) return;
    const handler = (e: MouseEvent) => {
      if (emojiBoxRef.current && !emojiBoxRef.current.contains(e.target as Node)) {
        setShowEmoji(false);
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [showEmoji]);

  // 点击外部关闭 @提及 面板
  useEffect(() => {
    if (!mentionOpen) return;
    const handler = (e: MouseEvent) => {
      if (mentionBoxRef.current && !mentionBoxRef.current.contains(e.target as Node)) {
        closeMention();
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [mentionOpen, closeMention]);

  // 组件卸载时清理 @mention 状态
  useEffect(() => {
    return () => {
      mentionStartRef.current = null;
      allUsersRef.current = null;
    };
  }, []);

  return (
    <div
      className={`relative border-t border-slate-200/70 dark:border-slate-700/60 bg-white/95 dark:bg-slate-800/90 backdrop-blur-xl transition-all ${
        disabled ? 'opacity-60 pointer-events-none' : ''
      }`}
      onDrop={handleDrop}
      onDragOver={handleDragOver}
    >
      {/* @提及 自动完成面板(仅客服侧) */}
      {!isCustomer && mentionOpen && (
        <div
          ref={mentionBoxRef}
          className="absolute bottom-full left-3 right-3 mb-3 bg-white dark:bg-slate-800 rounded-2xl shadow-[0_20px_60px_-15px_rgba(15,23,42,0.35)] border border-slate-200 dark:border-slate-700 overflow-hidden z-20"
        >
          <div className="flex items-center gap-2 px-4 py-3 border-b border-slate-100 dark:border-slate-700 bg-gradient-to-r from-blue-50/60 to-indigo-50/60 dark:from-blue-900/30 dark:to-indigo-900/20">
            <AtSign className="w-4 h-4 text-blue-500" />
            <span className="text-sm font-bold text-slate-700 dark:text-slate-200">
              {t('chat.composer.mentionTitle')}
            </span>
            {mentionLoading && (
              <Loader2 className="w-3.5 h-3.5 animate-spin text-slate-400 ml-auto" />
            )}
          </div>
          <div className="max-h-60 overflow-y-auto">
            {!mentionLoading && mentionUsers.length === 0 ? (
              <div className="py-8 text-center text-sm text-slate-400">
                <div className="w-10 h-10 mx-auto mb-2 rounded-full bg-slate-100 dark:bg-slate-700/50 flex items-center justify-center">
                  <AtSign className="w-5 h-5" />
                </div>
                {mentionQuery
                  ? t('chat.composer.mentionNoMatch')
                  : t('chat.composer.mentionEmpty')}
              </div>
            ) : (
              mentionUsers.map((user, idx) => (
                <button
                  key={user.id}
                  type="button"
                  onMouseDown={(e) => {
                    e.preventDefault();
                    handlePickMention(user);
                  }}
                  onMouseEnter={() => setMentionIndex(idx)}
                  className={`w-full flex items-center gap-3 px-4 py-2.5 transition-all text-left ${
                    idx === mentionIndex
                      ? 'bg-gradient-to-r from-blue-50/90 to-indigo-50/90 dark:from-blue-900/40 dark:to-indigo-900/30'
                      : 'hover:bg-slate-50 dark:hover:bg-slate-700/50'
                  } border-b border-slate-50 dark:border-slate-700/40 last:border-0`}
                >
                  <div className="w-9 h-9 rounded-xl bg-gradient-to-br from-emerald-400 to-teal-600 flex items-center justify-center text-white text-xs font-bold flex-shrink-0 shadow-sm">
                    {user.fullName?.charAt(0) || '?'}
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-semibold text-slate-700 dark:text-slate-200 truncate">
                      {user.fullName}
                    </p>
                    {user.email && (
                      <p className="text-xs text-slate-400 dark:text-slate-500 truncate">
                        {user.email}
                      </p>
                    )}
                  </div>
                  {idx === mentionIndex && (
                    <span className="px-2 py-0.5 rounded-md bg-blue-500 text-white text-[10px] font-bold shadow-sm">
                      Enter
                    </span>
                  )}
                </button>
              ))
            )}
          </div>
          {mentionUsers.length > 0 && (
            <div className="px-4 py-2 border-t border-slate-100 dark:border-slate-700 bg-slate-50/50 dark:bg-slate-900/30 text-[11px] text-slate-500 dark:text-slate-400 font-medium flex items-center justify-between">
              <span>{t('chat.composer.mentionHint')}</span>
              <span className="text-slate-400">
                {t('chat.composer.mentionCount', { count: mentionUsers.length })}
              </span>
            </div>
          )}
        </div>
      )}

      {/* 快捷回复弹出层(仅客服侧) */}
      {!isCustomer && showCanned && (
        <div
          ref={cannedBoxRef}
          className="absolute bottom-full left-3 right-3 mb-3 bg-white dark:bg-slate-800 rounded-2xl shadow-[0_20px_60px_-15px_rgba(15,23,42,0.35)] border border-slate-200 dark:border-slate-700 overflow-hidden z-10"
        >
          <div className="p-4 border-b border-slate-100 dark:border-slate-700 bg-gradient-to-r from-amber-50/60 to-orange-50/60 dark:from-amber-900/20 dark:to-orange-900/20">
            <div className="flex items-center gap-2 mb-3">
              <div className="w-8 h-8 rounded-xl bg-gradient-to-br from-amber-400 to-orange-500 flex items-center justify-center shadow-sm">
                <Zap className="w-4 h-4 text-white" />
              </div>
              <div>
                <div className="text-sm font-bold text-slate-700 dark:text-slate-200">
                  {t('chat.composer.cannedTitle')}
                </div>
                <div className="text-[11px] text-slate-500 dark:text-slate-400">
                  {t('chat.composer.cannedSubtitle')}
                </div>
              </div>
              <span className="ml-auto px-2 py-0.5 rounded-md bg-white dark:bg-slate-700 text-[10px] font-bold text-amber-600 dark:text-amber-300 shadow-sm">
                {cannedList.length}
              </span>
            </div>
            <div className="relative group">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400 group-focus-within:text-blue-500 transition-colors" />
              <input
                type="text"
                value={cannedKeyword}
                onChange={(e) => handleCannedSearch(e.target.value)}
                placeholder={t('chat.composer.cannedSearchPlaceholder')}
                autoFocus
                className="w-full pl-9 pr-3 py-2 text-sm border border-slate-200 dark:border-slate-700 rounded-xl bg-white dark:bg-slate-900/50 text-slate-900 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-amber-500/30 focus:border-amber-500 transition-all shadow-sm"
              />
            </div>
          </div>
          <div className="max-h-72 overflow-y-auto">
            {cannedLoading ? (
              <div className="flex flex-col items-center justify-center gap-2 py-10 text-sm text-slate-400">
                <Loader2 className="w-6 h-6 animate-spin text-amber-500" />
                {t('common.shell.loading')}
              </div>
            ) : cannedList.length === 0 ? (
              <div className="py-10 text-center text-sm text-slate-400">
                <div className="w-12 h-12 mx-auto mb-3 rounded-2xl bg-slate-100 dark:bg-slate-700/50 flex items-center justify-center">
                  <Zap className="w-6 h-6 opacity-70" />
                </div>
                {cannedKeyword
                  ? t('chat.composer.cannedNoMatch')
                  : t('chat.composer.cannedEmpty')}
              </div>
            ) : (
              cannedList.map((item) => (
                <button
                  key={item.id}
                  onClick={() => handlePickCanned(item)}
                  className="w-full text-left px-4 py-3 hover:bg-amber-50/50 dark:hover:bg-amber-900/10 transition-all border-b border-slate-50 dark:border-slate-700/40 last:border-0 group"
                >
                  <p className="text-sm font-bold text-slate-700 dark:text-slate-200 truncate group-hover:text-amber-600 dark:group-hover:text-amber-400 transition-colors">
                    {item.name}
                  </p>
                  <p className="text-xs text-slate-500 dark:text-slate-400 truncate mt-1 leading-relaxed line-clamp-2">
                    {item.body}
                  </p>
                </button>
              ))
            )}
          </div>
        </div>
      )}

      {/* 表情面板 */}
      {showEmoji && (
        <div
          ref={emojiBoxRef}
          className="absolute bottom-full left-3 right-3 mb-3 p-3 bg-white dark:bg-slate-800 rounded-2xl shadow-[0_20px_60px_-15px_rgba(15,23,42,0.35)] border border-slate-200 dark:border-slate-700 z-10"
        >
          <div className="flex items-center gap-2 px-1 pb-3 mb-2 border-b border-slate-100 dark:border-slate-700">
            <div className="w-8 h-8 rounded-xl bg-gradient-to-br from-amber-300 to-yellow-500 flex items-center justify-center shadow-sm">
              <Smile className="w-4 h-4 text-white" />
            </div>
            <div>
              <span className="text-sm font-bold text-slate-700 dark:text-slate-200">
                {t('chat.composer.emojiTitle')}
              </span>
              <div className="text-[10px] text-slate-400 hidden sm:block">
                {t('chat.composer.emojiSubtitle')}
              </div>
            </div>
          </div>
          <div className="grid grid-cols-8 gap-1.5 max-h-56 overflow-y-auto">
            {COMMON_EMOJIS.map((emoji) => (
              <button
                key={emoji}
                onClick={() => handleEmojiPick(emoji)}
                className="w-10 h-10 flex items-center justify-center text-2xl hover:bg-gradient-to-br hover:from-amber-50 hover:to-orange-50 dark:hover:from-amber-900/20 dark:hover:to-orange-900/20 rounded-xl transition-all hover:scale-110 active:scale-95"
                type="button"
                aria-label={t('chat.composer.insertEmoji', { emoji })}
              >
                {emoji}
              </button>
            ))}
          </div>
        </div>
      )}

      {/* 附件预览条 */}
      {pendingAttachment && (
        <div className="px-4 pt-4">
          <div className="inline-flex items-center gap-3 px-4 py-3 bg-gradient-to-r from-slate-50 to-blue-50/50 dark:from-slate-700/60 dark:to-blue-900/20 rounded-2xl border border-slate-200 dark:border-slate-700 shadow-sm group hover:shadow-md transition-all">
            <div className="w-11 h-11 rounded-xl bg-gradient-to-br from-blue-400 to-indigo-500 text-white flex items-center justify-center flex-shrink-0 shadow-md">
              {pendingAttachment.isImage ? (
                <ImageIcon className="w-5 h-5" />
              ) : (
                <FileText className="w-5 h-5" />
              )}
            </div>
            <div className="min-w-0 max-w-[240px]">
              <p className="text-sm font-bold text-slate-700 dark:text-slate-200 truncate">
                {pendingAttachment.name}
              </p>
              <p className="text-[11px] text-slate-500 dark:text-slate-400 font-medium">
                {formatFileSize(pendingAttachment.file.size)}
              </p>
            </div>
            <button
              onClick={() => setPendingAttachment(null)}
              className="p-2 rounded-xl hover:bg-red-50 hover:text-red-500 dark:hover:bg-red-900/20 dark:hover:text-red-400 text-slate-400 transition-all hover:scale-110"
              aria-label={t('chat.composer.removeAttachment')}
            >
              <X className="w-4.5 h-4.5" />
            </button>
          </div>
        </div>
      )}

        {/* 输入区 */}
      <div className="flex items-end gap-2.5 p-3 sm:p-4">
        <input
          ref={fileInputRef}
          type="file"
          className="hidden"
          onChange={handleFileInputChange}
          accept="image/*,application/pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv,.zip,.rar"
        />

        <div className="flex items-center gap-1" title={t('chat.composer.toolbar')}>
          <button
            onClick={() => fileInputRef.current?.click()}
            disabled={disabled || uploading}
            className="p-2.5 text-slate-500 dark:text-slate-400 hover:text-blue-600 dark:hover:text-blue-400 hover:bg-blue-50 dark:hover:bg-blue-900/20 rounded-2xl transition-all hover:scale-105 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:scale-100 disabled:hover:bg-transparent disabled:hover:text-current"
            title={t('chat.composer.uploadAttachmentHint')}
            aria-label={t('chat.composer.uploadAttachment')}
          >
            {uploading ? (
              <Loader2 className="w-5 h-5 animate-spin text-blue-500" />
            ) : (
              <Paperclip className="w-5 h-5" />
            )}
          </button>

          {!isCustomer && (
            <button
              onClick={handleToggleCanned}
              disabled={disabled}
              className={`p-2.5 rounded-2xl transition-all hover:scale-105 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:scale-100 ${
                showCanned
                  ? 'text-amber-600 dark:text-amber-400 bg-gradient-to-br from-amber-50 to-orange-50 dark:from-amber-900/30 dark:to-orange-900/20 shadow-md'
                  : 'text-slate-500 dark:text-slate-400 hover:text-amber-600 dark:hover:text-amber-400 hover:bg-amber-50 dark:hover:bg-amber-900/20'
              }`}
              title={t('chat.composer.cannedTitle')}
              aria-label={t('chat.composer.cannedTitle')}
            >
              <Zap className="w-5 h-5" />
            </button>
          )}

          <button
            onClick={handleToggleEmoji}
            disabled={disabled}
            className={`p-2.5 rounded-2xl transition-all hover:scale-105 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:scale-100 ${
              showEmoji
                ? 'text-amber-600 dark:text-amber-400 bg-gradient-to-br from-amber-50 to-orange-50 dark:from-amber-900/30 dark:to-orange-900/20 shadow-md'
                : 'text-slate-500 dark:text-slate-400 hover:text-amber-600 dark:hover:text-amber-400 hover:bg-amber-50 dark:hover:bg-amber-900/20'
            }`}
            title={t('chat.composer.emojiTitle')}
            aria-label={t('chat.composer.emojiTitle')}
          >
            <Smile className="w-5 h-5" />
          </button>
        </div>

        <div className="flex-1 relative group">
          <textarea
            ref={textareaRef}
            value={content}
            onChange={handleContentChange}
            onKeyDown={handleKeyDown}
            disabled={disabled}
            placeholder={
              disabled ? t('chat.composer.sessionClosed') : t('chat.composer.placeholder')
            }
            rows={1}
            maxLength={MAX_CONTENT_LENGTH}
            className="w-full px-4 py-3 text-sm border-2 border-slate-200 dark:border-slate-700 rounded-2xl bg-white dark:bg-slate-900/50 text-slate-900 dark:text-slate-100 placeholder:text-slate-400 resize-none focus:outline-none focus:ring-4 focus:ring-blue-500/20 focus:border-blue-500 transition-all shadow-sm group-focus-within:shadow-md disabled:opacity-60 disabled:cursor-not-allowed max-h-52 whitespace-pre-wrap break-words leading-relaxed"
            style={{ minHeight: '48px' }}
          />
          {content.length > 0 && (
            <div className={`absolute right-3 -top-7 text-[10px] font-semibold px-2 py-0.5 rounded-md shadow-sm ${
              content.length > MAX_CONTENT_LENGTH
                ? 'bg-red-50 text-red-600 dark:bg-red-900/30 dark:text-red-400'
                : content.length > MAX_CONTENT_LENGTH * 0.8
                ? 'bg-amber-50 text-amber-600 dark:bg-amber-900/30 dark:text-amber-400'
                : 'bg-slate-100 text-slate-500 dark:bg-slate-700/60 dark:text-slate-400'
            }`}>
              {content.length} / {MAX_CONTENT_LENGTH}
            </div>
          )}
        </div>

        <button
          onClick={handleSend}
          disabled={!canSend}
          title={
            canSend
              ? t('chat.composer.sendMessage')
              : disabled
              ? t('chat.composer.sessionClosed')
              : t('chat.composer.enterContent')
          }
          className={`group/btn p-3 rounded-2xl transition-all shadow-md hover:shadow-lg active:scale-95 flex items-center justify-center relative overflow-hidden ${
            canSend
              ? 'bg-gradient-to-br from-blue-500 via-indigo-500 to-purple-600 text-white hover:from-blue-600 hover:via-indigo-600 hover:to-purple-700 hover:-translate-y-0.5'
              : 'bg-slate-200 dark:bg-slate-700 text-slate-400 dark:text-slate-500 cursor-not-allowed'
          }`}
          aria-label={t('chat.composer.send')}
        >
          {sending ? (
            <Loader2 className="w-5 h-5 animate-spin" />
          ) : (
            <Send className="w-5 h-5 group-hover/btn:translate-x-0.5 group-hover/btn:-translate-y-0.5 transition-transform" />
          )}
        </button>
      </div>
    </div>
  );
}
