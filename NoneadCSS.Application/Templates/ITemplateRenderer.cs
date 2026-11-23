namespace NoneadCSS.Application.Templates;

/// <summary>
/// 统一模板渲染引擎接口
/// </summary>
public interface ITemplateRenderer
{
    /// <summary>
    /// 渲染模板：替换变量 + 清理未匹配占位符
    /// </summary>
    string Render(string template, Dictionary<string, string> variables, RenderOptions? options = null);
}
