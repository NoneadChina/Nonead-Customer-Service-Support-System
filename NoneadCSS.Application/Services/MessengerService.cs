using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
/// Messenger 管理服务 - 配置读写（密钥 AES 加密 + ENC: 前缀）、连接状态、坐席回复。
/// 与 WhatsApp 的关键差异：
///   - 24h 窗口外不发送模板消息，而是按 MessageTagPolicy 带 message_tag(HUMAN_AGENT) 发送；
///   - 出站文本按 TextChunkChars（默认 2000）分片，同一 PSID 串行发送（规避 613 限流）；
///   - Page Access Token 无刷新端点：观察到 Graph 190 时把失效时间写入 SystemOptions 供状态页告警。
/// </summary>
public class MessengerService : IMessengerService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEncryptionService _encryptionService;
    private readonly IMessengerSettingsProvider _settingsProvider;
    private readonly IMessengerClient _messengerClient;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<MessengerService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _env;
    private readonly IWebhookService? _webhookService;

    public MessengerService(
        IUnitOfWork unitOfWork,
        IEncryptionService encryptionService,
        IMessengerSettingsProvider settingsProvider,
        IMessengerClient messengerClient,
        ICurrentUserAccessor currentUserAccessor,
        INotificationPublisher notificationPublisher,
        ILogger<MessengerService> logger,
        IConfiguration configuration,
        IHostEnvironment env,
        IWebhookService? webhookService = null)
    {
        _unitOfWork = unitOfWork;
        _encryptionService = encryptionService;
        _settingsProvider = settingsProvider;
        _messengerClient = messengerClient;
        _currentUserAccessor = currentUserAccessor;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
        _configuration = configuration;
        _env = env;
        _webhookService = webhookService;
    }

    public Task<MessengerSettingsDto> GetSettingsAsync()
    {
        var opt = _settingsProvider.Current;

        return Task.FromResult(new MessengerSettingsDto
        {
            AppId = opt.AppId,
            PageId = opt.PageId,
            PageUsername = opt.PageUsername,
            ApiVersion = opt.ApiVersion,
            MailboxId = opt.MailboxId,
            TextChunkChars = opt.TextChunkChars,
            WindowHours = opt.WindowHours,
            MessageTagPolicy = opt.MessageTagPolicy,
            MessageTag = opt.MessageTag,
            TypingIndicator = opt.TypingIndicator,
            MarkSeen = opt.MarkSeen,
            ContactCollectionEnabled = opt.ContactCollectionEnabled,
            AutoReplyEnabled = opt.AutoReplyEnabled,
            AutoReplyMessage = opt.AutoReplyMessage,
            Enabled = opt.Enabled,
            SubscribedFields = opt.SubscribedFields,
            CallbackUrl = BuildCallbackUrl(),
            // 密钥只回报是否已配置，绝不回显原值
            HasPageAccessToken = !string.IsNullOrWhiteSpace(opt.PageAccessToken),
            HasAppSecret = !string.IsNullOrWhiteSpace(opt.AppSecret),
            HasVerifyToken = !string.IsNullOrWhiteSpace(opt.VerifyToken)
        });
    }

    public async Task<MessengerSettingsDto> UpdateSettingsAsync(UpdateMessengerSettingsRequest request)
    {
        // 持久化到 SystemOptions 表（Category=messenger，键名小写下划线）
        if (!string.IsNullOrEmpty(request.AppId))
            await SaveOptionAsync(MessengerOptionKeys.AppId, request.AppId);

        if (!string.IsNullOrEmpty(request.AppSecret))
        {
            var encrypted = "ENC:" + _encryptionService.Encrypt(request.AppSecret);
            await SaveOptionAsync(MessengerOptionKeys.AppSecret, encrypted);
        }

        if (!string.IsNullOrEmpty(request.PageId))
            await SaveOptionAsync(MessengerOptionKeys.PageId, request.PageId);

        if (!string.IsNullOrEmpty(request.PageAccessToken))
        {
            var encrypted = "ENC:" + _encryptionService.Encrypt(request.PageAccessToken);
            await SaveOptionAsync(MessengerOptionKeys.PageAccessToken, encrypted);
        }

        if (!string.IsNullOrEmpty(request.VerifyToken))
            await SaveOptionAsync(MessengerOptionKeys.VerifyToken, request.VerifyToken);

        if (request.PageUsername != null)
            await SaveOptionAsync(MessengerOptionKeys.PageUsername, request.PageUsername);

        if (request.ApiVersion != null)
            await SaveOptionAsync(MessengerOptionKeys.ApiVersion, request.ApiVersion);

        if (request.MailboxId.HasValue)
            await SaveOptionAsync(MessengerOptionKeys.MailboxId, request.MailboxId.Value.ToString());

        if (request.TextChunkChars.HasValue)
            await SaveOptionAsync(MessengerOptionKeys.TextChunkChars, request.TextChunkChars.Value.ToString());

        if (request.WindowHours.HasValue)
            await SaveOptionAsync(MessengerOptionKeys.WindowHours, request.WindowHours.Value.ToString());

        if (request.MessageTagPolicy != null)
            await SaveOptionAsync(MessengerOptionKeys.MessageTagPolicy, request.MessageTagPolicy);

        if (request.TypingIndicator.HasValue)
            await SaveOptionAsync(MessengerOptionKeys.TypingIndicator, request.TypingIndicator.Value.ToString().ToLowerInvariant());

        if (request.MarkSeen.HasValue)
            await SaveOptionAsync(MessengerOptionKeys.MarkSeen, request.MarkSeen.Value.ToString().ToLowerInvariant());

        if (request.ContactCollectionEnabled.HasValue)
            await SaveOptionAsync(MessengerOptionKeys.ContactCollectionEnabled, request.ContactCollectionEnabled.Value.ToString().ToLowerInvariant());

        if (request.AutoReplyEnabled.HasValue)
            await SaveOptionAsync(MessengerOptionKeys.AutoReplyEnabled, request.AutoReplyEnabled.Value.ToString().ToLowerInvariant());

        if (request.AutoReplyMessage != null)
            await SaveOptionAsync(MessengerOptionKeys.AutoReplyMessage, request.AutoReplyMessage);

        if (request.Enabled.HasValue)
            await SaveOptionAsync(MessengerOptionKeys.Enabled, request.Enabled.Value.ToString().ToLowerInvariant());

        if (request.SubscribedFields != null)
            await SaveOptionAsync(MessengerOptionKeys.SubscribedFields, request.SubscribedFields);

        await _unitOfWork.SaveChangesAsync();

        // 刷新配置缓存，使新配置立即生效
        await _settingsProvider.RefreshAsync();

        return await GetSettingsAsync();
    }

    public async Task<MessengerStatusDto> GetStatusAsync()
    {
        var opt = _settingsProvider.Current;
        var status = new MessengerStatusDto
        {
            Configured = opt.IsConfigured,
            Enabled = opt.Enabled,
            CallbackUrl = BuildCallbackUrl(),
            PageId = opt.PageId,
            ApiVersion = opt.ApiVersion
        };

        // 24h 窗口状态：取最近一条 Messenger 客户线程（与 ReplyAsync 的窗口判定同源）
        try
        {
            var recentThreads = await _unitOfWork.Threads.FindAsync(
                t => t.SourceType == SourceType.Messenger
                    && t.Type == ThreadType.Customer
                    && t.State != ThreadState.Deleted);
            var recentThread = recentThreads
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefault();

            if (recentThread != null)
            {
                status.LastCustomerMessageAt = recentThread.CreatedAt;
                var remaining = recentThread.CreatedAt.AddHours(opt.WindowHours) - DateTime.UtcNow;

                if (remaining <= TimeSpan.Zero)
                {
                    status.WindowStatus = "expired";
                    status.WindowRemainingHours = 0;
                }
                else if (remaining <= TimeSpan.FromHours(opt.WarningThresholdHours))
                {
                    status.WindowStatus = "warning";
                    status.WindowRemainingHours = (int)Math.Ceiling(remaining.TotalHours);
                }
                else
                {
                    status.WindowStatus = "ok";
                    status.WindowRemainingHours = (int)Math.Ceiling(remaining.TotalHours);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询 Messenger 客服窗口状态失败");
        }

        // Page Access Token 失效时间（Graph 190 观察到时写入 SystemOptions）
        try
        {
            var tokenInvalidAt = await _unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.LastTokenInvalidAt);
            if (!string.IsNullOrWhiteSpace(tokenInvalidAt) && DateTime.TryParse(tokenInvalidAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                status.TokenInvalidAt = parsed;

            var errorCodeValue = await _unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.LastErrorCode);
            var fbtraceValue = await _unitOfWork.SystemOptions.GetValueAsync(MessengerOptionKeys.LastErrorFbtraceId);
            if (int.TryParse(errorCodeValue, out var errorCode))
            {
                status.LastError = new MessengerApiError
                {
                    Code = errorCode,
                    Message = $"最近一次 Graph 错误 code={errorCode}",
                    FbtraceId = string.IsNullOrWhiteSpace(fbtraceValue) ? null : fbtraceValue
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取 Messenger 诊断配置失败");
        }

        // Graph API 可达性：真实探测（带超时，避免网络波动误判、阻塞页面加载）
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(opt.StatusCheckTimeoutSeconds));
            status.ApiReachable = opt.IsConfigured && await _messengerClient.TestConnectionAsync(cts.Token);
            status.LastError ??= _messengerClient.LastError;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Messenger Graph API 可达性检测超时（>{Timeout}s），视为不可达，不阻塞页面加载", opt.StatusCheckTimeoutSeconds);
            status.ApiReachable = false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Messenger Graph API 可达性检测失败");
            status.ApiReachable = false;
        }

        return status;
    }

    public async Task<MessengerReplyResult> ReplyAsync(MessengerReplyRequest request, int userId)
    {
        _logger.LogInformation("Messenger 开始回复：ConversationId={ConvId}, UserId={UserId}, ContentLength={ContentLen}, AttachmentCount={AttachCount}",
            request.ConversationId, userId, request.Content?.Length ?? 0, request.AttachmentIds?.Count ?? 0);

        var opt = _settingsProvider.Current;
        if (!opt.Enabled || !opt.IsConfigured)
        {
            _logger.LogWarning("Messenger 回复被拒绝：未启用或未配置（Enabled={Enabled}, IsConfigured={IsConfigured}）", opt.Enabled, opt.IsConfigured);
            return new MessengerReplyResult { Sent = false, ErrorMessage = "Messenger 未启用或未配置" };
        }

        if (string.IsNullOrWhiteSpace(request.Content) && (request.AttachmentIds == null || request.AttachmentIds.Count == 0))
        {
            return new MessengerReplyResult { Sent = false, ErrorMessage = "回复内容与附件不能同时为空" };
        }

        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(request.ConversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Messenger 回复被拒绝：会话不存在 ConversationId={ConvId}", request.ConversationId);
            return new MessengerReplyResult { Sent = false, ErrorMessage = "会话不存在" };
        }

        // 必须是 Messenger 会话：否则持有 messenger.manage 的账号可以拿一个微信/WhatsApp 会话 ID，
        // 把文本经 Messenger Send API 发到陌生 PSID，并把 Messenger 线程写进别的渠道会话。
        if (conversation.SourceType != SourceType.Messenger)
        {
            _logger.LogWarning("Messenger 回复被拒绝：会话渠道不匹配 ConversationId={ConvId}, SourceType={SourceType}",
                conversation.Id, conversation.SourceType);
            return new MessengerReplyResult { Sent = false, ErrorMessage = "会话不存在" };
        }

        // 24h 窗口判定：窗口内不带 tag；窗口外按 MessageTagPolicy 决定带 tag 或拒绝
        var window = await CheckWindowAsync(conversation.Id, opt.WindowHours, opt.WarningThresholdHours);
        var messageTag = ResolveMessageTag(window, opt, out var rejectReason);
        if (messageTag == RejectTag)
        {
            _logger.LogWarning("Messenger 回复被拒绝：{Reason} ConversationId={ConvId}", rejectReason, conversation.Id);
            return new MessengerReplyResult { Sent = false, ErrorMessage = rejectReason };
        }

        _logger.LogDebug("Messenger 窗口检查：ConversationId={ConvId}, Window={Window}, MessageTag={Tag}",
            conversation.Id, window, messageTag ?? "(none)");

        // 提取客户 PSID（最近一条客户线程的 From）
        var threads = await _unitOfWork.Threads.FindAsync(
            t => t.ConversationId == request.ConversationId
                && t.Type == ThreadType.Customer
                && t.State != ThreadState.Deleted);
        var lastCustomerThread = threads.OrderByDescending(t => t.CreatedAt).FirstOrDefault();
        if (lastCustomerThread == null || string.IsNullOrEmpty(lastCustomerThread.From))
        {
            _logger.LogWarning("Messenger 回复被拒绝：无法获取客户 PSID ConversationId={ConvId}", request.ConversationId);
            return new MessengerReplyResult { Sent = false, ErrorMessage = "无法获取客户 Messenger 标识(PSID)" };
        }

        var psid = lastCustomerThread.From;
        _logger.LogDebug("Messenger 客户 PSID 提取成功：ConversationId={ConvId}, PsId={PsId}", conversation.Id, psid);

        if (opt.TypingIndicator)
        {
            try
            {
                await _messengerClient.SendSenderActionAsync(psid, "typing_on");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Messenger typing_on 失败（不影响发送）：PsId={PsId}", psid);
            }
        }

        string? sentMessageId = null;
        var chunkCount = 0;
        var sendErrors = new List<string>();

        // 发送文本（按 TextChunkChars 分片，同一 PSID 串行，规避 613 限流）
        if (!string.IsNullOrWhiteSpace(request.Content))
        {
            var chunks = SplitText(request.Content, opt.TextChunkChars);
            _logger.LogDebug("Messenger 文本分片：总长度={Len}, 分片数={Count}, 上限={Max}", request.Content.Length, chunks.Count, opt.TextChunkChars);

            for (var i = 0; i < chunks.Count; i++)
            {
                var mid = await SendTextWithBackoffAsync(psid, chunks[i], messageTag, request.ReplyToMessageId);
                if (string.IsNullOrEmpty(mid))
                {
                    var error = _messengerClient.LastError;
                    await PersistErrorDiagnosticsAsync(error);
                    var detail = FormatError(error, $"第 {i + 1}/{chunks.Count} 片发送失败");
                    sendErrors.Add(detail);
                    _logger.LogError("Messenger 文本分片发送失败：ConversationId={ConvId}, PsId={PsId}, Chunk={Index}/{Total}, Graph={Error}",
                        conversation.Id, psid, i + 1, chunks.Count, detail);

                    if (error != null && error.ShouldStopRetrying)
                    {
                        // 551 不可投递 / 190 令牌失效 / 2635 PSID 不匹配：不再尝试后续分片与附件
                        _logger.LogWarning("Messenger 错误 {Code} 不应继续自动重试，中止本次发送：ConversationId={ConvId}", error.Code, conversation.Id);
                        break;
                    }
                }
                else
                {
                    sentMessageId = sentMessageId ?? mid;
                    chunkCount++;
                }
            }

            if (chunkCount == 0 && sendErrors.Count > 0)
            {
                return new MessengerReplyResult { Sent = false, ErrorMessage = string.Join("; ", sendErrors) };
            }
        }

        // 发送附件（Attachment Upload API → attachment_id → attachment 消息）
        var mediaErrors = new List<string>();
        if (request.AttachmentIds != null && request.AttachmentIds.Count > 0)
        {
            _logger.LogInformation("Messenger 开始发送 {Count} 个附件：ConversationId={ConvId}", request.AttachmentIds.Count, conversation.Id);
            var attachmentBasePath = AttachmentPathResolver.ResolveBasePath(
                _configuration["Attachment:UploadPath"], _env.ContentRootPath);
            var allAtts = await _unitOfWork.Attachments.GetByIdsAsync(request.AttachmentIds);
            var attMap = allAtts.ToDictionary(a => a.Id);

            foreach (var attachmentId in request.AttachmentIds)
            {
                if (!attMap.TryGetValue(attachmentId, out var attachment))
                {
                    _logger.LogWarning("Messenger 附件不存在：AttachmentId={AttachId}", attachmentId);
                    mediaErrors.Add($"附件 {attachmentId} 不存在");
                    continue;
                }

                // 路径遍历防护
                var cleanFileDir = (attachment.FileDir ?? string.Empty).Trim().Replace('\\', '/');
                var cleanStoredName = Path.GetFileName(attachment.StoredFileName ?? attachment.FileName) ?? string.Empty;
                var fullPath = Path.GetFullPath(Path.Combine(attachmentBasePath, cleanFileDir, cleanStoredName));
                var normalizedBase = Path.GetFullPath(attachmentBasePath);
                if (!fullPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Messenger 附件路径遍历拦截：FileDir={FileDir}", attachment.FileDir);
                    mediaErrors.Add($"附件 {attachment.FileName} 路径无效");
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    _logger.LogWarning("Messenger 附件文件不存在：AttachmentId={AttachId}", attachmentId);
                    mediaErrors.Add($"附件 {attachment.FileName} 文件不存在");
                    continue;
                }

                var data = await File.ReadAllBytesAsync(fullPath);
                _logger.LogDebug("Messenger 附件读取成功：FileName={FileName}, Size={Size}, MimeType={MimeType}",
                    attachment.FileName, data.Length, attachment.MimeType);

                var attachmentSpecId = await _messengerClient.UploadAttachmentAsync(data, attachment.MimeType ?? "application/octet-stream");
                if (string.IsNullOrEmpty(attachmentSpecId))
                {
                    var uploadError = _messengerClient.LastError;
                    await PersistErrorDiagnosticsAsync(uploadError);
                    _logger.LogError("Messenger 附件上传失败：FileName={FileName}, Size={Size}, Graph={Error}",
                        attachment.FileName, data.Length, FormatError(uploadError, "上传失败"));
                    mediaErrors.Add($"附件 {attachment.FileName} 上传失败");
                    continue;
                }

                var spec = new MessengerAttachmentSpec
                {
                    Kind = "id",
                    Type = MapAttachmentType(attachment.Type),
                    Payload = attachmentSpecId,
                    IsPublic = false
                };

                var mediaMid = await _messengerClient.SendAttachmentAsync(psid, spec, messageTag);
                if (!string.IsNullOrEmpty(mediaMid))
                {
                    sentMessageId = sentMessageId ?? mediaMid;
                    chunkCount++;
                    _logger.LogInformation("Messenger 附件发送成功：FileName={FileName}, Type={Type}, MessageId={MsgId}",
                        attachment.FileName, spec.Type, mediaMid);
                }
                else
                {
                    var sendError = _messengerClient.LastError;
                    await PersistErrorDiagnosticsAsync(sendError);
                    _logger.LogError("Messenger 附件发送失败：FileName={FileName}, Type={Type}, Graph={Error}",
                        attachment.FileName, spec.Type, FormatError(sendError, "发送失败"));
                    mediaErrors.Add($"附件 {attachment.FileName} 发送失败");
                }
            }
        }

        // 事务创建回复线程
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var thread = new ConversationThread
            {
                ConversationId = conversation.Id,
                From = opt.PageId ?? string.Empty,
                To = psid,
                Subject = string.Empty,
                Body = BuildReplyHtmlBody(request, messageTag),
                MessageId = sentMessageId ?? string.Empty,
                SourceType = SourceType.Messenger,
                Type = ThreadType.Message,
                SourceVia = SourceVia.User,
                CreatedByUserId = userId,
                SendStatus = sendErrors.Count + mediaErrors.Count > 0 ? SendStatus.Failed : SendStatus.Sent,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Threads.AddAsync(thread);
            await _unitOfWork.SaveChangesAsync();

            // 关联附件：批量追踪查询后在内存中回填 ThreadId
            if (request.AttachmentIds != null && request.AttachmentIds.Count > 0)
            {
                var trackedAtts = await _unitOfWork.Attachments.GetByIdsTrackedAsync(request.AttachmentIds);
                foreach (var attachment in trackedAtts)
                {
                    attachment.ThreadId = thread.Id;
                }
                await _unitOfWork.SaveChangesAsync();
            }

            conversation.UpdatedAt = DateTime.UtcNow;
            conversation.LastReplyAt = DateTime.UtcNow;
            conversation.LastReplyFrom = SourceVia.User;
            await _unitOfWork.SaveChangesAsync();

            await transaction.CommitAsync();

            await _notificationPublisher.NotifyMailboxAsync(
                conversation.MailboxId.ToString(),
                "conversation_updated",
                new { conversationId = conversation.Id });

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

            var errors = sendErrors.Concat(mediaErrors).ToList();
            _logger.LogInformation("Messenger 回复完成：ConversationId={ConvId}, ThreadId={ThreadId}, MessageId={MsgId}, Chunks={Chunks}, Tagged={Tagged}, Errors={ErrorCount}",
                conversation.Id, thread.Id, sentMessageId, chunkCount, messageTag != null, errors.Count);

            return new MessengerReplyResult
            {
                Sent = true,
                ThreadCreated = true,
                ThreadId = thread.Id,
                MessageId = sentMessageId,
                ChunkCount = chunkCount,
                MessageTagApplied = messageTag != null,
                ErrorMessage = errors.Count > 0 ? string.Join("; ", errors) : null
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Messenger 回复线程创建失败：ConversationId={ConvId}, MessageId={MsgId}", request.ConversationId, sentMessageId);

            // 出站动作已经发生：线程落库失败不能把此前收集到的发送/附件错误（例如附件缺失/404）替换成
            // 「线程记录失败」这一条泛化文案，否则坐端只看到落库问题、丢失真正的投递失败原因。
            var lateErrors = sendErrors.Concat(mediaErrors).ToList();
            const string threadFailure = "消息已发送，但会话线程记录失败";
            return new MessengerReplyResult
            {
                Sent = true,
                ThreadCreated = false,
                MessageId = sentMessageId,
                ChunkCount = chunkCount,
                MessageTagApplied = messageTag != null,
                ErrorMessage = lateErrors.Count > 0
                    ? threadFailure + "：" + string.Join("; ", lateErrors)
                    : threadFailure
            };
        }
    }

    public async Task<bool> SendSenderActionAsync(int conversationId, string action)
    {
        var opt = _settingsProvider.Current;
        if (!opt.Enabled || !opt.IsConfigured)
        {
            _logger.LogDebug("Messenger sender_action 跳过：未启用或未配置");
            return false;
        }

        if (action is not ("typing_on" or "typing_off" or "mark_seen"))
        {
            _logger.LogWarning("Messenger sender_action 非法：Action={Action}", action);
            return false;
        }

        var threads = await _unitOfWork.Threads.FindAsync(
            t => t.ConversationId == conversationId
                && t.Type == ThreadType.Customer
                && t.State != ThreadState.Deleted);
        var psid = threads.OrderByDescending(t => t.CreatedAt).FirstOrDefault()?.From;
        if (string.IsNullOrEmpty(psid))
        {
            _logger.LogWarning("Messenger sender_action 无法确定 PSID：ConversationId={ConvId}", conversationId);
            return false;
        }

        try
        {
            return await _messengerClient.SendSenderActionAsync(psid, action);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Messenger sender_action 发送失败：ConversationId={ConvId}, Action={Action}", conversationId, action);
            return false;
        }
    }

    public async Task<MessengerSubscribeResult> SubscribeFieldsAsync(string? fields = null)
    {
        var opt = _settingsProvider.Current;
        if (!opt.IsConfigured)
        {
            return new MessengerSubscribeResult { Success = false, ErrorMessage = "Messenger 未配置（缺少 PageId / PageAccessToken / AppSecret / VerifyToken）" };
        }

        var targetFields = string.IsNullOrWhiteSpace(fields) ? opt.SubscribedFields : fields!;
        try
        {
            var ok = await _messengerClient.SubscribeFieldsAsync(targetFields);
            var error = _messengerClient.LastError;
            if (!ok)
            {
                await PersistErrorDiagnosticsAsync(error);
                return new MessengerSubscribeResult
                {
                    Success = false,
                    Fields = targetFields,
                    ErrorMessage = FormatError(error, "订阅失败（需 pages_manage_metadata 权限，并确认 Webhook 已配置且为公网 HTTPS）")
                };
            }

            _logger.LogInformation("Messenger 订阅字段成功：Fields={Fields}", targetFields);
            return new MessengerSubscribeResult { Success = true, Fields = targetFields };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Messenger 订阅字段异常");
            return new MessengerSubscribeResult { Success = false, Fields = targetFields, ErrorMessage = $"订阅异常：{ex.Message}" };
        }
    }

    public async Task<MessengerTestResult> TestConnectionAsync()
    {
        var opt = _settingsProvider.Current;
        if (!opt.IsConfigured)
        {
            return new MessengerTestResult
            {
                Success = false,
                ErrorMessage = "配置不完整，请先填写 Page ID、Page Access Token、App Secret 和 Verify Token"
            };
        }

        try
        {
            // 只探测 Graph API（GET /me?fields=id,name），不向任何 PSID 发送消息
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(opt.StatusCheckTimeoutSeconds));
            var reachable = await _messengerClient.TestConnectionAsync(cts.Token);
            var error = _messengerClient.LastError;

            if (!reachable)
            {
                await PersistErrorDiagnosticsAsync(error);
                return new MessengerTestResult
                {
                    Success = false,
                    PageId = opt.PageId,
                    ApiVersion = opt.ApiVersion,
                    Error = error,
                    ErrorMessage = error != null && error.Code == 190
                        ? "Page Access Token 已失效（Graph 190），需重新换发并粘贴到设置页（Messenger 无令牌刷新接口）"
                        : FormatError(error, "Graph API 不可达或令牌无效，请检查 Page Access Token、App Secret 与网络出网策略")
                };
            }

            _logger.LogInformation("Messenger 测试连接成功：PageId={PageId}, ApiVersion={ApiVersion}", opt.PageId, opt.ApiVersion);
            return new MessengerTestResult { Success = true, PageId = opt.PageId, ApiVersion = opt.ApiVersion };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Messenger 测试连接超时（>{Timeout}s）", opt.StatusCheckTimeoutSeconds);
            return new MessengerTestResult { Success = false, PageId = opt.PageId, ApiVersion = opt.ApiVersion, ErrorMessage = $"检测超时（>{opt.StatusCheckTimeoutSeconds}s），请检查服务器到 graph.facebook.com 的出网策略" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Messenger 测试连接异常");
            return new MessengerTestResult { Success = false, PageId = opt.PageId, ApiVersion = opt.ApiVersion, ErrorMessage = $"测试失败：{ex.Message}" };
        }
    }

    // ---------------------------------------------------------------- 窗口与 message_tag

    /// <summary>哨兵值：表示按策略应拒绝发送（而非"不带 tag"）</summary>
    private const string RejectTag = "\u0000reject";

    private async Task<string> CheckWindowAsync(int conversationId, int windowHours, int warningThresholdHours)
    {
        var threads = await _unitOfWork.Threads.FindAsync(
            t => t.ConversationId == conversationId
                && t.Type == ThreadType.Customer
                && t.State != ThreadState.Deleted);
        var lastCustomerThread = threads.OrderByDescending(t => t.CreatedAt).FirstOrDefault();
        if (lastCustomerThread == null)
            return "expired";

        var remaining = lastCustomerThread.CreatedAt.AddHours(windowHours) - DateTime.UtcNow;

        if (remaining <= TimeSpan.Zero) return "expired";
        if (remaining <= TimeSpan.FromHours(warningThresholdHours)) return "warning";
        return "ok";
    }

    /// <summary>
    /// 依窗口状态与 MessageTagPolicy 决定本次发送使用的 message_tag。
    /// auto：窗口外带 tag（客服场景 HUMAN_AGENT），窗口内不带；
    /// none：窗口外拒绝发送并提示坐席；
    /// always：任何情况都带 tag。
    /// </summary>
    private static string? ResolveMessageTag(string window, MessengerOptions opt, out string? rejectReason)
    {
        rejectReason = null;
        var tag = string.IsNullOrWhiteSpace(opt.MessageTag) ? "HUMAN_AGENT" : opt.MessageTag;

        switch ((opt.MessageTagPolicy ?? "auto").ToLowerInvariant())
        {
            case "none":
                if (window == "expired")
                {
                    rejectReason = $"{opt.WindowHours} 小时客服窗口已过期，且策略为不带 message_tag（MessageTagPolicy=none），无法主动回复；请等客户再次发言或改用其他渠道";
                    return RejectTag;
                }
                return null;

            case "always":
                return tag;

            case "auto":
            default:
                return window == "expired" ? tag : null;
        }
    }

    /// <summary>
    /// 613（限流）按 RateLimitBackoffTries 指数退避重试；其余错误直接返回失败（LastError 由客户端保留）。
    /// 同一 PSID 的分片串行发送，不并发。
    /// </summary>
    private async Task<string?> SendTextWithBackoffAsync(string psid, string text, string? messageTag, string? replyToMid)
    {
        var opt = _settingsProvider.Current;
        var maxTries = Math.Max(0, opt.RateLimitBackoffTries);

        for (var attempt = 0; attempt <= maxTries; attempt++)
        {
            var mid = await _messengerClient.SendTextMessageAsync(psid, text, messageTag, replyToMid);
            if (!string.IsNullOrEmpty(mid))
                return mid;

            var error = _messengerClient.LastError;
            if (error == null || !error.IsRateLimited)
                return null;

            if (attempt >= maxTries)
            {
                _logger.LogWarning("Messenger 限流（613）重试已用尽：PsId={PsId}, Tries={Tries}", psid, maxTries);
                return null;
            }

            var delayMs = Math.Min(2000, 500 * (1 << attempt));
            _logger.LogInformation("Messenger 命中 613 限流，退避 {Delay}ms 后重试（第 {Attempt}/{Max} 次）：PsId={PsId}",
                delayMs, attempt + 1, maxTries, psid);
            await Task.Delay(delayMs);
        }

        return null;
    }

    private static string MapAttachmentType(AttachmentType type)
    {
        return type switch
        {
            AttachmentType.Image => "image",
            AttachmentType.Audio => "audio",
            AttachmentType.Video => "video",
            // Graph Send API 只接受 image / audio / video / file 四种 attachment type，
            // 旧实现兜底写的是 "files"（复数）⇒ 文档/PDF 等文件类附件被 Graph 以 400 拒绝，
            // 坐席上传的“文件”永远发不出去。
            _ => "file"
        };
    }

    private static string FormatError(MessengerApiError? error, string fallback)
    {
        if (error == null)
            return fallback;

        var suffix = string.IsNullOrEmpty(error.FbtraceId) ? string.Empty : $", fbtrace_id={error.FbtraceId}";
        return $"{fallback}（Graph code={error.Code}, subcode={error.Subcode}, http={error.HttpStatus}{suffix}）: {error.Message}";
    }

    /// <summary>
    /// 把 Graph 错误诊断（code / fbtrace_id / 190 令牌失效时间）写入 SystemOptions，供状态页与运维报障。
    /// 不保存令牌或完整响应体。失败仅记日志。
    /// </summary>
    private async Task PersistErrorDiagnosticsAsync(MessengerApiError? error)
    {
        if (error == null || error.Code == 0)
            return;

        try
        {
            await SaveOptionAsync(MessengerOptionKeys.LastErrorCode, error.Code.ToString());
            await SaveOptionAsync(MessengerOptionKeys.LastErrorFbtraceId, error.FbtraceId ?? string.Empty);

            if (error.Code == 190)
            {
                await SaveOptionAsync(MessengerOptionKeys.LastTokenInvalidAt, DateTime.UtcNow.ToString("O"));
                _logger.LogError("Messenger Page Access Token 失效（Graph 190），需人工换发：FbtraceId={Fbtrace}", error.FbtraceId);
            }

            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Messenger 错误诊断写入 SystemOptions 失败：Code={Code}", error.Code);
        }
    }

    // ---------------------------------------------------------------- 文本分片

    /// <summary>
    /// 按 Graph 文本上限（MessengerOptions.TextChunkChars，默认 2000）分片。
    /// 优先在换行处断开，其次空格；不在代理对（表情）中间硬切。
    /// 分片边界只做「回退」而不做「吞并」：分隔符（换行/空格）归属前一片，因此拼接所有分片可逐字符还原输入。
    /// （与 LineClient.SplitText 同一口径：Graph 不会替调用方去掉分隔符，边界处删字符等于丢内容。）
    /// </summary>
    internal static List<string> SplitText(string text, int maxChars)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text))
            return result;

        // 下界 200 保证不切得过碎；上界钳到 Graph 的 2000 —— 配置来自数据库，
        // 即使被写成 3000（旧 Range 允许到 4000）也不能让分片超过平台上限。
        var max = maxChars < 200 ? 200 : maxChars > 2000 ? 2000 : maxChars;

        var start = 0;
        while (start < text.Length)
        {
            var remaining = text.Length - start;
            if (remaining <= max)
            {
                result.Add(text[start..]);
                break;
            }

            var end = start + max;

            // 避免把 UTF-16 代理对切两半（表情/部分 CJK 扩展字符）
            if (char.IsHighSurrogate(text[end - 1]))
                end--;

            // 优先在换行处断开（回退最多 25% 的窗口），其次空格
            var newline = text.LastIndexOf('\n', end - 1, Math.Min(end - start, Math.Max(1, max / 4)));
            if (newline > start)
            {
                end = newline + 1;
            }
            else
            {
                var space = text.LastIndexOf(' ', end - 1, Math.Min(end - start, Math.Max(1, max / 4)));
                if (space > start)
                    end = space + 1;
            }

            result.Add(text[start..end]);
            start = end;
        }

        return result;
    }

    // ---------------------------------------------------------------- 持久化辅助

    private async Task SaveOptionAsync(string key, string value)
    {
        // 必须使用 tracking 查询！AsNoTracking 返回 Detached 实体，改属性后 ChangeTracker 检测不到变更，
        // SaveChangesAsync 不会生成 UPDATE 语句。
        var option = await _unitOfWork.SystemOptions.GetByNameTrackedAsync(key);
        if (option != null)
        {
            option.Value = value;
            option.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            await _unitOfWork.SystemOptions.AddAsync(new SystemOption
            {
                Name = key,
                Value = value,
                Category = MessengerOptionKeys.Category,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
    }

    private string BuildCallbackUrl()
    {
        var baseUrl = _configuration["App:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("缺少配置项 App:BaseUrl，请通过环境变量或 appsettings.json 配置生产环境域名");
        }
        var callbackPath = _settingsProvider.Current.CallbackPath ?? "/api/messenger";
        return $"{baseUrl.TrimEnd('/')}{callbackPath}";
    }

    private static string BuildReplyHtmlBody(MessengerReplyRequest request, string? messageTag)
    {
        var content = string.IsNullOrEmpty(request.Content)
            ? string.Empty
            : $"<div class=\"messenger-text\">{System.Web.HttpUtility.HtmlEncode(request.Content).Replace("\n", "<br>")}</div>";

        if (request.AttachmentIds != null && request.AttachmentIds.Count > 0)
        {
            content += $"<div class=\"messenger-media\">📎 附件 ×{request.AttachmentIds.Count}</div>";
        }

        if (!string.IsNullOrEmpty(messageTag))
        {
            content += $"<div class=\"messenger-tag\">🏷️ message_tag: {System.Web.HttpUtility.HtmlEncode(messageTag)}</div>";
        }

        return content;
    }
}
