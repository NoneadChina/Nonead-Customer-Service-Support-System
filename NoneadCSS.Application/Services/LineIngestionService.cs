using System.Text.Json;
using System.Text.Json.Nodes;
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
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Application.Services;

/// <summary>
/// LINE 入站服务：把已验签的 Webhook 载荷落库为会话/线程，转存入站媒体，并触发自动回复。
/// 关键约束（docs/integration/line.md）：
///   1) 按 message.id 幂等去重（LINE 超时后会重投同一事件）；
///   2) mode=bot 表示频道处于禁用/受限态，禁止任何回复；
///   3) 入站媒体保留期极短，必须在入站路径内下载转存；
///   4) replyToken 存活极短 ⇒ 自动回复直接复用（走 reply 不消耗月度配额）。
/// </summary>
public class LineIngestionService : ILineIngestionService
{
    private const int MaxContactExtractAttempts = 3;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowService _workflowService;
    private readonly ILineSettingsProvider _settingsProvider;
    private readonly ILineClient _client;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ILogger<LineIngestionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly IWebhookService? _webhookService;
    private readonly TimeSpan _conversationMatchWindow;

    public LineIngestionService(
        IUnitOfWork unitOfWork,
        IWorkflowService workflowService,
        ILineSettingsProvider settingsProvider,
        ILineClient client,
        INotificationPublisher notificationPublisher,
        IDistributedLockService distributedLockService,
        ILogger<LineIngestionService> logger,
        IConfiguration configuration,
        IOptions<IngestionMatchOptions> ingestionMatchOptions,
        IHostEnvironment environment,
        IWebhookService? webhookService = null)
    {
        _unitOfWork = unitOfWork;
        _workflowService = workflowService;
        _settingsProvider = settingsProvider;
        _client = client;
        _notificationPublisher = notificationPublisher;
        _distributedLockService = distributedLockService;
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
        _webhookService = webhookService;
        // 会话复用窗口：LINE userId 稳定，48 小时足够跨时段续接（共享配置项见 A15）
        _conversationMatchWindow = TimeSpan.FromHours(ingestionMatchOptions.Value.LineMatchWindowHours);
    }

