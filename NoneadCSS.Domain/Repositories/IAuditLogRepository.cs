using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface IAuditLogRepository : IRepository<AuditLog>
{
    Task<(IEnumerable<AuditLog> Items, int Total)> GetByUserIdAsync(int userId, int page, int pageSize);
    Task<(IEnumerable<AuditLog> Items, int Total)> GetByEntityAsync(string entityType, int entityId, int page, int pageSize);

    /// <summary>
    /// 分页查询审计日志，支持多维过滤。
    /// 所有过滤参数为可选；字符串字段使用大小写不敏感匹配。
    /// </summary>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页条数</param>
    /// <param name="action">操作类型过滤（大小写不敏感，精确匹配）</param>
    /// <param name="entityType">实体类型过滤（大小写不敏感，精确匹配）</param>
    /// <param name="userId">操作用户 ID 过滤</param>
    /// <param name="entityId">实体 ID 过滤（需配合 entityType 使用更佳）</param>
    /// <param name="ipAddress">IP 地址过滤（大小写不敏感，精确匹配）</param>
    /// <param name="keyword">关键字搜索（在 Action / EntityType / IpAddress / UserAgent 中模糊匹配）</param>
    /// <param name="startDate">起始时间（UTC，含等于）</param>
    /// <param name="endDate">结束时间（UTC，含等于）</param>
    Task<(IEnumerable<AuditLog> Items, int Total)> GetPagedAsync(
        int page,
        int pageSize,
        string? action = null,
        string? entityType = null,
        int? userId = null,
        int? entityId = null,
        string? ipAddress = null,
        string? keyword = null,
        DateTime? startDate = null,
        DateTime? endDate = null);

    /// <summary>
    /// 全局统计：按操作类型、实体类型聚合，以及总数、活跃用户数、异常事件数。
    /// 统计基于全量数据（受同一组过滤条件约束），而非当前页。
    /// </summary>
    Task<AuditLogStats> GetStatsAsync(
        string? action = null,
        string? entityType = null,
        int? userId = null,
        int? entityId = null,
        string? ipAddress = null,
        string? keyword = null,
        DateTime? startDate = null,
        DateTime? endDate = null);

    /// <summary>
    /// 导出符合条件的审计日志（不分页，按上限截断）。
    /// 调用方应限制 maxRows 防止 OOM。
    /// </summary>
    Task<List<AuditLog>> ExportAsync(
        string? action = null,
        string? entityType = null,
        int? userId = null,
        int? entityId = null,
        string? ipAddress = null,
        string? keyword = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int maxRows = 5000);
}

/// <summary>
/// 审计日志全局统计聚合。
/// </summary>
public class AuditLogStats
{
    /// <summary>符合过滤条件的总记录数</summary>
    public int TotalLogs { get; set; }

    /// <summary>符合过滤条件的独立用户数（UserId 去重，排除系统空用户）</summary>
    public int UniqueUsers { get; set; }

    /// <summary>近 24 小时（相对于查询时刻）的新增记录数</summary>
    public int Last24hCount { get; set; }

    /// <summary>异常事件数（loginfailed / lockout / resetfailed）</summary>
    public int FailedActions { get; set; }

    /// <summary>操作类型分布（Top N，按计数降序）</summary>
    public List<(string Action, int Count)> ActionDistribution { get; set; } = new();

    /// <summary>实体类型分布（Top N，按计数降序）</summary>
    public List<(string EntityType, int Count)> EntityDistribution { get; set; } = new();
}
