using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IFeishuIngestionService
{
    /// <summary>
    /// 处理飞书推送的事件消息，创建/更新会话与线程。
    /// </summary>
    /// <param name="rawBody">飞书事件回调的原始 JSON body</param>
    /// <returns>处理结果</returns>
    Task<FeishuIngestionResult> ProcessEventAsync(string rawBody);

    /// <summary>
    /// T9: 清空变更跟踪器。Stream 长连接复用单个 scoped DbContext，逐事件处理后调用
    /// 可防止 ChangeTracker 无限累积（内存增长 + 后续 SaveChanges 性能退化）。
    /// </summary>
    void ResetTracking();
}
