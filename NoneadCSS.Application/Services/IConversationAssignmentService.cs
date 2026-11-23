using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IConversationAssignmentService
{
    Task<ConversationResponse?> AssignAsync(int id, int userId);
    Task<BulkOperationResponse> BulkAssignAsync(BulkAssignRequest request, int? currentUserId = null);
}
