using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace NoneadCSS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class SatisfactionRatingsController : ControllerBase
{
    private readonly ISatisfactionRatingService _service;

    public SatisfactionRatingsController(ISatisfactionRatingService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequireSuperAdmin]
    public async Task<ActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] int? rating = null,
        [FromQuery] int? mailboxId = null,
        [FromQuery] int? userId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetListAsync(
            page, pageSize, rating, mailboxId, userId, startDate, endDate, keyword);

        return Ok(new PagedResponse<SatisfactionRatingDto>
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
    public async Task<ActionResult<SatisfactionRatingDto>> GetById(int id)
    {
        var result = await _service.GetByIdAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("conversation/{conversationId}")]
    [RequireSuperAdmin]
    public async Task<ActionResult<SatisfactionRatingDto>> GetByConversation(int conversationId)
    {
        var result = await _service.GetByConversationAsync(conversationId);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("overview")]
    [RequireSuperAdmin]
    public async Task<ActionResult<SatisfactionOverviewResponse>> GetOverview(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int? mailboxId = null)
    {
        var result = await _service.GetOverviewAsync(startDate, endDate, mailboxId);
        return Ok(result);
    }

    [HttpGet("recent")]
    [RequireSuperAdmin]
    public async Task<ActionResult<List<SatisfactionRatingDto>>> GetRecent(
        [FromQuery] int count = 10,
        [FromQuery] int? rating = null)
    {
        // 限制返回数量上限，防止请求大量数据导致 DoS
        count = Math.Min(Math.Max(count, 1), PaginationHelper.MaxPageSize);
        var result = await _service.GetRecentAsync(count, rating);
        return Ok(result);
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("SatisfactionPolicy")]
    public async Task<ActionResult<SatisfactionRatingDto>> Create(
        [FromBody] CreateSatisfactionRatingRequest request)
    {
        // ModelState 校验已由 [ApiController] 自动处理

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers["User-Agent"].ToString();

        var result = await _service.CreateAsync(request, ipAddress, userAgent);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpDelete("{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult> Delete(int id)
    {
        var success = await _service.DeleteAsync(id);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpGet("rating-options")]
    [RequireSuperAdmin]
    public async Task<ActionResult<Dictionary<int, string>>> GetRatingOptions()
    {
        var options = await _service.GetRatingOptionsAsync();
        return Ok(options);
    }

    [HttpGet("validate/{token}")]
    [AllowAnonymous]
    [EnableRateLimiting("SatisfactionPolicy")]
    public async Task<ActionResult> ValidateToken(string token)
    {
        var result = await _service.ValidateTokenAsync(token);
        if (result == null)
        {
            return BadRequest(new { message = "无效的评价链接" });
        }
        return Ok(result);
    }
}
