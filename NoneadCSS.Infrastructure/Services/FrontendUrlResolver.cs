using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// 前端 URL 解析器实现。
/// 严格从配置 App:FrontendUrl / App:BaseUrl 读取，不依赖 HTTP 请求 host 动态推导，
/// 确保邮件模板、IM 消息模板、满意度链接等外发 URL 前缀统一可控。
/// 若配置缺失则立即抛异常，避免因 fallback 到访问者 host 导致生成错误的外部链接。
/// </summary>
public class FrontendUrlResolver : IFrontendUrlResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;

    public FrontendUrlResolver(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
    }

    public string GetFrontendUrl() => ResolveUrl("App:FrontendUrl");

    public string GetBaseUrl() => ResolveUrl("App:BaseUrl");

    private string ResolveUrl(string configKey)
    {
        var configured = _configuration[configKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.TrimEnd('/');
        }

        throw new InvalidOperationException(
            $"{configKey} 未配置。请在 appsettings.json 中显式配置 App:FrontendUrl/App:BaseUrl，" +
            "以确保邮件/IM 消息中的外部链接前缀正确且一致。");
    }
}
