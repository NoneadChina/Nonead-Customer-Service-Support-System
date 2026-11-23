using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 仪表盘控制器 - 提供系统统计数据与工作负载分析
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public DashboardController(IDashboardService dashboardService, ICurrentUserAccessor currentUserAccessor)
    {
        _dashboardService = dashboardService;
        _currentUserAccessor = currentUserAccessor;
    }

    /// <summary>
    /// 获取仪表盘统计数据
    /// </summary>
    /// <remarks>返回会话统计、用户工作负载、邮箱统计和近7天趋势数据。</remarks>
    /// <param name="mailboxId">可选邮箱ID，按邮箱过滤统计</param>
    /// <returns>仪表盘统计数据</returns>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] int? mailboxId = null)
    {
        // S6: mailboxId 必须校验归属，非超管只能查询其可访问的邮箱
        if (mailboxId.HasValue && !await _currentUserAccessor.CanAccessMailboxAsync(mailboxId))
            return Forbid();
        var stats = await _dashboardService.GetDashboardStatsAsync(mailboxId);
        return Ok(stats);
    }
}
