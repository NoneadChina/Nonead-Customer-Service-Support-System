using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 钉钉开放平台控制器 - 事件订阅 HTTP 推送接收 + 机器人 Webhook + 配置管理 + 回复消息。
/// 支持两种 HTTP 接收模式：
///   1. 事件订阅 HTTP 推送（加密）：GET 验证 URL + POST 加密回调（msg_signature + encrypt）
///   2. 机器人 outgoing Webhook（明文）：POST 明文 JSON（timestamp + sign header）
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DingDingController : ControllerBase
{
    private readonly IDingDingService _dingDingService;
    private readonly IDingDingIngestionService _ingestionService;
    private readonly IDingDingSettingsProvider _settingsProvider;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ILogger<DingDingController> _logger;

    public DingDingController(
        IDingDingService dingDingService,
        IDingDingIngestionService ingestionService,
        IDingDingSettingsProvider settingsProvider,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<DingDingController> logger)
    {
        _dingDingService = dingDingService;
        _ingestionService = ingestionService;
        _settingsProvider = settingsProvider;
        _currentUserAccessor = currentUserAccessor;
        _logger = logger;
    }

    /// <summary>
    /// GET 验证回调 URL 有效性（钉钉事件订阅 HTTP 模式注册时触发）。
    /// 钉钉发送 GET 请求，query 含 signature/msg_signature、timestamp、nonce、encrypt，
    /// 服务端需校验签名 → AES 解密 encrypt → 返回明文 echostr 的加密 Map。
    /// 必须在 2500ms 内响应。
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult VerifyUrl(
        [FromQuery(Name = "msg_signature")] string? msgSignature,
        [FromQuery(Name = "signature")] string? signature,
        [FromQuery(Name = "timestamp")] string? timestamp,
        [FromQuery(Name = "timeStamp")] string? timeStampAlt,
        [FromQuery(Name = "nonce")] string? nonce,
        [FromQuery(Name = "encrypt")] string? encrypt)
    {
        var sig = msgSignature ?? signature;
        var ts = timestamp ?? timeStampAlt;

        if (string.IsNullOrEmpty(sig) || string.IsNullOrEmpty(ts) || string.IsNullOrEmpty(encrypt))
        {
            _logger.LogWarning("钉钉 URL 验证缺少必要参数");
            return BadRequest(new { errmsg = "missing parameters" });
        }

        var settings = _settingsProvider.Current;
        if (string.IsNullOrEmpty(settings.CallbackToken) || string.IsNullOrEmpty(settings.EncodingAesKey) || string.IsNullOrEmpty(settings.AppKey))
        {
            _logger.LogError("钉钉 URL 验证失败：CallbackToken/EncodingAesKey/AppKey 未配置");
            return StatusCode(500, new { errmsg = "server not configured" });
        }

        var (success, plaintext, error) = DingTalkCallbackCrypto.Decrypt(
            settings.CallbackToken, settings.EncodingAesKey, settings.AppKey,
            sig, ts, nonce ?? string.Empty, encrypt);

        if (!success || string.IsNullOrEmpty(plaintext))
        {
            _logger.LogWarning("钉钉 URL 验证解密失败：{Error}", error);
            return Ok(new { errmsg = "decrypt failed" });
        }

        _logger.LogInformation("钉钉 URL 验证成功：echostr={EchoStr}", plaintext.Length > 50 ? plaintext[..50] + "..." : plaintext);

        // 返回加密后的 "success" 响应（钉钉要求返回加密 Map）
        var encryptedResp = DingTalkCallbackCrypto.Encrypt(
            settings.CallbackToken, settings.EncodingAesKey, settings.AppKey, plaintext);
        return Ok(encryptedResp);
    }

    /// <summary>
    /// POST 事件通知 - 接收钉钉推送的消息（兼容两种模式）。
    /// 模式1（事件订阅 HTTP 推送）：query 含 msg_signature/timestamp/nonce，body 含 encrypt 字段 → AES 解密后处理
    /// 模式2（机器人 outgoing Webhook）：header 含 timestamp/sign，body 为明文 JSON → 签名校验后处理
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveWebhook()
    {
        // 1. 读取原始 body
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        if (string.IsNullOrEmpty(rawBody))
            return Ok();

        var settings = _settingsProvider.Current;

        // 2. 判断模式：事件订阅 HTTP 推送（query 有 msg_signature/signature + body 有 encrypt）
        var msgSignature = Request.Query["msg_signature"].FirstOrDefault() ?? Request.Query["signature"].FirstOrDefault();
        var hasEncrypt = rawBody.Contains("\"encrypt\"", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(msgSignature) && hasEncrypt)
        {
            // ===== 模式1：事件订阅 HTTP 推送（加密回调）=====
            return await HandleEncryptedCallback(rawBody, msgSignature, settings);
        }

        // ===== 模式2：机器人 outgoing Webhook（明文）=====
        // 强制校验签名：AppSecret 未配置时直接拒绝，避免攻击者伪造任意 Webhook 载荷
        if (string.IsNullOrEmpty(settings.AppSecret))
        {
            _logger.LogWarning("钉钉 Webhook 签名验证失败：AppSecret 未配置，拒绝请求");
            return Unauthorized();
        }

        {
            var timestamp = Request.Headers["timestamp"].FirstOrDefault();
            var sign = Request.Headers["sign"].FirstOrDefault();

            if (!DingDingWebhookParser.VerifySignature(timestamp, sign, settings.AppSecret))
            {
                _logger.LogWarning("钉钉 Webhook 签名验证失败");
                return Unauthorized();
            }
        }

        // 3. 处理消息
        try
        {
            var result = await _ingestionService.ProcessWebhookPayloadAsync(rawBody);

            if (result.IsDuplicate)
                _logger.LogInformation("钉钉 Webhook 消息重复，已跳过");
            else if (result.NewConversationCount > 0)
                _logger.LogInformation("钉钉新会话创建：ConversationId={ConversationId}", result.ConversationId);
            else if (result.ReplyCount > 0)
                _logger.LogInformation("钉钉会话追加回复：ConversationId={ConversationId}", result.ConversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉 Webhook 处理失败");
        }

        return Ok();
    }

    /// <summary>
    /// 处理事件订阅 HTTP 推送的加密回调。
    /// </summary>
    private async Task<IActionResult> HandleEncryptedCallback(
        string rawBody, string msgSignature, Application.Configuration.DingDingOptions settings)
    {
        var timestamp = Request.Query["timestamp"].FirstOrDefault() ?? Request.Query["timeStamp"].FirstOrDefault();
        var nonce = Request.Query["nonce"].FirstOrDefault() ?? string.Empty;

        if (string.IsNullOrEmpty(settings.CallbackToken) || string.IsNullOrEmpty(settings.EncodingAesKey) || string.IsNullOrEmpty(settings.AppKey))
        {
            _logger.LogError("钉钉加密回调处理失败：CallbackToken/EncodingAesKey/AppKey 未配置");
            return Ok(new { errmsg = "server not configured" });
        }

        // 提取 encrypt 字段
        string? encrypt;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            encrypt = doc.RootElement.TryGetProperty("encrypt", out var encEl) ? encEl.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "钉钉加密回调 body 解析失败");
            return Ok(new { errmsg = "invalid body" });
        }

        if (string.IsNullOrEmpty(encrypt))
        {
            _logger.LogWarning("钉钉加密回调缺少 encrypt 字段");
            return Ok(new { errmsg = "missing encrypt" });
        }

        // 解密
        var (success, plaintext, error) = DingTalkCallbackCrypto.Decrypt(
            settings.CallbackToken, settings.EncodingAesKey, settings.AppKey,
            msgSignature, timestamp ?? string.Empty, nonce, encrypt);

        if (!success || string.IsNullOrEmpty(plaintext))
        {
            _logger.LogWarning("钉钉加密回调解密失败：{Error}", error);
            // 返回加密的 "success" 以避免钉钉重试（解密失败可能是测试请求）
            var failResp = DingTalkCallbackCrypto.Encrypt(
                settings.CallbackToken, settings.EncodingAesKey, settings.AppKey, "success");
            return Ok(failResp);
        }

        _logger.LogDebug("钉钉加密回调解密成功：{Plaintext}", plaintext.Length > 200 ? plaintext[..200] + "..." : plaintext);

        // 检查解密后的事件类型（check_url / 普通 EVENT / 机器人消息）
        string? eventType = null;
        bool hasBotMsgFields = false;
        try
        {
            using var checkDoc = JsonDocument.Parse(plaintext);
            if (checkDoc.RootElement.TryGetProperty("EventType", out var etEl))
                eventType = etEl.GetString();

            // check_url 验证事件：直接返回加密 success
            if (eventType == "check_url")
            {
                _logger.LogInformation("钉钉 check_url 验证事件");
                var resp = DingTalkCallbackCrypto.Encrypt(
                    settings.CallbackToken, settings.EncodingAesKey, settings.AppKey, "success");
                return Ok(resp);
            }

            // 判断是否含有机器人 outgoing Webhook 结构（msgtype / msgId / conversationType 任一即可）
            hasBotMsgFields = checkDoc.RootElement.TryGetProperty("msgtype", out _)
                              || checkDoc.RootElement.TryGetProperty("msgId", out _)
                              || checkDoc.RootElement.TryGetProperty("conversationType", out _);

            // 纯 EVENT 类型（订阅事件、非机器人消息）：不进 ingestion，记录日志后跳过（和 Stream 模式行为一致）
            if (!string.IsNullOrEmpty(eventType) && !hasBotMsgFields)
            {
                string? eventId = null;
                if (checkDoc.RootElement.TryGetProperty("EventId", out var eidEl))
                    eventId = eidEl.GetString();
                _logger.LogInformation("钉钉事件订阅 EVENT（非机器人消息），跳过 ingestion：EventType={EventType}, EventId={EventId}",
                    eventType, eventId);
                var evtResp = DingTalkCallbackCrypto.Encrypt(
                    settings.CallbackToken, settings.EncodingAesKey, settings.AppKey, "success");
                return Ok(evtResp);
            }
        }
        catch { /* 非标准 JSON，继续走消息处理流程 */ }

        // 处理解密后的消息（机器人 outgoing Webhook 结构）
        try
        {
            _logger.LogDebug("钉钉加密回调：进入机器人消息 ingestion 流程 EventType={EventType}, HasBotMsgFields={HasBotMsgFields}",
                eventType, hasBotMsgFields);
            var result = await _ingestionService.ProcessWebhookPayloadAsync(plaintext);

            if (result.IsDuplicate)
                _logger.LogInformation("钉钉事件订阅消息重复，已跳过");
            else if (result.NewConversationCount > 0)
                _logger.LogInformation("钉钉事件订阅新会话创建：ConversationId={ConversationId}", result.ConversationId);
            else if (result.ReplyCount > 0)
                _logger.LogInformation("钉钉事件订阅追加回复：ConversationId={ConversationId}", result.ConversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉事件订阅消息处理失败");
        }

        // 返回加密的 "success" 响应
        var successResp = DingTalkCallbackCrypto.Encrypt(
            settings.CallbackToken, settings.EncodingAesKey, settings.AppKey, "success");
        return Ok(successResp);
    }

    /// <summary>获取钉钉配置（脱敏）</summary>
    [HttpGet("settings")]
    [RequireModule("dingding")]
    [Permission("dingding.view")]
    public async Task<ActionResult<DingDingSettingsDto>> GetSettings()
    {
        var settings = await _dingDingService.GetSettingsAsync();
        return Ok(settings);
    }

    /// <summary>更新钉钉配置</summary>
    [HttpPut("settings")]
    [RequireModule("dingding")]
    [RequireSuperAdmin]
    public async Task<ActionResult<DingDingSettingsDto>> UpdateSettings([FromBody] UpdateDingDingSettingsRequest request)
    {
        var settings = await _dingDingService.UpdateSettingsAsync(request);
        return Ok(settings);
    }

    /// <summary>获取钉钉连接状态</summary>
    [HttpGet("status")]
    [RequireModule("dingding")]
    [Permission("dingding.view")]
    public async Task<ActionResult<DingDingStatusDto>> GetStatus()
    {
        var status = await _dingDingService.GetStatusAsync();
        return Ok(status);
    }

    /// <summary>回复客户消息</summary>
    [HttpPost("reply")]
    [RequireModule("dingding")]
    [Permission("dingding.manage")]
    public async Task<ActionResult<DingDingReplyResult>> Reply([FromBody] DingDingReplyRequest request)
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0)
        {
            return Unauthorized();
        }

        var result = await _dingDingService.ReplyAsync(request, userId);

        if (!result.Sent)
        {
            return BadRequest(result);
        }

        if (result.Sent && !result.ThreadCreated)
        {
            return StatusCode(207, result);
        }

        return Ok(result);
    }
}
