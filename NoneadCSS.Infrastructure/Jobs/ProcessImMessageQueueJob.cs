using NoneadCSS.Application.Services.WorkflowActions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace NoneadCSS.Infrastructure.Jobs;

/// <summary>
/// IM 消息队列处理定时任务：扫描 ImMessageLog 表中状态为 Pending 且到期的记录，
/// 调用对应渠道 Client 发送，支持失败重试（指数退避）。
/// 参考 <see cref="ProcessEmailQueueJob"/> 的设计模式。
/// </summary>
[DisallowConcurrentExecution]
public class ProcessImMessageQueueJob : RetryableJobBase
{
    private readonly IServiceProvider _serviceProvider;

    public ProcessImMessageQueueJob(IServiceProvider serviceProvider, ILogger<ProcessImMessageQueueJob> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteInternal(IJobExecutionContext context)
    {
        _logger.LogInformation("开始处理 IM 消息队列");

        using var scope = _serviceProvider.CreateScope();
        var imMessageQueueService = scope.ServiceProvider.GetRequiredService<IImMessageQueueService>();

        await imMessageQueueService.ProcessQueueAsync();
        _logger.LogInformation("IM 消息队列处理完成");
    }
}
