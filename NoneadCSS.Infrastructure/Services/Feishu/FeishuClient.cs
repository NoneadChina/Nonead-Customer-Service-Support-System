using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Infrastructure.Services.Feishu;

/// <summary>
/// 飞书 API HTTP 客户端 - 封装消息发送、回复、媒体上传下载、用户信息查询等 API 调用。
/// 通过 IHttpClientFactory 注入，所有请求自动附加 Bearer tenant_access_token。
/// </summary>
public class FeishuClient : IFeishuClient
{
    private readonly HttpClient _httpClient;
    private readonly IFeishuAccessTokenService _tokenService;
    private readonly ILogger<FeishuClient> _logger;

    private const string BaseUrl = "https://open.feishu.cn/open-apis";

    /// <summary>图片上传大小限制：10MB</summary>
    private const long ImageMaxSize = 10 * 1024 * 1024;
    /// <summary>文件上传大小限制：30MB</summary>
    private const long FileMaxSize = 30 * 1024 * 1024;

    public FeishuClient(
        HttpClient httpClient,
        IOptions<HttpClientOptions> httpClientOptions,
        IFeishuAccessTokenService tokenService,
        ILogger<FeishuClient> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(httpClientOptions.Value.StandardTimeoutSeconds);
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <summary>
    /// 发送消息到指定用户（通过 open_id）。
    /// </summary>
    /// <param name="openId">接收者 Open ID</param>
    /// <param name="msgType">消息类型：text / image / file / audio / media / post / interactive</param>
    /// <param name="content">消息内容 JSON 字符串</param>
    /// <returns>飞书消息 ID；失败返回 null</returns>
    public async Task<string?> SendMessageAsync(string openId, string msgType, string content)
    {
        _logger.LogInformation("飞书开始发送消息：OpenId={OpenId}, MsgType={MsgType}, ContentLength={ContentLength}",
            openId, msgType, content?.Length ?? 0);

        var token = await GetTokenAsync("发送飞书消息");
        if (token == null)
        {
            return null;
        }

        var payload = new
        {
            receive_id = openId,
            msg_type = msgType,
            content = content
        };

        var data = await PostFeishuApiAsync("/im/v1/messages?receive_id_type=open_id", payload, token);
        var messageId = ExtractString(data, "message_id");
        if (!string.IsNullOrEmpty(messageId))
        {
            _logger.LogInformation("飞书消息发送成功：OpenId={OpenId}, MsgType={MsgType}, MessageId={MessageId}",
                openId, msgType, messageId);
        }
        else
        {
            _logger.LogWarning("飞书消息发送失败：OpenId={OpenId}, MsgType={MsgType}, Data={Data}",
                openId, msgType, data?.ToString() ?? "null");
        }
        return messageId;
    }

    /// <summary>
    /// 回复消息（在现有消息上回复）。
    /// </summary>
    /// <param name="messageId">被回复的消息 ID</param>
    /// <param name="msgType">消息类型</param>
    /// <param name="content">消息内容 JSON 字符串</param>
    /// <returns>飞书消息 ID；失败返回 null</returns>
    public async Task<string?> ReplyMessageAsync(string messageId, string msgType, string content)
    {
        _logger.LogInformation("飞书开始回复消息：ReplyToMessageId={ReplyToMessageId}, MsgType={MsgType}, ContentLength={ContentLength}",
            messageId, msgType, content?.Length ?? 0);

        var token = await GetTokenAsync("回复飞书消息");
        if (token == null)
        {
            return null;
        }

        var payload = new
        {
            msg_type = msgType,
            content = content
        };

        var data = await PostFeishuApiAsync($"/im/v1/messages/{Uri.EscapeDataString(messageId)}/reply", payload, token);
        var replyMessageId = ExtractString(data, "message_id");
        if (!string.IsNullOrEmpty(replyMessageId))
        {
            _logger.LogInformation("飞书回复消息成功：ReplyToMessageId={ReplyToMessageId}, NewMessageId={NewMessageId}",
                messageId, replyMessageId);
        }
        else
        {
            _logger.LogWarning("飞书回复消息失败：ReplyToMessageId={ReplyToMessageId}, MsgType={MsgType}",
                messageId, msgType);
        }
        return replyMessageId;
    }

    /// <summary>
    /// 上传图片，返回 image_key。
    /// </summary>
    /// <param name="data">图片字节流</param>
    /// <param name="filename">文件名（含扩展名）</param>
    /// <returns>image_key；失败返回 null</returns>
    public async Task<string?> UploadImageAsync(byte[] data, string filename)
    {
        ArgumentNullException.ThrowIfNull(data);

        _logger.LogInformation("飞书开始上传图片：Filename={Filename}, Size={Size}bytes",
            filename, data.Length);

        // 服务端文件大小校验（纵深防御，前端已校验但服务端不可缺）
        if (data.Length > ImageMaxSize)
        {
            _logger.LogWarning("飞书图片上传被拦截：文件大小 {Size} 超过限制 {MaxSize}（文件名={Filename}）",
                data.Length, ImageMaxSize, filename);
            return null;
        }

        var token = await GetTokenAsync("上传飞书图片");
        if (token == null)
        {
            return null;
        }

        try
        {
            const string path = "/im/v1/images";
            using var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent("message"), "image_type");
            var fileContent = new ByteArrayContent(data);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(fileContent, "image", filename);

            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = multipart;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var response = await _httpClient.SendAsync(request);
            sw.Stop();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();

            _logger.LogInformation("飞书图片上传 HTTP 响应：StatusCode={StatusCode}, 耗时={ElapsedMs}ms, Filename={Filename}",
                (int)response.StatusCode, sw.ElapsedMilliseconds, filename);
            return ExtractFromResponse(result, "image_key", path, filename);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "飞书图片上传异常：filename={Filename}", filename);
            return null;
        }
    }

    /// <summary>
    /// 上传文件，返回 file_key。
    /// </summary>
    /// <param name="data">文件字节流</param>
    /// <param name="filename">文件名（含扩展名）</param>
    /// <param name="fileType">文件类型：opus / mp4 / pdf / doc / xls / ppt / stream</param>
    /// <returns>file_key；失败返回 null</returns>
    public async Task<string?> UploadFileAsync(byte[] data, string filename, string fileType)
    {
        ArgumentNullException.ThrowIfNull(data);

        _logger.LogInformation("飞书开始上传文件：Filename={Filename}, FileType={FileType}, Size={Size}bytes",
            filename, fileType, data.Length);

        // 服务端文件大小校验（纵深防御，前端已校验但服务端不可缺）
        if (data.Length > FileMaxSize)
        {
            _logger.LogWarning("飞书文件上传被拦截：文件大小 {Size} 超过限制 {MaxSize}（类型={Type}, 文件名={Filename}）",
                data.Length, FileMaxSize, fileType, filename);
            return null;
        }

        var token = await GetTokenAsync("上传飞书文件");
        if (token == null)
        {
            return null;
        }

        try
        {
            const string path = "/im/v1/files";
            using var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent(fileType), "file_type");
            multipart.Add(new StringContent(filename), "file_name");
            var fileContent = new ByteArrayContent(data);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(fileContent, "file", filename);

            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = multipart;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var response = await _httpClient.SendAsync(request);
            sw.Stop();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();

            _logger.LogInformation("飞书文件上传 HTTP 响应：StatusCode={StatusCode}, 耗时={ElapsedMs}ms, Filename={Filename}",
                (int)response.StatusCode, sw.ElapsedMilliseconds, filename);
            return ExtractFromResponse(result, "file_key", path, filename);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "飞书文件上传异常：filename={Filename}", filename);
            return null;
        }
    }

    /// <summary>
    /// 下载消息中的媒体资源（图片/文件）。
    /// </summary>
    /// <param name="messageId">飞书消息 ID（用于构造下载路径）</param>
    /// <param name="fileKey">文件 key（image_key 或 file_key）</param>
    /// <returns>二进制数据和文件扩展名；失败返回 null</returns>
    public async Task<(byte[] data, string? extension)?> DownloadMediaAsync(string messageId, string fileKey)
    {
        _logger.LogInformation("飞书开始下载媒体：MessageId={MessageId}, FileKey={FileKey}", messageId, fileKey);

        var token = await GetTokenAsync("下载飞书媒体");
        if (token == null)
        {
            return null;
        }

        var path = $"/im/v1/messages/{Uri.EscapeDataString(messageId)}/resources/{Uri.EscapeDataString(fileKey)}?type=file";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var response = await _httpClient.SendAsync(request);
            sw.Stop();

            // 飞书素材下载有两种响应：
            // 1. 直接返回二进制文件（Content-Type: application/octet-stream 等）
            // 2. 返回 JSON 错误（Content-Type: application/json）
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            _logger.LogInformation("飞书媒体下载 HTTP 响应：StatusCode={StatusCode}, ContentType={ContentType}, 耗时={ElapsedMs}ms",
                (int)response.StatusCode, contentType, sw.ElapsedMilliseconds);

            if (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            {
                var errorJson = await response.Content.ReadFromJsonAsync<JsonElement>();
                var code = errorJson.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : -1;
                var msg = errorJson.TryGetProperty("msg", out var m) ? m.GetString() : "未知错误";
                _logger.LogError("下载飞书媒体失败：api={ApiPath}, code={Code}, msg={Msg}", path, code, msg);
                return null;
            }

            var data = await response.Content.ReadAsByteArrayAsync();
            var extension = GetExtensionFromResponse(response, contentType);
            _logger.LogInformation("飞书媒体下载成功：MessageId={MessageId}, FileKey={FileKey}, Size={Size}bytes, Extension={Extension}",
                messageId, fileKey, data.Length, extension);
            return (data, extension);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下载飞书媒体异常：api={ApiPath}", path);
            return null;
        }
    }

    /// <summary>
    /// 获取用户信息。
    /// </summary>
    /// <param name="openId">用户 Open ID</param>
    /// <returns>用户信息 JSON；失败返回 null</returns>
    public async Task<JsonElement?> GetUserInfoAsync(string openId)
    {
        var token = await GetTokenAsync("获取飞书用户信息");
        if (token == null)
        {
            return null;
        }

        return await GetFeishuApiAsync($"/contact/v3/users/{Uri.EscapeDataString(openId)}?user_id_type=open_id", token);
    }

    /// <summary>
    /// 获取 tenant_access_token，失败时记录日志并返回 null。
    /// </summary>
    private async Task<string?> GetTokenAsync(string operation)
    {
        _logger.LogDebug("飞书 {Operation} 准备获取 token", operation);
        var token = await _tokenService.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogError("{Operation}失败：tenant_access_token 为空（请检查飞书配置、网络连通性或 AppId/AppSecret 是否正确）", operation);
            return null;
        }
        _logger.LogDebug("飞书 {Operation} 获取 token 成功：TokenLength={TokenLength}", operation, token.Length);
        return token;
    }

    /// <summary>
    /// 发送 JSON POST 请求，校验 code 后返回 data 节点。
    /// </summary>
    private async Task<JsonElement?> PostFeishuApiAsync(string path, object payload, string token)
    {
        try
        {
            var url = BaseUrl + path;
            var json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var response = await _httpClient.SendAsync(request);
            sw.Stop();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();

            _logger.LogDebug("飞书 POST API 响应：api={ApiPath}, StatusCode={StatusCode}, 耗时={ElapsedMs}ms",
                path, (int)response.StatusCode, sw.ElapsedMilliseconds);

            if (result.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.Number && codeProp.GetInt32() == 0)
            {
                if (result.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Object)
                {
                    return dataProp;
                }
                _logger.LogWarning("飞书 API 响应 code=0 但缺少 data 节点：api={ApiPath}", path);
                return null;
            }

            var code = codeProp.ValueKind == JsonValueKind.Number ? codeProp.GetInt32() : -1;
            var msg = result.TryGetProperty("msg", out var msgProp) ? msgProp.GetString() : "未知错误";
            // 日志中不打印完整 URL（含 token），仅打印 API 端点路径
            _logger.LogError("飞书 API 调用失败：api={ApiPath}, code={Code}, msg={Msg}, StatusCode={StatusCode}",
                path, code, msg, (int)response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "飞书 API 调用异常：api={ApiPath}", path);
            return null;
        }
    }

    /// <summary>
    /// 发送 GET 请求，校验 code 后返回 data 节点。
    /// </summary>
    private async Task<JsonElement?> GetFeishuApiAsync(string path, string token)
    {
        try
        {
            var url = BaseUrl + path;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var response = await _httpClient.SendAsync(request);
            sw.Stop();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();

            _logger.LogDebug("飞书 GET API 响应：api={ApiPath}, StatusCode={StatusCode}, 耗时={ElapsedMs}ms",
                path, (int)response.StatusCode, sw.ElapsedMilliseconds);

            if (result.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.Number && codeProp.GetInt32() == 0)
            {
                if (result.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Object)
                {
                    return dataProp;
                }
                _logger.LogWarning("飞书 API 响应 code=0 但缺少 data 节点：api={ApiPath}", path);
                return null;
            }

            var code = codeProp.ValueKind == JsonValueKind.Number ? codeProp.GetInt32() : -1;
            var msg = result.TryGetProperty("msg", out var msgProp) ? msgProp.GetString() : "未知错误";
            _logger.LogError("飞书 API 调用失败：api={ApiPath}, code={Code}, msg={Msg}, StatusCode={StatusCode}",
                path, code, msg, (int)response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "飞书 API 调用异常：api={ApiPath}", path);
            return null;
        }
    }

    /// <summary>
    /// 从飞书响应的 data 节点中提取字符串字段。
    /// </summary>
    private static string? ExtractString(JsonElement? data, string fieldName)
    {
        if (data == null || data.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return data.Value.TryGetProperty(fieldName, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString()
            : null;
    }

    /// <summary>
    /// 从飞书完整响应中校验 code 并提取 data 中的指定字符串字段（用于 multipart 上传响应）。
    /// </summary>
    private string? ExtractFromResponse(JsonElement result, string fieldName, string apiPath, string? context = null)
    {
        if (result.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.Number && codeProp.GetInt32() == 0)
        {
            if (result.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Object)
            {
                if (dataProp.TryGetProperty(fieldName, out var fieldProp) && fieldProp.ValueKind == JsonValueKind.String)
                {
                    return fieldProp.GetString();
                }
                _logger.LogWarning("飞书 API 响应缺少字段 {Field}：api={ApiPath}, context={Context}", fieldName, apiPath, context);
                return null;
            }
            return null;
        }

        var code = codeProp.ValueKind == JsonValueKind.Number ? codeProp.GetInt32() : -1;
        var msg = result.TryGetProperty("msg", out var msgProp) ? msgProp.GetString() : "未知错误";
        _logger.LogError("飞书 API 调用失败：api={ApiPath}, code={Code}, msg={Msg}, context={Context}", apiPath, code, msg, context);
        return null;
    }

    /// <summary>
    /// 从响应中提取文件扩展名：优先 Content-Disposition 文件名，回退到 Content-Type。
    /// </summary>
    private static string GetExtensionFromResponse(HttpResponseMessage response, string contentType)
    {
        // 优先从 Content-Disposition 文件名提取扩展名（飞书下载响应通常携带）
        var disposition = response.Content.Headers.ContentDisposition;
        if (disposition != null)
        {
            var fileName = !string.IsNullOrEmpty(disposition.FileNameStar)
                ? disposition.FileNameStar
                : disposition.FileName;
            if (!string.IsNullOrEmpty(fileName))
            {
                var name = fileName.Trim('"');
                var dot = name.LastIndexOf('.');
                if (dot >= 0 && dot < name.Length - 1)
                {
                    return name.Substring(dot);
                }
            }
        }

        // 回退到 Content-Type 推断
        return GetExtensionFromContentType(contentType);
    }

    private static string GetExtensionFromContentType(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/webp" => ".webp",
            "audio/amr" => ".amr",
            "audio/mpeg" => ".mp3",
            "audio/opus" => ".opus",
            "video/mp4" => ".mp4",
            "application/pdf" => ".pdf",
            _ => ".bin"
        };
    }
}
