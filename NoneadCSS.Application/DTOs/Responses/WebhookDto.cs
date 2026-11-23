using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Responses;

public class WebhookDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public List<string> Events { get; set; } = new();
    public Dictionary<string, string> Headers { get; set; } = new();
    public bool HasSecret { get; set; }
    public bool IsActive { get; set; }
    public int? MailboxId { get; set; }
    public string? MailboxName { get; set; }
    public int TriggerCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>乐观锁版本标记（RowVersion base64 字符串），更新/启用停用时回传 ifMatch。</summary>
    public string? RowVersion { get; set; }
}

public class WebhookLogDto
{
    public int Id { get; set; }
    public int WebhookId { get; set; }
    public string WebhookName { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public int? ResponseStatusCode { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public int DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WebhookOverviewResponse
{
    public int TotalWebhooks { get; set; }
    public int ActiveWebhooks { get; set; }
    public int InactiveWebhooks { get; set; }
    public int GlobalWebhooks { get; set; }
    public int MailboxWebhooks { get; set; }
    public int TotalTriggers { get; set; }
    public int TotalSuccess { get; set; }
    public int TotalFail { get; set; }
    public decimal SuccessRate { get; set; }
    public List<WebhookLogDto> RecentLogs { get; set; } = new();
    public List<WebhookDto> TopWebhooks { get; set; } = new();
    public Dictionary<string, int> EventsCount { get; set; } = new();
}

public class CreateWebhookRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Url]
    public string Url { get; set; } = string.Empty;

    [Required]
    public string Method { get; set; } = "POST";

    [Required]
    public List<string> Events { get; set; } = new();

    public Dictionary<string, string> Headers { get; set; } = new();
    public string? Secret { get; set; }
    public bool IsActive { get; set; } = true;
    public int? MailboxId { get; set; }
}

public class UpdateWebhookRequest
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Url]
    public string Url { get; set; } = string.Empty;

    [Required]
    public string Method { get; set; } = "POST";

    [Required]
    public List<string> Events { get; set; } = new();

    public Dictionary<string, string> Headers { get; set; } = new();
    public string? Secret { get; set; }
    public bool IsActive { get; set; }
    public int? MailboxId { get; set; }
    /// <summary>乐观锁版本标记（RowVersion base64）；不匹配时返回 409。</summary>
    public string? IfMatch { get; set; }
}

public class WebhookQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int? MailboxId { get; set; }
    public bool? IsActive { get; set; }
    public string? Keyword { get; set; }
    public string? EventType { get; set; }
}

public class WebhookLogQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int? WebhookId { get; set; }
    public string? EventType { get; set; }
    public bool? IsSuccess { get; set; }
}

public class TestWebhookRequest
{
    public string EventType { get; set; } = "conversation_created";
}

public class TestWebhookResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public int DurationMs { get; set; }
}
