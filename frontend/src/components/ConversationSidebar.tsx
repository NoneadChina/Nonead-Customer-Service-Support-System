import { useState, useEffect, useRef } from 'react';
import type { TranslationKey } from '../locales/zh-Hans';
import { useI18n } from '../context/I18nContext';
import type { ConversationResponse, MailboxResponse, UserResponse, SatisfactionRatingDto, ChatSessionDto } from '../api/types';
import { formatDateTime, getStatusLabel, getStatusColor, getPriorityLabel, getPriorityColor, getPriorityTextColor, getSourceTypeLabel, getSourceTypeColor, safeUrl, sourceTypeToString } from '../utils/format';
import { getRoleLabel } from '../utils/permissions';
import UserAvatar from './UserAvatar';
import PriorityIcon from './PriorityIcon';
import SourceTypeIcon from './SourceTypeIcon';
import { MessageCircle, Star, CalendarDays, Check, AlertTriangle } from 'lucide-react';
import CustomFieldsPanel from './CustomFieldsPanel';

interface Props {
  conversation: ConversationResponse;
  mailboxes: MailboxResponse[];
  users: UserResponse[];
  isFollowing: boolean;
  onToggleFollow: () => void;
  onStatusChange: (status: number) => void;
  onPriorityChange: (priority: number) => void;
  onAssign: (userId: number) => void;
  updating: boolean;
  satisfactionRating?: SatisfactionRatingDto | null;
  /** 该工单若由在线聊天转工单生成，关联的 ChatSession（包含在线聊天直接评分 rating / ratingComment） */
  chatSessionRating?: ChatSessionDto | null;
  readOnly?: boolean;
  canAssign?: boolean;
}

const RATING_BADGE: Record<string, string> = {
  good: 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-400',
  ok: 'bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-400',
  bad: 'bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-400',
};

/**
 * 在线聊天评分档位。**只持有真源 key（labelKey）**，渲染时必须经 `t()` 解析——
 * 直接渲染表里的值会把 `conversations.sidebar.ratingSatisfied` 这样的原始 key 漏到界面上。
 */
const CHAT_RATING_CATEGORY: Record<number, { badge: string; labelKey: TranslationKey }> = {
  5: { badge: 'bg-gradient-to-r from-amber-400/90 to-orange-500/90 text-white shadow-sm shadow-amber-500/30', labelKey: 'conversations.sidebar.ratingVerySatisfied' },
  4: { badge: 'bg-gradient-to-r from-emerald-400/90 to-teal-500/90 text-white shadow-sm shadow-emerald-500/30', labelKey: 'conversations.sidebar.ratingSatisfied' },
  3: { badge: 'bg-gradient-to-r from-sky-400/90 to-blue-500/90 text-white shadow-sm shadow-sky-500/30', labelKey: 'conversations.sidebar.ratingNeutral' },
  2: { badge: 'bg-gradient-to-r from-orange-400/90 to-amber-500/90 text-white shadow-sm shadow-orange-500/30', labelKey: 'conversations.sidebar.ratingDissatisfied' },
  1: { badge: 'bg-gradient-to-r from-rose-400/90 to-red-500/90 text-white shadow-sm shadow-rose-500/30', labelKey: 'conversations.sidebar.ratingVeryDissatisfied' },
};

