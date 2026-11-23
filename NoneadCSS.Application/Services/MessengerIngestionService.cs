using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Application.Services;

/// <summary>
/// Messenger 入站消息接入服务 - 处理 Page Webhook 推送的 messaging 事件。
/// 跳过 is_echo / sender.id==PageId → 去重(mid + 分布式锁 + 事务内复检 + 唯一约束兜底)
/// → 客户匹配(PSID) → 会话匹配(24h 窗口) → 事务持久化 → 附件即时下载转存 → 工作流 → SignalR/Webhook 通知
/// </summary>
public class MessengerIngestionService : IMessengerIngestionService
{
    /// <summary>Customer.Channel 取值（与 SourceType.Messenger 对应的字符串键）</summary>
    public const string CustomerChannel = "messenger";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowService _workflowService;
    private readonly IMessengerSettingsProvider _settingsProvider;
    private readonly IMessengerClient _messengerClient;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ILogger<MessengerIngestionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _env;
    private readonly IWebhookService? _webhookService;

    public MessengerIngestionService(
        IUnitOfWork unitOfWork,
        IWorkflowService workflowService,
        IMessengerSettingsProvider settingsProvider,
        IMessengerClient messengerClient,
        INotificationPublisher notificationPublisher,
        IDistributedLockService distributedLockService,
        ILogger<MessengerIngestionService> logger,
        IConfiguration configuration,
        IHostEnvironment env,
        IWebhookService? webhookService = null)
    {
        _unitOfWork = unitOfWork;
        _workflowService = workflowService;
        _settingsProvider = settingsProvider;
        _messengerClient = messengerClient;
        _notificationPublisher = notificationPublisher;
        _distributedLockService = distributedLockService;
        _logger = logger;
        _configuration = configuration;
        _env = env;
        _webhookService = webhookService;
    }

