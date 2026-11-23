using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// iMessage / Apple Messages 渠道端点（镜像 WhatsAppController）。
/// 两套入站面：
///   bridge  → POST /api/imessage/bluebubbles/webhook（BlueBubbles 事件：new-message / updated-message / message-error / …）
///   amb_*   → POST /api/imessage/amb/message（Apple Messages for Business：body 可能是 gzip，身份在 header）
/// 管理面：settings / status / reply / test / webhook-register。
/// </summary>
[ApiController]
[Route("api/imessage")]
[Authorize]
public class IMessageController : ControllerBase
{
    private readonly IIMessageService _imessageService;
    private readonly IIMessageIngestionService _ingestionService;
    private readonly IIMessageSettingsProvider _settingsProvider;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ILogger<IMessageController> _logger;

    /// <summary>
    /// bridge 入站"未配置共享密钥"告警是否已打过（进程级一次性）。
    /// 该告警只为提醒管理员补配置，不必每个请求都刷日志。
    /// </summary>
    private static int _bridgeIngressAnonymousWarned;

    public IMessageController(
        IIMessageService imessageService,
        IIMessageIngestionService ingestionService,
        IIMessageSettingsProvider settingsProvider,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<IMessageController> logger)
    {
        _imessageService = imessageService;
        _ingestionService = ingestionService;
        _settingsProvider = settingsProvider;
        _currentUserAccessor = currentUserAccessor;
        _logger = logger;
    }

    // ============================================================
    // bridge（BlueBubbles）入站
    // ============================================================

