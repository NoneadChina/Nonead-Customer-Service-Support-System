using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NoneadCSS.Application.DTOs.Requests;

public class AssignChatSessionRequest
{
    public int UserId { get; set; }

    /// <summary>乐观锁 If-Match（ChatSessionDto.rowVersion 回传）</summary>
    [JsonPropertyName("ifMatch")]
    public string? IfMatch { get; set; }
}

public class MarkAsReadRequest
{
    [Required]
    public string ReaderType { get; set; } = string.Empty;

    /// <summary>会话层乐观锁 If-Match（更新 AgentUnreadCount/CustomerUnreadCount 等计数时使用）</summary>
    [JsonPropertyName("sessionIfMatch")]
    public string? SessionIfMatch { get; set; }
}
