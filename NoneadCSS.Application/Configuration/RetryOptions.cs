namespace NoneadCSS.Application.Configuration;

/// <summary>
/// 重试策略配置选项
/// </summary>
public class RetryOptions
{
    /// <summary>邮件发送首次失败后的重试延迟（分钟）</summary>
    public int EmailFirstRetryDelayMinutes { get; set; } = 5;

    /// <summary>SMTP 发送邮件超时（毫秒），默认 120s，用于 SendEmailAsync / SendNotificationAsync 发送可能带大附件的邮件</summary>
    public int EmailSmtpSendTimeoutMs { get; set; } = 120_000;

    /// <summary>邮件连接测试 / 快捷操作超时（毫秒），默认 15s，用于 GetImapFoldersAsync / DeleteEmailAsync / TestConnectionAsync 等轻量操作</summary>
    public int EmailQuickOperationTimeoutMs { get; set; } = 15_000;

    /// <summary>IMAP 批量收取邮件超时（毫秒），默认 30s，用于 FetchNewMessagesAsync 批量拉取和解析</summary>
    public int EmailFetchTimeoutMs { get; set; } = 30_000;
}
