using System.Text.Json.Serialization;
using NoneadCSS.Application.DTOs.Requests;

namespace NoneadCSS.Application.DTOs.Responses;

public class CustomerResponse
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? MainEmail { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
    public string? Company { get; set; }
    public string? JobTitle { get; set; }
    public string? Notes { get; set; }
    /// <summary>客户来源渠道（如 wechat、email、phone）</summary>
    public string? Channel { get; set; }
    /// <summary>渠道对应的外部标识（微信 OpenId 等）</summary>
    public string? ChannelId { get; set; }
    public List<CustomerPhoneDto>? Phones { get; set; }
    public List<CustomerSocialProfileDto>? SocialProfiles { get; set; }
    public List<string>? Websites { get; set; }
    public int ConversationsCount { get; set; }
    public string? Avatar { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 乐观锁版本标记（RowVersion base64 字符串）。
    /// 客户资料编辑、批量打标签、合并、分配、渠道画像回写时回传 ifMatch = rowVersion。
    /// </summary>
    [JsonPropertyName("rowVersion")]
    public string? RowVersion { get; set; }
}

/// <summary>
/// 客户更新请求扩展：在共用的 CreateCustomerRequest 字段之上附加乐观锁 If-Match。
/// 前端调用 PUT /customers/{id} 时建议使用本类型而非 CreateCustomerRequest 单独传 ifMatch。
/// 为保持兼容，CustomerService.UpdateCustomerAsync 仍接受 CreateCustomerRequest 并可从路由查询参数取 ifMatch，
/// 这里显式声明方便代码搜索与类型安全。
/// </summary>
public class UpdateCustomerRequest : CreateCustomerRequest
{
    [JsonPropertyName("ifMatch")]
    public string? IfMatch { get; set; }
}
