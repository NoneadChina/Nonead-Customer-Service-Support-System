using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 会话 - 表示客户与客服之间的完整对话线程
/// </summary>
public class Conversation : ISoftDelete
{
    public int Id { get; set; }
    
    /// <summary>
    /// 工单编号（自动递增，用于客户引用）
    /// </summary>
    public int Number { get; set; }
    
    /// <summary>
    /// 线程数量（不包括LineItem类型）
    /// </summary>
    public int ThreadsCount { get; set; }
    
    public int MailboxId { get; set; }
    public int? CustomerId { get; set; }
    public int? UserId { get; set; }
    public int? FolderId { get; set; }
    
    /// <summary>
    /// 会话类型：Email、Phone、Chat
    /// </summary>
    public ConversationType Type { get; set; } = ConversationType.Email;
    
    /// <summary>
    /// 来源渠道：Customer(客户) 或 User(用户)
    /// </summary>
    public SourceVia SourceVia { get; set; } = SourceVia.Customer;
    
    /// <summary>
    /// 来源类型：Email、Web、API等
    /// </summary>
    public SourceType SourceType { get; set; } = SourceType.Email;
    
    public ConversationStatus Status { get; set; } = ConversationStatus.Active;
    public ConversationState State { get; set; } = ConversationState.Published;

    /// <summary>
    /// 优先级：Low(0)、Normal(1)、High(2)、Urgent(3)
    /// </summary>
    public ConversationPriority Priority { get; set; } = ConversationPriority.Normal;

    public string Subject { get; set; } = string.Empty;
    
    /// <summary>
    /// 客户邮箱（用于回复）
    /// </summary>
    public string CustomerEmail { get; set; } = string.Empty;
    
    /// <summary>
    /// 抄送地址（JSON数组）
    /// </summary>
    public string Cc { get; set; } = string.Empty;
    
    /// <summary>
    /// 密送地址（JSON数组）
    /// </summary>
    public string Bcc { get; set; } = string.Empty;
    
    /// <summary>
    /// 预览文本（最新回复摘要，用于列表显示）
    /// </summary>
    public string Preview { get; set; } = string.Empty;

    /// <summary>
    /// 搜索向量：Subject + Preview + CustomerEmail + Customer 姓名的小写拼接，用于全文 LIKE 搜索优化。
    /// 在保存时通过 DbContext.UpdateSearchVectors 填充。
    /// </summary>
    public string? SearchVector { get; set; }

    public bool HasAttachments { get; set; }
    
    /// <summary>
    /// 是否为导入的历史数据（不触发通知）
    /// </summary>
    public bool Imported { get; set; }
    
    /// <summary>
    /// 创建用户ID（用户创建的会话）
    /// </summary>
    public int? CreatedByUserId { get; set; }
    
    /// <summary>
    /// 创建客户ID（客户创建的会话）
    /// </summary>
    public int? CreatedByCustomerId { get; set; }
    
    /// <summary>
    /// 关闭用户ID
    /// </summary>
    public int? ClosedByUserId { get; set; }
    
    /// <summary>
    /// 最后回复时间
    /// </summary>
    public DateTime? LastReplyAt { get; set; }
    
    /// <summary>
    /// 最后回复来源：Customer 或 User
    /// </summary>
    public SourceVia? LastReplyFrom { get; set; }
    
    /// <summary>
    /// 最后用户更新时间
    /// </summary>
    public DateTime? UserUpdatedAt { get; set; }
    
