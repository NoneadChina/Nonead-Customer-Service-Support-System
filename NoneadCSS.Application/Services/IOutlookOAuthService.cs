using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

/// <summary>
/// Outlook OAuth 认证服务接口
/// 实现 Azure AD v2.0 端点的授权码流程与令牌刷新
/// </summary>
public interface IOutlookOAuthService
{
    /// <summary>
    /// 生成 OAuth 授权 URL（用户在浏览器中访问以授权）。
    /// T000099: 使用用户级凭据（GetForUserAsync），支持每个用户自配置 Azure AD 应用注册。
    /// 改为异步方法以支持按 userId 查询数据库读取用户级凭据。
    /// </summary>
    /// <param name="userId">当前用户 ID（写入 state 参数，回调时校验；同时用于读取用户级凭据）</param>
    /// <returns>(授权 URL, state 值)</returns>
    Task<(string authorizeUrl, string state)> GetAuthorizeUrlAsync(int userId);

    /// <summary>
    /// 校验回调 state（CSRF 防护）—— 已知预期 userId 时使用
    /// </summary>
    Task<bool> ValidateStateAsync(string state, int expectedUserId);

    /// <summary>
    /// 从 state 中解析 userId（OAuth 回调时使用，因为此时还不知道 userId）
    /// </summary>
    /// <param name="state">state 参数</param>
    /// <returns>(是否有效, 解析出的 userId)</returns>
    Task<(bool success, int userId)> TryParseStateAsync(string state);

    /// <summary>
    /// 用授权码换取 Access/Refresh Token。
    /// T000099: 使用用户级凭据（GetForUserAsync），按 userId 读取对应 Azure AD 应用注册的 ClientId/ClientSecret。
    /// </summary>
    /// <param name="code">授权码</param>
    /// <param name="userId">用户 ID（用于读取用户级 Azure AD 凭据）</param>
    /// <returns>令牌响应</returns>
    Task<OutlookTokenResponse> ExchangeCodeAsync(string code, int userId);

    /// <summary>
    /// 使用 Refresh Token 刷新 Access Token。
    /// T000099: 使用用户级凭据（GetForUserAsync），按 userId 读取对应 Azure AD 应用注册的 ClientId/ClientSecret。
    /// </summary>
    /// <param name="refreshToken">刷新令牌</param>
    /// <param name="userId">用户 ID（用于读取用户级 Azure AD 凭据）</param>
    Task<OutlookTokenResponse?> RefreshTokenAsync(string refreshToken, int userId);

    /// <summary>
    /// 获取有效的 Access Token（必要时自动刷新）
    /// </summary>
    /// <param name="setting">用户的同步设置</param>
    /// <returns>有效的 Access Token，失败返回 null</returns>
    Task<string?> EnsureValidAccessTokenAsync(OutlookSyncSetting setting);
}

/// <summary>
/// OAuth 令牌响应
/// </summary>
public class OutlookTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = "Bearer";
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// 令牌过期时间（UTC）
    /// </summary>
    public DateTime ExpiresAt => DateTime.UtcNow.AddSeconds(ExpiresIn);
}
