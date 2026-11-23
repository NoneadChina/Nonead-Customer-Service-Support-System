using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface IAttachmentRepository : IRepository<Attachment>
{
    /// <summary>
    /// 按 ID 列表批量获取附件（避免 N+1 查询，只读场景使用）
    /// </summary>
    Task<List<Attachment>> GetByIdsAsync(IEnumerable<int> ids);

    /// <summary>
    /// 按 ID 列表批量获取附件（跟踪查询，用于需要修改附件属性的场景，如回填 ThreadId）
    /// </summary>
    Task<List<Attachment>> GetByIdsTrackedAsync(IEnumerable<int> ids);

    Task<IEnumerable<Attachment>> GetByThreadIdAsync(int threadId);
    Task<IEnumerable<Attachment>> GetByConversationIdAsync(int conversationId);
    Task<IEnumerable<Attachment>> GetByUserIdAsync(int userId);
}
