namespace NoneadCSS.Application.Services;

/// <summary>
/// 前端 URL 动态解析器：优先从当前 HTTP 请求上下文提取域名（多域名部署时自动适配），
/// 无 HTTP 上下文时（如后台 Quartz 任务）回退到配置 App:FrontendUrl / App:BaseUrl。
/// </summary>
public interface IFrontendUrlResolver
{
    /// <summary>获取前端基础 URL（用于邮件中拼接会话/评价链接等）</summary>
    string GetFrontendUrl();

    /// <summary>获取应用基础 URL（用于邮件中拼接登录/重置密码链接等）</summary>
    string GetBaseUrl();
}
