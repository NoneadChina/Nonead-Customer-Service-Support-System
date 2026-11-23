import { useState, useEffect, useRef, useCallback } from 'react';
import { customFieldApi } from '../api';
import { useTimedToast } from '../hooks/useTimedToast';
import { genId } from '../utils/id';
import { getSourceTypeLabelByCode } from '../utils/format';
import { useI18n } from '../context/I18nContext';
import { tBound } from '../i18n/locale';
import type { TranslationKey } from '../locales/zh-Hans';
import type {
  CustomFieldDto,
  CustomFieldOverviewResponse,
  CustomFieldQuery,
  CreateCustomFieldRequest,
  CustomFieldChannelDto,
  PagedResponse,
} from '../api/types';
import Modal from '../components/Modal';
import TrashDialogs from '../components/TrashDialogs';
import {
  Layers,
  Plus,
  Search,
  Edit2,
  Trash2,
  CheckCircle2,
  XCircle,
  ToggleLeft,
  ToggleRight,
  Type,
  AlignLeft,
  ChevronDown,
  Calendar,
  CheckSquare,
  Hash,
  Users,
  MessageSquare,
  GripVertical,
  X,
  ChevronLeft,
  ChevronRight,
  Settings,
  FileText,
  AlertTriangle,
  Globe,
} from 'lucide-react';

/**
 * 字段类型选项。**后端不校验 type**（`CustomFieldsController` 只透传字符串），
 * 故这里的 6 个值就是全部可达值（开发库实测：text / textarea / select / date / checkbox / number）。
 * 值只存**稳定 code**，文案走 `labelKey` 字典——字段名刻意命名成 `labelKey`，
 * 这样任何忘记包 `t()` 的使用点都会以 TS2339 暴露。
 */
const FIELD_TYPE_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: 'text', labelKey: 'customers.customFields.typeText' },
  { value: 'textarea', labelKey: 'customers.customFields.typeTextarea' },
  { value: 'select', labelKey: 'customers.customFields.typeSelect' },
  { value: 'date', labelKey: 'customers.customFields.typeDate' },
  { value: 'checkbox', labelKey: 'customers.customFields.typeCheckbox' },
  { value: 'number', labelKey: 'customers.customFields.typeNumber' },
];

/** 关联对象选项。后端 `CustomField.EntityType` 的取值集合即这两个 code（无枚举类，按字符串存取）。 */
const ENTITY_TYPE_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: 'conversation', labelKey: 'customers.customFields.entityConversation' },
  { value: 'customer', labelKey: 'customers.customFields.entityCustomer' },
];

const STATUS_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: '', labelKey: 'customers.customFields.statusAll' },
  { value: 'true', labelKey: 'customers.customFields.statusEnabled' },
  { value: 'false', labelKey: 'customers.customFields.statusDisabled' },
];

const TYPE_BADGE: Record<string, string> = {
  text: 'bg-sky-100 text-sky-700',
  textarea: 'bg-indigo-100 text-indigo-700',
  select: 'bg-violet-100 text-violet-700',
  date: 'bg-amber-100 text-amber-700',
  checkbox: 'bg-emerald-100 text-emerald-700',
  number: 'bg-rose-100 text-rose-700',
};

const ENTITY_TYPE_BADGE: Record<string, string> = {
  conversation: 'bg-blue-100 text-blue-700',
  customer: 'bg-purple-100 text-purple-700',
};

/**
 * 对象类型 code → 文案 key。写成 `TranslationKey | undefined` 而不是 `TranslationKey`：
 * 本仓库未开 `noUncheckedIndexedAccess`，后者是**类型说谎**（后端新增实体类型时会静默返回 undefined）。
 */
const ENTITY_TYPE_LABEL: Record<string, TranslationKey | undefined> = {
  conversation: 'customers.customFields.entityConversation',
  customer: 'customers.customFields.entityCustomer',
};

/** 模块级纯函数（非组件）→ 用 `tBound` 取当前绑定语言；未知类型原样返回，便于暴露后端新类型 */
function getFieldTypeLabel(type: string): string {
  const opt = FIELD_TYPE_OPTIONS.find((o) => o.value === type);
  return opt ? tBound(opt.labelKey) : type;
}

