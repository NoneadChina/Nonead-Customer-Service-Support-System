import { useState, useEffect, useRef, useCallback, useMemo } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { conversationApi, profileApi, mailApi, satisfactionApi, weChatApi, whatsAppApi, feishuApi, dingdingApi, chatApi, messengerApi, telegramApi, lineApi, imessageApi } from '../api';
import type {
  ThreadResponse,
  MailboxResponse,
  UserResponse,
  SatisfactionRatingDto,
  ChatSessionDto,
} from '../api/types';
import { ThreadType } from '../api/types';
import { useI18n } from '../context/I18nContext';
import { getStatusLabel, getStatusColor, getPriorityLabel, getPriorityColor, getSourceTypeLabel, getSourceTypeColor, formatDateTime, sanitizeHtml, displayCustomerName } from '../utils/format';
import ThreadList from '../components/ThreadList';
import ReplyBox from '../components/ReplyBox';
import WeChatReplyBox from '../components/WeChatReplyBox';
import WhatsAppReplyBox from '../components/WhatsAppReplyBox';
import FeishuReplyBox from '../components/FeishuReplyBox';
import DingDingReplyBox from '../components/DingDingReplyBox';
import MessengerReplyBox from '../components/MessengerReplyBox';
import TelegramReplyBox from '../components/TelegramReplyBox';
import LineReplyBox from '../components/LineReplyBox';
import IMessageReplyBox from '../components/IMessageReplyBox';
import ConversationSidebar from '../components/ConversationSidebar';
import PriorityIcon from '../components/PriorityIcon';
import SourceTypeIcon from '../components/SourceTypeIcon';
import Modal from '../components/Modal';
import { useSignalREvent, useSignalRGroup } from '../hooks/useSignalR';
import { useAutoRefresh } from '../hooks/useAutoRefresh';
import { usePermission } from '../hooks/usePermission';
import { queryKeys, useConversation, useConversationThreads, useAllMailboxes, useAssignableUsers } from '../hooks/useConversations';
import { useToast } from '../context/ToastContext';
import { SignalREvents } from '../services/signalR';
import { Clock, AlertTriangle, CheckCircle2, ExternalLink } from 'lucide-react';

const escapeHtml = (str: string | undefined | null): string => {
  if (!str) return '';
  return str
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
};

