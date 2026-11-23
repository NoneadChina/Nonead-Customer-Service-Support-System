using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IMessengerIngestionService
{
    /// <summary>
    /// 处理 Messenger Page Webhook 推送的消息，创建/更新会话与线程。
    /// 必须跳过 is_echo 与 sender.id==PageId 的事件；按 message.mid 幂等去重。
    /// </summary>
    /// <param name="payload">已验证签名的 Webhook JSON payload（原始字符串）</param>
    /// <returns>处理结果</returns>
    Task<MessengerIngestionResult> ProcessWebhookPayloadAsync(string? payload);
}
