import { useState, useEffect, useCallback, useRef } from 'react';
import { moduleApi } from '../api';
import { useTimedToast } from '../hooks/useTimedToast';
import type {
  ModuleDto,
  ModuleOverviewResponse,
  CreateModuleRequest,
  UpdateModuleRequest,
} from '../api/types';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import { formatDateTime } from '../utils/format';
import { useModules } from '../context/ModuleContext';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import {
  Puzzle,
  Activity,
  List,
  Plus,
  Search,
  Edit2,
  Trash2,
  CheckCircle2,
  XCircle,
  ToggleLeft,
  ToggleRight,
  ChevronLeft,
  ChevronRight,
  Settings,
  Package,
  Zap,
  BarChart3,
  Wrench,
  Globe,
  MessageSquare,
  Layers,
  Users,
} from 'lucide-react';

// 分类码 → key：下拉选项用（`labelKey` 而非 `label`，漏包 `t()` 的使用点会以 TS2339 报出来）
const categoryOptions = [
  { value: 'integration', labelKey: 'integration.modules.category.integration' },
  { value: 'communication', labelKey: 'integration.modules.category.communication' },
  { value: 'reports', labelKey: 'integration.modules.category.reports' },
  { value: 'tools', labelKey: 'integration.modules.category.tools' },
  { value: 'general', labelKey: 'integration.modules.category.general' },
  { value: 'automation', labelKey: 'integration.modules.category.automation' },
] satisfies { value: string; labelKey: TranslationKey }[];

/**
 * 分类码 → key：列表徽章与**概览页的分类统计**共用。
 *
 * 概览页原来渲染后端的 `ModuleCategoryStats.CategoryLabel`（`ModuleService.cs:200-212` 的中文
 * switch）——那是"后端中文标签直出"，本波改为按 code 查本表取词（zh 值与后端 switch 的 6 条
 * 逐字相同，故中文零回归；未命中 code 仍回落 code 本身，与后端 `_ => category` 同义）。
 */
const CATEGORY_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  integration: 'integration.modules.category.integration',
  communication: 'integration.modules.category.communication',
  reports: 'integration.modules.category.reports',
  tools: 'integration.modules.category.tools',
  general: 'integration.modules.category.general',
  automation: 'integration.modules.category.automation',
};

const CATEGORY_BADGE: Record<string, string> = {
  integration: 'bg-blue-50 text-blue-700 dark:bg-blue-900/20 dark:text-blue-400',
  communication: 'bg-green-50 text-green-700 dark:bg-green-900/20 dark:text-green-400',
  reports: 'bg-purple-50 text-purple-700 dark:bg-purple-900/20 dark:text-purple-400',
  tools: 'bg-amber-50 text-amber-700 dark:bg-amber-900/20 dark:text-amber-400',
  general: 'bg-gray-50 text-gray-700 dark:bg-slate-700 dark:text-slate-300',
  automation: 'bg-rose-50 text-rose-700 dark:bg-rose-900/20 dark:text-rose-400',
};

const tabs = [
  { key: 'overview', labelKey: 'integration.common.tabOverview', icon: <Activity className="w-4 h-4" /> },
  { key: 'list', labelKey: 'integration.modules.tabList', icon: <List className="w-4 h-4" /> },
] as const;

