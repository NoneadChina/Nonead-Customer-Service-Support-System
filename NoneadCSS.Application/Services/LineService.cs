using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Services;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Application.Services;

/// <summary>
/// LINE 渠道业务服务。
/// 与其它渠道的关键差异：
///   1) 出站附件没有上传接口 ⇒ 必须签发本部署的公网 HTTPS 直链（LineMediaLinkSigner + /api/line/media/{id}）。
///   2) reply 不消耗月度配额、push 消耗 ⇒ 会话回复优先用最近一条客户消息的 replyToken（存活极短），过期回落 push。
///   3) 非好友/已屏蔽 ⇒ 404/403，会话标记不可达（写内部备注线程，不新增表字段）。
/// </summary>
public class LineService : ILineService
{
    /// <summary>人工接管标记线程的 MessageId 前缀（按前缀 LIKE 查询，无需新表）</summary>
    internal const string TakeoverThreadPrefix = "line-takeover-";

    /// <summary>不可达标记线程的 MessageId 前缀</summary>
    internal const string UnreachableThreadPrefix = "line-unreachable-";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IEncryptionService _encryptionService;
    private readonly ILineSettingsProvider _settingsProvider;
    private readonly ILineClient _client;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<LineService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly IWebhookService? _webhookService;

    public LineService(
        IUnitOfWork unitOfWork,
        IEncryptionService encryptionService,
        ILineSettingsProvider settingsProvider,
        ILineClient client,
        ICurrentUserAccessor currentUserAccessor,
        INotificationPublisher notificationPublisher,
        ILogger<LineService> logger,
        IConfiguration configuration,
        IHostEnvironment environment,
        IWebhookService? webhookService = null)
    {
        _unitOfWork = unitOfWork;
        _encryptionService = encryptionService;
        _settingsProvider = settingsProvider;
        _client = client;
        _currentUserAccessor = currentUserAccessor;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
        _webhookService = webhookService;
    }

    // ── 设置 ────────────────────────────────────────────────────────────────────

    public async Task<LineSettingsDto> GetSettingsAsync()
    {
        var options = await _settingsProvider.GetCurrentAsync();
        return MapSettings(options);
    }

