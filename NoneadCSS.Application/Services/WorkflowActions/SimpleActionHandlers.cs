using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;

namespace NoneadCSS.Application.Services.WorkflowActions;

// ====== 简单动作处理器（无外部服务依赖或仅依赖 IUnitOfWork） ======

/// <summary>
/// 设置会话状态
/// </summary>
public class SetStatusActionHandler : IWorkflowActionHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFolderService _folderService;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<SetStatusActionHandler> _logger;

    public SetStatusActionHandler(
        IUnitOfWork unitOfWork,
        IFolderService folderService,
        INotificationPublisher notificationPublisher,
        ILogger<SetStatusActionHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _folderService = folderService;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
    }

    public string ActionType => "set_status";

    public async Task<bool> ExecuteAsync(WorkflowAction action, Conversation conversation, WorkflowActionContext context)
    {
        if (!int.TryParse(action.Value, out var status) || !Enum.IsDefined(typeof(ConversationStatus), status))
            return false;

        var oldStatus = conversation.Status;
        var wasClosed = oldStatus == ConversationStatus.Closed;
        var newStatus = (ConversationStatus)status;

        conversation.SetStatus(newStatus);

        // 同步文件夹归属（状态变更可能影响文件夹类型，如 Spam→垃圾邮件文件夹）
        try
        {
            await _folderService.UpdateConversationFolderAsync(conversation);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "工作流更新会话 {ConversationId} 文件夹归属失败", conversation.Id);
        }

        // H-006: 状态未发生变化时跳过实时通知与关闭/重开工作流，避免引发工作流递归
        if (oldStatus != newStatus)
        {
            // 发送状态变更实时通知
            try
            {
                await _notificationPublisher.NotifyMailboxAsync(
                    conversation.MailboxId.ToString(),
                    "conversation_status_changed",
                    conversation.ToResponse());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "工作流发送会话 {ConversationId} 状态变更通知失败", conversation.Id);
            }

            // BUG-05 修复：工作流 set_status 关闭/重开会话时，需补充触发 conversation_closed /
            // conversation_reopened 工作流，与 ConversationStatusService.UpdateStatusAsync 行为一致。
            // 之前通过 set_status: closed 关闭会话仅触发 status_changed，conversation_closed 被绕过，
            // 导致用户配置的"会话关闭"工作流（满意度评价邮件、归档通知、SLA 停表等）无法执行。
            //
            // 使用 PendingSideEffects 延迟到外层工作流事务提交后执行：
            //   1. 确保被触发的工作流能读到已持久化的新状态（避免读到未提交的旧状态）
            //   2. 若外层事务回滚，副作用不会执行，避免在未持久化的状态上触发工作流
            //   3. 通过 IServiceProvider 延迟解析 IWorkflowService 以避免循环依赖
            //     （WorkflowService → IWorkflowActionHandlerProvider → IWorkflowActionHandler → 本处理器）
            var isClosing = !wasClosed && newStatus == ConversationStatus.Closed;
            var isReopening = wasClosed && newStatus != ConversationStatus.Closed;

            if (isClosing || isReopening)
            {
                var conversationId = conversation.Id;
                var triggerType = isClosing
                    ? WorkflowConst.TriggerConversationClosed
                    : WorkflowConst.TriggerConversationReopened;

                var sideEffect = async () =>
                {
                    try
                    {
                        var workflowService = context.ServiceProvider.GetRequiredService<IWorkflowService>();
                        await workflowService.ExecuteForConversationAsync(conversationId, triggerType);
                        _logger.LogInformation(
                            "工作流 set_status 变更会话 {ConversationId} 状态，已触发 {TriggerType} 工作流",
                            conversationId, triggerType);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "工作流 set_status 触发 {TriggerType} 工作流失败：ConversationId={ConversationId}",
                            triggerType, conversationId);
                    }
                };

                if (context.PendingSideEffects != null)
                {
                    context.PendingSideEffects.Add(sideEffect);
                }
                else
                {
                    // 非事务场景（如 TestAsync），立即执行
                    await sideEffect();
                }
            }
        }

        return true;
    }
}

/// <summary>
/// 会话处理（软删除/硬删除）
/// </summary>
public class ConversationProcessActionHandler : IWorkflowActionHandler
{
    private readonly ILogger<ConversationProcessActionHandler> _logger;

    public ConversationProcessActionHandler(ILogger<ConversationProcessActionHandler> logger)
    {
        _logger = logger;
    }

    public string ActionType => "conversation_process";

    public async Task<bool> ExecuteAsync(WorkflowAction action, Conversation conversation, WorkflowActionContext context)
    {
        if (action.Value == "soft_delete")
        {
            conversation.SoftDelete();
            return true;
        }
        if (action.Value == "hard_delete")
        {
            // M-005: 硬删除可能导致外键级联问题，改为软删除并记录告警日志
            _logger.LogWarning("工作流 hard_delete 动作已降级为软删除（建议使用软删除避免级联问题），ConversationId={ConversationId}", conversation.Id);
            conversation.SoftDelete();
            return true;
        }
        return false;
    }
}

