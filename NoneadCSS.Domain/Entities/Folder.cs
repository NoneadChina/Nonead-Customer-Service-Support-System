using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Domain.Entities;

public class Folder : ISoftDelete
{
    public int Id { get; set; }
    public int MailboxId { get; set; }
    public int? UserId { get; set; }
    public FolderType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ActiveCount { get; set; }
    public int TotalCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    public Mailbox Mailbox { get; set; } = null!;
    public User? User { get; set; }
    public ICollection<ConversationFolder> ConversationFolders { get; set; } = [];

    public bool IsDeleted() => DeletedAt.HasValue;

    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Restore()
    {
        DeletedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsSystemFolder() => Type is FolderType.Unassigned or FolderType.Mine or FolderType.Assigned or FolderType.Closed or FolderType.Deleted or FolderType.Spam or FolderType.Drafts or FolderType.Starred;

    public bool IsCustomFolder() => !IsSystemFolder();

    public bool IsPersonalFolder() => UserId.HasValue;

    public bool IsSharedFolder() => !UserId.HasValue && IsCustomFolder();

    public bool BelongsToUser(int userId) => UserId == userId;

    public void UpdateName(string newName)
    {
        Name = newName;
        UpdatedAt = DateTime.UtcNow;
    }

    public void IncrementActiveCount()
    {
        ActiveCount++;
        TotalCount++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void IncrementTotalCount()
    {
        TotalCount++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void DecrementActiveCount()
    {
        if (ActiveCount > 0)
        {
            ActiveCount--;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateCounts(int activeCount, int totalCount)
    {
        ActiveCount = activeCount;
        TotalCount = totalCount;
        UpdatedAt = DateTime.UtcNow;
    }
}
