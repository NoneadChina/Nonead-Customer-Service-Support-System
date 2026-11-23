namespace NoneadCSS.Application.Templates;

/// <summary>
/// 模板变量提供者接口 — 每个实现负责一个领域的变量收集
/// </summary>
public interface ITemplateVariableProvider
{
    /// <summary>执行优先级（数字小的先执行）</summary>
    int Order { get; }

    /// <summary>
    /// 从上下文中收集变量
    /// </summary>
    Task<Dictionary<string, string>> ResolveAsync(VariableContext context);
}
