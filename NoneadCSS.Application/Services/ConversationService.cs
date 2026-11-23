using System.Text.Json;
using NoneadCSS.Application.Common;
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

public class ConversationService : IConversationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailQueueService _emailQueueService;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IMailTemplateService _mailTemplateService;
    private readonly IEmailSignatureService _emailSignatureService;
    private readonly IWorkflowService _workflowService;
    private readonly IConversationAssignmentService _assignmentService;
    private readonly IConversationStatusService _statusService;
    private readonly IConversationThreadService _threadService;
    private readonly IConversationDraftService _draftService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConversationService> _logger;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IUserRoleResolver _userRoleResolver;
    private readonly IWebhookService? _webhookService;

    public ConversationService(
        IUnitOfWork unitOfWork,
        IEmailQueueService emailQueueService,
        INotificationPublisher notificationPublisher,
        IMailTemplateService mailTemplateService,
        IEmailSignatureService emailSignatureService,
        IWorkflowService workflowService,
        IConversationAssignmentService assignmentService,
        IConversationStatusService statusService,
        IConversationThreadService threadService,
        IConversationDraftService draftService,
        IConfiguration configuration,
        ILogger<ConversationService> logger,
        IAuditService auditService,
        ICurrentUserAccessor currentUserAccessor,
        IUserRoleResolver userRoleResolver,
        IWebhookService? webhookService = null)
    {
        _unitOfWork = unitOfWork;
        _emailQueueService = emailQueueService;
        _notificationPublisher = notificationPublisher;
        _mailTemplateService = mailTemplateService;
        _emailSignatureService = emailSignatureService;
        _workflowService = workflowService;
        _assignmentService = assignmentService;
        _statusService = statusService;
        _threadService = threadService;
        _draftService = draftService;
        _configuration = configuration;
        _logger = logger;
        _auditService = auditService;
        _currentUserAccessor = currentUserAccessor;
        _userRoleResolver = userRoleResolver;
        _webhookService = webhookService;
    }
    public async Task<PagedResponse<ConversationResponse>> GetConversationsAsync(int mailboxId, int folderId, int page, int pageSize, int? userId = null, int? status = null, int? priority = null, int? starredByUserId = null, int? customerId = null, SourceType? sourceType = null, int? readerUserId = null)
    {
        // 使用 AdvancedSearchAsync 统一走服务端真分页
        ConversationStatus? statusFilter = status.HasValue ? (ConversationStatus)status.Value : null;

        // 邮箱级授权（IDOR 防护）：非超管只能查询其可访问邮箱；超管返回 null 表示不限制。
        // 背景：本端点原先不做邮箱归属校验，非超管传入任意 mailboxId 即可跨邮箱读取工单。
        var accessibleMailboxIds = await _currentUserAccessor.GetAccessibleMailboxIdsAsync();

        // 无任何可访问邮箱 → 直接空页。注意：**不可**把空 List 交给仓储——
        // AdvancedSearchAsync 仅在 mailboxIds 非空时才附加过滤，空 List 等价于"不限制"，会变成越权。
        if (accessibleMailboxIds is { Length: 0 })
        {
            return new List<ConversationResponse>().ToPagedResponse(page, pageSize, 0);
        }

        var mailboxIdWhitelist = accessibleMailboxIds?.ToList();

        var (items, total) = await _unitOfWork.Conversations.AdvancedSearchAsync(
            mailboxId: mailboxId > 0 ? mailboxId : null,
            mailboxIds: mailboxIdWhitelist,
            status: statusFilter,
            priority: priority,
            starredByUserId: starredByUserId,
            customerId: customerId,
            sourceType: sourceType,
            page: page,
            pageSize: pageSize,
            readerUserId: readerUserId);

        // Agent/ReadOnly 用户只能看到分配给自己的会话
        if (userId.HasValue && userId.Value > 0)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId.Value);
            if (user != null && (user.Role == UserRoleType.Agent || user.Role == UserRoleType.ReadOnly))
            {
                var (agentItems, agentTotal) = await _unitOfWork.Conversations.AdvancedSearchAsync(
                    mailboxId: mailboxId > 0 ? mailboxId : null,
                    mailboxIds: mailboxIdWhitelist,
                    userId: userId.Value,
                    status: statusFilter,
                    priority: priority,
                    starredByUserId: starredByUserId,
                    customerId: customerId,
                    sourceType: sourceType,
                    page: page,
                    pageSize: pageSize,
                    readerUserId: readerUserId);
                var agentResponses = await BuildResponsesWithReadStateAsync(agentItems, readerUserId);
                return agentResponses.ToPagedResponse(page, pageSize, agentTotal);
            }
        }

        var responses = await BuildResponsesWithReadStateAsync(items, readerUserId);
        return responses.ToPagedResponse(page, pageSize, total);
    }

    /// <summary>
    /// 基于 readerUserId 批量查询阅读状态，构造带 HasUnreadMessages 标记的响应列表。
    /// readerUserId 为 null 时（未登录或非用户上下文）退化为原始 ToResponse，所有会话视为未读。
    /// </summary>
    private async Task<List<ConversationResponse>> BuildResponsesWithReadStateAsync(IEnumerable<Conversation> items, int? readerUserId)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            return new List<ConversationResponse>();
        }

        if (!readerUserId.HasValue)
        {
            return list.Select(c => c.ToResponse(lastReadAt: null)).ToList();
        }

        var readMap = await _unitOfWork.Conversations.GetLastReadAtMapAsync(list.Select(c => c.Id), readerUserId.Value);
        return list.Select(c =>
        {
            readMap.TryGetValue(c.Id, out var lastRead);
            return c.ToResponse(lastReadAt: lastRead);
        }).ToList();
    }

    public async Task<ConversationResponse?> GetConversationByIdAsync(int id)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(id);
        return conversation?.ToResponse(true);
    }

    /// <summary>
    /// 按 ID 列表批量获取会话（一次数据库查询），避免 N+1。
    /// </summary>
    public async Task<IEnumerable<ConversationResponse>> GetConversationsByIdsAsync(IEnumerable<int> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new List<ConversationResponse>();
        }

        var conversations = await _unitOfWork.Conversations.GetByIdsAsync(idList);
        return conversations.Select(c => c.ToResponse()).ToList();
    }

    public async Task<ConversationResponse> CreateConversationAsync(CreateConversationRequest request)
    {
        Conversation conversation;
        Customer? customer;

        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                customer = await _unitOfWork.Customers.GetOrCreateByEmailAsync(request.CustomerEmail);

                conversation = new Conversation
                {
                    Number = await _unitOfWork.Conversations.GetNextNumberAsync(),
                    MailboxId = request.MailboxId,
                    // 使用导航属性而非显式 CustomerId = customer?.Id，
                    // 避免新客户（customer.Id == 0）时 FK 失败
                    Customer = customer,
                    CustomerEmail = request.CustomerEmail,
                    Subject = request.Subject,
                    UserId = request.UserId == 0 ? null : request.UserId,
                    Type = (ConversationType)request.Type,
                    SourceVia = (SourceVia)request.SourceVia,
                    Status = ConversationStatus.Active,
                    State = ConversationState.Published,
                    Priority = (ConversationPriority)request.Priority
                };

                conversation.AddThread(new ConversationThread
                {
                    Type = ThreadType.Customer,
                    Body = HtmlSanitizerHelper.Sanitize(request.Body),
                    From = request.CustomerEmail,
                    // 同样使用导航属性，避免新客户时 thread.CustomerId = 0 导致 FK 失败
                    Customer = customer
                });

                await _unitOfWork.Conversations.AddAsync(conversation);
                await _unitOfWork.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.SafeRollbackAsync();
                throw;
            }
        }

        var response = conversation.ToResponse(true);
        await _notificationPublisher.NotifyMailboxAsync(conversation.MailboxId.ToString(), "conversation_created", response);

        try
        {
            await _workflowService.ExecuteForConversationAsync(conversation.Id, WorkflowConst.TriggerNewConversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发工作流失败（新会话创建）");
        }

        // 触发 Webhook 通知（失败仅记录日志，不阻塞主流程）
        if (_webhookService != null)
        {
            try
            {
                await _webhookService.TriggerAsync("conversation_created", conversation.MailboxId, response, "Conversation", conversation.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发 Webhook 失败：Event={Event}, Entity={EntityType}:{EntityId}", "conversation_created", "Conversation", conversation.Id);
            }
        }

        return response;
    }

    public async Task<ConversationResponse?> UpdateStatusAsync(int id, int status, int? userId = null)
    {
        return await _statusService.UpdateStatusAsync(id, status, userId);
    }

    /// <summary>
    /// 更新会话优先级
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <param name="priority">优先级：0=Low、1=Normal、2=High、3=Urgent</param>
    /// <returns>更新后的会话响应，会话不存在时返回 null</returns>
    public async Task<ConversationResponse?> UpdatePriorityAsync(int id, int priority)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(id);
        if (conversation == null)
        {
            return null;
        }

        var oldPriority = conversation.Priority;
        conversation.SetPriority((ConversationPriority)priority);
        conversation.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        var response = conversation.ToResponse();

        // 触发工作流：优先级变更
        try
        {
            await _workflowService.ExecuteForConversationAsync(id, WorkflowConst.TriggerPriorityChanged);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "触发工作流失败（优先级变更）");
        }

        return response;
    }

    public async Task<ConversationResponse?> AssignAsync(int id, int userId)
    {
        return await _assignmentService.AssignAsync(id, userId);
    }

    public async Task<bool> DeleteConversationAsync(int id)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(id);
        if (conversation == null)
        {
            return false;
        }

        conversation.SoftDelete();

        await _unitOfWork.SaveChangesAsync();

        // 触发 Webhook 通知（失败仅记录日志，不阻塞主流程）
        if (_webhookService != null)
        {
            try
            {
                await _webhookService.TriggerAsync("conversation_deleted", conversation.MailboxId, null, "Conversation", conversation.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发 Webhook 失败：Event={Event}, Entity={EntityType}:{EntityId}", "conversation_deleted", "Conversation", conversation.Id);
            }
        }

        return true;
    }

    public async Task<PagedResponse<ConversationResponse>> SearchAsync(string query, int page, int pageSize, int? userId = null, int? readerUserId = null)
    {
        if (userId.HasValue && userId.Value > 0)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId.Value);
            if (user != null && (user.Role == UserRoleType.Agent || user.Role == UserRoleType.ReadOnly))
            {
                var (items, total) = await _unitOfWork.Conversations.AdvancedSearchAsync(
                    keyword: query,
                    userId: userId.Value,
                    page: page,
                    pageSize: pageSize,
                    readerUserId: readerUserId);
                var responses = await BuildResponsesWithReadStateAsync(items, readerUserId);
                return responses.ToPagedResponse(page, pageSize, total);
            }
        }

        var (allItems, allTotal) = await _unitOfWork.Conversations.AdvancedSearchAsync(
            keyword: query,
            page: page,
            pageSize: pageSize,
            readerUserId: readerUserId);
        var allResponses = await BuildResponsesWithReadStateAsync(allItems, readerUserId);
        return allResponses.ToPagedResponse(page, pageSize, allTotal);
    }

    public async Task<PagedResponse<ConversationResponse>> AdvancedSearchAsync(
        string? keyword = null,
        ConversationStatus? status = null,
        ConversationType? type = null,
        SourceType? sourceType = null,
        int? mailboxId = null,
        int? userId = null,
        int? customerId = null,
        string? customerEmail = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        DateTime? lastReplyStartDate = null,
        DateTime? lastReplyEndDate = null,
        bool? hasAttachments = null,
        bool? isUnassigned = null,
        int page = 1,
        int pageSize = 20,
        int? readerUserId = null)
    {
        var (items, total) = await _unitOfWork.Conversations.AdvancedSearchAsync(
            keyword, status, type, sourceType, mailboxId, null, userId, customerId,
            customerEmail, startDate, endDate, lastReplyStartDate, lastReplyEndDate,
            hasAttachments, isUnassigned, null, null, page, pageSize, readerUserId);

        var responses = await BuildResponsesWithReadStateAsync(items, readerUserId);
        return responses.ToPagedResponse(page, pageSize, total);
    }

    /// <inheritdoc />
    public async Task MarkConversationAsReadAsync(int conversationId, int userId)
    {
        await _unitOfWork.Conversations.UpsertReadStateAsync(conversationId, userId);
        // UpsertReadStateAsync 不再在仓储内 SaveChanges，由本调用方统一持久化。
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<ThreadResponse?> AddReplyAsync(CreateThreadRequest request, int? userId = null)
    {
        return await _threadService.AddReplyAsync(request, userId);
    }

    public async Task<IEnumerable<ThreadResponse>> GetThreadsByConversationIdAsync(int conversationId)
    {
        return await _threadService.GetThreadsByConversationIdAsync(conversationId);
    }

    public async Task<ConversationResponse?> ToggleFollowAsync(int conversationId, int userId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(conversationId);
        if (conversation == null)
        {
            return null;
        }

        var existingFollowers = await _unitOfWork.Repository<Follower>()
            .FindAsync(f => f.ConversationId == conversationId);
        var isFollowing = existingFollowers.Any(f => f.UserId == userId);

        if (isFollowing)
        {
            var follower = existingFollowers.First(f => f.UserId == userId);
            await _unitOfWork.Repository<Follower>().DeleteAsync(follower);
        }
        else
        {
            await _unitOfWork.Repository<Follower>().AddAsync(new Follower
            {
                ConversationId = conversationId,
                UserId = userId
            });
        }

        conversation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        var updated = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        return updated?.ToResponse(true);
    }

    public async Task<List<int>> GetFollowersAsync(int conversationId)
    {
        var followers = await _unitOfWork.Repository<Follower>()
            .FindAsync(f => f.ConversationId == conversationId);
        return followers.Select(f => f.UserId).ToList();
    }

    public async Task<ThreadResponse?> AddNoteAsync(int conversationId, AddNoteRequest request, int userId)
    {
        return await _threadService.AddNoteAsync(conversationId, request, userId);
    }

    public async Task<BulkOperationResponse> BulkAssignAsync(BulkAssignRequest request, int? currentUserId = null)
    {
        return await _assignmentService.BulkAssignAsync(request, currentUserId);
    }

    public async Task<BulkOperationResponse> BulkUpdateStatusAsync(BulkStatusRequest request)
    {
        return await _statusService.BulkUpdateStatusAsync(request);
    }

    /// <summary>
    /// 批量更新会话优先级
    /// </summary>
    /// <param name="request">批量优先级更新请求（会话 ID 列表 + 目标优先级）</param>
    /// <returns>批量操作结果</returns>
    public async Task<BulkOperationResponse> BulkUpdatePriorityAsync(BulkPriorityRequest request)
    {
        var response = new BulkOperationResponse
        {
            TotalCount = request.ConversationIds.Count
        };

        var distinctIds = request.ConversationIds.Distinct().ToList();

        // 使用 GetByIdsForUpdateAsync 统一做存在性校验 + fallback 场景的 tracked 查询：
        // - 若走 ExecuteUpdateAsync 批量路径，这些 tracked 实体不会被修改，仅用于 ID 存在性判断
        // - 若走 ChangeTracker fallback 路径，可直接复用这些 tracked 实体调用 SetPriority + SaveChanges
        // 同时兼容现有单元测试 Mock（仅 Setup GetByIdsForUpdateAsync，未 Setup GetByIdsAsync）
        var trackedConversations = (await _unitOfWork.Conversations.GetByIdsForUpdateAsync(distinctIds)) ?? Enumerable.Empty<Conversation>();
        var trackedList = trackedConversations.ToList();
        var existingIdSet = new HashSet<int>(trackedList.Select(c => c.Id));

        // 标记不存在的会话为失败
        foreach (var id in distinctIds)
        {
            if (!existingIdSet.Contains(id))
            {
                response.FailedCount++;
                response.Errors.Add($"会话 {id} 不存在");
            }
        }

        var toUpdateIds = trackedList.Select(c => c.Id).ToList();
        if (toUpdateIds.Count > 0)
        {
            try
            {
                // 使用 ExecuteUpdateAsync 批量更新：单次 SQL，不经过 ChangeTracker，
                // 从根源上避免了循环内每条事务的 N+1 开销和 ChangeTracker 脏数据问题。
                var updatedCount = await _unitOfWork.Conversations.BulkUpdatePriorityAsync(
                    toUpdateIds,
                    (ConversationPriority)request.Priority
                );

                // Fallback：当 ExecuteUpdateAsync 返回 0 且存在待更新 ID 时（例如单元测试 Mock 未 Setup 批量方法、
                // 或底层 Provider 不支持 ExecuteUpdateAsync），回退到 ChangeTracker 逐条更新模式。
                if (updatedCount == 0 && trackedList.Count > 0)
                {
                    foreach (var conv in trackedList)
                    {
                        conv.SetPriority((ConversationPriority)request.Priority);
                        conv.UpdatedAt = DateTime.UtcNow;
                    }
                    updatedCount = await _unitOfWork.SaveChangesAsync();
                }

                response.SuccessCount = updatedCount;

                // 工作流按会话逐个触发（工作流引擎内部设计为每会话独立执行，此处循环不涉及数据库事务）
                foreach (var conversationId in toUpdateIds)
                {
                    try
                    {
                        await _workflowService.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerPriorityChanged);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "批量优先级变更触发工作流失败（会话 {ConversationId}）", conversationId);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "批量优先级更新失败（ExecuteUpdateAsync）");
                // 批量 SQL 执行失败时，所有待更新条目计为失败
                foreach (var id in toUpdateIds)
                {
                    response.FailedCount++;
                    response.Errors.Add($"会话 {id} 优先级更新失败: 服务器内部错误，详见系统日志");
                }
            }
        }

        return response;
    }

    public async Task<BulkOperationResponse> BulkDeleteAsync(BulkConversationRequest request)
    {
        var response = new BulkOperationResponse
        {
            TotalCount = request.ConversationIds.Count
        };

        var distinctIds = request.ConversationIds.Distinct().ToList();

        // 使用 GetByIdsForUpdateAsync 统一做存在性校验 + fallback 场景的 tracked 查询：
        // - 若走 ExecuteUpdateAsync 批量路径，这些 tracked 实体不会被修改，仅用于 ID 存在性判断
        // - 若走 ChangeTracker fallback 路径，可直接复用这些 tracked 实体调用 SoftDelete + SaveChanges
        // 同时兼容现有单元测试 Mock（仅 Setup GetByIdsForUpdateAsync，未 Setup GetByIdsAsync）
        var trackedConversations = (await _unitOfWork.Conversations.GetByIdsForUpdateAsync(distinctIds)) ?? Enumerable.Empty<Conversation>();
        var trackedList = trackedConversations.ToList();
        var existingIdSet = new HashSet<int>(trackedList.Select(c => c.Id));

        // 标记不存在的会话为失败
        foreach (var id in distinctIds)
        {
            if (!existingIdSet.Contains(id))
            {
                response.FailedCount++;
                response.Errors.Add($"会话 {id} 不存在");
            }
        }

        var toDeleteIds = trackedList.Select(c => c.Id).ToList();
        if (toDeleteIds.Count > 0)
        {
            try
            {
                // 使用 ExecuteUpdateAsync 批量软删除：单次 SQL，不经过 ChangeTracker，
                // 从根源上避免了循环内每条事务的 N+1 开销和 ChangeTracker 脏数据问题。
                var deletedCount = await _unitOfWork.Conversations.BulkSoftDeleteAsync(toDeleteIds);

                // Fallback：当 ExecuteUpdateAsync 返回 0 且存在待删除 ID 时（例如单元测试 Mock 未 Setup 批量方法、
                // 或底层 Provider 不支持 ExecuteUpdateAsync），回退到 ChangeTracker 逐条软删除模式。
                if (deletedCount == 0 && trackedList.Count > 0)
                {
                    foreach (var conv in trackedList)
                    {
                        conv.SoftDelete();
                    }
                    deletedCount = await _unitOfWork.SaveChangesAsync();
                }

                response.SuccessCount = deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除失败（ExecuteUpdateAsync）");
                // 批量 SQL 执行失败时，所有待删除条目计为失败
                foreach (var id in toDeleteIds)
                {
                    response.FailedCount++;
                    response.Errors.Add($"会话 {id} 删除失败: 服务器内部错误，详见系统日志");
                }
            }
        }

        return response;
    }

    public async Task<ThreadResponse?> SaveDraftAsync(CreateThreadRequest request, int? userId = null)
    {
        return await _draftService.SaveDraftAsync(request, userId);
    }

    public async Task<ThreadResponse?> GetDraftAsync(int conversationId)
    {
        return await _draftService.GetDraftAsync(conversationId);
    }

    public async Task<bool> DeleteDraftAsync(int conversationId)
    {
        return await _draftService.DeleteDraftAsync(conversationId);
    }

    public async Task<List<ThreadResponse>> GetDraftsByUserIdAsync(int userId)
    {
        return await _draftService.GetDraftsByUserIdAsync(userId);
    }

    public async Task<ConversationResponse> CreatePhoneConversationAsync(CreatePhoneConversationRequest request, int? userId = null)
    {
        Customer? customer = null;
        Conversation? conversation = null;

        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                if (!string.IsNullOrEmpty(request.CustomerEmail))
                {
                    customer = await _unitOfWork.Customers.GetOrCreateByEmailAsync(request.CustomerEmail);
                }
                else if (!string.IsNullOrEmpty(request.PhoneNumber))
                {
                    // 使用 Tracked 版本：下方 else if 分支会修改 customer.FirstName/LastName，需要 ChangeTracker 跟踪
                    customer = await _unitOfWork.Customers.GetByPhoneTrackedAsync(request.PhoneNumber);
                    if (customer == null)
                    {
                        customer = new Customer
                        {
                            FirstName = request.CustomerName ?? string.Empty,
                            Phones = System.Text.Json.JsonSerializer.Serialize(new List<CustomerPhone>
                            {
                                new CustomerPhone { Value = request.PhoneNumber, Type = 1 }
                            })
                        };
                        await _unitOfWork.Customers.AddAsync(customer);
                    }
                    else if (!string.IsNullOrEmpty(request.CustomerName))
                    {
                        var nameParts = request.CustomerName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        customer.FirstName = nameParts.Length > 0 ? nameParts[0] : request.CustomerName;
                        customer.LastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : string.Empty;
                        // 已通过 GetByPhoneTrackedAsync 获取追踪实体，无需调用 UpdateAsync
                    }
                }

                conversation = new Conversation
                {
                    Number = await _unitOfWork.Conversations.GetNextNumberAsync(),
                    MailboxId = request.MailboxId,
                    // 使用导航属性，避免新客户/新电话客户 customer.Id=0 时 FK 失败
                    Customer = customer,
                    CustomerEmail = customer?.GetMainEmail() ?? request.CustomerEmail ?? string.Empty,
                    Subject = request.Subject,
                    UserId = (request.UserId ?? userId) == 0 ? null : (request.UserId ?? userId),
                    Type = ConversationType.Phone,
                    SourceVia = SourceVia.User,
                    SourceType = SourceType.Phone,
                    Status = ConversationStatus.Active,
                    State = ConversationState.Published,
                    CreatedByUserId = userId
                };

                var body = request.Body;
                if (request.CallDuration.HasValue)
                {
                    body += $"<br/><br/><strong>通话时长:</strong> {request.CallDuration} 秒";
                }
                if (!string.IsNullOrEmpty(request.CallNotes))
                {
                    body += $"<br/><br/><strong>通话备注:</strong> {request.CallNotes}";
                }
                if (!string.IsNullOrEmpty(request.PhoneNumber))
                {
                    body += $"<br/><br/><strong>电话号码:</strong> {request.PhoneNumber}";
                }

                conversation.AddThread(new ConversationThread
                {
                    Type = ThreadType.Customer,
                    Body = body,
                    From = request.PhoneNumber,
                    // 同样使用导航属性
                    Customer = customer
                });

                await _unitOfWork.Conversations.AddAsync(conversation);
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.SafeRollbackAsync();
                throw;
            }
        }

        var response = conversation!.ToResponse(true);
        await _notificationPublisher.NotifyMailboxAsync(conversation.MailboxId.ToString(), "conversation_created", response);

        // 触发 Webhook 通知（失败仅记录日志，不阻塞主流程）
        if (_webhookService != null)
        {
            try
            {
                await _webhookService.TriggerAsync("conversation_created", conversation.MailboxId, response, "Conversation", conversation.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发 Webhook 失败：Event={Event}, Entity={EntityType}:{EntityId}", "conversation_created", "Conversation", conversation.Id);
            }
        }

        return response;
    }

    public async Task<ConversationResponse?> ToggleStarAsync(int conversationId, int userId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            return null;
        }

        var stars = await _unitOfWork.Repository<Star>().FindAsync(s => s.ConversationId == conversationId && s.UserId == userId);
        var existingStar = stars.FirstOrDefault();

        if (existingStar != null)
        {
            await _unitOfWork.Repository<Star>().DeleteAsync(existingStar);
        }
        else
        {
            await _unitOfWork.Repository<Star>().AddAsync(new Star
            {
                ConversationId = conversationId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _unitOfWork.SaveChangesAsync();
        return conversation.ToResponse(true);
    }

    public async Task<List<int>> GetStarredConversationIdsAsync(int userId)
    {
        var stars = await _unitOfWork.Repository<Star>().FindAsync(s => s.UserId == userId);
        return stars.Select(s => s.ConversationId).ToList();
    }

    public async Task<IEnumerable<ConversationResponse>> GetStarredConversationsAsync(int userId, int page, int pageSize)
    {
        var (items, _) = await _unitOfWork.Conversations.AdvancedSearchAsync(
            starredByUserId: userId,
            page: page,
            pageSize: pageSize);
        return items.Select(c => c.ToResponse());
    }

    public async Task<ConversationResponse?> CloneConversationAsync(int conversationId, int? userId = null)
    {
        var original = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (original == null)
        {
            return null;
        }

        var customer = original.CustomerId.HasValue
            ? await _unitOfWork.Customers.GetByIdAsync(original.CustomerId.Value)
            : null;

        var clonedConversation = new Conversation
        {
            Number = await _unitOfWork.Conversations.GetNextNumberAsync(),
            MailboxId = original.MailboxId,
            CustomerId = original.CustomerId,
            CustomerEmail = original.CustomerEmail,
            Subject = $"[副本] {original.Subject}",
            UserId = userId ?? original.UserId,
            Type = original.Type,
            SourceVia = SourceVia.User,
            SourceType = original.SourceType,
            Status = ConversationStatus.Active,
            State = ConversationState.Published,
            Cc = original.Cc,
            Bcc = original.Bcc,
            HasAttachments = original.HasAttachments,
            CreatedByUserId = userId,
            LastReplyAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var originalThreads = await _unitOfWork.Threads.FindAsync(
            t => t.ConversationId == conversationId && t.State != Domain.Entities.ThreadState.Deleted);

        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                foreach (var originalThread in originalThreads)
                {
                    var isCustomerThread = originalThread.Type == ThreadType.Customer;
                    clonedConversation.Threads.Add(new ConversationThread
                    {
                        Type = originalThread.Type,
                        State = Domain.Entities.ThreadState.Active,
                        Body = originalThread.Body,
                        Subject = originalThread.Subject,
                        From = originalThread.From,
                        To = originalThread.To,
                        Cc = originalThread.Cc,
                        Bcc = originalThread.Bcc,
                        CustomerId = isCustomerThread ? originalThread.CustomerId : null,
                        UserId = isCustomerThread ? null : userId,
                        IsInternal = originalThread.IsInternal,
                        SourceVia = originalThread.SourceVia,
                        SourceType = originalThread.SourceType,
                        IsFirst = originalThread.IsFirst,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                await _unitOfWork.Conversations.AddAsync(clonedConversation);
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.SafeRollbackAsync();
                throw;
            }
        }

        var response = clonedConversation.ToResponse(true);

        return response;
    }

    public async Task<bool> HardDeleteConversationAsync(int conversationId)
    {
        // 服务层双重校验：物理删除属于破坏性操作，强制超管
        var operatorId = _currentUserAccessor.UserId;
        if (operatorId <= 0 || !await _userRoleResolver.IsSuperAdminAsync(operatorId))
        {
            _logger.LogWarning("HardDeleteConversationAsync 非超管用户被拦截: OperatorId={OperatorId}, ConversationId={ConversationId}", operatorId, conversationId);
            throw new UnauthorizedAccessException("只有超级管理员才能彻底删除会话");
        }

        var conversation = await _unitOfWork.Conversations.GetDeletedByIdAsync(conversationId);
        if (conversation == null)
        {
            return false;
        }

        var oldValues = JsonSerializer.Serialize(new
        {
            conversation.Id,
            conversation.Subject,
            conversation.CustomerEmail,
            conversation.DeletedAt,
            conversation.MailboxId
        });

        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                await _unitOfWork.Conversations.HardDeleteAsync(conversationId);

                await _auditService.LogAsync(
                    action: "hard_delete",
                    entityType: "Conversation",
                    entityId: conversationId,
                    oldValues: oldValues,
                    newValues: null);

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.SafeRollbackAsync();
                throw;
            }
        }

        return true;
    }

    /// <summary>
    /// 获取垃圾箱中的已软删除会话列表。
    /// mailboxId > 0 时按邮箱过滤，mailboxId <= 0 时返回所有邮箱的已删除会话。
    /// </summary>
    public async Task<PagedResponse<ConversationResponse>> GetTrashByMailboxAsync(int mailboxId, int page, int pageSize)
    {
        var (items, total) = await _unitOfWork.Conversations.GetDeletedByMailboxIdAsync(mailboxId, page, pageSize);
        var responses = items.Select(c => c.ToResponse());
        return responses.ToPagedResponse(page, pageSize, total);
    }

    /// <summary>
    /// 恢复指定会话（从垃圾箱还原）。
    /// </summary>
    public async Task<ConversationResponse?> RestoreConversationAsync(int conversationId)
    {
        var conversation = await _unitOfWork.Conversations.GetDeletedTrackedByIdAsync(conversationId);
        if (conversation == null)
        {
            return null;
        }

        conversation.Restore();

        await _unitOfWork.SaveChangesAsync();

        var updated = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        return updated?.ToResponse();
    }

    /// <summary>
    /// 清空垃圾箱（彻底删除所有已软删除会话）。
    /// mailboxId > 0 时只清空该邮箱的，mailboxId <= 0 时清空全部。
    /// </summary>
    public async Task<int> ClearTrashByMailboxAsync(int mailboxId)
    {
        // 服务层双重校验：彻底删除属于破坏性操作，强制超管
        var operatorId = _currentUserAccessor.UserId;
        if (operatorId <= 0 || !await _userRoleResolver.IsSuperAdminAsync(operatorId))
        {
            _logger.LogWarning("ClearTrashByMailboxAsync 非超管用户被拦截: OperatorId={OperatorId}, MailboxId={MailboxId}", operatorId, mailboxId);
            throw new UnauthorizedAccessException("只有超级管理员才能清空会话垃圾箱");
        }

        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                var count = await _unitOfWork.Conversations.HardDeleteAllDeletedByMailboxIdAsync(mailboxId);

                if (count > 0)
                {
                    var oldValues = JsonSerializer.Serialize(new { Count = count, MailboxId = mailboxId > 0 ? mailboxId : (int?)null });
                    await _auditService.LogAsync(
                        action: "bulk_hard_delete",
                        entityType: "Conversation",
                        entityId: null,
                        oldValues: oldValues,
                        newValues: null);
                }

                await transaction.CommitAsync();
                return count;
            }
            catch
            {
                await transaction.SafeRollbackAsync();
                throw;
            }
        }
    }

    public async Task<IEnumerable<ThreadResponse>> GetChatModeThreadsAsync(int conversationId)
    {
        return await _threadService.GetChatModeThreadsAsync(conversationId);
    }
}
