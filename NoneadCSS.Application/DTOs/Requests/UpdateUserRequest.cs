using System.ComponentModel.DataAnnotations;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.DTOs.Requests;

public class UpdateUserRequest : IValidatableObject
{
    [Required(ErrorMessage = "名字不能为空")]
    [MaxLength(100, ErrorMessage = "名字最多 100 个字符")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "姓氏不能为空")]
    [MaxLength(100, ErrorMessage = "姓氏最多 100 个字符")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "邮箱不能为空")]
    [EmailAddress(ErrorMessage = "无效的邮箱格式")]
    [MaxLength(255, ErrorMessage = "邮箱最多 255 个字符")]
    public string Email { get; set; } = string.Empty;

    public string? Password { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "角色值无效")]
    public int Role { get; set; } = 1;

    public List<int> MailboxIds { get; set; } = [];

    /// <summary>乐观锁版本标记（RowVersion base64）；不匹配时返回 409。</summary>
    public string? IfMatch { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(typeof(UserRoleType), Role))
        {
            yield return new ValidationResult(
                $"角色值 '{Role}' 无效，有效值为：{string.Join(", ", Enum.GetNames<UserRoleType>())}",
                new[] { nameof(Role) });
        }
    }
}
