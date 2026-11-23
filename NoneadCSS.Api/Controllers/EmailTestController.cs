using NoneadCSS.Api.Authorization;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MimeKit;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 邮件测试控制器 - 用于验证邮件线程匹配功能
/// 仅限超级管理员访问，避免测试接口暴露邮件处理内部细节。
/// 并且**仅在开发环境可用**：生产环境统一返回 404（见 OnActionExecuting），
/// 避免把 EML 解析/线程匹配这类内存模拟工具随产品发布出去（功能全景总结 #16）。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireSuperAdmin]
public class EmailTestController : ControllerBase, IActionFilter
{
    private readonly IEmailProcessingService _emailProcessingService;
    private readonly ILogger<EmailTestController> _logger;
    private readonly IWebHostEnvironment _env;

    public EmailTestController(IEmailProcessingService emailProcessingService, ILogger<EmailTestController> logger, IWebHostEnvironment env)
    {
        _emailProcessingService = emailProcessingService;
        _logger = logger;
        _env = env;
    }

    /// <summary>
    /// 生产环境直接 404：测试端点不对外暴露。
    /// </summary>
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!_env.IsDevelopment())
        {
            context.Result = NotFound();
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    /// <summary>
    /// 测试解析 EML 文件（支持 base64 编码以避免 JSON 转义问题）
    /// </summary>
    [HttpPost("parse")]
    public async Task<IActionResult> ParseEmail([FromBody] ParseEmailRequest request)
    {
        try
        {
            string emlContent = request.EmlContent;
            
            // 如果是 base64 编码，先解码
            if (request.IsBase64 == true)
            {
                emlContent = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(request.EmlContent));
            }
            
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(emlContent));
            var message = await MimeMessage.LoadAsync(stream);
            
            var result = _emailProcessingService.ParseEmail(message, 1);
            
            return Ok(new
            {
                MessageId = result.MessageId,
                InReplyTo = result.InReplyTo,
                References = result.References,
                FromEmail = result.FromEmail,
                FromName = result.FromName,
                Subject = result.Subject,
                HasAttachments = result.HasAttachments,
                IsAutoReply = result.IsAutoReply,
                IsBounce = result.IsBounce,
                Date = result.Date
            });
        }
        catch (Exception ex)
        {
            // 仅记录详细错误到日志，对外只返回通用消息，避免暴露 MimeKit 内部异常细节
            _logger.LogError(ex, "解析邮件失败");
            return BadRequest("邮件解析失败，请检查输入内容");
        }
    }

    /// <summary>
    /// 测试生成 Message-ID
    /// </summary>
    [HttpGet("generate-message-id/{threadId}")]
    public IActionResult GenerateMessageId(int threadId, [FromQuery] string domain = "example.com")
    {
        var messageId = _emailProcessingService.GenerateMessageId(threadId, domain);
        return Ok(new { MessageId = messageId });
    }

    /// <summary>
    /// 测试线程匹配逻辑（内存模拟）
    /// </summary>
    [HttpPost("simulate-thread-match")]
    public async Task<IActionResult> SimulateThreadMatch([FromBody] List<string> emlContents)
    {
        if (emlContents == null || emlContents.Count == 0)
        {
            return BadRequest("请提供邮件内容");
        }

        var storedMessageIds = new HashSet<string>();
        var conversations = new List<ConversationSim>();
        var results = new List<object>();

        foreach (var eml in emlContents)
        {
            try
            {
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(eml));
                var message = await MimeMessage.LoadAsync(stream);
                var parseResult = _emailProcessingService.ParseEmail(message, 1);

                string? matchedConversationId = null;
                string matchType = "none";

                // 1. Message-ID 直接匹配
                if (storedMessageIds.Contains(parseResult.MessageId))
                {
                    var existingConv = conversations.FirstOrDefault(c => c.MessageIds.Contains(parseResult.MessageId));
                    if (existingConv != null)
                    {
                        matchedConversationId = existingConv.Id;
                        matchType = "message-id";
                    }
                }
                // 2. In-Reply-To 匹配
                else if (!string.IsNullOrEmpty(parseResult.InReplyTo) && storedMessageIds.Contains(parseResult.InReplyTo))
                {
                    var existingConv = conversations.FirstOrDefault(c => c.MessageIds.Contains(parseResult.InReplyTo));
                    if (existingConv != null)
                    {
                        matchedConversationId = existingConv.Id;
                        matchType = "in-reply-to";
                    }
                }
                // 3. References 链匹配
                else
                {
                    foreach (var refId in parseResult.References)
                    {
                        if (storedMessageIds.Contains(refId))
                        {
                            var existingConv = conversations.FirstOrDefault(c => c.MessageIds.Contains(refId));
                            if (existingConv != null)
                            {
                                matchedConversationId = existingConv.Id;
                                matchType = "references";
                                break;
                            }
                        }
                    }
                }

                // 4. 创建新会话或附加到现有会话
                if (matchedConversationId != null)
                {
                    var conv = conversations.First(c => c.Id == matchedConversationId);
                    conv.MessageIds.Add(parseResult.MessageId);
                    storedMessageIds.Add(parseResult.MessageId);
                    
                    results.Add(new
                    {
                        MessageId = parseResult.MessageId,
                        From = parseResult.FromEmail,
                        Subject = parseResult.Subject,
                        Action = "attached",
                        ConversationId = matchedConversationId,
                        MatchType = matchType
                    });
                }
                else
                {
                    var newConv = new ConversationSim
                    {
                        Id = Guid.NewGuid().ToString().Substring(0, 8),
                        Subject = parseResult.Subject
                    };
                    newConv.MessageIds.Add(parseResult.MessageId);
                    conversations.Add(newConv);
                    storedMessageIds.Add(parseResult.MessageId);
                    
                    results.Add(new
                    {
                        MessageId = parseResult.MessageId,
                        From = parseResult.FromEmail,
                        Subject = parseResult.Subject,
                        Action = "created",
                        ConversationId = newConv.Id,
                        MatchType = "new"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理邮件失败");
                results.Add(new { Error = "邮件处理失败，请检查日志获取详细信息" });
            }
        }

        return Ok(new
        {
            TotalEmails = emlContents.Count,
            TotalConversations = conversations.Count,
            Results = results
        });
    }

    private class ConversationSim
    {
        public string Id { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public List<string> MessageIds { get; set; } = [];
    }
}