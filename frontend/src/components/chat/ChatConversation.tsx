import { useState, useRef, useEffect, useMemo, useCallback } from 'react';
import type { ChatSessionDto, ChatMessageDto } from '../../api/types';
import { isAwaitingRatingStatus, isTerminalChatStatus } from '../../api/types';
import { useI18n } from '../../context/I18nContext';
import { toIntlLocale } from '../../i18n/locale';
import type { TranslationKey } from '../../locales/zh-Hans';
import ChatMessageBubble from './ChatMessageBubble';
import ChatComposer from './ChatComposer';
import { useSignalREvent } from '../../hooks/useSignalR';
import { SignalREvents } from '../../services/signalR';
import {
  ArrowLeft,
  Mail,
  User,
  FileText,
  X,
  Search,
  ChevronUp,
  ChevronDown,
  MessageCircle,
  Loader2,
  Clock,
} from 'lucide-react';

interface ChatConversationProps {
  session: ChatSessionDto;
  messages: ChatMessageDto[];
  loading: boolean;
  onBack: () => void;
  onSend: (
    content: string,
    attachment?: { url: string; name: string } | null,
  ) => Promise<void>;
  onTransfer: () => void;
  onClose: () => void;
  mailboxId?: number | null;
  /** 当前登录用户 ID,用于判断消息是否可撤回/编辑 */
  currentUserId?: number | null;
  /** 撤回消息回调 */
  onRecallMessage?: (sessionId: number, messageId: number) => Promise<void>;
  /** 编辑消息回调 */
  onEditMessage?: (sessionId: number, messageId: number, content: string) => Promise<void>;
}

const STATUS_AVATAR_COLORS: Record<string, string> = {
  waiting: 'bg-gradient-to-br from-amber-400 to-orange-500',
  active: 'bg-gradient-to-br from-emerald-500 to-teal-600',
  awaiting_rating: 'bg-gradient-to-br from-violet-500 to-fuchsia-600',
  closed: 'bg-gradient-to-br from-slate-400 to-slate-500',
  transferred: 'bg-gradient-to-br from-blue-500 to-indigo-600',
};

const STATUS_BADGE_COLORS: Record<string, string> = {
  waiting: 'bg-amber-50 dark:bg-amber-900/30 text-amber-600 dark:text-amber-400',
  active: 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-600 dark:text-emerald-400',
  awaiting_rating: 'bg-violet-50 dark:bg-violet-900/30 text-violet-600 dark:text-violet-400',
  closed: 'bg-slate-100 dark:bg-slate-700/50 text-slate-500 dark:text-slate-400',
  transferred: 'bg-blue-50 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400',
};

/**
 * 会话状态 code → 文案 key。
 *
 * 后端 `MappingExtensions.ToDto(ChatSession)`（`MappingExtensions.cs:600-607`）下发**中文**
 * `statusLabel`；这里改按稳定 code 走前端字典，取值恰为这 5 码，未知码回落渲染原始 code
 * （**不再**回落 `session.statusLabel`，否则英文界面会漏出后端中文）。
 */
const CHAT_STATUS_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  waiting: 'chat.status.waiting',
  active: 'chat.status.active',
  awaiting_rating: 'chat.status.awaitingRating',
  closed: 'chat.status.closed',
  transferred: 'chat.status.transferred',
};

/** 模块级纯函数用的最小翻译函数形态（与 I18nContext 的 `t` 兼容：多出的可选参数可忽略） */
type TranslateFn = (key: TranslationKey) => string;

/** 把"毫秒数"格式化为 MM:SS（用于评分等待倒计时） */
function formatCountdown(ms: number): string {
  if (!isFinite(ms) || ms <= 0) return '00:00';
  const totalSec = Math.floor(ms / 1000);
  const m = Math.floor(totalSec / 60).toString().padStart(2, '0');
  const s = (totalSec % 60).toString().padStart(2, '0');
  return `${m}:${s}`;
}