export default function CustomFields() {
  const { t } = useI18n();
  const [overview, setOverview] = useState<CustomFieldOverviewResponse | null>(null);
  const [channels, setChannels] = useState<CustomFieldChannelDto[]>([]);

  const [query, setQuery] = useState<CustomFieldQuery>({ page: 1, pageSize: 20 });
  const [list, setList] = useState<PagedResponse<CustomFieldDto> | null>(null);
  const [trashList, setTrashList] = useState<{ data: CustomFieldDto[]; totalItems: number; totalPages: number; currentPage?: number } | null>(null);
  const [loading, setLoading] = useState(false);

  const [editing, setEditing] = useState<CustomFieldDto | null>(null);
  const [showEditor, setShowEditor] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<CustomFieldDto | null>(null);

  const [activeTab, setActiveTab] = useState<'list' | 'trash'>('list');
  const [restoreTarget, setRestoreTarget] = useState<CustomFieldDto | null>(null);
  const [hardDeleteTarget, setHardDeleteTarget] = useState<CustomFieldDto | null>(null);
  const [clearTrashOpen, setClearTrashOpen] = useState(false);

  const { message: toast, show: showToast } = useTimedToast();

  // 与 ApiKeys 一致：服务端搜索 650ms 输入防抖 + Enter 即时触发（缓冲 state，避免每次按键发请求）
  const keywordTimerRef = useRef<number | null>(null);
  const [keywordInput, setKeywordInput] = useState('');
  const applyKeyword = useCallback((value: string) => {
    setQuery((q) => ({ ...q, keyword: value, page: 1 }));
  }, []);
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
  // query.keyword 被其他入口（如 tab 切换 handleFilterChange('keyword','')）改动时，同步输入框缓冲
  useEffect(() => {
    setKeywordInput(query.keyword ?? '');
  }, [query.keyword]);
  // 卸载时清理挂起的防抖定时器
  useEffect(() => {
    return () => {
      if (keywordTimerRef.current !== null) window.clearTimeout(keywordTimerRef.current);
    };
  }, []);

  useEffect(() => {
    customFieldApi.overview().then(setOverview).catch((err: unknown) => console.error('加载自定义字段概览失败:', err));
    customFieldApi.getChannels().then(setChannels).catch(() => {});
    customFieldApi.getTrash().then(setTrashList).catch(() => {});
  }, []);

  useEffect(() => {
    setLoading(true);
    let cancelled = false;
    customFieldApi
      .list(query)
      .then((data) => { if (!cancelled) setList(data); })
      .catch((err: unknown) => console.error('加载自定义字段列表失败:', err))
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [query, overview]);

  const refreshTrash = () => {
    customFieldApi.getTrash().then(setTrashList).catch(() => {});
  };

  const refreshOverview = () => {
    customFieldApi.overview().then(setOverview).catch((err: unknown) => console.error('加载自定义字段概览失败:', err));
  };

  const handleFilterChange = <K extends keyof CustomFieldQuery>(key: K, value: CustomFieldQuery[K]) => {
    setQuery((q) => ({ ...q, [key]: value, page: 1 }));
  };

  const handlePageChange = (page: number) => {
    setQuery((q) => ({ ...q, page }));
  };

  const handleToggleActive = async (f: CustomFieldDto) => {
    try {
      await customFieldApi.toggleActive(f.id);
      showToast({ type: 'success', text: f.isActive
        ? t('customers.customFields.toggledDisabled', { name: f.name })
        : t('customers.customFields.toggledEnabled', { name: f.name }) });
      refreshOverview();
    } catch (_e: unknown) {
      showToast({ type: 'error', text: t('customers.common.operationFailed') });
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    try {
      await customFieldApi.remove(deleteTarget.id);
      showToast({ type: 'success', text: t('customers.customFields.movedToTrash') });
      setDeleteTarget(null);
      refreshOverview();
      refreshTrash();
    } catch (_e: unknown) {
      showToast({ type: 'error', text: t('customers.common.deleteFailed') });
    }
  };

  const handleRestore = async () => {
    if (!restoreTarget) return;
    try {
      await customFieldApi.restore(restoreTarget.id);
      showToast({ type: 'success', text: t('customers.customFields.restored') });
      setRestoreTarget(null);
      refreshOverview();
      refreshTrash();
    } catch (_e: unknown) {
      showToast({ type: 'error', text: t('customers.common.restoreFailed') });
    }
  };

  const handleHardDelete = async () => {
    if (!hardDeleteTarget) return;
    try {
      await customFieldApi.hardDelete(hardDeleteTarget.id);
      showToast({ type: 'success', text: t('customers.customFields.hardDeleted') });
      setHardDeleteTarget(null);
      refreshTrash();
    } catch (_e: unknown) {
      showToast({ type: 'error', text: t('customers.common.hardDeleteFailed') });
    }
  };

  const handleClearTrash = async () => {
    try {
      await customFieldApi.clearTrash();
      showToast({ type: 'success', text: t('customers.customFields.trashCleared') });
      setClearTrashOpen(false);
      refreshTrash();
    } catch (_e: unknown) {
      showToast({ type: 'error', text: t('customers.common.clearFailed') });
    }
  };

  const handleSave = async (data: CreateCustomFieldRequest, id?: number) => {
    try {
      if (id) {
        await customFieldApi.update(id, { ...data, id });
        showToast({ type: 'success', text: t('customers.customFields.updated') });
      } else {
        await customFieldApi.create(data);
        showToast({ type: 'success', text: t('customers.customFields.created') });
      }
      setShowEditor(false);
      setEditing(null);
      refreshOverview();
    } catch (e: unknown) {
      showToast({ type: 'error', text: t('customers.customFields.saveFailed') });
      throw e;
    }
  };

  const currentList = activeTab === 'list' ? list : trashList;
  const data = (currentList?.data ?? []) as CustomFieldDto[];
  const total = currentList?.totalItems ?? 0;
  const totalPages = currentList?.totalPages ?? 0;
  const currentPage = currentList?.currentPage ?? 1;
  const trashCount = trashList?.data?.length ?? 0;

  const getFieldTypeIcon = (type: string) => {
    switch (type) {
      case 'text': return <Type className="w-3.5 h-3.5" />;
      case 'textarea': return <AlignLeft className="w-3.5 h-3.5" />;
      case 'select': return <ChevronDown className="w-3.5 h-3.5" />;
      case 'date': return <Calendar className="w-3.5 h-3.5" />;
      case 'checkbox': return <CheckSquare className="w-3.5 h-3.5" />;
      case 'number': return <Hash className="w-3.5 h-3.5" />;
      default: return <Type className="w-3.5 h-3.5" />;
    }
  };

  /** 对象类型 code → 文案（未知 code 原样返回，便于暴露后端新增实体类型） */
  const getEntityTypeLabel = (entityType: string) => {
    const key = ENTITY_TYPE_LABEL[entityType];
    return key ? t(key) : entityType;
  };

  // 概览卡片。文案只存 key（字段名刻意叫 `labelKey`）：忘包 t() 的使用点会以 TS2339 暴露
  const statCards = [
    { labelKey: 'customers.customFields.statTotal', value: overview?.totalFields ?? 0, color: 'text-primary-600 dark:text-primary-400', bg: 'bg-primary-50 dark:bg-primary-900/20', icon: Layers },
    { labelKey: 'customers.customFields.statActive', value: overview?.activeFields ?? 0, color: 'text-green-600 dark:text-green-400', bg: 'bg-green-50 dark:bg-green-900/20', icon: CheckCircle2 },
    { labelKey: 'customers.customFields.statConversation', value: overview?.conversationFields ?? 0, color: 'text-blue-600 dark:text-blue-400', bg: 'bg-blue-50 dark:bg-blue-900/20', icon: MessageSquare },
    { labelKey: 'customers.customFields.statCustomer', value: overview?.customerFields ?? 0, color: 'text-purple-600 dark:text-purple-400', bg: 'bg-purple-50 dark:bg-purple-900/20', icon: Users },
  ] satisfies { labelKey: TranslationKey; value: number; color: string; bg: string; icon: typeof Layers }[];

  return (
    <div className="space-y-5">
      <div className="relative overflow-hidden bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-800 dark:to-slate-900 rounded-2xl border border-primary-100 dark:border-slate-700/50 p-4 sm:p-6">
        <div className="absolute top-0 right-0 w-64 h-64 bg-primary-200/30 dark:bg-primary-500/10 rounded-full -translate-y-1/2 translate-x-1/2 blur-3xl" />
        <div className="absolute bottom-0 left-0 w-48 h-48 bg-indigo-200/30 dark:bg-indigo-500/10 rounded-full translate-y-1/2 -translate-x-1/2 blur-3xl" />
        <div className="relative flex items-start justify-between gap-4 flex-wrap">
          <div className="flex items-start gap-3 sm:gap-4 min-w-0 flex-1">
            <div className="p-2.5 sm:p-3 bg-white dark:bg-slate-800 rounded-xl shadow-md flex-shrink-0">
              <Layers className="w-6 h-6 sm:w-7 sm:h-7 text-primary-600 dark:text-primary-400" />
            </div>
            <div className="min-w-0">
              <h1 className="text-lg sm:text-xl font-bold text-gray-900 dark:text-slate-100">{t('customers.customFields.title')}</h1>
              <p className="text-xs sm:text-sm text-gray-600 dark:text-slate-400 mt-1 max-w-xl">
                {t('customers.customFields.subtitle')}
              </p>
            </div>
          </div>
          {activeTab === 'list' && (
            <button
              onClick={() => {
                setEditing(null);
                setShowEditor(true);
              }}
              className="inline-flex items-center gap-1.5 sm:gap-2 px-4 sm:px-5 py-2 sm:py-2.5 text-sm font-medium text-white bg-primary-600 rounded-xl hover:bg-primary-700 transition-all shadow-md hover:shadow-lg active:scale-[0.98] flex-shrink-0"
            >
              <Plus className="w-4 h-4 sm:w-4.5 sm:h-4.5" />
              <span className="hidden sm:inline">{t('customers.customFields.newField')}</span>
              <span className="sm:hidden">{t('customers.customFields.newShort')}</span>
            </button>
          )}
        </div>
      </div>

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        {statCards.map((s) => (
          <div key={s.labelKey} className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 p-5 shadow-sm hover:shadow-md transition-all group">
            <div className="flex items-center justify-between">
              <span className="text-sm text-gray-500 dark:text-slate-400">{t(s.labelKey)}</span>
              <div className={`p-2 rounded-lg ${s.bg} transition-transform group-hover:scale-110`}>
                <s.icon className={`w-5 h-5 ${s.color}`} />
              </div>
            </div>
            <div className={`text-3xl font-bold mt-3 ${s.color}`}>{s.value}</div>
          </div>
        ))}
      </div>

      <div className="flex items-center gap-1 p-1 bg-gray-100 dark:bg-slate-800 rounded-xl w-fit max-w-full overflow-x-auto">
        <button
          onClick={() => { setActiveTab('list'); handleFilterChange('keyword', ''); }}
          className={`px-3 sm:px-4 py-1.5 text-sm rounded-lg transition flex items-center gap-1.5 whitespace-nowrap ${
            activeTab === 'list'
              ? 'bg-white dark:bg-slate-700 text-primary-600 dark:text-primary-400 shadow-sm font-medium'
              : 'text-gray-600 dark:text-slate-400 hover:text-gray-900 dark:hover:text-slate-200'
          }`}
        >
          <Layers className="w-4 h-4" />
          {t('customers.customFields.tabAll')}
          <span className="px-1.5 py-0.5 text-xs rounded-full bg-gray-200 dark:bg-slate-600 text-gray-600 dark:text-slate-300">
            {list?.data?.length ?? 0}
          </span>
        </button>
        <button
          onClick={() => { setActiveTab('trash'); handleFilterChange('keyword', ''); }}
          className={`px-3 sm:px-4 py-1.5 text-sm rounded-lg transition flex items-center gap-1.5 whitespace-nowrap ${
            activeTab === 'trash'
              ? 'bg-white dark:bg-slate-700 text-primary-600 dark:text-primary-400 shadow-sm font-medium'
              : 'text-gray-600 dark:text-slate-400 hover:text-gray-900 dark:hover:text-slate-200'
          }`}
        >
          <Trash2 className="w-4 h-4" />
          {t('customers.common.trash')}
          {trashCount > 0 && (
            <span className="px-1.5 py-0.5 text-xs rounded-full bg-red-100 dark:bg-red-900/30 text-red-600 dark:text-red-400">
              {trashCount}
            </span>
          )}
        </button>
      </div>

      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 shadow-sm overflow-hidden">
        <div className="px-3 sm:px-5 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gray-50/50 dark:bg-slate-900/30">
          <div className="flex items-center justify-between gap-2 sm:gap-3 flex-wrap">
            <div className="flex items-center gap-2 sm:gap-3 flex-wrap flex-1">
              <div className="flex-1 min-w-[160px] sm:min-w-[200px] w-full sm:w-auto">
                <div className="relative">
                  <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400 dark:text-slate-500" />
                  <input
                    type="text"
                    placeholder={t('customers.customFields.searchPlaceholder')}
                    value={keywordInput}
                    onChange={(e) => handleKeywordInput(e.target.value)}
                    onKeyDown={(e) => { if (e.key === 'Enter') triggerKeywordSearch(); }}
                    className="bg-white dark:bg-slate-900 w-full pl-10 pr-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition-all"
                  />
                </div>
              </div>
              {activeTab === 'list' && (
                <>
                  <select
                    value={query.entityType ?? ''}
                    onChange={(e) => handleFilterChange('entityType', e.target.value || null)}
                    className="bg-white dark:bg-slate-900 px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition-all"
                  >
                    <option value="">{t('customers.customFields.allEntities')}</option>
                    {ENTITY_TYPE_OPTIONS.map((o) => (
                      <option key={o.value} value={o.value}>
                        {t(o.labelKey)}
                      </option>
                    ))}
                  </select>
                  <select
                    value={query.isActive === null || query.isActive === undefined ? '' : String(query.isActive)}
                    onChange={(e) =>
                      handleFilterChange('isActive', e.target.value === '' ? null : e.target.value === 'true')
                    }
                    className="bg-white dark:bg-slate-900 px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition-all"
                  >
                    {STATUS_OPTIONS.map((o) => (
                      <option key={o.value} value={o.value}>
                        {t(o.labelKey)}
                      </option>
                    ))}
                  </select>
                </>
              )}
            </div>
            {activeTab === 'trash' && trashCount > 0 && (
              <button
                onClick={() => setClearTrashOpen(true)}
                className="inline-flex items-center gap-1.5 px-3 sm:px-4 py-2 text-sm bg-red-50 hover:bg-red-100 dark:bg-red-900/20 dark:hover:bg-red-900/30 text-red-600 dark:text-red-400 rounded-lg transition"
              >
                <Trash2 className="w-4 h-4" />
                <span className="hidden sm:inline">{t('customers.common.clearTrash')}</span>
                <span className="sm:hidden">{t('customers.customFields.clearShort')}</span>
              </button>
            )}
          </div>
        </div>

        {/* PC 端表格视图 */}
        <div className="hidden md:block overflow-x-auto">
          <table className="w-full">
            <thead className="bg-gray-50 dark:bg-slate-900/50 border-b border-gray-200 dark:border-slate-700">
              <tr>
                <th className="px-5 py-3.5 text-left text-xs font-semibold text-gray-600 dark:text-slate-400 uppercase tracking-wider">{t('customers.customFields.fieldName')}</th>
                <th className="px-5 py-3.5 text-left text-xs font-semibold text-gray-600 dark:text-slate-400 uppercase tracking-wider">{t('customers.customFields.colType')}</th>
                <th className="px-5 py-3.5 text-left text-xs font-semibold text-gray-600 dark:text-slate-400 uppercase tracking-wider">{t('customers.customFields.colEntity')}</th>
                <th className="px-5 py-3.5 text-left text-xs font-semibold text-gray-600 dark:text-slate-400 uppercase tracking-wider">{t('customers.customFields.channelsLabel')}</th>
                <th className="px-5 py-3.5 text-center text-xs font-semibold text-gray-600 dark:text-slate-400 uppercase tracking-wider">{t('customers.customFields.colRequired')}</th>
                <th className="px-5 py-3.5 text-center text-xs font-semibold text-gray-600 dark:text-slate-400 uppercase tracking-wider">{t('customers.customFields.colSortOrder')}</th>
                <th className="px-5 py-3.5 text-center text-xs font-semibold text-gray-600 dark:text-slate-400 uppercase tracking-wider">{t('customers.customFields.colStatus')}</th>
                <th className="px-5 py-3.5 text-right text-xs font-semibold text-gray-600 dark:text-slate-400 uppercase tracking-wider">{t('customers.common.actions')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 dark:divide-slate-700/50">
              {loading ? (
                <tr>
                  <td colSpan={8} className="px-5 py-16 text-center">
                    <div className="animate-spin h-8 w-8 border-4 border-primary-500 border-t-transparent rounded-full mx-auto mb-3" />
                    <p className="text-sm text-gray-400 dark:text-slate-500">{t('customers.common.loading')}</p>
                  </td>
                </tr>
              ) : data.length === 0 ? (
                <tr>
                  <td colSpan={8} className="px-5 py-16 text-center">
                    <Layers className="w-12 h-12 mx-auto mb-3 text-gray-300 dark:text-slate-600" />
                    <p className="text-sm text-gray-400 dark:text-slate-500">
                      {t('customers.customFields.emptyList')}
                    </p>
                  </td>
                </tr>
              ) : (
                data.map((f) => (
                  <tr key={f.id} className="hover:bg-gray-50/70 dark:hover:bg-slate-700/30 transition-colors">
                    <td className="px-5 py-4">
                      <div className="flex items-center gap-3">
                        <div className={`p-2 rounded-lg ${TYPE_BADGE[f.type] || 'bg-gray-100 dark:bg-slate-700'} bg-opacity-20`}>
                          {getFieldTypeIcon(f.type)}
                        </div>
                        <div>
                          <div className="text-sm font-semibold text-gray-900 dark:text-slate-100">{f.name}</div>
                          {f.helpText && (
                            <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-1 max-w-xs">{f.helpText}</div>
                          )}
                        </div>
                      </div>
                    </td>
                    <td className="px-5 py-4">
                      <span
                        className={`inline-flex items-center gap-1.5 text-xs font-medium px-2.5 py-1 rounded-full ${
                          TYPE_BADGE[f.type] || 'bg-gray-100 text-gray-700 dark:bg-slate-700 dark:text-slate-300'
                        }`}
                      >
                        {getFieldTypeIcon(f.type)}
                        {getFieldTypeLabel(f.type)}
                      </span>
                    </td>
                    <td className="px-5 py-4">
                      <span
                        className={`inline-flex items-center gap-1.5 text-xs font-medium px-2.5 py-1 rounded-full ${
                          ENTITY_TYPE_BADGE[f.entityType] || 'bg-gray-100 text-gray-700 dark:bg-slate-700 dark:text-slate-300'
                        }`}
                      >
                        {f.entityType === 'conversation' ? <MessageSquare className="w-3.5 h-3.5" /> : <Users className="w-3.5 h-3.5" />}
                        {getEntityTypeLabel(f.entityType)}
                      </span>
                    </td>
                    <td className="px-5 py-4 text-sm text-gray-700 dark:text-slate-300">
                      {f.entityType === 'customer' ? (
                        <span className="inline-flex items-center gap-1.5 text-gray-400 dark:text-slate-500">
                          <Globe className="w-3.5 h-3.5" />
                          {t('customers.customFields.globalCustomerField')}
                        </span>
                      ) : !f.sourceTypes || f.sourceTypes.length === 0 ? (
                        <span className="inline-flex items-center gap-1.5 text-gray-400 dark:text-slate-500">
                          <Globe className="w-3.5 h-3.5" />
                          {t('customers.common.global')}
                        </span>
                      ) : (
                        <div className="flex flex-wrap items-center gap-1">
                          {f.sourceTypes.map((st) => {
                            const isDisabled = f.disabledChannels?.includes(st);
                            return (
                              <span
                                key={st}
                                title={isDisabled ? t('customers.customFields.channelDisabledTitle', { channel: getSourceTypeLabelByCode(st) }) : undefined}
                                className={`inline-flex items-center gap-1 text-xs font-medium px-2 py-0.5 rounded-full ${
                                  isDisabled
                                    ? 'bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400 border border-amber-200 dark:border-amber-800/50'
                                    : 'bg-primary-50 text-primary-700 dark:bg-primary-900/30 dark:text-primary-400 border border-primary-100 dark:border-primary-800/50'
                                }`}
                              >
                                {isDisabled && <AlertTriangle className="w-3 h-3" />}
                                {getSourceTypeLabelByCode(st)}
                              </span>
                            );
                          })}
                          {f.disabledChannels && f.disabledChannels.length > 0 && (
                            <span className="inline-flex items-center gap-1 text-xs text-amber-600 dark:text-amber-400 ml-1">
                              <AlertTriangle className="w-3 h-3" />
                              {f.disabledChannels.length}{t('customers.customFields.channelsDisabledSuffix')}
                            </span>
                          )}
                        </div>
                      )}
                    </td>
                    <td className="px-5 py-4 text-center">
                      {f.isRequired ? (
                        <span className="inline-flex items-center gap-1 text-xs font-medium px-2 py-0.5 rounded-full bg-red-50 text-red-600 dark:bg-red-900/20 dark:text-red-400">
                          {t('customers.customFields.colRequired')}
                        </span>
                      ) : (
                        <span className="text-gray-300 dark:text-slate-600">—</span>
                      )}
                    </td>
                    <td className="px-5 py-4 text-center">
                      <span className="inline-flex items-center gap-1 text-sm text-gray-600 dark:text-slate-300 font-mono">
                        <GripVertical className="w-3.5 h-3.5 text-gray-400" />
                        {f.sortOrder}
                      </span>
                    </td>
                    <td className="px-5 py-4 text-center">
                      {activeTab === 'list' ? (
                        <button
                          onClick={() => handleToggleActive(f)}
                          className="relative inline-flex items-center"
                          title={f.isActive ? t('customers.customFields.clickDisable') : t('customers.customFields.clickEnable')}
                        >
                          {f.isActive ? (
                            <ToggleRight className="w-11 h-6 text-green-500" />
                          ) : (
                            <ToggleLeft className="w-11 h-6 text-gray-400 dark:text-slate-500" />
                          )}
                        </button>
                      ) : (
                        <span className="text-sm text-gray-400 dark:text-slate-500">{t('customers.customFields.softDeleted')}</span>
                      )}
                    </td>
                    <td className="px-5 py-4 text-right">
                      <div className="flex items-center justify-end gap-1">
                        {activeTab === 'list' ? (
                          <>
                            <button
                              onClick={() => {
                                setEditing(f);
                                setShowEditor(true);
                              }}
                              className="p-2 text-gray-500 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/20 rounded-lg transition-colors"
                              title={t('customers.common.edit')}
                            >
                              <Edit2 className="w-4 h-4" />
                            </button>
                            <button
                              onClick={() => setDeleteTarget(f)}
                              className="p-2 text-gray-500 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg transition-colors"
                              title={t('customers.common.delete')}
                            >
                              <Trash2 className="w-4 h-4" />
                            </button>
                          </>
                        ) : (
                          <>
                            <button
                              onClick={() => setRestoreTarget(f)}
                              className="p-2 text-gray-500 hover:text-green-600 hover:bg-green-50 dark:hover:bg-green-900/20 rounded-lg transition-colors"
                              title={t('customers.common.restore')}
                            >
                              <CheckCircle2 className="w-4 h-4" />
                            </button>
                            <button
                              onClick={() => setHardDeleteTarget(f)}
                              className="p-2 text-gray-500 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg transition-colors"
                              title={t('customers.common.hardDelete')}
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
          {loading ? (
            <div className="py-12 text-center">
              <div className="animate-spin h-8 w-8 border-4 border-primary-500 border-t-transparent rounded-full mx-auto mb-3" />
              <p className="text-sm text-gray-400 dark:text-slate-500">{t('customers.common.loading')}</p>
            </div>
          ) : data.length === 0 ? (
            <div className="py-12 text-center">
              <Layers className="w-10 h-10 mx-auto mb-2 text-gray-300 dark:text-slate-600" />
              <p className="text-sm text-gray-400 dark:text-slate-500">
                {activeTab === 'list' ? t('customers.customFields.emptyNoFields') : t('customers.common.trashEmpty')}
              </p>
            </div>
          ) : (
            data.map((f) => (
              <div key={f.id} className="p-4 hover:bg-gray-50/70 dark:hover:bg-slate-700/30 space-y-3">
                <div className="flex items-start justify-between gap-2">
                  <div className="flex items-center gap-2.5 min-w-0 flex-1">
                    <div className={`shrink-0 w-9 h-9 rounded-xl flex items-center justify-center ring-1 ${TYPE_BADGE[f.type] || 'bg-gray-100 text-gray-700 dark:bg-slate-700 dark:text-slate-300'}`}>
                      {getFieldTypeIcon(f.type)}
                    </div>
                    <div className="min-w-0">
                      <div className={`text-sm font-bold text-gray-900 dark:text-slate-100 truncate ${activeTab === 'trash' ? 'line-through decoration-1 decoration-rose-300/60 dark:decoration-rose-800/50' : ''}`}>
                        {f.name}
                      </div>
                      <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5 flex items-center gap-1.5">
                        <span className={`inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded-full ${ENTITY_TYPE_BADGE[f.entityType] || 'bg-gray-100 text-gray-700 dark:bg-slate-700 dark:text-slate-300'}`}>
                          {f.entityType === 'conversation' ? <MessageSquare className="w-3 h-3" /> : <Users className="w-3 h-3" />}
                          {getEntityTypeLabel(f.entityType)}
                        </span>
                        <span className="opacity-40">·</span>
                        <span className="inline-flex items-center gap-0.5">
                          <GripVertical className="w-3 h-3" />
                          {f.sortOrder}
                        </span>
                        {f.isRequired && (
                          <>
                            <span className="opacity-40">·</span>
                            <span className="text-red-600 dark:text-red-400">{t('customers.customFields.colRequired')}</span>
                          </>
                        )}
                      </div>
                    </div>
                  </div>
                  {activeTab === 'list' ? (
                    <button
                      onClick={() => handleToggleActive(f)}
                      className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors flex-shrink-0 ${
                        f.isActive ? 'bg-green-500' : 'bg-gray-300 dark:bg-slate-600'
                      }`}
                      title={f.isActive ? t('customers.customFields.clickDisable') : t('customers.customFields.clickEnable')}
                    >
                      <span
                        className={`inline-block h-4 w-4 transform rounded-full bg-white transition ${
                          f.isActive ? 'translate-x-6' : 'translate-x-1'
                        }`}
                      />
                    </button>
                  ) : (
                    <span className="text-[11px] text-gray-400 dark:text-slate-500 flex-shrink-0">{t('customers.customFields.softDeleted')}</span>
                  )}
                </div>

                {f.helpText && (
                  <div className="text-xs text-gray-600 dark:text-slate-400 line-clamp-2">
                    {f.helpText}
                  </div>
                )}

                {/* 适用渠道 */}
                <div>
                  {f.entityType === 'customer' ? (
                    <span className="inline-flex items-center gap-1 text-[11px] text-gray-400 dark:text-slate-500">
                      <Globe className="w-3 h-3" />
                      {t('customers.customFields.globalCustomerField')}
                    </span>
                  ) : !f.sourceTypes || f.sourceTypes.length === 0 ? (
                    <span className="inline-flex items-center gap-1 text-[11px] text-gray-400 dark:text-slate-500">
                      <Globe className="w-3 h-3" />
                      {t('customers.common.global')}
                    </span>
                  ) : (
                    <div className="flex flex-wrap items-center gap-1">
                      {f.sourceTypes.map((st) => {
                        const isDisabled = f.disabledChannels?.includes(st);
                        return (
                          <span
                            key={st}
                            className={`inline-flex items-center gap-0.5 text-[11px] font-medium px-1.5 py-0.5 rounded-full ${
                              isDisabled
                                ? 'bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400'
                                : 'bg-primary-50 text-primary-700 dark:bg-primary-900/30 dark:text-primary-400'
                            }`}
                          >
                            {isDisabled && <AlertTriangle className="w-2.5 h-2.5" />}
                            {getSourceTypeLabelByCode(st)}
                          </span>
                        );
                      })}
                      {f.disabledChannels && f.disabledChannels.length > 0 && (
                        <span className="inline-flex items-center gap-0.5 text-[11px] text-amber-600 dark:text-amber-400">
                          <AlertTriangle className="w-3 h-3" />
                          {f.disabledChannels.length}{t('customers.customFields.channelsDisabledSuffix')}
                        </span>
                      )}
                    </div>
                  )}
                </div>

                <div className="flex items-center justify-end pt-1">
                  <div className="flex items-center gap-1">
                    {activeTab === 'list' ? (
                      <>
                        <button
                          onClick={() => {
                            setEditing(f);
                            setShowEditor(true);
                          }}
                          className="p-2 text-gray-500 dark:text-slate-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition"
                          title={t('customers.common.edit')}
                        >
                          <Edit2 className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => setDeleteTarget(f)}
                          className="p-2 text-gray-500 dark:text-slate-400 hover:text-rose-600 hover:bg-rose-50 dark:hover:bg-rose-900/30 rounded-xl transition"
                          title={t('customers.common.delete')}
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </>
                    ) : (
                      <>
                        <button
                          onClick={() => setRestoreTarget(f)}
                          className="p-2 text-gray-500 dark:text-slate-400 hover:text-emerald-600 hover:bg-emerald-50 dark:hover:bg-emerald-900/30 rounded-xl transition"
                          title={t('customers.common.restore')}
                        >
                          <CheckCircle2 className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => setHardDeleteTarget(f)}
                          className="p-2 text-gray-500 dark:text-slate-400 hover:text-rose-600 hover:bg-rose-50 dark:hover:bg-rose-900/30 rounded-xl transition"
                          title={t('customers.common.hardDelete')}
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </>
                    )}
                  </div>
                </div>
              </div>
            ))
          )}
        </div>

        {totalPages > 1 && (
          <div className="flex items-center justify-between px-4 sm:px-5 py-3 sm:py-3.5 border-t border-gray-100 dark:border-slate-700/50 bg-gray-50/30 dark:bg-slate-900/20">
            <div className="text-xs sm:text-sm text-gray-500 dark:text-slate-400">
              {t('customers.customFields.pageTotalPrefix')}<span className="font-medium text-gray-700 dark:text-slate-300">{total}</span>{t('customers.customFields.pageTotalMiddle', { count: total })}<span className="font-medium text-gray-700 dark:text-slate-300">{currentPage}</span>{t('customers.customFields.pageTotalSuffix', { total: totalPages })}
            </div>
            <div className="flex items-center gap-1.5">
              <button
                onClick={() => handlePageChange(currentPage - 1)}
                disabled={currentPage <= 1}
                className="inline-flex items-center gap-1 px-2.5 sm:px-3 py-1.5 text-sm font-medium border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                <ChevronLeft className="w-4 h-4" />
                <span className="hidden sm:inline">{t('customers.common.prevPage')}</span>
              </button>
              <button
                onClick={() => handlePageChange(currentPage + 1)}
                disabled={currentPage >= totalPages}
                className="inline-flex items-center gap-1 px-2.5 sm:px-3 py-1.5 text-sm font-medium border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                <span className="hidden sm:inline">{t('customers.common.nextPage')}</span>
                <ChevronRight className="w-4 h-4" />
              </button>
            </div>
          </div>
        )}
      </div>

      {showEditor && (
        <CustomFieldEditor
          editing={editing}
          channels={channels}
          onSave={handleSave}
          onCancel={() => {
            setShowEditor(false);
            setEditing(null);
          }}
        />
      )}

      <TrashDialogs<CustomFieldDto>
        itemLabel={t('customers.customFields.title')}
        deleteTarget={deleteTarget}
        restoreTarget={restoreTarget}
        hardDeleteTarget={hardDeleteTarget}
        clearTrashOpen={clearTrashOpen}
        onDelete={handleDelete}
        onRestore={handleRestore}
        onHardDelete={handleHardDelete}
        onClearTrash={handleClearTrash}
        onCancelDelete={() => setDeleteTarget(null)}
        onCancelRestore={() => setRestoreTarget(null)}
        onCancelHardDelete={() => setHardDeleteTarget(null)}
        onCancelClearTrash={() => setClearTrashOpen(false)}
        getItemName={(item) => item.name}
      />

      {toast && (
        <div className="fixed top-4 right-4 z-50">
          <div
            className={`px-4 py-2 rounded-lg shadow-lg text-white text-sm ${
              toast.type === 'success' ? 'bg-green-500' : 'bg-red-500'
            }`}
          >
            {toast.text}
          </div>
        </div>
      )}
    </div>
  );
}

interface CustomFieldEditorProps {
  editing: CustomFieldDto | null;
  channels: CustomFieldChannelDto[];
  onSave: (data: CreateCustomFieldRequest, id?: number) => Promise<void>;
  onCancel: () => void;
}

function CustomFieldEditor({ editing, channels, onSave, onCancel }: CustomFieldEditorProps) {
  const { t } = useI18n();
  const [name, setName] = useState(editing?.name ?? '');
  const [type, setType] = useState(editing?.type ?? 'text');
  const [entityType, setEntityType] = useState(editing?.entityType ?? 'conversation');
  const [sourceTypes, setSourceTypes] = useState<string[]>(editing?.sourceTypes ?? []);
  const [hideWhenChannelDisabled, setHideWhenChannelDisabled] = useState(
    editing?.hideWhenChannelDisabled ?? false,
  );
  const [isRequired, setIsRequired] = useState(editing?.isRequired ?? false);
  const [isActive, setIsActive] = useState(editing?.isActive ?? true);
  const [sortOrder, setSortOrder] = useState(editing?.sortOrder ?? 0);
  const [options, setOptions] = useState<string[]>(editing?.options ?? []);
  const [optionKeys, setOptionKeys] = useState<string[]>(() => (editing?.options ?? []).map(() => genId()));
  const [newOption, setNewOption] = useState('');
  const [defaultValue, setDefaultValue] = useState(editing?.defaultValue ?? '');
  const [placeholder, setPlaceholder] = useState(editing?.placeholder ?? '');
  const [helpText, setHelpText] = useState(editing?.helpText ?? '');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  /**
   * 合并渠道列表：
   * - 已启用渠道（来自 channels prop，过滤掉 "global" 占位项）→ 可勾选
   * - 编辑中的字段已绑定但当前已关闭的渠道（不在 channels 列表里）→ 仍展示，标记为 disabled，可取消勾选但不能勾选
   * 客户字段不绑定渠道（始终全局），调用方应不渲染本组件。
   *
   * ⚠ 渠道**显示名一律走前端字典**（`getSourceTypeLabelByCode`），**不要**用后端
   * `CustomFieldChannelDto.label`——那是 `CustomFieldService.KnownChannels` 里的中文标签，
   * 直出会让英文态渲染出中文（本波消除的"后端中文标签直出"渲染点之一）。
   */
  const availableChannels = channels.filter((c) => c.value !== 'global');
  const boundDisabledChannels = (editing?.sourceTypes ?? [])
    .filter((st) => !availableChannels.some((c) => c.value === st))
    .map((st) => ({
      value: st,
      label: getSourceTypeLabelByCode(st) || st,
      isEnabled: false,
    }));
  const channelOptions = [...availableChannels, ...boundDisabledChannels];

  const toggleChannel = (value: string) => {
    setSourceTypes((prev) =>
      prev.includes(value) ? prev.filter((v) => v !== value) : [...prev, value],
    );
  };

  // 切换 entityType 时重置渠道相关状态
  const handleEntityTypeChange = (next: string) => {
    setEntityType(next);
    if (next === 'customer') {
      setSourceTypes([]);
      setHideWhenChannelDisabled(false);
    }
  };

  const handleAddOption = () => {
    if (!newOption.trim()) return;
    setOptions((prev) => [...prev, newOption.trim()]);
    setOptionKeys((prev) => [...prev, genId()]);
    setNewOption('');
  };

  const handleRemoveOption = (index: number) => {
    setOptions((prev) => prev.filter((_, i) => i !== index));
    setOptionKeys((prev) => prev.filter((_, i) => i !== index));
  };

  const handleSubmit = async () => {
    if (!name.trim()) {
      setError(t('customers.customFields.nameRequired'));
      return;
    }
    if (type === 'select' && options.length === 0) {
      setError(t('customers.customFields.selectOptionsRequired'));
      return;
    }
    setSaving(true);
    setError('');
    try {
      // 客户字段强制全局：后端会再次兜底忽略，前端也清空避免歧义
      const finalSourceTypes = entityType === 'customer' ? [] : sourceTypes;
      const finalHide = entityType === 'customer' ? false : hideWhenChannelDisabled;
      await onSave(
        {
          name: name.trim(),
          type,
          entityType,
          mailboxId: null,
          sourceTypes: finalSourceTypes,
          hideWhenChannelDisabled: finalHide,
          isRequired,
          isActive,
          sortOrder,
          options,
          defaultValue: defaultValue || null,
          placeholder: placeholder || null,
          helpText: helpText || null,
        },
        editing?.id,
      );
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('customers.customFields.saveFailed'));
    } finally {
      setSaving(false);
    }
  };

  const showOptions = type === 'select';

  const inputClass = "bg-white dark:bg-slate-900 w-full px-3.5 py-2.5 text-sm border border-gray-300 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition-all";
  const labelClass = "block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5";

  return (
    <Modal
      open
      title={
        <div className="flex items-center gap-3">
          <div className="p-2 bg-primary-100 dark:bg-primary-900/30 rounded-lg">
            <Settings className="w-5 h-5 text-primary-600 dark:text-primary-400" />
          </div>
          <span>{editing ? t('customers.customFields.editTitle') : t('customers.customFields.newTitle')}</span>
        </div>
      }
      onClose={onCancel}
      size="lg"
      footer={
        <>
          <button
            onClick={onCancel}
            disabled={saving}
            className="px-5 py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition-all"
          >
            {t('customers.common.cancel')}
          </button>
          <button
            onClick={handleSubmit}
            disabled={saving}
            className="px-5 py-2.5 text-sm font-medium text-white bg-primary-600 rounded-xl hover:bg-primary-700 disabled:opacity-50 transition-all shadow-sm hover:shadow-md active:scale-[0.98]"
          >
            {saving ? t('customers.common.saving') : t('customers.common.save')}
          </button>
        </>
      }
    >
      <div className="space-y-5">
        {error && (
          <div className="p-4 text-sm text-red-700 bg-red-50 dark:bg-red-900/20 rounded-xl border border-red-200 dark:border-red-800/50 flex items-start gap-3">
            <XCircle className="w-5 h-5 text-red-500 flex-shrink-0 mt-0.5" />
            <span>{error}</span>
          </div>
        )}

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-5">
          <div>
            <label className={labelClass}>{t('customers.customFields.fieldName')} <span className="text-red-500">*</span></label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={t('customers.customFields.namePlaceholder')}
              className={inputClass}
            />
          </div>
          <div>
            <label className={labelClass}>{t('customers.customFields.fieldType')}</label>
            <select
              value={type}
              onChange={(e) => setType(e.target.value)}
              className={inputClass}
            >
              {FIELD_TYPE_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {t(o.labelKey)}
                </option>
              ))}
            </select>
          </div>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-5">
          <div>
            <label className={labelClass}>{t('customers.customFields.entityType')}</label>
            <select
              value={entityType}
              onChange={(e) => handleEntityTypeChange(e.target.value)}
              className={inputClass}
            >
              {ENTITY_TYPE_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {t(o.labelKey)}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className={labelClass}>
              {t('customers.customFields.channelsLabel')}
              {entityType === 'conversation' && sourceTypes.length === 0 && (
                <span className="ml-2 text-xs font-normal text-gray-400 dark:text-slate-500">{t('customers.customFields.channelsUnselectedHint')}</span>
              )}
            </label>
            {entityType === 'customer' ? (
              <div className={`px-3.5 py-2.5 text-sm rounded-xl border border-dashed border-gray-300 dark:border-slate-600 text-gray-500 dark:text-slate-400 bg-gray-50/50 dark:bg-slate-900/30`}>
                {t('customers.customFields.customerFieldsGlobal')}
              </div>
            ) : channelOptions.length === 0 ? (
              <div className={`px-3.5 py-2.5 text-sm rounded-xl border border-dashed border-gray-300 dark:border-slate-600 text-gray-500 dark:text-slate-400 bg-gray-50/50 dark:bg-slate-900/30`}>
                {t('customers.customFields.noChannelsHint')}
              </div>
            ) : (
              <>
                <div className="flex flex-wrap gap-2">
                  {channelOptions.map((c) => {
                    const checked = sourceTypes.includes(c.value);
                    const isDisabledChannel = !c.isEnabled;
                    return (
                      <button
                        key={c.value}
                        type="button"
                        onClick={() => toggleChannel(c.value)}
                        title={isDisabledChannel ? t('customers.customFields.channelDisabledCurrentTitle', { channel: getSourceTypeLabelByCode(c.value) }) : undefined}
                        className={`inline-flex items-center gap-1.5 px-3 py-1.5 text-sm rounded-lg border transition-all ${
                          checked
                            ? isDisabledChannel
                              ? 'bg-amber-500 text-white border-amber-500'
                              : 'bg-primary-600 text-white border-primary-600'
                            : isDisabledChannel
                              ? 'bg-amber-50 text-amber-700 border-amber-200 dark:bg-amber-900/20 dark:text-amber-400 dark:border-amber-800/50'
                              : 'bg-white dark:bg-slate-900 text-gray-700 dark:text-slate-300 border-gray-300 dark:border-slate-600 hover:border-primary-400'
                        }`}
                      >
                        {isDisabledChannel && <AlertTriangle className="w-3.5 h-3.5" />}
                        {getSourceTypeLabelByCode(c.value)}
                        {checked && <CheckCircle2 className="w-3.5 h-3.5" />}
                      </button>
                    );
                  })}
                </div>
                <p className="mt-1.5 text-xs text-gray-500 dark:text-slate-400">
                  {t('customers.customFields.channelsMultiHint')}
                  {boundDisabledChannels.length > 0 && (
                    <span className="ml-1 text-amber-600 dark:text-amber-400">
                      {t('customers.customFields.channelsDisabledHint')}
                    </span>
                  )}
                </p>
              </>
            )}
          </div>
        </div>

        {entityType === 'conversation' && sourceTypes.length > 0 && (
          <div className="p-4 rounded-xl border border-gray-200 dark:border-slate-700 bg-gray-50/50 dark:bg-slate-900/30">
            <label className="flex items-start gap-3 cursor-pointer group">
              <div className="relative flex-shrink-0 mt-0.5">
                <input
                  type="checkbox"
                  checked={hideWhenChannelDisabled}
                  onChange={(e) => setHideWhenChannelDisabled(e.target.checked)}
                  className="sr-only peer"
                />
                <div className="w-10 h-6 bg-gray-200 dark:bg-slate-600 rounded-full peer-checked:bg-primary-600 transition-colors" />
                <div className="absolute top-0.5 left-0.5 w-5 h-5 bg-white rounded-full shadow transition-transform peer-checked:translate-x-4" />
              </div>
              <div className="flex-1">
                <div className="flex items-center gap-1.5 text-sm font-medium text-gray-700 dark:text-slate-300 group-hover:text-gray-900 dark:group-hover:text-slate-100 transition-colors">
                  <AlertTriangle className="w-3.5 h-3.5 text-amber-500" />
                  {t('customers.customFields.hideWhenDisabled')}
                </div>
                <p className="mt-1 text-xs text-gray-500 dark:text-slate-400">
                  {t('customers.customFields.hideWhenDisabledHint')}
                </p>
              </div>
            </label>
          </div>
        )}

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-5">
          <div>
            <label className={labelClass}>{t('customers.customFields.sortOrder')}</label>
            <input
              type="number"
              value={sortOrder}
              onChange={(e) => setSortOrder(Number(e.target.value))}
              className={inputClass}
            />
          </div>
          <div className="flex items-end gap-6">
            <label className="flex items-center gap-2.5 cursor-pointer group">
              <div className="relative">
                <input
                  type="checkbox"
                  checked={isRequired}
                  onChange={(e) => setIsRequired(e.target.checked)}
                  className="sr-only peer"
                />
                <div className="w-10 h-6 bg-gray-200 dark:bg-slate-600 rounded-full peer-checked:bg-red-500 transition-colors" />
                <div className="absolute top-0.5 left-0.5 w-5 h-5 bg-white rounded-full shadow transition-transform peer-checked:translate-x-4" />
              </div>
              <span className="text-sm text-gray-700 dark:text-slate-300 group-hover:text-gray-900 dark:group-hover:text-slate-100 transition-colors">{t('customers.customFields.requiredField')}</span>
            </label>
            <label className="flex items-center gap-2.5 cursor-pointer group">
              <div className="relative">
                <input
                  type="checkbox"
                  checked={isActive}
                  onChange={(e) => setIsActive(e.target.checked)}
                  className="sr-only peer"
                />
                <div className="w-10 h-6 bg-gray-200 dark:bg-slate-600 rounded-full peer-checked:bg-green-500 transition-colors" />
                <div className="absolute top-0.5 left-0.5 w-5 h-5 bg-white rounded-full shadow transition-transform peer-checked:translate-x-4" />
              </div>
              <span className="text-sm text-gray-700 dark:text-slate-300 group-hover:text-gray-900 dark:group-hover:text-slate-100 transition-colors">{t('customers.customFields.enabled')}</span>
            </label>
          </div>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-5">
          <div>
            <label className={labelClass}>{t('customers.customFields.placeholderLabel')}</label>
            <input
              type="text"
              value={placeholder}
              onChange={(e) => setPlaceholder(e.target.value)}
              placeholder={t('customers.customFields.placeholderHint')}
              className={inputClass}
            />
          </div>
          <div>
            <label className={labelClass}>{t('customers.customFields.defaultValue')}</label>
            <input
              type="text"
              value={defaultValue}
              onChange={(e) => setDefaultValue(e.target.value)}
              placeholder={t('customers.customFields.defaultValueHint')}
              className={inputClass}
            />
          </div>
        </div>

        <div>
          <label className={labelClass}>{t('customers.customFields.helpText')}</label>
          <input
            type="text"
            value={helpText}
            onChange={(e) => setHelpText(e.target.value)}
            placeholder={t('customers.customFields.helpTextHint')}
            className={inputClass}
          />
        </div>

        {showOptions && (
          <div>
            <div className="flex items-center justify-between mb-3">
              <label className="text-sm font-semibold text-gray-700 dark:text-slate-200">{t('customers.customFields.optionsLabel')}</label>
              <span className="text-xs text-gray-400 dark:text-slate-500">{options.length}{t('customers.customFields.optionsCountSuffix')}</span>
            </div>
            <div className="flex items-center gap-2.5 mb-3">
              <input
                type="text"
                value={newOption}
                onChange={(e) => setNewOption(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault();
                    handleAddOption();
                  }
                }}
                placeholder={t('customers.customFields.optionPlaceholder')}
                className={`${inputClass} flex-1`}
              />
              <button
                onClick={handleAddOption}
                className="px-4 py-2.5 text-sm font-medium bg-gray-100 dark:bg-slate-700 hover:bg-gray-200 dark:hover:bg-slate-600 rounded-xl text-gray-700 dark:text-slate-200 transition-colors inline-flex items-center gap-1.5"
              >
                <Plus className="w-4 h-4" />
                {t('customers.customFields.addOption')}
              </button>
            </div>
            <div className="space-y-2">
              {options.length === 0 && (
                <div className="text-center py-8 text-sm text-gray-400 dark:text-slate-500 border-2 border-dashed border-gray-200 dark:border-slate-700 rounded-xl">
                  <FileText className="w-8 h-8 mx-auto mb-2 opacity-50" />
                  {t('customers.customFields.noOptions')}
                </div>
              )}
              {options.map((opt, index) => (
                <div
                  key={optionKeys[index] ?? index}
                  className="flex items-center justify-between px-4 py-3 bg-gray-50 dark:bg-slate-900/50 rounded-xl border border-gray-200 dark:border-slate-700 hover:border-primary-200 dark:hover:border-primary-800/50 transition-colors group"
                >
                  <div className="flex items-center gap-3">
                    <span className="w-6 h-6 flex items-center justify-center bg-white dark:bg-slate-800 rounded-lg text-xs font-medium text-gray-500 dark:text-slate-400 border border-gray-200 dark:border-slate-600">
                      {index + 1}
                    </span>
                    <span className="text-sm text-gray-700 dark:text-slate-200">{opt}</span>
                  </div>
                  <button
                    onClick={() => handleRemoveOption(index)}
                    className="p-1.5 text-gray-400 hover:text-red-500 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg opacity-0 group-hover:opacity-100 transition-all"
                  >
                    <X className="w-4 h-4" />
                  </button>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
}
