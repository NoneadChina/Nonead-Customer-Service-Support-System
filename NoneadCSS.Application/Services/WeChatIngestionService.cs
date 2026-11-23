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
using NoneadCSS.Domain.Events;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 微信消息接入服务 - 将微信推送的消息转换为会话(Conversation)+线程(Thread)。
/// 严格复刻 EmailIngestionService 的流程：
/// 解析 → 去重 → 客户匹配 → 会话匹配(新建/追加回复) → 事务持久化 → 素材下载 → 触发后置工作流。
/// </summary>
public class WeChatIngestionService : IWeChatIngestionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowService _workflowService;
    private readonly IWeChatSettingsProvider _settingsProvider;
    private readonly IWeChatClient _weChatClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ILogger<WeChatIngestionService> _logger;
    private readonly string _frontendUrl;
    private readonly string _attachmentBasePath;
    private readonly TimeSpan _conversationMatchWindow;
    private readonly IOptions<HttpClientOptions> _httpClientOptions;
    private readonly IWebhookService? _webhookService;

    /// <summary>微信媒体消息类型集合，需要下载素材</summary>
    private static readonly HashSet<string> MediaMsgTypes = new(StringComparer.OrdinalIgnoreCase) { "image", "voice", "video", "shortvideo" };

    public WeChatIngestionService(
        IUnitOfWork unitOfWork,
        IWorkflowService workflowService,
        IWeChatSettingsProvider settingsProvider,
        IWeChatClient weChatClient,
        IHttpClientFactory httpClientFactory,
        INotificationPublisher notificationPublisher,
        IDistributedLockService distributedLockService,
        ILogger<WeChatIngestionService> logger,
        IConfiguration configuration,
        IOptions<IngestionMatchOptions> ingestionMatchOptions,
        IOptions<HttpClientOptions> httpClientOptions,
        IHostEnvironment env,
        IWebhookService? webhookService = null)
    {
        _unitOfWork = unitOfWork;
        _workflowService = workflowService;
        _settingsProvider = settingsProvider;
        _weChatClient = weChatClient;
        _httpClientFactory = httpClientFactory;
        _notificationPublisher = notificationPublisher;
        _distributedLockService = distributedLockService;
        _logger = logger;
        _frontendUrl = configuration["App:FrontendUrl"] ?? "http://localhost:5173";
        // 附件根目录统一解析（T000112）：绝对路径原样、相对/缺失锚定 ContentRoot，与上传侧一致
        _attachmentBasePath = AttachmentPathResolver.ResolveBasePath(
            configuration["Attachment:UploadPath"], env.ContentRootPath);
        _conversationMatchWindow = TimeSpan.FromHours(ingestionMatchOptions.Value.WeChatMatchWindowHours);
        _httpClientOptions = httpClientOptions;
        _webhookService = webhookService;
    }

    private WeChatOptions Options => _settingsProvider.Current;

    public async Task<WeChatIngestionResult> ProcessMessageAsync(WeChatMessageDto message)
    {
        var result = new WeChatIngestionResult { IsEvent = message.IsEvent };

        // 事件推送（关注/取消关注/菜单点击/地理位置等）
        if (message.IsEvent)
        {
            return await HandleEventAsync(message);
        }

        // 确定关联邮箱
        var mailboxId = await ResolveMailboxIdAsync();
        if (mailboxId == null)
        {
            _logger.LogWarning("微信消息处理失败：无可用邮箱，请配置 WeChat:MailboxId 或确保至少有一个活跃邮箱");
            return result;
        }

        // 事务内处理会话与线程
        var ingestionContext = await IngestConversationInTransactionAsync(message, mailboxId.Value);

        if (ingestionContext == null)
        {
            result.IsDuplicate = true;
            return result;
        }

        result.NewConversationCount = ingestionContext.IsNewConversation ? 1 : 0;
        result.ReplyCount = ingestionContext.IsNewConversation ? 0 : 1;
        result.ConversationId = ingestionContext.Conversation.Id;

        // 事务提交后下载媒体素材并关联附件（外部 API 调用，不影响主流程）
        if (MediaMsgTypes.Contains(message.MsgType))
        {
            await DownloadAndSaveMediaAsync(message, ingestionContext.Conversation, ingestionContext.Thread, mailboxId.Value);
        }

        // 设计说明（M-Q6）：TriggerPostIngestionWorkflowsAsync 在事务提交后执行，失败时隔离捕获并 ClearChanges。
        // 引导消息发送与持久化在工作流之后进行：若工作流失败 ClearChanges 仅清除残留待插入实体，
        // 引导消息作为独立 AddAsync + SaveChangesAsync 路径不受影响。F-H3 修复后 WorkflowService 不再 ClearChanges，
        // 此问题已自动缓解，保持现状。
        // 事务提交后触发后置工作流
        await TriggerPostIngestionWorkflowsAsync(ingestionContext.Conversation, ingestionContext.IsNewConversation);

        // SignalR 实时通知前端刷新会话列表和会话详情
        // - mailbox 组：新会话推 conversation_created，已有会话追加回复推 conversation_updated（会话列表刷新）
        // - mailbox 组：客户消息本质是新回复，同时推 reply_created（会话详情页线程列表刷新）
        try
        {
            var eventName = ingestionContext.IsNewConversation ? "conversation_created" : "conversation_updated";
            await _notificationPublisher.NotifyMailboxAsync(
                ingestionContext.Conversation.MailboxId.ToString(),
                eventName,
                new { conversationId = ingestionContext.Conversation.Id });

            // 客户消息也作为 reply_created 推送，让会话详情页监听器自动刷新线程列表
            // 注意：SignalR 事件名用下划线（与 ConversationThreadService 保持一致），非 NoneadCSSEvents 常量的点号
            await _notificationPublisher.NotifyMailboxAsync(
                ingestionContext.Conversation.MailboxId.ToString(),
                "reply_created",
                new { conversationId = ingestionContext.Conversation.Id, threadId = ingestionContext.Thread.Id });

            // 触发 Webhook 通知（失败仅记录日志，不阻塞主流程）
            if (_webhookService != null)
            {
                try
                {
                    await _webhookService.TriggerAsync(eventName, ingestionContext.Conversation.MailboxId, new { conversationId = ingestionContext.Conversation.Id }, "Conversation", ingestionContext.Conversation.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "触发 Webhook 失败：Event={Event}, Entity={EntityType}:{EntityId}", eventName, "Conversation", ingestionContext.Conversation.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "微信消息 SignalR 通知失败：ConversationId={ConversationId}", ingestionContext.Conversation.Id);
        }

        // 软提示：未关注用户首次发消息时，通过客服消息引导关注服务号以获取昵称和头像
        // 仅在新会话时发送一次，避免反复打扰；用户关注后 subscribe 事件自动拉取资料，后续不再触发
        if (ingestionContext.NeedsSubscribeGuidance && ingestionContext.IsNewConversation)
        {
            try
            {
                var guidanceMessage = "您好！感谢您的咨询。关注我们的服务号后可完善您的个人资料（昵称、头像），方便我们为您提供更优质的服务。";
                var sent = await _weChatClient.SendTextMessageAsync(message.FromUserName, guidanceMessage);
                _logger.LogInformation("已发送关注引导提示：OpenId={OpenId}, Sent={Sent}", message.FromUserName, sent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "发送关注引导提示失败：OpenId={OpenId}", message.FromUserName);
            }
        }

        // 联系方式收集引导：新会话且客户缺姓名/手机/邮箱任一字段时，发送引导消息请客户提供
        // 发送成功后，同步作为"用户回复"类型的系统消息写入 ConversationThread，让客服在会话详情页能看到该引导
        if (ingestionContext.NeedsContactGuidance)
        {
            try
            {
                var contactGuidance = "为了更好地为您服务，方便后续发送相关资料，请您回复您的姓名，手机，邮箱地址，谢谢！";
                var sent = await _weChatClient.SendTextMessageAsync(message.FromUserName, contactGuidance);
                if (sent)
                {
                    _logger.LogInformation("已发送联系方式收集引导：OpenId={OpenId}, Sent={Sent}", message.FromUserName, sent);
                    // 写入系统消息到会话线程（Type=Message, SourceVia=User），同步显示在会话记录中
                    try
                    {
                        // 确保引导消息时间戳晚于客户消息（客户消息 CreatedAt 来自渠道服务器时间，可能与服务端 UTC 有偏差）
                        var guidanceTime = DateTime.UtcNow;
                        var convUpdated = ingestionContext.Conversation.UpdatedAt;
                        if (convUpdated >= guidanceTime) guidanceTime = convUpdated.AddSeconds(1);

                        var guidanceThread = new ConversationThread
                        {
                            ConversationId = ingestionContext.Conversation.Id,
                            CustomerId = ingestionContext.Conversation.CustomerId,
                            Type = ThreadType.Message,
                            SourceVia = SourceVia.User,
                            SourceType = SourceType.WeChat,
                            From = "wechat@system",
                            To = message.FromUserName,
                            Body = contactGuidance,
                            SendStatus = SendStatus.Sent,
                            IsInternal = false,
                            MessageId = $"wx-contact-guidance-{Guid.NewGuid():N}",
                            CreatedAt = guidanceTime,
                            UpdatedAt = guidanceTime
                        };
                        await _unitOfWork.Threads.AddAsync(guidanceThread);
                        await _unitOfWork.SaveChangesAsync();
                        _logger.LogInformation("微信联系方式收集引导已写入会话线程：ConversationId={ConversationId}",
                            ingestionContext.Conversation.Id);
                    }
                    catch (Exception thrEx)
                    {
                        _logger.LogWarning(thrEx, "微信联系方式收集引导写入会话线程失败：ConversationId={ConversationId}",
                            ingestionContext.Conversation.Id);
                    }
                }
                else
                {
                    _logger.LogWarning("联系方式收集引导发送失败（未抛异常）：OpenId={OpenId}", message.FromUserName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "发送联系方式收集引导失败：OpenId={OpenId}", message.FromUserName);
            }
        }

        return result;
    }

    /// <summary>
    /// 处理微信事件推送（关注/取消关注/菜单点击/菜单跳转/地理位置上报）。
    /// 关注事件：创建/更新客户记录，并在启用自动回复时返回欢迎语作为被动回复。
    /// 其他事件：记录日志，不创建会话。
    /// </summary>
    private async Task<WeChatIngestionResult> HandleEventAsync(WeChatMessageDto message)
    {
        var evt = message.Event?.ToLowerInvariant() ?? string.Empty;
        var result = new WeChatIngestionResult { IsEvent = true };

        _logger.LogInformation("收到微信事件：Event={Event}, EventKey={EventKey}, OpenId={OpenId}",
            message.Event, message.EventKey, message.FromUserName);

        try
        {
            switch (evt)
            {
                case "subscribe":
                    // 用户关注：创建/更新客户记录，以便后续消息能关联到客户
                    var customer = await _unitOfWork.Customers.GetOrCreateByChannelAsync("wechat", message.FromUserName);
                    if (customer != null)
                    {
                        // 尝试拉取微信用户昵称更新客户资料（失败不影响主流程）
                        try
                        {
                            await TryEnrichCustomerProfileAsync(customer, message.FromUserName);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "微信关注事件 enrich 客户资料失败：OpenId={OpenId}, CustomerId={CustomerId}", message.FromUserName, customer?.Id);
                        }
                    }

                    // 启用自动回复时返回欢迎语作为被动回复
                    if (Options.AutoReplyEnabled && !string.IsNullOrWhiteSpace(Options.AutoReplyMessage))
                    {
                        result.PassiveReplyContent = Options.AutoReplyMessage;
                    }
                    _logger.LogInformation("微信用户关注：OpenId={OpenId}, CustomerId={CustomerId}",
                        message.FromUserName, customer?.Id);
                    break;

                case "unsubscribe":
                    // 用户取消关注：记录日志，不删除客户记录（保留历史会话）
                    _logger.LogInformation("微信用户取消关注：OpenId={OpenId}", message.FromUserName);
                    break;

                case "click":
                    // 菜单点击事件：记录 EventKey，可用于统计或触发自定义逻辑
                    _logger.LogInformation("微信菜单点击：OpenId={OpenId}, EventKey={EventKey}",
                        message.FromUserName, message.EventKey);
                    break;

                case "view":
                    // 菜单跳转事件：记录跳转 URL
                    _logger.LogInformation("微信菜单跳转：OpenId={OpenId}, Url={Url}",
                        message.FromUserName, message.EventKey);
                    break;

                case "location":
                    // 上报地理位置事件：记录坐标
                    _logger.LogInformation("微信地理位置上报：OpenId={OpenId}, Lat={Lat}, Lng={Lng}",
                        message.FromUserName, message.Latitude, message.Longitude);
                    break;

                default:
                    _logger.LogDebug("未处理的微信事件：Event={Event}, EventKey={EventKey}", message.Event, message.EventKey);
                    break;
            }
        }
        catch (Exception ex)
        {
            // 事件处理失败不影响微信服务器重试机制（返回 success）
            _logger.LogError(ex, "微信事件处理异常：Event={Event}, OpenId={OpenId}", evt, message.FromUserName);
        }

        return result;
    }

    /// <summary>
    /// 尝试通过微信用户信息接口拉取资料并更新客户信息。
    /// 注意：微信 2021-12-27 起 user/info 接口对未关注用户不返回昵称和头像，
    /// 但已关注用户仍返回完整资料。本方法通过 subscribe 字段判断关注状态，
    /// 未关注时返回 true 以便调用方引导用户关注。
    /// 失败时自动重试 1 次（1 秒延迟），应对瞬时网络故障。
    /// </summary>
    /// <returns>true=用户未关注（subscribe=0），需要引导关注；false=已关注或无法判断</returns>
    private async Task<bool> TryEnrichCustomerProfileAsync(Customer customer, string openId)
    {
        // 第一次尝试
        var (success, needsGuidance) = await TryFetchWeChatUserProfileOnceAsync(customer, openId);
        if (success)
        {
            return needsGuidance;
        }

        // 第一次失败，延迟 1 秒后重试 1 次（应对瞬时网络故障）
        _logger.LogDebug("微信用户资料拉取第一次失败，1 秒后重试：OpenId={OpenId}", openId);
        await Task.Delay(1000);
        (_, needsGuidance) = await TryFetchWeChatUserProfileOnceAsync(customer, openId);
        return needsGuidance;
    }

    /// <summary>
    /// 单次拉取微信用户资料并更新客户信息（不含重试逻辑）。
    /// </summary>
    /// <returns>(是否成功, 是否需要引导关注)</returns>
    private async Task<(bool success, bool needsGuidance)> TryFetchWeChatUserProfileOnceAsync(Customer customer, string openId)
    {
        try
        {
            var userInfo = await _weChatClient.GetUserInfoAsync(openId);
            if (!userInfo.HasValue || userInfo.Value.ValueKind != JsonValueKind.Object)
            {
                return (false, false);
            }

            var info = userInfo.Value;
            bool changed = false;

            // 检查关注状态：subscribe=0 表示未关注，无法获取昵称和头像
            // subscribe 字段为 0 时，nickname 和 headimgurl 为空（微信 2021-12-27 隐私限制）
            var subscribe = info.TryGetProperty("subscribe", out var sub) && sub.ValueKind == JsonValueKind.Number
                ? sub.GetInt32() : 1; // 默认假设已关注（兼容旧接口）
            var needsGuidance = subscribe == 0;

            // 昵称（2021-12-27 后未关注用户不返回，降级处理）
            // 用中文姓名拆分工具分别填入姓和名
            if (info.TryGetProperty("nickname", out var nick) && nick.ValueKind == JsonValueKind.String)
            {
                var nickname = nick.GetString();
                if (!string.IsNullOrWhiteSpace(nickname)
                    && (string.IsNullOrWhiteSpace(customer.FirstName) || string.IsNullOrWhiteSpace(customer.LastName)))
                {
                    var (lastName, firstName) = ChineseNameSplitter.Split(nickname);
                    customer.LastName = lastName;
                    customer.FirstName = firstName;
                    changed = true;
                }
            }

            // 性别（1=男, 2=女, 0=未知）
            if (info.TryGetProperty("sex", out var sex) && sex.ValueKind == JsonValueKind.Number
                && sex.GetInt32() > 0)
            {
                // 无专门字段存储性别，暂跳过（避免过度修改）
            }

            // 城市/省份/国家（2021-12-27 后未关注用户仍返回）
            var city = info.TryGetProperty("city", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var country = info.TryGetProperty("country", out var co) && co.ValueKind == JsonValueKind.String ? co.GetString() : null;
            var province = info.TryGetProperty("province", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

            if (!string.IsNullOrWhiteSpace(city) && string.IsNullOrWhiteSpace(customer.City))
            {
                customer.City = city.Trim();
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(country) && string.IsNullOrWhiteSpace(customer.Country))
            {
                customer.Country = country.Trim();
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(province) && string.IsNullOrWhiteSpace(customer.State))
            {
                customer.State = province.Trim();
                changed = true;
            }

            // 备注（运营在公众号后台给用户添加的备注 remark）
            if (info.TryGetProperty("remark", out var remarkEl) && remarkEl.ValueKind == JsonValueKind.String)
            {
                var remark = remarkEl.GetString();
                if (!string.IsNullOrWhiteSpace(remark) && string.IsNullOrWhiteSpace(customer.Notes))
                {
                    customer.Notes = remark.Trim();
                    changed = true;
                }
            }

            // 头像 URL（2021-12-27 后未关注用户不返回）
            var headImgUrl = info.TryGetProperty("headimgurl", out var h) && h.ValueKind == JsonValueKind.String
                ? h.GetString() : null;
            if (!string.IsNullOrWhiteSpace(headImgUrl) && string.IsNullOrEmpty(customer.Avatar))
            {
                // 纵深防御：即使来源是微信 API，也校验 URL 协议白名单
                var avatarErr = CustomerService.ValidateAndNormalizeAvatarUrl(headImgUrl, out var normalizedAvatar);
                if (avatarErr == null)
                {
                    customer.Avatar = normalizedAvatar;
                    changed = true;
                }
            }

            if (changed)
            {
                customer.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
                _logger.LogInformation("微信客户资料已更新：CustomerId={CustomerId}, OpenId={OpenId}", customer.Id, openId);
            }

            if (needsGuidance)
            {
                _logger.LogInformation("微信用户未关注服务号，无法获取昵称和头像：OpenId={OpenId}, CustomerId={CustomerId}", openId, customer.Id);
            }

            return (true, needsGuidance);
        }
        catch (Exception ex)
        {
            // 48006 等错误降级处理，不影响主流程
            _logger.LogDebug(ex, "拉取微信用户资料失败（可能因隐私限制）：OpenId={OpenId}", openId);
            return (false, false);
        }
    }

    /// <summary>
    /// 在事务内完成会话匹配/创建、线程创建。
    /// </summary>
    private async Task<WeChatIngestionContext?> IngestConversationInTransactionAsync(
        WeChatMessageDto message, int mailboxId)
    {
        // 用 "wx_{MsgId}" 或 "wx_event_{...}" 作为 MessageId 存入 Threads.MessageId
        var messageId = BuildWeChatMessageId(message);

        // TD-06：在进入事务前获取分布式锁，串行化同一微信消息（MessageId）的并发摄入，消除 TOCTOU 竞态。
        // 锁必须在事务外获取/释放：DistributedLockService 与 IUnitOfWork 共享同一 DbContext，
        // 若在事务内获取，锁的 INSERT 会并入当前事务，导致后续 Rollback 时把锁一并回滚，锁失效。
        var lockKey = $"wechat_ingest:{messageId}";
        var lockHandle = await _distributedLockService.TryAcquireAsync(lockKey, timeoutMs: 0);
        if (lockHandle == null)
        {
            _logger.LogInformation("微信消息正在被其他实例处理，跳过：MessageId={MessageId}", message.MsgId);
            return null;
        }

        try
        {
            using var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                // 1. 消息去重：用 "wx_{MsgId}" 作为 MessageId 存入 Threads.MessageId
                var existingThread = await _unitOfWork.Threads.FindByMessageIdAsync(messageId);
                if (existingThread != null)
                {
                    _logger.LogDebug("微信消息已存在，跳过：MsgId={MsgId}", message.MsgId);
                    await transaction.RollbackAsync();
                    return null;
                }

                // 2. 客户匹配/创建（按 OpenId）
                var customer = await _unitOfWork.Customers.GetOrCreateByChannelAsync("wechat", message.FromUserName);
                if (customer == null)
                {
                    _logger.LogWarning("无法获取或创建微信客户，跳过：OpenId={OpenId}", message.FromUserName);
                    await transaction.RollbackAsync();
                    return null;
                }

                // 3. 尝试匹配已有会话（同 OpenId + 最近 24h 内未关闭的会话）
                var existingConversation = await FindRelatedWeChatConversationAsync(message.FromUserName, mailboxId);

                Conversation conversation;
                ConversationThread thread;
                bool isNewConversation;

                if (existingConversation != null)
                {
                    (conversation, thread) = await AddReplyToExistingConversationAsync(message, customer.Id, existingConversation.Id, messageId);
                    isNewConversation = false;
                    _logger.LogInformation("微信会话 {ConversationId} 收到客户回复，ThreadId={ThreadId}",
                        conversation.Id, thread.Id);
                }
                else
                {
                    (conversation, thread) = await CreateNewConversationAsync(message, customer, mailboxId, messageId);
                    isNewConversation = true;
                    _logger.LogInformation("创建新微信会话：Subject={Subject}，ThreadId={ThreadId}",
                        conversation.Subject, thread.Id);
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

                // 事务提交后异步拉取微信用户资料（昵称、头像），填充客户姓名和头像。
                // 放在事务外调用，避免拉取失败回滚主消息处理事务；
                // TryEnrichCustomerProfileAsync 内部已有 try-catch 隔离 + 1 次重试，失败不影响主流程。
                //
                // 重试策略（轻量重试，无需额外字段）：
                // 1. 立即重试：TryEnrichCustomerProfileAsync 内部失败时自动延迟 1 秒重试 1 次
                // 2. 后续消息补拉取：如果客户姓名或头像仍为空，且距 UpdatedAt 超过 1 小时，
                //    在后续每条普通消息时再次拉取（自然节流，避免频繁调用 API）
                // 3. 引导关注：subscribe=0 时返回 needsGuidance=true，由调用方发送软提示
                var needsSubscribeGuidance = false;
                var profileIncomplete = string.IsNullOrWhiteSpace(customer.FirstName)
                    || string.IsNullOrEmpty(customer.Avatar);
                var hoursSinceLastUpdate = (DateTime.UtcNow - customer.UpdatedAt).TotalHours;

                if (profileIncomplete || hoursSinceLastUpdate > 1)
                {
                    needsSubscribeGuidance = await TryEnrichCustomerProfileAsync(customer, message.FromUserName);
                }

                // 联系方式收集：提取适用于所有消息（新会话+回复），引导仅限新会话
                // 微信 API 不提供用户邮箱，通过对话引导 + 正则提取实现
                // 1. 先尝试从客户当前消息文本中提取姓名、手机、邮箱（客户可能直接回复了信息）
                //    提取方法内部已有最大尝试次数限制（3 次）和字段完整性检查，不会过度提取
                // 2. 若客户缺任一字段且本次是新会话，发送引导消息请客户提供（引导仅发一次）
                var contactExtracted = false;
                if (Options.ContactCollectionEnabled
                    && message.MsgType?.ToLowerInvariant() == "text"
                    && !string.IsNullOrWhiteSpace(message.Content))
                {
                    contactExtracted = await TryExtractContactInfoFromMessageAsync(customer, message.Content, conversation.Id);
                }

                // 判断客户当前是否缺任一字段（姓名、手机、邮箱）
                bool hasName = !string.IsNullOrWhiteSpace(customer.FirstName) || !string.IsNullOrWhiteSpace(customer.LastName);
                bool hasPhone = customer.GetPhones().Count > 0;
                bool hasEmail = customer.Emails != null && customer.Emails.Any(e => !string.IsNullOrWhiteSpace(e.EmailAddress));
                var needsContactGuidance = Options.ContactCollectionEnabled
                    && (contactExtracted == false || !hasName || !hasPhone || !hasEmail)
                    && isNewConversation
                    && (!hasName || !hasPhone || !hasEmail);

                return new WeChatIngestionContext
                {
                    Conversation = conversation,
                    Thread = thread,
                    IsNewConversation = isNewConversation,
                    NeedsSubscribeGuidance = needsSubscribeGuidance,
                    NeedsContactGuidance = needsContactGuidance
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
                    _logger.LogInformation("微信消息并发写入重复，按幂等处理跳过：MessageId={MessageId}, ThreadId={ThreadId}",
                        message.MsgId, duplicate.Id);
                    return null;
                }
                _logger.LogError(ex, "微信消息事务唯一约束冲突且非重复，上报：MessageId={MessageId}", message.MsgId);
                throw;
            }
            catch (Exception ex)
            {
                await transaction.SafeRollbackAsync();
                _logger.LogError(ex, "微信消息处理事务失败：OpenId={OpenId}, MsgId={MsgId}", message.FromUserName, message.MsgId);
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
        WeChatMessageDto message, int customerId, int existingConversationId, string messageId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdTrackedAsync(existingConversationId);
        if (conversation == null)
        {
            throw new InvalidOperationException($"关联会话已被删除：ConversationId={existingConversationId}");
        }

        var thread = CreateThreadFromWeChatMessage(message, customerId, messageId);
        thread.ConversationId = conversation.Id;

        // 已关闭的会话收到新回复时重新打开
        if (conversation.Status == ConversationStatus.Closed)
        {
            conversation.Status = ConversationStatus.Active;
        }

        var messageTime = DateTimeOffset.FromUnixTimeSeconds(message.CreateTime).UtcDateTime;
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
        WeChatMessageDto message, Customer customer, int mailboxId, string messageId)
    {
        var messageTime = DateTimeOffset.FromUnixTimeSeconds(message.CreateTime).UtcDateTime;
        // 防止渠道服务器时钟超前于本服务端，导致客户消息时间戳"未来化"
        if (messageTime > DateTime.UtcNow) messageTime = DateTime.UtcNow;
        var subject = BuildConversationSubject(message, customer.GetFullName());

        var conversation = new Conversation
        {
            Number = await _unitOfWork.Conversations.GetNextNumberAsync(),
            MailboxId = mailboxId,
            Customer = customer, // 使用导航属性而非 CustomerId = customer.Id，确保新客户（Id=0）时 EF Core 能正确做 FK fixup
            CustomerEmail = string.Empty, // 微信客户无邮箱
            Subject = subject,
            Type = ConversationType.Chat, // 微信归类为 Chat 类型
            Status = ConversationStatus.Active,
            State = ConversationState.Published,
            SourceVia = SourceVia.Customer,
            SourceType = SourceType.WeChat,
            CreatedAt = messageTime,
            UpdatedAt = messageTime,
            LastReplyAt = messageTime,
            LastReplyFrom = SourceVia.Customer,
            ThreadsCount = 1,
            HasAttachments = message.MsgType is "image" or "voice" or "video" or "shortvideo",
            CreatedByCustomerId = customer.Id // 新客户时为 0，SaveChanges 后由调用方更新
        };

        conversation.SetPreview(WeChatEmojiConverter.Convert(message.BodyText));

        var thread = CreateThreadFromWeChatMessage(message, customer.Id, messageId);
        thread.IsFirst = true;
        thread.State = ThreadState.Active;

        conversation.Threads.Add(thread);

        await AssignFolderAsync(conversation, mailboxId);

        return (conversation, thread);
    }

    /// <summary>
    /// 从微信消息创建线程实体。Body 使用 HTML 格式（前端用 dangerouslySetInnerHTML 渲染）。
    /// </summary>
    private static ConversationThread CreateThreadFromWeChatMessage(WeChatMessageDto message, int customerId, string messageId)
    {
        var messageTime = DateTimeOffset.FromUnixTimeSeconds(message.CreateTime).UtcDateTime;
        // 防止渠道服务器时钟超前于本服务端，导致客户消息时间戳"未来化"
        if (messageTime > DateTime.UtcNow) messageTime = DateTime.UtcNow;

        return new ConversationThread
        {
            Type = ThreadType.Customer,
            State = ThreadState.Active,
            Body = BuildThreadHtmlBody(message),
            Subject = BuildConversationSubject(message, null),
            From = message.FromUserName, // 存储 OpenId
            To = message.ToUserName, // 存储服务号原始 ID
            MessageId = messageId, // "wx_{MsgId}"，用于去重
            IsInternal = false,
            HasAttachments = MediaMsgTypes.Contains(message.MsgType),
            IsFirst = false,
            SourceVia = SourceVia.Customer,
            SourceType = SourceType.WeChat,
            CreatedByCustomerId = customerId,
            CreatedAt = messageTime,
            UpdatedAt = messageTime
        };
    }

    /// <summary>
    /// 构建线程 HTML 正文（前端用 dangerouslySetInnerHTML 渲染）。
    /// 文本消息转义并保留换行；媒体消息使用语义化 class 的卡片式占位（附件下载后前端展示缩略图）。
    /// 前端 ThreadList 组件通过 Tailwind 任意选择器为以下 class 提供样式：
    ///   .wechat-media / .wechat-media-icon / .wechat-media-info / .wechat-media-title / .wechat-media-desc
    ///   .wechat-location / .wechat-link / .wechat-link-title / .wechat-link-desc / .wechat-link-url
    ///   .wechat-unknown
    /// </summary>
    private static string BuildThreadHtmlBody(WeChatMessageDto message)
    {
        return message.MsgType switch
        {
            // 先将微信表情代码（/:ok、/::D、[Facepalm] 等）转换为 Unicode emoji，再 HTML 转义和换行处理。
            // 微信推送内置表情时 Content 是文本代码而非 emoji，直接存储会话记录会显示原始代码。
            "text" => $"<div class=\"wechat-text\">{HttpUtility.HtmlEncode(WeChatEmojiConverter.Convert(message.Content)).Replace("\n", "<br>")}</div>",
            "image" => BuildMediaPlaceholder("📷", "图片消息", "客户发送了一张图片，请查看下方附件"),
            "voice" => BuildMediaPlaceholder("🎤", "语音消息", "客户发送了一段语音，请查看下方附件"),
            "video" => BuildMediaPlaceholder("🎬", "视频消息", "客户发送了一段视频，请查看下方附件"),
            "shortvideo" => BuildMediaPlaceholder("🎬", "短视频消息", "客户发送了一段短视频，请查看下方附件"),
            "location" => $"<a href=\"https://uri.amap.com/marker?position={HttpUtility.HtmlEncode(message.Longitude)},{HttpUtility.HtmlEncode(message.Latitude)}&name=客户位置&coordinate=gaode&callnative=1\" target=\"_blank\" rel=\"noopener noreferrer\" class=\"wechat-location wechat-location-link\" data-lat=\"{HttpUtility.HtmlEncode(message.Latitude)}\" data-lng=\"{HttpUtility.HtmlEncode(message.Longitude)}\" data-name=\"客户位置\"><div class=\"wechat-media-header\"><span class=\"wechat-media-icon\">📍</span><span class=\"wechat-media-title\">位置消息</span><span class=\"wechat-location-hint\">点击查看地图 →</span></div><div class=\"wechat-media-desc\">经度：{HttpUtility.HtmlEncode(message.Longitude)}<br>纬度：{HttpUtility.HtmlEncode(message.Latitude)}</div></a>",
            "link" => $"<div class=\"wechat-link\"><div class=\"wechat-link-inner\"><span class=\"wechat-media-icon\">🔗</span><div class=\"wechat-link-content\"><div class=\"wechat-link-title\">{HttpUtility.HtmlEncode(message.Title)}</div>{(!string.IsNullOrEmpty(message.Description) ? $"<div class=\"wechat-link-desc\">{HttpUtility.HtmlEncode(message.Description)}</div>" : "")}<a href=\"{HttpUtility.HtmlEncode(message.Url)}\" target=\"_blank\" rel=\"noopener noreferrer\" class=\"wechat-link-url\">{HttpUtility.HtmlEncode(message.Url)}</a></div></div></div>",
            _ => $"<div class=\"wechat-unknown\">[{HttpUtility.HtmlEncode(message.MsgType)} 消息]</div>"
        };
    }

    /// <summary>
    /// 构建微信媒体消息的卡片式占位 HTML。
    /// 使用语义化 class，前端通过 Tailwind 任意选择器渲染为：左侧图标徽章 + 右侧标题/说明，
    /// 整体浅灰底+绿色左边框，与微信主题色保持一致。
    /// </summary>
    private static string BuildMediaPlaceholder(string icon, string title, string description)
    {
        return $"<div class=\"wechat-media\">" +
               $"<div class=\"wechat-media-inner\">" +
               $"<div class=\"wechat-media-icon\">{icon}</div>" +
               $"<div class=\"wechat-media-info\">" +
               $"<div class=\"wechat-media-title\">{HttpUtility.HtmlEncode(title)}</div>" +
               $"<div class=\"wechat-media-desc\">{HttpUtility.HtmlEncode(description)}</div>" +
               $"</div>" +
               $"</div>" +
               $"</div>";
    }

    /// <summary>
    /// 查找同一微信用户在时间窗口内未关闭的会话。
    /// 微信无邮件头引用链，改用 OpenId + 时间窗口匹配。
    /// </summary>
    private async Task<Conversation?> FindRelatedWeChatConversationAsync(string openId, int mailboxId)
    {
        var cutoff = DateTime.UtcNow - _conversationMatchWindow;

        // 查找该 OpenId 在时间窗口内最近一条未关闭的微信会话
        var recentThread = await _unitOfWork.Threads.GetQueryable()
            .Where(t => t.From == openId
                && t.SourceType == SourceType.WeChat
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
    /// 事务提交后下载微信临时素材并保存为附件。
    /// 外部 API 调用，失败不影响主流程（线程已持久化）。
    /// </summary>
    private async Task DownloadAndSaveMediaAsync(WeChatMessageDto message, Conversation conversation, ConversationThread thread, int mailboxId)
    {
        if (string.IsNullOrEmpty(message.MediaId))
        {
            _logger.LogWarning("微信媒体消息无 MediaId，跳过下载：MsgId={MsgId}, MsgType={MsgType}", message.MsgId, message.MsgType);
            return;
        }

        byte[]? mediaData = null;
        string? extension = null;

        // 优先通过 MediaId 下载临时素材
        try
        {
            var result = await _weChatClient.DownloadMediaAsync(message.MediaId);
            if (result.HasValue)
            {
                mediaData = result.Value.data;
                extension = result.Value.extension;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "通过 MediaId 下载微信素材失败：MediaId={MediaId}", message.MediaId);
        }

        // 图片消息可回退到 PicUrl 下载
        if (mediaData == null && message.MsgType == "image" && !string.IsNullOrEmpty(message.PicUrl))
        {
            try
            {
                using var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(_httpClientOptions.Value.WeChatMediaDownloadTimeoutSeconds);
                using var response = await httpClient.GetAsync(message.PicUrl);
                if (response.IsSuccessStatusCode)
                {
                    mediaData = await response.Content.ReadAsByteArrayAsync();
                    extension = ".jpg";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "通过 PicUrl 下载微信图片失败：PicUrl={PicUrl}", message.PicUrl);
            }
        }

        if (mediaData == null || mediaData.Length == 0)
        {
            _logger.LogWarning("微信素材下载失败，跳过附件创建：MsgId={MsgId}, MediaId={MediaId}", message.MsgId, message.MediaId);
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
            _logger.LogError(ex, "微信素材保存磁盘失败：Path={Path}", fullPath);
            // 清理可能部分写入的损坏文件
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
            _logger.LogInformation("微信素材已保存为附件：ThreadId={ThreadId}, AttachmentId={AttachmentId}, FileName={FileName}",
                thread.Id, attachment.Id, fileName);
        }
        catch (Exception ex)
        {
            // 附件记录创建失败，删除已写入的文件避免孤儿文件
            _logger.LogError(ex, "微信附件记录创建失败，清理磁盘文件：Path={Path}", fullPath);
            try { if (File.Exists(fullPath)) File.Delete(fullPath); } catch { /* 忽略清理失败 */ }
        }
    }

    /// <summary>
    /// 根据消息类型和扩展名构建文件名。
    /// </summary>
    private static string BuildMediaFileName(string msgType, string? extension)
    {
        extension ??= ".bin";
        if (!extension.StartsWith('.')) extension = "." + extension;
        return msgType switch
        {
            "image" => $"wechat_image_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}",
            "voice" => $"wechat_voice_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}",
            "video" => $"wechat_video_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}",
            "shortvideo" => $"wechat_shortvideo_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}",
            _ => $"wechat_media_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}"
        };
    }

    /// <summary>
    /// 根据消息类型和扩展名推断 MIME 类型。
    /// </summary>
    private static string GetMimeType(string msgType, string? extension)
    {
        return (msgType, extension?.ToLowerInvariant()) switch
        {
            ("image", ".png") => "image/png",
            ("image", ".gif") => "image/gif",
            ("image", ".bmp") => "image/bmp",
            ("image", _) => "image/jpeg",
            ("voice", ".mp3") => "audio/mpeg",
            ("voice", _) => "audio/amr",
            ("video", _) => "video/mp4",
            ("shortvideo", _) => "video/mp4",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// 根据消息类型获取附件分类。
    /// </summary>
    private static AttachmentType GetAttachmentType(string msgType)
    {
        return msgType switch
        {
            "image" => AttachmentType.Image,
            "voice" => AttachmentType.Audio,
            "video" => AttachmentType.Video,
            "shortvideo" => AttachmentType.Video,
            _ => AttachmentType.Other
        };
    }

    /// <summary>
    /// 事务提交后触发后置工作流。
    /// 工作流执行失败被隔离捕获，不影响主流程。
    /// 工作流内部的事务失败已由 WorkflowService 通过 ClearPendingChanges 清理工作流自身产生的待持久化实体，
    /// 此处无需再调用 ClearChanges（避免清除外层 tracked 实体）。
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
    /// 解析微信会话应归属的邮箱 ID。
    /// 优先使用 WeChat:MailboxId 配置，未配置时使用第一个活跃邮箱。
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
    /// 统一添加 "微信:" 前缀，与飞书/钉钉/WhatsApp 渠道保持一致。
    /// </summary>
    private static string BuildConversationSubject(WeChatMessageDto message, string? senderName)
    {
        // 文本消息先将微信表情代码转换为 emoji，避免会话主题显示 /:ok 这样的原始代码
        var textContent = WeChatEmojiConverter.Convert(message.Content);
        var preview = message.MsgType switch
        {
            "text" => textContent.Length > 50 ? textContent[..50] + "..." : textContent,
            "image" => "[图片消息]",
            "voice" => "[语音消息]",
            "video" or "shortvideo" => "[视频消息]",
            "location" => "[位置消息]",
            "link" => message.Title,
            _ => $"[{message.MsgType} 消息]"
        };

        var name = !string.IsNullOrWhiteSpace(senderName) ? senderName : message.FromUserName;
        var subject = $"微信: {name} - {preview}";
        return subject.Length > 100 ? subject[..100] : subject;
    }

    /// <summary>联系方式提取最大尝试次数：超过后即使字段缺失也不再尝试，避免每条消息反复跑正则。</summary>
    private const int MaxContactExtractAttempts = 3;

    /// <summary>
    /// 从客户消息文本中提取联系方式（姓名、手机、邮箱），写入 Customer 对应字段。
    /// 微信 API 不提供用户邮箱，通过对话引导用户提供后用正则自动识别。
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
                _logger.LogWarning("微信客户实体已被 detach，重新获取 tracked 实体：CustomerId={CustomerId}", customer.Id);
                var reloaded = await _unitOfWork.Customers.GetOrCreateByChannelAsync(
                    customer.Channel ?? "wechat", customer.ChannelId ?? string.Empty);
                if (reloaded == null)
                {
                    _logger.LogError("微信客户实体重新加载失败：CustomerId={CustomerId}", customer.Id);
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
                _logger.LogDebug("微信联系方式提取已达最大尝试次数，跳过：CustomerId={CustomerId}, Attempts={Attempts}, Max={Max}",
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
                _logger.LogDebug("微信对话姓名提取成功：CustomerId={CustomerId}, Name={Name}, LastName={LastName}, FirstName={FirstName}",
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
                _logger.LogDebug("微信对话手机号提取成功：CustomerId={CustomerId}, Phone={Phone}", customer.Id, info.Phone);
            }

            // 3. 邮箱：直接替换已有邮箱（客户在对话中提供的邮箱视为最新信息）
            customer.Emails ??= new List<Email>();
            if (!string.IsNullOrWhiteSpace(info.Email))
            {
                // 邮箱全局唯一约束检查：避免与其他客户（含已删除）的邮箱冲突
                var emailOwned = await _unitOfWork.Customers.IsEmailOwnedByAnyCustomerAsync(info.Email, customer.Id);
                if (emailOwned)
                {
                    _logger.LogWarning("微信对话邮箱已被其他客户占用，跳过添加：CustomerId={CustomerId}, Email={Email}", customer.Id, info.Email);
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
                    _logger.LogDebug("微信对话邮箱提取成功：CustomerId={CustomerId}, Email={Email}", customer.Id, info.Email);

                    // 将提取到的邮箱回填到会话的 CustomerEmail，确保会话关闭时能发送通知邮件
                    // 仅当会话当前 CustomerEmail 为空时才回填（已有邮箱不覆盖）
                    // 使用 GetByIdForUpdateAsync 获取追踪实体，修改后随 SaveChangesAsync 一起持久化
                    var conversationToUpdate = await _unitOfWork.Conversations.GetByIdForUpdateAsync(conversationId);
                    if (conversationToUpdate != null && string.IsNullOrWhiteSpace(conversationToUpdate.CustomerEmail))
                    {
                        conversationToUpdate.CustomerEmail = info.Email;
                        _logger.LogInformation("微信会话邮箱回填成功：ConversationId={ConversationId}, CustomerEmail={Email}",
                            conversationId, info.Email);
                    }
                }
            }

            // 尝试次数已自增，即使 changed=false 也要 SaveChanges 持久化次数
            customer.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();

            if (changed)
            {
                _logger.LogInformation("微信对话联系方式提取成功：CustomerId={CustomerId}, Name={Name}, Phone={Phone}, Email={Email}, Attempts={Attempts}",
                    customer.Id, info.Name, info.Phone, info.Email, customer.ContactExtractAttempts);
                return true;
            }

            _logger.LogDebug("微信联系方式提取未找到缺失字段，但已记录尝试次数：CustomerId={CustomerId}, Attempts={Attempts}",
                customer.Id, customer.ContactExtractAttempts);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "微信对话联系方式提取失败：CustomerId={CustomerId}", customer.Id);
            _unitOfWork.ClearChanges();
            return false;
        }
    }

    /// <summary>
    /// 构建微信消息的去重 ID，加 "wx_" 前缀避免与邮件 Message-Id 冲突。
    /// 事件推送无 MsgId，使用 OpenId+Event+CreateTime 作为去重键。
    /// </summary>
    private static string BuildWeChatMessageId(WeChatMessageDto message)
    {
        if (!string.IsNullOrEmpty(message.MsgId))
        {
            return $"wx_{message.MsgId}";
        }

        // 事件推送无 MsgId
        return $"wx_event_{message.FromUserName}_{message.Event}_{message.CreateTime}";
    }

    private sealed class WeChatIngestionContext
    {
        public required Conversation Conversation { get; init; }
        public required ConversationThread Thread { get; init; }
        public required bool IsNewConversation { get; init; }
        /// <summary>是否需要引导未关注用户关注服务号（user/info 返回 subscribe=0 时为 true）</summary>
        public bool NeedsSubscribeGuidance { get; init; }
        /// <summary>是否需要发送联系方式收集引导消息（新会话 + 客户缺姓名/手机/邮箱任一字段时为 true）</summary>
        public bool NeedsContactGuidance { get; init; }
    }
}
