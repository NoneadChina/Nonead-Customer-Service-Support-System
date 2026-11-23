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
/// Telegram 渠道控制器。
/// 入站：POST /api/telegram/webhook —— Telegram 服务器直接 POST，无签名，只有
///       X-Telegram-Bot-Api-Secret-Token 头（setWebhook 时登记的 secret_token）可校验来源；
///       必须快速返回 200：持续非 200 会让 Telegram 重试并最终禁用 webhook。
/// 出站/配置：settings / status / reply / webhook/setup / webhook/delete / unreachable/clear / test。
/// 路由刻意写死为小写 api/telegram，与 CsrfProtectionMiddleware 的豁免路径与 moduleMenuMap 保持一致。
/// </summary>
[ApiController]
[Route("api/telegram")]
[Authorize]
public class TelegramController : ControllerBase
{
    private readonly ITelegramService _telegramService;
    private readonly ITelegramIngestionService _ingestionService;
    private readonly ITelegramSettingsProvider _settingsProvider;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ILogger<TelegramController> _logger;

    public TelegramController(
        ITelegramService telegramService,
        ITelegramIngestionService ingestionService,
        ITelegramSettingsProvider settingsProvider,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<TelegramController> logger)
    {
        _telegramService = telegramService;
        _ingestionService = ingestionService;
        _settingsProvider = settingsProvider;
        _currentUserAccessor = currentUserAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Telegram Webhook 回调（AllowAnonymous）。
    /// 校验顺序：secret 头（常量时间比对）→ 解析 Update → 交接入层；任何解析/入库异常都仍返回 200，
    /// 由本地日志与重试机制兜底，避免 Telegram 侧禁用 webhook 导致后续消息全部丢失。
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveWebhook()
    {
        var settings = _settingsProvider.Current;

        // 先校验 secret 头（不依赖 body）：Telegram 不签 body，secret_token 是唯一的入站来源校验。
        // 未通过者连 body 都不读，避免未鉴权请求逼进程缓冲任意大报文。
        var configuredSecret = settings.WebhookSecretToken;
        if (string.IsNullOrWhiteSpace(configuredSecret))
        {
            _logger.LogWarning("Telegram Webhook 收到请求但未配置 WebhookSecretToken，已拒绝（请调用 POST /api/telegram/webhook/setup 自动生成）");
            return Unauthorized();
        }

        var headerSecret = Request.Headers[TelegramOptions.WebhookSecretHeader].FirstOrDefault();
        if (!TelegramWebhookParser.VerifySecretToken(configuredSecret, headerSecret))
        {
            _logger.LogWarning("Telegram Webhook 密钥校验失败，来源不可信，已丢弃");
            return Unauthorized();
        }

        const long maxWebhookBodyBytes = 1024 * 1024;
        if (Request.ContentLength is > maxWebhookBodyBytes)
        {
            _logger.LogWarning("Telegram Webhook 报文过大（{Length} 字节 > {Limit}），已拒绝", Request.ContentLength, maxWebhookBodyBytes);
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return Ok();
        }

        try
        {
            var result = await _ingestionService.ProcessWebhookPayloadAsync(rawBody);
            if (result.NewConversationCount > 0 || result.ReplyCount > 0)
            {
                _logger.LogInformation("Telegram Webhook 接入完成：新建会话 {New}，消息 {Reply}，重复 {Duplicate}",
                    result.NewConversationCount, result.ReplyCount, result.IsDuplicate);
            }
        }
        catch (Exception ex)
        {
            // Telegram 对非 2xx 会重发（退避重试，约 24h 后放弃）。因此失败必须让它可见：
            // 返回 500 触发平台重投；返回 200 会把这条消息永久吞掉（旧实现正是如此）。
            _logger.LogError(ex, "Telegram Webhook 处理失败，返回 500 以触发 Telegram 重投");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok();
    }

    /// <summary>读取 Telegram 配置（脱敏，不含 BotToken / WebhookSecretToken）</summary>
    [HttpGet("settings")]
    [RequireModule("telegram")]
    [Permission("telegram.view")]
    public async Task<ActionResult<TelegramSettingsDto>> GetSettings()
    {
        return Ok(await _telegramService.GetSettingsAsync());
    }

    /// <summary>保存 Telegram 配置（仅超管；按当前 Mode 自动 setWebhook / deleteWebhook）</summary>
    [HttpPut("settings")]
    [RequireModule("telegram")]
    [RequireSuperAdmin]
    public async Task<ActionResult<TelegramSettingsDto>> UpdateSettings([FromBody] UpdateTelegramSettingsRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return Ok(await _telegramService.UpdateSettingsAsync(request));
    }

    /// <summary>读取状态（getMe + getWebhookInfo + 轮询游标 + 不可达会话数）</summary>
    [HttpGet("status")]
    [RequireModule("telegram")]
    [Permission("telegram.view")]
    public async Task<ActionResult<TelegramStatusDto>> GetStatus()
    {
        return Ok(await _telegramService.GetStatusAsync());
    }

    /// <summary>坐席回复（超长自动分片；403 标记不可达；429 返回 retry_after）</summary>
    [HttpPost("reply")]
    [RequireModule("telegram")]
    [Permission("telegram.manage")]
    public async Task<ActionResult<TelegramReplyResult>> Reply([FromBody] TelegramReplyRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var userId = _currentUserAccessor.UserId;
        if (userId == 0)
        {
            return Unauthorized();
        }

        var result = await _telegramService.ReplyAsync(request, userId);
        if (!result.Sent)
        {
            return BadRequest(result);
        }

        if (result.Sent && !result.ThreadCreated)
        {
            // 消息已发到 Telegram，但站内线程写入失败：207 让前端明确提示"已发出未入库"
            return StatusCode(StatusCodes.Status207MultiStatus, result);
        }

        return Ok(result);
    }

    /// <summary>登记/重新登记 Webhook（自动生成并加密保存缺失的 secret_token）</summary>
    [HttpPost("webhook/setup")]
    [RequireModule("telegram")]
    [RequireSuperAdmin]
    public async Task<ActionResult<TelegramTestResult>> SetupWebhook([FromBody] TelegramWebhookSetupRequest? request)
    {
        var result = await _telegramService.SetupWebhookAsync(request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>删除 Telegram 侧 Webhook（切换轮询前 / 排障）</summary>
    [HttpPost("webhook/delete")]
    [RequireModule("telegram")]
    [RequireSuperAdmin]
    public async Task<ActionResult<TelegramTestResult>> DeleteWebhook()
    {
        var result = await _telegramService.RemoveWebhookAsync();
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>清除会话的不可达标记（客户重新联系或坐席确认后手动清除）</summary>
    [HttpPost("unreachable/clear")]
    [RequireModule("telegram")]
    [Permission("telegram.manage")]
    public async Task<ActionResult<TelegramReplyResult>> ClearUnreachable([FromBody] TelegramUnreachableStateRequest request)
    {
        var cleared = await _telegramService.ClearUnreachableMarkAsync(request.ConversationId);
        return Ok(new TelegramReplyResult
        {
            Sent = cleared,
            ErrorMessage = cleared ? null : "该会话没有不可达标记"
        });
    }

    /// <summary>连接性测试（getMe，不发送任何消息，也不打印 token）</summary>
    [HttpPost("test")]
    [RequireModule("telegram")]
    [RequireSuperAdmin]
    public async Task<ActionResult<TelegramTestResult>> TestConnection()
    {
        var result = await _telegramService.TestConnectionAsync();
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
