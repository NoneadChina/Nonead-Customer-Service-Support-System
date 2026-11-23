import { useEffect, useState, FormEvent, useCallback, useMemo } from 'react';
import { userApi, mailboxApi, roleApi } from '../api';
import type { UserResponse, MailboxResponse, CreateUserRequest, RoleDto } from '../api/types';
import { formatDate } from '../utils/format';
import { getRoleLabel } from '../utils/permissions';
import { getErrorData } from './calendar/store';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import UserAvatar from '../components/UserAvatar';
import PasswordPolicyHint from '../components/PasswordPolicyHint';
import { usePasswordPolicy } from '../hooks/usePasswordPolicy';
import { useAuth } from '../context/AuthContext';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';

// 系统角色 slug → UserRoleType 枚举值映射（1-4）
const SYSTEM_ROLE_SLUG_MAP: Record<string, number> = {
  viewer: 1,
  admin: 2,
  supervisor: 3,
  agent: 4,
};

/**
 * 系统角色 slug（后端 `Roles.Slug`，种子恰好 4 码，见
 * `NoneadCSS.Api/DatabaseSeeder.cs:963-1014`）→ 展示文案 key（复用已有的 `format.role.*`）。
 *
 * 设计 §6.3：角色属「稳定 code + 字典」，**不再渲染 `Roles.Name`**——种子里的 Name 是中文
 * （超级管理员 / 客服主管 / 客服专员 / 只读用户），英文界面下直出会漏中文。
 * 自定义角色（`IsSystem=false`）的 Name 是用户数据，不在字典范围内，仍原样渲染。
 */
const SYSTEM_ROLE_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  viewer: 'format.role.readOnly',
  admin: 'format.role.superAdmin',
  supervisor: 'format.role.agentSupervisor',
  agent: 'format.role.agent',
};

const UserRole = {
  ReadOnly: 1,
  SuperAdmin: 2,
  AgentSupervisor: 3,
  Agent: 4,
};

/**
 * 系统角色兜底权限文案（码域 = `UserRoleType` 1-4，见 `NoneadCSS.Domain/Enums/UserRole.cs`）。
 *
 * 正常路径下后端种子角色的 `PermissionsJson` 非空，页面渲染的是 `RoleDto.permissions`
 * （权限 key，形如 conversations.view 这样的稳定 code，前端原样展示、不翻译）；
 * 本表只在角色列表尚未加载/为空时兜底。
 */
const rolePermissionKeys: Record<number, TranslationKey[] | undefined> = {
  1: ['teamPermissions.users.permViewAssignedConversations', 'teamPermissions.users.permViewReports'],
  2: ['teamPermissions.users.permViewAllConversations', 'teamPermissions.users.permCreateConversations', 'teamPermissions.users.permEditConversations', 'teamPermissions.users.permDeleteConversations', 'teamPermissions.users.permManageMailboxes', 'teamPermissions.users.permManageUsers', 'teamPermissions.users.permManageSettings', 'teamPermissions.users.permViewReports', 'teamPermissions.users.permExportData'],
  3: ['teamPermissions.users.permViewAllConversations', 'teamPermissions.users.permCreateConversations', 'teamPermissions.users.permEditConversations', 'teamPermissions.users.permDeleteConversations', 'teamPermissions.users.permViewReports'],
  4: ['teamPermissions.users.permViewAssignedConversations', 'teamPermissions.users.permCreateConversations', 'teamPermissions.users.permEditConversations'],
};

/**
 * 用户状态 code（后端 `UserStatus`，`NoneadCSS.Domain/Enums/UserStatus.cs:3-9`，恰好 4 码）
 * → 展示文案 key。`Active=1` 复用既有 `format.status.active`（同义），其余三码在本分区字典内。
 */
const statusMap: Record<number, { labelKey: TranslationKey; color: string; dot: string } | undefined> = {
  1: { labelKey: 'format.status.active', color: 'bg-emerald-50 text-emerald-700 ring-emerald-100 dark:bg-emerald-900/30 dark:text-emerald-300 dark:ring-emerald-800/50', dot: 'bg-emerald-500' },
  2: { labelKey: 'teamPermissions.users.statusDisabled', color: 'bg-red-50 text-red-700 ring-red-100 dark:bg-red-900/30 dark:text-red-300 dark:ring-red-800/50', dot: 'bg-red-500' },
  3: { labelKey: 'teamPermissions.users.statusPendingActivation', color: 'bg-orange-50 text-orange-700 ring-orange-100 dark:bg-orange-900/30 dark:text-orange-300 dark:ring-orange-800/50', dot: 'bg-orange-500' },
  4: { labelKey: 'teamPermissions.users.statusDeleted', color: 'bg-gray-50 text-gray-600 ring-gray-100 dark:bg-slate-700/40 dark:text-slate-400 dark:ring-slate-600/50', dot: 'bg-gray-400' },
};

/** 状态码未覆盖时的回落展示（含样式，避免渲染处再写字面量） */
const UNKNOWN_STATUS: { labelKey: TranslationKey; color: string; dot: string } = {
  labelKey: 'format.unknown',
  color: 'bg-gray-100 text-gray-600',
  dot: 'bg-gray-400',
};

