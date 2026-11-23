using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IConversationStatusService
{
    Task<ConversationResponse?> UpdateStatusAsync(int id, int status, int? userId = null);
    Task<BulkOperationResponse> BulkUpdateStatusAsync(BulkStatusRequest request);
}
