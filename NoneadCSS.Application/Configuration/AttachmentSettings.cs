namespace NoneadCSS.Application.Configuration;

public class AttachmentSettings
{
    public long MaxSize { get; set; } = 26214400;
    public List<string> AllowedExtensions { get; set; } = ["jpg", "jpeg", "png", "gif", "pdf", "doc", "docx", "xls", "xlsx", "txt"];
    public List<string> AllowedMimeTypes { get; set; } = ["image/", "application/pdf", "text/"];
    public string UploadPath { get; set; } = "Data/Attachments";
}
