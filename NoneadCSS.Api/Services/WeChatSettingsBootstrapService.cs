using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Api;

/// <summary>
/// 应用启动时预加载所有渠道（微信/钉钉/飞书/WhatsApp/Messenger/Telegram/LINE/iMessage）配置到内存缓存，
/// 避免首次调用 IxxxSettingsProvider.Current 时返回 appsettings.json 基线
/// 而非 SystemOptions 数据库合并值。
/// </summary>
public class WeChatSettingsBootstrapService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WeChatSettingsBootstrapService> _logger;

    public WeChatSettingsBootstrapService(
        IServiceScopeFactory scopeFactory,
        ILogger<WeChatSettingsBootstrapService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        // 并行预热所有渠道配置
        await Task.WhenAll(
            PreloadWeChatAsync(sp),
            PreloadDingDingAsync(sp),
            PreloadFeishuAsync(sp),
            PreloadWhatsAppAsync(sp),
            PreloadMessengerAsync(sp),
            PreloadTelegramAsync(sp),
            PreloadLineAsync(sp),
            PreloadIMessageAsync(sp)
        );
    }

    private async Task PreloadWeChatAsync(IServiceProvider sp)
    {
        try
        {
            var provider = sp.GetRequiredService<IWeChatSettingsProvider>();
            await provider.RefreshAsync();
            var s = provider.Current;
            
            // 检查配置完整性
            var hasAppSecret = !string.IsNullOrWhiteSpace(s.AppSecret);
            var hasToken = !string.IsNullOrWhiteSpace(s.Token);
            var hasEncodingAESKey = !string.IsNullOrWhiteSpace(s.EncodingAESKey);
            
            if (s.Enabled && s.IsConfigured)
            {
                _logger.LogInformation("微信配置已启动加载：Enabled={Enabled}, AppId={AppId}, EncryptMode={EncryptMode}, HasAppSecret={HasAppSecret}, HasToken={HasToken}",
                    s.Enabled, string.IsNullOrEmpty(s.AppId) ? "(empty)" : Mask(s.AppId), s.EncryptMode, hasAppSecret, hasToken);
            }
            else if (s.Enabled && !s.IsConfigured)
            {
                // 已启用但配置不完整 - 可能是解密失败导致
                _logger.LogError("微信配置已启用但配置不完整！AppId={AppId}, HasAppSecret={HasAppSecret}, HasToken={HasToken}, HasEncodingAESKey={HasEncodingAESKey}。可能是 Encryption:Key 变更导致敏感字段解密失败，请重新配置",
                    string.IsNullOrEmpty(s.AppId) ? "(empty)" : Mask(s.AppId), hasAppSecret, hasToken, hasEncodingAESKey);
            }
            else
            {
                _logger.LogInformation("微信配置已加载但未启用：Enabled={Enabled}, AppId={AppId}",
                    s.Enabled, string.IsNullOrEmpty(s.AppId) ? "(empty)" : Mask(s.AppId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动时加载微信配置失败。请检查：1) 数据库连接 2) Encryption:Key 配置 3) 数据库中配置是否存在");
        }
    }

    private async Task PreloadDingDingAsync(IServiceProvider sp)
    {
        try
        {
            var provider = sp.GetRequiredService<IDingDingSettingsProvider>();
            await provider.RefreshAsync();
            var s = provider.Current;
            
            var hasAppSecret = !string.IsNullOrWhiteSpace(s.AppSecret);
            var hasCallbackToken = !string.IsNullOrWhiteSpace(s.CallbackToken);
            var hasEncodingAesKey = !string.IsNullOrWhiteSpace(s.EncodingAesKey);
            
            if (s.Enabled && !string.IsNullOrWhiteSpace(s.AppKey) && hasAppSecret)
            {
                _logger.LogInformation("钉钉配置已启动加载：Enabled={Enabled}, AppKey={AppKey}, ReceiveMode={ReceiveMode}, HasAppSecret={HasAppSecret}",
                    s.Enabled, string.IsNullOrEmpty(s.AppKey) ? "(empty)" : Mask(s.AppKey), s.ReceiveMode, hasAppSecret);
            }
            else if (s.Enabled)
            {
                _logger.LogError("钉钉配置已启用但配置不完整！AppKey={AppKey}, HasAppSecret={HasAppSecret}, HasCallbackToken={HasCallbackToken}, HasEncodingAesKey={HasEncodingAesKey}。可能是 Encryption:Key 变更导致敏感字段解密失败，请重新配置",
                    string.IsNullOrEmpty(s.AppKey) ? "(empty)" : Mask(s.AppKey), hasAppSecret, hasCallbackToken, hasEncodingAesKey);
            }
            else
            {
                _logger.LogInformation("钉钉配置已加载但未启用：Enabled={Enabled}, AppKey={AppKey}",
                    s.Enabled, string.IsNullOrEmpty(s.AppKey) ? "(empty)" : Mask(s.AppKey));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动时加载钉钉配置失败。请检查：1) 数据库连接 2) Encryption:Key 配置 3) 数据库中配置是否存在");
        }
    }

    private async Task PreloadFeishuAsync(IServiceProvider sp)
    {
        try
        {
            var provider = sp.GetRequiredService<IFeishuSettingsProvider>();
            await provider.RefreshAsync();
            var s = provider.Current;
            
            var hasAppSecret = !string.IsNullOrWhiteSpace(s.AppSecret);
            var hasEncryptKey = !string.IsNullOrWhiteSpace(s.EncryptKey);
            
            if (s.Enabled && !string.IsNullOrWhiteSpace(s.AppId) && hasAppSecret)
            {
                _logger.LogInformation("飞书配置已启动加载：Enabled={Enabled}, AppId={AppId}, ReceiveMode={ReceiveMode}, HasAppSecret={HasAppSecret}",
                    s.Enabled, string.IsNullOrEmpty(s.AppId) ? "(empty)" : Mask(s.AppId), s.ReceiveMode, hasAppSecret);
            }
            else if (s.Enabled)
            {
                _logger.LogError("飞书配置已启用但配置不完整！AppId={AppId}, HasAppSecret={HasAppSecret}, HasEncryptKey={HasEncryptKey}。可能是 Encryption:Key 变更导致敏感字段解密失败，请重新配置",
                    string.IsNullOrEmpty(s.AppId) ? "(empty)" : Mask(s.AppId), hasAppSecret, hasEncryptKey);
            }
            else
            {
                _logger.LogInformation("飞书配置已加载但未启用：Enabled={Enabled}, AppId={AppId}",
                    s.Enabled, string.IsNullOrEmpty(s.AppId) ? "(empty)" : Mask(s.AppId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动时加载飞书配置失败。请检查：1) 数据库连接 2) Encryption:Key 配置 3) 数据库中配置是否存在");
        }
    }

    private async Task PreloadWhatsAppAsync(IServiceProvider sp)
    {
        try
        {
            var provider = sp.GetRequiredService<IWhatsAppSettingsProvider>();
            await provider.RefreshAsync();
            var s = provider.Current;
            
            var hasAccessToken = !string.IsNullOrWhiteSpace(s.AccessToken);
            var hasAppSecret = !string.IsNullOrWhiteSpace(s.AppSecret);
            
            if (s.Enabled && !string.IsNullOrWhiteSpace(s.PhoneNumberId) && hasAccessToken)
            {
                _logger.LogInformation("WhatsApp配置已启动加载：Enabled={Enabled}, PhoneNumberId={PhoneId}, HasAccessToken={HasAccessToken}",
                    s.Enabled, string.IsNullOrEmpty(s.PhoneNumberId) ? "(empty)" : Mask(s.PhoneNumberId), hasAccessToken);
            }
            else if (s.Enabled)
            {
                _logger.LogError("WhatsApp配置已启用但配置不完整！PhoneNumberId={PhoneId}, HasAccessToken={HasAccessToken}, HasAppSecret={HasAppSecret}。可能是 Encryption:Key 变更导致敏感字段解密失败，请重新配置",
                    string.IsNullOrEmpty(s.PhoneNumberId) ? "(empty)" : Mask(s.PhoneNumberId), hasAccessToken, hasAppSecret);
            }
            else
            {
                _logger.LogInformation("WhatsApp配置已加载但未启用：Enabled={Enabled}, PhoneNumberId={PhoneId}",
                    s.Enabled, string.IsNullOrEmpty(s.PhoneNumberId) ? "(empty)" : Mask(s.PhoneNumberId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动时加载WhatsApp配置失败。请检查：1) 数据库连接 2) Encryption:Key 配置 3) 数据库中配置是否存在");
        }
    }

    private async Task PreloadMessengerAsync(IServiceProvider sp)
    {
        try
        {
            var provider = sp.GetRequiredService<IMessengerSettingsProvider>();
            await provider.RefreshAsync();
            var s = provider.Current;

            var hasPageToken = !string.IsNullOrWhiteSpace(s.PageAccessToken);
            var hasAppSecret = !string.IsNullOrWhiteSpace(s.AppSecret);
            var hasVerifyToken = !string.IsNullOrWhiteSpace(s.VerifyToken);

            if (s.Enabled && hasPageToken && hasAppSecret && hasVerifyToken)
            {
                _logger.LogInformation("Messenger配置已启动加载：Enabled={Enabled}, PageId={PageId}, ApiVersion={ApiVersion}, HasPageAccessToken={HasPageToken}",
                    s.Enabled, string.IsNullOrEmpty(s.PageId) ? "(empty)" : Mask(s.PageId), s.ApiVersion, hasPageToken);
            }
            else if (s.Enabled)
            {
                _logger.LogError("Messenger配置已启用但配置不完整！PageId={PageId}, HasPageAccessToken={HasPageToken}, HasAppSecret={HasAppSecret}, HasVerifyToken={HasVerifyToken}。可能是 Encryption:Key 变更导致敏感字段解密失败，请重新配置",
                    string.IsNullOrEmpty(s.PageId) ? "(empty)" : Mask(s.PageId), hasPageToken, hasAppSecret, hasVerifyToken);
            }
            else
            {
                _logger.LogInformation("Messenger配置已加载但未启用：Enabled={Enabled}, PageId={PageId}",
                    s.Enabled, string.IsNullOrEmpty(s.PageId) ? "(empty)" : Mask(s.PageId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动时加载Messenger配置失败。请检查：1) 数据库连接 2) Encryption:Key 配置 3) 数据库中配置是否存在");
        }
    }

    private async Task PreloadTelegramAsync(IServiceProvider sp)
    {
        try
        {
            var provider = sp.GetRequiredService<ITelegramSettingsProvider>();
            await provider.RefreshAsync();
            var s = provider.Current;

            var hasBotToken = !string.IsNullOrWhiteSpace(s.BotToken);
            var hasWebhookSecret = !string.IsNullOrWhiteSpace(s.WebhookSecretToken);

            if (s.Enabled && hasBotToken && (string.Equals(s.Mode, "polling", StringComparison.OrdinalIgnoreCase) || hasWebhookSecret))
            {
                _logger.LogInformation("Telegram配置已启动加载：Enabled={Enabled}, Mode={Mode}, BotUsername={Username}, HasBotToken={HasBotToken}",
                    s.Enabled, s.Mode, string.IsNullOrEmpty(s.BotUsername) ? "(empty)" : s.BotUsername, hasBotToken);
            }
            else if (s.Enabled)
            {
                _logger.LogError("Telegram配置已启用但配置不完整！Mode={Mode}, HasBotToken={HasBotToken}, HasWebhookSecretToken={HasWebhookSecret}。webhook 模式下缺少 WebhookSecretToken 将无法校验入站请求来源（Telegram 不签名请求体）",
                    s.Mode, hasBotToken, hasWebhookSecret);
            }
            else
            {
                _logger.LogInformation("Telegram配置已加载但未启用：Enabled={Enabled}, Mode={Mode}", s.Enabled, s.Mode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动时加载Telegram配置失败。请检查：1) 数据库连接 2) Encryption:Key 配置 3) 数据库中配置是否存在");
        }
    }

    private async Task PreloadLineAsync(IServiceProvider sp)
    {
        try
        {
            var provider = sp.GetRequiredService<ILineSettingsProvider>();
            await provider.RefreshAsync();
            var s = provider.Current;

            var hasChannelSecret = !string.IsNullOrWhiteSpace(s.ChannelSecret);
            var hasAccessToken = !string.IsNullOrWhiteSpace(s.ChannelAccessToken);

            if (s.Enabled && hasChannelSecret && hasAccessToken)
            {
                _logger.LogInformation("LINE配置已启动加载：Enabled={Enabled}, ChannelId={ChannelId}, HasChannelSecret={HasChannelSecret}, HasChannelAccessToken={HasAccessToken}",
                    s.Enabled, string.IsNullOrEmpty(s.ChannelId) ? "(empty)" : Mask(s.ChannelId), hasChannelSecret, hasAccessToken);
            }
            else if (s.Enabled)
            {
                _logger.LogError("LINE配置已启用但配置不完整！ChannelId={ChannelId}, HasChannelSecret={HasChannelSecret}, HasChannelAccessToken={HasAccessToken}。ChannelSecret 缺失将无法校验 X-Line-Signature",
                    string.IsNullOrEmpty(s.ChannelId) ? "(empty)" : Mask(s.ChannelId), hasChannelSecret, hasAccessToken);
            }
            else
            {
                _logger.LogInformation("LINE配置已加载但未启用：Enabled={Enabled}, ChannelId={ChannelId}",
                    s.Enabled, string.IsNullOrEmpty(s.ChannelId) ? "(empty)" : Mask(s.ChannelId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动时加载LINE配置失败。请检查：1) 数据库连接 2) Encryption:Key 配置 3) 数据库中配置是否存在");
        }
    }

    private async Task PreloadIMessageAsync(IServiceProvider sp)
    {
        try
        {
            var provider = sp.GetRequiredService<IIMessageSettingsProvider>();
            await provider.RefreshAsync();
            var s = provider.Current;

            var amb = s.Mode is not null && s.Mode.StartsWith("amb", StringComparison.OrdinalIgnoreCase);
            var hasEndpoint = amb ? !string.IsNullOrWhiteSpace(s.ApiBaseUrl) : !string.IsNullOrWhiteSpace(s.BaseUrl);
            var hasCredential = amb ? (!string.IsNullOrWhiteSpace(s.ApiKey) || !string.IsNullOrWhiteSpace(s.MspSharedSecret))
                                    : !string.IsNullOrWhiteSpace(s.Password);

            if (s.Enabled && hasEndpoint && hasCredential)
            {
                _logger.LogInformation("iMessage配置已启动加载：Enabled={Enabled}, Mode={Mode}, SendMethod={SendMethod}, HasEndpoint={HasEndpoint}",
                    s.Enabled, s.Mode, s.SendMethod, hasEndpoint);
            }
            else if (s.Enabled)
            {
                _logger.LogError("iMessage配置已启用但配置不完整！Mode={Mode}, 需要 {Required}，当前 HasEndpoint={HasEndpoint}, HasCredential={HasCredential}",
                    s.Mode,
                    amb ? "AMB 凭据（ApiBaseUrl + ApiKey/MspSharedSecret，且需 Apple 核准的 MSP 身份）"
                        : "桥接服务凭据（BaseUrl + Password，指向自建 macOS 上的 BlueBubbles）",
                    hasEndpoint, hasCredential);
            }
            else
            {
                _logger.LogInformation("iMessage配置已加载但未启用：Enabled={Enabled}, Mode={Mode}", s.Enabled, s.Mode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动时加载iMessage配置失败。请检查：1) 数据库连接 2) Encryption:Key 配置 3) 桥接服务是否可达");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string Mask(string value)
    {
        if (value.Length <= 4) return "****";
        return $"{value[..2]}***{value[^2..]}";
    }
}
