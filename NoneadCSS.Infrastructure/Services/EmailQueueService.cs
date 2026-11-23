using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Services;

public class EmailQueueService : IEmailQueueService
{
    private readonly ILogger<EmailQueueService> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _emailService;
    private readonly IAttachmentService _attachmentService;

    public EmailQueueService(ILogger<EmailQueueService> logger, IUnitOfWork unitOfWork,
        IEmailService emailService, IAttachmentService attachmentService)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
        _emailService = emailService;
        _attachmentService = attachmentService;
    }

    private static string SanitizeHeaderValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Replace("\0", string.Empty)
            .Replace("\t", string.Empty)   // 制表符可绕过部分头部解析器
            .Replace("\uFEFF", string.Empty); // BOM 字符防御
    }

    public async Task EnqueueEmailAsync(int mailboxId, string to, string subject, string body,
        List<int>? attachmentIds = null, string? cc = null, string? bcc = null, 
        int? threadId = null, string? inReplyTo = null, string? references = null,
        int mailType = 1, int? userId = null, int? customerId = null)
    {
        var queueItem = new SendLog
        {
            MailboxId = mailboxId,
            Email = SanitizeHeaderValue(to),
            Cc = SanitizeHeaderValue(cc ?? string.Empty),
            Bcc = SanitizeHeaderValue(bcc ?? string.Empty),
            Subject = SanitizeHeaderValue(subject),
            Body = body,
            Status = SendLogStatus.Accepted,
            MailType = mailType,
            CreatedAt = DateTime.UtcNow,
            AttemptCount = 0,
            ThreadId = threadId,
            InReplyTo = SanitizeHeaderValue(inReplyTo ?? string.Empty),
            References = SanitizeHeaderValue(references ?? string.Empty),
            UserId = userId,
            CustomerId = customerId,
        };

        if (attachmentIds != null && attachmentIds.Any())
        {
            // 批量验证附件存在性，避免 N+1 查询；使用 string.Join + Distinct 一次性拼接，避免循环内字符串拼接开销
            var attachments = await _unitOfWork.Attachments.GetByIdsAsync(attachmentIds);
            var validAttachmentIds = attachments.Select(a => a.Id).ToHashSet();

            var validIds = attachmentIds.Where(id => validAttachmentIds.Contains(id)).Distinct();
            queueItem.AttachmentIds = string.Join(",", validIds);
        }

        await _unitOfWork.SendLogs.AddAsync(queueItem);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("邮件已加入发送队列: To={To}, Subject={Subject}, QueueId={QueueId}", to, subject, queueItem.Id);
    }

    public async Task ProcessQueueAsync()
    {
        var pendingItems = await _unitOfWork.SendLogs.GetPendingAsync();
        if (!pendingItems.Any()) return;

        _logger.LogInformation("开始处理邮件队列，待处理数量: {Count}", pendingItems.Count());

        foreach (var item in pendingItems)
        {
            await ProcessQueueItemAsync(item);
        }
    }

    private async Task ProcessQueueItemAsync(SendLog item)
    {
        // 设计说明（M-Q2）：当前流程在事务内先标记 SendIntermediateError 并持久化，
        // 防止队列重入时重复发送；发送成功后更新为 DeliverySuccess 并提交事务，失败则回滚后独立记录 SendError。
        // 重构为"事务仅包裹发送+成功状态更新"的改动风险较大（涉及队列幂等、并发重入与状态机一致性），
        // 暂保持现状，后续可在具备完整测试覆盖时重构。
        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            item.AttemptCount++;
            item.Status = SendLogStatus.SendIntermediateError;
            // item 已通过 GetPendingAsync 查询（tracking），直接 SaveChangesAsync 即可持久化
            await _unitOfWork.SaveChangesAsync();

            if (!item.MailboxId.HasValue)
            {
                item.Status = SendLogStatus.SendError;
                item.ErrorMessage = "邮箱ID为空";
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
                return;
            }

            var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(item.MailboxId.Value);
            if (mailbox == null)
            {
                item.Status = SendLogStatus.SendError;
                item.ErrorMessage = "邮箱不存在";
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
                return;
            }

            List<Attachment>? attachments = null;
            if (!string.IsNullOrEmpty(item.AttachmentIds))
            {
                var attIds = (item.AttachmentIds ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var id) ? id : 0)
                    .Where(id => id > 0)
                    .ToList();
                // 批量查询：替代原来 N 次循环内的 GetByIdAsync，消除高频 N+1 查询
                var allAtts = await _unitOfWork.Attachments.GetByIdsAsync(attIds);
                var attMap = allAtts.ToDictionary(a => a.Id);
                attachments = new List<Attachment>(attIds.Count);
                foreach (var id in attIds)
                {
                    if (attMap.TryGetValue(id, out var att))
                        attachments.Add(att);
                }
            }

            var subject = item.Subject ?? string.Empty;
            var body = item.Body ?? string.Empty;
            
            var domain = mailbox.Email.Split('@').LastOrDefault() ?? "localhost";
            var threadPart = item.ThreadId?.ToString() ?? "system";
            var messageId = $"reply-{threadPart}-{Guid.NewGuid():N}@{domain}";
            
            await _emailService.SendEmailWithHeadersAsync(
                mailbox, item.Email, subject, body, attachments, 
                item.Cc, item.Bcc, 
                item.InReplyTo, item.References, messageId);

            await _emailService.SaveToSentFolderAsync(mailbox, item.Email, subject, body, attachments, item.Cc, item.Bcc);

            item.Status = SendLogStatus.DeliverySuccess;
            item.SentAt = DateTime.UtcNow;
            item.MessageId = messageId;

            // 将外发邮件的 MessageId 写回 ConversationThread，
            // 确保客户回复此邮件时能通过 In-Reply-To/References 匹配到现有线程（步骤 1-3）
            if (item.ThreadId is int threadId && threadId > 0)
            {
                var thread = await _unitOfWork.Threads.GetByIdTrackedAsync(threadId);
                if (thread != null)
                {
                    thread.MessageId = messageId;
                }
            }

            // item 已通过 GetPendingAsync 查询（tracking），直接 SaveChangesAsync 即可持久化
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("邮件发送成功并保存到已发送文件夹: QueueId={QueueId}, To={To}, MessageId={MessageId}", item.Id, item.Email, messageId);
        }
        catch (Exception ex)
        {
            try
            {
                await transaction.RollbackAsync();
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "回滚事务失败: QueueId={QueueId}", item.Id);
            }

            // 回滚后 ClearChanges 清除所有 tracked 实体的 Modified 状态（包括 thread.MessageId 等事务内修改），
            // 然后重新 Attach item 并仅修改失败状态字段，确保不会持久化已回滚的修改
            _unitOfWork.ClearChanges();

            // 重新获取 tracked item 实体（ClearChanges 后原引用已 detach）
            var itemTracked = await _unitOfWork.SendLogs.GetByIdTrackedAsync(item.Id);
            if (itemTracked != null)
            {
                // BUG-07 修复：引入重试机制，避免一次失败立即固化为终态 SendError。
                // - AttemptCount 已在事务内递增（item.AttemptCount），回滚后需手动写回 itemTracked
                // - 当 AttemptCount >= MaxRetryCount：状态固化为 SendError（终态，不再被队列拾起）
                // - 当 AttemptCount <  MaxRetryCount：状态重置为 Accepted，等待下一轮队列处理重试
                itemTracked.AttemptCount = item.AttemptCount; // 保留 AttemptCount 递增值
                var attempts = itemTracked.AttemptCount;
                var isTerminal = attempts >= SendLogStatus.MaxRetryCount;
                itemTracked.Status = isTerminal ? SendLogStatus.SendError : SendLogStatus.Accepted;
                itemTracked.ErrorMessage = ex.Message;

                try
                {
                    await _unitOfWork.SaveChangesAsync();
                    if (isTerminal)
                    {
                        _logger.LogWarning("邮件发送失败并达到最大重试次数 {MaxRetry}，状态固化为 SendError: QueueId={QueueId}, Error={Error}",
                            SendLogStatus.MaxRetryCount, item.Id, ex.Message);
                    }
                    else
                    {
                        _logger.LogInformation("邮件发送失败，状态重置为 Accepted 等待重试: QueueId={QueueId}, Attempt={Attempt}/{MaxRetry}, Error={Error}",
                            item.Id, attempts, SendLogStatus.MaxRetryCount, ex.Message);
                    }
                }
                catch (Exception saveEx)
                {
                    _logger.LogError(saveEx, "保存邮件发送失败状态时出错: QueueId={QueueId}", item.Id);
                }
            }
            else
            {
                _logger.LogError("回滚后重新获取 SendLog 失败：QueueId={QueueId}", item.Id);
            }

            _logger.LogError(ex, "邮件发送失败: QueueId={QueueId}, To={To}, Attempt={Attempt}", item.Id, item.Email, item.AttemptCount);
        }
    }
}
