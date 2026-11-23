import type { ComponentType } from 'react';
import { lazyWithRetry } from '../lib/lazyRetry';

// ===== 关键修复：所有路由懒加载统一包装 lazyWithRetry =====
// 解决：SPA 滚动部署时，旧 index.html 引用的旧 hash chunk 404 → Failed to fetch dynamically imported module
//       → Promise reject → ErrorBoundary「页面发生错误」。
// lazyWithRetry 会做最多 3 次指数退避重试 + 耗尽时在 window 上标记版本不匹配，
// ErrorBoundary 识别后直接自动刷新页面（不用用户手动点按钮）。
const Dashboard = lazyWithRetry(() => import('../pages/Dashboard'));
const Conversations = lazyWithRetry(() => import('../pages/Conversations'));
const ConversationDetail = lazyWithRetry(() => import('../pages/ConversationDetail'));
const Users = lazyWithRetry(() => import('../pages/Users'));
const Mailboxes = lazyWithRetry(() => import('../pages/Mailboxes'));
const Customers = lazyWithRetry(() => import('../pages/Customers'));
const Tags = lazyWithRetry(() => import('../pages/Tags'));
const CannedResponses = lazyWithRetry(() => import('../pages/CannedResponses'));
const Notifications = lazyWithRetry(() => import('../pages/Notifications'));
const AuditLogs = lazyWithRetry(() => import('../pages/AuditLogs'));
const Settings = lazyWithRetry(() => import('../pages/Settings'));
const Reports = lazyWithRetry(() => import('../pages/Reports'));
const SendLogs = lazyWithRetry(() => import('../pages/SendLogs'));
const Satisfaction = lazyWithRetry(() => import('../pages/Satisfaction'));
const Workflows = lazyWithRetry(() => import('../pages/Workflows'));
const Sla = lazyWithRetry(() => import('../pages/Sla'));
const Macros = lazyWithRetry(() => import('../pages/Macros'));
const CustomFields = lazyWithRetry(() => import('../pages/CustomFields'));
const Webhooks = lazyWithRetry(() => import('../pages/Webhooks'));
const Roles = lazyWithRetry(() => import('../pages/Roles'));
const MailTemplates = lazyWithRetry(() => import('../pages/MailTemplates'));
const Teams = lazyWithRetry(() => import('../pages/Teams'));
const EmailSignatures = lazyWithRetry(() => import('../pages/EmailSignatures'));
const Modules = lazyWithRetry(() => import('../pages/Modules'));
const NotificationTemplates = lazyWithRetry(() => import('../pages/NotificationTemplates'));
const NotificationRules = lazyWithRetry(() => import('../pages/NotificationRules'));
const ApiKeys = lazyWithRetry(() => import('../pages/ApiKeys'));
const Profile = lazyWithRetry(() => import('../pages/Profile'));
const ImportExport = lazyWithRetry(() => import('../pages/ImportExport'));
const SystemTools = lazyWithRetry(() => import('../pages/SystemTools'));
const Localization = lazyWithRetry(() => import('../pages/Localization'));
const Calendar = lazyWithRetry(() => import('../pages/Calendar'));
const Knowledge = lazyWithRetry(() => import('../pages/Knowledge'));
const Chat = lazyWithRetry(() => import('../pages/Chat'));
const WeChat = lazyWithRetry(() => import('../pages/WeChat'));
const WhatsApp = lazyWithRetry(() => import('../pages/WhatsApp'));
const Feishu = lazyWithRetry(() => import('../pages/Feishu'));
const DingDing = lazyWithRetry(() => import('../pages/DingDing'));
const Messenger = lazyWithRetry(() => import('../pages/Messenger'));
const Telegram = lazyWithRetry(() => import('../pages/Telegram'));
const Line = lazyWithRetry(() => import('../pages/Line'));
const IMessage = lazyWithRetry(() => import('../pages/IMessage'));
const TimeTracking = lazyWithRetry(() => import('../pages/TimeTracking'));
const CustomerChatWidget = lazyWithRetry(() => import('../pages/CustomerChatWidget'));
const Folders = lazyWithRetry(() => import('../pages/Folders'));
const CustomReports = lazyWithRetry(() => import('../pages/CustomReports'));
const CustomReportEditor = lazyWithRetry(() => import('../pages/CustomReportEditor'));
const CustomReportView = lazyWithRetry(() => import('../pages/CustomReportView'));

