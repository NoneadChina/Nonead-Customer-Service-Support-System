using NoneadCSS.Application.Configuration;
using Microsoft.Extensions.Options;

namespace NoneadCSS.Application.Templates.Providers;

/// <summary>
/// 系统变量提供者 — company_name, login_url 等品牌级变量
/// 优先级最高（Order=0），确保所有模板都能获取到系统变量
/// </summary>
public class SystemVariableProvider : ITemplateVariableProvider
{
    private readonly BrandOptions _brand;

    public int Order => 0;

    public SystemVariableProvider(IOptions<BrandOptions> brand)
    {
        _brand = brand.Value;
    }

    public Task<Dictionary<string, string>> ResolveAsync(VariableContext context)
    {
        var vars = new Dictionary<string, string>
        {
            ["company_name"] = _brand.CompanyName,
            ["login_url"] = _brand.LoginUrl,
            ["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
            ["support_email"] = _brand.SupportEmail,
        };

        return Task.FromResult(vars);
    }
}
