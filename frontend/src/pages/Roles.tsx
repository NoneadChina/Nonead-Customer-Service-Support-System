import { useState, useEffect } from 'react';
import { roleApi } from '../api';
import type {
  RoleDto,
  RoleOverviewResponse,
  PermissionGroup,
  PermissionItem,
  CreateRoleRequest,
  RoleUserDto,
} from '../api/types';
import UserAvatar from '../components/UserAvatar';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import { useToast } from '../context/ToastContext';
import { useI18n } from '../context/I18nContext';
import { formatDateTime } from '../utils/format';
import { getApiErrorMessage } from '../utils/error';
import type { TranslationKey } from '../locales/zh-Hans';

const inputClass = "w-full px-3 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 transition-all";

/** `t()` 的签名形状：模块级解析函数按参数收 t，不把 t 绑成全局状态 */
type TranslateFn = ReturnType<typeof useI18n>['t'];

/**
 * 后端权限码 → 本文件内查表用的键。
 *
 * 权限码形如「命名空间 + 点 + 动作」（例如会话的查看权限码），与 i18n key 的「命名空间.点分」
 * 形态**完全同形**，而本文件里两种字符串的含义截然不同（一个是后端权限码、一个是前端词条 key），
 * 混在一起时无论人工阅读还是文本级检查都容易误读。因此统一把点归一成 `::` 作为**本文件私有**的
 * 查表键（转换点只有 `permissionCodeKey()` 一处）。**权限码本身不受影响**：请求、存库、比较
 * （`selectedPermissions.includes(p.key)`）仍然使用原始 `p.key`，本函数只作用于下面两张查表。
 */
function permissionCodeKey(code: string): string {
  return code.replace(/\./g, '::');
}

/**
 * 内置四角色名（slug → 既有 `format.role.*`）。
 * 产出方：`NoneadCSS.Api/DatabaseSeeder.cs:963-1013` 的 4 个 `IsSystem = true` 种子角色
 * （slug `admin`/`supervisor`/`agent`/`viewer`，与 `NoneadCSS.Domain/Enums/UserRole.cs`
 * 的 4 个枚举值一一对应）。设计 §6.3：不再渲染 `Roles.Name`。
 * **自定义角色名是用户数据**（`RoleService.CreateAsync` 由请求体写入），不在表内 → 回落原文。
 */
const ROLE_NAME_KEY_BY_SLUG: Record<string, TranslationKey | undefined> = {
  'admin': 'format.role.superAdmin',
  'supervisor': 'format.role.agentSupervisor',
  'agent': 'format.role.agent',
  'viewer': 'format.role.readOnly',
};

/**
 * 权限分组名（16 组）。
 * 产出方：`NoneadCSS.Application/Services/RoleService.cs` `GetAllPermissionGroups()`。
 */
const PERMISSION_GROUP_KEY: Record<string, TranslationKey | undefined> = {
  'conversations': 'teamPermissions.roles.group.conversations',
  'customers': 'teamPermissions.roles.group.customers',
  'mailboxes': 'teamPermissions.roles.group.mailboxes',
  'users': 'teamPermissions.roles.group.users',
  'profile': 'teamPermissions.roles.group.profile',
  'settings': 'teamPermissions.roles.group.settings',
  'reports': 'teamPermissions.roles.group.reports',
  'custom_reports': 'teamPermissions.roles.group.custom_reports',
  'automation': 'teamPermissions.roles.group.automation',
  'teams': 'teamPermissions.roles.group.teams',
  'time_tracking': 'teamPermissions.roles.group.time_tracking',
  'content': 'teamPermissions.roles.group.content',
  'attachments': 'teamPermissions.roles.group.attachments',
  'communication': 'teamPermissions.roles.group.communication',
  'system': 'teamPermissions.roles.group.system',
  'audit': 'teamPermissions.roles.group.audit',
};

/** 权限项名称（91 项，key = 权限 code）。产出方同上。 */
const PERMISSION_LABEL_KEY: Record<string, TranslationKey | undefined> = {
  'conversations::view': 'teamPermissions.roles.perm.conversations.view',
  'conversations::create': 'teamPermissions.roles.perm.conversations.create',
  'conversations::update': 'teamPermissions.roles.perm.conversations.update',
  'conversations::delete': 'teamPermissions.roles.perm.conversations.delete',
  'conversations::reply': 'teamPermissions.roles.perm.conversations.reply',
  'conversations::assign': 'teamPermissions.roles.perm.conversations.assign',
  'conversations::manage_tags': 'teamPermissions.roles.perm.conversations.manage_tags',
  'conversations::view_all': 'teamPermissions.roles.perm.conversations.view_all',
  'customers::view': 'teamPermissions.roles.perm.customers.view',
  'customers::create': 'teamPermissions.roles.perm.customers.create',
  'customers::update': 'teamPermissions.roles.perm.customers.update',
  'customers::delete': 'teamPermissions.roles.perm.customers.delete',
  'customers::export': 'teamPermissions.roles.perm.customers.export',
  'customers::import': 'teamPermissions.roles.perm.customers.import',
  'mailboxes::view': 'teamPermissions.roles.perm.mailboxes.view',
  'mailboxes::create': 'teamPermissions.roles.perm.mailboxes.create',
  'mailboxes::update': 'teamPermissions.roles.perm.mailboxes.update',
  'mailboxes::delete': 'teamPermissions.roles.perm.mailboxes.delete',
  'mailboxes::manage': 'teamPermissions.roles.perm.mailboxes.manage',
  'users::view': 'teamPermissions.roles.perm.users.view',
  'users::create': 'teamPermissions.roles.perm.users.create',
  'users::update': 'teamPermissions.roles.perm.users.update',
  'users::delete': 'teamPermissions.roles.perm.users.delete',
  'users::manage_roles': 'teamPermissions.roles.perm.users.manage_roles',
  'profile::update': 'teamPermissions.roles.perm.profile.update',
  'profile::change_password': 'teamPermissions.roles.perm.profile.change_password',
  'settings::view': 'teamPermissions.roles.perm.settings.view',
  'settings::update': 'teamPermissions.roles.perm.settings.update',
  'settings::manage': 'teamPermissions.roles.perm.settings.manage',
  'reports::view': 'teamPermissions.roles.perm.reports.view',
  'reports::export': 'teamPermissions.roles.perm.reports.export',
  'reports::manage': 'teamPermissions.roles.perm.reports.manage',
  'custom_reports::create': 'teamPermissions.roles.perm.custom_reports.create',
  'custom_reports::update': 'teamPermissions.roles.perm.custom_reports.update',
  'custom_reports::delete': 'teamPermissions.roles.perm.custom_reports.delete',
  'custom_reports::execute': 'teamPermissions.roles.perm.custom_reports.execute',
  'custom_reports::schedules::manage': 'teamPermissions.roles.perm.custom_reports.schedules.manage',
  'workflows::view': 'teamPermissions.roles.perm.workflows.view',
  'workflows::manage': 'teamPermissions.roles.perm.workflows.manage',
  'sla::view': 'teamPermissions.roles.perm.sla.view',
  'sla::manage': 'teamPermissions.roles.perm.sla.manage',
  'macros::view': 'teamPermissions.roles.perm.macros.view',
  'macros::manage': 'teamPermissions.roles.perm.macros.manage',
  'webhooks::view': 'teamPermissions.roles.perm.webhooks.view',
  'webhooks::manage': 'teamPermissions.roles.perm.webhooks.manage',
  'custom_fields::view': 'teamPermissions.roles.perm.custom_fields.view',
  'custom_fields::manage': 'teamPermissions.roles.perm.custom_fields.manage',
  'teams::view': 'teamPermissions.roles.perm.teams.view',
  'teams::create': 'teamPermissions.roles.perm.teams.create',
  'teams::update': 'teamPermissions.roles.perm.teams.update',
  'teams::delete': 'teamPermissions.roles.perm.teams.delete',
  'teams::manage_members': 'teamPermissions.roles.perm.teams.manage_members',
  'time-tracking::manage': 'teamPermissions.roles.perm.time-tracking.manage',
  'canned_responses::view': 'teamPermissions.roles.perm.canned_responses.view',
  'canned_responses::manage': 'teamPermissions.roles.perm.canned_responses.manage',
  'email_signatures::view': 'teamPermissions.roles.perm.email_signatures.view',
  'email_signatures::manage': 'teamPermissions.roles.perm.email_signatures.manage',
  'mail_templates::view': 'teamPermissions.roles.perm.mail_templates.view',
  'mail_templates::manage': 'teamPermissions.roles.perm.mail_templates.manage',
  'tags::view': 'teamPermissions.roles.perm.tags.view',
  'tags::manage': 'teamPermissions.roles.perm.tags.manage',
  'knowledge::view': 'teamPermissions.roles.perm.knowledge.view',
  'knowledge::manage': 'teamPermissions.roles.perm.knowledge.manage',
  'attachments::manage': 'teamPermissions.roles.perm.attachments.manage',
  'chat::view': 'teamPermissions.roles.perm.chat.view',
  'chat::reply': 'teamPermissions.roles.perm.chat.reply',
  'calendar::view': 'teamPermissions.roles.perm.calendar.view',
  'calendar::manage': 'teamPermissions.roles.perm.calendar.manage',
  'wechat::view': 'teamPermissions.roles.perm.wechat.view',
  'wechat::manage': 'teamPermissions.roles.perm.wechat.manage',
  'whatsapp::view': 'teamPermissions.roles.perm.whatsapp.view',
  'whatsapp::manage': 'teamPermissions.roles.perm.whatsapp.manage',
  'feishu::view': 'teamPermissions.roles.perm.feishu.view',
  'feishu::manage': 'teamPermissions.roles.perm.feishu.manage',
  'dingding::view': 'teamPermissions.roles.perm.dingding.view',
  'dingding::manage': 'teamPermissions.roles.perm.dingding.manage',
  'messenger::view': 'teamPermissions.roles.perm.messenger.view',
  'messenger::manage': 'teamPermissions.roles.perm.messenger.manage',
  'telegram::view': 'teamPermissions.roles.perm.telegram.view',
  'telegram::manage': 'teamPermissions.roles.perm.telegram.manage',
  'line::view': 'teamPermissions.roles.perm.line.view',
  'line::manage': 'teamPermissions.roles.perm.line.manage',
  'imessage::view': 'teamPermissions.roles.perm.imessage.view',
  'imessage::manage': 'teamPermissions.roles.perm.imessage.manage',
  'api_keys::view': 'teamPermissions.roles.perm.api_keys.view',
  'api_keys::manage': 'teamPermissions.roles.perm.api_keys.manage',
  'import_export::view': 'teamPermissions.roles.perm.import_export.view',
  'import_export::manage': 'teamPermissions.roles.perm.import_export.manage',
  'system_tools::view': 'teamPermissions.roles.perm.system_tools.view',
  'system_tools::manage': 'teamPermissions.roles.perm.system_tools.manage',
  'localization::view': 'teamPermissions.roles.perm.localization.view',
  'localization::manage': 'teamPermissions.roles.perm.localization.manage',
  'modules::view': 'teamPermissions.roles.perm.modules.view',
  'modules::manage': 'teamPermissions.roles.perm.modules.manage',
  'audit_logs::view': 'teamPermissions.roles.perm.audit_logs.view',
  'send_logs::view': 'teamPermissions.roles.perm.send_logs.view',
  'notifications::view': 'teamPermissions.roles.perm.notifications.view',
  'notifications::manage': 'teamPermissions.roles.perm.notifications.manage',
  'satisfaction::view': 'teamPermissions.roles.perm.satisfaction.view',
};

