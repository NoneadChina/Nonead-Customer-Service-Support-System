using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 用户个人资料和权限管理控制器
/// 对应 PHP: app/Http/Controllers/UsersController.php (profile, permissions, notifications, password)
/// </summary>
[ApiController]
[Authorize]
[RequireModule("core")]
[Route("api/user-profile")]
public class UserProfileController : ControllerBase
{
    private readonly IUserService _userService;

    public UserProfileController(IUserService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// 获取个人资料
    /// 对应 PHP: UsersController@profile (GET)
    /// </summary>
    [HttpGet("me/profile")]
    public async Task<IActionResult> GetMyProfile()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var user = await _userService.GetUserByIdAsync(userId.Value);
        if (user == null) return NotFound();
        return Ok(user);
    }

    /// <summary>
    /// 更新个人资料
    /// 对应 PHP: UsersController@profile (POST)
    /// </summary>
    [HttpPut("me/profile")]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var user = await _userService.UpdateProfileAsync(userId.Value, request);
        if (user == null) return NotFound();
        return Ok(user);
    }

    /// <summary>
    /// 修改密码
    /// 对应 PHP: UsersController@password (POST)
    /// </summary>
    [HttpPost("me/password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var success = await _userService.ChangePasswordAsync(userId.Value, request);
        if (!success) return BadRequest(new { error = "密码修改失败" });
        return Ok();
    }

    /// <summary>
    /// 获取用户权限
    /// 对应 PHP: UsersController@permissions (GET)
    /// </summary>
    [HttpGet("{userId:int}/permissions")]
    [RequireSuperAdminOrSelf]
    public async Task<IActionResult> GetUserPermissions(int userId)
    {
        var permissions = await _userService.GetUserPermissionsAsync(userId);
        if (permissions == null) return NotFound();
        return Ok(permissions);
    }

    /// <summary>
    /// 更新用户权限
    /// 对应 PHP: UsersController@permissions (POST)
    /// </summary>
    [HttpPut("{userId:int}/permissions")]
    [RequireSuperAdmin]
    public async Task<IActionResult> UpdateUserPermissions(int userId, [FromBody] UserPermissionsRequest request)
    {
        var permissions = await _userService.UpdateUserPermissionsAsync(userId, request);
        if (permissions == null) return NotFound();
        return Ok(permissions);
    }

    /// <summary>
    /// 获取用户通知设置
    /// 对应 PHP: UsersController@notifications (GET)
    /// </summary>
    [HttpGet("me/notifications")]
    public async Task<IActionResult> GetMyNotificationSettings()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var settings = await _userService.GetUserNotificationSettingsAsync(userId.Value);
        if (settings == null) return NotFound();
        return Ok(settings);
    }

    /// <summary>
    /// 更新用户通知设置
    /// 对应 PHP: UsersController@notifications (POST)
    /// </summary>
    [HttpPut("me/notifications")]
    public async Task<IActionResult> UpdateMyNotificationSettings([FromBody] UserNotificationSettingsRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var settings = await _userService.UpdateUserNotificationSettingsAsync(userId.Value, request);
        if (settings == null) return NotFound();
        return Ok(settings);
    }

    private int? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId))
        {
            return userId;
        }
        return null;
    }

    /// <summary>
    /// 判断当前用户是否为超级管理员
    /// </summary>
    private bool IsSuperAdmin()
    {
        var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
        return roleClaim == "2";
    }
}
