using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Services.WhatsApp;

/// <summary>
/// WhatsApp Cloud API HTTP 客户端实现。
/// 使用 Bearer Token 鉴权（长期有效，无需刷新），调用 graph.facebook.com 端点。
/// 通过 IHttpClientFactory 注入 HttpClient（避免 DNS 过期和连接耗尽）。
/// </summary>
public class WhatsAppClient : IWhatsAppClient
{
    private readonly HttpClient _httpClient;
    private readonly IWhatsAppSettingsProvider _settingsProvider;
    private readonly ILogger<WhatsAppClient> _logger;

    private const string GraphApiBaseUrl = "https://graph.facebook.com";

    public WhatsAppClient(
        HttpClient httpClient,
        IOptions<HttpClientOptions> httpClientOptions,
        IWhatsAppSettingsProvider settingsProvider,
        ILogger<WhatsAppClient> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(httpClientOptions.Value.StandardTimeoutSeconds);
        _settingsProvider = settingsProvider;
        _logger = logger;
    }

    public async Task<string?> SendTextMessageAsync(string waId, string body, string? contextMessageId = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = waId,
            ["type"] = "text",
            ["text"] = new { body = body }
        };

        if (!string.IsNullOrEmpty(contextMessageId))
        {
            payload["context"] = new { message_id = contextMessageId };
        }

