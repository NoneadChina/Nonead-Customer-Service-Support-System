using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IConversationTagService
{
    Task<List<TagResponse>> GetConversationTagsAsync(int conversationId);
    Task<List<TagResponse>> UpdateConversationTagsAsync(int conversationId, UpdateConversationTagsRequest request);
    Task<List<TagResponse>> AddTagToConversationAsync(int conversationId, int tagId);
    Task<bool> RemoveTagFromConversationAsync(int conversationId, int tagId);
    Task<BulkOperationResponse> BulkUpdateConversationTagsAsync(BulkUpdateConversationTagsRequest request);
}
