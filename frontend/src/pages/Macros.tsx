import { useState, useEffect, useRef, useCallback } from 'react';
import { macroApi, mailboxApi, userApi, conversationApi } from '../api';
import { useI18n } from '../context/I18nContext';
import { useCrudPage, type PagedList } from '../hooks/useCrudPage';
import type { TranslationKey } from '../locales/zh-Hans';
import type { TranslateParams } from '../i18n/types';
import type {
  MacroDto,
  MacroExecutionLogDto,
  MacroOverviewResponse,
  MacroQuery,
  MacroExecutionLogQuery,
  MacroPreviewResponse,
  CreateMacroRequest,
  MailboxResponse,
} from '../api/types';
import MacroEditor from '../components/MacroEditor';
import MacroTrashModal from '../components/MacroTrashModal';
import MacroDuplicateModal from '../components/MacroDuplicateModal';
import MacroPreviewModal, { describeMacroActions } from '../components/MacroPreviewModal';
import ConfirmDialog from '../components/ConfirmDialog';
import { formatDateTime } from '../utils/format';
import {
  Edit2,
  Trash2,
  Search,
  Plus,
  Activity,
  CheckCircle2,
  XCircle,
  Layers,
  ChevronLeft,
  ChevronRight,
  Copy,
  Eye,
} from 'lucide-react';

type TabKey = 'macros' | 'logs';

/** 与 `I18nContext` 的 `t` 签名一致；模块级纯函数需显式传入 `t` */
type TranslateFn = (key: TranslationKey, params?: TranslateParams) => string;

/** 状态筛选：`value` 是提交给后端的稳定值，`labelKey` 只持有真源 key（渲染处必须经 `t()`） */
const STATUS_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: '', labelKey: 'automation.common.allStatuses' },
  { value: 'true', labelKey: 'automation.common.enabled' },
  { value: 'false', labelKey: 'automation.common.disabled' },
];

const EXECUTION_STATUS_BADGE: Record<string, string> = {
  success: 'bg-emerald-100 text-emerald-700',
  failed: 'bg-red-100 text-red-700',
  partial: 'bg-amber-100 text-amber-700',
};

/**
 * 宏执行日志状态 → 文案 key。
 *
 * 产出方：后端 `MacroService` 仅写 `success` / `failed` 两值（`partial` 无产出方，
 * 与后端同名的 `EXECUTION_STATUS_BADGE` 配色一并保留为防御性映射）。
 * 表里只放 key，渲染处经 `t()`——直接渲染会把原始 key 漏到界面。
 * 类型用 `| undefined`（未开 `noUncheckedIndexedAccess`，否则未知码回落分支在类型上不可达）。
 */
const EXECUTION_STATUS_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  success: 'automation.common.statusSuccess',
  failed: 'automation.common.statusFailed',
  partial: 'automation.common.statusPartial',
};

/** 未知状态码回落原始码，与原行为一致 */
const executionStatusLabel = (t: TranslateFn, status: string) => {
  const key = EXECUTION_STATUS_LABEL_KEYS[status];
  return key ? t(key) : status;
};

import { getApiErrorMessage } from '../utils/error';

