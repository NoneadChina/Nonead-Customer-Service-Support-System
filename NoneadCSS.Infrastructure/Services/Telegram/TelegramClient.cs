using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Infrastructure.Services.Telegram;

/// <summary>
/// Telegram Bot API HttpClient 实现。
/// 安全约束：Bot Token 位于 URL 路径（/bot&lt;token&gt;/&lt;method&gt;），
/// 因此本类任何日志都只输出方法名与状态码，绝不输出 URL、token 或请求头。
/// ApiBaseUrl 可指向自建 Bot API 服务器（tdlib/telegram-bot-api），不写死官方域名。
/// </summary>
public class TelegramClient : ITelegramClient
{
    private readonly HttpClient _httpClient;
    private readonly ITelegramSettingsProvider _settingsProvider;
    private readonly ILogger<TelegramClient> _logger;

    public TelegramClient(
        HttpClient httpClient,
        IOptions<HttpClientOptions> httpClientOptions,
        ITelegramSettingsProvider settingsProvider,
        ILogger<TelegramClient> logger)
    {
        _httpClient = httpClient;
        // 轮询使用 long polling（timeout 最长 50s），必须超过标准超时，否则长连接会被本地掐断
        var standardTimeout = httpClientOptions.Value.StandardTimeoutSeconds;
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(standardTimeout, 60));
        _settingsProvider = settingsProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<TelegramSendResult> SendTextMessageAsync(string chatId, string text, string? replyToMessageId = null, CancellationToken cancellationToken = default, int? messageThreadId = null, string? parseMode = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["text"] = text,
            ["disable_web_page_preview"] = true
        };

        if (!string.IsNullOrWhiteSpace(replyToMessageId) && long.TryParse(replyToMessageId, out var replyId))
        {
            payload["reply_parameters"] = new Dictionary<string, object?> { ["message_id"] = replyId };
        }

        // 论坛群（forum）里不带 message_thread_id 的消息会落到“General”话题，坐席在某个话题里的回复看起来就“跑题”了。
        // 0 不是合法话题号（Telegram 会报 400），所以只有拿到正整数才写该字段，拿不到就不写。
        if (messageThreadId is > 0)
        {
            payload["message_thread_id"] = messageThreadId.Value;
        }

        // 纯文本（"none"/空/null）绝不下发 parse_mode：Telegram 收到 null 会按空串处理并报 400。
        // 只有坐席明确选了格式化模式才写该字段；文本本身不做转义（是操作员主动选的标记语义）。
        var normalizedParseMode = parseMode?.Trim();
        if (!string.IsNullOrEmpty(normalizedParseMode)
            && !normalizedParseMode.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            payload["parse_mode"] = normalizedParseMode;
        }

        return PostJsonAsync("sendMessage", payload, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TelegramSendResult> SendMediaMessageAsync(string chatId, string mediaType, string fileIdOrUrl, string? caption = null, CancellationToken cancellationToken = default)
    {
        var method = ResolveSendMediaMethod(mediaType);
        var payload = new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            [method[4..].ToLowerInvariant()] = fileIdOrUrl
        };

        if (!string.IsNullOrWhiteSpace(caption))
        {
            payload["caption"] = caption;
        }

        return PostJsonAsync(method, payload, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TelegramSendResult> UploadAndSendMediaAsync(string chatId, string mediaType, byte[] data, string fileName, string mimeType, string? caption = null, CancellationToken cancellationToken = default)
    {
        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured)
        {
            return new TelegramSendResult { Success = false, ErrorMessage = "Telegram 未配置 BotToken" };
        }

        var method = ResolveSendMediaMethod(mediaType);
        var fieldName = method[4..].ToLowerInvariant();

        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(data);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType);
        multipart.Add(fileContent, fieldName, fileName);
        multipart.Add(new StringContent(chatId), "chat_id");

        if (!string.IsNullOrWhiteSpace(caption))
        {
            multipart.Add(new StringContent(caption), "caption");
        }

        return await SendAsync(method, multipart, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SendChatActionAsync(string chatId, string action = "typing", CancellationToken cancellationToken = default)
    {
        var result = await PostJsonAsync("sendChatAction", new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["action"] = action
        }, cancellationToken);

        return result.Success;
    }

    /// <inheritdoc />
    public async Task<TelegramFileLocation?> GetFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured || string.IsNullOrWhiteSpace(fileId))
        {
            return null;
        }

