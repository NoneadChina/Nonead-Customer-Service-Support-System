using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 文件夹服务 - 管理邮箱文件夹和会话分类逻辑
/// </summary>
public class FolderService : IFolderService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<FolderService> _logger;

    // 文件夹名称映射（中文）
    private static readonly Dictionary<FolderType, string> DefaultFolderNames = new()
    {
        { FolderType.Unassigned, "未分配" },
        { FolderType.Mine, "我的工单" },
        { FolderType.Starred, "已加星标" },
        { FolderType.Drafts, "草稿" },
        { FolderType.Assigned, "已分配" },
        { FolderType.Closed, "已关闭" },
        { FolderType.Deleted, "已删除" },
        { FolderType.Spam, "垃圾邮件" }
    };

    public FolderService(IUnitOfWork unitOfWork, ILogger<FolderService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IEnumerable<FolderResponse>> GetFoldersByMailboxIdAsync(int mailboxId)
    {
        var folders = await _unitOfWork.Folders.GetByMailboxIdAsync(mailboxId);
        return folders.Select(f => f.ToResponse());
    }

    public async Task<IEnumerable<FolderResponse>> GetFoldersByUserIdAsync(int userId)
    {
        var folders = await _unitOfWork.Folders.GetByUserIdAsync(userId);
        return folders.Select(f => f.ToResponse());
    }

    public async Task<FolderResponse?> GetFolderByIdAsync(int id)
    {
        var folder = await _unitOfWork.Folders.GetByIdAsync(id);
        return folder?.ToResponse();
    }

    public async Task<FolderResponse?> CreateFolderAsync(int mailboxId, string name, int type)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(mailboxId);
        if (mailbox == null)
        {
            return null;
        }

        var existingFolder = await _unitOfWork.Folders.GetByMailboxIdAndTypeAsync(mailboxId, type);
        if (existingFolder != null)
        {
            return null;
        }

        var folder = new Folder
        {
            MailboxId = mailboxId,
            Name = name,
            Type = (FolderType)type,
            ActiveCount = 0,
            TotalCount = 0
        };

        await _unitOfWork.Folders.AddAsync(folder);
        await _unitOfWork.SaveChangesAsync();

        return folder.ToResponse();
    }

    public async Task<FolderResponse?> UpdateFolderAsync(int id, string name)
    {
        var folder = await _unitOfWork.Folders.GetByIdTrackedAsync(id);
        if (folder == null)
        {
            return null;
        }

        folder.Name = name;

        await _unitOfWork.SaveChangesAsync();

        return folder.ToResponse();
    }

    public async Task<bool> DeleteFolderAsync(int id)
    {
        // BUG-04 修复：使用 tracked 查询以支持软删除
        var folder = await _unitOfWork.Folders.GetByIdTrackedAsync(id);
        if (folder == null)
        {
            return false;
        }

        // BUG-04 修复：禁止删除系统文件夹（未分配/我的工单/已关闭/垃圾邮件等），
        // 删除系统文件夹会导致 UpdateConversationFolderAsync 找不到目标文件夹，
        // 会话分类逻辑崩溃
        if (folder.IsSystemFolder())
        {
            _logger.LogWarning("拒绝删除系统文件夹：{FolderName}（类型={FolderType}），邮箱ID={MailboxId}",
                folder.Name, folder.Type, folder.MailboxId);
            return false;
        }

        // BUG-04 修复：使用软删除替代硬删除，保留审计记录，避免外键悬空
        folder.SoftDelete();
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    /// <summary>
    /// 更新会话所属文件夹（核心逻辑 - 根据状态和分配人自动计算）
    /// </summary>
    public async Task UpdateConversationFolderAsync(Conversation conversation)
    {
        if (conversation == null)
        {
            return;
        }

        var oldFolderId = conversation.FolderId;

        var newFolderType = Conversation.CalculateFolderType(conversation.Status, conversation.UserId);

        var newFolder = await _unitOfWork.Folders.GetByMailboxIdAndTypeAsync(
            conversation.MailboxId,
            (int)newFolderType);

        if (newFolder == null)
        {
            _logger.LogError("找不到文件夹类型 {FolderType}，邮箱ID={MailboxId}，系统文件夹配置缺失",
                newFolderType, conversation.MailboxId);
            return;
        }

        if (oldFolderId == newFolder.Id)
        {
            return;
        }

        // conversation 已被 EF Core 跟踪，直接修改属性即可
        conversation.FolderId = newFolder.Id;

        // 直接从数据库查询现有关联（追踪查询），不依赖 Conversation.ConversationFolders 导航属性。
        // 因为会话查询（GetByIdTrackedAsync）未 Include ConversationFolders，导航集合可能为空，
        // 此时 Remove 找不到旧关联、Any 误判为不存在，导致旧关联不删除、新关联重复 INSERT 触发 PK 冲突（T000060）。
        var existingFolders = await _unitOfWork.Conversations.GetConversationFoldersTrackedAsync(conversation.Id);

        // 移除所有不等于新文件夹的旧关联
        foreach (var existing in existingFolders.Where(f => f.FolderId != newFolder.Id).ToList())
        {
            _unitOfWork.Conversations.RemoveConversationFolder(existing);
        }

        // 仅当新关联不存在时才添加，避免重复 INSERT 触发 (ConversationId, FolderId) PK 冲突
        if (!existingFolders.Any(f => f.FolderId == newFolder.Id))
        {
            conversation.ConversationFolders.Add(new ConversationFolder
            {
                ConversationId = conversation.Id,
                FolderId = newFolder.Id
            });
        }

        if (oldFolderId.HasValue)
        {
            await IncrementFolderCountAsync(oldFolderId.Value, -1);
        }

        await IncrementFolderCountAsync(newFolder.Id, 1);

        _logger.LogDebug("会话 {ConversationId} 从文件夹 {OldFolderId} 移动到 {NewFolderId}",
            conversation.Id, oldFolderId, newFolder.Id);
    }

    /// <summary>
    /// 更新邮箱所有文件夹的计数器（批量查询，避免 N+1）
    /// </summary>
    public async Task UpdateFolderCountersAsync(int mailboxId)
    {
        var folders = await _unitOfWork.Folders.GetByMailboxIdTrackedAsync(mailboxId);
        if (!folders.Any())
        {
            return;
        }

        await UpdateFolderCountersBatchAsync(folders);

        await _unitOfWork.SaveChangesAsync();
        _logger.LogDebug("已更新邮箱 {MailboxId} 的所有文件夹计数器", mailboxId);
    }

    /// <summary>
    /// 增量更新文件夹计数
    /// </summary>
    public async Task IncrementFolderCountAsync(int folderId, int delta = 1)
    {
        var folder = await _unitOfWork.Folders.GetByIdTrackedAsync(folderId);
        if (folder == null)
        {
            return;
        }

        folder.TotalCount += delta;
        folder.ActiveCount += delta;

        // 确保计数不为负
        if (folder.TotalCount < 0) folder.TotalCount = 0;
        if (folder.ActiveCount < 0) folder.ActiveCount = 0;

        _logger.LogDebug("文件夹 {FolderId} 计数更新：+{Delta}，当前总数={TotalCount}，活跃数={ActiveCount}",
            folderId, delta, folder.TotalCount, folder.ActiveCount);
    }

    /// <summary>
    /// 确保邮箱存在所有必需的文件夹
    /// </summary>
    public async Task EnsureMailboxFoldersAsync(int mailboxId)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(mailboxId);
        if (mailbox == null)
        {
            _logger.LogWarning("尝试为不存在的邮箱 {MailboxId} 创建系统文件夹", mailboxId);
            return;
        }

        var existingFolders = await _unitOfWork.Folders.GetByMailboxIdAsync(mailboxId);
        var existingTypes = existingFolders.Select(f => f.Type).ToHashSet();

        // 仅补齐系统文件夹（DefaultFolderNames 中的类型），避免把 Custom 等非系统类型也自动创建
        var requiredTypes = DefaultFolderNames.Keys.ToList();
        var foldersToCreate = requiredTypes
            .Where(t => !existingTypes.Contains(t))
            .Select(t => new Folder
            {
                MailboxId = mailboxId,
                Type = t,
                Name = DefaultFolderNames[t],
                ActiveCount = 0,
                TotalCount = 0
            })
            .ToList();

        if (!foldersToCreate.Any())
        {
            return;
        }

        using var tx = await _unitOfWork.BeginTransactionAsync();
        try
        {
            foreach (var folder in foldersToCreate)
            {
                await _unitOfWork.Folders.AddAsync(folder);
                _logger.LogInformation("为邮箱 {MailboxId} 创建文件夹：{FolderName}（类型={FolderType}）",
                    mailboxId, folder.Name, folder.Type);
            }

            await _unitOfWork.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("成功为邮箱 {MailboxId} 补齐 {Count} 个系统文件夹", mailboxId, foldersToCreate.Count);
        }
        catch
        {
            // 显式回滚事务（CommitAsync 失败时事务可能仍活动），并清理 ChangeTracker 避免回滚数据被重新写入
            try { await tx.RollbackAsync(); }
            catch (Exception rollbackEx)
            {
                _logger.LogWarning(rollbackEx, "回滚文件夹创建事务失败: MailboxId={MailboxId}", mailboxId);
            }
            _unitOfWork.ClearChanges();
            throw;
        }
    }

    /// <summary>
    /// 批量更新文件夹计数器（一次查询 + 内存分组，避免 N+1）
    /// </summary>
    private async Task UpdateFolderCountersBatchAsync(IEnumerable<Folder> folders)
    {
        var folderIds = folders.Select(f => f.Id).ToList();
        if (folderIds.Count == 0)
        {
            return;
        }

        // 一次查询获取所有文件夹的关联记录
        var conversationFolders = (await _unitOfWork.Repository<ConversationFolder>()
            .FindAsync(cf => folderIds.Contains(cf.FolderId))).ToList();
        var totalCounts = conversationFolders
            .GroupBy(cf => cf.FolderId)
            .ToDictionary(g => g.Key, g => g.Count());

        // 批量查询关联会话的状态，计算活跃数
        var allConvIds = conversationFolders.Select(cf => cf.ConversationId).Distinct().ToList();
        var activeConvIds = new HashSet<int>();
        if (allConvIds.Count > 0)
        {
            var conversations = await _unitOfWork.Conversations.GetByIdsAsync(allConvIds);
            activeConvIds = conversations
                .Where(c => c.Status == ConversationStatus.Active || c.Status == ConversationStatus.Pending)
                .Select(c => c.Id)
                .ToHashSet();
        }
        var activeCounts = conversationFolders
            .Where(cf => activeConvIds.Contains(cf.ConversationId))
            .GroupBy(cf => cf.FolderId)
            .ToDictionary(g => g.Key, g => g.Count());

        // 内存中更新文件夹计数（folder 已通过 tracked 查询，由 SaveChangesAsync 持久化，无需 UpdateAsync）
        foreach (var folder in folders)
        {
            folder.TotalCount = totalCounts.GetValueOrDefault(folder.Id, 0);
            folder.ActiveCount = activeCounts.GetValueOrDefault(folder.Id, 0);
        }
    }

    /// <summary>
    /// 更新单个文件夹的计数器（从数据库重新计算）
    /// </summary>
    private async Task UpdateSingleFolderCounterAsync(Folder folder)
    {
        var conversations = (await _unitOfWork.Repository<ConversationFolder>()
            .FindAsync(cf => cf.FolderId == folder.Id)).ToList();

        var conversationIds = conversations.Select(cf => cf.ConversationId).ToList();

        // 统计总数
        folder.TotalCount = conversationIds.Count;

        // 统计活跃数（状态为 Active 或 Pending 的会话）
        if (conversationIds.Count > 0)
        {
            var convList = await _unitOfWork.Conversations.GetByIdsAsync(conversationIds);
            folder.ActiveCount = convList.Count(c => c.Status == ConversationStatus.Active || c.Status == ConversationStatus.Pending);
        }
        else
        {
            folder.ActiveCount = 0;
        }
    }

    public async Task UpdateFolderCountersByIdAsync(int folderId)
    {
        var folder = await _unitOfWork.Folders.GetByIdTrackedAsync(folderId);
        if (folder == null) return;
        await UpdateSingleFolderCounterAsync(folder);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task UpdateMailboxFolderCountersAsync(int mailboxId)
    {
        var folders = await _unitOfWork.Folders.GetByMailboxIdTrackedAsync(mailboxId);
        if (folders.Any())
        {
            await UpdateFolderCountersBatchAsync(folders);
        }
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task UpdateAllFolderCountersAsync()
    {
        var allFolders = await _unitOfWork.Folders.GetAllTrackedAsync();
        if (allFolders.Any())
        {
            await UpdateFolderCountersBatchAsync(allFolders);
        }
        await _unitOfWork.SaveChangesAsync();
    }
}