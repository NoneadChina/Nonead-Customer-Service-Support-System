using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Services;
using ConversationEntity = NoneadCSS.Domain.Entities.Conversation;

namespace NoneadCSS.Application.Services;

/// <summary>
/// iMessage / Apple Messages 管理服务（镜像 WhatsAppService 的结构）。
/// 关键差异（docs/integration/imessage.md）：
/// 1) 一个抽象、两套传输：bridge（BlueBubbles，默认）与 amb_*（Apple Messages for Business，需 Apple/MSP 资格，默认关闭）；
/// 2) AMB 是**会话制**，没有 Meta 那种 24h/48h 时间窗口 ⇒ 绝不能用时间窗口去判"能不能回"，
///    只能判"该会话里有没有客户消息"（永不允许冷启动私聊）；
/// 3) 身份按 (mode, externalId) 作用域，Customers.ChannelId 形如 "bridge:+1555…" / "amb:urn:mbid:…"。
/// </summary>
public class IMessageService : IIMessageService
{
    private const string ChannelName = "imessage";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IEncryptionService _encryptionService;
    private readonly IIMessageSettingsProvider _settingsProvider;
    private readonly IIMessageClient _client;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<IMessageService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly IWebhookService? _webhookService;

    public IMessageService(
        IUnitOfWork unitOfWork,
        IEncryptionService encryptionService,
        IIMessageSettingsProvider settingsProvider,
        IIMessageClient client,
        ICurrentUserAccessor currentUserAccessor,
        INotificationPublisher notificationPublisher,
        ILogger<IMessageService> logger,
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

    /// <inheritdoc />
    public async Task<IMessageSettingsDto> GetSettingsAsync()
    {
        var options = await _settingsProvider.GetCurrentAsync();
        return MapToDto(options);
    }

    /// <inheritdoc />
    public async Task<IMessageSettingsDto> UpdateSettingsAsync(UpdateIMessageSettingsRequest request)
    {
        // 模式白名单：非法值直接拒绝，避免把拼错的 mode 写进库导致两套传输都不生效
        if (!string.IsNullOrWhiteSpace(request.Mode) && !IMessageIdentity.IsModeValid(request.Mode))
        {
            _logger.LogWarning("iMessage 配置更新被拒绝：未知 Mode={Mode}", request.Mode);
            request.Mode = null;
        }

        if (request is { Mode: not null }) await SaveOptionAsync(IMessageOptionKeys.Mode, request.Mode!);
        if (request.Enabled.HasValue) await SaveOptionAsync(IMessageOptionKeys.Enabled, request.Enabled.Value.ToString().ToLowerInvariant());

        // ===== bridge =====
        if (request.BaseUrl is not null) await SaveOptionAsync(IMessageOptionKeys.BaseUrl, request.BaseUrl);
        if (request.AuthStyle is not null) await SaveOptionAsync(IMessageOptionKeys.AuthStyle, request.AuthStyle);
        if (!string.IsNullOrWhiteSpace(request.Password))
            await SaveOptionAsync(IMessageOptionKeys.Password, "ENC:" + _encryptionService.Encrypt(request.Password!));
        // bridge webhook 共享密钥按密文落库：它是入站验签依据，明文入库等于把闸门钥匙摊在配置表里。
        // 空值表示"不修改"（与 Password/ApiKey 的写法一致），因此旧部署不会被这条新键意外清空。
        if (!string.IsNullOrWhiteSpace(request.BridgeWebhookSecret))
            await SaveOptionAsync(IMessageOptionKeys.BridgeWebhookSecret, "ENC:" + _encryptionService.Encrypt(request.BridgeWebhookSecret!));
        if (request.WebhookPath is not null)
        {
            // BlueBubbles 回调路由是编译期常量（IMessageController 的 [HttpPost("bluebubbles/webhook")]），
            // 配置成别的值只会让真实回调 404，因此统一归一化回常量并在漂移时告警。
            const string fixedWebhookPath = "/api/imessage/bluebubbles/webhook";
            if (!string.IsNullOrWhiteSpace(request.WebhookPath) &&
                !string.Equals(request.WebhookPath.Trim(), fixedWebhookPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "iMessage WebhookPath 配置已忽略：回调路由固定为 {Fixed}，提交值为 {Requested}",
                    fixedWebhookPath,
                    request.WebhookPath);
            }

            await SaveOptionAsync(IMessageOptionKeys.WebhookPath, fixedWebhookPath);
        }
        if (request.WebhookBaseUrl is not null) await SaveOptionAsync(IMessageOptionKeys.WebhookBaseUrl, request.WebhookBaseUrl);
        if (request.SendMethod is not null) await SaveOptionAsync(IMessageOptionKeys.SendMethod, request.SendMethod);
        if (request.AllowSmsFallback.HasValue) await SaveOptionAsync(IMessageOptionKeys.AllowSmsFallback, request.AllowSmsFallback.Value.ToString().ToLowerInvariant());
        if (request.AllowList is not null) await SaveOptionAsync(IMessageOptionKeys.AllowList, request.AllowList);
        if (request.RateLimitPerMinute.HasValue) await SaveOptionAsync(IMessageOptionKeys.RateLimitPerMinute, request.RateLimitPerMinute.Value.ToString());
        if (request.VerifyTls.HasValue) await SaveOptionAsync(IMessageOptionKeys.VerifyTls, request.VerifyTls.Value.ToString().ToLowerInvariant());
        if (request.HealthEndpoint is not null) await SaveOptionAsync(IMessageOptionKeys.HealthEndpoint, request.HealthEndpoint);
        if (request.MinServerVersion is not null) await SaveOptionAsync(IMessageOptionKeys.MinServerVersion, request.MinServerVersion);
        if (request.ReadHistoryOnStart.HasValue) await SaveOptionAsync(IMessageOptionKeys.ReadHistoryOnStart, request.ReadHistoryOnStart.Value.ToString().ToLowerInvariant());

        // ===== 会话 / 邮箱 / 自动回复 =====
        if (request.MatchWindowHours.HasValue) await SaveOptionAsync(IMessageOptionKeys.MatchWindowHours, request.MatchWindowHours.Value.ToString());
        if (request.MailboxId.HasValue) await SaveOptionAsync(IMessageOptionKeys.MailboxId, request.MailboxId.Value.ToString());
        if (request.AutoReplyEnabled.HasValue) await SaveOptionAsync(IMessageOptionKeys.AutoReplyEnabled, request.AutoReplyEnabled.Value.ToString().ToLowerInvariant());
        if (request.AutoReplyMessage is not null) await SaveOptionAsync(IMessageOptionKeys.AutoReplyMessage, request.AutoReplyMessage);
        if (request.StatusCheckTimeoutSeconds.HasValue) await SaveOptionAsync(IMessageOptionKeys.StatusCheckTimeoutSeconds, request.StatusCheckTimeoutSeconds.Value.ToString());

        // ===== amb_msp =====
        if (request.MspConnector is not null) await SaveOptionAsync(IMessageOptionKeys.MspConnector, request.MspConnector);
        if (request.ApiBaseUrl is not null) await SaveOptionAsync(IMessageOptionKeys.ApiBaseUrl, request.ApiBaseUrl);
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
            await SaveOptionAsync(IMessageOptionKeys.ApiKey, "ENC:" + _encryptionService.Encrypt(request.ApiKey!));
        if (!string.IsNullOrWhiteSpace(request.WebhookSecret))
            await SaveOptionAsync(IMessageOptionKeys.WebhookSecret, "ENC:" + _encryptionService.Encrypt(request.WebhookSecret!));
        if (request.BusinessIdentifier is not null) await SaveOptionAsync(IMessageOptionKeys.BusinessIdentifier, request.BusinessIdentifier);
        if (request.FallbackChannel is not null) await SaveOptionAsync(IMessageOptionKeys.FallbackChannel, request.FallbackChannel);

        // ===== amb_apple_direct（实验性，默认关闭）=====
        if (request.MspId is not null) await SaveOptionAsync(IMessageOptionKeys.MspId, request.MspId);
        if (!string.IsNullOrWhiteSpace(request.MspSharedSecret))
            await SaveOptionAsync(IMessageOptionKeys.MspSharedSecret, "ENC:" + _encryptionService.Encrypt(request.MspSharedSecret!));
        if (request.TokenTtlSeconds.HasValue)
        {
            var ttl = Math.Clamp(request.TokenTtlSeconds.Value, 60, IMessageJwtSigner.MaxTtlSeconds);
            await SaveOptionAsync(IMessageOptionKeys.TokenTtlSeconds, ttl.ToString());
        }
        if (request.GatewayBaseUrl is not null) await SaveOptionAsync(IMessageOptionKeys.GatewayBaseUrl, request.GatewayBaseUrl);
        if (request.BusinessId is not null) await SaveOptionAsync(IMessageOptionKeys.BusinessId, request.BusinessId);
        if (request.InboundPath is not null) await SaveOptionAsync(IMessageOptionKeys.InboundPath, request.InboundPath);
        if (request.AttachmentKeySize.HasValue) await SaveOptionAsync(IMessageOptionKeys.AttachmentKeySize, request.AttachmentKeySize.Value.ToString());
        if (request.MaxBodyBytes.HasValue) await SaveOptionAsync(IMessageOptionKeys.MaxBodyBytes, request.MaxBodyBytes.Value.ToString());
        if (request.ExperimentalAppleDirect.HasValue)
            await SaveOptionAsync(IMessageOptionKeys.ExperimentalAppleDirect, request.ExperimentalAppleDirect.Value.ToString().ToLowerInvariant());

        await _unitOfWork.SaveChangesAsync();
        await _settingsProvider.RefreshAsync();

        return await GetSettingsAsync();
    }

    /// <inheritdoc />
    public async Task<IMessageStatusDto> GetStatusAsync()
    {
        var options = await _settingsProvider.GetCurrentAsync();
        var status = new IMessageStatusDto
        {
            Configured = options.IsConfigured,
            Enabled = options.Enabled,
            Mode = options.Mode,
            AppleDirectAllowed = options.ExperimentalAppleDirect,
            CallbackUrl = BuildCallbackUrl(options),
        };

        // 最近一条客户消息（用于会话状态提示；AMB 没有 Apple 侧的时间窗口，这里只反映本系统会话）
        var lastCustomerThread = await _unitOfWork.Threads.FindAsync(
            t => t.SourceType == SourceType.IMessage && t.Type == ThreadType.Customer && t.State != ThreadState.Deleted);
        var latest = lastCustomerThread?.OrderByDescending(t => t.CreatedAt).FirstOrDefault();
        if (latest != null)
        {
            status.LastCustomerMessageAt = latest.CreatedAt;
            status.HoursSinceCustomerMessage = (int)Math.Max(0, Math.Floor((DateTime.UtcNow - latest.CreatedAt).TotalHours));
            var idleHours = TimeSpan.FromHours(Math.Max(1, options.MatchWindowHours));
            status.WindowStatus = (DateTime.UtcNow - latest.CreatedAt) <= idleHours ? "ok" : "expired";
        }

        if (!options.IsConfigured)
        {
            status.ApiReachable = false;
            return status;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(options.StatusCheckTimeoutSeconds, 1, 60)));
            status.ApiReachable = await _client.TestConnectionAsync(cts.Token);

            if (!options.IsAmbMode)
            {
                status.ServerVersion = await _client.GetServerVersionAsync(cts.Token);
                var hooks = await _client.ListRegisteredWebhooksAsync(cts.Token);
                status.RegisteredWebhookUrl = hooks.Count > 0 ? hooks[0] : null;

                if (!string.IsNullOrWhiteSpace(status.ServerVersion)
                    && IsVersionLower(status.ServerVersion, options.MinServerVersion))
                {
                    _logger.LogWarning("iMessage 桥接服务器版本 {Version} 低于要求的 {MinVersion}（webhook 需 ≥1.0.0）",
                        status.ServerVersion, options.MinServerVersion);
                }
            }
        }
        catch (OperationCanceledException)
        {
            status.ApiReachable = false;
            _logger.LogWarning("iMessage 连通性检查超时（{Timeout}s）", options.StatusCheckTimeoutSeconds);
        }
        catch (Exception ex)
        {
            status.ApiReachable = false;
            _logger.LogWarning(ex, "iMessage 连通性检查失败");
        }

