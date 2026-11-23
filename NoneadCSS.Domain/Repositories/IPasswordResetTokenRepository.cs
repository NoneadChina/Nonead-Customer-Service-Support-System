namespace NoneadCSS.Domain.Repositories;

public interface IPasswordResetTokenRepository : IRepository<Entities.PasswordResetToken>
{
    Task<Entities.PasswordResetToken?> GetByTokenAsync(string token);
    Task<IEnumerable<Entities.PasswordResetToken>> GetByUserIdAsync(int userId);
    Task InvalidateUserTokensAsync(int userId);
}
