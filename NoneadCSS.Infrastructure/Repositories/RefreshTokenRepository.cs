using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class RefreshTokenRepository : Repository<RefreshToken>, IRefreshTokenRepository
{
    public RefreshTokenRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 按 Token 哈希值查询。传入参数应为 SHA-256 哈希后的字符串（见 <see cref="NoneadCSS.Application.Security.SecretHasher"/>）。
    /// </summary>
    public async Task<RefreshToken?> GetByTokenAsync(string token)
    {
        return await _dbSet
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token);
    }

    public async Task<IEnumerable<RefreshToken>> GetActiveByUserIdAsync(int userId)
    {
        return await _dbSet
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
    }

    public async Task RevokeAsync(string token, string? reason = null)
    {
        // 使用 ExecuteUpdateAsync 直接在数据库层更新，避免加载实体到变更跟踪器，
        // 同时无需 SaveChangesAsync（由本方法直接持久化）。
        await _dbSet
            .Where(t => t.Token == token && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, DateTime.UtcNow)
                .SetProperty(t => t.ReasonRevoked, reason));
    }

    public async Task RevokeUserTokensAsync(int userId, string? reason = null)
    {
        // 使用 ExecuteUpdateAsync 批量撤销，避免逐条加载实体到变更跟踪器，
        // 同时无需 SaveChangesAsync（由本方法直接持久化）。
        await _dbSet
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, DateTime.UtcNow)
                .SetProperty(t => t.ReasonRevoked, reason));
    }
}
