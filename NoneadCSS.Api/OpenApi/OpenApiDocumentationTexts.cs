using Microsoft.OpenApi;

namespace NoneadCSS.Api.OpenApi;

/// <summary>
/// OpenAPI 文档头部（info 段）的中英文文案集中在这里，供 <c>AddOpenApi</c> 的文档转换器调用。
/// 接口级的 summary/description 不在这里，它们由 <see cref="OpenApiEnglishLocalizer"/> 按词典
/// (<see cref="OpenApiEnglishLocalizer.DictionaryRelativePath"/>) 从中文翻译为英文。
/// 文案以 <c>\n</c> 分行（与 JSON 输出保持一致），Markdown 由 Scalar 渲染。
/// </summary>
public static class OpenApiDocumentationTexts
{
    /// <summary>文档版本（中英文档共用）。</summary>
    public const string DocumentVersion = "1.0.0";

    /// <summary>联系人名称。刻意不带 URL：开源仓库不应保留内部站点占位地址。</summary>
    public const string ContactName = "NoneadCSS";

    /// <summary>开源许可证（中英文档共用）。</summary>
    public static OpenApiLicense License => new()
    {
        Name = "AGPL-3.0",
        Url = new Uri("https://www.gnu.org/licenses/agpl-3.0.html"),
    };

    public static void ApplyChineseInfo(OpenApiDocument document)
    {
        document.Info = new OpenApiInfo
        {
            Title = "NoneadCSS API - 拓德客户服务与支持系统",
            Version = DocumentVersion,
            Description = "拓德客户服务与支持系统 RESTful API 文档\n\n" +
                "## 快速开始\n" +
                "1. 调用 `POST /api/auth/login` 使用管理员账号登录\n" +
                "2. 复制返回的 `token` 值\n" +
                "3. 点击右上角「Authentication」粘贴 Token 进行认证\n" +
                "4. 调用其他需要认证的接口\n\n" +
                "## 响应格式\n" +
                "所有 JSON 接口返回统一包装格式：`{ success, code, data, message }`，业务数据在 `data` 字段中。\n" +
                "文件下载、图片预览等非 JSON 接口直接返回原始内容。\n\n" +
                "## 国际化\n" +
                "所有接口支持通过 `?lang=zh-Hans` 或 `?lang=en` 切换响应消息语言，默认简体中文。",
            Contact = new OpenApiContact { Name = ContactName },
            License = License,
        };
    }

    public static void ApplyEnglishInfo(OpenApiDocument document)
    {
        document.Info = new OpenApiInfo
        {
            Title = "NoneadCSS API - Tuode Customer Service and Support System",
            Version = DocumentVersion,
            Description = "RESTful API documentation for the Tuode Customer Service and Support System\n\n" +
                "## Quick Start\n" +
                "1. Call `POST /api/auth/login` to sign in with an administrator account\n" +
                "2. Copy the `token` value returned\n" +
                "3. Click \"Authentication\" in the top-right corner and paste the token to authenticate\n" +
                "4. Call other endpoints that require authentication\n\n" +
                "## Response Format\n" +
                "All JSON endpoints return a unified wrapper format: `{ success, code, data, message }`, with business data in the `data` field.\n" +
                "Non-JSON endpoints such as file downloads and image previews return raw content directly.\n\n" +
                "## Internationalization\n" +
                "All endpoints support switching the response message language via `?lang=zh-Hans` or `?lang=en`; the default is Simplified Chinese.",
            Contact = new OpenApiContact { Name = ContactName },
            License = License,
        };
    }
}
