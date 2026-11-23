using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 时间追踪 API - 记录处理工单/客户/独立任务花费的时间
/// </summary>
[Authorize]
[RequireModule("time-tracking")]
[ApiController]
[Route("api/time-tracking")]
public class TimeTrackingController : ControllerBase
{
    private readonly ITimeEntryService _timeEntryService;

    public TimeTrackingController(ITimeEntryService timeEntryService)
    {
        _timeEntryService = timeEntryService;
    }

    /// <summary>获取时间记录列表</summary>
    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] TimeEntryQuery query)
    {
        var (items, total) = await _timeEntryService.GetListAsync(query);
        return Ok(new
        {
            data = items,
            totalItems = total,
            currentPage = query.Page,
            totalPages = (int)Math.Ceiling((double)total / query.PageSize)
        });
    }

    /// <summary>获取时间记录详情</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var entry = await _timeEntryService.GetByIdAsync(id);
        if (entry == null) return NotFound(new { message = "时间记录不存在" });
        return Ok(entry);
    }

    /// <summary>创建手动时间记录</summary>
    [HttpPost]
    [Permission("time-tracking.manage")]
    public async Task<IActionResult> Create([FromBody] CreateTimeEntryRequest request)
    {
        try
        {
            var entry = await _timeEntryService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = entry.Id }, entry);
        }
        catch (InvalidOperationException ex)
        {
            // TT-024: timer 类型已有活跃计时器时返回 400 而非 500
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>更新时间记录</summary>
    /// <param name="id">时间记录 ID</param>
    /// <param name="request">更新请求体</param>
    /// <param name="ifMatch">可选：ETag（Base64 RowVersion）用于并发冲突检测，冲突返回 409</param>
    [HttpPut("{id:int}")]
    [Permission("time-tracking.manage")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTimeEntryRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        // HTTP 标准优先：If-Match 请求头覆盖 body.ifMatch
        if (!string.IsNullOrEmpty(ifMatch)) request.IfMatch = ifMatch;
        try
        {
            var entry = await _timeEntryService.UpdateAsync(id, request, ifMatch);
            if (entry == null) return NotFound(new { message = "时间记录不存在" });
            return Ok(entry);
        }
        catch (NoneadCSS.Application.Exceptions.ConcurrencyException ex)
        {
            return Conflict(new
            {
                message = ex.Message,
                entityType = ex.EntityType,
                entityId = ex.EntityId,
                latest = ex.Latest
            });
        }
    }

    /// <summary>删除时间记录（软删除）</summary>
    [HttpDelete("{id:int}")]
    [Permission("time-tracking.manage")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _timeEntryService.DeleteAsync(id);
        if (!result) return NotFound(new { message = "时间记录不存在" });
        return NoContent();
    }

    // ===== 计时器 =====

    /// <summary>开始计时器</summary>
    [HttpPost("timer/start")]
    [Permission("time-tracking.manage")]
    public async Task<IActionResult> StartTimer([FromBody] StartTimerRequest request)
    {
        try
        {
            var entry = await _timeEntryService.StartTimerAsync(request);
            return Ok(entry);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>停止计时器</summary>
    /// <param name="id">计时器记录 ID</param>
    /// <param name="ifMatch">可选：ETag（Base64 RowVersion）用于并发冲突检测，冲突返回 409</param>
    [HttpPost("timer/{id:int}/stop")]
    [Permission("time-tracking.manage")]
    public async Task<IActionResult> StopTimer(int id, [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        try
        {
            var entry = await _timeEntryService.StopTimerAsync(id, ifMatch);
            if (entry == null) return NotFound(new { message = "计时器不存在或已停止" });
            return Ok(entry);
        }
        catch (NoneadCSS.Application.Exceptions.ConcurrencyException ex)
        {
            return Conflict(new
            {
                message = ex.Message,
                entityType = ex.EntityType,
                entityId = ex.EntityId,
                latest = ex.Latest
            });
        }
    }

    /// <summary>获取当前运行中的计时器</summary>
    [HttpGet("timer/active")]
    public async Task<IActionResult> GetActiveTimer()
    {
        var entry = await _timeEntryService.GetActiveTimerAsync();
        return Ok(entry);
    }

    // ===== 统计与报表 =====

    /// <summary>获取概览统计</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview()
    {
        var overview = await _timeEntryService.GetOverviewAsync();
        return Ok(overview);
    }

    /// <summary>获取报表数据</summary>
    [HttpGet("report")]
    public async Task<IActionResult> GetReport([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var report = await _timeEntryService.GetReportAsync(startDate, endDate);
        return Ok(report);
    }

    /// <summary>导出CSV</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] TimeEntryQuery query)
    {
        var bytes = await _timeEntryService.ExportAsync(query);
        return File(bytes, "text/csv; charset=utf-8", $"time-entries-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // ===== 垃圾箱 =====

    /// <summary>获取垃圾箱列表</summary>
    [HttpGet("trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTrash([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null)
    {
        var (items, total) = await _timeEntryService.GetTrashAsync(page, pageSize, keyword);
        return Ok(new
        {
            data = items,
            totalItems = total,
            currentPage = page,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    /// <summary>恢复已删除的时间记录</summary>
    [HttpPost("{id:int}/restore")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Restore(int id)
    {
        var result = await _timeEntryService.RestoreAsync(id);
        if (!result) return NotFound(new { message = "时间记录不存在" });
        return Ok(new { message = "恢复成功" });
    }

    /// <summary>永久删除时间记录</summary>
    [HttpDelete("{id:int}/hard")]
    [RequireSuperAdmin]
    public async Task<IActionResult> HardDelete(int id)
    {
        var result = await _timeEntryService.HardDeleteAsync(id);
        if (!result) return NotFound(new { message = "时间记录不存在" });
        return NoContent();
    }

    /// <summary>清空垃圾箱</summary>
    [HttpPost("trash/clear")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ClearTrash()
    {
        var count = await _timeEntryService.ClearTrashAsync();
        return Ok(new { message = $"已清空 {count} 条记录", count });
    }
}
