namespace NoneadCSS.Application.Configuration;

/// <summary>
/// 消息接入匹配窗口配置选项（各渠道消息匹配已有会话的时间窗口）
/// </summary>
public class IngestionMatchOptions
{
    /// <summary>微信消息匹配已有会话的时间窗口（小时），超过则创建新会话</summary>
    public int WeChatMatchWindowHours { get; set; } = 48;

    /// <summary>钉钉消息匹配已有会话的时间窗口（小时）</summary>
    public int DingDingMatchWindowHours { get; set; } = 72;

    /// <summary>飞书消息匹配已有会话的时间窗口（小时）</summary>
    public int FeishuMatchWindowHours { get; set; } = 48;

    /// <summary>WhatsApp 消息匹配已有会话的时间窗口（小时）</summary>
    public int WhatsAppMatchWindowHours { get; set; } = 24;

    /// <summary>WhatsApp 24h 窗口预警阈值（小时），剩余时间低于此值时显示警告状态</summary>
    public int WhatsAppWarningThresholdHours { get; set; } = 4;

    /// <summary>Messenger 消息匹配已有会话的时间窗口（小时）；平台自身限制是 24h 回复窗口，见 MessengerOptions</summary>
    public int MessengerMatchWindowHours { get; set; } = 24;

    /// <summary>Messenger 24h 回复窗口预警阈值（小时）</summary>
    public int MessengerWarningThresholdHours { get; set; } = 4;

    /// <summary>Telegram 消息匹配已有会话的时间窗口（小时）；Telegram 无回复窗口限制，此值只用于本站会话归组</summary>
    public int TelegramMatchWindowHours { get; set; } = 72;

    /// <summary>LINE 消息匹配已有会话的时间窗口（小时）；LINE 用 replyToken（约 30s）回复，超窗自动回落 push</summary>
    public int LineMatchWindowHours { get; set; } = 72;

    /// <summary>iMessage 消息匹配已有会话的时间窗口（小时）；Apple 侧按会话而非时间窗，此值只用于本站会话归组，故取较长窗口</summary>
    public int IMessageMatchWindowHours { get; set; } = 168;
}
