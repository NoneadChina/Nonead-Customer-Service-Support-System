import { memo, useCallback } from 'react';
import type { ChatSessionDto } from '../../api/types';
import { isAwaitingRatingStatus } from '../../api/types';
import { useI18n } from '../../context/I18nContext';
import { relativeTime } from '../../utils/format';
import type { TranslationKey } from '../../locales/zh-Hans';
import {
  Search,
  Phone,
  X,
  MessageCircle,
  ChevronLeft,
  ChevronRight,
  Inbox,
  Clock,
  Trash2,
  Archive,
  CheckSquare,
  Square,
} from 'lucide-react';

interface MailboxOption {
  id: number;
  name: string;
}

interface ChatSessionListProps {
  sessions: ChatSessionDto[];
  loading: boolean;
  activeSessionId: number | null;
  searchKeyword: string;
  statusFilter: string | null;
  mailboxes: MailboxOption[];
  mailboxFilter: number | null;
  page: number;
  totalPages: number;
  totalItems: number;
  pageSize: number;
  onSearchChange: (v: string) => void;
  onStatusChange: (v: string | null) => void;
  onMailboxChange: (v: number | null) => void;
  onSelectSession: (s: ChatSessionDto) => void;
  onAssignSession: (id: number) => void;
  onPageChange: (p: number) => void;
  // 删除/多选/垃圾箱（仅 canManage=true 时启用）
  canManage: boolean;
  selectedIds: Set<number>;
  onSelectMode: boolean;
  onToggleSelect: (id: number) => void;
  onToggleSelectMode: () => void;
  onSelectAll: () => void;
  onDeleteSession: (id: number) => void;
  onBulkDelete: () => void;
  onOpenTrash: () => void;
  trashCount: number;
}

/**
 * 状态筛选 Tab。
 *
 * `labelKey` 指向 `chat.list.filter.*`（**不是** `chat.status.*`）：Tab 用的是短标签，
 * 其中"等待 / 待评分"与完整状态文案"等待接待 / 等待评分"不同，直接复用会改掉中文界面；
 * 且英文状态文案 "Converted to conversation" 在窄 Tab 里会溢出。
 */
const STATUS_TABS: { key: string | null; labelKey: TranslationKey; dot: string }[] = [
  { key: null, labelKey: 'chat.list.filter.all', dot: 'bg-slate-400' },
  { key: 'waiting', labelKey: 'chat.list.filter.waiting', dot: 'bg-amber-500' },
  { key: 'active', labelKey: 'chat.list.filter.active', dot: 'bg-emerald-500' },
  { key: 'awaiting_rating', labelKey: 'chat.list.filter.awaitingRating', dot: 'bg-violet-500' },
  { key: 'closed', labelKey: 'chat.list.filter.closed', dot: 'bg-slate-500' },
  { key: 'transferred', labelKey: 'chat.list.filter.transferred', dot: 'bg-blue-500' },
];

