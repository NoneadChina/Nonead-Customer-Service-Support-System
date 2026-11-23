using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 密码策略服务 - 统一管理密码策略的读取与校验
/// 策略来源于系统配置（SystemOption 表 security 分类）
/// </summary>
public interface IPasswordPolicyService
{
    /// <summary>
    /// 获取当前密码策略（已缓存，5 分钟过期）
    /// </summary>
    /// <returns>密码策略响应 DTO</returns>
    Task<PasswordPolicyResponse> GetPolicyAsync();

    /// <summary>
    /// 校验密码是否符合当前密码策略
    /// </summary>
    /// <param name="password">待校验的明文密码</param>
    /// <returns>校验结果，包含是否通过和错误信息列表</returns>
    Task<PasswordValidationResult> ValidateAsync(string password);

    /// <summary>
    /// 清除密码策略缓存（在策略更新后调用）
    /// </summary>
    void InvalidateCache();
}

/// <summary>
/// 密码校验结果
/// </summary>
public class PasswordValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
}
