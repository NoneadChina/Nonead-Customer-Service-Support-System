using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Services;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 飞书机器人管理服务实现 - 配置读写、连接状态查询、消息回复。
/// 配置持久化到 SystemOptions 数据库表（敏感字段加密），通过 IFeishuSettingsProvider 刷新内存缓存。
/// </summary>
public class FeishuService : IFeishuService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEncryptionService _encryptionService;
    private readonly IFeishuSettingsProvider _settingsProvider;
    private readonly IFeishuAccessTokenService _accessTokenService;
    private readonly IFeishuClient _feishuClient;
    private readonly IFeishuStreamConnectionStatus _streamConnectionStatus;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<FeishuService> _logger;
    private readonly string _baseUrl;
    private readonly string _attachmentBasePath;

    public FeishuService(
        IUnitOfWork unitOfWork,
        IEncryptionService encryptionService,
        IFeishuSettingsProvider settingsProvider,
        IFeishuAccessTokenService accessTokenService,
        IFeishuClient feishuClient,
        IFeishuStreamConnectionStatus streamConnectionStatus,
        ICurrentUserAccessor currentUserAccessor,
        INotificationPublisher notificationPublisher,
        ILogger<FeishuService> logger,
        IConfiguration configuration,
        IHostEnvironment env)
    {
        _unitOfWork = unitOfWork;
        _encryptionService = encryptionService;
        _settingsProvider = settingsProvider;
        _accessTokenService = accessTokenService;
        _feishuClient = feishuClient;
        _streamConnectionStatus = streamConnectionStatus;
        _currentUserAccessor = currentUserAccessor;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
        _baseUrl = configuration["App:BaseUrl"] ?? configuration["App:FrontendUrl"]
            ?? throw new InvalidOperationException("配置 App:BaseUrl 或 App:FrontendUrl 未设置，飞书回调 URL 无法生成。请在 appsettings 或环境变量中配置。");
        // 附件根目录统一解析（T000112）：绝对路径原样、相对/缺失锚定 ContentRoot，与上传侧一致
        _attachmentBasePath = AttachmentPathResolver.ResolveBasePath(
            configuration["Attachment:UploadPath"], env.ContentRootPath);
    }

    public async Task<FeishuSettingsDto> GetSettingsAsync()
    {
        var opt = _settingsProvider.Current;

        return new FeishuSettingsDto
        {
            AppId = opt.AppId,
            HasAppSecret = !string.IsNullOrWhiteSpace(opt.AppSecret),
            HasVerificationToken = !string.IsNullOrWhiteSpace(opt.VerificationToken),
            HasEncryptKey = !string.IsNullOrWhiteSpace(opt.EncryptKey),
            EncryptEnabled = opt.EncryptEnabled,
            MailboxId = opt.MailboxId,
            ContactCollectionEnabled = opt.ContactCollectionEnabled,
            AutoReplyEnabled = opt.AutoReplyEnabled,
            AutoReplyMessage = opt.AutoReplyMessage,
            Enabled = opt.Enabled,
            CallbackUrl = BuildCallbackUrl(),
            ReceiveMode = opt.ReceiveMode
        };
    }

    public async Task<FeishuSettingsDto> UpdateSettingsAsync(UpdateFeishuSettingsRequest request)
    {
        // 持久化到 SystemOptions（敏感字段加密）
        if (!string.IsNullOrWhiteSpace(request.AppId))
        {
            await _unitOfWork.SystemOptions.SetAsync(FeishuOptionKeys.AppId, request.AppId, FeishuOptionKeys.Category);
        }

        // AppSecret 仅在提供新值时更新（加密存储，加 ENC: 前缀标记）
        if (!string.IsNullOrWhiteSpace(request.AppSecret))
        {
            var cipher = "ENC:" + _encryptionService.Encrypt(request.AppSecret);
            await _unitOfWork.SystemOptions.SetAsync(FeishuOptionKeys.AppSecret, cipher, FeishuOptionKeys.Category);
        }

        if (!string.IsNullOrWhiteSpace(request.VerificationToken))
        {
            await _unitOfWork.SystemOptions.SetAsync(FeishuOptionKeys.VerificationToken, request.VerificationToken, FeishuOptionKeys.Category);
        }

        // EncryptKey 仅在提供新值时更新（加密存储，加 ENC: 前缀标记）
        if (!string.IsNullOrWhiteSpace(request.EncryptKey))
        {
            var cipher = "ENC:" + _encryptionService.Encrypt(request.EncryptKey);
            await _unitOfWork.SystemOptions.SetAsync(FeishuOptionKeys.EncryptKey, cipher, FeishuOptionKeys.Category);
        }

        if (request.EncryptEnabled.HasValue)
        {
            await _unitOfWork.SystemOptions.SetAsync(FeishuOptionKeys.EncryptEnabled, request.EncryptEnabled.Value.ToString().ToLowerInvariant(), FeishuOptionKeys.Category);
        }

        if (request.MailboxId.HasValue)
        {
            await _unitOfWork.SystemOptions.SetAsync(FeishuOptionKeys.MailboxId, request.MailboxId.Value.ToString() ?? string.Empty, FeishuOptionKeys.Category);
        }

        if (request.ContactCollectionEnabled.HasValue)
        {
            await _unitOfWork.SystemOptions.SetAsync(FeishuOptionKeys.ContactCollectionEnabled, request.ContactCollectionEnabled.Value.ToString().ToLowerInvariant(), FeishuOptionKeys.Category);
        }

        if (request.AutoReplyEnabled.HasValue)
        {
            await _unitOfWork.SystemOptions.SetAsync(FeishuOptionKeys.AutoReplyEnabled, request.AutoReplyEnabled.Value.ToString().ToLowerInvariant(), FeishuOptionKeys.Category);
        }

        if (request.AutoReplyMessage != null)
        {
            await _unitOfWork.SystemOptions.SetAsync(FeishuOptionKeys.AutoReplyMessage, request.AutoReplyMessage, FeishuOptionKeys.Category);
        }

        if (request.Enabled.HasValue)
        {
            await _unitOfWork.SystemOptions.SetAsync(FeishuOptionKeys.Enabled, request.Enabled.Value.ToString().ToLowerInvariant(), FeishuOptionKeys.Category);
        }

        if (!string.IsNullOrWhiteSpace(request.ReceiveMode))
        {
            await _unitOfWork.SystemOptions.SetAsync(FeishuOptionKeys.ReceiveMode, request.ReceiveMode, FeishuOptionKeys.Category);
        }

        await _unitOfWork.SaveChangesAsync();

        // 刷新内存缓存使新配置立即生效
        await _settingsProvider.RefreshAsync();

        // 触发 Stream 客户端立即重连（如果正在退避等待中）
        _streamConnectionStatus.TriggerReconnect();

        _logger.LogInformation("飞书配置已更新，操作者 UserId={UserId}", _currentUserAccessor.UserId);

        return await GetSettingsAsync();
    }

    public async Task<FeishuStatusDto> GetStatusAsync()
    {
        var opt = _settingsProvider.Current;
        var isStreamMode = string.Equals(opt.ReceiveMode, "Stream", StringComparison.OrdinalIgnoreCase);

        bool? tokenValid = null;

        if (isStreamMode)
        {
            // Stream 模式：不检查 Token，只通过 Stream 连接状态反映健康度
            tokenValid = null;
        }
        else if (opt.Enabled && opt.IsConfigured)
        {
            // Http 模式：尝试获取 access_token 以验证配置是否有效 —— 3 秒超时，避免阻塞页面加载
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(opt.StatusCheckTimeoutSeconds));
                var tokenTask = _accessTokenService.GetAccessTokenAsync(cts.Token);
                var delayTask = Task.Delay(Timeout.Infinite, cts.Token);
                var completed = await Task.WhenAny(tokenTask, delayTask);
                if (completed == tokenTask)
                    tokenValid = !string.IsNullOrEmpty(await tokenTask);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("飞书 AccessToken 检测超时（>3s），视为无效，不阻塞页面加载");
                tokenValid = false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "飞书 AccessToken 检测失败");
                tokenValid = false;
            }
        }

        return new FeishuStatusDto
        {
            Configured = opt.IsConfigured,
            Enabled = opt.Enabled,
            TokenValid = tokenValid,
            CallbackUrl = BuildCallbackUrl(),
            ReceiveMode = opt.ReceiveMode,
            StreamConnected = isStreamMode && _streamConnectionStatus.IsConnected,
            StreamError = isStreamMode ? _streamConnectionStatus.LastError : null
        };
    }

    public async Task<FeishuReplyResult> ReplyAsync(FeishuReplyRequest request, int userId)
    {
        _logger.LogInformation("飞书客服回复开始：ConversationId={ConversationId}, UserId={UserId}, HasText={HasText}, AttachmentCount={AttachmentCount}",
            request.ConversationId, userId, !string.IsNullOrWhiteSpace(request.Content),
            request.AttachmentIds?.Count ?? 0);

        var opt = _settingsProvider.Current;
        if (!opt.Enabled || !opt.IsConfigured)
        {
            _logger.LogWarning("飞书回复被拦截：接入未启用或未配置（Enabled={Enabled}, IsConfigured={IsConfigured}）",
                opt.Enabled, opt.IsConfigured);
            return new FeishuReplyResult
            {
                Sent = false,
                ErrorMessage = "飞书接入未启用或未配置"
            };
        }

        // 文本和附件不能同时为空
        var hasText = !string.IsNullOrWhiteSpace(request.Content);
        var hasAttachments = request.AttachmentIds != null && request.AttachmentIds.Count > 0;
        if (!hasText && !hasAttachments)
        {
            _logger.LogWarning("飞书回复被拦截：文本和附件同时为空（ConversationId={ConversationId}）", request.ConversationId);
            return new FeishuReplyResult
            {
                Sent = false,
                ErrorMessage = "回复内容和附件不能同时为空"
            };
        }

        // 查找会话获取客户 OpenId
        var conversation = await _unitOfWork.Conversations.GetByIdTrackedAsync(request.ConversationId);
        if (conversation == null)
        {
            _logger.LogWarning("飞书回复被拦截：会话不存在（ConversationId={ConversationId}）", request.ConversationId);
            return new FeishuReplyResult
            {
                Sent = false,
                ErrorMessage = $"会话不存在：{request.ConversationId}"
            };
        }

        if (conversation.SourceType != SourceType.Feishu)
        {
            _logger.LogWarning("飞书回复被拦截：会话类型不匹配（ConversationId={ConversationId}, SourceType={SourceType}）",
                request.ConversationId, conversation.SourceType);
            return new FeishuReplyResult
            {
                Sent = false,
                ErrorMessage = "该会话不是飞书会话，无法通过飞书渠道回复"
            };
        }

        // 从最近一条客户线程中提取 OpenId（存储在 Thread.To 字段，飞书回复线程 To=openId）
        // 飞书无 48 小时客服消息窗口限制，无需时间窗口校验
        var customerThread = await _unitOfWork.Threads.GetQueryable()
            .Where(t => t.ConversationId == request.ConversationId
                && t.SourceType == SourceType.Feishu
                && t.Type == ThreadType.Customer
                && t.State != ThreadState.Deleted)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        string? openId = null;
        if (customerThread != null)
        {
            // 客户线程 From 存储的是 open_id
            openId = customerThread.From;
        }

        if (string.IsNullOrEmpty(openId))
        {
            _logger.LogWarning("飞书回复被拦截：无法获取客户 OpenId（ConversationId={ConversationId}, FoundCustomerThread={FoundCustomerThread}）",
                request.ConversationId, customerThread != null);
            return new FeishuReplyResult
            {
                Sent = false,
                ErrorMessage = "无法获取客户 OpenId"
            };
        }

        _logger.LogInformation("飞书回复 OpenId 解析成功：ConversationId={ConversationId}, OpenId={OpenId}",
            request.ConversationId, openId);

        // 预加载附件（tracked，稍后需关联 ThreadId）
        List<Attachment> attachments = hasAttachments
            ? await _unitOfWork.Attachments.GetByIdsTrackedAsync(request.AttachmentIds!)
            : new List<Attachment>();

        // 1. 发送文本消息（如果有）
        if (hasText)
        {
            // 飞书文本消息 content 格式：{ "text": "xxx" }
            var textContent = JsonSerializer.Serialize(new { text = request.Content });
            var sentMessageId = await _feishuClient.SendMessageAsync(openId, "text", textContent);
            if (string.IsNullOrEmpty(sentMessageId))
            {
                _logger.LogError("飞书文本消息发送失败：ConversationId={ConversationId}, OpenId={OpenId}",
                    request.ConversationId, openId);
                return new FeishuReplyResult
                {
                    Sent = false,
                    ErrorMessage = "消息发送失败，请检查 access_token 是否有效"
                };
            }
        }

        // 2. 逐个上传并发送附件（图片/文件）
        //    飞书每条消息只能包含一种媒体类型，因此每个附件单独发送一条消息。
        var mediaErrors = new List<string>();
        foreach (var attachment in attachments)
        {
            // 读取附件文件内容（路径遍历校验，与 WeChatService 对齐）
            var cleanFileDir = (attachment.FileDir ?? string.Empty).Trim().Replace('\\', '/');
            var cleanStoredName = Path.GetFileName(attachment.StoredFileName ?? attachment.FileName) ?? string.Empty;
            var fullPath = Path.GetFullPath(Path.Combine(_attachmentBasePath, cleanFileDir, cleanStoredName));
            var normalizedBasePath = Path.GetFullPath(_attachmentBasePath);
            if (!fullPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("飞书附件路径遍历拦截: FileDir={FileDir}, FileName={FileName}",
                    attachment.FileDir, attachment.FileName);
                mediaErrors.Add($"附件 {attachment.FileName} 路径无效");
                continue;
            }
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("飞书回复附件文件不存在：AttachmentId={AttachmentId}, Path={Path}", attachment.Id, fullPath);
                mediaErrors.Add($"附件 {attachment.FileName} 文件不存在");
                continue;
            }

            byte[] fileData;
            try
            {
                fileData = await File.ReadAllBytesAsync(fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取附件文件失败：AttachmentId={AttachmentId}", attachment.Id);
                mediaErrors.Add($"附件 {attachment.FileName} 读取失败");
                continue;
            }

            // 图片走 UploadImageAsync，其他文件走 UploadFileAsync
            string? sentId = null;
            if (attachment.MimeType != null && attachment.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                var imageKey = await _feishuClient.UploadImageAsync(fileData, attachment.FileName);
                if (!string.IsNullOrEmpty(imageKey))
                {
                    var imageContent = JsonSerializer.Serialize(new { image_key = imageKey });
                    sentId = await _feishuClient.SendMessageAsync(openId, "image", imageContent);
                }
            }
            else
            {
                var fileType = ClassifyFeishuFileType(attachment.FileName);
                var fileKey = await _feishuClient.UploadFileAsync(fileData, attachment.FileName, fileType);
                if (!string.IsNullOrEmpty(fileKey))
                {
                    var fileContent = JsonSerializer.Serialize(new { file_key = fileKey });
                    sentId = await _feishuClient.SendMessageAsync(openId, "file", fileContent);
                }
            }

            if (string.IsNullOrEmpty(sentId))
            {
                mediaErrors.Add($"附件 {attachment.FileName} 发送失败");
            }
        }

        // 文本无 + 附件全失败 → 短路返回，不创建空线程
        if (!hasText && mediaErrors.Count == attachments.Count)
        {
            return new FeishuReplyResult
            {
                Sent = false,
                ErrorMessage = $"所有附件发送失败：{string.Join("；", mediaErrors)}"
            };
        }

        // 3. 创建回复线程（记录文本+附件）
        ConversationThread? thread = null;
        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                var user = await _unitOfWork.Users.GetByIdAsync(userId);
                var now = DateTime.UtcNow;
                var userFullName = user?.GetFullName();
                var userEmail = user?.Email;

                // 构建线程 Body：文本 + 附件占位（前端渲染时显示附件列表）
                var body = hasText ? (request.Content ?? string.Empty) : string.Empty;
                if (attachments.Count > 0)
                {
                    var mediaList = string.Join("", attachments.Select(a =>
                        $"<div class=\"feishu-media-attachment\" data-attachment-id=\"{a.Id}\">" +
                        $"<div class=\"feishu-media-attachment-inner\">" +
                        $"<span class=\"feishu-media-attachment-icon\">📎</span>" +
                        $"<span class=\"feishu-media-attachment-name\">{System.Net.WebUtility.HtmlEncode(a.FileName)}</span>" +
                        $"</div>" +
                        $"</div>"));
                    body = string.IsNullOrEmpty(body) ? mediaList : $"<div class=\"feishu-text\">{System.Net.WebUtility.HtmlEncode(body)}</div>{mediaList}";
                }

                thread = new ConversationThread
                {
                    ConversationId = conversation.Id,
                    Type = ThreadType.Message,
                    State = ThreadState.Active,
                    Body = body,
                    Subject = conversation.Subject,
                    From = !string.IsNullOrEmpty(userFullName) ? userFullName : (!string.IsNullOrEmpty(userEmail) ? userEmail : $"user:{userId}"),
                    To = openId,
                    MessageId = $"fs_reply_{now.Ticks}_{request.ConversationId}",
                    IsInternal = false,
                    HasAttachments = attachments.Count > 0,
                    IsFirst = false,
                    SourceVia = SourceVia.User,
                    SourceType = SourceType.Feishu,
                    UserId = userId,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                await _unitOfWork.Threads.AddAsync(thread);

                // 关联附件到线程
                foreach (var attachment in attachments)
                {
                    attachment.ThreadId = thread.Id;
                    thread.Attachments.Add(attachment);
                }

                conversation.LastReplyAt = now;
                conversation.LastReplyFrom = SourceVia.User;
                conversation.UpdatedAt = now;
                conversation.SetPreview(hasText ? (request.Content ?? string.Empty) : $"[附件 x{attachments.Count}]");
                conversation.ThreadsCount++;

                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("飞书客服消息回复成功：ConversationId={ConversationId}, ThreadId={ThreadId}, Attachments={AttachmentCount}",
                    conversation.Id, thread.Id, attachments.Count);
            }
            catch (Exception ex)
            {
                await transaction.SafeRollbackAsync();
                _logger.LogError(ex, "飞书回复线程创建失败：ConversationId={ConversationId}", request.ConversationId);
                return new FeishuReplyResult
                {
                    Sent = true, // 消息已发送给客户
                    ThreadCreated = false, // 线程记录创建失败
                    ErrorMessage = "消息已发送，但会话线程记录失败"
                };
            }
        }

        // 4. 显式加载 User 导航属性并推送 SignalR 通知（多用户实时刷新）
        if (thread != null && thread.UserId.HasValue && thread.User == null)
        {
            thread.User = await _unitOfWork.Users.GetByIdAsync(thread.UserId.Value);
        }

        if (thread != null)
        {
            try
            {
                var threadResponse = thread.ToResponse();
                await _notificationPublisher.NotifyMailboxAsync(
                    conversation.MailboxId.ToString(),
                    NoneadCSSEvents.ReplyCreated,
                    threadResponse);
            }
            catch (Exception ex)
            {
                // 通知失败不影响主流程
                _logger.LogWarning(ex, "飞书回复 SignalR 通知推送失败：ThreadId={ThreadId}", thread.Id);
            }
        }

        return new FeishuReplyResult
        {
            Sent = true,
            ThreadCreated = thread != null,
            ThreadId = thread?.Id,
            ErrorMessage = mediaErrors.Count > 0 ? string.Join("；", mediaErrors) : null
        };
    }

    /// <summary>
    /// 根据文件扩展名推断飞书文件类型。
    /// 飞书 UploadFile 支持的 fileType：opus / mp4 / pdf / doc / xls / ppt / stream
    /// </summary>
    private static string ClassifyFeishuFileType(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        return ext switch
        {
            ".opus" => "opus",
            ".mp4" or ".mov" or ".avi" or ".mkv" => "mp4",
            ".pdf" => "pdf",
            ".doc" or ".docx" => "doc",
            ".xls" or ".xlsx" => "xls",
            ".ppt" or ".pptx" => "ppt",
            _ => "stream"
        };
    }

    private string BuildCallbackUrl()
    {
        var baseUri = _baseUrl.TrimEnd('/');
        if (baseUri.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || baseUri.Contains("127.0.0.1"))
        {
            _logger.LogWarning("飞书回调 URL 使用了 localhost/127.0.0.1，飞书服务器无法访问本地地址。请在配置 App:BaseUrl 或 App:FrontendUrl 中设置公网可访问地址（当前值={BaseUri}）", baseUri);
        }
        var path = _settingsProvider.Current.CallbackPath;
        if (string.IsNullOrEmpty(path)) path = "/api/feishu";
        if (!path.StartsWith('/')) path = "/" + path;
        return baseUri + path;
    }
}
