namespace NoneadCSS.Domain.Events;

/// <summary>
/// 预定义的事件名称常量
/// </summary>
public static class NoneadCSSEvents
{
    // 会话事件
    public const string ConversationCreated = "conversation.created";
    public const string ConversationStatusChanged = "conversation.status_changed";
    public const string ConversationPriorityChanged = "conversation.priority_changed";
    public const string ConversationAssigned = "conversation.assigned";
    public const string ConversationDeleted = "conversation.deleted";
    public const string ConversationTagAdded = "conversation.tag_added";
    public const string ConversationTagRemoved = "conversation.tag_removed";

    // 回复事件
    public const string ReplyCreated = "reply.created";
    public const string NoteCreated = "note.created";

    // 客户事件
    public const string CustomerCreated = "customer.created";
    public const string CustomerUpdated = "customer.updated";

    // 用户事件
    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";

    // 邮箱事件
    public const string MailboxCreated = "mailbox.created";
    public const string MailboxUpdated = "mailbox.updated";

    // 邮件事件
    public const string EmailSent = "email.sent";
    public const string EmailReceived = "email.received";
    public const string EmailOpened = "email.opened";

    // 过滤器
    public const string FilterConversationSubject = "filter.conversation.subject";
    public const string FilterConversationBody = "filter.conversation.body";
    public const string FilterEmailBody = "filter.email.body";
}
