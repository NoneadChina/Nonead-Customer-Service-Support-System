using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Services.Messenger;

/// <summary>
/// Messenger Send API / Graph API HTTP 客户端实现。
/// 使用 Page Access Token 的 Bearer 鉴权（无刷新端点，失效后需人工换发），
/// 版本从 MessengerOptions.ApiVersion 读取（不写死 v24.0/v25.0）。
/// 通过 IHttpClientFactory 注入 HttpClient（避免 DNS 过期和连接耗尽）。
/// Graph 错误对象通过 <see cref="LastError"/> 暴露给上层做限流退避与状态页告警。
/// </summary>
public class MessengerClient : IMessengerClient
{
    private readonly HttpClient _httpClient;
    private readonly IMessengerSettingsProvider _settingsProvider;
    private readonly ILogger<MessengerClient> _logger;

    private const string GraphApiBaseUrl = "https://graph.facebook.com";

    public MessengerClient(
        HttpClient httpClient,
        IOptions<HttpClientOptions> httpClientOptions,
        IMessengerSettingsProvider settingsProvider,
        ILogger<MessengerClient> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(httpClientOptions.Value.StandardTimeoutSeconds);
        _settingsProvider = settingsProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public MessengerApiError? LastError { get; private set; }

    public async Task<string?> SendTextMessageAsync(string psid, string body, string? messageTag = null, string? replyToMid = null)
    {
        var message = new Dictionary<string, object> { ["text"] = body };
        ApplyMessageTag(message, messageTag);
        if (!string.IsNullOrEmpty(replyToMid))
            message["reply_to"] = new { mid = replyToMid };

        var payload = new Dictionary<string, object>
        {
            ["recipient"] = new { id = psid },
            ["message"] = message,
            ["notification_type"] = "REGULAR"
        };

        return await SendMessageAsync(payload, "text", psid);
    }

    public async Task<string?> SendAttachmentAsync(string psid, MessengerAttachmentSpec spec, string? messageTag = null)
    {
        var payloadObj = new Dictionary<string, object>();
        if (string.Equals(spec.Kind, "url", StringComparison.OrdinalIgnoreCase))
        {
            payloadObj["url"] = spec.Payload;
            payloadObj["is_public"] = spec.IsPublic;
        }
        else
        {
            payloadObj["attachment_id"] = spec.Payload;
        }

        var attachment = new Dictionary<string, object>
        {
            // Graph 的 attachment type 是单数：image / audio / video / file。
            ["type"] = string.IsNullOrEmpty(spec.Type) ? "file" : spec.Type,
            ["payload"] = payloadObj
        };

        var message = new Dictionary<string, object> { ["attachment"] = attachment };
        ApplyMessageTag(message, messageTag);

        var payload = new Dictionary<string, object>
        {
            ["recipient"] = new { id = psid },
            ["message"] = message,
            ["notification_type"] = "REGULAR"
        };

        return await SendMessageAsync(payload, "attachment", psid);
    }

    /// <summary>
    /// Attachment Upload API：multipart 上传二进制到 /{ver}/me/message_attachments，
    /// 返回可复用的 attachment_id（约 30 分钟有效，跨会话不可长期缓存）。
    /// </summary>
    public async Task<string?> UploadAttachmentAsync(byte[] data, string mimeType)
    {
        var settings = _settingsProvider.Current;
        if (string.IsNullOrEmpty(settings.PageAccessToken))
        {
            _logger.LogError("Messenger 上传附件失败：PageAccessToken 未配置");
            LastError = new MessengerApiError { Message = "PageAccessToken 未配置" };
            return null;
        }

        var url = $"{GraphApiBaseUrl}/{settings.ApiVersion}/me/message_attachments";
        var attachmentType = MapAttachmentType(mimeType);
        var messageJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["attachment"] = new Dictionary<string, object>
            {
                ["type"] = attachmentType,
                ["payload"] = new { is_reusable = true }
            }
        });

