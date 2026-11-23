#nullable enable
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Application.Services.Calendar;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace NoneadCSS.Infrastructure.Jobs;

/// <summary>
/// P2P3 §3.2 #7.3：提醒推送兜底补发任务（CalendarReminderFallbackJob）。
/// DEV-3 NOTE: 原规范 cron "* * * * * ?"（每秒）已修正为 "0 * * * * ?"（每分钟），
/// 用于降低 DB 负载并显著降低 SELECT→UPDATE 竞态窗口概率。
/// 当前端在确认窗口（默认 5 分钟）内未 SignalR ACK 时，
/// 本 Job 先对每个 pending 事件执行原子 CLAIM（pending→fallback_in_progress），
/// 再独立触发 EMAIL + IM 两个通道的补发（各自独立 try-catch，互不影响），
/// 最后用 ExecuteUpdateAsync 原子更新 ReminderConfirmation (fallback_sent 或 failed)。
/// </summary>
[DisallowConcurrentExecution]
public class CalendarReminderFallbackJob : RetryableJobBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<CalendarOptions> _calendarOptions;

    public CalendarReminderFallbackJob(
        IServiceProvider serviceProvider,
        IOptions<CalendarOptions> calendarOptions,
        ILogger<CalendarReminderFallbackJob> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
        _calendarOptions = calendarOptions;
    }

    protected override async Task ExecuteInternal(IJobExecutionContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var ct = context.CancellationToken;
        var windowMinutes = _calendarOptions.Value.ReminderConfirmationWindowMinutes;
        if (windowMinutes <= 0) windowMinutes = 5;
        var windowCutoff = now.AddMinutes(-windowMinutes);
        var ancientHours = _calendarOptions.Value.FallbackAncientCutoffHours;
        if (ancientHours <= 0) ancientHours = 48;
        var ancientCutoff = now.AddHours(-ancientHours);

        using var scope = _serviceProvider.CreateScope();

        var ctx = scope.ServiceProvider.GetRequiredService<NoneadCSSDbContext>();

        // H-01：使用 FromSqlInterpolated 由 EF Core 自动参数化（不再手动 ToString("o") 拼接），
        // 规避 ISO 8601 +08:00 偏移字面量与 SQLite 列存储格式不一致导致的字符串字典序比较错误。
        // SQLite EF Core 对 DateTimeOffset? 与 DateTimeOffset 的 LINQ < 比较不支持翻译（仅 ==），
        // 详见 https://learn.microsoft.com/ef/core/providers/sqlite/limitations——DateTimeOffset 的
        // 排序/比较需在客户端评估；为保留 SQL 端 48h 上限过滤（evt13 必须被 WHERE 排除），
        // 这里使用 FromSqlInterpolated + DateTimeOffset 参数：EF Core 会按 Provider 规则将参数
        // 格式化为与列存储一致的字符串字面量，字典序等价于时间顺序（ISO 8601 固定字段宽度）。
        // 索引利用：复合索引 IX_CalendarEvents_ReminderDue (ReminderSentAt, Status, DeletedAt) 覆盖此查询。
        // ISoftDelete 全局查询过滤器会自动追加 DeletedAt IS NULL，无需重复。
        var pendingEvents = await ctx.CalendarEvents
            .FromSqlInterpolated($@"
                SELECT e.*
                FROM ""CalendarEvents"" AS e
                WHERE e.""ReminderSentAt"" IS NOT NULL
                  AND e.""ReminderConfirmation"" = 'pending'
                  AND e.""ReminderSentAt"" < {windowCutoff}
                  AND e.""ReminderSentAt"" >= {ancientCutoff}")
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (pendingEvents.Count == 0)
        {
            _logger.LogDebug("FallbackJob：无待兜底补发的提醒事件（窗口={WindowMin}min，{AncientH}h上限）", windowMinutes, ancientHours);
            return;
        }

        _logger.LogInformation(
            "FallbackJob：扫描到 {Count} 个未在窗口内确认的提醒事件，启动邮件+IM 独立补发流程",
            pendingEvents.Count);

        var attemptBatch = new List<CalendarReminderAttempt>();

        foreach (var evt in pendingEvents)
        {
            ct.ThrowIfCancellationRequested();

            bool emailOk = false;
            bool imOk = false;

            int claimedRows = await ctx.CalendarEvents
                .Where(e => e.Id == evt.Id
                         && e.ReminderConfirmation == "pending"
                         && e.DeletedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(e => e.ReminderConfirmation, "fallback_in_progress"))
                .ConfigureAwait(false);
            if (claimedRows == 0)
            {
                _logger.LogDebug("Fallback: event {EventId} no longer pending (already confirmed or claimed by other instance), skip", evt.Id);
                continue;
            }

            // ====== Channel 1：EMAIL（独立 try-catch）======
            // T000090-new：主流程（CalendarReminderJob）已在邮件通道真实入队（attempt_channel='email' status='sent'）时，
            // 跳过邮件兜底补发，避免同一提醒收到两封邮件；此时仅补发 IM 通道。
            bool mainEmailAlreadySent = await ctx.Set<CalendarReminderAttempt>()
                .AsNoTracking()
                .AnyAsync(a => a.EventId == evt.Id
                            && a.AttemptChannel == "email"
                            && a.Status == "sent", ct)
                .ConfigureAwait(false);
            if (mainEmailAlreadySent)
            {
                emailOk = true;
                _logger.LogDebug(
                    "Fallback: 主流程已发送邮件 (EventId={EventId})，跳过邮件补发以避免重复",
                    evt.Id);
            }
            else
            {
                try
                {
                    emailOk = await TrySendFallbackEmailAsync(scope.ServiceProvider, evt, _logger, ct)
                        .ConfigureAwait(false);
                    attemptBatch.Add(MakeAttempt(evt.Id, "email", 1,
                        emailOk ? "sent" : "failed",
                        error: emailOk ? null : "TrySendFallbackEmailAsync returned false"));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Fallback Email 通道异常（EventId={EventId}）", evt.Id);
                    attemptBatch.Add(MakeAttempt(evt.Id, "email", 1, "failed", error: ErrorSanitizer.Sanitize(ex)));
                }
            }

            // ====== Channel 2：IM（独立 try-catch）======
            try
            {
                imOk = await TrySendFallbackImAsync(scope.ServiceProvider, evt, _logger, ct)
                    .ConfigureAwait(false);
                attemptBatch.Add(MakeAttempt(evt.Id, "im", 1,
                    imOk ? "sent" : "failed",
                    error: imOk ? null : "TrySendFallbackImAsync returned false"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback IM 通道异常（EventId={EventId}）", evt.Id);
                attemptBatch.Add(MakeAttempt(evt.Id, "im", 1, "failed", error: ErrorSanitizer.Sanitize(ex)));
            }

            var finalStatus = (emailOk || imOk) ? "fallback_sent" : "failed";
            await ctx.CalendarEvents
                .Where(e => e.Id == evt.Id
                         && e.ReminderConfirmation == "fallback_in_progress")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(e => e.ReminderConfirmation, finalStatus)
                    .SetProperty(e => e.UpdatedAt, DateTime.UtcNow))
                .ConfigureAwait(false);
        }

        if (attemptBatch.Count > 0)
        {
            ctx.AddRange(attemptBatch);
            await ctx.SaveChangesAsync(ct)
                .ConfigureAwait(false);
        }
    }

    private static CalendarReminderAttempt MakeAttempt(
        int eventId,
        string channel,
        int attemptNo,
        string status,
        string? payload = null,
        string? error = null)
    {
        return new CalendarReminderAttempt
        {
            EventId = eventId,
            AttemptChannel = channel,
            AttemptNo = attemptNo,
            Status = status,
            Payload = payload,
            ErrorDetail = error,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// 通道 1：邮件兜底补发。
    /// 复用 CalendarReminderService.SendCalendarReminderEmailAsync 实际发送邮件，
    /// 解析邮件模板和发件邮箱，确保邮件真正入队。
    /// </summary>
    internal static async Task<bool> TrySendFallbackEmailAsync(
        IServiceProvider sp,
        CalendarEvent evt,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            var reminderSvc = sp.GetService<ICalendarReminderService>();
            if (reminderSvc is null)
            {
                logger.LogWarning("Fallback email: ICalendarReminderService 不可用，跳过 (EventId={EventId})", evt.Id);
                return false;
            }

            var unitOfWork = sp.GetService<IUnitOfWork>();
            if (unitOfWork is null)
            {
                logger.LogWarning("Fallback email: IUnitOfWork 不可用，跳过 (EventId={EventId})", evt.Id);
                return false;
            }

            // 获取邮件模板
            var template = await unitOfWork.MailTemplates.GetBySlugAsync("calendar-reminder").ConfigureAwait(false);
            if (template != null && !template.IsActive)
                template = null;

            // 解析发件邮箱（带缓存）
            var senderMailbox = await reminderSvc.ResolveSenderMailboxAsync(
                evt.MailboxId, new Dictionary<int, Mailbox?>()).ConfigureAwait(false);

            // 实际发送邮件（返回是否真实入队：无发件邮箱/无收件人/入队失败时为 false）
            var queued = await reminderSvc.SendCalendarReminderEmailAsync(
                evt, evt.StartAt, template, senderMailbox).ConfigureAwait(false);

            logger.LogInformation("Fallback email 处理完成 (EventId={EventId}, queued={Queued})", evt.Id, queued);
            return queued;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fallback email failed for event {EventId}", evt.Id);
            return false;
        }
    }

    /// <summary>
    /// 通道 2：IM/站内通知兜底补发。
    /// 使用 INotificationService 发送高优先级站内通知，确保用户未收到 SignalR 弹窗时仍能看到提醒。
    /// </summary>
    internal static async Task<bool> TrySendFallbackImAsync(
        IServiceProvider sp,
        CalendarEvent evt,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            var notificationSvc = sp.GetService<INotificationService>();
            if (notificationSvc is null)
            {
                logger.LogWarning("Fallback IM: INotificationService 不可用，跳过 (EventId={EventId})", evt.Id);
                return false;
            }

            if (!evt.UserId.HasValue)
            {
                logger.LogDebug("Fallback IM: 事件无关联用户，跳过 (EventId={EventId})", evt.Id);
                return false;
            }

            var title = $"日程提醒（补发）：{evt.Title}";
            var message = evt.IsAllDay
                ? $"您的日程「{evt.Title}」即将开始，请及时查看。"
                : $"您的日程「{evt.Title}」即将于 {evt.StartAt:HH:mm} 开始，请及时查看。";
            var link = $"/calendar/{evt.Id}";

            await notificationSvc.SendNotificationAsync(
                evt.UserId.Value, title, message, link).ConfigureAwait(false);

            logger.LogInformation("Fallback IM/站内通知发送成功 (EventId={EventId}, UserId={UserId})", evt.Id, evt.UserId);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fallback IM failed for event {EventId}", evt.Id);
            return false;
        }
    }
}
