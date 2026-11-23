namespace NoneadCSS.Application.Configuration;

/// <summary>
/// 限流策略配置选项
/// </summary>
public class RateLimitOptions
{
    /// <summary>每分钟窗口（秒），用于分钟级限流策略</summary>
    public int PerMinuteWindowSeconds { get; set; } = 60;

    /// <summary>每小时窗口（秒），用于小时级限流策略</summary>
    public int PerHourWindowSeconds { get; set; } = 3600;
}
