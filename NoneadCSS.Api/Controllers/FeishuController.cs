using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 飞书机器人应用接入控制器 - 包含匿名事件回调端点（飞书服务器调用）和鉴权管理端点（后台配置）。
/// 回调端点（GET URL 验证 + POST 事件推送）使用 [AllowAnonymous] 并通过 CSRF 豁免放行。
/// 管理端点需要登录 + feishu.view / feishu.manage 权限。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FeishuController : ControllerBase
{
    private readonly IFeishuService _feishuService;
    private readonly IFeishuIngestionService _ingestionService;
    private readonly IFeishuSettingsProvider _settingsProvider;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ILogger<FeishuController> _logger;
    private readonly IWebHostEnvironment _env;

    public FeishuController(
        IFeishuService feishuService,
        IFeishuIngestionService ingestionService,
        IFeishuSettingsProvider settingsProvider,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<FeishuController> logger,
        IWebHostEnvironment env)
    {
        _feishuService = feishuService;
        _ingestionService = ingestionService;
        _settingsProvider = settingsProvider;
        _currentUserAccessor = currentUserAccessor;
        _logger = logger;
        _env = env;
    }

    /// <summary>
    /// 飞书 URL 验证（GET）。
    /// </summary>
    /// <remarks>
    /// 该端点**仅供开发/测试环境使用**：GET 不带签名校验，生产环境一律返回 404（安全加固 S-M2）。
    /// 生产环境飞书的 URL 验证由 POST 端点（带 X-Lark-Signature 校验）完成。
    /// 开发环境行为：带 challenge 参数时原样回显；不带时返回 403。
    /// </remarks>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult VerifyUrl([FromQuery] string? challenge)
    {
        // S-M2 修复：GET 端点无签名校验，生产环境禁用。
        // 飞书的 URL 验证实际由 POST 端点（带签名校验）处理，GET 端点仅开发测试用。
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }

        if (string.IsNullOrEmpty(challenge))
        {
            return Forbid();
        }

        _logger.LogInformation("飞书 URL 验证（GET）：challenge={Challenge}", challenge);
        return Ok(new { challenge });
    }

    /// <summary>
    /// 接收飞书推送的事件（POST）。
    /// 飞书事件订阅以 JSON 格式推送用户消息和事件，启用加密时需校验 X-Lark-Signature 并解密。
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveEvent()
    {
        // 1. 读取原始 JSON body（用于签名校验）
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        var contentType = Request.ContentType ?? string.Empty;
        var contentLength = Request.ContentLength ?? rawBody?.Length ?? 0;
        _logger.LogInformation("飞书事件回调入口：ContentLength={ContentLength}, ContentType={ContentType}, Path={Path}",
            contentLength, contentType, Request.Path);

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            _logger.LogWarning("飞书事件回调收到空 body，忽略");
            return Ok();
        }

        // 2. 未启用接入时静默返回 Ok，避免飞书服务器重试
        var settings = _settingsProvider.Current;
        if (!settings.Enabled)
        {
            _logger.LogWarning("飞书接入未启用，拒绝事件推送（返回 200 避免飞书重试）");
            return Ok();
        }

        // 实际传递给 ingestion 的事件 JSON（加密场景下为解密后的明文）
        var effectiveBody = rawBody;

        try
        {
            // 3. 强制校验 X-Lark-Signature 头（飞书始终会推送签名，无论是否启用加密）
            var signature = Request.Headers["X-Lark-Signature"].FirstOrDefault();
            var timestamp = Request.Headers["X-Lark-Request-Timestamp"].FirstOrDefault();
            var nonce = Request.Headers["X-Lark-Request-Nonce"].FirstOrDefault();
            var encryptKey = settings.EncryptKey ?? string.Empty;

            if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(nonce))
            {
                _logger.LogWarning("飞书事件推送缺少签名参数，拒绝请求");
                return Unauthorized();
            }

            if (settings.EncryptEnabled)
            {
                _logger.LogInformation("飞书事件加密模式：启用签名校验+解密, HasSignature={HasSignature}, HasTimestamp={HasTimestamp}, HasNonce={HasNonce}",
                    !string.IsNullOrEmpty(signature), !string.IsNullOrEmpty(timestamp), !string.IsNullOrEmpty(nonce));

                if (!FeishuMessageParser.ValidateSignature(timestamp, nonce, encryptKey, rawBody, signature))
                {
                    _logger.LogWarning("飞书事件推送签名校验失败：signature={Signature}", signature);
                    return Unauthorized();
                }

                _logger.LogInformation("飞书事件签名校验通过");

                // 提取 encrypt 字段并解密
                var encryptedData = ExtractEncryptField(rawBody);
                if (string.IsNullOrEmpty(encryptedData))
                {
                    _logger.LogWarning("飞书事件推送启用加密但未携带 encrypt 字段");
                    return Ok();
                }

                var decrypted = FeishuMessageParser.DecryptPayload(encryptKey, encryptedData);
                if (string.IsNullOrEmpty(decrypted))
                {
                    _logger.LogWarning("飞书事件载荷解密失败：EncryptKey 可能不匹配");
                    return Ok();
                }

                _logger.LogInformation("飞书事件载荷解密成功：DecryptedLength={DecryptedLength}", decrypted.Length);
                effectiveBody = decrypted;
            }
            else
            {
                // 关闭加密时，若配置了 EncryptKey 仍校验签名完整性；否则仅要求签名头存在
                if (!string.IsNullOrEmpty(encryptKey))
                {
                    if (!FeishuMessageParser.ValidateSignature(timestamp, nonce, encryptKey, rawBody, signature))
                    {
                        _logger.LogWarning("飞书事件推送签名校验失败：signature={Signature}", signature);
                        return Unauthorized();
                    }
                    _logger.LogInformation("飞书事件签名校验通过");
                }
                else
                {
                    // 未配置 EncryptKey 时，使用 VerificationToken 作为签名密钥（飞书签名算法支持任意字符串作为 key）
                    var fallbackKey = settings.VerificationToken ?? string.Empty;
                    if (!string.IsNullOrEmpty(fallbackKey))
                    {
                        if (!FeishuMessageParser.ValidateSignature(timestamp, nonce, fallbackKey, rawBody, signature))
                        {
                            _logger.LogWarning("飞书事件推送签名校验失败（使用 VerificationToken 作为密钥）：signature={Signature}", signature);
                            return Unauthorized();
                        }
                        _logger.LogInformation("飞书事件签名校验通过（使用 VerificationToken 作为密钥）");
                    }
                    else
                    {
                        _logger.LogWarning("飞书事件推送缺少 EncryptKey 和 VerificationToken，无法校验签名，拒绝请求");
                        return Unauthorized();
                    }
                }
            }

            // 5. 检查是否为 URL 验证挑战请求
            var eventDto = FeishuMessageParser.ParseEvent(effectiveBody);
            if (eventDto != null && eventDto.IsChallenge)
            {
                _logger.LogInformation("飞书 URL 验证（POST）：challenge={Challenge}", eventDto.Challenge);
                return Ok(new { challenge = eventDto.Challenge ?? string.Empty });
            }

            // 6. 校验事件 token 是否与配置一致（所有事件必须携带 token 且与配置一致）
            if (eventDto == null || string.IsNullOrEmpty(eventDto.Token))
            {
                _logger.LogWarning("飞书事件缺少 token，拒绝请求");
                return Unauthorized();
            }

            var expectedToken = settings.VerificationToken ?? string.Empty;
            if (!string.Equals(eventDto.Token, expectedToken, StringComparison.Ordinal))
            {
                _logger.LogWarning("飞书事件 token 校验失败");
                return Unauthorized();
            }

            _logger.LogInformation("飞书事件进入 ingestion 处理流程：EventType={EventType}, MessageId={MessageId}",
                eventDto?.EventType, eventDto?.MessageId);

            // 7. 交由 ingestion 服务处理事件（传入解密后的明文 JSON，便于服务端解析）
            var result = await _ingestionService.ProcessEventAsync(effectiveBody);

            if (result.IsDuplicate)
            {
                _logger.LogInformation("飞书事件消息重复，已跳过");
            }
            else if (result.NewConversationCount > 0)
            {
                _logger.LogInformation("飞书新会话创建成功：ConversationId={ConversationId}, AutoReply={HasAutoReply}",
                    result.ConversationId, !string.IsNullOrEmpty(result.AutoReplyContent));
            }
            else if (result.ReplyCount > 0)
            {
                _logger.LogInformation("飞书会话追加回复成功：ConversationId={ConversationId}", result.ConversationId);
            }
            else if (result.IsEvent)
            {
                _logger.LogDebug("飞书事件回调处理完成（非消息事件）");
            }
            else if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                _logger.LogWarning("飞书事件处理返回错误：ErrorMessage={ErrorMessage}", result.ErrorMessage);
            }

            // 8. 飞书不要求被动回复，返回 200 OK 即可
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "飞书事件处理异常：Path={Path}", Request.Path);
            // 9. 即使处理失败也返回 Ok，避免飞书服务器反复重试
            return Ok();
        }
    }

    // ====== 以下为管理端点，需要鉴权 ======

    /// <summary>
    /// 获取飞书配置（敏感字段脱敏）。
    /// </summary>
    [HttpGet("settings")]
    [Authorize]
    [RequireModule("feishu")]
    [Permission("feishu.view")]
    public async Task<ActionResult> GetSettings()
    {
        var settings = await _feishuService.GetSettingsAsync();
        return Ok(settings);
    }

    /// <summary>
    /// 更新飞书配置。仅超级管理员可操作。
    /// </summary>
    [HttpPut("settings")]
    [Authorize]
    [RequireModule("feishu")]
    [RequireSuperAdmin]
    public async Task<ActionResult> UpdateSettings([FromBody] UpdateFeishuSettingsRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var settings = await _feishuService.UpdateSettingsAsync(request);
        return Ok(settings);
    }

    /// <summary>
    /// 获取飞书连接状态。
    /// </summary>
    [HttpGet("status")]
    [Authorize]
    [RequireModule("feishu")]
    [Permission("feishu.view")]
    public async Task<ActionResult> GetStatus()
    {
        var status = await _feishuService.GetStatusAsync();
        return Ok(status);
    }

    /// <summary>
    /// 通过飞书消息 API 回复客户。
    /// </summary>
    [HttpPost("reply")]
    [Authorize]
    [RequireModule("feishu")]
    [Permission("feishu.manage")]
    public async Task<ActionResult> Reply([FromBody] FeishuReplyRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = _currentUserAccessor.UserId;
        if (userId == 0)
        {
            return Unauthorized();
        }

        var result = await _feishuService.ReplyAsync(request, userId);
        if (!result.Sent)
        {
            return BadRequest(new { message = result.ErrorMessage });
        }

        // 消息已发送但线程记录创建失败 → 返回 207 Multi-Status，前端需提示用户数据可能不一致
        if (!result.ThreadCreated)
        {
            return StatusCode(207, result);
        }

        return Ok(result);
    }

    /// <summary>
    /// 从飞书加密事件 JSON 中提取 encrypt 字段值。
    /// 加密载荷格式：{ "encrypt": "&lt;Base64 加密数据&gt;" }
    /// </summary>
    private static string? ExtractEncryptField(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("encrypt", out var enc)
                && enc.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return enc.GetString();
            }
        }
        catch
        {
            // 解析失败由调用方处理
        }

        return null;
    }
}
