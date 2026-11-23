using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Infrastructure.Services.IMessage;

/// <summary>
/// bridge 传输：自建 macOS 上的 BlueBubbles REST 服务器（docs/integration/imessage.md §4）。
/// 鉴权：默认 `Authorization: Bearer <password>`（推荐）；AuthStyle=query 时才把口令放进 query，
/// 该方式会把密钥泄进访问日志，仅在旧部署只认 query 时启用 —— 因此日志里绝不打完整 URL。
/// 响应封装：{"status":int,"message":String,"data":any}，错误另带 error:{type,error}。
/// 路径随服务器版本漂移（REST 需 ≥0.2.0，webhook 需 ≥1.0.0）⇒ 端点集中为常量，权威清单是部署自身的 OpenAPI。
/// </summary>
public class IMessageClient : IIMessageClient
{
    // ===== 端点常量（勿在调用处拼字符串）=====
    internal const string TextPath = "/api/v1/message/text";
    internal const string AttachmentSendPath = "/api/v1/message/attachment";
    internal const string ChatNewPath = "/api/v1/chat/new";
    internal const string ChatQueryPath = "/api/v1/chat/query";
    internal const string ServerInfoPath = "/api/v1/server/info";
    internal const string WebhookRegisterPath = "/api/v1/server/webhook";
    internal const string PushListPath = "/api/v1/push";

    /// <summary>附件数据下载路径（版本相关：部分部署为 /api/v1/message/attachment/{guid}）。见交付说明的 OPEN ISSUES。</summary>
    internal const string AttachmentDataPathFormat = "/api/v1/attachment/{0}/data";

    private const string RateScope = "imessage:bridge";

    // ===== AMB（Apple Messages for Business）端点常量：docs/integration/imessage.md §3 =====
    internal const string AmbMessagePath = "/v1/message";

    /// <summary>
    /// 入站附件换取媒体地址/密钥的端点（§3）。post 下载前必须先用 attachment 的
    /// url/signature/owner 调它拿 download-url —— 旧实现完全没有这条路径，导致 AMB 附件全部丢失。
    /// </summary>
    internal const string AmbPreDownloadPath = "/v1/preDownload";

    /// <summary>
    /// 出站 body 的 locale。文档示例为 en_US，但只规定"必填"、未规定必须取哪个值（待证实），
    /// 本系统客户以中文为主，取 zh_CN；如需多语言应改为渠道配置项。
    /// </summary>
    internal const string AmbDefaultLocale = "zh_CN";

    /// <summary>capability-list 是必需头；Mac 客户端可能缺 FORM，故默认发保守集合。</summary>
    internal const string AmbDefaultCapabilityList = "QUICK,LIST,TIME,AUTH,AUTH2";

    /// <summary>Apple 只记录 msp-agent 的前 64 字符 ⇒ 填本系统名。</summary>
    internal const string AmbAgentName = "NoneadCSS";

    private const string AmbRateScope = "imessage:amb";

    private readonly HttpClient _httpClient;
    private readonly IIMessageSettingsProvider _settingsProvider;
    private readonly IMessageRateLimiter _rateLimiter;
    private readonly ILogger<IMessageClient> _logger;

    public IMessageClient(
        HttpClient httpClient,
        IOptions<HttpClientOptions> httpClientOptions,
        IIMessageSettingsProvider settingsProvider,
        IMessageRateLimiter rateLimiter,
        ILogger<IMessageClient> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(httpClientOptions.Value.StandardTimeoutSeconds);
        _settingsProvider = settingsProvider;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> SendTextMessageAsync(string destination, string body, string? idempotencyId = null)
    {
        // 保留旧的"失败即 null"契约给现有调用方（管理面回复 / IChatChannelMessageSender），
        // 内部统一走带状态码的新方法，避免两套发送逻辑漂移。
        var outcome = await SendTextMessageWithOutcomeAsync(destination, body, idempotencyId);
        return outcome.Success ? outcome.ExternalMessageId : null;
    }

    /// <inheritdoc />
    public async Task<IMessageSendOutcome> SendTextMessageWithOutcomeAsync(
        string destination, string body, string? idempotencyId = null)
    {
        var settings = await _settingsProvider.GetCurrentAsync();

        // 两种传输共用同一抽象：Mode 决定走 BlueBubbles 还是 Apple/MSP 网关
        if (settings.IsAmbMode)
            return await SendAmbTextWithOutcomeAsync(settings, destination, body, idempotencyId);

        if (!IsBridgeUsable(settings, nameof(SendTextMessageWithOutcomeAsync)))
            return IMessageSendOutcome.Terminal("iMessage bridge 未启用或缺少 BaseUrl/口令");

        if (!_rateLimiter.TryAcquire(RateScope, settings.RateLimitPerMinute))
        {
            // 限流是本地策略失败：重试不会变快，等队列退避反而会持续撞同一分钟的额度 ⇒ 按终端失败处理更诚实
            _logger.LogWarning("iMessage 出站限流：超过 {Limit} 条/分钟，已拒绝发送（大出站量有 Apple ID 锁定风险）",
                settings.RateLimitPerMinute);
            return IMessageSendOutcome.Terminal($"iMessage bridge 出站限流（{settings.RateLimitPerMinute}/分钟），本条未发送");
        }

        var tempGuid = idempotencyId ?? $"oc-{Guid.NewGuid():N}";

        var payload = new Dictionary<string, object?>
        {
            ["chatGuid"] = destination,
            ["message"] = body,
            ["tempGuid"] = tempGuid,
            ["method"] = ResolveSendMethod(settings),
        };

        try
        {
            using var request = BuildRequest(settings, TextPath, HttpMethod.Post, Json(payload));
            using var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var data = await ReadEnvelopeAsync(response, TextPath);
                // 200 但响应体无法解析：既不能当成功（不知道消息 id/是否真的入队），也不能断言失败；
                // 保守判为可重试，让队列按既有次数退避（旧实现这里返回 null，等价于"失败且重试"）。
                if (!data.HasValue)
                    return IMessageSendOutcome.Retryable("iMessage bridge 返回 200 但响应体无法解析", (int)response.StatusCode);

                return IMessageSendOutcome.Ok(ExtractMessageId(data.Value) ?? tempGuid);
            }

            var detail = await SafeReadBodyAsync(response);
            if (!string.IsNullOrWhiteSpace(detail) && detail.Length > 300) detail = detail.Substring(0, 300);

            return ClassifyHttpFailure((int)response.StatusCode, detail, "iMessage bridge");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("iMessage bridge 请求异常（不可达或超时）：{Type}", ex.GetType().Name);
            return IMessageSendOutcome.Retryable("iMessage bridge 请求异常（网络不可达或超时）", null);
        }
    }

