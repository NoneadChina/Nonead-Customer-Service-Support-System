using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Templates;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Application.Services;

public interface IConversationThreadService
{
    Task<ThreadResponse?> AddReplyAsync(CreateThreadRequest request, int? userId = null);
    Task<IEnumerable<ThreadResponse>> GetThreadsByConversationIdAsync(int conversationId);
    Task<ThreadResponse?> AddNoteAsync(int conversationId, AddNoteRequest request, int userId);
    Task<IEnumerable<ThreadResponse>> GetChatModeThreadsAsync(int conversationId);

    /// <summary>
    /// 进入一个抑制作用域：在此作用域内，AddReplyAsync / AddNoteAsync 将：
    /// 1. 跳过自身的事务管理（复用外层事务，由调用方负责提交/回滚）；
    /// 2. 跳过嵌套的工作流触发（避免 ClearChanges 污染外层状态或无限递归）。
    /// 调用方务必在 Dispose 时退出作用域。
    /// </summary>
    IDisposable BeginSuppressedScope();
}

public class ConversationThreadService : IConversationThreadService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailQueueService _emailQueueService;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IMailTemplateService _mailTemplateService;
    private readonly IEmailSignatureService _emailSignatureService;
    private readonly IWorkflowService _workflowService;
    private readonly IWeChatClient _weChatClient;
    private readonly IDingDingClient _dingDingClient;
    private readonly IDingDingSettingsProvider _dingDingSettingsProvider;
    private readonly IConfiguration _configuration;
    private readonly IFrontendUrlResolver _frontendUrlResolver;
    private readonly ILogger<ConversationThreadService> _logger;
    private readonly ITemplateRenderer _renderer;
    private readonly BrandOptions _brand;
    private readonly IHostEnvironment _env;
    private readonly IWebhookService? _webhookService;
    private readonly IDomainEventDispatcher? _domainEventDispatcher;
    private readonly string _attachmentBasePath;

    /// <summary>
    /// 抑制标志：值 > 0 表示当前处于抑制作用域内（允许嵌套多次进入，引用计数）。
    /// 使用 AsyncLocal 以确保跨异步调用链的独立上下文。
    /// </summary>
    private static readonly AsyncLocal<int> _suppressedDepth = new();

    private static bool IsSuppressed => _suppressedDepth.Value > 0;

    public ConversationThreadService(
        IUnitOfWork unitOfWork,
        IEmailQueueService emailQueueService,
        INotificationPublisher notificationPublisher,
        IMailTemplateService mailTemplateService,
        IEmailSignatureService emailSignatureService,
        IWorkflowService workflowService,
        IWeChatClient weChatClient,
        IDingDingClient dingDingClient,
        IDingDingSettingsProvider dingDingSettingsProvider,
        IConfiguration configuration,
        IFrontendUrlResolver frontendUrlResolver,
        ILogger<ConversationThreadService> logger,
        ITemplateRenderer renderer,
        IOptions<BrandOptions> brand,
        IHostEnvironment env,
        IWebhookService? webhookService = null,
        IDomainEventDispatcher? domainEventDispatcher = null)
    {
        _unitOfWork = unitOfWork;
        _emailQueueService = emailQueueService;
        _notificationPublisher = notificationPublisher;
        _mailTemplateService = mailTemplateService;
        _emailSignatureService = emailSignatureService;
        _workflowService = workflowService;
        _weChatClient = weChatClient;
        _dingDingClient = dingDingClient;
        _dingDingSettingsProvider = dingDingSettingsProvider;
        _configuration = configuration;
        _frontendUrlResolver = frontendUrlResolver;
        // 附件根目录：统一解析（绝对路径原样、相对/缺失锚定 ContentRoot，与上传侧一致，T000108/T000112）
        _attachmentBasePath = AttachmentPathResolver.ResolveBasePath(
            configuration["Attachment:UploadPath"], env.ContentRootPath);
        _logger = logger;
        _renderer = renderer;
        _brand = brand.Value;
        _env = env;
        _webhookService = webhookService;
        _domainEventDispatcher = domainEventDispatcher;
    }

    /// <summary>
    /// 发布强类型领域事件。未注入分发器（如部分单元测试构造）时直接跳过；
    /// 分发失败由 DomainEventDispatcher 内部捕获并记录日志，不影响主流程。
    /// </summary>
    /// <typeparam name="TEvent">事件类型。</typeparam>
    /// <param name="event">事件实例。</param>
    private Task DispatchDomainEventAsync<TEvent>(TEvent @event) where TEvent : class
        => _domainEventDispatcher is null ? Task.CompletedTask : _domainEventDispatcher.DispatchAsync(@event);

    public async Task<ThreadResponse?> AddReplyAsync(CreateThreadRequest request, int? userId = null)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(request.ConversationId);
        if (conversation == null)
        {
            return null;
        }

        var threadType = request.IsNote ? ThreadType.Note : ThreadType.Message;
        ConversationThread thread;
        var suppressed = IsSuppressed;
        var wasClosed = false;

        async Task BuildAndPersistThreadAsync()
        {
            thread = new ConversationThread
            {
                ConversationId = request.ConversationId,
                UserId = userId,
                CustomerId = conversation.CustomerId,
                Type = threadType,
                Body = HtmlSanitizerHelper.Sanitize(request.Body),
                Subject = conversation.Subject,
                From = request.From,
                To = request.To ?? conversation.CustomerEmail,
                Cc = request.Cc ?? string.Empty,
                Bcc = request.Bcc ?? string.Empty,
                IsInternal = request.IsNote,
                CreatedAt = DateTime.UtcNow
            };

            if (request.AttachmentIds != null && request.AttachmentIds.Any())
            {
                var attachments = await _unitOfWork.Attachments.GetByIdsTrackedAsync(request.AttachmentIds);
                foreach (var attachment in attachments)
                {
                    thread.Attachments.Add(attachment);
                }
            }

            conversation.AddThread(thread);

            if (conversation.Status == ConversationStatus.Closed)
            {
                conversation.SetStatus(ConversationStatus.Active);
                wasClosed = true;
            }

            conversation.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();

            if (thread.Attachments.Any())
            {
                // 与 DingDingService.ReplyAsync 保持一致：关联附件时同步标记 HasAttachments，
                // 否则线程/会话的附件标志位保持 false，影响 UI 展示与筛选（T000112 连带发现）。
                thread.HasAttachments = true;
                if (!conversation.HasAttachments)
                {
                    conversation.HasAttachments = true;
                }
                foreach (var attachment in thread.Attachments)
                {
                    attachment.ThreadId = thread.Id;
                }
                await _unitOfWork.SaveChangesAsync();
            }
        }

        if (suppressed)
        {
            // 外层（宏/工作流）已开启事务，这里不重复开启，避免嵌套事务或 Savepoints 异常。
            thread = null!;
            await BuildAndPersistThreadAsync();
        }
        else
        {
            // M-8 事务嵌套 + 多层 catch 逻辑复杂度高。当前实现功能正确（suppressed 走外层事务，
            // 否则自建事务 + try/commit/catch/rollback/throw），结构性重构需单独排期以降低回归风险。
            using (var transaction = await _unitOfWork.BeginTransactionAsync())
            {
                try
                {
                    thread = null!;
                    await BuildAndPersistThreadAsync();
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.SafeRollbackAsync();
                    throw;
                }
            }
        }

        if (!request.IsNote)
        {
            _logger.LogInformation("客服回复渠道分发：ConversationId={ConversationId}, SourceType={SourceType}, ThreadId={ThreadId}, HasAttachments={HasAttachments}, AttachmentIds={AttachmentIds}",
                conversation.Id, conversation.SourceType, thread.Id, thread.HasAttachments,
                request.AttachmentIds != null ? string.Join(",", request.AttachmentIds) : "(null)");

            if (conversation.SourceType == SourceType.WeChat)
            {
                // 微信会话：通过客服消息接口发送，不走邮件入队
                await SendWeChatReplyAsync(conversation, thread, request.Body);
            }
            else if (conversation.SourceType == SourceType.DingTalk)
            {
                // 钉钉会话：通过 sessionWebhook 或批量发送接口回复，不走邮件入队
                _logger.LogInformation("进入钉钉回复分支：ConversationId={ConversationId}, ThreadId={ThreadId}", conversation.Id, thread.Id);
                await SendDingDingReplyAsync(conversation, thread, request.Body);
            }
            else
            {
                // 邮件及其他渠道：走邮件入队
                try
                {
                    var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(conversation.MailboxId);
                    if (mailbox != null)
                    {
                        var replySubject = conversation.Subject.StartsWith("Re: ", StringComparison.OrdinalIgnoreCase)
                            ? conversation.Subject
                            : $"Re: {conversation.Subject}";

                        if (!replySubject.Contains($"[#{conversation.Number}]"))
                        {
                            replySubject = $"[#{conversation.Number}] {replySubject}";
                        }

                        var emailBody = await RenderReplyEmailBodyAsync(conversation, request.Body, userId, mailbox);

                        emailBody += $"\n<div style=\"display:none !important;visibility:hidden;mso-hide:all;height:0;max-height:0;overflow:hidden;font-size:0;line-height:0;\">{{#FS:{thread.Id}#}}</div>";

                        var attachmentIds = thread.Attachments.Any() ? thread.Attachments.Select(a => a.Id).ToList() : null;

                        string? inReplyTo = null;
                        string? references = null;
                        var lastCustomerThread = await _unitOfWork.Threads.GetLatestCustomerThreadAsync(conversation.Id);
                        if (lastCustomerThread != null)
                        {
                            inReplyTo = lastCustomerThread.MessageId;
                            // RFC 5322 §3.6.4：References 必须包含完整祖先链
                            // = 父邮件的 References + 父邮件的 Message-Id
                            var parentReferences = EmailHeaderHelper.ExtractReferences(lastCustomerThread.Headers);
                            references = EmailHeaderHelper.BuildReferences(parentReferences, lastCustomerThread.MessageId);
                        }

                        await _emailQueueService.EnqueueEmailAsync(mailbox.Id, conversation.CustomerEmail, replySubject, emailBody,
                            attachmentIds, request.Cc, request.Bcc, thread.Id, inReplyTo, references,
                            userId: userId, customerId: conversation.CustomerId);
                    }
                }
                catch (Exception ex)
                {
                    // 邮件入队失败：线程已提交到数据库，无法回滚。
                    // 标记 thread.SendStatus = Failed 以便后续重试或 UI 提示，
                    // 并记录完整上下文（ConversationId/ThreadId/MailboxId）便于排查。
                    _logger.LogError(ex, "SendEmail 入队失败 in AddReplyAsync: ConversationId={ConversationId}, ThreadId={ThreadId}, MailboxId={MailboxId}",
                        request.ConversationId, thread.Id, conversation.MailboxId);
                    try
                    {
                        thread.SetSendStatus(SendStatus.Failed);
                        await _unitOfWork.SaveChangesAsync();
                    }
                    catch (Exception innerEx)
                    {
                        // 状态更新也失败：记录完整上下文（ThreadId/ConversationId）以便运维排查，
                        // 不 rethrow 以避免阻塞回复操作主流程。
                        _logger.LogError(innerEx, "邮件入队失败且状态更新也失败。ThreadId={ThreadId}, ConversationId={ConversationId}",
                            thread.Id, thread.ConversationId);
                    }
                }
            }
        }

        // 显式加载 User 导航属性：ToResponse 映射需要 thread.User 生成完整响应，
        // 否则前端因 user 为 null 回退到客户姓名显示。
        if (thread.UserId.HasValue)
        {
            thread.User = await _unitOfWork.Users.GetByIdAsync(thread.UserId.Value);
        }

        var threadResponse = thread.ToResponse();
        await _notificationPublisher.NotifyMailboxAsync(conversation.MailboxId.ToString(), request.IsNote ? "note_created" : "reply_created", threadResponse);

        // 发布强类型领域事件：由 UserRepliedListener 给会话关注者发站内通知（「给关注者发通知」此前没有等价实现）。
        // 备注（IsNote）不算回复；审计 action="reply" 由 AuditActionFilter 写入、SignalR 由上面的 NotifyMailboxAsync 推送，
        // 监听器已精简为只做关注者通知。必须在回复保存之后发布：监听器用自己的 Scope 与 DbContext。
        var repliedByUser = thread.User;
        if (!request.IsNote && thread.UserId.HasValue && repliedByUser != null)
        {
            await DispatchDomainEventAsync(new UserRepliedEvent
            {
                Conversation = conversation,
                Thread = thread,
                User = repliedByUser
            });
        }

        // 抑制作用域（工作流/宏嵌套调用）下不触发嵌套工作流：
        // 1. 避免 ClearChanges 把外层工作流已追踪的会话/宏实体状态清空；
        // 2. 避免同一个事件触发无限递归的工作流（例如宏执行 add_note → 触发 note_added 工作流 → 再次执行宏）。
        if (!suppressed)
        {
            if (request.IsNote)
            {
                try
                {
                    await _workflowService.ExecuteForConversationAsync(request.ConversationId, WorkflowConst.TriggerNoteAdded);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "触发工作流失败（内部备注）");
                }
            }
            else
            {
                try
                {
                    await _workflowService.ExecuteForConversationAsync(request.ConversationId, WorkflowConst.TriggerAgentReplied);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "触发工作流失败（客服回复）");
                }

                // 会话从关闭状态因回复自动重开时，额外触发会话重新打开工作流
                // 参考 ConversationStatusService.UpdateStatusAsync 中 wasClosed 的处理
                if (wasClosed)
                {
                    try
                    {
                        await _workflowService.ExecuteForConversationAsync(request.ConversationId, WorkflowConst.TriggerConversationReopened);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "触发工作流失败（会话重新打开）");
                    }
                }
            }
        }

        // 触发 Webhook 通知（失败仅记录日志，不阻塞主流程）
        if (_webhookService != null)
        {
            try
            {
                await _webhookService.TriggerAsync("thread_created", conversation.MailboxId, threadResponse, "ConversationThread", thread.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发 Webhook 失败：Event={Event}, Entity={EntityType}:{EntityId}", "thread_created", "ConversationThread", thread.Id);
            }
        }

        return threadResponse;
    }

    /// <summary>
    /// 通过微信客服消息接口发送回复。
    /// 仅在客户最近互动 48 小时内有效，超出窗口微信 API 会返回 errcode=45015。
    /// </summary>
    private async Task SendWeChatReplyAsync(Conversation conversation, ConversationThread thread, string htmlBody)
    {
        try
        {
            // 获取客户 OpenId：优先从 Customer.ChannelId，回退到最近客户线程的 From
            var openId = conversation.Customer?.ChannelId;
            if (string.IsNullOrEmpty(openId) && conversation.CustomerId.HasValue)
            {
                var customer = await _unitOfWork.Customers.GetByIdAsync(conversation.CustomerId.Value);
                openId = customer?.ChannelId;
            }

            if (string.IsNullOrEmpty(openId))
            {
                var lastCustomerThread = await _unitOfWork.Threads.GetQueryable()
                    .Where(t => t.ConversationId == conversation.Id
                        && t.SourceType == SourceType.WeChat
                        && t.Type == ThreadType.Customer
                        && t.State != ThreadState.Deleted)
                    .OrderByDescending(t => t.CreatedAt)
                    .FirstOrDefaultAsync();
                openId = lastCustomerThread?.From;
            }

            if (string.IsNullOrEmpty(openId))
            {
                _logger.LogError("微信回复失败：无法获取客户 OpenId，ConversationId={ConversationId}, ThreadId={ThreadId}",
                    conversation.Id, thread.Id);
                thread.SetSendStatus(SendStatus.Failed);
                await _unitOfWork.SaveChangesAsync();
                return;
            }

            // 微信客服消息只支持纯文本，将 HTML 正文转为纯文本
            var plainText = StripHtml(htmlBody);
            if (string.IsNullOrWhiteSpace(plainText))
            {
                _logger.LogWarning("微信回复跳过：转换后正文为空，ThreadId={ThreadId}", thread.Id);
                return;
            }

            var sent = await _weChatClient.SendTextMessageAsync(openId, plainText);
            if (!sent)
            {
                _logger.LogError("微信客服消息发送失败：ConversationId={ConversationId}, ThreadId={ThreadId}, OpenId={OpenId}",
                    conversation.Id, thread.Id, openId);
                thread.SetSendStatus(SendStatus.Failed);
                await _unitOfWork.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "微信回复异常：ConversationId={ConversationId}, ThreadId={ThreadId}",
                conversation.Id, thread.Id);
            try
            {
                thread.SetSendStatus(SendStatus.Failed);
                await _unitOfWork.SaveChangesAsync();
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "标记 Thread SendStatus=Failed 失败: ThreadId={ThreadId}", thread.Id);
            }
        }
    }

    /// <summary>
    /// 通过钉钉 sessionWebhook 或批量发送接口回复客户消息。
    /// 仅发送消息到钉钉，不创建会话线程（线程已由 AddReplyAsync 创建）。
    /// sessionWebhook 存储在最近一条客户线程的 MessageId 字段中（以 "sw:" 前缀标记）。
    /// 支持发送文本和附件（图片优先用 sessionWebhook base64 直传；其他附件或 sessionWebhook 失效时走 media_upload + batchSend）。
    /// </summary>
    private async Task SendDingDingReplyAsync(Conversation conversation, ConversationThread thread, string htmlBody)
    {
        try
        {
            var settings = _dingDingSettingsProvider.Current;
            if (!settings.Enabled || !settings.IsConfigured)
            {
                _logger.LogError("钉钉回复失败：未启用或未配置，ConversationId={ConversationId}, ThreadId={ThreadId}",
                    conversation.Id, thread.Id);
                thread.SetSendStatus(SendStatus.Failed);
                await _unitOfWork.SaveChangesAsync();
                return;
            }

            // 查找最近一条客户线程，提取 sessionWebhook（"sw:" 前缀）和客户 userId
            var lastCustomerThread = await _unitOfWork.Threads.GetQueryable()
                .Where(t => t.ConversationId == conversation.Id
                    && t.SourceType == SourceType.DingTalk
                    && t.Type == ThreadType.Customer
                    && t.State != ThreadState.Deleted)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            string? sessionWebhook = null;
            if (lastCustomerThread != null)
            {
                // T000113：优先 Headers（sessionWebhook: 前缀，新格式），兜底 MessageId "sw:"（旧数据兼容）
                const string headerPrefix = "sessionWebhook:";
                if (!string.IsNullOrWhiteSpace(lastCustomerThread.Headers))
                {
                    var idx = lastCustomerThread.Headers.IndexOf(headerPrefix, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        var value = lastCustomerThread.Headers.Substring(idx + headerPrefix.Length).Trim();
                        if (value.Length > 0)
                        {
                            sessionWebhook = value;
                        }
                    }
                }
                if (sessionWebhook == null
                    && !string.IsNullOrEmpty(lastCustomerThread.MessageId)
                    && lastCustomerThread.MessageId.StartsWith("sw:"))
                {
                    sessionWebhook = lastCustomerThread.MessageId[3..];
                }
            }

            var customerUserId = lastCustomerThread?.From ?? string.Empty;

            // 钉钉机器人消息只支持纯文本
            var plainText = StripHtml(htmlBody);

            // 收集该线程的附件（图片/文件）：显式按 ThreadId 查询，确保不依赖导航属性而漏发
            var attachments = (await _unitOfWork.Attachments.GetByThreadIdAsync(thread.Id)).ToList();
            var textSent = string.IsNullOrWhiteSpace(plainText); // 如果正文为空，标记为已发（不发空文本）
            bool allAttachmentsDelivered = attachments.Count == 0;

            // 1. 发送正文（文本）
            if (!textSent)
            {
                bool textOk = false;
                if (!string.IsNullOrEmpty(sessionWebhook))
                {
                    textOk = await _dingDingClient.SendBySessionWebhookAsync(
                        sessionWebhook, "text", new { content = plainText });
                }
                if (!textOk && !string.IsNullOrEmpty(customerUserId) && !string.IsNullOrEmpty(settings.RobotCode))
                {
                    var contentJson = System.Text.Json.JsonSerializer.Serialize(new { content = plainText });
                    var processKey = await _dingDingClient.BatchSendOtoMessageAsync(
                        settings.RobotCode!, new[] { customerUserId! }, "sampleText", contentJson);
                    textOk = !string.IsNullOrEmpty(processKey);
                }
                textSent = textOk;
                if (textOk)
                {
                    _logger.LogInformation("钉钉文本回复发送成功：ConversationId={ConversationId}, ThreadId={ThreadId}",
                        conversation.Id, thread.Id);
                }
                else
                {
                    _logger.LogWarning("钉钉文本回复发送失败：ConversationId={ConversationId}, ThreadId={ThreadId}, HasSw={HasSw}, CustomerUserId={UserId}",
                        conversation.Id, thread.Id, !string.IsNullOrEmpty(sessionWebhook), customerUserId);
                }
            }

            // 2. 发送附件（图片/文件）
            if (attachments.Count > 0)
            {
                allAttachmentsDelivered = true;
                var sentCount = 0;

                foreach (var attachment in attachments)
                {
                    var delivered = await SendDingDingAttachmentAsync(
                        attachment, sessionWebhook, customerUserId, settings.RobotCode);
                    if (delivered)
                        sentCount++;
                    else
                        allAttachmentsDelivered = false;
                }

                _logger.LogInformation("钉钉附件发送结果：ConversationId={ConversationId}, ThreadId={ThreadId}, Sent={Sent}/{Total}",
                    conversation.Id, thread.Id, sentCount, attachments.Count);
            }

            // 只要文本或附件至少一个成功就算发送成功；全部失败才标记 Failed
            if (textSent || allAttachmentsDelivered)
            {
                // 不需要显式 SetSendStatus=Sent，默认 Pending/Sent 状态由下游处理
            }
            else
            {
                _logger.LogError("钉钉回复整体发送失败：ConversationId={ConversationId}, ThreadId={ThreadId}",
                    conversation.Id, thread.Id);
                thread.SetSendStatus(SendStatus.Failed);
                await _unitOfWork.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉回复异常：ConversationId={ConversationId}, ThreadId={ThreadId}",
                conversation.Id, thread.Id);
            try
            {
                thread.SetSendStatus(SendStatus.Failed);
                await _unitOfWork.SaveChangesAsync();
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "标记 Thread SendStatus=Failed 失败: ThreadId={ThreadId}", thread.Id);
            }
        }
    }

    /// <summary>
    /// 发送单个钉钉附件。
    /// 图片优先：sessionWebhook → base64+md5 发送 image 消息；
    /// 失败或非图片 → 上传媒体获取 media_id，通过 BatchSendImageMessageAsync / BatchSendFileMessageAsync 发送。
    /// </summary>
    private async Task<bool> SendDingDingAttachmentAsync(
        Attachment attachment,
        string? sessionWebhook,
        string customerUserId,
        string? robotCode)
    {
        if (attachment == null) return false;

        // 读取本地文件
        var filePath = string.IsNullOrEmpty(attachment.FileDir)
            ? Path.Combine(_attachmentBasePath, attachment.StoredFileName)
            : Path.Combine(_attachmentBasePath, attachment.FileDir, attachment.StoredFileName);
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("钉钉附件文件不存在：AttachmentId={AttachmentId}, Path={Path}",
                attachment.Id, filePath);
            return false;
        }

        byte[] fileBytes;
        try
        {
            fileBytes = await File.ReadAllBytesAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉附件读取失败：AttachmentId={AttachmentId}, Path={Path}",
                attachment.Id, filePath);
            return false;
        }

        if (fileBytes.Length == 0)
        {
            _logger.LogWarning("钉钉附件文件为空：AttachmentId={AttachmentId}, Path={Path}",
                attachment.Id, filePath);
            return false;
        }

        var isImage = attachment.Type == AttachmentType.Image
            || (attachment.MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false);

        // 说明：钉钉自定义机器人 / sessionWebhook 的消息类型不支持 msgtype:"image"（仅支持 text/link/markdown/actionCard/feedCard）。
        // 因此无论是图片还是文件，统一走 UploadMediaAsync + BatchSendOTO 的路线，使用 sampleImageMsg / sampleFile 模板。
        // 如果 sessionWebhook 路径可用，我们仅用于发送文本消息，不用于发送图片/文件，避免返回 errcode。

        // 路径 2：上传媒体获取 media_id，再通过 batchSend 发送（用于 sessionWebhook 发送图片失败或非图片附件）
        if (string.IsNullOrEmpty(robotCode) || string.IsNullOrEmpty(customerUserId))
        {
            _logger.LogWarning("钉钉附件发送缺少 robotCode 或 customerUserId：AttachmentId={AttachmentId}", attachment.Id);
            return false;
        }

        string? mediaId;
        try
        {
            mediaId = await _dingDingClient.UploadMediaAsync(
                fileBytes, attachment.FileName ?? $"file{GetExtension(attachment)}",
                attachment.MimeType ?? "application/octet-stream");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉附件上传媒体失败：AttachmentId={AttachmentId}", attachment.Id);
            return false;
        }

        if (string.IsNullOrEmpty(mediaId))
        {
            _logger.LogError("钉钉附件上传返回 media_id 为空：AttachmentId={AttachmentId}", attachment.Id);
            return false;
        }

        try
        {
            string? processKey;
            if (isImage)
            {
                processKey = await _dingDingClient.BatchSendImageMessageAsync(
                    robotCode!, new[] { customerUserId }, mediaId);
                if (!string.IsNullOrEmpty(processKey))
                {
                    _logger.LogInformation("钉钉图片附件通过 batchSend 发送成功：AttachmentId={AttachmentId}", attachment.Id);
                    return true;
                }
            }
            else
            {
                var ext = GetExtension(attachment).TrimStart('.');
                processKey = await _dingDingClient.BatchSendFileMessageAsync(
                    robotCode!, new[] { customerUserId }, mediaId,
                    attachment.FileName ?? $"file.{ext}", ext);
                if (!string.IsNullOrEmpty(processKey))
                {
                    _logger.LogInformation("钉钉文件附件通过 batchSend 发送成功：AttachmentId={AttachmentId}", attachment.Id);
                    return true;
                }
            }

            _logger.LogError("钉钉附件 batchSend 发送失败：AttachmentId={AttachmentId}, Image={IsImage}",
                attachment.Id, isImage);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉附件 batchSend 发送异常：AttachmentId={AttachmentId}", attachment.Id);
            return false;
        }
    }

    /// <summary>
    /// 从 Attachment 获取扩展名，优先从 FileName/StoredFileName 提取，兜底 .bin。
    /// </summary>
    private static string GetExtension(Attachment attachment)
    {
        var name = attachment.FileName;
        if (string.IsNullOrEmpty(name)) name = attachment.StoredFileName;
        var ext = Path.GetExtension(name);
        if (string.IsNullOrEmpty(ext)) ext = ".bin";
        return ext;
    }

    /// <summary>
    /// 计算字节数组的 MD5 HEX（小写，32 字符）。
    /// </summary>
    private static string ComputeMd5Hex(byte[] data)
    {
        if (data == null || data.Length == 0) return string.Empty;
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(data);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>
    /// 将 HTML 转为纯文本（用于微信客服消息等纯文本场景）。
    /// 实现说明（L-F3）：使用 Regex 而非 HtmlSanitizerHelper.ConvertToPlainText，
    /// 因为本方法仅用于已受信任的内部生成的 HTML（客服回复正文），且场景对性能要求较高。
    /// Regex 方案在受控输入下安全可行；如需处理不可信 HTML，应改用 HtmlSanitizerHelper.ConvertToPlainText。
    /// </summary>
    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        // 将 <br> 和 </p> 替换为换行，保留段落结构
        var text = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</p\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<p[^>]*>", "", RegexOptions.IgnoreCase);
        // 移除其他所有 HTML 标签
        text = Regex.Replace(text, @"<[^>]+>", "");
        // 解码 HTML 实体（&amp; &lt; &gt; 等）
        text = WebUtility.HtmlDecode(text);
        return text.Trim();
    }

    public async Task<IEnumerable<ThreadResponse>> GetThreadsByConversationIdAsync(int conversationId)
    {
        var threads = await _unitOfWork.Conversations.GetThreadsByConversationIdAsync(conversationId);
        return threads.Select(t => t.ToResponse());
    }

    public async Task<ThreadResponse?> AddNoteAsync(int conversationId, AddNoteRequest request, int userId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(conversationId);
        if (conversation == null)
        {
            return null;
        }

        var thread = new ConversationThread
        {
            ConversationId = conversationId,
            UserId = userId,
            CustomerId = conversation.CustomerId,
            Type = ThreadType.Note,
            Body = HtmlSanitizerHelper.Sanitize(request.Body),
            IsInternal = true,
            CreatedAt = DateTime.UtcNow
        };

        if (request.AttachmentIds != null && request.AttachmentIds.Any())
        {
            var attachments = await _unitOfWork.Attachments.GetByIdsTrackedAsync(request.AttachmentIds);
            foreach (var attachment in attachments)
            {
                thread.Attachments.Add(attachment);
            }
        }

        conversation.Threads.Add(thread);
        conversation.UpdatedAt = DateTime.UtcNow;

        var suppressed = IsSuppressed;

        async Task PersistAsync()
        {
            await _unitOfWork.SaveChangesAsync();

            if (thread.Attachments.Any())
            {
                foreach (var attachment in thread.Attachments)
                {
                    attachment.ThreadId = thread.Id;
                }
                await _unitOfWork.SaveChangesAsync();
            }
        }

        if (suppressed)
        {
            await PersistAsync();
        }
        else
        {
            using (var transaction = await _unitOfWork.BeginTransactionAsync())
            {
                try
                {
                    await PersistAsync();
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.SafeRollbackAsync();
                    throw;
                }
            }
        }

        // 显式加载 User 导航属性：ToResponse 映射需要 thread.User 生成完整响应，
        // 否则前端因 user 为 null 回退到客户姓名显示。
        if (thread.UserId.HasValue && thread.User == null)
        {
            thread.User = await _unitOfWork.Users.GetByIdAsync(thread.UserId.Value);
        }

        var threadResponse = thread.ToResponse();
        await _notificationPublisher.NotifyMailboxAsync(conversation.MailboxId.ToString(), "note_created", threadResponse);

        // 抑制作用域（工作流/宏嵌套调用）下不触发嵌套工作流：
        // 1. 避免 ClearChanges 把外层工作流已追踪的会话/宏实体状态清空；
        // 2. 避免同一个事件触发无限递归的工作流（例如宏执行 add_note → 触发 note_added 工作流 → 再次执行宏）。
        if (!suppressed)
        {
            try
            {
                await _workflowService.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerNoteAdded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发工作流失败（内部备注）");
            }
        }

        return threadResponse;
    }

    public async Task<IEnumerable<ThreadResponse>> GetChatModeThreadsAsync(int conversationId)
    {
        var threads = await _unitOfWork.Threads.GetChatModeThreadsAsync(conversationId);
        return threads.Select(t => t.ToResponse());
    }

    /// <inheritdoc/>
    public IDisposable BeginSuppressedScope() => new SuppressedScopeCookie();

    /// <summary>
    /// 引用计数的抑制作用域 Cookie。
    /// Dispose 时递减深度，确保嵌套 BeginSuppressedScope 正确。
    /// </summary>
    private sealed class SuppressedScopeCookie : IDisposable
    {
        private int _disposed;
        public SuppressedScopeCookie()
        {
            _suppressedDepth.Value = _suppressedDepth.Value + 1;
        }
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
            var current = _suppressedDepth.Value;
            if (current > 0) _suppressedDepth.Value = current - 1;
        }
    }

    private async Task<string> RenderReplyEmailBodyAsync(Conversation conversation, string replyBody, int? userId, Mailbox mailbox)
    {
        var customer = conversation.CustomerId > 0
            ? await _unitOfWork.Customers.GetByIdAsync(conversation.CustomerId ?? 0)
            : null;
        var user = userId.HasValue
            ? await _unitOfWork.Users.GetByIdAsync(userId.Value)
            : null;

        // replyBody 来自富文本编辑器，使用 HtmlSanitizerHelper 进行白名单消毒防止 XSS
        var sanitizedReplyBody = HtmlSanitizerHelper.Sanitize(replyBody);

        var variables = new Dictionary<string, string>
        {
            ["subject"] = conversation.Subject ?? string.Empty,
            ["conversation_id"] = conversation.Id.ToString(),
            ["conversation_url"] = $"{_frontendUrlResolver.GetFrontendUrl()}/conversations/{conversation.Id}",
            ["customer_name"] = customer != null ? $"{customer.FirstName} {customer.LastName}".Trim() : conversation.CustomerEmail,
            ["customer_email"] = conversation.CustomerEmail ?? string.Empty,
            ["user_name"] = user != null ? $"{user.FirstName} {user.LastName}".Trim() : string.Empty,
            ["user_email"] = user?.Email ?? string.Empty,
            ["mailbox_name"] = mailbox.Name ?? string.Empty,
            ["mailbox_email"] = mailbox.Email ?? string.Empty,
            ["message_body"] = sanitizedReplyBody,
            ["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
            ["company_name"] = _brand.CompanyName
        };

        var template = await _mailTemplateService.GetBySlugAsync("customer-reply-default");
        if (template != null && template.IsActive)
        {
            // message_body 已经过 HtmlSanitizerHelper 消毒，标记为 raw HTML 跳过 HtmlEncode，避免标签被转义为纯文本
            var renderedBody = await _mailTemplateService.RenderTemplateAsync(template.Id, variables, new HashSet<string> { "message_body" });
            return await AppendSignature(renderedBody, userId, mailbox, user);
        }

        var defaultBody = $@"
<p>您好，</p>
<div style=""background:#f9f9f9;padding:15px;border-left:3px solid #007bff;margin:10px 0;"">
  <p><strong>{WebUtility.HtmlEncode(variables["user_name"])}</strong> 回复道：</p>
  <div>{sanitizedReplyBody}</div>
</div>";
        return await AppendSignature(defaultBody, userId, mailbox, user);
    }

    private async Task<string> AppendSignature(string body, int? userId, Mailbox mailbox, Domain.Entities.User? user = null)
    {
        string? signatureHtml = null;

        if (userId.HasValue)
        {
            var userSignature = await _emailSignatureService.GetDefaultAsync(userId.Value, mailbox.Id);
            if (userSignature != null)
            {
                signatureHtml = userSignature.Body;
            }
        }

        if (string.IsNullOrEmpty(signatureHtml) && userId.HasValue)
        {
            var globalSignature = await _emailSignatureService.GetDefaultAsync(userId.Value, null);
            if (globalSignature != null)
            {
                signatureHtml = globalSignature.Body;
            }
        }

        if (string.IsNullOrEmpty(signatureHtml) && !string.IsNullOrEmpty(mailbox.Signature))
        {
            signatureHtml = $"<p>{WebUtility.HtmlEncode(mailbox.Signature)}</p>";
        }

        if (!string.IsNullOrEmpty(signatureHtml))
        {
            var signatureVars = new Dictionary<string, string>
            {
                ["user_name"] = user != null ? $"{user.FirstName} {user.LastName}".Trim() : string.Empty,
                ["company_name"] = _brand.CompanyName,
                ["mailbox_name"] = mailbox.Name ?? string.Empty,
                ["mailbox_email"] = mailbox.Email ?? string.Empty
            };
            signatureHtml = _renderer.Render(signatureHtml, signatureVars);

            body += $"<br/><hr/>{signatureHtml}";
        }

        return body;
    }
}
