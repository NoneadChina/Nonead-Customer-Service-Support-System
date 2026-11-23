using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Infrastructure.Services.Line;

/// <summary>
/// LINE Messaging API 客户端实现（官方无 C# SDK，直接用 HttpClient + System.Text.Json）。
/// 关键点：
///   1) 所有调用带 Authorization: Bearer &lt;ChannelAccessToken&gt;；日志绝不输出令牌或完整签名。
///   2) reply 不消耗月度配额、push 消耗 ⇒ 有 replyToken 就优先 reply，失败回落 push。
///   3) 单次请求 messages ≤ MaxMessagesPerRequest（约 5），文本 ≤ TextChunkChars（约 5000）⇒ 自动拆分。
///   4) 429/500/503 单次退避重试（带抖动）；401 令牌失效 / 404 非好友 / 403 配额或权限 不重试。
/// </summary>
public class LineClient : ILineClient
{
    private const string ReplyPath = "/v2/bot/message/reply";
    private const string PushPath = "/v2/bot/message/push";
    private const string MarkAsReadPath = "/v2/bot/chat/markAsRead";
    private const string LoadingStartPath = "/v2/bot/chat/loading/start";
    private const string ProfilePathFormat = "/v2/bot/profile/{0}";
    private const string ContentPathFormat = "/v2/bot/message/{0}/content";
    private const string QuotaPath = "/v2/bot/message/quota";
    private const string QuotaConsumptionPath = "/v2/bot/message/quota/consumption";
    private const string TokenVerifyPath = "/v2/oauth/verify";
    private const string WebhookEndpointPath = "/v2/bot/channel/webhook/endpoint";
    private const string WebhookTestPath = "/v2/bot/channel/webhook/test";

    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private readonly HttpClient _httpClient;
    private readonly ILineSettingsProvider _settingsProvider;
    private readonly ILogger<LineClient> _logger;
    private readonly Random _jitter = new();

    public LineClient(
        HttpClient httpClient,
        IOptions<HttpClientOptions> httpClientOptions,
        ILineSettingsProvider settingsProvider,
        ILogger<LineClient> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(httpClientOptions.Value.StandardTimeoutSeconds);
        _settingsProvider = settingsProvider;
        _logger = logger;
    }

    public async Task<LineSendResult> SendMessagesAsync(
        IReadOnlyList<LineMessageObject> messages,
        string? replyToken = null,
        string? toUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (messages is null || messages.Count == 0)
        {
            return new LineSendResult { Success = false, ErrorMessage = "没有可发送的 LINE 消息" };
        }

        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured)
        {
            _logger.LogError("LINE 未配置（缺少 ChannelSecret/ChannelAccessToken），放弃发送");
            return new LineSendResult { Success = false, ErrorMessage = "LINE 未配置" };
        }

        if (string.IsNullOrWhiteSpace(settings.ChannelAccessToken))
        {
            return new LineSendResult { Success = false, ErrorMessage = "LINE Channel Access Token 未配置" };
        }

        var batches = SplitIntoBatches(messages, settings.MaxMessagesPerRequest);
        var chunkCount = 0;
        string? replyTokenRemaining = string.IsNullOrWhiteSpace(replyToken) ? null : replyToken;
        LineSendResult? lastFailure = null;
        // 真实投递路径可能与请求方式不同（replyToken 被确定性拒绝后回落 push），
        // 成功返回里的 Via 必须反映实际用的端点：LineService 靠 Via 判断是否发生了回落。
        var deliveredByPush = false;

        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            var useReply = replyTokenRemaining is not null;
            LineSendResult result;

