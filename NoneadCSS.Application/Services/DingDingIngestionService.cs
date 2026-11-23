using System.Text.Json;
using System.Web;
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
/// 钉钉入站消息接入服务 - 处理机器人 Webhook 推送的消息。
/// 去重(msgId) → 客户匹配(senderStaffId) → 会话匹配(72h窗口) → 事务持久化 → 触发工作流
/// </summary>
public class DingDingIngestionService : IDingDingIngestionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowService _workflowService;
    private readonly IDingDingSettingsProvider _settingsProvider;
    private readonly IDingDingClient _dingDingClient;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<DingDingIngestionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _attachmentBasePath;
    // 钉钉会话匹配窗口：72 小时（钉钉无 24h 限制，自定义更长窗口）
    private readonly TimeSpan _conversationMatchWindow;
    private readonly IWebhookService? _webhookService;

    /// <summary>钉钉媒体消息类型集合，需要通过 downloadCode 下载素材</summary>
    private static readonly HashSet<string> MediaMsgTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "picture", "audio", "video", "file"
    };

    public DingDingIngestionService(
        IUnitOfWork unitOfWork,
        IWorkflowService workflowService,
        IDingDingSettingsProvider settingsProvider,
        IDingDingClient dingDingClient,
        INotificationPublisher notificationPublisher,
        ILogger<DingDingIngestionService> logger,
        IConfiguration configuration,
        IOptions<IngestionMatchOptions> ingestionMatchOptions,
        IHostEnvironment env,
        IWebhookService? webhookService = null)
    {
        _unitOfWork = unitOfWork;
        _workflowService = workflowService;
        _settingsProvider = settingsProvider;
        _dingDingClient = dingDingClient;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
        _configuration = configuration;
        _attachmentBasePath = AttachmentPathResolver.ResolveBasePath(
            configuration["Attachment:UploadPath"], env.ContentRootPath);
        _conversationMatchWindow = TimeSpan.FromHours(ingestionMatchOptions.Value.DingDingMatchWindowHours);
        _webhookService = webhookService;
    }

    /// <inheritdoc />
    public void ResetTracking() => _unitOfWork.ClearChanges();

    public async Task<DingDingIngestionResult> ProcessWebhookPayloadAsync(string payload)
    {
        var result = new DingDingIngestionResult();

        // 解析消息
        var message = DingDingWebhookParser.ParseMessage(payload);
        if (message == null)
        {
            _logger.LogWarning("钉钉 Webhook 消息解析失败");
            result.ErrorMessage = "消息解析失败";
            return result;
        }

        // 只处理单聊消息（群聊消息暂不处理）
        if (!message.IsSingleChat)
        {
            _logger.LogInformation("钉钉群聊消息暂不处理：ConversationId={ConversationId}", message.ConversationId);
            return result;
        }

        return await ProcessSingleMessageAsync(message);
    }

    private async Task<DingDingIngestionResult> ProcessSingleMessageAsync(DingDingIncomingMessage message)
    {
        var result = new DingDingIngestionResult();

        // 1. 去重：仅基于 message.MsgId（钉钉为每条消息分配的消息级唯一 ID）。
        // 注意：sessionWebhook 是会话级唯一（同一用户单聊会话 2 小时内共用一个），不能用作去重键，
        // 否则同一会话的第二条及之后消息会被误判为重复而被丢弃。
        // T000113 后：Thread.MessageId 存真实 MsgId（消息级唯一，满足 UNIQUE 约束）；
        // sessionWebhook 改存 Thread.Headers（"sessionWebhook:" 前缀），仅用于客服回复时路由。
        if (!string.IsNullOrEmpty(message.MsgId))
        {
            var existingThread = await _unitOfWork.Threads.FindAsync(
                t => t.State != ThreadState.Deleted && t.MessageId == message.MsgId);
            if (existingThread.Any())
            {
                _logger.LogInformation("钉钉消息重复，跳过：MsgId={MsgId}", message.MsgId);
                result.IsDuplicate = true;
                return result;
            }
        }

        // 2. 客户匹配 (senderStaffId → Customer)
        var senderId = message.SenderStaffId ?? message.ChatbotUserId ?? string.Empty;
        if (string.IsNullOrEmpty(senderId))
        {
            result.ErrorMessage = "无法获取发送者 ID";
            _logger.LogError("钉钉消息缺少 senderStaffId");
            return result;
        }

        var customer = await _unitOfWork.Customers.GetOrCreateByChannelAsync(
            "dingtalk", senderId, message.SenderNick);
        if (customer == null)
        {
            result.ErrorMessage = "客户创建失败";
            _logger.LogError("钉钉客户创建失败：StaffId={StaffId}", senderId);
            return result;
        }

        // 2.1 补全客户资料：调用钉钉 /topapi/v2/user/get 反查用户详情，
        // 按需要补全 FirstName、LastName、Avatar、JobTitle、Country/State/City、
        // Email、Mobile 等字段。任何异常都吞掉，不阻塞主流程。
        // 注意：TryEnrichCustomerProfileAsync 内部失败时会 ClearChanges 导致 customer 被 detach，
        // 该方法返回后会返回 tracked 的 customer 实体（失败时重新查询获取）。
        customer = await TryEnrichCustomerProfileAsync(customer, senderId);

        // 3. 解析邮箱
        var mailboxId = await ResolveMailboxIdAsync();
        if (mailboxId == 0)
        {
            result.ErrorMessage = "未找到可用邮箱";
            _logger.LogError("钉钉消息处理失败：未找到可用邮箱");
            return result;
        }

        // 4. 会话匹配 (72h 窗口)
        var conversation = await _unitOfWork.Conversations
            .FindActiveDingTalkConversationByUserIdAsync(senderId, _conversationMatchWindow);
        var isNewConversation = conversation == null;

        // 5. 事务持久化
        ConversationThread? thread = null;
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            if (isNewConversation)
            {
                conversation = await CreateNewConversationAsync(customer, mailboxId, message);
                result.NewConversationCount = 1;
            }
            else
            {
                result.ReplyCount = 1;
                // 已有会话追加客户回复：更新 LastReplyAt，保证会话列表排序正确
                conversation!.LastReplyAt = DateTime.UtcNow;
                conversation.LastReplyFrom = SourceVia.Customer;
                conversation.UpdatedAt = DateTime.UtcNow;
            }

            // 创建线程
            thread = CreateThreadFromMessage(conversation!, customer, message);
            await _unitOfWork.Threads.AddAsync(thread);
            await _unitOfWork.SaveChangesAsync();

            // 新客户在 SaveChanges 后才获得实际 ID，需回填 CreatedByCustomerId
            if (isNewConversation && conversation!.CreatedByCustomerId == 0 && customer.Id > 0)
            {
                conversation.CreatedByCustomerId = customer.Id;
                await _unitOfWork.SaveChangesAsync();
            }

            await transaction.CommitAsync();

            // 6. 触发工作流（失败不阻塞主流程）
            await TriggerPostIngestionWorkflowsAsync(conversation!, isNewConversation);

            // 7. SignalR 通知
            await _notificationPublisher.NotifyMailboxAsync(
                conversation!.MailboxId.ToString(),
                "conversation_updated",
                new { conversationId = conversation.Id });

            // 7.1 触发 Webhook 通知（失败仅记录日志，不阻塞主流程）
            if (_webhookService != null)
            {
                try
                {
                    await _webhookService.TriggerAsync("conversation_updated", conversation.MailboxId, new { conversationId = conversation.Id }, "Conversation", conversation.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "触发 Webhook 失败：Event={Event}, Entity={EntityType}:{EntityId}", "conversation_updated", "Conversation", conversation.Id);
                }
            }

            // 8. 事务提交后下载媒体素材并关联附件（外部 API 调用，不影响主流程）
            if (thread != null && MediaMsgTypes.Contains(message.MsgType ?? string.Empty))
            {
                await DownloadAndSaveMediaAsync(message, conversation!, thread, mailboxId);
            }

            // 9. 自动回复
            if (isNewConversation)
            {
                await SendAutoReplyAsync(message);
            }

            result.ConversationId = conversation.Id;

            // 10. 联系方式收集：提取适用于所有消息（新会话+回复），引导仅限新会话
            //     先从客户当前消息文本尝试提取姓名/手机/邮箱（客户可能直接回复了）
            //     提取方法内部已有最大尝试次数限制（3 次）和字段完整性检查，不会过度提取
            //     新会话且缺任一字段时，发送引导消息（受 ContactCollectionEnabled 开关控制）
            if (_settingsProvider.Current.ContactCollectionEnabled)
            {
                // customer 变量已在主流程中获取并经过 TryEnrichCustomerProfileAsync 更新，
                // 是 _unitOfWork tracking 中的最新实体，可直接使用
                // 1. 从文本消息中提取（仅文本消息有 TextContent）
                bool contactExtracted = false;
                if (!string.IsNullOrWhiteSpace(message.TextContent))
                {
                    contactExtracted = await TryExtractContactInfoFromMessageAsync(customer, message.TextContent, conversation.Id);
                }

                // 2. 判断客户当前是否缺任一字段
                bool hasName = !string.IsNullOrWhiteSpace(customer.FirstName) || !string.IsNullOrWhiteSpace(customer.LastName);
                bool hasPhone = customer.GetPhones().Count > 0;
                bool hasEmail = customer.Emails != null && customer.Emails.Any(e => !string.IsNullOrWhiteSpace(e.EmailAddress));

                _logger.LogInformation("钉钉客户信息收集判断：StaffId={StaffId}, CustomerId={CustomerId}, HasName={HasName}, HasPhone={HasPhone}, HasEmail={HasEmail}, ContactExtracted={ContactExtracted}, HasSessionWebhook={HasSessionWebhook}, IsNew={IsNew}",
                    message.SenderStaffId, customer.Id, hasName, hasPhone, hasEmail, contactExtracted, !string.IsNullOrEmpty(message.SessionWebhook), isNewConversation);

                // 3. 新会话 + 缺任一字段 +（本次没提取到或提取后仍缺字段）→ 发送引导
                if ((!contactExtracted || !hasName || !hasPhone || !hasEmail)
                    && isNewConversation
                    && (!hasName || !hasPhone || !hasEmail))
                {
                    try
                    {
                        var contactGuidance = "为了更好地为您服务，方便后续发送相关资料，请您回复您的姓名，手机，邮箱地址，谢谢！";
                        var guidanceSent = false;

                        // 优先通过 sessionWebhook 发送（HTTP 回调模式和 Stream 模式均可能携带此字段）
                        if (!string.IsNullOrEmpty(message.SessionWebhook))
                        {
                            guidanceSent = await _dingDingClient.SendBySessionWebhookAsync(
                                message.SessionWebhook, "text", new { content = contactGuidance });
                        }

                        // sessionWebhook 为空或发送失败时，通过机器人单聊消息 API 发送（Stream 模式备选方案）
                        if (!guidanceSent)
                        {
                            var settings = _settingsProvider.Current;
                            var robotCode = !string.IsNullOrEmpty(settings.RobotCode) ? settings.RobotCode : settings.AppKey;
                            if (!string.IsNullOrEmpty(robotCode) && !string.IsNullOrEmpty(message.SenderStaffId))
                            {
                                var content = JsonSerializer.Serialize(new { content = contactGuidance });
                                var processKey = await _dingDingClient.BatchSendOtoMessageAsync(
                                    robotCode, new[] { message.SenderStaffId }, "sampleText", content);
                                guidanceSent = !string.IsNullOrEmpty(processKey);
                            }
                            else
                            {
                                _logger.LogWarning("钉钉联系方式收集引导无法发送：sessionWebhook 为空且 RobotCode/AppKey 或 SenderStaffId 缺失");
                            }
                        }

                        if (guidanceSent)
                        {
                            _logger.LogInformation("已发送钉钉联系方式收集引导：StaffId={StaffId}, CustomerId={CustomerId}, 缺字段 Name={NeedName}, Phone={NeedPhone}, Email={NeedEmail}",
                                message.SenderStaffId, customer.Id, !hasName, !hasPhone, !hasEmail);
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
                                    SourceType = SourceType.DingTalk,
                                    From = "dingtalk@system",
                                    To = message.SenderStaffId ?? string.Empty,
                                    Body = contactGuidance,
                                    SendStatus = SendStatus.Sent,
                                    IsInternal = false,
                                    MessageId = $"dingtalk-contact-guidance-{Guid.NewGuid():N}",
                                    CreatedAt = guidanceTime,
                                    UpdatedAt = guidanceTime
                                };
                                await _unitOfWork.Threads.AddAsync(guidanceThread);
                                await _unitOfWork.SaveChangesAsync();
                                _logger.LogInformation("钉钉联系方式收集引导已写入会话线程：ConversationId={ConversationId}",
                                    conversation.Id);
                            }
                            catch (Exception thrEx)
                            {
                                _logger.LogWarning(thrEx, "钉钉联系方式收集引导写入会话线程失败：ConversationId={ConversationId}",
                                    conversation.Id);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("钉钉联系方式收集引导发送失败：StaffId={StaffId}", message.SenderStaffId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "发送钉钉联系方式收集引导失败：StaffId={StaffId}", message.SenderStaffId);
                    }
                }
                else if (isNewConversation && hasName && hasPhone && hasEmail)
                {
                    // 内部员工：TryEnrich 已通过钉钉 API 补全全部字段，无需发送引导消息
                    _logger.LogInformation("钉钉客户信息收集跳过：客户资料已齐全（内部员工），CustomerId={CustomerId}, StaffId={StaffId}",
                        customer.Id, message.SenderStaffId);
                }
                else if (!isNewConversation)
                {
                    _logger.LogDebug("钉钉客户信息收集：非新会话，仅提取不发送引导：StaffId={StaffId}, ContactExtracted={ContactExtracted}",
                        message.SenderStaffId, contactExtracted);
                }
            }
        }
        catch (Exception ex)
        {
            await transaction.SafeRollbackAsync();
            // 清除变更跟踪器中残留的实体（Conversation、Thread、Email 等），
            // 避免事务回滚后这些 Added 状态的实体在下一条消息处理时被重新写入，
            // 导致 UNIQUE constraint failed（Conversations.Number / Emails.EmailAddress）
            // 等错误传染到后续消息，使后续消息也无法创建会话。
            _unitOfWork.ClearChanges();
            _logger.LogError(ex, "钉钉消息持久化失败：MsgId={MsgId}", message.MsgId);
            result.ErrorMessage = "消息持久化失败";
        }

        return result;
    }

    private async Task<Conversation> CreateNewConversationAsync(Customer customer, int mailboxId, DingDingIncomingMessage message)
    {
        var number = await _unitOfWork.Conversations.GetNextNumberAsync();

        var conversation = new Conversation
        {
            Number = number,
            Subject = BuildConversationSubject(message),
            Customer = customer, // 使用导航属性而非 CustomerId = customer.Id，确保新客户（Id=0）时 EF Core 能正确做 FK fixup
            MailboxId = mailboxId,
            SourceType = SourceType.DingTalk,
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

    private static ConversationThread CreateThreadFromMessage(Conversation conversation, Customer customer, DingDingIncomingMessage message)
    {
        var thread = new ConversationThread
        {
            ConversationId = conversation.Id,
            From = message.SenderStaffId ?? string.Empty,
            To = message.ChatbotUserId ?? string.Empty,
            Subject = string.Empty,
            Body = BuildThreadHtmlBody(message),
            // T000113：Threads.MessageId 有 UNIQUE 约束，必须存"消息级唯一"的真实 MsgId。
            // 会话级 sessionWebhook 改存 Headers（"sessionWebhook:" 前缀），
            // 否则同一会话第二/后续消息因 MessageId 同值（sw:{webhook}）触发唯一冲突而丢失。
            MessageId = message.MsgId ?? $"dingtalk-msg-{Guid.NewGuid():N}",
            Headers = !string.IsNullOrEmpty(message.SessionWebhook)
                ? $"sessionWebhook:{message.SessionWebhook}"
                : null,
            SourceType = SourceType.DingTalk,
            Type = ThreadType.Customer,
            SourceVia = SourceVia.Customer,
            CreatedByCustomerId = customer.Id,
            CreatedAt = message.CreatedAtUtc > DateTime.UtcNow ? DateTime.UtcNow : message.CreatedAtUtc
        };
        return thread;
    }

    /// <summary>
    /// 调用钉钉 /topapi/v2/user/get 反查用户详情，按需要补全客户资料：
    /// - 姓名：result.name → FirstName（LastName 留空）
    /// - 头像：result.avatar → Avatar（通过 CustomerService.ValidateAndNormalizeAvatarUrl 校验）
    /// - 邮箱：result.email → Emails（仅当客户尚无任何 email 时写入）
    /// - 手机号：result.mobile → Phones（仅当客户尚无手机号时写入）
    /// - 职位：result.title → JobTitle
    /// - 办公地点：result.work_place → Address（拼接 City）
    /// - 备注：result.remark → Notes（仅当 Notes 为空时写入）
    /// 失败（权限不足、用户未设置、网络异常）全部吞掉，不阻塞消息主流程。
    /// 为避免每条消息都重复调用 API，以下任一条件满足时会整体跳过：
    ///   1. FirstName、Avatar、Emails、Phones、JobTitle、Notes 都已非空（已有完整资料）
    ///   2. 本次抓取未获得任何新字段（已填充过的不再覆盖）
    /// 返回值：始终返回 tracked 的 Customer 实体。
    ///   - 成功时返回入参 customer（可能已被 enrich 更新）。
    ///   - 失败时 catch 块会 ClearChanges 导致 customer 被 detach，此时重新查询获取 tracked 实体返回，
    ///     避免调用方持有 detached 实体导致后续 SaveChangesAsync 静默无效。
    /// </summary>
    private async Task<Customer> TryEnrichCustomerProfileAsync(Customer customer, string staffId)
    {
        try
        {
            // 快速判断：如果所有 enrich 目标字段已有值，则跳过 API 调用，避免浪费配额
            // 注意：姓名需要 FirstName 和 LastName 都有值才算完整
            bool hasName = !string.IsNullOrWhiteSpace(customer.FirstName) && !string.IsNullOrWhiteSpace(customer.LastName);
            bool hasAvatar = !string.IsNullOrWhiteSpace(customer.Avatar);
            bool hasEmail = customer.Emails != null && customer.Emails.Any(e => !string.IsNullOrWhiteSpace(e.EmailAddress));
            bool hasPhone = !string.IsNullOrWhiteSpace(customer.Phones) && customer.GetPhones().Count > 0;
            bool hasJobTitle = !string.IsNullOrWhiteSpace(customer.JobTitle);
            bool hasNotes = !string.IsNullOrWhiteSpace(customer.Notes);
            if (hasName && hasAvatar && hasEmail && hasPhone && hasJobTitle && hasNotes)
                return customer;

            var detail = await _dingDingClient.GetUserDetailAsync(staffId);
            if (detail == null || detail.Value.ValueKind != JsonValueKind.Object)
                return customer;

            var info = detail.Value;
            bool changed = false;

            // 1. 姓名（name 字段一定返回，只要 API 成功）：用中文姓名拆分工具分别填入姓和名
            if (!hasName && info.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                var name = nameEl.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var (lastName, firstName) = ChineseNameSplitter.Split(name);
                    customer.LastName = lastName;
                    customer.FirstName = firstName;
                    changed = true;
                    _logger.LogDebug("钉钉客户补全姓名：CustomerId={CustomerId}, Name={Name}, LastName={LastName}, FirstName={FirstName}",
                        customer.Id, name, lastName, firstName);
                }
            }

            // 2. 头像（仅在用户手动设置了头像时返回该字段；默认头像不返回）
            if (!hasAvatar && info.TryGetProperty("avatar", out var avatarEl) && avatarEl.ValueKind == JsonValueKind.String)
            {
                var avatarUrl = avatarEl.GetString();
                if (!string.IsNullOrWhiteSpace(avatarUrl))
                {
                    var err = CustomerService.ValidateAndNormalizeAvatarUrl(avatarUrl, out var normalized);
                    if (err == null && !string.IsNullOrWhiteSpace(normalized))
                    {
                        customer.Avatar = normalized;
                        changed = true;
                        _logger.LogDebug("钉钉客户补全头像：CustomerId={CustomerId}", customer.Id);
                    }
                }
            }

            // 3. 邮箱（需"邮箱等个人信息"权限）：优先个人邮箱 email，其次组织邮箱 org_email
            if (!hasEmail)
            {
                customer.Emails ??= new List<Email>();

                // 3a. 个人邮箱
                if (info.TryGetProperty("email", out var emailEl) && emailEl.ValueKind == JsonValueKind.String)
                {
                    var email = emailEl.GetString();
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        var normalizedEmail = email.Trim();
                        // 检查邮箱是否已被其他客户占用（包括已删除的客户），避免 Emails.EmailAddress 全局唯一约束冲突
                        var emailOwned = await _unitOfWork.Customers.IsEmailOwnedByAnyCustomerAsync(normalizedEmail, customer.Id);
                        if (emailOwned)
                        {
                            _logger.LogWarning("钉钉客户邮箱已被其他客户占用，跳过添加：CustomerId={CustomerId}, Email={Email}", customer.Id, email);
                        }
                        else
                        {
                            customer.Emails.Add(new Email
                            {
                                EmailAddress = normalizedEmail,
                                Type = 1,
                                CustomerId = customer.Id
                            });
                            changed = true;
                            _logger.LogInformation("钉钉客户邮箱补全成功：CustomerId={CustomerId}, Email={Email}", customer.Id, email);
                        }
                    }
                }

                // 3b. 组织邮箱（作为第二邮箱，Type=2 工作邮箱）
                if (info.TryGetProperty("org_email", out var orgEmailEl) && orgEmailEl.ValueKind == JsonValueKind.String)
                {
                    var orgEmail = orgEmailEl.GetString();
                    if (!string.IsNullOrWhiteSpace(orgEmail))
                    {
                        var normalized = orgEmail.Trim().ToLowerInvariant();
                        if (!customer.Emails.Any(e => e.EmailAddress.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                        {
                            // 检查邮箱是否已被其他客户占用
                            var orgEmailOwned = await _unitOfWork.Customers.IsEmailOwnedByAnyCustomerAsync(normalized, customer.Id);
                            if (orgEmailOwned)
                            {
                                _logger.LogWarning("钉钉客户组织邮箱已被其他客户占用，跳过添加：CustomerId={CustomerId}, OrgEmail={OrgEmail}", customer.Id, orgEmail);
                            }
                            else
                            {
                                customer.Emails.Add(new Email
                                {
                                    EmailAddress = normalized,
                                    Type = 2,
                                    CustomerId = customer.Id
                                });
                                changed = true;
                                _logger.LogInformation("钉钉客户组织邮箱补全成功：CustomerId={CustomerId}, OrgEmail={OrgEmail}", customer.Id, orgEmail);
                            }
                        }
                    }
                }
            }

            // 4. 手机号（需"企业员工手机号信息"权限）+ 国际区号 state_code
            if (!hasPhone && info.TryGetProperty("mobile", out var mobileEl) && mobileEl.ValueKind == JsonValueKind.String)
            {
                var mobile = mobileEl.GetString();
                if (!string.IsNullOrWhiteSpace(mobile))
                {
                    var stateCode = info.TryGetProperty("state_code", out var scEl) && scEl.ValueKind == JsonValueKind.String
                        ? scEl.GetString()?.Trim()
                        : null;
                    var phones = customer.GetPhones();
                    phones.Add(new CustomerPhone
                    {
                        Value = mobile.Trim(),
                        Type = 1,
                        N = !string.IsNullOrEmpty(stateCode) ? $"+{stateCode}" : null
                    });
                    customer.SetPhones(phones);
                    changed = true;
                    _logger.LogDebug("钉钉客户补全手机号：CustomerId={CustomerId}, StateCode={StateCode}", customer.Id, stateCode);
                }
            }

            // 5. 职位（title）
            if (!hasJobTitle && info.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String)
            {
                var title = titleEl.GetString();
                if (!string.IsNullOrWhiteSpace(title))
                {
                    customer.JobTitle = title.Trim();
                    changed = true;
                    _logger.LogDebug("钉钉客户补全职位：CustomerId={CustomerId}, JobTitle={JobTitle}", customer.Id, title);
                }
            }

            // 6. 地址：优先使用 address（真实地址），其次 work_place（办公地点）→ 写入 Address
            //    City 单独从 work_place 填充（办公地点通常是城市级别）
            if (string.IsNullOrWhiteSpace(customer.Address))
            {
                if (info.TryGetProperty("address", out var addrEl) && addrEl.ValueKind == JsonValueKind.String)
                {
                    var addr = addrEl.GetString();
                    if (!string.IsNullOrWhiteSpace(addr))
                    {
                        customer.Address = addr.Trim();
                        changed = true;
                    }
                }

                // address 字段为空时，用 work_place 兜底写入 Address
                if (string.IsNullOrWhiteSpace(customer.Address)
                    && info.TryGetProperty("work_place", out var wpEl) && wpEl.ValueKind == JsonValueKind.String)
                {
                    var workPlace = wpEl.GetString();
                    if (!string.IsNullOrWhiteSpace(workPlace))
                    {
                        customer.Address = workPlace.Trim();
                        changed = true;
                    }
                }
            }

            // 6b. 城市：从 work_place 填充（办公地点通常包含城市信息）
            if (string.IsNullOrWhiteSpace(customer.City)
                && info.TryGetProperty("work_place", out var wpEl2) && wpEl2.ValueKind == JsonValueKind.String)
            {
                var workPlace = wpEl2.GetString();
                if (!string.IsNullOrWhiteSpace(workPlace))
                {
                    customer.City = workPlace.Trim();
                    changed = true;
                }
            }

            // 7. 备注（remark）→ Notes（仅当 Notes 为空时写入，避免覆盖运营已录入的内容）
            if (!hasNotes && info.TryGetProperty("remark", out var remarkEl) && remarkEl.ValueKind == JsonValueKind.String)
            {
                var remark = remarkEl.GetString();
                if (!string.IsNullOrWhiteSpace(remark))
                {
                    customer.Notes = remark.Trim();
                    changed = true;
                }
            }

            if (changed)
            {
                customer.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
                _logger.LogDebug("钉钉客户资料 enrich 完成并持久化：CustomerId={CustomerId}", customer.Id);
            }
            return customer;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "钉钉客户资料补全失败：CustomerId={CustomerId}, StaffId={StaffId}", customer.Id, staffId);
            // 清除变更跟踪器中残留的实体（例如已 Add 但 SaveChanges 失败的 Email），
            // 避免后续主流程 SaveChangesAsync 时重新写入这些已回滚/失败的实体，
            // 导致 UNIQUE constraint failed 等错误传染到会话创建事务，使新会话无法创建。
            _unitOfWork.ClearChanges();

            // ClearChanges 会将所有实体从变更跟踪器移除（包括 customer），导致 customer 被 detach。
            // 若调用方持有 detached 实体，后续修改属性 + SaveChangesAsync 将静默无效（写入 0 行）。
            // 此处重新查询获取 tracked 实体返回，确保调用方拿到的是被 EF Core 跟踪的 customer。
            // 使用 GetByIdTrackedAsync（基于 FindAsync）优先从变更跟踪器查找，未命中则查数据库。
            var trackedCustomer = await _unitOfWork.Customers.GetByIdTrackedAsync(customer.Id);
            return trackedCustomer ?? customer;
        }
    }

    /// <summary>联系方式提取最大尝试次数：超过后即使字段缺失也不再尝试，避免每条消息反复跑正则。</summary>
    private const int MaxContactExtractAttempts = 3;

    /// <summary>
    /// 从客户消息文本中提取联系方式（姓名、手机、邮箱），写入 Customer 对应字段。
    /// 钉钉 /topapi/v2/user/get 接口在权限不足时无法返回邮箱、手机号，
    /// 通过对话引导用户提供后用 ContactInfoExtractor 正则自动识别。
    /// 提取到的字段直接覆盖已有值（覆盖策略）。
    /// </summary>
    /// <returns>true=成功提取并写入至少一个字段；false=未提取到</returns>
    private async Task<bool> TryExtractContactInfoFromMessageAsync(Customer customer, string text, int conversationId)
    {
        try
        {
            // 前置检查：确保 customer 被 EF Core 跟踪。
            if (_unitOfWork.Customers.FindTracked(customer.Id) == null)
            {
                _logger.LogWarning("钉钉客户实体已被 detach，重新获取 tracked 实体：CustomerId={CustomerId}", customer.Id);
                var reloaded = await _unitOfWork.Customers.GetOrCreateByChannelAsync(
                    customer.Channel ?? "dingtalk", customer.ChannelId ?? string.Empty);
                if (reloaded == null)
                {
                    _logger.LogError("钉钉客户实体重新加载失败：CustomerId={CustomerId}", customer.Id);
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
                _logger.LogDebug("钉钉联系方式提取已达最大尝试次数，跳过：CustomerId={CustomerId}, Attempts={Attempts}, Max={Max}",
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
                _logger.LogDebug("钉钉对话姓名提取成功：CustomerId={CustomerId}, Name={Name}, LastName={LastName}, FirstName={FirstName}",
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
                _logger.LogDebug("钉钉对话手机号提取成功：CustomerId={CustomerId}, Phone={Phone}", customer.Id, info.Phone);
            }

            // 3. 邮箱：先清空已有邮箱再写入，实现替换（覆盖策略）
            customer.Emails ??= new List<Email>();
            if (!string.IsNullOrWhiteSpace(info.Email))
            {
                // 邮箱全局唯一约束检查：避免与其他客户（含已删除）的邮箱冲突
                var emailOwned = await _unitOfWork.Customers.IsEmailOwnedByAnyCustomerAsync(info.Email, customer.Id);
                if (emailOwned)
                {
                    _logger.LogWarning("钉钉对话邮箱已被其他客户占用，跳过添加：CustomerId={CustomerId}, Email={Email}", customer.Id, info.Email);
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
                    _logger.LogDebug("钉钉对话邮箱提取成功：CustomerId={CustomerId}, Email={Email}", customer.Id, info.Email);

                    // 将提取到的邮箱回填到会话的 CustomerEmail，确保会话关闭时能发送通知邮件
                    // 仅当会话当前 CustomerEmail 为空时才回填（已有邮箱不覆盖）
                    // 使用 GetByIdForUpdateAsync 获取追踪实体，修改后随 SaveChangesAsync 一起持久化
                    var conversationToUpdate = await _unitOfWork.Conversations.GetByIdForUpdateAsync(conversationId);
                    if (conversationToUpdate != null && string.IsNullOrWhiteSpace(conversationToUpdate.CustomerEmail))
                    {
                        conversationToUpdate.CustomerEmail = info.Email;
                        _logger.LogInformation("钉钉会话邮箱回填成功：ConversationId={ConversationId}, CustomerEmail={Email}",
                            conversationId, info.Email);
                    }
                }
            }

            // 尝试次数已自增，即使 changed=false 也要 SaveChanges 持久化次数
            customer.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();

            if (changed)
            {
                _logger.LogInformation("钉钉对话联系方式提取成功：CustomerId={CustomerId}, Name={Name}, Phone={Phone}, Email={Email}, Attempts={Attempts}",
                    customer.Id, info.Name, info.Phone, info.Email, customer.ContactExtractAttempts);
                return true;
            }

            _logger.LogDebug("钉钉联系方式提取未找到缺失字段，但已记录尝试次数：CustomerId={CustomerId}, Attempts={Attempts}",
                customer.Id, customer.ContactExtractAttempts);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "钉钉对话联系方式提取失败：CustomerId={CustomerId}", customer.Id);
            _unitOfWork.ClearChanges();
            return false;
        }
    }

    private async Task SendAutoReplyAsync(DingDingIncomingMessage message)
    {
        try
        {
            var settings = _settingsProvider.Current;
            if (!settings.AutoReplyEnabled || string.IsNullOrEmpty(settings.AutoReplyMessage))
                return;

            var replySent = false;

            // 优先通过 sessionWebhook 发送（HTTP 回调模式和 Stream 模式均可能携带此字段）
            if (!string.IsNullOrEmpty(message.SessionWebhook))
            {
                // 检查 sessionWebhook 是否在有效期内
                if (message.SessionWebhookExpiredTime > 0)
                {
                    var expiry = DateTimeOffset.FromUnixTimeMilliseconds(message.SessionWebhookExpiredTime).UtcDateTime;
                    if (DateTime.UtcNow > expiry)
                    {
                        _logger.LogInformation("钉钉 sessionWebhook 已过期，尝试通过机器人 API 发送自动回复");
                    }
                    else
                    {
                        replySent = await _dingDingClient.SendBySessionWebhookAsync(
                            message.SessionWebhook, "text", new { content = settings.AutoReplyMessage });
                    }
                }
                else
                {
                    replySent = await _dingDingClient.SendBySessionWebhookAsync(
                        message.SessionWebhook, "text", new { content = settings.AutoReplyMessage });
                }
            }

            // sessionWebhook 为空或发送失败时，通过机器人单聊消息 API 发送（Stream 模式备选方案）
            if (!replySent)
            {
                var robotCode = !string.IsNullOrEmpty(settings.RobotCode) ? settings.RobotCode : settings.AppKey;
                if (!string.IsNullOrEmpty(robotCode) && !string.IsNullOrEmpty(message.SenderStaffId))
                {
                    var content = JsonSerializer.Serialize(new { content = settings.AutoReplyMessage });
                    var processKey = await _dingDingClient.BatchSendOtoMessageAsync(
                        robotCode, new[] { message.SenderStaffId }, "sampleText", content);
                    replySent = !string.IsNullOrEmpty(processKey);
                }
            }

            if (replySent)
                _logger.LogInformation("钉钉自动回复发送成功：StaffId={StaffId}", message.SenderStaffId);
            else
                _logger.LogWarning("钉钉自动回复发送失败：StaffId={StaffId}", message.SenderStaffId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉自动回复发送失败");
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
            _logger.LogError(ex, "钉钉工作流触发失败：ConversationId={ConversationId}, IsNew={IsNew}",
                conversation.Id, isNewConversation);
        }
    }

    private static string BuildConversationSubject(DingDingIncomingMessage message)
    {
        var name = !string.IsNullOrEmpty(message.SenderNick) ? message.SenderNick : message.SenderStaffId;
        return message.MsgType switch
        {
            "text" => $"钉钉: {name} - {Truncate(DingDingEmojiConverter.Convert(message.TextContent), 30)}",
            "picture" => $"钉钉: {name} - 图片消息",
            "audio" => $"钉钉: {name} - 语音消息",
            "video" => $"钉钉: {name} - 视频消息",
            "file" => $"钉钉: {name} - 文件消息: {message.FileName}",
            "richText" => $"钉钉: {name} - 富文本消息",
            _ => $"钉钉: {name} - {message.MsgType} 消息"
        };
    }

    private static string BuildThreadHtmlBody(DingDingIncomingMessage message)
    {
        return message.MsgType switch
        {
            "text" => $"<div class=\"dingding-text\">{HttpUtility.HtmlEncode(DingDingEmojiConverter.Convert(message.TextContent ?? string.Empty)).Replace("\n", "<br>")}</div>",
            "picture" => BuildMediaPlaceholder("📷", "图片消息", "客户发送了一张图片"),
            "audio" => $"<div class=\"dingding-audio\">🎤 <strong>语音消息</strong>{(message.Duration.HasValue ? $" ({message.Duration.Value}s)" : "")}<br>{HttpUtility.HtmlEncode(message.Recognition ?? "语音转文字不可用")}</div>",
            "video" => BuildMediaPlaceholder("🎥", "视频消息", "客户发送了一段视频"),
            "file" => $"<div class=\"dingding-file\">📄 <strong>文件消息</strong><br>{HttpUtility.HtmlEncode(message.FileName ?? "未命名文件")}</div>",
            "richText" => $"<div class=\"dingding-richtext\">📝 <strong>富文本消息</strong><br>{HttpUtility.HtmlEncode(DingDingEmojiConverter.Convert(message.TextContent ?? string.Empty)).Replace("\n", "<br>")}</div>",
            _ => $"<div class=\"dingding-unknown\">[{HttpUtility.HtmlEncode(message.MsgType ?? "unknown")} 消息]</div>"
        };
    }

    private static string BuildMediaPlaceholder(string emoji, string label, string description)
    {
        return $"<div class=\"dingding-media\">{emoji} <strong>{label}</strong><br>{HttpUtility.HtmlEncode(description)}</div>";
    }

    /// <summary>
    /// 事务提交后下载钉钉媒体素材并保存为附件。
    /// 外部 API 调用，失败不影响主流程（线程已持久化）。
    /// 通过 downloadCode + robotCode 调用钉钉 API 获取临时下载链接，再下载实际文件内容。
    /// </summary>
    private async Task DownloadAndSaveMediaAsync(
        DingDingIncomingMessage message,
        Conversation conversation,
        ConversationThread thread,
        int mailboxId)
    {
        if (string.IsNullOrEmpty(message.DownloadCode))
        {
            _logger.LogWarning("钉钉媒体消息无 DownloadCode，跳过下载：MsgId={MsgId}, MsgType={MsgType}",
                message.MsgId, message.MsgType);
            return;
        }

        // robotCode 优先使用配置值，兜底使用 AppKey（企业自建应用两者通常相同）
        var settings = _settingsProvider.Current;
        var robotCode = !string.IsNullOrEmpty(settings.RobotCode) ? settings.RobotCode : settings.AppKey;
        if (string.IsNullOrEmpty(robotCode))
        {
            _logger.LogWarning("钉钉 RobotCode 和 AppKey 均为空，无法下载媒体文件：MsgId={MsgId}", message.MsgId);
            return;
        }

        byte[]? mediaData = null;
        string? extension = null;

        try
        {
            var result = await _dingDingClient.DownloadMediaAsync(message.DownloadCode, robotCode!);
            if (result.HasValue)
            {
                mediaData = result.Value.data;
                extension = result.Value.extension;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "通过 DownloadCode 下载钉钉素材失败：DownloadCode={DownloadCode}", message.DownloadCode);
        }

        if (mediaData == null || mediaData.Length == 0)
        {
            _logger.LogWarning("钉钉素材下载失败，跳过附件创建：MsgId={MsgId}, DownloadCode={DownloadCode}",
                message.MsgId, message.DownloadCode);
            return;
        }

        // 保存文件到磁盘
        var fileDir = mailboxId.ToString();
        var dirPath = Path.Combine(_attachmentBasePath, fileDir);
        Directory.CreateDirectory(dirPath);

        var fileName = BuildMediaFileName(message.MsgType, extension);
        var storedFileName = $"{Guid.NewGuid():N}_{fileName}";
        var fullPath = Path.Combine(dirPath, storedFileName);

        try
        {
            await File.WriteAllBytesAsync(fullPath, mediaData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉素材保存磁盘失败：Path={Path}", fullPath);
            try { if (File.Exists(fullPath)) File.Delete(fullPath); } catch { /* 忽略清理失败 */ }
            return;
        }

        // 创建附件记录
        var mimeType = GetMimeType(message.MsgType, extension);
        var attachment = new Attachment
        {
            ThreadId = thread.Id,
            FileName = fileName,
            StoredFileName = storedFileName,
            MimeType = mimeType,
            Type = GetAttachmentType(message.MsgType),
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
            _logger.LogInformation("钉钉素材已保存为附件：ThreadId={ThreadId}, AttachmentId={AttachmentId}, FileName={FileName}",
                thread.Id, attachment.Id, fileName);
        }
        catch (Exception ex)
        {
            // 附件记录创建失败，删除已写入的文件避免孤儿文件
            _logger.LogError(ex, "钉钉附件记录创建失败，清理磁盘文件：Path={Path}", fullPath);
            try { if (File.Exists(fullPath)) File.Delete(fullPath); } catch { /* 忽略清理失败 */ }
        }
    }

    /// <summary>
    /// 根据消息类型和扩展名构建文件名。
    /// </summary>
    private static string BuildMediaFileName(string? msgType, string? extension)
    {
        extension ??= ".bin";
        if (!extension.StartsWith('.')) extension = "." + extension;
        return msgType?.ToLowerInvariant() switch
        {
            "picture" => $"dingding_image_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}",
            "audio" => $"dingding_audio_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}",
            "video" => $"dingding_video_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}",
            "file" => $"dingding_file_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}",
            _ => $"dingding_media_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}"
        };
    }

    /// <summary>
    /// 根据消息类型和扩展名推断 MIME 类型。
    /// </summary>
    private static string GetMimeType(string? msgType, string? extension)
    {
        var ext = extension?.ToLowerInvariant();
        return (msgType?.ToLowerInvariant(), ext) switch
        {
            ("picture", ".png") => "image/png",
            ("picture", ".gif") => "image/gif",
            ("picture", ".bmp") => "image/bmp",
            ("picture", ".webp") => "image/webp",
            ("picture", _) => "image/jpeg",
            ("audio", ".mp3") => "audio/mpeg",
            ("audio", ".aac") => "audio/aac",
            ("audio", ".wav") => "audio/wav",
            ("audio", ".ogg") => "audio/ogg",
            ("audio", _) => "audio/amr",
            ("video", ".mov") => "video/quicktime",
            ("video", ".avi") => "video/x-msvideo",
            ("video", _) => "video/mp4",
            ("file", ".pdf") => "application/pdf",
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
            "picture" => AttachmentType.Image,
            "audio" => AttachmentType.Audio,
            "video" => AttachmentType.Video,
            "file" => AttachmentType.Other,
            _ => AttachmentType.Other
        };
    }

    private static string Truncate(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLen ? text : text[..maxLen] + "...";
    }
}
