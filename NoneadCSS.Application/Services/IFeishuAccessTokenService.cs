namespace NoneadCSS.Application.Services;

/// <summary>
/// 飞书 tenant_access_token 管理服务 - 负责获取、缓存和自动刷新应用访问令牌。
/// 飞书 tenant_access_token 有效期 7200 秒，使用 IMemoryCache 中央缓存 + 提前刷新 +
/// 互斥锁防止并发刷新。
/// </summary>
public interface IFeishuAccessTokenService
{
    /// <summary>
    /// 获取有效的 tenant_access_token。优先从缓存读取，未命中或即将过期时自动刷新。
    /// </summary>
    /// <param name="cancellationToken">可取消令牌（用于快速超时场景）。</param>
    /// <returns>有效的 tenant_access_token；若未配置或获取失败则返回 null</returns>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 飞书接入是否已配置（AppId + AppSecret + VerificationToken 均非空）。
    /// </summary>
    bool IsConfigured { get; }
}
