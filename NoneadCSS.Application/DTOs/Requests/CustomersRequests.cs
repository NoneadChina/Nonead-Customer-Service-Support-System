using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class BulkDeleteRequest
{
    /// <summary>客户 ID 列表（S7: 单次最多 500 个，防超大请求体）</summary>
    [MaxLength(500)]
    public List<int> Ids { get; set; } = new();
}
