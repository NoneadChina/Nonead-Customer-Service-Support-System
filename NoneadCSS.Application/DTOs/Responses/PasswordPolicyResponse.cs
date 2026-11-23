namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 密码策略响应 DTO
/// </summary>
public class PasswordPolicyResponse
{
    /// <summary>
    /// 最小密码长度
    /// </summary>
    public int MinLength { get; set; } = 10;

    /// <summary>
    /// 是否必须包含大写字母
    /// </summary>
    public bool RequireUppercase { get; set; } = false;

    /// <summary>
    /// 是否必须包含小写字母
    /// </summary>
    public bool RequireLowercase { get; set; } = false;

    /// <summary>
    /// 是否必须包含数字
    /// </summary>
    public bool RequireDigit { get; set; } = false;

    /// <summary>
    /// 是否必须包含特殊字符（非字母数字）
    /// </summary>
    public bool RequireNonAlphanumeric { get; set; } = false;

    /// <summary>
    /// 是否启用弱密码字典检查
    /// </summary>
    public bool CheckWeakPassword { get; set; } = true;

    /// <summary>
    /// 是否启用常见模式检查
    /// </summary>
    public bool CheckCommonPatterns { get; set; } = true;

    /// <summary>
    /// 密码策略可读描述（用于前端展示）
    /// </summary>
    public List<string> Requirements { get; set; } = [];
}
