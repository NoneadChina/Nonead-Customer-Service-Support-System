using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Extensions;

namespace NoneadCSS.Api.Middleware;

public class CsrfProtectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _enforceCsrf;

    // CSRF 豁免路径：无 Cookie/Session 的公开端点或服务间调用入口
    private static readonly HashSet<string> CsrfExemptPaths =
    [
        // 注意：refresh-token 不在豁免列表中。
        // refresh-token 端点会签发新的 JWT/refresh cookie，属于敏感写操作；
        // 若豁免 CSRF，攻击者可借助已登录用户的 Cookie 强制刷新令牌并窃取新会话。
        "/api/auth/login",
        "/api/auth/register",
        "/api/auth/forgot-password",
        "/api/auth/reset-password",
        "/api/setup",
        "/api/webhook",
        "/api/satisfactionratings",
        "/api/chat/initiate",
        "/api/chat/token",
        // T000104：客户侧 SignalR ChatHub（/hubs/chat）必须豁免 CSRF。
        // 客户 Widget 无登录态（无 XSRF-TOKEN Cookie），SignalR 协商是 POST，
        // 不走豁免会被本中间件 403 拦截 → 客户永远无法建立实时连接 → 收不到客服回复。
        // ChatHub 为 [AllowAnonymous] + sessionToken 鉴权、不依赖 Cookie，CSRF 防护对其无意义。
        "/hubs/chat",
        "/api/tracking",
        "/api/wechat",
        "/api/whatsapp",       // WhatsApp Webhook 回调端点豁免（Webhook POST 来自 Meta 服务器，无 CSRF token）
        "/api/feishu",         // 飞书事件回调端点豁免（事件 POST 来自飞书服务器，无 CSRF token）
        "/api/dingding",       // 钉钉事件回调端点豁免（事件 POST 来自钉钉服务器，无 CSRF token）
        // 四个新渠道的 Webhook 不在此处豁免：它们各自的回调路径与后台管理接口共用同一个
        // /api/<channel> 前缀，若按前缀豁免，settings PUT / reply / takeover 等敏感写操作
        // 也会一起失去 CSRF 防护。改为在 CsrfExemptWebhookPostPaths 中按「精确路径 + 仅 POST」豁免。
        "/health"
    ];

    // 渠道 Webhook 回调路径：仅豁免 POST（Webhook 只能是 POST），且必须精确匹配，
    // 避免 /api/messenger 这类前缀把 /api/messenger/settings 一并放行。
    private static readonly HashSet<string> CsrfExemptWebhookPostPaths =
    [
        "/api/messenger",                      // Meta 订阅校验后的消息回调（控制器根路径）
        "/api/telegram/webhook",               // Telegram setWebhook 注册的回调路径
        "/api/line/webhook",                   // LINE Messaging API Webhook 端点
        "/api/imessage/bluebubbles/webhook",   // BlueBubbles 桥接服务回调
        "/api/imessage/amb/message"            // Apple Messages for Business 回调
    ];

    public CsrfProtectionMiddleware(RequestDelegate next, IConfiguration configuration, IWebHostEnvironment env)
    {
        _next = next;
        // 通过配置 Security:EnforceCsrf 控制，默认始终启用 CSRF 防护
        // 开发环境如需放宽（Postman/curl 调试），在 appsettings.Development.json 中设置 Security:EnforceCsrf=false
        // 生产环境强制启用，忽略配置项，防止误关导致安全漏洞
        var configured = configuration["Security:EnforceCsrf"];
        var configuredValue = bool.TryParse(configured, out var v) ? v : true;
        _enforceCsrf = env.IsDevelopment() ? configuredValue : true;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path;
        if (IsCsrfExemptPath(method, path))
        {
            await _next(context);
            return;
        }

        // 移除对 Bearer / X-Api-Key 的无条件 CSRF 豁免
        // CSRF 防护与认证方式正交：即便使用 Bearer，仍需 Origin/CSRF Token 校验
        // 仅在以下情况才豁免 CSRF：
        // 1. 显式标记为内部调用（如 Webhook 触发的服务端请求）
        // 2. 通过 X-Internal-Token 头（由后端服务间调用使用，不暴露给浏览器）
        if (IsInternalServiceCall(context))
        {
            await _next(context);
            return;
        }

        var cookieToken = context.Request.Cookies["XSRF-TOKEN"];
        var headerToken = context.Request.Headers["X-XSRF-TOKEN"].FirstOrDefault();

        // 使用常量时间比较 token，防止攻击者通过响应时间差异推断 token 值（时序攻击）
        var cookieBytes = cookieToken is null ? null : Encoding.UTF8.GetBytes(cookieToken);
        var headerBytes = headerToken is null ? null : Encoding.UTF8.GetBytes(headerToken);

        bool tokensValid = cookieBytes != null && headerBytes != null
            && cookieBytes.Length == headerBytes.Length
            && CryptographicOperations.FixedTimeEquals(cookieBytes, headerBytes);

        if (!tokensValid && _enforceCsrf)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = NoneadCSS.Api.Localization.ApiMessageLocalizer.Localize("CSRF 验证失败", NoneadCSS.Api.Localization.ApiRequestCulture.Of(context)) ?? "CSRF 验证失败" });
            return;
        }

        // 当 EnforceCsrf 启用时（默认），强制校验 Origin；
        // 调试时需在 appsettings.Development.json 中设置 Security:EnforceCsrf=false 或显式提供 Origin 头
        var origin = context.Request.Headers["Origin"].FirstOrDefault();
        if (!string.IsNullOrEmpty(origin) && _enforceCsrf)
        {
            var referer = context.Request.Headers["Referer"].FirstOrDefault();

            // HIGH-3 修复：Origin 与 Referer 不必严格同域，Origin 只需命中以下任一条：
            //   1. Origin 与 Referer 同域（传统浏览器同源请求）
            //   2. Origin 在 CORS 白名单中（跨域合法站点，例：前端域名 css.example.com）
            //   3. Origin 与当前 Host（含端口）同域 —— S4：仅当 Host 本身在受信白名单中才允许，
            //      防止攻击者伪造 Host 头 + 任意 Origin 绕过校验。
            // 三者都不命中才返回 403。
            var sameOriginOk = !string.IsNullOrEmpty(referer) && IsSameOrigin(origin, referer);
            var whitelistOk = IsCorsWhitelistedOrigin(context, origin);
            var sameAsHostOk = IsTrustedSameHost(context.Request, origin);

            if (!sameOriginOk && !whitelistOk && !sameAsHostOk)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { message = NoneadCSS.Api.Localization.ApiMessageLocalizer.Localize("CSRF 验证失败：Origin 不在合法白名单", NoneadCSS.Api.Localization.ApiRequestCulture.Of(context)) ?? "CSRF 验证失败：Origin 不在合法白名单" });
                return;
            }
        }
        else if (string.IsNullOrEmpty(origin) && _enforceCsrf)
        {
            // 浏览器请求必须带 Origin；缺失则拒绝（curl/Postman 等需显式提供 Origin 头）
            // 不再对 localhost 做特殊豁免，防止伪造 Host 头绕过 Origin 校验
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = NoneadCSS.Api.Localization.ApiMessageLocalizer.Localize("CSRF 验证失败：缺少 Origin 头", NoneadCSS.Api.Localization.ApiRequestCulture.Of(context)) ?? "CSRF 验证失败：缺少 Origin 头" });
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// 检查请求是否在 CSRF 豁免列表中。
    /// ① 普通豁免：精确匹配或以 exemptPath + "/" 开头的前缀匹配（公开端点/服务间调用）；
    /// ② 渠道 Webhook：只豁免 POST，且必须精确匹配（容忍尾部斜杠），
    ///    这样 /api/messenger 放行 Webhook，而 /api/messenger/settings 仍需 CSRF Token。
    /// </summary>
    private static bool IsCsrfExemptPath(string method, PathString path)
    {
        var pathStr = path.Value ?? string.Empty;

        if (HttpMethods.IsPost(method))
        {
            var normalized = pathStr.Length > 1 ? pathStr.TrimEnd('/') : pathStr;
            foreach (var webhookPath in CsrfExemptWebhookPostPaths)
            {
                if (normalized.Equals(webhookPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        foreach (var exemptPath in CsrfExemptPaths)
        {
            // 精确匹配，或以 "exemptPath/" 开头（防止 /api/webhook 误匹配 /api/webhooks）
            if (pathStr.Equals(exemptPath, StringComparison.OrdinalIgnoreCase) ||
                pathStr.StartsWith(exemptPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 判断是否为内部服务调用（来自其他后端服务而非浏览器）
    /// </summary>
    private static bool IsInternalServiceCall(HttpContext context)
    {
        // 服务间调用约定：使用 X-Internal-Token 头，由后端在配置中生成
        var internalToken = context.Request.Headers["X-Internal-Token"].FirstOrDefault();
        if (string.IsNullOrEmpty(internalToken))
        {
            return false;
        }
        // 实际生产中应从配置读取并使用 FixedTimeEquals 比较
        var configuredToken = context.RequestServices.GetService<IConfiguration>()?["Security:InternalServiceToken"];
        if (string.IsNullOrEmpty(configuredToken))
        {
            return false;
        }
        var provided = Encoding.UTF8.GetBytes(internalToken);
        var expected = Encoding.UTF8.GetBytes(configuredToken);
        return provided.Length == expected.Length && CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    private static bool IsSameOrigin(string origin, string referer)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }
        if (!Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
        {
            return false;
        }
        return originUri.Scheme == refererUri.Scheme &&
               originUri.Host == refererUri.Host &&
               originUri.Port == refererUri.Port;
    }

    /// <summary>
    /// S4: 判断 Origin 是否与当前请求的 Host 同域，且仅当该 Host 本身命中受信白名单时才放行。
    /// 原 IsSameAsHost 直接信任 request.Host —— Host 头可由攻击者伪造：
    /// 伪造 Host=evil.com 的请求再带上 Origin=evil.com 即可绕过校验。
    /// 现改为双重条件：request.Host 必须命中 CORS 白名单/受信主机，且 Origin 与 Host 同域。
    /// </summary>
    private static bool IsTrustedSameHost(HttpRequest request, string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            return false;

        var scheme = request.Scheme;
        var host = request.Host;

        // 1) 请求的 Host 本身必须在受信白名单中，否则一律拒绝（防 Host 头伪造）
        if (!IsOriginWhitelisted(request.HttpContext, $"{scheme}://{host}"))
            return false;

        // 2) Origin 与 Host 同域（含端口）
        var originHost = originUri.Host;
        var originPort = originUri.IsDefaultPort
            ? (originUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80)
            : originUri.Port;

        if (!string.Equals(scheme, originUri.Scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        if (host.Port.HasValue)
        {
            return string.Equals(host.Host, originHost, StringComparison.OrdinalIgnoreCase)
                   && host.Port.Value == originPort;
        }

        // Host 不带端口 → 按 Scheme 默认端口
        var defaultPort = scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
        return string.Equals(host.Host, originHost, StringComparison.OrdinalIgnoreCase)
               && originPort == defaultPort;
    }

    /// <summary>
    /// 判断 Origin 是否在配置的 CORS 白名单中。
    /// 支持三种配置来源（优先级从高到低）：
    ///   1. 环境变量 CORS__AllowedOrigins（逗号分隔）
    ///   2. appsettings.json 中 CORS:AllowedOrigins 数组
    ///   3. 开发环境内置 localhost / 127.0.0.1
    /// </summary>
    private static bool IsCorsWhitelistedOrigin(HttpContext context, string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            return false;

        var originBase = $"{originUri.Scheme}://{originUri.Host}";
        if (!originUri.IsDefaultPort) originBase += $":{originUri.Port}";

        return IsOriginWhitelisted(context, originBase);
    }

    /// <summary>
    /// 判断给定的 originBase（scheme://host[:port]）是否命中受信白名单。
    /// 供 CORS 白名单校验与 Host 受信判定共用。
    /// </summary>
    private static bool IsOriginWhitelisted(HttpContext? context, string originBase)
    {
        if (context == null) return false;

        var cfg = context.RequestServices.GetService<IConfiguration>();
        if (cfg == null) return false;

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var envOrigins = Environment.GetEnvironmentVariable("CORS__AllowedOrigins");
        if (!string.IsNullOrEmpty(envOrigins))
        {
            foreach (var o in envOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                candidates.Add(o.Trim().TrimEnd('/'));
            }
        }

        var cfgSection = cfg.GetSection("CORS:AllowedOrigins");
        if (cfgSection.Exists())
        {
            foreach (var o in cfgSection.GetChildren())
            {
                if (!string.IsNullOrEmpty(o.Value))
                    candidates.Add(o.Value.Trim().TrimEnd('/'));
            }
        }

        var env = context.RequestServices.GetService<IWebHostEnvironment>();
        if (env != null && env.IsDevelopment())
        {
            // 开发环境内置 CSRF Origin 白名单；与 Program.cs 的 CORS 白名单保持一致，
            // 避免 CORS 放行但 CSRF 拦截的调试死锁。
            var devSchemes = new[] { "http", "https" };
            var devPorts = new[] { 0, 5171, 5172, 5173, 3000, 8080 }; // 0 代表默认端口 (80/443)

            // 1) 本机调试域名/IP
            foreach (var scheme in devSchemes)
            foreach (var devHost in new[] { "localhost", "127.0.0.1" })
            foreach (var port in devPorts)
            {
                if (port == 0) candidates.Add($"{scheme}://{devHost}");
                else candidates.Add($"{scheme}://{devHost}:{port}");
            }

            // 2) 开发业务域名：css.example.com 及其常用子域
            foreach (var scheme in devSchemes)
            foreach (var devHost in new[] { "css.example.com", "www.css.example.com", "app.css.example.com" })
            foreach (var port in devPorts)
            {
                if (port == 0) candidates.Add($"{scheme}://{devHost}");
                else candidates.Add($"{scheme}://{devHost}:{port}");
            }

            // 3) 从配置 Csrf:DevAllowedOrigins 读取开发环境额外允许的 Origin 主机（IP 或域名）
            // M-3 修复：原硬编码 203.0.113.10 已迁移到 appsettings.Development.json 的 Csrf:DevAllowedOrigins 配置项，
            // 仅在 IsDevelopment 分支生效，生产环境不加载。多实例/多人协作时各自维护配置即可。
            var devAllowedOrigins = cfg.GetSection("Csrf:DevAllowedOrigins");
            if (devAllowedOrigins.Exists())
            {
                foreach (var hostEntry in devAllowedOrigins.GetChildren())
                {
                    var devHost = hostEntry.Value?.Trim();
                    if (string.IsNullOrEmpty(devHost)) continue;
                    foreach (var scheme in devSchemes)
                    foreach (var port in devPorts)
                    {
                        if (port == 0) candidates.Add($"{scheme}://{devHost}");
                        else candidates.Add($"{scheme}://{devHost}:{port}");
                    }
                }
            }
        }

        return candidates.Contains(originBase.TrimEnd('/'));
    }
}
