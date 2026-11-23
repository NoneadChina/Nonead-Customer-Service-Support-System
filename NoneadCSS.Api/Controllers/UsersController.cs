using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 用户管理控制器 - 管理系统用户（增删改查、状态管理、角色分配）
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class UsersController : ControllerBase
{
    // 写操作（创建/更新/删除）需 SuperAdmin（角色 2），在各 Action 上以 [Authorize(Roles="2")] 标注
    private readonly IUserService _userService;
    private readonly ILocalizationService _localizer;

    public UsersController(IUserService userService, ILocalizationService localizer)
    {
        _userService = userService;
        _localizer = localizer;
    }

    /// <summary>
    /// 获取用户列表（分页）
    /// </summary>
    /// <returns>用户列表</returns>
    [HttpGet]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] string? keyword = null,
        [FromQuery] int? status = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _userService.GetPagedUsersAsync(page, pageSize, keyword, status);
        return Ok(new PagedResponse<UserResponse>
        {
            Data = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取可被分配会话的活跃用户列表（不分页）。
    /// 供会话/工单的"分配负责人"下拉框使用：拥有 conversations.assign 权限的客服主管/客服
    /// 无需 SuperAdmin 即可获取可分配的活跃客服列表，修复客服主管分配会话时下拉框为空的问题（T000061）。
    /// 仅返回 Status=Active 的用户，避免分配给已禁用/待激活账户。
    /// </summary>
    /// <returns>可分配用户列表</returns>
    [HttpGet("assignable")]
    [Permission("conversations.assign")]
    public async Task<IActionResult> GetAssignableUsers()
    {
        var users = await _userService.GetAssignableUsersAsync();
        return Ok(users);
    }

    /// <summary>
    /// 获取用户统计数据
    /// </summary>
    /// <returns>用户统计数据</returns>
    [HttpGet("stats")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetStats()
    {
        var stats = await _userService.GetStatsAsync();
        return Ok(new
        {
            total = stats.Total,
            active = stats.Active,
            disabled = stats.Disabled,
            pending = stats.Pending,
        });
    }

    /// <summary>
    /// 根据 ID 获取用户详情
    /// </summary>
    /// <param name="id">用户 ID</param>
    /// <returns>用户详情</returns>
    /// <response code="200">返回用户详情</response>
    /// <response code="403">无权限访问该用户信息</response>
    /// <response code="404">用户不存在</response>
    [HttpGet("{id}")]
    [RequireSuperAdminOrSelf]
    public async Task<IActionResult> GetUser(int id)
    {
        var user = await _userService.GetUserByIdAsync(id);

        if (user == null)
        {
            return NotFound(new { message = _localizer["UserNotFound"] });
        }

        return Ok(user);
    }

    /// <summary>
    /// 创建新用户
    /// </summary>
    /// <param name="request">创建用户请求</param>
    /// <returns>新创建的用户</returns>
    /// <response code="201">用户创建成功</response>
    /// <response code="400">邮箱已存在或密码不符合策略</response>
    [HttpPost]
    [RequireSuperAdmin]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        try
        {
            var user = await _userService.CreateUserAsync(request);

            if (user == null)
            {
                return BadRequest(new { message = _localizer["EmailAlreadyExists"] });
            }

            return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 更新用户信息
    /// </summary>
    /// <param name="id">用户 ID</param>
    /// <param name="request">更新用户请求</param>
    /// <returns>更新后的用户</returns>
    /// <response code="200">用户更新成功</response>
    /// <response code="400">邮箱已存在或密码不符合策略</response>
    /// <response code="404">用户不存在</response>
    [HttpPut("{id}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        try
        {
            var user = await _userService.UpdateUserAsync(id, request, ifMatch);

            if (user == null)
            {
                var existingUser = await _userService.GetUserByIdAsync(id);
                if (existingUser == null)
                {
                    return NotFound(new { message = _localizer["UserNotFound"] });
                }
                return BadRequest(new { message = _localizer["EmailAlreadyExists"] });
            }

            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 删除用户（软删除，移入垃圾箱）
    /// </summary>
    /// <param name="id">用户 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">删除成功</response>
    /// <response code="404">用户不存在</response>
    [HttpDelete("{id}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var (success, errorMessage) = await _userService.DeleteUserAsync(id);

        if (!success && errorMessage != null)
        {
            return BadRequest(new { message = errorMessage });
        }

        if (!success)
        {
            return NotFound(new { message = _localizer["UserNotFound"] });
        }

        return NoContent();
    }

    /// <summary>
    /// 获取垃圾箱中的用户列表
    /// </summary>
    /// <returns>已删除的用户列表</returns>
    [HttpGet("trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTrash()
    {
        var users = await _userService.GetDeletedUsersAsync();
        return Ok(users);
    }

    /// <summary>
    /// 从垃圾箱恢复用户
    /// </summary>
    /// <param name="id">用户 ID</param>
    /// <returns>恢复后的用户</returns>
    [HttpPost("{id}/restore")]
    [RequireSuperAdmin]
    public async Task<IActionResult> RestoreUser(int id)
    {
        var user = await _userService.RestoreUserAsync(id);
        if (user == null)
        {
            return NotFound(new { message = _localizer["UserNotFound"] });
        }
        return Ok(user);
    }

    /// <summary>
    /// 彻底删除用户（从数据库永久删除）
    /// </summary>
    /// <param name="id">用户 ID</param>
    /// <returns>无内容</returns>
    [HttpDelete("{id}/hard")]
    [RequireSuperAdmin]
    public async Task<IActionResult> HardDeleteUser(int id)
    {
        var result = await _userService.HardDeleteUserAsync(id);
        if (!result)
        {
            return NotFound(new { message = _localizer["UserNotFound"] });
        }
        return NoContent();
    }

    /// <summary>
    /// 清空垃圾箱（永久删除所有已软删除的用户）
    /// </summary>
    /// <returns>清空的用户数量</returns>
    [HttpDelete("trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ClearTrash()
    {
        var count = await _userService.ClearTrashAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 激活用户（将状态从待激活改为活跃）
    /// </summary>
    /// <param name="id">用户 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">激活成功</response>
    /// <response code="404">用户不存在</response>
    [HttpPost("{id}/activate")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ActivateUser(int id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        if (user == null)
        {
            return NotFound(new { message = _localizer["UserNotFound"] });
        }

        await _userService.ActivateUserAsync(id);
        return NoContent();
    }

    /// <summary>
    /// 修改用户状态
    /// </summary>
    /// <param name="id">用户 ID</param>
    /// <param name="request">状态请求</param>
    /// <returns>无内容</returns>
    /// <response code="204">修改成功</response>
    /// <response code="404">用户不存在</response>
    [HttpPost("{id}/status")]
    [RequireSuperAdmin]
    public async Task<IActionResult> UpdateUserStatus(int id, [FromBody] UpdateUserStatusRequest request)
    {
        var result = await _userService.UpdateUserStatusAsync(id, (Domain.Enums.UserStatus)request.Status);
        if (!result)
        {
            return NotFound(new { message = _localizer["UserNotFound"] });
        }

        return NoContent();
    }
}
