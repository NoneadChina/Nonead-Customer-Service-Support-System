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
/// 角色权限控制器 - 管理系统角色与权限
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class RolesController : ControllerBase
{
    private readonly IRoleService _service;

    public RolesController(IRoleService service)
    {
        _service = service;
    }

    /// <summary>
    /// 获取角色列表（分页）
    /// </summary>
    [HttpGet]
    [RequireSuperAdmin]
    [OutputCache(PolicyName = "Roles")]
    public async Task<ActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetPagedAsync(new RoleQuery
        {
            Page = page,
            PageSize = pageSize,
            Keyword = keyword,
        });

        return Ok(new PagedResponse<RoleDto>
        {
            Data = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取角色详情
    /// </summary>
    [HttpGet("{id}")]
    [RequireSuperAdmin]
    [OutputCache(PolicyName = "Roles")]
    public async Task<ActionResult<RoleDto>> GetById(int id)
    {
        var result = await _service.GetByIdAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// 获取垃圾箱角色列表（分页）
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
        return Ok(new PagedResponse<RoleDto>
        {
            Data = items.ToList(),
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取垃圾箱角色数量
    /// </summary>
    [HttpGet("trash/count")]
    [RequireSuperAdmin]
    public async Task<ActionResult> GetTrashCount()
    {
        var count = await _service.GetTrashCountAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 获取角色概览
    /// </summary>
    [HttpGet("overview")]
    [RequireSuperAdmin]
    [OutputCache(PolicyName = "Roles")]
    public async Task<ActionResult<RoleOverviewResponse>> GetOverview()
    {
        var result = await _service.GetOverviewAsync();
        return Ok(result);
    }

    /// <summary>
    /// 获取所有权限分组定义
    /// </summary>
    [HttpGet("permissions")]
    [RequireSuperAdmin]
    [OutputCache(PolicyName = "Roles")]
    public ActionResult<List<PermissionGroup>> GetPermissions()
    {
        var result = _service.GetAllPermissionGroups();
        return Ok(result);
    }

    /// <summary>
    /// 创建角色
    /// </summary>
    [HttpPost]
    [RequireSuperAdmin]
    public async Task<ActionResult<RoleDto>> Create([FromBody] CreateRoleRequest request)
    {
        try
        {
            var result = await _service.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 更新角色
    /// </summary>
    [HttpPut("{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult<RoleDto>> Update(int id, [FromBody] UpdateRoleRequest request)
    {
        if (id != request.Id) return BadRequest("ID 不匹配");
        try
        {
            var result = await _service.UpdateAsync(request);
            if (result == null) return NotFound();
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 删除角色（移至垃圾箱）
    /// </summary>
    [HttpDelete("{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult> Delete(int id)
    {
        try
        {
            var result = await _service.DeleteAsync(id);
            if (!result) return NotFound();
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 恢复垃圾箱中的角色
    /// </summary>
    [HttpPost("{id}/restore")]
    [RequireSuperAdmin]
    public async Task<ActionResult> Restore(int id)
    {
        var result = await _service.RestoreAsync(id);
        if (!result) return NotFound(new { message = "角色不存在或未在垃圾箱中" });
        return NoContent();
    }

    /// <summary>
    /// 彻底删除角色（从垃圾箱永久删除）
    /// </summary>
    [HttpDelete("{id}/hard")]
    [RequireSuperAdmin]
    public async Task<ActionResult> HardDelete(int id)
    {
        var result = await _service.HardDeleteAsync(id);
        if (!result) return NotFound(new { message = "角色不存在或未在垃圾箱中" });
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
    /// 分配角色给用户
    /// </summary>
    [HttpPost("assign")]
    [RequireSuperAdmin]
    public async Task<ActionResult> AssignRole([FromBody] AssignRoleRequest request)
    {
        var result = await _service.AssignRoleAsync(request);
        return Ok(new { success = result });
    }

    /// <summary>
    /// 批量分配角色
    /// </summary>
    [HttpPost("batch-assign")]
    [RequireSuperAdmin]
    public async Task<ActionResult> BatchAssignRole([FromBody] BatchAssignRoleRequest request)
    {
        var result = await _service.BatchAssignRoleAsync(request);
        return Ok(new { success = result });
    }

    /// <summary>
    /// 移除用户角色分配
    /// </summary>
    [HttpDelete("assignments/{userRoleId}")]
    [RequireSuperAdmin]
    public async Task<ActionResult> RemoveRoleAssignment(int userRoleId)
    {
        var result = await _service.RemoveRoleAssignmentAsync(userRoleId);
        if (!result) return NotFound();
        return Ok();
    }

    /// <summary>
    /// 获取角色的用户列表
    /// </summary>
    /// <remarks>仅管理员可访问</remarks>
    [HttpGet("{id}/users")]
    [RequireSuperAdmin]
    public async Task<ActionResult<List<RoleUserDto>>> GetRoleUsers(int id)
    {
        var result = await _service.GetRoleUsersAsync(id);
        return Ok(result);
    }

    /// <summary>
    /// 获取用户的角色列表
    /// </summary>
    /// <remarks>仅管理员或本人可访问（防止 IDOR）</remarks>
    [HttpGet("user/{userId}")]
    [RequireSuperAdminOrSelf]
    public async Task<ActionResult<List<RoleDto>>> GetUserRoles(
        int userId,
        [FromQuery] int? mailboxId = null)
    {
        var result = await _service.GetUserRolesAsync(userId, mailboxId);
        return Ok(result);
    }

    /// <summary>
    /// 获取用户的权限列表
    /// </summary>
    /// <remarks>仅管理员或本人可访问（防止 IDOR）</remarks>
    [HttpGet("user/{userId}/permissions")]
    [RequireSuperAdminOrSelf]
    public async Task<ActionResult<List<string>>> GetUserPermissions(
        int userId,
        [FromQuery] int? mailboxId = null)
    {
        var result = await _service.GetUserPermissionsAsync(userId, mailboxId);
        return Ok(result);
    }

    /// <summary>
    /// 检查用户是否拥有某权限
    /// </summary>
    /// <remarks>仅管理员或本人可访问（防止 IDOR）</remarks>
    [HttpGet("user/{userId}/has-permission")]
    [RequireSuperAdminOrSelf]
    public async Task<ActionResult<bool>> HasPermission(
        int userId,
        [FromQuery] string permission,
        [FromQuery] int? mailboxId = null)
    {
        var result = await _service.UserHasPermissionAsync(userId, permission, mailboxId);
        return Ok(result);
    }
}
