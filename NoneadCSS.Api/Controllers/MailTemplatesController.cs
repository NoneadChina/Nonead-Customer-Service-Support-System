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
/// 邮件模板控制器 - 管理系统邮件模板
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class MailTemplatesController : ControllerBase
{
    private readonly IMailTemplateService _service;

    public MailTemplatesController(IMailTemplateService service)
    {
        _service = service;
    }

    /// <summary>
    /// 获取邮件模板列表（分页）
    /// </summary>
    /// <param name="page">页码</param>
    /// <param name="pageSize">每页条数</param>
    /// <param name="type">模板类型</param>
    /// <param name="mailboxId">邮箱ID</param>
    /// <param name="isActive">是否启用</param>
    /// <param name="keyword">搜索关键词</param>
    /// <returns>模板列表</returns>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] int? type = null,
        [FromQuery] int? mailboxId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var query = new MailTemplateQuery
        {
            Page = page,
            PageSize = pageSize,
            Type = type.HasValue ? (Domain.Enums.MailTemplateType)type.Value : null,
            MailboxId = mailboxId,
            IsActive = isActive,
            Keyword = keyword
        };

        var (items, total) = await _service.GetPagedAsync(query);
        return Ok(new PagedResponse<MailTemplateResponse>
        {
            Data = items.ToList(),
            TotalItems = total,
            CurrentPage = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    /// <summary>
    /// 获取模板详情
    /// </summary>
    /// <param name="id">模板ID</param>
    /// <returns>模板详情</returns>
    [HttpGet("{id:int}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetById(int id)
    {
        var template = await _service.GetByIdAsync(id);
        if (template == null)
        {
            return NotFound(new { message = "模板不存在" });
        }
        return Ok(template);
    }

    /// <summary>
    /// 根据标识获取模板
    /// </summary>
    /// <param name="slug">模板标识</param>
    /// <returns>模板详情</returns>
    [HttpGet("slug/{slug}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetBySlug(string slug)
    {
        var template = await _service.GetBySlugAsync(slug);
        if (template == null)
        {
            return NotFound(new { message = "模板不存在" });
        }
        return Ok(template);
    }

    /// <summary>
    /// 获取垃圾箱模板列表（分页）
    /// </summary>
    /// <param name="page">页码</param>
    /// <param name="pageSize">每页条数</param>
    /// <param name="keyword">搜索关键词</param>
    /// <returns>已删除的模板列表</returns>
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
        return Ok(new PagedResponse<MailTemplateResponse>
        {
            Data = items.ToList(),
            TotalItems = total,
            CurrentPage = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    /// <summary>
    /// 获取垃圾箱模板数量
    /// </summary>
    /// <returns>数量</returns>
    [HttpGet("trash/count")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTrashCount()
    {
        var count = await _service.GetTrashCountAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 创建邮件模板
    /// </summary>
    /// <param name="request">创建请求</param>
    /// <returns>新创建的模板</returns>
    [HttpPost]
    [RequireSuperAdmin]
    public async Task<IActionResult> Create([FromBody] CreateMailTemplateRequest request)
    {
        try
        {
            var template = await _service.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = template.Id }, template);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 更新邮件模板
    /// </summary>
    /// <param name="id">模板ID</param>
    /// <param name="request">更新请求</param>
    /// <returns>更新后的模板</returns>
    [HttpPut("{id:int}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateMailTemplateRequest request)
    {
        try
        {
            var template = await _service.UpdateAsync(id, request);
            if (template == null)
            {
                return NotFound(new { message = "模板不存在" });
            }
            return Ok(template);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 删除邮件模板（移至垃圾箱）
    /// </summary>
    /// <param name="id">模板ID</param>
    /// <returns>无内容</returns>
    [HttpDelete("{id:int}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var result = await _service.DeleteAsync(id);
            if (!result)
            {
                return NotFound(new { message = "模板不存在" });
            }
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 恢复垃圾箱中的模板
    /// </summary>
    /// <param name="id">模板ID</param>
    /// <returns>无内容</returns>
    [HttpPost("{id:int}/restore")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Restore(int id)
    {
        var result = await _service.RestoreAsync(id);
        if (!result)
        {
            return NotFound(new { message = "模板不存在或未在垃圾箱中" });
        }
        return NoContent();
    }

    /// <summary>
    /// 彻底删除模板（从垃圾箱永久删除）
    /// </summary>
    /// <param name="id">模板ID</param>
    /// <returns>无内容</returns>
    [HttpDelete("{id:int}/hard")]
    [RequireSuperAdmin]
    public async Task<IActionResult> HardDelete(int id)
    {
        var result = await _service.HardDeleteAsync(id);
        if (!result)
        {
            return NotFound(new { message = "模板不存在或未在垃圾箱中" });
        }
        return NoContent();
    }

    /// <summary>
    /// 清空垃圾箱
    /// </summary>
    /// <returns>删除的模板数量</returns>
    [HttpDelete("trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ClearTrash()
    {
        var count = await _service.ClearTrashAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 获取模板概览统计
    /// </summary>
    /// <returns>概览数据</returns>
    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview()
    {
        var overview = await _service.GetOverviewAsync();
        return Ok(overview);
    }

    /// <summary>
    /// 预览模板渲染结果
    /// </summary>
    /// <param name="request">预览请求</param>
    /// <returns>渲染后的模板</returns>
    [HttpPost("preview")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Preview([FromBody] PreviewMailTemplateRequest request)
    {
        var result = await _service.PreviewAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// 预览指定邮件模板的渲染结果
    /// </summary>
    /// <param name="id">模板 ID</param>
    /// <param name="request">预览请求</param>
    /// <returns>渲染后的模板</returns>
    /// <response code="200">预览成功</response>
    /// <response code="404">模板不存在</response>
    [HttpPost("{id:int}/preview")]
    [RequireSuperAdmin]
    public async Task<IActionResult> PreviewTemplate(int id, [FromBody] MailTemplatePreviewRequest request)
    {
        var result = await _service.PreviewTemplateAsync(id, request);
        if (result == null)
        {
            return NotFound(new { message = "模板不存在" });
        }
        return Ok(result);
    }

    /// <summary>
    /// 获取内置变量列表
    /// </summary>
    /// <returns>变量列表</returns>
    [HttpGet("variables/built-in")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetBuiltInVariables()
    {
        var variables = await _service.GetBuiltInVariablesAsync();
        return Ok(variables);
    }
}
