using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 忘记密码请求
/// </summary>
public class ForgotPasswordRequest
{
    /// <summary>
    /// 名
    /// </summary>
    [Required(ErrorMessage = "名不能为空")]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// 姓
    /// </summary>
    [Required(ErrorMessage = "姓不能为空")]
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// 邮箱地址
    /// </summary>
    [Required(ErrorMessage = "邮箱地址不能为空")]
    [EmailAddress(ErrorMessage = "无效的邮箱格式")]
    public string Email { get; set; } = string.Empty;
}
