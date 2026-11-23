using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Infrastructure.Services.Feishu;

/// <summary>
/// 飞书 Stream 长连接客户端（原生实现，无需额外 NuGet）。
/// 协议流程（https://open.feishu.cn/document/server-docs/server-side-sdk）：
///   1. POST /callback/ws/endpoint  获取 WebSocket 连接 URL + 客户端配置（PingInterval/Reconnect*）
///   2. 以返回的 URL 建立 WebSocket 长连接
///   3. 按 PingInterval 发送 Protobuf Ping 控制帧；服务端回 Pong
///   4. 接收 Protobuf 数据帧 → 解析 JSON payload → 发送 ACK → 交由 IFeishuIngestionService 处理
///   5. 异常/网络断开 → 指数退避重连
/// </summary>
public class FeishuStreamClient
{
    private const string ApiBaseUrl = "https://open.feishu.cn";
    private const string WsEndpointUri = "/callback/ws/endpoint";
    private const string SdkUa = "noneadcss-feishu-stream-dotnet/1.0";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly IFeishuSettingsProvider _settingsProvider;
    private readonly IFeishuIngestionService _ingestionService;
    private readonly IFeishuStreamConnectionStatus _connectionStatus;
    private readonly ILogger<FeishuStreamClient> _logger;

    // 连接配置（由 /callback/ws/endpoint 返回）
    private int _serviceId;
    private TimeSpan _pingInterval = TimeSpan.FromSeconds(30);
    private TimeSpan _reconnectInterval = TimeSpan.FromMinutes(1);
    private int _reconnectCount = -1; // -1 = 无限重连

    // 幂等去重：跟踪已处理的事件 ID
    // T27: 幂等去重缓存——值存储事件首次出现的时间戳（Ticks），按 TTL 滑动窗口淘汰
    private readonly ConcurrentDictionary<string, long> _processedEventIds = new();
    private static readonly TimeSpan DedupTtl = TimeSpan.FromMinutes(30);

