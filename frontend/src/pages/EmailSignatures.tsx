import { useState, useEffect, useRef, useMemo, useCallback } from 'react';
import { emailSignatureApi, mailboxApi, userApi } from '../api';
import { useCrudPage } from '../hooks/useCrudPage';
import type {
  EmailSignatureDto,
  EmailSignatureOverviewResponse,
  CreateEmailSignatureRequest,
  UpdateEmailSignatureRequest,
  MailboxResponse,
  UserResponse,
} from '../api/types';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import UserAvatar from '../components/UserAvatar';
import EmailSignatureTrashModal from '../components/EmailSignatureTrashModal';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import { formatDateTime, sanitizeHtml } from '../utils/format';
import { getApiErrorMessage } from '../utils/error';
import {
  PenLine,
  CheckCircle2,
  Plus,
  Search,
  Edit2,
  Trash2,
  Eye,
  X,
  Mail,
  Globe,
  Star,
  Hash,
  Sparkles,
  RefreshCw,
  Layers,
  Inbox,
  Filter,
  AlertCircle,
  ChevronLeft,
  ChevronRight,
  User2,
  Database,
  Zap,
  Type,
} from 'lucide-react';

interface EmailSignatureQuery {
  page: number;
  pageSize: number;
  keyword: string;
  mailboxId: number | null;
  isDefault: boolean | null;
  userId: number | null;
}

// 通用按钮样式
const btnPrimary =
  'inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold text-white rounded-xl bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 shadow-sm shadow-primary-500/20 hover:shadow hover:shadow-primary-500/30 transition-all disabled:opacity-50 ring-1 ring-primary-500/20';
const btnSecondary =
  'inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold text-gray-700 dark:text-slate-200 rounded-xl bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 hover:bg-gray-50 dark:hover:bg-slate-700/60 transition-all disabled:opacity-50 shadow-sm';

// 通用输入框样式
const inputBase =
  'w-full px-3.5 py-2.5 text-sm bg-white dark:bg-slate-900/80 border border-gray-200 dark:border-slate-700 rounded-xl text-gray-800 dark:text-slate-200 placeholder:text-gray-400 dark:placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-400 dark:focus:border-primary-500 shadow-sm transition-all';

const selectBase =
  'px-3.5 py-2.5 text-sm bg-white dark:bg-slate-900/80 border border-gray-200 dark:border-slate-700 rounded-xl text-gray-800 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-400 dark:focus:border-primary-500 shadow-sm transition-all cursor-pointer';

// 概览统计 -> 配色（与通知模版/邮件模板一致）
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
    from: 'from-sky-500',
    to: 'to-blue-500',
    textLight: 'text-sky-600 dark:text-sky-400',
    bgSoft: 'bg-sky-50 dark:bg-sky-900/30',
    ringSoft: 'ring-sky-500/20',
  },
  {
    from: 'from-violet-500',
    to: 'to-fuchsia-500',
    textLight: 'text-violet-600 dark:text-violet-400',
    bgSoft: 'bg-violet-50 dark:bg-violet-900/30',
    ringSoft: 'ring-violet-500/20',
  },
];

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

/** 概览统计卡：`labelKey` / `hintKey` 只持有真源 key，渲染处必须经 `t()` */
interface SignatureStatItem {
  labelKey: TranslationKey;
  value: number;
  hintKey: TranslationKey;
  icon: React.ComponentType<{ className?: string; strokeWidth?: string | number }>;
  palette: (typeof statPalette)[number];
}

/** 活动筛选胶囊：`labelKey` 只是文案前缀，动态值留在独立文本节点里（不塞进译文串） */
interface SignatureFilterChip {
  key: string;
  labelKey: TranslationKey;
  value: string;
}

