import { useState, useEffect } from 'react';
import { teamApi, userApi, mailboxApi } from '../api';
import type {
  TeamDto,
  TeamDetailDto,
  TeamOverviewResponse,
  TeamMemberDto,
  CreateTeamRequest,
  UpdateTeamRequest,
  UserResponse,
  MailboxResponse,
} from '../api/types';
import UserAvatar from '../components/UserAvatar';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import { isAdmin } from '../utils/permissions';
import { useToast } from '../context/ToastContext';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import { formatDateTime } from '../utils/format';

/**
 * 团队内成员角色 code → 展示文案 key。
 *
 * 取值来自后端 `TeamMemberRole`（`NoneadCSS.Domain/Enums/TeamMemberRole.cs:5-6`：
 * `Member = 1` / `Leader = 2`），与 `utils/permissions.ts` 的**用户角色** `UserRoleType`
 * （真源 `format.role.*`）不是同一张表，故本页不复用 `format.role.*`。
 * 类型写 `TranslationKey | undefined`：本仓库未开 `noUncheckedIndexedAccess`，
 * 窄类型会让「未知码回落」分支在类型上不可达。
 */
const TEAM_MEMBER_ROLE_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  1: 'teamPermissions.teams.role.member',
  2: 'teamPermissions.teams.role.leader',
};

/** 角色下拉选项（新增成员 / 调整角色共用）：`value` 为后端 code，`labelKey` 指向真源 */
const ROLE_OPTIONS: { value: number; labelKey: TranslationKey | undefined }[] = Object.keys(
  TEAM_MEMBER_ROLE_LABEL_KEYS
).map(code => ({ value: Number(code), labelKey: TEAM_MEMBER_ROLE_LABEL_KEYS[code] }));

const inputClass = "w-full px-3 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 transition-all";

