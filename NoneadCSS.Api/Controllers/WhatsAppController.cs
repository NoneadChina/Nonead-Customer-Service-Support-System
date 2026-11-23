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
/// WhatsApp Business Platform 控制器 - Webhook 接入 + 配置管理 + 回复 + 模板消息。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WhatsAppController : ControllerBase
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly IWhatsAppIngestionService _ingestionService;
    private readonly IWhatsAppSettingsProvider _settingsProvider;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ILogger<WhatsAppController> _logger;

    public WhatsAppController(
        IWhatsAppService whatsAppService,
        IWhatsAppIngestionService ingestionService,
        IWhatsAppSettingsProvider settingsProvider,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<WhatsAppController> logger)
    {
        _whatsAppService = whatsAppService;
        _ingestionService = ingestionService;
        _settingsProvider = settingsProvider;
        _currentUserAccessor = currentUserAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Webhook GET 验证 - Meta 配置 Webhook 时发送的订阅验证请求。
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string? hubMode,
        [FromQuery(Name = "hub.challenge")] string? hubChallenge,
        [FromQuery(Name = "hub.verify_token")] string? hubVerifyToken)
    {
        var settings = _settingsProvider.Current;

        var challenge = WhatsAppWebhookParser.VerifyWebhookSubscription(
            hubMode, hubVerifyToken, hubChallenge, settings.VerifyToken);

        if (challenge == null)
        {
            _logger.LogWarning("WhatsApp Webhook 验证失败：hub.mode={Mode}, verify_token 校验失败", hubMode);
            return Forbid();
        }

        _logger.LogInformation("WhatsApp Webhook 验证成功");
        return Content(challenge, "text/plain");
    }

    /// <summary>
    /// Webhook POST 事件通知 - 接收 Meta 推送的消息/状态回执。
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveWebhook()
    {
        // 1. 读取原始 body（用于签名验证）
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        if (string.IsNullOrEmpty(rawBody))
        {
            return Ok();
        }

        // 2. 签名验证 (X-Hub-Signature-256)
        var settings = _settingsProvider.Current;
        var signatureHeader = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        var rawBytes = System.Text.Encoding.UTF8.GetBytes(rawBody);

        // 强制校验签名：AppSecret 未配置时直接拒绝，避免攻击者伪造任意 Webhook 载荷
        if (string.IsNullOrEmpty(settings.AppSecret))
        {
            _logger.LogWarning("WhatsApp Webhook 签名验证失败：AppSecret 未配置，拒绝请求");
            return Unauthorized();
        }

        if (!WhatsAppWebhookParser.VerifySignature(rawBytes, settings.AppSecret, signatureHeader))
        {
            _logger.LogWarning("WhatsApp Webhook 签名验证失败");
            return Unauthorized();
        }

        // 3. 立即返回 200 OK（避免 Meta 重试），后台处理
        // 注意：WhatsApp 不要求像微信那样返回被动回复 XML
        // 但为了确保数据处理完成再返回，我们同步处理
        try
        {
            var result = await _ingestionService.ProcessWebhookPayloadAsync(rawBody);

            if (result.IsDuplicate)
            {
                _logger.LogInformation("WhatsApp Webhook 消息重复，已跳过");
            }
            else if (result.NewConversationCount > 0)
            {
                _logger.LogInformation("WhatsApp 新会话创建：ConversationId={ConversationId}", result.ConversationId);
            }
            else if (result.ReplyCount > 0)
            {
                _logger.LogInformation("WhatsApp 会话追加回复：ConversationId={ConversationId}", result.ConversationId);
            }
            else if (result.IsStatus)
            {
                _logger.LogDebug("WhatsApp 状态回执处理完成");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp Webhook 处理失败");
            // 仍然返回 200 OK 避免 Meta 重试（已记录日志）
        }

        return Ok();
    }

    /// <summary>获取 WhatsApp 配置（脱敏）</summary>
    [HttpGet("settings")]
    [RequireModule("whatsapp")]
    [Permission("whatsapp.view")]
    public async Task<ActionResult<WhatsAppSettingsDto>> GetSettings()
    {
        var settings = await _whatsAppService.GetSettingsAsync();
        return Ok(settings);
    }

    /// <summary>更新 WhatsApp 配置</summary>
    [HttpPut("settings")]
    [RequireModule("whatsapp")]
    [RequireSuperAdmin]
    public async Task<ActionResult<WhatsAppSettingsDto>> UpdateSettings([FromBody] UpdateWhatsAppSettingsRequest request)
    {
        var settings = await _whatsAppService.UpdateSettingsAsync(request);
        return Ok(settings);
    }

    /// <summary>获取 WhatsApp 连接状态</summary>
    [HttpGet("status")]
    [RequireModule("whatsapp")]
    [Permission("whatsapp.view")]
    public async Task<ActionResult<WhatsAppStatusDto>> GetStatus()
    {
        var status = await _whatsAppService.GetStatusAsync();
        return Ok(status);
    }

    /// <summary>回复客户消息</summary>
    [HttpPost("reply")]
    [RequireModule("whatsapp")]
    [Permission("whatsapp.manage")]
    public async Task<ActionResult<WhatsAppReplyResult>> Reply([FromBody] WhatsAppReplyRequest request)
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0)
        {
            return Unauthorized();
        }

        var result = await _whatsAppService.ReplyAsync(request, userId);

        if (!result.Sent)
        {
            return BadRequest(result);
        }

        if (result.Sent && !result.ThreadCreated)
        {
            return StatusCode(207, result); // Multi-Status
        }

        return Ok(result);
    }

    /// <summary>发送模板消息</summary>
    [HttpPost("template/send")]
    [RequireModule("whatsapp")]
    [Permission("whatsapp.manage")]
    public async Task<ActionResult<WhatsAppTemplateResult>> SendTemplate([FromBody] WhatsAppTemplateRequest request)
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0)
        {
            return Unauthorized();
        }

        var result = await _whatsAppService.SendTemplateMessageAsync(request, userId);

        if (!result.Sent)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>测试连接 — 向商家号码发送测试消息验证配置是否正确</summary>
    [HttpPost("test")]
    [RequireModule("whatsapp")]
    [RequireSuperAdmin]
    public async Task<ActionResult<WhatsAppTestResult>> TestConnection()
    {
        var result = await _whatsAppService.TestConnectionAsync();
        if (!result.Success)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }
}
