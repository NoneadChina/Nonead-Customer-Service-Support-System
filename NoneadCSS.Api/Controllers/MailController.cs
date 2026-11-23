using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 邮件控制器 - 处理邮件发送、收取等邮件操作
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class MailController : ControllerBase
{
    private readonly IEmailService _emailService;
    private readonly IEmailQueueService _emailQueueService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationService _conversationService;
    private readonly IEmailIngestionService _emailIngestionService;
    private readonly IMailboxService _mailboxService;
    private readonly IUserRoleResolver _userRoleResolver;
    private readonly Microsoft.Extensions.Logging.ILogger<MailController> _logger;
    private readonly string _attachmentBasePath;

    public MailController(IEmailService emailService, IEmailQueueService emailQueueService, IUnitOfWork unitOfWork, 
        IConversationService conversationService, IEmailIngestionService emailIngestionService,
        IMailboxService mailboxService, IUserRoleResolver userRoleResolver,
        Microsoft.Extensions.Logging.ILogger<MailController> logger,
        IWebHostEnvironment env, IOptions<AttachmentSettings> attachmentSettings)
    {
        _emailService = emailService;
        _emailQueueService = emailQueueService;
        _unitOfWork = unitOfWork;
        _conversationService = conversationService;
        _emailIngestionService = emailIngestionService;
        _mailboxService = mailboxService;
        _userRoleResolver = userRoleResolver;
        _logger = logger;
        _attachmentBasePath = Path.Combine(env.ContentRootPath, attachmentSettings.Value.UploadPath);
    }

    private async Task<bool> HasMailboxAccess(int mailboxId)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        if (await _userRoleResolver.IsSuperAdminAsync(userId))
        {
            return true;
        }
        var mailboxes = await _mailboxService.GetMailboxesByUserIdAsync(userId);
        return mailboxes.Any(m => m.Id == mailboxId);
    }

    /// <summary>
    /// 发送邮件
    /// </summary>
    /// <param name="request">发送邮件请求</param>
    /// <returns>发送结果</returns>
    /// <response code="200">发送成功</response>
    /// <response code="400">邮箱不存在或邮件发送失败</response>
    [HttpPost("send")]
    [Permission("mailboxes.manage")]
    public async Task<IActionResult> SendEmail([FromBody] SendEmailRequest request)
    {
        if (!await HasMailboxAccess(request.MailboxId))
        {
            return Forbid();
        }

        var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(request.MailboxId);

        if (mailbox == null)
        {
            return BadRequest(new { message = "邮箱不存在" });
        }

        await _emailQueueService.EnqueueEmailAsync(mailbox.Id, request.To, request.Subject, request.Body);
        return Ok(new { message = "邮件已加入发送队列" });
    }

    /// <summary>
    /// 通过会话发送回复邮件
    /// </summary>
    /// <param name="conversationId">会话 ID</param>
    /// <param name="request">回复请求</param>
    /// <returns>发送结果</returns>
    /// <response code="200">发送成功</response>
    /// <response code="404">会话不存在</response>
    [HttpPost("reply/{conversationId}")]
    [Permission("mailboxes.manage")]
    public async Task<IActionResult> ReplyByConversation(
        int conversationId,
        [FromBody] ReplyRequest request)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);

        if (conversation == null)
        {
            return NotFound(new { message = "会话不存在" });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(conversation.MailboxId);

        if (mailbox == null)
        {
            return BadRequest(new { message = "邮箱不存在" });
        }

        if (request.IsInternal)
        {
            // 内部备注：调用 AddNoteAsync，仅保存到 Thread 表，不发送外部邮件
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            await _conversationService.AddNoteAsync(
                conversationId,
                new Application.DTOs.Requests.AddNoteRequest
                {
                    Body = request.Body
                },
                userId);
            return Ok(new { message = "内部备注已保存" });
        }
        else
        {
            // 外部回复：调用 AddReplyAsync，会保存 Thread 并加入发送队列
            await _conversationService.AddReplyAsync(
                new Application.DTOs.Requests.CreateThreadRequest
                {
                    ConversationId = conversationId,
                    Body = request.Body,
                    From = mailbox.Email,
                    To = conversation.CustomerEmail,
                    Subject = conversation.Subject,
                    IsNote = false
                },
                conversation.UserId);
            return Ok(new { message = "邮件已加入发送队列" });
        }
    }

    /// <summary>
    /// 从邮箱收取邮件
    /// </summary>
    /// <param name="mailboxId">邮箱 ID</param>
    /// <returns>收取到的邮件列表</returns>
    /// <response code="200">收取成功</response>
    /// <response code="404">邮箱不存在</response>
    [HttpPost("fetch/{mailboxId}")]
    [Permission("mailboxes.manage")]
    public async Task<IActionResult> FetchEmails(int mailboxId)
    {
        if (!await HasMailboxAccess(mailboxId))
        {
            return Forbid();
        }

        var mailbox = await _unitOfWork.Mailboxes.GetByIdTrackedAsync(mailboxId);

        if (mailbox == null)
        {
            return NotFound(new { success = false, message = "邮箱不存在" });
        }

        List<MimeMessage> messages;
        ulong newUid;

        try
        {
            var (msgs, uid) = await _emailService.FetchMimeMessagesAsync(mailbox, mailbox.LastUid);
            messages = msgs;
            newUid = uid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "收取邮箱 {Mailbox} 邮件失败", mailbox.Email);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, count = 0, message = "收取邮件失败，请查看系统日志" });
        }

        _logger.LogInformation("从邮箱 {Mailbox} 获取到 {Count} 封新邮件", mailbox.Email, messages.Count);

        var newConversationCount = 0;
        var replyCount = 0;

        foreach (var message in messages)
        {
            try
            {
                var result = await _emailIngestionService.ProcessMessageAsync(message, mailbox, _attachmentBasePath);
                newConversationCount += result.NewConversationCount;
                replyCount += result.ReplyCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理邮件失败：Subject={Subject}", message.Subject);
            }
        }

        if (newUid > mailbox.LastUid)
        {
            mailbox.LastUid = newUid;
            // mailbox 已通过 GetByIdTrackedAsync 查询（tracking），直接 SaveChangesAsync 即可持久化
        }

        await _unitOfWork.SaveChangesAsync();

        return Ok(new { success = true, count = messages.Count, newConversations = newConversationCount, replies = replyCount });
    }
}
