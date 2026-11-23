using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Services;

namespace NoneadCSS.Infrastructure.Services.Line;

/// <summary>
/// LINE 有效配置提供器：合并 appsettings 基线与 SystemOptions 数据库覆盖。
/// 敏感字段（ChannelSecret / ChannelAccessToken）在库中以 "ENC:" 前缀加密存储，读取时解密。
/// 注册为 Singleton；后台刷新通过 IServiceScopeFactory 建独立 scope，避免 DbContext 被提前释放。
/// </summary>
public class LineSettingsProvider : ILineSettingsProvider
{
    private const string CacheKey = "Line:EffectiveSettings";
    private const string EncryptedPrefix = "ENC:";

    private readonly LineOptions _baseline;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEncryptionService _encryptionService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LineSettingsProvider> _logger;
    private readonly TimeSpan _cacheDuration;

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private LineOptions? _lastKnown;
    private int _refreshing;

    public LineSettingsProvider(
        IOptions<LineOptions> baseline,
        IOptions<CacheOptions> cacheOptions,
        IServiceScopeFactory scopeFactory,
        IEncryptionService encryptionService,
        IMemoryCache cache,
        ILogger<LineSettingsProvider> logger)
    {
        _baseline = baseline.Value;
        _cacheDuration = TimeSpan.FromMinutes(cacheOptions.Value.ChannelSettingsCacheMinutes);
        _scopeFactory = scopeFactory;
        _encryptionService = encryptionService;
        _cache = cache;
        _logger = logger;
    }

    public LineOptions Current
    {
        get
        {
            if (_cache.TryGetValue<LineOptions>(CacheKey, out var cached) && cached != null)
            {
                _lastKnown = cached;
                return cached;
            }

            TriggerBackgroundRefresh();
            return _lastKnown ?? _baseline;
        }
    }