        var payload = new Dictionary<string, object?> { ["file_id"] = fileId };
        var response = await PostRawAsync("getFile", payload, cancellationToken);
        if (response == null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            if (!root.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
            {
                LogApiFailure("getFile", response);
                return null;
            }

            var result = root.TryGetProperty("result", out var resultElement) ? resultElement : default;
            if (result.ValueKind == JsonValueKind.Undefined || !result.TryGetProperty("file_path", out var pathElement))
            {
                return null;
            }

            var filePath = pathElement.GetString() ?? string.Empty;
            return new TelegramFileLocation
            {
                FileId = fileId,
                FileUniqueId = result.TryGetProperty("file_unique_id", out var uniqueElement) ? uniqueElement.GetString() : null,
                FileSize = result.TryGetProperty("file_size", out var sizeElement) && sizeElement.TryGetInt64(out var size) ? size : null,
                FilePath = filePath,
                // 自建 --local 服务器返回本机绝对路径（Windows 形如 C:\... 或带盘符；Linux 以 / 开头）
                IsLocalPath = filePath.StartsWith('/')
                    || (filePath.Length >= 3 && char.IsLetter(filePath[0]) && filePath[1] == ':' && (filePath[2] == '\\' || filePath[2] == '/'))
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "解析 Telegram getFile 响应失败");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<(byte[] data, string? mimeType)?> DownloadFileAsync(TelegramFileLocation location, CancellationToken cancellationToken = default)
    {
        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured || string.IsNullOrEmpty(location.FilePath))
        {
            return null;
        }

        // 自建服务器（--local）且本机可直接读盘：跳过 HTTP 下载（官方下载上限 20MB 也不再适用）
        if (location.IsLocalPath && File.Exists(location.FilePath))
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(location.FilePath, cancellationToken);
                return (bytes, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取自建 Bot API 服务器本地文件失败，回退 HTTP 下载");
            }
        }

        var url = BuildFileUrl(location.FilePath);
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 只记录方法段与状态码：URL 中含 token，绝不能出现在日志里
                _logger.LogError("Telegram 文件下载失败：{Status}", (int)response.StatusCode);
                return null;
            }

            var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return (data, response.Content.Headers.ContentType?.MediaType);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram 文件下载异常");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<TelegramFileDownloadResult> DownloadFileToTempAsync(string fileId, long maxBytes, CancellationToken cancellationToken = default)
    {
        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured)
        {
            return TelegramFileDownloadResult.Failed("Telegram 未配置 BotToken");
        }

