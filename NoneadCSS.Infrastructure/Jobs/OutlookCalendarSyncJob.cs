using NoneadCSS.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace NoneadCSS.Infrastructure.Jobs;

/// <summary>
/// Outlook 日历双向同步定时任务
/// 默认每 30 分钟触发一次（可通过 Quartz:Jobs:OutlookCalendarSync 配置覆盖）
/// 扫描所有启用同步的用户，调用 IOutlookSyncService.SyncAllEnabledUsersAsync
/// </summary>
[DisallowConcurrentExecution]
public class OutlookCalendarSyncJob : RetryableJobBase
{
    private readonly IServiceProvider _serviceProvider;

    public OutlookCalendarSyncJob(IServiceProvider serviceProvider, ILogger<OutlookCalendarSyncJob> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteInternal(IJobExecutionContext context)
    {
        using var scope = _serviceProvider.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<IOutlookSyncService>();

        _logger.LogInformation("Outlook 日历双向同步定时任务开始");
        var successCount = await syncService.SyncAllEnabledUsersAsync();
        _logger.LogInformation("Outlook 日历双向同步定时任务完成，成功同步用户数={SuccessCount}", successCount);
    }
}
