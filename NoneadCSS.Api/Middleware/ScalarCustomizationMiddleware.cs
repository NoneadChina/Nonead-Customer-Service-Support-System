using System.Text;
using System.Text.Json;

namespace NoneadCSS.Api.Middleware;

/// <summary>
/// Scalar API 文档页面（<c>/docs</c>）自定义中间件：
/// 1) 替换 Scalar 底部版权为 Nonead（产品决定，保留原行为）；
/// 2) 中英双语切换：中文（默认）走 <c>/openapi/v1.json</c>，英文走 <c>/openapi/en.json</c>，
///    并在中文模式下按 DOM 层汉化 Scalar 自带界面文案（Scalar.AspNetCore 2.0.7 没有任何 locale 选项，只能这样处理）；
/// 3) 注入服务端渲染的语言切换按钮，脚本失败也可用。
/// 语言由 <c>?lang=en|zh-Hans</c> 决定，选择结果写入 Cookie <c>ncss_docs_lang</c>，页面内跳转沿用该选择。
/// </summary>
public class ScalarCustomizationMiddleware
{
    /// <summary>本中间件只处理该路径前缀（含 /docs 下的子路径）。</summary>
    private const string DocsPathPrefix = "/docs";

    // ===== 与 Program.cs 的 OpenAPI 文档名约定：中文 = v1，英文 = en =====
    internal const string LanguageQueryParam = "lang";
    internal const string LanguageCookieName = "ncss_docs_lang";
    internal const string English = "en";
    internal const string Chinese = "zh-Hans";
    internal const string ChineseOpenApiPath = "/openapi/v1.json";
    internal const string EnglishOpenApiPath = "/openapi/en.json";
    internal const string ChineseTitle = "NoneadCSS API 文档";
    internal const string EnglishTitle = "NoneadCSS API Documentation";

    /// <summary>语言切换按钮的两个固定链接：只用这两个字面量，不拼接任何请求内容。</summary>
    private const string EnglishSwitchHref = "/docs?lang=en";
    private const string ChineseSwitchHref = "/docs?lang=zh-Hans";

    private static readonly TimeSpan LanguageCookieMaxAge = TimeSpan.FromDays(30);

    private readonly RequestDelegate _next;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="next">下一个中间件</param>
    public ScalarCustomizationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// 执行中间件逻辑
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // 只处理 /docs 路径的请求
        if (!context.Request.Path.StartsWithSegments(DocsPathPrefix))
        {
            await _next(context);
            return;
        }

        // 解析语言：?lang= 优先，其次 Cookie，最后默认中文；只有显式带 ?lang= 时才写 Cookie
        var language = ResolveLanguage(context.Request, out var explicitChoice);
        if (explicitChoice)
        {
            context.Response.Cookies.Append(LanguageCookieName, language, new CookieOptions
            {
                Path = DocsPathPrefix,
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = LanguageCookieMaxAge,
            });
        }

