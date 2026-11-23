namespace NoneadCSS.Domain.Enums;

public enum MailTemplateType
{
    NewConversation = 1,
    NewReply = 2,
    ConversationAssigned = 3,
    ConversationClosed = 4,
    SatisfactionSurvey = 5,
    PasswordReset = 6,
    Welcome = 7,
    CalendarReminder = 8,
    Custom = 99,
    /// <summary>
    /// 在线聊天（Chat/Widget 渠道）会话关闭后通知客户的邮件模板。
    /// 纯文本，必含 {{satisfaction_url}} 占位符（客户点击可完成满意度评分）。
    /// 用于区分通用 ConversationClosed（所有渠道共用），让在线聊天渠道有专属文案。
    /// </summary>
    ChatSessionClosed = 10,
}
