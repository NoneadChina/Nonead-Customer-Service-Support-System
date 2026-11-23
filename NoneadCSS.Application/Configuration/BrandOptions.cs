using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.Configuration;

/// <summary>
/// 品牌配置 — 统一所有模板中使用的系统级变量来源。
/// 启动时通过 IOptions.ValidateDataAnnotations().ValidateOnStart() 校验失败会直接阻止启动，
/// 避免运行时因为空 FrontendUrl 生成 "/conversations/xxx" 这样错误的相对路径。
/// </summary>
public class BrandOptions : IValidatableObject
{
    public const string SectionName = "Brand";

    [Required(AllowEmptyStrings = false, ErrorMessage = "Brand:CompanyName 不能为空")]
    public string CompanyName { get; set; } = "拓德科技";

    public string LoginUrl { get; set; } = "";

    /// <summary>
    /// 前端站点的公共 URL（不含末尾斜杠）。
    /// 显式配置优先；为空时从 App:FrontendUrl 联动回退（由 Program.cs 的 PostConfigure 填充）。
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Brand:FrontendUrl 未配置，邮件/IM 模板中的外部链接无法生成。请在 appsettings 的 Brand 段设置 FrontendUrl，或通过环境变量 Brand__FrontendUrl 配置。")]
    public string FrontendUrl { get; set; } = "";

    public string SupportEmail { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(FrontendUrl))
        {
            FrontendUrl = FrontendUrl.TrimEnd('/');
        }
        if (!string.IsNullOrWhiteSpace(LoginUrl))
        {
            LoginUrl = LoginUrl.TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(FrontendUrl) &&
            !FrontendUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !FrontendUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult(
                "Brand:FrontendUrl 必须以 http:// 或 https:// 开头（例如 https://css.example.com）",
                new[] { nameof(FrontendUrl) });
        }

        if (!string.IsNullOrWhiteSpace(SupportEmail) &&
            !SupportEmail.Contains('@'))
        {
            yield return new ValidationResult(
                "Brand:SupportEmail 格式不合法",
                new[] { nameof(SupportEmail) });
        }
    }
}
