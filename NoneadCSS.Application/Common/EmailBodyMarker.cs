using System.Text.RegularExpressions;

namespace NoneadCSS.Application.Common;

/// <summary>
/// T000106：邮件正文会话回环标记 {#FS:threadId#} 规范化。
/// 背景：客户回复需要回环匹配到原会话，正文需携带隐藏标记；但历史路径（工作流模板渲染、
/// 满意度/关闭通知等）生成的标记 div 可能在途中丢失 display:none 内联样式，
/// 变成可见 <div>{#FS:1115#}</div>，收件端会看到明文 "#FS"。
/// 出站前统一规范化：移除既有标记 div（无论是否带样式）与裸露标记文本，
/// 再在末尾追加单个隐藏样式 div —— HTML 渲染不可见、纯文本备选被剥离、入站仍可提取。
/// </summary>
public static class EmailBodyMarker
{
    /// <summary>隐藏会话标记的 div 样式（display:none + mso-hide 兼容 Outlook）。</summary>
    public const string HiddenStyle =
        "display:none !important;visibility:hidden;mso-hide:all;height:0;max-height:0;overflow:hidden;font-size:0;line-height:0;";

    /// <summary>规范化正文：正文不含 {#FS:...#} 时原样返回。</summary>
    public static string Normalize(string? html)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("{#FS:", StringComparison.Ordinal))
            return html ?? string.Empty;

        var idMatch = Regex.Match(html, @"\{#FS:(\d+)#\}");
        if (!idMatch.Success)
            return html;

        // 1) 移除所有既有标记 div（无论是否带隐藏样式），避免可见或重复
        html = Regex.Replace(html, @"<div[^>]*>\s*\{#FS:\d+#\}\s*</div>", string.Empty, RegexOptions.IgnoreCase);
        // 2) 移除任何裸露的标记文本（兜底）
        html = Regex.Replace(html, @"\{#FS:\d+#\}", string.Empty, RegexOptions.IgnoreCase);
        // 3) 末尾追加单个隐藏样式标记
        return html.TrimEnd('\r', '\n', ' ')
            + $"\n<div style=\"{HiddenStyle}\">{{#FS:{idMatch.Groups[1].Value}#}}</div>";
    }
}
