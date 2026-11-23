namespace NoneadCSS.Application.Templates;

/// <summary>
/// 模板构建器 — 编排 Provider 管道：收集变量 → 别名展开 → 统一渲染
/// </summary>
public class TemplateBuilder
{
    private readonly IEnumerable<ITemplateVariableProvider> _providers;
    private readonly ITemplateRenderer _renderer;

    public TemplateBuilder(
        IEnumerable<ITemplateVariableProvider> providers,
        ITemplateRenderer renderer)
    {
        _providers = providers;
        _renderer = renderer;
    }

    /// <summary>
    /// 渲染模板：运行所有 Provider → 合并 Extra → 别名展开 → 统一渲染
    /// </summary>
    public async Task<string> RenderAsync(
        string template,
        VariableContext context,
        RenderOptions? options = null)
    {
        // 1. 运行所有 Provider 收集变量
        var variables = new Dictionary<string, string>();
        foreach (var provider in _providers.OrderBy(p => p.Order))
        {
            var vars = await provider.ResolveAsync(context);
            foreach (var kvp in vars)
                variables[kvp.Key] = kvp.Value;
        }

        // 2. 合并手动附加变量
        foreach (var kvp in context.Extra)
            variables[kvp.Key] = kvp.Value;

        // 3. 别名展开（同义词自动填充）
        variables = TemplateVariableRegistry.Expand(variables);

        // 4. 统一渲染 + 占位符清理
        return _renderer.Render(template, variables, options);
    }

    /// <summary>
    /// 仅收集变量不渲染（供调试或预览使用）
    /// </summary>
    public async Task<Dictionary<string, string>> CollectVariablesAsync(VariableContext context)
    {
        var variables = new Dictionary<string, string>();
        foreach (var provider in _providers.OrderBy(p => p.Order))
        {
            var vars = await provider.ResolveAsync(context);
            foreach (var kvp in vars)
                variables[kvp.Key] = kvp.Value;
        }

        foreach (var kvp in context.Extra)
            variables[kvp.Key] = kvp.Value;

        return TemplateVariableRegistry.Expand(variables);
    }
}
