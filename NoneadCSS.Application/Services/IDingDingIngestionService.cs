using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IDingDingIngestionService
{
    /// <summary>
    /// 处理钉钉 Webhook 推送的消息，创建/更新会话与线程。
    /// </summary>
    /// <param name="payload">已验证签名的 Webhook JSON payload（原始字符串）</param>
    /// <returns>处理结果</returns>
    Task<DingDingIngestionResult> ProcessWebhookPayloadAsync(string payload);

    /// <summary>
    /// T9: 清空变更跟踪器。Stream 长连接复用单个 scoped DbContext，逐事件处理后调用
    /// 可防止 ChangeTracker 无限累积（内存增长 + 后续 SaveChanges 性能退化）。
    /// </summary>
    void ResetTracking();
}
