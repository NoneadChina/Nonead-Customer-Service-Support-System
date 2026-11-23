using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Infrastructure.Services.DingDing;

/// <summary>
/// 钉钉 Stream 模式协议客户端（原生实现，无需额外 NuGet）。
/// 协议流程（https://opensource.dingtalk.com/developerpedia/docs/learn/stream/protocol）：
///   1. POST /v1.0/gateway/connections/open  获取 wss endpoint + ticket（90s 有效，一次性）
///   2. 以 ticket 为 query 参数建立 WebSocket 长连接
///   3. 接收推送并回复 ACK（code=200 + messageId 回传）
///      - CALLBACK(机器人消息): data={"response":null}
///      - EVENT(事件推送):     data={"status":"SUCCESS|LATER","message":"..."}
///      - SYSTEM/ping:         data={"opaque": request.opaque}
///   4. 异常/网络断开/收到 disconnect → 指数退避重连
/// </summary>
public class DingTalkStreamClient
{
    private const string ApiBaseUrl = "https://api.dingtalk.com";
    private const string SdkUa = "noneadcss-stream-dotnet/1.0";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly IDingDingSettingsProvider _settingsProvider;
    private readonly IDingDingIngestionService _ingestionService;
    private readonly IDingTalkStreamConnectionStatus _connectionStatus;
    private readonly ILogger<DingTalkStreamClient> _logger;

    public DingTalkStreamClient(
        HttpClient httpClient,
        IDingDingSettingsProvider settingsProvider,
        IDingDingIngestionService ingestionService,
        IDingTalkStreamConnectionStatus connectionStatus,
        ILogger<DingTalkStreamClient> logger)
    {
        _httpClient = httpClient;
        _settingsProvider = settingsProvider;
        _ingestionService = ingestionService;
        _connectionStatus = connectionStatus;
        _logger = logger;
    }

    /// <summary>
    /// 启动 Stream 长连接并循环处理消息。仅在 ReceiveMode == "Stream" 且凭证有效时运行。
    /// 外部通过 CancellationToken 关闭；内部任何异常都会触发退避重连。
    /// </summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var retryDelay = TimeSpan.FromSeconds(3);
        var maxRetryDelay = TimeSpan.FromMinutes(2);

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = _settingsProvider.Current;
            if (!settings.Enabled
                || !settings.IsConfigured
                || !string.Equals(settings.ReceiveMode, "Stream", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("钉钉 Stream 模式未启用（Enabled={Enabled}, Mode={ReceiveMode}），10s 后再次检查",
                    settings.Enabled, settings.ReceiveMode);
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) { return; }
                retryDelay = TimeSpan.FromSeconds(3);
                continue;
            }

