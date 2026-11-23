/* eslint-disable react-refresh/only-export-components -- 文件同时导出组件与工具函数/常量，fast refresh 非关注点 */
import { memo, useState, useCallback, useEffect, useRef, ChangeEvent, KeyboardEvent } from 'react';
import type { ChatMessageDto } from '../../api/types';
import { safeAttachmentUrl } from '../../utils/format';
import { useI18n } from '../../context/I18nContext';
import { getBoundLocale, toIntlLocale } from '../../i18n/locale';
import {
  AlertCircle,
  Copy,
  Check,
  FileText,
  Download,
  MoreVertical,
  RotateCcw,
  Pencil,
  CheckCheck,
  Sparkles,
} from 'lucide-react';

interface ChatMessageBubbleProps {
  message: ChatMessageDto;
  /** 搜索高亮关键词 */
  highlight?: string;
  /** 当前登录用户 ID,用于判断是否可撤回/编辑 */
  currentUserId?: number | null;
  /** 是否处于编辑模式(由父组件控制) */
  isEditing?: boolean;
  /** 进入编辑模式回调 */
  onEnterEdit?: (messageId: number) => void;
  /** 取消编辑回调 */
  onCancelEdit?: () => void;
  /** 提交编辑回调 */
  onSubmitEdit?: (messageId: number, content: string) => Promise<void>;
  /** 撤回消息回调 */
  onRecall?: (messageId: number) => Promise<void>;
  /** 撤回/编辑操作进行中(由父组件传入,禁用按钮) */
  actionPending?: boolean;
}

const SENDER_TYPE_AVATAR_COLORS: Record<string, string> = {
  customer: 'bg-gradient-to-br from-blue-500 to-indigo-600',
  agent: 'bg-gradient-to-br from-emerald-500 to-teal-600',
  system: 'bg-gradient-to-br from-slate-400 to-slate-500',
};

/** 撤回时间窗口(与后端 ChatService.RecallMessageAsync 保持一致:2 分钟) */
const RECALL_WINDOW_MS = 2 * 60 * 1000;

/** 匹配 @[姓名] 格式的提及标记 */
const MENTION_REGEX = /@\[([^\]]+)\]/g;

/**
 * 将文本按关键词分段,匹配部分高亮;同时将 @[姓名] 提及标记渲染为蓝色高亮标签。
 * 提及高亮优先于搜索关键词高亮,确保 @用户名 始终可见。
 * @param isAgent 客服消息(蓝底白字)使用更亮的高亮配色
 */
function renderHighlightedText(text: string, highlight?: string, isAgent = false) {
  // 先按 @提及 分段
  const mentionParts: Array<{ text: string; isMention: boolean }> = [];
  let lastIndex = 0;
  let match: RegExpExecArray | null;
  // 重置正则 lastIndex(全局正则复用时必须)
  MENTION_REGEX.lastIndex = 0;
  while ((match = MENTION_REGEX.exec(text)) !== null) {
    if (match.index > lastIndex) {
      mentionParts.push({ text: text.substring(lastIndex, match.index), isMention: false });
    }
    mentionParts.push({ text: match[1], isMention: true });
    lastIndex = match.index + match[0].length;
  }
  if (lastIndex < text.length) {
    mentionParts.push({ text: text.substring(lastIndex), isMention: false });
  }

  // 客服消息(蓝底白字)使用半透明白色背景;客户消息(浅底深字)使用蓝色背景
  const mentionClassName = isAgent
    ? 'inline-flex items-center gap-0.5 px-1 mx-0.5 rounded bg-white/25 text-white font-medium'
    : 'inline-flex items-center gap-0.5 px-1 mx-0.5 rounded bg-blue-100 text-blue-700 font-medium dark:bg-blue-900/40 dark:text-blue-300';

  // 对非提及部分应用搜索关键词高亮
  return mentionParts.map((part, i) => {
    if (part.isMention) {
      return (
        <span
          key={`mention-${i}`}
          className={mentionClassName}
        >
          @{part.text}
        </span>
      );
    }
    return (
      <span key={`text-${i}`}>{renderSearchHighlight(part.text, highlight)}</span>
    );
  });
}

