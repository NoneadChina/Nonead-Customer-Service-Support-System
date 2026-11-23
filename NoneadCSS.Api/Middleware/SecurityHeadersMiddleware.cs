using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace NoneadCSS.Api.Middleware;

/// <summary>
/// 安全响应头中间件，为所有 HTTP 响应添加安全相关的响应头，
/// 包括内容安全策略（CSP）、X-Frame-Options、X-Content-Type-Options 等。
/// H-03：在生产/非开发环境下为前端 index.html 注入 per-request nonce（替换 __CSP_NONCE__ 占位符），
/// 并在 CSP 响应头中声明同一 nonce，从而实现 nonce-based CSP，消除 'unsafe-inline'。
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly bool _enableCsp;
    private readonly bool _cspReportOnly;
    private readonly bool _isDevelopment;
    private const string CspNonceItemKey = "CSP-Nonce";
    internal const string CspNoncePlaceholder = "__CSP_NONCE__";

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="next">下一个中间件</param>
    /// <param name="configuration">配置对象</param>
    /// <param name="env">Web 宿主环境</param>
    public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration, IWebHostEnvironment env)
    {
        _next = next;
        _configuration = configuration;
        _env = env;

        _enableCsp = _configuration.GetValue<bool>("Security:EnableCsp", true);
        _cspReportOnly = _configuration.GetValue<bool>("Security:CspReportOnly", false);
        _isDevelopment = _env.IsDevelopment();
    }

    /// <summary>
    /// 中间件执行入口
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Response.HasStarted)
        {
            await _next(context);
            return;
        }

        // H-03：提前生成 per-request nonce（如果此请求最终会走文档路径 / SPA 回退，则会在写入响应前
        // 替换 HTML 中 __CSP_NONCE__ 占位符）。开发环境仍使用宽松 CSP，故不生成。
        if (_enableCsp && !_isDevelopment)
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
            context.Items[CspNonceItemKey] = nonce;
            context.Response.Headers["X-CSP-Nonce"] = nonce;
        }

        context.Response.OnStarting(() =>
        {
            if (context.Response.HasStarted)
            {
                return Task.CompletedTask;
            }

            AddSecurityHeaders(context);
            return Task.CompletedTask;
        });

        // H-03：只有 HTML（index.html / SPA fallback）需要替换 nonce 占位符，
        // 其他响应直接透传以避免内存/性能开销。
        var requiresNonceInjection = RequiresHtmlNonceInjection(context.Request);
        if (!requiresNonceInjection)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);

            if (context.Response.StatusCode == StatusCodes.Status200OK
                && buffer.Length > 0
                && IsHtmlContentType(context.Response.ContentType))
            {
                buffer.Position = 0;
                var html = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync().ConfigureAwait(false);
                var nonce = context.Items[CspNonceItemKey] as string;
                if (!string.IsNullOrEmpty(nonce))
                {
                    // H-03：先替换 HTML 内的 __CSP_NONCE__ 占位符（meta / 模板中的 <script nonce=...> 等）。
                    if (html.Contains(CspNoncePlaceholder, StringComparison.Ordinal))
                    {
                        html = html.Replace(CspNoncePlaceholder, nonce, StringComparison.Ordinal);
                    }

                    // H-03：Vite 构建会自动注入 <script type="module" src="..."> 与 <link rel="stylesheet" href="...">，
                    // 它们在模板中并不存在，因此没有 nonce 属性。若不为其补齐 nonce，将被 strict nonce-based CSP 拦截。
                    html = InjectNonceIntoScripts(html, nonce);
                    html = InjectNonceIntoStylesheetLinks(html, nonce);

                    var bytes = Encoding.UTF8.GetBytes(html);
                    context.Response.Body = originalBody;
                    context.Response.ContentLength = bytes.Length;
                    await originalBody.WriteAsync(bytes.AsMemory(0, bytes.Length), context.RequestAborted)
                        .ConfigureAwait(false);
                    return;
                }

                // nonce 未启用（开发）：直接回写原始 buffer。
                buffer.Position = 0;
                context.Response.Body = originalBody;
                context.Response.ContentLength = buffer.Length;
                await buffer.CopyToAsync(originalBody, context.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                // 非 HTML / 非 200：直接回写 buffer（常见如 404 / 重定向）。
                buffer.Position = 0;
                context.Response.Body = originalBody;
                if (buffer.Length > 0)
                {
                    context.Response.ContentLength = buffer.Length;
                    await buffer.CopyToAsync(originalBody, context.RequestAborted).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            // 防御性：确保原始流被还原。
            if (!ReferenceEquals(context.Response.Body, originalBody))
            {
                context.Response.Body = originalBody;
            }
        }
    }

    /// <summary>
    /// 判断请求是否可能命中前端 index.html（默认文档、静态 /index.html、以及 SPA fallback 路径）。
    /// 只有这类请求才需要做 nonce 占位符替换，从而避免对 JSON/图片/JS/CSS 做无谓的缓冲。
    /// </summary>
    private static bool RequiresHtmlNonceInjection(HttpRequest request)
    {
        var path = request.Path.Value ?? "/";
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/attachments/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/docs", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // / 或 /index.html 或 典型 SPA 路由（无扩展名，非静态资源路径）。
        if (path == "/" || path.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase))
            return true;

        // Vite 构建产物 .js/.css 等静态资源不做替换。
        var ext = Path.GetExtension(path);
        return string.IsNullOrEmpty(ext);
    }

    private static bool IsHtmlContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return false;
        return contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// H-03 helper：为 HTML 中尚未携带 nonce 属性的 script 标签注入 nonce。
    /// 跳过已含 nonce= / src=data: / 内嵌事件处理脚本（保留原顺序和属性）。
    /// </summary>
    private static string InjectNonceIntoScripts(string html, string nonce)
    {
        if (string.IsNullOrEmpty(html)) return html;
        // <script ...> 开始标签；忽略已带 nonce=（大小写不敏感）的情况。
        return System.Text.RegularExpressions.Regex.Replace(
            html,
            "<script(?<attrs>\\s[^>]*)?>",
            m =>
            {
                var attrs = m.Groups["attrs"].Value;
                if (System.Text.RegularExpressions.Regex.IsMatch(
                        attrs,
                        "\\snonce\\s*=",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return m.Value;
                }
                return $"<script{attrs} nonce=\"{nonce}\">";
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// H-03 helper：为 HTML 中尚未携带 nonce 属性的 link rel=stylesheet 标签注入 nonce。
    /// link rel=preload、link rel=icon 等不受 style-src 管控，因此跳过。
    /// </summary>
    private static string InjectNonceIntoStylesheetLinks(string html, string nonce)
    {
        if (string.IsNullOrEmpty(html)) return html;
        // 匹配 <link ...> 自闭合标签，先过滤 rel 中包含 stylesheet 的再处理。
        return System.Text.RegularExpressions.Regex.Replace(
            html,
            "<link(?<attrs>\\s[^>]*)?\\s*/?>",
            m =>
            {
                var attrs = m.Groups["attrs"].Value;
                // rel=stylesheet / "stylesheet" 都算；支持单/双引号和省略引号。
                var relMatch = System.Text.RegularExpressions.Regex.Match(
                    attrs,
                    "\\srel\\s*=\\s*(?:\"(?<v>[^\"]*)\"|'(?<v>[^']*)'|(?<v>\\S+))",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!relMatch.Success) return m.Value;
                var relValue = relMatch.Groups["v"].Value;
                if (!System.Text.RegularExpressions.Regex.IsMatch(
                        relValue,
                        "(?:^|\\s)stylesheet(?:\\s|$)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return m.Value;
                }

                // 已带 nonce= 的直接跳过。
                if (System.Text.RegularExpressions.Regex.IsMatch(
                        attrs,
                        "\\snonce\\s*=",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return m.Value;
                }

                var selfClosing = m.Value.EndsWith("/>", StringComparison.Ordinal) ? "/" : string.Empty;
                return $"<link{attrs} nonce=\"{nonce}\"{selfClosing}>";
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// 添加安全响应头
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    private void AddSecurityHeaders(HttpContext context)
    {
        var response = context.Response;

        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

        var isDocPath = context.Request.Path.StartsWithSegments("/docs")
                        || context.Request.Path.StartsWithSegments("/openapi")
                        || context.Request.Path.StartsWithSegments("/scalar");

        // 文档页面允许同源 iframe 嵌入（Scalar/OpenAPI），其余页面统一 SAMEORIGIN
        response.Headers["X-Frame-Options"] = "SAMEORIGIN";

        if (_enableCsp)
        {
            var cspHeaderName = _cspReportOnly ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy";
            var cspPolicy = BuildCspPolicy(context, isDocPath);
            response.Headers[cspHeaderName] = cspPolicy;
        }
    }

    /// <summary>
    /// 构建内容安全策略（CSP）
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="isDocPath">是否为 API 文档路径</param>
    /// <returns>CSP 策略字符串</returns>
    private string BuildCspPolicy(HttpContext context, bool isDocPath)
    {
        if (isDocPath)
        {
            return "default-src 'self'; " +
                   "script-src 'self' 'unsafe-inline'; " +
                   "style-src 'self' 'unsafe-inline'; " +
                   "img-src 'self' data: https:; " +
                   "font-src 'self' data:; " +
                   "connect-src 'self' https:; " +
                   "frame-ancestors 'self';";
        }

        if (_isDevelopment)
        {
            // L-3 安全说明：开发环境 CSP 较宽松（允许 unsafe-inline / unsafe-eval），仅供本地调试使用，
            // 用于支持热重载、浏览器内 source map、Vue/React DevTools 等开发工具。
            // 生产环境（见下方 else 分支）已使用 nonce-based CSP（每次请求生成随机 nonce），
            // 不包含 'unsafe-inline' 或 'unsafe-eval'，可有效防御 XSS 注入。
            // 此分支仅在 _isDevelopment=true 时生效，生产环境不会加载。
            return "default-src 'self'; " +
                   "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
                   "style-src 'self' 'unsafe-inline'; " +
                   "img-src 'self' data: https:; " +
                   "font-src 'self' data:; " +
                   "connect-src 'self' wss: ws:; " +
                   "frame-ancestors 'self';";
        }

        // H-03：生产 / 非开发环境使用 nonce-based CSP。
        // nonce 已在 InvokeAsync 入口生成并写入 context.Items[CSP-Nonce]；
        // index.html 内的 __CSP_NONCE__ 占位符会在响应缓冲后替换为同一 nonce，
        // 保证响应头 CSP 与 index.html 内所有 <script>/<meta> nonce 属性严格一致。
        var nonce = context.Items[CspNonceItemKey] as string
                    ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        return "default-src 'self'; " +
               $"script-src 'self' 'nonce-{nonce}'; " +
               $"style-src 'self' 'nonce-{nonce}'; " +
               "img-src 'self' data: https:; " +
               "font-src 'self' data:; " +
               "connect-src 'self' wss: ws:; " +
               "frame-ancestors 'self';";
    }
}
