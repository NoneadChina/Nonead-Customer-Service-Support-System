using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services.WorkflowActions;

// ====== 复杂动作处理器（依赖外部服务：邮件模板、Webhook、宏等） ======

/// <summary>
/// 发送邮件动作处理器：根据模板 slug 渲染邮件并入队发送。
/// 支持 action.Extra 指定收件人：customer / assignee / 具体邮箱地址（逗号分隔）。
/// </summary>
public class SendEmailActionHandler : IWorkflowActionHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMailTemplateService _mailTemplateService;
    private readonly IEmailQueueService _emailQueueService;
    private readonly IConfiguration _configuration;
    private readonly ISatisfactionTokenGenerator _satisfactionTokenGenerator;
    private readonly IFrontendUrlResolver _frontendUrlResolver;
    private readonly ILogger<SendEmailActionHandler> _logger;
    private readonly BrandOptions _brand;

    public SendEmailActionHandler(
        IUnitOfWork unitOfWork,
        IMailTemplateService mailTemplateService,
        IEmailQueueService emailQueueService,
        IConfiguration configuration,
        ISatisfactionTokenGenerator satisfactionTokenGenerator,
        IFrontendUrlResolver frontendUrlResolver,
        ILogger<SendEmailActionHandler> logger,
        IOptions<BrandOptions> brand)
    {
        _unitOfWork = unitOfWork;
        _mailTemplateService = mailTemplateService;
        _emailQueueService = emailQueueService;
        _configuration = configuration;
        _satisfactionTokenGenerator = satisfactionTokenGenerator;
        _frontendUrlResolver = frontendUrlResolver;
        _logger = logger;
        _brand = brand.Value;
    }

    public string ActionType => WorkflowConst.ActionSendEmail;

    public async Task<bool> ExecuteAsync(WorkflowAction action, Conversation conversation, WorkflowActionContext context)
    {
        var slug = action.Value?.Trim();
        if (string.IsNullOrWhiteSpace(slug))
        {
            _logger.LogWarning("send_email 动作缺少模板 slug，工作流会话 {ConversationId}", conversation.Id);
            return false;
        }

        var template = await _mailTemplateService.GetBySlugAsync(slug);
        if (template == null)
        {
            _logger.LogError("send_email 动作未找到模板 slug={Slug}，工作流配置错误", slug);
            return false;
        }
        if (!template.IsActive)
        {
            _logger.LogInformation("send_email 动作模板 slug={Slug} 已禁用，跳过发送", slug);
            return false;
        }

        // 收集变量
        var variables = await BuildMailVariablesAsync(conversation, context.Rating);

        // 渲染主题与正文（message_body 已经过 HtmlSanitizer 消毒，跳过编码）
        var rendered = await _mailTemplateService.RenderTemplateFullAsync(template.Id, variables, rawHtmlKeys: new HashSet<string> { "message_body", "first_message_body" });

        // 解析收件人
        var recipients = await ResolveRecipientsAsync(action.Extra, conversation);

        if (recipients.Count == 0)
        {
            // 收件人为空：send_email 仅负责邮件发送，IM 渠道的关闭通知由独立的 send_im_message 动作处理。
            // 工作流配置应通过 source_type 条件将邮件渠道和 IM 渠道的关闭通知分离到不同工作流。
            _logger.LogInformation("send_email 动作无收件人，跳过：slug={Slug}, conversation={ConversationId}, sourceType={SourceType}",
                slug, conversation.Id, conversation.SourceType);
            return false;
        }

        // 入队发送：获取会话中客户最后一条消息，用于设置 In-Reply-To 和 References 头
        var lastCustomerThread = await _unitOfWork.Threads.GetLatestCustomerThreadAsync(conversation.Id);
        string? inReplyTo = null;
        string? references = null;
        int? threadId = null;
        if (lastCustomerThread != null)
        {
            inReplyTo = lastCustomerThread.MessageId;
            // RFC 5322 §3.6.4：References 必须包含完整祖先链
            // = 父邮件的 References + 父邮件的 Message-Id
            var parentReferences = EmailHeaderHelper.ExtractReferences(lastCustomerThread.Headers);
            references = EmailHeaderHelper.BuildReferences(parentReferences, lastCustomerThread.MessageId);
            threadId = lastCustomerThread.Id;
        }

        // 在主题中添加会话编号标记，确保客户回复时能通过主题匹配到原会话
        var sendSubject = rendered.Subject;
        if (!string.IsNullOrEmpty(sendSubject) && !sendSubject.Contains($"[#{conversation.Number}]"))
        {
            sendSubject = $"[#{conversation.Number}] {sendSubject}";
        }

        // 在正文末尾追加 {#FS:threadId#} 隐藏标记，用于邮件网关删除 Message-ID 时的兜底会话匹配
        // 与 EmailIngestionService.SendAutoReplyAsync 和 ConversationThreadService 保持一致
        var bodyWithMarker = rendered.BodyHtml;
        if (threadId.HasValue)
        {
            bodyWithMarker += $"\n<div style=\"display:none !important;visibility:hidden;mso-hide:all;height:0;max-height:0;overflow:hidden;font-size:0;line-height:0;\">{{#FS:{threadId.Value}#}}</div>";
        }

        foreach (var to in recipients)
        {
            await _emailQueueService.EnqueueEmailAsync(
                mailboxId: conversation.MailboxId,
                to: to,
                subject: sendSubject,
                body: bodyWithMarker,
                attachmentIds: null,
                cc: null,
                bcc: null,
                threadId: threadId,
                inReplyTo: inReplyTo,
                references: references,
                customerId: conversation.CustomerId);
        }

        _logger.LogInformation("send_email 动作完成：slug={Slug}, 收件人={Recipients}, 会话={ConversationId}",
            slug, string.Join(",", recipients), conversation.Id);
        return true;
    }

    /// <summary>
    /// 构造邮件模板变量字典（与会话上下文绑定）
    /// </summary>
    private async Task<Dictionary<string, string>> BuildMailVariablesAsync(Conversation conversation, SatisfactionRating? rating = null)
    {
        var frontendUrl = _frontendUrlResolver.GetFrontendUrl();
        // 使用 GetQueryable(true) 而非 Mailboxes.GetByIdAsync，避免 Include MailboxUsers
        // 导致 EF Core fixup 将 MailboxUsers 错误标记为 Added（与 WorkflowService 保持一致）
        var mailbox = await _unitOfWork.Repository<Mailbox>()
            .GetQueryable(true)
            .FirstOrDefaultAsync(m => m.Id == conversation.MailboxId && m.DeletedAt == null);
        Customer? customer = null;
        if (conversation.CustomerId.HasValue)
        {
            customer = await _unitOfWork.Customers.GetByIdAsync(conversation.CustomerId.Value);
        }
        User? assignee = null;
        if (conversation.UserId.HasValue)
        {
            assignee = await _unitOfWork.Users.GetByIdAsync(conversation.UserId.Value);
        }

        // T000111：满意度评价链接必须以客户邮箱为标识生成。会话反规范化列 CustomerEmail
        // 可能为空（客户邮箱仅存在于 Customer.Emails 集合），此时回退到 GetMainEmail()，
        // 避免 satisfaction_url 为空导致邮件中的评价链接变成 about:blank / 空 href。
        var customerEmail = conversation.CustomerEmail ?? string.Empty;
        if (string.IsNullOrWhiteSpace(customerEmail) && customer != null)
        {
            customerEmail = customer.GetMainEmail() ?? string.Empty;
        }

        // 同时提供首条消息（用于 new_conversation 场景）和最新消息（用于 new_reply 等场景）
        // message_body 默认指向最新一条线程正文，避免工作流误用客户原始邮件内容
        var threads = await _unitOfWork.Threads.FindAsync(t => t.ConversationId == conversation.Id);
        var orderedThreads = threads.OrderBy(t => t.CreatedAt).ToList();
        var firstThread = orderedThreads.FirstOrDefault();
        var latestThread = orderedThreads.LastOrDefault();

        var variables = new Dictionary<string, string>
        {
            ["subject"] = conversation.Subject ?? string.Empty,
            ["conversation_id"] = conversation.Id.ToString(),
            ["conversation_url"] = $"{frontendUrl}/conversations/{conversation.Id}",
            ["customer_name"] = customer != null ? $"{customer.FirstName} {customer.LastName}".Trim() : (customerEmail),
            ["customer_email"] = customerEmail,
            ["user_name"] = assignee != null ? $"{assignee.FirstName} {assignee.LastName}".Trim() : string.Empty,
            ["user_email"] = assignee?.Email ?? string.Empty,
            ["mailbox_name"] = mailbox?.Name ?? string.Empty,
            ["mailbox_email"] = mailbox?.Email ?? string.Empty,
            // message_body 指向最新一条线程正文（客服回复/客户最新回复等场景）
            ["message_body"] = latestThread?.Body ?? string.Empty,
            // first_message_body 保留首条线程正文，便于新会话通知等场景显式引用
            ["first_message_body"] = firstThread?.Body ?? string.Empty,
            ["created_at"] = conversation.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            ["company_name"] = _brand.CompanyName,
            ["login_url"] = $"{frontendUrl}/login",
            ["reset_password_url"] = $"{frontendUrl}/reset-password",
            ["activation_url"] = $"{frontendUrl}/activate",
            ["received_time"] = conversation.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            ["assigned_to"] = assignee != null ? $"{assignee.FirstName} {assignee.LastName}".Trim() : string.Empty,
            ["priority"] = conversation.Priority.ToString(),
            ["waiting_time"] = string.Empty,
            ["timeout_minutes"] = string.Empty,
            ["last_reply_time"] = string.Empty,
            ["escalation_reason"] = GetEscalationReason(conversation),
            ["escalated_by"] = string.IsNullOrEmpty(GetEscalationReason(conversation)) ? string.Empty : "系统工作流",
            ["escalated_at"] = string.IsNullOrEmpty(GetEscalationReason(conversation)) ? string.Empty : DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
            ["replier_name"] = string.Empty,
            ["assignee_name"] = assignee != null ? $"{assignee.FirstName} {assignee.LastName}".Trim() : string.Empty,
            ["assigner_name"] = string.Empty,
            ["closer_name"] = string.Empty,
            ["updater_name"] = string.Empty,
            ["update_content"] = string.Empty,
            ["mentioner_name"] = string.Empty,
            ["rating_url"] = string.Empty,
            ["feedback_url"] = string.Empty,
            ["satisfaction_url"] = string.Empty,
            ["sla_name"] = string.Empty,
            ["sla_level"] = string.Empty,
            ["sla_response_time"] = string.Empty,
            ["sla_deadline"] = string.Empty,
            ["deadline"] = string.Empty,
            ["sla_remaining"] = string.Empty,
            ["remaining_time"] = string.Empty,
            ["violation_type"] = string.Empty,
            ["satisfaction_rating"] = string.Empty,
            ["satisfaction_rating_value"] = string.Empty,
            ["satisfaction_comment"] = string.Empty,
            ["user_role"] = assignee?.Role.ToString() ?? string.Empty,
            ["reset_url"] = $"{frontendUrl}/reset-password",
            ["expire_time"] = "24小时",
            ["total_count"] = string.Empty,
            ["success_count"] = string.Empty,
            ["failed_count"] = string.Empty,
            ["error_report_url"] = string.Empty
        };

        var lastReplyThread = threads.OrderByDescending(t => t.CreatedAt).FirstOrDefault();
        if (lastReplyThread != null)
        {
            variables["last_reply_time"] = lastReplyThread.CreatedAt.ToString("yyyy-MM-dd HH:mm");
            var waitingMinutes = (DateTime.UtcNow - lastReplyThread.CreatedAt).TotalMinutes;
            variables["waiting_time"] = $"{(int)waitingMinutes} 分钟";
            variables["timeout_minutes"] = ((int)waitingMinutes).ToString();
        }

        var customerThreads = threads.Where(t => t.Type == ThreadType.Customer).OrderBy(t => t.CreatedAt).ToList();
        var replyThreads = threads.Where(t => t.Type == ThreadType.Message).OrderBy(t => t.CreatedAt).ToList();

        if (customerThreads.Any())
        {
            var lastCustomerThread = customerThreads.Last();
            var threadCustomer = lastCustomerThread.Customer ?? customer;
            variables["replier_name"] = threadCustomer != null ? $"{threadCustomer.FirstName} {threadCustomer.LastName}".Trim() : lastCustomerThread.From ?? conversation.CustomerEmail ?? string.Empty;
        }
        else if (replyThreads.Any())
        {
            var lastReply = replyThreads.Last();
            var threadUser = lastReply.User ?? assignee;
            variables["replier_name"] = threadUser != null ? $"{threadUser.FirstName} {threadUser.LastName}".Trim() : lastReply.From ?? string.Empty;
        }

        // 满意度评价链接
        // 双路径生成：
        // - 邮件渠道：客户邮箱非空（优先会话列，回退 Customer.Emails 主邮箱），用邮箱生成 token
        // - 聊天渠道（微信/飞书/WhatsApp/钉钉）：客户邮箱为空，用 Customer.ChannelId 生成 token
        if (!string.IsNullOrWhiteSpace(customerEmail))
        {
            var satisfactionToken = _satisfactionTokenGenerator.GenerateToken(conversation.Id, customerEmail);
            variables["satisfaction_url"] = $"{frontendUrl}/satisfaction/{satisfactionToken}";
        }
        else if (customer != null && !string.IsNullOrWhiteSpace(customer.ChannelId))
        {
            var satisfactionToken = _satisfactionTokenGenerator.GenerateTokenByChannelId(conversation.Id, customer.ChannelId);
            variables["satisfaction_url"] = $"{frontendUrl}/satisfaction/{satisfactionToken}";
        }
        else
        {
            // 客户既无邮箱也无渠道 ID：无法生成可验证的评价链接，记录告警避免静默产出空链接（T000111）
            _logger.LogWarning("send_email 动作无法生成满意度评价链接：会话 {ConversationId} 无客户邮箱或渠道 ID",
                conversation.Id);
        }

        // SLA 变量
        var slaPolicy = await _unitOfWork.SlaPolicies.GetPolicyForConversationAsync(conversation.MailboxId);
        if (slaPolicy != null)
        {
            var now = DateTime.UtcNow;
            var firstCustomerMessageAt = customerThreads.FirstOrDefault()?.CreatedAt;

            variables["sla_name"] = slaPolicy.Name ?? string.Empty;
            variables["sla_level"] = slaPolicy.Priority.ToString();
            variables["sla_response_time"] = $"{slaPolicy.FirstResponseHours} 小时";

            DateTime? deadline = null;
            string slaRemaining = string.Empty;
            string violationType = string.Empty;

            if (firstCustomerMessageAt.HasValue)
            {
                var firstResponseDueAt = slaPolicy.CalculateFirstResponseDueAt(firstCustomerMessageAt.Value);
                if (replyThreads.Any())
                {
                    deadline = slaPolicy.CalculateResolutionDueAt(conversation.CreatedAt);
                    violationType = "resolution";
                }
                else
                {
                    deadline = firstResponseDueAt;
                    violationType = "first_response";
                }
            }
            else
            {
                deadline = slaPolicy.CalculateResolutionDueAt(conversation.CreatedAt);
                violationType = "resolution";
            }

            if (deadline.HasValue)
            {
                var remainingHours = (deadline.Value - now).TotalHours;
                slaRemaining = SlaConst.FormatHours(remainingHours);
                variables["deadline"] = deadline.Value.ToString("yyyy-MM-dd HH:mm");
                variables["sla_deadline"] = deadline.Value.ToString("yyyy-MM-dd HH:mm");
            }

            variables["sla_remaining"] = slaRemaining;
            variables["remaining_time"] = slaRemaining;
            variables["violation_type"] = violationType switch
            {
                "first_response" => "首次响应超时",
                "resolution" => "解决时长超时",
                _ => violationType
            };
        }

        // 满意度评价变量（仅在 satisfaction_submitted 触发的工作流中可用）
        if (rating != null)
        {
            variables["satisfaction_rating"] = GetSatisfactionRatingLabel(rating.Rating);
            variables["satisfaction_rating_value"] = rating.Rating.ToString();
            variables["satisfaction_comment"] = rating.Comment ?? string.Empty;
        }

        return variables;
    }

    /// <summary>
    /// 解析收件人列表。支持关键字 customer/assignee 与具体邮箱地址，逗号分隔。
    /// </summary>
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
                // T000111：会话 CustomerEmail 列可能为空，回退到 Customer.Emails 主邮箱，
                // 确保关闭会话后客户仍能收到含满意度评价链接的通知邮件。
                var email = conversation.CustomerEmail ?? string.Empty;
                if (string.IsNullOrWhiteSpace(email) && conversation.CustomerId.HasValue)
                {
                    var cust = await _unitOfWork.Customers.GetByIdAsync(conversation.CustomerId.Value);
                    email = cust?.GetMainEmail() ?? string.Empty;
                }
                if (!string.IsNullOrWhiteSpace(email))
                {
                    result.Add(email);
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

    /// <summary>
    /// 根据会话上下文推断升级原因。
    /// 判断优先级（Urgent）→ 主题关键词（升级/投诉/emergency）→ SLA 违规状态，
    /// 返回空字符串表示当前会话并非升级场景。
    /// </summary>
    private static string GetEscalationReason(Conversation conversation)
    {
        if (conversation.Priority == ConversationPriority.Urgent)
        {
            return "会话优先级被升级为紧急";
        }

        var subject = conversation.Subject ?? string.Empty;
        if (subject.Contains("升级", StringComparison.OrdinalIgnoreCase)
            || subject.Contains("投诉", StringComparison.OrdinalIgnoreCase)
            || subject.Contains("emergency", StringComparison.OrdinalIgnoreCase))
        {
            return "会话主题触发升级关键词";
        }

        return string.Empty;
    }

    /// <summary>
    /// 将满意度评分数值转换为文字标签
    /// </summary>
    private static string GetSatisfactionRatingLabel(int rating) => rating switch
    {
        5 => "远超期望",
        4 => "物有所值",
        3 => "中规中矩",
        2 => "略有失望",
        1 => "无法接受",
        _ => "未知"
    };

}

