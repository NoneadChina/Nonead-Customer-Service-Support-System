using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services;

public interface IConversationService
{
    Task<PagedResponse<ConversationResponse>> GetConversationsAsync(int mailboxId, int folderId, int page, int pageSize, int? userId = null, int? status = null, int? priority = null, int? starredByUserId = null, int? customerId = null, SourceType? sourceType = null, int? readerUserId = null);
    Task<ConversationResponse?> GetConversationByIdAsync(int id);
    /// <summary>
    /// 按 ID 列表批量获取会话（一次数据库查询），用于批量鉴权等场景避免 N+1。
    /// </summary>
    Task<IEnumerable<ConversationResponse>> GetConversationsByIdsAsync(IEnumerable<int> ids);
    Task<ConversationResponse> CreateConversationAsync(CreateConversationRequest request);
    Task<ConversationResponse?> UpdateStatusAsync(int id, int status, int? userId = null);
    Task<ConversationResponse?> UpdatePriorityAsync(int id, int priority);
    Task<ConversationResponse?> AssignAsync(int id, int userId);
    Task<bool> DeleteConversationAsync(int id);
    Task<PagedResponse<ConversationResponse>> SearchAsync(string query, int page, int pageSize, int? userId = null, int? readerUserId = null);
    Task<PagedResponse<ConversationResponse>> AdvancedSearchAsync(
        string? keyword = null,
        ConversationStatus? status = null,
        ConversationType? type = null,
        SourceType? sourceType = null,
        int? mailboxId = null,
        int? userId = null,
        int? customerId = null,
        string? customerEmail = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        DateTime? lastReplyStartDate = null,
        DateTime? lastReplyEndDate = null,
        bool? hasAttachments = null,
        bool? isUnassigned = null,
        int page = 1,
        int pageSize = 20,
        int? readerUserId = null);
    /// <summary>
    /// 标记指定会话已被当前用户阅读（更新 LastReadAt 为当前 UTC 时间）。
    /// 进入会话详情时调用，使列表页未读徽章消失。
    /// </summary>
    /// <param name="conversationId">会话 ID</param>
    /// <param name="userId">当前用户 ID</param>
    Task MarkConversationAsReadAsync(int conversationId, int userId);
    Task<ThreadResponse?> AddReplyAsync(CreateThreadRequest request, int? userId = null);
    Task<IEnumerable<ThreadResponse>> GetThreadsByConversationIdAsync(int conversationId);
    Task<ConversationResponse?> ToggleFollowAsync(int conversationId, int userId);
    Task<List<int>> GetFollowersAsync(int conversationId);
    Task<ThreadResponse?> AddNoteAsync(int conversationId, AddNoteRequest request, int userId);
    Task<BulkOperationResponse> BulkAssignAsync(BulkAssignRequest request, int? currentUserId = null);
    Task<BulkOperationResponse> BulkUpdateStatusAsync(BulkStatusRequest request);
    Task<BulkOperationResponse> BulkUpdatePriorityAsync(BulkPriorityRequest request);
    Task<BulkOperationResponse> BulkDeleteAsync(BulkConversationRequest request);
    
    Task<ThreadResponse?> SaveDraftAsync(CreateThreadRequest request, int? userId = null);
    Task<ThreadResponse?> GetDraftAsync(int conversationId);
    Task<bool> DeleteDraftAsync(int conversationId);
    Task<List<ThreadResponse>> GetDraftsByUserIdAsync(int userId);
    
    Task<ConversationResponse> CreatePhoneConversationAsync(CreatePhoneConversationRequest request, int? userId = null);
    Task<ConversationResponse?> ToggleStarAsync(int conversationId, int userId);
    Task<List<int>> GetStarredConversationIdsAsync(int userId);
    Task<IEnumerable<ConversationResponse>> GetStarredConversationsAsync(int userId, int page, int pageSize);
    Task<ConversationResponse?> CloneConversationAsync(int conversationId, int? userId = null);
    Task<bool> HardDeleteConversationAsync(int conversationId);
    /// <summary>
    /// 获取垃圾箱中的已软删除会话列表。
    /// mailboxId > 0 时按邮箱过滤，mailboxId <= 0 时返回所有邮箱的已删除会话。
    /// </summary>
    Task<PagedResponse<ConversationResponse>> GetTrashByMailboxAsync(int mailboxId, int page, int pageSize);
    /// <summary>
    /// 恢复指定会话（从垃圾箱还原）。
    /// </summary>
    Task<ConversationResponse?> RestoreConversationAsync(int conversationId);
    /// <summary>
    /// 清空垃圾箱（彻底删除所有已软删除会话）。
    /// mailboxId > 0 时只清空该邮箱的，mailboxId <= 0 时清空全部。
    /// </summary>
    Task<int> ClearTrashByMailboxAsync(int mailboxId);
    Task<IEnumerable<ThreadResponse>> GetChatModeThreadsAsync(int conversationId);
}
