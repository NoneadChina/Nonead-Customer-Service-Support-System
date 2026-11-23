// ====== 角色与权限相关 ======
export interface RoleDto {
  id: number;
  name: string;
  slug: string;
  description: string | null;
  permissions: string[];
  isSystem: boolean;
  sortOrder: number;
  userCount: number;
  createdAt: string;
  updatedAt: string;
  deletedAt: string | null;
}

export interface RoleUserDto {
  userId: number;
  userName: string;
  userEmail: string;
  roleId: number;
  roleName: string;
  mailboxId: number | null;
  mailboxName: string | null;
  createdAt: string;
}

export interface PermissionGroup {
  key: string;
  label: string;
  permissions: PermissionItem[];
}

export interface PermissionItem {
  key: string;
  label: string;
  description: string | null;
}

export interface RoleOverviewResponse {
  totalRoles: number;
  systemRoles: number;
  customRoles: number;
  totalRoleAssignments: number;
  roles: RoleDto[];
  usersPerRole: Record<string, number>;
  permissionGroups: PermissionGroup[];
}

export interface CreateRoleRequest {
  name: string;
  slug: string;
  description: string | null;
  permissions: string[];
  sortOrder: number;
}

export interface UpdateRoleRequest {
  id: number;
  name: string;
  description: string | null;
  permissions: string[];
  sortOrder: number;
}

export interface AssignRoleRequest {
  userId: number;
  roleId: number;
  mailboxId: number | null;
}

export interface BatchAssignRoleRequest {
  roleId: number;
  userIds: number[];
  mailboxId: number | null;
}

// ====== 团队管理 ======
export interface TeamDto {
  id: number;
  name: string;
  description: string | null;
  mailboxId: number | null;
  mailboxName: string | null;
  isActive: boolean;
  sortOrder: number;
  memberCount: number;
  leaderCount: number;
  createdAt: string;
  updatedAt: string;
  deletedAt: string | null;
}

export interface TeamMemberDto {
  id: number;
  teamId: number;
  teamName: string;
  userId: number;
  userName: string;
  userEmail: string;
  role: number;
  roleName: string;
  joinedAt: string;
}

export interface TeamDetailDto {
  id: number;
  name: string;
  description: string | null;
  mailboxId: number | null;
  mailboxName: string | null;
  isActive: boolean;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
  members: TeamMemberDto[];
}

export interface TeamOverviewResponse {
  totalTeams: number;
  activeTeams: number;
  inactiveTeams: number;
  totalMembers: number;
  teamsPerMailbox: number;
  topTeams: TeamDto[];
}

export interface CreateTeamRequest {
  name: string;
  description: string | null;
  mailboxId: number | null;
  isActive: boolean;
  sortOrder: number;
}

export interface UpdateTeamRequest {
  name: string;
  description: string | null;
  mailboxId: number | null;
  isActive: boolean;
  sortOrder: number;
}

export interface AddTeamMemberRequest {
  userId: number;
  role: number;
}

export interface UpdateTeamMemberRequest {
  role: number;
}

export interface TeamQuery {
  page?: number;
  pageSize?: number;
  mailboxId?: number | null;
  isActive?: boolean | null;
  keyword?: string | null;
}

// ====== 导入导出 ======
export interface EntityTypeResponse {
  type: string;
  name: string;
  description: string;
  supportsExport: boolean;
  supportsImport: boolean;
}

export interface ImportEntityPreview {
  type: string;
  count: number;
  sampleData: string[];
}

export interface ImportPreviewResponse {
  valid: boolean;
  entities: ImportEntityPreview[];
  errors: string[];
}

export interface ImportResponse {
  success: boolean;
  totalCount: number;
  importedCount: number;
  failedCount: number;
  errors: string[];
}
