using System.Security.Claims;
using System.Security.Cryptography;
using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Api.Controllers;

[ApiController]
[Route("api/oauth")]
[Authorize]
[RequireModule("core")]
public class OauthController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<OauthController> _logger;
    private const string OAuthStateCookieName = "oauth_state";

    public OauthController(IUnitOfWork unitOfWork, ILogger<OauthController> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    [HttpGet("mailbox/{mailboxId}/authorize")]
    [RequireSuperAdmin]
    public async Task<IActionResult> AuthorizeMailbox(int mailboxId, [FromQuery] string provider)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(mailboxId);
        if (mailbox == null)
        {
            return NotFound(new { message = "邮箱不存在" });
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            return BadRequest(new { message = "provider 不能为空" });
        }

        // 当前登录用户，绑定到 state 中防止其他用户重放
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        if (userId <= 0)
        {
            return Unauthorized(new { message = "无法识别当前用户" });
        }

        // 生成「上下文绑定」的加密随机 state：格式 = userId:mailboxId:provider:expiryUnix:32bytes_random_b64url
        // 包含 userId（用户绑定）+ expiryUnix（有效期）+ nonce，防止 state 跨用户/跨时间/跨 mailbox / 跨 provider 被复用
        var state = GenerateState(userId, mailboxId, provider.Trim().ToLowerInvariant());
        Response.Cookies.Append(OAuthStateCookieName, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = !Request.IsHttps == false, // 开发 HTTP 环境自动降级；生产 HTTPS 保持 Secure
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(10),
            Path = "/api/oauth",
            IsEssential = true
        });

        if (string.Equals(provider, "google", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                provider = "google",
                authorizeUrl = "https://accounts.google.com/o/oauth2/v2/auth",
                scopes = new[] { "https://mail.google.com/", "email", "profile" },
                state = state,
                note = "请使用OAuth授权码登录，然后将授权码输入到邮箱配置中"
            });
        }

        if (string.Equals(provider, "microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                provider = "microsoft",
                authorizeUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
                scopes = new[] { "https://outlook.office.com/IMAP.AccessAsUser.All", "https://outlook.office.com/SMTP.Send", "email", "profile", "openid" },
                state = state,
                note = "请使用OAuth授权码登录，然后将授权码输入到邮箱配置中"
            });
        }

        if (string.Equals(provider, "qq", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                provider = "qq",
                authorizeUrl = "https://graph.qq.com/oauth2.0/authorize",
                scopes = new[] { "email" },
                state = state,
                note = "QQ邮箱建议使用授权码方式，而非OAuth"
            });
        }

        return BadRequest(new { message = "不支持的OAuth提供商" });
    }

    /// <summary>
    /// 生成「上下文绑定」的加密随机 state
    /// 格式 = {userId}:{mailboxId}:{providerLower}:{expiryUnixSeconds}:{32bytes_random_b64url}
    /// 绑定 userId + mailboxId + provider + expiry，防止 state 跨用户/跨邮箱/跨提供方/过期重放
    /// </summary>
    private static string GenerateState(int userId, int mailboxId, string providerLower)
    {
        var nonce = RandomNumberGenerator.GetBytes(32);
        var nonceB64 = Convert.ToBase64String(nonce)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var expiryUnix = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        return $"{userId}:{mailboxId}:{providerLower}:{expiryUnix}:{nonceB64}";
    }

    /// <summary>
    /// 校验 state 的上下文绑定：除了 cookie 与请求值 FixedTimeEquals 外，
    /// 还要求 userId、mailboxId、provider、expiry 与 state 内绑定的值完全一致
    /// 返回 (isValid: bool, parsedProvider: string?)
    /// </summary>
    private static (bool isValid, string? parsedProvider) ValidateStateBinding(string? cookieState, string requestState, int expectedUserId, int expectedMailboxId, string? expectedProviderLower)
    {
        if (string.IsNullOrEmpty(cookieState) || string.IsNullOrEmpty(requestState))
            return (false, null);

        // 先做恒时比较防时序攻击（比较整串值）
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(cookieState),
                System.Text.Encoding.UTF8.GetBytes(requestState)))
            return (false, null);

        // 解析五段式：userId:mailboxId:provider:expiryUnix:nonce
        var parts = cookieState.Split(':', 5);
        if (parts.Length != 5)
            return (false, null);

        if (!int.TryParse(parts[0], out var boundUserId) || boundUserId != expectedUserId || expectedUserId <= 0)
            return (false, null);

        if (!int.TryParse(parts[1], out var boundMailboxId) || boundMailboxId != expectedMailboxId)
            return (false, null);

        var boundProvider = parts[2];
        if (!string.IsNullOrEmpty(expectedProviderLower) && !string.Equals(boundProvider, expectedProviderLower, StringComparison.Ordinal))
            return (false, null);

        if (!long.TryParse(parts[3], out var boundExpiry) || boundExpiry < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return (false, null);

        return (true, boundProvider);
    }

    [HttpPost("mailbox/{mailboxId}/token")]
    [RequireSuperAdmin]
    public async Task<IActionResult> SaveOAuthToken(int mailboxId, [FromBody] OAuthTokenRequest request)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetQueryable(false).FirstOrDefaultAsync(m => m.Id == mailboxId);
        if (mailbox == null)
        {
            return NotFound(new { message = "邮箱不存在" });
        }

        // 当前登录用户，校验 state 的用户绑定
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        if (userId <= 0)
        {
            return Unauthorized(new { message = "无法识别当前用户" });
        }

        // 三重校验：1) cookie 与提交值恒时相等；2) state 中绑定的 userId/mailboxId/provider/expiry 与当前请求一致
        if (string.IsNullOrEmpty(request.State))
        {
            return BadRequest(new { message = "缺少 state 参数" });
        }
        var cookieState = Request.Cookies[OAuthStateCookieName];
        var providerLower = string.IsNullOrWhiteSpace(request.Provider) ? null : request.Provider.Trim().ToLowerInvariant();
        var (stateValid, boundProvider) = ValidateStateBinding(cookieState, request.State, userId, mailboxId, providerLower);
        if (!stateValid)
        {
            _logger.LogWarning(
                "OAuth state 校验失败（上下文不匹配或值不一致）：MailboxId={MailboxId}, ExpectedProvider={Provider}, BoundProvider={BoundProvider}",
                mailboxId, providerLower, boundProvider);
            return BadRequest(new { message = "state 校验失败或与邮箱/提供方不匹配，请重新发起授权" });
        }

        // 一次性使用：清除 state cookie
        Response.Cookies.Delete(OAuthStateCookieName, new CookieOptions { Path = "/api/oauth", Secure = !Request.IsHttps == false });

        mailbox.OauthEnabled = true;
        mailbox.OauthProvider = request.Provider;
        mailbox.OauthAccessToken = request.AccessToken;
        mailbox.OauthRefreshToken = request.RefreshToken ?? string.Empty;
        mailbox.OauthTokenExpiresAt = request.ExpiresAt;
        mailbox.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return Ok(new { message = "OAuth令牌保存成功" });
    }

    [HttpPost("mailbox/{mailboxId}/refresh")]
    [RequireSuperAdmin]
    public async Task<IActionResult> RefreshOAuthToken(int mailboxId)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(mailboxId);
        if (mailbox == null)
        {
            return NotFound(new { message = "邮箱不存在" });
        }

        if (!mailbox.OauthEnabled || string.IsNullOrEmpty(mailbox.OauthRefreshToken))
        {
            return BadRequest(new { message = "邮箱未配置OAuth或没有刷新令牌" });
        }

        return Ok(new
        {
            message = "OAuth令牌刷新功能需要外部OAuth服务支持，当前仅返回提示",
            provider = mailbox.OauthProvider,
            expiresAt = mailbox.OauthTokenExpiresAt
        });
    }

    [HttpDelete("mailbox/{mailboxId}/token")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ClearOAuthToken(int mailboxId)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetQueryable(false).FirstOrDefaultAsync(m => m.Id == mailboxId);
        if (mailbox == null)
        {
            return NotFound(new { message = "邮箱不存在" });
        }

        mailbox.OauthEnabled = false;
        mailbox.OauthProvider = string.Empty;
        mailbox.OauthAccessToken = string.Empty;
        mailbox.OauthRefreshToken = string.Empty;
        mailbox.OauthTokenExpiresAt = null;
        mailbox.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return Ok(new { message = "OAuth配置已清除" });
    }

    [HttpGet("providers")]
    public IActionResult GetSupportedProviders()
    {
        var providers = new[]
        {
            new
            {
                id = "google",
                name = "Google",
                description = "Google邮箱OAuth授权",
                scopes = new[] { "IMAP", "SMTP" },
                requiresAppRegistration = true,
                note = "需要在Google Cloud Console创建OAuth应用"
            },
            new
            {
                id = "microsoft",
                name = "Microsoft",
                description = "Microsoft Outlook/Office365邮箱OAuth授权",
                scopes = new[] { "IMAP", "SMTP" },
                requiresAppRegistration = true,
                note = "需要在Azure Active Directory创建应用注册"
            },
            new
            {
                id = "qq",
                name = "QQ邮箱",
                description = "QQ邮箱授权码方式",
                scopes = new[] { "IMAP", "SMTP" },
                requiresAppRegistration = false,
                note = "QQ邮箱建议使用授权码，在邮箱设置中开启POP3/SMTP并获取授权码"
            },
            new
            {
                id = "163",
                name = "网易163邮箱",
                description = "网易163邮箱授权码方式",
                scopes = new[] { "IMAP", "SMTP" },
                requiresAppRegistration = false,
                note = "在邮箱设置中开启授权码，获取后作为密码使用"
            },
            new
            {
                id = "gmail",
                name = "Gmail",
                description = "Gmail邮箱OAuth授权",
                scopes = new[] { "IMAP", "SMTP" },
                requiresAppRegistration = true,
                note = "同Google OAuth，需要在Google Cloud Console创建应用"
            }
        };

        return Ok(providers);
    }
}
