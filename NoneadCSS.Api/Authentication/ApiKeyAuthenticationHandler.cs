using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Security;
using NoneadCSS.Domain;

namespace NoneadCSS.Api.Authentication;

/// <summary>
/// ApiKey 认证处理器 - 通过 X-Api-Key 请求头验证 API 密钥
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IUnitOfWork _unitOfWork;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IUnitOfWork unitOfWork)
        : base(options, logger, encoder)
    {
        _unitOfWork = unitOfWork;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 检查请求头中是否包含 API Key
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationOptions.HeaderName, out var apiKeyValues))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKey = apiKeyValues.ToString();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AuthenticateResult.Fail("API Key 不能为空");
        }

        // 数据库仅存储 SHA-256 哈希值，查询时需对明文进行哈希后比对
        var keyHash = SecretHasher.Hash(apiKey);
        var keyEntity = await _unitOfWork.ApiKeys.GetByKeyAsync(keyHash);
        if (keyEntity == null)
        {
            return AuthenticateResult.Fail("无效的 API Key");
        }

        // 检查是否过期
        if (keyEntity.ExpiresAt.HasValue && keyEntity.ExpiresAt.Value < DateTime.UtcNow)
        {
            return AuthenticateResult.Fail("API Key 已过期");
        }

        // 检查是否关联了用户
        if (!keyEntity.UserId.HasValue)
        {
            return AuthenticateResult.Fail("API Key 未关联用户");
        }

        // 检查用户状态是否为激活
        if (keyEntity.User == null || keyEntity.User.Status != Domain.Enums.UserStatus.Active)
        {
            return AuthenticateResult.Fail("用户已被禁用，API Key 无法使用");
        }

        // 更新使用记录 - 使用原子 SQL 增量更新，避免并发计数丢失
        // T11: 由 fire-and-forget Task.Run 改为同步执行（单条原子 UPDATE，代价很小），
        // 避免高 QPS 下无界任务积压与停机丢统计；请求作用域内 _unitOfWork 仍有效
        var apiKeyId = keyEntity.Id;
        var remoteIp = Request.HttpContext.Connection.RemoteIpAddress?.ToString();
        try
        {
            await _unitOfWork.ApiKeys.IncrementUsageAsync(
                apiKeyId,
                DateTime.UtcNow,
                remoteIp,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // 使用记录更新失败不应阻塞认证流程，但记录日志便于排查
            Logger.LogWarning(ex, "更新 API Key 使用记录失败: ApiKeyId={ApiKeyId}", apiKeyId);
        }

        // 构建认证主体
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, keyEntity.UserId.Value.ToString()),
            new("api_key_id", keyEntity.Id.ToString()),
            new("auth_scheme", ApiKeyAuthenticationOptions.Scheme)
        };

        // T4: 最小权限——声明了 scopes 的 API Key 仅在包含 "*" 时继承绑定用户的角色，
        // 否则丢弃 Role claim（scope 即最小权限承诺，不得因绑定超管用户而获得超管能力）；
        // 未声明 scopes 保持向后兼容（旧密钥行为不变）
        var scopes = string.IsNullOrWhiteSpace(keyEntity.Scopes)
            ? Array.Empty<string>()
            : keyEntity.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hasWildcardScope = scopes.Contains("*", StringComparer.OrdinalIgnoreCase);

        // 如果关联了用户角色，添加角色声明
        if (keyEntity.User != null)
        {
            claims.Add(new Claim(ClaimTypes.Name, keyEntity.User.GetFullName()));
            claims.Add(new Claim(ClaimTypes.Email, keyEntity.User.Email));
            if (scopes.Length == 0 || hasWildcardScope)
            {
                claims.Add(new Claim(ClaimTypes.Role, keyEntity.User.Role.ToString("D")));
            }
        }

        // 解析 Scopes 并添加到 Claims
        // 存储格式：逗号分隔字符串（如 "read,write,*"），由 ApiKeyService.CreateAsync/UpdateAsync 写入
        // 必须使用 Split 而非 JsonSerializer.Deserialize，避免 JsonException
        foreach (var scope in scopes)
        {
            if (!string.IsNullOrWhiteSpace(scope))
            {
                claims.Add(new Claim("scope", scope));
            }
        }

        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationOptions.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationOptions.Scheme);

        return AuthenticateResult.Success(ticket);
    }
}

/// <summary>
/// ApiKey 认证选项
/// </summary>
public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
}

/// <summary>
/// ApiKey 认证中间件 - 记录调用日志
/// </summary>
public class ApiKeyLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public ApiKeyLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IUnitOfWork unitOfWork, ILogger<ApiKeyLoggingMiddleware> logger)
    {
        var apiKeyIdClaim = context.User?.FindFirst("api_key_id")?.Value;

        if (int.TryParse(apiKeyIdClaim, out var apiKeyId))
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var endpoint = context.Request.Path.Value ?? string.Empty;
            var method = context.Request.Method;
            var ip = context.Connection.RemoteIpAddress?.ToString();
            var userAgent = context.Request.Headers.UserAgent.ToString();

            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();

                try
                {
                    var log = new NoneadCSS.Domain.Entities.ApiKeyLog
                    {
                        ApiKeyId = apiKeyId,
                        Endpoint = endpoint,
                        Method = method,
                        StatusCode = context.Response.StatusCode,
                        IpAddress = ip,
                        UserAgent = userAgent,
                        DurationMs = (int)stopwatch.ElapsedMilliseconds
                    };

                    await unitOfWork.ApiKeyLogs.AddAsync(log);
                    await unitOfWork.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "写入 API Key 调用日志失败: ApiKeyId={ApiKeyId}", apiKeyId);
                }
            }
        }
        else
        {
            await _next(context);
        }
    }
}
