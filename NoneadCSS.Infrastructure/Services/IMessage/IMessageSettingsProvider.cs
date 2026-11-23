using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Services;

namespace NoneadCSS.Infrastructure.Services.IMessage;

/// <summary>
/// iMessage 有效配置提供器（与 WhatsAppSettingsProvider 同一模式）：
/// appsettings 基线 + SystemOptions 数据库覆盖（密钥以 ENC: 前缀加密存储）合并后放 IMemoryCache，
/// 数据库读取通过 IServiceScopeFactory 开作用域，避免 Singleton 捕获 Scoped 依赖。
/// </summary>
public class IMessageSettingsProvider : IIMessageSettingsProvider
{
    private const string CacheKey = "IMessage:EffectiveSettings";

    private readonly IMessageOptions _baseline;
    private readonly TimeSpan _cacheDuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEncryptionService _encryptionService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<IMessageSettingsProvider> _logger;

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private IMessageOptions? _lastKnown;
    private int _refreshing;

    public IMessageSettingsProvider(
        IOptions<IMessageOptions> baseline,
        IOptions<CacheOptions> cacheOptions,
        IServiceScopeFactory scopeFactory,
        IEncryptionService encryptionService,
        IMemoryCache cache,
        ILogger<IMessageSettingsProvider> logger)
    {
        _baseline = baseline.Value;
        _cacheDuration = TimeSpan.FromMinutes(cacheOptions.Value.ChannelSettingsCacheMinutes);
        _scopeFactory = scopeFactory;
        _encryptionService = encryptionService;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public IMessageOptions Current
    {
        get
        {
            if (_cache.TryGetValue(CacheKey, out IMessageOptions? cached) && cached != null)
            {
                _lastKnown = cached;
                return cached;
            }

            TriggerBackgroundRefresh();
            return _lastKnown ?? _baseline;
        }
    }

    /// <inheritdoc />
    public async Task<IMessageOptions> GetCurrentAsync()
    {
        if (_cache.TryGetValue(CacheKey, out IMessageOptions? cached) && cached != null)
        {
            _lastKnown = cached;
            return cached;
        }

        await RefreshAsync();

        if (_cache.TryGetValue(CacheKey, out IMessageOptions? reloaded) && reloaded != null)
        {
            _lastKnown = reloaded;
            return reloaded;
        }

        return _lastKnown ?? _baseline;
    }

    /// <inheritdoc />
    public async Task RefreshAsync()
    {
        await _loadLock.WaitAsync();
        try
        {
            var options = await LoadMergedOptionsAsync();
            _lastKnown = options;
            _cache.Set(CacheKey, options, _cacheDuration);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private void TriggerBackgroundRefresh()
    {
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("后台刷新 iMessage 配置失败：{Message}", ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        });
    }

    private async Task<IMessageOptions> LoadMergedOptionsAsync()
    {
        var options = new IMessageOptions
        {
            Mode = _baseline.Mode,
            Enabled = _baseline.Enabled,
            BaseUrl = _baseline.BaseUrl,
            AuthStyle = _baseline.AuthStyle,
            Password = _baseline.Password,
            BridgeWebhookSecret = _baseline.BridgeWebhookSecret,
            WebhookPath = _baseline.WebhookPath,
            WebhookBaseUrl = _baseline.WebhookBaseUrl,
            SendMethod = _baseline.SendMethod,
            AllowSmsFallback = _baseline.AllowSmsFallback,
            AllowList = _baseline.AllowList,
            RateLimitPerMinute = _baseline.RateLimitPerMinute,
            VerifyTls = _baseline.VerifyTls,
            HealthEndpoint = _baseline.HealthEndpoint,
            MinServerVersion = _baseline.MinServerVersion,
            ReadHistoryOnStart = _baseline.ReadHistoryOnStart,
            MatchWindowHours = _baseline.MatchWindowHours,
            MailboxId = _baseline.MailboxId,
            AutoReplyEnabled = _baseline.AutoReplyEnabled,
            AutoReplyMessage = _baseline.AutoReplyMessage,
            StatusCheckTimeoutSeconds = _baseline.StatusCheckTimeoutSeconds,
            MspConnector = _baseline.MspConnector,
            ApiBaseUrl = _baseline.ApiBaseUrl,
            ApiKey = _baseline.ApiKey,
            WebhookSecret = _baseline.WebhookSecret,
            BusinessIdentifier = _baseline.BusinessIdentifier,
            FallbackChannel = _baseline.FallbackChannel,
            MspId = _baseline.MspId,
            MspSharedSecret = _baseline.MspSharedSecret,
            TokenTtlSeconds = _baseline.TokenTtlSeconds,
            GatewayBaseUrl = _baseline.GatewayBaseUrl,
            BusinessId = _baseline.BusinessId,
            InboundPath = _baseline.InboundPath,
            AttachmentKeySize = _baseline.AttachmentKeySize,
            MaxBodyBytes = _baseline.MaxBodyBytes,
            ExperimentalAppleDirect = _baseline.ExperimentalAppleDirect,
        };

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            options.Mode = await ReadStringAsync(unitOfWork, IMessageOptionKeys.Mode) ?? options.Mode;
            options.BaseUrl = await ReadStringAsync(unitOfWork, IMessageOptionKeys.BaseUrl) ?? options.BaseUrl;
            options.AuthStyle = await ReadStringAsync(unitOfWork, IMessageOptionKeys.AuthStyle) ?? options.AuthStyle;
            options.SendMethod = await ReadStringAsync(unitOfWork, IMessageOptionKeys.SendMethod) ?? options.SendMethod;
            options.WebhookPath = await ReadStringAsync(unitOfWork, IMessageOptionKeys.WebhookPath) ?? options.WebhookPath;
            options.WebhookBaseUrl = await ReadStringAsync(unitOfWork, IMessageOptionKeys.WebhookBaseUrl) ?? options.WebhookBaseUrl;
            options.AllowList = await ReadStringAsync(unitOfWork, IMessageOptionKeys.AllowList) ?? options.AllowList;
            options.HealthEndpoint = await ReadStringAsync(unitOfWork, IMessageOptionKeys.HealthEndpoint) ?? options.HealthEndpoint;
            options.MinServerVersion = await ReadStringAsync(unitOfWork, IMessageOptionKeys.MinServerVersion) ?? options.MinServerVersion;
            options.AutoReplyMessage = await ReadStringAsync(unitOfWork, IMessageOptionKeys.AutoReplyMessage) ?? options.AutoReplyMessage;

            options.MspConnector = await ReadStringAsync(unitOfWork, IMessageOptionKeys.MspConnector) ?? options.MspConnector;
            options.ApiBaseUrl = await ReadStringAsync(unitOfWork, IMessageOptionKeys.ApiBaseUrl) ?? options.ApiBaseUrl;
            options.BusinessIdentifier = await ReadStringAsync(unitOfWork, IMessageOptionKeys.BusinessIdentifier) ?? options.BusinessIdentifier;
            options.FallbackChannel = await ReadStringAsync(unitOfWork, IMessageOptionKeys.FallbackChannel) ?? options.FallbackChannel;
            options.MspId = await ReadStringAsync(unitOfWork, IMessageOptionKeys.MspId) ?? options.MspId;
            options.GatewayBaseUrl = await ReadStringAsync(unitOfWork, IMessageOptionKeys.GatewayBaseUrl) ?? options.GatewayBaseUrl;
            options.BusinessId = await ReadStringAsync(unitOfWork, IMessageOptionKeys.BusinessId) ?? options.BusinessId;
            options.InboundPath = await ReadStringAsync(unitOfWork, IMessageOptionKeys.InboundPath) ?? options.InboundPath;

            // 密钥字段：数据库里是 ENC: 密文，需解密后才用于出站调用
            options.Password = DecryptIfEncrypted(await ReadRawAsync(unitOfWork, IMessageOptionKeys.Password)) ?? options.Password;
            // bridge webhook 共享密钥同样按密文存储（写入口只加 ENC: 前缀，不落明文）
            options.BridgeWebhookSecret = DecryptIfEncrypted(await ReadRawAsync(unitOfWork, IMessageOptionKeys.BridgeWebhookSecret)) ?? options.BridgeWebhookSecret;
            options.ApiKey = DecryptIfEncrypted(await ReadRawAsync(unitOfWork, IMessageOptionKeys.ApiKey)) ?? options.ApiKey;
            options.WebhookSecret = DecryptIfEncrypted(await ReadRawAsync(unitOfWork, IMessageOptionKeys.WebhookSecret)) ?? options.WebhookSecret;
            options.MspSharedSecret = DecryptIfEncrypted(await ReadRawAsync(unitOfWork, IMessageOptionKeys.MspSharedSecret)) ?? options.MspSharedSecret;

            options.Enabled = await ReadBoolAsync(unitOfWork, IMessageOptionKeys.Enabled) ?? options.Enabled;
            options.AllowSmsFallback = await ReadBoolAsync(unitOfWork, IMessageOptionKeys.AllowSmsFallback) ?? options.AllowSmsFallback;
            options.VerifyTls = await ReadBoolAsync(unitOfWork, IMessageOptionKeys.VerifyTls) ?? options.VerifyTls;
            options.ReadHistoryOnStart = await ReadBoolAsync(unitOfWork, IMessageOptionKeys.ReadHistoryOnStart) ?? options.ReadHistoryOnStart;
            options.AutoReplyEnabled = await ReadBoolAsync(unitOfWork, IMessageOptionKeys.AutoReplyEnabled) ?? options.AutoReplyEnabled;
            options.ExperimentalAppleDirect = await ReadBoolAsync(unitOfWork, IMessageOptionKeys.ExperimentalAppleDirect) ?? options.ExperimentalAppleDirect;

            options.RateLimitPerMinute = await ReadIntAsync(unitOfWork, IMessageOptionKeys.RateLimitPerMinute) ?? options.RateLimitPerMinute;
            options.MatchWindowHours = await ReadIntAsync(unitOfWork, IMessageOptionKeys.MatchWindowHours) ?? options.MatchWindowHours;
            options.MailboxId = await ReadIntAsync(unitOfWork, IMessageOptionKeys.MailboxId) ?? options.MailboxId;
            options.StatusCheckTimeoutSeconds = await ReadIntAsync(unitOfWork, IMessageOptionKeys.StatusCheckTimeoutSeconds) ?? options.StatusCheckTimeoutSeconds;
            options.TokenTtlSeconds = await ReadIntAsync(unitOfWork, IMessageOptionKeys.TokenTtlSeconds) ?? options.TokenTtlSeconds;
            options.AttachmentKeySize = await ReadIntAsync(unitOfWork, IMessageOptionKeys.AttachmentKeySize) ?? options.AttachmentKeySize;
            options.MaxBodyBytes = await ReadIntAsync(unitOfWork, IMessageOptionKeys.MaxBodyBytes) ?? options.MaxBodyBytes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("加载 iMessage 配置从数据库失败，使用基线配置：{Message}", ex.Message);
        }

        // 安全钳位：Apple 对 iat 超过 1 小时的令牌直接拒
        if (options.TokenTtlSeconds <= 0 || options.TokenTtlSeconds > IMessageJwtTtl.HardLimitSeconds)
            options.TokenTtlSeconds = IMessageJwtTtl.HardLimitSeconds;

        if (string.IsNullOrWhiteSpace(options.Mode)) options.Mode = IMessageModes.Bridge;
        if (string.IsNullOrWhiteSpace(options.AuthStyle)) options.AuthStyle = IMessageAuthStyles.Bearer;
        if (string.IsNullOrWhiteSpace(options.SendMethod)) options.SendMethod = IMessageSendMethods.AppleScript;
        if (string.IsNullOrWhiteSpace(options.WebhookPath)) options.WebhookPath = "/api/imessage/bluebubbles/webhook";
        if (string.IsNullOrWhiteSpace(options.HealthEndpoint)) options.HealthEndpoint = "/api/v1/ping";
        if (string.IsNullOrWhiteSpace(options.GatewayBaseUrl)) options.GatewayBaseUrl = "https://mspgw.push.apple.com";
        if (string.IsNullOrWhiteSpace(options.InboundPath)) options.InboundPath = "/message";
        if (options.MatchWindowHours <= 0) options.MatchWindowHours = 168;
        // 限流下限钳制：IMessageRateLimiter.TryAcquire 对 limitPerMinute <= 0 一律返回 false，
        // 数据库里存 0（历史脏数据 / 早先接口未校验时写入）会把全部出站静默停发，
        // 出站侧只留下一条 warn。这里回落到代码默认值，与 DTO 的 [Range(1, 600)] 形成双保险。
        if (options.RateLimitPerMinute <= 0) options.RateLimitPerMinute = IMessageOptions.DefaultRateLimitPerMinute;

        return options;
    }

    private static async Task<string?> ReadStringAsync(IUnitOfWork unitOfWork, string key)
    {
        var value = await unitOfWork.SystemOptions.GetValueAsync(key);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static Task<string?> ReadRawAsync(IUnitOfWork unitOfWork, string key)
        => unitOfWork.SystemOptions.GetValueAsync(key);

    private static async Task<bool?> ReadBoolAsync(IUnitOfWork unitOfWork, string key)
    {
        var value = await unitOfWork.SystemOptions.GetValueAsync(key);
        if (string.IsNullOrWhiteSpace(value)) return null;
        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    private static async Task<int?> ReadIntAsync(IUnitOfWork unitOfWork, string key)
    {
        var value = await unitOfWork.SystemOptions.GetValueAsync(key);
        if (string.IsNullOrWhiteSpace(value)) return null;
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// 解出密钥明文：约定 `ENC:` 前缀 = SystemOptions 里显式加密；无前缀时尝试直接解密
    /// （历史数据/其它写入路径），仍失败则视为明文返回。解不出时返回 null，让调用方回落到基线值。
    /// 任何分支都不把密钥内容写进日志。
    /// </summary>
    private string? DecryptIfEncrypted(string? storedValue)
    {
        const string encPrefix = "ENC:";
        if (string.IsNullOrEmpty(storedValue)) return null;

        if (!storedValue.StartsWith(encPrefix, StringComparison.Ordinal))
        {
            if (_encryptionService.TryDecrypt(storedValue, out var directPlain)) return directPlain;
            if (storedValue.Length >= 24 && IsLikelyBase64(storedValue))
            {
                _logger.LogError("iMessage 密钥字段疑似密文但解密失败，已忽略该数据库值（需在渠道设置页重新录入）");
                return null;
            }

            return storedValue;
        }

        var cipher = storedValue[encPrefix.Length..];
        if (_encryptionService.TryDecrypt(cipher, out var plain)) return plain;

        _logger.LogError("iMessage 密钥字段解密失败（ENC: 前缀存在），已忽略该数据库值");
        return null;
    }

    private static bool IsLikelyBase64(string value)
        => value.All(c => char.IsLetterOrDigit(c) || c is '+' or '/' or '=');
}

/// <summary>AMB 令牌 TTL 的硬上限常量（Apple：iat 超 1 小时即拒）。</summary>
public static class IMessageJwtTtl
{
    public const int HardLimitSeconds = 3600;
}
