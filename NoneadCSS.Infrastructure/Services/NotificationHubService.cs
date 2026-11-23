using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// SignalR 通知服务实现
/// </summary>
public class NotificationHubService : INotificationHubService
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IHubContext<ChatHub> _chatHubContext;
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<NotificationHubService> _logger;

    public NotificationHubService(
        IHubContext<NotificationHub> hubContext,
        IHubContext<ChatHub> chatHubContext,
        IConnectionManager connectionManager,
        ILogger<NotificationHubService> logger)
    {
        _hubContext = hubContext;
        _chatHubContext = chatHubContext;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task SendToAll(string eventName, object data)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(eventName, data);
            _logger.LogDebug("已向所有用户发送事件 {EventName}", eventName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送事件 {EventName} 到所有用户时出错", eventName);
        }
    }

    public async Task SendToUser(int userId, string eventName, object data)
    {
        try
        {
            await _hubContext.Clients.Group($"user_{userId}").SendAsync(eventName, data);
            _logger.LogDebug("已向用户 {UserId} 发送事件 {EventName}", userId, eventName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送事件 {EventName} 到用户 {UserId} 时出错", eventName, userId);
        }
    }

    public async Task SendToGroup(string groupName, string eventName, object data)
    {
        try
        {
            await _hubContext.Clients.Group(groupName).SendAsync(eventName, data);
            _logger.LogDebug("已向组 {GroupName} 发送事件 {EventName}", groupName, eventName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送事件 {EventName} 到组 {GroupName} 时出错", eventName, groupName);
        }
    }

    public async Task AddUserToGroup(int userId, string groupName)
    {
        var connectionIds = _connectionManager.GetConnectionIds(userId).ToList();
        if (connectionIds.Count == 0)
        {
            _logger.LogDebug("用户 {UserId} 当前无活跃连接，跳过加入组 {GroupName}", userId, groupName);
            return;
        }

        foreach (var connectionId in connectionIds)
        {
            try
            {
                await _hubContext.Groups.AddToGroupAsync(connectionId, groupName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "将连接 {ConnectionId} 加入组 {GroupName} 失败", connectionId, groupName);
            }
        }
        _logger.LogInformation("用户 {UserId} 的 {Count} 个连接已加入组 {GroupName}", userId, connectionIds.Count, groupName);
    }

    public async Task RemoveUserFromGroup(int userId, string groupName)
    {
        var connectionIds = _connectionManager.GetConnectionIds(userId).ToList();
        if (connectionIds.Count == 0)
        {
            _logger.LogDebug("用户 {UserId} 当前无活跃连接，跳过移出组 {GroupName}", userId, groupName);
            return;
        }

        foreach (var connectionId in connectionIds)
        {
            try
            {
                await _hubContext.Groups.RemoveFromGroupAsync(connectionId, groupName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "将连接 {ConnectionId} 移出组 {GroupName} 失败", connectionId, groupName);
            }
        }
        _logger.LogInformation("用户 {UserId} 的 {Count} 个连接已移出组 {GroupName}", userId, connectionIds.Count, groupName);
    }

    public async Task NotifyUserViewingConversationAsync(int conversationId, int userId, string userName)
    {
        try
        {
            var groupName = $"conv_{conversationId}";
            await _hubContext.Clients.Group(groupName).SendAsync("RealtimeConvView", new
            {
                conversationId,
                userId,
                userName,
                timestamp = DateTime.UtcNow
            });
            _logger.LogDebug("已通知会话 {ConversationId} 有用户 {UserId} ({UserName}) 正在查看",
                conversationId, userId, userName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "通知会话查看事件失败");
        }
    }

    public async Task NotifyConversationRefreshAsync(int mailboxId, int? conversationId)
    {
        try
        {
            await _hubContext.Clients.Group($"mailbox_{mailboxId}").SendAsync("RefreshConversations", new
            {
                mailboxId,
                conversationId,
                timestamp = DateTime.UtcNow
            });
            _logger.LogDebug("已通知邮箱 {MailboxId} 刷新会话", mailboxId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "通知会话刷新失败");
        }
    }

    public async Task NotifyCalendarEventChangedAsync(string changeType, int eventId, IEnumerable<int> userIds, object payload)
    {
        if (userIds == null || !userIds.Any())
        {
            _logger.LogWarning("日历事件变更通知跳过：userIds 为空或 null，eventId={EventId}, changeType={ChangeType}", eventId, changeType);
            return;
        }

        var data = new
        {
            changeType,
            eventId,
            data = payload,
            timestamp = DateTime.UtcNow
        };

        var tasks = userIds.Select(async userId =>
        {
            try
            {
                await SendToUser(userId, "CalendarEventChanged", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "向用户 {UserId} 推送日历事件变更失败，eventId={EventId}, changeType={ChangeType}", userId, eventId, changeType);
            }
        });
        await Task.WhenAll(tasks);
    }

    public async Task NotifyCalendarRefreshAsync(IEnumerable<int> userIds)
    {
        if (userIds == null || !userIds.Any())
        {
            _logger.LogWarning("日历刷新通知跳过：userIds 为空或 null");
            return;
        }

        var data = new
        {
            timestamp = DateTime.UtcNow
        };

        foreach (var userId in userIds)
        {
            try
            {
                await SendToUser(userId, "CalendarRefresh", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "向用户 {UserId} 推送日历刷新失败", userId);
            }
        }
    }

    public async Task SendCalendarReminderAsync(int userId, int eventId, string title, DateTimeOffset startAt,
        DateTimeOffset? endAt, bool isAllDay, int? reminderMinutes, DateTimeOffset sentAt)
    {
        try
        {
            var payload = new
            {
                eventId,
                title = title ?? string.Empty,
                startAt = startAt,
                endAt = endAt,
                isAllDay,
                reminderMinutes,
                sentAt = sentAt,
            };
            await SendToUser(userId, "CalendarReminder", payload);
            _logger.LogDebug("已向用户 {UserId} 推送日程提醒 eventId={EventId}", userId, eventId);
        }
        catch (Exception ex)
        {
            // SignalR 推送失败不影响提醒主流程（后端仍有邮件/IM 兜底），记录日志即可
            _logger.LogError(ex, "向用户 {UserId} 推送日程提醒 SignalR 失败，eventId={EventId}，会走邮件/IM 兜底",
                userId, eventId);
        }
    }

    public async Task SendToChatSessionAsync(int sessionId, string eventName, object data)
    {
        try
        {
            await _chatHubContext.Clients.Group($"chat_session_{sessionId}").SendAsync(eventName, data);
            _logger.LogDebug("已向聊天会话 {SessionId} 推送事件 {EventName}", sessionId, eventName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "向聊天会话 {SessionId} 推送事件 {EventName} 失败", sessionId, eventName);
        }
    }

    public async Task NotifyChatTypingAsync(int sessionId, string senderType)
    {
        try
        {
            await _chatHubContext.Clients.Group($"chat_session_{sessionId}").SendAsync("ChatTyping", new
            {
                sessionId,
                senderType,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "推送输入状态失败，会话 {SessionId}", sessionId);
        }
    }

    public async Task NotifyAgentChatAsync(int? agentUserId, string eventName, object data)
    {
        if (agentUserId == null) return;
        try
        {
            await _hubContext.Clients.Group($"user_{agentUserId}").SendAsync(eventName, data);
            _logger.LogDebug("已向客服 {AgentUserId} 推送聊天事件 {EventName}", agentUserId, eventName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "向客服 {AgentUserId} 推送聊天事件 {EventName} 失败", agentUserId, eventName);
        }
    }
}