export default function ConversationDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { t } = useI18n();
  const { isReadOnly, canAssignConversations } = usePermission();
  const queryClient = useQueryClient();
  const globalToast = useToast();

  /** 客服消息窗口状态 → 文案（微信 / WhatsApp 的 sr-only 描述共用） */
  const windowStatusLabel = useCallback(
    (status: 'ok' | 'warning' | 'expired' | 'unknown'): string => {
      if (status === 'ok') return t('conversations.detail.windowStatusOk');
      if (status === 'warning') return t('conversations.detail.windowStatusWarning');
      if (status === 'expired') return t('conversations.detail.expired');
      return t('format.unknown');
    },
    [t]
  );

  const convId = id ? Number(id) : 0;

  // React Query 数据获取
  const { data: conversation, isLoading: loading, error: queryError } = useConversation(convId || null);
  const { data: threadsData } = useConversationThreads(convId || null);
  const { data: mailboxesData } = useAllMailboxes();
  // 使用 /api/users/assignable（conversations.assign 权限）而非 /api/users（SuperAdmin），
  // 确保客服主管也能加载"分配负责人"下拉框（T000061）
  const { data: usersData } = useAssignableUsers();
  const { data: currentUser } = useQuery({
    queryKey: ['profile'],
    queryFn: () => profileApi.get(),
    staleTime: 5 * 60 * 1000,
  });

  const threads: ThreadResponse[] = useMemo(() => {
    if (threadsData) return Array.isArray(threadsData) ? threadsData : [];
    return conversation?.threads ?? [];
  }, [threadsData, conversation]);
  const mailboxes: MailboxResponse[] = mailboxesData
    ? (Array.isArray(mailboxesData) ? mailboxesData : [])
    : [];
  const users: UserResponse[] = usersData
    ? (Array.isArray(usersData) ? (usersData as UserResponse[]) : [])
    : [];

  const [updating, setUpdating] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [showNoteBox, setShowNoteBox] = useState(false);
  const [satisfactionRating, setSatisfactionRating] = useState<SatisfactionRatingDto | null>(null);
  const [chatSessionRating, setChatSessionRating] = useState<ChatSessionDto | null>(null);
  const [draft, setDraft] = useState<ThreadResponse | null>(null);

  // 转发相关
  const [forwardOpen, setForwardOpen] = useState(false);
  const [forwardTo, setForwardTo] = useState('');
  const [forwardNote, setForwardNote] = useState('');
  const [forwarding, setForwarding] = useState(false);
  const [forwardError, setForwardError] = useState('');

  // 操作提示
  const [toast, setToast] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [toastVisible, setToastVisible] = useState(false);
  const toastTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const toastInnerTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // 小屏幕操作按钮折叠
  const [actionsOpen, setActionsOpen] = useState(false);
  const actionsRef = useRef<HTMLDivElement>(null);

  // 线程列表滚动管理：长对话中显示"跳到最新消息"按钮 + 新消息自动滚动
  const threadListRef = useRef<HTMLDivElement | null>(null);
  const [showScrollBottom, setShowScrollBottom] = useState(false);
  const prevThreadCountRef = useRef(0);

  const handleThreadScroll = useCallback(() => {
    const el = threadListRef.current;
    if (!el) return;
    const distFromBottom = el.scrollHeight - el.scrollTop - el.clientHeight;
    setShowScrollBottom(distFromBottom > 200);
  }, []);

  // 新消息到达时：若用户当前在底部附近则自动滚动，否则仅显示提示按钮
  useEffect(() => {
    const el = threadListRef.current;
    if (!el) return;
    const wasNearBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 200;
    const hasNewMessages = threads.length > prevThreadCountRef.current;
    prevThreadCountRef.current = threads.length;
    if (hasNewMessages && wasNearBottom) {
      requestAnimationFrame(() => {
        el.scrollTop = el.scrollHeight;
      });
    }
  }, [threads.length]);

  // 进入会话详情时，后端 GET /api/conversations/{id} 已将该会话标记为已读。
  // 此处使列表缓存失效，确保用户返回列表页时未读徽章消失（列表查询会以 refetchOnMount 重新拉取）。
  useEffect(() => {
    if (!convId) return;
    queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
  }, [convId, queryClient]);

  const scrollToBottom = useCallback(() => {
    const el = threadListRef.current;
    if (!el) return;
    el.scrollTo({ top: el.scrollHeight, behavior: 'smooth' });
    setShowScrollBottom(false);
  }, []);

  const error = queryError instanceof Error ? queryError.message : '';

  // 微信会话标识：sourceType === 7 表示 WeChat
  const isWeChatConversation = conversation?.sourceType === 7;
  // 微信回复内容
  const [weChatReplyContent, setWeChatReplyContent] = useState('');
  // 微信回复框重置键：每次成功回复后递增以清空附件状态
  const [weChatReplyKey, setWeChatReplyKey] = useState(0);

  // 统一时间基准 now：定时器触发 setNow → useMemo 依赖 now → 重算窗口状态
  const [now, setNow] = useState(() => Date.now());

  // ---------- 微信窗口：客户最近互动时间戳（供定时器闭包读取最新值） ----------
  const weChatLastInteractionAtRef = useRef<number | null>(null);
  const weChatTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // 同步最新客户互动时间到 ref（threads 更新时触发，纯副作用）
  useEffect(() => {
    if (!isWeChatConversation) {
      weChatLastInteractionAtRef.current = null;
      return;
    }
    const customerThreads = threads.filter((t) => t.type === ThreadType.Customer);
    if (customerThreads.length === 0) {
      weChatLastInteractionAtRef.current = null;
      return;
    }
    const latest = customerThreads.reduce(
      (acc, t) => Math.max(acc, new Date(t.createdAt).getTime()),
      0
    );
    weChatLastInteractionAtRef.current = latest > 0 ? latest : null;
  }, [isWeChatConversation, threads]);
  // 链式 setTimeout 调度：每次根据当前 remaining 决定下一次延迟
  // 优势：不依赖 useMemo 写入 ref；同时同一时刻仅一个 pending timeout，不会泄漏
  useEffect(() => {
    if (!isWeChatConversation) return;
    const WINDOW_MS = 48 * 60 * 60 * 1000;
    const tick = () => {
      const nowMs = Date.now();
      setNow(nowMs);
      const lastAt = weChatLastInteractionAtRef.current;
      let delay = 60_000;
      if (lastAt != null) {
        const remainingMs = WINDOW_MS - (nowMs - lastAt);
        if (remainingMs <= 0) delay = 60_000; // 已过期，每分钟再检查
        else if (remainingMs < 60 * 60 * 1000) delay = 1000; // < 1h：秒级倒计时
        else delay = 60_000; // 默认分钟级
      }
      weChatTimerRef.current = setTimeout(tick, delay);
    };
    tick(); // 立即触发一次以校准 now
    return () => {
      if (weChatTimerRef.current != null) clearTimeout(weChatTimerRef.current);
    };
  }, [isWeChatConversation]);

  // 微信 48 小时客服消息窗口计算（纯 useMemo：不再写 ref）
  const weChatWindow = useMemo(() => {
    if (!isWeChatConversation) return null;
    const customerThreads = threads
      .filter((t) => t.type === ThreadType.Customer)
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
    if (customerThreads.length === 0) {
      return { status: 'unknown' as const, remainingMs: 0, lastInteraction: null as string | null, expireAt: null as string | null };
    }
    const lastInteraction = customerThreads[0].createdAt;
    const elapsed = now - new Date(lastInteraction).getTime();
    const windowMs = 48 * 60 * 60 * 1000;
    const remainingMs = windowMs - elapsed;
    const expireAt = new Date(new Date(lastInteraction).getTime() + windowMs).toISOString();
    if (remainingMs <= 0) return { status: 'expired' as const, remainingMs: 0, lastInteraction, expireAt };
    if (remainingMs <= 12 * 60 * 60 * 1000) return { status: 'warning' as const, remainingMs, lastInteraction, expireAt };
    return { status: 'ok' as const, remainingMs, lastInteraction, expireAt };
  }, [isWeChatConversation, threads, now]);

  // WhatsApp 会话标识：sourceType === 8 表示 WhatsApp
  const isWhatsAppConversation = conversation?.sourceType === 8;

  const [whatsAppReplyContent, setWhatsAppReplyContent] = useState('');
  // 用于回复后强制重置 WhatsAppReplyBox 内部状态
  const [whatsAppReplyKey, setWhatsAppReplyKey] = useState(0);

  // 飞书会话标识：sourceType === 9 表示飞书
  const isFeishuConversation = conversation?.sourceType === 9;
  const [feishuReplyContent, setFeishuReplyContent] = useState('');
  // 用于回复后强制重置 FeishuReplyBox 内部状态
  const [feishuReplyKey, setFeishuReplyKey] = useState(0);

  // 钉钉会话标识：sourceType === 10 表示钉钉
  const isDingDingConversation = conversation?.sourceType === 10;
  const [dingDingReplyContent, setDingDingReplyContent] = useState('');
  // 用于回复后强制重置 DingDingReplyBox 内部状态
  const [dingDingReplyKey, setDingDingReplyKey] = useState(0);

  // ---------- 新增四渠道（Messenger=11 / Telegram=12 / Line=13 / IMessage=14）----------
  // 说明：渠道常量必须与后端 Customer.Channel / ConversationRepository 的字面量一致，
  // 但前端这里只需要 sourceType 数字（与 ConversationDetail 其它渠道同一口径）。
  const isMessengerConversation = conversation?.sourceType === 11;
  const isTelegramConversation = conversation?.sourceType === 12;
  const isLineConversation = conversation?.sourceType === 13;
  const isIMessageConversation = conversation?.sourceType === 14;

  const [messengerReplyContent, setMessengerReplyContent] = useState('');
  const [messengerReplyKey, setMessengerReplyKey] = useState(0);
  const [telegramReplyContent, setTelegramReplyContent] = useState('');
  const [telegramReplyKey, setTelegramReplyKey] = useState(0);
  const [telegramParseMode, setTelegramParseMode] = useState<'none' | 'HTML' | 'Markdown'>('none');
  const [telegramQuoteLastCustomerMessage, setTelegramQuoteLastCustomerMessage] = useState(false);
  const [lineReplyContent, setLineReplyContent] = useState('');
  const [lineReplyKey, setLineReplyKey] = useState(0);
  const [iMessageReplyContent, setIMessageReplyContent] = useState('');
  const [iMessageReplyKey, setIMessageReplyKey] = useState(0);

  /**
   * 通用「客户最近互动 → 窗口状态」推导，供 Messenger(24h) / iMessage(168h) 复用。
   * 阈值语义与微信/钉钉一致：过期=expired，剩余不足窗口的 1/4=warning，否则 ok；
   * 会话里还没有客户消息时返回 unknown（不能假定窗口在走）。
   */
  const buildImWindow = (windowMs: number, enabled: boolean) => {
    if (!enabled) return null
    const customerThreads = threads
      .filter((t) => t.type === ThreadType.Customer)
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
    if (customerThreads.length === 0) {
      return { status: 'unknown' as const, remainingMs: 0, lastInteraction: null as string | null }
    }
    const lastInteraction = customerThreads[0].createdAt
    const remainingMs = windowMs - (now - new Date(lastInteraction).getTime())
    let status: 'ok' | 'warning' | 'expired'
    if (remainingMs <= 0) status = 'expired'
    else if (remainingMs < windowMs / 4) status = 'warning'
    else status = 'ok'
    return { status, remainingMs, lastInteraction }
  }

  const formatRemainingText = (remainingMs: number) => {
    const totalMinutes = Math.floor(remainingMs / 60000)
    const hours = Math.floor(totalMinutes / 60)
    const minutes = totalMinutes % 60
    if (hours > 0) return t('conversations.detail.remainingHoursMinutes', { hours, minutes })
    return t('conversations.detail.remainingMinutes', { minutes })
  }

  const whatsAppLikeWindow = (window: { status: 'ok' | 'warning' | 'expired' | 'unknown' } | null): 'ok' | 'warning' | 'expired' | 'unknown' | undefined => {
    if (!window) return undefined
    return window.status as 'ok' | 'warning' | 'expired' | 'unknown'
  }

  // Messenger 客服窗口长度以后台配置为准（默认 24h，可改到 168h）。
  // 旧实现写死 24h ⇒ 后台调成 72h 后前端仍显示「窗口已过期」，坐席误以为不能回复。
  const { data: messengerWindowConfig } = useQuery({
    queryKey: ['messenger', 'window-config'],
    queryFn: () => messengerApi.getSettings(),
    enabled: isMessengerConversation,
    staleTime: 5 * 60 * 1000,
  })
  const messengerWindowMs = (messengerWindowConfig?.windowHours ?? 24) * 60 * 60 * 1000
  const messengerWindow = useMemo(() => buildImWindow(messengerWindowMs, isMessengerConversation), [messengerWindowMs, isMessengerConversation, threads, now])

  const iMessageWindow = useMemo(() => buildImWindow(168 * 60 * 60 * 1000, isIMessageConversation), [isIMessageConversation, threads, now])

  // Telegram / LINE 的「不可达」与「人工接管」由后端写入的内部 Note 线程标记推导，
  // 无需新增数据库字段（后端刻意用线程标记建模，见 TelegramUnreachableState / LineService）。
  const noteBody = (marker: string) =>
    threads.some(
      (th) =>
        th.type === ThreadType.Note &&
        ((th.subject ?? '').includes(marker) || (th.body ?? '').includes(marker))
    )

  const telegramUnreachable = useMemo(
    () => (isTelegramConversation ? noteBody('telegram_unreachable_') : false),
    [isTelegramConversation, threads]
  )

  const lineUnreachable = useMemo(
    () => (isLineConversation ? noteBody('line-unreachable-') : false),
    [isLineConversation, threads]
  )

  /**
   * LINE 的一次性 replyToken（约 25s）与人工接管静默窗口都只存在在线索 Header 里，
   * 列表接口不下发 Header，因此这里按「最近一条客户消息时间」推导 token 窗口，
   * 推导不出来时如实返回 unknown（后端会在过期时自动降级为 push 并计入配额），
   * 不假装知道状态。人工接管则读取 line-takeover- 标记里的 untilUtc。
   */
  const lineReplyWindow = useMemo(() => {
    if (!isLineConversation) return { status: 'unknown' as const, remainingText: undefined as string | undefined }
    const customerThreads = threads
      .filter((th) => th.type === ThreadType.Customer)
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
    if (customerThreads.length === 0) return { status: 'unknown' as const, remainingText: undefined }
    const elapsedMs = now - new Date(customerThreads[0].createdAt).getTime()
    const ttlMs = 25 * 1000
    const remainingMs = ttlMs - elapsedMs
    if (remainingMs <= 0) return { status: 'expired' as const, remainingText: undefined }
    if (remainingMs < 5 * 1000) return { status: 'expiring' as const, remainingText: t('conversations.detail.remainingMinutes', { minutes: 0 }) }
    return { status: 'ok' as const, remainingText: undefined }
  }, [isLineConversation, threads, now, t])

  const lineHandover = useMemo(() => {
    if (!isLineConversation) return { active: false, remainingText: undefined as string | undefined }
    const marker = 'line-takeover-'
    const notes = threads
      .filter((th) => th.type === ThreadType.Note && ((th.subject ?? '').includes(marker) || (th.body ?? '').includes(marker)))
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
    if (notes.length === 0) return { active: false, remainingText: undefined }
    const raw = (notes[0].body ?? '') + ' ' + (notes[0].subject ?? '')
    const m = raw.match(/"untilUtc"s*:s*"([^"]+)"/)
    if (!m) return { active: false, remainingText: undefined }
    const until = Date.parse(m[1].replace(/\\/g, ''))
    if (!Number.isFinite(until) || until <= now) return { active: false, remainingText: undefined }
    return { active: true, remainingText: formatRemainingText(until - now) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isLineConversation, threads, now, t])

  // 钉钉 sessionWebhook 72 小时窗口计算
  const dingDingWindow = useMemo(() => {
    if (!isDingDingConversation) return null;
    const windowMs = 72 * 60 * 60 * 1000;
    const customerThreads = threads
      .filter((t) => t.type === ThreadType.Customer)
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
    if (customerThreads.length === 0) {
      return { status: 'unknown' as const, remainingMs: 0, lastInteraction: null as string | null };
    }
    const lastInteraction = customerThreads[0].createdAt;
    const elapsedMs = now - new Date(lastInteraction).getTime();
    const remainingMs = windowMs - elapsedMs;
    let status: 'ok' | 'warning' | 'expired';
    if (remainingMs <= 0) status = 'expired';
    else if (remainingMs < 6 * 60 * 60 * 1000) status = 'warning'; // 剩余 < 6 小时
    else status = 'ok';
    return { status, remainingMs, lastInteraction };
  }, [isDingDingConversation, threads, now]);

  // 钉钉窗口剩余时间格式化
  const dingDingWindowRemainingText = useMemo(() => {
    if (!dingDingWindow || dingDingWindow.status === 'expired' || dingDingWindow.status === 'unknown') return undefined;
    const totalMinutes = Math.floor(dingDingWindow.remainingMs / (60 * 1000));
    const hours = Math.floor(totalMinutes / 60);
    const minutes = totalMinutes % 60;
    if (hours > 0) return t('conversations.detail.remainingHoursMinutes', { hours, minutes });
    return t('conversations.detail.remainingMinutes', { minutes });
  }, [dingDingWindow, t]);

  // ---------- WhatsApp 窗口（24h）：同微信的 ref 同步 + chained setTimeout 模式 ----------
  const whatsAppLastInteractionAtRef = useRef<number | null>(null);
  const whatsAppTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => {
    if (!isWhatsAppConversation) {
      whatsAppLastInteractionAtRef.current = null;
      return;
    }
    const customerThreads = threads.filter((t) => t.type === ThreadType.Customer);
    if (customerThreads.length === 0) {
      whatsAppLastInteractionAtRef.current = null;
      return;
    }
    const latest = customerThreads.reduce(
      (acc, t) => Math.max(acc, new Date(t.createdAt).getTime()),
      0
    );
    whatsAppLastInteractionAtRef.current = latest > 0 ? latest : null;
  }, [isWhatsAppConversation, threads]);
  useEffect(() => {
    if (!isWhatsAppConversation) return;
    const WINDOW_MS = 24 * 60 * 60 * 1000;
    const tick = () => {
      const nowMs = Date.now();
      setNow(nowMs);
      const lastAt = whatsAppLastInteractionAtRef.current;
      let delay = 60_000;
      if (lastAt != null) {
        const remainingMs = WINDOW_MS - (nowMs - lastAt);
        if (remainingMs <= 0) delay = 60_000;
        else if (remainingMs < 60 * 60 * 1000) delay = 1000; // < 1h 秒级倒计时
        else delay = 60_000;
      }
      whatsAppTimerRef.current = setTimeout(tick, delay);
    };
    tick();
    return () => {
      if (whatsAppTimerRef.current != null) clearTimeout(whatsAppTimerRef.current);
    };
  }, [isWhatsAppConversation]);

  // WhatsApp 24 小时客服消息窗口计算（纯 useMemo：不再写 ref）
  const whatsAppWindow = useMemo(() => {
    if (!isWhatsAppConversation) return null;
    const windowMs = 24 * 60 * 60 * 1000;
    const customerThreads = threads
      .filter((t) => t.type === ThreadType.Customer)
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
    if (customerThreads.length === 0) {
      return { status: 'unknown' as const, remainingMs: 0, lastInteraction: null as string | null, expireAt: null as string | null };
    }
    const lastInteraction = customerThreads[0].createdAt;
    const elapsedMs = now - new Date(lastInteraction).getTime();
    const remainingMs = windowMs - elapsedMs;
    const expireAt = new Date(new Date(lastInteraction).getTime() + windowMs).toISOString();
    let status: 'ok' | 'warning' | 'expired';
    if (remainingMs <= 0) status = 'expired';
    else if (remainingMs < 2 * 60 * 60 * 1000) status = 'warning'; // 剩余 < 2 小时
    else status = 'ok';
    return { status, remainingMs, lastInteraction, expireAt };
  }, [isWhatsAppConversation, threads, now]);

  // 格式化剩余时间（如 "23 小时 45 分钟"，秒级精度临近过期时显示秒）
  const formatRemaining = (ms: number): string => {
    if (ms <= 0) return t('conversations.detail.expired');
    const hours = Math.floor(ms / (60 * 60 * 1000));
    const minutes = Math.floor((ms % (60 * 60 * 1000)) / (60 * 1000));
    const seconds = Math.floor((ms % (60 * 1000)) / 1000);
    if (hours > 0) {
      return minutes > 0
        ? t('conversations.detail.remainingHoursMinutes', { hours, minutes })
        : t('conversations.detail.remainingHours', { hours });
    }
    if (minutes > 0) return t('conversations.detail.remainingMinutes', { minutes });
    return t('conversations.detail.remainingSeconds', { seconds });
  };

  // 加载满意度评价和草稿（不阻塞主流程）
  useEffect(() => {
    if (!convId) return;
    let cancelled = false;
    satisfactionApi.getByConversation(convId).then((data) => { if (!cancelled) setSatisfactionRating(data); }).catch((err: unknown) => console.error('加载满意度评价失败:', err));
    chatApi
      .getSessionByConversation(convId)
      .then((s) => { if (!cancelled) setChatSessionRating(s ?? null); })
      .catch((err: unknown) => console.error('加载在线聊天评分信息失败:', err));
    conversationApi.getDraft(convId).then((data) => { if (!cancelled) setDraft(data); }).catch(() => { if (!cancelled) setDraft(null); });
    return () => { cancelled = true; };
  }, [convId]);

  // 加入会话 SignalR 组以接收实时更新
  useSignalRGroup(convId ? `conv_${convId}` : null);

  // 监听新回复事件，自动刷新线程列表
  useSignalREvent('reply_created', (data: { conversationId: number }) => {
    if (data.conversationId === convId) {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.detail(convId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
    }
  });

  useSignalREvent('note_created', (data: { conversationId: number }) => {
    if (data.conversationId === convId) {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.detail(convId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
    }
  });

  // 监听满意度评价提交事件，刷新侧边栏满意度数据
  useSignalREvent(SignalREvents.SatisfactionUpdated, (data: { conversationId?: number }) => {
    if (!convId) return;
    // 邮件/IM 渠道的 SatisfactionUpdated 携带 conversationId；在线聊天的可能不携带，统一刷新
    if (!data?.conversationId || data.conversationId === convId) {
      satisfactionApi.getByConversation(convId).then((d) => setSatisfactionRating(d)).catch(() => {});
      chatApi.getSessionByConversation(convId).then((s) => setChatSessionRating(s ?? null)).catch(() => {});
    }
  });

  // 根据用户偏好自动刷新会话详情
  const refreshConversation = useCallback(() => {
    if (!convId) return;
    queryClient.invalidateQueries({ queryKey: queryKeys.conversations.detail(convId) });
    queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
  }, [convId, queryClient]);

  useAutoRefresh(refreshConversation, [convId]);

  const handleToggleFollow = async () => {
    if (!convId) return;
    setUpdating(true);
    try {
      const updated = await conversationApi.toggleFollow(convId);
      if (updated) {
        queryClient.setQueryData(queryKeys.conversations.detail(convId), updated);
      }
      const nowFollowing = currentUser?.id !== undefined && updated?.followerIds?.includes(currentUser.id) || false;
      setToast({
        type: 'success',
        text: nowFollowing ? t('conversations.detail.followed') : t('conversations.detail.unfollowed'),
      });
    } catch (err: unknown) {
      console.error(err);
      setToast({ type: 'error', text: err instanceof Error ? err.message : t('conversations.detail.operationFailed') });
    } finally {
      setUpdating(false);
    }
  };

  const handleStatusChange = async (status: number) => {
    if (!convId || !conversation) return;
    setUpdating(true);
    try {
      const updated = await conversationApi.updateStatus(convId, status);
      if (updated) {
        queryClient.setQueryData(queryKeys.conversations.detail(convId), updated);
      }
      setToast({ type: 'success', text: t('conversations.detail.statusUpdated', { status: getStatusLabel(status) }) });
    } catch (err: unknown) {
      console.error(err);
      setToast({ type: 'error', text: err instanceof Error ? err.message : t('conversations.detail.statusUpdateFailed') });
    } finally {
      setUpdating(false);
    }
  };

  const handlePriorityChange = async (priority: number) => {
    if (!convId || !conversation) return;
    setUpdating(true);
    try {
      const updated = await conversationApi.updatePriority(convId, priority);
      if (updated) {
        queryClient.setQueryData(queryKeys.conversations.detail(convId), updated);
      }
      setToast({ type: 'success', text: t('conversations.detail.priorityUpdated', { priority: getPriorityLabel(priority) }) });
    } catch (err: unknown) {
      console.error(err);
      setToast({ type: 'error', text: err instanceof Error ? err.message : t('conversations.detail.priorityUpdateFailed') });
    } finally {
      setUpdating(false);
    }
  };

  const handleAssign = async (userId: number) => {
    if (!convId) return;
    setUpdating(true);
    try {
      const updated = await conversationApi.assign(convId, userId);
      if (updated) {
        queryClient.setQueryData(queryKeys.conversations.detail(convId), updated);
      }
      const assignedUser = users.find(u => u.id === userId);
      const userName =
        assignedUser?.fullName || (userId === 0 ? t('format.unassigned') : t('common.layout.unknownUser'));
      setToast({
        type: 'success',
        text: userId === 0 ? t('conversations.detail.unassigned') : t('conversations.detail.assignedTo', { name: userName }),
      });
    } catch (err: unknown) {
      console.error(err);
      setToast({ type: 'error', text: err instanceof Error ? err.message : t('conversations.detail.assignFailed') });
    } finally {
      setUpdating(false);
    }
  };

  const handleReply = async (body: string, attachmentIds?: number[], cc?: string, bcc?: string) => {
    if (!convId || !currentUser) return;
    setSubmitting(true);
    try {
      const newThread = await conversationApi.reply(convId, body, currentUser.email, attachmentIds, cc, bcc);
      if (newThread) {
        queryClient.setQueryData<ThreadResponse[] | undefined>(queryKeys.conversations.threads(convId), (old) => {
          const oldThreads = old ?? [];
          return [...oldThreads, newThread];
        });
      }
      const updated = await conversationApi.get(convId);
      if (updated) {
        queryClient.setQueryData(queryKeys.conversations.detail(convId), updated);
      }
      // 回复成功后清理草稿
      conversationApi.deleteDraft(convId).then(() => setDraft(null)).catch((err: unknown) => console.error('删除草稿失败:', err));
    } catch (err: unknown) {
      globalToast.error(err instanceof Error ? err.message : t('conversations.detail.replyFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  // 微信客服消息回复（48 小时窗口内有效）
  const handleWeChatReply = async (attachmentIds?: number[]) => {
    if (!convId) return;
    const content = weChatReplyContent.trim();
    if (!content && (!attachmentIds || attachmentIds.length === 0)) {
      globalToast.error(t('conversations.detail.emptyReply'));
      return;
    }
    setSubmitting(true);
    try {
      const result = await weChatApi.reply({ conversationId: convId, content, attachmentIds });
      if (!result.sent) {
        globalToast.error(result.errorMessage ?? t('conversations.detail.wechatSendFailed'));
        return;
      }
      // 消息已发送但线程记录创建失败（HTTP 207）→ 提示用户数据可能不一致
      if (result.threadCreated === false) {
        globalToast.warning(
          result.errorMessage
            ? t('conversations.detail.threadCreateFailedWithReason', { message: result.errorMessage })
            : t('conversations.detail.threadCreateFailed')
        );
      } else if (result.errorMessage) {
        // 部分附件发送失败时提示但不算失败
        globalToast.warning(t('conversations.detail.partialSend', { message: result.errorMessage }));
      }
      setWeChatReplyContent('');
      setWeChatReplyKey((k) => k + 1);
      // 线程记录创建成功才刷新会话详情展示新回复；否则等待用户手动刷新
      if (result.threadCreated !== false) {
        const updated = await conversationApi.get(convId);
        if (updated) {
          queryClient.setQueryData(queryKeys.conversations.detail(convId), updated);
        }
        await queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
        setToast({ type: 'success', text: t('conversations.detail.wechatSent') });
      }
    } catch (err: unknown) {
      globalToast.error(err instanceof Error ? err.message : t('conversations.detail.wechatFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  // WhatsApp 24 小时窗口内回复
  const handleWhatsAppReply = async (attachmentIds?: number[]) => {
    if (!convId) return;
    const content = whatsAppReplyContent.trim();
    if (!content && (!attachmentIds || attachmentIds.length === 0)) {
      globalToast.error(t('conversations.detail.emptyReply'));
      return;
    }
    setSubmitting(true);
    try {
      const result = await whatsAppApi.reply({
        conversationId: convId,
        content,
        attachmentIds,
      });
      if (!result.sent) {
        globalToast.error(result.errorMessage ?? t('conversations.detail.whatsAppSendFailed'));
        return;
      }
      // 消息已发送但线程记录创建失败（HTTP 207）→ 提示用户数据可能不一致
      if (result.threadCreated === false) {
        globalToast.warning(
          result.errorMessage
            ? t('conversations.detail.threadCreateFailedWithReason', { message: result.errorMessage })
            : t('conversations.detail.threadCreateFailed')
        );
      } else if (result.errorMessage) {
        // 部分附件/子步骤发送失败时提示但不算失败
        globalToast.warning(t('conversations.detail.partialSend', { message: result.errorMessage }));
      }
      setWhatsAppReplyContent('');
      setWhatsAppReplyKey((k) => k + 1);
      // 线程记录创建成功才刷新；失败则提示用户手动刷新
      if (result.threadCreated !== false) {
        const updated = await conversationApi.get(convId);
        if (updated) {
          queryClient.setQueryData(queryKeys.conversations.detail(convId), updated);
        }
        await queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
        setToast({ type: 'success', text: t('conversations.detail.whatsAppSent') });
      }
    } catch (err: unknown) {
      globalToast.error(err instanceof Error ? err.message : t('conversations.detail.whatsAppFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  // 飞书回复
  const handleFeishuReply = async (attachmentIds?: number[]) => {
    if (!convId) return;
    const content = feishuReplyContent.trim();
    if (!content && (!attachmentIds || attachmentIds.length === 0)) {
      globalToast.error(t('conversations.detail.emptyReply'));
      return;
    }
    setSubmitting(true);
    try {
      const result = await feishuApi.reply({
        conversationId: convId,
        content,
        attachmentIds,
      });
      if (!result.sent) {
        globalToast.error(result.errorMessage ?? t('conversations.detail.feishuSendFailed'));
        return;
      }
      setFeishuReplyContent('');
      setFeishuReplyKey((k) => k + 1);
      // 刷新会话和线程列表以显示新回复
      const updated = await conversationApi.get(convId);
      if (updated) {
        queryClient.setQueryData(queryKeys.conversations.detail(convId), updated);
      }
      await queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
      setToast({ type: 'success', text: t('conversations.detail.feishuSent') });
    } catch (err: unknown) {
      globalToast.error(err instanceof Error ? err.message : t('conversations.detail.feishuFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  // 钉钉回复
  const handleDingDingReply = async (attachmentIds?: number[]) => {
    if (!convId) return;
    const content = dingDingReplyContent.trim();
    if (!content && (!attachmentIds || attachmentIds.length === 0)) {
      globalToast.error(t('conversations.detail.emptyReply'));
      return;
    }
    setSubmitting(true);
    try {
      const result = await dingdingApi.reply({
        conversationId: convId,
        content,
        attachmentIds,
      });
      if (!result.sent) {
        globalToast.error(result.errorMessage ?? t('conversations.detail.dingtalkSendFailed'));
        return;
      }
      setDingDingReplyContent('');
      setDingDingReplyKey((k) => k + 1);
      // 刷新会话和线程列表以显示新回复
      const updated = await conversationApi.get(convId);
      if (updated) {
        queryClient.setQueryData(queryKeys.conversations.detail(convId), updated);
      }
      await queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
      setToast({ type: 'success', text: t('conversations.detail.dingtalkSent') });
    } catch (err: unknown) {
      globalToast.error(err instanceof Error ? err.message : t('conversations.detail.dingtalkFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  // Messenger 回复（24h 窗口外由后端按 MessageTagPolicy 处理）
  const handleMessengerReply = async (attachmentIds?: number[]) => {
    if (!convId) return;
    const content = messengerReplyContent.trim();
    if (!content && (!attachmentIds || attachmentIds.length === 0)) {
      globalToast.error(t('conversations.detail.emptyReply'));
      return;
    }
    setSubmitting(true);
    try {
      const result = await messengerApi.reply({
        conversationId: convId,
        content,
        attachmentIds,
      });
      if (!result.sent) {
        globalToast.error(result.errorMessage ?? t('conversations.detail.messengerSendFailed'));
        return;
      }
      // 后端 207 语义：消息已发出但线程落库失败（threadCreated=false）。
      // 此时不能清草稿，否则刷新后回复从会话记录里消失，坐席以为发成功了。
      if (result.threadCreated === false) {
        globalToast.error(t('conversations.detail.messengerSentNoThread'));
        await queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
        return;
      }
      setMessengerReplyContent('');
      setMessengerReplyKey((k) => k + 1);
      const updated = await conversationApi.get(convId);
      if (updated) {
        queryClient.setQueryData(queryKeys.conversations.detail(convId), updated);
      }
      await queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
      setToast({ type: 'success', text: t('conversations.detail.messengerSent') });
    } catch (err: unknown) {
      globalToast.error(err instanceof Error ? err.message : t('conversations.detail.messengerFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  // Telegram 回复
  const handleTelegramReply = async (attachmentIds?: number[]) => {
    if (!convId) return;
    const content = telegramReplyContent.trim();
    if (!content && (!attachmentIds || attachmentIds.length === 0)) {
      globalToast.error(t('conversations.detail.emptyReply'));
      return;
    }
    setSubmitting(true);
    try {
      const result = await telegramApi.reply({
        conversationId: convId,
        content,
        attachmentIds,
        parseMode: telegramParseMode === 'none' ? undefined : telegramParseMode,
        quoteLastCustomerMessage: telegramQuoteLastCustomerMessage,
      });
      if (!result.sent) {
        globalToast.error(result.errorMessage ?? t('conversations.detail.telegramSendFailed'));
        return;
      }
      setTelegramReplyContent('');
      setTelegramReplyKey((k) => k + 1);
      const updated = await conversationApi.get(convId);
      if (updated) {
        queryClient.setQueryData(queryKeys.conversations.detail(convId), updated);
      }
      await queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
      setToast({ type: 'success', text: t('conversations.detail.telegramSent') });
    } catch (err: unknown) {
      globalToast.error(err instanceof Error ? err.message : t('conversations.detail.telegramFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  // 清除 Telegram「用户已屏蔽机器人」标记，允许客服再次尝试
  const handleTelegramClearUnreachable = async () => {
    if (!convId) return;
    try {
      await telegramApi.clearUnreachable({ conversationId: convId });
      await queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
      setToast({ type: 'success', text: t('conversations.detail.telegramUnreachableCleared') });
    } catch (err: unknown) {
      globalToast.error(err instanceof Error ? err.message : t('conversations.detail.telegramFailed'));
    }
  };

  // LINE 回复（replyToken 过期时后端自动降级为 push 并消耗配额）
  const handleLineReply = async (attachmentIds?: number[]) => {
    if (!convId) return;
    const content = lineReplyContent.trim();
    if (!content && (!attachmentIds || attachmentIds.length === 0)) {
      globalToast.error(t('conversations.detail.emptyReply'));
      return;
    }
    setSubmitting(true);
    try {
      const result = await lineApi.reply({
        conversationId: convId,
        content,
        attachmentIds,
      });
      if (!result.sent) {
        globalToast.error(result.errorMessage ?? t('conversations.detail.lineSendFailed'));
        return;
      }
      setLineReplyContent('');
      setLineReplyKey((k) => k + 1);
      const updated = await conversationApi.get(convId);
      if (updated) {
        queryClient.setQueryData(queryKeys.conversations.detail(convId), updated);
      }
      await queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
      setToast({ type: 'success', text: t('conversations.detail.lineSent') });
    } catch (err: unknown) {
      globalToast.error(err instanceof Error ? err.message : t('conversations.detail.lineFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  // LINE 人工接管 / 解除接管（接管期间后端抑制自动回复）
  const handleLineTakeover = async () => {
    if (!convId) return;
    try {
      await lineApi.takeover({ conversationId: convId });
      await queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
      setToast({ type: 'success', text: t('conversations.detail.lineTakeoverOn') });
    } catch (err: unknown) {
      globalToast.error(err instanceof Error ? err.message : t('conversations.detail.lineFailed'));
    }
  };

  const handleLineReleaseTakeover = async () => {
    if (!convId) return;
    try {
      await lineApi.releaseTakeover(convId);
      await queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
      setToast({ type: 'success', text: t('conversations.detail.lineTakeoverOff') });
    } catch (err: unknown) {
      globalToast.error(err instanceof Error ? err.message : t('conversations.detail.lineFailed'));
    }
  };

  // iMessage 回复（桥接/AMB 由后端按配置模式选择，前端不感知）
  const handleIMessageReply = async (attachmentIds?: number[]) => {
    if (!convId) return;
    const content = iMessageReplyContent.trim();
    if (!content && (!attachmentIds || attachmentIds.length === 0)) {
      globalToast.error(t('conversations.detail.emptyReply'));
      return;
    }
    setSubmitting(true);
    try {
      const result = await imessageApi.reply({
        conversationId: convId,
        content,
        attachmentIds,
      });
      if (!result.sent) {
        globalToast.error(result.errorMessage ?? t('conversations.detail.iMessageSendFailed'));
        return;
      }
      setIMessageReplyContent('');
      setIMessageReplyKey((k) => k + 1);
      const updated = await conversationApi.get(convId);
      if (updated) {
        queryClient.setQueryData(queryKeys.conversations.detail(convId), updated);
      }
      await queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
      setToast({ type: 'success', text: t('conversations.detail.iMessageSent') });
    } catch (err: unknown) {
      globalToast.error(err instanceof Error ? err.message : t('conversations.detail.iMessageFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  const handleSaveDraft = async (body: string, attachmentIds?: number[], cc?: string, bcc?: string) => {
    if (!convId || !currentUser) return;
    try {
      const saved = await conversationApi.saveDraft(convId, body, currentUser.email, attachmentIds, cc, bcc);
      setDraft(saved);
      setToast({ type: 'success', text: t('conversations.detail.draftSaved') });
    } catch (err: unknown) {
      setToast({ type: 'error', text: err instanceof Error ? err.message : t('conversations.detail.draftSaveFailed') });
      throw err;
    }
  };

  const handleAddNote = async (body: string, attachmentIds?: number[]) => {
    if (!convId) return;
    setSubmitting(true);
    try {
      const newThread = await conversationApi.addNote(convId, body, attachmentIds);
      if (newThread) {
        queryClient.setQueryData<ThreadResponse[] | undefined>(queryKeys.conversations.threads(convId), (old) => {
          const oldThreads = old ?? [];
          return [...oldThreads, newThread];
        });
      }
      setShowNoteBox(false);
    } catch (err: unknown) {
      globalToast.error(err instanceof Error ? err.message : t('conversations.detail.addNoteFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  // 转发会话
  const handleForward = async () => {
    if (!convId || !conversation) return;
    if (!forwardTo.trim()) {
      setForwardError(t('conversations.detail.forwardRecipientRequired'));
      return;
    }
    // 简单邮箱格式校验
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(forwardTo.trim())) {
      setForwardError(t('conversations.detail.forwardRecipientInvalid'));
      return;
    }
    setForwarding(true);
    setForwardError('');
    try {
      // 拼接转发邮件内容：原会话信息 + 所有线程
      const lines: string[] = [];
      lines.push('<div style="font-family: sans-serif; font-size: 14px; color: #374151;">');
      lines.push(`<p>${t('conversations.detail.forwardBodyGreeting')}</p>`);
      if (forwardNote.trim()) {
        lines.push(`<p>${escapeHtml(forwardNote).replace(/\n/g, '<br/>')}</p>`);
      }
      lines.push('<hr style="border: 0; border-top: 1px solid #e5e7eb; margin: 20px 0;" />');
      lines.push(`<p style="color: #6b7280; font-size: 12px;">${t('conversations.detail.forwardBodyHeading')}</p>`);
      lines.push(
        `<p><strong>${t('conversations.detail.forwardBodySubject')}</strong>${escapeHtml(conversation.subject) || t('conversations.shared.noSubject')}</p>`
      );
      lines.push(`<p><strong>${t('conversations.detail.forwardBodyCustomer')}</strong>${escapeHtml(conversation.customerEmail)}</p>`);
      if (conversation.user) {
        lines.push(`<p><strong>${t('conversations.detail.forwardBodyAssignee')}</strong>${escapeHtml(conversation.user.fullName)}</p>`);
      }
      lines.push(`<p><strong>${t('conversations.detail.forwardBodyCreatedAt')}</strong>${formatDateTime(conversation.createdAt)}</p>`);
      lines.push('<hr style="border: 0; border-top: 1px solid #e5e7eb; margin: 20px 0;" />');
      const sorted = threads.slice().sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime());
      sorted.forEach((thread, i) => {
        const author = escapeHtml(thread.user?.fullName || thread.from || t('format.customerFallback'));
        const type =
          thread.type === 1
            ? t('conversations.detail.threadTypeCustomer')
            : thread.type === 2
            ? t('conversations.detail.threadTypeReply')
            : thread.type === 3
            ? t('conversations.detail.threadTypeNote')
            : t('conversations.detail.threadTypeSystem');
        lines.push(`<div style="margin-bottom: 16px; padding: 12px; background: #f9fafb; border-radius: 8px;">`);
        lines.push(`<div style="font-size: 12px; color: #6b7280; margin-bottom: 4px;">#${i + 1} ${author} · ${type} · ${formatDateTime(thread.createdAt)}</div>`);
        lines.push(`<div>${sanitizeHtml(thread.body)}</div>`);
        lines.push('</div>');
      });
      lines.push('</div>');
      const body = lines.join('\n');
      const subject = t('conversations.detail.forwardSubjectPrefix', {
        subject: conversation.subject || t('conversations.shared.noSubject'),
      });
      await mailApi.send(conversation.mailboxId, forwardTo.trim(), subject, body);
      setToast({ type: 'success', text: t('conversations.detail.forwardSuccess', { email: forwardTo }) });
      setForwardOpen(false);
      setForwardTo('');
      setForwardNote('');
    } catch (err: unknown) {
      setForwardError(err instanceof Error ? err.message : t('conversations.detail.forwardFailed'));
    } finally {
      setForwarding(false);
    }
  };

  // 打印会话
  const handlePrint = () => {
    window.print();
  };

  // 克隆会话
  const handleClone = async () => {
    if (!convId) return;
    if (!confirm(t('conversations.detail.cloneConfirm'))) return;
    try {
      const cloned = await conversationApi.clone(convId);
      setToast({ type: 'success', text: t('conversations.detail.cloneSuccess') });
      navigate(`/conversations/${cloned.id}`);
    } catch (err: unknown) {
      setToast({ type: 'error', text: err instanceof Error ? err.message : t('conversations.detail.cloneFailed') });
    }
  };

  // toast 显示/隐藏动画控制
  useEffect(() => {
    if (toast) {
      // 先设为可见以触发入场动画
      requestAnimationFrame(() => setToastVisible(true));
      if (toastTimerRef.current) clearTimeout(toastTimerRef.current);
      toastTimerRef.current = setTimeout(() => {
        setToastVisible(false);
        // 等待退场动画完成后再移除
        if (toastInnerTimerRef.current) clearTimeout(toastInnerTimerRef.current);
        toastInnerTimerRef.current = setTimeout(() => setToast(null), 300);
      }, 3700);
      return () => {
        if (toastTimerRef.current) clearTimeout(toastTimerRef.current);
        if (toastInnerTimerRef.current) clearTimeout(toastInnerTimerRef.current);
      };
    } else {
      setToastVisible(false);
    }
  }, [toast]);

  // 点击外部关闭操作按钮下拉
  useEffect(() => {
    if (!actionsOpen) return;
    const handleClickOutside = (e: MouseEvent) => {
      if (actionsRef.current && !actionsRef.current.contains(e.target as Node)) {
        setActionsOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [actionsOpen]);

  // 判断当前用户是否关注了该会话（通过关注者列表判断）
  const isFollowing = conversation && currentUser
    ? conversation.followerIds?.includes(currentUser.id) || false
    : false;

  if (loading) {
    return (
      <div
        className="flex items-center justify-center h-64"
        role="status"
        aria-live="polite"
        aria-busy="true"
      >
        <svg
          className="animate-spin h-8 w-8 text-primary-600 dark:text-primary-400"
          xmlns="http://www.w3.org/2000/svg"
          fill="none"
          viewBox="0 0 24 24"
          aria-hidden="true"
        >
          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
        </svg>
        <span className="sr-only">{t('conversations.detail.loading')}</span>
      </div>
    );
  }

  if (error || !conversation) {
    return (
      <div className="max-w-2xl mx-auto">
        <div className="bg-red-50 dark:bg-red-900/30 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 px-4 py-3 rounded-lg mb-4 flex items-center gap-2">
          <svg className="w-5 h-5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
          </svg>
          {error || t('conversations.detail.notFound')}
        </div>
        <Link
          to="/conversations"
          className="text-primary-600 dark:text-primary-400 hover:text-primary-700 dark:hover:text-primary-300 text-sm inline-flex items-center gap-1"
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M15 19l-7-7 7-7" />
          </svg>
          {t('conversations.detail.backToList')}
        </Link>
      </div>
    );
  }

  const mailbox = mailboxes.find((m) => m.id === conversation.mailboxId);

  return (
    <div className="space-y-4">
      {/* 操作提示 toast */}
      {toast && (
        <div
          className={`fixed top-4 right-4 z-50 px-4 py-3 rounded-lg shadow-lg text-sm flex items-center gap-2 transition-all duration-300 ${
            toastVisible ? 'opacity-100 translate-x-0' : 'opacity-0 translate-x-8'
          } ${toast.type === 'success' ? 'bg-green-600 dark:bg-green-700 text-white' : 'bg-red-600 dark:bg-red-700 text-white'}`}
        >
          {toast.type === 'success' ? (
            <svg className="w-5 h-5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          ) : (
            <svg className="w-5 h-5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M10 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2m7-2a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          )}
          {toast.text}
        </div>
      )}

      {/* 顶部面包屑和返回 */}
      <div className="flex items-center justify-between no-print">
        <div className="flex items-center gap-2 text-sm">
          <Link
            to="/conversations"
            className="text-gray-500 dark:text-slate-400 hover:text-primary-600 dark:hover:text-primary-400 inline-flex items-center gap-1"
          >
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M15 19l-7-7 7-7" />
            </svg>
            {t('conversations.detail.breadcrumbList')}
          </Link>
          <span className="text-gray-300 dark:text-slate-600">/</span>
          <span className="text-gray-700 dark:text-slate-300 font-medium">#{conversation.id}</span>
        </div>
      </div>

      {/* 头部信息卡片 - 渐变背景（微信会话使用绿色主题）*/}
      <div className={`rounded-xl overflow-hidden shadow-md border ${
        isWeChatConversation
          ? 'bg-gradient-to-r from-green-50 to-emerald-50 dark:from-green-900/30 dark:to-emerald-900/30 border-green-100 dark:border-green-800/50'
          : 'bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-primary-900/30 dark:to-indigo-900/30 border-primary-100 dark:border-primary-800/50'
      }`}>
        <div className="px-4 sm:px-5 pt-4 sm:pt-5 pb-3 sm:pb-4">
          <div className="flex items-start justify-between gap-3 sm:gap-4">
            <div className="flex-1 min-w-0">
              <div className="flex items-center gap-3 flex-wrap">
                {isWeChatConversation && (
                  <div className="flex-shrink-0 p-1.5 bg-green-500 rounded-lg shadow-sm shadow-green-500/30" title={t('conversations.detail.wechatServiceAccount')} role="img" aria-label={t('conversations.detail.wechatServiceAccount')}>
                    <SourceTypeIcon sourceType={7} className="w-4 h-4 text-white" title={t('conversations.detail.wechatServiceAccount')} />
                  </div>
                )}
                <h1 className="text-xl font-bold text-slate-800 dark:text-white truncate">
                  {conversation.subject || t('conversations.shared.noSubject')}
                </h1>
                <span className={`inline-flex px-2.5 py-0.5 rounded-full text-xs font-medium border ${getStatusColor(conversation.status)}`}>
                  {getStatusLabel(conversation.status)}
                </span>
                {conversation.priority !== 1 && (
                  <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-medium border ${getPriorityColor(conversation.priority)}`}>
                    <PriorityIcon priority={conversation.priority} className="w-3 h-3" />
                    {getPriorityLabel(conversation.priority)}
                  </span>
                )}
                {conversation.sourceType && conversation.sourceType !== 1 && (
                  <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-medium ${getSourceTypeColor(conversation.sourceType)}`}>
                    <SourceTypeIcon sourceType={conversation.sourceType} className="w-3 h-3" />
                    {getSourceTypeLabel(conversation.sourceType)}
                  </span>
                )}
              </div>
              <div className="mt-2 flex items-center gap-4 text-xs text-slate-600 dark:text-slate-300 flex-wrap">
                {mailbox && (
                  <span className="inline-flex items-center gap-1">
                    <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
                    </svg>
                    {mailbox.name}
                  </span>
                )}
                <span className="inline-flex items-center gap-1">
                  {isWeChatConversation && conversation.customer?.avatar ? (
                    <img
                      src={conversation.customer.avatar}
                      alt={conversation.customer?.fullName || t('conversations.detail.customerAvatar')}
                      loading="lazy"
                      className="w-4 h-4 rounded-full object-cover border border-white/40 dark:border-white/20"
                    />
                  ) : (
                    <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
                    </svg>
                  )}
                  {t('conversations.detail.customerLabel', { name: displayCustomerName(conversation.customer?.fullName) })}
                </span>
                {conversation.user && (
                  <span className="inline-flex items-center gap-1">
                    <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M5.121 17.804A13.937 13.937 0 0112 16c2.5 0 4.847.655 6.879 1.804M15 10a3 3 0 11-6 0 3 3 0 016 0zm6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                    {t('conversations.detail.assigneeLabel', { name: conversation.user.fullName })}
                  </span>
                )}
              </div>
            </div>

            {/* 操作按钮 - 大屏幕 */}
            <div className="hidden lg:flex items-center gap-2 flex-shrink-0 no-print">
              {!isReadOnly && (
                <>
                  <button
                    onClick={() => setShowNoteBox(!showNoteBox)}
                    className={`inline-flex items-center gap-1.5 px-3 py-2 text-sm rounded-lg transition-colors ${
                      showNoteBox
                        ? 'bg-primary-600 text-white'
                        : 'bg-white dark:bg-slate-700 text-slate-700 dark:text-slate-200 hover:bg-primary-50 dark:hover:bg-primary-800/50 border border-slate-200 dark:border-slate-600'
                    }`}
                  >
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                    </svg>
                    {t('conversations.detail.addNote')}
                  </button>
                  <button
                    onClick={() => setForwardOpen(true)}
                    className="inline-flex items-center gap-1.5 px-3 py-2 text-sm bg-white dark:bg-slate-700 text-slate-700 dark:text-slate-200 hover:bg-primary-50 dark:hover:bg-primary-800/50 rounded-lg transition-colors border border-slate-200 dark:border-slate-600"
                    title={t('conversations.detail.forwardConversation')}
                  >
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M8 7H16M16 7L13 4M16 7L13 10M8 17H16M8 17L11 14M8 17L11 20" />
                    </svg>
                    {t('conversations.detail.forward')}
                  </button>
                  <button
                    onClick={handleClone}
                    className="inline-flex items-center gap-1.5 px-3 py-2 text-sm bg-white dark:bg-slate-700 text-slate-700 dark:text-slate-200 hover:bg-primary-50 dark:hover:bg-primary-800/50 rounded-lg transition-colors border border-slate-200 dark:border-slate-600"
                    title={t('conversations.detail.cloneConversation')}
                  >
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M8 7v8a2 2 0 002 2h6M8 7V5a2 2 0 012-2h4.586a1 1 0 01.707.293l4.414 4.414a1 1 0 01.293.707V15a2 2 0 01-2 2h-2M8 7H6a2 2 0 00-2 2v10a2 2 0 002 2h8a2 2 0 002-2v-2" />
                    </svg>
                    {t('conversations.detail.clone')}
                  </button>
                </>
              )}
              <button
                onClick={handlePrint}
                className="inline-flex items-center gap-1.5 px-3 py-2 text-sm bg-white dark:bg-slate-700 text-slate-700 dark:text-slate-200 hover:bg-primary-50 dark:hover:bg-primary-800/50 rounded-lg transition-colors border border-slate-200 dark:border-slate-600"
                title={t('conversations.detail.printConversation')}
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M17 17h2a2 2 0 002-2v-4a2 2 0 00-2-2H5a2 2 0 00-2 2v4a2 2 0 002 2h2m2 4h6a2 2 0 002-2v-4a2 2 0 00-2-2H9a2 2 0 00-2 2v4a2 2 0 002 2zm8-12V5a2 2 0 00-2-2H9a2 2 0 00-2 2v4h10z" />
                </svg>
                {t('conversations.detail.print')}
              </button>
            </div>

            {/* 操作按钮 - 小屏幕折叠 */}
            <div className="lg:hidden relative flex-shrink-0 no-print" ref={actionsRef}>
              <button
                onClick={() => setActionsOpen(!actionsOpen)}
                className="inline-flex items-center gap-1.5 px-3 py-2 text-sm bg-white dark:bg-slate-700 text-slate-700 dark:text-slate-200 hover:bg-primary-50 dark:hover:bg-primary-800/50 rounded-lg transition-colors border border-slate-200 dark:border-slate-600"
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 5v.01M12 12v.01M12 19v.01M12 6a1 1 0 110-2 1 1 0 010 2zm0 7a1 1 0 110-2 1 1 0 010 2zm0 7a1 1 0 110-2 1 1 0 010 2z" />
                </svg>
                {t('conversations.detail.actions')}
              </button>
              {actionsOpen && (
                <div className="absolute right-0 top-full mt-2 w-44 bg-white dark:bg-slate-800 rounded-lg shadow-lg border border-gray-200 dark:border-slate-700 py-1 z-30">
                  {!isReadOnly && (
                    <>
                      <button
                        onClick={() => { setShowNoteBox(!showNoteBox); setActionsOpen(false); }}
                        className="w-full text-left px-4 py-2.5 text-sm text-gray-700 dark:text-slate-200 hover:bg-gray-50 dark:hover:bg-slate-700 flex items-center gap-2"
                      >
                        <svg className="w-4 h-4 text-amber-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                        </svg>
                        {t('conversations.detail.addNote')}
                      </button>
                      <button
                        onClick={() => { setForwardOpen(true); setActionsOpen(false); }}
                        className="w-full text-left px-4 py-2.5 text-sm text-gray-700 dark:text-slate-200 hover:bg-gray-50 dark:hover:bg-slate-700 flex items-center gap-2"
                      >
                        <svg className="w-4 h-4 text-primary-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M8 7H16M16 7L13 4M16 7L13 10M8 17H16M8 17L11 14M8 17L11 20" />
                        </svg>
                        {t('conversations.detail.forward')}
                      </button>
                      <button
                        onClick={() => { handleClone(); setActionsOpen(false); }}
                        className="w-full text-left px-4 py-2.5 text-sm text-gray-700 dark:text-slate-200 hover:bg-gray-50 dark:hover:bg-slate-700 flex items-center gap-2"
                      >
                        <svg className="w-4 h-4 text-gray-500 dark:text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M8 7v8a2 2 0 002 2h6M8 7V5a2 2 0 012-2h4.586a1 1 0 01.707.293l4.414 4.414a1 1 0 01.293.707V15a2 2 0 01-2 2h-2M8 7H6a2 2 0 00-2 2v10a2 2 0 002 2h8a2 2 0 002-2v-2" />
                        </svg>
                        {t('conversations.detail.clone')}
                      </button>
                    </>
                  )}
                  <button
                    onClick={() => { handlePrint(); setActionsOpen(false); }}
                    className="w-full text-left px-4 py-2.5 text-sm text-gray-700 dark:text-slate-200 hover:bg-gray-50 dark:hover:bg-slate-700 flex items-center gap-2"
                  >
                    <svg className="w-4 h-4 text-gray-500 dark:text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M17 17h2a2 2 0 002-2v-4a2 2 0 00-2-2H5a2 2 0 00-2 2v4a2 2 0 002 2h2m2 4h6a2 2 0 002-2v-4a2 2 0 00-2-2H9a2 2 0 00-2 2v4a2 2 0 002 2zm8-12V5a2 2 0 00-2-2H9a2 2 0 00-2 2v4h10z" />
                    </svg>
                    {t('conversations.detail.print')}
                  </button>
                </div>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* 主内容区：左侧消息 + 右侧边栏 - 响应式垂直布局 */}
      <div className="flex flex-col lg:flex-row gap-4 items-stretch lg:items-start">
        <div className="flex-1 min-w-0 space-y-4">
          {/* 内部备注框 */}
          {showNoteBox && (
            <ReplyBox
              mode="note"
              mailboxId={conversation?.mailboxId}
              onSubmit={handleAddNote}
              submitting={submitting}
              readOnly={isReadOnly}
            />
          )}

          {/* 线程列表 */}
          <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4 sm:p-5">
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">
                {t('conversations.detail.threads')} <span className="text-gray-400 dark:text-slate-500 font-normal">({threads.length})</span>
              </h3>
              <button
                onClick={() => {
                  queryClient.invalidateQueries({ queryKey: queryKeys.conversations.detail(convId) });
                  queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(convId) });
                }}
                className="text-xs text-gray-500 dark:text-slate-400 hover:text-primary-600 dark:hover:text-primary-400 inline-flex items-center gap-1"
              >
                <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                </svg>
                {t('conversations.detail.refresh')}
              </button>
            </div>
            <div className="relative">
              <div
                ref={threadListRef}
                onScroll={handleThreadScroll}
                className="max-h-[70vh] overflow-y-auto scroll-smooth"
              >
                <ThreadList
                  threads={threads}
                  customerAvatar={conversation.customer?.avatar}
                  customerName={conversation.customer?.fullName}
                />
              </div>
              {/* 跳到最新消息悬浮按钮 */}
              {showScrollBottom && (
                <button
                  type="button"
                  onClick={scrollToBottom}
                  className="sticky bottom-3 left-1/2 -translate-x-1/2 z-10 inline-flex items-center gap-1.5 px-3 py-1.5 bg-primary-600 hover:bg-primary-700 text-white text-xs font-medium rounded-full shadow-lg transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary-400 focus-visible:ring-offset-2"
                  aria-label={t('conversations.detail.jumpToLatest')}
                >
                  <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M19 14l-7 7m0 0l-7-7m7 7V3" />
                  </svg>
                  {t('conversations.detail.latestMessage')}
                </button>
              )}
            </div>
          </div>

          {/* 回复框 */}
          {isWeChatConversation ? (
            <>
              {/* 微信 48 小时客服消息窗口提示（含进度条可视化）*/}
              {weChatWindow && (() => {
                const windowMs = 48 * 60 * 60 * 1000;
                const elapsedMs = weChatWindow.lastInteraction
                  ? windowMs - weChatWindow.remainingMs
                  : 0;
                const progressPercent = weChatWindow.status === 'expired'
                  ? 100
                  : Math.min(100, Math.max(0, (elapsedMs / windowMs) * 100));
                const barColor = weChatWindow.status === 'ok'
                  ? 'bg-green-500'
                  : weChatWindow.status === 'warning'
                  ? 'bg-amber-500'
                  : weChatWindow.status === 'expired'
                  ? 'bg-red-500'
                  : 'bg-gray-400';
                return (
                  <div className={`rounded-lg text-xs border overflow-hidden ${
                    weChatWindow.status === 'ok'
                      ? 'bg-green-50 border-green-200 text-green-700 dark:bg-green-900/20 dark:border-green-800/50 dark:text-green-300'
                      : weChatWindow.status === 'warning'
                      ? 'bg-amber-50 border-amber-200 text-amber-700 dark:bg-amber-900/20 dark:border-amber-800/50 dark:text-amber-300'
                      : weChatWindow.status === 'expired'
                      ? 'bg-red-50 border-red-200 text-red-700 dark:bg-red-900/20 dark:border-red-800/50 dark:text-red-300'
                      : 'bg-gray-50 border-gray-200 text-gray-600 dark:bg-slate-800 dark:border-slate-600 dark:text-slate-300'
                  }`}>
                    <div className="flex items-center gap-2 px-3 py-2">
                      {weChatWindow.status === 'ok' && <CheckCircle2 className="w-3.5 h-3.5 flex-shrink-0" />}
                      {weChatWindow.status === 'warning' && <Clock className="w-3.5 h-3.5 flex-shrink-0" />}
                      {weChatWindow.status === 'expired' && <AlertTriangle className="w-3.5 h-3.5 flex-shrink-0" />}
                      {weChatWindow.status === 'unknown' && <Clock className="w-3.5 h-3.5 flex-shrink-0" />}
                      <span className="flex-1 min-w-0 flex items-center gap-2 flex-wrap">
                        {weChatWindow.status === 'ok' &&
                          t('conversations.detail.wechatWindowOk', {
                            remaining: formatRemaining(weChatWindow.remainingMs),
                          })}
                        {weChatWindow.status === 'warning' &&
                          t('conversations.detail.wechatWindowWarning', {
                            remaining: formatRemaining(weChatWindow.remainingMs),
                          })}
                        {weChatWindow.status === 'expired' && (
                          <>
                            <span>{t('conversations.detail.wechatWindowExpired')}</span>
                            <button
                              type="button"
                              onClick={() => navigate('/wechat?tab=template')}
                              className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-red-600 hover:bg-red-700 text-white text-[11px] font-medium transition-colors flex-shrink-0 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-red-400 focus-visible:ring-offset-1 dark:focus-visible:ring-offset-slate-900"
                              aria-label={t('conversations.detail.wechatTemplateLinkAria')}
                              title={t('conversations.detail.wechatTemplateLinkTitle')}
                            >
                              <ExternalLink className="w-3 h-3" />
                              {t('conversations.detail.goToTemplate')}
                            </button>
                          </>
                        )}
                        {weChatWindow.status === 'unknown' && t('conversations.detail.wechatWindowUnknown')}
                      </span>
                      {weChatWindow.lastInteraction && (
                        <span className="text-[10px] opacity-70 flex-shrink-0 whitespace-nowrap" title={t('conversations.detail.lastInteractionTitle', { time: formatDateTime(weChatWindow.lastInteraction) })}>
                          <span className="hidden sm:inline">{t('conversations.detail.lastInteraction')}</span>
                          <span className="sm:hidden">{t('conversations.detail.lastInteractionShort')}</span>
                          {formatDateTime(weChatWindow.lastInteraction)}
                        </span>
                      )}
                    </div>
                    {/* 48 小时窗口进度条：含 sr-only 详细描述以便屏幕阅读器正确朗读 */}
                    {weChatWindow.status !== 'unknown' && (
                      <div className="px-3 pb-2">
                        <div className="sr-only">
                          {t('conversations.detail.wechatWindowSrText', {
                            status: windowStatusLabel(weChatWindow.status),
                            percent: Math.round(progressPercent),
                            remaining:
                              weChatWindow.status === 'expired'
                                ? t('conversations.detail.wechatWindowExpiredAdvice')
                                : t('conversations.detail.remainingLabel', {
                                    remaining: formatRemaining(weChatWindow.remainingMs),
                                  }),
                          })}
                        </div>
                        <div
                          className="h-1.5 bg-white/60 dark:bg-slate-900/40 rounded-full overflow-hidden"
                          role="progressbar"
                          aria-valuenow={Math.floor(weChatWindow.remainingMs / (60 * 60 * 1000))}
                          aria-valuemin={0}
                          aria-valuemax={48}
                          aria-label={t('conversations.detail.wechatWindowProgressAria', {
                            remaining: formatRemaining(weChatWindow.remainingMs),
                          })}
                        >
                          <div
                            className={`h-full ${barColor} rounded-full transition-all duration-500 ease-out`}
                            style={{ width: `${progressPercent}%` }}
                          />
                        </div>
                        <div className="flex justify-between mt-1 text-[10px] opacity-70" aria-hidden="true">
                          <span>0h</span>
                          <span>
                            {weChatWindow.status === 'expired'
                              ? t('conversations.detail.expired')
                              : t('conversations.detail.wechatUsedRemaining', {
                                  used: Math.floor((progressPercent / 100) * 48),
                                  remaining: formatRemaining(weChatWindow.remainingMs),
                                })}
                          </span>
                          <span>48h</span>
                        </div>
                        {/* 过期时间显示（warning/expired 状态下显示） */}
                        {weChatWindow.expireAt && (weChatWindow.status === 'warning' || weChatWindow.status === 'expired') && (
                          <div className="mt-1 text-[10px] opacity-70 text-center" title={t('conversations.detail.windowExpiresAtTitle', {
                              time: formatDateTime(weChatWindow.expireAt),
                            })}>
                            {weChatWindow.status === 'expired'
                              ? t('conversations.detail.windowExpiredAtPrefix')
                              : t('conversations.detail.windowWillExpireAtPrefix')}
                            <span className="font-mono font-semibold">{formatDateTime(weChatWindow.expireAt)}</span>
                            {weChatWindow.status === 'expired'
                              ? t('conversations.detail.windowExpiredAtSuffix')
                              : t('conversations.detail.windowWillExpireAtSuffix')}
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                );
              })()}
              <WeChatReplyBox
                key={weChatReplyKey}
                value={weChatReplyContent}
                onChange={setWeChatReplyContent}
                onSubmit={handleWeChatReply}
                submitting={submitting}
                readOnly={isReadOnly}
                windowStatus={weChatWindow?.status}
              />
            </>
          ) : isWhatsAppConversation ? (
            <div className="space-y-3">
              {/* WhatsApp 24 小时客户服务窗口提示（含进度条可视化）*/}
              {whatsAppWindow && (() => {
                const windowMs = 24 * 60 * 60 * 1000;
                const elapsedMs = whatsAppWindow.lastInteraction
                  ? windowMs - whatsAppWindow.remainingMs
                  : 0;
                const progressPercent = whatsAppWindow.status === 'expired'
                  ? 100
                  : Math.min(100, Math.max(0, (elapsedMs / windowMs) * 100));
                const gradientBar = whatsAppWindow.status === 'ok'
                  ? 'bg-gradient-to-r from-emerald-400 via-emerald-500 to-teal-500'
                  : whatsAppWindow.status === 'warning'
                  ? 'bg-gradient-to-r from-yellow-400 via-amber-500 to-orange-500'
                  : 'bg-gradient-to-r from-red-400 via-red-500 to-rose-500';
                const containerGradient = whatsAppWindow.status === 'ok'
                  ? 'bg-gradient-to-br from-emerald-50 via-emerald-50/80 to-teal-50/60 dark:from-emerald-900/25 dark:via-emerald-900/20 dark:to-teal-900/15'
                  : whatsAppWindow.status === 'warning'
                  ? 'bg-gradient-to-br from-yellow-50 via-amber-50 to-orange-50 dark:from-yellow-900/25 dark:via-amber-900/20 dark:to-orange-900/15'
                  : whatsAppWindow.status === 'expired'
                  ? 'bg-gradient-to-br from-red-50 via-rose-50 to-pink-50 dark:from-red-900/25 dark:via-rose-900/20 dark:to-pink-900/15'
                  : 'bg-gradient-to-br from-gray-50 via-slate-50 to-slate-50 dark:from-slate-800 dark:via-slate-800 dark:to-slate-800';
                const iconBg = whatsAppWindow.status === 'ok'
                  ? 'bg-gradient-to-br from-emerald-100 to-teal-100 dark:from-emerald-900/50 dark:to-teal-900/40 text-emerald-600 dark:text-emerald-300 ring-emerald-200 dark:ring-emerald-800/40'
                  : whatsAppWindow.status === 'warning'
                  ? 'bg-gradient-to-br from-yellow-100 to-amber-100 dark:from-yellow-900/50 dark:to-amber-900/40 text-amber-600 dark:text-amber-300 ring-amber-200 dark:ring-amber-800/40'
                  : whatsAppWindow.status === 'expired'
                  ? 'bg-gradient-to-br from-red-100 to-rose-100 dark:from-red-900/50 dark:to-rose-900/40 text-red-600 dark:text-red-300 ring-red-200 dark:ring-red-800/40'
                  : 'bg-gradient-to-br from-gray-100 to-slate-100 dark:from-slate-700 dark:to-slate-600 text-gray-500 dark:text-slate-300 ring-gray-200 dark:ring-slate-600';
                const accentBorder = whatsAppWindow.status === 'ok'
                  ? 'border-l-emerald-400'
                  : whatsAppWindow.status === 'warning'
                  ? 'border-l-amber-400'
                  : whatsAppWindow.status === 'expired'
                  ? 'border-l-red-400'
                  : 'border-l-gray-300';
                return (
                  <div
                    className={`relative rounded-2xl overflow-hidden border shadow-sm ${containerGradient} ${
                      whatsAppWindow.status === 'ok'
                        ? 'border-emerald-200/70 dark:border-emerald-800/40'
                        : whatsAppWindow.status === 'warning'
                        ? 'border-amber-200/70 dark:border-amber-800/40'
                        : whatsAppWindow.status === 'expired'
                        ? 'border-red-200/70 dark:border-red-800/40'
                        : 'border-gray-200 dark:border-slate-700'
                    } border-l-4 ${accentBorder}`}
                  >
                    {/* 顶部装饰渐变条 */}
                    <div
                      className={`h-1 w-full ${gradientBar} ${
                        whatsAppWindow.status === 'warning' || whatsAppWindow.status === 'expired' ? 'animate-pulse' : ''
                      }`}
                      aria-hidden="true"
                    />
                    <div className="p-4 sm:p-5">
                      <div className="flex flex-col sm:flex-row sm:items-start gap-3 sm:gap-4">
                        {/* 状态图标 */}
                        <div className={`flex-shrink-0 w-11 h-11 rounded-xl flex items-center justify-center ring-1 ${iconBg} shadow-sm`}>
                          {whatsAppWindow.status === 'ok' && <CheckCircle2 className="w-5 h-5" />}
                          {whatsAppWindow.status === 'warning' && <Clock className="w-5 h-5" />}
                          {whatsAppWindow.status === 'expired' && <AlertTriangle className="w-5 h-5" />}
                          {whatsAppWindow.status === 'unknown' && <Clock className="w-5 h-5" />}
                        </div>

                        {/* 主体内容 */}
                        <div className="flex-1 min-w-0">
                          <div className="flex flex-col sm:flex-row sm:items-center gap-2 sm:gap-3 mb-3">
                            <div className="flex items-center gap-2 flex-wrap flex-1 min-w-0">
                              {whatsAppWindow.status === 'ok' && (
                                <>
                                  <span
                                    className={`text-sm font-bold ${
                                      whatsAppWindow.status === 'ok'
                                        ? 'text-emerald-800 dark:text-emerald-200'
                                        : whatsAppWindow.status === 'warning'
                                        ? 'text-amber-800 dark:text-amber-200'
                                        : whatsAppWindow.status === 'expired'
                                        ? 'text-red-800 dark:text-red-200'
                                        : 'text-gray-700 dark:text-slate-200'
                                    }`}
                                  >
                                    {t('conversations.detail.whatsAppWindowOk')}
                                  </span>
                                  <span className="inline-flex items-center px-2 py-0.5 rounded-full bg-white/70 dark:bg-slate-900/40 text-[11px] font-semibold text-emerald-700 dark:text-emerald-300 ring-1 ring-emerald-200/70 dark:ring-emerald-800/30">
                                    {t('conversations.detail.whatsAppActive')}
                                  </span>
                                </>
                              )}
                              {whatsAppWindow.status === 'warning' && (
                                <>
                                  <span className="text-sm font-bold text-amber-800 dark:text-amber-200">
                                    {t('conversations.detail.whatsAppWindowWarning')}
                                  </span>
                                  <span className="inline-flex items-center px-2 py-0.5 rounded-full bg-white/70 dark:bg-slate-900/40 text-[11px] font-semibold text-amber-700 dark:text-amber-300 ring-1 ring-amber-200/70 dark:ring-amber-800/30 animate-pulse">
                                    {t('conversations.detail.whatsAppReplySoon')}
                                  </span>
                                </>
                              )}
                              {whatsAppWindow.status === 'expired' && (
                                <>
                                  <span className="text-sm font-bold text-red-800 dark:text-red-200">
                                    {t('conversations.detail.whatsAppWindowExpired')}
                                  </span>
                                  <span className="inline-flex items-center px-2 py-0.5 rounded-full bg-white/70 dark:bg-slate-900/40 text-[11px] font-semibold text-red-700 dark:text-red-300 ring-1 ring-red-200/70 dark:ring-red-800/30">
                                    {t('conversations.detail.whatsAppTemplateOnly')}
                                  </span>
                                </>
                              )}
                              {whatsAppWindow.status === 'unknown' && (
                                <span className="text-sm font-bold text-gray-700 dark:text-slate-200">
                                  {t('conversations.detail.whatsAppAwaitingCustomer')}
                                </span>
                              )}
                            </div>
                          </div>

                          {/* 描述文字 + 按钮 */}
                          <div className="flex flex-col sm:flex-row sm:items-center gap-2 sm:gap-3 mb-3">
                            <p
                              className={`text-xs sm:text-sm flex-1 min-w-0 ${
                                whatsAppWindow.status === 'ok'
                                  ? 'text-emerald-700/90 dark:text-emerald-300/90'
                                  : whatsAppWindow.status === 'warning'
                                  ? 'text-amber-700/90 dark:text-amber-300/90'
                                  : whatsAppWindow.status === 'expired'
                                  ? 'text-red-700/90 dark:text-red-300/90'
                                  : 'text-gray-600/90 dark:text-slate-300/90'
                              }`}
                            >
                              {whatsAppWindow.status === 'ok' && (
                                <>
                                  {t('conversations.detail.whatsAppOkDesc')}
                                  <span className="font-mono font-semibold ml-0.5">
                                    {formatRemaining(whatsAppWindow.remainingMs)}
                                  </span>
                                </>
                              )}
                              {whatsAppWindow.status === 'warning' && (
                                <>
                                  {t('conversations.detail.whatsAppWarningDesc')}
                                  <span className="font-mono font-semibold ml-0.5">
                                    {formatRemaining(whatsAppWindow.remainingMs)}
                                  </span>
                                </>
                              )}
                              {whatsAppWindow.status === 'expired' && (
                                <>
                                  {t('conversations.detail.whatsAppExpiredDesc')}
                                </>
                              )}
                              {whatsAppWindow.status === 'unknown' && t('conversations.detail.whatsAppUnknownDesc')}
                            </p>
                            {whatsAppWindow.status === 'expired' && (
                              <button
                                type="button"
                                onClick={() => navigate('/whatsapp?tab=template')}
                                className="inline-flex items-center gap-1.5 px-3.5 py-1.5 rounded-lg bg-gradient-to-r from-red-600 to-rose-600 hover:from-red-700 hover:to-rose-700 text-white text-xs font-semibold transition-all shadow-md shadow-red-500/20 active:scale-[0.98] self-start sm:self-auto focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-red-400 focus-visible:ring-offset-1 dark:focus-visible:ring-offset-slate-800"
                                aria-label={t('conversations.detail.whatsAppTemplateLinkAria')}
                                title={t('conversations.detail.whatsAppTemplateLinkTitle')}
                              >
                                <ExternalLink className="w-3 h-3" />
                                {t('conversations.detail.goToTemplate')}
                              </button>
                            )}
                            {whatsAppWindow.lastInteraction && (
                              <div
                                className="flex items-center gap-1.5 px-2.5 py-1 rounded-lg bg-white/60 dark:bg-slate-900/30 text-[11px] font-medium whitespace-nowrap self-start sm:self-auto"
                                title={t('conversations.detail.lastInteractionTitle', {
                                  time: formatDateTime(whatsAppWindow.lastInteraction),
                                })}
                              >
                                <Clock className="w-3 h-3 opacity-70" />
                                <span className="hidden sm:inline opacity-70">{t('conversations.detail.lastInteraction')}</span>
                                <span className="font-mono">
                                  {formatDateTime(whatsAppWindow.lastInteraction)}
                                </span>
                              </div>
                            )}
                          </div>

                          {/* 24 小时窗口进度条 */}
                          {whatsAppWindow.status !== 'unknown' && (
                            <div>
                              <div className="sr-only">
                                {t('conversations.detail.whatsAppWindowSrText', {
                                  status: windowStatusLabel(whatsAppWindow.status),
                                  percent: Math.round(progressPercent),
                                  remaining:
                                    whatsAppWindow.status === 'expired'
                                      ? t('conversations.detail.whatsAppFreeMessageBlocked')
                                      : t('conversations.detail.remainingLabel', {
                                          remaining: formatRemaining(whatsAppWindow.remainingMs),
                                        }),
                                })}
                              </div>
                              {/* 进度条外框 */}
                              <div className="relative rounded-full bg-white/70 dark:bg-slate-900/40 p-0.5 shadow-inner ring-1 ring-black/5 dark:ring-white/5">
                                <div
                                  className="h-2.5 rounded-full overflow-hidden"
                                  role="progressbar"
                                  aria-valuenow={Math.floor(whatsAppWindow.remainingMs / (60 * 60 * 1000))}
                                  aria-valuemin={0}
                                  aria-valuemax={24}
                                  aria-label={t('conversations.detail.whatsAppWindowProgressAria', {
                                    remaining: formatRemaining(whatsAppWindow.remainingMs),
                                  })}
                                >
                                  <div
                                    className={`h-full ${gradientBar} rounded-full transition-all duration-700 ease-out relative overflow-hidden ${
                                      whatsAppWindow.status === 'ok' ? '' : 'animate-pulse'
                                    }`}
                                    style={{ width: `${progressPercent}%` }}
                                  >
                                    {/* 条纹光泽效果 */}
                                    <div
                                      className="absolute inset-0 opacity-40"
                                      style={{
                                        backgroundImage:
                                          'linear-gradient(90deg, rgba(255,255,255,0) 0%, rgba(255,255,255,0.4) 50%, rgba(255,255,255,0) 100%)',
                                        backgroundSize: '32px 100%',
                                      }}
                                    />
                                  </div>
                                </div>
                              </div>
                              {/* 进度标尺 */}
                              <div className="flex justify-between mt-1.5 px-0.5" aria-hidden="true">
                                <span className="text-[10px] font-mono opacity-60">{t('conversations.detail.whatsAppRulerStart')}</span>
                                <span
                                  className={`text-[10px] font-mono font-semibold ${
                                    whatsAppWindow.status === 'expired'
                                      ? 'text-red-600 dark:text-red-300'
                                      : whatsAppWindow.status === 'warning'
                                      ? 'text-amber-600 dark:text-amber-300'
                                      : 'text-emerald-600 dark:text-emerald-300'
                                  }`}
                                >
                                  {whatsAppWindow.status === 'expired'
                                    ? t('conversations.detail.whatsAppStopped')
                                    : t('conversations.detail.whatsAppConsumed', {
                                        percent: Math.round(progressPercent),
                                        remaining: formatRemaining(whatsAppWindow.remainingMs),
                                      })}
                                </span>
                                <span className="text-[10px] font-mono opacity-60">{t('conversations.detail.whatsAppRulerEnd')}</span>
                              </div>
                              {/* 过期时间显示 */}
                              {whatsAppWindow.expireAt && (whatsAppWindow.status === 'warning' || whatsAppWindow.status === 'expired') && (
                                <div
                                  className={`mt-2 flex items-center justify-center gap-1.5 text-[10px] font-medium ${
                                    whatsAppWindow.status === 'expired'
                                      ? 'text-red-600/90 dark:text-red-300/90'
                                      : 'text-amber-600/90 dark:text-amber-300/90'
                                  }`}
                                  title={t('conversations.detail.windowExpiresAtTitle', {
                                    time: formatDateTime(whatsAppWindow.expireAt),
                                  })}
                                >
                                  <Clock className="w-2.5 h-2.5" />
                                  {whatsAppWindow.status === 'expired'
                                    ? t('conversations.detail.windowExpiredAtPrefix')
                                    : t('conversations.detail.whatsAppWillExpireAtPrefix')}
                                  <span className="font-mono font-semibold">{formatDateTime(whatsAppWindow.expireAt)}</span>
                                  {whatsAppWindow.status === 'expired'
                                    ? t('conversations.detail.windowExpiredAtSuffix')
                                    : t('conversations.detail.whatsAppWillExpireAtSuffix')}
                                </div>
                              )}
                            </div>
                          )}
                        </div>
                      </div>
                    </div>
                  </div>
                );
              })()}
              <WhatsAppReplyBox
                key={whatsAppReplyKey}
                value={whatsAppReplyContent}
                onChange={setWhatsAppReplyContent}
                onSubmit={handleWhatsAppReply}
                submitting={submitting}
                readOnly={isReadOnly}
                windowStatus={whatsAppWindow?.status}
              />
            </div>
          ) : isFeishuConversation ? (
            <FeishuReplyBox
              key={feishuReplyKey}
              value={feishuReplyContent}
              onChange={setFeishuReplyContent}
              onSubmit={handleFeishuReply}
              submitting={submitting}
              readOnly={isReadOnly}
            />
          ) : isDingDingConversation ? (
            <DingDingReplyBox
              key={dingDingReplyKey}
              value={dingDingReplyContent}
              onChange={setDingDingReplyContent}
              onSubmit={handleDingDingReply}
              submitting={submitting}
              readOnly={isReadOnly}
              windowStatus={dingDingWindow?.status}
              windowRemainingText={dingDingWindowRemainingText}
            />
          ) : isMessengerConversation ? (
            <MessengerReplyBox
              key={messengerReplyKey}
              conversationId={convId}
              value={messengerReplyContent}
              onChange={setMessengerReplyContent}
              onSubmit={handleMessengerReply}
              submitting={submitting}
              readOnly={isReadOnly}
              windowStatus={whatsAppLikeWindow(messengerWindow)}
            />
          ) : isTelegramConversation ? (
            <TelegramReplyBox
              key={telegramReplyKey}
              value={telegramReplyContent}
              onChange={setTelegramReplyContent}
              onSubmit={handleTelegramReply}
              submitting={submitting}
              readOnly={isReadOnly}
              unreachable={telegramUnreachable}
              onClearUnreachable={handleTelegramClearUnreachable}
              parseMode={telegramParseMode}
              onParseModeChange={setTelegramParseMode}
              quoteLastCustomerMessage={telegramQuoteLastCustomerMessage}
              onQuoteChange={setTelegramQuoteLastCustomerMessage}
            />
          ) : isLineConversation ? (
            <LineReplyBox
              key={lineReplyKey}
              value={lineReplyContent}
              onChange={setLineReplyContent}
              onSubmit={handleLineReply}
              submitting={submitting}
              readOnly={isReadOnly}
              replyWindowStatus={lineReplyWindow.status}
              replyWindowRemainingText={lineReplyWindow.remainingText}
              unreachable={lineUnreachable}
              humanHandoverActive={lineHandover.active}
              humanHandoverRemainingText={lineHandover.remainingText}
              onTakeover={handleLineTakeover}
              onReleaseTakeover={handleLineReleaseTakeover}
            />
          ) : isIMessageConversation ? (
            <IMessageReplyBox
              key={iMessageReplyKey}
              value={iMessageReplyContent}
              onChange={setIMessageReplyContent}
              onSubmit={handleIMessageReply}
              submitting={submitting}
              readOnly={isReadOnly}
              windowStatus={whatsAppLikeWindow(iMessageWindow)}
            />
          ) : (
            <ReplyBox
              mode="reply"
              mailboxId={conversation?.mailboxId}
              defaultFrom={currentUser?.email}
              onSubmit={handleReply}
              onSaveDraft={handleSaveDraft}
              submitting={submitting}
              initialBody={draft?.body}
              initialCc={draft?.cc ?? undefined}
              initialBcc={draft?.bcc ?? undefined}
              initialAttachments={draft?.attachments ?? undefined}
              readOnly={isReadOnly}
            />
          )}
        </div>

        {/* 右侧边栏 */}
        <ConversationSidebar
          conversation={conversation}
          mailboxes={mailboxes}
          users={users}
          isFollowing={isFollowing}
          onToggleFollow={handleToggleFollow}
          onStatusChange={handleStatusChange}
          onPriorityChange={handlePriorityChange}
          onAssign={handleAssign}
          updating={updating}
          satisfactionRating={satisfactionRating}
          chatSessionRating={chatSessionRating}
          readOnly={isReadOnly}
          canAssign={canAssignConversations}
        />
      </div>

      {/* 转发会话 Modal */}
      <Modal
        open={forwardOpen}
        title={t('conversations.detail.forwardConversation')}
        onClose={() => setForwardOpen(false)}
        size="md"
        footer={
          <>
            <button
              onClick={() => setForwardOpen(false)}
              disabled={forwarding}
              className="px-4 py-2 text-sm text-gray-700 dark:text-slate-200 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors"
            >
              {t('common.cancel')}
            </button>
            <button
              onClick={handleForward}
              disabled={forwarding}
              className="px-4 py-2 text-sm bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 dark:disabled:bg-primary-800 text-white rounded-lg transition-colors inline-flex items-center gap-1.5"
            >
              {forwarding && (
                <svg className="animate-spin h-4 w-4" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                  <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                  <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                </svg>
              )}
              {forwarding ? t('conversations.detail.forwarding') : t('conversations.detail.confirmForward')}
            </button>
          </>
        }
      >
        <div className="space-y-3">
          {forwardError && (
            <div className="bg-red-50 dark:bg-red-900/30 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 px-3 py-2 rounded-lg text-sm flex items-center gap-2">
              <svg className="w-4 h-4 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
              </svg>
              {forwardError}
            </div>
          )}
          <p className="text-sm text-gray-600 dark:text-slate-300">
            {t('conversations.detail.forwardHintPrefix')}
            <span className="font-semibold text-gray-900 dark:text-slate-100">#{conversation.id} {conversation.subject}</span>
            {t('conversations.detail.forwardHintSuffix')}
          </p>
          <div>
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1">{t('conversations.detail.forwardRecipient')}</label>
            <input
              type="email"
              value={forwardTo}
              onChange={(e) => setForwardTo(e.target.value)}
              placeholder="recipient@example.com"
              className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 dark:focus:ring-primary-400 text-sm text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500"
            />
          </div>
          <div>
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1">{t('conversations.detail.forwardNoteLabel')}</label>
            <textarea
              value={forwardNote}
              onChange={(e) => setForwardNote(e.target.value)}
              placeholder={t('conversations.detail.forwardNotePlaceholder')}
              rows={3}
              className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 dark:focus:ring-primary-400 text-sm resize-y text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500"
            />
          </div>
          <div className="bg-blue-50 dark:bg-blue-900/30 border border-blue-200 dark:border-blue-800 rounded-lg px-3 py-2 text-xs text-blue-700 dark:text-blue-300">
            <div className="flex items-start gap-1.5">
              <svg className="w-4 h-4 flex-shrink-0 mt-0.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              <div>
                <p className="font-medium">{t('conversations.detail.forwardIncludes')}</p>
                <p>{t('conversations.detail.forwardIncludesMeta')}</p>
                <p>{t('conversations.detail.forwardIncludesMessages', { count: threads.length })}</p>
                <p>
                  {t('conversations.detail.forwardIncludesSender', {
                    email: mailbox?.email || t('conversations.detail.defaultMailbox'),
                  })}
                </p>
              </div>
            </div>
          </div>
        </div>
      </Modal>

      {/* 打印样式 */}
      <style>{`
        @media print {
          .no-print { display: none !important; }
          body { background: white !important; }
          .bg-white { box-shadow: none !important; border: 1px solid #e5e7eb !important; }
          aside, .fixed { display: none !important; }
        }
      `}</style>
    </div>
  );
}
