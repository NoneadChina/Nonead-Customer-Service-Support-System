import type { TranslationKey } from '../locales/zh-Hans';
import { tBound } from '../i18n/locale';

// 角色常量，与后端 UserRoleType 枚举对应
export const UserRole = {
  ReadOnly: 1,
  SuperAdmin: 2,
  AgentSupervisor: 3,
  Agent: 4,
} as const;

const VALID_ROLES: number[] = [UserRole.ReadOnly, UserRole.SuperAdmin, UserRole.AgentSupervisor, UserRole.Agent];

export function isValidRole(role: number): boolean {
  return VALID_ROLES.indexOf(role) !== -1;
}

export function validateRole(role: number): number {
  if (!isValidRole(role)) {
    return UserRole.ReadOnly;
  }
  return role;
}

export function validateStringPermissions(permissions: string[] | undefined | null): string[] {
  if (!Array.isArray(permissions)) {
    return [];
  }
  return permissions.filter((p) => typeof p === 'string' && p.length > 0);
}

export function validateNumericPermissions(permissions: number[] | undefined | null): number[] {
  if (!Array.isArray(permissions)) {
    return [];
  }
  return permissions.filter((p) => typeof p === 'number' && p > 0);
}

// 权限常量，与后端 UserPermissions 静态类对应
export const Permission = {
  DeleteConversations: 1,
  EditConversations: 2,
  EditSavedReplies: 3,
  EditTags: 4,
  EditCustomFolders: 5,
  EditUsers: 10,
} as const;

// 细粒度权限字符串常量（与后端 PermissionItem.Key 对应，共74个）
export const PermissionKey = {
  ConversationsView: 'conversations.view',
  ConversationsCreate: 'conversations.create',
  ConversationsUpdate: 'conversations.update',
  ConversationsDelete: 'conversations.delete',
  ConversationsReply: 'conversations.reply',
  ConversationsAssign: 'conversations.assign',
  ConversationsManageTags: 'conversations.manage_tags',
  ConversationsViewAll: 'conversations.view_all',

  CustomersView: 'customers.view',
  CustomersCreate: 'customers.create',
  CustomersUpdate: 'customers.update',
  CustomersDelete: 'customers.delete',
  CustomersExport: 'customers.export',
  CustomersImport: 'customers.import',

  MailboxesView: 'mailboxes.view',
  MailboxesCreate: 'mailboxes.create',
  MailboxesUpdate: 'mailboxes.update',
  MailboxesDelete: 'mailboxes.delete',
  MailboxesManage: 'mailboxes.manage',

  UsersView: 'users.view',
  UsersCreate: 'users.create',
  UsersUpdate: 'users.update',
  UsersDelete: 'users.delete',
  UsersManageRoles: 'users.manage_roles',

  SettingsView: 'settings.view',
  SettingsUpdate: 'settings.update',
  SettingsManage: 'settings.manage',

  ReportsView: 'reports.view',
  ReportsExport: 'reports.export',
  ReportsManage: 'reports.manage',

  WorkflowsView: 'workflows.view',
  WorkflowsManage: 'workflows.manage',
  SlaView: 'sla.view',
  SlaManage: 'sla.manage',
  MacrosView: 'macros.view',
  MacrosManage: 'macros.manage',
  WebhooksView: 'webhooks.view',
  WebhooksManage: 'webhooks.manage',
  CustomFieldsView: 'custom_fields.view',
  CustomFieldsManage: 'custom_fields.manage',

  TeamsView: 'teams.view',
  TeamsCreate: 'teams.create',
  TeamsUpdate: 'teams.update',
  TeamsDelete: 'teams.delete',
  TeamsManageMembers: 'teams.manage_members',

  CannedResponsesView: 'canned_responses.view',
  CannedResponsesManage: 'canned_responses.manage',
  EmailSignaturesView: 'email_signatures.view',
  EmailSignaturesManage: 'email_signatures.manage',
  MailTemplatesView: 'mail_templates.view',
  MailTemplatesManage: 'mail_templates.manage',
  TagsView: 'tags.view',
  TagsManage: 'tags.manage',
  KnowledgeView: 'knowledge.view',
  KnowledgeManage: 'knowledge.manage',

  ChatView: 'chat.view',
  ChatReply: 'chat.reply',
  CalendarView: 'calendar.view',
  CalendarManage: 'calendar.manage',

  WeChatView: 'wechat.view',
  WeChatManage: 'wechat.manage',

  WhatsAppView: 'whatsapp.view',
  WhatsAppManage: 'whatsapp.manage',

  DingDingView: 'dingding.view',
  DingDingManage: 'dingding.manage',

  // 飞书渠道：后端 RoleService 已定义 feishu.view / feishu.manage，
  // 默认角色（客服主管/专员/只读）均已持有 feishu.view；此前前端漏了这两个键，
  // 导致 pathPermissionMap 无映射 → canAccessItem 恒 false → 非超管既看不到菜单、直连 URL 也被送 /forbidden。
  FeishuView: 'feishu.view',
  FeishuManage: 'feishu.manage',

  // 新增四渠道：后端 RoleService 已定义对应 .view/.manage，
  // DatabaseSeeder 的默认角色 PermissionsJson 已含 .view。缺键会导致菜单不可见 + 直连 URL 被送 /forbidden。
  MessengerView: 'messenger.view',
  MessengerManage: 'messenger.manage',

  TelegramView: 'telegram.view',
  TelegramManage: 'telegram.manage',

  LineView: 'line.view',
  LineManage: 'line.manage',

  IMessageView: 'imessage.view',
  IMessageManage: 'imessage.manage',

  // 工时统计：后端默认角色持有 time-tracking.view（主管另有 time-tracking.manage）
  TimeTrackingView: 'time-tracking.view',
  TimeTrackingManage: 'time-tracking.manage',

  ApiKeysView: 'api_keys.view',
  ApiKeysManage: 'api_keys.manage',
  ImportExportView: 'import_export.view',
  ImportExportManage: 'import_export.manage',
  SystemToolsView: 'system_tools.view',
  SystemToolsManage: 'system_tools.manage',
  LocalizationView: 'localization.view',
  LocalizationManage: 'localization.manage',
  ModulesView: 'modules.view',
  ModulesManage: 'modules.manage',

  AuditLogsView: 'audit_logs.view',
  SendLogsView: 'send_logs.view',
  NotificationsView: 'notifications.view',
  NotificationsManage: 'notifications.manage',
  SatisfactionView: 'satisfaction.view',
} as const;

