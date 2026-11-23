using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services;

public interface IFolderService
{
    Task<IEnumerable<FolderResponse>> GetFoldersByMailboxIdAsync(int mailboxId);
    Task<IEnumerable<FolderResponse>> GetFoldersByUserIdAsync(int userId);
    Task<FolderResponse?> GetFolderByIdAsync(int id);
    Task<FolderResponse?> CreateFolderAsync(int mailboxId, string name, int type);
    Task<FolderResponse?> UpdateFolderAsync(int id, string name);
    Task<bool> DeleteFolderAsync(int id);
    
    /// <summary>
    /// 更新会话所属文件夹（根据状态和分配人自动计算）
    /// </summary>
    Task UpdateConversationFolderAsync(Conversation conversation);
    
    /// <summary>
    /// 更新邮箱所有文件夹的计数器
    /// </summary>
    Task UpdateFolderCountersAsync(int mailboxId);
    
    /// <summary>
    /// 增量更新文件夹计数（+1 或 -1）
    /// </summary>
    Task IncrementFolderCountAsync(int folderId, int delta = 1);
    
    /// <summary>
    /// 确保邮箱存在所有必需的文件夹
    /// </summary>
    Task EnsureMailboxFoldersAsync(int mailboxId);

    // 扩展方法 - 对应 PHP 的 UpdateFolderCounters
    Task UpdateFolderCountersByIdAsync(int folderId);
    Task UpdateMailboxFolderCountersAsync(int mailboxId);
    Task UpdateAllFolderCountersAsync();
}
