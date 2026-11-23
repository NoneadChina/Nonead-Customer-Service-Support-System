using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

public class TagService : ITagService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationTagService _conversationTagService;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public TagService(
        IUnitOfWork unitOfWork,
        IConversationTagService conversationTagService,
        ICurrentUserAccessor currentUserAccessor)
    {
        _unitOfWork = unitOfWork;
        _conversationTagService = conversationTagService;
        _currentUserAccessor = currentUserAccessor;
    }

    public async Task<List<TagResponse>> GetTagsAsync(int? mailboxId = null)
    {
        var tags = await _unitOfWork.Tags.GetByMailboxIdAsync(mailboxId);
        var tagList = tags.ToList();

        // 单次 GROUP BY 查询批量获取所有标签的会话计数，避免对每个标签单独查询（N+1）。
        var counts = await _unitOfWork.Tags.GetTagCountsAsync(tagList.Select(t => t.Id));

        return tagList
            .Select(t => t.ToResponse(counts.TryGetValue(t.Id, out var c) ? c : 0))
            .ToList();
    }

    public async Task<TagResponse?> GetTagByIdAsync(int id)
    {
        var tag = await _unitOfWork.Tags.GetByIdAsync(id);
        if (tag == null || tag.DeletedAt != null)
        {
            return null;
        }

        if (!await _currentUserAccessor.CanAccessMailboxAsync(tag.MailboxId))
            return null;

        // 翻译为 SQL COUNT(*) WHERE，不再将整张 ConversationTags 表加载到内存。
        var count = await _unitOfWork.Tags.GetTagCountAsync(tag.Id);

        return tag.ToResponse(count);
    }

    public async Task<TagResponse> CreateTagAsync(CreateTagRequest request)
    {
        var existingTag = await _unitOfWork.Tags.GetByNameAsync(request.Name, request.MailboxId);
        if (existingTag != null)
        {
            throw new InvalidOperationException($"标签 \"{request.Name}\" 已存在");
        }

        var tag = new Tag
        {
            Name = request.Name,
            Color = request.Color,
            MailboxId = request.MailboxId
        };

        await _unitOfWork.Tags.AddAsync(tag);
        await _unitOfWork.SaveChangesAsync();

        return tag.ToResponse();
    }

    public async Task<TagResponse?> UpdateTagAsync(int id, UpdateTagRequest request)
    {
        var tag = await _unitOfWork.Tags.GetByIdTrackedAsync(id);
        if (tag == null || tag.DeletedAt != null)
        {
            return null;
        }

        if (!await _currentUserAccessor.CanAccessMailboxAsync(tag.MailboxId))
            return null;

        var existingTag = await _unitOfWork.Tags.GetByNameAsync(request.Name, tag.MailboxId);
        if (existingTag != null && existingTag.Id != id)
        {
            throw new InvalidOperationException($"标签 \"{request.Name}\" 已存在");
        }

        tag.Name = request.Name;
        tag.Color = request.Color;
        tag.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        var count = await _unitOfWork.Tags.GetTagCountAsync(tag.Id);

        return tag.ToResponse(count);
    }

    public async Task<bool> DeleteTagAsync(int id)
    {
        var tag = await _unitOfWork.Tags.GetByIdTrackedAsync(id);
        if (tag == null || tag.DeletedAt != null)
        {
            return false;
        }

        if (!await _currentUserAccessor.CanAccessMailboxAsync(tag.MailboxId))
            return false;

        tag.DeletedAt = DateTime.UtcNow;
        tag.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<List<TagResponse>> GetConversationTagsAsync(int conversationId)
    {
        return await _conversationTagService.GetConversationTagsAsync(conversationId);
    }

    public async Task<List<TagResponse>> UpdateConversationTagsAsync(int conversationId, UpdateConversationTagsRequest request)
    {
        return await _conversationTagService.UpdateConversationTagsAsync(conversationId, request);
    }

    public async Task<List<TagResponse>> AddTagToConversationAsync(int conversationId, int tagId)
    {
        return await _conversationTagService.AddTagToConversationAsync(conversationId, tagId);
    }

    public async Task<bool> RemoveTagFromConversationAsync(int conversationId, int tagId)
    {
        return await _conversationTagService.RemoveTagFromConversationAsync(conversationId, tagId);
    }

    public async Task<BulkOperationResponse> BulkUpdateConversationTagsAsync(BulkUpdateConversationTagsRequest request)
    {
        return await _conversationTagService.BulkUpdateConversationTagsAsync(request);
    }

    public async Task<PagedResponse<TagResponse>> GetTrashAsync(int page, int pageSize, string? query = null)
    {
        var (items, total) = await _unitOfWork.Tags.GetDeletedAsync(page, pageSize, query);
        var responses = items.Select(t => t.ToResponse()).ToList();
        return responses.ToPagedResponse(page, pageSize, total);
    }

    public async Task<int> GetTrashCountAsync()
    {
        return await _unitOfWork.Tags.CountDeletedAsync();
    }

    public async Task<bool> RestoreTagAsync(int id)
    {
        return await _unitOfWork.Tags.RestoreAsync(id);
    }

    public async Task<bool> HardDeleteTagAsync(int id)
    {
        if (_currentUserAccessor != null && !_currentUserAccessor.IsSuperAdmin)
        {
            return false;
        }
        return await _unitOfWork.Tags.HardDeleteAsync(id);
    }

    public async Task<int> ClearTrashAsync()
    {
        return await _unitOfWork.Tags.HardDeleteAllDeletedAsync();
    }
}
