using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 审计日志控制器 - 管理系统审计日志
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditService _auditService;

    public AuditLogsController(IAuditService auditService)
    {
        _auditService = auditService;
    }

    /// <summary>
    /// 获取审计日志列表（支持多维过滤）
    /// </summary>
    /// <param name="page">页码，默认1</param>
    /// <param name="pageSize">每页条数，默认20</param>
    /// <param name="action">操作类型过滤（大小写不敏感）</param>
    /// <param name="entityType">实体类型过滤（大小写不敏感）</param>
    /// <param name="userId">操作用户 ID 过滤</param>
    /// <param name="entityId">实体 ID 过滤</param>
    /// <param name="ipAddress">IP 地址过滤（大小写不敏感）</param>
    /// <param name="keyword">关键字模糊搜索（Action / EntityType / IpAddress / UserAgent）</param>
    /// <param name="startDate">起始时间（UTC，ISO 8601）</param>
    /// <param name="endDate">结束时间（UTC，ISO 8601）</param>
    /// <returns>审计日志列表</returns>
    [HttpGet]
    [Permission("audit_logs.view")]
    [OutputCache(PolicyName = "AuditLogs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] string? action = null,
        [FromQuery] string? entityType = null,
        [FromQuery] int? userId = null,
        [FromQuery] int? entityId = null,
        [FromQuery] string? ipAddress = null,
        [FromQuery] string? keyword = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _auditService.GetLogsAsync(
            page, pageSize, action, entityType, userId, entityId, ipAddress, keyword, startDate, endDate);
        return Ok(new PagedResponse<AuditLogResponse>
        {
            Data = items,
            TotalItems = total,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取审计日志全局统计（基于全量数据，受同一组过滤条件约束）
    /// </summary>
    [HttpGet("stats")]
    [Permission("audit_logs.view")]
    [OutputCache(PolicyName = "AuditLogs")]
    public async Task<IActionResult> GetStats(
        [FromQuery] string? action = null,
        [FromQuery] string? entityType = null,
        [FromQuery] int? userId = null,
        [FromQuery] int? entityId = null,
        [FromQuery] string? ipAddress = null,
        [FromQuery] string? keyword = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var stats = await _auditService.GetStatsAsync(
            action, entityType, userId, entityId, ipAddress, keyword, startDate, endDate);
        return Ok(stats);
    }

    /// <summary>
    /// 导出符合条件的审计日志为 CSV（带 BOM，Excel 友好）
    /// </summary>
    /// <remarks>
    /// 导出按 CreatedAt 倒序，最多 <paramref name="maxRows"/> 条（默认 5000，上限 10000）。
    /// 注意：本端点不使用 OutputCache，避免大响应体驻留缓存。
    /// </remarks>
    [HttpGet("export")]
    [Permission("audit_logs.view")]
    public async Task<IActionResult> Export(
        [FromQuery] string? action = null,
        [FromQuery] string? entityType = null,
        [FromQuery] int? userId = null,
        [FromQuery] int? entityId = null,
        [FromQuery] string? ipAddress = null,
        [FromQuery] string? keyword = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int maxRows = 5000)
    {
        maxRows = Math.Min(Math.Max(maxRows, 1), 10000);
        var csv = await _auditService.ExportCsvAsync(
            action, entityType, userId, entityId, ipAddress, keyword, startDate, endDate, maxRows);

        var fileName = $"audit-logs-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.csv";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    /// <summary>
    /// 获取指定用户的操作日志
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="page">页码，默认1</param>
    /// <param name="pageSize">每页条数，默认20</param>
    /// <returns>用户操作日志列表</returns>
    [HttpGet("user/{userId}")]
    [RequireSuperAdminOrSelf] // 超管可查任意用户；普通用户只能查自己 userId 的登录/操作历史（个人中心用）
    [OutputCache(PolicyName = "AuditLogs")]
    public async Task<IActionResult> GetUserLogs(int userId, [FromQuery] int page = 1, [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _auditService.GetLogsByUserIdAsync(userId, page, pageSize);
        return Ok(new PagedResponse<AuditLogResponse>
        {
            Data = items,
            TotalItems = total,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取指定实体的操作日志
    /// </summary>
    /// <param name="entityType">实体类型</param>
    /// <param name="entityId">实体ID</param>
    /// <param name="page">页码，默认1</param>
    /// <param name="pageSize">每页条数，默认20</param>
    /// <returns>实体操作日志列表</returns>
    [HttpGet("{entityType}/{entityId}")]
    [Permission("audit_logs.view")]
    [OutputCache(PolicyName = "AuditLogs")]
    public async Task<IActionResult> GetEntityLogs(string entityType, int entityId, [FromQuery] int page = 1, [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _auditService.GetLogsByEntityAsync(entityType, entityId, page, pageSize);
        return Ok(new PagedResponse<AuditLogResponse>
        {
            Data = items,
            TotalItems = total,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取单条审计日志详情
    /// </summary>
    /// <param name="id">日志ID</param>
    /// <returns>审计日志详情</returns>
    [HttpGet("{id:int}")]
    [Permission("audit_logs.view")]
    [OutputCache(PolicyName = "AuditLogs")]
    public async Task<IActionResult> GetById(int id)
    {
        var log = await _auditService.GetByIdAsync(id);
        if (log == null)
        {
            return NotFound();
        }
        return Ok(log);
    }

    /// <summary>
    /// 校验单条审计日志的 HMAC 签名完整性。
    /// </summary>
    /// <param name="id">日志 ID</param>
    /// <returns>校验结果（包含存储签名、期望签名、是否被篡改）</returns>
    [HttpGet("{id:int}/verify")]
    [Permission("audit_logs.view")]
    public async Task<IActionResult> VerifySingle(int id)
    {
        var result = await _auditService.VerifySingleAsync(id);
        if (result == null)
        {
            return NotFound();
        }
        return Ok(result);
    }

    /// <summary>
    /// 获取关联日志（同实体的其他操作记录，排除自身）。
    /// 用于详情页快速查看该实体的完整操作链。
    /// </summary>
    /// <param name="id">当前日志 ID（排除自身）</param>
    /// <param name="limit">返回条数上限，默认 10，最大 50</param>
    [HttpGet("{id:int}/related")]
    [Permission("audit_logs.view")]
    public async Task<IActionResult> GetRelated(int id, [FromQuery] int limit = 10)
    {
        limit = Math.Min(Math.Max(limit, 1), 50);
        var log = await _auditService.GetByIdAsync(id);
        if (log == null)
        {
            return NotFound();
        }

        // 优先返回同实体的关联日志；若当前日志无实体类型，回退到同用户关联
        if (!string.IsNullOrEmpty(log.EntityType) && log.EntityId.HasValue)
        {
            var related = await _auditService.GetRelatedByEntityAsync(id, log.EntityType, log.EntityId.Value, limit);
            return Ok(new { source = "entity", items = related });
        }

        if (log.UserId.HasValue)
        {
            var related = await _auditService.GetRelatedByUserAsync(id, log.UserId.Value, limit);
            return Ok(new { source = "user", items = related });
        }

        return Ok(new { source = "none", items = Array.Empty<AuditLogResponse>() });
    }

    /// <summary>
    /// 校验审计日志完整性（HMAC 签名验证），检测被篡改的记录。
    /// </summary>
    /// <param name="page">页码，默认1</param>
    /// <param name="pageSize">每页条数，默认100</param>
    /// <returns>完整性校验报告</returns>
    [HttpGet("verify-integrity")]
    [Permission("audit_logs.view")]
    public async Task<IActionResult> VerifyIntegrity([FromQuery] int page = 1, [FromQuery] int pageSize = PaginationHelper.MaxPageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxAuditLogPageSize);
        var report = await _auditService.VerifyIntegrityAsync(page, pageSize);
        return Ok(report);
    }
}
