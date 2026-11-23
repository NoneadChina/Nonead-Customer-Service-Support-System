using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace NoneadCSS.Infrastructure.Jobs;

/// <summary>
/// 更新文件夹计数器的后台任务
/// 对应 PHP: app/Jobs/UpdateFolderCounters.php
/// </summary>
[DisallowConcurrentExecution]
public class UpdateFolderCountersJob : RetryableJobBase
{
    private readonly IServiceProvider _serviceProvider;

    public UpdateFolderCountersJob(IServiceProvider serviceProvider, ILogger<UpdateFolderCountersJob> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteInternal(IJobExecutionContext context)
    {
        var jobData = context.JobDetail.JobDataMap;
        var mailboxId = jobData.GetInt("mailboxId");
        var folderId = jobData.GetInt("folderId");

        using var scope = _serviceProvider.CreateScope();
        var folderService = scope.ServiceProvider.GetRequiredService<IFolderService>();

        if (folderId > 0)
        {
            await folderService.UpdateFolderCountersByIdAsync(folderId);
            _logger.LogInformation("已更新文件夹 {FolderId} 计数器", folderId);
        }
        else if (mailboxId > 0)
        {
            await folderService.UpdateMailboxFolderCountersAsync(mailboxId);
            _logger.LogInformation("已更新邮箱 {MailboxId} 所有文件夹计数器", mailboxId);
        }
        else
        {
            await folderService.UpdateAllFolderCountersAsync();
            _logger.LogInformation("已更新所有文件夹计数器");
        }
    }
}
