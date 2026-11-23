using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Services;

namespace NoneadCSS.Infrastructure.Services.Outlook;

/// <summary>
/// Outlook OAuth 认证服务 - 实现 Azure AD v2.0 端点的授权码流程与令牌刷新
/// T000099: Azure AD 凭据从 IOutlookSettingsProvider 读取（合并 appsettings 基线 + SystemOptions 数据库覆盖），
/// 支持管理后台 UI 配置（OutlookSyncController.GetAdminSettings / UpdateAdminSettings）。
/// </summary>
public class OutlookOAuthService : IOutlookOAuthService
{
    // C-O3: 通过 IHttpClientFactory 注入，避免 static HttpClient 的 DNS 过期和连接耗尽问题
    private readonly HttpClient _httpClient;

    private readonly IOutlookSettingsProvider _settingsProvider;
    private readonly IEncryptionService _encryptionService;
    private readonly IDistributedCache _cache;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ILogger<OutlookOAuthService> _logger;

    // state 有效期 10 分钟
    private const int StateTtlMinutes = 10;
    // 域分隔符，确保 state 与其他用途的加密负载不可互换
    private const string StateDomain = "outlook-oauth-state:v1";

    public OutlookOAuthService(
        HttpClient httpClient,
        IOutlookSettingsProvider settingsProvider,
        IOptions<HttpClientOptions> httpClientOptions,
        IEncryptionService encryptionService,
        IDistributedCache cache,
        IDistributedLockService distributedLockService,
        ILogger<OutlookOAuthService> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(httpClientOptions.Value.StandardTimeoutSeconds);
        _settingsProvider = settingsProvider;
        _encryptionService = encryptionService;
        _cache = cache;
        _distributedLockService = distributedLockService;
        _logger = logger;
    }

    private static string FormatConfigHelp()
    {
        var nl = Environment.NewLine;
        return
            "请联系系统管理员在「日历 → Outlook 同步」面板中点击「配置 Azure AD 凭据」按钮完成配置，" + nl +
            "或在 appsettings.json 的 AzureAd 段配置，或通过环境变量 AzureAd__ClientId / AzureAd__ClientSecret / AzureAd__RedirectUri 设置。" + nl +
            "推荐示例配置（与 Azure 门户中「应用注册 → Web → 重定向 URI」保持一致）：" + nl +
            "  {" + nl +
            "    \"AzureAd\": {" + nl +
            "      \"ClientId\":     \"<Azure AD 应用程序(客户端) ID>\"," + nl +
            "      \"ClientSecret\": \"<客户端密码值>\"," + nl +
            "      \"TenantId\":     \"common | <目录(租户) ID>\"," + nl +
            "      \"RedirectUri\":  \"https://<你的域名>/api/outlooksync/oauth/callback\"" + nl +
            "    }" + nl +
            "  }";
    }

