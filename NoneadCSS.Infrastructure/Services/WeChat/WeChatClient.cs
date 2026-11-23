using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Infrastructure.Services.WeChat;

/// <summary>
/// 微信 API HTTP 客户端 - 封装客服消息发送、素材下载、用户信息查询等 API 调用。
/// 通过 IHttpClientFactory 注入，所有请求自动附加 access_token。
/// </summary>
public class WeChatClient : IWeChatClient
{
    private readonly HttpClient _httpClient;
    private readonly IWeChatAccessTokenService _tokenService;
    private readonly ILogger<WeChatClient> _logger;

    /// <summary>微信公众平台官方 API 基础地址（直连，无中转网关）</summary>
    private const string DirectApiBase = "https://api.weixin.qq.com";

    // 微信 API 端点路径（相对于 BaseUrl）
    private const string CustomSendPath = "cgi-bin/message/custom/send";
    private const string MediaDownloadPath = "cgi-bin/media/get";
    private const string MediaUploadPath = "cgi-bin/media/upload";
    private const string UserInfoPath = "cgi-bin/user/info";
    private const string TemplateSendPath = "cgi-bin/message/template/send";
    private const string SubscribeSendPath = "cgi-bin/message/subscribe/bizsend";
    private const string MenuCreatePath = "cgi-bin/menu/create";
    private const string MenuGetPath = "cgi-bin/menu/get";
    private const string MenuDeletePath = "cgi-bin/menu/delete";
    private const string GetCurrentSelfMenuPath = "cgi-bin/get_current_selfmenu_info";

    /// <summary>微信 access_token 无效错误码，命中时强制刷新 token 并重试一次</summary>
    private const int ErrCodeAccessTokenInvalid = 40001;
    private const int ErrCodeAccessTokenExpired = 42001;

