using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface ISatisfactionRatingService
{
    Task<(IEnumerable<SatisfactionRatingDto> Items, int Total)> GetListAsync(
        int page,
        int pageSize,
        int? rating = null,
        int? mailboxId = null,
        int? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? keyword = null);

    Task<SatisfactionRatingDto?> GetByIdAsync(int id);

    Task<SatisfactionRatingDto?> GetByConversationAsync(int conversationId);

    Task<SatisfactionOverviewResponse> GetOverviewAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        int? mailboxId = null);

    Task<List<SatisfactionRatingDto>> GetRecentAsync(int count = 10, int? rating = null);

    Task<SatisfactionRatingDto> CreateAsync(CreateSatisfactionRatingRequest request, string? ipAddress = null, string? userAgent = null);

    Task<bool> DeleteAsync(int id);

    Task<Dictionary<int, string>> GetRatingOptionsAsync();

    Task<SatisfactionTokenValidationResult?> ValidateTokenAsync(string token);
}

public class SatisfactionTokenValidationResult
{
    public int ConversationId { get; set; }
    public string CustomerEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public bool AlreadyRated { get; set; }
}
