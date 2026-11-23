namespace NoneadCSS.Application.DTOs.Responses;

public class SystemOptionResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Category { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class NotificationTemplateResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? EventTypeLabel { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<string> Variables { get; set; } = new();
}

public class NotificationRuleResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? EventTypeLabel { get; set; }
    public int? MailboxId { get; set; }
    public string? MailboxName { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public int? RoleId { get; set; }
    public string? RoleName { get; set; }
    public int? TeamId { get; set; }
    public string? TeamName { get; set; }
    public int? RecipientMailboxId { get; set; }
    public string? RecipientMailboxName { get; set; }
    public int? TemplateId { get; set; }
    public string? TemplateName { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    /// <summary>乐观锁版本标记（RowVersion base64 字符串），更新时回传 ifMatch。</summary>
    public string? RowVersion { get; set; }
}

public class NotificationSendResultResponse
{
    public int TotalRules { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = [];
}

public class NotificationTemplateOverviewResponse
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int Inactive { get; set; }
    public List<NotificationTemplateEventStats> Events { get; set; } = new();
}

public class NotificationTemplateEventStats
{
    public string EventType { get; set; } = string.Empty;
    public string EventTypeLabel { get; set; } = string.Empty;
    public int Count { get; set; }
    public int ActiveCount { get; set; }
}

public class NotificationRuleOverviewResponse
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int Inactive { get; set; }
    public List<NotificationRuleEventStats> Events { get; set; } = new();
}

public class NotificationRuleEventStats
{
    public string EventType { get; set; } = string.Empty;
    public string EventTypeLabel { get; set; } = string.Empty;
    public int Count { get; set; }
    public int ActiveCount { get; set; }
}

public class TemplateVariableValidateResponse
{
    public List<string> SubjectVariables { get; set; } = new();
    public List<string> BodyVariables { get; set; } = new();
    public List<string> AllVariables { get; set; } = new();
    public List<string> CommonVariables { get; set; } = new();
    public List<string> UncommonVariables { get; set; } = new();
}

public class DuplicateNotificationTemplateRequest
{
    public string? NewName { get; set; }
}

public class DuplicateNotificationRuleRequest
{
    public string? NewName { get; set; }
}
