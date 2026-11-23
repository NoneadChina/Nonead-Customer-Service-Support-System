using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IRoleService
{
    Task<RoleDto?> GetByIdAsync(int id);
    Task<(List<RoleDto> Items, int Total)> GetPagedAsync(RoleQuery query);
    Task<RoleOverviewResponse> GetOverviewAsync();
    Task<RoleDto> CreateAsync(CreateRoleRequest request);
    Task<RoleDto?> UpdateAsync(UpdateRoleRequest request);
    Task<bool> DeleteAsync(int id);
    Task<bool> AssignRoleAsync(AssignRoleRequest request);
    Task<bool> BatchAssignRoleAsync(BatchAssignRoleRequest request);
    Task<bool> RemoveRoleAssignmentAsync(int userRoleId);
    Task<bool> UserHasPermissionAsync(int userId, string permission, int? mailboxId = null);
    Task<List<string>> GetUserPermissionsAsync(int userId, int? mailboxId = null);
    Task<List<RoleDto>> GetUserRolesAsync(int userId, int? mailboxId = null);
    Task<List<RoleUserDto>> GetRoleUsersAsync(int roleId);
    Task<(IEnumerable<RoleDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword = null);
    Task<int> GetTrashCountAsync();
    Task<bool> RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> ClearTrashAsync();
    List<PermissionGroup> GetAllPermissionGroups();
}
