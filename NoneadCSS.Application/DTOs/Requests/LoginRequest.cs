using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 用户登录请求
/// </summary>
public class LoginRequest
{
    /// <summary>
    /// 邮箱地址
    /// </summary>
    [Required(ErrorMessage = "邮箱地址不能为空")]
    [EmailAddress(ErrorMessage = "无效的邮箱格式")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 密码
    /// </summary>
    [Required(ErrorMessage = "密码不能为空")]
    [MaxLength(1024, ErrorMessage = "密码长度不能超过 1024 个字符")]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 验证码ID
    /// </summary>
    public string CaptchaId { get; set; } = string.Empty;

    /// <summary>
    /// 验证码
    /// </summary>
    public string CaptchaCode { get; set; } = string.Empty;

    /// <summary>
    /// 本次登录使用的界面语言（如 zh-Hans / en）。可选：为空时由控制器端从请求文化解析的语言兜底，
    /// 用于把语言回写到 Users.Locale。
    /// </summary>
    [MaxLength(20, ErrorMessage = "语言代码长度不能超过 20 个字符")]
    public string? Locale { get; set; }
}
