using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 在线聊天服务接口
/// </summary>
public interface IChatService
{
    // ====== 会话管理（客服侧） ======
    Task<PagedResponse<ChatSessionDto>> GetSessionsAsync(ChatSessionQuery query);
    Task<ChatSessionDto?> GetSessionByIdAsync(int id);
    Task<ChatSessionDto?> GetSessionByTokenAsync(string sessionToken);
    /// <summary>
    /// 按工单 ConversationId 查询在线聊天会话（若该工单由聊天转工单生成）
    /// </summary>
    Task<ChatSessionDto?> GetSessionByConversationIdAsync(int conversationId);
    Task<ChatSessionDto> AssignSessionAsync(int sessionId, int userId, string? ifMatch = null);
    /// <summary>
    /// 客服侧关闭会话：两阶段关闭的阶段 1，将状态设为 awaiting_rating，等待客户评分或超时。
    /// requestRating = false 时直接进入阶段 2 立即 closed（不推送满意度调查）。
    /// </summary>
    Task<ChatSessionDto> CloseSessionAsync(int sessionId, bool requestRating = true, string? ifMatch = null);
    /// <summary>
    /// 两阶段关闭的阶段 2：真正将会话置为 closed，写入 system message（可选）、设置 ClosedAt、发关闭邮件。
    /// 被 Rating 提交路径（RateChatAsync）与超时自动关闭 Job（ChatRatingTimeoutService）调用。
    /// </summary>
    Task ReallyCloseChatSessionAsync(NoneadCSS.Domain.Entities.ChatSession session, string? closeReasonSystemMsg = null);
    Task<List<ChatSessionDto>> GetActiveSessionsAsync(int? mailboxId = null);
    Task<ChatOverviewResponse> GetOverviewAsync();

    // ====== 消息管理 ======
    Task<List<ChatMessageDto>> GetMessagesAsync(int sessionId, int limit = 100);
    Task<ChatMessageDto> SendMessageAsync(int sessionId, SendChatMessageRequest request, int? userId = null, string? senderName = null);
    Task MarkAsReadAsync(int sessionId, string readerType, string? sessionIfMatch = null);
    Task<int> GetUnreadCountAsync(int sessionId, string readerType);
    Task<ChatMessageDto> RecallMessageAsync(int sessionId, int messageId, int userId, string? ifMatch = null);
    Task<ChatMessageDto> EditMessageAsync(int sessionId, int messageId, string newContent, int userId, string? ifMatch = null);
    Task NotifyTypingAsync(int sessionId, string senderType, int? userId = null);

    // ====== 客户端接口 ======
    Task<InitiateChatResponse> InitiateChatAsync(InitiateChatRequest request, string? customerIp = null, string? userAgent = null);
    Task<ChatMessageDto> SendCustomerMessageAsync(string sessionToken, SendChatMessageRequest request);
    Task<List<ChatMessageDto>> GetCustomerMessagesAsync(string sessionToken, int limit = 100);
    Task<ChatSessionDto> CloseSessionByTokenAsync(string sessionToken);
    Task<ChatSessionDto> RateChatAsync(string sessionToken, RateChatRequest request);

    // ====== 转工单 ======
    Task<ChatSessionDto> TransferToTicketAsync(int sessionId, TransferToTicketRequest request, int operatedByUserId);

    // ====== 软删除 / 垃圾箱 / 硬删除（仅超级管理员与客服主管） ======
    Task<PagedResponse<ChatSessionDto>> GetDeletedSessionsAsync(int page, int pageSize, string? keyword = null);
    Task<bool> DeleteSessionAsync(int sessionId);
    Task<bool> BulkDeleteSessionsAsync(List<int> sessionIds);
    Task<bool> RestoreSessionAsync(int sessionId);
    Task<bool> HardDeleteSessionAsync(int sessionId);
    Task<int> HardDeleteAllDeletedSessionsAsync();
}
