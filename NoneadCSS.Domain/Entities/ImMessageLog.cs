using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;

namespace NoneadCSS.Domain.Entities;

/// <summary>
/// IM 消息发送日志实体：跟踪通过聊天渠道（微信/飞书/WhatsApp/钉钉）发送的消息状态，
/// 支持 IM 消息发送队列的持久化与重试调度。
/// 参考 <see cref="SendLog"/> 的设计模式。
/// </summary>
public class ImMessageLog
{
    public int Id { get; set; }

    /// <summary>
    /// 关联会话 Id
    /// </summary>
    public int ConversationId { get; set; }

    /// <summary>
    /// 关联客户 Id（冗余存储，避免重试时再查 Conversation）
    /// </summary>
    public int? CustomerId { get; set; }

    /// <summary>
    /// 渠道类型：WeChat / Feishu / WhatsApp / DingTalk 之一
    /// </summary>
    public SourceType SourceType { get; set; }

    /// <summary>
    /// 客户在该渠道中的标识（如微信 openId、飞书 openId、WhatsApp waId、钉钉 userId）
    /// </summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// 待发送的消息文本
    /// </summary>
    public string MessageText { get; set; } = string.Empty;

    /// <summary>
    /// 发送状态，参见 <see cref="Repositories.ImMessageLogStatus"/>
    /// </summary>
    public int Status { get; set; }

    /// <summary>
    /// 已尝试发送次数（含首次发送）
    /// </summary>
    public int AttemptCount { get; set; } = 0;

    /// <summary>
    /// 最大重试次数（含首次发送），默认 4 次
    /// </summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>
    /// 下次重试时间（UTC），null 表示无需重试或已成功
    /// </summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>
    /// 错误信息（最近一次失败原因）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 渠道返回的外部消息 Id（如飞书 message_id、WhatsApp wamid、钉钉 processQueryKey）
    /// </summary>
    public string? ExternalMessageId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 实际发送成功时间（UTC）
    /// </summary>
    public DateTime? SentAt { get; set; }

    /// <summary>
    /// 最近一次状态更新时间（UTC）
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    public Conversation? Conversation { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>
    /// 标记为待发送（首次入队）
    /// </summary>
    public void MarkAsPending()
    {
        Status = ImMessageLogStatus.Pending;
        NextRetryAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 标记为发送中（处理任务取出后临时状态）
    /// </summary>
    public void MarkAsSending()
    {
        Status = ImMessageLogStatus.Sending;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 标记为发送成功
    /// </summary>
    /// <param name="externalMessageId">渠道返回的外部消息 Id（可选）</param>
    public void MarkAsSent(string? externalMessageId = null)
    {
        Status = ImMessageLogStatus.Sent;
        SentAt = DateTime.UtcNow;
        NextRetryAt = null;
        ErrorMessage = null;
        ExternalMessageId = externalMessageId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 标记为发送失败，并按指数退避策略计算下次重试时间。
    /// 超过最大重试次数则标记为最终失败。
    /// </summary>
    /// <param name="errorMessage">错误信息</param>
    public void MarkAsFailed(string errorMessage)
    {
        AttemptCount++;
        ErrorMessage = errorMessage;

        if (AttemptCount >= MaxAttempts)
        {
            Status = ImMessageLogStatus.Failed;
            NextRetryAt = null;
        }
        else
        {
            // 状态回退为 Pending，等待下次重试
            Status = ImMessageLogStatus.Pending;
            NextRetryAt = DateTime.UtcNow.AddMinutes(GetBackoffMinutes(AttemptCount));
        }
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 指数退避重试间隔（分钟）：1 → 5 → 15 → 60
    /// </summary>
    private static int GetBackoffMinutes(int attemptCount)
    {
        return attemptCount switch
        {
            1 => 1,
            2 => 5,
            3 => 15,
            _ => 60
        };
    }

    public bool IsSent() => Status == ImMessageLogStatus.Sent;

    public bool IsFailed() => Status == ImMessageLogStatus.Failed;

    public bool IsPending() => Status == ImMessageLogStatus.Pending;
}
