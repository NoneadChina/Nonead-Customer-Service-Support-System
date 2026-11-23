using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface IRoleRepository : IRepository<Role>
{
    Task<Role?> GetBySlugAsync(string slug);
    Task<(IEnumerable<Role> Items, int Total)> GetPagedAsync(int page, int pageSize, string? keyword = null);
    new Task<List<Role>> GetAllAsync();
    Task<List<Role>> GetByIdsAsync(IEnumerable<int> ids);
    Task<List<Role>> GetByUserIdAsync(int userId, int? mailboxId = null);
    Task<Dictionary<string, int>> GetStatsAsync();
    Task<bool> UserHasPermissionAsync(int userId, string permission, int? mailboxId = null);
    Task<List<string>> GetUserPermissionsAsync(int userId, int? mailboxId = null);
    Task<(IEnumerable<Role> Items, int Total)> GetDeletedAsync(int page, int pageSize, string? keyword = null);
    Task<int> CountDeletedAsync();
    Task<bool> RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> HardDeleteAllDeletedAsync();
}

public interface IUserRoleRepository : IRepository<UserRole>
{
    Task<List<UserRole>> GetByUserIdAsync(int userId);
    Task<List<UserRole>> GetByRoleIdAsync(int roleId);
    Task<UserRole?> GetByUserAndRoleAsync(int userId, int roleId, int? mailboxId = null);
    Task DeleteByUserIdAsync(int userId);
    Task DeleteByRoleIdAsync(int roleId);
}
