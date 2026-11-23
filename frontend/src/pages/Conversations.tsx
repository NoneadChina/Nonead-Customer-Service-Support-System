import { useEffect, useState, useMemo, useCallback, useRef } from 'react';
import { Link } from 'react-router-dom';
import { useQueryClient, useIsFetching } from '@tanstack/react-query';
import type { ConversationResponse, PagedResponse, TagResponse } from '../api/types';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import PriorityIcon from '../components/PriorityIcon';
import SourceTypeIcon from '../components/SourceTypeIcon';
import { useSignalREvent } from '../hooks/useSignalR';
import { useAutoRefresh } from '../hooks/useAutoRefresh';
import signalRService, { SignalREvents } from '../services/signalR';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import { getPriorityLabel, getPriorityColor, getSourceTypeLabel, getSourceTypeColor, formatDateTime, safeUrl } from '../utils/format';
import { usePermission } from '../hooks/usePermission';
import { useModules } from '../context/ModuleContext';
import {
  queryKeys,
  useConversations,
  useAllMailboxes,
  useAssignableUsers,
  useTags,
  useStarredIds,
  useBulkAssign,
  useBulkUpdateStatus,
  useBulkUpdatePriority,
  useBulkDelete,
  useBulkAddTags,
  useTrashConversations,
  useRestoreConversation,
  useHardDeleteConversation,
  useClearTrash,
} from '../hooks/useConversations';

// 每页显示条数：超过 10 条即自动分页
const PAGE_SIZE = 10;

/**
 * 状态样式表。**只持有真源 key（labelKey）**，渲染时必须经 t() 解析——
 * 直接渲染表里的值会把 `format.status.active` 这样的原始 key 漏到界面上。
 * 文案真源是 `format` 模块（状态/优先级/来源类型等标签的公共来源），此处不重复造键。
 */
interface StatusStyle {
  labelKey: TranslationKey;
  color: string;
  darkColor: string;
}

const statusMap: Record<number, StatusStyle> = {
  1: { labelKey: 'format.status.active', color: 'bg-emerald-50 text-emerald-700 border-emerald-200', darkColor: 'dark:bg-emerald-900/30 dark:text-emerald-300 dark:border-emerald-700' },
  2: { labelKey: 'format.status.pending', color: 'bg-amber-50 text-amber-700 border-amber-200', darkColor: 'dark:bg-amber-900/30 dark:text-amber-300 dark:border-amber-700' },
  3: { labelKey: 'format.status.closed', color: 'bg-slate-50 text-slate-600 border-slate-200', darkColor: 'dark:bg-slate-700/40 dark:text-slate-300 dark:border-slate-600' },
  4: { labelKey: 'format.status.spam', color: 'bg-red-50 text-red-700 border-red-200', darkColor: 'dark:bg-red-900/30 dark:text-red-300 dark:border-red-700' },
};

/** 未知状态 / 未知类型的兜底样式（文案 key 指向 format.unknown） */
const fallbackStatus: StatusStyle = {
  labelKey: 'format.unknown',
  color: 'bg-gray-50 text-gray-600 border-gray-200',
  darkColor: 'dark:bg-slate-700/40 dark:text-slate-300 dark:border-slate-600',
};

// SVG 图标组件
function MailIcon({ className = 'w-4 h-4' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round">
      <rect x="2" y="4" width="20" height="16" rx="2" />
      <path d="M22 6l-10 7L2 6" />
    </svg>
  );
}

function PhoneIcon({ className = 'w-4 h-4' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round">
      <path d="M22 16.92v3a2 2 0 01-2.18 2 19.79 19.79 0 01-8.63-3.07 19.5 19.5 0 01-6-6 19.79 19.79 0 01-3.07-8.67A2 2 0 014.11 2h3a2 2 0 012 1.72 12.84 12.84 0 00.7 2.81 2 2 0 01-.45 2.11L8.09 9.91a16 16 0 006 6l1.27-1.27a2 2 0 012.11-.45 12.84 12.84 0 002.81.7A2 2 0 0122 16.92z" />
    </svg>
  );
}

function ChatIcon({ className = 'w-4 h-4' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round">
      <path d="M21 15a2 2 0 01-2 2H7l-4 4V5a2 2 0 012-2h14a2 2 0 012 2z" />
    </svg>
  );
}

function PaperclipIcon({ className = 'w-4 h-4' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round">
      <path d="M21.44 11.05l-9.19 9.19a6 6 0 01-8.49-8.49l9.19-9.19a4 4 0 015.66 5.66l-9.2 9.19a2 2 0 01-2.83-2.83l8.49-8.48" />
    </svg>
  );
}

function SearchIcon({ className = 'w-4 h-4' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
      <circle cx="11" cy="11" r="8" />
      <path d="M21 21l-4.35-4.35" />
    </svg>
  );
}

