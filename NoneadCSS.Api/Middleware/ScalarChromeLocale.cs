using System.Text.Json;

namespace NoneadCSS.Api.Middleware;

/// <summary>
/// Scalar 界面 chrome 文案的英中对照表（仅用于 /docs 页面在 DOM 层做界面汉化）。
///
/// 数据来源（不是猜的）：Scalar.AspNetCore 2.0.7 内嵌的 @scalar/api-reference 1.25.111 打包产物
/// （<c>%USERPROFILE%\.nuget\packages\scalar.aspnetcore\2.0.7\lib\net9.0\Scalar.AspNetCore.dll</c>，
/// 以单字节形式内嵌，bundle 区从偏移 159674 起，约 2.7 MB）。
/// 通过脚本扫描该 bundle 中处于 UI 上下文（label: / placeholder: / title: / aria-label: / 文本节点）里的英文字面量，
/// 逐条核对后再翻译；Scalar 本身没有任何 locale/语言选项，因此只能这样在 DOM 层翻译。
/// 分组注释里标 [推断] 的条目在 bundle 中没有作为独立可见标签出现（只出现在组件名 / CSS 键 / 内部枚举里），
/// 保留它们只是为了在 Scalar 后续版本出现该标签时自动生效——匹配不到就是无操作，不会误伤正文。
/// 详见 ScalarCustomizationMiddleware 的注入逻辑与 NoneadCSS.Tests/Infrastructure/ScalarDocsLocalizationTests.cs。
/// </summary>
internal static class ScalarChromeLocale
{
    /// <summary>英文 chrome 标签 → 简体中文。键必须与 Scalar 打包产物里的字面量完全一致（区分大小写）。</summary>
    internal static IReadOnlyDictionary<string, string> ZhCn { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // 侧边栏 / 搜索（bundle 中确认：placeholder:"Search..."、aria-label:"Reference Search"、label:"Clear Search"）
        ["Search..."] = "搜索…",
        ["Search"] = "搜索",
        ["Clear Search"] = "清除搜索",
        ["Reference Search"] = "文档搜索",
        ["Reference Search Results"] = "文档搜索结果",
        ["Search Results"] = "搜索结果",
        ["Filter Sections"] = "筛选章节",
        ["Open Menu"] = "打开菜单",
        ["Close Menu"] = "关闭菜单",
        ["Close toast"] = "关闭提示",
        ["Introduction"] = "简介", // [推断] bundle 里只作为组件名 Introduction 出现
        ["Documentation"] = "文档", // [推断]
        ["Endpoints"] = "接口",
        ["Models"] = "模型",
        ["Webhooks"] = "Webhook",

        // 请求按钮 / 客户端面板（bundle 中确认：可见 "Send" + sr-only " Send Request "）
        ["Send"] = "发送",
        ["Send Request"] = "发送请求",
        ["Test Request"] = "测试请求",
        ["Request Name"] = "请求名称",
        ["Add Request"] = "添加请求",
        ["New Request"] = "新建请求",
        ["Create Request"] = "创建请求",
        ["Import Request"] = "导入请求",
        ["Request History"] = "请求历史",
        ["Client Libraries"] = "客户端代码库",
        ["Select Client Library"] = "选择客户端库",
        ["Close Client"] = "关闭客户端",
        ["Open API Client"] = "打开 API 客户端",
        ["Selected HTTP client:"] = "已选 HTTP 客户端：",
        ["Copy content"] = "复制内容",
        ["Copy endpoint URL"] = "复制接口地址",
        ["Download"] = "下载",
        ["Raw"] = "原文",
        ["Preview"] = "预览",
        ["Preview unavailable"] = "预览不可用",
        ["CORS Proxy"] = "CORS 代理",
        ["Skip the proxy"] = "跳过代理",

        // 参数 / 请求体（bundle 中确认：mi("Query Parameters")、mi("Path Parameters")、yge," No Body ")
        ["Query Parameters"] = "查询参数",
        ["Path Parameters"] = "路径参数",
        ["Headers"] = "标头",
        ["Show Headers"] = "显示标头",
        ["Hide Headers"] = "隐藏标头",
        ["Cookies"] = "Cookie",
        ["Add Cookie"] = "添加 Cookie",
        ["Edit Cookie"] = "编辑 Cookie",
        ["Body"] = "正文",
        ["No Body"] = "无请求正文",
        ["Payload"] = "请求负载",
        ["Binary file"] = "二进制文件",
        ["Upload File"] = "上传文件",

        // 响应（bundle 中确认：sr-only "Response Body" 紧邻可见 "Download"、"Response Information, Duration:"）
        ["Response"] = "响应",
        ["Responses"] = "响应",
        ["Response Body"] = "响应正文",
        ["Response Information, Duration:"] = "响应信息，耗时：",
        ["Status"] = "状态", // [推断]
        ["Duration"] = "耗时", // [推断]
        ["Size"] = "大小", // [推断]

        // 示例 / 结构（bundle 中确认："Selected Example:"、"Selected Example Values:"、"Show Example"、"Show Schema"）
        ["Example"] = "示例",
        ["Examples"] = "示例",
        ["Example:"] = "示例：",
        ["Selected Example:"] = "已选示例：",
        ["Selected Example Values:"] = "已选示例值：",
        ["Show Example"] = "显示示例",
        ["Add Example"] = "添加示例",
        ["Schema"] = "数据模型", // [推断]：可见标签只有 "Show Schema"
        ["Show Schema"] = "显示数据模型",
        ["Show More"] = "显示更多",
        ["Show"] = "显示",
        ["Hide"] = "隐藏",
        ["Required"] = "必填", // 注意：Scalar 的 "Required" 由 CSS ::after 生成，文本节点走不到，见中间件的 <style> 覆盖
        ["Deprecated"] = "已弃用",

        // 服务器 / 认证（bundle 中确认："Servers"、"Add Server"、"Auth Type"、"Add new authentication"）
        ["Servers"] = "服务器",
        ["Server"] = "服务器",
        ["Server:"] = "服务器：",
        ["Add Server"] = "添加服务器",
        ["Base URL"] = "基础地址",
        ["Authentication"] = "认证",
        ["Add new authentication"] = "添加认证方式",
        ["Available authentication"] = "可用的认证方式",
        ["Required authentication"] = "必需的认证方式",
        ["Auth Type"] = "认证类型",
        ["Authorize"] = "授权",
        ["Access Token"] = "访问令牌",
        ["Bearer Token"] = "Bearer 令牌",
        ["API Key in Headers"] = "请求头中的 API 密钥",
        ["API Key in Query Params"] = "查询参数中的 API 密钥",
        ["API Key in Cookies"] = "Cookie 中的 API 密钥",
        ["Client ID"] = "客户端 ID",
        ["Client Secret"] = "客户端密钥",
        ["Token URL"] = "令牌地址",
        ["Auth URL"] = "授权地址",
        ["Redirect URL"] = "重定向地址",
        ["Username"] = "用户名",
        ["Password"] = "密码",
        ["Delete Security Scheme"] = "删除安全方案",

        // 外观与设置（bundle 中确认：mi(" Dark Mode Always ")、"System Preference (default)"、"Appearance"）
        ["Appearance"] = "外观",
        ["Themes"] = "主题",
        ["Framework Themes"] = "框架主题",
        ["Dark Mode Always"] = "始终深色模式",
        ["Light Mode Always"] = "始终浅色模式",
        ["System Preference (default)"] = "跟随系统（默认）",
        ["Settings"] = "设置", // [推断]：bundle 里仅出现在图标名与枚举键
        ["Loading"] = "加载中", // [推断]

        // 集合 / 环境 / 草稿（API 客户端侧功能，默认可见性有限，翻译代价极低）
        ["Collections"] = "集合",
        ["Collection"] = "集合",
        ["Select Collection"] = "选择集合",
        ["Create Collection"] = "创建集合",
        ["Import Collection"] = "导入集合",
        ["Drafts"] = "草稿",
        ["Clear Drafts"] = "清除草稿",
        ["Variables"] = "变量",
        ["Add Variable"] = "添加变量",
        ["Manage Environments"] = "管理环境",
        ["Add Environment"] = "添加环境",
        ["No Environment"] = "无环境",
        ["Environment name"] = "环境名称",
        ["Create Workspace"] = "创建工作区",
        ["Rename Workspace"] = "重命名工作区",
        ["Delete workspace"] = "删除工作区",

        // 通用动作与兜底文案（bundle 中确认："Cancel"、"Rename"、"Oops, something went wrong here."）
        ["Cancel"] = "取消",
        ["Delete"] = "删除",
        ["Rename"] = "重命名",
        ["Edit"] = "编辑",
        ["Clear"] = "清除",
        ["Add Item"] = "添加条目",
        ["Add value"] = "添加值",
        ["Continue"] = "继续",
        ["Get Started"] = "开始使用",
        ["No content found"] = "未找到内容",
        ["Body not found"] = "未找到正文",
        ["Oops, something went wrong here."] = "抱歉，这里出现了错误。",
        ["Unknown"] = "未知",
        ["Select Type"] = "选择类型",
        ["Select an icon"] = "选择图标",
        ["Name"] = "名称",
        ["Value"] = "值",
        ["Key"] = "键",
    };

    /// <summary>把对照表序列化成注入用的 JS 对象字面量（紧凑、UTF-8 原样输出中文，便于控制注入体积）。</summary>
    internal static string ToJavaScriptObjectLiteral()
    {
        // UnsafeRelaxedJsonEscaping 不转义 < / >，因此这里显式把 "</" 打散，杜绝 </script> 提前闭合注入的脚本。
        var json = JsonSerializer.Serialize(ZhCn, JsonOptions);
        return json.Replace("</", "<\\/", StringComparison.Ordinal);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };
}
