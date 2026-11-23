using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 在线聊天服务实现
/// </summary>
public class ChatService : IChatService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly INotificationHubService _notificationHub;
    private readonly IWorkflowService _workflowService;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IMailTemplateService _mailTemplateService;
    private readonly IEmailQueueService _emailQueueService;
    private readonly ISatisfactionTokenGenerator _satisfactionTokenGenerator;
    private readonly IFrontendUrlResolver _frontendUrlResolver;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatService> _logger;
    private readonly ChatOptions _chatOptions;
    private readonly IWebhookService? _webhookService;
    private readonly ILocalizationService? _localization;

    /// <summary>
    /// 匹配消息内容中的 @提及 标记,格式: @[姓名]
    /// 示例: @[张三] 或 @[Zhang San]
    /// </summary>
    private static readonly Regex MentionPattern = new(@"@\[([^\]]+)\]", RegexOptions.Compiled);

    /// <summary>
    /// 客户未评分超时时间：客服主动发起关闭后，等待 N 分钟仍未评分则自动关闭会话。
    /// 需求指定：3 分钟。
    /// </summary>
    public static readonly TimeSpan RatingWaitTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// 脱敏 SessionToken：仅保留前 4 位和后 4 位，中间用 **** 代替。
    /// 防止日志中泄漏完整 token 被用于会话劫持。
    /// </summary>
    private static string MaskToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return "(null)";
        return token.Length <= 8 ? "****" : $"{token[..4]}****{token[^4..]}";
    }

    /// <summary>
    /// T6: 附件 URL 协议白名单——仅允许 http/https 绝对地址或相对路径，
    /// 拒绝 javascript:/data:/vbscript: 等危险协议（防存储型 XSS）。
    /// 非法值返回 null（该消息将不带附件渲染）。
    /// </summary>
    private static string? NormalizeAttachmentUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        var trimmed = url.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return uri.Scheme is "http" or "https" ? trimmed : null;
        }
        // 相对路径（/api/... 或 ./ ../）
        if (trimmed.StartsWith('/') || trimmed.StartsWith("./") || trimmed.StartsWith("../"))
        {
            return trimmed;
        }
        return null;
    }

    /// <summary>
    /// 在线聊天手机号宽松清洗（仅保留数字和 +，去除空格/横线/控制字符），
    /// 然后按中国大陆手机号规则规范化：
    /// - 接受 11 位纯数字 1[3-9]xxxxxxxxx → 原样保留
    /// - 接受 +861[3-9]xxxxxxxxx / 861[3-9]xxxxxxxxx / 00861[3-9]xxxxxxxxx → 规范化为 +86 前缀形式
    /// - 其他格式（如 7~20 位非大陆号）：宽松接受，供国际访客使用（DataAnnotations 已经过前端基础校验，
    ///   后端此处执行二次清洗并放宽到 7~20 位数字，避免 Customer.Phones 被 ValidateAndNormalizePhone 拒绝）
    /// </summary>
    private static (bool Ok, string Normalized, string? Error) NormalizeChatCustomerPhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (false, string.Empty, "手机号不能为空");
        // 只保留数字和 +
        var sanitized = Regex.Replace(raw.Trim(), @"[^\d\+]", string.Empty);
        if (string.IsNullOrEmpty(sanitized))
            return (false, string.Empty, "手机号不合法");

        // 情况 1：+861[3-9]xxxxxxxxx → 保留 +86 前缀
        if (sanitized.StartsWith("+861", StringComparison.Ordinal) && sanitized.Length == 14)
            return (true, sanitized, null);

        // 情况 2：00861[3-9]xxxxxxxxx → 转 +86 前缀
        if (sanitized.StartsWith("00861", StringComparison.Ordinal) && sanitized.Length == 15)
            return (true, $"+86{sanitized[4..]}", null);

        // 情况 3：861[3-9]xxxxxxxxx → 转 +86 前缀
        if (sanitized.StartsWith("861", StringComparison.Ordinal) && sanitized.Length == 13)
            return (true, $"+{sanitized}", null);

        // 情况 4：11 位 1[3-9]xxxxxxxxx（纯数字大陆手机号）
        if (sanitized.Length == 11 && sanitized[0] == '1' &&
            sanitized[1] is >= '3' and <= '9' && sanitized.All(char.IsDigit))
            return (true, sanitized, null);

        // 情况 5：国际号码宽松接受（7~20 位数字，可含单前导 +）
        if (sanitized.Count(c => c == '+') > 1)
            return (false, sanitized, "手机号包含非法字符：多个 +");
        if (!sanitized.StartsWith('+') && !sanitized.All(char.IsDigit))
            return (false, sanitized, "手机号包含非法字符");
        int digitCount = sanitized.Count(char.IsDigit);
        if (digitCount is < 7 or > 20)
            return (false, sanitized, $"手机号位数 {digitCount} 不合法（允许 7~20 位）");

        return (true, sanitized, null);
    }

    public ChatService(
        IUnitOfWork unitOfWork,
        ICurrentUserAccessor currentUserAccessor,
        INotificationHubService notificationHub,
        IWorkflowService workflowService,
        INotificationPublisher notificationPublisher,
        IMailTemplateService mailTemplateService,
        IEmailQueueService emailQueueService,
        ISatisfactionTokenGenerator satisfactionTokenGenerator,
        IFrontendUrlResolver frontendUrlResolver,
        IConfiguration configuration,
        ILogger<ChatService> logger,
        IOptions<ChatOptions> chatOptions,
        IWebhookService? webhookService = null,
        ILocalizationService? localization = null)
    {
        _unitOfWork = unitOfWork;
        _currentUserAccessor = currentUserAccessor;
        _notificationHub = notificationHub;
        _workflowService = workflowService;
        _notificationPublisher = notificationPublisher;
        _mailTemplateService = mailTemplateService;
        _emailQueueService = emailQueueService;
        _satisfactionTokenGenerator = satisfactionTokenGenerator;
        _frontendUrlResolver = frontendUrlResolver;
        _configuration = configuration;
        _logger = logger;
        _chatOptions = chatOptions.Value;
        _webhookService = webhookService;
        _localization = localization;
    }

    // ====== 会话管理（客服侧） ======

    public async Task<PagedResponse<ChatSessionDto>> GetSessionsAsync(ChatSessionQuery query)
    {
        // 权限校验（defense-in-depth）：非超管用户需限制到可访问邮箱，
        // 防止不传 mailboxId 时越权拉取所有邮箱的会话（IDOR）
        if (!_currentUserAccessor.IsSuperAdmin)
        {
            if (!query.MailboxId.HasValue)
            {
                // 不传 mailboxId 时，自动限制到第一个可访问邮箱
                var accessibleIds = await _currentUserAccessor.GetAccessibleMailboxIdsAsync();
                if (accessibleIds == null || accessibleIds.Length == 0)
                {
                    return new PagedResponse<ChatSessionDto>
                    {
                        Data = new List<ChatSessionDto>(),
                        CurrentPage = query.Page,
                        PageSize = query.PageSize,
                        TotalItems = 0,
                        TotalPages = 0
                    };
                }
                query.MailboxId = accessibleIds[0];
            }
            else if (!await _currentUserAccessor.CanAccessMailboxAsync(query.MailboxId))
            {
                // 传了 mailboxId 时，校验是否有权访问该邮箱
                return new PagedResponse<ChatSessionDto>
                {
                    Data = new List<ChatSessionDto>(),
                    CurrentPage = query.Page,
                    PageSize = query.PageSize,
                    TotalItems = 0,
                    TotalPages = 0
                };
            }
        }

        var items = await _unitOfWork.ChatSessions.GetListAsync(
            query.Status, query.MailboxId, query.UserId, query.CustomerId,
            query.Keyword, query.StartDate, query.EndDate,
            query.Page, query.PageSize);

        var total = await _unitOfWork.ChatSessions.CountAsync(
            query.Status, query.MailboxId, query.UserId, query.CustomerId,
            query.Keyword, query.StartDate, query.EndDate);

        // 批量预加载所有会话的消息数，避免 N+1 查询
        var messageCounts = await _unitOfWork.ChatMessages.GetCountBySessionIdsAsync(items.Select(s => s.Id));

        var dtos = new List<ChatSessionDto>();
        foreach (var s in items)
        {
            var dto = s.ToDto();
            dto.MessageCount = messageCounts.GetValueOrDefault(s.Id, 0);
            dtos.Add(dto);
        }

        return new PagedResponse<ChatSessionDto>
        {
            Data = dtos,
            CurrentPage = query.Page,
            PageSize = query.PageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)Math.Max(query.PageSize, 1))
        };
    }

    public async Task<ChatSessionDto?> GetSessionByIdAsync(int id)
    {
        var s = await _unitOfWork.ChatSessions.GetByIdAsync(id);
        if (s == null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(s.MailboxId))
            return null;

        var dto = s.ToDto();
        dto.MessageCount = await _unitOfWork.ChatMessages.CountAsync(sessionId: s.Id);
        return dto;
    }

    public async Task<ChatSessionDto?> GetSessionByTokenAsync(string sessionToken)
    {
        var s = await _unitOfWork.ChatSessions.GetByTokenAsync(sessionToken);
        if (s == null) return null;
        var dto = s.ToDto();
        dto.MessageCount = await _unitOfWork.ChatMessages.CountAsync(sessionId: s.Id);
        return dto;
    }

    public async Task<ChatSessionDto?> GetSessionByConversationIdAsync(int conversationId)
    {
        var s = await _unitOfWork.ChatSessions.GetByConversationIdAsync(conversationId);
        if (s == null) return null;
        // 工单详情页反查聊天：也需要校验 mailbox 权限（和会话详情权限一致）
        if (!await _currentUserAccessor.CanAccessMailboxAsync(s.MailboxId))
            return null;
        var dto = s.ToDto();
        dto.MessageCount = await _unitOfWork.ChatMessages.CountAsync(sessionId: s.Id);
        return dto;
    }

    public async Task<ChatSessionDto> AssignSessionAsync(int sessionId, int userId, string? ifMatch = null)
    {
        var session = await _unitOfWork.ChatSessions.GetByIdTrackedAsync(sessionId);
        if (session == null) throw new InvalidOperationException("会话不存在");

        if (!await _currentUserAccessor.CanAccessMailboxAsync(session.MailboxId))
            throw new InvalidOperationException("无权访问该会话");

        ConcurrencyCheckHelper.Check(
            session,
            session.RowVersion,
            ifMatch,
            entityType: "ChatSession",
            entityId: session.Id,
            latestFactory: () => session.ToDto(),
            _logger);

        var user = await _unitOfWork.Users.GetByIdAsync(userId);

        session.UserId = userId;
        session.Status = "active";
        session.AgentLastSeenAt = DateTime.UtcNow;
        session.AgentUnreadCount = 0;
        session.UpdatedAt = DateTime.UtcNow;

        // 系统消息：客服接入
        await _unitOfWork.ChatMessages.AddAsync(new ChatMessage
        {
            SessionId = sessionId,
            SenderType = "system",
            MessageType = "system",
            Content = user != null ? $"客服 {user.GetFullName()} 已接入对话" : "客服已接入对话",
            SenderName = "system",
            IsRead = true,
            CreatedAt = DateTime.UtcNow
        });

        await _unitOfWork.SaveChangesAsync();
        return await GetSessionOrThrowAsync(sessionId);
    }

    public async Task<ChatSessionDto> CloseSessionAsync(int sessionId, bool requestRating = true, string? ifMatch = null)
    {
        var session = await _unitOfWork.ChatSessions.GetByIdTrackedAsync(sessionId);
        if (session == null) throw new InvalidOperationException("会话不存在");

        if (!await _currentUserAccessor.CanAccessMailboxAsync(session.MailboxId))
            throw new InvalidOperationException("无权访问该会话");

        ConcurrencyCheckHelper.Check(
            session,
            session.RowVersion,
            ifMatch,
            entityType: "ChatSession",
            entityId: session.Id,
            latestFactory: () => session.ToDto(),
            _logger);

        // 如果已经关闭或已转工单：直接返回，幂等
        if (session.Status is "closed" or "transferred")
            return await GetSessionOrThrowAsync(sessionId);

        var now = DateTime.UtcNow;

        if (!requestRating)
        {
            // 直接关闭分支：走 ReallyClose 逻辑（Status=closed + ClosedAt + 发送关闭邮件）
            await ReallyCloseChatSessionAsync(session, closeReasonSystemMsg: null);
            return await GetSessionOrThrowAsync(sessionId);
        }

        // ===== 两阶段关闭 第一阶段：进入等待评分状态 =====
        session.Status = "awaiting_rating";
        session.RatingRequestedAt = now;
        session.RatingTimeoutAt = now.Add(RatingWaitTimeout);
        session.UpdatedAt = now;

        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("在线聊天进入等待评分阶段：SessionId={SessionId}, CustomerEmail={CustomerEmail}, " +
                               "RatingRequestedAt={RatingRequestedAt}, RatingTimeoutAt={RatingTimeoutAt}",
            session.Id, session.CustomerEmail, session.RatingRequestedAt, session.RatingTimeoutAt);

        // 触发系统工作流（出错记录日志但不影响主流程返回，避免因工作流挂导致前端卡关闭）
        try
        {
            await _workflowService.ExecuteForChatSessionAsync(
                session.Id,
                WorkflowConst.TriggerChatCloseRequested);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发 [聊天]关闭满意度消息 工作流失败（chat_close_requested），SessionId={SessionId}", session.Id);
        }

        return await GetSessionOrThrowAsync(sessionId);
    }

    /// <summary>
    /// 两阶段关闭 第二阶段：真正关闭 ChatSession（Status=closed, ClosedAt=UtcNow），
    /// 可选写入关闭原因 system message，并按需求发送在线聊天渠道会话关闭通知邮件（含满意度链接）。
    /// 本方法不抛异常：邮件发送或工作流出错仅记录日志，保证会话始终能关闭。
    /// 调用方需要先通过仓储 GetByIdTrackedAsync 获取 tracked 的 session 对象并传进来。
    /// 对 ChatRatingTimeoutService 后台服务公开（不同程序集）。
    /// </summary>
    public async Task ReallyCloseChatSessionAsync(ChatSession session, string? closeReasonSystemMsg = null)
    {
        if (session.Status == "closed") return; // 幂等

        var now = DateTime.UtcNow;
        session.Status = "closed";
        session.ClosedAt = now;
        session.UpdatedAt = now;

        // 可选：写入 system 消息（例如"等待超时，会话已自动关闭" / "会话已关闭"）
        if (!string.IsNullOrWhiteSpace(closeReasonSystemMsg))
        {
            await _unitOfWork.ChatMessages.AddAsync(new ChatMessage
            {
                SessionId = session.Id,
                SenderType = "system",
                MessageType = "system",
                Content = closeReasonSystemMsg,
                SenderName = "system",
                IsRead = true,
                CreatedAt = now
            });
        }

        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("在线聊天真正关闭：SessionId={SessionId}, Status={Status}, Rating={Rating}, " +
                               "Reason={CloseReason}",
            session.Id, session.Status, session.Rating?.ToString() ?? "(null)",
            string.IsNullOrWhiteSpace(closeReasonSystemMsg) ? "(无)" : closeReasonSystemMsg);

        // ====== 发送在线聊天渠道会话关闭通知邮件（需求3） ======
        // 条件：CustomerEmail 非空；异常只记录日志（不影响会话关闭）
        if (!string.IsNullOrWhiteSpace(session.CustomerEmail))
        {
            try
            {
                await EnqueueChatSessionClosedEmailAsync(session);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送在线聊天会话关闭通知邮件失败（chat-session-closed），SessionId={SessionId}, To={To}",
                    session.Id, session.CustomerEmail);
            }
        }
    }

    /// <summary>
    /// 按 MailTemplate.Slug=chat-session-closed（Type=ChatSessionClosed）渲染纯文本模板并入队邮件发送。
    /// 分离出来供 ChatService、后台 Worker 复用。渲染失败（模板禁用/缺失）会记录警告，不抛异常。
    /// </summary>
    private async Task EnqueueChatSessionClosedEmailAsync(ChatSession session)
    {
        var template = await _mailTemplateService.GetBySlugAsync("chat-session-closed");
        if (template == null)
        {
            _logger.LogWarning("未找到 chat-session-closed 邮件模板，跳过发送关闭通知（SessionId={SessionId}）", session.Id);
            return;
        }
        if (!template.IsActive)
        {
            _logger.LogInformation("chat-session-closed 邮件模板已禁用，跳过发送关闭通知（SessionId={SessionId}）", session.Id);
            return;
        }

        var variables = await BuildChatClosedEmailVariablesAsync(session);
        var rendered = await _mailTemplateService.RenderTemplateFullAsync(template.Id, variables);

        // 需求3：纯文本。BodyText 必非空（空的话兜底用 Subject），BodyHtml 即使空也直接传
        var bodyText = !string.IsNullOrWhiteSpace(rendered.BodyText) ? rendered.BodyText : rendered.Subject;
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            _logger.LogWarning("chat-session-closed 模板渲染后正文为空，跳过发送（SessionId={SessionId}）", session.Id);
            return;
        }

        // 使用 CustomerEmail 直接入队（MailboxId 沿用 ChatSession.MailboxId，没有则传 0）
        await _emailQueueService.EnqueueEmailAsync(
            mailboxId: session.MailboxId ?? 0,
            to: session.CustomerEmail!,
            subject: rendered.Subject,
            body: bodyText,
            attachmentIds: null,
            cc: null,
            bcc: null,
            threadId: null,
            inReplyTo: null,
            references: null,
            mailType: 1,
            userId: null,
            customerId: session.CustomerId);

        _logger.LogInformation("在线聊天会话关闭通知邮件已入队：SessionId={SessionId}, To={To}, TemplateId={TemplateId}",
            session.Id, session.CustomerEmail, template.Id);
    }

    /// <summary>
    /// 构造 chat-session-closed 邮件模板的变量字典。与会话上下文 + 满意度评价链接生成绑定。
    /// 与 SendEmailActionHandler / SendImMessageActionHandler 中满意度 URL 双路径生成保持一致：
    ///   - 有 CustomerEmail → 邮箱生成 token；否则用 Customer.ChannelId。
    ///   - 对在线聊天（Widget 直接接入无 ChannelId 且 ConversationId 为 null 的未转工单 ChatSession）：
    ///     放宽允许不生成 token，satisfaction_url 为空字符串（由前端自行拼接或后续补 token 方案）。
    /// </summary>
    private async Task<Dictionary<string, string>> BuildChatClosedEmailVariablesAsync(ChatSession session)
    {
        var frontendUrl = _frontendUrlResolver.GetFrontendUrl();
        Customer? customer = null;
        if (session.CustomerId.HasValue)
        {
            customer = await _unitOfWork.Customers.GetByIdAsync(session.CustomerId.Value);
        }
        User? assignee = null;
        if (session.UserId.HasValue)
        {
            assignee = await _unitOfWork.Users.GetByIdAsync(session.UserId.Value);
        }

        var customerName = !string.IsNullOrWhiteSpace(session.CustomerName)
            ? session.CustomerName
            : customer != null ? $"{customer.FirstName} {customer.LastName}".Trim() : string.Empty;
        var agentName = assignee != null ? $"{assignee.FirstName} {assignee.LastName}".Trim() : string.Empty;

        var variables = new Dictionary<string, string>
        {
            ["chat_session_id"] = session.Id.ToString(),
            ["customer_name"] = customerName,
            ["company_name"] = _configuration["CompanyName"] ?? "拓德科技",
            ["chat_created_at"] = session.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            ["agent_name"] = agentName,
            ["chat_subject"] = session.Subject ?? string.Empty,
            ["customer_email"] = session.CustomerEmail ?? string.Empty,
            ["customer_phone"] = session.CustomerPhone ?? string.Empty,
            ["satisfaction_url"] = string.Empty
        };

        // 满意度 URL：优先用 CustomerEmail（邮箱生成 token → 通用 satisfaction 页）
        // 若 Email 为空但有 Customer + ChannelId（IM 渠道转来的聊天），用 ChannelId 生成
        if (!string.IsNullOrWhiteSpace(session.CustomerEmail))
        {
            // 如果已转工单（ConversationId 有值），用 ConversationId 作为满意度评价主体；否则用 0 兜底
            var subjectId = session.ConversationId ?? session.Id;
            var token = _satisfactionTokenGenerator.GenerateToken(subjectId, session.CustomerEmail);
            variables["satisfaction_url"] = $"{frontendUrl}/satisfaction/{token}";
        }
        else if (customer != null && !string.IsNullOrWhiteSpace(customer.ChannelId))
        {
            var subjectId = session.ConversationId ?? session.Id;
            var token = _satisfactionTokenGenerator.GenerateTokenByChannelId(subjectId, customer.ChannelId);
            variables["satisfaction_url"] = $"{frontendUrl}/satisfaction/{token}";
        }
        else if (session.ConversationId.HasValue)
        {
            // 无 Email 无 ChannelId 但已转工单：尝试 Conversation.CustomerEmail（邮箱渠道工单必然有）
            var conv = await _unitOfWork.Conversations.GetByIdAsync(session.ConversationId.Value);
            if (conv != null && !string.IsNullOrWhiteSpace(conv.CustomerEmail))
            {
                var token = _satisfactionTokenGenerator.GenerateToken(conv.Id, conv.CustomerEmail);
                variables["satisfaction_url"] = $"{frontendUrl}/satisfaction/{token}";
            }
        }

        // 兜底：所有路径都未生成 satisfaction_url 时，使用聊天满意度页面（前端自行处理）
        if (string.IsNullOrWhiteSpace(variables["satisfaction_url"]))
        {
            variables["satisfaction_url"] = $"{frontendUrl}/chat/satisfaction/{session.Id}";
        }

        return variables;
    }

    public async Task<List<ChatSessionDto>> GetActiveSessionsAsync(int? mailboxId = null)
    {
        // 权限校验（defense-in-depth，与 GetSessionsAsync 一致）：
        // 非超管用户限制到可访问邮箱，防止枚举所有邮箱的活跃会话（跨邮箱越权）
        int? effectiveMailboxId = mailboxId;
        if (!_currentUserAccessor.IsSuperAdmin)
        {
            if (!effectiveMailboxId.HasValue)
            {
                var accessibleIds = await _currentUserAccessor.GetAccessibleMailboxIdsAsync();
                if (accessibleIds == null || accessibleIds.Length == 0)
                {
                    return new List<ChatSessionDto>();
                }
                effectiveMailboxId = accessibleIds[0];
            }
            else if (!await _currentUserAccessor.CanAccessMailboxAsync(effectiveMailboxId))
            {
                return new List<ChatSessionDto>();
            }
        }

        var items = await _unitOfWork.ChatSessions.GetListAsync(
            status: "active", mailboxId: effectiveMailboxId, pageSize: 500);

        // 批量预加载所有会话的消息数，避免 N+1 查询
        var messageCounts = await _unitOfWork.ChatMessages.GetCountBySessionIdsAsync(items.Select(s => s.Id));

        var dtos = new List<ChatSessionDto>();
        foreach (var s in items)
        {
            var dto = s.ToDto();
            dto.MessageCount = messageCounts.GetValueOrDefault(s.Id, 0);
            dtos.Add(dto);
        }
        return dtos;
    }

    public async Task<ChatOverviewResponse> GetOverviewAsync()
    {
        // 权限校验（defense-in-depth）：非超管用户仅统计可访问邮箱的会话数据
        int[]? mailboxIds = null;
        if (!_currentUserAccessor.IsSuperAdmin)
        {
            var accessibleIds = await _currentUserAccessor.GetAccessibleMailboxIdsAsync();
            if (accessibleIds == null || accessibleIds.Length == 0)
            {
                return new ChatOverviewResponse
                {
                    Stats = new Dictionary<string, int>
                    {
                        ["total"] = 0, ["waiting"] = 0, ["active"] = 0,
                        ["closed"] = 0, ["transferred"] = 0, ["unread"] = 0
                    },
                    ActiveSessions = new List<ChatSessionDto>(),
                    WaitingSessions = new List<ChatSessionDto>(),
                    StatusOptions = BuildLocalizedStatusOptions()
                };
            }
            mailboxIds = accessibleIds;
        }

        var stats = new Dictionary<string, int>
        {
            ["total"] = await _unitOfWork.ChatSessions.CountAsync(mailboxIds: mailboxIds),
            ["waiting"] = await _unitOfWork.ChatSessions.CountByStatusAsync("waiting", mailboxIds: mailboxIds),
            ["active"] = await _unitOfWork.ChatSessions.CountByStatusAsync("active", mailboxIds: mailboxIds),
            ["closed"] = await _unitOfWork.ChatSessions.CountByStatusAsync("closed", mailboxIds: mailboxIds),
            ["transferred"] = await _unitOfWork.ChatSessions.CountByStatusAsync("transferred", mailboxIds: mailboxIds),
            ["unread"] = await _unitOfWork.ChatSessions.SumUnreadCountAsync(mailboxIds: mailboxIds)
        };

        var active = await _unitOfWork.ChatSessions.GetListAsync(status: "active", mailboxIds: mailboxIds, pageSize: 50);
        var waiting = await _unitOfWork.ChatSessions.GetListAsync(status: "waiting", mailboxIds: mailboxIds, pageSize: 50);

        return new ChatOverviewResponse
        {
            Stats = stats,
            ActiveSessions = active.Select(s => s.ToDto()).ToList(),
            WaitingSessions = waiting.Select(s => s.ToDto()).ToList(),
            StatusOptions = BuildLocalizedStatusOptions()
        };
    }

    /// <summary>
    /// 会话状态目录（等待接待/进行中/已关闭/已转工单）：走 Messages 资源（en / zh-Hans），
    /// 词条缺失或未注入本地化服务时回退 zh-Hans 原文，保证 zh-Hans 输出逐字不变。
    /// </summary>
    private Dictionary<string, string> BuildLocalizedStatusOptions() => new(StringComparer.Ordinal)
    {
        ["waiting"] = Translate("ChatStatus_waiting", "等待接待"),
        ["active"] = Translate("ChatStatus_active", "进行中"),
        ["closed"] = Translate("ChatStatus_closed", "已关闭"),
        ["transferred"] = Translate("ChatStatus_transferred", "已转工单")
    };

    /// <summary>
    /// 取词条文本：本地化服务在词条缺失时会返回键名本身，此时回退给定中文原文，避免把键名吐给前端。
    /// </summary>
    private string Translate(string key, string zhFallback, params object[] args)
    {
        if (_localization is null) return args.Length > 0 ? string.Format(zhFallback, args) : zhFallback;
        var value = _localization[key];
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal))
        {
            return args.Length > 0 ? string.Format(zhFallback, args) : zhFallback;
        }
        return args.Length > 0 ? string.Format(value, args) : value;
    }

    // ====== 消息管理 ======

    public async Task<List<ChatMessageDto>> GetMessagesAsync(int sessionId, int limit = 100)
    {
        var messages = await _unitOfWork.ChatMessages.GetBySessionIdAsync(sessionId, limit);
        return messages.Select(m => m.ToDto()).ToList();
    }

    public async Task<ChatMessageDto> SendMessageAsync(int sessionId, SendChatMessageRequest request, int? userId = null, string? senderName = null)
    {
        _logger.LogInformation("在线聊天客服回复：SessionId={SessionId}, UserId={UserId}, SenderName={SenderName}, MessageType={MessageType}, ContentLength={ContentLen}, HasAttachment={HasAttachment}, AttachmentName={AttachmentName}",
            sessionId, userId, senderName ?? "(null)", request.MessageType, request.Content?.Length ?? 0,
            !string.IsNullOrEmpty(request.AttachmentUrl), request.AttachmentName ?? "(null)");

        var session = await _unitOfWork.ChatSessions.GetByIdTrackedAsync(sessionId);
        if (session == null)
        {
            _logger.LogWarning("在线聊天客服回复失败：会话不存在 SessionId={SessionId}", sessionId);
            throw new InvalidOperationException("会话不存在");
        }
        if (session.Status == "closed")
        {
            _logger.LogWarning("在线聊天客服回复失败：会话已关闭 SessionId={SessionId}", sessionId);
            throw new InvalidOperationException("会话已关闭");
        }

        if (!await _currentUserAccessor.CanAccessMailboxAsync(session.MailboxId))
        {
            _logger.LogWarning("在线聊天客服回复失败：无权访问该会话 SessionId={SessionId}, MailboxId={MailboxId}, UserId={UserId}",
                sessionId, session.MailboxId, userId);
            throw new InvalidOperationException("无权访问该会话");
        }

        _logger.LogDebug("在线聊天客服回复开始持久化：SessionId={SessionId}, MailboxId={MailboxId}, PreviousStatus={PreviousStatus}",
            sessionId, session.MailboxId, session.Status);

        var message = new ChatMessage
        {
            SessionId = sessionId,
            SenderType = "agent",
            SenderId = userId,
            SenderName = senderName ?? "客服",
            MessageType = request.MessageType,
            Content = request.Content ?? string.Empty,
            AttachmentUrl = NormalizeAttachmentUrl(request.AttachmentUrl),
            AttachmentName = request.AttachmentName,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.ChatMessages.AddAsync(message);

        // 更新会话状态
        session.AgentLastSeenAt = DateTime.UtcNow;
        session.CustomerUnreadCount += 1;
        session.UpdatedAt = DateTime.UtcNow;
        var statusChanged = false;
        if (session.Status == "waiting")
        {
            session.Status = "active";
            statusChanged = true;
        }

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // 并发下会话计数自增冲突（409）：分离冲突实体后重新读取最新会话，重新应用增量并重试一次，
            // 避免重复累加。低风险方案：不改变并发模型，仅增加一次自动重试。
            _logger.LogWarning("在线聊天会话计数并发冲突（客服回复），重试一次：SessionId={SessionId}", sessionId);
            _unitOfWork.ClearPendingChanges();
            // ClearPendingChanges 会分离尚未保存的 message，需重置临时键后重新跟踪，让数据库重新生成 Id
            message.Id = 0;
            await _unitOfWork.ChatMessages.AddAsync(message);
            var freshSession = await _unitOfWork.ChatSessions.GetByIdTrackedAsync(sessionId);
            if (freshSession == null) throw new InvalidOperationException("会话不存在");
            freshSession.AgentLastSeenAt = DateTime.UtcNow;
            freshSession.CustomerUnreadCount += 1;
            freshSession.UpdatedAt = DateTime.UtcNow;
            if (freshSession.Status == "waiting")
            {
                freshSession.Status = "active";
                statusChanged = true;
            }
            session = freshSession;
            await _unitOfWork.SaveChangesAsync();
        }

        _logger.LogInformation("在线聊天客服回复成功：SessionId={SessionId}, MessageId={MessageId}, StatusChanged={StatusChanged}, CustomerUnreadCount={UnreadCount}",
            sessionId, message.Id, statusChanged, session.CustomerUnreadCount);

        var dto = message.ToDto();
        var payload = new { sessionId, message = dto, timestamp = DateTime.UtcNow };
        await _notificationHub.NotifyAgentChatAsync(session.UserId, "RealtimeChat", payload);
        await _notificationHub.SendToChatSessionAsync(sessionId, "RealtimeChat", payload);
        _logger.LogDebug("在线聊天客服回复已推送 SignalR：SessionId={SessionId}, MessageId={MessageId}, AgentUserId={AgentUserId}",
            sessionId, message.Id, session.UserId);

        // 解析并通知 @提及 的客服用户
        await NotifyMentionedUsersAsync(sessionId, dto, session.MailboxId, senderId: userId, senderName: senderName ?? "客服");

        // T000062：若该聊天已转工单，把客服本次回复镜像到关联工单，保持工单内容持续更新
        await SyncTransferredChatToTicketAsync(session, message, agentUser: userId);

        return dto;
    }

    public async Task MarkAsReadAsync(int sessionId, string readerType, string? sessionIfMatch = null)
    {
        // 先更新消息 IsRead 标记（ChatMessage 已加 RowVersion，高并发写即使冲突也会被 EF Core 自动重试或抛 409）
        await _unitOfWork.ChatMessages.MarkAsReadAsync(sessionId, readerType);

        var session = await _unitOfWork.ChatSessions.GetByIdTrackedAsync(sessionId);
        if (session != null)
        {
            // 多端同时打开会话、同时拉取未读并标记已读：UnreadCount 与 LastSeenAt 属于高频写计数，
            // 显式调用乐观锁避免"前端显示还有未读"的计数覆盖。
            ConcurrencyCheckHelper.Check(
                session,
                session.RowVersion,
                sessionIfMatch,
                entityType: "ChatSession",
                entityId: session.Id,
                latestFactory: () => session.ToDto(),
                _logger);

            if (readerType == "customer")
            {
                session.CustomerUnreadCount = 0;
                session.CustomerLastSeenAt = DateTime.UtcNow;
            }
            else
            {
                session.AgentUnreadCount = 0;
                session.AgentLastSeenAt = DateTime.UtcNow;
            }
            session.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
        }

        // 推送已读回执到客服端和客户端
        var readPayload = new { sessionId, readerType, timestamp = DateTime.UtcNow };
        await _notificationHub.NotifyAgentChatAsync(session?.UserId, "ChatMessageRead", readPayload);
        await _notificationHub.SendToChatSessionAsync(sessionId, "ChatMessageRead", readPayload);
    }

    public async Task<int> GetUnreadCountAsync(int sessionId, string readerType)
    {
        return await _unitOfWork.ChatMessages.CountUnreadAsync(sessionId, readerType);
    }

    public async Task<ChatMessageDto> RecallMessageAsync(int sessionId, int messageId, int userId, string? ifMatch = null)
    {
        var message = await _unitOfWork.ChatMessages.GetByIdTrackedAsync(messageId);
        if (message == null || message.SessionId != sessionId)
            throw new InvalidOperationException("消息不存在");
        if (message.SenderType != "agent" || message.SenderId != userId)
            throw new InvalidOperationException("只能撤回自己发送的消息");
        if (message.IsRecalled)
            throw new InvalidOperationException("消息已撤回");
        // 2分钟内可撤回
        if (DateTime.UtcNow - message.CreatedAt > TimeSpan.FromMinutes(_chatOptions.MessageRecallWindowMinutes))
            throw new InvalidOperationException("消息发送超过2分钟，无法撤回");

        ConcurrencyCheckHelper.Check(
            message,
            message.RowVersion,
            ifMatch,
            entityType: "ChatMessage",
            entityId: message.Id,
            latestFactory: () => message.ToDto(),
            _logger);

        message.IsRecalled = true;
        message.RecalledAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        var dto = message.ToDto();
        var payload = new { sessionId, messageId, message = dto, timestamp = DateTime.UtcNow };
        var session = await _unitOfWork.ChatSessions.GetByIdAsync(sessionId);
        await _notificationHub.NotifyAgentChatAsync(session?.UserId, "ChatMessageRecalled", payload);
        await _notificationHub.SendToChatSessionAsync(sessionId, "ChatMessageRecalled", payload);
        return dto;
    }

    public async Task<ChatMessageDto> EditMessageAsync(int sessionId, int messageId, string newContent, int userId, string? ifMatch = null)
    {
        var message = await _unitOfWork.ChatMessages.GetByIdTrackedAsync(messageId);
        if (message == null || message.SessionId != sessionId)
            throw new InvalidOperationException("消息不存在");
        if (message.SenderType != "agent" || message.SenderId != userId)
            throw new InvalidOperationException("只能编辑自己发送的消息");
        if (message.IsRecalled)
            throw new InvalidOperationException("消息已撤回，无法编辑");
        if (string.IsNullOrWhiteSpace(newContent))
            throw new InvalidOperationException("消息内容不能为空");

        ConcurrencyCheckHelper.Check(
            message,
            message.RowVersion,
            ifMatch,
            entityType: "ChatMessage",
            entityId: message.Id,
            latestFactory: () => message.ToDto(),
            _logger);

        message.Content = newContent.Trim();
        message.EditedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        var dto = message.ToDto();
        var payload = new { sessionId, messageId, message = dto, timestamp = DateTime.UtcNow };
        var session = await _unitOfWork.ChatSessions.GetByIdAsync(sessionId);
        await _notificationHub.NotifyAgentChatAsync(session?.UserId, "ChatMessageEdited", payload);
        await _notificationHub.SendToChatSessionAsync(sessionId, "ChatMessageEdited", payload);

        // 编辑后同样解析并通知 @提及 的客服用户
        if (session != null)
        {
            await NotifyMentionedUsersAsync(sessionId, dto, session.MailboxId, senderId: userId, senderName: message.SenderName);
        }

        return dto;
    }

    public async Task NotifyTypingAsync(int sessionId, string senderType, int? userId = null)
    {
        // 输入状态通过 ChatHub 实时推送给会话双方
        await _notificationHub.NotifyChatTypingAsync(sessionId, senderType);
        if (senderType == "customer")
        {
            // 客户正在输入 → 通知客服
            var session = await _unitOfWork.ChatSessions.GetByIdAsync(sessionId);
            await _notificationHub.NotifyAgentChatAsync(session?.UserId, "ChatTyping", new { sessionId, senderType, timestamp = DateTime.UtcNow });
        }
    }

    // ====== 客户端接口 ======

    public async Task<InitiateChatResponse> InitiateChatAsync(InitiateChatRequest request, string? customerIp = null, string? userAgent = null)
    {
        // 手机号规范化（前端已作基础合规性校验，后端二次清洗并放宽到国际号码 7~20 位容错）
        var (phoneOk, normalizedPhone, phoneErr) = NormalizeChatCustomerPhone(request.CustomerPhone);
        if (!phoneOk)
            throw new InvalidOperationException($"手机号校验失败：{phoneErr}");

        _logger.LogInformation("在线聊天发起会话：CustomerName={CustomerName}, CustomerEmail={CustomerEmail}, CustomerPhone={CustomerPhone}, MailboxId={MailboxId}, Subject={Subject}, HasInitialMessage={HasInitialMsg}, CustomerIp={Ip}",
            request.CustomerName ?? "(null)", request.CustomerEmail ?? "(null)",
            string.IsNullOrEmpty(normalizedPhone) ? "(null)" : normalizedPhone,
            request.MailboxId, request.Subject ?? "(null)",
            !string.IsNullOrWhiteSpace(request.InitialMessage), customerIp ?? "(unknown)");

        var session = new ChatSession
        {
            SessionToken = Guid.NewGuid().ToString("N"),
            CustomerName = string.IsNullOrWhiteSpace(request.CustomerName) ? "访客" : request.CustomerName,
            CustomerEmail = request.CustomerEmail ?? string.Empty,
            CustomerPhone = normalizedPhone,
            CustomerIp = customerIp,
            CustomerUserAgent = userAgent,
            MailboxId = request.MailboxId,
            Subject = request.Subject,
            Status = "waiting",
            CustomerLastSeenAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _unitOfWork.ChatSessions.AddAsync(session);

        // 欢迎消息
        // 注意:使用导航属性 Session = session 而非 SessionId = session.Id,
        // 因为此时 session.Id 尚为 0(待 SaveChanges 后由数据库生成自增主键),
        // 直接赋值 SessionId=0 会触发 FK_ChatMessages_ChatSessions_SessionId 外键约束失败。
        var welcome = new ChatMessage
        {
            Session = session,
            SenderType = "system",
            MessageType = "system",
            Content = $"您好，{session.CustomerName}！感谢联系我们，客服将在稍后与您取得联系。",
            SenderName = "system",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.ChatMessages.AddAsync(welcome);

        // 客户首条消息（如有）
        ChatMessage? initial = null;
        if (!string.IsNullOrWhiteSpace(request.InitialMessage))
        {
            initial = new ChatMessage
            {
                Session = session,
                SenderType = "customer",
                SenderName = session.CustomerName,
                MessageType = "text",
                Content = request.InitialMessage,
                IsRead = false,
                CreatedAt = DateTime.UtcNow.AddSeconds(1)
            };
            await _unitOfWork.ChatMessages.AddAsync(initial);
            session.AgentUnreadCount = 1;
        }

        await _unitOfWork.SaveChangesAsync();

        // 重新加载带导航属性的会话
        var reloaded = await _unitOfWork.ChatSessions.GetByIdAsync(session.Id);
        var sessionDto = (reloaded ?? session).ToDto();
        sessionDto.MessageCount = await _unitOfWork.ChatMessages.CountAsync(sessionId: session.Id);

        // 通知所有在线客服有新聊天会话（等待接待的会话尚未分配客服，无法定向推送）
        await _notificationHub.SendToAll("RealtimeChat", new
        {
            sessionId = session.Id,
            isNewSession = true,
            mailboxId = session.MailboxId,
            timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("在线聊天会话创建成功：SessionId={SessionId}, SessionToken={SessionTokenMasked}, CustomerName={CustomerName}, MailboxId={MailboxId}",
            session.Id, MaskToken(session.SessionToken), session.CustomerName, session.MailboxId);

        return new InitiateChatResponse
        {
            Session = sessionDto,
            WelcomeMessage = welcome.ToDto()
        };
    }

    public async Task<ChatMessageDto> SendCustomerMessageAsync(string sessionToken, SendChatMessageRequest request)
    {
        _logger.LogInformation("在线聊天客户消息接收：SessionToken={SessionTokenMasked}, MessageType={MessageType}, ContentLength={ContentLen}, HasAttachment={HasAttachment}, AttachmentName={AttachmentName}",
            MaskToken(sessionToken), request.MessageType, request.Content?.Length ?? 0,
            !string.IsNullOrEmpty(request.AttachmentUrl), request.AttachmentName ?? "(null)");

        var session = await _unitOfWork.ChatSessions.GetByTokenAsync(sessionToken);
        if (session == null)
        {
            _logger.LogWarning("在线聊天客户消息接收失败：会话不存在或已过期 SessionToken={SessionTokenMasked}", MaskToken(sessionToken));
            throw new InvalidOperationException("会话不存在或已过期");
        }
        if (session.Status == "closed")
        {
            _logger.LogWarning("在线聊天客户消息接收失败：会话已关闭 SessionId={SessionId}", session.Id);
            throw new InvalidOperationException("会话已关闭");
        }

        _logger.LogDebug("在线聊天客户消息开始持久化：SessionId={SessionId}, CustomerName={CustomerName}, MailboxId={MailboxId}, Status={Status}, AssignedAgentId={AgentId}",
            session.Id, session.CustomerName, session.MailboxId, session.Status, session.UserId);

        var message = new ChatMessage
        {
            SessionId = session.Id,
            SenderType = "customer",
            SenderName = session.CustomerName,
            MessageType = request.MessageType,
            Content = request.Content ?? string.Empty,
            AttachmentUrl = NormalizeAttachmentUrl(request.AttachmentUrl),
            AttachmentName = request.AttachmentName,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.ChatMessages.AddAsync(message);

        session.CustomerLastSeenAt = DateTime.UtcNow;
        session.AgentUnreadCount += 1;
        session.UpdatedAt = DateTime.UtcNow;
        if (session.Status == "waiting")
        {
            // 保持等待状态，等待客服接入
        }

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // 并发下会话计数自增冲突（409）：分离冲突实体后重新读取最新会话，重新应用增量并重试一次，
            // 避免重复累加。低风险方案：不改变并发模型，仅增加一次自动重试。
            _logger.LogWarning("在线聊天会话计数并发冲突（客户发消息），重试一次：SessionId={SessionId}", session.Id);
            _unitOfWork.ClearPendingChanges();
            // ClearPendingChanges 会分离尚未保存的 message，需重置临时键后重新跟踪，让数据库重新生成 Id
            message.Id = 0;
            await _unitOfWork.ChatMessages.AddAsync(message);
            var freshSession = await _unitOfWork.ChatSessions.GetByIdTrackedAsync(session.Id);
            if (freshSession == null) throw new InvalidOperationException("会话不存在");
            freshSession.CustomerLastSeenAt = DateTime.UtcNow;
            freshSession.AgentUnreadCount += 1;
            freshSession.UpdatedAt = DateTime.UtcNow;
            session = freshSession;
            await _unitOfWork.SaveChangesAsync();
        }

        _logger.LogInformation("在线聊天客户消息接收成功：SessionId={SessionId}, MessageId={MessageId}, AgentUnreadCount={UnreadCount}, HasAssignedAgent={HasAgent}",
            session.Id, message.Id, session.AgentUnreadCount, session.UserId.HasValue);

        var dto = message.ToDto();
        var payload = new { sessionId = session.Id, message = dto, timestamp = DateTime.UtcNow };

        // 已分配客服的会话：定向推送给该客服
        // 未分配的等待会话：推送给所有在线客服，以便左侧栏刷新显示新消息
        if (session.UserId.HasValue)
        {
            await _notificationHub.NotifyAgentChatAsync(session.UserId, "RealtimeChat", payload);
            _logger.LogDebug("在线聊天客户消息推送至已分配客服：SessionId={SessionId}, MessageId={MessageId}, AgentUserId={AgentUserId}",
                session.Id, message.Id, session.UserId);
        }
        else
        {
            await _notificationHub.SendToAll("RealtimeChat", payload);
            _logger.LogDebug("在线聊天客户消息推送至全部在线客服（未分配）：SessionId={SessionId}, MessageId={MessageId}",
                session.Id, message.Id);
        }

        // T000062：若该聊天已转工单，把客户后续消息镜像到关联工单，保证工单记录不遗漏
        await SyncTransferredChatToTicketAsync(session, message, agentUser: null);

        return dto;
    }

    public async Task<List<ChatMessageDto>> GetCustomerMessagesAsync(string sessionToken, int limit = 100)
    {
        var session = await _unitOfWork.ChatSessions.GetByTokenAsync(sessionToken);
        if (session == null) throw new InvalidOperationException("会话不存在或已过期");

        var messages = await _unitOfWork.ChatMessages.GetBySessionIdAsync(session.Id, limit);
        return messages.Select(m => m.ToDto()).ToList();
    }

    public async Task<ChatSessionDto> CloseSessionByTokenAsync(string sessionToken)
    {
        var session = await _unitOfWork.ChatSessions.GetByTokenAsync(sessionToken);
        if (session == null) throw new InvalidOperationException("会话不存在或已过期");

        session.Status = "closed";
        session.ClosedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.ChatMessages.AddAsync(new ChatMessage
        {
            SessionId = session.Id,
            SenderType = "system",
            MessageType = "system",
            Content = "客户已结束对话",
            SenderName = "system",
            IsRead = true,
            CreatedAt = DateTime.UtcNow
        });

        await _unitOfWork.SaveChangesAsync();
        return await GetSessionOrThrowAsync(session.Id);
    }

    public async Task<ChatSessionDto> RateChatAsync(string sessionToken, RateChatRequest request)
    {
        // 先用 GetByTokenAsync 找到 sessionId，再 GetByIdTrackedAsync 拿到跟踪对象，便于后续 ReallyClose 改字段
        var existingSession = await _unitOfWork.ChatSessions.GetByTokenAsync(sessionToken);
        if (existingSession == null) throw new InvalidOperationException("会话不存在或已过期");
        var session = await _unitOfWork.ChatSessions.GetByIdTrackedAsync(existingSession.Id);
        if (session == null) throw new InvalidOperationException("会话不存在或已过期");

        // 客户提交评分也可能与客服"关闭会话"或后台超时任务并发写入，加乐观锁
        ConcurrencyCheckHelper.Check(
            session,
            session.RowVersion,
            request.IfMatch,
            entityType: "ChatSession",
            entityId: session.Id,
            latestFactory: () => session.ToDto(),
            _logger);

        var rating = Math.Clamp(request.Rating, 1, 5);
        session.Rating = rating;
        session.RatingComment = request.Comment;
        session.UpdatedAt = DateTime.UtcNow;

        // 先 SaveChanges：让下面工作流和关闭动作能够拿到最新 Rating / RatingComment
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("客户提交在线聊天满意度评分：SessionId={SessionId}, Rating={Rating}, Comment={Comment}, Status={Status}",
            session.Id, rating, request.Comment ?? "(空)", session.Status);

        // 实时推送满意度评价更新通知给服务台在线客服
        try
        {
            var ratingPayload = new
            {
                sessionId = session.Id,
                conversationId = session.ConversationId,
                rating = rating,
                comment = request.Comment,
                source = "chat",
                createdAt = DateTime.UtcNow
            };
            await _notificationHub.SendToAll("SatisfactionUpdated", ratingPayload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "推送在线聊天满意度实时通知失败（不影响主流程）");
        }

        // 如果会话当前正处于"客服主动发起关闭后的等待评分"阶段 → 触发 chat_satisfaction_submitted 工作流 → 真关闭
        if (session.Status == "awaiting_rating")
        {
            try
            {
                await _workflowService.ExecuteForChatSessionAsync(
                    session.Id,
                    WorkflowConst.TriggerChatSatisfactionSubmitted,
                    chatRating: rating,
                    chatRatingComment: request.Comment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "触发 [聊天]满意度评价感谢消息 工作流失败（chat_satisfaction_submitted），SessionId={SessionId}", session.Id);
            }

            // 真关闭（不额外写"会话已关闭"message：系统工作流已写感谢消息；或留空原因由前端自行展示状态）
            await ReallyCloseChatSessionAsync(session);
        }

        return await GetSessionOrThrowAsync(session.Id);
    }

    // ====== 转工单 ======

    public async Task<ChatSessionDto> TransferToTicketAsync(int sessionId, TransferToTicketRequest request, int operatedByUserId)
    {
        _logger.LogInformation("在线聊天转工单：SessionId={SessionId}, OperatedByUserId={OperatedByUserId}, TargetMailboxId={TargetMailboxId}, Subject={Subject}",
            sessionId, operatedByUserId, request.MailboxId, request.Subject ?? "(null)");

        var session = await _unitOfWork.ChatSessions.GetByIdTrackedAsync(sessionId);
        if (session == null) throw new InvalidOperationException("会话不存在");

        // 构造会话内容（包含全部聊天记录，使用结构化 HTML 提升可读性）
        // 注意：sanitizeHtml 仅允许 class 属性（过滤 style），因此样式通过 class + 前端 Tailwind 任意选择器实现。
        // 分页加载消息（避免一次性 int.MaxValue 加载导致大数据量下内存溢出）：
        //   - GetListAsync 内部是 OrderByDescending(CreatedAt)，故每页内是倒序；
        //   - 从最后一页往第 1 页遍历，且每页内 Reverse()，最终整体顺序为正序（从最早到最新）。
        var html = new System.Text.StringBuilder();
        html.Append("<div class=\"chat-transcript\">");
        html.Append("<h3>💬 在线聊天转工单</h3>");
        html.Append("<div class=\"chat-meta\">");
        html.Append($"<p><strong>客户：</strong>{WebUtility.HtmlEncode(session.CustomerName)} ({WebUtility.HtmlEncode(session.CustomerEmail ?? "-")})</p>");
        html.Append($"<p><strong>主题：</strong>{WebUtility.HtmlEncode(session.Subject ?? "(无)")}</p>");
        html.Append($"<p><strong>发起时间：</strong>{session.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC</p>");
        html.Append("</div>");
        html.Append("<hr>");
        html.Append("<div class=\"chat-messages\">");
        const int pageSize = 1000;
        var totalMsg = await _unitOfWork.ChatMessages.CountAsync(sessionId: sessionId);
        // 先收集所有消息（正序），供构造 HTML 和提取附件 ID 两处使用
        var messages = new List<ChatMessage>();
        if (totalMsg > 0)
        {
            var totalPages = (int)Math.Ceiling((double)totalMsg / pageSize);
            for (int page = totalPages; page >= 1; page--)
            {
                var pageMessages = await _unitOfWork.ChatMessages.GetListAsync(
                    sessionId: sessionId,
                    page: page,
                    pageSize: pageSize);
                // 每页内是倒序，Reverse 后变正序，再按正序 append
                var orderedPage = pageMessages.Reverse();
                messages.AddRange(orderedPage);
            }
        }
        foreach (var m in messages)
        {
            var (roleClass, senderLabel, senderPrefix) = m.SenderType switch
            {
                "customer" => ("chat-msg-customer", $"客户 · {m.SenderName}", "👤"),
                "agent" => ("chat-msg-agent", $"客服 · {m.SenderName}", "🧑‍💼"),
                _ => ("chat-msg-system", "系统消息", "🔔")
            };
            var content = WebUtility.HtmlEncode(m.Content ?? "");
            // 保留换行：将 \n 转换为 <br>
            content = content.Replace("\n", "<br>");
            // 追加附件链接（如有），确保转工单后工单正文包含聊天中发送的附件
            if (!string.IsNullOrEmpty(m.AttachmentUrl))
            {
                var attachmentName = WebUtility.HtmlEncode(m.AttachmentName ?? "附件");
                var attachmentUrl = WebUtility.HtmlEncode(m.AttachmentUrl);
                content += $"<div class=\"chat-attachment\"><a href=\"{attachmentUrl}\" target=\"_blank\">📎 {attachmentName}</a></div>";
            }
            if (m.SenderType == "system")
            {
                html.Append($"<div class=\"chat-msg {roleClass}\">");
                html.Append($"<span class=\"chat-time\">{m.CreatedAt:HH:mm:ss}</span>");
                html.Append($"<span class=\"chat-content\">{senderPrefix} {content}</span>");
                html.Append("</div>");
            }
            else
            {
                html.Append($"<div class=\"chat-msg {roleClass}\">");
                html.Append($"<div class=\"chat-msg-header\">");
                html.Append($"<span class=\"chat-time\">{m.CreatedAt:HH:mm:ss}</span>");
                html.Append($"<span class=\"chat-sender\">{senderPrefix} {WebUtility.HtmlEncode(senderLabel)}</span>");
                html.Append("</div>");
                html.Append($"<div class=\"chat-content\">{content}</div>");
                html.Append("</div>");
            }
        }
        html.Append("</div>");
        html.Append("</div>");
        var threadBody = html.ToString();

        // 创建客户（按邮箱主路径，手机为降级路径）
        // 客户进入 Widget 时已填写姓名、联系邮箱、联系手机（3 项均为必填/强制合规），直接用这些信息创建/补齐客户资料，
        // 不再从聊天历史中二次提取（已删除转工单后联系方式提取流程）。
        Conversation conversation;
        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                // 1. 用 ChineseNameSplitter 智能拆姓/名：
                //    - 中文（2-4 个汉字，无空格/拉丁）：支持复姓（欧阳/司马等）→ LastName=姓，FirstName=名
                //    - 其他语言/西文姓名：FirstName=完整姓名，LastName 留空（与 ConversationService 约定一致）
                var (lastName, firstName) = ChineseNameSplitter.Split(session.CustomerName);

                Customer? customer = null;

                // 2. 主路径：按邮箱查找/创建（与 Email 渠道一致的客户合并策略）
                //    GetOrCreateByEmailAsync 内部会在"创建新客户"分支把 firstName/lastName 写入 FirstName/LastName；
                //    对已存在的老客户，仓储不修改其姓名，由下方"空白补齐"逻辑按需填充。
                if (!string.IsNullOrWhiteSpace(session.CustomerEmail))
                {
                    customer = await _unitOfWork.Customers.GetOrCreateByEmailAsync(
                        session.CustomerEmail,
                        string.IsNullOrWhiteSpace(firstName) ? null : firstName,
                        string.IsNullOrWhiteSpace(lastName) ? null : lastName);
                }
                // 3. 降级路径（理论不会走到，Widget 已强制邮箱必填并做合规校验）：按手机号查找已有客户
                //    使用 Tracked 版本：下方步骤 4a 会修改 customer.FirstName/LastName，需要 ChangeTracker 跟踪
                else if (!string.IsNullOrWhiteSpace(session.CustomerPhone))
                {
                    customer = await _unitOfWork.Customers.GetByPhoneTrackedAsync(session.CustomerPhone);
                }

                // 4. 兜底：若以上两条路径都未拿到 customer（极端空值），仍基于 Widget 姓名创建一个新客户，避免后续 Conversation.CustomerId 为空
                if (customer == null && (!string.IsNullOrWhiteSpace(session.CustomerEmail) ||
                                         !string.IsNullOrWhiteSpace(session.CustomerPhone) ||
                                         !string.IsNullOrWhiteSpace(session.CustomerName)))
                {
                    customer = new Customer
                    {
                        FirstName = firstName,
                        LastName = lastName
                    };
                    await _unitOfWork.Customers.AddAsync(customer);
                }

                if (customer != null)
                {
                    bool customerChanged = false;

                    // 4a. 姓名回填：以客户每次进入 Widget 时实际填写的 session.CustomerName（经 ChineseNameSplitter 拆名）为准。
                    //     无论 Customer.FirstName/LastName 原先是否有值（即使是老客户已保存过姓名），
                    //     **一律清除并改写**为 Widget 本次提交的值（空串则清空为 string.Empty）。
                    //     用户明确要求："不管顾客姓名里有没有内容，都清除，改写"
                    //     （避免老客户被历史版本的空格粗暴拆分/空白回填策略写入了不完整/不正确的姓名，再也不被更新）。
                    if (!string.Equals(customer.FirstName ?? string.Empty, firstName ?? string.Empty, StringComparison.Ordinal))
                    {
                        customer.FirstName = firstName ?? string.Empty;
                        customerChanged = true;
                    }
                    if (!string.Equals(customer.LastName ?? string.Empty, lastName ?? string.Empty, StringComparison.Ordinal))
                    {
                        customer.LastName = lastName ?? string.Empty;
                        customerChanged = true;
                    }

                    // 4b. 邮箱补齐：确保 Customer.Emails 中存在 session.CustomerEmail（去重，Type=1 默认工作邮箱）
                    //     新建客户由 GetOrCreateByEmailAsync 已写入 Emails，此处做幂等保护（重复则忽略）
                    if (!string.IsNullOrWhiteSpace(session.CustomerEmail))
                    {
                        customer.Emails ??= new List<Email>();
                        var emailAddr = session.CustomerEmail.Trim();
                        if (!customer.Emails.Any(e => string.Equals(e.EmailAddress, emailAddr, StringComparison.OrdinalIgnoreCase)))
                        {
                            customer.Emails.Add(new Email { EmailAddress = emailAddr, Type = 1 });
                            customerChanged = true;
                        }
                    }

                    // 4c. 手机补齐：去重写入 Customer.Phones，类型 Mobile=1
                    //     手机号规范化已在 InitiateChatAsync → NormalizeChatCustomerPhone 阶段完成，此处直接使用。
                    if (!string.IsNullOrWhiteSpace(session.CustomerPhone))
                    {
                        var phones = customer.GetPhones();
                        if (!phones.Any(p => string.Equals(p.Value, session.CustomerPhone, StringComparison.Ordinal)))
                        {
                            phones.Add(new CustomerPhone { Value = session.CustomerPhone, Type = 1 /* Mobile */ });
                            customer.SetPhones(phones);
                            customerChanged = true;
                        }
                    }

                    if (customerChanged)
                        customer.UpdatedAt = DateTime.UtcNow;
                }

                // 生成下一个工单编号(Number 字段有 UNIQUE 索引,过滤已软删除记录)。
                var nextNumber = await _unitOfWork.Conversations.GetNextNumberAsync();

                // 会话主题：统一加上 [聊天转工单] 前缀
                // 优先级：1) 前端指定的 request.Subject -> 2) 聊天会话自身 session.Subject -> 3) 客户姓名
                // 如果主题已包含前缀则不重复添加，避免形如 [聊天转工单] [聊天转工单] XXX。
                const string TicketTranscriptPrefix = "[聊天转工单]";
                string rawSubject;
                if (!string.IsNullOrWhiteSpace(request.Subject))
                    rawSubject = request.Subject.Trim();
                else if (!string.IsNullOrWhiteSpace(session.Subject))
                    rawSubject = session.Subject.Trim();
                else
                    rawSubject = session.CustomerName?.Trim() ?? string.Empty;

                string finalSubject = rawSubject.StartsWith(TicketTranscriptPrefix, StringComparison.Ordinal)
                    ? rawSubject
                    : $"{TicketTranscriptPrefix} {rawSubject}".Trim();

                conversation = new Conversation
                {
                    Number = nextNumber,
                    MailboxId = request.MailboxId ?? session.MailboxId ?? 1,
                    // 导航属性+外键双属性同步：
                    // - customer.Id>0（已存在客户）：显式设置两个属性，避免对 EF Core NavigationFixer 的隐式依赖，
                    //   也确保单元测试（无EF ChangeTracker）能拿到正确的外键值。
                    // - customer.Id==0（新建客户）：仅设置导航属性，SaveChanges 后 EF 自动 fixup 外键。
                    //   显式设置 CustomerId=null 防止 Id=0 触发 FK 失败。
                    Customer = customer,
                    CustomerId = customer != null && customer.Id > 0 ? customer.Id : null,
                    CustomerEmail = session.CustomerEmail ?? string.Empty,
                    Subject = finalSubject,
                    // UserId 分配逻辑：request.UserId > session.UserId > operatedByUserId。
                    // 若 operatedByUserId=0，显式设为 null 避免 UserId=0 FK 冲突
                    UserId = (request.UserId ?? session.UserId ?? operatedByUserId) == 0
                        ? null
                        : (request.UserId ?? session.UserId ?? operatedByUserId),
                    Type = ConversationType.Chat,
                    SourceType = SourceType.Chat,
                    SourceVia = SourceVia.Customer,
                    Status = ConversationStatus.Active,
                    State = ConversationState.Published,
                    Priority = ConversationPriority.Normal,
                    LastReplyAt = DateTime.UtcNow
                };
                var thread = new ConversationThread
                {
                    Type = ThreadType.Customer,
                    Body = threadBody,
                    From = session.CustomerEmail ?? string.Empty,
                    // 同步使用导航属性，避免新客户 CustomerId=0 导致 Thread FK 失败
                    Customer = customer
                };
                // 使用 AddThread 而非 Threads.Add,以正确递增 ThreadsCount、设置 LastReplyFrom、UpdatedAt
                conversation.AddThread(thread);
                // 设置列表预览(去除 HTML 标签后的纯文本)
                conversation.SetPreview(threadBody);
                await _unitOfWork.Conversations.AddAsync(conversation);

                // 将聊天中上传的附件(ThreadId=null 的孤儿记录)关联到新创建的 ConversationThread,
                // 确保转工单后工单详情页能查询到这些附件。
                // 附件 URL 格式:/api/attachments/{id}/download,从中解析附件 ID。
                // 注意:此时 thread.Id 尚为 0(待 SaveChanges 后由数据库生成自增主键),
                // 直接赋值 att.ThreadId=thread.Id 会写入 0 导致关联失败,
                // 因此使用导航属性 thread.Attachments.Add 让 EF Core 在 SaveChanges 时自动设置 FK。
                var attachmentIds = new List<int>();
                foreach (var m in messages)
                {
                    if (string.IsNullOrEmpty(m.AttachmentUrl)) continue;
                    var match = Regex.Match(m.AttachmentUrl, @"/api/attachments/(\d+)/download", RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var aid))
                    {
                        attachmentIds.Add(aid);
                    }
                }
                if (attachmentIds.Count > 0)
                {
                    // 去重后批量查询(tracking),通过导航属性关联到 thread
                    var distinctIds = attachmentIds.Distinct().ToList();
                    var attachments = await _unitOfWork.Attachments.GetByIdsTrackedAsync(distinctIds);
                    foreach (var att in attachments)
                    {
                        thread.Attachments.Add(att);
                    }
                    thread.HasAttachments = true;
                }

                // 更新聊天会话状态
                // 导航属性+外键双属性同步（与上面对 Customer/CustomerId 的处理同理）
                session.Conversation = conversation;
                if (conversation.Id > 0)
                    session.ConversationId = conversation.Id;
                // 同时同步 session.Customer（若已有老 session.CustomerId 则保留为空，
                // 但通过导航属性关联的 customer 会被 EF fixup 填充正确值）
                if (customer != null && session.CustomerId == null)
                {
                    session.Customer = customer;
                }
                session.Status = "transferred";
                session.UpdatedAt = DateTime.UtcNow;

                await _unitOfWork.ChatMessages.AddAsync(new ChatMessage
                {
                    Session = session,
                    SenderType = "system",
                    MessageType = "system",
                    Content = $"会话已转为工单",
                    SenderName = "system",
                    IsRead = true,
                    CreatedAt = DateTime.UtcNow
                });

                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("在线聊天转工单事务提交成功：SessionId={SessionId}, ConversationId={ConversationId}, ConversationNumber={ConversationNumber}, CustomerId={CustomerId}, AttachmentCount={AttachmentCount}",
                    sessionId, conversation.Id, conversation.Number, conversation.CustomerId, attachmentIds.Count);
            }
            catch (Exception ex)
            {
                await transaction.SafeRollbackAsync();
                _logger.LogError(ex, "在线聊天转工单事务失败：SessionId={SessionId}", sessionId);
                throw;
            }
        }

        // 发送会话创建实时通知,使会话管理界面自动刷新
        var createdResponse = conversation.ToResponse(true);
        await _notificationPublisher.NotifyMailboxAsync(conversation.MailboxId.ToString(), "conversation_created", createdResponse);

        // 触发 new_conversation 工作流(失败隔离,不阻塞主流程)
        try
        {
            await _workflowService.ExecuteForConversationAsync(conversation.Id, WorkflowConst.TriggerNewConversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发工作流失败（聊天转工单）：ConversationId={ConversationId}", conversation.Id);
        }

        // 触发 Webhook 通知（失败仅记录日志，不阻塞主流程）
        if (_webhookService != null)
        {
            try
            {
                await _webhookService.TriggerAsync("conversation_created", conversation.MailboxId, createdResponse, "Conversation", conversation.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发 Webhook 失败：Event={Event}, Entity={EntityType}:{EntityId}", "conversation_created", "Conversation", conversation.Id);
            }
        }

        _logger.LogInformation("在线聊天转工单完成：SessionId={SessionId}, ConversationId={ConversationId}", sessionId, conversation.Id);

        return await GetSessionOrThrowAsync(sessionId);
    }

    /// <summary>
    /// T000062：聊天会话已转工单后，客户/客服后续消息**实时镜像**到关联工单（为 Conversation 追加新线程）。
    /// 背景：转工单快照只包含当时的聊天记录；若客户之后继续在 Widget 聊天、或客服继续在在线聊天中回复，
    /// 这些新消息仅写入 ChatMessage 表，工单(会话管理)里看不到，导致工单记录与真实沟通脱节。
    /// 镜像失败仅记录日志，绝不影响在线聊天的正常收发。
    /// </summary>
    private async Task SyncTransferredChatToTicketAsync(ChatSession session, ChatMessage message, int? agentUser)
    {
        if (session == null || message == null || message.Id <= 0)
            return;
        if (session.Status != "transferred" || !session.ConversationId.HasValue)
            return;

        try
        {
            var conversation = await _unitOfWork.Conversations.GetByIdTrackedAsync(session.ConversationId.Value);
            if (conversation == null)
                return;

            var isAgent = string.Equals(message.SenderType, "agent", StringComparison.OrdinalIgnoreCase);
            var label = isAgent
                ? $"🧑‍💼 客服 · {WebUtility.HtmlEncode(message.SenderName ?? "客服")}"
                : $"👤 客户 · {WebUtility.HtmlEncode(message.SenderName ?? session.CustomerName ?? "客户")}";

            var content = WebUtility.HtmlEncode(message.Content ?? string.Empty);
            content = content.Replace("\r\n", "\n").Replace("\n", "<br>");

            // 附件：正文追加可点击链接 + 解析真实附件并挂到线程（与 TransferToTicketAsync 一致）
            int? attachmentId = null;
            if (!string.IsNullOrEmpty(message.AttachmentUrl))
            {
                var attachmentName = WebUtility.HtmlEncode(message.AttachmentName ?? "附件");
                var attachmentUrl = WebUtility.HtmlEncode(message.AttachmentUrl);
                content += $"<div class=\"chat-attachment\"><a href=\"{attachmentUrl}\" target=\"_blank\">📎 {attachmentName}</a></div>";

                var match = Regex.Match(message.AttachmentUrl, @"/api/attachments/(\d+)/download", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var aid))
                {
                    attachmentId = aid;
                }
            }

            var body = $"<div class=\"chat-transcript chat-continue\"><p><strong>{label}</strong> · {message.CreatedAt:yyyy-MM-dd HH:mm:ss}</p><p>{content}</p></div>";

            var thread = new ConversationThread
            {
                ConversationId = conversation.Id,
                Conversation = conversation,
                UserId = isAgent ? agentUser : null,
                CustomerId = conversation.CustomerId,
                Type = isAgent ? ThreadType.Message : ThreadType.Customer,
                Body = body,
                Subject = conversation.Subject,
                From = isAgent
                    ? (message.SenderName ?? "客服")
                    : (session.CustomerEmail ?? message.SenderName ?? "客户"),
                IsFirst = false,
                CreatedAt = message.CreatedAt,
                UpdatedAt = DateTime.UtcNow
            };

            if (attachmentId.HasValue)
            {
                var att = await _unitOfWork.Attachments.GetByIdTrackedAsync(attachmentId.Value);
                if (att != null)
                {
                    thread.Attachments.Add(att);
                    thread.HasAttachments = true;
                }
            }

            // 使用 AddThread 统一维护 ThreadsCount / LastReplyAt / LastReplyFrom / UpdatedAt
            conversation.AddThread(thread);
            conversation.SetPreview(body);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("在线聊天已转工单，后续消息已镜像到工单：SessionId={SessionId}, ConversationId={ConversationId}, MessageId={MessageId}, ThreadType={ThreadType}",
                session.Id, conversation.Id, message.Id, thread.Type);
        }
        catch (Exception ex)
        {
            // 镜像失败不阻塞聊天本身，仅记录日志便于排查
            _logger.LogError(ex, "聊天转工单后的消息镜像到工单失败：SessionId={SessionId}, MessageId={MessageId}",
                session?.Id, message?.Id);
        }
    }

    // ====== 软删除 / 垃圾箱 / 硬删除 ======

    public async Task<PagedResponse<ChatSessionDto>> GetDeletedSessionsAsync(int page, int pageSize, string? keyword = null)
    {
        var items = await _unitOfWork.ChatSessions.GetDeletedListAsync(page, pageSize, keyword);
        var total = await _unitOfWork.ChatSessions.CountDeletedAsync(keyword);

        // 批量预加载消息数，避免 N+1
        var messageCounts = await _unitOfWork.ChatMessages.GetCountBySessionIdsAsync(items.Select(s => s.Id));

        var dtos = new List<ChatSessionDto>();
        foreach (var s in items)
        {
            var dto = s.ToDto();
            dto.MessageCount = messageCounts.GetValueOrDefault(s.Id, 0);
            dtos.Add(dto);
        }

        return new PagedResponse<ChatSessionDto>
        {
            Data = dtos,
            CurrentPage = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)Math.Max(pageSize, 1))
        };
    }

    public async Task<bool> DeleteSessionAsync(int sessionId)
    {
        var result = await _unitOfWork.ChatSessions.SoftDeleteAsync(sessionId);
        if (result)
        {
            await _unitOfWork.SaveChangesAsync();
        }
        return result;
    }

    public Task<bool> BulkDeleteSessionsAsync(List<int> sessionIds)
        => _unitOfWork.ChatSessions.SoftDeleteAsync(sessionIds);

    public async Task<bool> RestoreSessionAsync(int sessionId)
    {
        var result = await _unitOfWork.ChatSessions.RestoreAsync(sessionId);
        if (result)
        {
            await _unitOfWork.SaveChangesAsync();
        }
        return result;
    }

    public Task<bool> HardDeleteSessionAsync(int sessionId)
        => _unitOfWork.ChatSessions.HardDeleteAsync(sessionId);

    public Task<int> HardDeleteAllDeletedSessionsAsync()
        => _unitOfWork.ChatSessions.HardDeleteAllDeletedAsync();

    /// <summary>
    /// 解析消息内容中的 @[姓名] 提及标记,匹配用户表中的客服用户,
    /// 并通过 SignalR 向被提及的用户推送 ChatMention 事件。
    /// 失败静默处理,不影响主消息发送流程。
    /// </summary>
    private async Task NotifyMentionedUsersAsync(int sessionId, ChatMessageDto message, int? mailboxId, int? senderId, string senderName)
    {
        try
        {
            if (string.IsNullOrEmpty(message.Content))
                return;

            var matches = MentionPattern.Matches(message.Content);
            if (matches.Count == 0)
                return;

            // 提取去重的姓名列表
            var mentionedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in matches)
            {
                if (m.Groups.Count > 1 && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                {
                    mentionedNames.Add(m.Groups[1].Value.Trim());
                }
            }

            if (mentionedNames.Count == 0)
                return;

            // 查询活跃用户（AsNoTracking 只读，避免加载全部用户到 ChangeTracker）
            // GetFullName() 不可翻译为 SQL，需在内存中按姓名匹配
            var allUsers = await _unitOfWork.Users.GetQueryable(true)
                .Where(u => u.DeletedAt == null && u.Status == UserStatus.Active)
                .Select(u => new { u.Id, u.FirstName, u.LastName })
                .Take(2000)
                .ToListAsync();
            var targetUsers = allUsers
                .Where(u => mentionedNames.Contains($"{u.FirstName} {u.LastName}".Trim()))
                .ToList();

            if (targetUsers.Count == 0)
                return;

            // 推送给每个被提及的用户(排除发送者自己)
            foreach (var user in targetUsers)
            {
                if (senderId.HasValue && user.Id == senderId.Value)
                    continue;

                var fullName = $"{user.FirstName} {user.LastName}".Trim();
                var mentionPayload = new
                {
                    sessionId,
                    messageId = message.Id,
                    mentionedUserId = user.Id,
                    mentionedUserName = fullName,
                    senderId = senderId,
                    senderName = senderName,
                    senderType = message.SenderType,
                    preview = TruncateForPreview(message.Content),
                    timestamp = DateTime.UtcNow
                };
                await _notificationHub.SendToUser(user.Id, "ChatMention", mentionPayload);
            }
        }
        catch (Exception ex)
        {
            // @提及 通知失败不应阻塞主消息发送流程，但需记录日志便于排查
            _logger.LogWarning(ex, "聊天 @提及 通知发送失败：SessionId={SessionId}", sessionId);
        }
    }

    /// <summary>
    /// 截取消息内容前 100 字符作为预览,移除 @[姓名] 中的方括号以提升可读性
    /// </summary>
    private static string TruncateForPreview(string content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        var preview = MentionPattern.Replace(content, m =>
            m.Groups.Count > 1 ? "@" + m.Groups[1].Value : m.Value);
        return preview.Length > 100 ? preview.Substring(0, 100) + "..." : preview;
    }

    /// <summary>
    /// 重新查询会话并保证返回非空 DTO。若会话在保存后无法被查询到（异常并发删除等），
    /// 抛出 InvalidOperationException 以避免向调用方返回 null。
    /// </summary>
    private async Task<ChatSessionDto> GetSessionOrThrowAsync(int sessionId)
    {
        var dto = await GetSessionByIdAsync(sessionId);
        if (dto == null)
            throw new InvalidOperationException("会话保存后无法重新加载，可能已被其他进程删除。");
        return dto;
    }

}
