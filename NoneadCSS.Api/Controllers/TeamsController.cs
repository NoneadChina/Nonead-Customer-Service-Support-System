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
/// 团队控制器 - 管理系统团队
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class TeamsController : ControllerBase
{
    private readonly ITeamService _service;

    public TeamsController(ITeamService service)
    {
        _service = service;
    }

    /// <summary>
    /// 获取团队列表（分页）
    /// </summary>
    [HttpGet]
    [RequireSuperAdmin]
    [OutputCache(PolicyName = "Teams")]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] int? mailboxId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var query = new TeamQuery
        {
            Page = page,
            PageSize = pageSize,
            MailboxId = mailboxId,
            IsActive = isActive,
            Keyword = keyword
        };

        var (items, total) = await _service.GetPagedAsync(query);
        return Ok(new PagedResponse<TeamDto>
        {
            Data = items.ToList(),
            TotalItems = total,
            CurrentPage = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    /// <summary>
    /// 获取团队详情
    /// </summary>
    [HttpGet("{id}")]
    [RequireSuperAdmin]
    [OutputCache(PolicyName = "Teams")]
    public async Task<IActionResult> GetById(int id)
    {
        var team = await _service.GetByIdAsync(id);
        if (team == null) return NotFound(new { message = "团队不存在" });
        return Ok(team);
    }

    /// <summary>
    /// 获取垃圾箱团队列表（分页）
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
        return Ok(new PagedResponse<TeamDto>
        {
            Data = items.ToList(),
            TotalItems = total,
            CurrentPage = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    /// <summary>
    /// 获取垃圾箱团队数量
    /// </summary>
    [HttpGet("trash/count")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTrashCount()
    {
        var count = await _service.GetTrashCountAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 创建团队
    /// </summary>
    [HttpPost]
    [RequireSuperAdmin]
    public async Task<IActionResult> Create([FromBody] CreateTeamRequest request)
    {
        try
        {
            var team = await _service.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = team.Id }, team);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 更新团队
    /// </summary>
    [HttpPut("{id}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTeamRequest request)
    {
        try
        {
            var team = await _service.UpdateAsync(id, request);
            if (team == null) return NotFound(new { message = "团队不存在" });
            return Ok(team);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
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

    /// <summary>
    /// 删除团队（移至垃圾箱）
    /// </summary>
    [HttpDelete("{id}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _service.DeleteAsync(id);
        if (!result) return NotFound(new { message = "团队不存在" });
        return NoContent();
    }

    /// <summary>
    /// 恢复垃圾箱中的团队
    /// </summary>
    [HttpPost("{id}/restore")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Restore(int id)
    {
        var result = await _service.RestoreAsync(id);
        if (!result) return NotFound(new { message = "团队不存在或未在垃圾箱中" });
        return NoContent();
    }

    /// <summary>
    /// 彻底删除团队（从垃圾箱永久删除）
    /// </summary>
    [HttpDelete("{id}/hard")]
    [RequireSuperAdmin]
    public async Task<IActionResult> HardDelete(int id)
    {
        var result = await _service.HardDeleteAsync(id);
        if (!result) return NotFound(new { message = "团队不存在或未在垃圾箱中" });
        return NoContent();
    }

    /// <summary>
    /// 获取团队概览统计
    /// </summary>
    [HttpGet("overview")]
    [RequireSuperAdmin]
    [OutputCache(PolicyName = "Teams")]
    public async Task<IActionResult> GetOverview()
    {
        var overview = await _service.GetOverviewAsync();
        return Ok(overview);
    }

    /// <summary>
    /// 按邮箱获取团队列表
    /// </summary>
    [HttpGet("mailbox/{mailboxId?}")]
    [OutputCache(PolicyName = "Teams")]
    public async Task<IActionResult> GetByMailbox(int? mailboxId = null)
    {
        var teams = await _service.GetByMailboxIdAsync(mailboxId);
        return Ok(teams);
    }

    /// <summary>
    /// 获取用户所属的团队
    /// </summary>
    [HttpGet("user/{userId}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetByUser(int userId)
    {
        var teams = await _service.GetByUserIdAsync(userId);
        return Ok(teams);
    }

    /// <summary>
    /// 获取团队成员列表
    /// </summary>
    [HttpGet("{id}/members")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetMembers(int id)
    {
        var members = await _service.GetMembersAsync(id);
        return Ok(members);
    }

    /// <summary>
    /// 添加团队成员
    /// </summary>
    [HttpPost("{id}/members")]
    [RequireSuperAdmin]
    public async Task<IActionResult> AddMember(int id, [FromBody] AddTeamMemberRequest request)
    {
        try
        {
            var member = await _service.AddMemberAsync(id, request);
            return Ok(member);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 更新成员角色
    /// </summary>
    [HttpPut("{id}/members/{userId}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> UpdateMemberRole(int id, int userId, [FromBody] UpdateTeamMemberRequest request)
    {
        var member = await _service.UpdateMemberRoleAsync(id, userId, request.Role);
        if (member == null) return NotFound(new { message = "成员不存在" });
        return Ok(member);
    }

    /// <summary>
    /// 移除团队成员
    /// </summary>
    [HttpDelete("{id}/members/{userId}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> RemoveMember(int id, int userId)
    {
        var result = await _service.RemoveMemberAsync(id, userId);
        if (!result) return NotFound(new { message = "成员不存在" });
        return NoContent();
    }
}
