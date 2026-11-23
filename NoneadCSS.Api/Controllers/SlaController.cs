using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// SLA 策略管理 API。提供 SLA 策略的 CRUD、违规查询、扫描和会话 SLA 状态查询。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class SlaController : ControllerBase
{
    private readonly ISlaService _service;

    public SlaController(ISlaService service)
    {
        _service = service;
    }

    /// <summary>
    /// 分页获取 SLA 策略列表。
    /// </summary>
    [HttpGet]
    [RequireSuperAdmin]
    [OutputCache(PolicyName = "Sla")]
    public async Task<ActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] int? mailboxId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetListAsync(page, pageSize, mailboxId, isActive, keyword);
        return Ok(new PagedResponse<SlaPolicyDto>
        {
            Data = items.ToList(),
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    [HttpGet("{id}")]
    [RequireSuperAdmin]
    [OutputCache(PolicyName = "Sla")]
    public async Task<ActionResult<SlaPolicyDto>> GetById(int id)
    {
        var result = await _service.GetByIdAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// SLA 概览：策略统计、违规统计、最近违规、预警列表。
    /// </summary>
    [HttpGet("overview")]
    [RequireSuperAdmin]
    public async Task<ActionResult<SlaOverviewResponse>> GetOverview()
    {
        var result = await _service.GetOverviewAsync();
        return Ok(result);
    }

    /// <summary>
    /// 分页获取 SLA 违规记录。
    /// </summary>
    [HttpGet("violations")]
    [RequireSuperAdmin]
    public async Task<ActionResult> GetViolations(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] int? policyId = null,
        [FromQuery] int? conversationId = null,
        [FromQuery] string? violationType = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetViolationsAsync(
            page, pageSize, policyId, conversationId, violationType, status, startDate, endDate);
        return Ok(new PagedResponse<SlaViolationDto>
        {
            Data = items.ToList(),
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取指定会话的 SLA 违规记录。
    /// </summary>
    [HttpGet("conversations/{conversationId}/violations")]
    [RequireSuperAdmin]
    public async Task<ActionResult<List<SlaViolationDto>>> GetConversationViolations(int conversationId)
    {
        var result = await _service.GetViolationsByConversationAsync(conversationId);
        return Ok(result);
    }

    /// <summary>
    /// 获取指定会话的 SLA 状态（首次响应和解决时长）。
    /// </summary>
    [HttpGet("conversations/{conversationId}/status")]
    [RequireSuperAdmin]
    public async Task<ActionResult<SlaConversationStatusResponse>> GetConversationStatus(int conversationId)
    {
        var result = await _service.GetConversationStatusAsync(conversationId);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// 创建 SLA 策略。
    /// </summary>
    [HttpPost]
    [RequireSuperAdmin]
    public async Task<ActionResult<SlaPolicyDto>> Create([FromBody] CreateSlaPolicyRequest request)
    {
        var result = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult<SlaPolicyDto>> Update(int id, [FromBody] UpdateSlaPolicyRequest request)
    {
        if (id != request.Id) return BadRequest("ID 不一致");
        var result = await _service.UpdateAsync(id, request);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult> Delete(int id)
    {
        var success = await _service.DeleteAsync(id);
        if (!success) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// 切换 SLA 策略启用状态。
    /// </summary>
    [HttpPost("{id}/toggle-active")]
    [RequireSuperAdmin]
    public async Task<ActionResult> ToggleActive(int id)
    {
        var isActive = await _service.ToggleActiveAsync(id);
        if (isActive == null) return NotFound();
        return Ok(new { isActive = isActive.Value });
    }

    // ====== 垃圾箱 ======

    /// <summary>
    /// 获取已软删除的 SLA 策略列表（垃圾箱）。
    /// </summary>
    [HttpGet("trash")]
    [RequireSuperAdmin]
    public async Task<ActionResult> GetTrash(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetTrashAsync(page, pageSize, keyword);
        return Ok(new PagedResponse<SlaPolicyDto>
        {
            Data = items.ToList(),
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取垃圾箱中的 SLA 策略数量。
    /// </summary>
    [HttpGet("trash/count")]
    [RequireSuperAdmin]
    public async Task<ActionResult> GetTrashCount()
    {
        var count = await _service.GetTrashCountAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 恢复软删除的 SLA 策略。
    /// </summary>
    [HttpPost("{id}/restore")]
    [RequireSuperAdmin]
    public async Task<ActionResult> Restore(int id)
    {
        var success = await _service.RestoreAsync(id);
        if (!success) return NotFound();
        return Ok();
    }

    /// <summary>
    /// 彻底删除指定 SLA 策略（不可恢复）。
    /// </summary>
    [HttpDelete("{id}/hard")]
    [RequireSuperAdmin]
    public async Task<ActionResult> HardDelete(int id)
    {
        var success = await _service.HardDeleteAsync(id);
        if (!success) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// 清空垃圾箱：彻底删除所有已软删除的 SLA 策略。
    /// </summary>
    [HttpDelete("trash")]
    [RequireSuperAdmin]
    public async Task<ActionResult> ClearTrash()
    {
        var count = await _service.ClearTrashAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 手动触发 SLA 违规扫描。扫描所有活跃会话，更新违规状态。
    /// </summary>
    [HttpPost("scan")]
    [RequireSuperAdmin]
    public async Task<ActionResult> Scan()
    {
        var (scanned, newViolations, breached) = await _service.ScanViolationsAsync();
        return Ok(new
        {
            scanned,
            newViolations,
            breached,
            scannedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// 为指定会话手动应用 SLA 策略（创建预警记录）。
    /// </summary>
    [HttpPost("apply/{conversationId}")]
    [RequireSuperAdmin]
    public async Task<ActionResult> ApplyToConversation(int conversationId)
    {
        await _service.ApplyToConversationAsync(conversationId);
        return Ok();
    }
}