/** 权限项描述（91 项，key = 权限 code）。产出方同上。 */
const PERMISSION_DESCRIPTION_KEY: Record<string, TranslationKey | undefined> = {
  'conversations::view': 'teamPermissions.roles.permDesc.conversations.view',
  'conversations::create': 'teamPermissions.roles.permDesc.conversations.create',
  'conversations::update': 'teamPermissions.roles.permDesc.conversations.update',
  'conversations::delete': 'teamPermissions.roles.permDesc.conversations.delete',
  'conversations::reply': 'teamPermissions.roles.permDesc.conversations.reply',
  'conversations::assign': 'teamPermissions.roles.permDesc.conversations.assign',
  'conversations::manage_tags': 'teamPermissions.roles.permDesc.conversations.manage_tags',
  'conversations::view_all': 'teamPermissions.roles.permDesc.conversations.view_all',
  'customers::view': 'teamPermissions.roles.permDesc.customers.view',
  'customers::create': 'teamPermissions.roles.permDesc.customers.create',
  'customers::update': 'teamPermissions.roles.permDesc.customers.update',
  'customers::delete': 'teamPermissions.roles.permDesc.customers.delete',
  'customers::export': 'teamPermissions.roles.permDesc.customers.export',
  'customers::import': 'teamPermissions.roles.permDesc.customers.import',
  'mailboxes::view': 'teamPermissions.roles.permDesc.mailboxes.view',
  'mailboxes::create': 'teamPermissions.roles.permDesc.mailboxes.create',
  'mailboxes::update': 'teamPermissions.roles.permDesc.mailboxes.update',
  'mailboxes::delete': 'teamPermissions.roles.permDesc.mailboxes.delete',
  'mailboxes::manage': 'teamPermissions.roles.permDesc.mailboxes.manage',
  'users::view': 'teamPermissions.roles.permDesc.users.view',
  'users::create': 'teamPermissions.roles.permDesc.users.create',
  'users::update': 'teamPermissions.roles.permDesc.users.update',
  'users::delete': 'teamPermissions.roles.permDesc.users.delete',
  'users::manage_roles': 'teamPermissions.roles.permDesc.users.manage_roles',
  'profile::update': 'teamPermissions.roles.permDesc.profile.update',
  'profile::change_password': 'teamPermissions.roles.permDesc.profile.change_password',
  'settings::view': 'teamPermissions.roles.permDesc.settings.view',
  'settings::update': 'teamPermissions.roles.permDesc.settings.update',
  'settings::manage': 'teamPermissions.roles.permDesc.settings.manage',
  'reports::view': 'teamPermissions.roles.permDesc.reports.view',
  'reports::export': 'teamPermissions.roles.permDesc.reports.export',
  'reports::manage': 'teamPermissions.roles.permDesc.reports.manage',
  'custom_reports::create': 'teamPermissions.roles.permDesc.custom_reports.create',
  'custom_reports::update': 'teamPermissions.roles.permDesc.custom_reports.update',
  'custom_reports::delete': 'teamPermissions.roles.permDesc.custom_reports.delete',
  'custom_reports::execute': 'teamPermissions.roles.permDesc.custom_reports.execute',
  'custom_reports::schedules::manage': 'teamPermissions.roles.permDesc.custom_reports.schedules.manage',
  'workflows::view': 'teamPermissions.roles.permDesc.workflows.view',
  'workflows::manage': 'teamPermissions.roles.permDesc.workflows.manage',
  'sla::view': 'teamPermissions.roles.permDesc.sla.view',
  'sla::manage': 'teamPermissions.roles.permDesc.sla.manage',
  'macros::view': 'teamPermissions.roles.permDesc.macros.view',
  'macros::manage': 'teamPermissions.roles.permDesc.macros.manage',
  'webhooks::view': 'teamPermissions.roles.permDesc.webhooks.view',
  'webhooks::manage': 'teamPermissions.roles.permDesc.webhooks.manage',
  'custom_fields::view': 'teamPermissions.roles.permDesc.custom_fields.view',
  'custom_fields::manage': 'teamPermissions.roles.permDesc.custom_fields.manage',
  'teams::view': 'teamPermissions.roles.permDesc.teams.view',
  'teams::create': 'teamPermissions.roles.permDesc.teams.create',
  'teams::update': 'teamPermissions.roles.permDesc.teams.update',
  'teams::delete': 'teamPermissions.roles.permDesc.teams.delete',
  'teams::manage_members': 'teamPermissions.roles.permDesc.teams.manage_members',
  'time-tracking::manage': 'teamPermissions.roles.permDesc.time-tracking.manage',
  'canned_responses::view': 'teamPermissions.roles.permDesc.canned_responses.view',
  'canned_responses::manage': 'teamPermissions.roles.permDesc.canned_responses.manage',
  'email_signatures::view': 'teamPermissions.roles.permDesc.email_signatures.view',
  'email_signatures::manage': 'teamPermissions.roles.permDesc.email_signatures.manage',
  'mail_templates::view': 'teamPermissions.roles.permDesc.mail_templates.view',
  'mail_templates::manage': 'teamPermissions.roles.permDesc.mail_templates.manage',
  'tags::view': 'teamPermissions.roles.permDesc.tags.view',
  'tags::manage': 'teamPermissions.roles.permDesc.tags.manage',
  'knowledge::view': 'teamPermissions.roles.permDesc.knowledge.view',
  'knowledge::manage': 'teamPermissions.roles.permDesc.knowledge.manage',
  'attachments::manage': 'teamPermissions.roles.permDesc.attachments.manage',
  'chat::view': 'teamPermissions.roles.permDesc.chat.view',
  'chat::reply': 'teamPermissions.roles.permDesc.chat.reply',
  'calendar::view': 'teamPermissions.roles.permDesc.calendar.view',
  'calendar::manage': 'teamPermissions.roles.permDesc.calendar.manage',
  'wechat::view': 'teamPermissions.roles.permDesc.wechat.view',
  'wechat::manage': 'teamPermissions.roles.permDesc.wechat.manage',
  'whatsapp::view': 'teamPermissions.roles.permDesc.whatsapp.view',
  'whatsapp::manage': 'teamPermissions.roles.permDesc.whatsapp.manage',
  'feishu::view': 'teamPermissions.roles.permDesc.feishu.view',
  'feishu::manage': 'teamPermissions.roles.permDesc.feishu.manage',
  'dingding::view': 'teamPermissions.roles.permDesc.dingding.view',
  'dingding::manage': 'teamPermissions.roles.permDesc.dingding.manage',
  'messenger::view': 'teamPermissions.roles.permDesc.messenger.view',
  'messenger::manage': 'teamPermissions.roles.permDesc.messenger.manage',
  'telegram::view': 'teamPermissions.roles.permDesc.telegram.view',
  'telegram::manage': 'teamPermissions.roles.permDesc.telegram.manage',
  'line::view': 'teamPermissions.roles.permDesc.line.view',
  'line::manage': 'teamPermissions.roles.permDesc.line.manage',
  'imessage::view': 'teamPermissions.roles.permDesc.imessage.view',
  'imessage::manage': 'teamPermissions.roles.permDesc.imessage.manage',
  'api_keys::view': 'teamPermissions.roles.permDesc.api_keys.view',
  'api_keys::manage': 'teamPermissions.roles.permDesc.api_keys.manage',
  'import_export::view': 'teamPermissions.roles.permDesc.import_export.view',
  'import_export::manage': 'teamPermissions.roles.permDesc.import_export.manage',
  'system_tools::view': 'teamPermissions.roles.permDesc.system_tools.view',
  'system_tools::manage': 'teamPermissions.roles.permDesc.system_tools.manage',
  'localization::view': 'teamPermissions.roles.permDesc.localization.view',
  'localization::manage': 'teamPermissions.roles.permDesc.localization.manage',
  'modules::view': 'teamPermissions.roles.permDesc.modules.view',
  'modules::manage': 'teamPermissions.roles.permDesc.modules.manage',
  'audit_logs::view': 'teamPermissions.roles.permDesc.audit_logs.view',
  'send_logs::view': 'teamPermissions.roles.permDesc.send_logs.view',
  'notifications::view': 'teamPermissions.roles.permDesc.notifications.view',
  'notifications::manage': 'teamPermissions.roles.permDesc.notifications.manage',
  'satisfaction::view': 'teamPermissions.roles.permDesc.satisfaction.view',
};