// 菜单路径 → 所需权限键映射表
// 用于基于后端返回的 stringPermissions 动态控制菜单和路由访问
const pathPermissionMap: Record<string, string> = {
  '/conversations': PermissionKey.ConversationsView,
  '/customers': PermissionKey.CustomersView,
  '/tags': PermissionKey.TagsView,
  '/satisfaction': PermissionKey.SatisfactionView,
  '/mailboxes': PermissionKey.MailboxesView,
  '/folders': PermissionKey.MailboxesView,
  '/mail-templates': PermissionKey.MailTemplatesView,
  '/email-signatures': PermissionKey.EmailSignaturesView,
  '/canned-responses': PermissionKey.CannedResponsesView,
  '/users': PermissionKey.UsersView,
  '/teams': PermissionKey.TeamsView,
  '/roles': PermissionKey.UsersManageRoles,
  '/workflows': PermissionKey.WorkflowsView,
  '/sla': PermissionKey.SlaView,
  '/macros': PermissionKey.MacrosView,
  '/notifications': PermissionKey.NotificationsView,
  '/notification-templates': PermissionKey.NotificationsManage,
  '/notification-rules': PermissionKey.NotificationsManage,
  '/webhooks': PermissionKey.WebhooksView,
  '/api-keys': PermissionKey.ApiKeysView,
  '/custom-fields': PermissionKey.CustomFieldsView,
  '/modules': PermissionKey.ModulesView,
  // 自定义报表：此前无精确键，按最长前缀命中 /modules → 需要 modules.view，
  // 而它位于 data 组（该组权限集只有 reports.view / import_export.view），
  // 造成「看得到入口却进不去」与「进得去但看不到分组」两种矛盾。统一按 reports.view 判定。
  '/modules/custom-reports': PermissionKey.ReportsView,
  '/reports': PermissionKey.ReportsView,
  '/import-export': PermissionKey.ImportExportView,
  '/calendar': PermissionKey.CalendarView,
  '/knowledge': PermissionKey.KnowledgeView,
  '/chat': PermissionKey.ChatView,
  '/wechat': PermissionKey.WeChatView,
  '/whatsapp': PermissionKey.WhatsAppView,
  '/dingding': PermissionKey.DingDingView,
  '/feishu': PermissionKey.FeishuView,
  '/messenger': PermissionKey.MessengerView,
  '/telegram': PermissionKey.TelegramView,
  '/line': PermissionKey.LineView,
  '/imessage': PermissionKey.IMessageView,
  '/time-tracking': PermissionKey.TimeTrackingView,
  '/audit-logs': PermissionKey.AuditLogsView,
  '/send-logs': PermissionKey.SendLogsView,
  '/system-tools': PermissionKey.SystemToolsView,
  '/localization': PermissionKey.LocalizationView,
  '/settings': PermissionKey.SettingsView,
};

