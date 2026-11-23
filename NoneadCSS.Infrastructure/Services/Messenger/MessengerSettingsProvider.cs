using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Services;

namespace NoneadCSS.Infrastructure.Services.Messenger;

/// <summary>
/// Messenger 有效配置提供器实现 - 合并 appsettings.json 基线配置与 SystemOptions 数据库覆盖配置。
/// 敏感字段（PageAccessToken、AppSecret、VerifyToken）在数据库中加密存储（"ENC:" 前缀），读取时解密。
/// 使用 IMemoryCache 缓存合并后的配置，避免每次请求都查库。
/// 注册为 Singleton，通过 IServiceScopeFactory 在后台刷新时创建独立 scope，避免 DbContext 被提前释放。
/// </summary>
public class MessengerSettingsProvider : IMessengerSettingsProvider
{
    private readonly MessengerOptions _baseline;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEncryptionService _encryptionService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MessengerSettingsProvider> _logger;

    private const string CacheKey = "Messenger:EffectiveSettings";
    private readonly TimeSpan _cacheDuration;

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private MessengerOptions? _lastKnown;
    private int _refreshing;

    public MessengerSettingsProvider(
        IOptions<MessengerOptions> baseline,
        IOptions<CacheOptions> cacheOptions,
        IServiceScopeFactory scopeFactory,
        IEncryptionService encryptionService,
        IMemoryCache cache,
        ILogger<MessengerSettingsProvider> logger)
    {
        _baseline = baseline.Value;
        _cacheDuration = TimeSpan.FromMinutes(cacheOptions.Value.ChannelSettingsCacheMinutes);
        _scopeFactory = scopeFactory;
        _encryptionService = encryptionService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// 当前有效的 Messenger 配置。
    /// 缓存命中时直接返回；缓存 miss 时返回上次已知值（或基线）并触发后台异步刷新，
    /// 避免 sync-over-async 阻塞线程池。
    /// </summary>
    public MessengerOptions Current
    {
        get
        {
            if (_cache.TryGetValue<MessengerOptions>(CacheKey, out var cached) && cached != null)
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
    public async Task<MessengerOptions> GetCurrentAsync()
    {
        if (_cache.TryGetValue<MessengerOptions>(CacheKey, out var cached) && cached != null)
        {
            _lastKnown = cached;
            return cached;
        }

        await RefreshAsync();
        if (_cache.TryGetValue<MessengerOptions>(CacheKey, out var reloaded) && reloaded != null)
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
                _logger.LogWarning(ex, "后台刷新Messenger配置失败");
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

    private async Task<MessengerOptions> LoadMergedOptionsAsync()
    {
        // 以 appsettings.json 基线为底（Graph 版本号来自配置，不写死）
        var result = new MessengerOptions
        {
            AppId = _baseline.AppId,
            AppSecret = _baseline.AppSecret,
            PageId = _baseline.PageId,
            PageAccessToken = _baseline.PageAccessToken,
            VerifyToken = _baseline.VerifyToken,
            PageUsername = _baseline.PageUsername,
            ApiVersion = _baseline.ApiVersion,
            MailboxId = _baseline.MailboxId,
            CallbackPath = _baseline.CallbackPath,
            TextChunkChars = _baseline.TextChunkChars,
            WindowHours = _baseline.WindowHours,
            WarningThresholdHours = _baseline.WarningThresholdHours,
            MessageTagPolicy = _baseline.MessageTagPolicy,
            MessageTag = _baseline.MessageTag,
            TypingIndicator = _baseline.TypingIndicator,
            MarkSeen = _baseline.MarkSeen,
            ContactCollectionEnabled = _baseline.ContactCollectionEnabled,
            AutoReplyEnabled = _baseline.AutoReplyEnabled,
            AutoReplyMessage = _baseline.AutoReplyMessage,
            Enabled = _baseline.Enabled,
            SubscribedFields = _baseline.SubscribedFields,
            MediaDownloadMaxMB = _baseline.MediaDownloadMaxMB,
            StatusCheckTimeoutSeconds = _baseline.StatusCheckTimeoutSeconds,
            RateLimitBackoffTries = _baseline.RateLimitBackoffTries
        };

        // 从 SystemOptions 数据库覆盖
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var appId = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.AppId);
            if (!string.IsNullOrWhiteSpace(appId))
                result.AppId = appId;

            var appSecretCipher = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.AppSecret);
            if (!string.IsNullOrWhiteSpace(appSecretCipher))
            {
                result.AppSecret = DecryptIfEncrypted(appSecretCipher);
            }

            var pageId = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.PageId);
            if (!string.IsNullOrWhiteSpace(pageId))
                result.PageId = pageId;

            var accessTokenCipher = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.PageAccessToken);
            if (!string.IsNullOrWhiteSpace(accessTokenCipher))
            {
                result.PageAccessToken = DecryptIfEncrypted(accessTokenCipher);
            }

            var verifyTokenCipher = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.VerifyToken);
            if (!string.IsNullOrWhiteSpace(verifyTokenCipher))
            {
                // VerifyToken 也按密文存储（与 PageAccessToken/AppSecret 一致），历史明文自动兼容
                var decrypted = DecryptIfEncrypted(verifyTokenCipher);
                result.VerifyToken = string.IsNullOrWhiteSpace(decrypted) ? null : decrypted;
            }

