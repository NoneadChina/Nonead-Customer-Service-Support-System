using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class AttachmentRepository : Repository<Attachment>, IAttachmentRepository
{
    public AttachmentRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 按 ID 列表批量获取附件（只读场景使用，AsNoTracking）
    /// </summary>
    public async Task<List<Attachment>> GetByIdsAsync(IEnumerable<int> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new List<Attachment>();
        }

        return await _dbSet
            .AsNoTracking()
            .Where(a => idList.Contains(a.Id))
            .ToListAsync();
    }

    /// <summary>
    /// 按 ID 列表批量获取附件（跟踪查询，用于需要修改附件属性的场景，如回填 ThreadId）
    /// </summary>
    public async Task<List<Attachment>> GetByIdsTrackedAsync(IEnumerable<int> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new List<Attachment>();
        }

        return await _dbSet
            .Where(a => idList.Contains(a.Id))
            .ToListAsync();
    }

    public async Task<IEnumerable<Attachment>> GetByThreadIdAsync(int threadId)
    {
        // 防御性限制：单次最多返回 500 条，防止极端场景下海量附件拖垮查询与序列化
        return await _dbSet.AsNoTracking()
            .Where(a => a.ThreadId == threadId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(500)
            .ToListAsync();
    }

    public async Task<IEnumerable<Attachment>> GetByConversationIdAsync(int conversationId)
    {
        // 防御性限制：单次最多返回 500 条，防止极端场景下海量附件拖垮查询与序列化
        return await _dbSet.AsNoTracking()
            .Where(a => a.Thread.ConversationId == conversationId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(500)
            .ToListAsync();
    }

    public async Task<IEnumerable<Attachment>> GetByUserIdAsync(int userId)
    {
        // 防御性限制：单次最多返回 500 条，防止极端场景下海量附件拖垮查询与序列化
        return await _dbSet.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(500)
            .ToListAsync();
    }
}
