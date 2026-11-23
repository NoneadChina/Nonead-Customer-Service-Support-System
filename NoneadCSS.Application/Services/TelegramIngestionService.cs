using System.Text;
using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
/// Telegram 入站消息接入服务 —— 处理 Webhook 推送与轮询拉取到的 Update。
/// 去重(chat.id+message_id) → 客户匹配(Customers.Channel=\"telegram\", ChannelId=chat.id)
///   → 会话匹配(默认 30 天窗口) → 事务持久化 → 媒体即时下载 → 触发工作流/通知/Webhook
/// 注意：Telegram 无 24 小时回复窗口，但用户可能拉黑 bot（403）——入站即代表可回复，
/// 因此入站成功时会清除该会话的“不可达”标记。
/// </summary>
public class TelegramIngestionService : ITelegramIngestionService
{
    /// <summary>托管 Bot API 的 getFile 下载上限 20 MB（自建 Local Bot API Server 无此限制）</summary>
    private const long MaxDownloadBytes = 20L * 1024 * 1024;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowService _workflowService;
    private readonly ITelegramSettingsProvider _settingsProvider;
    private readonly ITelegramClient _telegramClient;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ILogger<TelegramIngestionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _env;
    private readonly IWebhookService? _webhookService;

    public TelegramIngestionService(
        IUnitOfWork unitOfWork,
        IWorkflowService workflowService,
        ITelegramSettingsProvider settingsProvider,
        ITelegramClient telegramClient,
        INotificationPublisher notificationPublisher,
        IDistributedLockService distributedLockService,
        ILogger<TelegramIngestionService> logger,
        IConfiguration configuration,
        IHostEnvironment env,
        IWebhookService? webhookService = null)
    {
        _unitOfWork = unitOfWork;
        _workflowService = workflowService;
        _settingsProvider = settingsProvider;
        _telegramClient = telegramClient;
        _notificationPublisher = notificationPublisher;
        _distributedLockService = distributedLockService;
        _logger = logger;
        _configuration = configuration;
        _env = env;
        _webhookService = webhookService;
    }

    /// <inheritdoc />
    public async Task<TelegramIngestionResult> ProcessWebhookPayloadAsync(string? payload)
    {
        var result = new TelegramIngestionResult();
        var settings = _settingsProvider.Current;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return result;
        }

        if (!settings.Enabled)
        {
            result.ErrorMessage = "Telegram 模块未启用";
            return result;
        }

        if (!TelegramWebhookParser.IsTelegramUpdate(payload))
        {
            result.ErrorMessage = "载荷不是 Telegram Update";
            return result;
        }

        TelegramWebhookMessage update;
        try
        {
            update = TelegramWebhookParser.ParseUpdate(payload)!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析 Telegram Update 失败（载荷长度 {Length}）", payload.Length);
            result.ErrorMessage = "Update 解析失败";
            return result;
        }

