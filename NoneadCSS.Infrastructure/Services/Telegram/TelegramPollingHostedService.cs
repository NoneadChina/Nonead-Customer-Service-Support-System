using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Infrastructure.Services.Telegram;

/// <summary>
/// Telegram 轮询（getUpdates）后台托管服务 —— 与 DingTalk/Feishu Stream HostedService 同构的可选入站通道。
/// - 仅在 TelegramOptions.Enabled 且 Mode=polling 时真正拉取；
/// - 每轮重新读取 settings（配置页切换模式无需重启进程）；
/// - 每轮创建独立 Scope：ITelegramIngestionService / IUnitOfWork 是 Scoped，不能由单例 HostedService 直接注入；
/// - 409（webhook 已登记）时按配置 deleteWebhook；429 按 retry_after 退避；401 长退避，避免无意义刷请求。
/// 轮询游标持久化在 SystemOptions（telegram.polling_offset_cursor），重启后可续跑。
/// </summary>
public class TelegramPollingHostedService : BackgroundService
{
    /// <summary>无 Update / 未启用时的最短空闲间隔（避免空转）</summary>
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);

    /// <summary>模块未启用或非 polling 模式时的检查间隔（配置热切换的响应粒度）</summary>
    private static readonly TimeSpan DisabledCheckDelay = TimeSpan.FromSeconds(15);

    /// <summary>BotToken 无效（401）时的退避，防止持续打爆 API</summary>
    private static readonly TimeSpan InvalidTokenBackoff = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 单轮未捕获异常的递增退避阶梯（第 1/2/3… 次连续失败依次取 5s、30s、60s，之后封顶 60s）；一轮成功即清零重新计数。
    /// 声明成 protected virtual 只为单元测试注入更短的阶梯（否则测试要真等 5 秒），生产路径用默认阶梯。
    /// </summary>
    protected virtual IReadOnlyList<TimeSpan> FailureBackoffLadder { get; } =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1)
    ];

    private readonly IServiceProvider _services;
    private readonly ILogger<TelegramPollingHostedService> _logger;

    public TelegramPollingHostedService(
        IServiceProvider services,
        ILogger<TelegramPollingHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telegram 轮询 HostedService 已启动（实际拉取受 Mode=polling 控制）");

        var consecutiveFailures = 0;
        var backoffLadder = FailureBackoffLadder;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan delay;
                try
                {
                    delay = await RunOnceAsync(stoppingToken);
                    // 审计 #10：catch 必须在 while 之内。旧写法把 catch 放在循环外，
                    // 一次未捕获异常（DI 解析失败、CreateScope 抛错、status 写入抛错等，
                    // 这些都不在 RunOnceAsync 自己的 try 里）就记 Critical 后退出循环 ⇒ 之后再也不轮询，只能重启进程。
                    // 现在按退避阶梯递增等待后继续轮询，任一轮成功即清零。
                    consecutiveFailures = 0;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    consecutiveFailures++;
                    delay = backoffLadder[Math.Min(consecutiveFailures - 1, backoffLadder.Count - 1)];
                    _logger.LogError(ex, "Telegram 轮询单轮异常，连续失败 {Count} 次，退避 {Backoff} 后继续轮询", consecutiveFailures, delay);
                }

                await Task.Delay(delay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 正常停止
        }
        catch (Exception ex)
        {
            // 兜底：退避本身都无法继续（例如停机过程中 Task.Delay 被非取消原因打断）时才走到这里
            _logger.LogCritical(ex, "Telegram 轮询 HostedService 异常退出，轮询入站将不可用（可改用 webhook 模式）");
        }
        finally
        {
            TrySetStopped("轮询服务已退出");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        TrySetStopped("应用关闭");
        _logger.LogInformation("Telegram 轮询 HostedService 正在停止");
        await base.StopAsync(cancellationToken);
    }

    /// <summary>执行一轮拉取，返回下一轮应等待的时间</summary>
    private async Task<TimeSpan> RunOnceAsync(CancellationToken stoppingToken)
    {
        TelegramOptions settings;
        ITelegramPollingConnectionStatus status;

        using (var probeScope = _services.CreateScope())
        {
            var settingsProvider = probeScope.ServiceProvider.GetRequiredService<ITelegramSettingsProvider>();
            status = probeScope.ServiceProvider.GetRequiredService<ITelegramPollingConnectionStatus>();

            try
            {
                settings = await settingsProvider.GetCurrentAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram 轮询读取配置失败，稍后重试");
                return DisabledCheckDelay;
            }
        }

        if (!settings.Enabled || !settings.IsConfigured || !settings.IsPollingMode)
        {
            if (status.IsRunning)
            {
                status.SetStopped();
            }

            return DisabledCheckDelay;
        }

        status.SetRunning();

        try
        {
            // 一轮 getUpdates（≤100 条 Update）共用同一个 Scope / DbContext：同批入库共享变更跟踪与事务、游标只推进一次，
            // 这是刻意口径，不要改成逐条建 Scope（审计 #10 只修循环存活，不动这里）。
            using var scope = _services.CreateScope();
            var ingestion = scope.ServiceProvider.GetRequiredService<ITelegramIngestionService>();
            var result = await ingestion.PollOnceAsync(stoppingToken);

            if (result.WebhookConflict)
            {
                // getUpdates 与已登记的 webhook 互斥（409）：按配置自动 deleteWebhook，否则提示改回 webhook 模式
                var deleted = await TryDeleteWebhookAsync(scope.ServiceProvider, settings);
                status.MarkError(deleted
                    ? "检测到已登记 Webhook（409），已自动 deleteWebhook，下一轮起正常轮询"
                    : "检测到已登记 Webhook（409）：请在配置页删除 Webhook 或改回 webhook 模式");
                return deleted ? IdleDelay : TimeSpan.FromSeconds(30);
            }

            if (result.InvalidToken)
            {
                status.MarkError("BotToken 无效（401），请重新填写");
                return InvalidTokenBackoff;
            }

            if (!result.Ok)
            {
                status.MarkError(result.ErrorMessage ?? "getUpdates 失败");
                return result.RetryAfterSeconds is > 0
                    ? TimeSpan.FromSeconds(Math.Clamp(result.RetryAfterSeconds.Value, 1, 300))
                    : TimeSpan.FromSeconds(10);
            }

            if (result.RetryAfterSeconds is > 0)
            {
                status.MarkError($"限流（429），{result.RetryAfterSeconds} 秒后重试");
                return TimeSpan.FromSeconds(Math.Clamp(result.RetryAfterSeconds.Value, 1, 300));
            }

            if (result.ProcessedCount > 0)
            {
                _logger.LogInformation("Telegram 轮询接入 {Received} 条 Update（成功 {Processed} 条，游标 {Offset}）",
                    result.ReceivedCount, result.ProcessedCount, result.NewOffset);
            }

            status.MarkActivity();

            // 长轮询已在服务端挂起 PollingTimeout 秒；拿到整页时立刻续拉，避免积压
            return result.ReceivedCount >= settings.PollingLimit ? TimeSpan.Zero : IdleDelay;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram 轮询单轮执行失败");
            status.MarkError(ex.Message);
            return TimeSpan.FromSeconds(10);
        }
    }

    private async Task<bool> TryDeleteWebhookAsync(IServiceProvider serviceProvider, TelegramOptions settings)
    {
        if (!settings.DeleteWebhookOnSwitch)
        {
            return false;
        }

        try
        {
            var client = serviceProvider.GetRequiredService<ITelegramClient>();
            var ok = await client.DeleteWebhookAsync(dropPendingUpdates: false);
            _logger.LogInformation("Telegram 轮询遇到 409：deleteWebhook {Result}", ok ? "成功" : "失败");
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram 轮询 409 后 deleteWebhook 失败");
            return false;
        }
    }

    private void TrySetStopped(string reason)
    {
        try
        {
            using var scope = _services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITelegramPollingConnectionStatus>().SetStopped(reason);
        }
        catch
        {
            // 关闭阶段的清理失败不应影响进程退出
        }
    }
}
