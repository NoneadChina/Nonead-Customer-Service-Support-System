import { useEffect, useState, useMemo } from 'react';
import { cannedResponseApi, mailboxApi } from '../api';
import { useI18n } from '../context/I18nContext';
import { useCrudPage } from '../hooks/useCrudPage';
import type { TranslationKey } from '../locales/zh-Hans';
import type { CannedResponseDto, MailboxResponse } from '../api/types';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import CannedResponseTrashModal from '../components/CannedResponseTrashModal';
import { formatDateTime, sanitizeHtml } from '../utils/format';
import { getApiErrorMessage } from '../utils/error';
import {
  FileText,
  CheckCircle2,
  XCircle,
  Plus,
  Search,
  Edit2,
  Trash2,
  ChevronRight,
  Tag,
  Copy,
  AlertCircle,
  Eye,
  X,
  Columns3,
  Code2,
  MonitorPlay,
  Zap,
  Shield,
  Clock,
  RefreshCw,
  Mail,
  Users,
  Filter,
  Inbox,
  Share2,
  User2,
  Layers,
  MessageSquare,
} from 'lucide-react';

interface CannedResponseQuery {
  mailboxId: number | null;
}

// 通用按钮样式
const btnPrimary =
  'inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold text-white rounded-xl bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 shadow-sm shadow-primary-500/20 hover:shadow hover:shadow-primary-500/30 transition-all disabled:opacity-50 ring-1 ring-primary-500/20';
const btnSecondary =
  'inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold text-gray-700 dark:text-slate-200 rounded-xl bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 hover:bg-gray-50 dark:hover:bg-slate-700/60 transition-all disabled:opacity-50 shadow-sm';

// 通用输入框样式
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

// 通用：加载态
function LoadingState({ text }: { text?: string }) {
  const { t } = useI18n();
  return (
    <div className="flex items-center justify-center gap-3 py-12">
      <div className="w-9 h-9 border-4 border-primary-200 dark:border-primary-900 border-t-primary-600 dark:border-t-primary-400 rounded-full animate-spin shadow-sm" />
      <span className="text-sm font-medium text-gray-500 dark:text-slate-400">{text ?? t('common.shell.loading')}</span>
    </div>
  );
}

// 概览统计 -> 配色（与通知模版一致）
const statPalette = [
  {
    from: 'from-primary-500',
    to: 'to-indigo-500',
    textLight: 'text-primary-600 dark:text-primary-400',
    bgSoft: 'bg-primary-50 dark:bg-primary-900/30',
    ringSoft: 'ring-primary-500/20',
  },
  {
    from: 'from-emerald-500',
    to: 'to-teal-500',
    textLight: 'text-emerald-600 dark:text-emerald-400',
    bgSoft: 'bg-emerald-50 dark:bg-emerald-900/30',
    ringSoft: 'ring-emerald-500/20',
  },
  {
    from: 'from-amber-500',
    to: 'to-orange-500',
    textLight: 'text-amber-600 dark:text-amber-400',
    bgSoft: 'bg-amber-50 dark:bg-amber-900/30',
    ringSoft: 'ring-amber-500/20',
  },
  {
    from: 'from-violet-500',
    to: 'to-fuchsia-500',
    textLight: 'text-violet-600 dark:text-violet-400',
    bgSoft: 'bg-violet-50 dark:bg-violet-900/30',
    ringSoft: 'ring-violet-500/20',
  },
];

// 分类 -> 渐变色板（基于哈希，保证同一分类始终相同）
function hashString(s: string): number {
  let h = 0;
  for (let i = 0; i < s.length; i++) {
    h = (h * 31 + s.charCodeAt(i)) | 0;
  }
  return Math.abs(h);
}
const categoryPalettes = [
  { from: 'from-sky-500', to: 'to-blue-500', accent: 'bg-sky-500', bar: 'bg-sky-500', text: 'text-sky-700 dark:text-sky-400', bg: 'bg-sky-50 dark:bg-sky-900/25', ring: 'ring-sky-500/20', ringSoft: 'ring-sky-500/20' },
  { from: 'from-emerald-500', to: 'to-teal-500', accent: 'bg-emerald-500', bar: 'bg-emerald-500', text: 'text-emerald-700 dark:text-emerald-400', bg: 'bg-emerald-50 dark:bg-emerald-900/25', ring: 'ring-emerald-500/20', ringSoft: 'ring-emerald-500/20' },
  { from: 'from-violet-500', to: 'to-indigo-500', accent: 'bg-violet-500', bar: 'bg-violet-500', text: 'text-violet-700 dark:text-violet-400', bg: 'bg-violet-50 dark:bg-violet-900/25', ring: 'ring-violet-500/20', ringSoft: 'ring-violet-500/20' },
  { from: 'from-rose-500', to: 'to-pink-500', accent: 'bg-rose-500', bar: 'bg-rose-500', text: 'text-rose-700 dark:text-rose-400', bg: 'bg-rose-50 dark:bg-rose-900/25', ring: 'ring-rose-500/20', ringSoft: 'ring-rose-500/20' },
  { from: 'from-amber-500', to: 'to-orange-500', accent: 'bg-amber-500', bar: 'bg-amber-500', text: 'text-amber-700 dark:text-amber-400', bg: 'bg-amber-50 dark:bg-amber-900/25', ring: 'ring-amber-500/20', ringSoft: 'ring-amber-500/20' },
  { from: 'from-fuchsia-500', to: 'to-purple-500', accent: 'bg-fuchsia-500', bar: 'bg-fuchsia-500', text: 'text-fuchsia-700 dark:text-fuchsia-400', bg: 'bg-fuchsia-50 dark:bg-fuchsia-900/25', ring: 'ring-fuchsia-500/20', ringSoft: 'ring-fuchsia-500/20' },
  { from: 'from-cyan-500', to: 'to-sky-500', accent: 'bg-cyan-500', bar: 'bg-cyan-500', text: 'text-cyan-700 dark:text-cyan-400', bg: 'bg-cyan-50 dark:bg-cyan-900/25', ring: 'ring-cyan-500/20', ringSoft: 'ring-cyan-500/20' },
];
function pickPalette(key: string) {
  if (!key) return { from: 'from-gray-400', to: 'to-gray-500', accent: 'bg-gray-400', bar: 'bg-gray-400', text: 'text-gray-600 dark:text-gray-400', bg: 'bg-gray-50 dark:bg-slate-700/40', ring: 'ring-gray-500/10', ringSoft: 'ring-gray-500/10' };
  return categoryPalettes[hashString(key) % categoryPalettes.length];
}

/** 概览统计卡：`labelKey` / `hintKey` 只持真源 key，渲染处必须经 `t()` */
type StatItem = {
  labelKey: TranslationKey;
  value: number;
  icon: React.ComponentType<{ className?: string; strokeWidth?: string | number }>;
  hintKey: TranslationKey;
  palette: (typeof statPalette)[number];
};

/**
 * 左侧分类导航项：**文案项**（"全部模板" / "未分类"）带 `labelKey`，
 * **数据项**（用户自建分类名，来自 `CannedResponseDto.category`）带 `label`。
 * 分类名是用户数据、不进字典，因此两者用可辨识联合表达，渲染处按
 * `'labelKey' in cat` 收窄——避免出现"两个字段都可缺"这种类型上不可达的状态。
 */
type CategoryNavItem = {
  key: string;
  icon: React.ComponentType<{ className?: string; strokeWidth?: string | number }>;
  count: number;
  palette: ReturnType<typeof pickPalette>;
} & ({ labelKey: TranslationKey } | { label: string });

