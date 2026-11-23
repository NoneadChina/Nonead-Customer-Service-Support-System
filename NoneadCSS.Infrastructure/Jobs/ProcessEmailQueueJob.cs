using NoneadCSS.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace NoneadCSS.Infrastructure.Jobs;

[DisallowConcurrentExecution]
public class ProcessEmailQueueJob : RetryableJobBase
{
    private readonly IServiceProvider _serviceProvider;

    public ProcessEmailQueueJob(IServiceProvider serviceProvider, ILogger<ProcessEmailQueueJob> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteInternal(IJobExecutionContext context)
    {
        _logger.LogInformation("开始处理邮件队列");

        using var scope = _serviceProvider.CreateScope();
        var emailQueueService = scope.ServiceProvider.GetRequiredService<IEmailQueueService>();

        await emailQueueService.ProcessQueueAsync();
        _logger.LogInformation("邮件队列处理完成");
    }
}
