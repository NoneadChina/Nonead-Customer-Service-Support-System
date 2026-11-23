import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { auditLogApi } from '../api';
import type {
  AuditLogListResponse,
  AuditLogResponse,
  AuditLogStatsResponse,
  AuditLogIntegrityResult,
  AuditLogRelatedResponse,
  AuditIntegrityReport,
} from '../api/types';
import { formatDateTime, formatRelative } from '../utils/format';
import { getApiErrorMessage } from '../utils/error';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import Modal from '../components/Modal';
import UserAvatar from '../components/UserAvatar';
import {
  ShieldCheck,
  ShieldX,
  LogIn,
  LogOut,
  Plus,
  Edit,
  Trash2,
  UserCheck,
  RefreshCw,
  MessageSquare,
  StickyNote,
  KeyRound,
  Lock,
  AlertTriangle,
  FileText,
  Users,
  Mail,
  Tag,
  User,
  Paperclip,
  Folder,
  Shield,
  Workflow,
  Send,
  Webhook,
  Clock,
  Zap,
  ListChecks,
  Megaphone,
  Key,
  Calendar,
  BookOpen,
  PenLine,
  Bell,
  Eye,
  RotateCcw,
  Settings,
  Star,
  ChevronLeft,
  ChevronRight,
  Globe,
  Monitor,
  Activity,
  BarChart3,
  Hash,
  Search,
  X,
  Download,
  Loader2,
  Link2,
  type LucideIcon,
} from 'lucide-react';

// ===== 操作类型枚举（统一小写匹配，后端已支持大小写不敏感）=====
// 字段名刻意用 `labelKey`（TranslationKey）而非渲染好的字符串：任何漏包 `t()` 的使用点
// 都会被 `tsc` 以 TS2339 抓出来。
const actionOptions: { value: string; labelKey: TranslationKey }[] = [
  { value: '', labelKey: 'data.audit.action.all' },
  { value: 'login', labelKey: 'data.audit.action.login' },
  { value: 'logout', labelKey: 'data.audit.action.logout' },
  { value: 'loginfailed', labelKey: 'data.audit.action.loginfailed' },
  { value: 'lockout', labelKey: 'data.audit.action.lockout' },
  { value: 'create', labelKey: 'data.audit.action.create' },
  { value: 'update', labelKey: 'data.audit.action.update' },
  { value: 'delete', labelKey: 'data.audit.action.delete' },
  { value: 'assign', labelKey: 'data.audit.action.assign' },
  { value: 'status_change', labelKey: 'data.audit.action.status_change' },
  { value: 'reply', labelKey: 'data.audit.action.reply' },
  { value: 'note', labelKey: 'data.audit.action.note' },
  { value: 'addnote', labelKey: 'data.audit.action.addnote' },
  { value: 'password_change', labelKey: 'data.audit.action.password_change' },
  { value: 'reset', labelKey: 'data.audit.action.reset' },
  { value: 'resetfailed', labelKey: 'data.audit.action.resetfailed' },
  { value: 'createdraft', labelKey: 'data.audit.action.createdraft' },
];

const entityTypeOptions: { value: string; labelKey: TranslationKey }[] = [
  { value: '', labelKey: 'data.common.allTypes' },
  { value: 'User', labelKey: 'data.audit.entityType.user' },
  { value: 'Customer', labelKey: 'data.audit.entityType.customer' },
  { value: 'Conversation', labelKey: 'data.audit.entityType.conversation' },
  { value: 'ConversationThread', labelKey: 'data.audit.entityType.conversationthread' },
  { value: 'Mailbox', labelKey: 'data.audit.entityType.mailbox' },
  { value: 'Tag', labelKey: 'data.audit.entityType.tag' },
  { value: 'Folder', labelKey: 'data.audit.entityType.folder' },
  { value: 'Attachment', labelKey: 'data.audit.entityType.attachment' },
  { value: 'Auth', labelKey: 'data.audit.entityType.auth' },
  { value: 'Draft', labelKey: 'data.audit.entityType.draft' },
  { value: 'SendLog', labelKey: 'data.audit.entityType.sendlog' },
  { value: 'Workflow', labelKey: 'data.audit.entityType.workflow' },
  { value: 'Role', labelKey: 'data.audit.entityType.role' },
  { value: 'Team', labelKey: 'data.audit.entityType.team' },
  { value: 'MailTemplate', labelKey: 'data.audit.entityType.mailtemplate' },
  { value: 'CannedResponse', labelKey: 'data.audit.entityType.cannedresponse' },
  { value: 'Webhook', labelKey: 'data.audit.entityType.webhook' },
  { value: 'Sla', labelKey: 'data.audit.entityType.sla' },
  { value: 'Macro', labelKey: 'data.audit.entityType.macro' },
  { value: 'CustomField', labelKey: 'data.audit.entityType.customfield' },
  { value: 'NotificationRule', labelKey: 'data.audit.entityType.notificationrule' },
  { value: 'NotificationTemplate', labelKey: 'data.audit.entityType.notificationtemplate' },
  { value: 'ApiKey', labelKey: 'data.audit.entityType.apikey' },
  { value: 'CalendarEvent', labelKey: 'data.audit.entityType.calendarevent' },
  { value: 'KnowledgeArticle', labelKey: 'data.audit.entityType.knowledgearticle' },
  { value: 'EmailSignature', labelKey: 'data.audit.entityType.emailsignature' },
];

/** 动作徽章：词条键 + 样式。`labelKey` 是 TranslationKey，漏包 `t()` 的使用点会被 tsc 抓出 */
type ActionStyle = { labelKey: TranslationKey; color: string; ring: string; icon: LucideIcon };
/** 未知动作的兜底：没有词条，按改造前行为渲染原始标识符（纯文本字段，类型上与 labelKey 区分） */
type ActionStyleFallback = { labelText: string; color: string; ring: string; icon: LucideIcon };
/** 未知实体类型的兜底，同上 */
type EntityTypeStyle = { labelKey: TranslationKey; color: string; icon: LucideIcon };
type EntityTypeStyleFallback = { labelText: string; color: string; icon: LucideIcon };

