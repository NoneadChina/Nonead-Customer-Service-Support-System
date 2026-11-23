import { useState, useEffect, useRef, useCallback } from 'react';
import { webhookApi } from '../api';
import { useTimedToast } from '../hooks/useTimedToast';
import type {
  WebhookDto,
  WebhookLogDto,
  WebhookOverviewResponse,
  CreateWebhookRequest,
} from '../api/types';
import Modal from '../components/Modal';
import TrashDialogs from '../components/TrashDialogs';
import { useI18n } from '../context/I18nContext';
import { toIntlLocale } from '../i18n/locale';
import type { TranslationKey } from '../locales/zh-Hans';
import {
  Webhook,
  List,
  FileText,
  Plus,
  Search,
  Edit2,
  Trash2,
  Play,
  CheckCircle2,
  XCircle,
  Clock,
  Activity,
  Globe,
  Shield,
  Zap,
  AlertCircle,
  ChevronLeft,
  ChevronRight,
} from 'lucide-react';

/**
 * 事件类型目录（分组 → 选项）。
 *
 * 产出方：后端 `IWebhookService.TriggerAsync` 的调用点（9 码，见波次 14 报告 §4）；
 * `WebhooksController` **没有** event-types 目录端点，后端只**产出**这些码、不枚举也不加工，
 * 列表与日志里回传的 `eventType` 就是原始码。故本表即前端唯一目录，9 码与后端 0 缺 0 余。
 *
 * ⚠ 不复用 `notifications.eventType.*`：那是站内通知的事件目录（22 项、语义化键名），
 * 与本目录的 snake_case 码集不同源；同一个 `conversation_updated` 在通知侧叫「会话已更新」、
 * 在本页叫「会话更新」，共享会改中文实词。
 */
const EVENT_GROUPS = [
  {
    categoryKey: 'integration.webhooks.eventCategory.conversation',
    events: [
      { value: 'conversation_created', labelKey: 'integration.webhooks.event.conversationCreated' },
      { value: 'conversation_updated', labelKey: 'integration.webhooks.event.conversationUpdated' },
      { value: 'conversation_deleted', labelKey: 'integration.webhooks.event.conversationDeleted' },
      { value: 'conversation_status_changed', labelKey: 'integration.webhooks.event.conversationStatusChanged' },
      { value: 'thread_created', labelKey: 'integration.webhooks.event.threadCreated' },
    ],
  },
  {
    categoryKey: 'integration.webhooks.eventCategory.customer',
    events: [
      { value: 'customer_created', labelKey: 'integration.webhooks.event.customerCreated' },
      { value: 'customer_updated', labelKey: 'integration.webhooks.event.customerUpdated' },
    ],
  },
  {
    categoryKey: 'integration.webhooks.eventCategory.user',
    events: [
      { value: 'user_created', labelKey: 'integration.webhooks.event.userCreated' },
      { value: 'user_updated', labelKey: 'integration.webhooks.event.userUpdated' },
    ],
  },
] satisfies { categoryKey: TranslationKey; events: { value: string; labelKey: TranslationKey }[] }[];

/**
 * 请求方法：值是 HTTP 动词本身（后端 `CreateWebhookRequest.Method` 原样透传，
 * 列表里也**原样**渲染），两种语言下都是同一串技术标识，故不入字典。
 */
const METHOD_OPTIONS = ['POST', 'PUT', 'PATCH', 'DELETE', 'GET'];

const tabs = [
  { key: 'overview', labelKey: 'integration.common.tabOverview', icon: <Activity className="w-4 h-4" /> },
  { key: 'list', labelKey: 'integration.webhooks.tabList', icon: <List className="w-4 h-4" /> },
  { key: 'logs', labelKey: 'integration.webhooks.tabLogs', icon: <FileText className="w-4 h-4" /> },
] as const;