    public async Task<LineSettingsDto> UpdateSettingsAsync(UpdateLineSettingsRequest request)
    {
        // 敏感字段：加密后以 ENC: 前缀入库；非敏感字段直接覆盖
        if (!string.IsNullOrWhiteSpace(request.ChannelId))
        {
            await SaveOptionAsync(LineOptionKeys.ChannelId, request.ChannelId!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.ChannelSecret))
        {
            await SaveOptionAsync(LineOptionKeys.ChannelSecret, "ENC:" + _encryptionService.Encrypt(request.ChannelSecret!.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.ChannelAccessToken))
        {
            await SaveOptionAsync(LineOptionKeys.ChannelAccessToken, "ENC:" + _encryptionService.Encrypt(request.ChannelAccessToken!.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.ApiBaseUrl))
        {
            await SaveOptionAsync(LineOptionKeys.ApiBaseUrl, request.ApiBaseUrl!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.DataApiBaseUrl))
        {
            await SaveOptionAsync(LineOptionKeys.DataApiBaseUrl, request.DataApiBaseUrl!.Trim());
        }

        if (request.AttachmentPublicBaseUrl is not null)
        {
            var publicBase = request.AttachmentPublicBaseUrl.Trim();
            if (publicBase.Length > 0 && !publicBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // LINE 强制出站直链为 HTTPS，http 会被客户端拒绝渲染
                _logger.LogWarning("LINE 附件公网基址不是 https：{Base}（LINE 要求 HTTPS 直链）", publicBase);
            }

            await SaveOptionAsync(LineOptionKeys.AttachmentPublicBaseUrl, publicBase);
        }

        if (request.AttachmentPublicUrlTtlMinutes is > 0)
        {
            await SaveOptionAsync(LineOptionKeys.AttachmentPublicUrlTtlMinutes, request.AttachmentPublicUrlTtlMinutes.Value.ToString());
        }

        if (!string.IsNullOrWhiteSpace(request.RichMenuId))
        {
            await SaveOptionAsync(LineOptionKeys.RichMenuId, request.RichMenuId!.Trim());
        }

        if (request.WebhookEnabled.HasValue)
        {
            await SaveOptionAsync(LineOptionKeys.WebhookEnabled, request.WebhookEnabled.Value.ToString().ToLowerInvariant());
        }

        if (request.MarkAsReadEnabled.HasValue)
        {
            await SaveOptionAsync(LineOptionKeys.MarkAsReadEnabled, request.MarkAsReadEnabled.Value.ToString().ToLowerInvariant());
        }

        if (request.LoadingIndicatorEnabled.HasValue)
        {
            await SaveOptionAsync(LineOptionKeys.LoadingIndicatorEnabled, request.LoadingIndicatorEnabled.Value.ToString().ToLowerInvariant());
        }

        if (request.ProfileEnrichEnabled.HasValue)
        {
            await SaveOptionAsync(LineOptionKeys.ProfileEnrichEnabled, request.ProfileEnrichEnabled.Value.ToString().ToLowerInvariant());
        }

        if (request.AutoReplyEnabled.HasValue)
        {
            await SaveOptionAsync(LineOptionKeys.AutoReplyEnabled, request.AutoReplyEnabled.Value.ToString().ToLowerInvariant());
        }

        if (request.AutoReplyMessage is not null)
        {
            await SaveOptionAsync(LineOptionKeys.AutoReplyMessage, request.AutoReplyMessage);
        }

        if (request.TextChunkChars is > 0)
        {
            await SaveOptionAsync(LineOptionKeys.TextChunkChars, request.TextChunkChars.Value.ToString());
        }

        if (request.MaxMessagesPerRequest is > 0)
        {
            await SaveOptionAsync(LineOptionKeys.MaxMessagesPerRequest, request.MaxMessagesPerRequest.Value.ToString());
        }

        if (request.ReplyTokenTtlSeconds is > 0)
        {
            await SaveOptionAsync(LineOptionKeys.ReplyTokenTtlSeconds, request.ReplyTokenTtlSeconds.Value.ToString());
        }

        if (request.HumanHandoverMuteMinutes.HasValue && request.HumanHandoverMuteMinutes.Value >= 0)
        {
            await SaveOptionAsync(LineOptionKeys.HumanHandoverMuteMinutes, request.HumanHandoverMuteMinutes.Value.ToString());
        }

        if (request.QuotaWarnPercent is > 0)
        {
            await SaveOptionAsync(LineOptionKeys.QuotaWarnPercent, request.QuotaWarnPercent.Value.ToString());
        }

        // 审计 #12：以下六项此前只有 LineOptions 里有，DTO 没暴露 ⇒ 界面调不了。
        // 校验区间与 UpdateLineSettingsRequest 上的 [Range] 一致；0 值语义逐项注释在下面。
        if (request.StatusCheckTimeoutSeconds is > 0)
        {
            await SaveOptionAsync(LineOptionKeys.StatusCheckTimeoutSeconds, request.StatusCheckTimeoutSeconds.Value.ToString());
        }

        // 重试次数 0 是合法取值（完全关闭重试），所以判 >= 0 而不是 is > 0
        if (request.RetryMaxAttempts is >= 0)
        {
            await SaveOptionAsync(LineOptionKeys.RetryMaxAttempts, request.RetryMaxAttempts.Value.ToString());
        }

        if (request.MaxInboundMediaBytes is > 0)
        {
            await SaveOptionAsync(LineOptionKeys.MaxInboundMediaBytes, request.MaxInboundMediaBytes.Value.ToString());
        }

        if (request.MaxOutboundAttachmentBytes is > 0)
        {
            await SaveOptionAsync(LineOptionKeys.MaxOutboundAttachmentBytes, request.MaxOutboundAttachmentBytes.Value.ToString());
        }

        if (request.OutboundImageExtensions is not null)
        {
            // 出站时按小写扩展名比对（BuildAttachmentMessagesAsync），这里先归一化，
            // 否则 "PNG" 这类写法会静默失效；空串 = 恢复默认（LineSettingsProvider 对空白值不覆盖默认）
            await SaveOptionAsync(LineOptionKeys.OutboundImageExtensions, NormalizeImageExtensions(request.OutboundImageExtensions));
        }

        if (request.ChannelAccessTokenKeyId is not null)
        {
            // key_id 是令牌的可读标识（非密钥，不加密），只在界面展示与轮换核对用；
            // 空串 = 清除该行（LineSettingsProvider 对空白值回落到 appsettings 基线）
            await SaveOptionAsync(LineOptionKeys.ChannelAccessTokenKeyId, request.ChannelAccessTokenKeyId.Trim());
        }

        if (request.MailboxId.HasValue)
        {
            await SaveOptionAsync(LineOptionKeys.MailboxId, request.MailboxId.Value.ToString());
        }

        if (request.Enabled.HasValue)
        {
            await SaveOptionAsync(LineOptionKeys.Enabled, request.Enabled.Value.ToString().ToLowerInvariant());
        }

        await _unitOfWork.SaveChangesAsync();
        await _settingsProvider.RefreshAsync();
        return await GetSettingsAsync();
    }

    private async Task SaveOptionAsync(string name, string value)
    {
        // 必须取 tracked 实体：AsNoTracking 返回的实例脱离 ChangeTracker，改值不会生成 UPDATE
        var existing = await _unitOfWork.SystemOptions.GetByNameTrackedAsync(name);
        if (existing is not null)
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
            return;
        }

        await _unitOfWork.SystemOptions.AddAsync(new SystemOption
        {
            Name = name,
            Value = value,
            Category = LineOptionKeys.Category,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
    }

    /// <summary>
    /// 把扩展名白名单归一化成 "jpg,jpeg,png" 口径：去空白、去前导点、统一小写、按首次出现去重。
    /// 出站比对用的是小写扩展名（BuildAttachmentMessagesAsync），不归一化会让大写写法静默失效。
    /// </summary>
    internal static string NormalizeImageExtensions(string raw)
    {
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.TrimStart('.').ToLowerInvariant())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal);
        return string.Join(',', parts);
    }

    private LineSettingsDto MapSettings(LineOptions options)
    {
        return new LineSettingsDto
        {
            ChannelId = options.ChannelId,
            ApiBaseUrl = options.ApiBaseUrl,
            DataApiBaseUrl = options.DataApiBaseUrl,
            AttachmentPublicBaseUrl = ResolveAttachmentPublicBaseUrl(options),
            AttachmentPublicUrlTtlMinutes = options.AttachmentPublicUrlTtlMinutes,
            RichMenuId = options.RichMenuId,
            WebhookEnabled = options.WebhookEnabled,
            MarkAsReadEnabled = options.MarkAsReadEnabled,
            LoadingIndicatorEnabled = options.LoadingIndicatorEnabled,
            AutoReplyEnabled = options.AutoReplyEnabled,
            AutoReplyMessage = options.AutoReplyMessage,
            ProfileEnrichEnabled = options.ProfileEnrichEnabled,
            TextChunkChars = options.TextChunkChars,
            MaxMessagesPerRequest = options.MaxMessagesPerRequest,
            ReplyTokenTtlSeconds = options.ReplyTokenTtlSeconds,
            HumanHandoverMuteMinutes = options.HumanHandoverMuteMinutes,
            QuotaWarnPercent = options.QuotaWarnPercent,
            // 审计 #12：回显六项可调参数（ChannelAccessTokenKeyId 已在下面，属展示项）
            StatusCheckTimeoutSeconds = options.StatusCheckTimeoutSeconds,
            RetryMaxAttempts = options.RetryMaxAttempts,
            MaxInboundMediaBytes = options.MaxInboundMediaBytes,
            MaxOutboundAttachmentBytes = options.MaxOutboundAttachmentBytes,
            OutboundImageExtensions = options.OutboundImageExtensions,
            MailboxId = options.MailboxId,
            Enabled = options.Enabled,
            WebhookUrl = SafeBuildWebhookUrl(options),
            HasChannelAccessToken = !string.IsNullOrWhiteSpace(options.ChannelAccessToken),
            HasChannelSecret = !string.IsNullOrWhiteSpace(options.ChannelSecret),
            ChannelAccessTokenKeyId = options.ChannelAccessTokenKeyId,
        };
    }

    // ── 状态 ────────────────────────────────────────────────────────────────────

    public async Task<LineStatusDto> GetStatusAsync()
    {
        var options = await _settingsProvider.GetCurrentAsync();
        var status = new LineStatusDto
        {
            Configured = options.IsConfigured,
            Enabled = options.Enabled,
            WebhookEnabled = options.WebhookEnabled,
            WebhookUrl = SafeBuildWebhookUrl(options),
        };

        if (!options.IsConfigured)
        {
            return status;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, options.StatusCheckTimeoutSeconds)));