            try
            {
                _logger.LogInformation("钉钉 Stream 模式开始连接（AppKey={AppKey}）", settings.AppKey);
                using var conn = await OpenConnectionAsync(settings.AppKey!, settings.AppSecret!, stoppingToken);
                _logger.LogInformation("钉钉 Stream 连接成功 endpoint={Endpoint}", conn.Endpoint);
                _connectionStatus.SetConnected();
                retryDelay = TimeSpan.FromSeconds(3); // 连接成功后重置退避时间（与 FeishuStreamClient 行为一致）
                await ProcessLoopAsync(conn.WebSocket, stoppingToken);
                // ProcessLoop 正常返回表示服务端关闭或网络断开
                _connectionStatus.SetDisconnected("stream closed by server");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _connectionStatus.SetDisconnected("application shutdown");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "钉钉 Stream 连接中断，{Delay}s 后重连",
                    Math.Round(retryDelay.TotalSeconds, 1));
                _connectionStatus.SetDisconnected(ex.Message);
                try { await Task.Delay(retryDelay, stoppingToken); }
                catch (OperationCanceledException) { return; }
                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, maxRetryDelay.Ticks));
            }
        }
    }

    // ===== 协议步骤一：HTTP 握手注册 endpoint+ticket =====
    private async Task<StreamConnection> OpenConnectionAsync(
        string clientId, string clientSecret, CancellationToken ct)
    {
        var localIps = string.Join(",", Dns.GetHostAddresses(Dns.GetHostName())
            .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(ip => ip.ToString())
            .DefaultIfEmpty("127.0.0.1"));

        var requestBody = JsonSerializer.Serialize(new
        {
            clientId,
            clientSecret,
            localIp = localIps,
            subscriptions = new[]
            {
                new { topic = "/v1.0/im/bot/messages/get", type = "CALLBACK" },
                new { topic = "*", type = "EVENT" },
            },
            ua = SdkUa
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/v1.0/gateway/connections/open")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };

        using var resp = await _httpClient.SendAsync(req, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"钉钉 Stream 注册连接失败 status={(int)resp.StatusCode} body={respBody}");
        }

        var openResp = JsonSerializer.Deserialize<OpenConnectionResponse>(respBody, JsonOpts)
                       ?? throw new InvalidOperationException("钉钉 Stream 注册返回为空");
        if (string.IsNullOrEmpty(openResp.Endpoint) || string.IsNullOrEmpty(openResp.Ticket))
        {
            throw new InvalidOperationException($"钉钉 Stream 注册缺少 endpoint/ticket body={respBody}");
        }

        var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        var builder = new UriBuilder(openResp.Endpoint);
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? $"ticket={Uri.EscapeDataString(openResp.Ticket)}"
            : $"{builder.Query}&ticket={Uri.EscapeDataString(openResp.Ticket)}";

        await ws.ConnectAsync(builder.Uri, ct);
        return new StreamConnection(ws, openResp.Endpoint);
    }

    // ===== 协议步骤二：接收-处理-ACK 主循环 =====
    private async Task ProcessLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        // 8KB buffer，多段 WebSocket 消息通过 MemoryStream 拼接
        var recvBuffer = new byte[8192];
        var ms = new MemoryStream();
        var outstandingAck = new ConcurrentDictionary<string, byte>();

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            ms.Position = 0;
            ms.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                try
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(recvBuffer), ct);
                }
                catch (WebSocketException ex)
                {
                    // 服务端未完成握手即关闭（如连接空闲超时）属预期网络事件，Debug 级即可，避免噪音
                    _logger.LogDebug(ex, "钉钉 Stream WebSocket 连接关闭，触发重连");
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("钉钉 Stream 服务端请求关闭 CloseStatus={CloseStatus} Desc={CloseStatusDescription}",
                        result.CloseStatus, result.CloseStatusDescription);
                    return;
                }
                if (result.Count > 0)
                    ms.Write(recvBuffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (ms.Length == 0) continue;
            var raw = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            StreamEnvelope? envelope;
            try { envelope = JsonSerializer.Deserialize<StreamEnvelope>(raw, JsonOpts); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "钉钉 Stream 推送解析失败 raw={Raw}", Truncate(raw, 500));
                continue;
            }
            if (envelope == null) continue;

            var messageId = envelope.Headers?.MessageId ?? Guid.NewGuid().ToString("N");
            if (!outstandingAck.TryAdd(messageId, 0))
            {
                // 幂等：同一 messageId 已在处理中，直接 ACK 跳过
                await SendAckAsync(ws, messageId, 200, "OK", "{\"response\": null}", ct);
                continue;
            }
            try
            {
                var ack = await DispatchEnvelopeAsync(envelope, ct);
                await SendAckAsync(ws, messageId, ack.Code, ack.Message, ack.DataJson, ct);
            }
            catch (Exception ex)
            {
                // 服务端主动 disconnect（reason=persistent connection is timeout 等）属预期事件，
                // 上一条已记录 Info；此处仅 Debug 记录，避免正常重连循环产生 Error 噪音
                if (ex is InvalidOperationException ioe && ioe.Message.StartsWith("stream disconnect:", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug(ex, "钉钉 Stream 收到预期 disconnect，准备重连 messageId={MessageId}", messageId);
                }
                else
                {
                    _logger.LogError(ex, "钉钉 Stream 消息处理异常 messageId={MessageId}", messageId);
                }
                await SendAckAsync(ws, messageId, 500, "internal error",
                    "{\"status\":\"LATER\",\"message\":\"processing error\"}", ct);
            }
            finally
            {
                outstandingAck.TryRemove(messageId, out _);
                // T9: 逐事件清空变更跟踪器，防止长连接复用 scoped DbContext 时 ChangeTracker 无限累积
                try { _ingestionService.ResetTracking(); }
                catch (Exception resetEx) { _logger.LogDebug(resetEx, "重置钉钉摄入跟踪器失败"); }
            }
        }
    }

    // ===== 分发：SYSTEM / EVENT / CALLBACK =====
    private async Task<DispatchAck> DispatchEnvelopeAsync(StreamEnvelope env, CancellationToken ct)
    {
        var topic = env.Headers?.Topic ?? string.Empty;
        _logger.LogInformation("钉钉 Stream 收到消息 envelope：Type={Type}, Topic={Topic}, MessageId={MessageId}, DataLength={DataLength}",
            env.Type, topic, env.Headers?.MessageId, env.Data?.Length ?? 0);

        switch (env.Type)
        {
            case "SYSTEM":
                if (topic == "ping")
                {
                    var opaque = ExtractStringField(env.Data, "opaque");
                    return new DispatchAck(200, "OK",
                        $"{{\"opaque\":{JsonSerializer.Serialize(opaque)}}}");
                }
                if (topic == "disconnect")
                {
                    var reason = ExtractStringField(env.Data, "reason");
                    _logger.LogInformation("钉钉 Stream 收到 disconnect 通知 reason={Reason}，准备重连", reason);
                    throw new InvalidOperationException($"stream disconnect: {reason}");
                }
                return new DispatchAck(200, "OK", "{}");

            case "CALLBACK":
                if (topic == "/v1.0/im/bot/messages/get")
                {
                    if (string.IsNullOrEmpty(env.Data))
                        return new DispatchAck(200, "OK", "{\"response\": null}");
                    _logger.LogInformation("钉钉 Stream 机器人 CALLBACK 消息进入处理：DataPreview={DataPreview}",
                        env.Data.Length > 100 ? env.Data.Substring(0, 100) : env.Data);
                    var cbResult = await _ingestionService.ProcessWebhookPayloadAsync(env.Data);
                    if (!string.IsNullOrEmpty(cbResult.ErrorMessage))
                    {
                        _logger.LogWarning("钉钉 Stream 机器人消息处理失败：{Error}", cbResult.ErrorMessage);
                    }
                    else
                    {
                        _logger.LogInformation("钉钉 Stream 机器人消息处理完成：ConversationId={ConversationId}, IsNew={IsNew}, NewCount={NewCount}, ReplyCount={ReplyCount}",
                            cbResult.ConversationId, cbResult.NewConversationCount > 0, cbResult.NewConversationCount, cbResult.ReplyCount);
                    }
                    return new DispatchAck(200, "OK", "{\"response\": null}");
                }
                else
                {
                    _logger.LogWarning("钉钉 Stream CALLBACK topic 不匹配，跳过：Topic={Topic}, DataPreview={DataPreview}",
                        topic, env.Data?.Length > 100 ? env.Data.Substring(0, 100) : env.Data);
                    return new DispatchAck(200, "OK", "{\"response\": null}");
                }

            case "EVENT":
                _logger.LogDebug("钉钉 Stream 事件推送 eventType={EventType} eventId={EventId}",
                    env.Headers?.EventType, env.Headers?.EventId);
                return new DispatchAck(200, "OK", "{\"status\":\"SUCCESS\",\"message\":\"ok\"}");

            default:
                _logger.LogWarning("钉钉 Stream 未处理的消息类型 type={Type} topic={Topic}", env.Type, topic);
                return new DispatchAck(404, "topic not handled", "{\"response\": null}");
        }
    }

    // ===== ACK 发送 =====
    private static async Task SendAckAsync(ClientWebSocket ws, string messageId,
        int code, string message, string dataJson, CancellationToken ct)
    {
        var ack = JsonSerializer.Serialize(new
        {
            code,
            message,
            headers = new { messageId, contentType = "application/json" },
            data = dataJson
        });
        var bytes = Encoding.UTF8.GetBytes(ack);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private static string? ExtractStringField(string? json, string field)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var el) ? el.GetString() : null;
        }
        catch { return null; }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    // ===== DTO =====
    private sealed record StreamConnection(ClientWebSocket WebSocket, string Endpoint) : IDisposable
    {
        public void Dispose()
        {
            // 直接 Dispose WebSocket，底层会处理关闭握手，避免 sync-over-async 阻塞
            WebSocket.Dispose();
        }
    }

    private sealed record OpenConnectionResponse(string? Endpoint, string? Ticket);

    private sealed record StreamHeaders(
        [property: System.Text.Json.Serialization.JsonPropertyName("topic")] string? Topic,
        [property: System.Text.Json.Serialization.JsonPropertyName("messageId")] string? MessageId,
        [property: System.Text.Json.Serialization.JsonPropertyName("contentType")] string? ContentType,
        [property: System.Text.Json.Serialization.JsonPropertyName("eventType")] string? EventType,
        [property: System.Text.Json.Serialization.JsonPropertyName("eventId")] string? EventId
    );

    private sealed record StreamEnvelope(
        [property: System.Text.Json.Serialization.JsonPropertyName("specVersion")] string? SpecVersion,
        [property: System.Text.Json.Serialization.JsonPropertyName("type")] string? Type,
        [property: System.Text.Json.Serialization.JsonPropertyName("headers")] StreamHeaders? Headers,
        [property: System.Text.Json.Serialization.JsonPropertyName("data")] string? Data
    );

    private sealed record DispatchAck(int Code, string Message, string DataJson);
}
