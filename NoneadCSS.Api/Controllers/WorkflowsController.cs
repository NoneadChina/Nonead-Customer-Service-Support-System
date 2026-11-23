using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NoneadCSS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("automation-rules")]
public class WorkflowsController : ControllerBase
{
    private readonly IWorkflowService _service;

    public WorkflowsController(IWorkflowService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequireSuperAdmin]
    public async Task<ActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] int? mailboxId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? triggerType = null,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetListAsync(page, pageSize, mailboxId, isActive, triggerType, keyword);
        return Ok(new PagedResponse<WorkflowDto>
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
    public async Task<ActionResult<WorkflowDto>> GetById(int id)
    {
        var result = await _service.GetByIdAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("overview")]
    [RequireSuperAdmin]
    public async Task<ActionResult<WorkflowOverviewResponse>> GetOverview()
    {
        var result = await _service.GetOverviewAsync();
        return Ok(result);
    }

    [HttpGet("execution-logs")]
    [RequireSuperAdmin]
    public async Task<ActionResult> GetExecutionLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] int? workflowId = null,
        [FromQuery] int? conversationId = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetExecutionLogsAsync(
            page, pageSize, workflowId, conversationId, status, startDate, endDate);
        return Ok(new PagedResponse<WorkflowExecutionLogDto>
        {
            Data = items.ToList(),
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    [HttpPost]
    [RequireSuperAdmin]
    public async Task<ActionResult<WorkflowDto>> Create([FromBody] CreateWorkflowRequest request)
    {
        var result = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult<WorkflowDto>> Update(int id, [FromBody] UpdateWorkflowRequest request)
    {
        if (id != request.Id) return BadRequest("ID 不一致");
        var result = await _service.UpdateAsync(id, request);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("trash")]
    [RequireSuperAdmin]
    public async Task<ActionResult> ClearTrash([FromQuery] int? mailboxId = null)
    {
        var count = await _service.ClearTrashAsync(mailboxId);
        return Ok(new { count });
    }

    [HttpDelete("{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult> Delete(int id)
    {
        var success = await _service.DeleteAsync(id);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpPost("{id}/toggle-active")]
    [RequireSuperAdmin]
    public async Task<ActionResult> ToggleActive(int id)
    {
        var success = await _service.ToggleActiveAsync(id);
        if (!success) return NotFound();
        return Ok();
    }

    [HttpPost("{id}/test")]
    [RequireSuperAdmin]
    public async Task<ActionResult<WorkflowTestResponse>> Test(int id, [FromBody] WorkflowTestRequest request)
    {
        var result = await _service.TestAsync(id, request);
        return Ok(result);
    }

    [HttpPost("execute/{conversationId}/{triggerType}")]
    [RequireSuperAdmin]
    public async Task<ActionResult> Execute(int conversationId, string triggerType)
    {
        var count = await _service.ExecuteForConversationAsync(conversationId, triggerType);
        return Ok(new { executedCount = count });
    }

    [HttpGet("trash")]
    [RequireSuperAdmin]
    public async Task<ActionResult> GetTrash(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] int? mailboxId = null,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetDeletedListAsync(page, pageSize, mailboxId, keyword);
        return Ok(new PagedResponse<WorkflowDto>
        {
            Data = items.ToList(),
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    [HttpGet("trash/count")]
    [RequireSuperAdmin]
    public async Task<ActionResult> GetTrashCount([FromQuery] int? mailboxId = null)
    {
        var count = await _service.GetDeletedCountAsync(mailboxId);
        return Ok(new { count });
    }

    [HttpPost("{id}/restore")]
    [RequireSuperAdmin]
    public async Task<ActionResult> Restore(int id)
    {
        var success = await _service.RestoreAsync(id);
        if (!success) return NotFound();
        return Ok();
    }

    [HttpDelete("{id}/hard")]
    [RequireSuperAdmin]
    public async Task<ActionResult> HardDelete(int id)
    {
        var success = await _service.HardDeleteAsync(id);
        if (!success) return NotFound();
        return NoContent();
    }
}
