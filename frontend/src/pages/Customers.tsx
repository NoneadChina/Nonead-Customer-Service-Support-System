import { useEffect, useState, useCallback, useRef } from 'react';
import { customerApi, conversationApi } from '../api';
import type {
  CustomerResponse,
  CreateCustomerRequest,
  ConversationResponse,
} from '../api/types';
import { formatDateTime, formatDate, safeUrl } from '../utils/format';
import { getApiErrorMessage } from '../utils/error';
import { genId } from '../utils/id';
import { useAuth } from '../context/AuthContext';
import { useI18n } from '../context/I18nContext';
import { tBound } from '../i18n/locale';
import CustomFieldsPanel from '../components/CustomFieldsPanel';
import { isAdmin } from '../utils/permissions';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';

const initialForm: CreateCustomerRequest = {
  firstName: '',
  lastName: '',
  email: '',
  address: '',
  city: '',
  state: '',
  zip: '',
  country: '',
  company: '',
  jobTitle: '',
  notes: '',
  phones: [],
  socialProfiles: [],
  websites: [],
  avatar: null,
};

/** 模块级函数（非组件）→ 用 `tBound` 取当前绑定语言（由 I18nProvider 在渲染体内绑定） */
const uploadAvatar = async (file: File): Promise<string> => {
  if (!file.type.startsWith('image/')) {
    throw new Error(tBound('customers.customers.uploadNotImage'));
  }
  if (file.size > 2 * 1024 * 1024) {
    throw new Error(tBound('customers.customers.uploadTooLarge'));
  }
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const dataUrl = reader.result as string;
      // 通过 canvas 压缩到 256x256
      const img = new Image();
      img.onload = () => {
        const canvas = document.createElement('canvas');
        canvas.width = 256;
        canvas.height = 256;
        const ctx = canvas.getContext('2d');
        if (!ctx) {
          reject(new Error(tBound('customers.customers.imageUnprocessable')));
          return;
        }
        // 居中裁剪
        const size = Math.min(img.width, img.height);
        const sx = (img.width - size) / 2;
        const sy = (img.height - size) / 2;
        ctx.drawImage(img, sx, sy, size, size, 0, 0, 256, 256);
        const compressed = canvas.toDataURL('image/jpeg', 0.85);
        resolve(compressed);
      };
      img.onerror = () => reject(new Error(tBound('customers.customers.imageLoadFailed')));
      img.src = dataUrl;
    };
    reader.onerror = () => reject(new Error(tBound('customers.customers.fileReadFailed')));
    reader.readAsDataURL(file);
  });
};

const avatarColors = [
  'bg-primary-100 text-primary-700 dark:bg-primary-900/30 dark:text-primary-300',
  'bg-indigo-100 text-indigo-700 dark:bg-indigo-900/30 dark:text-indigo-300',
  'bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-300',
  'bg-pink-100 text-pink-700 dark:bg-pink-900/30 dark:text-pink-300',
  'bg-rose-100 text-rose-700 dark:bg-rose-900/30 dark:text-rose-300',
  'bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-300',
  'bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300',
  'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-300',
  'bg-teal-100 text-teal-700 dark:bg-teal-900/30 dark:text-teal-300',
  'bg-cyan-100 text-cyan-700 dark:bg-cyan-900/30 dark:text-cyan-300',
];

const getAvatarColor = (name: string) => {
  let hash = 0;
  for (let i = 0; i < name.length; i++) {
    hash = name.charCodeAt(i) + ((hash << 5) - hash);
  }
  return avatarColors[Math.abs(hash) % avatarColors.length];
};

