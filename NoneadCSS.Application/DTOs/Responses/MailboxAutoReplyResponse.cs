namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 邮箱自动回复响应
/// 对应 PHP: app/MailboxAutoReply.php
/// </summary>
public class MailboxAutoReplyResponse
{
    public int MailboxId { get; set; }
    public bool Enabled { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public bool SendOncePerCustomer { get; set; } = true;
    public List<string> TriggerOn { get; set; } = new() { "new_conversation", "after_hours" };
    public string? AfterHoursStart { get; set; }
    public string? AfterHoursEnd { get; set; }
    public List<int> ExcludeUserIds { get; set; } = new();
}
