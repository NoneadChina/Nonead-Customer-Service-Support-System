namespace NoneadCSS.Application.Configuration;

/// <summary>
/// Token 刷新缓冲配置选项
/// </summary>
public class TokenRefreshOptions
{
    /// <summary>Token 提前刷新缓冲时间（分钟），在 Token 实际过期前提前刷新</summary>
    public int RefreshBufferMinutes { get; set; } = 5;
}
