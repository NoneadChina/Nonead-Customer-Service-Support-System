import { memo } from 'react';
import type { ChatSessionDto } from '../../api/types';
import { useI18n } from '../../context/I18nContext';
import { formatDateTime } from '../../utils/format';
import type { TranslationKey } from '../../locales/zh-Hans';
import {
  Mail,
  Phone,
  User,
  Globe,
  Clock,
  MessageCircle,
  Inbox,
  Star,
  ExternalLink,
  Tag,
  Server,
  Crown,
  Sparkles,
} from 'lucide-react';

interface ChatInfoPanelProps {
  session: ChatSessionDto;
  onJumpToConversation?: (conversationId: number) => void;
}

const STATUS_DOT_COLORS: Record<string, string> = {
  waiting: 'bg-amber-500',
  active: 'bg-emerald-500',
  closed: 'bg-slate-400',
  transferred: 'bg-blue-500',
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

/** 评分星级渲染 */
function RatingStars({ rating }: { rating: number | null }) {
  if (rating == null) return null;
  return (
    <div className="flex items-center gap-0.5">
      {[1, 2, 3, 4, 5].map((n) => (
        <Star
          key={n}
          className={`w-4 h-4 transition-all ${
            n <= rating
              ? 'text-amber-400 fill-amber-400 drop-shadow-[0_2px_4px_rgba(251,191,36,0.45)]'
              : 'text-slate-300 dark:text-slate-600'
          }`}
          strokeWidth={n <= rating ? 0 : 1.8}
        />
      ))}
      <span className="ml-1.5 text-sm font-bold text-slate-700 dark:text-slate-200 tabular-nums">
        {rating}/5
      </span>
    </div>
  );
}

function InfoRow({
  icon,
  label,
  value,
  mono,
}: {
  icon: React.ReactNode;
  label: string;
  value: React.ReactNode;
  mono?: boolean;
}) {
  return (
    <div className="flex items-start gap-2.5 py-2 min-w-0">
      <div className="mt-0.5 flex-shrink-0 w-7 h-7 rounded-lg bg-gradient-to-br from-slate-50 to-white dark:from-slate-700/80 dark:to-slate-800 flex items-center justify-center text-slate-400 dark:text-slate-400 ring-1 ring-slate-200/70 dark:ring-slate-600/40 shadow-sm">
        {icon}
      </div>
      <div className="flex-1 min-w-0">
        <p className="text-[10px] font-semibold text-slate-400 dark:text-slate-500 uppercase tracking-wider leading-none mb-1">
          {label}
        </p>
        <p
          className={`text-sm text-slate-700 dark:text-slate-200 break-words leading-snug ${
            mono ? 'font-mono tabular-nums' : ''
          }`}
        >
          {value != null && value !== '' ? value : '-'}
        </p>
      </div>
    </div>
  );
}

function SectionCard({
  title,
  icon,
  accent,
  children,
}: {
  title: string;
  icon: React.ReactNode;
  accent?: 'indigo' | 'emerald' | 'rose' | 'amber' | 'sky' | 'violet';
  children: React.ReactNode;
}) {
  const accentStyles: Record<string, string> = {
    indigo: 'from-indigo-500 via-blue-500 to-violet-500',
    emerald: 'from-emerald-500 via-teal-500 to-green-500',
    rose: 'from-rose-500 via-pink-500 to-red-500',
    amber: 'from-amber-500 via-orange-500 to-yellow-500',
    sky: 'from-sky-500 via-blue-500 to-cyan-500',
    violet: 'from-violet-500 via-purple-500 to-fuchsia-500',
  };
  const gradient = accentStyles[accent ?? 'indigo'];
  return (
    <div className="bg-white dark:bg-slate-800/90 rounded-2xl border border-slate-200/80 dark:border-slate-700/60 overflow-hidden shadow-[0_2px_10px_-4px_rgba(15,23,42,0.12)] hover:shadow-[0_4px_16px_-6px_rgba(15,23,42,0.18)] transition-shadow duration-300 relative">
      <div className={`absolute top-0 left-0 right-0 h-[3px] bg-gradient-to-r ${gradient}`} />
      <div className="px-4 py-3 border-b border-slate-100/90 dark:border-slate-700/50 flex items-center gap-2 bg-gradient-to-b from-slate-50/60 to-white dark:from-slate-800/60 dark:to-slate-800">
        <span className={`w-6 h-6 rounded-lg flex items-center justify-center bg-gradient-to-br ${gradient} text-white shadow-sm shadow-black/10`}>
          {icon}
        </span>
        <h4 className="text-[11px] font-bold text-slate-700 dark:text-slate-200 uppercase tracking-wider">
          {title}
        </h4>
      </div>
      <div className="px-4 py-1.5 divide-y divide-slate-50 dark:divide-slate-700/30 min-w-0">
        {children}
      </div>
    </div>
  );
}

function ChatInfoPanelImpl({ session, onJumpToConversation }: ChatInfoPanelProps) {
  const { t } = useI18n();
  const statusLabelKey = CHAT_STATUS_LABEL_KEYS[session.status];
  const avatarColor =
    session.status === 'waiting'
      ? 'bg-gradient-to-br from-amber-400 to-orange-500'
      : session.status === 'active'
        ? 'bg-gradient-to-br from-emerald-500 to-teal-600'
        : session.status === 'transferred'
          ? 'bg-gradient-to-br from-blue-500 to-indigo-600'
          : 'bg-gradient-to-br from-slate-400 to-slate-500';

  return (
    <aside
      className="w-full lg:w-80 flex-shrink-0 flex flex-col bg-gradient-to-b from-slate-50 via-slate-50/70 to-white/60 dark:from-slate-900/70 dark:via-slate-900/50 dark:to-slate-950/60 border-l border-slate-200/80 dark:border-slate-700/60 min-w-0 overflow-x-hidden"
      style={{
        overflowY: 'auto',
        scrollbarWidth: 'thin',
        scrollbarColor: 'rgba(148,163,184,0.35) transparent',
      }}
    >
      <style>{`
        .chat-info-panel-scroll::-webkit-scrollbar { width: 6px; height: 0; }
        .chat-info-panel-scroll::-webkit-scrollbar-track { background: transparent; }
        .chat-info-panel-scroll::-webkit-scrollbar-thumb {
          background: rgba(148,163,184,0.35);
          border-radius: 999px;
          border: 2px solid transparent;
          background-clip: padding-box;
        }
        .chat-info-panel-scroll::-webkit-scrollbar-thumb:hover {
          background: rgba(100,116,139,0.5);
          background-clip: padding-box;
          border: 2px solid transparent;
        }
        html body aside[class*="chat-info-panel-"],
        .chat-info-panel-scroll {
          scrollbar-width: thin;
        }
      `}</style>

      <div className="chat-info-panel-scroll h-full w-full min-w-0">
        {/* ====== 客户卡片 ====== */}
        <div className="p-4 relative">
          {/* 背景光斑 */}
          <div className="absolute top-2 right-4 w-32 h-32 rounded-full bg-gradient-to-br from-indigo-400/20 via-blue-400/15 to-violet-400/10 blur-3xl pointer-events-none" />
          <div className="absolute -bottom-4 left-0 w-28 h-28 rounded-full bg-gradient-to-br from-emerald-400/15 via-teal-400/10 to-cyan-400/10 blur-3xl pointer-events-none" />

          <div className="relative bg-white dark:bg-slate-800/90 rounded-2xl border border-slate-200/80 dark:border-slate-700/60 p-4 shadow-[0_8px_24px_-10px_rgba(15,23,42,0.15)] overflow-hidden">
            {/* 顶部装饰条 */}
            <div className="absolute inset-x-0 top-0 h-1 bg-gradient-to-r from-indigo-500 via-blue-500 via-sky-500 to-violet-500" />

            <div className="flex flex-col items-center text-center relative pt-1">
              <div className="relative">
                <div
                  className={`w-16 h-16 rounded-2xl flex items-center justify-center text-white text-xl font-bold shadow-[0_10px_24px_-10px_rgba(15,23,42,0.4)] ring-4 ring-white dark:ring-slate-800 ${avatarColor}`}
                >
                  {session.customerName?.charAt(0) || '?'}
                </div>
                <span
                  className={`absolute -bottom-1 -right-1 w-5 h-5 rounded-full ring-4 ring-white dark:ring-slate-800 shadow-lg ${
                    STATUS_DOT_COLORS[session.status] ?? 'bg-slate-400'
                  } ${session.status === 'waiting' ? 'animate-pulse' : ''}`}
                />
              </div>

              <div className="mt-3 flex items-center gap-2 flex-wrap justify-center min-w-0">
                <h3 className="text-base font-bold text-slate-900 dark:text-slate-100 leading-tight break-words max-w-full">
                  {session.customerName || t('chat.info.unknownCustomer')}
                </h3>
              </div>

              <div className="mt-1.5 inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold bg-gradient-to-r from-slate-100 to-white dark:from-slate-700/70 dark:to-slate-700/50 text-slate-600 dark:text-slate-200 border border-slate-200/70 dark:border-slate-600/60 shadow-sm">
                <Sparkles className="w-3 h-3 text-indigo-500 dark:text-indigo-400 flex-shrink-0" />
                <span className="truncate">{statusLabelKey ? t(statusLabelKey) : session.status}</span>
              </div>
            </div>

            {/* 评分区 */}
            {session.rating != null && (
              <div className="mt-4 pt-4 border-t border-dashed border-slate-200/80 dark:border-slate-700/60 relative">
                <div className="flex items-start justify-between gap-2 min-w-0">
                  <div className="min-w-0">
                    <div className="flex items-center gap-1 mb-1">
                      <Crown className="w-3.5 h-3.5 text-amber-500 flex-shrink-0" />
                      <span className="text-[11px] font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider">
                        {t('chat.info.customerRating')}
                      </span>
                    </div>
                    <RatingStars rating={session.rating} />
                  </div>
                </div>
                {session.ratingComment && (
                  <div className="mt-3 text-xs text-slate-700 dark:text-slate-200 bg-gradient-to-br from-amber-50 via-yellow-50 to-orange-50 dark:from-amber-900/20 dark:via-yellow-900/15 dark:to-orange-900/15 rounded-xl p-3 italic leading-relaxed border-l-4 border-amber-400 shadow-[0_2px_8px_-4px_rgba(245,158,11,0.25)] break-words min-w-0">
                    <span className="text-amber-500/80 dark:text-amber-400/80 font-serif text-lg leading-none select-none mr-1 align-middle">&ldquo;</span>
                    {session.ratingComment}
                    <span className="text-amber-500/80 dark:text-amber-400/80 font-serif text-lg leading-none select-none ml-1 align-middle">&rdquo;</span>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>

        {/* ====== 客户信息 ====== */}
        <div className="px-4 pb-4 min-w-0">
          <SectionCard title={t('chat.info.customerSection')} icon={<User className="w-3 h-3" />} accent="emerald">
            <InfoRow
              icon={<Mail className="w-3.5 h-3.5 text-emerald-500" />}
              label={t('chat.info.email')}
              value={session.customerEmail || t('chat.info.notProvided')}
            />
            <InfoRow
              icon={<Phone className="w-3.5 h-3.5 text-teal-500" />}
              label={t('chat.info.phone')}
              value={session.customerPhone || t('chat.info.notProvided')}
              mono
            />
            <InfoRow
              icon={<Globe className="w-3.5 h-3.5 text-sky-500" />}
              label={t('chat.info.ipAddress')}
              value={session.customerIp || t('format.unknown')}
              mono
            />
            <InfoRow
              icon={<User className="w-3.5 h-3.5 text-violet-500" />}
              label={t('chat.info.customerId')}
              value={session.customerId ?? t('chat.info.notLinked')}
              mono
            />
          </SectionCard>
        </div>

        {/* ====== 会话信息 ====== */}
        <div className="px-4 pb-4 min-w-0">
          <SectionCard title={t('chat.info.conversationSection')} icon={<MessageCircle className="w-3 h-3" />} accent="indigo">
            <InfoRow
              icon={<Tag className="w-3.5 h-3.5 text-rose-500" />}
              label={t('chat.info.subject')}
              value={session.subject || t('chat.info.noSubject')}
            />
            <InfoRow
              icon={<Server className="w-3.5 h-3.5 text-indigo-500" />}
              label={t('chat.info.mailbox')}
              value={session.mailboxName || t('format.unassigned')}
            />
            <InfoRow
              icon={<User className="w-3.5 h-3.5 text-sky-500" />}
              label={t('chat.info.agent')}
              value={session.userName || t('format.unassigned')}
            />
            <InfoRow
              icon={<MessageCircle className="w-3.5 h-3.5 text-violet-500" />}
              label={t('chat.info.messageCount')}
              value={
                <span className="inline-flex items-center gap-1.5 flex-wrap min-w-0">
                  <span className="tabular-nums">{session.messageCount}</span>
                  {session.agentUnreadCount > 0 && (
                    <span className="inline-flex items-center justify-center min-w-[18px] h-[18px] px-1.5 bg-gradient-to-br from-red-500 via-rose-500 to-pink-600 text-white text-[10px] font-bold rounded-full shadow-[0_3px_8px_-2px_rgba(239,68,68,0.5)] ring-1 ring-white/60 dark:ring-white/10 tabular-nums flex-shrink-0">
                      {t('chat.info.unreadCount', { count: session.agentUnreadCount })}
                    </span>
                  )}
                </span>
              }
            />
            <InfoRow
              icon={<Clock className="w-3.5 h-3.5 text-slate-500" />}
              label={t('chat.info.createdAt')}
              value={formatDateTime(session.createdAt)}
            />
            <InfoRow
              icon={<Clock className="w-3.5 h-3.5 text-amber-500" />}
              label={t('chat.info.updatedAt')}
              value={formatDateTime(session.updatedAt)}
            />
            {session.closedAt && (
              <InfoRow
                icon={<Clock className="w-3.5 h-3.5 text-slate-400" />}
                label={t('chat.info.closedAt')}
                value={formatDateTime(session.closedAt)}
              />
            )}
          </SectionCard>
        </div>

        {/* ====== 关联工单 ====== */}
        {session.conversationId && (
          <div className="px-4 pb-6 min-w-0">
            <button
              onClick={() => onJumpToConversation?.(session.conversationId!)}
              className="w-full flex items-center justify-between gap-2 px-4 py-3.5 rounded-2xl bg-gradient-to-br from-indigo-50 via-blue-50 to-violet-50 dark:from-indigo-900/40 dark:via-blue-900/35 dark:to-violet-900/30 border border-indigo-200/70 dark:border-indigo-700/40 hover:border-indigo-400/70 dark:hover:border-indigo-500/70 hover:shadow-[0_8px_20px_-8px_rgba(79,70,229,0.45)] hover:-translate-y-0.5 transition-all duration-300 group relative overflow-hidden"
            >
              <div className="absolute inset-0 opacity-0 group-hover:opacity-100 transition-opacity duration-500 bg-[radial-gradient(circle_at_20%_0%,rgba(99,102,241,0.2),transparent_50%),radial-gradient(circle_at_100%_100%,rgba(139,92,246,0.18),transparent_50%)]" />
              <div className="flex items-center gap-2.5 min-w-0 relative">
                <div className="w-9 h-9 rounded-xl bg-gradient-to-br from-indigo-500 via-blue-500 to-violet-600 text-white flex items-center justify-center shadow-[0_4px_14px_-4px_rgba(79,70,229,0.6)] flex-shrink-0">
                  <Inbox className="w-4 h-4" strokeWidth={2.1} />
                </div>
                <div className="min-w-0 text-left">
                  <p className="text-[10px] font-bold text-indigo-500 dark:text-indigo-300 uppercase tracking-wider mb-0.5 leading-none">
                    {t('chat.info.linkedConversation')}
                  </p>
                  <p className="text-sm font-bold text-slate-800 dark:text-slate-100 truncate leading-tight">
                    <span className="text-indigo-500 dark:text-indigo-300 mr-1">#</span>
                    {session.conversationId}
                  </p>
                </div>
              </div>
              <div className="flex items-center gap-1 flex-shrink-0 relative">
                <span className="text-[11px] font-semibold text-indigo-600 dark:text-indigo-300 opacity-0 group-hover:opacity-100 transition-all duration-300 translate-x-1 group-hover:translate-x-0">
                  {t('chat.info.viewConversation')}
                </span>
                <div className="w-7 h-7 rounded-lg bg-white/70 dark:bg-slate-800/70 border border-indigo-200/60 dark:border-indigo-800/40 flex items-center justify-center text-slate-500 group-hover:text-indigo-500 dark:group-hover:text-indigo-300 transition-all duration-300 group-hover:translate-x-0.5 group-hover:bg-white dark:group-hover:bg-slate-800 shadow-sm">
                  <ExternalLink className="w-3.5 h-3.5" strokeWidth={2.2} />
                </div>
              </div>
            </button>
          </div>
        )}
      </div>
    </aside>
  );
}

const ChatInfoPanel = memo(ChatInfoPanelImpl);
export default ChatInfoPanel;
