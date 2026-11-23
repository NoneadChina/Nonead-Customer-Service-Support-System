using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure.Jobs;
using NoneadCSS.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Quartz;

namespace NoneadCSS.Infrastructure.Listeners;

/// <summary>
/// 更新邮箱计数器的事件监听器
/// 对应 PHP: app/Listeners/UpdateMailboxCounters.php
/// </summary>
public class UpdateMailboxCountersListener : IListener<MailboxUpdatedEvent>
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ILogger<UpdateMailboxCountersListener> _logger;

    public UpdateMailboxCountersListener(ISchedulerFactory schedulerFactory, ILogger<UpdateMailboxCountersListener> logger)
    {
        _schedulerFactory = schedulerFactory;
        _logger = logger;
    }

    public async Task HandleAsync(MailboxUpdatedEvent @event, CancellationToken cancellationToken = default)
    {
        // 异常由 DomainEventDispatcher 统一捕获并解包 TargetInvocationException 后记录
        var scheduler = await _schedulerFactory.GetScheduler();
        // 使用基于 mailboxId 的固定 JobKey，确保 [DisallowConcurrentExecution] 能正确生效
        // 避免短时间内多次触发产生键碰撞（ObjectAlreadyExistsException）
        var jobKey = new JobKey("UpdateFolderCountersJob_" + @event.MailboxId);
        var triggerKey = new TriggerKey("UpdateFolderCountersTrigger_" + @event.MailboxId + "_" + Guid.NewGuid().ToString("N"));

        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .StartNow()
            .ForJob(jobKey)
            .Build();

        // 检查 job 是否已存在（避免重复创建）
        var existingJob = await scheduler.GetJobDetail(jobKey);
        if (existingJob == null)
        {
            var job = JobBuilder.Create<UpdateFolderCountersJob>()
                .WithIdentity(jobKey)
                .StoreDurably()
                .UsingJobData("mailboxId", @event.MailboxId)
                .Build();
            await scheduler.AddJob(job, replace: false);
        }

        await scheduler.ScheduleJob(trigger);

        _logger.LogInformation("邮箱 {MailboxId} 计数器更新任务已加入队列", @event.MailboxId);
    }
}
