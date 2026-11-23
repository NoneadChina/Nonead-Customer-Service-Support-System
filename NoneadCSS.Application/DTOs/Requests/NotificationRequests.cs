using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class CreateNotificationTemplateRequest
{
    [Required(ErrorMessage = "模板名称不能为空")]
    [MaxLength(100, ErrorMessage = "模板名称最多 100 个字符")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "主题最多 500 个字符")]
    public string Subject { get; set; } = string.Empty;

    [MaxLength(10000, ErrorMessage = "正文最多 10000 个字符")]
    public string Body { get; set; } = string.Empty;

    [Required(ErrorMessage = "事件类型不能为空")]
    [MaxLength(100, ErrorMessage = "事件类型最多 100 个字符")]
    [RegularExpression(@"^[a-z][a-z0-9_]{0,99}$", ErrorMessage = "事件类型格式无效，仅允许小写字母、数字、下划线")]
    public string EventType { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public class UpdateNotificationTemplateRequest : CreateNotificationTemplateRequest { }

public class CreateNotificationRuleRequest
{
    [Required(ErrorMessage = "规则名称不能为空")]
    [MaxLength(100, ErrorMessage = "规则名称最多 100 个字符")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "事件类型不能为空")]
    [MaxLength(100, ErrorMessage = "事件类型最多 100 个字符")]
    [RegularExpression(@"^[a-z][a-z0-9_]{0,99}$", ErrorMessage = "事件类型格式无效，仅允许小写字母、数字、下划线")]
    public string EventType { get; set; } = string.Empty;

    public int? MailboxId { get; set; }
    public int? UserId { get; set; }
    public int? RoleId { get; set; }
    public int? TeamId { get; set; }
    public int? RecipientMailboxId { get; set; }
    public int? TemplateId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdateNotificationRuleRequest : CreateNotificationRuleRequest
{
    /// <summary>乐观锁版本标记（RowVersion base64）；不匹配时返回 409。</summary>
    public string? IfMatch { get; set; }
}

public class SendNotificationRequest
{
    [Required(ErrorMessage = "事件类型不能为空")]
    [MaxLength(100, ErrorMessage = "事件类型最多 100 个字符")]
    [RegularExpression(@"^[a-z][a-z0-9_]{0,99}$", ErrorMessage = "事件类型格式无效，仅允许小写字母、数字、下划线")]
    public string EventType { get; set; } = string.Empty;

    public int? MailboxId { get; set; }
    public int? ConversationId { get; set; }
    public Dictionary<string, string> Variables { get; set; } = [];
}

public class NotificationTemplateQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? EventType { get; set; }
    public bool? IsActive { get; set; }
    public string? Search { get; set; }
}

public class NotificationRuleQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? EventType { get; set; }
    public int? MailboxId { get; set; }
    public int? UserId { get; set; }
    public bool? IsActive { get; set; }
    public string? Search { get; set; }
}

public class NotificationTemplatePreviewRequest
{
    public Dictionary<string, string>? CustomVariables { get; set; }
}

public class NotificationTemplatePreviewResponse
{
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}