export default function Modules() {
  const { refreshModules } = useModules();
  const { t } = useI18n();
  const [activeTab, setActiveTab] = useState<'overview' | 'list'>('overview');
  const [overview, setOverview] = useState<ModuleOverviewResponse | null>(null);
  const [modules, setModules] = useState<ModuleDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [totalItems, setTotalItems] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [searchInput, setSearchInput] = useState('');
  const [categoryFilter, setCategoryFilter] = useState<string | null>(null);
  const [activeFilter, setActiveFilter] = useState<boolean | null>(null);

  const [showEditor, setShowEditor] = useState(false);
  const [editingModule, setEditingModule] = useState<ModuleDto | null>(null);

  const [formData, setFormData] = useState<CreateModuleRequest>({
    name: '',
    title: '',
    description: '',
    version: '1.0.0',
    author: '',
    icon: 'puzzle',
    category: 'integration',
    sortOrder: 0,
  });
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);

  const [deleteName, setDeleteName] = useState<string | null>(null);
  const { message: toast, show: showToast } = useTimedToast();

  const pageSize = 10;

  /** 分类码 → 本地化标签；未知 code 回落 code 本身（与后端 `GetCategoryLabel` 的默认分支同义） */
  const categoryLabel = (code: string) => {
    const key = CATEGORY_LABEL_KEYS[code];
    return key ? t(key) : code;
  };

  // T000040：搜索触发策略——650ms 输入防抖自动搜索 + 回车/按钮即时触发，
  // 不再在每次 onChange 立即请求（避免用户尚未输入完成就触发搜索），也不在失焦时触发（防止误搜）。
  const searchTimerRef = useRef<number | null>(null);
  const applySearch = useCallback((value: string) => {
    setSearch(value);
    setPage(1);
  }, []);

  const handleSearchInput = useCallback((value: string) => {
    setSearchInput(value);
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
    if (activeTab === 'overview') loadOverview();
    if (activeTab === 'list') loadList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab]);

  useEffect(() => {
    if (activeTab === 'list') loadList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, search, categoryFilter, activeFilter]);

  async function loadOverview() {
    setLoading(true);
    try {
      const data = await moduleApi.overview();
      setOverview(data);
    } finally {
      setLoading(false);
    }
  }

  async function loadList() {
    setLoading(true);
    try {
      const data = await moduleApi.list({
        page,
        pageSize,
        category: categoryFilter,
        isActive: activeFilter,
        search: search || null,
      });
      setModules(data.data ?? []);
      setTotalItems(data.totalItems ?? 0);
    } finally {
      setLoading(false);
    }
  }

  function openCreate() {
    setEditingModule(null);
    setFormData({
      name: '',
      title: '',
      description: '',
      version: '1.0.0',
      author: '',
      icon: 'puzzle',
      category: 'integration',
      sortOrder: 0,
    });
    setFormError('');
    setShowEditor(true);
  }

  function openEdit(m: ModuleDto) {
    setEditingModule(m);
    setFormData({
      name: m.name,
      title: m.title,
      description: m.description,
      version: m.version,
      author: m.author,
      icon: m.icon,
      category: m.category,
      sortOrder: m.sortOrder,
    });
    setFormError('');
    setShowEditor(true);
  }

  async function handleSave() {
    if (!formData.name.trim()) {
      setFormError(t('integration.modules.editor.errNameRequired'));
      return;
    }
    if (!formData.title.trim()) {
      setFormError(t('integration.modules.editor.errTitleRequired'));
      return;
    }
    setSaving(true);
    setFormError('');
    try {
      if (editingModule) {
        await moduleApi.update(editingModule.name, formData as UpdateModuleRequest);
        showToast({ type: 'success', text: t('integration.modules.toast.updated') });
      } else {
        await moduleApi.create(formData);
        showToast({ type: 'success', text: t('integration.modules.toast.created') });
      }
      setShowEditor(false);
      if (activeTab === 'overview') loadOverview();
      if (activeTab === 'list') loadList();
    } catch (e: unknown) {
      setFormError(e instanceof Error ? e.message : t('integration.common.saveFailed'));
    } finally {
      setSaving(false);
    }
  }

  /** 确认对话框触发的实际删除执行（不带参数，从 deleteName 读取） */
  async function executeDelete() {
    if (!deleteName) return;
    try {
      await moduleApi.remove(deleteName);
      showToast({ type: 'success', text: t('integration.modules.toast.deleted') });
      setDeleteName(null);
      if (activeTab === 'overview') loadOverview();
      if (activeTab === 'list') loadList();
    } catch (e: unknown) {
      showToast({ type: 'error', text: e instanceof Error ? e.message : t('integration.common.deleteFailed') });
    }
  }

  async function handleToggleActive(m: ModuleDto) {
    if (m.isSystemModule) {
      showToast({ type: 'error', text: t('integration.modules.err.systemCannotDisable') });
      return;
    }
    try {
      await moduleApi.toggleActive(m.name);
      showToast({ type: 'success', text: m.isActive ? t('integration.modules.toast.disabled') : t('integration.modules.toast.enabled') });
      if (activeTab === 'overview') loadOverview();
      if (activeTab === 'list') loadList();
      await refreshModules();
    } catch (e: unknown) {
      showToast({ type: 'error', text: e instanceof Error ? e.message : t('integration.common.operationFailed') });
    }
  }

  /** 列表"删除"按钮点击：对核心模块前端直接拒绝，其他模块打开确认对话框 */
  async function handleDelete(m: ModuleDto) {
    if (m.isSystemModule) {
      showToast({ type: 'error', text: t('integration.modules.err.systemCannotDelete') });
      return;
    }
    setDeleteName(m.name);
  }

  const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));

  const getCategoryIcon = (category: string) => {
    switch (category) {
      case 'integration': return <Globe className="w-5 h-5" />;
      case 'communication': return <MessageSquare className="w-5 h-5" />;
      case 'reports': return <BarChart3 className="w-5 h-5" />;
      case 'tools': return <Wrench className="w-5 h-5" />;
      case 'general': return <Settings className="w-5 h-5" />;
      case 'automation': return <Zap className="w-5 h-5" />;
      default: return <Package className="w-5 h-5" />;
    }
  };

  const inputClass = "bg-white dark:bg-slate-900 w-full px-3.5 py-2.5 text-sm border border-gray-300 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition-all";
  const labelClass = "block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5";

  return (
    <div className="space-y-5">
      <div className="relative overflow-hidden bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-800 dark:to-slate-900 rounded-2xl border border-primary-100 dark:border-slate-700/50 p-4 sm:p-6">
        <div className="absolute top-0 right-0 w-64 h-64 bg-primary-200/30 dark:bg-primary-500/10 rounded-full -translate-y-1/2 translate-x-1/2 blur-3xl" />
        <div className="absolute bottom-0 left-0 w-48 h-48 bg-indigo-200/30 dark:bg-indigo-500/10 rounded-full translate-y-1/2 -translate-x-1/2 blur-3xl" />
        <div className="relative flex items-start justify-between gap-4 flex-wrap">
          <div className="flex items-start gap-3 sm:gap-4 min-w-0 flex-1">
            <div className="p-2.5 sm:p-3 bg-white dark:bg-slate-800 rounded-xl shadow-md flex-shrink-0">
              <Puzzle className="w-6 h-6 sm:w-7 sm:h-7 text-primary-600 dark:text-primary-400" />
            </div>
            <div className="min-w-0">
              <h1 className="text-lg sm:text-xl font-bold text-gray-900 dark:text-slate-100">{t('integration.modules.pageTitle')}</h1>
              <p className="text-xs sm:text-sm text-gray-600 dark:text-slate-400 mt-1 max-w-xl">
                {t('integration.modules.subtitle')}
              </p>
            </div>
          </div>
          {activeTab === 'list' && (
            <button
              onClick={openCreate}
              className="inline-flex items-center gap-1.5 sm:gap-2 px-4 sm:px-5 py-2 sm:py-2.5 text-sm font-medium text-white bg-primary-600 rounded-xl hover:bg-primary-700 transition-all shadow-md hover:shadow-lg active:scale-[0.98] flex-shrink-0"
            >
              <Plus className="w-4 h-4 sm:w-4.5 sm:h-4.5" />
              <span className="hidden sm:inline">{t('integration.modules.newModule')}</span>
              <span className="sm:hidden">{t('integration.common.newShort')}</span>
            </button>
          )}
        </div>
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

      {activeTab === 'overview' && overview && !loading && (
        <div className="space-y-5">
          <div className="grid grid-cols-3 gap-2 sm:gap-4">
            {[
              { label: t('integration.modules.statTotal'), value: overview.total, color: 'text-primary-600 dark:text-primary-400', bg: 'bg-primary-50 dark:bg-primary-900/20', icon: Package },
              { label: t('integration.common.statsActive'), value: overview.active, color: 'text-green-600 dark:text-green-400', bg: 'bg-green-50 dark:bg-green-900/20', icon: CheckCircle2 },
              { label: t('integration.common.statsInactive'), value: overview.inactive, color: 'text-gray-600 dark:text-slate-400', bg: 'bg-gray-50 dark:bg-slate-700/50', icon: XCircle },
            ].map((s) => (
              <div key={s.label} className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 p-3 sm:p-5 shadow-sm hover:shadow-md transition-all group">
                <div className="flex items-center justify-between">
                  <span className="text-xs sm:text-sm text-gray-500 dark:text-slate-400 truncate">{s.label}</span>
                  <div className={`p-1.5 sm:p-2 rounded-lg ${s.bg} transition-transform group-hover:scale-110 flex-shrink-0`}>
                    <s.icon className={`w-4 h-4 sm:w-5 sm:h-5 ${s.color}`} />
                  </div>
                </div>
                <div className={`text-xl sm:text-3xl font-bold mt-2 sm:mt-3 ${s.color}`}>{s.value}</div>
              </div>
            ))}
          </div>

          <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 shadow-sm overflow-hidden">
            <div className="px-4 sm:px-5 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50">
              <h3 className="text-sm font-semibold text-gray-700 dark:text-slate-200">{t('integration.modules.byCategory')}</h3>
            </div>
            <div className="divide-y divide-gray-100 dark:divide-slate-700/50">
              {overview.categories.map((c) => (
                <div key={c.category} className="px-4 sm:px-5 py-3 sm:py-4 flex items-center justify-between hover:bg-gray-50/50 dark:hover:bg-slate-700/30 transition-colors">
                  <div className="flex items-center gap-2.5 sm:gap-3 min-w-0">
                    <div className={`p-1.5 sm:p-2 rounded-lg ${CATEGORY_BADGE[c.category] || 'bg-gray-100 text-gray-600'} flex-shrink-0`}>
                      {getCategoryIcon(c.category)}
                    </div>
                    <span className="text-xs sm:text-sm font-medium text-gray-700 dark:text-slate-200 truncate">{categoryLabel(c.category)}</span>
                  </div>
                  <div className="flex items-center gap-3 sm:gap-5 flex-shrink-0">
                    <div className="text-right">
                      <div className="text-base sm:text-lg font-bold text-gray-900 dark:text-slate-100">{c.count}</div>
                      <div className="text-[10px] sm:text-xs text-gray-400 dark:text-slate-500">{t('integration.modules.countTotal')}</div>
                    </div>
                    <div className="w-px h-7 sm:h-8 bg-gray-200 dark:bg-slate-700" />
                    <div className="text-right">
                      <div className="text-base sm:text-lg font-bold text-green-600">{c.activeCount}</div>
                      <div className="text-[10px] sm:text-xs text-gray-400 dark:text-slate-500">{t('integration.common.statsActive')}</div>
                    </div>
                    <div className="w-16 sm:w-28">
                      <div className="w-full bg-gray-100 dark:bg-slate-700 rounded-full h-2">
                        <div
                          className="bg-green-500 h-2 rounded-full"
                          style={{ width: `${c.count > 0 ? (c.activeCount / c.count) * 100 : 0}%` }}
                        />
                      </div>
                    </div>
                  </div>
                </div>
              ))}
              {overview.categories.length === 0 && (
                <div className="px-4 sm:px-5 py-12 text-center">
                  <Layers className="w-12 h-12 mx-auto mb-3 text-gray-300 dark:text-slate-600" />
                  <p className="text-sm text-gray-400 dark:text-slate-500">{t('integration.modules.empty')}</p>
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {activeTab === 'list' && !loading && (
        <div className="space-y-4">
          <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 p-3 sm:p-4 shadow-sm">
            <div className="flex items-center gap-2 sm:gap-3 flex-wrap">
              <div className="flex-1 min-w-[160px] sm:min-w-[200px] w-full sm:w-auto">
                <div className="relative">
                  <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400 dark:text-slate-500" />
                  <input
                    type="text"
                    placeholder={t('integration.modules.searchPlaceholder')}
                    value={searchInput}
                    onChange={(e) => handleSearchInput(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') triggerSearchNow();
                    }}
                    className="bg-white dark:bg-slate-900 w-full pl-10 pr-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition-all"
                  />
                </div>
              </div>
              <select
                value={categoryFilter ?? ''}
                onChange={(e) => { setCategoryFilter(e.target.value || null); setPage(1); }}
                className="bg-white dark:bg-slate-900 px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition-all"
              >
                <option value="">{t('integration.modules.allCategories')}</option>
                {categoryOptions.map((o) => (
                  <option key={o.value} value={o.value}>{t(o.labelKey)}</option>
                ))}
              </select>
              <select
                value={activeFilter === null ? '' : String(activeFilter)}
                onChange={(e) => {
                  const v = e.target.value;
                  setActiveFilter(v === '' ? null : v === 'true');
                  setPage(1);
                }}
                className="bg-white dark:bg-slate-900 px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition-all"
              >
                <option value="">{t('integration.common.allStatuses')}</option>
                <option value="true">{t('integration.common.enabled')}</option>
                <option value="false">{t('integration.common.disabled')}</option>
              </select>
            </div>
          </div>

          <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 shadow-sm overflow-hidden">
            {modules.length === 0 ? (
              <div className="py-12 sm:py-16 text-center">
                <Puzzle className="w-10 h-10 sm:w-12 sm:h-12 mx-auto mb-3 text-gray-300 dark:text-slate-600" />
                <p className="text-sm text-gray-400 dark:text-slate-500">{t('integration.modules.empty')}</p>
              </div>
            ) : (
              <>
                {/* PC 端列表视图 */}
                <div className="hidden md:block divide-y divide-gray-100 dark:divide-slate-700/50">
                  {modules.map((m) => (
                    <div key={m.name} className="px-5 py-4 flex items-center gap-4 hover:bg-gray-50/70 dark:hover:bg-slate-700/30 transition-colors group">
                      <div className={`w-12 h-12 rounded-xl flex items-center justify-center flex-shrink-0 ${CATEGORY_BADGE[m.category] || 'bg-gray-100 text-gray-600'}`}>
                        {getCategoryIcon(m.category)}
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 flex-wrap">
                          <h4 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{m.title}</h4>
                          <span className="text-xs text-gray-400 dark:text-slate-500 font-mono">v{m.version}</span>
                          {m.isSystemModule && (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium text-primary-700 bg-primary-50 dark:bg-primary-900/30 dark:text-primary-400 rounded-full border border-primary-200/60 dark:border-primary-700/30">
                              <Zap className="w-3 h-3" />
                              {t('integration.modules.systemModule')}
                            </span>
                          )}
                          {m.isActive ? (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium text-green-700 bg-green-50 dark:bg-green-900/20 dark:text-green-400 rounded-full">
                              <CheckCircle2 className="w-3 h-3" />
                              {t('integration.common.enabled')}
                            </span>
                          ) : (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium text-gray-600 dark:text-slate-400 bg-gray-100 dark:bg-slate-700 rounded-full">
                              <XCircle className="w-3 h-3" />
                              {t('integration.common.disabled')}
                            </span>
                          )}
                          <span className={`inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-full ${CATEGORY_BADGE[m.category] || 'bg-gray-100 text-gray-600'}`}>
                            {categoryLabel(m.category)}
                          </span>
                        </div>
                        <p className="text-sm text-gray-500 dark:text-slate-400 mt-1 line-clamp-1">{m.description}</p>
                        <div className="text-xs text-gray-400 dark:text-slate-500 mt-1.5 flex items-center gap-3">
                          <span className="inline-flex items-center gap-1">
                            <Users className="w-3.5 h-3.5" />
                            {m.author}
                          </span>
                          <span>·</span>
                          <span>{t('integration.modules.updatedAt')} {formatDateTime(m.updatedAt)}</span>
                        </div>
                      </div>
                      <div className="flex items-center gap-1 flex-shrink-0">
                        <button
                          onClick={() => handleToggleActive(m)}
                          disabled={m.isSystemModule}
                          className={`p-2 rounded-lg transition-colors ${
                            m.isSystemModule
                              ? 'text-gray-300 dark:text-slate-600 cursor-not-allowed bg-transparent'
                              : 'text-gray-500 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/20'
                          }`}
                          title={m.isSystemModule ? t('integration.modules.err.systemCannotDisable') : (m.isActive ? t('integration.modules.action.disable') : t('integration.modules.action.enable'))}
                        >
                          {m.isActive ? <ToggleRight className="w-5 h-5 text-green-500" /> : <ToggleLeft className="w-5 h-5" />}
                        </button>
                        <button
                          onClick={() => openEdit(m)}
                          className="p-2 text-gray-500 hover:text-blue-600 hover:bg-blue-50 dark:hover:bg-blue-900/20 rounded-lg transition-colors"
                          title={t('common.edit')}
                        >
                          <Edit2 className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => handleDelete(m)}
                          disabled={m.isSystemModule}
                          className={`p-2 rounded-lg transition-colors ${
                            m.isSystemModule
                              ? 'text-gray-300 dark:text-slate-600 cursor-not-allowed bg-transparent'
                              : 'text-gray-500 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20'
                          }`}
                          title={m.isSystemModule ? t('integration.modules.err.systemCannotDelete') : t('common.delete')}
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                    </div>
                  ))}
                </div>

                {/* 移动端卡片视图 */}
                <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
                  {modules.map((m) => (
                    <div key={m.name} className="p-4 hover:bg-gray-50/70 dark:hover:bg-slate-700/30 space-y-3">
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex items-center gap-2.5 min-w-0 flex-1">
                          <div className={`shrink-0 w-10 h-10 rounded-xl flex items-center justify-center ${CATEGORY_BADGE[m.category] || 'bg-gray-100 text-gray-600'}`}>
                            {getCategoryIcon(m.category)}
                          </div>
                          <div className="min-w-0">
                            <div className="text-sm font-bold text-gray-900 dark:text-slate-100 truncate">{m.title}</div>
                            <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5 flex items-center gap-1.5 flex-wrap">
                              <span className="font-mono">v{m.version}</span>
                              <span className="opacity-40">·</span>
                              <span className="inline-flex items-center gap-0.5">
                                <Users className="w-3 h-3" />
                                {m.author}
                              </span>
                            </div>
                          </div>
                        </div>
                        <button
                          onClick={() => handleToggleActive(m)}
                          disabled={m.isSystemModule}
                          className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors flex-shrink-0 ${
                            m.isSystemModule ? 'bg-gray-300 dark:bg-slate-600 opacity-50 cursor-not-allowed' : (m.isActive ? 'bg-green-500' : 'bg-gray-300 dark:bg-slate-600')
                          }`}
                          title={m.isSystemModule ? t('integration.modules.err.systemCannotDisable') : (m.isActive ? t('integration.modules.action.disable') : t('integration.modules.action.enable'))}
                        >
                          <span
                            className={`inline-block h-4 w-4 transform rounded-full bg-white transition ${
                              m.isActive ? 'translate-x-6' : 'translate-x-1'
                            }`}
                          />
                        </button>
                      </div>

                      <div className="flex flex-wrap items-center gap-1.5">
                        {m.isSystemModule && (
                          <span className="inline-flex items-center gap-0.5 px-1.5 py-0.5 text-[11px] font-medium text-primary-700 bg-primary-50 dark:bg-primary-900/30 dark:text-primary-400 rounded-full">
                            <Zap className="w-2.5 h-2.5" />
                            {t('integration.modules.systemModule')}
                          </span>
                        )}
                        {m.isActive ? (
                          <span className="inline-flex items-center gap-0.5 px-1.5 py-0.5 text-[11px] font-medium text-green-700 bg-green-50 dark:bg-green-900/20 dark:text-green-400 rounded-full">
                            <CheckCircle2 className="w-2.5 h-2.5" />
                            {t('integration.common.enabled')}
                          </span>
                        ) : (
                          <span className="inline-flex items-center gap-0.5 px-1.5 py-0.5 text-[11px] font-medium text-gray-600 dark:text-slate-400 bg-gray-100 dark:bg-slate-700 rounded-full">
                            <XCircle className="w-2.5 h-2.5" />
                            {t('integration.common.disabled')}
                          </span>
                        )}
                        <span className={`inline-flex items-center gap-0.5 px-1.5 py-0.5 text-[11px] font-medium rounded-full ${CATEGORY_BADGE[m.category] || 'bg-gray-100 text-gray-600'}`}>
                          {categoryLabel(m.category)}
                        </span>
                      </div>

                      {m.description && (
                        <div className="text-xs text-gray-600 dark:text-slate-400 line-clamp-2">
                          {m.description}
                        </div>
                      )}

                      <div className="flex items-center justify-between pt-1">
                        <div className="text-[11px] text-gray-500 dark:text-slate-400">
                          {t('integration.modules.updatedAt')} {formatDateTime(m.updatedAt)}
                        </div>
                        <div className="flex items-center gap-1">
                          <button
                            onClick={() => openEdit(m)}
                            className="p-2 text-gray-500 dark:text-slate-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition"
                            title={t('common.edit')}
                          >
                            <Edit2 className="w-4 h-4" />
                          </button>
                          <button
                            onClick={() => handleDelete(m)}
                            disabled={m.isSystemModule}
                            className={`p-2 rounded-xl transition ${
                              m.isSystemModule
                                ? 'text-gray-300 dark:text-slate-600 cursor-not-allowed'
                                : 'text-gray-500 dark:text-slate-400 hover:text-rose-600 hover:bg-rose-50 dark:hover:bg-rose-900/30'
                            }`}
                            title={m.isSystemModule ? t('integration.modules.err.systemCannotDelete') : t('common.delete')}
                          >
                            <Trash2 className="w-4 h-4" />
                          </button>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              </>
            )}

            {totalPages > 1 && (
              <div className="flex items-center justify-between px-4 sm:px-5 py-3 sm:py-3.5 border-t border-gray-100 dark:border-slate-700/50 bg-gray-50/30 dark:bg-slate-900/20">
                <div className="text-xs sm:text-sm text-gray-500 dark:text-slate-400">
                  {t('integration.modules.paginationPrefix')}<span className="font-medium text-gray-700 dark:text-slate-300">{totalItems}</span>{t('integration.modules.paginationMiddle')}<span className="font-medium text-gray-700 dark:text-slate-300">{page}</span>{t('integration.modules.paginationSuffix', { total: totalPages })}
                </div>
                <div className="flex items-center gap-1.5">
                  <button
                    onClick={() => setPage((p) => Math.max(1, p - 1))}
                    disabled={page <= 1}
                    className="inline-flex items-center gap-1 px-2.5 sm:px-3 py-1.5 text-sm font-medium border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                  >
                    <ChevronLeft className="w-4 h-4" />
                    <span className="hidden sm:inline">{t('integration.modules.previousPage')}</span>
                  </button>
                  <button
                    onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                    disabled={page >= totalPages}
                    className="inline-flex items-center gap-1 px-2.5 sm:px-3 py-1.5 text-sm font-medium border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                  >
                    <span className="hidden sm:inline">{t('integration.modules.nextPage')}</span>
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
        title={
          <div className="flex items-center gap-3">
            <div className="p-2 bg-primary-100 dark:bg-primary-900/30 rounded-lg">
              <Puzzle className="w-5 h-5 text-primary-600 dark:text-primary-400" />
            </div>
            <span>{editingModule ? t('integration.modules.editor.editTitle') : t('integration.modules.newModule')}</span>
          </div>
        }
        onClose={() => setShowEditor(false)}
        size="lg"
        footer={
          <>
            <button
              onClick={() => setShowEditor(false)}
              className="px-5 py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition-all"
            >
              {t('common.cancel')}
            </button>
            <button
              onClick={handleSave}
              disabled={saving}
              className="px-5 py-2.5 text-sm font-medium text-white bg-primary-600 rounded-xl hover:bg-primary-700 disabled:opacity-50 transition-all shadow-sm hover:shadow-md active:scale-[0.98]"
            >
              {saving ? t('integration.common.saving') : t('common.save')}
            </button>
          </>
        }
      >
        <div className="space-y-5">
          {formError && (
            <div className="p-4 text-sm text-red-700 bg-red-50 dark:bg-red-900/20 rounded-xl border border-red-200 dark:border-red-800/50 flex items-start gap-3">
              <XCircle className="w-5 h-5 text-red-500 flex-shrink-0 mt-0.5" />
              <span>{formError}</span>
            </div>
          )}

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-5">
            <div>
              <label className={labelClass}>{t('integration.modules.editor.name')} <span className="text-red-500">*</span></label>
              <input
                type="text"
                value={formData.name}
                onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                disabled={!!editingModule}
                className={`${inputClass} disabled:bg-gray-50 dark:disabled:bg-slate-700 disabled:text-gray-500 disabled:cursor-not-allowed`}
                placeholder={t('integration.modules.editor.namePlaceholder')}
              />
            </div>
            <div>
              <label className={labelClass}>{t('integration.modules.editor.title')} <span className="text-red-500">*</span></label>
              <input
                type="text"
                value={formData.title}
                onChange={(e) => setFormData({ ...formData, title: e.target.value })}
                className={inputClass}
                placeholder={t('integration.modules.editor.titlePlaceholder')}
              />
            </div>
          </div>

          <div>
            <label className={labelClass}>{t('integration.modules.editor.description')}</label>
            <textarea
              value={formData.description}
              onChange={(e) => setFormData({ ...formData, description: e.target.value })}
              rows={3}
              className={`${inputClass} resize-none`}
              placeholder={t('integration.modules.editor.descriptionPlaceholder')}
            />
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-3 gap-5">
            <div>
              <label className={labelClass}>{t('integration.modules.editor.version')}</label>
              <input
                type="text"
                value={formData.version}
                onChange={(e) => setFormData({ ...formData, version: e.target.value })}
                className={inputClass}
                placeholder="1.0.0"
              />
            </div>
            <div>
              <label className={labelClass}>{t('integration.modules.editor.author')}</label>
              <input
                type="text"
                value={formData.author}
                onChange={(e) => setFormData({ ...formData, author: e.target.value })}
                className={inputClass}
                placeholder={t('integration.modules.editor.authorPlaceholder')}
              />
            </div>
            <div>
              <label className={labelClass}>{t('integration.modules.editor.category')}</label>
              <select
                value={formData.category}
                onChange={(e) => setFormData({ ...formData, category: e.target.value })}
                className={inputClass}
              >
                {categoryOptions.map((o) => (
                  <option key={o.value} value={o.value}>{t(o.labelKey)}</option>
                ))}
              </select>
            </div>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-5">
            <div>
              <label className={labelClass}>{t('integration.modules.editor.icon')}</label>
              <input
                type="text"
                value={formData.icon}
                onChange={(e) => setFormData({ ...formData, icon: e.target.value })}
                className={inputClass}
                placeholder={t('integration.modules.editor.iconPlaceholder')}
              />
            </div>
            <div>
              <label className={labelClass}>{t('integration.modules.editor.sortOrder')}</label>
              <input
                type="number"
                value={formData.sortOrder}
                onChange={(e) => setFormData({ ...formData, sortOrder: Number(e.target.value) })}
                className={inputClass}
              />
            </div>
          </div>
        </div>
      </Modal>

      <ConfirmDialog
        open={!!deleteName}
        title={t('integration.modules.confirmDeleteTitle')}
        message={t('integration.modules.confirmDeleteMessage')}
        onCancel={() => setDeleteName(null)}
        onConfirm={executeDelete}
        variant="danger"
      />

      {toast && (
        <div className="fixed top-4 right-4 z-50">
          <div
            className={`px-4 py-3 rounded-xl shadow-lg text-sm flex items-center gap-2 ${
              toast.type === 'success'
                ? 'bg-green-500 text-white'
                : 'bg-red-500 text-white'
            }`}
          >
            {toast.type === 'success' ? <CheckCircle2 className="w-4 h-4" /> : <XCircle className="w-4 h-4" />}
            {toast.text}
          </div>
        </div>
      )}
    </div>
  );
}
