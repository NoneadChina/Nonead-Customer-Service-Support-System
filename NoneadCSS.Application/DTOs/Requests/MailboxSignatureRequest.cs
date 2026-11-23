using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class MailboxSignatureRequest
{
    [MaxLength(5000)]
    public string? Signature { get; set; }

    public bool IsHtml { get; set; } = true;

    [MaxLength(5000)]
    public string? DefaultSignature { get; set; }
}