/// <summary>
/// 触发 Webhook 动作处理器：采用两阶段提交策略，
/// 事务内仅收集调用信息到 PendingSideEffects，事务提交后由调用方执行。
/// 若 PendingSideEffects 为 null（如 TriggerActionAsync 直接调用），则立即执行。
/// Webhook 调用失败不影响工作流主流程（返回 true），仅记录到 WebhookLog。
/// </summary>
public class TriggerWebhookActionHandler : IWorkflowActionHandler
{
    private readonly IWebhookService _webhookService;
    private readonly ILogger<TriggerWebhookActionHandler> _logger;

    public TriggerWebhookActionHandler(
        IWebhookService webhookService,
        ILogger<TriggerWebhookActionHandler> logger)
    {
        _webhookService = webhookService;
        _logger = logger;
    }

    public string ActionType => WorkflowConst.ActionTriggerWebhook;

    public async Task<bool> ExecuteAsync(WorkflowAction action, Conversation conversation, WorkflowActionContext context)
    {
        var eventType = action.Value;
        if (string.IsNullOrWhiteSpace(eventType))
        {
            _logger.LogWarning("工作流 trigger_webhook 动作缺少 Value（事件类型），已跳过");
            return false;
        }

        // 构造负载：会话核心信息 + 客户信息
        var payload = new
        {
            event_type = eventType,
            timestamp = DateTime.UtcNow,
            test = false,
            data = new
            {
                conversation_id = conversation.Id,
                subject = conversation.Subject,
                status = conversation.Status.ToString(),
                priority = conversation.Priority.ToString(),
                mailbox_id = conversation.MailboxId,
                customer_email = conversation.CustomerEmail,
                customer_id = conversation.CustomerId,
                assigned_to = conversation.UserId,
                created_at = conversation.CreatedAt
            }
        };

        var mailboxId = conversation.MailboxId;
        var conversationId = conversation.Id;
        var pendingSideEffects = context.PendingSideEffects;

        if (pendingSideEffects != null)
        {
            // 两阶段提交：事务内仅收集，事务提交后执行
            pendingSideEffects.Add(async () =>
            {
                try
                {
                    var count = await _webhookService.TriggerAsync(
                        eventType, mailboxId, payload,
                        entityType: "Conversation", entityId: conversationId);

                    _logger.LogInformation("工作流触发 Webhook 完成: ConversationId={ConversationId}, EventType={EventType}, TriggeredCount={Count}",
                        conversationId, eventType, count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "工作流触发 Webhook 失败（不影响主流程）: ConversationId={ConversationId}, EventType={EventType}",
                        conversationId, eventType);
                }
            });
        }
        else
        {
            // 立即执行（TriggerActionAsync 等非事务场景）
            try
            {
                var count = await _webhookService.TriggerAsync(
                    eventType, mailboxId, payload,
                    entityType: "Conversation", entityId: conversationId);

                _logger.LogInformation("工作流触发 Webhook 完成: ConversationId={ConversationId}, EventType={EventType}, TriggeredCount={Count}",
                    conversationId, eventType, count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "工作流触发 Webhook 失败（不影响主流程）: ConversationId={ConversationId}, EventType={EventType}",
                    conversationId, eventType);
            }
        }

        return true;
    }
}

