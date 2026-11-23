using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 更新用户通知设置请求
/// </summary>
public class UserNotificationSettingsRequest
{
    public string Locale { get; set; } = "zh-Hans";
    public string Timezone { get; set; } = "Asia/Shanghai";

    public bool EmailOnNewConversation { get; set; } = true;
    public bool EmailOnAssignedToMe { get; set; } = true;
    public bool EmailOnNewReply { get; set; }
    public bool EmailOnMention { get; set; } = true;
    public bool BrowserOnNewConversation { get; set; } = true;
    public bool BrowserOnAssignedToMe { get; set; } = true;
    public bool BrowserOnNewReply { get; set; } = true;
    public bool BrowserOnMention { get; set; } = true;

    [MaxLength(2000)]
    public string? DefaultSignature { get; set; }
    public bool UseGlobalSignature { get; set; } = true;
}
