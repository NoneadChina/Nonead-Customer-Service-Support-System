using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class CreateApiKeyRequest
{
    [Required(ErrorMessage = "密钥名称不能为空")]
    [MaxLength(100, ErrorMessage = "密钥名称最多 100 个字符")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "描述最多 500 个字符")]
    public string? Description { get; set; }

    public int? UserId { get; set; }

    public List<string> Scopes { get; set; } = new();

    public DateTime? ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;
}

public class UpdateApiKeyRequest
{
    [Required(ErrorMessage = "ID不能为空")]
    public int Id { get; set; }

    [Required(ErrorMessage = "密钥名称不能为空")]
    [MaxLength(100, ErrorMessage = "密钥名称最多 100 个字符")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "描述最多 500 个字符")]
    public string? Description { get; set; }

    public List<string> Scopes { get; set; } = new();

    public DateTime? ExpiresAt { get; set; }

    public bool IsActive { get; set; }

    /// <summary>乐观锁版本标记（RowVersion base64）；不匹配时返回 409。</summary>
    public string? IfMatch { get; set; }
}

public class ApiKeyQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int? UserId { get; set; }
    public bool? IsActive { get; set; }
    public string? Keyword { get; set; }
    public string? Scope { get; set; }
}

public class ApiKeyLogQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int? ApiKeyId { get; set; }
    public string? Endpoint { get; set; }
    public int? StatusCode { get; set; }
}
