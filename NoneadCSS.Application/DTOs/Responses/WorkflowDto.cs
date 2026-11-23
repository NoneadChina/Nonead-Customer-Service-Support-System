using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.DTOs.Responses;

public class WorkflowDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? MailboxId { get; set; }
    public string? MailboxName { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    public string TriggerType { get; set; } = "new_conversation";
    public string? TriggerLabel { get; set; }
    public WorkflowConditionGroup? Conditions { get; set; }
    public List<WorkflowActionDto> Actions { get; set; } = new();
    public int MatchCount { get; set; }
    public DateTime? LastMatchedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class WorkflowActionDto
{
    /// <summary>
    /// 动作类型：set_status / assign_user / add_tag / remove_tag / send_reply / set_priority / forward
    /// </summary>
    public string Type { get; set; } = "set_status";

    public string? Value { get; set; }

    /// <summary>
    /// 附加参数（如回复正文等）
    /// </summary>
    public string? Extra { get; set; }
}

public class WorkflowExecutionLogDto
{
    public int Id { get; set; }
    public int WorkflowId { get; set; }
    public string WorkflowName { get; set; } = string.Empty;
    public int? ConversationId { get; set; }
    public string? ConversationSubject { get; set; }
    public string Status { get; set; } = "success";
    public string StatusLabel { get; set; } = string.Empty;
    public int ActionsExecuted { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ExecutedAt { get; set; }
}

public class WorkflowOverviewResponse
{
    public Dictionary<string, int> Stats { get; set; } = new();
    public List<WorkflowDto> RecentWorkflows { get; set; } = new();
    public List<WorkflowExecutionLogDto> RecentExecutions { get; set; } = new();
    public Dictionary<string, string> TriggerOptions { get; set; } = new();
    public Dictionary<string, string> FieldOptions { get; set; } = new();
    public Dictionary<string, string> OperatorOptions { get; set; } = new();
    public Dictionary<string, string> ActionTypeOptions { get; set; } = new();
}

public class CreateWorkflowRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? MailboxId { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string TriggerType { get; set; } = "new_conversation";
    public WorkflowConditionGroup? Conditions { get; set; }
    public List<WorkflowActionDto> Actions { get; set; } = new();
}

public class UpdateWorkflowRequest : CreateWorkflowRequest
{
    public int Id { get; set; }
}

public class WorkflowTestRequest
{
    public string? Subject { get; set; }
    public string? FromEmail { get; set; }
    public string? Body { get; set; }
    public string? ToEmail { get; set; }
    public int MailboxId { get; set; }
    public string? CustomerName { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public string? Tags { get; set; }
    public string? Mailbox { get; set; }
    public string? Assignee { get; set; }
    public string? SatisfactionRating { get; set; }
    public string? SatisfactionComment { get; set; }
}

public class WorkflowTestResponse
{
    public bool Matched { get; set; }
    public List<string> MatchedConditions { get; set; } = new();
    public List<string> ActionsToExecute { get; set; } = new();
    /// <summary>动作预览详情（包含类型、值、附加参数）</summary>
    public List<WorkflowActionPreview> ActionPreviews { get; set; } = new();

    /// <summary>
    /// 测试模式下 send_email 动作实际执行结果（T000041）。
    /// 规则匹配成功后，send_email 动作会真实发送测试邮件，其他动作仅预览。
    /// </summary>
    public List<WorkflowTestActionResult> ActionResults { get; set; } = new();
}

/// <summary>
/// 工作流测试动作执行结果
/// </summary>
public class WorkflowTestActionResult
{
    /// <summary>动作类型</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>动作类型显示标签</summary>
    public string TypeLabel { get; set; } = string.Empty;
    /// <summary>是否实际执行（send_email 为 true，其他动作为 false 仅预览）</summary>
    public bool Executed { get; set; }
    /// <summary>执行是否成功</summary>
    public bool Success { get; set; }
    /// <summary>结果消息（成功时包含收件人信息，失败时包含错误信息）</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 工作流动作预览信息
/// </summary>
public class WorkflowActionPreview
{
    /// <summary>动作类型</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>动作类型显示标签</summary>
    public string TypeLabel { get; set; } = string.Empty;
    /// <summary>动作值</summary>
    public string? Value { get; set; }
    /// <summary>附加参数</summary>
    public string? Extra { get; set; }
    /// <summary>可读描述</summary>
    public string Description { get; set; } = string.Empty;
}