    public async Task<MessengerIngestionResult> ProcessWebhookPayloadAsync(string? payload)
    {
        var result = new MessengerIngestionResult();

        _logger.LogDebug("Messenger Webhook 收到 payload，长度={Length}", payload?.Length ?? 0);

        if (payload == null || !MessengerWebhookParser.IsMessengerWebhook(payload))
        {
            _logger.LogWarning("非 Messenger Page Webhook payload，忽略");
            return result;
        }

        // 渠道关闭：仍返回成功让 Meta 停止重投，但不再落库/出站。
        // 页面文案承诺「关闭后 Webhook 仍可接收，但不再落库与出站」，而旧实现从不读 Enabled
        // ⇒ 关掉渠道后照样建客户、建会话、写入站线程。
        if (!_settingsProvider.Current.Enabled)
        {
            _logger.LogInformation("Messenger 渠道已关闭（Enabled=false），仅接收不入库");
            return result;
        }

        // 坐席/Page 自身产生的事件（is_echo、sender.id==PageId）只计数，不建线程
        result.IgnoredEchoCount = MessengerWebhookParser.CountIgnoredEchoes(payload);
        if (result.IgnoredEchoCount > 0)
        {
            _logger.LogInformation("Messenger Webhook 忽略 {Count} 条 echo/Page 自发事件", result.IgnoredEchoCount);
        }

        // 1. 先处理投递/已读回执（delivery / read）
        var receipts = MessengerWebhookParser.ParseReceipts(payload);
        if (receipts.Count > 0)
        {
            _logger.LogInformation("Messenger Webhook 包含 {Count} 条投递/已读回执", receipts.Count);
            foreach (var receipt in receipts)
            {
                await UpdateThreadReceiptAsync(receipt);
            }

            result.ReceiptCount = receipts.Count;
            // 回执与消息可能在同一 payload 中，继续处理消息
        }

        // 2. 解析入站消息（解析器已过滤 echo / 自发 / 回执 / 空事件）
        var messages = MessengerWebhookParser.ParseMessages(payload);
        if (messages.Count == 0)
        {
            _logger.LogDebug("Messenger Webhook 无入站消息（仅回执、echo 或空 payload）");
            return result;
        }

        _logger.LogInformation("Messenger Webhook 解析到 {Count} 条入站消息，开始逐条处理", messages.Count);

        // 多 Page 场景只接受本租户 Page 的入站事件。
        // recipient.id（= message.PageId）不属于本渠道配置的 PageId 时，另一个 Page 的客户消息
        // 会被当成本租户客户落库（出站又因 2635「PSID 不属于本 Page」失败），
        // 且多 entry 载荷里别的 Page 的 echo 还会被当客户消息，存在自回环风险。
        var configuredPageId = _settingsProvider.Current.PageId;
        if (!string.IsNullOrWhiteSpace(configuredPageId))
        {
            var total = messages.Count;
            // PageId 缺失时放行（无法判别归属，保持既有行为）；只有明确属于别的 Page 才丢弃。
            messages = messages
                .Where(m => string.IsNullOrWhiteSpace(m.PageId)
                            || string.Equals(m.PageId, configuredPageId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (messages.Count < total)
            {
                _logger.LogWarning(
                    "Messenger Webhook 丢弃 {Dropped} 条非本 Page 事件（期望 PageId={Expected}，共收到 {Total} 条）",
                    total - messages.Count, configuredPageId, total);
            }

            if (messages.Count == 0)
            {
                return result;
            }
        }

        foreach (var message in messages)
        {
            var singleResult = await ProcessSingleMessageAsync(message);
            if (singleResult.IsDuplicate)
            {
                result.IsDuplicate = true;
            }

            result.NewConversationCount += singleResult.NewConversationCount;
            result.ReplyCount += singleResult.ReplyCount;
            if (singleResult.ConversationId.HasValue && !result.ConversationId.HasValue)
            {
                result.ConversationId = singleResult.ConversationId;
            }
            if (!string.IsNullOrEmpty(singleResult.ErrorMessage) && string.IsNullOrEmpty(result.ErrorMessage))
            {
                result.ErrorMessage = singleResult.ErrorMessage;
            }
            if (!string.IsNullOrEmpty(singleResult.AutoReplyContent) && string.IsNullOrEmpty(result.AutoReplyContent))
            {
                result.AutoReplyContent = singleResult.AutoReplyContent;
            }
        }

        _logger.LogInformation("Messenger Webhook 处理完成：新会话={NewConv}, 回复={Reply}, 重复={Dup}, echo={Echo}, 回执={Receipt}, 错误={Error}",
            result.NewConversationCount, result.ReplyCount, result.IsDuplicate, result.IgnoredEchoCount, result.ReceiptCount, result.ErrorMessage ?? "无");

        return result;
    }

    private async Task<MessengerIngestionResult> ProcessSingleMessageAsync(MessengerWebhookMessage message)
    {
        var result = new MessengerIngestionResult();

        _logger.LogInformation("Messenger 开始处理消息：PsId={From}, PageId={PageId}, MessageType={Type}, MessageId={MessageId}, TimestampMs={Ts}",
            message.From, message.PageId, message.MessageType, message.MessageId, message.TimestampMs);

        if (string.IsNullOrWhiteSpace(message.From))
        {
            _logger.LogWarning("Messenger 消息缺少 sender.id，忽略：MessageId={MessageId}", message.MessageId);
            return result;
        }

        // 幂等第 1 层：分布式锁串行化同一 mid 的并发摄入（Meta 会在非 2xx 时立即重试）。
        // 锁必须在事务外获取/释放：DistributedLockService 与 IUnitOfWork 共享同一 DbContext，
        // 若在事务内获取，锁的 INSERT 会并入当前事务，回滚时锁一并失效。
        var lockKey = $"messenger_ingest:{message.MessageId}";
        var lockHandle = await _distributedLockService.TryAcquireAsync(lockKey, timeoutMs: 0);
        if (lockHandle == null)
        {
            _logger.LogInformation("Messenger 消息正在被其他实例处理，跳过：MessageId={MessageId}", message.MessageId);
            result.IsDuplicate = true;
            return result;
        }

        try
        {
            return await ProcessSingleMessageWithLockAsync(message);
        }
        finally
        {
            await lockHandle.DisposeAsync();
        }
    }

    private async Task<MessengerIngestionResult> ProcessSingleMessageWithLockAsync(MessengerWebhookMessage message)
    {
        var result = new MessengerIngestionResult();

        // 幂等第 2 层：同一 PSID 的并发摄入串行化。
        // 只锁 mid 挡不住「同一客户的两条不同消息并发到达」——两者都查不到活跃会话，于是各建一个会话，
        // 客户在坐席端就会看到两条同名会话。这里按 PSID 再串行化一次（最多等 5 秒）；
        // 拿不到锁就继续处理：宁可冒一次重复会话的风险，也不能丢客户消息。
        await using var psidLockHandle = await _distributedLockService.TryAcquireAsync(
            $"messenger_ingest_psid:{message.From}", timeoutMs: 5000);
        if (psidLockHandle == null)
        {
            _logger.LogWarning("Messenger 同一 PSID 并发摄入未取得锁，继续处理（可能产生重复会话）：PsId={PsId}, MessageId={MessageId}",
                message.From, message.MessageId);
        }

        // 1. 客户匹配（PSID → Customer）。PSID 是 Page 作用域标识，不是手机号，故不预填 Phones。
        _logger.LogDebug("Messenger 客户匹配：PsId={PsId}", message.From);
        var customer = await _unitOfWork.Customers.GetOrCreateByChannelAsync(
            CustomerChannel, message.From, null);
        if (customer == null)
        {
            result.ErrorMessage = "客户创建失败";
            _logger.LogError("Messenger 客户创建失败：PsId={PsId}", message.From);
            return result;
        }
        _logger.LogDebug("Messenger 客户匹配成功：CustomerId={CustomerId}, PsId={PsId}", customer.Id, message.From);

        // 1.1 补全客户资料：Webhook 不含姓名，需调 Graph 客户资料接口（first_name/last_name/profile_pic）。
        //     失败全部吞掉，不阻塞主流程。
        //     这里【不受】MessengerOptions.ContactCollectionEnabled 约束（与 :215 的联系方式引导不同）：
        //     ContactCollectionEnabled 只管手机号/邮箱这类「联系方式收集」；姓名与头像来自 Graph 公开资料接口
        //     （docs/integration/messenger.md §4 客户资料），属于客户识别的基础字段，任何配置下都应当补全。
        await TryEnrichCustomerProfileAsync(customer, message.From);

        // 2. 解析邮箱
        var mailboxId = await ResolveMailboxIdAsync();
        if (mailboxId == 0)
        {
            result.ErrorMessage = "未找到可用邮箱";
            _logger.LogError("Messenger 消息处理失败：未找到可用邮箱（配置的 MailboxId={MailboxId}）", _settingsProvider.Current.MailboxId);
            return result;
        }
        _logger.LogDebug("Messenger 邮箱解析：MailboxId={MailboxId}", mailboxId);

        // 3. 会话匹配（窗口取 MessengerOptions.WindowHours，默认 24h，不依赖共享的 IngestionMatchOptions）
        var window = TimeSpan.FromHours(Math.Max(1, _settingsProvider.Current.WindowHours));
        _logger.LogDebug("Messenger 会话匹配：PsId={PsId}, 窗口={Window}h", message.From, window.TotalHours);
        var conversation = await _unitOfWork.Conversations
            .FindActiveMessengerConversationByChannelIdAsync(message.From, window);
        var isNewConversation = conversation == null;
        _logger.LogDebug("Messenger 会话匹配结果：IsNew={IsNew}, ConversationId={ConvId}", isNewConversation, conversation?.Id);

        // 4. 事务持久化
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            // 幂等第 2 层：事务内按 mid 复检（配合唯一过滤索引提供数据库级兜底）
            _logger.LogDebug("Messenger 事务内去重检查：MessageId={MessageId}", message.MessageId);
            var existingThread = await _unitOfWork.Threads.FindAsync(
                t => t.MessageId == message.MessageId && t.State != ThreadState.Deleted);
            if (existingThread.Any())
            {
                _logger.LogInformation("Messenger 消息重复，跳过：MessageId={MessageId}, From={From}", message.MessageId, message.From);
                await transaction.RollbackAsync();
                result.IsDuplicate = true;
                return result;
            }
            _logger.LogDebug("Messenger 事务内去重通过：MessageId={MessageId}", message.MessageId);

            if (isNewConversation)
            {
                conversation = await CreateNewConversationAsync(customer, mailboxId, message);
                result.NewConversationCount = 1;
                _logger.LogInformation("Messenger 新会话创建：ConversationId={ConvId}, Number={Number}, CustomerId={CustomerId}",
                    conversation.Id, conversation.Number, customer.Id);
            }
            else
            {
                result.ReplyCount = 1;
                conversation!.LastReplyAt = DateTime.UtcNow;
                conversation.LastReplyFrom = SourceVia.Customer;
                conversation.UpdatedAt = DateTime.UtcNow;
                _logger.LogDebug("Messenger 已有会话追加回复：ConversationId={ConvId}", conversation.Id);
            }

            var thread = CreateThreadFromMessage(conversation!, customer, message);
            await _unitOfWork.Threads.AddAsync(thread);
            await _unitOfWork.SaveChangesAsync();
            _logger.LogDebug("Messenger 线程创建成功：ThreadId={ThreadId}, ConversationId={ConvId}, MessageId={MessageId}",
                thread.Id, conversation.Id, thread.MessageId);

            // 新客户在 SaveChanges 后才获得实际 ID，需回填 CreatedByCustomerId
            if (isNewConversation && conversation!.CreatedByCustomerId == 0 && customer.Id > 0)
            {
                conversation.CreatedByCustomerId = customer.Id;
                await _unitOfWork.SaveChangesAsync();
            }

            // 5. 入站媒体即时转存（payload.url 为短 TTL 签名 CDN 链，拖延即失效）
            if (!string.IsNullOrEmpty(message.MediaUrl))
            {
                _logger.LogInformation("Messenger 媒体消息需要下载附件：MessageId={MessageId}, AttachmentType={AttachmentType}, MediaUrl={MediaUrl}",
                    message.MessageId, message.AttachmentType, message.MediaUrl);
                await DownloadAndAttachMediaAsync(thread, message);
            }

            await transaction.CommitAsync();
            _logger.LogDebug("Messenger 事务提交成功：ConversationId={ConvId}", conversation.Id);

            // 6. mark_seen（sender_action），失败仅记日志
            // 不能用 _ = 丢弃任务：请求作用域（DbContext/HttpClient）在方法返回后即被释放，
            // 后台任务会在已释放的作用域上发请求，随机静默失败。此处的辅助方法自带 try/catch，
            // 所以直接 await 既安全又不会把失败抛给调用方。
            if (_settingsProvider.Current.MarkSeen)
            {
                await MarkSeenAsync(message.From);
            }

            // 7. 触发工作流
            await TriggerPostIngestionWorkflowsAsync(conversation!, isNewConversation);

            // 8. SignalR 通知
            await _notificationPublisher.NotifyMailboxAsync(
                conversation!.MailboxId.ToString(),
                "conversation_updated",
                new { conversationId = conversation.Id });
            _logger.LogDebug("Messenger SignalR 通知已发送：MailboxId={MailboxId}, ConversationId={ConvId}",
                conversation.MailboxId, conversation.Id);

            // 8.1 Webhook 通知（失败仅记录日志，不阻塞主流程）
            if (_webhookService != null)
            {
                var webhookEvent = isNewConversation ? "conversation_created" : "conversation_updated";
                try
                {
                    await _webhookService.TriggerAsync(webhookEvent, conversation.MailboxId, new { conversationId = conversation.Id }, "Conversation", conversation.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "触发 Webhook 失败：Event={Event}, Entity={EntityType}:{EntityId}", webhookEvent, "Conversation", conversation.Id);
                }
            }

            result.ConversationId = conversation.Id;
            _logger.LogInformation("Messenger 消息处理完成：ConversationId={ConvId}, ThreadId={ThreadId}, MessageId={MessageId}, Type={Type}",
                conversation.Id, thread.Id, message.MessageId, message.MessageType);

            // 9. 联系方式收集：提取适用所有消息，引导仅限新会话（Messenger 不提供姓名以外的联系方式）
            if (_settingsProvider.Current.ContactCollectionEnabled)
            {
                var contactExtracted = false;
                if (!string.IsNullOrWhiteSpace(message.TextBody))
                {
                    contactExtracted = await TryExtractContactInfoFromMessageAsync(customer, message.TextBody, conversation.Id);
                }

                var hasName = !string.IsNullOrWhiteSpace(customer.FirstName) || !string.IsNullOrWhiteSpace(customer.LastName);
                var hasPhone = customer.GetPhones().Count > 0;
                var hasEmail = customer.Emails != null && customer.Emails.Any(e => !string.IsNullOrWhiteSpace(e.EmailAddress));

                _logger.LogInformation("Messenger 客户信息收集判断：PsId={PsId}, CustomerId={CustomerId}, HasName={HasName}, HasPhone={HasPhone}, HasEmail={HasEmail}, ContactExtracted={ContactExtracted}, IsNew={IsNew}",
                    message.From, customer.Id, hasName, hasPhone, hasEmail, contactExtracted, isNewConversation);

                if ((!contactExtracted || !hasName || !hasPhone || !hasEmail)
                    && isNewConversation
                    && (!hasName || !hasPhone || !hasEmail))
                {
                    await TrySendContactGuidanceAsync(customer, message, conversation);
                }
            }

            // 10. 自动回复：仅新会话且启用时发送欢迎语（窗口内，无需 message_tag）
            if (isNewConversation
                && _settingsProvider.Current.AutoReplyEnabled
                && !string.IsNullOrWhiteSpace(_settingsProvider.Current.AutoReplyMessage)
                && !string.IsNullOrEmpty(message.From))
            {
                var autoReplySent = await SendAutoReplyAsync(
                    message.From,
                    conversation.Id,
                    customer.Id,
                    _settingsProvider.Current.AutoReplyMessage,
                    conversation.UpdatedAt);
                if (autoReplySent)
                {
                    result.AutoReplyContent = _settingsProvider.Current.AutoReplyMessage;
                }
            }
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // 幂等兜底层：罕见竞态绕过锁导致并发写入同一 mid 时，唯一约束冲突 → 回滚 + 清理跟踪 + 重查
            await transaction.SafeRollbackAsync();
            _unitOfWork.ClearChanges();
            var duplicate = await _unitOfWork.Threads.FindAsync(
                t => t.MessageId == message.MessageId && t.State != ThreadState.Deleted);
            if (duplicate.Any())
            {
                _logger.LogInformation("Messenger 消息并发写入重复，按幂等处理跳过：MessageId={MessageId}, ThreadId={ThreadId}",
                    message.MessageId, duplicate.First().Id);
                result.IsDuplicate = true;
                return result;
            }

            _logger.LogError(ex, "Messenger 消息事务唯一约束冲突且非重复，上报：MessageId={MessageId}", message.MessageId);
            result.ErrorMessage = "消息持久化失败";
            throw;
        }
        catch (Exception ex)
        {
            await transaction.SafeRollbackAsync();
            _logger.LogError(ex, "Messenger 消息持久化失败：MessageId={MessageId}, From={From}, ConversationId={ConvId}",
                message.MessageId, message.From, conversation?.Id);
            result.ErrorMessage = "消息持久化失败";
            throw new InvalidOperationException("Messenger 消息持久化失败", ex);
        }

        return result;
    }

    /// <summary>
    /// 判断数据库更新异常是否为唯一约束冲突（覆盖 SQL Server / SQLite / PostgreSQL）。
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var msg = (ex.InnerException?.Message ?? ex.Message).ToLowerInvariant();
        return msg.Contains("duplicate")
               || msg.Contains("unique")
               || msg.Contains("cannot insert duplicate key")
               || msg.Contains("constraint")
               || msg.Contains("conflict");
    }

