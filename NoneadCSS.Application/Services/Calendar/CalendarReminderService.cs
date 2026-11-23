#nullable enable
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Metrics;
using NoneadCSS.Application.Templates;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services.Calendar;

public class CalendarReminderService : ICalendarReminderService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly INotificationHubService _notificationHubService;
    private readonly IEmailQueueService _emailQueueService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CalendarReminderService> _logger;
    private readonly BrandOptions _brand;
    private readonly CalendarOptions _calendarOptions;
    private readonly ITemplateRenderer _renderer;
    private readonly IRecurrenceExpansionService _recurrence;

    public CalendarReminderService(
        IUnitOfWork unitOfWork,
        INotificationService notificationService,
        ICurrentUserAccessor currentUserAccessor,
        INotificationHubService notificationHubService,
        IEmailQueueService emailQueueService,
        ILogger<CalendarReminderService> logger,
        IOptions<BrandOptions> brand,
        IOptions<CalendarOptions> calendarOptions,
        ITemplateRenderer renderer,
        IConfiguration configuration,
        IRecurrenceExpansionService recurrence)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(currentUserAccessor);
        ArgumentNullException.ThrowIfNull(notificationHubService);
        ArgumentNullException.ThrowIfNull(emailQueueService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(brand);
        ArgumentNullException.ThrowIfNull(calendarOptions);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(recurrence);
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
        _currentUserAccessor = currentUserAccessor;
        _notificationHubService = notificationHubService;
        _emailQueueService = emailQueueService;
        _logger = logger;
        _brand = brand.Value;
        _calendarOptions = calendarOptions.Value;
        _renderer = renderer;
        _configuration = configuration;
        _recurrence = recurrence;
    }

    public async Task<int> ScanAndSendRemindersAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var scanWindowHours = _calendarOptions.ScanWindowHours;
        var scanLookbackMinutes = _calendarOptions.ScanLookbackMinutes;
        var scanOverdueExpireHours = _calendarOptions.ScanOverdueExpireHours;
        var scanWindow = now.AddHours(scanWindowHours);
        var lookbackFrom = now.AddMinutes(-scanLookbackMinutes);

        var upcomingEvents = await _unitOfWork.CalendarEvents.GetUpcomingRemindersAsync(lookbackFrom, scanWindow).ConfigureAwait(false);
        var sentCount = 0;
        var skippedCount = 0;
        var failedCount = 0;

        _logger.LogInformation(
            "日历提醒扫描开始: lookback={Lookback}, window={Window}, candidates={Count}",
            lookbackFrom, scanWindow, upcomingEvents.Count);

        var reminderTemplate = await _unitOfWork.MailTemplates.GetBySlugAsync("calendar-reminder").ConfigureAwait(false);
        if (reminderTemplate != null && !reminderTemplate.IsActive)
            reminderTemplate = null;

        var senderMailboxCache = new Dictionary<int, Mailbox?>();

        foreach (var evt in upcomingEvents)
        {
            if (!evt.ReminderMinutes.HasValue)
                continue;

            var effectiveStartAt = evt.StartAt;
            if (evt.IsRecurring)
            {
                effectiveStartAt = _recurrence.GetNextOccurrenceStart(evt, now, scanWindow);
                if (effectiveStartAt == DateTime.MinValue)
                {
                    _logger.LogDebug(
                        "日历提醒跳过(重复日程无下次occurrence): event_id={EventId}, title={Title}",
                        evt.Id, evt.Title);
                    skippedCount++;
                    continue;
                }
            }

            var reminderTime = effectiveStartAt.AddMinutes(-evt.ReminderMinutes.Value);
            if (reminderTime > now)
                continue;

            if (reminderTime < now.AddHours(-scanOverdueExpireHours))
                continue;

            var isRecurringReset = false;
            if (evt.IsReminderSent)
            {
                if (evt.IsRecurring && evt.LastReminderSentAt.HasValue
                    && effectiveStartAt > evt.LastReminderSentAt.Value.AddMinutes(1))
                {
                    isRecurringReset = true;
                }
                else
                {
                    continue;
                }
            }

            int claimed;
            if (isRecurringReset)
            {
                var cycleCutoff = effectiveStartAt.AddMinutes(-1);
                claimed = await _unitOfWork.CalendarEvents
                    .TryClaimRecurringReminderResetAsync(evt.Id, cycleCutoff, DateTime.UtcNow)
                    .ConfigureAwait(false);
            }
            else
            {
                claimed = await _unitOfWork.CalendarEvents
                    .TryClaimFirstReminderAsync(evt.Id, DateTime.UtcNow)
                    .ConfigureAwait(false);
            }

            if (claimed == 0)
            {
                _logger.LogDebug(
                    "日历提醒跳过(claim失败已被其他实例发送): event_id={EventId}, title={Title}",
                    evt.Id, evt.Title);
                skippedCount++;
                continue;
            }

            sentCount++;

            // T000090 修复：按 ReminderType 分通道发送提醒。
            // v2.2.13（T000090）：ReminderType 支持逗号分隔**多选组合**（popup,email,sms 可同时勾选）：
            //   popup  → SignalR 弹窗 + 站内通知
            //   email  → 邮件
            //   sms    → 站内通知（无 SMS 网关，降级为站内通知并记录日志）
            // 空/null → popup + email（向后兼容旧默认行为）
            // 兼容旧单值数据（popup/email/sms 任意一个）。
            var reminderTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(evt.ReminderType))
            {
                foreach (var part in evt.ReminderType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!string.IsNullOrEmpty(part)) reminderTypes.Add(part.ToLowerInvariant());
                }
            }
            var sendPopup = reminderTypes.Count == 0 || reminderTypes.Contains("popup");
            var sendEmail = reminderTypes.Count == 0 || reminderTypes.Contains("email");
            var sendSms = reminderTypes.Contains("sms");

            var channels = new List<string>();
            if (sendPopup) channels.Add("popup+inapp");
            if (sendEmail) channels.Add("email");
            if (sendSms) channels.Add("sms(inapp-fallback)");

            _logger.LogInformation(
                "日历提醒发送成功: event_id={EventId}, user_id={UserId}, title={Title}, channels={Channels}, reminderType={ReminderType}",
                evt.Id, evt.UserId, evt.Title, string.Join("+", channels), evt.ReminderType ?? "(null)");

            if (sendPopup) { CalendarMetrics.RecordReminderSent("inapp"); CalendarMetrics.RecordReminderSent("signalr"); }
            if (sendEmail) CalendarMetrics.RecordReminderSent("email");
            if (sendSms) CalendarMetrics.RecordReminderSent("sms");

            evt.IsReminderSent = true;
            evt.LastReminderSentAt = DateTime.UtcNow;
            // P2-A#5 #7.2：写 ReminderSentAt（UTC 偏移）+ 将确认状态重置为 pending，
            // 便于 CalendarReminderFallbackJob 在窗口届满后扫描兜底通道。
            // 字段型为 DateTimeOffset，需用 UtcNow 的 Offset 语义
            evt.ReminderSentAt = DateTimeOffset.UtcNow;
            var reminderSentAt = evt.ReminderSentAt.Value;
            evt.ReminderConfirmation = "pending";
            evt.UpdatedAt = DateTime.UtcNow;

            // === 站内通知 + SignalR 弹窗（popup 或 null/空 时发送）===
            if (sendPopup && evt.UserId.HasValue)
            {
                try
                {
                    var title = $"日程提醒：{evt.Title}";
                    var message = evt.IsAllDay
                        ? $"您的日程「{evt.Title}」将于今天开始。"
                        : $"您的日程「{evt.Title}」将于 {effectiveStartAt:HH:mm} 开始。";
                    var link = $"/calendar/{evt.Id}";

                    await _notificationService.SendNotificationAsync(
                        evt.UserId.Value, title, message, link).ConfigureAwait(false);

                    var userId = evt.UserId.Value;
                    var effectiveEnd = evt.EndAt;
                    if (evt.IsRecurring && effectiveEnd.HasValue)
                    {
                        var duration = effectiveEnd.Value - evt.StartAt;
                        effectiveEnd = effectiveStartAt + duration;
                    }
                    var payload = new
                    {
                        eventId = evt.Id,
                        title = evt.Title ?? string.Empty,
                        startAt = effectiveStartAt,
                        endAt = effectiveEnd,
                        isAllDay = evt.IsAllDay,
                        reminderMinutes = evt.ReminderMinutes,
                        sentAt = reminderSentAt.DateTime,
                    };
                    await _notificationHubService.SendToUser(userId, "CalendarReminder", payload)
                        .ConfigureAwait(false);
                    await WriteAttemptAsync(evt.Id, "signalr", 1, "sent", $"user_{userId}")
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "发送提醒通知(站内/SignalR)失败: event_id={EventId}, user_id={UserId}, title={Title}",
                        evt.Id, evt.UserId, evt.Title);
                    CalendarMetrics.RecordReminderSent("signalr", "failed");
                    await WriteAttemptAsync(evt.Id, "signalr", 1, "failed", error: ErrorSanitizer.Sanitize(ex))
                        .ConfigureAwait(false);
                    failedCount++;
                }
            }

            // === 邮件（email 或 null/空 时发送）===
            if (sendEmail)
            {
                try
                {
                    var senderMailbox = await ResolveSenderMailboxAsync(evt.MailboxId, senderMailboxCache).ConfigureAwait(false);
                    var emailQueued = await SendCalendarReminderEmailAsync(evt, effectiveStartAt, reminderTemplate, senderMailbox).ConfigureAwait(false);
                    if (emailQueued)
                    {
                        // T000090-new：主流程邮件真实入队后写 attempt，供 fallback 判定避免重复补发邮件
                        await WriteAttemptAsync(evt.Id, "email", 1, "sent").ConfigureAwait(false);
                    }
                    else
                    {
                        // 无可发邮箱/无收件人/入队失败 → 不标记 sent，允许 fallback 在无兜底通道时尝试
                        CalendarMetrics.RecordReminderSent("email", "failed");
                        failedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "发送提醒邮件失败: event_id={EventId}, user_id={UserId}, title={Title}",
                        evt.Id, evt.UserId, evt.Title);
                    CalendarMetrics.RecordReminderSent("email", "failed");
                    await WriteAttemptAsync(evt.Id, "email", 1, "failed", error: ErrorSanitizer.Sanitize(ex))
                        .ConfigureAwait(false);
                    failedCount++;
                }
            }

            // === SMS（无 SMS 网关，降级为站内通知）===
            if (sendSms && evt.UserId.HasValue)
            {
                _logger.LogWarning(
                    "SMS 网关未配置，降级为站内通知: event_id={EventId}, user_id={UserId}, title={Title}",
                    evt.Id, evt.UserId, evt.Title);
                try
                {
                    var title = $"日程提醒（短信降级）：{evt.Title}";
                    var message = evt.IsAllDay
                        ? $"您的日程「{evt.Title}」将于今天开始。"
                        : $"您的日程「{evt.Title}」将于 {effectiveStartAt:HH:mm} 开始。";
                    var link = $"/calendar/{evt.Id}";
                    await _notificationService.SendNotificationAsync(
                        evt.UserId.Value, title, message, link).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "SMS 降级站内通知发送失败: event_id={EventId}", evt.Id);
                    failedCount++;
                }
            }
        }

        _logger.LogInformation(
            "日历提醒扫描结束: sent={Sent}, skipped={Skipped}, failed={Failed}",
            sentCount, skippedCount, failedCount);

        return sentCount;
    }

    public async Task<Mailbox?> ResolveSenderMailboxAsync(int? mailboxId, Dictionary<int, Mailbox?> cache)
    {
        var cacheKey = mailboxId ?? 0;

        if (cache.TryGetValue(cacheKey, out var cached))
            return cached;

        Mailbox? resolved = null;

        if (mailboxId.HasValue && mailboxId.Value > 0)
        {
            var mb = await _unitOfWork.Mailboxes.GetByIdAsync(mailboxId.Value).ConfigureAwait(false);
            if (mb != null && mb.IsActive && !mb.IsDeleted())
            {
                resolved = mb;
            }
            else
            {
                _logger.LogWarning(
                    "日历事件关联邮箱 ID={MailboxId} 不可用（不存在/未激活/已删除），回退到默认发件邮箱",
                    mailboxId.Value);
            }
        }

        resolved ??= await _unitOfWork.Mailboxes.GetFirstActiveAsync().ConfigureAwait(false);

        cache[cacheKey] = resolved;
        return resolved;
    }

    public async Task<bool> SendCalendarReminderEmailAsync(
        CalendarEvent evt,
        DateTime effectiveStartAt,
        MailTemplate? template,
        Mailbox? senderMailbox)
    {
        if (senderMailbox == null)
        {
            _logger.LogWarning("无可用的发件邮箱，跳过日历提醒邮件发送（事件 ID={EventId}）", evt.Id);
            return false;
        }

        var userIds = new HashSet<int>();
        userIds.Add(evt.CreatedBy);
        if (evt.UserId.HasValue)
            userIds.Add(evt.UserId.Value);

        if (!string.IsNullOrEmpty(evt.AttendeesJson))
        {
            try
            {
                var attendees = JsonSerializer.Deserialize<List<int>>(evt.AttendeesJson);
                if (attendees != null)
                {
                    foreach (var uid in attendees)
                        userIds.Add(uid);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析 AttendeesJson 失败，事件 ID={EventId}", evt.Id);
            }
        }

        var users = await _unitOfWork.Users.GetByIdsAsync(userIds).ConfigureAwait(false);
        var emailSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in users)
        {
            if (!string.IsNullOrEmpty(user.Email) && user.Status != Domain.Enums.UserStatus.Deleted)
                emailSet.Add(user.Email.Trim());
        }

        if (!string.IsNullOrWhiteSpace(evt.ReminderEmails))
        {
            foreach (var part in evt.ReminderEmails.Split(',', ';', '\n', '\r'))
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed) && trimmed.Contains('@'))
                    emailSet.Add(trimmed);
            }
        }

        if (emailSet.Count == 0)
        {
            _logger.LogDebug("日历提醒邮件无收件人，跳过发送（事件 ID={EventId}）", evt.Id);
            return false;
        }

        var eventTypeLabel = evt.EventType switch
        {
            "due_date" => "截止日期",
            "meeting" => "会议",
            "reminder" => "提醒",
            _ => "计划任务"
        };

        var timeStr = evt.IsAllDay
            ? effectiveStartAt.ToString("yyyy年MM月dd日 全天")
            : effectiveStartAt.ToString("yyyy年MM月dd日 HH:mm");

        var duration = evt.EndAt.HasValue ? evt.EndAt.Value - evt.StartAt : TimeSpan.Zero;
        var effectiveEnd = duration > TimeSpan.Zero ? effectiveStartAt + duration : effectiveStartAt;
        var endStr = evt.EndAt.HasValue && !evt.IsAllDay
            ? effectiveEnd.ToString("yyyy年MM月dd日 HH:mm")
            : timeStr;

        var baseUrl = !string.IsNullOrWhiteSpace(_brand.FrontendUrl) ? _brand.FrontendUrl.TrimEnd('/') : string.Empty;
        var variables = new Dictionary<string, string>
        {
            ["event_title"] = evt.Title,
            ["event_type"] = eventTypeLabel,
            ["event_description"] = evt.Description ?? "（无描述）",
            ["event_start_time"] = timeStr,
            ["event_end_time"] = endStr,
            ["event_location"] = evt.Location ?? "—",
            ["event_meeting_url"] = evt.MeetingUrl ?? "—",
            ["event_status"] = evt.Status switch
            {
                "completed" => "已完成",
                "cancelled" => "已取消",
                _ => "待处理"
            },
            ["event_priority"] = evt.Priority.HasValue ? $"{evt.Priority.Value}/5" : "—",
            ["event_url"] = $"{baseUrl}/calendar/{evt.Id}",
            ["event_id"] = evt.Id.ToString(),
            ["company_name"] = _brand.CompanyName,
            ["frontend_url"] = _brand.FrontendUrl,
            ["login_url"] = _brand.LoginUrl,
            ["support_email"] = _brand.SupportEmail,
        };

        string subject;
        string bodyHtml;

        if (template != null)
        {
            subject = _renderer.Render(template.Subject, variables, new RenderOptions { HtmlEncode = false });
            bodyHtml = _renderer.Render(template.BodyHtml, variables, new RenderOptions { HtmlEncode = true });
        }
        else
        {
            subject = $"【日程提醒】{evt.Title} - {timeStr}";
            bodyHtml = BuildDefaultReminderHtml(evt, effectiveStartAt, eventTypeLabel, timeStr, endStr);
        }

        var recipients = string.Join(",", emailSet);
        try
        {
            await _emailQueueService.EnqueueEmailAsync(
                senderMailbox.Id,
                recipients,
                subject,
                bodyHtml,
                mailType: 10,
                userId: evt.CreatedBy).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "日历提醒邮件入队失败，事件 ID={EventId}", evt.Id);
            return false;
        }
    }

    public string BuildDefaultReminderHtml(
        CalendarEvent evt, DateTime effectiveStartAt, string eventTypeLabel, string timeStr, string endStr)
    {
        var baseUrl = !string.IsNullOrWhiteSpace(_brand.FrontendUrl) ? _brand.FrontendUrl.TrimEnd('/') : string.Empty;
        var priorityBadge = evt.Priority switch
        {
            >= 4 => "<span style=\"background:#ef4444;color:#fff;padding:2px 10px;border-radius:12px;font-size:12px;\">高优先级</span>",
            >= 3 => "<span style=\"background:#f59e0b;color:#fff;padding:2px 10px;border-radius:12px;font-size:12px;\">中优先级</span>",
            _ => "<span style=\"background:#10b981;color:#fff;padding:2px 10px;border-radius:12px;font-size:12px;\">普通</span>"
        };

        var locationRow = !string.IsNullOrEmpty(evt.Location)
            ? $"<tr><td style=\"padding:8px 0;color:#6b7280;width:80px;\">地点</td><td style=\"padding:8px 0;color:#111827;\">{System.Net.WebUtility.HtmlEncode(evt.Location)}</td></tr>"
            : "";

        var meetingRow = !string.IsNullOrEmpty(evt.MeetingUrl)
            ? $"<tr><td style=\"padding:8px 0;color:#6b7280;width:80px;\">会议链接</td><td style=\"padding:8px 0;\"><a href=\"{System.Net.WebUtility.HtmlEncode(evt.MeetingUrl)}\" style=\"color:#3b82f6;text-decoration:none;\">{System.Net.WebUtility.HtmlEncode(evt.MeetingUrl)}</a></td></tr>"
            : "";

        var descRow = !string.IsNullOrEmpty(evt.Description)
            ? $"<tr><td colspan=\"2\" style=\"padding:12px 0;border-top:1px solid #f3f4f6;\"><div style=\"color:#6b7280;font-size:13px;margin-bottom:6px;\">详情</div><div style=\"color:#374151;font-size:14px;line-height:1.6;white-space:pre-wrap;\">{System.Net.WebUtility.HtmlEncode(evt.Description)}</div></td></tr>"
            : "";

        return $"""
<!DOCTYPE html>
<html><body style="margin:0;padding:0;background:#f9fafb;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;">
<div style="max-width:600px;margin:0 auto;padding:24px;">
  <div style="background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.08);">
    <div style="background:linear-gradient(135deg,#3b82f6,#8b5cf6);padding:24px 28px;">
      <div style="color:#fff;font-size:12px;text-transform:uppercase;letter-spacing:1px;margin-bottom:4px;opacity:0.85;">{eventTypeLabel}提醒</div>
      <div style="color:#fff;font-size:20px;font-weight:700;">{System.Net.WebUtility.HtmlEncode(evt.Title)}</div>
    </div>
    <div style="padding:20px 28px;">
      <table style="width:100%;border-collapse:collapse;font-size:14px;">
        <tr><td style="padding:8px 0;color:#6b7280;width:80px;">开始时间</td><td style="padding:8px 0;color:#111827;font-weight:500;">{timeStr}</td></tr>
        <tr><td style="padding:8px 0;color:#6b7280;">结束时间</td><td style="padding:8px 0;color:#111827;">{endStr}</td></tr>
        <tr><td style="padding:8px 0;color:#6b7280;">优先级</td><td style="padding:8px 0;">{priorityBadge}</td></tr>
        {locationRow}
        {meetingRow}
        {descRow}
      </table>
    </div>
    <div style="background:#f9fafb;padding:16px 28px;text-align:center;">
      <a href="{baseUrl}/calendar/{evt.Id}" style="display:inline-block;padding:10px 28px;background:#3b82f6;color:#fff;text-decoration:none;border-radius:8px;font-size:14px;font-weight:500;">查看详情</a>
    </div>
  </div>
  <div style="text-align:center;padding:16px 0;color:#9ca3af;font-size:12px;">此邮件由系统自动发送，请勿直接回复</div>
</div>
</body></html>
""";
    }

    private async Task WriteAttemptAsync(
        int eventId,
        string channel,
        int attemptNo,
        string status,
        string? payload = null,
        string? error = null)
    {
        try
        {
            var attempt = new CalendarReminderAttempt
            {
                EventId = eventId,
                AttemptChannel = channel,
                AttemptNo = attemptNo,
                Status = status,
                Payload = payload,
                ErrorDetail = error,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _unitOfWork.Repository<CalendarReminderAttempt>().AddAsync(attempt)
                .ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "写入提醒投递审计记录失败（EventId={EventId}, Channel={Channel}, Status={Status}）",
                eventId, channel, status);
        }
    }
}
