using System.Text.Json.Serialization;

namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 子任务 DTO
/// </summary>
public class SubTaskDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}
