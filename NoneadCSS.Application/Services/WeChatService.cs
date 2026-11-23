using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
/// 微信服务号管理服务实现 - 配置读写、连接状态查询、客服消息回复。
/// 配置持久化到 SystemOptions 数据库表（敏感字段加密），通过 IWeChatSettingsProvider 刷新内存缓存。
/// </summary>
public class WeChatService : IWeChatService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEncryptionService _encryptionService;
    private readonly IWeChatSettingsProvider _settingsProvider;
    private readonly IWeChatAccessTokenService _accessTokenService;
    private readonly IWeChatClient _weChatClient;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<WeChatService> _logger;
    private readonly string _baseUrl;
    private readonly string _attachmentBasePath;
    private readonly IOptions<IngestionMatchOptions> _ingestionMatchOptions;

    public WeChatService(
        IUnitOfWork unitOfWork,
        IEncryptionService encryptionService,
        IWeChatSettingsProvider settingsProvider,
        IWeChatAccessTokenService accessTokenService,
        IWeChatClient weChatClient,
        ICurrentUserAccessor currentUserAccessor,
        INotificationPublisher notificationPublisher,
        ILogger<WeChatService> logger,
        IConfiguration configuration,
        IOptions<IngestionMatchOptions> ingestionMatchOptions,
        IHostEnvironment env)
    {
        _unitOfWork = unitOfWork;
        _encryptionService = encryptionService;
        _settingsProvider = settingsProvider;
        _accessTokenService = accessTokenService;
        _weChatClient = weChatClient;
        _currentUserAccessor = currentUserAccessor;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
        _baseUrl = configuration["App:BaseUrl"] ?? configuration["App:FrontendUrl"] ?? "http://localhost:5009";
        // 附件根目录统一解析（T000112）：绝对路径原样、相对/缺失锚定 ContentRoot，与上传侧一致
        _attachmentBasePath = AttachmentPathResolver.ResolveBasePath(
            configuration["Attachment:UploadPath"], env.ContentRootPath);
        _ingestionMatchOptions = ingestionMatchOptions;
    }

    public async Task<WeChatSettingsDto> GetSettingsAsync()
    {
        var opt = _settingsProvider.Current;

        return new WeChatSettingsDto
        {
            AppId = opt.AppId,
            HasAppSecret = !string.IsNullOrWhiteSpace(opt.AppSecret),
            HasToken = !string.IsNullOrWhiteSpace(opt.Token),
            HasEncodingAESKey = !string.IsNullOrWhiteSpace(opt.EncodingAESKey),
            EncryptMode = opt.EncryptMode,
            MailboxId = opt.MailboxId,
            ContactCollectionEnabled = opt.ContactCollectionEnabled,
            AutoReplyEnabled = opt.AutoReplyEnabled,
            AutoReplyMessage = opt.AutoReplyMessage,
            Enabled = opt.Enabled,
            CallbackUrl = BuildCallbackUrl()
        };
    }

    public async Task<WeChatSettingsDto> UpdateSettingsAsync(UpdateWeChatSettingsRequest request)
    {
        // 持久化到 SystemOptions（敏感字段加密）
        if (!string.IsNullOrWhiteSpace(request.AppId))
        {
            await _unitOfWork.SystemOptions.SetAsync(WeChatOptionKeys.AppId, request.AppId, WeChatOptionKeys.Category);
        }

        // AppSecret 仅在提供新值时更新（加密存储，加 ENC: 前缀标记）
        // 加密存储提升安全性，读取时由 WeChatSettingsProvider 检测前缀并解密
        if (!string.IsNullOrWhiteSpace(request.AppSecret))
        {
            var encrypted = "ENC:" + _encryptionService.Encrypt(request.AppSecret);
            await _unitOfWork.SystemOptions.SetAsync(WeChatOptionKeys.AppSecret, encrypted, WeChatOptionKeys.Category);
        }

        // Token 仅在提供新值时更新（明文存储）
        // 原因：Token 是签名盐值，需与微信公众平台后台明文配置一致；加密会导致
        // 开发环境 Encryption:Key 变化后无法解密，进而用密文当 Token 验签导致签名失败
        if (!string.IsNullOrWhiteSpace(request.Token))
        {
            await _unitOfWork.SystemOptions.SetAsync(WeChatOptionKeys.Token, request.Token, WeChatOptionKeys.Category);
        }

        // EncodingAESKey 仅在提供新值时更新（加密存储，加 ENC: 前缀标记）
        // 安全模式下用于消息解密，加密存储提升安全性
        if (!string.IsNullOrWhiteSpace(request.EncodingAESKey))
        {
            var encrypted = "ENC:" + _encryptionService.Encrypt(request.EncodingAESKey);
            await _unitOfWork.SystemOptions.SetAsync(WeChatOptionKeys.EncodingAESKey, encrypted, WeChatOptionKeys.Category);
        }

        await _unitOfWork.SystemOptions.SetAsync(WeChatOptionKeys.EncryptMode, request.EncryptMode, WeChatOptionKeys.Category);
        await _unitOfWork.SystemOptions.SetAsync(WeChatOptionKeys.MailboxId, request.MailboxId?.ToString() ?? string.Empty, WeChatOptionKeys.Category);
        await _unitOfWork.SystemOptions.SetAsync(WeChatOptionKeys.ContactCollectionEnabled, request.ContactCollectionEnabled.ToString().ToLowerInvariant(), WeChatOptionKeys.Category);
        await _unitOfWork.SystemOptions.SetAsync(WeChatOptionKeys.AutoReplyEnabled, request.AutoReplyEnabled.ToString().ToLowerInvariant(), WeChatOptionKeys.Category);
        await _unitOfWork.SystemOptions.SetAsync(WeChatOptionKeys.AutoReplyMessage, request.AutoReplyMessage ?? string.Empty, WeChatOptionKeys.Category);
        await _unitOfWork.SystemOptions.SetAsync(WeChatOptionKeys.Enabled, request.Enabled.ToString().ToLowerInvariant(), WeChatOptionKeys.Category);

        await _unitOfWork.SaveChangesAsync();

        // 刷新内存缓存使新配置立即生效
        await _settingsProvider.RefreshAsync();

        _logger.LogInformation("微信配置已更新，操作者 UserId={UserId}", _currentUserAccessor.UserId);

        return await GetSettingsAsync();
    }

    public async Task<WeChatStatusDto> GetStatusAsync()
    {
        var opt = _settingsProvider.Current;

        bool tokenValid = false;
        if (opt.Enabled && opt.IsConfigured)
        {
            // 尝试获取 access_token 以验证配置是否有效（不强制刷新）—— 10 秒超时，避免阻塞页面加载
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var tokenTask = _accessTokenService.GetAccessTokenAsync(cts.Token);
                var delayTask = Task.Delay(Timeout.Infinite, cts.Token);
                var completed = await Task.WhenAny(tokenTask, delayTask);
                if (completed == tokenTask)
                    tokenValid = !string.IsNullOrEmpty(await tokenTask);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("微信 AccessToken 检测超时（>10s），视为无效，不阻塞页面加载");
                tokenValid = false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "微信 AccessToken 检测失败");
                tokenValid = false;
            }
        }

        return new WeChatStatusDto
        {
            Configured = opt.IsConfigured,
            Enabled = opt.Enabled,
            TokenValid = tokenValid,
            CallbackUrl = BuildCallbackUrl()
        };
    }

    /// <summary>从 User 实体构造前端展示的显示名称，避免暴露邮箱。优先 FirstName + LastName，回退到 user:userId。</summary>
    private static string BuildDisplayName(User? user, int userId)
    {
        if (user == null) return $"user:{userId}";
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(user.FirstName)) parts.Add(user.FirstName.Trim());
        if (!string.IsNullOrWhiteSpace(user.LastName)) parts.Add(user.LastName.Trim());
        if (parts.Count > 0) return string.Join(" ", parts);
        return $"user:{userId}";
    }

    public async Task<WeChatReplyResult> ReplyAsync(WeChatReplyRequest request, int userId)
    {
        var opt = _settingsProvider.Current;
        if (!opt.Enabled || !opt.IsConfigured)
        {
            return new WeChatReplyResult
            {
                Sent = false,
                ErrorMessage = "微信接入未启用或未配置"
            };
        }

        // 文本和附件不能同时为空
        var hasText = !string.IsNullOrWhiteSpace(request.Content);
        var hasAttachments = request.AttachmentIds != null && request.AttachmentIds.Count > 0;
        if (!hasText && !hasAttachments)
        {
            return new WeChatReplyResult
            {
                Sent = false,
                ErrorMessage = "回复内容和附件不能同时为空"
            };
        }

        // 查找会话获取客户 OpenId
        var conversation = await _unitOfWork.Conversations.GetByIdTrackedAsync(request.ConversationId);
        if (conversation == null)
        {
            return new WeChatReplyResult
            {
                Sent = false,
                ErrorMessage = $"会话不存在：{request.ConversationId}"
            };
        }

        if (conversation.SourceType != SourceType.WeChat)
        {
            return new WeChatReplyResult
            {
                Sent = false,
                ErrorMessage = "该会话不是微信会话，无法通过微信渠道回复"
            };
        }

        // 从最近一条客户线程中提取 OpenId（存储在 Thread.From 字段）
        // 同时用于 48h 窗口校验
        var customerThread = await _unitOfWork.Threads.GetQueryable()
            .Where(t => t.ConversationId == request.ConversationId
                && t.SourceType == SourceType.WeChat
                && t.Type == ThreadType.Customer)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        // 优先从 Conversation.Customer.ChannelId 获取 OpenId（更直接可靠）
        // 回退到客户线程的 From 字段（历史兼容）
        var openId = conversation.Customer?.ChannelId;
        if (string.IsNullOrEmpty(openId))
        {
            // Customer 导航属性可能未加载（取决于 Includes），显式查询
            if (conversation.CustomerId > 0)
            {
                var customer = await _unitOfWork.Customers.GetByIdAsync(conversation.CustomerId.Value);
                openId = customer?.ChannelId;
            }
            if (string.IsNullOrEmpty(openId))
            {
                openId = customerThread?.From;
            }
        }

        if (string.IsNullOrEmpty(openId))
        {
            return new WeChatReplyResult
            {
                Sent = false,
                ErrorMessage = "无法获取客户 OpenId"
            };
        }

        // 后端入口校验 48 小时客服消息窗口：避免前端绕过 UI 触发发送
        // 最近一次客户线程在 48h 前的，直接拒绝，引导走模板/订阅消息
        var nowUtc = DateTime.UtcNow;
        var lastCustomerAt = customerThread?.CreatedAt ?? conversation.LastReplyAt ?? conversation.CreatedAt;
        if ((nowUtc - lastCustomerAt) > TimeSpan.FromHours(_ingestionMatchOptions.Value.WeChatMatchWindowHours))
        {
            return new WeChatReplyResult
            {
                Sent = false,
                ErrorMessage = $"客户最近一次互动已超过 48 小时（{lastCustomerAt:yyyy-MM-dd HH:mm}），客服消息窗口已关闭。请使用模板消息或订阅消息联系客户。"
            };
        }

        // 预加载附件（tracked，稍后需关联 ThreadId）
        List<Attachment> attachments = hasAttachments
            ? await _unitOfWork.Attachments.GetByIdsTrackedAsync(request.AttachmentIds!)
            : new List<Attachment>();

        // 1. 发送文本消息（如果有）
        if (hasText)
        {
            var textSent = await _weChatClient.SendTextMessageAsync(openId, request.Content);
            if (!textSent)
            {
                _logger.LogError("微信客服文本消息发送失败：ConversationId={ConversationId}, OpenId={OpenId}",
                    request.ConversationId, openId);
                return new WeChatReplyResult
                {
                    Sent = false,
                    ErrorMessage = "客服消息发送失败，请检查 access_token 是否有效或客户是否在 48 小时客服消息窗口内"
                };
            }
        }

        // 2. 逐个上传并发送附件（图片/语音/视频）
        //    微信客服消息每条只能包含一种媒体类型，因此每个附件单独发送一条消息。
        var mediaErrors = new List<string>();
        foreach (var attachment in attachments)
        {
            var (mediaType, sendFunc) = ClassifyMediaAttachment(attachment.MimeType, openId, _weChatClient);
            if (mediaType == null)
            {
                mediaErrors.Add($"附件 {attachment.FileName} 类型不支持（{attachment.MimeType}）");
                continue;
            }

            // 读取附件文件内容（路径遍历校验，与 EmailService.GetAttachmentFilePath 对齐）
            var cleanFileDir = (attachment.FileDir ?? string.Empty).Trim().Replace('\\', '/');
            var cleanStoredName = Path.GetFileName(attachment.StoredFileName ?? attachment.FileName) ?? string.Empty;
            var fullPath = Path.GetFullPath(Path.Combine(_attachmentBasePath, cleanFileDir, cleanStoredName));
            var normalizedBasePath = Path.GetFullPath(_attachmentBasePath);
            if (!fullPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("微信附件路径遍历拦截: FileDir={FileDir}, FileName={FileName}",
                    attachment.FileDir, attachment.FileName);
                mediaErrors.Add($"附件 {attachment.FileName} 路径无效");
                continue;
            }
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("微信回复附件文件不存在：AttachmentId={AttachmentId}, Path={Path}", attachment.Id, fullPath);
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

            // 上传到微信临时素材
            var mediaId = await _weChatClient.UploadMediaAsync(fileData, attachment.FileName, attachment.MimeType, mediaType);
            if (string.IsNullOrEmpty(mediaId))
            {
                mediaErrors.Add($"附件 {attachment.FileName} 上传微信失败");
                continue;
            }

            // 发送对应类型的客服消息
            var mediaSent = await sendFunc(mediaId);
            if (!mediaSent)
            {
                mediaErrors.Add($"附件 {attachment.FileName} 发送失败");
            }
        }

        // 文本无 + 附件全失败 → 短路返回，不创建空线程
        if (!hasText && mediaErrors.Count == attachments.Count)
        {
            return new WeChatReplyResult
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

                // 构建线程 Body：文本 + 附件占位（前端渲染时显示附件列表）
                var body = hasText ? request.Content : string.Empty;
                if (attachments.Count > 0)
                {
                    var mediaList = string.Join("", attachments.Select(a =>
                        $"<div class=\"wechat-media-attachment\" data-attachment-id=\"{a.Id}\">" +
                        $"<div class=\"wechat-media-attachment-inner\">" +
                        $"<span class=\"wechat-media-attachment-icon\">📎</span>" +
                        $"<span class=\"wechat-media-attachment-name\">{System.Net.WebUtility.HtmlEncode(a.FileName)}</span>" +
                        $"</div>" +
                        $"</div>"));
                    body = string.IsNullOrEmpty(body) ? mediaList : $"<div class=\"wechat-text\">{System.Net.WebUtility.HtmlEncode(body)}</div>{mediaList}";
                }

                thread = new ConversationThread
                {
                    ConversationId = conversation.Id,
                    Type = ThreadType.Message,
                    State = ThreadState.Active,
                    Body = body,
                    Subject = conversation.Subject,
                    From = BuildDisplayName(user, userId),
                    To = openId,
                    MessageId = $"wx_reply_{now.Ticks}_{request.ConversationId}",
                    IsInternal = false,
                    HasAttachments = attachments.Count > 0,
                    IsFirst = false,
                    SourceVia = SourceVia.User,
                    SourceType = SourceType.WeChat,
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
                conversation.SetPreview(hasText ? request.Content : $"[附件 x{attachments.Count}]");
                conversation.ThreadsCount++;

                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("微信客服消息回复成功：ConversationId={ConversationId}, ThreadId={ThreadId}, Attachments={AttachmentCount}",
                    conversation.Id, thread.Id, attachments.Count);
            }
            catch (Exception ex)
            {
                await transaction.SafeRollbackAsync();
                _logger.LogError(ex, "微信回复线程创建失败：ConversationId={ConversationId}", request.ConversationId);
                return new WeChatReplyResult
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
                _logger.LogWarning(ex, "微信回复 SignalR 通知推送失败：ThreadId={ThreadId}", thread.Id);
            }
        }

        return new WeChatReplyResult
        {
            Sent = true,
            ThreadCreated = thread != null,
            ThreadId = thread?.Id,
            ErrorMessage = mediaErrors.Count > 0 ? string.Join("；", mediaErrors) : null
        };
    }

    /// <summary>
    /// 根据附件 MIME 类型分类为微信素材类型，并返回对应的发送函数。
    /// 微信仅支持 image、voice、video 三种客服媒体消息类型。
    /// </summary>
    private static (string? mediaType, Func<string, Task<bool>> sendFunc) ClassifyMediaAttachment(
        string mimeType, string openId, IWeChatClient client)
    {
        if (string.IsNullOrEmpty(mimeType))
        {
            return (null, _ => Task.FromResult(false));
        }

        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return ("image", mediaId => client.SendImageMessageAsync(openId, mediaId));
        }

        if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return ("voice", mediaId => client.SendVoiceMessageAsync(openId, mediaId));
        }

        if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return ("video", mediaId => client.SendVideoMessageAsync(openId, mediaId));
        }

        return (null, _ => Task.FromResult(false));
    }

    private string BuildCallbackUrl()
    {
        // 优先使用 WeChat:CallbackBaseUrl（独立回调域名/代理），未配置时回退到 App:BaseUrl/App:FrontendUrl
        var callbackBaseUrl = _settingsProvider.Current.CallbackBaseUrl;
        var baseUri = (!string.IsNullOrWhiteSpace(callbackBaseUrl)
            ? callbackBaseUrl
            : _baseUrl).TrimEnd('/');

        // 本地开发默认走 localhost/127.0.0.1，这是合理的；仅在 Debug 级别记录，避免 Info/Warning 每
        // 次打开设置页就刷一条。发布环境应配置 WeChat:CallbackBaseUrl 或 App:BaseUrl / App:FrontendUrl 为公网地址。
        if (baseUri.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || baseUri.Contains("127.0.0.1"))
        {
            _logger.LogDebug("微信回调 URL 当前为本地地址（{BaseUri}），微信服务器无法回调。生产环境请设置 WeChat:CallbackBaseUrl 或 App:BaseUrl 为公网可访问地址。", baseUri);
        }
        var path = _settingsProvider.Current.CallbackPath;
        if (string.IsNullOrEmpty(path)) path = "/api/wechat";
        if (!path.StartsWith('/')) path = "/" + path;
        return baseUri + path;
    }

    /// <summary>
    /// 发送模板消息到指定会话的客户。
    /// </summary>
    public async Task<WeChatTemplateResult> SendTemplateMessageAsync(WeChatTemplateRequest request, int userId)
    {
        var opt = _settingsProvider.Current;
        if (!opt.Enabled || !opt.IsConfigured)
        {
            return new WeChatTemplateResult { Sent = false, ErrorMessage = "微信接入未启用或未配置" };
        }

        // 从会话中获取客户 OpenId
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(request.ConversationId);
        if (conversation == null)
        {
            return new WeChatTemplateResult { Sent = false, ErrorMessage = $"会话不存在：{request.ConversationId}" };
        }

        if (conversation.SourceType != SourceType.WeChat)
        {
            return new WeChatTemplateResult { Sent = false, ErrorMessage = "该会话不是微信会话" };
        }

        // 从会话客户获取 OpenId（优先 Customer.ChannelId，回退客户线程 From）
        var openId = conversation.Customer?.ChannelId;
        if (string.IsNullOrEmpty(openId))
        {
            if (conversation.CustomerId > 0)
            {
                var customer = await _unitOfWork.Customers.GetByIdAsync(conversation.CustomerId.Value);
                openId = customer?.ChannelId;
            }
        }
        if (string.IsNullOrEmpty(openId))
        {
            var customerThread = await _unitOfWork.Threads.GetQueryable()
                .Where(t => t.ConversationId == request.ConversationId
                    && t.SourceType == SourceType.WeChat
                    && t.Type == ThreadType.Customer)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();
            openId = customerThread?.From;
        }

        if (string.IsNullOrEmpty(openId))
        {
            return new WeChatTemplateResult { Sent = false, ErrorMessage = "无法获取客户 OpenId" };
        }

        // 解析模板数据 JSON（必须是非空的合法 JSON 对象/字典；空字符串/null 直接报错）
        if (string.IsNullOrWhiteSpace(request.DataJson))
        {
            return new WeChatTemplateResult { Sent = false, ErrorMessage = "模板数据 DataJson 不能为空" };
        }
        System.Text.Json.JsonElement data;
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(request.DataJson);
            data = doc.RootElement.Clone(); // 脱离 JsonDocument 生命周期，避免后续 ObjectDisposedException
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "微信模板消息 DataJson 解析失败：ConversationId={ConversationId}, RawJson={RawJson}",
                request.ConversationId, request.DataJson.Length > 200 ? request.DataJson[..200] : request.DataJson);
            return new WeChatTemplateResult { Sent = false, ErrorMessage = "模板数据 JSON 格式无效" };
        }
        // 微信模板消息的 data 字段必须是 { key: {value,color} } 结构的 object
        if (data.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return new WeChatTemplateResult { Sent = false, ErrorMessage = "模板数据必须是 JSON 对象（形如 {\"thing1\":{\"value\":\"xxx\"}}）" };
        }

        var sent = await _weChatClient.SendTemplateMessageAsync(openId, request.TemplateId, data, request.Url);
        if (!sent)
        {
            _logger.LogError("微信模板消息发送失败：ConversationId={ConversationId}, TemplateId={TemplateId}",
                request.ConversationId, request.TemplateId);
            return new WeChatTemplateResult { Sent = false, ErrorMessage = "模板消息发送失败，请检查模板 ID 和 access_token。注意：新注册服务号可能已不支持模板消息，请改用订阅通知。" };
        }

        _logger.LogInformation("微信模板消息发送成功：ConversationId={ConversationId}, TemplateId={TemplateId}, UserId={UserId}",
            request.ConversationId, request.TemplateId, userId);

        return new WeChatTemplateResult { Sent = true };
    }

    /// <summary>
    /// 发送订阅通知到指定会话的客户（新版，替代旧版模板消息）。
    /// </summary>
    public async Task<WeChatTemplateResult> SendSubscribeMessageAsync(WeChatSubscribeRequest request, int userId)
    {
        var opt = _settingsProvider.Current;
        if (!opt.Enabled || !opt.IsConfigured)
        {
            return new WeChatTemplateResult { Sent = false, ErrorMessage = "微信接入未启用或未配置" };
        }

        var conversation = await _unitOfWork.Conversations.GetByIdAsync(request.ConversationId);
        if (conversation == null)
        {
            return new WeChatTemplateResult { Sent = false, ErrorMessage = $"会话不存在：{request.ConversationId}" };
        }

        if (conversation.SourceType != SourceType.WeChat)
        {
            return new WeChatTemplateResult { Sent = false, ErrorMessage = "该会话不是微信会话" };
        }

        // 从会话客户获取 OpenId
        var openId = conversation.Customer?.ChannelId;
        if (string.IsNullOrEmpty(openId))
        {
            if (conversation.CustomerId > 0)
            {
                var customer = await _unitOfWork.Customers.GetByIdAsync(conversation.CustomerId.Value);
                openId = customer?.ChannelId;
            }
        }
        if (string.IsNullOrEmpty(openId))
        {
            var customerThread = await _unitOfWork.Threads.GetQueryable()
                .Where(t => t.ConversationId == request.ConversationId
                    && t.SourceType == SourceType.WeChat
                    && t.Type == ThreadType.Customer)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();
            openId = customerThread?.From;
        }

        if (string.IsNullOrEmpty(openId))
        {
            return new WeChatTemplateResult { Sent = false, ErrorMessage = "无法获取客户 OpenId" };
        }

        // 解析订阅通知模板数据 JSON（同模板消息要求：非空、合法 JSON、类型为 object
        if (string.IsNullOrWhiteSpace(request.DataJson))
        {
            return new WeChatTemplateResult { Sent = false, ErrorMessage = "订阅通知数据 DataJson 不能为空" };
        }
        System.Text.Json.JsonElement data;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(request.DataJson);
            data = doc.RootElement.Clone();
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "微信订阅通知 DataJson 解析失败：ConversationId={ConversationId}, RawJson={RawJson}",
                request.ConversationId, request.DataJson.Length > 200 ? request.DataJson[..200] : request.DataJson);
            return new WeChatTemplateResult { Sent = false, ErrorMessage = "订阅通知数据 JSON 格式无效" };
        }
        if (data.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return new WeChatTemplateResult { Sent = false, ErrorMessage = "订阅通知数据必须是 JSON 对象（形如 {\"thing1\":{\"value\":\"xxx\"}}）" };
        }

        var sent = await _weChatClient.SendSubscribeMessageAsync(openId, request.TemplateId, data, request.Page);
        if (!sent)
        {
            _logger.LogError("微信订阅通知发送失败：ConversationId={ConversationId}, TemplateId={TemplateId}",
                request.ConversationId, request.TemplateId);
            return new WeChatTemplateResult { Sent = false, ErrorMessage = "订阅通知发送失败，请检查模板 ID、access_token 及用户是否已订阅" };
        }

        _logger.LogInformation("微信订阅通知发送成功：ConversationId={ConversationId}, TemplateId={TemplateId}, UserId={UserId}",
            request.ConversationId, request.TemplateId, userId);

        return new WeChatTemplateResult { Sent = true };
    }

    /// <summary>
    /// 创建自定义菜单。
    /// </summary>
    public async Task<WeChatMenuResult> CreateMenuAsync(WeChatMenuRequest request)
    {
        var opt = _settingsProvider.Current;
        if (!opt.Enabled || !opt.IsConfigured)
        {
            return new WeChatMenuResult { Success = false, ErrorMessage = "微信接入未启用或未配置" };
        }

        // 菜单 JSON 必须是非空合法 JSON（根节点必须是 object / button 数组所在对象）
        if (string.IsNullOrWhiteSpace(request.MenuJson))
        {
            return new WeChatMenuResult { Success = false, ErrorMessage = "菜单 JSON 不能为空" };
        }
        System.Text.Json.JsonElement menu;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(request.MenuJson);
            menu = doc.RootElement.Clone();
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "微信自定义菜单 MenuJson 解析失败：RawJson={RawJson}",
                request.MenuJson.Length > 300 ? request.MenuJson[..300] : request.MenuJson);
            return new WeChatMenuResult { Success = false, ErrorMessage = "菜单 JSON 格式无效" };
        }
        if (menu.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return new WeChatMenuResult { Success = false, ErrorMessage = "自定义菜单根节点必须是 JSON 对象（形如 {\"button\":[...]}）" };
        }

        var success = await _weChatClient.CreateMenuAsync(menu);
        return new WeChatMenuResult
        {
            Success = success,
            ErrorMessage = success ? null : "创建菜单失败，请检查 access_token 和菜单格式"
        };
    }

    /// <summary>
    /// 查询当前自定义菜单。
    /// </summary>
    public async Task<string?> GetMenuAsync()
    {
        var opt = _settingsProvider.Current;
        if (!opt.Enabled || !opt.IsConfigured)
        {
            return null;
        }

        var menu = await _weChatClient.GetMenuAsync();
        return menu.HasValue ? menu.Value.GetRawText() : null;
    }

    /// <summary>
    /// 删除自定义菜单。
    /// </summary>
    public async Task<WeChatMenuResult> DeleteMenuAsync()
    {
        var opt = _settingsProvider.Current;
        if (!opt.Enabled || !opt.IsConfigured)
        {
            return new WeChatMenuResult { Success = false, ErrorMessage = "微信接入未启用或未配置" };
        }

        var success = await _weChatClient.DeleteMenuAsync();
        return new WeChatMenuResult
        {
            Success = success,
            ErrorMessage = success ? null : "删除菜单失败"
        };
    }
}
