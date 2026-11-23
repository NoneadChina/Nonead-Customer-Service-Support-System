import { useCallback, useEffect, useState, FormEvent, useMemo, useRef } from 'react';
import { mailboxApi, userApi } from '../api';
import type { MailboxResponse, CreateMailboxRequest, UserResponse } from '../api/types';
import { formatDate, formatDateTime } from '../utils/format';
import { getApiErrorMessage } from '../utils/error';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import UserAvatar from '../components/UserAvatar';
import { useMailboxes } from '../context/MailboxContext';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import type { TranslateParams } from '../i18n/types';
import {
  Mail,
  Mailbox as MailboxIcon,
  Plus,
  Search,
  Edit2,
  Trash2,
  X,
  Inbox,
  Activity,
  Layers,
  Send,
  DownloadCloud,
  RefreshCw,
  Power,
  PowerOff,
  Zap,
  CheckCircle2,
  XCircle,
  AlertCircle,
  Filter,
  Clock,
  Users,
  KeyRound,
  AtSign,
  Reply,
  FileSignature,
  Info,
  type LucideIcon,
} from 'lucide-react';

/**
 * 加密方式下拉：按**后端枚举**绑定文案。
 *
 * 后端 `NoneadCSS.Domain/Enums/EncryptionType.cs` 是 `None = 0`、`Tls = 1`、`Ssl = 2`，
 * 且 `EmailService.GetSocketOptions` 把 `Tls → StartTls`、`Ssl → SslOnConnect` —— **枚举是权威**。
 * ⚠ 波次 9 交付时这里 1/2 的文案与后端**对调**（界面把 2 显示成 TLS，后端语义是 Ssl；
 * 开发库 3 个邮箱收发加密全为 2）；波次 9 验收已按后端语义**修正文案绑定**（只改显示、不改入库值）。
 */
const encryptionOptions: { value: number; labelKey: TranslationKey }[] = [
  { value: 0, labelKey: 'mail.mailboxes.encryption.none' },
  { value: 1, labelKey: 'mail.mailboxes.encryption.tls' },
  { value: 2, labelKey: 'mail.mailboxes.encryption.ssl' },
];

interface FormState {
  name: string;
  email: string;
  inHost: string;
  inPort: number;
  inEncryption: number;
  inValidateCert: boolean;
  inUsername: string;
  inPassword: string;
  imapSentFolder: string;
  outHost: string;
  outPort: number;
  outEncryption: number;
  outUsername: string;
  outPassword: string;
  aliases: string;
  aliasesReply: string;
  autoBcc: string;
  autoReplyEnabled: boolean;
  autoReplySubject: string;
  autoReplyMessage: string;
  beforeReply: string;
  signature: string;
  oauthEnabled: boolean;
  oauthProvider: string;
  oauthAccessToken: string;
  oauthRefreshToken: string;
  isActive: boolean;
  userIds: number[];
  fetchFrequency: number;
}

const emptyForm: FormState = {
  name: '',
  email: '',
  inHost: '',
  inPort: 993,
  inEncryption: 2,
  inValidateCert: true,
  inUsername: '',
  inPassword: '',
  imapSentFolder: '',
  outHost: '',
  outPort: 465,
  outEncryption: 2,
  outUsername: '',
  outPassword: '',
  aliases: '',
  aliasesReply: '',
  autoBcc: '',
  autoReplyEnabled: false,
  autoReplySubject: '',
  autoReplyMessage: '',
  beforeReply: '',
  signature: '',
  oauthEnabled: false,
  oauthProvider: '',
  oauthAccessToken: '',
  oauthRefreshToken: '',
  isActive: true,
  userIds: [],
  fetchFrequency: 5,
};

// 通用按钮样式
const btnPrimary =
  'inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold text-white rounded-xl bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 shadow-sm shadow-primary-500/20 hover:shadow hover:shadow-primary-500/30 transition-all disabled:opacity-50 ring-1 ring-primary-500/20';
const btnSecondary =
  'inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold text-gray-700 dark:text-slate-200 rounded-xl bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 hover:bg-gray-50 dark:hover:bg-slate-700/60 transition-all disabled:opacity-50 shadow-sm';
const btnDangerSoft =
  'inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-bold rounded-xl bg-gradient-to-br from-rose-50 to-red-50 dark:from-rose-900/30 dark:to-red-900/20 text-rose-700 dark:text-rose-300 ring-1 ring-rose-500/20 hover:from-rose-100 hover:to-red-100 dark:hover:from-rose-900/50 dark:hover:to-red-900/40 transition-all disabled:opacity-50';

const inputBase =
  'w-full px-3.5 py-2.5 text-sm bg-white dark:bg-slate-900/80 border border-gray-200 dark:border-slate-700 rounded-xl text-gray-800 dark:text-slate-200 placeholder:text-gray-400 dark:placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-400 dark:focus:border-primary-500 shadow-sm transition-all';
const selectBase =
  'w-full px-3.5 py-2.5 text-sm bg-white dark:bg-slate-900/80 border border-gray-200 dark:border-slate-700 rounded-xl text-gray-800 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-400 dark:focus:border-primary-500 shadow-sm transition-all cursor-pointer';

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
    <div className="py-14 flex flex-col items-center text-gray-400">
      <div className="w-24 h-24 rounded-3xl bg-gradient-to-br from-gray-50 to-gray-100 dark:from-slate-800 dark:to-slate-900 border border-gray-100 dark:border-slate-700 flex items-center justify-center mb-4 shadow-sm">
        <Icon className="w-12 h-12 opacity-30" />
      </div>
      <p className="text-sm font-semibold text-gray-500 dark:text-slate-400">{title}</p>
      <p className="text-xs mt-1 text-gray-400 dark:text-slate-500">{subtitle}</p>
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}

// 通用：加载态
// text 改必填：原先的中文默认值是不可达的死值（唯一调用点始终显式传文案），
// 删掉默认值即可，不必为一个到不了的分支单独造词条
function LoadingState({ text }: { text: string }) {
  return (
    <div className="flex items-center justify-center gap-3 py-14">
      <div className="w-10 h-10 border-4 border-primary-200 dark:border-primary-900 border-t-primary-600 dark:border-t-primary-400 rounded-full animate-spin shadow-sm" />
      <span className="text-sm font-medium text-gray-500 dark:text-slate-400">{text}</span>
    </div>
  );
}

// 表单分区头
function SectionHeader({
  icon: Icon,
  title,
  subtitle,
  color,
}: {
  icon: LucideIcon;
  title: string;
  subtitle?: string;
  color: string;
}) {
  return (
    <div className="flex items-center gap-2.5">
      <div className={`shrink-0 w-8 h-8 rounded-lg bg-gradient-to-br ${color} text-white flex items-center justify-center shadow-sm ring-1 ring-white/30 dark:ring-white/10`}>
        <Icon className="w-4 h-4" strokeWidth={2} />
      </div>
      <div className="min-w-0">
        <h4 className="text-sm font-extrabold tracking-tight text-gray-900 dark:text-slate-50">{title}</h4>
        {subtitle && <p className="text-[11px] text-gray-500 dark:text-slate-400 mt-0.5">{subtitle}</p>}
      </div>
    </div>
  );
}