    /// <inheritdoc />
    public async Task<string?> SendAttachmentAsync(string destination, byte[] data, string filename, string mimeType)
    {
        var settings = await _settingsProvider.GetCurrentAsync();
        if (!IsBridgeUsable(settings, nameof(SendAttachmentAsync))) return null;

        if (!_rateLimiter.TryAcquire(RateScope, settings.RateLimitPerMinute))
        {
            _logger.LogWarning("iMessage 出站限流：附件发送被拒绝（上限 {Limit} 条/分钟）", settings.RateLimitPerMinute);
            return null;
        }

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(destination), "chatGuid");
        form.Add(new StringContent(ResolveSendMethod(settings)), "method");
        var fileContent = new ByteArrayContent(data);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType);
        form.Add(fileContent, "attachment", filename);

        using var response = await PostRawAsync(settings, AttachmentSendPath, form);
        var envelope = await ReadEnvelopeAsync(response, AttachmentSendPath);
        return envelope.HasValue ? ExtractMessageId(envelope.Value) : null;
    }

    /// <inheritdoc />
    public async Task<IMessageAttachmentDownloadResult> DownloadAttachmentAsync(
        IMessageAttachmentRef attachment, string mode)
    {
        if (attachment is null)
            return IMessageAttachmentDownloadResult.Failed("附件元数据为空");

        var settings = await _settingsProvider.GetCurrentAsync();

        // 两种传输的附件协议完全不同：bridge 用 guid 直接拼 BlueBubbles REST 路径；
        // AMB 必须先 /v1/preDownload 换取媒体地址与解密密钥，再下载并按 AES/CTR 解密。
        // 旧实现在这里直接 `if (!IsBridgeUsable) return null`，导致 AMB 的入站附件被静默丢弃。
        if (settings.IsAmbMode || IMessageIdentity.IsAmbMode(mode))
            return await DownloadAmbAttachmentAsync(settings, attachment);

        return await DownloadBridgeAttachmentAsync(settings, attachment);
    }

    private async Task<IMessageAttachmentDownloadResult> DownloadBridgeAttachmentAsync(
        IMessageOptions settings, IMessageAttachmentRef attachment)
    {
        if (!IsBridgeUsable(settings, nameof(DownloadAttachmentAsync)))
            return IMessageAttachmentDownloadResult.Failed("iMessage bridge 未启用或缺少 BaseUrl/口令");

        var attachmentRef = string.IsNullOrWhiteSpace(attachment.Guid) ? attachment.DownloadUrl : attachment.Guid;
        if (string.IsNullOrWhiteSpace(attachmentRef))
            return IMessageAttachmentDownloadResult.Failed("bridge 附件缺少 guid/url，无法定位附件数据");

        var path = string.Format(AttachmentDataPathFormat, Uri.EscapeDataString(attachmentRef));
        try
        {
            using var request = BuildRequest(settings, path, HttpMethod.Get);
            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("iMessage 附件下载失败：HTTP {Status} {Path}", (int)response.StatusCode, path);
                return IMessageAttachmentDownloadResult.Failed($"bridge 附件下载失败：HTTP {(int)response.StatusCode}");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            var mime = response.Content.Headers.ContentType?.MediaType ?? attachment.MimeType;
            return IMessageAttachmentDownloadResult.Ok(bytes, mime, attachment.Filename);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("iMessage 附件下载异常：{Type}", ex.GetType().Name);
            return IMessageAttachmentDownloadResult.Failed("bridge 附件下载异常（网络不可达或超时）");
        }
    }

    /// <summary>
    /// AMB 入站附件：docs/integration/imessage.md §3 ——
    /// `GET /v1/preDownload`（头 authorization/source-id/url/signature/owner）→ `download-url` → GET →
    /// 若 `encrypted` 为 true 则按 AES/CTR/NoPadding + 全零 16 字节 IV 解密。
    /// 文档没有规定入站附件的 key 来源（key 由上传方生成），因此 preDownload 不返回 key 时只能报"协议输入缺失"，
    /// 绝不能把密文当明文存盘。
    /// </summary>
    private async Task<IMessageAttachmentDownloadResult> DownloadAmbAttachmentAsync(
        IMessageOptions settings, IMessageAttachmentRef attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.DownloadUrl))
            return IMessageAttachmentDownloadResult.Failed("AMB 附件缺少 mmcs-url/url，无法调用 /v1/preDownload");

        var issuerAudience = !string.IsNullOrWhiteSpace(settings.MspId) ? settings.MspId : settings.BusinessIdentifier;
        var secret = !string.IsNullOrWhiteSpace(settings.MspSharedSecret) ? settings.MspSharedSecret : settings.ApiKey;
        if (string.IsNullOrWhiteSpace(issuerAudience) || string.IsNullOrWhiteSpace(secret))
            return IMessageAttachmentDownloadResult.Failed(
                "AMB 配置不完整（MspId/BusinessIdentifier 或 MspSharedSecret/ApiKey 缺失），无法调用 preDownload");

        var baseUrl = !string.IsNullOrWhiteSpace(settings.ApiBaseUrl)
            ? settings.ApiBaseUrl!.TrimEnd('/')
            : settings.GatewayBaseUrl.TrimEnd('/');

        try
        {
            // 1) preDownload：用附件 url/signature/owner 换媒体下载地址（以及平台可能返回的解密密钥）
            using var preRequest = new HttpRequestMessage(HttpMethod.Get, baseUrl + AmbPreDownloadPath);
            preRequest.Headers.TryAddWithoutValidation("Authorization",
                "Bearer " + IMessageJwtSigner.MintToken(issuerAudience!, secret!, settings.TokenTtlSeconds));
            preRequest.Headers.TryAddWithoutValidation("source-id", settings.BusinessIdentifier ?? issuerAudience!);
            preRequest.Headers.TryAddWithoutValidation("url", attachment.DownloadUrl!);
            if (!string.IsNullOrWhiteSpace(attachment.Signature))
                preRequest.Headers.TryAddWithoutValidation("signature", attachment.Signature);
            if (!string.IsNullOrWhiteSpace(attachment.Owner))
                preRequest.Headers.TryAddWithoutValidation("owner", attachment.Owner);
            preRequest.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var preResponse = await _httpClient.SendAsync(preRequest);
            var preBody = await SafeReadBodyAsync(preResponse);
            if (!preResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("iMessage AMB preDownload 失败：HTTP {Status}", (int)preResponse.StatusCode);
                return IMessageAttachmentDownloadResult.Failed($"AMB preDownload 失败：HTTP {(int)preResponse.StatusCode}");
            }

            JsonDocument preDoc;
            try
            {
                preDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(preBody) ? "{}" : preBody);
            }
            catch (JsonException)
            {
                return IMessageAttachmentDownloadResult.Failed("AMB preDownload 响应不是合法 JSON");
            }

            using (preDoc)
            {
                var root = preDoc.RootElement;
                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                    root = data;

                var downloadUrl = ReadAmbString(root, "download-url", "downloadUrl");
                var keyMaterial = ReadAmbString(root, "key", "aes-key", "aesKey", "encryption-key", "encryptionKey",
                    "attachment-key", "attachmentKey");
                var ivMaterial = ReadAmbString(root, "iv", "aes-iv", "aesIv");
                var mime = ReadAmbString(root, "mime-type", "mimeType") ?? attachment.MimeType;
                var filename = ReadAmbString(root, "file-name", "fileName") ?? attachment.Filename;
                // 文档只对"加密附件"规定 AES/CTR；若平台显式声明明文则按明文处理（MIME 维度未在文档中区分）
                var isPlaintext = root.TryGetProperty("encrypted", out var encryptedProp)
                                  && encryptedProp.ValueKind == JsonValueKind.False;

                if (string.IsNullOrWhiteSpace(downloadUrl))
                    return IMessageAttachmentDownloadResult.Failed("AMB preDownload 响应缺少 download-url");

                // 2) 下载媒体字节（TLS SNI 由返回的下载 URL 主机名决定，继续用同一个 HttpClient 即可）
                var mediaUri = Uri.TryCreate(downloadUrl, UriKind.Absolute, out var absolute)
                    ? absolute
                    : new Uri(baseUrl + "/" + downloadUrl!.TrimStart('/'));

                using var mediaResponse = await _httpClient.GetAsync(mediaUri);
                if (!mediaResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("iMessage AMB 附件媒体下载失败：HTTP {Status}", (int)mediaResponse.StatusCode);
                    return IMessageAttachmentDownloadResult.Failed(
                        $"AMB 附件媒体下载失败：HTTP {(int)mediaResponse.StatusCode}");
                }

                var cipherBytes = await mediaResponse.Content.ReadAsByteArrayAsync();
                mime ??= mediaResponse.Content.Headers.ContentType?.MediaType;

                if (isPlaintext)
                    return IMessageAttachmentDownloadResult.Ok(cipherBytes, mime, filename);

                if (string.IsNullOrWhiteSpace(keyMaterial))
                    return IMessageAttachmentDownloadResult.Unsupported(
                        "preDownload 未返回附件解密 key：docs/integration/imessage.md §3 只规定 AES/CTR/NoPadding 与全零 IV，" +
                        "未规定入站附件密钥的来源，因此无法用文档给出的输入解密（需要平台补充 key/iv 字段）",
                        mime, filename);

                if (!TryDecryptAmbAttachment(cipherBytes, keyMaterial, ivMaterial, out var plainBytes, out var decryptError))
                    return IMessageAttachmentDownloadResult.Failed(decryptError!, mime, filename);

                return IMessageAttachmentDownloadResult.Ok(plainBytes, mime, filename);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("iMessage AMB 附件下载异常：{Type}", ex.GetType().Name);
            return IMessageAttachmentDownloadResult.Failed($"AMB 附件下载异常（{ex.GetType().Name}）");
        }
    }

    private static string? ReadAmbString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value)) continue;
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetString();
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return value.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// 按 docs/integration/imessage.md §3 解密附件：AES/CTR/NoPadding，计数器 16 字节大端递增，全零 IV 为默认。
    /// .NET 的 Aes 没有 CTR 模式 ⇒ 用 ECB 加密计数器块得到密钥流再 XOR（不引入第三方包）。
    /// </summary>
    internal static bool TryDecryptAmbAttachment(
        byte[] ciphertext, string keyMaterial, string? ivMaterial, out byte[] plaintext, out string? error)
    {
        plaintext = [];
        error = null;

        var key = ParseAmbKeyMaterial(keyMaterial);
        if (key is null)
        {
            error = "附件解密 key 格式无法识别（支持 hex/base64，长度须为 16/24/32 字节）";
            return false;
        }

        var counter = new byte[16];
        if (!string.IsNullOrWhiteSpace(ivMaterial))
        {
            var iv = ParseAmbKeyMaterial(ivMaterial);
            if (iv is null || iv.Length != 16)
            {
                error = "附件 IV 格式无法识别（须为 16 字节的 hex/base64）";
                return false;
            }

            Array.Copy(iv, counter, 16);
        }
        // iv 缺省即文档规定的全零 16 字节 IV

        plaintext = AesCtrTransform(ciphertext, key, counter);
        return true;
    }

    /// <summary>解析平台下发的附件密钥材料：文档规定 key 以 hex 存储并带 "00" 前缀，也兼容 base64 表示。</summary>
    internal static byte[]? ParseAmbKeyMaterial(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var value = raw.Trim();
        if (value.Length % 2 == 0 && value.Length >= 32 && value.All(Uri.IsHexDigit))
        {
            var bytes = Convert.FromHexString(value);
            // 文档的 "00" 前缀是存储格式的一部分，不是密钥本身 ⇒ 33 字节且首字节为 0 时剥掉
            if (bytes.Length == 33 && bytes[0] == 0) bytes = bytes[1..];
            if (bytes.Length is 16 or 24 or 32) return bytes;
        }

        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length == 33 && bytes[0] == 0) bytes = bytes[1..];
            if (bytes.Length is 16 or 24 or 32) return bytes;
        }
        catch (FormatException)
        {
            // 既不是 hex 也不是 base64 ⇒ 交给调用方报"格式无法识别"
        }

        return null;
    }

    private static byte[] AesCtrTransform(byte[] input, byte[] key, byte[] counter)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;

        using var encryptor = aes.CreateEncryptor();
        var output = new byte[input.Length];
        var keystream = new byte[16];

        for (var offset = 0; offset < input.Length; offset += 16)
        {
            encryptor.TransformBlock(counter, 0, 16, keystream, 0);
            var count = Math.Min(16, input.Length - offset);
            for (var i = 0; i < count; i++)
                output[offset + i] = (byte)(input[offset + i] ^ keystream[i]);

            // 计数器按大端 +1（CTR 语义）
            for (var i = 15; i >= 0; i--)
            {
                if (++counter[i] != 0) break;
            }
        }

        return output;
    }

    /// <inheritdoc />
    public async Task<bool> MarkAsReadAsync(string chatGuidOrSourceId)
    {
        var settings = await _settingsProvider.GetCurrentAsync();
        if (!IsBridgeUsable(settings, nameof(MarkAsReadAsync))) return false;

        // 已读回执属于 Private API（需关闭 SIP）。apple-script 模式下不发这个请求，避免制造无谓的失败日志。
        if (!string.Equals(settings.SendMethod, IMessageSendMethods.PrivateApi, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(chatGuidOrSourceId)) return false;

        var payload = new Dictionary<string, object?>
        {
            ["chatGuid"] = chatGuidOrSourceId,
            ["method"] = IMessageSendMethods.PrivateApi,
        };

        using var response = await PostJsonAsync(settings, $"/api/v1/chat/{Uri.EscapeDataString(chatGuidOrSourceId)}/read", payload);
        return response.IsSuccessStatusCode;
    }

    /// <inheritdoc />
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsProvider.GetCurrentAsync();
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.Password)) return false;

        var path = string.IsNullOrWhiteSpace(settings.HealthEndpoint) ? "/api/v1/ping" : settings.HealthEndpoint;
        try
        {
            using var request = BuildRequest(settings, path, HttpMethod.Get);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return false;

            var envelope = await ReadEnvelopeAsync(response, path);
            return envelope.HasValue;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning("iMessage 健康检查失败：{Type}", ex.GetType().Name);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RegisterWebhookAsync(string publicWebhookUrl, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsProvider.GetCurrentAsync();
        if (!IsBridgeUsable(settings, nameof(RegisterWebhookAsync))) return false;
        if (string.IsNullOrWhiteSpace(publicWebhookUrl)) return false;

        // BlueBubbles 的 webhook 注册需要把口令随 body 提交（等价 POST /api/v1/push）
        var payload = new Dictionary<string, object?>
        {
            ["url"] = publicWebhookUrl,
            ["password"] = settings.Password,
        };

        try
        {
            using var request = BuildRequest(settings, WebhookRegisterPath, HttpMethod.Post, Json(payload));
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var ok = response.IsSuccessStatusCode;
            if (!ok)
                _logger.LogWarning("iMessage webhook 注册失败：HTTP {Status}", (int)response.StatusCode);
            // 日志不打印 URL 内容之外的敏感信息；webhook url 本身不含密钥
            return ok;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("iMessage webhook 注册异常：{Type}", ex.GetType().Name);
            return false;
        }
    }

    /// <summary>读取服务器版本（GET /api/v1/server/info），用于与 MinServerVersion 比对。</summary>
    public async Task<string?> GetServerVersionAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsProvider.GetCurrentAsync();
        if (!IsBridgeUsable(settings, nameof(GetServerVersionAsync))) return null;

        try
        {
            using var request = BuildRequest(settings, ServerInfoPath, HttpMethod.Get);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var envelope = await ReadEnvelopeAsync(response, ServerInfoPath);
            if (!envelope.HasValue) return null;

            var data = envelope.Value;
            if (data.ValueKind != JsonValueKind.Object) return null;
            foreach (var key in new[] { "version", "server_version", "build" })
            {
                if (data.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
            }

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>列出服务器上已登记的推送地址（GET /api/v1/push）。</summary>
    public async Task<List<string>> ListRegisteredWebhooksAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        var settings = await _settingsProvider.GetCurrentAsync();
        if (!IsBridgeUsable(settings, nameof(ListRegisteredWebhooksAsync))) return result;

        try
        {
            using var request = BuildRequest(settings, PushListPath, HttpMethod.Get);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var envelope = await ReadEnvelopeAsync(response, PushListPath);
            if (envelope?.ValueKind != JsonValueKind.Array) return result;

            foreach (var item in envelope.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
                {
                    var value = url.GetString();
                    // 旧部署可能把 guid 拼进 url ⇒ 只回报不含口令的部分
                    if (!string.IsNullOrWhiteSpace(value) && !LooksLikeCredentialBearingUrl(value!))
                        result.Add(value!);
                }
                else if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value) && !LooksLikeCredentialBearingUrl(value!))
                        result.Add(value!);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("iMessage 读取 webhook 列表失败：{Type}", ex.GetType().Name);
        }

        return result;
    }

    /// <summary>新建会话（POST /api/v1/chat/new），返回 chatGuid。仅在主动发起场景使用；AMB 侧绝不允许冷启动私聊。</summary>
    public async Task<string?> CreateChatAsync(string handle)
    {
        var settings = await _settingsProvider.GetCurrentAsync();
        if (!IsBridgeUsable(settings, nameof(CreateChatAsync))) return null;

        var payload = new Dictionary<string, object?>
        {
            ["handles"] = new[] { handle },
        };

        using var response = await PostJsonAsync(settings, ChatNewPath, payload);
        var envelope = await ReadEnvelopeAsync(response, ChatNewPath);
        if (envelope?.ValueKind != JsonValueKind.Object) return null;
        return envelope.Value.TryGetProperty("guid", out var guid) && guid.ValueKind == JsonValueKind.String
            ? guid.GetString()
            : null;
    }

    // ===== 请求构造 =====

    private bool IsBridgeUsable(IMessageOptions settings, string operation)
    {
        if (!string.Equals(settings.Mode, IMessageModes.Bridge, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("iMessage 当前模式为 {Mode}，{Operation} 不适用于 bridge 传输", settings.Mode, operation);
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.Password))
        {
            _logger.LogWarning("iMessage bridge 未配置（缺少 BaseUrl 或口令），{Operation} 已跳过", operation);
            return false;
        }

        return true;
    }

    private string ResolveSendMethod(IMessageOptions settings)
    {
        var method = (settings.SendMethod ?? IMessageSendMethods.AppleScript).Trim();
        return method.ToLowerInvariant() switch
        {
            IMessageSendMethods.PrivateApi => IMessageSendMethods.PrivateApi,
            IMessageSendMethods.Any => IMessageSendMethods.Any,
            _ => IMessageSendMethods.AppleScript,
        };
    }

    private async Task<HttpResponseMessage> PostJsonAsync(IMessageOptions settings, string path, object payload)
    {
        using var request = BuildRequest(settings, path, HttpMethod.Post, Json(payload));
        try
        {
            return await _httpClient.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("iMessage 请求异常：{Path} {Type}", path, ex.GetType().Name);
            return new HttpResponseMessage(System.Net.HttpStatusCode.RequestTimeout);
        }
    }

    private async Task<HttpResponseMessage> PostRawAsync(IMessageOptions settings, string path, HttpContent content)
    {
        using var request = BuildRequest(settings, path, HttpMethod.Post, content);
        try
        {
            return await _httpClient.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("iMessage 请求异常：{Path} {Type}", path, ex.GetType().Name);
            return new HttpResponseMessage(System.Net.HttpStatusCode.RequestTimeout);
        }
    }

    private static StringContent Json(object payload)
        => new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    /// <summary>
    /// 构造请求：base + path。口令默认走 Authorization 头；仅当 AuthStyle=query 时才拼 query 参数
    /// （此时日志只打 path，不打最终 URL）。
    /// </summary>
    private HttpRequestMessage BuildRequest(IMessageOptions settings, string path, HttpMethod method, HttpContent? content = null)
    {
        var baseUrl = settings.BaseUrl!.TrimEnd('/');
        var queryStyle = string.Equals(settings.AuthStyle, IMessageAuthStyles.Query, StringComparison.OrdinalIgnoreCase);

        var url = $"{baseUrl}{path}";
        if (queryStyle)
        {
            // 文档口径：guid（别名 password/token）。该方式会把口令写进访问日志 ⇒ 默认关闭。
            var sep = url.Contains('?') ? "&" : "?";
            url = $"{url}{sep}guid={Uri.EscapeDataString(settings.Password!)}";
        }

        var request = new HttpRequestMessage(method, url);
        if (!queryStyle)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Password);

        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        if (content != null)
            request.Content = content;

        return request;
    }

    /// <summary>
    /// AMB 出站（Apple Messages for Business）：POST {ApiBaseUrl|GatewayBaseUrl}/v1/message。
    /// 口径见 docs/integration/imessage.md §3：必需头 destination-id / source-id / id / capability-list +
    /// Authorization（HS256 JWT 按小时重签，绝不跨小时缓存）；成功 200 通常空 body。
    /// 请求体为**扁平**信封 `{"type":"text","v":1,"id":uuid,"sourceId":…,"destinationId":…,"locale":…,"body":"文本"}`：
    /// 旧实现发的是 `{"type":"TEXT","text":…}` —— type 大写且缺 v/id/sourceId/destinationId/locale，
    /// 平台按"headers/JSON 不合规"直接 400，所以 AMB 出站从未成功过。
    /// 状态码语义：400/403 参数或凭据错误；404 destination-id 未知或用户暂时不可达（Apple 不会稍后重投）；
    /// 410 Gone 用户已关闭会话，绝不重试；只有 5xx/429/超时值得让上层队列重试。
    /// ⚠ 该路径需要 Apple 批准的 MSP 资格或其连接器，仓库内无凭据无法实测；上线前须与连接器厂商逐字段核对（见 OPEN ISSUES）。
    /// </summary>
    private async Task<IMessageSendOutcome> SendAmbTextWithOutcomeAsync(
        IMessageOptions settings, string destination, string body, string? idempotencyId)
    {
        var issuerAudience = !string.IsNullOrWhiteSpace(settings.MspId) ? settings.MspId : settings.BusinessIdentifier;
        var secret = !string.IsNullOrWhiteSpace(settings.MspSharedSecret) ? settings.MspSharedSecret : settings.ApiKey;
        if (string.IsNullOrWhiteSpace(issuerAudience) || string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning("iMessage AMB 配置不完整（缺 MspId/BusinessIdentifier 或 MspSharedSecret/ApiKey），拒绝发送");
            return IMessageSendOutcome.Terminal("iMessage AMB 配置不完整（缺 MspId/BusinessIdentifier 或 MspSharedSecret/ApiKey）");
        }

        if (!_rateLimiter.TryAcquire(AmbRateScope, settings.RateLimitPerMinute))
        {
            _logger.LogWarning("iMessage AMB 出站限流：超过 {Limit} 条/分钟，已拒绝发送", settings.RateLimitPerMinute);
            return IMessageSendOutcome.Terminal($"iMessage AMB 出站限流（{settings.RateLimitPerMinute}/分钟），本条未发送");
        }

        var baseUrl = !string.IsNullOrWhiteSpace(settings.ApiBaseUrl)
            ? settings.ApiBaseUrl!.TrimEnd('/')
            : settings.GatewayBaseUrl.TrimEnd('/');

        // 文档要求 id 是 UUID，且 AMB 把 id 当**客户端消息标识**：同一条消息的重试必须复用同一个 id，
        // 否则网关视作两条不同消息，客户会收到重复投递（超时/5xx 重试场景）。
        // 因此：调用方给了幂等键就收束成稳定 UUID（队列行派生的 id 每次重试都相同）；
        // 只有交互式发送（无队列行、调用方不传 id）才新生成 —— 那本来就是一条新消息。
        var messageId = IMessageIdempotency.NormalizeAmbMessageId(idempotencyId);
        var sourceId = settings.BusinessIdentifier ?? issuerAudience!;
        var payload = BuildAmbTextEnvelope(sourceId, destination, body, messageId);

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + AmbMessagePath);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + IMessageJwtSigner.MintToken(issuerAudience!, secret!, settings.TokenTtlSeconds));
        request.Headers.TryAddWithoutValidation("destination-id", destination);
        request.Headers.TryAddWithoutValidation("source-id", sourceId);
        request.Headers.TryAddWithoutValidation("id", messageId);
        request.Headers.TryAddWithoutValidation("capability-list", AmbDefaultCapabilityList);
        request.Headers.TryAddWithoutValidation("msp-agent", AmbAgentName);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            // 200 通常空 body：只要状态码成功即视为已受理（消息 id 由本地生成的 id 兜底）
            if (response.IsSuccessStatusCode) return IMessageSendOutcome.Ok(messageId);

            var detail = await SafeReadBodyAsync(response);
            if (!string.IsNullOrWhiteSpace(detail) && detail.Length > 300) detail = detail.Substring(0, 300);
            var providerCode = TryReadAmbErrorCode(detail);

            _logger.LogWarning("iMessage AMB 发送失败：HTTP {Status} Code={Code} Detail={Detail}",
                (int)response.StatusCode, providerCode, detail);

            return ClassifyHttpFailure((int)response.StatusCode, detail, "iMessage AMB", providerCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "iMessage AMB 请求异常（网关不可达或超时）");
            return IMessageSendOutcome.Retryable("iMessage AMB 请求异常（网关不可达或超时）", null);
        }
    }

    /// <summary>
    /// 构造文档 §3 规定的 AMB 文本信封。sourceId/destinationId 必须与请求头一致，
    /// 否则平台会按"destination-id inconsistent"返回 400。
    /// </summary>
    internal static Dictionary<string, object?> BuildAmbTextEnvelope(
        string sourceId, string destinationId, string body, string messageId)
    {
        return new Dictionary<string, object?>
        {
            // type 必须是**小写** text（文档枚举：text|interactive|typing_start|typing_end|close）
            ["type"] = "text",
            ["v"] = 1,
            ["id"] = messageId,
            ["sourceId"] = sourceId,
            ["destinationId"] = destinationId,
            ["locale"] = AmbDefaultLocale,
            // type=text 时 body 是文本**字符串**，不是 {type,text} 对象
            ["body"] = body,
        };
    }

    /// <summary>尝试从 AMB 错误响应体里取出平台错误码（code/errorCode/error_code/error.code）。</summary>
    internal static string? TryReadAmbErrorCode(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return null;

        try
        {
            using var doc = JsonDocument.Parse(detail);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            foreach (var key in new[] { "code", "errorCode", "error_code", "error" })
            {
                if (!root.TryGetProperty(key, out var value)) continue;
                switch (value.ValueKind)
                {
                    case JsonValueKind.String:
                        return value.GetString();
                    case JsonValueKind.Number:
                        return value.ToString();
                    case JsonValueKind.Object when value.TryGetProperty("code", out var inner):
                        return inner.ValueKind == JsonValueKind.String ? inner.GetString() : inner.ToString();
                }
            }
        }
        catch (JsonException)
        {
            // 非 JSON 错误体（例如网关返回 HTML）：没有错误码可取
        }

        return null;
    }

    /// <summary>
    /// 把 HTTP 失败映射成"可重试 / 永久失败"。
    /// 只有 5xx / 429 / 408 可重试；400/403/404/410 等一律永久失败 —— 旧实现把它们全折叠成 null，
    /// 队列因此会把 410（用户已关闭会话）重试到 MaxAttempts。
    /// </summary>
    internal static IMessageSendOutcome ClassifyHttpFailure(
        int status, string? detail, string source, string? providerErrorCode = null)
    {
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : "：" + detail;

        if (status is 408 or 429 || status >= 500)
            return IMessageSendOutcome.Retryable($"{source} 发送失败：HTTP {status}{suffix}", status, providerErrorCode);

        var reason = status switch
        {
            400 => "请求参数或请求头不合规（400）",
            401 => "鉴权失败（401）",
            403 => "凭据或 MSP 校验失败（403）",
            404 => "目标不可达（404：destination-id 未知或用户暂时不可达，平台不会重投）",
            410 => "用户已关闭会话（410 Gone），永久失败不重试",
            _ => $"不可重试的失败（HTTP {status}）",
        };

        return IMessageSendOutcome.Terminal($"{source} 发送失败：{reason}{suffix}", status, providerErrorCode);
    }


