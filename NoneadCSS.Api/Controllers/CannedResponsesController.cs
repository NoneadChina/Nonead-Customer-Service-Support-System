using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using System.Security.Claims;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 快捷回复模板控制器 - 管理客服回复模板和宏
/// </summary>
[Route("api/[controller]")]
[RequireModule("core")]
public class CannedResponsesController : BaseController
{
    private readonly ICannedResponseService _service;

    public CannedResponsesController(ICannedResponseService service)
    {
        _service = service;
    }

    private int CurrentUserRole => int.Parse(User.FindFirst(ClaimTypes.Role)?.Value ?? "0");
    private bool IsAdmin => CurrentUserRole == (int)UserRoleType.SuperAdmin;

    /// <summary>
    /// 获取模板列表
    /// </summary>
    /// <param name="mailboxId">邮箱 ID（可选，不传返回全局+当前邮箱）</param>
    /// <param name="category">分类筛选（可选）</param>
    /// <returns>模板列表</returns>
    [HttpGet]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetList(
        [FromQuery] int? mailboxId = null,
        [FromQuery] string? category = null)
    {
        var list = await _service.GetListAsync(mailboxId, CurrentUserId ?? 0, category);
        return Ok(list);
    }

    /// <summary>
    /// 获取模板详情
    /// </summary>
    /// <param name="id">模板 ID</param>
    /// <returns>模板详情</returns>
    [HttpGet("{id}")]
    [RequireSuperAdmin]
    [OutputCache(PolicyName = "CannedResponses")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _service.GetByIdAsync(id);
        if (item == null)
        {
            return NotFound(new { message = "模板不存在" });
        }
        return Ok(item);
    }

    /// <summary>
    /// 搜索模板
    /// </summary>
    /// <param name="keyword">搜索关键词</param>
    /// <param name="mailboxId">邮箱 ID（可选）</param>
    /// <returns>匹配的模板列表</returns>
    [HttpGet("search")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Search(
        [FromQuery] string keyword,
        [FromQuery] int? mailboxId = null)
    {
        var list = await _service.SearchAsync(keyword, mailboxId, CurrentUserId ?? 0);
        return Ok(list);
    }

    /// <summary>
    /// 获取所有分类
    /// </summary>
    /// <param name="mailboxId">邮箱 ID（可选）</param>
    /// <returns>分类名称列表</returns>
    [HttpGet("categories")]
    [OutputCache(PolicyName = "CannedResponses")]
    public async Task<IActionResult> GetCategories([FromQuery] int? mailboxId = null)
    {
        var categories = await _service.GetCategoriesAsync(mailboxId);
        return Ok(categories);
    }

    /// <summary>
    /// 创建模板
    /// </summary>
    /// <param name="request">创建请求</param>
    /// <returns>新创建的模板</returns>
    [HttpPost]
    [RequireSuperAdmin]
    public async Task<IActionResult> Create([FromBody] CreateCannedResponseRequest request)
    {
        var item = await _service.CreateAsync(request, CurrentUserId ?? 0);
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
    }

    /// <summary>
    /// 更新模板
    /// </summary>
    /// <param name="id">模板 ID</param>
    /// <param name="request">更新请求</param>
    /// <returns>更新后的模板</returns>
    [HttpPut("{id}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCannedResponseRequest request)
    {
        var item = await _service.UpdateAsync(id, request, CurrentUserId ?? 0, IsAdmin);
        if (item == null)
        {
            return NotFound(new { message = "模板不存在或无权限修改" });
        }
        return Ok(item);
    }

    /// <summary>
    /// 删除模板（移至垃圾箱）
    /// </summary>
    /// <param name="id">模板 ID</param>
    /// <returns>无内容</returns>
    [HttpDelete("{id}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _service.DeleteAsync(id, CurrentUserId ?? 0, IsAdmin);
        if (!result)
        {
            return NotFound(new { message = "模板不存在或无权限删除" });
        }
        return NoContent();
    }

    /// <summary>
    /// 获取垃圾箱列表（分页）
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
        return Ok(new PagedResponse<CannedResponseDto>
        {
            Data = items.ToList(),
            TotalItems = total,
            CurrentPage = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    /// <summary>
    /// 获取垃圾箱中的模板数量
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
    /// 恢复垃圾箱中的模板
    /// </summary>
    /// <param name="id">模板 ID</param>
    /// <returns>无内容</returns>
    [HttpPost("{id:int}/restore")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Restore(int id)
    {
        var result = await _service.RestoreAsync(id, CurrentUserId ?? 0, IsAdmin);
        if (!result)
        {
            return NotFound(new { message = "模板不存在或未在垃圾箱中" });
        }
        return NoContent();
    }

    /// <summary>
    /// 彻底删除模板（从垃圾箱永久删除）
    /// </summary>
    /// <param name="id">模板 ID</param>
    /// <returns>无内容</returns>
    [HttpDelete("{id:int}/hard")]
    [RequireSuperAdmin]
    public async Task<IActionResult> HardDelete(int id)
    {
        var result = await _service.HardDeleteAsync(id, CurrentUserId ?? 0, IsAdmin);
        if (!result)
        {
            return NotFound(new { message = "模板不存在或未在垃圾箱中" });
        }
        return NoContent();
    }

    /// <summary>
    /// 清空垃圾箱（仅管理员可操作）
    /// </summary>
    /// <returns>删除的模板数量</returns>
    [HttpDelete("trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ClearTrash()
    {
        // 仅管理员可清空垃圾箱
        if (!IsAdmin)
        {
            return Forbid();
        }
        var count = await _service.ClearTrashAsync();
        return Ok(new { count });
    }
}