export default function EmailSignatures() {
  const { t } = useI18n();
  const [activeTab, setActiveTab] = useState<'overview' | 'list'>('overview');
  const [overview, setOverview] = useState<EmailSignatureOverviewResponse | null>(null);
  const [overviewLoading, setOverviewLoading] = useState(false);

  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [users, setUsers] = useState<UserResponse[]>([]);

  // 编辑表单
  const [formData, setFormData] = useState<CreateEmailSignatureRequest>({
    name: '',
    body: '',
    mailboxId: null,
    isDefault: false,
    sortOrder: 0,
  });
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);

  // 预览
  const [showPreview, setShowPreview] = useState(false);
  const [previewSignature, setPreviewSignature] = useState<EmailSignatureDto | null>(null);

  // 删除
  const [deleting, setDeleting] = useState(false);

  // 垃圾箱操作目标
  const [restoreTarget, setRestoreTarget] = useState<EmailSignatureDto | null>(null);
  const [hardDeleteTarget, setHardDeleteTarget] = useState<EmailSignatureDto | null>(null);
  const [clearTrashOpen, setClearTrashOpen] = useState(false);

  const crud = useCrudPage<EmailSignatureDto, EmailSignatureQuery>({
    defaultQuery: { page: 1, pageSize: 10, keyword: '', mailboxId: null, isDefault: null, userId: null },
    fetchList: async (q) => {
      const data = await emailSignatureApi.list({
        page: q.page,
        pageSize: q.pageSize,
        keyword: q.keyword || null,
        mailboxId: q.mailboxId,
        isDefault: q.isDefault,
        userId: q.userId,
      });
      return {
        data: data.data,
        totalItems: data.totalItems,
        totalPages: data.totalPages,
        currentPage: q.page,
      };
    },
    fetchTrashList: async (q) => {
      const res = await emailSignatureApi.getTrash(q.page, q.pageSize, q.keyword);
      return { data: res.data ?? [], totalItems: res.totalItems ?? 0, totalPages: res.totalPages ?? 0, currentPage: q.page };
    },
    fetchTrashCount: emailSignatureApi.getTrashCount,
  });

  const { toast } = crud;

  // 与 ApiKeys 一致：服务端搜索 650ms 输入防抖 + Enter 即时触发（缓冲 state，避免每次按键发请求）
  const keywordTimerRef = useRef<number | null>(null);
  const [keywordInput, setKeywordInput] = useState('');
  const applyKeyword = useCallback((value: string) => {
    crud.handleFilterChange('keyword', value);
  }, [crud.handleFilterChange]);
  const handleKeywordInput = useCallback((value: string) => {
    setKeywordInput(value);
    if (keywordTimerRef.current !== null) window.clearTimeout(keywordTimerRef.current);
    keywordTimerRef.current = window.setTimeout(() => applyKeyword(value), 650);
  }, [applyKeyword]);
  const triggerKeywordSearch = useCallback(() => {
    if (keywordTimerRef.current !== null) {
      window.clearTimeout(keywordTimerRef.current);
      keywordTimerRef.current = null;
    }
    applyKeyword(keywordInput);
  }, [applyKeyword, keywordInput]);
  // crud.query.keyword 被其他入口（筛选胶囊移除/重置等）改动时，同步输入框缓冲
  useEffect(() => {
    setKeywordInput(crud.query.keyword ?? '');
  }, [crud.query.keyword]);
  // 卸载时清理挂起的防抖定时器
  useEffect(() => {
    return () => {
      if (keywordTimerRef.current !== null) window.clearTimeout(keywordTimerRef.current);
    };
  }, []);

  // 加载邮箱和用户列表
  useEffect(() => {
    let cancelled = false;
    mailboxApi
      .listAll()
      .then((res: MailboxResponse[]) => { if (!cancelled) setMailboxes(Array.isArray(res) ? res : []); })
      .catch((err: unknown) => { if (!cancelled) console.error('加载邮箱列表失败:', err); });
    userApi
      .list()
      .then((res) => { if (!cancelled) setUsers(Array.isArray(res?.data) ? res.data : []); })
      .catch((err: unknown) => { if (!cancelled) console.error('加载用户失败:', err); });
    return () => { cancelled = true; };
  }, []);

  // 加载概览数据
  const loadOverviewRunning = useRef(false);
  const loadOverview = async () => {
    if (loadOverviewRunning.current) return;
    loadOverviewRunning.current = true;
    let cancelled = false;
    setOverviewLoading(true);
    try {
      const data = await emailSignatureApi.overview();
      if (!cancelled) setOverview(data);
    } finally {
      if (!cancelled) setOverviewLoading(false);
      loadOverviewRunning.current = false;
    }
    return () => { cancelled = true; };
  };

  useEffect(() => {
    if (activeTab === 'overview') {
      const cleanup = loadOverview();
      return () => { (cleanup as Promise<void> | undefined)?.then?.(() => { /* noop */ }); };
    }
    return () => { };
     
  }, [activeTab]);

  const openTrash = () => {
    crud.trash.setOpen(true);
    crud.trash.setQuery({ page: 1, pageSize: 10, keyword: '' });
    crud.trash.refreshCount();
  };

  const handleRestore = async () => {
    if (!restoreTarget) return;
    crud.trash.setActionLoading(true);
    try {
      await emailSignatureApi.restore(restoreTarget.id);
      toast.show({ type: 'success', text: t('mail.signatures.toast.restored', { name: restoreTarget.name }) });
      setRestoreTarget(null);
      crud.trash.refresh();
      crud.trash.refreshCount();
      crud.refresh();
      if (activeTab === 'overview') await loadOverview();
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
      await emailSignatureApi.hardDelete(hardDeleteTarget.id);
      toast.show({ type: 'success', text: t('mail.signatures.toast.hardDeleted', { name: hardDeleteTarget.name }) });
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
      const res: { count?: number } = await emailSignatureApi.clearTrash();
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
    setFormData({
      name: '',
      body: '',
      mailboxId: null,
      isDefault: false,
      sortOrder: 0,
    });
    setFormError('');
    crud.openEditor();
  };

  const openEdit = (sig: EmailSignatureDto) => {
    setFormData({
      name: sig.name,
      body: sig.body,
      mailboxId: sig.mailboxId,
      isDefault: sig.isDefault,
      sortOrder: sig.sortOrder,
    });
    setFormError('');
    crud.openEditor(sig);
  };

  const openPreview = (sig: EmailSignatureDto) => {
    setPreviewSignature(sig);
    setShowPreview(true);
  };

  const handleSave = async () => {
    if (!formData.name.trim()) {
      setFormError(t('mail.signatures.form.nameRequired'));
      return;
    }
    if (!formData.body.trim()) {
      setFormError(t('mail.signatures.form.bodyRequired'));
      return;
    }
    setSaving(true);
    setFormError('');
    try {
      if (crud.editing) {
        await emailSignatureApi.update(crud.editing.id, formData as UpdateEmailSignatureRequest);
        toast.show({ type: 'success', text: t('mail.signatures.toast.updated') });
      } else {
        await emailSignatureApi.create(formData);
        toast.show({ type: 'success', text: t('mail.signatures.toast.created') });
      }
      crud.closeEditor();
      crud.refresh();
      if (activeTab === 'overview') await loadOverview();
    } catch (e: unknown) {
      setFormError(getApiErrorMessage(e, t('mail.common.saveFailed')));
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    if (!crud.deleteTarget) return;
    setDeleting(true);
    try {
      await emailSignatureApi.remove(crud.deleteTarget.id);
      toast.show({ type: 'success', text: t('mail.signatures.toast.movedToTrash') });
      crud.setDeleteTarget(null);
      crud.trash.refreshCount();
      crud.refresh();
      if (activeTab === 'overview') await loadOverview();
    } catch (e: unknown) {
      toast.show({ type: 'error', text: getApiErrorMessage(e, t('mail.common.deleteFailed')) });
    } finally {
      setDeleting(false);
    }
  };

  const handleSetDefault = async (id: number) => {
    try {
      await emailSignatureApi.setDefault(id);
      toast.show({ type: 'success', text: t('mail.signatures.toast.setDefault') });
      crud.refresh();
      if (activeTab === 'overview') await loadOverview();
    } catch (e: unknown) {
      toast.show({ type: 'error', text: getApiErrorMessage(e, t('mail.signatures.toast.setDefaultFailed')) });
    }
  };

  const signatures = crud.list?.data ?? [];
  const totalItems = crud.list?.totalItems ?? 0;
  const page = crud.query.page;
  const pageSize = crud.query.pageSize;
  const totalPages = Math.ceil(totalItems / pageSize) || 1;

  // 概览统计卡片
  const statItems = useMemo<SignatureStatItem[]>(() => {
    if (!overview) return [];
    return [
      {
        labelKey: 'mail.signatures.stats.total',
        value: overview.totalSignatures,
        hintKey: 'mail.signatures.stats.totalHint',
        icon: PenLine,
        palette: statPalette[0],
      },
      {
        labelKey: 'mail.signatures.stats.default',
        value: overview.defaultSignatures,
        hintKey: 'mail.signatures.stats.defaultHint',
        icon: CheckCircle2,
        palette: statPalette[1],
      },
      {
        labelKey: 'mail.signatures.stats.mailbox',
        value: overview.mailboxSignatures,
        hintKey: 'mail.signatures.stats.mailboxHint',
        icon: Mail,
        palette: statPalette[2],
      },
      {
        labelKey: 'mail.signatures.stats.global',
        value: overview.globalSignatures,
        hintKey: 'mail.signatures.stats.globalHint',
        icon: Globe,
        palette: statPalette[3],
      },
    ];
  }, [overview]);

  // 活动筛选条件摘要
  const activeFilters = useMemo<SignatureFilterChip[]>(() => {
    const arr: SignatureFilterChip[] = [];
    if (crud.query.keyword) arr.push({ key: 'keyword', labelKey: 'mail.signatures.filter.keywordPrefix', value: crud.query.keyword });
    if (crud.query.mailboxId !== null) {
      const mb = mailboxes.find((m) => m.id === crud.query.mailboxId);
      arr.push({ key: 'mailboxId', labelKey: 'mail.signatures.filter.mailboxPrefix', value: mb?.name ?? `#${crud.query.mailboxId}` });
    }
    if (crud.query.userId !== null) {
      const u = users.find((x) => x.id === crud.query.userId);
      arr.push({ key: 'userId', labelKey: 'mail.signatures.filter.userPrefix', value: u?.fullName ?? `#${crud.query.userId}` });
    }
    if (crud.query.isDefault !== null) {
      arr.push({ key: 'isDefault', labelKey: crud.query.isDefault ? 'mail.signatures.filter.onlyDefault' : 'mail.signatures.filter.nonDefault', value: '' });
    }
    return arr;
  }, [crud.query, mailboxes, users]);

  const removeFilter = (key: string) => {
    if (key === 'keyword') crud.handleFilterChange('keyword', '');
    else if (key === 'mailboxId') crud.handleFilterChange('mailboxId', null);
    else if (key === 'userId') crud.handleFilterChange('userId', null);
    else if (key === 'isDefault') crud.handleFilterChange('isDefault', null);
  };

  return (
    <div className="p-6 space-y-5">
      {/* Toast */}
      {toast.message && (
        <div className={`fixed top-4 right-4 z-50 px-4 py-3 rounded-xl shadow-lg text-sm flex items-center gap-2 ${
          toast.message.type === 'success'
            ? 'bg-gradient-to-br from-emerald-500 to-teal-500 text-white shadow-emerald-500/30'
            : 'bg-gradient-to-br from-rose-500 to-red-500 text-white shadow-rose-500/30'
        }`}>
          {toast.message.type === 'success' ? (
            <CheckCircle2 className="w-5 h-5" strokeWidth={2} />
          ) : (
            <AlertCircle className="w-5 h-5" strokeWidth={2} />
          )}
          {toast.message.text}
        </div>
      )}

      {/* 页头 Hero 渐变卡 */}
      <div className="relative overflow-hidden rounded-3xl border border-gray-200 dark:border-slate-700/60 bg-gradient-to-br from-primary-50 via-white to-indigo-50 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-900/20 shadow-sm">
        <div className="pointer-events-none absolute -top-24 -right-24 w-72 h-72 rounded-full bg-primary-500/20 blur-3xl" />
        <div className="pointer-events-none absolute top-16 -left-16 w-60 h-60 rounded-full bg-indigo-500/15 blur-3xl" />
        <div className="relative p-6 md:p-7">
          <div className="flex items-start justify-between gap-4 flex-wrap">
            <div className="flex items-start gap-4">
              <div className="shrink-0 w-14 h-14 rounded-2xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-lg shadow-primary-500/30 flex items-center justify-center ring-1 ring-white/40 dark:ring-white/10">
                <PenLine className="w-7 h-7" strokeWidth={2} />
              </div>
              <div className="min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <h1 className="text-2xl md:text-3xl font-extrabold tracking-tight text-gray-900 dark:text-slate-50">{t('mail.signatures.page.title')}</h1>
                  <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider rounded-full bg-gradient-to-r from-sky-100 to-blue-100 dark:from-sky-900/40 dark:to-blue-900/40 text-sky-700 dark:text-sky-300 ring-1 ring-sky-500/20">
                    <Mail className="w-3 h-3" />
                    {t('mail.signatures.page.badge')}
                  </span>
                </div>
                <p className="text-sm md:text-base text-gray-500 dark:text-slate-400 mt-1.5 leading-relaxed">
                  {t('mail.signatures.page.subtitle')}
                </p>
              </div>
            </div>
            <div className="flex items-center gap-2.5 flex-wrap">
              <button onClick={openTrash} className={btnSecondary}>
                <Trash2 className="w-4 h-4" />
                {t('mail.common.trash')}
                {crud.trash.count > 0 && (
                  <span className="px-1.5 py-0.5 text-[10px] font-black rounded-full bg-rose-100 dark:bg-rose-900/40 text-rose-600 dark:text-rose-400">
                    {crud.trash.count}
                  </span>
                )}
              </button>
              <button onClick={openCreate} className={btnPrimary}>
                <Plus className="w-4 h-4" />
                {t('mail.signatures.page.new')}
              </button>
            </div>
          </div>

          {/* 概览统计卡 */}
          {activeTab === 'overview' && overview && !overviewLoading && (
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

      {/* Tab 切换条 */}
      <div className="rounded-2xl border border-gray-200 dark:border-slate-700/60 bg-white dark:bg-slate-800/60 shadow-sm p-1.5 inline-flex gap-1">
        <button
          className={`inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold rounded-xl transition-all ${
            activeTab === 'overview'
              ? 'bg-gradient-to-br from-primary-600 to-indigo-600 text-white shadow-sm shadow-primary-500/30 ring-1 ring-primary-500/20'
              : 'text-gray-600 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-700/60'
          }`}
          onClick={() => setActiveTab('overview')}
        >
          <Layers className="w-4 h-4" />
          {t('mail.common.overview')}
        </button>
        <button
          className={`inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold rounded-xl transition-all ${
            activeTab === 'list'
              ? 'bg-gradient-to-br from-primary-600 to-indigo-600 text-white shadow-sm shadow-primary-500/30 ring-1 ring-primary-500/20'
              : 'text-gray-600 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-700/60'
          }`}
          onClick={() => setActiveTab('list')}
        >
          <Database className="w-4 h-4" />
          {t('mail.signatures.page.tabsList')}
        </button>
      </div>

      {/* 概览 Tab */}
      {activeTab === 'overview' && (
        <div className="space-y-5">
          {overviewLoading ? (
            <div className="bg-white dark:bg-slate-800/60 rounded-2xl border border-gray-200 dark:border-slate-700/60 shadow-sm">
              <LoadingState text={t('mail.signatures.page.loadingOverview')} />
            </div>
          ) : overview ? (
            <>
              {/* 常用签名列表卡 */}
              <div className="bg-white dark:bg-slate-800/70 rounded-2xl border border-gray-200 dark:border-slate-700/60 shadow-sm overflow-hidden">
                <div className="flex items-center justify-between px-5 py-4 border-b border-gray-100 dark:border-slate-700/60">
                  <h3 className="text-base font-extrabold text-gray-800 dark:text-slate-100 flex items-center gap-2">
                    <div className="w-8 h-8 rounded-lg bg-gradient-to-br from-primary-500 to-indigo-500 text-white flex items-center justify-center shadow-sm">
                      <Star className="w-4 h-4" strokeWidth={2} />
                    </div>
                    {t('mail.signatures.overview.sectionTitle')}
                  </h3>
                  <button
                    onClick={() => setActiveTab('list')}
                    className="inline-flex items-center gap-1 text-sm text-primary-600 hover:text-primary-700 dark:text-primary-400 dark:hover:text-primary-300 font-bold transition-colors"
                  >
                    {t('mail.signatures.overview.viewAll')}
                    <ChevronRight className="w-4 h-4" strokeWidth={2} />
                  </button>
                </div>
                <div className="p-5">
                  {overview.topSignatures.length > 0 ? (
                    <div className="space-y-3">
                      {overview.topSignatures.map((sig) => {
                        const isGlobal = sig.mailboxId === null;
                        return (
                          <div
                            key={sig.id}
                            className="group flex items-center justify-between p-4 rounded-2xl bg-gradient-to-r from-gray-50 to-slate-50 dark:from-slate-900/50 dark:to-slate-800/50 hover:from-primary-50/50 hover:to-indigo-50/50 dark:hover:from-primary-900/20 dark:hover:to-indigo-900/20 border border-gray-100 dark:border-slate-700/60 transition-all"
                          >
                            <div className="flex items-center gap-3 min-w-0 flex-1">
                              <div className="shrink-0 w-10 h-10 rounded-xl bg-gradient-to-br from-primary-100 to-indigo-100 dark:from-primary-900/40 dark:to-indigo-900/40 text-primary-600 dark:text-primary-400 flex items-center justify-center ring-1 ring-primary-500/20">
                                <Type className="w-5 h-5" strokeWidth={2} />
                              </div>
                              <div className="flex-1 min-w-0">
                                <div className="flex items-center gap-2 flex-wrap">
                                  <span className="font-bold text-gray-800 dark:text-slate-100 tracking-tight">{sig.name}</span>
                                  {sig.isDefault && (
                                    <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider rounded-full bg-gradient-to-br from-emerald-50 to-teal-50 dark:from-emerald-900/30 dark:to-teal-900/20 text-emerald-700 dark:text-emerald-400 ring-1 ring-emerald-500/20">
                                      <CheckCircle2 className="w-3 h-3" />
                                      {t('mail.signatures.badge.default')}
                                    </span>
                                  )}
                                  <span
                                    className={`inline-flex items-center gap-1 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider rounded-full ring-1 ${
                                      isGlobal
                                        ? 'bg-gradient-to-br from-violet-50 to-fuchsia-50 dark:from-violet-900/30 dark:to-fuchsia-900/20 text-violet-700 dark:text-violet-300 ring-violet-500/20'
                                        : 'bg-gradient-to-br from-sky-50 to-blue-50 dark:from-sky-900/30 dark:to-blue-900/20 text-sky-700 dark:text-sky-300 ring-sky-500/20'
                                    }`}
                                  >
                                    {isGlobal ? <Globe className="w-3 h-3" /> : <Mail className="w-3 h-3" />}
                                    {isGlobal ? t('mail.common.global') : sig.mailboxName}
                                  </span>
                                </div>
                                <p className="text-xs text-gray-500 dark:text-slate-400 mt-1 flex items-center gap-1.5">
                                  <User2 className="w-3 h-3 opacity-70" />
                                  {sig.userName}
                                </p>
                              </div>
                            </div>
                            <div className="flex items-center gap-1 ml-4">
                              <button
                                onClick={() => openPreview(sig)}
                                className="p-2.5 text-gray-400 dark:text-slate-500 hover:text-primary-600 dark:hover:text-primary-400 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition-all"
                                title={t('mail.common.preview')}
                              >
                                <Eye className="w-[18px] h-[18px]" strokeWidth={2} />
                              </button>
                              <button
                                onClick={() => openEdit(sig)}
                                className="p-2.5 text-gray-400 dark:text-slate-500 hover:text-sky-600 dark:hover:text-sky-400 hover:bg-sky-50 dark:hover:bg-sky-900/30 rounded-xl transition-all"
                                title={t('common.edit')}
                              >
                                <Edit2 className="w-[18px] h-[18px]" strokeWidth={2} />
                              </button>
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  ) : (
                    <EmptyState
                      icon={PenLine}
                      title={t('mail.signatures.overview.emptyTitle')}
                      subtitle={t('mail.signatures.overview.emptySubtitle')}
                      action={
                        <button onClick={openCreate} className={btnPrimary}>
                          <Plus className="w-4 h-4" />
                          {t('mail.signatures.page.new')}
                        </button>
                      }
                    />
                  )}
                </div>
              </div>
            </>
          ) : null}
        </div>
      )}

      {/* 列表 Tab */}
      {activeTab === 'list' && (
        <div className="space-y-4">
          {/* 筛选栏 */}
          <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm p-3 md:p-4">
            <div className="flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-3">
              <Filter className="w-3.5 h-3.5" />
              {t('mail.signatures.list.filterTitle')}
            </div>
            <div className="flex flex-wrap items-center gap-2.5">
              <div className="relative flex-1 min-w-[200px] max-w-[320px]">
                <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
                <input
                  type="text"
                  placeholder={t('mail.signatures.list.searchPlaceholder')}
                  value={keywordInput}
                  onChange={(e) => handleKeywordInput(e.target.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter') triggerKeywordSearch(); }}
                  className="w-full pl-10 pr-3 py-2.5 text-sm bg-slate-50 dark:bg-slate-900/60 border border-gray-200 dark:border-slate-700 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-400 dark:focus:border-primary-500 text-gray-800 dark:text-slate-200 placeholder:text-gray-400 dark:placeholder:text-slate-500 transition-all shadow-sm"
                />
              </div>
              <select
                value={crud.query.mailboxId ?? ''}
                onChange={(e) => crud.handleFilterChange('mailboxId', e.target.value === '' ? null : Number(e.target.value))}
                className={selectBase}
              >
                <option value="">{t('mail.signatures.filter.allMailboxes')}</option>
                {mailboxes.map((m) => (
                  <option key={m.id} value={m.id}>{m.name}</option>
                ))}
              </select>
              <select
                value={crud.query.userId ?? ''}
                onChange={(e) => crud.handleFilterChange('userId', e.target.value === '' ? null : Number(e.target.value))}
                className={selectBase}
              >
                <option value="">{t('mail.signatures.filter.allUsers')}</option>
                {users.map((u) => (
                  <option key={u.id} value={u.id}>{u.fullName}</option>
                ))}
              </select>
              <select
                value={crud.query.isDefault === null ? '' : String(crud.query.isDefault)}
                onChange={(e) => {
                  const v = e.target.value;
                  crud.handleFilterChange('isDefault', v === '' ? null : v === 'true');
                }}
                className={selectBase}
              >
                <option value="">{t('mail.common.allStatuses')}</option>
                <option value="true">{t('mail.signatures.filter.onlyDefault')}</option>
                <option value="false">{t('mail.signatures.filter.nonDefault')}</option>
              </select>
              {(crud.query.keyword ||
                crud.query.mailboxId !== null ||
                crud.query.userId !== null ||
                crud.query.isDefault !== null) && (
                <button
                  onClick={() => {
                    crud.handleFilterChange('keyword', '');
                    crud.handleFilterChange('mailboxId', null);
                    crud.handleFilterChange('userId', null);
                    crud.handleFilterChange('isDefault', null);
                  }}
                  className="inline-flex items-center gap-1 px-3 py-2 text-xs text-gray-600 dark:text-slate-300 hover:text-rose-600 dark:hover:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-900/30 rounded-xl transition-all font-bold border border-gray-200 dark:border-slate-700"
                >
                  <RefreshCw className="w-3.5 h-3.5" />
                  {t('common.reset')}
                </button>
              )}
            </div>

            {/* 筛选摘要胶囊条 */}
            {activeFilters.length > 0 && (
              <div className="flex items-center gap-1.5 mt-3 pt-3 border-t border-gray-100 dark:border-slate-700/60 flex-wrap">
                <span className="text-[11px] font-bold uppercase tracking-wider text-gray-400 dark:text-slate-500">{t('mail.signatures.list.appliedColon')}</span>
                {activeFilters.map((f) => (
                  <button
                    key={f.key}
                    onClick={() => removeFilter(f.key)}
                    className="inline-flex items-center gap-1 px-2.5 py-1 text-[11px] font-bold rounded-full bg-gradient-to-br from-primary-50 to-indigo-50 dark:from-primary-900/30 dark:to-indigo-900/20 text-primary-700 dark:text-primary-300 ring-1 ring-primary-500/20 hover:from-rose-50 hover:to-red-50 dark:hover:from-rose-900/30 dark:hover:to-red-900/20 hover:text-rose-600 dark:hover:text-rose-400 hover:ring-rose-500/20 transition-all"
                  >
                    {t(f.labelKey)}{f.value}
                    <X className="w-3 h-3" />
                  </button>
                ))}
              </div>
            )}
          </div>

          {/* 列表表格 */}
          <div className="bg-white dark:bg-slate-800/70 rounded-2xl border border-gray-200 dark:border-slate-700/60 shadow-sm overflow-hidden">
            {crud.loading ? (
              <LoadingState text={t('mail.signatures.page.loadingList')} />
            ) : signatures.length > 0 ? (
              <>
                {/* PC 端表格视图 */}
                <div className="hidden md:block overflow-x-auto">
                  <table className="w-full min-w-[900px] text-sm">
                    <thead>
                      <tr className="bg-gradient-to-r from-primary-50/70 via-white to-indigo-50/70 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-900/20 text-[11px] font-black uppercase tracking-wider text-primary-700 dark:text-primary-300 border-b border-gray-100 dark:border-slate-700/60">
                        <th className="px-4 md:px-5 py-3.5 text-left">{t('mail.signatures.field.name')}</th>
                        <th className="px-4 md:px-5 py-3.5 text-left">{t('mail.common.ownerUser')}</th>
                        <th className="px-4 md:px-5 py-3.5 text-left">{t('mail.signatures.list.scopeColumn')}</th>
                        <th className="px-4 md:px-5 py-3.5 text-left">{t('mail.common.status')}</th>
                        <th className="px-4 md:px-5 py-3.5 text-left">{t('mail.signatures.field.sortOrder')}</th>
                        <th className="px-4 md:px-5 py-3.5 text-left">{t('mail.signatures.field.createdAt')}</th>
                        <th className="px-4 md:px-5 py-3.5 text-right pr-5 md:pr-6">{t('mail.common.actions')}</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-50 dark:divide-slate-700/50">
                      {signatures.map((sig) => {
                        const isGlobal = sig.mailboxId === null;
                        return (
                          <tr
                            key={sig.id}
                            className="hover:bg-gradient-to-r hover:from-primary-50/30 hover:via-white hover:to-indigo-50/30 dark:hover:from-slate-800/60 dark:hover:via-slate-800/30 dark:hover:to-slate-800/60 transition-colors"
                          >
                            <td className="px-4 md:px-5 py-3.5">
                              <div className="flex items-center gap-2">
                                <div className="shrink-0 w-8 h-8 rounded-lg bg-gradient-to-br from-primary-100 to-indigo-100 dark:from-primary-900/40 dark:to-indigo-900/40 text-primary-600 dark:text-primary-400 flex items-center justify-center ring-1 ring-primary-500/20">
                                  <Type className="w-4 h-4" strokeWidth={2} />
                                </div>
                                <div className="min-w-0">
                                  <div className="flex items-center gap-1.5 flex-wrap">
                                    <span className="font-bold text-gray-800 dark:text-slate-100 tracking-tight">{sig.name}</span>
                                    {sig.isDefault && (
                                      <span className="inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded-md bg-gradient-to-br from-emerald-50 to-teal-50 dark:from-emerald-900/30 dark:to-teal-900/20 text-emerald-700 dark:text-emerald-400 text-[10px] font-bold uppercase tracking-wider ring-1 ring-emerald-500/20">
                                        <Star className="w-2.5 h-2.5" />
                                        {t('mail.signatures.badge.default')}
                                      </span>
                                    )}
                                  </div>
                                  <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-1">
                                    <span dangerouslySetInnerHTML={{ __html: sanitizeHtml(sig.body) }} />
                                  </div>
                                </div>
                              </div>
                            </td>
                            <td className="px-4 md:px-5 py-3.5">
                              <div className="flex items-center gap-2">
                                <UserAvatar userId={sig.userId} name={sig.userName} size="sm" />
                                <div className="min-w-0">
                                  <div className="font-medium text-gray-700 dark:text-slate-200 truncate">{sig.userName}</div>
                                  <div className="text-[11px] text-gray-400 dark:text-slate-500 truncate">{sig.userEmail}</div>
                                </div>
                              </div>
                            </td>
                            <td className="px-4 md:px-5 py-3.5">
                              <span
                                className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider ring-1 ${
                                  isGlobal
                                    ? 'bg-gradient-to-br from-violet-50 to-fuchsia-50 dark:from-violet-900/30 dark:to-fuchsia-900/20 text-violet-700 dark:text-violet-300 ring-violet-500/20'
                                    : 'bg-gradient-to-br from-sky-50 to-blue-50 dark:from-sky-900/30 dark:to-blue-900/20 text-sky-700 dark:text-sky-300 ring-sky-500/20'
                                }`}
                              >
                                {isGlobal ? <Globe className="w-3 h-3" /> : <Mail className="w-3 h-3" />}
                                {isGlobal ? t('mail.common.global') : sig.mailboxName}
                              </span>
                            </td>
                            <td className="px-4 md:px-5 py-3.5">
                              {sig.isDefault ? (
                                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-gradient-to-br from-emerald-50 to-teal-50 dark:from-emerald-900/30 dark:to-teal-900/20 text-emerald-700 dark:text-emerald-400 text-[10px] font-bold uppercase tracking-wider ring-1 ring-emerald-500/20">
                                  <CheckCircle2 className="w-3 h-3" />
                                  {t('mail.signatures.list.statusDefault')}
                                </span>
                              ) : (
                                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-gray-100 dark:bg-slate-700/50 text-gray-600 dark:text-slate-300 text-[10px] font-bold uppercase tracking-wider ring-1 ring-gray-500/10">
                                  {t('mail.signatures.list.statusNormal')}
                                </span>
                              )}
                            </td>
                            <td className="px-4 md:px-5 py-3.5">
                              <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded-md bg-gray-100 dark:bg-slate-700/50 text-gray-600 dark:text-slate-300 text-[10px] font-bold ring-1 ring-gray-500/10 font-mono">
                                <Hash className="w-3 h-3" />
                                {sig.sortOrder}
                              </span>
                            </td>
                            <td className="px-4 md:px-5 py-3.5 text-xs text-gray-500 dark:text-slate-400 whitespace-nowrap">
                              {formatDateTime(sig.createdAt)}
                            </td>
                            <td className="px-4 md:px-5 py-3.5 text-right">
                              <div className="inline-flex items-center gap-1">
                                <button
                                  onClick={() => openPreview(sig)}
                                  className="p-2.5 text-gray-400 dark:text-slate-500 hover:text-primary-600 dark:hover:text-primary-400 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition-all"
                                  title={t('mail.common.preview')}
                                >
                                  <Eye className="w-[18px] h-[18px]" strokeWidth={2} />
                                </button>
                                {!sig.isDefault && (
                                  <button
                                    onClick={() => handleSetDefault(sig.id)}
                                    className="p-2.5 text-gray-400 dark:text-slate-500 hover:text-emerald-600 dark:hover:text-emerald-400 hover:bg-emerald-50 dark:hover:bg-emerald-900/30 rounded-xl transition-all"
                                    title={t('mail.signatures.list.setDefault')}
                                  >
                                    <Star className="w-[18px] h-[18px]" strokeWidth={2} />
                                  </button>
                                )}
                                <button
                                  onClick={() => openEdit(sig)}
                                  className="p-2.5 text-gray-400 dark:text-slate-500 hover:text-sky-600 dark:hover:text-sky-400 hover:bg-sky-50 dark:hover:bg-sky-900/30 rounded-xl transition-all"
                                  title={t('common.edit')}
                                >
                                  <Edit2 className="w-[18px] h-[18px]" strokeWidth={2} />
                                </button>
                                <button
                                  onClick={() => crud.setDeleteTarget(sig)}
                                  className="p-2.5 text-gray-400 dark:text-slate-500 hover:text-rose-600 dark:hover:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-900/30 rounded-xl transition-all"
                                  title={t('common.delete')}
                                >
                                  <Trash2 className="w-[18px] h-[18px]" strokeWidth={2} />
                                </button>
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
                  {signatures.map((sig) => {
                    const isGlobal = sig.mailboxId === null;
                    return (
                      <div
                        key={sig.id}
                        className="p-4 hover:bg-gradient-to-r hover:from-primary-50/30 hover:via-white hover:to-indigo-50/30 dark:hover:from-slate-800/60 dark:hover:via-slate-800/30 dark:hover:to-slate-800/60 transition-colors"
                      >
                        {/* 顶部：名称 + 默认标记 + 操作 */}
                        <div className="flex items-start justify-between gap-3">
                          <div className="flex items-start gap-2 min-w-0 flex-1">
                            <div className="shrink-0 w-8 h-8 rounded-lg bg-gradient-to-br from-primary-100 to-indigo-100 dark:from-primary-900/40 dark:to-indigo-900/40 text-primary-600 dark:text-primary-400 flex items-center justify-center ring-1 ring-primary-500/20">
                              <Type className="w-4 h-4" strokeWidth={2} />
                            </div>
                            <div className="min-w-0 flex-1">
                              <div className="flex items-center gap-1.5 flex-wrap">
                                <span className="font-bold text-gray-800 dark:text-slate-100 tracking-tight truncate">{sig.name}</span>
                                {sig.isDefault && (
                                  <span className="inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded-md bg-gradient-to-br from-emerald-50 to-teal-50 dark:from-emerald-900/30 dark:to-teal-900/20 text-emerald-700 dark:text-emerald-400 text-[10px] font-bold uppercase tracking-wider ring-1 ring-emerald-500/20">
                                    <Star className="w-2.5 h-2.5" />
                                    {t('mail.signatures.badge.default')}
                                  </span>
                                )}
                              </div>
                              <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-1">
                                <span dangerouslySetInnerHTML={{ __html: sanitizeHtml(sig.body) }} />
                              </div>
                            </div>
                          </div>
                          <div className="shrink-0 inline-flex items-center gap-1">
                            <button
                              onClick={() => openPreview(sig)}
                              className="p-2 text-gray-400 dark:text-slate-500 hover:text-primary-600 dark:hover:text-primary-400 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition-all"
                              title={t('mail.common.preview')}
                            >
                              <Eye className="w-[18px] h-[18px]" strokeWidth={2} />
                            </button>
                            {!sig.isDefault && (
                              <button
                                onClick={() => handleSetDefault(sig.id)}
                                className="p-2 text-gray-400 dark:text-slate-500 hover:text-emerald-600 dark:hover:text-emerald-400 hover:bg-emerald-50 dark:hover:bg-emerald-900/30 rounded-xl transition-all"
                                title={t('mail.signatures.list.setDefault')}
                              >
                                <Star className="w-[18px] h-[18px]" strokeWidth={2} />
                              </button>
                            )}
                            <button
                              onClick={() => openEdit(sig)}
                              className="p-2 text-gray-400 dark:text-slate-500 hover:text-sky-600 dark:hover:text-sky-400 hover:bg-sky-50 dark:hover:bg-sky-900/30 rounded-xl transition-all"
                              title={t('common.edit')}
                            >
                              <Edit2 className="w-[18px] h-[18px]" strokeWidth={2} />
                            </button>
                            <button
                              onClick={() => crud.setDeleteTarget(sig)}
                              className="p-2 text-gray-400 dark:text-slate-500 hover:text-rose-600 dark:hover:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-900/30 rounded-xl transition-all"
                              title={t('common.delete')}
                            >
                              <Trash2 className="w-[18px] h-[18px]" strokeWidth={2} />
                            </button>
                          </div>
                        </div>

                        {/* 所属用户 */}
                        <div className="mt-2.5 flex items-center gap-2">
                          <UserAvatar userId={sig.userId} name={sig.userName} size="sm" />
                          <div className="min-w-0">
                            <div className="font-medium text-gray-700 dark:text-slate-200 truncate">{sig.userName}</div>
                            <div className="text-[11px] text-gray-400 dark:text-slate-500 truncate">{sig.userEmail}</div>
                          </div>
                        </div>

                        {/* 标签组：适用范围 / 状态 / 排序 */}
                        <div className="mt-2.5 flex items-center gap-1.5 flex-wrap">
                          <span
                            className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider ring-1 ${
                              isGlobal
                                ? 'bg-gradient-to-br from-violet-50 to-fuchsia-50 dark:from-violet-900/30 dark:to-fuchsia-900/20 text-violet-700 dark:text-violet-300 ring-violet-500/20'
                                : 'bg-gradient-to-br from-sky-50 to-blue-50 dark:from-sky-900/30 dark:to-blue-900/20 text-sky-700 dark:text-sky-300 ring-sky-500/20'
                            }`}
                          >
                            {isGlobal ? <Globe className="w-3 h-3" /> : <Mail className="w-3 h-3" />}
                            {isGlobal ? t('mail.common.global') : sig.mailboxName}
                          </span>
                          {sig.isDefault ? (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-gradient-to-br from-emerald-50 to-teal-50 dark:from-emerald-900/30 dark:to-teal-900/20 text-emerald-700 dark:text-emerald-400 text-[10px] font-bold uppercase tracking-wider ring-1 ring-emerald-500/20">
                              <CheckCircle2 className="w-3 h-3" />
                              {t('mail.signatures.list.statusDefault')}
                            </span>
                          ) : (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-gray-100 dark:bg-slate-700/50 text-gray-600 dark:text-slate-300 text-[10px] font-bold uppercase tracking-wider ring-1 ring-gray-500/10">
                              {t('mail.signatures.list.statusNormal')}
                            </span>
                          )}
                          <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded-md bg-gray-100 dark:bg-slate-700/50 text-gray-600 dark:text-slate-300 text-[10px] font-bold ring-1 ring-gray-500/10 font-mono">
                            <Hash className="w-3 h-3" />
                            {sig.sortOrder}
                          </span>
                        </div>

                        {/* 创建时间 */}
                        <div className="mt-2 text-[11px] text-gray-400 dark:text-slate-500">
                          {t('mail.common.createdAtPrefix')} {formatDateTime(sig.createdAt)}
                        </div>
                      </div>
                    );
                  })}
                </div>

                {/* 分页 */}
                <div className="px-4 md:px-5 py-3 md:py-3.5 border-t border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50/60 via-white to-gray-50/60 dark:from-slate-900/50 dark:via-slate-800/60 dark:to-slate-900/50 flex flex-wrap items-center gap-3 md:gap-4 justify-between">
                  <div className="inline-flex items-center gap-2 text-xs text-gray-500 dark:text-slate-400 flex-wrap">
                    <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg bg-gradient-to-br from-primary-50 to-indigo-50 dark:from-primary-900/30 dark:to-indigo-900/20 text-primary-700 dark:text-primary-300 ring-1 ring-primary-500/20 font-bold">
                      <Inbox className="w-3.5 h-3.5" />
                      {t('mail.common.trashFooterTotalPrefix')}<b className="font-black ml-0.5">{totalItems}</b>{t('mail.signatures.list.totalSuffix')}
                    </span>
                    <span className="text-gray-400 dark:text-slate-500">{t('mail.common.trashRangePrefix')}</span>
                    <b className="font-black text-gray-700 dark:text-slate-200 tabular-nums">
                      {totalItems === 0 ? 0 : (page - 1) * pageSize + 1}
                    </b>
                    <span className="text-gray-400 dark:text-slate-500">{t('mail.common.trashRangeSeparator')}</span>
                    <b className="font-black text-gray-700 dark:text-slate-200 tabular-nums">
                      {Math.min(page * pageSize, totalItems)}
                    </b>
                    <span className="text-gray-400 dark:text-slate-500">{t('mail.common.trashRangeSuffix')}</span>
                  </div>
                  <div className="flex items-center gap-1.5 ml-auto">
                    <button
                      onClick={() => crud.handlePageChange(Math.max(1, page - 1))}
                      disabled={page <= 1}
                      className="inline-flex items-center justify-center w-9 h-9 rounded-xl bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-600 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-700/60 disabled:opacity-40 disabled:cursor-not-allowed shadow-sm transition-all"
                    >
                      <ChevronLeft className="w-4 h-4" strokeWidth={2} />
                    </button>
                    <div className="inline-flex items-center gap-1 px-2.5 py-1.5 rounded-xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white text-xs font-black shadow-sm shadow-primary-500/20 ring-1 ring-primary-500/20 tabular-nums">
                      {page} / {totalPages}
                    </div>
                    <button
                      onClick={() => crud.handlePageChange(Math.min(totalPages, page + 1))}
                      disabled={page >= totalPages}
                      className="inline-flex items-center justify-center w-9 h-9 rounded-xl bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-600 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-700/60 disabled:opacity-40 disabled:cursor-not-allowed shadow-sm transition-all"
                    >
                      <ChevronRight className="w-4 h-4" strokeWidth={2} />
                    </button>
                  </div>
                </div>
              </>
            ) : (
              <EmptyState
                icon={PenLine}
                title={t('mail.signatures.overview.emptyTitle')}
                subtitle={t('mail.signatures.overview.emptySubtitle')}
                action={
                  <button onClick={openCreate} className={btnPrimary}>
                    <Plus className="w-4 h-4" />
                    {t('mail.signatures.page.new')}
                  </button>
                }
              />
            )}
          </div>
        </div>
      )}

      {/* 编辑 Modal */}
      <Modal
        open={crud.showEditor}
        onClose={() => crud.closeEditor()}
        title={crud.editing ? t('mail.signatures.editor.titleEdit') : t('mail.signatures.editor.titleCreate')}
        size="xl"
      >
        <div className="space-y-5">
          {/* 渐变头部说明卡 */}
          <div className="relative rounded-2xl bg-gradient-to-br from-primary-50 via-white to-indigo-50 dark:from-slate-900 dark:via-slate-800/60 dark:to-indigo-900/20 border border-primary-100 dark:border-slate-700/60 p-4 md:p-5 overflow-hidden">
            <div className="pointer-events-none absolute -top-16 -right-16 w-44 h-44 rounded-full bg-primary-500/15 blur-3xl" />
            <div className="relative flex items-start gap-4">
              <div className="shrink-0 w-11 h-11 rounded-2xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-md shadow-primary-500/30 flex items-center justify-center ring-1 ring-white/40 dark:ring-white/10">
                <PenLine className="w-5 h-5" strokeWidth={2} />
              </div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <h4 className="text-sm md:text-base font-extrabold tracking-tight text-gray-900 dark:text-slate-50">
                    {crud.editing ? t('mail.signatures.editor.headerEdit') : t('mail.signatures.editor.headerCreate')}
                  </h4>
                  <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full bg-gradient-to-br from-primary-100 to-indigo-100 dark:from-primary-900/40 dark:to-indigo-900/40 text-primary-700 dark:text-primary-300 text-[10px] font-bold uppercase tracking-wider ring-1 ring-primary-500/20">
                    <Sparkles className="w-3 h-3" />
                    {crud.editing ? t('mail.signatures.editor.badgeEdit') : t('mail.signatures.editor.badgeCreate')}
                  </span>
                </div>
                <p className="text-[11px] md:text-xs text-gray-500 dark:text-slate-400 mt-1 leading-relaxed">
                  {t('mail.signatures.editor.headerHintPrefix')}<b className="font-semibold text-primary-600 dark:text-primary-400">{t('mail.signatures.editor.headerHintBold')}</b>{t('mail.signatures.editor.headerHintSuffix')}
                </p>
              </div>
            </div>
          </div>

          {formError && (
            <div className="p-3 bg-rose-50 dark:bg-rose-900/20 border border-rose-200 dark:border-rose-800 text-rose-700 dark:text-rose-400 text-sm rounded-xl flex items-center gap-2">
              <AlertCircle className="w-5 h-5 flex-shrink-0" strokeWidth={2} />
              {formError}
            </div>
          )}

          {/* 基本信息 */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">
                {t('mail.signatures.field.name')} <span className="text-rose-500">*</span>
              </label>
              <input
                type="text"
                value={formData.name}
                onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                className={inputBase}
                placeholder={t('mail.signatures.editor.namePlaceholder')}
              />
            </div>
            <div>
              <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">
                {t('mail.common.applicableMailbox')}
              </label>
              <select
                value={formData.mailboxId ?? ''}
                onChange={(e) => setFormData({ ...formData, mailboxId: e.target.value === '' ? null : Number(e.target.value) })}
                className={inputBase}
              >
                <option value="">{t('mail.common.globalAllMailboxes')}</option>
                {mailboxes.map((m) => (
                  <option key={m.id} value={m.id}>{m.name}</option>
                ))}
              </select>
            </div>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">
                {t('mail.signatures.field.sortOrder')}
              </label>
              <input
                type="number"
                value={formData.sortOrder}
                onChange={(e) => setFormData({ ...formData, sortOrder: Number(e.target.value) })}
                className={inputBase}
              />
            </div>
            <div>
              <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">
                {t('mail.signatures.editor.defaultStatusLabel')}
              </label>
              <button
                type="button"
                onClick={() => setFormData({ ...formData, isDefault: !formData.isDefault })}
                className={`w-full px-4 py-2.5 rounded-xl border-2 transition-all flex items-center justify-between ${
                  formData.isDefault
                    ? 'border-emerald-400 dark:border-emerald-500 bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-400'
                    : 'border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-900/80 text-gray-600 dark:text-slate-300 hover:border-primary-300'
                }`}
              >
                <span className="text-sm font-bold flex items-center gap-2">
                  <Star className={`w-4 h-4 ${formData.isDefault ? 'fill-emerald-500 text-emerald-500' : ''}`} strokeWidth={2} />
                  {formData.isDefault ? t('mail.signatures.editor.enableDefault') : t('mail.signatures.editor.keepNormal')}
                </span>
                <div className={`relative w-10 h-6 rounded-full transition-colors ${formData.isDefault ? 'bg-emerald-500' : 'bg-gray-300 dark:bg-slate-600'}`}>
                  <div className={`absolute top-0.5 left-0.5 w-5 h-5 bg-white rounded-full shadow-sm transition-transform ${formData.isDefault ? 'translate-x-4' : ''}`} />
                </div>
              </button>
            </div>
          </div>

          {/* 签名内容 */}
          <div>
            <div className="flex items-center justify-between mb-1.5">
              <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400">
                {t('mail.signatures.editor.bodyLabel')} <span className="text-rose-500">*</span>
              </label>
              <span className="inline-flex items-center gap-1 text-[11px] text-gray-400 dark:text-slate-500 font-bold">
                <Zap className="w-3 h-3" />
                {t('mail.signatures.editor.bodyFormatHint')}
              </span>
            </div>
            <textarea
              value={formData.body}
              onChange={(e) => setFormData({ ...formData, body: e.target.value })}
              rows={8}
              className={`${inputBase} font-mono resize-y min-h-[160px]`}
              placeholder={t('mail.signatures.editor.bodyPlaceholder')}
            />
          </div>

          {/* 实时预览 */}
          <div className="rounded-2xl bg-gradient-to-br from-slate-50 to-gray-50 dark:from-slate-900/50 dark:to-slate-800/50 border border-gray-200 dark:border-slate-700/60 p-4">
            <p className="text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-2 flex items-center gap-1.5">
              <Eye className="w-3.5 h-3.5" />
              {t('mail.signatures.editor.previewTitle')}
            </p>
            <div className="border border-gray-200 dark:border-slate-700 rounded-xl p-5 bg-white dark:bg-slate-800">
              <p className="text-sm text-gray-500 dark:text-slate-400 mb-3">{t('mail.signatures.editor.previewBodySample')}</p>
              <div className="border-t border-gray-200 dark:border-slate-700 pt-4">
                <div
                  className="text-gray-800 dark:text-slate-200"
                  dangerouslySetInnerHTML={{ __html: formData.body ? sanitizeHtml(formData.body) : `<span class="text-gray-400 dark:text-slate-500">${t('mail.signatures.editor.previewEmpty')}</span>` }}
                />
              </div>
            </div>
          </div>

          {/* 操作按钮 */}
          <div className="flex justify-end gap-3 pt-4 border-t border-gray-100 dark:border-slate-700/60">
            <button onClick={() => crud.closeEditor()} className={btnSecondary}>
              <X className="w-4 h-4" />
              {t('common.cancel')}
            </button>
            <button
              onClick={handleSave}
              disabled={saving}
              className={btnPrimary}
            >
              {saving ? (
                <>
                  <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                  {t('mail.signatures.editor.saving')}
                </>
              ) : (
                <>
                  <CheckCircle2 className="w-4 h-4" />
                  {t('common.save')}
                </>
              )}
            </button>
          </div>
        </div>
      </Modal>

      {/* 预览 Modal */}
      <Modal
        open={showPreview}
        onClose={() => setShowPreview(false)}
        title={t('mail.signatures.preview.title')}
        size="md"
      >
        <div className="space-y-4">
          {/* 头部信息卡 */}
          <div className="relative rounded-2xl bg-gradient-to-br from-primary-50 via-white to-indigo-50 dark:from-slate-900 dark:via-slate-800/60 dark:to-indigo-900/20 border border-primary-100 dark:border-slate-700/60 p-4 overflow-hidden">
            <div className="pointer-events-none absolute -top-12 -right-12 w-32 h-32 rounded-full bg-primary-500/15 blur-3xl" />
            <div className="relative flex items-center gap-3">
              <div className="shrink-0 w-12 h-12 rounded-2xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-md shadow-primary-500/30 flex items-center justify-center ring-1 ring-white/40 dark:ring-white/10">
                <PenLine className="w-6 h-6" strokeWidth={2} />
              </div>
              <div className="flex-1 min-w-0">
                <h4 className="font-extrabold text-gray-900 dark:text-slate-50 tracking-tight">{previewSignature?.name}</h4>
                <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 flex items-center gap-1.5">
                  <User2 className="w-3 h-3 opacity-70" />
                  {previewSignature?.userName}
                  <span className="text-gray-300 dark:text-slate-600">·</span>
                  {previewSignature?.mailboxName ? (
                    <span className="inline-flex items-center gap-1 text-sky-600 dark:text-sky-400 font-medium">
                      <Mail className="w-3 h-3" />
                      {previewSignature.mailboxName}
                    </span>
                  ) : (
                    <span className="inline-flex items-center gap-1 text-violet-600 dark:text-violet-400 font-medium">
                      <Globe className="w-3 h-3" />
                      {t('mail.common.global')}
                    </span>
                  )}
                </p>
              </div>
              {previewSignature?.isDefault && (
                <span className="inline-flex items-center gap-1 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider rounded-full bg-gradient-to-br from-emerald-50 to-teal-50 dark:from-emerald-900/30 dark:to-teal-900/20 text-emerald-700 dark:text-emerald-400 ring-1 ring-emerald-500/20">
                  <Star className="w-3 h-3" />
                  {t('mail.signatures.badge.default')}
                </span>
              )}
            </div>
          </div>

          {/* 邮件预览窗口 */}
          <div className="rounded-2xl border border-gray-200 dark:border-slate-700/60 overflow-hidden shadow-sm">
            {/* 浏览器窗口风格 */}
            <div className="px-4 py-2.5 bg-gray-100 dark:bg-slate-900/60 border-b border-gray-200 dark:border-slate-700/60 flex items-center gap-1.5">
              <div className="w-3 h-3 rounded-full bg-rose-400" />
              <div className="w-3 h-3 rounded-full bg-amber-400" />
              <div className="w-3 h-3 rounded-full bg-emerald-400" />
              <div className="flex-1 text-center text-[11px] text-gray-500 dark:text-slate-400 font-mono ml-2">
                {t('mail.signatures.preview.windowTitle')}
              </div>
            </div>
            <div className="p-5 bg-white dark:bg-slate-800">
              <p className="text-gray-700 dark:text-slate-300 mb-4">{t('mail.signatures.preview.greeting')}</p>
              <p className="text-gray-700 dark:text-slate-300 mb-4">{t('mail.signatures.preview.paragraph1')}</p>
              <p className="text-gray-700 dark:text-slate-300 mb-6">{t('mail.signatures.preview.paragraph2')}</p>
              <div className="border-t border-gray-200 dark:border-slate-700 pt-4">
                <div
                  className="text-gray-800 dark:text-slate-200"
                  dangerouslySetInnerHTML={{ __html: sanitizeHtml(previewSignature?.body || '') }}
                />
              </div>
            </div>
          </div>

          <div className="flex justify-end">
            <button onClick={() => setShowPreview(false)} className={btnPrimary}>
              <X className="w-4 h-4" />
              {t('common.close')}
            </button>
          </div>
        </div>
      </Modal>

      {/* 删除确认 */}
      <ConfirmDialog
        open={!!crud.deleteTarget}
        onCancel={() => crud.setDeleteTarget(null)}
        onConfirm={handleDelete}
        title={t('mail.signatures.delete.title')}
        message={t('mail.signatures.delete.message', { name: crud.deleteTarget?.name ?? '' })}
        confirmText={t('common.delete')}
        variant="danger"
        loading={deleting}
      />

      <EmailSignatureTrashModal
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
        users={users}
      />
    </div>
  );
}