    /// <summary>各素材类型的大小限制（字节）</summary>
    private static readonly Dictionary<string, long> MediaSizeLimits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image"] = 2 * 1024 * 1024,   // 2MB
        ["voice"] = 2 * 1024 * 1024,   // 2MB
        ["video"] = 10 * 1024 * 1024,  // 10MB
        ["thumb"] = 64 * 1024,         // 64KB
    };

    /// <summary>
    /// JSON 序列化选项：使用 UnicodeRanges.All 编码器，避免中文字符和 emoji 被转义为 \uXXXX。
    /// 注意：UnsafeRelaxedJsonEscaping 会转义 UTF-16 代理对（4 字节 emoji 如 😀 = \uD83D\uDE00），
    /// 导致微信服务号显示乱码。改用 UnicodeRanges.All 确保所有 Unicode 字符（含 emoji）原样输出。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public WeChatClient(
        HttpClient httpClient,
        IOptions<HttpClientOptions> httpClientOptions,
        IWeChatAccessTokenService tokenService,
        ILogger<WeChatClient> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(httpClientOptions.Value.StandardTimeoutSeconds);
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <summary>
    /// 拼接微信公众平台 API 基础地址和端点路径，生成完整的请求 URL：
    /// https://api.weixin.qq.com/cgi-bin/...
    /// </summary>
    private string BuildApiUrl(string path) => $"{DirectApiBase}/{path.TrimStart('/')}";

    /// <summary>
    /// 发送客服文本消息（48 小时客服消息窗口内有效）。
    /// </summary>
    /// <param name="openId">接收者 OpenId</param>
    /// <param name="content">文本内容</param>
    /// <returns>是否发送成功</returns>
    public async Task<bool> SendTextMessageAsync(string openId, string content)
    {
        // 手动构造 JSON，绕过 JsonSerializer 对 emoji（surrogate pair）的转义。
        // System.Text.Json 即使使用 UnsafeRelaxedJsonEscaping 或 UnicodeRanges.All，
        // 仍会把 4 字节 emoji（如 😊 U+1F60A）转义为 "\uD83D\uDE0A" 字面量，
        // 导致微信服务号显示乱码。手动构造确保 emoji 原样输出为 UTF-8 4 字节字符。
        var json = "{\"touser\":\"" + EscapeJsonString(openId) +
                   "\",\"msgtype\":\"text\",\"text\":{\"content\":\"" + EscapeJsonString(content) + "\"}}";

        return await PostWithTokenRetryRawAsync(CustomSendPath, json, "发送客服消息");
    }

    /// <summary>
    /// 发送客服图片消息。
    /// </summary>
    public async Task<bool> SendImageMessageAsync(string openId, string mediaId)
    {
        var payload = new
        {
            touser = openId,
            msgtype = "image",
            image = new { media_id = mediaId }
        };

        return await PostWithTokenRetryAsync(CustomSendPath, payload, "发送客服图片消息");
    }

    /// <summary>
    /// 发送客服语音消息。
    /// </summary>
    public async Task<bool> SendVoiceMessageAsync(string openId, string mediaId)
    {
        var payload = new
        {
            touser = openId,
            msgtype = "voice",
            voice = new { media_id = mediaId }
        };

        return await PostWithTokenRetryAsync(CustomSendPath, payload, "发送客服语音消息");
    }

    /// <summary>
    /// 发送客服视频消息。
    /// </summary>
    public async Task<bool> SendVideoMessageAsync(string openId, string mediaId)
    {
        var payload = new
        {
            touser = openId,
            msgtype = "video",
            video = new { media_id = mediaId }
        };

        return await PostWithTokenRetryAsync(CustomSendPath, payload, "发送客服视频消息");
    }

    /// <summary>
    /// 上传临时素材（图片、语音、视频等），返回 media_id。
    /// 临时素材有效期为 3 天。微信限制：图片 2MB、语音 2MB(amr/mp3)、视频 10MB、缩略图 64KB。
    /// </summary>
    public async Task<string?> UploadMediaAsync(byte[] data, string filename, string mimeType, string mediaType)
    {
        // 服务端文件大小校验（纵深防御，前端已校验但服务端不可缺）
        if (MediaSizeLimits.TryGetValue(mediaType, out var maxSize) && data.Length > maxSize)
        {
            _logger.LogWarning("微信素材上传被拦截：文件大小 {Size} 超过限制 {MaxSize}（类型={Type}, 文件名={Filename}）",
                data.Length, maxSize, mediaType, filename);
            return null;
        }

        // 手动构造 multipart/form-data body（与 token 无关，仅构造一次）
        var safeFilename = System.Text.RegularExpressions.Regex.Replace(filename, @"[^a-zA-Z0-9._\-]", "_");
        if (string.IsNullOrWhiteSpace(safeFilename)) safeFilename = "media.bin";
        var boundary = "----WebKitFormBoundary" + Guid.NewGuid().ToString("N").Substring(0, 16);

        var headerPart = $"--{boundary}\r\n" +
                         $"Content-Disposition: form-data; name=\"media\"; filename=\"{safeFilename}\"\r\n" +
                         $"Content-Type: {(!string.IsNullOrEmpty(mimeType) ? mimeType : "application/octet-stream")}\r\n" +
                         $"\r\n";
        var footerPart = $"\r\n--{boundary}--\r\n";

        var headerBytes = Encoding.UTF8.GetBytes(headerPart);
        var footerBytes = Encoding.UTF8.GetBytes(footerPart);

        var bodyBytes = new byte[headerBytes.Length + data.Length + footerBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, bodyBytes, 0, headerBytes.Length);
        Buffer.BlockCopy(data, 0, bodyBytes, headerBytes.Length, data.Length);
        Buffer.BlockCopy(footerBytes, 0, bodyBytes, headerBytes.Length + data.Length, footerBytes.Length);

        // 局部函数：使用指定 token 执行上传，返回 (mediaId, errCode)
        async Task<(string? mediaId, int errCode)> TryUploadAsync(string accessToken)
        {
            try
            {
                var url = $"{BuildApiUrl(MediaUploadPath)}?access_token={accessToken}&type={Uri.EscapeDataString(mediaType)}";

                using var content = new ByteArrayContent(bodyBytes);
                content.Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");
                content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", boundary));

                _logger.LogInformation("微信素材上传请求：type={Type}, filename={Filename}, safeFilename={SafeFilename}, size={Size}, mimeType={MimeType}, boundary={Boundary}, bodySize={BodySize}",
                    mediaType, filename, safeFilename, data.Length, mimeType, boundary, bodyBytes.Length);

                using var response = await _httpClient.PostAsync(url, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("微信素材上传响应：status={Status}", (int)response.StatusCode);

                JsonElement result;
                try
                {
                    result = JsonSerializer.Deserialize<JsonElement>(responseBody);
                }
                catch (JsonException)
                {
                    _logger.LogError("微信素材上传响应解析失败（非 JSON）：status={Status}, body={Body}, filename={Filename}",
                        (int)response.StatusCode, responseBody, filename);
                    return (null, -1);
                }

                if (result.TryGetProperty("media_id", out var mediaIdProp) && mediaIdProp.ValueKind == JsonValueKind.String)
                {
                    var mediaId = mediaIdProp.GetString();
                    _logger.LogInformation("微信素材上传成功：type={Type}, filename={Filename}, media_id={MediaId}", mediaType, filename, mediaId);
                    return (mediaId, 0);
                }

                var errcode = result.TryGetProperty("errcode", out var ec) ? ec.GetInt32() : -1;
                var errmsg = result.TryGetProperty("errmsg", out var em) ? em.GetString() : "未知错误";
                _logger.LogError("微信素材上传失败：errcode={ErrCode}, errmsg={ErrMsg}, filename={Filename}", errcode, errmsg, filename);
                return (null, errcode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "微信素材上传异常：filename={Filename}", filename);
                return (null, -1);
            }
        }

        // 首次尝试
        var token = await _tokenService.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogError("上传素材失败：access_token 为空");
            return null;
        }

        var (mediaId, errCode) = await TryUploadAsync(token);
        if (mediaId != null) return mediaId;

        // Token 失效时强制刷新并重试一次
        if (errCode == ErrCodeAccessTokenInvalid || errCode == ErrCodeAccessTokenExpired)
        {
            _logger.LogWarning("上传素材：access_token 失效(errcode={ErrCode})，强制刷新后重试", errCode);
            token = await _tokenService.RefreshAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("上传素材重试失败：刷新后 access_token 仍为空");
                return null;
            }
            var (retryMediaId, _) = await TryUploadAsync(token);
            return retryMediaId;
        }

        return null;
    }

    /// <summary>
    /// 下载临时素材（图片、语音、视频等），返回二进制流。
    /// </summary>
    /// <param name="mediaId">媒体文件 ID</param>
    /// <returns>(文件字节, 文件扩展名)；失败返回 null</returns>
    public async Task<(byte[] data, string? extension)?> DownloadMediaAsync(string mediaId)
    {
        // 局部函数：使用指定 token 执行下载，返回 (文件数据, 扩展名, errCode)
        async Task<(byte[]? data, string? extension, int errCode)> TryDownloadAsync(string accessToken)
        {
            var url = $"{BuildApiUrl(MediaDownloadPath)}?access_token={accessToken}&media_id={Uri.EscapeDataString(mediaId)}";
            using var response = await _httpClient.GetAsync(url);

            // 微信素材下载有两种响应：
            // 1. 直接返回二进制文件（Content-Type: image/jpeg 等）
            // 2. 返回 JSON 错误（Content-Type: application/json）
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            if (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            {
                var errorJson = await response.Content.ReadFromJsonAsync<JsonElement>();
                var errcode = errorJson.TryGetProperty("errcode", out var ec) ? ec.GetInt32() : 0;
                var errmsg = errorJson.TryGetProperty("errmsg", out var em) ? em.GetString() : "未知错误";
                _logger.LogError("下载素材失败：errcode={ErrCode}, errmsg={ErrMsg}, media_id={MediaId}", errcode, errmsg, mediaId);
                return (null, null, errcode);
            }

            var data = await response.Content.ReadAsByteArrayAsync();
            var extension = GetExtensionFromContentType(contentType);
            return (data, extension, 0);
        }

        var token = await _tokenService.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogError("下载素材失败：access_token 为空");
            return null;
        }

        var (fileData, extension, errCode) = await TryDownloadAsync(token);
        if (fileData != null) return (fileData, extension);

        // Token 失效时强制刷新并重试一次
        if (errCode == ErrCodeAccessTokenInvalid || errCode == ErrCodeAccessTokenExpired)
        {
            _logger.LogWarning("下载素材：access_token 失效(errcode={ErrCode})，强制刷新后重试", errCode);
            token = await _tokenService.RefreshAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("下载素材重试失败：刷新后 access_token 仍为空");
                return null;
            }
            var (retryData, retryExt, _) = await TryDownloadAsync(token);
            if (retryData != null) return (retryData, retryExt);
        }

        return null;
    }

    /// <summary>
    /// 获取用户基本信息（昵称、头像、性别等）。
    /// </summary>
    /// <param name="openId">用户 OpenId</param>
    /// <returns>用户信息 JSON 元素；失败返回 null</returns>
    public async Task<JsonElement?> GetUserInfoAsync(string openId)
    {
        var token = await _tokenService.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var url = $"{BuildApiUrl(UserInfoPath)}?access_token={token}&openid={Uri.EscapeDataString(openId)}&lang=zh_CN";
        var json = await _httpClient.GetFromJsonAsync<JsonElement>(url);

        if (json.TryGetProperty("errcode", out var ec) && ec.GetInt32() != 0)
        {
            var errmsg = json.TryGetProperty("errmsg", out var em) ? em.GetString() : "未知错误";
            _logger.LogWarning("获取微信用户信息失败：errcode={ErrCode}, errmsg={ErrMsg}, openId={OpenId}", ec.GetInt32(), errmsg, openId);
            return null;
        }

        return json;
    }

    /// <summary>
    /// 从用户信息 JSON 中提取昵称。
    /// </summary>
    public static string? ExtractNickname(JsonElement? userInfo)
    {
        if (userInfo == null || userInfo.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return userInfo.Value.TryGetProperty("nickname", out var nick) && nick.ValueKind == JsonValueKind.String
            ? nick.GetString()
            : null;
    }

    /// <summary>
    /// 发送模板消息（旧版，新注册服务号已停用，建议使用订阅消息）。
    /// </summary>
    public async Task<bool> SendTemplateMessageAsync(string openId, string templateId, object data, string? url = null, object? miniProgram = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["touser"] = openId,
            ["template_id"] = templateId,
            ["data"] = data
        };

        if (!string.IsNullOrEmpty(url))
        {
            payload["url"] = url;
        }

        if (miniProgram != null)
        {
            payload["miniprogram"] = miniProgram;
        }

        return await PostWithTokenRetryAsync(TemplateSendPath, payload, "发送模板消息");
    }

    /// <summary>
    /// 发送订阅通知（新版，替代旧版模板消息）。
    /// 需用户主动订阅后才能下发，不受 48 小时客服消息窗口限制。
    /// </summary>
    public async Task<bool> SendSubscribeMessageAsync(string openId, string templateId, object data, string? page = null, string? miniprogram = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["touser"] = openId,
            ["template_id"] = templateId,
            ["data"] = data
        };

        if (!string.IsNullOrEmpty(page))
        {
            payload["page"] = page;
        }

        if (!string.IsNullOrEmpty(miniprogram))
        {
            payload["miniprogram"] = miniprogram;
        }

        return await PostWithTokenRetryAsync(SubscribeSendPath, payload, "发送订阅通知");
    }

    /// <summary>
    /// 创建自定义菜单。
    /// </summary>
    public async Task<bool> CreateMenuAsync(object menu)
    {
        return await PostWithTokenRetryAsync(MenuCreatePath, menu, "创建菜单");
    }

    /// <summary>
    /// 查询自定义菜单（使用 get_current_selfmenu_info 获取当前生效菜单，更准确）。
    /// </summary>
    public async Task<JsonElement?> GetMenuAsync()
    {
        var token = await _tokenService.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogError("查询菜单失败：access_token 为空");
            return null;
        }

        try
        {
            // 优先使用 get_current_selfmenu_info（返回实际生效的菜单，含个性化菜单匹配结果）
            var url = $"{BuildApiUrl(GetCurrentSelfMenuPath)}?access_token={token}";
            var json = await _httpClient.GetFromJsonAsync<JsonElement>(url);

            // 检查是否为错误响应
            if (json.TryGetProperty("errcode", out var ec) && ec.ValueKind == JsonValueKind.Number && ec.GetInt32() != 0)
            {
                var errmsg = json.TryGetProperty("errmsg", out var em) ? em.GetString() : "未知错误";
                _logger.LogWarning("查询菜单失败：errcode={ErrCode}, errmsg={ErrMsg}", ec.GetInt32(), errmsg);
                return null;
            }

            return json;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询菜单异常");
            return null;
        }
    }

    /// <summary>
    /// 删除自定义菜单。
    /// </summary>
    public async Task<bool> DeleteMenuAsync()
    {
        return await PostWithTokenRetryAsync(MenuDeletePath, new { }, "删除菜单");
    }

    /// <summary>
    /// 带 Token 失效自动重试的 POST 请求（序列化 payload 为 JSON）。
    /// 当微信返回 40001/42001（access_token 无效/过期）时，强制刷新 token 并重试一次。
    /// </summary>
    private async Task<bool> PostWithTokenRetryAsync<T>(string apiPath, T payload, string errorTag)
    {
        var token = await _tokenService.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogError("{Tag}失败：access_token 为空", errorTag);
            return false;
        }

        var url = $"{BuildApiUrl(apiPath)}?access_token={token}";
        var (success, errcode) = await PostWeChatApiAsync(url, payload);
        if (success) return true;

        // Token 失效错误码：40001(access_token 无效) / 42001(access_token 过期)
        if (errcode == ErrCodeAccessTokenInvalid || errcode == ErrCodeAccessTokenExpired)
        {
            _logger.LogWarning("{Tag}：access_token 失效(errcode={ErrCode})，强制刷新后重试", errorTag, errcode);
            token = await _tokenService.RefreshAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("{Tag}重试失败：刷新后 access_token 仍为空", errorTag);
                return false;
            }
            url = $"{BuildApiUrl(apiPath)}?access_token={token}";
            var (retrySuccess, _) = await PostWeChatApiAsync(url, payload);
            return retrySuccess;
        }

        return false;
    }

    /// <summary>
    /// 带 Token 失效自动重试的 POST 请求（发送原始 JSON 字符串，用于 emoji 等场景）。
    /// </summary>
    private async Task<bool> PostWithTokenRetryRawAsync(string apiPath, string json, string errorTag)
    {
        var token = await _tokenService.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogError("{Tag}失败：access_token 为空", errorTag);
            return false;
        }

        var url = $"{BuildApiUrl(apiPath)}?access_token={token}";
        var (success, errcode) = await PostWeChatApiRawAsync(url, json);
        if (success) return true;

        if (errcode == ErrCodeAccessTokenInvalid || errcode == ErrCodeAccessTokenExpired)
        {
            _logger.LogWarning("{Tag}：access_token 失效(errcode={ErrCode})，强制刷新后重试", errorTag, errcode);
            token = await _tokenService.RefreshAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("{Tag}重试失败：刷新后 access_token 仍为空", errorTag);
                return false;
            }
            url = $"{BuildApiUrl(apiPath)}?access_token={token}";
            var (retrySuccess, _) = await PostWeChatApiRawAsync(url, json);
            return retrySuccess;
        }

        return false;
    }

    private async Task<(bool Success, int ErrCode)> PostWeChatApiAsync<T>(string url, T payload)
    {
        try
        {
            // 使用 UnicodeRanges.All 编码器，避免中文/emoji 被转义为 \uXXXX（见 JsonOptions 注释）
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // S3: 请求体含客户消息全文（PII），严禁 Info 级记录；仅 Debug 级记录长度便于排查
            var apiPath = new Uri(url).AbsolutePath;
            _logger.LogDebug("微信 API 请求：api={ApiPath}, bodyLength={BodyLength}", apiPath, json.Length);

            using var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            // S3: 响应体可能含敏感信息，仅 Debug 级记录并截断
            _logger.LogDebug("微信 API 响应：api={ApiPath}, status={Status}, body={Body}",
                apiPath, (int)response.StatusCode, Truncate(responseBody, 300));

            JsonElement result;
            try
            {
                result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            }
            catch (JsonException)
            {
                _logger.LogError("微信 API 响应解析失败：api={ApiPath}", apiPath);
                return (false, -1);
            }

            if (result.TryGetProperty("errcode", out var ec) && ec.GetInt32() == 0)
            {
                return (true, 0);
            }

            var errcode = ec.ValueKind == JsonValueKind.Undefined ? -1 : ec.GetInt32();
            var errmsg = result.TryGetProperty("errmsg", out var em) ? em.GetString() : "未知错误";
            _logger.LogError("微信 API 调用失败：api={ApiPath}, errcode={ErrCode}, errmsg={ErrMsg}", apiPath, errcode, errmsg);
            return (false, errcode);
        }
        catch (Exception ex)
        {
            var apiPath = new Uri(url).AbsolutePath;
            _logger.LogError(ex, "微信 API 调用异常：api={ApiPath}", apiPath);
            return (false, -1);
        }
    }

    /// <summary>
    /// 发送原始 JSON 字符串到微信 API（绕过 JsonSerializer 的自动转义）。
    /// 用于 emoji 等 4 字节字符场景，确保字符原样输出为 UTF-8。
    /// </summary>
    private async Task<(bool Success, int ErrCode)> PostWeChatApiRawAsync(string url, string json)
    {
        try
        {
            // 原始 JSON 直发（保留 emoji 等 4 字节字符），Content-Type 由 StringContent 设定
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // S3: 请求体含客户消息全文（PII），严禁 Info 级记录；仅 Debug 级记录长度便于排查
            var apiPath = new Uri(url).AbsolutePath;
            _logger.LogDebug("微信 API 请求（raw）：api={ApiPath}, bodyLength={BodyLength}", apiPath, json.Length);

            using var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            // S3: 响应体可能含敏感信息，仅 Debug 级记录并截断
            _logger.LogDebug("微信 API 响应（raw）：api={ApiPath}, status={Status}, body={Body}",
                apiPath, (int)response.StatusCode, Truncate(responseBody, 300));

            JsonElement result;
            try
            {
                result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            }
            catch (JsonException)
            {
                _logger.LogError("微信 API 响应解析失败：api={ApiPath}", apiPath);
                return (false, -1);
            }

            if (result.TryGetProperty("errcode", out var ec) && ec.GetInt32() == 0)
            {
                return (true, 0);
            }

            var errcode = ec.ValueKind == JsonValueKind.Undefined ? -1 : ec.GetInt32();
            var errmsg = result.TryGetProperty("errmsg", out var em) ? em.GetString() : "未知错误";
            _logger.LogError("微信 API 调用失败：api={ApiPath}, errcode={ErrCode}, errmsg={ErrMsg}", apiPath, errcode, errmsg);
            return (false, errcode);
        }
        catch (Exception ex)
        {
            var apiPath = new Uri(url).AbsolutePath;
            _logger.LogError(ex, "微信 API 调用异常：api={ApiPath}", apiPath);
            return (false, -1);
        }
    }

    /// <summary>
    /// 手动转义 JSON 字符串中的特殊字符。
    /// 仅转义 JSON 规范要求的特殊字符（引号、反斜杠、控制字符），
    /// 保留 emoji（4 字节 Unicode，UTF-16 代理对）和中文字符原样，
    /// 避免 System.Text.Json 将其转义为 \uXXXX 字面量导致微信端显示乱码。
    /// </summary>
    private static string EscapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length + 2);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    // 控制字符（U+0000 ~ U+001F）必须转义
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        // 普通字符（含中文、emoji 代理对）原样保留，
                        // 后续由 Encoding.UTF8 编码为 UTF-8 字节序列发送
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    private static string GetExtensionFromContentType(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "audio/amr" => ".amr",
            "audio/mpeg" => ".mp3",
            "audio/speex" => ".spx",
            "audio/x-speex" => ".spx",
            "video/mp4" => ".mp4",
            _ => ".bin"
        };
    }

    /// <summary>
    /// 截断日志文本（S3：响应体等可能含敏感信息，记录时截断）
    /// </summary>
    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...(truncated)";
    }
}