export default function CannedResponses() {
  const { t } = useI18n();

  // 辅助数据
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [categories, setCategories] = useState<string[]>([]);

  // 客户端过滤（不触发服务端请求）
  const [searchInput, setSearchInput] = useState('');
  const [activeCategory, setActiveCategory] = useState<string>('all');

  // 编辑表单
  const [editingId, setEditingId] = useState<number | null>(null);
  const [formName, setFormName] = useState('');
  const [formBody, setFormBody] = useState('');
  const [formCategory, setFormCategory] = useState('');
  const [formMailboxId, setFormMailboxId] = useState<number | null>(null);
  const [formSortOrder, setFormSortOrder] = useState(0);
  const [formIsShared, setFormIsShared] = useState(true);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState('');

  // 预览
  const [previewId, setPreviewId] = useState<number | null>(null);

  // 删除
  const [deleting, setDeleting] = useState(false);

  // 垃圾箱操作目标
  const [restoreTarget, setRestoreTarget] = useState<CannedResponseDto | null>(null);
  const [hardDeleteTarget, setHardDeleteTarget] = useState<CannedResponseDto | null>(null);
  const [clearTrashOpen, setClearTrashOpen] = useState(false);

  const crud = useCrudPage<CannedResponseDto, CannedResponseQuery>({
    defaultQuery: { mailboxId: null },
    fetchList: async (q) => {
      const [list, cats] = await Promise.all([
        cannedResponseApi.list(q.mailboxId),
        cannedResponseApi.categories(q.mailboxId),
      ]);
      setCategories(cats ?? []);
      const arr = Array.isArray(list) ? list : [];
      return { data: arr, totalItems: arr.length, totalPages: 1, currentPage: 1 };
    },
    fetchTrashList: async (q) => {
      const res = await cannedResponseApi.getTrash(q.page, q.pageSize, q.keyword);
      return { data: res.data ?? [], totalItems: res.totalItems ?? 0, totalPages: res.totalPages ?? 0, currentPage: q.page };
    },
    fetchTrashCount: cannedResponseApi.getTrashCount,
  });

  const { toast } = crud;

  // 加载邮箱列表
  useEffect(() => {
    let cancelled = false;
    mailboxApi
      .listAll()
      .then((res: MailboxResponse[]) => { if (!cancelled) setMailboxes(Array.isArray(res) ? res : []); })
      .catch((err: unknown) => { if (!cancelled) console.error('加载邮箱列表失败:', err); });
    return () => { cancelled = true; };
  }, []);

  // 全量数据（来自 hook）
  const items = useMemo(() => crud.list?.data ?? [], [crud.list?.data]);

  const stats = useMemo(() => ({
    total: items.length,
    shared: items.filter((i) => i.isShared).length,
    personal: items.filter((i) => !i.isShared).length,
    categorized: categories.length,
  }), [items, categories]);

  const statItems = useMemo((): StatItem[] => {
    return [
      { labelKey: 'mail.canned.stats.total', value: stats.total, icon: FileText, palette: statPalette[0], hintKey: 'mail.canned.stats.totalHint' },
      { labelKey: 'mail.canned.stats.shared', value: stats.shared, icon: Share2, palette: statPalette[1], hintKey: 'mail.canned.stats.sharedHint' },
      { labelKey: 'mail.canned.stats.personal', value: stats.personal, icon: User2, palette: statPalette[2], hintKey: 'mail.canned.stats.personalHint' },
      { labelKey: 'mail.canned.stats.categories', value: stats.categorized, icon: Layers, palette: statPalette[3], hintKey: 'mail.canned.stats.categoriesHint' },
    ];
  }, [stats]);

  // 客户端过滤：按分类 + 搜索关键字
  const filteredItems = useMemo(() => {
    let list = items;
    if (activeCategory === 'none') {
      list = list.filter((i) => !i.category);
    } else if (activeCategory !== 'all') {
      list = list.filter((i) => i.category === activeCategory);
    }
    if (searchInput.trim()) {
      const kw = searchInput.toLowerCase();
      list = list.filter(
        (i) =>
          i.name.toLowerCase().includes(kw) ||
          i.body.toLowerCase().includes(kw) ||
          (i.category && i.category.toLowerCase().includes(kw)),
      );
    }
    return list;
  }, [items, activeCategory, searchInput]);

  // 按分组组织（"未分类"用空串作哨兵键：分组头文案在渲染处经 t() 解析，
  // 调色板哈希输入因此与界面语言无关）
  const groupedItems = useMemo(() => {
    const groups: Record<string, CannedResponseDto[]> = {};
    filteredItems.forEach((item) => {
      const key = item.category || '';
      if (!groups[key]) groups[key] = [];
      groups[key].push(item);
    });
    return groups;
  }, [filteredItems]);

  const openTrash = () => {
    crud.trash.setOpen(true);
    crud.trash.setQuery({ page: 1, pageSize: 10, keyword: '' });
    crud.trash.refreshCount();
  };

  const handleRestore = async () => {
    if (!restoreTarget) return;
    crud.trash.setActionLoading(true);
    try {
      await cannedResponseApi.restore(restoreTarget.id);
      toast.show({ type: 'success', text: t('mail.canned.toast.restored', { name: restoreTarget.name }) });
      setRestoreTarget(null);
      crud.trash.refresh();
      crud.trash.refreshCount();
      crud.refresh();
    } catch (e: unknown) {
      toast.show({ type: 'error', text: getApiErrorMessage(e, t('mail.common.restoreFailed')) });
    } finally {
      crud.trash.setActionLoading(false);
    }
  };

  const handleHardDelete = async () => {
    if (!hardDeleteTarget) return;
    crud.trash.setActionLoading(true);
    try {
      await cannedResponseApi.hardDelete(hardDeleteTarget.id);
      toast.show({ type: 'success', text: t('mail.canned.toast.hardDeleted', { name: hardDeleteTarget.name }) });
      setHardDeleteTarget(null);
      crud.trash.refresh();
      crud.trash.refreshCount();
    } catch (e: unknown) {
      toast.show({ type: 'error', text: getApiErrorMessage(e, t('mail.common.hardDeleteFailed')) });
    } finally {
      crud.trash.setActionLoading(false);
    }
  };

  const handleClearTrash = async () => {
    crud.trash.setActionLoading(true);
    try {
      const res: { count?: number } = await cannedResponseApi.clearTrash();
      const count = res?.count ?? 0;
      toast.show({ type: 'success', text: t('mail.common.trashCleared', { count }) });
      setClearTrashOpen(false);
      crud.trash.refresh();
      crud.trash.refreshCount();
    } catch (e: unknown) {
      toast.show({ type: 'error', text: getApiErrorMessage(e, t('mail.common.clearTrashFailed')) });
    } finally {
      crud.trash.setActionLoading(false);
    }
  };

  const openCreate = () => {
    setEditingId(null);
    setFormName('');
    setFormBody('');
    setFormCategory('');
    setFormMailboxId(crud.query.mailboxId);
    setFormSortOrder(0);
    setFormIsShared(true);
    setFormError('');
    crud.openEditor();
  };

  const openEdit = (item: CannedResponseDto) => {
    setEditingId(item.id);
    setFormName(item.name);
    setFormBody(item.body);
    setFormCategory(item.category || '');
    setFormMailboxId(item.mailboxId);
    setFormSortOrder(item.sortOrder);
    setFormIsShared(item.isShared);
    setFormError('');
    crud.openEditor(item);
  };

  const handleSave = async () => {
    if (!formName.trim()) {
      setFormError(t('mail.canned.editor.errorNameRequired'));
      return;
    }
    if (!formBody.trim()) {
      setFormError(t('mail.canned.editor.errorBodyRequired'));
      return;
    }
    setSaving(true);
    setFormError('');
    try {
      const payload = {
        name: formName.trim(),
        body: formBody,
        category: formCategory.trim() || null,
        mailboxId: formMailboxId,
        sortOrder: formSortOrder,
        isShared: formIsShared,
      };
      if (editingId) {
        await cannedResponseApi.update(editingId, payload);
        toast.show({ type: 'success', text: t('mail.canned.toast.updated') });
      } else {
        await cannedResponseApi.create(payload);
        toast.show({ type: 'success', text: t('mail.canned.toast.created') });
      }
      crud.closeEditor();
      crud.refresh();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : t('mail.common.saveFailed'));
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    if (!crud.deleteTarget) return;
    setDeleting(true);
    try {
      await cannedResponseApi.remove(crud.deleteTarget.id);
      toast.show({ type: 'success', text: t('mail.canned.toast.movedToTrash') });
      crud.setDeleteTarget(null);
      crud.trash.refreshCount();
      crud.refresh();
    } catch (err: unknown) {
      toast.show({ type: 'error', text: err instanceof Error ? err.message : t('mail.common.deleteFailed') });
    } finally {
      setDeleting(false);
    }
  };

  const previewItem = items.find((i) => i.id === previewId);

  return (
    <div className="space-y-5 md:space-y-6 relative">
      {/* Toast */}
      {toast.message && (
        <div className="fixed top-6 right-6 z-[60] animate-in slide-in-from-top-6 fade-in duration-300">
          <div
            className={`inline-flex items-center gap-2.5 pl-3 pr-4 py-3 rounded-2xl shadow-2xl text-sm font-semibold border backdrop-blur-md ${
              toast.message.type === 'success'
                ? 'bg-gradient-to-br from-emerald-500/95 to-teal-500/95 text-white border-emerald-400/40 shadow-emerald-500/25'
                : 'bg-gradient-to-br from-red-500/95 to-rose-500/95 text-white border-red-400/40 shadow-red-500/25'
            }`}
          >
            <div className="w-7 h-7 rounded-xl flex items-center justify-center bg-white/25">
              {toast.message.type === 'success' ? <CheckCircle2 className="w-4 h-4" strokeWidth={2.4} /> : <XCircle className="w-4 h-4" strokeWidth={2.4} />}
            </div>
            <span className="pr-0.5">{toast.message.text}</span>
          </div>
        </div>
      )}

      {/* 页头（渐变） */}
      <div className="relative overflow-hidden rounded-3xl border border-gray-200 dark:border-slate-700/60 bg-gradient-to-br from-primary-50 via-white to-indigo-50 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-900/20 shadow-sm">
        {/* 装饰光斑 */}
        <div className="pointer-events-none absolute -top-24 -right-24 w-72 h-72 rounded-full bg-primary-500/20 blur-3xl" />
        <div className="pointer-events-none absolute top-16 -left-16 w-60 h-60 rounded-full bg-indigo-500/15 blur-3xl" />

        <div className="relative p-6 md:p-7">
          <div className="flex items-start justify-between gap-4 flex-wrap mb-6">
            <div className="flex items-start gap-4">
              <div className="shrink-0 w-14 h-14 rounded-2xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-lg shadow-primary-500/30 flex items-center justify-center ring-1 ring-white/40 dark:ring-white/10">
                <MessageSquare className="w-7 h-7" strokeWidth={2} />
              </div>
              <div className="min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <h1 className="text-2xl md:text-3xl font-extrabold tracking-tight text-gray-900 dark:text-slate-50">{t('mail.canned.page.title')}</h1>
                  <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider rounded-full bg-gradient-to-r from-emerald-100 to-teal-100 dark:from-emerald-900/40 dark:to-teal-900/40 text-emerald-700 dark:text-emerald-300 ring-1 ring-emerald-500/20">
                    <Zap className="w-3 h-3" />
                    {t('mail.canned.page.badge')}
                  </span>
                </div>
                <p className="text-sm md:text-base text-gray-500 dark:text-slate-400 mt-1.5 leading-relaxed">
                  {t('mail.canned.page.subtitle')}
                </p>
              </div>
            </div>
            <div className="flex items-center gap-2.5 flex-wrap">
              <div className="relative">
                <Mail className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
                <select
                  value={crud.query.mailboxId ?? ''}
                  onChange={(e) => crud.handleFilterChange('mailboxId', e.target.value === '' ? null : Number(e.target.value))}
                  className="pl-9 pr-9 py-2 text-sm bg-white dark:bg-slate-900/80 border border-gray-200 dark:border-slate-700 rounded-xl text-gray-700 dark:text-slate-200 shadow-sm focus:outline-none focus:ring-2 focus:ring-primary-500/30 appearance-none min-w-[140px] cursor-pointer transition-all"
                >
                  <option value="">{t('mail.canned.page.allMailboxesGlobal')}</option>
                  {mailboxes.map((m) => (
                    <option key={m.id} value={m.id}>{m.name}</option>
                  ))}
                </select>
                <ChevronRight className="w-3.5 h-3.5 absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 -rotate-90 pointer-events-none" />
              </div>
              <button
                onClick={openTrash}
                className="inline-flex items-center gap-1.5 px-4 py-2 text-sm bg-white dark:bg-slate-800/80 border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-200 rounded-xl shadow-sm hover:bg-gray-50 dark:hover:bg-slate-800 transition-all"
              >
                <Trash2 className="w-4 h-4" strokeWidth={2} />
                <span className="font-semibold">{t('mail.common.trash')}</span>
                {crud.trash.count > 0 && (
                  <span className="inline-flex items-center justify-center px-1.5 py-0.5 text-[10px] font-black rounded-full bg-gradient-to-br from-red-500 to-rose-500 text-white shadow-sm shadow-red-500/30 min-w-[20px]">
                    {crud.trash.count}
                  </span>
                )}
              </button>
              <button
                onClick={openCreate}
                className={btnPrimary}
              >
                <Plus className="w-4 h-4" strokeWidth={2.4} />
                <span>{t('mail.canned.page.newCannedResponse')}</span>
              </button>
            </div>
          </div>

          {/* 统计卡片 */}
          <div className="grid grid-cols-2 md:grid-cols-4 gap-3 md:gap-4 relative">
            {statItems.map((s, idx) => (
              <div
                key={s.labelKey}
                className={`relative rounded-2xl overflow-hidden border border-gray-100 dark:border-slate-700/60 bg-white dark:bg-slate-800/70 p-4 md:p-5 shadow-sm hover:shadow-md transition-all`}
              >
                {/* 顶部渐变细条 */}
                <div className={`absolute top-0 left-0 right-0 h-1 bg-gradient-to-r ${s.palette.from} ${s.palette.to}`} />
                <div className="flex items-start gap-3.5 md:gap-4">
                  <div className={`shrink-0 w-11 h-11 md:w-12 md:h-12 rounded-2xl ${s.palette.bgSoft} flex items-center justify-center ring-1 ${s.palette.ringSoft} shadow-sm`}>
                    <s.icon className={`w-5 h-5 md:w-[22px] md:h-[22px] ${s.palette.textLight}`} strokeWidth={2} />
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className="text-[11px] md:text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1">{t(s.labelKey)}</p>
                    <p className="text-xl md:text-2xl font-black text-gray-900 dark:text-slate-50 tabular-nums leading-none mb-1.5">
                      {s.value}
                    </p>
                    <p className="text-[10px] md:text-[11px] text-gray-400 dark:text-slate-500 leading-snug">{t(s.hintKey)}</p>
                  </div>
                </div>
                {/* 序号徽章 */}
                <div className={`absolute bottom-3 right-3 w-7 h-7 rounded-xl bg-gradient-to-br ${s.palette.from} ${s.palette.to} text-white flex items-center justify-center text-[11px] font-black shadow-sm ring-1 ring-white/40 dark:ring-white/10 opacity-80`}>
                  {String(idx + 1).padStart(2, '0')}
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* 主工作区：左侧分类栏 + 右侧模板列表 */}
      <div className="flex gap-4 md:gap-5 items-start flex-col lg:flex-row">
        {/* 左侧分类栏 */}
        <div className="w-full lg:w-64 flex-shrink-0">
          <div className="bg-white dark:bg-slate-800/70 rounded-2xl border border-gray-200 dark:border-slate-700/60 overflow-hidden shadow-sm">
            <div className="p-3.5 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-primary-50/60 via-white to-indigo-50/60 dark:from-slate-900/50 dark:via-slate-800/60 dark:to-indigo-900/20">
              <div className="flex items-center gap-2 mb-2.5">
                <div className="p-1.5 rounded-lg bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-sm">
                  <Tag className="w-3.5 h-3.5" />
                </div>
                <h3 className="text-sm font-extrabold text-gray-800 dark:text-slate-100 tracking-tight">{t('mail.canned.category.navTitle')}</h3>
              </div>
              <div className="relative">
                <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
                <input
                  type="text"
                  placeholder={t('mail.canned.category.searchPlaceholder')}
                  value={searchInput}
                  onChange={(e) => setSearchInput(e.target.value)}
                  className={inputBase + ' pl-10 !py-2'}
                />
              </div>
            </div>
            <div className="p-2 space-y-0.5 max-h-[calc(100vh-340px)] overflow-y-auto">
              {([
                { key: 'all', labelKey: 'mail.canned.category.all', icon: Inbox, count: items.length, palette: categoryPalettes[0] },
              ] as CategoryNavItem[]).concat(
                categories.map((cat): CategoryNavItem => {
                  const c = items.filter((i) => i.category === cat).length;
                  return { key: cat, label: cat, icon: Tag, count: c, palette: pickPalette(cat) };
                }),
              ).concat([
                { key: 'none', labelKey: 'mail.canned.category.uncategorized', icon: Layers, count: items.filter((i) => !i.category).length, palette: pickPalette('') },
              ]).map((cat) => {
                const active = activeCategory === cat.key;
                return (
                  <button
                    key={cat.key}
                    onClick={() => setActiveCategory(cat.key)}
                    className={`w-full text-left px-3 py-2.5 text-sm rounded-xl flex items-center justify-between gap-2 transition-all group border-l-2 ${
                      active
                        ? `${cat.palette.bg} ${cat.palette.text} font-bold border-l-transparent shadow-sm ring-1 ${cat.palette.ringSoft}`
                        : 'text-gray-600 dark:text-slate-400 hover:bg-gray-50 dark:hover:bg-slate-700/40 border-l-transparent'
                    }`}
                  >
                    <span className="flex items-center gap-2.5 min-w-0">
                      <div className={`p-1.5 rounded-lg shrink-0 transition ${active ? `bg-gradient-to-br ${cat.palette.from} ${cat.palette.to} text-white shadow-sm` : `${cat.palette.bg} ${cat.palette.text} ring-1 ${cat.palette.ringSoft}`}`}>
                        <cat.icon className="w-3.5 h-3.5" strokeWidth={2} />
                      </div>
                      <span className="truncate font-medium">{'labelKey' in cat ? t(cat.labelKey) : cat.label}</span>
                    </span>
                    <span className={`text-[11px] px-2 py-0.5 rounded-lg font-black flex-shrink-0 ${
                      active
                        ? `bg-gradient-to-br ${cat.palette.from} ${cat.palette.to} text-white shadow-sm`
                        : 'bg-gray-100 dark:bg-slate-700/50 text-gray-500 dark:text-slate-400'
                    }`}>{cat.count}</span>
                  </button>
                );
              })}
            </div>
          </div>
        </div>

        {/* 右侧模板列表 */}
        <div className="flex-1 min-w-0 w-full">
          {crud.loading ? (
            <LoadingState />
          ) : filteredItems.length === 0 ? (
            <div className="rounded-3xl border border-dashed border-gray-200 dark:border-slate-700 bg-white/60 dark:bg-slate-800/40 shadow-sm">
              <EmptyState
                icon={FileText}
                title={searchInput ? t('mail.canned.list.noMatch') : t('mail.canned.list.empty')}
                subtitle={searchInput ? t('mail.canned.list.noMatchHint') : t('mail.canned.list.emptyHint')}
                action={
                  !searchInput && (
                    <button onClick={openCreate} className={btnPrimary}>
                      <Plus className="w-4 h-4" />
                      {t('mail.canned.list.createFirst')}
                    </button>
                  )
                }
              />
            </div>
          ) : (
            <div className="space-y-5">
              {/* 筛选摘要 */}
              {(activeCategory !== 'all' || searchInput.trim()) && (
                <div className="rounded-2xl bg-white dark:bg-slate-800/70 border border-gray-200 dark:border-slate-700/60 shadow-sm p-3.5 flex flex-wrap items-center gap-2">
                  <span className="inline-flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mr-1">
                    <Filter className="w-3.5 h-3.5" />
                    {t('mail.common.currentFilters')}
                  </span>
                  {activeCategory !== 'all' && (
                    <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-xs font-bold ${pickPalette(activeCategory).bg} ${pickPalette(activeCategory).text} ring-1 ${pickPalette(activeCategory).ringSoft}`}>
                      <Tag className="w-3 h-3" />
                      {activeCategory === 'none' ? t('mail.canned.category.uncategorized') : activeCategory}
                      <button
                        onClick={() => setActiveCategory('all')}
                        className="ml-1 opacity-80 hover:opacity-100"
                      >
                        <X className="w-3 h-3" />
                      </button>
                    </span>
                  )}
                  {searchInput.trim() && (
                    <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-xs font-bold bg-primary-50 dark:bg-primary-900/30 text-primary-700 dark:text-primary-300 ring-1 ring-primary-500/20">
                      <Search className="w-3 h-3" />
                      "{searchInput}"
                      <button
                        onClick={() => setSearchInput('')}
                        className="ml-1 opacity-80 hover:opacity-100"
                      >
                        <X className="w-3 h-3" />
                      </button>
                    </span>
                  )}
                  <div className="flex-1" />
                  <button
                    onClick={() => { setActiveCategory('all'); setSearchInput(''); }}
                    className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-xs font-bold text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700/50 transition"
                  >
                    <RefreshCw className="w-3 h-3" />
                    {t('mail.canned.list.resetAll')}
                  </button>
                </div>
              )}

              {Object.entries(groupedItems).map(([groupName, groupItems]) => {
                const palette = pickPalette(groupName);
                return (
                  <div key={groupName} className="bg-white dark:bg-slate-800/70 rounded-2xl border border-gray-200 dark:border-slate-700/60 overflow-hidden shadow-sm hover:shadow-md transition-all">
                    <div className={`px-4 md:px-5 py-3 md:py-3.5 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50/70 via-white to-gray-50/70 dark:from-slate-900/50 dark:via-slate-800/60 dark:to-slate-900/50 flex items-center justify-between flex-wrap gap-3 relative overflow-hidden`}>
                      {/* 左侧分类彩条 + 渐变装饰 */}
                      <div className={`absolute top-0 bottom-0 left-0 w-1 bg-gradient-to-b ${palette.from} ${palette.to}`} />
                      <div className="flex items-center gap-3 flex-wrap pl-1">
                        <div className={`w-9 h-9 rounded-xl bg-gradient-to-br ${palette.from} ${palette.to} text-white shadow-sm flex items-center justify-center ring-1 ring-white/40 dark:ring-white/10 shrink-0`}>
                          <Tag className="w-4 h-4" strokeWidth={2} />
                        </div>
                        <div>
                          <h3 className="text-sm md:text-[15px] font-extrabold tracking-tight text-gray-800 dark:text-slate-100">{groupName}</h3>
                          <p className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5">{t('mail.canned.list.groupHint')}</p>
                        </div>
                      </div>
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className={`inline-flex items-center gap-1 px-3 py-1 rounded-xl text-[11px] font-black ${palette.bg} ${palette.text} ring-1 ${palette.ringSoft}`}>
                          <Copy className="w-3 h-3" />
                          {groupItems.length}{t('mail.canned.list.groupCountSuffix', { count: groupItems.length })}
                        </span>
                      </div>
                    </div>
                    <div className="divide-y divide-gray-50 dark:divide-slate-700/50">
                      {groupItems.map((item) => {
                        const catPalette = pickPalette(item.category || '');
                        return (
                          <div
                            key={item.id}
                            className="p-4 md:p-5 hover:bg-gradient-to-r hover:from-gray-50/60 hover:via-white hover:to-gray-50/60 dark:hover:from-slate-800/60 dark:hover:via-slate-800/30 dark:hover:to-slate-800/60 transition-all cursor-pointer group relative"
                            onClick={() => setPreviewId(item.id)}
                          >
                            {/* 左侧小彩条 */}
                            <div className={`absolute left-0 top-4 bottom-4 w-1 rounded-r-full ${catPalette.bar} opacity-70 group-hover:opacity-100 transition`} />
                            <div className="flex items-start justify-between gap-4 pl-1 flex-col sm:flex-row sm:items-start">
                              <div className="flex-1 min-w-0 w-full sm:w-auto">
                                <div className="flex items-center gap-2 flex-wrap mb-2">
                                  <h4 className="font-bold text-gray-900 dark:text-slate-100 tracking-tight truncate md:max-w-[68%]">{item.name}</h4>
                                  {!item.isShared && (
                                    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider bg-gradient-to-br from-amber-50 to-orange-50 dark:from-amber-900/30 dark:to-orange-900/20 text-amber-700 dark:text-amber-400 ring-1 ring-amber-500/20">
                                      <User2 className="w-3 h-3" />
                                      {t('mail.canned.badge.personal')}
                                    </span>
                                  )}
                                  {item.isShared && (
                                    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider bg-gradient-to-br from-emerald-50 to-teal-50 dark:from-emerald-900/30 dark:to-teal-900/20 text-emerald-700 dark:text-emerald-400 ring-1 ring-emerald-500/20">
                                      <Share2 className="w-3 h-3" />
                                      {t('mail.canned.badge.shared')}
                                    </span>
                                  )}
                                  {item.mailboxName && (
                                    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider bg-gradient-to-br from-primary-50 to-indigo-50 dark:from-primary-900/30 dark:to-indigo-900/20 text-primary-700 dark:text-primary-300 ring-1 ring-primary-500/20">
                                      <Mail className="w-3 h-3" />
                                      {item.mailboxName}
                                    </span>
                                  )}
                                  {item.category && (
                                    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider ${catPalette.bg} ${catPalette.text} ring-1 ${catPalette.ringSoft}`}>
                                      <Layers className="w-3 h-3" />
                                      {item.category}
                                    </span>
                                  )}
                                </div>
                                <div
                                  className="text-sm text-gray-600 dark:text-slate-300 leading-relaxed line-clamp-2 [&_strong]:font-bold [&_a]:text-primary-600 dark:[&_a]:text-primary-400 [&_a]:font-semibold [&_ul]:list-disc [&_ul]:pl-5 [&_ol]:list-decimal [&_ol]:pl-5"
                                  dangerouslySetInnerHTML={{ __html: sanitizeHtml(item.body) }}
                                />
                                <div className="mt-3 text-[11px] text-gray-400 dark:text-slate-500 flex items-center gap-x-4 gap-y-1.5 flex-wrap">
                                  <span className="inline-flex items-center gap-1">
                                    <Clock className="w-3 h-3 opacity-70" />
                                    {t('mail.common.updatedAtPrefix')} {formatDateTime(item.updatedAt)}
                                  </span>
                                  {item.createdByUserName && (
                                    <span className="inline-flex items-center gap-1">
                                      <User2 className="w-3 h-3 opacity-70" />
                                      {item.createdByUserName}
                                    </span>
                                  )}
                                  {typeof item.sortOrder === 'number' && (
                                    <span className="inline-flex items-center gap-1">
                                      <Shield className="w-3 h-3 opacity-70" />
                                      {t('mail.common.trashSortOrderPrefix')}{item.sortOrder}
                                    </span>
                                  )}
                                </div>
                              </div>
                              <div className="flex items-center gap-1.5 flex-shrink-0 -mt-1 self-end sm:self-auto">
                                <button
                                  onClick={(e) => { e.stopPropagation(); setPreviewId(item.id); }}
                                  className="p-2.5 text-gray-400 dark:text-slate-500 hover:text-primary-600 dark:hover:text-primary-400 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition-all group/btn"
                                  title={t('mail.common.preview')}
                                >
                                  <Eye className="w-[18px] h-[18px]" strokeWidth={2} />
                                </button>
                                <button
                                  onClick={(e) => { e.stopPropagation(); openEdit(item); }}
                                  className="p-2.5 text-gray-400 dark:text-slate-500 hover:text-primary-600 dark:hover:text-primary-400 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition-all"
                                  title={t('common.edit')}
                                >
                                  <Edit2 className="w-[18px] h-[18px]" strokeWidth={2} />
                                </button>
                                <button
                                  onClick={(e) => { e.stopPropagation(); crud.setDeleteTarget(item); }}
                                  className="p-2.5 text-gray-400 dark:text-slate-500 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-xl transition-all"
                                  title={t('mail.canned.list.moveToTrash')}
                                >
                                  <Trash2 className="w-[18px] h-[18px]" strokeWidth={2} />
                                </button>
                              </div>
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* 编辑/新建 Modal */}
      <Modal
        open={crud.showEditor}
        onClose={() => !saving && crud.closeEditor()}
        title={editingId ? t('mail.canned.editor.titleEdit') : t('mail.canned.editor.titleCreate')}
        size="xl"
      >
        <div className="space-y-4">
          {/* 头部说明卡片 */}
          <div className="relative rounded-2xl bg-gradient-to-br from-primary-50/70 via-white to-indigo-50/70 dark:from-slate-900 dark:via-slate-800/60 dark:to-indigo-900/20 border border-primary-100 dark:border-slate-700/60 p-4 md:p-5 overflow-hidden">
            <div className="pointer-events-none absolute -top-16 -right-16 w-44 h-44 rounded-full bg-primary-500/15 blur-3xl" />
            <div className="relative flex items-start gap-4">
              <div className="shrink-0 w-11 h-11 md:w-12 md:h-12 rounded-2xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-md shadow-primary-500/30 flex items-center justify-center ring-1 ring-white/40 dark:ring-white/10">
                <MessageSquare className="w-5 h-5 md:w-5.5 md:h-5.5" strokeWidth={2} />
              </div>
              <div className="flex-1 min-w-0">
                <h4 className="text-sm md:text-base font-extrabold tracking-tight text-gray-900 dark:text-slate-50 flex items-center gap-2 flex-wrap">
                  {editingId ? t('mail.canned.editor.headingEdit') : t('mail.canned.editor.headingCreate')}
                  <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider rounded-full bg-gradient-to-r from-emerald-100 to-teal-100 dark:from-emerald-900/40 dark:to-teal-900/40 text-emerald-700 dark:text-emerald-300 ring-1 ring-emerald-500/20">
                    <CheckCircle2 className="w-3 h-3" />
                    {t('mail.canned.editor.htmlRichText')}
                  </span>
                </h4>
                <p className="text-[11px] md:text-xs text-gray-500 dark:text-slate-400 mt-1 leading-relaxed">
                  {t('mail.canned.editor.headingHint')}
                </p>
              </div>
            </div>
          </div>

          {formError && (
            <div className="p-4 rounded-2xl bg-gradient-to-br from-red-50 to-rose-50 dark:from-red-900/30 dark:to-rose-900/20 border border-red-100 dark:border-red-900/40 text-red-700 dark:text-red-300 text-sm flex items-start gap-2.5 shadow-sm">
              <div className="p-1 rounded-lg bg-red-100 dark:bg-red-900/40 text-red-600 dark:text-red-400 shrink-0">
                <AlertCircle className="w-4 h-4" />
              </div>
              <div className="flex-1 pt-0.5 font-semibold">{formError}</div>
            </div>
          )}

          {/* 基础字段卡片 */}
          <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm overflow-hidden">
            <div className="px-4 md:px-5 py-3 md:py-3.5 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50/60 via-white to-gray-50/60 dark:from-slate-900/50 dark:via-slate-800/60 dark:to-slate-900/50 flex items-center justify-between flex-wrap gap-2">
              <div className="flex items-center gap-2">
                <div className="p-1.5 rounded-lg bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-sm">
                  <FileText className="w-3.5 h-3.5" />
                </div>
                <h3 className="text-sm font-extrabold text-gray-800 dark:text-slate-100 tracking-tight">{t('mail.canned.editor.sectionInfo')}</h3>
              </div>
              <div className="flex items-center gap-1 text-[11px] text-gray-500 dark:text-slate-400">
                <Users className="w-3 h-3" />
                <span>{t('mail.canned.editor.requiredHint')}</span>
              </div>
            </div>
            <div className="p-4 md:p-5 space-y-4 md:space-y-5">
              <div>
                <label className="block text-xs font-bold text-gray-600 dark:text-slate-400 uppercase tracking-wider mb-2">
                  {t('mail.canned.editor.nameLabel')} <span className="text-red-500 ml-0.5">*</span>
                </label>
                <input
                  type="text"
                  value={formName}
                  onChange={(e) => setFormName(e.target.value)}
                  placeholder={t('mail.canned.editor.namePlaceholder')}
                  className={inputBase}
                />
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 md:gap-5">
                <div>
                  <label className="block text-xs font-bold text-gray-600 dark:text-slate-400 uppercase tracking-wider mb-2">
                    {t('mail.canned.editor.categoryLabel')}
                  </label>
                  <div className="relative">
                    <Tag className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
                    <input
                      type="text"
                      value={formCategory}
                      onChange={(e) => setFormCategory(e.target.value)}
                      placeholder={t('mail.canned.editor.categoryPlaceholder')}
                      list="category-suggestions"
                      className={inputBase + ' pl-10'}
                    />
                    <datalist id="category-suggestions">
                      {categories.map((c) => (
                        <option key={c} value={c} />
                      ))}
                    </datalist>
                  </div>
                </div>
                <div>
                  <label className="block text-xs font-bold text-gray-600 dark:text-slate-400 uppercase tracking-wider mb-2">
                    {t('mail.common.ownerMailbox')}
                  </label>
                  <select
                    value={formMailboxId ?? ''}
                    onChange={(e) => setFormMailboxId(e.target.value === '' ? null : Number(e.target.value))}
                    className={inputBase}
                  >
                    <option value="">{t('mail.canned.editor.globalAllMailboxesAvailable')}</option>
                    {mailboxes.map((m) => (
                      <option key={m.id} value={m.id}>{m.name}</option>
                    ))}
                  </select>
                </div>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 md:gap-5 items-end">
                <div>
                  <label className="block text-xs font-bold text-gray-600 dark:text-slate-400 uppercase tracking-wider mb-2">
                    {t('mail.canned.editor.sortOrderLabel')}
                  </label>
                  <input
                    type="number"
                    value={formSortOrder}
                    onChange={(e) => setFormSortOrder(Number(e.target.value))}
                    className={inputBase}
                  />
                </div>
                {/* 共享大开关卡片 */}
                <div
                  className={`relative rounded-xl border shadow-sm overflow-hidden transition ${
                    formIsShared
                      ? 'bg-gradient-to-br from-emerald-50 via-white to-teal-50 dark:from-emerald-900/20 dark:via-slate-800 dark:to-teal-900/20 border-emerald-100 dark:border-emerald-800/40'
                      : 'bg-white dark:bg-slate-800/60 border-gray-200 dark:border-slate-700/60'
                  }`}
                >
                  <label className="flex items-center gap-3 md:gap-4 px-4 py-3.5 cursor-pointer select-none">
                    <div className="shrink-0 relative">
                      <input
                        type="checkbox"
                        checked={formIsShared}
                        onChange={(e) => setFormIsShared(e.target.checked)}
                        className="peer sr-only"
                      />
                      <div className={`w-14 h-8 rounded-full transition-all shadow-inner ring-1 ${
                        formIsShared
                          ? 'bg-gradient-to-br from-emerald-500 to-teal-500 ring-emerald-500/30'
                          : 'bg-gray-200 dark:bg-slate-700 ring-gray-500/10'
                      }`} />
                      <div className={`absolute top-0.5 left-0.5 w-7 h-7 rounded-full bg-white dark:bg-slate-100 shadow-md transition-all flex items-center justify-center ${
                        formIsShared ? 'translate-x-6' : 'translate-x-0'
                      }`}>
                        {formIsShared ? (
                          <CheckCircle2 className="w-4 h-4 text-emerald-500" strokeWidth={2.5} />
                        ) : (
                          <XCircle className="w-4 h-4 text-gray-400" strokeWidth={2} />
                        )}
                      </div>
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className={`text-sm font-extrabold ${formIsShared ? 'text-emerald-700 dark:text-emerald-400' : 'text-gray-700 dark:text-slate-200'}`}>
                          {formIsShared ? t('mail.canned.editor.sharedToggleOn') : t('mail.canned.editor.sharedToggleOff')}
                        </span>
                        {formIsShared && (
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-emerald-100 dark:bg-emerald-900/40 text-emerald-700 dark:text-emerald-300 text-[10px] font-bold uppercase tracking-wider ring-1 ring-emerald-500/20">
                            <span className="relative flex w-1.5 h-1.5">
                              <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75" />
                              <span className="relative inline-flex rounded-full h-1.5 w-1.5 bg-emerald-500" />
                            </span>
                            Live
                          </span>
                        )}
                      </div>
                      <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 leading-relaxed">
                        {formIsShared
                          ? t('mail.canned.editor.sharedToggleHint')
                          : t('mail.canned.editor.personalToggleHint')}
                      </p>
                    </div>
                  </label>
                </div>
              </div>
            </div>
          </div>

          {/* 内容编辑器（双栏：代码 + 预览） */}
          <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm overflow-hidden">
            <div className="px-4 md:px-5 py-3 md:py-3.5 border-b border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-slate-50/60 via-white to-slate-50/60 dark:from-slate-900/50 dark:via-slate-800/60 dark:to-slate-900/50 flex items-center justify-between flex-wrap gap-2">
              <div className="flex items-center gap-2">
                <div className="p-1.5 rounded-lg bg-gradient-to-br from-slate-700 to-slate-900 text-slate-100 shadow-sm">
                  <Columns3 className="w-3.5 h-3.5" />
                </div>
                <h3 className="text-sm font-extrabold text-gray-800 dark:text-slate-100 tracking-tight">
                  {t('mail.canned.editor.bodyLabel')} <span className="text-red-500 ml-0.5">*</span>
                </h3>
              </div>
              <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-bold text-primary-700 dark:text-primary-300 bg-primary-50 dark:bg-primary-900/30 ring-1 ring-primary-500/20">
                <Code2 className="w-3.5 h-3.5" />
                {t('mail.canned.editor.bodySupportsHtml')}
              </span>
            </div>
            <div className="p-4 md:p-5">
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 md:gap-5">
                {/* 左：HTML 编辑器 */}
                <div className="flex flex-col">
                  <div className="mb-2 flex items-center justify-between flex-wrap gap-2">
                    <div className="inline-flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider text-gray-600 dark:text-slate-400">
                      <div className="flex gap-1">
                        <span className="w-2.5 h-2.5 rounded-full bg-red-400" />
                        <span className="w-2.5 h-2.5 rounded-full bg-amber-400" />
                        <span className="w-2.5 h-2.5 rounded-full bg-emerald-400" />
                      </div>
                      <span className="ml-1">{t('mail.canned.editor.codeEditor')}</span>
                    </div>
                    <span className="text-[10px] font-mono text-gray-400 dark:text-slate-500">{formBody.length} chars</span>
                  </div>
                  <textarea
                    value={formBody}
                    onChange={(e) => setFormBody(e.target.value)}
                    rows={14}
                    placeholder={t('mail.canned.editor.bodyPlaceholder')}
                    spellCheck={false}
                    className="flex-1 px-3.5 py-3 bg-slate-50 dark:bg-slate-900/80 text-sm font-mono leading-[1.75] rounded-xl border border-gray-200 dark:border-slate-700 focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-400 dark:text-slate-200 placeholder:text-slate-400 tabular-nums resize-none transition-all"
                  />
                </div>
                {/* 右：实时预览 */}
                <div className="flex flex-col">
                  <div className="mb-2 flex items-center justify-between flex-wrap gap-2">
                    <div className="inline-flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider text-emerald-700 dark:text-emerald-300">
                      <div className="p-1 rounded-md bg-gradient-to-br from-emerald-500 to-teal-500 text-white shadow-sm">
                        <MonitorPlay className="w-3 h-3" />
                      </div>
                      {t('mail.canned.editor.liveRender')}
                    </div>
                    <span className="inline-flex items-center gap-1 text-[10px] text-gray-400 dark:text-slate-500">
                      <RefreshCw className="w-3 h-3" />
                      {t('mail.canned.editor.noDelay')}
                    </span>
                  </div>
                  <div className="flex-1 rounded-xl border border-gray-200 dark:border-slate-700/60 overflow-hidden bg-gradient-to-br from-white via-gray-50/30 to-white dark:from-slate-900 dark:via-slate-800/30 dark:to-slate-900 min-h-[300px] flex flex-col">
                    {/* 浏览器窗口条 */}
                    <div className="px-3.5 py-2.5 border-b border-gray-100 dark:border-slate-800 bg-gradient-to-r from-gray-50 via-white to-gray-50 dark:from-slate-900 dark:via-slate-800 dark:to-slate-900 flex items-center gap-2 flex-wrap">
                      <div className="flex gap-1.5">
                        <span className="w-3 h-3 rounded-full bg-red-400 shadow-inner" />
                        <span className="w-3 h-3 rounded-full bg-amber-400 shadow-inner" />
                        <span className="w-3 h-3 rounded-full bg-emerald-400 shadow-inner" />
                      </div>
                      <div className="flex-1 min-w-[140px] px-3 py-0.5 rounded-lg bg-gray-100 dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-[10px] font-mono text-gray-500 dark:text-slate-400 truncate">
                        {t('mail.canned.editor.bodyPreviewLabel')}
                      </div>
                    </div>
                    <div className="flex-1 p-4 md:p-5 overflow-auto">
                      {formBody ? (
                        <div className="prose prose-sm prose-slate dark:prose-invert max-w-none prose-headings:font-bold prose-a:text-primary-600 prose-a:no-underline hover:prose-a:underline [&_ul]:list-disc [&_ul]:pl-5 [&_ol]:list-decimal [&_ol]:pl-5 [&_li]:mb-1">
                          <div
                            dangerouslySetInnerHTML={{ __html: sanitizeHtml(formBody) }}
                          />
                        </div>
                      ) : (
                        <div className="flex flex-col items-center justify-center h-full text-gray-400 dark:text-slate-500 text-center py-8">
                          <div className="w-14 h-14 mb-3 rounded-2xl bg-gradient-to-br from-gray-50 to-gray-100 dark:from-slate-800 dark:to-slate-900 border border-gray-100 dark:border-slate-700 flex items-center justify-center">
                            <MonitorPlay className="w-6 h-6 opacity-30" />
                          </div>
                          <p className="text-xs font-semibold">{t('mail.canned.editor.previewPlaceholder')}</p>
                          <p className="text-[11px] mt-0.5 opacity-70">{t('mail.canned.editor.previewPlaceholderHint')}</p>
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
        <div className="mt-5 md:mt-6 flex items-center gap-2.5 flex-wrap justify-end">
          <button
            onClick={crud.closeEditor}
            disabled={saving}
            className={btnSecondary}
          >
            {t('common.cancel')}
          </button>
          <button
            onClick={handleSave}
            disabled={saving}
            className={btnPrimary + ' min-w-[130px] justify-center'}
          >
            {saving && <RefreshCw className="w-4 h-4 animate-spin" />}
            {saving ? t('mail.canned.editor.saving') : editingId ? t('mail.canned.editor.saveChanges') : t('mail.canned.editor.createSubmit')}
          </button>
        </div>
      </Modal>

      {/* 预览 Modal */}
      <Modal
        open={!!previewId}
        onClose={() => setPreviewId(null)}
        title={previewItem?.name || t('mail.canned.preview.title')}
        size="xl"
      >
        {previewItem && (
          <div className="space-y-4">
            {/* 主题 + 标签卡 */}
            <div className="relative rounded-2xl bg-gradient-to-br from-primary-50/70 via-white to-indigo-50/70 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-900/20 border border-primary-100 dark:border-slate-700/60 p-4 md:p-5 overflow-hidden">
              <div className="pointer-events-none absolute -top-16 -right-16 w-44 h-44 rounded-full bg-primary-500/15 blur-3xl" />
              <div className="relative flex items-start gap-4">
                <div className="shrink-0 w-11 h-11 md:w-12 md:h-12 rounded-2xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-md shadow-primary-500/30 flex items-center justify-center ring-1 ring-white/40 dark:ring-white/10">
                  <MessageSquare className="w-5 h-5 md:w-5.5 md:h-5.5" strokeWidth={2} />
                </div>
                <div className="flex-1 min-w-0">
                  <h3 className="text-base md:text-lg font-extrabold tracking-tight text-gray-900 dark:text-slate-50 break-words leading-snug">
                    {previewItem.name}
                  </h3>
                  <div className="mt-2.5 flex items-center gap-2 flex-wrap">
                    {previewItem.category && (
                      <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-xs font-bold ${pickPalette(previewItem.category).bg} ${pickPalette(previewItem.category).text} ring-1 ${pickPalette(previewItem.category).ringSoft}`}>
                        <Tag className="w-3 h-3" />
                        {previewItem.category}
                      </span>
                    )}
                    {previewItem.mailboxName && (
                      <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-xs font-bold bg-gray-100 dark:bg-slate-700/50 text-gray-700 dark:text-slate-300 ring-1 ring-gray-500/10">
                        <Mail className="w-3 h-3" />
                        {previewItem.mailboxName}
                      </span>
                    )}
                    {previewItem.isShared ? (
                      <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-xs font-bold bg-gradient-to-br from-emerald-50 to-teal-50 dark:from-emerald-900/30 dark:to-teal-900/20 text-emerald-700 dark:text-emerald-300 ring-1 ring-emerald-500/20">
                        <Share2 className="w-3 h-3" />
                        {t('mail.canned.preview.sharedBadge')}
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-xs font-bold bg-gradient-to-br from-amber-50 to-orange-50 dark:from-amber-900/30 dark:to-orange-900/20 text-amber-700 dark:text-amber-300 ring-1 ring-amber-500/20">
                        <User2 className="w-3 h-3" />
                        {t('mail.canned.preview.personalBadge')}
                      </span>
                    )}
                    {typeof previewItem.sortOrder === 'number' && (
                      <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-xs font-bold text-primary-700 dark:text-primary-300 bg-primary-50 dark:bg-primary-900/30 ring-1 ring-primary-500/20">
                        <Zap className="w-3 h-3" />
                        {t('mail.common.trashSortOrderPrefix')}{previewItem.sortOrder}
                      </span>
                    )}
                  </div>
                </div>
              </div>
            </div>

            {/* 浏览器预览窗口 */}
            <div className="rounded-2xl border border-gray-200 dark:border-slate-700/60 overflow-hidden shadow-sm">
              <div className="px-4 py-3 border-b border-gray-100 dark:border-slate-800 bg-gradient-to-r from-gray-50 via-white to-gray-50 dark:from-slate-900 dark:via-slate-800 dark:to-slate-900 flex items-center gap-3 flex-wrap">
                <div className="flex gap-1.5">
                  <span className="w-3 h-3 rounded-full bg-red-400 shadow-inner" />
                  <span className="w-3 h-3 rounded-full bg-amber-400 shadow-inner" />
                  <span className="w-3 h-3 rounded-full bg-emerald-400 shadow-inner" />
                </div>
                <div className="flex-1 min-w-[180px] px-3 py-1 rounded-lg bg-gray-100 dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-[11px] font-mono text-gray-500 dark:text-slate-400 truncate">
                  {t('mail.canned.preview.browserBar')}
                </div>
                <div className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg bg-emerald-50 dark:bg-emerald-900/30 text-emerald-600 dark:text-emerald-400 ring-1 ring-emerald-500/20">
                  <CheckCircle2 className="w-3.5 h-3.5" />
                  <span className="text-[10px] font-bold uppercase tracking-wider">{t('mail.canned.preview.renderDone')}</span>
                </div>
              </div>
              <div className="p-5 md:p-7 min-h-[320px] bg-gradient-to-br from-white via-gray-50/30 to-white dark:from-slate-900 dark:via-slate-800/30 dark:to-slate-900">
                <div className="prose prose-sm prose-slate dark:prose-invert max-w-none prose-headings:font-bold prose-a:text-primary-600 prose-a:no-underline hover:prose-a:underline [&_ul]:list-disc [&_ul]:pl-5 [&_ol]:list-decimal [&_ol]:pl-5 [&_li]:mb-1">
                  <div
                    dangerouslySetInnerHTML={{ __html: sanitizeHtml(previewItem.body) }}
                  />
                </div>
                {!previewItem.body && (
                  <EmptyState
                    icon={FileText}
                    title={t('mail.canned.preview.emptyTitle')}
                    subtitle={t('mail.canned.preview.emptyHint')}
                  />
                )}
              </div>
            </div>

            {/* 时间 + 创建人 */}
            <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm px-4 md:px-5 py-3.5 flex items-center justify-between flex-wrap gap-3">
              <div className="flex items-center gap-4 flex-wrap text-xs text-gray-500 dark:text-slate-400">
                <span className="inline-flex items-center gap-1.5">
                  <Clock className="w-3.5 h-3.5 opacity-70" />
                  {t('mail.common.createdAtPrefix')} <b className="text-gray-700 dark:text-slate-200 font-semibold">{formatDateTime(previewItem.createdAt)}</b>
                </span>
                <span className="inline-flex items-center gap-1.5">
                  <RefreshCw className="w-3.5 h-3.5 opacity-70" />
                  {t('mail.common.updatedAtPrefix')} <b className="text-gray-700 dark:text-slate-200 font-semibold">{formatDateTime(previewItem.updatedAt)}</b>
                </span>
                {previewItem.createdByUserName && (
                  <span className="inline-flex items-center gap-1.5">
                    <User2 className="w-3.5 h-3.5 opacity-70" />
                    {t('mail.canned.preview.createdBy')} <b className="text-gray-700 dark:text-slate-200 font-semibold">{previewItem.createdByUserName}</b>
                  </span>
                )}
              </div>
              <div className="flex items-center gap-2">
                <button
                  onClick={() => { setPreviewId(null); openEdit(previewItem); }}
                  className={btnSecondary + ' !py-1.5 !text-xs'}
                >
                  <Edit2 className="w-3.5 h-3.5" />
                  {t('mail.canned.preview.editNow')}
                </button>
              </div>
            </div>
          </div>
        )}
      </Modal>

      {/* 删除确认 */}
      <ConfirmDialog
        open={!!crud.deleteTarget}
        title={t('mail.canned.deleteDialog.title')}
        message={t('mail.canned.deleteDialog.message')}
        confirmText={t('common.delete')}
        variant="danger"
        loading={deleting}
        onConfirm={handleDelete}
        onCancel={() => crud.setDeleteTarget(null)}
      />

      {/* 垃圾箱弹窗 */}
      <CannedResponseTrashModal
        open={crud.trash.open}
        onClose={() => crud.trash.setOpen(false)}
        query={crud.trash.query}
        onQueryChange={crud.trash.setQuery}
        list={crud.trash.list}
        loading={crud.trash.loading}
        count={crud.trash.count}
        actionLoading={crud.trash.actionLoading}
        restoreTarget={restoreTarget}
        onSetRestoreTarget={setRestoreTarget}
        hardDeleteTarget={hardDeleteTarget}
        onSetHardDeleteTarget={setHardDeleteTarget}
        clearTrashOpen={clearTrashOpen}
        onSetClearTrashOpen={setClearTrashOpen}
        onRestore={handleRestore}
        onHardDelete={handleHardDelete}
        onClearTrash={handleClearTrash}
        mailboxes={mailboxes}
      />
    </div>
  );
}
