using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Infrastructure.Services.DingDing;

/// <summary>
/// 钉钉 Stream 模式后台托管服务。
/// - 启动时检查 DingDingOptions.ReceiveMode == "Stream" 且 Enabled=true
/// - 条件满足时启动 DingTalkStreamClient 长连接循环
/// - 配置变更后无需重启：内部 RunAsync 每 10s 轮询 settings，切换 Stream/Http 模式
/// - 通过 IHttpClientFactory 命名为 "DingTalkStream" 创建 HttpClient 复用
/// </summary>
public class DingTalkStreamHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DingTalkStreamHostedService> _logger;

    public DingTalkStreamHostedService(
        IServiceProvider services,
        ILogger<DingTalkStreamHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // 创建 Scoped 作用域，让 DingTalkStreamClient 能够使用 Scoped 的
            // DingDingIngestionService / UnitOfWork，避免 HostedService 单例注入导致的生命周期不匹配
            using var scope = _services.CreateScope();

            var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            var settingsProvider = scope.ServiceProvider.GetRequiredService<IDingDingSettingsProvider>();
            var ingestion = scope.ServiceProvider.GetRequiredService<IDingDingIngestionService>();
            var connectionStatus = scope.ServiceProvider.GetRequiredService<IDingTalkStreamConnectionStatus>();
            var clientLogger = scope.ServiceProvider.GetRequiredService<ILogger<DingTalkStreamClient>>();

            // 预热 settings，避免第一次 RunAsync 才读取数据库
            try { await settingsProvider.RefreshAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "钉钉 Stream 启动时刷新 settings 失败，使用基线配置"); }

            var httpClient = httpClientFactory.CreateClient("DingTalkStream");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var client = new DingTalkStreamClient(httpClient, settingsProvider, ingestion, connectionStatus, clientLogger);
            _logger.LogInformation("钉钉 Stream HostedService 已启动（实际连接受 ReceiveMode=Stream 控制）");
            await client.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 正常停止
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "钉钉 Stream HostedService 异常退出，钉钉机器人消息接收将不可用");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("钉钉 Stream HostedService 正在停止");
        return base.StopAsync(cancellationToken);
    }
}
