using Microsoft.EntityFrameworkCore;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Application.Services;

public class ConversationTagService : IConversationTagService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowService _workflowService;
    private readonly ILogger<ConversationTagService> _logger;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public ConversationTagService(
        IUnitOfWork unitOfWork,
        IWorkflowService workflowService,
        ILogger<ConversationTagService> logger,
        ICurrentUserAccessor currentUserAccessor)
    {
        _unitOfWork = unitOfWork;
        _workflowService = workflowService;
        _logger = logger;
        _currentUserAccessor = currentUserAccessor;
    }

    /// <summary>
    /// 会话级访问控制（#21 越权审计修复）：本 Service 的公开方法都按 conversationId 操作，
    /// 此前完全没有用户上下文，任何持有 conversations.manage_tags 的用户都能改任意会话的标签。
    /// 规则：无 HTTP 用户上下文（后台 Job / 工作流系统调用）放行；超管放行；
    /// 其余按会话所属邮箱判定 CanAccessMailboxAsync。
    /// </summary>
    private async Task EnsureCanAccessConversationAsync(int conversationId)
    {
        if (_currentUserAccessor.UserId <= 0) return;
        if (_currentUserAccessor.IsSuperAdmin) return;

        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            throw new InvalidOperationException("会话不存在");
        }

        if (!await _currentUserAccessor.CanAccessMailboxAsync(conversation.MailboxId))
        {
            _logger.LogWarning("拒绝越权访问会话标签：UserId={UserId}, ConversationId={ConversationId}, MailboxId={MailboxId}",
                _currentUserAccessor.UserId, conversationId, conversation.MailboxId);
            throw new UnauthorizedAccessException("无权访问该会话");
        }
    }

    /// <summary>
    /// 批量场景的访问判定（不抛异常，返回 false 由调用方计入失败项）。
    /// </summary>
    private async Task<bool> IsConversationAccessibleAsync(int conversationId)
    {
        if (_currentUserAccessor.UserId <= 0) return true;
        if (_currentUserAccessor.IsSuperAdmin) return true;

        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        return conversation != null && await _currentUserAccessor.CanAccessMailboxAsync(conversation.MailboxId);
    }

    public async Task<List<TagResponse>> GetConversationTagsAsync(int conversationId)
    {
        await EnsureCanAccessConversationAsync(conversationId);
        var tags = await _unitOfWork.Tags.GetByConversationIdAsync(conversationId);
        return tags.Select(t => t.ToResponse()).ToList();
    }

    public async Task<List<TagResponse>> UpdateConversationTagsAsync(int conversationId, UpdateConversationTagsRequest request)
    {
        await EnsureCanAccessConversationAsync(conversationId);
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(conversationId);
        if (conversation == null)
        {
            throw new InvalidOperationException("会话不存在");
        }

        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var existingTags = (await _unitOfWork.Repository<ConversationTag>()
                .FindAsync(ct => ct.ConversationId == conversationId))
                .ToList();

            foreach (var existingTag in existingTags)
            {
                await _unitOfWork.Repository<ConversationTag>().DeleteAsync(existingTag);
            }

            var newTags = await _unitOfWork.Tags.GetByIdsAsync(request.TagIds);
            foreach (var tag in newTags)
            {
                var conversationTag = new ConversationTag
                {
                    ConversationId = conversationId,
                    TagId = tag.Id
                };
                await _unitOfWork.Repository<ConversationTag>().AddAsync(conversationTag);
            }

            conversation.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.SafeRollbackAsync();
            _unitOfWork.ClearChanges();
            throw;
        }

        return (await _unitOfWork.Tags.GetByConversationIdAsync(conversationId))
            .Select(t => t.ToResponse()).ToList();
    }

    public async Task<List<TagResponse>> AddTagToConversationAsync(int conversationId, int tagId)
    {
        await EnsureCanAccessConversationAsync(conversationId);
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(conversationId);
        if (conversation == null)
        {
            throw new InvalidOperationException("会话不存在");
        }

        var tag = await _unitOfWork.Tags.GetByIdAsync(tagId);
        if (tag == null)
        {
            throw new InvalidOperationException("标签不存在");
        }

        bool isNewTag = false;

        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var existing = (await _unitOfWork.Repository<ConversationTag>()
                .FindAsync(ct => ct.ConversationId == conversationId && ct.TagId == tagId))
                .FirstOrDefault();

            if (existing == null)
            {
                var conversationTag = new ConversationTag
                {
                    ConversationId = conversationId,
                    TagId = tagId
                };
                await _unitOfWork.Repository<ConversationTag>().AddAsync(conversationTag);
                isNewTag = true;
            }

            conversation.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.SafeRollbackAsync();
            _unitOfWork.ClearChanges();
            throw;
        }

        if (isNewTag)
        {
            try
            {
                await _workflowService.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerTagAdded);
            }
            catch (Exception ex)
            {
                // 工作流失败不阻塞主操作（标签已在事务中添加），仅记录日志供后续排查。
                _logger.LogError(ex, "触发工作流失败（标签添加）: ConversationId={ConversationId}, TagId={TagId}", conversationId, tagId);
            }
        }

        return (await _unitOfWork.Tags.GetByConversationIdAsync(conversationId))
            .Select(t => t.ToResponse()).ToList();
    }

    public async Task<bool> RemoveTagFromConversationAsync(int conversationId, int tagId)
    {
        await EnsureCanAccessConversationAsync(conversationId);
        var conversationTag = (await _unitOfWork.Repository<ConversationTag>()
            .FindAsync(ct => ct.ConversationId == conversationId && ct.TagId == tagId))
            .FirstOrDefault();

        if (conversationTag == null)
        {
            return false;
        }

        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            await _unitOfWork.Repository<ConversationTag>().DeleteAsync(conversationTag);

            var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(conversationId);
            if (conversation != null)
            {
                conversation.UpdatedAt = DateTime.UtcNow;
            }

            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.SafeRollbackAsync();
            _unitOfWork.ClearChanges();
            throw;
        }

        try
        {
            await _workflowService.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerTagRemoved);
        }
        catch (Exception ex)
        {
            // 工作流失败不阻塞主操作（标签已在事务中移除），仅记录日志供后续排查。
            _logger.LogError(ex, "触发工作流失败（标签移除）: ConversationId={ConversationId}, TagId={TagId}", conversationId, tagId);
        }

        return true;
    }

    public async Task<BulkOperationResponse> BulkUpdateConversationTagsAsync(BulkUpdateConversationTagsRequest request)
    {
        int successCount = 0;
        int failedCount = 0;
        var errors = new List<string>();

        var newTags = await _unitOfWork.Tags.GetByIdsAsync(request.TagIds);

        var conversations = await _unitOfWork.Conversations.GetByIdsForUpdateAsync(request.ConversationIds);
        var conversationDict = conversations.ToDictionary(c => c.Id);

        // ── 预分类：找出不存在的会话（先记录失败，不入事务） ─────────────────
        var validConversationIds = new List<int>();
        foreach (var conversationId in request.ConversationIds)
        {
            if (!conversationDict.TryGetValue(conversationId, out _))
            {
                failedCount++;
                errors.Add($"会话 {conversationId} 不存在");
            }
            else if (await IsConversationAccessibleAsync(conversationId))
            {
                validConversationIds.Add(conversationId);
            }
            else
            {
                failedCount++;
                errors.Add($"会话 {conversationId} 无权访问");
            }
        }

        if (validConversationIds.Count == 0)
        {
            return new BulkOperationResponse
            {
                TotalCount = request.ConversationIds.Count,
                SuccessCount = 0,
                FailedCount = failedCount,
                Errors = errors
            };
        }

        // ── 批量查询所有现有 tags（1 条 SQL，替代会话内 N 次查询） ─────────
        var allExistingTags = await _unitOfWork.Repository<ConversationTag>().GetQueryable()
            .Where(ct => validConversationIds.Contains(ct.ConversationId))
            .ToListAsync();

        // ── 单事务包住所有有效会话的标签删除/重建（原 N 次事务 → 1 次） ────
        // 若部分会话在执行过程中因并发被删除，整体事务回滚并降级为逐条事务重试，
        // 保持与旧实现一致的「会话级部分成功」语义。
        try
        {
            using var transaction = await _unitOfWork.BeginTransactionAsync();
            var now = DateTime.UtcNow;
            var committedIds = new HashSet<int>();

            foreach (var conversationId in validConversationIds)
            {
                if (!conversationDict.TryGetValue(conversationId, out var conversation))
                {
                    failedCount++;
                    errors.Add($"会话 {conversationId} 不存在");
                    continue;
                }

                var existingTags = allExistingTags
                    .Where(ct => ct.ConversationId == conversationId)
                    .ToList();

                foreach (var existingTag in existingTags)
                {
                    await _unitOfWork.Repository<ConversationTag>().DeleteAsync(existingTag);
                }

                foreach (var tag in newTags)
                {
                    await _unitOfWork.Repository<ConversationTag>().AddAsync(new ConversationTag
                    {
                        ConversationId = conversationId,
                        TagId = tag.Id
                    });
                }

                conversation.UpdatedAt = now;
                committedIds.Add(conversationId);
            }

            // 统一 1 次 SaveChangesAsync（原来 N 次 → 1 次）
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
            successCount = committedIds.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量标签更新事务失败，降级为逐条事务重试");
            _unitOfWork.ClearChanges();

            // 降级：逐条重试，保持原部分成功语义
            foreach (var conversationId in validConversationIds)
            {
                using var perTx = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    if (!conversationDict.TryGetValue(conversationId, out var conversation))
                    {
                        failedCount++;
                        errors.Add($"会话 {conversationId} 不存在");
                        continue;
                    }

                    var existingTags = allExistingTags
                        .Where(ct => ct.ConversationId == conversationId)
                        .ToList();

                    foreach (var existingTag in existingTags)
                    {
                        await _unitOfWork.Repository<ConversationTag>().DeleteAsync(existingTag);
                    }

                    foreach (var tag in newTags)
                    {
                        await _unitOfWork.Repository<ConversationTag>().AddAsync(new ConversationTag
                        {
                            ConversationId = conversationId,
                            TagId = tag.Id
                        });
                    }

                    conversation.UpdatedAt = DateTime.UtcNow;
                    await _unitOfWork.SaveChangesAsync();
                    await perTx.CommitAsync();
                    successCount++;
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, "批量标签更新降级重试失败（会话 {ConversationId}）", conversationId);
                    await perTx.SafeRollbackAsync();
                    _unitOfWork.ClearChanges();
                    failedCount++;
                    errors.Add($"会话 {conversationId}: {innerEx.Message}");
                }
            }
        }

        return new BulkOperationResponse
        {
            TotalCount = request.ConversationIds.Count,
            SuccessCount = successCount,
            FailedCount = failedCount,
            Errors = errors
        };
    }
}
