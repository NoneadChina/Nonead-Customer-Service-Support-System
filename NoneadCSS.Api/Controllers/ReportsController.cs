using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 报表控制器 - 提供综合运营数据统计与分析
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportsController(IReportService reportService)
    {
        _reportService = reportService;
    }

    /// <summary>
    /// 获取综合报表
    /// </summary>
    /// <remarks>
    /// 根据时间范围和邮箱筛选返回综合报表数据，包含核心指标、趋势、状态分布、客服绩效（前 100 条概览）、邮箱负载、客户域名 Top10。
    /// 当客服数大于 100 时，完整绩效请调用 GET /api/reports/user-performance 分页端点。
    /// 
    /// 示例：
    /// - GET /api/reports?startDate=2026-07-01&amp;endDate=2026-07-13
    /// - GET /api/reports?startDate=2026-07-01&amp;endDate=2026-07-13&amp;mailboxId=1
    /// </remarks>
    /// <param name="startDate">开始日期（含），格式 yyyy-MM-dd</param>
    /// <param name="endDate">结束日期（含），格式 yyyy-MM-dd</param>
    /// <param name="mailboxId">可选邮箱 ID</param>
    /// <returns>综合报表数据</returns>
    [HttpGet]
    [RequireSuperAdmin]
    [EnableRateLimiting("ReportsPolicy")]
    public async Task<IActionResult> GetReport(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int? mailboxId = null)
    {
        // 默认时间范围：最近 30 天
        var end = endDate?.Date ?? DateTime.Today;
        var start = startDate?.Date ?? end.AddDays(-29);

        // 保证 start &lt;= end
        if (start > end)
        {
            (start, end) = (end, start);
        }

        var report = await _reportService.GetReportAsync(start, end, mailboxId);
        return Ok(report);
    }

    /// <summary>
    /// 分页获取客服绩效报表
    /// </summary>
    /// <remarks>
    /// 当综合报表中 UserPerformance 被截断（超过 100 名客服）时，使用该端点获取完整分页数据。
    /// 
    /// 示例：
    /// - GET /api/reports/user-performance?startDate=2026-07-01&amp;endDate=2026-07-13&amp;page=1&amp;pageSize=50
    /// - GET /api/reports/user-performance?startDate=2026-07-01&amp;endDate=2026-07-13&amp;mailboxId=1
    /// </remarks>
    /// <param name="startDate">开始日期（含），格式 yyyy-MM-dd</param>
    /// <param name="endDate">结束日期（含），格式 yyyy-MM-dd</param>
    /// <param name="mailboxId">可选邮箱 ID</param>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页条数，默认 20，最大 100</param>
    /// <returns>分页后的客服绩效数据</returns>
    [HttpGet("user-performance")]
    [RequireSuperAdmin]
    [EnableRateLimiting("ReportsPolicy")]
    public async Task<IActionResult> GetUserPerformancePaged(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int? mailboxId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);

        // 默认时间范围：最近 30 天
        var end = endDate?.Date ?? DateTime.Today;
        var start = startDate?.Date ?? end.AddDays(-29);

        if (start > end)
        {
            (start, end) = (end, start);
        }

        var (items, totalCount) = await _reportService.GetUserPerformancePagedAsync(start, end, mailboxId, page, pageSize);
        return Ok(new PagedResponse<ReportUserPerformanceDto>
        {
            Data = items,
            CurrentPage = page,
            PageSize = pageSize,
            TotalItems = totalCount,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }
}
