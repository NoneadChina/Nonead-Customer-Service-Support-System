namespace NoneadCSS.Application.DTOs.Requests;

public class ConvViewRequest
{
    public int ConversationId { get; set; }
}

public class ConvUpdatedRequest
{
    public int MailboxId { get; set; }
    public int? ConversationId { get; set; }
}

public class UserTypingRequest
{
    public int ConversationId { get; set; }
}
