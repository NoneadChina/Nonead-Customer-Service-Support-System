#nullable enable
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services.Calendar;

public interface ICalendarReminderService
{
    Task<int> ScanAndSendRemindersAsync(CancellationToken ct = default);
    /// <summary>
    /// 发送日历提醒邮件（解析模板 + 发件邮箱 + 收件人后入队）。
    /// 返回 true 表示邮件真实入队；false 表示无可发邮箱/无收件人/入队失败（静默跳过）。
    /// </summary>
    Task<bool> SendCalendarReminderEmailAsync(CalendarEvent evt, DateTime effectiveStartAt, MailTemplate? template, Mailbox? senderMailbox);
    string BuildDefaultReminderHtml(CalendarEvent evt, DateTime effectiveStartAt, string eventTypeLabel, string timeStr, string endStr);
    Task<Mailbox?> ResolveSenderMailboxAsync(int? mailboxId, Dictionary<int, Mailbox?> cache);
}
