namespace NoneadCSS.Application.Configuration;

/// <summary>
/// 响应输出缓存策略配置选项
/// </summary>
public class ResponseCachingOptions
{
    /// <summary>管理端列表页缓存过期时间（分钟），适用于 Roles/Tags/CustomFields/Teams/Webhooks/Modules/Sla/CannedResponses</summary>
    public int ManagementListCacheMinutes { get; set; } = 5;

    /// <summary>系统选项缓存过期时间（分钟），数据较稳定</summary>
    public int SystemOptionsCacheMinutes { get; set; } = 10;

    /// <summary>审计日志缓存过期时间（分钟），数据频繁增长，使用较短过期时间</summary>
    public int AuditLogCacheMinutes { get; set; } = 1;
}