// 邮箱卡片头像渐变（按 id 取模选择，确保视觉稳定）
const CARD_PALETTES = [
  { from: 'from-primary-500', to: 'to-indigo-500', bg: 'bg-primary-50 dark:bg-primary-900/20', text: 'text-primary-700 dark:text-primary-300', ring: 'ring-primary-500/20' },
  { from: 'from-sky-500', to: 'to-blue-500', bg: 'bg-sky-50 dark:bg-sky-900/20', text: 'text-sky-700 dark:text-sky-300', ring: 'ring-sky-500/20' },
  { from: 'from-emerald-500', to: 'to-teal-500', bg: 'bg-emerald-50 dark:bg-emerald-900/20', text: 'text-emerald-700 dark:text-emerald-300', ring: 'ring-emerald-500/20' },
  { from: 'from-violet-500', to: 'to-fuchsia-500', bg: 'bg-violet-50 dark:bg-violet-900/20', text: 'text-violet-700 dark:text-violet-300', ring: 'ring-violet-500/20' },
  { from: 'from-amber-500', to: 'to-orange-500', bg: 'bg-amber-50 dark:bg-amber-900/20', text: 'text-amber-700 dark:text-amber-300', ring: 'ring-amber-500/20' },
  { from: 'from-rose-500', to: 'to-pink-500', bg: 'bg-rose-50 dark:bg-rose-900/20', text: 'text-rose-700 dark:text-rose-300', ring: 'ring-rose-500/20' },
  { from: 'from-cyan-500', to: 'to-sky-500', bg: 'bg-cyan-50 dark:bg-cyan-900/20', text: 'text-cyan-700 dark:text-cyan-300', ring: 'ring-cyan-500/20' },
  { from: 'from-lime-500', to: 'to-green-500', bg: 'bg-lime-50 dark:bg-lime-900/20', text: 'text-lime-700 dark:text-lime-300', ring: 'ring-lime-500/20' },
];

function getCardPalette(id: number) {
  return CARD_PALETTES[id % CARD_PALETTES.length];
}

