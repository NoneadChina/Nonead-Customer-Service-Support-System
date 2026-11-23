namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 用户通知设置响应
/// 对应 PHP: app/UserNotification.php
/// </summary>
public class UserNotificationSettingsResponse
{
    public int UserId { get; set; }
    public string Locale { get; set; } = "zh-Hans";
    public string Timezone { get; set; } = "Asia/Shanghai";

    public bool EmailOnNewConversation { get; set; } = true;
    public bool EmailOnAssignedToMe { get; set; } = true;
    public bool EmailOnNewReply { get; set; } = false;
    public bool EmailOnMention { get; set; } = true;
    public bool BrowserOnNewConversation { get; set; } = true;
    public bool BrowserOnAssignedToMe { get; set; } = true;
    public bool BrowserOnNewReply { get; set; } = true;
    public bool BrowserOnMention { get; set; } = true;

    public string? DefaultSignature { get; set; }
    public bool UseGlobalSignature { get; set; } = true;
}