        return await SendMessageAsync(payload);
    }

    public async Task<string?> SendMediaMessageAsync(string waId, string mediaType, string mediaId, string? caption = null, string? filename = null)
    {
        var mediaObj = new Dictionary<string, object> { ["id"] = mediaId };
        if (!string.IsNullOrEmpty(caption) && (mediaType == "image" || mediaType == "video" || mediaType == "document"))
            mediaObj["caption"] = caption;
        if (!string.IsNullOrEmpty(filename) && mediaType == "document")
            mediaObj["filename"] = filename;

        var payload = new Dictionary<string, object>
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = waId,
            ["type"] = mediaType,
            [mediaType] = mediaObj
        };

        return await SendMessageAsync(payload);
    }

    public async Task<string?> SendTemplateMessageAsync(string waId, string templateName, string language, object? components = null)
    {
        var templateObj = new Dictionary<string, object>
        {
            ["name"] = templateName,
            ["language"] = new { code = language }
        };

        if (components != null)
            templateObj["components"] = components;

        var payload = new Dictionary<string, object>
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = waId,
            ["type"] = "template",
            ["template"] = templateObj
        };

        return await SendMessageAsync(payload);
    }

    public async Task<string?> UploadMediaAsync(byte[] data, string filename, string mimeType)
    {
        var settings = _settingsProvider.Current;
        if (string.IsNullOrEmpty(settings.PhoneNumberId))
        {
            _logger.LogError("WhatsApp 上传媒体失败：PhoneNumberId 未配置");
            return null;
        }

        var url = $"{GraphApiBaseUrl}/{settings.ApiVersion}/{settings.PhoneNumberId}/media";
        _logger.LogDebug("WhatsApp 上传媒体请求：Url={Url}, FileName={FileName}, Size={Size} bytes, MimeType={MimeType}",
            url, filename, data.Length, mimeType);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("whatsapp"), "messaging_product");
        multipart.Add(new StringContent(mimeType), "type");

        var fileContent = new ByteArrayContent(data);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        multipart.Add(fileContent, "file", filename);

        request.Content = multipart;

        try
        {
            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("WhatsApp 上传媒体失败：FileName={FileName}, StatusCode={StatusCode}, Response={Body}",
                    filename, (int)response.StatusCode, responseBody);
                return null;
            }

            _logger.LogDebug("WhatsApp 上传媒体成功：FileName={FileName}, StatusCode={StatusCode}", filename, (int)response.StatusCode);

            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("id", out var idEl))
            {
                var mediaId = idEl.GetString();
                _logger.LogDebug("WhatsApp 上传媒体返回 MediaId：FileName={FileName}, MediaId={MediaId}", filename, mediaId);
                return mediaId;
            }

            _logger.LogError("WhatsApp 上传媒体响应缺少 id 字段：FileName={FileName}", filename);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp 上传媒体异常：FileName={FileName}, Size={Size}", filename, data.Length);
            return null;
        }
    }

    public async Task<(byte[] data, string? mimeType)?> DownloadMediaAsync(string mediaUrl)
    {
        var settings = _settingsProvider.Current;
        _logger.LogDebug("WhatsApp 下载媒体请求：Url={Url}", mediaUrl);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("WhatsApp 下载媒体失败：Url={Url}, StatusCode={StatusCode}, Response={Body}",
                    mediaUrl, (int)response.StatusCode, body);
                return null;
            }

            var data = await response.Content.ReadAsByteArrayAsync();
            var mimeType = response.Content.Headers.ContentType?.MediaType;
            _logger.LogDebug("WhatsApp 下载媒体成功：Url={Url}, Size={Size} bytes, MimeType={MimeType}", mediaUrl, data.Length, mimeType);
            return (data, mimeType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp 下载媒体异常：Url={Url}", mediaUrl);
            return null;
        }
    }

    public async Task<bool> MarkAsReadAsync(string messageId)
    {
        var settings = _settingsProvider.Current;
        if (string.IsNullOrEmpty(settings.PhoneNumberId))
        {
            _logger.LogError("WhatsApp 标记已读失败：PhoneNumberId 未配置");
            return false;
        }

        var url = $"{GraphApiBaseUrl}/{settings.ApiVersion}/{settings.PhoneNumberId}/messages";

        var payload = new Dictionary<string, object>
        {
            ["messaging_product"] = "whatsapp",
            ["status"] = "read",
            ["message_id"] = messageId
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
            request.Content = content;

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("WhatsApp 标记已读失败：{StatusCode} - {Body}", response.StatusCode, body);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp 标记已读异常：MessageId={MessageId}", messageId);
            return false;
        }
    }

    /// <summary>
    /// 探测 Graph API 可达性。
    /// 通过 GET {GraphApiBaseUrl}/{ApiVersion}/{PhoneNumberId} 请求 Phone Number 资源，
    /// 返回 200 即代表配置与网络正常，不发送任何消息。
    /// 超时/网络错误/未配置/非 200 均返回 false。
    /// </summary>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsProvider.Current;
        if (string.IsNullOrEmpty(settings.PhoneNumberId) || string.IsNullOrEmpty(settings.AccessToken))
        {
            _logger.LogDebug("WhatsApp 可达性探测跳过：PhoneNumberId 或 AccessToken 未配置");
            return false;
        }

        var url = $"{GraphApiBaseUrl}/{settings.ApiVersion}/{settings.PhoneNumberId}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("WhatsApp 可达性探测返回非 200：Url={Url}, StatusCode={StatusCode}, Response={Body}",
                    url, (int)response.StatusCode, body);
                return false;
            }

            _logger.LogDebug("WhatsApp 可达性探测成功：Url={Url}", url);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WhatsApp 可达性探测超时或被取消：Url={Url}", url);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp 可达性探测异常：Url={Url}", url);
            return false;
        }
    }

    /// <summary>
    /// 发送消息的通用方法，返回 wamid 或 null。
    /// </summary>
    private async Task<string?> SendMessageAsync(Dictionary<string, object> payload)
    {
        var settings = _settingsProvider.Current;
        if (string.IsNullOrEmpty(settings.PhoneNumberId))
        {
            _logger.LogError("WhatsApp 发送消息失败：PhoneNumberId 未配置");
            return null;
        }

        var url = $"{GraphApiBaseUrl}/{settings.ApiVersion}/{settings.PhoneNumberId}/messages";

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var msgType = payload.TryGetValue("type", out var t) ? t.ToString() : "unknown";
        var recipient = payload.TryGetValue("to", out var r) ? r.ToString() : "unknown";
        _logger.LogDebug("WhatsApp API 发送请求：Url={Url}, Type={Type}, To={To}, PayloadLength={Len}", url, msgType, recipient, json.Length);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
            request.Content = content;

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("WhatsApp API 发送失败：Url={Url}, Type={Type}, To={To}, StatusCode={StatusCode}, Response={Body}",
                    url, msgType, recipient, (int)response.StatusCode, responseBody);
                return null;
            }

            _logger.LogDebug("WhatsApp API 发送成功：StatusCode={StatusCode}, ResponseLength={Len}", (int)response.StatusCode, responseBody.Length);

            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("messages", out var messages) &&
                messages.ValueKind == JsonValueKind.Array &&
                messages.GetArrayLength() > 0)
            {
                var firstMsg = messages[0];
                if (firstMsg.TryGetProperty("id", out var idEl))
                {
                    var wamid = idEl.GetString();
                    _logger.LogDebug("WhatsApp API 返回 MessageId：Type={Type}, To={To}, Wamid={Wamid}", msgType, recipient, wamid);
                    return wamid;
                }
            }

            _logger.LogError("WhatsApp API 响应缺少 messages[0].id：Type={Type}, To={To}", msgType, recipient);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp API 发送异常：Url={Url}, Type={Type}, To={To}", url, msgType, recipient);
            return null;
        }
    }
}