    private async Task<Conversation> CreateNewConversationAsync(Customer customer, int mailboxId, MessengerWebhookMessage message)
    {
        var number = await _unitOfWork.Conversations.GetNextNumberAsync();

        var displayName = BuildDisplayName(customer);

        var conversation = new Conversation
        {
            Number = number,
            Subject = BuildConversationSubject(displayName, message),
            Customer = customer, // 用导航属性而非 CustomerId，确保新客户（Id=0）时 EF Core 正确做 FK fixup
            MailboxId = mailboxId,
            SourceType = SourceType.Messenger,
            Type = ConversationType.Chat,
            Status = ConversationStatus.Active,
            Priority = ConversationPriority.Urgent,
            CustomerEmail = string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastReplyAt = DateTime.UtcNow,
            LastReplyFrom = SourceVia.Customer,
            CreatedByCustomerId = customer.Id // 新客户时为 0，SaveChanges 后由调用方回填
        };

        await _unitOfWork.Conversations.AddAsync(conversation);
        await _unitOfWork.SaveChangesAsync();
        return conversation;
    }

    private ConversationThread CreateThreadFromMessage(Conversation conversation, Customer customer, MessengerWebhookMessage message)
    {
        var createdAt = message.TimestampMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(message.TimestampMs).UtcDateTime
            : DateTime.UtcNow;

        return new ConversationThread
        {
            ConversationId = conversation.Id,
            From = message.From,
            To = message.PageId ?? _settingsProvider.Current.PageId ?? string.Empty,
            Subject = string.Empty,
            Body = BuildThreadHtmlBody(message),
            MessageId = message.MessageId,
            SourceType = SourceType.Messenger,
            Type = ThreadType.Customer,
            SourceVia = SourceVia.Customer,
            CreatedByCustomerId = customer.Id,
            // 渠道服务器时间可能超前于服务端 UTC，钳制避免排序错乱
            CreatedAt = createdAt > DateTime.UtcNow ? DateTime.UtcNow : createdAt
        };
    }

