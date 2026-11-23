import { useEffect, useState, useCallback, FormEvent, useMemo, useRef } from 'react';
import { folderApi, mailboxApi } from '../api';
import type { FolderResponse, MailboxResponse } from '../api/types';
import { formatDate } from '../utils/format';
import { getApiErrorMessage } from '../utils/error';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import { useTimedToast } from '../hooks/useTimedToast';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import type { TranslateParams } from '../i18n/types';
import { toIntlLocale } from '../i18n/locale';
import {
  Folder as FolderIcon,
  FolderPlus,
  Mail,
  RefreshCw,
  Plus,
  Search,
  Edit2,
  Trash2,
  X,
  Inbox,
  Shield,
  ShieldCheck,
  Hash,
  Activity,
  Layers,
  Sparkles,
  AlertCircle,
  ChevronUp,
  ChevronDown,
  ChevronsUpDown,
  Filter,
  CheckCircle2,
  Info,
} from 'lucide-react';

/** 与 `I18nContext` 的 `t` 签名一致；模块级纯函数需显式传入 `t` */
type TranslateFn = (key: TranslationKey, params?: TranslateParams) => string;

/**
 * 系统文件夹类型 → 文案 key（8 个系统码，对齐后端 `FolderType.cs`）。
 *
 * 100（`Custom`）与未知码**不在表内**：渲染处回落 `mail.folders.type.customWithCode`
 * （`自定义 ({code})`），与改造前逐字一致。
 */
const FOLDER_TYPE_LABEL_KEYS: Record<number, TranslationKey | undefined> = {
  1: 'mail.folders.type.unassigned',
  20: 'mail.folders.type.mine',
  25: 'mail.folders.type.starred',
  30: 'mail.folders.type.drafts',
  40: 'mail.folders.type.assigned',
  60: 'mail.folders.type.closed',
  70: 'mail.folders.type.deleted',
  80: 'mail.folders.type.spam',
};

/**
 * 系统文件夹码集合——**与字典表解耦的稳定常量**。
 *
 * `isSystemFolder` 的语义是"后端 `FolderType` 的系统码"，**不能随字典增删而漂移**：
 * 后端 `NoneadCSS.Domain/Enums/FolderType.cs` 中 1/20/25/30/40/60/70/80 为系统码，
 * 100（`Custom`）由用户创建、`EnsureMailboxFoldersAsync` 不补齐，必须仍判为"非系统"。
 */
const SYSTEM_FOLDER_TYPES = [1, 20, 25, 30, 40, 60, 70, 80] as const;
const SYSTEM_FOLDER_TYPE_SET: ReadonlySet<number> = new Set(SYSTEM_FOLDER_TYPES);

/** 类型标签：系统码查字典；其余（含 100 Custom）回落 `自定义 ({code})` */
function getFolderTypeLabel(t: TranslateFn, type: number): string {
  const key = FOLDER_TYPE_LABEL_KEYS[type];
  return key ? t(key) : t('mail.folders.type.customWithCode', { code: type });
}

function isSystemFolder(type: number): boolean {
  return SYSTEM_FOLDER_TYPE_SET.has(type);
}

// 文件夹类型 -> 渐变色板（升级版，与邮件中心其他页面保持一致）
interface FolderPalette {
  from: string;
  to: string;
  text: string;
  bg: string;
  ring: string;
  bar: string;
}

const FOLDER_TYPE_PALETTES: Record<number, FolderPalette> = {
  1: { from: 'from-gray-500', to: 'to-slate-500', text: 'text-gray-700 dark:text-gray-300', bg: 'bg-gray-50 dark:bg-gray-900/30', ring: 'ring-gray-500/20', bar: 'bg-gray-500' },
  20: { from: 'from-sky-500', to: 'to-blue-500', text: 'text-sky-700 dark:text-sky-300', bg: 'bg-sky-50 dark:bg-sky-900/30', ring: 'ring-sky-500/20', bar: 'bg-sky-500' },
  25: { from: 'from-amber-500', to: 'to-yellow-500', text: 'text-amber-700 dark:text-amber-300', bg: 'bg-amber-50 dark:bg-amber-900/30', ring: 'ring-amber-500/20', bar: 'bg-amber-500' },
  30: { from: 'from-violet-500', to: 'to-purple-500', text: 'text-violet-700 dark:text-violet-300', bg: 'bg-violet-50 dark:bg-violet-900/30', ring: 'ring-violet-500/20', bar: 'bg-violet-500' },
  40: { from: 'from-indigo-500', to: 'to-blue-600', text: 'text-indigo-700 dark:text-indigo-300', bg: 'bg-indigo-50 dark:bg-indigo-900/30', ring: 'ring-indigo-500/20', bar: 'bg-indigo-500' },
  60: { from: 'from-emerald-500', to: 'to-teal-500', text: 'text-emerald-700 dark:text-emerald-300', bg: 'bg-emerald-50 dark:bg-emerald-900/30', ring: 'ring-emerald-500/20', bar: 'bg-emerald-500' },
  70: { from: 'from-rose-500', to: 'to-red-500', text: 'text-rose-700 dark:text-rose-300', bg: 'bg-rose-50 dark:bg-rose-900/30', ring: 'ring-rose-500/20', bar: 'bg-rose-500' },
  80: { from: 'from-orange-500', to: 'to-amber-500', text: 'text-orange-700 dark:text-orange-300', bg: 'bg-orange-50 dark:bg-orange-900/30', ring: 'ring-orange-500/20', bar: 'bg-orange-500' },
};

const CUSTOM_PALETTE: FolderPalette = {
  from: 'from-fuchsia-500',
  to: 'to-pink-500',
  text: 'text-fuchsia-700 dark:text-fuchsia-300',
  bg: 'bg-fuchsia-50 dark:bg-fuchsia-900/30',
  ring: 'ring-fuchsia-500/20',
  bar: 'bg-fuchsia-500',
};

function getFolderPalette(type: number): FolderPalette {
  return FOLDER_TYPE_PALETTES[type] ?? CUSTOM_PALETTE;
}

type SortKey = 'name' | 'type' | 'active' | 'total' | 'updated';
type SortDir = 'asc' | 'desc';

interface FormState {
  mailboxId: number | null;
  name: string;
  type: number;
}

const emptyForm: FormState = {
  mailboxId: null,
  name: '',
  type: 100,
};

// 通用按钮样式（与邮件中心其他页面一致）
const btnPrimary =
  'inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold text-white rounded-xl bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 shadow-sm shadow-primary-500/20 hover:shadow hover:shadow-primary-500/30 transition-all disabled:opacity-50 ring-1 ring-primary-500/20';
const btnSecondary =
  'inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold text-gray-700 dark:text-slate-200 rounded-xl bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 hover:bg-gray-50 dark:hover:bg-slate-700/60 transition-all disabled:opacity-50 shadow-sm';
