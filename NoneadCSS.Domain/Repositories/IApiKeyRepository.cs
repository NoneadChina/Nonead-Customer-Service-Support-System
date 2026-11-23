using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface IApiKeyRepository : IRepository<ApiKey>
{
    Task<ApiKey?> GetByKeyAsync(string key);

    /// <summary>
    /// 原子递增 API Key 使用次数并更新最后使用时间，避免并发更新丢失。
    /// 使用 SQL 原生增量更新，比 EF Core 读改写更安全。
    /// </summary>
    Task IncrementUsageAsync(int apiKeyId, DateTime lastUsedAt, string? lastUsedIp, CancellationToken cancellationToken = default);

    Task<(IEnumerable<ApiKey> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        int? userId = null, bool? isActive = null, string? keyword = null, string? scope = null);

    Task<List<ApiKey>> GetAllWithUserAsync();
    Task<Dictionary<string, int>> GetStatsAsync();

    /// <summary>
    /// 获取所有 scope 的使用次数统计（按 scope 字符串分组）。
    /// 仅统计未软删除的密钥，避免删除数据干扰统计。
    /// 使用数据库端聚合（GROUP BY），避免将全表加载到内存。
    /// </summary>
    Task<Dictionary<string, int>> GetScopeCountsAsync();

    Task<ApiKey?> GetDeletedByIdAsync(int id);
    Task<(IEnumerable<ApiKey> Items, int Total)> GetDeletedPagedAsync(int page, int pageSize, string? keyword = null);
    Task<int> CountDeletedAsync();
    Task RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> HardDeleteAllDeletedAsync();
}

public interface IApiKeyLogRepository : IRepository<ApiKeyLog>
{
    Task<List<ApiKeyLog>> GetByApiKeyAsync(int apiKeyId, int limit = 50);

    Task<(IEnumerable<ApiKeyLog> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        int? apiKeyId = null, string? endpoint = null, int? statusCode = null);

    Task<Dictionary<string, int>> GetStatsAsync(DateTime? start = null, DateTime? end = null);
}
