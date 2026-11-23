using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 重置密码请求
/// </summary>
public class ResetPasswordRequest : IValidatableObject
{
    /// <summary>
    /// 重置令牌
    /// </summary>
    [Required(ErrorMessage = "令牌不能为空")]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// 新密码
    /// </summary>
    [Required(ErrorMessage = "新密码不能为空")]
    public string NewPassword { get; set; } = string.Empty;

    /// <summary>
    /// 确认密码
    /// </summary>
    [Required(ErrorMessage = "确认密码不能为空")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>
    /// 自定义模型验证（两次密码一致性检查）
    /// </summary>
    /// <param name="validationContext">验证上下文</param>
    /// <returns>验证结果集合</returns>
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
