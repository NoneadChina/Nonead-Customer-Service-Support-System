using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IWebhookService
{
    Task<WebhookDto?> GetByIdAsync(int id);
    Task<(List<WebhookDto> Items, int Total)> GetPagedAsync(WebhookQuery query);
    Task<WebhookOverviewResponse> GetOverviewAsync();
    Task<WebhookDto> CreateAsync(CreateWebhookRequest request);
    Task<WebhookDto?> UpdateAsync(UpdateWebhookRequest request, string? ifMatch = null);
    Task<bool> DeleteAsync(int id);
    Task<bool> ToggleActiveAsync(int id, string? ifMatch = null);
    Task<TestWebhookResponse> TestAsync(int id, TestWebhookRequest request);

    /// <summary>
    /// 触发指定事件的所有活跃 Webhook。
    /// 用于业务事件（如工作流动作）派发 webhook 通知。
    /// </summary>
    /// <param name="eventType">事件类型（与 Webhook.Events 匹配）</param>
    /// <param name="mailboxId">邮箱 ID（用于过滤，null 表示仅匹配全局 webhook）</param>
    /// <param name="payload">自定义负载对象，将序列化为 JSON 发送</param>
    /// <param name="entityType">关联实体类型（用于日志记录，可选）</param>
    /// <param name="entityId">关联实体 ID（用于日志记录，可选）</param>
    /// <returns>触发的 webhook 数量</returns>
    Task<int> TriggerAsync(string eventType, int? mailboxId = null, object? payload = null, string? entityType = null, int? entityId = null);

    Task<List<WebhookLogDto>> GetLogsAsync(int webhookId, int limit = 50);
    Task<(List<WebhookLogDto> Items, int Total)> GetLogsPagedAsync(WebhookLogQuery query);
    Task<(List<WebhookDto> Items, int Total)> GetTrashPagedAsync(int page, int pageSize, string? keyword);
    Task<WebhookDto?> RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> ClearTrashAsync();
}
