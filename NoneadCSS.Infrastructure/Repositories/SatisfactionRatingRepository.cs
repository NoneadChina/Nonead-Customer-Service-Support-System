using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public static class SatisfactionRatingValue
{
    public const int Excellent = 5;
    public const int Good = 4;
    public const int Average = 3;
    public const int Poor = 2;
    public const int Bad = 1;

    public static string GetLabel(int rating) => rating switch
    {
        Excellent => "远超期望",
        Good => "物有所值",
        Average => "中规中矩",
        Poor => "略有失望",
        Bad => "无法接受",
        _ => "未知"
    };

    public static string GetCategory(int rating) => rating switch
    {
        Excellent => "excellent",
        Good => "good",
        Average => "average",
        Poor => "poor",
        Bad => "bad",
        _ => "unknown"
    };
}

public class SatisfactionRatingRepository : Repository<SatisfactionRating>, ISatisfactionRatingRepository
{
    public SatisfactionRatingRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    public async Task<(IEnumerable<SatisfactionRating> Items, int Total)> GetPagedAsync(
        int page,
        int pageSize,
        int? rating = null,
        int? mailboxId = null,
        int? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? keyword = null)
    {
        var query = _dbSet.AsNoTracking();

        if (rating.HasValue)
        {
            query = query.Where(r => r.Rating == rating.Value);
        }

        if (userId.HasValue)
        {
            query = query.Where(r => r.UserId == userId.Value);
        }

        if (startDate.HasValue)
        {
            query = query.Where(r => r.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            var endExclusive = endDate.Value.AddDays(1);
            query = query.Where(r => r.CreatedAt < endExclusive);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(r =>
                (r.CustomerName != null && r.CustomerName.Contains(kw)) ||
                (r.CustomerEmail != null && r.CustomerEmail.Contains(kw)) ||
                (r.Comment != null && r.Comment.Contains(kw)));
        }

        if (mailboxId.HasValue)
        {
            query = query
                .Include(r => r.Conversation)
                .Where(r => r.Conversation != null && r.Conversation.MailboxId == mailboxId.Value);
        }

        var total = await query.CountAsync();

        var items = await query
            .Include(r => r.Conversation)
            .Include(r => r.Customer)
            .Include(r => r.User)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<SatisfactionRatingStats> GetStatsAsync(DateTime? startDate = null, DateTime? endDate = null, int? mailboxId = null, int? userId = null)
    {
        var query = _dbSet.AsNoTracking();

        if (startDate.HasValue)
        {
            query = query.Where(r => r.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            var endExclusive = endDate.Value.AddDays(1);
            query = query.Where(r => r.CreatedAt < endExclusive);
        }

        if (userId.HasValue)
        {
            query = query.Where(r => r.UserId == userId.Value);
        }

        if (mailboxId.HasValue)
        {
            query = query
                .Include(r => r.Conversation)
                .Where(r => r.Conversation != null && r.Conversation.MailboxId == mailboxId.Value);
        }

        var total = await query.CountAsync();
        var excellent = await query.CountAsync(r => r.Rating == SatisfactionRatingValue.Excellent);
        var good = await query.CountAsync(r => r.Rating == SatisfactionRatingValue.Good);
        var average = await query.CountAsync(r => r.Rating == SatisfactionRatingValue.Average);
        var poor = await query.CountAsync(r => r.Rating == SatisfactionRatingValue.Poor);
        var bad = await query.CountAsync(r => r.Rating == SatisfactionRatingValue.Bad);
        var totalScore = await query.SumAsync(r => (int)r.Rating);

        var stats = new SatisfactionRatingStats
        {
            TotalCount = total,
            ExcellentCount = excellent,
            GoodCount = good,
            AverageCount = average,
            PoorCount = poor,
            BadCount = bad,
            AverageScore = total > 0 ? (double)totalScore / total : 0,
            GoodRate = total > 0 ? (double)(excellent + good) / total * 100 : 0
        };

        return stats;
    }

    public async Task<List<DailySatisfactionStats>> GetDailyStatsAsync(DateTime startDate, DateTime endDate, int? mailboxId = null)
    {
        var query = _dbSet.AsNoTracking();

        query = query.Where(r => r.CreatedAt >= startDate && r.CreatedAt < endDate.AddDays(1));

        if (mailboxId.HasValue)
        {
            query = query
                .Include(r => r.Conversation)
                .Where(r => r.Conversation != null && r.Conversation.MailboxId == mailboxId.Value);
        }

        var stats = await query
            .GroupBy(r => new { r.CreatedAt.Date })
            .Select(g => new DailySatisfactionStats
            {
                Date = g.Key.Date,
                TotalCount = g.Count(),
                ExcellentCount = g.Count(r => r.Rating == SatisfactionRatingValue.Excellent),
                GoodCount = g.Count(r => r.Rating == SatisfactionRatingValue.Good),
                AverageCount = g.Count(r => r.Rating == SatisfactionRatingValue.Average),
                PoorCount = g.Count(r => r.Rating == SatisfactionRatingValue.Poor),
                BadCount = g.Count(r => r.Rating == SatisfactionRatingValue.Bad),
                AverageScore = (double)g.Sum(r => r.Rating) / g.Count()
            })
            .OrderBy(s => s.Date)
            .ToListAsync();

        return stats;
    }

    public async Task<List<MailboxSatisfactionStats>> GetStatsByMailboxAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = from r in _dbSet.AsNoTracking()
                    join c in _context.Conversations on r.ConversationId equals c.Id
                    select new { Rating = r, c.MailboxId };

        if (startDate.HasValue)
        {
            query = query.Where(x => x.Rating.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            var endExclusive = endDate.Value.AddDays(1);
            query = query.Where(x => x.Rating.CreatedAt < endExclusive);
        }

        var groupedStats = await query
            .GroupBy(x => x.MailboxId)
            .Select(g => new
            {
                MailboxId = g.Key,
                TotalCount = g.Count(),
                ExcellentCount = g.Count(x => x.Rating.Rating == SatisfactionRatingValue.Excellent),
                GoodCount = g.Count(x => x.Rating.Rating == SatisfactionRatingValue.Good),
                AverageCount = g.Count(x => x.Rating.Rating == SatisfactionRatingValue.Average),
                PoorCount = g.Count(x => x.Rating.Rating == SatisfactionRatingValue.Poor),
                BadCount = g.Count(x => x.Rating.Rating == SatisfactionRatingValue.Bad),
                TotalScore = g.Sum(x => (int)x.Rating.Rating),
            })
            .OrderByDescending(g => g.TotalCount > 0 ? (double)g.TotalScore / g.TotalCount : 0)
            .ToListAsync();

        var mailboxIds = groupedStats.Select(g => g.MailboxId).Distinct().ToList();
        var mailboxes = await _context.Mailboxes
            .Where(m => mailboxIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m);

        var stats = groupedStats.Select(g =>
        {
            var mailbox = mailboxes.TryGetValue(g.MailboxId, out var m) ? m : null;
            return new MailboxSatisfactionStats
            {
                MailboxId = g.MailboxId,
                MailboxName = mailbox?.Name ?? "未知",
                Email = mailbox?.Email ?? "-",
                TotalCount = g.TotalCount,
                ExcellentCount = g.ExcellentCount,
                GoodCount = g.GoodCount,
                AverageCount = g.AverageCount,
                PoorCount = g.PoorCount,
                BadCount = g.BadCount,
                AverageScore = g.TotalCount > 0 ? (double)g.TotalScore / g.TotalCount : 0,
                GoodRate = g.TotalCount > 0 ? (double)(g.ExcellentCount + g.GoodCount) / g.TotalCount * 100 : 0
            };
        }).ToList();

        return stats;
    }

    public async Task<List<UserSatisfactionStats>> GetStatsByUserAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _dbSet.AsNoTracking();

        if (startDate.HasValue)
        {
            query = query.Where(r => r.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            var endExclusive = endDate.Value.AddDays(1);
            query = query.Where(r => r.CreatedAt < endExclusive);
        }

        var groupedStats = await query
            .Where(r => r.UserId != null)
            .GroupBy(r => r.UserId!.Value)
            .Select(g => new
            {
                UserId = g.Key,
                TotalCount = g.Count(),
                ExcellentCount = g.Count(r => r.Rating == SatisfactionRatingValue.Excellent),
                GoodCount = g.Count(r => r.Rating == SatisfactionRatingValue.Good),
                AverageCount = g.Count(r => r.Rating == SatisfactionRatingValue.Average),
                PoorCount = g.Count(r => r.Rating == SatisfactionRatingValue.Poor),
                BadCount = g.Count(r => r.Rating == SatisfactionRatingValue.Bad),
                TotalScore = g.Sum(r => (int)r.Rating),
            })
            .OrderByDescending(g => g.TotalCount > 0 ? (double)g.TotalScore / g.TotalCount : 0)
            .ToListAsync();

        var userIds = groupedStats.Select(g => g.UserId).Distinct().ToList();
        var users = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u);

        var stats = groupedStats.Select(g =>
        {
            var user = users.TryGetValue(g.UserId, out var u) ? u : null;
            return new UserSatisfactionStats
            {
                UserId = g.UserId,
                UserName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "未知",
                Email = user?.Email ?? "-",
                TotalCount = g.TotalCount,
                ExcellentCount = g.ExcellentCount,
                GoodCount = g.GoodCount,
                AverageCount = g.AverageCount,
                PoorCount = g.PoorCount,
                BadCount = g.BadCount,
                AverageScore = g.TotalCount > 0 ? (double)g.TotalScore / g.TotalCount : 0,
                GoodRate = g.TotalCount > 0 ? (double)(g.ExcellentCount + g.GoodCount) / g.TotalCount * 100 : 0
            };
        }).ToList();

        return stats;
    }

    public async Task<SatisfactionRating?> GetByConversationAsync(int conversationId)
    {
        return await _dbSet
            .AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.ConversationId == conversationId);
    }

    public async Task<List<SatisfactionRating>> GetRecentAsync(int count = 10, int? rating = null)
    {
        var query = _dbSet.AsNoTracking();

        if (rating.HasValue)
        {
            query = query.Where(r => r.Rating == rating.Value);
        }

        return await query
            .Include(r => r.Conversation)
            .Include(r => r.Customer)
            .Include(r => r.User)
            .OrderByDescending(r => r.CreatedAt)
            .Take(count)
            .ToListAsync();
    }
}
