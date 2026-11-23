namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 工作流条件评估上下文。携带会话相关信息供工作流条件匹配。
/// </summary>
public class WorkflowEvaluationContext
{
    public string Subject { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string ToEmail { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public string Mailbox { get; set; } = string.Empty;
    public string Assignee { get; set; } = string.Empty;
    public string SatisfactionRating { get; set; } = string.Empty;
    public string SatisfactionComment { get; set; } = string.Empty;

    /// <summary>
    /// 会话来源渠道类型（Email/WeChat/Feishu/WhatsApp/DingTalk/Chat 等）。
    /// 用于工作流条件按渠道过滤，例如仅对 IM 渠道触发特定动作。
    /// </summary>
    public string SourceType { get; set; } = string.Empty;
}
