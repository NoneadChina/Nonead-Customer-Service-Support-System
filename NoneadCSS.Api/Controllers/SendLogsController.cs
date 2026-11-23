using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 发送日志控制器 - 查看系统邮件发送日志
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class SendLogsController : ControllerBase
{
    private readonly ISendLogService _service;

    public SendLogsController(ISendLogService service)
    {
        _service = service;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    private int CurrentUserRole => int.Parse(User.FindFirst(ClaimTypes.Role)?.Value ?? "0");
    private bool IsAdmin => CurrentUserRole == (int)UserRoleType.SuperAdmin;

    /// <summary>
    /// 分页查询发送日志
    /// </summary>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页数量（默认 20，最大 200）</param>
    /// <param name="status">发送状态筛选</param>
    /// <param name="mailType">邮件类型筛选</param>
    /// <param name="mailboxId">邮箱 ID 筛选</param>
    /// <param name="userId">操作用户 ID 筛选</param>
    /// <param name="startDate">开始日期（可选，ISO 格式 yyyy-MM-dd）</param>
    /// <param name="endDate">结束日期（可选，ISO 格式 yyyy-MM-dd）</param>
    /// <param name="keyword">关键词搜索（收件邮箱、消息 ID）</param>
    /// <returns>分页发送日志列表</returns>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] int? status = null,
        [FromQuery] int? mailType = null,
        [FromQuery] int? mailboxId = null,
        [FromQuery] int? userId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var result = await _service.GetListAsync(
            page, pageSize, status, mailType, mailboxId,
            IsAdmin ? userId : CurrentUserId,
            startDate, endDate, keyword);

        return Ok(result);
    }

    /// <summary>
    /// 获取日志详情
    /// </summary>
    /// <param name="id">日志 ID</param>
    /// <returns>日志详情</returns>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _service.GetByIdAsync(id);
        if (item == null)
        {
            return NotFound(new { message = "日志不存在" });
        }
        // 非管理员只能查看自己的发送日志，防止越权读取
        if (!IsAdmin && item.UserId != CurrentUserId)
        {
            return Forbid();
        }
        return Ok(item);
    }

    /// <summary>
    /// 获取发送日志总览（统计、邮箱分布、趋势、最近失败）
    /// </summary>
    /// <param name="startDate">开始日期（可选，默认最近 30 天）</param>
    /// <param name="endDate">结束日期（可选，默认今天）</param>
    /// <param name="mailboxId">邮箱 ID 筛选（可选）</param>
    /// <returns>总览数据</returns>
    [HttpGet("overview")]
    [Permission("send_logs.view")]
    public async Task<IActionResult> GetOverview(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int? mailboxId = null)
    {
        var overview = await _service.GetOverviewAsync(startDate, endDate, mailboxId);
        return Ok(overview);
    }

    /// <summary>
    /// 获取最近失败的发送记录
    /// </summary>
    /// <param name="count">返回数量（默认 10）</param>
    /// <returns>失败记录列表</returns>
    [HttpGet("recent-failures")]
    [Permission("send_logs.view")]
    public async Task<IActionResult> GetRecentFailures([FromQuery] int count = 10)
    {
        var list = await _service.GetRecentFailuresAsync(count);
        return Ok(list);
    }

    /// <summary>
    /// 获取发送状态选项
    /// </summary>
    /// <returns>状态码与名称映射</returns>
    [HttpGet("status-options")]
    public async Task<IActionResult> GetStatusOptions()
    {
        var options = await _service.GetStatusOptionsAsync();
        return Ok(options);
    }

    /// <summary>
    /// 获取邮件类型选项
    /// </summary>
    /// <returns>类型码与名称映射</returns>
    [HttpGet("mail-type-options")]
    public async Task<IActionResult> GetMailTypeOptions()
    {
        var options = await _service.GetMailTypeOptionsAsync();
        return Ok(options);
    }
}
