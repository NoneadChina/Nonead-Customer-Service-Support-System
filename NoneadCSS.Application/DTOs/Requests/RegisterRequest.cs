using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 用户注册请求
/// </summary>
public class RegisterRequest
{
    /// <summary>
    /// 邮箱地址
    /// </summary>
    [Required(ErrorMessage = "邮箱地址不能为空")]
    [EmailAddress(ErrorMessage = "无效的邮箱格式")]
    [MaxLength(256, ErrorMessage = "邮箱地址长度不能超过 256 个字符")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 密码
    /// </summary>
    [Required(ErrorMessage = "密码不能为空")]
    [StringLength(128, MinimumLength = 10, ErrorMessage = "密码长度必须在 10 到 128 个字符之间")]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 名
    /// </summary>
    [Required(ErrorMessage = "名不能为空")]
    [MaxLength(100, ErrorMessage = "名长度不能超过 100 个字符")]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// 姓
    /// </summary>
    [Required(ErrorMessage = "姓不能为空")]
    [MaxLength(100, ErrorMessage = "姓长度不能超过 100 个字符")]
    public string LastName { get; set; } = string.Empty;
}