import client from '../client';
import type {
  RoleDto,
  RoleUserDto,
  RoleOverviewResponse,
  PermissionGroup,
  CreateRoleRequest,
  UpdateRoleRequest,
  AssignRoleRequest,
  BatchAssignRoleRequest,
  ApiKeyDto,
  ApiKeyLogDto,
  ApiKeyOverviewResponse,
  CreateApiKeyRequest,
  UpdateApiKeyRequest,
  ApiKeyQuery,
  ApiKeyLogQuery,
  TeamDto,
  TeamMemberDto,
  TeamDetailDto,
  TeamOverviewResponse,
  CreateTeamRequest,
  UpdateTeamRequest,
  AddTeamMemberRequest,
  TeamQuery,
  EntityTypeResponse,
  ImportPreviewResponse,
  ImportResponse,
} from '../types';

// ====== 角色与权限 API ======
export const roleApi = {
  list: (params?: { page?: number; pageSize?: number; keyword?: string }) =>
    client
      .get<{ data: RoleDto[]; totalItems: number; totalPages: number }>('/api/roles', { params })
      .then((r) => r.data),
  get: (id: number) =>
    client.get<RoleDto>(`/api/roles/${id}`).then((r) => r.data),
  overview: () =>
    client.get<RoleOverviewResponse>('/api/roles/overview').then((r) => r.data),
  permissions: () =>
    client.get<PermissionGroup[]>('/api/roles/permissions').then((r) => r.data),
  create: (data: CreateRoleRequest) =>
    client.post<RoleDto>('/api/roles', data).then((r) => r.data),
  update: (id: number, data: UpdateRoleRequest) =>
    client.put<RoleDto>(`/api/roles/${id}`, data).then((r) => r.data),
  remove: (id: number) =>
    client.delete<boolean>(`/api/roles/${id}`).then((r) => r.data),
  assign: (data: AssignRoleRequest) =>
    client.post<boolean>('/api/roles/assign', data).then((r) => r.data),
  batchAssign: (data: BatchAssignRoleRequest) =>
    client.post<boolean>('/api/roles/batch-assign', data).then((r) => r.data),
  removeAssignment: (userRoleId: number) =>
    client.delete<boolean>(`/api/roles/assignments/${userRoleId}`).then((r) => r.data),
  roleUsers: (id: number) =>
    client.get<RoleUserDto[]>(`/api/roles/${id}/users`).then((r) => r.data),
  userRoles: (userId: number, mailboxId?: number | null) =>
    client.get<RoleDto[]>(`/api/roles/user/${userId}`, { params: { mailboxId } }).then((r) => r.data),
  userPermissions: (userId: number, mailboxId?: number | null) =>
    client.get<string[]>(`/api/roles/user/${userId}/permissions`, { params: { mailboxId } }).then((r) => r.data),
  hasPermission: (userId: number, permission: string, mailboxId?: number | null) =>
    client.get<boolean>(`/api/roles/user/${userId}/has-permission`, { params: { permission, mailboxId } }).then((r) => r.data),
  getTrash: (page = 1, pageSize = 20, keyword = '') =>
    client
      .get<{ data: RoleDto[]; totalItems: number; totalPages: number }>('/api/roles/trash', {
        params: { page, pageSize, keyword },
      })
      .then((r) => r.data),
  getTrashCount: () => client.get<{ count: number }>('/api/roles/trash/count').then((r) => r.data),
  restore: (id: number) => client.post<boolean>(`/api/roles/${id}/restore`).then((r) => r.data),
  hardDelete: (id: number) => client.delete<boolean>(`/api/roles/${id}/hard`).then((r) => r.data),
  clearTrash: () => client.delete<{ count: number }>('/api/roles/trash').then((r) => r.data),
};

