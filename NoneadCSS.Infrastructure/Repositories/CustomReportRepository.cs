using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class CustomReportRepository : Repository<CustomReport>, ICustomReportRepository
{
    public CustomReportRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    public override async Task<CustomReport?> GetByIdTrackedAsync(int id)
    {
        return await _dbSet
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<(List<CustomReport> Items, int TotalCount)> GetPagedAsync(
        int userId,
        bool isSuperAdmin,
        List<int> userTeamIds,
        string? search,
        string? category,
        string? visibility,
        int page,
        int pageSize)
    {
        var teamIds = userTeamIds.Count == 0 ? new List<int> { -1 } : userTeamIds;

        var query = _dbSet.AsNoTracking().AsQueryable();

        if (!isSuperAdmin)
        {
            query = query.Where(r =>
                r.Visibility == "public" ||
                r.OwnerId == userId ||
                (r.Visibility == "team" && r.TeamId.HasValue && teamIds.Contains(r.TeamId.Value)));
        }

        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            query = query.Where(r =>
                r.Name.ToLower().Contains(s) ||
                (r.Description != null && r.Description.ToLower().Contains(s)));
        }

        if (!string.IsNullOrEmpty(category))
        {
            query = query.Where(r => r.Category == category);
        }

        if (!string.IsNullOrEmpty(visibility))
        {
            query = query.Where(r => r.Visibility == visibility);
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(r => r.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<List<CustomReport>> GetDashboardPinnedAsync(
        int userId,
        bool isSuperAdmin,
        List<int> userTeamIds,
        int limit)
    {
        var teamIds = userTeamIds.Count == 0 ? new List<int> { -1 } : userTeamIds;

        var query = _dbSet.AsNoTracking().AsQueryable();

        query = query.Where(r => r.IsDashboardPinned);

        if (!isSuperAdmin)
        {
            query = query.Where(r =>
                r.Visibility == "public" ||
                r.OwnerId == userId ||
                (r.Visibility == "team" && r.TeamId.HasValue && teamIds.Contains(r.TeamId.Value)));
        }

        return await query
            .OrderBy(r => r.DashboardSortOrder)
            .ThenByDescending(r => r.UpdatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<bool> ExistsByNameAndOwnerAsync(string name, int ownerId, int? excludeId = null)
    {
        var query = _dbSet.AsNoTracking()
            .Where(r => r.Name == name && r.OwnerId == ownerId && r.DeletedAt == null);

        if (excludeId.HasValue)
        {
            query = query.Where(r => r.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<CustomReport?> GetByIdIgnoreQueryFilterAsync(int id)
    {
        return await _dbSet
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<(List<CustomReport> Items, int TotalCount)> GetTrashPagedAsync(
        int userId,
        bool isSuperAdmin,
        List<int> userTeamIds,
        string? search,
        int page,
        int pageSize)
    {
        var teamIds = userTeamIds.Count == 0 ? new List<int> { -1 } : userTeamIds;

        var query = _dbSet.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.DeletedAt != null);

        if (!isSuperAdmin)
        {
            query = query.Where(r => r.OwnerId == userId);
        }

        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            query = query.Where(r =>
                r.Name.ToLower().Contains(s) ||
                (r.Description != null && r.Description.ToLower().Contains(s)));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(r => r.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public Task HardDeleteAsync(CustomReport entity)
    {
        _dbSet.Remove(entity);
        return Task.CompletedTask;
    }

    public async Task<int> ClearTrashAsync(int userId, bool isSuperAdmin, List<int> userTeamIds)
    {
        var teamIds = userTeamIds.Count == 0 ? new List<int> { -1 } : userTeamIds;

        var query = _dbSet.IgnoreQueryFilters()
            .Where(r => r.DeletedAt != null);

        if (!isSuperAdmin)
        {
            query = query.Where(r => r.OwnerId == userId);
        }

        // 使用 ExecuteDeleteAsync 直接执行 DELETE SQL，绕过 ChangeTracker 软删除拦截
        return await query.ExecuteDeleteAsync();
    }
}
