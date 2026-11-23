using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Application.Common;

/// <summary>
/// Telegram 会话“不可达”标记（用户拉黑 bot / bot 被移出群 / chat not found）。
/// Telegram 无客服窗口，但 403 属终态错误：一旦判定不可达就应停止重试，
/// 直到该 chat 再次发来消息（说明已解除拉黑 / 重新加群）时自动清除。
/// 实现方式：不新增字段、不迁移表结构 —— 在会话下写一条内部备注线程（MessageId 固定前缀），
/// 存在即代表不可达；清除时把该线程置为 Deleted。
/// </summary>
public static class TelegramUnreachableState
{
    /// <summary>标记线程的去重键前缀</summary>
    public const string MarkPrefix = "telegram_unreachable_";

    public static string MarkMessageId(int conversationId) => $"{MarkPrefix}{conversationId}";

    /// <summary>会话是否已被标记不可达</summary>
    public static async Task<bool> ExistsAsync(IUnitOfWork unitOfWork, int conversationId)
    {
        var messageId = MarkMessageId(conversationId);
        var existing = await unitOfWork.Threads.FindAsync(t =>
            t.ConversationId == conversationId
            && t.MessageId == messageId
            && t.State != ThreadState.Deleted);

        return existing.Any(t => t.MessageId == messageId);
    }

    /// <summary>读取不可达原因（最近一条标记线程正文），未标记返回 null</summary>
    public static async Task<string?> GetReasonAsync(IUnitOfWork unitOfWork, int conversationId)
    {
        var messageId = MarkMessageId(conversationId);
        var existing = await unitOfWork.Threads.FindAsync(t =>
            t.ConversationId == conversationId
            && t.MessageId == messageId
            && t.State != ThreadState.Deleted);

        return existing.Where(t => t.MessageId == messageId).OrderByDescending(t => t.CreatedAt).FirstOrDefault()?.Body;
    }

    /// <summary>标记会话不可达（幂等：已存在则只刷新原因）</summary>
    public static async Task MarkAsync(IUnitOfWork unitOfWork, int conversationId, string reason)
    {
        var messageId = MarkMessageId(conversationId);
        var existing = await unitOfWork.Threads.FindAsync(t =>
            t.ConversationId == conversationId
            && t.MessageId == messageId
            && t.State != ThreadState.Deleted);

        // 只复用确实是标记线程的那条（MessageId 命中前缀键）；否则一律新建，
        // 避免仓储谓词翻译差异把客户/坐席线程正文改写成系统备注
        var mark = existing.FirstOrDefault(t => t.MessageId == messageId);
        if (mark == null)
        {
            await unitOfWork.Threads.AddAsync(new ConversationThread
            {
                ConversationId = conversationId,
                Type = ThreadType.Note,
                State = ThreadState.Active,
                IsInternal = true,
                From = "telegram@system",
                To = string.Empty,
                Subject = "Telegram 不可达",
                Body = string.IsNullOrWhiteSpace(reason) ? "客户已拉黑机器人或 bot 已被移出群，停止自动重试。" : reason,
                MessageId = messageId,
                SourceType = SourceType.Telegram,
                SourceVia = SourceVia.Customer,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            mark.Body = string.IsNullOrWhiteSpace(reason) ? mark.Body : reason;
            mark.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>清除不可达标记（客户重新来信 / 坐席手动确认）</summary>
    public static async Task<bool> ClearAsync(IUnitOfWork unitOfWork, int conversationId)
    {
        var messageId = MarkMessageId(conversationId);
        var existing = await unitOfWork.Threads.FindAsync(t =>
            t.ConversationId == conversationId
            && t.MessageId == messageId
            && t.State != ThreadState.Deleted);

        var cleared = false;
        foreach (var mark in existing.Where(t => t.MessageId == messageId))
        {
            mark.State = ThreadState.Deleted;
            mark.UpdatedAt = DateTime.UtcNow;
            cleared = true;
        }

        return cleared;
    }

    /// <summary>统计所有被标记不可达的会话数（状态页展示）</summary>
    public static async Task<int> CountMarkedAsync(IUnitOfWork unitOfWork)
    {
        var marks = await unitOfWork.Threads.FindAsync(t =>
            t.MessageId.StartsWith(MarkPrefix)
            && t.State != ThreadState.Deleted);

        return marks.Select(t => t.ConversationId).Distinct().Count();
    }
}
