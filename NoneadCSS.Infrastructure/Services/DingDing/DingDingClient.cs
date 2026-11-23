using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Services.DingDing;

/// <summary>
/// 钉钉开放平台 HTTP 客户端实现。
/// 新版 v1.0 API 鉴权：Header x-acs-dingtalk-access-token；旧版 oapi 鉴权：query access_token。
/// Access Token 缓存 110 分钟（有效期 7200 秒，提前 10 分钟刷新）。
/// 通过 IHttpClientFactory 注入 HttpClient。
/// </summary>
public class DingDingClient : IDingDingClient
{
    private readonly HttpClient _httpClient;
    private readonly IDingDingSettingsProvider _settingsProvider;
    private readonly ILogger<DingDingClient> _logger;
    // 信号量锁：最多持有 30 秒（配合 _httpClient.Timeout），避免 HTTP 卡死时永久持有锁
    private const int TokenSemaphoreTimeoutSeconds = 30;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);

    private string? _cachedAccessToken;
    private DateTime _tokenExpiresAt;

    // 新版 v1.0 API
    private const string ApiBaseUrl = "https://api.dingtalk.com";
    // 旧版 oapi
    private const string OapiBaseUrl = "https://oapi.dingtalk.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DingDingClient(
        HttpClient httpClient,
        IOptions<HttpClientOptions> httpClientOptions,
        IDingDingSettingsProvider settingsProvider,
        ILogger<DingDingClient> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(httpClientOptions.Value.StandardTimeoutSeconds);
        _settingsProvider = settingsProvider;
        _logger = logger;
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // 快速路径：缓存有效
        if (!string.IsNullOrEmpty(_cachedAccessToken) && DateTime.UtcNow < _tokenExpiresAt)
            return _cachedAccessToken;

        await _tokenSemaphore.WaitAsync(cancellationToken);
        try
        {
            // 双重检查
            if (!string.IsNullOrEmpty(_cachedAccessToken) && DateTime.UtcNow < _tokenExpiresAt)
                return _cachedAccessToken;

            var settings = _settingsProvider.Current;
            if (string.IsNullOrEmpty(settings.AppKey) || string.IsNullOrEmpty(settings.AppSecret))
            {
                _logger.LogWarning("钉钉 AppKey/AppSecret 未配置，无法获取 Access Token");
                return null;
            }

            var payload = JsonSerializer.Serialize(new
            {
                appKey = settings.AppKey,
                appSecret = settings.AppSecret
            });

            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            // 传入 CancellationToken：超时时底层 HTTP 请求会被取消，避免锁被长期持有
            var response = await _httpClient.PostAsync($"{ApiBaseUrl}/v1.0/oauth2/accessToken", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 错误日志不记录响应体（钉钉 5xx/4xx 响应有可能不慎带上 token 片段），仅记录状态码与错误类型名
                _logger.LogError("钉钉获取 Access Token 失败：StatusCode={StatusCode}, ErrorType={ErrorType}",
                    (int)response.StatusCode, "DingDingAccessTokenHttpError");
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseBody);
            var accessToken = doc.RootElement.GetProperty("accessToken").GetString();
            var expireIn = doc.RootElement.GetProperty("expireIn").GetInt32();

            _cachedAccessToken = accessToken;
            _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expireIn - 600); // 提前 10 分钟过期

            _logger.LogInformation("钉钉 Access Token 获取成功，有效期 {ExpireIn}s", expireIn);
            return _cachedAccessToken;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("钉钉获取 Access Token 已取消（超时或调用方取消）");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉获取 Access Token 异常");
            return null;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    public async Task<bool> SendBySessionWebhookAsync(string sessionWebhook, string msgType, object payload)
    {
        if (string.IsNullOrEmpty(sessionWebhook))
        {
            _logger.LogWarning("钉钉 sessionWebhook 为空，无法发送消息");
            return false;
        }

        try
        {
            // 钉钉 sessionWebhook 期望的 body 格式：
            // {"msgtype":"text","text":{"content":"..."}}
            // msgType 作为外层 msgtype 字段值，payload 直接作为 msgType 对应键的值
            var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
            var body = $"{{\"msgtype\":\"{msgType}\",\"{msgType}\":{payloadJson}}}";

            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(sessionWebhook, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("钉钉 sessionWebhook 发送失败：StatusCode={StatusCode}", (int)response.StatusCode);
                return false;
            }

            var respBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respBody);
            if (doc.RootElement.TryGetProperty("errcode", out var errcodeEl) && errcodeEl.GetInt32() != 0)
            {
                var errmsg = doc.RootElement.TryGetProperty("errmsg", out var msgEl) ? msgEl.GetString() : "unknown";
                _logger.LogError("钉钉 sessionWebhook 发送业务错误：errcode={ErrCode} errmsg={ErrMsg}", errcodeEl.GetInt32(), errmsg);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉 sessionWebhook 发送异常");
            return false;
        }
    }

    public async Task<string?> BatchSendOtoMessageAsync(string robotCode, string[] userIds, string msgKey, string content)
    {
        var accessToken = await GetAccessTokenAsync();
        if (string.IsNullOrEmpty(accessToken))
            return null;

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                robotCode,
                userIds = userIds,
                msgKey = msgKey,
                msgParam = content
            });

            _logger.LogInformation("钉钉批量发送单聊消息：robotCode={RobotCode}, userIds={UserIds}, msgKey={MsgKey}, msgParam={MsgParam}",
                robotCode, string.Join(",", userIds), msgKey, content);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/v1.0/robot/oToMessages/batchSend")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-acs-dingtalk-access-token", accessToken);

            var response = await _httpClient.SendAsync(request);
            var respBody = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("钉钉批量发送单聊消息响应：StatusCode={StatusCode}, Body={Body}", response.StatusCode, respBody);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("钉钉批量发送单聊消息失败：{StatusCode} {Body}", response.StatusCode, respBody);
                return null;
            }

            using var doc = JsonDocument.Parse(respBody);
            if (doc.RootElement.TryGetProperty("processQueryKey", out var keyEl))
                return keyEl.GetString();

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉批量发送单聊消息异常");
            return null;
        }
    }

    public async Task<string?> UploadMediaAsync(byte[] data, string filename, string mimeType)
    {
        var accessToken = await GetAccessTokenAsync();
        if (string.IsNullOrEmpty(accessToken))
            return null;

        // 钉钉 oapi 媒体上传接口必须传 type 参数：
        //   image = 图片（jpg/png/gif 等）
        //   voice = 语音（amr/mp3/aac/wav/ogg）
        //   video = 视频（mp4/mov/avi）
        //   file  = 其他文件（pdf/xlsx/docx/zip 等）
        // 未传 type 会返回 errcode=40004 "不合法的媒体文件类型"
        var type = InferMediaType(filename, mimeType);

        try
        {
            using var multipart = new MultipartFormDataContent();
            multipart.Add(new ByteArrayContent(data), "media", filename);

            var url = $"{OapiBaseUrl}/media/upload?access_token={HttpUtility.UrlEncode(accessToken)}&type={HttpUtility.UrlEncode(type)}";
            var response = await _httpClient.PostAsync(url, multipart);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("钉钉上传媒体文件失败：StatusCode={StatusCode}, MediaType={MediaType}", (int)response.StatusCode, type);
                return null;
            }

            var respBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respBody);

            if (doc.RootElement.TryGetProperty("errcode", out var errcodeEl) && errcodeEl.GetInt32() != 0)
            {
                var errmsg = doc.RootElement.TryGetProperty("errmsg", out var msgEl) ? msgEl.GetString() : "unknown";
                _logger.LogError("钉钉上传媒体文件业务错误：errcode={ErrCode} errmsg={ErrMsg}, MediaType={MediaType}",
                    errcodeEl.GetInt32(), errmsg, type);
                return null;
            }

            if (doc.RootElement.TryGetProperty("media_id", out var mediaIdEl))
                return mediaIdEl.GetString();

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉上传媒体文件异常, MediaType={MediaType}", type);
            return null;
        }
    }

    /// <summary>
    /// 根据文件名和 MimeType 推断钉钉 oapi media/upload 所需的 type 枚举值。
    /// 返回 image / voice / video / file 四选一。
    /// </summary>
    private static string InferMediaType(string? filename, string? mimeType)
    {
        var ext = Path.GetExtension(filename ?? string.Empty).TrimStart('.').ToLowerInvariant();
        var mt = (mimeType ?? string.Empty).ToLowerInvariant();

        // 图片
        if (mt.StartsWith("image/") || ext is "jpg" or "jpeg" or "png" or "gif" or "bmp" or "webp")
            return "image";

        // 语音
        if (mt.StartsWith("audio/") || ext is "amr" or "mp3" or "aac" or "wav" or "ogg")
            return "voice";

        // 视频
        if (mt.StartsWith("video/") || ext is "mp4" or "mov" or "avi" or "mkv" or "wmv")
            return "video";

        // 其他一律走 file 类型（pdf/xlsx/docx/ppt/txt/zip/rar/7z 等）
        return "file";
    }

    public async Task<string?> GetUserIdByMobileAsync(string mobile)
    {
        var accessToken = await GetAccessTokenAsync();
        if (string.IsNullOrEmpty(accessToken))
            return null;

        try
        {
            var url = $"{OapiBaseUrl}/user/get_by_mobile?access_token={HttpUtility.UrlEncode(accessToken)}&mobile={HttpUtility.UrlEncode(mobile)}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("钉钉根据手机号查 userId 失败：{StatusCode}", response.StatusCode);
                return null;
            }

            var respBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respBody);

            if (doc.RootElement.TryGetProperty("errcode", out var errcodeEl) && errcodeEl.GetInt32() != 0)
            {
                _logger.LogWarning("钉钉手机号 {Mobile} 未找到对应用户", mobile);
                return null;
            }

            if (doc.RootElement.TryGetProperty("userid", out var userIdEl))
                return userIdEl.GetString();

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉根据手机号查 userId 异常");
            return null;
        }
    }

    public async Task<string?> GetUserEmailAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        var accessToken = await GetAccessTokenAsync();
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("钉钉 Access Token 为空，无法获取用户邮箱");
            return null;
        }

        try
        {
            // 旧版服务端 API：POST /topapi/v2/user/get
            // body: {"userid":"..."}
            // 需应用在钉钉开放平台开通"通讯录-个人手机号/邮箱等个人信息"权限（field_email），
            // 否则返回的 email 字段为空字符串。
            var url = $"{OapiBaseUrl}/topapi/v2/user/get?access_token={HttpUtility.UrlEncode(accessToken)}";
            var payload = JsonSerializer.Serialize(new { userid = userId });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("钉钉获取用户邮箱失败：UserId={UserId}, StatusCode={StatusCode}", userId, response.StatusCode);
                return null;
            }

            var respBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respBody);

            if (doc.RootElement.TryGetProperty("errcode", out var errcodeEl) && errcodeEl.GetInt32() != 0)
            {
                var errmsg = doc.RootElement.TryGetProperty("errmsg", out var emEl) ? emEl.GetString() : string.Empty;
                _logger.LogInformation("钉钉获取用户邮箱未成功：UserId={UserId}, ErrCode={ErrCode}, ErrMsg={ErrMsg}",
                    userId, errcodeEl.GetInt32(), errmsg);
                return null;
            }

            if (doc.RootElement.TryGetProperty("result", out var resultEl)
                && resultEl.TryGetProperty("email", out var emailEl))
            {
                var email = emailEl.GetString();
                if (!string.IsNullOrWhiteSpace(email))
                    return email.Trim();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉获取用户邮箱异常：UserId={UserId}", userId);
            return null;
        }
    }

    /// <summary>
    /// 获取钉钉用户完整详情（name / email / mobile / avatar / title / work_place / unionid / remark 等）。
    /// 返回 result 节点；调用方可按字段名读取。失败或 errcode!=0 时返回 null。
    /// </summary>
    public async Task<JsonElement?> GetUserDetailAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        var accessToken = await GetAccessTokenAsync();
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("钉钉 Access Token 为空，无法获取用户详情");
            return null;
        }

        try
        {
            var url = $"{OapiBaseUrl}/topapi/v2/user/get?access_token={HttpUtility.UrlEncode(accessToken)}";
            var payload = JsonSerializer.Serialize(new { userid = userId, language = "zh_CN" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("钉钉获取用户详情失败：UserId={UserId}, StatusCode={StatusCode}", userId, response.StatusCode);
                return null;
            }

            var respBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respBody);

            if (doc.RootElement.TryGetProperty("errcode", out var errcodeEl) && errcodeEl.GetInt32() != 0)
            {
                var errmsg = doc.RootElement.TryGetProperty("errmsg", out var emEl) ? emEl.GetString() : string.Empty;
                _logger.LogInformation("钉钉获取用户详情未成功：UserId={UserId}, ErrCode={ErrCode}, ErrMsg={ErrMsg}",
                    userId, errcodeEl.GetInt32(), errmsg);
                return null;
            }

            if (doc.RootElement.TryGetProperty("result", out var resultEl)
                && resultEl.ValueKind == JsonValueKind.Object)
            {
                return resultEl.Clone();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉获取用户详情异常：UserId={UserId}", userId);
            return null;
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        return !string.IsNullOrEmpty(token);
    }

    public async Task<(byte[] data, string? extension)?> DownloadMediaAsync(string downloadCode, string robotCode)
    {
        if (string.IsNullOrEmpty(downloadCode))
        {
            _logger.LogWarning("钉钉下载码为空，无法下载媒体文件");
            return null;
        }

        if (string.IsNullOrEmpty(robotCode))
        {
            _logger.LogWarning("钉钉 robotCode 为空，无法下载媒体文件");
            return null;
        }

        var accessToken = await GetAccessTokenAsync();
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogError("钉钉 Access Token 为空，无法下载媒体文件");
            return null;
        }

        // 步骤 1：调用 /v1.0/robot/messageFiles/download 获取临时下载链接
        string? downloadUrl;
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                downloadCode,
                robotCode
            });

            var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/v1.0/robot/messageFiles/download")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-acs-dingtalk-access-token", accessToken);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("钉钉获取媒体下载链接失败：StatusCode={StatusCode}", (int)response.StatusCode);
                return null;
            }

            var respBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respBody);
            if (!doc.RootElement.TryGetProperty("downloadUrl", out var urlEl) || urlEl.ValueKind != JsonValueKind.String)
            {
                _logger.LogError("钉钉媒体下载响应缺少 downloadUrl：{Body}", respBody);
                return null;
            }
            downloadUrl = urlEl.GetString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉获取媒体下载链接异常：DownloadCode={DownloadCode}", downloadCode);
            return null;
        }

        if (string.IsNullOrEmpty(downloadUrl))
        {
            _logger.LogWarning("钉钉返回的 downloadUrl 为空：DownloadCode={DownloadCode}", downloadCode);
            return null;
        }

        // 步骤 2：从临时下载链接获取实际文件内容
        try
        {
            using var fileResponse = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!fileResponse.IsSuccessStatusCode)
            {
                _logger.LogError("钉钉媒体文件下载失败：DownloadUrl={DownloadUrl}, StatusCode={StatusCode}",
                    downloadUrl, fileResponse.StatusCode);
                return null;
            }

            var data = await fileResponse.Content.ReadAsByteArrayAsync();
            if (data.Length == 0)
            {
                _logger.LogWarning("钉钉媒体文件下载内容为空：DownloadUrl={DownloadUrl}", downloadUrl);
                return null;
            }

            var contentType = fileResponse.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var extension = GetExtensionFromContentType(contentType);
            _logger.LogInformation("钉钉媒体文件下载成功：DownloadCode={DownloadCode}, Size={Size} bytes, ContentType={ContentType}",
                downloadCode, data.Length, contentType);
            return (data, extension);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉媒体文件下载异常：DownloadUrl={DownloadUrl}", downloadUrl);
            return null;
        }
    }

    /// <summary>
    /// 根据 Content-Type 推断文件扩展名（含钉钉常用媒体类型）。
    /// 钉钉 downloadUrl 返回的 Content-Type 可能不准确，未匹配时返回 null 由调用方按消息类型兜底。
    /// </summary>
    private static string? GetExtensionFromContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return null;
        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/webp" => ".webp",
            "audio/amr" => ".amr",
            "audio/mpeg" or "audio/mp3" => ".mp3",
            "audio/aac" => ".aac",
            "audio/wav" or "audio/x-wav" => ".wav",
            "audio/ogg" => ".ogg",
            "video/mp4" => ".mp4",
            "video/quicktime" => ".mov",
            "video/x-msvideo" => ".avi",
            "application/pdf" => ".pdf",
            "application/octet-stream" => null,
            _ => null
        };
    }

    public async Task<bool> SendImageBySessionWebhookAsync(string sessionWebhook, string imageBase64, string imageMd5)
    {
        if (string.IsNullOrEmpty(sessionWebhook))
        {
            _logger.LogWarning("钉钉 sessionWebhook 为空，无法发送图片");
            return false;
        }
        if (string.IsNullOrEmpty(imageBase64) || string.IsNullOrEmpty(imageMd5))
        {
            _logger.LogWarning("钉钉图片 base64 或 md5 为空，无法发送图片");
            return false;
        }

        try
        {
            // body: {"msgtype":"image","image":{"base64":"...","md5":"..."}}
            var payload = JsonSerializer.Serialize(new
            {
                msgtype = "image",
                image = new
                {
                    base64 = imageBase64,
                    md5 = imageMd5
                }
            });

            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(sessionWebhook, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("钉钉 sessionWebhook 发送图片失败：StatusCode={StatusCode}", (int)response.StatusCode);
                return false;
            }

            var respBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respBody);
            if (doc.RootElement.TryGetProperty("errcode", out var errcodeEl) && errcodeEl.GetInt32() != 0)
            {
                var errmsg = doc.RootElement.TryGetProperty("errmsg", out var msgEl) ? msgEl.GetString() : "unknown";
                _logger.LogError("钉钉 sessionWebhook 发送图片业务错误：errcode={ErrCode} errmsg={ErrMsg}",
                    errcodeEl.GetInt32(), errmsg);
                return false;
            }

            _logger.LogInformation("钉钉 sessionWebhook 发送图片成功");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉 sessionWebhook 发送图片异常");
            return false;
        }
    }

    public async Task<string?> BatchSendImageMessageAsync(string robotCode, string[] userIds, string photoMediaId)
    {
        if (string.IsNullOrEmpty(photoMediaId))
        {
            _logger.LogWarning("钉钉 photoMediaId 为空，无法发送图片消息");
            return null;
        }

        // 按官方文档《机器人发送消息的类型 / 图片类型 sampleImageMsg》：
        //   msgParam = {"photoURL": "xxxx"}，扁平结构，不要嵌套 photo 对象
        //
        // photoURL 可填写图片的完整 URL 路径，也可填写图片的 media_id。
        // media_id 通过 oapi media/upload 返回值形如 "@lADOADmaWMzazQKA"（自带 @ 前缀），原样传递即可。
        var msgParam = JsonSerializer.Serialize(new
        {
            photoURL = photoMediaId
        });

        return await BatchSendOtoMessageAsync(robotCode, userIds, "sampleImageMsg", msgParam);
    }

    public async Task<string?> BatchSendFileMessageAsync(
        string robotCode,
        string[] userIds,
        string fileMediaId,
        string fileName,
        string fileType)
    {
        if (string.IsNullOrEmpty(fileMediaId))
        {
            _logger.LogWarning("钉钉 fileMediaId 为空，无法发送文件消息");
            return null;
        }
        if (string.IsNullOrEmpty(fileName)) fileName = "file";
        if (string.IsNullOrEmpty(fileType)) fileType = "file";

        // 按官方文档《机器人发送消息的类型 / 文件类型 sampleFile》：
        //   msgParam = {"mediaId":"@...", "fileName":"xxx", "fileType":"xlsx"}
        // 扁平结构，不要嵌套 file 对象；media_id 保留 @ 前缀
        var msgParam = JsonSerializer.Serialize(new
        {
            mediaId = fileMediaId,
            fileName = fileName,
            fileType = fileType.TrimStart('.')
        });

        return await BatchSendOtoMessageAsync(robotCode, userIds, "sampleFile", msgParam);
    }

    /// <summary>
    /// 计算字节数组的 MD5 HEX（小写，32 字符）。
    /// sessionWebhook 发送图片消息需要 md5 字段。
    /// </summary>
    public static string ComputeMd5Hex(byte[] data)
    {
        if (data == null || data.Length == 0) return string.Empty;
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(data);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
