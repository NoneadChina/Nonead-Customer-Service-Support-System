using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

public interface IEmailService
{
    Task SendEmailAsync(Mailbox mailbox, string to, string subject, string body, List<Attachment>? attachments = null, CancellationToken cancellationToken = default);
    Task<List<ConversationThread>> FetchEmailsAsync(Mailbox mailbox, CancellationToken cancellationToken = default);
    Task<(List<MimeKit.MimeMessage> Messages, ulong NewUid)> FetchMimeMessagesAsync(Mailbox mailbox, ulong lastUid, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(Mailbox mailbox, CancellationToken cancellationToken = default);
    Task<(bool Success, string? ErrorMessage)> TestConnectionWithDetailsAsync(Mailbox mailbox, CancellationToken cancellationToken = default);
    Task SaveToSentFolderAsync(Mailbox mailbox, string to, string subject, string body, List<Attachment>? attachments = null, string? cc = null, string? bcc = null, CancellationToken cancellationToken = default);

    // 扩展方法 - 对应 PHP 邮件处理增强
    Task SendEmailWithHeadersAsync(Mailbox mailbox, string to, string subject, string body,
        List<Attachment>? attachments = null, string? cc = null, string? bcc = null,
        string? inReplyTo = null, string? references = null, string? messageId = null,
        EmailConversationHistory historyType = EmailConversationHistory.None,
        CancellationToken cancellationToken = default);
    Task SendEmailWithRetryAsync(Mailbox mailbox, string to, string subject, string body,
        List<Attachment>? attachments = null, int maxRetries = 168, int retryDelaySeconds = 3600,
        CancellationToken cancellationToken = default);
    Task<List<string>> GetImapFoldersAsync(Mailbox mailbox, CancellationToken cancellationToken = default);
    Task<bool> ForwardEmailAsync(Mailbox mailbox, Conversation conversation, ConversationThread thread, List<string> toAddresses, CancellationToken cancellationToken = default);
    Task<string> GetSmtpQueueIdAsync();
    Task<List<EmailBounceInfo>> DetectBounceAsync(Mailbox mailbox, MimeKit.MimeMessage message);
    Task SendSystemNotificationAsync(Mailbox mailbox, string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
    Task<bool> DeleteEmailAsync(Mailbox mailbox, string messageId, CancellationToken cancellationToken = default);
}

public enum EmailConversationHistory
{
    None = 0,
    Last = 1,
    Full = 2
}

public class EmailBounceInfo
{
    public string OriginalRecipient { get; set; } = string.Empty;
    public string BounceType { get; set; } = string.Empty;
    public string DiagnosticInfo { get; set; } = string.Empty;
    public DateTime BouncedAt { get; set; } = DateTime.UtcNow;
}