export default function Webhooks() {
  const { t, language } = useI18n();
  const [activeTab, setActiveTab] = useState<'overview' | 'list' | 'logs'>('overview');
  const [listSubTab, setListSubTab] = useState<'active' | 'trash'>('active');
  const [overview, setOverview] = useState<WebhookOverviewResponse | null>(null);
  const [webhooks, setWebhooks] = useState<WebhookDto[]>([]);
  const [trashWebhooks, setTrashWebhooks] = useState<WebhookDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [totalItems, setTotalItems] = useState(0);
  const [page, setPage] = useState(1);
  const [keyword, setKeyword] = useState('');
  // 搜索输入缓冲：即时回显，650ms 停顿后才写入 keyword（触发 loadList）
  const [keywordInput, setKeywordInput] = useState('');
  const [activeFilter, setActiveFilter] = useState<boolean | null>(null);
  const [showEditor, setShowEditor] = useState(false);
  const [editingWebhook, setEditingWebhook] = useState<WebhookDto | null>(null);
  const [logs, setLogs] = useState<WebhookLogDto[]>([]);
  const [logsTotal, setLogsTotal] = useState(0);
  const [logsPage, setLogsPage] = useState(1);
  const [testResult, setTestResult] = useState<{
    show: boolean;
    success: boolean;
    message: string;
    statusCode: number | null;
    responseBody: string | null;
    durationMs: number;
  } | null>(null);
  const [testingId, setTestingId] = useState<number | null>(null);
  const [deleteId, setDeleteId] = useState<number | null>(null);
  const [restoreId, setRestoreId] = useState<number | null>(null);
  const [hardDeleteId, setHardDeleteId] = useState<number | null>(null);
  const [clearTrashOpen, setClearTrashOpen] = useState(false);
  const { message: toast, show: showToast } = useTimedToast();
  const pageSize = 10;

  // 搜索防抖：650ms 输入停顿后写入 keyword 并回到第 1 页（loadList 由其 effect 触发），Enter 立即搜索
  const searchTimerRef = useRef<number | null>(null);
  const applySearch = useCallback((value: string) => {
    setKeyword(value);
    setPage(1);
  }, []);
  const handleSearchInput = useCallback((value: string) => {
    setKeywordInput(value);
    if (searchTimerRef.current !== null) window.clearTimeout(searchTimerRef.current);
    searchTimerRef.current = window.setTimeout(() => applySearch(value), 650);
  }, [applySearch]);
  const triggerSearchNow = useCallback(() => {
    if (searchTimerRef.current !== null) {
      window.clearTimeout(searchTimerRef.current);
      searchTimerRef.current = null;
    }
    applySearch(keywordInput);
  }, [applySearch, keywordInput]);
  const clearSearch = useCallback(() => {
    if (searchTimerRef.current !== null) {
      window.clearTimeout(searchTimerRef.current);
      searchTimerRef.current = null;
    }
    setKeywordInput('');
    setKeyword('');
  }, []);
  useEffect(() => {
    return () => {
      if (searchTimerRef.current !== null) window.clearTimeout(searchTimerRef.current);
    };
  }, []);

  useEffect(() => {
    if (activeTab === 'overview') loadOverview();
    if (activeTab === 'list') {
      loadList();
      loadTrash();
    }
    if (activeTab === 'logs') loadLogs();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab]);

  useEffect(() => {
    if (activeTab === 'list') loadList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, keyword, activeFilter]);

  useEffect(() => {
    if (activeTab === 'logs') loadLogs();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [logsPage]);

  async function loadOverview() {
    setLoading(true);
    try {
      const data = await webhookApi.overview();
      setOverview(data);
    } finally {
      setLoading(false);
    }
  }

  async function loadList() {
    setLoading(true);
    try {
      const data = await webhookApi.list({
      page,
      pageSize,
      keyword: keyword || null,
      isActive: activeFilter,
    });
    setWebhooks(data.data);
    setTotalItems(data.totalItems);
    } finally {
      setLoading(false);
    }
  }

  async function loadTrash() {
    setLoading(true);
    try {
      const data = await webhookApi.getTrash();
      setTrashWebhooks(data.data);
    } finally {
      setLoading(false);
    }
  }

  async function loadLogs() {
    setLoading(true);
    try {
      const data = await webhookApi.allLogs({ page: logsPage, pageSize });
      setLogs(data.data);
      setLogsTotal(data.totalItems);
    } finally {
      setLoading(false);
    }
  }

  async function handleToggle(id: number) {
    try {
      await webhookApi.toggleActive(id);
      showToast({ type: 'success', text: t('integration.common.toggleSucceeded') });
      if (activeTab === 'overview') loadOverview();
      if (activeTab === 'list') loadList();
    } catch {
      showToast({ type: 'error', text: t('integration.common.operationFailed') });
    }
  }

  async function handleDelete() {
    if (!deleteId) return;
    try {
      await webhookApi.remove(deleteId);
      showToast({ type: 'success', text: t('integration.webhooks.toast.movedToTrash') });
      setDeleteId(null);
      loadList();
      loadTrash();
      if (activeTab === 'overview') loadOverview();
    } catch {
      showToast({ type: 'error', text: t('integration.common.deleteFailed') });
    }
  }

  async function handleRestore() {
    if (!restoreId) return;
    try {
      await webhookApi.restore(restoreId);
      showToast({ type: 'success', text: t('integration.webhooks.toast.restored') });
      setRestoreId(null);
      loadList();
      loadTrash();
      if (activeTab === 'overview') loadOverview();
    } catch {
      showToast({ type: 'error', text: t('integration.common.restoreFailed') });
    }
  }

  async function handleHardDelete() {
    if (!hardDeleteId) return;
    try {
      await webhookApi.hardDelete(hardDeleteId);
      showToast({ type: 'success', text: t('integration.webhooks.toast.hardDeleted') });
      setHardDeleteId(null);
      loadTrash();
    } catch {
      showToast({ type: 'error', text: t('integration.common.hardDeleteFailed') });
    }
  }

  async function handleClearTrash() {
    try {
      await webhookApi.clearTrash();
      showToast({ type: 'success', text: t('integration.common.trashCleared') });
      setClearTrashOpen(false);
      loadTrash();
    } catch {
      showToast({ type: 'error', text: t('integration.common.clearFailed') });
    }
  }

  function handleCreate() {
    setEditingWebhook(null);
    setShowEditor(true);
  }

  function handleEdit(webhook: WebhookDto) {
    setEditingWebhook(webhook);
    setShowEditor(true);
  }

  function handleCloseEditor() {
    setShowEditor(false);
    setEditingWebhook(null);
  }

  async function handleSave(data: CreateWebhookRequest) {
    if (editingWebhook) {
      await webhookApi.update(editingWebhook.id, { ...data, id: editingWebhook.id });
      showToast({ type: 'success', text: t('integration.webhooks.toast.updated') });
    } else {
      await webhookApi.create(data);
      showToast({ type: 'success', text: t('integration.webhooks.toast.created') });
    }
    setShowEditor(false);
    setEditingWebhook(null);
    if (activeTab === 'list') loadList();
    if (activeTab === 'overview') loadOverview();
  }

  async function handleTest(id: number) {
    setTestingId(id);
    try {
      const result = await webhookApi.test(id, { eventType: 'conversation_created' });
      setTestResult({
        show: true,
        success: result.success,
        message: result.message,
        statusCode: result.statusCode,
        responseBody: result.responseBody,
        durationMs: result.durationMs,
      });
    } finally {
      setTestingId(null);
    }
  }

  const totalPages = Math.ceil(totalItems / pageSize);
  const totalLogPages = Math.ceil(logsTotal / pageSize);

  return (
    <div className="space-y-5">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-semibold text-gray-900 dark:text-slate-100">{t('integration.webhooks.pageTitle')}</h1>
          <p className="text-sm text-gray-500 dark:text-slate-400 mt-1">
            {t('integration.webhooks.subtitle')}
          </p>
        </div>
        {activeTab === 'list' && (
          <button
            onClick={handleCreate}
            className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-lg hover:bg-primary-700 transition shadow-sm"
          >
            <Plus className="w-4 h-4" />
            <span className="hidden sm:inline">{t('integration.webhooks.newWebhook')}</span>
            <span className="sm:hidden">{t('integration.common.newShort')}</span>
          </button>
        )}
      </div>

      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 shadow-sm">
        <div className="flex gap-1 p-1.5 border-b border-gray-100 dark:border-slate-700/50 overflow-x-auto">
          {tabs.map((tab) => (
            <button
              key={tab.key}
              onClick={() => setActiveTab(tab.key)}
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
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            {[
              { label: t('integration.webhooks.overview.totalWebhooks'), value: overview.totalWebhooks, color: 'text-blue-600', bg: 'bg-blue-50 dark:bg-blue-900/20', icon: Webhook },
              { label: t('integration.common.statsActive'), value: overview.activeWebhooks, color: 'text-green-600', bg: 'bg-green-50 dark:bg-green-900/20', icon: CheckCircle2 },
              { label: t('integration.webhooks.overview.totalTriggers'), value: overview.totalTriggers, color: 'text-purple-600', bg: 'bg-purple-50 dark:bg-purple-900/20', icon: Zap },
              { label: t('integration.webhooks.overview.successRate'), value: `${overview.successRate}%`, color: 'text-orange-600', bg: 'bg-orange-50 dark:bg-orange-900/20', icon: Shield },
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
                <h3 className="text-sm font-semibold text-gray-700 dark:text-slate-200">{t('integration.webhooks.overview.distribution')}</h3>
              </div>
              <div className="p-5 space-y-3">
                <div className="flex justify-between text-sm">
                  <span className="text-gray-500 dark:text-slate-400">{t('integration.webhooks.overview.globalWebhooks')}</span>
                  <span className="font-medium text-gray-900 dark:text-slate-100">{overview.globalWebhooks}</span>
                </div>
                <div className="flex justify-between text-sm">
                  <span className="text-gray-500 dark:text-slate-400">{t('integration.webhooks.overview.mailboxWebhooks')}</span>
                  <span className="font-medium text-gray-900 dark:text-slate-100">{overview.mailboxWebhooks}</span>
                </div>
                <div className="flex justify-between text-sm">
                  <span className="text-gray-500 dark:text-slate-400">{t('integration.webhooks.disabled')}</span>
                  <span className="font-medium text-gray-900 dark:text-slate-100">{overview.inactiveWebhooks}</span>
                </div>
                <div className="h-px bg-gray-100 dark:bg-slate-700/50 my-2" />
                <div className="flex justify-between text-sm">
                  <span className="text-gray-500 dark:text-slate-400">{t('integration.webhooks.overview.successCount')}</span>
                  <span className="font-medium text-green-600">{overview.totalSuccess}</span>
                </div>
                <div className="flex justify-between text-sm">
                  <span className="text-gray-500 dark:text-slate-400">{t('integration.webhooks.overview.failCount')}</span>
                  <span className="font-medium text-red-600">{overview.totalFail}</span>
                </div>
              </div>
            </div>

            <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 shadow-sm overflow-hidden">
              <div className="px-5 py-4 border-b border-gray-100 dark:border-slate-700/50">
                <h3 className="text-sm font-semibold text-gray-700 dark:text-slate-200">{t('integration.webhooks.overview.eventTypeDistribution')}</h3>
              </div>
              <div className="p-5 space-y-2.5">
                {(() => {
                  const values = Object.values(overview.eventsCount);
                  const maxVal = Math.max(1, ...values);
                  return Object.entries(overview.eventsCount).map(([event, count]) => (
                  <div key={event} className="flex items-center gap-3">
                    <span className="text-sm text-gray-600 dark:text-slate-300 flex-1 truncate">{event}</span>
                    <div className="w-32 bg-gray-100 dark:bg-slate-700 rounded-full h-2">
                      <div
                        className="bg-primary-500 h-2 rounded-full"
                        style={{ width: `${Math.min(100, (count / maxVal) * 100)}%` }}
                      />
                    </div>
                    <span className="text-sm font-medium w-8 text-right text-gray-900 dark:text-slate-100">{count}</span>
                  </div>
                ));
                })()}
                {Object.keys(overview.eventsCount).length === 0 && (
                  <p className="text-sm text-gray-400 dark:text-slate-500 text-center py-4">{t('common.empty')}</p>
                )}
              </div>
            </div>
          </div>

          <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 shadow-sm overflow-hidden">
            <div className="px-5 py-4 border-b border-gray-100 dark:border-slate-700/50">
              <h3 className="text-sm font-semibold text-gray-700 dark:text-slate-200">{t('integration.webhooks.overview.recentTriggers')}</h3>
            </div>
            {overview.recentLogs.length > 0 ? (
              <>
              <div className="hidden md:block overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="bg-gray-50 dark:bg-slate-900">
                    <tr className="text-left text-gray-500 dark:text-slate-400">
                      <th className="px-5 py-3 font-medium text-xs uppercase">Webhook</th>
                      <th className="px-5 py-3 font-medium text-xs uppercase">{t('integration.webhooks.columns.event')}</th>
                      <th className="px-5 py-3 font-medium text-xs uppercase">{t('integration.common.status')}</th>
                      <th className="px-5 py-3 font-medium text-xs uppercase">{t('integration.webhooks.columns.duration')}</th>
                      <th className="px-5 py-3 font-medium text-xs uppercase">{t('integration.webhooks.columns.time')}</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100 dark:divide-slate-700/50">
                    {overview.recentLogs.map(log => (
                      <tr key={log.id} className="hover:bg-gray-50 dark:hover:bg-slate-700/30">
                      <td className="px-5 py-3 text-gray-900 dark:text-slate-100 font-medium">{log.webhookName}</td>
                      <td className="px-5 py-3 text-gray-600 dark:text-slate-300">{log.eventType}</td>
                      <td className="px-5 py-3">
                        <span className={`inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-full ${log.isSuccess
                          ? 'bg-green-50 text-green-700 dark:bg-green-900/30 dark:text-green-400'
                          : 'bg-red-50 text-red-700 dark:bg-red-900/30 dark:text-red-400'}`}>
                          {log.isSuccess ? <CheckCircle2 className="w-3 h-3" /> : <XCircle className="w-3 h-3" />}
                          {log.isSuccess ? t('integration.webhooks.success') : t('integration.webhooks.failed')}
                        </span>
                      </td>
                      <td className="px-5 py-3 text-gray-600 dark:text-slate-300">
                        <span className="inline-flex items-center gap-1">
                          <Clock className="w-3.5 h-3.5 text-gray-400" />
                          {log.durationMs} ms
                        </span>
                      </td>
                      <td className="px-5 py-3 text-gray-500 dark:text-slate-400 whitespace-nowrap">
                        {new Date(log.createdAt).toLocaleString(toIntlLocale(language))}
                      </td>
                    </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
                {overview.recentLogs.map(log => (
                  <div key={log.id} className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700/30 space-y-2.5">
                    <div className="flex items-start justify-between gap-2">
                      <div className="flex items-center gap-2.5 min-w-0 flex-1">
                        <div className={`shrink-0 w-9 h-9 rounded-xl flex items-center justify-center ring-1 ${
                          log.isSuccess
                            ? 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-600 dark:text-emerald-400 ring-emerald-500/20'
                            : 'bg-rose-50 dark:bg-rose-900/30 text-rose-600 dark:text-rose-400 ring-rose-500/20'
                        }`}>
                          {log.isSuccess ? <CheckCircle2 className="w-4 h-4" /> : <XCircle className="w-4 h-4" />}
                        </div>
                        <div className="min-w-0">
                          <div className="text-sm font-bold text-gray-900 dark:text-slate-100 truncate">{log.webhookName}</div>
                          <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5">{log.eventType}</div>
                        </div>
                      </div>
                      <span className={`inline-flex items-center gap-1 px-2 py-0.5 text-[11px] font-bold rounded-full flex-shrink-0 ${
                        log.isSuccess
                          ? 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-400'
                          : 'bg-rose-50 dark:bg-rose-900/30 text-rose-700 dark:text-rose-400'
                      }`}>
                        {log.isSuccess ? t('integration.webhooks.success') : t('integration.webhooks.failed')}
                      </span>
                    </div>
                    <div className="flex items-center justify-between text-[11px] text-gray-500 dark:text-slate-400">
                      <span className="inline-flex items-center gap-1">
                        <Clock className="w-3 h-3" />
                        {log.durationMs} ms
                      </span>
                      <span>{new Date(log.createdAt).toLocaleString(toIntlLocale(language))}</span>
                    </div>
                  </div>
                ))}
              </div>
              </>
            ) : (
              <p className="text-gray-400 dark:text-slate-500 text-center py-10">
                <Activity className="w-12 h-12 mx-auto mb-3 opacity-30" />
                {t('integration.webhooks.noTriggers')}
              </p>
            )}
          </div>
        </div>
      )}

      {activeTab === 'list' && !loading && (
        <div className="space-y-4">
          <div className="flex items-center gap-3 flex-wrap">
            <div className="flex items-center gap-1 p-1 bg-gray-100 dark:bg-slate-800 rounded-xl">
              <button
                onClick={() => { setListSubTab('active'); setPage(1); clearSearch(); setActiveFilter(null); }}
                className={`px-3 sm:px-4 py-1.5 text-sm rounded-lg transition flex items-center gap-1.5 ${
                  listSubTab === 'active'
                    ? 'bg-white dark:bg-slate-700 text-primary-600 dark:text-primary-400 shadow-sm font-medium'
                    : 'text-gray-600 dark:text-slate-400 hover:text-gray-900 dark:hover:text-slate-200'
                }`}
              >
                <List className="w-4 h-4" />
                <span className="hidden sm:inline">{t('integration.webhooks.tabList')}</span>
                <span className="sm:hidden">{t('integration.webhooks.listShort')}</span>
                <span className="px-1.5 py-0.5 text-xs rounded-full bg-gray-200 dark:bg-slate-600 text-gray-600 dark:text-slate-300">
                  {webhooks.length}
                </span>
              </button>
              <button
                onClick={() => { setListSubTab('trash'); setPage(1); clearSearch(); setActiveFilter(null); }}
                className={`px-3 sm:px-4 py-1.5 text-sm rounded-lg transition flex items-center gap-1.5 ${
                  listSubTab === 'trash'
                    ? 'bg-white dark:bg-slate-700 text-primary-600 dark:text-primary-400 shadow-sm font-medium'
                    : 'text-gray-600 dark:text-slate-400 hover:text-gray-900 dark:hover:text-slate-200'
                }`}
              >
                <Trash2 className="w-4 h-4" />
                <span className="hidden sm:inline">{t('integration.common.trash')}</span>
                {trashWebhooks.length > 0 && (
                  <span className="px-1.5 py-0.5 text-xs rounded-full bg-red-100 dark:bg-red-900/30 text-red-600 dark:text-red-400">
                    {trashWebhooks.length}
                  </span>
                )}
              </button>
            </div>
            {listSubTab === 'trash' && trashWebhooks.length > 0 && (
              <button
                onClick={() => setClearTrashOpen(true)}
                className="inline-flex items-center gap-1.5 px-3 sm:px-4 py-2 text-sm bg-red-50 hover:bg-red-100 dark:bg-red-900/20 dark:hover:bg-red-900/30 text-red-600 dark:text-red-400 rounded-lg transition"
              >
                <Trash2 className="w-4 h-4" />
                <span className="hidden sm:inline">{t('integration.common.clearTrash')}</span>
                <span className="sm:hidden">{t('integration.webhooks.clearShort')}</span>
              </button>
            )}
          </div>

          <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 p-4 shadow-sm">
            <div className="flex items-center gap-3 flex-wrap">
              <div className="flex-1 min-w-[160px] sm:min-w-[200px]">
                <div className="relative">
                  <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
                  <input
                    type="text"
                    placeholder={t('integration.webhooks.searchPlaceholder')}
                    value={keywordInput}
                    onChange={e => handleSearchInput(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') triggerSearchNow();
                    }}
                    className="w-full pl-9 pr-3 py-2 text-sm border border-gray-200 dark:border-slate-700 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:bg-slate-900/50 dark:text-slate-200"
                  />
                </div>
              </div>
              {listSubTab === 'active' && (
                <select
                  value={activeFilter === null ? '' : String(activeFilter)}
                  onChange={e => { setActiveFilter(e.target.value === '' ? null : e.target.value === 'true'); setPage(1); }}
                  className="px-3 py-2 text-sm border border-gray-200 dark:border-slate-700 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:bg-slate-900/50 dark:text-slate-200"
                >
                  <option value="">{t('integration.common.allStatuses')}</option>
                  <option value="true">{t('integration.common.enabled')}</option>
                  <option value="false">{t('integration.webhooks.disabled')}</option>
                </select>
              )}
            </div>
          </div>

          <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 shadow-sm overflow-hidden">
            <div className="hidden md:block overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-gray-50 dark:bg-slate-900 border-b border-gray-200 dark:border-slate-700">
                  <tr className="text-left text-gray-500 dark:text-slate-400">
                    <th className="px-5 py-3 font-medium text-xs uppercase">{t('integration.common.name')}</th>
                    <th className="px-5 py-3 font-medium text-xs uppercase">URL</th>
                    <th className="px-5 py-3 font-medium text-xs uppercase">{t('integration.webhooks.columns.method')}</th>
                    <th className="px-5 py-3 font-medium text-xs uppercase">{t('integration.webhooks.columns.event')}</th>
                    <th className="px-5 py-3 font-medium text-xs uppercase">{t('integration.common.status')}</th>
                    {listSubTab === 'active' && (
                      <th className="px-5 py-3 font-medium text-xs uppercase">{t('integration.webhooks.columns.triggerSuccessFail')}</th>
                    )}
                    <th className="px-5 py-3 text-right font-medium text-xs uppercase">{t('integration.common.actions')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100 dark:divide-slate-700/50">
                  {(listSubTab === 'active' ? webhooks : trashWebhooks).map(wh => (
                    <tr key={wh.id} className="hover:bg-gray-50 dark:hover:bg-slate-700/30">
                      <td className="px-5 py-3">
                        <div className="font-medium text-gray-900 dark:text-slate-100">{wh.name}</div>
                        <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
                          <span className="inline-flex items-center gap-1">
                            <Globe className="w-3 h-3" />
                            {wh.mailboxName || t('integration.webhooks.globalMailbox')}
                          </span>
                        </div>
                      </td>
                      <td className="px-5 py-3 text-gray-600 dark:text-slate-300 max-w-xs truncate" title={wh.url}>
                        <code className="text-xs bg-gray-100 dark:bg-slate-700 px-2 py-1 rounded">{wh.url}</code>
                      </td>
                      <td className="px-5 py-3">
                        <span className="px-2 py-0.5 text-xs font-medium bg-gray-100 dark:bg-slate-700 text-gray-700 dark:text-slate-300 rounded">
                          {wh.method}
                        </span>
                      </td>
                      <td className="px-5 py-3">
                        <div className="flex flex-wrap gap-1 max-w-xs">
                          {wh.events.slice(0, 2).map(ev => (
                            <span key={ev} className="px-1.5 py-0.5 text-xs bg-primary-50 text-primary-700 dark:bg-primary-900/30 dark:text-primary-400 rounded">
                              {ev}
                            </span>
                          ))}
                          {wh.events.length > 2 && (
                            <span className="px-1.5 py-0.5 text-xs text-gray-500 dark:text-slate-400">
                            +{wh.events.length - 2}
                            </span>
                          )}
                        </div>
                      </td>
                      <td className="px-5 py-3">
                        {listSubTab === 'active' ? (
                          <button
                            onClick={() => handleToggle(wh.id)}
                            className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${
                              wh.isActive ? 'bg-green-500' : 'bg-gray-300 dark:bg-slate-600'
                            }`}
                          >
                            <span
                              className={`inline-block h-4 w-4 transform rounded-full bg-white transition ${
                                wh.isActive ? 'translate-x-6' : 'translate-x-1'
                              }`}
                            />
                          </button>
                        ) : (
                          <span className="text-sm text-gray-400 dark:text-slate-500">{t('integration.common.deleted')}</span>
                        )}
                      </td>
                      {listSubTab === 'active' && (
                      <td className="px-5 py-3 text-gray-600 dark:text-slate-300">
                        <span className="text-gray-900 dark:text-slate-100 font-medium">{wh.triggerCount}</span>
                        <span className="text-gray-400 dark:text-slate-500 mx-1">/</span>
                        <span className="text-green-600">{wh.successCount}</span>
                        <span className="text-gray-400 dark:text-slate-500 mx-1">/</span>
                        <span className="text-red-600">{wh.failCount}</span>
                      </td>
                    )}
                      <td className="px-5 py-3">
                        <div className="flex items-center justify-end gap-1">
                          {listSubTab === 'active' ? (
                            <>
                              <button
                                onClick={() => handleTest(wh.id)}
                                disabled={testingId === wh.id}
                                className="p-1.5 text-gray-500 dark:text-slate-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/20 rounded-md transition disabled:opacity-50"
                                title={t('integration.webhooks.action.test')}
                              >
                                <Play className="w-4 h-4" />
                              </button>
                              <button
                                onClick={() => handleEdit(wh)}
                                className="p-1.5 text-gray-500 dark:text-slate-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/20 rounded-md transition"
                                title={t('common.edit')}
                              >
                                <Edit2 className="w-4 h-4" />
                              </button>
                              <button
                                onClick={() => setDeleteId(wh.id)}
                                className="p-1.5 text-gray-500 dark:text-slate-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-md transition"
                                title={t('common.delete')}
                              >
                                <Trash2 className="w-4 h-4" />
                              </button>
                            </>
                          ) : (
                            <>
                              <button
                                onClick={() => setRestoreId(wh.id)}
                                className="p-1.5 text-gray-500 dark:text-slate-400 hover:text-green-600 hover:bg-green-50 dark:hover:bg-green-900/20 rounded-md transition"
                                title={t('integration.common.restore')}
                              >
                                <CheckCircle2 className="w-4 h-4" />
                              </button>
                              <button
                                onClick={() => setHardDeleteId(wh.id)}
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
                  ))}
                  {(listSubTab === 'active' ? webhooks : trashWebhooks).length === 0 && (
                    <tr>
                      <td colSpan={7} className="px-5 py-12 text-center text-gray-400 dark:text-slate-500">
                        <Webhook className="w-12 h-12 mx-auto mb-3 opacity-30" />
                        {listSubTab === 'active' ? t('integration.webhooks.empty') : t('integration.common.trashEmpty')}
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>

            {/* 移动端卡片视图 */}
            <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
              {(listSubTab === 'active' ? webhooks : trashWebhooks).map(wh => (
                <div key={wh.id} className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700/30 space-y-3">
                  <div className="flex items-start justify-between gap-2">
                    <div className="flex items-center gap-2.5 min-w-0 flex-1">
                      <div className={`shrink-0 w-9 h-9 rounded-xl flex items-center justify-center ring-1 ${
                        listSubTab === 'trash'
                          ? 'bg-gray-100 dark:bg-slate-700/60 text-gray-500 dark:text-slate-400 ring-gray-300/60 dark:ring-slate-600/60'
                          : wh.isActive
                            ? 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-600 dark:text-emerald-400 ring-emerald-500/20'
                            : 'bg-gray-100 dark:bg-slate-700/60 text-gray-500 dark:text-slate-400 ring-gray-300/60 dark:ring-slate-600/60'
                      }`}>
                        <Webhook className="w-4 h-4" />
                      </div>
                      <div className="min-w-0">
                        <div className={`text-sm font-bold text-gray-900 dark:text-slate-100 truncate ${listSubTab === 'trash' ? 'line-through decoration-1 decoration-rose-300/60 dark:decoration-rose-800/50' : ''}`}>
                          {wh.name}
                        </div>
                        <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5 flex items-center gap-1">
                          <Globe className="w-3 h-3" />
                          <span className="truncate">{wh.mailboxName || t('integration.webhooks.globalMailbox')}</span>
                          <span className="opacity-40">·</span>
                          <span className="font-mono">{wh.method}</span>
                        </div>
                      </div>
                    </div>
                    {listSubTab === 'active' ? (
                      <button
                        onClick={() => handleToggle(wh.id)}
                        className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors flex-shrink-0 ${
                          wh.isActive ? 'bg-green-500' : 'bg-gray-300 dark:bg-slate-600'
                        }`}
                        title={wh.isActive ? t('integration.common.clickToDisable') : t('integration.common.clickToEnable')}
                      >
                        <span
                          className={`inline-block h-4 w-4 transform rounded-full bg-white transition ${
                            wh.isActive ? 'translate-x-6' : 'translate-x-1'
                          }`}
                        />
                      </button>
                    ) : (
                      <span className="text-[11px] text-gray-400 dark:text-slate-500 flex-shrink-0">{t('integration.common.deleted')}</span>
                    )}
                  </div>

                  <div className="text-xs text-gray-600 dark:text-slate-400 break-all">
                    <code className="text-[11px] bg-gray-100 dark:bg-slate-700 px-2 py-1 rounded font-mono">{wh.url}</code>
                  </div>

                  <div className="flex flex-wrap items-center gap-1.5">
                    {wh.events.slice(0, 3).map(ev => (
                      <span key={ev} className="px-1.5 py-0.5 text-[11px] bg-primary-50 text-primary-700 dark:bg-primary-900/30 dark:text-primary-400 rounded">
                        {ev}
                      </span>
                    ))}
                    {wh.events.length > 3 && (
                      <span className="px-1.5 py-0.5 text-[11px] text-gray-500 dark:text-slate-400">
                        +{wh.events.length - 3}
                      </span>
                    )}
                  </div>

                  {listSubTab === 'active' && (
                    <div className="flex items-center justify-between pt-1">
                      <div className="text-[11px] text-gray-500 dark:text-slate-400">
                        {t('integration.webhooks.triggerLabel')} <span className="font-bold text-gray-900 dark:text-slate-100">{wh.triggerCount}</span>
                        <span className="mx-1 opacity-40">·</span>
                        {t('integration.webhooks.success')} <span className="font-bold text-emerald-600">{wh.successCount}</span>
                        <span className="mx-1 opacity-40">·</span>
                        {t('integration.webhooks.failed')} <span className="font-bold text-rose-600">{wh.failCount}</span>
                      </div>
                      <div className="flex items-center gap-1">
                        <button
                          onClick={() => handleTest(wh.id)}
                          disabled={testingId === wh.id}
                          className="p-2 text-gray-500 dark:text-slate-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition disabled:opacity-50"
                          title={t('integration.webhooks.action.test')}
                        >
                          <Play className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => handleEdit(wh)}
                          className="p-2 text-gray-500 dark:text-slate-400 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-xl transition"
                          title={t('common.edit')}
                        >
                          <Edit2 className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => setDeleteId(wh.id)}
                          className="p-2 text-gray-500 dark:text-slate-400 hover:text-rose-600 hover:bg-rose-50 dark:hover:bg-rose-900/30 rounded-xl transition"
                          title={t('common.delete')}
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                    </div>
                  )}

                  {listSubTab === 'trash' && (
                    <div className="flex items-center justify-end gap-1 pt-1">
                      <button
                        onClick={() => setRestoreId(wh.id)}
                        className="p-2 text-gray-500 dark:text-slate-400 hover:text-emerald-600 hover:bg-emerald-50 dark:hover:bg-emerald-900/30 rounded-xl transition"
                        title={t('integration.common.restore')}
                      >
                        <CheckCircle2 className="w-4 h-4" />
                      </button>
                      <button
                        onClick={() => setHardDeleteId(wh.id)}
                        className="p-2 text-gray-500 dark:text-slate-400 hover:text-rose-600 hover:bg-rose-50 dark:hover:bg-rose-900/30 rounded-xl transition"
                        title={t('integration.common.hardDelete')}
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </div>
                  )}
                </div>
              ))}
              {(listSubTab === 'active' ? webhooks : trashWebhooks).length === 0 && (
                <div className="px-5 py-12 text-center text-gray-400 dark:text-slate-500">
                  <Webhook className="w-12 h-12 mx-auto mb-3 opacity-30" />
                  {listSubTab === 'active' ? t('integration.webhooks.empty') : t('integration.common.trashEmpty')}
                </div>
              )}
            </div>

            {totalPages > 1 && (
              <div className="flex items-center justify-between px-5 py-3 border-t border-gray-100 dark:border-slate-700/50">
                <span className="text-sm text-gray-500 dark:text-slate-400">
                  {t('integration.common.paginationTotal', { count: totalItems, page, total: totalPages })}
                </span>
                <div className="flex items-center gap-1">
                  <button
                    onClick={() => setPage(p => Math.max(1, p - 1))}
                    disabled={page === 1}
                    className="p-1.5 border border-gray-300 dark:border-slate-600 rounded-md text-sm disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50 dark:hover:bg-slate-700"
                  >
                    <ChevronLeft className="w-4 h-4" />
                  </button>
                  <span className="px-3 py-1 text-sm text-gray-600 dark:text-slate-300">
                    {page} / {totalPages}
                  </span>
                  <button
                    onClick={() => setPage(p => Math.min(totalPages, p + 1))}
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

      {activeTab === 'logs' && !loading && (
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 shadow-sm overflow-hidden">
          <div className="hidden md:block overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-gray-50 dark:bg-slate-900 border-b border-gray-200 dark:border-slate-700">
                <tr className="text-left text-gray-500 dark:text-slate-400">
                  <th className="px-5 py-3 font-medium text-xs uppercase">Webhook</th>
                  <th className="px-5 py-3 font-medium text-xs uppercase">{t('integration.webhooks.columns.event')}</th>
                  <th className="px-5 py-3 font-medium text-xs uppercase">{t('integration.webhooks.columns.statusCode')}</th>
                  <th className="px-5 py-3 font-medium text-xs uppercase">{t('integration.common.status')}</th>
                  <th className="px-5 py-3 font-medium text-xs uppercase">{t('integration.webhooks.columns.duration')}</th>
                  <th className="px-5 py-3 font-medium text-xs uppercase">{t('integration.webhooks.columns.time')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 dark:divide-slate-700/50">
                {logs.map(log => (
                  <tr key={log.id} className="hover:bg-gray-50 dark:hover:bg-slate-700/30">
                    <td className="px-5 py-3 text-gray-900 dark:text-slate-100 font-medium">{log.webhookName}</td>
                    <td className="px-5 py-3 text-gray-600 dark:text-slate-300">{log.eventType}</td>
                    <td className="px-5 py-3 text-gray-600 dark:text-slate-300">
                      {log.responseStatusCode ? (
                        <code className="px-2 py-0.5 bg-gray-100 dark:bg-slate-700 rounded text-xs">{log.responseStatusCode}</code>
                      ) : '-'}
                    </td>
                    <td className="px-5 py-3">
                      <span className={`inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-full ${
                        log.isSuccess
                          ? 'bg-green-50 text-green-700 dark:bg-green-900/30 dark:text-green-400'
                          : 'bg-red-50 text-red-700 dark:bg-red-900/30 dark:text-red-400'
                      }`}>
                        {log.isSuccess ? <CheckCircle2 className="w-3 h-3" /> : <XCircle className="w-3 h-3" />}
                        {log.isSuccess ? t('integration.webhooks.success') : t('integration.webhooks.failed')}
                      </span>
                      {log.errorMessage && (
                        <div className="text-xs text-red-500 mt-1">{log.errorMessage}</div>
                      )}
                    </td>
                    <td className="px-5 py-3 text-gray-600 dark:text-slate-300">
                      <span className="inline-flex items-center gap-1">
                        <Clock className="w-3.5 h-3.5 text-gray-400" />
                        {log.durationMs} ms
                      </span>
                    </td>
                    <td className="px-5 py-3 text-gray-500 dark:text-slate-400 whitespace-nowrap">
                      {new Date(log.createdAt).toLocaleString(toIntlLocale(language))}
                    </td>
                  </tr>
                ))}
                {logs.length === 0 && (
                  <tr>
                    <td colSpan={6} className="px-5 py-12 text-center text-gray-400 dark:text-slate-500">
                      <FileText className="w-12 h-12 mx-auto mb-3 opacity-30" />
                      {t('integration.webhooks.noLogs')}
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>

          {/* 移动端卡片视图 - 日志 */}
          <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
            {logs.map(log => (
              <div key={log.id} className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700/30 space-y-2.5">
                <div className="flex items-start justify-between gap-2">
                  <div className="flex items-center gap-2.5 min-w-0 flex-1">
                    <div className={`shrink-0 w-9 h-9 rounded-xl flex items-center justify-center ring-1 ${
                      log.isSuccess
                        ? 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-600 dark:text-emerald-400 ring-emerald-500/20'
                        : 'bg-rose-50 dark:bg-rose-900/30 text-rose-600 dark:text-rose-400 ring-rose-500/20'
                    }`}>
                      {log.isSuccess ? <CheckCircle2 className="w-4 h-4" /> : <XCircle className="w-4 h-4" />}
                    </div>
                    <div className="min-w-0">
                      <div className="text-sm font-bold text-gray-900 dark:text-slate-100 truncate">{log.webhookName}</div>
                      <div className="text-[11px] text-gray-400 dark:text-slate-500 mt-0.5">{log.eventType}</div>
                    </div>
                  </div>
                  <span className={`inline-flex items-center gap-1 px-2 py-0.5 text-[11px] font-bold rounded-full flex-shrink-0 ${
                    log.isSuccess
                      ? 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-400'
                      : 'bg-rose-50 dark:bg-rose-900/30 text-rose-700 dark:text-rose-400'
                  }`}>
                    {log.isSuccess ? t('integration.webhooks.success') : t('integration.webhooks.failed')}
                  </span>
                </div>

                {log.errorMessage && (
                  <div className="text-[11px] text-rose-600 dark:text-rose-400 bg-rose-50 dark:bg-rose-900/20 px-2.5 py-1.5 rounded-lg">
                    {log.errorMessage}
                  </div>
                )}

                <div className="flex items-center justify-between text-[11px] text-gray-500 dark:text-slate-400">
                  <div className="flex items-center gap-3">
                    <span className="inline-flex items-center gap-1">
                      <Clock className="w-3 h-3" />
                      {log.durationMs} ms
                    </span>
                    {log.responseStatusCode && (
                      <code className="px-1.5 py-0.5 bg-gray-100 dark:bg-slate-700 rounded font-mono">{log.responseStatusCode}</code>
                    )}
                  </div>
                  <span>{new Date(log.createdAt).toLocaleString(toIntlLocale(language))}</span>
                </div>
              </div>
            ))}
            {logs.length === 0 && (
              <div className="px-5 py-12 text-center text-gray-400 dark:text-slate-500">
                <FileText className="w-12 h-12 mx-auto mb-3 opacity-30" />
                {t('integration.webhooks.noLogs')}
              </div>
            )}
          </div>
          {totalLogPages > 1 && (
            <div className="flex items-center justify-between px-5 py-3 border-t border-gray-100 dark:border-slate-700/50">
              <span className="text-sm text-gray-500 dark:text-slate-400">
                {t('integration.common.paginationTotal', { count: logsTotal, page: logsPage, total: totalLogPages })}
              </span>
              <div className="flex items-center gap-1">
                <button
                  onClick={() => setLogsPage(p => Math.max(1, p - 1))}
                  disabled={logsPage === 1}
                  className="p-1.5 border border-gray-300 dark:border-slate-600 rounded-md text-sm disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50 dark:hover:bg-slate-700"
                >
                  <ChevronLeft className="w-4 h-4" />
                </button>
                <span className="px-3 py-1 text-sm text-gray-600 dark:text-slate-300">
                  {logsPage} / {totalLogPages}
                </span>
                <button
                  onClick={() => setLogsPage(p => Math.min(totalLogPages, p + 1))}
                  disabled={logsPage === totalLogPages}
                  className="p-1.5 border border-gray-300 dark:border-slate-600 rounded-md text-sm disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50 dark:hover:bg-slate-700"
                >
                  <ChevronRight className="w-4 h-4" />
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {showEditor && (
        <WebhookEditor
          webhook={editingWebhook}
          onClose={handleCloseEditor}
          onSave={handleSave}
        />
      )}

      {testResult?.show && (
        <Modal
          open={testResult.show}
          onClose={() => setTestResult(null)}
          title={t('integration.webhooks.testResult.title')}
          footer={
            <button
              onClick={() => setTestResult(null)}
              className="px-4 py-2 text-sm text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition"
            >
              {t('common.close')}
            </button>
          }
        >
          <div className="space-y-4">
            <div className={`p-4 rounded-lg ${testResult.success ? 'bg-green-50 dark:bg-green-900/20 text-green-800 dark:text-green-400' : 'bg-red-50 dark:bg-red-900/20 text-red-800 dark:text-red-400'}`}>
              <div className="font-medium flex items-center gap-2">
                {testResult.success ? <CheckCircle2 className="w-5 h-5" /> : <XCircle className="w-5 h-5" />}
                {testResult.success ? t('integration.webhooks.testResult.sentSuccess') : t('integration.webhooks.testResult.sentFailed')}
              </div>
              <div className="text-sm mt-1">{testResult.message}</div>
            </div>
            <div className="space-y-2.5 text-sm">
              <div className="flex justify-between items-center">
                <span className="text-gray-500 dark:text-slate-400">{t('integration.webhooks.columns.statusCode')}</span>
                <span className="font-medium text-gray-900 dark:text-slate-100">{testResult.statusCode || '-'}</span>
              </div>
              <div className="flex justify-between items-center">
                <span className="text-gray-500 dark:text-slate-400">{t('integration.webhooks.columns.duration')}</span>
                <span className="font-medium text-gray-900 dark:text-slate-100">{testResult.durationMs} ms</span>
              </div>
              {testResult.responseBody && (
                <div>
                  <div className="text-gray-500 dark:text-slate-400 mb-1.5">{t('integration.webhooks.testResult.responseBody')}</div>
                  <pre className="bg-gray-50 dark:bg-slate-900 p-3 rounded-lg text-xs overflow-auto max-h-32 text-gray-700 dark:text-slate-300">
                    {testResult.responseBody}
                  </pre>
                </div>
              )}
            </div>
          </div>
        </Modal>
      )}

      <TrashDialogs<number>
        itemLabel={t('integration.webhooks.title')}
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

function WebhookEditor({
  webhook,
  onClose,
  onSave,
}: {
  webhook: WebhookDto | null;
  onClose: () => void;
  onSave: (data: CreateWebhookRequest) => void;
}) {
  const { t } = useI18n();
  const [name, setName] = useState(webhook?.name || '');
  const [url, setUrl] = useState(webhook?.url || '');
  const [method, setMethod] = useState(webhook?.method || 'POST');
  const [events, setEvents] = useState<string[]>(webhook?.events || []);
  const [headersStr, setHeadersStr] = useState(
    webhook ? JSON.stringify(webhook.headers, null, 2) : '{}',
  );
  const [secret, setSecret] = useState(webhook?.hasSecret ? '••••••••' : '');
  const [isActive, setIsActive] = useState(webhook?.isActive ?? true);
  const [mailboxId, setMailboxId] = useState<number | ''>(
    webhook?.mailboxId ?? '');
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  function toggleEvent(ev: string) {
    setEvents(prev =>
      prev.includes(ev) ? prev.filter(e => e !== ev) : [...prev, ev],
    );
  }

  async function handleSubmit() {
    setError('');
    if (!name.trim()) return setError(t('integration.webhooks.editor.errNameRequired'));
    if (!url.trim()) return setError(t('integration.webhooks.editor.errUrlRequired'));
    if (events.length === 0) return setError(t('integration.webhooks.editor.errEventsRequired'));

    let headers: Record<string, string> = {};
    try {
      const parsed = JSON.parse(headersStr);
      if (typeof parsed === 'object' && parsed !== null) {
        // T000105：请求头值必须是字符串。若含数字/布尔等类型，后端 Dictionary<string,string>
        // 反序列化会整体失败并报"the request field is required"这类晦涩错误，这里提前拦截并提示。
        for (const [k, v] of Object.entries(parsed as Record<string, unknown>)) {
          if (typeof v !== 'string') {
            return setError(t('integration.webhooks.editor.errHeaderValueType', { key: k, type: typeof v }));
          }
        }
        headers = parsed as Record<string, string>;
      }
    } catch {
      return setError(t('integration.webhooks.editor.errHeadersJson'));
    }

    setSaving(true);
    try {
      await onSave({
        name: name.trim(),
        url: url.trim(),
        method,
        events,
        headers,
        secret: secret === '••••••••' ? null : (secret || null),
        isActive,
        mailboxId: mailboxId === '' ? null : mailboxId,
      });
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : t('integration.common.saveFailed'));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={webhook ? t('integration.webhooks.editor.editTitle') : t('integration.webhooks.newWebhook')}
      size="lg"
      footer={
        <>
          <button
            onClick={onClose}
            disabled={saving}
            className="px-4 py-2 text-sm text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition disabled:opacity-50"
          >
            {t('common.cancel')}
          </button>
          <button
            onClick={handleSubmit}
            disabled={saving}
            className="px-4 py-2 text-sm bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white rounded-lg transition"
          >
            {saving ? t('integration.common.saving') : webhook ? t('common.save') : t('integration.webhooks.editor.create')}
          </button>
        </>
      }
    >
      <div className="space-y-4">
        {error && (
          <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-400 px-3 py-2 rounded-lg text-sm flex items-center gap-2">
            <AlertCircle className="w-4 h-4 flex-shrink-0" />
            {error}
          </div>
        )}

        <div>
          <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
            {t('integration.common.name')} <span className="text-red-500">*</span>
          </label>
          <input
            type="text"
            value={name}
            onChange={e => setName(e.target.value)}
            className="w-full px-3 py-2 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:bg-slate-900/50 dark:text-slate-200"
            placeholder={t('integration.webhooks.editor.namePlaceholder')}
          />
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
              {t('integration.webhooks.editor.callbackUrl')} <span className="text-red-500">*</span>
            </label>
            <input
              type="url"
              value={url}
              onChange={e => setUrl(e.target.value)}
              className="w-full px-3 py-2 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:bg-slate-900/50 dark:text-slate-200"
              placeholder="https://..."
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
              {t('integration.webhooks.editor.method')}
            </label>
            <select
              value={method}
              onChange={e => setMethod(e.target.value)}
              className="w-full px-3 py-2 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:bg-slate-900/50 dark:text-slate-200"
            >
              {METHOD_OPTIONS.map(m => (
                <option key={m} value={m}>{m}</option>
              ))}
            </select>
          </div>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-2">
            {t('integration.webhooks.editor.events')} <span className="text-red-500">*</span>
          </label>
          <div className="space-y-3 max-h-48 overflow-y-auto border border-gray-200 dark:border-slate-700 rounded-lg p-3">
            {EVENT_GROUPS.map((group) => (
              <div key={group.categoryKey}>
                <div className="text-xs font-medium text-gray-500 dark:text-slate-400 mb-1.5">{t(group.categoryKey)}</div>
                <div className="flex flex-wrap gap-2">
                  {group.events.map(ev => (
                    <button
                      key={ev.value}
                      type="button"
                      onClick={() => toggleEvent(ev.value)}
                      className={`px-3 py-1 text-xs rounded-full border transition-colors ${
                        events.includes(ev.value)
                          ? 'bg-primary-50 border-primary-300 text-primary-700 dark:bg-primary-900/30 dark:border-primary-700 dark:text-primary-400'
                          : 'bg-white dark:bg-slate-800 border-gray-200 dark:border-slate-600 text-gray-600 dark:text-slate-400 hover:border-gray-300 dark:hover:border-slate-500'
                      }`}
                    >
                      {t(ev.labelKey)}
                    </button>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
            {t('integration.webhooks.editor.headers')}
          </label>
          <textarea
            value={headersStr}
            onChange={e => setHeadersStr(e.target.value)}
            rows={4}
            className="w-full px-3 py-2 border border-gray-300 dark:border-slate-600 rounded-lg font-mono text-xs focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:bg-slate-900/50 dark:text-slate-200"
            placeholder='{"Authorization": "Bearer xxx"}'
          />
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
            {t('integration.webhooks.editor.secret')}
          </label>
          <input
            type="password"
            value={secret}
            onChange={e => setSecret(e.target.value)}
            className="w-full px-3 py-2 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:bg-slate-900/50 dark:text-slate-200"
            placeholder={t('integration.webhooks.editor.secretPlaceholder')}
          />
          <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">
            {t('integration.webhooks.editor.secretHint')}
          </p>
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
              {t('integration.webhooks.editor.mailbox')}
            </label>
            <select
              value={String(mailboxId)}
              onChange={e => setMailboxId(e.target.value === '' ? '' : Number(e.target.value))}
              className="w-full px-3 py-2 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 dark:bg-slate-900/50 dark:text-slate-200"
            >
              <option value="">{t('integration.webhooks.editor.mailboxAllOption')}</option>
              {/* 这两个是**邮箱名（数据）**，不随界面语言变化 ⇒ 保留可读字面量并登记为接受例外
                  （波次 14 队长裁定：翻成 Technical support / Sales 会让英文界面显示一个
                  与邮箱实际名称、与其它页面渲染的 DB 名称都不一致的标签）。
                  同时登记一处既有缺陷：这里的 mailboxId 写死 1/2、未走邮箱列表 API —— 修它属
                  另一件事，修好后这两项应改为遍历真实邮箱列表，本例外随之消失。 */}
              {/* i18n-exempt-block-start: 邮箱名（数据），不随界面语言变化，保留可读字面量 */}
              <option value="1">技术支持</option>
              <option value="2">销售咨询</option>
              {/* i18n-exempt-block-end */}
            </select>
          </div>
          <div className="flex items-end">
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={isActive}
                onChange={e => setIsActive(e.target.checked)}
                className="w-4 h-4 text-primary-600 rounded focus:ring-primary-500"
              />
              <span className="text-sm text-gray-700 dark:text-slate-300">{t('integration.webhooks.editor.enableWebhook')}</span>
            </label>
          </div>
        </div>
      </div>
    </Modal>
  );
}
