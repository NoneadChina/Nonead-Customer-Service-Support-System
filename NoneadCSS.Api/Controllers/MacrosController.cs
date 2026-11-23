using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using System.Security.Claims;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 宏（Macro）控制器 - 管理预定义的批量操作集合
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("automation-rules")]
public class MacrosController : ControllerBase
{
    private readonly IMacroService _service;

    public MacrosController(IMacroService service)
    {
        _service = service;
    }

    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    private bool IsAdmin => User.FindFirst(ClaimTypes.Role)?.Value == "2";

    /// <summary>
    /// 获取宏列表（分页）
    /// </summary>
    [HttpGet]
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
        var (items, total) = await _service.GetPagedAsync(new MacroQuery
        {
            Page = page,
            PageSize = pageSize,
            MailboxId = mailboxId,
            IsActive = isActive,
            Keyword = keyword,
        });

        return Ok(new PagedResponse<MacroDto>
        {
            Data = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取宏详情
    /// </summary>
    [HttpGet("{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult<MacroDto>> GetById(int id)
    {
        var result = await _service.GetByIdAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// 获取宏概览统计
    /// </summary>
    [HttpGet("overview")]
    [RequireSuperAdmin]
    public async Task<ActionResult<MacroOverviewResponse>> GetOverview()
    {
        var result = await _service.GetOverviewAsync();
        return Ok(result);
    }

    /// <summary>
    /// 获取指定邮箱可用的宏列表
    /// </summary>
    [HttpGet("mailbox/{mailboxId}")]
    public async Task<ActionResult> GetByMailbox(int mailboxId)
    {
        var result = await _service.GetByMailboxAsync(mailboxId);
        return Ok(result);
    }

    /// <summary>
    /// 创建宏
    /// </summary>
    [HttpPost]
    [RequireSuperAdmin]
    public async Task<ActionResult<MacroDto>> Create([FromBody] CreateMacroRequest request)
    {
        var result = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// 更新宏
    /// </summary>
    [HttpPut("{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult<MacroDto>> Update(int id, [FromBody] UpdateMacroRequest request)
    {
        if (id != request.Id) return BadRequest(new { message = "ID 不匹配" });

        var result = await _service.UpdateAsync(request);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// 删除宏（移入垃圾箱）
    /// </summary>
    [HttpDelete("{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult> Delete(int id)
    {
        var success = await _service.DeleteAsync(id);
        if (!success) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// 获取垃圾箱中的宏列表
    /// </summary>
    [HttpGet("trash")]
    [RequireSuperAdmin]
    public async Task<ActionResult> GetTrash(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.SmallPageSize,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetTrashAsync(page, pageSize, keyword);
        return Ok(new PagedResponse<MacroDto>
        {
            Data = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取垃圾箱中的宏数量
    /// </summary>
    [HttpGet("trash/count")]
    [RequireSuperAdmin]
    public async Task<ActionResult> GetTrashCount()
    {
        var count = await _service.GetTrashCountAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 恢复垃圾箱中的宏
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
    /// 彻底删除垃圾箱中的宏
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
    /// 清空垃圾箱
    /// </summary>
    [HttpDelete("trash")]
    [RequireSuperAdmin]
    public async Task<ActionResult> ClearTrash()
    {
        var count = await _service.ClearTrashAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 切换宏启用状态
    /// </summary>
    [HttpPost("{id}/toggle-active")]
    [RequireSuperAdmin]
    public async Task<ActionResult> ToggleActive(int id)
    {
        var success = await _service.ToggleActiveAsync(id);
        if (!success) return NotFound();
        return Ok();
    }

    /// <summary>
    /// 复制宏（基于现有宏创建副本，UsageCount 重置为 0）
    /// </summary>
    [HttpPost("{id}/duplicate")]
    [RequireSuperAdmin]
    public async Task<ActionResult<MacroDto>> Duplicate(int id, [FromBody] DuplicateMacroRequest? request)
    {
        var result = await _service.DuplicateAsync(id, request?.NewName);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// 预览宏执行（不实际执行，仅返回将要执行的动作描述）
    /// </summary>
    [HttpPost("{id}/preview")]
    [RequireSuperAdmin]
    public async Task<ActionResult<MacroPreviewResponse>> Preview(int id, [FromBody] ExecuteMacroRequest request)
    {
        var result = await _service.PreviewAsync(id, request.ConversationId);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    /// <summary>
    /// 执行宏
    /// </summary>
    [HttpPost("{id}/execute")]
    [RequireSuperAdmin]
    public async Task<ActionResult<ExecuteMacroResponse>> Execute(int id, [FromBody] ExecuteMacroRequest request)
    {
        var result = await _service.ExecuteAsync(id, request.ConversationId, CurrentUserId);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    /// <summary>
    /// 获取执行日志
    /// </summary>
    [HttpGet("execution-logs")]
    [RequireSuperAdmin]
    public async Task<ActionResult> GetExecutionLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] int? macroId = null,
        [FromQuery] int? conversationId = null)
    {
        // 规范化分页参数，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);

        var (items, total) = await _service.GetExecutionLogsAsync(new MacroExecutionLogQuery
        {
            Page = page,
            PageSize = pageSize,
            MacroId = macroId,
            ConversationId = conversationId,
        });

        return Ok(new PagedResponse<MacroExecutionLogDto>
        {
            Data = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }
}
