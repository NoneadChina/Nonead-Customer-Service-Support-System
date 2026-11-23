using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 实时通信控制器
/// 对应 PHP: app/Http/Controllers/RealtimeController.php
/// </summary>
[ApiController]
[Authorize]
[RequireModule("core")]
[Route("api/realtime")]
public class RealtimeController : ControllerBase
{
    private readonly INotificationHubService _hubService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRoleResolver _userRoleResolver;

    public RealtimeController(INotificationHubService hubService, IUnitOfWork unitOfWork, IUserRoleResolver userRoleResolver)
    {
        _hubService = hubService;
        _unitOfWork = unitOfWork;
        _userRoleResolver = userRoleResolver;
    }

    /// <summary>
    /// 通知其他用户当前用户正在查看某个会话
    /// 对应 PHP: RealtimeController@convView
    /// </summary>
    [HttpPost("conv-view")]
    public async Task<IActionResult> ConvView([FromBody] ConvViewRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var userName = User.Identity?.Name ?? $"用户{userId}";
        await _hubService.NotifyUserViewingConversationAsync(request.ConversationId, userId.Value, userName);
        return Ok();
    }

    /// <summary>
    /// 通知会话已更新
    /// 对应 PHP: RealtimeController@convUpdated
    /// </summary>
    [HttpPost("conv-updated")]
    public async Task<IActionResult> ConvUpdated([FromBody] ConvUpdatedRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        // 校验用户对目标 MailboxId 的访问权限，防止被滥用刷任意邮箱通知
        if (!await HasMailboxAccess(userId.Value, request.MailboxId))
        {
            return Forbid();
        }

        await _hubService.NotifyConversationRefreshAsync(request.MailboxId, request.ConversationId);
        return Ok();
    }

    /// <summary>
    /// 判断用户对指定邮箱是否有访问权限（超管默认拥有全部邮箱权限，其余用户按 UserRoles 关联表判断）
    /// </summary>
    private async Task<bool> HasMailboxAccess(int userId, int mailboxId)
    {
        if (await _userRoleResolver.IsSuperAdminAsync(userId))
        {
            return true;
        }

        var scopedRoleIds = await _unitOfWork.UserRoles.GetQueryable()
            .Where(ur => ur.UserId == userId && (ur.MailboxId == null || ur.MailboxId == mailboxId))
            .Select(ur => ur.RoleId)
            .AnyAsync();
        if (scopedRoleIds)
        {
            return true;
        }

        // 如果用户在 User.Role 字段中是专员/主管角色级别，也认为可访问（允许通过）
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user != null && (int)user.Role >= (int)NoneadCSS.Domain.Enums.UserRoleType.Agent)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 用户上线通知
    /// 对应 PHP: RealtimeController@userTyping
    /// </summary>
    [HttpPost("user-typing")]
    public async Task<IActionResult> UserTyping([FromBody] UserTypingRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var userName = User.Identity?.Name ?? $"用户{userId}";
        await _hubService.SendToGroup($"conv_{request.ConversationId}", "UserTyping", new
        {
            conversationId = request.ConversationId,
            userId,
            userName
        });
        return Ok();
    }

    /// <summary>
    /// 通知用户停止输入
    /// </summary>
    [HttpPost("user-stop-typing")]
    public async Task<IActionResult> UserStopTyping([FromBody] UserTypingRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        await _hubService.SendToGroup($"conv_{request.ConversationId}", "UserStopTyping", new
        {
            conversationId = request.ConversationId,
            userId
        });
        return Ok();
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
}

