using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 邮件签名控制器 - 管理系统邮件签名
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class EmailSignaturesController : ControllerBase
{
    private readonly IEmailSignatureService _service;

    public EmailSignaturesController(IEmailSignatureService service)
    {
        _service = service;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim != null && int.TryParse(claim.Value, out var userId))
        {
            return userId;
        }
        return 1;
    }

    /// <summary>
    /// 获取签名列表（分页）
    /// </summary>
    [HttpGet]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] int? userId = null,
        [FromQuery] int? mailboxId = null,
        [FromQuery] bool? isDefault = null,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var query = new EmailSignatureQuery
        {
            Page = page,
            PageSize = pageSize,
            UserId = userId,
            MailboxId = mailboxId,
            IsDefault = isDefault,
            Keyword = keyword
        };

        var (items, total) = await _service.GetPagedAsync(query);
        return Ok(new PagedResponse<EmailSignatureDto>
        {
            Data = items.ToList(),
            TotalItems = total,
            CurrentPage = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    /// <summary>
    /// 获取签名详情
    /// </summary>
    [HttpGet("{id}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetById(int id)
    {
        var sig = await _service.GetByIdAsync(id);
        if (sig == null) return NotFound(new { message = "签名不存在" });
        return Ok(sig);
    }

    /// <summary>
    /// 创建签名
    /// </summary>
    [HttpPost]
    [RequireSuperAdmin]
    public async Task<IActionResult> Create([FromBody] CreateEmailSignatureRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var sig = await _service.CreateAsync(userId, request);
            return CreatedAtAction(nameof(GetById), new { id = sig.Id }, sig);
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { message = "操作失败，请重试" });
        }
    }

    /// <summary>
    /// 更新签名
    /// </summary>
    [HttpPut("{id}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateEmailSignatureRequest request)
    {
        try
        {
            var sig = await _service.UpdateAsync(id, request);
            if (sig == null) return NotFound(new { message = "签名不存在" });
            return Ok(sig);
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { message = "操作失败，请重试" });
        }
    }

    /// <summary>
    /// 删除签名
    /// </summary>
    [HttpDelete("{id}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _service.DeleteAsync(id);
        if (!result) return NotFound(new { message = "签名不存在" });
        return NoContent();
    }

    /// <summary>
    /// 获取概览统计
    /// </summary>
    [HttpGet("overview")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetOverview()
    {
        var overview = await _service.GetOverviewAsync();
        return Ok(overview);
    }

    /// <summary>
    /// 获取当前用户的签名列表
    /// </summary>
    [HttpGet("my")]
    public async Task<IActionResult> GetMySignatures([FromQuery] int? mailboxId = null)
    {
        var userId = GetCurrentUserId();
        var sigs = await _service.GetByUserIdAsync(userId, mailboxId);
        return Ok(sigs);
    }

    /// <summary>
    /// 获取指定用户的签名列表
    /// </summary>
    [HttpGet("user/{userId}")]
    [RequireSuperAdminOrSelf]
    public async Task<IActionResult> GetByUser(int userId, [FromQuery] int? mailboxId = null)
    {
        var sigs = await _service.GetByUserIdAsync(userId, mailboxId);
        return Ok(sigs);
    }

    /// <summary>
    /// 获取当前用户的默认签名
    /// </summary>
    [HttpGet("my/default")]
    public async Task<IActionResult> GetMyDefault([FromQuery] int? mailboxId = null)
    {
        var userId = GetCurrentUserId();
        var sig = await _service.GetDefaultAsync(userId, mailboxId);
        if (sig == null) return NotFound(new { message = "无默认签名" });
        return Ok(sig);
    }

    /// <summary>
    /// 设置默认签名
    /// </summary>
    [HttpPost("{id}/set-default")]
    [RequireSuperAdmin]
    public async Task<IActionResult> SetDefault(int id)
    {
        var userId = GetCurrentUserId();
        var result = await _service.SetDefaultAsync(userId, id);
        if (!result) return NotFound(new { message = "签名不存在" });
        return Ok(new { message = "已设为默认" });
    }

    /// <summary>
    /// 获取垃圾箱中的签名列表（分页）
    /// </summary>
    [HttpGet("trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTrash(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetTrashAsync(page, pageSize, keyword);
        return Ok(new PagedResponse<EmailSignatureDto>
        {
            Data = items.ToList(),
            TotalItems = total,
            CurrentPage = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    /// <summary>
    /// 获取垃圾箱中的签名数量
    /// </summary>
    [HttpGet("trash/count")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTrashCount()
    {
        var count = await _service.GetTrashCountAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 恢复垃圾箱中的签名
    /// </summary>
    [HttpPost("{id:int}/restore")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Restore(int id)
    {
        var result = await _service.RestoreAsync(id);
        if (!result) return NotFound(new { message = "签名不存在或未在垃圾箱中" });
        return NoContent();
    }

    /// <summary>
    /// 彻底删除垃圾箱中的签名（物理删除）
    /// </summary>
    [HttpDelete("{id:int}/hard")]
    [RequireSuperAdmin]
    public async Task<IActionResult> HardDelete(int id)
    {
        var result = await _service.HardDeleteAsync(id);
        if (!result) return NotFound(new { message = "签名不存在或未在垃圾箱中" });
        return NoContent();
    }

    /// <summary>
    /// 清空垃圾箱
    /// </summary>
    [HttpDelete("trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ClearTrash()
    {
        var count = await _service.ClearTrashAsync();
        return Ok(new { count });
    }
}