            var quotaTask = _client.GetQuotaAsync(cts.Token);
            var tokenTask = _client.VerifyTokenAsync(cts.Token);
            var endpointTask = _client.GetWebhookEndpointAsync(cts.Token);
            await Task.WhenAll(quotaTask, tokenTask, endpointTask);

            var quota = quotaTask.Result;
            if (quota is not null)
            {
                status.ApiReachable = true;
                // LINE 的 /v2/bot/message/quota 只返回 type = "limited" | "none"（none = 不限额，此时不带 value）。
                // 前端只认 "unlimited"，所以在这里归一化，否则不限额账号的配额卡永远显示 -/未知。
                status.QuotaType = string.Equals(quota.Type, "none", StringComparison.OrdinalIgnoreCase)
                    ? "unlimited"
                    : quota.Type;
                status.QuotaTotal = quota.Quota;
                status.QuotaConsumed = quota.TotalUsage;
                if (quota.Quota > 0 && quota.TotalUsage >= 0)
                {
                    status.QuotaUsedPercent = (int)Math.Round(quota.TotalUsage * 100.0 / quota.Quota);
                    status.QuotaWarning = status.QuotaUsedPercent >= options.QuotaWarnPercent;
                }
                else if (status.QuotaType == "unlimited")
                {
                    status.QuotaUsedPercent = 0;
                }
            }

