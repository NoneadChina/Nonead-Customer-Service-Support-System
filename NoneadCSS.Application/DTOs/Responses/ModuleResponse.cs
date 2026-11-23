namespace NoneadCSS.Application.DTOs.Responses;

public class ModuleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Icon { get; set; }
    /// <summary>启用状态。对于核心模块永远为 true。</summary>
    public bool IsActive { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Settings { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>核心模块标识：true 表示不能被禁用/删除。</summary>
    public bool IsSystemModule { get; set; }
}

public class ModuleOverviewResponse
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int Inactive { get; set; }
    public List<ModuleCategoryStats> Categories { get; set; } = [];
}

public class ModuleCategoryStats
{
    public string Category { get; set; } = string.Empty;
    public string CategoryLabel { get; set; } = string.Empty;
    public int Count { get; set; }
    public int ActiveCount { get; set; }
}
