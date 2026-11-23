using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Infrastructure;
using NoneadCSS.Infrastructure.Persistence;
using System.Linq;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// FolderService 集成测试 - 使用 InMemoryDatabase 验证文件夹关联的真实跟踪行为。
/// 重点验证 T000054：会话状态从 Spam 切换到其他状态时，ConversationFolder 关联能正确更新，
/// 不会因导航集合未加载导致旧关联残留、新关联重复添加（T000060 回归）。
///
/// 注意：InMemoryDatabase 不强制 PK 约束，但 EF Core 变更跟踪器仍然正确跟踪实体的 Added/Deleted/Modified 状态。
/// 本测试通过验证最终 ConversationFolders 状态来确认逻辑正确性：
/// - 旧关联被删除（不存在于最终查询结果中）
/// - 新关联被添加（存在且唯一）
/// </summary>
public class FolderServiceIntegrationTests : IAsyncLifetime
{
    private NoneadCSSDbContext _context = null!;
    private UnitOfWork _unitOfWork = null!;
    private FolderService _folderService = null!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseInMemoryDatabase(databaseName: $"FolderServiceTestDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _context = new NoneadCSSDbContext(options);
        _unitOfWork = new UnitOfWork(_context, NoneadCSS.Tests.Shared.TestServiceProviderBuilder.Build(_context));
        _folderService = new FolderService(_unitOfWork, NullLogger<FolderService>.Instance);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    /// <summary>
    /// 创建测试邮箱并补齐所有系统文件夹
    /// </summary>
    private async Task<Mailbox> CreateMailboxWithFoldersAsync()
    {
        var mailbox = new Mailbox
        {
            Email = "test@example.com",
            Name = "Test Mailbox",
            InHost = "imap.example.com",
            InPort = 993,
            OutHost = "smtp.example.com",
            OutPort = 587
        };
        await _context.Mailboxes.AddAsync(mailbox);
        await _context.SaveChangesAsync();

        await _folderService.EnsureMailboxFoldersAsync(mailbox.Id);

        return mailbox;
    }

    /// <summary>
    /// 创建测试会话
    /// </summary>
    private async Task<Conversation> CreateConversationAsync(int mailboxId, ConversationStatus status, int? folderId = null)
    {
        var conversation = new Conversation
        {
            MailboxId = mailboxId,
            Subject = "Test Conversation",
            Status = status,
            CustomerEmail = "customer@example.com",
            FolderId = folderId,
            Number = new Random().Next(1000, 99999)
        };
        await _context.Conversations.AddAsync(conversation);
        await _context.SaveChangesAsync();

        return conversation;
    }

    /// <summary>
    /// 获取指定会话的所有 ConversationFolder 关联（不追踪）
    /// </summary>
    private async Task<List<ConversationFolder>> GetConversationFoldersAsync(int conversationId)
    {
        return await _context.ConversationFolders
            .AsNoTracking()
            .Where(cf => cf.ConversationId == conversationId)
            .ToListAsync();
    }

    [Fact]
    public async Task UpdateConversationFolderAsync_FromSpamToActive_ShouldTransitionWithoutConflict()
    {
        // Arrange - T000054 核心场景：会话已标记为垃圾邮件，尝试改回活跃
        var mailbox = await CreateMailboxWithFoldersAsync();
        var spamFolder = await _context.Folders.FirstAsync(f => f.MailboxId == mailbox.Id && f.Type == FolderType.Spam);
        var unassignedFolder = await _context.Folders.FirstAsync(f => f.MailboxId == mailbox.Id && f.Type == FolderType.Unassigned);

        var conversation = await CreateConversationAsync(mailbox.Id, ConversationStatus.Spam, spamFolder.Id);

        // 添加 Spam 文件夹关联
        await _context.ConversationFolders.AddAsync(new ConversationFolder
        {
            ConversationId = conversation.Id,
            FolderId = spamFolder.Id
        });
        await _context.SaveChangesAsync();

        // 清除变更跟踪器，模拟从 DB 重新加载的场景
        _context.ChangeTracker.Clear();

        // Act - 从 Spam 切换到 Active
        // 先用 tracked 查询加载会话（模拟 ConversationStatusService.UpdateStatusAsync 的行为）
        // GetByIdTrackedAsync 不 Include ConversationFolders，导航集合为空（这是 T000060 的根因场景）
        var trackedConversation = await _unitOfWork.Conversations.GetByIdTrackedAsync(conversation.Id);
        trackedConversation.Should().NotBeNull();
        trackedConversation!.SetStatus(ConversationStatus.Active);

        // 调用 UpdateConversationFolderAsync（T000060 修复的核心方法）
        await _folderService.UpdateConversationFolderAsync(trackedConversation);
        await _unitOfWork.SaveChangesAsync();

        // Assert - 文件夹关联应正确更新，不残留旧关联
        var folders = await GetConversationFoldersAsync(conversation.Id);
        folders.Should().HaveCount(1, "Spam 关联应被删除，Unassigned 关联应被添加");
        folders[0].FolderId.Should().Be(unassignedFolder.Id);
        trackedConversation.FolderId.Should().Be(unassignedFolder.Id);
    }

    [Fact]
    public async Task UpdateConversationFolderAsync_SpamToActiveToSpamRoundTrip_ShouldNotConflict()
    {
        // Arrange - 验证 Spam → Active → Spam → Active 来回切换不冲突
        var mailbox = await CreateMailboxWithFoldersAsync();
        var spamFolder = await _context.Folders.FirstAsync(f => f.MailboxId == mailbox.Id && f.Type == FolderType.Spam);
        var unassignedFolder = await _context.Folders.FirstAsync(f => f.MailboxId == mailbox.Id && f.Type == FolderType.Unassigned);

        var conversation = await CreateConversationAsync(mailbox.Id, ConversationStatus.Spam, spamFolder.Id);
        await _context.ConversationFolders.AddAsync(new ConversationFolder
        {
            ConversationId = conversation.Id,
            FolderId = spamFolder.Id
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Act & Assert - Spam → Active
        var conv1 = await _unitOfWork.Conversations.GetByIdTrackedAsync(conversation.Id);
        conv1!.SetStatus(ConversationStatus.Active);
        await _folderService.UpdateConversationFolderAsync(conv1);
        await _unitOfWork.SaveChangesAsync();

        var folders1 = await GetConversationFoldersAsync(conversation.Id);
        folders1.Should().HaveCount(1);
        folders1[0].FolderId.Should().Be(unassignedFolder.Id);

        _context.ChangeTracker.Clear();

        // Act & Assert - Active → Spam
        var conv2 = await _unitOfWork.Conversations.GetByIdTrackedAsync(conversation.Id);
        conv2!.SetStatus(ConversationStatus.Spam);
        await _folderService.UpdateConversationFolderAsync(conv2);
        await _unitOfWork.SaveChangesAsync();

        var folders2 = await GetConversationFoldersAsync(conversation.Id);
        folders2.Should().HaveCount(1);
        folders2[0].FolderId.Should().Be(spamFolder.Id);

        _context.ChangeTracker.Clear();

        // Act & Assert - Spam → Active（第二次切换，验证不会因残留数据冲突）
        var conv3 = await _unitOfWork.Conversations.GetByIdTrackedAsync(conversation.Id);
        conv3!.SetStatus(ConversationStatus.Active);
        await _folderService.UpdateConversationFolderAsync(conv3);
        await _unitOfWork.SaveChangesAsync();

        var folders3 = await GetConversationFoldersAsync(conversation.Id);
        folders3.Should().HaveCount(1);
        folders3[0].FolderId.Should().Be(unassignedFolder.Id);
    }

    [Fact]
    public async Task UpdateConversationFolderAsync_FromSpamToClosed_ShouldTransitionCorrectly()
    {
        // Arrange - Spam → Closed
        var mailbox = await CreateMailboxWithFoldersAsync();
        var spamFolder = await _context.Folders.FirstAsync(f => f.MailboxId == mailbox.Id && f.Type == FolderType.Spam);
        var closedFolder = await _context.Folders.FirstAsync(f => f.MailboxId == mailbox.Id && f.Type == FolderType.Closed);

        var conversation = await CreateConversationAsync(mailbox.Id, ConversationStatus.Spam, spamFolder.Id);
        await _context.ConversationFolders.AddAsync(new ConversationFolder
        {
            ConversationId = conversation.Id,
            FolderId = spamFolder.Id
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Act
        var conv = await _unitOfWork.Conversations.GetByIdTrackedAsync(conversation.Id);
        conv!.SetStatus(ConversationStatus.Closed);
        await _folderService.UpdateConversationFolderAsync(conv);
        await _unitOfWork.SaveChangesAsync();

        // Assert
        var folders = await GetConversationFoldersAsync(conversation.Id);
        folders.Should().HaveCount(1);
        folders[0].FolderId.Should().Be(closedFolder.Id);
    }

    [Fact]
    public async Task UpdateConversationFolderAsync_FromSpamToPending_ShouldTransitionCorrectly()
    {
        // Arrange - Spam → Pending
        var mailbox = await CreateMailboxWithFoldersAsync();
        var spamFolder = await _context.Folders.FirstAsync(f => f.MailboxId == mailbox.Id && f.Type == FolderType.Spam);
        var unassignedFolder = await _context.Folders.FirstAsync(f => f.MailboxId == mailbox.Id && f.Type == FolderType.Unassigned);

        var conversation = await CreateConversationAsync(mailbox.Id, ConversationStatus.Spam, spamFolder.Id);
        await _context.ConversationFolders.AddAsync(new ConversationFolder
        {
            ConversationId = conversation.Id,
            FolderId = spamFolder.Id
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Act
        var conv = await _unitOfWork.Conversations.GetByIdTrackedAsync(conversation.Id);
        conv!.SetStatus(ConversationStatus.Pending);
        await _folderService.UpdateConversationFolderAsync(conv);
        await _unitOfWork.SaveChangesAsync();

        // Assert
        var folders = await GetConversationFoldersAsync(conversation.Id);
        folders.Should().HaveCount(1);
        folders[0].FolderId.Should().Be(unassignedFolder.Id);
    }

    [Fact]
    public async Task UpdateConversationFolderAsync_WithMultipleFolderAssociations_ShouldCleanUpAndKeepOnlyNew()
    {
        // Arrange - 模拟脏数据：会话同时关联了 Spam 和 Unassigned 两个文件夹
        // 这可能是 T000060 修复前遗留的脏数据
        var mailbox = await CreateMailboxWithFoldersAsync();
        var spamFolder = await _context.Folders.FirstAsync(f => f.MailboxId == mailbox.Id && f.Type == FolderType.Spam);
        var unassignedFolder = await _context.Folders.FirstAsync(f => f.MailboxId == mailbox.Id && f.Type == FolderType.Unassigned);
        var closedFolder = await _context.Folders.FirstAsync(f => f.MailboxId == mailbox.Id && f.Type == FolderType.Closed);

        var conversation = await CreateConversationAsync(mailbox.Id, ConversationStatus.Spam, spamFolder.Id);
        await _context.ConversationFolders.AddRangeAsync(
            new ConversationFolder { ConversationId = conversation.Id, FolderId = spamFolder.Id },
            new ConversationFolder { ConversationId = conversation.Id, FolderId = unassignedFolder.Id }
        );
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Act - 切换到 Closed 状态
        var conv = await _unitOfWork.Conversations.GetByIdTrackedAsync(conversation.Id);
        conv!.SetStatus(ConversationStatus.Closed);
        await _folderService.UpdateConversationFolderAsync(conv);
        await _unitOfWork.SaveChangesAsync();

        // Assert - 应只保留 Closed 文件夹关联，Spam 和 Unassigned 都被移除
        var folders = await GetConversationFoldersAsync(conversation.Id);
        folders.Should().HaveCount(1, "脏数据应被清理，仅保留目标文件夹关联");
        folders[0].FolderId.Should().Be(closedFolder.Id);
    }

    [Fact]
    public async Task UpdateConversationFolderAsync_FromActiveToSpamThenToActiveWithAssignee_ShouldTransitionCorrectly()
    {
        // Arrange - 验证带 assignee 的场景：Active → Spam → Active（带 assignee）
        var mailbox = await CreateMailboxWithFoldersAsync();
        var spamFolder = await _context.Folders.FirstAsync(f => f.MailboxId == mailbox.Id && f.Type == FolderType.Spam);
        var assignedFolder = await _context.Folders.FirstAsync(f => f.MailboxId == mailbox.Id && f.Type == FolderType.Assigned);

        // 创建一个已分配的会话
        var conversation = await CreateConversationAsync(mailbox.Id, ConversationStatus.Active, null);
        conversation.AssignTo(5); // 分配给用户 5
        await _context.SaveChangesAsync();

        await _context.ConversationFolders.AddAsync(new ConversationFolder
        {
            ConversationId = conversation.Id,
            FolderId = assignedFolder.Id
        });
        conversation.FolderId = assignedFolder.Id;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Act 1 - Active → Spam
        var conv1 = await _unitOfWork.Conversations.GetByIdTrackedAsync(conversation.Id);
        conv1!.SetStatus(ConversationStatus.Spam);
        await _folderService.UpdateConversationFolderAsync(conv1);
        await _unitOfWork.SaveChangesAsync();

        var folders1 = await GetConversationFoldersAsync(conversation.Id);
        folders1.Should().HaveCount(1);
        folders1[0].FolderId.Should().Be(spamFolder.Id);

        _context.ChangeTracker.Clear();

        // Act 2 - Spam → Active（会话仍有 assignee，应回到 Assigned 文件夹）
        var conv2 = await _unitOfWork.Conversations.GetByIdTrackedAsync(conversation.Id);
        conv2!.SetStatus(ConversationStatus.Active);
        await _folderService.UpdateConversationFolderAsync(conv2);
        await _unitOfWork.SaveChangesAsync();

        // Assert
        var folders2 = await GetConversationFoldersAsync(conversation.Id);
        folders2.Should().HaveCount(1);
        folders2[0].FolderId.Should().Be(assignedFolder.Id);
    }

    [Fact]
    public async Task UpdateConversationFolderAsync_FromSpamToActive_NoExistingFolderAssociation_ShouldStillWork()
    {
        // Arrange - 会话状态为 Spam 但没有任何 ConversationFolder 关联记录
        // （可能由旧版代码或手动数据库操作导致）
        var mailbox = await CreateMailboxWithFoldersAsync();
        var spamFolder = await _context.Folders.FirstAsync(f => f.MailboxId == mailbox.Id && f.Type == FolderType.Spam);
        var unassignedFolder = await _context.Folders.FirstAsync(f => f.MailboxId == mailbox.Id && f.Type == FolderType.Unassigned);

        var conversation = await CreateConversationAsync(mailbox.Id, ConversationStatus.Spam, spamFolder.Id);
        // 故意不创建 ConversationFolder 关联
        _context.ChangeTracker.Clear();

        // Act
        var conv = await _unitOfWork.Conversations.GetByIdTrackedAsync(conversation.Id);
        conv!.SetStatus(ConversationStatus.Active);
        await _folderService.UpdateConversationFolderAsync(conv);
        await _unitOfWork.SaveChangesAsync();

        // Assert - 应正确创建新的 Unassigned 关联
        var folders = await GetConversationFoldersAsync(conversation.Id);
        folders.Should().HaveCount(1);
        folders[0].FolderId.Should().Be(unassignedFolder.Id);
    }
}