const btnDangerSoft =
  'inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-bold rounded-xl bg-gradient-to-br from-rose-50 to-red-50 dark:from-rose-900/30 dark:to-red-900/20 text-rose-700 dark:text-rose-300 ring-1 ring-rose-500/20 hover:from-rose-100 hover:to-red-100 dark:hover:from-rose-900/50 dark:hover:to-red-900/40 transition-all disabled:opacity-50';

const inputBase =
  'w-full px-3.5 py-2.5 text-sm bg-white dark:bg-slate-900/80 border border-gray-200 dark:border-slate-700 rounded-xl text-gray-800 dark:text-slate-200 placeholder:text-gray-400 dark:placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-400 dark:focus:border-primary-500 shadow-sm transition-all';

// 通用：空态插画
function EmptyState({
  icon: Icon,
  title,
  subtitle,
  action,
}: {
  icon: React.ComponentType<{ className?: string }>;
  title: string;
  subtitle: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="py-12 flex flex-col items-center text-gray-400">
      <div className="w-20 h-20 rounded-2xl bg-gradient-to-br from-gray-50 to-gray-100 dark:from-slate-800 dark:to-slate-900 border border-gray-100 dark:border-slate-700 flex items-center justify-center mb-4">
        <Icon className="w-10 h-10 opacity-30" />
      </div>
      <p className="text-sm font-semibold text-gray-500 dark:text-slate-400">{title}</p>
      <p className="text-xs mt-1 text-gray-400 dark:text-slate-500">{subtitle}</p>
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}

// 通用：加载态（缺省文案走共享键 common.shell.loading）
function LoadingState({ text }: { text?: string }) {
  const { t } = useI18n();
  return (
    <div className="flex items-center justify-center gap-3 py-12">
      <div className="w-9 h-9 border-4 border-primary-200 dark:border-primary-900 border-t-primary-600 dark:border-t-primary-400 rounded-full animate-spin shadow-sm" />
      <span className="text-sm font-medium text-gray-500 dark:text-slate-400">{text ?? t('common.shell.loading')}</span>
    </div>
  );
}

export default function Folders() {
  const { t, language } = useI18n();
  const [list, setList] = useState<FolderResponse[]>([]);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState('');

  const [selectedMailbox, setSelectedMailbox] = useState<number | null>(null);
  const [search, setSearch] = useState('');
  const [typeFilter, setTypeFilter] = useState<'all' | 'system' | 'custom'>('all');
  const [sortKey, setSortKey] = useState<SortKey>('type');
  const [sortDir, setSortDir] = useState<SortDir>('asc');

  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<FormState>(emptyForm);
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState('');

  const [deleteTarget, setDeleteTarget] = useState<FolderResponse | null>(null);
  const [deleting, setDeleting] = useState(false);

  // 批量选择
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());
  const [batchDeleteOpen, setBatchDeleteOpen] = useState(false);
  const [batchDeleting, setBatchDeleting] = useState(false);

  const searchInputRef = useRef<HTMLInputElement>(null);
  const { message: toast, show: showToast } = useTimedToast();
  const debouncedSearch = useDebouncedValue(search, 200);

  // 稳定 fetch 函数引用，避免 useCallback 依赖触发 effect 死循环
  const loadMailboxesRef = useRef<() => Promise<void>>(async () => {});
  const loadFoldersRef = useRef<() => Promise<void>>(async () => {});

  const loadMailboxes = useCallback(async () => {
    try {
      const data = await mailboxApi.list();
      const arr = Array.isArray(data) ? data : [];
      setMailboxes(arr);
      setSelectedMailbox((prev) => prev ?? (arr.length > 0 ? arr[0].id : null));
    } catch {
      // 邮箱列表可能为空，忽略
    }
  }, []);

  const loadFolders = useCallback(async () => {
    if (!selectedMailbox) {
      setList([]);
      setLoading(false);
      return;
    }
    try {
      setLoading(true);
      setError('');
      const data = await folderApi.getByMailbox(selectedMailbox);
      setList(Array.isArray(data) ? data : []);
      setSelectedIds(new Set());
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, t('mail.common.loadFailed')));
    } finally {
      setLoading(false);
    }
  }, [selectedMailbox, t]);

  loadMailboxesRef.current = loadMailboxes;
  loadFoldersRef.current = loadFolders;

  useEffect(() => {
    loadMailboxesRef.current();
  }, []);

  useEffect(() => {
    loadFoldersRef.current();
  }, [selectedMailbox]);

  // 过滤 + 排序
  const filteredList = useMemo(() => {
    let result = list;
    if (debouncedSearch.trim()) {
      const kw = debouncedSearch.toLowerCase();
      result = result.filter(
        (f) => f.name.toLowerCase().includes(kw) || getFolderTypeLabel(t, f.type).toLowerCase().includes(kw),
      );
    }
    if (typeFilter !== 'all') {
      result = result.filter((f) =>
        typeFilter === 'system' ? isSystemFolder(f.type) : !isSystemFolder(f.type),
      );
    }
    return result;
  }, [list, debouncedSearch, typeFilter, t]);

  const sortedList = useMemo(() => {
    const arr = [...filteredList];
    arr.sort((a, b) => {
      // 系统文件夹始终在自定义文件夹之前
      const aSys = isSystemFolder(a.type) ? 0 : 1;
      const bSys = isSystemFolder(b.type) ? 0 : 1;
      if (aSys !== bSys) return aSys - bSys;
      let cmp = 0;
      switch (sortKey) {
        case 'name':
          cmp = a.name.localeCompare(b.name, toIntlLocale(language));
          break;
        case 'type':
          cmp = a.type - b.type;
          break;
        case 'active':
          cmp = a.activeCount - b.activeCount;
          break;
        case 'total':
          cmp = a.totalCount - b.totalCount;
          break;
        case 'updated':
          cmp = new Date(a.updatedAt).getTime() - new Date(b.updatedAt).getTime();
          break;
      }
      return sortDir === 'asc' ? cmp : -cmp;
    });
    return arr;
  }, [filteredList, sortKey, sortDir, language]);

  const handleSort = (key: SortKey) => {
    if (sortKey === key) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortKey(key);
      setSortDir('asc');
    }
  };

  const SortIcon = ({ k }: { k: SortKey }) => {
    if (sortKey !== k) {
      return <ChevronsUpDown className="w-3 h-3 text-gray-300 dark:text-slate-600" />;
    }
    return sortDir === 'asc' ? (
      <ChevronUp className="w-3.5 h-3.5 text-primary-600 dark:text-primary-400" strokeWidth={2.5} />
    ) : (
      <ChevronDown className="w-3.5 h-3.5 text-primary-600 dark:text-primary-400" strokeWidth={2.5} />
    );
  };

  const handleCreate = () => {
    setEditingId(null);
    setForm({ ...emptyForm, mailboxId: selectedMailbox });
    setFormError('');
    setModalOpen(true);
  };

  const handleEdit = (folder: FolderResponse) => {
    setEditingId(folder.id);
    setForm({
      mailboxId: folder.mailboxId,
      name: folder.name,
      type: folder.type,
    });
    setFormError('');
    setModalOpen(true);
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!form.mailboxId || !form.name.trim()) {
      setFormError(t('mail.folders.form.errorRequired'));
      return;
    }
    setSubmitting(true);
    setFormError('');
    try {
      if (editingId) {
        await folderApi.update(editingId, { name: form.name.trim() });
        showToast({ type: 'success', text: t('mail.folders.toast.updated', { name: form.name.trim() }) });
      } else {
        await folderApi.create({
          mailboxId: form.mailboxId,
          name: form.name.trim(),
          type: form.type,
        });
        showToast({ type: 'success', text: t('mail.folders.toast.created', { name: form.name.trim() }) });
      }
      setModalOpen(false);
      await loadFolders();
    } catch (err: unknown) {
      setFormError(getApiErrorMessage(err, t('mail.common.operationFailed')));
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await folderApi.delete(deleteTarget.id);
      showToast({ type: 'success', text: t('mail.folders.toast.deleted', { name: deleteTarget.name }) });
      setDeleteTarget(null);
      await loadFolders();
    } catch (err: unknown) {
      showToast({ type: 'error', text: getApiErrorMessage(err, t('mail.common.deleteFailed')) });
    } finally {
      setDeleting(false);
    }
  };

  const handleRefreshCounters = async () => {
    if (!selectedMailbox) return;
    setRefreshing(true);
    try {
      const res = await folderApi.refreshCounters(selectedMailbox);
      setList(res.folders);
      showToast({ type: 'success', text: t('mail.folders.toast.countersRefreshed', { count: res.affected }) });
    } catch (err: unknown) {
      showToast({ type: 'error', text: getApiErrorMessage(err, t('mail.folders.toast.refreshFailed')) });
    } finally {
      setRefreshing(false);
    }
  };

  const handleEnsureSystem = async () => {
    if (!selectedMailbox) return;
    setRefreshing(true);
    try {
      const res = await folderApi.ensureSystem(selectedMailbox);
      if (res.created > 0) {
        showToast({ type: 'success', text: t('mail.folders.toast.systemEnsured', { count: res.created }) });
      } else {
        showToast({ type: 'success', text: t('mail.folders.toast.systemAlreadyComplete') });
      }
      await loadFolders();
    } catch (err: unknown) {
      showToast({ type: 'error', text: getApiErrorMessage(err, t('mail.folders.toast.ensureFailed')) });
    } finally {
      setRefreshing(false);
    }
  };

  // 批量选择
  const toggleSelect = (id: number) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };
  const toggleSelectAll = () => {
    const selectableIds = sortedList.filter((f) => !isSystemFolder(f.type)).map((f) => f.id);
    if (selectableIds.every((id) => selectedIds.has(id))) {
      setSelectedIds(new Set());
    } else {
      setSelectedIds(new Set(selectableIds));
    }
  };
  const handleBatchDelete = async () => {
    const ids = Array.from(selectedIds);
    if (ids.length === 0) return;
    setBatchDeleting(true);
    let successCount = 0;
    let failCount = 0;
    for (const id of ids) {
      try {
        await folderApi.delete(id);
        successCount++;
      } catch {
        failCount++;
      }
    }
    if (successCount > 0) {
      showToast({ type: 'success', text: `${t('mail.folders.toast.batchDeleted', { count: successCount })}${failCount > 0 ? t('mail.folders.toast.batchDeleteFailedSuffix', { count: failCount }) : ''}` });
    } else {
      showToast({ type: 'error', text: t('mail.folders.toast.batchDeleteFailed', { count: failCount }) });
    }
    setBatchDeleteOpen(false);
    setSelectedIds(new Set());
    await loadFolders();
    setBatchDeleting(false);
  };

  // 键盘快捷键
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      // Ctrl/Cmd + K：聚焦搜索
      if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
        e.preventDefault();
        searchInputRef.current?.focus();
        return;
      }
      // Ctrl/Cmd + N：新建（仅在未聚焦输入框时）
      if ((e.ctrlKey || e.metaKey) && e.key === 'n' && !['INPUT', 'TEXTAREA', 'SELECT'].includes((e.target as HTMLElement).tagName)) {
        e.preventDefault();
        handleCreate();
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedMailbox]);

  const getMailboxName = (id: number) => mailboxes.find((m) => m.id === id)?.name ?? `#${id}`;

  const systemCount = list.filter((f) => isSystemFolder(f.type)).length;
  const customCount = list.filter((f) => !isSystemFolder(f.type)).length;
  const totalActive = list.reduce((sum, f) => sum + f.activeCount, 0);
  const totalAll = list.reduce((sum, f) => sum + f.totalCount, 0);
  const customSelectedCount = Array.from(selectedIds).filter((id) => {
    const folder = list.find((f) => f.id === id);
    return folder && !isSystemFolder(folder.type);
  }).length;

  // 统计卡数据（表里只持有 TranslationKey，渲染处经 t()）
  const statItems = useMemo(() => {
    const items = [
      {
        labelKey: 'mail.folders.page.statSystemLabel',
        value: systemCount,
        hintKey: 'mail.folders.page.statSystemHint',
        icon: ShieldCheck,
        palette: { from: 'from-sky-500', to: 'to-blue-500', textLight: 'text-sky-600 dark:text-sky-400', bgSoft: 'bg-sky-50 dark:bg-sky-900/30', ringSoft: 'ring-sky-500/20' },
      },
      {
        labelKey: 'mail.folders.page.statCustomLabel',
        value: customCount,
        hintKey: 'mail.folders.page.statCustomHint',
        icon: FolderPlus,
        palette: { from: 'from-violet-500', to: 'to-fuchsia-500', textLight: 'text-violet-600 dark:text-violet-400', bgSoft: 'bg-violet-50 dark:bg-violet-900/30', ringSoft: 'ring-violet-500/20' },
      },
      {
        labelKey: 'mail.folders.page.statActiveLabel',
        value: totalActive,
        hintKey: 'mail.folders.page.statActiveHint',
        icon: Activity,
        palette: { from: 'from-emerald-500', to: 'to-teal-500', textLight: 'text-emerald-600 dark:text-emerald-400', bgSoft: 'bg-emerald-50 dark:bg-emerald-900/30', ringSoft: 'ring-emerald-500/20' },
      },
      {
        labelKey: 'mail.folders.page.statTotalLabel',
        value: totalAll,
        hintKey: 'mail.folders.page.statTotalHint',
        icon: Layers,
        palette: { from: 'from-primary-500', to: 'to-indigo-500', textLight: 'text-primary-600 dark:text-primary-400', bgSoft: 'bg-primary-50 dark:bg-primary-900/30', ringSoft: 'ring-primary-500/20' },
      },
    ] as const;
    return items;
  }, [systemCount, customCount, totalActive, totalAll]);

  // 筛选摘要胶囊（只持有 key 与值：搜索前缀走共享键，值与译文分开渲染，不塞进译文字符串）
  const filterChips = useMemo(() => {
    const chips: { labelKey: TranslationKey; value?: string; onClear: () => void }[] = [];
    if (debouncedSearch.trim()) {
      chips.push({ labelKey: 'mail.common.searchPrefix', value: debouncedSearch.trim(), onClear: () => setSearch('') });
    }
    if (typeFilter !== 'all') {
      chips.push({
        labelKey: typeFilter === 'system' ? 'mail.folders.filter.typeSystem' : 'mail.folders.filter.typeCustom',
        onClear: () => setTypeFilter('all'),
      });
    }
    return chips;
  }, [debouncedSearch, typeFilter]);

  const currentMailbox = mailboxes.find((m) => m.id === selectedMailbox);

  return (
    <div className="space-y-5">
      {/* 页头 Hero */}
      <div className="relative overflow-hidden rounded-3xl border border-gray-200 dark:border-slate-700/60 bg-gradient-to-br from-primary-50 via-white to-indigo-50 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-900/20 shadow-sm">
        <div className="pointer-events-none absolute -top-24 -right-24 w-72 h-72 rounded-full bg-primary-500/20 blur-3xl" />
        <div className="pointer-events-none absolute top-16 -left-16 w-60 h-60 rounded-full bg-indigo-500/15 blur-3xl" />
        <div className="relative p-6 md:p-7">
          <div className="flex items-start justify-between gap-4 flex-wrap">
            <div className="flex items-start gap-4">
              <div className="shrink-0 w-14 h-14 rounded-2xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-lg shadow-primary-500/30 flex items-center justify-center ring-1 ring-white/40 dark:ring-white/10">
                <FolderIcon className="w-7 h-7" strokeWidth={2} />
              </div>
              <div className="min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <h1 className="text-2xl md:text-3xl font-extrabold tracking-tight text-gray-900 dark:text-slate-50">{t('mail.folders.page.title')}</h1>
                  <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider rounded-full bg-gradient-to-r from-sky-100 to-blue-100 dark:from-sky-900/40 dark:to-blue-900/40 text-sky-700 dark:text-sky-300 ring-1 ring-sky-500/20">
                    <Mail className="w-3 h-3" />
                    {t('mail.folders.page.badge')}
                  </span>
                </div>
                <p className="text-sm md:text-base text-gray-500 dark:text-slate-400 mt-1.5 leading-relaxed">
                  {t('mail.folders.page.subtitle')}
                </p>
              </div>
            </div>
            <div className="flex items-center gap-2.5 flex-wrap">
              <button
                onClick={handleEnsureSystem}
                disabled={!selectedMailbox || refreshing}
                className={btnSecondary}
                title={t('mail.folders.page.ensureSystemTooltip')}
              >
                <ShieldCheck className={`w-4 h-4 ${refreshing ? 'animate-pulse' : ''}`} />
                {t('mail.folders.page.ensureSystem')}
              </button>
              <button
                onClick={handleRefreshCounters}
                disabled={!selectedMailbox || refreshing}
                className={btnSecondary}
                title={t('mail.folders.page.refreshCountersTooltip')}
              >
                <RefreshCw className={`w-4 h-4 ${refreshing ? 'animate-spin' : ''}`} />
                {t('mail.folders.page.refreshCounters')}
              </button>
              <button
                onClick={handleCreate}
                disabled={!selectedMailbox}
                className={btnPrimary}
              >
                <Plus className="w-4 h-4" />
                {t('mail.folders.page.create')}
              </button>
            </div>
          </div>

          {/* 统计卡片 */}
          {!loading && list.length > 0 && (
            <div className="grid grid-cols-2 md:grid-cols-4 gap-3 md:gap-4 relative mt-6">
              {statItems.map((stat, idx) => (
                <div
                  key={idx}
                  className={`group relative overflow-hidden rounded-2xl border border-gray-200 dark:border-slate-700/60 bg-white dark:bg-slate-800/60 p-4 shadow-sm hover:shadow-md transition-all ${stat.palette.bgSoft} ring-1 ${stat.palette.ringSoft}`}
                >
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="text-xs font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t(stat.labelKey)}</p>
                      <p className={`text-2xl md:text-3xl font-extrabold mt-1 ${stat.palette.textLight}`}>{stat.value}</p>
                      <p className="text-[10px] text-gray-400 dark:text-slate-500 mt-0.5">{t(stat.hintKey)}</p>
                    </div>
                    <div className={`shrink-0 w-10 h-10 rounded-xl bg-gradient-to-br ${stat.palette.from} ${stat.palette.to} text-white shadow-sm flex items-center justify-center`}>
                      <stat.icon className="w-5 h-5" strokeWidth={2} />
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* 邮箱选择器（胶囊式 Tab） */}
      {mailboxes.length > 0 && (
        <div className="relative overflow-hidden rounded-2xl border border-gray-200 dark:border-slate-700/60 bg-white dark:bg-slate-800/60 shadow-sm p-3 md:p-4">
          <div className="flex items-center gap-3 flex-wrap">
            <div className="inline-flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400">
              <Mail className="w-3.5 h-3.5" />
              {t('mail.folders.list.mailbox')}
            </div>
            <div className="flex items-center gap-1.5 flex-wrap">
              {mailboxes.map((mb) => {
                const active = selectedMailbox === mb.id;
                return (
                  <button
                    key={mb.id}
                    onClick={() => setSelectedMailbox(mb.id)}
                    className={`group inline-flex items-center gap-1.5 px-3 py-1.5 text-xs md:text-sm font-bold rounded-xl transition-all ${
                      active
                        ? 'bg-gradient-to-br from-primary-600 to-indigo-600 text-white shadow-md shadow-primary-500/20 ring-1 ring-primary-500/20'
                        : 'bg-white dark:bg-slate-900/60 text-gray-700 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-700/60 border border-gray-200 dark:border-slate-700 shadow-sm'
                    }`}
                  >
                    <Mail className={`w-3.5 h-3.5 ${active ? 'opacity-90' : 'opacity-60'}`} />
                    {mb.name}
                  </button>
                );
              })}
            </div>
            {currentMailbox && (
              <div className="ml-auto inline-flex items-center gap-2 text-xs text-gray-500 dark:text-slate-400">
                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-gray-100 dark:bg-slate-700/50 text-gray-600 dark:text-slate-300 font-mono">
                  <Hash className="w-3 h-3" />
                  {currentMailbox.email}
                </span>
              </div>
            )}
          </div>
        </div>
      )}

      {/* 搜索 + 过滤工具栏 */}
      {!loading && list.length > 0 && (
        <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm p-3 md:p-4 flex items-center gap-2 md:gap-3 flex-wrap">
          <div className="inline-flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mr-0.5">
            <Filter className="w-3.5 h-3.5" />
            {t('mail.folders.filter.label')}
          </div>
          <div className="relative flex-1 min-w-[200px] max-w-[420px]">
            <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
            <input
              ref={searchInputRef}
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('mail.folders.filter.searchPlaceholder')}
              className="w-full pl-10 pr-20 py-2 text-sm bg-slate-50 dark:bg-slate-900/60 border border-gray-200 dark:border-slate-700 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-400 dark:focus:border-primary-500 text-gray-800 dark:text-slate-200 placeholder:text-gray-400 dark:placeholder:text-slate-500 transition-all shadow-sm"
            />
            <kbd className="hidden sm:inline-flex absolute right-2 top-1/2 -translate-y-1/2 px-1.5 py-0.5 text-[10px] font-mono text-gray-500 dark:text-slate-400 bg-gray-100 dark:bg-slate-700 border border-gray-200 dark:border-slate-600 rounded">
              Ctrl+K
            </kbd>
          </div>
          <div className="inline-flex rounded-xl border border-gray-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-900/60 p-0.5">
            {(['all', 'system', 'custom'] as const).map((opt) => {
              const active = typeFilter === opt;
              return (
                <button
                  key={opt}
                  onClick={() => setTypeFilter(opt)}
                  className={`px-3 py-1.5 text-xs font-bold rounded-lg transition-all ${
                    active
                      ? 'bg-gradient-to-br from-primary-600 to-indigo-600 text-white shadow-sm shadow-primary-500/20'
                      : 'text-gray-600 dark:text-slate-300 hover:bg-white dark:hover:bg-slate-700/60'
                  }`}
                >
                  {opt === 'all' ? t('mail.folders.filter.all') : opt === 'system' ? t('mail.common.system') : t('mail.common.custom')}
                </button>
              );
            })}
          </div>
          {customSelectedCount > 0 && (
            <button
              onClick={() => setBatchDeleteOpen(true)}
              className={btnDangerSoft}
            >
              <Trash2 className="w-3.5 h-3.5" />
              {t('mail.folders.filter.batchDelete', { count: customSelectedCount })}
            </button>
          )}
          <div className="ml-auto inline-flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400">
            <Inbox className="w-3.5 h-3.5" />
            {t('mail.folders.filter.totalPrefix')} <b className="font-black text-gray-700 dark:text-slate-200">{sortedList.length}</b> / {list.length}
          </div>
        </div>
      )}

      {/* 筛选摘要胶囊条 */}
      {!loading && list.length > 0 && filterChips.length > 0 && (
        <div className="flex items-center gap-2 flex-wrap">
          <span className="inline-flex items-center gap-1 text-[11px] font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400">
            <Filter className="w-3 h-3" />
            {t('mail.common.currentFiltersColon')}
          </span>
          {filterChips.map((chip, idx) => (
            <span
              key={idx}
              className="inline-flex items-center gap-1 pl-2.5 pr-1 py-1 rounded-lg bg-gradient-to-br from-primary-50 to-indigo-50 dark:from-primary-900/30 dark:to-indigo-900/20 text-primary-700 dark:text-primary-300 text-xs font-bold ring-1 ring-primary-500/20"
            >
              {t(chip.labelKey)}{chip.value}
              <button
                onClick={chip.onClear}
                className="p-0.5 rounded hover:bg-primary-200/50 dark:hover:bg-primary-800/50 transition"
                title={t('mail.common.removeFilter')}
              >
                <X className="w-3 h-3" />
              </button>
            </span>
          ))}
          <button
            onClick={() => {
              setSearch('');
              setTypeFilter('all');
            }}
            className="text-xs text-rose-600 dark:text-rose-400 hover:underline font-bold"
          >
            {t('mail.common.clearAllFilters')}
          </button>
        </div>
      )}

      {/* 错误提示 */}
      {error && (
        <div className="rounded-2xl border border-rose-200 dark:border-rose-900/40 bg-gradient-to-br from-rose-50 to-red-50 dark:from-rose-900/30 dark:to-red-900/20 p-4 text-rose-700 dark:text-rose-300 text-sm flex items-start gap-3 shadow-sm">
          <div className="shrink-0 w-8 h-8 rounded-lg bg-gradient-to-br from-rose-100 to-red-100 dark:from-rose-900/50 dark:to-red-900/50 text-rose-600 dark:text-rose-400 flex items-center justify-center ring-1 ring-rose-500/20">
            <AlertCircle className="w-4 h-4" strokeWidth={2} />
          </div>
          <div className="flex-1 min-w-0">
            <p className="font-bold">{t('mail.common.loadFailed')}</p>
            <p className="text-xs mt-0.5 opacity-90">{error}</p>
          </div>
          <button
            onClick={() => setError('')}
            className="p-1 text-rose-400 hover:text-rose-600 rounded-md hover:bg-rose-100 dark:hover:bg-rose-900/40 transition"
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {/* 列表 */}
      {loading ? (
        <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm">
          <LoadingState text={t('mail.folders.list.loading')} />
        </div>
      ) : list.length === 0 ? (
        <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm">
          <EmptyState
            icon={FolderIcon}
            title={selectedMailbox ? t('mail.folders.list.emptyNoFolders') : t('mail.folders.list.emptyNoMailbox')}
            subtitle={selectedMailbox ? t('mail.folders.list.emptyNoFoldersHint') : t('mail.folders.list.emptyNoMailboxHint')}
            action={
              selectedMailbox && (
                <button
                  onClick={handleEnsureSystem}
                  className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold text-white rounded-xl bg-gradient-to-br from-emerald-600 to-teal-600 hover:from-emerald-700 hover:to-teal-700 shadow-sm shadow-emerald-500/20 transition-all ring-1 ring-emerald-500/20"
                >
                  <ShieldCheck className="w-4 h-4" />
                  {t('mail.folders.page.ensureSystem')}
                </button>
              )
            }
          />
        </div>
      ) : filteredList.length === 0 ? (
        <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm">
          <EmptyState
            icon={Search}
            title={t('mail.folders.list.emptyNoMatch')}
            subtitle={t('mail.folders.list.emptyNoMatchHint')}
            action={
              <button
                onClick={() => {
                  setSearch('');
                  setTypeFilter('all');
                }}
                className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold text-primary-600 dark:text-primary-400 bg-primary-50 dark:bg-primary-900/20 rounded-xl hover:bg-primary-100 dark:hover:bg-primary-900/30 transition-all ring-1 ring-primary-500/20"
              >
                <X className="w-4 h-4" />
                {t('mail.folders.list.clearFilters')}
              </button>
            }
          />
        </div>
      ) : (
        <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm overflow-hidden">
          {/* PC 端表格视图 */}
          <div className="hidden md:block overflow-x-auto">
            <table className="w-full min-w-[920px] text-sm">
              <thead>
                <tr className="bg-gradient-to-r from-primary-50 via-white to-indigo-50 dark:from-slate-900/60 dark:via-slate-800 dark:to-indigo-900/20 text-[11px] font-black uppercase tracking-wider text-gray-600 dark:text-slate-300 border-b border-gray-100 dark:border-slate-700/60">
                  <th className="px-4 md:px-5 py-3.5 w-12">
                    <input
                      type="checkbox"
                      checked={
                        sortedList.filter((f) => !isSystemFolder(f.type)).length > 0 &&
                        sortedList
                          .filter((f) => !isSystemFolder(f.type))
                          .every((f) => selectedIds.has(f.id))
                      }
                      onChange={toggleSelectAll}
                      className="w-4 h-4 text-primary-600 border-gray-300 dark:border-slate-500 rounded focus:ring-primary-500 cursor-pointer"
                      title={t('mail.folders.list.selectAllTooltip')}
                    />
                  </th>
                  <th className="px-4 md:px-5 py-3.5 text-left min-w-[200px] cursor-pointer select-none" onClick={() => handleSort('name')}>
                    <span className="inline-flex items-center gap-1.5">{t('mail.folders.list.columnName')} <SortIcon k="name" /></span>
                  </th>
                  <th className="px-4 md:px-5 py-3.5 text-left min-w-[140px] cursor-pointer select-none" onClick={() => handleSort('type')}>
                    <span className="inline-flex items-center gap-1.5">{t('mail.common.type')} <SortIcon k="type" /></span>
                  </th>
                  <th className="px-4 md:px-5 py-3.5 text-center min-w-[90px] cursor-pointer select-none" onClick={() => handleSort('active')}>
                    <span className="inline-flex items-center gap-1.5 justify-center">{t('mail.folders.list.columnActive')} <SortIcon k="active" /></span>
                  </th>
                  <th className="px-4 md:px-5 py-3.5 text-center min-w-[90px] cursor-pointer select-none" onClick={() => handleSort('total')}>
                    <span className="inline-flex items-center gap-1.5 justify-center">{t('mail.folders.list.columnTotal')} <SortIcon k="total" /></span>
                  </th>
                  <th className="px-4 md:px-5 py-3.5 text-left min-w-[120px]">{t('mail.common.ownerMailbox')}</th>
                  <th className="px-4 md:px-5 py-3.5 text-left min-w-[140px] cursor-pointer select-none" onClick={() => handleSort('updated')}>
                    <span className="inline-flex items-center gap-1.5">{t('mail.folders.list.columnUpdated')} <SortIcon k="updated" /></span>
                  </th>
                  <th className="px-4 md:px-5 py-3.5 text-right pr-5 md:pr-6 w-28">{t('mail.common.actions')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-50 dark:divide-slate-700/50">
                {sortedList.map((folder) => {
                  const sys = isSystemFolder(folder.type);
                  const palette = getFolderPalette(folder.type);
                  return (
                    <tr
                      key={folder.id}
                      className={`hover:bg-gradient-to-r hover:from-primary-50/30 hover:via-white hover:to-indigo-50/30 dark:hover:from-slate-800/60 dark:hover:via-slate-800/30 dark:hover:to-slate-800/60 transition-colors ${
                        selectedIds.has(folder.id) ? 'bg-primary-50/50 dark:bg-primary-900/10' : ''
                      }`}
                    >
                      <td className="px-4 md:px-5 py-3.5">
                        <input
                          type="checkbox"
                          checked={selectedIds.has(folder.id)}
                          onChange={() => toggleSelect(folder.id)}
                          disabled={sys}
                          className="w-4 h-4 text-primary-600 border-gray-300 dark:border-slate-500 rounded focus:ring-primary-500 disabled:opacity-30 cursor-pointer disabled:cursor-not-allowed"
                          title={sys ? t('mail.folders.list.systemDisabledTooltip') : t('mail.folders.list.selectTooltip')}
                        />
                      </td>
                      <td className="px-4 md:px-5 py-3.5">
                        <div className="flex items-center gap-2.5">
                          <div className={`shrink-0 w-9 h-9 rounded-xl bg-gradient-to-br ${palette.from} ${palette.to} text-white flex items-center justify-center shadow-sm ring-1 ${palette.ring}`}>
                            <FolderIcon className="w-4 h-4" strokeWidth={2} />
                          </div>
                          <div className="min-w-0">
                            <div className="flex items-center gap-1.5 flex-wrap">
                              <span className="font-bold text-gray-800 dark:text-slate-100 tracking-tight">{folder.name}</span>
                              {sys && (
                                <span className="inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded-md bg-gradient-to-br from-sky-50 to-blue-50 dark:from-sky-900/30 dark:to-blue-900/20 text-sky-700 dark:text-sky-300 text-[10px] font-bold uppercase tracking-wider ring-1 ring-sky-500/20">
                                  <Shield className="w-2.5 h-2.5" />
                                  {t('mail.common.system')}
                                </span>
                              )}
                            </div>
                            <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5 font-mono">
                              ID #{folder.id}
                            </div>
                          </div>
                        </div>
                      </td>
                      <td className="px-4 md:px-5 py-3.5">
                        <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider ring-1 ${palette.bg} ${palette.text} ${palette.ring}`}>
                          <span className={`w-1.5 h-1.5 rounded-full ${palette.bar}`} />
                          {getFolderTypeLabel(t, folder.type)}
                        </span>
                      </td>
                      <td className="px-4 md:px-5 py-3.5 text-center">
                        <span
                          className={`inline-flex items-center justify-center min-w-[2rem] px-2 py-0.5 text-xs font-black rounded-full ${
                            folder.activeCount > 0
                              ? 'bg-gradient-to-br from-emerald-50 to-teal-50 dark:from-emerald-900/30 dark:to-teal-900/20 text-emerald-700 dark:text-emerald-300 ring-1 ring-emerald-500/20'
                              : 'bg-gray-100 dark:bg-slate-700/50 text-gray-400 dark:text-slate-500 ring-1 ring-gray-500/10'
                          }`}
                        >
                          {folder.activeCount}
                        </span>
                      </td>
                      <td className="px-4 md:px-5 py-3.5 text-center">
                        <span className="inline-flex items-center justify-center min-w-[2rem] px-2 py-0.5 text-xs font-bold text-gray-700 dark:text-slate-300 rounded-md bg-gray-100 dark:bg-slate-700/50 ring-1 ring-gray-500/10 tabular-nums">
                          {folder.totalCount}
                        </span>
                      </td>
                      <td className="px-4 md:px-5 py-3.5">
                        <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md bg-gray-50 dark:bg-slate-700/30 text-gray-600 dark:text-slate-300 text-xs font-medium ring-1 ring-gray-500/10">
                          <Mail className="w-3 h-3 opacity-60" />
                          {getMailboxName(folder.mailboxId)}
                        </span>
                      </td>
                      <td className="px-4 md:px-5 py-3.5 text-xs text-gray-500 dark:text-slate-400 whitespace-nowrap">
                        {formatDate(folder.updatedAt)}
                      </td>
                      <td className="px-4 md:px-5 py-3.5 text-right">
                        <div className="inline-flex items-center gap-1">
                          <button
                            onClick={() => handleEdit(folder)}
                            className="p-2.5 text-gray-400 dark:text-slate-500 hover:text-sky-600 dark:hover:text-sky-400 hover:bg-sky-50 dark:hover:bg-sky-900/30 rounded-xl transition-all"
                            title={t('common.edit')}
                          >
                            <Edit2 className="w-[18px] h-[18px]" strokeWidth={2} />
                          </button>
                          {!sys && (
                            <button
                              onClick={() => setDeleteTarget(folder)}
                              className="p-2.5 text-gray-400 dark:text-slate-500 hover:text-rose-600 dark:hover:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-900/30 rounded-xl transition-all"
                              title={t('common.delete')}
                            >
                              <Trash2 className="w-[18px] h-[18px]" strokeWidth={2} />
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          {/* 移动端卡片视图 */}
          <div className="md:hidden divide-y divide-gray-50 dark:divide-slate-700/50">
            {sortedList.map((folder) => {
              const sys = isSystemFolder(folder.type);
              const palette = getFolderPalette(folder.type);
              return (
                <div
                  key={folder.id}
                  className={`p-4 hover:bg-gradient-to-r hover:from-primary-50/30 hover:via-white hover:to-indigo-50/30 dark:hover:from-slate-800/60 dark:hover:via-slate-800/30 dark:hover:to-slate-800/60 transition-colors ${
                    selectedIds.has(folder.id) ? 'bg-primary-50/50 dark:bg-primary-900/10' : ''
                  }`}
                >
                  {/* 顶部：复选框 + 名称 + 操作 */}
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex items-start gap-2 min-w-0 flex-1">
                      <input
                        type="checkbox"
                        checked={selectedIds.has(folder.id)}
                        onChange={() => toggleSelect(folder.id)}
                        disabled={sys}
                        className="mt-1.5 w-4 h-4 text-primary-600 border-gray-300 dark:border-slate-500 rounded focus:ring-primary-500 disabled:opacity-30 cursor-pointer disabled:cursor-not-allowed shrink-0"
                        title={sys ? t('mail.folders.list.systemDisabledTooltip') : t('mail.folders.list.selectTooltip')}
                      />
                      <div className={`shrink-0 w-9 h-9 rounded-xl bg-gradient-to-br ${palette.from} ${palette.to} text-white flex items-center justify-center shadow-sm ring-1 ${palette.ring}`}>
                        <FolderIcon className="w-4 h-4" strokeWidth={2} />
                      </div>
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-1.5 flex-wrap">
                          <span className="font-bold text-gray-800 dark:text-slate-100 tracking-tight truncate">{folder.name}</span>
                          {sys && (
                            <span className="inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded-md bg-gradient-to-br from-sky-50 to-blue-50 dark:from-sky-900/30 dark:to-blue-900/20 text-sky-700 dark:text-sky-300 text-[10px] font-bold uppercase tracking-wider ring-1 ring-sky-500/20">
                              <Shield className="w-2.5 h-2.5" />
                              {t('mail.common.system')}
                            </span>
                          )}
                        </div>
                        <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5 font-mono">
                          ID #{folder.id}
                        </div>
                      </div>
                    </div>
                    <div className="shrink-0 inline-flex items-center gap-1">
                      <button
                        onClick={() => handleEdit(folder)}
                        className="p-2 text-gray-400 dark:text-slate-500 hover:text-sky-600 dark:hover:text-sky-400 hover:bg-sky-50 dark:hover:bg-sky-900/30 rounded-xl transition-all"
                        title={t('common.edit')}
                      >
                        <Edit2 className="w-[18px] h-[18px]" strokeWidth={2} />
                      </button>
                      {!sys && (
                        <button
                          onClick={() => setDeleteTarget(folder)}
                          className="p-2 text-gray-400 dark:text-slate-500 hover:text-rose-600 dark:hover:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-900/30 rounded-xl transition-all"
                          title={t('common.delete')}
                        >
                          <Trash2 className="w-[18px] h-[18px]" strokeWidth={2} />
                        </button>
                      )}
                    </div>
                  </div>

                  {/* 标签组：类型 / 所属邮箱 */}
                  <div className="mt-2.5 flex items-center gap-1.5 flex-wrap">
                    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider ring-1 ${palette.bg} ${palette.text} ${palette.ring}`}>
                      <span className={`w-1.5 h-1.5 rounded-full ${palette.bar}`} />
                      {getFolderTypeLabel(t, folder.type)}
                    </span>
                    <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md bg-gray-50 dark:bg-slate-700/30 text-gray-600 dark:text-slate-300 text-xs font-medium ring-1 ring-gray-500/10">
                      <Mail className="w-3 h-3 opacity-60" />
                      {getMailboxName(folder.mailboxId)}
                    </span>
                  </div>

                  {/* 统计 + 时间 */}
                  <div className="mt-2.5 flex items-center gap-2 flex-wrap">
                    <span
                      className={`inline-flex items-center justify-center min-w-[2rem] px-2 py-0.5 text-xs font-black rounded-full ${
                        folder.activeCount > 0
                          ? 'bg-gradient-to-br from-emerald-50 to-teal-50 dark:from-emerald-900/30 dark:to-teal-900/20 text-emerald-700 dark:text-emerald-300 ring-1 ring-emerald-500/20'
                          : 'bg-gray-100 dark:bg-slate-700/50 text-gray-400 dark:text-slate-500 ring-1 ring-gray-500/10'
                      }`}
                    >
                      {t('mail.folders.list.activeBadge', { count: folder.activeCount })}
                    </span>
                    <span className="inline-flex items-center justify-center min-w-[2rem] px-2 py-0.5 text-xs font-bold text-gray-700 dark:text-slate-300 rounded-md bg-gray-100 dark:bg-slate-700/50 ring-1 ring-gray-500/10 tabular-nums">
                      {t('mail.folders.list.totalBadge', { count: folder.totalCount })}
                    </span>
                    <span className="text-[11px] text-gray-400 dark:text-slate-500">
                      {t('mail.common.updatedAtPrefix')} {formatDate(folder.updatedAt)}
                    </span>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* 新建/编辑弹窗 */}
      <Modal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        title={editingId ? t('mail.folders.form.titleEdit') : t('mail.folders.page.create')}
        size="md"
        footer={
          <>
            <button
              onClick={() => setModalOpen(false)}
              disabled={submitting}
              className="px-4 py-2 text-sm font-bold text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700/60 transition-all shadow-sm"
            >
              {t('common.cancel')}
            </button>
            <button
              type="submit"
              form="folder-form"
              disabled={submitting}
              className="px-4 py-2 text-sm font-bold text-white rounded-xl bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 disabled:opacity-50 shadow-sm shadow-primary-500/20 transition-all ring-1 ring-primary-500/20"
            >
              {submitting ? t('mail.folders.form.saving') : t('common.save')}
            </button>
          </>
        }
      >
        <form id="folder-form" onSubmit={handleSubmit} className="space-y-5">
          {/* 渐变头部装饰卡 */}
          <div className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-primary-50 via-white to-indigo-50 dark:from-slate-900 dark:via-slate-800/60 dark:to-indigo-900/20 border border-primary-100 dark:border-slate-700/60 p-4">
            <div className="pointer-events-none absolute -top-12 -right-12 w-32 h-32 rounded-full bg-primary-500/10 blur-3xl" />
            <div className="relative flex items-start gap-3">
              <div className="shrink-0 w-10 h-10 rounded-xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-md shadow-primary-500/30 flex items-center justify-center ring-1 ring-white/40 dark:ring-white/10">
                {editingId ? <Edit2 className="w-4 h-4" strokeWidth={2} /> : <FolderPlus className="w-4 h-4" strokeWidth={2} />}
              </div>
              <div className="min-w-0">
                <h4 className="text-sm font-extrabold tracking-tight text-gray-900 dark:text-slate-50">
                  {editingId ? t('mail.folders.form.editHeading') : t('mail.folders.form.createHeading')}
                </h4>
                <p className="text-[11px] text-gray-500 dark:text-slate-400 mt-0.5 leading-relaxed">
                  {editingId ? t('mail.folders.form.editHint') : t('mail.folders.form.createHint')}
                </p>
              </div>
            </div>
          </div>

          {formError && (
            <div className="rounded-xl border border-rose-200 dark:border-rose-900/40 bg-gradient-to-br from-rose-50 to-red-50 dark:from-rose-900/30 dark:to-red-900/20 p-3 text-rose-700 dark:text-rose-300 text-sm flex items-start gap-2">
              <AlertCircle className="w-4 h-4 flex-shrink-0 mt-0.5" />
              <span>{formError}</span>
            </div>
          )}

          <div>
            <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">
              {t('mail.common.ownerMailbox')} <span className="text-rose-500">*</span>
            </label>
            <select
              value={form.mailboxId ?? ''}
              onChange={(e) => setForm({ ...form, mailboxId: e.target.value ? Number(e.target.value) : null })}
              disabled={!!editingId}
              className={`${inputBase} disabled:opacity-50 disabled:cursor-not-allowed cursor-pointer`}
            >
              <option value="">{t('mail.folders.form.mailboxPlaceholder')}</option>
              {mailboxes.map((mb) => (
                <option key={mb.id} value={mb.id}>
                  {mb.name}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">
              {t('mail.folders.form.nameLabel')} <span className="text-rose-500">*</span>
            </label>
            <input
              type="text"
              required
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              maxLength={100}
              placeholder={t('mail.folders.form.namePlaceholder')}
              className={inputBase}
            />
            <p className="text-[11px] text-gray-400 dark:text-slate-500 mt-1">{t('mail.folders.form.nameHint')}</p>
          </div>

          <div>
            <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.folders.form.typeLabel')}</label>
            {!editingId ? (
              <div className="px-3.5 py-3 bg-gradient-to-br from-fuchsia-50 to-pink-50 dark:from-fuchsia-900/20 dark:to-pink-900/20 border border-fuchsia-200 dark:border-fuchsia-900/40 rounded-xl text-sm flex items-center gap-2 ring-1 ring-fuchsia-500/20">
                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-gradient-to-br from-fuchsia-100 to-pink-100 dark:from-fuchsia-900/50 dark:to-pink-900/50 text-fuchsia-700 dark:text-fuchsia-300 text-[10px] font-bold uppercase tracking-wider ring-1 ring-fuchsia-500/20">
                  <Sparkles className="w-2.5 h-2.5" />
                  {t('mail.common.custom')}
                </span>
                <span className="text-gray-600 dark:text-slate-300 font-medium">{t('mail.folders.type.custom')}</span>
              </div>
            ) : (
              <div className={`px-3.5 py-3 ${getFolderPalette(form.type).bg} border border-gray-200 dark:border-slate-700 rounded-xl text-sm flex items-center gap-2 ring-1 ${getFolderPalette(form.type).ring}`}>
                <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider ring-1 ${getFolderPalette(form.type).bg} ${getFolderPalette(form.type).text} ${getFolderPalette(form.type).ring}`}>
                  <span className={`w-1.5 h-1.5 rounded-full ${getFolderPalette(form.type).bar}`} />
                  {getFolderTypeLabel(t, form.type)}
                </span>
                <span className="text-gray-600 dark:text-slate-300 font-medium">
                  {isSystemFolder(form.type) ? t('mail.folders.type.systemLocked') : t('mail.folders.type.custom')}
                </span>
              </div>
            )}
            <p className="text-[11px] text-gray-400 dark:text-slate-500 mt-1 flex items-center gap-1">
              <Info className="w-3 h-3" />
              {editingId ? t('mail.folders.form.typeLockedHint') : t('mail.folders.form.typeCreateHint')}
            </p>
          </div>
        </form>
      </Modal>

      {/* 删除确认 */}
      <ConfirmDialog
        open={!!deleteTarget}
        onCancel={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        title={t('mail.folders.delete.title')}
        message={t('mail.folders.delete.message', { name: deleteTarget?.name ?? '', count: deleteTarget?.totalCount ?? 0 })}
        confirmText={t('common.delete')}
        cancelText={t('common.cancel')}
        variant="danger"
        loading={deleting}
      />

      {/* 批量删除确认 */}
      <ConfirmDialog
        open={batchDeleteOpen}
        onCancel={() => setBatchDeleteOpen(false)}
        onConfirm={handleBatchDelete}
        title={t('mail.folders.delete.batchTitle')}
        message={t('mail.folders.delete.batchMessage', { count: customSelectedCount })}
        confirmText={t('mail.folders.delete.batchConfirm', { count: customSelectedCount })}
        cancelText={t('common.cancel')}
        variant="danger"
        loading={batchDeleting}
      />

      {/* Toast */}
      {toast && (
        <div className="fixed top-4 right-4 z-50 animate-in fade-in slide-in-from-top-2 duration-300">
          <div
            className={`inline-flex items-center gap-2.5 px-4 py-2.5 rounded-xl shadow-lg text-white text-sm font-bold ring-1 ${
              toast.type === 'success'
                ? 'bg-gradient-to-br from-emerald-600 to-teal-600 shadow-emerald-500/20 ring-emerald-500/30'
                : 'bg-gradient-to-br from-rose-600 to-red-600 shadow-rose-500/20 ring-rose-500/30'
            }`}
          >
            {toast.type === 'success' ? (
              <CheckCircle2 className="w-4 h-4" strokeWidth={2.5} />
            ) : (
              <AlertCircle className="w-4 h-4" strokeWidth={2.5} />
            )}
            {toast.text}
          </div>
        </div>
      )}
    </div>
  );
}
