using Ganss.Xss;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace NoneadCSS.Application.Common;

/// <summary>
/// HTML 消毒工具类，基于 Ganss.Xss.HtmlSanitizer 库进行白名单过滤，防止 XSS 攻击。
/// 使用成熟的 DOM 解析器替代正则方案，更安全、更准确。
/// </summary>
public static class HtmlSanitizerHelper
{
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer
        {
            KeepChildNodes = true,
        };

        // 白名单标签：保留原有业务所需的富文本标签
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.Add("p");
        sanitizer.AllowedTags.Add("br");
        sanitizer.AllowedTags.Add("div");
        sanitizer.AllowedTags.Add("span");
        sanitizer.AllowedTags.Add("a");
        sanitizer.AllowedTags.Add("strong");
        sanitizer.AllowedTags.Add("b");
        sanitizer.AllowedTags.Add("em");
        sanitizer.AllowedTags.Add("i");
        sanitizer.AllowedTags.Add("u");
        sanitizer.AllowedTags.Add("ul");
        sanitizer.AllowedTags.Add("ol");
        sanitizer.AllowedTags.Add("li");
        sanitizer.AllowedTags.Add("h1");
        sanitizer.AllowedTags.Add("h2");
        sanitizer.AllowedTags.Add("h3");
        sanitizer.AllowedTags.Add("h4");
        sanitizer.AllowedTags.Add("h5");
        sanitizer.AllowedTags.Add("h6");
        sanitizer.AllowedTags.Add("table");
        sanitizer.AllowedTags.Add("thead");
        sanitizer.AllowedTags.Add("tbody");
        sanitizer.AllowedTags.Add("tr");
        sanitizer.AllowedTags.Add("td");
        sanitizer.AllowedTags.Add("th");
        sanitizer.AllowedTags.Add("img");
        sanitizer.AllowedTags.Add("blockquote");
        sanitizer.AllowedTags.Add("code");
        sanitizer.AllowedTags.Add("pre");
        sanitizer.AllowedTags.Add("hr");
        sanitizer.AllowedTags.Add("font");

        // 白名单属性
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedAttributes.Add("src");
        sanitizer.AllowedAttributes.Add("alt");
        sanitizer.AllowedAttributes.Add("title");
        // 安全说明（M-S2）：不放开 style 属性。style 中的 background-image/url() 可触发外部请求（CSS 注入与数据外泄），
        // 且表达式类属性（expression、behavior）在旧浏览器中可执行脚本。富文本样式通过下方白名单 CSS 属性 + class 控制。
        sanitizer.AllowedAttributes.Add("class");
        sanitizer.AllowedAttributes.Add("target");
        sanitizer.AllowedAttributes.Add("colspan");
        sanitizer.AllowedAttributes.Add("rowspan");
        sanitizer.AllowedAttributes.Add("width");
        sanitizer.AllowedAttributes.Add("height");
        sanitizer.AllowedAttributes.Add("color");
        sanitizer.AllowedAttributes.Add("face");
        sanitizer.AllowedAttributes.Add("size");
        sanitizer.AllowedAttributes.Add("align");
        sanitizer.AllowedAttributes.Add("valign");
        sanitizer.AllowedAttributes.Add("cellpadding");
        sanitizer.AllowedAttributes.Add("cellspacing");
        sanitizer.AllowedAttributes.Add("border");
        sanitizer.AllowedAttributes.Add("bgcolor");

        // 白名单 CSS 属性
        // 注意：不放行 background-image / background 等可携带 url() 的属性，防止通过 CSS 触发外部请求或数据外泄。
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedCssProperties.Add("color");
        sanitizer.AllowedCssProperties.Add("background-color");
        sanitizer.AllowedCssProperties.Add("font-size");
        sanitizer.AllowedCssProperties.Add("font-weight");
        sanitizer.AllowedCssProperties.Add("font-style");
        sanitizer.AllowedCssProperties.Add("text-align");
        sanitizer.AllowedCssProperties.Add("text-decoration");
        sanitizer.AllowedCssProperties.Add("margin");
        sanitizer.AllowedCssProperties.Add("padding");
        sanitizer.AllowedCssProperties.Add("border");
        sanitizer.AllowedCssProperties.Add("width");
        sanitizer.AllowedCssProperties.Add("height");

        // 白名单 URL 协议
        // 注意：放行 cid 协议用于保留内嵌图片引用（<img src="cid:xxx">），
        // 后续在邮件附件保存后会将 cid: 引用替换为实际附件下载 URL。
        // cid: 本身是内嵌资源标识而非可外部请求的协议，故放行无外部请求/数据外泄风险。
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");
        sanitizer.AllowedSchemes.Add("cid");

