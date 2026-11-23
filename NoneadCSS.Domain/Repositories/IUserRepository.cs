using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface IUserRepository : IRepository<User>
{
    /// <summary>
    /// 按 ID 列表批量获取用户（避免 N+1 查询）
    /// </summary>
    Task<List<User>> GetByIdsAsync(IEnumerable<int> ids);

    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByEmailAndPasswordAsync(string email, string passwordHash);
    Task<IEnumerable<User>> GetByRoleAsync(int role);
    Task<bool> EmailExistsAsync(string email);

    Task<(IEnumerable<User> Items, int Total)> GetPagedAsync(int page, int pageSize, string? keyword = null, int? status = null);

    Task<(int Total, int Active, int Disabled, int Pending)> GetStatsAsync();

    Task<IEnumerable<User>> GetDeletedAsync();
    Task<User?> GetDeletedByIdAsync(int id);
    Task HardDeleteAsync(int id);
    Task HardDeleteAllDeletedAsync();
}