/** 角色显示名：内置角色走字典，自定义角色名回落 `Roles.Name`（用户数据，不翻译） */
function roleLabelOf(role: RoleDto, t: TranslateFn): string {
  const key = ROLE_NAME_KEY_BY_SLUG[role.slug];
  return key ? t(key) : role.name;
}

/** 权限分组显示名：未收录的分组码回落 `group.label`（未知码分支类型可达） */
function groupLabelOf(group: PermissionGroup, t: TranslateFn): string {
  const key = PERMISSION_GROUP_KEY[group.key];
  return key ? t(key) : group.label;
}

/** 权限项显示名：未收录的权限码回落 `permission.label` */
function permissionLabelOf(permission: PermissionItem, t: TranslateFn): string {
  const key = PERMISSION_LABEL_KEY[permissionCodeKey(permission.key)];
  return key ? t(key) : permission.label;
}

/** 权限项描述：未收录的权限码回落 `permission.description` */
function permissionDescriptionOf(permission: PermissionItem, t: TranslateFn): string | null {
  const key = PERMISSION_DESCRIPTION_KEY[permissionCodeKey(permission.key)];
  return key ? t(key) : permission.description;
}

export default function Roles() {
  const [activeTab, setActiveTab] = useState<'overview' | 'list' | 'users'>('overview');
  const [overview, setOverview] = useState<RoleOverviewResponse | null>(null);
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const toast = useToast();
  const { t } = useI18n();
  const [loading, setLoading] = useState(false);
  const [totalItems, setTotalItems] = useState(0);
  const [page, setPage] = useState(1);
  const [keyword, setKeyword] = useState('');
  const [searchInput, setSearchInput] = useState('');
  const [showEditor, setShowEditor] = useState(false);
  const [editingRole, setEditingRole] = useState<RoleDto | null>(null);
  const [selectedRole, setSelectedRole] = useState<RoleDto | null>(null);
  const [roleUsers, setRoleUsers] = useState<RoleUserDto[]>([]);
  const pageSize = 10;

  // 垃圾箱相关状态
  const [trashOpen, setTrashOpen] = useState(false);
  const [trashList, setTrashList] = useState<RoleDto[]>([]);
  const [trashLoading, setTrashLoading] = useState(false);
  const [trashError, setTrashError] = useState('');
  const [trashSearch, setTrashSearch] = useState('');
  const [trashPage, setTrashPage] = useState(1);
  const [trashPageSize] = useState(10);
  const [trashTotalItems, setTrashTotalItems] = useState(0);
  const [trashTotalPages, setTrashTotalPages] = useState(0);
  const [trashCount, setTrashCount] = useState(0);

  const [restoreTarget, setRestoreTarget] = useState<RoleDto | null>(null);
  const [restoring, setRestoring] = useState(false);

  const [hardDeleteTarget, setHardDeleteTarget] = useState<RoleDto | null>(null);
  const [hardDeleting, setHardDeleting] = useState(false);

  const [clearTrashOpen, setClearTrashOpen] = useState(false);
  const [clearingTrash, setClearingTrash] = useState(false);

  const [deleteTarget, setDeleteTarget] = useState<RoleDto | null>(null);
  const [deleting, setDeleting] = useState(false);

  useEffect(() => {
    if (activeTab === 'overview') loadOverview();
    if (activeTab === 'list') loadList();
    if (activeTab === 'users' && selectedRole) loadRoleUsers();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTab]);

  useEffect(() => {
    if (activeTab === 'list') loadList();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, keyword]);

  async function loadOverview() {
    setLoading(true);
    try {
      const [data, trashCountResp] = await Promise.all([roleApi.overview(), roleApi.getTrashCount()]);
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
        roleApi.list({ page, pageSize, keyword }),
        roleApi.getTrashCount(),
      ]);
      setRoles(data.data);
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
      const data = await roleApi.getTrash(trashPage, trashPageSize, trashSearch);
      setTrashList(data.data ?? []);
      setTrashTotalItems(data.totalItems ?? 0);
      setTrashTotalPages(data.totalPages ?? 0);
      const countResp = await roleApi.getTrashCount();
      setTrashCount(countResp?.count ?? 0);
    } catch (e: unknown) {
      setTrashError(getApiErrorMessage(e, t('teamPermissions.roles.toast.loadTrashFailed')));
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
      await roleApi.restore(restoreTarget.id);
      setRestoreTarget(null);
      await loadTrash();
      if (activeTab === 'list') await loadList();
      if (activeTab === 'overview') await loadOverview();
    } catch (e: unknown) {
      setTrashError(e instanceof Error ? e.message : t('teamPermissions.roles.toast.restoreFailed'));
    } finally {
      setRestoring(false);
    }
  }

  async function handleHardDelete() {
    if (!hardDeleteTarget) return;
    setHardDeleting(true);
    try {
      await roleApi.hardDelete(hardDeleteTarget.id);
      setHardDeleteTarget(null);
      await loadTrash();
    } catch (e: unknown) {
      setTrashError(e instanceof Error ? e.message : t('teamPermissions.roles.toast.hardDeleteFailed'));
    } finally {
      setHardDeleting(false);
    }
  }

  async function handleClearTrash() {
    setClearingTrash(true);
    try {
      await roleApi.clearTrash();
      setTrashList([]);
      setTrashCount(0);
      setTrashTotalItems(0);
      setTrashTotalPages(0);
      setClearTrashOpen(false);
    } catch (e: unknown) {
      setTrashError(getApiErrorMessage(e, t('teamPermissions.roles.toast.clearTrashFailed')));
    } finally {
      setClearingTrash(false);
    }
  }

  async function loadRoleUsers() {
    if (!selectedRole) return;
    setLoading(true);
    try {
      const data = await roleApi.roleUsers(selectedRole.id);
      setRoleUsers(data);
    } finally {
      setLoading(false);
    }
  }

  function handleCreate() {
    setEditingRole(null);
    setShowEditor(true);
  }

  function handleEdit(role: RoleDto) {
    setEditingRole(role);
    setShowEditor(true);
  }

  async function handleDelete() {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await roleApi.remove(deleteTarget.id);
      setDeleteTarget(null);
      setTrashCount((c) => c + 1);
      if (activeTab === 'list') loadList();
      if (activeTab === 'overview') loadOverview();
    } catch (e: unknown) {
      toast.error(getApiErrorMessage(e, t('teamPermissions.roles.toast.deleteFailed')));
    } finally {
      setDeleting(false);
    }
  }

  function handleViewUsers(role: RoleDto) {
    setSelectedRole(role);
    setActiveTab('users');
  }

  async function handleSave(data: CreateRoleRequest) {
    if (editingRole) {
      await roleApi.update(editingRole.id, { ...data, id: editingRole.id });
      toast.success(t('teamPermissions.roles.toast.updated'));
    } else {
      await roleApi.create(data);
      toast.success(t('teamPermissions.roles.toast.created'));
    }
    setShowEditor(false);
    setEditingRole(null);
    if (activeTab === 'list') loadList();
    if (activeTab === 'overview') loadOverview();
  }

  async function handleRemoveAssignment(ur: RoleUserDto) {
    if (!confirm(t('teamPermissions.roles.confirm.removeAssignment', { name: ur.userName }))) return;
    toast.info(t('teamPermissions.roles.toast.adjustInUsers'));
  }

  function handleSearch() {
    setKeyword(searchInput.trim());
    setPage(1);
  }

  function handleResetSearch() {
    setSearchInput('');
    setKeyword('');
    setPage(1);
  }

  const totalPages = Math.ceil(totalItems / pageSize);

  const statCards: {
    labelKey: TranslationKey;
    value: number;
    icon: string;
    bg: string;
    text: string;
  }[] = overview ? [
    { labelKey: 'teamPermissions.roles.stat.totalRoles', value: overview.totalRoles, icon: 'M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z', bg: 'bg-blue-50 dark:bg-blue-900/20', text: 'text-blue-600 dark:text-blue-400' },
    { labelKey: 'teamPermissions.roles.stat.systemRoles', value: overview.systemRoles, icon: 'M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z', bg: 'bg-purple-50 dark:bg-purple-900/20', text: 'text-purple-600 dark:text-purple-400' },
    { labelKey: 'teamPermissions.roles.stat.customRoles', value: overview.customRoles, icon: 'M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z', bg: 'bg-emerald-50 dark:bg-emerald-900/20', text: 'text-emerald-600 dark:text-emerald-400' },
    { labelKey: 'teamPermissions.roles.stat.totalAssignments', value: overview.totalRoleAssignments, icon: 'M17 20h5v-2a4 4 0 00-3-3.87M9 20H4v-2a4 4 0 013-3.87m6-1.13a4 4 0 10-4-4 4 4 0 004 4z', bg: 'bg-amber-50 dark:bg-amber-900/20', text: 'text-amber-600 dark:text-amber-400' },
  ] : [];

  const tabs: {
    key: 'overview' | 'list' | 'users';
    labelKey: TranslationKey;
    labelParams?: Parameters<TranslateFn>[1];
  }[] = [
    { key: 'overview', labelKey: 'teamPermissions.roles.tab.overview' },
    { key: 'list', labelKey: 'teamPermissions.roles.tab.list' },
    ...(selectedRole
      ? [
          {
            key: 'users' as const,
            labelKey: 'teamPermissions.roles.tab.usersOfRole' as const,
            labelParams: { name: roleLabelOf(selectedRole, t) },
          },
        ]
      : []),
  ];

  return (
    <div className="space-y-6">
      {/* 页头 */}
      <div className="bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-800 dark:to-slate-800/50 rounded-2xl p-6 border border-primary-100/50 dark:border-slate-700/50">
        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100">{t('teamPermissions.roles.title')}</h1>
            <p className="text-sm text-gray-500 dark:text-slate-400 mt-1">{t('teamPermissions.roles.subtitle')}</p>
          </div>
          {activeTab !== 'users' && (
            <div className="flex items-center gap-2">
              <button
                onClick={openTrashModal}
                className="inline-flex items-center justify-center gap-1.5 px-4 py-2.5 text-sm font-medium bg-white dark:bg-slate-700 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-200 rounded-xl shadow-sm hover:bg-gray-50 dark:hover:bg-slate-600 transition-all"
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                </svg>
                {t('teamPermissions.roles.trash.button')}
                {trashCount > 0 && (
                  <span className="px-1.5 py-0.5 text-xs rounded-full bg-red-100 dark:bg-red-900/30 text-red-600 dark:text-red-400">
                    {trashCount}
                  </span>
                )}
              </button>
              <button
                onClick={handleCreate}
                className="inline-flex items-center justify-center gap-1.5 px-4 py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 text-white rounded-xl shadow-sm shadow-primary-200/50 dark:shadow-primary-900/30 transition-all hover:-translate-y-0.5"
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m8-8H4" />
                </svg>
                {t('teamPermissions.roles.create')}
              </button>
            </div>
          )}
        </div>
      </div>

      {/* Tab 切换 */}
      <div className="flex items-center gap-3 flex-wrap">
        <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 p-1.5 shadow-sm inline-flex">
          {tabs.map(tab => (
            <button
              key={tab.key}
              onClick={() => {
                if (tab.key === 'users' && !selectedRole) return;
                setActiveTab(tab.key as typeof activeTab);
              }}
              className={`px-5 py-2 text-sm font-medium rounded-xl transition-all ${
                activeTab === tab.key
                  ? 'bg-primary-600 text-white shadow-sm'
                  : 'text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-200'
              }`}
            >
              {t(tab.labelKey, tab.labelParams)}
            </button>
          ))}
        </div>
        {selectedRole && (
          <button
            onClick={() => {
              setSelectedRole(null);
              setActiveTab('list');
            }}
            className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-gray-600 dark:text-slate-300 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
          >
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M10 19l-7-7m0 0l7-7m-7 7h18" />
            </svg>
            {t('teamPermissions.roles.backToList')}
          </button>
        )}
      </div>

      {loading && (
        <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 p-12 text-center">
          <svg className="animate-spin w-8 h-8 mx-auto mb-3 text-primary-500" fill="none" viewBox="0 0 24 24">
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
          </svg>
          <p className="text-sm text-gray-500 dark:text-slate-400">{t('common.shell.loading')}</p>
        </div>
      )}

      {/* 概览 */}
      {activeTab === 'overview' && overview && !loading && (
        <div className="space-y-6">
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

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            {/* 角色用户分布 */}
            <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 p-6 shadow-sm">
              <div className="flex items-center gap-2 mb-4">
                <svg className="w-5 h-5 text-primary-600 dark:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
                </svg>
                <h3 className="text-base font-semibold text-gray-900 dark:text-slate-100">{t('teamPermissions.roles.section.usersPerRole')}</h3>
              </div>
              <div className="space-y-3">
                {Object.entries(overview.usersPerRole).map(([roleName, count]) => {
                  const maxCount = Math.max(1, Math.max(...Object.values(overview.usersPerRole)));
                  const namedRole = overview.roles.find(r => r.name === roleName);
                  return (
                    <div key={roleName} className="flex items-center gap-3">
                      <span className="text-sm text-gray-600 dark:text-slate-300 w-28 truncate">{namedRole ? roleLabelOf(namedRole, t) : roleName}</span>
                      <div className="flex-1 bg-gray-100 dark:bg-slate-700 rounded-full h-2.5 overflow-hidden">
                        <div
                          className="bg-gradient-to-r from-primary-500 to-indigo-500 h-2.5 rounded-full transition-all duration-500"
                          style={{ width: `${Math.min(100, (count / maxCount) * 100)}%` }}
                        />
                      </div>
                      <span className="text-sm font-semibold text-gray-900 dark:text-slate-100 w-8 text-right">{count}</span>
                    </div>
                  );
                })}
              </div>
            </div>

            {/* 权限分组概览 */}
            <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 p-6 shadow-sm">
              <div className="flex items-center gap-2 mb-4">
                <svg className="w-5 h-5 text-primary-600 dark:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
                </svg>
                <h3 className="text-base font-semibold text-gray-900 dark:text-slate-100">{t('teamPermissions.roles.section.permissionGroups')}</h3>
              </div>
              <div className="grid grid-cols-2 gap-3">
                {overview.permissionGroups.map(group => (
                  <div
                    key={group.key}
                    className="p-3.5 bg-gray-50/70 dark:bg-slate-900/40 rounded-xl hover:bg-primary-50/50 dark:hover:bg-slate-700/40 transition-all"
                  >
                    <div className="font-medium text-gray-900 dark:text-slate-100 text-sm">{groupLabelOf(group, t)}</div>
                    <div className="text-xs text-gray-500 dark:text-slate-400 mt-1 flex items-center gap-1">
                      <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
                      </svg>
                      {t('teamPermissions.roles.count.permissionsOfGroup', { count: group.permissions.length })}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>

          {/* 所有角色 */}
          <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 p-6 shadow-sm">
            <div className="flex items-center gap-2 mb-4">
              <svg className="w-5 h-5 text-primary-600 dark:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M17 20h5v-2a4 4 0 00-3-3.87M9 20H4v-2a4 4 0 013-3.87m6-1.13a4 4 0 10-4-4 4 4 0 004 4z" />
              </svg>
              <h3 className="text-base font-semibold text-gray-900 dark:text-slate-100">{t('teamPermissions.roles.section.allRoles')}</h3>
            </div>
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
              {overview.roles.map(role => (
                <div
                  key={role.id}
                  className="p-4 border border-gray-200 dark:border-slate-700 rounded-2xl hover:border-primary-300 hover:shadow-md hover:bg-primary-50/30 dark:hover:bg-slate-700/30 transition-all cursor-pointer"
                  onClick={() => handleEdit(role)}
                >
                  <div className="flex items-start justify-between mb-2">
                    <div className="w-9 h-9 bg-gradient-to-br from-primary-500 to-indigo-500 rounded-xl flex items-center justify-center text-white font-bold text-sm shrink-0">
                      {roleLabelOf(role, t).charAt(0)}
                    </div>
                    {role.isSystem && (
                      <span className="inline-flex items-center px-2 py-0.5 text-xs font-medium rounded-full ring-1 bg-purple-50 text-purple-700 ring-purple-100 dark:bg-purple-900/30 dark:text-purple-300 dark:ring-purple-800/50">
                        {t('teamPermissions.roles.badge.system')}
                      </span>
                    )}
                  </div>
                  <div className="font-medium text-gray-900 dark:text-slate-100 mt-2">{roleLabelOf(role, t)}</div>
                  <div className="text-xs text-gray-500 dark:text-slate-400 mt-1 line-clamp-2 h-8">{role.description || t('teamPermissions.roles.noDescription')}</div>
                  <div className="flex items-center justify-between text-xs mt-3 pt-3 border-t border-gray-100 dark:border-slate-700/50">
                    <span className="flex items-center gap-1 text-gray-500 dark:text-slate-400">
                      <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
                      </svg>
                      {t('teamPermissions.roles.count.permissionsShort', { count: role.permissions.length })}
                    </span>
                    <span className="flex items-center gap-1 text-primary-600 dark:text-primary-400 font-medium">
                      <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z" />
                      </svg>
                      {t('teamPermissions.roles.count.members', { count: role.userCount })}
                    </span>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* 角色列表 */}
      {activeTab === 'list' && !loading && (
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
                  placeholder={t('teamPermissions.roles.searchPlaceholder')}
                  value={searchInput}
                  onChange={e => setSearchInput(e.target.value)}
                  onKeyDown={e => e.key === 'Enter' && handleSearch()}
                  className="w-full pl-9 pr-4 py-2.5 text-sm bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500/20 focus:border-primary-500 transition-all"
                />
              </div>
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
            </div>
          </div>

          {/* 列表 */}
          <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 overflow-hidden shadow-sm">
            {/* PC 端表格视图 */}
            <div className="hidden md:block overflow-x-auto">
              <table className="min-w-full divide-y divide-gray-100 dark:divide-slate-700/30">
                <thead className="bg-gray-50/50 dark:bg-slate-900/50">
                  <tr>
                    <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.column.name')}</th>
                    <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.column.slug')}</th>
                    <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.column.type')}</th>
                    <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.column.permissionCount')}</th>
                    <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.column.memberCount')}</th>
                    <th className="px-5 py-3.5 text-right text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.column.actions')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100 dark:divide-slate-700/30 virtual-list">
                  {roles.map((role, index) => (
                    <tr key={role.id} className={`transition-all duration-150 ${index % 2 === 0 ? 'bg-white dark:bg-slate-800' : 'bg-gray-50/50 dark:bg-slate-800/50'} hover:bg-primary-50/50 dark:hover:bg-slate-700/50`}>
                      <td className="px-5 py-3.5">
                        <div className="flex items-center gap-3">
                          <div className="w-9 h-9 bg-gradient-to-br from-primary-500 to-indigo-500 rounded-xl flex items-center justify-center text-white font-bold text-sm shrink-0">
                            {roleLabelOf(role, t).charAt(0)}
                          </div>
                          <div className="min-w-0">
                            <div className="font-medium text-gray-900 dark:text-slate-100">{roleLabelOf(role, t)}</div>
                            <div className="text-xs text-gray-500 dark:text-slate-400 truncate max-w-xs">{role.description || t('teamPermissions.roles.noDescription')}</div>
                          </div>
                        </div>
                      </td>
                      <td className="px-5 py-3.5">
                        <code className="text-xs bg-gray-100 dark:bg-slate-700 text-gray-700 dark:text-slate-300 px-2 py-1 rounded-lg">{role.slug}</code>
                      </td>
                      <td className="px-5 py-3.5">
                        <span className={`inline-flex items-center px-2.5 py-1 text-xs font-medium rounded-full ring-1 ${
                          role.isSystem
                            ? 'bg-purple-50 text-purple-700 ring-purple-100 dark:bg-purple-900/30 dark:text-purple-300 dark:ring-purple-800/50'
                            : 'bg-blue-50 text-blue-700 ring-blue-100 dark:bg-blue-900/30 dark:text-blue-300 dark:ring-blue-800/50'
                        }`}>
                          {role.isSystem ? t('teamPermissions.roles.badge.system') : t('teamPermissions.roles.badge.custom')}
                        </span>
                      </td>
                      <td className="px-5 py-3.5 text-sm text-gray-700 dark:text-slate-300">
                        <span className="inline-flex items-center gap-1">
                          <svg className="w-4 h-4 text-primary-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                            <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
                          </svg>
                          {role.permissions.length}
                        </span>
                      </td>
                      <td className="px-5 py-3.5">
                        <button
                          className="inline-flex items-center gap-1 text-sm text-primary-600 hover:text-primary-700 dark:text-primary-400 font-medium hover:underline"
                          onClick={() => handleViewUsers(role)}
                        >
                          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z" />
                          </svg>
                          {t('teamPermissions.roles.count.users', { count: role.userCount })}
                        </button>
                      </td>
                      <td className="px-5 py-3.5 text-right">
                        <div className="flex items-center justify-end gap-1">
                          <button
                            onClick={() => handleEdit(role)}
                            className="p-2 text-gray-400 dark:text-slate-500 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/20 rounded-lg transition-all"
                            title={t('common.edit')}
                          >
                            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                              <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                            </svg>
                          </button>
                          {!role.isSystem && (
                            <button
                              onClick={() => setDeleteTarget(role)}
                              className="p-2 text-gray-400 dark:text-slate-500 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg transition-all"
                              title={t('common.delete')}
                            >
                              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                                <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                              </svg>
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {roles.length === 0 && (
                <div className="p-12 text-center">
                  <svg className="w-12 h-12 mx-auto mb-3 text-gray-300 dark:text-slate-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
                  </svg>
                  <p className="text-sm text-gray-500 dark:text-slate-400">
                    {keyword ? t('teamPermissions.roles.emptyNoMatch') : t('teamPermissions.roles.empty')}
                  </p>
                </div>
              )}
            </div>

            {/* 移动端卡片视图 */}
            {roles.length === 0 ? (
              <div className="md:hidden p-12 text-center">
                <svg className="w-12 h-12 mx-auto mb-3 text-gray-300 dark:text-slate-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
                </svg>
                <p className="text-sm text-gray-500 dark:text-slate-400">
                  {keyword ? t('teamPermissions.roles.emptyNoMatch') : t('teamPermissions.roles.empty')}
                </p>
              </div>
            ) : (
              <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/30">
                {roles.map((role, index) => (
                  <div key={role.id} className={`p-4 ${index % 2 === 0 ? 'bg-white dark:bg-slate-800' : 'bg-gray-50/50 dark:bg-slate-800/50'} hover:bg-primary-50/50 dark:hover:bg-slate-700/50 transition-all`}>
                    <div className="flex items-center justify-between gap-3 mb-3">
                      <div className="flex items-center gap-3 min-w-0">
                        <div className="w-9 h-9 bg-gradient-to-br from-primary-500 to-indigo-500 rounded-xl flex items-center justify-center text-white font-bold text-sm shrink-0">
                          {roleLabelOf(role, t).charAt(0)}
                        </div>
                        <div className="min-w-0">
                          <div className="font-medium text-gray-900 dark:text-slate-100 truncate">{roleLabelOf(role, t)}</div>
                          <div className="text-xs text-gray-500 dark:text-slate-400 truncate">{role.description || t('teamPermissions.roles.noDescription')}</div>
                        </div>
                      </div>
                      <span className={`inline-flex items-center px-2.5 py-1 text-xs font-medium rounded-full ring-1 flex-shrink-0 ${
                        role.isSystem
                          ? 'bg-purple-50 text-purple-700 ring-purple-100 dark:bg-purple-900/30 dark:text-purple-300 dark:ring-purple-800/50'
                          : 'bg-blue-50 text-blue-700 ring-blue-100 dark:bg-blue-900/30 dark:text-blue-300 dark:ring-blue-800/50'
                      }`}>
                        {role.isSystem ? t('teamPermissions.roles.badge.system') : t('teamPermissions.roles.badge.custom')}
                      </span>
                    </div>
                    <div className="flex items-center gap-2 mb-3 flex-wrap">
                      <code className="text-xs bg-gray-100 dark:bg-slate-700 text-gray-700 dark:text-slate-300 px-2 py-1 rounded-lg">{role.slug}</code>
                      <span className="inline-flex items-center gap-1 text-xs text-gray-600 dark:text-slate-400">
                        <svg className="w-3.5 h-3.5 text-primary-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
                        </svg>
                        {t('teamPermissions.roles.count.permissionsShort', { count: role.permissions.length })}
                      </span>
                      <button
                        className="inline-flex items-center gap-1 text-xs text-primary-600 hover:text-primary-700 dark:text-primary-400 font-medium"
                        onClick={() => handleViewUsers(role)}
                      >
                        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z" />
                        </svg>
                        {t('teamPermissions.roles.count.members', { count: role.userCount })}
                      </button>
                    </div>
                    <div className="flex items-center gap-1.5 pt-3 border-t border-gray-100 dark:border-slate-700/50">
                      <button
                        onClick={() => handleEdit(role)}
                        className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-medium text-primary-600 bg-primary-50 dark:bg-primary-900/20 rounded-lg hover:bg-primary-100 dark:hover:bg-primary-900/30 transition-all"
                      >
                        {t('common.edit')}
                      </button>
                      {!role.isSystem && (
                        <button
                          onClick={() => setDeleteTarget(role)}
                          className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-medium text-red-600 bg-red-50 dark:bg-red-900/20 rounded-lg hover:bg-red-100 dark:hover:bg-red-900/30 transition-all"
                        >
                          {t('common.delete')}
                        </button>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            )}

            {totalPages > 1 && (
              <div className="px-5 py-3.5 border-t border-gray-100 dark:border-slate-700/50 flex items-center justify-between bg-gray-50/30 dark:bg-slate-900/30">
                <span className="text-sm text-gray-500 dark:text-slate-400">
                  {t('teamPermissions.roles.pagination.summary', { total: totalItems, page, totalPages })}
                </span>
                <div className="flex gap-2">
                  <button
                    onClick={() => setPage(p => Math.max(1, p - 1))}
                    disabled={page === 1}
                    className="px-3 py-1.5 text-sm border border-gray-200 dark:border-slate-600 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
                  >
                    {t('teamPermissions.roles.pagination.prev')}
                  </button>
                  <button
                    onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                    disabled={page === totalPages}
                    className="px-3 py-1.5 text-sm border border-gray-200 dark:border-slate-600 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
                  >
                    {t('teamPermissions.roles.pagination.next')}
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      {/* 角色成员 */}
      {activeTab === 'users' && selectedRole && !loading && (
        <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-100 dark:border-slate-700/50 overflow-hidden shadow-sm">
          <div className="px-6 py-4 border-b border-gray-100 dark:border-slate-700/50 flex items-center justify-between bg-gradient-to-r from-primary-50/50 to-indigo-50/50 dark:from-slate-900/30 dark:to-slate-900/10">
            <div className="flex items-center gap-3">
              <div className="w-12 h-12 bg-gradient-to-br from-primary-500 to-indigo-500 rounded-2xl flex items-center justify-center text-white font-bold">
                {roleLabelOf(selectedRole, t).charAt(0)}
              </div>
              <div>
                <h3 className="font-semibold text-gray-900 dark:text-slate-100">{t('teamPermissions.roles.membersOfRoleTitle', { name: roleLabelOf(selectedRole, t) })}</h3>
                <p className="text-sm text-gray-500 dark:text-slate-400 mt-0.5">{selectedRole.description || t('teamPermissions.roles.noDescription')}</p>
              </div>
            </div>
            <button
              className="inline-flex items-center gap-1.5 px-4 py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 text-white rounded-xl shadow-sm transition-all"
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m8-8H4" />
              </svg>
              {t('teamPermissions.roles.assignUsers')}
            </button>
          </div>
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-100 dark:divide-slate-700/30">
              <thead className="bg-gray-50/50 dark:bg-slate-900/50">
                <tr>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.memberColumn.user')}</th>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.memberColumn.email')}</th>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.memberColumn.scope')}</th>
                  <th className="px-5 py-3.5 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.memberColumn.assignedAt')}</th>
                  <th className="px-5 py-3.5 text-right text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.column.actions')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 dark:divide-slate-700/30 virtual-list">
                {roleUsers.map((ur, index) => (
                  <tr key={`${ur.userId}-${ur.mailboxId || 0}`} className={`transition-all duration-150 ${index % 2 === 0 ? 'bg-white dark:bg-slate-800' : 'bg-gray-50/50 dark:bg-slate-800/50'} hover:bg-primary-50/50 dark:hover:bg-slate-700/50`}>
                    <td className="px-5 py-3.5">
                      <div className="flex items-center gap-3">
                        <UserAvatar userId={ur.userId} name={ur.userName} size="sm" />
                        <span className="font-medium text-gray-900 dark:text-slate-100">{ur.userName}</span>
                      </div>
                    </td>
                    <td className="px-5 py-3.5 text-sm text-gray-700 dark:text-slate-300">{ur.userEmail}</td>
                    <td className="px-5 py-3.5">
                      <span className="inline-flex items-center px-2.5 py-1 text-xs font-medium rounded-full ring-1 bg-gray-50 text-gray-600 ring-gray-100 dark:bg-slate-700/40 dark:text-slate-400 dark:ring-slate-600/50">
                        {ur.mailboxName || t('teamPermissions.roles.scope.global')}
                      </span>
                    </td>
                    <td className="px-5 py-3.5 text-sm text-gray-500 dark:text-slate-400 whitespace-nowrap">
                      {formatDateTime(ur.createdAt)}
                    </td>
                    <td className="px-5 py-3.5 text-right">
                      <button
                        onClick={() => handleRemoveAssignment(ur)}
                        className="p-2 text-gray-400 dark:text-slate-500 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg transition-all"
                        title={t('teamPermissions.roles.remove')}
                      >
                        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                        </svg>
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {roleUsers.length === 0 && (
              <div className="p-12 text-center">
                <svg className="w-12 h-12 mx-auto mb-3 text-gray-300 dark:text-slate-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z" />
                </svg>
                <p className="text-sm text-gray-500 dark:text-slate-400">{t('teamPermissions.roles.emptyMembers')}</p>
              </div>
            )}
          </div>
        </div>
      )}

      {showEditor && (
        // 使用 key 强制重新挂载，避免编辑不同角色时 useState 初始值不更新
        // 关键原因：useState 的初始值仅在首次渲染时生效，
        // 当父组件切换 editingRole 后，子组件若不重新挂载，内部的
        // name/slug/description/selectedPermissions 仍保留上一次编辑的值
        <RoleEditor
          key={editingRole ? `edit-${editingRole.id}` : 'new'}
          role={editingRole}
          permissionGroups={overview?.permissionGroups || []}
          onClose={() => {
            setShowEditor(false);
            setEditingRole(null);
          }}
          onSave={handleSave}
        />
      )}

      {/* 删除确认 */}
      <ConfirmDialog
        open={!!deleteTarget}
        title={t('teamPermissions.roles.deleteDialog.title')}
        message={t('teamPermissions.roles.deleteDialog.message', {
          name: deleteTarget ? roleLabelOf(deleteTarget, t) : '',
        })}
        confirmText={t('common.delete')}
        variant="danger"
        loading={deleting}
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
      />

      {/* 垃圾箱弹窗 */}
      <Modal
        open={trashOpen}
        title={t('teamPermissions.roles.trash.title')}
        onClose={() => setTrashOpen(false)}
        size="xl"
        footer={
          <>
            <button
              onClick={() => setClearTrashOpen(true)}
              disabled={trashList.length === 0 || clearingTrash}
              className="px-4 py-2 text-sm bg-red-600 hover:bg-red-700 disabled:bg-red-300 text-white rounded-lg transition"
            >
              {clearingTrash ? t('teamPermissions.roles.trash.clearing') : t('teamPermissions.roles.trash.clear')}
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
                placeholder={t('teamPermissions.roles.trash.searchPlaceholder')}
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
            {t('teamPermissions.roles.trash.countBefore')}<span className="font-semibold text-gray-900 dark:text-slate-100">{trashTotalItems}</span>{t('teamPermissions.roles.trash.countAfter', { count: trashTotalItems })}
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
                <p className="text-sm font-medium text-gray-900 dark:text-slate-100 mb-1">{t('teamPermissions.roles.trash.empty')}</p>
                <p className="text-sm text-gray-500 dark:text-slate-400">
                  {trashSearch ? t('teamPermissions.roles.trash.emptyNoMatch') : t('teamPermissions.roles.trash.emptyNone')}
                </p>
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-gray-100 dark:divide-slate-700/50">
                  <thead className="bg-gray-50 dark:bg-slate-900/50">
                    <tr>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.column.name')}</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.column.slug')}</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.column.type')}</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.column.permissionCount')}</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.column.memberCount')}</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.column.deletedAt')}</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('teamPermissions.roles.column.actions')}</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100 dark:divide-slate-700/30 virtual-list">
                    {trashList.map((role) => (
                      <tr key={role.id} className="hover:bg-gray-50 dark:hover:bg-slate-700/30 transition-colors">
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-2">
                            <div className="w-8 h-8 bg-gradient-to-br from-gray-400 to-slate-500 rounded-lg flex items-center justify-center text-white font-bold text-xs shrink-0">
                              {roleLabelOf(role, t).charAt(0)}
                            </div>
                            <div className="min-w-0">
                              <div className="font-medium text-gray-800 dark:text-slate-100">{roleLabelOf(role, t)}</div>
                              {role.description && <div className="text-xs text-gray-500 dark:text-slate-400 truncate max-w-xs">{role.description}</div>}
                            </div>
                          </div>
                        </td>
                        <td className="px-4 py-3 text-sm text-gray-600 dark:text-slate-300 font-mono">{role.slug}</td>
                        <td className="px-4 py-3">
                          <span className={`inline-flex items-center px-2.5 py-1 text-xs font-medium rounded-full ring-1 ${
                            role.isSystem
                              ? 'bg-purple-50 text-purple-700 ring-purple-100 dark:bg-purple-900/30 dark:text-purple-300 dark:ring-purple-800/50'
                              : 'bg-blue-50 text-blue-700 ring-blue-100 dark:bg-blue-900/30 dark:text-blue-300 dark:ring-blue-800/50'
                          }`}>
                            {role.isSystem ? t('teamPermissions.roles.badge.system') : t('teamPermissions.roles.badge.custom')}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-sm text-gray-600 dark:text-slate-300">{role.permissions.length}</td>
                        <td className="px-4 py-3 text-sm text-gray-600 dark:text-slate-300">{role.userCount}</td>
                        <td className="px-4 py-3 text-sm text-gray-500 dark:text-slate-400 whitespace-nowrap">
                          {role.deletedAt ? formatDateTime(role.deletedAt) : '-'}
                        </td>
                        <td className="px-4 py-3 text-sm">
                          <div className="flex items-center gap-1">
                            <button
                              onClick={() => setRestoreTarget(role)}
                              className="p-1.5 text-gray-400 hover:text-green-600 hover:bg-green-50 dark:hover:bg-green-900/20 rounded-md transition"
                              title={t('teamPermissions.roles.restore')}
                            >
                              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                <path strokeLinecap="round" strokeLinejoin="round" d="M3 10h10a8 8 0 018 8v2M3 10l6 6m-6-6l6-6" />
                              </svg>
                            </button>
                            <button
                              onClick={() => setHardDeleteTarget(role)}
                              className="p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-md transition"
                              title={t('teamPermissions.roles.hardDelete')}
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
                {t('teamPermissions.roles.pagination.prev')}
              </button>
              <span className="text-sm text-gray-500 dark:text-slate-400">
                {t('teamPermissions.roles.trash.pageIndicator', { page: trashPage, totalPages: trashTotalPages })}
              </span>
              <button
                onClick={() => {
                  setTrashPage((p) => Math.min(trashTotalPages, p + 1));
                  loadTrash();
                }}
                disabled={trashPage >= trashTotalPages}
                className="px-3 py-1.5 text-sm border rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition dark:text-slate-200"
              >
                {t('teamPermissions.roles.pagination.next')}
              </button>
            </div>
          )}
        </div>
      </Modal>

      {/* 恢复确认 */}
      <ConfirmDialog
        open={!!restoreTarget}
        title={t('teamPermissions.roles.restoreDialog.title')}
        message={t('teamPermissions.roles.restoreDialog.message', {
          name: restoreTarget ? roleLabelOf(restoreTarget, t) : '',
        })}
        confirmText={t('teamPermissions.roles.restore')}
        variant="primary"
        loading={restoring}
        onConfirm={handleRestore}
        onCancel={() => setRestoreTarget(null)}
      />

      {/* 彻底删除确认 */}
      <ConfirmDialog
        open={!!hardDeleteTarget}
        title={t('teamPermissions.roles.hardDeleteDialog.title')}
        message={t('teamPermissions.roles.hardDeleteDialog.message', {
          name: hardDeleteTarget ? roleLabelOf(hardDeleteTarget, t) : '',
        })}
        confirmText={t('teamPermissions.roles.hardDelete')}
        variant="danger"
        loading={hardDeleting}
        onConfirm={handleHardDelete}
        onCancel={() => setHardDeleteTarget(null)}
      />

      {/* 清空垃圾箱确认 */}
      <ConfirmDialog
        open={clearTrashOpen}
        title={t('teamPermissions.roles.trash.clearConfirmTitle')}
        message={t('teamPermissions.roles.trash.clearConfirmMessage')}
        confirmText={t('teamPermissions.roles.trash.clearConfirm')}
        variant="danger"
        loading={clearingTrash}
        onConfirm={handleClearTrash}
        onCancel={() => setClearTrashOpen(false)}
      />
    </div>
  );
}

function RoleEditor({
  role,
  permissionGroups,
  onClose,
  onSave,
}: {
  role: RoleDto | null;
  permissionGroups: PermissionGroup[];
  onClose: () => void;
  onSave: (data: CreateRoleRequest) => void;
}) {
  const { t } = useI18n();
  const [name, setName] = useState(role?.name || '');
  const [slug, setSlug] = useState(role?.slug || '');
  const [description, setDescription] = useState(role?.description || '');
  const [selectedPermissions, setSelectedPermissions] = useState<string[]>(role?.permissions || []);
  const sortOrder = role?.sortOrder || 0;
  const [error, setError] = useState('');
  const [perms, setPerms] = useState<PermissionGroup[]>(permissionGroups);

  useEffect(() => {
    if (perms.length === 0) {
      loadPermissions();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function loadPermissions() {
    try {
      const data = await roleApi.permissions();
      setPerms(data);
    } catch {
      setError(t('teamPermissions.roles.editor.loadPermissionsFailed'));
    }
  }

  function togglePermission(key: string) {
    setSelectedPermissions(prev =>
      prev.includes(key) ? prev.filter(p => p !== key) : [...prev, key],
    );
  }

  function toggleGroup(groupKey: string) {
    const group = perms.find(g => g.key === groupKey);
    if (!group) return;
    const allSelected = group.permissions.every(p => selectedPermissions.includes(p.key));
    if (allSelected) {
      setSelectedPermissions(prev =>
        prev.filter(p => !group.permissions.some(gp => gp.key === p)),
      );
    } else {
      setSelectedPermissions(prev => [
        ...prev,
        ...group.permissions.filter(p => !prev.includes(p.key)).map(p => p.key),
      ]);
    }
  }

  function handleSubmit() {
    setError('');
    if (!name.trim()) return setError(t('teamPermissions.roles.editor.nameRequired'));
    if (!role && !slug.trim()) return setError(t('teamPermissions.roles.editor.slugRequired'));

    onSave({
      name: name.trim(),
      slug: slug.trim().toLowerCase().replace(/\s+/g, '-'),
      description: description || null,
      // 系统角色权限由种子数据定义，不允许修改，提交时保留原始权限
      permissions: role?.isSystem ? (role.permissions || []) : selectedPermissions,
      sortOrder,
    });
  }

  return (
    <div
      className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4"
      onClick={onClose}
    >
      <div
        className="bg-white dark:bg-slate-800 rounded-2xl shadow-xl w-full max-w-3xl max-h-[90vh] flex flex-col"
        onClick={e => e.stopPropagation()}
      >
        <div className="px-6 py-4 border-b border-gray-100 dark:border-slate-700/50 flex items-center justify-between">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-slate-100">
            {role ? t('teamPermissions.roles.editor.titleEdit') : t('teamPermissions.roles.create')}
          </h2>
          <button onClick={onClose} className="p-1.5 text-gray-400 dark:text-slate-500 hover:text-gray-600 dark:hover:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700 rounded-lg transition-all">
            <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <div className="p-6 space-y-5 overflow-y-auto flex-1">
          {error && (
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-300 px-4 py-3 rounded-xl text-sm flex items-center gap-2">
              <svg className="w-4 h-4 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              {error}
            </div>
          )}

          {/* 基本信息 */}
          <div className="space-y-4">
            <div className="flex items-center gap-2 pb-2 border-b border-gray-100 dark:border-slate-700/50">
              <svg className="w-4 h-4 text-primary-600 dark:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
              </svg>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('teamPermissions.roles.editor.basicInfo')}</h3>
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
                  {t('teamPermissions.roles.column.name')} <span className="text-red-500">*</span>
                  {role?.isSystem && <span className="text-gray-400 dark:text-slate-500 text-xs ml-1">{t('teamPermissions.roles.editor.systemLocked')}</span>}
                </label>
                <input
                  type="text"
                  value={name}
                  onChange={e => setName(e.target.value)}
                  disabled={!!role?.isSystem}
                  className={`${inputClass} disabled:opacity-60 disabled:cursor-not-allowed`}
                  placeholder={t('teamPermissions.roles.editor.namePlaceholder')}
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
                  {t('teamPermissions.roles.editor.field.slug')} {role && <span className="text-gray-400 dark:text-slate-500 text-xs">{t('teamPermissions.roles.editor.systemLocked')}</span>}
                  {!role && <span className="text-red-500">*</span>}
                </label>
                <input
                  type="text"
                  value={slug}
                  onChange={e => setSlug(e.target.value)}
                  disabled={!!role?.isSystem}
                  className={`${inputClass} disabled:opacity-60 disabled:cursor-not-allowed`}
                  placeholder={t('teamPermissions.roles.editor.slugPlaceholder')}
                />
              </div>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('teamPermissions.roles.editor.field.description')}</label>
              <textarea
                value={description}
                onChange={e => setDescription(e.target.value)}
                rows={2}
                className={inputClass}
                placeholder={t('teamPermissions.roles.editor.descriptionPlaceholder')}
              />
            </div>
          </div>

          {/* 权限配置 */}
          <div className="space-y-4">
            <div className="flex items-center gap-2 pb-2 border-b border-gray-100 dark:border-slate-700/50">
              <svg className="w-4 h-4 text-primary-600 dark:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
              </svg>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('teamPermissions.roles.editor.permissions')}</h3>
            </div>
            {role?.isSystem ? (
              <div className="space-y-3">
                <div className="flex items-center gap-2 px-4 py-3 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 rounded-xl text-sm text-amber-700 dark:text-amber-300">
                  <svg className="w-4 h-4 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                  </svg>
                  {t('teamPermissions.roles.editor.systemPermissionsLocked')}
                </div>
                <div className="flex items-center justify-between mb-2">
                  <span className="text-sm text-gray-500 dark:text-slate-400">{t('teamPermissions.roles.editor.currentPermissionsReadOnly')}</span>
                  <span className="text-xs text-primary-600 dark:text-primary-400 font-medium bg-primary-50 dark:bg-primary-900/20 px-2.5 py-1 rounded-full">
                    {t('teamPermissions.roles.count.permissionItems', { count: selectedPermissions.length })}
                  </span>
                </div>
                <div className="space-y-3 max-h-96 overflow-y-auto pr-2">
                  {perms.map(group => {
                    const groupAllSelected = group.permissions.every(p =>
                      selectedPermissions.includes(p.key),
                    );
                    const groupSomeSelected = group.permissions.some(p =>
                      selectedPermissions.includes(p.key),
                    );
                    return (
                      <div
                        key={group.key}
                        className="border border-gray-200 dark:border-slate-700 rounded-xl overflow-hidden opacity-75"
                      >
                        <div className="px-4 py-2.5 bg-gray-50/70 dark:bg-slate-900/40 flex items-center justify-between">
                          <div className="flex items-center gap-2">
                            <input
                              type="checkbox"
                              checked={groupAllSelected}
                              disabled
                              ref={el => {
                                if (el) el.indeterminate = groupSomeSelected && !groupAllSelected;
                              }}
                              className="w-4 h-4 text-gray-400 rounded cursor-not-allowed"
                            />
                            <span className="font-medium text-gray-900 dark:text-slate-100">{groupLabelOf(group, t)}</span>
                            <span className="text-xs text-gray-500 dark:text-slate-400">
                              {t('teamPermissions.roles.count.groupPermissionItems', { count: group.permissions.length })}
                            </span>
                          </div>
                        </div>
                        <div className="px-4 py-3 grid grid-cols-1 sm:grid-cols-2 gap-2">
                          {group.permissions.map(p => {
                            const description = permissionDescriptionOf(p, t);
                            return (
                              <div
                                key={p.key}
                                className="flex items-start gap-2 p-2 rounded-lg"
                              >
                                <input
                                  type="checkbox"
                                  checked={selectedPermissions.includes(p.key)}
                                  disabled
                                  className="mt-0.5 w-4 h-4 text-gray-400 rounded cursor-not-allowed"
                                />
                                <div>
                                  <div className="text-sm text-gray-900 dark:text-slate-100">{permissionLabelOf(p, t)}</div>
                                  {description && (
                                    <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{description}</div>
                                  )}
                                </div>
                              </div>
                            );
                          })}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            ) : (
              <div>
                <div className="flex items-center justify-between mb-2">
                  <span className="text-sm text-gray-500 dark:text-slate-400">{t('teamPermissions.roles.editor.selectPermissions')}</span>
                  <span className="text-xs text-primary-600 dark:text-primary-400 font-medium bg-primary-50 dark:bg-primary-900/20 px-2.5 py-1 rounded-full">
                    {t('teamPermissions.roles.editor.selectedPermissions', { count: selectedPermissions.length })}
                  </span>
                </div>
                <div className="space-y-3 max-h-96 overflow-y-auto pr-2">
                  {perms.map(group => {
                    const groupAllSelected = group.permissions.every(p =>
                      selectedPermissions.includes(p.key),
                    );
                    const groupSomeSelected = group.permissions.some(p =>
                      selectedPermissions.includes(p.key),
                    );
                    return (
                      <div
                        key={group.key}
                        className="border border-gray-200 dark:border-slate-700 rounded-xl overflow-hidden"
                      >
                        <div className="px-4 py-2.5 bg-gray-50/70 dark:bg-slate-900/40 flex items-center justify-between">
                          <label className="flex items-center gap-2 cursor-pointer">
                            <input
                              type="checkbox"
                              checked={groupAllSelected}
                              ref={el => {
                                if (el) el.indeterminate = groupSomeSelected && !groupAllSelected;
                              }}
                              onChange={() => toggleGroup(group.key)}
                              className="w-4 h-4 text-primary-600 rounded focus:ring-primary-500"
                            />
                            <span className="font-medium text-gray-900 dark:text-slate-100">{groupLabelOf(group, t)}</span>
                            <span className="text-xs text-gray-500 dark:text-slate-400">
                              {t('teamPermissions.roles.count.groupPermissionItems', { count: group.permissions.length })}
                            </span>
                          </label>
                        </div>
                        <div className="px-4 py-3 grid grid-cols-1 sm:grid-cols-2 gap-2">
                          {group.permissions.map(p => {
                            const description = permissionDescriptionOf(p, t);
                            return (
                              <label
                                key={p.key}
                                className="flex items-start gap-2 cursor-pointer p-2 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 transition-colors"
                              >
                                <input
                                  type="checkbox"
                                  checked={selectedPermissions.includes(p.key)}
                                  onChange={() => togglePermission(p.key)}
                                  className="mt-0.5 w-4 h-4 text-primary-600 rounded focus:ring-primary-500"
                                />
                                <div>
                                  <div className="text-sm text-gray-900 dark:text-slate-100">{permissionLabelOf(p, t)}</div>
                                  {description && (
                                    <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{description}</div>
                                  )}
                                </div>
                              </label>
                            );
                          })}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            )}
          </div>
        </div>

        <div className="px-6 py-4 border-t border-gray-100 dark:border-slate-700/50 flex gap-3 justify-end">
          <button
            onClick={onClose}
            className="px-4 py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition-all"
          >
            {t('common.cancel')}
          </button>
          <button
            onClick={handleSubmit}
            className="px-4 py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 text-white rounded-xl shadow-sm shadow-primary-200/50 dark:shadow-primary-900/30 transition-all"
          >
            {role ? t('common.save') : t('teamPermissions.roles.editor.submitCreate')}
          </button>
        </div>
      </div>
    </div>
  );
}
