using System.Text.Json.Serialization;

namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 重复日程排除/额外日期 DTO
/// </summary>
public class RecurrenceExclusionsDto
{
    /// <summary>排除的日期列表（yyyy-MM-dd）</summary>
    [JsonPropertyName("excluded")]
    public List<string> Excluded { get; set; } = new();
    
    /// <summary>额外添加的日期列表</summary>
    [JsonPropertyName("added")]
    public List<AddedRecurrenceDto> Added { get; set; } = new();
}

/// <summary>
/// 额外添加的重复实例
/// </summary>
public class AddedRecurrenceDto
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("startAt")]
    public DateTime StartAt { get; set; }

    [JsonPropertyName("endAt")]
    public DateTime? EndAt { get; set; }
}
