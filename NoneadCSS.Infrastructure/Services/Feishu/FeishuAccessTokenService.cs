using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Services;

namespace NoneadCSS.Infrastructure.Services.Feishu;

/// <summary>
/// 飞书 tenant_access_token 管理服务 - 调用 /auth/v3/tenant_access_token/internal 获取应用令牌。
/// 使用 IMemoryCache 缓存令牌，提前 5 分钟刷新以留出缓冲窗口。
/// 使用 SemaphoreSlim + 双重检查防止多请求并发刷新。
/// </summary>
public class FeishuAccessTokenService : IFeishuAccessTokenService
{
    private readonly HttpClient _httpClient;
    private readonly IFeishuSettingsProvider _settingsProvider;
    private readonly IEncryptionService _encryptionService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FeishuAccessTokenService> _logger;

    private const string CacheKey = "Feishu:TenantAccessToken";
    private const string TokenUrl = "https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal";
    /// <summary>提前刷新的缓冲时间（5 分钟），避免使用即将过期的 token</summary>
    private readonly TimeSpan _refreshBuffer;

    /// <summary>互斥锁，防止并发刷新 tenant_access_token</summary>
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public FeishuAccessTokenService(
        HttpClient httpClient,
        IOptions<HttpClientOptions> httpClientOptions,
        IOptions<TokenRefreshOptions> tokenRefreshOptions,
        IFeishuSettingsProvider settingsProvider,
        IEncryptionService encryptionService,
        IMemoryCache cache,
        ILogger<FeishuAccessTokenService> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(httpClientOptions.Value.TokenServiceTimeoutSeconds);
        _refreshBuffer = TimeSpan.FromMinutes(tokenRefreshOptions.Value.RefreshBufferMinutes);
        _settingsProvider = settingsProvider;
        _encryptionService = encryptionService;
        _cache = cache;
        _logger = logger;
    }

    private FeishuOptions Options => _settingsProvider.Current;

    public bool IsConfigured => Options.IsConfigured;

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("飞书 GetAccessTokenAsync 跳过：未配置（AppId 或 AppSecret 为空）");
            return null;
        }

        // 从缓存读取，未命中或已过期时刷新
        if (_cache.TryGetValue<FeishuTokenCache>(CacheKey, out var cached) && cached != null)
        {
            if (cached.ExpiresAt > DateTimeOffset.UtcNow.Add(_refreshBuffer))
            {
                _logger.LogDebug("飞书 tenant_access_token 缓存命中：剩余有效期 {Remaining} 秒",
                    (int)(cached.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
                return cached.Token;
            }
            // 即将过期，触发刷新
            _logger.LogDebug("飞书 tenant_access_token 缓存即将过期，触发刷新：ExpiresAt={ExpiresAt:O}",
                cached.ExpiresAt);
        }
        else
        {
            _logger.LogDebug("飞书 tenant_access_token 缓存未命中，触发首次获取");
        }

        return await RefreshAccessTokenAsync(cancellationToken);
    }

    private async Task<string?> RefreshAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("飞书 RefreshAccessTokenAsync 跳过：未配置");
            return null;
        }

        _logger.LogInformation("飞书开始刷新 tenant_access_token：AppId={AppId}, Url={TokenUrl}",
            Options.AppId, TokenUrl);

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // 双重检查：获取锁后再次确认缓存是否已被其他请求刷新
            if (_cache.TryGetValue<FeishuTokenCache>(CacheKey, out var cached) && cached != null)
            {
                if (cached.ExpiresAt > DateTimeOffset.UtcNow.Add(_refreshBuffer))
                {
                    _logger.LogDebug("飞书 tenant_access_token 双重检查命中：其他请求已刷新，剩余 {Remaining} 秒",
                        (int)(cached.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
                    return cached.Token;
                }
            }

            // 解密 AppSecret
            var appSecret = DecryptSecret(Options.AppSecret);
            if (string.IsNullOrEmpty(appSecret))
            {
                _logger.LogError("飞书 AppSecret 解密失败或为空：AppId={AppId}", Options.AppId);
                return null;
            }

            // 调用飞书内部应用获取 tenant_access_token 接口
            var payload = new
            {
                app_id = Options.AppId,
                app_secret = appSecret
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var response = await _httpClient.PostAsync(TokenUrl, content, cancellationToken);
            sw.Stop();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

            _logger.LogInformation("飞书 tenant_access_token HTTP 响应：StatusCode={StatusCode}, 耗时={ElapsedMs}ms",
                (int)response.StatusCode, sw.ElapsedMilliseconds);

            // 响应格式：{ "code": 0, "tenant_access_token": "t-xxx", "expire": 7200 }
            if (result.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.Number && codeProp.GetInt32() == 0)
            {
                if (result.TryGetProperty("tenant_access_token", out var tokenProp) && tokenProp.ValueKind == JsonValueKind.String)
                {
                    var token = tokenProp.GetString()!;
                    var expire = result.TryGetProperty("expire", out var expProp) && expProp.ValueKind == JsonValueKind.Number
                        ? expProp.GetInt32()
                        : 7200;

                    var cacheEntry = new FeishuTokenCache
                    {
                        Token = token,
                        ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expire)
                    };

                    // 缓存到 token 实际过期时间（不提前，GetAccessTokenAsync 会在 _refreshBuffer 前主动刷新）
                    _cache.Set(CacheKey, cacheEntry, TimeSpan.FromSeconds(expire));

                    // 日志中仅记录长度，不记录 token 任何部分，避免泄露
                    _logger.LogInformation("飞书 tenant_access_token 刷新成功：有效期 {Expire} 秒, TokenLength={TokenLength}",
                        expire, token.Length);
                    return token;
                }
            }

            // 错误响应
            var code = codeProp.ValueKind == JsonValueKind.Number ? codeProp.GetInt32() : -1;
            var msg = result.TryGetProperty("msg", out var msgProp) ? msgProp.GetString() : "未知错误";
            _logger.LogError("飞书 tenant_access_token 获取失败：code={Code}, msg={Msg}, AppId={AppId}", code, msg, Options.AppId);
            return null;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning("飞书 tenant_access_token 获取被取消（可能超时）：AppId={AppId}, Cancelled={Cancelled}",
                Options.AppId, ex.CancellationToken.IsCancellationRequested);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "飞书 tenant_access_token HTTP 请求异常：AppId={AppId}, StatusCode={StatusCode}, Message={HttpMessage}",
                Options.AppId, ex.StatusCode, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "飞书 tenant_access_token 获取异常：AppId={AppId}", Options.AppId);
            return null;
        }
        finally
        {
            _refreshLock.Release();
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

    private sealed class FeishuTokenCache
    {
        public string Token { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
