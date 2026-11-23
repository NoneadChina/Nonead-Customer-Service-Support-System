using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace NoneadCSS.Api.Localization;

/// <summary>
/// 在响应出口点统一取「界面语言」。
///
/// 为什么不直接用 CultureInfo.CurrentUICulture：GlobalExceptionMiddleware 注册在
/// UseRequestLocalization 之前（Program.cs:1166 vs :1173），异常向外冒泡时
/// RequestLocalization 已经把自己的 culture 还原掉，此刻读线程文化会拿到默认中文。
/// IRequestCultureFeature 挂在 HttpContext 上、不会被还原，因此优先读它。
/// </summary>
public static class ApiRequestCulture
{
    /// <summary>取当前请求的 UI 文化名（如 "en" / "zh-Hans"）；拿不到时回落线程文化。</summary>
    public static string? Of(HttpContext? context)
    {
        var fromFeature = context?.Features.Get<IRequestCultureFeature>()?.RequestCulture?.UICulture?.Name;
        return string.IsNullOrWhiteSpace(fromFeature) ? CultureInfo.CurrentUICulture.Name : fromFeature;
    }
}