        _logger.LogDebug("Messenger 上传附件请求：Url={Url}, Type={Type}, Size={Size} bytes, MimeType={MimeType}",
            url, attachmentType, data.Length, mimeType);

        try
        {
            using var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent(messageJson, Encoding.UTF8), "message");

            var fileContent = new ByteArrayContent(data);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType);
            multipart.Add(fileContent, "filedata", BuildFileName(mimeType));

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.PageAccessToken);
            request.Content = multipart;

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                LastError = ParseGraphError(responseBody, (int)response.StatusCode);
                _logger.LogError("Messenger 上传附件失败：Type={Type}, StatusCode={StatusCode}, GraphCode={GraphCode}, FbtraceId={Fbtrace}, Message={Msg}",
                    attachmentType, (int)response.StatusCode, LastError.Code, LastError.FbtraceId, LastError.Message);
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var attachmentId = FirstStringValue(doc.RootElement, "attachment_id", "id");
            if (string.IsNullOrEmpty(attachmentId))
            {
                LastError = new MessengerApiError { Message = "响应缺少 attachment_id", HttpStatus = (int)response.StatusCode };
                _logger.LogError("Messenger 上传附件响应缺少 attachment_id：Type={Type}", attachmentType);
                return null;
            }

            LastError = null;
            _logger.LogDebug("Messenger 上传附件成功：Type={Type}, Size={Size}, AttachmentId={AttachmentId}",
                attachmentType, data.Length, attachmentId);
            return attachmentId;
        }
        catch (Exception ex)
        {
            LastError = new MessengerApiError { Message = ex.Message };
            _logger.LogError(ex, "Messenger 上传附件异常：Type={Type}, Size={Size}", attachmentType, data.Length);
            return null;
        }
    }

    public async Task<(byte[] data, string? mimeType)?> DownloadMediaAsync(string mediaUrl)
    {
        var settings = _settingsProvider.Current;
        _logger.LogDebug("Messenger 下载媒体请求：Host={Host}", TryGetHost(mediaUrl));

        try
        {
            // 上限在「读响应体之前」就生效：Content-Length 预检 + 流式累计校验，
            // 避免先 ReadAsByteArrayAsync 把整个大文件读进内存再丢弃（媒体文件可达数百 MB）。
            var maxBytes = (long)settings.MediaDownloadMaxMB * 1024 * 1024;

            // 先按 Authorization 头鉴权；部分 CDN 只认 access_token 查询参数，失败后重试一次
            var result = await TryDownloadAsync(mediaUrl, settings.PageAccessToken, useQueryParam: false, maxBytes);
            if (result == null && !string.IsNullOrEmpty(settings.PageAccessToken))
            {
                result = await TryDownloadAsync(mediaUrl, settings.PageAccessToken, useQueryParam: true, maxBytes);
            }

            if (result is null)
                return null;

            var (data, mimeType) = result.Value;

            _logger.LogDebug("Messenger 下载媒体成功：Host={Host}, Size={Size} bytes, MimeType={MimeType}",
                TryGetHost(mediaUrl), data.Length, mimeType);
            return (data, mimeType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Messenger 下载媒体异常：Host={Host}", TryGetHost(mediaUrl));
            return null;
        }
    }

    private async Task<(byte[] data, string? mimeType)?> TryDownloadAsync(
        string mediaUrl, string? accessToken, bool useQueryParam, long maxBytes)
    {
        var url = useQueryParam && !string.IsNullOrEmpty(accessToken)
            ? AppendQueryParam(mediaUrl, "access_token", accessToken!)
            : mediaUrl;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!useQueryParam && !string.IsNullOrEmpty(accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // ResponseHeadersRead：拿到响应头就返回，不把整个响应体预先缓冲
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            LastError = ParseGraphError(body, (int)response.StatusCode);
            _logger.LogWarning("Messenger 下载媒体失败：Host={Host}, StatusCode={StatusCode}, QueryParam={QueryParam}, BodyLength={Len}",
                TryGetHost(mediaUrl), (int)response.StatusCode, useQueryParam, body.Length);
            return null;
        }

        var declaredLength = response.Content.Headers.ContentLength;
        if (maxBytes > 0 && declaredLength.HasValue && declaredLength.Value > maxBytes)
        {
            _logger.LogWarning("Messenger 下载媒体被丢弃（Content-Length 超限）：Host={Host}, Declared={Declared} bytes, MaxMB={MaxMB}",
                TryGetHost(mediaUrl), declaredLength.Value, maxBytes / 1024 / 1024);
            return null;
        }

        // 没有 Content-Length 时靠流式累计兜底：超过上限立刻中断，不继续读、不保留已读部分
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            total += read;
            if (maxBytes > 0 && total > maxBytes)
            {
                _logger.LogWarning("Messenger 下载媒体被中断（流式超过上限）：Host={Host}, Read={Read} bytes, MaxMB={MaxMB}",
                    TryGetHost(mediaUrl), total, maxBytes / 1024 / 1024);
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read));
        }

        return (buffer.ToArray(), response.Content.Headers.ContentType?.MediaType);
    }

    public async Task<bool> SendSenderActionAsync(string psid, string action)
    {
        var settings = _settingsProvider.Current;
        if (string.IsNullOrEmpty(settings.PageAccessToken))
        {
            _logger.LogDebug("Messenger sender_action 跳过：PageAccessToken 未配置");
            return false;
        }

        var url = $"{GraphApiBaseUrl}/{settings.ApiVersion}/me/messages";
        var payload = new Dictionary<string, object>
        {
            ["recipient"] = new { id = psid },
            ["sender_action"] = action
        };

        try
        {
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.PageAccessToken);
            request.Content = content;

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                // sender_action 失败属于体验优化，不影响消息本身，降级为 Debug 日志
                var body = await response.Content.ReadAsStringAsync();
                LastError = ParseGraphError(body, (int)response.StatusCode);
                _logger.LogDebug("Messenger sender_action 失败：Action={Action}, GraphCode={GraphCode}, Message={Msg}",
                    action, LastError.Code, LastError.Message);
                return false;
            }

            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Messenger sender_action 异常：Action={Action}", action);
            return false;
        }
    }

    public async Task<MessengerCustomerProfile?> GetCustomerProfileAsync(string psid)
    {
        var settings = _settingsProvider.Current;
        if (string.IsNullOrEmpty(settings.PageAccessToken) || string.IsNullOrEmpty(psid))
            return null;

        var fields = "first_name,last_name,profile_pic,locale,timezone,gender";
        var url = $"{GraphApiBaseUrl}/{settings.ApiVersion}/{Uri.EscapeDataString(psid)}?fields={fields}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.PageAccessToken);

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                LastError = ParseGraphError(responseBody, (int)response.StatusCode);
                _logger.LogDebug("Messenger 拉取客户资料失败：GraphCode={GraphCode}, Message={Msg}",
                    LastError.Code, LastError.Message);
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            var profile = new MessengerCustomerProfile
            {
                FirstName = StringOrNull(root, "first_name"),
                LastName = StringOrNull(root, "last_name"),
                ProfilePic = StringOrNull(root, "profile_pic"),
                Locale = StringOrNull(root, "locale"),
                Gender = StringOrNull(root, "gender"),
                TimezoneOffset = root.TryGetProperty("timezone", out var tz) && tz.ValueKind == JsonValueKind.Number ? tz.GetDouble() : (double?)null
            };

            if (string.IsNullOrEmpty(profile.FirstName) && string.IsNullOrEmpty(profile.LastName))
                return null;

            LastError = null;
            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Messenger 拉取客户资料异常");
            return null;
        }
    }

    public async Task<bool> SubscribeFieldsAsync(string? fields = null)
    {
        var settings = _settingsProvider.Current;
        if (string.IsNullOrEmpty(settings.PageAccessToken))
        {
            _logger.LogWarning("Messenger 订阅字段失败：PageAccessToken 未配置");
            LastError = new MessengerApiError { Message = "PageAccessToken 未配置" };
            return false;
        }

        var target = string.IsNullOrWhiteSpace(fields) ? settings.SubscribedFields : fields!;
        var url = $"{GraphApiBaseUrl}/{settings.ApiVersion}/me/subscribed_apps?subscribed_fields={Uri.EscapeDataString(target)}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.PageAccessToken);

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                LastError = ParseGraphError(responseBody, (int)response.StatusCode);
                _logger.LogError("Messenger 订阅字段失败：Fields={Fields}, StatusCode={StatusCode}, GraphCode={GraphCode}, FbtraceId={Fbtrace}, Message={Msg}",
                    target, (int)response.StatusCode, LastError.Code, LastError.FbtraceId, LastError.Message);
                return false;
            }

            LastError = null;
            _logger.LogInformation("Messenger 订阅字段成功：Fields={Fields}", target);
            return true;
        }
        catch (Exception ex)
        {
            LastError = new MessengerApiError { Message = ex.Message };
            _logger.LogError(ex, "Messenger 订阅字段异常：Fields={Fields}", target);
            return false;
        }
    }

    /// <summary>
    /// 探测 Graph 可达性与令牌有效性：GET /{ver}/me?fields=id,name。
    /// 200 即代表配置与网络正常；190 代表 Page Access Token 已失效。
    /// 不发送任何消息（Messenger 无"给商家自己发测试消息"的安全路径，向陌生 PSID 发消息会违规）。
    /// </summary>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsProvider.Current;
        if (string.IsNullOrEmpty(settings.PageAccessToken))
        {
            _logger.LogDebug("Messenger 可达性探测跳过：PageAccessToken 未配置");
            LastError = new MessengerApiError { Message = "PageAccessToken 未配置" };
            return false;
        }

        var url = $"{GraphApiBaseUrl}/{settings.ApiVersion}/me?fields=id,name";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.PageAccessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LastError = ParseGraphError(responseBody, (int)response.StatusCode);
                _logger.LogWarning("Messenger 可达性探测返回非 200：StatusCode={StatusCode}, GraphCode={GraphCode}, Message={Msg}",
                    (int)response.StatusCode, LastError.Code, LastError.Message);
                return false;
            }

            LastError = null;
            _logger.LogDebug("Messenger 可达性探测成功：Url={Url}", url);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Messenger 可达性探测超时或被取消：Url={Url}", url);
            LastError = new MessengerApiError { Message = "探测超时或被取消" };
            throw;
        }
        catch (Exception ex)
        {
            LastError = new MessengerApiError { Message = ex.Message };
            _logger.LogWarning(ex, "Messenger 可达性探测异常：Url={Url}", url);
            return false;
        }
    }

    // ---------------------------------------------------------------- 内部辅助

    private async Task<string?> SendMessageAsync(Dictionary<string, object> payload, string kind, string psid)
    {
        LastError = null;

        var settings = _settingsProvider.Current;
        if (string.IsNullOrEmpty(settings.PageAccessToken))
        {
            _logger.LogError("Messenger 发送失败：PageAccessToken 未配置");
            LastError = new MessengerApiError { Message = "PageAccessToken 未配置" };
            return null;
        }

        // Graph 版本号来自配置（MessengerOptions.ApiVersion），不写死
        var url = $"{GraphApiBaseUrl}/{settings.ApiVersion}/me/messages";
        var json = JsonSerializer.Serialize(payload);

        _logger.LogDebug("Messenger Send API 请求：Url={Url}, Kind={Kind}, To={To}, PayloadLength={Len}", url, kind, psid, json.Length);

        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.PageAccessToken);
            request.Content = content;

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                LastError = ParseGraphError(responseBody, (int)response.StatusCode);
                _logger.LogError("Messenger Send API 失败：Kind={Kind}, To={To}, StatusCode={StatusCode}, GraphCode={GraphCode}, Subcode={Subcode}, FbtraceId={Fbtrace}, Message={Msg}",
                    kind, psid, (int)response.StatusCode, LastError.Code, LastError.Subcode, LastError.FbtraceId, LastError.Message);
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var messageId = FirstStringValue(doc.RootElement, "message_id", "mid");
            if (string.IsNullOrEmpty(messageId))
            {
                LastError = new MessengerApiError { Message = "响应缺少 message_id", HttpStatus = (int)response.StatusCode };
                _logger.LogError("Messenger Send API 响应缺少 message_id：Kind={Kind}, To={To}", kind, psid);
                return null;
            }

            _logger.LogInformation("Messenger Send API 成功：Kind={Kind}, To={To}, MessageId={MessageId}", kind, psid, messageId);
            return messageId;
        }
        catch (Exception ex)
        {
            LastError = new MessengerApiError { Message = ex.Message };
            _logger.LogError(ex, "Messenger Send API 异常：Url={Url}, Kind={Kind}, To={To}", url, kind, psid);
            return null;
        }
    }

    private static void ApplyMessageTag(Dictionary<string, object> message, string? messageTag)
    {
        if (string.IsNullOrWhiteSpace(messageTag))
            return;

        // 客服窗口（24h）外的主动回复必须带 message_tag，HUMAN_AGENT 是唯一免费且允许客服使用的标签
        message["message_tag"] = new { tag = messageTag };
    }

    /// <summary>
    /// 解析 Graph 错误对象：{"error":{"message":...,"code":...,"error_subcode":...,"fbtrace_id":...}}。
    /// 只记录 code/subcode/fbtrace_id/message，不记录令牌或完整响应体（令牌可能出现在 URL 里）。
    /// </summary>
    private static MessengerApiError ParseGraphError(string responseBody, int httpStatus)
    {
        var error = new MessengerApiError { HttpStatus = httpStatus, Message = responseBody };

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                error.Message = StringOrNull(err, "message") ?? responseBody;
                error.Code = IntOrZero(err, "code");
                error.Subcode = IntOrZero(err, "error_subcode");
                error.FbtraceId = StringOrNull(err, "fbtrace_id");
            }
        }
        catch (JsonException)
        {
            // 非 JSON 响应（网关 HTML 错误页等）：保留原始文本片段供排障
            error.Message = responseBody.Length > 300 ? responseBody[..300] : responseBody;
        }

        return error;
    }

    private static string MapAttachmentType(string? mimeType)
    {
        var m = (mimeType ?? string.Empty).ToLowerInvariant();
        if (m.StartsWith("image/")) return "image";
        if (m.StartsWith("audio/")) return "audio";
        if (m.StartsWith("video/")) return "video";
        // Graph 只认单数 "file"；写成 "files" 会让 PDF/文档类附件在上传即被 400 拒绝。
        return "file";
    }

    private static string BuildFileName(string? mimeType)
    {
        var ext = (mimeType ?? string.Empty).ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "audio/mpeg" => ".mp3",
            "audio/mp4" => ".m4a",
            "audio/ogg" => ".ogg",
            "video/mp4" => ".mp4",
            "video/quicktime" => ".mov",
            "application/pdf" => ".pdf",
            _ => ".bin"
        };

        return $"attachment{ext}";
    }

    private static string? TryGetHost(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "(invalid-url)";
    }

    private static string AppendQueryParam(string url, string key, string value)
    {
        var separator = url.Contains('?') ? "&" : "?";
        return $"{url}{separator}{key}={HttpUtility.UrlEncode(value)}";
    }

    private static string? FirstStringValue(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            var value = StringOrNull(root, name);
            if (!string.IsNullOrEmpty(value))
                return value;
        }

        return null;
    }

    private static string? StringOrNull(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static int IntOrZero(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt32(out var value)
            ? value
            : 0;
    }
}