    public async Task<LineIngestionResult> ProcessWebhookPayloadAsync(string? payload)
    {
        var result = new LineIngestionResult();
        if (string.IsNullOrWhiteSpace(payload) || !LineWebhookParser.IsLineWebhook(payload))
        {
            result.ErrorMessage = "不是 LINE Webhook 载荷";
            return result;
        }

        var events = LineWebhookParser.ParseEvents(payload);
        result.EventCount = events.Count;
        if (events.Count == 0)
        {
            return result;
        }

        var destination = LineWebhookParser.ParseDestination(payload);
        var errors = new List<string>();

        // 单事件失败不影响同批其余事件（LINE 一次可投递多个事件）
        foreach (var evt in events)
        {
            try
            {
                var single = await ProcessEventAsync(evt, destination);
                result.NewConversationCount += single.NewConversationCount;
                result.ReplyCount += single.ReplyCount;
                result.ConversationId ??= single.ConversationId;
                result.AutoReplyContent ??= single.AutoReplyContent;
                if (single.IsDuplicate)
                {
                    result.IsDuplicate = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LINE 事件处理失败 eventType={Type} dedupeKey={Key}", evt.EventType, evt.DedupeKey);
                errors.Add($"{evt.EventType}:{ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            result.ErrorMessage = string.Join(" | ", errors);
        }

        return result;
    }

    public async Task<LineIngestionResult> ProcessEventAsync(LineWebhookEvent evt, string? destination)
    {
        var result = new LineIngestionResult();
        var settings = await _settingsProvider.GetCurrentAsync();

        if (!settings.WebhookEnabled)
        {
            result.ErrorMessage = "LINE webhook 入站在设置中已关闭，事件未处理";
            _logger.LogWarning("LINE webhook 已关闭，忽略事件 {Type}", evt.EventType);
            return result;
        }

        if (string.IsNullOrWhiteSpace(evt.DedupeKey))
        {
            result.ErrorMessage = "事件缺少去重键，已跳过";
            return result;
        }

        // 分布式锁：同一事件并发投递（LINE 重试）时只处理一次
        var lockKey = $"line_ingest:{evt.DedupeKey}";
        var lockHandle = await _distributedLockService.TryAcquireAsync(lockKey, timeoutMs: 0);
        if (lockHandle is null)
        {
            result.IsDuplicate = true;
            return result;
        }

        try
        {
            return await HandleEventCoreAsync(evt, destination, settings, result);
        }
        finally
        {
            await lockHandle.DisposeAsync();
        }
    }

    private async Task<LineIngestionResult> HandleEventCoreAsync(
        LineWebhookEvent evt,
        string? destination,
        LineOptions settings,
        LineIngestionResult result)
    {
        // unsend：客户撤回消息 ⇒ 把对应线程标记删除，不新建会话
        if (evt.EventType == "unsend")
        {
            await MarkUnsentAsync(evt);
            result.ConversationId = null;
            return result;
        }

        // 会话外部标识：单聊用 userId，群/房间用 groupId/roomId（群内发言者仍是 userId）
        var externalId = evt.SourceType == "user" ? evt.UserId : evt.ChatId;
        if (string.IsNullOrWhiteSpace(externalId))
        {
            result.ErrorMessage = $"事件 {evt.EventType} 缺少可用的 userId/chatId";
            return result;
        }

        // follow/unfollow/join/leave/memberJoined/memberLeft/beacon/videoPlayComplete ⇒ 只记录，不建会话
        if (evt.EventType is "follow" or "unfollow" or "join" or "leave"
            or "memberJoined" or "memberLeft" or "beacon" or "videoPlayComplete")
        {
            await AppendEventNoteAsync(evt, externalId!, settings);
            return result;
        }

        if (evt.EventType != "message" && evt.EventType != "postback")
        {
            result.ErrorMessage = $"暂不处理的 LINE 事件类型：{evt.EventType}";
            return result;
        }

        var customer = await _unitOfWork.Customers.GetOrCreateByChannelAsync(LineChannelConst, externalId!, null);
        if (customer is null)
        {
            result.ErrorMessage = "客户创建失败";
            return result;
        }

        if (evt.SourceType == "user" && settings.ProfileEnrichEnabled)
        {
            await TryEnrichProfileAsync(customer.Id, externalId!);
        }

        if (evt.IsCustomerMessage)
        {
            await TryExtractContactAsync(customer.Id, evt.TextBody);
        }

        var mailboxId = await ResolveMailboxIdAsync(settings);
        if (mailboxId == 0)
        {
            // 与 WhatsApp 同口径：无可用邮箱时建档会留下 MailboxId=0 的脏会话，直接判定该事件处理失败
            result.ErrorMessage = "未找到可用邮箱";
            _logger.LogError("LINE 消息处理失败：未找到可用邮箱（配置的 MailboxId={MailboxId}）", settings.MailboxId);
            return result;
        }

        var existing = await _unitOfWork.Conversations.FindActiveLineConversationByUserIdAsync(externalId!, _conversationMatchWindow);

        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        Conversation conversation;
        var isNewConversation = existing is null;
        var now = DateTime.UtcNow;

        if (isNewConversation)
        {
            conversation = await CreateConversationAsync(externalId!, customer, mailboxId, evt, now);
        }
        else
        {
            conversation = existing!;
        }

        // 数据库级去重（锁只防并发，重复投递最终由 MessageId 唯一/查询拦截）
        var duplicated = await _unitOfWork.Threads.FindAsync(t => t.MessageId == evt.DedupeKey && t.State != ThreadState.Deleted);
        if (duplicated.Any())
        {
            await transaction.SafeRollbackAsync();
            _unitOfWork.ClearChanges();
            result.IsDuplicate = true;
            result.ConversationId = conversation.Id;
            return result;
        }

        var thread = await CreateThreadAsync(conversation, customer, evt, externalId!, settings, now);
        conversation.UpdatedAt = thread.CreatedAt > conversation.UpdatedAt ? thread.CreatedAt : now;
        conversation.LastReplyAt = thread.CreatedAt;
        conversation.LastReplyFrom = SourceVia.Customer;

        await _unitOfWork.SaveChangesAsync();

        if (conversation.CreatedByCustomerId is null or 0)
        {
            conversation.CreatedByCustomerId = customer.Id;
            await _unitOfWork.SaveChangesAsync();
        }

        // 入站媒体：LINE 内容地址保留期极短，必须在入站路径内下载转存。
        // 审计 #14：这里只把字节写到「暂存目录」（附件根目录下的 .line-inbound-staging），
        // 真正的落位（移到 yyyy/MM/dd/xxx）挪到事务提交之后 —— 回滚时暂存文件会被删掉，
        // 附件目录里不会再出现「没有对应 Attachment 行」的孤儿文件。
        var stagedMedia = new List<StagedInboundMedia>();
        if (evt.IsCustomerMessage && evt.MessageType is "image" or "video" or "audio" or "file" && !string.IsNullOrWhiteSpace(evt.MessageId))
        {
            await DownloadAndStageMediaAsync(conversation, thread, evt, settings, stagedMedia);
        }

        try
        {
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.SafeRollbackAsync();
            _unitOfWork.ClearChanges();
            // 事务回滚 ⇒ 本轮暂存的字节一并丢弃（附件目录本来就没写过，不会留孤儿文件）
            DiscardStagedMedia(stagedMedia);
            var afterFail = await _unitOfWork.Threads.FindAsync(t => t.MessageId == evt.DedupeKey && t.State != ThreadState.Deleted);
            result.IsDuplicate = afterFail.Any();
            result.ConversationId = conversation.Id;
            if (!result.IsDuplicate)
            {
                throw;
            }

            return result;
        }
        catch
        {
            // 其它提交失败：数据库改动会随事务回滚，暂存文件也必须跟着清掉，否则留下无人引用的字节
            DiscardStagedMedia(stagedMedia);
            throw;
        }

        // 提交成功之后才把暂存文件移到正式附件目录。移动失败不静默：warn 日志给出暂存路径与目标路径，
        // 字节仍留在暂存处（可由运维按日志二次搬运），Attachment 行已提交、内容不会丢。
        await FlushStagedMediaAsync(stagedMedia);

        result.ConversationId = conversation.Id;
        result.ReplyCount = 1;
        if (isNewConversation)
        {
            result.NewConversationCount = 1;
        }

        // 聊天启用（Chat-enabled）功能的已读回执，失败不影响主流程
        if (settings.MarkAsReadEnabled && !string.IsNullOrWhiteSpace(evt.MarkAsReadToken))
        {
            _ = MarkAsReadFireAndForgetAsync(evt.MarkAsReadToken!);
        }

        await TriggerWorkflowsAsync(conversation.Id, isNewConversation);
        await NotifyAsync(conversation, isNewConversation);

        // 机器人自动回复：仅新会话，且频道非 bot 模式、无坐席接管
        if (isNewConversation && settings.AutoReplyEnabled && !string.IsNullOrWhiteSpace(settings.AutoReplyMessage)
            && !evt.IsReplyForbidden && evt.IsCustomerMessage)
        {
            if (await IsHumanTakeoverActiveAsync(conversation.Id))
            {
                _logger.LogInformation("LINE 会话处于人工接管，跳过自动回复 conversationId={Id}", conversation.Id);
            }
            else
            {
                result.AutoReplyContent = await SendAutoReplyAsync(conversation, customer.Id, evt, settings, thread);
            }
        }

        if (evt.IsReplyForbidden)
        {
            result.SkippedStandbyCount = 1;
            _logger.LogWarning("LINE 事件 mode=bot（频道禁用/受限），不回复 conversationId={Id} dedupeKey={Key}", conversation.Id, evt.DedupeKey);
        }

        if (!string.IsNullOrWhiteSpace(destination))
        {
            _logger.LogDebug("LINE 事件已入库 destination={Destination} 与频道 ChannelId 匹配={Match}",
                destination,
                string.Equals(destination, settings.ChannelId, StringComparison.OrdinalIgnoreCase));
        }

        return result;
    }

    // ── 建档 ────────────────────────────────────────────────────────────────────

    private async Task<Conversation> CreateConversationAsync(
        string externalId,
        Customer customer,
        int mailboxId,
        LineWebhookEvent evt,
        DateTime now)
    {
        var number = await _unitOfWork.Conversations.GetNextNumberAsync();
        var conversation = new Conversation
        {
            Number = number,
            Subject = BuildConversationSubject(customer, evt),
            Customer = customer, // 用导航属性而非 FK：新建客户 Id 仍为 0，交给 EF fixup
            MailboxId = mailboxId,
            SourceType = SourceType.Line,
            Type = ConversationType.Chat,
            Status = ConversationStatus.Active,
            Priority = ConversationPriority.Urgent,
            CustomerEmail = customer.GetMainEmail() ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
            LastReplyAt = now,
            LastReplyFrom = SourceVia.Customer,
            CreatedByCustomerId = customer.Id,
        };

        await _unitOfWork.Conversations.AddAsync(conversation);
        await _unitOfWork.SaveChangesAsync();
        return conversation;
    }

    private async Task<ConversationThread> CreateThreadAsync(
        Conversation conversation,
        Customer customer,
        LineWebhookEvent evt,
        string externalId,
        LineOptions settings,
        DateTime now)
    {
        var createdAt = evt.Timestamp > 0
            ? Min(ToUtc(evt.Timestamp), now)
            : now;

        var headers = new Dictionary<string, string?>
        {
            ["lineDestination"] = settings.ChannelId,
            ["lineSourceType"] = evt.SourceType,
            ["lineMessageId"] = evt.MessageId,
            ["lineMode"] = evt.Mode,
            ["lineReplyTokenCreatedAtUtc"] = createdAt.ToString("O"),
        };

        if (!string.IsNullOrWhiteSpace(evt.ChatId))
        {
            headers["lineChatId"] = evt.ChatId;
        }

        if (!string.IsNullOrWhiteSpace(evt.ReplyToken))
        {
            // 一次性令牌：坐席/自动回复在 TTL 内可复用（不消耗月度配额）
            headers["replyToken"] = evt.ReplyToken;
        }

        if (!string.IsNullOrWhiteSpace(evt.MarkAsReadToken))
        {
            headers["markAsReadToken"] = evt.MarkAsReadToken;
        }

        var thread = new ConversationThread
        {
            ConversationId = conversation.Id,
            CustomerId = customer.Id,
            Type = evt.EventType == "postback" ? ThreadType.Customer : ThreadType.Customer,
            SourceVia = SourceVia.Customer,
            SourceType = SourceType.Line,
            From = externalId,
            To = settings.ChannelId ?? string.Empty,
            Subject = string.Empty,
            Body = BuildThreadHtmlBody(evt),
            MessageId = evt.DedupeKey,
            Headers = JsonSerializer.Serialize(headers),
            IsFirst = false,
            CreatedByCustomerId = customer.Id,
            CreatedAt = createdAt,
            UpdatedAt = now,
        };

        await _unitOfWork.Threads.AddAsync(thread);
        return thread;
    }

    private async Task AppendEventNoteAsync(LineWebhookEvent evt, string externalId, LineOptions settings)
    {
        // 关注/退关/入群等只写内部备注，避免污染客户可见的时间线
        var conversation = await _unitOfWork.Conversations.FindActiveLineConversationByUserIdAsync(externalId, _conversationMatchWindow);
        if (conversation is null)
        {
            return;
        }

        var now = DateTime.UtcNow;

        // LINE 会对同一次投递重发（webhook 返回非 2xx 时重试），事件备注若不按 DedupeKey 去重，
        // 会话里就会出现多条「客户已添加机器人为好友」之类的重复备注。
        if (!string.IsNullOrWhiteSpace(evt.DedupeKey))
        {
            var existingNotes = await _unitOfWork.Threads.FindAsync(
                t => t.MessageId == evt.DedupeKey && t.State != ThreadState.Deleted);
            var existingNote = existingNotes.FirstOrDefault();
            if (existingNote != null)
            {
                _logger.LogDebug("LINE 事件备注已存在，跳过重复写入：DedupeKey={Key}, ThreadId={ThreadId}",
                    evt.DedupeKey, existingNote.Id);
                return;
            }
        }

        var description = evt.EventType switch
        {
            "follow" => "客户已添加机器人为好友（此后 push 可达）",
            "unfollow" => "客户已取消添加机器人为好友（push 将不可达）",
            "join" => "机器人已加入群/房间",
            "leave" => "机器人已离开群/房间",
            "memberJoined" => "有成员加入群/房间",
            "memberLeft" => "有成员退出群/房间",
            "beacon" => "收到 LINE Beacon 事件",
            "videoPlayComplete" => "客户已完整观看视频消息",
            _ => evt.EventType,
        };

        await _unitOfWork.Threads.AddAsync(new ConversationThread
        {
            ConversationId = conversation.Id,
            Type = ThreadType.Note,
            IsInternal = true,
            SourceType = SourceType.Line,
            SourceVia = SourceVia.Customer,
            From = externalId,
            To = settings.ChannelId ?? string.Empty,
            Subject = $"LINE {evt.EventType}",
            Body = $"<div class=\"line-event line-{evt.EventType}\">{HttpUtility.HtmlEncode(description)}</div>",
            MessageId = evt.DedupeKey,
            CreatedAt = evt.Timestamp > 0 ? Min(ToUtc(evt.Timestamp), now) : now,
            UpdatedAt = now,
        });

        conversation.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync();
    }

    private async Task MarkUnsentAsync(LineWebhookEvent evt)
    {
        if (evt.UnsentMessageIds.Count == 0)
        {
            return;
        }

        var affected = 0;
        foreach (var messageId in evt.UnsentMessageIds)
        {
            var threads = await _unitOfWork.Threads.FindAsync(t => t.SourceType == SourceType.Line
                && t.MessageId == messageId
                && t.State != ThreadState.Deleted);
            foreach (var thread in threads)
            {
                thread.MarkAsDeleted();
                thread.Body = $"<div class=\"line-unsend\">客户已撤回该消息</div>{thread.Body}";
                affected++;
            }
        }

        if (affected > 0)
        {
            await _unitOfWork.SaveChangesAsync();
        }
    }

    // ── 媒体 ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 审计 #14：入站附件的「已下载但尚未落位」状态。字节先写到暂存目录（附件根下的
    /// .line-inbound-staging/&lt;yyyy/MM/dd&gt;/），事务提交成功后才移到正式附件目录。
    /// </summary>
    private sealed class StagedInboundMedia
    {
        public required string TempPath { get; init; }
        public required string TargetPath { get; init; }
        public required string MessageId { get; init; }
        public required string StoredFileName { get; init; }
        public int ThreadId { get; init; }
    }

    /// <summary>
    /// 下载入站媒体并写入暂存目录（不写正式附件目录）。Attachment 行照常在本事务内建立 ——
    /// 行的 FileDir/StoredFileName 指向"提交后必然存在"的最终路径，提交失败时本方法留下的
    /// 暂存文件会被 DiscardStagedMedia 删除，磁盘上不会留下没有数据库行的孤儿文件。
    /// </summary>
    private async Task DownloadAndStageMediaAsync(Conversation conversation, ConversationThread thread, LineWebhookEvent evt, LineOptions settings, List<StagedInboundMedia> staged)
    {
        var content = await _client.DownloadContentAsync(evt.MessageId!);
        if (content is null)
        {
            _logger.LogWarning("LINE 入站媒体下载失败 messageId={Id} type={Type}", evt.MessageId, evt.MessageType);
            return;
        }

        if (content.TooLarge)
        {
            _logger.LogWarning("LINE 入站媒体超过上限，未转存 messageId={Id}（上限 {Max} 字节）", evt.MessageId, settings.MaxInboundMediaBytes);
            return;
        }

        var mimeType = content.MimeType ?? DefaultMimeType(evt.MessageType);
        var extension = GetExtensionFromMimeType(mimeType) ?? ExtensionForMessageType(evt.MessageType);
        var dayFolder = DateTime.UtcNow.ToString("yyyy/MM/dd");
        var basePath = AttachmentPathResolver.ResolveBasePath(_configuration["Attachment:UploadPath"], _environment.ContentRootPath);

        var storedName = $"{Guid.NewGuid():N}{extension}";
        // 暂存目录与正式目录同根（同一卷），提交后的移动是同卷 rename，代价恒定
        var tempDir = Path.Combine(basePath, StagingDirectoryName, dayFolder);
        var targetDir = Path.Combine(basePath, dayFolder);
        Directory.CreateDirectory(tempDir);

        var tempPath = Path.Combine(tempDir, storedName);
        await File.WriteAllBytesAsync(tempPath, content.Content);

        var originalName = !string.IsNullOrWhiteSpace(content.FileName)
            ? content.FileName!
            : evt.FileName is { Length: > 0 } ? evt.FileName : $"{evt.MessageType}-{evt.MessageId}{extension}";

        var attachment = new Attachment
        {
            FileName = originalName,
            StoredFileName = storedName,
            FileDir = dayFolder,
            Size = content.Content.LongLength,
            MimeType = mimeType,
            Type = ClassifyMediaAttachment(mimeType, evt.MessageType),
            ThreadId = thread.Id,
            Embedded = false,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Attachments.AddAsync(attachment);
        thread.HasAttachments = true;
        await _unitOfWork.SaveChangesAsync();

        staged.Add(new StagedInboundMedia
        {
            TempPath = tempPath,
            TargetPath = Path.Combine(targetDir, storedName),
            MessageId = evt.MessageId!,
            StoredFileName = storedName,
            ThreadId = thread.Id,
        });
    }

    /// <summary>暂存目录名（点前缀，附件列表/清理任务都不应把它当日期目录）</summary>
    internal const string StagingDirectoryName = ".line-inbound-staging";

    /// <summary>
    /// 提交失败（含唯一键冲突回落）时丢弃本轮暂存字节。删除失败不掩盖：留 warn 日志说明
    /// 暂存文件仍在原地，可人工回收 —— 但正式附件目录始终干净。
    /// </summary>
    private void DiscardStagedMedia(List<StagedInboundMedia> staged)
    {
        foreach (var item in staged)
        {
            try
            {
                if (File.Exists(item.TempPath))
                {
                    File.Delete(item.TempPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LINE 入站媒体暂存文件清理失败（正式附件目录未被写入，可人工回收）temp={Temp} messageId={Id}", item.TempPath, item.MessageId);
            }
        }

        staged.Clear();
    }

    /// <summary>
    /// 事务提交后把暂存文件移到正式附件目录。失败语义：数据库改动已经生效，因此不抛异常、
    /// 不静默 —— 留 warn 日志（给出暂存路径与目标路径），字节保留在暂存目录供二次搬运，
    /// Attachment 行仍可被运维据此定位并补文件。
    /// </summary>
    private Task FlushStagedMediaAsync(List<StagedInboundMedia> staged)
    {
        foreach (var item in staged)
        {
            try
            {
                var targetDir = Path.GetDirectoryName(item.TargetPath);
                if (!string.IsNullOrWhiteSpace(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Move(item.TempPath, item.TargetPath, overwrite: false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "LINE 入站媒体已入库但落位失败：文件仍在暂存目录，可按日志搬运回附件目录 thread={Thread} messageId={Id} temp={Temp} target={Target}",
                    item.ThreadId, item.MessageId, item.TempPath, item.TargetPath);
            }
        }

        staged.Clear();
        return Task.CompletedTask; // 落位是同卷 rename，无需异步；保留 Task 让调用方统一 await
    }

    // ── 自动回复 / 接管 ─────────────────────────────────────────────────────────

    private async Task<string?> SendAutoReplyAsync(Conversation conversation, int customerId, LineWebhookEvent evt, LineOptions settings, ConversationThread customerThread)
    {
        var message = settings.AutoReplyMessage ?? string.Empty;

        // 优先 reply（复用刚收到的 replyToken，不消耗月度配额）；LINE 无会话接管 API ⇒ 只能系统内静音
        var send = await _client.SendTextAsync(message, evt.ReplyToken, evt.UserId);
        var now = DateTime.UtcNow.AddSeconds(1); // 保证晚于 conversation.UpdatedAt，排序稳定

        // replyToken 是一次性的：自动回复用掉后必须落标记。否则在 TTL 内坐席回复会重放同一 token，
        // LINE 返回 409，再回落 push 白耗一次月度配额（LineService 也按这个标记判断是否可走 reply）。
        // 判据是"这次尝试过 token"而不是"最终经 reply 投递"：客户端回落 push 意味着 token 已被确定性拒绝
        // （过期/无效），同样不可再用，漏标记会让坐席再吃一次 409 + 一次无谓重试。
        if (send.Success && !string.IsNullOrWhiteSpace(evt.ReplyToken))
        {
            MarkReplyTokenUsedOnThread(customerThread, now);
        }

        await _unitOfWork.Threads.AddAsync(new ConversationThread
        {
            ConversationId = conversation.Id,
            CustomerId = customerId,
            Type = ThreadType.Message,
            SourceVia = SourceVia.User,
            SourceType = SourceType.Line,
            IsInternal = false,
            From = string.IsNullOrWhiteSpace(settings.ChannelId) ? "line@system" : settings.ChannelId!,
            To = evt.UserId ?? string.Empty,
            Subject = string.Empty,
            Body = $"<div class=\"line-text\">{HttpUtility.HtmlEncode(message).Replace("\n", "<br>")}</div>",
            MessageId = $"line-auto-reply-{Guid.NewGuid():N}",
            SendStatus = send.Success ? SendStatus.Sent : SendStatus.Failed,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _unitOfWork.SaveChangesAsync();

        if (!send.Success)
        {
            _logger.LogWarning("LINE 自动回复失败 conversationId={Id}：{Error}", conversation.Id, send.ErrorMessage);
            return null;
        }

        return message;
    }

    /// <summary>
    /// 在入站线程的 Headers 上标记 replyToken 已被自动回复消耗。
    /// 与 LineService.MarkReplyTokenUsedAsync 写同一个属性名（replyTokenUsed），
    /// 否则 TTL 内坐席回复会重放已失效的一次性令牌（409），再回落 push 白耗一次配额。
    /// </summary>
    private static void MarkReplyTokenUsedOnThread(ConversationThread thread, DateTime now)
    {
        JsonObject obj;
        if (string.IsNullOrWhiteSpace(thread.Headers))
        {
            obj = new JsonObject();
        }
        else
        {
            try
            {
                obj = JsonNode.Parse(thread.Headers) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                obj = new JsonObject();
            }
        }

        obj["replyTokenUsed"] = true;
        thread.Headers = obj.ToJsonString();
        thread.UpdatedAt = now;
    }

    /// <summary>人工接管判定：读最近一条 line-takeover- 备注线程的 untilUtc（无需新增表字段）</summary>
    private async Task<bool> IsHumanTakeoverActiveAsync(int conversationId)
    {
        var notes = await _unitOfWork.Threads.FindAsync(t => t.ConversationId == conversationId
            && t.MessageId.StartsWith(LineService.TakeoverThreadPrefix)
            && t.State != ThreadState.Deleted);

        var latest = notes.OrderByDescending(t => t.CreatedAt).FirstOrDefault();
        if (latest is null || string.IsNullOrWhiteSpace(latest.Headers))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(latest.Headers);
            if (doc.RootElement.TryGetProperty("untilUtc", out var untilElement)
                && untilElement.ValueKind == JsonValueKind.String
                && DateTime.TryParse(untilElement.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var until))
            {
                return until > DateTime.UtcNow;
            }
        }
        catch (JsonException)
        {
            // 备注头损坏 ⇒ 保守视为已过期，不阻塞机器人
        }

        return false;
    }

    private async Task MarkAsReadFireAndForgetAsync(string markAsReadToken)
    {
        try
        {
            await _client.MarkAsReadAsync(markAsReadToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LINE markAsRead 调用失败（不影响主流程）");
        }
    }

    // ── 客户资料 / 联系人 ───────────────────────────────────────────────────────

    private async Task TryEnrichProfileAsync(int customerId, string userId)
    {
        var profile = await _client.GetProfileAsync(userId);
        if (profile is null || string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            // 404 ⇒ 非好友（或已屏蔽）；只记 debug，不产生噪声日志
            _logger.LogDebug("LINE profile 不可得（非好友/已屏蔽）userId={UserId}", userId);
            return;
        }

        var customer = _unitOfWork.Customers.FindTracked(customerId);
        if (customer is null)
        {
            return;
        }

        var changed = false;
        if (string.IsNullOrWhiteSpace(customer.FirstName) && string.IsNullOrWhiteSpace(customer.LastName))
        {
            var (lastName, firstName) = ChineseNameSplitter.Split(profile.DisplayName);
            customer.LastName = lastName;
            customer.FirstName = firstName;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(customer.Avatar) && !string.IsNullOrWhiteSpace(profile.PictureUrl))
        {
            customer.Avatar = profile.PictureUrl;
            changed = true;
        }

        if (changed)
        {
            customer.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
        }
    }

    private async Task TryExtractContactAsync(int customerId, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var customer = _unitOfWork.Customers.FindTracked(customerId);
        if (customer is null || customer.ContactExtractAttempts >= MaxContactExtractAttempts)
        {
            return;
        }

        customer.ContactExtractAttempts++;

        var info = ContactInfoExtractor.Extract(text, _logger);
        if (!info.HasAny)
        {
            await _unitOfWork.SaveChangesAsync();
            return;
        }

        if (!string.IsNullOrWhiteSpace(info.Name) && string.IsNullOrWhiteSpace(customer.FirstName) && string.IsNullOrWhiteSpace(customer.LastName))
        {
            var (lastName, firstName) = ChineseNameSplitter.Split(info.Name!);
            customer.LastName = lastName;
            customer.FirstName = firstName;
        }

        if (!string.IsNullOrWhiteSpace(info.Phone) && !customer.GetPhones().Any(p => p.Value == info.Phone))
        {
            var phones = customer.GetPhones();
            phones.Add(new CustomerPhone { Value = info.Phone!, Type = 1, N = "LINE" });
            customer.SetPhones(phones);
        }

        if (!string.IsNullOrWhiteSpace(info.Email)
            && !customer.Emails.Any(e => string.Equals(e.EmailAddress, info.Email, StringComparison.OrdinalIgnoreCase))
            && !await _unitOfWork.Customers.IsEmailOwnedByAnyCustomerAsync(info.Email!, customer.Id))
        {
            customer.Emails.Add(new Email { EmailAddress = info.Email!, Type = 1, CustomerId = customer.Id });
        }

        customer.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();
    }

    private async Task<int> ResolveMailboxIdAsync(LineOptions settings)
    {
        if (settings.MailboxId is > 0)
        {
            return settings.MailboxId.Value;
        }

        var mailboxes = await _unitOfWork.Mailboxes.FindAsync(m => m.IsActive && m.DeletedAt == null);
        return mailboxes.FirstOrDefault()?.Id ?? 0;
    }

    // ── 后置动作 ────────────────────────────────────────────────────────────────

    private async Task TriggerWorkflowsAsync(int conversationId, bool isNewConversation)
    {
        try
        {
            if (isNewConversation)
            {
                await _workflowService.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerNewConversation);
            }

            await _workflowService.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerNewReply);
        }
        catch (Exception ex)
        {
            // 自动化失败不能影响入站（Webhook 已回 200）
            _logger.LogWarning(ex, "LINE 自动化触发失败 conversationId={Id}", conversationId);
        }
    }

    private async Task NotifyAsync(Conversation conversation, bool isNewConversation)
    {
        try
        {
            await _notificationPublisher.NotifyMailboxAsync(conversation.MailboxId.ToString(), "conversation_updated",
                new { conversationId = conversation.Id });

            if (_webhookService is not null)
            {
                await _webhookService.TriggerAsync(isNewConversation ? "conversation_created" : "conversation_updated",
                    conversation.MailboxId, new { conversationId = conversation.Id }, "Conversation", conversation.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LINE 入站通知失败 conversationId={Id}", conversation.Id);
        }
    }

    // ── 文案 / 工具 ─────────────────────────────────────────────────────────────

    private static string BuildConversationSubject(Customer customer, LineWebhookEvent evt)
    {
        var name = customer.GetFullName();
        var who = string.IsNullOrWhiteSpace(name) ? $"LINE {Truncate(evt.SourceType == "user" ? evt.UserId : evt.ChatId, 12)}" : name;
        var prefix = evt.SourceType switch
        {
            "group" => "LINE 群聊",
            "room" => "LINE 房间",
            _ => "LINE 客户",
        };

        var snippet = evt.EventType switch
        {
            "message" when evt.MessageType == "text" => Truncate(evt.TextBody, 40),
            "message" => $"[{evt.MessageType}] 消息",
            "postback" => Truncate(evt.PostbackData, 40),
            _ => string.Empty,
        };

        return string.IsNullOrWhiteSpace(snippet) ? prefix : $"{prefix}：{snippet}";
    }

    private static string BuildThreadHtmlBody(LineWebhookEvent evt)
    {
        switch (evt.EventType)
        {
            case "postback":
                return $"<div class=\"line-postback\" data-data=\"{HttpUtility.HtmlAttributeEncode(evt.PostbackData ?? string.Empty)}\">客户点击了菜单/按钮：{HttpUtility.HtmlEncode(evt.PostbackData ?? string.Empty)}</div>";
            case "message" when evt.MessageType == "text":
                return $"<div class=\"line-text\">{HttpUtility.HtmlEncode(evt.TextBody ?? string.Empty).Replace("\n", "<br>")}</div>";
            case "message" when evt.MessageType == "location":
                return $"<div class=\"line-location\" data-lat=\"{evt.Latitude?.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" data-lng=\"{evt.Longitude?.ToString(System.Globalization.CultureInfo.InvariantCulture)}\">位置：{HttpUtility.HtmlEncode(evt.LocationAddress ?? string.Empty)}</div>";
            case "message" when evt.MessageType is "image" or "video" or "audio" or "file":
                return BuildMediaPlaceholder(evt);
            case "message":
                return $"<div class=\"line-unknown\">[{HttpUtility.HtmlEncode(evt.MessageType)} 消息]</div>";
            default:
                return $"<div class=\"line-event\">{HttpUtility.HtmlEncode(evt.EventType)}</div>";
        }
    }

    private static string BuildMediaPlaceholder(LineWebhookEvent evt)
    {
        var label = evt.MessageType switch
        {
            "image" => "图片",
            "video" => "视频",
            "audio" => "语音",
            "file" => "文件",
            _ => evt.MessageType,
        };

        var name = string.IsNullOrWhiteSpace(evt.FileName) ? string.Empty : $"：{HttpUtility.HtmlEncode(evt.FileName)}";
        return $"<div class=\"line-media line-{evt.MessageType}\">{label}{name}</div>";
    }

    private static AttachmentType ClassifyMediaAttachment(string mimeType, string messageType) => messageType switch
    {
        "image" => AttachmentType.Image,
        "audio" => AttachmentType.Audio,
        "video" => AttachmentType.Video,
        _ when mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => AttachmentType.Image,
        _ when mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => AttachmentType.Audio,
        _ when mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => AttachmentType.Video,
        _ => AttachmentType.Other,
    };

    private static string DefaultMimeType(string messageType) => messageType switch
    {
        "image" => "image/jpeg",
        "video" => "video/mp4",
        "audio" => "audio/mpeg",
        "file" => "application/octet-stream",
        _ => "application/octet-stream",
    };

    private static string? GetExtensionFromMimeType(string? mimeType) => mimeType?.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/gif" => ".gif",
        "video/mp4" => ".mp4",
        "audio/mpeg" => ".mp3",
        "audio/mp4" => ".m4a",
        "application/pdf" => ".pdf",
        _ => null,
    };

    private static string ExtensionForMessageType(string messageType) => messageType switch
    {
        "image" => ".jpg",
        "video" => ".mp4",
        "audio" => ".m4a",
        _ => ".bin",
    };

    private static DateTime ToUtc(long unixMillis) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMillis).UtcDateTime;

    private static DateTime Min(DateTime a, DateTime b) => a <= b ? a : b;

    private static string Truncate(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLen ? value : value[..maxLen] + "…";
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var message = (ex.InnerException?.Message ?? ex.Message).ToLowerInvariant();
        return message.Contains("duplicate") || message.Contains("unique")
            || message.Contains("cannot insert duplicate key") || message.Contains("constraint") || message.Contains("conflict");
    }

    internal const string LineChannelConst = "line";
}