            var token = tokenTask.Result;
            if (token is not null)
            {
                status.TokenValid = true;
                status.TokenScope = token.Scope;
                if (token.ExpiresInSeconds is > 0)
                {
                    status.TokenExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds.Value);
                }
            }
            else
            {
                status.TokenValid = false;
                status.TokenInvalidAt = DateTime.UtcNow;
            }

            var endpoint = endpointTask.Result;
            if (endpoint is not null)
            {
                status.WebhookEndpointOnLine = endpoint.Endpoint;
                status.WebhookDeliveryOnLine = endpoint.Active;
            }

            // 审计 #11：ReplyModeHint —— 说明当前会话实际可用的回复方式。
            // 判据不需要额外调用外部 API，复用上面三个任务已经拿到的结果：
            //   reply  = 令牌有效且 WebhookEnabled（入站事件带 replyToken，会话内回复可用；不计入月度推送配额）
            //   push   = 令牌有效但 WebhookEnabled=false（拿不到 replyToken，只能主动推送）
            //   unknown= 令牌未验证/探测超时或失败（无法判定）
            // 该字段是机器可读代码而不是文案：服务端返回中文文案会让前端 i18n 失效（切英文仍显示中文），
            // 与 QuotaType 归一化同一口径，由前端按代码取 locale key（channels.line.status.replyMode*）。
            status.ReplyModeHint = status.TokenValid
                ? (options.WebhookEnabled ? "reply" : "push")
                : "unknown";
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("LINE 状态检测超时（>{Seconds}s），视为不可达，不阻塞页面加载", options.StatusCheckTimeoutSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LINE 状态检测失败");
        }

        // 审计 #11：LastError —— 放在 try/catch 之后，探测超时也要能把最近一次真实失败显示出来。
        // 来源是 LineService 出站失败点写入的 SystemOptions（LineClient 不暴露错误上下文，ILineClient 也扩不动）；
        // 语义与 Messenger 的 last_error_* 一致：记录后不清除，所以它是"最近一次真实失败"而非"当前正在失败"。
        status.LastError = await ReadLastErrorAsync();

        return status;
    }

    /// <summary>
    /// 读回最近一次 LINE 出站失败（审计 #11）。两个键都缺失或 HTTP 状态不可解析时返回 null，
    /// 前端据此不渲染错误条。存储沿用 <see cref="SaveOptionAsync"/> 的口径，不新增表。
    /// </summary>
    private async Task<LineApiError?> ReadLastErrorAsync()
    {
        var rawStatus = await _unitOfWork.SystemOptions.GetValueAsync(LineOptionKeys.LastErrorHttpStatus);
        var message = await _unitOfWork.SystemOptions.GetValueAsync(LineOptionKeys.LastErrorMessage);
        if (!int.TryParse(rawStatus, out var httpStatus) || httpStatus <= 0)
        {
            return null;
        }

        return new LineApiError
        {
            HttpStatus = httpStatus,
            Message = message ?? string.Empty,
        };
    }

    /// <summary>
    /// 记录一次真实的 LINE 出站失败，供状态页回显（审计 #11）。无 HTTP 状态（本地异常/网络不可达）时不记，
    /// 避免把"读不到状态"的前端空白误显示成 HTTP 0。失败本身不应影响回复流程，所以这里吞掉异常只留日志。
    /// </summary>
    private async Task RecordLastErrorAsync(LineApiError? error)
    {
        if (error is null || error.HttpStatus <= 0)
        {
            return;
        }

        try
        {
            await SaveOptionAsync(LineOptionKeys.LastErrorHttpStatus, error.HttpStatus.ToString());
            await SaveOptionAsync(LineOptionKeys.LastErrorMessage, error.Message ?? string.Empty);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LINE 记录最近错误失败（不影响主流程）");
        }
    }

    // ── 回复 ────────────────────────────────────────────────────────────────────

    public async Task<LineReplyResult> ReplyAsync(LineReplyRequest request, int userId)
    {
        var options = await _settingsProvider.GetCurrentAsync();
        if (!options.Enabled || !options.IsConfigured)
        {
            return new LineReplyResult { Sent = false, ErrorMessage = "LINE 未启用或未配置" };
        }

        if (string.IsNullOrWhiteSpace(request.Content) && (request.AttachmentIds is null || request.AttachmentIds.Count == 0))
        {
            return new LineReplyResult { Sent = false, ErrorMessage = "回复内容或附件至少提供一个" };
        }

        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(request.ConversationId);
        if (conversation is null)
        {
            return new LineReplyResult { Sent = false, ErrorMessage = $"会话 {request.ConversationId} 不存在" };
        }

        if (conversation.SourceType != SourceType.Line)
        {
            return new LineReplyResult { Sent = false, ErrorMessage = "该会话不是 LINE 会话" };
        }

        var lineUserId = await ResolveLineUserIdAsync(conversation);
        if (string.IsNullOrWhiteSpace(lineUserId))
        {
            return new LineReplyResult { Sent = false, ErrorMessage = "无法解析该会话的 LINE userId（客户未发送过消息）" };
        }

        // 附件转成公网直链消息对象（LINE 无上传接口）
        var messages = new List<LineMessageObject>();
        var mediaErrors = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Content))
        {
            messages.Add(new LineMessageObject { Type = "text", Text = request.Content! });
        }

        if (request.AttachmentIds is { Count: > 0 })
        {
            var (attachmentMessages, errors) = await BuildAttachmentMessagesAsync(conversation.Id, request.AttachmentIds, options);
            messages.AddRange(attachmentMessages);
            mediaErrors.AddRange(errors);
        }

        if (messages.Count == 0)
        {
            return new LineReplyResult { Sent = false, ErrorMessage = string.Join("；", mediaErrors.DefaultIfEmpty("没有可发送的内容")), MediaErrors = mediaErrors };
        }

        // replyToken 优先（不耗配额）：取最近一条客户消息上的一次性令牌
        string? replyToken = null;
        int? replyTokenThreadId = null;
        if (request.PreferReplyToken)
        {
            (replyToken, replyTokenThreadId) = await TryGetFreshReplyTokenAsync(conversation.Id, options);
        }

        if (options.LoadingIndicatorEnabled)
        {
            // 显示"正在输入…"，失败不影响发送
            await _client.StartLoadingAsync(lineUserId);
        }

        var sendResult = await _client.SendMessagesAsync(messages, replyToken, lineUserId);
        var usedPushFallback = false;

        // replyToken 已在 LINE 侧失效（409 已使用 / 400 令牌过期）⇒ 回落 push。
        // push 消耗月度配额且只对好友生效，因此仅在 reply 明确因令牌失败、且"本次确实没投递"时才回落：
        //  · 409 duplicate 若发生在 PostSendAsync 的 429/5xx 重试之后（Attempts > 1），说明第一次请求
        //    可能已被 LINE 接受（只是响应丢了）。此时投递状态不确定，再 push 就会给客户发两条同样的消息
        //    ⇒ 记为"结果未知"，不回落，交给坐席人工核对。
        // 客户端在「重试后才被判为令牌问题」时已置位 DuplicateRisk（并自己跳过了 push 回落），
        // 这里把它一并纳入判据，避免 400-after-retry 这一支绕过节流之外的第二道防线。
        var duplicateAfterRetry = sendResult.DuplicateRisk
            || (sendResult.Error?.IsDuplicate == true && sendResult.Attempts > 1);
        if (duplicateAfterRetry)
        {
            _logger.LogWarning(
                "LINE reply 在重试后才返回 409（尝试 {Attempts} 次），无法判定是否已投递，跳过 push 回落：conversationId={Id}",
                sendResult.Attempts, conversation.Id);
        }

        if (!sendResult.Success && replyToken is not null && !sendResult.Unreachable
            && !duplicateAfterRetry
            && (sendResult.Error?.IsDuplicate == true || sendResult.Error?.HttpStatus == 400))
        {
            _logger.LogInformation("LINE replyToken 不可用（HTTP {Status}），回落 push conversationId={Id}",
                sendResult.Error?.HttpStatus, conversation.Id);

            var pushResult = await _client.SendMessagesAsync(messages, replyToken: null, toUserId: lineUserId);
            if (pushResult.Success)
            {
                sendResult = pushResult;
                usedPushFallback = true;
            }
        }

        if (!sendResult.Success)
        {
            if (sendResult.Unreachable)
            {
                await MarkConversationUnreachableAsync(conversation, sendResult, options);
            }

            // 审计 #11：把这次真实失败（HTTP 状态 + LINE 返回的 message）记下来，状态卡的 LastError 才有内容。
            // 此刻没有开启事务（成功分支才开），所以写 SystemOptions 不会与回复流程互相回滚。
            await RecordLastErrorAsync(sendResult.Error);

            return new LineReplyResult
            {
                Sent = false,
                Via = sendResult.Via,
                ChunkCount = sendResult.ChunkCount,
                Unreachable = sendResult.Unreachable,
                MediaErrors = mediaErrors,
                ErrorMessage = sendResult.ErrorMessage ?? "LINE 发送失败",
            };
        }

        var usedFallback = usedPushFallback || (replyToken is not null && sendResult.Via != "reply");

        try
        {
            await using var transaction = await _unitOfWork.BeginTransactionAsync();
            var now = DateTime.UtcNow;

            var thread = new ConversationThread
            {
                ConversationId = conversation.Id,
                // LINE 机器人没有可读名称，用 ChannelId（@ 开头的 Basic ID）或固定标识
                From = string.IsNullOrWhiteSpace(options.ChannelId) ? "line@bot" : options.ChannelId!,
                To = lineUserId!,
                Subject = string.Empty,
                Body = BuildReplyHtmlBody(request, mediaErrors),
                MessageId = sendResult.ExternalMessageId ?? $"line-reply-{Guid.NewGuid():N}",
                SourceType = SourceType.Line,
                Type = ThreadType.Message,
                SourceVia = SourceVia.User,
                CreatedByUserId = userId,
                HasAttachments = request.AttachmentIds is { Count: > 0 },
                SendStatus = SendStatus.Sent,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await _unitOfWork.Threads.AddAsync(thread);
            await _unitOfWork.SaveChangesAsync();

            if (thread.Id > 0 && request.AttachmentIds is { Count: > 0 })
            {
                var attachments = await _unitOfWork.Attachments.GetByIdsTrackedAsync(request.AttachmentIds);
                foreach (var attachment in attachments)
                {
                    attachment.ThreadId = thread.Id;
                }

                await _unitOfWork.SaveChangesAsync();
            }

            // 令牌用完即废：把 used 标记写回原线程 Headers，避免二次使用触发 409
            if (replyTokenThreadId.HasValue)
            {
                await MarkReplyTokenUsedAsync(replyTokenThreadId.Value);
            }

            conversation.UpdatedAt = now;
            conversation.LastReplyAt = now;
            conversation.LastReplyFrom = SourceVia.User;

            // 人工接管：客服发言后自动静默机器人，避免与坐席抢话
            await ApplyHumanTakeoverMarkerAsync(conversation.Id, options.HumanHandoverMuteMinutes, userId, now);

            await transaction.CommitAsync();

            await _notificationPublisher.NotifyMailboxAsync(conversation.MailboxId.ToString(), "conversation_updated",
                new { conversationId = conversation.Id });

            if (_webhookService is not null)
            {
                try
                {
                    await _webhookService.TriggerAsync("conversation_updated", conversation.MailboxId,
                        new { conversationId = conversation.Id }, "Conversation", conversation.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LINE conversation_updated webhook 触发失败 conversationId={Id}", conversation.Id);
                }
            }

            return new LineReplyResult
            {
                Sent = true,
                ThreadCreated = true,
                ThreadId = thread.Id,
                Via = sendResult.Via,
                ChunkCount = sendResult.ChunkCount,
                FallbackFromReplyToken = usedFallback,
                Unreachable = false,
                MediaErrors = mediaErrors,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LINE 回复线程写入失败 conversationId={Id}", conversation.Id);
            // 消息已发出，仅本地记录失败 ⇒ 207 语义，坐席可见但不应重发
            return new LineReplyResult
            {
                Sent = true,
                ThreadCreated = false,
                Via = sendResult.Via,
                ChunkCount = sendResult.ChunkCount,
                FallbackFromReplyToken = usedFallback,
                MediaErrors = mediaErrors,
                ErrorMessage = "消息已发送，但会话线程记录失败",
            };
        }
    }

    /// <summary>把附件转为 image/file 消息（签发本部署公网直链）；失败项收集为错误文案</summary>
    private async Task<(List<LineMessageObject> Messages, List<string> Errors)> BuildAttachmentMessagesAsync(
        int conversationId,
        List<int> attachmentIds,
        LineOptions options)
    {
        var messages = new List<LineMessageObject>();
        var errors = new List<string>();

        var publicBase = ResolveAttachmentPublicBaseUrl(options);
        if (string.IsNullOrWhiteSpace(publicBase))
        {
            errors.Add("未配置 App:BaseUrl 或 Line:AttachmentPublicBaseUrl，无法生成 LINE 附件直链");
            _logger.LogWarning("LINE 附件直链基址缺失 conversationId={Id}", conversationId);
            return (messages, errors);
        }

        var secret = options.ChannelSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            errors.Add("缺少 ChannelSecret，无法签发附件直链签名");
            return (messages, errors);
        }

        var attachments = await _unitOfWork.Attachments.GetByIdsAsync(attachmentIds);
        if (attachments.Count == 0)
        {
            errors.Add("附件不存在");
            return (messages, errors);
        }

        // 越权防护：attachmentIds 由前端直接传入，必须先确认这些附件不属于「别的会话」。
        // 直链签名只绑定 attachmentId+exp（不绑身份），一旦签发就等于把文件公开，任何拿到链接的人
        // 在 TTL 内都能下载；不校验归属时，持有 line.manage 的坐席可把别处客户的附件签成公网直链。
        // 判据取「附件所挂线程是否属于本会话」：ThreadId 为空（刚上传、尚未挂到线程）时放行——
        // 那条路径本来就是坐席自己刚传的文件，硬拦会把正常发送一起打死。
        var conversationThreads = await _unitOfWork.Threads.FindAsync(t => t.ConversationId == conversationId)
            ?? Enumerable.Empty<ConversationThread>();
        var ownedThreadIds = conversationThreads.Select(t => t.Id).ToHashSet();
        var foreign = attachments
            .Where(a => a.ThreadId is not null && !ownedThreadIds.Contains(a.ThreadId.Value))
            .ToList();
        if (foreign.Count > 0)
        {
            _logger.LogWarning(
                "LINE 附件属于其它会话，已拒绝签发直链：conversationId={ConversationId} 附件={Ids}",
                conversationId, string.Join(",", foreign.Select(a => a.Id)));
            foreach (var f in foreign)
            {
                errors.Add($"附件 {f.FileName} 不属于当前会话，已拒绝");
            }
            attachments = attachments
                .Where(a => a.ThreadId is null || ownedThreadIds.Contains(a.ThreadId.Value))
                .ToList();
            if (attachments.Count == 0)
            {
                return (messages, errors);
            }
        }

        var imageExtensions = (options.OutboundImageExtensions ?? "jpg,jpeg,png")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(options.AttachmentPublicUrlTtlMinutes > 0 ? options.AttachmentPublicUrlTtlMinutes : 10080);

        foreach (var attachment in attachments)
        {
            if (attachment.Size > options.MaxOutboundAttachmentBytes)
            {
                errors.Add($"附件 {attachment.FileName} 超过 LINE 直链上限（{attachment.Size} > {options.MaxOutboundAttachmentBytes} 字节）");
                continue;
            }

            // 附件必须已落库到本站存储，否则直链取不到文件
            var baseDir = AttachmentPathResolver.ResolveBasePath(_configuration["Attachment:UploadPath"], _environment.ContentRootPath);
            // 与 Telegram/WhatsApp/WeChat 同口径：拼成绝对路径后必须确认仍落在附件根目录内，
            // 否则 FileDir 里的 ".." 会让运维侧读到服务器上的任意文件。
            var fullPath = Path.GetFullPath(Path.Combine(baseDir, attachment.FileDir ?? string.Empty, attachment.StoredFileName));
            var normalizedBase = Path.GetFullPath(baseDir) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("LINE 附件路径越出附件根目录，已拒绝：AttachmentId={Id} FileDir={Dir}", attachment.Id, attachment.FileDir);
                errors.Add($"附件 {attachment.Id} 路径非法");
                continue;
            }

            var storedPath = fullPath;
            if (!File.Exists(storedPath))
            {
                                // 对外错误只给可读信息；解析后的绝对路径只进日志（与 WeChatService 同一口径：Path={Path}），
                // 便于直接看出 FileDir（yyyy/MM/dd 嵌套目录）与实际落盘路径是否对得上。
                _logger.LogWarning("LINE 附件实体缺失，无法生成直链：AttachmentId={Id} FileDir={Dir} StoredFileName={Name} Path={Path}",
                    attachment.Id, attachment.FileDir, attachment.StoredFileName, storedPath);
                errors.Add($"附件 {attachment.FileName} 文件不存在，无法生成直链");
                continue;
            }

            var extension = Path.GetExtension(attachment.FileName).TrimStart('.').ToLowerInvariant();
            var isImage = attachment.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                && (extension is "jpg" or "jpeg" or "png" || imageExtensions.Contains(extension));

            var url = LineMediaLinkSigner.BuildUrl(publicBase, attachment.Id, expiresAt, secret!);

            if (isImage)
            {
                // LINE image 消息要求 previewImageUrl；没有缩略图时指向 /preview（服务端按原图回传）
                var previewUrl = LineMediaLinkSigner.BuildUrl(publicBase, attachment.Id, expiresAt, secret!, "/preview");
                messages.Add(new LineMessageObject
                {
                    Type = "image",
                    OriginalContentUrl = url,
                    PreviewImageUrl = previewUrl,
                });
                continue;
            }

            if (attachment.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
                || attachment.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            {
                // LINE audio/video 必须给出精确时长（毫秒）且对编码有硬性要求；
                // 无法从附件表可靠取得时长 ⇒ 按 file 消息发送（客户可下载播放），避免被判参数错误。
                _logger.LogInformation("LINE 音视频附件按 file 发送（时长未知）attachmentId={Id}", attachment.Id);
            }

            messages.Add(new LineMessageObject
            {
                Type = "file",
                OriginalContentUrl = url,
                FileName = string.IsNullOrWhiteSpace(attachment.FileName) ? "attachment" : attachment.FileName,
                FileSize = attachment.Size,
            });
        }

        return (messages, errors);
    }

    /// <summary>取最近一条客户消息上仍存活的 replyToken（一次性）</summary>
    private async Task<(string? Token, int? ThreadId)> TryGetFreshReplyTokenAsync(int conversationId, LineOptions options)
    {
        var latest = await _unitOfWork.Threads.GetLatestCustomerThreadAsync(conversationId);
        if (latest is null || string.IsNullOrWhiteSpace(latest.Headers) || latest.SourceType != SourceType.Line)
        {
            return (null, null);
        }

        var age = DateTime.UtcNow - latest.CreatedAt;
        var ttl = TimeSpan.FromSeconds(options.ReplyTokenTtlSeconds > 0 ? options.ReplyTokenTtlSeconds : 25);
        if (age > ttl)
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(latest.Headers);
            if (!doc.RootElement.TryGetProperty("replyToken", out var tokenElement)
                || tokenElement.ValueKind != JsonValueKind.String)
            {
                return (null, null);
            }

            // 一次性令牌用完即废，绝不能再打 reply（LINE 返回 409 并浪费一次请求）。
            // Headers 由多方写入（入站侧写 Dictionary<string,string> ⇒ 全是 JSON 字符串；
            // MarkReplyTokenUsedAsync 写 JSON 布尔），两种形态都必须认，否则已用令牌会被重放。
            if (doc.RootElement.TryGetProperty("replyTokenUsed", out var used)
                && (used.ValueKind == JsonValueKind.True
                    || (used.ValueKind == JsonValueKind.String
                        && bool.TryParse(used.GetString(), out var usedFlag) && usedFlag)))
            {
                return (null, null);
            }

            var token = tokenElement.GetString();
            return string.IsNullOrWhiteSpace(token) ? (null, null) : (token, latest.Id);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private async Task MarkReplyTokenUsedAsync(int threadId)
    {
        var thread = await _unitOfWork.Threads.GetByIdTrackedAsync(threadId);
        if (thread is null || string.IsNullOrWhiteSpace(thread.Headers))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(thread.Headers);
            var values = new Dictionary<string, JsonElement>();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.Clone();
            }

            values["replyTokenUsed"] = JsonDocument.Parse("true").RootElement;
            thread.Headers = JsonSerializer.Serialize(values);
            thread.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
        }
        catch (JsonException)
        {
            // Headers 非 JSON：不冒险改写，令牌自然过期回落 push
        }
    }

    /// <summary>404/403 目标不可达：写一条内部备注线程（每天最多一条），供坐席与状态页识别</summary>
    private async Task MarkConversationUnreachableAsync(Conversation conversation, LineSendResult sendResult, LineOptions options)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var markerMessageId = $"{UnreachableThreadPrefix}{conversation.Id}-{today}";
        var existing = await _unitOfWork.Threads.FindAsync(t => t.MessageId == markerMessageId && t.State != ThreadState.Deleted);
        if (existing.Any())
        {
            return;
        }

        var reason = sendResult.Error?.IsNotFriended == true
            ? "客户未添加本机器人为好友（或已屏蔽），消息无法送达"
            : "LINE 拒绝向该客户投递（可能被屏蔽或配额受限），消息无法送达";

        try
        {
            var now = DateTime.UtcNow;
            await _unitOfWork.Threads.AddAsync(new ConversationThread
            {
                ConversationId = conversation.Id,
                Type = ThreadType.Note,
                IsInternal = true,
                SourceType = SourceType.Line,
                SourceVia = SourceVia.User,
                From = "line@system",
                To = string.Empty,
                Subject = "LINE 会话不可达",
                Body = $"<div class=\"line-unreachable\">{HttpUtility.HtmlEncode(reason)}<br/>{HttpUtility.HtmlEncode(sendResult.ErrorMessage ?? string.Empty)}</div>",
                MessageId = markerMessageId,
                SendStatus = SendStatus.DeliveryError,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await _unitOfWork.SaveChangesAsync();
            _logger.LogWarning("LINE 会话标记为不可达 conversationId={Id}：{Reason}", conversation.Id, reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LINE 不可达标记写入失败 conversationId={Id}", conversation.Id);
        }
    }

    // ── 人工接管 ────────────────────────────────────────────────────────────────

    public async Task<bool> SetHumanTakeoverAsync(LineTakeoverRequest request, int userId)
    {
        var options = await _settingsProvider.GetCurrentAsync();
        var minutes = request.Minutes ?? (options.HumanHandoverMuteMinutes > 0 ? options.HumanHandoverMuteMinutes : 60);
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(request.ConversationId);
        if (conversation is null)
        {
            return false;
        }

        await ApplyHumanTakeoverMarkerAsync(conversation.Id, minutes, userId, DateTime.UtcNow);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ReleaseHumanTakeoverAsync(int conversationId, int userId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(conversationId);
        if (conversation is null)
        {
            return false;
        }

        var notes = await _unitOfWork.Threads.FindAsync(t =>
            t.ConversationId == conversationId
            && t.MessageId.StartsWith(TakeoverThreadPrefix)
            && t.State != ThreadState.Deleted);

        var now = DateTime.UtcNow;
        foreach (var note in notes)
        {
            note.Body = note.Body.Replace("\"untilUtc\":\"", "\"untilUtc\":\"RELEASED ");
            note.UpdatedAt = now;
        }

        // 直接写一条已结束的标记（untilUtc = now）比改写历史备注更可靠
        await _unitOfWork.Threads.AddAsync(new ConversationThread
        {
            ConversationId = conversationId,
            Type = ThreadType.Note,
            IsInternal = true,
            SourceType = SourceType.Line,
            SourceVia = SourceVia.User,
            From = "line@system",
            Subject = "LINE 接管结束",
            Body = $"<div class=\"line-takeover\">机器人自动回复已恢复（操作人 {userId}）</div>",
            MessageId = $"{TakeoverThreadPrefix}{conversationId}-released-{Guid.NewGuid():N}",
            Headers = JsonSerializer.Serialize(new Dictionary<string, string> { ["untilUtc"] = now.ToString("O") }),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    private async Task ApplyHumanTakeoverMarkerAsync(int conversationId, int minutes, int userId, DateTime now)
    {
        if (minutes <= 0)
        {
            return;
        }

        var until = now.AddMinutes(minutes);
        await _unitOfWork.Threads.AddAsync(new ConversationThread
        {
            ConversationId = conversationId,
            Type = ThreadType.Note,
            IsInternal = true,
            SourceType = SourceType.Line,
            SourceVia = SourceVia.User,
            From = "line@system",
            Subject = "LINE 人工接管",
            Body = $"<div class=\"line-takeover\">人工接管至 {until:u}（{minutes} 分钟，操作人 {userId}）</div>",
            MessageId = $"{TakeoverThreadPrefix}{conversationId}-{Guid.NewGuid():N}",
            Headers = JsonSerializer.Serialize(new Dictionary<string, string> { ["untilUtc"] = until.ToString("O") }),
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    // ── 测试 / 自检 ─────────────────────────────────────────────────────────────

    public async Task<LineSendResult> PushTestAsync(LinePushTestRequest request)
    {
        var options = await _settingsProvider.GetCurrentAsync();
        if (!options.IsConfigured)
        {
            return new LineSendResult { Success = false, ErrorMessage = "LINE 未配置" };
        }

        var target = request.UserId;
        if (request.ConversationId.HasValue)
        {
            var conversation = await _unitOfWork.Conversations.GetByIdAsync(request.ConversationId.Value);
            if (conversation is not null)
            {
                target = await ResolveLineUserIdAsync(conversation);
            }
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return new LineSendResult { Success = false, ErrorMessage = "未指定可推送的 LINE userId（push 只对已添加机器人的客户生效，且消耗月度配额）" };
        }

        var result = await _client.SendTextAsync(request.Text, null, target!);
        if (!result.Success)
        {
            // 审计 #11：手动推送测试失败也是"最近一次真实失败"，同样记下来供状态卡显示
            await RecordLastErrorAsync(result.Error);
        }

        return result;
    }

    public async Task<LineWebhookCheckResult> CheckWebhookAsync(LineWebhookCheckRequest request)
    {
        var options = await _settingsProvider.GetCurrentAsync();
        var url = string.IsNullOrWhiteSpace(request.WebhookUrl) ? BuildWebhookUrl(options) : request.WebhookUrl!.Trim();
        return await _client.CheckWebhookAsync(url);
    }

    public async Task<LineWebhookCheckResult> ApplyWebhookEndpointAsync(bool enabled)
    {
        var options = await _settingsProvider.GetCurrentAsync();
        var url = BuildWebhookUrl(options);
        var ok = await _client.SetWebhookEndpointAsync(url, enabled);
        return new LineWebhookCheckResult
        {
            Success = ok,
            WebhookUrl = url,
            Detail = ok ? (enabled ? "已在 LINE 登记回调地址并开启 webhook" : "已在 LINE 登记回调地址并关闭 webhook") : string.Empty,
            ErrorMessage = ok ? null : "LINE webhook 端点配置失败，请检查访问令牌权限（需 Channel management 权限）",
        };
    }

    public async Task<LineTestResult> TestConnectionAsync()
    {
        var options = await _settingsProvider.GetCurrentAsync();
        if (!options.IsConfigured)
        {
            return new LineTestResult { Success = false, ErrorMessage = "LINE 未配置" };
        }

        var reachable = await _client.TestConnectionAsync();
        return new LineTestResult
        {
            Success = reachable,
            ErrorMessage = reachable ? null : "LINE Message API 不可达（检查令牌是否过期、服务器出网是否允许 api.line.me）",
        };
    }

    // ── 辅助 ────────────────────────────────────────────────────────────────────

    /// <summary>会话对外的 LINE userId：优先 Customer.ChannelId，回落最近客户线程的 From</summary>
    private async Task<string?> ResolveLineUserIdAsync(Conversation conversation)
    {
        if (conversation.CustomerId is > 0)
        {
            var customer = await _unitOfWork.Customers.GetByIdAsync(conversation.CustomerId.Value);
            if (customer is not null && string.Equals(customer.Channel, LineChannelConst, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(customer.ChannelId))
            {
                return customer.ChannelId;
            }
        }

        var latestCustomer = await _unitOfWork.Threads.GetLatestCustomerThreadAsync(conversation.Id);
        return string.IsNullOrWhiteSpace(latestCustomer?.From) ? null : latestCustomer!.From;
    }

    internal const string LineChannelConst = "line";

    internal static string BuildReplyHtmlBody(LineReplyRequest request, List<string>? mediaErrors = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Content))
        {
            parts.Add($"<div class=\"line-text\">{HttpUtility.HtmlEncode(request.Content).Replace("\n", "<br>")}</div>");
        }

        var attachmentCount = request.AttachmentIds?.Count ?? 0;
        if (attachmentCount > 0)
        {
            parts.Add($"<div class=\"line-attachments\">附件 {attachmentCount} 个（经本部署公网直链发送）</div>");
        }

        if (mediaErrors is { Count: > 0 })
        {
            parts.Add($"<div class=\"line-media-errors\">{HttpUtility.HtmlEncode(string.Join("；", mediaErrors))}</div>");
        }

        return string.Concat(parts);
    }

    /// <summary>LINE 回调地址（公网 HTTPS，LINE 控制台填写用）</summary>
    private string BuildWebhookUrl(LineOptions options)
    {
        var baseUrl = _configuration["App:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("缺少配置项 App:BaseUrl，请通过环境变量或 appsettings.json 配置生产环境域名");
        }

        // 回调路由是编译期常量（LineController 的 [HttpPost("webhook")]），配置项写成别的值只会让真实回调 404。
        // 这里固定输出真实路由，并在配置漂移时告警，避免运维照着页面把错地址填进 LINE 控制台。
        const string fixedPath = "/api/line/webhook";
        if (!string.IsNullOrWhiteSpace(options.WebhookPath) &&
            !string.Equals(options.WebhookPath.Trim(), fixedPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "LINE WebhookPath 配置已忽略：回调路由固定为 {Fixed}，配置为 {Configured}",
                fixedPath,
                options.WebhookPath);
        }

        return $"{baseUrl.TrimEnd('/')}{fixedPath}";
    }

    private string? SafeBuildWebhookUrl(LineOptions options)
    {
        try
        {
            return BuildWebhookUrl(options);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>附件公网直链基址：优先 Line:AttachmentPublicBaseUrl，回落 App:BaseUrl</summary>
    private string? ResolveAttachmentPublicBaseUrl(LineOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AttachmentPublicBaseUrl))
        {
            return options.AttachmentPublicBaseUrl.TrimEnd('/');
        }

        var baseUrl = _configuration["App:BaseUrl"];
        return string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.TrimEnd('/');
    }
}