/// <summary>
/// 分配客服
/// </summary>
public class AssignUserActionHandler : IWorkflowActionHandler
{
    public string ActionType => "assign_user";

    public Task<bool> ExecuteAsync(WorkflowAction action, Conversation conversation, WorkflowActionContext context)
    {
        if (int.TryParse(action.Value, out var userId))
        {
            conversation.AssignTo(userId);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}

/// <summary>
/// 添加标签
/// </summary>
public class AddTagActionHandler : IWorkflowActionHandler
{
    private readonly IUnitOfWork _unitOfWork;

    public AddTagActionHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public string ActionType => "add_tag";

    public async Task<bool> ExecuteAsync(WorkflowAction action, Conversation conversation, WorkflowActionContext context)
    {
        if (string.IsNullOrEmpty(action.Value))
            return false;

        var tag = (await _unitOfWork.Tags
            .FindAsync(t => t.Name == action.Value && (t.MailboxId == conversation.MailboxId || t.MailboxId == null)))
            .OrderByDescending(t => t.MailboxId.HasValue)
            .FirstOrDefault();

        if (tag == null)
            return false;

        var existing = (await _unitOfWork.Repository<ConversationTag>()
            .FindAsync(ct => ct.ConversationId == conversation.Id && ct.TagId == tag.Id))
            .FirstOrDefault();

        if (existing != null)
            return false;

        await _unitOfWork.Repository<ConversationTag>().AddAsync(new ConversationTag
        {
            ConversationId = conversation.Id,
            TagId = tag.Id
        });
        conversation.UpdatedAt = DateTime.UtcNow;

        return true;
    }
}

/// <summary>
/// 移除标签
/// </summary>
public class RemoveTagActionHandler : IWorkflowActionHandler
{
    private readonly IUnitOfWork _unitOfWork;

    public RemoveTagActionHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public string ActionType => "remove_tag";

    public async Task<bool> ExecuteAsync(WorkflowAction action, Conversation conversation, WorkflowActionContext context)
    {
        if (string.IsNullOrEmpty(action.Value))
            return false;

        var tag = (await _unitOfWork.Tags
            .FindAsync(t => t.Name == action.Value && (t.MailboxId == conversation.MailboxId || t.MailboxId == null)))
            .OrderByDescending(t => t.MailboxId.HasValue)
            .FirstOrDefault();

        if (tag == null)
            return false;

        var existing = (await _unitOfWork.Repository<ConversationTag>()
            .FindAsync(ct => ct.ConversationId == conversation.Id && ct.TagId == tag.Id))
            .FirstOrDefault();

        if (existing == null)
            return false;

        await _unitOfWork.Repository<ConversationTag>().DeleteAsync(existing);
        conversation.UpdatedAt = DateTime.UtcNow;

        return true;
    }
}

/// <summary>
/// 设置优先级
/// </summary>
public class SetPriorityActionHandler : IWorkflowActionHandler
{
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<SetPriorityActionHandler> _logger;

    public SetPriorityActionHandler(
        INotificationPublisher notificationPublisher,
        ILogger<SetPriorityActionHandler> logger)
    {
        _notificationPublisher = notificationPublisher;
        _logger = logger;
    }

    public string ActionType => "set_priority";

    public async Task<bool> ExecuteAsync(WorkflowAction action, Conversation conversation, WorkflowActionContext context)
    {
        if (!int.TryParse(action.Value, out var priority) || !Enum.IsDefined(typeof(ConversationPriority), priority))
            return false;

        conversation.SetPriority((ConversationPriority)priority);

        try
        {
            await _notificationPublisher.NotifyMailboxAsync(
                conversation.MailboxId.ToString(),
                "conversation_priority_changed",
                conversation.ToResponse());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "工作流发送会话 {ConversationId} 优先级变更通知失败", conversation.Id);
        }

        return true;
    }
}

/// <summary>
/// 发送回复/转发动作处理器（历史占位动作，现已实现真实发送）。
/// 语义：
///   send_reply —— 向客户（或 action.Extra 指定收件人）发送回复邮件。
///   forward   —— 将会话转发到 action.Extra/action.Value 指定的收件人。
/// 取值约定：
///   action.Value —— 邮件模板 slug（优先，复用 send_email 完整模板渲染、变量替换、引用头链路）
///                   或字面回复/转发正文内容。
///   action.Extra —— 收件人：customer / assignee / 具体邮箱地址，逗号分隔；默认 customer。
/// </summary>
public class SendReplyForwardActionHandler : IWorkflowActionHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMailTemplateService _mailTemplateService;
    private readonly IEmailQueueService _emailQueueService;
    private readonly SendEmailActionHandler _sendEmailHandler;
    private readonly ILogger<SendReplyForwardActionHandler> _logger;

    public SendReplyForwardActionHandler(
        IUnitOfWork unitOfWork,
        IMailTemplateService mailTemplateService,
        IEmailQueueService emailQueueService,
        SendEmailActionHandler sendEmailHandler,
        ILogger<SendReplyForwardActionHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _mailTemplateService = mailTemplateService;
        _emailQueueService = emailQueueService;
        _sendEmailHandler = sendEmailHandler;
        _logger = logger;
    }

