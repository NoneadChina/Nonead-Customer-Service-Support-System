import { useState, useEffect, useRef, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { chatApi, userApi } from '../api';
import type {
  ChatSessionDto,
  ChatMessageDto,
  ChatSessionQuery,
  ChatOverviewResponse,
} from '../api/types';
import ConfirmDialog from '../components/ConfirmDialog';
import Modal from '../components/Modal';
import ChatSessionList from '../components/chat/ChatSessionList';
import ChatConversation from '../components/chat/ChatConversation';
import ChatInfoPanel from '../components/chat/ChatInfoPanel';
import ChatTransferModal from '../components/chat/ChatTransferModal';
import { useAuth } from '../context/AuthContext';
import { useI18n } from '../context/I18nContext';
import { useMailboxes } from '../context/MailboxContext';
import { useToast } from '../context/ToastContext';
import { useSignalREvent } from '../hooks/useSignalR';
import { SignalREvents } from '../services/signalR';
import { canManageChatSessions } from '../utils/permissions';
import { getApiErrorMessage } from '../utils/error';
import { formatDateTime } from '../utils/format';
import { toIntlLocale } from '../i18n/locale';
import type { TranslationKey } from '../locales/zh-Hans';
import {
  MessageSquare,
  RefreshCw,
  MessageCircle,
  Clock,
  Users,
  Archive,
  AlertCircle,
  Phone,
  Trash2,
  RotateCcw,
  Search,
  ChevronLeft,
  ChevronRight,
} from 'lucide-react';

interface MailboxOption {
  id: number;
  name: string;
}

interface UserOption {
  id: number;
  fullName: string;
}

interface StatCardConfig {
  key: string;
  labelKey: TranslationKey;
  icon: React.ReactNode;
  valueClass: string;
  iconBg: string;
}

const PAGE_SIZE = 20;

/**
 * 聊天会话状态码 → 前端标签 key。
 *
 * 后端 `MappingExtensions.ToDto(ChatSession)`（`MappingExtensions.cs:600-607`）下发**中文**
 * `StatusLabel`，英文界面下直接渲染会漏出中文，故改按稳定 code 取前端字典。码集合恰好为这五个
 * （waiting / active / awaiting_rating / closed / transferred），无缺无余；字典未覆盖的码回落
 * 渲染原始 code（**不再**回落后端 `statusLabel`）。
 */
const CHAT_STATUS_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  waiting: 'chat.status.waiting',
  active: 'chat.status.active',
  awaiting_rating: 'chat.status.awaitingRating',
  closed: 'chat.status.closed',
  transferred: 'chat.status.transferred',
};

/** 会话状态码 → 展示文案；字典未覆盖的码回落渲染原始 code */
function chatStatusLabel(t: (key: TranslationKey) => string, status: string): string {
  const key = CHAT_STATUS_LABEL_KEYS[status];
  return key ? t(key) : status;
}

