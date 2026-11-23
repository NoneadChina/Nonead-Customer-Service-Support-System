using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Application.Services;

public class ConversationAssignmentService : IConversationAssignmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFolderService _folderService;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IEmailQueueService _emailQueueService;
    private readonly IWorkflowService _workflowService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConversationAssignmentService> _logger;
    private readonly IDomainEventDispatcher? _domainEventDispatcher;

    public ConversationAssignmentService(
        IUnitOfWork unitOfWork,
        IFolderService folderService,
        INotificationPublisher notificationPublisher,
        IEmailQueueService emailQueueService,
        IWorkflowService workflowService,
        IConfiguration configuration,
        ILogger<ConversationAssignmentService> logger,
        IDomainEventDispatcher? domainEventDispatcher = null)
    {
        _unitOfWork = unitOfWork;
        _folderService = folderService;
        _notificationPublisher = notificationPublisher;
        _emailQueueService = emailQueueService;
        _workflowService = workflowService;
        _configuration = configuration;
        _logger = logger;
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

    public async Task<ConversationResponse?> AssignAsync(int id, int userId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(id);
        if (conversation == null)
        {
            return null;
        }

        var oldAssigneeId = conversation.UserId;

        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                conversation.AssignTo(userId);
                await _folderService.UpdateConversationFolderAsync(conversation);
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.SafeRollbackAsync();
                _logger.LogError(ex,
                    "分配会话失败: ConversationId={ConversationId}, OldAssigneeId={OldAssigneeId}, NewUserId={NewUserId}",
                    id, oldAssigneeId, userId);
                throw;
            }
        }

        conversation = await _unitOfWork.Conversations.GetByIdAsync(id) ?? conversation;

        // 发送分配实时通知
        var assignResponse = conversation.ToResponse();
        await _notificationPublisher.NotifyUserAsync(userId.ToString(), "conversation_assigned", assignResponse);

        // 发布强类型领域事件：由 ConversationUserChangedListener 给新受理人发站内通知。
        // 审计 action="assign" 由 AuditActionFilter 写入、SignalR 由上面的 NotifyUserAsync 推送，
        // 监听器已精简为只做站内通知，避免重复副作用。必须在事务提交之后发布：
        // 监听器用自己的 Scope 与 DbContext，读不到未提交的数据。
        // AssignAsync 没有操作者参数，ChangedByUserId 保持为 null。
        await DispatchDomainEventAsync(new ConversationUserChangedEvent
        {
            Conversation = conversation,
            OldUserId = oldAssigneeId,
            NewUserId = userId,
            ChangedByUserId = null
        });

        // 触发工作流：分配变更
        try
        {
            await _workflowService.ExecuteForConversationAsync(id, WorkflowConst.TriggerAssigneeChanged);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发工作流失败（分配变更）");
        }

        return assignResponse;
    }

    public async Task<BulkOperationResponse> BulkAssignAsync(BulkAssignRequest request, int? currentUserId = null)
    {
        _logger.LogInformation("批量分配会话: {Count} 个会话分配给用户 {TargetUserId}, 操作者: {OperatorId}",
            request.ConversationIds.Count, request.UserId, currentUserId);

        var response = new BulkOperationResponse
        {
            TotalCount = request.ConversationIds.Count
        };

        var updatedIds = new List<int>();

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

                using (var transaction = await _unitOfWork.BeginTransactionAsync())
                {
                    try
                    {
                        conversation.AssignTo(request.UserId);
                        await _folderService.UpdateConversationFolderAsync(conversation);
                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();
                        response.SuccessCount++;
                        updatedIds.Add(conversationId);
                    }
                    catch
                    {
                        await transaction.SafeRollbackAsync();
                        // 事务回滚后清理 ChangeTracker，防止已回滚的实体（Modified 状态）被后续
                        // 迭代的 SaveChangesAsync 重新写入数据库；并发冲突时避免单个失败级联。
                        _unitOfWork.ClearChanges();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量分配失败（会话 {ConversationId}）", conversationId);
                response.FailedCount++;
                response.Errors.Add($"会话 {conversationId} 分配失败: 服务器内部错误，详见系统日志");
            }
        }

        foreach (var conversationId in updatedIds)
        {
            try
            {
                await _workflowService.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerAssigneeChanged);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量分配触发工作流失败（会话 {ConversationId}）", conversationId);
            }
        }

        return response;
    }
}
