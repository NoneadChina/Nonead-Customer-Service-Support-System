using System.Text;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Entities;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Application.Services;

/// <summary>
/// iMessage / Apple Messages 入站处理服务。
/// 两条传输共用同一套落库流程：bridge = BlueBubbles webhook（事件型），amb_* = Apple/MSP 直推（单条报文 + 头部身份）。
/// 关键约束（docs/integration/imessage.md §5）：
/// 1. 入站按消息 guid（bridge）/ Apple `Id` 头（amb）幂等去重；
/// 2. isFromMe=true 一律忽略（自己发出的回声）；
/// 3. 私聊身份归一化到裸 handle，群聊保留 chat GUID；
/// 4. 客户身份按 (channel_mode, external_id) 作用域，Customers.ChannelId 存 "{mode}:{externalId}"。
/// </summary>
public class IMessageIngestionService : IIMessageIngestionService
{
    /// <summary>Customers.Channel 字面量，必须与 ConversationRepository 包装、A17 测试映射完全一致。</summary>
    private const string ChannelName = "imessage";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IIMessageSettingsProvider _settingsProvider;
    private readonly IIMessageClient _client;
    private readonly IWorkflowService _workflowService;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IDistributedLockService _distributedLockService;
    private readonly IWebhookService? _webhookService;
    private readonly ILogger<IMessageIngestionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _env;
    private readonly IngestionMatchOptions _matchOptions;

    public IMessageIngestionService(
        IUnitOfWork unitOfWork,
        IIMessageSettingsProvider settingsProvider,
        IIMessageClient client,
        IWorkflowService workflowService,
        INotificationPublisher notificationPublisher,
        IDistributedLockService distributedLockService,
        ILogger<IMessageIngestionService> logger,
        IConfiguration configuration,
        IHostEnvironment env,
        IOptions<IngestionMatchOptions> matchOptions,
        IWebhookService? webhookService = null)
    {
        _unitOfWork = unitOfWork;
        _settingsProvider = settingsProvider;
        _client = client;
        _workflowService = workflowService;
        _notificationPublisher = notificationPublisher;
        _distributedLockService = distributedLockService;
        _logger = logger;
        _configuration = configuration;
        _env = env;
        _matchOptions = matchOptions.Value;
        _webhookService = webhookService;
    }

    /// <inheritdoc />
    public async Task<IMessageIngestionResult> ProcessWebhookPayloadAsync(string? payload, string? modeHint = null)
    {
        var result = new IMessageIngestionResult();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return result;
        }

        var settings = await _settingsProvider.GetCurrentAsync();

        // 停用 / 模式不可用 / 未配置时一律不入库：iMessage 入库会建客户、开会话、触发工作流与通知，
        // 历史缺陷是这两条入站路径都不判 Enabled，模块关掉后上游回调仍会持续写数据。
        if (!settings.Enabled || !settings.IsModeActive() || !settings.IsConfigured)
        {
            _logger.LogWarning("iMessage 回调已忽略：模块停用、模式不可用或未配置（Mode={Mode}）", settings.Mode);
            return result;
        }
        var mode = IMessageIdentity.NormalizeMode(modeHint ?? settings.Mode);
        var window = ResolveMatchWindow(settings);

