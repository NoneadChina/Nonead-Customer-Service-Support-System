namespace NoneadCSS.Application.Configuration;

/// <summary>
/// HTTP 客户端超时配置选项
/// </summary>
public class HttpClientOptions
{
    /// <summary>标准 API 客户端超时（秒），适用于 DingDing/WhatsApp/WeChat/Feishu/OutlookOAuth 等业务接口</summary>
    public int StandardTimeoutSeconds { get; set; } = 30;

    /// <summary>Token 服务超时（秒），适用于 WeChat/Feishu AccessToken 获取</summary>
    public int TokenServiceTimeoutSeconds { get; set; } = 15;

    /// <summary>Webhook 回调超时（秒），需较短以避免阻塞推送流程</summary>
    public int WebhookTimeoutSeconds { get; set; } = 10;

    /// <summary>Outlook Graph API 超时（秒），因可能涉及大量日历数据同步，超时较长</summary>
    public int OutlookGraphTimeoutSeconds { get; set; } = 60;

    /// <summary>微信图片下载超时（秒）</summary>
    public int WeChatMediaDownloadTimeoutSeconds { get; set; } = 15;
}