/** 仅对搜索关键词进行高亮(供 renderHighlightedText 内部对非提及文本使用) */
function renderSearchHighlight(text: string, highlight?: string) {
  if (!highlight || !highlight.trim()) return text;
  const safe = highlight.trim();
  // 转义正则特殊字符
  const escaped = safe.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const parts = text.split(new RegExp(`(${escaped})`, 'gi'));
  return parts.map((part, i) =>
    part.toLowerCase() === safe.toLowerCase() ? (
      <mark
        key={i}
        className="bg-amber-200 dark:bg-amber-500/40 text-inherit rounded px-0.5"
      >
        {part}
      </mark>
    ) : (
      <span key={i}>{part}</span>
    ),
  );
}

/** 判断 URL 是否为图片 */
function isImageUrl(url: string): boolean {
  return /\.(png|jpe?g|gif|webp|bmp|svg)(\?.*)?$/i.test(url);
}

/**
 * HH:mm。按当前语言走 `Intl`（`zh-Hans` → `14:30`，`en` → `02:30 PM`）。
 * `locale` 显式传入时用传入值，缺省用 Provider 绑定的界面语言
 * （本函数被导出为 `formatMessageTime`，调用点可能不是组件，无法拿到 hook）。
 */
function formatTime(dateStr: string, locale?: string): string {
  try {
    const d = new Date(dateStr);
    if (isNaN(d.getTime())) return dateStr;
    return d.toLocaleTimeString(toIntlLocale(locale ?? getBoundLocale()), {
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return dateStr;
  }
}

/** 判断消息是否在可撤回时间窗口内 */
function canRecall(message: ChatMessageDto): boolean {
  if (message.isRecalled) return false;
  try {
    const created = new Date(message.createdAt).getTime();
    if (isNaN(created)) return false;
    return Date.now() - created < RECALL_WINDOW_MS;
  } catch {
    return false;
  }
}

function ChatMessageBubbleImpl({
  message,
  highlight,
  currentUserId,
  isEditing = false,
  onEnterEdit,
  onCancelEdit,
  onSubmitEdit,
  onRecall,
  actionPending = false,
}: ChatMessageBubbleProps) {
  const { t, language } = useI18n();
  const [copied, setCopied] = useState(false);

  // 复制成功后定时复位 copied 状态（集中管理 timeout，避免泄漏）
  useEffect(() => {
    if (!copied) return;
    const t = setTimeout(() => setCopied(false), 1500);
    return () => clearTimeout(t);
  }, [copied]);
  const [menuOpen, setMenuOpen] = useState(false);
  const [editContent, setEditContent] = useState(message.content);
  const [submittingEdit, setSubmittingEdit] = useState(false);
  const [recalling, setRecalling] = useState(false);

  const menuRef = useRef<HTMLDivElement>(null);
  const editTextareaRef = useRef<HTMLTextAreaElement>(null);

  // 进入编辑模式时,初始化编辑内容并聚焦
  useEffect(() => {
    if (isEditing) {
      setEditContent(message.content);
      const rafId = requestAnimationFrame(() => {
        const el = editTextareaRef.current;
        if (el) {
          el.focus();
          el.style.height = 'auto';
          el.style.height = `${Math.min(el.scrollHeight, 160)}px`;
        }
      });
      return () => cancelAnimationFrame(rafId);
    }
  }, [isEditing, message.content]);

  // 点击外部关闭菜单
  useEffect(() => {
    if (!menuOpen) return;
    const handler = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setMenuOpen(false);
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [menuOpen]);

  const handleCopy = useCallback(async () => {
    try {
      await navigator.clipboard.writeText(message.content);
      setCopied(true);
    } catch {
      // 剪贴板可能被浏览器策略拒绝,静默失败
    }
  }, [message.content]);

  const handleRecall = useCallback(async () => {
    if (!onRecall || recalling) return;
    setMenuOpen(false);
    try {
      setRecalling(true);
      await onRecall(message.id);
    } finally {
      setRecalling(false);
    }
  }, [onRecall, recalling, message.id]);

  const handleEnterEdit = useCallback(() => {
    if (!onEnterEdit) return;
    setMenuOpen(false);
    onEnterEdit(message.id);
  }, [onEnterEdit, message.id]);

  const handleCancelEdit = useCallback(() => {
    setEditContent(message.content);
    onCancelEdit?.();
  }, [message.content, onCancelEdit]);

  const handleSubmitEdit = useCallback(async () => {
    if (!onSubmitEdit || submittingEdit) return;
    const trimmed = editContent.trim();
    if (!trimmed || trimmed === message.content) {
      onCancelEdit?.();
      return;
    }
    try {
      setSubmittingEdit(true);
      await onSubmitEdit(message.id, trimmed);
    } finally {
      setSubmittingEdit(false);
    }
  }, [onSubmitEdit, submittingEdit, editContent, message.id, message.content, onCancelEdit]);

  const handleEditKeyDown = useCallback(
    (e: KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        void handleSubmitEdit();
      } else if (e.key === 'Escape') {
        e.preventDefault();
        handleCancelEdit();
      }
    },
    [handleSubmitEdit, handleCancelEdit],
  );

  const handleEditChange = useCallback((e: ChangeEvent<HTMLTextAreaElement>) => {
    setEditContent(e.target.value);
    const el = e.target;
    el.style.height = 'auto';
    el.style.height = `${Math.min(el.scrollHeight, 160)}px`;
  }, []);

  // 系统消息:居中横幅样式(适配工作流发出的满意度调查、感谢通知等多行含链接的消息)
  if (message.senderType === 'system') {
    // 识别常见满意度关键词,决定图标与主色(纯视觉,不影响逻辑)
    const satHint =
      /满意度|评价|评分|打分|调查|感谢您|感谢您的|会话已结束|survey|satisfaction|rate/i.test(
        message.content,
      );
    return (
      <div className="flex justify-center my-3 px-2">
        <div
          className={`max-w-[92%] w-full sm:max-w-[80%] rounded-2xl px-4 py-3.5 border shadow-[0_1px_0_rgba(15,23,42,0.02)] relative overflow-hidden ${
            satHint
              ? 'bg-gradient-to-br from-violet-50/80 via-fuchsia-50/60 to-blue-50/70 dark:from-violet-900/20 dark:via-fuchsia-900/15 dark:to-blue-900/15 text-slate-700 dark:text-slate-200 border-violet-200/70 dark:border-violet-800/30'
              : 'bg-gradient-to-br from-slate-50/90 to-slate-100/70 dark:from-slate-800/60 dark:to-slate-900/40 text-slate-600 dark:text-slate-300 border-slate-200/70 dark:border-slate-700/50'
          }`}
        >
          {/* 装饰光斑 */}
          {satHint && (
            <>
              <div className="pointer-events-none absolute -top-10 -right-10 w-24 h-24 rounded-full bg-violet-400/10 blur-2xl" />
              <div className="pointer-events-none absolute -bottom-10 -left-10 w-24 h-24 rounded-full bg-fuchsia-400/10 blur-2xl" />
            </>
          )}

          <div className="relative flex flex-col">
            <div className="flex items-center gap-2 mb-2">
              <div
                className={`w-7 h-7 rounded-xl flex items-center justify-center shadow-sm flex-shrink-0 ${
                  satHint
                    ? 'bg-gradient-to-br from-violet-500 to-fuchsia-500 text-white'
                    : 'bg-slate-200/80 dark:bg-slate-700/70 text-slate-500 dark:text-slate-300'
                }`}
              >
                {satHint ? (
                  <Sparkles className="w-4 h-4" strokeWidth={2.2} />
                ) : (
                  <AlertCircle className="w-4 h-4" strokeWidth={2} />
                )}
              </div>
              <div className="flex-1 min-w-0 flex items-center gap-2">
                <span className={`text-xs font-bold ${satHint ? 'text-violet-700 dark:text-violet-300' : 'text-slate-600 dark:text-slate-300'}`}>
                  {satHint ? t('chat.bubble.satisfactionSurvey') : t('chat.bubble.systemNotice')}
                </span>
                <span className="text-[11px] text-slate-400 dark:text-slate-500 tabular-nums flex-shrink-0">
                  {formatTime(message.createdAt, language)}
                </span>
              </div>
            </div>
            <p
              className={`text-sm whitespace-pre-wrap break-words leading-relaxed ${
                satHint ? 'text-slate-700 dark:text-slate-200' : 'text-slate-600 dark:text-slate-300'
              }`}
            >
              {renderHighlightedText(message.content, highlight, false)}
            </p>
          </div>
        </div>
      </div>
    );
  }

  const isAgent = message.senderType === 'agent';
  // 当前用户是否为消息发送者(用于判断可否撤回/编辑)
  const isOwnMessage =
    isAgent &&
    currentUserId != null &&
    message.senderId != null &&
    message.senderId === currentUserId;
  // 是否可撤回:本人消息 + 文本消息 + 未撤回 + 在时间窗口内
  const recallable = isOwnMessage && canRecall(message) && !!onRecall;
  // 是否可编辑:本人消息 + 文本消息 + 未撤回
  const editable = isOwnMessage && !message.isRecalled && !!message.content && !!onEnterEdit;

  // 已读状态:仅客服消息显示已读回执
  const readReceipt = isAgent && !message.isRecalled ? (
    message.isRead ? (
      <span className="flex items-center gap-0.5 text-[10px] text-blue-500 dark:text-blue-400" title={t('chat.bubble.readAt', { time: message.readAt ? formatTime(message.readAt, language) : '' })}>
        <CheckCheck className="w-3 h-3" />
      </span>
    ) : (
      <span className="flex items-center gap-0.5 text-[10px] text-slate-400 dark:text-slate-500" title={t('chat.bubble.unread')}>
        <Check className="w-3 h-3" />
      </span>
    )
  ) : null;

  return (
    <div
      className={`group flex ${isAgent ? 'justify-end' : 'justify-start'} gap-2.5`}
    >
      {!isAgent && (
        <div
          className={`w-9 h-9 rounded-xl flex items-center justify-center text-white text-sm font-semibold shadow-sm flex-shrink-0 ${
            SENDER_TYPE_AVATAR_COLORS[message.senderType] ?? SENDER_TYPE_AVATAR_COLORS.customer
          }`}
        >
          {message.senderName?.charAt(0) || '?'}
        </div>
      )}

      <div
        className={`max-w-[75%] flex flex-col ${
          isAgent ? 'items-end' : 'items-start'
        }`}
      >
        <div className="flex items-center gap-2 mb-1">
          <span className="text-xs font-medium text-slate-500 dark:text-slate-400">
            {message.senderName || (isAgent ? t('chat.bubble.agent') : t('format.customerFallback'))}
          </span>
          <span className="text-[11px] text-slate-400 dark:text-slate-500">
            {formatTime(message.createdAt, language)}
          </span>
          {message.editedAt && !message.isRecalled && (
            <span className="text-[10px] text-slate-400 dark:text-slate-500" title={t('chat.bubble.editedAt', { time: formatTime(message.editedAt, language) })}>
              {t('chat.bubble.edited')}
            </span>
          )}
          {readReceipt}
        </div>

        <div className="relative">
          {/* 编辑模式 */}
          {isEditing ? (
            <div className="bg-white dark:bg-slate-800 border border-blue-400 dark:border-blue-500 rounded-2xl rounded-br-md shadow-md p-2 min-w-[200px]">
              <textarea
                ref={editTextareaRef}
                value={editContent}
                onChange={handleEditChange}
                onKeyDown={handleEditKeyDown}
                maxLength={5000}
                rows={1}
                disabled={submittingEdit}
                className="w-full px-2 py-1.5 text-sm border border-slate-200 dark:border-slate-700 rounded-lg bg-slate-50 dark:bg-slate-900/50 text-slate-900 dark:text-slate-100 resize-none focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-60"
                style={{ minHeight: '36px' }}
              />
              <div className="flex items-center justify-between mt-1.5 px-1">
                <span className="text-[10px] text-slate-400">{t('chat.bubble.editHint')}</span>
                <div className="flex items-center gap-1">
                  <button
                    onClick={handleCancelEdit}
                    disabled={submittingEdit}
                    className="px-2 py-1 text-xs text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700 rounded transition-colors disabled:opacity-60"
                  >
                    {t('common.cancel')}
                  </button>
                  <button
                    onClick={handleSubmitEdit}
                    disabled={submittingEdit || !editContent.trim() || editContent.trim() === message.content}
                    className="px-2 py-1 text-xs bg-blue-500 hover:bg-blue-600 text-white rounded transition-colors disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-1"
                  >
                    {submittingEdit ? (
                      <>
                        <span className="inline-block w-3 h-3 border-2 border-white/40 border-t-white rounded-full animate-spin" />
                        {t('chat.bubble.saving')}
                      </>
                    ) : (
                      t('common.save')
                    )}
                  </button>
                </div>
              </div>
            </div>
          ) : (
            <div
              className={`px-4 py-2.5 transition-shadow group-hover:shadow-md ${
                message.isRecalled
                  ? 'bg-slate-100 dark:bg-slate-700/40 text-slate-400 dark:text-slate-500 italic rounded-2xl'
                  : isAgent
                    ? 'bg-gradient-to-br from-blue-500 via-indigo-500 to-indigo-600 text-white rounded-2xl rounded-br-md shadow-md shadow-indigo-500/15'
                    : 'bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 rounded-2xl rounded-bl-md border border-slate-200/70 dark:border-slate-700/60 shadow-sm'
              }`}
            >
              {message.isRecalled ? (
                <p className="text-sm">{t('chat.bubble.recalledBy', { name: message.senderName || (isAgent ? t('chat.bubble.agent') : t('format.customerFallback')) })}</p>
              ) : (
                <>
                  {message.content && (
                    <p className="text-sm whitespace-pre-wrap break-words leading-relaxed">
                      {renderHighlightedText(message.content, highlight, isAgent)}
                    </p>
                  )}

                  {message.attachmentUrl && (
                    <div
                      className={`mt-2 ${
                        message.content ? 'pt-2 border-t border-white/20' : ''
                      } ${
                        isAgent
                          ? 'border-white/20'
                          : 'border-slate-200/70 dark:border-slate-700/60'
                      }`}
                    >
                      {isImageUrl(message.attachmentUrl) ? (
                        <a
                          href={safeAttachmentUrl(message.attachmentUrl)}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="block rounded-lg overflow-hidden hover:opacity-90 transition-opacity"
                          title={message.attachmentName || t('chat.bubble.viewImage')}
                        >
                          <img
                            src={safeAttachmentUrl(message.attachmentUrl)}
                            alt={message.attachmentName || t('chat.bubble.image')}
                            className="max-w-full max-h-48 object-cover"
                            loading="lazy"
                          />
                        </a>
                      ) : (
                        <a
                          href={safeAttachmentUrl(message.attachmentUrl)}
                          target="_blank"
                          rel="noopener noreferrer"
                          download={message.attachmentName || undefined}
                          className={`flex items-center gap-2.5 p-2.5 rounded-lg transition-colors ${
                            isAgent
                              ? 'bg-white/15 hover:bg-white/25 text-white'
                              : 'bg-slate-50 dark:bg-slate-700/50 hover:bg-slate-100 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-200'
                          }`}
                        >
                          <div
                            className={`w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0 ${
                              isAgent
                                ? 'bg-white/20'
                                : 'bg-blue-50 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400'
                            }`}
                          >
                            <FileText className="w-5 h-5" />
                          </div>
                          <div className="min-w-0 flex-1">
                            <p className="text-xs font-medium truncate">
                              {message.attachmentName || t('chat.bubble.attachment')}
                            </p>
                            <p
                              className={`text-[11px] flex items-center gap-1 ${
                                isAgent
                                  ? 'text-white/70'
                                  : 'text-slate-400 dark:text-slate-500'
                              }`}
                            >
                              <Download className="w-3 h-3" />
                              {t('chat.bubble.clickToDownload')}
                            </p>
                          </div>
                        </a>
                      )}
                    </div>
                  )}
                </>
              )}
            </div>
          )}

          {/* 操作按钮区:仅非编辑模式且非撤回消息显示 */}
          {!isEditing && !message.isRecalled && (
            <>
              {/* 复制按钮:仅文本消息且 hover 时显示 */}
              {message.content && (
                <button
                  onClick={handleCopy}
                  className={`absolute top-1 ${
                    isAgent ? 'right-full mr-1' : 'left-full ml-1'
                  } p-1.5 rounded-lg bg-white dark:bg-slate-700 shadow-md border border-slate-200 dark:border-slate-600 opacity-0 group-hover:opacity-100 transition-opacity`}
                  title={t('chat.bubble.copyMessage')}
                  aria-label={t('chat.bubble.copyMessage')}
                >
                  {copied ? (
                    <Check className="w-3.5 h-3.5 text-emerald-500" />
                  ) : (
                    <Copy className="w-3.5 h-3.5 text-slate-500 dark:text-slate-400" />
                  )}
                </button>
              )}

              {/* 更多操作菜单:仅本人消息显示 */}
              {(recallable || editable) && (
                <div
                  ref={menuRef}
                  className={`absolute top-1 ${
                    isAgent ? 'right-full mr-1' : 'left-full ml-1'
                  } ${menuOpen ? '' : 'opacity-0 group-hover:opacity-100'} transition-opacity`}
                >
                  <button
                    onClick={() => setMenuOpen((v) => !v)}
                    disabled={actionPending || recalling}
                    className="p-1.5 rounded-lg bg-white dark:bg-slate-700 shadow-md border border-slate-200 dark:border-slate-600 hover:bg-slate-50 dark:hover:bg-slate-600 transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
                    title={t('chat.bubble.moreActions')}
                    aria-label={t('chat.bubble.moreActions')}
                    aria-expanded={menuOpen}
                  >
                    {recalling ? (
                      <span className="inline-block w-3.5 h-3.5 border-2 border-slate-300 border-t-blue-500 rounded-full animate-spin" />
                    ) : (
                      <MoreVertical className="w-3.5 h-3.5 text-slate-500 dark:text-slate-400" />
                    )}
                  </button>

                  {menuOpen && (
                    <div
                      className={`absolute top-full mt-1 ${
                        isAgent ? 'right-0' : 'left-0'
                      } z-10 min-w-[120px] bg-white dark:bg-slate-800 rounded-lg shadow-xl border border-slate-200 dark:border-slate-700 overflow-hidden`}
                    >
                      {editable && (
                        <button
                          onClick={handleEnterEdit}
                          disabled={actionPending}
                          className="w-full flex items-center gap-2 px-3 py-2 text-xs text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700/50 transition-colors disabled:opacity-40"
                        >
                          <Pencil className="w-3.5 h-3.5" />
                          {t('common.edit')}
                        </button>
                      )}
                      {recallable && (
                        <button
                          onClick={handleRecall}
                          disabled={actionPending || recalling}
                          className="w-full flex items-center gap-2 px-3 py-2 text-xs text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 transition-colors disabled:opacity-40"
                        >
                          <RotateCcw className="w-3.5 h-3.5" />
                          {t('chat.bubble.recall')}
                        </button>
                      )}
                    </div>
                  )}
                </div>
              )}
            </>
          )}
        </div>
      </div>

      {isAgent && (
        <div
          className={`w-9 h-9 rounded-xl flex items-center justify-center text-white text-sm font-semibold shadow-sm flex-shrink-0 ${
            SENDER_TYPE_AVATAR_COLORS[message.senderType] ?? SENDER_TYPE_AVATAR_COLORS.agent
          }`}
        >
          {message.senderName?.charAt(0) || '?'}
        </div>
      )}
    </div>
  );
}

const ChatMessageBubble = memo(ChatMessageBubbleImpl);
export default ChatMessageBubble;

// 导出供其他组件使用
export { isImageUrl, formatTime as formatMessageTime };
