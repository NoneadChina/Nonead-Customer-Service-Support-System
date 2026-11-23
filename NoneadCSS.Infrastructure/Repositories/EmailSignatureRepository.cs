using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class EmailSignatureRepository : Repository<EmailSignature>, IEmailSignatureRepository
{
    public EmailSignatureRepository(NoneadCSSDbContext context) : base(context) { }

    private IQueryable<EmailSignature> ApplyIncludes(IQueryable<EmailSignature> query)
    {
        return query
            .Include(s => s.User)
            .Include(s => s.Mailbox);
    }

    public async Task<List<EmailSignature>> GetByUserIdAsync(int userId)
    {
        return await ApplyIncludes(_dbSet)
            .Where(s => s.UserId == userId && s.DeletedAt == null)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .ToListAsync();
    }

    public async Task<List<EmailSignature>> GetByUserIdAndMailboxIdAsync(int userId, int? mailboxId)
    {
        var query = ApplyIncludes(_dbSet)
            .Where(s => s.UserId == userId && s.DeletedAt == null)
            .AsQueryable();

        if (mailboxId.HasValue)
        {
            query = query.Where(s => s.MailboxId == null || s.MailboxId == mailboxId.Value);
        }

        return await query
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .ToListAsync();
    }

    public async Task<EmailSignature?> GetDefaultByUserIdAsync(int userId, int? mailboxId = null)
    {
        var query = ApplyIncludes(_dbSet)
            .Where(s => s.UserId == userId && s.IsDefault && s.DeletedAt == null)
            .AsQueryable();

        if (mailboxId.HasValue)
        {
            query = query.Where(s => s.MailboxId == null || s.MailboxId == mailboxId.Value);
        }

        return await query.FirstOrDefaultAsync();
    }

    public async Task<(IEnumerable<EmailSignature> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        int? userId = null, int? mailboxId = null,
        bool? isDefault = null, string? keyword = null)
    {
        var query = ApplyIncludes(_dbSet)
            .Where(s => s.DeletedAt == null)
            .AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(s => s.UserId == userId.Value);
        }
        if (mailboxId.HasValue)
        {
            query = query.Where(s => s.MailboxId == null || s.MailboxId == mailboxId.Value);
        }
        if (isDefault.HasValue)
        {
            query = query.Where(s => s.IsDefault == isDefault.Value);
        }
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(s =>
                s.Name.Contains(keyword) ||
                s.Body.Contains(keyword));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.SortOrder)
            .ThenByDescending(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task SetDefaultAsync(int userId, int? mailboxId, int signatureId)
    {
        var currentDefaults = await _dbSet
            .Where(s => s.UserId == userId && s.IsDefault && s.DeletedAt == null &&
                (mailboxId == null || s.MailboxId == null || s.MailboxId == mailboxId.Value))
            .ToListAsync();

        foreach (var sig in currentDefaults)
        {
            sig.IsDefault = false;
            sig.UpdatedAt = DateTime.UtcNow;
            // sig 已通过 tracking 查询，由 SaveChangesAsync 持久化，无需 _dbSet.Update
        }

        var target = await _dbSet.FindAsync(signatureId);
        if (target != null && target.DeletedAt == null)
        {
            target.IsDefault = true;
            target.UpdatedAt = DateTime.UtcNow;
            // target 已通过 FindAsync 查询（tracking），由 SaveChangesAsync 持久化，无需 _dbSet.Update
        }

    }

    /// <inheritdoc />
    public async Task<(IEnumerable<EmailSignature> Items, int Total)> GetDeletedAsync(int page, int pageSize, string? keyword = null)
    {
        var query = ApplyIncludes(_dbSet.IgnoreQueryFilters())
            .Where(s => s.DeletedAt != null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(s => s.Name.Contains(keyword));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(s => s.DeletedAt)
            .ThenByDescending(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<int> CountDeletedAsync()
    {
        return await _dbSet.IgnoreQueryFilters().CountAsync(s => s.DeletedAt != null);
    }

    /// <inheritdoc />
    public async Task<bool> RestoreAsync(int id)
    {
        var exists = await _dbSet.IgnoreQueryFilters().AnyAsync(s => s.Id == id && s.DeletedAt != null);
        if (!exists) return false;

        await _dbSet.IgnoreQueryFilters()
            .Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(s => s.DeletedAt, (DateTime?)null)
                .SetProperty(s => s.UpdatedAt, DateTime.UtcNow));
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> HardDeleteAsync(int id)
    {
        var exists = await _dbSet.IgnoreQueryFilters().AnyAsync(s => s.Id == id && s.DeletedAt != null);
        if (!exists) return false;

        await _dbSet.IgnoreQueryFilters()
            .Where(s => s.Id == id)
            .ExecuteDeleteAsync();
        return true;
    }

    /// <inheritdoc />
    public async Task<int> HardDeleteAllDeletedAsync()
    {
        var count = await _dbSet.IgnoreQueryFilters().CountAsync(s => s.DeletedAt != null);
        if (count > 0)
        {
            await _dbSet.IgnoreQueryFilters().Where(s => s.DeletedAt != null).ExecuteDeleteAsync();
        }
        return count;
    }
}
