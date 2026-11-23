using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Infrastructure.Services.Feishu;

/// <summary>
/// 飞书 Stream 模式后台托管服务。
/// - 启动时检查 FeishuOptions.ReceiveMode == "Stream" 且 Enabled=true
/// - 条件满足时启动 FeishuStreamClient 长连接循环
/// - 配置变更后无需重启：内部 RunAsync 每 10s 轮询 settings，切换 Stream/Http 模式
/// - 通过 IHttpClientFactory 命名为 "FeishuStream" 创建 HttpClient 复用
/// </summary>
public class FeishuStreamHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<FeishuStreamHostedService> _logger;

    public FeishuStreamHostedService(
        IServiceProvider services,
        ILogger<FeishuStreamHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // 创建 Scoped 作用域，让 FeishuStreamClient 能够使用 Scoped 的
            // FeishuIngestionService / UnitOfWork，避免 HostedService 单例注入导致的生命周期不匹配
            using var scope = _services.CreateScope();

            var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            var settingsProvider = scope.ServiceProvider.GetRequiredService<IFeishuSettingsProvider>();
            var ingestion = scope.ServiceProvider.GetRequiredService<IFeishuIngestionService>();
            var connectionStatus = scope.ServiceProvider.GetRequiredService<IFeishuStreamConnectionStatus>();
            var clientLogger = scope.ServiceProvider.GetRequiredService<ILogger<FeishuStreamClient>>();

            // 预热 settings，避免第一次 RunAsync 才读取数据库
            try { await settingsProvider.RefreshAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "飞书 Stream 启动时刷新 settings 失败，使用基线配置"); }

            var httpClient = httpClientFactory.CreateClient("FeishuStream");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var client = new FeishuStreamClient(httpClient, settingsProvider, ingestion, connectionStatus, clientLogger);
            _logger.LogInformation("飞书 Stream HostedService 已启动（实际连接受 ReceiveMode=Stream 控制）");
            await client.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 正常停止
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "飞书 Stream HostedService 异常退出，飞书事件接收将不可用");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("飞书 Stream HostedService 正在停止");
        return base.StopAsync(cancellationToken);
    }
}
