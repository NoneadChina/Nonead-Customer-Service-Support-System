using System.Text.Json.Serialization;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 更新钉钉配置请求
/// </summary>
public class UpdateDingDingSettingsRequest
{
    public string? AppKey { get; set; }
    public string? AppSecret { get; set; }
    public long? AgentId { get; set; }
    public string? RobotCode { get; set; }
    public string? CallbackToken { get; set; }
    public string? EncodingAesKey { get; set; }
    public string? ReceiveMode { get; set; }
    public int? MailboxId { get; set; }
    public bool? ContactCollectionEnabled { get; set; }
    public bool? AutoReplyEnabled { get; set; }
    public string? AutoReplyMessage { get; set; }
    public bool? Enabled { get; set; }
}

/// <summary>
/// 钉钉回复客户消息请求
/// </summary>
public class DingDingReplyRequest
{
    [JsonPropertyName("conversationId")]
    public int ConversationId { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("attachmentIds")]
    public int[]? AttachmentIds { get; set; }
}
