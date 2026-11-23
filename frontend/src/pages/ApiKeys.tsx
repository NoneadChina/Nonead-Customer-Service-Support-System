import { useState, useEffect, useCallback, useRef } from 'react';
import { apiKeyApi } from '../api';
import { useTimedToast } from '../hooks/useTimedToast';
import type {
  ApiKeyDto,
  ApiKeyOverviewResponse,
  CreateApiKeyRequest,
  UpdateApiKeyRequest,
} from '../api/types';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import TrashDialogs from '../components/TrashDialogs';
import { formatDateTime } from '../utils/format';
import { getApiErrorMessage } from '../utils/error';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import {
  Key,
  List,
  Activity,
  Plus,
  Search,
  Edit2,
  Trash2,
  RefreshCw,
  CheckCircle2,
  XCircle,
  Shield,
  Users,
  Copy,
  AlertCircle,
  ChevronLeft,
  ChevronRight,
  BarChart3,
} from 'lucide-react';

/**
 * 权限范围目录（分组 → 选项）。
 *
 * 产出方：**后端无枚举、无白名单**——`ApiKeyService` 只把 `Scopes` 逗号拼接后存取，
 * 全仓唯一的取值引用是种子数据 `DatabaseSeeder.cs:2335` 的 `conversations:read,conversations:write`。
 * 因此本表就是前端 UI 目录，12 码即全集。
 *
 * ⚠ 不复用 `teamPermissions.*`：细粒度权限用点号（`webhooks.manage`，见 `utils/permissions.ts`
 * 的 `PermissionKey`），API 作用域用冒号（`webhooks:manage`）——同一概念两种字符串，不是同源目录。
 */
const SCOPE_GROUPS = [
  {
    categoryKey: 'integration.apiKeys.scopeCategory.conversations',
    scopes: [
      { value: 'conversations:read', labelKey: 'integration.apiKeys.scope.conversationsRead' },
      { value: 'conversations:write', labelKey: 'integration.apiKeys.scope.conversationsWrite' },
    ],
  },
  {
    categoryKey: 'integration.apiKeys.scopeCategory.customers',
    scopes: [
      { value: 'customers:read', labelKey: 'integration.apiKeys.scope.customersRead' },
      { value: 'customers:write', labelKey: 'integration.apiKeys.scope.customersWrite' },
    ],
  },
  {
    categoryKey: 'integration.apiKeys.scopeCategory.users',
    scopes: [
      { value: 'users:read', labelKey: 'integration.apiKeys.scope.usersRead' },
      { value: 'users:write', labelKey: 'integration.apiKeys.scope.usersWrite' },
    ],
  },
  {
    categoryKey: 'integration.apiKeys.scopeCategory.mailboxes',
    scopes: [
      { value: 'mailboxes:read', labelKey: 'integration.apiKeys.scope.mailboxesRead' },
    ],
  },
  {
    categoryKey: 'integration.apiKeys.scopeCategory.tags',
    scopes: [
      { value: 'tags:read', labelKey: 'integration.apiKeys.scope.tagsRead' },
      { value: 'tags:write', labelKey: 'integration.apiKeys.scope.tagsWrite' },
    ],
  },
  {
    categoryKey: 'integration.apiKeys.scopeCategory.reports',
    scopes: [
      { value: 'reports:read', labelKey: 'integration.apiKeys.scope.reportsRead' },
    ],
  },
  {
    categoryKey: 'integration.apiKeys.scopeCategory.webhooks',
    scopes: [
      { value: 'webhooks:manage', labelKey: 'integration.apiKeys.scope.webhooksManage' },
    ],
  },
  {
    categoryKey: 'integration.apiKeys.scopeCategory.admin',
    scopes: [
      { value: 'admin:full', labelKey: 'integration.apiKeys.scope.adminFull' },
    ],
  },
] satisfies { categoryKey: TranslationKey; scopes: { value: string; labelKey: TranslationKey }[] }[];

const tabs = [
  { key: 'overview', labelKey: 'integration.common.tabOverview', icon: <Activity className="w-4 h-4" /> },
  { key: 'list', labelKey: 'integration.apiKeys.tabList', icon: <List className="w-4 h-4" /> },
] as const;

