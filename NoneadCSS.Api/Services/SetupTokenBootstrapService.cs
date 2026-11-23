using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Api.Services;

/// <summary>
/// 应用启动时把冷启动安装令牌打印一次（M-07），供运维调用 /api/setup 使用。
/// 令牌已持久化在文件中，此处只负责提示文件名并输出令牌内容一次。
/// </summary>
public sealed class SetupTokenBootstrapService : IHostedService
{
    private readonly ISetupTokenService _setupTokenService;
    private readonly ILogger<SetupTokenBootstrapService> _logger;

    public SetupTokenBootstrapService(
        ISetupTokenService setupTokenService,
        ILogger<SetupTokenBootstrapService> logger)
    {
        _setupTokenService = setupTokenService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // T8: 不打印令牌明文（令牌持久化在文件中，读文件即可），
        // 防止登录/日志可访问者借 X-Setup-Token 接管超管账号
        _logger.LogInformation(
            "M-07 冷启动安装令牌已就绪。调用 /api/setup 的 POST 端点需携带 X-Setup-Token 头。令牌文件：{TokenFile}（请从该文件读取令牌，勿外传）",
            _setupTokenService.TokenFilePath);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
