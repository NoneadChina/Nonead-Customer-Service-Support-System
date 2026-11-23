namespace NoneadCSS.Application.Services;

/// <summary>
/// 微信 access_token 管理服务 - 负责获取、缓存和自动刷新全局 access_token。
/// 微信 access_token 有效期 7200 秒，全局唯一，多实例并发刷新会互相失效，
/// 因此必须中央缓存（IMemoryCache）+ 提前刷新 + 互斥锁防止并发刷新。
/// </summary>
public interface IWeChatAccessTokenService
{
    /// <summary>
    /// 获取有效的 access_token。优先从缓存读取，未命中或即将过期时自动刷新。
    /// </summary>
    /// <param name="cancellationToken">可取消令牌（用于快速超时场景）。</param>
    /// <returns>有效的 access_token；若未配置或获取失败则返回 null</returns>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 强制刷新 access_token（用于 token 失效时的重试场景）。
    /// </summary>
    Task<string?> RefreshAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 微信接入是否已配置（AppId + AppSecret + Token 均非空）。
    /// </summary>
    bool IsConfigured { get; }
}