            if (useReply)
            {
                result = await PostSendAsync(ReplyPath, BuildReplyPayload(replyTokenRemaining!, batch), settings, cancellationToken);
                // replyToken 一次性：用过一次就不再复用（后续批次必须走 push）
                replyTokenRemaining = null;

                if (!result.Success && IsReplyTokenProblem(result.Error))
                {
                    // 关键守卫：PostSendAsync 对 429/5xx 会内部重试，而这类响应可能已经投递成功
                    // （LINE 侧已收到消息，只是网关返回失败），此时回落 push 会造成**重复发送**。
                    // 因此只有「单次尝试即被判定为令牌问题」（HTTP 401/403/409 等确定性拒绝，
                    // 不会与已投递混淆）时才允许回落；重试过的失败交给服务层判断。
                    if (result.Attempts > 1)
                    {
                        _logger.LogWarning("LINE reply 经过 {Attempts} 次尝试仍失败（HTTP {Status}），为避免重复投递不回落 push",
                            result.Attempts, result.Error?.HttpStatus);
                        result.DuplicateRisk = true;
                        return result;
                    }

                    if (string.IsNullOrWhiteSpace(toUserId))
                    {
                        _logger.LogWarning("LINE replyToken 失效（HTTP {Status}）且无 to 可回落 push，发送中止", result.Error?.HttpStatus);
                        return new LineSendResult
                        {
                            Success = false,
                            Via = "reply",
                            ChunkCount = chunkCount,
                            ErrorMessage = result.ErrorMessage ?? "replyToken 已失效",
                            Error = result.Error,
                        };
                    }

                    _logger.LogInformation("LINE replyToken 不可用（HTTP {Status}），回落 push", result.Error?.HttpStatus);
                    deliveredByPush = true;
                    result = await PostSendAsync(PushPath, BuildPushPayload(toUserId!, batch), settings, cancellationToken);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(toUserId))
                {
                    return new LineSendResult { Success = false, ErrorMessage = "LINE push 需要目标 userId" };
                }

                result = await PostSendAsync(PushPath, BuildPushPayload(toUserId!, batch), settings, cancellationToken);
            }

