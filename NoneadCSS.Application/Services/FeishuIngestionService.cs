using System.Net;
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
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 飞书消息接入服务 - 将飞书事件订阅推送的消息转换为会话(Conversation)+线程(Thread)。
/// 严格复刻 WeChatIngestionService 的流程：
/// 解析 → 去重 → 客户匹配 → 会话匹配(新建/追加回复) → 事务持久化 → 素材下载 → 触发后置工作流。
/// 飞书与微信的关键差异：无 48 小时客服消息窗口、无被动回复、自动回复通过 API 异步发送。
/// </summary>
public class FeishuIngestionService : IFeishuIngestionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowService _workflowService;
    private readonly IFeishuSettingsProvider _settingsProvider;
    private readonly IFeishuClient _feishuClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ILogger<FeishuIngestionService> _logger;
    private readonly string _frontendUrl;
    private readonly string _attachmentBasePath;
    /// <summary>飞书消息匹配已有会话的时间窗口：48 小时（与微信对齐）</summary>
    private readonly TimeSpan _conversationMatchWindow;

    /// <summary>飞书媒体消息类型集合，需要下载素材</summary>
    private static readonly HashSet<string> MediaMsgTypes = new(StringComparer.OrdinalIgnoreCase) { "image", "file", "audio", "media" };

    public FeishuIngestionService(
        IUnitOfWork unitOfWork,
        IWorkflowService workflowService,
        IFeishuSettingsProvider settingsProvider,
        IFeishuClient feishuClient,
        IHttpClientFactory httpClientFactory,
        IDistributedLockService distributedLockService,
        ILogger<FeishuIngestionService> logger,
        IConfiguration configuration,
        IOptions<IngestionMatchOptions> ingestionMatchOptions,
        IHostEnvironment env)
    {
        _unitOfWork = unitOfWork;
        _workflowService = workflowService;
        _settingsProvider = settingsProvider;
        _feishuClient = feishuClient;
        _httpClientFactory = httpClientFactory;
        _distributedLockService = distributedLockService;
        _logger = logger;
        _frontendUrl = configuration["App:FrontendUrl"]
            ?? throw new InvalidOperationException("配置 App:FrontendUrl 未设置，飞书消息卡片链接无法生成。请在 appsettings 或环境变量中配置 App:FrontendUrl。");
        // 附件根目录统一解析（T000112）：绝对路径原样、相对/缺失锚定 ContentRoot，与上传侧一致
        _attachmentBasePath = AttachmentPathResolver.ResolveBasePath(
            configuration["Attachment:UploadPath"], env.ContentRootPath);
        _conversationMatchWindow = TimeSpan.FromHours(ingestionMatchOptions.Value.FeishuMatchWindowHours);
    }

    private FeishuOptions Options => _settingsProvider.Current;

    /// <inheritdoc />
    public void ResetTracking() => _unitOfWork.ClearChanges();

    public async Task<FeishuIngestionResult> ProcessEventAsync(string rawBody)
    {
        var result = new FeishuIngestionResult();

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            _logger.LogWarning("飞书事件处理跳过：请求体为空");
            result.ErrorMessage = "请求体为空";
            return result;
        }

        _logger.LogInformation("飞书收到事件推送：BodyLength={BodyLength}", rawBody.Length);

        // 1. 首次解析，判断是否为挑战验证或加密载荷
        FeishuEventDto? dto;
        try
        {
            // 检查是否为加密载荷：{ "encrypt": "xxx" }
            string jsonToParse = rawBody;
            try
            {
                using var probe = JsonDocument.Parse(rawBody);
                if (probe.RootElement.TryGetProperty("encrypt", out var enc)
                    && enc.ValueKind == JsonValueKind.String
                    && Options.EncryptEnabled
                    && !string.IsNullOrEmpty(Options.EncryptKey))
                {
                    var encrypted = enc.GetString();
                    var decrypted = FeishuMessageParser.DecryptPayload(Options.EncryptKey!, encrypted!);
                    if (!string.IsNullOrEmpty(decrypted))
                    {
                        jsonToParse = decrypted;
                    }
                    else
                    {
                        result.ErrorMessage = "事件载荷解密失败";
                        _logger.LogWarning("飞书事件解密失败：EncryptKey 可能不匹配");
                        return result;
                    }
                }
            }
            catch (JsonException)
            {
                // 非 JSON（理论上不会发生），按原样处理
            }

            dto = FeishuMessageParser.ParseEvent(jsonToParse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "飞书事件解析异常");
            result.ErrorMessage = "事件解析失败";
            return result;
        }

        if (dto == null)
        {
            _logger.LogWarning("飞书事件解析失败：返回 null dto");
            result.ErrorMessage = "事件解析失败";
            return result;
        }

        // 2. 挑战验证请求：直接返回（控制器会回显 challenge）
        if (dto.IsChallenge)
        {
            result.IsEvent = true;
            // 通过 ErrorMessage 字段回传 challenge 给控制器
            result.ErrorMessage = dto.Challenge;
            _logger.LogInformation("飞书 URL 验证挑战请求：Challenge={Challenge}", dto.Challenge);
            return result;
        }

        // 3. 校验事件 token，防止伪冒
        if (!string.IsNullOrEmpty(Options.VerificationToken)
            && !string.Equals(dto.Token, Options.VerificationToken, StringComparison.Ordinal))
        {
            _logger.LogWarning("飞书事件 token 校验失败：ExpectedTokenLength={ExpectedLen}, ActualTokenLength={ActualLen}",
                Options.VerificationToken?.Length ?? 0, dto.Token?.Length ?? 0);
            result.ErrorMessage = "token 校验失败";
            return result;
        }

        // 4. 非消息事件（如用户加入群聊等）仅记录日志，不创建会话
        if (dto.IsEvent)
        {
            result.IsEvent = true;
            _logger.LogInformation("收到飞书非消息事件：EventType={EventType}, EventId={EventId}",
                dto.EventType, dto.EventId);
            return result;
        }

        _logger.LogInformation("飞书消息事件详情：MessageId={MessageId}, EventType={EventType}, MessageType={MessageType}, SenderOpenId={SenderOpenId}, ChatType={ChatType}",
            dto.MessageId, dto.EventType, dto.MessageType, dto.SenderOpenId, dto.ChatType);

        // 仅处理 p2p 单聊消息，群聊消息暂不接入
        if (!string.IsNullOrEmpty(dto.ChatType)
            && !dto.ChatType.Equals("p2p", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("飞书群聊消息暂不处理：ChatType={ChatType}, MessageId={MessageId}",
                dto.ChatType, dto.MessageId);
            return result;
        }

        // 确定关联邮箱
        var mailboxId = await ResolveMailboxIdAsync();
        if (mailboxId == null)
        {
            _logger.LogWarning("飞书消息处理失败：无可用邮箱，请配置 Feishu:MailboxId 或确保至少有一个活跃邮箱");
            return result;
        }

        // 事务内处理会话与线程
        var ingestionContext = await IngestConversationInTransactionAsync(dto, mailboxId.Value);

        if (ingestionContext == null)
        {
            result.IsDuplicate = true;
            return result;
        }

        result.NewConversationCount = ingestionContext.IsNewConversation ? 1 : 0;
        result.ReplyCount = ingestionContext.IsNewConversation ? 0 : 1;
        result.ConversationId = ingestionContext.Conversation.Id;

        // 事务提交后下载媒体素材并关联附件（外部 API 调用，不影响主流程）
        if (MediaMsgTypes.Contains(dto.MessageType ?? string.Empty))
        {
            await DownloadAndSaveMediaAsync(dto, ingestionContext.Conversation, ingestionContext.Thread, mailboxId.Value);
        }

        // 事务提交后触发后置工作流
        await TriggerPostIngestionWorkflowsAsync(ingestionContext.Conversation, ingestionContext.IsNewConversation);

        // 新会话且启用自动回复时，通过 API 异步发送欢迎语（飞书无被动回复机制）
        if (ingestionContext.IsNewConversation
            && Options.AutoReplyEnabled
            && !string.IsNullOrWhiteSpace(Options.AutoReplyMessage)
            && !string.IsNullOrEmpty(dto.SenderOpenId))
        {
            await SendAutoReplyAsync(dto.SenderOpenId, Options.AutoReplyMessage);
            result.AutoReplyContent = Options.AutoReplyMessage;
        }

        // 联系方式收集：提取适用于所有消息（新会话+回复），引导仅限新会话
        //                先从客户当前消息文本尝试提取姓名/手机/邮箱（客户可能直接回复了）
        //                提取方法内部已有最大尝试次数限制（3 次）和字段完整性检查，不会过度提取
        //                新会话且缺任一字段时，发送引导消息（受 ContactCollectionEnabled 开关控制）
        if (!Options.ContactCollectionEnabled)
        {
            _logger.LogInformation("飞书客户信息收集跳过：ContactCollectionEnabled={Enabled}",
                Options.ContactCollectionEnabled);
            return result;
        }

        var customerRef = await _unitOfWork.Threads.GetQueryable()
            .Where(t => t.Id == ingestionContext.Thread.Id)
            .Include(t => t.Conversation!)
                .ThenInclude(c => c!.Customer)
            .Select(t => t.Conversation!.Customer)
            .FirstOrDefaultAsync();

        if (customerRef == null)
        {
            _logger.LogWarning("飞书客户信息收集：无法加载客户实体，ThreadId={ThreadId}", ingestionContext.Thread.Id);
            return result;
        }

        // 1. 从文本消息中提取（文本消息才有 BodyText）
        bool contactExtracted = false;
        if (!string.IsNullOrWhiteSpace(dto.BodyText))
        {
            contactExtracted = await TryExtractContactInfoFromMessageAsync(customerRef, dto.BodyText, ingestionContext.Conversation.Id);
        }

        // 2. 判断客户当前是否缺任一字段
        bool hasName = !string.IsNullOrWhiteSpace(customerRef.FirstName) || !string.IsNullOrWhiteSpace(customerRef.LastName);
        bool hasPhone = customerRef.GetPhones().Count > 0;
        bool hasEmail = customerRef.Emails != null && customerRef.Emails.Any(e => !string.IsNullOrWhiteSpace(e.EmailAddress));

        _logger.LogInformation("飞书客户信息收集判断：OpenId={OpenId}, CustomerId={CustomerId}, HasName={HasName}, HasPhone={HasPhone}, HasEmail={HasEmail}, ContactExtracted={ContactExtracted}, IsNew={IsNew}",
            dto.SenderOpenId, customerRef.Id, hasName, hasPhone, hasEmail, contactExtracted, ingestionContext.IsNewConversation);

        // 3. 新会话 + 缺任一字段 +（本次没提取到或提取后仍缺字段）→ 发送引导
        if ((!contactExtracted || !hasName || !hasPhone || !hasEmail)
            && ingestionContext.IsNewConversation
            && (!hasName || !hasPhone || !hasEmail))
        {
            if (string.IsNullOrEmpty(dto.SenderOpenId))
            {
                _logger.LogWarning("飞书客户信息收集：SenderOpenId 为空，无法发送引导消息，CustomerId={CustomerId}", customerRef.Id);
            }
            else
            {
                try
                {
                    var contactGuidance = "为了更好地为您服务，方便后续发送相关资料，请您回复您的姓名，手机，邮箱地址，谢谢！";
                    var guidanceSent = await SendAutoReplyAsync(dto.SenderOpenId, contactGuidance);
                    if (guidanceSent)
                    {
                        _logger.LogInformation("已发送飞书联系方式收集引导：OpenId={OpenId}, CustomerId={CustomerId}, 缺字段 Name={NeedName}, Phone={NeedPhone}, Email={NeedEmail}",
                            dto.SenderOpenId, customerRef.Id, !hasName, !hasPhone, !hasEmail);
                        // 写入系统消息到会话线程（Type=Message, SourceVia=User），同步显示在会话记录中
                        try
                        {
                            var conv = ingestionContext.Conversation;
                            // 确保引导消息时间戳晚于客户消息（客户消息 CreatedAt 来自渠道服务器时间，可能与服务端 UTC 有偏差）
                            var guidanceTime = DateTime.UtcNow;
                            var convUpdated = conv.UpdatedAt;
                            if (convUpdated >= guidanceTime) guidanceTime = convUpdated.AddSeconds(1);

                            var guidanceThread = new ConversationThread
                            {
                                ConversationId = conv.Id,
                                CustomerId = customerRef.Id,
                                Type = ThreadType.Message,
                                SourceVia = SourceVia.User,
                                SourceType = SourceType.Feishu,
                                From = "feishu@system",
                                To = dto.SenderOpenId,
                                Body = contactGuidance,
                                SendStatus = SendStatus.Sent,
                                IsInternal = false,
                                MessageId = $"feishu-contact-guidance-{Guid.NewGuid():N}",
                                CreatedAt = guidanceTime,
                                UpdatedAt = guidanceTime
                            };
                            await _unitOfWork.Threads.AddAsync(guidanceThread);
                            await _unitOfWork.SaveChangesAsync();
                            _logger.LogInformation("飞书联系方式收集引导已写入会话线程：ConversationId={ConversationId}",
                                conv.Id);
                        }
                        catch (Exception thrEx)
                        {
                            _logger.LogWarning(thrEx, "飞书联系方式收集引导写入会话线程失败：ConversationId={ConversationId}",
                                ingestionContext.Conversation.Id);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("飞书联系方式收集引导发送失败：OpenId={OpenId}", dto.SenderOpenId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "发送飞书联系方式收集引导失败：OpenId={OpenId}", dto.SenderOpenId);
                }
            }
        }
        else if (ingestionContext.IsNewConversation && hasName && hasPhone && hasEmail)
        {
            // 内部员工：TryEnrich 已通过飞书 API 补全全部字段，无需发送引导消息
            _logger.LogInformation("飞书客户信息收集跳过：客户资料已齐全（内部员工），CustomerId={CustomerId}, OpenId={OpenId}",
                customerRef.Id, dto.SenderOpenId);
        }
        else if (!ingestionContext.IsNewConversation)
        {
            _logger.LogDebug("飞书客户信息收集：非新会话，仅提取不发送引导：OpenId={OpenId}, ContactExtracted={ContactExtracted}",
                dto.SenderOpenId, contactExtracted);
        }

        return result;
    }

    /// <summary>
    /// 在事务内完成会话匹配/创建、线程创建。
    /// </summary>
    private async Task<FeishuIngestionContext?> IngestConversationInTransactionAsync(
        FeishuEventDto dto, int mailboxId)
    {
        // 用 "fs_{MessageId}" 作为 MessageId 存入 Threads.MessageId
        var messageId = BuildFeishuMessageId(dto);

        // TD-06：在进入事务前获取分布式锁，串行化同一飞书消息（MessageId）的并发摄入，消除 TOCTOU 竞态。
        // 锁必须在事务外获取/释放：DistributedLockService 与 IUnitOfWork 共享同一 DbContext，
        // 若在事务内获取，锁的 INSERT 会并入当前事务，导致后续 Rollback 时把锁一并回滚，锁失效。
        var lockKey = $"feishu_ingest:{messageId}";
        var lockHandle = await _distributedLockService.TryAcquireAsync(lockKey, timeoutMs: 0);
        if (lockHandle == null)
        {
            _logger.LogInformation("飞书消息正在被其他实例处理，跳过：MessageId={MessageId}", dto.MessageId);
            return null;
        }

        try
        {
            using var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                // 1. 消息去重：用 "fs_{MessageId}" 作为 MessageId 存入 Threads.MessageId
                _logger.LogDebug("飞书消息去重检查：DedupKey={DedupKey}, MessageId={MessageId}, EventId={EventId}",
                    messageId, dto.MessageId, dto.EventId);
                var existingThread = await _unitOfWork.Threads.FindByMessageIdAsync(messageId);
            if (existingThread != null)
            {
                _logger.LogInformation("飞书消息已存在，跳过：MessageId={MessageId}, ExistingThreadId={ThreadId}",
                    dto.MessageId, existingThread.Id);
                await transaction.RollbackAsync();
                return null;
            }

            // 2. 客户匹配/创建（按 OpenId）
            var openId = dto.SenderOpenId ?? string.Empty;
            _logger.LogDebug("飞书客户匹配/创建：OpenId={OpenId}", openId);
            var customer = await _unitOfWork.Customers.GetOrCreateByChannelAsync("feishu", openId);
            if (customer == null)
            {
                _logger.LogWarning("无法获取或创建飞书客户，跳过：OpenId={OpenId}", openId);
                await transaction.RollbackAsync();
                return null;
            }
            _logger.LogDebug("飞书客户匹配成功：CustomerId={CustomerId}, OpenId={OpenId}", customer.Id, openId);

            // 2.1 补全客户资料：调用飞书 /contact/v3/users/{open_id} 获取姓名、邮箱、手机号、头像、职位等。
            // 需在飞书开放平台为应用开通通讯录权限：
            //   - contact:contact.base:readonly 或 contact:user.base:readonly（基础信息：name、en_name、avatar）
            //   - contact:user.email:readonly（邮箱字段）
            //   - contact:user.phone:readonly（手机号字段，仅企业内部应用）
            // 任何失败（权限不足、用户未设置、网络异常）都被吞掉，不阻塞主流程。
            await TryEnrichCustomerProfileAsync(customer, openId);

            // 3. 尝试匹配已有会话（同 OpenId + 时间窗口内未关闭的会话）
            var existingConversation = await FindRelatedFeishuConversationAsync(openId, mailboxId);
            _logger.LogDebug("飞书会话匹配结果：OpenId={OpenId}, FoundExisting={FoundExisting}",
                openId, existingConversation != null);

            Conversation conversation;
            ConversationThread thread;
            bool isNewConversation;

            if (existingConversation != null)
            {
                (conversation, thread) = await AddReplyToExistingConversationAsync(dto, customer.Id, existingConversation.Id, messageId);
                isNewConversation = false;
                _logger.LogInformation("飞书会话 {ConversationId} 收到客户回复，ThreadId={ThreadId}, MessageId={MessageId}",
                    conversation.Id, thread.Id, dto.MessageId);
            }
            else
            {
                (conversation, thread) = await CreateNewConversationAsync(dto, customer, mailboxId, messageId);
                isNewConversation = true;
                _logger.LogInformation("创建新飞书会话：ConversationId={ConversationId}, Subject={Subject}, ThreadId={ThreadId}, MessageId={MessageId}",
                    conversation.Id, conversation.Subject, thread.Id, dto.MessageId);
            }

            await _unitOfWork.Threads.AddAsync(thread);

            if (isNewConversation)
            {
                await _unitOfWork.Conversations.AddAsync(conversation);
            }

            await _unitOfWork.SaveChangesAsync();

            // 新客户在 SaveChanges 后才获得实际 ID，需回填 CreatedByCustomerId
            if (isNewConversation && conversation.CreatedByCustomerId == 0 && customer.Id > 0)
            {
                conversation.CreatedByCustomerId = customer.Id;
                await _unitOfWork.SaveChangesAsync();
            }

            await transaction.CommitAsync();

            _logger.LogInformation("飞书消息处理事务提交成功：ConversationId={ConversationId}, ThreadId={ThreadId}, IsNew={IsNew}",
                conversation.Id, thread.Id, isNewConversation);

            return new FeishuIngestionContext
            {
                Conversation = conversation,
                Thread = thread,
                IsNewConversation = isNewConversation
            };
        }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // TD-06 幂等兜底：若因罕见竞态绕过了锁导致并发写入重复 MessageId，
                // 唯一约束冲突时回滚 + 清理跟踪 + 重查，若该消息已存在则视为重复返回 null；否则上报真实异常。
                await transaction.SafeRollbackAsync();
                _unitOfWork.ClearChanges();
                var duplicate = await _unitOfWork.Threads.FindByMessageIdAsync(messageId);
                if (duplicate != null)
                {
                    _logger.LogInformation("飞书消息并发写入重复，按幂等处理跳过：MessageId={MessageId}, ThreadId={ThreadId}",
                        dto.MessageId, duplicate.Id);
                    return null;
                }
                _logger.LogError(ex, "飞书消息事务唯一约束冲突且非重复，上报：MessageId={MessageId}", dto.MessageId);
                throw;
            }
            catch (Exception ex)
            {
                await transaction.SafeRollbackAsync();
                _logger.LogError(ex, "飞书消息处理事务失败：OpenId={OpenId}, MessageId={MessageId}, EventId={EventId}",
                    dto.SenderOpenId, dto.MessageId, dto.EventId);
                throw;
            }
        }
        finally
        {
            await lockHandle.DisposeAsync();
        }
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

    /// <summary>
    /// 为已存在的会话创建回复线程并更新会话状态。
    /// </summary>
    private async Task<(Conversation conversation, ConversationThread thread)> AddReplyToExistingConversationAsync(
        FeishuEventDto dto, int customerId, int existingConversationId, string messageId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdTrackedAsync(existingConversationId);
        if (conversation == null)
        {
            throw new InvalidOperationException($"关联会话已被删除：ConversationId={existingConversationId}");
        }

        var thread = CreateThreadFromFeishuEvent(dto, customerId, messageId);
        thread.ConversationId = conversation.Id;

        // 已关闭的会话收到新回复时重新打开
        if (conversation.Status == ConversationStatus.Closed)
        {
            conversation.Status = ConversationStatus.Active;
        }

        var messageTime = ParseFeishuTime(dto.MessageCreateTime ?? dto.CreateTime);
        // 防止渠道服务器时钟超前于本服务端，导致客户消息时间戳"未来化"
        if (messageTime > DateTime.UtcNow) messageTime = DateTime.UtcNow;
        conversation.LastReplyAt = messageTime;
        conversation.LastReplyFrom = SourceVia.Customer;
        conversation.UpdatedAt = messageTime;
        conversation.SetPreview(thread.Body);
        conversation.ThreadsCount++;

        return (conversation, thread);
    }

    /// <summary>
    /// 创建新会话和首条线程，并分配默认文件夹。
    /// </summary>
    private async Task<(Conversation conversation, ConversationThread thread)> CreateNewConversationAsync(
        FeishuEventDto dto, Customer customer, int mailboxId, string messageId)
    {
        var messageTime = ParseFeishuTime(dto.MessageCreateTime ?? dto.CreateTime);
        // 防止渠道服务器时钟超前于本服务端，导致客户消息时间戳"未来化"
        if (messageTime > DateTime.UtcNow) messageTime = DateTime.UtcNow;
        var subject = BuildConversationSubject(dto);

        var conversation = new Conversation
        {
            Number = await _unitOfWork.Conversations.GetNextNumberAsync(),
            MailboxId = mailboxId,
            Customer = customer, // 使用导航属性而非 CustomerId = customer.Id，确保新客户（Id=0）时 EF Core 能正确做 FK fixup
            CustomerEmail = string.Empty, // 飞书客户无邮箱
            Subject = subject,
            Type = ConversationType.Chat, // 飞书归类为 Chat 类型
            Status = ConversationStatus.Active,
            State = ConversationState.Published,
            SourceVia = SourceVia.Customer,
            SourceType = SourceType.Feishu,
            CreatedAt = messageTime,
            UpdatedAt = messageTime,
            LastReplyAt = messageTime,
            LastReplyFrom = SourceVia.Customer,
            ThreadsCount = 1,
            HasAttachments = MediaMsgTypes.Contains(dto.MessageType ?? string.Empty),
            CreatedByCustomerId = customer.Id
        };

        conversation.SetPreview(dto.BodyText ?? string.Empty);

        var thread = CreateThreadFromFeishuEvent(dto, customer.Id, messageId);
        thread.IsFirst = true;
        thread.State = ThreadState.Active;

        conversation.Threads.Add(thread);

        await AssignFolderAsync(conversation, mailboxId);

        return (conversation, thread);
    }

    /// <summary>
    /// 从飞书事件创建线程实体。Body 使用 HTML 格式（前端用 dangerouslySetInnerHTML 渲染）。
    /// </summary>
    private static ConversationThread CreateThreadFromFeishuEvent(FeishuEventDto dto, int customerId, string messageId)
    {
        var messageTime = ParseFeishuTime(dto.MessageCreateTime ?? dto.CreateTime);
        // 防止渠道服务器时钟超前于本服务端，导致客户消息时间戳"未来化"
        if (messageTime > DateTime.UtcNow) messageTime = DateTime.UtcNow;

        return new ConversationThread
        {
            Type = ThreadType.Customer,
            State = ThreadState.Active,
            Body = BuildThreadHtmlBody(dto),
            Subject = BuildConversationSubject(dto),
            From = dto.SenderOpenId ?? string.Empty, // 存储 OpenId
            To = dto.AppId ?? string.Empty, // 存储飞书应用 App ID
            MessageId = messageId, // "fs_{MessageId}"，用于去重
            IsInternal = false,
            HasAttachments = MediaMsgTypes.Contains(dto.MessageType ?? string.Empty),
            IsFirst = false,
            SourceVia = SourceVia.Customer,
            SourceType = SourceType.Feishu,
            CreatedByCustomerId = customerId,
            CreatedAt = messageTime,
            UpdatedAt = messageTime
        };
    }

    /// <summary>
    /// 构建线程 HTML 正文（前端用 dangerouslySetInnerHTML 渲染）。
    /// 文本消息转义并保留换行；媒体消息使用语义化 class 的卡片式占位（附件下载后前端展示缩略图）。
    /// 前端 ThreadList 组件通过 Tailwind 任意选择器为以下 class 提供样式：
    ///   .feishu-text / .feishu-image / .feishu-media / .feishu-media-icon / .feishu-media-info
    ///   .feishu-media-title / .feishu-media-desc / .feishu-unknown
    /// </summary>
    private static string BuildThreadHtmlBody(FeishuEventDto dto)
    {
        var msgType = (dto.MessageType ?? string.Empty).ToLowerInvariant();
        var content = dto.Content ?? string.Empty;

        return msgType switch
        {
            "text" => $"<div class=\"feishu-text\">{HttpUtility.HtmlEncode(dto.BodyText ?? string.Empty).Replace("\n", "<br>")}</div>",
            "image" => BuildMediaPlaceholder("feishu-image", "📷", "图片消息", "客户发送了一张图片，请查看下方附件"),
            "audio" => BuildMediaPlaceholder("feishu-media", "🎤", "语音消息", "客户发送了一段语音，请查看下方附件"),
            "media" => BuildMediaPlaceholder("feishu-media", "🎬", "视频消息", "客户发送了一段视频，请查看下方附件"),
            "file" => $"<div class=\"feishu-media\"><div class=\"feishu-media-inner\"><div class=\"feishu-media-icon\">📄</div><div class=\"feishu-media-info\"><div class=\"feishu-media-title\">文件消息</div><div class=\"feishu-media-desc\">{HttpUtility.HtmlEncode(dto.BodyText ?? "未命名文件")}</div></div></div></div>",
            "post" => $"<div class=\"feishu-text\">{HttpUtility.HtmlEncode(dto.BodyText ?? string.Empty).Replace("\n", "<br>")}</div>",
            _ => $"<div class=\"feishu-unknown\">[{HttpUtility.HtmlEncode(dto.MessageType ?? "unknown")} 消息]</div>"
        };
    }

    /// <summary>
    /// 构建飞书媒体消息的卡片式占位 HTML。
    /// 使用语义化 class，前端通过 Tailwind 任意选择器渲染为：左侧图标徽章 + 右侧标题/说明。
    /// </summary>
    private static string BuildMediaPlaceholder(string wrapperClass, string icon, string title, string description)
    {
        return $"<div class=\"{wrapperClass}\">" +
               $"<div class=\"feishu-media-inner\">" +
               $"<div class=\"feishu-media-icon\">{icon}</div>" +
               $"<div class=\"feishu-media-info\">" +
               $"<div class=\"feishu-media-title\">{HttpUtility.HtmlEncode(title)}</div>" +
               $"<div class=\"feishu-media-desc\">{HttpUtility.HtmlEncode(description)}</div>" +
               $"</div>" +
               $"</div>" +
               $"</div>";
    }

    /// <summary>
    /// 查找同一飞书用户在时间窗口内未关闭的会话。
    /// 飞书无邮件头引用链，改用 OpenId + 时间窗口匹配。
    /// </summary>
    private async Task<Conversation?> FindRelatedFeishuConversationAsync(string openId, int mailboxId)
    {
        var cutoff = DateTime.UtcNow - _conversationMatchWindow;

        // 查找该 OpenId 在时间窗口内最近一条未关闭的飞书会话
        var recentThread = await _unitOfWork.Threads.GetQueryable()
            .Where(t => t.From == openId
                && t.SourceType == SourceType.Feishu
                && t.CreatedAt >= cutoff)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (recentThread == null)
        {
            return null;
        }

        var conversation = await _unitOfWork.Conversations.GetQueryable()
            .Where(c => c.Id == recentThread.ConversationId
                && c.MailboxId == mailboxId
                && c.State == ConversationState.Published
                && c.Status != ConversationStatus.Closed
                && c.DeletedAt == null)
            .FirstOrDefaultAsync();

        return conversation;
    }

    /// <summary>
    /// 事务提交后下载飞书媒体资源并保存为附件。
    /// 外部 API 调用，失败不影响主流程（线程已持久化）。
    /// </summary>
    private async Task DownloadAndSaveMediaAsync(FeishuEventDto dto, Conversation conversation, ConversationThread thread, int mailboxId)
    {
        if (string.IsNullOrEmpty(dto.MessageId))
        {
            _logger.LogWarning("飞书媒体消息无 MessageId，跳过下载：MessageType={MessageType}", dto.MessageType);
            return;
        }

        // 从 content JSON 中提取 image_key / file_key
        var fileKey = ExtractMediaKey(dto.Content, dto.MessageType);
        if (string.IsNullOrEmpty(fileKey))
        {
            _logger.LogWarning("飞书媒体消息无 file_key/image_key，跳过下载：MessageId={MessageId}, MessageType={MessageType}",
                dto.MessageId, dto.MessageType);
            return;
        }

        byte[]? mediaData = null;
        string? extension = null;

        try
        {
            var result = await _feishuClient.DownloadMediaAsync(dto.MessageId, fileKey);
            if (result.HasValue)
            {
                mediaData = result.Value.data;
                extension = result.Value.extension;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "下载飞书素材失败：MessageId={MessageId}, FileKey={FileKey}", dto.MessageId, fileKey);
        }

        if (mediaData == null || mediaData.Length == 0)
        {
            _logger.LogWarning("飞书素材下载失败，跳过附件创建：MessageId={MessageId}, FileKey={FileKey}",
                dto.MessageId, fileKey);
            return;
        }

        // 保存文件到磁盘
        var fileDir = mailboxId.ToString();
        var dirPath = Path.Combine(_attachmentBasePath, fileDir);
        Directory.CreateDirectory(dirPath);

        var fileName = BuildMediaFileName(dto.MessageType, extension);
        var storedFileName = $"{Guid.NewGuid():N}_{fileName}";
        var fullPath = Path.Combine(dirPath, storedFileName);

        try
        {
            await File.WriteAllBytesAsync(fullPath, mediaData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "飞书素材保存磁盘失败：Path={Path}", fullPath);
            // 清理可能部分写入的损坏文件
            try { if (File.Exists(fullPath)) File.Delete(fullPath); } catch { /* 忽略清理失败 */ }
            return;
        }

        // 创建附件记录
        var mimeType = GetMimeType(dto.MessageType, extension);
        var attachment = new Attachment
        {
            ThreadId = thread.Id,
            FileName = fileName,
            StoredFileName = storedFileName,
            MimeType = mimeType,
            Type = GetAttachmentType(dto.MessageType),
            FileDir = fileDir,
            Size = mediaData.Length,
            Embedded = false,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _unitOfWork.Attachments.AddAsync(attachment);
            thread.HasAttachments = true;
            conversation.HasAttachments = true;
            await _unitOfWork.SaveChangesAsync();
            _logger.LogInformation("飞书素材已保存为附件：ThreadId={ThreadId}, AttachmentId={AttachmentId}, FileName={FileName}",
                thread.Id, attachment.Id, fileName);
        }
        catch (Exception ex)
        {
            // 附件记录创建失败，删除已写入的文件避免孤儿文件
            _logger.LogError(ex, "飞书附件记录创建失败，清理磁盘文件：Path={Path}", fullPath);
            try { if (File.Exists(fullPath)) File.Delete(fullPath); } catch { /* 忽略清理失败 */ }
        }
    }

    /// <summary>
    /// 从飞书消息 content JSON 中提取媒体 key。
    /// 图片消息：{ "image_key": "xxx" }
    /// 文件/语音/视频消息：{ "file_key": "xxx", "file_name": "xxx" }
    /// </summary>
    private static string? ExtractMediaKey(string? content, string? messageType)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (string.Equals(messageType, "image", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("image_key", out var key) && key.ValueKind == JsonValueKind.String)
                {
                    return key.GetString();
                }
            }
            else
            {
                if (root.TryGetProperty("file_key", out var key) && key.ValueKind == JsonValueKind.String)
                {
                    return key.GetString();
                }
            }
        }
        catch
        {
            // 忽略解析失败
        }

        return null;
    }

    /// <summary>
    /// 通过飞书消息 API 异步发送自动回复（新会话欢迎语）。
    /// 飞书无被动回复机制，自动回复必须通过 API 主动发送。
    /// 返回值：true=发送成功（sentId 非空），false=发送失败或异常
    /// </summary>
    private async Task<bool> SendAutoReplyAsync(string openId, string autoReplyMessage)
    {
        try
        {
            var content = JsonSerializer.Serialize(new { text = autoReplyMessage });
            var sentId = await _feishuClient.SendMessageAsync(openId, "text", content);
            if (string.IsNullOrEmpty(sentId))
            {
                _logger.LogWarning("飞书自动回复发送失败：OpenId={OpenId}", openId);
                return false;
            }
            _logger.LogInformation("飞书自动回复已发送：OpenId={OpenId}, SentMessageId={SentMessageId}",
                openId, sentId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "飞书自动回复发送异常：OpenId={OpenId}", openId);
            return false;
        }
    }

    /// <summary>
    /// 根据消息类型和扩展名构建文件名。
    /// </summary>
    private static string BuildMediaFileName(string? msgType, string? extension)
    {
        extension ??= ".bin";
        if (!extension.StartsWith('.')) extension = "." + extension;
        return (msgType ?? "media").ToLowerInvariant() switch
        {
            "image" => $"feishu_image_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}",
            "audio" => $"feishu_voice_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}",
            "media" => $"feishu_video_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}",
            "file" => $"feishu_file_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}",
            _ => $"feishu_media_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}"
        };
    }

    /// <summary>
    /// 根据消息类型和扩展名推断 MIME 类型。
    /// </summary>
    private static string GetMimeType(string? msgType, string? extension)
    {
        return (msgType?.ToLowerInvariant(), extension?.ToLowerInvariant()) switch
        {
            ("image", ".png") => "image/png",
            ("image", ".gif") => "image/gif",
            ("image", ".bmp") => "image/bmp",
            ("image", ".webp") => "image/webp",
            ("image", _) => "image/jpeg",
            ("audio", ".mp3") => "audio/mpeg",
            ("audio", ".aac") => "audio/aac",
            ("audio", _) => "audio/opus",
            ("media", ".mov") => "video/quicktime",
            ("media", _) => "video/mp4",
            ("file", ".pdf") => "application/pdf",
            ("file", ".doc") or ("file", ".docx") => "application/msword",
            ("file", ".xls") or ("file", ".xlsx") => "application/vnd.ms-excel",
            ("file", ".ppt") or ("file", ".pptx") => "application/vnd.ms-powerpoint",
            ("file", _) => "application/octet-stream",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// 根据消息类型获取附件分类。
    /// </summary>
    private static AttachmentType GetAttachmentType(string? msgType)
    {
        return msgType?.ToLowerInvariant() switch
        {
            "image" => AttachmentType.Image,
            "audio" => AttachmentType.Audio,
            "media" => AttachmentType.Video,
            "file" => AttachmentType.Other,
            _ => AttachmentType.Other
        };
    }

    /// <summary>
    /// 事务提交后触发后置工作流。
    /// 工作流执行失败被隔离捕获，不影响主流程。
    /// </summary>
    private async Task TriggerPostIngestionWorkflowsAsync(Conversation conversation, bool isNewConversation)
    {
        try
        {
            var trigger = isNewConversation
                ? WorkflowConst.TriggerNewConversation
                : WorkflowConst.TriggerNewReply;
            await _workflowService.ExecuteForConversationAsync(conversation.Id, trigger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发工作流失败：ConversationId={ConversationId}, IsNew={IsNew}",
                conversation.Id, isNewConversation);
        }
    }

    /// <summary>
    /// 解析飞书会话应归属的邮箱 ID。
    /// 优先使用 Feishu:MailboxId 配置，未配置时使用第一个活跃邮箱。
    /// </summary>
    private async Task<int?> ResolveMailboxIdAsync()
    {
        if (Options.MailboxId.HasValue && Options.MailboxId.Value > 0)
        {
            return Options.MailboxId.Value;
        }

        var mailbox = await _unitOfWork.Mailboxes.GetFirstActiveAsync();
        return mailbox?.Id;
    }

    private async Task AssignFolderAsync(Conversation conversation, int mailboxId)
    {
        var folderType = Conversation.CalculateFolderType(conversation.Status, conversation.UserId);
        var folder = await _unitOfWork.Folders.GetByMailboxIdAndTypeAsync(mailboxId, (int)folderType);

        if (folder == null)
        {
            folder = await _unitOfWork.Folders.GetByMailboxIdAndTypeAsync(mailboxId, (int)FolderType.Unassigned);
        }

        if (folder != null)
        {
            conversation.FolderId = folder.Id;
            conversation.ConversationFolders.Add(new ConversationFolder
            {
                FolderId = folder.Id,
                ConversationId = conversation.Id
            });
        }
    }

    /// <summary>
    /// 构建会话主题（根据消息类型生成摘要标题）。
    /// </summary>
    private static string BuildConversationSubject(FeishuEventDto dto)
    {
        var sender = !string.IsNullOrEmpty(dto.SenderOpenId) ? dto.SenderOpenId : "未知用户";
        var preview = (dto.MessageType ?? string.Empty).ToLowerInvariant() switch
        {
            "text" => dto.BodyText != null && dto.BodyText.Length > 50 ? dto.BodyText[..50] + "..." : (dto.BodyText ?? string.Empty),
            "image" => "[图片消息]",
            "audio" => "[语音消息]",
            "media" => "[视频消息]",
            "file" => dto.BodyText ?? "[文件消息]",
            "post" => "[富文本消息]",
            _ => $"[{dto.MessageType ?? "unknown"} 消息]"
        };

        var subject = $"飞书: {sender} - {preview}";
        return subject.Length > 100 ? subject[..100] : subject;
    }

    /// <summary>
    /// 构建飞书消息的去重 ID，加 "fs_" 前缀避免与微信/邮件 Message-Id 冲突。
    /// 事件无 MessageId 时使用 EventId 作为去重键。
    /// </summary>
    private static string BuildFeishuMessageId(FeishuEventDto dto)
    {
        if (!string.IsNullOrEmpty(dto.MessageId))
        {
            return $"fs_{dto.MessageId}";
        }

        return $"fs_event_{dto.EventId}_{dto.CreateTime}";
    }

    /// <summary>
    /// 解析飞书时间戳（Unix 毫秒字符串）为 UTC DateTime。
    /// 飞书 create_time 为毫秒级 Unix 时间戳。
    /// </summary>
    private static DateTime ParseFeishuTime(string? timeStr)
    {
        if (string.IsNullOrEmpty(timeStr))
        {
            return DateTime.UtcNow;
        }

        // 飞书时间戳为毫秒
        if (long.TryParse(timeStr, out var ms))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            }
            catch
            {
                // 数值超出范围时降级
            }
        }

        return DateTime.UtcNow;
    }

    /// <summary>
    /// 调用飞书 /contact/v3/users/{open_id} 补全客户资料。
    /// 权限要求（在飞书开放后台"权限管理"中申请）：
    ///   - 基础信息（name/en_name/nickname/avatar）：contact:contact.base:readonly 或已废弃的 contact:user.base:readonly
    ///   - 邮箱：contact:user.email:readonly
    ///   - 手机号：contact:user.phone:readonly（仅企业内部应用）
    ///   - 职位/部门等：对应字段级权限（未开通时不返回，代码自动跳过）
    /// 
    /// 填充字段：
    ///   - name / en_name → FirstName（优先中文；无中文时用英文）
    ///   - nickname → 仅在 name 缺失时作为兜底
    ///   - email → Emails（无 email 时写入）
    ///   - mobile → Phones（无手机号时写入），含国家码 mobile_visible 标志忽略
    ///   - avatar（avatar_origin / avatar_640 择高分辨率）→ Avatar（通过 ValidateAndNormalizeAvatarUrl 校验）
    ///   - job_title（若返回）→ JobTitle
    ///   - city / country（若返回）→ Customer.City / Customer.Country
    /// 
    /// 为避免每条消息重复调用，若所有目标字段均已有值，则直接跳过。
    /// 所有异常全部捕获，不阻塞消息主流程。
    /// </summary>
    private async Task TryEnrichCustomerProfileAsync(Customer customer, string openId)
    {
        try
        {
            bool hasName = !string.IsNullOrWhiteSpace(customer.FirstName) && !string.IsNullOrWhiteSpace(customer.LastName);
            bool hasAvatar = !string.IsNullOrWhiteSpace(customer.Avatar);
            bool hasEmail = customer.Emails != null && customer.Emails.Any(e => !string.IsNullOrWhiteSpace(e.EmailAddress));
            bool hasPhone = !string.IsNullOrWhiteSpace(customer.Phones) && customer.GetPhones().Count > 0;
            bool hasJobTitle = !string.IsNullOrWhiteSpace(customer.JobTitle);
            bool hasCity = !string.IsNullOrWhiteSpace(customer.City);
            bool hasCountry = !string.IsNullOrWhiteSpace(customer.Country);
            if (hasName && hasAvatar && hasEmail && hasPhone && hasJobTitle && hasCity && hasCountry)
                return;

            var data = await _feishuClient.GetUserInfoAsync(openId);
            if (data == null || data.Value.ValueKind != JsonValueKind.Object)
                return;

            // 飞书返回结构：{ "user": {...} }
            if (!data.Value.TryGetProperty("user", out var user) || user.ValueKind != JsonValueKind.Object)
                return;

            bool changed = false;

            // 1. 姓名：name（中文）> en_name（英文）> nickname（昵称兜底）
            if (!hasName)
            {
                string? finalName = null;
                if (user.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(nameEl.GetString()))
                {
                    finalName = nameEl.GetString()!.Trim();
                }
                else if (user.TryGetProperty("en_name", out var enNameEl) && enNameEl.ValueKind == JsonValueKind.String
                         && !string.IsNullOrWhiteSpace(enNameEl.GetString()))
                {
                    finalName = enNameEl.GetString()!.Trim();
                }
                else if (user.TryGetProperty("nickname", out var nickEl) && nickEl.ValueKind == JsonValueKind.String
                         && !string.IsNullOrWhiteSpace(nickEl.GetString()))
                {
                    finalName = nickEl.GetString()!.Trim();
                }

                if (!string.IsNullOrWhiteSpace(finalName))
                {
                    var (lastName, firstName) = ChineseNameSplitter.Split(finalName);
                    customer.LastName = lastName;
                    customer.FirstName = firstName;
                    changed = true;
                    _logger.LogDebug("飞书客户补全姓名：CustomerId={CustomerId}, Name={Name}, LastName={LastName}, FirstName={FirstName}",
                        customer.Id, finalName, lastName, firstName);
                }
            }

            // 2. 头像：优先 avatar_origin > avatar_640 > avatar_240 > avatar_72
            if (!hasAvatar && user.TryGetProperty("avatar", out var avatarObj) && avatarObj.ValueKind == JsonValueKind.Object)
            {
                string? avatarUrl = null;
                string[] fallbacks = ["avatar_origin", "avatar_640", "avatar_240", "avatar_72"];
                foreach (var key in fallbacks)
                {
                    if (avatarObj.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(el.GetString()))
                    {
                        avatarUrl = el.GetString()!;
                        break;
                    }
                }
                if (!string.IsNullOrWhiteSpace(avatarUrl))
                {
                    var err = CustomerService.ValidateAndNormalizeAvatarUrl(avatarUrl, out var normalized);
                    if (err == null && !string.IsNullOrWhiteSpace(normalized))
                    {
                        customer.Avatar = normalized;
                        changed = true;
                        _logger.LogDebug("飞书客户补全头像：CustomerId={CustomerId}", customer.Id);
                    }
                }
            }

            // 3. 邮箱：需 contact:user.email:readonly 权限。优先个人邮箱 email，其次企业邮箱 enterprise_email
            if (!hasEmail)
            {
                customer.Emails ??= new List<Email>();

                // 3a. 个人邮箱
                if (user.TryGetProperty("email", out var emailEl) && emailEl.ValueKind == JsonValueKind.String)
                {
                    var email = emailEl.GetString();
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        customer.Emails.Add(new Email
                        {
                            EmailAddress = email.Trim(),
                            Type = 1,
                            CustomerId = customer.Id
                        });
                        changed = true;
                        _logger.LogInformation("飞书客户邮箱补全成功：CustomerId={CustomerId}, Email={Email}", customer.Id, email);
                    }
                }

                // 3b. 企业邮箱（作为第二邮箱，Type=2 工作邮箱）
                if (user.TryGetProperty("enterprise_email", out var entEmailEl) && entEmailEl.ValueKind == JsonValueKind.String)
                {
                    var entEmail = entEmailEl.GetString();
                    if (!string.IsNullOrWhiteSpace(entEmail))
                    {
                        var normalized = entEmail.Trim().ToLowerInvariant();
                        if (!customer.Emails.Any(e => e.EmailAddress.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                        {
                            customer.Emails.Add(new Email
                            {
                                EmailAddress = normalized,
                                Type = 2,
                                CustomerId = customer.Id
                            });
                            changed = true;
                            _logger.LogInformation("飞书客户企业邮箱补全成功：CustomerId={CustomerId}, EntEmail={EntEmail}", customer.Id, entEmail);
                        }
                    }
                }
            }

            // 4. 手机号：需 contact:user.phone:readonly 权限
            if (!hasPhone && user.TryGetProperty("mobile", out var mobileEl) && mobileEl.ValueKind == JsonValueKind.String)
            {
                var mobile = mobileEl.GetString();
                if (!string.IsNullOrWhiteSpace(mobile))
                {
                    // 国家码：优先从 mobile 返回结果中推断，若飞书有单独字段则使用
                    var countryCode = user.TryGetProperty("country_code", out var ccEl) && ccEl.ValueKind == JsonValueKind.String
                        ? ccEl.GetString()?.Trim()
                        : null;
                    var phones = customer.GetPhones();
                    phones.Add(new CustomerPhone
                    {
                        Value = mobile.Trim(),
                        Type = 1,
                        N = !string.IsNullOrEmpty(countryCode) ? (countryCode.StartsWith('+') ? countryCode : $"+{countryCode}") : null
                    });
                    customer.SetPhones(phones);
                    changed = true;
                    _logger.LogDebug("飞书客户补全手机号：CustomerId={CustomerId}, CountryCode={CountryCode}", customer.Id, countryCode);
                }
            }

            // 5. 职位 job_title（部分飞书版本/权限会返回该字段）
            if (!hasJobTitle)
            {
                string? title = null;
                if (user.TryGetProperty("job_title", out var jtEl) && jtEl.ValueKind == JsonValueKind.String)
                    title = jtEl.GetString();
                else if (user.TryGetProperty("job_title_i18n", out var jtiEl) && jtiEl.ValueKind == JsonValueKind.Object
                         && jtiEl.TryGetProperty("zh_cn", out var zhEl) && zhEl.ValueKind == JsonValueKind.String)
                    title = zhEl.GetString();

                if (!string.IsNullOrWhiteSpace(title))
                {
                    customer.JobTitle = title.Trim();
                    changed = true;
                    _logger.LogDebug("飞书客户补全职位：CustomerId={CustomerId}, JobTitle={JobTitle}", customer.Id, title);
                }
            }

            // 6. 城市 / 国家
            if (!hasCity && user.TryGetProperty("city", out var cityEl) && cityEl.ValueKind == JsonValueKind.String)
            {
                var city = cityEl.GetString();
                if (!string.IsNullOrWhiteSpace(city))
                {
                    customer.City = city.Trim();
                    changed = true;
                }
            }
            if (!hasCountry && user.TryGetProperty("country", out var countryEl) && countryEl.ValueKind == JsonValueKind.String)
            {
                var country = countryEl.GetString();
                if (!string.IsNullOrWhiteSpace(country))
                {
                    customer.Country = country.Trim();
                    changed = true;
                }
            }

            // 7. 工位（work_station）→ Address
            if (string.IsNullOrWhiteSpace(customer.Address)
                && user.TryGetProperty("work_station", out var wsEl) && wsEl.ValueKind == JsonValueKind.String)
            {
                var workStation = wsEl.GetString();
                if (!string.IsNullOrWhiteSpace(workStation))
                {
                    customer.Address = workStation.Trim();
                    changed = true;
                }
            }

            // 8. 工号（employee_no）→ Notes（仅当 Notes 为空时写入）
            if (string.IsNullOrWhiteSpace(customer.Notes)
                && user.TryGetProperty("employee_no", out var empNoEl) && empNoEl.ValueKind == JsonValueKind.String)
            {
                var employeeNo = empNoEl.GetString();
                if (!string.IsNullOrWhiteSpace(employeeNo))
                {
                    customer.Notes = $"工号：{employeeNo.Trim()}";
                    changed = true;
                }
            }

            if (changed)
            {
                customer.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
                _logger.LogDebug("飞书客户资料 enrich 完成并持久化：CustomerId={CustomerId}", customer.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "飞书客户资料补全失败：CustomerId={CustomerId}, OpenId={OpenId}", customer.Id, openId);
        }
    }

    /// <summary>联系方式提取最大尝试次数：超过后即使字段缺失也不再尝试，避免每条消息反复跑正则。</summary>
    private const int MaxContactExtractAttempts = 3;

    /// <summary>
    /// 从客户消息文本中提取联系方式（姓名、手机、邮箱），写入 Customer 对应字段。
    /// 飞书 /contact/v3/users 接口在权限不足时无法返回邮箱、手机号，
    /// 通过对话引导用户提供后用 ContactInfoExtractor 正则自动识别。
    /// 提取到的字段直接覆盖已有值（覆盖策略）。
    /// </summary>
    /// <returns>true=成功提取并写入至少一个字段；false=未提取到</returns>
    private async Task<bool> TryExtractContactInfoFromMessageAsync(Customer customer, string text, int conversationId)
    {
        try
        {
            // 前置检查：确保 customer 被 EF Core 跟踪（工作流执行链路可能已 ClearChanges 导致 detach）
            if (_unitOfWork.Customers.FindTracked(customer.Id) == null)
            {
                _logger.LogWarning("飞书客户实体已被 detach，重新获取 tracked 实体：CustomerId={CustomerId}", customer.Id);
                var reloaded = await _unitOfWork.Customers.GetOrCreateByChannelAsync(
                    customer.Channel ?? "feishu", customer.ChannelId ?? string.Empty);
                if (reloaded == null)
                {
                    _logger.LogError("飞书客户实体重新加载失败：CustomerId={CustomerId}", customer.Id);
                    return false;
                }
                customer = reloaded;
            }

            // 字段当前状态（用于调试日志），覆盖策略下不再因已有字段而跳过提取
            bool hasName = !string.IsNullOrWhiteSpace(customer.FirstName) || !string.IsNullOrWhiteSpace(customer.LastName);
            bool hasPhone = customer.GetPhones().Count > 0;
            bool hasEmail = customer.Emails != null && customer.Emails.Any(e => !string.IsNullOrWhiteSpace(e.EmailAddress));

            // 跳过条件：提取尝试次数达到上限（3 次），直接跳过
            if (customer.ContactExtractAttempts >= MaxContactExtractAttempts)
            {
                _logger.LogDebug("飞书联系方式提取已达最大尝试次数，跳过：CustomerId={CustomerId}, Attempts={Attempts}, Max={Max}",
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

            // 1. 姓名：直接覆盖已有姓名（覆盖策略）
            if (!string.IsNullOrWhiteSpace(info.Name))
            {
                var (lastName, firstName) = ChineseNameSplitter.Split(info.Name);
                customer.LastName = lastName;
                customer.FirstName = firstName;
                changed = true;
                _logger.LogDebug("飞书对话姓名提取成功：CustomerId={CustomerId}, Name={Name}, LastName={LastName}, FirstName={FirstName}",
                    customer.Id, info.Name, lastName, firstName);
            }

            // 2. 手机号：先清空已有手机号再写入，实现替换（覆盖策略）
            if (!string.IsNullOrWhiteSpace(info.Phone))
            {
                var phones = customer.GetPhones();
                phones.Clear();
                phones.Add(new CustomerPhone
                {
                    Value = info.Phone,
                    Type = 4  // 4=手机
                });
                customer.SetPhones(phones);
                changed = true;
                _logger.LogDebug("飞书对话手机号提取成功：CustomerId={CustomerId}, Phone={Phone}", customer.Id, info.Phone);
            }

            // 3. 邮箱：先清空已有邮箱再写入，实现替换（覆盖策略）
            customer.Emails ??= new List<Email>();
            if (!string.IsNullOrWhiteSpace(info.Email))
            {
                // 邮箱全局唯一约束检查：避免与其他客户（含已删除）的邮箱冲突
                var emailOwned = await _unitOfWork.Customers.IsEmailOwnedByAnyCustomerAsync(info.Email, customer.Id);
                if (emailOwned)
                {
                    _logger.LogWarning("飞书对话邮箱已被其他客户占用，跳过添加：CustomerId={CustomerId}, Email={Email}", customer.Id, info.Email);
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
                    _logger.LogDebug("飞书对话邮箱提取成功：CustomerId={CustomerId}, Email={Email}", customer.Id, info.Email);

                    // 将提取到的邮箱回填到会话的 CustomerEmail，确保会话关闭时能发送通知邮件
                    // 仅当会话当前 CustomerEmail 为空时才回填（已有邮箱不覆盖）
                    // 使用 GetByIdForUpdateAsync 获取追踪实体，修改后随 SaveChangesAsync 一起持久化
                    var conversationToUpdate = await _unitOfWork.Conversations.GetByIdForUpdateAsync(conversationId);
                    if (conversationToUpdate != null && string.IsNullOrWhiteSpace(conversationToUpdate.CustomerEmail))
                    {
                        conversationToUpdate.CustomerEmail = info.Email;
                        _logger.LogInformation("飞书会话邮箱回填成功：ConversationId={ConversationId}, CustomerEmail={Email}",
                            conversationId, info.Email);
                    }
                }
            }

            // 尝试次数已自增，即使 changed=false 也要 SaveChanges 持久化次数
            customer.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();

            if (changed)
            {
                _logger.LogInformation("飞书对话联系方式提取成功：CustomerId={CustomerId}, Name={Name}, Phone={Phone}, Email={Email}, Attempts={Attempts}",
                    customer.Id, info.Name, info.Phone, info.Email, customer.ContactExtractAttempts);
                return true;
            }

            _logger.LogDebug("飞书联系方式提取未找到缺失字段，但已记录尝试次数：CustomerId={CustomerId}, Attempts={Attempts}",
                customer.Id, customer.ContactExtractAttempts);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "飞书对话联系方式提取失败：CustomerId={CustomerId}", customer.Id);
            _unitOfWork.ClearChanges();
            return false;
        }
    }

    private sealed class FeishuIngestionContext
    {
        public required Conversation Conversation { get; init; }
        public required ConversationThread Thread { get; init; }
        public required bool IsNewConversation { get; init; }
    }
}
