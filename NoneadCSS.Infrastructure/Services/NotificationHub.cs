using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// SignalR Hub，处理实时通信
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    private readonly ILogger<NotificationHub> _logger;
    private readonly IConnectionManager _connectionManager;
    private readonly IUnitOfWork _unitOfWork;

    public NotificationHub(
        ILogger<NotificationHub> logger,
        IConnectionManager connectionManager,
        IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _connectionManager = connectionManager;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 用户连接时触发，自动加入相关组
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        if (userId.HasValue)
        {
            _connectionManager.AddConnection(userId.Value, Context.ConnectionId);

            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId.Value}");

            _logger.LogInformation("用户 {UserId} 已连接 SignalR，连接 ID: {ConnectionId}，当前在线用户: {OnlineCount}",
                userId.Value, Context.ConnectionId, _connectionManager.GetOnlineUserCount());
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// 用户断开连接时触发
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        if (userId.HasValue)
        {
            _connectionManager.RemoveConnection(Context.ConnectionId);

            _logger.LogInformation("用户 {UserId} 已断开 SignalR 连接，连接 ID: {ConnectionId}，当前在线用户: {OnlineCount}",
                userId.Value, Context.ConnectionId, _connectionManager.GetOnlineUserCount());
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// 加入指定组
    /// </summary>
    public async Task JoinGroup(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            throw new HubException("组名不能为空");

        var userId = GetUserId();
        if (!userId.HasValue)
            throw new HubException("未认证");

        var hasAccess = await CheckGroupAccessAsync(userId.Value, groupName);
        if (!hasAccess)
        {
            _logger.LogWarning("用户 {UserId} 尝试加入无权限的组 {GroupName}", userId.Value, groupName);
            throw new HubException("无权加入该组");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug("用户 {UserId} 加入组 {GroupName}", userId.Value, groupName);
    }

    /// <summary>
    /// 离开指定组
    /// </summary>
    public async Task LeaveGroup(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            throw new HubException("组名不能为空");

        var userId = GetUserId();
        if (!userId.HasValue)
            throw new HubException("未认证");

        var hasAccess = await CheckGroupAccessAsync(userId.Value, groupName);
        if (!hasAccess)
        {
            _logger.LogWarning("用户 {UserId} 尝试离开无权限的组 {GroupName}", userId.Value, groupName);
            throw new HubException("无权操作该组");
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug("用户 {UserId} 离开组 {GroupName}", userId.Value, groupName);
    }

    /// <summary>
    /// 校验用户是否有权访问指定组
    /// </summary>
    private async Task<bool> CheckGroupAccessAsync(int userId, string groupName)
    {
        var parts = groupName.Split('_', 2);
        if (parts.Length != 2)
            return false;

        var prefix = parts[0];
        if (!int.TryParse(parts[1], out var entityId))
            return false;

        switch (prefix)
        {
            case "user":
                return entityId == userId;

            case "mailbox":
                var mailboxUsers = await _unitOfWork.Repository<MailboxUser>()
                    .FindAsync(mu => mu.MailboxId == entityId && mu.UserId == userId);
                return mailboxUsers.Any();

            case "conv":
            case "conversation":
                var conversation = await _unitOfWork.Conversations.GetByIdAsync(entityId);
                if (conversation == null) return false;
                if (conversation.UserId == userId) return true;

                var user = await _unitOfWork.Users.GetByIdAsync(userId);
                if (user != null && user.IsAdmin()) return true;

                var convMailboxUsers = await _unitOfWork.Repository<MailboxUser>()
                    .FindAsync(mu => mu.MailboxId == conversation.MailboxId && mu.UserId == userId);
                return convMailboxUsers.Any();

            default:
                return false;
        }
    }

    /// <summary>
    /// 获取当前用户 ID
    /// </summary>
    private int? GetUserId()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId))
        {
            return userId;
        }
        return null;
    }
}
