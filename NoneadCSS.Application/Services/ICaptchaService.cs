namespace NoneadCSS.Application.Services;

/// <summary>
/// 图形验证码服务接口
/// </summary>
public interface ICaptchaService
{
    /// <summary>
    /// 生成验证码
    /// </summary>
    /// <param name="width">验证码图片宽度</param>
    /// <param name="height">验证码图片高度</param>
    /// <param name="codeLength">验证码字符长度</param>
    /// <returns>验证码ID和SVG图片内容</returns>
    Task<(string captchaId, string svgContent)> GenerateAsync(int width = 120, int height = 40, int codeLength = 4);

    /// <summary>
    /// 验证验证码
    /// </summary>
    /// <param name="captchaId">验证码ID</param>
    /// <param name="code">用户输入的验证码</param>
    /// <returns>验证是否成功</returns>
    Task<bool> ValidateAsync(string captchaId, string code);
}
