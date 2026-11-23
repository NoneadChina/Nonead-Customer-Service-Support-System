using Microsoft.EntityFrameworkCore;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Api.Services;

/// <summary>
/// #5：工作流执行中断检测。
///
/// 背景：工作流由事件同步触发，执行前会写一条 Status="pending" 的 WorkflowExecutionLog
/// （见 WorkflowService.ExecuteWorkflowsForConversationAsync / ExecuteWorkflowsForChatSessionAsync）。
/// 进程若在动作执行中途被杀，这条记录会永远停在 pending。
///
/// 本服务在启动时把「超过 10 分钟仍为 pending」的记录标记为 "interrupted"，
/// 让"任务丢了"从无声无息变成可查可见。
///
/// **故意不自动重跑**：动作可能已部分执行（发信、改状态、调用 Webhook），
/// 盲目重跑会产生重复副作用。需要重跑时由人根据日志判断。
/// </summary>
public sealed class WorkflowInterruptionHostedService : BackgroundService
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowInterruptionHostedService> _logger;

    public WorkflowInterruptionHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<WorkflowInterruptionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动后稍等再扫，避免和应用启动争抢 SQLite 写锁。
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var cutoff = DateTime.UtcNow - StaleThreshold;
            var repo = unitOfWork.Repository<WorkflowExecutionLog>();

            var stale = await repo.GetQueryable(asNoTracking: false)
                .Where(l => l.Status == "pending" && l.ExecutedAt < cutoff)
                .ToListAsync(stoppingToken);

            if (stale.Count == 0)
            {
                _logger.LogInformation("工作流中断检测：无遗留 pending 记录");
                return;
            }

            foreach (var log in stale)
            {
                log.Status = "interrupted";
                log.ErrorMessage = "进程重启时该工作流仍处于 pending，已标记为中断（动作可能部分执行，请人工判断是否重跑）";
                log.ExecutedAt = log.ExecutedAt; // 保留原始执行时间，便于追溯
            }

            await unitOfWork.SaveChangesAsync(stoppingToken);

            _logger.LogWarning(
                "工作流中断检测：发现 {Count} 条遗留 pending 记录（早于 {Cutoff:u}），已标记为 interrupted。WorkflowId/ConversationId 见 WorkflowExecutionLogs。",
                stale.Count, cutoff);
        }
        catch (OperationCanceledException)
        {
            // 正常停机
        }
        catch (Exception ex)
        {
            // 检测失败不应影响应用启动
            _logger.LogError(ex, "工作流中断检测失败");
        }
    }
}
