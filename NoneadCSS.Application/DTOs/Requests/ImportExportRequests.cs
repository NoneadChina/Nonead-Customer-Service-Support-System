namespace NoneadCSS.Application.DTOs.Requests;

public class ExportRequest
{
    public List<string> EntityTypes { get; set; } = [];
    public string Format { get; set; } = "json";
}

public class ImportRequest
{
    public string Format { get; set; } = "json";
    public string Data { get; set; } = string.Empty;
}

public class ImportPreviewRequest
{
    public string Format { get; set; } = "json";
    public string Data { get; set; } = string.Empty;
}
