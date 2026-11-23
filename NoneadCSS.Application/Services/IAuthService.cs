using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IAuthService
{
    /// <summary>
    /// 登录。失败计数与锁定按 Security:LoginLockout 配置维护；各失败分支发布 LoginFailedEvent / LockoutEvent，
    /// 成功后发布 UserLoggedInEvent（供 RememberUserLocaleListener 回写 Users.Locale）。
    /// </summary>
    /// <param name="request">登录请求，可携带 Locale。</param>
    /// <param name="ipAddress">客户端 IP。</param>
    /// <param name="userAgent">客户端 User-Agent，可为空。</param>
    /// <param name="locale">控制器端从请求文化解析出的界面语言，请求体未带 Locale 时使用，可为空。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>登录结果；失败时 Success=false 且返回统一的错误消息。</returns>
    Task<LoginResponse> LoginAsync(LoginRequest request, string ipAddress, string? userAgent = null, string? locale = null, CancellationToken cancellationToken = default);
    Task<bool> ValidateTokenAsync(string token);
    Task<LoginResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<(bool Success, string ErrorMessage)> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task<(bool Success, string ErrorMessage)> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
    Task<bool> GetAllowRegistrationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用 Refresh Token 轮换生成新的 Access Token 与 Refresh Token。
    /// </summary>
    Task<(bool Success, string? AccessToken, string? RefreshToken, string? ErrorMessage)> RefreshTokenAsync(string refreshToken, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤销指定的 Refresh Token（登出时使用）。
    /// </summary>
    Task<bool> RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
}
