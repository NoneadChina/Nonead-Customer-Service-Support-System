using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.Configuration;

/// <summary>
/// 应用级配置选项（强类型校验）
/// </summary>
public class AppOptions
{
    [Url(ErrorMessage = "App:FrontendUrl 必须是有效的 URL")]
    public string FrontendUrl { get; set; } = string.Empty;

    [Url(ErrorMessage = "App:BaseUrl 必须是有效的 URL")]
    public string BaseUrl { get; set; } = string.Empty;
}