        // BlueBubbles 的事件信封；非该形态的报文交给 AMB 通道处理（控制器已经分过流，这里只做兜底）。
        var parsed = IMessageWebhookParser.ParseEvent(payload);
        switch (parsed.Kind)
        {
            case IMessageEventKinds.NewMessage:
                if (parsed.Message != null)
                {
                    await ProcessIncomingMessageAsync(parsed.Message, mode, window, settings, result);
                }
                break;

            case IMessageEventKinds.UpdatedMessage:
            case IMessageEventKinds.ReadReceipt:
            case IMessageEventKinds.ChatReadStatus:
            case IMessageEventKinds.TypingIndicator:
            case IMessageEventKinds.MessageError:
            case IMessageEventKinds.MessageSendError:
                await ProcessEventUpdateAsync(parsed, mode, result);
                break;

            case IMessageEventKinds.GroupNameChange:
            case IMessageEventKinds.ParticipantAdded:
            case IMessageEventKinds.ParticipantRemoved:
            case IMessageEventKinds.ParticipantLeft:
            case IMessageEventKinds.ServerUpdate:
            case IMessageEventKinds.UrlChange:
            case IMessageEventKinds.Test:
                _logger.LogInformation("iMessage 事件（不落库）：Kind={Kind}, Notice={Notice}", parsed.Kind, parsed.Notice);
                result.IgnoredCount++;
                break;

            default:
                // 未知信封：可能是 AMB 报文被误投到 bridge 端点，也可能是未来版本新增事件。
                _logger.LogWarning("iMessage 未识别的 webhook 事件：Kind={Kind}, Notice={Notice}", parsed.Kind, parsed.Notice);
                result.IgnoredCount++;
                break;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IMessageIngestionResult> ProcessAmbInboundAsync(
        string? payload,
        string? sourceIdHeader,
        string? destinationIdHeader,
        string? idHeader)
    {
        var result = new IMessageIngestionResult();
        var settings = await _settingsProvider.GetCurrentAsync();

        // 停用 / 模式不可用 / 未配置时一律不入库：iMessage 入库会建客户、开会话、触发工作流与通知，
        // 历史缺陷是这两条入站路径都不判 Enabled，模块关掉后上游回调仍会持续写数据。
        if (!settings.Enabled || !settings.IsModeActive() || !settings.IsConfigured)
        {
            _logger.LogWarning("iMessage 回调已忽略：模块停用、模式不可用或未配置（Mode={Mode}）", settings.Mode);
            return result;
        }
        var mode = IMessageIdentity.NormalizeMode(settings.Mode);

        var parsed = IMessageWebhookParser.ParseAmbInbound(
            payload, sourceIdHeader, destinationIdHeader, idHeader, out var clientError);
        if (clientError != null)
        {
            // 调用方（控制器）据此回 400；此处只留痕，不吞错误。
            result.ErrorMessage = clientError;
            return result;
        }

        var window = ResolveMatchWindow(settings);

        if (parsed.Kind == "amb-close")
        {
            // Apple：只有用户能关闭会话；收到 close 之后必须屏蔽所有坐席发送，否则后续发送会 410。
            await HandleAmbCloseAsync(parsed, mode, result);
            return result;
        }

        if (parsed.Message != null)
        {
            await ProcessIncomingMessageAsync(parsed.Message, mode, window, settings, result);
        }
        else
        {
            result.IgnoredCount++;
        }

        return result;
    }

    // ------------------------------------------------------------------
    // 入站消息 → 客户 / 会话 / 线程
    // ------------------------------------------------------------------

    private async Task ProcessIncomingMessageAsync(
        IMessageWebhookMessage message,
        string mode,
        TimeSpan window,
        IMessageOptions settings,
        IMessageIngestionResult result)
    {
        if (message.IsFromMe)
        {
            // 自己（或坐席经同一 Apple Account）发出的回声，bridge 会回推，一律忽略。
            result.IgnoredCount++;
            return;
        }

        if (string.IsNullOrWhiteSpace(message.MessageGuid) || string.IsNullOrWhiteSpace(message.ExternalId))
        {
            _logger.LogWarning("iMessage 入站缺少 guid 或可归一化的发送方身份，已忽略：ChatGuid={ChatGuid}, Handle={Handle}",
                message.ChatGuid, message.HandleAddress);
            result.IgnoredCount++;
            return;
        }

        if (!IsAllowedByAllowList(settings.AllowList, message))
        {
            _logger.LogInformation("iMessage 入站被 allowlist 拦截：ExternalId={ExternalId}, Group={IsGroup}",
                message.ExternalId, message.IsGroup);
            result.IgnoredCount++;
            return;
        }

        // 去重（第一层）：同一 guid 并发到达时只让一个进入事务。锁必须在事务之外。
        var lockKey = $"imessage_ingest:{mode}:{message.MessageGuid}";
        var lockHandle = await _distributedLockService.TryAcquireAsync(lockKey, timeoutMs: 0);
        if (lockHandle == null)
        {
            result.IsDuplicate = true;
            return;
        }

        try
        {
            // 客户身份按 (mode, external_id) 作用域：AMB 的不透明 id 与 bridge 的 handle/GUID 命名空间不同。
            var scopedChannelId = IMessageIdentity.BuildScopedChannelId(mode, message.ExternalId);
            // 预热 chatIdentifier→chatGuid：回复时沿用入站见过的 GUID（含服务前缀），避免主动回复降级成 SMS。
            IMessageIdentity.RememberChatGuid(scopedChannelId, message.ChatGuid);
            var displayName = ResolveDisplayName(message);
            var customer = await _unitOfWork.Customers.GetOrCreateByChannelAsync(ChannelName, scopedChannelId, displayName);

            var mailboxId = await ResolveMailboxIdAsync(settings);
            var conversation = await _unitOfWork.Conversations
                .FindActiveIMessageConversationAsync(scopedChannelId, window);
            var isNewConversation = conversation == null;

            await using var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                // 去重（第二层）：事务内按消息 id 复查，覆盖"上一批已落库、锁已释放"的情形。
                var existingThreads = await _unitOfWork.Threads.FindAsync(
                    t => t.MessageId == message.MessageGuid && t.State != ThreadState.Deleted);
                if (existingThreads.Any())
                {
                    await transaction.SafeRollbackAsync();
                    result.IsDuplicate = true;
                    return;
                }

                if (conversation == null)
                {
                    conversation = await CreateNewConversationAsync(customer, mailboxId, message, displayName);
                }
                else if (conversation.Status == ConversationStatus.Closed)
                {
                    // 客户在已关闭会话里重新发言 ⇒ 会话制渠道必须重开，而不是另起新会话。
                    conversation.Status = ConversationStatus.Active;
                    conversation.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    conversation.LastReplyAt = DateTime.UtcNow;
                    conversation.LastReplyFrom = SourceVia.Customer;
                    conversation.UpdatedAt = DateTime.UtcNow;
                }

                var thread = CreateThreadFromMessage(conversation, customer, message, displayName);
                await _unitOfWork.Threads.AddAsync(thread);
                await _unitOfWork.SaveChangesAsync();

                if (conversation.CreatedByCustomerId == 0 && customer.Id != 0)
                {
                    conversation.CreatedByCustomerId = customer.Id;
                    await _unitOfWork.SaveChangesAsync();
                }

                await transaction.CommitAsync();

                result.ConversationId = conversation.Id;
                if (isNewConversation)
                {
                    result.NewConversationCount++;
                }
                else
                {
                    result.ReplyCount++;
                }
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await transaction.SafeRollbackAsync();
                _unitOfWork.ClearChanges();
                var dupThreads = await _unitOfWork.Threads.FindAsync(t => t.MessageId == message.MessageGuid);
                if (dupThreads.Any())
                {
                    result.IsDuplicate = true;
                    return;
                }
                throw;
            }
            catch (Exception ex)
            {
                await transaction.SafeRollbackAsync();
                _logger.LogError(ex, "iMessage 消息持久化失败：Guid={Guid}, ChatGuid={ChatGuid}",
                    message.MessageGuid, message.ChatGuid);
                throw new InvalidOperationException("iMessage 消息持久化失败", ex);
            }

            // 自动回复只在「新会话」触发，且必须在事务提交之后发：会话没落库就发问候，
            // 入站重试会重复打扰客户。发送失败只记 warn，不改变入站结果（与 Telegram 渠道同一口径）。
            if (isNewConversation && settings.AutoReplyEnabled && !string.IsNullOrWhiteSpace(settings.AutoReplyMessage))
            {
                result.AutoReplyContent = settings.AutoReplyMessage;
                await SendAutoReplyAsync(mode, scopedChannelId, conversation!, settings.AutoReplyMessage!);
            }

            // 入站附件即时转存（BlueBubbles 附件走本机 REST，不存在短 TTL 外链；AMB 的 mmcs 链接 30 天但会失效）。
            if (message.Attachments.Count > 0)
            {
                await DownloadAndAttachMediaAsync(message, mode);
            }

            await TriggerPostIngestionWorkflowsAsync(conversation!.Id);
            await _notificationPublisher.NotifyMailboxAsync(
                conversation.MailboxId.ToString(),
                isNewConversation ? "conversation_created" : "conversation_updated",
                new { conversationId = conversation.Id });

            if (_webhookService != null)
            {
                try
                {
                    await _webhookService.TriggerAsync(
                        isNewConversation ? "conversation_created" : "conversation_updated",
                        conversation.MailboxId,
                        new { conversationId = conversation.Id },
                        "Conversation",
                        conversation.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "iMessage Webhook 触发失败：ConversationId={ConversationId}", conversation.Id);
                }
            }
        }
        finally
        {
            await lockHandle.DisposeAsync();
        }
    }

