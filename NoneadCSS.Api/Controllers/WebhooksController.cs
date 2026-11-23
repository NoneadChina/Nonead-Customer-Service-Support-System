using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// Webhook 控制器 - 管理系统 Webhook
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class WebhooksController : ControllerBase
{
    private readonly IWebhookService _service;

    public WebhooksController(IWebhookService service)
    {
        _service = service;
    }

    /// <summary>
    /// 获取 Webhook 列表（分页）
    /// </summary>
    [HttpGet]
    [Permission("webhooks.view")]
    [OutputCache(PolicyName = "Webhooks")]
    public async Task<ActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] int? mailboxId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? keyword = null,
        [FromQuery] string? eventType = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetPagedAsync(new WebhookQuery
        {
            Page = page,
            PageSize = pageSize,
            MailboxId = mailboxId,
            IsActive = isActive,
            Keyword = keyword,
            EventType = eventType,
        });

        return Ok(new PagedResponse<WebhookDto>
        {
            Data = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取 Webhook 详情
    /// </summary>
    [HttpGet("{id}")]
    [Permission("webhooks.view")]
    [OutputCache(PolicyName = "Webhooks")]
    public async Task<ActionResult<WebhookDto>> GetById(int id)
    {
        var result = await _service.GetByIdAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// 获取 Webhook 概览统计
    /// </summary>
    [HttpGet("overview")]
    [Permission("webhooks.view")]
    [OutputCache(PolicyName = "Webhooks")]
    public async Task<ActionResult<WebhookOverviewResponse>> GetOverview()
    {
        var result = await _service.GetOverviewAsync();
        return Ok(result);
    }

    /// <summary>
    /// 创建 Webhook
    /// </summary>
    [HttpPost]
    [RequireSuperAdmin]
    public async Task<ActionResult<WebhookDto>> Create([FromBody] CreateWebhookRequest request)
    {
        var result = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// 更新 Webhook
    /// </summary>
    [HttpPut("{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult<WebhookDto>> Update(int id, [FromBody] UpdateWebhookRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        if (id != request.Id) return BadRequest("ID 不匹配");
        var result = await _service.UpdateAsync(request, ifMatch);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// 删除 Webhook
    /// </summary>
    [HttpDelete("{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult> Delete(int id)
    {
        var result = await _service.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok();
    }

    /// <summary>
    /// 切换启用/禁用状态
    /// </summary>
    [HttpPost("{id}/toggle-active")]
    [RequireSuperAdmin]
    public async Task<ActionResult> ToggleActive(int id, [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        var result = await _service.ToggleActiveAsync(id, ifMatch);
        if (!result) return NotFound();
        return Ok();
    }

    /// <summary>
    /// 测试 Webhook 连通性
    /// </summary>
    [HttpPost("{id}/test")]
    [RequireSuperAdmin]
    public async Task<ActionResult<TestWebhookResponse>> Test(int id, [FromBody] TestWebhookRequest request)
    {
        var result = await _service.TestAsync(id, request);
        return Ok(result);
    }

    /// <summary>
    /// 获取 Webhook 触发日志
    /// </summary>
    [HttpGet("{id}/logs")]
    [Permission("webhooks.view")]
    [OutputCache(PolicyName = "Webhooks")]
    public async Task<ActionResult> GetLogs(
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] string? eventType = null,
        [FromQuery] bool? isSuccess = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetLogsPagedAsync(new WebhookLogQuery
        {
            Page = page,
            PageSize = pageSize,
            WebhookId = id,
            EventType = eventType,
            IsSuccess = isSuccess,
        });

        return Ok(new PagedResponse<WebhookLogDto>
        {
            Data = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取所有 Webhook 触发日志（分页）
    /// </summary>
    [HttpGet("logs")]
    [Permission("webhooks.view")]
    [OutputCache(PolicyName = "Webhooks")]
    public async Task<ActionResult> GetAllLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] int? webhookId = null,
        [FromQuery] string? eventType = null,
        [FromQuery] bool? isSuccess = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetLogsPagedAsync(new WebhookLogQuery
        {
            Page = page,
            PageSize = pageSize,
            WebhookId = webhookId,
            EventType = eventType,
            IsSuccess = isSuccess,
        });

        return Ok(new PagedResponse<WebhookLogDto>
        {
            Data = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取垃圾箱中的 Webhook 列表（分页）
    /// </summary>
    [HttpGet("trash")]
    [Permission("webhooks.view")]
    public async Task<ActionResult> GetTrash(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.SmallPageSize,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetTrashPagedAsync(page, pageSize, keyword);
        return Ok(new PagedResponse<WebhookDto>
        {
            Data = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 恢复已删除的 Webhook
    /// </summary>
    [HttpPost("{id}/restore")]
    [RequireSuperAdmin]
    public async Task<ActionResult> Restore(int id)
    {
        var result = await _service.RestoreAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// 彻底删除 Webhook（管理员）
    /// </summary>
    [HttpDelete("{id}/hard")]
    [RequireSuperAdmin]
    public async Task<ActionResult> HardDelete(int id)
    {
        await _service.HardDeleteAsync(id);
        return Ok();
    }

    /// <summary>
    /// 清空垃圾箱（管理员）
    /// </summary>
    [HttpDelete("trash")]
    [RequireSuperAdmin]
    public async Task<ActionResult> ClearTrash()
    {
        var count = await _service.ClearTrashAsync();
        return Ok(new { count });
    }
}
