using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class UpdateProfileRequest
{
    [Required(ErrorMessage = "名字不能为空")]
    [MaxLength(100, ErrorMessage = "名字最多 100 个字符")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "姓氏不能为空")]
    [MaxLength(100, ErrorMessage = "姓氏最多 100 个字符")]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(50, ErrorMessage = "时区最多 50 个字符")]
    public string Timezone { get; set; } = string.Empty;

    [MaxLength(10, ErrorMessage = "语言代码最多 10 个字符")]
    public string Locale { get; set; } = string.Empty;

    /// <summary>
    /// 用户头像 URL（指向 /api/attachments/{id}/public-preview）。
    /// 传 null 或空字符串清除头像。
    /// </summary>
    [MaxLength(255, ErrorMessage = "头像 URL 最多 255 个字符")]
    public string? Avatar { get; set; }
}

public class ChangePasswordRequest : IValidatableObject
{
    [Required(ErrorMessage = "当前密码不能为空")]
    [MaxLength(1024, ErrorMessage = "密码长度不能超过 1024 个字符")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "新密码不能为空")]
    [MaxLength(1024, ErrorMessage = "密码长度不能超过 1024 个字符")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "确认密码不能为空")]
    [MaxLength(1024, ErrorMessage = "密码长度不能超过 1024 个字符")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (NewPassword != ConfirmPassword)
        {
            yield return new ValidationResult(
                "两次输入的密码不一致",
                new[] { nameof(ConfirmPassword) });
        }
    }
}
