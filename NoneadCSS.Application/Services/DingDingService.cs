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
/// 钉钉管理服务 - 配置读写、连接状态查询、回复消息。
/// 复刻 WhatsAppService 模式，敏感字段 AES 加密存储。
/// </summary>
public class DingDingService : IDingDingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEncryptionService _encryptionService;
    private readonly IDingDingSettingsProvider _settingsProvider;
    private readonly IDingDingClient _dingDingClient;
    private readonly IDingTalkStreamConnectionStatus _streamConnectionStatus;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<DingDingService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _env;
    private readonly IWebhookService? _webhookService;

    public DingDingService(
        IUnitOfWork unitOfWork,
        IEncryptionService encryptionService,
        IDingDingSettingsProvider settingsProvider,
        IDingDingClient dingDingClient,
        IDingTalkStreamConnectionStatus streamConnectionStatus,
        ICurrentUserAccessor currentUserAccessor,
        INotificationPublisher notificationPublisher,
        ILogger<DingDingService> logger,
        IConfiguration configuration,
        IHostEnvironment env,
        IWebhookService? webhookService = null)
    {
        _unitOfWork = unitOfWork;
        _encryptionService = encryptionService;
        _settingsProvider = settingsProvider;
        _dingDingClient = dingDingClient;
        _streamConnectionStatus = streamConnectionStatus;
        _currentUserAccessor = currentUserAccessor;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
        _configuration = configuration;
        _env = env;
        _webhookService = webhookService;
    }

    public async Task<DingDingSettingsDto> GetSettingsAsync()
    {
        var opt = _settingsProvider.Current;

        return new DingDingSettingsDto
        {
            AppKey = opt.AppKey,
            HasAppSecret = !string.IsNullOrWhiteSpace(opt.AppSecret),
            AgentId = opt.AgentId,
            RobotCode = opt.RobotCode,
            HasCallbackToken = !string.IsNullOrWhiteSpace(opt.CallbackToken),
            HasEncodingAesKey = !string.IsNullOrWhiteSpace(opt.EncodingAesKey),
            ReceiveMode = opt.ReceiveMode,
            MailboxId = opt.MailboxId,
            ContactCollectionEnabled = opt.ContactCollectionEnabled,
            AutoReplyEnabled = opt.AutoReplyEnabled,
            AutoReplyMessage = opt.AutoReplyMessage,
            Enabled = opt.Enabled,
            CallbackPath = opt.CallbackPath
        };
    }

    public async Task<DingDingSettingsDto> UpdateSettingsAsync(UpdateDingDingSettingsRequest request)
    {
        _logger.LogInformation("钉钉配置更新请求到达: Enabled={Enabled}, ReceiveMode={ReceiveMode}, AppKey={AppKey}, AppSecret={HasAppSecret}",
            request.Enabled, request.ReceiveMode, request.AppKey, !string.IsNullOrEmpty(request.AppSecret));

        if (!string.IsNullOrEmpty(request.AppKey))
            await SaveOptionAsync(DingDingOptionKeys.AppKey, request.AppKey);

        if (!string.IsNullOrEmpty(request.AppSecret))
        {
            var encrypted = "ENC:" + _encryptionService.Encrypt(request.AppSecret);
            await SaveOptionAsync(DingDingOptionKeys.AppSecret, encrypted);
        }

        if (request.AgentId.HasValue)
            await SaveOptionAsync(DingDingOptionKeys.AgentId, request.AgentId.Value.ToString());

        if (!string.IsNullOrEmpty(request.RobotCode))
            await SaveOptionAsync(DingDingOptionKeys.RobotCode, request.RobotCode);

        if (!string.IsNullOrEmpty(request.CallbackToken))
        {
            var encrypted = "ENC:" + _encryptionService.Encrypt(request.CallbackToken);
            await SaveOptionAsync(DingDingOptionKeys.CallbackToken, encrypted);
        }

        if (!string.IsNullOrEmpty(request.EncodingAesKey))
        {
            var encrypted = "ENC:" + _encryptionService.Encrypt(request.EncodingAesKey);
            await SaveOptionAsync(DingDingOptionKeys.EncodingAesKey, encrypted);
        }

        if (!string.IsNullOrEmpty(request.ReceiveMode))
            await SaveOptionAsync(DingDingOptionKeys.ReceiveMode, request.ReceiveMode);

        if (request.MailboxId.HasValue)
            await SaveOptionAsync(DingDingOptionKeys.MailboxId, request.MailboxId.Value.ToString());

        if (request.ContactCollectionEnabled.HasValue)
            await SaveOptionAsync(DingDingOptionKeys.ContactCollectionEnabled, request.ContactCollectionEnabled.Value.ToString().ToLowerInvariant());

        if (request.AutoReplyEnabled.HasValue)
            await SaveOptionAsync(DingDingOptionKeys.AutoReplyEnabled, request.AutoReplyEnabled.Value.ToString().ToLowerInvariant());

        if (request.AutoReplyMessage != null)
            await SaveOptionAsync(DingDingOptionKeys.AutoReplyMessage, request.AutoReplyMessage);

        if (request.Enabled.HasValue)
            await SaveOptionAsync(DingDingOptionKeys.Enabled, request.Enabled.Value.ToString().ToLowerInvariant());

        await _unitOfWork.SaveChangesAsync();

        await _settingsProvider.RefreshAsync();

        var refreshed = _settingsProvider.Current;
        _logger.LogInformation("钉钉配置已保存并刷新缓存: Enabled={Enabled}, Mode={ReceiveMode}, IsConfigured={IsConfigured}",
            refreshed.Enabled, refreshed.ReceiveMode, refreshed.IsConfigured);

        return await GetSettingsAsync();
    }

    public async Task<DingDingStatusDto> GetStatusAsync()
    {
        var opt = _settingsProvider.Current;
        var status = new DingDingStatusDto
        {
            Configured = opt.IsConfigured,
            Enabled = opt.Enabled,
            CallbackUrl = BuildCallbackUrl(),
            ReceiveMode = opt.ReceiveMode,
            RobotCode = opt.RobotCode
        };

        // Stream 模式：直接读取 Stream 长连接状态，不请求 Access Token API
        // 避免 GetAccessTokenAsync 内部 SemaphoreSlim 锁竞争导致的永久超时问题
        if (string.Equals(opt.ReceiveMode, "Stream", StringComparison.OrdinalIgnoreCase))
        {
            status.ApiReachable = opt.IsConfigured && _streamConnectionStatus.IsConnected;
            _logger.LogDebug("钉钉 Stream 模式状态查询: IsConnected={IsConnected}, LastConnectedAt={LastConnectedAt}, LastError={LastError}",
                _streamConnectionStatus.IsConnected, _streamConnectionStatus.LastConnectedAt, _streamConnectionStatus.LastError);
            return status;
        }

        // Http 模式：检测 Access Token API 可达性（8 秒超时，避免网络波动误判）
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(opt.StatusCheckTimeoutSeconds));
            status.ApiReachable = opt.IsConfigured && await TestConnectionWithCancellationAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("钉钉 API 可达性检测超时（>8s），视为不可达，不阻塞页面加载");
            status.ApiReachable = false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "钉钉 API 可达性检测失败");
            status.ApiReachable = false;
        }

        return status;
    }

    private async Task<bool> TestConnectionWithCancellationAsync(CancellationToken cancellationToken)
    {
        // 将 CancellationToken 传入 TestConnectionAsync，确保底层 HTTP 请求也能被取消
        // 避免 SemaphoreSlim 锁被长期持有导致后续所有请求阻塞
        return await _dingDingClient.TestConnectionAsync(cancellationToken);
    }

    public async Task<DingDingReplyResult> ReplyAsync(DingDingReplyRequest request, int userId)
    {
        var opt = _settingsProvider.Current;
        if (!opt.Enabled || !opt.IsConfigured)
        {
            return new DingDingReplyResult
            {
                Sent = false,
                ErrorMessage = "钉钉未启用或未配置"
            };
        }

        // 查找会话
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(request.ConversationId);
        if (conversation == null)
        {
            return new DingDingReplyResult { Sent = false, ErrorMessage = "会话不存在" };
        }

        // 提取客户 sessionWebhook 和 userId（从最近一条客户线程）
        var threads = await _unitOfWork.Threads.FindAsync(
            t => t.ConversationId == request.ConversationId
                && t.Type == ThreadType.Customer
                && t.State != ThreadState.Deleted
                && t.SourceType == SourceType.DingTalk);
        var lastCustomerThread = threads.OrderByDescending(t => t.CreatedAt).FirstOrDefault();

        // sessionWebhook 可能已过期，存放在 thread 的 MessageId 字段中（以 "sw:" 前缀标记）
        var sessionWebhook = ExtractSessionWebhook(lastCustomerThread);
        var customerUserId = lastCustomerThread?.From ?? string.Empty;

        bool sent = false;

        // 优先通过 sessionWebhook 回复（即时会话）
        if (!string.IsNullOrEmpty(sessionWebhook) && !string.IsNullOrEmpty(request.Content))
        {
            var payload = new { content = request.Content };
            sent = await _dingDingClient.SendBySessionWebhookAsync(sessionWebhook, "text", payload);
        }

        // sessionWebhook 失效时，通过批量发送单聊消息
        if (!sent && !string.IsNullOrEmpty(customerUserId) && !string.IsNullOrEmpty(opt.RobotCode))
        {
            if (!string.IsNullOrEmpty(request.Content))
            {
                var contentJson = System.Text.Json.JsonSerializer.Serialize(new { content = request.Content });
                var processKey = await _dingDingClient.BatchSendOtoMessageAsync(
                    opt.RobotCode!, new[] { customerUserId }, "sampleText", contentJson);
                sent = !string.IsNullOrEmpty(processKey);
            }
        }

        if (!sent && !string.IsNullOrEmpty(request.Content))
        {
            return new DingDingReplyResult { Sent = false, ErrorMessage = "消息发送失败（sessionWebhook 已过期且无法通过批量发送）" };
        }

        // 事务创建回复线程
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var thread = new ConversationThread
            {
                ConversationId = conversation.Id,
                UserId = userId,
                From = opt.RobotCode ?? opt.AppKey ?? string.Empty,
                To = customerUserId,
                Subject = string.Empty,
                Body = BuildReplyHtmlBody(request),
                MessageId = $"dingding-reply-{Guid.NewGuid():N}",
                SourceType = SourceType.DingTalk,
                Type = ThreadType.Message,
                SourceVia = SourceVia.User,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Threads.AddAsync(thread);
            await _unitOfWork.SaveChangesAsync();

            // 关联附件（批量查询，避免 N+1：N 个附件 = N 次 DB 往返）
            if (request.AttachmentIds != null && request.AttachmentIds.Length > 0)
            {
                var attachments = await _unitOfWork.Attachments.GetByIdsTrackedAsync(request.AttachmentIds);
                foreach (var attachment in attachments)
                {
                    attachment.ThreadId = thread.Id;
                }
                await _unitOfWork.SaveChangesAsync();
                thread.HasAttachments = true;
                if (!conversation.HasAttachments)
                {
                    conversation.HasAttachments = true;
                }
            }

            conversation.UpdatedAt = DateTime.UtcNow;
            conversation.LastReplyAt = DateTime.UtcNow;
            conversation.LastReplyFrom = SourceVia.User;
            await _unitOfWork.SaveChangesAsync();

            await transaction.CommitAsync();

            // 发送附件（图片/文件）到钉钉
            // 钉钉 sessionWebhook 不支持 image 类型，统一走 UploadMedia + BatchSend 路径
            if (request.AttachmentIds != null && request.AttachmentIds.Length > 0)
            {
                await SendDingDingAttachmentsAsync(request.AttachmentIds, thread.Id, customerUserId, opt.RobotCode ?? opt.AppKey);
            }

            // SignalR 通知
            await _notificationPublisher.NotifyMailboxAsync(
                conversation.MailboxId.ToString(),
                "conversation_updated",
                new { conversationId = conversation.Id });

            // 触发 Webhook 通知（失败仅记录日志，不阻塞主流程）
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

            return new DingDingReplyResult
            {
                Sent = true,
                ThreadCreated = true,
                ThreadId = thread.Id,
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "钉钉回复线程创建失败：ConversationId={ConversationId}", request.ConversationId);
            return new DingDingReplyResult
            {
                Sent = sent,
                ThreadCreated = false,
                ErrorMessage = "消息已发送，但会话线程记录失败"
            };
        }
    }

    private static string? ExtractSessionWebhook(ConversationThread? thread)
    {
        if (thread == null)
            return null;

        // T000113：sessionWebhook 由 MessageId（sw: 前缀）迁移到 Headers（sessionWebhook: 前缀），
        // 因为 Threads.MessageId 有 UNIQUE 约束，而 sessionWebhook 是会话级同值，
        // 存 MessageId 会导致同一会话第二/后续消息唯一冲突而丢失。
        // 读取时优先 Headers（新格式），兜底 MessageId "sw:"（旧数据兼容）。
        const string headerPrefix = "sessionWebhook:";
        if (!string.IsNullOrWhiteSpace(thread.Headers))
        {
            var idx = thread.Headers.IndexOf(headerPrefix, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var value = thread.Headers.Substring(idx + headerPrefix.Length).Trim();
                if (value.Length > 0)
                    return value;
            }
        }

        if (!string.IsNullOrEmpty(thread.MessageId) && thread.MessageId.StartsWith("sw:"))
            return thread.MessageId[3..];

        return null;
    }

    private async Task SaveOptionAsync(string key, string value)
    {
        // 必须使用 tracking 查询！否则 AsNoTracking 返回的实体处于 Detached 状态，
        // 修改属性后 ChangeTracker 检测不到变更，SaveChangesAsync 不会生成 UPDATE 语句！
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
                Category = DingDingOptionKeys.Category,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
    }

    private string BuildCallbackUrl()
    {
        var baseUrl = _configuration["App:BaseUrl"]
            ?? throw new InvalidOperationException("配置 App:BaseUrl 未设置，钉钉回调 URL 无法生成。请在 appsettings 或环境变量中配置 App:BaseUrl。");
        var callbackPath = _settingsProvider.Current.CallbackPath ?? "/api/dingding";
        return $"{baseUrl.TrimEnd('/')}{callbackPath}";
    }

    private static string BuildReplyHtmlBody(DingDingReplyRequest request)
    {
        var content = string.IsNullOrEmpty(request.Content)
            ? string.Empty
            : $"<div class=\"dingding-text\">{System.Web.HttpUtility.HtmlEncode(request.Content).Replace("\n", "<br>")}</div>";

        if (request.AttachmentIds != null && request.AttachmentIds.Length > 0)
        {
            content += $"<div class=\"dingding-media\">📎 附件 ×{request.AttachmentIds.Length}</div>";
        }

        return content;
    }

    /// <summary>
    /// 发送附件（图片/文件）到钉钉用户。
    /// 钉钉 sessionWebhook 不支持 image 类型，统一走 UploadMedia + BatchSend 路径：
    /// 1. 读取本地附件文件
    /// 2. UploadMediaAsync 上传获取 media_id
    /// 3. 图片 → BatchSendImageMessageAsync (sampleImageMsg)
    ///    文件 → BatchSendFileMessageAsync (sampleFile)
    /// </summary>
    private async Task SendDingDingAttachmentsAsync(int[] attachmentIds, int threadId, string customerUserId, string? robotCode)
    {
        if (attachmentIds == null || attachmentIds.Length == 0) return;
        if (string.IsNullOrEmpty(customerUserId) || string.IsNullOrEmpty(robotCode))
        {
            _logger.LogWarning("钉钉附件发送跳过：缺少 customerUserId 或 robotCode，ThreadId={ThreadId}", threadId);
            return;
        }

        // 附件根目录：统一解析（绝对路径原样、相对/缺失锚定 ContentRoot，与上传侧一致，T000112）
        var attachmentBasePath = AttachmentPathResolver.ResolveBasePath(
            _configuration["Attachment:UploadPath"], _env.ContentRootPath);
        var sentCount = 0;

        // 批量预查询：一次性取回所有附件字典，消除循环内 N+1 DB 往返
        var allAttachments = await _unitOfWork.Attachments.GetByIdsAsync(attachmentIds);
        var attById = allAttachments.ToDictionary(a => a.Id);

        foreach (var attachmentId in attachmentIds)
        {
            try
            {
                if (!attById.TryGetValue(attachmentId, out var attachment))
                {
                    _logger.LogWarning("钉钉附件不存在：AttachmentId={AttachmentId}", attachmentId);
                    continue;
                }

                // 读取本地文件
                var filePath = string.IsNullOrEmpty(attachment.FileDir)
                    ? Path.Combine(attachmentBasePath, attachment.StoredFileName)
                    : Path.Combine(attachmentBasePath, attachment.FileDir, attachment.StoredFileName);

                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("钉钉附件文件不存在：AttachmentId={AttachmentId}, Path={Path}", attachmentId, filePath);
                    continue;
                }

                var fileBytes = await File.ReadAllBytesAsync(filePath);
                if (fileBytes.Length == 0)
                {
                    _logger.LogWarning("钉钉附件文件为空：AttachmentId={AttachmentId}", attachmentId);
                    continue;
                }

                // 上传到钉钉获取 media_id
                var fileName = attachment.FileName ?? $"file{Path.GetExtension(attachment.StoredFileName)}";
                var mimeType = attachment.MimeType ?? "application/octet-stream";
                var mediaId = await _dingDingClient.UploadMediaAsync(fileBytes, fileName, mimeType);

                if (string.IsNullOrEmpty(mediaId))
                {
                    _logger.LogError("钉钉附件上传媒体失败，media_id 为空：AttachmentId={AttachmentId}", attachmentId);
                    continue;
                }

                _logger.LogInformation("钉钉附件上传媒体成功：AttachmentId={AttachmentId}, MediaId={MediaId}", attachmentId, mediaId);

                // 判断是否为图片
                var isImage = attachment.Type == AttachmentType.Image
                    || (attachment.MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false);

                string? processKey;
                if (isImage)
                {
                    processKey = await _dingDingClient.BatchSendImageMessageAsync(
                        robotCode!, new[] { customerUserId }, mediaId);
                }
                else
                {
                    var ext = Path.GetExtension(fileName).TrimStart('.');
                    if (string.IsNullOrEmpty(ext)) ext = "file";
                    processKey = await _dingDingClient.BatchSendFileMessageAsync(
                        robotCode!, new[] { customerUserId }, mediaId, fileName, ext);
                }

                if (!string.IsNullOrEmpty(processKey))
                {
                    sentCount++;
                    _logger.LogInformation("钉钉附件发送成功：AttachmentId={AttachmentId}, IsImage={IsImage}, ProcessKey={ProcessKey}",
                        attachmentId, isImage, processKey);
                }
                else
                {
                    _logger.LogError("钉钉附件发送失败：AttachmentId={AttachmentId}, IsImage={IsImage}", attachmentId, isImage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "钉钉附件发送异常：AttachmentId={AttachmentId}", attachmentId);
            }
        }

        _logger.LogInformation("钉钉附件发送完成：ThreadId={ThreadId}, Sent={Sent}/{Total}", threadId, sentCount, attachmentIds.Length);
    }
}
