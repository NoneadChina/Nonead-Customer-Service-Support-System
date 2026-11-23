using Microsoft.EntityFrameworkCore;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Infrastructure.Repositories;

public class UserNotificationRepository : Repository<UserNotification>, IUserNotificationRepository
{
    public UserNotificationRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    public async Task<(IEnumerable<UserNotification> Items, int Total)> GetByUserIdAsync(int userId, int page, int pageSize)
    {
        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 20;

        var query = _dbSet.AsNoTracking().Where(n => n.UserId == userId);
        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public Task<int> CountUnreadAsync(int userId)
        => _dbSet.CountAsync(n => n.UserId == userId && !n.IsRead);
}