    public FeishuStreamClient(
        HttpClient httpClient,
        IFeishuSettingsProvider settingsProvider,
        IFeishuIngestionService ingestionService,
        IFeishuStreamConnectionStatus connectionStatus,
        ILogger<FeishuStreamClient> logger)
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
                _logger.LogDebug("飞书 Stream 模式未启用（Enabled={Enabled}, Mode={ReceiveMode}），10s 后再次检查",
                    settings.Enabled, settings.ReceiveMode);
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) { return; }
                retryDelay = TimeSpan.FromSeconds(3);
                continue;
            }

            // Stream 模式需要 AppId + AppSecret（不需要 VerificationToken）
            if (string.IsNullOrWhiteSpace(settings.AppId) || string.IsNullOrWhiteSpace(settings.AppSecret))
            {
                _logger.LogWarning("飞书 Stream 模式缺少 AppId/AppSecret，10s 后再次检查");
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            try
            {
                _logger.LogInformation("飞书 Stream 模式开始连接（AppId={AppId}, AppSecretLength={AppSecretLength}）",
                    settings.AppId, settings.AppSecret?.Length ?? 0);
                using var conn = await OpenConnectionAsync(settings.AppId!, settings.AppSecret!, stoppingToken);
                _logger.LogInformation("飞书 Stream 连接成功 endpoint={Endpoint}", conn.Endpoint);
                _connectionStatus.SetConnected();
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
                _logger.LogError(ex, "飞书 Stream 连接中断，{Delay}s 后重连",
                    Math.Round(retryDelay.TotalSeconds, 1));
                _connectionStatus.SetDisconnected(ex.Message);
                var signaled = await _connectionStatus.WaitForReconnectSignalAsync(retryDelay, stoppingToken);
                if (stoppingToken.IsCancellationRequested)
                    return;
                if (signaled)
                {
                    _logger.LogInformation("飞书 Stream 收到重连信号，立即重连");
                    // 配置可能已变更，刷新本作用域的 settings 缓存
                    try { await _settingsProvider.RefreshAsync(); }
                    catch (Exception refreshEx) { _logger.LogWarning(refreshEx, "重连前刷新 settings 失败，使用缓存值"); }
                    retryDelay = TimeSpan.FromSeconds(3); // 重置退避
                    continue; // 跳过退避翻倍，直接重试
                }
                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, maxRetryDelay.Ticks));
            }
        }
    }

    // ===== 协议步骤一：HTTP 获取 WebSocket 连接 URL =====
    private async Task<StreamConnection> OpenConnectionAsync(
        string appId, string appSecret, CancellationToken ct)
    {
        var requestBody = JsonSerializer.Serialize(new
        {
            AppID = appId,
            AppSecret = appSecret
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}{WsEndpointUri}")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("locale", "zh");
        req.Headers.Add("User-Agent", SdkUa);

        using var resp = await _httpClient.SendAsync(req, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"飞书 Stream 获取连接 URL 失败 status={(int)resp.StatusCode} body={respBody}");
        }

        var endpointResp = JsonSerializer.Deserialize<WsEndpointResponse>(respBody, JsonOpts)
                           ?? throw new InvalidOperationException("飞书 Stream 响应为空");
        if (endpointResp.Code != 0)
        {
            throw new InvalidOperationException(
                $"飞书 Stream 获取连接 URL 失败 code={endpointResp.Code} msg={endpointResp.Msg}");
        }

        var data = endpointResp.Data;
        if (data == null || string.IsNullOrEmpty(data.URL))
        {
            throw new InvalidOperationException($"飞书 Stream 响应缺少 URL body={respBody}");
        }

        // 解析 ClientConfig
        if (data.ClientConfig != null)
        {
            if (data.ClientConfig.PingInterval > 0)
                _pingInterval = TimeSpan.FromSeconds(data.ClientConfig.PingInterval);
            if (data.ClientConfig.ReconnectInterval > 0)
                _reconnectInterval = TimeSpan.FromSeconds(data.ClientConfig.ReconnectInterval);
            _reconnectCount = data.ClientConfig.ReconnectCount; // -1 = 无限
        }

        // 从 URL query 中解析 service_id
        var uri = new Uri(data.URL);
        var query = HttpUtility.ParseQueryString(uri.Query);
        var serviceIdStr = query["service_id"];
        if (!string.IsNullOrEmpty(serviceIdStr) && int.TryParse(serviceIdStr, out var sid))
            _serviceId = sid;

        _logger.LogInformation("飞书 Stream 连接配置：PingInterval={PingInterval}s, ReconnectInterval={ReconnectInterval}s, ServiceId={ServiceId}",
            _pingInterval.TotalSeconds, _reconnectInterval.TotalSeconds, _serviceId);

        var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

        await ws.ConnectAsync(uri, ct);
        return new StreamConnection(ws, data.URL);
    }

    // ===== 协议步骤二：接收-处理-ACK 主循环 + 心跳并行 =====
    private async Task ProcessLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        // 创建链接级 CTS：心跳和接收任一异常都通知对方退出
        using var linkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pingTask = PingLoopAsync(ws, linkCts.Token);
        var receiveTask = ReceiveLoopAsync(ws, linkCts.Token);

        // 等待任一任务完成（异常或正常退出）
        var completed = await Task.WhenAny(pingTask, receiveTask);

        // 取消另一个任务
        linkCts.Cancel();

        // 记录异常（如果有）
        try { await completed; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* 应用关闭 */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "飞书 Stream 主循环异常，触发重连");
            throw;
        }

        // 等待另一个任务也结束（忽略异常）
        try { await Task.WhenAll(pingTask, receiveTask); }
        catch { /* 忽略清理阶段的异常 */ }
    }

    // ===== 心跳循环：按 PingInterval 发送 Protobuf Ping 控制帧 =====
    private async Task PingLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pingInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (ws.State != WebSocketState.Open) return;

            try
            {
                var pingFrame = FeishuStreamFrame.CreatePingFrame(_serviceId);
                var bytes = pingFrame.ToByteArray();
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, ct);
                _logger.LogDebug("飞书 Stream Ping 已发送");
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning(ex, "飞书 Stream Ping 发送失败，触发重连");
                throw;
            }
        }
    }

    // ===== 接收循环：接收 Protobuf Frame → 分发处理 =====
    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var recvBuffer = new byte[8192];
        var ms = new MemoryStream();

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
                    _logger.LogWarning(ex, "飞书 Stream WebSocket 接收异常，触发重连");
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("飞书 Stream 服务端请求关闭 CloseStatus={CloseStatus} Desc={CloseStatusDescription}",
                        result.CloseStatus, result.CloseStatusDescription);
                    return;
                }
                if (result.Count > 0)
                    ms.Write(recvBuffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (ms.Length == 0) continue;

            // 解析 Protobuf Frame
            FeishuStreamFrame frame;
            try
            {
                var frameData = ms.GetBuffer().AsSpan(0, (int)ms.Length);
                frame = FeishuStreamFrame.Parse(frameData);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "飞书 Stream Frame 解析失败 length={Length}", ms.Length);
                continue;
            }

            try
            {
                await HandleFrameAsync(ws, frame, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "飞书 Stream 消息处理异常 SeqId={SeqId}", frame.SeqId);
            }
        }
    }

    // ===== 帧分发：控制帧 / 数据帧 =====
    private async Task HandleFrameAsync(ClientWebSocket ws, FeishuStreamFrame frame, CancellationToken ct)
    {
        var type = frame.GetHeader(FeishuStreamConstants.HeaderKeyType);

        switch (frame.Method)
        {
            case FeishuStreamConstants.FrameTypeControl:
                // 控制帧：Pong（心跳响应）或 Close
                if (string.Equals(type, FeishuStreamConstants.MessageTypePong, StringComparison.Ordinal))
                {
                    _logger.LogDebug("飞书 Stream 收到 Pong（心跳正常）");
                }
                else if (string.Equals(type, FeishuStreamConstants.MessageTypeClose, StringComparison.Ordinal))
                {
                    _logger.LogInformation("飞书 Stream 收到服务端 Close 通知，准备重连");
                    throw new InvalidOperationException("stream close notification from server");
                }
                else
                {
                    _logger.LogDebug("飞书 Stream 收到未知控制帧 type={Type}", type);
                }
                break;

            case FeishuStreamConstants.FrameTypeData:
                // 数据帧：事件推送
                await HandleDataFrameAsync(ws, frame, ct);
                break;

            default:
                _logger.LogWarning("飞书 Stream 收到未知帧类型 Method={Method} type={Type}", frame.Method, type);
                break;
        }
    }

    // ===== 数据帧处理：解析 payload → 处理成功后再回 ACK（失败不回 ACK，由服务端重推） =====
    private async Task HandleDataFrameAsync(ClientWebSocket ws, FeishuStreamFrame frame, CancellationToken ct)
    {
        // 解析 payload JSON
        if (frame.Payload == null || frame.Payload.Length == 0)
        {
            _logger.LogDebug("飞书 Stream 数据帧 payload 为空 SeqId={SeqId}", frame.SeqId);
            return;
        }

        string json;
        try
        {
            json = Encoding.UTF8.GetString(frame.Payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "飞书 Stream payload UTF-8 解码失败 SeqId={SeqId}", frame.SeqId);
            return;
        }

        // 幂等去重：提取 event_id；值记录首次出现时间戳
        var eventId = ExtractEventId(json);
        var nowTicks = DateTime.UtcNow.Ticks;
        if (!string.IsNullOrEmpty(eventId) && !_processedEventIds.TryAdd(eventId, nowTicks))
        {
            _logger.LogDebug("飞书 Stream 事件已处理过，跳过 EventId={EventId}", eventId);
            return;
        }

        // T27: 滑动窗口淘汰超过 TTL 的旧条目（替换原"满 1000 条整体 Clear"导致的去重失效）
        try
        {
            var cutoffTicks = DateTime.UtcNow.Subtract(DedupTtl).Ticks;
            foreach (var kv in _processedEventIds)
            {
                if (kv.Value < cutoffTicks)
                {
                    _processedEventIds.TryRemove(kv.Key, out _);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "飞书 Stream 去重缓存清理异常");
        }

        _logger.LogInformation("飞书 Stream 收到事件 EventId={EventId} PayloadLength={Length}", eventId, json.Length);

        // 交由 IngestionService 处理（复用现有 Webhook 事件处理逻辑）
        var processed = false;
        try
        {
            var result = await _ingestionService.ProcessEventAsync(json);
            if (!string.IsNullOrEmpty(result.ErrorMessage) && !result.IsEvent)
            {
                _logger.LogWarning("飞书 Stream 事件处理返回错误：{Error}", result.ErrorMessage);
            }
            else if (result.ConversationId > 0)
            {
                _logger.LogInformation("飞书 Stream 事件处理成功 ConversationId={ConversationId} NewConversation={New} Reply={Reply}",
                    result.ConversationId, result.NewConversationCount, result.ReplyCount);
            }
            processed = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "飞书 Stream 事件处理异常 EventId={EventId}", eventId);
        }
        finally
        {
            // T9: 逐事件清空变更跟踪器，防止长连接复用 scoped DbContext 时 ChangeTracker 无限累积
            try { _ingestionService.ResetTracking(); }
            catch (Exception resetEx) { _logger.LogDebug(resetEx, "重置飞书摄入跟踪器失败"); }
        }

        // T10: ACK 延后到处理成功之后——失败不回 ACK（飞书将重推该事件，实现 at-least-once），
        // 避免原"先 ACK 后处理"导致的业务失败事件永久丢失
        if (!processed)
        {
            // 失败时移除去重条目，允许重推事件被重新处理
            if (!string.IsNullOrEmpty(eventId))
            {
                _processedEventIds.TryRemove(eventId, out _);
            }
            return;
        }

        try
        {
            var ackFrame = FeishuStreamFrame.CreateAckFrame(frame.SeqId, frame.LogId, frame.Service, Array.Empty<byte>());
            var ackBytes = ackFrame.ToByteArray();
            await ws.SendAsync(new ArraySegment<byte>(ackBytes), WebSocketMessageType.Binary, true, ct);
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "飞书 Stream ACK 发送失败 SeqId={SeqId}", frame.SeqId);
        }
    }

    /// <summary>从事件 JSON 中提取 event_id 用于幂等去重</summary>
    private static string? ExtractEventId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            // v2.0 事件：header.event_id
            if (doc.RootElement.TryGetProperty("header", out var header)
                && header.TryGetProperty("event_id", out var eventId))
            {
                return eventId.GetString();
            }
            // v1.0 事件：uuid
            if (doc.RootElement.TryGetProperty("uuid", out var uuid))
            {
                return uuid.GetString();
            }
            return null;
        }
        catch { return null; }
    }

    // ===== DTO =====
    private sealed record StreamConnection(ClientWebSocket WebSocket, string Endpoint) : IDisposable
    {
        public void Dispose()
        {
            // 直接 Dispose WebSocket，底层会处理关闭握手，避免 sync-over-async 阻塞
            WebSocket.Dispose();
        }
    }

    // /callback/ws/endpoint 响应 DTO
    private sealed class WsEndpointResponse
    {
        public int Code { get; set; }
        public string? Msg { get; set; }
        public WsEndpointData? Data { get; set; }
    }

    private sealed class WsEndpointData
    {
        public string? URL { get; set; }
        public WsClientConfig? ClientConfig { get; set; }
    }

    private sealed class WsClientConfig
    {
        /// <summary>心跳间隔（秒）</summary>
        public int PingInterval { get; set; }
        /// <summary>重连次数（-1 = 无限）</summary>
        public int ReconnectCount { get; set; }
        /// <summary>重连间隔（秒）</summary>
        public int ReconnectInterval { get; set; }
        /// <summary>首次重连随机抖动（秒）</summary>
        public int ReconnectNonce { get; set; }
    }
}
