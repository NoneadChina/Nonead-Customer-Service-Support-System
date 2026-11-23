using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain;
using MimeKit;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 邮件处理服务接口 - 处理邮件解析、线程匹配、附件提取等
/// </summary>
public interface IEmailProcessingService
{
    /// <summary>
    /// 解析邮件并提取关键信息
    /// </summary>
    EmailParseResult ParseEmail(MimeMessage message, int mailboxId);
    
    /// <summary>
    /// 根据邮件头查找关联的会话
    /// </summary>
    Task<Conversation?> FindRelatedConversationAsync(MimeMessage message, int mailboxId, IUnitOfWork unitOfWork);
    
    /// <summary>
    /// 从邮件创建线程对象
    /// </summary>
    ConversationThread CreateThreadFromEmail(MimeMessage message, EmailParseResult parseResult, int customerId, Customer? customer = null);
    
    /// <summary>
    /// 保存邮件附件
    /// </summary>
    Task<List<Attachment>> SaveAttachmentsAsync(MimeMessage message, int threadId, string attachmentBasePath);
    
    /// <summary>
    /// 生成Message-ID（用于发送回复）
    /// </summary>
    string GenerateMessageId(int threadId, string domain);
}

/// <summary>
/// 邮件解析结果
/// </summary>
public class EmailParseResult
{
    /// <summary>
    /// Message-ID 头
    /// </summary>
    public string MessageId { get; set; } = string.Empty;
    
    /// <summary>
    /// In-Reply-To 头
    /// </summary>
    public string? InReplyTo { get; set; }
    
    /// <summary>
    /// References 头（引用链）
    /// </summary>
    public List<string> References { get; set; } = [];
    
    /// <summary>
    /// 发件人邮箱
    /// </summary>
    public string FromEmail { get; set; } = string.Empty;
    
    /// <summary>
    /// 发件人名称
    /// </summary>
    public string FromName { get; set; } = string.Empty;
    
    /// <summary>
    /// 收件人列表
    /// </summary>
    public List<EmailAddress> ToAddresses { get; set; } = [];
    
    /// <summary>
    /// 抄送列表
    /// </summary>
    public List<EmailAddress> CcAddresses { get; set; } = [];
    
    /// <summary>
    /// 密送列表
    /// </summary>
    public List<EmailAddress> BccAddresses { get; set; } = [];
    
    /// <summary>
    /// 主题
    /// </summary>
    public string Subject { get; set; } = string.Empty;
    
    /// <summary>
    /// HTML正文
    /// </summary>
    public string HtmlBody { get; set; } = string.Empty;
    
    /// <summary>
    /// 纯文本正文
    /// </summary>
    public string TextBody { get; set; } = string.Empty;
    
    /// <summary>
    /// 发送时间
    /// </summary>
    public DateTime Date { get; set; }
    
    /// <summary>
    /// 是否有附件
    /// </summary>
    public bool HasAttachments { get; set; }
    
    /// <summary>
    /// 是否为自动回复邮件
    /// </summary>
    public bool IsAutoReply { get; set; }
    
    /// <summary>
    /// 是否为退信
    /// </summary>
    public bool IsBounce { get; set; }
    
    /// <summary>
    /// 退信详情（如果是退信）
    /// </summary>
    public BounceInfo? BounceInfo { get; set; }
    
    /// <summary>
    /// 原始邮件头
    /// </summary>
    public string RawHeaders { get; set; } = string.Empty;
}

/// <summary>
/// 退信信息
/// </summary>
public class BounceInfo
{
    public string? OriginalMessageId { get; set; }
    public string? RecipientEmail { get; set; }
    public string? BounceType { get; set; }
    public string? BounceSubtype { get; set; }
    public string? DiagnosticCode { get; set; }
    public string? StatusCode { get; set; }
    public string? Action { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// 邮箱地址
/// </summary>
public class EmailAddress
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}