// 菜单分组 → 子项权限键集合映射表
// 用于基于 stringPermissions 动态控制分组显示（任一子项有权限即显示）
const groupPermissionMap: Record<string, string[]> = {
  helpdesk: [
    PermissionKey.ConversationsView,
    PermissionKey.CustomersView,
    PermissionKey.TagsView,
    PermissionKey.SatisfactionView,
  ],
  mail: [
    PermissionKey.MailboxesView,
    PermissionKey.MailTemplatesView,
    PermissionKey.EmailSignaturesView,
    PermissionKey.CannedResponsesView,
  ],
  team: [
    PermissionKey.UsersView,
    PermissionKey.TeamsView,
    PermissionKey.UsersManageRoles,
  ],
  automation: [
    PermissionKey.WorkflowsView,
    PermissionKey.SlaView,
    PermissionKey.MacrosView,
  ],
  notification: [
    PermissionKey.NotificationsView,
    PermissionKey.NotificationsManage,
  ],
  integration: [
    PermissionKey.WebhooksView,
    PermissionKey.ApiKeysView,
    PermissionKey.CustomFieldsView,
    PermissionKey.ModulesView,
  ],
  data: [
    PermissionKey.ReportsView,
    PermissionKey.ImportExportView,
  ],
  features: [
    PermissionKey.CalendarView,
    PermissionKey.KnowledgeView,
    PermissionKey.ChatView,
    PermissionKey.WeChatView,
    PermissionKey.WhatsAppView,
    PermissionKey.DingDingView,
    PermissionKey.FeishuView,
    PermissionKey.MessengerView,
    PermissionKey.TelegramView,
    PermissionKey.LineView,
    PermissionKey.IMessageView,
    PermissionKey.TimeTrackingView,
  ],
  system: [
    PermissionKey.AuditLogsView,
    PermissionKey.SendLogsView,
    PermissionKey.SystemToolsView,
    PermissionKey.LocalizationView,
    PermissionKey.SystemToolsManage,
    PermissionKey.SettingsView,
  ],
};

export type UserRoleType = typeof UserRole[keyof typeof UserRole];

export function hasRole(userRole: number, requiredRole: number): boolean {
  const safeRole = validateRole(userRole);
  return safeRole === requiredRole;
}

export function isAdmin(role: number): boolean {
  return validateRole(role) === UserRole.SuperAdmin;
}

export function isSupervisor(role: number): boolean {
  return validateRole(role) === UserRole.AgentSupervisor;
}

export function isAgent(role: number): boolean {
  return validateRole(role) === UserRole.Agent;
}

export function isReadOnly(role: number): boolean {
  return validateRole(role) === UserRole.ReadOnly;
}

// 是否可以管理在线聊天会话（删除/批量删除/垃圾箱）
// 仅超级管理员和客服主管可见
export function canManageChatSessions(role: number): boolean {
  return isAdmin(role) || isSupervisor(role);
}

// 细粒度权限检查（基于后端 PermissionItem.Key 字符串权限）
// 超级管理员拥有所有权限；其他角色需要显式授予对应权限
// 注意：前端权限检查仅用于 UI 显示控制，真正的权限验证在后端
export function hasPermissionKey(role: number, permissions: string[] = [], key: string): boolean {
  const safeRole = validateRole(role);
  const safePermissions = validateStringPermissions(permissions);
  if (isAdmin(safeRole)) return true;
  if (safePermissions.includes('*')) return true;
  return safePermissions.includes(key);
}

// 是否可以编辑会话（conversations.update）
export function canEditConversations(role: number, stringPermissions: string[] = []): boolean {
  return hasPermissionKey(role, stringPermissions, PermissionKey.ConversationsUpdate);
}

// 是否可以删除会话（conversations.delete）
export function canDeleteConversations(role: number, stringPermissions: string[] = []): boolean {
  return hasPermissionKey(role, stringPermissions, PermissionKey.ConversationsDelete);
}

