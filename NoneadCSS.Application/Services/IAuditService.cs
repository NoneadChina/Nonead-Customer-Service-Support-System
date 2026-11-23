using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IAuditService
{
    /// <summary>
    /// 记录审计日志。
    /// </summary>
    /// <param name="action">操作类型，如 login / create / update</param>
    /// <param name="entityType">实体类型</param>
    /// <param name="entityId">实体 ID</param>
    /// <param name="oldValues">变更前值</param>
    /// <param name="newValues">变更后值</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="userId">
    /// 可选：显式指定操作人用户 ID。
    /// 当请求发生时用户尚未通过身份认证（例如 Login 动作在 HttpContext.User 填充之前执行），
    /// 或在非 HTTP 请求上下文中调用，应传入此参数；否则会从当前 HttpContext.User 中推导。
    /// </param>
    /// <param name="ipAddress">可选：显式指定客户端 IP，默认从当前请求推断</param>
    /// <param name="userAgent">可选：显式指定 User-Agent，默认从当前请求推断</param>
    Task LogAsync(string action, string entityType, int? entityId = null, string? oldValues = null, string? newValues = null, CancellationToken cancellationToken = default, int? userId = null, string? ipAddress = null, string? userAgent = null);
    Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
    Task<AuditLogResponse?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页查询审计日志，支持多维过滤。
    /// </summary>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页条数</param>
    /// <param name="action">操作类型过滤（大小写不敏感）</param>
    /// <param name="entityType">实体类型过滤（大小写不敏感）</param>
    /// <param name="userId">操作用户 ID 过滤</param>
    /// <param name="entityId">实体 ID 过滤</param>
    /// <param name="ipAddress">IP 地址过滤（大小写不敏感）</param>
    /// <param name="keyword">关键字模糊搜索（Action / EntityType / IpAddress / UserAgent）</param>
    /// <param name="startDate">起始时间（UTC）</param>
    /// <param name="endDate">结束时间（UTC）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<(List<AuditLogResponse> Items, int Total)> GetLogsAsync(
        int page,
        int pageSize,
        string? action = null,
        string? entityType = null,
        int? userId = null,
        int? entityId = null,
        string? ipAddress = null,
        string? keyword = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default);

    Task<(List<AuditLogResponse> Items, int Total)> GetLogsByUserIdAsync(int userId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<AuditLogResponse> Items, int Total)> GetLogsByEntityAsync(string entityType, int entityId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// 全局统计：基于全量数据（受同一组过滤条件约束）。
    /// </summary>
    Task<AuditLogStatsResponse> GetStatsAsync(
        string? action = null,
        string? entityType = null,
        int? userId = null,
        int? entityId = null,
        string? ipAddress = null,
        string? keyword = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 导出符合条件的审计日志为 CSV 文本（已包含表头）。
    /// 调用方负责设置 Content-Type 与 Content-Disposition。
    /// </summary>
    Task<string> ExportCsvAsync(
        string? action = null,
        string? entityType = null,
        int? userId = null,
        int? entityId = null,
        string? ipAddress = null,
        string? keyword = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int maxRows = 5000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 单条日志完整性校验（HMAC 签名）。
    /// 返回 null 表示日志不存在。
    /// </summary>
    Task<AuditLogIntegrityResult?> VerifySingleAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取关联日志：同实体（EntityType + EntityId）的其他操作记录，排除自身。
    /// 用于详情页快速查看该实体的完整操作链。
    /// </summary>
    Task<List<AuditLogResponse>> GetRelatedByEntityAsync(
        int excludeLogId,
        string entityType,
        int entityId,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取关联日志：同用户在指定时间窗口内的其他操作记录，排除自身。
    /// </summary>
    Task<List<AuditLogResponse>> GetRelatedByUserAsync(
        int excludeLogId,
        int userId,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量校验审计日志的 HMAC 签名，检测被篡改的记录。
    /// </summary>
    Task<AuditIntegrityReport> VerifyIntegrityAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}

/// <summary>
/// 审计日志完整性校验报告
/// </summary>
public class AuditIntegrityReport
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalLogs { get; set; }
    public int CheckedCount { get; set; }
    public int TamperedCount { get; set; }
    public List<int> TamperedLogIds { get; set; } = new();
}

/// <summary>
/// 单条审计日志的完整性校验结果。
/// </summary>
public class AuditLogIntegrityResult
{
    public int Id { get; set; }
    /// <summary>true 表示签名匹配（未被篡改）；false 表示签名缺失或不匹配（疑似篡改）</summary>
    public bool IsValid { get; set; }
    /// <summary>当前存储的签名（Base64），可能为空</summary>
    public string StoredSignature { get; set; } = string.Empty;
    /// <summary>基于当前数据重新计算的签名（Base64）</summary>
    public string ExpectedSignature { get; set; } = string.Empty;
    /// <summary>校验时刻（UTC ISO 8601）</summary>
    public DateTime VerifiedAt { get; set; }
}
