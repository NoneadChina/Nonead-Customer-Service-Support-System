namespace NoneadCSS.Application.DTOs.Requests;

public class ParseEmailRequest
{
    public string EmlContent { get; set; } = string.Empty;
    public bool? IsBase64 { get; set; }
}
