namespace NoneadCSS.Domain.Repositories;

public interface IRefreshTokenRepository : IRepository<Entities.RefreshToken>
{
    Task<Entities.RefreshToken?> GetByTokenAsync(string token);
    Task<IEnumerable<Entities.RefreshToken>> GetActiveByUserIdAsync(int userId);
    Task RevokeAsync(string token, string? reason = null);
    Task RevokeUserTokensAsync(int userId, string? reason = null);
}
