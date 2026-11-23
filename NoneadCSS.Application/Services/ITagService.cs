using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface ITagService
{
    Task<List<TagResponse>> GetTagsAsync(int? mailboxId = null);
    Task<TagResponse?> GetTagByIdAsync(int id);
    Task<TagResponse> CreateTagAsync(CreateTagRequest request);
    Task<TagResponse?> UpdateTagAsync(int id, UpdateTagRequest request);
    Task<bool> DeleteTagAsync(int id);
    Task<List<TagResponse>> GetConversationTagsAsync(int conversationId);
    Task<List<TagResponse>> UpdateConversationTagsAsync(int conversationId, UpdateConversationTagsRequest request);
    Task<List<TagResponse>> AddTagToConversationAsync(int conversationId, int tagId);
    Task<bool> RemoveTagFromConversationAsync(int conversationId, int tagId);
    Task<BulkOperationResponse> BulkUpdateConversationTagsAsync(BulkUpdateConversationTagsRequest request);
    Task<PagedResponse<TagResponse>> GetTrashAsync(int page, int pageSize, string? query = null);
    Task<int> GetTrashCountAsync();
    Task<bool> RestoreTagAsync(int id);
    Task<bool> HardDeleteTagAsync(int id);
    Task<int> ClearTrashAsync();
}