    // 主动作类型为 send_reply，同时通过 AdditionalActionTypes 复用 forward
    public string ActionType => "send_reply";

    public string[] AdditionalActionTypes => new[] { "forward" };

    public async Task<bool> ExecuteAsync(WorkflowAction action, Conversation conversation, WorkflowActionContext context)
    {
        var value = action.Value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            _logger.LogWarning("工作流动作 {ActionType} 缺少 Value（模板 slug 或回复/转发内容），会话 {ConversationId}",
                action.Type, conversation.Id);
            return false;
        }

        // Value 是有效且启用的邮件模板 → 复用 send_email 完整链路（模板渲染 + 变量替换 + 引用头 + 入队）
        var template = await _mailTemplateService.GetBySlugAsync(value);
        if (template != null && template.IsActive)
        {
            return await _sendEmailHandler.ExecuteAsync(action, conversation, context);
        }

        // Value 不是模板 slug → 视为字面回复/转发内容，直接以纯文本邮件发出
        return await SendPlainAsync(action, conversation, value);
    }

    private async Task<bool> SendPlainAsync(WorkflowAction action, Conversation conversation, string bodyText)
    {
        var recipients = await ResolveRecipientsAsync(action.Extra, conversation);
        if (recipients.Count == 0)
        {
            _logger.LogInformation("send_reply/forward 字面内容无收件人，跳过：ConversationId={ConversationId}, ActionType={ActionType}",
                conversation.Id, action.Type);
            return false;
        }

        // 获取会话中客户最后一条消息，用于设置 In-Reply-To 与 References 头以及会话匹配标记
        var lastCustomerThread = await _unitOfWork.Threads.GetLatestCustomerThreadAsync(conversation.Id);
        string? inReplyTo = null;
        string? references = null;
        int? threadId = null;
        if (lastCustomerThread != null)
        {
            inReplyTo = lastCustomerThread.MessageId;
            // RFC 5322 §3.6.4：References 必须包含完整祖先链
            var parentReferences = EmailHeaderHelper.ExtractReferences(lastCustomerThread.Headers);
            references = EmailHeaderHelper.BuildReferences(parentReferences, lastCustomerThread.MessageId);
            threadId = lastCustomerThread.Id;
        }

        var sendSubject = string.IsNullOrWhiteSpace(conversation.Subject)
            ? (action.Type == "forward" ? "转发会话" : "回复")
            : conversation.Subject;
        if (!sendSubject.Contains($"[#{conversation.Number}]"))
        {
            sendSubject = $"[#{conversation.Number}] {sendSubject}";
        }

        var htmlBody = $"<div style=\"white-space:pre-wrap;word-break:break-word;\">{System.Net.WebUtility.HtmlEncode(bodyText)}</div>";
        // 追加 {#FS:threadId#} 隐藏标记，与 EmailIngestionService / ConversationThreadService 保持一致
        if (threadId.HasValue)
        {
            htmlBody += $"\n<div style=\"display:none !important;visibility:hidden;mso-hide:all;height:0;max-height:0;overflow:hidden;font-size:0;line-height:0;\">{{#FS:{threadId.Value}#}}</div>";
        }

        foreach (var to in recipients)
        {
            await _emailQueueService.EnqueueEmailAsync(
                mailboxId: conversation.MailboxId,
                to: to,
                subject: sendSubject,
                body: htmlBody,
                attachmentIds: null,
                cc: null,
                bcc: null,
                threadId: threadId,
                inReplyTo: inReplyTo,
                references: references,
                customerId: conversation.CustomerId);
        }

        _logger.LogInformation("send_reply/forward 字面内容发送完成：ActionType={ActionType}, 收件人={Recipients}, 会话={ConversationId}",
            action.Type, string.Join(",", recipients), conversation.Id);
        return true;
    }

    private async Task<List<string>> ResolveRecipientsAsync(string? extra, Conversation conversation)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(extra))
        {
            // 默认发给客户
            if (!string.IsNullOrWhiteSpace(conversation.CustomerEmail))
            {
                result.Add(conversation.CustomerEmail);
            }
            return result;
        }

        var tokens = extra.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            if (string.Equals(token, "customer", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(conversation.CustomerEmail))
                {
                    result.Add(conversation.CustomerEmail);
                }
                continue;
            }
            if (string.Equals(token, "assignee", StringComparison.OrdinalIgnoreCase))
            {
                if (conversation.UserId.HasValue)
                {
                    var user = await _unitOfWork.Users.GetByIdAsync(conversation.UserId.Value);
                    if (user != null && !string.IsNullOrWhiteSpace(user.Email))
                    {
                        result.Add(user.Email);
                    }
                }
                continue;
            }
            // 当作具体邮箱地址（简单校验）
            if (token.Contains('@') && token.Contains('.'))
            {
                result.Add(token);
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
