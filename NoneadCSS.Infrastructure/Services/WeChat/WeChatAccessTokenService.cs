using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Services;

namespace NoneadCSS.Infrastructure.Services.WeChat;

/// <summary>
/// 微信 access_token 管理服务 - 使用稳定版 stable_token API + 互斥锁 + 提前刷新。
/// 稳定版接口不会因重复调用而互相失效，更适合多实例部署。
/// access_token 有效期 7200 秒，缓存提前 5 分钟过期以留出刷新窗口。
/// 使用 SemaphoreSlim 防止多请求并发刷新导致互相失效。
/// </summary>
public class WeChatAccessTokenService : IWeChatAccessTokenService
{
    private readonly HttpClient _httpClient;
    private readonly IWeChatSettingsProvider _settingsProvider;
    private readonly IEncryptionService _encryptionService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WeChatAccessTokenService> _logger;

    private const string CacheKey = "WeChat:AccessToken";
    private const string StableTokenPath = "cgi-bin/stable_token";
    private const string DirectApiBase = "https://api.weixin.qq.com";
    /// <summary>提前刷新的缓冲时间（5 分钟），避免使用即将过期的 token</summary>
    private readonly TimeSpan _refreshBuffer;

    /// <summary>互斥锁，防止并发刷新 access_token（微信限制每天获取次数）</summary>
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public WeChatAccessTokenService(
        HttpClient httpClient,
        IOptions<HttpClientOptions> httpClientOptions,
        IOptions<TokenRefreshOptions> tokenRefreshOptions,
        IWeChatSettingsProvider settingsProvider,
        IEncryptionService encryptionService,
        IMemoryCache cache,
        ILogger<WeChatAccessTokenService> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(httpClientOptions.Value.TokenServiceTimeoutSeconds);
        _refreshBuffer = TimeSpan.FromMinutes(tokenRefreshOptions.Value.RefreshBufferMinutes);
        _settingsProvider = settingsProvider;
        _encryptionService = encryptionService;
        _cache = cache;
        _logger = logger;
    }

    private WeChatOptions Options => _settingsProvider.Current;

    public bool IsConfigured => Options.IsConfigured;

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        // 从缓存读取，未命中或已过期时刷新
        if (_cache.TryGetValue<WeChatTokenCache>(CacheKey, out var cached) && cached != null)
        {
            if (cached.ExpiresAt > DateTimeOffset.UtcNow.Add(_refreshBuffer))
            {
                return cached.Token;
            }
            // 即将过期，触发刷新
        }

        return await RefreshAccessTokenAsync(cancellationToken);
    }

    public async Task<string?> RefreshAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        bool lockAcquired = false;
        try
        {
            await _refreshLock.WaitAsync(cancellationToken);
            lockAcquired = true;
            // ↓ 双重检查：获取锁后再次确认缓存是否已被其他请求刷新
            if (_cache.TryGetValue<WeChatTokenCache>(CacheKey, out var cached) && cached != null)
            {
                if (cached.ExpiresAt > DateTimeOffset.UtcNow.Add(_refreshBuffer))
                {
                    return cached.Token;
                }
            }

            // 解密 AppSecret
            var appSecret = DecryptSecret(Options.AppSecret);
            if (string.IsNullOrEmpty(appSecret))
            {
                _logger.LogError("微信 AppSecret 解密失败或为空");
                return null;
            }

            // 使用稳定版 stable_token API（POST JSON，不会因重复调用互相失效）
            var payload = new
            {
                grant_type = "client_credential",
                appid = Options.AppId,
                secret = appSecret,
                force_refresh = false
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // 稳定版 token 接口固定直连微信公众平台官方地址
            var stableTokenUrl = $"{DirectApiBase}/{StableTokenPath.TrimStart('/')}";
            using var response = await _httpClient.PostAsync(stableTokenUrl, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            // Debug 日志：脱敏（删除 responseBody 中可能包含的 access_token / expires_in 字段，防止 token 片段泄漏）
            _logger.LogDebug(
                "微信 access_token 请求详情：URL={Url}, AppId={AppId}, StatusCode={StatusCode}",
                stableTokenUrl, Options.AppId, (int)response.StatusCode);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                // 截断防止日志过长
                var truncatedBody = responseBody.Length > 1000
                    ? responseBody[..1000] + "...(截断)"
                    : responseBody;
                _logger.LogError(
                    "微信 access_token HTTP 失败：StatusCode={StatusCode}, Body={Body}",
                    (int)response.StatusCode, truncatedBody);
                return null;
            }

            JsonElement result;
            try
            {
                result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "微信 access_token 响应解析失败：ErrorType={ErrorType}", "AccessTokenParseError");
                return null;
            }

            if (result.TryGetProperty("access_token", out var tokenProp) && tokenProp.ValueKind == JsonValueKind.String)
            {
                var token = tokenProp.GetString()!;
                var expiresIn = result.TryGetProperty("expires_in", out var expProp) ? expProp.GetInt32() : 7200;

                var cacheEntry = new WeChatTokenCache
                {
                    Token = token,
                    ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn)
                };

                // 缓存到 token 实际过期时间（不提前，GetAccessTokenAsync 会在 _refreshBuffer 前主动刷新）
                _cache.Set(CacheKey, cacheEntry, TimeSpan.FromSeconds(expiresIn));

                _logger.LogInformation("微信 access_token 刷新成功（stable_token），有效期 {ExpiresIn} 秒", expiresIn);
                return token;
            }

            // 错误响应
            var errcode = result.TryGetProperty("errcode", out var ec) && ec.ValueKind == JsonValueKind.Number ? ec.GetInt32() : 0;
            var errmsg = result.TryGetProperty("errmsg", out var em) ? em.GetString() : "未知错误";
            _logger.LogError(
                "微信 access_token 获取失败：errcode={ErrCode}, errmsg={ErrMsg}, URL={Url}",
                errcode, errmsg, stableTokenUrl);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "微信 access_token 获取异常");
            return null;
        }
        finally
        {
            // 只在成功进入锁临界区时释放。
            // 若 WaitAsync 被 OperationCanceledException/其他异常中断（未获得锁），直接 Release 会
            // 抛出 SemaphoreFullException 或让锁计数错误，导致后续所有 RefreshAccessTokenAsync 永远阻塞。
            if (lockAcquired)
            {
                try { _refreshLock.Release(); }
                catch (SemaphoreFullException ex)
                {
                    _logger.LogWarning(ex, "微信 Token 刷新互斥锁状态异常（Release 时计数 > 上限），已忽略，避免阻塞后续刷新。");
                }
            }
        }
    }

    /// <summary>
    /// 解密 AppSecret。如果密文无法解密（可能是明文配置），则直接返回原值。
    /// </summary>
    private string? DecryptSecret(string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return null;
        }

        // 优先尝试解密（密文存储场景）
        if (_encryptionService.TryDecrypt(secret, out var plain) && !string.IsNullOrEmpty(plain))
        {
            return plain;
        }

        // 降级：可能是明文配置（开发环境），直接返回
        return secret;
    }

    private sealed class WeChatTokenCache
    {
        public string Token { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
