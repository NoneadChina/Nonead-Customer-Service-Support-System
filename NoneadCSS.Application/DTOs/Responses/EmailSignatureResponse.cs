namespace NoneadCSS.Application.DTOs.Responses;

public class EmailSignatureDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public int? MailboxId { get; set; }
    public string? MailboxName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>软删除时间（null 表示未删除）</summary>
    public DateTime? DeletedAt { get; set; }
}

public class EmailSignatureOverviewResponse
{
    public int TotalSignatures { get; set; }
    public int DefaultSignatures { get; set; }
    public int MailboxSignatures { get; set; }
    public int GlobalSignatures { get; set; }
    public List<EmailSignatureDto> TopSignatures { get; set; } = [];
}