// 是否可以分配会话（conversations.assign）
export function canAssignConversations(role: number, stringPermissions: string[] = []): boolean {
  return hasPermissionKey(role, stringPermissions, PermissionKey.ConversationsAssign);
}

// 是否可以回复会话（conversations.reply）
export function canReplyConversations(role: number, stringPermissions: string[] = []): boolean {
  return hasPermissionKey(role, stringPermissions, PermissionKey.ConversationsReply);
}

// 是否可以创建会话（conversations.create）
export function canCreateConversations(role: number, stringPermissions: string[] = []): boolean {
  return hasPermissionKey(role, stringPermissions, PermissionKey.ConversationsCreate);
}

// 是否可以管理用户（users.manage_roles）
export function canManageUsers(role: number, stringPermissions: string[] = []): boolean {
  return hasPermissionKey(role, stringPermissions, PermissionKey.UsersManageRoles);
}

// 是否可以管理邮箱（mailboxes.update）
export function canManageMailboxes(role: number, stringPermissions: string[] = []): boolean {
  return hasPermissionKey(role, stringPermissions, PermissionKey.MailboxesUpdate);
}

// 是否可以管理系统设置（system_tools.manage）
export function canManageSettings(role: number, stringPermissions: string[] = []): boolean {
  return hasPermissionKey(role, stringPermissions, PermissionKey.SystemToolsManage);
}

// 是否可以导出数据（import_export.view 视为可导出，写操作由后端控制）
export function canExportData(role: number, stringPermissions: string[] = []): boolean {
  return hasPermissionKey(role, stringPermissions, PermissionKey.ImportExportView);
}

// 是否可以查看所有会话（需拥有 conversations.view_all 权限键；超级管理员自动拥有 * 通配符）
export function canViewAllConversations(role: number, stringPermissions: string[] = []): boolean {
  return hasPermissionKey(role, stringPermissions, PermissionKey.ConversationsViewAll);
}

export function canAccessGroup(_role: number, groupKey: string, stringPermissions: string[] = []): boolean {
  const safePermissions = validateStringPermissions(stringPermissions);

  // 超级管理员通过 * 通配符放行
  if (safePermissions.includes('*')) return true;

  // 基于后端返回的字符串权限动态判断：分组内任一子项有权限即显示
  const requiredPermissions = groupPermissionMap[groupKey];
  if (!requiredPermissions || requiredPermissions.length === 0) return false;
  return requiredPermissions.some((perm) => safePermissions.includes(perm));
}

export function canAccessItem(_role: number, path: string, stringPermissions: string[] = []): boolean {
  const safePermissions = validateStringPermissions(stringPermissions);

  // 首页和个人中心对所有登录用户可见
  if (path === '/' || path === '/profile') return true;

  // 超级管理员通过 * 通配符放行
  if (safePermissions.includes('*')) return true;

  // 基于后端返回的字符串权限动态判断
  // 支持子路由：先精确匹配，再按父路径前缀匹配
  let requiredPermission: string | undefined = pathPermissionMap[path];
  if (!requiredPermission) {
    // 查找最长前缀匹配的父路径
    const matchedParent = Object.keys(pathPermissionMap)
      .filter((key) => path.startsWith(key + '/'))
      .sort((a, b) => b.length - a.length)[0];
    if (matchedParent) {
      requiredPermission = pathPermissionMap[matchedParent];
    }
  }

  if (!requiredPermission) return false;
  if (safePermissions.includes('*')) return true;
  return safePermissions.includes(requiredPermission);
}

/**
 * 角色 code（与后端 `UserRoleType` 枚举对应）→ 展示文案 key。
 *
 * code 是稳定键（授权、接口、存库都用它），**永不翻译**；展示只认 key。
 * 文案在真源 `locales/{zh-Hans,en}/format.ts` 的 `format.role.*`。
 */
const ROLE_LABEL_KEYS: Record<number, TranslationKey> = {
  1: 'format.role.readOnly',
  2: 'format.role.superAdmin',
  3: 'format.role.agentSupervisor',
  4: 'format.role.agent',
};

/**
 * 角色显示名。
 * @param role 角色 code
 * @param locale 显式语言（可选，缺省用当前界面语言，见 `i18n/locale.ts`）
 */
export function getRoleLabel(role: number, locale?: string): string {
  const key = ROLE_LABEL_KEYS[role];
  return key ? tBound(key, locale) : tBound('format.unknown', locale);
}
