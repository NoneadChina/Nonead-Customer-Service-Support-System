using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class PasswordResetTokenRepository : Repository<PasswordResetToken>, IPasswordResetTokenRepository
{
    public PasswordResetTokenRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 按 Token 哈希值查询。传入参数应为 SHA-256 哈希后的字符串（见 <see cref="NoneadCSS.Application.Security.SecretHasher"/>）。
    /// </summary>
    public async Task<PasswordResetToken?> GetByTokenAsync(string token)
    {
        return await _dbSet
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token);
    }

    public async Task<IEnumerable<PasswordResetToken>> GetByUserIdAsync(int userId)
    {
        return await _dbSet
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task InvalidateUserTokensAsync(int userId)
    {
        // 仓储内不再调用 SaveChangesAsync，由调用方（AuthService.ResetPasswordAsync）统一 SaveChanges。
        // 加载到变更跟踪器的 token 实体（UsedAt 已修改）将由调用方 SaveChangesAsync 持久化。
        var tokens = await _dbSet
            .Where(t => t.UserId == userId && !t.UsedAt.HasValue)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.UsedAt = DateTime.UtcNow;
        }
    }
}
