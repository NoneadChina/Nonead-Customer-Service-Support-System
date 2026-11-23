namespace NoneadCSS.Application.Services;

public interface IEmailQueueService
{
    Task EnqueueEmailAsync(int mailboxId, string to, string subject, string body, List<int>? attachmentIds = null,
        string? cc = null, string? bcc = null, int? threadId = null, string? inReplyTo = null, string? references = null,
        int mailType = 1, int? userId = null, int? customerId = null);
    Task ProcessQueueAsync();
}