export default function Macros() {
  const { t } = useI18n();
  const [tab, setTab] = useState<TabKey>('macros');
  const [overview, setOverview] = useState<MacroOverviewResponse | null>(null);
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [users, setUsers] = useState<{ id: number; fullName: string }[]>([]);

  const crud = useCrudPage<MacroDto, MacroQuery>({
    defaultQuery: { page: 1, pageSize: 20 },
    fetchList: macroApi.list,
    fetchTrashList: macroApi.getTrash,
    fetchTrashCount: macroApi.getTrashCount,
  });

  const [logQuery, setLogQuery] = useState<MacroExecutionLogQuery>({ page: 1, pageSize: 20 });
  const [logList, setLogList] = useState<PagedList<MacroExecutionLogDto> | null>(null);
  const [logLoading, setLogLoading] = useState(false);

  // 垃圾箱操作目标
  const [restoreTarget, setRestoreTarget] = useState<MacroDto | null>(null);
  const [hardDeleteTarget, setHardDeleteTarget] = useState<MacroDto | null>(null);
  const [clearTrashOpen, setClearTrashOpen] = useState(false);

  // 复制
  const [duplicateTarget, setDuplicateTarget] = useState<MacroDto | null>(null);
  const [duplicateLoading, setDuplicateLoading] = useState(false);

  // 预览
  const [previewTarget, setPreviewTarget] = useState<MacroDto | null>(null);
  const [previewInitialConversationId, setPreviewInitialConversationId] = useState(1);
  const [previewData, setPreviewData] = useState<MacroPreviewResponse | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [executeLoading, setExecuteLoading] = useState(false);
  const [executeResult, setExecuteResult] = useState<{ success: boolean; message: string } | null>(null);

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
  // crud.query.keyword 被其他入口改动时，同步输入框缓冲
  useEffect(() => {
    setKeywordInput(crud.query.keyword ?? '');
  }, [crud.query.keyword]);
  // 卸载时清理挂起的防抖定时器
  useEffect(() => {
    return () => {
      if (keywordTimerRef.current !== null) window.clearTimeout(keywordTimerRef.current);
    };
  }, []);

  useEffect(() => {
    let destroyed = false;
    macroApi
      .overview()
      .then((r) => { if (!destroyed) setOverview(r); })
      .catch((err: unknown) => console.error('加载宏概览失败:', err));
    mailboxApi
      .listAll()
      .then((res: MailboxResponse[]) => { if (!destroyed) setMailboxes(Array.isArray(res) ? res : []); })
      .catch((err: unknown) => console.error('加载邮箱列表失败:', err));
    userApi
      .list()
      .then((res) => {
        if (destroyed) return;
        setUsers(
          Array.isArray(res.data)
            ? res.data.map((u) => ({ id: u.id, fullName: u.fullName || u.firstName + ' ' + u.lastName }))
            : [],
        );
      })
      .catch((err: unknown) => console.error('加载用户列表失败:', err));
    return () => { destroyed = true; };
  }, []);

  useEffect(() => {
    if (tab === 'logs') {
      setLogLoading(true);
      let cancelled = false;
      macroApi
        .executionLogs(logQuery)
        .then((data) => { if (!cancelled) setLogList(data); })
        .catch((err: unknown) => console.error('加载宏执行日志失败:', err))
        .finally(() => { if (!cancelled) setLogLoading(false); });
      return () => { cancelled = true; };
    }
  }, [tab, logQuery]);

  const refreshOverview = () => {
    macroApi.overview().then(setOverview).catch((err: unknown) => console.error('加载宏概览失败:', err));
  };

  const handleFilterChange = <T extends { page?: number }>(
    setter: React.Dispatch<React.SetStateAction<T>>,
    key: keyof T,
    value: T[keyof T],
  ) => {
    setter((q) => ({ ...q, [key]: value, page: 1 }));
  };

  const handlePageChange = <T extends { page?: number }>(
    setter: React.Dispatch<React.SetStateAction<T>>,
    page: number,
  ) => {
    setter((q) => ({ ...q, page }));
  };

  const handleToggleActive = async (m: MacroDto) => {
    try {
      await macroApi.toggleActive(m.id);
      toast.show({ type: 'success', text: t(m.isActive ? 'automation.common.toggleDisabled' : 'automation.common.toggleEnabled', { name: m.name }) });
      refreshOverview();
      crud.refresh();
    } catch {
      toast.show({ type: 'error', text: t('automation.common.operationFailed') });
    }
  };

  const handleDelete = async () => {
    if (!crud.deleteTarget) return;
    try {
      await macroApi.remove(crud.deleteTarget.id);
      toast.show({ type: 'success', text: t('automation.macros.movedToTrash') });
      crud.setDeleteTarget(null);
      refreshOverview();
      crud.trash.refreshCount();
      crud.refresh();
    } catch {
      toast.show({ type: 'error', text: t('automation.common.deleteFailed') });
    }
  };

  // ====== 垃圾箱 ======
  const openTrash = () => {
    crud.trash.setOpen(true);
    crud.trash.setQuery({ page: 1, pageSize: 10, keyword: '' });
    crud.trash.refreshCount();
  };

  const handleRestore = async () => {
    if (!restoreTarget) return;
    crud.trash.setActionLoading(true);
    try {
      await macroApi.restore(restoreTarget.id);
      toast.show({ type: 'success', text: t('automation.macros.restored', { name: restoreTarget.name }) });
      setRestoreTarget(null);
      refreshOverview();
      crud.trash.refresh();
      crud.trash.refreshCount();
      crud.refresh();
    } catch (e: unknown) {
      toast.show({ type: 'error', text: getApiErrorMessage(e, t('automation.common.restoreFailed')) });
    } finally {
      crud.trash.setActionLoading(false);
    }
  };

  const handleHardDelete = async () => {
    if (!hardDeleteTarget) return;
    crud.trash.setActionLoading(true);
    try {
      await macroApi.hardDelete(hardDeleteTarget.id);
      toast.show({ type: 'success', text: t('automation.macros.hardDeleted', { name: hardDeleteTarget.name }) });
      setHardDeleteTarget(null);
      crud.trash.refresh();
      crud.trash.refreshCount();
      refreshOverview();
    } catch (e: unknown) {
      toast.show({ type: 'error', text: getApiErrorMessage(e, t('automation.common.deleteFailed')) });
    } finally {
      crud.trash.setActionLoading(false);
    }
  };

  const handleClearTrash = async () => {
    crud.trash.setActionLoading(true);
    try {
      const res: { count?: number } = await macroApi.clearTrash();
      const count = res?.count ?? 0;
      toast.show({ type: 'success', text: t('automation.common.trashCleared', { count }) });
      setClearTrashOpen(false);
      crud.trash.refresh();
      crud.trash.refreshCount();
      refreshOverview();
    } catch (e: unknown) {
      toast.show({ type: 'error', text: getApiErrorMessage(e, t('automation.common.clearTrashFailed')) });
    } finally {
      crud.trash.setActionLoading(false);
    }
  };

  const handleSave = async (data: CreateMacroRequest, id?: number) => {
    try {
      if (id) {
        await macroApi.update(id, { ...data, id });
        toast.show({ type: 'success', text: t('automation.macros.updated') });
      } else {
        await macroApi.create(data);
        toast.show({ type: 'success', text: t('automation.macros.created') });
      }
      crud.closeEditor();
      refreshOverview();
      crud.refresh();
    } catch (e: unknown) {
      toast.show({ type: 'error', text: t('automation.common.saveFailed') });
      throw e;
    }
  };

  const openDuplicate = (m: MacroDto) => {
    setDuplicateTarget(m);
  };

  const handleDuplicate = async (name: string) => {
    if (!duplicateTarget) return;
    setDuplicateLoading(true);
    try {
      const created = await macroApi.duplicate(duplicateTarget.id, name || undefined);
      toast.show({ type: 'success', text: t('automation.macros.duplicated', { name: created?.name ?? duplicateTarget.name + t('automation.macros.copySuffix') }) });
      setDuplicateTarget(null);
      refreshOverview();
      crud.refresh();
    } catch (e: unknown) {
      toast.show({ type: 'error', text: getApiErrorMessage(e, t('automation.macros.duplicateFailed')) });
    } finally {
      setDuplicateLoading(false);
    }
  };

  const openPreview = async (m: MacroDto) => {
    setPreviewTarget(m);
    setPreviewData(null);
    setExecuteResult(null);
    try {
      const res = (await conversationApi.list({ page: 1, pageSize: 1 })) as { data?: { id: number }[] };
      const firstConversation = res?.data?.[0];
      setPreviewInitialConversationId(firstConversation?.id ?? 0);
    } catch {
      setPreviewInitialConversationId(1);
    }
  };

  const closePreview = () => {
    setPreviewTarget(null);
    setPreviewData(null);
    setExecuteResult(null);
  };

  const runPreview = async (conversationId: number) => {
    if (!previewTarget || !conversationId) return;
    setPreviewLoading(true);
    setExecuteResult(null);
    try {
      const data = await macroApi.preview(previewTarget.id, conversationId);
      setPreviewData(data);
    } catch (e: unknown) {
      toast.show({ type: 'error', text: getApiErrorMessage(e, t('automation.macros.previewFailed')) });
    } finally {
      setPreviewLoading(false);
    }
  };

  const executeMacro = async (conversationId: number) => {
    if (!previewTarget || !conversationId) return;
    setExecuteLoading(true);
    try {
      const result = await macroApi.execute(previewTarget.id, conversationId);
      // 成功路径由前端按 actionsExecuted 组装（后端成功文案即「已执行 N 个动作」）；
      // 失败路径保留后端下发的错误正文（API 错误消息属批次 9 的 .resx 范围）
      setExecuteResult({
        success: result.success,
        message: result.success
          ? t('automation.macros.execute.executedCount', { count: result.actionsExecuted })
          : result.message,
      });
      if (result.success) {
        toast.show({ type: 'success', text: t('automation.macros.execute.succeededNamed', { name: previewTarget.name }) });
      }
    } catch (e: unknown) {
      const msg = getApiErrorMessage(e, t('automation.macros.execute.failed'));
      setExecuteResult({ success: false, message: msg });
      toast.show({ type: 'error', text: msg });
    } finally {
      setExecuteLoading(false);
    }
  };

  const macroData = crud.list?.data ?? [];
  const macroTotal = crud.list?.totalItems ?? 0;
  const macroTotalPages = crud.list?.totalPages ?? 0;
  const macroCurrentPage = crud.list?.currentPage ?? 1;

  const logData = logList?.data ?? [];
  const logTotal = logList?.totalItems ?? 0;
  const logTotalPages = logList?.totalPages ?? 0;
  const logCurrentPage = logList?.currentPage ?? 1;

  return (
    <div className="space-y-5">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-semibold text-gray-900 dark:text-slate-100">{t('automation.macros.page.title')}</h1>
          <p className="text-sm text-gray-500 dark:text-slate-400 mt-1">{t('automation.macros.page.subtitle')}</p>
        </div>
        <div className="flex items-center gap-2 flex-wrap">
          <button
            onClick={openTrash}
            className="relative px-3 py-2 text-sm font-medium text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition flex items-center gap-1.5"
            title={t('automation.common.trash')}
          >
            <Trash2 className="w-4 h-4" />
            <span className="hidden sm:inline">{t('automation.common.trash')}</span>
            {crud.trash.count > 0 && (
              <span className="absolute -top-1.5 -right-1.5 min-w-[20px] h-5 px-1.5 flex items-center justify-center text-xs font-bold text-white bg-red-500 rounded-full">
                {crud.trash.count > 99 ? '99+' : crud.trash.count}
              </span>
            )}
          </button>
          <button
            onClick={() => crud.openEditor()}
            className="px-3 py-2 text-sm font-medium text-white bg-primary-600 rounded-lg hover:bg-primary-700 flex items-center gap-1.5"
          >
            <Plus className="w-4 h-4" />
            <span className="hidden sm:inline">{t('automation.macros.page.new')}</span>
            <span className="sm:hidden">{t('automation.common.newShort')}</span>
          </button>
        </div>
      </div>

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        {[
          { labelKey: 'automation.macros.stats.total' as TranslationKey, value: overview?.totalMacros ?? 0, color: 'text-gray-700 dark:text-slate-200', bg: 'bg-gray-50 dark:bg-slate-800/50', icon: Layers },
          { labelKey: 'automation.common.statsActive' as TranslationKey, value: overview?.activeMacros ?? 0, color: 'text-green-600', bg: 'bg-green-50', icon: CheckCircle2 },
          { labelKey: 'automation.common.statsInactive' as TranslationKey, value: overview?.inactiveMacros ?? 0, color: 'text-gray-500 dark:text-slate-400', bg: 'bg-gray-50 dark:bg-slate-800/50', icon: XCircle },
          { labelKey: 'automation.common.statsExecutions' as TranslationKey, value: overview?.totalExecutions ?? 0, color: 'text-blue-600', bg: 'bg-blue-50', icon: Activity },
        ].map((s) => (
          <div key={s.labelKey} className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-5 shadow-sm hover:shadow-md transition-shadow">
            <div className="flex items-center justify-between">
              <div className="text-sm text-gray-500 dark:text-slate-400">{t(s.labelKey)}</div>
              <div className={`p-2 rounded-lg ${s.bg}`}>
                <s.icon className={`w-5 h-5 ${s.color}`} />
              </div>
            </div>
            <div className={`text-3xl font-bold mt-3 ${s.color}`}>{s.value}</div>
          </div>
        ))}
      </div>

      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm">
        <div className="flex border-b border-gray-200 dark:border-slate-700">
          <button
            onClick={() => setTab('macros')}
            className={`px-5 py-3 text-sm font-medium border-b-2 -mb-px transition ${
              tab === 'macros'
                ? 'border-primary-500 text-primary-600'
                : 'border-transparent text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:text-slate-300'
            }`}
          >
            {t('automation.macros.tabs.list', { count: macroTotal })}
          </button>
          <button
            onClick={() => setTab('logs')}
            className={`px-5 py-3 text-sm font-medium border-b-2 -mb-px transition ${
              tab === 'logs'
                ? 'border-primary-500 text-primary-600'
                : 'border-transparent text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:text-slate-300'
            }`}
          >
            {t('automation.macros.tabs.logs', { count: logTotal })}
          </button>
        </div>

        {tab === 'macros' && (
          <>
            <div className="p-4 border-b border-gray-100 dark:border-slate-700/50">
              <div className="flex items-center gap-2 md:gap-3 flex-wrap">
                <div className="flex-1 min-w-[140px]">
                  <div className="relative">
                    <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
                    <input
                      type="text"
                      placeholder={t('automation.macros.list.searchPlaceholder')}
                      value={keywordInput}
                      onChange={(e) => handleKeywordInput(e.target.value)}
                      onKeyDown={(e) => { if (e.key === 'Enter') triggerKeywordSearch(); }}
                      className="w-full pl-9 pr-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-900"
                    />
                  </div>
                </div>
                <select
                  value={crud.query.mailboxId ?? ''}
                  onChange={(e) =>
                    crud.handleFilterChange(
                      'mailboxId',
                      e.target.value ? Number(e.target.value) : null,
                    )
                  }
                  className="flex-1 min-w-[100px] px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-900 dark:text-slate-200"
                >
                  <option value="">{t('automation.common.allMailboxes')}</option>
                  {mailboxes.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.name}
                    </option>
                  ))}
                </select>
                <select
                  value={crud.query.isActive === null || crud.query.isActive === undefined ? '' : String(crud.query.isActive)}
                  onChange={(e) =>
                    crud.handleFilterChange(
                      'isActive',
                      e.target.value === '' ? null : e.target.value === 'true',
                    )
                  }
                  className="flex-1 min-w-[100px] px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-900 dark:text-slate-200"
                >
                  {STATUS_OPTIONS.map((o) => (
                    <option key={o.value} value={o.value}>
                      {t(o.labelKey)}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            <div className="hidden md:block overflow-x-auto">
              <table className="w-full">
                <thead className="bg-gray-50 dark:bg-slate-900 border-b border-gray-200 dark:border-slate-700">
                  <tr>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.macros.list.columns.name')}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.common.mailboxColumn')}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.macros.list.columns.actionsPreview')}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.macros.list.columns.usageCount')}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.macros.list.columns.lastUsed')}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.common.status')}</th>
                    <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.common.actions')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {crud.loading ? (
                    <tr>
                      <td colSpan={7} className="px-4 py-12 text-center text-gray-400 text-sm">
                        {t('common.shell.loading')}
                      </td>
                    </tr>
                  ) : macroData.length === 0 ? (
                    <tr>
                      <td colSpan={7} className="px-4 py-12 text-center text-gray-400 text-sm">
                        {t('automation.macros.list.empty')}
                      </td>
                    </tr>
                  ) : (
                    macroData.map((m) => (
                      <tr key={m.id} className="hover:bg-gray-50 dark:hover:bg-slate-700 dark:bg-slate-900">
                        <td className="px-4 py-3">
                          <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{m.name}</div>
                          {m.description && (
                            <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-1">{m.description}</div>
                          )}
                        </td>
                        <td className="px-4 py-3 text-sm text-gray-700 dark:text-slate-300">
                          {!m.mailboxId ? <span className="text-gray-400">{t('automation.common.allMailboxes')}</span> : m.mailboxName || `#${m.mailboxId}`}
                        </td>
                        <td className="px-4 py-3 text-sm text-gray-700 dark:text-slate-300 max-w-md">
                          <span className="px-2 py-0.5 text-xs rounded-full bg-blue-50 text-blue-700 mr-2 whitespace-nowrap">
                            {t('automation.common.actionCount', { count: m.actions.length })}
                          </span>
                          {m.actions.length > 0 && (
                            <span className="text-xs text-gray-500 dark:text-slate-400 line-clamp-2 inline">
                              {describeMacroActions(t, m.actions)}
                            </span>
                          )}
                        </td>
                        <td className="px-4 py-3 text-sm font-medium text-gray-900 dark:text-slate-100">{m.usageCount}</td>
                        <td className="px-4 py-3 text-xs text-gray-500 dark:text-slate-400">
                          {m.lastUsedAt ? formatDateTime(m.lastUsedAt) : '—'}
                        </td>
                        <td className="px-4 py-3">
                          <button
                            onClick={() => handleToggleActive(m)}
                            className={`relative inline-flex h-6 w-11 items-center rounded-full transition ${
                              m.isActive ? 'bg-green-500' : 'bg-gray-300'
                            }`}
                          >
                            <span
                              className={`inline-block h-4 w-4 transform rounded-full bg-white dark:bg-slate-800 transition ${
                                m.isActive ? 'translate-x-6' : 'translate-x-1'
                              }`}
                            />
                          </button>
                        </td>
                        <td className="px-4 py-3 text-right">
                          <div className="flex items-center justify-end gap-1">
                            <button
                              onClick={() => openPreview(m)}
                              className="p-2 text-gray-600 hover:bg-gray-100 dark:hover:bg-slate-700 rounded-lg transition-colors"
                              title={t('automation.macros.list.previewTooltip')}
                            >
                              <Eye className="w-4 h-4" />
                            </button>
                            <button
                              onClick={() => openDuplicate(m)}
                              className="p-2 text-emerald-600 hover:bg-emerald-50 rounded-lg transition-colors"
                              title={t('automation.macros.list.duplicateTooltip')}
                            >
                              <Copy className="w-4 h-4" />
                            </button>
                            <button
                              onClick={() => crud.openEditor(m)}
                              className="p-2 text-blue-600 hover:bg-blue-50 rounded-lg transition-colors"
                              title={t('common.edit')}
                            >
                              <Edit2 className="w-4 h-4" />
                            </button>
                            <button
                              onClick={() => crud.setDeleteTarget(m)}
                              className="p-2 text-red-600 hover:bg-red-50 rounded-lg transition-colors"
                              title={t('common.delete')}
                            >
                              <Trash2 className="w-4 h-4" />
                            </button>
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
              {crud.loading ? (
                <div className="p-12 text-center text-gray-400 text-sm">{t('common.shell.loading')}</div>
              ) : macroData.length === 0 ? (
                <div className="p-12 text-center text-gray-400 text-sm">{t('automation.macros.list.empty')}</div>
              ) : (
                macroData.map((m) => (
                  <div key={m.id} className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700 space-y-2.5">
                    <div className="flex items-start justify-between gap-2">
                      <div className="flex-1 min-w-0">
                        <div className="text-sm font-semibold text-gray-900 dark:text-slate-100 truncate">{m.name}</div>
                        {m.description && (
                          <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-2">{m.description}</div>
                        )}
                      </div>
                      <button
                        onClick={() => handleToggleActive(m)}
                        className={`relative inline-flex h-6 w-11 flex-shrink-0 items-center rounded-full transition ${
                          m.isActive ? 'bg-green-500' : 'bg-gray-300'
                        }`}
                        title={m.isActive ? t('automation.common.clickToDisable') : t('automation.common.clickToEnable')}
                      >
                        <span
                          className={`inline-block h-4 w-4 transform rounded-full bg-white dark:bg-slate-800 transition ${
                            m.isActive ? 'translate-x-6' : 'translate-x-1'
                          }`}
                        />
                      </button>
                    </div>

                    <div className="flex flex-wrap items-center gap-1.5 text-xs">
                      <span className="px-2 py-0.5 rounded-full bg-blue-50 text-blue-700">
                        {t('automation.common.actionCount', { count: m.actions.length })}
                      </span>
                      <span className="px-2 py-0.5 rounded-full bg-gray-100 dark:bg-slate-700/50 text-gray-700 dark:text-slate-300">
                        {!m.mailboxId ? t('automation.common.allMailboxes') : m.mailboxName || `#${m.mailboxId}`}
                      </span>
                      <span className="px-2 py-0.5 rounded-full bg-gray-100 dark:bg-slate-700/50 text-gray-700 dark:text-slate-300">
                        {t('automation.macros.list.usageCountBadge', { count: m.usageCount })}
                      </span>
                    </div>

                    {m.actions.length > 0 && (
                      <div className="text-xs text-gray-500 dark:text-slate-400 line-clamp-2">{describeMacroActions(t, m.actions)}</div>
                    )}

                    <div className="flex items-center justify-between pt-1">
                      <div className="text-xs text-gray-500 dark:text-slate-400">
                        {m.lastUsedAt ? t('automation.macros.list.lastUsedLine', { value: formatDateTime(m.lastUsedAt) }) : t('automation.macros.list.neverUsed')}
                      </div>
                      <div className="flex items-center gap-1">
                        <button
                          onClick={() => openPreview(m)}
                          className="p-2 text-gray-600 hover:bg-gray-100 dark:hover:bg-slate-700 rounded-lg transition-colors"
                          title={t('automation.macros.list.previewTooltip')}
                        >
                          <Eye className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => openDuplicate(m)}
                          className="p-2 text-emerald-600 hover:bg-emerald-50 dark:hover:bg-emerald-900/30 rounded-lg transition-colors"
                          title={t('automation.macros.list.duplicateTooltip')}
                        >
                          <Copy className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => crud.openEditor(m)}
                          className="p-2 text-blue-600 hover:bg-blue-50 dark:hover:bg-blue-900/30 rounded-lg transition-colors"
                          title={t('common.edit')}
                        >
                          <Edit2 className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => crud.setDeleteTarget(m)}
                          className="p-2 text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-lg transition-colors"
                          title={t('common.delete')}
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                    </div>
                  </div>
                ))
              )}
            </div>

            {macroTotalPages > 1 && (
              <div className="flex items-center justify-between px-4 py-3 border-t border-gray-100 dark:border-slate-700/50">
                <div className="text-sm text-gray-500 dark:text-slate-400">
                  {t('automation.common.paginationTotalAndPage', { count: macroTotal, page: macroCurrentPage, total: macroTotalPages })}
                </div>
                <div className="flex items-center gap-1">
                  <button
                    onClick={() => crud.handlePageChange(macroCurrentPage - 1)}
                    disabled={macroCurrentPage <= 1}
                    className="p-2 text-gray-600 dark:text-slate-400 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition-colors"
                  >
                    <ChevronLeft className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => crud.handlePageChange(macroCurrentPage + 1)}
                    disabled={macroCurrentPage >= macroTotalPages}
                    className="p-2 text-gray-600 dark:text-slate-400 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition-colors"
                  >
                    <ChevronRight className="w-4 h-4" />
                  </button>
                </div>
              </div>
            )}
          </>
        )}

        {tab === 'logs' && (
          <>
            <div className="p-4 border-b border-gray-100 dark:border-slate-700/50">
              <div className="flex items-center gap-3 flex-wrap">
                <select
                  value={logQuery.macroId ?? ''}
                  onChange={(e) =>
                    handleFilterChange(
                      setLogQuery,
                      'macroId',
                      e.target.value ? Number(e.target.value) : null,
                    )
                  }
                  className="px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500"
                >
                  <option value="">{t('automation.macros.list.allMacros')}</option>
                  {macroData.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.name}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            <div className="hidden md:block overflow-x-auto">
              <table className="w-full">
                <thead className="bg-gray-50 dark:bg-slate-900 border-b border-gray-200 dark:border-slate-700">
                  <tr>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.macros.logs.columns.macroName')}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.common.conversation')}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.macros.logs.columns.executedBy')}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.macros.logs.columns.actionsExecuted')}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.common.status')}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.macros.logs.columns.executedAt')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {logLoading ? (
                    <tr>
                      <td colSpan={6} className="px-4 py-12 text-center text-gray-400 text-sm">
                        {t('common.shell.loading')}
                      </td>
                    </tr>
                  ) : logData.length === 0 ? (
                    <tr>
                      <td colSpan={6} className="px-4 py-12 text-center text-gray-400 text-sm">
                        {t('automation.common.noExecutionLogs')}
                      </td>
                    </tr>
                  ) : (
                    logData.map((l) => (
                      <tr key={l.id} className="hover:bg-gray-50 dark:hover:bg-slate-700 dark:bg-slate-900">
                        <td className="px-4 py-3 text-sm font-medium text-gray-900 dark:text-slate-100">{l.macroName}</td>
                        <td className="px-4 py-3 text-sm text-gray-700 dark:text-slate-300">
                          <a
                            href={`/conversations/${l.conversationId}`}
                            className="text-primary-600 hover:underline"
                          >
                            #{l.conversationId}
                          </a>
                          {l.conversationSubject && (
                            <span className="text-gray-500 dark:text-slate-400 ml-2">
                              {l.conversationSubject.length > 30
                                ? l.conversationSubject.slice(0, 30) + '...'
                                : l.conversationSubject}
                            </span>
                          )}
                        </td>
                        <td className="px-4 py-3 text-sm text-gray-700 dark:text-slate-300">
                          {l.executedByUserName || t('automation.common.userNumber', { id: l.executedByUserId })}
                        </td>
                        <td className="px-4 py-3 text-sm text-gray-700 dark:text-slate-300">{t('automation.common.countSuffix', { count: l.actionsExecuted })}</td>
                        <td className="px-4 py-3">
                          <span
                            className={`text-xs px-2 py-0.5 rounded-full ${
                              EXECUTION_STATUS_BADGE[l.status] || 'bg-gray-100 dark:bg-slate-700/50 text-gray-700 dark:text-slate-300'
                            }`}
                          >
                            {executionStatusLabel(t, l.status)}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-xs text-gray-500 dark:text-slate-400">
                          {l.executedAt ? formatDateTime(l.executedAt) : '—'}
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            {/* 移动端卡片视图 */}
            <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
              {logLoading ? (
                <div className="p-12 text-center text-gray-400 text-sm">{t('common.shell.loading')}</div>
              ) : logData.length === 0 ? (
                <div className="p-12 text-center text-gray-400 text-sm">{t('automation.common.noExecutionLogs')}</div>
              ) : (
                logData.map((l) => (
                  <div key={l.id} className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700 space-y-2">
                    <div className="flex items-start justify-between gap-2">
                      <div className="flex-1 min-w-0">
                        <div className="text-sm font-semibold text-gray-900 dark:text-slate-100 truncate">{l.macroName}</div>
                        <a
                          href={`/conversations/${l.conversationId}`}
                          className="text-xs text-primary-600 hover:underline"
                        >
                          {t('automation.common.conversationNumber', { id: l.conversationId })}
                        </a>
                      </div>
                      <span
                        className={`text-xs px-2 py-0.5 rounded-full flex-shrink-0 ${
                          EXECUTION_STATUS_BADGE[l.status] || 'bg-gray-100 dark:bg-slate-700/50 text-gray-700 dark:text-slate-300'
                        }`}
                      >
                        {executionStatusLabel(t, l.status)}
                      </span>
                    </div>

                    {l.conversationSubject && (
                      <div className="text-xs text-gray-600 dark:text-slate-300 line-clamp-2">{l.conversationSubject}</div>
                    )}

                    <div className="flex items-center justify-between text-xs text-gray-500 dark:text-slate-400">
                      <div className="flex items-center gap-2">
                        <span>{t('automation.macros.logs.executedByPrefix')}<span className="text-gray-700 dark:text-slate-300">{l.executedByUserName || `#${l.executedByUserId}`}</span></span>
                        <span>·</span>
                        <span>{t('automation.macros.logs.actionsPrefix')}<span className="text-gray-700 dark:text-slate-300">{l.actionsExecuted}</span>{t('automation.macros.logs.actionsSuffix')}</span>
                      </div>
                    </div>

                    <div className="text-xs text-gray-500 dark:text-slate-400">
                      {l.executedAt ? formatDateTime(l.executedAt) : '—'}
                    </div>
                  </div>
                ))
              )}
            </div>

            {logTotalPages > 1 && (
              <div className="flex items-center justify-between px-4 py-3 border-t border-gray-100 dark:border-slate-700/50">
                <div className="text-sm text-gray-500 dark:text-slate-400">
                  {t('automation.common.paginationTotalAndPage', { count: logTotal, page: logCurrentPage, total: logTotalPages })}
                </div>
                <div className="flex items-center gap-1">
                  <button
                    onClick={() => handlePageChange(setLogQuery, logCurrentPage - 1)}
                    disabled={logCurrentPage <= 1}
                    className="p-2 text-gray-600 dark:text-slate-400 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition-colors"
                  >
                    <ChevronLeft className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => handlePageChange(setLogQuery, logCurrentPage + 1)}
                    disabled={logCurrentPage >= logTotalPages}
                    className="p-2 text-gray-600 dark:text-slate-400 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition-colors"
                  >
                    <ChevronRight className="w-4 h-4" />
                  </button>
                </div>
              </div>
            )}
          </>
        )}
      </div>

      {crud.showEditor && (
        <MacroEditor
          editing={crud.editing}
          mailboxes={mailboxes}
          users={users}
          onSave={handleSave}
          onCancel={crud.closeEditor}
        />
      )}

      <ConfirmDialog
        open={!!crud.deleteTarget}
        title={t('automation.common.moveToTrash')}
        message={t('automation.macros.delete.message', { name: crud.deleteTarget?.name ?? '' })}
        confirmText={t('automation.common.moveToTrash')}
        variant="danger"
        onConfirm={handleDelete}
        onCancel={() => crud.setDeleteTarget(null)}
      />

      <MacroTrashModal
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
      />

      <MacroDuplicateModal
        target={duplicateTarget}
        onClose={() => setDuplicateTarget(null)}
        onConfirm={handleDuplicate}
        loading={duplicateLoading}
      />

      <MacroPreviewModal
        target={previewTarget}
        initialConversationId={previewInitialConversationId}
        onClose={closePreview}
        onPreview={runPreview}
        onExecute={executeMacro}
        previewLoading={previewLoading}
        executeLoading={executeLoading}
        previewData={previewData}
        executeResult={executeResult}
      />

      {toast.message && (
        <div className="fixed top-4 right-4 z-50">
          <div
            className={`px-4 py-2 rounded-lg shadow-lg text-white text-sm ${
              toast.message.type === 'success' ? 'bg-green-500' : 'bg-red-500'
            }`}
          >
            {toast.message.text}
          </div>
        </div>
      )}
    </div>
  );
}
