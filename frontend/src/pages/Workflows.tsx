import { useCallback, useState, useEffect, useRef } from 'react';
import { workflowApi, mailboxApi, userApi, mailTemplateApi, tagApi, macroApi } from '../api';
import { useTimedToast } from '../hooks/useTimedToast';
import { useI18n } from '../context/I18nContext';
import { tBound, toIntlLocale } from '../i18n/locale';
import { genId } from '../utils/id';
import { formatDate, formatDateTime } from '../utils/format';
import { getApiErrorMessage } from '../utils/error';
import type { TranslationKey } from '../locales/zh-Hans';
import type { TranslateParams } from '../i18n/types';
import type {
  WorkflowDto,
  WorkflowOverviewResponse,
  WorkflowQuery,
  WorkflowCondition,
  WorkflowActionDto,
  WorkflowExecutionLogDto,
  CreateWorkflowRequest,
  WorkflowTestResponse,
  MailTemplateDto,
  MailboxResponse,
  TagResponse,
  MacroDto,
} from '../api/types';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import {
  Edit2,
  Trash2,
  Search,
  Plus,
  Settings,
  Activity,
  CheckCircle2,
  XCircle,
  Layers,
  Zap,
  BookOpen,
  AlertCircle,
  ChevronLeft,
  ChevronRight,
  RotateCcw,
  Clock,
  ChevronDown,
  ChevronUp,
  Filter,
  RefreshCcw,
  MessageSquare,
  Workflow,
  Ban,
  XOctagon,
  AlertTriangle,
} from 'lucide-react';

/** 与 `I18nContext` 的 `t` 签名一致；模块级纯函数需显式传入 `t` */
type TranslateFn = (key: TranslationKey, params?: TranslateParams) => string;

/** 动作参数里的状态选项：`value` 是提交给后端的稳定数字（`ConversationStatus`） */
const STATUS_OPTIONS: { value: number; labelKey: TranslationKey }[] = [
  { value: 1, labelKey: 'format.status.active' },
  { value: 2, labelKey: 'format.status.pending' },
  { value: 3, labelKey: 'format.status.closed' },
  { value: 4, labelKey: 'format.status.spam' },
];

/** 动作参数里的优先级选项（0=Low…3=Urgent） */
const PRIORITY_OPTIONS: { value: number; labelKey: TranslationKey }[] = [
  { value: 0, labelKey: 'format.priority.low' },
  { value: 1, labelKey: 'format.priority.normal' },
  { value: 2, labelKey: 'format.priority.high' },
  { value: 3, labelKey: 'format.priority.urgent' },
];

/**
 * 触发类型下拉项。**必须与后端 `WorkflowService.GetOverviewAsync` 的 `TriggerOptions` 同集合同顺序**
 * （13 码；`agent_replied` 后端有标签但**不在**选项里，见下面的标签表）。
 * `value` 是稳定 code，`labelKey` 只持有真源 key。
 */
const WORKFLOW_TRIGGER_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: 'new_conversation', labelKey: 'automation.workflowTrigger.newConversation' },
  { value: 'new_reply', labelKey: 'automation.workflowTrigger.newReply' },
  { value: 'status_changed', labelKey: 'automation.workflowTrigger.statusChanged' },
  { value: 'assignee_changed', labelKey: 'automation.workflowTrigger.assigneeChanged' },
  { value: 'conversation_closed', labelKey: 'automation.workflowTrigger.conversationClosed' },
  { value: 'conversation_reopened', labelKey: 'automation.workflowTrigger.conversationReopened' },
  { value: 'tag_added', labelKey: 'automation.workflowTrigger.tagAdded' },
  { value: 'tag_removed', labelKey: 'automation.workflowTrigger.tagRemoved' },
  { value: 'satisfaction_submitted', labelKey: 'automation.workflowTrigger.satisfactionSubmitted' },
  { value: 'note_added', labelKey: 'automation.workflowTrigger.noteAdded' },
  { value: 'priority_changed', labelKey: 'automation.workflowTrigger.priorityChanged' },
  { value: 'chat_close_requested', labelKey: 'automation.workflowTrigger.chatCloseRequested' },
  { value: 'chat_satisfaction_submitted', labelKey: 'automation.workflowTrigger.chatSatisfactionSubmitted' },
];

/**
 * 触发类型 code → 文案 key。**覆盖后端 `WorkflowConst.GetTriggerLabel` 的全部 14 码**：
 * 比下拉项多一条 `agent_replied`（该码有标签、无下拉项、开发库内也无数据），
 * 属于"潜伏码"——不进下拉项，但历史/脏数据出现时不会把原始 code 渲染到界面。
 * 未知 code 回落原始 code（与后端 `_ => trigger` 一致）。
 */
const WORKFLOW_TRIGGER_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  ...Object.fromEntries(WORKFLOW_TRIGGER_OPTIONS.map((o) => [o.value, o.labelKey] as const)),
  agent_replied: 'automation.workflowTrigger.agentReplied',
};

/** 触发条件字段 code → 文案 key（后端 `GetFieldLabel` 13 码 = `FieldOptions` 13 项，无缺无余） */
const WORKFLOW_FIELD_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  subject: 'automation.workflowField.subject',
  from_email: 'automation.workflowField.fromEmail',
  to_email: 'automation.workflowField.toEmail',
  body: 'automation.workflowField.body',
  customer_name: 'automation.workflowField.customerName',
  status: 'automation.workflowField.status',
  priority: 'automation.workflowField.priority',
  tag: 'automation.workflowField.tag',
  mailbox: 'automation.workflowField.mailbox',
  assignee: 'automation.workflowField.assignee',
  satisfaction_rating: 'automation.workflowField.satisfactionRating',
  satisfaction_comment: 'automation.workflowField.satisfactionComment',
  source_type: 'automation.workflowField.sourceType',
};

/** 触发条件运算符 code → 文案 key（后端 `GetOperatorLabel` 7 码 = `OperatorOptions` 7 项） */
const WORKFLOW_OPERATOR_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  contains: 'automation.workflowOperator.contains',
  not_contains: 'automation.workflowOperator.notContains',
  equals: 'automation.workflowOperator.equals',
  not_equals: 'automation.workflowOperator.notEquals',
  starts_with: 'automation.workflowOperator.startsWith',
  ends_with: 'automation.workflowOperator.endsWith',
  regex: 'automation.workflowOperator.regex',
};

/** 动作类型 code → 文案 key（后端 `GetActionLabel` 13 码 = `ActionTypeOptions` 13 项） */
const WORKFLOW_ACTION_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  set_status: 'automation.workflowAction.setStatus',
  assign_user: 'automation.action.assignUser',
  add_tag: 'automation.action.addTag',
  remove_tag: 'automation.action.removeTag',
  send_reply: 'automation.action.sendReply',
  set_priority: 'automation.action.setPriority',
  forward: 'automation.workflowAction.forward',
  send_email: 'automation.workflowAction.sendEmail',
  send_im_message: 'automation.workflowAction.sendImMessage',
  conversation_process: 'automation.workflowAction.conversationProcess',
  trigger_webhook: 'automation.workflowAction.triggerWebhook',
  execute_macro: 'automation.workflowAction.executeMacro',
  send_chat_message: 'automation.workflowAction.sendChatMessage',
};

/**
 * 下拉框的选项顺序。对象字面量的插入顺序即后端 `FieldOptions` / `OperatorOptions` /
 * `ActionTypeOptions` 的顺序，故直接用 `Object.keys` 派生，避免再抄一份可能漂移的数组。
 */
const WORKFLOW_FIELD_CODES = Object.keys(WORKFLOW_FIELD_LABEL_KEYS);
const WORKFLOW_OPERATOR_CODES = Object.keys(WORKFLOW_OPERATOR_LABEL_KEYS);
const WORKFLOW_ACTION_CODES = Object.keys(WORKFLOW_ACTION_LABEL_KEYS);

/**
 * 执行日志状态筛选条。`value` 为提交给后端的状态码（null = 全部），`labelKey` 只持有真源 key。
 */
const LOG_STATUS_FILTERS: { value: string | null; labelKey: TranslationKey }[] = [
  { value: null, labelKey: 'automation.workflows.logs.filterAll' },
  { value: 'success', labelKey: 'automation.common.statusSuccess' },
  { value: 'partial', labelKey: 'automation.common.statusPartial' },
  { value: 'not_matched', labelKey: 'automation.common.statusNotMatched' },
  { value: 'failed', labelKey: 'automation.common.statusFailed' },
];

/**
 * 执行日志状态 code → 文案 key（后端 `WorkflowService.ToLogDto` 识别 4 码）。
 * 可达性：`success` / `failed` / `not_matched` 有产出方（开发库 77 / 346 / 430 条）；
 * `partial` 只在后端标签开关里存在、**无任何产出方**（`DashboardService` 会读该码，
 * 故保留映射，避免历史数据渲染原始 code）。
 */
const WORKFLOW_LOG_STATUS_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  success: 'automation.common.statusSuccess',
  partial: 'automation.common.statusPartial',
  failed: 'automation.common.statusFailed',
  not_matched: 'automation.common.statusNotMatched',
};

/** 通用"按 code 查表，未知回落 code" */
const labelByCode = (t: TranslateFn, table: Record<string, TranslationKey | undefined>, code: string) => {
  const key = table[code];
  return key ? t(key) : code;
};

const workflowTriggerLabel = (t: TranslateFn, code: string) => labelByCode(t, WORKFLOW_TRIGGER_LABEL_KEYS, code);
const workflowLogStatusLabel = (t: TranslateFn, code: string) => labelByCode(t, WORKFLOW_LOG_STATUS_LABEL_KEYS, code);
const workflowFieldLabel = (t: TranslateFn, code: string) => labelByCode(t, WORKFLOW_FIELD_LABEL_KEYS, code);
const workflowOperatorLabel = (t: TranslateFn, code: string) => labelByCode(t, WORKFLOW_OPERATOR_LABEL_KEYS, code);
const workflowActionLabel = (t: TranslateFn, code: string) => labelByCode(t, WORKFLOW_ACTION_LABEL_KEYS, code);

interface PagedList<T> {
  data: T[];
  totalItems: number;
  totalPages: number;
  currentPage: number;
}

// 触发类型对应的颜色徽章样式
function getTriggerBadgeClass(trigger: string): string {
  switch (trigger) {
    case 'new_conversation':
      return 'bg-blue-50 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300 ring-1 ring-blue-200/60 dark:ring-blue-700/40';
    case 'new_reply':
      return 'bg-cyan-50 dark:bg-cyan-900/30 text-cyan-700 dark:text-cyan-300 ring-1 ring-cyan-200/60 dark:ring-cyan-700/40';
    case 'status_changed':
      return 'bg-amber-50 dark:bg-amber-900/30 text-amber-700 dark:text-amber-300 ring-1 ring-amber-200/60 dark:ring-amber-700/40';
    case 'assignee_changed':
      return 'bg-violet-50 dark:bg-violet-900/30 text-violet-700 dark:text-violet-300 ring-1 ring-violet-200/60 dark:ring-violet-700/40';
    case 'conversation_closed':
      return 'bg-slate-50 dark:bg-slate-700/40 text-slate-700 dark:text-slate-300 ring-1 ring-slate-200/60 dark:ring-slate-600/40';
    case 'conversation_reopened':
      return 'bg-green-50 dark:bg-green-900/30 text-green-700 dark:text-green-300 ring-1 ring-green-200/60 dark:ring-green-700/40';
    case 'tag_added':
      return 'bg-pink-50 dark:bg-pink-900/30 text-pink-700 dark:text-pink-300 ring-1 ring-pink-200/60 dark:ring-pink-700/40';
    case 'tag_removed':
      return 'bg-rose-50 dark:bg-rose-900/30 text-rose-700 dark:text-rose-300 ring-1 ring-rose-200/60 dark:ring-rose-700/40';
    case 'satisfaction_submitted':
      return 'bg-indigo-50 dark:bg-indigo-900/30 text-indigo-700 dark:text-indigo-300 ring-1 ring-indigo-200/60 dark:ring-indigo-700/40';
    case 'note_added':
      return 'bg-teal-50 dark:bg-teal-900/30 text-teal-700 dark:text-teal-300 ring-1 ring-teal-200/60 dark:ring-teal-700/40';
    case 'priority_changed':
      return 'bg-orange-50 dark:bg-orange-900/30 text-orange-700 dark:text-orange-300 ring-1 ring-orange-200/60 dark:ring-orange-700/40';
    default:
      return 'bg-gray-50 dark:bg-slate-700/40 text-gray-700 dark:text-slate-300 ring-1 ring-gray-200/60 dark:ring-slate-600/40';
  }
}

