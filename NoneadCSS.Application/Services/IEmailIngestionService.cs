using MimeKit;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

public interface IEmailIngestionService
{
    Task<EmailIngestionResult> ProcessMessageAsync(MimeMessage message, Mailbox mailbox, string attachmentBasePath);
}

public class EmailIngestionResult
{
    public int NewConversationCount { get; set; }
    public int ReplyCount { get; set; }
    public bool IsAutoReply { get; set; }
    public bool IsBounce { get; set; }
}