export default function Chat() {
  const navigate = useNavigate();
  const toast = useToast();
  const { t } = useI18n();
  const { user } = useAuth();
  const currentUserId = user?.id || 1;
  const canManage = user ? canManageChatSessions(user.role) : false;
  const { mailboxes: globalMailboxes } = useMailboxes();

  // ====== 状态 ======
  const [overview, setOverview] = useState<ChatOverviewResponse | null>(null);
  const [sessions, setSessions] = useState<ChatSessionDto[]>([]);
  const [currentSession, setCurrentSession] = useState<ChatSessionDto | null>(null);
  const [messages, setMessages] = useState<ChatMessageDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [refreshLock, setRefreshLock] = useState(false);
  const [messagesLoading, setMessagesLoading] = useState(false);
  const [searchKeyword, setSearchKeyword] = useState('');
  const [statusFilter, setStatusFilter] = useState<string | null>(null);
  const [mailboxFilter, setMailboxFilter] = useState<number | null>(null);
  const [page, setPage] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [totalPages, setTotalPages] = useState(1);

  const [mailboxes, setMailboxes] = useState<MailboxOption[]>([]);
  const [users, setUsers] = useState<UserOption[]>([]);

  // 右栏信息面板折叠状态(桌面端)
  const [infoPanelOpen, setInfoPanelOpen] = useState(true);

  // 模态窗
  const [showTransferModal, setShowTransferModal] = useState(false);
  const [showConfirmClose, setShowConfirmClose] = useState(false);

  // ====== 删除/批量删除/垃圾箱 ======
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());
  const [selectMode, setSelectMode] = useState(false);
  const [trashCount, setTrashCount] = useState(0);
  const [trashOpen, setTrashOpen] = useState(false);
  const [trashList, setTrashList] = useState<ChatSessionDto[]>([]);
  const [trashLoading, setTrashLoading] = useState(false);
  const [trashPage, setTrashPage] = useState(1);
  const [trashTotalPages, setTrashTotalPages] = useState(1);
  const [trashTotalItems, setTrashTotalItems] = useState(0);
  const [trashKeyword, setTrashKeyword] = useState('');
  // 垃圾箱搜索输入缓冲：即时回显，650ms 停顿后才写入 trashKeyword 触发加载
  const [trashKeywordInput, setTrashKeywordInput] = useState('');
  // 删除确认状态：'single' | 'bulk' | 'hard' | 'clear'
  const [deleteConfirm, setDeleteConfirm] = useState<{
    type: 'single' | 'bulk' | 'hard' | 'clear';
    id?: number;
  } | null>(null);
  // 操作进行中（避免重复点击）
  const [actionLoading, setActionLoading] = useState(false);

  const isMountedRef = useRef(true);
  // 防抖标记：避免短时间内多次刷新会话列表
  const refreshTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // 请求序号：快速切换会话/筛选时丢弃过期响应，避免旧响应覆盖新数据
  const messagesReqSeqRef = useRef(0);
  const sessionsReqSeqRef = useRef(0);
  // 垃圾箱搜索防抖计时器
  const trashSearchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const applyTrashSearch = useCallback((v: string) => {
    setTrashKeyword(v);
    setTrashPage(1);
  }, []);
  // 回车：立即按当前缓冲输入触发搜索，并取消挂起的防抖
  const triggerTrashSearchNow = useCallback(() => {
    if (trashSearchTimerRef.current !== null) clearTimeout(trashSearchTimerRef.current);
    trashSearchTimerRef.current = null;
    applyTrashSearch(trashKeywordInput);
  }, [applyTrashSearch, trashKeywordInput]);

  // 组件挂载/卸载时标记,防止异步回调对已卸载组件执行 setState
  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
      if (refreshTimerRef.current) clearTimeout(refreshTimerRef.current);
      if (trashSearchTimerRef.current) clearTimeout(trashSearchTimerRef.current);
    };
  }, []);

  useEffect(() => {
    setMailboxes(globalMailboxes.map((m) => ({ id: m.id, name: m.name })));
  }, [globalMailboxes]);

  const fetchOverview = useCallback(async () => {
    try {
      const data = await chatApi.overview();
      if (!isMountedRef.current) return;
      setOverview(data);
    } catch {
      if (!isMountedRef.current) return;
      setOverview(null);
    }
  }, []);

  // 获取垃圾箱数量（仅超管/客服主管）
  const fetchTrashCount = useCallback(async () => {
    if (!canManage) return;
    try {
      const data = await chatApi.getTrashSessions(1, 1);
      if (!isMountedRef.current) return;
      setTrashCount(data.totalItems ?? 0);
    } catch {
      // 静默失败：垃圾箱计数不应阻塞主流程
    }
  }, [canManage]);

  const fetchSessions = useCallback(async () => {
    const seq = ++sessionsReqSeqRef.current;
    try {
      setLoading(true);
      // "进行中" tab 语义包含正在沟通的 active 会话以及客服刚发起关闭等待客户评分的 awaiting_rating 会话(两者都算客服工作台"活跃"bucket,
      // 至少别让 awaiting_rating 显示到 closed 桶)
      const effectiveStatus: string | undefined =
        statusFilter === 'active'
          ? 'active,awaiting_rating'
          : statusFilter || undefined;
      const params: ChatSessionQuery = {
        status: effectiveStatus,
        mailboxId: mailboxFilter ?? undefined,
        keyword: searchKeyword || undefined,
        page,
        pageSize: PAGE_SIZE,
      };
      const data = await chatApi.listSessions(params);
      if (!isMountedRef.current || seq !== sessionsReqSeqRef.current) return;
      setSessions(data.data);
      setTotalItems(data.totalItems);
      setTotalPages(data.totalPages || 1);
    } catch {
      if (!isMountedRef.current || seq !== sessionsReqSeqRef.current) return;
      setSessions([]);
    } finally {
      if (isMountedRef.current && seq === sessionsReqSeqRef.current) setLoading(false);
    }
  }, [statusFilter, mailboxFilter, searchKeyword, page]);

  const isRefreshing = loading || refreshLock;
  const handleRefresh = useCallback(() => {
    if (isRefreshing) return;
    setRefreshLock(true);
    try {
      void fetchOverview();
      void fetchSessions();
    } finally {
      setTimeout(() => setRefreshLock(false), 400);
    }
  }, [isRefreshing, fetchOverview, fetchSessions]);

  const fetchMessages = useCallback(async (sessionId: number) => {
    const seq = ++messagesReqSeqRef.current;
    try {
      setMessagesLoading(true);
      const data = await chatApi.getMessages(sessionId);
      if (!isMountedRef.current || seq !== messagesReqSeqRef.current) return;
      setMessages(data);
    } catch (e: unknown) {
      // 网络抖动不应清空已显示的消息
      console.error('Chat fetchMessages failed:', e);
    } finally {
      if (isMountedRef.current && seq === messagesReqSeqRef.current) setMessagesLoading(false);
    }
  }, []);

  const fetchUsers = useCallback(async () => {
    try {
      const result = await userApi.list();
      if (!isMountedRef.current) return;
      const list = Array.isArray(result?.data) ? result.data : [];
      setUsers(list.map((u: { id: number; fullName: string }) => ({ id: u.id, fullName: u.fullName })));
    } catch {
      if (!isMountedRef.current) return;
      setUsers([]);
    }
  }, []);

  useEffect(() => {
    fetchOverview();
    fetchUsers();
    fetchTrashCount();
  }, [fetchOverview, fetchUsers, fetchTrashCount]);

  useEffect(() => {
    fetchSessions();
  }, [fetchSessions]);

  // 垃圾箱列表加载（仅在打开垃圾箱时）
  useEffect(() => {
    if (!trashOpen) return;
    setTrashLoading(true);
    chatApi
      .getTrashSessions(trashPage, 20, trashKeyword || undefined)
      .then((data) => {
        if (!isMountedRef.current) return;
        setTrashList(data.data ?? []);
        setTrashTotalItems(data.totalItems ?? 0);
        setTrashTotalPages(data.totalPages || 1);
      })
      .catch(() => {
        if (!isMountedRef.current) return;
        setTrashList([]);
      })
      .finally(() => {
        if (isMountedRef.current) setTrashLoading(false);
      });
  }, [trashOpen, trashPage, trashKeyword]);

  /**
   * 当 sessions 列表刷新(接待/关闭/转工单/列表筛选等)后，
   * 若存在 currentSession 且 id 匹配，则同步为最新的会话数据，
   * 避免聊天页顶部客户名右侧的状态徽章一直显示「等待接待」。
   */
  useEffect(() => {
    if (!currentSession) return;
    const fresh = sessions.find((s) => s.id === currentSession.id);
    if (!fresh) return;
    // 浅层比较：仅在关键字段有差异时才 setState, 避免重复触发副作用
    if (
      fresh.status !== currentSession.status
      || fresh.userName !== currentSession.userName
      || fresh.userId !== currentSession.userId
      || fresh.messageCount !== currentSession.messageCount
      || fresh.agentUnreadCount !== currentSession.agentUnreadCount
      || fresh.customerUnreadCount !== currentSession.customerUnreadCount
      || fresh.rating !== currentSession.rating
      || fresh.closedAt !== currentSession.closedAt
      || fresh.updatedAt !== currentSession.updatedAt
    ) {
      setCurrentSession(fresh);
    }
  }, [sessions, currentSession]);

  // 切换会话时初次加载消息
  useEffect(() => {
    if (currentSession) {
      fetchMessages(currentSession.id);
    } else {
      setMessages([]);
    }
  }, [currentSession, fetchMessages]);

  // 用 ref 保存最新的 sessions/currentSession，供 SignalR 回调读取，避免闭包陈旧
  const sessionsRef = useRef<ChatSessionDto[]>([]);
  sessionsRef.current = sessions;
  const currentSessionRef = useRef<ChatSessionDto | null>(null);
  currentSessionRef.current = currentSession;

  // 监听 SignalR RealtimeChat 事件
  // - 新会话创建(isNewSession) 或 会话不在当前列表 → 刷新会话列表 + 概览
  // - 当前打开的会话有新消息 → 刷新消息
  useSignalREvent<unknown>(SignalREvents.RealtimeChat, (data) => {
    if (!data || typeof data !== 'object') return;
    const payload = data as { sessionId?: number; isNewSession?: boolean };
    const eventId = payload.sessionId;

    // 新会话 或 会话不在当前列表中 → 刷新会话列表和概览（防抖 500ms）
    const needListRefresh = payload.isNewSession === true
      || (eventId != null && !sessionsRef.current.some((s) => s.id === eventId));
    if (needListRefresh) {
      if (refreshTimerRef.current) clearTimeout(refreshTimerRef.current);
      refreshTimerRef.current = setTimeout(() => {
        fetchSessions();
        fetchOverview();
      }, 500);
    }

    // 当前打开的会话有新消息 → 刷新消息
    const cur = currentSessionRef.current;
    if (cur && eventId === cur.id) {
      fetchMessages(cur.id);
    }
  });

  // ====== 事件处理 ======
  const handleSelectSession = useCallback(
    async (session: ChatSessionDto) => {
      setCurrentSession(session);
      setInfoPanelOpen(true);
      try {
        await chatApi.markAsRead(session.id, 'agent');
        // 本地更新未读数,避免等待列表刷新
        setSessions((prev) =>
          prev.map((s) =>
            s.id === session.id ? { ...s, agentUnreadCount: 0 } : s,
          ),
        );
      } catch {
        // 标记已读失败不阻塞会话打开
      }
    },
    [],
  );

  const handleBack = useCallback(() => {
    setCurrentSession(null);
    setMessages([]);
  }, []);

  const handleSendMessage = useCallback(
    async (
      content: string,
      attachment?: { url: string; name: string } | null,
    ) => {
      if (!currentSession) return;
      const request = {
        content: content || (attachment ? attachment.name : ''),
        messageType: attachment ? 'file' : 'text',
        attachmentUrl: attachment?.url,
        attachmentName: attachment?.name,
      };
      await chatApi.sendMessage(currentSession.id, request);
      // 发送后立即刷新消息,不依赖 SignalR 延迟
      fetchMessages(currentSession.id);
    },
    [currentSession, fetchMessages],
  );

  const handleAssignSession = useCallback(
    async (sessionId: number) => {
      try {
        const updated = await chatApi.assignSession(sessionId, { userId: currentUserId });
        toast.success(t('chat.page.toastAccepted'));
        // 如果接待的就是当前正在查看的会话 -> 立即更新 currentSession，
        // 让聊天页顶栏的状态徽章从「等待接待」变为「进行中」。
        if (currentSessionRef.current?.id === sessionId) {
          setCurrentSession(updated);
        }
        fetchSessions();
        fetchOverview();
      } catch (e: unknown) {
        const msg = e instanceof Error ? e.message : t('chat.page.acceptFailed');
        toast.error(msg);
      }
    },
    [currentUserId, t, toast, fetchSessions, fetchOverview],
  );

  const handleCloseSession = useCallback(async () => {
    if (!currentSession) return;
    try {
      const result = await chatApi.closeSession(currentSession.id);
      // 两阶段关闭
      // - status === 'awaiting_rating'：只是发起满意度等待，会话仍然打开展示倒计时徽章，保持 currentSession
      // - status === 'closed'：后台直接真关（例如客户主动关闭后客服再次补关、或权限直接关）→ 回到列表
      if (result.status === 'awaiting_rating') {
        toast.success(t('chat.page.ratingRequested'));
        setShowConfirmClose(false);
        // 合并新字段,保持会话打开在右侧继续显示 awaiting_rating 徽章 + 倒计时
        setCurrentSession({ ...currentSession, ...result });
      } else {
        toast.success(t('chat.page.sessionClosed'));
        setShowConfirmClose(false);
        setCurrentSession(null);
        setMessages([]);
      }
      fetchSessions();
      fetchOverview();
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('chat.page.closeFailed');
      toast.error(msg);
    }
  }, [currentSession, t, toast, fetchSessions, fetchOverview]);

  const handleTransferToTicket = useCallback(
    async (data: {
      mailboxId?: number | null;
      userId?: number | null;
      subject?: string | null;
    }) => {
      if (!currentSession) return;
      await chatApi.transferToTicket(currentSession.id, data);
      toast.success(t('chat.page.convertedToConversation'));
      setCurrentSession(null);
      setMessages([]);
      setShowTransferModal(false);
      fetchSessions();
      fetchOverview();
    },
    [currentSession, t, toast, fetchSessions, fetchOverview],
  );

  const handleJumpToConversation = useCallback(
    (conversationId: number) => {
      navigate(`/conversations/${conversationId}`);
    },
    [navigate],
  );

  const handleSearchChange = useCallback((v: string) => {
    setSearchKeyword(v);
    setPage(1);
  }, []);

  const handleStatusChange = useCallback((v: string | null) => {
    setStatusFilter(v);
    setPage(1);
  }, []);

  const handleMailboxChange = useCallback((v: number | null) => {
    setMailboxFilter(v);
    setPage(1);
  }, []);

  const handlePageChange = useCallback((p: number) => {
    setPage(p);
  }, []);

  // ====== 删除/批量删除/垃圾箱 事件处理 ======
  const handleToggleSelectMode = useCallback(() => {
    setSelectMode((prev) => !prev);
    setSelectedIds(new Set());
  }, []);

  const handleToggleSelect = useCallback((id: number) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }, []);

  const handleSelectAll = useCallback(() => {
    setSelectedIds((prev) => {
      if (prev.size === sessions.length) return new Set();
      return new Set(sessions.map((s) => s.id));
    });
  }, [sessions]);

  // 打开单个删除确认
  const handleDeleteSession = useCallback((id: number) => {
    setDeleteConfirm({ type: 'single', id });
  }, []);

  // 打开批量删除确认
  const handleBulkDelete = useCallback(() => {
    if (selectedIds.size === 0) return;
    setDeleteConfirm({ type: 'bulk' });
  }, [selectedIds]);

  // 单个删除（软删除 → 移入垃圾箱）
  const confirmDeleteSession = useCallback(async () => {
    const id = deleteConfirm?.id;
    if (!id) return;
    setActionLoading(true);
    try {
      await chatApi.deleteSession(id);
      toast.success(t('chat.page.movedToTrash'));
      setDeleteConfirm(null);
      // 如果删除的是当前打开的会话，关闭右侧详情
      if (currentSessionRef.current?.id === id) {
        setCurrentSession(null);
        setMessages([]);
      }
      fetchSessions();
      fetchOverview();
      fetchTrashCount();
    } catch (e: unknown) {
      toast.error(getApiErrorMessage(e, t('chat.page.deleteFailed')));
    } finally {
      if (isMountedRef.current) setActionLoading(false);
    }
  }, [deleteConfirm, t, toast, fetchSessions, fetchOverview, fetchTrashCount]);

  // 批量删除（软删除 → 移入垃圾箱）
  const confirmBulkDelete = useCallback(async () => {
    if (selectedIds.size === 0) return;
    setActionLoading(true);
    try {
      await chatApi.bulkDeleteSessions(Array.from(selectedIds));
      toast.success(t('chat.page.bulkDeleted', { count: selectedIds.size }));
      setDeleteConfirm(null);
      setSelectedIds(new Set());
      setSelectMode(false);
      // 如果当前打开的会话被删除，关闭右侧详情
      if (currentSessionRef.current && selectedIds.has(currentSessionRef.current.id)) {
        setCurrentSession(null);
        setMessages([]);
      }
      fetchSessions();
      fetchOverview();
      fetchTrashCount();
    } catch (e: unknown) {
      toast.error(getApiErrorMessage(e, t('chat.page.bulkDeleteFailed')));
    } finally {
      if (isMountedRef.current) setActionLoading(false);
    }
  }, [selectedIds, t, toast, fetchSessions, fetchOverview, fetchTrashCount]);

  // 恢复会话
  const handleRestoreSession = useCallback(
    async (id: number) => {
      setActionLoading(true);
      try {
        await chatApi.restoreSession(id);
        toast.success(t('chat.page.restored'));
        // 刷新垃圾箱列表（重置到第一页以反映最新状态）
        setTrashPage(1);
        fetchTrashCount();
        fetchOverview();
      } catch (e: unknown) {
        toast.error(getApiErrorMessage(e, t('chat.page.restoreFailed')));
      } finally {
        if (isMountedRef.current) setActionLoading(false);
      }
    },
    [toast, t, fetchTrashCount, fetchOverview],
  );

  // 打开彻底删除确认
  const handleHardDeleteSession = useCallback((id: number) => {
    setDeleteConfirm({ type: 'hard', id });
  }, []);

  // 彻底删除（不可恢复）
  const confirmHardDeleteSession = useCallback(async () => {
    const id = deleteConfirm?.id;
    if (!id) return;
    setActionLoading(true);
    try {
      await chatApi.hardDeleteSession(id);
      toast.success(t('chat.page.permanentlyDeleted'));
      setDeleteConfirm(null);
      setTrashPage(1);
      fetchTrashCount();
    } catch (e: unknown) {
      toast.error(getApiErrorMessage(e, t('chat.page.deleteFailed')));
    } finally {
      if (isMountedRef.current) setActionLoading(false);
    }
  }, [deleteConfirm, t, toast, fetchTrashCount]);

  // 打开清空垃圾箱确认
  const handleClearTrash = useCallback(() => {
    setDeleteConfirm({ type: 'clear' });
  }, []);

  // 清空垃圾箱（不可恢复）
  const confirmClearTrash = useCallback(async () => {
    setActionLoading(true);
    try {
      const result = await chatApi.clearTrash();
      toast.success(t('chat.page.trashCleared', { count: result.count }));
      setDeleteConfirm(null);
      setTrashList([]);
      setTrashCount(0);
      setTrashTotalItems(0);
      setTrashPage(1);
    } catch (e: unknown) {
      toast.error(getApiErrorMessage(e, t('chat.page.clearTrashFailed')));
    } finally {
      if (isMountedRef.current) setActionLoading(false);
    }
  }, [toast, t]);

  // 打开垃圾箱 Modal
  const handleOpenTrash = useCallback(() => {
    setTrashPage(1);
    if (trashSearchTimerRef.current !== null) clearTimeout(trashSearchTimerRef.current);
    trashSearchTimerRef.current = null;
    setTrashKeyword('');
    setTrashKeywordInput('');
    setTrashOpen(true);
  }, []);

  // 垃圾箱搜索/翻页
  const handleTrashSearchChange = useCallback((v: string) => {
    setTrashKeywordInput(v);
    // 输入停顿 650ms 后自动搜索（期间继续输入会重置计时器）
    if (trashSearchTimerRef.current !== null) clearTimeout(trashSearchTimerRef.current);
    trashSearchTimerRef.current = setTimeout(() => applyTrashSearch(v), 650);
  }, [applyTrashSearch]);
  const handleTrashPageChange = useCallback((p: number) => {
    setTrashPage(p);
  }, []);

  // 删除确认弹窗的统一处理
  const handleConfirmConfirm = useCallback(() => {
    if (!deleteConfirm) return;
    if (deleteConfirm.type === 'single') confirmDeleteSession();
    else if (deleteConfirm.type === 'bulk') confirmBulkDelete();
    else if (deleteConfirm.type === 'hard') confirmHardDeleteSession();
    else if (deleteConfirm.type === 'clear') confirmClearTrash();
  }, [
    deleteConfirm,
    confirmDeleteSession,
    confirmBulkDelete,
    confirmHardDeleteSession,
    confirmClearTrash,
  ]);

  const handleConfirmCancel = useCallback(() => {
    setDeleteConfirm(null);
  }, []);

  // 删除确认弹窗的文案
  const deleteConfirmConfig = deleteConfirm
    ? (() => {
        if (deleteConfirm.type === 'single')
          return {
            title: t('chat.page.moveToTrash'),
            message: t('chat.page.moveToTrashMessage'),
            confirmText: t('chat.page.moveToTrash'),
            variant: 'danger' as const,
          };
        if (deleteConfirm.type === 'bulk')
          return {
            title: t('chat.page.bulkMoveToTrash'),
            message: t('chat.page.bulkMoveToTrashMessage', { count: selectedIds.size }),
            confirmText: t('chat.page.bulkMoveToTrash'),
            variant: 'danger' as const,
          };
        if (deleteConfirm.type === 'hard')
          return {
            title: t('chat.page.permanentlyDelete'),
            message: t('chat.page.permanentlyDeleteMessage'),
            confirmText: t('chat.page.permanentlyDelete'),
            variant: 'danger' as const,
          };
        return {
          title: t('chat.page.clearTrash'),
          message: t('chat.page.clearTrashMessage'),
          confirmText: t('chat.page.clearTrash'),
          variant: 'danger' as const,
        };
      })()
    : null;

  // ====== 概览统计卡片配置 ======
  const statCards: StatCardConfig[] = overview
    ? [
        {
          key: 'total',
          labelKey: 'chat.page.statTotal',
          icon: <MessageCircle className="w-5 h-5 text-slate-600 dark:text-slate-300" />,
          valueClass: 'text-slate-900 dark:text-white',
          iconBg: 'bg-gradient-to-br from-slate-100 to-slate-200 dark:from-slate-700 dark:to-slate-600',
        },
        {
          key: 'waiting',
          labelKey: 'chat.status.waiting',
          icon: <Clock className="w-5 h-5 text-amber-600 dark:text-amber-400" />,
          valueClass: 'text-amber-600 dark:text-amber-400',
          iconBg: 'bg-gradient-to-br from-amber-100 to-amber-200 dark:from-amber-900/30 dark:to-amber-800/30',
        },
        {
          key: 'active',
          labelKey: 'chat.status.active',
          icon: <Users className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />,
          valueClass: 'text-emerald-600 dark:text-emerald-400',
          iconBg: 'bg-gradient-to-br from-emerald-100 to-emerald-200 dark:from-emerald-900/30 dark:to-emerald-800/30',
        },
        {
          key: 'closed',
          labelKey: 'chat.status.closed',
          icon: <Archive className="w-5 h-5 text-slate-600 dark:text-slate-400" />,
          valueClass: 'text-slate-600 dark:text-slate-400',
          iconBg: 'bg-gradient-to-br from-slate-100 to-slate-200 dark:from-slate-700 dark:to-slate-600',
        },
        {
          key: 'unread',
          labelKey: 'chat.page.statUnread',
          icon: <AlertCircle className="w-5 h-5 text-blue-600 dark:text-blue-400" />,
          valueClass: 'text-blue-600 dark:text-blue-400',
          iconBg: 'bg-gradient-to-br from-blue-100 to-blue-200 dark:from-blue-900/30 dark:to-blue-800/30',
        },
      ]
    : [];

  // ====== 渲染 ======
  return (
    <div className="h-[calc(100vh-64px)] flex overflow-hidden">
      {/* 左栏:会话列表(桌面始终显示,移动端未选中时显示) */}
      <div
        className={`${
          currentSession ? 'hidden lg:flex' : 'flex'
        } w-full lg:w-[260px] xl:w-[260px] 2xl:w-[260px] flex-shrink-0 min-w-0`}
      >
        <ChatSessionList
          sessions={sessions}
          loading={loading}
          activeSessionId={currentSession?.id ?? null}
          searchKeyword={searchKeyword}
          statusFilter={statusFilter}
          mailboxes={mailboxes}
          mailboxFilter={mailboxFilter}
          page={page}
          totalPages={totalPages}
          totalItems={totalItems}
          pageSize={PAGE_SIZE}
          onSearchChange={handleSearchChange}
          onStatusChange={handleStatusChange}
          onMailboxChange={handleMailboxChange}
          onSelectSession={handleSelectSession}
          onAssignSession={handleAssignSession}
          onPageChange={handlePageChange}
          canManage={canManage}
          selectedIds={selectedIds}
          onSelectMode={selectMode}
          onToggleSelect={handleToggleSelect}
          onToggleSelectMode={handleToggleSelectMode}
          onSelectAll={handleSelectAll}
          onDeleteSession={handleDeleteSession}
          onBulkDelete={handleBulkDelete}
          onOpenTrash={handleOpenTrash}
          trashCount={trashCount}
        />
      </div>

      {/* 中栏:概览或聊天(桌面始终显示,移动端选中时显示) */}
      <div
        className={`${
          currentSession ? 'flex' : 'hidden lg:flex'
        } flex-1 min-w-0`}
      >
        {currentSession ? (
          <ChatConversation
            session={currentSession}
            messages={messages}
            loading={messagesLoading}
            onBack={handleBack}
            onSend={handleSendMessage}
            onTransfer={() => setShowTransferModal(true)}
            onClose={() => setShowConfirmClose(true)}
          />
        ) : (
          <ChatOverview
            overview={overview}
            onRefresh={handleRefresh}
            isRefreshing={isRefreshing}
            statCards={statCards}
            onAssignSession={handleAssignSession}
          />
        )}
      </div>

      {/* 右栏:信息面板(桌面选中时显示) */}
      {currentSession && infoPanelOpen && (
        <div className="hidden lg:flex w-80 flex-shrink-0 min-w-0">
          <ChatInfoPanel
            session={currentSession}
            onJumpToConversation={handleJumpToConversation}
          />
        </div>
      )}

      {/* 关闭会话确认 */}
      <ConfirmDialog
        open={showConfirmClose}
        title={t('chat.page.confirmClose')}
        message={t('chat.page.confirmCloseMessage')}
        confirmText={t('chat.page.confirmClose')}
        variant="danger"
        onConfirm={handleCloseSession}
        onCancel={() => setShowConfirmClose(false)}
      />

      {/* 转工单模态窗 */}
      <ChatTransferModal
        open={showTransferModal}
        onClose={() => setShowTransferModal(false)}
        onConfirm={handleTransferToTicket}
        mailboxes={mailboxes}
        users={users}
        customerName={currentSession?.customerName}
      />

      {/* 垃圾箱 Modal */}
      {canManage && (
        <Modal
          open={trashOpen}
          title={t('chat.page.trashTitle')}
          onClose={() => setTrashOpen(false)}
          size="xl"
          footer={
            <>
              <button
                onClick={handleClearTrash}
                disabled={trashList.length === 0 || actionLoading}
                className="w-full sm:w-auto px-4 py-2.5 text-xs sm:text-sm font-medium text-red-600 bg-red-50 hover:bg-red-100 border border-red-200 rounded-xl disabled:opacity-50 disabled:cursor-not-allowed transition-all"
              >
                {t('chat.page.clearTrash')}
              </button>
              <button
                onClick={() => setTrashOpen(false)}
                className="w-full sm:w-auto px-4 py-2.5 text-xs sm:text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
              >
                {t('common.close')}
              </button>
            </>
          }
        >
          <div className="space-y-4">
            {/* 搜索框 */}
            <div className="relative">
              <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
              <input
                type="text"
                placeholder={t('chat.page.trashSearchPlaceholder')}
                value={trashKeywordInput}
                onChange={(e) => handleTrashSearchChange(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') triggerTrashSearchNow();
                }}
                className="w-full pl-9 pr-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-900"
              />
            </div>

            {/* 数量统计 */}
            <div className="text-sm text-gray-500 dark:text-slate-400">
              {t('chat.page.trashCountBefore')}<span className="font-semibold text-gray-900 dark:text-slate-100">{trashTotalItems}</span>{t('chat.page.trashCountAfter', { count: trashTotalItems })}
            </div>

            {/* 列表 */}
            <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-100 dark:border-slate-700/50 overflow-hidden">
              {trashLoading ? (
                <div className="p-8 text-center">
                  <div className="inline-block animate-spin rounded-full h-6 w-6 border-4 border-primary-200 dark:border-primary-800 border-t-primary-600"></div>
                  <p className="mt-2 text-sm text-gray-500 dark:text-slate-400">{t('common.shell.loading')}</p>
                </div>
              ) : trashList.length === 0 ? (
                <div className="p-8 text-center">
                  <div className="inline-flex items-center justify-center w-12 h-12 rounded-full bg-gray-100 dark:bg-slate-700/50 mb-3">
                    <Archive className="w-6 h-6 text-gray-400" />
                  </div>
                  <h3 className="text-sm font-medium text-gray-900 dark:text-slate-100 mb-1">{t('chat.page.trashEmpty')}</h3>
                  <p className="text-sm text-gray-500 dark:text-slate-400">
                    {trashKeyword ? t('chat.page.trashNoMatch') : t('chat.page.trashEmptyHint')}
                  </p>
                </div>
              ) : (
                <>
                <div className="overflow-x-auto hidden md:block">
                  <table className="min-w-full divide-y divide-gray-100 dark:divide-slate-700/50">
                    <thead className="bg-gray-50 dark:bg-slate-900/50">
                      <tr>
                        <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('chat.page.colCustomer')}</th>
                        <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('chat.page.colEmail')}</th>
                        <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('chat.page.colStatus')}</th>
                        <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('chat.page.colDeletedAt')}</th>
                        <th className="px-4 py-3 text-right text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('chat.page.colActions')}</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100 dark:divide-slate-700/30">
                      {trashList.map((s) => (
                        <tr key={s.id} className="hover:bg-gray-50 dark:hover:bg-slate-700/30 transition-colors">
                          <td className="px-4 py-3">
                            <div className="flex items-center gap-2">
                              <div className="w-7 h-7 rounded-full bg-gradient-to-br from-slate-400 to-slate-500 flex items-center justify-center text-white text-xs font-semibold flex-shrink-0">
                                {s.customerName?.charAt(0) || '?'}
                              </div>
                              <span className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate max-w-[160px]">
                                {s.customerName || t('chat.page.unknownCustomerParen')}
                              </span>
                            </div>
                          </td>
                          <td className="px-4 py-3 text-sm text-gray-700 dark:text-slate-300 truncate max-w-[200px]">
                            {s.customerEmail || '-'}
                          </td>
                          <td className="px-4 py-3 text-sm">
                            <span className="inline-flex items-center px-2 py-0.5 rounded-full text-[11px] font-medium bg-slate-100 dark:bg-slate-700/50 text-slate-600 dark:text-slate-300">
                              {chatStatusLabel(t, s.status)}
                            </span>
                          </td>
                          <td className="px-4 py-3 text-xs text-gray-500 dark:text-slate-400 whitespace-nowrap">
                            {formatDateTime(s.deletedAt || s.updatedAt)}
                          </td>
                          <td className="px-4 py-3 text-sm">
                            <div className="flex items-center justify-end gap-1">
                              <button
                                onClick={() => handleRestoreSession(s.id)}
                                disabled={actionLoading}
                                className="p-1.5 text-gray-400 hover:text-green-600 hover:bg-green-50 dark:hover:bg-green-900/20 rounded-lg transition-all disabled:opacity-50"
                                title={t('chat.page.restore')}
                                aria-label={t('chat.page.restoreAria')}
                              >
                                <RotateCcw className="w-4 h-4" />
                              </button>
                              <button
                                onClick={() => handleHardDeleteSession(s.id)}
                                disabled={actionLoading}
                                className="p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg transition-all disabled:opacity-50"
                                title={t('chat.page.permanentlyDelete')}
                                aria-label={t('chat.page.permanentlyDeleteAria')}
                              >
                                <Trash2 className="w-4 h-4" />
                              </button>
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>

                {/* 移动端卡片视图 */}
                <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
                  {trashList.map((s) => (
                    <div key={s.id} className="p-3.5 hover:bg-gray-50/70 dark:hover:bg-slate-700/30 transition-colors">
                      <div className="flex items-center justify-between gap-2 mb-2">
                        <div className="flex items-center gap-2 min-w-0">
                          <div className="w-8 h-8 rounded-full bg-gradient-to-br from-slate-400 to-slate-500 flex items-center justify-center text-white text-xs font-semibold flex-shrink-0">
                            {s.customerName?.charAt(0) || '?'}
                          </div>
                          <span className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate">
                            {s.customerName || t('chat.page.unknownCustomerParen')}
                          </span>
                        </div>
                        <span className="inline-flex items-center px-2 py-0.5 rounded-full text-[11px] font-medium bg-slate-100 dark:bg-slate-700/50 text-slate-600 dark:text-slate-300 flex-shrink-0">
                          {chatStatusLabel(t, s.status)}
                        </span>
                      </div>
                      <div className="space-y-1 mb-2.5">
                        <p className="text-xs text-gray-500 dark:text-slate-400 truncate">
                          {t('chat.page.mobileEmail')}{s.customerEmail || '-'}
                        </p>
                        <p className="text-xs text-gray-500 dark:text-slate-400">
                          {t('chat.page.mobileDeletedAt')}{formatDateTime(s.deletedAt || s.updatedAt)}
                        </p>
                      </div>
                      <div className="flex items-center gap-2">
                        <button
                          onClick={() => handleRestoreSession(s.id)}
                          disabled={actionLoading}
                          className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-semibold text-green-600 bg-green-50 hover:bg-green-100 dark:bg-green-900/20 dark:hover:bg-green-900/30 border border-green-200 dark:border-green-800/40 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed transition-all"
                        >
                          <RotateCcw className="w-3.5 h-3.5" />
                          {t('chat.page.restore')}
                        </button>
                        <button
                          onClick={() => handleHardDeleteSession(s.id)}
                          disabled={actionLoading}
                          className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-semibold text-red-600 bg-red-50 hover:bg-red-100 dark:bg-red-900/20 dark:hover:bg-red-900/30 border border-red-200 dark:border-red-800/40 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed transition-all"
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                          {t('chat.page.permanentlyDelete')}
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
                </>
              )}
            </div>

            {/* 分页 */}
            {!trashLoading && trashTotalPages > 1 && (
              <div className="flex items-center justify-between">
                <div className="text-sm text-gray-500 dark:text-slate-400">
                  {t('chat.page.trashPageOf', { page: trashPage, totalPages: trashTotalPages })}
                </div>
                <div className="flex items-center gap-1">
                  <button
                    onClick={() => handleTrashPageChange(Math.max(1, trashPage - 1))}
                    disabled={trashPage <= 1}
                    className="inline-flex items-center justify-center w-8 h-8 text-gray-600 dark:text-slate-400 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 disabled:cursor-not-allowed transition-all"
                    aria-label={t('chat.page.prevPage')}
                  >
                    <ChevronLeft className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => handleTrashPageChange(Math.min(trashTotalPages, trashPage + 1))}
                    disabled={trashPage >= trashTotalPages}
                    className="inline-flex items-center justify-center w-8 h-8 text-gray-600 dark:text-slate-400 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 disabled:cursor-not-allowed transition-all"
                    aria-label={t('chat.page.nextPage')}
                  >
                    <ChevronRight className="w-4 h-4" />
                  </button>
                </div>
              </div>
            )}
          </div>
        </Modal>
      )}

      {/* 删除/批量删除/彻底删除/清空垃圾箱 统一确认（置于最后确保 z-index 层叠在 Modal 之上） */}
      <ConfirmDialog
        open={!!deleteConfirm}
        title={deleteConfirmConfig?.title ?? ''}
        message={deleteConfirmConfig?.message ?? ''}
        confirmText={deleteConfirmConfig?.confirmText ?? t('common.confirm')}
        variant={deleteConfirmConfig?.variant ?? 'danger'}
        loading={actionLoading}
        onConfirm={handleConfirmConfirm}
        onCancel={handleConfirmCancel}
      />
    </div>
  );
}

