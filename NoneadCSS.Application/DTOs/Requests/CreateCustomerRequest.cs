using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class CreateCustomerRequest
{
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// 邮箱（可选）。
    /// 微信/钉钉/WhatsApp 新建的客户可能没有邮箱，允许为空，后续通过其他渠道补全。
    /// 不为空时会进行邮箱格式校验和唯一性检查。
    /// </summary>
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Address { get; set; }
    [MaxLength(100)]
    public string? City { get; set; }
    [MaxLength(100)]
    public string? State { get; set; }
    [MaxLength(20)]
    public string? Zip { get; set; }
    [MaxLength(100)]
    public string? Country { get; set; }
    [MaxLength(200)]
    public string? Company { get; set; }
    [MaxLength(100)]
    public string? JobTitle { get; set; }
    [MaxLength(2000)]
    public string? Notes { get; set; }
    public List<CustomerPhoneDto>? Phones { get; set; }
    public List<CustomerSocialProfileDto>? SocialProfiles { get; set; }
    public List<string>? Websites { get; set; }
    public string? Avatar { get; set; }
}

public class CustomerPhoneDto
{
    [Required]
    [MaxLength(50)]
    public string Value { get; set; } = string.Empty;
    public int Type { get; set; } = 1;
}

public class CustomerSocialProfileDto
{
    [Required]
    [MaxLength(255)]
    public string Value { get; set; } = string.Empty;
    public int Type { get; set; }
}