        return status;
    }

    /// <inheritdoc />
    public async Task<IMessageReplyResult> ReplyAsync(IMessageReplyRequest request, int userId)
    {
        var options = await _settingsProvider.GetCurrentAsync();
        if (!options.Enabled || !options.IsConfigured)
        {
            return new IMessageReplyResult { Sent = false, ErrorMessage = "iMessage 未启用或未配置" };
        }

        if (string.IsNullOrWhiteSpace(request.Content) && (request.AttachmentIds == null || request.AttachmentIds.Count == 0))
        {
            return new IMessageReplyResult { Sent = false, ErrorMessage = "回复内容或附件至少提供一项" };
        }

        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(request.ConversationId);
        if (conversation == null)
        {
            return new IMessageReplyResult { Sent = false, ErrorMessage = $"会话 {request.ConversationId} 不存在" };
        }
        if (conversation.SourceType != SourceType.IMessage)
        {
            return new IMessageReplyResult { Sent = false, ErrorMessage = "该会话不是 iMessage / Apple Messages 会话" };
        }

        var customer = conversation.CustomerId > 0
            ? await _unitOfWork.Customers.GetByIdAsync(conversation.CustomerId.Value)
            : conversation.Customer;
        var scopedChannelId = customer?.ChannelId;
        if (string.IsNullOrWhiteSpace(scopedChannelId))
        {
            return new IMessageReplyResult { Sent = false, ErrorMessage = "该会话缺少客户身份（ChannelId），无法回复" };
        }

        var mode = IMessageIdentity.ExtractModePrefix(scopedChannelId);
        if (mode != options.Mode)
        {
            return new IMessageReplyResult
            {
                Sent = false,
                ErrorMessage = $"会话身份模式（{mode}）与当前配置的传输模式（{options.Mode}）不一致，切换模式后旧会话不可回复",
            };
        }

        var externalId = IMessageIdentity.ExtractExternalId(scopedChannelId);

        // AMB 硬规则：永不允许冷启动私聊 —— 会话里没有客户消息就不发。
        if (IMessageIdentity.IsAmbMode(mode))
        {
            var inbound = await _unitOfWork.Threads.FindAsync(
                t => t.ConversationId == conversation.Id && t.Type == ThreadType.Customer && t.State != ThreadState.Deleted);
            if (inbound == null || !inbound.Any())
            {
                return new IMessageReplyResult { Sent = false, ErrorMessage = "AMB 不允许主动冷启动私聊：该会话没有客户消息" };
            }
        }

        var destination = IMessageIdentity.IsAmbMode(mode)
            ? externalId
            : IMessageIdentity.BuildReplyGuid(scopedChannelId, inboundService: null);

        var businessId = IMessageIdentity.IsAmbMode(mode)
            ? (options.BusinessIdentifier ?? options.BusinessId ?? string.Empty)
            : (options.BaseUrl ?? "bridge");

        string? sentMessageId = null;
        var mediaErrors = new List<string>();

        try
        {
            if (!string.IsNullOrWhiteSpace(request.Content))
            {
                sentMessageId = await _client.SendTextMessageAsync(destination, request.Content!);
                if (sentMessageId == null)
                {
                    return new IMessageReplyResult { Sent = false, ErrorMessage = "消息发送失败，请检查桥接服务器/MSP 状态与日志" };
                }
            }

            if (request.AttachmentIds is { Count: > 0 })
            {
                var attachments = await _unitOfWork.Attachments.GetByIdsAsync(request.AttachmentIds);
                if (attachments != null)
                {
                    foreach (var attachment in attachments)
                    {
                        var path = ResolveAttachmentPath(attachment);
                        if (path == null || !File.Exists(path))
                        {
                            mediaErrors.Add(attachment.FileName);
                            continue;
                        }

                        var bytes = await File.ReadAllBytesAsync(path);
                        var id = await _client.SendAttachmentAsync(destination, bytes, attachment.FileName, attachment.MimeType ?? "application/octet-stream");
                        if (id == null) mediaErrors.Add(attachment.FileName);
                        else sentMessageId ??= id;
                    }
                }

                // 请求里给了 id 但库里查不到的附件也算"没发出去"，按 #id 计入失败清单。
                // 不计的话「请求 3 个、只查到 1 个、那 1 个还失败」会被误判成部分失败而不是全部失败。
                var foundIds = new HashSet<int>(attachments?.Select(a => a.Id) ?? []);
                foreach (var missingId in request.AttachmentIds.Where(id => !foundIds.Contains(id)))
                {
                    mediaErrors.Add($"#{missingId}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "iMessage 回复发送失败：ConversationId={ConversationId}", conversation.Id);
            return new IMessageReplyResult { Sent = false, ErrorMessage = $"发送失败：{ex.Message}" };
        }

        // 附件失败的三种口径（审计 #5：旧实现无论附件是否全部失败都返回 Sent=true）：
        //  1) 请求的附件全部失败、且没有正文可退化成纯文本 ⇒ 这条回复什么都没送达，必须 Sent=false，
        //     且不写线程（写线程会让会话里出现一条"已发送"的记录，而客户那边根本没收到）；
        //  2) 附件全部失败但正文已发出 ⇒ 正文送达，算成功，失败清单回给调用方；
        //  3) 部分附件失败 ⇒ 成功，失败清单回给调用方。
        var allAttachmentsFailed = request.AttachmentIds is { Count: > 0 }
                                   && mediaErrors.Count >= request.AttachmentIds.Count;
        if (allAttachmentsFailed && string.IsNullOrWhiteSpace(request.Content))
        {
            _logger.LogWarning("iMessage 全部附件发送失败且无正文，按未发送返回：ConversationId={ConversationId}, Files={Files}",
                conversation.Id, string.Join(", ", mediaErrors));
            return new IMessageReplyResult
            {
                Sent = false,
                ThreadCreated = false,
                ErrorMessage = "附件全部发送失败，消息未发出：" + string.Join(", ", mediaErrors),
                FailedAttachments = mediaErrors,
            };
        }

        if (mediaErrors.Count > 0)
        {
            _logger.LogWarning("iMessage 部分附件发送失败：ConversationId={ConversationId}, Files={Files}",
                conversation.Id, string.Join(", ", mediaErrors));
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var thread = new ConversationThread
            {
                ConversationId = conversation.Id,
                From = businessId,
                To = externalId,
                Subject = string.Empty,
                Body = BuildReplyHtmlBody(request.Content),
                MessageId = sentMessageId,
                SourceType = SourceType.IMessage,
                Type = ThreadType.Message,
                SourceVia = SourceVia.User,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
            };
            await _unitOfWork.Threads.AddAsync(thread);
            await _unitOfWork.SaveChangesAsync();

            if (request.AttachmentIds is { Count: > 0 })
            {
                var tracked = await _unitOfWork.Attachments.GetByIdsTrackedAsync(request.AttachmentIds);
                if (tracked != null)
                {
                    foreach (var attachment in tracked)
                    {
                        if (attachment.ThreadId == 0) attachment.ThreadId = thread.Id;
                    }
                    await _unitOfWork.SaveChangesAsync();
                }
            }

            conversation.UpdatedAt = DateTime.UtcNow;
            conversation.LastReplyAt = DateTime.UtcNow;
            conversation.LastReplyFrom = SourceVia.User;
            await _unitOfWork.SaveChangesAsync();

            await transaction.CommitAsync();

            try
            {
                await _notificationPublisher.NotifyMailboxAsync(
                    conversation.MailboxId.ToString(), "conversation_updated", new { conversationId = conversation.Id });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "iMessage 回复通知推送失败：ConversationId={ConversationId}", conversation.Id);
            }

            if (_webhookService != null)
            {
                try
                {
                    await _webhookService.TriggerAsync("conversation_updated", conversation.MailboxId,
                        new { conversationId = conversation.Id }, "Conversation", conversation.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "iMessage 回复 webhook 触发失败：ConversationId={ConversationId}", conversation.Id);
                }
            }

            return new IMessageReplyResult
            {
                Sent = true,
                ThreadCreated = true,
                ThreadId = thread.Id,
                FailedAttachments = mediaErrors.Count > 0 ? mediaErrors : null,
            };
        }
        catch (Exception ex)
        {
            await transaction.SafeRollbackAsync();
            _logger.LogError(ex, "iMessage 回复落库失败（消息已发出）：ConversationId={ConversationId}", conversation.Id);
            // 消息已经发出去了，不能谎称未发送；标记线程未创建，让上层提示坐席
            return new IMessageReplyResult { Sent = true, ThreadCreated = false, ErrorMessage = "消息已发送但记录失败，请刷新会话确认" };
        }
    }

    /// <inheritdoc />
    public async Task<IMessageTestResult> TestConnectionAsync(string? testHandle = null)
    {
        var settings = await _settingsProvider.GetCurrentAsync();
        if (!settings.IsConfigured)
        {
            return new IMessageTestResult { Success = false, ErrorMessage = "配置不完整：请先填写桥接服务器地址与口令（或 MSP/Apple 参数）" };
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(settings.StatusCheckTimeoutSeconds, 1, 60)));
            var reachable = await _client.TestConnectionAsync(cts.Token);
            string? version = null;

            if (!settings.IsAmbMode)
            {
                version = await _client.GetServerVersionAsync(cts.Token);
                if (!reachable)
                {
                    return new IMessageTestResult { Success = false, ServerVersion = version, ErrorMessage = "桥接服务器不可达（检查 BaseUrl、口令与服务器是否在运行）" };
                }
                if (!string.IsNullOrWhiteSpace(version) && IsVersionLower(version, settings.MinServerVersion))
                {
                    return new IMessageTestResult
                    {
                        Success = false,
                        ServerVersion = version,
                        ErrorMessage = $"服务器版本 {version} 低于要求 {settings.MinServerVersion}（webhook 需 ≥1.0.0）",
                    };
                }

                if (!string.IsNullOrWhiteSpace(testHandle))
                {
                    var scoped = IMessageIdentity.BuildScopedChannelId(IMessageModes.Bridge, IMessageIdentity.NormalizeHandle(testHandle!));
                    var destination = IMessageIdentity.BuildReplyGuid(scoped, inboundService: null);
                    var id = await _client.SendTextMessageAsync(destination,
                        "NoneadCSS 连通性测试（iMessage bridge）", $"oc-test-{Guid.NewGuid():N}");
                    return id == null
                        ? new IMessageTestResult { Success = false, ServerVersion = version, ErrorMessage = "测试消息发送失败：请确认该 handle 已在 Messages.app 里与桥接账号有过会话" }
                        : new IMessageTestResult { Success = true, MessageId = id, ServerVersion = version };
                }

                return new IMessageTestResult { Success = true, ServerVersion = version };
            }

            // AMB：本仓库不冒充 Apple 资格 —— 只验证参数与 JWT 可签发，不向 Apple 网关发真消息
            if (settings.Mode == IMessageModes.AmbAppleDirect)
            {
                var token = IMessageJwtSigner.MintToken(
                    settings.MspId ?? string.Empty, settings.MspSharedSecret ?? string.Empty, settings.TokenTtlSeconds);
                return new IMessageTestResult
                {
                    Success = !string.IsNullOrWhiteSpace(token),
                    MessageId = null,
                    ErrorMessage = !string.IsNullOrWhiteSpace(token)
                        ? null
                        : "JWT 签发失败：检查 MspId 与 MspSharedSecret",
                };
            }

            return new IMessageTestResult
            {
                Success = reachable,
                ErrorMessage = reachable ? null : "MSP 网关不可达：检查 ApiBaseUrl 与 ApiKey",
            };
        }
        catch (OperationCanceledException)
        {
            return new IMessageTestResult { Success = false, ErrorMessage = $"连接超时（{settings.StatusCheckTimeoutSeconds}s）" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "iMessage 测试连接异常");
            return new IMessageTestResult { Success = false, ErrorMessage = $"测试失败：{ex.Message}" };
        }
    }

    /// <summary>向桥接服务器登记本系统的 webhook 接收地址（bridge 专属；AMB 由 Apple/MSP 侧配置）。</summary>
    public async Task<IMessageTestResult> RegisterWebhookAsync()
    {
        var settings = await _settingsProvider.GetCurrentAsync();
        if (settings.IsAmbMode)
        {
            return new IMessageTestResult { Success = false, ErrorMessage = "AMB 模式下入站由 Apple/MSP 配置，不支持自动登记 webhook" };
        }

        var url = BuildCallbackUrl(settings);
        if (string.IsNullOrWhiteSpace(url))
        {
            return new IMessageTestResult { Success = false, ErrorMessage = "缺少 App:BaseUrl 或 IMessage:WebhookBaseUrl，无法生成回调地址" };
        }

        var ok = await _client.RegisterWebhookAsync(url!);
        return ok
            ? new IMessageTestResult { Success = true, ErrorMessage = null }
            : new IMessageTestResult { Success = false, ErrorMessage = "webhook 登记失败：检查服务器版本（需 ≥1.0.0）与口令" };
    }

    // ===== 私有辅助 =====

    private async Task SaveOptionAsync(string key, string? value)
    {
        // 必须用可跟踪查询：AsNoTracking 拿到的实体不挂到 ChangeTracker，后续赋值不会生成 UPDATE
        var existing = await _unitOfWork.SystemOptions.GetByNameTrackedAsync(key);
        if (existing != null)
        {
            existing.Value = value ?? string.Empty;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _unitOfWork.SystemOptions.AddAsync(new SystemOption
            {
                Name = key,
                Value = value ?? string.Empty,
                Category = IMessageOptionKeys.Category,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }
    }

    private static IMessageSettingsDto MapToDto(IMessageOptions o) => new()
    {
        Mode = o.Mode,
        ModeActive = o.IsModeActive(),
        ExperimentalAppleDirect = o.ExperimentalAppleDirect,
        Enabled = o.Enabled,
        Configured = o.IsConfigured,
        BaseUrl = o.BaseUrl,
        AuthStyle = o.AuthStyle,
        HasPassword = !string.IsNullOrWhiteSpace(o.Password),
        HasBridgeWebhookSecret = !string.IsNullOrWhiteSpace(o.BridgeWebhookSecret),
        WebhookPath = o.WebhookPath,
        WebhookBaseUrl = o.WebhookBaseUrl,
        SendMethod = o.SendMethod,
        AllowSmsFallback = o.AllowSmsFallback,
        AllowList = o.AllowList,
        RateLimitPerMinute = o.RateLimitPerMinute,
        VerifyTls = o.VerifyTls,
        HealthEndpoint = o.HealthEndpoint,
        MinServerVersion = o.MinServerVersion,
        ReadHistoryOnStart = o.ReadHistoryOnStart,
        MatchWindowHours = o.MatchWindowHours,
        MailboxId = o.MailboxId,
        AutoReplyEnabled = o.AutoReplyEnabled,
        AutoReplyMessage = o.AutoReplyMessage,
        MspConnector = o.MspConnector,
        ApiBaseUrl = o.ApiBaseUrl,
        HasApiKey = !string.IsNullOrWhiteSpace(o.ApiKey),
        HasWebhookSecret = !string.IsNullOrWhiteSpace(o.WebhookSecret),
        BusinessIdentifier = o.BusinessIdentifier,
        FallbackChannel = o.FallbackChannel,
        MspId = o.MspId,
        HasMspSharedSecret = !string.IsNullOrWhiteSpace(o.MspSharedSecret),
        TokenTtlSeconds = o.TokenTtlSeconds,
        GatewayBaseUrl = o.GatewayBaseUrl,
        BusinessId = o.BusinessId,
        InboundPath = o.InboundPath,
        AttachmentKeySize = o.AttachmentKeySize,
        MaxBodyBytes = o.MaxBodyBytes,
    };

    /// <summary>
    /// 本系统对外回调地址：优先 IMessage:WebhookBaseUrl（桥接服务器看到的公网地址），
    /// 否则退回 App:BaseUrl。bridge 的 path 是 /api/imessage/bluebubbles/webhook。
    /// </summary>
    private string? BuildCallbackUrl(IMessageOptions options)
    {
        var configured = !string.IsNullOrWhiteSpace(options.WebhookBaseUrl)
            ? options.WebhookBaseUrl
            : _configuration["App:BaseUrl"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            _logger.LogWarning("缺少配置项 App:BaseUrl 或 IMessage:WebhookBaseUrl，无法生成 iMessage 回调地址");
            return null;
        }
        var path = string.IsNullOrWhiteSpace(options.WebhookPath) ? "/api/imessage/bluebubbles/webhook" : options.WebhookPath;
        return $"{configured.TrimEnd('/')}{(path.StartsWith('/') ? path : "/" + path)}";
    }

    private string? ResolveAttachmentPath(Domain.Entities.Attachment attachment)
    {
        var uploadPath = _configuration["Attachment:UploadPath"];
        if (string.IsNullOrWhiteSpace(uploadPath)) return null;
        // 与 WhatsApp/Email 走同一套解析：相对路径按 ContentRootPath 展开，避免不同入口解析出不同目录
        var baseDir = AttachmentPathResolver.ResolveBasePath(uploadPath, _environment.ContentRootPath);
        var full = Path.GetFullPath(Path.Combine(baseDir, attachment.FileDir ?? string.Empty, attachment.StoredFileName));
        // 目录穿越防护：解析后的路径必须仍在附件根目录内
        return full.StartsWith(Path.GetFullPath(baseDir), StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static string BuildReplyHtmlBody(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        var safe = HttpUtility.HtmlEncode(content).Replace("\n", "<br>");
        return $"<div class=\"imessage-text\">{safe}</div>";
    }

    /// <summary>粗粒度语义化版本比较（只比较数字段，缺段按 0）。</summary>
    internal static bool IsVersionLower(string? actual, string? required)
    {
        // 版本读不到（桥接未上报 / 接口异常）时不能判成「版本过低」——那是两种不同的运维结论，
        // 误报会把健康的 1.x 服务器写成不达标。未知一律按「无法判定 ⇒ 不算低」。
        if (string.IsNullOrWhiteSpace(actual)) return false;
        var a = ParseVersion(actual);
        var b = ParseVersion(required);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var av = i < a.Length ? a[i] : 0;
            var bv = i < b.Length ? b[i] : 0;
            if (av != bv) return av < bv;
        }
        return false;
    }

    private static int[] ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var parts = value.Trim().TrimStart('v', 'V').Split('.', '-', '+');
        var numbers = new List<int>();
        foreach (var part in parts)
        {
            if (int.TryParse(part, out var n)) numbers.Add(n);
            else break;
        }
        return numbers.ToArray();
    }
}