/// <summary>
/// 执行宏动作处理器：根据 action.Value 中的宏 ID 查找宏，
/// 在当前工作流事务内执行宏的所有动作。宏执行成功则计数+1，
/// 失败则整个工作流回滚（与宏的事务原子性保持一致）。
/// 使用 IServiceProvider 延迟解析以避免循环依赖。
/// </summary>
public class ExecuteMacroActionHandler : IWorkflowActionHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExecuteMacroActionHandler> _logger;

    public ExecuteMacroActionHandler(
        IServiceProvider serviceProvider,
        ILogger<ExecuteMacroActionHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public string ActionType => WorkflowConst.ActionExecuteMacro;

    public async Task<bool> ExecuteAsync(WorkflowAction action, Conversation conversation, WorkflowActionContext context)
    {
        try
        {
            if (!int.TryParse(action.Value, out var macroId) || macroId <= 0)
            {
                _logger.LogWarning("工作流 execute_macro 动作缺少有效的宏 ID，会话 {ConversationId}", conversation.Id);
                return false;
            }

            var macroService = _serviceProvider.GetRequiredService<IMacroService>();
            var result = await macroService.ExecuteForWorkflowAsync(macroId, conversation.Id);
            if (!result.Success)
            {
                _logger.LogWarning("工作流执行宏失败: MacroId={MacroId}, ConversationId={ConversationId}, Error={Error}",
                    macroId, conversation.Id, result.Message);
                return false;
            }

            _logger.LogInformation("工作流执行宏成功: MacroId={MacroId}, ConversationId={ConversationId}, Actions={ActionCount}",
                macroId, conversation.Id, result.ActionsExecuted);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "工作流 execute_macro 动作执行异常: MacroValue={MacroValue}, ConversationId={ConversationId}",
                action.Value, conversation.Id);
            return false;
        }
    }
}
