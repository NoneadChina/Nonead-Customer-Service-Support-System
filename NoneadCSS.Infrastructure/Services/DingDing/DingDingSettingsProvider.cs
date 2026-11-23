using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Services;

namespace NoneadCSS.Infrastructure.Services.DingDing;

/// <summary>
/// 钉钉有效配置提供器 - 合并 appsettings.json 基线配置与 SystemOptions 数据库覆盖配置。
/// 敏感字段（AppSecret、CallbackToken、EncodingAesKey）优先解密，解密失败按明文处理。
/// 使用 IMemoryCache 缓存（Singleton，跨请求共享），配置更新后由 DingDingService 调用 RefreshAsync 刷新。
/// 注册为 Singleton，通过 IServiceScopeFactory 在后台刷新时创建独立 scope，避免 DbContext 被提前释放。
/// </summary>
public class DingDingSettingsProvider : IDingDingSettingsProvider
{
    private readonly IOptions<DingDingOptions> _baseline;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<DingDingSettingsProvider> _logger;
    private readonly IMemoryCache _cache;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly TimeSpan _cacheDuration;
    private DingDingOptions? _lastKnown;
    private int _refreshing;

    private const string CacheKey = "DingDingSettings";

    public DingDingSettingsProvider(
        IOptions<DingDingOptions> baseline,
        IOptions<CacheOptions> cacheOptions,
        IServiceScopeFactory scopeFactory,
        IEncryptionService encryptionService,
        IMemoryCache cache,
        ILogger<DingDingSettingsProvider> logger)
    {
        _baseline = baseline;
        _cacheDuration = TimeSpan.FromMinutes(cacheOptions.Value.ChannelSettingsCacheMinutes);
        _scopeFactory = scopeFactory;
        _encryptionService = encryptionService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// 当前有效的钉钉配置。
    /// 缓存命中时直接返回；缓存 miss 时返回上次已知值（或基线）并触发后台异步刷新，
    /// 避免 sync-over-async 阻塞线程池。
    /// </summary>
    public DingDingOptions Current
    {
        get
        {
            if (_cache.TryGetValue<DingDingOptions>(CacheKey, out var cached) && cached != null)
            {
                _lastKnown = cached;
                return cached;
            }

            TriggerBackgroundRefresh();
            return _lastKnown ?? _baseline.Value;
        }
    }

    /// <summary>
    /// 异步获取当前配置（优先读缓存，miss 时异步加载）。
    /// </summary>
    public async Task<DingDingOptions> GetCurrentAsync()
    {
        if (_cache.TryGetValue<DingDingOptions>(CacheKey, out var cached) && cached != null)
        {
            _lastKnown = cached;
            return cached;
        }

        await RefreshAsync();
        if (_cache.TryGetValue<DingDingOptions>(CacheKey, out var reloaded) && reloaded != null)
        {
            _lastKnown = reloaded;
            return reloaded;
        }

        return _lastKnown ?? _baseline.Value;
    }

    private void TriggerBackgroundRefresh()
    {
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "后台刷新钉钉配置失败");
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        });
    }

    public async Task RefreshAsync()
    {
        await _loadLock.WaitAsync();
        try
        {
            var merged = await LoadMergedOptionsAsync();
            _cache.Set(CacheKey, merged, _cacheDuration);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<DingDingOptions> LoadMergedOptionsAsync()
    {
        var result = new DingDingOptions
        {
            AppKey = _baseline.Value.AppKey,
            AppSecret = _baseline.Value.AppSecret,
            AgentId = _baseline.Value.AgentId,
            RobotCode = _baseline.Value.RobotCode,
            CallbackToken = _baseline.Value.CallbackToken,
            EncodingAesKey = _baseline.Value.EncodingAesKey,
            ReceiveMode = _baseline.Value.ReceiveMode,
            MailboxId = _baseline.Value.MailboxId,
            ContactCollectionEnabled = _baseline.Value.ContactCollectionEnabled,
            AutoReplyEnabled = _baseline.Value.AutoReplyEnabled,
            AutoReplyMessage = _baseline.Value.AutoReplyMessage,
            Enabled = _baseline.Value.Enabled,
            CallbackPath = _baseline.Value.CallbackPath
        };

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var appKey = await unitOfWork.SystemOptions.GetValueAsync(DingDingOptionKeys.AppKey);
            if (!string.IsNullOrWhiteSpace(appKey))
                result.AppKey = appKey;

            var appSecretCipher = await unitOfWork.SystemOptions.GetValueAsync(DingDingOptionKeys.AppSecret);
            if (!string.IsNullOrWhiteSpace(appSecretCipher))
            {
                result.AppSecret = DecryptIfEncrypted(appSecretCipher);
            }

            var agentIdStr = await unitOfWork.SystemOptions.GetValueAsync(DingDingOptionKeys.AgentId);
            if (!string.IsNullOrWhiteSpace(agentIdStr) && long.TryParse(agentIdStr, out var agentId))
                result.AgentId = agentId;

            var robotCode = await unitOfWork.SystemOptions.GetValueAsync(DingDingOptionKeys.RobotCode);
            if (!string.IsNullOrWhiteSpace(robotCode))
                result.RobotCode = robotCode;

            var callbackTokenCipher = await unitOfWork.SystemOptions.GetValueAsync(DingDingOptionKeys.CallbackToken);
            if (!string.IsNullOrWhiteSpace(callbackTokenCipher))
            {
                result.CallbackToken = DecryptIfEncrypted(callbackTokenCipher);
            }

            var aesKeyCipher = await unitOfWork.SystemOptions.GetValueAsync(DingDingOptionKeys.EncodingAesKey);
            if (!string.IsNullOrWhiteSpace(aesKeyCipher))
            {
                result.EncodingAesKey = DecryptIfEncrypted(aesKeyCipher);
            }

            var receiveMode = await unitOfWork.SystemOptions.GetValueAsync(DingDingOptionKeys.ReceiveMode);
            if (!string.IsNullOrWhiteSpace(receiveMode))
                result.ReceiveMode = receiveMode;

            var mailboxIdStr = await unitOfWork.SystemOptions.GetValueAsync(DingDingOptionKeys.MailboxId);
            if (!string.IsNullOrWhiteSpace(mailboxIdStr) && int.TryParse(mailboxIdStr, out var mailboxId))
                result.MailboxId = mailboxId;

            var contactCollectionEnabledStr = await unitOfWork.SystemOptions.GetValueAsync(DingDingOptionKeys.ContactCollectionEnabled);
            if (!string.IsNullOrWhiteSpace(contactCollectionEnabledStr) && bool.TryParse(contactCollectionEnabledStr, out var contactCollectionEnabled))
                result.ContactCollectionEnabled = contactCollectionEnabled;

            var autoReplyEnabledStr = await unitOfWork.SystemOptions.GetValueAsync(DingDingOptionKeys.AutoReplyEnabled);
            if (!string.IsNullOrWhiteSpace(autoReplyEnabledStr) && bool.TryParse(autoReplyEnabledStr, out var autoReplyEnabled))
                result.AutoReplyEnabled = autoReplyEnabled;

            var autoReplyMessage = await unitOfWork.SystemOptions.GetValueAsync(DingDingOptionKeys.AutoReplyMessage);
            if (autoReplyMessage != null)
                result.AutoReplyMessage = autoReplyMessage;

            var enabledStr = await unitOfWork.SystemOptions.GetValueAsync(DingDingOptionKeys.Enabled);
            if (!string.IsNullOrWhiteSpace(enabledStr) && bool.TryParse(enabledStr, out var enabled))
                result.Enabled = enabled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载钉钉配置从数据库失败，使用基线配置");
        }

        return result;
    }

    /// <summary>
    /// 解密密文字段。支持两种格式：
    /// 1. ENC: 前缀格式（微信/飞书风格）
    /// 2. 无前缀纯密文格式（钉钉当前格式）
    /// 解密失败时返回 null，避免将密文原文当作有效配置使用。
    /// </summary>
    private string? DecryptIfEncrypted(string storedValue)
    {
        const string encPrefix = "ENC:";
        
        // 检查是否有 ENC: 前缀
        if (storedValue.StartsWith(encPrefix, StringComparison.Ordinal))
        {
            var cipherText = storedValue[encPrefix.Length..];
            if (_encryptionService.TryDecrypt(cipherText, out var plain) && !string.IsNullOrEmpty(plain))
            {
                return plain;
            }
            _logger.LogError("钉钉敏感配置解密失败（ENC前缀格式）：Encryption:Key 可能已变更。请重新配置");
            return null;
        }

        // 无前缀：直接尝试解密（钉钉当前格式），失败则视为历史明文
        if (_encryptionService.TryDecrypt(storedValue, out var plainText) && !string.IsNullOrEmpty(plainText))
        {
            return plainText;
        }

        // 解密失败但看起来像 base64 密文（长度≥24），记录错误
        if (storedValue.Length >= 24 && IsLikelyBase64(storedValue))
        {
            _logger.LogError("钉钉敏感配置解密失败：Encryption:Key 可能已变更。请重新配置 AppSecret/CallbackToken/EncodingAesKey");
            return null;
        }

        // 明显不是密文（太短或非 base64），视为历史明文
        return storedValue;
    }

    private static bool IsLikelyBase64(string value)
    {
        return value.All(c => char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=');
    }
}
