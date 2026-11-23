namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 邮箱签名响应
/// </summary>
public class MailboxSignatureResponse
{
    public int MailboxId { get; set; }
    public string? Signature { get; set; }
    public bool IsHtml { get; set; } = true;
    public string? DefaultSignature { get; set; }
}
