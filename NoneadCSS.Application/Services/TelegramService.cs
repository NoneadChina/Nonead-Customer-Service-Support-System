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
/// Telegram 管理服务实现。
/// 与 WhatsAppService 同构（DB 覆盖 + ENC: 加密 + 不回显密钥），差异点：
///   1. 无 24 小时回复窗口：任何时间都可回复，因此不检查窗口；
///   2. 403（用户拉黑 / bot 被移出群）与 400（chat not found / bot can't initiate conversation）
///      为终态：不重试，改为把会话标记为不可达（见 <see cref="TelegramUnreachableState"/>）；
///   3. 429/420 带 retry_after：短则本地等待，长则返回让消息队列延后重试（见 MaxLocalBackoffSeconds）；
///   4. 文本超过 MaxMessageChars（官方 4096）自动分片发送，每段之间受速率限制器约束。
/// </summary>
public class TelegramService : ITelegramService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEncryptionService _encryptionService;
    private readonly ITelegramSettingsProvider _settingsProvider;
    private readonly ITelegramClient _telegramClient;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<TelegramService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _env;
    private readonly ITelegramPollingConnectionStatus _pollingStatus;
    private readonly TelegramThrottle _throttle;
    private readonly IWebhookService? _webhookService;

    public TelegramService(
        IUnitOfWork unitOfWork,
        IEncryptionService encryptionService,
        ITelegramSettingsProvider settingsProvider,
        ITelegramClient telegramClient,
        ICurrentUserAccessor currentUserAccessor,
        INotificationPublisher notificationPublisher,
        ILogger<TelegramService> logger,
        IConfiguration configuration,
        IHostEnvironment env,
        ITelegramPollingConnectionStatus pollingStatus,
        TelegramThrottle throttle,
        IWebhookService? webhookService = null)
    {
        _unitOfWork = unitOfWork;
        _encryptionService = encryptionService;
        _settingsProvider = settingsProvider;
        _telegramClient = telegramClient;
        _currentUserAccessor = currentUserAccessor;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
        _configuration = configuration;
        _env = env;
        _pollingStatus = pollingStatus;
        _throttle = throttle;
        _webhookService = webhookService;
    }

    /// <inheritdoc />
    public async Task<TelegramSettingsDto> GetSettingsAsync()
    {
        var options = await _settingsProvider.GetCurrentAsync();
        return MapSettings(options);
    }

    /// <inheritdoc />
    public async Task<TelegramSettingsDto> UpdateSettingsAsync(UpdateTelegramSettingsRequest request)
    {
        var previousMode = _settingsProvider.Current.Mode;

        // ===== 敏感字段：显式清除优先，否则非空才覆盖；统一 ENC: 加密，任何接口都不回显原值 =====
        // 审计 #5：原实现只有"非空才写"，前端把输入框清空提交后库里的旧密文原封不动 ⇒ 泄露的密钥撤不掉。
        // 清除走 ClearOptionAsync（删除 telegram.* 行）：TelegramSettingsProvider 只在库里取到非空值时才覆盖基线，
        // 写空串既会被 SystemOptionRepository 的空串缓存归一成 null，也无法压掉 appsettings/环境变量里的基线，
        // 因此"经 TELEGRAM__BotToken 之类环境变量注入的基线凭据"只能在部署侧撤销，本接口只撤销库里的覆盖值。
        // 清空后的安全分支：
        //   · BotToken → options.IsConfigured=false，SyncInboundModeAsync / SetupWebhookAsync 直接早退，出站发送返回"配置不完整"；
        //   · WebhookSecretToken → TelegramController.ReceiveWebhook 在读取请求体之前就 Unauthorized（fail-closed，等价于关闭端点）；
        //     若此时仍是启用的 webhook 模式，下面的 SyncInboundModeAsync 会经 SetupWebhookAsync 重新生成一把新密钥（即轮换），
        //     两种结果都不会让旧密钥继续可用；
        //   · LinkTokenHmacKey → HasLinkTokenHmacKey 变 false（深链绑定视为未配置）。
        if (request.ClearBotToken)
        {
            await ClearOptionAsync(TelegramOptionKeys.BotToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.BotToken))
        {
            await SaveOptionAsync(TelegramOptionKeys.BotToken, "ENC:" + _encryptionService.Encrypt(request.BotToken.Trim()));
        }

        if (request.ClearWebhookSecretToken)
        {
            await ClearOptionAsync(TelegramOptionKeys.WebhookSecretToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.WebhookSecretToken))
        {
            await SaveOptionAsync(TelegramOptionKeys.WebhookSecretToken, "ENC:" + _encryptionService.Encrypt(request.WebhookSecretToken.Trim()));
        }

        if (request.ClearLinkTokenHmacKey)
        {
            await ClearOptionAsync(TelegramOptionKeys.LinkTokenHmacKey);
        }
        else if (!string.IsNullOrWhiteSpace(request.LinkTokenHmacKey))
        {
            await SaveOptionAsync(TelegramOptionKeys.LinkTokenHmacKey, "ENC:" + _encryptionService.Encrypt(request.LinkTokenHmacKey.Trim()));
        }

        // ===== 非敏感字段 =====
        if (request.Enabled.HasValue)
        {
            await SaveOptionAsync(TelegramOptionKeys.Enabled, request.Enabled.Value.ToString().ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(request.Mode))
        {
            await SaveOptionAsync(TelegramOptionKeys.Mode, request.Mode.Trim().ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(request.ApiBaseUrl))
        {
            await SaveOptionAsync(TelegramOptionKeys.ApiBaseUrl, request.ApiBaseUrl.Trim().TrimEnd('/'));
        }

        if (request.UseLocalBotApiServer.HasValue)
        {
            await SaveOptionAsync(TelegramOptionKeys.UseLocalBotApiServer, request.UseLocalBotApiServer.Value.ToString().ToLowerInvariant());
        }

        if (request.WebhookUrl != null)
        {
            await SaveOptionAsync(TelegramOptionKeys.WebhookUrl, request.WebhookUrl.Trim());
        }

        if (request.WebhookIpAddress != null)
        {
            await SaveOptionAsync(TelegramOptionKeys.WebhookIpAddress, request.WebhookIpAddress.Trim());
        }

        if (request.MaxWebhookConnections.HasValue)
        {
            await SaveOptionAsync(TelegramOptionKeys.MaxWebhookConnections, request.MaxWebhookConnections.Value.ToString());
        }

        if (request.AllowedUpdates != null)
        {
            await SaveOptionAsync(TelegramOptionKeys.AllowedUpdates, string.Join(",", request.AllowedUpdates.Select(u => u.Trim()).Where(u => u.Length > 0)));
        }

        if (request.PollingTimeout.HasValue)
        {
            await SaveOptionAsync(TelegramOptionKeys.PollingTimeout, request.PollingTimeout.Value.ToString());
        }

        if (request.PollingLimit.HasValue)
        {
            await SaveOptionAsync(TelegramOptionKeys.PollingLimit, request.PollingLimit.Value.ToString());
        }

        if (request.DeleteWebhookOnSwitch.HasValue)
        {
            await SaveOptionAsync(TelegramOptionKeys.DeleteWebhookOnSwitch, request.DeleteWebhookOnSwitch.Value.ToString().ToLowerInvariant());
        }

        if (request.BotUsername != null)
        {
            await SaveOptionAsync(TelegramOptionKeys.BotUsername, request.BotUsername.Trim().TrimStart('@'));
        }

        if (request.AcceptGroupMessages.HasValue)
        {
            await SaveOptionAsync(TelegramOptionKeys.AcceptGroupMessages, request.AcceptGroupMessages.Value.ToString().ToLowerInvariant());
        }

        if (request.MailboxId.HasValue)
        {
            await SaveOptionAsync(TelegramOptionKeys.MailboxId, request.MailboxId.Value.ToString());
        }

        if (request.AutoReplyEnabled.HasValue)
        {
            await SaveOptionAsync(TelegramOptionKeys.AutoReplyEnabled, request.AutoReplyEnabled.Value.ToString().ToLowerInvariant());
        }

        if (request.AutoReplyMessage != null)
        {
            await SaveOptionAsync(TelegramOptionKeys.AutoReplyMessage, request.AutoReplyMessage);
        }

        if (request.ConversationMatchWindowHours.HasValue)
        {
            await SaveOptionAsync(TelegramOptionKeys.ConversationMatchWindowHours, request.ConversationMatchWindowHours.Value.ToString());
        }

        if (request.MaxMessageChars.HasValue)
        {
            await SaveOptionAsync(TelegramOptionKeys.MaxMessageChars, request.MaxMessageChars.Value.ToString());
        }

        if (request.RateLimitGlobalPerSeconds.HasValue)
        {
            await SaveOptionAsync(TelegramOptionKeys.RateLimitGlobalPerSeconds, request.RateLimitGlobalPerSeconds.Value.ToString());
        }

        if (request.RateLimitPerChatPerSeconds.HasValue)
        {
            await SaveOptionAsync(TelegramOptionKeys.RateLimitPerChatPerSeconds, request.RateLimitPerChatPerSeconds.Value.ToString());
        }

        if (request.LinkTokenTtlMinutes.HasValue)
        {
            await SaveOptionAsync(TelegramOptionKeys.LinkTokenTtlMinutes, request.LinkTokenTtlMinutes.Value.ToString());
        }

        await _unitOfWork.SaveChangesAsync();
        await _settingsProvider.RefreshAsync();

        var options = _settingsProvider.Current;
        _throttle.Configure(options.RateLimitGlobalPerSeconds, options.RateLimitPerChatPerSeconds);

        // 模式切换：webhook → polling 前必须 deleteWebhook，否则 getUpdates 返回 409
        await SyncInboundModeAsync(previousMode, options);

        return MapSettings(options);
    }

    /// <inheritdoc />
    public async Task<TelegramStatusDto> GetStatusAsync()
    {
        var options = await _settingsProvider.GetCurrentAsync();
        var status = new TelegramStatusDto
        {
            Configured = options.IsConfigured,
            Enabled = options.Enabled,
            Mode = options.Mode,
            CallbackUrl = TryBuildCallbackUrl(options)
        };

        if (options.IsConfigured)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.StatusCheckTimeoutSeconds));
                var bot = await _telegramClient.GetMeAsync(cts.Token);
                if (bot != null)
                {
                    status.ApiReachable = true;
                    status.BotUsername = bot.Username ?? options.BotUsername;
                    status.CanReadAllGroupMessages = bot.CanReadAllGroupMessages;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Telegram getMe 探测超时（{Seconds}s）", options.StatusCheckTimeoutSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram getMe 探测失败");
            }

            if (options.IsWebhookMode)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.StatusCheckTimeoutSeconds));
                    var info = await _telegramClient.GetWebhookInfoAsync(cts.Token);
                    if (info != null)
                    {
                        status.WebhookRegistered = !string.IsNullOrEmpty(info.Url);
                        status.WebhookUrl = info.Url;
                        status.PendingUpdateCount = info.PendingUpdateCount;
                        status.LastErrorMessage = info.LastErrorMessage;
                        status.LastErrorTime = info.LastErrorTime;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Telegram getWebhookInfo 探测失败");
                }
            }
            else
            {
                status.PollingRunning = _pollingStatus.IsRunning;
                status.PollingLastActivityAt = _pollingStatus.LastActivityAt;
                status.PollingLastErrorMessage = _pollingStatus.LastError;
                status.PollingOffset = await GetPollingOffsetValueAsync();
            }
        }

        try
        {
            status.UnreachableConversationCount = await TelegramUnreachableState.CountMarkedAsync(_unitOfWork);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "统计 Telegram 不可达会话数失败");
        }

        return status;
    }

    /// <inheritdoc />
    public async Task<TelegramReplyResult> ReplyAsync(TelegramReplyRequest request, int userId)
    {
        var options = await _settingsProvider.GetCurrentAsync();

        if (!options.Enabled || !options.IsConfigured)
        {
            return new TelegramReplyResult { Sent = false, ErrorMessage = "Telegram 未启用或未配置" };
        }

        if (string.IsNullOrWhiteSpace(request.Content) && (request.AttachmentIds == null || request.AttachmentIds.Count == 0))
        {
            return new TelegramReplyResult { Sent = false, ErrorMessage = "回复内容为空" };
        }

        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(request.ConversationId);
        if (conversation == null)
        {
            return new TelegramReplyResult { Sent = false, ErrorMessage = "会话不存在" };
        }

        var chatId = await ResolveChatIdAsync(conversation);
        if (string.IsNullOrEmpty(chatId))
        {
            return new TelegramReplyResult { Sent = false, ErrorMessage = "无法解析该会话的 Telegram chat_id" };
        }

        var lastCustomerThread = (await _unitOfWork.Threads.FindAsync(t =>
                t.ConversationId == conversation.Id
                && t.Type == ThreadType.Customer
                && t.State != ThreadState.Deleted))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefault();

        // 入站线程的 MessageId 存的是去重键 "chat.id:message_id"，reply_parameters 需要纯 message_id
        var replyToMessageId = request.QuoteLastCustomerMessage ? ExtractPlatformMessageId(lastCustomerThread?.MessageId) : null;

        // 论坛群（forum topic）里的回复必须带 message_thread_id 才会落在同一个话题下，否则会跑到 General。
        // ConversationThread 没有独立的 topic 列，入站时话题 id 只写进了 Body 的 telegram-topic 标记，
        // 因此从最后一条客户消息的 Body 里还原；取不到就传 null，发送侧会省略该字段（绝不传 0）。
        var messageThreadId = TelegramOutboundSender.ExtractForumTopicId(lastCustomerThread?.Body);

        // 坐席显式选择的格式（HTML/Markdown/MarkdownV2）；纯文本/未知值一律 null ⇒ 不写 parse_mode。
        // 正文不做转义：操作员主动选了标记模式，应按其原样交给 Telegram 解析。
        var parseMode = NormalizeParseMode(request.ParseMode);

        _throttle.Configure(options.RateLimitGlobalPerSeconds, options.RateLimitPerChatPerSeconds);

        var sentMessageIds = new List<string>();
        var mediaErrors = new List<string>();
        var partsSent = 0;

        // ===== 文本：超长自动分片（官方硬限制 4096 字符） =====
        if (!string.IsNullOrWhiteSpace(request.Content))
        {
            var parts = TelegramWebhookParser.SplitLongMessage(request.Content.Trim(), options.MaxMessageChars);
            for (var i = 0; i < parts.Count; i++)
            {
                // 只有首段引用客户原消息，后续段保持普通消息，避免刷屏式引用
                var sendResult = await SendTextWithThrottleAsync(chatId, parts[i], i == 0 ? replyToMessageId : null, options, messageThreadId, parseMode);
                if (!sendResult.Success)
                {
                    var failure = await HandleSendFailureAsync(sendResult, conversation.Id, sentMessageIds, partsSent, isMedia: false);
                    if (failure != null)
                    {
                        return failure;
                    }

                    continue;
                }

                partsSent++;
                if (!string.IsNullOrEmpty(sendResult.MessageId))
                {
                    sentMessageIds.Add(sendResult.MessageId!);
                }
            }
        }

        // ===== 附件：读取站内文件并以 multipart 上传（自建服务器可用本地路径） =====
        var attachmentCount = 0;
        if (request.AttachmentIds is { Count: > 0 })
        {
            var basePath = AttachmentPathResolver.ResolveBasePath(_configuration["Attachment:UploadPath"], _env.ContentRootPath);
            var attachments = await _unitOfWork.Attachments.GetByIdsAsync(request.AttachmentIds);

            foreach (var attachmentId in request.AttachmentIds)
            {
                var attachment = attachments.FirstOrDefault(a => a.Id == attachmentId);
                if (attachment == null)
                {
                    mediaErrors.Add($"附件 {attachmentId} 不存在");
                    continue;
                }

                var fullPath = Path.GetFullPath(Path.Combine(basePath, attachment.FileDir, attachment.StoredFileName));
                var normalizedBase = Path.GetFullPath(basePath) + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                {
                    mediaErrors.Add($"附件 {attachmentId} 路径非法");
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    mediaErrors.Add($"附件 {attachment.FileName} 文件缺失");
                    continue;
                }

                var mediaType = attachment.Type switch
                {
                    AttachmentType.Image => "photo",
                    AttachmentType.Audio => "audio",
                    AttachmentType.Video => "video",
                    _ => "document"
                };

                var bytes = await File.ReadAllBytesAsync(fullPath);
                var uploadResult = await _telegramClient.UploadAndSendMediaAsync(
                    chatId, mediaType, bytes, attachment.FileName, attachment.MimeType);

                if (!uploadResult.Success)
                {
                    var failure = await HandleSendFailureAsync(uploadResult, conversation.Id, sentMessageIds, partsSent, isMedia: true);
                    if (failure != null)
                    {
                        return failure;
                    }

                    mediaErrors.Add(uploadResult.ErrorMessage ?? "媒体发送失败");
                    continue;
                }

                partsSent++;
                attachmentCount++;
                if (!string.IsNullOrEmpty(uploadResult.MessageId))
                {
                    sentMessageIds.Add(uploadResult.MessageId!);
                }
            }
        }

        if (partsSent == 0)
        {
            return new TelegramReplyResult
            {
                Sent = false,
                PartsSent = 0,
                ErrorMessage = mediaErrors.Count > 0 ? string.Join("；", mediaErrors) : "没有可发送的内容"
            };
        }

        // ===== 站内记录出站线程（Telegram 无窗口限制，失败时也不写"草稿"） =====
        try
        {
            await using var transaction = await _unitOfWork.BeginTransactionAsync();

            var thread = new ConversationThread
            {
                ConversationId = conversation.Id,
                From = options.BotUsername ?? conversation.MailboxId.ToString(),
                To = chatId,
                Subject = string.Empty,
                Body = BuildReplyHtmlBody(request, partsSent),
                MessageId = sentMessageIds.Count > 0 ? sentMessageIds[0] : $"telegram-out-{Guid.NewGuid():N}",
                SourceType = SourceType.Telegram,
                Type = ThreadType.Message,
                SourceVia = SourceVia.User,
                CreatedByUserId = userId,
                SendStatus = SendStatus.Sent,
                HasAttachments = attachmentCount > 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Threads.AddAsync(thread);
            await _unitOfWork.SaveChangesAsync();

            if (attachmentCount > 0)
            {
                var tracked = await _unitOfWork.Attachments.GetByIdsTrackedAsync(request.AttachmentIds!);
                foreach (var attachment in tracked)
                {
                    attachment.ThreadId = thread.Id;
                }

                await _unitOfWork.SaveChangesAsync();
            }

            // 回复成功即代表该 chat 可投递（此前可能被标记不可达）
            await TelegramUnreachableState.ClearAsync(_unitOfWork, conversation.Id);

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
                    await _webhookService.TriggerAsync(
                        "conversation_updated",
                        conversation.MailboxId,
                        new { conversationId = conversation.Id },
                        "Conversation",
                        conversation.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Telegram 回复触发 Webhook 失败（会话 {ConversationId}）", conversation.Id);
                }
            }

            return new TelegramReplyResult
            {
                Sent = true,
                ThreadCreated = true,
                ThreadId = thread.Id,
                MessageId = sentMessageIds.Count > 0 ? sentMessageIds[0] : null,
                PartsSent = partsSent,
                ErrorMessage = mediaErrors.Count > 0 ? string.Join("；", mediaErrors) : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram 回复写线程失败（会话 {ConversationId}，消息已发出）", conversation.Id);
            return new TelegramReplyResult
            {
                Sent = true,
                ThreadCreated = false,
                PartsSent = partsSent,
                MessageId = sentMessageIds.Count > 0 ? sentMessageIds[0] : null,
                ErrorMessage = "消息已发送，但会话线程记录失败"
            };
        }
    }

    /// <inheritdoc />
    public async Task<TelegramTestResult> SetupWebhookAsync(TelegramWebhookSetupRequest? request)
    {
        var options = await _settingsProvider.GetCurrentAsync();
        if (!options.IsConfigured)
        {
            return new TelegramTestResult { Success = false, ErrorMessage = "配置不完整，请先填写 BotToken" };
        }

        var url = !string.IsNullOrWhiteSpace(request?.WebhookUrl)
            ? request!.WebhookUrl!.Trim()
            : !string.IsNullOrWhiteSpace(options.WebhookUrl)
                ? options.WebhookUrl!
                : BuildCallbackUrl(options);

        // 未配置密钥时自动生成并加密落库：Webhook 无签名可验，secret_token 是唯一来源校验手段
        var secretToken = options.WebhookSecretToken;
        if (string.IsNullOrWhiteSpace(secretToken))
        {
            secretToken = GenerateSecretToken();
            await SaveOptionAsync(TelegramOptionKeys.WebhookSecretToken, "ENC:" + _encryptionService.Encrypt(secretToken));
            await _unitOfWork.SaveChangesAsync();
            await _settingsProvider.RefreshAsync();
            _logger.LogInformation("Telegram Webhook 密钥已自动生成并加密保存");
        }

        var ok = await _telegramClient.SetWebhookAsync(
            url,
            secretToken,
            options.MaxWebhookConnections,
            options.AllowedUpdates,
            request?.DropPendingUpdates ?? false,
            options.WebhookCertificate,
            options.WebhookIpAddress);

        return new TelegramTestResult
        {
            Success = ok,
            BotUsername = options.BotUsername,
            ErrorMessage = ok ? null : "setWebhook 失败，请检查 BotToken、回调地址可公网访问（自建服务器需 HTTPS）"
        };
    }

    /// <inheritdoc />
    public async Task<TelegramTestResult> RemoveWebhookAsync()
    {
        var options = await _settingsProvider.GetCurrentAsync();
        if (!options.IsConfigured)
        {
            return new TelegramTestResult { Success = false, ErrorMessage = "配置不完整，请先填写 BotToken" };
        }

        var ok = await _telegramClient.DeleteWebhookAsync(dropPendingUpdates: false);
        return new TelegramTestResult
        {
            Success = ok,
            BotUsername = options.BotUsername,
            ErrorMessage = ok ? null : "deleteWebhook 失败，请检查 BotToken 与网络"
        };
    }

    /// <inheritdoc />
    public async Task<bool> ClearUnreachableMarkAsync(int conversationId)
    {
        var cleared = await TelegramUnreachableState.ClearAsync(_unitOfWork, conversationId);
        if (cleared)
        {
            await _unitOfWork.SaveChangesAsync();
        }

        return cleared;
    }

    /// <inheritdoc />
    public async Task<TelegramTestResult> TestConnectionAsync()
    {
        var options = await _settingsProvider.GetCurrentAsync();
        if (!options.IsConfigured)
        {
            return new TelegramTestResult { Success = false, ErrorMessage = "配置不完整，请先填写 BotToken" };
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.StatusCheckTimeoutSeconds));
            var bot = await _telegramClient.GetMeAsync(cts.Token);
            if (bot == null)
            {
                return new TelegramTestResult { Success = false, ErrorMessage = "getMe 失败：请检查 BotToken 与 ApiBaseUrl" };
            }

            return new TelegramTestResult
            {
                Success = true,
                BotId = bot.Id,
                BotUsername = bot.Username
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram 连接测试失败");
            return new TelegramTestResult { Success = false, ErrorMessage = "getMe 异常：" + ex.Message };
        }
    }

    /// <summary>
    /// 受速率限制保护的文本发送：
    ///   1. 先按令牌桶计算等待时间，超过 MaxLocalBackoffSeconds 直接返回（交回消息队列延后重试，避免长时间占用 worker）；
    ///   2. 429/420 且 retry_after ≤ MaxLocalBackoffSeconds 时本地等待后重试一次（官方建议按 retry_after + 抖动退避）。
    /// 具体实现放在 <see cref="TelegramOutboundSender"/>：出站队列的 Telegram 分支也走同一段逻辑，
    /// 否则排队消息会绕过限速与错误分类（本次修复的出站旁路问题）。
    /// </summary>
    private Task<TelegramSendResult> SendTextWithThrottleAsync(
        string chatId,
        string text,
        string? replyToMessageId,
        TelegramOptions options,
        int? messageThreadId = null,
        string? parseMode = null)
        => TelegramOutboundSender.SendTextWithThrottleAsync(
            _telegramClient, _throttle, chatId, text, replyToMessageId, options, messageThreadId, parseMode);

    /// <summary>
    /// 归一化回复请求里的 parse_mode：trim + 大小写不敏感匹配 HTML / Markdown / MarkdownV2；
    /// null、空串、none 与任何无法识别的值都返回 null（纯文本，不写 parse_mode 字段），未知值额外记一条警告。
    /// 注意：不转义正文——坐席明确选择了标记模式，应按其原样交给 Telegram 解析。
    /// </summary>
    private string? NormalizeParseMode(string? parseMode)
    {
        if (string.IsNullOrWhiteSpace(parseMode))
        {
            return null;
        }

        var trimmed = parseMode.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (trimmed.Equals("HTML", StringComparison.OrdinalIgnoreCase))
        {
            return "HTML";
        }

        if (trimmed.Equals("Markdown", StringComparison.OrdinalIgnoreCase))
        {
            return "Markdown";
        }

        if (trimmed.Equals("MarkdownV2", StringComparison.OrdinalIgnoreCase))
        {
            return "MarkdownV2";
        }

        _logger.LogWarning("Telegram 回复的 parse_mode 无法识别，已按纯文本发送：{ParseMode}", parseMode);
        return null;
    }

    /// <summary>
    /// 出站失败分类。返回非 null 表示应立即中止本次回复并把该结果返回给调用方；
    /// 返回 null 表示该错误只影响当前附件，记录 mediaErrors 后继续处理其余附件。
    /// </summary>
    private async Task<TelegramReplyResult?> HandleSendFailureAsync(
        TelegramSendResult sendResult,
        int conversationId,
        List<string> sentMessageIds,
        int partsSent,
        bool isMedia)
    {
        // chat.id 迁移：改写 Customers.ChannelId，本次不重试（坐席重发即可）
        if (sendResult.MigrateToChatId.HasValue)
        {
            await ApplyChatMigrationAsync(conversationId, sendResult.MigrateToChatId.Value);
            return new TelegramReplyResult
            {
                Sent = sentMessageIds.Count > 0,
                PartsSent = partsSent,
                MessageId = sentMessageIds.Count > 0 ? sentMessageIds[0] : null,
                ErrorMessage = $"chat.id 已迁移为 {sendResult.MigrateToChatId.Value}，请重新发送本条消息"
            };
        }

        if (sendResult.Blocked || sendResult.Unreachable)
        {
            var reason = sendResult.Blocked
                ? "客户已拉黑机器人或 bot 已被移出群（403），已标记为不可达，停止重试"
                : sendResult.Description ?? "该 chat 不可投递（chat not found / bot 无法主动发起会话）";
            await TelegramUnreachableState.MarkAsync(_unitOfWork, conversationId, reason);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogWarning("Telegram 会话 {ConversationId} 标记为不可达：{Reason}", conversationId, reason);

            return new TelegramReplyResult
            {
                Sent = false,
                MarkedUnreachable = true,
                PartsSent = partsSent,
                MessageId = sentMessageIds.Count > 0 ? sentMessageIds[0] : null,
                ErrorMessage = reason
            };
        }

        if (sendResult.InvalidToken)
        {
            return new TelegramReplyResult
            {
                Sent = false,
                PartsSent = partsSent,
                ErrorMessage = "BotToken 无效（401），请在集成配置页重新填写"
            };
        }

        if (sendResult.Conflict)
        {
            return new TelegramReplyResult
            {
                Sent = false,
                PartsSent = partsSent,
                ErrorMessage = "入站模式冲突（409）：轮询前需先调用 deleteWebhook 删除 Webhook"
            };
        }

        if (sendResult.RetryAfterSeconds.HasValue)
        {
            var retryAfter = sendResult.RetryAfterSeconds.Value;
            var chatKey = long.TryParse(await ResolveChatIdByIdAsync(conversationId), out var numeric) ? numeric : 0;
            _throttle.RegisterRetryAfter(chatKey, retryAfter, chatScoped: sendResult.ErrorCode == 420);

            return new TelegramReplyResult
            {
                Sent = sentMessageIds.Count > 0,
                PartsSent = partsSent,
                MessageId = sentMessageIds.Count > 0 ? sentMessageIds[0] : null,
                RetryAfterSeconds = retryAfter,
                ErrorMessage = $"触发限流（{sendResult.ErrorCode}），{retryAfter} 秒后重试"
            };
        }

        // 400 参数类 / 5xx / 网络异常：不标记不可达。文本段失败直接返回，附件失败则继续处理其余附件
        if (!isMedia)
        {
            return new TelegramReplyResult
            {
                Sent = sentMessageIds.Count > 0,
                PartsSent = partsSent,
                MessageId = sentMessageIds.Count > 0 ? sentMessageIds[0] : null,
                ErrorMessage = sendResult.ErrorMessage ?? $"Telegram 发送失败（{sendResult.ErrorCode}）"
            };
        }

        return null;
    }

    /// <summary>切换 webhook / polling 时同步 Telegram 侧配置（409 冲突的唯一正确处理方式）</summary>
    private async Task SyncInboundModeAsync(string previousMode, TelegramOptions options)
    {
        if (!options.IsConfigured)
        {
            return;
        }

        try
        {
            if (string.Equals(previousMode, options.Mode, StringComparison.OrdinalIgnoreCase))
            {
                // 模式未变但启用了 webhook：仍尝试登记（幂等，便于配置修改后自动生效）
                if (options.IsWebhookMode && options.Enabled)
                {
                    await SetupWebhookAsync(null);
                }

                return;
            }

            if (options.IsPollingMode)
            {
                if (options.DeleteWebhookOnSwitch)
                {
                    var deleted = await _telegramClient.DeleteWebhookAsync(dropPendingUpdates: false);
                    _logger.LogInformation("Telegram 切换到 polling：deleteWebhook {Result}", deleted ? "成功" : "失败");
                }
            }
            else if (options.Enabled)
            {
                var setup = await SetupWebhookAsync(null);
                _logger.LogInformation("Telegram 切换到 webhook：setWebhook {Result} {Error}",
                    setup.Success ? "成功" : "失败", setup.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram 入站模式切换同步失败（配置已保存，可手动重试 setWebhook/deleteWebhook）");
        }
    }

    private async Task<string?> ResolveChatIdAsync(Conversation conversation)
    {
        // 优先取站内客户绑定（Customers.Channel="telegram", ChannelId=chat.id）
        if (conversation.CustomerId > 0)
        {
            var customers = await _unitOfWork.Customers.FindAsync(c => c.Id == conversation.CustomerId);
            var customer = customers.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(customer?.ChannelId))
            {
                return customer!.ChannelId;
            }
        }

        // 回退：取最后一条入站线程的 To（入站时写入的就是 chat.id）
        var threads = await _unitOfWork.Threads.FindAsync(t =>
            t.ConversationId == conversation.Id
            && t.Type == ThreadType.Customer
            && t.SourceType == SourceType.Telegram
            && t.State != ThreadState.Deleted);

        return threads.OrderByDescending(t => t.CreatedAt).FirstOrDefault()?.To;
    }

    private async Task<string?> ResolveChatIdByIdAsync(int conversationId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(conversationId);
        return conversation == null ? null : await ResolveChatIdAsync(conversation);
    }

    /// <summary>
    /// 从站内线程的 MessageId 取出 Telegram 平台 message_id。
    /// 入站线程存的是去重键 "chat.id:message_id"（见 TelegramWebhookParser.BuildDedupeKey），
    /// 而 reply_parameters 只接受纯 message_id；旧数据也可能直接存 message_id，因此两种形态都要能还原。
    /// </summary>
    private static string? ExtractPlatformMessageId(string? storedMessageId)
    {
        if (string.IsNullOrWhiteSpace(storedMessageId))
        {
            return null;
        }

        var candidate = storedMessageId.Contains(':') ? storedMessageId[(storedMessageId.LastIndexOf(':') + 1)..] : storedMessageId;
        return long.TryParse(candidate, out _) ? candidate : null;
    }

    private async Task ApplyChatMigrationAsync(int conversationId, long newChatId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(conversationId);
        if (conversation == null || conversation.CustomerId <= 0)
        {
            return;
        }

        var customers = await _unitOfWork.Customers.FindAsync(c => c.Id == conversation.CustomerId);
        foreach (var customer in customers)
        {
            customer.ChannelId = newChatId.ToString();
        }

        await _unitOfWork.SaveChangesAsync();
    }

    private async Task<long?> GetPollingOffsetValueAsync()
    {
        try
        {
            var raw = await _unitOfWork.SystemOptions.GetValueAsync(TelegramOptionKeys.PollingOffsetCursor);
            return long.TryParse(raw, out var offset) ? offset : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取 Telegram 轮询游标失败");
            return null;
        }
    }

    private TelegramSettingsDto MapSettings(TelegramOptions options)
    {
        return new TelegramSettingsDto
        {
            Enabled = options.Enabled,
            Mode = options.Mode,
            ApiBaseUrl = options.ApiBaseUrl,
            UseLocalBotApiServer = options.UseLocalBotApiServer,
            CallbackUrl = TryBuildCallbackUrl(options),
            BotUsername = options.BotUsername,
            AcceptGroupMessages = options.AcceptGroupMessages,
            DeleteWebhookOnSwitch = options.DeleteWebhookOnSwitch,
            MaxWebhookConnections = options.MaxWebhookConnections,
            PollingTimeout = options.PollingTimeout,
            PollingLimit = options.PollingLimit,
            AllowedUpdates = options.AllowedUpdates,
            MailboxId = options.MailboxId,
            AutoReplyEnabled = options.AutoReplyEnabled,
            AutoReplyMessage = options.AutoReplyMessage,
            ConversationMatchWindowHours = options.ConversationMatchWindowHours,
            MaxMessageChars = options.MaxMessageChars,
            RateLimitGlobalPerSeconds = options.RateLimitGlobalPerSeconds,
            RateLimitPerChatPerSeconds = options.RateLimitPerChatPerSeconds,
            DeepLinkPrefix = string.IsNullOrWhiteSpace(options.BotUsername)
                ? null
                : $"https://t.me/{options.BotUsername}",
            HasBotToken = !string.IsNullOrWhiteSpace(options.BotToken),
            HasWebhookSecretToken = !string.IsNullOrWhiteSpace(options.WebhookSecretToken),
            HasLinkTokenHmacKey = !string.IsNullOrWhiteSpace(options.LinkTokenHmacKey)
        };
    }

    private string? TryBuildCallbackUrl(TelegramOptions options)
    {
        try
        {
            return BuildCallbackUrl(options);
        }
        catch (InvalidOperationException)
        {
            // App:BaseUrl 未配置（开发环境常见）：不影响其它状态字段
            return null;
        }
    }

    private string BuildCallbackUrl(TelegramOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.WebhookUrl))
        {
            return options.WebhookUrl!;
        }

        var baseUrl = _configuration["App:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("缺少配置项 App:BaseUrl，请通过环境变量或 appsettings.json 配置生产环境域名");
        }

        var callbackPath = string.IsNullOrWhiteSpace(options.CallbackPath) ? "/api/telegram/webhook" : options.CallbackPath;
        return $"{baseUrl.TrimEnd('/')}{callbackPath}";
    }

    /// <summary>生成 Webhook 密钥（32 随机字节 ⇒ 43 位 base64url：URL 安全、无 `=` 填充歧义；官方 secret_token 上限 256）</summary>
    private static string GenerateSecretToken()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string BuildReplyHtmlBody(TelegramReplyRequest request, int partsSent)
    {
        var builder = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.Content))
        {
            var encoded = System.Web.HttpUtility.HtmlEncode(request.Content).Replace("\n", "<br>");
            builder.Append("<div class=\"telegram-text\">").Append(encoded).Append("</div>");
            if (partsSent > 1)
            {
                builder.Append("<div class=\"telegram-parts\">（已分 ").Append(partsSent).Append(" 段发送）</div>");
            }
        }

        if (request.AttachmentIds is { Count: > 0 })
        {
            builder.Append("<div class=\"telegram-media\">📎 附件 ×").Append(request.AttachmentIds.Count).Append("</div>");
        }

        return builder.ToString();
    }

    /// <summary>
    /// 保存单个 SystemOption。必须用 GetByNameTrackedAsync（AsNoTracking 的查询结果 Detached，
    /// 修改后不会发出 UPDATE），否则配置改动看似保存成功但数据库不变。
    /// </summary>
    private async Task SaveOptionAsync(string key, string value)
    {
        var existing = await _unitOfWork.SystemOptions.GetByNameTrackedAsync(key);
        if (existing != null)
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            await _unitOfWork.SystemOptions.AddAsync(new SystemOption
            {
                Name = key,
                Value = value,
                Category = TelegramOptionKeys.Category,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// 清除单个 SystemOption：删除整行，而不是把值写成空串（审计 #5）。
    /// 写空串不可靠——TelegramSettingsProvider 只接受非空库值作为覆盖，空串会退回 appsettings/环境变量基线；
    /// 而 SystemOptionRepository 的 30 秒缓存又把空串归一成 null，两条读取路径对空串的解释并不一致。
    /// 删行后 GetValueAsync 读不到记录 ⇒ 该敏感项在本库范围内被彻底撤销。
    /// GetByNameTrackedAsync 取实体时已移除该键缓存，这里再显式 InvalidateCache 兜底（外部批量写路径可能留了旧值）。
    /// </summary>
    private async Task ClearOptionAsync(string key)
    {
        var existing = await _unitOfWork.SystemOptions.GetByNameTrackedAsync(key);
        if (existing != null)
        {
            await _unitOfWork.SystemOptions.DeleteAsync(existing);
        }

        _unitOfWork.SystemOptions.InvalidateCache(key);
    }
}
