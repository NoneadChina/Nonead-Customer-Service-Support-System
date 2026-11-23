using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 自定义报表控制器 - 管理自定义报表与调度
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("custom-reports")]
public class CustomReportsController : ControllerBase
{
    private readonly ICustomReportService _customReportService;

    public CustomReportsController(ICustomReportService customReportService)
    {
        _customReportService = customReportService;
    }

    /// <summary>
    /// 将业务层 InvalidOperationException 映射为正确的 HTTP 状态码。
    /// - 消息包含"无权" → 403 Forbidden
    /// - 消息包含"不存在" → 404 Not Found
    /// - 其他业务校验失败 → 400 BadRequest
    /// </summary>
    private ActionResult MapBusinessException(InvalidOperationException ex)
    {
        var msg = ex.Message ?? string.Empty;
        if (msg.Contains("无权"))
        {
            return Forbid();
        }
        if (msg.Contains("不存在"))
        {
            return NotFound(new { message = msg });
        }
        return BadRequest(new { message = msg });
    }

    public record PinRequest(bool Pinned, int? SortOrder);

    [HttpGet]
    public async Task<ActionResult> GetPaged(
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        [FromQuery] string? visibility = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        try
        {
            var (items, totalCount) = await _customReportService.GetPagedAsync(search, category, visibility, page, pageSize);
            return Ok(new
            {
                data = items,
                totalItems = totalCount,
                currentPage = page,
                totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
            });
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpGet("pinned")]
    public async Task<ActionResult<List<CustomReportDto>>> GetPinned([FromQuery] int limit = 10)
    {
        limit = Math.Min(Math.Max(limit, 1), PaginationHelper.MaxPageSize);
        try
        {
            var result = await _customReportService.GetPinnedAsync(limit);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CustomReportDto>> GetById(int id)
    {
        try
        {
            var result = await _customReportService.GetByIdAsync(id);
            if (result == null) return NotFound();
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpPost]
    [Permission("custom_reports.create")]
    public async Task<ActionResult<CustomReportDto>> Create([FromBody] CreateCustomReportRequest request)
    {
        try
        {
            var result = await _customReportService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpPut("{id}")]
    [Permission("custom_reports.update")]
    public async Task<ActionResult<CustomReportDto>> Update(int id, [FromBody] UpdateCustomReportRequest request)
    {
        try
        {
            var result = await _customReportService.UpdateAsync(id, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpDelete("{id}")]
    [Permission("custom_reports.delete")]
    public async Task<ActionResult> Delete(int id)
    {
        try
        {
            await _customReportService.DeleteAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    /// <summary>
    /// 获取已删除（垃圾箱）列表 - 支持分页与搜索
    /// </summary>
    [HttpGet("trash")]
    public async Task<ActionResult> GetTrashPaged(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        try
        {
            var (items, totalCount) = await _customReportService.GetTrashPagedAsync(search, page, pageSize);
            return Ok(new
            {
                data = items,
                totalItems = totalCount,
                currentPage = page,
                totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
            });
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    /// <summary>
    /// 恢复垃圾箱中的报表
    /// </summary>
    [HttpPost("{id}/restore")]
    [Permission("custom_reports.delete")]
    public async Task<ActionResult<CustomReportDto>> Restore(int id)
    {
        try
        {
            var result = await _customReportService.RestoreAsync(id);
            if (result == null) return NotFound();
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    /// <summary>
    /// 永久删除（硬删除）垃圾箱中的报表
    /// </summary>
    [HttpDelete("{id}/hard")]
    [Permission("custom_reports.delete")]
    public async Task<ActionResult> HardDelete(int id)
    {
        try
        {
            var success = await _customReportService.HardDeleteAsync(id);
            if (!success) return NotFound();
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    /// <summary>
    /// 清空垃圾箱（一次永久删除本人可见的所有已删除报表）
    /// </summary>
    [HttpPost("trash/clear")]
    [Permission("custom_reports.delete")]
    public async Task<ActionResult<int>> ClearTrash()
    {
        try
        {
            var count = await _customReportService.ClearTrashAsync();
            return Ok(new { cleared = count });
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpPatch("{id}/pin")]
    [Permission("custom_reports.update")]
    public async Task<ActionResult<CustomReportDto>> TogglePin(int id, [FromBody] PinRequest req)
    {
        try
        {
            var result = await _customReportService.ToggleDashboardPinAsync(id, req.Pinned, req.SortOrder);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpGet("{id}/execute")]
    public async Task<ActionResult<CustomReportExecutionResultDto>> Execute(int id)
    {
        try
        {
            var result = await _customReportService.ExecuteAsync(id);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    /// <summary>
    /// 基于传入配置直接预览报表，不持久化到数据库。
    /// 用于编辑器中的"刷新预览"按钮，预览与保存解耦。
    /// </summary>
    [HttpPost("execute-preview")]
    [Permission("custom_reports.execute")]
    public async Task<ActionResult<CustomReportExecutionResultDto>> ExecutePreview([FromBody] CreateCustomReportRequest request)
    {
        try
        {
            var result = await _customReportService.ExecutePreviewAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpGet("{id}/export")]
    public async Task<IActionResult> Export(int id, [FromQuery] string format = "csv")
    {
        try
        {
            var (content, contentType, fileName) = await _customReportService.ExportAsync(id, format);
            return File(content, contentType, fileName);
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpGet("{id}/schedules")]
    public async Task<ActionResult<List<CustomReportScheduleDto>>> ListSchedules(int id)
    {
        try
        {
            var result = await _customReportService.ListSchedulesAsync(id);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpPost("{id}/schedules")]
    [Permission("custom_reports.schedules.manage")]
    public async Task<ActionResult<CustomReportScheduleDto>> CreateSchedule(int id, [FromBody] CreateScheduleRequest request)
    {
        try
        {
            var result = await _customReportService.CreateScheduleAsync(id, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpPut("schedules/{scheduleId}")]
    [Permission("custom_reports.schedules.manage")]
    public async Task<ActionResult<CustomReportScheduleDto>> UpdateSchedule(int scheduleId, [FromBody] UpdateScheduleRequest request)
    {
        try
        {
            var result = await _customReportService.UpdateScheduleAsync(scheduleId, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpDelete("schedules/{scheduleId}")]
    [Permission("custom_reports.schedules.manage")]
    public async Task<ActionResult> DeleteSchedule(int scheduleId)
    {
        try
        {
            await _customReportService.DeleteScheduleAsync(scheduleId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return MapBusinessException(ex);
        }
    }
}
