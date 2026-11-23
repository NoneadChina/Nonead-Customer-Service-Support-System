using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IWhatsAppIngestionService
{
    /// <summary>
    /// 处理 WhatsApp Webhook 推送的消息，创建/更新会话与线程。
    /// </summary>
    /// <param name="payload">已验证签名的 Webhook JSON payload（原始字符串）</param>
    /// <returns>处理结果</returns>
    Task<WhatsAppIngestionResult> ProcessWebhookPayloadAsync(string? payload);
}
