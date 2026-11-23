using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Services;

namespace NoneadCSS.Infrastructure.Services.WeChat;

/// <summary>
/// 微信有效配置提供器实现 - 合并 appsettings.json 基线配置与 SystemOptions 数据库覆盖配置。
/// 敏感字段（AppSecret、EncodingAESKey）在数据库中加密存储，读取时解密。
/// 使用 IMemoryCache 缓存合并后的配置，避免每次请求都查库。
/// 注册为 Singleton，通过 IServiceScopeFactory 在后台刷新时创建独立 scope，避免 DbContext 被提前释放。
/// </summary>
public class WeChatSettingsProvider : IWeChatSettingsProvider
{
    private readonly WeChatOptions _baseline;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEncryptionService _encryptionService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WeChatSettingsProvider> _logger;

    private const string CacheKey = "WeChat:EffectiveSettings";
    private readonly TimeSpan _cacheDuration;

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private WeChatOptions? _lastKnown;
    private int _refreshing;

    public WeChatSettingsProvider(
        IOptions<WeChatOptions> baseline,
        IOptions<CacheOptions> cacheOptions,
        IServiceScopeFactory scopeFactory,
        IEncryptionService encryptionService,
        IMemoryCache cache,
        ILogger<WeChatSettingsProvider> logger)
    {
        _baseline = baseline.Value;
        _cacheDuration = TimeSpan.FromMinutes(cacheOptions.Value.ChannelSettingsCacheMinutes);
        _scopeFactory = scopeFactory;
        _encryptionService = encryptionService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// 当前有效的微信配置。
    /// 缓存命中时直接返回；缓存 miss 时返回上次已知值（或基线）并触发后台异步刷新，
    /// 避免 sync-over-async 阻塞线程池。
    /// </summary>
    public WeChatOptions Current
    {
        get
        {
            if (_cache.TryGetValue<WeChatOptions>(CacheKey, out var cached) && cached != null)
            {
                _lastKnown = cached;
                return cached;
            }

            TriggerBackgroundRefresh();
            return _lastKnown ?? _baseline;
        }
    }

    /// <summary>
    /// 异步获取当前配置（优先读缓存，miss 时异步加载）。
    /// </summary>
    public async Task<WeChatOptions> GetCurrentAsync()
    {
        if (_cache.TryGetValue<WeChatOptions>(CacheKey, out var cached) && cached != null)
        {
            _lastKnown = cached;
            return cached;
        }

        await RefreshAsync();
        if (_cache.TryGetValue<WeChatOptions>(CacheKey, out var reloaded) && reloaded != null)
        {
            _lastKnown = reloaded;
            return reloaded;
        }

        return _lastKnown ?? _baseline;
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
                _logger.LogWarning(ex, "后台刷新微信配置失败");
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

    private async Task<WeChatOptions> LoadMergedOptionsAsync()
    {
        // 以 appsettings.json 为基线，数据库非空值覆盖
        var result = new WeChatOptions
        {
            AppId = _baseline.AppId,
            AppSecret = _baseline.AppSecret,
            Token = _baseline.Token,
            EncodingAESKey = _baseline.EncodingAESKey,
            EncryptMode = _baseline.EncryptMode,
            MailboxId = _baseline.MailboxId,
            ContactCollectionEnabled = _baseline.ContactCollectionEnabled,
            AutoReplyEnabled = _baseline.AutoReplyEnabled,
            AutoReplyMessage = _baseline.AutoReplyMessage,
            Enabled = _baseline.Enabled,
            CallbackPath = _baseline.CallbackPath,
            CallbackBaseUrl = _baseline.CallbackBaseUrl
        };

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var appId = await unitOfWork.SystemOptions.GetValueAsync(WeChatOptionKeys.AppId);
            if (!string.IsNullOrWhiteSpace(appId)) result.AppId = appId;

            var appSecretValue = await unitOfWork.SystemOptions.GetValueAsync(WeChatOptionKeys.AppSecret);
            if (!string.IsNullOrWhiteSpace(appSecretValue))
            {
                // 加密存储格式：ENC:<密文>；无前缀视为历史明文直接使用
                result.AppSecret = DecryptIfEncrypted(appSecretValue);
            }

            var tokenValue = await unitOfWork.SystemOptions.GetValueAsync(WeChatOptionKeys.Token);
            if (!string.IsNullOrWhiteSpace(tokenValue))
            {
                // Token 明文存储（签名盐值，需与微信公众平台后台一致）
                // 兼容历史加密存储：尝试解密失败则视为明文
                if (_encryptionService.TryDecrypt(tokenValue, out var plain) && !string.IsNullOrEmpty(plain))
                {
                    result.Token = plain;
                }
                else
                {
                    result.Token = tokenValue;
                }
            }

            var aesKeyValue = await unitOfWork.SystemOptions.GetValueAsync(WeChatOptionKeys.EncodingAESKey);
            if (!string.IsNullOrWhiteSpace(aesKeyValue))
            {
                // 加密存储格式：ENC:<密文>；无前缀视为历史明文直接使用
                result.EncodingAESKey = DecryptIfEncrypted(aesKeyValue);
            }

            var encryptMode = await unitOfWork.SystemOptions.GetValueAsync(WeChatOptionKeys.EncryptMode);
            if (!string.IsNullOrWhiteSpace(encryptMode)) result.EncryptMode = encryptMode;

            var mailboxIdStr = await unitOfWork.SystemOptions.GetValueAsync(WeChatOptionKeys.MailboxId);
            if (int.TryParse(mailboxIdStr, out var mailboxId) && mailboxId > 0)
            {
                result.MailboxId = mailboxId;
            }

            var contactCollectionEnabledStr = await unitOfWork.SystemOptions.GetValueAsync(WeChatOptionKeys.ContactCollectionEnabled);
            if (bool.TryParse(contactCollectionEnabledStr, out var contactCollectionEnabled))
            {
                result.ContactCollectionEnabled = contactCollectionEnabled;
            }

            var autoReplyEnabledStr = await unitOfWork.SystemOptions.GetValueAsync(WeChatOptionKeys.AutoReplyEnabled);
            if (bool.TryParse(autoReplyEnabledStr, out var autoReplyEnabled))
            {
                result.AutoReplyEnabled = autoReplyEnabled;
            }

            var autoReplyMessage = await unitOfWork.SystemOptions.GetValueAsync(WeChatOptionKeys.AutoReplyMessage);
            if (autoReplyMessage != null) result.AutoReplyMessage = autoReplyMessage;

            var enabledStr = await unitOfWork.SystemOptions.GetValueAsync(WeChatOptionKeys.Enabled);
            if (bool.TryParse(enabledStr, out var enabled))
            {
                result.Enabled = enabled;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载微信 SystemOptions 配置失败，回退到 appsettings.json 基线");
        }

        return result;
    }

    /// <summary>
    /// 解密带 ENC: 前缀的密文；无前缀视为历史明文直接返回。
    /// 解密失败时返回 null，避免将密文原文当作有效配置使用。
    /// </summary>
    private string? DecryptIfEncrypted(string storedValue)
    {
        const string encPrefix = "ENC:";
        if (!storedValue.StartsWith(encPrefix, StringComparison.Ordinal))
        {
            // 无前缀：历史明文，直接使用
            return storedValue;
        }

        var cipherText = storedValue[encPrefix.Length..];
        if (_encryptionService.TryDecrypt(cipherText, out var plain) && !string.IsNullOrEmpty(plain))
        {
            return plain;
        }

        // 解密失败（如 Encryption:Key 变更）：返回 null，让调用方识别为"未配置"状态
        _logger.LogError("微信敏感配置解密失败：Encryption:Key 可能已变更。StoredValuePrefix={Preview}，请重新配置 AppSecret/EncodingAESKey",
            cipherText.Length > 8 ? cipherText[..8] + "..." : cipherText);
        return null;
    }
}