export default function Customers() {
  const { t } = useI18n();
  const { user } = useAuth();
  // 后端 DeleteCustomer/GetTrash/RestoreCustomer/HardDeleteCustomer/ClearTrash/BulkDelete/Export
  // 均标注 [RequireSuperAdmin]，非超管调用会返回 403。前端据此隐藏对应 UI，避免无谓的 403 请求。
  const isSuperUser = user ? isAdmin(user.role) : false;

  const [list, setList] = useState<CustomerResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const [searchInput, setSearchInput] = useState('');
  const [searchKeyword, setSearchKeyword] = useState('');
  const [page, setPage] = useState(1);
  // 每页显示条数：超过 10 条即自动分页
  const [pageSize] = useState(10);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(0);

  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<CreateCustomerRequest>(initialForm);
  const [phoneKeys, setPhoneKeys] = useState<string[]>([]);
  const [websiteKeys, setWebsiteKeys] = useState<string[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState('');

  const [deleteTarget, setDeleteTarget] = useState<CustomerResponse | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState('');

  const [detailTarget, setDetailTarget] = useState<CustomerResponse | null>(null);
  const [detailConversations, setDetailConversations] = useState<ConversationResponse[]>([]);
  const [detailLoading, setDetailLoading] = useState(false);

  const [jumpPageInput, setJumpPageInput] = useState('');

  const [trashOpen, setTrashOpen] = useState(false);
  const [trashList, setTrashList] = useState<CustomerResponse[]>([]);
  const [trashLoading, setTrashLoading] = useState(false);
  const [trashError, setTrashError] = useState('');
  const [trashSearch, setTrashSearch] = useState('');
  const [trashPage, setTrashPage] = useState(1);
  const [trashPageSize] = useState(10);
  const [trashTotalCount, setTrashTotalCount] = useState(0);
  const [trashTotalPages, setTrashTotalPages] = useState(0);
  const [trashCount, setTrashCount] = useState(0);

  const [restoreTarget, setRestoreTarget] = useState<CustomerResponse | null>(null);
  const [restoring, setRestoring] = useState(false);

  const [hardDeleteTarget, setHardDeleteTarget] = useState<CustomerResponse | null>(null);
  const [hardDeleting, setHardDeleting] = useState(false);
  const [hardDeleteStatus, setHardDeleteStatus] = useState<{ activeCount: number; trashCount: number } | null>(null);
  const [hardDeleteStatusLoading, setHardDeleteStatusLoading] = useState(false);

  const [clearTrashOpen, setClearTrashOpen] = useState(false);
  const [clearingTrash, setClearingTrash] = useState(false);
  const [clearTrashStatus, setClearTrashStatus] = useState<{ customerCount: number; totalActiveCount: number; totalTrashCount: number } | null>(null);
  const [clearTrashStatusLoading, setClearTrashStatusLoading] = useState(false);

  // 批量操作
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());
  const [bulkDeleteOpen, setBulkDeleteOpen] = useState(false);
  const [bulkDeleting, setBulkDeleting] = useState(false);
  const [bulkError, setBulkError] = useState('');
  const [exporting, setExporting] = useState(false);

  const fetchCancelled = useRef(false);
  useEffect(() => {
    return () => { fetchCancelled.current = true; };
  }, []);

  const fetch = useCallback(async () => {
    fetchCancelled.current = false;
    setLoading(true);
    setError('');
    try {
      // 使用 Promise.allSettled 而非 Promise.all，避免垃圾箱接口 403
      // （非超管调用 GetTrash 会返回 403）阻断主列表加载
      const [listResult, trashResult] = await Promise.allSettled([
        customerApi.list({
          search: searchKeyword || undefined,
          page,
          pageSize: pageSize,
        }),
        // 非超管不调用 trash 接口（后端 [RequireSuperAdmin] 会返回 403）
        isSuperUser ? customerApi.getTrash(1, 1, '') : Promise.resolve(null),
      ]);

      if (fetchCancelled.current) return;

      if (listResult.status === 'fulfilled') {
        const resp = listResult.value;
        // customerApi.list 返回 PagedResponse<CustomerResponse>，resp.data 已是 CustomerResponse[]
        const data: CustomerResponse[] = resp?.data ?? [];
        setList(Array.isArray(data) ? data : []);
        setTotalCount(resp?.totalItems ?? data.length);
        setTotalPages(resp?.totalPages ?? Math.max(1, Math.ceil(data.length / pageSize)));
      } else {
        // 主列表失败时才报错给用户
        const err = listResult.reason;
        setError(err instanceof Error ? err.message : t('customers.customers.loadFailed'));
        setList([]);
      }

      if (trashResult.status === 'fulfilled' && trashResult.value) {
        setTrashCount(trashResult.value?.totalItems ?? 0);
      } else {
        // 非超管或 trash 接口失败时，垃圾箱计数为 0，不阻塞主列表
        setTrashCount(0);
      }
    } finally {
      if (!fetchCancelled.current) setLoading(false);
    }
  }, [searchKeyword, page, pageSize, isSuperUser, t]);

  useEffect(() => {
    fetch();
  }, [fetch]);

  const handleSearch = () => {
    setPage(1);
    setSearchKeyword(searchInput.trim());
    setSelectedIds(new Set());
  };

  const handleResetSearch = () => {
    setSearchInput('');
    setSearchKeyword('');
    setPage(1);
    setSelectedIds(new Set());
  };

  const handleJumpPage = () => {
    const num = parseInt(jumpPageInput, 10);
    if (!isNaN(num) && num >= 1 && num <= totalPages) {
      setPage(num);
      setJumpPageInput('');
    }
  };

  const openCreate = () => {
    setEditingId(null);
    setForm(initialForm);
    setPhoneKeys([]);
    setWebsiteKeys([]);
    setFormError('');
    setModalOpen(true);
  };

  const openEdit = (c: CustomerResponse | null) => {
    if (!c) {
      setFormError(t('customers.customers.customerNotLoaded'));
      return;
    }
    setEditingId(c.id);
    setForm({
      firstName: c.firstName ?? '',
      lastName: c.lastName ?? '',
      email: c.mainEmail ?? '',
      address: c.address ?? '',
      city: c.city ?? '',
      state: c.state ?? '',
      zip: c.zip ?? '',
      country: c.country ?? '',
      company: c.company ?? '',
      jobTitle: c.jobTitle ?? '',
      notes: c.notes ?? '',
      phones: c.phones ?? [],
      socialProfiles: c.socialProfiles ?? [],
      websites: c.websites ?? [],
      avatar: c.avatar ?? null,
    });
    setPhoneKeys((c.phones ?? []).map(() => genId()));
    setWebsiteKeys((c.websites ?? []).map(() => genId()));
    setFormError('');
    setModalOpen(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!form.firstName.trim() || !form.lastName.trim()) {
      setFormError(t('customers.customers.nameRequired'));
      return;
    }
    // 邮箱改为可选（各渠道新建的客户可能没有邮箱，如微信/钉钉/WhatsApp 客户）
    // 有邮箱时才校验格式；无邮箱时允许保存（编辑时可能后续通过其他渠道补全）
    if (form.email.trim()) {
      if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email.trim())) {
        setFormError(t('customers.customers.emailInvalid'));
        return;
      }
    }

    const payload: CreateCustomerRequest = {
      firstName: form.firstName.trim(),
      lastName: form.lastName.trim(),
      email: form.email.trim(),
      address: form.address?.trim() || null,
      city: form.city?.trim() || null,
      state: form.state?.trim() || null,
      zip: form.zip?.trim() || null,
      country: form.country?.trim() || null,
      company: form.company?.trim() || null,
      jobTitle: form.jobTitle?.trim() || null,
      notes: form.notes?.trim() || null,
      phones: form.phones?.filter(p => p.value.trim()) || [],
      socialProfiles: form.socialProfiles?.filter(sp => sp.value.trim()) || [],
      websites: form.websites?.filter(w => w.trim()) || [],
      // avatar: null 表示未提供（保留原值），'' 表示显式清除，非空字符串表示新头像 URL
      avatar: form.avatar,
    };

    setSubmitting(true);
    setFormError('');
    try {
      if (editingId) {
        await customerApi.update(editingId, payload);
      } else {
        await customerApi.create(payload);
      }
      setModalOpen(false);
      await fetch();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : t('customers.common.operationFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    setDeleteError('');
    try {
      await customerApi.remove(deleteTarget.id);
      setDeleteTarget(null);
      setTrashCount(c => c + 1);
      await fetch();
    } catch (err: unknown) {
      setDeleteError(err instanceof Error ? err.message : t('customers.common.deleteFailed'));
    } finally {
      setDeleting(false);
    }
  };

  // ====== 批量操作 ======
  const toggleSelect = (id: number) => {
    setSelectedIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const toggleSelectAll = () => {
    if (selectedIds.size === list.length && list.length > 0) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(list.map(c => c.id)));
    }
  };

  const isAllSelected = list.length > 0 && selectedIds.size === list.length;
  const isPartialSelected = selectedIds.size > 0 && selectedIds.size < list.length;

  const handleBulkDelete = async () => {
    setBulkDeleting(true);
    setBulkError('');
    try {
      const ids = Array.from(selectedIds);
      const result = await customerApi.bulkRemove(ids);
      // 部分失败时展示失败详情，但仍刷新列表
      if (result.failedCount > 0) {
        const failedDetails = result.errors?.slice(0, 5).join(t('customers.customers.errorSeparator'));
        const moreHint = result.errors.length > 5 ? t('customers.customers.moreErrors', { count: result.errors.length }) : '';
        setBulkError(t('customers.customers.bulkResult', {
          success: result.successCount,
          failed: result.failedCount,
          details: failedDetails ?? '',
          more: moreHint,
        }));
      } else {
        setBulkDeleteOpen(false);
      }
      setTrashCount(c => c + result.successCount);
      setSelectedIds(new Set());
      await fetch();
    } catch (err: unknown) {
      setBulkError(err instanceof Error ? err.message : t('customers.customers.bulkDeleteFailed'));
    } finally {
      setBulkDeleting(false);
    }
  };

  const handleExport = async () => {
    setExporting(true);
    try {
      const ids = selectedIds.size > 0 ? Array.from(selectedIds) : undefined;
      const blob = await customerApi.exportCsv(ids);
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `customers_export_${new Date().toISOString().slice(0, 10)}.csv`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('customers.customers.exportFailed'));
    } finally {
      setExporting(false);
    }
  };

  const fetchTrash = async () => {
    setTrashLoading(true);
    setTrashError('');
    try {
      const resp = await customerApi.getTrash(trashPage, trashPageSize, trashSearch);
      // customerApi.getTrash 返回 PagedResponse<CustomerResponse>，resp.data 已是 CustomerResponse[]
      const data: CustomerResponse[] = resp?.data ?? [];
      setTrashList(Array.isArray(data) ? data : []);
      // resp 已是 PagedResponse<CustomerResponse>，直接使用结构化字段
      const total = resp?.totalItems ?? data.length;
      setTrashTotalCount(total);
      setTrashTotalPages(resp?.totalPages ?? Math.max(1, Math.ceil(total / trashPageSize)));
      setTrashCount(total);
    } catch (err: unknown) {
      setTrashError(err instanceof Error ? err.message : t('customers.customers.loadTrashFailed'));
    } finally {
      setTrashLoading(false);
    }
  };

  const openTrashModal = () => {
    setTrashPage(1);
    setTrashSearch('');
    setTrashOpen(true);
    fetchTrash();
  };

  const handleRestore = async () => {
    if (!restoreTarget) return;
    setRestoring(true);
    try {
      await customerApi.restore(restoreTarget.id);
      setRestoreTarget(null);
      setTrashCount(c => Math.max(0, c - 1));
      await fetchTrash();
      await fetch();
    } catch (err: unknown) {
      setTrashError(err instanceof Error ? err.message : t('customers.common.restoreFailed'));
    } finally {
      setRestoring(false);
    }
  };

  const openHardDelete = async (c: CustomerResponse) => {
    setHardDeleteTarget(c);
    setHardDeleteStatus(null);
    setHardDeleteStatusLoading(true);
    try {
      const status = await customerApi.getHardDeleteStatus(c.id);
      setHardDeleteStatus(status);
    } catch {
      setHardDeleteStatus(null);
    } finally {
      setHardDeleteStatusLoading(false);
    }
  };

  const handleHardDelete = async () => {
    if (!hardDeleteTarget) return;
    // 前端预校验：有关联会话时直接提示，避免无意义的请求
    if (hardDeleteStatus && (hardDeleteStatus.activeCount > 0 || hardDeleteStatus.trashCount > 0)) {
      setTrashError(t('customers.customers.hardDeleteBlockedHint'));
      setHardDeleteTarget(null);
      return;
    }
    setHardDeleting(true);
    try {
      await customerApi.hardDelete(hardDeleteTarget.id);
      setHardDeleteTarget(null);
      setHardDeleteStatus(null);
      setTrashCount(c => Math.max(0, c - 1));
      await fetchTrash();
    } catch (err: unknown) {
      setTrashError(getApiErrorMessage(err, t('customers.customers.hardDeleteFailedHint')));
    } finally {
      setHardDeleting(false);
    }
  };

  const openClearTrash = async () => {
    setClearTrashOpen(true);
    setClearTrashStatus(null);
    setClearTrashStatusLoading(true);
    try {
      const status = await customerApi.getClearTrashStatus();
      setClearTrashStatus(status);
    } catch {
      setClearTrashStatus(null);
    } finally {
      setClearTrashStatusLoading(false);
    }
  };

  const handleClearTrash = async () => {
    // 前端预校验：有关联会话时直接提示，避免无意义的请求
    if (clearTrashStatus && (clearTrashStatus.totalActiveCount > 0 || clearTrashStatus.totalTrashCount > 0)) {
      setTrashError(t('customers.customers.clearTrashBlockedHint'));
      setClearTrashOpen(false);
      return;
    }
    setClearingTrash(true);
    try {
      await customerApi.clearTrash();
      setTrashCount(0);
      setTrashList([]);
      setClearTrashOpen(false);
      setClearTrashStatus(null);
    } catch (err: unknown) {
      setTrashError(getApiErrorMessage(err, t('customers.customers.clearTrashFailedHint')));
    } finally {
      setClearingTrash(false);
    }
  };

  const openDetail = async (c: CustomerResponse) => {
    setDetailTarget(c);
    setDetailConversations([]);
    setDetailLoading(true);
    try {
      const resp = await conversationApi.list({ customerId: c.id, pageSize: 50 });
      // conversationApi.list 返回 PagedResponse<ConversationResponse>，resp.data 已是 ConversationResponse[]
      const data: ConversationResponse[] = resp?.data ?? [];
      setDetailConversations(Array.isArray(data) ? data : []);
    } catch {
      setDetailConversations([]);
    } finally {
      setDetailLoading(false);
    }
  };

  // 是否有关联会话阻塞硬删除/清空垃圾箱
  const hardDeleteBlocked = !!hardDeleteStatus && (hardDeleteStatus.activeCount > 0 || hardDeleteStatus.trashCount > 0);
  const clearTrashBlocked = !!clearTrashStatus && (clearTrashStatus.totalActiveCount > 0 || clearTrashStatus.totalTrashCount > 0);

  const renderPageNumbers = () => {
    const pages = [];
    const maxVisible = 5;
    let start = Math.max(1, page - Math.floor(maxVisible / 2));
    const end = Math.min(totalPages, start + maxVisible - 1);

    if (end - start + 1 < maxVisible) {
      start = Math.max(1, end - maxVisible + 1);
    }

    if (start > 1) {
      pages.push(
        <button
          key="first"
          onClick={() => setPage(1)}
          className="px-2.5 py-1.5 text-sm border rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition"
        >
          1
        </button>
      );
      if (start > 2) {
        pages.push(<span key="ellipsis1" className="px-2 text-gray-400 dark:text-slate-500">...</span>);
      }
    }

    for (let i = start; i <= end; i++) {
      pages.push(
        <button
          key={i}
          onClick={() => setPage(i)}
          className={`px-3 py-1.5 text-sm rounded-lg font-medium transition ${
            i === page
              ? 'bg-primary-600 text-white shadow-sm shadow-primary-200 dark:shadow-primary-900/50'
              : 'border hover:bg-gray-50 dark:hover:bg-slate-700'
          }`}
        >
          {i}
        </button>
      );
    }

    if (end < totalPages) {
      if (end < totalPages - 1) {
        pages.push(<span key="ellipsis2" className="px-2 text-gray-400 dark:text-slate-500">...</span>);
      }
      pages.push(
        <button
          key="last"
          onClick={() => setPage(totalPages)}
          className="px-2.5 py-1.5 text-sm border rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition"
        >
          {totalPages}
        </button>
      );
    }

    return pages;
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <div>
          <h1 className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100">{t('customers.customers.title')}</h1>
          <p className="text-sm text-gray-500 dark:text-slate-400 mt-1">
            {t('customers.customers.subtitle')}{totalCount > 0 && <span className="text-gray-400 dark:text-slate-500">{t('customers.customers.subtitleCountPrefix')}{totalCount}{t('customers.customers.subtitleCountSuffix')}</span>}
          </p>
        </div>
        <button
          onClick={openCreate}
          className="inline-flex items-center justify-center gap-1.5 px-3 sm:px-4 py-2 sm:py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 text-white rounded-xl shadow-sm shadow-primary-200/50 dark:shadow-primary-900/30 transition-all hover:-translate-y-0.5"
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m8-8H4" />
          </svg>
          {t('customers.customers.newCustomer')}
        </button>
      </div>

      {/* 搜索栏 */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 p-3 sm:p-4 shadow-sm shadow-gray-100/50 dark:shadow-none">
        <div className="flex flex-col sm:flex-row sm:items-end gap-3">
          <div className="relative flex-1 w-full">
            <label className="hidden sm:block text-xs font-medium text-gray-500 dark:text-slate-400 mb-1.5">{t('customers.customers.searchLabel')}</label>
            <div className="relative group">
              <svg className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400 dark:text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
              </svg>
              <input
                type="text"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
                placeholder={t('customers.customers.searchPlaceholder')}
                className="w-full pl-9 pr-10 py-2 sm:py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 transition-all"
              />
              {searchInput && (
                <button
                  onClick={handleResetSearch}
                  className="absolute right-3 top-1/2 -translate-y-1/2 p-1 text-gray-400 hover:text-gray-600 dark:hover:text-slate-300 transition"
                >
                  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              )}
            </div>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            <button
              onClick={handleSearch}
              className="inline-flex items-center justify-center gap-1.5 px-3 sm:px-4 py-2 sm:py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 text-white rounded-xl shadow-sm transition-all"
              title={t('customers.common.search')}
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
              </svg>
              <span className="sm:inline">{t('customers.common.search')}</span>
            </button>
            <button
              onClick={handleResetSearch}
              className="inline-flex items-center justify-center gap-1.5 px-3 sm:px-4 py-2 sm:py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-600 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
              title={t('customers.common.reset')}
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
              </svg>
              <span className="sm:inline">{t('customers.common.reset')}</span>
            </button>
            {isSuperUser && (
              <button
                onClick={openTrashModal}
                className="inline-flex items-center justify-center gap-1.5 px-3 sm:px-4 py-2 sm:py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-600 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
                title={t('customers.common.trash')}
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                </svg>
                <span className="sm:inline">{t('customers.common.trash')}</span>
                {trashCount > 0 && (
                  <span className="px-1.5 py-0.5 text-xs rounded-full bg-red-100 dark:bg-red-900/30 text-red-600 dark:text-red-400">
                    {trashCount}
                  </span>
                )}
              </button>
            )}
          </div>
        </div>
      </div>

      {error && (
        <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 px-4 py-3 rounded-xl text-sm flex items-center gap-2">
          <svg className="w-4 h-4 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          {t('customers.customers.loadFailedPrefix')}{error}
        </div>
      )}

      {/* 批量操作工具栏 */}
      {selectedIds.size > 0 && (
        <div className="bg-primary-50 dark:bg-primary-900/20 border border-primary-200 dark:border-primary-800 rounded-2xl px-5 py-3 flex items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <span className="inline-flex items-center justify-center w-7 h-7 rounded-full bg-primary-600 text-white text-xs font-bold">
              {selectedIds.size}
            </span>
            <span className="text-primary-800 dark:text-primary-300 font-medium">{t('customers.customers.selectedPrefix')}{selectedIds.size}{t('customers.customers.selectedSuffix')}</span>
          </div>
          <div className="flex items-center gap-2">
            {isSuperUser && (
              <button
                onClick={handleExport}
                disabled={exporting}
                className="inline-flex items-center gap-1.5 px-3 py-2 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-600 dark:text-slate-300 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition-all"
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                </svg>
                {exporting ? t('customers.customers.exporting') : t('customers.customers.exportCsv')}
              </button>
            )}
            {isSuperUser && (
              <button
                onClick={() => { setBulkDeleteOpen(true); setBulkError(''); }}
                className="inline-flex items-center gap-1.5 px-3 py-2 text-sm font-medium bg-red-600 hover:bg-red-700 text-white rounded-lg shadow-sm transition-all"
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                </svg>
                {t('customers.customers.bulkDelete')}
              </button>
            )}
            <button
              onClick={() => setSelectedIds(new Set())}
              className="inline-flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-200 transition-all"
            >
              {t('customers.customers.clearSelection')}
            </button>
          </div>
        </div>
      )}

      {/* 表格 */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 overflow-hidden shadow-sm shadow-gray-100/50 dark:shadow-none">
        {loading ? (
          <div className="p-12 text-center">
            <div className="inline-block animate-spin rounded-full h-8 w-8 border-4 border-primary-200 dark:border-primary-800 border-t-primary-600"></div>
            <p className="mt-3 text-gray-500 dark:text-slate-400">{t('customers.common.loading')}</p>
          </div>
        ) : list.length === 0 ? (
          <div className="p-12 text-center">
            <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-gray-100 dark:bg-slate-700/50 mb-4">
              <svg className="w-8 h-8 text-gray-400 dark:text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z" />
              </svg>
            </div>
            <h3 className="text-lg font-medium text-gray-900 dark:text-slate-100 mb-1">
              {searchKeyword ? t('customers.customers.emptySearchTitle') : t('customers.customers.emptyTitle')}
            </h3>
            <p className="text-sm text-gray-500 dark:text-slate-400">
              {searchKeyword ? t('customers.customers.emptySearchHint', { keyword: searchKeyword }) : t('customers.customers.emptyHint')}
            </p>
          </div>
        ) : (
          <>
          {/* 移动端卡片式列表 */}
          <div className="sm:hidden divide-y divide-gray-100 dark:divide-slate-700/30">
            {list.map((c) => (
              <div key={c.id} className="p-4 hover:bg-primary-50/50 dark:hover:bg-slate-700/50 transition-all">
                <div className="flex items-start gap-3 mb-3">
                  {isSuperUser && (
                    <input
                      type="checkbox"
                      checked={selectedIds.has(c.id)}
                      onChange={() => toggleSelect(c.id)}
                      className="mt-1 w-4 h-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500 cursor-pointer shrink-0"
                    />
                  )}
                  <button onClick={() => openDetail(c)} className="flex items-center gap-3 text-left flex-1 min-w-0">
                    {c.avatar ? (
                      <img
                        src={safeUrl(c.avatar)}
                        alt={c.fullName || t('customers.customers.customerAlt')}
                        loading="lazy"
                        className="w-10 h-10 rounded-full object-cover border border-gray-200 dark:border-slate-600 shrink-0"
                      />
                    ) : (
                      <div className={`w-10 h-10 rounded-full flex items-center justify-center text-sm font-semibold shrink-0 ${getAvatarColor(c.fullName || '?')}`}>
                        {c.fullName?.charAt(0) || '?'}
                      </div>
                    )}
                    <div className="min-w-0 flex-1">
                      <div className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate">
                        {c.fullName || t('customers.customers.unnamed')}
                      </div>
                      <div className="text-xs text-gray-500 dark:text-slate-400 truncate">#{c.id}</div>
                    </div>
                  </button>
                  <span className="inline-flex items-center justify-center px-2 py-0.5 text-xs font-medium rounded-full bg-indigo-50 dark:bg-indigo-900/30 text-indigo-700 dark:text-indigo-300 shrink-0">
                    {c.conversationsCount}{t('customers.customers.conversationsSuffix')}
                  </span>
                </div>
                <div className="grid grid-cols-2 gap-2 text-xs text-gray-600 dark:text-slate-400 mb-3 pl-1">
                  <div className="truncate">
                    <span className="text-gray-400 dark:text-slate-500">{t('customers.customers.labelEmail')}</span>
                    {c.mainEmail || '-'}
                  </div>
                  <div className="truncate">
                    <span className="text-gray-400 dark:text-slate-500">{t('customers.customers.labelCompany')}</span>
                    {c.company || '-'}
                  </div>
                  <div className="truncate">
                    <span className="text-gray-400 dark:text-slate-500">{t('customers.customers.labelPhone')}</span>
                    {c.phones?.[0]?.value || '-'}
                  </div>
                  <div className="truncate">
                    <span className="text-gray-400 dark:text-slate-500">{t('customers.customers.labelCreated')}</span>
                    {formatDate(c.createdAt)}
                  </div>
                </div>
                <div className="flex items-center justify-end gap-1 pl-1">
                  <button
                    onClick={() => openDetail(c)}
                    className="p-2 text-gray-400 hover:text-blue-600 hover:bg-blue-50 dark:hover:bg-blue-900/20 rounded-lg transition-all"
                    title={t('customers.customers.viewDetail')}
                  >
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                      <path strokeLinecap="round" strokeLinejoin="round" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                    </svg>
                  </button>
                  <button
                    onClick={() => openEdit(c)}
                    className="p-2 text-gray-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/20 rounded-lg transition-all"
                    title={t('customers.common.edit')}
                  >
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                    </svg>
                  </button>
                  {isSuperUser && (
                    <button
                      onClick={() => { setDeleteTarget(c); setDeleteError(''); }}
                      className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg transition-all"
                      title={t('customers.common.delete')}
                    >
                      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                      </svg>
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>

          {/* PC 端表格 */}
          <div className="hidden sm:block overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-100 dark:divide-slate-700/50">
              <thead className="bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-900 dark:to-slate-800">
                <tr>
                  {isSuperUser && (
                    <th className="px-3 py-3.5 w-12">
                      <input
                        type="checkbox"
                        checked={isAllSelected}
                        ref={(el) => { if (el) el.indeterminate = isPartialSelected; }}
                        onChange={toggleSelectAll}
                        className="w-4 h-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500 cursor-pointer"
                      />
                    </th>
                  )}
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">ID</th>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('customers.customers.name')}</th>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('customers.customers.email')}</th>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('customers.customers.company')}</th>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('customers.customers.phone')}</th>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('customers.customers.conversationCount')}</th>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('customers.customers.createdAt')}</th>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('customers.common.actions')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 dark:divide-slate-700/30 virtual-list">
                {list.map((c, index) => (
                  <tr key={c.id} className={`hover:bg-primary-50/50 dark:hover:bg-slate-700/50 transition-all duration-150 ${index % 2 === 0 ? 'bg-white dark:bg-slate-800' : 'bg-gray-50/50 dark:bg-slate-800/50'}`}>
                    {isSuperUser && (
                      <td className="px-3 py-3.5">
                        <input
                          type="checkbox"
                          checked={selectedIds.has(c.id)}
                          onChange={() => toggleSelect(c.id)}
                          className="w-4 h-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500 cursor-pointer"
                        />
                      </td>
                    )}
                    <td className="px-5 py-3.5 text-sm text-gray-500 dark:text-slate-400 font-mono">#{c.id}</td>
                    <td className="px-5 py-3.5">
                      <button
                        onClick={() => openDetail(c)}
                        className="flex items-center gap-3 text-left group"
                      >
                        {c.avatar ? (
                          <img
                            src={safeUrl(c.avatar)}
                            alt={c.fullName || t('customers.customers.customerAlt')}
                            loading="lazy"
                            className="w-9 h-9 rounded-full object-cover border border-gray-200 dark:border-slate-600 transition-transform group-hover:scale-105"
                          />
                        ) : (
                          <div className={`w-9 h-9 rounded-full flex items-center justify-center text-xs font-semibold transition-transform group-hover:scale-105 ${getAvatarColor(c.fullName || '?')}`}>
                            {c.fullName?.charAt(0) || '?'}
                          </div>
                        )}
                        <span className="text-sm font-medium text-gray-900 dark:text-slate-100 group-hover:text-primary-700 dark:group-hover:text-primary-300 transition-colors">
                          {c.fullName || t('customers.customers.unnamed')}
                        </span>
                      </button>
                    </td>
                    <td className="px-5 py-3.5 text-sm text-gray-700 dark:text-slate-300 truncate max-w-xs">{c.mainEmail || '-'}</td>
                    <td className="px-5 py-3.5 text-sm text-gray-700 dark:text-slate-300 truncate max-w-xs">{c.company || '-'}</td>
                    <td className="px-5 py-3.5 text-sm text-gray-700 dark:text-slate-300">{c.phones?.[0]?.value || '-'}</td>
                    <td className="px-5 py-3.5 text-sm">
                      <span className="inline-flex items-center justify-center px-2.5 py-1 text-xs font-medium rounded-full bg-indigo-50 dark:bg-indigo-900/30 text-indigo-700 dark:text-indigo-300 ring-1 ring-indigo-100 dark:ring-indigo-800/50">
                        {c.conversationsCount}
                      </span>
                    </td>
                    <td className="px-5 py-3.5 text-sm text-gray-500 dark:text-slate-400 whitespace-nowrap">
                      {formatDate(c.createdAt)}
                    </td>
                    <td className="px-5 py-3.5 text-sm">
                      <div className="flex items-center gap-1">
                        <button
                          onClick={() => openDetail(c)}
                          className="p-2 text-gray-400 hover:text-blue-600 hover:bg-blue-50 dark:hover:bg-blue-900/20 rounded-lg transition-all"
                          title={t('customers.customers.viewDetail')}
                        >
                          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                            <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                            <path strokeLinecap="round" strokeLinejoin="round" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                          </svg>
                        </button>
                        <button
                          onClick={() => openEdit(c)}
                          className="p-2 text-gray-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/20 rounded-lg transition-all"
                          title={t('customers.common.edit')}
                        >
                          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                            <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                          </svg>
                        </button>
                        {isSuperUser && (
                          <button
                            onClick={() => { setDeleteTarget(c); setDeleteError(''); }}
                            className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg transition-all"
                            title={t('customers.common.delete')}
                          >
                            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                              <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                            </svg>
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          </>
        )}

        {/* 分页 */}
        {!loading && list.length > 0 && (
          <div className="px-5 py-4 border-t border-gray-100 dark:border-slate-700/50 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
            <div className="text-sm text-gray-500 dark:text-slate-400">
              {t('customers.customers.totalPrefix')}<span className="font-semibold text-gray-900 dark:text-slate-100">{totalCount}</span>{t('customers.customers.totalMiddle')}<span className="font-semibold text-gray-900 dark:text-slate-100">{(page - 1) * pageSize + 1}</span> - {Math.min(page * pageSize, totalCount)}{t('customers.customers.totalSuffix')}
            </div>
            <div className="flex items-center gap-2">
              <button
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                disabled={page === 1}
                className="inline-flex items-center gap-1 px-3 py-2 text-sm border rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M15 19l-7-7 7-7" />
                </svg>
                {t('customers.common.prevPage')}
              </button>
              <div className="flex items-center gap-1">{renderPageNumbers()}</div>
              <button
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                disabled={page >= totalPages}
                className="inline-flex items-center gap-1 px-3 py-2 text-sm border rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
              >
                {t('customers.common.nextPage')}
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
                </svg>
              </button>
              <div className="hidden sm:flex items-center gap-2 ml-2">
                <span className="text-sm text-gray-500 dark:text-slate-400">{t('customers.customers.jumpTo')}</span>
                <input
                  type="number"
                  min="1"
                  max={totalPages}
                  value={jumpPageInput}
                  onChange={(e) => setJumpPageInput(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleJumpPage()}
                  className="w-16 px-2 py-1.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500"
                  placeholder={t('customers.customers.pagePlaceholder')}
                />
                <button
                  onClick={handleJumpPage}
                  className="px-3 py-1.5 text-sm text-primary-600 hover:text-primary-700 font-medium transition-colors"
                >
                  {t('customers.customers.confirmJump')}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>

      {/* 新建/编辑 Modal */}
      <Modal
        open={modalOpen}
        title={editingId ? t('customers.customers.editCustomer') : t('customers.customers.newCustomer')}
        onClose={() => setModalOpen(false)}
        size="lg"
        footer={
          <>
            <button
              onClick={() => setModalOpen(false)}
              disabled={submitting}
              className="px-4 py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
            >
              {t('customers.common.cancel')}
            </button>
            <button
              type="submit"
              form="customer-form"
              disabled={submitting}
              className="px-4 py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white rounded-xl shadow-sm shadow-primary-200/50 dark:shadow-primary-900/30 transition-all"
            >
              {submitting ? t('customers.common.saving') : t('customers.common.save')}
            </button>
          </>
        }
      >
        <form id="customer-form" onSubmit={handleSubmit} className="space-y-6">
          {formError && (
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 px-4 py-3 rounded-xl text-sm flex items-center gap-2">
              <svg className="w-4 h-4 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              {formError}
            </div>
          )}

          <div className="space-y-4">
            <div className="flex items-center gap-2 pb-2 border-b border-gray-100 dark:border-slate-700/50">
              <svg className="w-4 h-4 text-primary-600 dark:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
              </svg>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('customers.customers.sectionBasic')}</h3>
            </div>
            <div className="flex flex-col sm:flex-row gap-4">
              <div className="flex flex-col items-center">
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('customers.customers.avatar')}</label>
                <div className="relative">
                  <div className={`w-20 h-20 rounded-full overflow-hidden border-2 border-gray-200 dark:border-slate-600 ${form.avatar ? 'bg-white dark:bg-slate-800' : getAvatarColor((form.firstName + form.lastName).trim() || '?')}`}>
                    {form.avatar ? (
                      <img src={safeUrl(form.avatar)} alt={t('customers.customers.avatar')} loading="lazy" className="w-full h-full object-cover" />
                    ) : (
                      <div className="w-full h-full flex items-center justify-center text-2xl font-semibold text-gray-600 dark:text-slate-400">
                        {(form.firstName + form.lastName).trim().charAt(0).toUpperCase() || '?'}
                      </div>
                    )}
                  </div>
                  <label className="absolute -bottom-1 -right-1 w-8 h-8 bg-primary-600 hover:bg-primary-700 text-white rounded-full flex items-center justify-center cursor-pointer transition-colors shadow-lg">
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
                    </svg>
                    <input
                      type="file"
                      accept="image/*"
                      className="hidden"
                      onChange={async (e) => {
                        const file = e.target.files?.[0];
                        if (!file) return;
                        try {
                          const url = await uploadAvatar(file);
                          setForm({ ...form, avatar: url });
                        } catch (err: unknown) {
                          setFormError(err instanceof Error ? err.message : t('customers.customers.avatarUploadFailed'));
                        }
                      }}
                    />
                  </label>
                  {form.avatar && (
                    <button
                      type="button"
                      onClick={() => setForm({ ...form, avatar: '' })}
                      className="absolute -top-1 -right-1 w-6 h-6 bg-red-500 hover:bg-red-600 text-white rounded-full flex items-center justify-center cursor-pointer transition-colors shadow-md"
                    >
                      <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                      </svg>
                    </button>
                  )}
                </div>
                <p className="text-xs text-gray-500 dark:text-slate-400 mt-2">{t('customers.customers.avatarHint')}</p>
              </div>
              <div className="flex-1 grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
                    {t('customers.customers.firstName')} <span className="text-red-500">*</span>
                  </label>
                  <input
                    type="text"
                    required
                    value={form.firstName}
                    onChange={(e) => setForm({ ...form, firstName: e.target.value })}
                    className="w-full px-3.5 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-500 transition-all"
                    placeholder={t('customers.customers.firstNamePlaceholder')}
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
                    {t('customers.customers.lastName')} <span className="text-red-500">*</span>
                  </label>
                  <input
                    type="text"
                    required
                    value={form.lastName}
                    onChange={(e) => setForm({ ...form, lastName: e.target.value })}
                    className="w-full px-3.5 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-500 transition-all"
                    placeholder={t('customers.customers.lastNamePlaceholder')}
                  />
                </div>
                <div className="sm:col-span-2">
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
                    {t('customers.customers.email')}
                  </label>
                  <input
                    type="email"
                    value={form.email}
                    onChange={(e) => setForm({ ...form, email: e.target.value })}
                    className="w-full px-3.5 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-500 transition-all"
                    placeholder={t('customers.customers.emailPlaceholder')}
                  />
                </div>
              </div>
            </div>
          </div>

          <div className="space-y-4">
            <div className="flex items-center gap-2 pb-2 border-b border-gray-100 dark:border-slate-700/50">
              <svg className="w-4 h-4 text-primary-600 dark:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4" />
              </svg>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('customers.customers.sectionWork')}</h3>
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('customers.customers.company')}</label>
                <input
                  type="text"
                  value={form.company ?? ''}
                  onChange={(e) => setForm({ ...form, company: e.target.value })}
                  className="w-full px-3.5 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-500 transition-all"
                  placeholder={t('customers.customers.companyPlaceholder')}
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('customers.customers.jobTitle')}</label>
                <input
                  type="text"
                  value={form.jobTitle ?? ''}
                  onChange={(e) => setForm({ ...form, jobTitle: e.target.value })}
                  className="w-full px-3.5 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-500 transition-all"
                  placeholder={t('customers.customers.jobTitlePlaceholder')}
                />
              </div>
            </div>
          </div>

          <div className="space-y-4">
            <div className="flex items-center gap-2 pb-2 border-b border-gray-100 dark:border-slate-700/50">
              <svg className="w-4 h-4 text-primary-600 dark:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M17.657 16.657L13.414 20.9a1.998 1.998 0 01-2.827 0l-4.244-4.243a8 8 0 1111.314 0z" />
                <path strokeLinecap="round" strokeLinejoin="round" d="M15 11a3 3 0 11-6 0 3 3 0 016 0z" />
              </svg>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('customers.customers.sectionAddress')}</h3>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('customers.customers.address')}</label>
              <input
                type="text"
                value={form.address ?? ''}
                onChange={(e) => setForm({ ...form, address: e.target.value })}
                className="w-full px-3.5 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-500 transition-all"
                placeholder={t('customers.customers.addressPlaceholder')}
              />
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('customers.customers.city')}</label>
                <input
                  type="text"
                  value={form.city ?? ''}
                  onChange={(e) => setForm({ ...form, city: e.target.value })}
                  className="w-full px-3.5 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-500 transition-all"
                  placeholder={t('customers.customers.cityPlaceholder')}
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('customers.customers.state')}</label>
                <input
                  type="text"
                  value={form.state ?? ''}
                  onChange={(e) => setForm({ ...form, state: e.target.value })}
                  className="w-full px-3.5 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-500 transition-all"
                  placeholder={t('customers.customers.statePlaceholder')}
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('customers.customers.zip')}</label>
                <input
                  type="text"
                  value={form.zip ?? ''}
                  onChange={(e) => setForm({ ...form, zip: e.target.value })}
                  className="w-full px-3.5 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-500 transition-all"
                  placeholder={t('customers.customers.zipPlaceholder')}
                />
              </div>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('customers.customers.country')}</label>
              <input
                type="text"
                value={form.country ?? ''}
                onChange={(e) => setForm({ ...form, country: e.target.value.toUpperCase() })}
                className="w-full px-3.5 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-500 transition-all"
                placeholder="CN, US, JP..."
                maxLength={2}
              />
            </div>
          </div>

          <div className="space-y-4">
            <div className="flex items-center gap-2 pb-2 border-b border-gray-100 dark:border-slate-700/50">
              <svg className="w-4 h-4 text-primary-600 dark:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M3 5a2 2 0 012-2h3.28a1 1 0 01.948.684l1.498 4.493a1 1 0 01-.502 1.21l-2.257 1.13a11.042 11.042 0 005.516 5.516l1.13-2.257a1 1 0 011.21-.502l4.493 1.498a1 1 0 01.684.949V19a2 2 0 01-2 2h-1C9.716 21 3 14.284 3 6V5z" />
              </svg>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('customers.customers.sectionContact')}</h3>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('customers.customers.phoneNumber')}</label>
              <div className="space-y-2">
                {form.phones?.map((phone, index) => (
                  <div key={phoneKeys[index] ?? index} className="flex gap-2">
                    <input
                      type="tel"
                      value={phone.value}
                      onChange={(e) => {
                        const newPhones = [...(form.phones || [])];
                        newPhones[index] = { ...newPhones[index], value: e.target.value };
                        setForm({ ...form, phones: newPhones });
                      }}
                      className="flex-1 px-3.5 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-500 transition-all"
                      placeholder={t('customers.customers.phoneNumberPlaceholder')}
                    />
                    <select
                      value={phone.type}
                      onChange={(e) => {
                        const newPhones = [...(form.phones || [])];
                        newPhones[index] = { ...newPhones[index], type: parseInt(e.target.value) };
                        setForm({ ...form, phones: newPhones });
                      }}
                      className="px-3.5 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-500 transition-all"
                    >
                      <option value={1}>{t('customers.customers.phoneTypeWork')}</option>
                      <option value={2}>{t('customers.customers.phoneTypeHome')}</option>
                      <option value={4}>{t('customers.customers.phoneTypeMobile')}</option>
                      <option value={5}>{t('customers.customers.phoneTypeFax')}</option>
                      <option value={6}>{t('customers.customers.phoneTypePager')}</option>
                      <option value={3}>{t('customers.customers.phoneTypeOther')}</option>
                    </select>
                    <button
                      type="button"
                      onClick={() => {
                        const newPhones = form.phones?.filter((_, i) => i !== index);
                        setForm({ ...form, phones: newPhones });
                        setPhoneKeys((prev) => prev.filter((_, i) => i !== index));
                      }}
                      className="p-2.5 text-red-500 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-xl transition-all"
                    >
                      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                      </svg>
                    </button>
                  </div>
                ))}
                <button
                  type="button"
                  onClick={() => {
                    setForm({ ...form, phones: [...(form.phones || []), { value: '', type: 1 }] });
                    setPhoneKeys((prev) => [...prev, genId()]);
                  }}
                  className="flex items-center gap-1.5 text-sm text-primary-600 hover:text-primary-700 font-medium transition-colors"
                >
                  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
                  </svg>
                  {t('customers.customers.addPhone')}
                </button>
              </div>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('customers.customers.website')}</label>
              <div className="space-y-2">
                {form.websites?.map((website, index) => (
                  <div key={websiteKeys[index] ?? index} className="flex gap-2">
                    <input
                      type="url"
                      value={website}
                      onChange={(e) => {
                        const newWebsites = [...(form.websites || [])];
                        newWebsites[index] = e.target.value;
                        setForm({ ...form, websites: newWebsites });
                      }}
                      className="flex-1 px-3.5 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-500 transition-all"
                      placeholder="https://example.com"
                    />
                    <button
                      type="button"
                      onClick={() => {
                        const newWebsites = form.websites?.filter((_, i) => i !== index);
                        setForm({ ...form, websites: newWebsites });
                        setWebsiteKeys((prev) => prev.filter((_, i) => i !== index));
                      }}
                      className="p-2.5 text-red-500 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-xl transition-all"
                    >
                      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                      </svg>
                    </button>
                  </div>
                ))}
                <button
                  type="button"
                  onClick={() => {
                    setForm({ ...form, websites: [...(form.websites || []), ''] });
                    setWebsiteKeys((prev) => [...prev, genId()]);
                  }}
                  className="flex items-center gap-1.5 text-sm text-primary-600 hover:text-primary-700 font-medium transition-colors"
                >
                  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
                  </svg>
                  {t('customers.customers.addWebsite')}
                </button>
              </div>
            </div>
          </div>

          <div className="space-y-4">
            <div className="flex items-center gap-2 pb-2 border-b border-gray-100 dark:border-slate-700/50">
              <svg className="w-4 h-4 text-primary-600 dark:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
              </svg>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('customers.customers.sectionNotes')}</h3>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('customers.customers.notes')}</label>
              <textarea
                value={form.notes ?? ''}
                onChange={(e) => setForm({ ...form, notes: e.target.value })}
                className="w-full px-3.5 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-500 transition-all resize-none"
                rows={3}
                placeholder={t('customers.customers.notesPlaceholder')}
              />
            </div>
          </div>

          {/* 自定义字段 */}
          <CustomFieldsPanel
            entityType="customer"
            entityId={editingId}
            variant="full"
          />
        </form>
      </Modal>

      {/* 删除确认 */}
      <ConfirmDialog
        open={!!deleteTarget}
        title={t('customers.customers.deleteCustomerTitle')}
        message={t('customers.customers.deleteCustomerMessage', { name: deleteTarget?.fullName ?? '' })}
        confirmText={t('customers.common.delete')}
        variant="danger"
        loading={deleting}
        error={deleteError}
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
      />

      {/* 批量删除确认 */}
      <ConfirmDialog
        open={bulkDeleteOpen}
        title={t('customers.customers.bulkDeleteTitle')}
        message={t('customers.customers.bulkDeleteMessage', { count: selectedIds.size })}
        confirmText={t('customers.customers.bulkDelete')}
        variant="danger"
        loading={bulkDeleting}
        error={bulkError}
        onConfirm={handleBulkDelete}
        onCancel={() => setBulkDeleteOpen(false)}
      />

      {/* 垃圾箱弹窗 */}
      <Modal
        open={trashOpen}
        title={t('customers.common.trash')}
        onClose={() => setTrashOpen(false)}
        size="xl"
        footer={
          <>
            <button
              onClick={() => openClearTrash()}
              disabled={trashList.length === 0 || clearingTrash}
              className="px-4 py-2.5 text-sm font-medium bg-red-600 hover:bg-red-700 disabled:bg-red-300 text-white rounded-xl transition-all"
            >
              {clearingTrash ? t('customers.customers.clearingTrash') : t('customers.common.clearTrash')}
            </button>
            <button
              onClick={() => setTrashOpen(false)}
              className="px-4 py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
            >
              {t('customers.common.close')}
            </button>
          </>
        }
      >
        <div className="space-y-4">
          {trashError && (
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 px-4 py-3 rounded-xl text-sm flex items-center gap-2">
              <svg className="w-4 h-4 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              {trashError}
            </div>
          )}

          <div className="flex items-center gap-2">
            <div className="relative flex-1">
              <svg className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
              </svg>
              <input
                type="text"
                value={trashSearch}
                onChange={(e) => setTrashSearch(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && (setTrashPage(1), fetchTrash())}
                placeholder={t('customers.customers.trashSearchPlaceholder')}
                className="w-full pl-9 pr-4 py-2 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 transition-all"
              />
            </div>
            <button
              onClick={() => { setTrashPage(1); fetchTrash(); }}
              className="px-4 py-2 text-sm font-medium bg-primary-600 hover:bg-primary-700 text-white rounded-xl transition-all"
            >
              {t('customers.common.search')}
            </button>
          </div>

          <div className="text-sm text-gray-500 dark:text-slate-400">
            {t('customers.customers.trashTotalPrefix')}<span className="font-semibold text-gray-900 dark:text-slate-100">{trashTotalCount}</span>{t('customers.customers.trashTotalSuffix')}
          </div>

          <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-100 dark:border-slate-700/50 overflow-hidden">
            {trashLoading ? (
              <div className="p-8 text-center">
                <div className="inline-block animate-spin rounded-full h-6 w-6 border-4 border-primary-200 dark:border-primary-800 border-t-primary-600"></div>
                <p className="mt-2 text-sm text-gray-500 dark:text-slate-400">{t('customers.common.loading')}</p>
              </div>
            ) : trashList.length === 0 ? (
              <div className="p-8 text-center">
                <div className="inline-flex items-center justify-center w-12 h-12 rounded-full bg-gray-100 dark:bg-slate-700/50 mb-3">
                  <svg className="w-6 h-6 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                  </svg>
                </div>
                <h3 className="text-sm font-medium text-gray-900 dark:text-slate-100 mb-1">{t('customers.common.trashEmpty')}</h3>
                <p className="text-sm text-gray-500 dark:text-slate-400">
                  {trashSearch ? t('customers.customers.trashEmptySearch') : t('customers.customers.trashEmptyHint')}
                </p>
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-gray-100 dark:divide-slate-700/50">
                  <thead className="bg-gray-50 dark:bg-slate-900/50">
                    <tr>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">ID</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('customers.customers.name')}</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('customers.customers.email')}</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('customers.common.deletedAt')}</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('customers.common.actions')}</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100 dark:divide-slate-700/30 virtual-list">
                    {trashList.map((c) => (
                      <tr key={c.id} className="hover:bg-gray-50 dark:hover:bg-slate-700/30 transition-colors">
                        <td className="px-4 py-3 text-sm text-gray-500 dark:text-slate-400 font-mono">#{c.id}</td>
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-2">
                            {c.avatar ? (
                              <img src={safeUrl(c.avatar)} alt={c.fullName || ''} loading="lazy" className="w-7 h-7 rounded-full object-cover" />
                            ) : (
                              <div className={`w-7 h-7 rounded-full flex items-center justify-center text-xs font-semibold ${getAvatarColor(c.fullName || '?')}`}>
                                {c.fullName?.charAt(0) || '?'}
                              </div>
                            )}
                            <span className="text-sm font-medium text-gray-900 dark:text-slate-100">
                              {c.fullName || t('customers.customers.unnamed')}
                            </span>
                          </div>
                        </td>
                        <td className="px-4 py-3 text-sm text-gray-700 dark:text-slate-300">{c.mainEmail || '-'}</td>
                        <td className="px-4 py-3 text-sm text-gray-500 dark:text-slate-400 whitespace-nowrap">
                          {formatDate(c.deletedAt || c.updatedAt)}
                        </td>
                        <td className="px-4 py-3 text-sm">
                          <div className="flex items-center gap-1">
                            <button
                              onClick={() => setRestoreTarget(c)}
                              className="p-1.5 text-gray-400 hover:text-green-600 hover:bg-green-50 dark:hover:bg-green-900/20 rounded-lg transition-all"
                              title={t('customers.common.restore')}
                            >
                              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                <path strokeLinecap="round" strokeLinejoin="round" d="M3 10h10a8 8 0 018 8v2M3 10l6 6m-6-6l6-6" />
                              </svg>
                            </button>
                            <button
                              onClick={() => openHardDelete(c)}
                              className="p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg transition-all"
                              title={t('customers.common.hardDelete')}
                            >
                              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                              </svg>
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {!trashLoading && trashTotalPages > 1 && (
            <div className="flex items-center justify-center gap-2">
              <button
                onClick={() => { setTrashPage(p => Math.max(1, p - 1)); fetchTrash(); }}
                disabled={trashPage === 1}
                className="px-3 py-1.5 text-sm border rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
              >
                {t('customers.common.prevPage')}
              </button>
              <span className="text-sm text-gray-500 dark:text-slate-400">
                {t('customers.common.pageOf', { page: trashPage, total: trashTotalPages })}
              </span>
              <button
                onClick={() => { setTrashPage(p => Math.min(trashTotalPages, p + 1)); fetchTrash(); }}
                disabled={trashPage >= trashTotalPages}
                className="px-3 py-1.5 text-sm border rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
              >
                {t('customers.common.nextPage')}
              </button>
            </div>
          )}
        </div>
      </Modal>

      {/* 恢复确认 */}
      <ConfirmDialog
        open={!!restoreTarget}
        title={t('customers.customers.restoreCustomerTitle')}
        message={t('customers.customers.restoreCustomerMessage', { name: restoreTarget?.fullName ?? '' })}
        confirmText={t('customers.common.restore')}
        variant="primary"
        loading={restoring}
        onConfirm={handleRestore}
        onCancel={() => setRestoreTarget(null)}
      />

      {/* 彻底删除确认 */}
      <ConfirmDialog
        open={!!hardDeleteTarget}
        title={t('customers.customers.hardDeleteCustomerTitle')}
        message={
          hardDeleteStatusLoading
            ? t('customers.customers.hardDeleteChecking', { name: hardDeleteTarget?.fullName ?? '' })
            : hardDeleteBlocked
            ? t('customers.customers.hardDeleteBlocked', { name: hardDeleteTarget?.fullName ?? '' })
            : t('customers.customers.hardDeleteConfirm', { name: hardDeleteTarget?.fullName ?? '' })
        }
        confirmText={hardDeleteBlocked ? t('customers.customers.gotIt') : t('customers.common.hardDelete')}
        variant={hardDeleteBlocked ? 'warning' : 'danger'}
        loading={hardDeleting || hardDeleteStatusLoading}
        onConfirm={
          hardDeleteBlocked
            ? () => { setHardDeleteTarget(null); setHardDeleteStatus(null); }
            : handleHardDelete
        }
        onCancel={() => { setHardDeleteTarget(null); setHardDeleteStatus(null); }}
      >
        {hardDeleteBlocked && (
          <div className="text-sm text-amber-700 dark:text-amber-300 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 px-3 py-2 rounded-lg space-y-1">
            {hardDeleteStatus!.activeCount > 0 && (
              <p>{t('customers.customers.blockedActivePrefix')}<span className="font-semibold">{hardDeleteStatus!.activeCount}</span>{t('customers.customers.blockedActiveSuffix')}</p>
            )}
            {hardDeleteStatus!.trashCount > 0 && (
              <p>{t('customers.customers.blockedTrashPrefix')}<span className="font-semibold">{hardDeleteStatus!.trashCount}</span>{t('customers.customers.blockedTrashSuffix')}</p>
            )}
            <p className="pt-1 text-gray-600 dark:text-slate-400">{t('customers.customers.hardDeleteBlockedHint')}</p>
          </div>
        )}
      </ConfirmDialog>

      {/* 清空垃圾箱确认 */}
      <ConfirmDialog
        open={clearTrashOpen}
        title={t('customers.common.clearTrash')}
        message={
          clearTrashStatusLoading
            ? t('customers.customers.clearTrashChecking')
            : clearTrashBlocked
            ? t('customers.customers.clearTrashBlocked')
            : `${t('customers.customers.clearTrashConfirmPrefix')}${clearTrashStatus ? t('customers.customers.clearTrashCountNotice', { count: clearTrashStatus.customerCount }) : ''}${t('customers.customers.clearTrashConfirmSuffix')}`
        }
        confirmText={clearTrashBlocked ? t('customers.customers.gotIt') : t('customers.customers.confirmClear')}
        variant={clearTrashBlocked ? 'warning' : 'danger'}
        loading={clearingTrash || clearTrashStatusLoading}
        onConfirm={
          clearTrashBlocked
            ? () => { setClearTrashOpen(false); setClearTrashStatus(null); }
            : handleClearTrash
        }
        onCancel={() => { setClearTrashOpen(false); setClearTrashStatus(null); }}
      >
        {clearTrashBlocked && (
          <div className="text-sm text-amber-700 dark:text-amber-300 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 px-3 py-2 rounded-lg space-y-1">
            {clearTrashStatus!.totalActiveCount > 0 && (
              <p>{t('customers.customers.blockedActivePrefix')}<span className="font-semibold">{clearTrashStatus!.totalActiveCount}</span>{t('customers.customers.blockedActiveSuffix')}</p>
            )}
            {clearTrashStatus!.totalTrashCount > 0 && (
              <p>{t('customers.customers.blockedTrashPrefix')}<span className="font-semibold">{clearTrashStatus!.totalTrashCount}</span>{t('customers.customers.blockedTrashSuffix')}</p>
            )}
            <p className="pt-1 text-gray-600 dark:text-slate-400">{t('customers.customers.clearTrashBlockedHint')}</p>
          </div>
        )}
      </ConfirmDialog>

      {/* 详情抽屉 */}
      <Modal
        open={!!detailTarget}
        title={t('customers.customers.detailTitle')}
        onClose={() => setDetailTarget(null)}
        size="lg"
        footer={
          <button
            onClick={() => setDetailTarget(null)}
            className="px-4 py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
          >
            {t('customers.common.close')}
          </button>
        }
      >
        {detailTarget && (
          <div className="space-y-6">
            {/* 基本信息 */}
            <div className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-primary-50 via-indigo-50 to-purple-50 dark:from-slate-900 dark:via-slate-800 dark:to-slate-900 p-5 shadow-sm">
              <div className="flex items-center gap-4 relative z-10">
                {detailTarget.avatar ? (
                  <img
                    src={safeUrl(detailTarget.avatar)}
                    alt={detailTarget.fullName || t('customers.customers.customerAlt')}
                    loading="lazy"
                    className="w-16 h-16 rounded-full object-cover border-2 border-white dark:border-slate-700 shadow-lg"
                  />
                ) : (
                  <div className={`w-16 h-16 rounded-full flex items-center justify-center text-xl font-semibold shadow-lg ${getAvatarColor(detailTarget.fullName || '?')}`}>
                    {detailTarget.fullName?.charAt(0) || '?'}
                  </div>
                )}
                <div className="flex-1 min-w-0">
                  <div className="text-xl font-bold text-gray-900 dark:text-slate-100">{detailTarget.fullName || t('customers.customers.unnamed')}</div>
                  <div className="text-sm text-gray-500 dark:text-slate-400 mt-0.5 flex items-center gap-2">
                    <span>ID: #{detailTarget.id}</span>
                    {detailTarget.jobTitle && (
                      <span className="inline-flex items-center px-2 py-0.5 text-xs font-medium rounded-full bg-white/70 dark:bg-slate-700/50 text-gray-600 dark:text-slate-300 border border-gray-100 dark:border-slate-600">
                        {detailTarget.jobTitle}
                      </span>
                    )}
                  </div>
                </div>
                {detailTarget.company && (
                  <div className="text-right shrink-0">
                    <div className="text-xs text-gray-500 dark:text-slate-400 mb-0.5">{t('customers.customers.company')}</div>
                    <div className="text-sm font-semibold text-gray-900 dark:text-slate-100">{detailTarget.company}</div>
                  </div>
                )}
              </div>
            </div>

            {/* 联系方式 */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div className="p-4 bg-white dark:bg-slate-800 border border-gray-100 dark:border-slate-700/50 rounded-xl">
                <div className="flex items-center gap-2 text-xs text-gray-500 dark:text-slate-400 mb-1.5">
                  <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
                  </svg>
                  {t('customers.customers.email')}
                </div>
                <div className="text-gray-900 dark:text-slate-100 break-all text-sm font-medium">{detailTarget.mainEmail || '-'}</div>
              </div>
              <div className="p-4 bg-white dark:bg-slate-800 border border-gray-100 dark:border-slate-700/50 rounded-xl">
                <div className="flex items-center gap-2 text-xs text-gray-500 dark:text-slate-400 mb-1.5">
                  <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4" />
                  </svg>
                  {t('customers.customers.company')}
                </div>
                <div className="text-gray-900 dark:text-slate-100 text-sm font-medium">{detailTarget.company || '-'}</div>
              </div>
              <div className="p-4 bg-white dark:bg-slate-800 border border-gray-100 dark:border-slate-700/50 rounded-xl">
                <div className="flex items-center gap-2 text-xs text-gray-500 dark:text-slate-400 mb-1.5">
                  <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M17.657 18.657A8 8 0 016.343 7.343S7 9 9 10c0-2 .5-5 2.986-7C14 5 16.09 5.777 17.656 7.343A7.975 7.975 0 0120 13a7.975 7.975 0 01-2.343 5.657z" />
                  </svg>
                  {t('customers.customers.jobTitle')}
                </div>
                <div className="text-gray-900 dark:text-slate-100 text-sm font-medium">{detailTarget.jobTitle || '-'}</div>
              </div>
              <div className="p-4 bg-white dark:bg-slate-800 border border-gray-100 dark:border-slate-700/50 rounded-xl">
                <div className="flex items-center gap-2 text-xs text-gray-500 dark:text-slate-400 mb-1.5">
                  <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M3 5a2 2 0 012-2h3.28a1 1 0 01.948.684l1.498 4.493a1 1 0 01-.502 1.21l-2.257 1.13a11.042 11.042 0 005.516 5.516l1.13-2.257a1 1 0 011.21-.502l4.493 1.498a1 1 0 01.684.949V19a2 2 0 01-2 2h-1C9.716 21 3 14.284 3 6V5z" />
                  </svg>
                  {t('customers.customers.phone')}
                </div>
                <div className="text-gray-900 dark:text-slate-100 text-sm">
                  {detailTarget.phones?.length ? (
                    <div className="space-y-1">
                      {detailTarget.phones.map((p, i) => (
                        <div key={i} className="flex items-center gap-2">
                          <span className="font-medium">{p.value}</span>
                          <span className="text-xs text-gray-400 dark:text-slate-500">
                            {p.type === 1 ? t('customers.customers.phoneTypeWorkShort') : p.type === 2 ? t('customers.customers.phoneTypeHomeShort') : p.type === 4 ? t('customers.customers.phoneTypeMobileShort') : p.type === 5 ? t('customers.customers.phoneTypeFaxShort') : p.type === 6 ? t('customers.customers.phoneTypePagerShort') : t('customers.customers.phoneTypeOtherShort')}
                          </span>
                        </div>
                      ))}
                    </div>
                  ) : '-'}
                </div>
              </div>
              <div className="p-4 bg-white dark:bg-slate-800 border border-gray-100 dark:border-slate-700/50 rounded-xl sm:col-span-2">
                  <div className="flex items-center gap-2 text-xs text-gray-500 dark:text-slate-400 mb-1.5">
                    <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M17.657 16.657L13.414 20.9a1.998 1.998 0 01-2.827 0l-4.244-4.243a8 8 0 1111.314 0z" />
                      <path strokeLinecap="round" strokeLinejoin="round" d="M15 11a3 3 0 11-6 0 3 3 0 016 0z" />
                    </svg>
                    {t('customers.customers.address')}
                  </div>
                  <div className="text-gray-900 dark:text-slate-100 text-sm">
                    {detailTarget.address || '-'}{detailTarget.city && `, ${detailTarget.city}`}{detailTarget.state && `, ${detailTarget.state}`}{detailTarget.zip && ` ${detailTarget.zip}`}
                  </div>
                </div>
                <div className="p-4 bg-white dark:bg-slate-800 border border-gray-100 dark:border-slate-700/50 rounded-xl">
                  <div className="flex items-center gap-2 text-xs text-gray-500 dark:text-slate-400 mb-1.5">
                    <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                    {t('customers.customers.country')}
                  </div>
                  <div className="text-gray-900 dark:text-slate-100 text-sm font-medium">{detailTarget.country || '-'}</div>
                </div>
                <div className="p-4 bg-white dark:bg-slate-800 border border-gray-100 dark:border-slate-700/50 rounded-xl">
                  <div className="flex items-center gap-2 text-xs text-gray-500 dark:text-slate-400 mb-1.5">
                    <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M21 12a9 9 0 01-9 9m9-9a9 9 0 00-9-9m9 9H3m9 9a9 9 0 01-9-9m9 9c1.657 0 3-4.03 3-9s-1.343-9-3-9m0 18c-1.657 0-3-4.03-3-9s1.343-9 3-9m-9 9a9 9 0 019-9" />
                    </svg>
                    {t('customers.customers.website')}
                  </div>
                  <div className="text-gray-900 dark:text-slate-100 text-sm">
                    {detailTarget.websites?.length ? (
                      <div className="space-y-1">
                        {detailTarget.websites.map((w, i) => (
                          <a key={i} href={safeUrl(w)} target="_blank" rel="noopener noreferrer" className="text-primary-600 hover:underline text-sm font-medium">
                            {w}
                          </a>
                        ))}
                      </div>
                    ) : '-'}
                  </div>
                </div>
                <div className="p-4 bg-white dark:bg-slate-800 border border-gray-100 dark:border-slate-700/50 rounded-xl">
                  <div className="flex items-center gap-2 text-xs text-gray-500 dark:text-slate-400 mb-1.5">
                    <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
                    </svg>
                    {t('customers.customers.createdAt')}
                  </div>
                  <div className="text-gray-900 dark:text-slate-100 text-sm font-medium">{formatDateTime(detailTarget.createdAt)}</div>
                </div>
                <div className="p-4 bg-white dark:bg-slate-800 border border-gray-100 dark:border-slate-700/50 rounded-xl">
                  <div className="flex items-center gap-2 text-xs text-gray-500 dark:text-slate-400 mb-1.5">
                    <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M8 10h.01M12 10h.01M16 10h.01M9 16H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-5l-5 5v-5z" />
                    </svg>
                    {t('customers.customers.conversationCount')}
                  </div>
                  <div className="inline-flex items-center justify-center px-2.5 py-1 text-xs font-medium rounded-full bg-indigo-50 dark:bg-indigo-900/30 text-indigo-700 dark:text-indigo-300">
                    {detailTarget.conversationsCount}
                  </div>
                </div>
              </div>

              {/* 自定义字段 */}
              <CustomFieldsPanel
                entityType="customer"
                entityId={detailTarget?.id}
                variant="full"
                readOnly
              />

              {detailTarget.notes && (
                <div className="p-4 bg-amber-50 dark:bg-amber-900/20 border border-amber-100 dark:border-amber-800 rounded-xl">
                  <div className="flex items-center gap-2 text-xs text-amber-700 dark:text-amber-400 mb-1.5">
                    <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                    </svg>
                    {t('customers.customers.notes')}
                  </div>
                  <div className="text-sm text-gray-900 dark:text-slate-100 whitespace-pre-wrap">{detailTarget.notes}</div>
                </div>
              )}

              {/* 历史会话 */}
              <div>
                <div className="flex items-center gap-2 pb-2 border-b border-gray-100 dark:border-slate-700/50">
                  <svg className="w-4 h-4 text-primary-600 dark:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M8 10h.01M12 10h.01M16 10h.01M9 16H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-5l-5 5v-5z" />
                  </svg>
                  <h4 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('customers.customers.historyTitle')}</h4>
                </div>
                {detailLoading ? (
                  <div className="p-8 text-center">
                    <div className="inline-block animate-spin rounded-full h-6 w-6 border-4 border-primary-200 dark:border-primary-800 border-t-primary-600"></div>
                    <p className="mt-2 text-sm text-gray-500 dark:text-slate-400">{t('customers.common.loading')}</p>
                  </div>
                ) : detailConversations.length === 0 ? (
                  <div className="p-8 text-center bg-gray-50 dark:bg-slate-800/50 rounded-xl">
                    <div className="inline-flex items-center justify-center w-12 h-12 rounded-full bg-gray-100 dark:bg-slate-700/50 mb-3">
                      <svg className="w-6 h-6 text-gray-400 dark:text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                        <path strokeLinecap="round" strokeLinejoin="round" d="M8 10h.01M12 10h.01M16 10h.01M9 16H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-5l-5 5v-5z" />
                      </svg>
                    </div>
                    <p className="text-sm text-gray-500 dark:text-slate-400">{t('customers.customers.historyEmpty')}</p>
                  </div>
                ) : (
                  <div className="space-y-2 max-h-72 overflow-y-auto">
                    {detailConversations.map((conv) => (
                      <a
                        key={conv.id}
                        href={`/conversations/${conv.id}`}
                        className="block p-4 bg-white dark:bg-slate-800 border border-gray-100 dark:border-slate-700/50 rounded-xl hover:border-primary-300 dark:hover:border-primary-700 hover:bg-primary-50/30 dark:hover:bg-primary-900/20 transition-all group"
                      >
                        <div className="flex items-start gap-3">
                          <div className="w-8 h-8 rounded-lg bg-gray-100 dark:bg-slate-700/50 flex items-center justify-center shrink-0 group-hover:bg-primary-100 dark:group-hover:bg-primary-900/30 transition-colors">
                            <svg className="w-4 h-4 text-gray-500 dark:text-slate-400 group-hover:text-primary-600 dark:group-hover:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                              <path strokeLinecap="round" strokeLinejoin="round" d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
                            </svg>
                          </div>
                          <div className="flex-1 min-w-0">
                            <div className="flex items-center justify-between mb-1">
                              <span className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate group-hover:text-primary-700 dark:group-hover:text-primary-300">
                                {conv.subject || t('customers.customers.noSubject')}
                              </span>
                              <span className="text-xs text-gray-400 dark:text-slate-500 ml-2 whitespace-nowrap">
                                {formatDate(conv.createdAt)}
                              </span>
                            </div>
                            <div className="text-xs text-gray-500 dark:text-slate-400">#{conv.id} · {conv.customerEmail}</div>
                          </div>
                        </div>
                      </a>
                    ))}
                  </div>
                )}
              </div>

              {/* 编辑按钮 */}
              <div className="flex justify-end gap-2 pt-2 border-t border-gray-100 dark:border-slate-700/50">
                <button
                  onClick={() => {
                    openEdit(detailTarget);
                    setDetailTarget(null);
                  }}
                  className="inline-flex items-center gap-1.5 px-4 py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 text-white rounded-xl shadow-sm shadow-primary-200/50 dark:shadow-primary-900/30 transition-all"
                >
                  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                  </svg>
                  {t('customers.customers.editCustomer')}
                </button>
              </div>
            </div>
          )}
        </Modal>
    </div>
  );
}