export default function Mailboxes() {
  const [list, setList] = useState<MailboxResponse[]>([]);
  const [trashList, setTrashList] = useState<MailboxResponse[]>([]);
  const [users, setUsers] = useState<UserResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [activeTab, setActiveTab] = useState<'list' | 'trash'>('list');

  const [searchInput, setSearchInput] = useState('');
  const [searchKeyword, setSearchKeyword] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'inactive'>('all');

  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [editingMailbox, setEditingMailbox] = useState<MailboxResponse | null>(null);
  const [form, setForm] = useState<FormState>(emptyForm);
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState('');

  const [deleteTarget, setDeleteTarget] = useState<MailboxResponse | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | undefined>(undefined);
  const [testingId, setTestingId] = useState<number | null>(null);
  const [testResult, setTestResult] = useState<{ id: number; success: boolean; message?: string } | null>(null);

  const [restoreTarget, setRestoreTarget] = useState<MailboxResponse | null>(null);
  const [restoring, setRestoring] = useState(false);
  const [hardDeleteTarget, setHardDeleteTarget] = useState<MailboxResponse | null>(null);
  const [hardDeleting, setHardDeleting] = useState(false);
  const [clearTrashOpen, setClearTrashOpen] = useState(false);
  const [clearingTrash, setClearingTrash] = useState(false);

  const { t } = useI18n();
  const { refreshMailboxes } = useMailboxes();
  const searchInputRef = useRef<HTMLInputElement>(null);

  const fetchMailboxes = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const [mbResp, userResp, trashResp] = await Promise.all([
        mailboxApi.listAll(),
        userApi.list(),
        mailboxApi.getTrash(),
      ]);
      setList(Array.isArray(mbResp) ? mbResp : []);
      setUsers(Array.isArray(userResp?.data) ? userResp.data : []);
      setTrashList(Array.isArray(trashResp) ? trashResp : []);
      refreshMailboxes();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, t('mail.common.loadFailed')));
    } finally {
      setLoading(false);
    }
  }, [refreshMailboxes, t]);

  useEffect(() => {
    fetchMailboxes();
  }, [fetchMailboxes]);

  const currentList = activeTab === 'list' ? list : trashList;

  const filteredList = useMemo(() => {
    return currentList.filter((m) => {
      const matchSearch =
        !searchKeyword ||
        m.name.toLowerCase().includes(searchKeyword.toLowerCase()) ||
        m.email.toLowerCase().includes(searchKeyword.toLowerCase());
      const matchStatus =
        statusFilter === 'all' ? true : statusFilter === 'active' ? m.isActive : !m.isActive;
      return matchSearch && matchStatus;
    });
  }, [currentList, searchKeyword, statusFilter]);

  const handleSearch = () => setSearchKeyword(searchInput.trim());
  const handleResetSearch = () => {
    setSearchInput('');
    setSearchKeyword('');
    setStatusFilter('all');
  };

  const openCreate = () => {
    setEditingId(null);
    setEditingMailbox(null);
    setForm(emptyForm);
    setFormError('');
    setModalOpen(true);
  };

  const openEdit = (m: MailboxResponse) => {
    setEditingId(m.id);
    setEditingMailbox(m);
    setForm({
      name: m.name,
      email: m.email,
      inHost: m.inHost ?? '',
      inPort: m.inPort ?? 993,
      inEncryption: m.inEncryption ?? 2,
      inValidateCert: m.inValidateCert ?? true,
      inUsername: m.inUsername ?? '',
      inPassword: '',
      imapSentFolder: m.imapSentFolder ?? '',
      outHost: m.outHost ?? '',
      outPort: m.outPort ?? 465,
      outEncryption: m.outEncryption ?? 2,
      outUsername: m.outUsername ?? '',
      outPassword: '',
      aliases: m.aliases ?? '',
      aliasesReply: m.aliasesReply ?? '',
      autoBcc: m.autoBcc ?? '',
      autoReplyEnabled: m.autoReplyEnabled ?? false,
      autoReplySubject: m.autoReplySubject ?? '',
      autoReplyMessage: m.autoReplyMessage ?? '',
      beforeReply: m.beforeReply ?? '',
      signature: m.signature ?? '',
      oauthEnabled: m.oauthEnabled ?? false,
      oauthProvider: m.oauthProvider ?? '',
      oauthAccessToken: '',
      oauthRefreshToken: '',
      isActive: m.isActive,
      userIds: m.userIds ?? [],
      fetchFrequency: m.fetchFrequency && m.fetchFrequency >= 1 && m.fetchFrequency <= 60 ? m.fetchFrequency : 5,
    });
    setFormError('');
    setModalOpen(true);
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!form.name.trim()) {
      setFormError(t('mail.mailboxes.validation.nameRequired'));
      return;
    }
    if (!form.email.trim()) {
      setFormError(t('mail.mailboxes.validation.emailRequired'));
      return;
    }
    if (!editingId) {
      if (!form.inHost.trim() || !form.outHost.trim()) {
        setFormError(t('mail.mailboxes.validation.hostRequired'));
        return;
      }
    }

    const payload: CreateMailboxRequest = {
      name: form.name.trim(),
      email: form.email.trim(),
      inHost: form.inHost.trim(),
      inPort: form.inPort,
      inEncryption: form.inEncryption,
      inValidateCert: form.inValidateCert,
      inUsername: form.inUsername.trim(),
      inPassword: form.inPassword,
      imapSentFolder: form.imapSentFolder.trim(),
      outHost: form.outHost.trim(),
      outPort: form.outPort,
      outEncryption: form.outEncryption,
      outUsername: form.outUsername.trim(),
      outPassword: form.outPassword,
      aliases: form.aliases.trim(),
      aliasesReply: form.aliasesReply.trim(),
      autoBcc: form.autoBcc.trim(),
      autoReplyEnabled: form.autoReplyEnabled,
      autoReplySubject: form.autoReplySubject.trim(),
      autoReplyMessage: form.autoReplyMessage.trim(),
      beforeReply: form.beforeReply.trim(),
      signature: form.signature.trim(),
      oauthEnabled: form.oauthEnabled,
      oauthProvider: form.oauthProvider.trim(),
      oauthAccessToken: form.oauthAccessToken,
      oauthRefreshToken: form.oauthRefreshToken,
      oauthTokenExpiresAt: null,
      isActive: form.isActive,
      userIds: form.userIds,
      fetchFrequency: form.fetchFrequency,
    };

    setSubmitting(true);
    setFormError('');
    try {
      if (editingId) {
        await mailboxApi.update(editingId, payload);
      } else {
        await mailboxApi.create(payload);
      }
      setModalOpen(false);
      await fetchMailboxes();
    } catch (err: unknown) {
      setFormError(getApiErrorMessage(err, t('mail.common.operationFailed')));
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    setDeleteError(undefined);
    try {
      await mailboxApi.remove(deleteTarget.id);
      setDeleteTarget(null);
      await fetchMailboxes();
    } catch (err: unknown) {
      setDeleteError(getApiErrorMessage(err, t('mail.common.deleteFailed')));
    } finally {
      setDeleting(false);
    }
  };

  const handleTestConnection = async (id: number) => {
    setTestingId(id);
    setTestResult(null);
    try {
      const result = await mailboxApi.testConnection(id);
      setTestResult({ id, success: result.success, message: result.message });
    } catch (err: unknown) {
      setTestResult({ id, success: false, message: getApiErrorMessage(err, t('mail.mailboxes.requestFailed')) });
    } finally {
      setTestingId(null);
    }
  };

  const handleToggleActive = async (id: number) => {
    try {
      await mailboxApi.toggleActive(id);
      await fetchMailboxes();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, t('mail.common.operationFailed')));
    }
  };

  const handleRestore = async () => {
    if (!restoreTarget) return;
    setRestoring(true);
    try {
      await mailboxApi.restore(restoreTarget.id);
      setRestoreTarget(null);
      await fetchMailboxes();
    } catch (err: unknown) {
      setFormError(getApiErrorMessage(err, t('mail.common.restoreFailed')));
    } finally {
      setRestoring(false);
    }
  };

  const handleHardDelete = async () => {
    if (!hardDeleteTarget) return;
    setHardDeleting(true);
    try {
      await mailboxApi.hardDelete(hardDeleteTarget.id);
      setHardDeleteTarget(null);
      await fetchMailboxes();
    } catch (err: unknown) {
      setFormError(getApiErrorMessage(err, t('mail.common.hardDeleteFailed')));
    } finally {
      setHardDeleting(false);
    }
  };

  const handleClearTrash = async () => {
    setClearingTrash(true);
    try {
      await mailboxApi.clearTrash();
      setClearTrashOpen(false);
      await fetchMailboxes();
    } catch (err: unknown) {
      setFormError(getApiErrorMessage(err, t('mail.common.clearTrashFailed')));
    } finally {
      setClearingTrash(false);
    }
  };

  const toggleUser = (id: number) => {
    setForm((prev) => ({
      ...prev,
      userIds: prev.userIds.includes(id) ? prev.userIds.filter((x) => x !== id) : [...prev.userIds, id],
    }));
  };

  // 键盘快捷键
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
        e.preventDefault();
        searchInputRef.current?.focus();
        return;
      }
      if ((e.ctrlKey || e.metaKey) && e.key === 'n' && !['INPUT', 'TEXTAREA', 'SELECT'].includes((e.target as HTMLElement).tagName)) {
        e.preventDefault();
        openCreate();
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
     
  }, []);

  // 统计数据
  const activeCount = list.filter((m) => m.isActive).length;
  const inactiveCount = list.length - activeCount;
  const totalConversations = list.reduce((sum, m) => sum + (m.conversationCount ?? 0), 0);
  const totalTodaySend = list.reduce((sum, m) => sum + (m.todaySendCount ?? 0), 0);

  const statItems = useMemo(() => {
    // 承载文案的字段一律持 key（labelKey / hintKey），渲染处经 t()——直接渲染会把原始 key 漏到界面。
    // 表本身不调用 t()，故依赖数组不含 t。
    const items: {
      labelKey: TranslationKey;
      value: number;
      hintKey: TranslationKey;
      hintParams?: TranslateParams;
      icon: LucideIcon;
      palette: {
        from: string;
        to: string;
        textLight: string;
        bgSoft: string;
        ringSoft: string;
      };
    }[] = [
      {
        labelKey: 'mail.mailboxes.stat.total',
        value: list.length,
        hintKey: 'mail.mailboxes.stat.totalHint',
        icon: MailboxIcon,
        palette: { from: 'from-primary-500', to: 'to-indigo-500', textLight: 'text-primary-600 dark:text-primary-400', bgSoft: 'bg-primary-50 dark:bg-primary-900/30', ringSoft: 'ring-primary-500/20' },
      },
      {
        labelKey: 'mail.mailboxes.stat.active',
        value: activeCount,
        hintKey: 'mail.mailboxes.stat.activeHint',
        hintParams: { count: inactiveCount },
        icon: Activity,
        palette: { from: 'from-emerald-500', to: 'to-teal-500', textLight: 'text-emerald-600 dark:text-emerald-400', bgSoft: 'bg-emerald-50 dark:bg-emerald-900/30', ringSoft: 'ring-emerald-500/20' },
      },
      {
        labelKey: 'mail.mailboxes.stat.conversations',
        value: totalConversations,
        hintKey: 'mail.mailboxes.stat.conversationsHint',
        icon: Layers,
        palette: { from: 'from-sky-500', to: 'to-blue-500', textLight: 'text-sky-600 dark:text-sky-400', bgSoft: 'bg-sky-50 dark:bg-sky-900/30', ringSoft: 'ring-sky-500/20' },
      },
      {
        labelKey: 'mail.mailboxes.stat.todaySent',
        value: totalTodaySend,
        hintKey: 'mail.mailboxes.stat.todaySentHint',
        icon: Send,
        palette: { from: 'from-violet-500', to: 'to-fuchsia-500', textLight: 'text-violet-600 dark:text-violet-400', bgSoft: 'bg-violet-50 dark:bg-violet-900/30', ringSoft: 'ring-violet-500/20' },
      },
    ];
    return items;
  }, [list.length, activeCount, inactiveCount, totalConversations, totalTodaySend]);

  // 筛选摘要胶囊
  // chip.label 是**已在表内成句的译文**（搜索胶囊复用队长冻结的 mail.common.searchPrefix
  // 再拼用户输入的关键词），故此处保持 string；调用点只渲染，不存在"把 key 当文案"的漏翻面
  const filterChips = useMemo(() => {
    const chips: { label: string; onClear: () => void }[] = [];
    if (searchKeyword.trim()) {
      chips.push({ label: t('mail.common.searchPrefix') + searchKeyword.trim(), onClear: () => { setSearchInput(''); setSearchKeyword(''); } });
    }
    if (statusFilter !== 'all') {
      chips.push({
        label: statusFilter === 'active' ? t('mail.mailboxes.filter.statusActive') : t('mail.mailboxes.filter.statusDisabled'),
        onClear: () => setStatusFilter('all'),
      });
    }
    return chips;
  }, [searchKeyword, statusFilter, t]);

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
                <MailboxIcon className="w-7 h-7" strokeWidth={2} />
              </div>
              <div className="min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <h1 className="text-2xl md:text-3xl font-extrabold tracking-tight text-gray-900 dark:text-slate-50">{t('mail.mailboxes.page.title')}</h1>
                  <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider rounded-full bg-gradient-to-r from-sky-100 to-blue-100 dark:from-sky-900/40 dark:to-blue-900/40 text-sky-700 dark:text-sky-300 ring-1 ring-sky-500/20">
                    <Mail className="w-3 h-3" />
                    {t('mail.mailboxes.page.badge')}
                  </span>
                </div>
                <p className="text-sm md:text-base text-gray-500 dark:text-slate-400 mt-1.5 leading-relaxed">
                  {t('mail.mailboxes.page.subtitle')}
                </p>
              </div>
            </div>
            <div className="flex items-center gap-2.5 flex-wrap">
              <button onClick={fetchMailboxes} disabled={loading} className={btnSecondary} title={t('mail.mailboxes.page.refreshTitle')}>
                <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
                {t('mail.mailboxes.refresh')}
              </button>
              {activeTab === 'trash' && trashList.length > 0 && (
                <button onClick={() => setClearTrashOpen(true)} className={btnDangerSoft}>
                  <Trash2 className="w-3.5 h-3.5" />
                  {t('mail.common.clearTrash')}
                </button>
              )}
              {activeTab === 'list' && (
                <button onClick={openCreate} className={btnPrimary}>
                  <Plus className="w-4 h-4" />
                  {t('mail.mailboxes.newMailbox')}
                </button>
              )}
            </div>
          </div>

          {/* 统计卡片 */}
          {!loading && list.length > 0 && activeTab === 'list' && (
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
                      <p className="text-[10px] text-gray-400 dark:text-slate-500 mt-0.5">{t(stat.hintKey, stat.hintParams)}</p>
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
      <div className="relative overflow-hidden rounded-2xl border border-gray-200 dark:border-slate-700/60 bg-white dark:bg-slate-800/60 shadow-sm p-1.5">
        <div className="flex items-center gap-1.5">
          <button
            onClick={() => { setActiveTab('list'); handleResetSearch(); }}
            className={`group inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold rounded-xl transition-all ${
              activeTab === 'list'
                ? 'bg-gradient-to-br from-primary-600 to-indigo-600 text-white shadow-md shadow-primary-500/20 ring-1 ring-primary-500/20'
                : 'text-gray-600 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-700/60'
            }`}
          >
            <Inbox className={`w-4 h-4 ${activeTab === 'list' ? 'opacity-90' : 'opacity-60'}`} />
            {t('mail.mailboxes.tabAll')}
            <span className={`px-1.5 py-0.5 text-[10px] font-bold rounded-full ${
              activeTab === 'list'
                ? 'bg-white/20 text-white'
                : 'bg-gray-100 dark:bg-slate-700 text-gray-600 dark:text-slate-300'
            }`}>
              {list.length}
            </span>
          </button>
          <button
            onClick={() => { setActiveTab('trash'); handleResetSearch(); }}
            className={`group inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold rounded-xl transition-all ${
              activeTab === 'trash'
                ? 'bg-gradient-to-br from-rose-600 to-red-600 text-white shadow-md shadow-rose-500/20 ring-1 ring-rose-500/20'
                : 'text-gray-600 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-700/60'
            }`}
          >
            <Trash2 className={`w-4 h-4 ${activeTab === 'trash' ? 'opacity-90' : 'opacity-60'}`} />
            {t('mail.common.trash')}
            {trashList.length > 0 && (
              <span className={`px-1.5 py-0.5 text-[10px] font-bold rounded-full ${
                activeTab === 'trash'
                  ? 'bg-white/20 text-white'
                  : 'bg-rose-100 dark:bg-rose-900/30 text-rose-600 dark:text-rose-400'
              }`}>
                {trashList.length}
              </span>
            )}
          </button>
        </div>
      </div>

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
          <button onClick={() => setError('')} className="p-1 text-rose-400 hover:text-rose-600 rounded-md hover:bg-rose-100 dark:hover:bg-rose-900/40 transition">
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {/* 搜索 + 筛选工具栏 */}
      {!loading && (
        <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm p-3 md:p-4 flex items-center gap-2 md:gap-3 flex-wrap">
          <div className="inline-flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mr-0.5">
            <Filter className="w-3.5 h-3.5" />
            {t('mail.mailboxes.filter.label')}
          </div>
          <div className="relative flex-1 min-w-[200px] max-w-[420px]">
            <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
            <input
              ref={searchInputRef}
              type="text"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
              placeholder={t('mail.mailboxes.searchPlaceholder')}
              className="w-full pl-10 pr-20 py-2 text-sm bg-slate-50 dark:bg-slate-900/60 border border-gray-200 dark:border-slate-700 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-400 dark:focus:border-primary-500 text-gray-800 dark:text-slate-200 placeholder:text-gray-400 dark:placeholder:text-slate-500 transition-all shadow-sm"
            />
            <kbd className="hidden sm:inline-flex absolute right-2 top-1/2 -translate-y-1/2 px-1.5 py-0.5 text-[10px] font-mono text-gray-500 dark:text-slate-400 bg-gray-100 dark:bg-slate-700 border border-gray-200 dark:border-slate-600 rounded">
              Ctrl+K
            </kbd>
          </div>
          {activeTab === 'list' && (
            <div className="inline-flex rounded-xl border border-gray-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-900/60 p-0.5">
              {(['all', 'active', 'inactive'] as const).map((s) => {
                const active = statusFilter === s;
                return (
                  <button
                    key={s}
                    onClick={() => setStatusFilter(s)}
                    className={`px-3 py-1.5 text-xs font-bold rounded-lg transition-all ${
                      active
                        ? 'bg-gradient-to-br from-primary-600 to-indigo-600 text-white shadow-sm shadow-primary-500/20'
                        : 'text-gray-600 dark:text-slate-300 hover:bg-white dark:hover:bg-slate-700/60'
                    }`}
                  >
                    {s === 'all' ? t('mail.mailboxes.filter.statusAll') : s === 'active' ? t('mail.common.active') : t('mail.common.disabled')}
                  </button>
                );
              })}
            </div>
          )}
          <button onClick={handleSearch} className={btnPrimary}>
            <Search className="w-4 h-4" />
            {t('common.search')}
          </button>
          <div className="ml-auto inline-flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400">
            <Inbox className="w-3.5 h-3.5" />
            {t('mail.mailboxes.countPrefix')} <b className="font-black text-gray-700 dark:text-slate-200">{filteredList.length}</b> / {currentList.length}
          </div>
        </div>
      )}

      {/* 筛选摘要胶囊条 */}
      {!loading && filterChips.length > 0 && (
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
              {chip.label}
              <button onClick={chip.onClear} className="p-0.5 rounded hover:bg-primary-200/50 dark:hover:bg-primary-800/50 transition" title={t('mail.common.removeFilter')}>
                <X className="w-3 h-3" />
              </button>
            </span>
          ))}
          <button
            onClick={handleResetSearch}
            className="text-xs text-rose-600 dark:text-rose-400 hover:underline font-bold"
          >
            {t('mail.common.clearAllFilters')}
          </button>
        </div>
      )}

      {/* 邮箱卡片网格 */}
      {loading ? (
        <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm">
          <LoadingState text={t('mail.mailboxes.loadingList')} />
        </div>
      ) : filteredList.length === 0 ? (
        <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm">
          <EmptyState
            icon={MailboxIcon}
            title={
              searchKeyword || statusFilter !== 'all'
                ? t('mail.mailboxes.empty.noMatch')
                : activeTab === 'list'
                ? t('mail.mailboxes.empty.noMailboxes')
                : t('mail.common.trashEmpty')
            }
            subtitle={
              searchKeyword || statusFilter !== 'all'
                ? t('mail.mailboxes.empty.noMatchHint')
                : activeTab === 'list'
                ? t('mail.mailboxes.empty.noMailboxesHint')
                : t('mail.mailboxes.empty.trashHint')
            }
            action={
              searchKeyword || statusFilter !== 'all' ? (
                <button
                  onClick={handleResetSearch}
                  className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-bold text-primary-600 dark:text-primary-400 bg-primary-50 dark:bg-primary-900/20 rounded-xl hover:bg-primary-100 dark:hover:bg-primary-900/30 transition-all ring-1 ring-primary-500/20"
                >
                  <X className="w-4 h-4" />
                  {t('mail.mailboxes.clearFilters')}
                </button>
              ) : activeTab === 'list' ? (
                <button
                  onClick={openCreate}
                  className={btnPrimary}
                >
                  <Plus className="w-4 h-4" />
                  {t('mail.mailboxes.newMailbox')}
                </button>
              ) : undefined
            }
          />
        </div>
      ) : (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          {filteredList.map((m) => {
            const palette = getCardPalette(m.id);
            return (
              <div
                key={m.id}
                className={`relative overflow-hidden bg-white dark:bg-slate-800/60 rounded-2xl border border-gray-200 dark:border-slate-700/60 p-5 shadow-sm hover:shadow-lg transition-all duration-200 group ring-1 ring-gray-100/50 dark:ring-slate-700/30`}
              >
                {/* 顶部彩色细条 */}
                <div className={`absolute top-0 left-0 right-0 h-1 bg-gradient-to-r ${palette.from} ${palette.to}`} />

                {/* 头部：图标 + 操作 */}
                <div className="flex items-start justify-between">
                  <div className={`shrink-0 w-11 h-11 rounded-xl bg-gradient-to-br ${palette.from} ${palette.to} text-white flex items-center justify-center shadow-md ring-1 ring-white/40 dark:ring-white/10 transition-transform group-hover:scale-110`}>
                    <MailboxIcon className="w-5 h-5" strokeWidth={2} />
                  </div>
                  <div className="flex items-center gap-1">
                    {activeTab === 'list' ? (
                      <>
                        <button
                          onClick={() => handleToggleActive(m.id)}
                          className={`p-2 rounded-lg transition-all ${
                            m.isActive
                              ? 'text-gray-400 dark:text-slate-500 hover:text-amber-600 dark:hover:text-amber-400 hover:bg-amber-50 dark:hover:bg-amber-900/30'
                              : 'text-emerald-600 dark:text-emerald-400 hover:text-emerald-700 hover:bg-emerald-50 dark:hover:bg-emerald-900/30'
                          }`}
                          title={m.isActive ? t('mail.common.disabled') : t('mail.common.enabled')}
                        >
                          {m.isActive ? <PowerOff className="w-4 h-4" /> : <Power className="w-4 h-4" />}
                        </button>
                        <button
                          onClick={() => handleTestConnection(m.id)}
                          disabled={testingId === m.id}
                          className="p-2 text-gray-400 dark:text-slate-500 hover:text-emerald-600 dark:hover:text-emerald-400 hover:bg-emerald-50 dark:hover:bg-emerald-900/30 rounded-lg transition-all disabled:opacity-50"
                          title={t('mail.mailboxes.testConnection')}
                        >
                          {testingId === m.id ? (
                            <RefreshCw className="w-4 h-4 animate-spin" />
                          ) : (
                            <Zap className="w-4 h-4" />
                          )}
                        </button>
                        <button
                          onClick={() => openEdit(m)}
                          className="p-2 text-gray-400 dark:text-slate-500 hover:text-sky-600 dark:hover:text-sky-400 hover:bg-sky-50 dark:hover:bg-sky-900/30 rounded-lg transition-all"
                          title={t('common.edit')}
                        >
                          <Edit2 className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => setDeleteTarget(m)}
                          className="p-2 text-gray-400 dark:text-slate-500 hover:text-rose-600 dark:hover:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-900/30 rounded-lg transition-all"
                          title={t('common.delete')}
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </>
                    ) : (
                      <>
                        <button
                          onClick={() => setRestoreTarget(m)}
                          className="p-2 text-gray-400 dark:text-slate-500 hover:text-emerald-600 dark:hover:text-emerald-400 hover:bg-emerald-50 dark:hover:bg-emerald-900/30 rounded-lg transition-all"
                          title={t('mail.common.restore')}
                        >
                          <RefreshCw className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => setHardDeleteTarget(m)}
                          className="p-2 text-gray-400 dark:text-slate-500 hover:text-rose-600 dark:hover:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-900/30 rounded-lg transition-all"
                          title={t('mail.common.hardDelete')}
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </>
                    )}
                  </div>
                </div>

                {/* 名称 + 状态 */}
                <div className="mt-3.5">
                  <div className="flex items-center gap-2 flex-wrap">
                    <h3 className="font-extrabold text-gray-900 dark:text-slate-100 tracking-tight">{m.name}</h3>
                    {m.isActive ? (
                      <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider rounded-full bg-gradient-to-br from-emerald-50 to-teal-50 dark:from-emerald-900/30 dark:to-teal-900/20 text-emerald-700 dark:text-emerald-300 ring-1 ring-emerald-500/20">
                        <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse" />
                        {t('mail.common.active')}
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider rounded-full bg-gray-100 dark:bg-slate-700/50 text-gray-600 dark:text-slate-300 ring-1 ring-gray-500/10">
                        <span className="w-1.5 h-1.5 rounded-full bg-gray-400" />
                        {t('mail.common.disabled')}
                      </span>
                    )}
                  </div>
                  <p className="text-sm text-gray-500 dark:text-slate-400 mt-1 flex items-center gap-1.5">
                    <AtSign className="w-3.5 h-3.5 flex-shrink-0 opacity-60" />
                    <span className="truncate font-mono" title={m.email}>{m.email}</span>
                  </p>
                </div>

                {/* 收发件服务器信息 */}
                <div className="mt-3 grid grid-cols-2 gap-2 text-xs">
                  <div className={`flex items-center gap-1.5 px-2 py-1.5 rounded-lg bg-sky-50 dark:bg-sky-900/20 text-sky-700 dark:text-sky-300 ring-1 ring-sky-500/10`}>
                    <DownloadCloud className="w-3.5 h-3.5 flex-shrink-0" />
                    <span className="truncate font-medium" title={m.inHost || t('mail.mailboxes.notConfigured')}>
                      {m.inHost || t('mail.mailboxes.notConfigured')}:{m.inPort ?? 993}
                    </span>
                  </div>
                  <div className={`flex items-center gap-1.5 px-2 py-1.5 rounded-lg bg-emerald-50 dark:bg-emerald-900/20 text-emerald-700 dark:text-emerald-300 ring-1 ring-emerald-500/10`}>
                    <Send className="w-3.5 h-3.5 flex-shrink-0" />
                    <span className="truncate font-medium" title={m.outHost || t('mail.mailboxes.notConfigured')}>
                      {m.outHost || t('mail.mailboxes.notConfigured')}:{m.outPort ?? 465}
                    </span>
                  </div>
                </div>

                {/* 收取频率 & 最后收取时间 */}
                {activeTab === 'list' && (
                  <div className="mt-2 flex items-center gap-3 text-[11px] text-gray-500 dark:text-slate-400">
                    <span className="inline-flex items-center gap-1">
                      <Clock className="w-3 h-3" />
                      {t('mail.mailboxes.fetchEvery', { count: m.fetchFrequency || 5 })}
                    </span>
                    {m.lastFetchAt && (
                      <span className="inline-flex items-center gap-1">
                        <CheckCircle2 className="w-3 h-3 text-emerald-500" />
                        {t('mail.mailboxes.lastFetchAt', { time: formatDateTime(m.lastFetchAt) })}
                      </span>
                    )}
                  </div>
                )}

                {/* 统计信息 */}
                {activeTab === 'list' && (m.conversationCount !== undefined || m.todaySendCount !== undefined) && (
                  <div className="mt-3 grid grid-cols-3 gap-2">
                    {m.conversationCount !== undefined && (
                      <div className="bg-gray-50 dark:bg-slate-700/40 rounded-lg px-2 py-2 text-center ring-1 ring-gray-500/10">
                        <div className="text-base font-extrabold text-gray-900 dark:text-slate-100 tabular-nums">{m.conversationCount}</div>
                        <div className="text-[10px] text-gray-500 dark:text-slate-400 mt-0.5">{t('mail.mailboxes.stat.totalConversations')}</div>
                      </div>
                    )}
                    {m.openConversationCount !== undefined && (
                      <div className="bg-amber-50 dark:bg-amber-900/20 rounded-lg px-2 py-2 text-center ring-1 ring-amber-500/10">
                        <div className="text-base font-extrabold text-amber-600 dark:text-amber-400 tabular-nums">{m.openConversationCount}</div>
                        <div className="text-[10px] text-amber-600 dark:text-amber-400 mt-0.5">{t('mail.mailboxes.stat.open')}</div>
                      </div>
                    )}
                    {m.todaySendCount !== undefined && (
                      <div className="bg-emerald-50 dark:bg-emerald-900/20 rounded-lg px-2 py-2 text-center ring-1 ring-emerald-500/10">
                        <div className="text-base font-extrabold text-emerald-600 dark:text-emerald-400 tabular-nums">{m.todaySendCount}</div>
                        <div className="text-[10px] text-emerald-600 dark:text-emerald-400 mt-0.5">{t('mail.mailboxes.stat.todaySent')}</div>
                      </div>
                    )}
                  </div>
                )}

                {/* 测试结果 */}
                {testResult?.id === m.id && (
                  <div
                    className={`mt-3 px-3 py-2 rounded-lg text-xs flex items-start gap-2 ring-1 ${
                      testResult.success
                        ? 'bg-emerald-50 dark:bg-emerald-900/20 text-emerald-700 dark:text-emerald-300 ring-emerald-500/20'
                        : 'bg-rose-50 dark:bg-rose-900/20 text-rose-700 dark:text-rose-300 ring-rose-500/20'
                    }`}
                  >
                    {testResult.success ? (
                      <CheckCircle2 className="w-4 h-4 flex-shrink-0 mt-0.5" />
                    ) : (
                      <XCircle className="w-4 h-4 flex-shrink-0 mt-0.5" />
                    )}
                    <span className="break-all">{testResult.success ? t('mail.mailboxes.testSuccess') : testResult.message ?? t('mail.mailboxes.testFailed')}</span>
                  </div>
                )}

                {/* 底部：时间 + 用户组 */}
                <div className="mt-4 pt-3 border-t border-gray-100 dark:border-slate-700/50 text-xs text-gray-500 dark:text-slate-400 flex justify-between items-center">
                  <span className="flex items-center gap-1">
                    {activeTab === 'trash' && m.deletedAt ? (
                      <>
                        <Trash2 className="w-3 h-3 text-rose-500" />
                        {t('mail.common.deletedAtPrefix')} {formatDate(m.deletedAt)}
                      </>
                    ) : (
                      <>
                        <Clock className="w-3 h-3" />
                        {formatDate(m.createdAt)}
                      </>
                    )}
                  </span>
                  <div className="flex items-center gap-2">
                    {activeTab === 'list' && m.userIds && m.userIds.length > 0 && (
                      <div className="flex -space-x-1.5" title={t('mail.mailboxes.authorizedUsersTitle', { count: m.userIds.length })}>
                        {users
                          .filter((u) => m.userIds.includes(u.id))
                          .slice(0, 3)
                          .map((u) => (
                            <UserAvatar key={u.id} userId={u.id} name={u.fullName} size="sm" avatarUrl={u.avatar ?? null} />
                          ))}
                        {m.userIds.length > 3 && (
                          <div className="w-5 h-5 rounded-full bg-gray-200 dark:bg-slate-600 text-[10px] text-gray-600 dark:text-slate-300 flex items-center justify-center font-bold border-2 border-white dark:border-slate-800">
                            +{m.userIds.length - 3}
                          </div>
                        )}
                      </div>
                    )}
                    <span className="font-mono px-2 py-0.5 rounded-md bg-gray-100 dark:bg-slate-700/50 text-gray-600 dark:text-slate-300 text-[10px]">
                      #{m.id}
                    </span>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* 新建/编辑弹窗 */}
      <Modal
        open={modalOpen}
        title={editingId ? t('mail.mailboxes.editMailbox') : t('mail.mailboxes.newMailbox')}
        onClose={() => setModalOpen(false)}
        size="lg"
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
              form="mailbox-form"
              disabled={submitting}
              className="px-4 py-2 text-sm font-bold text-white rounded-xl bg-gradient-to-br from-primary-600 to-indigo-600 hover:from-primary-700 hover:to-indigo-700 disabled:opacity-50 shadow-sm shadow-primary-500/20 transition-all ring-1 ring-primary-500/20 inline-flex items-center gap-1.5"
            >
              {submitting && <RefreshCw className="w-4 h-4 animate-spin" />}
              {submitting ? t('mail.mailboxes.form.saving') : t('common.save')}
            </button>
          </>
        }
      >
        <form id="mailbox-form" onSubmit={handleSubmit} className="space-y-6">
          {/* 渐变头部装饰卡 */}
          <div className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-primary-50 via-white to-indigo-50 dark:from-slate-900 dark:via-slate-800/60 dark:to-indigo-900/20 border border-primary-100 dark:border-slate-700/60 p-4">
            <div className="pointer-events-none absolute -top-12 -right-12 w-32 h-32 rounded-full bg-primary-500/10 blur-3xl" />
            <div className="relative flex items-start gap-3">
              <div className="shrink-0 w-10 h-10 rounded-xl bg-gradient-to-br from-primary-500 to-indigo-500 text-white shadow-md shadow-primary-500/30 flex items-center justify-center ring-1 ring-white/40 dark:ring-white/10">
                {editingId ? <Edit2 className="w-4 h-4" strokeWidth={2} /> : <Plus className="w-4 h-4" strokeWidth={2} />}
              </div>
              <div className="min-w-0">
                <h4 className="text-sm font-extrabold tracking-tight text-gray-900 dark:text-slate-50">
                  {editingId ? t('mail.mailboxes.form.editTitle') : t('mail.mailboxes.form.createTitle')}
                </h4>
                <p className="text-[11px] text-gray-500 dark:text-slate-400 mt-0.5 leading-relaxed">
                  {editingId ? t('mail.mailboxes.form.editSubtitle') : t('mail.mailboxes.form.createSubtitle')}
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

          {/* 基本信息 */}
          <div className="space-y-3">
            <SectionHeader icon={Info} title={t('mail.mailboxes.form.basicInfo')} subtitle={t('mail.mailboxes.form.basicInfoHint')} color="from-primary-500 to-indigo-500" />
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">
                  {t('mail.mailboxes.form.name')} <span className="text-rose-500">*</span>
                </label>
                <input
                  type="text"
                  required
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  className={inputBase}
                  placeholder={t('mail.mailboxes.form.namePlaceholder')}
                />
              </div>
              <div>
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">
                  {t('mail.mailboxes.form.email')} <span className="text-rose-500">*</span>
                </label>
                <input
                  type="email"
                  required
                  value={form.email}
                  onChange={(e) => setForm({ ...form, email: e.target.value })}
                  className={inputBase}
                  placeholder="support@example.com"
                />
              </div>
            </div>
            <label className="flex items-center gap-2.5 cursor-pointer">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
                className="w-4 h-4 text-primary-600 border-gray-300 dark:border-slate-500 rounded focus:ring-primary-500"
              />
              <span className="text-sm text-gray-700 dark:text-slate-300 font-medium">{t('mail.mailboxes.form.enableMailbox')}</span>
            </label>
          </div>

          {/* 收件服务器 */}
          <div className="space-y-3">
            <SectionHeader icon={DownloadCloud} title={t('mail.mailboxes.form.inboundTitle')} subtitle={t('mail.mailboxes.form.inboundHint')} color="from-sky-500 to-blue-500" />
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.serverHost')}</label>
                <input
                  type="text"
                  value={form.inHost}
                  onChange={(e) => setForm({ ...form, inHost: e.target.value })}
                  className={inputBase}
                  placeholder="imap.example.com"
                />
              </div>
              <div>
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.port')}</label>
                <input
                  type="number"
                  value={form.inPort}
                  onChange={(e) => setForm({ ...form, inPort: Number(e.target.value) })}
                  className={inputBase}
                />
              </div>
              <div>
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.encryption')}</label>
                <select
                  value={form.inEncryption}
                  onChange={(e) => setForm({ ...form, inEncryption: Number(e.target.value) })}
                  className={selectBase}
                >
                  {encryptionOptions.map((o) => (
                    <option key={o.value} value={o.value}>{t(o.labelKey)}</option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.username')}</label>
                <input
                  type="text"
                  value={form.inUsername}
                  onChange={(e) => setForm({ ...form, inUsername: e.target.value })}
                  className={inputBase}
                />
              </div>
              <div className="col-span-2">
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">
                  {t('mail.mailboxes.form.password')}
                  {editingId && editingMailbox?.hasInPassword && (
                    <span className="ml-2 text-xs text-emerald-600 inline-flex items-center gap-0.5">
                      <CheckCircle2 className="w-3 h-3" />
                      {t('mail.mailboxes.form.alreadySet')}
                    </span>
                  )}
                </label>
                <input
                  type="password"
                  value={form.inPassword}
                  onChange={(e) => setForm({ ...form, inPassword: e.target.value })}
                  className={inputBase}
                  placeholder={editingId ? t('mail.mailboxes.form.passwordKeepPlaceholder') : t('mail.mailboxes.form.passwordPlaceholder')}
                />
                {editingId && (
                  <p className="mt-1 text-[11px] text-gray-400 dark:text-slate-500 flex items-center gap-1">
                    <Info className="w-3 h-3" />
                    {t('mail.mailboxes.form.authCodeHint')}
                  </p>
                )}
              </div>
              <div className="col-span-2">
                <label className="flex items-center gap-2.5 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={form.inValidateCert}
                    onChange={(e) => setForm({ ...form, inValidateCert: e.target.checked })}
                    className="w-4 h-4 text-primary-600 border-gray-300 dark:border-slate-500 rounded focus:ring-primary-500"
                  />
                  <span className="text-sm text-gray-700 dark:text-slate-300 font-medium">{t('mail.mailboxes.form.validateCert')}</span>
                </label>
              </div>
              <div>
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.sentFolder')}</label>
                <input
                  type="text"
                  value={form.imapSentFolder}
                  onChange={(e) => setForm({ ...form, imapSentFolder: e.target.value })}
                  className={inputBase}
                  placeholder="Sent"
                />
              </div>
              <div>
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.fetchFrequency')}</label>
                <select
                  value={form.fetchFrequency}
                  onChange={(e) => setForm({ ...form, fetchFrequency: Number(e.target.value) })}
                  className={selectBase}
                >
                  {[1, 2, 3, 5, 10, 15, 30, 60].map((min) => (
                    <option key={min} value={min}>{t('mail.mailboxes.form.minutesOption', { count: min })}</option>
                  ))}
                </select>
              </div>
            </div>
          </div>

          {/* 发件服务器 */}
          <div className="space-y-3">
            <SectionHeader icon={Send} title={t('mail.mailboxes.form.outboundTitle')} subtitle={t('mail.mailboxes.form.outboundHint')} color="from-emerald-500 to-teal-500" />
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.serverHost')}</label>
                <input
                  type="text"
                  value={form.outHost}
                  onChange={(e) => setForm({ ...form, outHost: e.target.value })}
                  className={inputBase}
                  placeholder="smtp.example.com"
                />
              </div>
              <div>
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.port')}</label>
                <input
                  type="number"
                  value={form.outPort}
                  onChange={(e) => setForm({ ...form, outPort: Number(e.target.value) })}
                  className={inputBase}
                />
              </div>
              <div>
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.encryption')}</label>
                <select
                  value={form.outEncryption}
                  onChange={(e) => setForm({ ...form, outEncryption: Number(e.target.value) })}
                  className={selectBase}
                >
                  {encryptionOptions.map((o) => (
                    <option key={o.value} value={o.value}>{t(o.labelKey)}</option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.username')}</label>
                <input
                  type="text"
                  value={form.outUsername}
                  onChange={(e) => setForm({ ...form, outUsername: e.target.value })}
                  className={inputBase}
                />
              </div>
              <div className="col-span-2">
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">
                  {t('mail.mailboxes.form.password')}
                  {editingId && editingMailbox?.hasOutPassword && (
                    <span className="ml-2 text-xs text-emerald-600 inline-flex items-center gap-0.5">
                      <CheckCircle2 className="w-3 h-3" />
                      {t('mail.mailboxes.form.alreadySet')}
                    </span>
                  )}
                </label>
                <input
                  type="password"
                  value={form.outPassword}
                  onChange={(e) => setForm({ ...form, outPassword: e.target.value })}
                  className={inputBase}
                  placeholder={editingId ? t('mail.mailboxes.form.passwordKeepPlaceholder') : t('mail.mailboxes.form.passwordPlaceholder')}
                />
                {editingId && (
                  <p className="mt-1 text-[11px] text-gray-400 dark:text-slate-500 flex items-center gap-1">
                    <Info className="w-3 h-3" />
                    {t('mail.mailboxes.form.authCodeHint')}
                  </p>
                )}
              </div>
            </div>
          </div>

          {/* 别名和抄送 */}
          <div className="space-y-3">
            <SectionHeader icon={AtSign} title={t('mail.mailboxes.form.aliasesTitle')} subtitle={t('mail.mailboxes.form.aliasesHint')} color="from-amber-500 to-orange-500" />
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.alias')}</label>
                <input
                  type="text"
                  value={form.aliases}
                  onChange={(e) => setForm({ ...form, aliases: e.target.value })}
                  className={inputBase}
                  placeholder="alias1@domain.com, alias2@domain.com"
                />
              </div>
              <div>
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.aliasReply')}</label>
                <input
                  type="text"
                  value={form.aliasesReply}
                  onChange={(e) => setForm({ ...form, aliasesReply: e.target.value })}
                  className={inputBase}
                  placeholder="reply@domain.com"
                />
              </div>
              <div className="col-span-2">
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.autoBcc')}</label>
                <input
                  type="text"
                  value={form.autoBcc}
                  onChange={(e) => setForm({ ...form, autoBcc: e.target.value })}
                  className={inputBase}
                  placeholder="bcc1@domain.com, bcc2@domain.com"
                />
              </div>
            </div>
          </div>

          {/* 自动回复 */}
          <div className="space-y-3">
            <SectionHeader icon={Reply} title={t('mail.mailboxes.form.autoReplyTitle')} subtitle={t('mail.mailboxes.form.autoReplyHint')} color="from-cyan-500 to-sky-500" />
            <label className="flex items-center gap-2.5 cursor-pointer">
              <input
                type="checkbox"
                checked={form.autoReplyEnabled}
                onChange={(e) => setForm({ ...form, autoReplyEnabled: e.target.checked })}
                className="w-4 h-4 text-primary-600 border-gray-300 dark:border-slate-500 rounded focus:ring-primary-500"
              />
              <span className="text-sm text-gray-700 dark:text-slate-300 font-medium">{t('mail.mailboxes.form.autoReplyEnabled')}</span>
            </label>
            {form.autoReplyEnabled && (
              <div className="grid grid-cols-1 gap-4 p-3 rounded-xl bg-cyan-50/50 dark:bg-cyan-900/10 border border-cyan-100 dark:border-cyan-900/30">
                <div>
                  <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.autoReplySubject')}</label>
                  <input
                    type="text"
                    value={form.autoReplySubject}
                    onChange={(e) => setForm({ ...form, autoReplySubject: e.target.value })}
                    className={inputBase}
                    placeholder={t('mail.mailboxes.form.autoReplySubjectPlaceholder')}
                  />
                </div>
                <div>
                  <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.autoReplyMessage')}</label>
                  <textarea
                    value={form.autoReplyMessage}
                    onChange={(e) => setForm({ ...form, autoReplyMessage: e.target.value })}
                    rows={4}
                    className={`${inputBase} resize-none`}
                    placeholder={t('mail.mailboxes.form.autoReplyMessagePlaceholder')}
                  />
                </div>
              </div>
            )}
          </div>

          {/* 签名和回复前文本 */}
          <div className="space-y-3">
            <SectionHeader icon={FileSignature} title={t('mail.mailboxes.form.signatureTitle')} subtitle={t('mail.mailboxes.form.signatureHint')} color="from-indigo-500 to-violet-500" />
            <div>
              <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.beforeReply')}</label>
              <textarea
                value={form.beforeReply}
                onChange={(e) => setForm({ ...form, beforeReply: e.target.value })}
                rows={2}
                className={`${inputBase} resize-none`}
                placeholder={t('mail.mailboxes.form.beforeReplyPlaceholder')}
              />
            </div>
            <div>
              <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.form.signature')}</label>
              <textarea
                value={form.signature}
                onChange={(e) => setForm({ ...form, signature: e.target.value })}
                rows={4}
                className={`${inputBase} resize-none`}
                placeholder={t('mail.mailboxes.form.signaturePlaceholder')}
              />
            </div>
          </div>

          {/* OAuth 认证 */}
          <div className="space-y-3">
            <SectionHeader icon={KeyRound} title={t('mail.mailboxes.oauth.title')} subtitle={t('mail.mailboxes.oauth.hint')} color="from-rose-500 to-pink-500" />
            <label className="flex items-center gap-2.5 cursor-pointer">
              <input
                type="checkbox"
                checked={form.oauthEnabled}
                onChange={(e) => setForm({ ...form, oauthEnabled: e.target.checked })}
                className="w-4 h-4 text-primary-600 border-gray-300 dark:border-slate-500 rounded focus:ring-primary-500"
              />
              <span className="text-sm text-gray-700 dark:text-slate-300 font-medium">{t('mail.mailboxes.oauth.enabled')}</span>
            </label>
            {form.oauthEnabled && (
              <div className="grid grid-cols-2 gap-4 p-3 rounded-xl bg-rose-50/50 dark:bg-rose-900/10 border border-rose-100 dark:border-rose-900/30">
                <div className="col-span-2">
                  <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">{t('mail.mailboxes.oauth.provider')}</label>
                  <select
                    value={form.oauthProvider}
                    onChange={(e) => setForm({ ...form, oauthProvider: e.target.value })}
                    className={selectBase}
                  >
                    <option value="">{t('mail.mailboxes.oauth.selectPlaceholder')}</option>
                    <option value="google">Google (Gmail)</option>
                    <option value="microsoft">Microsoft (Outlook)</option>
                    <option value="other">{t('mail.mailboxes.oauth.other')}</option>
                  </select>
                </div>
                <div className="col-span-2">
                  <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">
                    Access Token
                    {editingId && editingMailbox?.oauthEnabled && (
                      <span className="ml-2 text-xs text-emerald-600 inline-flex items-center gap-0.5">
                        <CheckCircle2 className="w-3 h-3" />
                        {t('mail.mailboxes.form.alreadySet')}
                      </span>
                    )}
                  </label>
                  <input
                    type="password"
                    value={form.oauthAccessToken}
                    onChange={(e) => setForm({ ...form, oauthAccessToken: e.target.value })}
                    className={inputBase}
                    placeholder={editingId ? t('mail.mailboxes.oauth.keepPlaceholder') : 'OAuth Access Token'}
                  />
                </div>
                <div className="col-span-2">
                  <label className="block text-xs font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mb-1.5">
                    Refresh Token
                    {editingId && editingMailbox?.oauthEnabled && (
                      <span className="ml-2 text-xs text-emerald-600 inline-flex items-center gap-0.5">
                        <CheckCircle2 className="w-3 h-3" />
                        {t('mail.mailboxes.form.alreadySet')}
                      </span>
                    )}
                  </label>
                  <input
                    type="password"
                    value={form.oauthRefreshToken}
                    onChange={(e) => setForm({ ...form, oauthRefreshToken: e.target.value })}
                    className={inputBase}
                    placeholder={editingId ? t('mail.mailboxes.oauth.keepPlaceholder') : 'OAuth Refresh Token'}
                  />
                </div>
              </div>
            )}
          </div>

          {/* 授权用户 */}
          {users.length > 0 && (
            <div className="space-y-3">
              <SectionHeader icon={Users} title={t('mail.mailboxes.form.authorizedUsersTitle')} subtitle={t('mail.mailboxes.form.authorizedUsersHint')} color="from-purple-500 to-fuchsia-500" />
              <div className="space-y-1 max-h-48 overflow-y-auto p-2 border border-gray-200 dark:border-slate-700 rounded-xl bg-slate-50/50 dark:bg-slate-900/30">
                {users.map((u) => (
                  <label
                    key={u.id}
                    className={`flex items-center gap-2.5 px-2.5 py-2 rounded-lg cursor-pointer transition-all ${
                      form.userIds.includes(u.id)
                        ? 'bg-purple-50 dark:bg-purple-900/20 ring-1 ring-purple-500/20'
                        : 'hover:bg-gray-50 dark:hover:bg-slate-700/60'
                    }`}
                  >
                    <input
                      type="checkbox"
                      checked={form.userIds.includes(u.id)}
                      onChange={() => toggleUser(u.id)}
                      className="w-4 h-4 text-primary-600 border-gray-300 dark:border-slate-500 rounded focus:ring-primary-500"
                    />
                    <UserAvatar userId={u.id} name={u.fullName} size="xs" avatarUrl={u.avatar ?? null} />
                    <span className="text-sm font-medium text-gray-700 dark:text-slate-300">{u.fullName}</span>
                    <span className="text-xs text-gray-400 dark:text-slate-500 ml-auto truncate">{u.email}</span>
                  </label>
                ))}
              </div>
              {form.userIds.length > 0 && (
                <p className="text-[11px] text-purple-600 dark:text-purple-400 flex items-center gap-1">
                  <CheckCircle2 className="w-3 h-3" />
                  {t('mail.mailboxes.form.selectedUsers', { count: form.userIds.length })}
                </p>
              )}
            </div>
          )}
        </form>
      </Modal>

      {/* 删除确认 */}
      <ConfirmDialog
        open={!!deleteTarget}
        title={t('mail.mailboxes.delete.title')}
        message={t('mail.mailboxes.delete.message', { name: deleteTarget?.name ?? '', email: deleteTarget?.email ?? '' })}
        confirmText={t('common.delete')}
        variant="danger"
        loading={deleting}
        error={deleteError}
        onConfirm={handleDelete}
        onCancel={() => { setDeleteTarget(null); setDeleteError(undefined); }}
      />

      {/* 恢复确认 */}
      <ConfirmDialog
        open={!!restoreTarget}
        title={t('mail.mailboxes.restore.title')}
        message={t('mail.mailboxes.restore.message', { name: restoreTarget?.name ?? '', email: restoreTarget?.email ?? '' })}
        confirmText={t('mail.common.restore')}
        variant="primary"
        loading={restoring}
        onConfirm={handleRestore}
        onCancel={() => setRestoreTarget(null)}
      />

      {/* 彻底删除确认 */}
      <ConfirmDialog
        open={!!hardDeleteTarget}
        title={t('mail.mailboxes.hardDelete.title')}
        message={t('mail.mailboxes.hardDelete.message', { name: hardDeleteTarget?.name ?? '', email: hardDeleteTarget?.email ?? '' })}
        confirmText={t('mail.common.hardDelete')}
        variant="danger"
        loading={hardDeleting}
        onConfirm={handleHardDelete}
        onCancel={() => setHardDeleteTarget(null)}
      />

      {/* 清空垃圾箱确认 */}
      <ConfirmDialog
        open={clearTrashOpen}
        title={t('mail.common.clearTrash')}
        message={t('mail.mailboxes.clearTrash.message', { count: trashList.length })}
        confirmText={t('mail.mailboxes.clearTrash.confirm')}
        variant="danger"
        loading={clearingTrash}
        onConfirm={handleClearTrash}
        onCancel={() => setClearTrashOpen(false)}
      />
    </div>
  );
}