    /// <summary>
    /// BlueBubbles webhook 接收端点（对应 IMessageOptions.WebhookPath）。
    /// 平台事件必须**快速返回 2xx**：BlueBubbles 不重试，非 2xx 会静默丢事件。
    /// 注意：BlueBubbles 的 webhook 载荷不带签名 ⇒ 端点路径是固定常量、仅靠"路径不可猜"不足以挡伪造，
    /// 配置了 BridgeWebhookSecret 时强制校验共享密钥（X-Webhook-Secret 头，或兼容 ?secret= 查询参数）；
    /// 未配置时保持旧的放行行为，但只告警一次（避免升级瞬间打死正在运行的部署）。
    /// </summary>
    [HttpPost("bluebubbles/webhook")]
    [AllowAnonymous]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> ReceiveBlueBubblesWebhook()
    {
        var body = await ReadBodyAsync();
        if (string.IsNullOrWhiteSpace(body)) return Ok();

        var settings = await _settingsProvider.GetCurrentAsync();
        if (settings.IsAmbMode)
        {
            // 模式与端点不符：不处理，也不泄露配置细节
            _logger.LogWarning("iMessage：收到 bridge webhook 但当前 Mode={Mode}，已忽略", settings.Mode);
            return Ok();
        }

        // 共享密钥校验：伪造入站会直接落库成客户/会话，必须在解析前拦住。
        if (!VerifyBridgeWebhookSecret(settings))
        {
            _logger.LogWarning(
                "iMessage：bridge webhook 共享密钥校验失败，已拒绝（RemoteIp={RemoteIp}）",
                HttpContext.Connection.RemoteIpAddress);
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "invalid webhook secret" });
        }

        try
        {
            var result = await _ingestionService.ProcessWebhookPayloadAsync(body, IMessageModes.Bridge);
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                _logger.LogWarning("iMessage webhook 处理告警：{Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            // 仍然回 200：入站幂等由消息 guid 保证，重试交给桥接侧的手动重放
            _logger.LogError(ex, "iMessage webhook 处理异常");
        }

        return Ok();
    }

    /// <summary>webhook 路径的存活探测（便于在桥接服务器上确认地址可达）。</summary>
    [HttpGet("bluebubbles/webhook")]
    [AllowAnonymous]
    public IActionResult PingBlueBubblesWebhook() => Ok(new { channel = "imessage", transport = "bridge" });

    // ============================================================
    // amb_*（Apple Messages for Business）入站
    // ============================================================

    /// <summary>
    /// Apple/MSP 入站端点。规则（docs/integration/imessage.md §3）：
    /// • body 可能是 gzip ⇒ 必须先解压再解析；
    /// • 身份在 header（Source-Id / Destination-Id / Id），必须与 body 一致，否则 400；
    /// • 缺 Authorization ⇒ 401；JWT/MSP id 无效 ⇒ 403；
    /// • Apple **只对 5xx 重试**（4 次、退避从 100ms 起）⇒ 业务错误必须回 4xx，别让 Apple 重投。
    /// </summary>
    [HttpPost("amb/message")]
    [AllowAnonymous]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ReceiveAmbMessage(
        [FromHeader(Name = "Source-Id")] string? sourceId,
        [FromHeader(Name = "Destination-Id")] string? destinationId,
        [FromHeader(Name = "Id")] string? messageId,
        [FromHeader(Name = "Authorization")] string? authorization)
    {
        var settings = await _settingsProvider.GetCurrentAsync();
        if (!settings.IsAmbMode)
        {
            _logger.LogWarning("iMessage：收到 AMB 入站但当前 Mode={Mode}，已拒绝", settings.Mode);
            return BadRequest(new { error = "channel mode mismatch" });
        }

        // 体量硬上限（防放大攻击；Apple 的附件走单独的 preDownload 流程，不塞进 body）
        var rawBytes = await ReadRawBytesAsync();
        if (rawBytes.Length == 0) return Ok();
        if (settings.MaxBodyBytes > 0 && rawBytes.Length > settings.MaxBodyBytes)
        {
            _logger.LogWarning("iMessage AMB 入站超限：{Bytes} bytes > {Limit}", rawBytes.Length, settings.MaxBodyBytes);
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        // 鉴权：amb_apple_direct 验 Apple 的 HS256 JWT；amb_msp 验 MSP 的 HMAC 头
        if (settings.Mode == IMessageModes.AmbAppleDirect)
        {
            if (string.IsNullOrWhiteSpace(authorization)) return Unauthorized(new { error = "missing authorization" });
            var valid = IMessageJwtSigner.TryValidate(
                authorization,
                settings.MspSharedSecret ?? string.Empty,
                settings.MspId ?? settings.BusinessId ?? string.Empty,
                out var jwtError);
            if (!valid)
            {
                _logger.LogWarning("iMessage AMB 入站 JWT 校验失败：{Error}", jwtError);
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "invalid token" });
            }
        }
        else
        {
            // 安全底线：AMB(MSP) 入站必须验签。旧代码在 WebhookSecret 为空时整段跳过 else-if，
            // 于是任意伪造 POST（自带 Source-Id）都能在本租户下建客户/会话。
            // 这里改为"配置缺失即拒绝"：无法验签就无法证明请求来自 MSP，绝不能把配置疏漏当成放行理由。
            if (string.IsNullOrWhiteSpace(settings.WebhookSecret))
            {
                _logger.LogWarning("iMessage AMB(MSP) 入站被拒：WebhookSecret 未配置，无法验签；请在渠道设置页补齐后重试");
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "webhook secret not configured" });
            }

            var signature = Request.Headers["X-MSP-Signature"].FirstOrDefault()
                            ?? Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
            if (!IMessageWebhookParser.VerifyMspSignature(rawBytes, settings.WebhookSecret, signature))
            {
                _logger.LogWarning("iMessage AMB(MSP) 入站签名校验失败");
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "invalid signature" });
            }
        }

        string body;
        try
        {
            body = DecompressIfNeeded(rawBytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "iMessage AMB 入站 gzip 解压失败");
            return BadRequest(new { error = "bad gzip body" });
        }

        try
        {
            var result = await _ingestionService.ProcessAmbInboundAsync(body, sourceId, destinationId, messageId);
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                // 报文本身不合法（缺头 / body 与 header 不一致）⇒ 400，Apple 不会重投 4xx
                _logger.LogWarning("iMessage AMB 入站被拒：{Error}", result.ErrorMessage);
                return BadRequest(new { error = result.ErrorMessage });
            }
        }
        catch (Exception ex)
        {
            // 内部故障回 5xx：这正是 Apple 会重试的区间，配合 guid/Id 幂等去重才安全
            _logger.LogError(ex, "iMessage AMB 入站处理异常");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok();
    }

    // ============================================================
    // 管理面
    // ============================================================

    /// <summary>获取 iMessage 渠道配置（密钥字段脱敏）。</summary>
    [HttpGet("settings")]
    [RequireModule("imessage")]
    [Permission("imessage.view")]
    public async Task<IActionResult> GetSettings() => Ok(await _imessageService.GetSettingsAsync());

    /// <summary>更新 iMessage 渠道配置（仅超级管理员）。</summary>
    [HttpPut("settings")]
    [RequireModule("imessage")]
    [RequireSuperAdmin]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateIMessageSettingsRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        return Ok(await _imessageService.UpdateSettingsAsync(request));
    }

    /// <summary>获取连接状态（含服务器版本与已登记 webhook，仅 bridge 模式）。</summary>
    [HttpGet("status")]
    [RequireModule("imessage")]
    [Permission("imessage.view")]
    public async Task<IActionResult> GetStatus() => Ok(await _imessageService.GetStatusAsync());

    /// <summary>坐席回复（bridge：沿用入站 GUID；AMB：会话未关闭期间可回，冷启动会被拒）。</summary>
    [HttpPost("reply")]
    [RequireModule("imessage")]
    [Permission("imessage.manage")]
    public async Task<IActionResult> Reply([FromBody] IMessageReplyRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = _currentUserAccessor.UserId;
        if (userId == 0) return Unauthorized(new { message = "未登录" });

        var result = await _imessageService.ReplyAsync(request, userId);
        if (!result.Sent) return BadRequest(result);
        // 已发出但落库失败：回 207，让前端明确提示"可能已发送"
        if (result.Sent && !result.ThreadCreated) return StatusCode(StatusCodes.Status207MultiStatus, result);
        return Ok(result);
    }

    /// <summary>测试连通性。bridge 可带 testHandle 实发一条；AMB 只验参数与 JWT 可签发，不向 Apple 发真消息。</summary>
    [HttpPost("test")]
    [RequireModule("imessage")]
    [RequireSuperAdmin]
    public async Task<IActionResult> TestConnection([FromBody] IMessageTestRequest? request = null)
    {
        var result = await _imessageService.TestConnectionAsync(request?.TestHandle);
        return Ok(result);
    }

    /// <summary>
    /// 向 BlueBubbles 登记本系统的 webhook 地址（POST /api/v1/server/webhook）。
    /// 仅 bridge 模式有效；口令只出现在请求体里，绝不写进 URL。
    /// </summary>
    [HttpPost("webhook-register")]
    [RequireModule("imessage")]
    [RequireSuperAdmin]
    public async Task<IActionResult> RegisterWebhook()
    {
        var result = await _imessageService.RegisterWebhookAsync();
        return Ok(result);
    }

    // ============================================================
    // 私有辅助
    // ============================================================

    /// <summary>
    /// bridge 入站共享密钥校验。
    /// 期望值来自 IMessage:BridgeWebhookSecret；调用方可用 X-Webhook-Secret 头，桥接软件不能设头时用 ?secret=。
    /// 必须用 <see cref="CryptographicOperations.FixedTimeEquals"/> 做常量时间比较，普通字符串比较会泄露前缀匹配长度。
    /// 未配置密钥时返回 true（保持旧行为）并只告警一次；配置了则缺失/不等一律 false。
    /// </summary>
    private bool VerifyBridgeWebhookSecret(IMessageOptions settings)
    {
        var expected = settings.BridgeWebhookSecret;
        if (string.IsNullOrWhiteSpace(expected))
        {
            if (Interlocked.Exchange(ref _bridgeIngressAnonymousWarned, 1) == 0)
            {
                _logger.LogWarning(
                    "iMessage：bridge webhook 未配置共享密钥（IMessage:BridgeWebhookSecret），入站处于无鉴权状态，" +
                    "仅靠路径不可猜与网络隔离防护；建议尽快在渠道设置页配置");
            }

            return true;
        }

        var provided = Request.Headers["X-Webhook-Secret"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(provided))
        {
            // 兼容无法自定义请求头的桥接软件：webhook 地址可写 …?secret=xxx
            provided = Request.Query["secret"].FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(provided)) return false;

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return expectedBytes.Length == providedBytes.Length
               && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    private async Task<string> ReadBodyAsync()
    {
        var bytes = await ReadRawBytesAsync();
        return Encoding.UTF8.GetString(bytes);
    }

    private async Task<byte[]> ReadRawBytesAsync()
    {
        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        Request.Body.Position = 0;
        return ms.ToArray();
    }

    /// <summary>Apple 的入站 body 带 Content-Encoding: gzip ⇒ 手动解压（.NET 不自动解请求体）。</summary>
    private string DecompressIfNeeded(byte[] rawBytes)
    {
        var encoding = Request.Headers["Content-Encoding"].ToString();
        if (string.IsNullOrWhiteSpace(encoding) || !encoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.GetString(rawBytes);
        }

        using var compressed = new MemoryStream(rawBytes);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }
}

/// <summary>连通性测试请求（可选指定要实发一条测试消息的 handle，仅 bridge 模式生效）。</summary>
public class IMessageTestRequest
{
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? TestHandle { get; set; }
}
