using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Api.Filters;
using NoneadCSS.Api.Authorization;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 微信服务号接入控制器 - 包含匿名回调端点（微信服务器调用）和鉴权管理端点（后台配置）。
/// 回调端点（GET 签名校验 + POST 消息推送）使用 [AllowAnonymous] 并通过 CSRF 豁免放行。
/// 管理端点需要登录 + wechat.manage 权限。
/// </summary>
[Route("api/[controller]")]
public class WeChatController : BaseController
{
    private readonly IWeChatService _weChatService;
    private readonly IWeChatIngestionService _ingestionService;
    private readonly IWeChatSettingsProvider _settingsProvider;
    private readonly ILogger<WeChatController> _logger;
    private readonly IWebHostEnvironment _env;

    public WeChatController(
        IWeChatService weChatService,
        IWeChatIngestionService ingestionService,
        IWeChatSettingsProvider settingsProvider,
        ILogger<WeChatController> logger,
        IWebHostEnvironment env)
    {
        _weChatService = weChatService;
        _ingestionService = ingestionService;
        _settingsProvider = settingsProvider;
        _logger = logger;
        _env = env;
    }

    /// <summary>
    /// 微信服务器签名校验（GET）。
    /// 微信公众平台后台配置 URL 后，会发送 GET 请求校验签名。
    /// 校验通过返回 echostr 随机字符串。
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult VerifySignature(
        [FromQuery] string signature,
        [FromQuery] string timestamp,
        [FromQuery] string nonce,
        [FromQuery] string echostr)
    {
        var opt = _settingsProvider.Current;

        if (!opt.Enabled)
        {
            _logger.LogWarning("微信接入未启用，拒绝签名校验");
            return Unauthorized();
        }

        if (!WeChatMessageParser.ValidateSignature(opt.Token ?? string.Empty, timestamp, nonce, signature))
        {
            _logger.LogWarning("微信签名校验失败：signature={Signature}", signature);
            return Unauthorized();
        }

        _logger.LogInformation("微信签名校验成功");
        return Content(echostr ?? string.Empty, "text/plain");
    }

    /// <summary>
    /// 接收微信推送的消息（POST）。
    /// 微信服务器以 XML 格式推送用户消息和事件。
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveMessage(
        [FromQuery] string? signature,
        [FromQuery] string? timestamp,
        [FromQuery] string? nonce,
        [FromQuery] string? msg_signature,
        [FromQuery] string? encrypt_type)
    {
        var opt = _settingsProvider.Current;

        if (!opt.Enabled)
        {
            _logger.LogWarning("微信接入未启用，拒绝消息推送");
            return Ok("success");
        }

        // URL 参数签名校验：始终用 signature（token+timestamp+nonce）
        // msg_signature（token+timestamp+nonce+encrypt）用于加密消息体的校验，见下方读取 body 之后
        _logger.LogDebug("微信消息推送参数：signature={Signature}, msg_signature={MsgSignature}, timestamp={Timestamp}, nonce={Nonce}, encrypt_type={EncryptType}",
            signature, msg_signature, timestamp, nonce, encrypt_type);
        var effectiveSignature = signature;
        if (string.IsNullOrEmpty(effectiveSignature) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(nonce))
        {
            _logger.LogWarning("微信消息推送缺少签名参数，拒绝请求");
            return Unauthorized();
        }

        if (!WeChatMessageParser.ValidateSignature(opt.Token ?? string.Empty, timestamp, nonce, effectiveSignature))
        {
            _logger.LogWarning("微信消息推送签名校验失败，传入签名={Received}", effectiveSignature);
            return Unauthorized();
        }

        // 读取原始 XML body
        using var reader = new StreamReader(Request.Body);
        var xml = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(xml))
        {
            return Ok("success");
        }

        // 加密模式报文必须先校验 msg_signature（token+timestamp+nonce+Encrypt 的 SHA1），
        // 否则密文可被替换/伪造后才进入解密流程。
        // - safe：微信一定推送密文，出现明文报文视为异常，直接拒绝
        // - compatible：明文与密文都可能推送，仅对带 Encrypt 节点的报文强制校验 msg_signature
        // - plain：正常情况下只推送明文；若仍收到密文（后台被改为兼容/安全模式）同样强制校验
        var encryptValue = WeChatMessageParser.ExtractEncryptValue(xml);
        var isSafeMode = string.Equals(opt.EncryptMode, "safe", StringComparison.OrdinalIgnoreCase);
        if (encryptValue == null && isSafeMode)
        {
            _logger.LogWarning("微信安全模式下收到不含 Encrypt 节点的明文报文，拒绝处理：Xml长度={XmlLen}", xml.Length);
            return Unauthorized();
        }

