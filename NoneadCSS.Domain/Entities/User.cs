using System.ComponentModel.DataAnnotations.Schema;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Domain.Entities;

public class User : ISoftDelete
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public UserRoleType Role { get; set; } = UserRoleType.ReadOnly;
    public UserStatus Status { get; set; } = UserStatus.Active;
    public string Timezone { get; set; } = "UTC";
    /// <summary>
    /// 界面语言。默认值与创建用户时的口径保持一致（<c>AuthService</c>、<c>SetupController</c>、
    /// <c>UserService</c> 均落 <c>zh-Hans</c>，API 默认请求文化也是 <c>zh-Hans</c>），
    /// 避免「实体默认 en、创建路径写 zh-Hans」两套口径并存。支持值见 <c>LocalizationService.SupportedCultures</c>。
    /// </summary>
    public string Locale { get; set; } = "zh-Hans";
    public string Permissions { get; set; } = string.Empty;
    /// <summary>用户头像 URL（指向 /api/attachments/{id}/public-preview），null 表示无头像</summary>
    public string? Avatar { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEnd { get; set; }

    public bool EmailOnNewConversation { get; set; } = true;
    public bool EmailOnAssignedToMe { get; set; } = true;
    public bool EmailOnNewReply { get; set; }
    public bool EmailOnMention { get; set; } = true;
    public bool BrowserOnNewConversation { get; set; } = true;
    public bool BrowserOnAssignedToMe { get; set; } = true;
    public bool BrowserOnNewReply { get; set; } = true;
    public bool BrowserOnMention { get; set; } = true;

    public string? DefaultSignature { get; set; }
    public bool UseGlobalSignature { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 乐观锁 RowVersion；登录限流/通知设置/权限/签名/用户资料编辑均可能出现并发写。
    /// 例如 FailedLoginAttempts/LockoutEnd 在同账号多点登录并发下若不加锁，会被后写入覆盖导致计数丢失。
    /// </summary>
    public byte[]? RowVersion { get; set; }

    [NotMapped]
    public ICollection<Conversation> Conversations { get; set; } = [];

    [NotMapped]
    public ICollection<ConversationThread> Threads { get; set; } = [];

    public ICollection<Folder> Folders { get; set; } = [];
    public ICollection<MailboxUser> MailboxUsers { get; set; } = [];
    public ICollection<Follower> Followers { get; set; } = [];
    public ICollection<SendLog> SendLogs { get; set; } = [];

    /// <summary>
    /// 验证用户是否为管理员
    /// </summary>
    public bool IsAdmin() => Role == UserRoleType.SuperAdmin;

    /// <summary>
    /// 验证用户是否处于激活状态
    /// </summary>
    public bool IsActive() => Status == UserStatus.Active;

    /// <summary>
    /// 验证用户账号是否待激活
    /// </summary>
    public bool IsPendingActivation() => Status == UserStatus.PendingActivation;

    /// <summary>
    /// 验证用户账号是否已禁用
    /// </summary>
    public bool IsDisabled() => Status == UserStatus.Disabled;

    /// <summary>
    /// 获取用户全名
    /// </summary>
    public string GetFullName() => $"{FirstName} {LastName}".Trim();

    /// <summary>
    /// 验证密码是否正确
    /// </summary>
    /// <param name="password">明文密码</param>
    /// <param name="passwordHasher">密码哈希验证器</param>
    /// <returns>密码是否匹配</returns>
    public bool VerifyPassword(string password, IPasswordHasher passwordHasher)
    {
        if (string.IsNullOrEmpty(Password) || string.IsNullOrEmpty(password))
        {
            return false;
        }
        return passwordHasher.Verify(password, Password);
    }

    /// <summary>
    /// 设置密码（通过密码哈希器进行哈希处理）
    /// </summary>
    /// <param name="plainPassword">明文密码</param>
    /// <param name="passwordHasher">密码哈希器</param>
    public void SetPassword(string plainPassword, IPasswordHasher passwordHasher)
    {
        if (string.IsNullOrEmpty(plainPassword))
        {
            throw new ArgumentException("密码不能为空", nameof(plainPassword));
        }
        Password = passwordHasher.Hash(plainPassword);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 激活用户账号
    /// </summary>
    public void Activate()
    {
        Status = UserStatus.Active;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 禁用用户账号
    /// </summary>
    public void Disable()
    {
        Status = UserStatus.Disabled;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 设置用户状态
    /// </summary>
    /// <param name="status">新状态</param>
    public void SetStatus(UserStatus status)
    {
        Status = status;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 设置用户角色
    /// </summary>
    /// <param name="role">新角色</param>
    public void SetRole(UserRoleType role)
    {
        Role = role;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 检查是否拥有指定权限（管理员自动拥有所有权限）
    /// </summary>
    /// <param name="permission">权限ID</param>
    /// <returns>是否有权限</returns>
    public bool HasPermission(int permission)
    {
        if (IsAdmin())
        {
            return true;
        }
        return GetPermissions().Contains(permission);
    }

    /// <summary>
    /// 检查是否可以管理用户
    /// </summary>
    public bool CanManageUsers() => IsAdmin();

    /// <summary>
    /// 检查是否可以管理邮箱
    /// </summary>
    public bool CanManageMailboxes() => IsAdmin();

    /// <summary>
    /// 检查是否可以管理系统设置
    /// </summary>
    public bool CanManageSettings() => IsAdmin();

    /// <summary>
    /// 检查是否可以删除会话
    /// </summary>
    public bool CanDeleteConversations() => IsAdmin() || HasPermission(UserPermissions.DeleteConversations);

    /// <summary>
    /// 检查是否可以编辑会话
    /// </summary>
    public bool CanEditConversations() => IsAdmin() || HasPermission(UserPermissions.EditConversations);

    /// <summary>
    /// 检查是否可以导出数据
    /// </summary>
    public bool CanExportData() => IsAdmin();

    /// <summary>
    /// 检查是否可以查看所有会话
    /// </summary>
    public bool CanViewAllConversations() => IsAdmin();

    /// <summary>
    /// 检查用户是否有权限访问指定邮箱
    /// </summary>
    /// <param name="mailboxId">邮箱ID</param>
    /// <returns>是否有权限</returns>
    public bool CanAccessMailbox(int mailboxId)
    {
        if (IsAdmin())
        {
            return true;
        }
        return MailboxUsers.Any(mu => mu.MailboxId == mailboxId);
    }

    /// <summary>
    /// 获取用户可访问的邮箱ID列表
    /// </summary>
    public List<int> GetAccessibleMailboxIds()
    {
        return MailboxUsers.Select(mu => mu.MailboxId).ToList();
    }

    public List<int> GetPermissions()
    {
        if (string.IsNullOrEmpty(Permissions))
        {
            return [];
        }
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<int>>(Permissions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SetPermissions(List<int> permissions)
    {
        Permissions = System.Text.Json.JsonSerializer.Serialize(permissions);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 更新用户通知设置
    /// </summary>
    public void UpdateNotificationSettings(
        string locale,
        string timezone,
        bool emailOnNewConversation,
        bool emailOnAssignedToMe,
        bool emailOnNewReply,
        bool emailOnMention,
        bool browserOnNewConversation,
        bool browserOnAssignedToMe,
        bool browserOnNewReply,
        bool browserOnMention,
        string? defaultSignature,
        bool useGlobalSignature)
    {
        Locale = locale;
        Timezone = timezone;
        EmailOnNewConversation = emailOnNewConversation;
        EmailOnAssignedToMe = emailOnAssignedToMe;
        EmailOnNewReply = emailOnNewReply;
        EmailOnMention = emailOnMention;
        BrowserOnNewConversation = browserOnNewConversation;
        BrowserOnAssignedToMe = browserOnAssignedToMe;
        BrowserOnNewReply = browserOnNewReply;
        BrowserOnMention = browserOnMention;
        DefaultSignature = defaultSignature;
        UseGlobalSignature = useGlobalSignature;
        UpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// 密码哈希器接口（领域层抽象，基础设施层实现）
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// 哈希明文密码
    /// </summary>
    /// <param name="password">明文密码</param>
    /// <returns>哈希后的密码</returns>
    string Hash(string password);

    /// <summary>
    /// 验证密码
    /// </summary>
    /// <param name="password">明文密码</param>
    /// <param name="hashedPassword">哈希后的密码</param>
    /// <returns>是否匹配</returns>
    bool Verify(string password, string hashedPassword);
}

public static class UserPermissions
{
    public const int DeleteConversations = 1;
    public const int EditConversations = 2;
    public const int EditSavedReplies = 3;
    public const int EditTags = 4;
    public const int EditCustomFolders = 5;
    public const int EditUsers = 10;

    public static readonly List<int> AllPermissions = [
        DeleteConversations,
        EditConversations,
        EditSavedReplies,
        EditTags,
        EditCustomFolders,
        EditUsers
    ];
}
