using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class CreateAdminRequest
{
    [Required(ErrorMessage = "邮箱不能为空")]
    [EmailAddress(ErrorMessage = "邮箱格式不正确")]
    [MaxLength(255, ErrorMessage = "邮箱长度不能超过 255 个字符")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "密码不能为空")]
    [MaxLength(128, ErrorMessage = "密码长度不能超过 128 个字符")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "名不能为空")]
    [MaxLength(50, ErrorMessage = "名长度不能超过 50 个字符")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "姓不能为空")]
    [MaxLength(50, ErrorMessage = "姓长度不能超过 50 个字符")]
    public string LastName { get; set; } = string.Empty;
}

public class SetupMailboxRequest
{
    [Required(ErrorMessage = "名称不能为空")]
    [MaxLength(100, ErrorMessage = "名称长度不能超过 100 个字符")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "邮箱不能为空")]
    [EmailAddress(ErrorMessage = "邮箱格式不正确")]
    [MaxLength(255, ErrorMessage = "邮箱长度不能超过 255 个字符")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "收件服务器不能为空")]
    [MaxLength(255, ErrorMessage = "主机名长度不能超过 255 个字符")]
    public string InHost { get; set; } = string.Empty;

    [Range(1, 65535, ErrorMessage = "端口必须在 1-65535 之间")]
    public int InPort { get; set; } = 993;

    [Range(0, 2, ErrorMessage = "加密类型必须为 0(None)、1(SSL) 或 2(TLS)")]
    public int InEncryption { get; set; } = 2;

    [Required(ErrorMessage = "收件用户名不能为空")]
    [MaxLength(255, ErrorMessage = "用户名长度不能超过 255 个字符")]
    public string InUsername { get; set; } = string.Empty;

    [Required(ErrorMessage = "收件密码不能为空")]
    [MaxLength(255, ErrorMessage = "密码长度不能超过 255 个字符")]
    public string InPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "发件服务器不能为空")]
    [MaxLength(255, ErrorMessage = "主机名长度不能超过 255 个字符")]
    public string OutHost { get; set; } = string.Empty;

    [Range(1, 65535, ErrorMessage = "端口必须在 1-65535 之间")]
    public int OutPort { get; set; } = 465;

    [Range(0, 2, ErrorMessage = "加密类型必须为 0(None)、1(SSL) 或 2(TLS)")]
    public int OutEncryption { get; set; } = 2;

    [Required(ErrorMessage = "发件用户名不能为空")]
    [MaxLength(255, ErrorMessage = "用户名长度不能超过 255 个字符")]
    public string OutUsername { get; set; } = string.Empty;

    [Required(ErrorMessage = "发件密码不能为空")]
    [MaxLength(255, ErrorMessage = "密码长度不能超过 255 个字符")]
    public string OutPassword { get; set; } = string.Empty;
}
