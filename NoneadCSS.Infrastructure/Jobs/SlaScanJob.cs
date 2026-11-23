using NoneadCSS.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace NoneadCSS.Infrastructure.Jobs;

/// <summary>
/// SLA 违规扫描定时任务。每 5 分钟扫描一次活跃会话，检测 SLA 违规。
/// </summary>
[DisallowConcurrentExecution]
public class SlaScanJob : RetryableJobBase
{
    private readonly IServiceProvider _serviceProvider;

    public SlaScanJob(IServiceProvider serviceProvider, ILogger<SlaScanJob> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteInternal(IJobExecutionContext context)
    {
        _logger.LogInformation("开始执行 SLA 违规扫描任务");

        using var scope = _serviceProvider.CreateScope();
        var slaService = scope.ServiceProvider.GetRequiredService<ISlaService>();

        var result = await slaService.ScanViolationsAsync();

        _logger.LogInformation(
            "SLA 违规扫描完成：扫描 {Scanned} 个会话，新增 {NewViolations} 条违规，{Breached} 条已超时",
            result.Scanned, result.NewViolations, result.Breached);
    }
}
