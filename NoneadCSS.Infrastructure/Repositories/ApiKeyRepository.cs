using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class ApiKeyRepository : Repository<ApiKey>, IApiKeyRepository
{
    public ApiKeyRepository(NoneadCSSDbContext context) : base(context) { }

    /// <summary>
    /// 应用标准的 Include 链，包含关联的用户信息。
    /// </summary>
    private IQueryable<ApiKey> ApplyIncludes(IQueryable<ApiKey> query)
    {
        return query.Include(k => k.User);
    }

    /// <summary>
    /// 按 ID 查询 API Key（AsNoTracking，包含关联的用户信息）。
    /// </summary>
    public override async Task<ApiKey?> GetByIdAsync(int id)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .FirstOrDefaultAsync(k => k.Id == id);
    }

    /// <summary>
    /// 按 Key 哈希值查询。传入参数应为 SHA-256 哈希后的字符串（见 <see cref="NoneadCSS.Application.Security.SecretHasher"/>）。
    /// </summary>
    public async Task<ApiKey?> GetByKeyAsync(string key)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .FirstOrDefaultAsync(k => k.Key == key && k.IsActive);
    }

    /// <summary>
    /// 原子递增 API Key 使用次数并更新最后使用时间/IP。
    /// 使用 EF Core 的 ExecuteUpdateAsync（翻译为 UPDATE ... SET UsageCount = UsageCount + 1），
    /// 避免并发场景下的计数丢失；同时避免在认证热路径上加载完整实体到跟踪器。
    /// </summary>
    public async Task IncrementUsageAsync(int apiKeyId, DateTime lastUsedAt, string? lastUsedIp, CancellationToken cancellationToken = default)
    {
        await _dbSet
            .Where(k => k.Id == apiKeyId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(k => k.LastUsedAt, lastUsedAt)
                .SetProperty(k => k.LastUsedIp, lastUsedIp)
                .SetProperty(k => k.UsageCount, k => k.UsageCount + 1), cancellationToken);
    }

    public async Task<(IEnumerable<ApiKey> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        int? userId = null, bool? isActive = null, string? keyword = null, string? scope = null)
    {
        var query = ApplyIncludes(_dbSet.AsNoTracking()).AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(k => k.UserId == userId.Value);
        }
        if (isActive.HasValue)
        {
            query = query.Where(k => k.IsActive == isActive.Value);
        }
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(k =>
                k.Name.Contains(keyword) ||
                k.Description!.Contains(keyword));
        }
        if (!string.IsNullOrEmpty(scope))
        {
            query = query.Where(k => k.Scopes.Contains(scope));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(k => k.IsActive)
            .ThenByDescending(k => k.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<List<ApiKey>> GetAllWithUserAsync()
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .OrderByDescending(k => k.Id)
            .ToListAsync();
    }

    public async Task<Dictionary<string, int>> GetStatsAsync()
    {
        var total = await _dbSet.CountAsync(k => k.DeletedAt == null);
        var active = await _dbSet.CountAsync(k => k.DeletedAt == null && k.IsActive);
        var totalUsage = await _dbSet.Where(k => k.DeletedAt == null).SumAsync(k => (int?)k.UsageCount) ?? 0;

        return new Dictionary<string, int>
        {
            { "total", total },
            { "active", active },
            { "inactive", total - active },
            { "totalUsage", totalUsage },
        };
    }

    /// <summary>
    /// 在内存中按 scope 字符串聚合统计（SQLite 不支持 STRING_SPLIT/STRING_SPLIT_ROWS 等表值函数，
    /// 无法在数据库端按逗号分隔的字符串拆分聚合；使用单次全表扫描并在内存中拆分的方案，
    /// 避免 N+1（无需为每个 scope 单独查询）。
    /// </summary>
    public async Task<Dictionary<string, int>> GetScopeCountsAsync()
    {
        // 仅查询非空 Scopes 列，限制字段传输量
        var allScopesRaw = await _dbSet
            .Where(k => k.DeletedAt == null && k.Scopes != null && k.Scopes != string.Empty)
            .Select(k => k.Scopes!)
            .ToListAsync();

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var scopeStr in allScopesRaw)
        {
            foreach (var scope in scopeStr.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var s = scope.Trim();
                if (!string.IsNullOrEmpty(s))
                {
                    counts[s] = counts.GetValueOrDefault(s) + 1;
                }
            }
        }
        return counts;
    }

    public async Task<ApiKey?> GetDeletedByIdAsync(int id)
    {
        return await ApplyIncludes(_dbSet.IgnoreQueryFilters())
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.Id == id && k.DeletedAt != null);
    }

    public async Task<(IEnumerable<ApiKey> Items, int Total)> GetDeletedPagedAsync(int page, int pageSize, string? keyword = null)
    {
        var query = ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking())
            .Where(k => k.DeletedAt != null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(k =>
                k.Name.Contains(keyword) ||
                (k.Description != null && k.Description.Contains(keyword)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(k => k.DeletedAt)
            .ThenByDescending(k => k.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> CountDeletedAsync()
    {
        return await _dbSet.IgnoreQueryFilters().CountAsync(k => k.DeletedAt != null);
    }

    public async Task RestoreAsync(int id)
    {
        await _dbSet.IgnoreQueryFilters()
            .Where(k => k.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(k => k.DeletedAt, (DateTime?)null)
                .SetProperty(k => k.UpdatedAt, DateTime.UtcNow));
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        var exists = await _dbSet.IgnoreQueryFilters().AnyAsync(k => k.Id == id && k.DeletedAt != null);
        if (!exists) return false;
        await _context.ApiKeyLogs.IgnoreQueryFilters()
            .Where(l => l.ApiKeyId == id)
            .ExecuteDeleteAsync();
        await _dbSet.IgnoreQueryFilters()
            .Where(k => k.Id == id)
            .ExecuteDeleteAsync();
        return true;
    }

    public async Task<int> HardDeleteAllDeletedAsync()
    {
        var deletedIds = await _dbSet.IgnoreQueryFilters()
            .Where(k => k.DeletedAt != null)
            .Select(k => k.Id)
            .ToListAsync();

        if (deletedIds.Count > 0)
        {
            await _context.ApiKeyLogs.IgnoreQueryFilters()
                .Where(l => deletedIds.Contains(l.ApiKeyId))
                .ExecuteDeleteAsync();
        }

        return await _dbSet.IgnoreQueryFilters()
            .Where(k => k.DeletedAt != null)
            .ExecuteDeleteAsync();
    }
}

public class ApiKeyLogRepository : Repository<ApiKeyLog>, IApiKeyLogRepository
{
    public ApiKeyLogRepository(NoneadCSSDbContext context) : base(context) { }

    public async Task<List<ApiKeyLog>> GetByApiKeyAsync(int apiKeyId, int limit = 50)
    {
        return await _dbSet
            .Where(l => l.ApiKeyId == apiKeyId)
            .OrderByDescending(l => l.Id)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<(IEnumerable<ApiKeyLog> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        int? apiKeyId = null, string? endpoint = null, int? statusCode = null)
    {
        var query = _dbSet.AsQueryable();

        if (apiKeyId.HasValue)
        {
            query = query.Where(l => l.ApiKeyId == apiKeyId.Value);
        }
        if (!string.IsNullOrEmpty(endpoint))
        {
            query = query.Where(l => l.Endpoint.Contains(endpoint));
        }
        if (statusCode.HasValue)
        {
            query = query.Where(l => l.StatusCode == statusCode.Value);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<Dictionary<string, int>> GetStatsAsync(DateTime? start = null, DateTime? end = null)
    {
        var query = _dbSet.AsQueryable();

        if (start.HasValue)
        {
            query = query.Where(l => l.CreatedAt >= start.Value);
        }
        if (end.HasValue)
        {
            query = query.Where(l => l.CreatedAt <= end.Value);
        }

        var total = await query.CountAsync();
        var success = await query.CountAsync(l => l.StatusCode >= 200 && l.StatusCode < 300);
        var fail = total - success;

        return new Dictionary<string, int>
        {
            { "total", total },
            { "success", success },
            { "fail", fail },
        };
    }
}
