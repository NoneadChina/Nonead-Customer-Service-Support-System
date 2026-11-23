using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface ISatisfactionRatingRepository : IRepository<SatisfactionRating>
{
    Task<(IEnumerable<SatisfactionRating> Items, int Total)> GetPagedAsync(
        int page,
        int pageSize,
        int? rating = null,
        int? mailboxId = null,
        int? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? keyword = null);

    Task<SatisfactionRatingStats> GetStatsAsync(DateTime? startDate = null, DateTime? endDate = null, int? mailboxId = null, int? userId = null);

    Task<List<DailySatisfactionStats>> GetDailyStatsAsync(DateTime startDate, DateTime endDate, int? mailboxId = null);

    Task<List<MailboxSatisfactionStats>> GetStatsByMailboxAsync(DateTime? startDate = null, DateTime? endDate = null);

    Task<List<UserSatisfactionStats>> GetStatsByUserAsync(DateTime? startDate = null, DateTime? endDate = null);

    Task<SatisfactionRating?> GetByConversationAsync(int conversationId);

    Task<List<SatisfactionRating>> GetRecentAsync(int count = 10, int? rating = null);
}

public class SatisfactionRatingStats
{
    public int TotalCount { get; set; }
    public int ExcellentCount { get; set; }
    public int GoodCount { get; set; }
    public int AverageCount { get; set; }
    public int PoorCount { get; set; }
    public int BadCount { get; set; }
    public double AverageScore { get; set; }
    public double GoodRate { get; set; }
}

public class DailySatisfactionStats
{
    public DateTime Date { get; set; }
    public int TotalCount { get; set; }
    public int ExcellentCount { get; set; }
    public int GoodCount { get; set; }
    public int AverageCount { get; set; }
    public int PoorCount { get; set; }
    public int BadCount { get; set; }
    public double AverageScore { get; set; }
}

public class MailboxSatisfactionStats
{
    public int MailboxId { get; set; }
    public string MailboxName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int ExcellentCount { get; set; }
    public int GoodCount { get; set; }
    public int AverageCount { get; set; }
    public int PoorCount { get; set; }
    public int BadCount { get; set; }
    public double AverageScore { get; set; }
    public double GoodRate { get; set; }
}

public class UserSatisfactionStats
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int ExcellentCount { get; set; }
    public int GoodCount { get; set; }
    public int AverageCount { get; set; }
    public int PoorCount { get; set; }
    public int BadCount { get; set; }
    public double AverageScore { get; set; }
    public double GoodRate { get; set; }
}
