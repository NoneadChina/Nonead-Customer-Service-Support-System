using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services.WorkflowActions;

/// <summary>
/// IM 消息发送结果：用于队列消费方获取发送状态和渠道返回的外部消息 Id。
/// <para>
/// <see cref="ErrorCode"/> + <see cref="IsTerminal"/> 是给队列用的：像 Messenger 的
/// 190（令牌失效）/ 2635（PSID 不属本 Page）/ 551（用户不可达）/ 100（参数非法）这类失败
/// 重试多少次都不会成功，只会烧配额并延后真正的失败上报，因此由渠道侧判定为终态，
/// 队列见到 IsTerminal 就一次到位标记 Failed（与 iMessage 的 HTTP 终态语义一致）。
/// </para>
/// </summary>
public record ImMessageSendResult(
    bool Success,
    string? ExternalMessageId = null,
    string? ErrorMessage = null,
    string? ErrorCode = null,
    bool IsTerminal = false);

/// <summary>
/// 聊天渠道消息发送器接口。
/// 用于在邮件无法送达时（如客户无邮箱），通过对应聊天渠道
/// （微信/飞书/WhatsApp/钉钉/Messenger/Telegram/LINE/iMessage）发送通知消息。
/// </summary>
public interface IChatChannelMessageSender
{
    /// <summary>
    /// 判断指定会话是否支持通过聊天渠道发送消息。
    /// 支持 WeChat / Feishu / WhatsApp / DingTalk / Messenger / Telegram / Line / IMessage 八种来源类型。
    /// </summary>
    bool SupportsChatChannel(Conversation conversation);

    /// <summary>
    /// 通过会话对应的聊天渠道发送文本消息（同步路径，仅供测试/调试使用）。
    /// 失败将记录警告日志但不抛异常，返回 false。
    /// 生产路径请改用 <see cref="IImMessageQueueService.EnqueueAsync"/> 入队异步处理。
    /// </summary>
    /// <param name="conversation">目标会话</param>
    /// <param name="text">消息文本</param>
    /// <returns>发送成功返回 true；不支持、配置缺失或发送失败返回 false</returns>
    Task<bool> SendAsync(Conversation conversation, string text);

    /// <summary>
    /// 按渠道类型和 ChannelId 直接发送消息（供 IM 消息队列消费方调用）。
    /// 该方法绕过 Customer 二次查询，直接调用底层渠道 Client。
    /// 失败将记录警告日志但不抛异常，通过 <see cref="ImMessageSendResult"/> 返回失败信息。
    /// </summary>
    /// <param name="sourceType">渠道类型（WeChat / Feishu / WhatsApp / DingTalk 之一）</param>
    /// <param name="channelId">客户在该渠道中的标识（openId / waId / userId）</param>
    /// <param name="conversationId">关联会话 Id（仅用于日志记录）</param>
    /// <param name="text">消息文本</param>
    /// <returns>发送结果，包含成功标志、外部消息 Id 和错误信息</returns>
    Task<ImMessageSendResult> SendByChannelAsync(SourceType sourceType, string channelId, int conversationId, string text);
}