const roleStyle: Record<number, string> = {
  1: 'bg-gray-50 text-gray-600 ring-gray-100 dark:bg-slate-700/40 dark:text-slate-400 dark:ring-slate-600/50',
  2: 'bg-blue-50 text-blue-700 ring-blue-100 dark:bg-blue-900/30 dark:text-blue-300 dark:ring-blue-800/50',
  3: 'bg-amber-50 text-amber-700 ring-amber-100 dark:bg-amber-900/30 dark:text-amber-300 dark:ring-amber-800/50',
  4: 'bg-emerald-50 text-emerald-700 ring-emerald-100 dark:bg-emerald-900/30 dark:text-emerald-300 dark:ring-emerald-800/50',
};

interface FormState {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  role: number;
  mailboxIds: number[];
}

const emptyForm: FormState = {
  firstName: '',
  lastName: '',
  email: '',
  password: '',
  role: 1,
  mailboxIds: [],
};

const inputClass = "w-full px-3 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 transition-all";

export default function Users() {
  const { t } = useI18n();
  const { user: currentUser } = useAuth();
  const [list, setList] = useState<UserResponse[]>([]);
  const [trashList, setTrashList] = useState<UserResponse[]>([]);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [activeTab, setActiveTab] = useState<'list' | 'trash'>('list');

  const isAdmin = currentUser?.role === UserRole.SuperAdmin;
  const { policy, validate: validatePassword } = usePasswordPolicy();

  // 动态生成角色选项：系统角色映射为 UserRoleType 枚举值（1-4），自定义角色使用 Role.Id；
  // 系统角色展示名走 `format.role.*` 字典（不渲染后端中文 `Roles.Name`），
  // 自定义角色（无 `labelKey`）展示其自身的 Name（用户数据）。
  const roleOptions = useMemo(() => {
    return roles.map((r) => {
      const systemValue = SYSTEM_ROLE_SLUG_MAP[r.slug];
      return {
        value: systemValue ?? r.id,
        name: r.name,
        labelKey: SYSTEM_ROLE_LABEL_KEYS[r.slug],
        isSystem: r.isSystem,
      };
    });
  }, [roles]);

  // 分页
  const [currentPage, setCurrentPage] = useState(1);
  const [pageSize] = useState(20);
  const [totalItems, setTotalItems] = useState(0);
  const totalPages = Math.ceil(totalItems / pageSize);

  // 统计数据
  const [stats, setStats] = useState({ total: 0, active: 0, disabled: 0, pending: 0 });

  // 搜索
  const [searchInput, setSearchInput] = useState('');
  const [searchKeyword, setSearchKeyword] = useState('');
  const [statusFilter, setStatusFilter] = useState<number | null>(null);

  // 弹窗状态
  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<FormState>(emptyForm);
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState('');

  // 删除确认
  const [deleteTarget, setDeleteTarget] = useState<UserResponse | null>(null);
  const [deleting, setDeleting] = useState(false);
  // T000106: 删除失败时在确认弹窗内展示具体原因（如"关联会话数>0"），避免"无响应"误解
  const [deleteError, setDeleteError] = useState('');

  // 垃圾箱操作
  const [restoreTarget, setRestoreTarget] = useState<UserResponse | null>(null);
  const [restoring, setRestoring] = useState(false);
  const [hardDeleteTarget, setHardDeleteTarget] = useState<UserResponse | null>(null);
  const [hardDeleting, setHardDeleting] = useState(false);
  const [clearTrashOpen, setClearTrashOpen] = useState(false);
  const [clearingTrash, setClearingTrash] = useState(false);

  const fetch = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const [userResp, statsResp, mbResp, trashResp, roleResp] = await Promise.all([
        userApi.list({
          page: currentPage,
          pageSize,
          keyword: searchKeyword || undefined,
          status: statusFilter ?? undefined,
        }),
        userApi.stats(),
        mailboxApi.listAll(),
        userApi.getTrash(),
        roleApi.list({ pageSize: 1000 }),
      ]);
      setList(Array.isArray(userResp) ? userResp : (userResp?.data ?? []));
      setTotalItems(userResp.totalItems ?? 0);
      setStats(statsResp);
      setMailboxes(Array.isArray(mbResp) ? mbResp : []);
      setTrashList(Array.isArray(trashResp) ? trashResp : []);
      setRoles(roleResp?.data ?? []);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('teamPermissions.users.loadFailedFallback'));
    } finally {
      setLoading(false);
    }
  }, [currentPage, pageSize, searchKeyword, statusFilter, t]);

  useEffect(() => {
    fetch();
  }, [fetch]);

  const openCreate = () => {
    setEditingId(null);
    setForm(emptyForm);
    setFormError('');
    setModalOpen(true);
  };

  const openEdit = async (user: UserResponse) => {
    setEditingId(user.id);
    setForm({
      firstName: user.firstName,
      lastName: user.lastName,
      email: user.email,
      password: '',
      role: user.role,
      mailboxIds: user.mailboxIds ?? [],
    });
    setFormError('');
    setModalOpen(true);

    // 异步加载用户的自定义角色（UserRoles 关联表中的角色）
    try {
      const userRoles = await roleApi.userRoles(user.id);
      const customRole = userRoles.find((r) => !r.isSystem);
      if (customRole) {
        setForm((prev) => ({ ...prev, role: customRole.id }));
      }
    } catch {
      // 加载用户角色失败时静默回退到 User.Role 字段值
    }
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!form.firstName.trim() || !form.lastName.trim()) {
      setFormError(t('teamPermissions.users.errorNameRequired'));
      return;
    }
    if (!form.email.trim()) {
      setFormError(t('teamPermissions.users.errorEmailRequired'));
      return;
    }
    if (!editingId && !form.password.trim()) {
      setFormError(t('teamPermissions.users.errorPasswordRequired'));
      return;
    }
    if (form.password) {
      const pwdErrors = validatePassword(form.password);
      if (pwdErrors.length > 0) {
        setFormError(pwdErrors.join(t('auth.common.validationErrorSeparator')));
        return;
      }
    }

    const payload: CreateUserRequest = {
      firstName: form.firstName.trim(),
      lastName: form.lastName.trim(),
      email: form.email.trim(),
      password: form.password,
      role: form.role,
      mailboxIds: form.mailboxIds,
    };

    setSubmitting(true);
    setFormError('');
    try {
      if (editingId) {
        await userApi.update(editingId, payload);
      } else {
        await userApi.create(payload);
      }
      setModalOpen(false);
      await fetch();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : t('teamPermissions.users.errorSubmitFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    setDeleteError('');
    try {
      await userApi.remove(deleteTarget.id);
      setDeleteTarget(null);
      setDeleteError('');
      await fetch();
    } catch (err: unknown) {
      const errData = getErrorData<{ message?: string }>(err);
      setDeleteError(errData?.message || (err instanceof Error ? err.message : t('teamPermissions.users.errorDeleteFailed')));
    } finally {
      setDeleting(false);
    }
  };

  // 切换用户状态
  const [statusActionTarget, setStatusActionTarget] = useState<{ user: UserResponse; nextStatus: number; nextStatusKey: TranslationKey } | null>(null);
  const [statusChanging, setStatusChanging] = useState(false);

  const handleToggleStatus = async () => {
    if (!statusActionTarget) return;
    setStatusChanging(true);
    try {
      if (statusActionTarget.nextStatus === 1) {
        await userApi.activate(statusActionTarget.user.id);
      } else {
        await userApi.updateStatus(statusActionTarget.user.id, statusActionTarget.nextStatus);
      }
      setStatusActionTarget(null);
      await fetch();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : t('teamPermissions.users.errorStatusChangeFailed'));
    } finally {
      setStatusChanging(false);
    }
  };

  const getNextStatus = (u: UserResponse): { nextStatus: number; nextStatusKey: TranslationKey } | null => {
    if (u.status === 3) return { nextStatus: 1, nextStatusKey: 'teamPermissions.users.actionActivate' };
    if (u.status === 1) return { nextStatus: 2, nextStatusKey: 'teamPermissions.users.actionDisable' };
    if (u.status === 2) return { nextStatus: 1, nextStatusKey: 'teamPermissions.users.actionActivate' };
    return null;
  };

  const handleRestore = async () => {
    if (!restoreTarget) return;
    setRestoring(true);
    try {
      await userApi.restore(restoreTarget.id);
      setRestoreTarget(null);
      await fetch();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : t('teamPermissions.users.errorRestoreFailed'));
    } finally {
      setRestoring(false);
    }
  };

  const handleHardDelete = async () => {
    if (!hardDeleteTarget) return;
    setHardDeleting(true);
    try {
      await userApi.hardDelete(hardDeleteTarget.id);
      setHardDeleteTarget(null);
      await fetch();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : t('teamPermissions.users.errorHardDeleteFailed'));
    } finally {
      setHardDeleting(false);
    }
  };

  const handleClearTrash = async () => {
    setClearingTrash(true);
    try {
      await userApi.clearTrash();
      setClearTrashOpen(false);
      await fetch();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : t('teamPermissions.users.errorClearTrashFailed'));
    } finally {
      setClearingTrash(false);
    }
  };

  const toggleMailbox = (id: number) => {
    setForm((prev) => ({
      ...prev,
      mailboxIds: prev.mailboxIds.includes(id)
        ? prev.mailboxIds.filter((x) => x !== id)
        : [...prev.mailboxIds, id],
    }));
  };

  const handleSearch = () => {
    setSearchKeyword(searchInput.trim());
    setCurrentPage(1);
  };

  const handleResetSearch = () => {
    setSearchInput('');
    setSearchKeyword('');
    setStatusFilter(null);
    setCurrentPage(1);
  };

  const currentList = activeTab === 'list' ? list : trashList;

  const statCards: { labelKey: TranslationKey; value: number; icon: string; color: string; bg: string; text: string }[] = [
    { labelKey: 'teamPermissions.users.statsTotal', value: stats.total, icon: 'M17 20h5v-2a4 4 0 00-3-3.87M9 20H4v-2a4 4 0 013-3.87m6-1.13a4 4 0 10-4-4 4 4 0 004 4z', color: 'from-blue-500 to-indigo-500', bg: 'bg-blue-50 dark:bg-blue-900/20', text: 'text-blue-600 dark:text-blue-400' },
    { labelKey: 'teamPermissions.users.statsActive', value: stats.active, icon: 'M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z', color: 'from-emerald-500 to-teal-500', bg: 'bg-emerald-50 dark:bg-emerald-900/20', text: 'text-emerald-600 dark:text-emerald-400' },
    { labelKey: 'teamPermissions.users.statsDisabled', value: stats.disabled, icon: 'M18.364 18.364A9 9 0 005.636 5.636m12.728 12.728A9 9 0 015.636 5.636m12.728 12.728L5.636 5.636', color: 'from-red-500 to-rose-500', bg: 'bg-red-50 dark:bg-red-900/20', text: 'text-red-600 dark:text-red-400' },
    { labelKey: 'teamPermissions.users.statsPending', value: stats.pending, icon: 'M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z', color: 'from-orange-500 to-amber-500', bg: 'bg-orange-50 dark:bg-orange-900/20', text: 'text-orange-600 dark:text-orange-400' },
  ];

  const filterStatusOptions: { value: number | null; labelKey: TranslationKey }[] = [
    { value: null, labelKey: 'teamPermissions.users.filterAllStatus' },
    { value: 1, labelKey: 'format.status.active' },
    { value: 2, labelKey: 'teamPermissions.users.statusDisabled' },
    { value: 3, labelKey: 'teamPermissions.users.statusPendingActivation' },
    { value: 4, labelKey: 'teamPermissions.users.statusDeleted' },
  ];

  return (
    <div className="space-y-6">
      {/* 页头 */}
      <div className="bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-800 dark:to-slate-800/50 rounded-2xl p-6 border border-primary-100/50 dark:border-slate-700/50">
        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100">{t('teamPermissions.users.title')}</h1>
            <p className="text-sm text-gray-500 dark:text-slate-400 mt-1">
              {t('teamPermissions.users.subtitle')} {stats.total > 0 && <span className="text-gray-400 dark:text-slate-500">{t('teamPermissions.users.subtitleCount', { count: stats.total })}</span>}
            </p>
          </div>
          {isAdmin && (
            <div className="flex items-center gap-2">
              {activeTab === 'trash' && trashList.length > 0 && (
                <button
                  onClick={() => setClearTrashOpen(true)}
                  className="inline-flex items-center justify-center gap-1.5 px-4 py-2.5 text-sm font-medium bg-red-50 hover:bg-red-100 dark:bg-red-900/20 dark:hover:bg-red-900/30 text-red-600 dark:text-red-400 rounded-xl transition-all"
                >
                  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                  </svg>
                  {t('teamPermissions.users.clearTrash')}
                </button>
              )}
              {activeTab === 'list' && (
                <button
                  onClick={openCreate}
                  className="inline-flex items-center justify-center gap-1.5 px-4 py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 text-white rounded-xl shadow-sm shadow-primary-200/50 dark:shadow-primary-900/30 transition-all hover:-translate-y-0.5"
                >
                  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m8-8H4" />
                  </svg>
                  {t('teamPermissions.users.createUser')}
                </button>
              )}
            </div>
          )}
        </div>
      </div>

      {/* 标签页切换 */}
      <div className="flex items-center gap-1 p-1 bg-gray-100 dark:bg-slate-800 rounded-xl w-fit">
        <button
          onClick={() => { setActiveTab('list'); handleResetSearch(); }}
          className={`px-4 py-1.5 text-sm rounded-lg transition flex items-center gap-1.5 ${
            activeTab === 'list'
              ? 'bg-white dark:bg-slate-700 text-primary-600 dark:text-primary-400 shadow-sm font-medium'
              : 'text-gray-600 dark:text-slate-400 hover:text-gray-900 dark:hover:text-slate-200'
          }`}
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M17 20h5v-2a4 4 0 00-3-3.87M9 20H4v-2a4 4 0 013-3.87m6-1.13a4 4 0 10-4-4 4 4 0 004 4z" />
          </svg>
          {t('teamPermissions.users.tabAll')}
          <span className="px-1.5 py-0.5 text-xs rounded-full bg-gray-200 dark:bg-slate-600 text-gray-600 dark:text-slate-300">
            {stats.total}
          </span>
        </button>
        <button
          onClick={() => { setActiveTab('trash'); handleResetSearch(); }}
          className={`px-4 py-1.5 text-sm rounded-lg transition flex items-center gap-1.5 ${
            activeTab === 'trash'
              ? 'bg-white dark:bg-slate-700 text-primary-600 dark:text-primary-400 shadow-sm font-medium'
              : 'text-gray-600 dark:text-slate-400 hover:text-gray-900 dark:hover:text-slate-200'
          }`}
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
          </svg>
          {t('teamPermissions.users.tabTrash')}
          {trashList.length > 0 && (
            <span className="px-1.5 py-0.5 text-xs rounded-full bg-red-100 dark:bg-red-900/30 text-red-600 dark:text-red-400">
              {trashList.length}
            </span>
          )}
        </button>
      </div>

      {/* 统计卡片 */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        {statCards.map((card) => (
          <div
            key={card.labelKey}
            className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 p-5 shadow-sm hover:shadow-md transition-all"
          >
            <div className="flex items-center justify-between mb-3">
              <div className={`w-10 h-10 rounded-xl ${card.bg} flex items-center justify-center`}>
                <svg className={`w-5 h-5 ${card.text}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d={card.icon} />
                </svg>
              </div>
            </div>
            <div className="text-2xl font-bold text-gray-900 dark:text-slate-100">{card.value}</div>
            <div className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t(card.labelKey)}</div>
          </div>
        ))}
      </div>

      {error && (
        <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 px-4 py-3 rounded-xl text-sm flex items-center gap-2">
          <svg className="w-4 h-4 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          {t('teamPermissions.users.loadFailed', { error })}
        </div>
      )}

      {/* 搜索栏 */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 p-4 shadow-sm">
        <div className="flex flex-col sm:flex-row sm:items-center gap-3">
          <div className="relative flex-1">
            <svg className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400 dark:text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
            <input
              type="text"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
              placeholder={t('teamPermissions.users.searchPlaceholder')}
              className="w-full pl-9 pr-4 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 transition-all"
            />
          </div>
          <select
            value={statusFilter ?? ''}
            onChange={(e) => {
              setStatusFilter(e.target.value ? Number(e.target.value) : null);
              setCurrentPage(1);
            }}
            className="px-3 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 transition-all"
          >
            {filterStatusOptions.map((opt) => (
              <option key={opt.labelKey} value={opt.value ?? ''}>{t(opt.labelKey)}</option>
            ))}
          </select>
          <button
            onClick={handleSearch}
            className="px-4 py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 text-white rounded-xl shadow-sm transition-all"
          >
            {t('common.search')}
          </button>
          <button
            onClick={handleResetSearch}
            className="px-4 py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
          >
            {t('common.reset')}
          </button>
        </div>
      </div>

      {/* 用户列表 */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 overflow-hidden shadow-sm">
        {loading ? (
          <div className="p-12 text-center text-gray-500 dark:text-slate-400">
            <svg className="animate-spin w-8 h-8 mx-auto mb-3 text-primary-500" fill="none" viewBox="0 0 24 24">
              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
            </svg>
            {t('common.shell.loading')}
          </div>
        ) : currentList.length === 0 ? (
          <div className="p-12 text-center">
            <svg className="w-12 h-12 mx-auto mb-3 text-gray-300 dark:text-slate-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M17 20h5v-2a4 4 0 00-3-3.87M9 20H4v-2a4 4 0 013-3.87m6-1.13a4 4 0 10-4-4 4 4 0 004 4z" />
            </svg>
            <p className="text-sm text-gray-500 dark:text-slate-400">
              {activeTab === 'trash'
                ? (searchKeyword ? t('teamPermissions.users.emptyNoMatch') : t('teamPermissions.users.emptyTrash'))
                : (searchKeyword || statusFilter !== null ? t('teamPermissions.users.emptyNoMatch') : t('teamPermissions.users.emptyNoUsers'))}
            </p>
          </div>
        ) : (
          <>
          {/* PC 端表格视图 */}
          <div className="hidden md:block overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-100 dark:divide-slate-700/30">
              <thead className="bg-gray-50/50 dark:bg-slate-900/50">
                <tr>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">ID</th>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.users.colName')}</th>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.users.email')}</th>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.users.role')}</th>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.users.colStatus')}</th>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">
                    {activeTab === 'trash' ? t('teamPermissions.users.colDeletedAt') : t('teamPermissions.users.colCreatedAt')}
                  </th>
                  <th className="px-5 py-3.5 text-right text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.users.colActions')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 dark:divide-slate-700/30 virtual-list">
                {currentList.map((u, index) => {
                  const status = statusMap[u.status] ?? UNKNOWN_STATUS;
                  return (
                    <tr key={u.id} className={`transition-all duration-150 ${index % 2 === 0 ? 'bg-white dark:bg-slate-800' : 'bg-gray-50/50 dark:bg-slate-800/50'} hover:bg-primary-50/50 dark:hover:bg-slate-700/50`}>
                      <td className="px-5 py-3.5 text-sm text-gray-500 dark:text-slate-400 font-mono">#{u.id}</td>
                      <td className="px-5 py-3.5">
                        <div className="flex items-center gap-3">
                          <UserAvatar userId={u.id} name={u.fullName} size="sm" avatarUrl={u.avatar ?? null} />
                          <span className="text-sm font-medium text-gray-900 dark:text-slate-100">{u.fullName}</span>
                        </div>
                      </td>
                      <td className="px-5 py-3.5 text-sm text-gray-700 dark:text-slate-300 truncate max-w-xs">{u.email}</td>
                      <td className="px-5 py-3.5">
                        <span className={`inline-flex items-center px-2.5 py-1 text-xs font-medium rounded-full ring-1 ${roleStyle[u.role] ?? roleStyle[3]}`}>
                          {getRoleLabel(u.role)}
                        </span>
                      </td>
                      <td className="px-5 py-3.5">
                        <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium rounded-full ring-1 ${status.color}`}>
                          <span className={`w-1.5 h-1.5 rounded-full ${status.dot}`} />
                          {t(status.labelKey)}
                        </span>
                      </td>
                      <td className="px-5 py-3.5 text-sm text-gray-500 dark:text-slate-400 whitespace-nowrap">
                        {activeTab === 'trash' && u.deletedAt ? (
                          <span className="text-red-500 dark:text-red-400">{formatDate(u.deletedAt)}</span>
                        ) : (
                          formatDate(u.createdAt)
                        )}
                      </td>
                      <td className="px-5 py-3.5 text-right">
                        {isAdmin && (
                          <div className="flex items-center justify-end gap-1">
                            {activeTab === 'trash' ? (
                              <>
                                <button
                                  onClick={() => setRestoreTarget(u)}
                                  className="p-2 text-gray-400 dark:text-slate-500 hover:text-emerald-600 hover:bg-emerald-50 dark:hover:bg-emerald-900/20 rounded-lg transition-all"
                                  title={t('teamPermissions.users.restore')}
                                >
                                  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                                    <path strokeLinecap="round" strokeLinejoin="round" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                                  </svg>
                                </button>
                                <button
                                  onClick={() => setHardDeleteTarget(u)}
                                  className="p-2 text-gray-400 dark:text-slate-500 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg transition-all"
                                  title={t('teamPermissions.users.hardDelete')}
                                >
                                  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                                    <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                                  </svg>
                                </button>
                              </>
                            ) : (
                              <>
                                {getNextStatus(u) && (
                                  <button
                                    onClick={() => {
                                      const next = getNextStatus(u);
                                      if (next) {
                                        setStatusActionTarget({ user: u, nextStatus: next.nextStatus, nextStatusKey: next.nextStatusKey });
                                      }
                                    }}
                                    className={`p-2 rounded-lg transition-all ${
                                      u.status === 1
                                        ? 'text-amber-600 hover:text-amber-700 hover:bg-amber-50 dark:hover:bg-amber-900/20'
                                        : 'text-emerald-600 hover:text-emerald-700 hover:bg-emerald-50 dark:hover:bg-emerald-900/20'
                                    }`}
                                    title={u.status === 3 ? t('teamPermissions.users.actionActivateThisUser') : u.status === 1 ? t('teamPermissions.users.actionDisableThisUser') : u.status === 2 ? t('teamPermissions.users.actionActivateThisUser') : ''}
                                  >
                                    {u.status === 1 ? (
                                      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                                        <path strokeLinecap="round" strokeLinejoin="round" d="M18.364 18.364A9 9 0 005.636 5.636m12.728 12.728A9 9 0 015.636 5.636m12.728 12.728L5.636 5.636" />
                                      </svg>
                                    ) : (
                                      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                                        <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                                      </svg>
                                    )}
                                  </button>
                                )}
                                <button
                                  onClick={() => openEdit(u)}
                                  className="p-2 text-gray-400 dark:text-slate-500 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/20 rounded-lg transition-all"
                                  title={t('common.edit')}
                                >
                                  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                                    <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                                  </svg>
                                </button>
                                <button
                                  onClick={() => setDeleteTarget(u)}
                                  className="p-2 text-gray-400 dark:text-slate-500 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg transition-all"
                                  title={t('common.delete')}
                                >
                                  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                                    <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                                  </svg>
                                </button>
                              </>
                            )}
                          </div>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          {/* 移动端卡片视图 */}
          <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/30">
            {currentList.map((u, index) => {
              const status = statusMap[u.status] ?? UNKNOWN_STATUS;
              return (
                <div key={u.id} className={`p-4 ${index % 2 === 0 ? 'bg-white dark:bg-slate-800' : 'bg-gray-50/50 dark:bg-slate-800/50'} hover:bg-primary-50/50 dark:hover:bg-slate-700/50 transition-all`}>
                  <div className="flex items-center justify-between gap-3 mb-3">
                    <div className="flex items-center gap-3 min-w-0">
                      <UserAvatar userId={u.id} name={u.fullName} size="sm" avatarUrl={u.avatar ?? null} />
                      <div className="min-w-0">
                        <div className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate">{u.fullName}</div>
                        <div className="text-xs text-gray-500 dark:text-slate-400 font-mono">#{u.id}</div>
                      </div>
                    </div>
                    <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium rounded-full ring-1 ${status.color} flex-shrink-0`}>
                      <span className={`w-1.5 h-1.5 rounded-full ${status.dot}`} />
                      {t(status.labelKey)}
                    </span>
                  </div>
                  <div className="text-sm text-gray-700 dark:text-slate-300 truncate mb-2">{u.email}</div>
                  <div className="flex items-center gap-2 mb-3 flex-wrap">
                    <span className={`inline-flex items-center px-2.5 py-1 text-xs font-medium rounded-full ring-1 ${roleStyle[u.role] ?? roleStyle[3]}`}>
                      {getRoleLabel(u.role)}
                    </span>
                    <span className="text-xs text-gray-500 dark:text-slate-400">
                      {activeTab === 'trash' && u.deletedAt ? (
                        <span className="text-red-500 dark:text-red-400">{formatDate(u.deletedAt)}</span>
                      ) : (
                        formatDate(u.createdAt)
                      )}
                    </span>
                  </div>
                  {isAdmin && (
                    <div className="flex items-center gap-1.5 pt-3 border-t border-gray-100 dark:border-slate-700/50">
                      {activeTab === 'trash' ? (
                        <>
                          <button
                            onClick={() => setRestoreTarget(u)}
                            className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-medium text-emerald-600 bg-emerald-50 dark:bg-emerald-900/20 rounded-lg hover:bg-emerald-100 dark:hover:bg-emerald-900/30 transition-all"
                          >
                            {t('teamPermissions.users.restore')}
                          </button>
                          <button
                            onClick={() => setHardDeleteTarget(u)}
                            className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-medium text-red-600 bg-red-50 dark:bg-red-900/20 rounded-lg hover:bg-red-100 dark:hover:bg-red-900/30 transition-all"
                          >
                            {t('teamPermissions.users.hardDelete')}
                          </button>
                        </>
                      ) : (
                        <>
                          {getNextStatus(u) && (
                            <button
                              onClick={() => {
                                const next = getNextStatus(u);
                                if (next) {
                                  setStatusActionTarget({ user: u, nextStatus: next.nextStatus, nextStatusKey: next.nextStatusKey });
                                }
                              }}
                              className={`flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-medium rounded-lg transition-all ${
                                u.status === 1
                                  ? 'text-amber-600 bg-amber-50 dark:bg-amber-900/20 hover:bg-amber-100 dark:hover:bg-amber-900/30'
                                  : 'text-emerald-600 bg-emerald-50 dark:bg-emerald-900/20 hover:bg-emerald-100 dark:hover:bg-emerald-900/30'
                              }`}
                            >
                              {u.status === 1 ? t('teamPermissions.users.actionDisable') : t('teamPermissions.users.actionActivate')}
                            </button>
                          )}
                          <button
                            onClick={() => openEdit(u)}
                            className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-medium text-primary-600 bg-primary-50 dark:bg-primary-900/20 rounded-lg hover:bg-primary-100 dark:hover:bg-primary-900/30 transition-all"
                          >
                            {t('common.edit')}
                          </button>
                          <button
                            onClick={() => setDeleteTarget(u)}
                            className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-medium text-red-600 bg-red-50 dark:bg-red-900/20 rounded-lg hover:bg-red-100 dark:hover:bg-red-900/30 transition-all"
                          >
                            {t('common.delete')}
                          </button>
                        </>
                      )}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
          {activeTab === 'list' && totalPages > 1 && (
            <div className="px-5 py-3.5 border-t border-gray-100 dark:border-slate-700/50 flex items-center justify-between bg-gray-50/30 dark:bg-slate-900/30">
              <span className="text-sm text-gray-500 dark:text-slate-400">
                {t('teamPermissions.users.pagination', { total: totalItems, page: currentPage, pages: totalPages })}
              </span>
              <div className="flex gap-2">
                <button
                  onClick={() => setCurrentPage((p) => Math.max(1, p - 1))}
                  disabled={currentPage === 1}
                  className="px-3 py-1.5 text-sm border border-gray-200 dark:border-slate-600 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
                >
                  {t('teamPermissions.users.prevPage')}
                </button>
                <button
                  onClick={() => setCurrentPage((p) => Math.min(totalPages, p + 1))}
                  disabled={currentPage === totalPages}
                  className="px-3 py-1.5 text-sm border border-gray-200 dark:border-slate-600 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
                >
                  {t('teamPermissions.users.nextPage')}
                </button>
              </div>
            </div>
          )}
          </>
        )}
      </div>

      {/* 新建/编辑弹窗 */}
      <Modal
        open={modalOpen}
        title={editingId ? t('teamPermissions.users.editUser') : t('teamPermissions.users.createUser')}
        onClose={() => setModalOpen(false)}
        size="lg"
        footer={
          <>
            <button
              onClick={() => setModalOpen(false)}
              disabled={submitting}
              className="px-4 py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
            >
              {t('common.cancel')}
            </button>
            <button
              type="submit"
              form="user-form"
              disabled={submitting}
              className="px-4 py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white rounded-xl shadow-sm shadow-primary-200/50 dark:shadow-primary-900/30 transition-all"
            >
              {submitting ? t('teamPermissions.users.saving') : t('common.save')}
            </button>
          </>
        }
      >
        <form id="user-form" onSubmit={handleSubmit} className="space-y-6">
          {formError && (
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 px-4 py-3 rounded-xl text-sm flex items-center gap-2">
              <svg className="w-4 h-4 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              {formError}
            </div>
          )}

          {/* 基本信息 */}
          <div className="space-y-4">
            <div className="flex items-center gap-2 pb-2 border-b border-gray-100 dark:border-slate-700/50">
              <svg className="w-4 h-4 text-primary-600 dark:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
              </svg>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('teamPermissions.users.basicInfo')}</h3>
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
                  {t('teamPermissions.users.firstName')} <span className="text-red-500">*</span>
                </label>
                <input
                  type="text"
                  required
                  value={form.firstName}
                  onChange={(e) => setForm({ ...form, firstName: e.target.value })}
                  className={inputClass}
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
                  {t('teamPermissions.users.lastName')} <span className="text-red-500">*</span>
                </label>
                <input
                  type="text"
                  required
                  value={form.lastName}
                  onChange={(e) => setForm({ ...form, lastName: e.target.value })}
                  className={inputClass}
                />
              </div>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
                {t('teamPermissions.users.email')} <span className="text-red-500">*</span>
              </label>
              <input
                type="email"
                required
                value={form.email}
                onChange={(e) => setForm({ ...form, email: e.target.value })}
                className={inputClass}
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
                {t('teamPermissions.users.password')} {editingId && <span className="text-gray-400 dark:text-slate-500 text-xs">{t('teamPermissions.users.passwordKeepHint')}</span>}
                {!editingId && <span className="text-red-500">*</span>}
              </label>
              <input
                type="password"
                value={form.password}
                onChange={(e) => setForm({ ...form, password: e.target.value })}
                placeholder={editingId ? t('teamPermissions.users.passwordNewPlaceholder') : t('teamPermissions.users.passwordMinLength', { count: policy?.minLength ?? 6 })}
                className={inputClass}
              />
              <PasswordPolicyHint policy={policy} password={form.password} />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('teamPermissions.users.role')}</label>
              <select
                value={form.role}
                onChange={(e) => setForm({ ...form, role: Number(e.target.value) })}
                className={inputClass}
              >
                {roleOptions.map((r) => (
                  <option key={r.value} value={r.value}>{r.labelKey ? t(r.labelKey) : r.name}</option>
                ))}
              </select>
              <p className="mt-2 text-xs text-gray-400 dark:text-slate-500 flex items-center gap-1">
                <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
                {t('teamPermissions.users.rolePermissionHint')}
              </p>
            </div>
          </div>

          {/* 邮箱访问 */}
          <div className="space-y-4">
            <div className="flex items-center gap-2 pb-2 border-b border-gray-100 dark:border-slate-700/50">
              <svg className="w-4 h-4 text-primary-600 dark:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
              </svg>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('teamPermissions.users.mailboxAccess')}</h3>
            </div>

            {mailboxes.length > 0 && (
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('teamPermissions.users.accessibleMailboxes')}</label>
                <div className="space-y-1 max-h-40 overflow-y-auto p-2.5 border border-gray-200 dark:border-slate-600 rounded-xl bg-gray-50/50 dark:bg-slate-900/50">
                  {mailboxes.map((m) => (
                    <label key={m.id} className="flex items-center gap-2.5 px-2.5 py-2 hover:bg-white dark:hover:bg-slate-700 rounded-lg cursor-pointer transition-colors">
                      <input
                        type="checkbox"
                        checked={form.mailboxIds.includes(m.id)}
                        onChange={() => toggleMailbox(m.id)}
                        className="w-4 h-4 text-primary-600 rounded focus:ring-primary-500"
                      />
                      <span className="text-sm font-medium text-gray-700 dark:text-slate-300">{m.name}</span>
                      <span className="text-xs text-gray-400 dark:text-slate-500 truncate">{m.email}</span>
                    </label>
                  ))}
                </div>
              </div>
            )}
          </div>

          {/* 角色权限展示 */}
          <div className="space-y-4 mt-4 pt-4 border-t border-gray-100 dark:border-slate-700/50">
            <div className="flex items-center gap-2">
              <svg className="w-4 h-4 text-primary-600 dark:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
              </svg>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('teamPermissions.users.rolePermissions')}</h3>
              <span className="text-xs text-gray-400 dark:text-slate-500 ml-auto">{t('teamPermissions.users.readOnly')}</span>
            </div>
            <div className="flex flex-wrap gap-2">
              {/* 系统角色显示简化权限描述，自定义角色显示 PermissionsJson 中的权限键 */}
              {(() => {
                const selectedRole = roles.find((r) => {
                  const systemValue = SYSTEM_ROLE_SLUG_MAP[r.slug];
                  return (systemValue ?? r.id) === form.role;
                });
                if (selectedRole && selectedRole.permissions && selectedRole.permissions.length > 0) {
                  return selectedRole.permissions.map((perm) => (
                    <span
                      key={`perm-${perm}`}
                      className="px-3 py-1.5 text-xs font-medium bg-gray-100 dark:bg-slate-700/50 text-gray-600 dark:text-slate-300 rounded-lg border border-gray-200 dark:border-slate-600/50"
                    >
                      {perm}
                    </span>
                  ));
                }
                return (rolePermissionKeys[form.role] ?? []).map((permKey) => (
                  <span
                    key={`roleperm-${permKey}`}
                    className="px-3 py-1.5 text-xs font-medium bg-gray-100 dark:bg-slate-700/50 text-gray-600 dark:text-slate-300 rounded-lg border border-gray-200 dark:border-slate-600/50"
                  >
                    {t(permKey)}
                  </span>
                ));
              })()}
            </div>
          </div>
        </form>
      </Modal>

      {/* 删除确认 */}
      <ConfirmDialog
        open={!!deleteTarget}
        title={t('teamPermissions.users.deleteUser')}
        message={t('teamPermissions.users.deleteConfirmMessage', { name: deleteTarget?.fullName ?? '' })}
        confirmText={t('common.delete')}
        variant="danger"
        loading={deleting}
        error={deleteError || undefined}
        onConfirm={handleDelete}
        onCancel={() => { setDeleteTarget(null); setDeleteError(''); }}
      />

      {/* 恢复确认 */}
      <ConfirmDialog
        open={!!restoreTarget}
        title={t('teamPermissions.users.restoreUser')}
        message={t('teamPermissions.users.restoreConfirmMessage', { name: restoreTarget?.fullName ?? '' })}
        confirmText={t('teamPermissions.users.restore')}
        variant="primary"
        loading={restoring}
        onConfirm={handleRestore}
        onCancel={() => setRestoreTarget(null)}
      />

      {/* 彻底删除确认 */}
      <ConfirmDialog
        open={!!hardDeleteTarget}
        title={t('teamPermissions.users.hardDeleteUser')}
        message={t('teamPermissions.users.hardDeleteConfirmMessage', { name: hardDeleteTarget?.fullName ?? '' })}
        confirmText={t('teamPermissions.users.hardDelete')}
        variant="danger"
        loading={hardDeleting}
        onConfirm={handleHardDelete}
        onCancel={() => setHardDeleteTarget(null)}
      />

      {/* 清空垃圾箱确认 */}
      <ConfirmDialog
        open={clearTrashOpen}
        title={t('teamPermissions.users.clearTrash')}
        message={t('teamPermissions.users.clearTrashConfirmMessage', { count: trashList.length })}
        confirmText={t('teamPermissions.users.clearTrashConfirmText')}
        variant="danger"
        loading={clearingTrash}
        onConfirm={handleClearTrash}
        onCancel={() => setClearTrashOpen(false)}
      />

      {/* 状态切换确认 */}
      <ConfirmDialog
        open={!!statusActionTarget}
        title={statusActionTarget?.nextStatus === 1 ? t('teamPermissions.users.actionActivateUser') : t('teamPermissions.users.actionDisableUser')}
        message={
          statusActionTarget?.nextStatus === 1
            ? t('teamPermissions.users.statusToActiveConfirmMessage', {
                name: statusActionTarget.user.fullName,
                status: t((statusMap[statusActionTarget.user.status] ?? UNKNOWN_STATUS).labelKey),
              })
            : t('teamPermissions.users.statusToDisabledConfirmMessage', { name: statusActionTarget?.user.fullName ?? '' })
        }
        confirmText={statusActionTarget ? t(statusActionTarget.nextStatusKey) : t('common.confirm')}
        variant={statusActionTarget?.nextStatus === 1 ? 'primary' : 'danger'}
        loading={statusChanging}
        onConfirm={handleToggleStatus}
        onCancel={() => setStatusActionTarget(null)}
      />
    </div>
  );
}
