using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// Telegram 入站接入服务 - 处理 Webhook 推送的 Update 与轮询拉取的 Update。
/// 去重(chat.id + message_id) → 客户匹配(Channel = telegram, ChannelId = chat.id) → 会话匹配 → 事务持久化 → getFile 立即下载附件 → 触发工作流。
/// </summary>
public interface ITelegramIngestionService
{
    /// <summary>
    /// 处理单个 Webhook Update（payload = 已通过 secret_token 校验的原始 JSON 字符串）。
    /// Telegram 无签名，只有 X-Telegram-Bot-Api-Secret-Token 头校验，校验在控制器完成。
    /// </summary>
    Task<TelegramIngestionResult> ProcessWebhookPayloadAsync(string? payload);

    /// <summary>轮询模式：执行一次 getUpdates 并接入结果（由 TelegramPollingHostedService 调用）。</summary>
    Task<TelegramPollResult> PollOnceAsync(CancellationToken cancellationToken = default);

    /// <summary>持久化的轮询游标（update_id + 1），用于状态页展示与重启续跑。</summary>
    Task<long> GetPollingOffsetAsync();
}
