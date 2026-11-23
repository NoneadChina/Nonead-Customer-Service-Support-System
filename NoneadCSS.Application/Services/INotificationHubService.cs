namespace NoneadCSS.Application.Services;

/// <summary>
/// 实时通知服务接口，用于向客户端推送消息
/// </summary>
public interface INotificationHubService
{
    /// <summary>
    /// 向所有用户发送通知
    /// </summary>
    Task SendToAll(string eventName, object data);

    /// <summary>
    /// 向指定用户发送通知
    /// </summary>
    Task SendToUser(int userId, string eventName, object data);

    /// <summary>
    /// 向指定用户组发送通知（如团队成员）
    /// </summary>
    Task SendToGroup(string groupName, string eventName, object data);

    /// <summary>
    /// 将用户加入组
    /// </summary>
    Task AddUserToGroup(int userId, string groupName);

    /// <summary>
    /// 将用户从组中移除
    /// </summary>
    Task RemoveUserFromGroup(int userId, string groupName);

    /// <summary>
    /// 通知其他用户当前用户正在查看会话（对应 PHP: app/Events/RealtimeConvView.php）
    /// </summary>
    Task NotifyUserViewingConversationAsync(int conversationId, int userId, string userName);

    /// <summary>
    /// 通知会话刷新
    /// </summary>
    Task NotifyConversationRefreshAsync(int mailboxId, int? conversationId);

    /// <summary>
    /// 日历事件变更通知（create/update/delete/status_change/restore/hard_delete 统一入口）。
    /// </summary>
    /// <param name="changeType">create | update | delete | status_change | restore | hard_delete</param>
    /// <param name="eventId">日历事件 ID</param>
    /// <param name="userIds">要推送的用户 ID 集合（创建者 + 指派人 + 参会人）</param>
    /// <param name="payload">事件 DTO（部分或完整字段）</param>
    Task NotifyCalendarEventChangedAsync(string changeType, int eventId, IEnumerable<int> userIds, object payload);

    /// <summary>
    /// 日历刷新通知（大粒度刷新，用于批量操作）。
    /// </summary>
    Task NotifyCalendarRefreshAsync(IEnumerable<int> userIds);

    /// <summary>
    /// P2-A#5 #7.1：向指定用户推送"实时日程提醒弹层"。前端确认后即可跳过邮件/IM 兜底。
    /// 每个 event 对应一次推送；接收端按 (eventId + sentAt) 做幂等。
    /// </summary>
    /// <param name="userId">接收提醒的用户</param>
    /// <param name="eventId">日历事件 id</param>
    /// <param name="title">事件标题</param>
    /// <param name="startAt">有效开始时间（CalendarEvent.EffectiveStartAt 的 UTC 偏移值）</param>
    /// <param name="endAt">有效结束时间</param>
    /// <param name="isAllDay">是否全天日程</param>
    /// <param name="reminderMinutes">提前多少分钟提醒</param>
    /// <param name="sentAt">推送时间（UTC 偏移，即 CalendarEvent.ReminderSentAt）。用于前端 ACK 幂等。</param>
    Task SendCalendarReminderAsync(int userId, int eventId, string title, DateTimeOffset startAt,
        DateTimeOffset? endAt, bool isAllDay, int? reminderMinutes, DateTimeOffset sentAt);

    /// <summary>
    /// 向聊天会话的客户端连接推送事件（通过 ChatHub，客户侧基于 sessionToken 连接）
    /// </summary>
    Task SendToChatSessionAsync(int sessionId, string eventName, object data);

    /// <summary>
    /// 通知聊天会话有用户正在输入
    /// </summary>
    Task NotifyChatTypingAsync(int sessionId, string senderType);

    /// <summary>
    /// 通知客服端有新聊天消息/状态变更（通过 NotificationHub 推送给指定客服用户）
    /// </summary>
    Task NotifyAgentChatAsync(int? agentUserId, string eventName, object data);
}