export interface RouteConfig {
  path: string;
  element: ComponentType;
  protected?: boolean;
  module?: string;
}

export const publicRoutes: RouteConfig[] = [
  { path: '/login', element: lazyWithRetry(() => import('../pages/Login')) },
  { path: '/register', element: lazyWithRetry(() => import('../pages/Register')) },
  { path: '/forgot-password', element: lazyWithRetry(() => import('../pages/ForgotPassword')) },
  { path: '/reset-password', element: lazyWithRetry(() => import('../pages/ResetPassword')) },
  { path: '/satisfaction/:token', element: lazyWithRetry(() => import('../pages/SatisfactionForm')) },
  // 客户聊天门户:基于 sessionToken 的公开访问入口,无需登录
  { path: '/chat/widget', element: CustomerChatWidget },
];

export const protectedRoutes: RouteConfig[] = [
  { path: '/', element: Dashboard },
  { path: '/conversations', element: Conversations },
  { path: '/conversations/:id', element: ConversationDetail },
  { path: '/users', element: Users },
  { path: '/mailboxes', element: Mailboxes },
  { path: '/customers', element: Customers },
  { path: '/tags', element: Tags },
  { path: '/canned-responses', element: CannedResponses },
  { path: '/notifications', element: Notifications },
  { path: '/audit-logs', element: AuditLogs },
  { path: '/settings', element: Settings },
  { path: '/reports', element: Reports },
  { path: '/send-logs', element: SendLogs },
  { path: '/satisfaction', element: Satisfaction },
  { path: '/workflows', element: Workflows },
  { path: '/sla', element: Sla },
  { path: '/macros', element: Macros },
  { path: '/custom-fields', element: CustomFields },
  { path: '/webhooks', element: Webhooks },
  { path: '/roles', element: Roles },
  { path: '/mail-templates', element: MailTemplates },
  { path: '/teams', element: Teams },
  { path: '/email-signatures', element: EmailSignatures },
  { path: '/modules', element: Modules },
  { path: '/notification-templates', element: NotificationTemplates },
  { path: '/notification-rules', element: NotificationRules },
  { path: '/api-keys', element: ApiKeys },
  { path: '/profile', element: Profile },
  { path: '/import-export', element: ImportExport },
  { path: '/system-tools', element: SystemTools },
  { path: '/localization', element: Localization },
  { path: '/calendar', element: Calendar, module: 'calendar' },
  { path: '/knowledge', element: Knowledge, module: 'knowledge' },
  { path: '/chat', element: Chat, module: 'chat' },
  { path: '/wechat', element: WeChat, module: 'wechat' },
  { path: '/whatsapp', element: WhatsApp, module: 'whatsapp' },
  { path: '/feishu', element: Feishu, module: 'feishu' },
  { path: '/dingding', element: DingDing, module: 'dingding' },
  { path: '/messenger', element: Messenger, module: 'messenger' },
  { path: '/telegram', element: Telegram, module: 'telegram' },
  { path: '/line', element: Line, module: 'line' },
  { path: '/imessage', element: IMessage, module: 'imessage' },
  { path: '/time-tracking', element: TimeTracking, module: 'time-tracking' },
  { path: '/folders', element: Folders },
  { path: '/modules/custom-reports', element: CustomReports, module: 'custom-reports' },
  { path: '/modules/custom-reports/new', element: CustomReportEditor, module: 'custom-reports' },
  { path: '/modules/custom-reports/edit/:id', element: CustomReportEditor, module: 'custom-reports' },
  { path: '/modules/custom-reports/view/:id', element: CustomReportView, module: 'custom-reports' },
];
