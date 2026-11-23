using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface ICustomReportRepository : IRepository<CustomReport>
{
    Task<(List<CustomReport> Items, int TotalCount)> GetPagedAsync(
        int userId,
        bool isSuperAdmin,
        List<int> userTeamIds,
        string? search,
        string? category,
        string? visibility,
        int page,
        int pageSize);

    Task<List<CustomReport>> GetDashboardPinnedAsync(
        int userId,
        bool isSuperAdmin,
        List<int> userTeamIds,
        int limit);

    Task<bool> ExistsByNameAndOwnerAsync(string name, int ownerId, int? excludeId = null);

    Task<CustomReport?> GetByIdIgnoreQueryFilterAsync(int id);
    Task<(List<CustomReport> Items, int TotalCount)> GetTrashPagedAsync(
        int userId,
        bool isSuperAdmin,
        List<int> userTeamIds,
        string? search,
        int page,
        int pageSize);
    Task HardDeleteAsync(CustomReport entity);
    Task<int> ClearTrashAsync(int userId, bool isSuperAdmin, List<int> userTeamIds);
}
