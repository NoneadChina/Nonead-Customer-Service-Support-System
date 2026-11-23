using System.Text.Json.Serialization;

namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 任务 D#2：Outlook 同步审计日志 DTO
/// </summary>
public class OutlookSyncLogDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("mailboxId")]
    public int MailboxId { get; set; }

    /// <summary>
    /// 同步方向：inbound / outbound / both
    /// </summary>
    [JsonPropertyName("direction")]
    public string Direction { get; set; } = null!;

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; set; }

    [JsonPropertyName("finishedAt")]
    public DateTimeOffset FinishedAt { get; set; }

    /// <summary>
    /// 同步状态：success / partial / failed
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("createdCount")]
    public int CreatedCount { get; set; }

    [JsonPropertyName("updatedCount")]
    public int UpdatedCount { get; set; }

    [JsonPropertyName("deletedCount")]
    public int DeletedCount { get; set; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// 任务 D#2：查询 Outlook 同步审计日志分页响应
/// </summary>
public class OutlookSyncLogListResponse
{
    [JsonPropertyName("data")]
    public List<OutlookSyncLogDto> Data { get; set; } = [];

    [JsonPropertyName("currentPage")]
    public int CurrentPage { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("totalItems")]
    public int TotalItems { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }
}
