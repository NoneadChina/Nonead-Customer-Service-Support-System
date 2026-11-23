namespace NoneadCSS.Application.Configuration;

/// <summary>
/// /metrics 端点访问控制选项
/// </summary>
public class MetricsOptions
{
    /// <summary>
    /// 是否强制要求 SuperAdmin 角色访问（默认 true）。
    /// 当请求 IP 命中 <see cref="AllowedIpRanges"/> 白名单时，本要求可被豁免（即允许 Prometheus 内网抓取器无登录访问）。
    /// </summary>
    public bool RequireSuperAdmin { get; set; } = true;

    /// <summary>
    /// 允许匿名访问 /metrics 的 IP/CIDR 白名单（可选）。
    /// 示例：[ "127.0.0.1/32", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" ]
    /// 为空时仅按 <see cref="RequireSuperAdmin"/> 规则。
    /// </summary>
    public List<string> AllowedIpRanges { get; set; } = new();

    /// <summary>
    /// 当部署在反向代理后，是否信任 X-Forwarded-For 头判断真实 IP（默认 false）。
    /// 若启用，必须同时确保 ForwardedHeaders 中间件配置正确，否则可被伪造。
    /// </summary>
    public bool TrustXForwardedFor { get; set; } = false;
}