const STATUS_AVATAR_COLORS: Record<string, string> = {
  waiting: 'bg-gradient-to-br from-amber-400 to-orange-500',
  active: 'bg-gradient-to-br from-emerald-500 to-teal-600',
  awaiting_rating: 'bg-gradient-to-br from-violet-500 to-fuchsia-600',
  closed: 'bg-gradient-to-br from-slate-400 to-slate-500',
  transferred: 'bg-gradient-to-br from-blue-500 to-indigo-600',
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

/** 会话列表项骨架屏 */
function SessionItemSkeleton() {
  return (
    <div className="px-3 py-3 animate-pulse">
      <div className="flex items-center gap-3">
        <div className="w-11 h-11 rounded-2xl bg-slate-200 dark:bg-slate-700" />
        <div className="flex-1 min-w-0 space-y-2">
          <div className="h-3.5 w-2/3 bg-slate-200 dark:bg-slate-700 rounded" />
          <div className="h-3 w-1/2 bg-slate-200 dark:bg-slate-700 rounded" />
        </div>
      </div>
    </div>
  );
}

function ChatSessionListImpl({
  sessions,
  loading,
  activeSessionId,
  searchKeyword,
  statusFilter,
  mailboxes,
  mailboxFilter,
  page,
  totalPages,
  totalItems,
  pageSize,
  onSearchChange,
  onStatusChange,
  onMailboxChange,
  onSelectSession,
  onAssignSession,
  onPageChange,
  canManage,
  selectedIds,
  onSelectMode,
  onToggleSelect,
  onToggleSelectMode,
  onSelectAll,
  onDeleteSession,
  onBulkDelete,
  onOpenTrash,
  trashCount,
}: ChatSessionListProps) {
  const { t } = useI18n();
  const handleSearchInput = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      onSearchChange(e.target.value);
    },
    [onSearchChange],
  );

  const handleStatusTabClick = useCallback(
    (key: string | null) => {
      onStatusChange(key);
    },
    [onStatusChange],
  );

  const handleMailboxChange = useCallback(
    (e: React.ChangeEvent<HTMLSelectElement>) => {
      onMailboxChange(e.target.value ? parseInt(e.target.value) : null);
    },
    [onMailboxChange],
  );

  const allSelected = sessions.length > 0 && selectedIds.size === sessions.length;

  return (
    <aside className="w-full h-full flex flex-col bg-gradient-to-b from-white via-slate-50/70 to-slate-100/60 dark:from-slate-800 dark:via-slate-900/70 dark:to-slate-900 border-r border-slate-200/80 dark:border-slate-700/70 shadow-[inset_-1px_0_0_rgba(15,23,42,0.04),0_0_0_1px_rgba(15,23,42,0.02)] min-w-0 overflow-x-hidden">
      {/* ====== 搜索栏 ====== */}
      <div className="p-3 border-b border-slate-100/80 dark:border-slate-700/50 space-y-2.5 bg-gradient-to-b from-white/90 to-slate-50/70 dark:from-slate-800/80 dark:to-slate-800/50 backdrop-blur-md">
        <div className="flex items-center justify-between px-1 mb-0.5">
          <div className="flex items-center gap-2 min-w-0">
            <div className="w-7 h-7 rounded-lg bg-gradient-to-br from-indigo-500 to-violet-600 flex items-center justify-center text-white shadow-sm shadow-indigo-500/30 flex-shrink-0">
              <MessageCircle className="w-3.5 h-3.5" strokeWidth={2.2} />
            </div>
            <div className="min-w-0">
              <h2 className="text-sm font-bold text-slate-900 dark:text-white leading-tight tracking-tight truncate">{t('chat.list.title')}</h2>
              <p className="text-[10px] text-slate-500 dark:text-slate-400 leading-tight truncate">{t('chat.list.subtitle')}</p>
            </div>
          </div>
          {canManage && (
            <div className="flex items-center gap-1 flex-shrink-0">
              <button
                type="button"
                onClick={onToggleSelectMode}
                className={`relative inline-flex items-center justify-center w-7 h-7 rounded-lg transition-all flex-shrink-0 ${
                  onSelectMode
                    ? 'bg-indigo-500 text-white shadow-sm shadow-indigo-500/30'
                    : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-700/80 hover:text-slate-700 dark:hover:text-slate-200'
                }`}
                title={onSelectMode ? t('chat.list.exitSelectMode') : t('chat.list.selectMode')}
                aria-label={onSelectMode ? t('chat.list.exitSelectMode') : t('chat.list.selectMode')}
                aria-pressed={onSelectMode}
              >
                {onSelectMode ? (
                  <CheckSquare className="w-3.5 h-3.5" strokeWidth={2.2} />
                ) : (
                  <Square className="w-3.5 h-3.5" strokeWidth={2.2} />
                )}
              </button>
              <button
                type="button"
                onClick={onOpenTrash}
                className="relative inline-flex items-center justify-center w-7 h-7 rounded-lg text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-700/80 hover:text-slate-700 dark:hover:text-slate-200 transition-all flex-shrink-0"
                title={t('chat.list.trash')}
                aria-label={t('chat.list.trash')}
              >
                <Archive className="w-3.5 h-3.5" strokeWidth={2.2} />
                {trashCount > 0 && (
                  <span className="absolute -top-1 -right-1 inline-flex items-center justify-center min-w-[16px] h-4 px-1 rounded-full bg-red-500 text-white text-[9px] font-bold leading-none ring-2 ring-white dark:ring-slate-800 tabular-nums">
                    {trashCount > 99 ? '99+' : trashCount}
                  </span>
                )}
              </button>
            </div>
          )}
        </div>

        {/* 搜索框 */}
        <div className="group relative min-w-0">
          <div className="absolute left-2.5 top-1/2 -translate-y-1/2 w-6 h-6 rounded-md bg-gradient-to-br from-slate-100 to-slate-50 dark:from-slate-700/80 dark:to-slate-800 flex items-center justify-center text-slate-400 group-focus-within:from-blue-50 group-focus-within:to-indigo-50 group-focus-within:text-blue-500 dark:group-focus-within:from-blue-900/40 dark:group-focus-within:to-indigo-900/40 dark:group-focus-within:text-blue-300 transition-all flex-shrink-0">
            <Search className="w-3.5 h-3.5" />
          </div>
          <input
            type="text"
            value={searchKeyword}
            onChange={handleSearchInput}
            placeholder={t('chat.list.searchPlaceholder')}
            className="w-full pl-10 pr-3 py-2 text-sm border border-slate-200 dark:border-slate-700 rounded-xl bg-white dark:bg-slate-900/60 text-slate-900 dark:text-slate-100 placeholder:text-slate-400 dark:placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/25 focus:border-indigo-400 dark:focus:border-indigo-500 transition-all shadow-sm focus:shadow-md min-w-0 truncate"
          />
          {searchKeyword && (
            <button
              type="button"
              onClick={() => onSearchChange('')}
              className="absolute right-2 top-1/2 -translate-y-1/2 p-1 rounded-lg hover:bg-slate-100 dark:hover:bg-slate-700/80 text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 transition-colors flex-shrink-0"
              aria-label={t('chat.list.clearSearch')}
            >
              <X className="w-3.5 h-3.5" />
            </button>
          )}
        </div>

        {/* 邮箱下拉 */}
        <div className="relative min-w-0">
          <select
            value={mailboxFilter ?? ''}
            onChange={handleMailboxChange}
            className="w-full appearance-none px-3 py-1.5 pr-9 text-sm border border-slate-200 dark:border-slate-700 rounded-xl bg-white dark:bg-slate-800/80 text-slate-800 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-indigo-500/25 focus:border-indigo-400 dark:focus:border-indigo-500 transition-all shadow-sm min-w-0 truncate"
          >
            <option value="">📭 {t('chat.list.allMailboxes')}</option>
            {mailboxes.map((m) => (
              <option key={m.id} value={m.id}>
                {m.name}
              </option>
            ))}
          </select>
          <svg
            className="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-slate-400 flex-shrink-0"
            fill="none"
            viewBox="0 0 20 20"
            stroke="currentColor"
            strokeWidth={2.2}
          >
            <path strokeLinecap="round" strokeLinejoin="round" d="M6 8l4 4 4-4" />
          </svg>
        </div>

        {/* 多选模式工具栏：全选 + 已选数量 + 批量删除 */}
        {canManage && onSelectMode && (
          <div className="flex items-center justify-between gap-2 px-1 pt-1 min-w-0">
            <button
              type="button"
              onClick={onSelectAll}
              className="inline-flex items-center gap-1.5 px-2.5 py-1.5 text-xs font-semibold text-slate-700 dark:text-slate-200 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-700/70 transition-colors flex-shrink-0"
            >
              {allSelected ? (
                <CheckSquare className="w-3.5 h-3.5 text-indigo-500" strokeWidth={2.2} />
              ) : (
                <Square className="w-3.5 h-3.5 text-slate-400" strokeWidth={2.2} />
              )}
              {allSelected ? t('chat.list.deselectAll') : t('chat.list.selectAll')}
            </button>
            <span className="text-[11px] text-slate-500 dark:text-slate-400 font-medium tabular-nums flex-shrink-0 truncate">
              {t('chat.list.selectedCount', { selected: selectedIds.size, total: sessions.length })}
            </span>
            <button
              type="button"
              onClick={onBulkDelete}
              disabled={selectedIds.size === 0}
              className="inline-flex items-center gap-1 px-2.5 py-1.5 text-xs font-bold text-white bg-gradient-to-r from-red-500 to-rose-500 hover:from-red-600 hover:to-rose-600 rounded-lg shadow-sm transition-all disabled:opacity-40 disabled:cursor-not-allowed flex-shrink-0"
            >
              <Trash2 className="w-3.5 h-3.5" strokeWidth={2.2} />
              {t('chat.list.bulkDelete')}
            </button>
          </div>
        )}
      </div>

      {/* ====== 状态 Tab（两行排列） ====== */}
      <div
        className="px-1.5 py-2 border-b border-slate-100/90 dark:border-slate-700/60 bg-gradient-to-b from-white/80 to-slate-50/70 dark:from-slate-800/70 dark:to-slate-800/40 min-w-0 space-y-1"
      >
        {[STATUS_TABS.slice(0, 3), STATUS_TABS.slice(3)].map((row, rowIdx) => (
          <div key={rowIdx} className="flex items-center gap-1 min-w-0">
            {row.map((tab) => {
              const active = statusFilter === tab.key;
              return (
                <button
                  key={tab.labelKey}
                  onClick={() => handleStatusTabClick(tab.key)}
                  className={`group relative inline-flex items-center gap-1 flex-1 justify-center px-1 py-1.5 text-[11px] font-semibold rounded-lg whitespace-nowrap transition-all duration-200 ${
                    active
                      ? 'bg-gradient-to-r from-indigo-500 via-blue-500 to-violet-500 text-white shadow-[0_4px_12px_-3px_rgba(79,70,229,0.45)] ring-1 ring-white/40 dark:ring-white/10'
                      : 'text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700/60 hover:text-slate-900 dark:hover:text-white border border-transparent hover:border-slate-200/80 dark:hover-border-slate-600/60'
                  }`}
                >
                  <span
                    className={`relative w-1.5 h-1.5 rounded-full ${
                      active ? 'bg-white/90 shadow-[0_0_0_3px_rgba(255,255,255,0.18)]' : tab.dot
                    }`}
                  />
                  <span className="leading-none">{t(tab.labelKey)}</span>
                </button>
              );
            })}
          </div>
        ))}
      </div>

      {/* ====== 会话列表（纵向滚动、横向绝不溢出） ====== */}
      <div
        className="flex-1 min-w-0 overflow-x-hidden"
        style={{
          overflowY: 'auto',
          scrollbarWidth: 'thin',
          scrollbarColor: 'rgba(148,163,184,0.35) transparent',
        }}
      >
        <style>{`
          .chat-session-list-scroll::-webkit-scrollbar { width: 6px; height: 0; }
          .chat-session-list-scroll::-webkit-scrollbar-track { background: transparent; }
          .chat-session-list-scroll::-webkit-scrollbar-thumb {
            background: rgba(148,163,184,0.35);
            border-radius: 999px;
            border: 2px solid transparent;
            background-clip: padding-box;
          }
          .chat-session-list-scroll::-webkit-scrollbar-thumb:hover {
            background: rgba(100,116,139,0.5);
            background-clip: padding-box;
            border: 2px solid transparent;
          }
        `}</style>

        <div className="chat-session-list-scroll h-full min-w-0">
          {/* 加载中骨架屏 */}
          {loading && sessions.length === 0 ? (
            <div className="p-2">
              {Array.from({ length: 6 }).map((_, i) => (
                <SessionItemSkeleton key={i} />
              ))}
            </div>
          ) : sessions.length === 0 ? (
            /* 空状态 */
            <div className="flex flex-col items-center justify-center h-full text-slate-400 py-16 px-4">
              <div className="relative w-16 h-16 rounded-2xl bg-gradient-to-br from-slate-100 via-slate-50 to-white dark:from-slate-700/60 dark:via-slate-800/60 dark:to-slate-900/60 flex items-center justify-center mb-4 shadow-inner border border-slate-200/60 dark:border-slate-600/60">
                <div className="absolute inset-0 rounded-2xl bg-gradient-to-br from-indigo-500/5 to-violet-500/10 dark:from-indigo-400/10 dark:to-violet-400/10" />
                <Inbox className="w-7 h-7 text-slate-500 dark:text-slate-300 relative z-10" />
              </div>
              <p className="text-sm font-bold text-slate-600 dark:text-slate-200">{t('chat.list.emptyTitle')}</p>
              <p className="text-[11px] mt-1.5 text-center text-slate-400 dark:text-slate-500 max-w-[220px] leading-relaxed break-words">
                {searchKeyword || statusFilter || mailboxFilter
                  ? t('chat.list.emptyFiltered')
                  : t('chat.list.emptyHint')}
              </p>
            </div>
          ) : (
            /* 会话卡片列表 */
            <div className="py-1.5 px-1.5 space-y-1.5 min-w-0">
              {sessions.map((session) => {
                const isActive = !onSelectMode && activeSessionId === session.id;
                const statusLabelKey = CHAT_STATUS_LABEL_KEYS[session.status];
                const avatarColor =
                  STATUS_AVATAR_COLORS[session.status] ?? STATUS_AVATAR_COLORS.closed;
                const hasUnread = (session.agentUnreadCount ?? 0) > 0;
                const isSelected = canManage && onSelectMode && selectedIds.has(session.id);
                const cardClickHandler = canManage && onSelectMode
                  ? () => onToggleSelect(session.id)
                  : () => onSelectSession(session);
                return (
                  <div
                    key={session.id}
                    role="button"
                    tabIndex={0}
                    onClick={cardClickHandler}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        cardClickHandler();
                      }
                    }}
                    className={`w-full text-left rounded-2xl p-3 transition-all duration-200 border-2 relative overflow-hidden group min-w-0 cursor-pointer focus:outline-none focus:ring-2 focus:ring-indigo-500/30 ${
                      isSelected
                        ? 'bg-gradient-to-br from-indigo-50 via-blue-50 to-violet-50/60 dark:from-indigo-900/30 dark:via-blue-900/25 dark:to-violet-900/20 border-indigo-400/70 dark:border-indigo-500/70 shadow-[0_6px_20px_-8px_rgba(79,70,229,0.45)]'
                        : isActive
                          ? 'bg-gradient-to-br from-indigo-50 via-blue-50 to-violet-50/60 dark:from-indigo-900/30 dark:via-blue-900/25 dark:to-violet-900/20 border-indigo-400/70 dark:border-indigo-500/70 shadow-[0_6px_20px_-8px_rgba(79,70,229,0.45)] -translate-y-0.5'
                          : 'bg-white/80 dark:bg-slate-800/60 border-transparent hover:bg-white dark:hover:bg-slate-700/70 hover:border-slate-200 dark:hover:border-slate-600/80 hover:shadow-[0_4px_14px_-6px_rgba(15,23,42,0.12)] hover:-translate-y-0.5'
                    }`}
                  >
                    {/* 选中时左侧彩条 */}
                    {(isActive || isSelected) && (
                      <div className="absolute left-0 top-3 bottom-3 w-1 rounded-r-full bg-gradient-to-b from-indigo-500 via-blue-500 to-violet-500 shadow-[0_0_8px_rgba(79,70,229,0.5)]" />
                    )}
                    {/* 非多选模式 + canManage：右上角 hover 显示删除按钮 */}
                    {canManage && !onSelectMode && (
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation();
                          onDeleteSession(session.id);
                        }}
                        className="absolute top-1.5 right-1.5 z-10 inline-flex items-center justify-center w-7 h-7 rounded-lg bg-white/90 dark:bg-slate-700/90 text-slate-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30 border border-slate-200/70 dark:border-slate-600/60 opacity-0 group-hover:opacity-100 focus:opacity-100 transition-all shadow-sm"
                        title={t('chat.list.moveToTrash')}
                        aria-label={t('chat.list.moveToTrash')}
                      >
                        <Trash2 className="w-3.5 h-3.5" strokeWidth={2.2} />
                      </button>
                    )}
                    <div className="flex items-start gap-3 relative">
                      {/* 多选模式下：左侧显示勾选框 */}
                      {canManage && onSelectMode && (
                        <div className="flex-shrink-0 flex items-center" style={{ height: '44px' }}>
                          {isSelected ? (
                            <CheckSquare className="w-5 h-5 text-indigo-500" strokeWidth={2.2} />
                          ) : (
                            <Square className="w-5 h-5 text-slate-300 dark:text-slate-600" strokeWidth={2.2} />
                          )}
                        </div>
                      )}
                      {/* 头像 */}
                      <div className="relative flex-shrink-0">
                        <div
                          className={`w-11 h-11 rounded-2xl flex items-center justify-center text-white text-sm font-bold shadow-md ring-2 ring-white/60 dark:ring-slate-800/60 ${avatarColor}`}
                        >
                          {session.customerName?.charAt(0) || '?'}
                        </div>
                        {session.status === 'active' && (
                          <span className="absolute -bottom-0.5 -right-0.5 w-4 h-4 rounded-full bg-emerald-500 border-[3px] border-white dark:border-slate-800 shadow-[0_0_0_2px_rgba(16,185,129,0.18)]" />
                        )}
                        {session.status === 'waiting' && (
                          <span className="absolute -bottom-0.5 -right-0.5 w-4 h-4 rounded-full bg-amber-500 border-[3px] border-white dark:border-slate-800 animate-pulse shadow-[0_0_0_2px_rgba(245,158,11,0.2)]" />
                        )}
                        {isAwaitingRatingStatus(session.status) && (
                          <span className="absolute -bottom-0.5 -right-0.5 inline-flex items-center justify-center w-4.5 h-4.5 rounded-full bg-gradient-to-br from-violet-500 to-fuchsia-500 border-[3px] border-white dark:border-slate-800 shadow-[0_0_0_2px_rgba(139,92,246,0.2)]"
                            style={{ width: '18px', height: '18px' }}
                            title={t('chat.list.awaitingRatingHint')}
                          >
                            <Clock className="w-[10px] h-[10px] text-white" strokeWidth={3} />
                          </span>
                        )}
                        {hasUnread && (
                          <span className="absolute -top-0.5 -right-0.5 w-3.5 h-3.5 rounded-full bg-red-500 border-[3px] border-white dark:border-slate-800 shadow-[0_0_0_2px_rgba(239,68,68,0.2)]" />
                        )}
                      </div>

                      <div className="flex-1 min-w-0">
                        {/* 第一行：客户名 + 时间 */}
                        <div className="flex items-center justify-between gap-2 min-w-0">
                          <p
                            className={`text-sm font-semibold truncate ${
                              isActive || isSelected
                                ? 'text-indigo-700 dark:text-indigo-300'
                                : hasUnread
                                  ? 'text-slate-900 dark:text-white font-bold'
                                  : 'text-slate-800 dark:text-slate-100'
                            }`}
                          >
                            {session.customerName || t('chat.list.unknownCustomer')}
                          </p>
                          <span
                            className={`text-[11px] flex-shrink-0 tabular-nums ${
                              hasUnread
                                ? 'text-red-500 dark:text-red-400 font-semibold'
                                : 'text-slate-400 dark:text-slate-500'
                            }`}
                          >
                            {relativeTime(session.updatedAt || session.createdAt)}
                          </span>
                        </div>

                        {/* 第二行：邮箱/主题 + 未读数量 */}
                        <div className="flex items-center justify-between gap-2 mt-0.5 min-w-0">
                          <p
                            className={`text-xs truncate ${
                              hasUnread
                                ? 'text-slate-700 dark:text-slate-200 font-medium'
                                : 'text-slate-500 dark:text-slate-400'
                            }`}
                          >
                            {session.customerEmail || session.subject || '—'}
                          </p>
                          {session.agentUnreadCount > 0 && (
                            <span className="flex-shrink-0 inline-flex items-center justify-center min-w-[20px] h-[20px] px-1.5 bg-gradient-to-br from-red-500 via-rose-500 to-pink-600 text-white text-[10px] font-bold rounded-full shadow-[0_4px_10px_-2px_rgba(239,68,68,0.5)] ring-1 ring-white/60 dark:ring-white/10 tabular-nums">
                              {session.agentUnreadCount > 99 ? '99+' : session.agentUnreadCount}
                            </span>
                          )}
                        </div>

                        {/* 第三行：标签 + 接待按钮 */}
                        <div className="flex items-center gap-1.5 mt-2 flex-wrap min-w-0">
                          <span
                            className={`inline-flex items-center gap-0.5 px-2 py-0.5 rounded-full text-[10px] font-semibold shadow-sm flex-shrink-0 ${
                              session.status === 'waiting'
                                ? 'bg-gradient-to-r from-amber-100 to-orange-100 dark:from-amber-900/40 dark:to-orange-900/30 text-amber-700 dark:text-amber-300 border border-amber-200/70 dark:border-amber-700/30'
                                : session.status === 'active'
                                  ? 'bg-gradient-to-r from-emerald-100 to-teal-100 dark:from-emerald-900/40 dark:to-teal-900/30 text-emerald-700 dark:text-emerald-300 border border-emerald-200/70 dark:border-emerald-700/30'
                                  : session.status === 'awaiting_rating'
                                    ? 'bg-gradient-to-r from-violet-100 to-fuchsia-100 dark:from-violet-900/40 dark:to-fuchsia-900/30 text-violet-700 dark:text-violet-300 border border-violet-200/70 dark:border-violet-700/30'
                                    : session.status === 'transferred'
                                      ? 'bg-gradient-to-r from-blue-100 to-indigo-100 dark:from-blue-900/40 dark:to-indigo-900/30 text-blue-700 dark:text-blue-300 border border-blue-200/70 dark:border-blue-700/30'
                                      : 'bg-slate-100 dark:bg-slate-700/50 text-slate-500 dark:text-slate-400 border border-slate-200/60 dark:border-slate-600/40'
                            }`}
                          >
                            {statusLabelKey ? t(statusLabelKey) : session.status}
                          </span>
                          {session.messageCount > 0 && (
                            <span className="inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded-full bg-slate-100/80 dark:bg-slate-700/50 text-[10px] text-slate-500 dark:text-slate-400 border border-slate-200/60 dark:border-slate-600/40 font-medium flex-shrink-0">
                              <MessageCircle className="w-3 h-3" />
                              <span className="tabular-nums">{session.messageCount}</span>
                            </span>
                          )}
                          {!onSelectMode && session.status === 'waiting' && (
                            <button
                              type="button"
                              onClick={(e) => {
                                e.stopPropagation();
                                onAssignSession(session.id);
                              }}
                              className="ml-auto inline-flex items-center gap-1 px-2.5 py-1 text-[10px] font-bold text-white bg-gradient-to-r from-amber-500 via-orange-500 to-red-500 rounded-full hover:from-amber-600 hover:via-orange-600 hover:to-red-600 shadow-[0_4px_12px_-4px_rgba(249,115,22,0.55)] transition-all duration-200 hover:scale-[1.04] hover:-translate-y-px ring-1 ring-white/50 dark:ring-white/10 flex-shrink-0"
                            >
                              <Phone className="w-3 h-3" strokeWidth={2.2} />
                              {t('chat.list.accept')}
                            </button>
                          )}
                        </div>
                      </div>
                    </div>
                  </div>
                );
              })}

              {/* 分页器 */}
              {totalPages > 1 && (
                <div className="mx-1.5 mt-3 mb-2 px-3 py-2.5 rounded-2xl bg-gradient-to-br from-white via-slate-50 to-white/80 dark:from-slate-800/80 dark:via-slate-800 dark:to-slate-900/70 border border-slate-200/70 dark:border-slate-700/50 shadow-sm flex items-center justify-between min-w-0">
                  <span className="text-[11px] text-slate-500 dark:text-slate-400 font-semibold tabular-nums flex-shrink-0 break-words">
                    {((page - 1) * pageSize) + 1}-{Math.min(page * pageSize, totalItems)}&nbsp;/&nbsp;{totalItems}
                  </span>
                  <div className="flex items-center gap-1 flex-shrink-0">
                    <button
                      onClick={() => onPageChange(Math.max(page - 1, 1))}
                      disabled={page === 1}
                      className="inline-flex items-center justify-center w-7 h-7 rounded-xl text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-700/80 disabled:opacity-40 disabled:cursor-not-allowed transition-all border border-transparent hover:border-slate-200 dark:hover:border-slate-600/80"
                      aria-label={t('chat.list.prevPage')}
                    >
                      <ChevronLeft className="w-3.5 h-3.5" strokeWidth={2.4} />
                    </button>
                    <span className="text-xs font-bold text-slate-700 dark:text-slate-200 px-2 min-w-[50px] text-center tabular-nums bg-slate-100/70 dark:bg-slate-700/60 rounded-lg border border-slate-200/60 dark:border-slate-600/40 py-1 flex-shrink-0">
                      {page}&nbsp;/&nbsp;{totalPages}
                    </span>
                    <button
                      onClick={() => onPageChange(Math.min(page + 1, totalPages))}
                      disabled={page === totalPages}
                      className="inline-flex items-center justify-center w-7 h-7 rounded-xl text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-700/80 disabled:opacity-40 disabled:cursor-not-allowed transition-all border border-transparent hover:border-slate-200 dark:hover:border-slate-600/80"
                      aria-label={t('chat.list.nextPage')}
                    >
                      <ChevronRight className="w-3.5 h-3.5" strokeWidth={2.4} />
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </aside>
  );
}

const ChatSessionList = memo(ChatSessionListImpl);
export default ChatSessionList;