function EmptyInboxIcon({ className = 'w-20 h-20' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 120 120" fill="none" xmlns="http://www.w3.org/2000/svg">
      <rect x="15" y="30" width="90" height="65" rx="8" className="stroke-gray-200 dark:stroke-slate-600" strokeWidth="3" fill="none" />
      <path d="M15 65h30l8 12h14l8-12h30" className="stroke-gray-200 dark:stroke-slate-600" strokeWidth="3" fill="none" strokeLinejoin="round" />
      <circle cx="60" cy="22" r="3" className="fill-gray-200 dark:fill-slate-600" />
      <path d="M45 50 L60 40 L75 50" className="stroke-gray-300 dark:stroke-slate-500" strokeWidth="2.5" fill="none" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

interface ConvTypeStyle {
  labelKey: TranslationKey;
  icon: React.FC<{ className?: string }>;
  color: string;
}

const typeConfig: Record<number, ConvTypeStyle> = {
  1: { labelKey: 'format.sourceType.email', icon: MailIcon, color: 'text-blue-500 dark:text-blue-400' },
  2: { labelKey: 'format.sourceType.phone', icon: PhoneIcon, color: 'text-violet-500 dark:text-violet-400' },
  3: { labelKey: 'format.sourceType.chat', icon: ChatIcon, color: 'text-teal-500 dark:text-teal-400' },
};

const fallbackConvType: ConvTypeStyle = {
  labelKey: 'format.unknown',
  icon: MailIcon,
  color: 'text-gray-400 dark:text-slate-500',
};

/** 状态筛选 / 批量状态选项（两处复用同一份，标签经 t() 解析） */
const STATUS_OPTIONS: { value: number; labelKey: TranslationKey; color: string }[] = [
  { value: 1, labelKey: 'format.status.active', color: 'bg-green-100 text-green-700' },
  { value: 2, labelKey: 'format.status.pending', color: 'bg-amber-100 text-amber-700' },
  { value: 3, labelKey: 'format.status.closed', color: 'bg-gray-100 text-gray-600' },
  { value: 4, labelKey: 'format.status.spam', color: 'bg-red-100 text-red-700' },
];

/** 优先级筛选 / 批量优先级选项（0=低 … 3=紧急；筛选下拉按紧急优先倒序展示） */
const PRIORITY_OPTIONS: { value: number; labelKey: TranslationKey; color: string }[] = [
  { value: 0, labelKey: 'format.priority.low', color: 'bg-sky-100 text-sky-700 border-sky-200' },
  { value: 1, labelKey: 'format.priority.normal', color: 'bg-emerald-100 text-emerald-700 border-emerald-200' },
  { value: 2, labelKey: 'format.priority.high', color: 'bg-orange-100 text-orange-700 border-orange-200' },
  { value: 3, labelKey: 'format.priority.urgent', color: 'bg-red-100 text-red-700 border-red-200' },
];

/** 来源筛选下拉项（code 稳定，标签走真源 format.sourceType.*） */
const SOURCE_TYPE_OPTIONS: { value: number; labelKey: TranslationKey }[] = [
  { value: 1, labelKey: 'format.sourceType.email' },
  { value: 2, labelKey: 'format.sourceType.web' },
  { value: 3, labelKey: 'format.sourceType.api' },
  { value: 4, labelKey: 'format.sourceType.phone' },
  { value: 5, labelKey: 'format.sourceType.chat' },
  { value: 6, labelKey: 'format.sourceType.social' },
  { value: 7, labelKey: 'format.sourceType.wechat' },
  { value: 8, labelKey: 'format.sourceType.whatsapp' },
  { value: 9, labelKey: 'format.sourceType.feishu' },
  { value: 10, labelKey: 'format.sourceType.dingtalk' },
  { value: 11, labelKey: 'format.sourceType.messenger' },
  { value: 12, labelKey: 'format.sourceType.telegram' },
  { value: 13, labelKey: 'format.sourceType.line' },
  { value: 14, labelKey: 'format.sourceType.imessage' },
];

export default function Conversations() {
  const queryClient = useQueryClient();
  const { t } = useI18n();
  const { canAssignConversations } = usePermission();
  const { isModuleActive } = useModules();

  // 刷新状态：useIsFetching 检测 conversations 相关查询是否正在拉取，
  // + refreshLock 提供短暂防抖锁避免用户连续点击造成重复请求
  const isConversationsFetching = useIsFetching({ queryKey: queryKeys.conversations.all }) > 0;
  const isStarredFetching = useIsFetching({ queryKey: queryKeys.conversations.starredIds }) > 0;
  const [refreshLock, setRefreshLock] = useState(false);
  const isRefreshing = isConversationsFetching || isStarredFetching || refreshLock;

  const handleRefresh = useCallback(() => {
    if (isRefreshing) return;
    setRefreshLock(true);
    try {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.starredIds });
    } finally {
      // 至少 400ms 锁定期，避免图标刚转就停的闪烁感 + 防止连点
      setTimeout(() => setRefreshLock(false), 400);
    }
  }, [isRefreshing, queryClient]);

  // 筛选条件
  const [filterMailbox, setFilterMailbox] = useState<number | ''>('');
  const [filterStatus, setFilterStatus] = useState<number | ''>('');
  const [filterPriority, setFilterPriority] = useState<number | ''>('');
  const [filterFollowed, setFilterFollowed] = useState<number | ''>('');
  const [filterSourceType, setFilterSourceType] = useState<number | ''>('');
  const [searchKeyword, setSearchKeyword] = useState('');
  const [currentPage, setCurrentPage] = useState(1);

  // 搜索防抖：输入 650ms 停顿后才写入 searchKeyword（queryKey/effect 逻辑不变），
  // 缓冲值 searchInput 用于输入框即时回显；Enter 立即触发搜索
  const [searchInput, setSearchInput] = useState('');
  const searchTimerRef = useRef<number | null>(null);
  const applySearch = useCallback((value: string) => {
    setSearchKeyword(value);
  }, []);
  const handleSearchInput = useCallback((value: string) => {
    setSearchInput(value);
    if (searchTimerRef.current !== null) window.clearTimeout(searchTimerRef.current);
    searchTimerRef.current = window.setTimeout(() => applySearch(value), 650);
  }, [applySearch]);
  const triggerSearchNow = useCallback(() => {
    if (searchTimerRef.current !== null) {
      window.clearTimeout(searchTimerRef.current);
      searchTimerRef.current = null;
    }
    applySearch(searchInput);
  }, [applySearch, searchInput]);
  const clearSearch = useCallback(() => {
    if (searchTimerRef.current !== null) {
      window.clearTimeout(searchTimerRef.current);
      searchTimerRef.current = null;
    }
    setSearchInput('');
    setSearchKeyword('');
  }, []);
  useEffect(() => {
    return () => {
      if (searchTimerRef.current !== null) window.clearTimeout(searchTimerRef.current);
    };
  }, []);

  // React Query 数据获取 — 服务端真分页：传入 page/pageSize 和筛选条件 + 搜索关键词
  const { data: convData, isLoading: loading, error: queryError } = useConversations({
    mailboxId: filterMailbox === '' ? undefined : filterMailbox,
    page: currentPage,
    pageSize: PAGE_SIZE,
    status: filterStatus === '' ? undefined : filterStatus,
    priority: filterPriority === '' ? undefined : filterPriority,
    starred: filterFollowed !== '',
    sourceType: filterSourceType === '' ? undefined : filterSourceType,
    search: searchKeyword.trim() || undefined,
  });
  const { data: mailboxesData } = useAllMailboxes();
  // 使用 /api/users/assignable（conversations.assign 权限）而非 /api/users（SuperAdmin），
  // 确保客服主管也能加载"批量分配"下拉框（T000061）
  const { data: usersData } = useAssignableUsers();
  const { data: tagsData } = useTags();
  const { data: starredIdsData } = useStarredIds();
  const { data: trashData } = useTrashConversations(filterMailbox === '' ? 0 : filterMailbox);

  // 从服务端分页响应中提取数据和总数
  // convData 来自未显式泛型的 client.get，这里使用结构化类型访问替代 as any
  const pagedList = useMemo<ConversationResponse[]>(() => {
    if (!convData) return [];
    const resp = convData as { data?: ConversationResponse[] };
    const data = resp?.data;
    return Array.isArray(data) ? data : [];
  }, [convData]);

  const totalCount = useMemo(() => {
    if (!convData) return 0;
    const resp = convData as PagedResponse<ConversationResponse>;
    return resp?.totalItems ?? pagedList.length;
  }, [convData, pagedList]);

  const mailboxes = useMemo(() => {
    if (!mailboxesData) return [];
    return Array.isArray(mailboxesData) ? mailboxesData : [];
  }, [mailboxesData]);

  const users = useMemo(() => {
    if (!usersData) return [];
    return Array.isArray(usersData) ? usersData : [];
  }, [usersData]);

  const tags = useMemo<TagResponse[]>(() => {
    if (!tagsData) return [];
    return Array.isArray(tagsData) ? tagsData : [];
  }, [tagsData]);

  const starredIds = useMemo<Set<number>>(() => {
    if (!starredIdsData) return new Set();
    return new Set(Array.isArray(starredIdsData) ? starredIdsData : []);
  }, [starredIdsData]);

  const error = queryError instanceof Error ? queryError.message : '';

  const trashList = useMemo<ConversationResponse[]>(() => {
    if (!trashData) return [];
    return Array.isArray(trashData) ? trashData : (Array.isArray(trashData.data) ? trashData.data : []);
  }, [trashData]);

  const trashCount = useMemo(() => {
    if (!trashData) return 0;
    return trashData.totalItems ?? (Array.isArray(trashList) ? trashList.length : 0);
  }, [trashData, trashList]);

  // 多选
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());

  // 批量操作弹窗
  const [assignModalOpen, setAssignModalOpen] = useState(false);
  const [assignUserId, setAssignUserId] = useState<number | ''>('');
  const [batchSubmitting, setBatchSubmitting] = useState(false);
  const [batchError, setBatchError] = useState('');

  // 批量状态变更
  const [statusModalOpen, setStatusModalOpen] = useState(false);
  const [batchStatus, setBatchStatus] = useState<number>(1);

  // 批量优先级变更
  const [priorityModalOpen, setPriorityModalOpen] = useState(false);
  const [batchPriority, setBatchPriority] = useState<number>(1);

  // 批量删除确认
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);

  // 垃圾箱
  const [trashOpen, setTrashOpen] = useState(false);
  const [clearTrashOpen, setClearTrashOpen] = useState(false);
  const [clearingTrash, setClearingTrash] = useState(false);

  // 硬删除确认（替代 window.confirm，使用统一 ConfirmDialog）
  const [hardDeleteId, setHardDeleteId] = useState<number | null>(null);
  const [hardDeleteOpen, setHardDeleteOpen] = useState(false);
  const [hardDeleting, setHardDeleting] = useState(false);

  // 批量添加标签
  const [tagModalOpen, setTagModalOpen] = useState(false);
  const [selectedTagIds, setSelectedTagIds] = useState<Set<number>>(new Set());

  // 移动端筛选栏折叠
  const [mobileFilterOpen, setMobileFilterOpen] = useState(false);

  // 操作结果
  const [resultMsg, setResultMsg] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  // 筛选条件变化时重置分页到第1页并清空选择
  useEffect(() => {
    setCurrentPage(1);
    setSelectedIds(new Set());
  }, [filterMailbox, filterStatus, filterPriority, filterFollowed, filterSourceType, searchKeyword]);

  // 自动消失的结果消息
  useEffect(() => {
    if (resultMsg) {
      const timer = setTimeout(() => setResultMsg(null), 4000);
      return () => clearTimeout(timer);
    }
  }, [resultMsg]);

  // SignalR 实时刷新
  useSignalREvent(SignalREvents.RefreshConversations, (data: { mailboxId?: number; conversationId?: number }) => {
    if (filterMailbox === '' || data.mailboxId === filterMailbox) {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    }
  });

  useSignalREvent(SignalREvents.NewConversation, (data: { mailboxId?: number }) => {
    if (filterMailbox === '' || data.mailboxId === filterMailbox) {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    }
  });

  useSignalREvent(SignalREvents.NewReply, () => {
    queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
  });

  // 根据用户偏好自动刷新会话列表
  const refreshConversations = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
  }, [queryClient]);

  useAutoRefresh(refreshConversations, [filterMailbox, filterStatus, filterPriority, filterFollowed, filterSourceType, searchKeyword, currentPage]);

  // 加入邮箱组以接收实时通知
  useEffect(() => {
    if (filterMailbox) {
      signalRService.joinGroup(`mailbox_${filterMailbox}`);
    }
    return () => {
      if (filterMailbox) {
        signalRService.leaveGroup(`mailbox_${filterMailbox}`);
      }
    };
  }, [filterMailbox]);

  // ====== 分页计算（服务端真分页） ======
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  // 移动端筛选栏：当前已激活的筛选条件数量（不含快捷来源标签和搜索）
  const activeFilterCount = [
    filterMailbox !== '',
    filterStatus !== '',
    filterPriority !== '',
    filterFollowed !== '',
  ].filter(Boolean).length;

  // 生成分页按钮
  const getPageNumbers = () => {
    const pages: (number | 'ellipsis')[] = [];
    if (totalPages <= 7) {
      for (let i = 1; i <= totalPages; i++) pages.push(i);
    } else {
      pages.push(1);
      if (currentPage > 3) pages.push('ellipsis');
      const start = Math.max(2, currentPage - 1);
      const end = Math.min(totalPages - 1, currentPage + 1);
      for (let i = start; i <= end; i++) pages.push(i);
      if (currentPage < totalPages - 2) pages.push('ellipsis');
      pages.push(totalPages);
    }
    return pages;
  };

  // ====== 多选操作 ======
  const toggleSelect = (id: number) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const toggleSelectAll = () => {
    if (selectedIds.size === pagedList.length && pagedList.length > 0) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(pagedList.map((c) => c.id)));
    }
  };

  const clearSelection = () => setSelectedIds(new Set());

  const isAllSelected = pagedList.length > 0 && selectedIds.size === pagedList.length;
  const isPartialSelected = selectedIds.size > 0 && selectedIds.size < pagedList.length;

  // ====== 批量操作 ======
  const openAssignModal = () => {
    setAssignUserId('');
    setBatchError('');
    setAssignModalOpen(true);
  };

  const bulkAssignMutation = useBulkAssign();
  const bulkStatusMutation = useBulkUpdateStatus();
  const bulkPriorityMutation = useBulkUpdatePriority();
  const bulkDeleteMutation = useBulkDelete();
  const restoreMutation = useRestoreConversation();
  const hardDeleteMutation = useHardDeleteConversation();
  const clearTrashMutation = useClearTrash();
  const bulkAddTagsMutation = useBulkAddTags();

  const handleBatchAssign = async () => {
    if (!assignUserId) {
      setBatchError(t('conversations.list.errorSelectAssignee'));
      return;
    }
    setBatchSubmitting(true);
    setBatchError('');
    try {
      const ids = Array.from(selectedIds);
      await bulkAssignMutation.mutateAsync({ ids, userId: assignUserId as number });
      setResultMsg({ type: 'success', text: t('conversations.list.assignSuccess', { count: ids.length }) });
      setAssignModalOpen(false);
      clearSelection();
    } catch (err: unknown) {
      setBatchError(err instanceof Error ? err.message : t('conversations.list.assignFailed'));
    } finally {
      setBatchSubmitting(false);
    }
  };

  const handleBatchStatus = async () => {
    setBatchSubmitting(true);
    setBatchError('');
    try {
      const ids = Array.from(selectedIds);
      await bulkStatusMutation.mutateAsync({ ids, status: batchStatus });
      setResultMsg({ type: 'success', text: t('conversations.list.statusUpdateSuccess', { count: ids.length }) });
      setStatusModalOpen(false);
      clearSelection();
    } catch (err: unknown) {
      setBatchError(err instanceof Error ? err.message : t('conversations.list.statusUpdateFailed'));
    } finally {
      setBatchSubmitting(false);
    }
  };

  const handleBatchPriority = async () => {
    setBatchSubmitting(true);
    setBatchError('');
    try {
      const ids = Array.from(selectedIds);
      await bulkPriorityMutation.mutateAsync({ ids, priority: batchPriority });
      setResultMsg({ type: 'success', text: t('conversations.list.priorityUpdateSuccess', { count: ids.length }) });
      setPriorityModalOpen(false);
      clearSelection();
    } catch (err: unknown) {
      setBatchError(err instanceof Error ? err.message : t('conversations.list.priorityUpdateFailed'));
    } finally {
      setBatchSubmitting(false);
    }
  };

  const handleBatchDelete = async () => {
    setDeleting(true);
    try {
      const ids = Array.from(selectedIds);
      const resp = await bulkDeleteMutation.mutateAsync(ids);
      // 解析批量操作响应，处理部分失败
      const total = resp?.totalCount ?? ids.length;
      const success = resp?.successCount ?? total;
      const failed = resp?.failedCount ?? 0;
      const errors = resp?.errors ?? [];
      if (failed === 0) {
        setResultMsg({ type: 'success', text: t('conversations.list.deleteSuccess', { count: success }) });
      } else if (success === 0) {
        const details = errors.length > 0 ? errors.slice(0, 3).join('; ') : '';
        const base = t('conversations.list.deleteFailedAll', { count: failed });
        setResultMsg({
          type: 'error',
          text: details ? t('conversations.list.withFailureDetails', { message: base, details }) : base,
        });
      } else {
        const details = errors.length > 0 ? errors.slice(0, 2).join('; ') : '';
        const base = t('conversations.list.deletePartialSuccess', { success, failed, total });
        setResultMsg({
          type: 'error',
          text: details ? t('conversations.list.withFailureDetails', { message: base, details }) : base,
        });
      }
      setDeleteOpen(false);
      clearSelection();
    } catch (err: unknown) {
      setResultMsg({ type: 'error', text: err instanceof Error ? err.message : t('conversations.list.deleteFailed') });
    } finally {
      setDeleting(false);
    }
  };

  const openTrashModal = () => {
    setTrashOpen(true);
  };

  const handleRestoreFromTrash = async (id: number) => {
    try {
      await restoreMutation.mutateAsync(id);
      setResultMsg({ type: 'success', text: t('conversations.list.restoreSuccess', { id }) });
    } catch (err: unknown) {
      setResultMsg({ type: 'error', text: err instanceof Error ? err.message : t('conversations.list.restoreFailed') });
    }
  };

  const handleHardDeleteFromTrash = (id: number) => {
    setHardDeleteId(id);
    setHardDeleteOpen(true);
  };

  const confirmHardDelete = async () => {
    if (hardDeleteId === null) return;
    const id = hardDeleteId;
    setHardDeleting(true);
    try {
      await hardDeleteMutation.mutateAsync(id);
      setResultMsg({ type: 'success', text: t('conversations.list.hardDeleteSuccess', { id }) });
      setHardDeleteOpen(false);
      setHardDeleteId(null);
    } catch (err: unknown) {
      setResultMsg({ type: 'error', text: err instanceof Error ? err.message : t('conversations.list.hardDeleteFailed') });
    } finally {
      setHardDeleting(false);
    }
  };

  const handleClearTrash = async () => {
    const mbId = typeof filterMailbox === 'number' ? filterMailbox : 0;
    setClearingTrash(true);
    try {
      const resp = await clearTrashMutation.mutateAsync(mbId);
      const count = resp?.count ?? 0;
      setResultMsg({ type: 'success', text: t('conversations.list.clearTrashSuccess', { count }) });
      setClearTrashOpen(false);
    } catch (err: unknown) {
      setResultMsg({ type: 'error', text: err instanceof Error ? err.message : t('conversations.list.clearTrashFailed') });
    } finally {
      setClearingTrash(false);
    }
  };

  const openTagModal = () => {
    // 只选中1条会话时，预填充该会话已有的标签（API 为替换语义，便于查看/增删现有标签）
    if (selectedIds.size === 1) {
      const id = Array.from(selectedIds)[0];
      const conv = pagedList.find((c) => c.id === id);
      const existingTagIds = new Set<number>(conv?.tags?.map((t) => t.id) ?? []);
      setSelectedTagIds(existingTagIds);
    } else {
      setSelectedTagIds(new Set());
    }
    setTagModalOpen(true);
  };

  const toggleTagSelect = (tagId: number) => {
    setSelectedTagIds((prev) => {
      const next = new Set(prev);
      if (next.has(tagId)) {
        next.delete(tagId);
      } else {
        next.add(tagId);
      }
      return next;
    });
  };

  const handleBatchAddTags = async () => {
    // 单条会话允许清空所有标签（替换语义：空数组即删除全部标签）
    // 多条会话至少需选择一个标签，避免误清空
    const isSingle = selectedIds.size === 1;
    if (!isSingle && selectedTagIds.size === 0) {
      setBatchError(t('conversations.list.errorSelectTag'));
      return;
    }
    setBatchSubmitting(true);
    setBatchError('');
    try {
      const ids = Array.from(selectedIds);
      const tagIdArray = Array.from(selectedTagIds);
      await bulkAddTagsMutation.mutateAsync({ ids, tagIds: tagIdArray });
      setResultMsg({
        type: 'success',
        text: ids.length === 1
          ? t('conversations.list.tagUpdateSuccess')
          : t('conversations.list.tagAddSuccess', { count: ids.length }),
      });
      setTagModalOpen(false);
      clearSelection();
      setSelectedTagIds(new Set());
    } catch (err: unknown) {
      setBatchError(err instanceof Error ? err.message : t('conversations.list.tagUpdateFailed'));
    } finally {
      setBatchSubmitting(false);
    }
  };

  // 获取客户名称首字母
  const getInitial = (name: string) => {
    if (!name) return '?';
    return name.charAt(0).toUpperCase();
  };

  // 根据名称生成一致的颜色
  const getAvatarColor = (name: string) => {
    const colors = [
      'bg-blue-500 dark:bg-blue-600',
      'bg-emerald-500 dark:bg-emerald-600',
      'bg-violet-500 dark:bg-violet-600',
      'bg-amber-500 dark:bg-amber-600',
      'bg-rose-500 dark:bg-rose-600',
      'bg-cyan-500 dark:bg-cyan-600',
      'bg-indigo-500 dark:bg-indigo-600',
      'bg-teal-500 dark:bg-teal-600',
    ];
    let hash = 0;
    for (let i = 0; i < name.length; i++) {
      hash = name.charCodeAt(i) + ((hash << 5) - hash);
    }
    return colors[Math.abs(hash) % colors.length];
  };

  return (
    <div className="space-y-5">
      {/* 页面标题 */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100">{t('conversations.list.title')}</h1>
          <p className="hidden sm:block text-sm text-gray-500 dark:text-slate-400 mt-1">{t('conversations.list.subtitle')}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <button
            onClick={handleRefresh}
            disabled={isRefreshing}
            title={t('conversations.list.refresh')}
            className="inline-flex items-center gap-1.5 px-3 py-2 sm:px-4 sm:py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-xl shadow-sm hover:bg-gray-50 dark:hover:bg-slate-700 hover:shadow disabled:opacity-60 disabled:cursor-not-allowed active:scale-[0.98] transition-all duration-200"
          >
            <svg
              className={`w-4 h-4 ${isRefreshing ? 'animate-spin' : ''}`}
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              strokeWidth={2}
            >
              <path strokeLinecap="round" strokeLinejoin="round" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
            </svg>
            <span className="hidden sm:inline">{t('conversations.list.refresh')}</span>
          </button>
          <button
            onClick={openTrashModal}
            title={t('conversations.list.trash')}
            className="inline-flex items-center gap-1.5 px-3 py-2 sm:px-4 sm:py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-xl shadow-sm hover:bg-red-50 dark:hover:bg-red-900/20 hover:border-red-300 dark:hover:border-red-700 hover:text-red-700 dark:hover:text-red-300 transition-all duration-200"
          >
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
            </svg>
            <span className="hidden sm:inline">{t('conversations.list.trash')}</span>
            {trashCount > 0 && (
              <span className="px-1.5 py-0.5 text-xs rounded-full bg-red-100 dark:bg-red-900/30 text-red-600 dark:text-red-400">
                {trashCount}
              </span>
            )}
          </button>
        </div>
      </div>

      {/* 操作结果提示 */}
      {resultMsg && (
        <div className={`px-4 py-3 rounded-xl text-sm font-medium shadow-sm ${
          resultMsg.type === 'success'
            ? 'bg-emerald-50 border border-emerald-200 text-emerald-700 dark:bg-emerald-900/30 dark:border-emerald-700 dark:text-emerald-300'
            : 'bg-red-50 border border-red-200 text-red-700 dark:bg-red-900/30 dark:border-red-700 dark:text-red-300'
        }`}>
          <div className="flex items-center gap-2">
            {resultMsg.type === 'success' ? (
              <svg className="w-5 h-5 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
            ) : (
              <svg className="w-5 h-5 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
            )}
            {resultMsg.text}
          </div>
        </div>
      )}

      {/* 筛选栏 - 精致卡片样式 */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 shadow-sm p-3 sm:p-5">
        {/* 快捷来源标签 */}
        <div className="flex items-center gap-2 mb-4 flex-wrap">
          <button
            onClick={() => setFilterSourceType('')}
            className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-medium transition ${
              filterSourceType === ''
                ? 'bg-primary-100 text-primary-700 dark:bg-primary-900/40 dark:text-primary-300'
                : 'bg-gray-100 text-gray-500 hover:bg-gray-200 dark:bg-slate-700 dark:text-slate-400 dark:hover:bg-slate-600'
            }`}
          >
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M4 6h16M4 10h16M4 14h16M4 18h16" />
            </svg>
            {t('conversations.list.all')}
          </button>
          <button
            onClick={() => setFilterSourceType(1)}
            className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-medium transition ${
              filterSourceType === 1
                ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300'
                : 'bg-gray-100 text-gray-500 hover:bg-gray-200 dark:bg-slate-700 dark:text-slate-400 dark:hover:bg-slate-600'
            }`}
          >
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
            </svg>
            {t('format.sourceType.email')}
          </button>
          {isModuleActive('wechat') && (
            <button
              onClick={() => setFilterSourceType(7)}
              className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-medium transition ${
                filterSourceType === 7
                  ? 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300'
                  : 'bg-gray-100 text-gray-500 hover:bg-gray-200 dark:bg-slate-700 dark:text-slate-400 dark:hover:bg-slate-600'
              }`}
            >
              <SourceTypeIcon sourceType={7} className="w-3.5 h-3.5" />
              {t('format.sourceType.wechat')}
            </button>
          )}
          {isModuleActive('messenger') && (
            <button
              onClick={() => setFilterSourceType(11)}
              className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-medium transition ${
                filterSourceType === 11
                  ? 'violet-100 violet-700 dark:bg-violet-900/40 dark:text-violet-300'
                  : 'bg-gray-100 text-gray-500 hover:bg-gray-200 dark:bg-slate-700 dark:text-slate-400 dark:hover:bg-slate-600'
              }`}
            >
              <SourceTypeIcon sourceType={11} className="w-3.5 h-3.5" />
              {t('format.sourceType.messenger')}
            </button>
          )}
          {isModuleActive('telegram') && (
            <button
              onClick={() => setFilterSourceType(12)}
              className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-medium transition ${
                filterSourceType === 12
                  ? 'sky-100 sky-700 dark:bg-sky-900/40 dark:text-sky-300'
                  : 'bg-gray-100 text-gray-500 hover:bg-gray-200 dark:bg-slate-700 dark:text-slate-400 dark:hover:bg-slate-600'
              }`}
            >
              <SourceTypeIcon sourceType={12} className="w-3.5 h-3.5" />
              {t('format.sourceType.telegram')}
            </button>
          )}
          {isModuleActive('line') && (
            <button
              onClick={() => setFilterSourceType(13)}
              className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-medium transition ${
                filterSourceType === 13
                  ? 'green-100 green-700 dark:bg-green-900/40 dark:text-green-300'
                  : 'bg-gray-100 text-gray-500 hover:bg-gray-200 dark:bg-slate-700 dark:text-slate-400 dark:hover:bg-slate-600'
              }`}
            >
              <SourceTypeIcon sourceType={13} className="w-3.5 h-3.5" />
              {t('format.sourceType.line')}
            </button>
          )}
          {isModuleActive('imessage') && (
            <button
              onClick={() => setFilterSourceType(14)}
              className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-medium transition ${
                filterSourceType === 14
                  ? 'blue-100 blue-700 dark:bg-blue-900/40 dark:text-blue-300'
                  : 'bg-gray-100 text-gray-500 hover:bg-gray-200 dark:bg-slate-700 dark:text-slate-400 dark:hover:bg-slate-600'
              }`}
            >
              <SourceTypeIcon sourceType={14} className="w-3.5 h-3.5" />
              {t('format.sourceType.imessage')}
            </button>
          )}
          {isModuleActive('chat') && (
            <button
              onClick={() => setFilterSourceType(5)}
              className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-medium transition ${
                filterSourceType === 5
                  ? 'bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300'
                  : 'bg-gray-100 text-gray-500 hover:bg-gray-200 dark:bg-slate-700 dark:text-slate-400 dark:hover:bg-slate-600'
              }`}
            >
              <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
              </svg>
              {t('format.sourceType.chat')}
            </button>
          )}
          {isModuleActive('whatsapp') && (
            <button
              onClick={() => setFilterSourceType(8)}
              className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-medium transition ${
                filterSourceType === 8
                  ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300'
                  : 'bg-gray-100 text-gray-500 hover:bg-gray-200 dark:bg-slate-700 dark:text-slate-400 dark:hover:bg-slate-600'
              }`}
            >
              <SourceTypeIcon sourceType={8} className="w-3.5 h-3.5" />
              WhatsApp
            </button>
          )}
          {isModuleActive('feishu') && (
            <button
              onClick={() => setFilterSourceType(9)}
              className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-medium transition ${
                filterSourceType === 9
                  ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300'
                  : 'bg-gray-100 text-gray-500 hover:bg-gray-200 dark:bg-slate-700 dark:text-slate-400 dark:hover:bg-slate-600'
              }`}
            >
              <SourceTypeIcon sourceType={9} className="w-3.5 h-3.5" />
              {t('format.sourceType.feishu')}
            </button>
          )}
          {isModuleActive('dingding') && (
            <button
              onClick={() => setFilterSourceType(10)}
              className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-medium transition ${
                filterSourceType === 10
                  ? 'bg-indigo-100 text-indigo-700 dark:bg-indigo-900/40 dark:text-indigo-300'
                  : 'bg-gray-100 text-gray-500 hover:bg-gray-200 dark:bg-slate-700 dark:text-slate-400 dark:hover:bg-slate-600'
              }`}
            >
              <SourceTypeIcon sourceType={10} className="w-3.5 h-3.5" />
              {t('format.sourceType.dingtalk')}
            </button>
          )}
        </div>
        <div className={`${mobileFilterOpen ? 'flex' : 'hidden'} flex-col sm:flex sm:flex-row sm:items-end gap-4`}>
          {/* 搜索框 */}
          <div className="flex-1 min-w-0 sm:max-w-xs">
            <label className="block text-xs font-medium text-gray-500 dark:text-slate-400 mb-1.5">{t('conversations.list.search')}</label>
            <div className="relative">
              <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                <SearchIcon className="w-4 h-4 text-gray-400 dark:text-slate-500" />
              </div>
              <input
                type="text"
                value={searchInput}
                onChange={(e) => handleSearchInput(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') triggerSearchNow();
                }}
                placeholder={t('conversations.list.searchPlaceholder')}
                className="w-full pl-9 pr-3 py-2 bg-gray-50 dark:bg-slate-900/50 border border-gray-200 dark:border-slate-600 rounded-xl text-sm placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-primary-500/40 focus:border-primary-500 transition-all duration-200"
              />
              {searchInput && (
                <button
                  onClick={clearSearch}
                  className="absolute inset-y-0 right-0 pr-3 flex items-center text-gray-400 hover:text-gray-600 dark:text-slate-500 dark:hover:text-slate-300"
                  aria-label={t('conversations.list.clearSearch')}
                >
                  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              )}
            </div>
          </div>
          {/* 邮箱筛选 */}
          <div className="min-w-0 sm:w-44">
            <label className="block text-xs font-medium text-gray-500 dark:text-slate-400 mb-1.5">{t('conversations.list.mailbox')}</label>
            <select
              value={filterMailbox}
              onChange={(e) => setFilterMailbox(e.target.value === '' ? '' : Number(e.target.value))}
              className="w-full px-3 py-2 bg-gray-50 dark:bg-slate-900/50 border border-gray-200 dark:border-slate-600 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-primary-500/40 focus:border-primary-500 transition-all duration-200 appearance-none cursor-pointer"
            >
              <option value="">{t('conversations.list.allMailboxes')}</option>
              {mailboxes.map((m) => (
                <option key={m.id} value={m.id}>{m.name}</option>
              ))}
            </select>
          </div>
          {/* 状态筛选 */}
          <div className="min-w-0 sm:w-40">
            <label className="block text-xs font-medium text-gray-500 dark:text-slate-400 mb-1.5">{t('conversations.list.status')}</label>
            <select
              value={filterStatus}
              onChange={(e) => setFilterStatus(e.target.value === '' ? '' : Number(e.target.value))}
              className="w-full px-3 py-2 bg-gray-50 dark:bg-slate-900/50 border border-gray-200 dark:border-slate-600 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-primary-500/40 focus:border-primary-500 transition-all duration-200 appearance-none cursor-pointer"
            >
              <option value="">{t('conversations.list.allStatuses')}</option>
              {STATUS_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>{t(o.labelKey)}</option>
              ))}
            </select>
          </div>
          {/* 优先级筛选 */}
          <div className="min-w-0 sm:w-40">
            <label className="block text-xs font-medium text-gray-500 dark:text-slate-400 mb-1.5">{t('conversations.list.priority')}</label>
            <select
              value={filterPriority}
              onChange={(e) => setFilterPriority(e.target.value === '' ? '' : Number(e.target.value))}
              className="w-full px-3 py-2 bg-gray-50 dark:bg-slate-900/50 border border-gray-200 dark:border-slate-600 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-primary-500/40 focus:border-primary-500 transition-all duration-200 appearance-none cursor-pointer"
            >
              <option value="">{t('conversations.list.allPriorities')}</option>
              {[...PRIORITY_OPTIONS].reverse().map((o) => (
                <option key={o.value} value={o.value}>{t(o.labelKey)}</option>
              ))}
            </select>
          </div>
          {/* 来源筛选 */}
          <div className="min-w-0 sm:w-40">
            <label className="block text-xs font-medium text-gray-500 dark:text-slate-400 mb-1.5">{t('conversations.list.source')}</label>
            <select
              value={filterSourceType}
              onChange={(e) => setFilterSourceType(e.target.value === '' ? '' : Number(e.target.value))}
              className={`w-full px-3 py-2 bg-gray-50 dark:bg-slate-900/50 border rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-primary-500/40 focus:border-primary-500 transition-all duration-200 appearance-none cursor-pointer ${
                filterSourceType !== ''
                  ? 'border-green-300 dark:border-green-700 text-green-700 dark:text-green-400 font-medium'
                  : 'border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-200'
              }`}
            >
              <option value="">{t('conversations.list.allSources')}</option>
              {SOURCE_TYPE_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>{t(o.labelKey)}</option>
              ))}
            </select>
          </div>
          {/* 关注筛选 */}
          <div className="min-w-0 sm:w-40">
            <label className="block text-xs font-medium text-gray-500 dark:text-slate-400 mb-1.5">
              {t('conversations.list.follow')}
              {starredIds.size > 0 && (
                <span className="ml-1 text-[10px] text-amber-600 dark:text-amber-400">({starredIds.size})</span>
              )}
            </label>
            <select
              value={filterFollowed}
              onChange={(e) => setFilterFollowed(e.target.value === '' ? '' : Number(e.target.value))}
              className={`w-full px-3 py-2 bg-gray-50 dark:bg-slate-900/50 border rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-primary-500/40 focus:border-primary-500 transition-all duration-200 appearance-none cursor-pointer ${
                filterFollowed !== ''
                  ? 'border-amber-300 dark:border-amber-700 text-amber-700 dark:text-amber-400 font-medium'
                  : 'border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-200'
              }`}
            >
              <option value="">{t('conversations.list.allConversations')}</option>
              <option value="1">{t('conversations.list.onlyFollowed')}</option>
            </select>
          </div>
        </div>
        {/* Mobile filter toggle button */}
        <button
          onClick={() => setMobileFilterOpen((v) => !v)}
          className="md:hidden mt-3 w-full inline-flex items-center justify-between px-3 py-2 rounded-xl bg-gray-50 dark:bg-slate-900/50 border border-gray-200 dark:border-slate-600 text-sm text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700/60 transition-colors"
          aria-expanded={mobileFilterOpen}
        >
          <span className="inline-flex items-center gap-1.5 font-medium">
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M3 4a1 1 0 011-1h16a1 1 0 011 1v2.586a1 1 0 01-.293.707l-6.414 6.414a1 1 0 00-.293.707V17l-4 4v-3.586a1 1 0 00-.293-.707L3.293 7.293A1 1 0 013 6.586V4z" />
            </svg>
            {t('conversations.list.filters')}
            {activeFilterCount > 0 && (
              <span className="inline-flex items-center justify-center min-w-[18px] h-[18px] px-1 text-[10px] font-bold rounded-full bg-primary-600 text-white">
                {activeFilterCount}
              </span>
            )}
          </span>
          <svg
            className={`w-4 h-4 transition-transform ${mobileFilterOpen ? 'rotate-180' : ''}`}
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            strokeWidth={2}
          >
            <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
          </svg>
        </button>

        {/* 搜索结果统计 */}
        {searchKeyword.trim() && (
          <div className="mt-3 pt-3 border-t border-gray-100 dark:border-slate-700/50">
            <span className="text-xs text-gray-500 dark:text-slate-400">
              {t('conversations.list.resultCountPrefix')}
              <span className="font-semibold text-gray-700 dark:text-slate-200">{totalCount}</span>
              {t('conversations.list.resultCountSuffix', { count: totalCount })}
            </span>
          </div>
        )}
      </div>

      {error && (
        <div className="bg-red-50 dark:bg-red-900/30 border border-red-200 dark:border-red-700 text-red-700 dark:text-red-300 px-4 py-3 rounded-xl text-sm flex items-center gap-2">
          <svg className="w-5 h-5 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L4.082 16.5c-.77.833.192 2.5 1.732 2.5z" />
          </svg>
          {t('conversations.list.loadFailed', { message: error })}
        </div>
      )}

      {/* 批量操作工具栏 */}
      {selectedIds.size > 0 && (
        <div className="bg-primary-50 dark:bg-primary-900/20 border border-primary-200 dark:border-primary-700/50 rounded-2xl px-3 py-2 sm:px-5 sm:py-3 flex flex-wrap items-center gap-2 sm:gap-3 shadow-sm">
          <div className="flex items-center gap-2 text-sm">
            <span className="inline-flex items-center justify-center w-6 h-6 bg-primary-600 text-white text-xs font-semibold rounded-full">
              {selectedIds.size}
            </span>
            <span className="text-primary-800 dark:text-primary-300 font-medium">{t('conversations.list.selectedCount', { count: selectedIds.size })}</span>
          </div>
          <div className="flex flex-wrap items-center gap-1.5 sm:gap-2 ml-auto">
            {canAssignConversations && (
              <button
                onClick={openAssignModal}
                title={t('conversations.list.assignAssignee')}
                className="inline-flex items-center gap-1.5 px-2 py-1 sm:px-3 sm:py-1.5 text-sm bg-white dark:bg-slate-800 border border-primary-300 dark:border-primary-600 text-primary-700 dark:text-primary-300 rounded-lg hover:bg-primary-100 dark:hover:bg-primary-900/30 transition-colors"
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
                </svg>
                <span className="hidden sm:inline">{t('conversations.list.assignAssignee')}</span>
              </button>
            )}
            <button
              onClick={() => {
                setBatchStatus(1);
                setBatchError('');
                setStatusModalOpen(true);
              }}
              title={t('conversations.list.updateStatus')}
              className="inline-flex items-center gap-1.5 px-2 py-1 sm:px-3 sm:py-1.5 text-sm bg-white dark:bg-slate-800 border border-primary-300 dark:border-primary-600 text-primary-700 dark:text-primary-300 rounded-lg hover:bg-primary-100 dark:hover:bg-primary-900/30 transition-colors"
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              <span className="hidden sm:inline">{t('conversations.list.updateStatus')}</span>
            </button>
            <button
              onClick={() => {
                setBatchPriority(1);
                setBatchError('');
                setPriorityModalOpen(true);
              }}
              title={t('conversations.list.setPriority')}
              className="inline-flex items-center gap-1.5 px-2 py-1 sm:px-3 sm:py-1.5 text-sm bg-white dark:bg-slate-800 border border-primary-300 dark:border-primary-600 text-primary-700 dark:text-primary-300 rounded-lg hover:bg-primary-100 dark:hover:bg-primary-900/30 transition-colors"
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M3 6l6 6m0 0l6-6m-6 6v8" />
              </svg>
              <span className="hidden sm:inline">{t('conversations.list.setPriority')}</span>
            </button>
            <button
              onClick={openTagModal}
              title={t('conversations.list.addTags')}
              className="inline-flex items-center gap-1.5 px-2 py-1 sm:px-3 sm:py-1.5 text-sm bg-white dark:bg-slate-800 border border-primary-300 dark:border-primary-600 text-primary-700 dark:text-primary-300 rounded-lg hover:bg-primary-100 dark:hover:bg-primary-900/30 transition-colors"
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M7 7h7l3 3v5H4V10l3-3zM9 15l-3-3h10l-3 3z" />
              </svg>
              <span className="hidden sm:inline">{t('conversations.list.addTags')}</span>
            </button>
            <button
              onClick={() => setDeleteOpen(true)}
              title={t('common.delete')}
              className="inline-flex items-center gap-1.5 px-2 py-1 sm:px-3 sm:py-1.5 text-sm bg-white dark:bg-slate-800 border border-red-300 dark:border-red-700 text-red-700 dark:text-red-400 rounded-lg hover:bg-red-50 dark:hover:bg-red-900/20 transition-colors"
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
              </svg>
              <span className="hidden sm:inline">{t('common.delete')}</span>
            </button>
            <button
              onClick={clearSelection}
              title={t('conversations.list.clearSelection')}
              className="px-2 py-1 sm:px-3 sm:py-1.5 text-sm text-gray-600 dark:text-slate-400 hover:text-gray-900 dark:hover:text-slate-200 transition-colors"
            >
              <span className="hidden sm:inline">{t('conversations.list.clearSelection')}</span>
              <svg className="w-4 h-4 sm:hidden" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>
        </div>
      )}

      {/* 表格 */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 overflow-hidden shadow-sm">
        {loading ? (
          <div
            className="p-16 text-center"
            role="status"
            aria-live="polite"
            aria-busy="true"
          >
            <div className="inline-flex items-center gap-3 text-gray-400 dark:text-slate-500">
              <svg className="w-5 h-5 animate-spin" fill="none" viewBox="0 0 24 24" aria-hidden="true">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              <span className="text-sm font-medium">{t('conversations.list.loading')}</span>
            </div>
          </div>
        ) : pagedList.length === 0 ? (
          /* 友好的空状态 */
          <div className="p-16 text-center">
            {filterSourceType !== '' && [7, 8, 9, 10].includes(filterSourceType) ? (
              (() => {
                const sourceConfig: Record<number, { bg: string; text: string }> = {
                  7: { bg: 'bg-green-100 dark:bg-green-900/30', text: 'text-green-500 dark:text-green-400' },
                  8: { bg: 'bg-emerald-100 dark:bg-emerald-900/30', text: 'text-emerald-500 dark:text-emerald-400' },
                  9: { bg: 'bg-blue-100 dark:bg-blue-900/30', text: 'text-blue-500 dark:text-blue-400' },
                  10: { bg: 'bg-indigo-100 dark:bg-indigo-900/30', text: 'text-indigo-500 dark:text-indigo-400' },
                };
                const cfg = sourceConfig[filterSourceType] ?? { bg: 'bg-gray-100 dark:bg-slate-700', text: 'text-gray-500 dark:text-slate-400' };
                const sourceLabel = getSourceTypeLabel(filterSourceType);
                return (
                  <>
                    <div className={`w-16 h-16 mx-auto mb-4 rounded-2xl ${cfg.bg} flex items-center justify-center`}>
                      <SourceTypeIcon sourceType={filterSourceType} className={`w-8 h-8 ${cfg.text}`} />
                    </div>
                    <h3 className="text-lg font-semibold text-gray-400 dark:text-slate-500 mb-1">
                      {t('conversations.list.emptySourceTitle', { source: sourceLabel })}
                    </h3>
                    <p className="text-sm text-gray-300 dark:text-slate-600">
                      {t('conversations.list.emptySourceHint', { source: sourceLabel })}
                    </p>
                  </>
                );
              })()
            ) : (
              <>
                <EmptyInboxIcon className="w-24 h-24 mx-auto mb-4" />
                <h3 className="text-lg font-semibold text-gray-400 dark:text-slate-500 mb-1">
                  {searchKeyword.trim() ? t('conversations.list.emptySearch') : t('conversations.list.empty')}
                </h3>
                <p className="text-sm text-gray-300 dark:text-slate-600">
                  {searchKeyword.trim() ? t('conversations.list.emptySearchHint') : t('conversations.list.emptyHint')}
                </p>
              </>
            )}
          </div>
        ) : (
          <>
          {/* Mobile: card-based list */}
          <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
            {pagedList.map((c, idx) => {
              const status = statusMap[c.status] ?? fallbackStatus;
              const convType = typeConfig[c.type] ?? fallbackConvType;
              const isSelected = selectedIds.has(c.id);
              const customerName = c.customer?.fullName || c.customerEmail || '';
              const customerInitial = getInitial(customerName);
              const avatarColor = getAvatarColor(customerName);
              const IconComponent = convType.icon;
              return (
                <div
                  key={c.id}
                  className={`p-3.5 border-l-2 ${
                    c.sourceType === 7 ? 'border-l-green-500' : 'border-l-transparent'
                  } ${
                    isSelected
                      ? 'bg-primary-50/70 dark:bg-primary-900/20'
                      : idx % 2 === 0
                        ? 'bg-white dark:bg-slate-800'
                        : 'bg-gray-50/50 dark:bg-slate-800/50'
                  }`}
                >
                  <div className="flex items-start gap-2.5">
                    <input
                      type="checkbox"
                      checked={isSelected}
                      onChange={() => toggleSelect(c.id)}
                      className="mt-1 w-4 h-4 rounded border-gray-300 dark:border-slate-600 text-primary-600 focus:ring-primary-500 cursor-pointer shrink-0"
                    />
                    <div className="flex-1 min-w-0">
                      {/* Row 1: subject + status */}
                      <div className="flex items-start justify-between gap-2">
                        <Link
                          to={`/conversations/${c.id}`}
                          className={`no-underline hover:text-primary-600 dark:hover:text-primary-400 transition-colors duration-150 line-clamp-2 flex-1 min-w-0 ${
                            c.hasUnreadMessages ? 'font-bold text-gray-900 dark:text-white' : 'font-medium text-gray-700 dark:text-slate-200'
                          }`}
                        >
                          {c.subject || <span className="text-gray-400 dark:text-slate-500 italic">{t('conversations.shared.noSubject')}</span>}
                          {c.hasUnreadMessages && (
                            <span className="inline-flex items-center gap-1 px-1.5 py-0.5 ml-1 text-[10px] font-bold rounded-full bg-red-500 text-white shrink-0">
                              <span className="w-1.5 h-1.5 rounded-full bg-white" />
                              {t('conversations.list.new')}
                            </span>
                          )}
                        </Link>
                        <span className={`inline-flex px-2 py-0.5 text-[11px] font-semibold rounded-md border shrink-0 ${status.color} ${status.darkColor}`}>
                          {t(status.labelKey)}
                        </span>
                      </div>
                      {/* Row 2: preview */}
                      {c.preview && (
                        <p className={`text-xs mt-1 line-clamp-1 ${c.hasUnreadMessages ? 'text-gray-600 dark:text-slate-300 font-medium' : 'text-gray-400 dark:text-slate-500'}`}>
                          {c.preview}
                        </p>
                      )}
                      {/* Row 3: customer + source + priority */}
                      <div className="flex items-center gap-2 mt-2 flex-wrap">
                        <div className="flex items-center gap-1.5 min-w-0">
                          {c.customer?.avatar ? (
                            <img
                              src={safeUrl(c.customer.avatar)}
                              alt={customerName}
                              loading="lazy"
                              className="w-5 h-5 rounded-full object-cover shrink-0 border border-gray-200 dark:border-slate-600"
                            />
                          ) : (
                            <div className={`w-5 h-5 rounded-full flex items-center justify-center text-white text-[10px] font-semibold shrink-0 ${
                              c.sourceType === 7 ? 'bg-gradient-to-br from-green-500 to-emerald-600' : avatarColor
                            }`}>
                              {customerInitial}
                            </div>
                          )}
                          <span className="text-xs text-gray-600 dark:text-slate-300 truncate max-w-[120px]">
                            {c.customer?.fullName || c.customerEmail || '-'}
                          </span>
                        </div>
                        <span className={`inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded-full text-[10px] font-medium ${getSourceTypeColor(c.sourceType)}`}>
                          {c.sourceType !== 1 && <SourceTypeIcon sourceType={c.sourceType} className="w-2.5 h-2.5" />}
                          {getSourceTypeLabel(c.sourceType)}
                        </span>
                        <span className={`inline-flex items-center gap-0.5 px-1.5 py-0.5 text-[10px] font-semibold rounded-md border ${getPriorityColor(c.priority)}`}>
                          <PriorityIcon priority={c.priority} className="w-2.5 h-2.5" />
                          {getPriorityLabel(c.priority)}
                        </span>
                      </div>
                      {/* Row 4: assignee + last reply + tags */}
                      <div className="flex items-center gap-2 mt-1.5 text-[11px] text-gray-400 dark:text-slate-500">
                        <IconComponent className={`w-3 h-3 ${convType.color} shrink-0`} />
                        <span className="truncate">{c.user?.fullName || t('format.unassigned')}</span>
                        <span>·</span>
                        <span className="whitespace-nowrap">{formatDateTime(c.lastReplyAt)}</span>
                        {c.hasAttachments && <PaperclipIcon className="w-3 h-3 text-primary-500 dark:text-primary-400 shrink-0" />}
                      </div>
                      {/* Tags */}
                      {c.tags && c.tags.length > 0 && (
                        <div className="flex flex-wrap gap-1 mt-1.5">
                          {c.tags.slice(0, 3).map((tag) => (
                            <span
                              key={tag.id}
                              className="inline-flex px-1.5 py-0.5 text-[10px] font-medium rounded-md"
                              style={{
                                backgroundColor: tag.color + '18',
                                color: tag.color,
                                border: `1px solid ${tag.color}30`
                              }}
                            >
                              {tag.name}
                            </span>
                          ))}
                          {c.tags.length > 3 && (
                            <span className="text-[10px] text-gray-400 dark:text-slate-500">+{c.tags.length - 3}</span>
                          )}
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>

          {/* Desktop: table view */}
          <div className="hidden md:block overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-100 dark:divide-slate-700/50">
              <thead>
                <tr className="bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-800 dark:to-slate-900">
                  <th className="px-4 py-3.5 w-10">
                    <input
                      type="checkbox"
                      checked={isAllSelected}
                      ref={(el) => {
                        if (el) el.indeterminate = isPartialSelected;
                      }}
                      onChange={toggleSelectAll}
                      className="w-4 h-4 rounded border-slate-300 dark:border-slate-500 text-primary-500 focus:ring-primary-400 cursor-pointer bg-white dark:bg-slate-700"
                    />
                  </th>
                  <th className="px-4 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">ID</th>
                  <th className="px-4 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('conversations.list.columnType')}</th>
                  <th className="px-4 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('conversations.list.source')}</th>
                  <th className="px-4 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('conversations.list.columnSubject')}</th>
                  <th className="px-4 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('conversations.list.columnCustomer')}</th>
                  <th className="px-4 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('conversations.list.columnAssignee')}</th>
                  <th className="px-4 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('conversations.list.status')}</th>
                  <th className="px-4 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('conversations.list.priority')}</th>
                  <th className="px-4 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('conversations.list.columnAttachments')}</th>
                  <th className="px-4 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('conversations.list.columnLastReply')}</th>
                  <th className="px-4 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('conversations.list.columnCreatedAt')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-50 dark:divide-slate-700/40 virtual-list">
                {pagedList.map((c, idx) => {
                  const status = statusMap[c.status] ?? fallbackStatus;
                  const convType = typeConfig[c.type] ?? fallbackConvType;
                  const isSelected = selectedIds.has(c.id);
                  const customerName = c.customer?.fullName || c.customerEmail || '';
                  const customerInitial = getInitial(customerName);
                  const avatarColor = getAvatarColor(customerName);
                  const IconComponent = convType.icon;
                  return (
                    <tr
                      key={c.id}
                      className={`transition-colors duration-150 virtual-list-item border-l-2 ${
                        c.sourceType === 7
                          ? 'border-l-green-500'
                          : 'border-l-transparent'
                      } ${
                        isSelected
                          ? 'bg-primary-50/70 dark:bg-primary-900/20'
                          : idx % 2 === 0
                            ? 'bg-white dark:bg-slate-800'
                            : 'bg-gray-50/50 dark:bg-slate-800/50'
                      } hover:bg-primary-50/50 dark:hover:bg-slate-700/50`}
                    >
                      <td className="px-4 py-3">
                        <input
                          type="checkbox"
                          checked={isSelected}
                          onChange={() => toggleSelect(c.id)}
                          onClick={(e) => e.stopPropagation()}
                          className="w-4 h-4 rounded border-gray-300 dark:border-slate-600 text-primary-600 focus:ring-primary-500 cursor-pointer"
                        />
                      </td>
                      <td className="px-4 py-3 text-sm font-mono text-gray-400 dark:text-slate-500">#{c.id}</td>
                      <td className="px-4 py-3 text-sm" title={t(convType.labelKey)}>
                        <IconComponent className={`w-5 h-5 ${convType.color}`} />
                      </td>
                      <td className="px-4 py-3 text-sm whitespace-nowrap">
                        <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${getSourceTypeColor(c.sourceType)}`}>
                          {c.sourceType !== 1 && <SourceTypeIcon sourceType={c.sourceType} className="w-3 h-3" />}
                          {getSourceTypeLabel(c.sourceType)}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-sm max-w-xs">
                        <div className="flex items-center gap-2">
                          <Link
                            to={`/conversations/${c.id}`}
                            className={`no-underline hover:text-primary-600 dark:hover:text-primary-400 hover:underline transition-colors duration-150 truncate ${c.hasUnreadMessages ? 'font-bold text-gray-900 dark:text-white' : 'font-medium text-gray-700 dark:text-slate-200'}`}
                          >
                            {c.subject || <span className="text-gray-400 dark:text-slate-500 italic">{t('conversations.shared.noSubject')}</span>}
                          </Link>
                          {c.hasUnreadMessages && (
                            <span
                              className="inline-flex items-center gap-1 px-1.5 py-0.5 text-[10px] font-bold rounded-full bg-red-500 text-white shadow-sm shrink-0 animate-pulse"
                              title={t('conversations.list.hasNewMessage')}
                            >
                              <span className="w-1.5 h-1.5 rounded-full bg-white" />
                              {t('conversations.list.newMessage')}
                            </span>
                          )}
                        </div>
                        {c.preview && (
                          <div className={`text-xs mt-0.5 truncate ${c.hasUnreadMessages ? 'text-gray-700 dark:text-slate-300 font-medium' : 'text-gray-500 dark:text-slate-400'}`} title={c.preview}>
                            {c.preview}
                          </div>
                        )}
                        {c.tags && c.tags.length > 0 && (
                          <div className="flex flex-wrap gap-1 mt-1">
                            {c.tags.slice(0, 3).map((tag) => (
                              <span
                                key={tag.id}
                                className="inline-flex px-1.5 py-0.5 text-[10px] font-medium rounded-md"
                                style={{
                                  backgroundColor: tag.color + '18',
                                  color: tag.color,
                                  border: `1px solid ${tag.color}30`
                                }}
                              >
                                {tag.name}
                              </span>
                            ))}
                            {c.tags.length > 3 && (
                              <span className="text-[10px] text-gray-400 dark:text-slate-500">+{c.tags.length - 3}</span>
                            )}
                          </div>
                        )}
                      </td>
                      <td className="px-4 py-3 text-sm">
                        <div className="flex items-center gap-2">
                          {c.customer?.avatar ? (
                            <img
                              src={safeUrl(c.customer.avatar)}
                              alt={customerName}
                              loading="lazy"
                              className="w-7 h-7 rounded-full object-cover shrink-0 border border-gray-200 dark:border-slate-600"
                            />
                          ) : (
                            <div className={`w-7 h-7 rounded-full flex items-center justify-center text-white text-xs font-semibold shrink-0 ${
                              c.sourceType === 7
                                ? 'bg-gradient-to-br from-green-500 to-emerald-600 shadow-sm'
                                : avatarColor
                            }`}>
                              {customerInitial}
                            </div>
                          )}
                          <div className="min-w-0">
                            <div className="text-gray-700 dark:text-slate-300 truncate flex items-center gap-1">
                              {c.sourceType === 7 && (
                                <SourceTypeIcon sourceType={c.sourceType} className="w-3 h-3 text-green-500 dark:text-green-400 flex-shrink-0" title={t('conversations.shared.wechatCustomer')} />
                              )}
                              <span className="truncate">{c.customer?.fullName || '-'}</span>
                            </div>
                            {c.sourceType === 7 ? (
                              <div className="text-xs text-green-600 dark:text-green-400/80 truncate">{t('conversations.shared.wechatCustomer')}</div>
                            ) : c.customerEmail ? (
                              <div className="text-xs text-gray-400 dark:text-slate-500 truncate">{c.customerEmail}</div>
                            ) : null}
                          </div>
                        </div>
                      </td>
                      <td className="px-4 py-3 text-sm text-gray-700 dark:text-slate-300">
                        {c.user?.fullName || <span className="text-gray-400 dark:text-slate-500 italic text-xs">{t('format.unassigned')}</span>}
                      </td>
                      <td className="px-4 py-3">
                        <span className={`inline-flex px-2.5 py-1 text-xs font-semibold rounded-lg border ${status.color} ${status.darkColor}`}>
                          {t(status.labelKey)}
                        </span>
                      </td>
                      <td className="px-4 py-3 whitespace-nowrap">
                        <span className={`inline-flex items-center gap-1 px-2.5 py-1 text-xs font-semibold rounded-lg border ${getPriorityColor(c.priority)}`}>
                          <PriorityIcon priority={c.priority} className="w-3 h-3" />
                          {getPriorityLabel(c.priority)}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-sm">
                        {c.hasAttachments ? (
                          <PaperclipIcon className="w-4 h-4 text-primary-500 dark:text-primary-400" />
                        ) : (
                          <span className="text-gray-300 dark:text-slate-600">-</span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-sm text-gray-500 dark:text-slate-400 whitespace-nowrap">{formatDateTime(c.lastReplyAt)}</td>
                      <td className="px-4 py-3 text-sm text-gray-500 dark:text-slate-400 whitespace-nowrap">{formatDateTime(c.createdAt)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          </>
        )}

        {/* 分页控件 */}
        {!loading && totalCount > PAGE_SIZE && (
          <div className="px-3 py-3 sm:px-5 sm:py-4 border-t border-gray-100 dark:border-slate-700/50 flex flex-col sm:flex-row items-center justify-between gap-2 sm:gap-3">
            <div className="text-sm text-gray-500 dark:text-slate-400">
              {/* 与改造前一致：「共 N 条，」在 hidden sm:inline 里，页码在它外面（移动端本来可见） */}
              <span className="hidden sm:inline">
                {t('conversations.list.pageSummaryPrefix')}
                <span className="font-medium text-gray-700 dark:text-slate-200">{totalCount}</span>
                {t('conversations.list.pageSummaryAfterTotal', { count: totalCount })}
              </span>
              {t('conversations.list.pageSummaryBeforePage')}
              <span className="font-medium text-gray-700 dark:text-slate-200">{currentPage}</span>
              {t('conversations.list.pageSummaryBetween')}
              <span className="font-medium text-gray-700 dark:text-slate-200">{totalPages}</span>
              {t('conversations.list.pageSummarySuffix', { count: totalPages })}
            </div>
            <div className="flex items-center gap-1">
              {/* 上一页 */}
              <button
                onClick={() => setCurrentPage((p) => Math.max(1, p - 1))}
                disabled={currentPage === 1}
                className="inline-flex items-center justify-center w-9 h-9 text-sm rounded-lg border border-gray-200 dark:border-slate-600 text-gray-600 dark:text-slate-300 bg-white dark:bg-slate-800 hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M15 19l-7-7 7-7" />
                </svg>
              </button>
              {/* 页码 - 桌面端显示全部页码 */}
              <div className="hidden sm:flex items-center gap-1">
                {getPageNumbers().map((page, idx) =>
                  page === 'ellipsis' ? (
                    <span key={`ellipsis-${idx}`} className="w-9 h-9 flex items-center justify-center text-sm text-gray-400 dark:text-slate-500">...</span>
                  ) : (
                    <button
                      key={page}
                      onClick={() => setCurrentPage(page)}
                      className={`inline-flex items-center justify-center w-9 h-9 text-sm rounded-lg border transition-colors ${
                        currentPage === page
                          ? 'bg-primary-600 border-primary-600 text-white font-semibold shadow-sm'
                          : 'border-gray-200 dark:border-slate-600 text-gray-600 dark:text-slate-300 bg-white dark:bg-slate-800 hover:bg-gray-50 dark:hover:bg-slate-700'
                      }`}
                    >
                      {page}
                    </button>
                  )
                )}
              </div>
              {/* 移动端：当前页/总页数 简洁指示 */}
              <span className="sm:hidden inline-flex items-center justify-center min-w-[60px] h-9 px-2 text-sm text-gray-500 dark:text-slate-400">
                {currentPage} / {totalPages}
              </span>
              {/* 下一页 */}
              <button
                onClick={() => setCurrentPage((p) => Math.min(totalPages, p + 1))}
                disabled={currentPage === totalPages}
                className="inline-flex items-center justify-center w-9 h-9 text-sm rounded-lg border border-gray-200 dark:border-slate-600 text-gray-600 dark:text-slate-300 bg-white dark:bg-slate-800 hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
                </svg>
              </button>
            </div>
          </div>
        )}
      </div>

      {/* 批量分配 Modal */}
      <Modal
        open={assignModalOpen}
        title={t('conversations.list.assignModalTitle', { count: selectedIds.size })}
        onClose={() => setAssignModalOpen(false)}
        size="sm"
        footer={
          <>
            <button
              onClick={() => setAssignModalOpen(false)}
              disabled={batchSubmitting}
              className="px-4 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700"
            >
              {t('common.cancel')}
            </button>
            <button
              onClick={handleBatchAssign}
              disabled={batchSubmitting}
              className="px-4 py-2 text-sm bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white rounded-lg"
            >
              {batchSubmitting ? t('conversations.list.assigning') : t('conversations.list.confirmAssign')}
            </button>
          </>
        }
      >
        <div className="space-y-3">
          {batchError && (
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 px-3 py-2 rounded-lg text-sm">
              {batchError}
            </div>
          )}
          <p className="text-sm text-gray-600 dark:text-slate-300">
            {t('conversations.list.assignModalHintPrefix')}
            <span className="font-semibold text-gray-900 dark:text-slate-100">{selectedIds.size}</span>
            {t('conversations.list.assignModalHintSuffix', { count: selectedIds.size })}
          </p>
          <select
            value={assignUserId}
            onChange={(e) => setAssignUserId(e.target.value === '' ? '' : Number(e.target.value))}
            className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
          >
            <option value="">{t('conversations.list.selectUser')}</option>
            {users.map((u) => (
              <option key={u.id} value={u.id}>{u.fullName} ({u.email})</option>
            ))}
          </select>
        </div>
      </Modal>

      {/* 批量更新状态 Modal */}
      <Modal
        open={statusModalOpen}
        title={t('conversations.list.statusModalTitle', { count: selectedIds.size })}
        onClose={() => setStatusModalOpen(false)}
        size="sm"
        footer={
          <>
            <button
              onClick={() => setStatusModalOpen(false)}
              disabled={batchSubmitting}
              className="px-4 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700"
            >
              {t('common.cancel')}
            </button>
            <button
              onClick={handleBatchStatus}
              disabled={batchSubmitting}
              className="px-4 py-2 text-sm bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white rounded-lg"
            >
              {batchSubmitting ? t('conversations.list.updating') : t('conversations.list.confirmUpdate')}
            </button>
          </>
        }
      >
        <div className="space-y-3">
          {batchError && (
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 px-3 py-2 rounded-lg text-sm">
              {batchError}
            </div>
          )}
          <p className="text-sm text-gray-600 dark:text-slate-300">
            {t('conversations.list.statusModalHintPrefix')}
            <span className="font-semibold text-gray-900 dark:text-slate-100">{selectedIds.size}</span>
            {t('conversations.list.statusModalHintSuffix', { count: selectedIds.size })}
          </p>
          <div className="space-y-2">
            {STATUS_OPTIONS.map((s) => (
              <label
                key={s.value}
                className={`flex items-center gap-2 p-2.5 border rounded-lg cursor-pointer transition ${
                  batchStatus === s.value ? 'border-primary-500 bg-primary-50 dark:bg-primary-900/20' : 'border-gray-200 dark:border-slate-700 hover:bg-gray-50 dark:hover:bg-slate-700'
                }`}
              >
                <input
                  type="radio"
                  name="batch-status"
                  checked={batchStatus === s.value}
                  onChange={() => setBatchStatus(s.value)}
                  className="w-4 h-4 text-primary-600 focus:ring-primary-500"
                />
                <span className={`inline-flex px-2 py-0.5 text-xs font-medium rounded-full ${s.color}`}>
                  {t(s.labelKey)}
                </span>
              </label>
            ))}
          </div>
        </div>
      </Modal>

      {/* 批量设置优先级 Modal */}
      <Modal
        open={priorityModalOpen}
        title={t('conversations.list.priorityModalTitle', { count: selectedIds.size })}
        onClose={() => setPriorityModalOpen(false)}
        size="sm"
        footer={
          <>
            <button
              onClick={() => setPriorityModalOpen(false)}
              disabled={batchSubmitting}
              className="px-4 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700"
            >
              {t('common.cancel')}
            </button>
            <button
              onClick={handleBatchPriority}
              disabled={batchSubmitting}
              className="px-4 py-2 text-sm bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white rounded-lg"
            >
              {batchSubmitting ? t('conversations.list.updating') : t('conversations.list.confirmUpdate')}
            </button>
          </>
        }
      >
        <div className="space-y-3">
          {batchError && (
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 px-3 py-2 rounded-lg text-sm">
              {batchError}
            </div>
          )}
          <p className="text-sm text-gray-600 dark:text-slate-300">
            {t('conversations.list.priorityModalHintPrefix')}
            <span className="font-semibold text-gray-900 dark:text-slate-100">{selectedIds.size}</span>
            {t('conversations.list.priorityModalHintSuffix', { count: selectedIds.size })}
          </p>
          <div className="space-y-2">
            {PRIORITY_OPTIONS.map((p) => (
              <label
                key={p.value}
                className={`flex items-center gap-2 p-2.5 border rounded-lg cursor-pointer transition ${
                  batchPriority === p.value ? 'border-primary-500 bg-primary-50 dark:bg-primary-900/20' : 'border-gray-200 dark:border-slate-700 hover:bg-gray-50 dark:hover:bg-slate-700'
                }`}
              >
                <input
                  type="radio"
                  name="batch-priority"
                  checked={batchPriority === p.value}
                  onChange={() => setBatchPriority(p.value)}
                  className="w-4 h-4 text-primary-600 focus:ring-primary-500"
                />
                <span className={`inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-full border ${p.color}`}>
                  <PriorityIcon priority={p.value} className="w-3 h-3" />
                  {t(p.labelKey)}
                </span>
              </label>
            ))}
          </div>
        </div>
      </Modal>

      {/* 批量添加标签 Modal */}
      <Modal
        open={tagModalOpen}
        title={selectedIds.size === 1
          ? t('conversations.list.tagModalTitleSingle')
          : t('conversations.list.tagModalTitle', { count: selectedIds.size })}
        onClose={() => setTagModalOpen(false)}
        size="sm"
        footer={
          <>
            <button
              onClick={() => setTagModalOpen(false)}
              disabled={batchSubmitting}
              className="px-4 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700"
            >
              {t('common.cancel')}
            </button>
            <button
              onClick={handleBatchAddTags}
              disabled={batchSubmitting}
              className="px-4 py-2 text-sm bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white rounded-lg"
            >
              {batchSubmitting ? t('conversations.list.saving') : selectedIds.size === 1 ? t('common.save') : t('conversations.list.confirmAddTags')}
            </button>
          </>
        }
      >
        <div className="space-y-3">
          {batchError && (
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 px-3 py-2 rounded-lg text-sm">
              {batchError}
            </div>
          )}
          <p className="text-sm text-gray-600 dark:text-slate-300">
            {selectedIds.size === 1
              ? t('conversations.list.tagModalHintSingle')
              : (
                <>
                  {t('conversations.list.tagModalHintPrefix')}
                  <span className="font-semibold text-gray-900 dark:text-slate-100">{selectedIds.size}</span>
                  {t('conversations.list.tagModalHintSuffix', { count: selectedIds.size })}
                </>
              )
            }
          </p>
          {tags.length === 0 ? (
            <p className="text-sm text-gray-500 dark:text-slate-400 text-center py-4">{t('conversations.list.noTags')}</p>
          ) : (
            <div className="flex flex-wrap gap-2">
              {tags.map((tag) => {
                const isSelected = selectedTagIds.has(tag.id);
                return (
                  <button
                    key={tag.id}
                    onClick={() => toggleTagSelect(tag.id)}
                    className={`inline-flex items-center gap-1 px-3 py-1.5 text-sm rounded-full border transition ${
                      isSelected
                        ? 'border-primary-500 bg-primary-100 dark:bg-primary-900/30 text-primary-700 dark:text-primary-300'
                        : 'border-gray-200 dark:border-slate-600 bg-white dark:bg-slate-800 text-gray-700 dark:text-slate-300 hover:border-gray-300 dark:hover:border-slate-500'
                    }`}
                    style={{ borderColor: isSelected ? tag.color : undefined }}
                  >
                    <span
                      className="w-2 h-2 rounded-full"
                      style={{ backgroundColor: tag.color }}
                    />
                    {tag.name}
                    {isSelected && (
                      <svg className="w-3 h-3" fill="currentColor" viewBox="0 0 20 20">
                        <path
                          fillRule="evenodd"
                          d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z"
                          clipRule="evenodd"
                        />
                      </svg>
                    )}
                  </button>
                );
              })}
            </div>
          )}
        </div>
      </Modal>

      {/* 批量删除确认 */}
      <ConfirmDialog
        open={deleteOpen}
        title={t('conversations.list.bulkDeleteTitle')}
        message={t('conversations.list.bulkDeleteMessage', { count: selectedIds.size })}
        confirmText={t('conversations.list.confirmDelete')}
        variant="danger"
        loading={deleting}
        onConfirm={handleBatchDelete}
        onCancel={() => setDeleteOpen(false)}
      />

      {/* 垃圾箱 Modal */}
      <Modal
        open={trashOpen}
        title={t('conversations.list.trashTitle', {
          mailbox:
            filterMailbox === ''
              ? t('conversations.list.allMailboxes')
              : mailboxes.find((m) => m.id === filterMailbox)?.name ?? '',
        })}
        onClose={() => setTrashOpen(false)}
        size="lg"
        footer={
          <>
            <button
              onClick={() => setClearTrashOpen(true)}
              disabled={trashList.length === 0 || clearingTrash}
              className="px-4 py-2 text-sm bg-red-600 hover:bg-red-700 disabled:bg-red-300 text-white rounded-lg"
            >
              {t('conversations.list.clearTrash')}
            </button>
            <button
              onClick={() => setTrashOpen(false)}
              className="px-4 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700"
            >
              {t('common.close')}
            </button>
          </>
        }
      >
        <div className="space-y-3">
          {trashList.length === 0 ? (
            <div className="py-8 text-center text-sm text-gray-400 dark:text-slate-500">{t('conversations.list.trashEmpty')}</div>
          ) : (
            <div className="max-h-[60vh] overflow-y-auto divide-y divide-gray-100 dark:divide-slate-700/40">
              {trashList.map((c) => (
                <div key={c.id} className="py-3 flex items-center gap-3">
                  <div className="flex-1 min-w-0">
                    <div className="text-sm font-medium text-gray-800 dark:text-slate-200 truncate">
                      #{c.id} {c.subject || <span className="text-gray-400 italic">{t('conversations.shared.noSubject')}</span>}
                    </div>
                    <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 truncate">
                      {t('conversations.list.trashItemMeta', {
                        customer: c.customer?.fullName || c.customerEmail || '-',
                        assignee: c.user?.fullName || t('format.unassigned'),
                      })}
                    </div>
                  </div>
                  <button
                    onClick={() => handleRestoreFromTrash(c.id)}
                    disabled={restoreMutation.isPending}
                    className="px-3 py-1.5 text-xs bg-white dark:bg-slate-800 border border-primary-300 dark:border-primary-600 text-primary-700 dark:text-primary-300 rounded-lg hover:bg-primary-50 dark:hover:bg-primary-900/30 disabled:opacity-50 transition-colors"
                  >
                    {t('conversations.list.restore')}
                  </button>
                  <button
                    onClick={() => handleHardDeleteFromTrash(c.id)}
                    disabled={hardDeleteMutation.isPending || hardDeleting}
                    className="px-3 py-1.5 text-xs bg-white dark:bg-slate-800 border border-red-300 dark:border-red-700 text-red-700 dark:text-red-400 rounded-lg hover:bg-red-50 dark:hover:bg-red-900/20 disabled:opacity-50 transition-colors"
                  >
                    {t('conversations.list.hardDelete')}
                  </button>
                </div>
              ))}
            </div>
          )}
          <p className="text-xs text-gray-500 dark:text-slate-400 pt-2 border-t border-gray-100 dark:border-slate-700/50">
            {t('conversations.list.trashHint')}
          </p>
        </div>
      </Modal>

      {/* 清空垃圾箱确认 */}
      <ConfirmDialog
        open={clearTrashOpen}
        title={t('conversations.list.clearTrash')}
        message={t('conversations.list.clearTrashMessage', { count: trashList.length })}
        confirmText={t('conversations.list.confirmClearTrash')}
        variant="danger"
        loading={clearingTrash}
        onConfirm={handleClearTrash}
        onCancel={() => setClearTrashOpen(false)}
      />

      {/* 单条硬删除确认（替代 window.confirm） */}
      <ConfirmDialog
        open={hardDeleteOpen}
        title={t('conversations.list.hardDeleteTitle')}
        message={hardDeleteId === null ? '' : t('conversations.list.hardDeleteMessage', { id: hardDeleteId })}
        confirmText={t('conversations.list.confirmHardDelete')}
        variant="danger"
        loading={hardDeleting}
        onConfirm={confirmHardDelete}
        onCancel={() => {
          if (!hardDeleting) {
            setHardDeleteOpen(false);
            setHardDeleteId(null);
          }
        }}
      />
    </div>
  );
}