        // 捕获响应流
        var originalBodyStream = context.Response.Body;
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);

            // 非 HTML 响应：原样透传，不改写、不改 ContentLength
            if (!IsHtmlResponse(context.Response.ContentType))
            {
                responseBody.Seek(0, SeekOrigin.Begin);
                context.Response.Body = originalBodyStream;
                await responseBody.CopyToAsync(originalBodyStream, context.RequestAborted);
                return;
            }

            // HEAD / 空响应体：没有内容可改写，保持下游给出的 ContentLength
            var buffer = responseBody.GetBuffer();
            if (responseBody.Length == 0)
            {
                context.Response.Body = originalBodyStream;
                return;
            }

            var html = Encoding.UTF8.GetString(buffer, 0, (int)responseBody.Length);
            var modifiedBytes = Encoding.UTF8.GetBytes(RewriteHtml(html, language));

            context.Response.Body = originalBodyStream;
            if (!context.Response.HasStarted)
            {
                context.Response.ContentLength = modifiedBytes.Length;
            }

            await originalBodyStream.WriteAsync(modifiedBytes, 0, modifiedBytes.Length, context.RequestAborted);
        }
        finally
        {
            // 确保异常路径下响应流与 ContentLength 被正确还原，避免残留 MemoryStream 状态
            if (context.Response.Body == responseBody)
            {
                context.Response.Body = originalBodyStream;
                // 异常路径下未将修改后的内容写回，清除 ContentLength 避免与实际响应体不匹配
                context.Response.ContentLength = null;
            }

            responseBody.Dispose();
        }
    }

    private static bool IsHtmlResponse(string? contentType)
        => contentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// 解析本次请求要呈现的语言。仅接受 <see cref="English"/> / <see cref="Chinese"/> 两个归一化结果，
    /// 其它值（含伪造的 Cookie 内容）一律忽略，回落到中文。
    /// </summary>
    /// <param name="request">当前请求</param>
    /// <param name="explicitChoice">是否由 <c>?lang=</c> 显式指定（只有此时才应写 Cookie）</param>
    internal static string ResolveLanguage(HttpRequest request, out bool explicitChoice)
    {
        explicitChoice = false;

        if (request.Query.TryGetValue(LanguageQueryParam, out var queryValues))
        {
            var fromQuery = NormalizeLanguage(queryValues.ToString());
            if (fromQuery is not null)
            {
                explicitChoice = true;
                return fromQuery;
            }
        }

        if (request.Cookies.TryGetValue(LanguageCookieName, out var cookieValue))
        {
            var fromCookie = NormalizeLanguage(cookieValue);
            if (fromCookie is not null)
            {
                return fromCookie;
            }
        }

        return Chinese;
    }

    /// <summary>把请求里的语言写法归一化；无法识别时返回 null。</summary>
    internal static string? NormalizeLanguage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        return value.ToLowerInvariant() switch
        {
            "en" or "en-us" or "en-gb" or "en-au" or "en-ca" => English,
            "zh" or "zh-hans" or "zh-cn" or "zh-sg" or "zh-hant" or "zh-tw" or "zh-hk" => Chinese,
            _ => null,
        };
    }

    /// <summary>
    /// 按语言改写 Scalar 页面 HTML：英文模式换 OpenAPI 文档地址与标题；中文模式注入界面汉化脚本。
    /// 两种语言都会注入品牌替换脚本与服务端渲染的语言切换按钮。
    /// </summary>
    internal static string RewriteHtml(string html, string language)
    {
        if (language == English)
        {
            html = html.Replace(ChineseOpenApiPath, EnglishOpenApiPath, StringComparison.Ordinal);
            html = html.Replace(ChineseTitle, EnglishTitle, StringComparison.Ordinal);
        }

        html = InsertLanguageSwitcher(html, language);
        return InjectBeforeClosingHead(html, language == English ? BrandingScript : BrandingScript + ChineseChromeStyle + ChineseChromeScript);
    }

    // 要注入的自定义 JavaScript 脚本：把 Scalar 的 "Powered by Scalar" 换成 Nonead。
    // 打包产物里确认存在该文本节点：," Powered by Scalar ",-1 且其 a 标签 href 为 https://www.scalar.com。
    private const string BrandingScript = """
        <script>
        (function() {
            function replaceScalarBranding() {
                // 查找所有包含 Scalar 版权信息的链接
                var links = document.querySelectorAll('a[href*="scalar.com"]');
                links.forEach(function(link) {
                    var text = link.textContent || '';
                    if (text.indexOf('Powered') !== -1 || text.indexOf('powered') !== -1) {
                        // 修改链接地址
                        link.href = 'https://www.nonead.com';
                        link.target = '_blank';
                        link.rel = 'noopener noreferrer';
                        // 修改文字
                        link.textContent = 'Powered by Nonead';
                    }
                });
            }

            // 页面加载后执行
            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', function() {
                    replaceScalarBranding();
                    // 持续观察 DOM 变化，因为 Scalar 是 SPA
                    var observer = new MutationObserver(replaceScalarBranding);
                    observer.observe(document.body, { childList: true, subtree: true });
                });
            } else {
                replaceScalarBranding();
                var observer = new MutationObserver(replaceScalarBranding);
                observer.observe(document.body, { childList: true, subtree: true });
            }
        })();
        </script>
        """;

    // Scalar 的 "Required" 是 CSS 生成内容（打包产物里为 .required[data-v-de383491]:after{content:"Required"}），
    // 不是 DOM 文本节点，所以文本遍历改不到它，只能用内联 <style> 覆盖（CSP 对 /docs 允许 style-src 'unsafe-inline'）。
    private const string ChineseChromeStyle = """
        <style id="ncss-zh-chrome-css">.required:after,.required::after{content:"必填"!important}</style>
        """;

    /// <summary>中文界面汉化脚本（含 ScalarChromeLocale 生成的字典字面量，整体控制在 12 KB 以内）。</summary>
    private static readonly string ChineseChromeScript = BuildChineseChromeScript();

    private static string BuildChineseChromeScript()
        => string.Concat(
            """
            <script id="ncss-zh-chrome-js">(function(){var D=
            """,
            ScalarChromeLocale.ToJavaScriptObjectLiteral(),
            """
            ;var A=['title','aria-label','placeholder'];
            var SKIP='script,style,textarea,pre,code,.cm-editor,.cm-content,.cm-line,.property-name,.parameter-item-name,#ncss-lang-switch';
            function skipped(n){var e=n.nodeType===1?n:n.parentElement;if(!e||!e.closest){return false;}return e.closest(SKIP)!==null;}
            var FILTER={acceptNode:function(n){return skipped(n)?2:1;}};
            function hit(value){var t=value.trim();if(!t){return null;}var v=D[t];return (typeof v==='string'&&v!==t)?v:null;}
            function mark(n){var e=n.nodeType===1?n:n.parentElement;if(e){e.setAttribute('data-ncss-i18n','1');}}
            function translateText(n){var value=n.nodeValue;if(!value){return;}var v=hit(value);if(!v){return;}
            var lead=/^[\s\u00a0]*/.exec(value)[0],tail=/[\s\u00a0]*$/.exec(value)[0];n.nodeValue=lead+v+tail;mark(n);}
            function translateAttrs(e){for(var i=0;i<A.length;i++){var v=hit(e.getAttribute(A[i])||'');if(v){e.setAttribute(A[i],v);mark(e);}}}
            function walk(root){if(!root){return;}if(root.nodeType===3){translateText(root);return;}
            if(root.nodeType!==1&&root.nodeType!==9&&root.nodeType!==11){return;}if(root.nodeType===1&&skipped(root)){return;}
            if(root.nodeType===1){translateAttrs(root);}
            var w=document.createTreeWalker(root,4|1,FILTER,false),n;
            while((n=w.nextNode())){if(n.nodeType===3){translateText(n);}else{translateAttrs(n);}}}
            var queue=null,scheduled=false;
            function flush(){scheduled=false;var list=queue;queue=null;for(var i=0;i<list.length;i++){walk(list[i]);}}
            function schedule(n){if(!queue){queue=[];}queue.push(n);if(!scheduled){scheduled=true;
            (window.requestAnimationFrame||function(f){setTimeout(f,16);})(flush);}}
            function start(){walk(document.body);
            new MutationObserver(function(rs){for(var i=0;i<rs.length;i++){var an=rs[i].addedNodes;
            for(var j=0;j<an.length;j++){schedule(an[j]);}}}).observe(document.body,{childList:true,subtree:true});}
            if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',start);}else{start();}
            })();</script>
            """);

    /// <summary>构造服务端渲染的语言切换按钮（内联样式，脚本被禁用时依旧可用）。</summary>
    internal static string BuildLanguageSwitcher(string activeLanguage)
    {
        const string PillStyle = "position:fixed;top:calc(var(--refs-header-height,64px) + 10px);right:16px;z-index:2147483000;"
            + "display:flex;gap:4px;align-items:center;padding:3px;border-radius:9999px;pointer-events:auto;"
            + "background:rgba(24,24,32,.82);border:1px solid rgba(152,120,255,.38);"
            + "box-shadow:0 6px 24px rgba(0,0,0,.35);backdrop-filter:blur(6px);"
            + "font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;font-size:12px;line-height:1";

        var englishActive = activeLanguage == English;

        return @"<div id=""ncss-lang-switch"" role=""group"" aria-label=""Language / 语言"" style=""" + PillStyle + @""">"
            + BuildSwitchLink(EnglishSwitchHref, "English", "en", englishActive)
            + BuildSwitchLink(ChineseSwitchHref, "简体中文", "zh-Hans", !englishActive)
            + "</div>";
    }

    private static string BuildSwitchLink(string href, string label, string lang, bool active)
    {
        const string ActiveStyle = "padding:6px 11px;border-radius:9999px;text-decoration:none;background:#8b5cf6;color:#ffffff;font-weight:600";
        const string IdleStyle = "padding:6px 11px;border-radius:9999px;text-decoration:none;background:transparent;color:#c4b5fd;font-weight:500";

        return @"<a href=""" + href + @""" lang=""" + lang + @""" "
            + (active ? @"aria-current=""true"" " : string.Empty)
            + @"style=""" + (active ? ActiveStyle : IdleStyle) + @""">"
            + label
            + "</a>";
    }

    /// <summary>把语言切换按钮插到 &lt;body&gt; 起始标签之后（找不到时退回到 &lt;/head&gt; 之前或文档末尾）。</summary>
    private static string InsertLanguageSwitcher(string html, string language)
    {
        var markup = BuildLanguageSwitcher(language);

        var bodyStart = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (bodyStart >= 0)
        {
            var tagEnd = html.IndexOf('>', bodyStart);
            if (tagEnd >= 0)
            {
                return string.Concat(html.AsSpan(0, tagEnd + 1), markup, html.AsSpan(tagEnd + 1));
            }
        }

        var headEnd = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headEnd >= 0)
        {
            return string.Concat(html.AsSpan(0, headEnd), markup, html.AsSpan(headEnd));
        }

        return html + markup;
    }

    /// <summary>在 &lt;/head&gt; 前注入脚本/样式；没有 head 时退回到 &lt;/body&gt; 前。</summary>
    private static string InjectBeforeClosingHead(string html, string markup)
    {
        if (html.Contains("</head>", StringComparison.OrdinalIgnoreCase))
        {
            return html.Replace("</head>", markup + "</head>", StringComparison.OrdinalIgnoreCase);
        }

        if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
        {
            return html.Replace("</body>", markup + "</body>", StringComparison.OrdinalIgnoreCase);
        }

        return html + markup;
    }
}
