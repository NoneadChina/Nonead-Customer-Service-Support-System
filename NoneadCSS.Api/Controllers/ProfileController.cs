using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 个人资料控制器 - 管理当前用户的个人信息与密码
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class ProfileController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILocalizationService _localizer;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRoleResolver _userRoleResolver;
    private readonly IAttachmentService _attachmentService;

    public ProfileController(IUserService userService, ILocalizationService localizer, IUnitOfWork unitOfWork, IUserRoleResolver userRoleResolver, IAttachmentService attachmentService)
    {
        _userService = userService;
        _localizer = localizer;
        _unitOfWork = unitOfWork;
        _userRoleResolver = userRoleResolver;
        _attachmentService = attachmentService;
    }

    /// <summary>
    /// 获取当前用户个人资料
    /// </summary>
    /// <returns>用户个人资料</returns>
    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var user = await _userService.GetUserByIdAsync(userId);

        if (user == null)
        {
            return NotFound(new { message = _localizer["UserNotFound"] });
        }

        // 填充细粒度权限键列表，供前端进行按钮级权限控制
        user.StringPermissions = await _userRoleResolver.GetPermissionsAsync(userId);

        return Ok(user);
    }

    /// <summary>
    /// 获取当前用户的个人统计信息
    /// </summary>
    /// <returns>参与会话数、已回复数、在线时长（分钟）</returns>
    [HttpGet("stats")]
    public async Task<IActionResult> GetProfileStats()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        if (userId <= 0)
        {
            return Unauthorized();
        }

        // 参与会话数：使用 CountAsync 在数据库端聚合，避免加载全量数据导致 OOM。
        var conversationCount = await _unitOfWork.Conversations
            .GetQueryable()
            .CountAsync(c => c.UserId == userId);

        // 已回复数：该用户创建的 Message 类型线程数（数据库端 COUNT）。
        var replyCount = await _unitOfWork.Threads
            .GetQueryable()
            .CountAsync(t => t.UserId == userId && t.Type == ThreadType.Message);

        // 在线时长：仅取最近 1000 条登录/登出审计日志用于累计，
        // 避免 int.MaxValue 全量加载。
        var (auditLogs, _) = await _unitOfWork.AuditLogs.GetByUserIdAsync(userId, 1, 1000);
        var onlineMinutes = CalculateOnlineMinutes(auditLogs);

        return Ok(new
        {
            conversationCount,
            replyCount,
            onlineMinutes
        });
    }

    /// <summary>
    /// 根据审计日志中的 login/logout 记录累计在线时长（分钟）。
    /// 逻辑：按时间排序后，将每次 login 与其后最近的一次 logout 配对，
    /// 缺少配对 logout 的 login 不计入（可能仍在登录或异常退出）。
    /// </summary>
    private static int CalculateOnlineMinutes(IEnumerable<Domain.Entities.AuditLog> logs)
    {
        var ordered = logs
            .Where(l => l.Action.Equals("login", StringComparison.OrdinalIgnoreCase)
                     || l.Action.Equals("logout", StringComparison.OrdinalIgnoreCase))
            .OrderBy(l => l.CreatedAt)
            .ToList();

        var totalMinutes = 0.0;
        DateTime? pendingLogin = null;

        foreach (var log in ordered)
        {
            if (log.Action.Equals("login", StringComparison.OrdinalIgnoreCase))
            {
                pendingLogin = log.CreatedAt;
            }
            else if (log.Action.Equals("logout", StringComparison.OrdinalIgnoreCase) && pendingLogin.HasValue)
            {
                totalMinutes += (log.CreatedAt - pendingLogin.Value).TotalMinutes;
                pendingLogin = null;
            }
        }

        // 若当前仍有未配对的 login，说明用户可能仍在线，
        // 将从该 login 到当前时间的时长一并计入
        if (pendingLogin.HasValue)
        {
            totalMinutes += (DateTime.UtcNow - pendingLogin.Value).TotalMinutes;
        }

        return (int)Math.Max(0, totalMinutes);
    }

    /// <summary>
    /// 更新当前用户个人资料
    /// </summary>
    /// <param name="request">更新个人资料请求</param>
    /// <returns>更新后的用户资料</returns>
    /// <response code="200">更新成功</response>
    /// <response code="404">用户不存在</response>
    [HttpPut]
    [Permission("profile.update")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var user = await _userService.UpdateProfileAsync(userId, request);

        if (user == null)
        {
            return NotFound(new { message = _localizer["UserNotFound"] });
        }

        // 回填细粒度权限键，避免前端覆盖缓存后丢失权限
        user.StringPermissions = await _userRoleResolver.GetPermissionsAsync(userId);

        return Ok(user);
    }

    /// <summary>
    /// 上传当前用户头像
    /// </summary>
    /// <param name="file">头像图片文件（jpg/png/webp 等，由 IAttachmentService 校验类型与大小）</param>
    /// <returns>包含新头像公开 URL 的对象，以及更新后的用户资料</returns>
    /// <response code="200">上传成功</response>
    /// <response code="400">文件为空或校验失败</response>
    /// <response code="404">用户不存在</response>
    [HttpPost("avatar")]
    [RequestSizeLimit(5 * 1024 * 1024)] // 头像上限 5MB（与客户头像保持一致）
    [Permission("profile.update")]
    public async Task<IActionResult> UploadAvatar(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "请选择要上传的头像文件" });
        }

        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        try
        {
            // 复用通用附件服务上传（含类型/大小校验、病毒扫描、磁盘持久化）
            var attachment = await _attachmentService.UploadAsync(file, null, userId);
            var publicUrl = $"/api/attachments/{attachment.Id}/public-preview";

            // 读取当前用户资料，仅更新 Avatar 字段（保留其他字段）
            var currentUser = await _userService.GetUserByIdAsync(userId);
            if (currentUser == null)
            {
                return NotFound(new { message = _localizer["UserNotFound"] });
            }

            var finalUser = await _userService.UpdateProfileAsync(userId, new UpdateProfileRequest
            {
                FirstName = currentUser.FirstName,
                LastName = currentUser.LastName,
                Timezone = currentUser.Timezone,
                Locale = currentUser.Locale,
                Avatar = publicUrl
            });

            // 回填细粒度权限键，避免前端覆盖缓存后丢失权限
            if (finalUser != null)
            {
                finalUser.StringPermissions = await _userRoleResolver.GetPermissionsAsync(userId);
            }

            return Ok(new
            {
                url = publicUrl,
                user = finalUser
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 移除当前用户头像
    /// </summary>
    /// <returns>更新后的用户资料</returns>
    /// <response code="200">移除成功</response>
    /// <response code="404">用户不存在</response>
    [HttpDelete("avatar")]
    [Permission("profile.update")]
    public async Task<IActionResult> RemoveAvatar()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var currentUser = await _userService.GetUserByIdAsync(userId);
        if (currentUser == null)
        {
            return NotFound(new { message = _localizer["UserNotFound"] });
        }

        var updatedUser = await _userService.UpdateProfileAsync(userId, new UpdateProfileRequest
        {
            FirstName = currentUser.FirstName,
            LastName = currentUser.LastName,
            Timezone = currentUser.Timezone,
            Locale = currentUser.Locale,
            Avatar = null // 清除头像
        });

        // 回填细粒度权限键，避免前端覆盖缓存后丢失权限
        if (updatedUser != null)
        {
            updatedUser.StringPermissions = await _userRoleResolver.GetPermissionsAsync(userId);
        }

        return Ok(new { user = updatedUser });
    }

    /// <summary>
    /// 修改当前用户密码
    /// </summary>
    /// <param name="request">修改密码请求</param>
    /// <returns>操作结果</returns>
    /// <response code="200">密码修改成功</response>
    /// <response code="400">密码验证失败、两次密码不一致或新密码不符合策略</response>
    [HttpPost("change-password")]
    [Permission("profile.change_password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        try
        {
            var result = await _userService.ChangePasswordAsync(userId, request);

            if (!result)
            {
                return BadRequest(new { message = _localizer["InvalidCredentials"] });
            }

            return Ok(new { message = "密码修改成功" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
