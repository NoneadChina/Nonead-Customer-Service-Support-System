using NoneadCSS.Domain.Enums;
using System.Text.Json.Serialization;

namespace NoneadCSS.Domain.Entities;

public class Mailbox : ISoftDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public InProtocol InProtocol { get; set; } = InProtocol.Imap;
    public string InHost { get; set; } = string.Empty;
    public int InPort { get; set; } = 993;
    public EncryptionType InEncryption { get; set; } = EncryptionType.Ssl;
    public bool InValidateCert { get; set; } = true;
    public string InUsername { get; set; } = string.Empty;
    public string InPassword { get; set; } = string.Empty;
    public string ImapSentFolder { get; set; } = string.Empty;
    public OutMethod OutMethod { get; set; } = OutMethod.Smtp;
    public string OutHost { get; set; } = string.Empty;
    public int OutPort { get; set; } = 465;
    public EncryptionType OutEncryption { get; set; } = EncryptionType.Ssl;
    public string OutUsername { get; set; } = string.Empty;
    public string OutPassword { get; set; } = string.Empty;
    public string Aliases { get; set; } = string.Empty;
    public string AliasesReply { get; set; } = string.Empty;
    public string AutoBcc { get; set; } = string.Empty;
    public bool AutoReplyEnabled { get; set; } = false;
    public string AutoReplySubject { get; set; } = string.Empty;
    public string AutoReplyMessage { get; set; } = string.Empty;
    public string BeforeReply { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public bool OauthEnabled { get; set; } = false;
    public string OauthProvider { get; set; } = string.Empty;
    public string OauthAccessToken { get; set; } = string.Empty;
    public string OauthRefreshToken { get; set; } = string.Empty;
    public DateTime? OauthTokenExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
    public ulong LastUid { get; set; } = 0;
    public int FetchFrequency { get; set; } = 5;
    public DateTime? LastFetchAt { get; set; }

    /// <summary>
    /// Microsoft Graph API delta token，用于 Outlook 日历增量同步。
    /// 首次全量同步后写入；下次同步使用此 token 仅拉取增量变更。
    /// 增量失败时重置为 null，回退全量拉取保证不丢数据。
    /// </summary>
    public string? OutlookDeltaToken { get; set; }

    [JsonIgnore]
    public ICollection<Conversation> Conversations { get; set; } = [];
    public ICollection<Folder> Folders { get; set; } = [];
    public ICollection<MailboxUser> MailboxUsers { get; set; } = [];

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

    public bool IsInActive() => !string.IsNullOrEmpty(InHost);

    public bool IsOutActive() => !string.IsNullOrEmpty(OutHost);

    public bool IsMailboxActive() => IsInActive() && IsOutActive();

    public bool IsOauthExpired() => OauthEnabled && OauthTokenExpiresAt.HasValue && OauthTokenExpiresAt.Value < DateTime.UtcNow;

    public bool IsOauthValid() => OauthEnabled && !IsOauthExpired();

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateName(string newName)
    {
        Name = newName;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateEmail(string newEmail)
    {
        Email = newEmail;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateOauthToken(string accessToken, string refreshToken, DateTime expiresAt)
    {
        OauthAccessToken = accessToken;
        OauthRefreshToken = refreshToken;
        OauthTokenExpiresAt = expiresAt;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateLastUid(ulong uid)
    {
        LastUid = uid;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateLastFetchTime()
    {
        LastFetchAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void EnableAutoReply(string subject, string message)
    {
        AutoReplyEnabled = true;
        AutoReplySubject = subject;
        AutoReplyMessage = message;
        UpdatedAt = DateTime.UtcNow;
    }

    public void DisableAutoReply()
    {
        AutoReplyEnabled = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateSignature(string signature)
    {
        Signature = signature;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool HasUser(int userId) => MailboxUsers.Any(mu => mu.UserId == userId);

    public bool CanUserAccess(int userId) => IsActive && !IsDeleted() && HasUser(userId);

    public List<int> GetUserIds() => MailboxUsers.Select(mu => mu.UserId).ToList();

    public int UserCount => MailboxUsers.Count;

    public bool IsEmpty() => UserCount == 0;

    public bool AddUser(int userId)
    {
        if (HasUser(userId))
        {
            return false;
        }
        MailboxUsers.Add(new MailboxUser { MailboxId = Id, UserId = userId });
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public bool RemoveUser(int userId)
    {
        var mailboxUser = MailboxUsers.FirstOrDefault(mu => mu.UserId == userId);
        if (mailboxUser == null)
        {
            return false;
        }
        MailboxUsers.Remove(mailboxUser);
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public bool IsValidForFetch() => IsActive && !IsDeleted() && IsInActive() && !IsOauthExpired();

    public string GetIncomingServerAddress() => $"{InHost}:{InPort}";

    public string GetOutgoingServerAddress() => $"{OutHost}:{OutPort}";
}
