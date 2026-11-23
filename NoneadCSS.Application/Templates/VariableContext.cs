using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Templates;

/// <summary>
/// 模板变量上下文 — 承载所有领域数据，由调用方按需填充
/// 调用方不再手写 Dictionary，只需构造 Context
/// </summary>
public class VariableContext
{
    /// <summary>会话（邮件工单）</summary>
    public Conversation? Conversation { get; set; }

    /// <summary>客户</summary>
    public Customer? Customer { get; set; }

    /// <summary>客服用户</summary>
    public User? User { get; set; }

    /// <summary>聊天会话</summary>
    public ChatSession? ChatSession { get; set; }

    /// <summary>SLA 策略</summary>
    public SlaPolicy? SlaPolicy { get; set; }

    /// <summary>满意度评价</summary>
    public SatisfactionRating? Rating { get; set; }

    /// <summary>日历事件</summary>
    public CalendarEvent? CalendarEvent { get; set; }

    /// <summary>邮箱</summary>
    public Mailbox? Mailbox { get; set; }

    /// <summary>SLA 违规记录（可选）</summary>
    public SlaViolation? SlaViolation { get; set; }

    /// <summary>操作人（分配人/关闭人/更新人等，视场景而定）</summary>
    public User? Actor { get; set; }

    /// <summary>消息正文（回复/更新内容）</summary>
    public string? MessageBody { get; set; }

    /// <summary>首条消息正文</summary>
    public string? FirstMessageBody { get; set; }

    /// <summary>手动附加变量（给边缘场景留口子，如批量导入统计）</summary>
    public Dictionary<string, string> Extra { get; set; } = new();
}
