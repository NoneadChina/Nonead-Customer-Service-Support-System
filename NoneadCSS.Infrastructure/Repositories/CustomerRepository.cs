using NoneadCSS.Application.Common;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class CustomerRepository : Repository<Customer>, ICustomerRepository
{
    public CustomerRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    private IQueryable<Customer> ApplyIncludes(IQueryable<Customer> query)
    {
        return query.Include(c => c.Emails);
    }

    public override async Task<IEnumerable<Customer>> GetAllAsync()
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .OrderByDescending(c => c.CreatedAt)
            .Take(100)
            .ToListAsync();
    }

    public override async Task<Customer?> GetByIdAsync(int id)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null);
    }

    public async Task<Customer?> GetByEmailAsync(string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(c => c.DeletedAt == null)
            .FirstOrDefaultAsync(c => c.Emails.Any(e => e.EmailAddress.ToLower() == normalizedEmail));
    }

    public async Task<bool> ExistsByEmailAsync(string email, int? excludeCustomerId = null)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var query = _dbSet.AsNoTracking()
            .Where(c => c.DeletedAt == null)
            .Where(c => c.Emails.Any(e => e.EmailAddress.ToLower() == normalizedEmail));

        if (excludeCustomerId.HasValue)
        {
            query = query.Where(c => c.Id != excludeCustomerId.Value);
        }

        return await query.AnyAsync();
    }

    /// <summary>
    /// 检查邮箱是否已被任意客户占用（包括已软删除的客户）。
    /// Emails.EmailAddress 有全局唯一约束，enrich 时必须检查，否则 SaveChanges 会触发 UNIQUE constraint failed。
    /// </summary>
    public async Task<bool> IsEmailOwnedByAnyCustomerAsync(string email, int? excludeCustomerId = null)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var query = _context.Emails.IgnoreQueryFilters()
            .Where(e => e.EmailAddress.ToLower() == normalizedEmail);

        if (excludeCustomerId.HasValue)
        {
            query = query.Where(e => e.CustomerId != excludeCustomerId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<List<Customer>> GetByIdsAsync(IEnumerable<int> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return new List<Customer>();

        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(c => idList.Contains(c.Id) && c.DeletedAt == null)
            .ToListAsync();
    }

    public async Task<int> CountCreatedInDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _dbSet.AsNoTracking()
            .Where(c => c.DeletedAt == null && c.CreatedAt >= startDate && c.CreatedAt < endDate)
            .CountAsync();
    }

    public async Task<(IEnumerable<Customer> Items, int Total)> SearchAsync(string query, int page, int pageSize, int[]? accessibleMailboxIds = null)
    {
        // 设计说明（M-Q5）：以下 Where 中使用 _context.Conversations.Any(...) 关联子查询实现 IDOR 防护，
        // 仅返回在可访问邮箱下产生会话的客户。子查询在 SQL 层翻译为 EXISTS，对常见数据量性能可接受；
        // 若数据规模显著增长，可考虑改用 JOIN + Distinct 或物化"客户-邮箱访问"关系表。当前保持现状，跳过重构。
        var queryable = _dbSet
            .AsNoTracking()
            .Where(c => c.DeletedAt == null)
            .Where(c => c.FirstName.Contains(query) || c.LastName.Contains(query)
                || c.Company != null && c.Company.Contains(query)
                || c.Emails.Any(e => e.EmailAddress.Contains(query)));

        // IDOR 防护：当指定可访问邮箱 ID 时，仅返回曾在这些邮箱下产生会话的客户
        // accessibleMailboxIds = null 表示不限制（超管）；空数组表示无任何邮箱权限（返回空结果）
        if (accessibleMailboxIds != null)
        {
            var mailboxIdSet = accessibleMailboxIds;
            queryable = queryable.Where(c =>
                _context.Conversations.Any(conv =>
                    conv.CustomerId == c.Id && conv.DeletedAt == null && mailboxIdSet.Contains(conv.MailboxId)));
        }

        var total = await queryable.CountAsync();

        var items = await ApplyIncludes(queryable)
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<(IEnumerable<Customer> Items, int Total)> GetDeletedAsync(int page, int pageSize, string? query = null)
    {
        var queryable = ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking())
            .Where(c => c.DeletedAt != null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(query))
        {
            queryable = queryable.Where(c =>
                c.FirstName.Contains(query) || c.LastName.Contains(query) ||
                c.Company != null && c.Company.Contains(query) ||
                c.Emails.Any(e => e.EmailAddress.Contains(query)));
        }

        var total = await queryable.CountAsync();

        var items = await queryable
            .OrderByDescending(c => c.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<Customer?> GetDeletedByIdAsync(int id)
    {
        return await ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking())
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt != null);
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var rowsAffected = await _dbSet.IgnoreQueryFilters()
            .Where(c => c.Id == id && c.DeletedAt != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.DeletedAt, (DateTime?)null)
                .SetProperty(c => c.UpdatedAt, DateTime.UtcNow));
        return rowsAffected > 0;
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        if (_context.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                $"{nameof(HardDeleteAsync)} 必须在显式事务内调用，以保证链式 bulk 操作的原子性。请在服务层使用 _unitOfWork.BeginTransactionAsync() 包裹。");
        }

        var exists = await _dbSet.IgnoreQueryFilters().AnyAsync(c => c.Id == id && c.DeletedAt != null);
        if (!exists) return false;

        // SendLogs：将 CustomerId 设为 null（保留历史发送记录，断开与客户的关联）
        await _context.SendLogs.Where(s => s.CustomerId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, (int?)null));

        // ImMessageLogs：将 CustomerId 设为 null（DeleteBehavior.Restrict，必须先断开关联才能删 Customer）
        await _context.ImMessageLogs.IgnoreQueryFilters()
            .Where(l => l.CustomerId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, (int?)null));

        // 收集关联的会话 ID
        var conversationIds = await _context.Conversations.IgnoreQueryFilters()
            .Where(c => c.CustomerId == id)
            .Select(c => c.Id)
            .ToListAsync();

        if (conversationIds.Count > 0)
        {
            // 先删除审计记录（DeleteBehavior.Restrict，不会级联删除）
            await _context.WorkflowExecutionLogs.IgnoreQueryFilters()
                .Where(l => l.ConversationId.HasValue && conversationIds.Contains(l.ConversationId.Value))
                .ExecuteDeleteAsync();

            await _context.SlaViolations.IgnoreQueryFilters()
                .Where(v => conversationIds.Contains(v.ConversationId))
                .ExecuteDeleteAsync();

            await _context.MacroExecutionLogs.IgnoreQueryFilters()
                .Where(l => conversationIds.Contains(l.ConversationId))
                .ExecuteDeleteAsync();

            // 删除 Conversations（数据库会级联删除关联的 Threads、Tags、Followers、Stars 等）
            await _context.Conversations.IgnoreQueryFilters()
                .Where(c => conversationIds.Contains(c.Id))
                .ExecuteDeleteAsync();
        }

        // SendLogs：将 CustomerId 设为 null（DeleteBehavior.SetNull，需手动置空）
        await _context.SendLogs.IgnoreQueryFilters()
            .Where(s => s.CustomerId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, (int?)null));

        // ChatSessions：将 CustomerId 设为 null（DeleteBehavior.SetNull，需手动置空）
        await _context.ChatSessions.IgnoreQueryFilters()
            .Where(s => s.CustomerId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, (int?)null));

        // TimeEntries：删除（DeleteBehavior.Restrict，必须手动删除否则外键约束失败）
        await _context.TimeEntries.IgnoreQueryFilters()
            .Where(t => t.CustomerId == id)
            .ExecuteDeleteAsync();

        // 删除剩余的 SatisfactionRatings（未被 Conversation 级联删除的部分）
        await _context.SatisfactionRatings.IgnoreQueryFilters()
            .Where(s => s.CustomerId == id)
            .ExecuteDeleteAsync();

        // 删除 Emails
        await _context.Emails.IgnoreQueryFilters()
            .Where(e => e.CustomerId == id)
            .ExecuteDeleteAsync();

        // 删除客户本身（绕过全局查询过滤器）
        await _dbSet.IgnoreQueryFilters()
            .Where(c => c.Id == id)
            .ExecuteDeleteAsync();

        return true;
    }

    public async Task<int> HardDeleteAllDeletedAsync()
    {
        if (_context.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                $"{nameof(HardDeleteAllDeletedAsync)} 必须在显式事务内调用，以保证链式 bulk 操作的原子性。请在服务层使用 _unitOfWork.BeginTransactionAsync() 包裹。");
        }

        // 先收集要删除的客户 ID（避免多次查询 DeletedAt != null 时数据不一致）
        var ids = await _dbSet.IgnoreQueryFilters()
            .Where(c => c.DeletedAt != null)
            .Select(c => c.Id)
            .ToListAsync();

        if (ids.Count == 0) return 0;

        // SendLogs：将 CustomerId 设为 null
        await _context.SendLogs.IgnoreQueryFilters().Where(s => s.CustomerId.HasValue && ids.Contains(s.CustomerId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, (int?)null));

        // ImMessageLogs：将 CustomerId 设为 null（DeleteBehavior.Restrict，必须先断开关联才能删 Customer）
        await _context.ImMessageLogs.IgnoreQueryFilters()
            .Where(l => l.CustomerId.HasValue && ids.Contains(l.CustomerId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, (int?)null));

        // ChatSessions：将 CustomerId 设为 null（DeleteBehavior.SetNull，需手动置空）
        await _context.ChatSessions.IgnoreQueryFilters().Where(s => s.CustomerId.HasValue && ids.Contains(s.CustomerId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, (int?)null));

        // TimeEntries：删除（DeleteBehavior.Restrict，必须手动删除否则外键约束失败）
        await _context.TimeEntries.IgnoreQueryFilters()
            .Where(t => t.CustomerId.HasValue && ids.Contains(t.CustomerId.Value))
            .ExecuteDeleteAsync();

        // 收集关联的会话 ID
        var conversationIds = await _context.Conversations.IgnoreQueryFilters()
            .Where(c => c.CustomerId.HasValue && ids.Contains(c.CustomerId.Value))
            .Select(c => c.Id)
            .ToListAsync();

        if (conversationIds.Count > 0)
        {
            // 先删除审计记录（DeleteBehavior.Restrict，不会级联删除）
            await _context.WorkflowExecutionLogs.IgnoreQueryFilters()
                .Where(l => l.ConversationId.HasValue && conversationIds.Contains(l.ConversationId.Value))
                .ExecuteDeleteAsync();

            await _context.SlaViolations.IgnoreQueryFilters()
                .Where(v => conversationIds.Contains(v.ConversationId))
                .ExecuteDeleteAsync();

            await _context.MacroExecutionLogs.IgnoreQueryFilters()
                .Where(l => conversationIds.Contains(l.ConversationId))
                .ExecuteDeleteAsync();

            // 删除 Conversations（数据库会级联删除关联的 Threads、Tags、Followers、Stars 等）
            await _context.Conversations.IgnoreQueryFilters()
                .Where(c => conversationIds.Contains(c.Id))
                .ExecuteDeleteAsync();
        }

        // 删除剩余的 SatisfactionRatings
        await _context.SatisfactionRatings.IgnoreQueryFilters()
            .Where(s => ids.Contains(s.CustomerId))
            .ExecuteDeleteAsync();

        // 删除 Emails
        await _context.Emails.IgnoreQueryFilters()
            .Where(e => ids.Contains(e.CustomerId))
            .ExecuteDeleteAsync();

        // 删除所有已软删除的客户，返回实际删除的行数
        var rowsAffected = await _dbSet.IgnoreQueryFilters()
            .Where(c => ids.Contains(c.Id))
            .ExecuteDeleteAsync();

        return rowsAffected;
    }

    public async Task<Customer?> GetOrCreateByEmailAsync(string email, string? firstName = null, string? lastName = null)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        // 事务与持久化均由服务层统一管理（_unitOfWork.BeginTransactionAsync() + SaveChangesAsync），
        // 仓储层只负责查询与实体状态准备，不得自行 SaveChangesAsync。
        // TOCTOU 防护（唯一约束冲突重试）改由调用方在 SaveChangesAsync 时处理。

        // 1. 查找活跃客户（使用 tracking 模式，便于后续修改）
        var customer = await ApplyIncludes(_dbSet)
            .FirstOrDefaultAsync(c => c.DeletedAt == null && c.Emails.Any(e => e.EmailAddress.ToLower() == normalizedEmail));

        if (customer != null)
        {
            // 与步骤 2（软删除恢复）保持一致的"空白字段补齐"策略：
            // 若传入 firstName/lastName 非空，且客户对应字段为空（空格/空白），则写入该字段。
            // 注意：这里不做"强制覆盖"（因为仓储是通用方法，其他 Email 渠道创建 Conversation 时会调用到，
            //       不能因为一封历史邮件就改掉客户的老姓名）。
            //       "按 Widget 最新提交的姓名强制覆盖"放在 ChatService.TransferToTicketAsync 业务场景里处理。
            bool touched = false;
            if (string.IsNullOrWhiteSpace(customer.FirstName) && !string.IsNullOrEmpty(firstName))
            {
                customer.FirstName = firstName;
                touched = true;
            }
            if (string.IsNullOrWhiteSpace(customer.LastName) && !string.IsNullOrEmpty(lastName))
            {
                customer.LastName = lastName;
                touched = true;
            }
            if (touched)
            {
                customer.UpdatedAt = DateTime.UtcNow;
            }
            return customer;
        }

        // 2. 查找已软删除的客户（恢复而非创建重复记录）
        var softDeletedCustomer = await _dbSet.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.DeletedAt != null && c.Emails.Any(e => e.EmailAddress.ToLower() == normalizedEmail));

        if (softDeletedCustomer != null)
        {
            softDeletedCustomer.DeletedAt = null;
            if (!string.IsNullOrEmpty(firstName)) softDeletedCustomer.FirstName = firstName;
            if (!string.IsNullOrEmpty(lastName)) softDeletedCustomer.LastName = lastName;
            return softDeletedCustomer;
        }

        // 3. 创建新客户（仅 Add 到 ChangeTracker，不 SaveChanges）
        //    TOCTOU 防护：并发请求可能同时到达步骤 1（查不到），然后都执行创建。
        //    唯一约束冲突由调用方在 SaveChangesAsync 时 catch DbUpdateException 处理。
        customer = new Customer
        {
            FirstName = firstName ?? string.Empty,
            LastName = lastName ?? string.Empty
        };

        customer.Emails.Add(new Email { EmailAddress = email, Type = 1 });

        await _dbSet.AddAsync(customer);

        return customer;
    }

    /// <summary>
    /// 根据电话号码查找客户（只读，AsNoTracking）。
    /// 通过匹配 JSON 模式 "Value":"phoneNumber" 实现按列表项精确匹配，避免子串误匹配。
    /// Phones 字段存储格式为 [{"Value":"13800138000","Type":1,"N":null}, ...]，
    /// 使用带引号的键值模式可确保 "138" 不会误匹配 "13800138000"。
    /// </summary>
    public async Task<Customer?> GetByPhoneAsync(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        var normalizedPhone = phoneNumber.Trim();
        // 匹配 JSON 模式 "Value":"<phone>"，避免部分匹配
        // 使用 Like 而非 Contains 以便正确处理可能包含 % 或 _ 的号码
        var pattern = $"\"Value\":\"{normalizedPhone}\"";
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(c => c.DeletedAt == null)
            .FirstOrDefaultAsync(c => EF.Functions.Like(c.Phones, $"%{pattern}%"));
    }

    /// <summary>
    /// 根据电话号码查找客户（tracking，适用于后续修改 + SaveChanges 的场景）。
    /// </summary>
    public async Task<Customer?> GetByPhoneTrackedAsync(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        var normalizedPhone = phoneNumber.Trim();
        var pattern = $"\"Value\":\"{normalizedPhone}\"";
        return await ApplyIncludes(_dbSet)
            .Where(c => c.DeletedAt == null)
            .FirstOrDefaultAsync(c => EF.Functions.Like(c.Phones, $"%{pattern}%"));
    }

    /// <summary>
    /// 按渠道 + 渠道 ID 查找客户（只读，AsNoTracking，不创建）。
    /// </summary>
    public async Task<Customer?> GetByChannelAsync(string channel, string channelId)
    {
        if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(channelId))
            return null;

        var normalizedChannel = channel.Trim().ToLowerInvariant();
        var normalizedChannelId = channelId.Trim();

        return await ApplyIncludes(_dbSet.AsNoTracking())
            .FirstOrDefaultAsync(c => c.DeletedAt == null
                && c.Channel != null
                && c.Channel.ToLower() == normalizedChannel
                && c.ChannelId == normalizedChannelId);
    }

    /// <summary>
    /// 按渠道 + 渠道 ID 查找客户（tracking，适用于后续修改 + SaveChanges 的场景，不创建）。
    /// </summary>
    public async Task<Customer?> GetByChannelTrackedAsync(string channel, string channelId)
    {
        if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(channelId))
            return null;

        var normalizedChannel = channel.Trim().ToLowerInvariant();
        var normalizedChannelId = channelId.Trim();

        return await ApplyIncludes(_dbSet)
            .FirstOrDefaultAsync(c => c.DeletedAt == null
                && c.Channel != null
                && c.Channel.ToLower() == normalizedChannel
                && c.ChannelId == normalizedChannelId);
    }

    /// <summary>
    /// 按渠道标识（如微信 OpenId）获取或创建客户。
    /// 事务与持久化均由服务层统一管理，仓储层只负责查询与实体状态准备，不 SaveChangesAsync。
    /// </summary>
    public async Task<Customer?> GetOrCreateByChannelAsync(string channel, string channelId, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(channelId))
        {
            return null;
        }

        var normalizedChannel = channel.Trim().ToLowerInvariant();
        var normalizedChannelId = channelId.Trim();

        // 1. 查找活跃客户（tracking 模式）
        var customer = await ApplyIncludes(_dbSet)
            .FirstOrDefaultAsync(c => c.DeletedAt == null
                && c.Channel != null
                && c.Channel.ToLower() == normalizedChannel
                && c.ChannelId == normalizedChannelId);

        if (customer != null)
        {
            return customer;
        }

        // 2. 查找已软删除的客户（恢复而非创建重复记录）
        var softDeletedCustomer = await _dbSet.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.DeletedAt != null
                && c.Channel != null
                && c.Channel.ToLower() == normalizedChannel
                && c.ChannelId == normalizedChannelId);

        if (softDeletedCustomer != null)
        {
            softDeletedCustomer.DeletedAt = null;
            if (!string.IsNullOrEmpty(displayName))
            {
                var (lastName, firstName) = ChineseNameSplitter.Split(displayName);
                softDeletedCustomer.LastName = lastName;
                softDeletedCustomer.FirstName = firstName;
            }
            return softDeletedCustomer;
        }

        // 3. 创建新客户（仅 Add 到 ChangeTracker，不 SaveChanges）
        var (ln, fn) = ChineseNameSplitter.Split(displayName);
        customer = new Customer
        {
            FirstName = fn,
            LastName = ln,
            Channel = normalizedChannel,
            ChannelId = normalizedChannelId
        };

        await _dbSet.AddAsync(customer);

        return customer;
    }

    /// <summary>
    /// 获取所有未删除客户的所有邮箱地址（无 Take 限制，用于导入重复检查）。
    /// 通过 SelectMany 投影直接生成 SQL JOIN，避免加载整个 Customer 实体。
    /// </summary>
    public async Task<HashSet<string>> GetAllActiveEmailsAsync()
    {
        var emails = await _dbSet
            .AsNoTracking()
            .Where(c => c.DeletedAt == null)
            .SelectMany(c => c.Emails)
            .Where(e => !string.IsNullOrEmpty(e.EmailAddress))
            .Select(e => e.EmailAddress)
            .ToListAsync();

        return new HashSet<string>(emails, StringComparer.OrdinalIgnoreCase);
    }
}
