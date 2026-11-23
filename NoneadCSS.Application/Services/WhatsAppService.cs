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
/// WhatsApp 管理服务 - 配置读写、连接状态查询、回复消息、模板消息发送。
/// 复刻 WeChatService 模式，敏感字段 AES 加密存储。
/// </summary>
public class WhatsAppService : IWhatsAppService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEncryptionService _encryptionService;
    private readonly IWhatsAppSettingsProvider _settingsProvider;
    private readonly IWhatsAppClient _whatsAppClient;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<WhatsAppService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _env;
    private readonly IOptions<IngestionMatchOptions> _ingestionMatchOptions;
    private readonly IWebhookService? _webhookService;

    public WhatsAppService(
        IUnitOfWork unitOfWork,
        IEncryptionService encryptionService,
        IWhatsAppSettingsProvider settingsProvider,
        IWhatsAppClient whatsAppClient,
        ICurrentUserAccessor currentUserAccessor,
        INotificationPublisher notificationPublisher,
        ILogger<WhatsAppService> logger,
        IConfiguration configuration,
        IOptions<IngestionMatchOptions> ingestionMatchOptions,
        IHostEnvironment env,
        IWebhookService? webhookService = null)
    {
        _unitOfWork = unitOfWork;
        _encryptionService = encryptionService;
        _settingsProvider = settingsProvider;
        _whatsAppClient = whatsAppClient;
        _currentUserAccessor = currentUserAccessor;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
        _configuration = configuration;
        _ingestionMatchOptions = ingestionMatchOptions;
        _env = env;
        _webhookService = webhookService;
    }

    public async Task<WhatsAppSettingsDto> GetSettingsAsync()
    {
        var opt = _settingsProvider.Current;

        return new WhatsAppSettingsDto
        {
            PhoneNumberId = opt.PhoneNumberId,
            BusinessPhoneNumber = opt.BusinessPhoneNumber,
            ApiVersion = opt.ApiVersion,
            MailboxId = opt.MailboxId,
            ContactCollectionEnabled = opt.ContactCollectionEnabled,
            AutoReplyEnabled = opt.AutoReplyEnabled,
            AutoReplyMessage = opt.AutoReplyMessage,
            Enabled = opt.Enabled,
            CallbackUrl = BuildCallbackUrl(),
            HasAccessToken = !string.IsNullOrWhiteSpace(opt.AccessToken),
            HasAppSecret = !string.IsNullOrWhiteSpace(opt.AppSecret),
            HasVerifyToken = !string.IsNullOrWhiteSpace(opt.VerifyToken)
        };
    }

    public async Task<WhatsAppSettingsDto> UpdateSettingsAsync(UpdateWhatsAppSettingsRequest request)
    {
        // 持久化到 SystemOptions 表
        if (!string.IsNullOrEmpty(request.PhoneNumberId))
            await SaveOptionAsync(WhatsAppOptionKeys.PhoneNumberId, request.PhoneNumberId);

        if (!string.IsNullOrEmpty(request.AccessToken))
        {
            var encrypted = "ENC:" + _encryptionService.Encrypt(request.AccessToken);
            await SaveOptionAsync(WhatsAppOptionKeys.AccessToken, encrypted);
        }

        if (!string.IsNullOrEmpty(request.AppSecret))
        {
            var encrypted = "ENC:" + _encryptionService.Encrypt(request.AppSecret);
            await SaveOptionAsync(WhatsAppOptionKeys.AppSecret, encrypted);
        }

        if (!string.IsNullOrEmpty(request.VerifyToken))
            await SaveOptionAsync(WhatsAppOptionKeys.VerifyToken, request.VerifyToken);

        if (request.BusinessPhoneNumber != null)
            await SaveOptionAsync(WhatsAppOptionKeys.BusinessPhoneNumber, request.BusinessPhoneNumber);

        if (request.ApiVersion != null)
            await SaveOptionAsync(WhatsAppOptionKeys.ApiVersion, request.ApiVersion);

        if (request.MailboxId.HasValue)
            await SaveOptionAsync(WhatsAppOptionKeys.MailboxId, request.MailboxId.Value.ToString());

        if (request.ContactCollectionEnabled.HasValue)
        {
            await _unitOfWork.SystemOptions.SetAsync(WhatsAppOptionKeys.ContactCollectionEnabled, request.ContactCollectionEnabled.Value.ToString().ToLowerInvariant(), WhatsAppOptionKeys.Category);
        }

        if (request.AutoReplyEnabled.HasValue)
            await SaveOptionAsync(WhatsAppOptionKeys.AutoReplyEnabled, request.AutoReplyEnabled.Value.ToString().ToLowerInvariant());

        if (request.AutoReplyMessage != null)
            await SaveOptionAsync(WhatsAppOptionKeys.AutoReplyMessage, request.AutoReplyMessage);

        if (request.Enabled.HasValue)
            await SaveOptionAsync(WhatsAppOptionKeys.Enabled, request.Enabled.Value.ToString().ToLowerInvariant());

        await _unitOfWork.SaveChangesAsync();

        // 刷新配置缓存
        await _settingsProvider.RefreshAsync();

        return await GetSettingsAsync();
    }

    public async Task<WhatsAppStatusDto> GetStatusAsync()
    {
        var opt = _settingsProvider.Current;
        var status = new WhatsAppStatusDto
        {
            Configured = opt.IsConfigured,
            Enabled = opt.Enabled,
            CallbackUrl = BuildCallbackUrl()
        };

        // 24h 窗口状态（需要查找最近的 WhatsApp 会话）
        // 这里简化处理：尝试查找最近一条 WhatsApp 客户线程
        try
        {
            var recentThreads = await _unitOfWork.Threads.FindAsync(
                t => t.SourceType == SourceType.WhatsApp
                    && t.Type == ThreadType.Customer
                    && t.State != ThreadState.Deleted);
            var recentThread = recentThreads
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefault();

            if (recentThread != null)
            {
                status.LastCustomerMessageAt = recentThread.CreatedAt;
                var elapsed = DateTime.UtcNow - recentThread.CreatedAt;
                var windowEnd = recentThread.CreatedAt.AddHours(24);
                var remaining = windowEnd - DateTime.UtcNow;

                if (remaining <= TimeSpan.Zero)
                {
                    status.WindowStatus = "expired";
                    status.WindowRemainingHours = 0;
                }
                else if (remaining <= TimeSpan.FromHours(_ingestionMatchOptions.Value.WhatsAppWarningThresholdHours))
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
            _logger.LogError(ex, "查询 WhatsApp 24h 窗口状态失败");
        }

        // API 可达性：真实探测 Graph API（带超时控制，避免网络波动误判、阻塞页面加载）
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(opt.StatusCheckTimeoutSeconds));
            status.ApiReachable = opt.IsConfigured && await _whatsAppClient.TestConnectionAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WhatsApp API 可达性检测超时（>{Timeout}s），视为不可达，不阻塞页面加载", opt.StatusCheckTimeoutSeconds);
            status.ApiReachable = false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp API 可达性检测失败");
            status.ApiReachable = false;
        }

        return status;
    }

    public async Task<WhatsAppReplyResult> ReplyAsync(WhatsAppReplyRequest request, int userId)
    {
        _logger.LogInformation("WhatsApp 开始回复：ConversationId={ConvId}, UserId={UserId}, ContentLength={ContentLen}, AttachmentCount={AttachCount}",
            request.ConversationId, userId, request.Content?.Length ?? 0, request.AttachmentIds?.Count ?? 0);

        var opt = _settingsProvider.Current;
        if (!opt.Enabled || !opt.IsConfigured)
        {
            _logger.LogWarning("WhatsApp 回复被拒绝：未启用或未配置（Enabled={Enabled}, IsConfigured={IsConfigured}）", opt.Enabled, opt.IsConfigured);
            return new WhatsAppReplyResult
            {
                Sent = false,
                ErrorMessage = "WhatsApp 未启用或未配置"
            };
        }

        // 查找会话
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(request.ConversationId);
        if (conversation == null)
        {
            _logger.LogWarning("WhatsApp 回复被拒绝：会话不存在 ConversationId={ConvId}", request.ConversationId);
            return new WhatsAppReplyResult { Sent = false, ErrorMessage = "会话不存在" };
        }

        // 检查 24h 窗口
        var windowCheck = await Check24hWindowAsync(conversation.Id);
        _logger.LogDebug("WhatsApp 24h 窗口检查：ConversationId={ConvId}, WindowStatus={Status}", conversation.Id, windowCheck);
        if (windowCheck == "expired")
        {
            _logger.LogWarning("WhatsApp 回复被拒绝：24h 窗口已过期 ConversationId={ConvId}", conversation.Id);
            return new WhatsAppReplyResult
            {
                Sent = false,
                ErrorMessage = "24 小时客户服务窗口已过期，请发送模板消息"
            };
        }

        // 提取客户 wa_id（从最近一条客户线程的 From 字段）
        var threads = await _unitOfWork.Threads.FindAsync(
            t => t.ConversationId == request.ConversationId
                && t.Type == ThreadType.Customer
                && t.State != ThreadState.Deleted);
        var lastCustomerThread = threads.OrderByDescending(t => t.CreatedAt).FirstOrDefault();
        if (lastCustomerThread == null || string.IsNullOrEmpty(lastCustomerThread.From))
        {
            _logger.LogWarning("WhatsApp 回复被拒绝：无法获取客户号码 ConversationId={ConvId}", request.ConversationId);
            return new WhatsAppReplyResult { Sent = false, ErrorMessage = "无法获取客户 WhatsApp 号码" };
        }

        var waId = lastCustomerThread.From;
        _logger.LogDebug("WhatsApp 客户号码提取成功：ConversationId={ConvId}, WaId={WaId}", conversation.Id, waId);

        // 发送文本消息
        string? sentMessageId = null;
        if (!string.IsNullOrEmpty(request.Content))
        {
            _logger.LogDebug("WhatsApp 发送文本消息：WaId={WaId}, ContentLength={Len}", waId, request.Content.Length);
            sentMessageId = await _whatsAppClient.SendTextMessageAsync(waId, request.Content);
            if (string.IsNullOrEmpty(sentMessageId))
            {
                _logger.LogError("WhatsApp 文本消息发送失败：ConversationId={ConvId}, WaId={WaId}", conversation.Id, waId);
                return new WhatsAppReplyResult { Sent = false, ErrorMessage = "文本消息发送失败" };
            }
            _logger.LogInformation("WhatsApp 文本消息发送成功：ConversationId={ConvId}, WaId={WaId}, MessageId={MsgId}", conversation.Id, waId, sentMessageId);
        }

        // 发送附件
        var mediaErrors = new List<string>();
        if (request.AttachmentIds != null && request.AttachmentIds.Count > 0)
        {
            _logger.LogInformation("WhatsApp 开始发送 {Count} 个附件：ConversationId={ConvId}", request.AttachmentIds.Count, conversation.Id);
            // 附件根目录统一解析（T000112）：绝对路径原样、相对/缺失锚定 ContentRoot，与上传侧一致
            var attachmentBasePath = AttachmentPathResolver.ResolveBasePath(
                _configuration["Attachment:UploadPath"], _env.ContentRootPath);
            // 批量预查询：替代循环内逐条 GetByIdAsync，消除 N+1 查询
            var allAtts = await _unitOfWork.Attachments.GetByIdsAsync(request.AttachmentIds);
            var attMap = allAtts.ToDictionary(a => a.Id);
            foreach (var attachmentId in request.AttachmentIds)
            {
                if (!attMap.TryGetValue(attachmentId, out var attachment))
                {
                    _logger.LogWarning("WhatsApp 附件不存在：AttachmentId={AttachId}", attachmentId);
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
                    _logger.LogWarning("WhatsApp 附件路径遍历拦截：FileDir={FileDir}", attachment.FileDir);
                    mediaErrors.Add($"附件 {attachment.FileName} 路径无效");
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    _logger.LogWarning("WhatsApp 附件文件不存在：Path={Path}, AttachmentId={AttachId}", fullPath, attachmentId);
                    mediaErrors.Add($"附件 {attachment.FileName} 文件不存在");
                    continue;
                }

                var data = await File.ReadAllBytesAsync(fullPath);
                _logger.LogDebug("WhatsApp 附件读取成功：FileName={FileName}, Size={Size} bytes, MimeType={MimeType}", attachment.FileName, data.Length, attachment.MimeType);

                var mediaId = await _whatsAppClient.UploadMediaAsync(data, attachment.FileName, attachment.MimeType);
                if (string.IsNullOrEmpty(mediaId))
                {
                    _logger.LogError("WhatsApp 附件上传失败：FileName={FileName}, Size={Size}", attachment.FileName, data.Length);
                    mediaErrors.Add($"附件 {attachment.FileName} 上传失败");
                    continue;
                }
                _logger.LogDebug("WhatsApp 附件上传成功：FileName={FileName}, MediaId={MediaId}", attachment.FileName, mediaId);

                var mediaType = attachment.Type switch
                {
                    AttachmentType.Image => "image",
                    AttachmentType.Audio => "audio",
                    AttachmentType.Video => "video",
                    _ => "document"
                };

                var mediaWamid = await _whatsAppClient.SendMediaMessageAsync(
                    waId, mediaType, mediaId, null, attachment.FileName);
                if (!string.IsNullOrEmpty(mediaWamid))
                {
                    sentMessageId = sentMessageId ?? mediaWamid;
                    _logger.LogInformation("WhatsApp 附件发送成功：FileName={FileName}, MediaType={MediaType}, MessageId={MsgId}", attachment.FileName, mediaType, mediaWamid);
                }
                else
                {
                    _logger.LogError("WhatsApp 附件发送失败：FileName={FileName}, MediaType={MediaType}", attachment.FileName, mediaType);
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
                From = opt.BusinessPhoneNumber ?? string.Empty,
                To = waId,
                Subject = string.Empty,
                Body = BuildReplyHtmlBody(request),
                MessageId = sentMessageId ?? string.Empty,
                SourceType = SourceType.WhatsApp,
                Type = ThreadType.Message,
                SourceVia = SourceVia.User,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Threads.AddAsync(thread);
            await _unitOfWork.SaveChangesAsync();

            // 关联附件：批量追踪查询后在内存中回填 ThreadId，消除循环 N+1 与多次 SaveChanges
            if (request.AttachmentIds != null && request.AttachmentIds.Count > 0)
            {
                var trackedAtts = await _unitOfWork.Attachments.GetByIdsTrackedAsync(request.AttachmentIds);
                foreach (var attachment in trackedAtts)
                {
                    attachment.ThreadId = thread.Id;
                }
                await _unitOfWork.SaveChangesAsync();
            }

            // 更新会话时间
            conversation.UpdatedAt = DateTime.UtcNow;
            conversation.LastReplyAt = DateTime.UtcNow;
            conversation.LastReplyFrom = SourceVia.User;
            await _unitOfWork.SaveChangesAsync();

            await transaction.CommitAsync();

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

            _logger.LogInformation("WhatsApp 回复完成：ConversationId={ConvId}, ThreadId={ThreadId}, MessageId={MsgId}, MediaErrors={ErrorCount}",
                conversation.Id, thread.Id, sentMessageId, mediaErrors.Count);

            return new WhatsAppReplyResult
            {
                Sent = true,
                ThreadCreated = true,
                ThreadId = thread.Id,
                ErrorMessage = mediaErrors.Count > 0 ? string.Join("; ", mediaErrors) : null
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "WhatsApp 回复线程创建失败：ConversationId={ConvId}, MessageId={MsgId}", request.ConversationId, sentMessageId);
            return new WhatsAppReplyResult
            {
                Sent = true,
                ThreadCreated = false,
                ErrorMessage = "消息已发送，但会话线程记录失败"
            };
        }
    }

    public async Task<WhatsAppTemplateResult> SendTemplateMessageAsync(WhatsAppTemplateRequest request, int userId)
    {
        _logger.LogInformation("WhatsApp 开始发送模板消息：ConversationId={ConvId}, UserId={UserId}, Template={Template}, Language={Lang}, HasComponents={HasComponents}",
            request.ConversationId, userId, request.TemplateName, request.Language, !string.IsNullOrEmpty(request.ComponentsJson));

        var opt = _settingsProvider.Current;
        if (!opt.Enabled || !opt.IsConfigured)
        {
            _logger.LogWarning("WhatsApp 模板消息被拒绝：未启用或未配置（Enabled={Enabled}, IsConfigured={IsConfigured}）", opt.Enabled, opt.IsConfigured);
            return new WhatsAppTemplateResult { Sent = false, ErrorMessage = "WhatsApp 未启用或未配置" };
        }

        // 查找会话
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(request.ConversationId);
        if (conversation == null)
        {
            _logger.LogWarning("WhatsApp 模板消息被拒绝：会话不存在 ConversationId={ConvId}", request.ConversationId);
            return new WhatsAppTemplateResult { Sent = false, ErrorMessage = "会话不存在" };
        }

        // 提取客户 wa_id
        var threads = await _unitOfWork.Threads.FindAsync(
            t => t.ConversationId == request.ConversationId
                && t.Type == ThreadType.Customer
                && t.State != ThreadState.Deleted);
        var lastCustomerThread = threads.OrderByDescending(t => t.CreatedAt).FirstOrDefault();
        if (lastCustomerThread == null || string.IsNullOrEmpty(lastCustomerThread.From))
        {
            _logger.LogWarning("WhatsApp 模板消息被拒绝：无法获取客户号码 ConversationId={ConvId}", request.ConversationId);
            return new WhatsAppTemplateResult { Sent = false, ErrorMessage = "无法获取客户 WhatsApp 号码" };
        }

        var waId = lastCustomerThread.From;
        _logger.LogDebug("WhatsApp 模板消息客户号码提取：ConversationId={ConvId}, WaId={WaId}", conversation.Id, waId);

        // 解析组件参数
        object? components = null;
        if (!string.IsNullOrEmpty(request.ComponentsJson))
        {
            try
            {
                components = System.Text.Json.JsonSerializer.Deserialize<object>(request.ComponentsJson);
                _logger.LogDebug("WhatsApp 模板组件 JSON 解析成功：Components={Components}", request.ComponentsJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WhatsApp 模板组件 JSON 解析失败：Components={Components}", request.ComponentsJson);
                return new WhatsAppTemplateResult { Sent = false, ErrorMessage = "模板组件 JSON 解析失败" };
            }
        }

        // 发送模板消息
        _logger.LogDebug("WhatsApp 调用 API 发送模板消息：WaId={WaId}, Template={Template}, Language={Lang}", waId, request.TemplateName, request.Language);
        var wamid = await _whatsAppClient.SendTemplateMessageAsync(waId, request.TemplateName, request.Language, components);
        if (string.IsNullOrEmpty(wamid))
        {
            _logger.LogError("WhatsApp 模板消息发送失败（API 返回空）：ConversationId={ConvId}, Template={Template}, WaId={WaId}", conversation.Id, request.TemplateName, waId);
            return new WhatsAppTemplateResult { Sent = false, ErrorMessage = "模板消息发送失败" };
        }
        _logger.LogInformation("WhatsApp 模板消息发送成功：ConversationId={ConvId}, Template={Template}, WaId={WaId}, MessageId={MsgId}", conversation.Id, request.TemplateName, waId, wamid);

        // 创建线程记录
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var thread = new ConversationThread
            {
                ConversationId = conversation.Id,
                From = opt.BusinessPhoneNumber ?? string.Empty,
                To = waId,
                Subject = $"模板消息: {request.TemplateName}",
                Body = $"<div class=\"whatsapp-template\">📋 <strong>模板消息：</strong>{System.Web.HttpUtility.HtmlEncode(request.TemplateName)} ({request.Language})</div>",
                MessageId = wamid,
                SourceType = SourceType.WhatsApp,
                Type = ThreadType.Message,
                SourceVia = SourceVia.User,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Threads.AddAsync(thread);
            conversation.UpdatedAt = DateTime.UtcNow;
            conversation.LastReplyAt = DateTime.UtcNow;
            conversation.LastReplyFrom = SourceVia.User;
            await _unitOfWork.SaveChangesAsync();

            await transaction.CommitAsync();

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

            _logger.LogInformation("WhatsApp 模板消息线程创建成功：ConversationId={ConvId}, ThreadId={ThreadId}, MessageId={MsgId}",
                conversation.Id, thread.Id, wamid);

            return new WhatsAppTemplateResult { Sent = true, MessageId = wamid };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "WhatsApp 模板消息线程创建失败：ConversationId={ConvId}, MessageId={MsgId}", request.ConversationId, wamid);
            return new WhatsAppTemplateResult
            {
                Sent = true,
                MessageId = wamid,
                ErrorMessage = "消息已发送，但线程记录失败"
            };
        }
    }

    private async Task<string> Check24hWindowAsync(int conversationId)
    {
        var threads = await _unitOfWork.Threads.FindAsync(
            t => t.ConversationId == conversationId
                && t.Type == ThreadType.Customer
                && t.State != ThreadState.Deleted);
        var lastCustomerThread = threads.OrderByDescending(t => t.CreatedAt).FirstOrDefault();
        if (lastCustomerThread == null)
            return "expired";

        var windowEnd = lastCustomerThread.CreatedAt.AddHours(24);
        var remaining = windowEnd - DateTime.UtcNow;

        if (remaining <= TimeSpan.Zero) return "expired";
        if (remaining <= TimeSpan.FromHours(_ingestionMatchOptions.Value.WhatsAppWarningThresholdHours)) return "warning";
        return "ok";
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
                Category = WhatsAppOptionKeys.Category,
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
        var callbackPath = _settingsProvider.Current.CallbackPath ?? "/api/whatsapp";
        return $"{baseUrl.TrimEnd('/')}{callbackPath}";
    }

    private static string BuildReplyHtmlBody(WhatsAppReplyRequest request)
    {
        var content = string.IsNullOrEmpty(request.Content)
            ? string.Empty
            : $"<div class=\"whatsapp-text\">{System.Web.HttpUtility.HtmlEncode(request.Content).Replace("\n", "<br>")}</div>";

        if (request.AttachmentIds != null && request.AttachmentIds.Count > 0)
        {
            content += $"<div class=\"whatsapp-media\">📎 附件 ×{request.AttachmentIds.Count}</div>";
        }

        return content;
    }

    /// <inheritdoc/>
    public async Task<WhatsAppTestResult> TestConnectionAsync()
    {
        var settings = _settingsProvider.Current;

        if (!settings.IsConfigured)
        {
            return new WhatsAppTestResult
            {
                Success = false,
                ErrorMessage = "配置不完整，请先填写 Phone Number ID、Access Token 和 Verify Token"
            };
        }

        var businessPhone = settings.BusinessPhoneNumber?.Replace("+", "").Replace(" ", "").Replace("-", "");
        if (string.IsNullOrWhiteSpace(businessPhone))
        {
            return new WhatsAppTestResult
            {
                Success = false,
                ErrorMessage = "商家电话号码未填写"
            };
        }

        try
        {
            var testMessage = "✅ WhatsApp 连接测试成功 — 此消息由系统自动发送，无需回复。";
            var wamid = await _whatsAppClient.SendTextMessageAsync(businessPhone, testMessage);

            if (wamid != null)
            {
                _logger.LogInformation("WhatsApp 测试连接成功，MessageId={MessageId}", wamid);
                return new WhatsAppTestResult
                {
                    Success = true,
                    MessageId = wamid
                };
            }

            return new WhatsAppTestResult
            {
                Success = false,
                ErrorMessage = "消息发送失败，请检查 Access Token 权限和 Phone Number ID 是否正确"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp 测试连接异常");
            return new WhatsAppTestResult
            {
                Success = false,
                ErrorMessage = $"测试失败：{ex.Message}"
            };
        }
    }
}