    /// <summary>
    /// 启动时 / 首次使用前校验是否配置了 Azure AD 凭据。
    /// 配置缺失时立即抛出可读错误，而不是等到 OAuth 交换失败才在 Graph SDK 深处抛异常。
    /// T000099: 凭据来源改为 IOutlookSettingsProvider.GetForUserAsync(userId)（用户级 > 全局基线），
    /// 支持每个用户自配置 Azure AD 应用注册凭据。
    /// </summary>
    private void EnsureConfigured(OutlookSyncOptions options)
    {
        if (options.IsConfigured) return;

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ClientId))     missing.Add(nameof(options.ClientId));
        if (string.IsNullOrWhiteSpace(options.ClientSecret)) missing.Add(nameof(options.ClientSecret));
        if (string.IsNullOrWhiteSpace(options.RedirectUri))  missing.Add(nameof(options.RedirectUri));

        var help = FormatConfigHelp();
        _logger.LogError("Outlook 同步未配置 Azure AD 凭据：缺少字段 {MissingFields}。{Help}",
            string.Join(", ", missing), help);
        throw new InvalidOperationException(
            $"Outlook 同步未配置 Azure AD 凭据：缺少字段 {string.Join(", ", missing)}。{help}");
    }

    /// <inheritdoc />
    public async Task<(string authorizeUrl, string state)> GetAuthorizeUrlAsync(int userId)
    {
        // T000099: 按 userId 读取用户级凭据（用户级 > 全局基线）
        var options = await _settingsProvider.GetForUserAsync(userId);
        EnsureConfigured(options);

        var state = BuildState(userId);

        var authorizeUrl = string.Format(options.AuthorizeUrl, options.TenantId);
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = options.ClientId;
        query["response_type"] = "code";
        query["redirect_uri"] = options.RedirectUri;
        query["response_mode"] = "query";
        query["scope"] = options.Scopes;
        query["state"] = state;
        return ($"{authorizeUrl}?{query}", state);
    }

    /// <inheritdoc />
    public async Task<bool> ValidateStateAsync(string state, int expectedUserId)
    {
        var payload = await DecryptStateAsync(state);
        if (payload == null) return false;

        if (payload.UserId != expectedUserId)
        {
            _logger.LogWarning("Outlook OAuth state userId 不匹配：预期 {Expected}，实际 {Actual}", expectedUserId, payload.UserId);
            return false;
        }

        if (DateTime.UtcNow > payload.ExpiresAt)
        {
            _logger.LogWarning("Outlook OAuth state 已过期：过期时间 {Expiry}", payload.ExpiresAt);
            return false;
        }

        // H-O2: 通过分布式缓存原子标记 nonce 已使用（防重放）
        if (!await TryClaimNonceAsync(payload.Nonce))
        {
            _logger.LogWarning("Outlook OAuth state 重放攻击检测：nonce {Nonce} 已使用", payload.Nonce);
            return false;
        }
        return true;
    }

    /// <inheritdoc />
    public async Task<(bool success, int userId)> TryParseStateAsync(string state)
    {
        var payload = await DecryptStateAsync(state);
        if (payload == null) return (false, 0);

        if (DateTime.UtcNow > payload.ExpiresAt)
        {
            _logger.LogWarning("Outlook OAuth state 已过期：过期时间 {Expiry}", payload.ExpiresAt);
            return (false, 0);
        }

        // H-O2: 通过分布式缓存原子标记 nonce 已使用（防重放）
        if (!await TryClaimNonceAsync(payload.Nonce))
        {
            _logger.LogWarning("Outlook OAuth state 重放攻击检测：nonce {Nonce} 已使用", payload.Nonce);
            return (false, 0);
        }
        return (true, payload.UserId);
    }

    /// <inheritdoc />
    public async Task<OutlookTokenResponse> ExchangeCodeAsync(string code, int userId)
    {
        // T000099: 按 userId 读取用户级凭据（用户级 > 全局基线）
        var options = await _settingsProvider.GetForUserAsync(userId);
        var tokenUrl = string.Format(options.TokenUrl, options.TenantId);

        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = options.RedirectUri,
            ["grant_type"] = "authorization_code"
        };

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await _httpClient.PostAsync(tokenUrl, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            // H-34/M-24: 不记录完整响应体（可能含敏感信息），仅提取 error/error_description
            var errorDetail = ExtractOAuthError(responseBody);
            _logger.LogError("Outlook OAuth 授权码换令牌失败：{StatusCode} {Error}", response.StatusCode, errorDetail ?? "未知错误");
            throw new InvalidOperationException($"OAuth 授权码换令牌失败：{response.StatusCode}");
        }

        var token = ParseTokenResponse(responseBody);
        _logger.LogInformation("Outlook OAuth 授权码换令牌成功，过期秒数={ExpiresIn}", token.ExpiresIn);
        return token;
    }

    /// <inheritdoc />
    public async Task<OutlookTokenResponse?> RefreshTokenAsync(string refreshToken, int userId)
    {
        try
        {
            // T000099: 按 userId 读取用户级凭据（用户级 > 全局基线）
            var options = await _settingsProvider.GetForUserAsync(userId);
            var tokenUrl = string.Format(options.TokenUrl, options.TenantId);

            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = options.ClientId,
                ["client_secret"] = options.ClientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token",
                ["scope"] = options.Scopes
            };

            using var content = new FormUrlEncodedContent(parameters);
            using var response = await _httpClient.PostAsync(tokenUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // H-34/M-24: 不记录完整响应体（可能含敏感信息），仅提取 error/error_description
                var errorDetail = ExtractOAuthError(responseBody);
                _logger.LogWarning("Outlook OAuth 刷新令牌失败：{StatusCode} {Error}", response.StatusCode, errorDetail ?? "未知错误");
                return null;
            }

            var token = ParseTokenResponse(responseBody);
            // 若响应未返回新的 refresh_token，沿用旧值（部分租户不返回新 refresh_token）
            token.RefreshToken ??= refreshToken;
            _logger.LogInformation("Outlook OAuth 刷新令牌成功，过期秒数={ExpiresIn}", token.ExpiresIn);
            return token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outlook OAuth 刷新令牌异常");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> EnsureValidAccessTokenAsync(OutlookSyncSetting setting)
    {
        if (string.IsNullOrEmpty(setting.AccessToken) || string.IsNullOrEmpty(setting.RefreshToken))
        {
            _logger.LogWarning("用户 {UserId} 未配置 Outlook 令牌，无法获取有效 Access Token", setting.UserId);
            return null;
        }

        // 令牌未过期，直接返回
        if (!setting.IsTokenExpired)
        {
            return setting.AccessToken;
        }

        _logger.LogInformation("用户 {UserId} 的 Outlook 令牌已过期，尝试刷新", setting.UserId);
        var refreshed = await RefreshTokenAsync(setting.RefreshToken!, setting.UserId);
        if (refreshed == null)
        {
            _logger.LogWarning("用户 {UserId} 的 Outlook 令牌刷新失败", setting.UserId);
            return null;
        }

        // 注意：调用方负责持久化新令牌到数据库
        setting.AccessToken = refreshed.AccessToken;
        setting.RefreshToken = refreshed.RefreshToken ?? setting.RefreshToken;
        setting.TokenExpiresAt = refreshed.ExpiresAt;
        return setting.AccessToken;
    }

    /// <summary>
    /// 构造 state 参数：{userId}|{expiresUtcIso}|{nonce} 加密后 Base64Url 编码
    /// </summary>
    private string BuildState(int userId)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(StateTtlMinutes);
        // C-O1: nonce 熵提升至 128 位（32 个十六进制字符），满足 OAuth state 抗猜测要求
        var nonce = RandomNumberGenerator.GetHexString(32, lowercase: false);
        var payload = $"{StateDomain}|{userId}|{expiresAt:O}|{nonce}";
        var encrypted = _encryptionService.Encrypt(payload);
        return Base64UrlEncode(encrypted);
    }

    /// <summary>
    /// 解密并验证 state，检查 nonce 是否已被使用
    /// </summary>
    private async Task<StatePayload?> DecryptStateAsync(string state)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;
        try
        {
            var encrypted = Base64UrlDecode(state);
            if (!_encryptionService.TryDecrypt(encrypted, out var plain) || string.IsNullOrEmpty(plain))
            {
                return null;
            }

            var parts = plain!.Split('|');
            if (parts.Length != 4) return null;
            if (parts[0] != StateDomain) return null;

            if (!int.TryParse(parts[1], out var userId)) return null;
            if (!DateTime.TryParseExact(parts[2], "O", null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt))
                return null;

            return new StatePayload { UserId = userId, ExpiresAt = expiresAt, Nonce = parts[3] };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "解析 Outlook OAuth state 失败");
            return null;
        }
    }

    /// <summary>
    /// H-O2: 原子标记 nonce 已使用（防重放）。
    /// check-then-act 在并发/多实例下非原子：两个请求可能同时通过"不存在"检查后双双写入，
    /// 导致同一 nonce 被重复使用。这里用 DistributedLockService 对 nonce 加锁后再执行
    /// "检查 + 标记"，保证同实例/跨实例的原子性。
    /// </summary>
    private async Task<bool> TryClaimNonceAsync(string nonce)
    {
        var cacheKey = $"outlook-oauth-nonce:{nonce}";

        // 对 nonce 加分布式锁（等待最多 3 秒，避免并发窗口内瞬时冲突导致误拒）
        var lockKey = $"outlook-oauth-nonce-lock:{nonce}";
        await using var lockHandle = await _distributedLockService.TryAcquireAsync(lockKey, timeoutMs: 3000);
        if (lockHandle == null)
        {
            _logger.LogWarning("Outlook OAuth nonce 加锁失败（锁已被占用或超时），拒绝重放：Nonce={Nonce}", nonce);
            return false;
        }

        // 加锁后再检查是否已被使用
        var existing = await _cache.GetAsync(cacheKey);
        if (existing != null)
        {
            return false; // nonce 已被使用
        }

        // 原子标记为已使用，TTL 为 state 有效期的 2 倍
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(StateTtlMinutes * 2)
        };
        await _cache.SetAsync(cacheKey, Array.Empty<byte>(), options);
        return true;
    }

    /// <summary>
    /// H-34/M-24: 从 OAuth 错误响应中提取 error 与 error_description，避免记录完整响应体（可能含敏感信息）。
    /// </summary>
    private static string? ExtractOAuthError(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            var err = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            var desc = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            if (!string.IsNullOrEmpty(err) || !string.IsNullOrEmpty(desc))
                return $"{err}: {desc}";
        }
        catch
        {
            // 响应体非合法 JSON，忽略
        }
        return null;
    }

    private static OutlookTokenResponse ParseTokenResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errProp))
        {
            var error = errProp.GetString();
            var desc = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            throw new InvalidOperationException($"OAuth 令牌响应错误：{error} - {desc}");
        }

        return new OutlookTokenResponse
        {
            AccessToken = root.GetProperty("access_token").GetString() ?? string.Empty,
            RefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            ExpiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600,
            TokenType = root.TryGetProperty("token_type", out var tt) ? tt.GetString() ?? "Bearer" : "Bearer",
            Scope = root.TryGetProperty("scope", out var sc) ? sc.GetString() ?? string.Empty : string.Empty
        };
    }

    private static string Base64UrlEncode(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        var bytes = Convert.FromBase64String(padded);
        return Encoding.UTF8.GetString(bytes);
    }

    private sealed class StatePayload
    {
        public int UserId { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string Nonce { get; set; } = string.Empty;
    }
}
