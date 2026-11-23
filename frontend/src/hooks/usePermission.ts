import { useAuth } from '../context/AuthContext';
import {
  UserRole,
  Permission,
  isAdmin,
  isSupervisor,
  isAgent,
  isReadOnly,
  canEditConversations,
  canDeleteConversations,
  canAssignConversations,
  canReplyConversations,
  canCreateConversations,
  canManageUsers,
  canManageMailboxes,
  canManageSettings,
  canExportData,
  canViewAllConversations,
  canAccessGroup,
  canAccessItem,
  getRoleLabel,
  validateRole,
  validateStringPermissions,
  validateNumericPermissions,
} from '../utils/permissions';

export function usePermission() {
  const { user, loading } = useAuth();

  // 未登录或加载中时，使用最低权限（只读用户，无任何权限）
  // 防止在加载过程中权限数据未就绪时误判
  const safeRole = validateRole(user?.role ?? UserRole.ReadOnly);
  const safePermissions = validateNumericPermissions(user?.permissions);
  const safeStringPermissions = validateStringPermissions(user?.stringPermissions);

  return {
    role: safeRole,
    permissions: safePermissions,
    stringPermissions: safeStringPermissions,
    loading,
    isAdmin: isAdmin(safeRole),
    isSupervisor: isSupervisor(safeRole),
    isAgent: isAgent(safeRole),
    isReadOnly: isReadOnly(safeRole),
    canEditConversations: canEditConversations(safeRole, safeStringPermissions),
    canDeleteConversations: canDeleteConversations(safeRole, safeStringPermissions),
    canAssignConversations: canAssignConversations(safeRole, safeStringPermissions),
    canReplyConversations: canReplyConversations(safeRole, safeStringPermissions),
    canCreateConversations: canCreateConversations(safeRole, safeStringPermissions),
    canManageUsers: canManageUsers(safeRole, safeStringPermissions),
    canManageMailboxes: canManageMailboxes(safeRole, safeStringPermissions),
    canManageSettings: canManageSettings(safeRole, safeStringPermissions),
    canExportData: canExportData(safeRole, safeStringPermissions),
    canViewAllConversations: canViewAllConversations(safeRole, safeStringPermissions),
    canAccessGroup: (groupKey: string) => canAccessGroup(safeRole, groupKey, safeStringPermissions),
    canAccessItem: (path: string) => canAccessItem(safeRole, path, safeStringPermissions),
    canManageCalendar:
      safeRole === UserRole.SuperAdmin ||
      safeRole === UserRole.AgentSupervisor ||
      safeRole === UserRole.Agent,
    canManageCalendarTrash: safeRole === UserRole.SuperAdmin,
    roleLabel: getRoleLabel(safeRole),
  };
}

export { UserRole, Permission };
