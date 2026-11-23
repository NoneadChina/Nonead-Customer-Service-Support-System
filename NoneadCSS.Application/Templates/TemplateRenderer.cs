using System.Net;
using System.Text.RegularExpressions;

namespace NoneadCSS.Application.Templates;

/// <summary>
/// 统一模板渲染引擎 — 替换变量 + 清理未匹配占位符
/// 支持 {{key}} 双花括号和 {key} 单花括号两种语法
/// </summary>
public class TemplateRenderer : ITemplateRenderer
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{?\w+\}?\}", RegexOptions.Compiled);

    public string Render(string template, Dictionary<string, string> variables, RenderOptions? options = null)
    {
        if (string.IsNullOrEmpty(template))
            return template ?? string.Empty;

        options ??= RenderOptions.Default;
        var result = template;

        if (variables != null && variables.Count > 0)
        {
            // 别名展开：将同义词变量（如 assigned_to → assignee_name）自动填充
            variables = TemplateVariableRegistry.Expand(variables);
            foreach (var kvp in variables)
            {
                var value = kvp.Value ?? string.Empty;
                // RawHtmlKeys 为 null 表示没有需要跳过编码的 key，此时按 HtmlEncode 选项决定
                var shouldEncode = options.HtmlEncode
                    && (options.RawHtmlKeys == null || !options.RawHtmlKeys.Contains(kvp.Key));
                var rendered = shouldEncode ? WebUtility.HtmlEncode(value) : value;
                result = result.Replace($"{{{{{kvp.Key}}}}}", rendered);
                result = result.Replace($"{{{kvp.Key}}}", rendered);
            }
        }

        // 清理所有未匹配的占位符，避免用户看到 {{xxx}} 或 {xxx} 字面量
        result = PlaceholderRegex.Replace(result, string.Empty);

        return result;
    }
}
