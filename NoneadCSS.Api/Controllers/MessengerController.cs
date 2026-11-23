using System.Text;
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
/// Messenger Platform 控制器 - Webhook 接入（GET 校验 + POST 事件）+ 配置管理 + 坐席回复 + 订阅字段。
/// 路由 /api/messenger（必须加入 CSRF 豁免清单，否则 Meta 回调会被 400 拦截）。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessengerController : ControllerBase
{
    private readonly IMessengerService _messengerService;
    private readonly IMessengerIngestionService _ingestionService;
    private readonly IMessengerSettingsProvider _settingsProvider;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ILogger<MessengerController> _logger;

    public MessengerController(
        IMessengerService messengerService,
        IMessengerIngestionService ingestionService,
        IMessengerSettingsProvider settingsProvider,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<MessengerController> logger)
    {
        _messengerService = messengerService;
        _ingestionService = ingestionService;
        _settingsProvider = settingsProvider;
        _currentUserAccessor = currentUserAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Webhook GET 校验 - Meta 配置订阅时发送 hub.mode / hub.verify_token / hub.challenge。
    /// 校验通过必须把 hub.challenge 以纯文本原样回显（200），否则 Meta 判定订阅失败。
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string? hubMode,
        [FromQuery(Name = "hub.challenge")] string? hubChallenge,
        [FromQuery(Name = "hub.verify_token")] string? hubVerifyToken)
    {
        var settings = _settingsProvider.Current;

        var challenge = MessengerWebhookParser.VerifyWebhookSubscription(
            hubMode, hubVerifyToken, hubChallenge, settings.VerifyToken);

        if (challenge == null)
        {
            _logger.LogWarning("Messenger Webhook 校验失败：hub.mode={Mode}, hub.verify_token 比对不通过", hubMode);
            return Forbid();
        }

        _logger.LogInformation("Messenger Webhook 校验成功");
        return Content(challenge, "text/plain");
    }

    /// <summary>
    /// Webhook POST 事件 - 接收 page/messaging 推送（入站消息、postback、optin、送达与已读回执）。
    /// 必须快速返回 2xx：非 2xx 会被 Meta 立即重试、降频，约 36h 后丢弃并可导致取消订阅。
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [RequestSizeLimit(10 * 1024 * 1024)] // 单次批量最多 1000 条 messaging，留出余量
    public async Task<IActionResult> ReceiveWebhook()
    {
        // 1. 读取原始 body（签名必须基于原始字节校验，禁止反序列化后再序列化）
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawBody = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        if (string.IsNullOrEmpty(rawBody))
        {
            return Ok();
        }

        // 2. 签名验证（X-Hub-Signature-256 = sha256=HMAC-SHA256(AppSecret, rawBody)，常量时间比较）
        var settings = _settingsProvider.Current;
        var signatureHeader = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        var rawBytes = Encoding.UTF8.GetBytes(rawBody);

        // 强制校验签名：AppSecret 未配置时直接拒绝，避免攻击者伪造任意 Webhook 载荷
        if (string.IsNullOrEmpty(settings.AppSecret))
        {
            _logger.LogWarning("Messenger Webhook 签名校验失败：AppSecret 未配置，拒绝请求");
            return Unauthorized();
        }

        if (!MessengerWebhookParser.VerifySignature(rawBytes, settings.AppSecret, signatureHeader))
        {
            _logger.LogWarning("Messenger Webhook 签名校验失败");
            return Unauthorized();
        }

        // 3. 同步处理（幂等靠 message.mid 去重），异常也返回 200，避免 Meta 重试风暴
        try
        {
            var result = await _ingestionService.ProcessWebhookPayloadAsync(rawBody);

            if (result.IsDuplicate)
            {
                _logger.LogInformation("Messenger Webhook 消息重复（mid 已存在），已跳过");
            }
            else if (result.NewConversationCount > 0)
            {
                _logger.LogInformation("Messenger 新会话创建：ConversationId={ConversationId}, 忽略回声 {EchoCount}",
                    result.ConversationId, result.IgnoredEchoCount);
            }
            else if (result.ReplyCount > 0)
            {
                _logger.LogInformation("Messenger 会话追加回复：ConversationId={ConversationId}, 忽略回声 {EchoCount}",
                    result.ConversationId, result.IgnoredEchoCount);
            }
            else if (result.ReceiptCount > 0)
            {
                _logger.LogDebug("Messenger 送达/已读回执处理完成：ReceiptCount={ReceiptCount}", result.ReceiptCount);
            }
            else
            {
                _logger.LogDebug("Messenger Webhook 无有效入站事件（可能全部为回声或订阅确认），忽略回声 {EchoCount}", result.IgnoredEchoCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Messenger Webhook 处理失败");
            // 必须让 Meta 重试：2xx 一旦返回就意味着「已送达」，Meta 不再重投，
            // 而入库失败的消息也就永远等不到 mid 幂等补投 ⇒ 客户消息静默丢失。
            // 重投是安全的：入站按 message.mid 去重，重复投递只会命中 IsDuplicate。
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok();
    }

    /// <summary>获取 Messenger 配置（脱敏，密钥只回报是否已配置）</summary>
    [HttpGet("settings")]
    [RequireModule("messenger")]
    [Permission("messenger.view")]
    public async Task<ActionResult<MessengerSettingsDto>> GetSettings()
    {
        var settings = await _messengerService.GetSettingsAsync();
        return Ok(settings);
    }

    /// <summary>更新 Messenger 配置（密钥加密落库，不回显）</summary>
    [HttpPut("settings")]
    [RequireModule("messenger")]
    [RequireSuperAdmin]
    public async Task<ActionResult<MessengerSettingsDto>> UpdateSettings([FromBody] UpdateMessengerSettingsRequest request)
    {
        var settings = await _messengerService.UpdateSettingsAsync(request);
        return Ok(settings);
    }

    /// <summary>获取 Messenger 连接状态（含 24h 窗口与令牌失效告警）</summary>
    [HttpGet("status")]
    [RequireModule("messenger")]
    [Permission("messenger.view")]
    public async Task<ActionResult<MessengerStatusDto>> GetStatus()
    {
        var status = await _messengerService.GetStatusAsync();
        return Ok(status);
    }

    /// <summary>回复客户消息（窗口外按策略带 message_tag）</summary>
    [HttpPost("reply")]
    [RequireModule("messenger")]
    [Permission("messenger.manage")]
    public async Task<ActionResult<MessengerReplyResult>> Reply([FromBody] MessengerReplyRequest request)
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0)
        {
            return Unauthorized();
        }

        var result = await _messengerService.ReplyAsync(request, userId);

        if (!result.Sent)
        {
            return BadRequest(result);
        }

        if (result.Sent && !result.ThreadCreated)
        {
            return StatusCode(207, result); // Multi-Status：已发出但线程落库失败
        }

        return Ok(result);
    }

    /// <summary>发送 typing_on / typing_off / mark_seen 输入态提示</summary>
    [HttpPost("sender-action")]
    [RequireModule("messenger")]
    [Permission("messenger.manage")]
    public async Task<ActionResult<bool>> SendSenderAction([FromBody] MessengerSenderActionRequest request)
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0)
        {
            return Unauthorized();
        }

        var ok = await _messengerService.SendSenderActionAsync(request.ConversationId, request.Action);
        return Ok(ok);
    }

    /// <summary>订阅 Webhook 字段（需 pages_manage_metadata 权限）</summary>
    [HttpPost("subscribe")]
    [RequireModule("messenger")]
    [RequireSuperAdmin]
    public async Task<ActionResult<MessengerSubscribeResult>> Subscribe([FromBody] MessengerSubscribeRequest? request)
    {
        var result = await _messengerService.SubscribeFieldsAsync(request?.Fields);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>测试连接 - 仅探测 Graph 可达性与 Page Access Token 有效性，不向任何 PSID 发消息</summary>
    [HttpPost("test")]
    [RequireModule("messenger")]
    [RequireSuperAdmin]
    public async Task<ActionResult<MessengerTestResult>> TestConnection()
    {
        var result = await _messengerService.TestConnectionAsync();
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }
}