// 操作类型样式映射（key 小写）。`| undefined` 是刻意的：索引访问在运行期可能落空，
// 写成 `Record<string, ActionStyle>` 会让 getActionStyle 的兜底分支在类型上不可达 —— 那是类型谎言。
const actionStyles: Record<string, ActionStyle | undefined> = {
  login: { labelKey: 'data.audit.action.login', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', ring: 'ring-blue-500/20', icon: LogIn },
  logout: { labelKey: 'data.audit.action.logout', color: 'bg-slate-100 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', ring: 'ring-slate-500/20', icon: LogOut },
  loginfailed: { labelKey: 'data.audit.action.loginfailed', color: 'bg-orange-50 text-orange-700 dark:bg-orange-500/10 dark:text-orange-400', ring: 'ring-orange-500/20', icon: AlertTriangle },
  lockout: { labelKey: 'data.audit.action.lockout', color: 'bg-red-50 text-red-700 dark:bg-red-500/10 dark:text-red-400', ring: 'ring-red-500/20', icon: Lock },
  create: { labelKey: 'data.audit.action.create', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', ring: 'ring-emerald-500/20', icon: Plus },
  update: { labelKey: 'data.audit.action.update', color: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400', ring: 'ring-amber-500/20', icon: Edit },
  delete: { labelKey: 'data.audit.action.delete', color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400', ring: 'ring-rose-500/20', icon: Trash2 },
  assign: { labelKey: 'data.audit.action.assign', color: 'bg-violet-50 text-violet-700 dark:bg-violet-500/10 dark:text-violet-400', ring: 'ring-violet-500/20', icon: UserCheck },
  status_change: { labelKey: 'data.audit.action.status_change', color: 'bg-cyan-50 text-cyan-700 dark:bg-cyan-500/10 dark:text-cyan-400', ring: 'ring-cyan-500/20', icon: RefreshCw },
  reply: { labelKey: 'data.audit.action.reply', color: 'bg-teal-50 text-teal-700 dark:bg-teal-500/10 dark:text-teal-400', ring: 'ring-teal-500/20', icon: MessageSquare },
  note: { labelKey: 'data.audit.action.note', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', ring: 'ring-indigo-500/20', icon: StickyNote },
  addnote: { labelKey: 'data.audit.action.addnote', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', ring: 'ring-indigo-500/20', icon: StickyNote },
  password_change: { labelKey: 'data.audit.action.password_change', color: 'bg-pink-50 text-pink-700 dark:bg-pink-500/10 dark:text-pink-400', ring: 'ring-pink-500/20', icon: KeyRound },
  reset: { labelKey: 'data.audit.action.reset', color: 'bg-pink-50 text-pink-700 dark:bg-pink-500/10 dark:text-pink-400', ring: 'ring-pink-500/20', icon: KeyRound },
  resetfailed: { labelKey: 'data.audit.action.resetfailed', color: 'bg-orange-50 text-orange-700 dark:bg-orange-500/10 dark:text-orange-400', ring: 'ring-orange-500/20', icon: AlertTriangle },
  createdraft: { labelKey: 'data.audit.action.createdraft', color: 'bg-fuchsia-50 text-fuchsia-700 dark:bg-fuchsia-500/10 dark:text-fuchsia-400', ring: 'ring-fuchsia-500/20', icon: FileText },
  // 日历事件（CalendarMutationService 显式写入的 calendar.* 动作）
  'calendar.create': { labelKey: 'data.audit.action.calendarCreate', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', ring: 'ring-emerald-500/20', icon: Calendar },
  'calendar.update': { labelKey: 'data.audit.action.calendarUpdate', color: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400', ring: 'ring-amber-500/20', icon: Calendar },
  'calendar.delete': { labelKey: 'data.audit.action.calendarDelete', color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400', ring: 'ring-rose-500/20', icon: Calendar },
  'calendar.status_change': { labelKey: 'data.audit.action.calendarStatusChange', color: 'bg-cyan-50 text-cyan-700 dark:bg-cyan-500/10 dark:text-cyan-400', ring: 'ring-cyan-500/20', icon: RefreshCw },
  'calendar.duplicate': { labelKey: 'data.audit.action.calendarDuplicate', color: 'bg-violet-50 text-violet-700 dark:bg-violet-500/10 dark:text-violet-400', ring: 'ring-violet-500/20', icon: Calendar },
  'calendar.quick_update': { labelKey: 'data.audit.action.calendarQuickUpdate', color: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400', ring: 'ring-amber-500/20', icon: Calendar },
  'calendar.restore': { labelKey: 'data.audit.action.calendarRestore', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', ring: 'ring-emerald-500/20', icon: RotateCcw },
  'calendar.hard_delete': { labelKey: 'data.audit.action.calendarHardDelete', color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400', ring: 'ring-rose-500/20', icon: Trash2 },
  'calendar.clear_trash': { labelKey: 'data.audit.action.calendarClearTrash', color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400', ring: 'ring-rose-500/20', icon: Trash2 },
  'calendar.purge_trash': { labelKey: 'data.audit.action.calendarPurgeTrash', color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400', ring: 'ring-rose-500/20', icon: Trash2 },
  'calendar.reminder_confirmed': { labelKey: 'data.audit.action.calendarReminderConfirmed', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', ring: 'ring-blue-500/20', icon: Bell },
  // 工时（TimeEntryService 写入的 time_entry.* 动作）
  'time_entry.create': { labelKey: 'data.audit.action.timeEntryCreate', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', ring: 'ring-emerald-500/20', icon: Clock },
  'time_entry.update': { labelKey: 'data.audit.action.timeEntryUpdate', color: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400', ring: 'ring-amber-500/20', icon: Clock },
  'time_entry.delete': { labelKey: 'data.audit.action.timeEntryDelete', color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400', ring: 'ring-rose-500/20', icon: Clock },
  'time_entry.timer_start': { labelKey: 'data.audit.action.timeEntryTimerStart', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', ring: 'ring-emerald-500/20', icon: Clock },
  'time_entry.timer_stop': { labelKey: 'data.audit.action.timeEntryTimerStop', color: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400', ring: 'ring-amber-500/20', icon: Clock },
  'time_entry.restore': { labelKey: 'data.audit.action.timeEntryRestore', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', ring: 'ring-emerald-500/20', icon: RotateCcw },
  // 客户/会话 业务动作（Customer/Conversation 服务显式写入）
  restore: { labelKey: 'data.audit.action.restore', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', ring: 'ring-emerald-500/20', icon: RotateCcw },
  hard_delete: { labelKey: 'data.audit.action.hardDelete', color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400', ring: 'ring-rose-500/20', icon: Trash2 },
  bulk_soft_delete: { labelKey: 'data.audit.action.bulkSoftDelete', color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400', ring: 'ring-rose-500/20', icon: Trash2 },
  bulk_hard_delete: { labelKey: 'data.audit.action.bulkHardDelete', color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400', ring: 'ring-rose-500/20', icon: Trash2 },
  bulk_restore: { labelKey: 'data.audit.action.bulkRestore', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', ring: 'ring-emerald-500/20', icon: RotateCcw },
  // 日历批量操作（CalendarMutationService 动态拼接 calendar.batch_{op}[_result]，op ∈ delete/restore/setstatus/seteventtype/moveoffset）
  'calendar.batch_delete': { labelKey: 'data.audit.action.calendarBatchDelete', color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400', ring: 'ring-rose-500/20', icon: Trash2 },
  'calendar.batch_setstatus': { labelKey: 'data.audit.action.calendarBatchSetstatus', color: 'bg-cyan-50 text-cyan-700 dark:bg-cyan-500/10 dark:text-cyan-400', ring: 'ring-cyan-500/20', icon: RefreshCw },
  'calendar.batch_seteventtype': { labelKey: 'data.audit.action.calendarBatchSeteventtype', color: 'bg-violet-50 text-violet-700 dark:bg-violet-500/10 dark:text-violet-400', ring: 'ring-violet-500/20', icon: Calendar },
  'calendar.batch_moveoffset': { labelKey: 'data.audit.action.calendarBatchMoveoffset', color: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400', ring: 'ring-amber-500/20', icon: Calendar },
  'calendar.batch_restore': { labelKey: 'data.audit.action.calendarBatchRestore', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', ring: 'ring-emerald-500/20', icon: RotateCcw },
  'calendar.batch_delete_result': { labelKey: 'data.audit.action.calendarBatchDeleteResult', color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400', ring: 'ring-rose-500/20', icon: Trash2 },
  'calendar.batch_setstatus_result': { labelKey: 'data.audit.action.calendarBatchSetstatusResult', color: 'bg-cyan-50 text-cyan-700 dark:bg-cyan-500/10 dark:text-cyan-400', ring: 'ring-cyan-500/20', icon: RefreshCw },
  'calendar.batch_seteventtype_result': { labelKey: 'data.audit.action.calendarBatchSeteventtypeResult', color: 'bg-violet-50 text-violet-700 dark:bg-violet-500/10 dark:text-violet-400', ring: 'ring-violet-500/20', icon: Calendar },
  'calendar.batch_moveoffset_result': { labelKey: 'data.audit.action.calendarBatchMoveoffsetResult', color: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400', ring: 'ring-amber-500/20', icon: Calendar },
  'calendar.batch_restore_result': { labelKey: 'data.audit.action.calendarBatchRestoreResult', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', ring: 'ring-emerald-500/20', icon: RotateCcw },
};

const entityTypeStyles: Record<string, EntityTypeStyle | undefined> = {
  user: { labelKey: 'data.audit.entityType.user', color: 'bg-slate-50 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', icon: Users },
  customer: { labelKey: 'data.audit.entityType.customer', color: 'bg-orange-50 text-orange-700 dark:bg-orange-500/10 dark:text-orange-400', icon: User },
  conversation: { labelKey: 'data.audit.entityType.conversation', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', icon: MessageSquare },
  conversationthread: { labelKey: 'data.audit.entityType.conversationthread', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', icon: MessageSquare },
  mailbox: { labelKey: 'data.audit.entityType.mailbox', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: Mail },
  tag: { labelKey: 'data.audit.entityType.tag', color: 'bg-pink-50 text-pink-700 dark:bg-pink-500/10 dark:text-pink-400', icon: Tag },
  folder: { labelKey: 'data.audit.entityType.folder', color: 'bg-violet-50 text-violet-700 dark:bg-violet-500/10 dark:text-violet-400', icon: Folder },
  attachment: { labelKey: 'data.audit.entityType.attachment', color: 'bg-yellow-50 text-yellow-700 dark:bg-yellow-500/10 dark:text-yellow-400', icon: Paperclip },
  auth: { labelKey: 'data.audit.entityType.auth', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: Shield },
  draft: { labelKey: 'data.audit.entityType.draft', color: 'bg-fuchsia-50 text-fuchsia-700 dark:bg-fuchsia-500/10 dark:text-fuchsia-400', icon: FileText },
  sendlog: { labelKey: 'data.audit.entityType.sendlog', color: 'bg-cyan-50 text-cyan-700 dark:bg-cyan-500/10 dark:text-cyan-400', icon: Send },
  workflow: { labelKey: 'data.audit.entityType.workflow', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: Workflow },
  role: { labelKey: 'data.audit.entityType.role', color: 'bg-slate-50 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', icon: Shield },
  team: { labelKey: 'data.audit.entityType.team', color: 'bg-teal-50 text-teal-700 dark:bg-teal-500/10 dark:text-teal-400', icon: Users },
  mailtemplate: { labelKey: 'data.audit.entityType.mailtemplate', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: Mail },
  cannedresponse: { labelKey: 'data.audit.entityType.cannedresponse', color: 'bg-teal-50 text-teal-700 dark:bg-teal-500/10 dark:text-teal-400', icon: MessageSquare },
  webhook: { labelKey: 'data.audit.entityType.webhook', color: 'bg-fuchsia-50 text-fuchsia-700 dark:bg-fuchsia-500/10 dark:text-fuchsia-400', icon: Webhook },
  sla: { labelKey: 'data.audit.entityType.sla', color: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400', icon: Clock },
  macro: { labelKey: 'data.audit.entityType.macro', color: 'bg-violet-50 text-violet-700 dark:bg-violet-500/10 dark:text-violet-400', icon: Zap },
  customfield: { labelKey: 'data.audit.entityType.customfield', color: 'bg-slate-50 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', icon: ListChecks },
  notificationrule: { labelKey: 'data.audit.entityType.notificationrule', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: Bell },
  notificationtemplate: { labelKey: 'data.audit.entityType.notificationtemplate', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: Megaphone },
  apikey: { labelKey: 'data.audit.entityType.apikey', color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400', icon: Key },
  calendarevent: { labelKey: 'data.audit.entityType.calendarevent', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', icon: Calendar },
  knowledgearticle: { labelKey: 'data.audit.entityType.knowledgearticle', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', icon: BookOpen },
  emailsignature: { labelKey: 'data.audit.entityType.emailsignature', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: PenLine },
  workflowexecutionlog: { labelKey: 'data.audit.entityType.workflowexecutionlog', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: Workflow },
  systemoption: { labelKey: 'data.audit.entityType.systemoption', color: 'bg-slate-50 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', icon: Settings },
  // —— 以下为自动拦截器（AuditSaveChangesInterceptor）可能记录的其他实体类名，避免界面出现英文 ——
  apikeylog: { labelKey: 'data.audit.entityType.apikeylog', color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400', icon: Key },
  calendareventtemplate: { labelKey: 'data.audit.entityType.calendareventtemplate', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', icon: Calendar },
  calendarreminderattempt: { labelKey: 'data.audit.entityType.calendarreminderattempt', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: Bell },
  chatmessage: { labelKey: 'data.audit.entityType.chatmessage', color: 'bg-teal-50 text-teal-700 dark:bg-teal-500/10 dark:text-teal-400', icon: MessageSquare },
  chatsession: { labelKey: 'data.audit.entityType.chatsession', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', icon: MessageSquare },
  conversationfolder: { labelKey: 'data.audit.entityType.conversationfolder', color: 'bg-violet-50 text-violet-700 dark:bg-violet-500/10 dark:text-violet-400', icon: Folder },
  conversationreadstate: { labelKey: 'data.audit.entityType.conversationreadstate', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', icon: Eye },
  conversationtag: { labelKey: 'data.audit.entityType.conversationtag', color: 'bg-pink-50 text-pink-700 dark:bg-pink-500/10 dark:text-pink-400', icon: Tag },
  customfieldvalue: { labelKey: 'data.audit.entityType.customfieldvalue', color: 'bg-slate-50 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', icon: ListChecks },
  customreport: { labelKey: 'data.audit.entityType.customreport', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: BarChart3 },
  customreportschedule: { labelKey: 'data.audit.entityType.customreportschedule', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: Clock },
  distributedlock: { labelKey: 'data.audit.entityType.distributedlock', color: 'bg-slate-50 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', icon: Lock },
  email: { labelKey: 'data.audit.entityType.email', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: Mail },
  follower: { labelKey: 'data.audit.entityType.follower', color: 'bg-pink-50 text-pink-700 dark:bg-pink-500/10 dark:text-pink-400', icon: User },
  immessagelog: { labelKey: 'data.audit.entityType.immessagelog', color: 'bg-cyan-50 text-cyan-700 dark:bg-cyan-500/10 dark:text-cyan-400', icon: Send },
  knowledgearticleattachment: { labelKey: 'data.audit.entityType.knowledgearticleattachment', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', icon: Paperclip },
  knowledgecategory: { labelKey: 'data.audit.entityType.knowledgecategory', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', icon: BookOpen },
  macroexecutionlog: { labelKey: 'data.audit.entityType.macroexecutionlog', color: 'bg-violet-50 text-violet-700 dark:bg-violet-500/10 dark:text-violet-400', icon: Zap },
  mailboxuser: { labelKey: 'data.audit.entityType.mailboxuser', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: Mail },
  moduleentity: { labelKey: 'data.audit.entityType.moduleentity', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: Monitor },
  outlookeventmapping: { labelKey: 'data.audit.entityType.outlookeventmapping', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: Calendar },
  outlooksynclog: { labelKey: 'data.audit.entityType.outlooksynclog', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: RefreshCw },
  outlooksyncqueueitem: { labelKey: 'data.audit.entityType.outlooksyncqueueitem', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: Clock },
  outlooksyncsetting: { labelKey: 'data.audit.entityType.outlooksyncsetting', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: Settings },
  passwordresettoken: { labelKey: 'data.audit.entityType.passwordresettoken', color: 'bg-pink-50 text-pink-700 dark:bg-pink-500/10 dark:text-pink-400', icon: KeyRound },
  refreshtoken: { labelKey: 'data.audit.entityType.refreshtoken', color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400', icon: Key },
  satisfactionrating: { labelKey: 'data.audit.entityType.satisfactionrating', color: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400', icon: Star },
  slapolicy: { labelKey: 'data.audit.entityType.slapolicy', color: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400', icon: Clock },
  slaviolation: { labelKey: 'data.audit.entityType.slaviolation', color: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400', icon: AlertTriangle },
  star: { labelKey: 'data.audit.entityType.star', color: 'bg-yellow-50 text-yellow-700 dark:bg-yellow-500/10 dark:text-yellow-400', icon: Star },
  teammember: { labelKey: 'data.audit.entityType.teammember', color: 'bg-teal-50 text-teal-700 dark:bg-teal-500/10 dark:text-teal-400', icon: Users },
  timeentry: { labelKey: 'data.audit.entityType.timeentry', color: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400', icon: Clock },
  translation: { labelKey: 'data.audit.entityType.translation', color: 'bg-slate-50 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', icon: Globe },
  usernotification: { labelKey: 'data.audit.entityType.usernotification', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: Bell },
  userrole: { labelKey: 'data.audit.entityType.userrole', color: 'bg-slate-50 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', icon: Shield },
  webhooklog: { labelKey: 'data.audit.entityType.webhooklog', color: 'bg-fuchsia-50 text-fuchsia-700 dark:bg-fuchsia-500/10 dark:text-fuchsia-400', icon: Webhook },
  // —— 控制器名推断（AuditActionFilter.GetEntityTypeFromController：去 Controller 后缀 + 去尾 s）——
  auditlog: { labelKey: 'data.audit.entityType.auditlog', color: 'bg-slate-50 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', icon: ShieldCheck },
  calendar: { labelKey: 'data.audit.entityType.calendar', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', icon: Calendar },
  calendarfeed: { labelKey: 'data.audit.entityType.calendarfeed', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', icon: Calendar },
  chat: { labelKey: 'data.audit.entityType.chat', color: 'bg-teal-50 text-teal-700 dark:bg-teal-500/10 dark:text-teal-400', icon: MessageSquare },
  dashboard: { labelKey: 'data.audit.entityType.dashboard', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: BarChart3 },
  dingding: { labelKey: 'data.audit.entityType.dingding', color: 'bg-cyan-50 text-cyan-700 dark:bg-cyan-500/10 dark:text-cyan-400', icon: MessageSquare },
  emailtest: { labelKey: 'data.audit.entityType.emailtest', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: Mail },
  feishu: { labelKey: 'data.audit.entityType.feishu', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: MessageSquare },
  importexport: { labelKey: 'data.audit.entityType.importexport', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: Download },
  knowledge: { labelKey: 'data.audit.entityType.knowledge', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', icon: BookOpen },
  localization: { labelKey: 'data.audit.entityType.localization', color: 'bg-slate-50 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', icon: Globe },
  mail: { labelKey: 'data.audit.entityType.mail', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: Mail },
  mailboxe: { labelKey: 'data.audit.entityType.mailboxe', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: Mail },
  module: { labelKey: 'data.audit.entityType.module', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: Monitor },
  notification: { labelKey: 'data.audit.entityType.notification', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: Bell },
  oauth: { labelKey: 'data.audit.entityType.oauth', color: 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-400', icon: Key },
  outlooksync: { labelKey: 'data.audit.entityType.outlooksync', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: RefreshCw },
  profile: { labelKey: 'data.audit.entityType.profile', color: 'bg-slate-50 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', icon: User },
  realtime: { labelKey: 'data.audit.entityType.realtime', color: 'bg-cyan-50 text-cyan-700 dark:bg-cyan-500/10 dark:text-cyan-400', icon: Activity },
  report: { labelKey: 'data.audit.entityType.report', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: BarChart3 },
  search: { labelKey: 'data.audit.entityType.search', color: 'bg-slate-50 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', icon: Search },
  setup: { labelKey: 'data.audit.entityType.setup', color: 'bg-slate-50 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', icon: Settings },
  system: { labelKey: 'data.audit.entityType.system', color: 'bg-slate-50 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', icon: Settings },
  timetracking: { labelKey: 'data.audit.entityType.timetracking', color: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400', icon: Clock },
  tracking: { labelKey: 'data.audit.entityType.tracking', color: 'bg-cyan-50 text-cyan-700 dark:bg-cyan-500/10 dark:text-cyan-400', icon: Activity },
  userprofile: { labelKey: 'data.audit.entityType.userprofile', color: 'bg-slate-50 text-slate-700 dark:bg-slate-500/10 dark:text-slate-400', icon: User },
  wechat: { labelKey: 'data.audit.entityType.wechat', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', icon: MessageSquare },
  whatsapp: { labelKey: 'data.audit.entityType.whatsapp', color: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400', icon: MessageSquare },
  messenger: { labelKey: 'data.audit.entityType.messenger', color: 'bg-violet-50 text-violet-700 dark:bg-violet-500/10 dark:text-violet-400', icon: MessageSquare },
  telegram: { labelKey: 'data.audit.entityType.telegram', color: 'bg-sky-50 text-sky-700 dark:bg-sky-500/10 dark:text-sky-400', icon: MessageSquare },
  line: { labelKey: 'data.audit.entityType.line', color: 'bg-green-50 text-green-700 dark:bg-green-500/10 dark:text-green-400', icon: MessageSquare },
  imessage: { labelKey: 'data.audit.entityType.imessage', color: 'bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400', icon: MessageSquare },

  // —— EF 实体类名（拦截器用 `entry.Entity.GetType().Name` 写入，故类名即码值）——
  customerphone: { labelKey: 'data.audit.entityType.customerphone', color: 'bg-orange-50 text-orange-700 dark:bg-orange-500/10 dark:text-orange-400', icon: User },
  customersocialprofile: { labelKey: 'data.audit.entityType.customersocialprofile', color: 'bg-orange-50 text-orange-700 dark:bg-orange-500/10 dark:text-orange-400', icon: User },
  macroaction: { labelKey: 'data.audit.entityType.macroaction', color: 'bg-violet-50 text-violet-700 dark:bg-violet-500/10 dark:text-violet-400', icon: Zap },
  workflowaction: { labelKey: 'data.audit.entityType.workflowaction', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: Workflow },
  workflowcondition: { labelKey: 'data.audit.entityType.workflowcondition', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: Workflow },
  workflowconditiongroup: { labelKey: 'data.audit.entityType.workflowconditiongroup', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: Workflow },
  workflowevaluationcontext: { labelKey: 'data.audit.entityType.workflowevaluationcontext', color: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-400', icon: Workflow },
};

const getActionStyle = (action: string): ActionStyle | ActionStyleFallback => {
  const key = action.toLowerCase();
  const exact = actionStyles[key];
  if (exact) return exact;
  if (key.startsWith('calendar.')) {
    return { labelKey: 'data.audit.action.calendarFallback', color: 'bg-cyan-50 text-cyan-700 dark:bg-cyan-500/10 dark:text-cyan-400', ring: 'ring-cyan-500/20', icon: Calendar };
  }
  if (key.startsWith('time_entry.')) {
    return { labelKey: 'data.audit.action.timeEntryFallback', color: 'bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400', ring: 'ring-amber-500/20', icon: Clock };
  }
  // 未知动作沿用改造前行为：渲染原始标识符（不进字典，也绝不凭空发明中文）
  return { labelText: action, color: 'bg-slate-50 text-slate-600 dark:bg-slate-500/10 dark:text-slate-400', ring: 'ring-slate-500/20', icon: ShieldCheck };
};

const getEntityTypeStyle = (type: string): EntityTypeStyle | EntityTypeStyleFallback =>
  entityTypeStyles[type.toLowerCase()] ?? { labelText: type, color: 'bg-slate-50 text-slate-600 dark:bg-slate-500/10 dark:text-slate-400', icon: ShieldCheck };

const formatJson = (s: string): string => {
  if (!s) return '';
  try { return JSON.stringify(JSON.parse(s), null, 2); }
  catch { return s; }
};

/**
 * 设备档位（受控 code）。文案随界面语言变化，故走字典，由组件侧 `t()` 取词。
 * 浏览器名 `Edge/Opera/Chrome/Firefox/Safari` 是技术标识，保持字面量（非文案）。
 */
type DeviceKind = 'desktop' | 'mobile' | 'unknown';

/** 设备档位 → 文案键。键是闭合联合，查表必然命中，故不需要 `| undefined` */
const DEVICE_KIND_KEYS: Record<DeviceKind, TranslationKey> = {
  desktop: 'data.audit.device.desktop',
  mobile: 'data.audit.device.mobile',
  unknown: 'data.audit.device.unknown',
};

const parseUserAgent = (ua: string): { browser: string; device: DeviceKind } => {
  // 非文案：'-' 是既有的空值占位，不随界面语言变化
  if (!ua) return { browser: '-', device: 'unknown' };
  let browser = '-';
  if (/Edg\/[\d.]+/.test(ua)) browser = 'Edge';
  else if (/OPR\/[\d.]+|Opera/.test(ua)) browser = 'Opera';
  else if (/Chrome\/[\d.]+/.test(ua)) browser = 'Chrome';
  else if (/Firefox\/[\d.]+/.test(ua)) browser = 'Firefox';
  else if (/Safari\/[\d.]+/.test(ua)) browser = 'Safari';
  let device: DeviceKind = 'desktop';
  if (/Mobile|Android|iPhone|iPad|iPod/.test(ua)) device = 'mobile';
  return { browser, device };
};

/** 将本地日期 input 值（yyyy-MM-dd）转换为 UTC ISO 8601 起始时刻 */
const toUtcStartIso = (dateStr: string): string | undefined => {
  if (!dateStr) return undefined;
  const d = new Date(`${dateStr}T00:00:00`);
  if (Number.isNaN(d.getTime())) return undefined;
  return d.toISOString();
};

/** 将本地日期 input 值（yyyy-MM-dd）转换为 UTC ISO 8601 结束时刻 */
const toUtcEndIso = (dateStr: string): string | undefined => {
  if (!dateStr) return undefined;
  const d = new Date(`${dateStr}T23:59:59`);
  if (Number.isNaN(d.getTime())) return undefined;
  return d.toISOString();
};

export default function AuditLogs() {
  const { t } = useI18n();
  // 徽章取词：字典里的码走 t(labelKey)；未知码回落渲染原始标识符（labelText），与改造前一致
  const actionLabel = (s: ActionStyle | ActionStyleFallback) => ('labelKey' in s ? t(s.labelKey) : s.labelText);
  const entityLabel = (s: EntityTypeStyle | EntityTypeStyleFallback) => ('labelKey' in s ? t(s.labelKey) : s.labelText);
  const [list, setList] = useState<AuditLogResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [totalItems, setTotalItems] = useState(0);
  const [totalPages, setTotalPages] = useState(0);

  // 服务端过滤参数
  const [filterAction, setFilterAction] = useState('');
  const [filterEntityType, setFilterEntityType] = useState('');
  const [keyword, setKeyword] = useState('');           // 输入框即时值
  const [debouncedKeyword, setDebouncedKeyword] = useState('');  // 防抖后的值，传给后端
  const [filterUserId, setFilterUserId] = useState('');
  const [filterIp, setFilterIp] = useState('');
  const [filterStartDate, setFilterStartDate] = useState('');
  const [filterEndDate, setFilterEndDate] = useState('');

  const [page, setPage] = useState(1);
  const [pageSize] = useState(20);
  const [detailTarget, setDetailTarget] = useState<AuditLogResponse | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [refreshLock, setRefreshLock] = useState(false);
  const [lastRefreshAt, setLastRefreshAt] = useState<Date | null>(null);
  const isRefreshing = loading || refreshLock;

  // 全局统计（来自 /stats 端点，基于全量数据）
  const [stats, setStats] = useState<AuditLogStatsResponse | null>(null);
  const [statsLoading, setStatsLoading] = useState(false);

  // 导出 CSV 状态
  const [exporting, setExporting] = useState(false);

  // 完整性批量校验状态
  const [integrityReport, setIntegrityReport] = useState<AuditIntegrityReport | null>(null);
  const [integrityLoading, setIntegrityLoading] = useState(false);
  const [showIntegrityPanel, setShowIntegrityPanel] = useState(false);

  // 详情弹窗内的单条校验与关联日志
  const [singleVerify, setSingleVerify] = useState<AuditLogIntegrityResult | null>(null);
  const [singleVerifyLoading, setSingleVerifyLoading] = useState(false);
  const [related, setRelated] = useState<AuditLogRelatedResponse | null>(null);
  const [relatedLoading, setRelatedLoading] = useState(false);

  // 防抖 ref
  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const handleRefresh = useCallback(() => {
    if (isRefreshing) return;
    setRefreshLock(true);
    try {
      setRefreshKey((k) => k + 1);
    } finally {
      setTimeout(() => setRefreshLock(false), 400);
    }
  }, [isRefreshing]);

  // 关键字防抖：输入后 400ms 触发后端查询
  useEffect(() => {
    if (debounceTimerRef.current) clearTimeout(debounceTimerRef.current);
    debounceTimerRef.current = setTimeout(() => {
      setDebouncedKeyword(keyword);
      setPage(1);
    }, 400);
    return () => {
      if (debounceTimerRef.current) clearTimeout(debounceTimerRef.current);
    };
  }, [keyword]);

  // 当前过滤条件对象（供 list / stats / export 共用）
  const filterParams = useMemo(() => ({
    action: filterAction || undefined,
    entityType: filterEntityType || undefined,
    userId: filterUserId ? Number(filterUserId) || undefined : undefined,
    entityId: undefined,
    ipAddress: filterIp || undefined,
    keyword: debouncedKeyword || undefined,
    startDate: toUtcStartIso(filterStartDate),
    endDate: toUtcEndIso(filterEndDate),
  }), [filterAction, filterEntityType, filterUserId, filterIp, debouncedKeyword, filterStartDate, filterEndDate]);

  // 加载日志列表
  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError('');
    auditLogApi
      .list({ page, pageSize, ...filterParams })
      .then((resp: AuditLogListResponse) => {
        if (cancelled) return;
        const data = resp?.data ?? [];
        setList(data);
        setTotalItems(resp?.totalItems ?? 0);
        setTotalPages(resp?.totalPages ?? 0);
        setLastRefreshAt(new Date());
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setError(getApiErrorMessage(err, t('data.audit.loadFailed')));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, [page, pageSize, filterParams, refreshKey, t]);

  // 加载全局统计（与列表过滤条件联动，但独立请求避免阻塞列表）
  useEffect(() => {
    let cancelled = false;
    setStatsLoading(true);
    auditLogApi
      .stats(filterParams)
      .then((resp: AuditLogStatsResponse) => {
        if (cancelled) return;
        setStats(resp);
      })
      .catch(() => {
        // 统计失败不阻塞主流程，静默处理
        if (!cancelled) setStats(null);
      })
      .finally(() => {
        if (!cancelled) setStatsLoading(false);
      });
    return () => { cancelled = true; };
  }, [filterParams, refreshKey]);

  // 客户端二次过滤（仅在当前页内对 userName 等做补充过滤，主要匹配仍由后端完成）
  const filtered = useMemo(() => {
    if (!debouncedKeyword.trim()) return list;
    const q = debouncedKeyword.toLowerCase();
    return list.filter((l) =>
      (l.userName || '').toLowerCase().includes(q) ||
      (l.action || '').toLowerCase().includes(q) ||
      (l.entityType || '').toLowerCase().includes(q) ||
      (l.ipAddress || '').toLowerCase().includes(q) ||
      (l.userAgent || '').toLowerCase().includes(q) ||
      String(l.id).includes(q) ||
      (l.entityId && String(l.entityId).includes(q))
    );
  }, [list, debouncedKeyword]);

  // 操作/实体类型分布（来自全局统计，而非当前页）
  const actionDist = useMemo(() => {
    if (!stats?.actionDistribution) return [];
    return stats.actionDistribution.map((d) => [d.key, d.count] as [string, number]);
  }, [stats]);

  const entityDist = useMemo(() => {
    if (!stats?.entityDistribution) return [];
    return stats.entityDistribution.map((d) => [d.key, d.count] as [string, number]);
  }, [stats]);

  const handleResetFilters = () => {
    setFilterAction('');
    setFilterEntityType('');
    setKeyword('');
    setDebouncedKeyword('');
    setFilterUserId('');
    setFilterIp('');
    setFilterStartDate('');
    setFilterEndDate('');
    setPage(1);
  };

  // 导出 CSV
  const handleExport = useCallback(async () => {
    if (exporting) return;
    setExporting(true);
    try {
      const blob = await auditLogApi.exportCsv({ ...filterParams, maxRows: 5000 });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `audit-logs-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')}.csv`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch (err) {
      setError(getApiErrorMessage(err, t('data.common.exportFailed')));
    } finally {
      setExporting(false);
    }
  }, [exporting, filterParams, t]);

  // 批量校验完成后，将 isTampered 标记回写到当前列表，便于在表格中可视化
  const applyIntegrityToTable = useCallback((report: AuditIntegrityReport) => {
    setIntegrityReport(report);
    setShowIntegrityPanel(true);
    setList((prev) => prev.map((l) => {
      const tampered = report.tamperedLogIds.includes(l.id);
      // 仅当当前页处于校验范围时才能给出确定结论；否则置空（未知）
      const isInCheckedPage = report.page === page;
      // isTampered: true=被篡改 / false=未篡改 / null=未校验
      return { ...l, isTampered: isInCheckedPage ? tampered : null } as AuditLogResponse;
    }));
  }, [page]);

  // 批量完整性校验
  const handleVerifyIntegrity = useCallback(async () => {
    if (integrityLoading) return;
    setIntegrityLoading(true);
    try {
      const report = await auditLogApi.verifyIntegrity(page, 100);
      applyIntegrityToTable(report);
    } catch (err) {
      setError(getApiErrorMessage(err, t('data.audit.integrityFailed')));
    } finally {
      setIntegrityLoading(false);
    }
  }, [integrityLoading, page, applyIntegrityToTable, t]);

  // 详情弹窗：打开时加载关联日志 + 重置单条校验
  const handleOpenDetail = useCallback(async (log: AuditLogResponse) => {
    setDetailTarget(log);
    setSingleVerify(null);
    setRelated(null);
    setRelatedLoading(true);
    try {
      const rel = await auditLogApi.getRelated(log.id, 10);
      setRelated(rel);
    } catch {
      // 静默处理
    } finally {
      setRelatedLoading(false);
    }
  }, []);

  // 详情弹窗：单条完整性校验
  const handleVerifySingle = useCallback(async () => {
    if (!detailTarget || singleVerifyLoading) return;
    setSingleVerifyLoading(true);
    try {
      const result = await auditLogApi.verifySingle(detailTarget.id);
      setSingleVerify(result);
    } catch (err) {
      setError(getApiErrorMessage(err, t('data.audit.verifySingleFailed')));
    } finally {
      setSingleVerifyLoading(false);
    }
  }, [detailTarget, singleVerifyLoading, t]);

  const hasActiveFilters = useMemo(() =>
    !!(filterAction || filterEntityType || keyword || filterUserId || filterIp || filterStartDate || filterEndDate),
    [filterAction, filterEntityType, keyword, filterUserId, filterIp, filterStartDate, filterEndDate]);

  return (
    <div className="space-y-5">
      {/* ========== 页头 ========== */}
      <div className="relative overflow-hidden rounded-2xl border border-gray-200/60 dark:border-slate-700/50 bg-white dark:bg-slate-800/60 shadow-sm">
        <div className="absolute inset-0 bg-gradient-to-br from-primary-500/5 via-transparent to-indigo-500/5 dark:from-primary-500/10 dark:to-indigo-500/5 pointer-events-none" />
        <div className="absolute -top-20 -right-16 w-64 h-64 bg-primary-400/10 dark:bg-primary-500/10 rounded-full blur-3xl pointer-events-none" />
        <div className="absolute -bottom-16 -left-16 w-56 h-56 bg-indigo-400/10 dark:bg-indigo-500/10 rounded-full blur-3xl pointer-events-none" />
        <div className="relative p-4 sm:p-6 flex items-start justify-between gap-4 flex-wrap">
          <div className="flex items-start gap-3 sm:gap-4 min-w-0">
            <div className="relative w-10 h-10 sm:w-12 sm:h-12 shrink-0">
              <div className="absolute inset-0 bg-gradient-to-br from-primary-500 to-indigo-600 rounded-xl shadow-lg shadow-primary-500/25" />
              <div className="absolute inset-0 rounded-xl flex items-center justify-center text-white">
                <ShieldCheck className="w-5 h-5 sm:w-6 sm:h-6" strokeWidth={2.25} />
              </div>
            </div>
            <div className="min-w-0">
              <div className="flex items-center gap-2 flex-wrap">
                <h1 className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100 tracking-tight">{t('data.audit.title')}</h1>
                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-medium bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-400 ring-1 ring-inset ring-emerald-500/20">
                  <Activity className="w-3 h-3" /> {t('data.audit.hmacBadge')}
                </span>
              </div>
              <p className="text-xs sm:text-sm text-gray-500 dark:text-slate-400 mt-0.5">{t('data.audit.description')}</p>
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {lastRefreshAt && (
              <span className="text-xs text-gray-400 dark:text-slate-500 whitespace-nowrap">
                {t('data.audit.updatedAtPrefix')}{formatRelative(lastRefreshAt)}
              </span>
            )}
            <button
              onClick={handleVerifyIntegrity}
              disabled={integrityLoading}
              className="inline-flex items-center gap-1.5 px-3 py-2.5 text-sm font-medium bg-amber-50 dark:bg-amber-500/10 border border-amber-200 dark:border-amber-500/20 text-amber-700 dark:text-amber-400 rounded-xl hover:bg-amber-100 dark:hover:bg-amber-500/20 disabled:opacity-60 disabled:cursor-not-allowed active:scale-[0.98] transition-all duration-200"
              title={t('data.audit.verifyIntegrityTitle')}
            >
              {integrityLoading ? <Loader2 className="w-4 h-4 animate-spin" /> : <ShieldCheck className="w-4 h-4" />}
              <span className="hidden sm:inline">{t('data.audit.verifyIntegrityButton')}</span>
              <span className="sm:hidden">{t('data.audit.verifyShortButton')}</span>
            </button>
            <button
              onClick={handleRefresh}
              disabled={isRefreshing}
              className="inline-flex items-center gap-1.5 px-3 sm:px-4 py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-xl shadow-sm hover:bg-gray-50 dark:hover:bg-slate-700 hover:shadow disabled:opacity-60 disabled:cursor-not-allowed active:scale-[0.98] transition-all duration-200"
            >
              <RefreshCw className={`w-4 h-4 ${isRefreshing ? 'animate-spin' : ''}`} />
              <span className="hidden sm:inline">{t('data.common.refresh')}</span>
            </button>
          </div>
        </div>

        {/* KPI 统计卡片（来自全局 /stats，非当前页） */}
        <div className="relative grid grid-cols-2 md:grid-cols-4 gap-3 px-4 sm:px-6 pb-4 sm:pb-6">
          {[
            { label: t('data.audit.kpi.totalLogs'), value: stats?.totalLogs ?? '-', icon: Hash, color: 'from-sky-500/15 to-sky-500/0 text-sky-600 dark:text-sky-400', sub: statsLoading ? t('data.audit.kpi.calculating') : t('data.audit.kpi.matchingFilters') },
            { label: t('data.audit.kpi.activeUsers'), value: stats?.uniqueUsers ?? '-', icon: Users, color: 'from-violet-500/15 to-violet-500/0 text-violet-600 dark:text-violet-400', sub: t('data.audit.kpi.uniqueUsers') },
            { label: t('data.audit.kpi.last24h'), value: stats?.last24hCount ?? '-', icon: Activity, color: 'from-emerald-500/15 to-emerald-500/0 text-emerald-600 dark:text-emerald-400', sub: t('data.audit.kpi.newActions') },
            { label: t('data.audit.kpi.anomalies'), value: stats?.failedActions ?? '-', icon: AlertTriangle, color: (stats?.failedActions ?? 0) > 0 ? 'from-rose-500/15 to-rose-500/0 text-rose-600 dark:text-rose-400' : 'from-slate-500/15 to-slate-500/0 text-slate-600 dark:text-slate-400', sub: t('data.audit.kpi.failedOrLocked') },
          ].map((c, i) => (
            <div
              key={i}
              className={`relative overflow-hidden rounded-xl border border-gray-200/60 dark:border-slate-700/50 bg-gradient-to-br ${c.color} dark:bg-slate-900/40 p-3 sm:p-4 transition hover:shadow-md hover:-translate-y-0.5`}
            >
              <div className="flex items-start justify-between">
                <div className="min-w-0">
                  <div className="text-[10px] sm:text-xs text-gray-500 dark:text-slate-400 font-medium truncate">{c.label}</div>
                  <div className="mt-1 text-lg sm:text-2xl font-bold text-gray-900 dark:text-slate-100 tracking-tight tabular-nums">{c.value}</div>
                  <div className="mt-0.5 text-[10px] sm:text-[11px] text-gray-400 dark:text-slate-500 truncate">{c.sub}</div>
                </div>
                <div className="p-1.5 sm:p-2 rounded-lg bg-white/80 dark:bg-slate-800/80 ring-1 ring-black/5 dark:ring-white/10 flex-shrink-0">
                  <c.icon className="w-3.5 h-3.5 sm:w-4 sm:h-4" />
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* ========== 操作与类型分布摘要（来自全局统计） ========== */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <div className="lg:col-span-2 bg-white dark:bg-slate-800/60 rounded-xl border border-gray-200/60 dark:border-slate-700/50 shadow-sm p-4">
          <div className="flex items-center gap-2 mb-3">
            <BarChart3 className="w-4 h-4 text-gray-500 dark:text-slate-400" />
            <h3 className="text-sm font-semibold text-gray-800 dark:text-slate-200">{t('data.audit.chart.actionDistribution')}</h3>
          </div>
          {actionDist.length === 0 ? (
            <div className="text-sm text-gray-400 dark:text-slate-500 py-4 text-center">{t('common.empty')}</div>
          ) : (
            <div className="space-y-2">
              {actionDist.map(([key, count]) => {
                const s = getActionStyle(key);
                const max = actionDist[0][1] || 1;
                const pct = Math.max(4, Math.round((count / max) * 100));
                return (
                  <div key={key} className="group">
                    <div className="flex items-center justify-between text-xs mb-1">
                      <span className={`inline-flex items-center gap-1 px-1.5 py-0.5 rounded ${s.color} font-medium`}>
                        <s.icon className="w-3 h-3" />{actionLabel(s)}
                      </span>
                      <span className="text-gray-500 dark:text-slate-400 tabular-nums">{count}</span>
                    </div>
                    <div className="h-1.5 rounded-full bg-gray-100 dark:bg-slate-700/50 overflow-hidden">
                      <div
                        className="h-full rounded-full bg-gradient-to-r from-primary-500 to-indigo-500 transition-all duration-500 group-hover:from-primary-400 group-hover:to-indigo-400"
                        style={{ width: `${pct}%` }}
                      />
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>

        <div className="bg-white dark:bg-slate-800/60 rounded-xl border border-gray-200/60 dark:border-slate-700/50 shadow-sm p-4">
          <div className="flex items-center gap-2 mb-3">
            <Activity className="w-4 h-4 text-gray-500 dark:text-slate-400" />
            <h3 className="text-sm font-semibold text-gray-800 dark:text-slate-200">{t('data.audit.chart.entityDistribution')}</h3>
          </div>
          {entityDist.length === 0 ? (
            <div className="text-sm text-gray-400 dark:text-slate-500 py-4 text-center">{t('common.empty')}</div>
          ) : (
            <div className="space-y-2">
              {entityDist.map(([key, count]) => {
                const s = getEntityTypeStyle(key);
                const total = entityDist.reduce((s2, [, c]) => s2 + c, 0) || 1;
                const pct = Math.max(4, Math.round((count / total) * 100));
                return (
                  <div key={key}>
                    <div className="flex items-center justify-between text-xs mb-1">
                      <span className={`inline-flex items-center gap-1 px-1.5 py-0.5 rounded ${s.color} font-medium`}>
                        <s.icon className="w-3 h-3" />{entityLabel(s)}
                      </span>
                      <span className="text-gray-500 dark:text-slate-400 tabular-nums">{pct}%</span>
                    </div>
                    <div className="h-1.5 rounded-full bg-gray-100 dark:bg-slate-700/50 overflow-hidden">
                      <div
                        className={`h-full rounded-full ${s.color.replace('text-', 'bg-').split(' ').find((c2) => c2.startsWith('bg-'))} opacity-75`}
                        style={{ width: `${pct}%` }}
                      />
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* ========== 筛选条 ========== */}
      <div className="bg-white dark:bg-slate-800/60 rounded-xl border border-gray-200/60 dark:border-slate-700/50 shadow-sm p-4">
        <div className="grid grid-cols-2 md:flex md:flex-wrap items-end gap-3">
          <div className="col-span-2 md:flex-1 md:min-w-[220px] relative">
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1">{t('data.audit.filter.keyword')}</label>
            <div className="relative">
              <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400 dark:text-slate-500 pointer-events-none" />
              <input
                value={keyword}
                onChange={(e) => setKeyword(e.target.value)}
                placeholder={t('data.audit.filter.keywordPlaceholder')}
                className="w-full pl-9 pr-8 border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-gray-800 dark:text-slate-200 rounded-xl py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition placeholder:text-gray-400 dark:placeholder:text-slate-500"
              />
              {keyword && (
                <button
                  onClick={() => setKeyword('')}
                  className="absolute right-2 top-1/2 -translate-y-1/2 p-1 rounded-md hover:bg-gray-100 dark:hover:bg-slate-700 text-gray-400 dark:text-slate-500"
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              )}
            </div>
          </div>
          <div>
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1">{t('data.audit.field.actionType')}</label>
            <select
              value={filterAction}
              onChange={(e) => { setFilterAction(e.target.value); setPage(1); }}
              className="w-full md:min-w-[130px] border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-gray-800 dark:text-slate-200 rounded-xl px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition"
            >
              {actionOptions.map((o) => (
                <option key={o.value} value={o.value}>{t(o.labelKey)}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1">{t('data.audit.field.entityType')}</label>
            <select
              value={filterEntityType}
              onChange={(e) => { setFilterEntityType(e.target.value); setPage(1); }}
              className="w-full md:min-w-[130px] border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-gray-800 dark:text-slate-200 rounded-xl px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition"
            >
              {entityTypeOptions.map((o) => (
                <option key={o.value} value={o.value}>{t(o.labelKey)}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1">{t('data.audit.field.userId')}</label>
            <input
              type="number"
              value={filterUserId}
              onChange={(e) => { setFilterUserId(e.target.value); setPage(1); }}
              placeholder={t('data.audit.filter.userIdPlaceholder')}
              className="w-full md:w-24 border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-gray-800 dark:text-slate-200 rounded-xl px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition"
            />
          </div>
          <div className="col-span-2 md:col-span-1">
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1">{t('data.common.ipAddress')}</label>
            <input
              value={filterIp}
              onChange={(e) => { setFilterIp(e.target.value); setPage(1); }}
              placeholder={t('data.audit.filter.ipPlaceholder')}
              className="w-full md:w-40 border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-gray-800 dark:text-slate-200 rounded-xl px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition"
            />
          </div>
          <div>
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1">{t('data.common.startDate')}</label>
            <input
              type="date"
              value={filterStartDate}
              onChange={(e) => { setFilterStartDate(e.target.value); setPage(1); }}
              className="w-full border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-gray-800 dark:text-slate-200 rounded-xl px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition"
            />
          </div>
          <div>
            <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1">{t('data.common.endDate')}</label>
            <input
              type="date"
              value={filterEndDate}
              onChange={(e) => { setFilterEndDate(e.target.value); setPage(1); }}
              className="w-full border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-gray-800 dark:text-slate-200 rounded-xl px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition"
            />
          </div>
          <div className="col-span-2 md:col-span-1 flex gap-2">
            <button
              onClick={handleResetFilters}
              className="flex-1 md:flex-none inline-flex items-center justify-center gap-1.5 px-3 py-1.5 text-sm bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-200 rounded-xl shadow-sm hover:bg-gray-50 dark:hover:bg-slate-700 active:scale-[0.98] transition"
            >
              <RotateCcw className="w-4 h-4" />{t('common.reset')}
            </button>
            <button
              onClick={handleExport}
              disabled={exporting}
              className="flex-1 md:flex-none inline-flex items-center justify-center gap-1.5 px-3 py-1.5 text-sm bg-emerald-50 dark:bg-emerald-500/10 border border-emerald-200 dark:border-emerald-500/20 text-emerald-700 dark:text-emerald-400 rounded-xl shadow-sm hover:bg-emerald-100 dark:hover:bg-emerald-500/20 disabled:opacity-60 disabled:cursor-not-allowed active:scale-[0.98] transition"
              title={t('data.audit.exportTitle')}
            >
              {exporting ? <Loader2 className="w-4 h-4 animate-spin" /> : <Download className="w-4 h-4" />}
              <span className="md:hidden">{t('data.common.export')}</span>
              <span className="hidden md:inline">{t('data.common.exportCsv')}</span>
            </button>
          </div>
        </div>
        {hasActiveFilters && (
          <div className="mt-2 text-xs text-gray-500 dark:text-slate-400">
            {t('data.audit.filter.activeHint')}
          </div>
        )}
      </div>

      {/* 完整性校验结果面板 */}
      {showIntegrityPanel && integrityReport && (
        <div className="bg-white dark:bg-slate-800/60 rounded-xl border border-amber-200 dark:border-amber-500/30 shadow-sm p-4">
          <div className="flex items-start justify-between gap-3">
            <div className="flex items-start gap-3">
              <div className={`p-2 rounded-lg ${integrityReport.tamperedCount > 0 ? 'bg-rose-50 dark:bg-rose-500/10 text-rose-600 dark:text-rose-400' : 'bg-emerald-50 dark:bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'}`}>
                {integrityReport.tamperedCount > 0 ? <ShieldX className="w-5 h-5" /> : <ShieldCheck className="w-5 h-5" />}
              </div>
              <div>
                <h3 className="text-sm font-semibold text-gray-800 dark:text-slate-200">{t('data.audit.integrity.reportTitle')}</h3>
                <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
                  {t('data.audit.integrity.summary', { checked: integrityReport.checkedCount, page: integrityReport.page, total: integrityReport.totalLogs, tampered: integrityReport.tamperedCount })}
                </p>
                {integrityReport.tamperedCount > 0 && (
                  <div className="mt-2 flex flex-wrap gap-1">
                    {integrityReport.tamperedLogIds.slice(0, 20).map((id) => (
                      <button
                        key={id}
                        onClick={() => {
                          const log = list.find((l) => l.id === id);
                          if (log) handleOpenDetail(log);
                        }}
                        className="px-2 py-0.5 rounded text-xs font-mono bg-rose-50 dark:bg-rose-500/10 text-rose-700 dark:text-rose-400 border border-rose-200 dark:border-rose-500/20 hover:bg-rose-100 dark:hover:bg-rose-500/20 transition"
                      >
                        #{id}
                      </button>
                    ))}
                    {integrityReport.tamperedLogIds.length > 20 && (
                      <span className="text-xs text-gray-400 dark:text-slate-500">+{integrityReport.tamperedLogIds.length - 20}{t('data.audit.integrity.moreSuffix')}</span>
                    )}
                  </div>
                )}
              </div>
            </div>
            <button
              onClick={() => setShowIntegrityPanel(false)}
              className="p-1 rounded-md hover:bg-gray-100 dark:hover:bg-slate-700 text-gray-400 dark:text-slate-500"
            >
              <X className="w-4 h-4" />
            </button>
          </div>
        </div>
      )}

      {error && (
        <div className="bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/20 text-rose-700 dark:text-rose-400 px-4 py-3 rounded-xl text-sm flex items-start gap-2">
          <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" />
          <div>{t('data.common.loadFailedPrefix')}{error}</div>
        </div>
      )}

      {/* ========== 表格 ========== */}
      <div className="bg-white dark:bg-slate-800/60 rounded-xl border border-gray-200/60 dark:border-slate-700/50 shadow-sm overflow-hidden">
        {loading ? (
          <div className="p-10 text-center text-gray-500 dark:text-slate-400">
            <RefreshCw className="w-8 h-8 mx-auto mb-2 animate-spin text-primary-400" />
            <div>{t('common.shell.loading')}</div>
          </div>
        ) : filtered.length === 0 ? (
          <div className="p-14 text-center text-gray-500 dark:text-slate-400">
            <div className="mx-auto w-16 h-16 rounded-2xl bg-gray-50 dark:bg-slate-700/50 flex items-center justify-center mb-3">
              <ShieldCheck className="w-8 h-8 text-gray-300 dark:text-slate-600" />
            </div>
            <div className="font-medium text-gray-700 dark:text-slate-300">{t('data.audit.empty.title')}</div>
            <div className="text-xs text-gray-400 dark:text-slate-500 mt-1">{t('data.audit.empty.hint')}</div>
          </div>
        ) : (
          <>
            {/* PC 端表格视图 */}
            <div className="hidden md:block overflow-x-auto">
              <table className="min-w-full divide-y divide-gray-100 dark:divide-slate-700/50">
                <thead className="bg-gray-50/70 dark:bg-slate-900/60 backdrop-blur-sm">
                  <tr>
                    <th className="px-4 py-2.5 text-left text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">ID</th>
                    <th className="px-4 py-2.5 text-left text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('data.audit.field.time')}</th>
                    <th className="px-4 py-2.5 text-left text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('data.audit.field.user')}</th>
                    <th className="px-4 py-2.5 text-left text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('data.audit.field.action')}</th>
                    <th className="px-4 py-2.5 text-left text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('data.audit.field.entity')}</th>
                    <th className="px-4 py-2.5 text-left text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('data.audit.field.ipClient')}</th>
                    <th className="px-4 py-2.5 text-left text-[11px] font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">{t('data.audit.field.details')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100 dark:divide-slate-700/50">
                  {filtered.map((log) => {
                    const actionStyle = getActionStyle(log.action);
                    const entityTypeStyle = getEntityTypeStyle(log.entityType);
                    const ActionIcon = actionStyle.icon;
                    const EntityIcon = entityTypeStyle.icon;
                    const ua = parseUserAgent(log.userAgent);
                    const tampered = log.isTampered === true;
                    const verified = log.isTampered === true || log.isTampered === false;
                    return (
                      <tr key={log.id} className={`group hover:bg-gray-50/70 dark:hover:bg-slate-700/30 transition ${tampered ? 'bg-rose-50/40 dark:bg-rose-500/5' : ''}`}>
                        <td className="px-4 py-3 text-xs text-gray-500 dark:text-slate-400 font-mono tabular-nums">
                          <div className="flex items-center gap-1">
                            <span className="text-gray-400 dark:text-slate-500">#</span>{log.id}
                            {tampered && (
                              <span title={t('data.audit.signature.invalid')} className="inline-flex items-center">
                                <ShieldX className="w-3 h-3 text-rose-500 dark:text-rose-400" />
                              </span>
                            )}
                            {!tampered && verified && (
                              <span title={t('data.audit.signature.valid')} className="inline-flex items-center">
                                <ShieldCheck className="w-3 h-3 text-emerald-500 dark:text-emerald-400" />
                              </span>
                            )}
                          </div>
                        </td>
                        <td className="px-4 py-3 whitespace-nowrap">
                          <div className="text-sm text-gray-800 dark:text-slate-200 font-medium">{formatDateTime(log.createdAt)}</div>
                          <div className="text-[11px] text-gray-400 dark:text-slate-500">{formatRelative(new Date(log.createdAt))}</div>
                        </td>
                        <td className="px-4 py-3">
                          {log.userId ? (
                            <div className="flex items-center gap-2 min-w-0">
                              <UserAvatar userId={log.userId} name={log.userName} size="xs" />
                              <div className="min-w-0">
                                <div className="text-sm text-gray-800 dark:text-slate-200 font-medium truncate max-w-[140px]">
                                  {log.userName || t('data.audit.userRef', { id: log.userId })}
                                </div>
                                <div className="text-[11px] text-gray-400 dark:text-slate-500 font-mono tabular-nums">
                                  ID {log.userId}
                                </div>
                              </div>
                            </div>
                          ) : (
                            <span className="inline-flex items-center gap-1.5 px-2 py-1 rounded-lg bg-slate-50 dark:bg-slate-700/50 text-xs text-gray-500 dark:text-slate-400">
                              <Shield className="w-3 h-3" />{t('data.common.system')}
                            </span>
                          )}
                        </td>
                        <td className="px-4 py-3">
                          <span className={`inline-flex items-center gap-1 px-2 py-1 text-xs font-semibold rounded-lg ring-1 ring-inset ${actionStyle.color} ring-black/5 dark:ring-white/10`}>
                            <ActionIcon className="w-3.5 h-3.5" />
                            {actionLabel(actionStyle)}
                          </span>
                        </td>
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-2 flex-wrap">
                            <span className={`inline-flex items-center gap-1 px-2 py-1 text-xs font-semibold rounded-lg ring-1 ring-inset ${entityTypeStyle.color} ring-black/5 dark:ring-white/10`}>
                              <EntityIcon className="w-3.5 h-3.5" />
                              {entityLabel(entityTypeStyle)}
                            </span>
                            {log.entityId && (
                              <span className="text-[11px] text-gray-500 dark:text-slate-400 font-mono tabular-nums">
                                #{log.entityId}
                              </span>
                            )}
                          </div>
                        </td>
                        <td className="px-4 py-3">
                          <div className="text-xs">
                            <div className="font-mono text-gray-600 dark:text-slate-300 tabular-nums">{log.ipAddress || '-'}</div>
                            <div className="mt-1 flex items-center gap-1.5 text-gray-400 dark:text-slate-500">
                              <Monitor className="w-3 h-3" />
                              <span>{ua.browser}</span>
                              <span className="opacity-50">·</span>
                              <span>{t(DEVICE_KIND_KEYS[ua.device])}</span>
                            </div>
                          </div>
                        </td>
                        <td className="px-4 py-3">
                          {(log.oldValues || log.newValues || log.userAgent) ? (
                            <button
                              onClick={() => handleOpenDetail(log)}
                              className="inline-flex items-center gap-1 text-xs font-medium text-primary-600 hover:text-primary-700 dark:text-primary-400 dark:hover:text-primary-300 hover:underline underline-offset-2 transition"
                            >
                              <Eye className="w-3.5 h-3.5" />
                              {t('data.common.viewDetails')}
                            </button>
                          ) : (
                            <span className="text-xs text-gray-300 dark:text-slate-600">—</span>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            {/* 移动端卡片视图 */}
            <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
              {filtered.map((log) => {
                const actionStyle = getActionStyle(log.action);
                const entityTypeStyle = getEntityTypeStyle(log.entityType);
                const ActionIcon = actionStyle.icon;
                const EntityIcon = entityTypeStyle.icon;
                const ua = parseUserAgent(log.userAgent);
                const tampered = log.isTampered === true;
                const verified = log.isTampered === true || log.isTampered === false;
                return (
                  <div
                    key={log.id}
                    className={`p-3.5 hover:bg-gray-50/70 dark:hover:bg-slate-700/30 transition ${tampered ? 'bg-rose-50/40 dark:bg-rose-500/5' : ''}`}
                  >
                    {/* 卡片头部：ID + 签名状态 + 时间 */}
                    <div className="flex items-center justify-between gap-2 mb-2.5">
                      <div className="flex items-center gap-1.5 min-w-0">
                        <span className="text-xs text-gray-400 dark:text-slate-500 font-mono">#{log.id}</span>
                        {tampered && (
                          <span title={t('data.audit.signature.invalid')} className="inline-flex items-center">
                            <ShieldX className="w-3.5 h-3.5 text-rose-500 dark:text-rose-400" />
                          </span>
                        )}
                        {!tampered && verified && (
                          <span title={t('data.audit.signature.valid')} className="inline-flex items-center">
                            <ShieldCheck className="w-3.5 h-3.5 text-emerald-500 dark:text-emerald-400" />
                          </span>
                        )}
                      </div>
                      <div className="text-right flex-shrink-0">
                        <div className="text-xs text-gray-700 dark:text-slate-200 font-medium">{formatDateTime(log.createdAt)}</div>
                        <div className="text-[10px] text-gray-400 dark:text-slate-500">{formatRelative(new Date(log.createdAt))}</div>
                      </div>
                    </div>

                    {/* 用户 + 操作 + 实体 标签 */}
                    <div className="flex flex-wrap items-center gap-1.5 mb-2.5">
                      {log.userId ? (
                        <div className="flex items-center gap-1.5 mr-1">
                          <UserAvatar userId={log.userId} name={log.userName} size="xs" />
                          <span className="text-xs text-gray-700 dark:text-slate-200 font-medium truncate max-w-[100px]">
                            {log.userName || t('data.audit.userRef', { id: log.userId })}
                          </span>
                        </div>
                      ) : (
                        <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-slate-50 dark:bg-slate-700/50 text-[10px] text-gray-500 dark:text-slate-400">
                          <Shield className="w-2.5 h-2.5" />{t('data.common.system')}
                        </span>
                      )}
                      <span className={`inline-flex items-center gap-1 px-1.5 py-0.5 text-[10px] font-semibold rounded ring-1 ring-inset ${actionStyle.color} ring-black/5 dark:ring-white/10`}>
                        <ActionIcon className="w-3 h-3" />
                        {actionLabel(actionStyle)}
                      </span>
                      <span className={`inline-flex items-center gap-1 px-1.5 py-0.5 text-[10px] font-semibold rounded ring-1 ring-inset ${entityTypeStyle.color} ring-black/5 dark:ring-white/10`}>
                        <EntityIcon className="w-3 h-3" />
                        {entityLabel(entityTypeStyle)}
                      </span>
                      {log.entityId && (
                        <span className="text-[10px] text-gray-400 dark:text-slate-500 font-mono">#{log.entityId}</span>
                      )}
                    </div>

                    {/* IP 与客户端信息 */}
                    <div className="flex items-center justify-between gap-2 text-[11px] text-gray-500 dark:text-slate-400 mb-2.5">
                      <div className="flex items-center gap-1.5 min-w-0">
                        <span className="font-mono text-gray-600 dark:text-slate-300 truncate">{log.ipAddress || '-'}</span>
                      </div>
                      <div className="flex items-center gap-1 flex-shrink-0">
                        <Monitor className="w-3 h-3" />
                        <span>{ua.browser}</span>
                        <span className="opacity-50">·</span>
                        <span>{t(DEVICE_KIND_KEYS[ua.device])}</span>
                      </div>
                    </div>

                    {/* 详情按钮 */}
                    {(log.oldValues || log.newValues || log.userAgent) ? (
                      <button
                        onClick={() => handleOpenDetail(log)}
                        className="w-full inline-flex items-center justify-center gap-1 text-xs font-medium text-primary-600 dark:text-primary-400 bg-primary-50 dark:bg-primary-500/10 hover:bg-primary-100 dark:hover:bg-primary-500/20 py-1.5 rounded-lg transition"
                      >
                        <Eye className="w-3.5 h-3.5" />
                        {t('data.common.viewDetails')}
                      </button>
                    ) : (
                      <div className="text-center text-[11px] text-gray-300 dark:text-slate-600 py-1">—</div>
                    )}
                  </div>
                );
              })}
            </div>
          </>
        )}

        {/* 分页 */}
        {!loading && filtered.length > 0 && (
          <div className="px-4 py-3 border-t border-gray-100 dark:border-slate-700/50 flex flex-wrap items-center justify-between gap-2 text-sm">
            <div className="text-gray-500 dark:text-slate-400 flex items-center gap-3 flex-wrap">
              <span>
                {t('data.audit.pagination.pagePrefix')}<span className="font-semibold text-gray-700 dark:text-slate-300 tabular-nums">{page}</span>
                {totalPages > 0 ? <>{t('data.audit.pagination.pageTotal', { total: totalPages })}</> : t('data.audit.pagination.pageSuffix')}
              </span>
              <span className="hidden sm:inline text-gray-300 dark:text-slate-600">|</span>
              {totalItems > 0 && (
                <span>{t('data.audit.pagination.rangePrefix')}<span className="font-medium tabular-nums">{filtered.length}</span>{t('data.audit.pagination.rangeMiddle')}<span className="font-semibold tabular-nums text-gray-700 dark:text-slate-300">{totalItems}</span>{t('data.audit.pagination.rangeSuffix')}</span>
              )}
            </div>
            <div className="flex items-center gap-1.5">
              <button
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                disabled={page === 1}
                className="inline-flex items-center gap-1 px-2.5 py-1.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-200 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 active:scale-[0.97] disabled:opacity-40 disabled:cursor-not-allowed transition"
              >
                <ChevronLeft className="w-4 h-4" />
                <span className="hidden sm:inline">{t('data.common.previousPage')}</span>
              </button>
              <button
                onClick={() => setPage((p) => p + 1)}
                disabled={totalPages > 0 ? page >= totalPages : list.length < pageSize}
                className="inline-flex items-center gap-1 px-2.5 py-1.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-200 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 active:scale-[0.97] disabled:opacity-40 disabled:cursor-not-allowed transition"
              >
                <span className="hidden sm:inline">{t('data.common.nextPage')}</span>
                <ChevronRight className="w-4 h-4" />
              </button>
            </div>
          </div>
        )}
      </div>

      {/* ========== 详情弹窗 ========== */}
      <Modal
        open={!!detailTarget}
        title={t('data.audit.detail.title', { id: detailTarget?.id ?? '' })}
        onClose={() => setDetailTarget(null)}
        size="xl"
        footer={
          <div className="flex items-center gap-2 justify-end">
            {detailTarget && (
              <button
                onClick={handleVerifySingle}
                disabled={singleVerifyLoading}
                className="inline-flex items-center gap-1.5 px-3 py-2 text-sm bg-amber-50 dark:bg-amber-500/10 border border-amber-200 dark:border-amber-500/20 text-amber-700 dark:text-amber-400 rounded-xl hover:bg-amber-100 dark:hover:bg-amber-500/20 disabled:opacity-60 disabled:cursor-not-allowed active:scale-[0.98] transition"
              >
                {singleVerifyLoading ? <Loader2 className="w-4 h-4 animate-spin" /> : <ShieldCheck className="w-4 h-4" />}
                {t('data.audit.detail.verifySignature')}
              </button>
            )}
            {detailTarget && (detailTarget.oldValues || detailTarget.newValues) && (
              <button
                onClick={() => {
                  const blob = new Blob(
                    [JSON.stringify({ old: detailTarget.oldValues, new: detailTarget.newValues }, null, 2)],
                    { type: 'application/json' }
                  );
                  const url = URL.createObjectURL(blob);
                  const a = document.createElement('a');
                  a.href = url; a.download = `auditlog-${detailTarget.id}.json`;
                  a.click(); URL.revokeObjectURL(url);
                }}
                className="inline-flex items-center gap-1.5 px-3 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-700 dark:text-slate-200 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 active:scale-[0.98] transition"
              >
                <Download className="w-4 h-4" />
                {t('data.audit.detail.exportJson')}
              </button>
            )}
            <button
              onClick={() => setDetailTarget(null)}
              className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium bg-gradient-to-b from-primary-500 to-primary-600 hover:from-primary-400 hover:to-primary-500 text-white rounded-xl shadow-sm shadow-primary-500/20 active:scale-[0.98] transition"
            >
              {t('common.close')}
            </button>
          </div>
        }
      >
        {detailTarget && (
          <div className="space-y-5">
            {/* 单条完整性校验结果 */}
            {singleVerify && (
              <div className={`p-3 rounded-xl border ${singleVerify.isValid ? 'bg-emerald-50 dark:bg-emerald-500/10 border-emerald-200 dark:border-emerald-500/20' : 'bg-rose-50 dark:bg-rose-500/10 border-rose-200 dark:border-rose-500/20'}`}>
                <div className="flex items-start gap-2">
                  {singleVerify.isValid ? (
                    <ShieldCheck className="w-5 h-5 text-emerald-600 dark:text-emerald-400 shrink-0 mt-0.5" />
                  ) : (
                    <ShieldX className="w-5 h-5 text-rose-600 dark:text-rose-400 shrink-0 mt-0.5" />
                  )}
                  <div className="min-w-0">
                    <div className={`text-sm font-semibold ${singleVerify.isValid ? 'text-emerald-800 dark:text-emerald-300' : 'text-rose-800 dark:text-rose-300'}`}>
                      {singleVerify.isValid ? t('data.audit.signature.validLong') : t('data.audit.signature.invalidLong')}
                    </div>
                    <div className="text-xs text-gray-500 dark:text-slate-400 mt-1">
                      {t('data.audit.detail.verifiedAtPrefix')}{formatDateTime(singleVerify.verifiedAt)}
                    </div>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 mt-2">
                      <div>
                        <div className="text-[10px] text-gray-400 dark:text-slate-500 mb-0.5">{t('data.audit.detail.storedSignature')}</div>
                        <code className="text-[11px] font-mono break-all text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-900 px-2 py-1 rounded border border-gray-200/60 dark:border-slate-700/50 block">
                          {singleVerify.storedSignature || t('data.audit.emptyValue')}
                        </code>
                      </div>
                      <div>
                        <div className="text-[10px] text-gray-400 dark:text-slate-500 mb-0.5">{t('data.audit.detail.expectedSignature')}</div>
                        <code className="text-[11px] font-mono break-all text-gray-700 dark:text-slate-300 bg-white dark:bg-slate-900 px-2 py-1 rounded border border-gray-200/60 dark:border-slate-700/50 block">
                          {singleVerify.expectedSignature}
                        </code>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            )}

            {/* 基本信息卡片组 */}
            <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
              <div className="p-4 rounded-xl bg-gradient-to-br from-gray-50 to-slate-50 dark:from-slate-900/70 dark:to-slate-800 border border-gray-200/50 dark:border-slate-700/50">
                <div className="text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wider mb-1.5 flex items-center gap-1">
                  <Clock className="w-3 h-3" />{t('data.audit.field.time')}
                </div>
                <div className="text-base font-semibold text-gray-900 dark:text-slate-100">{formatDateTime(detailTarget.createdAt)}</div>
                <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{formatRelative(new Date(detailTarget.createdAt))}</div>
              </div>
              <div className="p-4 rounded-xl bg-gradient-to-br from-gray-50 to-slate-50 dark:from-slate-900/70 dark:to-slate-800 border border-gray-200/50 dark:border-slate-700/50">
                <div className="text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wider mb-1.5 flex items-center gap-1">
                  <User className="w-3 h-3" />{t('data.audit.field.operator')}
                </div>
                {detailTarget.userId ? (
                  <div className="flex items-center gap-2">
                    <UserAvatar userId={detailTarget.userId} name={detailTarget.userName} size="sm" />
                    <div className="min-w-0">
                      <div className="text-base font-semibold text-gray-900 dark:text-slate-100 truncate">
                        {detailTarget.userName || t('data.audit.userRef', { id: detailTarget.userId })}
                      </div>
                      <div className="text-xs text-gray-500 dark:text-slate-400 font-mono">ID: {detailTarget.userId}</div>
                    </div>
                  </div>
                ) : (
                  <div className="flex items-center gap-2">
                    <div className="w-8 h-8 rounded-lg bg-slate-100 dark:bg-slate-700/60 flex items-center justify-center text-slate-500">
                      <Shield className="w-4 h-4" />
                    </div>
                    <div>
                      <div className="text-base font-semibold text-gray-900 dark:text-slate-100">{t('data.common.system')}</div>
                      <div className="text-xs text-gray-500 dark:text-slate-400">{t('data.audit.field.automatedHint')}</div>
                    </div>
                  </div>
                )}
              </div>
              <div className="p-4 rounded-xl bg-gradient-to-br from-gray-50 to-slate-50 dark:from-slate-900/70 dark:to-slate-800 border border-gray-200/50 dark:border-slate-700/50">
                <div className="text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wider mb-1.5">{t('data.audit.field.actionType')}</div>
                {(() => {
                  const s = getActionStyle(detailTarget.action);
                  const Icon = s.icon;
                  return (
                    <span className={`inline-flex items-center gap-1.5 px-2.5 py-1.5 text-sm font-semibold rounded-xl ring-1 ring-inset ${s.color} ring-black/5 dark:ring-white/10`}>
                      <Icon className="w-4 h-4" />{actionLabel(s)}
                    </span>
                  );
                })()}
              </div>
              <div className="p-4 rounded-xl bg-gradient-to-br from-gray-50 to-slate-50 dark:from-slate-900/70 dark:to-slate-800 border border-gray-200/50 dark:border-slate-700/50">
                <div className="text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wider mb-1.5">{t('data.audit.field.targetEntity')}</div>
                <div className="flex items-center gap-2 flex-wrap">
                  {(() => {
                    const s = getEntityTypeStyle(detailTarget.entityType);
                    const Icon = s.icon;
                    return (
                      <span className={`inline-flex items-center gap-1.5 px-2.5 py-1.5 text-sm font-semibold rounded-xl ring-1 ring-inset ${s.color} ring-black/5 dark:ring-white/10`}>
                        <Icon className="w-4 h-4" />{entityLabel(s)}
                      </span>
                    );
                  })()}
                  {detailTarget.entityId && (
                    <span className="text-xs text-gray-600 dark:text-slate-400 font-mono bg-white dark:bg-slate-800 px-2 py-1 rounded-md border border-gray-200/60 dark:border-slate-700/50">
                      {t('data.audit.field.entityIdPrefix')}{detailTarget.entityId}
                    </span>
                  )}
                </div>
              </div>
            </div>

            {/* 网络上下文 */}
            <div className="p-4 rounded-xl bg-gradient-to-br from-gray-50 to-slate-50 dark:from-slate-900/70 dark:to-slate-800 border border-gray-200/50 dark:border-slate-700/50">
              <div className="text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wider mb-3 flex items-center gap-1">
                <Globe className="w-3 h-3" />{t('data.audit.field.networkContext')}
              </div>
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 text-sm">
                <div>
                  <div className="text-[11px] text-gray-500 dark:text-slate-400 mb-1">{t('data.common.ipAddress')}</div>
                  <div className="font-mono font-medium text-gray-900 dark:text-slate-100 break-all">{detailTarget.ipAddress || '-'}</div>
                </div>
                <div>
                  <div className="text-[11px] text-gray-500 dark:text-slate-400 mb-1">{t('data.audit.field.browser')}</div>
                  <div className="font-medium text-gray-900 dark:text-slate-100 flex items-center gap-1.5">
                    <Monitor className="w-3.5 h-3.5 text-gray-400 dark:text-slate-500" />
                    {parseUserAgent(detailTarget.userAgent).browser}
                    <span className="text-gray-300 dark:text-slate-600">·</span>
                    {t(DEVICE_KIND_KEYS[parseUserAgent(detailTarget.userAgent).device])}
                  </div>
                </div>
                <div className="sm:col-span-3">
                  <div className="text-[11px] text-gray-500 dark:text-slate-400 mb-1">{t('data.audit.field.fullUserAgent')}</div>
                  <div className="font-mono text-[11px] text-gray-600 dark:text-slate-400 bg-white dark:bg-slate-900 px-3 py-2 rounded-lg border border-gray-200/60 dark:border-slate-700/50 break-all">
                    {detailTarget.userAgent || t('data.audit.emptyValue')}
                  </div>
                </div>
              </div>
            </div>

            {/* 关联日志 */}
            <div className="p-4 rounded-xl bg-gradient-to-br from-gray-50 to-slate-50 dark:from-slate-900/70 dark:to-slate-800 border border-gray-200/50 dark:border-slate-700/50">
              <div className="text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wider mb-3 flex items-center gap-1">
                <Link2 className="w-3 h-3" />{t('data.audit.field.relatedLogs')}
              </div>
              {relatedLoading ? (
                <div className="text-sm text-gray-400 dark:text-slate-500 flex items-center gap-2">
                  <Loader2 className="w-3 h-3 animate-spin" />{t('common.shell.loading')}
                </div>
              ) : related && related.items.length > 0 ? (
                <div className="space-y-1.5">
                  <div className="text-xs text-gray-500 dark:text-slate-400 mb-2">
                    {t('data.audit.field.sourcePrefix')}{related.source === 'entity' ? t('data.audit.related.sameEntity') : related.source === 'user' ? t('data.audit.related.sameUser') : t('data.audit.related.none')}
                    {t('data.audit.related.countMiddle')}{related.items.length}{t('data.audit.related.countSuffix')}
                  </div>
                  {related.items.map((r) => {
                    const s = getActionStyle(r.action);
                    const RIcon = s.icon;
                    return (
                      <button
                        key={r.id}
                        onClick={() => handleOpenDetail(r)}
                        className="w-full text-left flex items-center gap-2 p-2 rounded-lg hover:bg-white dark:hover:bg-slate-800 border border-transparent hover:border-gray-200 dark:hover:border-slate-700 transition"
                      >
                        <span className="text-xs font-mono text-gray-400 dark:text-slate-500 tabular-nums">#{r.id}</span>
                        <span className={`inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[10px] font-semibold ${s.color}`}>
                          <RIcon className="w-3 h-3" />{actionLabel(s)}
                        </span>
                        <span className="text-xs text-gray-500 dark:text-slate-400 truncate flex-1">
                          {formatDateTime(r.createdAt)}
                        </span>
                      </button>
                    );
                  })}
                </div>
              ) : (
                <div className="text-sm text-gray-400 dark:text-slate-500">{t('data.audit.related.empty')}</div>
              )}
            </div>

            {/* 变更对比 */}
            {(detailTarget.oldValues || detailTarget.newValues) && (
              <div>
                <div className="text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wider mb-3 flex items-center gap-1">
                  <BarChart3 className="w-3 h-3" />{t('data.audit.field.changeComparison')}
                </div>
                <div className="grid grid-cols-1 lg:grid-cols-2 gap-3">
                  <div>
                    <div className="flex items-center gap-2 mb-2">
                      <span className="w-1 h-5 rounded-full bg-rose-500" />
                      <h4 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('data.audit.field.oldValues')}</h4>
                      {detailTarget.oldValues && (
                        <span className="text-[10px] text-gray-400 dark:text-slate-500 ml-auto font-mono">{detailTarget.oldValues.length} chars</span>
                      )}
                    </div>
                    <pre className="bg-rose-50 dark:bg-rose-500/10 border border-rose-100 dark:border-rose-500/20 rounded-xl p-3.5 text-xs text-rose-800 dark:text-rose-300 overflow-x-auto max-h-80 font-mono leading-relaxed">
                      {formatJson(detailTarget.oldValues) || t('data.audit.emptyValue')}
                    </pre>
                  </div>
                  <div>
                    <div className="flex items-center gap-2 mb-2">
                      <span className="w-1 h-5 rounded-full bg-emerald-500" />
                      <h4 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('data.audit.field.newValues')}</h4>
                      {detailTarget.newValues && (
                        <span className="text-[10px] text-gray-400 dark:text-slate-500 ml-auto font-mono">{detailTarget.newValues.length} chars</span>
                      )}
                    </div>
                    <pre className="bg-emerald-50 dark:bg-emerald-500/10 border border-emerald-100 dark:border-emerald-500/20 rounded-xl p-3.5 text-xs text-emerald-800 dark:text-emerald-300 overflow-x-auto max-h-80 font-mono leading-relaxed">
                      {formatJson(detailTarget.newValues) || t('data.audit.emptyValue')}
                    </pre>
                  </div>
                </div>
              </div>
            )}
          </div>
        )}
      </Modal>
    </div>
  );
}