// ====== 团队 API ======
export const teamApi = {
  list: (params?: TeamQuery) =>
    client
      .get<{ data: TeamDto[]; totalItems: number; totalPages: number }>('/api/teams', { params })
      .then((r) => r.data),
  get: (id: number) =>
    client.get<TeamDetailDto>(`/api/teams/${id}`).then((r) => r.data),
  overview: () =>
    client.get<TeamOverviewResponse>('/api/teams/overview').then((r) => r.data),
  create: (data: CreateTeamRequest) =>
    client.post<TeamDto>('/api/teams', data).then((r) => r.data),
  update: (id: number, data: UpdateTeamRequest) =>
    client.put<TeamDto>(`/api/teams/${id}`, data).then((r) => r.data),
  remove: (id: number) =>
    client.delete<boolean>(`/api/teams/${id}`).then((r) => r.data),
  listByMailbox: (mailboxId?: number) =>
    client
      .get<TeamDto[]>(`/api/teams/mailbox/${mailboxId ?? ''}`)
      .then((r) => r.data ?? []),
  listByUser: (userId: number) =>
    client.get<TeamDto[]>(`/api/teams/user/${userId}`).then((r) => r.data ?? []),
  getMembers: (id: number) =>
    client.get<TeamMemberDto[]>(`/api/teams/${id}/members`).then((r) => r.data ?? []),
  addMember: (id: number, data: AddTeamMemberRequest) =>
    client.post<TeamMemberDto>(`/api/teams/${id}/members`, data).then((r) => r.data),
  updateMemberRole: (id: number, userId: number, role: number) =>
    client.put<TeamMemberDto>(`/api/teams/${id}/members/${userId}`, { role }).then((r) => r.data),
  removeMember: (id: number, userId: number) =>
    client.delete<boolean>(`/api/teams/${id}/members/${userId}`).then((r) => r.data),
  getTrash: (page = 1, pageSize = 20, keyword = '') =>
    client
      .get<{ data: TeamDto[]; totalItems: number; totalPages: number }>('/api/teams/trash', {
        params: { page, pageSize, keyword },
      })
      .then((r) => r.data),
  getTrashCount: () => client.get<{ count: number }>('/api/teams/trash/count').then((r) => r.data),
  restore: (id: number) => client.post<boolean>(`/api/teams/${id}/restore`).then((r) => r.data),
  hardDelete: (id: number) => client.delete<boolean>(`/api/teams/${id}/hard`).then((r) => r.data),
  clearTrash: () => client.delete<{ count: number }>('/api/teams/trash').then((r) => r.data),
};

// ====== API 密钥 API ======
export const apiKeyApi = {
  list: (params?: ApiKeyQuery) =>
    client
      .get<{ data: ApiKeyDto[]; totalItems: number; currentPage: number; pageSize: number; totalPages: number }>('/api/apikeys', { params })
      .then((r) => r.data),
  get: (id: number) =>
    client.get<ApiKeyDto>(`/api/apikeys/${id}`).then((r) => r.data),
  overview: () =>
    client.get<ApiKeyOverviewResponse>('/api/apikeys/overview').then((r) => r.data),
  create: (data: CreateApiKeyRequest) =>
    client.post<ApiKeyDto>('/api/apikeys', data).then((r) => r.data),
  update: (id: number, data: UpdateApiKeyRequest) =>
    client.put<ApiKeyDto>(`/api/apikeys/${id}`, data).then((r) => r.data),
  remove: (id: number) =>
    client.delete<boolean>(`/api/apikeys/${id}`).then((r) => r.data),
  toggleActive: (id: number) =>
    client.post<ApiKeyDto>(`/api/apikeys/${id}/toggle`).then((r) => r.data),
  regenerate: (id: number) =>
    client.post<ApiKeyDto>(`/api/apikeys/${id}/regenerate`).then((r) => r.data),
  logs: (id: number, pageSize: number = 50) =>
    client.get<ApiKeyLogDto[]>(`/api/apikeys/${id}/logs`, { params: { pageSize } }).then((r) => r.data ?? []),
  allLogs: (params?: ApiKeyLogQuery) =>
    client
      .get<{ data: ApiKeyLogDto[]; totalItems: number; currentPage: number; pageSize: number; totalPages: number }>('/api/apikeys/logs', { params })
      .then((r) => r.data),
  getTrash: (page = 1, pageSize = 10, keyword = '') =>
    client
      .get<{ data: ApiKeyDto[]; totalItems: number; totalPages: number }>('/api/apikeys/trash', {
        params: { page, pageSize, keyword },
      })
      .then((r) => r.data),
  restore: (id: number) => client.post<boolean>(`/api/apikeys/${id}/restore`).then((r) => r.data),
  hardDelete: (id: number) => client.delete<boolean>(`/api/apikeys/${id}/hard`).then((r) => r.data),
  clearTrash: () => client.delete<{ count: number }>('/api/apikeys/trash').then((r) => r.data),
};

// ====== 导入导出 API ======
export const importExportApi = {
  getEntities: () =>
    client.get<EntityTypeResponse[]>('/api/importexport/entities').then((r) => r.data),

  exportData: (entityTypes: string[]) =>
    client.post<Blob>('/api/importexport/export', { entityTypes }, { responseType: 'blob' }).then((r) => r.data),

  previewImport: (data: string) =>
    client.post<ImportPreviewResponse>('/api/importexport/import/preview', { data }).then((r) => r.data),

  importData: (data: string) =>
    client.post<ImportResponse>('/api/importexport/import', { data }).then((r) => r.data),
};
