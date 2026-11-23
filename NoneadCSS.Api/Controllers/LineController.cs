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
/// LINE Messaging API 控制器 - Webhook 接入（验签）+ 配置管理 + 客服回复 + 人工接管 + 连通性自检。
/// 路由约定：/api/line（模块 line），Webhook 路径取自 LineOptions.WebhookPath（默认 /api/line/webhook）。
/// 注意：LINE 没有 GET 订阅校验握手（区别于 WhatsApp 的 hub.challenge），
/// 校验由 LINE 侧 POST /v2/bot/channel/webhook/test 向本端点投递测试事件完成 ⇒ 见 webhook-check 端点。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LineController : ControllerBase
{
    private const int MaxWebhookBodyBytes = 1024 * 1024; // LINE 单批事件很小，超限直接判为异常请求

    private readonly ILineService _lineService;
    private readonly ILineIngestionService _ingestionService;
    private readonly ILineSettingsProvider _settingsProvider;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ILogger<LineController> _logger;

    public LineController(
        ILineService lineService,
        ILineIngestionService ingestionService,
        ILineSettingsProvider settingsProvider,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<LineController> logger)
    {
        _lineService = lineService;
        _ingestionService = ingestionService;
        _settingsProvider = settingsProvider;
        _currentUserAccessor = currentUserAccessor;
        _logger = logger;
    }

    /// <summary>
    /// LINE Webhook 回调 - 校验 X-Line-Signature（Base64 HMAC-SHA256，对原始字节计算）后落库。
    /// 必须在 10 秒内返回 2xx（LINE 超时策略未公开，按 10 秒口径实现），因此事件处理保持精简，
    /// 失败也只记日志并返回 200，避免 LINE 反复重投导致重复入站（入站本身按 message.id 幂等）。
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [Consumes("application/json")]
    public async Task<IActionResult> ReceiveWebhook()
    {
        // 1. 读取原始字节（签名是对原始字节算的，不能经过 JSON 绑定或重新编码）
        Request.EnableBuffering();
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer);
        var rawBytes = buffer.ToArray();
        Request.Body.Position = 0;

        if (rawBytes.Length == 0)
        {
            return Ok();
        }

        if (rawBytes.Length > MaxWebhookBodyBytes)
        {
            _logger.LogWarning("LINE Webhook 请求体过大（{Bytes} 字节），已丢弃", rawBytes.Length);
            return Ok();
        }

        // 2. 签名验证（X-Line-Signature）：ChannelSecret 未配置时直接 401，避免伪造载荷入库
        var settings = _settingsProvider.Current;
        var signatureHeader = Request.Headers["X-Line-Signature"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(settings.ChannelSecret))
        {
            _logger.LogWarning("LINE Webhook 验签失败：ChannelSecret 未配置，拒绝请求");
            return Unauthorized();
        }

        // 模块停用：验签通过后再判，避免未签名请求探测开关状态；
        // 与 Telegram/Messenger 口径一致，返回 200 让 LINE 认为已投递（停用期间的事件不入库）。
        if (!settings.Enabled)
        {
            _logger.LogInformation("LINE Webhook 已忽略：LINE 模块当前停用");
            return Ok();
        }

        if (!LineWebhookParser.VerifySignature(rawBytes, settings.ChannelSecret, signatureHeader))
        {
            // 不打印签名内容与请求体，避免敏感信息入日志
            _logger.LogWarning("LINE Webhook 验签失败：签名不匹配或格式非法");
            return Unauthorized();
        }

        // 3. 处理事件（同步进行：replyToken 有效期极短，异步排队会让自动回复只能走 push 消耗配额）
        try
        {
            var payload = Encoding.UTF8.GetString(rawBytes);
            var result = await _ingestionService.ProcessWebhookPayloadAsync(payload);

            if (result.EventCount == 0)
            {
                _logger.LogDebug("LINE Webhook 无可用事件");
            }
            else if (result.NewConversationCount > 0)
            {
                _logger.LogInformation("LINE 新会话创建：ConversationId={ConversationId} Events={Events}", result.ConversationId, result.EventCount);
            }
            else if (result.ReplyCount > 0)
            {
                _logger.LogInformation("LINE 会话追加消息：ConversationId={ConversationId} Events={Events}", result.ConversationId, result.EventCount);
            }

            if (result.IsDuplicate)
            {
                _logger.LogInformation("LINE Webhook 事件重复，已跳过 dedupe");
            }

            if (result.SkippedStandbyCount > 0)
            {
                _logger.LogWarning("LINE 事件 mode=standby（频道待机），已跳过自动回复（{Count} 条）", result.SkippedStandbyCount);
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                _logger.LogWarning("LINE Webhook 部分事件处理失败：{Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            // LINE 对非 2xx 会重投，而重投是安全的（入站按 message.id 幂等去重）。
            // 因此这里必须把失败暴露给平台：返回 500 让 LINE 重投，而不是 200 把这条客户消息永久吞掉。
            _logger.LogError(ex, "LINE Webhook 处理失败，返回 500 以触发 LINE 重投");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok();
    }

    /// <summary>获取 LINE 配置（脱敏，不回显密钥）</summary>
    [HttpGet("settings")]
    [RequireModule("line")]
    [Permission("line.view")]
    public async Task<ActionResult<LineSettingsDto>> GetSettings()
    {
        var settings = await _lineService.GetSettingsAsync();
        return Ok(settings);
    }

    /// <summary>更新 LINE 配置（密钥加密落 SystemOptions，空值表示不修改）</summary>
    [HttpPut("settings")]
    [RequireModule("line")]
    [RequireSuperAdmin]
    public async Task<ActionResult<LineSettingsDto>> UpdateSettings([FromBody] UpdateLineSettingsRequest request)
    {
        var settings = await _lineService.UpdateSettingsAsync(request);
        return Ok(settings);
    }

    /// <summary>获取 LINE 连接状态（令牌有效性、配额、Webhook 端点、人工接管提示）</summary>
    [HttpGet("status")]
    [RequireModule("line")]
    [Permission("line.view")]
    public async Task<ActionResult<LineStatusDto>> GetStatus()
    {
        var status = await _lineService.GetStatusAsync();
        return Ok(status);
    }

    /// <summary>客服回复（replyToken 新鲜时走 reply，不消耗月度配额；否则回落 push）</summary>
    [HttpPost("reply")]
    [RequireModule("line")]
    [Permission("line.manage")]
    public async Task<ActionResult<LineReplyResult>> Reply([FromBody] LineReplyRequest request)
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0)
        {
            return Unauthorized();
        }

        var result = await _lineService.ReplyAsync(request, userId);

        if (!result.Sent)
        {
            return BadRequest(result);
        }

        if (result.Sent && !result.ThreadCreated)
        {
            return StatusCode(StatusCodes.Status207MultiStatus, result);
        }

        return Ok(result);
    }

    /// <summary>启用/延长人工接管：接管期内机器人不再自动回复（LINE 无会话接管 API，纯系统内实现）</summary>
    [HttpPost("takeover")]
    [RequireModule("line")]
    [Permission("line.manage")]
    public async Task<ActionResult<bool>> SetTakeover([FromBody] LineTakeoverRequest request)
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0)
        {
            return Unauthorized();
        }

        var ok = await _lineService.SetHumanTakeoverAsync(request, userId);
        return ok ? Ok(true) : BadRequest(false);
    }

    /// <summary>结束人工接管</summary>
    [HttpDelete("takeover/{conversationId:int}")]
    [RequireModule("line")]
    [Permission("line.manage")]
    public async Task<ActionResult<bool>> ReleaseTakeover(int conversationId)
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0)
        {
            return Unauthorized();
        }

        var ok = await _lineService.ReleaseHumanTakeoverAsync(conversationId, userId);
        return ok ? Ok(true) : BadRequest(false);
    }

    /// <summary>推送测试（消耗月度配额；对应 docs 的 push 探活，只允许超级管理员）</summary>
    [HttpPost("test")]
    [RequireModule("line")]
    [RequireSuperAdmin]
    public async Task<ActionResult<LineSendResult>> PushTest([FromBody] LinePushTestRequest? request = null)
    {
        var result = await _lineService.PushTestAsync(request ?? new LinePushTestRequest());
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>校验 LINE 侧回调地址连通性（LINE 官方 webhook/test，属 60/小时限流族）</summary>
    [HttpPost("webhook-check")]
    [RequireModule("line")]
    [RequireSuperAdmin]
    public async Task<ActionResult<LineWebhookCheckResult>> CheckWebhook([FromBody] LineWebhookCheckRequest? request = null)
    {
        var result = await _lineService.CheckWebhookAsync(request ?? new LineWebhookCheckRequest());
        return Ok(result);
    }

    /// <summary>把本部署回调地址写回 LINE 侧（可编程配置 Webhook）</summary>
    [HttpPost("webhook-endpoint")]
    [RequireModule("line")]
    [RequireSuperAdmin]
    public async Task<ActionResult<LineWebhookCheckResult>> ApplyWebhookEndpoint([FromQuery] bool enabled = true)
    {
        var result = await _lineService.ApplyWebhookEndpointAsync(enabled);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
