using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Api.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Hosting;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 认证控制器 - 处理用户登录、登出等身份认证相关操作
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILocalizationService _localizer;
    private readonly ICaptchaService _captchaService;
    private readonly IPasswordPolicyService _passwordPolicyService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly IAuditService _auditService;

    public AuthController(
        IAuthService authService,
        ILocalizationService localizer,
        ICaptchaService captchaService,
        IPasswordPolicyService passwordPolicyService,
        IConfiguration configuration,
        IWebHostEnvironment env,
        IAuditService auditService)
    {
        _authService = authService;
        _localizer = localizer;
        _captchaService = captchaService;
        _passwordPolicyService = passwordPolicyService;
        _configuration = configuration;
        _env = env;
        _auditService = auditService;
    }

    /// <summary>
    /// Cookie 是否启用 Secure 标志。
    /// 默认：开发环境 false，生产环境 true。
    /// 当生产环境通过 HTTP（非 HTTPS）部署时，需在配置中设置 Cookie:Secure=false，
    /// 否则浏览器不会保存 Secure Cookie，导致登录后立即跳回登录页。
    /// </summary>
    private bool CookieSecure =>
        _configuration.GetValue<bool?>("Cookie:Secure") ?? !_env.IsDevelopment();

    /// <summary>
    /// Cookie SameSite 策略。
    /// 默认 Lax：防 CSRF 且允许顶级导航携带 Cookie（跨子域跳转场景友好）。
    /// 可通过 Cookie:SameSite 配置覆盖（Strict/Lax/None）。
    /// </summary>
    private SameSiteMode CookieSameSite =>
        _configuration.GetValue<string>("Cookie:SameSite")?.ToLowerInvariant() switch
        {
            "strict" => SameSiteMode.Strict,
            "none" => SameSiteMode.None,
            _ => SameSiteMode.Lax
        };

    /// <summary>
    /// 将 JWT Token 写入 HttpOnly Cookie，防止 XSS 攻击窃取令牌
    /// </summary>
    private void SetAccessTokenCookie(string token)
    {
        // 使用 GetValue<double?> 容忍配置值非法（空字符串、非数字），避免运行时 FormatException
        var expireMinutes = _configuration.GetValue<double?>("Jwt:AccessTokenExpireMinutes") ?? 60;
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = CookieSecure,
            SameSite = CookieSameSite,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddMinutes(expireMinutes)
        };
        Response.Cookies.Append("access_token", token, cookieOptions);
    }

    /// <summary>
    /// 清除 HttpOnly Cookie 中的访问令牌
    /// </summary>
    private void ClearAccessTokenCookie()
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = CookieSecure,
            SameSite = CookieSameSite,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(-1)
        };
        Response.Cookies.Delete("access_token", cookieOptions);
    }

    /// <summary>
    /// 将 Refresh Token 写入 HttpOnly Cookie
    /// </summary>
    private void SetRefreshTokenCookie(string token)
    {
        var expireDays = _configuration.GetValue<double?>("Jwt:RefreshTokenExpireDays") ?? 7;
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = CookieSecure,
            SameSite = CookieSameSite,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(expireDays)
        };
        Response.Cookies.Append("refresh_token", token, cookieOptions);
    }

    /// <summary>
    /// 清除 HttpOnly Cookie 中的 Refresh Token
    /// </summary>
    private void ClearRefreshTokenCookie()
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = CookieSecure,
            SameSite = CookieSameSite,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(-1)
        };
        Response.Cookies.Delete("refresh_token", cookieOptions);
    }

    /// <summary>
    /// 设置 XSRF-TOKEN Cookie（双提交 Cookie 模式防 CSRF）
    /// </summary>
    private void SetXsrfTokenCookie()
    {
        var token = Guid.NewGuid().ToString("N");
        var cookieOptions = new CookieOptions
        {
            HttpOnly = false,
            Secure = CookieSecure,
            SameSite = CookieSameSite,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddMinutes(_configuration.GetValue<double?>("Jwt:AccessTokenExpireMinutes") ?? 60)
        };
        Response.Cookies.Append("XSRF-TOKEN", token, cookieOptions);
    }

    /// <summary>
    /// 清除 XSRF-TOKEN Cookie
    /// </summary>
    private void ClearXsrfTokenCookie()
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = false,
            Secure = CookieSecure,
            SameSite = CookieSameSite,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(-1)
        };
        Response.Cookies.Delete("XSRF-TOKEN", cookieOptions);
    }

    /// <summary>
    /// 获取当前密码策略
    /// </summary>
    /// <remarks>返回当前系统配置的密码策略，用于前端在注册、重置密码等场景展示密码要求。该接口无需认证。</remarks>
    /// <returns>密码策略</returns>
    [HttpGet("password-policy")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPasswordPolicy()
    {
        var policy = await _passwordPolicyService.GetPolicyAsync();
        return Ok(policy);
    }

    /// <summary>
    /// 获取公开注册配置
    /// </summary>
    /// <remarks>返回是否开放用户注册等公开配置，用于登录页和注册页判断是否显示注册入口。该接口无需认证。</remarks>
    /// <returns>公开注册配置</returns>
    [HttpGet("public-config")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicConfig()
    {
        var allowRegistrationStr = await _authService.GetAllowRegistrationAsync();
        return Ok(new { allowRegistration = allowRegistrationStr });
    }

    /// <summary>
    /// 获取图形验证码
    /// </summary>
    /// <remarks>生成 SVG 格式的图形验证码，返回验证码ID和 SVG 图片内容。验证码5分钟内有效，验证一次后失效。</remarks>
    /// <returns>验证码ID和SVG图片内容</returns>
    /// <response code="200">获取成功</response>
    [HttpGet("captcha")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCaptcha()
    {
        var (captchaId, svgContent) = await _captchaService.GenerateAsync(130, 44, 4);
        return Ok(new { captchaId, svgContent });
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    /// <remarks>
    /// 使用邮箱和密码登录系统，成功后返回 JWT 令牌。
    /// </remarks>
    /// <param name="request">登录请求</param>
    /// <returns>登录结果，包含 JWT 令牌和用户信息</returns>
    /// <response code="200">登录成功</response>
    /// <response code="401">邮箱或密码错误</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // [ApiController] 已自动基于 LoginRequest 的 DataAnnotations 进行 ModelState 校验，
        // 无效请求会在进入 action 前被框架自动拒绝并返回 400。

        var ipAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var userAgent = Request.Headers["User-Agent"].ToString() ?? string.Empty;
        // 界面语言：优先取请求体里的 LoginRequest.Locale，其次取请求文化（?lang= / Cookie / Accept-Language），
        // 由 AuthService 发布 UserLoggedInEvent，RememberUserLocaleListener 回写 Users.Locale。
        var locale = request.Locale ?? ApiRequestCulture.Of(HttpContext);
        var response = await _authService.LoginAsync(request, ipAddress, userAgent, locale);

        if (!response.Success)
        {
            return Unauthorized(new { message = response.ErrorMessage ?? _localizer["LoginFailed"] });
        }

        // 登录成功：写审计日志（userId 显式传入，保证 AuditLog.UserId 正确关联，
        // 不要依赖 HttpContext.User - 在登录响应返回前 JWT Cookie 尚未建立，
        // 若走 AuditActionFilter 会得到 UserId=null，导致"登录历史"永远查不到）。
        try
        {
            await _auditService.LogAsync(
                action: "login",
                entityType: "Auth",
                entityId: response.User?.Id,
                cancellationToken: HttpContext.RequestAborted,
                userId: response.User?.Id,
                ipAddress: ipAddress,
                userAgent: userAgent);
        }
        catch (Exception ex)
        {
            // 审计写入失败不应阻塞登录响应，仅通过 Serilog 记录以便排错
            var logger = HttpContext.RequestServices.GetService<ILogger<AuthController>>();
            logger?.LogError(ex, "登录成功，但写入 audit-log(login) 失败：UserId={UserId}, Ip={Ip}",
                response.User?.Id, ipAddress);
        }

        // 将 JWT Token 写入 HttpOnly Cookie，防止 XSS 攻击窃取
        // 同时在响应体中返回 token，保持向后兼容（支持 Authorization header 方式）
        SetAccessTokenCookie(response.Token);

        // 将 Refresh Token 写入 HttpOnly Cookie
        if (!string.IsNullOrEmpty(response.RefreshToken))
        {
            SetRefreshTokenCookie(response.RefreshToken);
        }

        // 设置 XSRF-TOKEN Cookie 用于 CSRF 防护
        SetXsrfTokenCookie();

        return Ok(response);
    }

    /// <summary>
    /// 刷新 Access Token
    /// </summary>
    /// <remarks>使用 HttpOnly Cookie 中的 Refresh Token 换取新的 Access Token 与 Refresh Token（轮换）。</remarks>
    /// <returns>新的 Access Token</returns>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [EnableRateLimiting("RefreshTokenPolicy")]
    public async Task<IActionResult> RefreshToken()
    {
        var refreshToken = Request.Cookies["refresh_token"];
        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized(new { message = "Refresh Token 缺失" });
        }

        var ipAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var (success, accessToken, newRefreshToken, errorMessage) = await _authService.RefreshTokenAsync(refreshToken, ipAddress);

        if (!success || string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(newRefreshToken))
        {
            ClearAccessTokenCookie();
            ClearRefreshTokenCookie();
            ClearXsrfTokenCookie();
            return Unauthorized(new { message = errorMessage ?? "Refresh Token 无效或已过期" });
        }

        SetAccessTokenCookie(accessToken);
        SetRefreshTokenCookie(newRefreshToken);
        SetXsrfTokenCookie();

        return Ok(new { token = accessToken });
    }

    /// <summary>
    /// 用户登出
    /// </summary>
    /// <remarks>清除服务端 HttpOnly Cookie 中的访问令牌，同时撤销 Refresh Token。</remarks>
    /// <returns>登出结果</returns>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        // 登出写审计日志：放在撤销 token 之前，拿到当前 HttpContext.User 中的操作人 ID 和 IP/UA
        // （清理 Cookie 不影响 User，不过先写更清晰）。
        // 失败不影响登出主流程。
        try
        {
            var claimUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(claimUserId, out var userId))
            {
                var ipAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
                var userAgent = Request.Headers["User-Agent"].ToString() ?? string.Empty;
                await _auditService.LogAsync(
                    action: "logout",
                    entityType: "Auth",
                    entityId: userId,
                    cancellationToken: HttpContext.RequestAborted,
                    userId: userId,
                    ipAddress: ipAddress,
                    userAgent: userAgent);
            }
        }
        catch (Exception ex)
        {
            var logger = HttpContext.RequestServices.GetService<ILogger<AuthController>>();
            logger?.LogError(ex, "登出前写入 audit-log(logout) 失败");
        }

        var refreshToken = Request.Cookies["refresh_token"];
        if (!string.IsNullOrEmpty(refreshToken))
        {
            await _authService.RevokeRefreshTokenAsync(refreshToken);
        }

        ClearAccessTokenCookie();
        ClearRefreshTokenCookie();
        ClearXsrfTokenCookie();
        return Ok(new { message = _localizer["LogoutSuccess"] });
    }

    /// <summary>
    /// 用户注册
    /// </summary>
    /// <remarks>
    /// 新用户注册，成功后返回 JWT 令牌和用户信息。
    /// 注册用户默认角色为普通用户，需管理员授权后才能访问完整功能。
    /// </remarks>
    /// <param name="request">注册请求</param>
    /// <returns>注册结果，包含 JWT 令牌和用户信息</returns>
    /// <response code="200">注册成功</response>
    /// <response code="400">参数校验失败或密码不符合策略</response>
    /// <response code="409">邮箱已被注册</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("RegisterPolicy")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var response = await _authService.RegisterAsync(request);

        if (!response.Success)
        {
            if (response.ErrorMessage == "用户注册功能未开放")
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = response.ErrorMessage });
            }
            if (response.ErrorMessage == "邮箱已被注册")
            {
                return Conflict(new { message = _localizer["EmailAlreadyExists"] });
            }
            return BadRequest(new { message = response.ErrorMessage });
        }

        return Ok(new { message = "注册成功，请等待管理员激活账号后登录", user = response.User });
    }

    /// <summary>
    /// 忘记密码
    /// </summary>
    /// <remarks>验证姓名和邮箱后发送密码重置链接。</remarks>
    /// <param name="request">忘记密码请求</param>
    /// <response code="200">重置链接已发送</response>
    /// <response code="400">参数验证失败或重置失败</response>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("ForgotPasswordPolicy")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var (success, errorMessage) = await _authService.ForgotPasswordAsync(request);

        if (!success)
        {
            return BadRequest(new { message = errorMessage });
        }

        return Ok(new { message = "密码重置链接已发送到您的邮箱" });
    }

    /// <summary>
    /// 重置密码
    /// </summary>
    /// <remarks>使用令牌重置密码。</remarks>
    /// <param name="request">重置密码请求</param>
    /// <response code="200">密码重置成功</response>
    /// <response code="400">参数验证失败或令牌无效或密码不符合策略</response>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("ResetPasswordPolicy")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var (success, errorMessage) = await _authService.ResetPasswordAsync(request);

        if (!success)
        {
            return BadRequest(new { message = errorMessage });
        }

        return Ok(new { message = "密码重置成功" });
    }
}
