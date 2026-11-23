using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Services;

namespace NoneadCSS.Infrastructure.Services.Outlook;

/// <summary>
/// Outlook 同步有效配置提供器 - 合并 appsettings.json 基线配置与 SystemOptions 数据库覆盖配置。
/// 敏感字段（ClientSecret）优先解密（"ENC:" 前缀格式），解密失败按 null 处理，避免将密文当作有效配置使用。
/// 使用 IMemoryCache 缓存（Singleton，跨请求共享），配置更新后由 OutlookSyncController 调用 RefreshAsync 刷新。
/// 注册为 Singleton，通过 IServiceScopeFactory 在后台刷新时创建独立 scope，避免 DbContext 被提前释放。
/// 模式与 <see cref="DingDing.DingDingSettingsProvider"/> 一致。
/// </summary>
public class OutlookSettingsProvider : IOutlookSettingsProvider
{
    private readonly IOptions<OutlookSyncOptions> _baseline;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<OutlookSettingsProvider> _logger;
    private readonly IMemoryCache _cache;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly TimeSpan _cacheDuration;
    private OutlookSyncOptions? _lastKnown;
    private int _refreshing;

    private const string CacheKey = "OutlookSyncSettings";

    public OutlookSettingsProvider(
        IOptions<OutlookSyncOptions> baseline,
        IOptions<CacheOptions> cacheOptions,
        IServiceScopeFactory scopeFactory,
        IEncryptionService encryptionService,
        IMemoryCache cache,
        ILogger<OutlookSettingsProvider> logger)
    {
        _baseline = baseline;
        _cacheDuration = TimeSpan.FromMinutes(cacheOptions.Value.ChannelSettingsCacheMinutes);
        _scopeFactory = scopeFactory;
        _encryptionService = encryptionService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// 当前有效的 Outlook 同步配置。
    /// 缓存命中时直接返回；缓存 miss 时返回上次已知值（或基线）并触发后台异步刷新，
    /// 避免 sync-over-async 阻塞线程池。
    /// </summary>
    public OutlookSyncOptions Current
    {
        get
        {
            if (_cache.TryGetValue<OutlookSyncOptions>(CacheKey, out var cached) && cached != null)
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
    public async Task<OutlookSyncOptions> GetCurrentAsync()
    {
        if (_cache.TryGetValue<OutlookSyncOptions>(CacheKey, out var cached) && cached != null)
        {
            _lastKnown = cached;
            return cached;
        }

        await RefreshAsync();
        if (_cache.TryGetValue<OutlookSyncOptions>(CacheKey, out var reloaded) && reloaded != null)
        {
            _lastKnown = reloaded;
            return reloaded;
        }

        return _lastKnown ?? _baseline.Value;
    }

    /// <summary>
    /// T000099: 异步获取指定用户的有效配置（用户级凭据 > 全局基线）。
    /// 用户级凭据存储在 OutlookSyncSetting 表的 ClientId/ClientSecret/TenantId/RedirectUri/Scopes 字段，
    /// 为空时回退到全局基线（appsettings + SystemOptions）。
    /// 注意：用户级 ClientSecret 由 EF Core HasConversion 自动加密/解密，此处读取即为明文。
    /// 不缓存（每个用户凭据不同，且 OAuth 流程调用频率低）。
    /// </summary>
    public async Task<OutlookSyncOptions> GetForUserAsync(int userId)
    {
        // 先获取全局基线配置（含 appsettings + SystemOptions 覆盖）
        var global = await GetCurrentAsync();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = unitOfWork.Repository<Domain.Entities.OutlookSyncSetting>();

            // 仅查询凭据字段，非 AsNoTracking 无所谓——只读不改
            var userSetting = await repo.GetQueryable(true)
                .Where(s => s.UserId == userId && s.DeletedAt == null)
                .Select(s => new
                {
                    s.ClientId,
                    s.ClientSecret,
                    s.TenantId,
                    s.RedirectUri,
                    s.Scopes
                })
                .FirstOrDefaultAsync();

            if (userSetting == null)
            {
                // 用户未自配置凭据，返回全局基线
                return global;
            }

            // 用户级字段覆盖全局基线（空值保留全局基线）
            var result = new OutlookSyncOptions
            {
                ClientId = !string.IsNullOrWhiteSpace(userSetting.ClientId) ? userSetting.ClientId : global.ClientId,
                ClientSecret = !string.IsNullOrWhiteSpace(userSetting.ClientSecret) ? userSetting.ClientSecret : global.ClientSecret,
                TenantId = !string.IsNullOrWhiteSpace(userSetting.TenantId) ? userSetting.TenantId : global.TenantId,
                RedirectUri = !string.IsNullOrWhiteSpace(userSetting.RedirectUri) ? userSetting.RedirectUri : global.RedirectUri,
                Scopes = !string.IsNullOrWhiteSpace(userSetting.Scopes) ? userSetting.Scopes : global.Scopes,
                // 非凭据配置沿用全局
                AuthorizeUrl = global.AuthorizeUrl,
                TokenUrl = global.TokenUrl,
                GraphBaseUrl = global.GraphBaseUrl,
                BackoffMinutes = global.BackoffMinutes,
                AutoDisableThreshold = global.AutoDisableThreshold,
                LogFlushBatchSize = global.LogFlushBatchSize,
                MappingBatchSize = global.MappingBatchSize,
                DeltaPageSize = global.DeltaPageSize,
                MaxHttpConcurrency = global.MaxHttpConcurrency,
                MaxDeltaRestarts = global.MaxDeltaRestarts,
                ConflictStrategy = global.ConflictStrategy
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载用户 {UserId} 的 Outlook 凭据失败，回退到全局基线", userId);
            return global;
        }
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
                _logger.LogWarning(ex, "后台刷新 Outlook 同步配置失败");
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

    private async Task<OutlookSyncOptions> LoadMergedOptionsAsync()
    {
        // 以 appsettings.json（或环境变量覆盖后的 IOptions）为基线
        var b = _baseline.Value;
        var result = new OutlookSyncOptions
        {
            ClientId = b.ClientId,
            ClientSecret = b.ClientSecret,
            TenantId = b.TenantId,
            RedirectUri = b.RedirectUri,
            Scopes = b.Scopes,
            AuthorizeUrl = b.AuthorizeUrl,
            TokenUrl = b.TokenUrl,
            GraphBaseUrl = b.GraphBaseUrl,
            // 同步策略参数（不在管理后台 UI 暴露，沿用基线 / appsettings）
            BackoffMinutes = b.BackoffMinutes,
            AutoDisableThreshold = b.AutoDisableThreshold,
            LogFlushBatchSize = b.LogFlushBatchSize,
            MappingBatchSize = b.MappingBatchSize,
            DeltaPageSize = b.DeltaPageSize,
            MaxHttpConcurrency = b.MaxHttpConcurrency,
            MaxDeltaRestarts = b.MaxDeltaRestarts,
            ConflictStrategy = b.ConflictStrategy
        };

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var clientId = await unitOfWork.SystemOptions.GetValueAsync(OutlookOptionKeys.ClientId);
            if (!string.IsNullOrWhiteSpace(clientId))
                result.ClientId = clientId;

            var clientSecretCipher = await unitOfWork.SystemOptions.GetValueAsync(OutlookOptionKeys.ClientSecret);
            if (!string.IsNullOrWhiteSpace(clientSecretCipher))
            {
                // 解密失败返回 null，避免把密文当作有效凭据使用（OAuth 调用必失败）
                result.ClientSecret = DecryptIfEncrypted(clientSecretCipher) ?? string.Empty;
            }

            var tenantId = await unitOfWork.SystemOptions.GetValueAsync(OutlookOptionKeys.TenantId);
            if (!string.IsNullOrWhiteSpace(tenantId))
                result.TenantId = tenantId;

            var redirectUri = await unitOfWork.SystemOptions.GetValueAsync(OutlookOptionKeys.RedirectUri);
            if (!string.IsNullOrWhiteSpace(redirectUri))
                result.RedirectUri = redirectUri;

            var scopes = await unitOfWork.SystemOptions.GetValueAsync(OutlookOptionKeys.Scopes);
            if (!string.IsNullOrWhiteSpace(scopes))
                result.Scopes = scopes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载 Outlook 同步配置从数据库失败，使用基线配置");
        }

        return result;
    }

    /// <summary>
    /// 解密 ClientSecret。支持两种格式：
    /// 1. ENC: 前缀格式（与 DingDing/WeChat/Feishu 一致）
    /// 2. 无前缀纯密文格式（尝试解密，失败则视为历史明文）
    /// 解密失败时返回 null，避免将密文原文当作有效配置使用。
    /// </summary>
    private string? DecryptIfEncrypted(string storedValue)
    {
        const string encPrefix = "ENC:";

        // ENC: 前缀格式
        if (storedValue.StartsWith(encPrefix, StringComparison.Ordinal))
        {
            var cipherText = storedValue[encPrefix.Length..];
            if (_encryptionService.TryDecrypt(cipherText, out var plain) && !string.IsNullOrEmpty(plain))
            {
                return plain;
            }
            _logger.LogError("Outlook ClientSecret 解密失败（ENC 前缀格式）：Encryption:Key 可能已变更。请重新配置");
            return null;
        }

        // 无前缀：尝试直接解密，失败则视为历史明文
        if (_encryptionService.TryDecrypt(storedValue, out var plainText) && !string.IsNullOrEmpty(plainText))
        {
            return plainText;
        }

        // 看起来像 base64 密文（长度 ≥ 24）但解密失败，视为损坏密文
        if (storedValue.Length >= 24 && IsLikelyBase64(storedValue))
        {
            _logger.LogError("Outlook ClientSecret 解密失败：Encryption:Key 可能已变更。请重新配置 ClientSecret");
            return null;
        }

        // 明显不是密文（太短或非 base64），视为历史明文（向后兼容）
        return storedValue;
    }

    private static bool IsLikelyBase64(string value)
    {
        return value.All(c => char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=');
    }
}
