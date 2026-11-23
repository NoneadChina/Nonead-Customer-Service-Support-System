using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;

namespace NoneadCSS.Api.Services;

/// <summary>
/// 任务 D#3：Outlook 同步重试队列处理器（BackgroundService）。
/// 每 30 秒扫描一次重试队列，取 Status=Pending 且 NextRetryAt &lt;= Now 的项执行重试。
/// - 指数退避：NextRetryAt = Now + 2^RetryCount * 30s
/// - 超过 MaxRetry 次则标记为 DeadLettered
/// </summary>
public class OutlookRetryProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutlookRetryProcessor> _logger;

    /// <summary>
    /// 扫描间隔：30 秒
    /// </summary>
    private const int ScanIntervalSeconds = 30;

    /// <summary>
    /// 每批最多出队 20 条
    /// </summary>
    private const int BatchSize = 20;

    public OutlookRetryProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<OutlookRetryProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outlook 重试队列处理器已启动，扫描间隔 {Interval}s", ScanIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outlook 重试队列扫描过程中发生异常（已捕获，下一轮继续）");
            }

            // 每 ScanIntervalSeconds 扫描一次
            await Task.Delay(TimeSpan.FromSeconds(ScanIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Outlook 重试队列处理器已停止");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IOutlookSyncQueueRepository>();
        var syncService = scope.ServiceProvider.GetRequiredService<IOutlookSyncService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // 1. 出队一批（原子标记为 Processing，避免并发重复消费）
        var batch = await queue.DequeueBatchAsync(BatchSize, ct);
        if (batch.Count == 0)
            return;

        _logger.LogInformation("Outlook 重试队列出队 {Count} 项，开始处理", batch.Count);

        foreach (var item in batch)
        {
            try
            {
                // 2. 完整实现：真正尝试重新同步该 Mailbox（复用 OutlookSyncService 的同步逻辑与令牌）。
                //    仅当该 Mailbox 仍有关联用户时可触发同步；否则退化为"只推进重试状态"的优雅降级，
                //    交由 OutlookCalendarSyncJob 每 30 分钟的常规同步兜底。
                var currentUserId = await ResolveMailboxUserIdAsync(unitOfWork, item.MailboxId, ct);
                if (currentUserId == null)
                {
                    await AdvanceRetryOnlyAsync(queue, item, "无法解析 Mailbox 关联用户", ct);
                    continue;
                }

                // 3. 触发真实重同步（forceDeltaOnly=false：DeltaToken 失效时回退全量，提高自愈成功率）。
                var syncResult = await syncService.SyncByMailboxIdAsync(
                    item.MailboxId, currentUserId.Value, forceDeltaOnly: false);

                if (syncResult.Success)
                {
                    await queue.MarkCompletedAsync(item.Id, ct);
                    _logger.LogInformation(
                        "Outlook 重试队列项 {Id}（Mailbox={MailboxId}, Direction={Dir}, Op={Op}）重新同步成功，标记完成",
                        item.Id, item.MailboxId, item.Direction, item.Operation);
                    continue;
                }

                // 4. 同步失败：记录错误并推进重试状态（指数退避），超过 MaxRetry 交由死信处理。
                var errorForRetry = syncResult.ErrorMessage ?? item.LastError ?? "重新同步失败";
                await AdvanceRetryOnlyAsync(queue, item, errorForRetry, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理 Outlook 重试队列项 {Id} 时发生异常", item.Id);
                try
                {
                    await queue.IncrementRetryAsync(item.Id,
                        $"RetryProcessor 处理异常：{ex.Message}", ct);
                }
                catch
                {
                    // 忽略：避免单个项异常阻塞整批
                }
            }
        }
    }

    /// <summary>
    /// 解析给定 Mailbox 的关联用户 ID（用于权限门禁）；返回 null 表示该 Mailbox 已无关联用户。
    /// </summary>
    private static async Task<int?> ResolveMailboxUserIdAsync(
        IUnitOfWork unitOfWork, int mailboxId, CancellationToken ct)
    {
        var muRepo = unitOfWork.Repository<MailboxUser>();
        return await muRepo.GetQueryable(false)
            .AsNoTracking()
            .Where(x => x.MailboxId == mailboxId)
            .Select(x => (int?)x.UserId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// 优雅降级路径：只推进重试状态（RetryCount + 1，NextRetryAt 指数退避），
    /// 超过 MaxRetry 则标记为 DeadLettered；真正重同步交由常规同步兜底。
    /// </summary>
    private async Task AdvanceRetryOnlyAsync(
        IOutlookSyncQueueRepository queue, OutlookSyncQueueItem item, string errorForRetry, CancellationToken ct)
    {
        errorForRetry = string.IsNullOrEmpty(errorForRetry) ? "RetryProcessor 重新尝试" : errorForRetry;

        var canContinue = await queue.IncrementRetryAsync(item.Id, errorForRetry, ct);
        if (!canContinue)
        {
            await queue.MarkDeadLetteredAsync(item.Id,
                $"OutlookRetryProcessor：已超过 MaxRetry={item.MaxRetry} 次，最后错误={errorForRetry}", ct);
            _logger.LogWarning(
                "Outlook 重试队列项 {Id}（Mailbox={MailboxId}, Direction={Dir}, Op={Op}）已达 {MaxRetry} 次上限，标记为死信",
                item.Id, item.MailboxId, item.Direction, item.Operation, item.MaxRetry);
        }
        else
        {
            _logger.LogInformation(
                "Outlook 重试队列项 {Id} 完成第 {Retry}/{Max} 次指数退避：NextRetryAt={Next}（{Reason}）",
                item.Id, item.RetryCount, item.MaxRetry,
                item.NextRetryAt?.ToString("o") ?? "立即", errorForRetry);
        }
    }
}