export default function Workflows() {
  const { t, language } = useI18n();
  const [query, setQuery] = useState<WorkflowQuery>({ page: 1, pageSize: 20 });
  const [list, setList] = useState<PagedList<WorkflowDto> | null>(null);
  const [overview, setOverview] = useState<WorkflowOverviewResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [mailboxes, setMailboxes] = useState<{ id: number; name: string }[]>([]);
  const [users, setUsers] = useState<{ id: number; fullName: string }[]>([]);

  const [editing, setEditing] = useState<WorkflowDto | null>(null);
  const [showEditor, setShowEditor] = useState(false);
  const [showLogs, setShowLogs] = useState(false);
  const [logs, setLogs] = useState<PagedList<WorkflowExecutionLogDto> | null>(null);
  const [logsLoading, setLogsLoading] = useState(false);
  const [logsQuery, setLogsQuery] = useState<{
    page: number;
    pageSize: number;
    status: string | null;
    keyword: string;
  }>({ page: 1, pageSize: 20, status: null, keyword: '' });
  const [logExpanded, setLogExpanded] = useState<Record<number, boolean>>({});

  const [testModal, setTestModal] = useState<{ id: number; name: string; actions: WorkflowActionDto[] } | null>(null);
  const [testInput, setTestInput] = useState({ subject: '', fromEmail: '', body: '' });
  const [testResult, setTestResult] = useState<WorkflowTestResponse | null>(null);
  const [testing, setTesting] = useState(false);

  const [trashOpen, setTrashOpen] = useState(false);
  const [trashQuery, setTrashQuery] = useState<{ page: number; pageSize: number; keyword: string; mailboxId: number | null }>({ page: 1, pageSize: 10, keyword: '', mailboxId: null });
  const [trashList, setTrashList] = useState<{ data: WorkflowDto[]; totalItems: number; totalPages: number; currentPage: number } | null>(null);
  const [trashLoading, setTrashLoading] = useState(false);
  const [trashCount, setTrashCount] = useState(0);
  const [restoreTarget, setRestoreTarget] = useState<WorkflowDto | null>(null);
  const [hardDeleteTarget, setHardDeleteTarget] = useState<WorkflowDto | null>(null);
  const [clearTrashOpen, setClearTrashOpen] = useState(false);
  const [trashActionLoading, setTrashActionLoading] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<WorkflowDto | null>(null);
  const [deleting, setDeleting] = useState(false);

  const { message: toast, show: showToast } = useTimedToast();

  // 主列表关键词搜索：缓冲值即时回显 + 650ms 防抖自动搜索 + 回车即时触发（与 ApiKeys 一致）
  const [keywordInput, setKeywordInput] = useState('');
  const keywordTimerRef = useRef<number | null>(null);
  const applyKeyword = useCallback((value: string) => {
    setQuery((q) => ({ ...q, keyword: value, page: 1 }));
  }, []);

  const handleKeywordInput = useCallback((value: string) => {
    setKeywordInput(value);
    // 输入停顿 650ms 后自动搜索（期间继续输入会重置计时器）
    if (keywordTimerRef.current !== null) window.clearTimeout(keywordTimerRef.current);
    keywordTimerRef.current = window.setTimeout(() => applyKeyword(value), 650);
  }, [applyKeyword]);

  const triggerKeywordNow = useCallback(() => {
    if (keywordTimerRef.current !== null) {
      window.clearTimeout(keywordTimerRef.current);
      keywordTimerRef.current = null;
    }
    applyKeyword(keywordInput);
  }, [applyKeyword, keywordInput]);

  // 垃圾箱关键词搜索：同样 650ms 防抖 + 回车即时
  const [trashKeywordInput, setTrashKeywordInput] = useState('');
  const trashKeywordTimerRef = useRef<number | null>(null);
  const applyTrashKeyword = useCallback((value: string) => {
    setTrashQuery((q) => ({ ...q, keyword: value, page: 1 }));
  }, []);

  const handleTrashKeywordInput = useCallback((value: string) => {
    setTrashKeywordInput(value);
    if (trashKeywordTimerRef.current !== null) window.clearTimeout(trashKeywordTimerRef.current);
    trashKeywordTimerRef.current = window.setTimeout(() => applyTrashKeyword(value), 650);
  }, [applyTrashKeyword]);

  const triggerTrashKeywordNow = useCallback(() => {
    if (trashKeywordTimerRef.current !== null) {
      window.clearTimeout(trashKeywordTimerRef.current);
      trashKeywordTimerRef.current = null;
    }
    applyTrashKeyword(trashKeywordInput);
  }, [applyTrashKeyword, trashKeywordInput]);

  // 打开垃圾箱时重置关键词缓冲（query 会被重置为空，需同步避免回显残留）
  const resetTrashKeywordSearch = useCallback(() => {
    if (trashKeywordTimerRef.current !== null) {
      window.clearTimeout(trashKeywordTimerRef.current);
      trashKeywordTimerRef.current = null;
    }
    setTrashKeywordInput('');
  }, []);

  useEffect(() => {
    return () => {
      if (keywordTimerRef.current !== null) window.clearTimeout(keywordTimerRef.current);
      if (trashKeywordTimerRef.current !== null) window.clearTimeout(trashKeywordTimerRef.current);
    };
  }, []);

  useEffect(() => {
    let destroyed = false;
    workflowApi
      .overview()
      .then((r) => { if (!destroyed) setOverview(r); })
      .catch((err: unknown) => console.error('加载工作流概览失败:', err));
    workflowApi
      .getTrashCount()
      .then((r: { count?: number }) => { if (!destroyed) setTrashCount(r?.count ?? 0); })
      .catch(() => {});
    mailboxApi
      .listAll()
      .then((res: MailboxResponse[]) => {
        if (!destroyed) setMailboxes(Array.isArray(res) ? res.map((m) => ({ id: m.id, name: m.name })) : []);
      })
      .catch((err: unknown) => console.error('加载邮箱列表失败:', err));
    userApi
      .list()
      .then((res) => {
        if (destroyed) return;
        setUsers(
          Array.isArray(res.data)
            ? res.data.map((u) => ({ id: u.id, fullName: u.fullName || `${u.firstName} ${u.lastName}`.trim() }))
            : [],
        );
      })
      .catch((err: unknown) => console.error('加载用户列表失败:', err));
    return () => { destroyed = true; };
  }, []);

  useEffect(() => {
    let destroyed = false;
    setLoading(true);
    workflowApi
      .list(query)
      .then((res: PagedList<WorkflowDto>) => { if (!destroyed) setList(res); })
      .catch((err: unknown) => console.error('加载工作流列表失败:', err))
      .finally(() => { if (!destroyed) setLoading(false); });
    return () => { destroyed = true; };
  }, [query]);

  const refreshOverview = () => {
    workflowApi
      .overview()
      .then(setOverview)
      .catch((err: unknown) => console.error('加载工作流概览失败:', err));
  };

  const refreshList = () => {
    setQuery((q) => ({ ...q }));
  };

  const handleFilterChange = <K extends keyof WorkflowQuery>(key: K, value: WorkflowQuery[K]) => {
    setQuery((q) => ({ ...q, [key]: value, page: 1 }));
  };

  const handlePageChange = (page: number) => {
    setQuery((q) => ({ ...q, page }));
  };

  const renderWorkflowRow = (w: WorkflowDto) => (
    <tr key={w.id} className="hover:bg-gray-50 dark:hover:bg-slate-700">
      <td className="px-4 py-3">
        <div className="flex items-center gap-2">
          <span className="text-sm font-medium text-gray-900 dark:text-slate-100">{w.name}</span>
          {w.isSystem && (
            <span className="text-xs px-2 py-0.5 rounded-full bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-slate-300 font-medium">
              {t('automation.workflows.list.systemBadge')}
            </span>
          )}
        </div>
        {w.description && <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-1">{w.description}</div>}
      </td>
      <td className="px-4 py-3">
        <span className={`text-xs px-2 py-1 rounded-full font-medium ${getTriggerBadgeClass(w.triggerType)}`}>
          {workflowTriggerLabel(t, w.triggerType)}
        </span>
      </td>
      <td className="px-4 py-3 text-sm text-gray-700 dark:text-slate-300">
        {!w.mailboxId ? <span className="text-gray-400 dark:text-slate-500">{t('automation.common.allMailboxes')}</span> : w.mailboxName ?? `#${w.mailboxId}`}
      </td>
      <td className="px-4 py-3">
        <div className="text-xs text-gray-600 dark:text-slate-400">
          <span className="text-gray-500 dark:text-slate-400">{t('automation.workflows.list.conditionsLabel')}</span>
          {t('automation.common.countSuffix', { count: w.conditions?.conditions?.length ?? 0 })}
        </div>
        <div className="text-xs text-gray-600 dark:text-slate-400 mt-0.5">
          <span className="text-gray-500 dark:text-slate-400">{t('automation.workflows.list.actionsLabel')}</span>
          {t('automation.common.countSuffix', { count: w.actions.length })}
        </div>
      </td>
      <td className="px-4 py-3">
        <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{w.matchCount}</div>
        {w.lastMatchedAt && (
          <div className="text-xs text-gray-400 dark:text-slate-500">{formatDate(w.lastMatchedAt)}</div>
        )}
      </td>
      <td className="px-4 py-3">
        <button
          onClick={() => handleToggleActive(w)}
          className={`relative inline-flex h-6 w-11 items-center rounded-full transition ${w.isActive ? 'bg-green-500' : 'bg-gray-300'}`}
        >
          <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition ${w.isActive ? 'translate-x-6' : 'translate-x-1'}`} />
        </button>
      </td>
      <td className="px-4 py-3 text-right">
        <div className="flex items-center justify-end gap-1">
          <button
            onClick={() => setTestModal({ id: w.id, name: w.name, actions: w.actions })}
            className="p-2 text-purple-600 hover:bg-purple-50 rounded-lg transition-colors"
            title={t('automation.workflows.list.testTooltip')}
          >
            <Zap className="w-4 h-4" />
          </button>
          <button
            onClick={() => {
              setEditing(w);
              setShowEditor(true);
            }}
            className="p-2 text-blue-600 hover:bg-blue-50 rounded-lg transition-colors"
            title={t('common.edit')}
          >
            <Edit2 className="w-4 h-4" />
          </button>
          <button
            onClick={() => {
              if (w.isSystem) return;
              setDeleteTarget(w);
            }}
            className={`p-2 rounded-lg transition-colors ${
              w.isSystem
                ? 'text-gray-300 dark:text-slate-600 cursor-not-allowed'
                : 'text-red-600 hover:bg-red-50'
            }`}
            disabled={w.isSystem}
            title={w.isSystem ? t('automation.workflows.list.systemNotDeletable') : t('common.delete')}
          >
            <Trash2 className="w-4 h-4" />
          </button>
        </div>
      </td>
    </tr>
  );

  const handleToggleActive = async (w: WorkflowDto) => {
    try {
      await workflowApi.toggleActive(w.id);
      showToast({ type: 'success', text: t(w.isActive ? 'automation.common.toggleDisabled' : 'automation.common.toggleEnabled', { name: w.name }) });
      refreshOverview();
      refreshList();
    } catch {
      showToast({ type: 'error', text: t('automation.common.operationFailed') });
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await workflowApi.remove(deleteTarget.id);
      showToast({ type: 'success', text: t('automation.workflows.movedToTrash') });
      setDeleteTarget(null);
      refreshOverview();
      refreshTrashCount();
      refreshList();
    } catch (err: unknown) {
      showToast({ type: 'error', text: getApiErrorMessage(err, t('automation.common.deleteFailed')) });
    } finally {
      setDeleting(false);
    }
  };

  const refreshTrashCount = () => {
    workflowApi.getTrashCount().then((r: { count?: number }) => setTrashCount(r?.count ?? 0)).catch(() => {});
  };

  const refreshTrashList = useCallback(() => {
    setTrashLoading(true);
    workflowApi.getTrash(trashQuery.page, trashQuery.pageSize, trashQuery.mailboxId ?? undefined, trashQuery.keyword || undefined)
      .then((res: PagedList<WorkflowDto>) => setTrashList(res))
      // `tBound` 而非依赖 `t`：本回调是下面 effect 的依赖，换 `t` 身份会让垃圾箱列表多请求一次。
      .catch(() => showToast({ type: 'error', text: tBound('automation.common.loadTrashFailed') }))
      .finally(() => setTrashLoading(false));
  }, [trashQuery, showToast]);

  const openTrashModal = () => {
    setTrashOpen(true);
    resetTrashKeywordSearch();
    setTrashQuery({ page: 1, pageSize: 10, keyword: '', mailboxId: null });
    refreshTrashCount();
  };

  useEffect(() => {
    if (trashOpen) refreshTrashList();
  }, [trashOpen, trashQuery, refreshTrashList]);

  const handleRestore = async () => {
    if (!restoreTarget) return;
    setTrashActionLoading(true);
    try {
      await workflowApi.restore(restoreTarget.id);
      showToast({ type: 'success', text: t('automation.workflows.restored', { name: restoreTarget.name }) });
      setRestoreTarget(null);
      refreshTrashList();
      refreshTrashCount();
      refreshOverview();
      refreshList();
    } catch (err: unknown) {
      showToast({ type: 'error', text: getApiErrorMessage(err, t('automation.common.restoreFailed')) });
    } finally {
      setTrashActionLoading(false);
    }
  };

  const handleHardDelete = async () => {
    if (!hardDeleteTarget) return;
    setTrashActionLoading(true);
    try {
      await workflowApi.hardDelete(hardDeleteTarget.id);
      showToast({ type: 'success', text: t('automation.workflows.hardDeleted', { name: hardDeleteTarget.name }) });
      setHardDeleteTarget(null);
      refreshTrashList();
      refreshTrashCount();
      refreshOverview();
    } catch (err: unknown) {
      showToast({ type: 'error', text: getApiErrorMessage(err, t('automation.common.deleteFailed')) });
    } finally {
      setTrashActionLoading(false);
    }
  };

  const handleClearTrash = async () => {
    setTrashActionLoading(true);
    try {
      const res: { count?: number } = await workflowApi.clearTrash(trashQuery.mailboxId ?? undefined);
      const count = res?.count ?? 0;
      showToast({ type: 'success', text: t('automation.common.trashCleared', { count }) });
      setClearTrashOpen(false);
      refreshTrashList();
      refreshTrashCount();
      refreshOverview();
    } catch (err: unknown) {
      showToast({ type: 'error', text: getApiErrorMessage(err, t('automation.common.clearTrashFailed')) });
    } finally {
      setTrashActionLoading(false);
    }
  };

  const handleSave = async (data: CreateWorkflowRequest, id?: number) => {
    try {
      if (id) {
        await workflowApi.update(id, { ...data, id });
        showToast({ type: 'success', text: t('automation.workflows.updated') });
      } else {
        await workflowApi.create(data);
        showToast({ type: 'success', text: t('automation.workflows.created') });
      }
      setShowEditor(false);
      setEditing(null);
      refreshOverview();
      refreshList();
    } catch (e: unknown) {
      showToast({ type: 'error', text: t('automation.common.saveFailed') });
      throw e;
    }
  };

  const handleTest = async () => {
    if (!testModal) return;
    setTesting(true);
    setTestResult(null);
    try {
      const result = await workflowApi.test(testModal.id, {
        subject: testInput.subject,
        fromEmail: testInput.fromEmail,
        body: testInput.body,
        mailboxId: 0,
      });
      setTestResult(result);
    } catch {
      showToast({ type: 'error', text: t('automation.workflows.test.failed') });
    } finally {
      setTesting(false);
    }
  };

  const openLogs = async (resetPage = true) => {
    setShowLogs(true);
    setLogsLoading(true);
    try {
      const q = resetPage ? { ...logsQuery, page: 1 } : logsQuery;
      const res = (await workflowApi.executionLogs({
        page: q.page,
        pageSize: q.pageSize,
        status: q.status || undefined,
      })) as PagedList<WorkflowExecutionLogDto>;
      let filtered = res;
      if (logsQuery.keyword.trim()) {
        const kw = logsQuery.keyword.trim().toLowerCase();
        const arr = (res.data ?? []).filter(
          (l) =>
            l.workflowName?.toLowerCase().includes(kw) ||
            (l.conversationSubject || '').toLowerCase().includes(kw) ||
            (l.errorMessage || '').toLowerCase().includes(kw) ||
            String(l.conversationId).includes(kw)
        );
        filtered = { ...res, data: arr, totalItems: arr.length };
      }
      if (resetPage) {
        setLogsQuery((prev) => ({ ...prev, page: 1 }));
      }
      setLogs(filtered);
    } finally {
      setLogsLoading(false);
    }
  };

  const data = list?.data ?? [];
  const totalItems = list?.totalItems ?? 0;
  const totalPages = list?.totalPages ?? 0;
  const currentPage = list?.currentPage ?? 1;

  const logsData = logs?.data ?? [];

  const stats = overview?.stats ?? {};

  return (
    <div className="space-y-5">
      {/* 页头 */}
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-semibold text-gray-900 dark:text-slate-100">{t('automation.workflows.page.title')}</h1>
          <p className="text-sm text-gray-500 dark:text-slate-400 mt-1">{t('automation.workflows.page.subtitle')}</p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={openTrashModal}
            className="relative px-4 py-2 text-sm font-medium text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition flex items-center gap-2"
            title={t('automation.common.trash')}
          >
            <Trash2 className="w-4 h-4" />
            {t('automation.common.trash')}
            {trashCount > 0 && (
              <span className="absolute -top-1.5 -right-1.5 min-w-[20px] h-5 px-1.5 flex items-center justify-center text-xs font-bold text-white bg-red-500 rounded-full">
                {trashCount > 99 ? '99+' : trashCount}
              </span>
            )}
          </button>
          <button
            onClick={() => openLogs(true)}
            className="px-3 py-2 text-sm font-medium text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 flex items-center gap-1.5"
          >
            <BookOpen className="w-4 h-4" />
            {t('automation.workflows.page.executionLogs')}
          </button>
          <button
            onClick={() => {
              setEditing(null);
              setShowEditor(true);
            }}
            className="px-3 py-2 text-sm font-medium text-white bg-primary-600 rounded-lg hover:bg-primary-700 flex items-center gap-1.5"
          >
            <Plus className="w-4 h-4" />
            {t('automation.workflows.page.new')}
          </button>
        </div>
      </div>

      {/* 统计卡片 */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        {[
          { labelKey: 'automation.workflows.stats.total' as TranslationKey, value: stats.total ?? 0, color: 'text-gray-700 dark:text-slate-200', bg: 'bg-gray-50 dark:bg-slate-800/50', icon: Layers },
          { labelKey: 'automation.common.statsActive' as TranslationKey, value: stats.active ?? 0, color: 'text-green-600', bg: 'bg-green-50', icon: CheckCircle2 },
          { labelKey: 'automation.common.statsInactive' as TranslationKey, value: stats.inactive ?? 0, color: 'text-gray-500 dark:text-slate-400', bg: 'bg-gray-50 dark:bg-slate-800/50', icon: XCircle },
          { labelKey: 'automation.common.statsExecutions' as TranslationKey, value: stats.executions ?? 0, color: 'text-blue-600', bg: 'bg-blue-50', icon: Activity },
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

      {/* 筛选 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4 shadow-sm">
        <div className="flex items-center gap-3 flex-wrap">
          <div className="flex-1 min-w-[200px]">
            <div className="relative">
              <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
              <input
                type="text"
                placeholder={t('automation.workflows.list.searchPlaceholder')}
                value={keywordInput}
                onChange={(e) => handleKeywordInput(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') triggerKeywordNow(); }}
                className="w-full pl-9 pr-3 py-2 text-sm bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 text-gray-900 dark:text-slate-100"
              />
            </div>
          </div>
          <select
            value={query.mailboxId ?? ''}
            onChange={(e) => handleFilterChange('mailboxId', e.target.value ? Number(e.target.value) : null)}
            className="px-3 py-2 text-sm bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 text-gray-900 dark:text-slate-100"
          >
            <option value="">{t('automation.common.allMailboxes')}</option>
            {mailboxes.map((m) => (
              <option key={m.id} value={m.id}>{m.name}</option>
            ))}
          </select>
          <select
            value={query.triggerType ?? ''}
            onChange={(e) => handleFilterChange('triggerType', e.target.value || null)}
            className="px-3 py-2 text-sm bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 text-gray-900 dark:text-slate-100"
          >
            <option value="">{t('automation.workflows.list.allTriggers')}</option>
            {WORKFLOW_TRIGGER_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>{t(o.labelKey)}</option>
            ))}
          </select>
          <select
            value={query.isActive === null || query.isActive === undefined ? '' : String(query.isActive)}
            onChange={(e) => handleFilterChange('isActive', e.target.value === '' ? null : e.target.value === 'true')}
            className="px-3 py-2 text-sm bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 text-gray-900 dark:text-slate-100"
          >
            <option value="">{t('automation.common.allStatuses')}</option>
            <option value="true">{t('automation.workflows.list.filterActive')}</option>
            <option value="false">{t('automation.workflows.list.filterInactive')}</option>
          </select>
        </div>
      </div>

      {/* 工作流列表 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm overflow-hidden">
        {/* PC 端表格视图 */}
        <div className="hidden md:block overflow-x-auto">
          <table className="w-full">
            <thead className="bg-gray-50 dark:bg-slate-900 border-b border-gray-200 dark:border-slate-700">
              <tr>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.workflows.list.columns.name')}</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.workflows.list.columns.trigger')}</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.workflows.list.columns.mailbox')}</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.workflows.list.columns.conditionsActions')}</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.workflows.list.columns.match')}</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.common.status')}</th>
                <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.common.actions')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 dark:divide-slate-700/50 virtual-list">
              {loading ? (
                <tr><td colSpan={7} className="px-4 py-12 text-center text-gray-400 text-sm">{t('common.shell.loading')}</td></tr>
              ) : data.length === 0 ? (
                <tr><td colSpan={7} className="px-4 py-12 text-center text-gray-400 text-sm">{t('automation.workflows.list.empty')}</td></tr>
              ) : (
                <>
                  {/* 系统内置工作流分组 */}
                  {data.some((w) => w.isSystem) && (
                    <>
                      <tr className="bg-blue-50 dark:bg-blue-900/10">
                        <td colSpan={7} className="px-4 py-2">
                          <div className="flex items-center gap-2">
                            <Settings className="w-4 h-4 text-blue-600 dark:text-blue-400" />
                            <span className="text-xs font-semibold text-blue-700 dark:text-blue-300 uppercase tracking-wide">
                              {t('automation.workflows.list.systemGroup')}
                            </span>
                            <span className="text-xs text-blue-500 dark:text-blue-400/70">
                              {t('automation.workflows.list.groupCount', { count: data.filter((w) => w.isSystem).length })}
                            </span>
                          </div>
                        </td>
                      </tr>
                      {data.filter((w) => w.isSystem).map((w) => renderWorkflowRow(w))}
                    </>
                  )}

                  {/* 自定义工作流分组 */}
                  {data.some((w) => !w.isSystem) && (
                    <>
                      <tr className="bg-green-50 dark:bg-green-900/10">
                        <td colSpan={7} className="px-4 py-2">
                          <div className="flex items-center gap-2">
                            <Layers className="w-4 h-4 text-green-600 dark:text-green-400" />
                            <span className="text-xs font-semibold text-green-700 dark:text-green-300 uppercase tracking-wide">
                              {t('automation.workflows.list.customGroup')}
                            </span>
                            <span className="text-xs text-green-500 dark:text-green-400/70">
                              {t('automation.workflows.list.groupCount', { count: data.filter((w) => !w.isSystem).length })}
                            </span>
                          </div>
                        </td>
                      </tr>
                      {data.filter((w) => !w.isSystem).map((w) => renderWorkflowRow(w))}
                    </>
                  )}
                </>
              )}
            </tbody>
          </table>
        </div>

        {/* 移动端卡片视图 */}
        <div className="md:hidden">
          {loading ? (
            <div className="p-12 text-center text-gray-400 text-sm">{t('common.shell.loading')}</div>
          ) : data.length === 0 ? (
            <div className="p-12 text-center text-gray-400 text-sm">{t('automation.workflows.list.empty')}</div>
          ) : (
            <>
              {data.some((w) => w.isSystem) && (
                <>
                  <div className="px-4 py-2 bg-blue-50 dark:bg-blue-900/10 flex items-center gap-2">
                    <Settings className="w-4 h-4 text-blue-600 dark:text-blue-400" />
                    <span className="text-xs font-semibold text-blue-700 dark:text-blue-300 uppercase tracking-wide">{t('automation.workflows.list.systemGroup')}</span>
                    <span className="text-xs text-blue-500 dark:text-blue-400/70">{t('automation.workflows.list.groupCount', { count: data.filter((w) => w.isSystem).length })}</span>
                  </div>
                  <div className="divide-y divide-gray-100 dark:divide-slate-700/50">
                    {data.filter((w) => w.isSystem).map((w) => (
                      <div key={w.id} className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700">
                        <div className="flex items-center justify-between gap-2 mb-2">
                          <div className="flex items-center gap-2 min-w-0">
                            <span className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate">{w.name}</span>
                            <span className="text-xs px-2 py-0.5 rounded-full bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-slate-300 font-medium flex-shrink-0">{t('automation.workflows.list.systemBadge')}</span>
                          </div>
                          <button
                            onClick={() => handleToggleActive(w)}
                            className={`relative inline-flex h-5 w-9 items-center rounded-full transition flex-shrink-0 ${w.isActive ? 'bg-green-500' : 'bg-gray-300'}`}
                          >
                            <span className={`inline-block h-3.5 w-3.5 transform rounded-full bg-white transition ${w.isActive ? 'translate-x-5' : 'translate-x-1'}`} />
                          </button>
                        </div>
                        {w.description && <div className="text-xs text-gray-500 dark:text-slate-400 mb-2 line-clamp-1">{w.description}</div>}
                        <div className="flex items-center gap-2 mb-3 flex-wrap">
                          <span className={`text-xs px-2 py-1 rounded-full font-medium ${getTriggerBadgeClass(w.triggerType)}`}>{workflowTriggerLabel(t, w.triggerType)}</span>
                          <span className="text-xs text-gray-500 dark:text-slate-400">{!w.mailboxId ? t('automation.common.allMailboxes') : w.mailboxName ?? `#${w.mailboxId}`}</span>
                        </div>
                        <div className="grid grid-cols-3 gap-2 mb-3">
                          <div className="text-center bg-gray-50 dark:bg-slate-700/40 rounded-lg py-1.5">
                            <div className="text-xs text-gray-400">{t('automation.workflows.list.conditionsCard')}</div>
                            <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{w.conditions?.conditions?.length ?? 0}</div>
                          </div>
                          <div className="text-center bg-gray-50 dark:bg-slate-700/40 rounded-lg py-1.5">
                            <div className="text-xs text-gray-400">{t('automation.workflows.list.actionsCard')}</div>
                            <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{w.actions.length}</div>
                          </div>
                          <div className="text-center bg-gray-50 dark:bg-slate-700/40 rounded-lg py-1.5">
                            <div className="text-xs text-gray-400">{t('automation.workflows.list.matchCard')}</div>
                            <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{w.matchCount}</div>
                          </div>
                        </div>
                        <div className="flex items-center gap-1.5 pt-3 border-t border-gray-100 dark:border-slate-700/50">
                          <button onClick={() => setTestModal({ id: w.id, name: w.name, actions: w.actions })} className="flex-1 px-3 py-2 text-xs font-medium text-purple-600 bg-purple-50 dark:bg-purple-900/20 rounded-lg">{t('automation.workflows.list.testAction')}</button>
                          <button onClick={() => { setEditing(w); setShowEditor(true); }} className="flex-1 px-3 py-2 text-xs font-medium text-blue-600 bg-blue-50 dark:bg-blue-900/20 rounded-lg">{t('common.edit')}</button>
                          <button onClick={() => { if (!w.isSystem) setDeleteTarget(w); }} disabled={w.isSystem} className={`flex-1 px-3 py-2 text-xs font-medium rounded-lg ${w.isSystem ? 'text-gray-300 bg-gray-50 cursor-not-allowed' : 'text-red-600 bg-red-50 dark:bg-red-900/20'}`}>{t('common.delete')}</button>
                        </div>
                      </div>
                    ))}
                  </div>
                </>
              )}
              {data.some((w) => !w.isSystem) && (
                <>
                  <div className="px-4 py-2 bg-green-50 dark:bg-green-900/10 flex items-center gap-2">
                    <Layers className="w-4 h-4 text-green-600 dark:text-green-400" />
                    <span className="text-xs font-semibold text-green-700 dark:text-green-300 uppercase tracking-wide">{t('automation.workflows.list.customGroup')}</span>
                    <span className="text-xs text-green-500 dark:text-green-400/70">{t('automation.workflows.list.groupCount', { count: data.filter((w) => !w.isSystem).length })}</span>
                  </div>
                  <div className="divide-y divide-gray-100 dark:divide-slate-700/50">
                    {data.filter((w) => !w.isSystem).map((w) => (
                      <div key={w.id} className="p-4 hover:bg-gray-50 dark:hover:bg-slate-700">
                        <div className="flex items-center justify-between gap-2 mb-2">
                          <span className="text-sm font-medium text-gray-900 dark:text-slate-100 truncate">{w.name}</span>
                          <button
                            onClick={() => handleToggleActive(w)}
                            className={`relative inline-flex h-5 w-9 items-center rounded-full transition flex-shrink-0 ${w.isActive ? 'bg-green-500' : 'bg-gray-300'}`}
                          >
                            <span className={`inline-block h-3.5 w-3.5 transform rounded-full bg-white transition ${w.isActive ? 'translate-x-5' : 'translate-x-1'}`} />
                          </button>
                        </div>
                        {w.description && <div className="text-xs text-gray-500 dark:text-slate-400 mb-2 line-clamp-1">{w.description}</div>}
                        <div className="flex items-center gap-2 mb-3 flex-wrap">
                          <span className={`text-xs px-2 py-1 rounded-full font-medium ${getTriggerBadgeClass(w.triggerType)}`}>{workflowTriggerLabel(t, w.triggerType)}</span>
                          <span className="text-xs text-gray-500 dark:text-slate-400">{!w.mailboxId ? t('automation.common.allMailboxes') : w.mailboxName ?? `#${w.mailboxId}`}</span>
                        </div>
                        <div className="grid grid-cols-3 gap-2 mb-3">
                          <div className="text-center bg-gray-50 dark:bg-slate-700/40 rounded-lg py-1.5">
                            <div className="text-xs text-gray-400">{t('automation.workflows.list.conditionsCard')}</div>
                            <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{w.conditions?.conditions?.length ?? 0}</div>
                          </div>
                          <div className="text-center bg-gray-50 dark:bg-slate-700/40 rounded-lg py-1.5">
                            <div className="text-xs text-gray-400">{t('automation.workflows.list.actionsCard')}</div>
                            <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{w.actions.length}</div>
                          </div>
                          <div className="text-center bg-gray-50 dark:bg-slate-700/40 rounded-lg py-1.5">
                            <div className="text-xs text-gray-400">{t('automation.workflows.list.matchCard')}</div>
                            <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{w.matchCount}</div>
                          </div>
                        </div>
                        <div className="flex items-center gap-1.5 pt-3 border-t border-gray-100 dark:border-slate-700/50">
                          <button onClick={() => setTestModal({ id: w.id, name: w.name, actions: w.actions })} className="flex-1 px-3 py-2 text-xs font-medium text-purple-600 bg-purple-50 dark:bg-purple-900/20 rounded-lg">{t('automation.workflows.list.testAction')}</button>
                          <button onClick={() => { setEditing(w); setShowEditor(true); }} className="flex-1 px-3 py-2 text-xs font-medium text-blue-600 bg-blue-50 dark:bg-blue-900/20 rounded-lg">{t('common.edit')}</button>
                          <button onClick={() => setDeleteTarget(w)} className="flex-1 px-3 py-2 text-xs font-medium text-red-600 bg-red-50 dark:bg-red-900/20 rounded-lg">{t('common.delete')}</button>
                        </div>
                      </div>
                    ))}
                  </div>
                </>
              )}
            </>
          )}
        </div>

        {totalPages > 0 && (
          <div className="px-4 py-3 border-t border-gray-100 dark:border-slate-700/50 flex items-center justify-between">
            <div className="text-sm text-gray-500">
              {t('automation.workflows.list.pagination', { page: currentPage, total: totalPages, count: totalItems })}
            </div>
            <div className="flex items-center gap-1">
              <button
                onClick={() => handlePageChange(currentPage - 1)}
                disabled={currentPage <= 1}
                className="p-2 text-gray-600 dark:text-slate-400 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition-colors"
              >
                <ChevronLeft className="w-4 h-4" />
              </button>
              <button
                onClick={() => handlePageChange(currentPage + 1)}
                disabled={currentPage >= totalPages}
                className="p-2 text-gray-600 dark:text-slate-400 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition-colors"
              >
                <ChevronRight className="w-4 h-4" />
              </button>
            </div>
          </div>
        )}
      </div>

      {/* Toast */}
      {toast && (
        <div className={`fixed bottom-6 right-6 px-4 py-2 rounded-lg shadow-lg text-sm text-white ${toast.type === 'success' ? 'bg-green-600' : 'bg-red-600'}`}>
          {toast.text}
        </div>
      )}

      {/* 编辑器 Modal */}
      {showEditor && (
        <WorkflowEditor
          workflow={editing}
          mailboxes={mailboxes}
          users={users}
          onClose={() => {
            setShowEditor(false);
            setEditing(null);
          }}
          onSave={handleSave}
        />
      )}

      {/* 测试 Modal */}
      <Modal
        open={!!testModal}
        onClose={() => {
          setTestModal(null);
          setTestResult(null);
          setTestInput({ subject: '', fromEmail: '', body: '' });
        }}
        title={t('automation.workflows.test.title', { name: testModal?.name ?? '' })}
        size="lg"
      >
        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('automation.workflows.test.subject')}</label>
            <input
              type="text"
              value={testInput.subject}
              onChange={(e) => setTestInput({ ...testInput, subject: e.target.value })}
              placeholder={t('automation.workflows.test.subjectPlaceholder')}
              className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('automation.workflows.test.fromEmail')}</label>
              <input
                type="text"
                value={testInput.fromEmail}
                onChange={(e) => setTestInput({ ...testInput, fromEmail: e.target.value })}
                placeholder={t('automation.workflows.test.fromEmailPlaceholder')}
                className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
                />
              <p className="mt-1 text-xs text-gray-400 dark:text-slate-500">{t('automation.workflows.test.fromEmailHint')}</p>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('automation.workflows.test.body')}</label>
              <textarea
                value={testInput.body}
                onChange={(e) => setTestInput({ ...testInput, body: e.target.value })}
                rows={3}
                placeholder={t('automation.workflows.test.bodyPlaceholder')}
                className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
            />
          </div>
          <div className="flex justify-end">
            <button
              onClick={handleTest}
              disabled={testing}
              className="px-4 py-2 text-sm font-medium text-white bg-purple-600 rounded-lg hover:bg-purple-700 disabled:opacity-50"
            >
              {testing ? t('automation.workflows.test.running') : t('automation.workflows.test.run')}
            </button>
          </div>
          {testResult && (
            <div className={`rounded-lg p-4 border ${testResult.matched ? 'bg-green-50 dark:bg-green-900/20 border-green-200 dark:border-green-800' : 'bg-gray-50 dark:bg-slate-900 border-gray-200 dark:border-slate-700'}`}>
              <div className="flex items-center gap-2 mb-3">
                <div className={`w-6 h-6 rounded-full flex items-center justify-center ${testResult.matched ? 'bg-green-500 text-white' : 'bg-gray-400 text-white'}`}>
                  {testResult.matched ? '✓' : '✗'}
                </div>
                <div className="font-medium text-sm text-gray-900 dark:text-slate-100">
                  {testResult.matched ? t('automation.workflows.test.matched') : t('automation.workflows.test.notMatched')}
                </div>
              </div>
              {testResult.matchedConditions.length > 0 && (
                <div className="mb-3">
                  <div className="text-xs text-gray-500 dark:text-slate-400 mb-1">{t('automation.workflows.test.matchedConditions')}</div>
                  <ul className="text-xs space-y-0.5 text-gray-700 dark:text-slate-300">
                    {/* 后端 EvaluateConditions 生成的诊断串（由字段/运算符原始 code 组成）——属数据层诊断输出，本波不翻译 */}
                    {testResult.matchedConditions.map((c, i) => (
                      <li key={`${i}-${c}`}>{c}</li>
                    ))}
                  </ul>
                </div>
              )}
              {(testModal?.actions?.length ?? 0) > 0 && (
                <div className="mb-3">
                  <div className="text-xs text-gray-500 dark:text-slate-400 mb-1">{t('automation.workflows.test.actionsToExecute')}</div>
                  <ul className="text-xs space-y-0.5 text-gray-700 dark:text-slate-300">
                    {(testModal?.actions ?? []).map((a, i) => (
                      <li key={`${i}-${a.type}`}>• {workflowActionLabel(t, a.type)}: {a.value ?? '-'}</li>
                    ))}
                  </ul>
                </div>
              )}
              {testResult.actionResults && testResult.actionResults.length > 0 && (
                <div>
                  <div className="text-xs text-gray-500 dark:text-slate-400 mb-1">{t('automation.workflows.test.actionResults')}</div>
                  <ul className="text-xs space-y-1.5">
                    {testResult.actionResults.map((r, i) => (
                      <li key={`${i}-${r.type}`} className="flex items-start gap-2">
                        <span className={`inline-flex items-center justify-center w-4 h-4 rounded-full text-[10px] flex-shrink-0 mt-0.5 ${
                          r.executed
                            ? (r.success ? 'bg-green-500 text-white' : 'bg-red-500 text-white')
                            : 'bg-gray-300 dark:bg-slate-600 text-gray-600 dark:text-slate-300'
                        }`}>
                          {r.executed ? (r.success ? '✓' : '✗') : '○'}
                        </span>
                        <div className="flex-1">
                          <span className={`font-medium ${r.executed && r.success ? 'text-green-700 dark:text-green-400' : r.executed && !r.success ? 'text-red-700 dark:text-red-400' : 'text-gray-600 dark:text-slate-400'}`}>
                            {workflowActionLabel(t, r.type)}
                          </span>
                          <span className="text-gray-500 dark:text-slate-400 ml-1">{r.executed ? r.message : t('automation.workflows.test.previewOnly')}</span>
                        </div>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          )}
        </div>
      </Modal>

      {/* 执行日志 Modal */}
      <Modal
        open={showLogs}
        onClose={() => setShowLogs(false)}
        title={
          <div className="flex items-center gap-3">
            <div className="relative w-9 h-9 rounded-xl bg-gradient-to-br from-violet-500 via-indigo-500 to-blue-600 flex items-center justify-center shadow-md shadow-indigo-500/20">
              <Activity className="w-4.5 h-4.5 text-white" strokeWidth={2.2} />
              <div className="absolute -bottom-0.5 -right-0.5 w-2.5 h-2.5 rounded-full bg-emerald-400 ring-2 ring-white dark:ring-slate-900 animate-pulse" />
            </div>
            <div>
              <div className="text-base font-semibold text-gray-900 dark:text-slate-100 leading-tight">{t('automation.workflows.logs.title')}</div>
              <div className="text-xs text-gray-400 dark:text-slate-500 mt-0.5 flex items-center gap-1.5">
                <Clock className="w-3 h-3" />
                {t('automation.workflows.logs.subtitle')}
              </div>
            </div>
          </div>
        }
        size="xl"
      >
        {/* 概览统计条 */}
        <div className="grid grid-cols-2 md:grid-cols-4 gap-2.5 mb-4">
          {[
            {
              code: 'total', labelKey: 'automation.workflows.logs.statsTotal' as TranslationKey, value: logs?.totalItems ?? 0,
              icon: Activity,
              bg: 'from-slate-500 to-gray-600',
              tint: 'bg-slate-50 dark:bg-slate-800/50',
              text: 'text-slate-700 dark:text-slate-200',
            },
            {
              code: 'success', labelKey: 'automation.common.statusSuccess' as TranslationKey, value: logsData.filter((l) => l.status === 'success').length + ((logs?.totalItems ?? 0) > 0 ? Math.round(((logsData.filter((l) => l.status === 'success').length / Math.max(logsData.length, 1))) * Math.min(logs?.totalItems ?? 0, (logs?.totalItems ?? 0))) : 0),
              icon: CheckCircle2,
              bg: 'from-emerald-500 to-green-600',
              tint: 'bg-emerald-50 dark:bg-emerald-900/20',
              text: 'text-emerald-700 dark:text-emerald-300',
            },
            {
              code: 'failedPartial', labelKey: 'automation.workflows.logs.statsFailedPartial' as TranslationKey, value: logsData.filter((l) => l.status === 'failed' || l.status === 'partial').length,
              icon: AlertTriangle,
              bg: 'from-amber-500 to-orange-600',
              tint: 'bg-amber-50 dark:bg-amber-900/20',
              text: 'text-amber-700 dark:text-amber-300',
            },
            {
              code: 'notMatched', labelKey: 'automation.common.statusNotMatched' as TranslationKey, value: logsData.filter((l) => l.status === 'not_matched').length,
              icon: Ban,
              bg: 'from-slate-400 to-slate-600',
              tint: 'bg-slate-50 dark:bg-slate-800/50',
              text: 'text-slate-600 dark:text-slate-400',
            },
          ].map((s) => {
            const Icon = s.icon;
            const displayVal = s.code === 'total'
              ? (logs?.totalItems ?? 0)
              : s.code === 'success'
                ? (logsData.filter((l) => l.status === 'success').length)
                : s.code === 'notMatched'
                  ? logsData.filter((l) => l.status === 'not_matched').length
                  : logsData.filter((l) => l.status === 'failed' || l.status === 'partial').length;
            return (
              <div key={s.code} className={`relative overflow-hidden rounded-xl border border-gray-200/80 dark:border-slate-700/60 ${s.tint} p-3.5`}>
                <div className={`absolute -top-8 -right-8 w-24 h-24 rounded-full bg-gradient-to-br ${s.bg} opacity-10 blur-xl`} />
                <div className="relative flex items-center justify-between">
                  <div>
                    <div className="text-xs text-gray-500 dark:text-slate-400">{t(s.labelKey)}</div>
                    <div className={`mt-1 text-2xl font-bold tabular-nums ${s.text}`}>{displayVal}</div>
                  </div>
                  <div className={`w-9 h-9 rounded-lg bg-gradient-to-br ${s.bg} flex items-center justify-center shadow-sm`}>
                    <Icon className="w-4.5 h-4.5 text-white" strokeWidth={2.2} />
                  </div>
                </div>
              </div>
            );
          })}
        </div>

        {/* 工具栏：筛选 + 搜索 + 刷新 */}
        <div className="flex flex-wrap items-center gap-2 mb-3 p-3 rounded-xl bg-gray-50/80 dark:bg-slate-800/40 border border-gray-200 dark:border-slate-700/60">
          <div className="flex items-center gap-1.5 text-xs text-gray-500 dark:text-slate-400 mr-1">
            <Filter className="w-3.5 h-3.5" />
            {t('automation.workflows.logs.filterLabel')}
          </div>
          <div className="flex flex-wrap items-center gap-1 bg-white dark:bg-slate-900 rounded-lg border border-gray-200 dark:border-slate-700 p-1">
            {LOG_STATUS_FILTERS.map((f) => (
              <button
                key={f.value ?? '__all'}
                onClick={() => {
                  setLogsQuery((q) => ({ ...q, status: f.value, page: 1 }));
                  setTimeout(() => openLogs(false), 0);
                }}
                className={`px-2.5 py-1 text-xs font-medium rounded-md transition-all ${
                  logsQuery.status === f.value
                    ? 'bg-gradient-to-r from-violet-600 to-indigo-600 text-white shadow-sm shadow-indigo-500/30'
                    : 'text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700'
                }`}
              >
                {t(f.labelKey)}
              </button>
            ))}
          </div>
          <div className="flex-1 min-w-[200px] max-w-md relative">
            <Search className="w-3.5 h-3.5 absolute left-2.5 top-1/2 -translate-y-1/2 text-gray-400" />
            <input
              type="text"
              placeholder={t('automation.workflows.logs.searchPlaceholder')}
              value={logsQuery.keyword}
              onChange={(e) => {
                setLogsQuery((q) => ({ ...q, keyword: e.target.value, page: 1 }));
              }}
              onKeyDown={(e) => { if (e.key === 'Enter') openLogs(false); }}
              className="w-full pl-8 pr-8 py-1.5 text-xs bg-white dark:bg-slate-900 border border-gray-200 dark:border-slate-700 rounded-lg focus:outline-none focus:ring-2 focus:ring-indigo-500/40 text-gray-800 dark:text-slate-100 placeholder-gray-400"
            />
            {logsQuery.keyword && (
              <button
                onClick={() => {
                  setLogsQuery((q) => ({ ...q, keyword: '' }));
                  setTimeout(() => openLogs(false), 0);
                }}
                className="absolute right-2 top-1/2 -translate-y-1/2 w-4 h-4 flex items-center justify-center text-gray-400 hover:text-gray-600 dark:hover:text-slate-200"
                title={t('automation.workflows.logs.clearSearch')}
              >
                <XCircle className="w-3.5 h-3.5" />
              </button>
            )}
          </div>
          <button
            onClick={() => openLogs(false)}
            disabled={logsLoading}
            className="flex items-center gap-1.5 px-2.5 py-1.5 text-xs font-medium text-gray-700 dark:text-slate-200 bg-white dark:bg-slate-900 border border-gray-200 dark:border-slate-700 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-800 transition disabled:opacity-50"
            title={t('automation.workflows.logs.refreshTitle')}
          >
            <RefreshCcw className={`w-3.5 h-3.5 ${logsLoading ? 'animate-spin' : ''}`} />
            {t('automation.workflows.logs.refresh')}
          </button>
        </div>

        {/* 日志列表：时间线卡片式 */}
        <div className="-mx-2 px-2 max-h-[55vh] overflow-y-auto pr-1 custom-scrollbar">
          {logsLoading ? (
            <div className="py-14 flex flex-col items-center justify-center text-gray-400">
              <Activity className="w-7 h-7 animate-spin text-indigo-500/70 mb-2" strokeWidth={1.8} />
              <div className="text-sm">{t('automation.workflows.logs.loading')}</div>
            </div>
          ) : logsData.length === 0 ? (
            <div className="py-14 flex flex-col items-center justify-center text-gray-400">
              <div className="w-14 h-14 rounded-full bg-gray-100 dark:bg-slate-800 flex items-center justify-center mb-3">
                <BookOpen className="w-6 h-6 text-gray-400 dark:text-slate-500" />
              </div>
              <div className="text-sm font-medium text-gray-500 dark:text-slate-400 mb-1">{t('automation.common.noExecutionLogs')}</div>
              <div className="text-xs text-gray-400 dark:text-slate-500">{t('automation.workflows.logs.emptyHint')}</div>
            </div>
          ) : (
            <div className="relative pl-6">
              {/* 左侧时间轴线 */}
              <div className="absolute left-[11px] top-1 bottom-1 w-0.5 bg-gradient-to-b from-violet-400 via-indigo-300 to-transparent dark:from-violet-500/70 dark:via-indigo-500/50 rounded-full" aria-hidden />

              <ul className="space-y-2.5">
                {logsData.map((log, i) => {
                  const isSuccess = log.status === 'success';
                  const isPartial = log.status === 'partial';
                  const isNotMatched = log.status === 'not_matched';
                  const hasError = !!log.errorMessage;
                  const expanded = !!logExpanded[log.id];
                  const execTime = new Date(log.executedAt);
                  const timeStr = execTime.toLocaleString(toIntlLocale(language), {
                    hour12: false, month: '2-digit', day: '2-digit',
                    hour: '2-digit', minute: '2-digit', second: '2-digit',
                  });
                  const dateKey = `${execTime.getFullYear()}-${String(execTime.getMonth() + 1).padStart(2, '0')}-${String(execTime.getDate()).padStart(2, '0')}`;
                  const prevDate = i > 0
                    ? (() => {
                        const d = new Date(logsData[i - 1].executedAt);
                        return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
                      })()
                    : null;
                  const showDateHeader = prevDate !== dateKey;

                  const dotColor = isSuccess
                    ? 'bg-emerald-500 ring-emerald-100 dark:ring-emerald-900/50'
                    : isPartial
                      ? 'bg-amber-500 ring-amber-100 dark:ring-amber-900/50'
                      : isNotMatched
                        ? 'bg-slate-400 ring-slate-100 dark:ring-slate-700/60'
                        : 'bg-red-500 ring-red-100 dark:ring-red-900/50';

                  const StatusIcon = isSuccess ? CheckCircle2 : isPartial ? AlertTriangle : isNotMatched ? Ban : XOctagon;
                  const statusTint = isSuccess
                    ? 'bg-emerald-50 dark:bg-emerald-900/20 text-emerald-700 dark:text-emerald-300 ring-emerald-200/60 dark:ring-emerald-800/50'
                    : isPartial
                      ? 'bg-amber-50 dark:bg-amber-900/20 text-amber-700 dark:text-amber-300 ring-amber-200/60 dark:ring-amber-800/50'
                      : isNotMatched
                        ? 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300 ring-slate-200/60 dark:ring-slate-700/50'
                        : 'bg-red-50 dark:bg-red-900/20 text-red-700 dark:text-red-300 ring-red-200/60 dark:ring-red-900/50';

                  return (
                    <li key={log.id} className="relative">
                      {showDateHeader && (
                        <div className="sticky top-0 z-[1] -ml-6 mb-2 flex items-center gap-2">
                          <div className="px-2 py-0.5 rounded-full bg-white dark:bg-slate-900 border border-gray-200 dark:border-slate-700 text-[10px] font-medium text-gray-500 dark:text-slate-400 shadow-sm">
                            {dateKey}
                          </div>
                        </div>
                      )}
                      {/* 时间点 */}
                      <div className={`absolute -left-[18px] top-4 w-4 h-4 rounded-full ring-4 ${dotColor} shadow-sm transition-transform hover:scale-125`} aria-hidden />

                      <div
                        className={`group rounded-xl border transition-all bg-white dark:bg-slate-800/80 border-gray-200 dark:border-slate-700 hover:border-indigo-300/60 dark:hover:border-indigo-600/40 hover:shadow-md hover:-translate-y-0.5 ${
                          hasError && !isPartial ? 'ring-1 ring-red-100 dark:ring-red-900/40' : ''
                        }`}
                      >
                        <div
                          className="px-4 py-3 cursor-pointer"
                          onClick={() => setLogExpanded((m) => ({ ...m, [log.id]: !m[log.id] }))}
                        >
                          <div className="flex items-start gap-3">
                            {/* 状态图标 + 状态徽标 */}
                            <div className={`flex-shrink-0 inline-flex items-center gap-1 px-2 py-1 rounded-lg ring-1 ${statusTint} text-[11px] font-medium`}>
                              <StatusIcon className="w-3.5 h-3.5" strokeWidth={2.2} />
                              {workflowLogStatusLabel(t, log.status)}
                            </div>

                            <div className="flex-1 min-w-0">
                              {/* 第一行：工作流名称 */}
                              <div className="flex flex-wrap items-center gap-2 mb-1">
                                <div className="flex items-center gap-1.5 min-w-0">
                                  <Workflow className="w-3.5 h-3.5 text-violet-500 flex-shrink-0" strokeWidth={2} />
                                  <span className="font-semibold text-sm text-gray-900 dark:text-slate-100 truncate" title={log.workflowName}>
                                    {log.workflowName}
                                  </span>
                                </div>
                                <div className="ml-auto flex items-center gap-2 flex-shrink-0">
                                  <span className="inline-flex items-center gap-1 text-[11px] text-gray-500 dark:text-slate-400 tabular-nums font-mono">
                                    <Clock className="w-3 h-3" />
                                    {timeStr.slice(-8)}
                                  </span>
                                  {expanded ? (
                                    <ChevronUp className="w-3.5 h-3.5 text-gray-400 transition-transform" />
                                  ) : (
                                    <ChevronDown className="w-3.5 h-3.5 text-gray-400 transition-transform" />
                                  )}
                                </div>
                              </div>

                              {/* 第二行：会话链接 + 执行数 */}
                              <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
                                <a
                                  href={`#/conversations/${log.conversationId}`}
                                  onClick={(e) => e.stopPropagation()}
                                  target="_blank"
                                  rel="noreferrer"
                                  className="flex items-center gap-1 text-indigo-600 dark:text-indigo-400 hover:underline truncate max-w-[50%]"
                                  title={log.conversationSubject || t('automation.common.conversationNumber', { id: log.conversationId })}
                                >
                                  <MessageSquare className="w-3 h-3 flex-shrink-0" />
                                  <span className="truncate">
                                    {log.conversationSubject || `#${log.conversationId}`}
                                  </span>
                                </a>
                                <span className="inline-flex items-center gap-1 text-gray-500 dark:text-slate-400">
                                  <Zap className="w-3 h-3 text-violet-500" />
                                  {t('automation.workflows.logs.actionsExecuted')}
                                  <span className="font-semibold tabular-nums text-gray-700 dark:text-slate-200">
                                    {log.actionsExecuted ?? 0}
                                  </span>
                                </span>
                                {hasError && !expanded && (
                                  <span className="inline-flex items-center gap-1 text-red-600 dark:text-red-400 truncate max-w-[40%]" title={log.errorMessage ?? ''}>
                                    <AlertCircle className="w-3 h-3 flex-shrink-0" />
                                    <span className="truncate">{log.errorMessage}</span>
                                  </span>
                                )}
                                {isPartial && !hasError && (
                                  <span className="inline-flex items-center gap-1 text-amber-600 dark:text-amber-400">
                                    <AlertTriangle className="w-3 h-3" />
                                    {t('automation.workflows.logs.partialIncomplete')}
                                  </span>
                                )}
                              </div>
                            </div>
                          </div>
                        </div>

                        {/* 展开详情 */}
                        {expanded && (
                          <div className="px-4 pb-4 border-t border-dashed border-gray-200 dark:border-slate-700 mt-1 pt-3 space-y-2.5 animate-[fadeIn_.2s_ease]">
                            <div className="grid grid-cols-2 md:grid-cols-4 gap-2.5 text-xs">
                              <div className="rounded-lg bg-gray-50 dark:bg-slate-900/60 px-3 py-2 border border-gray-100 dark:border-slate-700/60">
                                <div className="text-gray-400 dark:text-slate-500 mb-0.5">{t('automation.workflows.logs.logId')}</div>
                                <div className="font-mono font-semibold text-gray-800 dark:text-slate-200 tabular-nums">#{log.id}</div>
                              </div>
                              <div className="rounded-lg bg-gray-50 dark:bg-slate-900/60 px-3 py-2 border border-gray-100 dark:border-slate-700/60">
                                <div className="text-gray-400 dark:text-slate-500 mb-0.5">{t('automation.workflows.logs.workflowId')}</div>
                                <div className="font-mono font-semibold text-violet-600 dark:text-violet-400 tabular-nums">#{log.workflowId}</div>
                              </div>
                              <div className="rounded-lg bg-gray-50 dark:bg-slate-900/60 px-3 py-2 border border-gray-100 dark:border-slate-700/60">
                                <div className="text-gray-400 dark:text-slate-500 mb-0.5">{t('automation.common.conversationId')}</div>
                                <a
                                  href={`#/conversations/${log.conversationId}`}
                                  target="_blank"
                                  rel="noreferrer"
                                  onClick={(e) => e.stopPropagation()}
                                  className="font-mono font-semibold text-indigo-600 dark:text-indigo-400 tabular-nums hover:underline"
                                >
                                  #{log.conversationId}
                                </a>
                              </div>
                              <div className="rounded-lg bg-gray-50 dark:bg-slate-900/60 px-3 py-2 border border-gray-100 dark:border-slate-700/60">
                                <div className="text-gray-400 dark:text-slate-500 mb-0.5">{t('automation.workflows.logs.executedAt')}</div>
                                <div className="font-mono font-semibold text-gray-800 dark:text-slate-200 tabular-nums text-[11px]">
                                  {timeStr}
                                </div>
                              </div>
                            </div>

                            {hasError && (
                              <div className="rounded-lg border border-red-200 dark:border-red-900/40 bg-red-50/70 dark:bg-red-950/20 p-3">
                                <div className="flex items-center gap-2 text-red-700 dark:text-red-300 font-semibold text-xs mb-1.5">
                                  <AlertCircle className="w-4 h-4" />
                                  {t('automation.workflows.logs.errorMessage')}
                                </div>
                                <pre className="whitespace-pre-wrap break-words text-xs text-red-800 dark:text-red-200 font-mono leading-relaxed bg-white/60 dark:bg-slate-900/60 rounded-md p-2.5 border border-red-100 dark:border-red-900/30 max-h-40 overflow-y-auto custom-scrollbar">
{log.errorMessage}
                                </pre>
                              </div>
                            )}

                            <div className="flex items-center justify-between gap-3 pt-1">
                              <div className="text-[11px] text-gray-400 dark:text-slate-500 flex items-center gap-1.5">
                                <Activity className="w-3 h-3" />
                                <span className="font-mono">status={log.status}</span>
                              </div>
                              <a
                                href={`#/conversations/${log.conversationId}`}
                                target="_blank"
                                rel="noreferrer"
                                onClick={(e) => e.stopPropagation()}
                                className="inline-flex items-center gap-1 px-2.5 py-1 rounded-md bg-indigo-50 dark:bg-indigo-900/30 text-indigo-700 dark:text-indigo-300 border border-indigo-200/60 dark:border-indigo-800/50 text-xs font-medium hover:bg-indigo-100 dark:hover:bg-indigo-900/50 transition"
                              >
                                {t('automation.workflows.logs.openConversation')}
                                <ChevronRight className="w-3 h-3" />
                              </a>
                            </div>
                          </div>
                        )}
                      </div>
                    </li>
                  );
                })}
              </ul>
            </div>
          )}
        </div>

        {/* 分页 */}
        {logsData.length > 0 && (
          <div className="mt-4 pt-3 border-t border-gray-100 dark:border-slate-800 flex flex-wrap items-center justify-between gap-3">
            <div className="text-xs text-gray-500 dark:text-slate-400 flex items-center gap-2">
              {t('automation.workflows.logs.totalPrefix')}<span className="font-semibold tabular-nums text-gray-700 dark:text-slate-200">{logs?.totalItems ?? 0}</span>{t('automation.workflows.logs.totalMiddle', { count: logs?.totalItems ?? 0 })}
              <span className="font-semibold tabular-nums text-gray-700 dark:text-slate-200">{logs?.currentPage ?? 1}</span>{t('automation.workflows.logs.totalSuffix', { total: logs?.totalPages ?? 1 })}
            </div>
            <div className="flex items-center gap-1.5">
              <select
                value={logsQuery.pageSize}
                onChange={(e) => {
                  setLogsQuery((q) => ({ ...q, pageSize: Number(e.target.value), page: 1 }));
                  setTimeout(() => openLogs(false), 0);
                }}
                className="text-xs bg-white dark:bg-slate-900 border border-gray-200 dark:border-slate-700 rounded-md px-2 py-1 text-gray-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500/40"
              >
                {[10, 20, 50, 100].map((n) => (
                  <option key={n} value={n}>{t('automation.workflows.logs.perPage', { count: n })}</option>
                ))}
              </select>
              <button
                onClick={() => {
                  const nextPage = Math.max(1, (logs?.currentPage ?? 1) - 1);
                  setLogsQuery((q) => ({ ...q, page: nextPage }));
                  setTimeout(() => openLogs(false), 0);
                }}
                disabled={(logs?.currentPage ?? 1) <= 1 || logsLoading}
                className="p-1.5 rounded-md bg-white dark:bg-slate-900 border border-gray-200 dark:border-slate-700 text-gray-600 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed transition"
              >
                <ChevronLeft className="w-3.5 h-3.5" />
              </button>
              <button
                onClick={() => {
                  const nextPage = Math.min(logs?.totalPages ?? 1, (logs?.currentPage ?? 1) + 1);
                  setLogsQuery((q) => ({ ...q, page: nextPage }));
                  setTimeout(() => openLogs(false), 0);
                }}
                disabled={(logs?.currentPage ?? 1) >= (logs?.totalPages ?? 1) || logsLoading}
                className="p-1.5 rounded-md bg-white dark:bg-slate-900 border border-gray-200 dark:border-slate-700 text-gray-600 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed transition"
              >
                <ChevronRight className="w-3.5 h-3.5" />
              </button>
              <button
                onClick={() => {
                  setLogsQuery((q) => ({ ...q, page: 1 }));
                  setTimeout(() => openLogs(false), 0);
                }}
                className="px-2.5 py-1 rounded-md bg-gradient-to-r from-violet-600 to-indigo-600 text-white text-xs font-medium hover:from-violet-700 hover:to-indigo-700 shadow-sm shadow-indigo-500/30 disabled:opacity-50"
                disabled={(logs?.currentPage ?? 1) === 1}
                title={t('automation.workflows.logs.firstPageTitle')}
              >
                <RotateCcw className="w-3 h-3 inline mr-1" strokeWidth={2.2} />
                {t('automation.workflows.logs.firstPage')}
              </button>
            </div>
          </div>
        )}
      </Modal>

      {/* 垃圾箱 Modal */}
      <Modal
        open={trashOpen}
        onClose={() => setTrashOpen(false)}
        title={trashCount > 0 ? t('automation.workflows.trash.titleWithCount', { count: trashCount }) : t('automation.workflows.trash.title')}
        size="xl"
      >
        <div className="space-y-3">
          <div className="flex items-center gap-3 flex-wrap">
            <div className="flex-1 min-w-[220px] relative">
              <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
              <input
                type="text"
                placeholder={t('automation.workflows.trash.searchPlaceholder')}
                value={trashKeywordInput}
                onChange={(e) => handleTrashKeywordInput(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') triggerTrashKeywordNow(); }}
                className="w-full pl-9 pr-3 py-2 text-sm bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 text-gray-900 dark:text-slate-100"
              />
            </div>
            <button
              onClick={() => setClearTrashOpen(true)}
              disabled={!trashList?.data?.length}
              className="px-3 py-2 text-sm font-medium text-white bg-red-600 rounded-lg hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-1.5"
            >
              <Trash2 className="w-4 h-4" />
              {t('automation.common.clearTrash')}
            </button>
          </div>

          <div className="overflow-x-auto border border-gray-200 dark:border-slate-700 rounded-lg">
            <table className="w-full text-sm">
              <thead className="bg-gray-50 dark:bg-slate-900 text-gray-600 text-xs uppercase">
                <tr>
                  <th className="px-4 py-3 text-left font-medium">{t('automation.workflows.trash.columns.name')}</th>
                  <th className="px-4 py-3 text-left font-medium">{t('automation.workflows.trash.columns.trigger')}</th>
                  <th className="px-4 py-3 text-left font-medium">{t('automation.common.mailboxColumn')}</th>
                  <th className="px-4 py-3 text-left font-medium">{t('automation.common.deletedAt')}</th>
                  <th className="px-4 py-3 text-right font-medium">{t('automation.common.actions')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 dark:divide-slate-700/50 virtual-list">
                {trashLoading ? (
                  <tr><td colSpan={5} className="px-4 py-10 text-center text-gray-400">{t('common.shell.loading')}</td></tr>
                ) : !trashList?.data?.length ? (
                  <tr>
                    <td colSpan={5} className="px-4 py-10 text-center text-gray-400">
                      <Trash2 className="w-10 h-10 mx-auto mb-2 text-gray-300 dark:text-slate-600" />
                      {t('automation.common.trashEmpty')}
                    </td>
                  </tr>
                ) : (
                  trashList.data.map((w) => (
                    <tr key={w.id} className="hover:bg-gray-50 dark:hover:bg-slate-700">
                      <td className="px-4 py-3">
                        <div className="font-medium text-gray-900 dark:text-slate-100">{w.name}</div>
                        {w.description && <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-1">{w.description}</div>}
                      </td>
                      <td className="px-4 py-3">
                        <span className={`text-xs px-2 py-1 rounded-full font-medium ${getTriggerBadgeClass(w.triggerType)}`}>
                          {workflowTriggerLabel(t, w.triggerType)}
                        </span>
                      </td>
                      <td className="px-4 py-3">
                        {!w.mailboxId ? (
                          <span className="text-xs px-2 py-0.5 rounded-full bg-sky-100 text-sky-700">{t('automation.common.allMailboxes')}</span>
                        ) : (
                          <span className="text-xs px-2 py-0.5 rounded-full bg-gray-100 dark:bg-slate-700/50 text-gray-700 dark:text-slate-300">{w.mailboxName ?? t('automation.common.mailboxNumber', { id: w.mailboxId })}</span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-xs text-gray-500 dark:text-slate-400">
                        {w.deletedAt ? formatDateTime(w.deletedAt) : '-'}
                      </td>
                      <td className="px-4 py-3 text-right">
                        <div className="flex items-center justify-end gap-1">
                          <button
                            onClick={() => setRestoreTarget(w)}
                            disabled={trashActionLoading}
                            className="p-2 text-emerald-600 hover:bg-emerald-50 rounded-lg transition-colors disabled:opacity-50"
                            title={t('automation.common.restore')}
                          >
                            <RotateCcw className="w-4 h-4" />
                          </button>
                          <button
                            onClick={() => setHardDeleteTarget(w)}
                            disabled={trashActionLoading}
                            className="p-2 text-red-600 hover:bg-red-50 rounded-lg transition-colors disabled:opacity-50"
                            title={t('automation.common.hardDelete')}
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

          {trashList && trashList.totalPages > 1 && (
            <div className="flex items-center justify-between px-4 py-3 border-t border-gray-100 dark:border-slate-700/50 text-sm">
              <span className="text-gray-500 dark:text-slate-400">{t('automation.common.paginationPageOf', { page: trashList.currentPage, total: trashList.totalPages })}</span>
              <div className="flex gap-1">
                <button
                  onClick={() => setTrashQuery((q) => ({ ...q, page: Math.max(1, q.page - 1) }))}
                  disabled={trashList.currentPage <= 1}
                  className="p-2 text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition-colors"
                >
                  <ChevronLeft className="w-4 h-4" />
                </button>
                <button
                  onClick={() => setTrashQuery((q) => ({ ...q, page: Math.min(trashList.totalPages, q.page + 1) }))}
                  disabled={trashList.currentPage >= trashList.totalPages}
                  className="p-2 text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50 transition-colors"
                >
                  <ChevronRight className="w-4 h-4" />
                </button>
              </div>
            </div>
          )}
        </div>
      </Modal>

      <ConfirmDialog
        open={!!deleteTarget}
        title={t('automation.workflows.delete.title')}
        message={t('automation.workflows.delete.message', { name: deleteTarget?.name ?? '' })}
        confirmText={t('automation.common.moveToTrash')}
        cancelText={t('common.cancel')}
        variant="danger"
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
        loading={deleting}
      />

      <ConfirmDialog
        open={!!restoreTarget}
        title={t('automation.workflows.restore.title')}
        message={t('automation.workflows.restore.message', { name: restoreTarget?.name ?? '' })}
        confirmText={t('automation.common.restore')}
        cancelText={t('common.cancel')}
        variant="primary"
        onConfirm={handleRestore}
        onCancel={() => setRestoreTarget(null)}
        loading={trashActionLoading}
      />

      <ConfirmDialog
        open={!!hardDeleteTarget}
        title={t('automation.workflows.hardDelete.title')}
        message={t('automation.workflows.hardDelete.message', { name: hardDeleteTarget?.name ?? '' })}
        confirmText={t('automation.common.hardDelete')}
        cancelText={t('common.cancel')}
        variant="danger"
        onConfirm={handleHardDelete}
        onCancel={() => setHardDeleteTarget(null)}
        loading={trashActionLoading}
      />

      <ConfirmDialog
        open={clearTrashOpen}
        title={t('automation.common.clearTrash')}
        message={t('automation.workflows.clearTrash.message')}
        confirmText={t('automation.common.hardDeleteAll')}
        cancelText={t('common.cancel')}
        variant="danger"
        onConfirm={handleClearTrash}
        onCancel={() => setClearTrashOpen(false)}
        loading={trashActionLoading}
      />
    </div>
  );
}

// ====== 工作流编辑器组件 ======
function WorkflowEditor({
  workflow,
  mailboxes,
  users,
  onClose,
  onSave,
}: {
  workflow: WorkflowDto | null;
  mailboxes: { id: number; name: string }[];
  users: { id: number; fullName: string }[];
  onClose: () => void;
  onSave: (data: CreateWorkflowRequest, id?: number) => Promise<void>;
}) {
  const { t } = useI18n();
  const [name, setName] = useState(workflow?.name ?? '');
  const [description, setDescription] = useState(workflow?.description ?? '');
  const [mailboxId, setMailboxId] = useState(workflow?.mailboxId ?? 0);
  const [isActive, setIsActive] = useState(workflow?.isActive ?? true);
  const [sortOrder, setSortOrder] = useState(workflow?.sortOrder ?? 0);
  const [triggerType, setTriggerType] = useState(workflow?.triggerType ?? 'new_conversation');
  const [matchType, setMatchType] = useState(workflow?.conditions?.matchType ?? 'all');
  const [conditions, setConditions] = useState<WorkflowCondition[]>(
    workflow?.conditions?.conditions ?? []
  );
  const [conditionKeys, setConditionKeys] = useState<string[]>(() =>
    (workflow?.conditions?.conditions ?? []).map(() => genId())
  );
  const [actions, setActions] = useState<WorkflowActionDto[]>(workflow?.actions ?? []);
  const [actionKeys, setActionKeys] = useState<string[]>(() =>
    (workflow?.actions ?? []).map(() => genId())
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  // 邮件模版列表（含系统模板和自定义模板）
  const [customTemplates, setCustomTemplates] = useState<MailTemplateDto[]>([]);
  // 标签列表
  const [tags, setTags] = useState<TagResponse[]>([]);
  // 宏列表
  const [macros, setMacros] = useState<MacroDto[]>([]);


  // 加载邮件模版列表（仅启用的）
  useEffect(() => {
    mailTemplateApi
      .list({ page: 1, pageSize: 200, isActive: true })
      .then((resp) => {
        const list = (resp.data ?? []).sort((a, b) => {
          if (a.isSystem !== b.isSystem) return a.isSystem ? -1 : 1;
          return a.sortOrder - b.sortOrder;
        });
        setCustomTemplates(list);
      })
      .catch((e: unknown) => {
        console.error('加载邮件模版失败', e);
      });
  }, []);

  // 加载标签列表（全部邮箱）
  useEffect(() => {
    tagApi
      .list()
      .then((res) => {
        setTags(Array.isArray(res) ? res : []);
      })
      .catch((e: unknown) => {
        console.error('加载标签列表失败', e);
      });
  }, []);

  // 加载宏列表（根据邮箱过滤）
  useEffect(() => {
    const fetchMacros = async () => {
      try {
        const params = { page: 1, pageSize: 200, isActive: true };
        const resp = await macroApi.list(
          mailboxId && mailboxId > 0
            ? { ...params, mailboxId }
            : params
        );
        const data = resp?.data ?? [];
        setMacros(Array.isArray(data) ? data : []);
      } catch (e: unknown) {
        console.error('加载宏列表失败', e);
      }
    };
    fetchMacros();
  }, [mailboxId]);

  const addCondition = () => {
    setConditions([...conditions, { field: 'subject', operator: 'contains', value: '' }]);
    setConditionKeys((prev) => [...prev, genId()]);
  };

  const updateCondition = (i: number, key: keyof WorkflowCondition, value: string) => {
    const next = [...conditions];
    next[i] = { ...next[i], [key]: value };
    setConditions(next);
  };

  const removeCondition = (i: number) => {
    setConditions(conditions.filter((_, idx) => idx !== i));
    setConditionKeys((prev) => prev.filter((_, idx) => idx !== i));
  };

  const addAction = () => {
    setActions([...actions, { type: 'set_status', value: '' }]);
    setActionKeys((prev) => [...prev, genId()]);
  };

  const updateAction = (i: number, key: keyof WorkflowActionDto, value: string) => {
    const next = [...actions];
    next[i] = { ...next[i], [key]: value };
    setActions(next);
  };

  const removeAction = (i: number) => {
    setActions(actions.filter((_, idx) => idx !== i));
    setActionKeys((prev) => prev.filter((_, idx) => idx !== i));
  };

  const handleSubmit = async () => {
    setError('');
    if (!name.trim()) {
      setError(t('automation.workflows.editor.errorNameRequired'));
      return;
    }
    if (actions.length === 0) {
      setError(t('automation.workflows.editor.errorActionRequired'));
      return;
    }

    for (const c of conditions) {
      if (!c.value.trim()) {
        setError(t('automation.workflows.editor.errorConditionValueRequired'));
        return;
      }
    }

    // 校验动作参数
    for (const a of actions) {
      if (a.type === 'send_email' && !a.value?.trim()) {
        setError(t('automation.workflows.editor.errorEmailTemplateRequired'));
        return;
      }
      if (a.type === 'execute_macro' && !a.value?.trim()) {
        setError(t('automation.workflows.editor.errorMacroRequired'));
        return;
      }
    }

    const data: CreateWorkflowRequest = {
      name: name.trim(),
      description: description.trim() || null,
      mailboxId,
      isActive,
      sortOrder,
      triggerType,
      conditions: { matchType, conditions },
      actions,
    };

    setSaving(true);
    try {
      await onSave(data, workflow?.id);
    } catch (_e: unknown) {
      setError(t('automation.workflows.editor.errorSaveRetry'));
    } finally {
      setSaving(false);
    }
  };

  const renderActionValueInput = (action: WorkflowActionDto, i: number) => {
    if (action.type === 'set_status') {
      return (
        <select
          value={action.value ?? ''}
          onChange={(e) => updateAction(i, 'value', e.target.value)}
          className="px-2 py-1 text-xs border border-gray-300 dark:border-slate-600 rounded focus:outline-none focus:ring-1 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
        >
          <option value="">{t('automation.workflows.editor.selectStatus')}</option>
          {STATUS_OPTIONS.map((s) => (
            <option key={s.value} value={s.value}>{t(s.labelKey)}</option>
          ))}
        </select>
      );
    }
    if (action.type === 'conversation_process') {
      return (
        <select
          value={action.value ?? ''}
          onChange={(e) => updateAction(i, 'value', e.target.value)}
          className="px-2 py-1 text-xs border border-gray-300 dark:border-slate-600 rounded focus:outline-none focus:ring-1 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
        >
          <option value="">{t('automation.workflows.editor.selectProcess')}</option>
          <option value="soft_delete">{t('automation.workflows.editor.processSoftDelete')}</option>
          <option value="hard_delete">{t('automation.common.hardDelete')}</option>
        </select>
      );
    }
    if (action.type === 'assign_user') {
      return (
        <select
          value={action.value ?? ''}
          onChange={(e) => updateAction(i, 'value', e.target.value)}
          className="px-2 py-1 text-xs border border-gray-300 dark:border-slate-600 rounded focus:outline-none focus:ring-1 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
        >
          <option value="">{t('automation.workflows.editor.selectAssignee')}</option>
          {users.map((u) => (
            <option key={u.id} value={u.id}>{u.fullName}</option>
          ))}
        </select>
      );
    }
    if (action.type === 'set_priority') {
      return (
        <select
          value={action.value ?? ''}
          onChange={(e) => updateAction(i, 'value', e.target.value)}
          className="px-2 py-1 text-xs border border-gray-300 dark:border-slate-600 rounded focus:outline-none focus:ring-1 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
        >
          <option value="">{t('automation.workflows.editor.selectPriority')}</option>
          {PRIORITY_OPTIONS.map((p) => (
            <option key={p.value} value={p.value}>{t(p.labelKey)}</option>
          ))}
        </select>
      );
    }
    if (action.type === 'send_email') {
      return (
        <div className="flex flex-col gap-1.5 w-full">
          <select
            value={action.value ?? ''}
            onChange={(e) => updateAction(i, 'value', e.target.value)}
            className="px-2 py-1 text-xs border border-gray-300 dark:border-slate-600 rounded focus:outline-none focus:ring-1 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
          >
            <option value="">{t('automation.workflows.editor.selectTemplate')}</option>
            {customTemplates.length === 0 && (
              <option value="" disabled>{t('automation.workflows.editor.noTemplates')}</option>
            )}
            {customTemplates.map((tpl) => (
              <option key={tpl.id} value={tpl.slug}>
                {tpl.isSystem ? t('automation.workflows.editor.systemTemplatePrefix') : ''}{tpl.name}{t('automation.workflows.editor.slugPrefix')}{tpl.slug}{t('automation.workflows.editor.slugSuffix')}
              </option>
            ))}
          </select>
          <input
            type="text"
            value={action.extra ?? ''}
            onChange={(e) => updateAction(i, 'extra', e.target.value)}
            placeholder={t('automation.workflows.editor.extraPlaceholder')}
            className="px-2 py-1 text-xs border border-gray-300 dark:border-slate-600 rounded focus:outline-none focus:ring-1 focus:ring-primary-500 w-full bg-white dark:bg-slate-800 dark:text-slate-200"
          />
          <span className="text-[10px] text-gray-500 dark:text-slate-400">
            {t('automation.workflows.editor.extraHint')}
          </span>
        </div>
      );
    }
    if (action.type === 'execute_macro') {
      return (
        <select
          value={action.value ?? ''}
          onChange={(e) => updateAction(i, 'value', e.target.value)}
          className="px-2 py-1 text-xs border border-gray-300 dark:border-slate-600 rounded focus:outline-none focus:ring-1 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
        >
          <option value="">{t('automation.workflows.editor.selectMacro')}</option>
          {macros.length === 0 && (
            <option value="" disabled>{t('automation.workflows.editor.noMacros')}</option>
          )}
          {macros.map((m) => (
            <option key={m.id} value={String(m.id)}>{m.name}</option>
          ))}
        </select>
      );
    }
    if (action.type === 'add_tag' || action.type === 'remove_tag') {
      return (
        <select
          value={action.value ?? ''}
          onChange={(e) => updateAction(i, 'value', e.target.value)}
          className="px-2 py-1 text-xs border border-gray-300 dark:border-slate-600 rounded focus:outline-none focus:ring-1 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
        >
          <option value="">{t('automation.common.selectTag')}</option>
          {tags.length === 0 && (
            <option value="" disabled>{t('automation.common.noTags')}</option>
          )}
          {tags.map((tg) => (
            <option key={tg.id} value={tg.name}>{tg.name}</option>
          ))}
        </select>
      );
    }
    return (
      <input
        type="text"
        value={action.value ?? ''}
        onChange={(e) => updateAction(i, 'value', e.target.value)}
        placeholder={t('automation.workflows.editor.actionValuePlaceholder')}
        className="px-2 py-1 text-xs border border-gray-300 dark:border-slate-600 rounded focus:outline-none focus:ring-1 focus:ring-primary-500 w-full bg-white dark:bg-slate-800 dark:text-slate-200"
      />
    );
  };

  return (
    <Modal
      open={true}
      onClose={onClose}
      title={workflow ? t('automation.workflows.editor.editTitle') : t('automation.workflows.editor.createTitle')}
      size="xl"
    >
      <div className="space-y-4 max-h-[70vh] overflow-y-auto">
        {workflow?.isSystem && (
          <div className="flex items-center gap-2 px-4 py-3 bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 rounded-lg">
            <Settings className="w-5 h-5 text-blue-600 dark:text-blue-400 flex-shrink-0" />
            <div>
              <div className="text-sm font-medium text-blue-800 dark:text-blue-300">{t('automation.workflows.list.systemGroup')}</div>
              <div className="text-xs text-blue-600 dark:text-blue-400">{t('automation.workflows.editor.systemBanner')}</div>
            </div>
          </div>
        )}

        {/* 基本信息 */}
        <div className="grid grid-cols-2 gap-3">
          <div className="col-span-2">
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('automation.workflows.editor.nameLabel')}</label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              disabled={workflow?.isSystem}
              placeholder={t('automation.workflows.editor.namePlaceholder')}
              className={`w-full px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200 ${workflow?.isSystem ? 'opacity-60 cursor-not-allowed' : ''}`}
            />
          </div>
          <div className="col-span-2">
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('automation.common.description')}</label>
            <input
              type="text"
              value={description ?? ''}
              onChange={(e) => setDescription(e.target.value)}
              placeholder={t('automation.workflows.editor.descriptionPlaceholder')}
              className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('automation.workflows.editor.triggerType')}</label>
            <select
              value={triggerType}
              onChange={(e) => setTriggerType(e.target.value)}
              disabled={workflow?.isSystem}
              className={`w-full px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200 ${workflow?.isSystem ? 'opacity-60 cursor-not-allowed' : ''}`}
            >
              {WORKFLOW_TRIGGER_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>{t(o.labelKey)}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('automation.workflows.editor.mailbox')}</label>
            <select
              value={mailboxId}
              onChange={(e) => setMailboxId(Number(e.target.value))}
              disabled={workflow?.isSystem}
              className={`w-full px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200 ${workflow?.isSystem ? 'opacity-60 cursor-not-allowed' : ''}`}
            >
              <option value={0}>{t('automation.common.allMailboxes')}</option>
              {mailboxes.map((m) => (
                <option key={m.id} value={m.id}>{m.name}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('automation.workflows.editor.sortOrder')}</label>
            <input
              type="number"
              value={sortOrder}
              onChange={(e) => setSortOrder(Number(e.target.value))}
              className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('automation.common.enableStatus')}</label>
            <label className="flex items-center gap-2 mt-2">
              <input
                type="checkbox"
                checked={isActive}
                onChange={(e) => setIsActive(e.target.checked)}
                className="w-4 h-4 rounded text-primary-600 focus:ring-primary-500"
              />
              <span className="text-sm text-gray-700">{t('automation.workflows.editor.enableRule')}</span>
            </label>
          </div>
        </div>

        {/* 条件配置 */}
        <div className="bg-gradient-to-br from-gray-50 to-gray-100 dark:from-slate-900 dark:to-slate-800 rounded-xl p-5 border border-gray-200 dark:border-slate-700">
          <div className="flex items-center justify-between mb-4">
            <div className="flex items-center gap-2">
              <AlertCircle className="w-4 h-4 text-amber-500" />
              <h4 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('automation.workflows.editor.conditions')}</h4>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-xs text-gray-500 dark:text-slate-400">{t('automation.workflows.editor.matchType')}</span>
              <select
                value={matchType}
                onChange={(e) => setMatchType(e.target.value)}
                className="px-2 py-1 text-xs border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-1 focus:ring-primary-500 bg-white dark:bg-slate-800"
              >
                <option value="all">{t('automation.workflows.editor.matchAll')}</option>
                <option value="any">{t('automation.workflows.editor.matchAny')}</option>
              </select>
            </div>
          </div>
          <div className="space-y-3">
            {conditions.length === 0 && (
              <div className="text-xs text-gray-400 text-center py-6 border border-dashed border-gray-300 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-800">
                {t('automation.workflows.editor.noConditions')}
              </div>
            )}
            {conditions.map((c, i) => (
              <div key={conditionKeys[i] ?? i} className="flex items-center gap-2 bg-white dark:bg-slate-800 p-3 rounded-lg border border-gray-200 dark:border-slate-700 shadow-sm">
                <select
                  value={c.field}
                  onChange={(e) => updateCondition(i, 'field', e.target.value)}
                  className="px-2 py-1.5 text-xs border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-1 focus:ring-primary-500 bg-white dark:bg-slate-800 min-w-[120px]"
                >
                  {WORKFLOW_FIELD_CODES.map((code) => (
                    <option key={code} value={code}>{workflowFieldLabel(t, code)}</option>
                  ))}
                </select>
                <select
                  value={c.operator}
                  onChange={(e) => updateCondition(i, 'operator', e.target.value)}
                  className="px-2 py-1.5 text-xs border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-1 focus:ring-primary-500 bg-white dark:bg-slate-800 min-w-[100px]"
                >
                  {WORKFLOW_OPERATOR_CODES.map((code) => (
                    <option key={code} value={code}>{workflowOperatorLabel(t, code)}</option>
                  ))}
                </select>
                {c.field === 'mailbox' ? (
                  <select
                    value={c.value}
                    onChange={(e) => updateCondition(i, 'value', e.target.value)}
                    className="flex-1 px-2 py-1.5 text-xs border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-1 focus:ring-primary-500 bg-white dark:bg-slate-800"
                  >
                    <option value="">{t('automation.workflows.editor.selectMailbox')}</option>
                    {mailboxes.map((m) => (
                      <option key={m.id} value={m.id}>{m.name}</option>
                    ))}
                  </select>
                ) : (
                  <input
                    type="text"
                    value={c.value}
                    onChange={(e) => updateCondition(i, 'value', e.target.value)}
                    placeholder={t('automation.workflows.editor.matchValue')}
                    className="flex-1 px-2 py-1.5 text-xs border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-1 focus:ring-primary-500 bg-white dark:bg-slate-800"
                  />
                )}
                <button
                  onClick={() => removeCondition(i)}
                  className="text-red-500 hover:bg-red-50 rounded-lg p-1.5 transition-colors"
                  title={t('automation.workflows.editor.removeCondition')}
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </div>
            ))}
          </div>
          <button
            onClick={addCondition}
            className="mt-4 text-xs text-primary-600 hover:text-primary-700 font-medium flex items-center gap-1.5"
          >
            <Plus className="w-4 h-4" />
            {t('automation.workflows.editor.addCondition')}
          </button>
        </div>

        {/* 动作配置 */}
        <div className="bg-gradient-to-br from-blue-50 to-blue-100 dark:from-blue-900/30 dark:to-blue-800/20 rounded-xl p-5 border border-blue-200 dark:border-blue-700">
          <div className="flex items-center gap-2 mb-4">
            <Zap className="w-4 h-4 text-blue-500" />
            <h4 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('automation.workflows.editor.actions')}</h4>
          </div>
          <div className="space-y-3">
            {actions.length === 0 && (
              <div className="text-xs text-gray-400 text-center py-6 border border-dashed border-gray-300 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-800">
                {t('automation.workflows.editor.noActions')}
              </div>
            )}
            {actions.map((a, i) => (
              <div key={actionKeys[i] ?? i} className="flex items-center gap-2 bg-white dark:bg-slate-800 p-3 rounded-lg border border-gray-200 dark:border-slate-700 shadow-sm">
                <select
                  value={a.type}
                  onChange={(e) => updateAction(i, 'type', e.target.value)}
                  className="px-2 py-1.5 text-xs border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-1 focus:ring-primary-500 bg-white dark:bg-slate-800 min-w-[120px]"
                >
                  {WORKFLOW_ACTION_CODES.map((code) => (
                    <option key={code} value={code}>{workflowActionLabel(t, code)}</option>
                  ))}
                </select>
                <div className="flex-1">{renderActionValueInput(a, i)}</div>
                <button
                  onClick={() => removeAction(i)}
                  className="text-red-500 hover:bg-red-50 rounded-lg p-1.5 transition-colors"
                  title={t('automation.workflows.editor.removeAction')}
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </div>
            ))}
          </div>
          <button
            onClick={addAction}
            className="mt-4 text-xs text-primary-600 hover:text-primary-700 font-medium flex items-center gap-1.5"
          >
            <Plus className="w-4 h-4" />
            {t('automation.workflows.editor.addAction')}
          </button>
        </div>

        {error && (
          <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-3 text-sm text-red-700 dark:text-red-400">
            {error}
          </div>
        )}
      </div>

      <div className="flex justify-end gap-2 mt-4 pt-4 border-t border-gray-200 dark:border-slate-700">
        <button
          onClick={onClose}
          className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700"
        >
          {t('common.cancel')}
        </button>
        <button
          onClick={handleSubmit}
          disabled={saving}
          className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-lg hover:bg-primary-700 disabled:opacity-50"
        >
          {saving ? t('automation.common.saving') : t('common.save')}
        </button>
      </div>
    </Modal>
  );
}
