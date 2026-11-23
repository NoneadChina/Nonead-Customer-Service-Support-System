using NoneadCSS.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace NoneadCSS.Infrastructure.Jobs;

[DisallowConcurrentExecution]
public class ProcessCustomReportSchedulesJob : RetryableJobBase
{
    private readonly IServiceProvider _serviceProvider;

    public ProcessCustomReportSchedulesJob(IServiceProvider serviceProvider, ILogger<ProcessCustomReportSchedulesJob> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteInternal(IJobExecutionContext context)
    {
        // S10: 接入 RetryableJobBase —— 失败自动按 5/10/30 秒重试 3 次，最终失败向外抛出
        // 供 Quartz 标记执行失败，不再静默吞异常
        _logger.LogInformation("开始执行自定义报表调度处理任务");

        using var scope = _serviceProvider.CreateScope();
        var customReportService = scope.ServiceProvider.GetRequiredService<ICustomReportService>();

        var now = DateTime.UtcNow;
        await customReportService.ProcessScheduledReportsAsync(now);

        _logger.LogInformation("自定义报表调度处理完成 Time={Time}", now);
    }
}
