using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class MailboxRepository : Repository<Mailbox>, IMailboxRepository
{
    public MailboxRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    private IQueryable<Mailbox> ApplyIncludes(IQueryable<Mailbox> query)
    {
        return query
            .Include(m => m.MailboxUsers)
            .Include(m => m.Folders);
    }

    /// <summary>
    /// 按 ID 列表批量获取邮箱（避免 N+1 查询）
    /// </summary>
    public async Task<List<Mailbox>> GetByIdsAsync(IEnumerable<int> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new List<Mailbox>();
        }

        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(m => idList.Contains(m.Id) && m.DeletedAt == null)
            .ToListAsync();
    }

    public override async Task<Mailbox?> GetByIdAsync(int id)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .FirstOrDefaultAsync(m => m.Id == id && m.DeletedAt == null);
    }

    /// <summary>
    /// 按 ID 查询邮箱（tracking，含 MailboxUsers 和 Folders Includes），
    /// 适用于后续修改 + SaveChanges 的场景。不使用 AsNoTracking，返回的实体被跟踪，
    /// 修改属性后只需 SaveChangesAsync 即可持久化。
    /// </summary>
    public override async Task<Mailbox?> GetByIdTrackedAsync(int id)
    {
        return await ApplyIncludes(_dbSet)
            .FirstOrDefaultAsync(m => m.Id == id && m.DeletedAt == null);
    }

    public async Task<IEnumerable<Mailbox>> GetByUserIdAsync(int userId)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(m => m.IsActive && m.DeletedAt == null && m.MailboxUsers.Any(mu => mu.UserId == userId))
            .ToListAsync();
    }

    public async Task<IEnumerable<Mailbox>> GetActiveAsync()
    {
        return await _dbSet.AsNoTracking()
            .Where(m => m.IsActive && m.DeletedAt == null)
            .ToListAsync();
    }

    /// <summary>
    /// 获取所有活跃邮箱（tracking，适用于后续修改 + SaveChanges 的场景）
    /// </summary>
    public async Task<IEnumerable<Mailbox>> GetActiveTrackedAsync()
    {
        return await _dbSet
            .Where(m => m.IsActive && m.DeletedAt == null)
            .ToListAsync();
    }

    /// <summary>
    /// 获取第一个活跃邮箱（仅取一条，用于系统通知场景，避免 GetAllAsync 全表加载后再内存过滤）
    /// </summary>
    public async Task<Mailbox?> GetFirstActiveAsync()
    {
        return await _dbSet.AsNoTracking()
            .Where(m => m.IsActive && m.DeletedAt == null)
            .OrderBy(m => m.Id)
            .FirstOrDefaultAsync();
    }

    public override async Task<IEnumerable<Mailbox>> GetAllAsync()
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(m => m.DeletedAt == null)
            .ToListAsync();
    }

    public async Task<Mailbox?> GetByEmailAsync(string email)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(m => m.Email == email && m.DeletedAt == null);
    }

    public async Task<bool> UserHasAccessAsync(int mailboxId, int userId)
    {
        return await _context.MailboxUsers.AsNoTracking()
            .AnyAsync(mu => mu.MailboxId == mailboxId && mu.UserId == userId);
    }

    public async Task AddUserToMailboxAsync(int mailboxId, int userId)
    {
        var exists = await UserHasAccessAsync(mailboxId, userId);
        if (!exists)
        {
            await _context.MailboxUsers.AddAsync(new MailboxUser
            {
                MailboxId = mailboxId,
                UserId = userId
            });
        }
    }

    public async Task RemoveUserFromMailboxAsync(int mailboxId, int userId)
    {
        var mailboxUser = await _context.MailboxUsers
            .FirstOrDefaultAsync(mu => mu.MailboxId == mailboxId && mu.UserId == userId);
        if (mailboxUser != null)
        {
            _context.MailboxUsers.Remove(mailboxUser);
        }
    }

    public async Task<IEnumerable<Mailbox>> GetDeletedAsync()
    {
        return await ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking())
            .Where(m => m.DeletedAt != null)
            .OrderByDescending(m => m.DeletedAt)
            .ToListAsync();
    }

    public async Task<Mailbox?> GetDeletedByIdAsync(int id)
    {
        // 不使用 AsNoTracking，返回追踪实体，便于 RestoreMailboxAsync 修改属性后直接 SaveChangesAsync 持久化
        return await ApplyIncludes(_dbSet.IgnoreQueryFilters())
            .FirstOrDefaultAsync(m => m.Id == id && m.DeletedAt != null);
    }

    public async Task HardDeleteAsync(int id)
    {
        if (_context.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                $"{nameof(HardDeleteAsync)} 必须在显式事务内调用，以保证链式 bulk 操作的原子性。请在服务层使用 _unitOfWork.BeginTransactionAsync() 包裹。");
        }

        var exists = await _dbSet.IgnoreQueryFilters().AnyAsync(m => m.Id == id && m.DeletedAt != null);
        if (!exists) return;

        // ========== 清理 Cascade 删除行为的关联数据 ==========
        // 清理关联的 MailboxUsers（OnDelete: Cascade）
        await _context.MailboxUsers.IgnoreQueryFilters()
            .Where(mu => mu.MailboxId == id)
            .ExecuteDeleteAsync();

        // 清理关联的 Folders（OnDelete: Cascade，ConversationFolder -> Folder 级联删除）
        await _context.Folders.IgnoreQueryFilters()
            .Where(f => f.MailboxId == id)
            .ExecuteDeleteAsync();

        // 清理关联的 CannedResponse（OnDelete: Cascade）
        await _context.Set<CannedResponse>().IgnoreQueryFilters()
            .Where(c => c.MailboxId == id)
            .ExecuteDeleteAsync();

        // 清理关联的 CustomField（OnDelete: Cascade）
        await _context.Set<CustomField>().IgnoreQueryFilters()
            .Where(cf => cf.MailboxId == id)
            .ExecuteDeleteAsync();

        // 清理关联的 Webhook（OnDelete: Cascade）
        await _context.Set<Webhook>().IgnoreQueryFilters()
            .Where(w => w.MailboxId == id)
            .ExecuteDeleteAsync();

        // ========== 清理 SetNull 删除行为的关联数据（显式置空）==========
        // Workflow.SlaPolicy 等依赖 Workflow 存在，先置空 MailboxId
        await _context.Set<Workflow>().IgnoreQueryFilters()
            .Where(w => w.MailboxId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.MailboxId, (int?)null));

        // 清理 SlaPolicy（SetNull）
        await _context.Set<SlaPolicy>().IgnoreQueryFilters()
            .Where(s => s.MailboxId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(s => s.MailboxId, (int?)null));

        // 清理 Macro（SetNull）
        await _context.Set<Macro>().IgnoreQueryFilters()
            .Where(m => m.MailboxId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.MailboxId, (int?)null));

        // 清理 MailTemplate（SetNull）
        await _context.Set<MailTemplate>().IgnoreQueryFilters()
            .Where(mt => mt.MailboxId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(mt => mt.MailboxId, (int?)null));

        // 清理 Team（SetNull）
        await _context.Set<Team>().IgnoreQueryFilters()
            .Where(t => t.MailboxId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.MailboxId, (int?)null));

        // 清理 EmailSignature（SetNull）
        await _context.Set<EmailSignature>().IgnoreQueryFilters()
            .Where(es => es.MailboxId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(es => es.MailboxId, (int?)null));

        // 清理 ChatSession（SetNull）
        await _context.Set<ChatSession>().IgnoreQueryFilters()
            .Where(cs => cs.MailboxId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(cs => cs.MailboxId, (int?)null));

        // 清理 Tag（SetNull / 无关联外键但属于邮箱）
        await _context.Set<Tag>().IgnoreQueryFilters()
            .Where(t => t.MailboxId == id)
            .ExecuteDeleteAsync();

        // 删除邮箱本身（绕过全局查询过滤器）
        await _dbSet.IgnoreQueryFilters()
            .Where(m => m.Id == id && m.DeletedAt != null)
            .ExecuteDeleteAsync();
    }

    public async Task HardDeleteAllDeletedAsync()
    {
        if (_context.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                $"{nameof(HardDeleteAllDeletedAsync)} 必须在显式事务内调用，以保证链式 bulk 操作的原子性。请在服务层使用 _unitOfWork.BeginTransactionAsync() 包裹。");
        }

        var ids = await _dbSet.IgnoreQueryFilters()
            .Where(m => m.DeletedAt != null)
            .Select(m => m.Id)
            .ToListAsync();

        if (ids.Count == 0) return;

        foreach (var id in ids)
        {
            await HardDeleteAsync(id);
        }
    }
}