export default function ConversationSidebar({
  conversation,
  mailboxes,
  users,
  isFollowing,
  onToggleFollow,
  onStatusChange,
  onPriorityChange,
  onAssign,
  updating,
  satisfactionRating,
  chatSessionRating = null,
  readOnly = false,
  canAssign = true,
}: Props) {
  const { t } = useI18n();
  const [showAssignDropdown, setShowAssignDropdown] = useState(false);
  // OpenID 复制按钮的视觉反馈状态：idle / success / error
  const [openIdCopyState, setOpenIdCopyState] = useState<'idle' | 'success' | 'error'>('idle');
  const copyTimeoutRef = useRef<ReturnType<typeof setTimeout>>();

  // 组件卸载时清除复制状态复位定时器
  useEffect(() => {
    return () => {
      if (copyTimeoutRef.current) clearTimeout(copyTimeoutRef.current);
    };
  }, []);

  const customer = conversation.customer;
  const assignedUser = conversation.user;
  const mailboxName = mailboxes.find((m) => m.id === conversation.mailboxId)?.name || t('conversations.sidebar.unknownMailbox');
  const hasTags = conversation.tags && conversation.tags.length > 0;

  return (
    <aside className="w-full md:w-72 flex-shrink-0 space-y-4">
      {/* 快速操作 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4">
        <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100 mb-3 flex items-center gap-2">
          <svg className="w-4 h-4 text-primary-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.066 2.573c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.573 1.066c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.066-2.573c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
            <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
          </svg>
          {t('conversations.sidebar.actions')}
        </h3>
        <div className="space-y-2">
          <button
            onClick={onToggleFollow}
            disabled={updating || readOnly}
            className={`w-full flex items-center justify-center gap-2 px-3 py-2 rounded-lg text-sm font-medium transition ${
              isFollowing
                ? 'bg-amber-50 text-amber-700 border border-amber-200 hover:bg-amber-100 dark:bg-amber-900/30 dark:text-amber-400 dark:border-amber-700 dark:hover:bg-amber-900/50'
                : 'bg-gray-50 text-gray-700 border border-gray-200 hover:bg-gray-100 dark:bg-slate-700 dark:text-slate-300 dark:border-slate-600 dark:hover:bg-slate-600'
            } ${readOnly ? 'opacity-50 cursor-not-allowed' : ''}`}
          >
            <svg className="w-4 h-4" fill={isFollowing ? 'currentColor' : 'none'} viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M11.049 2.927c.3-.921 1.603-.921 1.902 0l1.519 4.674a1 1 0 00.95.69h4.915c.969 0 1.371 1.24.588 1.81l-3.976 2.888a1 1 0 00-.363 1.118l1.518 4.674c.3.922-.755 1.688-1.538 1.118l-3.976-2.888a1 1 0 00-1.176 0l-3.976 2.888c-.783.57-1.838-.197-1.538-1.118l1.518-4.674a1 1 0 00-.363-1.118l-3.976-2.888c-.784-.57-.38-1.81.588-1.81h4.914a1 1 0 00.951-.69l1.519-4.674z" />
            </svg>
            {isFollowing ? t('conversations.sidebar.unfollow') : t('conversations.sidebar.follow')}
          </button>

          {/* 状态切换 */}
          <div className="relative">
            <select
              value={conversation.status}
              onChange={(e) => onStatusChange(Number(e.target.value))}
              disabled={updating || readOnly}
              aria-label={t('conversations.sidebar.statusAria')}
              className={`w-full px-3 py-2 text-sm border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 appearance-none cursor-pointer bg-white dark:bg-slate-700 border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-200 ${
                conversation.status === 1 ? 'text-green-700 dark:text-green-400' :
                conversation.status === 2 ? 'text-amber-700 dark:text-amber-400' :
                conversation.status === 3 ? 'text-gray-500 dark:text-slate-400' :
                conversation.status === 4 ? 'text-red-700 dark:text-red-400' : ''
              } ${readOnly ? 'opacity-50 cursor-not-allowed' : ''}`}
            >
              {[1, 2, 3, 4].map((value) => (
                <option key={value} value={value}>
                  {t('conversations.sidebar.statusOption', { status: getStatusLabel(value) })}
                </option>
              ))}
            </select>
          </div>

          {/* 优先级切换 */}
          <div className="relative">
            <select
              value={conversation.priority}
              onChange={(e) => onPriorityChange(Number(e.target.value))}
              disabled={updating || readOnly}
              aria-label={t('conversations.sidebar.priorityAria')}
              className={`w-full px-3 py-2 text-sm border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 appearance-none cursor-pointer bg-white dark:bg-slate-700 border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-200 ${getPriorityTextColor(conversation.priority)} ${readOnly ? 'opacity-50 cursor-not-allowed' : ''}`}
            >
              {[0, 1, 2, 3].map((value) => (
                <option key={value} value={value}>
                  {t('conversations.sidebar.priorityOption', { priority: getPriorityLabel(value) })}
                </option>
              ))}
            </select>
          </div>

          {/* 分配用户 */}
          <div className="relative">
            <button
              onClick={() => canAssign && !readOnly && setShowAssignDropdown(!showAssignDropdown)}
              disabled={updating || readOnly || !canAssign}
              className={`w-full flex items-center justify-between gap-2 px-3 py-2 text-sm border border-gray-200 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-700 hover:bg-gray-50 dark:hover:bg-slate-600 text-gray-700 dark:text-slate-200 ${(readOnly || !canAssign) ? 'opacity-50 cursor-not-allowed' : ''}`}
            >
              <span className="truncate">
                {assignedUser
                  ? t('conversations.sidebar.assignedTo', { name: assignedUser.fullName })
                  : t('conversations.sidebar.assignPlaceholder')}
              </span>
              <svg className="w-4 h-4 text-gray-400 dark:text-slate-500 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
              </svg>
            </button>
            {showAssignDropdown && (
              <div className="absolute z-10 w-full mt-1 bg-white dark:bg-slate-700 border border-gray-200 dark:border-slate-600 rounded-lg shadow-lg max-h-48 overflow-y-auto">
                <button
                  onClick={() => { onAssign(0); setShowAssignDropdown(false); }}
                  className="w-full px-3 py-2 text-left text-sm text-gray-700 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-600"
                >
                  {t('format.unassigned')}
                </button>
                {users.map((u) => (
                  <button
                    key={u.id}
                    onClick={() => { onAssign(u.id); setShowAssignDropdown(false); }}
                    className={`w-full px-3 py-2 text-left text-sm hover:bg-gray-50 dark:hover:bg-slate-600 flex items-center gap-2 ${
                      u.id === assignedUser?.id ? 'bg-primary-50 text-primary-700 dark:bg-primary-900/30 dark:text-primary-400' : 'text-gray-700 dark:text-slate-300'
                    }`}
                  >
                    <UserAvatar userId={u.id} name={u.fullName} size="xs" avatarUrl={u.avatar ?? null} />
                    {u.fullName}
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>

      {/* 客户信息 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4">
        <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100 mb-3 flex items-center gap-2">
          <svg className="w-4 h-4 text-pink-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
          </svg>
          {t('conversations.sidebar.customerInfo')}
        </h3>
        {customer ? (
          <div className="space-y-3">
            <div className="flex items-center gap-3">
              {customer.avatar ? (
                <img
                  src={safeUrl(customer.avatar)}
                  alt={
                  customer.fullName ||
                  (customer.channel === 'wechat'
                    ? t('conversations.shared.wechatCustomer')
                    : t('format.customerFallback'))
                }
                  loading="lazy"
                  className="w-10 h-10 rounded-full object-cover border border-gray-200 dark:border-slate-600"
                />
              ) : (
                <div className={`w-10 h-10 rounded-full flex items-center justify-center text-sm font-semibold ${
                  customer.channel === 'wechat'
                    ? 'bg-gradient-to-br from-green-500 to-emerald-600 text-white shadow-sm shadow-green-500/20'
                    : 'bg-gradient-to-br from-pink-100 to-rose-100 text-pink-700 dark:from-pink-900/40 dark:to-rose-900/40 dark:text-pink-400'
                }`}>
                  {customer.fullName?.charAt(0) || '?'}
                </div>
              )}
              <div className="flex-1 min-w-0">
                <div className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate flex items-center gap-1.5">
                  {customer.fullName}
                  {customer.channel === 'wechat' && (
                    <SourceTypeIcon
                      sourceType={7}
                      className="w-3.5 h-3.5 text-green-500 dark:text-green-400 flex-shrink-0"
                      title={t('conversations.shared.wechatCustomer')}
                    />
                  )}
                </div>
                <div className="text-xs text-gray-500 dark:text-slate-400">ID: #{customer.id}</div>
              </div>
            </div>
            <div className="space-y-2 text-xs">
              {customer.channel === 'wechat' && !customer.mainEmail ? (
                <div className="flex items-start gap-2">
                  <SourceTypeIcon
                    sourceType={7}
                    className="w-3.5 h-3.5 text-green-500 dark:text-green-400 mt-0.5 flex-shrink-0"
                    title={t('conversations.sidebar.wechatChannel')}
                  />
                  <div className="flex-1 min-w-0 flex items-center gap-1.5 flex-wrap">
                    <span className="inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-400">
                      {t('conversations.sidebar.wechatChannel')}
                    </span>
                    {customer.channelId && (
                      <button
                        type="button"
                        onClick={() => {
                          const cid = customer.channelId;
                          if (!cid) return;
                          void navigator.clipboard?.writeText(cid)
                            .then(() => {
                              setOpenIdCopyState('success');
                              copyTimeoutRef.current = setTimeout(() => setOpenIdCopyState('idle'), 1500);
                            })
                            .catch(() => {
                              setOpenIdCopyState('error');
                              copyTimeoutRef.current = setTimeout(() => setOpenIdCopyState('idle'), 1500);
                            });
                        }}
                        className={`inline-flex items-center gap-0.5 font-mono text-[10px] transition-colors rounded px-1 py-0.5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-green-400 focus-visible:ring-offset-1 ${
                          openIdCopyState === 'success'
                            ? 'text-green-600 dark:text-green-400'
                            : openIdCopyState === 'error'
                            ? 'text-red-600 dark:text-red-400'
                            : 'text-gray-400 dark:text-slate-500 hover:text-green-600 dark:hover:text-green-400 group'
                        }`}
                        title={
                          openIdCopyState === 'success'
                            ? t('conversations.sidebar.copied')
                            : openIdCopyState === 'error'
                            ? t('conversations.sidebar.copyFailed')
                            : t('conversations.sidebar.copyOpenIdTitle', { id: customer.channelId })
                        }
                        aria-label={
                          openIdCopyState === 'success'
                            ? t('conversations.sidebar.copyOpenIdAriaSuccess')
                            : openIdCopyState === 'error'
                            ? t('conversations.sidebar.copyOpenIdAriaFailed')
                            : t('conversations.sidebar.copyOpenIdAria', { id: customer.channelId })
                        }
                        aria-live="polite"
                      >
                        <span className="truncate max-w-[120px]">OpenID: …{customer.channelId.slice(-6)}</span>
                        {openIdCopyState === 'success' ? (
                          <Check className="w-3 h-3 flex-shrink-0" aria-hidden="true" />
                        ) : openIdCopyState === 'error' ? (
                          <AlertTriangle className="w-3 h-3 flex-shrink-0" aria-hidden="true" />
                        ) : (
                          <svg className="w-3 h-3 opacity-0 group-hover:opacity-100 transition-opacity flex-shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                            <rect x="9" y="9" width="13" height="13" rx="2" ry="2" />
                            <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
                          </svg>
                        )}
                      </button>
                    )}
                  </div>
                </div>
              ) : (
                <div className="flex items-start gap-2">
                  <svg className="w-3.5 h-3.5 text-gray-400 dark:text-slate-500 mt-0.5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
                  </svg>
                  <div className="flex-1 min-w-0">
                    {customer.mainEmail ? (
                      <a href={`mailto:${customer.mainEmail}`} className="text-gray-700 dark:text-slate-300 truncate hover:text-primary-600 dark:hover:text-primary-400 transition">
                        {customer.mainEmail}
                      </a>
                    ) : (
                      <div className="text-gray-400 dark:text-slate-500">-</div>
                    )}
                  </div>
                </div>
              )}
              {customer.company && (
                <div className="flex items-start gap-2">
                  <svg className="w-3.5 h-3.5 text-gray-400 dark:text-slate-500 mt-0.5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4" />
                  </svg>
                  <div className="flex-1 min-w-0">
                    <span className="text-gray-700 dark:text-slate-300 truncate">{customer.company}</span>
                    {customer.jobTitle && (
                      <span className="text-gray-400 dark:text-slate-500 ml-1">· {customer.jobTitle}</span>
                    )}
                  </div>
                </div>
              )}
              {/* 邮箱渠道显示来源徽章；微信渠道已在上方展示 OpenID,此处不重复 */}
              {customer.channel && customer.channel !== 'wechat' && (
                <div className="flex items-start gap-2">
                  <svg className="w-3.5 h-3.5 text-gray-400 dark:text-slate-500 mt-0.5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M8.684 13.342C8.886 12.938 10 12 12 12s3.114.938 3.316 1.342M11 20A7 7 0 019.8 6.1C15.5 5 17 4.48 19 2c1 2 2 4.18 2 8 0 5.5-4.78 10-10 10z" />
                  </svg>
                  <div className="flex-1 min-w-0 flex items-center gap-1.5 flex-wrap">
                    <span className={`inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium ${
                      customer.channel === 'wechat'
                        ? 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-400'
                        : 'bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-slate-300'
                    }`}>
                      {customer.channel === 'wechat' ? t('format.sourceType.wechat') : customer.channel}
                    </span>
                    {customer.channelId && (
                      <span className="text-gray-400 dark:text-slate-500 font-mono text-[10px] truncate" title={customer.channelId}>
                        {customer.channel === 'wechat' ? `OpenID: …${customer.channelId.slice(-6)}` : customer.channelId}
                      </span>
                    )}
                  </div>
                </div>
              )}
              <div className="flex items-center gap-2 text-gray-500 dark:text-slate-400">
                <svg className="w-3.5 h-3.5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
                </svg>
                {t('conversations.sidebar.joinedAt', { date: formatDateTime(customer.createdAt) })}
              </div>
              {customer.conversationsCount > 0 && (
                <div className="flex items-center gap-2 text-gray-500 dark:text-slate-400">
                  <svg className="w-3.5 h-3.5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M8 10h.01M12 10h.01M16 10h.01M9 16H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-5l-5 5v-5z" />
                  </svg>
                  {t('conversations.sidebar.conversationCount', { count: customer.conversationsCount })}
                </div>
              )}
            </div>
          </div>
        ) : (
          <div className="space-y-3">
            <div className="flex items-center gap-3">
              <div className="w-10 h-10 bg-gray-200 text-gray-500 dark:bg-slate-700 dark:text-slate-400 rounded-full flex items-center justify-center text-sm font-semibold">
                {conversation.customerEmail?.charAt(0)?.toUpperCase() || '?'}
              </div>
              <div className="flex-1 min-w-0">
                <div className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate">
                  {conversation.customerEmail || t('conversations.sidebar.unknownCustomer')}
                </div>
                <div className="text-xs text-gray-500 dark:text-slate-400">{t('conversations.sidebar.unregisteredCustomer')}</div>
              </div>
            </div>
          </div>
        )}
      </div>

      {/* 客户自定义字段 */}
      {customer && (
        <CustomFieldsPanel
          entityType="customer"
          entityId={customer.id}
          variant="compact"
          readOnly={readOnly}
        />
      )}

      {/* 会话属性 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4">
        <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100 mb-3 flex items-center gap-2">
          <svg className="w-4 h-4 text-blue-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          {t('conversations.sidebar.conversationInfo')}
        </h3>
        <dl className="space-y-0 text-xs">
          <div className="flex justify-between py-2 border-b border-gray-100 dark:border-slate-700">
            <dt className="text-gray-500 dark:text-slate-400">{t('conversations.sidebar.conversationId')}</dt>
            <dd className="text-gray-900 dark:text-slate-200 font-mono">#{conversation.id}</dd>
          </div>
          <div className="flex justify-between items-center py-2 border-b border-gray-100 dark:border-slate-700">
            <dt className="text-gray-500 dark:text-slate-400">{t('conversations.sidebar.status')}</dt>
            <dd>
              <span className={`inline-flex px-2 py-0.5 rounded-full text-[11px] font-medium border ${getStatusColor(conversation.status)}`}>
                {getStatusLabel(conversation.status)}
              </span>
            </dd>
          </div>
          <div className="flex justify-between items-center py-2 border-b border-gray-100 dark:border-slate-700">
            <dt className="text-gray-500 dark:text-slate-400">{t('conversations.sidebar.priority')}</dt>
            <dd>
              <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium border ${getPriorityColor(conversation.priority)}`}>
                <PriorityIcon priority={conversation.priority} className="w-2.5 h-2.5" />
                {getPriorityLabel(conversation.priority)}
              </span>
            </dd>
          </div>
          <div className="flex justify-between items-center py-2 border-b border-gray-100 dark:border-slate-700">
            <dt className="text-gray-500 dark:text-slate-400">{t('conversations.sidebar.source')}</dt>
            <dd>
              <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${getSourceTypeColor(conversation.sourceType)}`}>
                {conversation.sourceType !== 1 && <SourceTypeIcon sourceType={conversation.sourceType} className="w-2.5 h-2.5" />}
                {getSourceTypeLabel(conversation.sourceType)}
              </span>
            </dd>
          </div>
          <div className="flex justify-between py-2 border-b border-gray-100 dark:border-slate-700">
            <dt className="text-gray-500 dark:text-slate-400">{t('conversations.sidebar.mailbox')}</dt>
            <dd className="text-gray-700 dark:text-slate-300 text-right">{mailboxName}</dd>
          </div>
          <div className="flex justify-between py-2 border-b border-gray-100 dark:border-slate-700">
            <dt className="text-gray-500 dark:text-slate-400">{t('conversations.sidebar.assignee')}</dt>
            <dd className="text-gray-700 dark:text-slate-300 text-right">
              {assignedUser?.fullName || <span className="text-gray-400 dark:text-slate-500">{t('format.unassigned')}</span>}
            </dd>
          </div>
          <div className="flex justify-between py-2 border-b border-gray-100 dark:border-slate-700">
            <dt className="text-gray-500 dark:text-slate-400">{t('conversations.sidebar.createdAt')}</dt>
            <dd className="text-gray-700 dark:text-slate-300 text-right">{formatDateTime(conversation.createdAt)}</dd>
          </div>
          <div className={`flex justify-between py-2 ${hasTags ? 'border-b border-gray-100 dark:border-slate-700' : ''}`}>
            <dt className="text-gray-500 dark:text-slate-400">{t('conversations.sidebar.lastReply')}</dt>
            <dd className="text-gray-700 dark:text-slate-300 text-right">{formatDateTime(conversation.lastReplyAt)}</dd>
          </div>
          {hasTags && (
            <div className="pt-2">
              <div className="text-gray-500 dark:text-slate-400 mb-2">{t('conversations.sidebar.tags')}</div>
              <div className="flex flex-wrap gap-1.5">
                {conversation.tags!.map((tag) => (
                  <span
                    key={tag.id}
                    className="inline-flex items-center px-2 py-0.5 rounded-full text-[11px] font-medium"
                    style={{
                      backgroundColor: tag.color + '20',
                      color: tag.color,
                      border: `1px solid ${tag.color}40`
                    }}
                  >
                    {tag.name}
                  </span>
                ))}
              </div>
            </div>
          )}
        </dl>
      </div>

      {/* 会话自定义字段 */}
      <CustomFieldsPanel
        entityType="conversation"
        entityId={conversation.id}
        sourceType={sourceTypeToString(conversation.sourceType)}
        variant="compact"
        readOnly={readOnly}
      />

      {/* 负责人信息 */}
      {assignedUser && (
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4">
          <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100 mb-3 flex items-center gap-2">
            <svg className="w-4 h-4 text-indigo-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M5.121 17.804A13.937 13.937 0 0112 16c2.5 0 4.847.655 6.879 1.804M15 10a3 3 0 11-6 0 3 3 0 016 0zm6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
            {t('conversations.sidebar.assignee')}
          </h3>
          <div className="flex items-center gap-3">
            <UserAvatar userId={assignedUser.id} name={assignedUser.fullName} size="md" avatarUrl={assignedUser.avatar ?? null} />
            <div className="flex-1 min-w-0">
              <div className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate">{assignedUser.fullName}</div>
              <div className="text-xs text-gray-500 dark:text-slate-400">{assignedUser.email}</div>
              <div className="text-xs text-gray-400 dark:text-slate-500 mt-0.5">{getRoleLabel(assignedUser.role)}</div>
            </div>
          </div>
        </div>
      )}

      {/* 满意度评价 */}
      {(satisfactionRating || chatSessionRating?.rating != null) && (
        <>
          {/* 来源1：模块二邮件满意度调查（SatisfactionRatings 表） */}
          {satisfactionRating && (
            <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4 relative overflow-hidden">
              <div className="absolute -top-10 -right-10 w-28 h-28 rounded-full bg-gradient-to-br from-emerald-200/40 dark:from-emerald-400/20 to-teal-200/40 dark:to-teal-400/20 blur-2xl pointer-events-none" />
              <div className="flex items-center justify-between mb-3 relative">
                <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2">
                  <svg className="w-4 h-4 text-emerald-500" fill="currentColor" viewBox="0 0 20 20">
                    <path d="M2.003 5.884L10 9.882l7.997-3.998A2 2 0 0016 4H4a2 2 0 00-1.997 1.884z" />
                    <path d="M18 8.118l-8 4-8-4V14a2 2 0 002 2h12a2 2 0 002-2V8.118z" />
                  </svg>
                  {t('conversations.sidebar.emailSatisfaction')}
                </h3>
                <span className={`text-[11px] px-2 py-0.5 rounded-full font-medium ${RATING_BADGE[satisfactionRating.ratingCategory] ?? 'bg-gray-100 text-gray-700 dark:bg-slate-700 dark:text-slate-300'}`}>
                  {satisfactionRating.ratingLabel}
                </span>
              </div>
              <div className="flex items-center gap-1 mb-3 relative">
                {[1, 2, 3, 4, 5].map((star) => (
                  <svg
                    key={star}
                    className={`w-5 h-5 ${star <= satisfactionRating.rating ? 'text-yellow-400 drop-shadow-[0_2px_4px_rgba(250,204,21,0.35)]' : 'text-gray-200 dark:text-slate-600'}`}
                    fill="currentColor"
                    viewBox="0 0 20 20"
                  >
                    <path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.07 3.292a1 1 0 00.95.69h3.462c.969 0 1.371 1.24.588 1.81l-2.8 2.034a1 1 0 00-.364 1.118l1.07 3.292c.3.921-.755 1.688-1.54 1.118l-2.8-2.034a1 1 0 00-1.175 0l-2.8 2.034c-.784.57-1.838-.197-1.539-1.118l1.07-3.292a1 1 0 00-.364-1.118L2.98 8.72c-.783-.57-.38-1.81.588-1.81h3.461a1 1 0 00.951-.69l1.07-3.292z" />
                  </svg>
                ))}
                <span className="ml-1.5 text-xs font-semibold text-slate-700 dark:text-slate-200">
                  {satisfactionRating.rating} / 5
                </span>
              </div>
              {satisfactionRating.comment && (
                <div className="bg-gray-50 dark:bg-slate-700/50 rounded-lg p-3 text-xs text-gray-700 dark:text-slate-300 italic border-l-2 border-emerald-300 dark:border-emerald-700/60 shadow-inner">
                  "{satisfactionRating.comment}"
                </div>
              )}
              <div className="mt-2.5 flex items-center justify-between text-[11px] text-gray-400 dark:text-slate-500">
                <div className="flex items-center gap-1">
                  <CalendarDays className="w-3 h-3" />
                  <span>{t('conversations.sidebar.ratedAt', { date: formatDateTime(satisfactionRating.createdAt) })}</span>
                </div>
              </div>
            </div>
          )}

          {/* 来源2：在线聊天直接评分（ChatSessions.Rating / RatingComment，聊天转工单后同步展示） */}
          {chatSessionRating?.rating != null && (
            <div className="bg-gradient-to-br from-white to-blue-50/60 dark:from-slate-800 dark:to-indigo-950/30 rounded-xl border border-slate-200 dark:border-indigo-900/40 p-4 relative overflow-hidden shadow-sm">
              <div className="absolute -top-10 -left-10 w-28 h-28 rounded-full bg-gradient-to-br from-indigo-300/40 dark:from-indigo-500/20 to-purple-300/40 dark:to-purple-500/20 blur-2xl pointer-events-none" />
              <div className="flex items-center justify-between mb-3 relative">
                <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2">
                  <span className="w-5 h-5 rounded-lg bg-gradient-to-br from-indigo-500 to-purple-500 flex items-center justify-center text-white shadow-sm shadow-indigo-500/40">
                    <MessageCircle className="w-3 h-3" strokeWidth={2.2} />
                  </span>
                  {t('conversations.sidebar.chatRating')}
                  <span className="text-[10px] px-1.5 py-0.5 rounded-md bg-indigo-100 dark:bg-indigo-900/40 text-indigo-700 dark:text-indigo-200 font-semibold">
                    {t('conversations.sidebar.chatToConversation')}
                  </span>
                </h3>
                <span className={`text-[11px] px-2.5 py-0.5 rounded-full font-bold ${
                  CHAT_RATING_CATEGORY[chatSessionRating.rating]?.badge ??
                  'bg-gray-100 text-gray-700 dark:bg-slate-700 dark:text-slate-300'
                }`}>
                  {CHAT_RATING_CATEGORY[chatSessionRating.rating]
                    ? t(CHAT_RATING_CATEGORY[chatSessionRating.rating].labelKey)
                    : t('conversations.sidebar.ratingStars', { rating: chatSessionRating.rating })}
                </span>
              </div>
              <div className="flex items-center gap-1 mb-3 relative">
                {[1, 2, 3, 4, 5].map((star) => {
                  const filled = star <= chatSessionRating.rating!;
                  return (
                    <Star
                      key={star}
                      className={`w-5 h-5 transition-all ${
                        filled
                          ? 'text-amber-400 fill-amber-400 drop-shadow-[0_3px_6px_rgba(251,191,36,0.45)]'
                          : 'text-slate-200 dark:text-slate-600'
                      }`}
                      strokeWidth={filled ? 0 : 1.6}
                    />
                  );
                })}
                <span className="ml-1.5 text-xs font-semibold text-slate-800 dark:text-slate-100 bg-white/70 dark:bg-slate-900/50 px-1.5 py-0.5 rounded-md border border-white/60 dark:border-slate-700/60">
                  {chatSessionRating.rating} / 5
                </span>
              </div>
              {chatSessionRating.ratingComment && (
                <div className="bg-white/80 dark:bg-slate-900/50 rounded-lg p-3 text-xs text-gray-700 dark:text-slate-200 italic border-l-2 border-amber-400/70 shadow-inner backdrop-blur-sm">
                  "{chatSessionRating.ratingComment}"
                </div>
              )}
              <div className="mt-2.5 flex items-center justify-between text-[11px] text-gray-400 dark:text-slate-500">
                <div className="flex items-center gap-1">
                  <CalendarDays className="w-3 h-3" />
                  <span>{t('conversations.sidebar.sessionUpdatedAt', { date: formatDateTime(chatSessionRating.updatedAt) })}</span>
                </div>
              </div>
            </div>
          )}
        </>
      )}
    </aside>
  );
}
