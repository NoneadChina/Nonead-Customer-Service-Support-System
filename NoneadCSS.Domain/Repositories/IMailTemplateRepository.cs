using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Domain.Repositories;

public interface IMailTemplateRepository : IRepository<MailTemplate>
{
    Task<List<MailTemplate>> GetByTypeAsync(MailTemplateType type);
    Task<List<MailTemplate>> GetByMailboxIdAsync(int? mailboxId);
    Task<MailTemplate?> GetBySlugAsync(string slug);
    Task<List<MailTemplate>> GetActiveByTypeAndMailboxAsync(MailTemplateType type, int? mailboxId);
    Task<(IEnumerable<MailTemplate> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        MailTemplateType? type = null, int? mailboxId = null, bool? isActive = null, string? keyword = null);
    Task<Dictionary<string, int>> GetStatsAsync();
    Task<(IEnumerable<MailTemplate> Items, int Total)> GetDeletedAsync(int page, int pageSize, string? keyword = null);
    Task<int> CountDeletedAsync();
    Task<bool> RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> HardDeleteAllDeletedAsync();
}
