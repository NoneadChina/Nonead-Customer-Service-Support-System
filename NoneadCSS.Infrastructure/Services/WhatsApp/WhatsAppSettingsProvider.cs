using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Services;

namespace NoneadCSS.Infrastructure.Services.WhatsApp;

/// <summary>
/// WhatsApp 有效配置提供器实现 - 合并 appsettings.json 基线配置与 SystemOptions 数据库覆盖配置。
/// 敏感字段（AccessToken、AppSecret）在数据库中加密存储，读取时解密。
/// 使用 IMemoryCache 缓存合并后的配置，避免每次请求都查库。
/// 注册为 Singleton，通过 IServiceScopeFactory 在后台刷新时创建独立 scope，避免 DbContext 被提前释放。
/// </summary>
public class WhatsAppSettingsProvider : IWhatsAppSettingsProvider
{
    private readonly WhatsAppOptions _baseline;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEncryptionService _encryptionService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WhatsAppSettingsProvider> _logger;

    private const string CacheKey = "WhatsApp:EffectiveSettings";
    private readonly TimeSpan _cacheDuration;

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private WhatsAppOptions? _lastKnown;
    private int _refreshing;

    public WhatsAppSettingsProvider(
        IOptions<WhatsAppOptions> baseline,
        IOptions<CacheOptions> cacheOptions,
        IServiceScopeFactory scopeFactory,
        IEncryptionService encryptionService,
        IMemoryCache cache,
        ILogger<WhatsAppSettingsProvider> logger)
    {
        _baseline = baseline.Value;
        _cacheDuration = TimeSpan.FromMinutes(cacheOptions.Value.ChannelSettingsCacheMinutes);
        _scopeFactory = scopeFactory;
        _encryptionService = encryptionService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// 当前有效的 WhatsApp 配置。
    /// 缓存命中时直接返回；缓存 miss 时返回上次已知值（或基线）并触发后台异步刷新，
    /// 避免 sync-over-async 阻塞线程池。
    /// </summary>
    public WhatsAppOptions Current
    {
        get
        {
            if (_cache.TryGetValue<WhatsAppOptions>(CacheKey, out var cached) && cached != null)
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
    public async Task<WhatsAppOptions> GetCurrentAsync()
    {
        if (_cache.TryGetValue<WhatsAppOptions>(CacheKey, out var cached) && cached != null)
        {
            _lastKnown = cached;
            return cached;
        }

        await RefreshAsync();
        if (_cache.TryGetValue<WhatsAppOptions>(CacheKey, out var reloaded) && reloaded != null)
        {
            _lastKnown = reloaded;
            return reloaded;
        }

        return _lastKnown ?? _baseline;
    }

    /// <summary>
    /// 触发后台刷新（fire-and-forget），使用 Interlocked 防止重复刷新。
    /// </summary>
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
                _logger.LogWarning(ex, "后台刷新WhatsApp配置失败");
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

    private async Task<WhatsAppOptions> LoadMergedOptionsAsync()
    {
        // 以 appsettings.json 基线为底
        var result = new WhatsAppOptions
        {
            PhoneNumberId = _baseline.PhoneNumberId,
            AccessToken = _baseline.AccessToken,
            AppSecret = _baseline.AppSecret,
            VerifyToken = _baseline.VerifyToken,
            BusinessPhoneNumber = _baseline.BusinessPhoneNumber,
            ApiVersion = _baseline.ApiVersion,
            MailboxId = _baseline.MailboxId,
            ContactCollectionEnabled = _baseline.ContactCollectionEnabled,
            AutoReplyEnabled = _baseline.AutoReplyEnabled,
            AutoReplyMessage = _baseline.AutoReplyMessage,
            Enabled = _baseline.Enabled,
            CallbackPath = _baseline.CallbackPath
        };

        // 从 SystemOptions 数据库覆盖
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var phoneNumberId = await unitOfWork.SystemOptions.GetValueAsync(WhatsAppOptionKeys.PhoneNumberId);
            if (!string.IsNullOrWhiteSpace(phoneNumberId))
                result.PhoneNumberId = phoneNumberId;

            var accessTokenCipher = await unitOfWork.SystemOptions.GetValueAsync(WhatsAppOptionKeys.AccessToken);
            if (!string.IsNullOrWhiteSpace(accessTokenCipher))
            {
                result.AccessToken = DecryptIfEncrypted(accessTokenCipher);
            }

            var appSecretCipher = await unitOfWork.SystemOptions.GetValueAsync(WhatsAppOptionKeys.AppSecret);
            if (!string.IsNullOrWhiteSpace(appSecretCipher))
            {
                result.AppSecret = DecryptIfEncrypted(appSecretCipher);
            }

            var verifyToken = await unitOfWork.SystemOptions.GetValueAsync(WhatsAppOptionKeys.VerifyToken);
            if (!string.IsNullOrWhiteSpace(verifyToken))
                result.VerifyToken = verifyToken;

            var businessPhoneNumber = await unitOfWork.SystemOptions.GetValueAsync(WhatsAppOptionKeys.BusinessPhoneNumber);
            if (!string.IsNullOrWhiteSpace(businessPhoneNumber))
                result.BusinessPhoneNumber = businessPhoneNumber;

            var apiVersion = await unitOfWork.SystemOptions.GetValueAsync(WhatsAppOptionKeys.ApiVersion);
            if (!string.IsNullOrWhiteSpace(apiVersion))
                result.ApiVersion = apiVersion;

            var mailboxIdStr = await unitOfWork.SystemOptions.GetValueAsync(WhatsAppOptionKeys.MailboxId);
            if (!string.IsNullOrWhiteSpace(mailboxIdStr) && int.TryParse(mailboxIdStr, out var mailboxId))
                result.MailboxId = mailboxId;

            var contactCollectionEnabledStr = await unitOfWork.SystemOptions.GetValueAsync(WhatsAppOptionKeys.ContactCollectionEnabled);
            if (bool.TryParse(contactCollectionEnabledStr, out var contactCollectionEnabled))
            {
                result.ContactCollectionEnabled = contactCollectionEnabled;
            }

            var autoReplyEnabledStr = await unitOfWork.SystemOptions.GetValueAsync(WhatsAppOptionKeys.AutoReplyEnabled);
            if (!string.IsNullOrWhiteSpace(autoReplyEnabledStr) && bool.TryParse(autoReplyEnabledStr, out var autoReplyEnabled))
                result.AutoReplyEnabled = autoReplyEnabled;

            var autoReplyMessage = await unitOfWork.SystemOptions.GetValueAsync(WhatsAppOptionKeys.AutoReplyMessage);
            if (autoReplyMessage != null)
                result.AutoReplyMessage = autoReplyMessage;

            var enabledStr = await unitOfWork.SystemOptions.GetValueAsync(WhatsAppOptionKeys.Enabled);
            if (!string.IsNullOrWhiteSpace(enabledStr) && bool.TryParse(enabledStr, out var enabled))
                result.Enabled = enabled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载 WhatsApp 配置从数据库失败，使用基线配置");
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
            // 无前缀：可能是历史明文，也可能是无前缀的旧密文
            // 尝试解密，成功则返回明文
            if (_encryptionService.TryDecrypt(storedValue, out var plainText) && !string.IsNullOrEmpty(plainText))
            {
                return plainText;
            }

            // 解密失败但看起来像 base64 密文，记录错误
            if (storedValue.Length >= 24 && IsLikelyBase64(storedValue))
            {
                _logger.LogError("WhatsApp敏感配置解密失败：Encryption:Key 可能已变更。请重新配置 AccessToken/AppSecret");
                return null;
            }

            // 明显不是密文，视为历史明文
            return storedValue;
        }

        var cipherText = storedValue[encPrefix.Length..];
        if (_encryptionService.TryDecrypt(cipherText, out var plain) && !string.IsNullOrEmpty(plain))
        {
            return plain;
        }

        _logger.LogError("WhatsApp敏感配置解密失败：Encryption:Key 可能已变更。请重新配置 AccessToken/AppSecret");
        return null;
    }

    private static bool IsLikelyBase64(string value)
    {
        return value.All(c => char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=');
    }
}
