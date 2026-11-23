namespace NoneadCSS.Domain.Entities;

public class Module
{
    /// <summary>核心模块的 Name 常量：永远启用，不能被禁用/删除</summary>
    public const string CoreModuleName = "core";

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = "1.0.0";
    public string? Author { get; set; }
    public string? Icon { get; set; }
    public bool IsActive { get; set; }
    public string Category { get; set; } = "general";
    public string? Settings { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>核心模块（Name == "core"）：永远启用，不能被关闭/删除</summary>
    public bool IsSystemModule => string.Equals(Name, CoreModuleName, StringComparison.OrdinalIgnoreCase);
}
