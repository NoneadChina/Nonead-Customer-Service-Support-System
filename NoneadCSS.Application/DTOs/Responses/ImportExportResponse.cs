namespace NoneadCSS.Application.DTOs.Responses;

public class EntityTypeResponse
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool SupportsExport { get; set; }
    public bool SupportsImport { get; set; }
}

public class ImportPreviewResponse
{
    public bool Valid { get; set; }
    public List<ImportEntityPreview> Entities { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

public class ImportEntityPreview
{
    public string Type { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<string> SampleData { get; set; } = [];
}

public class ImportResponse
{
    public bool Success { get; set; }
    public int TotalCount { get; set; }
    public int ImportedCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = [];
}