// ====== 概览页(中栏未选中会话时) ======
interface ChatOverviewProps {
  overview: ChatOverviewResponse | null;
  onRefresh: () => void;
  isRefreshing: boolean;
  statCards: StatCardConfig[];
  onAssignSession: (id: number) => void;
}

function ChatOverview({
  overview,
  onRefresh,
  isRefreshing,
  statCards,
  onAssignSession,
}: ChatOverviewProps) {
  const { t, language } = useI18n();
  const waitingSessions = overview?.waitingSessions ?? [];

  return (
    <div className="flex-1 overflow-y-auto bg-gradient-to-br from-slate-50 via-slate-50/50 to-indigo-50/20 dark:from-slate-900/60 dark:via-slate-900/40 dark:to-slate-900/60">
      {/* 页头 */}
      <div className="px-4 sm:px-6 py-4 sm:py-5 bg-white/90 dark:bg-slate-800/80 border-b border-slate-200/70 dark:border-slate-700/60 backdrop-blur-xl sticky top-0 z-10">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3.5">
            <div className="w-12 h-12 rounded-2xl bg-gradient-to-br from-blue-500 via-indigo-500 to-violet-600 flex items-center justify-center text-white shadow-lg shadow-indigo-500/25 ring-1 ring-white/40 dark:ring-white/10">
              <MessageSquare className="w-5 h-5" strokeWidth={2.2} />
            </div>
            <div>
              <h1 className="text-xl sm:text-2xl font-bold text-slate-900 dark:text-white tracking-tight">
                {t('chat.page.title')}
              </h1>
              <p className="text-xs sm:text-sm text-slate-500 dark:text-slate-400 mt-0.5">
                {t('chat.page.subtitle')}
              </p>
            </div>
          </div>
          <button
            onClick={onRefresh}
            disabled={isRefreshing}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold text-slate-600 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl hover:bg-slate-50 dark:hover:bg-slate-700/50 hover:shadow-sm hover:-translate-y-px disabled:opacity-60 disabled:cursor-not-allowed active:scale-[0.98] transition-all duration-200"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isRefreshing ? 'animate-spin' : ''}`} />
            {t('chat.page.refresh')}
          </button>
        </div>
      </div>

      <div className="p-4 sm:p-6 space-y-4 sm:space-y-5 max-w-[1400px] mx-auto">
        {/* 统计卡片 */}
        {overview && (
          <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-5 gap-3 sm:gap-4">
            {statCards.map((card) => {
              const value = overview.stats[card.key] ?? 0;
              return (
                <div
                  key={card.key}
                  className="relative bg-white dark:bg-slate-800 rounded-2xl p-4 border border-slate-200/70 dark:border-slate-700/60 shadow-sm hover:shadow-md hover:-translate-y-0.5 transition-all duration-200 overflow-hidden group"
                >
                  <div className="absolute inset-0 opacity-0 group-hover:opacity-100 transition-opacity duration-300 bg-gradient-to-br from-slate-50/50 to-transparent dark:from-slate-700/20" />
                  <div className="relative flex items-center justify-between mb-3">
                    <div
                      className={`w-10 h-10 rounded-xl flex items-center justify-center ${card.iconBg} ring-1 ring-white/60 dark:ring-white/10`}
                    >
                      {card.icon}
                    </div>
                  </div>
                  <p className="relative text-[11px] font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wider">
                    {t(card.labelKey)}
                  </p>
                  <p className={`relative text-3xl font-bold mt-0.5 tabular-nums ${card.valueClass}`}>
                    {value.toLocaleString(toIntlLocale(language))}
                  </p>
                </div>
              );
            })}
          </div>
        )}

        {/* 等待接待快速列表 */}
        {waitingSessions.length > 0 && (
          <div className="bg-white dark:bg-slate-800 rounded-2xl border border-amber-200/70 dark:border-amber-800/40 overflow-hidden shadow-sm">
            <div className="px-4 sm:px-5 py-3 sm:py-3.5 border-b border-amber-200/50 dark:border-amber-800/30 flex items-center justify-between bg-gradient-to-r from-amber-50/80 to-orange-50/60 dark:from-amber-900/15 dark:to-orange-900/10">
              <h3 className="text-sm font-bold text-slate-900 dark:text-white flex items-center gap-2">
                <span className="w-6 h-6 rounded-lg bg-gradient-to-br from-amber-400 to-orange-500 flex items-center justify-center shadow-sm">
                  <AlertCircle className="w-3.5 h-3.5 text-white" />
                </span>
                {t('chat.status.waiting')}
              </h3>
              <span className="px-2.5 py-0.5 bg-gradient-to-r from-amber-500 to-orange-500 text-white text-[11px] font-bold rounded-full shadow-sm tabular-nums">
                {waitingSessions.length}
              </span>
            </div>
            <div className="divide-y divide-amber-100/50 dark:divide-amber-800/20">
              {waitingSessions.slice(0, 5).map((session) => (
                <div
                  key={session.id}
                  className="px-4 sm:px-5 py-3 hover:bg-amber-50/40 dark:hover:bg-amber-900/8 transition-colors flex items-center justify-between gap-3 group"
                >
                  <div className="flex items-center gap-3 min-w-0">
                    <div className="relative">
                      <div className="w-10 h-10 rounded-xl bg-gradient-to-br from-amber-400 to-orange-500 flex items-center justify-center text-white text-sm font-bold flex-shrink-0 shadow-md ring-2 ring-white dark:ring-slate-800">
                        {session.customerName?.charAt(0) || '?'}
                      </div>
                      <span className="absolute -bottom-0.5 -right-0.5 w-3 h-3 rounded-full bg-amber-500 border-2 border-white dark:border-slate-800 animate-pulse" />
                    </div>
                    <div className="min-w-0">
                      <p className="text-sm font-semibold text-slate-900 dark:text-white truncate">
                        {session.customerName || t('chat.page.unknownCustomer')}
                      </p>
                      <p className="text-xs text-slate-500 dark:text-slate-400 truncate">
                        {session.customerEmail || '—'}
                      </p>
                    </div>
                  </div>
                  <button
                    onClick={() => onAssignSession(session.id)}
                    className="inline-flex items-center gap-1 px-3.5 py-1.5 text-xs font-bold text-white bg-gradient-to-r from-amber-500 to-orange-500 rounded-xl hover:from-amber-600 hover:to-orange-600 transition-all shadow-md hover:shadow-lg hover:-translate-y-px flex-shrink-0"
                  >
                    <Phone className="w-3.5 h-3.5" strokeWidth={2.2} />
                    {t('chat.page.accept')}
                  </button>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* 空状态提示 */}
        {!overview && (
          <div className="flex flex-col items-center justify-center py-20 text-slate-400">
            <div className="relative">
              <div className="w-16 h-16 rounded-2xl bg-gradient-to-br from-slate-100 to-slate-50 dark:from-slate-700/60 dark:to-slate-800/60 flex items-center justify-center mb-4 shadow-sm border border-slate-200/60 dark:border-slate-600/40">
                <MessageCircle className="w-8 h-8 text-slate-400 dark:text-slate-300" />
              </div>
              <div className="absolute -bottom-1 -right-1 w-5 h-5 rounded-full bg-amber-400 border-2 border-white dark:border-slate-800 animate-pulse" />
            </div>
            <p className="text-sm font-bold text-slate-600 dark:text-slate-200">{t('chat.page.noOverview')}</p>
            <p className="text-xs mt-1.5 text-slate-400 dark:text-slate-500">{t('chat.page.noOverviewHint')}</p>
          </div>
        )}

        {/* 提示条 */}
        {overview && waitingSessions.length === 0 && overview.stats.total === 0 && (
          <div className="flex flex-col items-center justify-center py-20 text-slate-400">
            <div className="relative">
              <div className="w-16 h-16 rounded-2xl bg-gradient-to-br from-slate-100 to-slate-50 dark:from-slate-700/60 dark:to-slate-800/60 flex items-center justify-center mb-4 shadow-sm border border-slate-200/60 dark:border-slate-600/40">
                <MessageCircle className="w-8 h-8 text-slate-400 dark:text-slate-300" />
              </div>
              <div className="absolute -bottom-1 -right-1 w-5 h-5 rounded-full bg-emerald-400 border-2 border-white dark:border-slate-800" />
            </div>
            <p className="text-sm font-bold text-slate-600 dark:text-slate-200">{t('chat.page.noSessions')}</p>
            <p className="text-xs mt-1.5 text-slate-400 dark:text-slate-500">{t('chat.page.noSessionsHint')}</p>
          </div>
        )}
      </div>
    </div>
  );
}