    private async Task DownloadAndAttachMediaAsync(ConversationThread thread, MessengerWebhookMessage message)
    {
        try
        {
            _logger.LogInformation("Messenger 开始下载媒体：MessageId={MessageId}, AttachmentType={AttachmentType}, MediaUrl={MediaUrl}",
                message.MessageId, message.AttachmentType, message.MediaUrl);

            var downloaded = await _messengerClient.DownloadMediaAsync(message.MediaUrl!);
            if (!downloaded.HasValue || downloaded.Value.data.Length == 0)
            {
                _logger.LogWarning("Messenger 媒体下载失败（签名链可能已过期）：MessageId={MessageId}, AttachmentId={AttachmentId}",
                    message.MessageId, message.AttachmentId);
                return;
            }

            var data = downloaded.Value.data;
            var mimeType = downloaded.Value.mimeType ?? GuessMimeTypeFromAttachmentType(message.AttachmentType);

            var maxBytes = Math.Max(1, _settingsProvider.Current.MediaDownloadMaxMB) * 1024 * 1024;
            if (data.Length > maxBytes)
            {
                _logger.LogWarning("Messenger 媒体超过大小上限，不落盘：MessageId={MessageId}, Size={Size}, Max={Max}",
                    message.MessageId, data.Length, maxBytes);
                return;
            }

            var extension = GetExtensionFromMimeType(mimeType);
            var fileName = !string.IsNullOrWhiteSpace(message.Title)
                ? EnsureExtension(message.Title!, extension)
                : $"{message.MessageId}.{extension}";

            var attachmentType = ClassifyMediaAttachment(mimeType, message.AttachmentType);

            // 附件根目录统一解析：绝对路径原样、相对/缺失锚定 ContentRoot，与上传侧一致
            var uploadPath = AttachmentPathResolver.ResolveBasePath(
                _configuration["Attachment:UploadPath"], _env.ContentRootPath);
            var fileDir = DateTime.UtcNow.ToString("yyyy/MM/dd");
            var storedName = $"{Guid.NewGuid():N}.{extension}";
            var fullDir = Path.Combine(uploadPath, fileDir);

            // 路径遍历防护
            var fullDirPath = Path.GetFullPath(fullDir);
            var normalizedBase = Path.GetFullPath(uploadPath);
            if (!fullDirPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Messenger 附件路径遍历拦截：FileDir={FileDir}", fileDir);
                return;
            }

            Directory.CreateDirectory(fullDirPath);
            var fullPath = Path.Combine(fullDirPath, storedName);
            await File.WriteAllBytesAsync(fullPath, data);
            _logger.LogDebug("Messenger 媒体文件已写入磁盘：Size={Size} bytes", data.Length);

            var attachment = new Attachment
            {
                FileName = fileName,
                StoredFileName = storedName,
                FileDir = fileDir,
                Size = data.Length,
                MimeType = mimeType ?? "application/octet-stream",
                Type = attachmentType,
                ThreadId = thread.Id,
                Embedded = false,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Attachments.AddAsync(attachment);
            await _unitOfWork.SaveChangesAsync();
            _logger.LogInformation("Messenger 媒体附件保存成功：AttachmentId={AttachmentId}, ThreadId={ThreadId}, FileName={FileName}, Size={Size}, Type={Type}",
                attachment.Id, thread.Id, fileName, data.Length, attachmentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Messenger 媒体下载和保存失败：MessageId={MessageId}, AttachmentId={AttachmentId}, ThreadId={ThreadId}",
                message.MessageId, message.AttachmentId, thread.Id);
        }
    }

    private async Task UpdateThreadReceiptAsync(MessengerReceiptUpdate receipt)
    {
        try
        {
            if (string.IsNullOrEmpty(receipt.MessageId))
                return;

            var threads = await _unitOfWork.Threads.FindAsync(
                t => t.MessageId == receipt.MessageId && t.State != ThreadState.Deleted);
            var thread = threads.FirstOrDefault();
            if (thread == null)
            {
                _logger.LogDebug("Messenger 回执未找到对应线程：MessageId={MessageId}", receipt.MessageId);
                return;
            }

            // Graph 的 delivery/read 只有 watermark（该时刻之前全部送达/已读），没有失败态；
            // 失败在发送时已置 SendStatus.Failed / DeliveryError，这里只刷新 UpdatedAt 并留日志。
            thread.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            _logger.LogInformation("Messenger 回执更新：MessageId={MessageId}, Status={Status}, WatermarkMs={Watermark}",
                receipt.MessageId, receipt.Status, receipt.WatermarkMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Messenger 回执处理失败：MessageId={MessageId}", receipt.MessageId);
        }
    }

    private async Task<int> ResolveMailboxIdAsync()
    {
        var settings = _settingsProvider.Current;
        if (settings.MailboxId.HasValue && settings.MailboxId.Value > 0)
        {
            return settings.MailboxId.Value;
        }

        var mailboxes = await _unitOfWork.Mailboxes.FindAsync(m => m.IsActive && m.DeletedAt == null);
        return mailboxes.FirstOrDefault()?.Id ?? 0;
    }

    private async Task TriggerPostIngestionWorkflowsAsync(Conversation conversation, bool isNewConversation)
    {
        try
        {
            if (isNewConversation)
            {
                await _workflowService.ExecuteForConversationAsync(conversation.Id, WorkflowConst.TriggerNewConversation);
            }
            else
            {
                await _workflowService.ExecuteForConversationAsync(conversation.Id, WorkflowConst.TriggerNewReply);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Messenger 工作流触发失败：ConversationId={ConversationId}, IsNew={IsNew}",
                conversation.Id, isNewConversation);
        }
    }

    private async Task TrySendContactGuidanceAsync(Customer customer, MessengerWebhookMessage message, Conversation conversation)
    {
        try
        {
            // 双语文案：Messenger 客户跨语种，Graph 的 profile.locale 目前只在取资料时读过、未落库，
            // 无法按客户语言分支，因此同一条引导同时给出中英两版，避免 en 客户只看到中文。
            var contactGuidance =
                "为了更好地为您服务、方便后续发送相关资料，请回复您的姓名、手机号和邮箱地址，谢谢！\n\n" +
                "To serve you better, please reply with your name, phone number and email address. Thank you!";
            var sentId = await _messengerClient.SendTextMessageAsync(message.From, contactGuidance);
            if (string.IsNullOrEmpty(sentId))
            {
                _logger.LogWarning("Messenger 联系方式收集引导发送失败：PsId={PsId}", message.From);
                return;
            }

            _logger.LogInformation("已发送 Messenger 联系方式收集引导：PsId={PsId}", message.From);

            try
            {
                // 引导消息时间戳需晚于客户消息（客户消息 CreatedAt 来自渠道服务器时间，可能与服务端 UTC 有偏差）
                var guidanceTime = DateTime.UtcNow;
                if (conversation.UpdatedAt >= guidanceTime)
                    guidanceTime = conversation.UpdatedAt.AddSeconds(1);

                var guidanceThread = new ConversationThread
                {
                    ConversationId = conversation.Id,
                    CustomerId = customer.Id,
                    Type = ThreadType.Message,
                    SourceVia = SourceVia.User,
                    SourceType = SourceType.Messenger,
                    From = "messenger@system",
                    To = message.From,
                    Body = contactGuidance,
                    SendStatus = SendStatus.Sent,
                    IsInternal = false,
                    MessageId = $"messenger-contact-guidance-{Guid.NewGuid():N}",
                    CreatedAt = guidanceTime,
                    UpdatedAt = guidanceTime
                };
                await _unitOfWork.Threads.AddAsync(guidanceThread);
                await _unitOfWork.SaveChangesAsync();
                _logger.LogInformation("Messenger 联系方式收集引导已写入会话线程：ConversationId={ConversationId}", conversation.Id);
            }
            catch (Exception thrEx)
            {
                _logger.LogWarning(thrEx, "Messenger 联系方式收集引导写入会话线程失败：ConversationId={ConversationId}", conversation.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "发送 Messenger 联系方式收集引导失败：PsId={PsId}", message.From);
        }
    }

    private async Task MarkSeenAsync(string psid)
    {
        try
        {
            if (!string.IsNullOrEmpty(psid))
                await _messengerClient.SendSenderActionAsync(psid, "mark_seen");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Messenger mark_seen 失败：PsId={PsId}", psid);
        }
    }

    /// <summary>
    /// 发送自动回复欢迎语并写入会话线程（SourceVia=User，同步显示在会话记录中）。
    /// 发送或写入失败均记录日志返回 false，不阻塞主流程（优雅降级）。
    /// </summary>
    private async Task<bool> SendAutoReplyAsync(string psid, int conversationId, int customerId, string autoReplyMessage, DateTime conversationUpdatedAt)
    {
        try
        {
            if (_settingsProvider.Current.TypingIndicator)
            {
                // 会话进行中先出 typing_on，体验更接近 Messenger 官方机器人（失败不阻塞）
                await _messengerClient.SendSenderActionAsync(psid, "typing_on");
            }

            var sentId = await _messengerClient.SendTextMessageAsync(psid, autoReplyMessage);
            if (string.IsNullOrEmpty(sentId))
            {
                var error = _messengerClient.LastError;
                _logger.LogWarning("Messenger 自动回复发送失败：PsId={PsId}, GraphCode={Code}, Fbtrace={Fbtrace}",
                    psid, error?.Code ?? 0, error?.FbtraceId);
                return false;
            }
            _logger.LogInformation("Messenger 自动回复发送成功：PsId={PsId}, MessageId={MessageId}", psid, sentId);

            try
            {
                var replyTime = DateTime.UtcNow;
                if (conversationUpdatedAt >= replyTime)
                    replyTime = conversationUpdatedAt.AddSeconds(1);

                var replyThread = new ConversationThread
                {
                    ConversationId = conversationId,
                    CustomerId = customerId,
                    Type = ThreadType.Message,
                    SourceVia = SourceVia.User,
                    SourceType = SourceType.Messenger,
                    From = "messenger@system",
                    To = psid,
                    Body = autoReplyMessage,
                    SendStatus = SendStatus.Sent,
                    IsInternal = false,
                    MessageId = $"messenger-auto-reply-{Guid.NewGuid():N}",
                    CreatedAt = replyTime,
                    UpdatedAt = replyTime
                };
                await _unitOfWork.Threads.AddAsync(replyThread);
                await _unitOfWork.SaveChangesAsync();
                _logger.LogInformation("Messenger 自动回复已写入会话线程：ConversationId={ConversationId}", conversationId);
            }
            catch (Exception thrEx)
            {
                _logger.LogWarning(thrEx, "Messenger 自动回复写入会话线程失败：ConversationId={ConversationId}", conversationId);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Messenger 自动回复发送异常：PsId={PsId}", psid);
            return false;
        }
    }

    /// <summary>
    /// 基于 Graph 客户资料接口补全客户姓名与头像。
    /// 说明：Messenger Webhook 不携带姓名，需 GET /{ver}/me/{PSID}?fields=first_name,last_name,profile_pic,locale,timezone,gender。
    /// PSID 不是手机号 ⇒ 不写 Phones（联系方式靠对话提取/引导）。
    /// 失败全部吞掉，不影响主流程。
    /// </summary>
    private async Task TryEnrichCustomerProfileAsync(Customer customer, string psid)
    {
        try
        {
            var hasName = !string.IsNullOrWhiteSpace(customer.FirstName) || !string.IsNullOrWhiteSpace(customer.LastName);
            var hasAvatar = !string.IsNullOrWhiteSpace(customer.Avatar);
            if (hasName && hasAvatar)
                return;

            var profile = await _messengerClient.GetCustomerProfileAsync(psid);
            if (profile == null)
            {
                _logger.LogDebug("Messenger 客户资料接口无返回（可能未配置令牌或 PSID 不可查）：PsId={PsId}", psid);
                return;
            }

            var changed = false;

            if (!hasName)
            {
                // Graph 的 /me/{PSID} 已经把姓名拆成 first_name 与 last_name 两个字段返回
                // （docs/integration/messenger.md §4），所以直接按字段落库，不再拼成整串去猜：
                // 之前拼串后交给 ChineseNameSplitter，对 "Li Wang" 这类非中文名会返回
                // (LastName="", FirstName="Li Wang")，把整串塞进 FirstName —— 与平台给定的分段语义不符。
                var graphFirst = (profile.FirstName ?? string.Empty).Trim();
                var graphLast = (profile.LastName ?? string.Empty).Trim();

                if (graphFirst.Length > 0 && graphLast.Length > 0)
                {
                    customer.FirstName = graphFirst;
                    customer.LastName = graphLast;
                    changed = true;
                }
                else
                {
                    // Graph 只给了一段（另一侧为空）：此时才用 ChineseNameSplitter 兜底拆分，
                    // 它能把中文连写名（如 "张三"/"欧阳锋"）拆出姓与名，非中文则整体作为 FirstName。
                    var single = graphFirst.Length > 0 ? graphFirst : graphLast;
                    if (!string.IsNullOrWhiteSpace(single))
                    {
                        var (lastName, firstName) = ChineseNameSplitter.Split(single);
                        customer.FirstName = firstName;
                        customer.LastName = lastName;
                        changed = true;
                    }
                }

                if (changed)
                {
                    _logger.LogDebug("Messenger 客户姓名补全：CustomerId={CustomerId}, FirstName={FirstName}, LastName={LastName}",
                        customer.Id, customer.FirstName, customer.LastName);
                }
            }

            if (!hasAvatar && !string.IsNullOrWhiteSpace(profile.ProfilePic))
            {
                customer.Avatar = profile.ProfilePic;
                changed = true;
            }

            if (changed)
            {
                customer.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Messenger 客户资料补全失败：CustomerId={CustomerId}, PsId={PsId}", customer.Id, psid);
        }
    }

    /// <summary>联系方式提取最大尝试次数：超过后即使字段缺失也不再尝试，避免每条消息反复跑正则。</summary>
    private const int MaxContactExtractAttempts = 3;

    /// <summary>
    /// 从客户消息文本中提取联系方式（姓名、手机、邮箱）写入 Customer。
    /// Messenger API 不提供手机号/邮箱，只能靠对话引导后用正则识别。
    /// 写入策略：直接覆盖已有字段（客户在对话中提供的最新信息视为权威来源）。
    /// </summary>
    /// <returns>true=成功提取并写入至少一个字段；false=未提取到或已达上限</returns>
    private async Task<bool> TryExtractContactInfoFromMessageAsync(Customer customer, string text, int conversationId)
    {
        try
        {
            // 前置检查：确保 customer 被 EF Core 跟踪（工作流链路可能已 ClearChanges 导致 detach）
            if (_unitOfWork.Customers.FindTracked(customer.Id) == null)
            {
                _logger.LogWarning("Messenger 客户实体已被 detach，重新获取 tracked 实体：CustomerId={CustomerId}", customer.Id);
                var reloaded = await _unitOfWork.Customers.GetOrCreateByChannelAsync(
                    customer.Channel ?? CustomerChannel, customer.ChannelId ?? string.Empty);
                if (reloaded == null)
                {
                    _logger.LogError("Messenger 客户实体重新加载失败：CustomerId={CustomerId}", customer.Id);
                    return false;
                }
                customer = reloaded;
            }

            if (customer.ContactExtractAttempts >= MaxContactExtractAttempts)
            {
                _logger.LogDebug("Messenger 联系方式提取已达最大尝试次数，跳过：CustomerId={CustomerId}, Attempts={Attempts}, Max={Max}",
                    customer.Id, customer.ContactExtractAttempts, MaxContactExtractAttempts);
                return false;
            }

            customer.ContactExtractAttempts++;

            var info = ContactInfoExtractor.Extract(text, _logger);
            if (!info.HasAny)
            {
                customer.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
                return false;
            }

            var changed = false;

            if (!string.IsNullOrWhiteSpace(info.Name))
            {
                var (lastName, firstName) = ChineseNameSplitter.Split(info.Name);
                customer.LastName = lastName;
                customer.FirstName = firstName;
                changed = true;
                _logger.LogDebug("Messenger 对话姓名提取成功：CustomerId={CustomerId}, Name={Name}", customer.Id, info.Name);
            }

            if (!string.IsNullOrWhiteSpace(info.Phone))
            {
                var phones = customer.GetPhones();
                phones.Clear();
                phones.Add(new CustomerPhone
                {
                    Value = info.Phone,
                    Type = 4 // 4=手机
                });
                customer.SetPhones(phones);
                changed = true;
                _logger.LogDebug("Messenger 对话手机号提取成功：CustomerId={CustomerId}, Phone={Phone}", customer.Id, info.Phone);
            }

            customer.Emails ??= new List<Email>();
            if (!string.IsNullOrWhiteSpace(info.Email))
            {
                var emailOwned = await _unitOfWork.Customers.IsEmailOwnedByAnyCustomerAsync(info.Email, customer.Id);
                if (emailOwned)
                {
                    _logger.LogWarning("Messenger 对话邮箱已被其他客户占用，跳过添加：CustomerId={CustomerId}, Email={Email}", customer.Id, info.Email);
                }
                else
                {
                    customer.Emails.Clear();
                    customer.Emails.Add(new Email
                    {
                        EmailAddress = info.Email,
                        Type = 1,
                        CustomerId = customer.Id
                    });
                    changed = true;

                    // 提取到的邮箱回填到会话 CustomerEmail（仅当为空时），确保关闭会话时能发通知邮件
                    var conversationToUpdate = await _unitOfWork.Conversations.GetByIdForUpdateAsync(conversationId);
                    if (conversationToUpdate != null && string.IsNullOrWhiteSpace(conversationToUpdate.CustomerEmail))
                    {
                        conversationToUpdate.CustomerEmail = info.Email;
                        _logger.LogInformation("Messenger 会话邮箱回填成功：ConversationId={ConversationId}, CustomerEmail={Email}", conversationId, info.Email);
                    }
                }
            }

            customer.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();

            if (changed)
            {
                _logger.LogInformation("Messenger 对话联系方式提取成功：CustomerId={CustomerId}, Name={Name}, Phone={Phone}, Email={Email}, Attempts={Attempts}",
                    customer.Id, info.Name, info.Phone, info.Email, customer.ContactExtractAttempts);
                return true;
            }

            _logger.LogDebug("Messenger 联系方式提取未写入字段，但已记录尝试次数：CustomerId={CustomerId}, Attempts={Attempts}",
                customer.Id, customer.ContactExtractAttempts);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Messenger 对话联系方式提取失败：CustomerId={CustomerId}", customer.Id);
            _unitOfWork.ClearChanges();
            return false;
        }
    }

    // ---------------------------------------------------------------- 展示文本

    private static string BuildDisplayName(Customer customer)
    {
        var name = $"{customer.LastName ?? string.Empty}{customer.FirstName ?? string.Empty}".Trim();
        return !string.IsNullOrWhiteSpace(name) ? name : (customer.ChannelId ?? string.Empty);
    }

    private static string BuildConversationSubject(string displayName, MessengerWebhookMessage message)
    {
        var name = !string.IsNullOrEmpty(displayName) ? displayName : Truncate(message.From, 24);
        return message.MessageType switch
        {
            "text" => $"Messenger: {name} - {Truncate(message.TextBody, 30)}",
            "image" => $"Messenger: {name} - 图片消息",
            "video" => $"Messenger: {name} - 视频消息",
            "audio" => $"Messenger: {name} - 语音消息",
            "file" => $"Messenger: {name} - 文件消息",
            "location" => $"Messenger: {name} - 位置消息",
            "fallback" => $"Messenger: {name} - 附件消息",
            "postback" => $"Messenger: {name} - 按钮点击: {Truncate(message.PostbackTitle ?? message.PostbackPayload, 30)}",
            "quick_reply" => $"Messenger: {name} - 快捷回复: {Truncate(message.QuickReplyPayload, 30)}",
            "optin" => $"Messenger: {name} - 授权订阅",
            _ => $"Messenger: {name} - {message.MessageType} 消息"
        };
    }

    private static string BuildThreadHtmlBody(MessengerWebhookMessage message)
    {
        switch (message.MessageType)
        {
            case "text":
                return $"<div class=\"messenger-text\">{HttpUtility.HtmlEncode(message.TextBody ?? string.Empty).Replace("\n", "<br>")}</div>";
            case "image":
                return BuildMediaPlaceholder("📷", "图片消息", message.Title ?? "客户发送了一张图片，请查看下方附件");
            case "video":
                return BuildMediaPlaceholder("🎥", "视频消息", message.Title ?? "客户发送了一段视频，请查看下方附件");
            case "audio":
                return BuildMediaPlaceholder("🎤", "语音消息", message.Title ?? "客户发送了一段语音，请查看下方附件");
            case "file":
                return BuildMediaPlaceholder("📄", "文件消息", message.Title ?? "客户发送了文件，请查看下方附件");
            case "fallback":
                return BuildMediaPlaceholder("📎", "附件消息", message.Title ?? "客户发送了暂不支持的附件");
            case "location":
                return BuildLocationHtml(message);
            case "postback":
                return $"<div class=\"messenger-postback\">🔘 <strong>按钮点击：</strong>{HttpUtility.HtmlEncode(message.PostbackTitle ?? message.PostbackPayload ?? string.Empty)}</div>"
                       + (string.IsNullOrWhiteSpace(message.PostbackPayload) ? string.Empty
                          : $"<br><code>{HttpUtility.HtmlEncode(message.PostbackPayload)}</code>");
            case "quick_reply":
                return $"<div class=\"messenger-quick-reply\">💬 <strong>快捷回复：</strong>{HttpUtility.HtmlEncode(message.QuickReplyPayload ?? message.TextBody ?? string.Empty)}</div>";
            case "optin":
                return $"<div class=\"messenger-optin\">✅ <strong>授权订阅：</strong>{HttpUtility.HtmlEncode(message.OptinRef ?? string.Empty)}</div>";
            default:
                return $"<div class=\"messenger-unknown\">[{HttpUtility.HtmlEncode(message.MessageType)} 消息]</div>";
        }
    }

    private static string BuildMediaPlaceholder(string emoji, string label, string description)
        => $"<div class=\"messenger-media\">{emoji} <strong>{label}</strong><br>{HttpUtility.HtmlEncode(description)}</div>";

    private static string BuildLocationHtml(MessengerWebhookMessage message)
    {
        var title = !string.IsNullOrEmpty(message.Title) ? HttpUtility.HtmlEncode(message.Title) : "未知位置";
        var coords = message.Latitude.HasValue && message.Longitude.HasValue
            ? $"<br>🌐 {message.Latitude.Value:F6}, {message.Longitude.Value:F6}"
            : "";
        var dataAttrs = message.Latitude.HasValue && message.Longitude.HasValue
            ? $" data-lat=\"{message.Latitude.Value:F6}\" data-lng=\"{message.Longitude.Value:F6}\" data-name=\"{HttpUtility.HtmlEncode(message.Title ?? "位置")}\""
            : "";
        return $"<div class=\"messenger-location\"{dataAttrs}>📍 <strong>{title}</strong>{coords}</div>";
    }

    private static string Truncate(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLen ? text : text[..maxLen] + "...";
    }

    private static string EnsureExtension(string fileName, string extension)
    {
        var safe = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safe))
            safe = "attachment";
        return Path.GetExtension(safe).Length > 0 ? safe : $"{safe}.{extension}";
    }

    private static string? GuessMimeTypeFromAttachmentType(string? attachmentType)
    {
        return attachmentType?.ToLowerInvariant() switch
        {
            "image" => "image/jpeg",
            "audio" => "audio/mp4",
            "video" => "video/mp4",
            "file" => "application/octet-stream",
            _ => null
        };
    }

    private static string GetExtensionFromMimeType(string? mimeType)
    {
        return mimeType?.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            "image/gif" => "gif",
            "audio/aac" => "aac",
            "audio/amr" => "amr",
            "audio/mp3" or "audio/mpeg" => "mp3",
            "audio/mp4" or "audio/m4a" => "m4a",
            "audio/ogg" => "ogg",
            "video/mp4" => "mp4",
            "video/3gpp" => "3gp",
            "application/pdf" => "pdf",
            "text/plain" => "txt",
            _ => "bin"
        };
    }

    private static AttachmentType ClassifyMediaAttachment(string? mimeType, string? attachmentType)
    {
        if (string.Equals(attachmentType, "image", StringComparison.OrdinalIgnoreCase)) return AttachmentType.Image;
        if (string.Equals(attachmentType, "video", StringComparison.OrdinalIgnoreCase)) return AttachmentType.Video;
        if (string.Equals(attachmentType, "audio", StringComparison.OrdinalIgnoreCase)) return AttachmentType.Audio;

        return mimeType?.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" or "image/png" or "image/webp" or "image/gif" => AttachmentType.Image,
            "audio/aac" or "audio/amr" or "audio/mp3" or "audio/mpeg" or "audio/mp4" or "audio/m4a" or "audio/ogg" => AttachmentType.Audio,
            "video/mp4" or "video/3gpp" => AttachmentType.Video,
            _ => AttachmentType.Other
        };
    }
}
