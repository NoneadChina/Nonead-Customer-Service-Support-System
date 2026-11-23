using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 满意度评价令牌生成器
/// 独立于 SatisfactionRatingService，避免与 IWorkflowService 形成循环依赖
/// 密钥读取逻辑与 SatisfactionRatingService 保持一致
/// </summary>
public class SatisfactionTokenGenerator : ISatisfactionTokenGenerator
{
    private readonly string _secretKey;
    private readonly ILogger<SatisfactionTokenGenerator> _logger;

    public SatisfactionTokenGenerator(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<SatisfactionTokenGenerator> logger)
    {
        _logger = logger;

        // 优先从环境变量读取（与 JWT/Encryption 一致的策略，生产环境强制使用环境变量）
        var keyString = Environment.GetEnvironmentVariable("APP__SatisfactionTokenKey")
            ?? Environment.GetEnvironmentVariable("APP_SATISFACTIONTOKENKEY")
            ?? configuration["App:SatisfactionTokenKey"];

        // 生产环境：必须从环境变量获取
        if (environment.IsProduction())
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APP__SatisfactionTokenKey"))
                && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APP_SATISFACTIONTOKENKEY")))
            {
                throw new InvalidOperationException(
                    "生产环境必须设置 APP__SatisfactionTokenKey 或 APP_SATISFACTIONTOKENKEY 环境变量。");
            }
        }

        // 开发环境：未配置时自动生成随机密钥（每次启动随机，不用于生产）
        if (string.IsNullOrEmpty(keyString))
        {
            if (environment.IsDevelopment())
            {
                var devKeyBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(48);
                keyString = Convert.ToBase64String(devKeyBytes);
                _logger.LogWarning("[DEV] 已自动生成开发环境 SatisfactionTokenKey（每次启动随机，请勿用于生产环境）");
                _logger.LogWarning("[DEV] 如需固定密钥，请设置 APP__SatisfactionTokenKey 环境变量");
            }
            else
            {
                throw new InvalidOperationException(
                    "SatisfactionTokenKey 未配置。请设置 APP__SatisfactionTokenKey 环境变量。");
            }
        }

        if (System.Text.Encoding.UTF8.GetByteCount(keyString) < 32)
        {
            throw new InvalidOperationException("App:SatisfactionTokenKey 长度必须至少为 32 字节");
        }
        _secretKey = keyString;
    }

    /// <inheritdoc/>
    public string GenerateToken(int conversationId, string customerEmail)
    {
        return SatisfactionTokenHelper.GenerateToken(conversationId, customerEmail, _secretKey);
    }

    /// <inheritdoc/>
    public string GenerateTokenByChannelId(int conversationId, string channelId)
    {
        return SatisfactionTokenHelper.GenerateTokenByChannelId(conversationId, channelId, _secretKey);
    }

    /// <inheritdoc/>
    public bool ValidateToken(string token, int conversationId, string customerEmail)
    {
        return SatisfactionTokenHelper.ValidateToken(token, conversationId, customerEmail, _secretKey);
    }

    /// <inheritdoc/>
    public bool ValidateTokenByChannelId(string token, int conversationId, string channelId)
    {
        return SatisfactionTokenHelper.ValidateTokenByChannelId(token, conversationId, channelId, _secretKey);
    }
}
