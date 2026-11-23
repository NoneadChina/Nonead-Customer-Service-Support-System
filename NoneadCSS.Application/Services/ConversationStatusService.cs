using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Application.Templates;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Application.Services;

public class ConversationStatusService : IConversationStatusService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFolderService _folderService;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IEmailQueueService _emailQueueService;
    private readonly IWorkflowService _workflowService;
    private readonly IConfiguration _configuration;
    private readonly ISatisfactionTokenGenerator _satisfactionTokenGenerator;
    private readonly IFrontendUrlResolver _frontendUrlResolver;
    private readonly ILogger<ConversationStatusService> _logger;
    private readonly ITemplateRenderer _renderer;
    private readonly IWebhookService? _webhookService;
    private readonly IDomainEventDispatcher? _domainEventDispatcher;

    public ConversationStatusService(
        IUnitOfWork unitOfWork,
        IFolderService folderService,
        INotificationPublisher notificationPublisher,
        IEmailQueueService emailQueueService,
        IWorkflowService workflowService,
        IConfiguration configuration,
        ISatisfactionTokenGenerator satisfactionTokenGenerator,
        IFrontendUrlResolver frontendUrlResolver,
        ILogger<ConversationStatusService> logger,
        ITemplateRenderer renderer,
        IWebhookService? webhookService = null,
        IDomainEventDispatcher? domainEventDispatcher = null)
    {
        _unitOfWork = unitOfWork;
        _folderService = folderService;
        _notificationPublisher = notificationPublisher;
        _emailQueueService = emailQueueService;
        _workflowService = workflowService;
        _configuration = configuration;
        _satisfactionTokenGenerator = satisfactionTokenGenerator;
        _frontendUrlResolver = frontendUrlResolver;
        _logger = logger;
        _renderer = renderer;
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

    public async Task<ConversationResponse?> UpdateStatusAsync(int id, int status, int? userId = null)
    {
        // 使用跟踪的完整 Include 查询，确保更新后 ToResponse 能拿到导航属性（Customer/User/Tags等）
        var conversation = await _unitOfWork.Conversations.GetByIdTrackedAsync(id);
        if (conversation == null)
        {
            return null;
        }

        var oldStatus = conversation.Status;
        var wasClosed = conversation.Status == ConversationStatus.Closed;
        var mailboxId = conversation.MailboxId;
        var newStatus = (ConversationStatus)status;

        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                conversation.SetStatus(newStatus);

                if (userId.HasValue && conversation.UserId == null)
                {
                    conversation.AssignTo(userId.Value);
                }

                await _folderService.UpdateConversationFolderAsync(conversation);
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.SafeRollbackAsync();
                throw;
            }
        }

        // 直接使用已修改并保存的跟踪对象生成响应，避免 AsNoTracking 重新查询
        // 可能因数据库连接/事务隔离级别等问题读到陈旧状态值
        var statusResponse = conversation.ToResponse();

        // 发送状态变更实时通知
        await _notificationPublisher.NotifyMailboxAsync(mailboxId.ToString(), "conversation_status_changed", statusResponse);

        // 发布强类型领域事件：由 ConversationStatusChangedListener 给会话归属用户发站内通知。
        // 审计（action="status_change"）由 AuditActionFilter 写入、SignalR 由上面的 NotifyMailboxAsync 推送，
        // 监听器已精简为只做站内通知，避免重复副作用。
        // 必须在事务提交之后发布：监听器用自己的 Scope 与 DbContext，读不到未提交的数据。
        await DispatchDomainEventAsync(new ConversationStatusChangedEvent
        {
            Conversation = conversation,
            OldStatus = oldStatus.ToString(),
            NewStatus = newStatus.ToString(),
            ChangedByUserId = userId
        });

        // 触发工作流：状态变更
        try
        {
            await _workflowService.ExecuteForConversationAsync(id, WorkflowConst.TriggerStatusChanged);

            // 如果是关闭操作，额外触发会话关闭触发器
            if (!wasClosed && newStatus == ConversationStatus.Closed)
            {
                await _workflowService.ExecuteForConversationAsync(id, WorkflowConst.TriggerConversationClosed);

                // 关闭会话后发送满意度调查邮件（失败仅记录日志，不阻塞主流程）
                try
                {
                    await SendSatisfactionSurveyAsync(conversation);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送满意度调查邮件失败：ConversationId={Id}", conversation.Id);
                }
            }
            // 如果是从关闭状态重新打开，额外触发会话重新打开触发器
            else if (wasClosed && newStatus != ConversationStatus.Closed)
            {
                await _workflowService.ExecuteForConversationAsync(id, WorkflowConst.TriggerConversationReopened);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发工作流失败（状态变更）");
        }

        // 触发 Webhook 通知（失败仅记录日志，不阻塞主流程）
        if (_webhookService != null)
        {
            try
            {
                await _webhookService.TriggerAsync("conversation_status_changed", mailboxId, statusResponse, "Conversation", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发 Webhook 失败：Event={Event}, Entity={EntityType}:{EntityId}", "conversation_status_changed", "Conversation", id);
            }
        }

        return statusResponse;
    }

    public async Task<BulkOperationResponse> BulkUpdateStatusAsync(BulkStatusRequest request)
    {
        var response = new BulkOperationResponse
        {
            TotalCount = request.ConversationIds.Count
        };

        var statusChanges = new List<(int ConversationId, bool WasClosed, ConversationStatus NewStatus)>();

        var conversations = await _unitOfWork.Conversations.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds);
        var conversationDict = conversations.ToDictionary(c => c.Id);

        foreach (var conversationId in request.ConversationIds)
        {
            try
            {
                if (!conversationDict.TryGetValue(conversationId, out var conversation))
                {
                    response.FailedCount++;
                    response.Errors.Add($"会话 {conversationId} 不存在");
                    continue;
                }

                var wasClosed = conversation.Status == ConversationStatus.Closed;

                using (var transaction = await _unitOfWork.BeginTransactionAsync())
                {
                    try
                    {
                        conversation.SetStatus((ConversationStatus)request.Status);
                        await _folderService.UpdateConversationFolderAsync(conversation);
                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();

                        response.SuccessCount++;
                        statusChanges.Add((conversationId, wasClosed, conversation.Status));
                    }
                    catch
                    {
                        await transaction.SafeRollbackAsync();
                        // 事务回滚后清理 ChangeTracker，防止已回滚的实体（Modified 状态）被后续
                        // 迭代的 SaveChangesAsync 重新写入数据库（数据与响应矛盾）；
                        // 并发冲突时也能避免单个失败级联导致后续全部会话失败。
                        _unitOfWork.ClearChanges();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量状态更新失败（会话 {ConversationId}）", conversationId);
                response.FailedCount++;
                response.Errors.Add($"会话 {conversationId} 状态更新失败: 服务器内部错误，详见系统日志");
            }
        }

        foreach (var (conversationId, wasClosed, newStatus) in statusChanges)
        {
            try
            {
                await _workflowService.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerStatusChanged);

                if (!wasClosed && newStatus == ConversationStatus.Closed)
                {
                    await _workflowService.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerConversationClosed);
                }
                else if (wasClosed && newStatus != ConversationStatus.Closed)
                {
                    await _workflowService.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerConversationReopened);
                }
            }
            catch (Exception ex)
            {
                // 状态变更已持久化但工作流触发失败，记录到响应结果中以便调用方感知并重试
                response.FailedWorkflowConversationIds.Add(conversationId);
                _logger.LogError(ex,
                    "批量状态变更触发工作流失败：ConversationId={ConversationId}, WasClosed={WasClosed}, NewStatus={NewStatus}",
                    conversationId, wasClosed, newStatus);
            }
        }

        return response;
    }

    private async Task SendSatisfactionSurveyAsync(Conversation conversation)
    {
        try
        {
            var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(conversation.MailboxId);
            if (mailbox == null)
            {
                return;
            }

            var templates = await _unitOfWork.MailTemplates.GetActiveByTypeAndMailboxAsync(
                MailTemplateType.SatisfactionSurvey,
                conversation.MailboxId);

            var template = templates.FirstOrDefault();
            if (template == null)
            {
                return;
            }

            // T000111：满意度调查邮件必须发给可识别的客户邮箱。会话 CustomerEmail 列可能为空，
            // 回退到 Customer.Emails 主邮箱；两者皆无则跳过（避免空收件人 + 空评价链接）。
            var customerEmail = conversation.CustomerEmail ?? string.Empty;
            if (string.IsNullOrWhiteSpace(customerEmail) && conversation.CustomerId.HasValue)
            {
                var customer = await _unitOfWork.Customers.GetByIdAsync(conversation.CustomerId.Value);
                customerEmail = customer?.GetMainEmail() ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(customerEmail))
            {
                _logger.LogWarning("发送满意度调查邮件跳过：会话 {ConversationId} 无客户邮箱", conversation.Id);
                return;
            }

            var token = _satisfactionTokenGenerator.GenerateToken(conversation.Id, customerEmail);
            var frontendUrl = _frontendUrlResolver.GetFrontendUrl();
            var satisfactionUrl = $"{frontendUrl}/satisfaction/{token}";

            var variables = new Dictionary<string, string>
            {
                ["company_name"] = _configuration["CompanyName"] ?? "拓德科技",
                ["customer_name"] = customerEmail,
                ["customer_email"] = customerEmail,
                ["subject"] = conversation.Subject ?? string.Empty,
                ["conversation_id"] = conversation.Id.ToString(),
                ["conversation_url"] = $"{frontendUrl}/conversations/{conversation.Id}",
                ["satisfaction_url"] = satisfactionUrl
            };

            var (subject, bodyHtml, _) = await RenderMailTemplateAsync(template, variables);

            await _emailQueueService.EnqueueEmailAsync(
                mailbox.Id,
                customerEmail,
                subject,
                bodyHtml,
                customerId: conversation.CustomerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送满意度调查邮件失败");
        }
    }

    private async Task<(string Subject, string BodyHtml, string BodyText)> RenderMailTemplateAsync(
        MailTemplate template,
        Dictionary<string, string> variables)
    {
        var subject = _renderer.Render(template.Subject ?? string.Empty, variables, new RenderOptions { HtmlEncode = false });
        var bodyHtml = _renderer.Render(template.BodyHtml ?? string.Empty, variables);
        var bodyText = _renderer.Render(template.BodyText ?? string.Empty, variables, new RenderOptions { HtmlEncode = false });

        return (subject, bodyHtml, bodyText);
    }
}