/// <summary>
/// 聊天渠道消息发送器实现。
/// 通过 Customer.Channel + ChannelId 定位客户渠道标识，再调用对应渠道 Client 发送消息。
/// 钉钉使用 BatchSendOtoMessageAsync 主动发送（sessionWebhook 已过期）。
/// </summary>
public class ChatChannelMessageSender : IChatChannelMessageSender
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWeChatClient _weChatClient;
    private readonly IFeishuClient _feishuClient;
    private readonly IWhatsAppClient _whatsAppClient;
    private readonly IDingDingClient _dingDingClient;
    private readonly IDingDingSettingsProvider _dingDingSettingsProvider;
    private readonly IMessengerClient _messengerClient;
    private readonly IMessengerSettingsProvider _messengerSettingsProvider;
    private readonly ITelegramClient _telegramClient;
    private readonly ITelegramSettingsProvider? _telegramSettingsProvider;
    private readonly TelegramThrottle? _telegramThrottle;
    private readonly ILineClient _lineClient;
    private readonly IIMessageClient _imessageClient;
    private readonly ILogger<ChatChannelMessageSender> _logger;

    public ChatChannelMessageSender(
        IUnitOfWork unitOfWork,
        IWeChatClient weChatClient,
        IFeishuClient feishuClient,
        IWhatsAppClient whatsAppClient,
        IDingDingClient dingDingClient,
        IDingDingSettingsProvider dingDingSettingsProvider,
        IMessengerClient messengerClient,
        IMessengerSettingsProvider messengerSettingsProvider,
        ITelegramClient telegramClient,
        ILineClient lineClient,
        IIMessageClient imessageClient,
        ILogger<ChatChannelMessageSender> logger,
        // 以下两项仅供 Telegram 分支使用。放在可选参数位是为了不破坏既有直接 new 的集成测试；
        // 生产环境由 DI 容器解析注入（ITelegramSettingsProvider 与单例 TelegramThrottle 均已注册）。
        ITelegramSettingsProvider? telegramSettingsProvider = null,
        TelegramThrottle? telegramThrottle = null)
    {
        _unitOfWork = unitOfWork;
        _weChatClient = weChatClient;
        _feishuClient = feishuClient;
        _whatsAppClient = whatsAppClient;
        _dingDingClient = dingDingClient;
        _dingDingSettingsProvider = dingDingSettingsProvider;
        _messengerClient = messengerClient;
        _messengerSettingsProvider = messengerSettingsProvider;
        _telegramClient = telegramClient;
        _telegramSettingsProvider = telegramSettingsProvider;
        _telegramThrottle = telegramThrottle;
        _lineClient = lineClient;
        _imessageClient = imessageClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool SupportsChatChannel(Conversation conversation)
    {
        return conversation.SourceType is SourceType.WeChat
            or SourceType.Feishu
            or SourceType.WhatsApp
            or SourceType.DingTalk
            or SourceType.Messenger
            or SourceType.Telegram
            or SourceType.Line
            or SourceType.IMessage;
    }

    /// <inheritdoc/>
    public async Task<bool> SendAsync(Conversation conversation, string text)
    {
        if (!SupportsChatChannel(conversation))
        {
            _logger.LogDebug("会话 {ConversationId} 来源类型 {SourceType} 不支持聊天渠道回退",
                conversation.Id, conversation.SourceType);
            return false;
        }

        if (!conversation.CustomerId.HasValue)
        {
            _logger.LogWarning("会话 {ConversationId} 无客户关联，无法发送聊天消息", conversation.Id);
            return false;
        }

        var customer = await _unitOfWork.Customers.GetByIdAsync(conversation.CustomerId.Value);
        if (customer == null || string.IsNullOrWhiteSpace(customer.ChannelId))
        {
            _logger.LogWarning("会话 {ConversationId} 客户 {CustomerId} 无 ChannelId，无法发送聊天消息",
                conversation.Id, conversation.CustomerId);
            return false;
        }

        var result = await SendByChannelAsync(conversation.SourceType, customer.ChannelId!, conversation.Id, text);
        return result.Success;
    }

    /// <inheritdoc/>
    public async Task<ImMessageSendResult> SendByChannelAsync(SourceType sourceType, string channelId, int conversationId, string text)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return new ImMessageSendResult(false, null, "ChannelId 为空");
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ImMessageSendResult(false, null, "消息文本为空");
        }

        try
        {
            return sourceType switch
            {
                SourceType.WeChat => await SendWeChatAsync(channelId, text, conversationId),
                SourceType.Feishu => await SendFeishuAsync(channelId, text, conversationId),
                SourceType.WhatsApp => await SendWhatsAppAsync(channelId, text, conversationId),
                SourceType.DingTalk => await SendDingTalkAsync(channelId, text, conversationId),
                SourceType.Messenger => await SendMessengerAsync(channelId, text, conversationId),
                SourceType.Telegram => await SendTelegramAsync(channelId, text, conversationId),
                SourceType.Line => await SendLineAsync(channelId, text, conversationId),
                SourceType.IMessage => await SendIMessageAsync(channelId, text, conversationId),
                _ => new ImMessageSendResult(false, null, $"不支持的渠道类型: {sourceType}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "会话 {ConversationId} 通过 {Channel} 渠道发送消息失败",
                conversationId, sourceType);
            return new ImMessageSendResult(false, null, ex.Message);
        }
    }

    private async Task<ImMessageSendResult> SendWeChatAsync(string openId, string text, int conversationId)
    {
        // 微信 48 小时客服窗口限制：超时发送会失败，记录警告但不影响主流程
        var sent = await _weChatClient.SendTextMessageAsync(openId, text);
        if (sent)
        {
            _logger.LogInformation("微信渠道消息发送成功：ConversationId={ConversationId}, OpenId={OpenId}",
                conversationId, openId);
            // 微信 SendTextMessageAsync 仅返回 bool，无外部消息 Id
            return new ImMessageSendResult(true);
        }
        _logger.LogWarning("微信渠道消息发送失败（可能超过48小时客服窗口）：ConversationId={ConversationId}, OpenId={OpenId}",
            conversationId, openId);
        return new ImMessageSendResult(false, null, "微信 API 返回失败（可能超过48小时客服窗口）");
    }

    private async Task<ImMessageSendResult> SendFeishuAsync(string openId, string text, int conversationId)
    {
        // 飞书 SendMessageAsync content 需为 JSON 字符串：{"text":"消息内容"}
        var content = System.Text.Json.JsonSerializer.Serialize(new { text });
        var messageId = await _feishuClient.SendMessageAsync(openId, "text", content);
        if (!string.IsNullOrEmpty(messageId))
        {
            _logger.LogInformation("飞书渠道消息发送成功：ConversationId={ConversationId}, OpenId={OpenId}, MessageId={MessageId}",
                conversationId, openId, messageId);
            return new ImMessageSendResult(true, messageId);
        }
        _logger.LogWarning("飞书渠道消息发送失败：ConversationId={ConversationId}, OpenId={OpenId}",
            conversationId, openId);
        return new ImMessageSendResult(false, null, "飞书 API 返回空 message_id");
    }

    private async Task<ImMessageSendResult> SendWhatsAppAsync(string waId, string text, int conversationId)
    {
        var wamid = await _whatsAppClient.SendTextMessageAsync(waId, text);
        if (!string.IsNullOrEmpty(wamid))
        {
            _logger.LogInformation("WhatsApp 渠道消息发送成功：ConversationId={ConversationId}, WaId={WaId}, WamId={WamId}",
                conversationId, waId, wamid);
            return new ImMessageSendResult(true, wamid);
        }
        _logger.LogWarning("WhatsApp 渠道消息发送失败：ConversationId={ConversationId}, WaId={WaId}",
            conversationId, waId);
        return new ImMessageSendResult(false, null, "WhatsApp API 返回空 wamid");
    }

    private async Task<ImMessageSendResult> SendDingTalkAsync(string userId, string text, int conversationId)
    {
        // 钉钉 sessionWebhook 有效期仅2小时，会话关闭时通常已过期，
        // 使用 BatchSendOtoMessageAsync 主动发送单聊消息
        var settings = await _dingDingSettingsProvider.GetCurrentAsync();
        var robotCode = !string.IsNullOrEmpty(settings.RobotCode) ? settings.RobotCode : settings.AppKey;
        if (string.IsNullOrEmpty(robotCode))
        {
            _logger.LogWarning("钉钉 RobotCode 和 AppKey 均为空，无法发送主动消息：ConversationId={ConversationId}", conversationId);
            return new ImMessageSendResult(false, null, "钉钉 RobotCode 和 AppKey 均为空");
        }

        // T000114：钉钉 sampleText（text）消息中的 URL 不可点击，客户无法打开评价链接。
        // 改用 sampleMarkdown，并自动将 http(s):// 链接包装为 markdown 链接 [url](url) 使其可点击；
        // 即使钉钉对 http 外链有限制，原 URL 文本仍完整可见，客户可手动复制。
        var markdownText = MakeDingTalkMarkdown(text);
        var content = System.Text.Json.JsonSerializer.Serialize(new { title = "服务通知", text = markdownText });
        var processQueryKey = await _dingDingClient.BatchSendOtoMessageAsync(robotCode!, new[] { userId }, "sampleMarkdown", content);
        if (!string.IsNullOrEmpty(processQueryKey))
        {
            _logger.LogInformation("钉钉渠道消息发送成功：ConversationId={ConversationId}, UserId={UserId}, ProcessQueryKey={Key}",
                conversationId, userId, processQueryKey);
            return new ImMessageSendResult(true, processQueryKey);
        }
        _logger.LogWarning("钉钉渠道消息发送失败：ConversationId={ConversationId}, UserId={UserId}",
            conversationId, userId);
        return new ImMessageSendResult(false, null, "钉钉 API 返回空 processQueryKey");
    }

    /// <summary>
    /// Messenger（Meta Graph Send API）。队列路径没有 reply 上下文，且这类消息本身多为会话关闭/满意度等
    /// 延迟通知，因此按"24 小时客服窗口已过"处理：依 messenger.message_tag_policy 决定是否带 message_tag。
    /// auto/always ⇒ 带 HUMAN_AGENT（窗口外客服场景唯一可用 tag）；none ⇒ 不主动投递。
    /// </summary>
    private async Task<ImMessageSendResult> SendMessengerAsync(string psid, string text, int conversationId)
    {
        var opt = await _messengerSettingsProvider.GetCurrentAsync();
        var policy = (opt.MessageTagPolicy ?? "auto").Trim().ToLowerInvariant();

        // 先按「客户最后一次来信时间」判断是否真的在 24 小时标准窗口内：
        // 窗口内发送不该带 message_tag（带 tag 会被 Graph 视为"人工代理"语义，且部分 tag 有独立有效期），
        // 原来的实现只要 policy≠none 就无条件挂 HUMAN_AGENT，等于把窗口内的正常主动消息也标成人工代理消息。
        var latestCustomerThread = await _unitOfWork.Threads.GetLatestCustomerThreadAsync(conversationId);
        var windowHours = opt.WindowHours > 0 ? opt.WindowHours : 24;
        var withinWindow = latestCustomerThread is not null
            && latestCustomerThread.CreatedAt >= DateTime.UtcNow.AddHours(-windowHours);

        string? messageTag = null;
        if (!withinWindow)
        {
            if (policy == "none")
            {
                _logger.LogWarning("Messenger 窗口外主动消息按 MessageTagPolicy=none 跳过（24h 窗口已过）：ConversationId={ConversationId}, Psid={Psid}, 最后来信={LastInbound}",
                    conversationId, psid, latestCustomerThread?.CreatedAt);
                return new ImMessageSendResult(false, null, "Messenger 窗口外不可投递（MessageTagPolicy=none）");
            }

            messageTag = string.IsNullOrWhiteSpace(opt.MessageTag) ? "HUMAN_AGENT" : opt.MessageTag.Trim();
        }

        var mid = await _messengerClient.SendTextMessageAsync(psid, text, messageTag);
        if (!string.IsNullOrEmpty(mid))
        {
            _logger.LogInformation("Messenger 渠道消息发送成功：ConversationId={ConversationId}, Psid={Psid}, Mid={Mid}, Tag={Tag}",
                conversationId, psid, mid, messageTag ?? "(窗口内，无 tag)");
            return new ImMessageSendResult(true, mid);
        }
        _logger.LogWarning("Messenger 渠道消息发送失败（常见原因：窗口过期且 tag 不被接受 / 190 token 失效 / 2635 PSID 不属本 Page）：ConversationId={ConversationId}, Psid={Psid}, ErrorCode={ErrorCode}",
            conversationId, psid, _messengerClient.LastError?.Code);

        // Graph 的这几类错误重试永远不会成功：190 令牌失效、2635 PSID 不属本 Page、
        // 551 用户不可达（拉黑/删除会话）、100 参数非法。判为终态，队列不再重试。
        var lastError = _messengerClient.LastError;
        var errorCode = lastError?.Code;
        var isTerminal = errorCode is 190 or 2635 or 551 or 100;
        var reason = errorCode.HasValue
            ? $"Messenger Graph API 未返回 mid（error_code={errorCode}，{lastError!.Message}）"
            : "Messenger Graph API 未返回 mid（Graph 未返回错误码，详见响应日志中的 error_code/fbtrace_id）";
        return new ImMessageSendResult(false, null, reason, errorCode?.ToString(), isTerminal);
    }

    /// <summary>
    /// Telegram Bot API sendMessage。chat_id 取 Customer.ChannelId（数值字符串或 -100… 超级群）。
    /// 403=已拉黑 bot（终态）、429=限流（RetryAfterSeconds 由队列侧的重试策略消费）、
    /// migrate_to_chat_id=超级群迁移，需人工把新的 chat_id 写回客户档案。
    ///
    /// 该分支必须走与坐席手工回复完全相同的出站链路（<see cref="TelegramOutboundSender"/>）：
    /// 原先直接 new 调用 <see cref="ITelegramClient.SendTextMessageAsync"/>，导致排队/工作流消息
    /// 既不按 4096 字符分片、也不限速、403 不标记不可达、429 的 retry_after 不落地。
    /// </summary>
    private async Task<ImMessageSendResult> SendTelegramAsync(string chatId, string text, int conversationId)
    {
        if (_telegramSettingsProvider is null || _telegramThrottle is null)
        {
            // 生产环境由 DI 注入；仅当外部绕过容器直接 new 时才可能为空，此时宁可明确失败也不静默降级为无限流直发。
            _logger.LogWarning("Telegram 发送依赖未注入（ITelegramSettingsProvider / TelegramThrottle），无法保证分片与限速：ConversationId={ConversationId}",
                conversationId);
            return new ImMessageSendResult(false, null, "Telegram 发送器未初始化（缺少设置/限速依赖），请检查依赖注入注册");
        }

        var options = await _telegramSettingsProvider.GetCurrentAsync();
        if (!options.Enabled || !options.IsConfigured)
        {
            return new ImMessageSendResult(false, null, "Telegram 未启用或未配置");
        }

        // 与手工回复一致：超长文本按 Markdown 边界/空格/硬切分片，避免 Bot API 400 message is too long。
        var parts = TelegramOutboundSender.SplitText(text.Trim(), options);
        if (parts.Count == 0)
        {
            return new ImMessageSendResult(false, null, "消息内容为空");
        }

        var sentMessageIds = new List<string>();
        foreach (var part in parts)
        {
            var result = await TelegramOutboundSender.SendTextWithThrottleAsync(
                _telegramClient, _telegramThrottle, chatId, part, null, options);

            if (result.Success)
            {
                if (!string.IsNullOrEmpty(result.MessageId))
                {
                    sentMessageIds.Add(result.MessageId!);
                }

                continue;
            }

            // 403/不可达是终态：写入不可达标记，队列侧据此停止重试（原先只有手工回复会标记）。
            if (result.Blocked || result.Unreachable)
            {
                var reason = result.Blocked
                    ? "客户已拉黑机器人或 bot 已被移出群（403），已标记为不可达，停止重试"
                    : result.Description ?? "该 chat 不可投递（chat not found / bot 无法主动发起会话）";
                try
                {
                    await TelegramUnreachableState.MarkAsync(_unitOfWork, conversationId, reason);
                    await _unitOfWork.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "标记 Telegram 不可达失败：ConversationId={ConversationId}", conversationId);
                }

                _logger.LogWarning("Telegram 渠道消息发送失败（不可达）：ConversationId={ConversationId}, ChatId={ChatId}, Reason={Reason}",
                    conversationId, chatId, reason);
                return new ImMessageSendResult(false, sentMessageIds.FirstOrDefault(), $"{reason}：{result.ErrorMessage ?? result.Description}");
            }

            if (result.MigrateToChatId.HasValue)
            {
                _logger.LogWarning("Telegram 群组已迁移为超级群，需把客户 ChannelId 由 {OldChatId} 更新为 {NewChatId}：ConversationId={ConversationId}",
                    chatId, result.MigrateToChatId.Value, conversationId);
            }

            var failureReason = result.InvalidToken ? "Bot Token 无效（401）"
                : result.Conflict ? "webhook 与 getUpdates 冲突（409）"
                : result.RetryAfterSeconds.HasValue ? $"限流（{result.ErrorCode}），{result.RetryAfterSeconds.Value}s 后重试"
                : $"Telegram API 返回 {result.ErrorCode}";
            _logger.LogWarning("Telegram 渠道消息发送失败：ConversationId={ConversationId}, ChatId={ChatId}, Reason={Reason}, Description={Description}",
                conversationId, chatId, failureReason, result.Description);
            return new ImMessageSendResult(false, sentMessageIds.FirstOrDefault(), $"{failureReason}：{result.ErrorMessage ?? result.Description}");
        }

        _logger.LogInformation("Telegram 渠道消息发送成功：ConversationId={ConversationId}, ChatId={ChatId}, Parts={Parts}, MessageId={MessageId}",
            conversationId, chatId, sentMessageIds.Count, sentMessageIds.FirstOrDefault());
        return new ImMessageSendResult(true, sentMessageIds.FirstOrDefault());
    }

    /// <summary>
    /// LINE Messaging API。队列路径无 replyToken（一次性、约 30 秒有效），只能用 push_message：
    /// 消耗月度配额，且仅对已加好友的用户可达（404=非好友 / 403=屏蔽或配额用尽 ⇒ 判为不可达）。
    /// LINE 成功响应体为空对象，没有外部消息 id。
    /// </summary>
    private async Task<ImMessageSendResult> SendLineAsync(string userId, string text, int conversationId)
    {
        var result = await _lineClient.SendTextAsync(text, toUserId: userId);
        if (result.Success)
        {
            _logger.LogInformation("LINE 渠道消息发送成功：ConversationId={ConversationId}, UserId={UserId}, Via={Via}, Chunks={Chunks}",
                conversationId, userId, result.Via, result.ChunkCount);
            return new ImMessageSendResult(true, result.ExternalMessageId);
        }

        var reason = result.Unreachable ? "客户未添加机器人为好友或已屏蔽（404/403）" : "LINE push 失败";
        _logger.LogWarning("LINE 渠道消息发送失败：ConversationId={ConversationId}, UserId={UserId}, Reason={Reason}, Error={Error}",
            conversationId, userId, reason, result.ErrorMessage);
        return new ImMessageSendResult(false, null, $"{reason}：{result.ErrorMessage}");
    }

    /// <summary>
    /// iMessage。destination 取 Customer.ChannelId（handle 如 tel:+… / email，或桥接层的 chatGuid）。
    /// 无固定时间窗（会话式投递），但若对端从未与本业务号通信过，Apple 会返回 404 且不会重投。
    /// </summary>
    private async Task<ImMessageSendResult> SendIMessageAsync(string destination, string text, int conversationId)
    {
        // 出站队列里的 ChannelId 是「模式作用域」标识（如 bridge:+15551234567 / amb_msp:urn:mbid:xyz），
        // 不能原样当投递目标：AMB 的 destination-id 要裸外部 id，BlueBubbles 的 chatGuid 必须带服务前缀，
        // 裸 handle 会被桥接服务拒绝（-101001009）。这里与 IMessageService.ReplyAsync 用同一套换算。
        var target = IMessageIdentity.IsAmbMode(destination)
            ? IMessageIdentity.ExtractExternalId(destination)
            : IMessageIdentity.BuildReplyGuid(destination, inboundService: null);

        // 带上跨重试稳定的幂等键：AMB 按 id 去重，重试换 id 会让客户收到两份；
        // 桥接模式下同一个键同时作为 BlueBubbles 的 tempGuid。
        var externalId = await _imessageClient.SendTextMessageAsync(
            target, text, BuildIMessageIdempotencyKey(conversationId, text));
        if (!string.IsNullOrEmpty(externalId))
        {
            _logger.LogInformation("iMessage 渠道消息发送成功：ConversationId={ConversationId}, Destination={Destination}, MessageRef={Ref}",
                conversationId, destination, externalId);
            return new ImMessageSendResult(true, externalId);
        }
        _logger.LogWarning("iMessage 渠道消息发送失败（常见原因：桥接服务不可达 / 该 handle 无既有会话 / 目标设备已关闭 410）：ConversationId={ConversationId}, Destination={Destination}",
            conversationId, destination);
        return new ImMessageSendResult(false, null, "iMessage 发送未返回消息标识（检查桥接服务连通性与目标会话是否存在）");
    }

    /// <summary>
    /// 为 iMessage 出站构造**跨重试稳定**的幂等键。
    /// AMB 把 id 当客户端消息标识（重试换 id ⇒ 网关重复投递），而本臂拿不到出站队列行 Id
    /// （队列的 iMessage 分支直连 IIMessageClient，见 ImMessageQueueService）。
    /// 本臂可用的稳定身份只有 (会话 Id, 文本) ⇒ 用它派生：同一会话的同一文本重试得到同一 id，
    /// 不同文本/不同会话得到不同 id。已知副作用：同一会话里重复发送**完全相同**的文本会被 AMB
    /// 去重成一条（若业务需要"同文重发"，应由调用方带来队列行级身份）。
    /// </summary>
    private static string BuildIMessageIdempotencyKey(int conversationId, string text)
    {
        var textHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty)));
        return $"im-{conversationId}-{textHash[..32]}";
    }

    private static readonly System.Text.RegularExpressions.Regex UrlRegex =
        new(@"https?://[^\s]+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// 将文本中的 http(s):// 链接包装为 markdown 可点击链接 [url](url)。
    /// 无链接的普通文本原样返回（markdown 渲染不受影响）。
    /// </summary>
    private static string MakeDingTalkMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;
        return UrlRegex.Replace(text, m => $"[{m.Value}]({m.Value})");
    }
}