    /// <summary>
    /// 新会话自动回复（AutoReplyEnabled / AutoReplyMessage 的唯一消费点）。
    /// 出站地址必须与坐席回复用同一套归一化（AMB 取作用域化 ChannelId 里的裸 externalId，
    /// 桥接走 <see cref="IMessageIdentity.BuildReplyGuid"/> 并复用入站预热过的 chatGuid），
    /// 自己拼地址会把问候语发到另一个身份上。
    /// 任何失败（返回 null 或抛异常）只记 warn：自动回复是尽力而为的附加动作，不得影响入站结果。
    /// </summary>
    private async Task SendAutoReplyAsync(
        string mode, string scopedChannelId, Conversation conversation, string autoReplyMessage)
    {
        var destination = IMessageIdentity.IsAmbMode(mode)
            ? IMessageIdentity.ExtractExternalId(scopedChannelId)
            : IMessageIdentity.BuildReplyGuid(scopedChannelId, inboundService: null);

        try
        {
            var sentMessageId = await _client.SendTextMessageAsync(destination, autoReplyMessage);
            if (sentMessageId == null)
            {
                _logger.LogWarning("iMessage 自动回复未发出（客户端返回失败）：ConversationId={ConversationId}, Destination={Destination}",
                    conversation.Id, destination);
                return;
            }

            // 问候语也要落一条线程：否则客服在会话里看不到系统已经回复过。
            // 桥接会把这条回推成 isFromMe=true 的回声并被忽略（见 ProcessIncomingMessageAsync 开头），不会重复入库。
            await _unitOfWork.Threads.AddAsync(new ConversationThread
            {
                ConversationId = conversation.Id,
                From = "imessage@system",
                To = IMessageIdentity.ExtractExternalId(scopedChannelId),
                Subject = string.Empty,
                Body = BuildAutoReplyHtmlBody(autoReplyMessage),
                MessageId = sentMessageId,
                SourceType = SourceType.IMessage,
                Type = ThreadType.Message,
                SourceVia = SourceVia.User,
                CreatedAt = DateTime.UtcNow,
            });

            conversation.LastReplyAt = DateTime.UtcNow;
            conversation.LastReplyFrom = SourceVia.User;
            conversation.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "iMessage 自动回复发送异常：ConversationId={ConversationId}", conversation.Id);
        }
    }

    private static string BuildAutoReplyHtmlBody(string text)
        => "<div class=\"imessage-text imessage-auto-reply\">"
           + HttpUtility.HtmlEncode(text).Replace("\n", "<br>")
           + "</div>";

    private async Task HandleAmbCloseAsync(IMessageParsedEvent parsed, string mode, IMessageIngestionResult result)
    {
        // close 事件带的是原始 sourceId（tel:… / urn:… / 裸 handle），而客户与会话落库时用的
        // ChannelId 是入站消息归一化后的身份（见 ProcessIncomingMessageAsync 里的 BuildScopedChannelId）。
        // 这里必须复用同一套归一化（NormalizeAmbSourceId == 解析器给 message.ExternalId 用的那个方法），
        // 否则两条路径算出的 scopedChannelId 对不上：会话永远关不掉，坐席之后发消息还会撞 410。
        var externalId = IMessageWebhookParser.NormalizeAmbSourceId(parsed.Update?.DisplayName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(externalId))
        {
            result.IgnoredCount++;
            return;
        }

        var scopedChannelId = IMessageIdentity.BuildScopedChannelId(mode, externalId);
        // close 之后不能只靠窗口匹配找会话：用户可能很久没说话，但会话仍未结案。
        var conversations = await _unitOfWork.Conversations.FindAsync(
            c => c.DeletedAt == null
                 && c.SourceType == SourceType.IMessage
                 && c.Customer != null
                 && c.Customer.Channel == ChannelName
                 && c.Customer.ChannelId == scopedChannelId);

        var target = conversations.OrderByDescending(c => c.UpdatedAt).FirstOrDefault();
        if (target == null)
        {
            _logger.LogInformation("iMessage 收到 close，但未找到对应会话：ExternalId={ExternalId}", externalId);
            result.IgnoredCount++;
            return;
        }

        target.Status = ConversationStatus.Closed;
        target.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        result.IsStatus = true;
        result.ConversationId = target.Id;
        _logger.LogInformation("iMessage 会话已由用户关闭（AMB close）：ConversationId={ConversationId}", target.Id);
    }

    private async Task ProcessEventUpdateAsync(IMessageParsedEvent parsed, string mode, IMessageIngestionResult result)
    {
        result.IsStatus = true;
        var update = parsed.Update;
        if (update == null)
        {
            return;
        }

        if (parsed.Kind is IMessageEventKinds.MessageError or IMessageEventKinds.MessageSendError)
        {
            if (!string.IsNullOrWhiteSpace(update.MessageGuid))
            {
                var threads = await _unitOfWork.Threads.FindAsync(t => t.MessageId == update.MessageGuid);
                var thread = threads.FirstOrDefault();
                if (thread != null)
                {
                    result.ConversationId = thread.ConversationId;
                }
            }

            _logger.LogWarning("iMessage 发送/投递失败：Kind={Kind}, MessageGuid={MessageGuid}, ChatGuid={ChatGuid}, Error={Error}",
                parsed.Kind, update.MessageGuid, update.ChatGuid, update.ErrorMessage);
            return;
        }

        _logger.LogDebug("iMessage 状态事件：Kind={Kind}, ChatGuid={ChatGuid}, MessageGuid={MessageGuid}, IsFromMe={IsFromMe}, Typing={Typing}",
            parsed.Kind, update.ChatGuid, update.MessageGuid, update.IsFromMe, update.IsTyping);
    }

    // ------------------------------------------------------------------
    // 建会 / 建线程 / 附件
    // ------------------------------------------------------------------

    private async Task<Conversation> CreateNewConversationAsync(
        Customer customer, int mailboxId, IMessageWebhookMessage message, string? displayName)
    {
        var number = await _unitOfWork.Conversations.GetNextNumberAsync();

        var conversation = new Conversation
        {
            Number = number,
            Subject = BuildConversationSubject(message, displayName),
            // 用导航属性而不是 CustomerId，新客户（Id=0）时 EF Core 才能正确做 FK fixup
            Customer = customer,
            MailboxId = mailboxId,
            SourceType = SourceType.IMessage,
            Type = ConversationType.Chat,
            Status = ConversationStatus.Active,
            Priority = ConversationPriority.Urgent,
            CustomerEmail = string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastReplyAt = DateTime.UtcNow,
            LastReplyFrom = SourceVia.Customer,
            CreatedByCustomerId = customer.Id
        };

        await _unitOfWork.Conversations.AddAsync(conversation);
        await _unitOfWork.SaveChangesAsync();
        return conversation;
    }

    private static ConversationThread CreateThreadFromMessage(
        Conversation conversation, Customer customer, IMessageWebhookMessage message, string? displayName)
    {
        var createdAt = message.DateCreatedUnixMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(message.DateCreatedUnixMs).UtcDateTime
            : DateTime.UtcNow;
        if (createdAt > DateTime.UtcNow)
        {
            createdAt = DateTime.UtcNow;
        }

        return new ConversationThread
        {
            ConversationId = conversation.Id,
            // From：私聊用归一化 handle（人可读），群聊用 chat GUID（没有单一发送方）
            From = message.IsGroup ? (message.ChatGuid ?? message.ExternalId) : message.ExternalId,
            To = displayName ?? string.Empty,
            Subject = string.Empty,
            Body = BuildThreadHtmlBody(message),
            MessageId = message.MessageGuid,
            SourceType = SourceType.IMessage,
            Type = ThreadType.Customer,
            SourceVia = SourceVia.Customer,
            CreatedByCustomerId = customer.Id,
            CreatedAt = createdAt
        };
    }

    private async Task DownloadAndAttachMediaAsync(IMessageWebhookMessage message, string mode)
    {
        // 刚提交的线程：按 MessageId 取回以拿到自增 Id（避免把未追踪对象再传一遍）
        var threads = await _unitOfWork.Threads.FindAsync(t => t.MessageId == message.MessageGuid);
        var thread = threads.FirstOrDefault();
        if (thread == null)
        {
            _logger.LogWarning("iMessage 附件下载跳过：未找到线程 MessageId={MessageId}", message.MessageGuid);
            return;
        }

        var storedCount = 0;
        var hasUnsupportedAttachment = false;

        foreach (var ref0 in message.Attachments)
        {
            try
            {
                // 类型化结果：Success / Unsupported（平台缺解密输入）/ Failed。
                // 旧实现只有"null 即跳过"，而 AMB 分支恒为 null ⇒ 附件被静默丢弃。
                var download = await _client.DownloadAttachmentAsync(ref0, mode);
                if (download.Kind == IMessageAttachmentOutcomeKind.Unsupported)
                {
                    hasUnsupportedAttachment = true;
                    _logger.LogWarning("iMessage 附件因协议输入缺失未保存：Guid={Guid}, Mode={Mode}, Reason={Reason}",
                        ref0.Guid, mode, download.ErrorMessage);
                    continue;
                }

                if (!download.IsSuccess)
                {
                    _logger.LogWarning("iMessage 附件下载失败：Guid={Guid}, Mode={Mode}, Reason={Reason}",
                        ref0.Guid, mode, download.ErrorMessage);
                    continue;
                }

                var data = download.Data!;
                var mimeType = !string.IsNullOrEmpty(download.MimeType)
                    ? download.MimeType
                    : ref0.MimeType;

                var fileName = !string.IsNullOrEmpty(download.Filename)
                    ? download.Filename
                    : !string.IsNullOrEmpty(ref0.Filename)
                        ? ref0.Filename
                        : $"{ref0.Guid ?? message.MessageGuid}.{GetExtensionFromMimeType(mimeType)}";

                var uploadPath = AttachmentPathResolver.ResolveBasePath(
                    _configuration["Attachment:UploadPath"], _env.ContentRootPath);
                var fileDir = DateTime.UtcNow.ToString("yyyy/MM/dd");
                var storedName = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
                var fullDir = Path.Combine(uploadPath, fileDir);

                var fullDirPath = Path.GetFullPath(fullDir);
                var normalizedBase = Path.GetFullPath(uploadPath);
                if (!fullDirPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("iMessage 附件路径遍历拦截：FileDir={FileDir}", fileDir);
                    continue;
                }

                Directory.CreateDirectory(fullDirPath);
                var fullPath = Path.Combine(fullDirPath, storedName);
                await File.WriteAllBytesAsync(fullPath, data);

                var attachment = new Attachment
                {
                    FileName = fileName,
                    StoredFileName = storedName,
                    FileDir = fileDir,
                    Size = data.Length,
                    MimeType = mimeType ?? "application/octet-stream",
                    Type = ClassifyMediaAttachment(mimeType),
                    ThreadId = thread.Id,
                    Embedded = false,
                    CreatedAt = DateTime.UtcNow
                };

                await _unitOfWork.Attachments.AddAsync(attachment);
                await _unitOfWork.SaveChangesAsync();
                storedCount++;
                _logger.LogInformation("iMessage 附件已保存：AttachmentId={AttachmentId}, ThreadId={ThreadId}, FileName={FileName}, Size={Size}",
                    attachment.Id, thread.Id, fileName, data.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "iMessage 附件下载/保存失败：Guid={Guid}, ThreadId={ThreadId}", ref0.Guid, thread.Id);
            }
        }

        // 诚实记账：线程正文在创建时**不**声明附件，等真正落盘后再按实际数量回写。
        // 这样"显示有附件"必然等于"附件已入库"，不会出现点不开的假附件。
        if (message.Attachments.Count > 0)
        {
            thread.Body = BuildThreadHtmlBody(message, storedCount, hasUnsupportedAttachment);
            await _unitOfWork.SaveChangesAsync();
        }
    }

    // ------------------------------------------------------------------
    // 杂项
    // ------------------------------------------------------------------

    private TimeSpan ResolveMatchWindow(IMessageOptions settings)
    {
        // 渠道页可单独覆盖（imessage.match_window_hours）；0/负数则用全局 IngestionMatchOptions。
        // iMessage 是会话制，没有 Meta 那套 24h/48h 窗口，所以默认取较长的一边。
        var hours = settings.MatchWindowHours > 0 ? settings.MatchWindowHours : _matchOptions.IMessageMatchWindowHours;
        if (hours <= 0)
        {
            hours = 168;
        }

        return TimeSpan.FromHours(hours);
    }

    private bool IsAllowedByAllowList(string? allowList, IMessageWebhookMessage message)
    {
        if (string.IsNullOrWhiteSpace(allowList))
        {
            return true;
        }

        var allowed = allowList
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(a => IMessageIdentity.NormalizeHandle(a.Trim()))
            .Where(a => a.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0)
        {
            return true;
        }

        var candidates = new List<string?>
        {
            message.ExternalId,
            message.HandleAddress,
            message.ChatGuid,
            message.ChatIdentifier,
            IMessageIdentity.GetIdentifierFromGuid(message.ChatGuid)
        };

        return candidates.Any(c => !string.IsNullOrWhiteSpace(c) && allowed.Contains(IMessageIdentity.NormalizeHandle(c!)));
    }

    private static string? ResolveDisplayName(IMessageWebhookMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.DisplayName))
        {
            return message.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(message.HandleAddress))
        {
            return message.HandleAddress;
        }

        return message.IsGroup ? message.ChatIdentifier : null;
    }

    private async Task<int> ResolveMailboxIdAsync(IMessageOptions settings)
    {
        if (settings.MailboxId > 0)
        {
            return settings.MailboxId;
        }

        var mailboxes = await _unitOfWork.Mailboxes.FindAsync(m => m.IsActive && m.DeletedAt == null);
        return mailboxes.FirstOrDefault()?.Id ?? 0;
    }

    private async Task TriggerPostIngestionWorkflowsAsync(int conversationId)
    {
        try
        {
            await _workflowService.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerNewConversation);
            await _workflowService.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerNewReply);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "iMessage 入站后工作流执行失败：ConversationId={ConversationId}", conversationId);
        }
    }

    private static string BuildConversationSubject(IMessageWebhookMessage message, string? displayName)
    {
        var who = string.IsNullOrWhiteSpace(displayName) ? "iMessage" : displayName!;
        var preview = Truncate(StripHtmlOrPlain(message.TextBody), 30);
        return string.IsNullOrEmpty(preview) ? $"iMessage: {who}" : $"iMessage: {who} - {preview}";
    }

    private static string BuildThreadHtmlBody(
        IMessageWebhookMessage message, int? attachmentsStored = null, bool hasUnsupportedAttachment = false)
    {
        var builder = new StringBuilder();
        var text = StripHtmlOrPlain(message.TextBody);
        if (!string.IsNullOrEmpty(text))
        {
            builder.Append("<div class=\"imessage-text\">")
                .Append(HttpUtility.HtmlEncode(text).Replace("\n", "<br>"))
                .Append("</div>");
        }

        if (message.IsGroup)
        {
            builder.Append("<div class=\"imessage-group\">群聊会话（chat GUID 保留）</div>");
        }

        if (message.Attachments.Count > 0 && attachmentsStored.HasValue)
        {
            // 只有**真正落盘**的附件才写成"附件 N 个"。
            // 旧实现在入库前就按 message.Attachments.Count 写死摘要，AMB 附件因无法解密被丢弃后，
            // 会话里依然显示"附件 N 个"却点不开 —— 等于对客服谎报附件已收到。
            var total = message.Attachments.Count;
            var stored = Math.Clamp(attachmentsStored.Value, 0, total);
            if (stored == total)
            {
                builder.Append("<div class=\"imessage-attachments\">附件 ").Append(total).Append(" 个</div>");
            }
            else
            {
                builder.Append("<div class=\"imessage-attachments imessage-attachments-missing\">收到附件 ")
                    .Append(total)
                    .Append(" 个，已保存 ")
                    .Append(stored)
                    .Append(" 个");
                if (hasUnsupportedAttachment)
                {
                    builder.Append("（其余附件因平台未提供解密密钥而无法保存）");
                }

                builder.Append("</div>");
            }
        }

        return builder.Length > 0 ? builder.ToString() : "<div class=\"imessage-empty\">(空消息)</div>";
    }

    private static string StripHtmlOrPlain(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Replace("<", "&lt;");
    }

    private static string Truncate(string? value, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLen ? value : value[..maxLen] + "…";
    }

    private static string GetExtensionFromMimeType(string? mimeType) => mimeType switch
    {
        "image/png" => "png",
        "image/jpeg" => "jpg",
        "image/gif" => "gif",
        "image/heic" => "heic",
        "image/webp" => "webp",
        "video/mp4" => "mp4",
        "video/quicktime" => "mov",
        "audio/mpeg" => "mp3",
        "audio/mp4" => "m4a",
        "audio/aac" => "aac",
        "application/pdf" => "pdf",
        _ => "bin"
    };

    private static AttachmentType ClassifyMediaAttachment(string? mimeType)
    {
        var m = (mimeType ?? string.Empty).ToLowerInvariant();
        if (m.StartsWith("image/")) return AttachmentType.Image;
        if (m.StartsWith("video/")) return AttachmentType.Video;
        if (m.StartsWith("audio/")) return AttachmentType.Audio;
        return AttachmentType.Other;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var msg = (ex.InnerException?.Message ?? ex.Message).ToLowerInvariant();
        return msg.Contains("duplicate")
               || msg.Contains("unique")
               || msg.Contains("cannot insert duplicate key")
               || msg.Contains("constraint")
               || msg.Contains("conflict");
    }
}
