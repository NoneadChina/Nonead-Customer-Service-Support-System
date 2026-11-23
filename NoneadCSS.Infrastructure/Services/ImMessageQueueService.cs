using NoneadCSS.Application.Common;
using NoneadCSS.Application.Services;
using NoneadCSS.Application.Services.WorkflowActions;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// IM 消息发送队列服务实现：将待发送的 IM 消息持久化入队，
/// 由后台 Job 异步处理，支持失败重试（指数退避）。
/// 参考 <see cref="EmailQueueService"/> 的设计模式。
/// </summary>
public class ImMessageQueueService : IImMessageQueueService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChatChannelMessageSender _chatChannelMessageSender;
    private readonly IIMessageClient _imessageClient;
    private readonly ILogger<ImMessageQueueService> _logger;

    public ImMessageQueueService(
        IUnitOfWork unitOfWork,
        IChatChannelMessageSender chatChannelMessageSender,
        IIMessageClient imessageClient,
        ILogger<ImMessageQueueService> logger)
    {
        _unitOfWork = unitOfWork;
        _chatChannelMessageSender = chatChannelMessageSender;
        _imessageClient = imessageClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<int?> EnqueueAsync(Conversation conversation, string text)
    {
        // 验证：必须是 IM 渠道
        if (!_chatChannelMessageSender.SupportsChatChannel(conversation))
        {
            _logger.LogDebug("会话 {ConversationId} 不支持 IM 渠道，跳过入队", conversation.Id);
            return null;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("会话 {ConversationId} 入队消息文本为空，跳过", conversation.Id);
            return null;
        }

        // 取客户的 ChannelId
        if (!conversation.CustomerId.HasValue)
        {
            _logger.LogWarning("会话 {ConversationId} 无客户关联，无法入队 IM 消息", conversation.Id);
            return null;
        }

        var customer = await _unitOfWork.Customers.GetByIdAsync(conversation.CustomerId.Value);
        if (customer == null || string.IsNullOrWhiteSpace(customer.ChannelId))
        {
            _logger.LogWarning("会话 {ConversationId} 客户 {CustomerId} 无 ChannelId，无法入队 IM 消息",
                conversation.Id, conversation.CustomerId);
            return null;
        }

        var log = new ImMessageLog
        {
            ConversationId = conversation.Id,
            CustomerId = conversation.CustomerId,
            SourceType = conversation.SourceType,
            ChannelId = customer.ChannelId!,
            MessageText = text,
            Status = ImMessageLogStatus.Pending,
            AttemptCount = 0,
            MaxAttempts = 4,
            NextRetryAt = DateTime.UtcNow,  // 立即可处理
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.ImMessageLogs.AddAsync(log);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("IM 消息已入队：QueueId={QueueId}, ConversationId={ConversationId}, SourceType={SourceType}, ChannelId={ChannelId}",
            log.Id, conversation.Id, conversation.SourceType, customer.ChannelId);
        return log.Id;
    }

    /// <inheritdoc/>
    public async Task ProcessQueueAsync()
    {
        // T3: 先回收异常遗留的 Sending 记录（进程崩溃/异常导致卡死），避免消息永久丢失
        try
        {
            var recovered = await _unitOfWork.ImMessageLogs.ResetStaleSendingAsync(TimeSpan.FromMinutes(5));
            if (recovered > 0)
            {
                _logger.LogWarning("IM 消息队列回收 {Recovered} 条卡死的 Sending 记录为 Pending", recovered);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "回收卡死的 IM Sending 记录失败");
        }

        var pendingItems = await _unitOfWork.ImMessageLogs.GetPendingAsync();
        if (pendingItems.Count == 0)
        {
            return;
        }

        _logger.LogInformation("开始处理 IM 消息队列，待处理数量: {Count}", pendingItems.Count);

        foreach (var item in pendingItems)
        {
            try
            {
                await ProcessQueueItemAsync(item.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理 IM 消息队列项异常：QueueId={QueueId}", item.Id);
            }
        }

        _logger.LogInformation("IM 消息队列处理完成");
    }

    private async Task ProcessQueueItemAsync(int logId)
    {
        // 重新取出跟踪实体（GetPendingAsync 返回的是 AsNoTracking 列表，
        // 直接修改不会持久化，所以这里用 GetByIdTrackedAsync 重新取出跟踪实例）
        var item = await _unitOfWork.ImMessageLogs.GetByIdTrackedAsync(logId);
        if (item == null)
        {
            _logger.LogWarning("IM 消息队列项不存在：QueueId={QueueId}", logId);
            return;
        }

        // 双重检查：状态必须是 Pending（防止并发重复处理）
        if (item.Status != ImMessageLogStatus.Pending)
        {
            _logger.LogDebug("IM 消息队列项状态非 Pending，跳过：QueueId={QueueId}, Status={Status}",
                logId, item.Status);
            return;
        }

        // 标记为发送中（持久化，避免其他处理实例重复处理）
        item.MarkAsSending();
        await _unitOfWork.SaveChangesAsync();

        // 调用渠道发送：iMessage 需要"带 HTTP 状态码"的结果来区分永久失败与可重试失败，
        // 而 IChatChannelMessageSender 只回 bool，所以 iMessage 直接走 IIMessageClient；
        // 其它渠道保持原有路径（SendByChannelAsync 绕过 Customer 二次查询）。
        ImMessageSendResult sendResult;
        IMessageSendOutcome? imessageOutcome = null;
        try
        {
            if (item.SourceType == SourceType.IMessage)
            {
                imessageOutcome = await _imessageClient.SendTextMessageWithOutcomeAsync(
                    ResolveIMessageTarget(item.ChannelId), item.MessageText,
                    // 重试必须复用同一个 AMB id：它由队列行 (Id, ConversationId) 纯函数派生，跨尝试稳定；
                    // 每次换 id 的话网关会当成新消息，客户会收到两份。
                    NoneadCSS.Infrastructure.Services.IMessage.IMessageIdempotency.DeriveAmbMessageId(
                        item.Id, item.ConversationId));
                sendResult = imessageOutcome.Success
                    ? new ImMessageSendResult(true, imessageOutcome.ExternalMessageId)
                    : new ImMessageSendResult(false, null, imessageOutcome.ErrorMessage ?? "iMessage 发送失败");
            }
            else
            {
                sendResult = await _chatChannelMessageSender.SendByChannelAsync(
                    item.SourceType, item.ChannelId, item.ConversationId, item.MessageText);
            }
        }
        catch (Exception ex)
        {
            // T3: 渠道抛异常（网络超时/API 异常）时不可让消息永久卡在 Sending 状态——
            // MarkAsFailed 会 AttemptCount++ 并决定回退 Pending（重试）或最终 Failed
            _logger.LogWarning(ex, "IM 消息发送抛异常，转入重试/失败路径：QueueId={QueueId}, Attempt={Attempt}/{MaxAttempts}",
                item.Id, item.AttemptCount + 1, item.MaxAttempts);
            var errMsg = ex.Message ?? "未知异常";
            item.MarkAsFailed(errMsg);
            try { await _unitOfWork.SaveChangesAsync(); }
            catch (Exception saveEx) { _logger.LogError(saveEx, "IM 消息异常状态落库失败：QueueId={QueueId}", item.Id); }
            _logger.LogWarning("IM 消息发送异常（将重试或最终失败）：QueueId={QueueId}, Attempt={Attempt}/{MaxAttempts}, Error={Error}",
                item.Id, item.AttemptCount, item.MaxAttempts, errMsg);
            return;
        }

        if (sendResult.Success)
        {
            // 发送成功：AttemptCount 不递增（MarkAsSent 内部不修改）
            item.MarkAsSent(sendResult.ExternalMessageId);
            await _unitOfWork.SaveChangesAsync();
            _logger.LogInformation("IM 消息发送成功：QueueId={QueueId}, Attempt={Attempt}, ExternalMessageId={ExternalMessageId}",
                item.Id, item.AttemptCount + 1, sendResult.ExternalMessageId);
            return;
        }

        // 发送失败：MarkAsFailed 内部会 AttemptCount++，并根据 AttemptCount 与 MaxAttempts 比较
        // 决定是回退为 Pending（等待下次重试）还是标记为 Failed（最终失败）。
        // 但**永久失败**必须一次到位标记 Failed —— 旧实现无差别重试，纯烧配额：
        //   · iMessage：400/403/404/410（参数错、凭据错、目标不存在、用户已关闭会话）
        //   · Messenger：190 令牌失效 / 2635 PSID 不属本 Page / 551 用户不可达 / 100 参数非法
        //     （由 IChatChannelMessageSender 判定后放进 ImMessageSendResult.IsTerminal）
        var isTerminalFailure = imessageOutcome is { IsTransient: false } || sendResult.IsTerminal;
        var failureReason = BuildFailureReason(sendResult.ErrorMessage, sendResult.ErrorCode, imessageOutcome);

        if (isTerminalFailure)
        {
            // 不新增领域方法：先把 AttemptCount 预置到 MaxAttempts-1，MarkAsFailed 的 ++ 正好触顶 ⇒ Failed + 清空重试时间
            item.AttemptCount = item.MaxAttempts - 1;
        }

        var willRetry = !isTerminalFailure && item.AttemptCount + 1 < item.MaxAttempts;
        // 永久失败：AttemptCount 会变成 MaxAttempts（终态 Failed），不会再有下一次
        item.MarkAsFailed(failureReason);
        await _unitOfWork.SaveChangesAsync();

        if (isTerminalFailure)
        {
            _logger.LogError("IM 消息永久失败（不重试）：QueueId={QueueId}, SourceType={SourceType}, Status={Status}, Code={Code}, Error={Error}",
                item.Id, item.SourceType,
                imessageOutcome?.HttpStatusCode?.ToString() ?? sendResult.ErrorCode ?? "-",
                imessageOutcome?.ProviderErrorCode, failureReason);            return;
        }

        if (willRetry && item.Status == ImMessageLogStatus.Pending)
        {
            _logger.LogWarning("IM 消息发送失败（将重试）：QueueId={QueueId}, Attempt={Attempt}/{MaxAttempts}, NextRetryAt={NextRetryAt:O}, Error={Error}",
                item.Id, item.AttemptCount, item.MaxAttempts, item.NextRetryAt, failureReason);
        }
        else
        {
            _logger.LogError("IM 消息发送最终失败：QueueId={QueueId}, Attempt={Attempt}/{MaxAttempts}, Error={Error}",
                item.Id, item.AttemptCount, item.MaxAttempts, failureReason);
        }
    }

    /// <summary>
    /// iMessage 目标地址归一化：与 IChatChannelMessageSender.SendIMessageAsync 内部保持一致 ——
    /// AMB 用裸 external id，bridge 用 guid 重新拼装（否则 bridge 会把 scoped id 当 chatGuid 发出去）。
    /// </summary>
    private static string ResolveIMessageTarget(string? scopedChannelId)
    {
        if (string.IsNullOrWhiteSpace(scopedChannelId)) return string.Empty;

        return IMessageIdentity.IsAmbMode(scopedChannelId)
            ? IMessageIdentity.ExtractExternalId(scopedChannelId)
            : IMessageIdentity.BuildReplyGuid(scopedChannelId, inboundService: null);
    }

    /// <summary>把失败原因拼成可直接落库/排查的字符串（带平台错误码，不含任何凭据）。</summary>
    private static string BuildFailureReason(string? message, string? errorCode, IMessageSendOutcome? outcome)
    {
        if (outcome is null)
        {
            var plain = string.IsNullOrWhiteSpace(message) ? "未知错误" : message!;
            return string.IsNullOrWhiteSpace(errorCode) ? plain : $"{plain}（平台错误码 {errorCode}）";
        }

        var reason = string.IsNullOrWhiteSpace(outcome.ErrorMessage) ? "iMessage 发送失败" : outcome.ErrorMessage!;
        if (!string.IsNullOrWhiteSpace(outcome.ProviderErrorCode))
            reason += $"（平台错误码 {outcome.ProviderErrorCode}）";
        return reason;
    }
}
