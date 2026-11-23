using NoneadCSS.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace NoneadCSS.Infrastructure.Jobs;

[DisallowConcurrentExecution]
public class PurgeCalendarTrashJob : RetryableJobBase
{
    private readonly IServiceProvider _serviceProvider;

    public PurgeCalendarTrashJob(IServiceProvider serviceProvider, ILogger<PurgeCalendarTrashJob> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteInternal(IJobExecutionContext context)
    {
        using var scope = _serviceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var calendarService = scope.ServiceProvider.GetRequiredService<ICalendarService>();

        var retentionDays = config.GetValue<int?>("Calendar:TrashRetentionDays")
                            ?? config.GetValue<int>("Trash:CalendarRetentionDays", 30);
        if (retentionDays < 0) retentionDays = 30;

        _logger.LogInformation("日历回收站清理开始，保留天数={RetentionDays}", retentionDays);
        var purged = await calendarService.PurgeTrashAsync(retentionDays);
        _logger.LogInformation("日历回收站清理完成，清理条数={Purged}", purged);
    }
}