/// <summary>解析 BlueBubbles 的 {status,message,data} 封装；失败返回 null 并记一次可诊断日志（不含口令）。</summary>
    private async Task<JsonElement?> ReadEnvelopeAsync(HttpResponseMessage response, string path)
    {
        var text = await SafeReadBodyAsync(response);
        if (string.IsNullOrWhiteSpace(text))
        {
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("iMessage 请求失败：HTTP {Status} {Path}", (int)response.StatusCode, path);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                var status = root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Number
                    ? s.GetInt32()
                    : response.IsSuccessStatusCode ? 200 : (int)response.StatusCode;

                if (status >= 400 || root.TryGetProperty("error", out _))
                {
                    var errorText = root.TryGetProperty("error", out var e)
                        ? (e.ValueKind == JsonValueKind.Object
                            ? (e.TryGetProperty("error", out var ee) ? ee.GetString() : e.GetRawText())
                            : e.GetString())
                        : root.TryGetProperty("message", out var m) ? m.GetString() : null;

                    _logger.LogWarning("iMessage 返回错误：HTTP {Status} {Path}：{Error}", (int)response.StatusCode, path, errorText);
                    return null;
                }

                if (root.TryGetProperty("data", out var data))
                    return data.Clone();

                return root.Clone();
            }

            return root.Clone();
        }
        catch (JsonException)
        {
            _logger.LogWarning("iMessage 响应不是 JSON：HTTP {Status} {Path}", (int)response.StatusCode, path);
            return null;
        }
    }

    private static async Task<string?> SafeReadBodyAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>从 data 中取消息标识：BlueBubbles 发送返回 rowid / GUID / 或对象里的 guid。</summary>
    private static string? ExtractMessageId(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.String) return data.GetString();
        if (data.ValueKind != JsonValueKind.Object) return null;

        foreach (var key in new[] { "guid", "tempGuid", "messageGuid" })
        {
            if (data.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        }

        if (data.TryGetProperty("rowid", out var rowid))
        {
            if (rowid.ValueKind == JsonValueKind.Number) return "rowid:" + rowid.GetInt64();
            if (rowid.ValueKind == JsonValueKind.String) return "rowid:" + rowid.GetString();
        }

        return null;
    }

    /// <summary>识别 url 里是否嵌了口令（避免把可疑地址显示到界面上）。</summary>
    private static bool LooksLikeCredentialBearingUrl(string url)
    {
        return url.Contains("guid=", StringComparison.OrdinalIgnoreCase)
               || url.Contains("password=", StringComparison.OrdinalIgnoreCase)
               || url.Contains("token=", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// AMB 出站消息 id 的确定性派生。
/// <para>
/// AMB 把信封与 <c>id</c> 头里的 id 当作**客户端消息标识**：网关按它去重；同一条排队消息在
/// 超时 / 5xx 重试时若换了 id，网关会当成新消息再投递一次（客户收到两份）。
/// 因此队列消息的 id 必须是"出站队列行"的纯函数：同一 (队列行 Id, 会话 Id) 恒等，不同队列行互异。
/// 队列每轮重试都复用同一行（只累加 AttemptCount），所以该函数在重试之间稳定。
/// </para>
/// </summary>
public static class IMessageIdempotency
{
    /// <summary>
    /// 按出站队列行派生稳定的 AMB 消息 id（UUID 的 "D" 形式）。
    /// 维度取 (队列行 Id, 会话 Id)：队列行 Id 已能唯一定位一条出站消息，再带会话 Id 是为了在
    /// 行号复用/数据迁移时避免与旧消息撞 id（撞了会被 AMB 当成重复投递直接丢弃）。
    /// </summary>
    public static string DeriveAmbMessageId(int queueItemId, int conversationId)
        => DeriveAmbMessageIdCore($"im-{queueItemId}-{conversationId}");

    /// <summary>
    /// 把调用方提供的幂等键收束成 AMB 要求的 UUID：
    /// <list type="bullet">
    /// <item>空 ⇒ 新生成（仅交互式发送走这里：交互发送本来就是一条新消息，不能复用旧 id）；</item>
    /// <item>已是 UUID ⇒ 规范化为 "D" 形式（保留调用方指定的值）；</item>
    /// <item>其它任意稳定键 ⇒ 哈希成 UUID（调用方不必自己造 UUID，只需保证键跨重试稳定）。</item>
    /// </list>
    /// </summary>
    public static string NormalizeAmbMessageId(string? idempotencyId)
    {
        if (string.IsNullOrWhiteSpace(idempotencyId))
            return Guid.NewGuid().ToString();

        return Guid.TryParse(idempotencyId, out var parsed)
            ? parsed.ToString()
            : DeriveAmbMessageIdCore(idempotencyId);
    }

    private static string DeriveAmbMessageIdCore(string key)
    {
        // 取 SHA-256 前 16 字节构造 Guid：只要求"稳定且互异"，不要求 RFC 4122 的 version/variant 位
        // （网关不校验这两位）；确定性来自哈希输入，与进程/时钟/随机源无关。
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }
}