        // 步骤 1：先 getFile —— 它的 file_size 是 Telegram 自己声明的大小，可在建立下载连接前就判超限（零缓冲成本）
        TelegramFileLocation? location;
        try
        {
            location = await GetFileAsync(fileId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram getFile 异常");
            return TelegramFileDownloadResult.Failed("getFile 请求异常");
        }

        if (location == null || string.IsNullOrEmpty(location.FilePath))
        {
            return TelegramFileDownloadResult.Failed("getFile 未返回 file_path");
        }

        if (maxBytes > 0 && location.FileSize is > 0 && location.FileSize.Value > maxBytes)
        {
            return TelegramFileDownloadResult.TooLarge(location.FileSize.Value, maxBytes);
        }

        var tempPath = BuildTempFilePath(location.FilePath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

            // 自建 Bot API 服务器（--local）返回的是本机绝对路径：直接按上限复制，不走 HTTP
            if (location.IsLocalPath && File.Exists(location.FilePath))
            {
                var sourceLength = new FileInfo(location.FilePath).Length;
                if (maxBytes > 0 && sourceLength > maxBytes)
                {
                    return TelegramFileDownloadResult.TooLarge(sourceLength, maxBytes);
                }

                await using var localStream = new FileStream(location.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                var localCopy = await CopyStreamWithCapAsync(localStream, tempPath, maxBytes, cancellationToken);
                if (localCopy.TooLarge)
                {
                    TryDeleteFile(tempPath);
                    return TelegramFileDownloadResult.TooLarge(localCopy.TotalBytes, maxBytes);
                }

                // 本地路径没有 HTTP 响应头，MIME 交给调用方按扩展名/消息类型兜底
                return TelegramFileDownloadResult.Ok(tempPath, localCopy.TotalBytes, null);
            }

            var url = BuildFileUrl(location.FilePath);

            // ResponseHeadersRead：响应头一到就返回，不先把整个响应体读进内存；Content-Length 因此可以作为第二道预检
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 只记录方法段与状态码：URL 中含 token，绝不能出现在日志里
                _logger.LogError("Telegram 文件下载失败：{Status}", (int)response.StatusCode);
                return TelegramFileDownloadResult.Failed($"下载失败（HTTP {(int)response.StatusCode}）");
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (maxBytes > 0 && contentLength is > 0 && contentLength.Value > maxBytes)
            {
                return TelegramFileDownloadResult.TooLarge(contentLength.Value, maxBytes);
            }

            var mimeType = response.Content.Headers.ContentType?.MediaType;
            await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var copy = await CopyStreamWithCapAsync(sourceStream, tempPath, maxBytes, cancellationToken);
            if (copy.TooLarge)
            {
                TryDeleteFile(tempPath);
                return TelegramFileDownloadResult.TooLarge(copy.TotalBytes, maxBytes);
            }

            return TelegramFileDownloadResult.Ok(tempPath, copy.TotalBytes, mimeType);
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram 文件流式下载异常");
            TryDeleteFile(tempPath);
            return TelegramFileDownloadResult.Failed("下载异常");
        }
    }

    /// <summary>
    /// 带增量上限的流式拷贝：Content-Length 可能缺失或被伪造，所以按「实际读到的字节数」逐块累加，
    /// 一旦超过 maxBytes 立即停止并交由调用方删除半成品（这是防止大文件把磁盘/内存撑爆的最后一道闸）。
    /// </summary>
    private static async Task<(bool TooLarge, long TotalBytes)> CopyStreamWithCapAsync(
        Stream source,
        string destinationPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (maxBytes > 0 && total > maxBytes)
            {
                // 先关文件再返回，调用方才能删掉半成品（Windows 上打开中的文件无法删除）
                await fileStream.DisposeAsync();
                return (true, total);
            }

            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return (false, total);
    }

    /// <summary>临时文件路径：只沿用原扩展名（file_path 形如 photos/file_1.jpg，不能当目录结构用）</summary>
    private static string BuildTempFilePath(string filePath)
    {
        var directory = Path.Combine(Path.GetTempPath(), "noneadcss-telegram");
        var extension = Path.GetExtension(filePath);
        if (extension.Length is 0 or > 16 || extension.Any(c => !char.IsLetterOrDigit(c) && c != '.'))
        {
            extension = string.Empty;
        }

        return Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 清理临时文件失败不影响主流程（临时目录会被系统回收）
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetWebhookAsync(string url, string? secretToken, int maxConnections, IReadOnlyList<string>? allowedUpdates, bool dropPendingUpdates, string? certificate = null, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["url"] = url,
            ["max_connections"] = Math.Clamp(maxConnections, 1, 100),
            ["drop_pending_updates"] = dropPendingUpdates
        };

        if (!string.IsNullOrWhiteSpace(secretToken))
        {
            payload["secret_token"] = secretToken;
        }

        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            payload["ip_address"] = ipAddress;
        }

        if (allowedUpdates is { Count: > 0 })
        {
            payload["allowed_updates"] = allowedUpdates;
        }

        // 自签名证书需上传 certificate（PEM）：官方 API 走 multipart，这里按有无证书选择表单形态
        if (!string.IsNullOrWhiteSpace(certificate))
        {
            using var multipart = new MultipartFormDataContent();
            var certificateContent = new StringContent(certificate);
            certificateContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-pem-file");
            multipart.Add(certificateContent, "certificate", "server.pem");
            foreach (var pair in payload)
            {
                multipart.Add(new StringContent(SerializeValue(pair.Value)), pair.Key);
            }

            var multipartResult = await SendAsync("setWebhook", multipart, cancellationToken);
            return multipartResult.Success;
        }

        var result = await PostJsonAsync("setWebhook", payload, cancellationToken);
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteWebhookAsync(bool dropPendingUpdates = false, CancellationToken cancellationToken = default)
    {
        var result = await PostJsonAsync("deleteWebhook",
            new Dictionary<string, object?> { ["drop_pending_updates"] = dropPendingUpdates }, cancellationToken);
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<TelegramWebhookInfo?> GetWebhookInfoAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured)
        {
            return null;
        }

        var response = await PostRawAsync("getWebhookInfo", new Dictionary<string, object?>(), cancellationToken);
        if (response == null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            if (!root.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
            {
                LogApiFailure("getWebhookInfo", response);
                return null;
            }

            var result = root.TryGetProperty("result", out var resultElement) ? resultElement : default;
            if (result.ValueKind == JsonValueKind.Undefined)
            {
                return null;
            }

            var info = new TelegramWebhookInfo
            {
                Url = ReadString(result, "url"),
                HasCustomCertificate = ReadBool(result, "has_custom_certificate"),
                PendingUpdateCount = (int)ReadInt64(result, "pending_update_count", 0),
                LastErrorMessage = ReadString(result, "last_error_message"),
                LastErrorTime = result.TryGetProperty("last_error_time", out var errTime) && errTime.TryGetInt64(out var ets) ? ets : null,
                MaxConnections = result.TryGetProperty("max_connections", out var maxConn) && maxConn.TryGetInt64(out var mc) ? (int)mc : null
            };

            if (result.TryGetProperty("allowed_updates", out var allowed) && allowed.ValueKind == JsonValueKind.Array)
            {
                info.AllowedUpdates = allowed.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToArray();
            }

            return info;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "解析 Telegram getWebhookInfo 响应失败");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<TelegramBotInfo?> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured)
        {
            return null;
        }

        var response = await PostRawAsync("getMe", new Dictionary<string, object?>(), cancellationToken);
        if (response == null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            if (!root.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
            {
                LogApiFailure("getMe", response);
                return null;
            }

            var result = root.TryGetProperty("result", out var resultElement) ? resultElement : default;
            if (result.ValueKind == JsonValueKind.Undefined)
            {
                return null;
            }

            return new TelegramBotInfo
            {
                Id = ReadInt64(result, "id", 0),
                IsBot = ReadBool(result, "is_bot"),
                FirstName = ReadString(result, "first_name"),
                Username = ReadString(result, "username"),
                CanJoinGroups = ReadBool(result, "can_join_groups"),
                CanReadAllGroupMessages = ReadBool(result, "can_read_all_group_messages"),
                SupportsInlineQueries = ReadBool(result, "supports_inline_queries")
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "解析 Telegram getMe 响应失败");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<TelegramUpdatesPage> GetUpdatesAsync(long offset, int limit, int timeout, IReadOnlyList<string>? allowedUpdates, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["offset"] = offset,
            ["limit"] = Math.Clamp(limit, 1, 100),
            ["timeout"] = Math.Clamp(timeout, 0, 50)
        };

        if (allowedUpdates is { Count: > 0 })
        {
            payload["allowed_updates"] = allowedUpdates;
        }

        var response = await PostRawAsync("getUpdates", payload, cancellationToken);
        if (response == null)
        {
            return new TelegramUpdatesPage { Ok = false, ErrorMessage = "getUpdates 请求失败" };
        }

        var updates = TelegramWebhookParser.ParseUpdates(response);
        if (updates.Count == 0)
        {
            // 空数组既可能是真的没有 Update，也可能是 ok=false —— 交给解析器判定错误分类
            var failure = TelegramWebhookParser.ParseSendResponse(response);
            if (!failure.Success)
            {
                return new TelegramUpdatesPage
                {
                    Ok = false,
                    RetryAfterSeconds = failure.RetryAfterSeconds,
                    WebhookConflict = failure.Conflict,
                    InvalidToken = failure.InvalidToken,
                    ErrorMessage = failure.ErrorMessage ?? failure.Description
                };
            }
        }

        var maxUpdateId = updates.Count == 0 ? offset - 1 : updates.Max(u => u.UpdateId);
        return new TelegramUpdatesPage
        {
            Ok = true,
            Updates = updates,
            MaxUpdateId = maxUpdateId
        };
    }

    /// <inheritdoc />
    public async Task<bool> AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?> { ["callback_query_id"] = callbackQueryId };
        if (!string.IsNullOrWhiteSpace(text))
        {
            payload["text"] = text;
        }

        var result = await PostJsonAsync("answerCallbackQuery", payload, cancellationToken);
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return await GetMeAsync(cancellationToken) != null;
    }

    private async Task<TelegramSendResult> PostJsonAsync(string method, Dictionary<string, object?> payload, CancellationToken cancellationToken)
    {
        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured)
        {
            return new TelegramSendResult { Success = false, ErrorMessage = "Telegram 未配置 BotToken" };
        }

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await SendAsync(method, content, cancellationToken);
    }

