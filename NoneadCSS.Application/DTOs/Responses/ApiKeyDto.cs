namespace NoneadCSS.Application.DTOs.Responses;

public class ApiKeyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string KeyPrefix { get; set; } = string.Empty;
    public string? Key { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public List<string> Scopes { get; set; } = new();
    public bool IsActive { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public string? LastUsedIp { get; set; }
    public int UsageCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>乐观锁版本标记（RowVersion base64 字符串），更新/启用停用/重生成时回传 ifMatch。</summary>
    public string? RowVersion { get; set; }
}

public class ApiKeyLogDto
{
    public int Id { get; set; }
    public int ApiKeyId { get; set; }
    public string ApiKeyName { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public int DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ApiKeyOverviewResponse
{
    public int TotalKeys { get; set; }
    public int ActiveKeys { get; set; }
    public int InactiveKeys { get; set; }
    public int TotalUsage { get; set; }
    public List<ApiKeyDto> RecentKeys { get; set; } = new();
    public List<ApiKeyLogDto> RecentLogs { get; set; } = new();
    public Dictionary<string, int> ScopesCount { get; set; } = new();
}