        if (encryptValue != null)
        {
            if (string.IsNullOrWhiteSpace(msg_signature)
                || !WeChatMessageParser.ValidateMessageSignature(opt.Token ?? string.Empty, timestamp!, nonce!, encryptValue, msg_signature))
            {
                _logger.LogWarning("微信消息体签名（msg_signature）校验失败，拒绝处理：MsgSignature={MsgSignature}, Encrypt长度={EncryptLen}",
                    string.IsNullOrEmpty(msg_signature) ? "(empty)" : "(mismatch)", encryptValue.Length);
                return Unauthorized();
            }
        }

        // 记录原始 XML 长度和 Encrypt 节点长度，便于排查图片消息解密失败问题
        var encryptLen = encryptValue?.Length ?? 0;
        _logger.LogInformation("微信消息接收：XML长度={XmlLen}, Encrypt长度={EncryptLen}", xml.Length, encryptLen);
        // PII 脱敏：仅记录长度与哈希摘要，避免日志泄露客户 OpenId、消息内容等
        _logger.LogDebug("微信消息原始 XML：XmlLength={XmlLen}, Hash={Hash}",
            xml.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(xml))).Substring(0, 16));

        // 解析消息（安全模式需解密并校验 AppId）
        var message = WeChatMessageParser.Parse(xml, opt.EncodingAESKey, opt.AppId, out var parseError);
        if (message == null)
        {
            _logger.LogWarning("微信消息解析失败：{Error}（AESKey长度={KeyLen}, AppId={AppId}, Encrypt长度={EncryptLen}）",
                parseError ?? "未知原因", opt.EncodingAESKey?.Length ?? 0, opt.AppId, encryptLen);
            return Ok("success");
        }

        // 调试日志：记录解析后的关键字段，FromUserName 已脱敏，Content 仅记录长度避免泄露 PII
        var maskedFromUserName = string.IsNullOrEmpty(message.FromUserName) ? string.Empty
            : (message.FromUserName.Length <= 6 ? message.FromUserName : message.FromUserName.Substring(0, 3) + "***" + message.FromUserName.Substring(message.FromUserName.Length - 3));
        _logger.LogDebug("微信消息解析结果：MsgType={MsgType}, FromUserName={FromUserName}(masked), ToUserName={ToUserName}, MsgId={MsgId}, ContentLength={ContentLen}",
            message.MsgType, maskedFromUserName, message.ToUserName, message.MsgId, message.Content.Length);

        try
        {
            var result = await _ingestionService.ProcessMessageAsync(message);

            // 被动回复：优先使用服务端计算的回复内容（subscribe 事件欢迎语），
            // 其次为首条消息自动回复
            var replyContent = result.PassiveReplyContent;
            if (string.IsNullOrEmpty(replyContent)
                && result.NewConversationCount > 0
                && opt.AutoReplyEnabled
                && !string.IsNullOrEmpty(opt.AutoReplyMessage))
            {
                replyContent = opt.AutoReplyMessage;
            }

            if (!string.IsNullOrEmpty(replyContent))
            {
                var replyXml = WeChatMessageParser.BuildTextReply(
                    message.FromUserName,
                    message.ToUserName,
                    replyContent);
                return Content(replyXml, "application/xml");
            }

            // 无需被动回复时返回 "success" 让微信服务器不再重试
            return Ok("success");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "微信消息处理异常：MsgId={MsgId}, OpenId={OpenId}", message.MsgId, message.FromUserName);
            // 即使处理失败也返回 success，避免微信服务器反复重试
            return Ok("success");
        }
    }

    // ====== 以下为管理端点，需要鉴权 ======

    /// <summary>
    /// 获取微信配置（敏感字段脱敏）。
    /// </summary>
    [HttpGet("settings")]
    [Authorize]
    [RequireModule("wechat")]
    [Permission("wechat.view")]
    public async Task<ActionResult> GetSettings()
    {
        var settings = await _weChatService.GetSettingsAsync();
        return Ok(settings);
    }

    /// <summary>
    /// 更新微信配置。仅超级管理员可操作。
    /// </summary>
    [HttpPut("settings")]
    [Authorize]
    [RequireModule("wechat")]
    [RequireSuperAdmin]
    public async Task<ActionResult> UpdateSettings([FromBody] UpdateWeChatSettingsRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var settings = await _weChatService.UpdateSettingsAsync(request);
        return Ok(settings);
    }

    /// <summary>
    /// 获取微信连接状态。
    /// </summary>
    [HttpGet("status")]
    [Authorize]
    [RequireModule("wechat")]
    [Permission("wechat.view")]
    public async Task<ActionResult> GetStatus()
    {
        var status = await _weChatService.GetStatusAsync();
        return Ok(status);
    }

    /// <summary>
    /// 通过微信客服消息接口回复客户。
    /// </summary>
    [HttpPost("reply")]
    [Authorize]
    [RequireModule("wechat")]
    [Permission("wechat.manage")]
    public async Task<ActionResult> Reply([FromBody] WeChatReplyRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = CurrentUserId ?? 0;
        if (userId == 0)
        {
            return Unauthorized();
        }

        var result = await _weChatService.ReplyAsync(request, userId);
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
    /// 发送微信模板消息到指定会话的客户。
    /// </summary>
    [HttpPost("template/send")]
    [Authorize]
    [RequireModule("wechat")]
    [Permission("wechat.manage")]
    public async Task<ActionResult> SendTemplateMessage([FromBody] WeChatTemplateRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = CurrentUserId ?? 0;
        if (userId == 0)
        {
            return Unauthorized();
        }

        var result = await _weChatService.SendTemplateMessageAsync(request, userId);
        if (!result.Sent)
        {
            return BadRequest(new { message = result.ErrorMessage });
        }

        return Ok(result);
    }

    /// <summary>
    /// 发送微信订阅通知到指定会话的客户（新版，替代旧版模板消息）。
    /// </summary>
    [HttpPost("subscribe/send")]
    [Authorize]
    [RequireModule("wechat")]
    [Permission("wechat.manage")]
    public async Task<ActionResult> SendSubscribeMessage([FromBody] WeChatSubscribeRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = CurrentUserId ?? 0;
        if (userId == 0)
        {
            return Unauthorized();
        }

        var result = await _weChatService.SendSubscribeMessageAsync(request, userId);
        if (!result.Sent)
        {
            return BadRequest(new { message = result.ErrorMessage });
        }

        return Ok(result);
    }

    /// <summary>
    /// 创建微信自定义菜单。
    /// </summary>
    [HttpPost("menu")]
    [Authorize]
    [RequireModule("wechat")]
    [RequireSuperAdmin]
    public async Task<ActionResult> CreateMenu([FromBody] WeChatMenuRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _weChatService.CreateMenuAsync(request);
        if (!result.Success)
        {
            return BadRequest(new { message = result.ErrorMessage });
        }

        return Ok(result);
    }

    /// <summary>
    /// 查询当前微信自定义菜单。
    /// </summary>
    [HttpGet("menu")]
    [Authorize]
    [RequireModule("wechat")]
    [Permission("wechat.view")]
    public async Task<ActionResult> GetMenu()
    {
        var menu = await _weChatService.GetMenuAsync();
        if (menu == null)
        {
            return Ok(new { menu = (string?)null });
        }

        return Ok(new { menu });
    }

    /// <summary>
    /// 删除微信自定义菜单。
    /// </summary>
    [HttpDelete("menu")]
    [Authorize]
    [RequireModule("wechat")]
    [RequireSuperAdmin]
    public async Task<ActionResult> DeleteMenu()
    {
        var result = await _weChatService.DeleteMenuAsync();
        if (!result.Success)
        {
            return BadRequest(new { message = result.ErrorMessage });
        }

        return Ok(result);
    }

    /// <summary>
    /// 【测试】注入一条模拟的微信位置消息，用于本地验证前端点击跳转效果。
    /// 仅限超级管理员访问，绕过签名校验直接调用 ingestion 服务。
    /// </summary>
    [HttpPost("test/location")]
    [Authorize]
    [RequireModule("wechat")]
    [RequireSuperAdmin]
    public async Task<ActionResult> InjectMockLocationMessage([FromBody] MockLocationRequest? request)
    {
        // S-M3 修复：测试端点绕过微信签名校验注入模拟消息，生产环境完全禁用。
        // 仅开发环境保留以便本地验证前端点击跳转效果。
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }

        // 默认坐标：北京天安门（116.397428, 39.90923），可由请求体覆盖
        var lat = request?.Latitude ?? "39.90923";
        var lng = request?.Longitude ?? "116.397428";
        var openId = string.IsNullOrWhiteSpace(request?.OpenId) ? "mock_openid_test_location" : request!.OpenId;
        var label = string.IsNullOrWhiteSpace(request?.Label) ? "客户位置" : request!.Label;

        var message = new WeChatMessageDto
        {
            ToUserName = "gh_test_service_account",
            FromUserName = openId,
            CreateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            MsgType = "location",
            MsgId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
            Latitude = lat,
            Longitude = lng,
            Precision = "15"
        };

        _logger.LogInformation("注入模拟微信位置消息：OpenId={OpenId}, Lat={Lat}, Lng={Lng}, Label={Label}",
            openId, lat, lng, label);

        try
        {
            var result = await _ingestionService.ProcessMessageAsync(message);
            return Ok(new
            {
                message = "位置消息已注入，请刷新会话列表查看",
                result.ConversationId,
                result.NewConversationCount,
                result.ReplyCount,
                result.IsDuplicate,
                lat,
                lng,
                label
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注入模拟微信位置消息失败");
            return StatusCode(500, new { message = ex.Message });
        }
    }
}