        return await ProcessUpdateAsync(update, result);
    }

    /// <inheritdoc />
    public async Task<TelegramPollResult> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsProvider.Current;
        var pollResult = new TelegramPollResult();

        if (!settings.Enabled || !settings.IsConfigured)
        {
            pollResult.ErrorMessage = "Telegram 未启用或未配置";
            return pollResult;
        }

        if (!settings.IsPollingMode)
        {
            pollResult.ErrorMessage = $"当前模式为 {settings.Mode}，轮询未启用";
            return pollResult;
        }

        var offset = await GetPollingOffsetAsync();
        var limit = Math.Clamp(settings.PollingLimit <= 0 ? 100 : settings.PollingLimit, 1, 100);
        var timeout = Math.Clamp(settings.PollingTimeout, 0, 50);

        var page = await _telegramClient.GetUpdatesAsync(offset, limit, timeout, settings.AllowedUpdates, cancellationToken);
        pollResult.Ok = page.Ok;
        pollResult.WebhookConflict = page.WebhookConflict;
        pollResult.InvalidToken = page.InvalidToken;
        pollResult.RetryAfterSeconds = page.RetryAfterSeconds;
        pollResult.ErrorMessage = page.ErrorMessage;
        pollResult.ReceivedCount = page.Updates.Count;

        if (!page.Ok)
        {
            return pollResult;
        }

        var offsetBlocked = false;
        foreach (var update in page.Updates)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // 每条 Update 单独持有一个结果对象：游标是否推进，只能看这条 Update 自己的处理结论
            var updateResult = new TelegramIngestionResult();
            var threw = false;

            try
            {
                await ProcessUpdateAsync(update, updateResult);
                pollResult.ProcessedCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                threw = true;
                // 异常说明这条 Update 还没处理完：游标不能前进，否则下一轮 getUpdates 拿不到它，消息就永久丢了
                _logger.LogWarning(ex, "处理 Telegram Update {UpdateId} 异常，游标不前进，下次轮询重试", update.UpdateId);
            }

            // 推进条件：没有抛异常，且「已成功入库」或「明确跳过」（非消息类 / 重复投递 / 数据本身不合法）。
            // 失败分支（客户创建失败、未找到可用邮箱、消息持久化失败）都会带 ErrorMessage 且不属于跳过 ⇒ 保持游标重试。
            var skipped = updateResult.IsNonMessage || updateResult.IsDuplicate;
            if (threw || (!skipped && !string.IsNullOrEmpty(updateResult.ErrorMessage)))
            {
                // 一旦有一条失败，本批后续成功的 Update 也不能抬高游标（否则会跨过失败的那条），
                // 它们下一轮会被重复投递，由 chat.id+message_id 去重兜底。
                offsetBlocked = true;

                if (!threw)
                {
                    _logger.LogWarning("Telegram Update {UpdateId} 未处理成功（{Error}），游标不前进，下次轮询重试",
                        update.UpdateId, updateResult.ErrorMessage);
                }

                continue;
            }

            if (!offsetBlocked && update.UpdateId >= pollResult.NewOffset)
            {
                pollResult.NewOffset = update.UpdateId + 1;
            }
        }

        if (pollResult.NewOffset > 0)
        {
            await SavePollingOffsetAsync(pollResult.NewOffset);
        }

        return pollResult;
    }

    /// <inheritdoc />
    public async Task<long> GetPollingOffsetAsync()
    {
        try
        {
            var raw = await _unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.PollingOffsetCursor);
            return long.TryParse(raw, out var offset) && offset > 0 ? offset : 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取 Telegram 轮询游标失败，按 0 重新拉取");
            return 0;
        }
    }

    private async Task SavePollingOffsetAsync(long nextOffset)
    {
        try
        {
            await _unitOfWork.SystemOptions.SetAsync(
                TelegramOptionKeys.PollingOffsetCursor,
                nextOffset.ToString(),
                TelegramOptionKeys.Category);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存 Telegram 轮询游标 {Offset} 失败", nextOffset);
        }
    }

    private async Task<TelegramIngestionResult> ProcessUpdateAsync(TelegramWebhookMessage update, TelegramIngestionResult result)
    {
        var settings = _settingsProvider.Current;

        // callback_query：必须 answerCallbackQuery（id 仅 10 秒有效），不产生会话线程
        if (update.UpdateKind == "callback_query")
        {
            result.IsNonMessage = true;
            if (!string.IsNullOrEmpty(update.CallbackQueryId))
            {
                try
                {
                    await _telegramClient.AnswerCallbackQueryAsync(update.CallbackQueryId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "answerCallbackQuery 失败（不影响会话数据）");
                }
            }

            return result;
        }

        // 非消息类 Update（my_chat_member / chat_member / inline_query / poll_answer 等）
        // 说明：`business_message` / `edited_business_message`（Telegram Business 账号转发的消息）虽然
        // 解析器能解出 Message 对象，但回复必须带 `business_connection_id`，本渠道尚未实现该参数，
        // 收进来只会得到"能看不能回"的会话 ⇒ 这里明确不入库，并留下 Warn 级日志，避免像以前那样静默丢弃。
        if (update.UpdateKind is not ("message" or "edited_message" or "channel_post" or "edited_channel_post"))
        {
            if (update.UpdateKind is "business_message" or "edited_business_message")
            {
                _logger.LogWarning(
                    "收到 Telegram Business 消息（updateKind={Kind}）但回复需 business_connection_id，暂不入库；如需支持请先在发送链路补齐该参数",
                    update.UpdateKind);
            }
            else
            {
                _logger.LogDebug("跳过非消息类 Telegram Update（updateKind={Kind}）", update.UpdateKind);
            }

            result.IsNonMessage = true;
            return result;
        }

        if (update.ChatId == 0 || update.MessageId == 0)
        {
            result.IsNonMessage = true;
            result.ErrorMessage = "Update 缺少 chat.id 或 message_id";
            return result;
        }

        // bot 自身消息（含客服在客户端里发的回声）与群服务消息不入库
        if (update.IsBotSender || update.MessageType == "service")
        {
            result.IsNonMessage = true;
            return result;
        }

        // 群消息默认不接入（privacy mode 下也收不到普通群消息），AcceptGroupMessages=true 时接入
        if (update.IsGroupMessage && !settings.AcceptGroupMessages)
        {
            result.IsNonMessage = true;
            return result;
        }

        if (update.MessageType == "migrate_chat" && update.MigrateToChatId.HasValue)
        {
            await ApplyChatMigrationAsync(update.ChatId, update.MigrateToChatId.Value);
            result.IsNonMessage = true;
            return result;
        }

        // 分布式锁在事务之外获取：锁实现与 IUnitOfWork 共用同一 DbContext，放进事务里会随回滚一起被释放
        var lockKey = $"telegram_ingest:{update.DedupeKey}";
        var lockHandle = await _distributedLockService.TryAcquireAsync(lockKey, timeoutMs: 0);
        if (lockHandle == null)
        {
            result.IsDuplicate = true;
            return result;
        }

        try
        {
            return await ProcessWithLockAsync(update, result, settings);
        }
        finally
        {
            await lockHandle.DisposeAsync();
        }
    }

    private async Task<TelegramIngestionResult> ProcessWithLockAsync(
        TelegramWebhookMessage message,
        TelegramIngestionResult result,
        TelegramOptions settings)
    {
        var chatId = message.ChatId.ToString();
        var displayName = message.DisplayName ?? message.ChatTitle ?? message.FromUsername;

        var customer = await _unitOfWork.Customers.GetOrCreateByChannelAsync("telegram", chatId, displayName);
        if (customer == null)
        {
            result.ErrorMessage = "客户创建失败";
            return result;
        }

        var mailboxId = await ResolveMailboxIdAsync(settings);
        if (mailboxId == 0)
        {
            result.ErrorMessage = "未找到可用邮箱";
            return result;
        }

        var window = TimeSpan.FromHours(Math.Max(1, settings.ConversationMatchWindowHours));
        var conversation = await _unitOfWork.Conversations.FindActiveTelegramConversationByChatIdAsync(chatId, window);
        var isNewConversation = conversation == null;

        try
        {
            await using var transaction = await _unitOfWork.BeginTransactionAsync();

            // 事务内二次去重（锁只挡住并发，重复投递仍需靠 chat.id+message_id）
            var existingThread = await _unitOfWork.Threads.FindAsync(t =>
                t.MessageId == message.DedupeKey && t.State != ThreadState.Deleted);
            if (existingThread.Any())
            {
                await transaction.RollbackAsync();
                result.IsDuplicate = true;
                return result;
            }

            if (isNewConversation)
            {
                conversation = await CreateNewConversationAsync(message, customer, mailboxId);
            }
            else
            {
                conversation!.LastReplyAt = DateTime.UtcNow;
                conversation.LastReplyFrom = SourceVia.Customer;
                conversation.UpdatedAt = DateTime.UtcNow;
            }

            var thread = CreateThreadFromMessage(conversation!, customer, message);
            await _unitOfWork.Threads.AddAsync(thread);
            await _unitOfWork.SaveChangesAsync();

            if (conversation.CreatedByCustomerId == 0 && customer.Id > 0)
            {
                conversation.CreatedByCustomerId = customer.Id;
            }

            // 客户重新来信 ⇒ 之前判定不可达的会话（被拉黑）自动恢复可回复
            if (await TelegramUnreachableState.ClearAsync(_unitOfWork, conversation.Id))
            {
                _logger.LogInformation("Telegram 会话 {ConversationId} 客户已重新来信，清除不可达标记", conversation.Id);
            }

            if (!string.IsNullOrEmpty(message.FileId))
            {
                await DownloadAndAttachMediaAsync(thread, message);
            }

            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();

            result.NewConversationCount = isNewConversation ? 1 : 0;
            result.ReplyCount = 1;
            result.ConversationId = conversation.Id;

            await TriggerPostIngestionWorkflowsAsync(conversation.Id, isNewConversation);
            await _notificationPublisher.NotifyMailboxAsync(
                conversation.MailboxId.ToString(),
                "conversation_updated",
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
                    _logger.LogError(ex, "Telegram 入站触发 Webhook 失败（会话 {ConversationId}）", conversation.Id);
                }
            }

            if (isNewConversation && settings.AutoReplyEnabled && !string.IsNullOrWhiteSpace(settings.AutoReplyMessage))
            {
                result.AutoReplyContent = settings.AutoReplyMessage;
                // 论坛群里把入站消息的话题号带上，自动回复才会落在同一个话题，而不是跑到 General
                await SendAutoReplyAsync(chatId, conversation.Id, customer.Id, settings.AutoReplyMessage, conversation.LastReplyAt ?? DateTime.UtcNow, message.MessageThreadId);
            }

            return result;
        }
        catch (Exception ex)
        {
            _unitOfWork.ClearChanges();
            _logger.LogError(ex, "Telegram 消息持久化失败（DedupeKey {DedupeKey}）", message.DedupeKey);
            result.ErrorMessage = "消息持久化失败";
            return result;
        }
    }

    private async Task<Conversation> CreateNewConversationAsync(
        TelegramWebhookMessage message,
        Customer customer,
        int mailboxId)
    {
        var number = await _unitOfWork.Conversations.GetNextNumberAsync();
        var now = DateTime.UtcNow;
        var conversation = new Conversation
        {
            Number = number,
            Subject = BuildConversationSubject(message),
            Customer = customer,
            MailboxId = mailboxId,
            SourceType = SourceType.Telegram,
            Type = ConversationType.Chat,
            Status = ConversationStatus.Active,
            Priority = ConversationPriority.Normal,
            CustomerEmail = string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
            LastReplyAt = now,
            LastReplyFrom = SourceVia.Customer,
            CreatedByCustomerId = customer.Id
        };

        await _unitOfWork.Conversations.AddAsync(conversation);
        await _unitOfWork.SaveChangesAsync();
        return conversation;
    }

    private ConversationThread CreateThreadFromMessage(Conversation conversation, Customer customer, TelegramWebhookMessage message)
    {
        var createdAt = DateTimeOffset.FromUnixTimeSeconds(message.Timestamp > 0 ? message.Timestamp : DateTimeOffset.UtcNow.ToUnixTimeSeconds()).UtcDateTime;
        if (createdAt > DateTime.UtcNow)
        {
            createdAt = DateTime.UtcNow;
        }

        return new ConversationThread
        {
            ConversationId = conversation.Id,
            From = message.FromId.ToString(),
            To = message.ChatId.ToString(),
            Subject = string.Empty,
            Body = BuildThreadHtmlBody(message),
            MessageId = message.DedupeKey,
            SourceType = SourceType.Telegram,
            Type = ThreadType.Customer,
            SourceVia = SourceVia.Customer,
            CreatedByCustomerId = customer.Id,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    /// <summary>
    /// 即时下载入站媒体（file_id 可长期复用，但下载动作放在回调里以免队列堆积）。
    /// 下载统一走 <see cref="ITelegramClient.DownloadFileToTempAsync"/>：先 getFile 用 file_size 预检，
    /// 再用 Content-Length 与流式累计上限兜底 —— 超限时不会像原先 ReadAsByteArrayAsync 那样先把整个文件读进内存（大文件直接 OOM）。
    /// 自建 Local Bot API Server（--local）时 file_path 是服务器本地绝对路径：由客户端实现按同一上限直接复制。
    /// </summary>
    private async Task DownloadAndAttachMediaAsync(ConversationThread thread, TelegramWebhookMessage message)
    {
        TelegramFileDownloadResult? download = null;
        try
        {
            download = await _telegramClient.DownloadFileToTempAsync(message.FileId!, MaxDownloadBytes);

            if (download.Status == TelegramFileDownloadStatus.TooLarge)
            {
                // 线程正文里已经写入了媒体占位（BuildMediaPlaceholder），这里只需放弃落盘，保持既有“仅保留占位”行为
                _logger.LogWarning("Telegram 媒体超过 {LimitMB} MB（{Bytes} 字节），仅保留占位（会话 {ConversationId}）",
                    MaxDownloadBytes / 1024 / 1024, download.ByteCount, thread.ConversationId);
                return;
            }

            if (download.Status != TelegramFileDownloadStatus.Success || string.IsNullOrEmpty(download.LocalPath))
            {
                _logger.LogWarning("Telegram 媒体下载失败（file_unique_id {FileUniqueId}）：{Error}",
                    message.FileUniqueId, download.ErrorMessage);
                return;
            }

            if (download.ByteCount == 0)
            {
                return;
            }

            var mimeType = download.MimeType ?? message.MimeType
                ?? GetMimeTypeFromExtension(Path.GetExtension(download.LocalPath));

            var baseDir = AttachmentPathResolver.ResolveBasePath(_configuration["Attachment:UploadPath"], _env.ContentRootPath);
            var fileDir = DateTime.UtcNow.ToString("yyyy/MM/dd");
            var fileName = !string.IsNullOrEmpty(message.Filename)
                ? message.Filename!
                : $"telegram-{message.MessageId}{GetExtensionFromMimeType(mimeType)}";
            var storedName = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}";

            var fullDirPath = Path.GetFullPath(Path.Combine(baseDir, fileDir));
            var normalizedBase = Path.GetFullPath(baseDir) + Path.DirectorySeparatorChar;
            if (!fullDirPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("拒绝写入附件目录（疑似路径穿越）：{Dir}", fullDirPath);
                return;
            }

            Directory.CreateDirectory(fullDirPath);
            var fullPath = Path.Combine(fullDirPath, storedName);

            // 临时文件与附件目录通常同卷：直接原子改名，避免把内容再读一遍/写一遍
            File.Move(download.LocalPath!, fullPath);
            var size = download.ByteCount;
            download = null; // 已落到正式目录，finally 不需要再清理临时文件

            await _unitOfWork.Attachments.AddAsync(new Attachment
            {
                FileName = fileName,
                StoredFileName = storedName,
                FileDir = fileDir,
                Size = size,
                MimeType = mimeType ?? "application/octet-stream",
                Type = ClassifyMediaAttachment(mimeType, message.MessageType),
                ThreadId = thread.Id,
                Embedded = false,
                CreatedAt = DateTime.UtcNow
            });

            thread.HasAttachments = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram 媒体落盘失败（DedupeKey {DedupeKey}）", message.DedupeKey);
        }
        finally
        {
            // 只有“下载成功但没被移动到附件目录”的情况才会走到这里，避免把半成品/孤儿文件留在临时目录
            if (download?.LocalPath is { Length: > 0 } tempPath && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "清理 Telegram 临时文件失败：{Path}", tempPath);
                }
            }
        }
    }

    /// <summary>chat.id 迁移（超级群合并/migrate_to_chat_id）：改写 Customers.ChannelId，会话无需重建</summary>
    private async Task ApplyChatMigrationAsync(long oldChatId, long newChatId)
    {
        try
        {
            var customers = await _unitOfWork.Customers.FindAsync(c =>
                c.Channel == "telegram" && c.ChannelId == oldChatId.ToString());

            foreach (var customer in customers)
            {
                customer.ChannelId = newChatId.ToString();
            }

            await _unitOfWork.SaveChangesAsync();
            _logger.LogInformation("Telegram chat.id 已迁移 {OldChatId} → {NewChatId}，同步 {Count} 个客户绑定",
                oldChatId, newChatId, customers.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram chat.id 迁移失败 {OldChatId} → {NewChatId}", oldChatId, newChatId);
        }
    }

    private async Task<int> ResolveMailboxIdAsync(TelegramOptions settings)
    {
        if (settings.MailboxId is > 0)
        {
            return settings.MailboxId.Value;
        }

        var mailboxes = await _unitOfWork.Mailboxes.FindAsync(m => m.IsActive && m.DeletedAt == null);
        return mailboxes.FirstOrDefault()?.Id ?? 0;
    }

    private async Task TriggerPostIngestionWorkflowsAsync(int conversationId, bool isNewConversation)
    {
        try
        {
            await _workflowService.ExecuteForConversationAsync(
                conversationId,
                isNewConversation ? WorkflowConst.TriggerNewConversation : WorkflowConst.TriggerNewReply);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram 入站触发工作流失败（会话 {ConversationId}）", conversationId);
        }
    }

    private async Task SendAutoReplyAsync(string chatId, int conversationId, int customerId, string autoReplyMessage, DateTime conversationUpdatedAt, int? messageThreadId = null)
    {
        try
        {
            // 话题号来自入站消息（TelegramWebhookMessage.MessageThreadId）；非论坛群为 null，此时不发该字段
            var sendResult = await _telegramClient.SendTextMessageAsync(chatId, autoReplyMessage, null, default, messageThreadId);
            if (!sendResult.Success)
            {
                _logger.LogWarning("Telegram 自动回复发送失败（会话 {ConversationId}）：{Error}", conversationId, sendResult.ErrorMessage);
                return;
            }

            var replyTime = DateTime.UtcNow;
            if (conversationUpdatedAt >= replyTime)
            {
                replyTime = conversationUpdatedAt.AddSeconds(1);
            }

            await _unitOfWork.Threads.AddAsync(new ConversationThread
            {
                ConversationId = conversationId,
                CustomerId = customerId,
                Type = ThreadType.Message,
                SourceVia = SourceVia.User,
                SourceType = SourceType.Telegram,
                From = "telegram@system",
                To = chatId,
                Subject = string.Empty,
                Body = autoReplyMessage,
                SendStatus = SendStatus.Sent,
                IsInternal = false,
                MessageId = sendResult.MessageId ?? $"telegram-auto-reply-{Guid.NewGuid():N}",
                CreatedAt = replyTime,
                UpdatedAt = replyTime
            });

            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram 自动回复写线程失败（会话 {ConversationId}）", conversationId);
        }
    }

    private static string BuildConversationSubject(TelegramWebhookMessage message)
    {
        var name = message.DisplayName ?? message.ChatTitle ?? message.FromUsername ?? message.ChatId.ToString();
        var preview = Truncate((message.TextBody ?? message.Caption ?? string.Empty).Trim(), 30);
        return string.IsNullOrEmpty(preview)
            ? $"Telegram: {name}"
            : $"Telegram: {name} - {preview}";
    }

    private static string BuildThreadHtmlBody(TelegramWebhookMessage message)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrEmpty(message.TextBody))
        {
            builder.Append("<div class=\"telegram-text\">")
                .Append(HttpUtility.HtmlEncode(message.TextBody).Replace("\n", "<br>"))
                .Append("</div>");
        }

        if (!string.IsNullOrEmpty(message.Caption))
        {
            builder.Append("<div class=\"telegram-caption\">")
                .Append(HttpUtility.HtmlEncode(message.Caption).Replace("\n", "<br>"))
                .Append("</div>");
        }

        if (!string.IsNullOrEmpty(message.FileId))
        {
            builder.Append(BuildMediaPlaceholder(message));
        }

        if (message.ReplyToMessageId.HasValue)
        {
            builder.Append("<div class=\"telegram-reply-to\">↩ 引用消息 #")
                .Append(message.ReplyToMessageId.Value)
                .Append("</div>");
        }

        if (message.MessageThreadId.HasValue)
        {
            builder.Append("<div class=\"telegram-topic\">话题 #")
                .Append(message.MessageThreadId.Value)
                .Append("</div>");
        }

        if (!string.IsNullOrEmpty(message.StartParameter))
        {
            builder.Append("<div class=\"telegram-start-param\">/start 参数：")
                .Append(HttpUtility.HtmlEncode(message.StartParameter))
                .Append("</div>");
        }

        return builder.Length > 0 ? builder.ToString() : $"(Telegram {message.MessageType} 消息)";
    }

    private static string BuildMediaPlaceholder(TelegramWebhookMessage message)
    {
        var (emoji, label) = message.MessageType switch
        {
            "photo" => ("🖼", "图片"),
            "document" => ("📄", "文档"),
            "audio" => ("🎵", "音频"),
            "voice" => ("🎙", "语音"),
            "video" => ("🎬", "视频"),
            "video_note" => ("🎬", "视频消息"),
            "animation" => ("🎞", "GIF"),
            "sticker" => ("🏷", "贴纸"),
            _ => ("📎", "附件")
        };

        return $"<div class=\"telegram-media\">{emoji} {label}";
    }

    private static AttachmentType ClassifyMediaAttachment(string? mimeType, string messageType)
    {
        return messageType switch
        {
            "photo" or "sticker" => AttachmentType.Image,
            "audio" or "voice" => AttachmentType.Audio,
            "video" or "video_note" or "animation" => AttachmentType.Video,
            _ => mimeType switch
            {
                null => AttachmentType.Other,
                var m when m.StartsWith("image/") => AttachmentType.Image,
                var m when m.StartsWith("audio/") => AttachmentType.Audio,
                var m when m.StartsWith("video/") => AttachmentType.Video,
                _ => AttachmentType.Other
            }
        };
    }

    private static string GetExtensionFromMimeType(string? mimeType)
    {
        return mimeType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "audio/ogg" => ".ogg",
            "audio/mpeg" => ".mp3",
            "video/mp4" => ".mp4",
            "application/pdf" => ".pdf",
            _ => ".bin"
        };
    }

    private static string? GetMimeTypeFromExtension(string? extension)
    {
        return extension?.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".ogg" => "audio/ogg",
            ".mp3" => "audio/mpeg",
            ".mp4" => "video/mp4",
            ".pdf" => "application/pdf",
            _ => null
        };
    }

    private static string Truncate(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen)
        {
            return text;
        }

        return text[..maxLen] + "...";
    }
}
