namespace NoneadCSS.Application.DTOs.Responses;

public class MailboxResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    // 收信配置
    public string InHost { get; set; } = string.Empty;
    public int InPort { get; set; }
    public int InEncryption { get; set; }
    public bool InValidateCert { get; set; }
    public string InUsername { get; set; } = string.Empty;
    public bool HasInPassword { get; set; }
    public string ImapSentFolder { get; set; } = string.Empty;

    // 发信配置
    public string OutHost { get; set; } = string.Empty;
    public int OutPort { get; set; }
    public int OutEncryption { get; set; }
    public string OutUsername { get; set; } = string.Empty;
    public bool HasOutPassword { get; set; }

    // 别名和自动回复
    public string Aliases { get; set; } = string.Empty;
    public string AliasesReply { get; set; } = string.Empty;
    public string AutoBcc { get; set; } = string.Empty;
    public bool AutoReplyEnabled { get; set; }
    public string AutoReplySubject { get; set; } = string.Empty;
    public string AutoReplyMessage { get; set; } = string.Empty;
    public string BeforeReply { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public bool OauthEnabled { get; set; }
    public string OauthProvider { get; set; } = string.Empty;
    public DateTime? OauthTokenExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;
    public List<int> UserIds { get; set; } = [];
    public List<FolderResponse> Folders { get; set; } = [];
    public int FetchFrequency { get; set; }
    public DateTime? LastFetchAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    // 统计信息
    public int ConversationCount { get; set; }
    public int OpenConversationCount { get; set; }
    public int TodaySendCount { get; set; }
    public int TodayReceiveCount { get; set; }
}
