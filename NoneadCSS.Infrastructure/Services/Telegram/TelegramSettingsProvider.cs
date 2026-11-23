using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Services;

namespace NoneadCSS.Infrastructure.Services.Telegram;

/// <summary>
/// Telegram 有效配置提供者：appsettings 基线与数据库 SystemOptions（category=telegram）合并。
/// 与 WhatsAppSettingsProvider 同构：Singleton + IMemoryCache + 后台刷新，绝不做 sync-over-async。
/// 敏感字段（BotToken / WebhookSecretToken / LinkTokenHmacKey）在库中以 "ENC:" 前缀密文存储，
/// 解密失败时返回 null（调用方按"未配置"处理），任何情况下都不写入日志。
/// </summary>
public class TelegramSettingsProvider : ITelegramSettingsProvider
{
    private const string CacheKey = "Telegram:EffectiveSettings";
    private const string EncPrefix = "ENC:";

    private readonly TelegramOptions _baseline;
    private readonly TimeSpan _cacheDuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEncryptionService _encryptionService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TelegramSettingsProvider> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private TelegramOptions? _lastKnown;
    private int _refreshing;

    public TelegramSettingsProvider(
        IOptions<TelegramOptions> baseline,
        IOptions<CacheOptions> cacheOptions,
        IServiceScopeFactory scopeFactory,
        IEncryptionService encryptionService,
        IMemoryCache cache,
        ILogger<TelegramSettingsProvider> logger)
    {
        _baseline = baseline.Value;
        _cacheDuration = TimeSpan.FromMinutes(cacheOptions.Value.ChannelSettingsCacheMinutes);
        _scopeFactory = scopeFactory;
        _encryptionService = encryptionService;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public TelegramOptions Current
    {
        get
        {
            if (_cache.TryGetValue(CacheKey, out TelegramOptions? cached) && cached != null)
            {
                _lastKnown = cached;
                return cached;
            }

            // 缓存缺失：立即返回上一次已知配置（或基线），同时后台刷新，避免阻塞请求线程
            TriggerBackgroundRefresh();
            return _lastKnown ?? _baseline;
        }
    }

    /// <inheritdoc />
    public async Task<TelegramOptions> GetCurrentAsync()
    {
        if (_cache.TryGetValue(CacheKey, out TelegramOptions? cached) && cached != null)
        {
            _lastKnown = cached;
            return cached;
        }

        await RefreshAsync();

        if (_cache.TryGetValue(CacheKey, out TelegramOptions? refreshed) && refreshed != null)
        {
            _lastKnown = refreshed;
            return refreshed;
        }

        return _lastKnown ?? _baseline;
    }

    /// <inheritdoc />
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

    private void TriggerBackgroundRefresh()
    {
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "后台刷新 Telegram 配置失败");
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        });
    }

    private async Task<TelegramOptions> LoadMergedOptionsAsync()
    {
        var merged = new TelegramOptions
        {
            Enabled = _baseline.Enabled,
            Mode = _baseline.Mode,
            BotToken = _baseline.BotToken,
            ApiBaseUrl = _baseline.ApiBaseUrl,
            UseLocalBotApiServer = _baseline.UseLocalBotApiServer,
            CallbackPath = _baseline.CallbackPath,
            WebhookUrl = _baseline.WebhookUrl,
            WebhookSecretToken = _baseline.WebhookSecretToken,
            WebhookCertificate = _baseline.WebhookCertificate,
            WebhookIpAddress = _baseline.WebhookIpAddress,
            MaxWebhookConnections = _baseline.MaxWebhookConnections,
            AllowedUpdates = _baseline.AllowedUpdates,
            PollingTimeout = _baseline.PollingTimeout,
            PollingLimit = _baseline.PollingLimit,
            DeleteWebhookOnSwitch = _baseline.DeleteWebhookOnSwitch,
            BotUsername = _baseline.BotUsername,
            AcceptGroupMessages = _baseline.AcceptGroupMessages,
            MailboxId = _baseline.MailboxId,
            AutoReplyEnabled = _baseline.AutoReplyEnabled,
            AutoReplyMessage = _baseline.AutoReplyMessage,
            ConversationMatchWindowHours = _baseline.ConversationMatchWindowHours,
            MaxMessageChars = _baseline.MaxMessageChars,
            RateLimitGlobalPerSeconds = _baseline.RateLimitGlobalPerSeconds,
            RateLimitPerChatPerSeconds = _baseline.RateLimitPerChatPerSeconds,
            MaxLocalBackoffSeconds = _baseline.MaxLocalBackoffSeconds,
            StatusCheckTimeoutSeconds = _baseline.StatusCheckTimeoutSeconds,
            LinkTokenHmacKey = _baseline.LinkTokenHmacKey,
            LinkTokenTtlMinutes = _baseline.LinkTokenTtlMinutes
        };

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var enabled = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.Enabled);
            if (bool.TryParse(enabled, out var enabledValue))
            {
                merged.Enabled = enabledValue;
            }

            var mode = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.Mode);
            if (!string.IsNullOrWhiteSpace(mode))
            {
                merged.Mode = mode.Trim().ToLowerInvariant();
            }

            // ===== 敏感字段：密文读取，解密失败按未配置处理（不回退明文、不打印值） =====
            var botToken = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.BotToken);
            if (!string.IsNullOrWhiteSpace(botToken))
            {
                merged.BotToken = DecryptIfEncrypted(botToken);
            }

            var webhookSecret = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.WebhookSecretToken);
            if (!string.IsNullOrWhiteSpace(webhookSecret))
            {
                merged.WebhookSecretToken = DecryptIfEncrypted(webhookSecret);
            }

            var linkKey = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.LinkTokenHmacKey);
            if (!string.IsNullOrWhiteSpace(linkKey))
            {
                merged.LinkTokenHmacKey = DecryptIfEncrypted(linkKey);
            }

            var apiBaseUrl = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.ApiBaseUrl);
            if (!string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                merged.ApiBaseUrl = apiBaseUrl.Trim().TrimEnd('/');
            }

            var useLocal = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.UseLocalBotApiServer);
            if (bool.TryParse(useLocal, out var useLocalValue))
            {
                merged.UseLocalBotApiServer = useLocalValue;
            }

            var callbackPath = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.CallbackPath);
            if (!string.IsNullOrWhiteSpace(callbackPath))
            {
                merged.CallbackPath = callbackPath.Trim();
            }

            var webhookUrl = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.WebhookUrl);
            if (!string.IsNullOrWhiteSpace(webhookUrl))
            {
                merged.WebhookUrl = webhookUrl.Trim();
            }

            var ipAddress = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.WebhookIpAddress);
            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                merged.WebhookIpAddress = ipAddress.Trim();
            }

            // WebhookCertificate 刻意不提供数据库覆盖：PEM 体积大且属部署材料，只允许来自 appsettings / 环境变量

            var maxConnections = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.MaxWebhookConnections);
            if (int.TryParse(maxConnections, out var maxConnectionsValue) && maxConnectionsValue is >= 1 and <= 100)
            {
                merged.MaxWebhookConnections = maxConnectionsValue;
            }

            var allowedUpdates = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.AllowedUpdates);
            if (allowedUpdates != null)
            {
                merged.AllowedUpdates = allowedUpdates
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            var pollingTimeout = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.PollingTimeout);
            if (int.TryParse(pollingTimeout, out var pollingTimeoutValue) && pollingTimeoutValue is >= 0 and <= 50)
            {
                merged.PollingTimeout = pollingTimeoutValue;
            }

            var pollingLimit = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.PollingLimit);
            if (int.TryParse(pollingLimit, out var pollingLimitValue) && pollingLimitValue is >= 1 and <= 100)
            {
                merged.PollingLimit = pollingLimitValue;
            }

            var deleteOnSwitch = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.DeleteWebhookOnSwitch);
            if (bool.TryParse(deleteOnSwitch, out var deleteOnSwitchValue))
            {
                merged.DeleteWebhookOnSwitch = deleteOnSwitchValue;
            }

            var botUsername = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.BotUsername);
            if (!string.IsNullOrWhiteSpace(botUsername))
            {
                merged.BotUsername = botUsername.Trim().TrimStart('@');
            }

            var acceptGroups = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.AcceptGroupMessages);
            if (bool.TryParse(acceptGroups, out var acceptGroupsValue))
            {
                merged.AcceptGroupMessages = acceptGroupsValue;
            }

            var mailboxId = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.MailboxId);
            if (int.TryParse(mailboxId, out var mailboxIdValue))
            {
                merged.MailboxId = mailboxIdValue;
            }

            var autoReplyEnabled = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.AutoReplyEnabled);
            if (bool.TryParse(autoReplyEnabled, out var autoReplyEnabledValue))
            {
                merged.AutoReplyEnabled = autoReplyEnabledValue;
            }

            var autoReplyMessage = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.AutoReplyMessage);
            if (autoReplyMessage != null)
            {
                merged.AutoReplyMessage = autoReplyMessage;
            }

            var matchWindow = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.ConversationMatchWindowHours);
            if (int.TryParse(matchWindow, out var matchWindowValue) && matchWindowValue > 0)
            {
                merged.ConversationMatchWindowHours = matchWindowValue;
            }

            var maxChars = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.MaxMessageChars);
            if (int.TryParse(maxChars, out var maxCharsValue) && maxCharsValue > 0)
            {
                merged.MaxMessageChars = Math.Min(maxCharsValue, 4096);
            }

            var globalRate = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.RateLimitGlobalPerSeconds);
            if (int.TryParse(globalRate, out var globalRateValue) && globalRateValue > 0)
            {
                merged.RateLimitGlobalPerSeconds = Math.Min(globalRateValue, 30);
            }

            var chatRate = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.RateLimitPerChatPerSeconds);
            if (int.TryParse(chatRate, out var chatRateValue) && chatRateValue > 0)
            {
                merged.RateLimitPerChatPerSeconds = chatRateValue;
            }

            var backoff = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.MaxLocalBackoffSeconds);
            if (int.TryParse(backoff, out var backoffValue) && backoffValue >= 0)
            {
                merged.MaxLocalBackoffSeconds = backoffValue;
            }

            var statusTimeout = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.StatusCheckTimeoutSeconds);
            if (int.TryParse(statusTimeout, out var statusTimeoutValue) && statusTimeoutValue > 0)
            {
                merged.StatusCheckTimeoutSeconds = statusTimeoutValue;
            }

            var linkTtl = await unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.LinkTokenTtlMinutes);
            if (int.TryParse(linkTtl, out var linkTtlValue) && linkTtlValue > 0)
            {
                merged.LinkTokenTtlMinutes = linkTtlValue;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载 Telegram 配置从数据库失败，使用基线配置");
        }

        return merged;
    }

    /// <summary>解密 ENC: 前缀密文；无历史明文兼容风险时返回 null（视为未配置）</summary>
    private string? DecryptIfEncrypted(string storedValue)
    {
        if (!storedValue.StartsWith(EncPrefix, StringComparison.Ordinal))
        {
            if (_encryptionService.TryDecrypt(storedValue, out var plainText))
            {
                return plainText;
            }

            if (storedValue.Length >= 24 && IsLikelyBase64(storedValue))
            {
                _logger.LogError("Telegram 敏感配置解密失败：Encryption:Key 可能已变更。请在集成配置页重新填写 BotToken / WebhookSecretToken");
                return null;
            }

            // 历史明文配置（早期未加密写入）直接沿用
            return storedValue;
        }

        if (_encryptionService.TryDecrypt(storedValue[EncPrefix.Length..], out var decrypted))
        {
            return decrypted;
        }

        _logger.LogError("Telegram 敏感配置解密失败：Encryption:Key 可能已变更。请在集成配置页重新填写 BotToken / WebhookSecretToken");
        return null;
    }

    private static bool IsLikelyBase64(string value)
    {
        return value.All(c => char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=');
    }
}