export default function ApiKeys() {
  const { t } = useI18n();
  const [activeTab, setActiveTab] = useState<'overview' | 'list'>('overview');
  const [overview, setOverview] = useState<ApiKeyOverviewResponse | null>(null);
  const [apiKeys, setApiKeys] = useState<ApiKeyDto[]>([]);
  const [trashApiKeys, setTrashApiKeys] = useState<ApiKeyDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [totalItems, setTotalItems] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [searchInput, setSearchInput] = useState('');
  const [activeFilter, setActiveFilter] = useState<boolean | null>(null);
  const [listSubTab, setListSubTab] = useState<'active' | 'trash'>('active');

  const [showEditor, setShowEditor] = useState(false);
  const [editingKey, setEditingKey] = useState<ApiKeyDto | null>(null);

  const [formData, setFormData] = useState<CreateApiKeyRequest>({
    name: '',
    description: '',
    scopes: [],
    isActive: true,
  });
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);

  const [newKey, setNewKey] = useState<ApiKeyDto | null>(null);
  const [deleteId, setDeleteId] = useState<number | null>(null);
  const [restoreId, setRestoreId] = useState<number | null>(null);
  const [hardDeleteId, setHardDeleteId] = useState<number | null>(null);
  const [clearTrashOpen, setClearTrashOpen] = useState(false);
  const [regenId, setRegenId] = useState<number | null>(null);
  const { message: toast, show: showToast } = useTimedToast();

  const pageSize = 10;

  useEffect(() => {
    if (activeTab === 'overview') loadOverview();
    if (activeTab === 'list') loadList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab]);

  // T000039：搜索触发策略——650ms 输入防抖自动搜索 + 回车/按钮即时触发，
  // 不再在每次 onChange 立即请求（避免用户尚未输入完成就触发搜索），也不在失焦时触发（防止误搜）。
  const searchTimerRef = useRef<number | null>(null);
  const applySearch = useCallback((value: string) => {
    setSearch(value);
    setPage(1);
  }, []);

  const handleSearchInput = useCallback((value: string) => {
    setSearchInput(value);
    // 输入停顿 650ms 后自动搜索（期间继续输入会重置计时器）
    if (searchTimerRef.current !== null) window.clearTimeout(searchTimerRef.current);
    searchTimerRef.current = window.setTimeout(() => applySearch(value), 650);
  }, [applySearch]);

  // 回车 / 点击搜索按钮：立即按当前输入触发，并取消挂起的防抖
  const triggerSearchNow = useCallback(() => {
    if (searchTimerRef.current !== null) {
      window.clearTimeout(searchTimerRef.current);
      searchTimerRef.current = null;
    }
    applySearch(searchInput);
  }, [applySearch, searchInput]);

  useEffect(() => {
    return () => {
      if (searchTimerRef.current !== null) window.clearTimeout(searchTimerRef.current);
    };
  }, []);

  useEffect(() => {
    if (activeTab === 'list') loadList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, search, activeFilter, listSubTab, activeTab]);

  async function loadOverview() {
    setLoading(true);
    setError('');
    try {
      const data = await apiKeyApi.overview();
      setOverview(data);
    } catch (err) {
      setError(getApiErrorMessage(err, t('integration.apiKeys.error.overviewLoadFailed')));
    } finally {
      setLoading(false);
    }
  }

  async function loadList() {
    setLoading(true);
    setError('');
    try {
      if (listSubTab === 'active') {
        const data = await apiKeyApi.list({
          page,
          pageSize,
          keyword: search || null,
          isActive: activeFilter,
        });
        setApiKeys(data.data ?? []);
        setTotalItems(data.totalItems ?? 0);
      } else {
        const data = await apiKeyApi.getTrash(page, pageSize, search || '');
        setTrashApiKeys(data.data ?? []);
        setTotalItems(data.totalItems ?? 0);
      }
    } catch (err) {
      setError(getApiErrorMessage(err, t('integration.apiKeys.error.listLoadFailed')));
    } finally {
      setLoading(false);
    }
  }

  async function loadTrash() {
    setLoading(true);
    setError('');
    try {
      const data = await apiKeyApi.getTrash(page, pageSize, search || '');
      setTrashApiKeys(data.data ?? []);
      setTotalItems(data.totalItems ?? 0);
    } catch (err) {
      setError(getApiErrorMessage(err, t('integration.apiKeys.error.trashLoadFailed')));
    } finally {
      setLoading(false);
    }
  }

  function openCreate() {
    setEditingKey(null);
    setFormData({
      name: '',
      description: '',
      scopes: [],
      isActive: true,
    });
    setFormError('');
    setShowEditor(true);
  }

  function openEdit(key: ApiKeyDto) {
    setEditingKey(key);
    setFormData({
      name: key.name,
      description: key.description ?? '',
      scopes: [...key.scopes],
      isActive: key.isActive,
    });
    setFormError('');
    setShowEditor(true);
  }

  async function handleSave() {
    if (!formData.name.trim()) {
      setFormError(t('integration.apiKeys.editor.errNameRequired'));
      return;
    }
    if (formData.scopes.length === 0) {
      setFormError(t('integration.apiKeys.editor.errScopesRequired'));
      return;
    }

    setSaving(true);
    setFormError('');
    try {
      if (editingKey) {
        await apiKeyApi.update(editingKey.id, formData as UpdateApiKeyRequest);
        showToast({ type: 'success', text: t('integration.apiKeys.toast.updated') });
      } else {
        const created = await apiKeyApi.create(formData);
        setNewKey(created);
        showToast({ type: 'success', text: t('integration.apiKeys.toast.created') });
      }
      setShowEditor(false);
      if (activeTab === 'list') loadList();
      if (activeTab === 'overview') loadOverview();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : t('integration.common.saveFailed'));
    } finally {
      setSaving(false);
    }
  }

  async function handleToggle(id: number) {
    try {
      await apiKeyApi.toggleActive(id);
      showToast({ type: 'success', text: t('integration.common.toggleSucceeded') });
      if (activeTab === 'list') loadList();
      if (activeTab === 'overview') loadOverview();
    } catch {
      showToast({ type: 'error', text: t('integration.common.operationFailed') });
    }
  }

  async function handleRegenerate() {
    if (!regenId) return;
    try {
      const result = await apiKeyApi.regenerate(regenId);
      setNewKey(result);
      showToast({ type: 'success', text: t('integration.apiKeys.toast.regenerated') });
      setRegenId(null);
      if (activeTab === 'list') loadList();
      if (activeTab === 'overview') loadOverview();
    } catch {
      showToast({ type: 'error', text: t('integration.apiKeys.toast.regenerateFailed') });
    }
  }

  async function handleDelete() {
    if (!deleteId) return;
    try {
      await apiKeyApi.remove(deleteId);
      showToast({ type: 'success', text: t('integration.apiKeys.toast.movedToTrash') });
      setDeleteId(null);
      if (activeTab === 'list') loadList();
      if (activeTab === 'overview') loadOverview();
    } catch {
      showToast({ type: 'error', text: t('integration.common.deleteFailed') });
    }
  }

  async function handleRestore() {
    if (!restoreId) return;
    try {
      await apiKeyApi.restore(restoreId);
      showToast({ type: 'success', text: t('integration.apiKeys.toast.restored') });
      setRestoreId(null);
      if (activeTab === 'list') loadTrash();
    } catch {
      showToast({ type: 'error', text: t('integration.common.restoreFailed') });
    }
  }

  async function handleHardDelete() {
    if (!hardDeleteId) return;
    try {
      await apiKeyApi.hardDelete(hardDeleteId);
      showToast({ type: 'success', text: t('integration.apiKeys.toast.hardDeleted') });
      setHardDeleteId(null);
      if (activeTab === 'list') loadTrash();
    } catch {
      showToast({ type: 'error', text: t('integration.common.hardDeleteFailed') });
    }
  }

  async function handleClearTrash() {
    try {
      await apiKeyApi.clearTrash();
      showToast({ type: 'success', text: t('integration.common.trashCleared') });
      setClearTrashOpen(false);
      if (activeTab === 'list') loadTrash();
    } catch {
      showToast({ type: 'error', text: t('integration.common.clearFailed') });
    }
  }

  async function copyToClipboard(text: string) {
    try {
      // 优先使用现代 Clipboard API（仅 HTTPS / localhost 可用）
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
      } else {
        // 降级方案：兼容非安全上下文（如 HTTP 局域网 IP）
        const textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.style.position = 'fixed';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand('copy');
        document.body.removeChild(textarea);
      }
      showToast({ type: 'success', text: t('integration.apiKeys.toast.copied') });
    } catch {
      showToast({ type: 'error', text: t('integration.apiKeys.toast.copyFailed') });
    }
  }

  const totalPages = Math.ceil(totalItems / pageSize);

  const retry = () => {
    setError('');
    if (activeTab === 'overview') loadOverview();
    else loadList();
  };

  return (
    <div className="space-y-5">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-semibold text-gray-900 dark:text-slate-100">{t('integration.apiKeys.pageTitle')}</h1>
          <p className="text-sm text-gray-500 dark:text-slate-400 mt-1">
            {t('integration.apiKeys.subtitle')}
          </p>
        </div>
        {activeTab === 'list' && listSubTab === 'active' && (
          <button
            onClick={openCreate}
            className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-lg hover:bg-primary-700 transition shadow-sm"
          >
            <Plus className="w-4 h-4" />
            <span className="hidden sm:inline">{t('integration.apiKeys.newKey')}</span>
            <span className="sm:hidden">{t('integration.common.newShort')}</span>
          </button>
        )}
      </div>

      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 shadow-sm">
        <div className="flex gap-1 p-1.5 border-b border-gray-100 dark:border-slate-700/50 overflow-x-auto">
          {tabs.map((tab) => (
            <button
              key={tab.key}
              onClick={() => { setActiveTab(tab.key); setPage(1); }}
              className={`inline-flex items-center gap-2 px-3 sm:px-4 py-2 text-sm font-medium rounded-lg transition-colors whitespace-nowrap ${
                activeTab === tab.key
                  ? 'bg-primary-50 dark:bg-primary-900/20 text-primary-600 dark:text-primary-400'
                  : 'text-gray-600 dark:text-slate-400 hover:bg-gray-50 dark:hover:bg-slate-700/50 hover:text-gray-900 dark:hover:text-slate-200'
              }`}
            >
              {tab.icon}
              <span className="hidden sm:inline">{t(tab.labelKey)}</span>
            </button>
          ))}
        </div>
      </div>

      {loading && (
        <div className="flex justify-center py-12">
          <div className="animate-spin h-8 w-8 border-4 border-primary-500 border-t-transparent rounded-full" />
        </div>
      )}

      {error && !loading && (
        <div className="bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/20 text-rose-700 dark:text-rose-400 px-4 py-3 rounded-xl text-sm flex items-start gap-2">
          <AlertCircle className="w-4 h-4 mt-0.5 shrink-0" />
          <div className="flex-1">
            {t('integration.apiKeys.loadFailedPrefix')}{error}
            <button
              onClick={retry}
              className="ml-3 inline-flex items-center gap-1 text-primary-600 dark:text-primary-400 hover:underline text-xs"
            >
              <RefreshCw className="w-3 h-3" />
              {t('common.retry')}
            </button>
          </div>
        </div>
      )}

      {activeTab === 'overview' && !loading && overview && (
        <div className="space-y-5">
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            {[
              { label: t('integration.apiKeys.statTotalKeys'), value: overview.totalKeys, color: 'text-blue-600', bg: 'bg-blue-50 dark:bg-blue-900/20', icon: Key },
              { label: t('integration.common.statsActive'), value: overview.activeKeys, color: 'text-green-600', bg: 'bg-green-50 dark:bg-green-900/20', icon: CheckCircle2 },
              { label: t('integration.common.statsInactive'), value: overview.inactiveKeys, color: 'text-gray-500 dark:text-slate-400', bg: 'bg-gray-100 dark:bg-slate-700', icon: XCircle },
              { label: t('integration.apiKeys.statTotalUsage'), value: overview.totalUsage, color: 'text-purple-600', bg: 'bg-purple-50 dark:bg-purple-900/20', icon: BarChart3 },
            ].map((s) => (
              <div key={s.label} className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 p-5 shadow-sm hover:shadow-md transition-shadow">
                <div className="flex items-center justify-between">
                  <div className="text-sm text-gray-500 dark:text-slate-400">{s.label}</div>
                  <div className={`p-2 rounded-lg ${s.bg}`}>
                    <s.icon className={`w-5 h-5 ${s.color}`} />
                  </div>
                </div>
                <div className={`text-3xl font-bold mt-3 ${s.color}`}>{s.value}</div>
              </div>
            ))}
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
            <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 shadow-sm overflow-hidden">
              <div className="px-5 py-4 border-b border-gray-100 dark:border-slate-700/50">
                <h3 className="text-sm font-semibold text-gray-700 dark:text-slate-200">{t('integration.apiKeys.recentKeys')}</h3>
              </div>
              <div className="divide-y divide-gray-100 dark:divide-slate-700/50">
                {overview.recentKeys.length === 0 ? (
                  <div className="px-5 py-8 text-center text-gray-400 dark:text-slate-500">
                    <Key className="w-10 h-10 mx-auto mb-2 opacity-30" />
                    {t('integration.apiKeys.noKeys')}
                  </div>
                ) : (
                  overview.recentKeys.slice(0, 5).map((key) => (
                    <div key={key.id} className="px-5 py-4 flex items-center justify-between hover:bg-gray-50 dark:hover:bg-slate-700/30">
                      <div className="flex items-center gap-3">
                        <div className="p-2 bg-blue-50 dark:bg-blue-900/20 rounded-lg">
                          <Key className="w-4 h-4 text-blue-600" />
                        </div>
                        <div>
                          <p className="font-medium text-gray-900 dark:text-slate-100 text-sm">{key.name}</p>
                          <p className="text-xs text-gray-500 dark:text-slate-400 font-mono">{key.keyPrefix}</p>
                        </div>
                      </div>
                      <span
                        className={`inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-full ${
                          key.isActive
                            ? 'bg-green-50 text-green-700 dark:bg-green-900/30 dark:text-green-400'
                            : 'bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-slate-400'
                        }`}
                      >
                        {key.isActive ? <CheckCircle2 className="w-3 h-3" /> : <XCircle className="w-3 h-3" />}
                        {key.isActive ? t('integration.common.enabled') : t('integration.common.disabled')}
                      </span>
                    </div>
                  ))
                )}
              </div>
            </div>

            <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 shadow-sm overflow-hidden">
              <div className="px-5 py-4 border-b border-gray-100 dark:border-slate-700/50">
                <h3 className="text-sm font-semibold text-gray-700 dark:text-slate-200">{t('integration.apiKeys.scopesDistribution')}</h3>
              </div>
              <div className="p-5">
                {Object.keys(overview.scopesCount).length === 0 ? (
                  <div className="text-center py-8 text-gray-400 dark:text-slate-500">
                    <Shield className="w-10 h-10 mx-auto mb-2 opacity-30" />
                    {t('common.empty')}
                  </div>
                ) : (
                  <div className="space-y-3">
                    {Object.entries(overview.scopesCount)
                      .sort(([, a], [, b]) => b - a)
                      .slice(0, 8)
                      .map(([scope, count]) => (
                        <div key={scope} className="flex items-center">
                          <span className="text-sm text-gray-600 dark:text-slate-300 w-48 truncate">{scope}</span>
                          <div className="flex-1 mx-4 bg-gray-100 dark:bg-slate-700 rounded-full h-2">
                            <div
                              className="bg-primary-500 h-2 rounded-full"
                              style={{
                                width: `${(count / Math.max(...Object.values(overview.scopesCount))) * 100}%`,
                              }}
                            />
                          </div>
                          <span className="text-sm font-medium text-gray-900 dark:text-slate-100 w-8 text-right">{count}</span>
                        </div>
                      ))}
                  </div>
                )}
              </div>
            </div>
          </div>
        </div>
      )}

      {activeTab === 'list' && !loading && (
        <div className="space-y-4">
          <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 p-3 sm:p-4 shadow-sm">
            <div className="flex items-center gap-2 sm:gap-3 flex-wrap">
              <div className="flex gap-1 order-1">
                <button
                  onClick={() => {
                    setListSubTab('active');
                    setPage(1);
                    setSearch('');
                    setSearchInput('');
                    setActiveFilter(null);
                  }}
                  className={`px-3 py-1.5 text-sm font-medium rounded-md transition-colors whitespace-nowrap ${
                    listSubTab === 'active'
                      ? 'bg-primary-50 dark:bg-primary-900/20 text-primary-600 dark:text-primary-400'
                      : 'text-gray-600 dark:text-slate-400 hover:bg-gray-50 dark:hover:bg-slate-700/50'
                  }`}
                >
                  {t('integration.apiKeys.tabActive')}
                </button>
                <button
                  onClick={() => {
                    setListSubTab('trash');
                    setPage(1);
                    setSearch('');
                    setSearchInput('');
                    setActiveFilter(null);
                  }}
                  className={`px-3 py-1.5 text-sm font-medium rounded-md transition-colors whitespace-nowrap ${
                    listSubTab === 'trash'
                      ? 'bg-primary-50 dark:bg-primary-900/20 text-primary-600 dark:text-primary-400'
                      : 'text-gray-600 dark:text-slate-400 hover:bg-gray-50 dark:hover:bg-slate-700/50'
                  }`}
                >
                  {t('integration.common.trash')}
                </button>
              </div>
              <div className="flex-1 min-w-[160px] sm:min-w-[200px] order-3 sm:order-2 w-full sm:w-auto">
                <div className="relative flex items-center gap-1">
                  <div className="relative flex-1">
                    <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400 pointer-events-none" />
                    <input
                      type="text"
                      placeholder={t('integration.apiKeys.searchPlaceholder')}
                      value={searchInput}
                      onChange={(e) => handleSearchInput(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') triggerSearchNow();
                      }}
                      className="w-full pl-9 pr-3 py-2 text-sm border border-gray-200 dark:border-slate-700 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:bg-slate-900/50 dark:text-slate-200"
                    />
                  </div>
                  <button
                    onClick={triggerSearchNow}
                    className="inline-flex items-center gap-1 px-3 py-2 text-sm font-medium text-white bg-primary-600 rounded-lg hover:bg-primary-700 transition shadow-sm whitespace-nowrap"
                  >
                    <Search className="w-3.5 h-3.5" />
                    <span className="hidden sm:inline">{t('common.search')}</span>
                  </button>
                </div>
              </div>
              {listSubTab === 'active' && (
                <select
                  value={activeFilter === null ? '' : String(activeFilter)}
                  onChange={(e) => { setActiveFilter(e.target.value === '' ? null : e.target.value === 'true'); setPage(1); }}
                  className="px-3 py-2 text-sm border border-gray-200 dark:border-slate-700 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:bg-slate-900/50 dark:text-slate-200 order-2 sm:order-3"
                >
                  <option value="">{t('integration.common.allStatuses')}</option>
                  <option value="true">{t('integration.common.enabled')}</option>
                  <option value="false">{t('integration.common.disabled')}</option>
                </select>
              )}
              {listSubTab === 'trash' && (
                <button
                  onClick={() => setClearTrashOpen(true)}
                  className="px-3 py-2 text-sm border border-red-200 dark:border-red-700 text-red-600 dark:text-red-400 rounded-lg hover:bg-red-50 dark:hover:bg-red-900/20 transition order-2 sm:order-3"
                >
                  {t('integration.common.clearTrash')}
                </button>
              )}
            </div>
          </div>

          <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 shadow-sm overflow-hidden">
            {/* PC 端表格视图 */}
            <div className="hidden md:block overflow-x-auto">
              <table className="w-full">
                <thead className="bg-gray-50 dark:bg-slate-900 border-b border-gray-200 dark:border-slate-700">
                  <tr>
                    <th className="px-5 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('integration.common.name')}</th>
                    <th className="px-5 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('integration.apiKeys.columns.keyPrefix')}</th>
                    <th className="px-5 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('integration.apiKeys.columns.createdBy')}</th>
                    {listSubTab === 'active' && (
                      <th className="px-5 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('integration.apiKeys.columns.usageCount')}</th>
                    )}
                    <th className="px-5 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('integration.common.status')}</th>
                    <th className="px-5 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('integration.apiKeys.columns.createdAt')}</th>
                    <th className="px-5 py-3 text-right text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('integration.common.actions')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100 dark:divide-slate-700/50">
                  {(listSubTab === 'active' ? apiKeys : trashApiKeys).length === 0 ? (
                    <tr>
                      <td colSpan={listSubTab === 'active' ? 7 : 6} className="px-5 py-12 text-center text-gray-400 dark:text-slate-500">
                        <Key className="w-12 h-12 mx-auto mb-3 opacity-30" />
                        {listSubTab === 'active' ? t('integration.apiKeys.noApiKeys') : t('integration.common.trashEmpty')}
                      </td>
                    </tr>
                  ) : (
                    (listSubTab === 'active' ? apiKeys : trashApiKeys).map((key) => (
                      <tr key={key.id} className="hover:bg-gray-50 dark:hover:bg-slate-700/30">
                        <td className="px-5 py-4">
                          <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{key.name}</div>
                          {key.description && (
                            <div className="text-sm text-gray-500 dark:text-slate-400 truncate max-w-xs">{key.description}</div>
                          )}
                        </td>
                        <td className="px-5 py-4 whitespace-nowrap">
                          <code className="text-sm text-gray-700 dark:text-slate-300 bg-gray-100 dark:bg-slate-700 px-2 py-1 rounded">
                            {key.keyPrefix}
                          </code>
                        </td>
                        <td className="px-5 py-4 whitespace-nowrap text-sm text-gray-600 dark:text-slate-300">
                          {key.userName ? (
                            <span className="inline-flex items-center gap-1.5">
                              <Users className="w-3.5 h-3.5 text-gray-400" />
                              {key.userName}
                            </span>
                          ) : '-'}
                        </td>
                        {listSubTab === 'active' && (
                        <td className="px-5 py-4 whitespace-nowrap text-sm text-gray-600 dark:text-slate-300">
                          <span className="inline-flex items-center gap-1.5">
                            <BarChart3 className="w-3.5 h-3.5 text-gray-400" />
                            {key.usageCount}
                          </span>
                        </td>
                      )}
                        <td className="px-5 py-4 whitespace-nowrap">
                          {listSubTab === 'active' ? (
                            <button
                              onClick={() => handleToggle(key.id)}
                              className={`inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-full cursor-pointer transition-colors ${
                                key.isActive
                                  ? 'bg-green-50 text-green-700 dark:bg-green-900/30 dark:text-green-400 hover:bg-green-100 dark:hover:bg-green-900/50'
                                  : 'bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-slate-400 hover:bg-gray-200 dark:hover:bg-slate-600'
                              }`}
                            >
                              {key.isActive ? <CheckCircle2 className="w-3 h-3" /> : <XCircle className="w-3 h-3" />}
                              {key.isActive ? t('integration.common.enabled') : t('integration.common.disabled')}
                            </button>
                          ) : (
                            <span className="text-sm text-gray-400 dark:text-slate-500">{t('integration.common.deleted')}</span>
                          )}
                        </td>
                        <td className="px-5 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-slate-400">
                          {formatDateTime(key.createdAt)}
                        </td>
                        <td className="px-5 py-4 whitespace-nowrap text-right">
                          <div className="flex items-center justify-end gap-1">
                            {listSubTab === 'active' ? (
                              <>
                                <button
                                  onClick={() => setRegenId(key.id)}
                                  className="p-1.5 text-gray-500 dark:text-slate-400 hover:text-amber-600 hover:bg-amber-50 dark:hover:bg-amber-900/20 rounded-md transition"
                                  title={t('integration.apiKeys.regenerate')}
                                >
                                  <RefreshCw className="w-4 h-4" />
                                </button>
                                <button
                                  onClick={() => openEdit(key)}
                                  className="p-1.5 text-gray-500 dark:text-slate-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/20 rounded-md transition"
                                  title={t('common.edit')}
                                >
                                  <Edit2 className="w-4 h-4" />
                                </button>
                                <button
                                  onClick={() => setDeleteId(key.id)}
                                  className="p-1.5 text-gray-500 dark:text-slate-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-md transition"
                                  title={t('common.delete')}
                                >
                                  <Trash2 className="w-4 h-4" />
                                </button>
                              </>
                            ) : (
                              <>
                                <button
                                  onClick={() => setRestoreId(key.id)}
                                  className="p-1.5 text-gray-500 dark:text-slate-400 hover:text-green-600 hover:bg-green-50 dark:hover:bg-green-900/20 rounded-md transition"
                                  title={t('integration.common.restore')}
                                >
                                  <CheckCircle2 className="w-4 h-4" />
                                </button>
                                <button
                                  onClick={() => setHardDeleteId(key.id)}
                                  className="p-1.5 text-gray-500 dark:text-slate-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-md transition"
                                  title={t('integration.common.hardDelete')}
                                >
                                  <Trash2 className="w-4 h-4" />
                                </button>
                              </>
                            )}
                          </div>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            {/* 移动端卡片视图 */}
            <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
              {(listSubTab === 'active' ? apiKeys : trashApiKeys).length === 0 ? (
                <div className="py-12 text-center text-gray-400 dark:text-slate-500">
                  <Key className="w-10 h-10 mx-auto mb-2 opacity-30" />
                  <p className="text-sm">{listSubTab === 'active' ? t('integration.apiKeys.noApiKeys') : t('integration.common.trashEmpty')}</p>
                </div>
              ) : (
                (listSubTab === 'active' ? apiKeys : trashApiKeys).map((key) => (
                  <div key={key.id} className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700/30 space-y-3">
                    <div className="flex items-start justify-between gap-2">
                      <div className="flex items-center gap-2.5 min-w-0 flex-1">
                        <div className={`shrink-0 w-9 h-9 rounded-xl flex items-center justify-center ring-1 ${
                          listSubTab === 'trash'
                            ? 'bg-gray-100 dark:bg-slate-700/60 text-gray-500 dark:text-slate-400 ring-gray-300/60 dark:ring-slate-600/60'
                            : key.isActive
                              ? 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-600 dark:text-emerald-400 ring-emerald-500/20'
                              : 'bg-gray-100 dark:bg-slate-700/60 text-gray-500 dark:text-slate-400 ring-gray-300/60 dark:ring-slate-600/60'
                        }`}>
                          <Key className="w-4 h-4" />
                        </div>
                        <div className="min-w-0">
                          <div className={`text-sm font-bold text-gray-900 dark:text-slate-100 truncate ${listSubTab === 'trash' ? 'line-through decoration-1 decoration-rose-300/60 dark:decoration-rose-800/50' : ''}`}>
                            {key.name}
                          </div>
                          <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5 flex items-center gap-1.5">
                            <code className="font-mono bg-gray-100 dark:bg-slate-700 px-1.5 py-0.5 rounded">{key.keyPrefix}</code>
                            {key.userName && (
                              <>
                                <span className="opacity-40">·</span>
                                <span className="inline-flex items-center gap-0.5 truncate">
                                  <Users className="w-3 h-3" />
                                  {key.userName}
                                </span>
                              </>
                            )}
                          </div>
                        </div>
                      </div>
                      {listSubTab === 'active' ? (
                        <button
                          onClick={() => handleToggle(key.id)}
                          className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors flex-shrink-0 ${
                            key.isActive ? 'bg-green-500' : 'bg-gray-300 dark:bg-slate-600'
                          }`}
                          title={key.isActive ? t('integration.common.clickToDisable') : t('integration.common.clickToEnable')}
                        >
                          <span
                            className={`inline-block h-4 w-4 transform rounded-full bg-white transition ${
                              key.isActive ? 'translate-x-6' : 'translate-x-1'
                            }`}
                          />
                        </button>
                      ) : (
                        <span className="text-[11px] text-gray-400 dark:text-slate-500 flex-shrink-0">{t('integration.common.deleted')}</span>
                      )}
                    </div>

                    {key.description && (
                      <div className="text-xs text-gray-600 dark:text-slate-400 line-clamp-2">
                        {key.description}
                      </div>
                    )}

                    {listSubTab === 'active' && (
                      <div className="flex items-center justify-between pt-1">
                        <div className="text-[11px] text-gray-500 dark:text-slate-400 flex items-center gap-2">
                          <span className="inline-flex items-center gap-1">
                            <BarChart3 className="w-3 h-3" />
                            {t('integration.apiKeys.callLabel')} <span className="font-bold text-gray-900 dark:text-slate-100">{key.usageCount}</span>
                          </span>
                          <span className="opacity-40">·</span>
                          <span>{formatDateTime(key.createdAt)}</span>
                        </div>
                        <div className="flex items-center gap-1">
                          <button
                            onClick={() => setRegenId(key.id)}
                            className="p-2 text-gray-500 dark:text-slate-400 hover:text-amber-600 hover:bg-amber-50 dark:hover:bg-amber-900/30 rounded-xl transition"
                            title={t('integration.apiKeys.regenerate')}
                          >
                            <RefreshCw className="w-4 h-4" />
                          </button>
                          <button
                            onClick={() => openEdit(key)}
                            className="p-2 text-gray-500 dark:text-slate-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition"
                            title={t('common.edit')}
                          >
                            <Edit2 className="w-4 h-4" />
                          </button>
                          <button
                            onClick={() => setDeleteId(key.id)}
                            className="p-2 text-gray-500 dark:text-slate-400 hover:text-rose-600 hover:bg-rose-50 dark:hover:bg-rose-900/30 rounded-xl transition"
                            title={t('common.delete')}
                          >
                            <Trash2 className="w-4 h-4" />
                          </button>
                        </div>
                      </div>
                    )}

                    {listSubTab === 'trash' && (
                      <div className="flex items-center justify-between pt-1">
                        <div className="text-[11px] text-gray-500 dark:text-slate-400">
                          {formatDateTime(key.createdAt)}
                        </div>
                        <div className="flex items-center gap-1">
                          <button
                            onClick={() => setRestoreId(key.id)}
                            className="p-2 text-gray-500 dark:text-slate-400 hover:text-emerald-600 hover:bg-emerald-50 dark:hover:bg-emerald-900/30 rounded-xl transition"
                            title={t('integration.common.restore')}
                          >
                            <CheckCircle2 className="w-4 h-4" />
                          </button>
                          <button
                            onClick={() => setHardDeleteId(key.id)}
                            className="p-2 text-gray-500 dark:text-slate-400 hover:text-rose-600 hover:bg-rose-50 dark:hover:bg-rose-900/30 rounded-xl transition"
                            title={t('integration.common.hardDelete')}
                          >
                            <Trash2 className="w-4 h-4" />
                          </button>
                        </div>
                      </div>
                    )}
                  </div>
                ))
              )}
            </div>

            {totalPages > 1 && (
              <div className="flex items-center justify-between px-4 sm:px-5 py-3 border-t border-gray-100 dark:border-slate-700/50">
                <p className="text-xs sm:text-sm text-gray-500 dark:text-slate-400">
                  {t('integration.common.paginationTotal', { count: totalItems, page, total: totalPages })}
                </p>
                <div className="flex items-center gap-1">
                  <button
                    onClick={() => setPage(Math.max(1, page - 1))}
                    disabled={page === 1}
                    className="p-1.5 border border-gray-300 dark:border-slate-600 rounded-md text-sm disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50 dark:hover:bg-slate-700"
                  >
                    <ChevronLeft className="w-4 h-4" />
                  </button>
                  <span className="px-3 py-1 text-sm text-gray-600 dark:text-slate-300">
                    {page} / {totalPages}
                  </span>
                  <button
                    onClick={() => setPage(Math.min(totalPages, page + 1))}
                    disabled={page === totalPages}
                    className="p-1.5 border border-gray-300 dark:border-slate-600 rounded-md text-sm disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50 dark:hover:bg-slate-700"
                  >
                    <ChevronRight className="w-4 h-4" />
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      <Modal
        open={showEditor}
        onClose={() => setShowEditor(false)}
        title={editingKey ? t('integration.apiKeys.editor.editTitle') : t('integration.apiKeys.editor.createTitle')}
        size="lg"
        footer={
          <>
            <button
              onClick={() => setShowEditor(false)}
              disabled={saving}
              className="px-4 py-2 text-sm text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition disabled:opacity-50"
            >
              {t('common.cancel')}
            </button>
            <button
              onClick={handleSave}
              disabled={saving}
              className="px-4 py-2 text-sm bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white rounded-lg transition"
            >
              {saving ? t('integration.common.saving') : editingKey ? t('integration.apiKeys.editor.update') : t('integration.apiKeys.editor.create')}
            </button>
          </>
        }
      >
        <div className="space-y-4">
          {formError && (
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-400 px-3 py-2 rounded-lg text-sm flex items-center gap-2">
              <AlertCircle className="w-4 h-4 flex-shrink-0" />
              {formError}
            </div>
          )}

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
              {t('integration.apiKeys.editor.name')} <span className="text-red-500">*</span>
            </label>
            <input
              type="text"
              value={formData.name}
              onChange={(e) => setFormData({ ...formData, name: e.target.value })}
              className="w-full px-3 py-2 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:bg-slate-900/50 dark:text-slate-200"
              placeholder={t('integration.apiKeys.editor.namePlaceholder')}
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
              {t('integration.apiKeys.editor.description')}
            </label>
            <textarea
              value={formData.description || ''}
              onChange={(e) => setFormData({ ...formData, description: e.target.value })}
              rows={2}
              className="w-full px-3 py-2 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:bg-slate-900/50 dark:text-slate-200"
              placeholder={t('integration.apiKeys.editor.descriptionPlaceholder')}
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-2">
              {t('integration.apiKeys.editor.scopes')} <span className="text-red-500">*</span>
            </label>
            <div className="max-h-64 overflow-y-auto border border-gray-200 dark:border-slate-700 rounded-lg p-3 space-y-3">
              {SCOPE_GROUPS.map((group) => (
                <div key={group.categoryKey}>
                  <div className="text-xs font-medium text-gray-500 dark:text-slate-400 mb-1.5">{t(group.categoryKey)}</div>
                  <div className="grid grid-cols-2 gap-2">
                    {group.scopes.map((scope) => (
                      <label key={scope.value} className="flex items-center gap-2 text-sm cursor-pointer p-2 rounded-md hover:bg-gray-50 dark:hover:bg-slate-700/50">
                        <input
                          type="checkbox"
                          checked={formData.scopes.includes(scope.value)}
                          onChange={(e) => {
                            if (e.target.checked) {
                              setFormData({ ...formData, scopes: [...formData.scopes, scope.value] });
                            } else {
                              setFormData({
                                ...formData,
                                scopes: formData.scopes.filter((s) => s !== scope.value),
                              });
                            }
                          }}
                          className="rounded text-primary-600 focus:ring-primary-500"
                        />
                        <span className="text-gray-700 dark:text-slate-300">{t(scope.labelKey)}</span>
                      </label>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>

          <div className="flex items-center">
            <input
              type="checkbox"
              id="isActive"
              checked={formData.isActive}
              onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
              className="rounded text-primary-600 focus:ring-primary-500"
            />
            <label htmlFor="isActive" className="ml-2 text-sm text-gray-700 dark:text-slate-300">
              {t('integration.apiKeys.editor.activateNow')}
            </label>
          </div>
        </div>
      </Modal>

      <Modal
        open={!!newKey}
        onClose={() => setNewKey(null)}
        title={t('integration.apiKeys.created.title')}
        footer={
          <button
            onClick={() => setNewKey(null)}
            className="px-4 py-2 text-sm bg-primary-600 hover:bg-primary-700 text-white rounded-lg transition"
          >
            {t('integration.apiKeys.created.saved')}
          </button>
        }
      >
        {newKey && (
          <div className="space-y-4">
            <div className="p-4 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 rounded-lg">
              <p className="text-sm text-amber-700 dark:text-amber-400 flex items-start gap-2">
                <AlertCircle className="w-5 h-5 flex-shrink-0 mt-0.5" />
                <span>
                  <strong>{t('integration.apiKeys.created.warningStrong')}</strong>{t('integration.apiKeys.created.warningBody')}
                </span>
              </p>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('integration.apiKeys.created.keyLabel')}</label>
              <div className="flex">
                <input
                  type="text"
                  readOnly
                  value={newKey.key || ''}
                  className="flex-1 px-3 py-2 border border-gray-300 dark:border-slate-600 rounded-l-lg bg-gray-50 dark:bg-slate-900 font-mono text-sm dark:text-slate-200"
                />
                <button
                  onClick={() => copyToClipboard(newKey.key || '')}
                  className="px-4 py-2 bg-primary-600 text-white rounded-r-lg hover:bg-primary-700 transition flex items-center gap-1.5"
                >
                  <Copy className="w-4 h-4" />
                  {t('integration.apiKeys.created.copy')}
                </button>
              </div>
            </div>
          </div>
        )}
      </Modal>

      <TrashDialogs<number>
        itemLabel={t('integration.apiKeys.title')}
        useIdMode
        deleteTarget={deleteId}
        restoreTarget={restoreId}
        hardDeleteTarget={hardDeleteId}
        clearTrashOpen={clearTrashOpen}
        onDelete={handleDelete}
        onRestore={handleRestore}
        onHardDelete={handleHardDelete}
        onClearTrash={handleClearTrash}
        onCancelDelete={() => setDeleteId(null)}
        onCancelRestore={() => setRestoreId(null)}
        onCancelHardDelete={() => setHardDeleteId(null)}
        onCancelClearTrash={() => setClearTrashOpen(false)}
      />

      <ConfirmDialog
        open={!!regenId}
        title={t('integration.apiKeys.regenerateTitle')}
        message={t('integration.apiKeys.regenerateMessage')}
        confirmText={t('integration.apiKeys.regenerate')}
        cancelText={t('common.cancel')}
        onConfirm={handleRegenerate}
        onCancel={() => setRegenId(null)}
        variant="danger"
      />

      {toast && (
        <div className="fixed top-4 right-4 z-50">
          <div
            className={`px-4 py-2 rounded-lg shadow-lg text-white text-sm flex items-center gap-2 ${
              toast.type === 'success' ? 'bg-green-500' : 'bg-red-500'
            }`}
          >
            {toast.type === 'success' ? <CheckCircle2 className="w-4 h-4" /> : <AlertCircle className="w-4 h-4" />}
            {toast.text}
          </div>
        </div>
      )}
    </div>
  );
}
