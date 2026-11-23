using System.ComponentModel.DataAnnotations;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 更新用户权限请求
/// </summary>
public class UserPermissionsRequest : IValidatableObject
{
    [Required]
    [MaxLength(10, ErrorMessage = "角色数量不能超过 10 个")]
    public List<string> Roles { get; set; } = new();

    public bool CanViewAllConversations { get; set; }
    public bool CanViewAssignedConversationsOnly { get; set; }
    public bool CanCreateConversations { get; set; } = true;
    public bool CanEditConversations { get; set; } = true;
    public bool CanDeleteConversations { get; set; }
    public bool CanManageMailboxes { get; set; }
    public bool CanManageUsers { get; set; }
    public bool CanManageSettings { get; set; }
    public bool CanViewReports { get; set; } = true;
    public bool CanExportData { get; set; }

    [MaxLength(200, ErrorMessage = "可访问邮箱 ID 数量不能超过 200")]
    public List<int> AccessibleMailboxIds { get; set; } = new();

    /// <summary>
    /// 验证 Roles 列表中的每个值是否为有效的 UserRoleType 枚举值
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Roles.Count == 0)
        {
            yield return new ValidationResult("角色列表不能为空", new[] { nameof(Roles) });
            yield break;
        }

        foreach (var role in Roles)
        {
            if (!Enum.TryParse<UserRoleType>(role, ignoreCase: true, out _))
            {
                yield return new ValidationResult(
                    $"角色值 '{role}' 无效，有效值为：{string.Join(", ", Enum.GetNames<UserRoleType>())}",
                    new[] { nameof(Roles) });
            }
        }
    }
}
