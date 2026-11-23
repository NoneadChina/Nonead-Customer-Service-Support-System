using System.Globalization;
using System.Text;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace NoneadCSS.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuditHmacSigner _hmacSigner;
    private readonly IServiceScopeFactory _scopeFactory;

    public AuditService(IUnitOfWork unitOfWork, IHttpContextAccessor httpContextAccessor, AuditHmacSigner hmacSigner, IServiceScopeFactory scopeFactory)
    {
        _unitOfWork = unitOfWork;
        _httpContextAccessor = httpContextAccessor;
        _hmacSigner = hmacSigner;
        _scopeFactory = scopeFactory;
    }

    public async Task LogAsync(string action, string entityType, int? entityId = null, string? oldValues = null, string? newValues = null, CancellationToken cancellationToken = default, int? userId = null, string? ipAddress = null, string? userAgent = null)
    {
        var context = _httpContextAccessor.HttpContext;
        var claimUserId = context?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var finalUserId = userId ?? TryParseUserId(claimUserId);
        var finalIpAddress = ipAddress ?? context?.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var finalUserAgent = userAgent ?? context?.Request.Headers["User-Agent"].ToString() ?? string.Empty;

        var auditLog = new AuditLog
        {
            UserId = finalUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = oldValues ?? string.Empty,
            NewValues = newValues ?? string.Empty,
            IpAddress = finalIpAddress,
            UserAgent = finalUserAgent,
            CreatedAt = DateTime.UtcNow
        };

        auditLog.HmacSignature = _hmacSigner.ComputeSignature(auditLog);

        // 审计写入使用独立 DI 作用域（新 DbContext）：与请求共享 DbContext 上的已跟踪实体隔离，
        // 避免 DetectChanges 导航修复时与业务操作跟踪的 User 等实体发生 IdentityMap 冲突，
        // 导致"记录审计日志失败"（生产日志 2026-09-01 15:20:19 AuditActionFilter）。
        using var scope = _scopeFactory.CreateScope();
        var auditUnitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await auditUnitOfWork.AuditLogs.AddAsync(auditLog);
        await auditUnitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        var context = _httpContextAccessor.HttpContext;
        var httpUserId = context?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var auditLog = new AuditLog
        {
            UserId = entry.UserId ?? TryParseUserId(httpUserId),
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            OldValues = entry.OldValues ?? string.Empty,
            NewValues = entry.NewValues ?? entry.Description ?? string.Empty,
            IpAddress = entry.IpAddress ?? context?.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            UserAgent = entry.UserAgent ?? context?.Request.Headers["User-Agent"].ToString() ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        auditLog.HmacSignature = _hmacSigner.ComputeSignature(auditLog);

        await _unitOfWork.AuditLogs.AddAsync(auditLog);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<(List<AuditLogResponse> Items, int Total)> GetLogsAsync(
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
        CancellationToken cancellationToken = default)
    {
        var (logs, total) = await _unitOfWork.AuditLogs.GetPagedAsync(
            page, pageSize, action, entityType, userId, entityId, ipAddress, keyword, startDate, endDate);
        return (logs.Select(ToResponse).ToList(), total);
    }

    public async Task<(List<AuditLogResponse> Items, int Total)> GetLogsByUserIdAsync(int userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var (logs, total) = await _unitOfWork.AuditLogs.GetByUserIdAsync(userId, page, pageSize);
        return (logs.Select(ToResponse).ToList(), total);
    }

    public async Task<(List<AuditLogResponse> Items, int Total)> GetLogsByEntityAsync(string entityType, int entityId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var (logs, total) = await _unitOfWork.AuditLogs.GetByEntityAsync(entityType, entityId, page, pageSize);
        return (logs.Select(ToResponse).ToList(), total);
    }

    public async Task<AuditLogStatsResponse> GetStatsAsync(
        string? action = null,
        string? entityType = null,
        int? userId = null,
        int? entityId = null,
        string? ipAddress = null,
        string? keyword = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        var stats = await _unitOfWork.AuditLogs.GetStatsAsync(
            action, entityType, userId, entityId, ipAddress, keyword, startDate, endDate);

        return new AuditLogStatsResponse
        {
            TotalLogs = stats.TotalLogs,
            UniqueUsers = stats.UniqueUsers,
            Last24hCount = stats.Last24hCount,
            FailedActions = stats.FailedActions,
            ActionDistribution = stats.ActionDistribution
                .Select(a => new AuditLogDistributionItem { Key = a.Action, Count = a.Count })
                .ToList(),
            EntityDistribution = stats.EntityDistribution
                .Select(e => new AuditLogDistributionItem { Key = e.EntityType, Count = e.Count })
                .ToList()
        };
    }

    public async Task<string> ExportCsvAsync(
        string? action = null,
        string? entityType = null,
        int? userId = null,
        int? entityId = null,
        string? ipAddress = null,
        string? keyword = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int maxRows = 5000,
        CancellationToken cancellationToken = default)
    {
        var logs = await _unitOfWork.AuditLogs.ExportAsync(
            action, entityType, userId, entityId, ipAddress, keyword, startDate, endDate, maxRows);

        var sb = new StringBuilder();
        // 表头（带 BOM，便于 Excel 正确识别 UTF-8）
        sb.Append("\uFEFF");
        sb.Append("ID,时间(UTC),用户ID,用户名,操作,实体类型,实体ID,IP地址,User-Agent,签名");
        sb.Append('\n');

        foreach (var log in logs)
        {
            sb.Append(log.Id).Append(',');
            sb.Append(EscapeCsvField(log.CreatedAt.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            sb.Append(log.UserId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            sb.Append(EscapeCsvField(log.User?.GetFullName() ?? string.Empty)).Append(',');
            sb.Append(EscapeCsvField(log.Action)).Append(',');
            sb.Append(EscapeCsvField(log.EntityType)).Append(',');
            sb.Append(log.EntityId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            sb.Append(EscapeCsvField(log.IpAddress)).Append(',');
            sb.Append(EscapeCsvField(log.UserAgent)).Append(',');
            sb.Append(EscapeCsvField(log.HmacSignature));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    public async Task<AuditLogIntegrityResult?> VerifySingleAsync(int id, CancellationToken cancellationToken = default)
    {
        var log = await _unitOfWork.AuditLogs.GetByIdAsync(id);
        if (log == null)
        {
            return null;
        }

        var expected = _hmacSigner.ComputeSignature(log);
        var isValid = _hmacSigner.VerifySignature(log);

        return new AuditLogIntegrityResult
        {
            Id = log.Id,
            IsValid = isValid,
            StoredSignature = log.HmacSignature,
            ExpectedSignature = expected,
            VerifiedAt = DateTime.UtcNow
        };
    }

    public async Task<List<AuditLogResponse>> GetRelatedByEntityAsync(
        int excludeLogId,
        string entityType,
        int entityId,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        // 复用 GetByEntity 分页接口，取第一页
        var pageSize = Math.Min(Math.Max(limit, 1), 50);
        var (logs, _) = await _unitOfWork.AuditLogs.GetByEntityAsync(entityType, entityId, 1, pageSize + 5);
        return logs
            .Where(l => l.Id != excludeLogId)
            .Take(limit)
            .Select(ToResponse)
            .ToList();
    }

    public async Task<List<AuditLogResponse>> GetRelatedByUserAsync(
        int excludeLogId,
        int userId,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Min(Math.Max(limit, 1), 50);
        var (logs, _) = await _unitOfWork.AuditLogs.GetByUserIdAsync(userId, 1, pageSize + 5);
        return logs
            .Where(l => l.Id != excludeLogId)
            .Take(limit)
            .Select(ToResponse)
            .ToList();
    }

    public async Task<AuditLogResponse?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var log = await _unitOfWork.AuditLogs.GetByIdAsync(id);
        return log == null ? null : ToResponse(log);
    }

    /// <summary>
    /// 批量校验审计日志的 HMAC 签名，检测被篡改的记录。
    /// </summary>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页数量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>校验结果：总校验数、篡改数、篡改记录 ID 列表</returns>
    public async Task<AuditIntegrityReport> VerifyIntegrityAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var (logs, total) = await _unitOfWork.AuditLogs.GetPagedAsync(page, pageSize);
        var logList = logs.ToList();

        var tamperedIds = new List<int>();
        foreach (var log in logList)
        {
            if (!_hmacSigner.VerifySignature(log))
            {
                tamperedIds.Add(log.Id);
            }
        }

        return new AuditIntegrityReport
        {
            Page = page,
            PageSize = pageSize,
            TotalLogs = total,
            CheckedCount = logList.Count,
            TamperedCount = tamperedIds.Count,
            TamperedLogIds = tamperedIds
        };
    }

    /// <summary>
    /// 安全解析用户ID，避免 int.Parse 在异常声明格式下抛出异常。
    /// </summary>
    private static int? TryParseUserId(string? userId)
    {
        if (int.TryParse(userId, out var parsedId))
        {
            return parsedId;
        }
        return null;
    }

    private static AuditLogResponse ToResponse(AuditLog log)
    {
        return new AuditLogResponse
        {
            Id = log.Id,
            UserId = log.UserId,
            UserName = log.User?.GetFullName() ?? string.Empty,
            Action = log.Action,
            EntityType = log.EntityType,
            EntityId = log.EntityId,
            OldValues = log.OldValues,
            NewValues = log.NewValues,
            IpAddress = log.IpAddress,
            UserAgent = log.UserAgent,
            CreatedAt = log.CreatedAt,
            HmacSignature = log.HmacSignature
        };
    }

    /// <summary>
    /// CSV 字段转义：包含逗号、引号或换行时用双引号包裹，内部双引号转义为两个双引号。
    /// </summary>
    private static string EscapeCsvField(string value)
    {
        if (value == null) return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }
}
