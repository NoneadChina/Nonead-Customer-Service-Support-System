using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimeKit;
using Quartz;

namespace NoneadCSS.Infrastructure.Jobs;

/// <summary>
/// 邮件收取定时任务 - 定期从邮箱收取新邮件并创建/更新会话
/// </summary>
[DisallowConcurrentExecution]
public class FetchEmailJob : RetryableJobBase
{
    private readonly IServiceProvider _serviceProvider;

    public FetchEmailJob(IServiceProvider serviceProvider, ILogger<FetchEmailJob> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteInternal(IJobExecutionContext context)
    {
        _logger.LogInformation("开始执行邮件收取任务 - {Time}", DateTime.Now);

        // 取出 Quartz 提供的取消令牌，传递给下游所有 await 调用，
        // 确保任务被 Shutdown 时能立即中断并正确释放资源（避免长事务与 SQLite 写锁）。
        var ct = context.CancellationToken;

        using var scope = _serviceProvider.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var configuration = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

        var mailboxes = await unitOfWork.Mailboxes.GetActiveTrackedAsync();
        var attachmentBasePath = Path.Combine(env.ContentRootPath, configuration["Attachment:UploadPath"] ?? "wwwroot/attachments");

        foreach (var mailbox in mailboxes)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (string.IsNullOrEmpty(mailbox.InHost) || string.IsNullOrEmpty(mailbox.InUsername))
                {
                    _logger.LogDebug("邮箱 {Mailbox} 未配置收件服务器，跳过", mailbox.Email);
                    continue;
                }

                if (mailbox.LastFetchAt.HasValue)
                {
                    var minutesSinceLastFetch = (DateTime.UtcNow - mailbox.LastFetchAt.Value).TotalMinutes;
                    if (minutesSinceLastFetch < mailbox.FetchFrequency)
                    {
                        _logger.LogDebug("邮箱 {Mailbox} 收取间隔为 {Frequency} 分钟，上次收取于 {LastFetchAt:HH:mm:ss}，跳过",
                            mailbox.Email, mailbox.FetchFrequency, mailbox.LastFetchAt.Value);
                        continue;
                    }
                }

                _logger.LogInformation("正在收取邮箱 {Mailbox} 的邮件（间隔：{Frequency} 分钟）", mailbox.Email, mailbox.FetchFrequency);

                // T17: 每个邮箱使用独立 scope/UoW 处理邮件，避免单个 DbContext 跟踪全部邮箱与全部邮件，
                // 防止 ChangeTracker 无限膨胀与最终 SaveChanges 提交规模过大。
                // mailbox 实体本身仍由外层 UoW 跟踪（LastFetchAt/LastUid 在最后统一持久化）。
                using (var mailboxScope = _serviceProvider.CreateScope())
                {
                    var mailboxUoW = mailboxScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var mailboxEmailService = mailboxScope.ServiceProvider.GetRequiredService<IEmailService>();
                    var mailboxIngestion = mailboxScope.ServiceProvider.GetRequiredService<IEmailIngestionService>();
                    await FetchMailboxEmailsAsync(mailbox, mailboxUoW, mailboxEmailService, mailboxIngestion, attachmentBasePath, ct);
                }

                mailbox.LastFetchAt = DateTime.UtcNow;
                // mailbox 已通过 GetActiveTrackedAsync 查询（tracking），直接 SaveChangesAsync 即可持久化
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "收取邮箱 {Mailbox} 邮件时出错", mailbox.Email);
            }
        }

        // SaveChangesAsync 必须在 try/catch 内（之前放在 try/catch 之外，任意邮箱异常会中断整个 Job）
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 正常取消，无需处理
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "邮件收取任务保存 LastFetchAt 失败");
        }

        _logger.LogInformation("邮件收取任务完成 - {Time}", DateTime.Now);
    }

    private async Task FetchMailboxEmailsAsync(
        Mailbox mailbox,
        IUnitOfWork unitOfWork,
        IEmailService emailService,
        IEmailIngestionService emailIngestionService,
        string attachmentBasePath,
        CancellationToken ct)
    {
        var (messages, newUid) = await emailService.FetchMimeMessagesAsync(mailbox, mailbox.LastUid, ct);

        _logger.LogInformation("从邮箱 {Mailbox} 获取到 {Count} 封新邮件", mailbox.Email, messages.Count);

        foreach (var message in messages)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await emailIngestionService.ProcessMessageAsync(message, mailbox, attachmentBasePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理邮件失败：Subject={Subject}", message.Subject);
            }
        }

        if (newUid > mailbox.LastUid)
        {
            mailbox.LastUid = newUid;
            // mailbox 已通过 GetActiveTrackedAsync 查询（tracking），由调用方的 SaveChangesAsync 持久化
            _logger.LogInformation("更新邮箱 {Mailbox} LastUid: {NewUid}", mailbox.Email, newUid);
        }
    }
}
