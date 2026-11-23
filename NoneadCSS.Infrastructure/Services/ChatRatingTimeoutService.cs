using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// 在线聊天满意度评分超时自动关闭后台服务。
/// 需求：客服主动关闭聊天后发感谢&满意度调查消息，如果客户 3 分钟内没有评分则自动关闭会话并发邮件通知。
///
/// 策略：
///   - 启动延迟 5 秒（等待其他服务就绪），之后每 60 秒扫描 1 次
///   - 每次最多处理 100 条（可配置），避免大流量长时间占用
///   - 处理逻辑：对 status=awaiting_rating 且 RatingTimeoutAt &lt;= UtcNow 的 ChatSession，
///     调用 ChatService.ReallyCloseChatSessionAsync，写入"等待超时，会话已自动关闭"system message 并发送关闭邮件
///   - 单条会话出错记录 ERROR 并跳过，保证其他会话仍能被关闭
/// </summary>
public class ChatRatingTimeoutService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChatRatingTimeoutService> _logger;

    public ChatRatingTimeoutService(
        IServiceProvider serviceProvider,
        ILogger<ChatRatingTimeoutService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ChatRatingTimeoutService 启动，每 60 秒扫描一次 awaiting_rating 超时会话");
        try
        {
            // 启动先等 5 秒，避免应用刚起来就立刻刷数据库
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await ProcessExpiredSessionsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("ChatRatingTimeoutService 停止（收到取消请求）");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChatRatingTimeoutService 扫描超时会话出错，将在下一轮重试");
            }
        }
    }

    private async Task ProcessExpiredSessionsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var chatService = sp.GetRequiredService<IChatService>();
        var uow = sp.GetRequiredService<NoneadCSS.Domain.IUnitOfWork>();
        var chatRepo = uow.ChatSessions;

        const int batchLimit = 100;
        var now = DateTime.UtcNow;
        var expiredList = await chatRepo.GetAwaitingRatingExpiredAsync(now, batchLimit).ConfigureAwait(false);
        if (expiredList == null || expiredList.Count == 0)
        {
            _logger.LogDebug("ChatRatingTimeoutService：当前无 awaiting_rating 超时会话（UtcNow={UtcNow}）", now);
            return;
        }

        _logger.LogInformation("ChatRatingTimeoutService：发现 {Count} 个超时未评分会话，开始逐个真关闭", expiredList.Count);
        int succeeded = 0, failed = 0;

        foreach (var session in expiredList)
        {
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                // ReallyCloseChatSessionAsync 需要 tracked 的 session 对象，当前已经是 AsTracking()
                await chatService.ReallyCloseChatSessionAsync(
                    session,
                    closeReasonSystemMsg: $"等待客户满意度评价超时（{ChatService.RatingWaitTimeout.TotalMinutes} 分钟），会话已自动关闭。").ConfigureAwait(false);
                succeeded++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex,
                    "ChatRatingTimeoutService 自动关闭 ChatSession 失败：SessionId={SessionId}, CustomerEmail={CustomerEmail}",
                    session.Id, session.CustomerEmail);
            }
        }

        _logger.LogInformation("ChatRatingTimeoutService：本轮结束（成功 {Succeeded}，失败 {Failed}，批次上限 {Limit}，UtcNow={UtcNow}）",
            succeeded, failed, batchLimit, DateTime.UtcNow);
    }
}
