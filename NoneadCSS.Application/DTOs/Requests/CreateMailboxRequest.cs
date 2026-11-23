using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class CreateMailboxRequest
{
    [Required(ErrorMessage = "邮箱名称不能为空")]
    [MaxLength(200, ErrorMessage = "邮箱名称最多 200 个字符")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "邮箱地址不能为空")]
    [EmailAddress(ErrorMessage = "无效的邮箱格式")]
    [MaxLength(255, ErrorMessage = "邮箱地址最多 255 个字符")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "收件服务器不能为空")]
    [MaxLength(255, ErrorMessage = "收件服务器最多 255 个字符")]
    public string InHost { get; set; } = string.Empty;

    [Range(1, 65535, ErrorMessage = "收件端口无效")]
    public int InPort { get; set; } = 993;

    [Range(0, 3, ErrorMessage = "加密类型无效")]
    public int InEncryption { get; set; } = 2;

    public bool InValidateCert { get; set; } = true;

    [MaxLength(255, ErrorMessage = "收件用户名最多 255 个字符")]
    public string InUsername { get; set; } = string.Empty;

    /// <summary>
    /// 收件服务器密码（或授权码）。最长 1024 字符，避免恶意超长输入。
    /// 更新时若为空则保持原密码不变。
    /// </summary>
    [MaxLength(1024, ErrorMessage = "收件密码最多 1024 个字符")]
    public string InPassword { get; set; } = string.Empty;

    [MaxLength(100, ErrorMessage = "IMAP发送文件夹最多 100 个字符")]
    public string ImapSentFolder { get; set; } = string.Empty;

    [Required(ErrorMessage = "发件服务器不能为空")]
    [MaxLength(255, ErrorMessage = "发件服务器最多 255 个字符")]
    public string OutHost { get; set; } = string.Empty;

    [Range(1, 65535, ErrorMessage = "发件端口无效")]
    public int OutPort { get; set; } = 465;

    [Range(0, 3, ErrorMessage = "加密类型无效")]
    public int OutEncryption { get; set; } = 2;

    [MaxLength(255, ErrorMessage = "发件用户名最多 255 个字符")]
    public string OutUsername { get; set; } = string.Empty;

    /// <summary>
    /// 发件服务器密码（或授权码）。最长 1024 字符。
    /// </summary>
    [MaxLength(1024, ErrorMessage = "发件密码最多 1024 个字符")]
    public string OutPassword { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "别名最多 500 个字符")]
    public string Aliases { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "回复别名最多 500 个字符")]
    public string AliasesReply { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "无效的自动密送邮箱格式")]
    [MaxLength(255, ErrorMessage = "自动密送最多 255 个字符")]
    public string? AutoBcc
    {
        get => _autoBcc;
        set => _autoBcc = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private string? _autoBcc;

    public bool AutoReplyEnabled { get; set; } = false;

    [MaxLength(200, ErrorMessage = "自动回复主题最多 200 个字符")]
    public string AutoReplySubject { get; set; } = string.Empty;

    public string AutoReplyMessage { get; set; } = string.Empty;

    public string BeforeReply { get; set; } = string.Empty;

    public string Signature { get; set; } = string.Empty;

    public bool OauthEnabled { get; set; } = false;

    [MaxLength(50, ErrorMessage = "OAuth提供商最多 50 个字符")]
    public string OauthProvider { get; set; } = string.Empty;

    public string OauthAccessToken { get; set; } = string.Empty;

    public string OauthRefreshToken { get; set; } = string.Empty;

    public DateTime? OauthTokenExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;

    public List<int> UserIds { get; set; } = [];

    [Range(1, 60, ErrorMessage = "获取频率必须在 1-60 分钟之间")]
    public int FetchFrequency { get; set; } = 5;
}
