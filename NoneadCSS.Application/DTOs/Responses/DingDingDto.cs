using System.Text.Json.Serialization;

namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 钉钉配置 DTO（脱敏，用于前端展示）
/// </summary>
public class DingDingSettingsDto
{
    public string? AppKey { get; set; }
    public bool HasAppSecret { get; set; }
    public long? AgentId { get; set; }
    public string? RobotCode { get; set; }
    public bool HasCallbackToken { get; set; }
    public bool HasEncodingAesKey { get; set; }
    public string ReceiveMode { get; set; } = "Http";
    public int? MailboxId { get; set; }
    public bool ContactCollectionEnabled { get; set; }
    public bool AutoReplyEnabled { get; set; }
    public string? AutoReplyMessage { get; set; }
    public bool Enabled { get; set; }
    public string CallbackPath { get; set; } = "/api/dingding";
}

/// <summary>
/// 钉钉连接状态
/// </summary>
public class DingDingStatusDto
{
    public bool Configured { get; set; }
    public bool Enabled { get; set; }
    public bool ApiReachable { get; set; }
    public string? CallbackUrl { get; set; }
    public string? ReceiveMode { get; set; }
    public string? RobotCode { get; set; }
}

/// <summary>
/// 钉钉回复结果
/// </summary>
public class DingDingReplyResult
{
    public bool Sent { get; set; }
    public bool ThreadCreated { get; set; } = true;
    public int? ThreadId { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 钉钉 Webhook 消息处理结果
/// </summary>
public class DingDingIngestionResult
{
    public int NewConversationCount { get; set; }
    public int ReplyCount { get; set; }
    public bool IsDuplicate { get; set; }
    public bool IsStatus { get; set; }
    public int? ConversationId { get; set; }
    public string? ErrorMessage { get; set; }
}
