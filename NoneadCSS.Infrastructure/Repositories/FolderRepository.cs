using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class FolderRepository : Repository<Folder>, IFolderRepository
{
    public FolderRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Folder>> GetByMailboxIdAsync(int mailboxId)
    {
        return await _dbSet.AsNoTracking()
            .Where(f => f.MailboxId == mailboxId)
            .OrderBy(f => f.Type)
            .ToListAsync();
    }

    /// <summary>
    /// 获取邮箱下所有文件夹（tracking，适用于批量修改 + SaveChanges 的场景，如更新计数器）
    /// </summary>
    public async Task<IEnumerable<Folder>> GetByMailboxIdTrackedAsync(int mailboxId)
    {
        return await _dbSet
            .Where(f => f.MailboxId == mailboxId)
            .OrderBy(f => f.Type)
            .ToListAsync();
    }

    public async Task<IEnumerable<Folder>> GetByUserIdAsync(int userId)
    {
        return await _dbSet.AsNoTracking()
            .Where(f => f.UserId == userId)
            .ToListAsync();
    }

    public async Task<Folder?> GetByMailboxIdAndTypeAsync(int mailboxId, int type)
    {
        return await _dbSet.AsNoTracking()
            .FirstOrDefaultAsync(f => f.MailboxId == mailboxId && (int)f.Type == type);
    }
}