        // 确保所有 on* 事件属性被移除（库默认已移除，这里显式声明）
        sanitizer.AllowedAttributes.Remove("on*");

        return sanitizer;
    }

    /// <summary>
    /// 对 HTML 内容进行消毒，移除危险标签和属性
    /// T000058: 在 HtmlSanitizer 处理之前，先移除 <style> 和 <script> 标签及其内容，
    /// 避免 KeepChildNodes=true 配置导致标签内容（CSS/JS 代码）泄漏为可见文本。
    /// </summary>
    /// <param name="html">原始 HTML 内容</param>
    /// <param name="logger">可选的日志对象，用于输出消毒过程的详细信息；传入 null 则不输出</param>
    /// <returns>消毒后的 HTML 内容</returns>
    public static string Sanitize(string? html, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            logger?.LogDebug("[HtmlSanitizer] 输入为空或仅空白，直接返回");
            return html ?? string.Empty;
        }

        var originalLen = html.Length;
        logger?.LogDebug("[HtmlSanitizer] 开始消毒：输入长度={Len}, 前100字符预览={Preview}",
            originalLen, html.Length > 100 ? html.Substring(0, 100) : html);

        // 预处理：移除 <style>...</style> 和 <script>...</script> 标签及其内容
        // HtmlSanitizer 的 KeepChildNodes=true 配置会保留被移除标签的子节点，
        // 导致 <style> 中的 CSS 代码和 <script> 中的 JS 代码泄漏为可见文本
        var beforeStyleLen = html.Length;
        html = Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", string.Empty, RegexOptions.IgnoreCase);
        var styleRemovedCount = beforeStyleLen - html.Length;
        logger?.LogDebug("[HtmlSanitizer] 移除 <style> 块：移除前长度={Before}, 移除后长度={After}, 减少字符数={Removed}",
            beforeStyleLen, html.Length, styleRemovedCount);

        var beforeScriptLen = html.Length;
        html = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
        var scriptRemovedCount = beforeScriptLen - html.Length;
        logger?.LogDebug("[HtmlSanitizer] 移除 <script> 块：移除前长度={Before}, 移除后长度={After}, 减少字符数={Removed}",
            beforeScriptLen, html.Length, scriptRemovedCount);

        var preprocessedLen = html.Length;
        var result = Sanitizer.Sanitize(html);
        var sanitizerRemovedCount = preprocessedLen - result.Length;
        logger?.LogDebug("[HtmlSanitizer] 白名单消毒完成：预处理后长度={PreLen}, 消毒后长度={ResultLen}, 消毒移除字符数={Removed}, 总移除字符数={TotalRemoved}, 最终前100字符预览={Preview}",
            preprocessedLen, result.Length, sanitizerRemovedCount,
            originalLen - result.Length,
            result.Length > 100 ? result.Substring(0, 100) : result);

        return result;
    }

    /// <summary>
    /// 将 HTML 转换为纯文本，用于邮件的 text/plain alternative 部分。
    /// 保留块级元素的换行，避免邮件客户端在没有纯文本备选时直接显示 HTML 源码。
    /// </summary>
    public static string ConvertToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        // 先消毒，确保危险内容被移除
        var sanitized = Sanitizer.Sanitize(html);

        // 移除隐藏的追踪标记 div（含 {#FS:...#}），避免纯文本中泄漏。
        // 注意：Sanitize() 的白名单不含 style 属性（M-S2），会先剥离 div 上的
        // display:none 样式，因此不能只按 style 匹配；改为匹配"包含 {#FS:...} 标记的 div"，
        // 并兜底清除任何裸露的 {#FS:...} 标记文本（T000106）。
        sanitized = Regex.Replace(sanitized, @"<div[^>]*>\s*\{#FS:\d+#\}\s*</div>", string.Empty, RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"\{#FS:\d+#\}", string.Empty, RegexOptions.IgnoreCase);

        // 块级标签转换为换行
        sanitized = Regex.Replace(sanitized, @"</(p|div|br|h[1-6]|li|tr)>", "\n", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

        // 移除所有剩余 HTML 标签
        sanitized = Regex.Replace(sanitized, @"<[^>]+>", string.Empty);

        // 解码 HTML 实体
        sanitized = System.Net.WebUtility.HtmlDecode(sanitized);

        // 压缩多余空白，保留段落换行
        var lines = sanitized.Split('\n', StringSplitOptions.None)
            .Select(l => l.TrimEnd())
            .Where(l => !string.IsNullOrWhiteSpace(l));
        return string.Join('\n', lines).Trim();
    }
}
