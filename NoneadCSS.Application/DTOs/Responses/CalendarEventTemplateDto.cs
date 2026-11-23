using System.Text.Json.Serialization;

namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 日历事件模板响应
/// </summary>
public class CalendarEventTemplateDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = "task";

    [JsonPropertyName("durationMin")]
    public int DurationMin { get; set; } = 60;

    [JsonPropertyName("isAllDay")]
    public bool IsAllDay { get; set; } = false;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; }

    [JsonPropertyName("isShared")]
    public bool IsShared { get; set; }

    [JsonPropertyName("mailboxId")]
    public int? MailboxId { get; set; }

    [JsonPropertyName("mailboxName")]
    public string? MailboxName { get; set; }

    [JsonPropertyName("createdByUserId")]
    public int? CreatedByUserId { get; set; }

    [JsonPropertyName("createdByUserName")]
    public string? CreatedByUserName { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("deletedAt")]
    public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// 日历批量操作响应
/// </summary>
public class CalendarBatchOperationResponse
{
    /// <summary>成功处理的事件数量</summary>
    [JsonPropertyName("successCount")]
    public int SuccessCount { get; set; }

    /// <summary>跳过/失败的事件数量</summary>
    [JsonPropertyName("failedCount")]
    public int FailedCount { get; set; }

    /// <summary>跳过/失败的事件详情（id + 原因）</summary>
    [JsonPropertyName("failures")]
    public List<CalendarBatchFailedItem>? Failures { get; set; }
}

public class CalendarBatchFailedItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}
