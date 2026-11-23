namespace NoneadCSS.Domain.Entities;

public class ApiKey : ISoftDelete
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int? UserId { get; set; }

    public string Scopes { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime? ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public string? LastUsedIp { get; set; }

    public int UsageCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 乐观锁 RowVersion；UsageCount/LastUsedAt 在高并发 API 调用时容易出现 lost update，
    /// 不加乐观锁情况下并发 SaveChangesAsync 可能把 UsageCount 写成比实际小。
    /// </summary>
    public byte[]? RowVersion { get; set; }

    public User? User { get; set; }
}

public class ApiKeyLog
{
    public int Id { get; set; }

    public int ApiKeyId { get; set; }

    public string Endpoint { get; set; } = string.Empty;

    public string Method { get; set; } = "GET";

    public int StatusCode { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public int DurationMs { get; set; }

    public string? RequestBody { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ApiKey? ApiKey { get; set; }
}