            var pageUsername = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.PageUsername);
            if (pageUsername != null)
                result.PageUsername = pageUsername;

            var apiVersion = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.ApiVersion);
            if (!string.IsNullOrWhiteSpace(apiVersion))
                result.ApiVersion = apiVersion;

            var mailboxIdStr = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.MailboxId);
            if (!string.IsNullOrWhiteSpace(mailboxIdStr) && int.TryParse(mailboxIdStr, out var mailboxId))
                result.MailboxId = mailboxId;

            var textChunkCharsStr = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.TextChunkChars);
            if (!string.IsNullOrWhiteSpace(textChunkCharsStr) && int.TryParse(textChunkCharsStr, out var textChunkChars))
                result.TextChunkChars = textChunkChars;

            var windowHoursStr = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.WindowHours);
            if (!string.IsNullOrWhiteSpace(windowHoursStr) && int.TryParse(windowHoursStr, out var windowHours))
                result.WindowHours = windowHours;

            var warningHoursStr = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.WarningThresholdHours);
            if (!string.IsNullOrWhiteSpace(warningHoursStr) && int.TryParse(warningHoursStr, out var warningHours))
                result.WarningThresholdHours = warningHours;

            var messageTagPolicy = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.MessageTagPolicy);
            if (!string.IsNullOrWhiteSpace(messageTagPolicy))
                result.MessageTagPolicy = messageTagPolicy;

            var messageTag = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.MessageTag);
            if (!string.IsNullOrWhiteSpace(messageTag))
                result.MessageTag = messageTag;

            var typingIndicatorStr = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.TypingIndicator);
            if (!string.IsNullOrWhiteSpace(typingIndicatorStr) && bool.TryParse(typingIndicatorStr, out var typingIndicator))
                result.TypingIndicator = typingIndicator;

            var markSeenStr = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.MarkSeen);
            if (!string.IsNullOrWhiteSpace(markSeenStr) && bool.TryParse(markSeenStr, out var markSeen))
                result.MarkSeen = markSeen;

            var contactCollectionEnabledStr = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.ContactCollectionEnabled);
            if (bool.TryParse(contactCollectionEnabledStr, out var contactCollectionEnabled))
            {
                result.ContactCollectionEnabled = contactCollectionEnabled;
            }

            var autoReplyEnabledStr = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.AutoReplyEnabled);
            if (!string.IsNullOrWhiteSpace(autoReplyEnabledStr) && bool.TryParse(autoReplyEnabledStr, out var autoReplyEnabled))
                result.AutoReplyEnabled = autoReplyEnabled;

            var autoReplyMessage = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.AutoReplyMessage);
            if (autoReplyMessage != null)
                result.AutoReplyMessage = autoReplyMessage;

            var enabledStr = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.Enabled);
            if (!string.IsNullOrWhiteSpace(enabledStr) && bool.TryParse(enabledStr, out var enabled))
                result.Enabled = enabled;

            var subscribedFields = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.SubscribedFields);
            if (!string.IsNullOrWhiteSpace(subscribedFields))
                result.SubscribedFields = subscribedFields;

            var mediaMaxMbStr = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.MediaDownloadMaxMB);
            if (!string.IsNullOrWhiteSpace(mediaMaxMbStr) && int.TryParse(mediaMaxMbStr, out var mediaMaxMb))
                result.MediaDownloadMaxMB = mediaMaxMb;

            var backoffTriesStr = await unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.RateLimitBackoffTries);
            if (!string.IsNullOrWhiteSpace(backoffTriesStr) && int.TryParse(backoffTriesStr, out var backoffTries))
                result.RateLimitBackoffTries = backoffTries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载 Messenger 配置从数据库失败，使用基线配置");
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
                _logger.LogError("Messenger敏感配置解密失败：Encryption:Key 可能已变更。请重新配置 PageAccessToken/AppSecret");
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

        _logger.LogError("Messenger敏感配置解密失败：Encryption:Key 可能已变更。请重新配置 PageAccessToken/AppSecret");
        return null;
    }

    private static bool IsLikelyBase64(string value)
    {
        return value.All(c => char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=');
    }
}
