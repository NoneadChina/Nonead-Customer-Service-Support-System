using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Services;

namespace NoneadCSS.Infrastructure.Services.Feishu;

/// <summary>
/// 飞书有效配置提供器实现 - 合并 appsettings.json 基线配置与 SystemOptions 数据库覆盖配置。
/// 敏感字段（AppSecret、EncryptKey）在数据库中加密存储，读取时解密。
/// 使用 IMemoryCache 缓存合并后的配置，避免每次请求都查库。
/// 注册为 Singleton，通过 IServiceScopeFactory 在后台刷新时创建独立 scope，避免 DbContext 被提前释放。
/// </summary>
public class FeishuSettingsProvider : IFeishuSettingsProvider
{
    private readonly FeishuOptions _baseline;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEncryptionService _encryptionService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FeishuSettingsProvider> _logger;

    private const string CacheKey = "Feishu:EffectiveSettings";
    private readonly TimeSpan _cacheDuration;

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private FeishuOptions? _lastKnown;
    private int _refreshing; // 0=idle, 1=refreshing (Interlocked)

    public FeishuSettingsProvider(
        IOptions<FeishuOptions> baseline,
        IOptions<CacheOptions> cacheOptions,
        IServiceScopeFactory scopeFactory,
        IEncryptionService encryptionService,
        IMemoryCache cache,
        ILogger<FeishuSettingsProvider> logger)
    {
        _baseline = baseline.Value;
        _cacheDuration = TimeSpan.FromMinutes(cacheOptions.Value.ChannelSettingsCacheMinutes);
        _scopeFactory = scopeFactory;
        _encryptionService = encryptionService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// 当前有效的飞书配置。
    /// 缓存命中时直接返回；缓存 miss 时返回上次已知值（或基线）并触发后台异步刷新，
    /// 避免 sync-over-async 阻塞线程池。
    /// </summary>
    public FeishuOptions Current
    {
        get
        {
            if (_cache.TryGetValue<FeishuOptions>(CacheKey, out var cached) && cached != null)
            {
                _lastKnown = cached;
                return cached;
            }

            // 缓存 miss：返回上次已知值（或基线），触发后台刷新（不阻塞调用线程）
            TriggerBackgroundRefresh();
            return _lastKnown ?? _baseline;
        }
    }

    /// <summary>
    /// 异步获取当前配置（优先读缓存，miss 时异步加载）。推荐在 async 调用链中使用。
    /// </summary>
    public async Task<FeishuOptions> GetCurrentAsync()
    {
        if (_cache.TryGetValue<FeishuOptions>(CacheKey, out var cached) && cached != null)
        {
            _lastKnown = cached;
            return cached;
        }

        await RefreshAsync();
        if (_cache.TryGetValue<FeishuOptions>(CacheKey, out var reloaded) && reloaded != null)
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
            return; // 已有刷新在进行中

        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "后台刷新飞书配置失败");
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

    private async Task<FeishuOptions> LoadMergedOptionsAsync()
    {
        // 以 appsettings.json 为基线，数据库非空值覆盖
        var result = new FeishuOptions
        {
            AppId = _baseline.AppId,
            AppSecret = _baseline.AppSecret,
            VerificationToken = _baseline.VerificationToken,
            EncryptKey = _baseline.EncryptKey,
            EncryptEnabled = _baseline.EncryptEnabled,
            MailboxId = _baseline.MailboxId,
            ContactCollectionEnabled = _baseline.ContactCollectionEnabled,
            AutoReplyEnabled = _baseline.AutoReplyEnabled,
            AutoReplyMessage = _baseline.AutoReplyMessage,
            Enabled = _baseline.Enabled,
            CallbackPath = _baseline.CallbackPath,
            ReceiveMode = _baseline.ReceiveMode
        };

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var appId = await unitOfWork.SystemOptions.GetValueAsync(FeishuOptionKeys.AppId);
            if (!string.IsNullOrWhiteSpace(appId)) result.AppId = appId;

            var appSecretCipher = await unitOfWork.SystemOptions.GetValueAsync(FeishuOptionKeys.AppSecret);
            if (!string.IsNullOrWhiteSpace(appSecretCipher))
            {
                // 加密存储格式：ENC:<密文>；无前缀视为历史明文直接使用
                result.AppSecret = DecryptIfEncrypted(appSecretCipher);
            }

            var verificationToken = await unitOfWork.SystemOptions.GetValueAsync(FeishuOptionKeys.VerificationToken);
            if (!string.IsNullOrWhiteSpace(verificationToken)) result.VerificationToken = verificationToken;

            var encryptKeyCipher = await unitOfWork.SystemOptions.GetValueAsync(FeishuOptionKeys.EncryptKey);
            if (!string.IsNullOrWhiteSpace(encryptKeyCipher))
            {
                // 加密存储格式：ENC:<密文>；无前缀视为历史明文直接使用
                result.EncryptKey = DecryptIfEncrypted(encryptKeyCipher);
            }

            var encryptEnabledStr = await unitOfWork.SystemOptions.GetValueAsync(FeishuOptionKeys.EncryptEnabled);
            if (bool.TryParse(encryptEnabledStr, out var encryptEnabled))
            {
                result.EncryptEnabled = encryptEnabled;
            }

            var mailboxIdStr = await unitOfWork.SystemOptions.GetValueAsync(FeishuOptionKeys.MailboxId);
            if (int.TryParse(mailboxIdStr, out var mailboxId) && mailboxId > 0)
            {
                result.MailboxId = mailboxId;
            }

            var contactCollectionEnabledStr = await unitOfWork.SystemOptions.GetValueAsync(FeishuOptionKeys.ContactCollectionEnabled);
            if (bool.TryParse(contactCollectionEnabledStr, out var contactCollectionEnabled))
            {
                result.ContactCollectionEnabled = contactCollectionEnabled;
            }

            var autoReplyEnabledStr = await unitOfWork.SystemOptions.GetValueAsync(FeishuOptionKeys.AutoReplyEnabled);
            if (bool.TryParse(autoReplyEnabledStr, out var autoReplyEnabled))
            {
                result.AutoReplyEnabled = autoReplyEnabled;
            }

            var autoReplyMessage = await unitOfWork.SystemOptions.GetValueAsync(FeishuOptionKeys.AutoReplyMessage);
            if (autoReplyMessage != null) result.AutoReplyMessage = autoReplyMessage;

            var enabledStr = await unitOfWork.SystemOptions.GetValueAsync(FeishuOptionKeys.Enabled);
            if (bool.TryParse(enabledStr, out var enabled))
            {
                result.Enabled = enabled;
            }

            var receiveMode = await unitOfWork.SystemOptions.GetValueAsync(FeishuOptionKeys.ReceiveMode);
            if (!string.IsNullOrWhiteSpace(receiveMode))
            {
                result.ReceiveMode = receiveMode;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载飞书 SystemOptions 配置失败，回退到 appsettings.json 基线");
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
            return storedValue;
        }

        var cipherText = storedValue[encPrefix.Length..];
        if (_encryptionService.TryDecrypt(cipherText, out var plain) && !string.IsNullOrEmpty(plain))
        {
            return plain;
        }

        _logger.LogError("飞书敏感配置解密失败：Encryption:Key 可能已变更。请重新配置 AppSecret/EncryptKey");
        return null;
    }
}
