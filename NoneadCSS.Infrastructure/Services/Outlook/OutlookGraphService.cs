#nullable enable
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Infrastructure.Services.Outlook;

/// <summary>
/// Microsoft Graph API 服务 - 与 Outlook 日历进行 CRUD 交互
/// 文档：https://learn.microsoft.com/graph/api/resources/event
/// </summary>
public class OutlookGraphService : IOutlookGraphService
{
    // C-O3: 通过 IHttpClientFactory 注入，避免 static HttpClient 的 DNS 过期和连接耗尽问题
    private readonly HttpClient _httpClient;
    private readonly OutlookSyncOptions _options;
    private readonly ILogger<OutlookGraphService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // H-O4: 429 限流重试配置
    private const int MaxRetries = 3;
    private const int DefaultRetryDelaySeconds = 5;

    // H-O5: 分页安全上限，防止恶意/异常数据导致无限分页
    private const int MaxPages = 50;

    public OutlookGraphService(HttpClient httpClient, IOptions<OutlookSyncOptions> options, IOptions<HttpClientOptions> httpClientOptions, ILogger<OutlookGraphService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient, nameof(httpClient));
        ArgumentNullException.ThrowIfNull(options, nameof(options));
        ArgumentNullException.ThrowIfNull(httpClientOptions, nameof(httpClientOptions));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));

        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(httpClientOptions.Value.OutlookGraphTimeoutSeconds);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OutlookUserInfo?> GetUserInfoAsync(string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        const string url = "https://graph.microsoft.com/v1.0/me";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await SendAsyncWithRetry(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            LogGraphError("GetUserInfo", response.StatusCode, body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new OutlookUserInfo
        {
            Email = root.TryGetProperty("mail", out var mail) && mail.ValueKind == JsonValueKind.String
                ? mail.GetString() ?? string.Empty
                : (root.TryGetProperty("userPrincipalName", out var upn) ? upn.GetString() ?? string.Empty : string.Empty),
            ObjectId = root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            DisplayName = root.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? string.Empty : string.Empty
        };
    }

    /// <inheritdoc />
    public async Task<string?> CreateEventAsync(string accessToken, CalendarEvent calendarEvent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentNullException.ThrowIfNull(calendarEvent);

        var url = $"{_options.GraphBaseUrl}/me/events";
        var payload = BuildEventPayload(calendarEvent);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await SendAsyncWithRetry(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            LogGraphError("CreateEvent", response.StatusCode, body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateEventAsync(string accessToken, string outlookEventId, CalendarEvent calendarEvent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(outlookEventId);
        ArgumentNullException.ThrowIfNull(calendarEvent);

        var url = $"{_options.GraphBaseUrl}/me/events/{Uri.EscapeDataString(outlookEventId)}";
        var payload = BuildEventPayload(calendarEvent);

        using var request = new HttpRequestMessage(HttpMethod.Patch, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await SendAsyncWithRetry(request).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) return true;

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        LogGraphError("UpdateEvent", response.StatusCode, body);
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteEventAsync(string accessToken, string outlookEventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(outlookEventId);

        var url = $"{_options.GraphBaseUrl}/me/events/{Uri.EscapeDataString(outlookEventId)}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await SendAsyncWithRetry(request).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) return true;
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return true;

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        LogGraphError("DeleteEvent", response.StatusCode, body);
        return false;
    }

    /// <inheritdoc />
    public async Task<List<OutlookEventDto>> ListEventsAsync(string accessToken, DateTime startDateTime, DateTime endDateTime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["startDateTime"] = startDateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        query["endDateTime"] = endDateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        query["$select"] = "id,subject,bodyPreview,body,start,end,isAllDay,location,onlineMeetingUrl,lastModifiedDateTime,createdDateTime";
        query["$top"] = "200";

        var url = $"{_options.GraphBaseUrl}/me/calendarView?{query}";

        var events = new List<OutlookEventDto>();
        string? nextLink = url;
        var pageCount = 0;

        while (!string.IsNullOrEmpty(nextLink) && pageCount < MaxPages)
        {
            pageCount++;

            using var request = new HttpRequestMessage(HttpMethod.Get, nextLink);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await SendAsyncWithRetry(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogGraphError("ListEvents", response.StatusCode, body);
                break;
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("value", out var valueProp)) break;

            foreach (var item in valueProp.EnumerateArray())
            {
                events.Add(ParseOutlookEvent(item));
            }

            if (doc.RootElement.TryGetProperty("@odata.nextLink", out var nextLinkProp)
                && nextLinkProp.ValueKind == JsonValueKind.String)
            {
                nextLink = nextLinkProp.GetString();
            }
            else
            {
                nextLink = null;
            }
        }

        if (pageCount >= MaxPages && !string.IsNullOrEmpty(nextLink))
        {
            _logger.LogWarning("Graph 列出事件达到分页上限 {MaxPages}，可能存在更多未拉取数据", MaxPages);
        }

        return events;
    }

    /// <inheritdoc/>
    public async Task<(List<OutlookEventDto> Events, string? NewDeltaToken, bool IsDeltaExpired)>
        ListEventsDeltaAsync(string accessToken, DateTime startDate, DateTime endDate, string? deltaToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var allEvents = new List<OutlookEventDto>();
        string? lastDeltaLink = null;

        var url = deltaToken != null
            ? deltaToken
            : BuildFirstDeltaUrl(startDate, endDate);

        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await SendAsyncWithRetry(request).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Gone ||
                response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (new List<OutlookEventDto>(), null, true);
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogGraphError("ListEventsDelta", response.StatusCode, body);
                response.EnsureSuccessStatusCode();
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("value", out var valueArr) && valueArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in valueArr.EnumerateArray())
                {
                    var dto = ParseOutlookEvent(item);
                    if (item.TryGetProperty("@removed", out var removed)
                        && removed.ValueKind == JsonValueKind.Object
                        && removed.TryGetProperty("reason", out var reason)
                        && reason.GetString() == "deleted")
                    {
                        dto.IsDeleted = true;
                    }
                    allEvents.Add(dto);
                }
            }

            if (root.TryGetProperty("@odata.nextLink", out var nextLink)
                && nextLink.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(nextLink.GetString()))
            {
                url = nextLink.GetString()!;
            }
            else
            {
                if (root.TryGetProperty("@odata.deltaLink", out var deltaLinkProp)
                    && deltaLinkProp.ValueKind == JsonValueKind.String)
                {
                    lastDeltaLink = deltaLinkProp.GetString();
                }
                break;
            }
        }

        return (allEvents, lastDeltaLink, false);
    }

    private string BuildFirstDeltaUrl(DateTime startDate, DateTime endDate)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["startDateTime"] = startDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        query["endDateTime"] = endDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        query["$select"] = "id,subject,bodyPreview,body,start,end,isAllDay,location,onlineMeetingUrl,lastModifiedDateTime,createdDateTime";
        query["$top"] = "200";
        return $"{_options.GraphBaseUrl}/me/calendarView/delta?{query}";
    }

    /// <summary>
    /// H-O4: 发送 HTTP 请求并处理 429 限流重试。
    /// 解析 Retry-After 响应头，按指示延迟后重试。
    /// </summary>
    private async Task<HttpResponseMessage> SendAsyncWithRetry(HttpRequestMessage request)
    {
        HttpResponseMessage? response = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var requestToSend = attempt == 0 ? request : await CloneRequestAsync(request).ConfigureAwait(false);

            response?.Dispose();
            response = await _httpClient.SendAsync(requestToSend).ConfigureAwait(false);

            if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
                return response!;

            if (attempt >= MaxRetries)
            {
                _logger.LogWarning("Graph API 429 限流，已达到最大重试次数 {MaxRetries}", MaxRetries);
                return response!;
            }

            var delaySeconds = DefaultRetryDelaySeconds;
            if (response.Headers.TryGetValues("Retry-After", out var retryValues))
            {
                var retryValue = retryValues.FirstOrDefault();
                if (retryValue != null && int.TryParse(retryValue, out var parsedSeconds))
                {
                    delaySeconds = Math.Min(parsedSeconds, 60);
                }
            }

            _logger.LogWarning("Graph API 429 限流，{Delay}s 后重试（第 {Attempt}/{Max} 次）",
                delaySeconds, attempt + 1, MaxRetries);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ConfigureAwait(false);
        }

        return response!;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (original.Content != null)
        {
            var contentBytes = await original.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            clone.Content = new ByteArrayContent(contentBytes);
            clone.Content.Headers.ContentType = original.Content.Headers.ContentType;
        }

        return clone;
    }

    /// <summary>
    /// H-O1: 记录 Graph API 错误日志（脱敏，截断响应体到 500 字符，不记录完整响应）
    /// </summary>
    private void LogGraphError(string operation, System.Net.HttpStatusCode statusCode, string responseBody)
    {
        // H-O1: 截断响应体，防止日志中泄露敏感信息（如 token、个人信息）
        var truncatedBody = responseBody.Length > 500
            ? responseBody[..500] + "...[truncated]"
            : responseBody;

        // 尝试提取 error/message 字段，仅记录结构化错误信息
        string? errorMessage = null;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var errorProp))
            {
                errorMessage = errorProp.TryGetProperty("message", out var msgProp)
                    ? msgProp.GetString()
                    : errorProp.GetRawText();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OutlookGraphService: failed to parse Graph error body as JSON");
        }

        _logger.LogError("Graph {Operation} 调用失败：{StatusCode} Error={Error} BodyPreview={BodyPreview}",
            operation, statusCode, errorMessage ?? "N/A", truncatedBody);
    }

    /// <summary>
    /// 构造 Graph API 事件 JSON 负载
    /// H-O7: 仅同步基本字段（标题、描述、时间、全天、地点），不同步敏感字段（参会人、子任务、会议链接等）
    /// </summary>
    private static string BuildEventPayload(CalendarEvent ev)
    {
        var payload = new
        {
            subject = string.IsNullOrWhiteSpace(ev.Title) ? "(无标题)" : ev.Title,
            body = new
            {
                contentType = "text",
                content = ev.Description ?? string.Empty
            },
            start = new
            {
                dateTime = ev.StartAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
                timeZone = "UTC"
            },
            end = new
            {
                dateTime = (ev.EndAt ?? ev.StartAt).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
                timeZone = "UTC"
            },
            isAllDay = ev.IsAllDay,
            location = new
            {
                displayName = ev.Location ?? string.Empty
            }
            // H-O7: 故意不包含 attendees、onlineMeetingUrl 等敏感字段，避免内部信息泄露到 Outlook
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static OutlookEventDto ParseOutlookEvent(JsonElement item)
    {
        var dto = new OutlookEventDto
        {
            Id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
            // H-O8: 对从 Outlook 拉取的文本字段进行 HTML 净化，防止恶意脚本注入本地存储与前端渲染
            Subject = HtmlSanitizerHelper.Sanitize(item.TryGetProperty("subject", out var subj) ? subj.GetString() : null),
            BodyPreview = item.TryGetProperty("bodyPreview", out var bp) ? bp.GetString() : null,
            IsAllDay = item.TryGetProperty("isAllDay", out var iad) && iad.GetBoolean(),
            Location = HtmlSanitizerHelper.Sanitize(
                item.TryGetProperty("location", out var loc) && loc.TryGetProperty("displayName", out var dn)
                    ? dn.GetString()
                    : null),
            OnlineMeetingUrl = item.TryGetProperty("onlineMeetingUrl", out var omu) ? omu.GetString() : null
        };

        if (item.TryGetProperty("body", out var bodyProp))
        {
            dto.BodyContentType = bodyProp.TryGetProperty("contentType", out var ct) ? ct.GetString() : null;
            var rawBody = bodyProp.TryGetProperty("content", out var c) ? c.GetString() : null;
            // H-O8: Body 可能是 HTML（contentType=html），统一走白名单消毒，剥离 <script>/on* 等危险节点
            dto.BodyContent = HtmlSanitizerHelper.Sanitize(rawBody);
        }

        if (item.TryGetProperty("start", out var startProp) && startProp.TryGetProperty("dateTime", out var sd)
            && DateTime.TryParse(sd.GetString(), null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var startDt))
        {
            dto.Start = startDt;
            dto.StartTimeZone = startProp.TryGetProperty("timeZone", out var tz) ? tz.GetString() : null;
        }

        if (item.TryGetProperty("end", out var endProp) && endProp.TryGetProperty("dateTime", out var ed)
            && DateTime.TryParse(ed.GetString(), null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var endDt))
        {
            dto.End = endDt;
            dto.EndTimeZone = endProp.TryGetProperty("timeZone", out var tz) ? tz.GetString() : null;
        }

        if (item.TryGetProperty("lastModifiedDateTime", out var lmd)
            && DateTime.TryParse(lmd.GetString(), null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var lmdDt))
        {
            dto.LastModifiedDateTime = lmdDt;
        }

        if (item.TryGetProperty("createdDateTime", out var cd)
            && DateTime.TryParse(cd.GetString(), null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var cdDt))
        {
            dto.CreatedDateTime = cdDt;
        }

        return dto;
    }
}
