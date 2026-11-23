using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IWorkflowService
{
    Task<(IEnumerable<WorkflowDto> Items, int Total)> GetListAsync(
        int page, int pageSize, int? mailboxId = null, bool? isActive = null,
        string? triggerType = null, string? keyword = null);

    Task<WorkflowDto?> GetByIdAsync(int id);
    Task<WorkflowDto> CreateAsync(CreateWorkflowRequest request);
    Task<WorkflowDto?> UpdateAsync(int id, UpdateWorkflowRequest request);
    Task<bool> DeleteAsync(int id);
    Task<bool> ToggleActiveAsync(int id);

    Task<(IEnumerable<WorkflowDto> Items, int Total)> GetDeletedListAsync(
        int page, int pageSize, int? mailboxId = null, string? keyword = null);
    Task<int> GetDeletedCountAsync(int? mailboxId = null);
    Task<bool> RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> ClearTrashAsync(int? mailboxId = null);

    Task<WorkflowOverviewResponse> GetOverviewAsync();
    Task<(IEnumerable<WorkflowExecutionLogDto> Items, int Total)> GetExecutionLogsAsync(
        int page, int pageSize, int? workflowId = null, int? conversationId = null,
        string? status = null, DateTime? startDate = null, DateTime? endDate = null);

    Task<WorkflowTestResponse> TestAsync(int workflowId, WorkflowTestRequest request);

    /// <summary>
    /// 根据触发类型和会话执行匹配的工作流规则。
    /// </summary>
    Task<int> ExecuteForConversationAsync(int conversationId, string triggerType);

    /// <summary>
    /// 根据触发类型和满意度评价执行匹配的工作流规则。
    /// </summary>
    Task<int> ExecuteForSatisfactionRatingAsync(int ratingId, string triggerType);

    /// <summary>
    /// 基于 ChatSession 执行匹配的工作流（用于 chat_close_requested / chat_satisfaction_submitted 等在线聊天专属 trigger）。
    /// 实现上只处理 send_chat_message 动作（在会话流内插入 system 消息），其他动作（如 send_email / assign_user）会被跳过。
    /// </summary>
    /// <param name="chatSessionId">在线聊天会话 ID</param>
    /// <param name="triggerType">WorkflowConst 中的 Trigger* 常量（如 TriggerChatCloseRequested）</param>
    /// <param name="chatRating">客户评分 1-5（仅 chat_satisfaction_submitted 时传）</param>
    /// <param name="chatRatingComment">客户评论文本（仅 chat_satisfaction_submitted 时传，可空）</param>
    /// <returns>执行成功的动作数</returns>
    Task<int> ExecuteForChatSessionAsync(int chatSessionId, string triggerType, int? chatRating = null, string? chatRatingComment = null);

    // 扩展方法 - 对应 PHP 的 TriggerAction
    Task TriggerActionAsync(string actionType, string entityType, int entityId, IDictionary<string, object>? jobData = null);
}
