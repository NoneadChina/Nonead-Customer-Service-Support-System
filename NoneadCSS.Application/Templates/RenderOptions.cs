namespace NoneadCSS.Application.Templates;

/// <summary>
/// 渲染选项
/// </summary>
public class RenderOptions
{
    /// <summary>是否对变量值进行 HTML 编码（默认 true）</summary>
    public bool HtmlEncode { get; set; } = true;

    /// <summary>跳过 HTML 编码的 key（如 message_body 已经过消毒）</summary>
    public HashSet<string>? RawHtmlKeys { get; set; }

    public static RenderOptions Default => new();
}