    public async Task<LineOptions> GetCurrentAsync()
    {
        if (_cache.TryGetValue<LineOptions>(CacheKey, out var cached) && cached != null)
        {
            _lastKnown = cached;
            return cached;
        }

        await RefreshAsync();
        if (_cache.TryGetValue<LineOptions>(CacheKey, out var reloaded) && reloaded != null)
        {
            _lastKnown = reloaded;
            return reloaded;
        }

        return _lastKnown ?? _baseline;
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
                _logger.LogWarning(ex, "后台刷新 LINE 配置失败");
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

    private async Task<LineOptions> LoadMergedOptionsAsync()
    {
        // 以基线为起点逐字段复制，避免直接改动 IOptions 单例
        var result = new LineOptions
        {
            ChannelId = _baseline.ChannelId,
            ChannelSecret = _baseline.ChannelSecret,
            ChannelAccessToken = _baseline.ChannelAccessToken,
            ChannelAccessTokenKeyId = _baseline.ChannelAccessTokenKeyId,
            ApiBaseUrl = _baseline.ApiBaseUrl,
            DataApiBaseUrl = _baseline.DataApiBaseUrl,
            AttachmentPublicBaseUrl = _baseline.AttachmentPublicBaseUrl,
            AttachmentPublicUrlTtlMinutes = _baseline.AttachmentPublicUrlTtlMinutes,
            WebhookEnabled = _baseline.WebhookEnabled,
            RichMenuId = _baseline.RichMenuId,
            MarkAsReadEnabled = _baseline.MarkAsReadEnabled,
            LoadingIndicatorEnabled = _baseline.LoadingIndicatorEnabled,
            AutoReplyEnabled = _baseline.AutoReplyEnabled,
            AutoReplyMessage = _baseline.AutoReplyMessage,
            ProfileEnrichEnabled = _baseline.ProfileEnrichEnabled,
            TextChunkChars = _baseline.TextChunkChars,
            MaxMessagesPerRequest = _baseline.MaxMessagesPerRequest,
            ReplyTokenTtlSeconds = _baseline.ReplyTokenTtlSeconds,
            HumanHandoverMuteMinutes = _baseline.HumanHandoverMuteMinutes,
            QuotaWarnPercent = _baseline.QuotaWarnPercent,
            StatusCheckTimeoutSeconds = _baseline.StatusCheckTimeoutSeconds,
            RetryMaxAttempts = _baseline.RetryMaxAttempts,
            MaxInboundMediaBytes = _baseline.MaxInboundMediaBytes,
            MaxOutboundAttachmentBytes = _baseline.MaxOutboundAttachmentBytes,
            OutboundImageExtensions = _baseline.OutboundImageExtensions,
            MailboxId = _baseline.MailboxId,
            Enabled = _baseline.Enabled,
            WebhookPath = _baseline.WebhookPath,
        };

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var values = await unitOfWork.SystemOptions.GetValuesByKeysAsync(new[]
            {
                LineOptionKeys.ChannelId,
                LineOptionKeys.ChannelSecret,
                LineOptionKeys.ChannelAccessToken,
                LineOptionKeys.ChannelAccessTokenKeyId,
                LineOptionKeys.ApiBaseUrl,
                LineOptionKeys.DataApiBaseUrl,
                LineOptionKeys.AttachmentPublicBaseUrl,
                LineOptionKeys.AttachmentPublicUrlTtlMinutes,
                LineOptionKeys.WebhookEnabled,
                LineOptionKeys.RichMenuId,
                LineOptionKeys.MarkAsReadEnabled,
                LineOptionKeys.LoadingIndicatorEnabled,
                LineOptionKeys.AutoReplyEnabled,
                LineOptionKeys.AutoReplyMessage,
                LineOptionKeys.ProfileEnrichEnabled,
                LineOptionKeys.TextChunkChars,
                LineOptionKeys.MaxMessagesPerRequest,
                LineOptionKeys.ReplyTokenTtlSeconds,
                LineOptionKeys.HumanHandoverMuteMinutes,
                LineOptionKeys.QuotaWarnPercent,
                LineOptionKeys.StatusCheckTimeoutSeconds,
                LineOptionKeys.RetryMaxAttempts,
                LineOptionKeys.MaxInboundMediaBytes,
                LineOptionKeys.MaxOutboundAttachmentBytes,
                LineOptionKeys.OutboundImageExtensions,
                LineOptionKeys.MailboxId,
                LineOptionKeys.Enabled,
            });

            if (values.Count == 0)
            {
                return result;
            }

            string? Get(string key) => values.TryGetValue(key, out var v) ? v : null;

            // ── 文本字段 ────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(Get(LineOptionKeys.ChannelId)))
            {
                result.ChannelId = Get(LineOptionKeys.ChannelId);
            }

            if (!string.IsNullOrWhiteSpace(Get(LineOptionKeys.RichMenuId)))
            {
                result.RichMenuId = Get(LineOptionKeys.RichMenuId);
            }

            if (!string.IsNullOrWhiteSpace(Get(LineOptionKeys.ChannelAccessTokenKeyId)))
            {
                result.ChannelAccessTokenKeyId = Get(LineOptionKeys.ChannelAccessTokenKeyId);
            }

            if (!string.IsNullOrWhiteSpace(Get(LineOptionKeys.ApiBaseUrl)))
            {
                result.ApiBaseUrl = Get(LineOptionKeys.ApiBaseUrl)!;
            }

            if (!string.IsNullOrWhiteSpace(Get(LineOptionKeys.DataApiBaseUrl)))
            {
                result.DataApiBaseUrl = Get(LineOptionKeys.DataApiBaseUrl)!;
            }

            if (!string.IsNullOrWhiteSpace(Get(LineOptionKeys.AttachmentPublicBaseUrl)))
            {
                result.AttachmentPublicBaseUrl = Get(LineOptionKeys.AttachmentPublicBaseUrl);
            }

            if (!string.IsNullOrWhiteSpace(Get(LineOptionKeys.AutoReplyMessage)))
            {
                result.AutoReplyMessage = Get(LineOptionKeys.AutoReplyMessage);
            }

            if (!string.IsNullOrWhiteSpace(Get(LineOptionKeys.OutboundImageExtensions)))
            {
                result.OutboundImageExtensions = Get(LineOptionKeys.OutboundImageExtensions)!;
            }

            // ── 敏感字段（解密后覆盖；解不出来时保留基线而不是写入 null）──────
            var secretCipher = Get(LineOptionKeys.ChannelSecret);
            if (!string.IsNullOrWhiteSpace(secretCipher))
            {
                var secret = DecryptIfEncrypted(secretCipher!, "ChannelSecret");
                if (!string.IsNullOrWhiteSpace(secret))
                {
                    result.ChannelSecret = secret;
                }
            }

            var tokenCipher = Get(LineOptionKeys.ChannelAccessToken);
            if (!string.IsNullOrWhiteSpace(tokenCipher))
            {
                var token = DecryptIfEncrypted(tokenCipher!, "ChannelAccessToken");
                if (!string.IsNullOrWhiteSpace(token))
                {
                    result.ChannelAccessToken = token;
                }
            }

            // ── 布尔字段 ────────────────────────────────────────────────
            if (bool.TryParse(Get(LineOptionKeys.Enabled), out var enabled))
            {
                result.Enabled = enabled;
            }

            if (bool.TryParse(Get(LineOptionKeys.WebhookEnabled), out var webhookEnabled))
            {
                result.WebhookEnabled = webhookEnabled;
            }

            if (bool.TryParse(Get(LineOptionKeys.MarkAsReadEnabled), out var markAsRead))
            {
                result.MarkAsReadEnabled = markAsRead;
            }

            if (bool.TryParse(Get(LineOptionKeys.LoadingIndicatorEnabled), out var loading))
            {
                result.LoadingIndicatorEnabled = loading;
            }

            if (bool.TryParse(Get(LineOptionKeys.AutoReplyEnabled), out var autoReply))
            {
                result.AutoReplyEnabled = autoReply;
            }

            if (bool.TryParse(Get(LineOptionKeys.ProfileEnrichEnabled), out var enrich))
            {
                result.ProfileEnrichEnabled = enrich;
            }

            // ── 数值字段 ────────────────────────────────────────────────
            if (int.TryParse(Get(LineOptionKeys.AttachmentPublicUrlTtlMinutes), out var ttl) && ttl > 0)
            {
                result.AttachmentPublicUrlTtlMinutes = ttl;
            }

            if (int.TryParse(Get(LineOptionKeys.TextChunkChars), out var chunk) && chunk > 0)
            {
                result.TextChunkChars = chunk;
            }

            if (int.TryParse(Get(LineOptionKeys.MaxMessagesPerRequest), out var maxMsg) && maxMsg > 0)
            {
                result.MaxMessagesPerRequest = maxMsg;
            }

            if (int.TryParse(Get(LineOptionKeys.ReplyTokenTtlSeconds), out var ttlSeconds) && ttlSeconds > 0)
            {
                result.ReplyTokenTtlSeconds = ttlSeconds;
            }

            if (int.TryParse(Get(LineOptionKeys.HumanHandoverMuteMinutes), out var mute) && mute >= 0)
            {
                result.HumanHandoverMuteMinutes = mute;
            }

            if (int.TryParse(Get(LineOptionKeys.QuotaWarnPercent), out var warn) && warn > 0)
            {
                result.QuotaWarnPercent = warn;
            }

            if (int.TryParse(Get(LineOptionKeys.StatusCheckTimeoutSeconds), out var timeout) && timeout > 0)
            {
                result.StatusCheckTimeoutSeconds = timeout;
            }

            if (int.TryParse(Get(LineOptionKeys.RetryMaxAttempts), out var retries) && retries >= 0)
            {
                result.RetryMaxAttempts = retries;
            }

            if (long.TryParse(Get(LineOptionKeys.MaxInboundMediaBytes), out var inboundMax) && inboundMax > 0)
            {
                result.MaxInboundMediaBytes = inboundMax;
            }

            if (long.TryParse(Get(LineOptionKeys.MaxOutboundAttachmentBytes), out var outboundMax) && outboundMax > 0)
            {
                result.MaxOutboundAttachmentBytes = outboundMax;
            }

            var mailboxRaw = Get(LineOptionKeys.MailboxId);
            if (!string.IsNullOrWhiteSpace(mailboxRaw))
            {
                result.MailboxId = int.TryParse(mailboxRaw, out var mailboxId) && mailboxId > 0 ? mailboxId : null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从数据库加载 LINE 配置失败，使用基线配置");
        }

        return result;
    }

    /// <summary>
    /// 解密以 "ENC:" 前缀存储的敏感配置。解密失败返回 null（调用方保留基线值），
    /// 无 "ENC:" 前缀时按历史明文/旧格式兼容处理。
    /// </summary>
    private string? DecryptIfEncrypted(string cipher, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(cipher))
        {
            return null;
        }

        if (!cipher.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            // 无 ENC: 前缀：先尝试直接解密（旧版本可能未加前缀）
            if (_encryptionService.TryDecrypt(cipher, out var legacy) && !string.IsNullOrWhiteSpace(legacy))
            {
                return legacy;
            }

            // 不像密文则视为明文直接返回；像密文却解不出来 ⇒ 多半是 Encryption:Key 变更
            if (cipher.Length >= 24 && IsLikelyBase64(cipher))
            {
                _logger.LogError("LINE 敏感配置解密失败（{Field}）：Encryption:Key 可能已变更，请在设置页重新保存 ChannelSecret/ChannelAccessToken", fieldName);
                return null;
            }

            return cipher;
        }

        var payload = cipher[EncryptedPrefix.Length..];
        if (_encryptionService.TryDecrypt(payload, out var plain) && !string.IsNullOrWhiteSpace(plain))
        {
            return plain;
        }

        _logger.LogError("LINE 敏感配置解密失败（{Field}）：Encryption:Key 可能已变更，请在设置页重新保存", fieldName);
        return null;
    }

    private static bool IsLikelyBase64(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c) && c != '+' && c != '/' && c != '=' && c != '-' && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
