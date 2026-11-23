using System.Text.Json;
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
/// WhatsApp 入站消息接入服务 - 处理 Webhook 推送的消息。
/// 去重(wamid) → 客户匹配(wa_id) → 会话匹配(24h窗口) → 事务持久化 → 附件下载 → 触发工作流
/// </summary>
public class WhatsAppIngestionService : IWhatsAppIngestionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowService _workflowService;
    private readonly IWhatsAppSettingsProvider _settingsProvider;
    private readonly IWhatsAppClient _whatsAppClient;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ILogger<WhatsAppIngestionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _env;
    private readonly TimeSpan _conversationMatchWindow;
    private readonly IWebhookService? _webhookService;

    public WhatsAppIngestionService(
        IUnitOfWork unitOfWork,
        IWorkflowService workflowService,
        IWhatsAppSettingsProvider settingsProvider,
        IWhatsAppClient whatsAppClient,
        INotificationPublisher notificationPublisher,
        IDistributedLockService distributedLockService,
        ILogger<WhatsAppIngestionService> logger,
        IConfiguration configuration,
        IOptions<IngestionMatchOptions> ingestionMatchOptions,
        IHostEnvironment env,
        IWebhookService? webhookService = null)
    {
        _unitOfWork = unitOfWork;
        _workflowService = workflowService;
        _settingsProvider = settingsProvider;
        _whatsAppClient = whatsAppClient;
        _notificationPublisher = notificationPublisher;
        _distributedLockService = distributedLockService;
        _logger = logger;
        _configuration = configuration;
        _conversationMatchWindow = TimeSpan.FromHours(ingestionMatchOptions.Value.WhatsAppMatchWindowHours);
        _env = env;
        _webhookService = webhookService;
    }

    public async Task<WhatsAppIngestionResult> ProcessWebhookPayloadAsync(string? payload)
    {
        var result = new WhatsAppIngestionResult();

        _logger.LogDebug("WhatsApp Webhook 收到 payload，长度={Length}", payload?.Length ?? 0);

        if (payload == null || !WhatsAppWebhookParser.IsWhatsAppWebhook(payload))
        {
            _logger.LogWarning("非 WhatsApp Webhook payload，忽略");
            return result;
        }

        // 1. 先处理状态回执
        var statuses = WhatsAppWebhookParser.ParseStatuses(payload);
        if (statuses.Count > 0)
        {
            _logger.LogInformation("WhatsApp Webhook 包含 {Count} 条状态回执", statuses.Count);
            foreach (var status in statuses)
            {
                await UpdateThreadStatusAsync(status);
            }
            result.IsStatus = true;
            // 状态回执和消息可能在同一 payload，继续处理消息
        }

        // 2. 解析入站消息
        var messages = WhatsAppWebhookParser.ParseMessages(payload);
        if (messages.Count == 0)
        {
            _logger.LogDebug("WhatsApp Webhook 无入站消息（仅状态回执或空 payload）");
            return result;
        }

        _logger.LogInformation("WhatsApp Webhook 解析到 {Count} 条入站消息，开始逐条处理", messages.Count);

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

        _logger.LogInformation("WhatsApp Webhook 处理完成：新会话={NewConv}, 回复={Reply}, 重复={Dup}, 错误={Error}",
            result.NewConversationCount, result.ReplyCount, result.IsDuplicate, result.ErrorMessage ?? "无");

        return result;
    }

    private async Task<WhatsAppIngestionResult> ProcessSingleMessageAsync(WhatsAppWebhookMessage message)
    {
        var result = new WhatsAppIngestionResult();

        _logger.LogInformation("WhatsApp 开始处理消息：From={From}, ProfileName={Name}, MessageType={Type}, MessageId={MessageId}, Timestamp={Ts}",
            message.From, message.ProfileName, message.MessageType, message.MessageId, message.Timestamp);

        // 系统消息不创建线程
        if (message.MessageType == "system")
        {
            _logger.LogInformation("WhatsApp 系统消息忽略：From={From}, MessageId={MessageId}", message.From, message.MessageId);
            return result;
        }

        // TD-06：在进入事务前获取分布式锁，串行化同一 WhatsApp 消息（MessageId）的并发摄入，消除 TOCTOU 竞态。
        // 锁必须在事务外获取/释放：DistributedLockService 与 IUnitOfWork 共享同一 DbContext，
        // 若在事务内获取，锁的 INSERT 会并入当前事务，导致后续 Rollback 时把锁一并回滚，锁失效。
        var lockKey = $"whatsapp_ingest:{message.MessageId}";
        var lockHandle = await _distributedLockService.TryAcquireAsync(lockKey, timeoutMs: 0);
        if (lockHandle == null)
        {
            _logger.LogInformation("WhatsApp 消息正在被其他实例处理，跳过：MessageId={MessageId}", message.MessageId);
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

    private async Task<WhatsAppIngestionResult> ProcessSingleMessageWithLockAsync(WhatsAppWebhookMessage message)
    {
        var result = new WhatsAppIngestionResult();

        // 2. 客户匹配 (wa_id → Customer)
        _logger.LogDebug("WhatsApp 客户匹配：WaId={WaId}, ProfileName={Name}", message.From, message.ProfileName);
        var customer = await _unitOfWork.Customers.GetOrCreateByChannelAsync(
            "whatsapp", message.From, message.ProfileName);
        if (customer == null)
        {
            result.ErrorMessage = "客户创建失败";
            _logger.LogError("WhatsApp 客户创建失败：WaId={WaId}, ProfileName={Name}", message.From, message.ProfileName);
            return result;
        }
        _logger.LogDebug("WhatsApp 客户匹配成功：CustomerId={CustomerId}, WaId={WaId}", customer.Id, message.From);

        // 2.1 补全客户资料：
        // - WhatsApp Webhook 已提供 contacts[].profile.name（已作为 displayName 传入 GetOrCreateByChannelAsync，写入 FirstName）
        // - wa_id（message.From）本身是 E.164 格式的手机号（含国家区号前缀，无 + 号），写入 Phones
        // - WhatsApp Cloud API 不提供 email、头像等其他个人信息，故仅补全手机号
        // 失败全部吞掉，不阻塞主流程
        await TryEnrichCustomerProfileAsync(customer, message.From, message.ProfileName);

        // 3. 解析邮箱
        var mailboxId = await ResolveMailboxIdAsync();
        if (mailboxId == 0)
        {
            result.ErrorMessage = "未找到可用邮箱";
            _logger.LogError("WhatsApp 消息处理失败：未找到可用邮箱（配置的 MailboxId={MailboxId}）", _settingsProvider.Current.MailboxId);
            return result;
        }
        _logger.LogDebug("WhatsApp 邮箱解析：MailboxId={MailboxId}", mailboxId);

        // 4. 会话匹配 (24h 窗口)
        _logger.LogDebug("WhatsApp 会话匹配：WaId={WaId}, 窗口={Window}h", message.From, _conversationMatchWindow.TotalHours);
        var conversation = await _unitOfWork.Conversations
            .FindActiveWhatsAppConversationByWaIdAsync(message.From, _conversationMatchWindow);
        var isNewConversation = conversation == null;
        _logger.LogDebug("WhatsApp 会话匹配结果：IsNew={IsNew}, ConversationId={ConvId}", isNewConversation, conversation?.Id);

        // 5. 事务持久化
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            // TD-06：在锁串行化后，仍需在事务内做最终去重，结合唯一过滤索引提供数据库级幂等兜底。
            _logger.LogDebug("WhatsApp 事务内去重检查：MessageId={MessageId}", message.MessageId);
            var existingThread = await _unitOfWork.Threads.FindAsync(
                t => t.MessageId == message.MessageId && t.State != ThreadState.Deleted);
            if (existingThread.Any())
            {
                _logger.LogInformation("WhatsApp 消息重复，跳过：MessageId={MessageId}, From={From}", message.MessageId, message.From);
                await transaction.RollbackAsync();
                result.IsDuplicate = true;
                return result;
            }
            _logger.LogDebug("WhatsApp 事务内去重通过：MessageId={MessageId}", message.MessageId);

            if (isNewConversation)
            {
                conversation = await CreateNewConversationAsync(customer, mailboxId, message);
                result.NewConversationCount = 1;
                _logger.LogInformation("WhatsApp 新会话创建：ConversationId={ConvId}, Number={Number}, CustomerId={CustomerId}",
                    conversation.Id, conversation.Number, customer.Id);
            }
            else
            {
                result.ReplyCount = 1;
                // 已有会话追加客户回复：更新 LastReplyAt，保证会话列表排序正确
                conversation!.LastReplyAt = DateTime.UtcNow;
                conversation.LastReplyFrom = SourceVia.Customer;
                conversation.UpdatedAt = DateTime.UtcNow;
                _logger.LogDebug("WhatsApp 已有会话追加回复：ConversationId={ConvId}", conversation!.Id);
            }

            // 创建线程
            var thread = CreateThreadFromMessage(conversation!, customer, message);
            await _unitOfWork.Threads.AddAsync(thread);
            await _unitOfWork.SaveChangesAsync();
            _logger.LogDebug("WhatsApp 线程创建成功：ThreadId={ThreadId}, ConversationId={ConvId}, MessageId={MessageId}",
                thread.Id, conversation!.Id, thread.MessageId);

            // 新客户在 SaveChanges 后才获得实际 ID，需回填 CreatedByCustomerId
            if (isNewConversation && conversation!.CreatedByCustomerId == 0 && customer.Id > 0)
            {
                conversation.CreatedByCustomerId = customer.Id;
                await _unitOfWork.SaveChangesAsync();
            }

            // 附件下载
            if (!string.IsNullOrEmpty(message.MediaId) || !string.IsNullOrEmpty(message.MediaUrl))
            {
                _logger.LogInformation("WhatsApp 媒体消息需要下载附件：MessageId={MessageId}, MediaId={MediaId}, MediaUrl={MediaUrl}, MimeType={MimeType}",
                    message.MessageId, message.MediaId, message.MediaUrl, message.MimeType);
                await DownloadAndAttachMediaAsync(thread, message);
            }

            await transaction.CommitAsync();
            _logger.LogDebug("WhatsApp 事务提交成功：ConversationId={ConvId}", conversation!.Id);

            // 6. 标记已读
            if (!string.IsNullOrEmpty(message.MessageId))
            {
                _logger.LogDebug("WhatsApp 异步标记已读：MessageId={MessageId}", message.MessageId);
                _ = MarkAsReadFireAndForgetAsync(message.MessageId);
            }

            // 7. 触发工作流
            _logger.LogDebug("WhatsApp 触发工作流：ConversationId={ConvId}, IsNew={IsNew}", conversation!.Id, isNewConversation);
            await TriggerPostIngestionWorkflowsAsync(conversation!, isNewConversation);

            // 8. SignalR 通知
            await _notificationPublisher.NotifyMailboxAsync(
                conversation!.MailboxId.ToString(),
                "conversation_updated",
                new { conversationId = conversation.Id });
            _logger.LogDebug("WhatsApp SignalR 通知已发送：MailboxId={MailboxId}, ConversationId={ConvId}",
                conversation.MailboxId, conversation.Id);

            // 8.1 触发 Webhook 通知（失败仅记录日志，不阻塞主流程）
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
            _logger.LogInformation("WhatsApp 消息处理完成：ConversationId={ConvId}, ThreadId={ThreadId}, MessageId={MessageId}, Type={Type}",
                conversation.Id, thread.Id, message.MessageId, message.MessageType);

            // 9. 联系方式收集：提取适用于所有消息（新会话+回复），引导仅限新会话
            //    WhatsApp API 不提供用户邮箱，通过对话引导 + 正则提取实现
            //    提取方法内部已有最大尝试次数限制（3 次）和字段完整性检查，不会过度提取
            //    受 ContactCollectionEnabled 开关控制（管理员可在设置页关闭）
            if (_settingsProvider.Current.ContactCollectionEnabled)
            {
                //    先尝试从客户当前消息文本中提取姓名、手机、邮箱（客户可能直接回复了信息）
                var contactExtracted = false;
                if (!string.IsNullOrWhiteSpace(message.TextBody))
                {
                    contactExtracted = await TryExtractContactInfoFromMessageAsync(customer, message.TextBody, conversation.Id);
                }

                //    判断客户当前是否缺任一字段（姓名、手机、邮箱）
                //    若客户缺任一字段且本次是新会话，发送引导消息请客户提供
                bool hasName = !string.IsNullOrWhiteSpace(customer.FirstName) || !string.IsNullOrWhiteSpace(customer.LastName);
                bool hasPhone = customer.GetPhones().Count > 0;
                bool hasEmail = customer.Emails != null && customer.Emails.Any(e => !string.IsNullOrWhiteSpace(e.EmailAddress));

                _logger.LogInformation("WhatsApp 客户信息收集判断：WaId={WaId}, CustomerId={CustomerId}, HasName={HasName}, HasPhone={HasPhone}, HasEmail={HasEmail}, ContactExtracted={ContactExtracted}, IsNew={IsNew}",
                    message.From, customer.Id, hasName, hasPhone, hasEmail, contactExtracted, isNewConversation);

                if ((!contactExtracted || !hasName || !hasPhone || !hasEmail)
                    && isNewConversation
                    && (!hasName || !hasPhone || !hasEmail))
                {
                    try
                    {
                        var contactGuidance = "为了更好地为您服务，方便后续发送相关资料，请您回复您的姓名，手机，邮箱地址，谢谢！";
                        var sentId = await _whatsAppClient.SendTextMessageAsync(message.From, contactGuidance);
                        var guidanceSent = !string.IsNullOrEmpty(sentId);
                        if (guidanceSent)
                        {
                            _logger.LogInformation("已发送 WhatsApp 联系方式收集引导：WaId={WaId}", message.From);
                            // 写入系统消息到会话线程（Type=Message, SourceVia=User），同步显示在会话记录中
                            try
                            {
                                // 确保引导消息时间戳晚于客户消息（客户消息 CreatedAt 来自渠道服务器时间，可能与服务端 UTC 有偏差）
                                var guidanceTime = DateTime.UtcNow;
                                var convUpdated = conversation.UpdatedAt;
                                if (convUpdated >= guidanceTime) guidanceTime = convUpdated.AddSeconds(1);

                                var guidanceThread = new ConversationThread
                                {
                                    ConversationId = conversation.Id,
                                    CustomerId = customer.Id,
                                    Type = ThreadType.Message,
                                    SourceVia = SourceVia.User,
                                    SourceType = SourceType.WhatsApp,
                                    From = "whatsapp@system",
                                    To = message.From,
                                    Body = contactGuidance,
                                    SendStatus = SendStatus.Sent,
                                    IsInternal = false,
                                    MessageId = $"whatsapp-contact-guidance-{Guid.NewGuid():N}",
                                    CreatedAt = guidanceTime,
                                    UpdatedAt = guidanceTime
                                };
                                await _unitOfWork.Threads.AddAsync(guidanceThread);
                                await _unitOfWork.SaveChangesAsync();
                                _logger.LogInformation("WhatsApp 联系方式收集引导已写入会话线程：ConversationId={ConversationId}",
                                    conversation.Id);
                            }
                            catch (Exception thrEx)
                            {
                                _logger.LogWarning(thrEx, "WhatsApp 联系方式收集引导写入会话线程失败：ConversationId={ConversationId}",
                                    conversation.Id);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("WhatsApp 联系方式收集引导发送失败：WaId={WaId}", message.From);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "发送 WhatsApp 联系方式收集引导失败：WaId={WaId}", message.From);
                    }
                }
                else if (isNewConversation && hasName && hasPhone && hasEmail)
                {
                    // 客户资料已齐全（罕见场景：Meta WhatsApp API 提供了邮箱或客户之前就提供了）
                    _logger.LogInformation("WhatsApp 客户信息收集跳过：客户资料已齐全，WaId={WaId}, CustomerId={CustomerId}",
                        message.From, customer.Id);
                }
                else if (!isNewConversation)
                {
                    _logger.LogDebug("WhatsApp 客户信息收集：非新会话，仅提取不发送引导：WaId={WaId}, ContactExtracted={ContactExtracted}",
                        message.From, contactExtracted);
                }
            }

            // 10. 自动回复：仅新会话且启用自动回复时，发送欢迎语并写入会话线程（参考飞书/钉钉被动回复模式）
            //    发送或写入失败均优雅降级（记录日志），不阻塞主流程。
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
            // TD-06 幂等兜底：若因罕见竞态绕过锁导致并发写入重复 MessageId，
            // 唯一约束冲突时回滚 + 清理跟踪 + 重查，若该消息已存在则视为重复跳过；否则上报真实异常。
            await transaction.SafeRollbackAsync();
            _unitOfWork.ClearChanges();
            var duplicate = await _unitOfWork.Threads.FindAsync(
                t => t.MessageId == message.MessageId && t.State != ThreadState.Deleted);
            if (duplicate.Any())
            {
                _logger.LogInformation("WhatsApp 消息并发写入重复，按幂等处理跳过：MessageId={MessageId}, ThreadId={ThreadId}",
                    message.MessageId, duplicate.First().Id);
                result.IsDuplicate = true;
                return result;
            }
            _logger.LogError(ex, "WhatsApp 消息事务唯一约束冲突且非重复，上报：MessageId={MessageId}", message.MessageId);
            result.ErrorMessage = "消息持久化失败";
            throw;
        }
        catch (Exception ex)
        {
            await transaction.SafeRollbackAsync();
            _logger.LogError(ex, "WhatsApp 消息持久化失败：MessageId={MessageId}, From={From}, ConversationId={ConvId}",
                message.MessageId, message.From, conversation?.Id);
            result.ErrorMessage = "消息持久化失败";
            throw new InvalidOperationException("WhatsApp 消息持久化失败", ex);
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

    private async Task<Conversation> CreateNewConversationAsync(Customer customer, int mailboxId, WhatsAppWebhookMessage message)
    {
        var number = await _unitOfWork.Conversations.GetNextNumberAsync();

        var conversation = new Conversation
        {
            Number = number,
            Subject = BuildConversationSubject(message),
            Customer = customer, // 使用导航属性而非 CustomerId = customer.Id，确保新客户（Id=0）时 EF Core 能正确做 FK fixup
            MailboxId = mailboxId,
            SourceType = SourceType.WhatsApp,
            Type = ConversationType.Chat,
            Status = ConversationStatus.Active,
            Priority = ConversationPriority.Urgent,
            CustomerEmail = string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastReplyAt = DateTime.UtcNow,
            LastReplyFrom = SourceVia.Customer,
            CreatedByCustomerId = customer.Id // 新客户时为 0，SaveChanges 后由调用方更新
        };

        await _unitOfWork.Conversations.AddAsync(conversation);
        await _unitOfWork.SaveChangesAsync();
        return conversation;
    }

    private static ConversationThread CreateThreadFromMessage(Conversation conversation, Customer customer, WhatsAppWebhookMessage message)
    {
        var thread = new ConversationThread
        {
            ConversationId = conversation.Id,
            From = message.From,
            To = message.DisplayPhoneNumber ?? string.Empty,
            Subject = string.Empty,
            Body = BuildThreadHtmlBody(message),
            MessageId = message.MessageId,
            SourceType = SourceType.WhatsApp,
            Type = ThreadType.Customer,
            SourceVia = SourceVia.Customer,
            CreatedByCustomerId = customer.Id,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(message.Timestamp).UtcDateTime > DateTime.UtcNow ? DateTime.UtcNow : DateTimeOffset.FromUnixTimeSeconds(message.Timestamp).UtcDateTime
        };
        return thread;
    }

    private async Task DownloadAndAttachMediaAsync(ConversationThread thread, WhatsAppWebhookMessage message)
    {
        try
        {
            _logger.LogInformation("WhatsApp 开始下载媒体：MessageId={MessageId}, MediaId={MediaId}, MediaUrl={MediaUrl}, MimeType={MimeType}",
                message.MessageId, message.MediaId, message.MediaUrl, message.MimeType);

            byte[]? data = null;
            string? mimeType = message.MimeType;

            // 优先使用 MediaUrl 下载（Webhook 返回的临时 URL，5 分钟有效）
            if (!string.IsNullOrEmpty(message.MediaUrl))
            {
                _logger.LogDebug("WhatsApp 尝试通过 MediaUrl 下载：Url={Url}", message.MediaUrl);
                var downloaded = await _whatsAppClient.DownloadMediaAsync(message.MediaUrl);
                if (downloaded.HasValue)
                {
                    data = downloaded.Value.data;
                    if (!string.IsNullOrEmpty(downloaded.Value.mimeType))
                        mimeType = downloaded.Value.mimeType;
                    _logger.LogDebug("WhatsApp MediaUrl 下载成功：Size={Size} bytes, MimeType={MimeType}", data.Length, mimeType);
                }
                else
                {
                    _logger.LogWarning("WhatsApp MediaUrl 下载返回空，将尝试 MediaId 方式：Url={Url}", message.MediaUrl);
                }
            }

            // MediaUrl 下载失败时，通过 MediaId 下载（需先获取 URL）
            if (data == null && !string.IsNullOrEmpty(message.MediaId))
            {
                // WhatsApp Cloud API: 通过 Graph API 获取媒体 URL
                var settings = _settingsProvider.Current;
                var mediaUrl = $"{settings.ApiVersion}/{message.MediaId}";
                // 使用完整 URL
                var fullUrl = $"https://graph.facebook.com/{mediaUrl}";
                _logger.LogDebug("WhatsApp 尝试通过 MediaId 下载：Url={Url}", fullUrl);
                var downloaded = await _whatsAppClient.DownloadMediaAsync(fullUrl);
                if (downloaded.HasValue)
                {
                    data = downloaded.Value.data;
                    if (!string.IsNullOrEmpty(downloaded.Value.mimeType))
                        mimeType = downloaded.Value.mimeType;
                    _logger.LogDebug("WhatsApp MediaId 下载成功：Size={Size} bytes, MimeType={MimeType}", data.Length, mimeType);
                }
                else
                {
                    _logger.LogWarning("WhatsApp MediaId 下载也返回空：MediaId={MediaId}", message.MediaId);
                }
            }

            if (data == null || data.Length == 0)
            {
                _logger.LogWarning("WhatsApp 媒体下载最终失败，所有方式均未获取到数据：MessageId={MessageId}, MediaId={MediaId}", message.MessageId, message.MediaId);
                return;
            }

            // 推导文件名和类型
            var fileName = !string.IsNullOrEmpty(message.Filename)
                ? message.Filename
                : $"{message.MessageId}.{GetExtensionFromMimeType(mimeType)}";

            var attachmentType = ClassifyMediaAttachment(mimeType, message.MessageType);

            // 附件根目录统一解析（T000112）：绝对路径原样、相对/缺失锚定 ContentRoot，与上传侧一致
            var uploadPath = AttachmentPathResolver.ResolveBasePath(
                _configuration["Attachment:UploadPath"], _env.ContentRootPath);
            var fileDir = DateTime.UtcNow.ToString("yyyy/MM/dd");
            var storedName = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
            var fullDir = Path.Combine(uploadPath, fileDir);

            // 路径遍历防护
            var fullDirPath = Path.GetFullPath(fullDir);
            var normalizedBase = Path.GetFullPath(uploadPath);
            if (!fullDirPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("WhatsApp 附件路径遍历拦截：FileDir={FileDir}", fileDir);
                return;
            }

            Directory.CreateDirectory(fullDirPath);
            var fullPath = Path.Combine(fullDirPath, storedName);
            await File.WriteAllBytesAsync(fullPath, data);
            _logger.LogDebug("WhatsApp 媒体文件已写入磁盘：Path={Path}, Size={Size} bytes", fullPath, data.Length);

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
            _logger.LogInformation("WhatsApp 媒体附件保存成功：AttachmentId={AttachmentId}, ThreadId={ThreadId}, FileName={FileName}, Size={Size}, Type={Type}",
                attachment.Id, thread.Id, fileName, data.Length, attachmentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp 媒体下载和保存失败：MessageId={MessageId}, MediaId={MediaId}, ThreadId={ThreadId}",
                message.MessageId, message.MediaId, thread.Id);
        }
    }

    private async Task UpdateThreadStatusAsync(WhatsAppStatusUpdate status)
    {
        try
        {
            var threads = await _unitOfWork.Threads.FindAsync(
                t => t.MessageId == status.MessageId && t.State != ThreadState.Deleted);
            var thread = threads.FirstOrDefault();
            if (thread == null)
            {
                _logger.LogDebug("WhatsApp 状态回执未找到对应线程：MessageId={MessageId}", status.MessageId);
                return;
            }

            // 更新线程状态字段（通过 SourceVia 或自定义字段记录）
            // 这里用 Subject 字段记录状态（如果 Subject 为空）或使用日志
            thread.UpdatedAt = DateTime.UtcNow;

            if (status.Status == "failed" && !string.IsNullOrEmpty(status.ErrorMessage))
            {
                _logger.LogWarning("WhatsApp 消息发送失败：MessageId={MessageId}, Error={Error}",
                    status.MessageId, status.ErrorMessage);
            }

            await _unitOfWork.SaveChangesAsync();
            _logger.LogInformation("WhatsApp 状态更新：MessageId={MessageId}, Status={Status}",
                status.MessageId, status.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp 状态回执处理失败：MessageId={MessageId}", status.MessageId);
        }
    }

    private async Task<int> ResolveMailboxIdAsync()
    {
        var settings = _settingsProvider.Current;
        if (settings.MailboxId.HasValue && settings.MailboxId.Value > 0)
        {
            return settings.MailboxId.Value;
        }

        // 回退到第一个活跃邮箱
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
            // 工作流失败不阻塞主流程
            _logger.LogError(ex, "WhatsApp 工作流触发失败：ConversationId={ConversationId}, IsNew={IsNew}",
                conversation.Id, isNewConversation);
        }
    }

    private static string BuildConversationSubject(WhatsAppWebhookMessage message)
    {
        var name = !string.IsNullOrEmpty(message.ProfileName) ? message.ProfileName : message.From;
        return message.MessageType switch
        {
            "text" => $"WhatsApp: {name} - {Truncate(message.TextBody, 30)}",
            "image" => $"WhatsApp: {name} - 图片消息",
            "video" => $"WhatsApp: {name} - 视频消息",
            "audio" => $"WhatsApp: {name} - 语音消息",
            "document" => $"WhatsApp: {name} - 文档消息",
            "sticker" => $"WhatsApp: {name} - 贴纸消息",
            "location" => $"WhatsApp: {name} - 位置消息",
            "interactive" => $"WhatsApp: {name} - 交互消息: {message.InteractiveTitle}",
            "button" => $"WhatsApp: {name} - 按钮消息: {message.ButtonText}",
            _ => $"WhatsApp: {name} - {message.MessageType} 消息"
        };
    }

    private static string BuildThreadHtmlBody(WhatsAppWebhookMessage message)
    {
        return message.MessageType switch
        {
            "text" => $"<div class=\"whatsapp-text\">{HttpUtility.HtmlEncode(message.TextBody ?? string.Empty).Replace("\n", "<br>")}</div>",
            "image" => BuildMediaPlaceholder("📷", "图片消息", message.Caption ?? "客户发送了一张图片，请查看下方附件"),
            "video" => BuildMediaPlaceholder("🎥", "视频消息", message.Caption ?? "客户发送了一段视频，请查看下方附件"),
            "audio" => BuildMediaPlaceholder("🎤", "语音消息", "客户发送了一段语音，请查看下方附件"),
            "document" => BuildMediaPlaceholder("📄", "文档消息", message.Caption ?? $"客户发送了文档: {message.Filename}"),
            "sticker" => BuildMediaPlaceholder("🏷️", "贴纸消息", "客户发送了一张贴纸，请查看下方附件"),
            "interactive" => $"<div class=\"whatsapp-interactive\">🔗 <strong>交互回复：</strong>{HttpUtility.HtmlEncode(message.InteractiveTitle ?? string.Empty)}</div>",
            "button" => $"<div class=\"whatsapp-button\">🔘 <strong>按钮点击：</strong>{HttpUtility.HtmlEncode(message.ButtonText ?? string.Empty)}</div>",
            "location" => BuildLocationHtml(message),
            _ => $"<div class=\"whatsapp-unknown\">[{HttpUtility.HtmlEncode(message.MessageType)} 消息]</div>"
        };
    }

    private static string BuildThreadTextBody(WhatsAppWebhookMessage message)
    {
        return message.MessageType switch
        {
            "text" => message.TextBody ?? string.Empty,
            "image" => $"[图片消息] {message.Caption ?? ""}",
            "video" => $"[视频消息] {message.Caption ?? ""}",
            "audio" => "[语音消息]",
            "document" => $"[文档消息] {message.Filename ?? message.Caption ?? ""}",
            "sticker" => "[贴纸消息]",
            "interactive" => $"[交互回复] {message.InteractiveTitle ?? ""}",
            "button" => $"[按钮点击] {message.ButtonText ?? ""}",
            "location" => $"[位置消息] {message.LocationName ?? ""} {message.LocationAddress ?? ""} ({message.Latitude}, {message.Longitude})",
            _ => $"[{message.MessageType} 消息]"
        };
    }

    private static string BuildMediaPlaceholder(string emoji, string label, string description)
    {
        return $"<div class=\"whatsapp-media\">{emoji} <strong>{label}</strong><br>{HttpUtility.HtmlEncode(description)}</div>";
    }

    private static string BuildLocationHtml(WhatsAppWebhookMessage message)
    {
        var name = !string.IsNullOrEmpty(message.LocationName) ? HttpUtility.HtmlEncode(message.LocationName) : "未知位置";
        var addr = !string.IsNullOrEmpty(message.LocationAddress) ? $"<br>📍 {HttpUtility.HtmlEncode(message.LocationAddress)}" : "";
        var coords = message.Latitude.HasValue && message.Longitude.HasValue
            ? $"<br>🌐 {message.Latitude.Value:F6}, {message.Longitude.Value:F6}"
            : "";
        var dataAttrs = message.Latitude.HasValue && message.Longitude.HasValue
            ? $" data-lat=\"{message.Latitude.Value:F6}\" data-lng=\"{message.Longitude.Value:F6}\" data-name=\"{HttpUtility.HtmlEncode(message.LocationName ?? "位置")}\""
            : "";
        return $"<div class=\"whatsapp-location\"{dataAttrs}>📍 <strong>{name}</strong>{addr}{coords}</div>";
    }

    private static string Truncate(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLen ? text : text[..maxLen] + "...";
    }

    private static string GetExtensionFromMimeType(string? mimeType)
    {
        return mimeType?.ToLowerInvariant() switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            "image/gif" => "gif",
            "audio/aac" => "aac",
            "audio/amr" => "amr",
            "audio/mp3" => "mp3",
            "audio/m4a" => "m4a",
            "audio/ogg" => "ogg",
            "video/mp4" => "mp4",
            "video/3gpp" => "3gp",
            "application/pdf" => "pdf",
            "text/plain" => "txt",
            _ => "bin"
        };
    }

    private static AttachmentType ClassifyMediaAttachment(string? mimeType, string messageType)
    {
        if (messageType == "sticker") return AttachmentType.Image;
        return mimeType?.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/png" or "image/webp" or "image/gif" => AttachmentType.Image,
            "audio/aac" or "audio/amr" or "audio/mp3" or "audio/m4a" or "audio/ogg" => AttachmentType.Audio,
            "video/mp4" or "video/3gpp" => AttachmentType.Video,
            _ => AttachmentType.Other
        };
    }

    /// <summary>联系方式提取最大尝试次数：超过后即使字段缺失也不再尝试，避免每条消息反复跑正则。</summary>
    private const int MaxContactExtractAttempts = 3;

    /// <summary>
    /// 从客户消息文本中提取联系方式（姓名、手机、邮箱），写入 Customer 对应字段。
    /// WhatsApp API 不提供用户邮箱，通过对话引导用户提供后用正则自动识别。
    /// 写入策略：直接覆盖已有字段（客户在对话中提供的最新信息视为权威来源）。
    /// </summary>
    /// <returns>true=成功提取并写入至少一个字段；false=未提取到或全部已存在</returns>
    private async Task<bool> TryExtractContactInfoFromMessageAsync(Customer customer, string text, int conversationId)
    {
        try
        {
            // 前置检查：确保 customer 被 EF Core 跟踪（工作流执行链路可能已 ClearChanges 导致 detach）
            if (_unitOfWork.Customers.FindTracked(customer.Id) == null)
            {
                _logger.LogWarning("WhatsApp 客户实体已被 detach，重新获取 tracked 实体：CustomerId={CustomerId}", customer.Id);
                var reloaded = await _unitOfWork.Customers.GetOrCreateByChannelAsync(
                    customer.Channel ?? "whatsapp", customer.ChannelId ?? string.Empty);
                if (reloaded == null)
                {
                    _logger.LogError("WhatsApp 客户实体重新加载失败：CustomerId={CustomerId}", customer.Id);
                    return false;
                }
                customer = reloaded;
            }

            // 客户当前各字段状态（保留以供调试日志使用，不再用于跳过判断）
            bool hasName = !string.IsNullOrWhiteSpace(customer.FirstName) || !string.IsNullOrWhiteSpace(customer.LastName);
            bool hasPhone = customer.GetPhones().Count > 0;
            bool hasEmail = customer.Emails != null && customer.Emails.Any(e => !string.IsNullOrWhiteSpace(e.EmailAddress));

            // 跳过条件 2：提取尝试次数达到上限（3 次），直接跳过
            if (customer.ContactExtractAttempts >= MaxContactExtractAttempts)
            {
                _logger.LogDebug("WhatsApp 联系方式提取已达最大尝试次数，跳过：CustomerId={CustomerId}, Attempts={Attempts}, Max={Max}",
                    customer.Id, customer.ContactExtractAttempts, MaxContactExtractAttempts);
                return false;
            }

            // 提取尝试次数 +1（无论本次是否能提取到有效信息，只要到了提取步骤就算一次尝试）
            customer.ContactExtractAttempts++;

            var info = ContactInfoExtractor.Extract(text, _logger);
            if (!info.HasAny)
            {
                // 文本中没提取到任何联系方式，但尝试次数已自增，需要持久化次数
                customer.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
                return false;
            }

            bool changed = false;

            // 1. 姓名：直接覆盖已有姓名（客户在对话中提供的姓名视为最新信息）
            if (!string.IsNullOrWhiteSpace(info.Name))
            {
                var (lastName, firstName) = ChineseNameSplitter.Split(info.Name);
                customer.LastName = lastName;
                customer.FirstName = firstName;
                changed = true;
                _logger.LogDebug("WhatsApp 对话姓名提取成功：CustomerId={CustomerId}, Name={Name}, LastName={LastName}, FirstName={FirstName}",
                    customer.Id, info.Name, lastName, firstName);
            }

            // 2. 手机号：直接替换已有手机号（客户在对话中提供的手机号视为最新信息）
            if (!string.IsNullOrWhiteSpace(info.Phone))
            {
                var phones = customer.GetPhones();
                phones.Clear();  // 清空已有手机号，实现替换
                phones.Add(new CustomerPhone
                {
                    Value = info.Phone,
                    Type = 4  // 4=手机
                });
                customer.SetPhones(phones);
                changed = true;
                _logger.LogDebug("WhatsApp 对话手机号提取成功：CustomerId={CustomerId}, Phone={Phone}", customer.Id, info.Phone);
            }

            // 3. 邮箱：直接替换已有邮箱（客户在对话中提供的邮箱视为最新信息）
            customer.Emails ??= new List<Email>();
            if (!string.IsNullOrWhiteSpace(info.Email))
            {
                // 邮箱全局唯一约束检查：避免与其他客户（含已删除）的邮箱冲突
                var emailOwned = await _unitOfWork.Customers.IsEmailOwnedByAnyCustomerAsync(info.Email, customer.Id);
                if (emailOwned)
                {
                    _logger.LogWarning("WhatsApp 对话邮箱已被其他客户占用，跳过添加：CustomerId={CustomerId}, Email={Email}", customer.Id, info.Email);
                }
                else
                {
                    customer.Emails.Clear();  // 清空已有邮箱，实现替换
                    customer.Emails.Add(new Email
                    {
                        EmailAddress = info.Email,
                        Type = 1,
                        CustomerId = customer.Id
                    });
                    changed = true;
                    _logger.LogDebug("WhatsApp 对话邮箱提取成功：CustomerId={CustomerId}, Email={Email}", customer.Id, info.Email);

                    // 将提取到的邮箱回填到会话的 CustomerEmail，确保会话关闭时能发送通知邮件
                    // 仅当会话当前 CustomerEmail 为空时才回填（已有邮箱不覆盖）
                    // 使用 GetByIdForUpdateAsync 获取追踪实体，修改后随 SaveChangesAsync 一起持久化
                    var conversationToUpdate = await _unitOfWork.Conversations.GetByIdForUpdateAsync(conversationId);
                    if (conversationToUpdate != null && string.IsNullOrWhiteSpace(conversationToUpdate.CustomerEmail))
                    {
                        conversationToUpdate.CustomerEmail = info.Email;
                        _logger.LogInformation("WhatsApp 会话邮箱回填成功：ConversationId={ConversationId}, CustomerEmail={Email}",
                            conversationId, info.Email);
                    }
                }
            }

            // 尝试次数已自增，即使 changed=false 也要 SaveChanges 持久化次数
            customer.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();

            if (changed)
            {
                _logger.LogInformation("WhatsApp 对话联系方式提取成功：CustomerId={CustomerId}, Name={Name}, Phone={Phone}, Email={Email}, Attempts={Attempts}",
                    customer.Id, info.Name, info.Phone, info.Email, customer.ContactExtractAttempts);
                return true;
            }

            _logger.LogDebug("WhatsApp 联系方式提取未找到缺失字段，但已记录尝试次数：CustomerId={CustomerId}, Attempts={Attempts}",
                customer.Id, customer.ContactExtractAttempts);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp 对话联系方式提取失败：CustomerId={CustomerId}", customer.Id);
            _unitOfWork.ClearChanges();
            return false;
        }
    }

    /// <summary>
    /// 基于 WhatsApp 提供的 wa_id 补全客户资料。
    /// 说明（与官方文档一致）：
    ///   - WhatsApp Cloud API 仅通过 Webhook 返回 contacts[].profile.name（客户在 WhatsApp 个人资料中的"显示姓名"）
    ///     以及 wa_id（E.164 格式手机号，含国家区号，无 + 号前缀）。
    ///   - Meta 出于隐私合规（GDPR/CCPA）未提供 email、头像、地址、职位等个人信息。
    ///   - 因此此处仅将 wa_id 作为手机号写入 Customer.Phones，避免重复赋值。
    /// 失败全部吞掉，不影响主流程。
    /// </summary>
    private async Task TryEnrichCustomerProfileAsync(Customer customer, string waId, string? profileName)
    {
        try
        {
            bool hasPhone = !string.IsNullOrWhiteSpace(customer.Phones) && customer.GetPhones().Count > 0;
            bool hasCountry = !string.IsNullOrWhiteSpace(customer.Country);
            bool hasName = !string.IsNullOrWhiteSpace(customer.FirstName);

            // 三项都已填充时无需调用
            if (hasPhone && hasCountry && hasName || string.IsNullOrWhiteSpace(waId))
                return;

            // wa_id 形如 "8613800138000"：前几位为国家区号，其余为本地号码。
            string full = waId.Trim();
            if (full.Length < 7)
                return; // 过短，不可能是合法手机号

            // 分离国家码：国家码 1~3 位，遵循 E.164 标准。
            // 简单策略：
            //   - 首位 1 → 国家码 = 1（北美编号计划）
            //   - 首位 7 → 国家码 = 7（俄罗斯/哈萨克斯坦）
            //   - 否则尝试匹配常见 2/3 位代码；无法确定时默认用前 2 位兜底，
            //     最终也会在手机号显示中呈现完整号，不会丢号。
            string countryCode;
            string localNumber;
            if (full.StartsWith('1'))
            {
                countryCode = "1";
                localNumber = full[1..];
            }
            else if (full.StartsWith('7'))
            {
                countryCode = "7";
                localNumber = full[1..];
            }
            else if (full.Length >= 11)
            {
                countryCode = full[..2];
                localNumber = full[2..];
            }
            else
            {
                // 号码较短时直接用全部作为 localNumber，N = "+" + waId 整体返回
                countryCode = string.Empty;
                localNumber = full;
            }

            bool changed = false;

            // 1. 手机号（仅当客户无任何手机号时写入）
            if (!hasPhone)
            {
                var phones = customer.GetPhones();
                phones.Add(new CustomerPhone
                {
                    Value = localNumber,
                    Type = 1, // Mobile
                    N = $"+{full}"
                });
                customer.SetPhones(phones);
                changed = true;
                _logger.LogDebug("WhatsApp 客户手机号补全：CustomerId={CustomerId}, CountryCode={CountryCode}, LocalNumber={LocalNumber}",
                    customer.Id, countryCode, localNumber);
            }

            // 2. 国家码 → Country（仅当 Country 为空时写入，格式 "+{code}"）
            if (!hasCountry && !string.IsNullOrEmpty(countryCode))
            {
                customer.Country = $"+{countryCode}";
                changed = true;
                _logger.LogDebug("WhatsApp 客户国家码补全：CustomerId={CustomerId}, Country={Country}",
                    customer.Id, customer.Country);
            }

            // 3. 姓名（仅当 FirstName 或 LastName 为空且 Webhook 提供了 profile.name 时写入）
            //    用中文姓名拆分工具分别填入姓和名
            if (!hasName && !string.IsNullOrWhiteSpace(profileName))
            {
                var (lastName, firstName) = ChineseNameSplitter.Split(profileName);
                customer.LastName = lastName;
                customer.FirstName = firstName;
                changed = true;
                _logger.LogDebug("WhatsApp 客户姓名补全：CustomerId={CustomerId}, Name={Name}, LastName={LastName}, FirstName={FirstName}",
                    customer.Id, profileName, lastName, firstName);
            }

            if (changed)
            {
                customer.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp 客户资料补全失败：CustomerId={CustomerId}, WaId={WaId}", customer.Id, waId);
        }
    }

    private async Task MarkAsReadFireAndForgetAsync(string? messageId)
    {
        try
        {
            if (!string.IsNullOrEmpty(messageId))
                await _whatsAppClient.MarkAsReadAsync(messageId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp 标记已读失败：MessageId={MessageId}", messageId);
        }
    }

    /// <summary>
    /// 发送自动回复欢迎语并写入会话线程（SourceVia=User，同步显示在会话记录中）。
    /// 发送或写入失败均记录日志返回 false，不阻塞主流程（优雅降级）。
    /// </summary>
    /// <param name="waId">接收者 wa_id（消息 From）</param>
    /// <param name="conversationId">会话 ID</param>
    /// <param name="customerId">客户 ID</param>
    /// <param name="autoReplyMessage">自动回复内容</param>
    /// <param name="conversationUpdatedAt">会话更新时间（用于保证回复线程时间戳晚于客户消息）</param>
    private async Task<bool> SendAutoReplyAsync(string waId, int conversationId, int customerId, string autoReplyMessage, DateTime conversationUpdatedAt)
    {
        try
        {
            var sentId = await _whatsAppClient.SendTextMessageAsync(waId, autoReplyMessage);
            if (string.IsNullOrEmpty(sentId))
            {
                _logger.LogWarning("WhatsApp 自动回复发送失败：WaId={WaId}", waId);
                return false;
            }
            _logger.LogInformation("WhatsApp 自动回复发送成功：WaId={WaId}, MessageId={MessageId}", waId, sentId);

            // 写入系统消息到会话线程，同步显示在会话记录中（参考联系方式收集引导的写入模式）
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
                    SourceType = SourceType.WhatsApp,
                    From = "whatsapp@system",
                    To = waId,
                    Body = autoReplyMessage,
                    SendStatus = SendStatus.Sent,
                    IsInternal = false,
                    MessageId = $"whatsapp-auto-reply-{Guid.NewGuid():N}",
                    CreatedAt = replyTime,
                    UpdatedAt = replyTime
                };
                await _unitOfWork.Threads.AddAsync(replyThread);
                await _unitOfWork.SaveChangesAsync();
                _logger.LogInformation("WhatsApp 自动回复已写入会话线程：ConversationId={ConversationId}", conversationId);
            }
            catch (Exception thrEx)
            {
                _logger.LogWarning(thrEx, "WhatsApp 自动回复写入会话线程失败：ConversationId={ConversationId}", conversationId);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp 自动回复发送异常：WaId={WaId}", waId);
            return false;
        }
    }
}