    /// <summary>发起请求并返回原始响应文本（失败返回 null）。日志只含方法名与状态码，不含 URL/token。</summary>
    private async Task<string?> PostRawAsync(string method, Dictionary<string, object?> payload, CancellationToken cancellationToken)
    {
        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            using var response = await _httpClient.PostAsync(BuildMethodUrl(method), content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Telegram API {Method} 返回 {Status}", method, (int)response.StatusCode);
            }

            return body;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram API {Method} 请求异常", method);
            return null;
        }
    }

    private async Task<TelegramSendResult> SendAsync(string method, HttpContent content, CancellationToken cancellationToken)
    {
        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured)
        {
            return new TelegramSendResult { Success = false, ErrorMessage = "Telegram 未配置 BotToken" };
        }

        try
        {
            using var response = await _httpClient.PostAsync(BuildMethodUrl(method), content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = TelegramWebhookParser.ParseSendResponse(body);

            if (!result.Success)
            {
                // description 不含 token；状态码 + 分类足以定位问题，绝不输出 URL
                _logger.LogError("Telegram API {Method} 失败：HTTP {Status} error_code={ErrorCode} description={Description}",
                    method, (int)response.StatusCode, result.ErrorCode, result.Description);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Telegram API {Method} 超时或已取消", method);
            return new TelegramSendResult { Success = false, ErrorMessage = $"请求超时或已取消：{method}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram API {Method} 异常", method);
            return new TelegramSendResult { Success = false, ErrorMessage = $"请求异常：{method}" };
        }
    }

    private string BuildMethodUrl(string method)
    {
        var settings = _settingsProvider.Current;
        var baseUrl = string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ? "https://api.telegram.org" : settings.ApiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/bot{settings.BotToken}/{method}";
    }

    private string BuildFileUrl(string filePath)
    {
        var settings = _settingsProvider.Current;
        var baseUrl = string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ? "https://api.telegram.org" : settings.ApiBaseUrl.TrimEnd('/');
        var sanitized = filePath.Replace('\\', '/').TrimStart('/');
        return $"{baseUrl}/file/bot{settings.BotToken}/{sanitized}";
    }

    /// <summary>把站内附件类型映射到 Bot API 方法名（未知类型按 document 发送，最稳妥）</summary>
    private static string ResolveSendMediaMethod(string mediaType)
    {
        return mediaType.ToLowerInvariant() switch
        {
            "photo" or "image" => "sendPhoto",
            "video" => "sendVideo",
            "audio" => "sendAudio",
            "voice" => "sendVoice",
            "animation" => "sendAnimation",
            "sticker" => "sendSticker",
            _ => "sendDocument"
        };
    }

    private static string SerializeValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            bool b => b ? "true" : "false",
            IEnumerable<string> list => JsonSerializer.Serialize(list),
            _ => value.ToString() ?? string.Empty
        };
    }

    private void LogApiFailure(string method, string response)
    {
        var result = TelegramWebhookParser.ParseSendResponse(response);
        _logger.LogError("Telegram API {Method} 失败：error_code={ErrorCode} description={Description}",
            method, result.ErrorCode, result.Description);
    }

    private static string? ReadString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool ReadBool(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.True;
    }

    private static long ReadInt64(JsonElement element, string property, long fallback)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return fallback;
        }

        return value.TryGetInt64(out var result) ? result : fallback;
    }
}
