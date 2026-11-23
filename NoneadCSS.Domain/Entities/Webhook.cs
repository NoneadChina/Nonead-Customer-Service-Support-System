namespace NoneadCSS.Domain.Entities;

/// <summary>
/// Webhook 配置 - 事件触发时回调外部接口
/// </summary>
public class Webhook : ISoftDelete
{
    public int Id { get; set; }

    /// <summary>Webhook 名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>回调 URL</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>请求方法：POST / PUT</summary>
    public string Method { get; set; } = "POST";

    /// <summary>
    /// 监听的事件类型，逗号分隔。如：
    /// conversation_created, conversation_updated, conversation_deleted,
    /// conversation_status_changed, customer_created, customer_updated,
    /// thread_created, user_created, user_updated
    /// </summary>
    public string Events { get; set; } = string.Empty;

    /// <summary>请求头 JSON，自定义 Headers</summary>
    public string? HeadersJson { get; set; }

    /// <summary>密钥，用于签名验证</summary>
    public string? Secret { get; set; }

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>所属邮箱 ID。null 表示全局。</summary>
    public int? MailboxId { get; set; }

    /// <summary>触发次数</summary>
    public int TriggerCount { get; set; }

    /// <summary>成功次数</summary>
    public int SuccessCount { get; set; }

    /// <summary>失败次数</summary>
    public int FailCount { get; set; }

    /// <summary>最后触发时间</summary>
    public DateTime? LastTriggeredAt { get; set; }

    /// <summary>最后错误信息</summary>
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 乐观锁 RowVersion；TriggerCount/SuccessCount/FailCount/LastTriggeredAt 在高并发回调时存在 lost update 风险。
    /// </summary>
    public byte[]? RowVersion { get; set; }

    public Mailbox? Mailbox { get; set; }
    public ICollection<WebhookLog> Logs { get; set; } = [];

    /// <summary>
    /// 检查是否监听指定事件
    /// </summary>
    /// <param name="eventType">事件类型</param>
    /// <returns>是否监听该事件</returns>
    public bool ShouldFireForEvent(string eventType)
    {
        if (!IsActive || DeletedAt.HasValue)
        {
            return false;
        }
        var events = Events.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return events.Contains(eventType);
    }

    /// <summary>
    /// 记录触发
    /// </summary>
    public void RecordTrigger()
    {
        TriggerCount++;
        LastTriggeredAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 记录成功
    /// </summary>
    public void RecordSuccess()
    {
        SuccessCount++;
        LastError = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 记录失败
    /// </summary>
    /// <param name="errorMessage">错误信息</param>
    public void RecordFailure(string errorMessage)
    {
        FailCount++;
        LastError = errorMessage;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 激活 Webhook
    /// </summary>
    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 停用 Webhook
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 软删除
    /// </summary>
    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 恢复软删除
    /// </summary>
    public void Restore()
    {
        DeletedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 检查是否已删除
    /// </summary>
    public bool IsDeleted() => DeletedAt.HasValue;

    /// <summary>
    /// 获取监听的事件列表
    /// </summary>
    public List<string> GetEventList()
    {
        return Events.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}

/// <summary>
/// Webhook 触发日志
/// </summary>
public class WebhookLog
{
    public int Id { get; set; }

    public int WebhookId { get; set; }

    /// <summary>事件类型</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>实体类型</summary>
    public string? EntityType { get; set; }

    /// <summary>实体 ID</summary>
    public int? EntityId { get; set; }

    /// <summary>请求 URL</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>请求方法</summary>
    public string Method { get; set; } = "POST";

    /// <summary>请求体（截断保存）</summary>
    public string? RequestBody { get; set; }

    /// <summary>响应状态码</summary>
    public int? ResponseStatusCode { get; set; }

    /// <summary>响应体（截断保存）</summary>
    public string? ResponseBody { get; set; }

    /// <summary>是否成功</summary>
    public bool IsSuccess { get; set; }

    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>请求耗时（毫秒）</summary>
    public int DurationMs { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Webhook? Webhook { get; set; }
}