/** 获取日期分组标签（`t` 与当前语言由调用方注入；本文件是组件文件，语言只在渲染期可得） */
function getDateLabel(dateStr: string, t: TranslateFn, locale: string): string {
  try {
    const d = new Date(dateStr);
    if (isNaN(d.getTime())) return dateStr;
    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const target = new Date(d.getFullYear(), d.getMonth(), d.getDate());
    const diffDays = Math.floor((today.getTime() - target.getTime()) / (24 * 60 * 60 * 1000));
    if (diffDays === 0) return t('chat.conversation.dateToday');
    if (diffDays === 1) return t('chat.conversation.dateYesterday');
    if (diffDays === -1) return t('chat.conversation.dateTomorrow');
    return d.toLocaleDateString(toIntlLocale(locale), {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  } catch {
    return dateStr;
  }
}

/** 获取日期的分组 key(YYYY-MM-DD) */
function getDateKey(dateStr: string): string {
  try {
    const d = new Date(dateStr);
    if (isNaN(d.getTime())) return 'unknown';
    return `${d.getFullYear()}-${d.getMonth() + 1}-${d.getDate()}`;
  } catch {
    return 'unknown';
  }
}

interface MessageGroup {
  dateKey: string;
  dateLabel: string;
  messages: ChatMessageDto[];
}

/** 客户输入指示器 */
interface TypingIndicatorProps {
  show: boolean;
  senderName?: string;
}

function TypingIndicator({ show, senderName }: TypingIndicatorProps) {
  const { t } = useI18n();
  if (!show) return null;
  return (
    <div className="flex items-center gap-2 px-1 mb-2 text-xs text-slate-500 dark:text-slate-400">
      <div className="flex gap-1">
        <span className="w-1.5 h-1.5 bg-slate-400 dark:bg-slate-500 rounded-full animate-bounce" style={{ animationDelay: '0ms' }} />
        <span className="w-1.5 h-1.5 bg-slate-400 dark:bg-slate-500 rounded-full animate-bounce" style={{ animationDelay: '150ms' }} />
        <span className="w-1.5 h-1.5 bg-slate-400 dark:bg-slate-500 rounded-full animate-bounce" style={{ animationDelay: '300ms' }} />
      </div>
      <span>{t('chat.conversation.typing', { name: senderName || t('format.customerFallback') })}</span>
    </div>
  );
}

export default function ChatConversation({
  session,
  messages,
  loading,
  onBack,
  onSend,
  onTransfer,
  onClose,
  mailboxId,
  currentUserId,
  onRecallMessage,
  onEditMessage,
}: ChatConversationProps) {
  const { t, language } = useI18n();
  const statusLabelKey = CHAT_STATUS_LABEL_KEYS[session.status];
  const [searchOpen, setSearchOpen] = useState(false);
  const [searchKeyword, setSearchKeyword] = useState('');
  const [currentMatchIndex, setCurrentMatchIndex] = useState(0);
  // 当前正在编辑的消息 ID(同一时间只允许编辑一条)
  const [editingMessageId, setEditingMessageId] = useState<number | null>(null);
  // 撤回/编辑操作进行中(禁用其他操作按钮)
  const [actionPending, setActionPending] = useState(false);
  // 客户输入指示器
  const [customerTyping, setCustomerTyping] = useState(false);

  const messagesEndRef = useRef<HTMLDivElement>(null);
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const typingTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // 当前会话是否只读（输入/按钮禁用）
  // - 终止态：closed / transferred → 完全禁用
  // - awaiting_rating：客服已发起关闭，等待客户评分 → 客服侧禁用再发消息、转工单、再次点击"关闭"
  const isReadOnly = isTerminalChatStatus(session.status) || isAwaitingRatingStatus(session.status);
  const isAwaitingRating = isAwaitingRatingStatus(session.status);

  // ====== 等待评分倒计时（仅 awaiting_rating）：每秒刷新显示 MM:SS ======
  const [countdownRemainMs, setCountdownRemainMs] = useState<number | null>(null);
  useEffect(() => {
    if (!isAwaitingRating || !session.ratingTimeoutAt) {
      setCountdownRemainMs(null);
      return;
    }
    const target = new Date(session.ratingTimeoutAt).getTime();
    const compute = () => setCountdownRemainMs(Math.max(0, target - Date.now()));
    compute();
    const t = setInterval(compute, 1000);
    return () => clearInterval(t);
  }, [isAwaitingRating, session.ratingTimeoutAt]);

  // 按日期分组消息
  const messageGroups = useMemo<MessageGroup[]>(() => {
    const groups: MessageGroup[] = [];
    let lastKey = '';
    for (const msg of messages) {
      const key = getDateKey(msg.createdAt);
      if (key !== lastKey) {
        groups.push({
          dateKey: key,
          dateLabel: getDateLabel(msg.createdAt, t, language),
          messages: [msg],
        });
        lastKey = key;
      } else {
        groups[groups.length - 1].messages.push(msg);
      }
    }
    return groups;
  }, [messages, t, language]);

  // 搜索匹配的消息 ID 列表
  const matchedMessageIds = useMemo(() => {
    if (!searchKeyword.trim()) return [];
    const kw = searchKeyword.toLowerCase();
    return messages
      .filter(
        (m) =>
          m.content &&
          m.content.toLowerCase().includes(kw) &&
          m.senderType !== 'system',
      )
      .map((m) => m.id);
  }, [messages, searchKeyword]);

  // 重置匹配索引
  useEffect(() => {
    setCurrentMatchIndex(0);
  }, [searchKeyword]);

  // 新消息到达时滚动到底部
  useEffect(() => {
    if (messages.length > 0 && !searchKeyword) {
      messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
    }
  }, [messages.length, searchKeyword]);

  // 切换会话时重置搜索与编辑状态
  useEffect(() => {
    setSearchOpen(false);
    setSearchKeyword('');
    setCurrentMatchIndex(0);
    setEditingMessageId(null);
    setCustomerTyping(false);
  }, [session.id]);

  // 清理 typing 定时器
  useEffect(() => {
    return () => {
      if (typingTimeoutRef.current) {
        clearTimeout(typingTimeoutRef.current);
      }
    };
  }, []);

  // 监听 ChatTyping 事件:仅处理当前会话的客户输入
  useSignalREvent<unknown>(SignalREvents.ChatTyping, (data) => {
    if (!data || typeof data !== 'object') return;
    const payload = data as { sessionId?: number; senderType?: string; senderName?: string };
    if (payload.sessionId !== session.id) return;
    if (payload.senderType === 'customer') {
      setCustomerTyping(true);
      if (typingTimeoutRef.current) clearTimeout(typingTimeoutRef.current);
      // 3 秒无新输入事件则隐藏
      typingTimeoutRef.current = setTimeout(() => {
        setCustomerTyping(false);
      }, 3000);
    }
  });

  // 监听 ChatMessageRead 事件:更新客服消息的已读状态(本地)
  useSignalREvent<unknown>(SignalREvents.ChatMessageRead, (data) => {
    if (!data || typeof data !== 'object') return;
    const payload = data as { sessionId?: number; readerType?: string };
    if (payload.sessionId !== session.id) return;
    // 客户读了我发的消息,本地刷新(由父组件 messages 状态控制,这里仅触发隐式刷新)
    // 实际状态更新由父组件 fetchMessages 后传入新 messages 实现
  });

  // 监听 ChatMention 事件:当其他客服在消息中 @提及 当前用户时,显示提示
  useSignalREvent<unknown>(SignalREvents.ChatMention, (data) => {
    if (!data || typeof data !== 'object') return;
    const payload = data as {
      sessionId?: number;
      mentionedUserName?: string;
      senderName?: string;
      preview?: string;
    };
    // 仅当提及发生在当前会话时显示提示
    if (payload.sessionId !== session.id) return;
    // 这里仅做轻量提示,实际通知由全局 NotificationBell 组件处理
    // 避免在会话内重复弹 toast 干扰用户
  });

  const handleSearchNav = useCallback(
    (direction: 'up' | 'down') => {
      if (matchedMessageIds.length === 0) return;
      let next = currentMatchIndex;
      if (direction === 'up') {
        next = (currentMatchIndex - 1 + matchedMessageIds.length) % matchedMessageIds.length;
      } else {
        next = (currentMatchIndex + 1) % matchedMessageIds.length;
      }
      setCurrentMatchIndex(next);
      const targetId = matchedMessageIds[next];
      const el = document.getElementById(`chat-msg-${targetId}`);
      el?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    },
    [matchedMessageIds, currentMatchIndex],
  );

  const handleSearchInput = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setSearchKeyword(e.target.value);
    },
    [],
  );

  const handleClearSearch = useCallback(() => {
    setSearchKeyword('');
    setSearchOpen(false);
  }, []);

  // 进入编辑模式
  const handleEnterEdit = useCallback((messageId: number) => {
    setEditingMessageId(messageId);
  }, []);

  // 取消编辑
  const handleCancelEdit = useCallback(() => {
    setEditingMessageId(null);
  }, []);

  // 提交编辑
  const handleSubmitEdit = useCallback(
    async (messageId: number, content: string) => {
      if (!onEditMessage) return;
      try {
        setActionPending(true);
        await onEditMessage(session.id, messageId, content);
        setEditingMessageId(null);
      } finally {
        setActionPending(false);
      }
    },
    [onEditMessage, session.id],
  );

  // 撤回消息
  const handleRecall = useCallback(
    async (messageId: number) => {
      if (!onRecallMessage) return;
      try {
        setActionPending(true);
        await onRecallMessage(session.id, messageId);
      } finally {
        setActionPending(false);
      }
    },
    [onRecallMessage, session.id],
  );

  return (
    <section className="flex-1 flex flex-col min-w-0 bg-gradient-to-b from-slate-50/80 via-slate-50/40 to-white/30 dark:from-slate-900/50 dark:via-slate-900/30 dark:to-slate-900/20 border-l border-slate-200/70 dark:border-slate-700/60">
      {/* 会话头 */}
      <header className="flex items-center justify-between px-5 py-3.5 border-b border-slate-200/70 dark:border-slate-700/60 bg-white/90 dark:bg-slate-800/80 backdrop-blur-xl shadow-sm">
        <div className="flex items-center gap-3.5 min-w-0">
          <button
            onClick={onBack}
            className="lg:hidden p-2 hover:bg-slate-100 dark:hover:bg-slate-700 rounded-xl transition-colors flex-shrink-0"
            aria-label={t('chat.conversation.back')}
          >
            <ArrowLeft className="w-5 h-5 text-slate-600 dark:text-slate-300" />
          </button>
          <div
            className={`w-12 h-12 rounded-2xl flex items-center justify-center text-white text-base font-semibold shadow-lg flex-shrink-0 ${
              STATUS_AVATAR_COLORS[session.status] ?? STATUS_AVATAR_COLORS.closed
            }`}
          >
            {session.customerName?.charAt(0) || '?'}
          </div>
          <div className="min-w-0">
            <h2 className="text-[15px] font-bold text-slate-900 dark:text-slate-100 truncate">
              {session.customerName || t('chat.conversation.unknownCustomer')}
            </h2>
            <div className="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400 mt-0.5 flex-wrap">
              {session.customerEmail && (
                <span className="flex items-center gap-1 truncate max-w-[220px]">
                  <Mail className="w-3 h-3 flex-shrink-0" />
                  <span className="truncate">{session.customerEmail}</span>
                </span>
              )}
              {session.userName && (
                <span className="inline-flex items-center gap-1 flex-shrink-0 px-1.5 py-0.5 rounded-md bg-slate-100 dark:bg-slate-700/60 text-slate-600 dark:text-slate-300 text-[11px] font-medium">
                  <User className="w-3 h-3" />
                  {session.userName}
                </span>
              )}
            </div>
          </div>
          <span
            className={`hidden sm:inline-flex items-center gap-1 px-2.5 py-1 rounded-full text-[11px] font-bold flex-shrink-0 shadow-sm ${
              STATUS_BADGE_COLORS[session.status] ?? STATUS_BADGE_COLORS.closed
            }`}
          >
            <span className={`w-1.5 h-1.5 rounded-full ${
              session.status === 'waiting'
                ? 'bg-amber-500 animate-pulse'
                : session.status === 'active'
                ? 'bg-emerald-500'
                : session.status === 'awaiting_rating'
                ? 'bg-violet-500 animate-pulse'
                : 'bg-slate-400'
            }`} />
            {statusLabelKey ? t(statusLabelKey) : session.status}
          </span>

          {/* awaiting_rating：额外显示倒计时徽章（评分等待） */}
          {isAwaitingRating && countdownRemainMs !== null && (
            <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-full text-[11px] font-bold bg-gradient-to-r from-violet-500/10 to-fuchsia-500/10 dark:from-violet-500/20 dark:to-fuchsia-500/20 text-violet-700 dark:text-violet-300 flex-shrink-0 border border-violet-200/60 dark:border-violet-800/30 shadow-sm animate-in fade-in slide-in-from-right-2 duration-300">
              <Clock className="w-3 h-3 flex-shrink-0" />
              <span>{t('chat.conversation.awaitingRating')}</span>
              <span className="tabular-nums font-mono">{formatCountdown(countdownRemainMs)}</span>
            </span>
          )}
        </div>

        <div className="flex items-center gap-1 flex-shrink-0">
          <button
            onClick={() => setSearchOpen((v) => !v)}
            className={`p-2.5 rounded-xl transition-all ${
              searchOpen
                ? 'text-blue-600 dark:text-blue-400 bg-gradient-to-br from-blue-50 to-indigo-50 dark:from-blue-900/40 dark:to-indigo-900/30 shadow-md'
                : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-700 hover:scale-105'
            }`}
            title={t('chat.conversation.searchMessages')}
            aria-label={t('chat.conversation.searchMessages')}
          >
            <Search className="w-4 h-4" />
          </button>
          <button
            onClick={onTransfer}
            disabled={isReadOnly}
            title={isAwaitingRating ? t('chat.conversation.transferDisabledAwaitingRating') : undefined}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold text-blue-600 dark:text-blue-300 bg-gradient-to-br from-blue-50 to-indigo-50 dark:from-blue-900/40 dark:to-indigo-900/30 rounded-xl hover:from-blue-100 hover:to-indigo-100 dark:hover:from-blue-900/50 dark:hover:to-indigo-900/40 disabled:opacity-40 disabled:cursor-not-allowed transition-all hover:shadow-md hover:-translate-y-0.5 shadow-sm border border-blue-200/60 dark:border-blue-800/30"
          >
            <FileText className="w-4 h-4" />
            <span className="hidden sm:inline">{t('chat.conversation.transfer')}</span>
          </button>
          {isAwaitingRating ? (
            <span className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold text-violet-700 dark:text-violet-300 bg-gradient-to-br from-violet-50 to-fuchsia-50 dark:from-violet-900/40 dark:to-fuchsia-900/30 rounded-xl shadow-sm border border-violet-200/60 dark:border-violet-800/30 opacity-90">
              <Clock className="w-4 h-4 animate-pulse" />
              <span className="hidden sm:inline">{t('chat.conversation.awaitingRatingTimeout')}</span>
              <span className="sm:hidden">{t('chat.status.awaitingRating')}</span>
            </span>
          ) : (
            <button
              onClick={onClose}
              disabled={isReadOnly}
              className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold text-red-600 dark:text-red-300 bg-gradient-to-br from-red-50 to-rose-50 dark:from-red-900/30 dark:to-rose-900/30 rounded-xl hover:from-red-100 hover:to-rose-100 dark:hover:from-red-900/40 dark:hover:to-rose-900/40 disabled:opacity-40 disabled:cursor-not-allowed transition-all hover:shadow-md hover:-translate-y-0.5 shadow-sm border border-red-200/60 dark:border-red-800/30"
            >
              <X className="w-4 h-4" />
              <span className="hidden sm:inline">{t('common.close')}</span>
            </button>
          )}
        </div>
      </header>

      {/* 搜索条 */}
      {searchOpen && (
        <div className="flex items-center gap-2 px-5 py-3 border-b border-slate-200/70 dark:border-slate-700/60 bg-gradient-to-r from-white to-slate-50/60 dark:from-slate-800/80 dark:to-slate-900/40">
          <div className="relative flex-1">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
            <input
              type="text"
              value={searchKeyword}
              onChange={handleSearchInput}
              placeholder={t('chat.conversation.searchInConversation')}
              autoFocus
              className="w-full pl-9 pr-3 py-2 text-sm border border-slate-200 dark:border-slate-700 rounded-xl bg-white dark:bg-slate-900/50 text-slate-900 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-blue-500/40 focus:border-blue-500 shadow-sm transition-all"
            />
          </div>
          {searchKeyword && (
            <span className="text-xs text-slate-500 dark:text-slate-400 whitespace-nowrap px-2 py-1 rounded-lg bg-slate-100 dark:bg-slate-700/50 font-semibold">
              {matchedMessageIds.length > 0
                ? `${currentMatchIndex + 1} / ${matchedMessageIds.length}`
                : t('chat.conversation.noMatches')}
            </span>
          )}
          <button
            onClick={() => handleSearchNav('up')}
            disabled={matchedMessageIds.length === 0}
            className="p-2 rounded-xl text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition-all hover:scale-105"
            aria-label={t('chat.conversation.prevMatch')}
          >
            <ChevronUp className="w-4 h-4" />
          </button>
          <button
            onClick={() => handleSearchNav('down')}
            disabled={matchedMessageIds.length === 0}
            className="p-2 rounded-xl text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition-all hover:scale-105"
            aria-label={t('chat.conversation.nextMatch')}
          >
            <ChevronDown className="w-4 h-4" />
          </button>
          <button
            onClick={handleClearSearch}
            className="p-2 rounded-xl text-slate-500 dark:text-slate-400 hover:bg-red-50 hover:text-red-500 dark:hover:bg-red-900/30 dark:hover:text-red-400 transition-all hover:scale-105"
            aria-label={t('chat.conversation.closeSearch')}
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {/* 消息流（含 content-visibility 长列表优化样式） */}
      {/*
        长消息列表性能优化：
        - 每条消息 / 日期分隔包裹层添加 content-visibility: auto：
          浏览器会跳过视口外（约 50% 视口高度 margin）内容的渲染（layout+paint），
          对 500+ 条长会话可显著降低首屏布局开销与滚动 jank。
        - contain-intrinsic-size 给浏览器一个"占位高度"估算，
          避免滚动条在用户滚动过程中剧烈跳动。
        - 这是零依赖、不破坏组件结构的浏览器原生优化，
          相比 react-window 无需预估每条消息高度，
          对含图片 / 附件 / 多行文本等动态高度的气泡更友好。
      */}
      <style>{`
        .chat-msg-wrap {
          content-visibility: auto;
          /* placeholder height: ~60px of text + meta row + gap ≈ 100px */
          contain-intrinsic-size: auto 100px;
        }
        .chat-date-sep {
          content-visibility: auto;
          contain-intrinsic-size: auto 40px;
        }
        /* fallback for browsers without content-visibility (Firefox < 124, Safari < 18.4) */
        @supports not (content-visibility: auto) {
          .chat-msg-wrap, .chat-date-sep {
            content-visibility: visible;
            contain: layout style;
          }
        }
      `}</style>
      <div
        ref={scrollContainerRef}
        className="flex-1 overflow-y-auto px-4 sm:px-6 py-5 space-y-4 scroll-smooth"
      >
        {loading && messages.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-full text-slate-400 gap-2">
            <div className="relative">
              <Loader2 className="w-10 h-10 animate-spin text-blue-500" />
              <div className="absolute inset-0 rounded-full border-2 border-blue-200/50 dark:border-blue-800/30 animate-ping" />
            </div>
            <p className="text-sm font-medium mt-2">{t('chat.conversation.loadingMessages')}</p>
          </div>
        ) : messages.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-full text-slate-400 gap-1">
            <div className="relative">
              <div className="w-20 h-20 rounded-3xl bg-gradient-to-br from-white to-slate-100 dark:from-slate-800 dark:to-slate-700/50 flex items-center justify-center mb-3 shadow-lg border border-slate-200/60 dark:border-slate-600/40">
                <MessageCircle className="w-10 h-10 opacity-70 text-blue-500/70 dark:text-blue-400/70" />
              </div>
              <div className="absolute -bottom-1 -right-1 w-6 h-6 rounded-full bg-gradient-to-br from-emerald-400 to-teal-500 border-2 border-white dark:border-slate-900/30 shadow-md flex items-center justify-center">
                <span className="w-2.5 h-2.5 rounded-full bg-white animate-pulse" />
              </div>
            </div>
            <p className="text-base font-bold text-slate-700 dark:text-slate-200 mt-3">{t('chat.conversation.startNewTitle')}</p>
            <p className="text-sm mt-1">{t('chat.conversation.startNewHint')}</p>
          </div>
        ) : (
          messageGroups.map((group) => (
            <div key={group.dateKey} className="space-y-4">
              {/* 日期分隔符（content-visibility 优化） */}
              <div className="chat-date-sep flex items-center justify-center sticky top-1 z-10 py-2">
                <div className="flex items-center gap-3 w-full">
                  <div className="flex-1 h-px bg-gradient-to-r from-transparent to-slate-200 dark:to-slate-700/60" />
                  <span className="px-3.5 py-1 text-[11px] font-bold text-slate-500 dark:text-slate-400 bg-white/90 dark:bg-slate-800/90 backdrop-blur-md rounded-full shadow-sm border border-slate-200/60 dark:border-slate-700/40 whitespace-nowrap">
                    {group.dateLabel}
                  </span>
                  <div className="flex-1 h-px bg-gradient-to-l from-transparent to-slate-200 dark:to-slate-700/60" />
                </div>
              </div>
              {/* 消息列表（content-visibility 优化） */}
              {group.messages.map((msg) => (
                <div key={msg.id} id={`chat-msg-${msg.id}`} className="chat-msg-wrap">
                  <ChatMessageBubble
                    message={msg}
                    highlight={searchKeyword || undefined}
                    currentUserId={currentUserId}
                    isEditing={editingMessageId === msg.id}
                    onEnterEdit={onEditMessage ? handleEnterEdit : undefined}
                    onCancelEdit={handleCancelEdit}
                    onSubmitEdit={onEditMessage ? handleSubmitEdit : undefined}
                    onRecall={onRecallMessage ? handleRecall : undefined}
                    actionPending={actionPending}
                  />
                </div>
              ))}
            </div>
          ))
        )}
        {/* 客户输入指示器 */}
        <TypingIndicator show={customerTyping} senderName={session.customerName || t('format.customerFallback')} />
        <div ref={messagesEndRef} />
      </div>

      {/* 输入区 */}
      <ChatComposer
        disabled={isReadOnly}
        onSend={onSend}
        mailboxId={mailboxId ?? session.mailboxId}
        sessionId={session.id}
      />
    </section>
  );
}
