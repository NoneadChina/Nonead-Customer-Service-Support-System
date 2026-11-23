namespace NoneadCSS.Application.DTOs.Responses;

public class BulkOperationResponse
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// 状态更新成功但工作流触发失败的会话 ID 列表。
    /// 这些会话的状态变更已持久化，但后续工作流触发失败，调用方可据此重试或人工排查。
    /// </summary>
    public List<int> FailedWorkflowConversationIds { get; set; } = [];
}
