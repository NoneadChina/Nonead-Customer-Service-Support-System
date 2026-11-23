using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 满意度评价令牌生成器接口
/// 独立于 ISatisfactionRatingService，避免与 IWorkflowService 形成循环依赖
/// 统一管理令牌密钥，确保生成与验证使用相同密钥
/// </summary>
public interface ISatisfactionTokenGenerator
{
    /// <summary>
    /// 生成满意度评价令牌（供邮件发送端调用，确保生成与验证使用相同密钥）
    /// </summary>
    /// <param name="conversationId">会话 ID</param>
    /// <param name="customerEmail">客户邮箱</param>
    /// <returns>评价令牌字符串</returns>
    string GenerateToken(int conversationId, string customerEmail);

    /// <summary>
    /// 生成满意度评价令牌（以客户渠道 ID 作为身份标识）。
    /// 用于微信/飞书/WhatsApp/钉钉等聊天渠道客户无邮箱时的满意度调查。
    /// </summary>
    /// <param name="conversationId">会话 ID</param>
    /// <param name="channelId">客户在渠道中的唯一标识（openId / waId / userId 等）</param>
    /// <returns>评价令牌字符串</returns>
    string GenerateTokenByChannelId(int conversationId, string channelId);

    /// <summary>
    /// 验证满意度评价令牌（供 SatisfactionRatingService 调用，确保与生成端使用相同密钥）
    /// </summary>
    /// <param name="token">令牌字符串</param>
    /// <param name="conversationId">会话 ID</param>
    /// <param name="customerEmail">客户邮箱</param>
    /// <returns>有效返回 true，无效或过期返回 false</returns>
    bool ValidateToken(string token, int conversationId, string customerEmail);

    /// <summary>
    /// 验证满意度评价令牌（以客户渠道 ID 作为身份标识）。
    /// </summary>
    /// <param name="token">令牌字符串</param>
    /// <param name="conversationId">会话 ID</param>
    /// <param name="channelId">客户在渠道中的唯一标识</param>
    /// <returns>有效返回 true，无效或过期返回 false</returns>
    bool ValidateTokenByChannelId(string token, int conversationId, string channelId);
}