            chunkCount += batch.Count;
            if (!result.Success)
            {
                lastFailure = result;
                break;
            }
        }

        if (lastFailure is not null)
        {
            lastFailure.ChunkCount = chunkCount;
            return lastFailure;
        }

        return new LineSendResult
        {
            Success = true,
            Via = batches.Count > 1 || deliveredByPush || string.IsNullOrWhiteSpace(replyToken) ? "push" : "reply",
            ChunkCount = chunkCount,
        };
    }

    public async Task<LineSendResult> SendTextAsync(
        string text,
        string? replyToken = null,
        string? toUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new LineSendResult { Success = false, ErrorMessage = "文本内容为空" };
        }

        var settings = _settingsProvider.Current;
        var pieces = SplitText(text, settings.TextChunkChars > 0 ? settings.TextChunkChars : 5000);
        var messages = pieces.Select(p => new LineMessageObject { Type = "text", Text = p }).Cast<LineMessageObject>().ToList();
        return await SendMessagesAsync(messages, replyToken, toUserId, cancellationToken);
    }

    public async Task<bool> MarkAsReadAsync(string markAsReadToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(markAsReadToken))
        {
            return false;
        }

        var settings = _settingsProvider.Current;
        if (!settings.MarkAsReadEnabled || !settings.IsConfigured)
        {
            return false;
        }

        var payload = new { markAsReadToken };
        var result = await PostSendAsync(MarkAsReadPath, payload, settings, cancellationToken);
        if (!result.Success)
        {
            // 聊天启用功能需官方开通；未开通时 LINE 返回 403，不作为业务失败上报
            _logger.LogDebug("LINE markAsRead 未生效：HTTP {Status} {Message}", result.Error?.HttpStatus, result.ErrorMessage);
        }

        return result.Success;
    }

    public async Task<bool> StartLoadingAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var settings = _settingsProvider.Current;
        if (!settings.LoadingIndicatorEnabled || !settings.IsConfigured)
        {
            return false;
        }

        var payload = new { chatId = userId };
        var result = await PostSendAsync(LoadingStartPath, payload, settings, cancellationToken);
        return result.Success;
    }

    public async Task<LineProfile?> GetProfileAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured)
        {
            return null;
        }

        var url = $"{settings.ApiBaseUrl.TrimEnd('/')}/v2/bot/profile/{Uri.EscapeDataString(userId)}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ChannelAccessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 404 = 非好友/已屏蔽，属正常业务分支，只记 Debug
                _logger.LogDebug("LINE 资料读取失败 HTTP {Status} userId={UserId}", (int)response.StatusCode, userId);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return new LineProfile
            {
                UserId = ReadString(root, "userId") ?? userId,
                DisplayName = ReadString(root, "displayName") ?? string.Empty,
                PictureUrl = ReadString(root, "pictureUrl"),
                StatusMessage = ReadString(root, "statusMessage"),
                Language = ReadString(root, "language"),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("LINE 资料读取超时 userId={UserId}", userId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LINE 资料读取异常 userId={UserId}", userId);
            return null;
        }
    }

    public async Task<LineContentResult?> DownloadContentAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return null;
        }

        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured)
        {
            return null;
        }

        var url = $"{settings.DataApiBaseUrl.TrimEnd('/')}/v2/bot/message/{Uri.EscapeDataString(messageId)}/content";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ChannelAccessToken);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 内容保留期极短，重复投递/迟到下载都会 404/410
                _logger.LogWarning("LINE 媒体下载失败 HTTP {Status} messageId={MessageId}", (int)response.StatusCode, messageId);
                return null;
            }

            var declared = response.Content.Headers.ContentLength ?? -1;
            if (declared > settings.MaxInboundMediaBytes)
            {
                _logger.LogWarning("LINE 媒体超过入站上限（{Declared} > {Limit}）messageId={MessageId}", declared, settings.MaxInboundMediaBytes, messageId);
                return new LineContentResult { TooLarge = true };
            }

            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            long total = 0;
            await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > settings.MaxInboundMediaBytes)
                    {
                        _logger.LogWarning("LINE 媒体实际体积超过入站上限 messageId={MessageId}", messageId);
                        return new LineContentResult { TooLarge = true };
                    }

                    memory.Write(buffer, 0, read);
                }
            }

            return new LineContentResult
            {
                Content = memory.ToArray(),
                MimeType = response.Content.Headers.ContentType?.MediaType,
                FileName = ParseFileName(response.Content.Headers.ContentDisposition?.FileName),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("LINE 媒体下载超时 messageId={MessageId}", messageId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LINE 媒体下载异常 messageId={MessageId}", messageId);
            return null;
        }
    }

    public async Task<LineQuotaInfo?> GetQuotaAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured)
        {
            return null;
        }

        var info = new LineQuotaInfo();
        var quotaDoc = await GetJsonAsync($"{settings.ApiBaseUrl.TrimEnd('/')}{QuotaPath}", settings, cancellationToken);
        if (quotaDoc is not null)
        {
            info.Type = ReadString(quotaDoc.RootElement, "type") ?? "unknown";
            // LINE GET /v2/bot/message/quota 的响应是 { "type": "limited" | "none", "value": <int> }。
            // 旧实现读的是 "quota" 字段（不存在）⇒ Quota 恒为 -1，前端配额卡永远是 unknown。
            // type=none 表示该账号不限量，保持 -1 作为「未知/不限」的统一语义。
            info.Quota = string.Equals(info.Type, "none", StringComparison.OrdinalIgnoreCase)
                ? -1
                : ReadInt32(quotaDoc.RootElement, "value", -1);
        }

        var consumptionDoc = await GetJsonAsync($"{settings.ApiBaseUrl.TrimEnd('/')}{QuotaConsumptionPath}", settings, cancellationToken);
        if (consumptionDoc is not null)
        {
            info.TotalUsage = ReadInt32(consumptionDoc.RootElement, "totalUsage", -1)
                + ReadInt32(consumptionDoc.RootElement, "additionalUsage", 0);
        }

        return quotaDoc is null && consumptionDoc is null ? null : info;
    }

    public async Task<LineTokenInfo?> VerifyTokenAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured)
        {
            return null;
        }

        // verify 是 POST（无请求体）
        var result = await PostRawAsync(TokenVerifyPath, "{}", settings, cancellationToken);
        if (result.Document is null)
        {
            return null;
        }

        var root = result.Document.RootElement;
        return new LineTokenInfo
        {
            ClientId = ReadString(root, "client_id"),
            ExpiresInSeconds = root.TryGetProperty("expires_in", out var exp) && exp.ValueKind == JsonValueKind.Number ? exp.GetInt32() : null,
            Scope = ReadString(root, "scope"),
        };
    }

    public async Task<LineWebhookEndpointInfo?> GetWebhookEndpointAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured)
        {
            return null;
        }

        var doc = await GetJsonAsync($"{settings.ApiBaseUrl.TrimEnd('/')}{WebhookEndpointPath}", settings, cancellationToken);
        if (doc is null)
        {
            return null;
        }

        return new LineWebhookEndpointInfo
        {
            Endpoint = ReadString(doc.RootElement, "endpoint"),
            Active = doc.RootElement.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True,
        };
    }

    public async Task<bool> SetWebhookEndpointAsync(string webhookUrl, bool enabled, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return false;
        }

        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured)
        {
            return false;
        }

        var payload = new { endpoint = webhookUrl, active = enabled };
        var result = await PutJsonAsync(WebhookEndpointPath, payload, settings, cancellationToken);
        return result.Success;
    }

    public async Task<LineWebhookCheckResult> CheckWebhookAsync(string webhookUrl, CancellationToken cancellationToken = default)
    {
        var settings = _settingsProvider.Current;
        if (!settings.IsConfigured || string.IsNullOrWhiteSpace(webhookUrl))
        {
            return new LineWebhookCheckResult { Success = false, ErrorMessage = "LINE 未配置或未提供回调地址", WebhookUrl = webhookUrl };
        }

        var payload = new { endpoint = webhookUrl };
        var result = await PostSendAsync(WebhookTestPath, payload, settings, cancellationToken);
        return new LineWebhookCheckResult
        {
            Success = result.Success,
            WebhookUrl = webhookUrl,
            Detail = result.Success ? "LINE 已向该回调地址发送测试事件" : string.Empty,
            ErrorMessage = result.ErrorMessage,
        };
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var quota = await GetQuotaAsync(cancellationToken);
        return quota is not null;
    }

    // ── HTTP 基础设施 ──────────────────────────────────────────────────────────────

    private async Task<LineSendResult> PostSendAsync(
        string path,
        object payload,
        LineOptions settings,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var attempts = Math.Max(0, settings.RetryMaxAttempts);
        LineSendResult? last = null;

        for (var attempt = 0; attempt <= attempts; attempt++)
        {
            var result = await PostSendCoreAsync(path, json, settings, cancellationToken);
            result.Attempts = attempt + 1;
            if (result.Success)
            {
                return result;
            }

            last = result;
            if (attempt < attempts && result.Error?.IsRetryable == true)
            {
                // 429/500/503：短退避 + 抖动，避免同一秒内重试打爆限流
                var delay = TimeSpan.FromMilliseconds(400 * (attempt + 1) + _jitter.Next(0, 300));
                _logger.LogWarning("LINE {Path} 返回 {Status}，{Delay}ms 后重试（第 {Attempt} 次）",
                    path, result.Error?.HttpStatus, delay.TotalMilliseconds, attempt + 1);
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                continue;
            }

            break;
        }

        return last ?? new LineSendResult { Success = false, ErrorMessage = "LINE 请求失败" };
    }

    private async Task<LineSendResult> PostSendCoreAsync(
        string path,
        string json,
        LineOptions settings,
        CancellationToken cancellationToken)
    {
        var url = $"{settings.ApiBaseUrl.TrimEnd('/')}{path}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ChannelAccessToken);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new LineSendResult { Success = true };
            }

            var error = ParseError((int)response.StatusCode, body);
            // 令牌/权限类错误按 Warning，业务类（404 非好友）按 Information，避免污染告警
            if (error.HttpStatus is 401 or 403)
            {
                _logger.LogWarning("LINE {Path} 调用失败 HTTP {Status}: {Message}", path, error.HttpStatus, error.Message);
            }
            else
            {
                _logger.LogInformation("LINE {Path} 调用失败 HTTP {Status}: {Message}", path, error.HttpStatus, error.Message);
            }

            return new LineSendResult
            {
                Success = false,
                ErrorMessage = BuildErrorMessage(error),
                Error = error,
                Unreachable = error.IsNotFriended || (error.IsQuotaOrPermission && LooksLikeTargetProblem(error)),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("LINE {Path} 请求超时", path);
            return new LineSendResult { Success = false, ErrorMessage = "LINE 请求超时" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LINE {Path} 请求异常", path);
            return new LineSendResult { Success = false, ErrorMessage = "LINE 请求异常：" + ex.Message };
        }
    }

    private async Task<JsonDocument?> PutJsonAsyncAndReturnNull(string path, object payload, LineOptions settings, CancellationToken ct)
        => (await PutJsonAsync(path, payload, settings, ct)).Document;

    private async Task<(bool Success, JsonDocument? Document, LineApiError? Error)> PutJsonAsync(
        string path,
        object payload,
        LineOptions settings,
        CancellationToken cancellationToken)
    {
        var url = $"{settings.ApiBaseUrl.TrimEnd('/')}{path}";
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ChannelAccessToken);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, TryParse(body), null);
            }

            var error = ParseError((int)response.StatusCode, body);
            _logger.LogWarning("LINE PUT {Path} 失败 HTTP {Status}: {Message}", path, error.HttpStatus, error.Message);
            return (false, null, error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LINE PUT {Path} 异常", path);
            return (false, null, null);
        }
    }

    private async Task<(bool Success, JsonDocument? Document, LineApiError? Error)> PostRawAsync(
        string path,
        string json,
        LineOptions settings,
        CancellationToken cancellationToken)
    {
        var url = $"{settings.ApiBaseUrl.TrimEnd('/')}{path}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ChannelAccessToken);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, TryParse(body), null);
            }

            var error = ParseError((int)response.StatusCode, body);
            _logger.LogInformation("LINE POST {Path} 失败 HTTP {Status}: {Message}", path, error.HttpStatus, error.Message);
            return (false, null, error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LINE POST {Path} 异常", path);
            return (false, null, null);
        }
    }

    private async Task<JsonDocument?> GetJsonAsync(
        string url,
        LineOptions settings,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ChannelAccessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("LINE GET {Url} 失败 HTTP {Status}", url, (int)response.StatusCode);
                return null;
            }

            return TryParse(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("LINE GET {Url} 超时", url);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LINE GET {Url} 异常", url);
            return null;
        }
    }

    private static JsonDocument? TryParse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LineApiError ParseError(int status, string? body)
    {
        var error = new LineApiError { HttpStatus = status };
        var doc = TryParse(body);
        if (doc is not null)
        {
            using (doc)
            {
                var root = doc.RootElement;
                error.Message = ReadString(root, "message") ?? string.Empty;
                if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in details.EnumerateArray())
                    {
                        var message = ReadString(item, "message");
                        var property = ReadString(item, "property");
                        if (!string.IsNullOrEmpty(message) || !string.IsNullOrEmpty(property))
                        {
                            error.Details.Add($"{property}: {message}");
                        }
                    }
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(body))
        {
            error.Message = body.Length > 200 ? body[..200] : body;
        }

        return error;
    }

    private static string BuildErrorMessage(LineApiError error)
    {
        var detail = error.Details.Count > 0 ? $"（{string.Join("; ", error.Details)}）" : string.Empty;
        return error.HttpStatus switch
        {
            401 => $"LINE 访问令牌无效或已过期：{error.Message}{detail}",
            403 => $"LINE 拒绝请求（配额或权限）：{error.Message}{detail}",
            404 => $"目标不可达（未添加机器人为好友或已屏蔽）：{error.Message}",
            409 => $"LINE 重复请求：{error.Message}",
            429 => $"LINE 触发限流，请稍后重试：{error.Message}",
            >= 500 => $"LINE 服务暂时不可用：{error.Message}",
            _ => $"LINE 请求失败（HTTP {error.HttpStatus}）：{error.Message}{detail}",
        };
    }

    /// <summary>replyToken 缺失/过期/已用：LINE 返回 400（Invalid reply token）或 409（重复使用）</summary>
    private static bool IsReplyTokenProblem(LineApiError? error)
    {
        if (error is null)
        {
            return false;
        }

        if (error.HttpStatus is 409)
        {
            return true;
        }

        if (error.HttpStatus != 400)
        {
            return false;
        }

        var text = $"{error.Message} {string.Join(" ", error.Details)}";
        return text.Contains("reply token", StringComparison.OrdinalIgnoreCase)
            || text.Contains("replyToken", StringComparison.OrdinalIgnoreCase)
            || text.Contains("expired", StringComparison.OrdinalIgnoreCase)
            || text.Contains("never valid", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>403 中区分"目标侧问题"（屏蔽/无权向该用户发送）与"账号侧配额问题"</summary>
    private static bool LooksLikeTargetProblem(LineApiError error)
    {
        var text = $"{error.Message} {string.Join(" ", error.Details)}";
        return text.Contains("blocked", StringComparison.OrdinalIgnoreCase)
            || text.Contains("not added", StringComparison.OrdinalIgnoreCase)
            || text.Contains("user", StringComparison.OrdinalIgnoreCase)
                && (text.Contains("unable", StringComparison.OrdinalIgnoreCase) || text.Contains("cannot", StringComparison.OrdinalIgnoreCase));
    }

    private static object BuildReplyPayload(string replyToken, IReadOnlyList<LineMessageObject> batch)
        => new { replyToken, messages = batch.Select(ToPayloadObject).ToArray() };

    private static object BuildPushPayload(string toUserId, IReadOnlyList<LineMessageObject> batch)
        => new { to = toUserId, messages = batch.Select(ToPayloadObject).ToArray() };

    /// <summary>把内部消息对象转成 LINE 报文对象（只输出该类型必要字段）</summary>
    private static object ToPayloadObject(LineMessageObject m)
    {
        switch (m.Type)
        {
            case "image":
                return new LineImagePayload
                {
                    Type = "image",
                    OriginalContentUrl = m.OriginalContentUrl ?? string.Empty,
                    PreviewImageUrl = m.PreviewImageUrl ?? m.OriginalContentUrl ?? string.Empty,
                };
            case "video":
                return new LineVideoPayload
                {
                    Type = "video",
                    OriginalContentUrl = m.OriginalContentUrl ?? string.Empty,
                    PreviewImageUrl = m.PreviewImageUrl ?? string.Empty,
                    DurationMs = m.DurationMs ?? 0,
                };
            case "audio":
                return new LineAudioPayload
                {
                    Type = "audio",
                    OriginalContentUrl = m.OriginalContentUrl ?? string.Empty,
                    DurationMs = m.DurationMs ?? 0,
                };
            case "file":
                return new LineFilePayload
                {
                    Type = "file",
                    OriginalContentUrl = m.OriginalContentUrl ?? string.Empty,
                    FileName = m.FileName ?? "attachment",
                    FileSize = m.FileSize ?? 0,
                };
            case "location":
                return new LineLocationPayload
                {
                    Type = "location",
                    Title = m.Title ?? string.Empty,
                    Address = m.Address ?? string.Empty,
                    Latitude = m.Latitude ?? 0,
                    Longitude = m.Longitude ?? 0,
                };
            default:
                return new LineTextPayload
                {
                    Type = "text",
                    Text = m.Text ?? string.Empty,
                    QuoteMessageId = m.QuoteMessageId,
                };
        }
    }

    private static List<IReadOnlyList<LineMessageObject>> SplitIntoBatches(
        IReadOnlyList<LineMessageObject> messages,
        int maxPerRequest)
    {
        var size = maxPerRequest > 0 ? maxPerRequest : 5;
        var batches = new List<IReadOnlyList<LineMessageObject>>();
        for (var i = 0; i < messages.Count; i += size)
        {
            batches.Add(messages.Skip(i).Take(size).ToList());
        }

        return batches;
    }

    /// <summary>
    /// 文本分片：按 LINE 字符上限切分，且不在代理对（emoji）中间断开、优先在换行处断。
    /// </summary>
    internal static List<string> SplitText(string text, int maxChars)
    {
        var pieces = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return pieces;
        }

        var index = 0;
        while (index < text.Length)
        {
            var remaining = text.Length - index;
            if (remaining <= maxChars)
            {
                pieces.Add(text[index..]);
                break;
            }

            var take = maxChars;
            var candidate = index + take;

            // 不切断代理对：若切点落在高代理项之后，退一个字符
            if (char.IsHighSurrogate(text[candidate - 1]))
            {
                take--;
            }

            // 优先在最近的换行处断开（太靠前才用硬切）
            var lastNewLine = text.LastIndexOf('\n', index + take - 1, Math.Min(take, take));
            if (lastNewLine > index + take / 2)
            {
                take = lastNewLine - index;
            }

            if (take <= 0)
            {
                take = 1;
            }

            pieces.Add(text.Substring(index, take));
            index += take;
        }

        return pieces;
    }

    private static string? ParseFileName(string? contentDispositionFileName)
    {
        if (string.IsNullOrWhiteSpace(contentDispositionFileName))
        {
            return null;
        }

        return contentDispositionFileName.Trim().Trim('"');
    }

    private static string? ReadString(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt32(JsonElement parent, string name, int fallback)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : fallback;
}

// LINE 报文对象（严格字段名，System.Text.Json 默认 PascalCase 会写错，故显式 JsonPropertyName）
internal sealed class LineTextPayload
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [System.Text.Json.Serialization.JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("quoteMessageId")]
    public string? QuoteMessageId { get; set; }
}

internal sealed class LineImagePayload
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; set; } = "image";

    [System.Text.Json.Serialization.JsonPropertyName("originalContentUrl")]
    public string OriginalContentUrl { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("previewImageUrl")]
    public string PreviewImageUrl { get; set; } = string.Empty;
}

internal sealed class LineVideoPayload
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; set; } = "video";

    [System.Text.Json.Serialization.JsonPropertyName("originalContentUrl")]
    public string OriginalContentUrl { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("previewImageUrl")]
    public string PreviewImageUrl { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("duration")]
    public int DurationMs { get; set; }
}

internal sealed class LineAudioPayload
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; set; } = "audio";

    [System.Text.Json.Serialization.JsonPropertyName("originalContentUrl")]
    public string OriginalContentUrl { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("duration")]
    public int DurationMs { get; set; }
}

internal sealed class LineFilePayload
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; set; } = "file";

    [System.Text.Json.Serialization.JsonPropertyName("originalContentUrl")]
    public string OriginalContentUrl { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("fileSize")]
    public long FileSize { get; set; }
}

internal sealed class LineLocationPayload
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; set; } = "location";

    [System.Text.Json.Serialization.JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("longitude")]
    public double Longitude { get; set; }
}