    public DateTime? ClosedAt { get; set; }
    public int Version { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    // 导航属性
    public Mailbox Mailbox { get; set; } = null!;
    public Customer? Customer { get; set; }
    public User? User { get; set; }
    public User? CreatedByUser { get; set; }
    public User? ClosedByUser { get; set; }
    public Folder? Folder { get; set; }
    public ICollection<ConversationThread> Threads { get; set; } = [];
    public ICollection<Follower> Followers { get; set; } = [];
    public ICollection<ConversationFolder> ConversationFolders { get; set; } = [];
    public ICollection<ConversationTag> ConversationTags { get; set; } = [];
    public ICollection<SatisfactionRating> SatisfactionRatings { get; set; } = [];

    /// <summary>
    /// 设置会话状态
    /// </summary>
    public void SetStatus(ConversationStatus status, User? user = null)
    {
        var oldStatus = Status;
        Status = status;
        UpdatedAt = DateTime.UtcNow;

        if (status == ConversationStatus.Closed)
        {
            ClosedAt = DateTime.UtcNow;
            if (user != null)
            {
                ClosedByUserId = user.Id;
            }
        }
        else if (oldStatus == ConversationStatus.Closed)
        {
            // 从已关闭状态切换到非关闭状态时，清除关闭时间与关闭人，避免数据残留（M-F4）
            // 与 Reopen() 保持一致，确保状态机一致性
            ClosedAt = null;
            ClosedByUserId = null;
        }
    }

    /// <summary>
    /// 关闭会话
    /// </summary>
    /// <param name="closedByUser">执行关闭的用户</param>
    public void Close(User? closedByUser = null)
    {
        Status = ConversationStatus.Closed;
        ClosedAt = DateTime.UtcNow;
        if (closedByUser != null)
        {
            ClosedByUserId = closedByUser.Id;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 重新打开会话
    /// </summary>
    public void Reopen()
    {
        Status = ConversationStatus.Active;
        ClosedAt = null;
        ClosedByUserId = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 分配会话给指定用户（userId &lt;= 0 表示取消分配）
    /// </summary>
    /// <param name="userId">用户ID（0 或负数表示取消分配）</param>
    public void AssignTo(int userId)
    {
        UserId = userId > 0 ? userId : null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 设置会话优先级
    /// </summary>
    /// <param name="priority">优先级</param>
    public void SetPriority(ConversationPriority priority)
    {
        Priority = priority;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 取消分配会话
    /// </summary>
    public void Unassign()
    {
        UserId = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 软删除会话（设置为已删除状态）
    /// </summary>
    public void SoftDelete()
    {
        State = ConversationState.Deleted;
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 恢复软删除的会话
    /// </summary>
    public void Restore()
    {
        State = ConversationState.Published;
        DeletedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 检查会话是否已关闭
    /// </summary>
    public bool IsClosed() => Status == ConversationStatus.Closed;

    /// <summary>
    /// 检查会话是否处于活动状态
    /// </summary>
    public bool IsActive() => Status == ConversationStatus.Active && State == ConversationState.Published;

    /// <summary>
    /// 检查会话是否已被删除（软删除）
    /// </summary>
    public bool IsDeleted() => State == ConversationState.Deleted || DeletedAt != null;

    /// <summary>
    /// 检查会话是否已分配给用户
    /// </summary>
    public bool IsAssigned() => UserId.HasValue;

    /// <summary>
    /// 检查会话是否分配给指定用户
    /// </summary>
    /// <param name="userId">用户ID</param>
    public bool IsAssignedTo(int userId) => UserId == userId;

    /// <summary>
    /// 添加关注者
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>是否添加成功（已存在则返回false）</returns>
    public bool AddFollower(int userId)
    {
        if (IsUserFollowing(userId))
        {
            return false;
        }
        Followers.Add(new Follower { ConversationId = Id, UserId = userId });
        return true;
    }

    /// <summary>
    /// 移除关注者
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>是否移除成功</returns>
    public bool RemoveFollower(int userId)
    {
        var follower = Followers.FirstOrDefault(f => f.UserId == userId);
        if (follower == null)
        {
            return false;
        }
        Followers.Remove(follower);
        return true;
    }

    /// <summary>
    /// 添加会话线程
    /// </summary>
    /// <param name="thread">会话线程</param>
    public void AddThread(ConversationThread thread)
    {
        Threads.Add(thread);
        ThreadsCount++;
        LastReplyAt = DateTime.UtcNow;
        LastReplyFrom = thread.Type == ThreadType.Customer ? SourceVia.Customer : SourceVia.User;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 获取当前应归属的文件夹类型
    /// </summary>
    /// <param name="viewingUserId">查看的用户ID（用于判断是否在"我的"文件夹）</param>
    public FolderType GetFolderType(int? viewingUserId = null)
    {
        return CalculateFolderType(Status, UserId, viewingUserId);
    }

    /// <summary>
    /// 添加标签
    /// </summary>
    /// <param name="tagId">标签ID</param>
    /// <returns>是否添加成功</returns>
    public bool AddTag(int tagId)
    {
        if (ConversationTags.Any(ct => ct.TagId == tagId))
        {
            return false;
        }
        ConversationTags.Add(new ConversationTag { TagId = tagId });
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// 移除标签
    /// </summary>
    /// <param name="tagId">标签ID</param>
    /// <returns>是否移除成功</returns>
    public bool RemoveTag(int tagId)
    {
        var tag = ConversationTags.FirstOrDefault(ct => ct.TagId == tagId);
        if (tag == null)
        {
            return false;
        }
        ConversationTags.Remove(tag);
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// 设置预览文本（截取最新回复摘要）
    /// </summary>
    public void SetPreview(string body, int maxLength = 255)
    {
        if (string.IsNullOrEmpty(body))
        {
            Preview = string.Empty;
            return;
        }
        
        // 移除HTML标签
        var plainText = System.Text.RegularExpressions.Regex.Replace(body, "<[^>]+>", string.Empty);
        // 移除多余空白
        plainText = System.Text.RegularExpressions.Regex.Replace(plainText, @"\s+", " ").Trim();
        
        Preview = plainText.Length > maxLength ? plainText[..maxLength] + "..." : plainText;
    }

    /// <summary>
    /// 更新搜索向量字段，用于 AdvancedSearch 的 LIKE 前缀匹配优化。
    /// 拼接 Subject + Preview + CustomerEmail + Customer 姓名的小写版本。
    /// 注意：调用前需确保 Customer 导航属性已加载（由 DbContext.UpdateSearchVectors 保证）。
    /// </summary>
    public void UpdateSearchVector()
    {
        var customerName = Customer != null
            ? $"{Customer.FirstName} {Customer.LastName}"
            : string.Empty;
        SearchVector = $"{Subject} {Preview} {CustomerEmail} {customerName}".ToLowerInvariant();
    }

    /// <summary>
    /// 检查用户是否关注此会话
    /// </summary>
    public bool IsUserFollowing(int userId)
        => Followers.Any(f => f.UserId == userId);

    /// <summary>
    /// 根据状态和分配人计算应有的文件夹类型
    /// </summary>
    public static FolderType CalculateFolderType(ConversationStatus status, int? assigneeId, int? viewingUserId = null)
    {
        // 已删除
        if (status == ConversationStatus.Spam)
        {
            return FolderType.Spam;
        }
        
        // 已关闭
        if (status == ConversationStatus.Closed)
        {
            return FolderType.Closed;
        }
        
        // 已分配但指定用户查看时显示在"我的"文件夹
        if (viewingUserId.HasValue && assigneeId == viewingUserId.Value)
        {
            return FolderType.Mine;
        }
        
        // 已分配
        if (assigneeId.HasValue)
        {
            return FolderType.Assigned;
        }
        
        // 未分配
        return FolderType.Unassigned;
    }
}
