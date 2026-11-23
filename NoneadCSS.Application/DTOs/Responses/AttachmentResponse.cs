namespace NoneadCSS.Application.DTOs.Responses;

public class AttachmentResponse
{
    public int Id { get; set; }
    public int? ThreadId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public int Type { get; set; }
    public long Size { get; set; }
    public bool Embedded { get; set; }
    public string Url { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
