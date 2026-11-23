#nullable enable
using NoneadCSS.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace NoneadCSS.Infrastructure.Jobs;

/// <summary>
/// 日历提醒扫描定时任务。每分钟扫描一次即将到来的日程，发送提醒通知。
/// P2P3 #7.2：继承 RetryableJobBase，支持指数退避重试（5/10/30s，最多 3 次）。
/// </summary>
[DisallowConcurrentExecution]
public class CalendarReminderJob : RetryableJobBase
{
    private readonly IServiceProvider _serviceProvider;

    public CalendarReminderJob(IServiceProvider serviceProvider, ILogger<CalendarReminderJob> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteInternal(IJobExecutionContext context)
    {
        _logger.LogInformation("开始执行日历提醒扫描任务");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var calendarService = scope.ServiceProvider.GetRequiredService<ICalendarService>();

            var sentCount = await calendarService.ScanAndSendRemindersAsync()
                .ConfigureAwait(false);

            _logger.LogInformation(
                "日历提醒扫描完成：发送了 {SentCount} 条提醒",
                sentCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "日历提醒扫描任务执行失败");
            throw;
        }
    }
}