export default function Teams() {
  const [activeTab, setActiveTab] = useState<'overview' | 'list'>('overview');
  const [overview, setOverview] = useState<TeamOverviewResponse | null>(null);
  const [teams, setTeams] = useState<TeamDto[]>([]);
  const toast = useToast();
  const { t, language } = useI18n();
  const [loading, setLoading] = useState(false);
  const [totalItems, setTotalItems] = useState(0);
  const [page, setPage] = useState(1);
  const [keyword, setKeyword] = useState('');
  const [searchInput, setSearchInput] = useState('');
  const [mailboxFilter, setMailboxFilter] = useState<number | null>(null);
  const [activeFilter, setActiveFilter] = useState<boolean | null>(null);
  const pageSize = 10;

  const [showEditor, setShowEditor] = useState(false);
  const [editingTeam, setEditingTeam] = useState<TeamDto | null>(null);
  const [formData, setFormData] = useState<CreateTeamRequest>({
    name: '',
    description: '',
    mailboxId: null,
    isActive: true,
    sortOrder: 0,
  });
  const [formError, setFormError] = useState('');

  const [showMembers, setShowMembers] = useState(false);
  const [currentTeam, setCurrentTeam] = useState<TeamDetailDto | null>(null);
  const [members, setMembers] = useState<TeamMemberDto[]>([]);
  const [users, setUsers] = useState<UserResponse[]>([]);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [selectedUserId, setSelectedUserId] = useState<number>(0);
  const [selectedRole, setSelectedRole] = useState(1);
  const [showAddMember, setShowAddMember] = useState(false);

  // 垃圾箱相关状态
  const [trashOpen, setTrashOpen] = useState(false);
  const [trashList, setTrashList] = useState<TeamDto[]>([]);
  const [trashLoading, setTrashLoading] = useState(false);
  const [trashError, setTrashError] = useState('');
  const [trashSearch, setTrashSearch] = useState('');
  const [trashPage, setTrashPage] = useState(1);
  const [trashPageSize] = useState(10);
  const [trashTotalItems, setTrashTotalItems] = useState(0);
  const [trashTotalPages, setTrashTotalPages] = useState(0);
  const [trashCount, setTrashCount] = useState(0);

  const [restoreTarget, setRestoreTarget] = useState<TeamDto | null>(null);
  const [restoring, setRestoring] = useState(false);

  const [hardDeleteTarget, setHardDeleteTarget] = useState<TeamDto | null>(null);
  const [hardDeleting, setHardDeleting] = useState(false);

  const [clearTrashOpen, setClearTrashOpen] = useState(false);
  const [clearingTrash, setClearingTrash] = useState(false);

  const [deleteTarget, setDeleteTarget] = useState<TeamDto | null>(null);
  const [deleting, setDeleting] = useState(false);

  useEffect(() => {
    loadUsers();
    loadMailboxes();
  }, []);

  useEffect(() => {
    if (activeTab === 'overview') loadOverview();
    if (activeTab === 'list') loadList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab]);

  useEffect(() => {
    if (activeTab === 'list') loadList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, keyword, mailboxFilter, activeFilter]);

  async function loadUsers() {
    try {
      const result = await userApi.list();
      setUsers(Array.isArray(result?.data) ? result.data : []);
    } catch (e: unknown) {
      console.error('加载用户失败', e);
    }
  }

  async function loadMailboxes() {
    try {
      const data = await mailboxApi.listAll();
      setMailboxes(data);
    } catch (e: unknown) {
      console.error('加载邮箱失败', e);
    }
  }

  async function loadOverview() {
    setLoading(true);
    try {
      const [data, trashCountResp] = await Promise.all([teamApi.overview(), teamApi.getTrashCount()]);
      setOverview(data);
      setTrashCount(trashCountResp?.count ?? 0);
    } finally {
      setLoading(false);
    }
  }

  async function loadList() {
    setLoading(true);
    try {
      const [data, trashCountResp] = await Promise.all([
        teamApi.list({
          page,
          pageSize,
          keyword: keyword || null,
          mailboxId: mailboxFilter,
          isActive: activeFilter,
        }),
        teamApi.getTrashCount(),
      ]);
      setTeams(data.data);
      setTotalItems(data.totalItems);
      setTrashCount(trashCountResp?.count ?? 0);
    } finally {
      setLoading(false);
    }
  }

  async function loadTrash() {
    setTrashLoading(true);
    setTrashError('');
    try {
      const data = await teamApi.getTrash(trashPage, trashPageSize, trashSearch);
      setTrashList(data.data ?? []);
      setTrashTotalItems(data.totalItems ?? 0);
      setTrashTotalPages(data.totalPages ?? 0);
      const countResp = await teamApi.getTrashCount();
      setTrashCount(countResp?.count ?? 0);
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } }; message?: string };
      setTrashError(errResp?.response?.data?.message || errResp?.message || t('teamPermissions.teams.trash.loadFailed'));
    } finally {
      setTrashLoading(false);
    }
  }

  function openTrashModal() {
    setTrashPage(1);
    setTrashSearch('');
    setTrashOpen(true);
    loadTrash();
  }

  async function handleRestore() {
    if (!restoreTarget) return;
    setRestoring(true);
    try {
      await teamApi.restore(restoreTarget.id);
      setRestoreTarget(null);
      await loadTrash();
      if (activeTab === 'list') await loadList();
      if (activeTab === 'overview') await loadOverview();
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } }; message?: string };
      setTrashError(errResp?.response?.data?.message || errResp?.message || t('teamPermissions.teams.trash.restoreFailed'));
    } finally {
      setRestoring(false);
    }
  }

  async function handleHardDelete() {
    if (!hardDeleteTarget) return;
    setHardDeleting(true);
    try {
      await teamApi.hardDelete(hardDeleteTarget.id);
      setHardDeleteTarget(null);
      await loadTrash();
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } }; message?: string };
      setTrashError(errResp?.response?.data?.message || errResp?.message || t('teamPermissions.teams.trash.hardDeleteFailed'));
    } finally {
      setHardDeleting(false);
    }
  }

  async function handleClearTrash() {
    setClearingTrash(true);
    try {
      await teamApi.clearTrash();
      setTrashList([]);
      setTrashCount(0);
      setTrashTotalItems(0);
      setTrashTotalPages(0);
      setClearTrashOpen(false);
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } }; message?: string };
      setTrashError(errResp?.response?.data?.message || errResp?.message || t('teamPermissions.teams.trash.clearFailed'));
    } finally {
      setClearingTrash(false);
    }
  }

  function openCreate() {
    setEditingTeam(null);
    setFormData({
      name: '',
      description: '',
      mailboxId: null,
      isActive: true,
      sortOrder: 0,
    });
    setFormError('');
    setShowEditor(true);
  }

  function openEdit(team: TeamDto) {
    setEditingTeam(team);
    setFormData({
      name: team.name,
      description: team.description,
      mailboxId: team.mailboxId,
      isActive: team.isActive,
      sortOrder: team.sortOrder,
    });
    setFormError('');
    setShowEditor(true);
  }

  async function handleSave() {
    if (!formData.name.trim()) {
      setFormError(t('teamPermissions.teams.form.nameRequired'));
      return;
    }

    try {
      if (editingTeam) {
        await teamApi.update(editingTeam.id, formData as UpdateTeamRequest);
      } else {
        await teamApi.create(formData);
      }
      setShowEditor(false);
      if (activeTab === 'list') loadList();
      if (activeTab === 'overview') loadOverview();
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } } };
      setFormError(errResp?.response?.data?.message || t('teamPermissions.teams.form.saveFailed'));
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await teamApi.remove(deleteTarget.id);
      setDeleteTarget(null);
      setTrashCount((c) => c + 1);
      if (activeTab === 'list') loadList();
      if (activeTab === 'overview') loadOverview();
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } } };
      toast.error(errResp?.response?.data?.message || t('teamPermissions.teams.deleteFailed'));
    } finally {
      setDeleting(false);
    }
  }

  async function openMembers(team: TeamDto) {
    try {
      const detail = await teamApi.get(team.id);
      setCurrentTeam(detail);
      setMembers(detail.members);
      setShowMembers(true);
      setShowAddMember(false);
    } catch (e: unknown) {
      console.error('加载团队详情失败', e);
      toast.error(t('teamPermissions.teams.loadFailed'));
    }
  }

  async function handleAddMember() {
    if (!currentTeam || !selectedUserId) {
      toast.error(t('teamPermissions.teams.members.selectUserRequired'));
      return;
    }

    try {
      const member = await teamApi.addMember(currentTeam.id, {
        userId: selectedUserId,
        role: selectedRole,
      });
      setMembers([...members, member]);
      setShowAddMember(false);
      setSelectedUserId(0);
      if (activeTab === 'list') loadList();
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } } };
      toast.error(errResp?.response?.data?.message || t('teamPermissions.teams.members.addFailed'));
    }
  }

  async function handleChangeRole(member: TeamMemberDto, role: number) {
    if (!currentTeam) return;
    try {
      const updated = await teamApi.updateMemberRole(currentTeam.id, member.userId, role);
      setMembers(members.map(m => (m.id === updated.id ? updated : m)));
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } } };
      toast.error(errResp?.response?.data?.message || t('teamPermissions.teams.members.updateRoleFailed'));
    }
  }

  async function handleRemoveMember(member: TeamMemberDto) {
    if (!currentTeam) return;
    if (!confirm(t('teamPermissions.teams.members.removeConfirm', { name: member.userName }))) return;
    try {
      await teamApi.removeMember(currentTeam.id, member.userId);
      setMembers(members.filter(m => m.id !== member.id));
      if (activeTab === 'list') loadList();
    } catch (e: unknown) {
      const errResp = e as { response?: { data?: { message?: string } } };
      toast.error(errResp?.response?.data?.message || t('teamPermissions.teams.members.removeFailed'));
    }
  }

  function handleSearch() {
    setKeyword(searchInput.trim());
    setPage(1);
  }

  function handleResetSearch() {
    setSearchInput('');
    setKeyword('');
    setMailboxFilter(null);
    setActiveFilter(null);
    setPage(1);
  }

  const totalPages = Math.ceil(totalItems / pageSize);
  const availableUsers = users.filter(
    u => !members.some(m => m.userId === u.id)
  );

  const statCards: { labelKey: TranslationKey; value: number; icon: string; bg: string; text: string }[] = overview ? [
    { labelKey: 'teamPermissions.teams.overview.totalTeams', value: overview.totalTeams, icon: 'M17 20h5v-2a4 4 0 00-3-3.87M9 20H4v-2a4 4 0 013-3.87m6-1.13a4 4 0 10-4-4 4 4 0 004 4z', bg: 'bg-blue-50 dark:bg-blue-900/20', text: 'text-blue-600 dark:text-blue-400' },
    { labelKey: 'teamPermissions.teams.status.active', value: overview.activeTeams, icon: 'M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z', bg: 'bg-emerald-50 dark:bg-emerald-900/20', text: 'text-emerald-600 dark:text-emerald-400' },
    { labelKey: 'teamPermissions.teams.overview.totalMembers', value: overview.totalMembers, icon: 'M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z', bg: 'bg-purple-50 dark:bg-purple-900/20', text: 'text-purple-600 dark:text-purple-400' },
    { labelKey: 'teamPermissions.teams.overview.teamsPerMailbox', value: overview.teamsPerMailbox, icon: 'M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z', bg: 'bg-amber-50 dark:bg-amber-900/20', text: 'text-amber-600 dark:text-amber-400' },
  ] : [];

  return (
    <div className="space-y-6">
      {/* 页头 */}
      <div className="bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-800 dark:to-slate-800/50 rounded-2xl p-6 border border-primary-100/50 dark:border-slate-700/50">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100">{t('teamPermissions.teams.page.title')}</h1>
          <p className="text-sm text-gray-500 dark:text-slate-400 mt-1">{t('teamPermissions.teams.page.subtitle')}</p>
        </div>
      </div>

      {/* Tab 切换 */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 p-1.5 shadow-sm inline-flex">
        <button
          className={`px-5 py-2 text-sm font-medium rounded-xl transition-all ${
            activeTab === 'overview'
              ? 'bg-primary-600 text-white shadow-sm'
              : 'text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-200'
          }`}
          onClick={() => setActiveTab('overview')}
        >
          {t('teamPermissions.teams.tabs.overview')}
        </button>
        <button
          className={`px-5 py-2 text-sm font-medium rounded-xl transition-all ${
            activeTab === 'list'
              ? 'bg-primary-600 text-white shadow-sm'
              : 'text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-200'
          }`}
          onClick={() => setActiveTab('list')}
        >
          {t('teamPermissions.teams.tabs.list')}
        </button>
      </div>

      {activeTab === 'overview' && (
        <div className="space-y-6">
          {loading && (
            <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 p-12 text-center">
              <svg className="animate-spin w-8 h-8 mx-auto mb-3 text-primary-500" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
              </svg>
              <p className="text-sm text-gray-500 dark:text-slate-400">{t('common.shell.loading')}</p>
            </div>
          )}

          {overview && (
            <>
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

              {/* 活跃团队 TOP 5 */}
              <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 p-6 shadow-sm">
                <div className="flex items-center justify-between mb-4">
                  <div className="flex items-center gap-2">
                    <svg className="w-5 h-5 text-primary-600 dark:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M11.049 2.927c.3-.921 1.603-.921 1.902 0l1.519 4.674a1 1 0 00.95.69h4.915c.969 0 1.371 1.24.588 1.81l-3.976 2.888a1 1 0 00-.363 1.118l1.518 4.674c.3.922-.755 1.688-1.538 1.118l-3.976-2.888a1 1 0 00-1.176 0l-3.976 2.888c-.783.57-1.838-.197-1.538-1.118l1.518-4.674a1 1 0 00-.363-1.118l-3.976-2.888c-.784-.57-.38-1.81.588-1.81h4.914a1 1 0 00.951-.69l1.519-4.674z" />
                    </svg>
                    <h3 className="text-base font-semibold text-gray-900 dark:text-slate-100">{t('teamPermissions.teams.overview.topTeams')}</h3>
                  </div>
                  <button
                    onClick={() => setActiveTab('list')}
                    className="text-sm text-primary-600 hover:text-primary-700 dark:text-primary-400 font-medium"
                  >
                    {t('teamPermissions.teams.overview.viewAll')}
                  </button>
                </div>
                {overview.topTeams.length === 0 ? (
                  <div className="text-center py-10">
                    <svg className="w-12 h-12 mx-auto mb-3 text-gray-300 dark:text-slate-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M17 20h5v-2a4 4 0 00-3-3.87M9 20H4v-2a4 4 0 013-3.87m6-1.13a4 4 0 10-4-4 4 4 0 004 4z" />
                    </svg>
                    <p className="text-sm text-gray-500 dark:text-slate-400">{t('teamPermissions.teams.empty.noTeams')}</p>
                  </div>
                ) : (
                  <div className="space-y-2">
                    {overview.topTeams.map((team, index) => (
                      <div key={team.id} className="flex items-center justify-between p-3.5 bg-gray-50/70 dark:bg-slate-900/40 rounded-xl hover:bg-primary-50/50 dark:hover:bg-slate-700/40 transition-all">
                        <div className="flex items-center gap-3">
                          <div className={`w-10 h-10 rounded-xl flex items-center justify-center font-bold text-sm ${index < 3 ? 'bg-gradient-to-br from-primary-500 to-indigo-500 text-white' : 'bg-primary-100 text-primary-600 dark:bg-primary-900/30 dark:text-primary-400'}`}>
                            {team.name.charAt(0)}
                          </div>
                          <div>
                            <p className="font-medium text-gray-900 dark:text-slate-100">{team.name}</p>
                            <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 flex items-center gap-2">
                              <span className="flex items-center gap-1">
                                <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z" />
                                </svg>
                                {t('teamPermissions.teams.overview.memberCount', { count: team.memberCount })}
                              </span>
                              <span className="flex items-center gap-1">
                                <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                  <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
                                </svg>
                                {t('teamPermissions.teams.overview.leaderCount', { count: team.leaderCount })}
                              </span>
                            </p>
                          </div>
                        </div>
                        <button
                          onClick={() => {
                            setActiveTab('list');
                          }}
                          className="text-sm text-primary-600 hover:text-primary-700 dark:text-primary-400 font-medium px-3 py-1.5 rounded-lg hover:bg-primary-50 dark:hover:bg-primary-900/20 transition-all"
                        >
                          {t('teamPermissions.teams.overview.view')}
                        </button>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </>
          )}
        </div>
      )}

      {activeTab === 'list' && (
        <div className="space-y-4">
          {/* 搜索栏 */}
          <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 p-4 shadow-sm">
            <div className="flex flex-col sm:flex-row sm:items-center gap-3">
              <div className="relative flex-1">
                <svg className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400 dark:text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                </svg>
                <input
                  type="text"
                  placeholder={t('teamPermissions.teams.search.placeholder')}
                  value={searchInput}
                  onChange={(e) => setSearchInput(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
                  className="w-full pl-9 pr-4 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 transition-all"
                />
              </div>
              <select
                value={mailboxFilter ?? ''}
                onChange={(e) => { setMailboxFilter(e.target.value === 'global' ? null : (e.target.value ? Number(e.target.value) : null)); setPage(1); }}
                className="px-3 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 transition-all"
              >
                <option value="">{t('teamPermissions.teams.filter.allMailboxes')}</option>
                <option value="global">{t('teamPermissions.teams.filter.globalTeams')}</option>
                {mailboxes.map(m => (
                  <option key={m.id} value={m.id}>{m.name}</option>
                ))}
              </select>
              <select
                value={activeFilter === null ? '' : String(activeFilter)}
                onChange={(e) => {
                  const val = e.target.value;
                  setActiveFilter(val === '' ? null : val === 'true');
                  setPage(1);
                }}
                className="px-3 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 transition-all"
              >
                <option value="">{t('teamPermissions.teams.filter.allStatus')}</option>
                <option value="true">{t('teamPermissions.teams.status.active')}</option>
                <option value="false">{t('teamPermissions.teams.status.inactive')}</option>
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
              <div className="flex-1" />
              <button
                onClick={openTrashModal}
                className="inline-flex items-center justify-center gap-1.5 px-4 py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-300 rounded-xl shadow-sm hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                </svg>
                {t('teamPermissions.teams.trash.button')}
                {trashCount > 0 && (
                  <span className="px-1.5 py-0.5 text-xs rounded-full bg-red-100 dark:bg-red-900/30 text-red-600 dark:text-red-400">
                    {trashCount}
                  </span>
                )}
              </button>
              <button
                onClick={openCreate}
                className="inline-flex items-center justify-center gap-1.5 px-4 py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 text-white rounded-xl shadow-sm shadow-primary-200/50 dark:shadow-primary-900/30 transition-all hover:-translate-y-0.5"
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m8-8H4" />
                </svg>
                {t('teamPermissions.teams.create')}
              </button>
            </div>
          </div>

          {/* 团队列表 */}
          <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 overflow-hidden shadow-sm">
            {loading ? (
              <div className="p-12 text-center">
                <svg className="animate-spin w-8 h-8 mx-auto mb-3 text-primary-500" fill="none" viewBox="0 0 24 24">
                  <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                  <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                </svg>
                <p className="text-sm text-gray-500 dark:text-slate-400">{t('common.shell.loading')}</p>
              </div>
            ) : teams.length === 0 ? (
              <div className="p-12 text-center">
                <svg className="w-12 h-12 mx-auto mb-3 text-gray-300 dark:text-slate-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M17 20h5v-2a4 4 0 00-3-3.87M9 20H4v-2a4 4 0 013-3.87m6-1.13a4 4 0 10-4-4 4 4 0 004 4z" />
                </svg>
                <p className="text-sm text-gray-500 dark:text-slate-400">
                  {keyword || mailboxFilter !== null || activeFilter !== null ? t('teamPermissions.teams.empty.noMatch') : t('teamPermissions.teams.empty.noTeams')}
                </p>
              </div>
            ) : (
              <>
                {/* PC 端表格视图 */}
                <div className="hidden md:block overflow-x-auto">
                  <table className="min-w-full divide-y divide-gray-100 dark:divide-slate-700/30">
                    <thead className="bg-gray-50/50 dark:bg-slate-900/50">
                      <tr>
                        <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.teams.field.name')}</th>
                        <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.teams.field.mailbox')}</th>
                        <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.teams.field.memberCount')}</th>
                        <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.teams.field.leaderCount')}</th>
                        <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.teams.field.status')}</th>
                        <th className="px-5 py-3.5 text-right text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.teams.field.actions')}</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100 dark:divide-slate-700/30 virtual-list">
                      {teams.map((team, index) => (
                        <tr key={team.id} className={`transition-all duration-150 ${index % 2 === 0 ? 'bg-white dark:bg-slate-800' : 'bg-gray-50/50 dark:bg-slate-800/50'} hover:bg-primary-50/50 dark:hover:bg-slate-700/50`}>
                          <td className="px-5 py-3.5">
                            <div className="flex items-center gap-3">
                              <div className="w-10 h-10 bg-gradient-to-br from-primary-500 to-indigo-500 rounded-xl flex items-center justify-center text-white font-bold text-sm shrink-0">
                                {team.name.charAt(0)}
                              </div>
                              <div className="min-w-0">
                                <div className="font-medium text-gray-900 dark:text-slate-100 truncate">{team.name}</div>
                                {team.description && <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 truncate">{team.description}</div>}
                              </div>
                            </div>
                          </td>
                          <td className="px-5 py-3.5 text-sm text-gray-700 dark:text-slate-300">
                            {team.mailboxName ? (
                              <span className="inline-flex items-center px-2.5 py-1 text-xs font-medium rounded-full ring-1 bg-purple-50 text-purple-700 ring-purple-100 dark:bg-purple-900/30 dark:text-purple-300 dark:ring-purple-800/50">
                                {team.mailboxName}
                              </span>
                            ) : (
                              <span className="inline-flex items-center px-2.5 py-1 text-xs font-medium rounded-full ring-1 bg-gray-50 text-gray-600 ring-gray-100 dark:bg-slate-700/40 dark:text-slate-400 dark:ring-slate-600/50">
                                {t('teamPermissions.teams.global')}
                              </span>
                            )}
                          </td>
                          <td className="px-5 py-3.5 text-sm text-gray-700 dark:text-slate-300">
                            <span className="inline-flex items-center gap-1">
                              <svg className="w-4 h-4 text-blue-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z" />
                              </svg>
                              {team.memberCount}
                            </span>
                          </td>
                          <td className="px-5 py-3.5 text-sm text-gray-700 dark:text-slate-300">
                            <span className="inline-flex items-center gap-1">
                              <svg className="w-4 h-4 text-amber-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
                              </svg>
                              {team.leaderCount}
                            </span>
                          </td>
                          <td className="px-5 py-3.5">
                            <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium rounded-full ring-1 ${
                              team.isActive
                                ? 'bg-emerald-50 text-emerald-700 ring-emerald-100 dark:bg-emerald-900/30 dark:text-emerald-300 dark:ring-emerald-800/50'
                                : 'bg-gray-50 text-gray-600 ring-gray-100 dark:bg-slate-700/40 dark:text-slate-400 dark:ring-slate-600/50'
                            }`}>
                              <span className={`w-1.5 h-1.5 rounded-full ${team.isActive ? 'bg-emerald-500' : 'bg-gray-400'}`} />
                              {team.isActive ? t('teamPermissions.teams.status.enabled') : t('teamPermissions.teams.status.disabled')}
                            </span>
                          </td>
                          <td className="px-5 py-3.5 text-right">
                            <div className="flex items-center justify-end gap-1">
                              <button
                                onClick={() => openMembers(team)}
                                className="p-2 text-gray-400 dark:text-slate-500 hover:text-emerald-600 hover:bg-emerald-50 dark:hover:bg-emerald-900/20 rounded-lg transition-all"
                                title={t('teamPermissions.teams.actions.manageMembers')}
                              >
                                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z" />
                                </svg>
                              </button>
                              <button
                                onClick={() => openEdit(team)}
                                className="p-2 text-gray-400 dark:text-slate-500 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/20 rounded-lg transition-all"
                                title={t('common.edit')}
                              >
                                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                                  <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                                </svg>
                              </button>
                              <button
                                onClick={() => setDeleteTarget(team)}
                                className="p-2 text-gray-400 dark:text-slate-500 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg transition-all"
                                title={t('common.delete')}
                              >
                                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
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

                {/* 移动端卡片视图 */}
                <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/30">
                  {teams.map((team, index) => (
                    <div key={team.id} className={`p-4 ${index % 2 === 0 ? 'bg-white dark:bg-slate-800' : 'bg-gray-50/50 dark:bg-slate-800/50'} hover:bg-primary-50/50 dark:hover:bg-slate-700/50 transition-all`}>
                      <div className="flex items-center justify-between gap-3 mb-3">
                        <div className="flex items-center gap-3 min-w-0">
                          <div className="w-10 h-10 bg-gradient-to-br from-primary-500 to-indigo-500 rounded-xl flex items-center justify-center text-white font-bold text-sm shrink-0">
                            {team.name.charAt(0)}
                          </div>
                          <div className="min-w-0">
                            <div className="font-medium text-gray-900 dark:text-slate-100 truncate">{team.name}</div>
                            {team.description && <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 truncate">{team.description}</div>}
                          </div>
                        </div>
                        <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium rounded-full ring-1 flex-shrink-0 ${
                          team.isActive
                            ? 'bg-emerald-50 text-emerald-700 ring-emerald-100 dark:bg-emerald-900/30 dark:text-emerald-300 dark:ring-emerald-800/50'
                            : 'bg-gray-50 text-gray-600 ring-gray-100 dark:bg-slate-700/40 dark:text-slate-400 dark:ring-slate-600/50'
                        }`}>
                          <span className={`w-1.5 h-1.5 rounded-full ${team.isActive ? 'bg-emerald-500' : 'bg-gray-400'}`} />
                          {team.isActive ? t('teamPermissions.teams.status.enabled') : t('teamPermissions.teams.status.disabled')}
                        </span>
                      </div>
                      <div className="grid grid-cols-3 gap-2 mb-3">
                        <div className="text-center bg-gray-50 dark:bg-slate-700/40 rounded-lg py-2">
                          <div className="text-xs text-gray-500 dark:text-slate-400 mb-0.5">{t('teamPermissions.teams.field.memberCount')}</div>
                          <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{team.memberCount}</div>
                        </div>
                        <div className="text-center bg-gray-50 dark:bg-slate-700/40 rounded-lg py-2">
                          <div className="text-xs text-gray-500 dark:text-slate-400 mb-0.5">{t('teamPermissions.teams.leader')}</div>
                          <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{team.leaderCount}</div>
                        </div>
                        <div className="text-center bg-gray-50 dark:bg-slate-700/40 rounded-lg py-2">
                          <div className="text-xs text-gray-500 dark:text-slate-400 mb-0.5">{t('teamPermissions.teams.field.mailboxShort')}</div>
                          <div className="text-xs font-medium text-gray-900 dark:text-slate-100 truncate">{team.mailboxName || t('teamPermissions.teams.global')}</div>
                        </div>
                      </div>
                      <div className="flex items-center gap-1.5 pt-3 border-t border-gray-100 dark:border-slate-700/50">
                        <button
                          onClick={() => openMembers(team)}
                          className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-medium text-emerald-600 bg-emerald-50 dark:bg-emerald-900/20 rounded-lg hover:bg-emerald-100 dark:hover:bg-emerald-900/30 transition-all"
                        >
                          {t('teamPermissions.teams.actions.manageMembers')}
                        </button>
                        <button
                          onClick={() => openEdit(team)}
                          className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-medium text-primary-600 bg-primary-50 dark:bg-primary-900/20 rounded-lg hover:bg-primary-100 dark:hover:bg-primary-900/30 transition-all"
                        >
                          {t('common.edit')}
                        </button>
                        <button
                          onClick={() => setDeleteTarget(team)}
                          className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-medium text-red-600 bg-red-50 dark:bg-red-900/20 rounded-lg hover:bg-red-100 dark:hover:bg-red-900/30 transition-all"
                        >
                          {t('common.delete')}
                        </button>
                      </div>
                    </div>
                  ))}
                </div>

                {totalPages > 1 && (
                  <div className="px-5 py-3.5 border-t border-gray-100 dark:border-slate-700/50 flex items-center justify-between bg-gray-50/30 dark:bg-slate-900/30">
                    <span className="text-sm text-gray-500 dark:text-slate-400">
                      {t('teamPermissions.teams.pagination.summary', { total: totalItems, page, totalPages })}
                    </span>
                    <div className="flex gap-2">
                      <button
                        onClick={() => setPage(p => Math.max(1, p - 1))}
                        disabled={page === 1}
                        className="px-3 py-1.5 text-sm border border-gray-200 dark:border-slate-600 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
                      >
                        {t('teamPermissions.teams.pagination.prev')}
                      </button>
                      <button
                        onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                        disabled={page === totalPages}
                        className="px-3 py-1.5 text-sm border border-gray-200 dark:border-slate-600 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
                      >
                        {t('teamPermissions.teams.pagination.next')}
                      </button>
                    </div>
                  </div>
                )}
              </>
            )}
          </div>
        </div>
      )}

      {/* 编辑团队弹窗 */}
      {showEditor && (
        <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4">
          <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-xl w-full max-w-lg max-h-[90vh] flex flex-col">
            <div className="px-6 py-4 border-b border-gray-100 dark:border-slate-700/50 flex items-center justify-between">
              <h2 className="text-lg font-semibold text-gray-900 dark:text-slate-100">
                {editingTeam ? t('teamPermissions.teams.editor.titleEdit') : t('teamPermissions.teams.create')}
              </h2>
              <button onClick={() => setShowEditor(false)} className="p-1.5 text-gray-400 dark:text-slate-500 hover:text-gray-600 dark:hover:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700 rounded-lg transition-all">
                <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            </div>

            <div className="p-6 space-y-4 overflow-y-auto">
              {formError && (
                <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 px-4 py-3 rounded-xl text-sm flex items-center gap-2">
                  <svg className="w-4 h-4 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                  </svg>
                  {formError}
                </div>
              )}

              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
                  {t('teamPermissions.teams.field.name')} <span className="text-red-500">*</span>
                </label>
                <input
                  type="text"
                  value={formData.name}
                  onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                  className={inputClass}
                  placeholder={t('teamPermissions.teams.form.namePlaceholder')}
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('teamPermissions.teams.form.description')}</label>
                <textarea
                  value={formData.description || ''}
                  onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                  rows={3}
                  className={inputClass}
                  placeholder={t('teamPermissions.teams.form.descriptionPlaceholder')}
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('teamPermissions.teams.field.mailbox')}</label>
                <select
                  value={formData.mailboxId ?? ''}
                  onChange={(e) => setFormData({ ...formData, mailboxId: e.target.value ? Number(e.target.value) : null })}
                  className={inputClass}
                >
                  <option value="">{t('teamPermissions.teams.form.mailboxGlobal')}</option>
                  {mailboxes.map(m => (
                    <option key={m.id} value={m.id}>{m.name}</option>
                  ))}
                </select>
                <p className="mt-2 text-xs text-gray-400 dark:text-slate-500 flex items-center gap-1">
                  <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                  </svg>
                  {t('teamPermissions.teams.form.mailboxHint')}
                </p>
              </div>

              <div className="flex items-center gap-6 pt-2">
                <label className="flex items-center gap-2 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={formData.isActive}
                    onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                    className="w-4 h-4 text-primary-600 rounded border-gray-300 dark:border-slate-600 focus:ring-primary-500"
                  />
                  <span className="text-sm text-gray-700 dark:text-slate-300">{t('teamPermissions.teams.form.isActive')}</span>
                </label>
                <div className="flex items-center gap-2">
                  <label className="text-sm text-gray-700 dark:text-slate-300">{t('teamPermissions.teams.form.sortOrder')}</label>
                  <input
                    type="number"
                    value={formData.sortOrder}
                    onChange={(e) => setFormData({ ...formData, sortOrder: Number(e.target.value) })}
                    className="w-20 px-2.5 py-1.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500"
                  />
                </div>
              </div>
            </div>

            <div className="px-6 py-4 border-t border-gray-100 dark:border-slate-700/50 flex justify-end gap-3">
              <button
                onClick={() => setShowEditor(false)}
                className="px-4 py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
              >
                {t('common.cancel')}
              </button>
              <button
                onClick={handleSave}
                className="px-4 py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 text-white rounded-xl shadow-sm shadow-primary-200/50 dark:shadow-primary-900/30 transition-all"
              >
                {t('common.save')}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 成员管理弹窗 */}
      {showMembers && currentTeam && (
        <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4">
          <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-xl w-full max-w-2xl max-h-[85vh] flex flex-col">
            <div className="px-6 py-4 border-b border-gray-100 dark:border-slate-700/50 flex items-center justify-between">
              <div>
                <h2 className="text-lg font-semibold text-gray-900 dark:text-slate-100">{t('teamPermissions.teams.members.title', { name: currentTeam.name })}</h2>
                <p className="text-sm text-gray-500 dark:text-slate-400 mt-0.5">{t('teamPermissions.teams.members.count', { count: members.length })}</p>
              </div>
              <button onClick={() => setShowMembers(false)} className="p-1.5 text-gray-400 dark:text-slate-500 hover:text-gray-600 dark:hover:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700 rounded-lg transition-all">
                <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            </div>

            <div className="px-6 py-3 border-b border-gray-100 dark:border-slate-700/50 flex items-center justify-between bg-gray-50/50 dark:bg-slate-900/30">
              <button
                onClick={() => setShowAddMember(!showAddMember)}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-primary-600 text-white text-sm font-medium rounded-lg hover:bg-primary-700 transition-all"
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m8-8H4" />
                </svg>
                {t('teamPermissions.teams.members.add')}
              </button>
            </div>

            {showAddMember && (
              <div className="px-6 py-4 border-b border-gray-100 dark:border-slate-700/50 bg-primary-50/50 dark:bg-primary-900/10">
                <div className="flex flex-col sm:flex-row gap-3 sm:items-end">
                  <div className="flex-1">
                    <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('teamPermissions.teams.members.selectUser')}</label>
                    <select
                      value={selectedUserId}
                      onChange={(e) => setSelectedUserId(Number(e.target.value))}
                      className={inputClass}
                    >
                      <option value={0}>{t('teamPermissions.teams.members.selectUserRequired')}</option>
                      {availableUsers.map(u => (
                        <option key={u.id} value={u.id}>
                          {u.firstName} {u.lastName} ({u.email})
                        </option>
                      ))}
                    </select>
                  </div>
                  <div className="w-full sm:w-36">
                    <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('teamPermissions.teams.members.role')}</label>
                    <select
                      value={selectedRole}
                      onChange={(e) => setSelectedRole(Number(e.target.value))}
                      className={inputClass}
                    >
                      {ROLE_OPTIONS.map(opt => (
                        <option key={opt.value} value={opt.value}>{t(opt.labelKey ?? 'format.unknown')}</option>
                      ))}
                    </select>
                  </div>
                  <div className="flex gap-2">
                    <button
                      onClick={handleAddMember}
                      className="px-4 py-2.5 bg-primary-600 text-white text-sm font-medium rounded-xl hover:bg-primary-700 transition-all"
                    >
                      {t('teamPermissions.teams.members.addSubmit')}
                    </button>
                    <button
                      onClick={() => setShowAddMember(false)}
                      className="px-4 py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
                    >
                      {t('common.cancel')}
                    </button>
                  </div>
                </div>
              </div>
            )}

            <div className="flex-1 overflow-y-auto p-6">
              {members.length === 0 ? (
                <div className="text-center py-12">
                  <svg className="w-12 h-12 mx-auto mb-3 text-gray-300 dark:text-slate-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z" />
                  </svg>
                  <p className="text-sm text-gray-500 dark:text-slate-400">{t('teamPermissions.teams.members.empty')}</p>
                  <p className="text-xs text-gray-400 dark:text-slate-500 mt-1">{t('teamPermissions.teams.members.emptyHint')}</p>
                </div>
              ) : (
                <div className="space-y-2">
                  {members.map(member => (
                    <div
                      key={member.id}
                      className="flex items-center justify-between p-3.5 bg-gray-50/70 dark:bg-slate-900/40 rounded-xl hover:bg-primary-50/50 dark:hover:bg-slate-700/40 transition-all"
                    >
                      <div className="flex items-center gap-3 min-w-0">
                        <UserAvatar userId={member.userId} name={member.userName} size="md" />
                        <div className="min-w-0">
                          <p className="font-medium text-gray-900 dark:text-slate-100 flex items-center gap-2">
                            {member.userName}
                            {isAdmin(member.role) && (
                              <span className="inline-flex items-center px-2 py-0.5 text-xs font-medium rounded-full ring-1 bg-orange-50 text-orange-700 ring-orange-100 dark:bg-orange-900/30 dark:text-orange-300 dark:ring-orange-800/50">
                                {t('teamPermissions.teams.leader')}
                              </span>
                            )}
                          </p>
                          <p className="text-xs text-gray-500 dark:text-slate-400 truncate">{member.userEmail}</p>
                        </div>
                      </div>
                      <div className="flex items-center gap-2 shrink-0">
                        <select
                          value={member.role}
                          onChange={(e) => handleChangeRole(member, Number(e.target.value))}
                          className="bg-white dark:bg-slate-900 text-sm border border-gray-200 dark:border-slate-600 rounded-lg px-2.5 py-1.5 focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500"
                        >
                          {ROLE_OPTIONS.map(opt => (
                            <option key={opt.value} value={opt.value}>{t(opt.labelKey ?? 'format.unknown')}</option>
                          ))}
                        </select>
                        <button
                          onClick={() => handleRemoveMember(member)}
                          className="p-2 text-gray-400 dark:text-slate-500 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg transition-all"
                          title={t('teamPermissions.teams.members.remove')}
                        >
                          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                            <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                          </svg>
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div className="px-6 py-4 border-t border-gray-100 dark:border-slate-700/50 flex justify-end">
              <button
                onClick={() => setShowMembers(false)}
                className="px-4 py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
              >
                {t('common.close')}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 删除确认 */}
      <ConfirmDialog
        open={!!deleteTarget}
        title={t('teamPermissions.teams.delete.title')}
        message={t('teamPermissions.teams.delete.message', { name: deleteTarget?.name ?? '' })}
        confirmText={t('common.delete')}
        variant="danger"
        loading={deleting}
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
      />

      {/* 垃圾箱弹窗 */}
      <Modal
        open={trashOpen}
        title={t('teamPermissions.teams.trash.title')}
        onClose={() => setTrashOpen(false)}
        size="xl"
        footer={
          <>
            <button
              onClick={() => setClearTrashOpen(true)}
              disabled={trashList.length === 0 || clearingTrash}
              className="px-4 py-2 text-sm bg-red-600 hover:bg-red-700 disabled:bg-red-300 text-white rounded-lg transition"
            >
              {clearingTrash ? t('teamPermissions.teams.trash.clearing') : t('teamPermissions.teams.trash.clear')}
            </button>
            <button
              onClick={() => setTrashOpen(false)}
              className="px-4 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 dark:text-slate-200"
            >
              {t('common.close')}
            </button>
          </>
        }
      >
        <div className="space-y-4">
          {trashError && (
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 px-4 py-3 rounded-xl text-sm">
              {trashError}
            </div>
          )}

          <div className="flex items-center gap-2">
            <div className="relative flex-1">
              <svg className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
              </svg>
              <input
                type="text"
                value={trashSearch}
                onChange={(e) => setTrashSearch(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    setTrashPage(1);
                    loadTrash();
                  }
                }}
                placeholder={t('teamPermissions.teams.trash.searchPlaceholder')}
                className="w-full pl-9 pr-4 py-2 text-sm bg-white dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 transition dark:text-slate-200"
              />
            </div>
            <button
              onClick={() => {
                setTrashPage(1);
                loadTrash();
              }}
              className="px-4 py-2 text-sm bg-primary-600 hover:bg-primary-700 text-white rounded-xl transition"
            >
              {t('common.search')}
            </button>
          </div>

          <div className="text-sm text-gray-500 dark:text-slate-400">
            {t('teamPermissions.teams.trash.countBefore')}<span className="font-semibold text-gray-900 dark:text-slate-100">{trashTotalItems}</span>{t('teamPermissions.teams.trash.countAfter', { count: trashTotalItems })}
          </div>

          <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-100 dark:border-slate-700/50 overflow-hidden">
            {trashLoading ? (
              <div className="p-8 text-center">
                <div className="w-8 h-8 border-4 border-primary-200 dark:border-primary-700 border-t-primary-600 rounded-full animate-spin mx-auto mb-2" />
                <p className="text-sm text-gray-500 dark:text-slate-400">{t('common.shell.loading')}</p>
              </div>
            ) : trashList.length === 0 ? (
              <div className="p-8 text-center">
                <div className="w-12 h-12 bg-gray-100 dark:bg-slate-700/50 rounded-full flex items-center justify-center mx-auto mb-3">
                  <svg className="w-6 h-6 text-gray-400 dark:text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                  </svg>
                </div>
                <p className="text-sm font-medium text-gray-900 dark:text-slate-100 mb-1">{t('teamPermissions.teams.trash.empty')}</p>
                <p className="text-sm text-gray-500 dark:text-slate-400">
                  {trashSearch ? t('teamPermissions.teams.trash.emptyNoMatch') : t('teamPermissions.teams.trash.emptyNone')}
                </p>
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-gray-100 dark:divide-slate-700/50">
                  <thead className="bg-gray-50 dark:bg-slate-900/50">
                    <tr>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">ID</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.teams.field.name')}</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.teams.field.mailbox')}</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.teams.field.memberCount')}</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.teams.field.leaderCount')}</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.teams.field.deletedAt')}</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.teams.field.actions')}</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100 dark:divide-slate-700/30 virtual-list">
                    {trashList.map((team) => (
                      <tr key={team.id} className="hover:bg-gray-50 dark:hover:bg-slate-700/30 transition-colors">
                        <td className="px-4 py-3 text-sm text-gray-500 dark:text-slate-400 font-mono">#{team.id}</td>
                        <td className="px-4 py-3">
                          <div className="font-medium text-gray-800 dark:text-slate-100">{team.name}</div>
                          {team.description && <div className="text-xs text-gray-500 dark:text-slate-400 mt-1">{team.description}</div>}
                        </td>
                        <td className="px-4 py-3 text-sm text-gray-600 dark:text-slate-300">
                          {team.mailboxName ?? t('teamPermissions.teams.global')}
                        </td>
                        <td className="px-4 py-3 text-sm text-gray-600 dark:text-slate-300">{team.memberCount}</td>
                        <td className="px-4 py-3 text-sm text-gray-600 dark:text-slate-300">{team.leaderCount}</td>
                        <td className="px-4 py-3 text-sm text-gray-500 dark:text-slate-400 whitespace-nowrap">
                          {team.deletedAt ? formatDateTime(team.deletedAt, undefined, language) : '-'}
                        </td>
                        <td className="px-4 py-3 text-sm">
                          <div className="flex items-center gap-1">
                            <button
                              onClick={() => setRestoreTarget(team)}
                              className="p-1.5 text-gray-400 hover:text-green-600 hover:bg-green-50 dark:hover:bg-green-900/20 rounded-md transition"
                              title={t('teamPermissions.teams.trash.restore')}
                            >
                              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                <path strokeLinecap="round" strokeLinejoin="round" d="M3 10h10a8 8 0 018 8v2M3 10l6 6m-6-6l6-6" />
                              </svg>
                            </button>
                            <button
                              onClick={() => setHardDeleteTarget(team)}
                              className="p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-md transition"
                              title={t('teamPermissions.teams.trash.hardDelete')}
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
                onClick={() => {
                  setTrashPage((p) => Math.max(1, p - 1));
                  loadTrash();
                }}
                disabled={trashPage === 1}
                className="px-3 py-1.5 text-sm border rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition dark:text-slate-200"
              >
                {t('teamPermissions.teams.pagination.prev')}
              </button>
              <span className="text-sm text-gray-500 dark:text-slate-400">
                {t('teamPermissions.teams.trash.pageIndicator', { page: trashPage, totalPages: trashTotalPages })}
              </span>
              <button
                onClick={() => {
                  setTrashPage((p) => Math.min(trashTotalPages, p + 1));
                  loadTrash();
                }}
                disabled={trashPage >= trashTotalPages}
                className="px-3 py-1.5 text-sm border rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition dark:text-slate-200"
              >
                {t('teamPermissions.teams.pagination.next')}
              </button>
            </div>
          )}
        </div>
      </Modal>

      {/* 恢复确认 */}
      <ConfirmDialog
        open={!!restoreTarget}
        title={t('teamPermissions.teams.restore.title')}
        message={t('teamPermissions.teams.restore.message', { name: restoreTarget?.name ?? '' })}
        confirmText={t('teamPermissions.teams.restore.confirm')}
        variant="primary"
        loading={restoring}
        onConfirm={handleRestore}
        onCancel={() => setRestoreTarget(null)}
      />

      {/* 彻底删除确认 */}
      <ConfirmDialog
        open={!!hardDeleteTarget}
        title={t('teamPermissions.teams.hardDelete.title')}
        message={t('teamPermissions.teams.hardDelete.message', { name: hardDeleteTarget?.name ?? '' })}
        confirmText={t('teamPermissions.teams.hardDelete.confirm')}
        variant="danger"
        loading={hardDeleting}
        onConfirm={handleHardDelete}
        onCancel={() => setHardDeleteTarget(null)}
      />

      {/* 清空垃圾箱确认 */}
      <ConfirmDialog
        open={clearTrashOpen}
        title={t('teamPermissions.teams.trash.clear')}
        message={t('teamPermissions.teams.trash.clearMessage')}
        confirmText={t('teamPermissions.teams.trash.clearConfirm')}
        variant="danger"
        loading={clearingTrash}
        onConfirm={handleClearTrash}
        onCancel={() => setClearTrashOpen(false)}
      />
    </div>
  );
}
