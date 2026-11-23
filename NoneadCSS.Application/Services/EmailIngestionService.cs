using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Templates;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using System.Net;
using System.Text.RegularExpressions;

namespace NoneadCSS.Application.Services;

internal static class SendLogStatusConst
{
    public const int DeliveryError = 5;
}

internal static class SendLogMailTypeConst
{
    public const int EmailToCustomer = 1;
}

/// <summary>
/// 邮件收取过程中事务内处理的中间结果，用于在后置工作流中传递会话引用。
/// </summary>
internal sealed class ConversationIngestionContext
{
    public required Conversation Conversation { get; init; }
    public required bool IsNewConversation { get; init; }
}

public class EmailIngestionService : IEmailIngestionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailProcessingService _emailProcessingService;
    private readonly IEmailQueueService _emailQueueService;
    private readonly IWorkflowService _workflowService;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ILogger<EmailIngestionService> _logger;
    private readonly ITemplateRenderer _renderer;
    private readonly string _frontendUrl;
    private readonly EmailOptions _emailOptions;

    /// <summary>联系方式提取最大尝试次数：超过后即使字段缺失也不再尝试。</summary>
    private const int MaxContactExtractAttempts = 3;

    public EmailIngestionService(
        IUnitOfWork unitOfWork,
        IEmailProcessingService emailProcessingService,
        IEmailQueueService emailQueueService,
        IWorkflowService workflowService,
        IDistributedLockService distributedLockService,
        ILogger<EmailIngestionService> logger,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        IOptions<EmailOptions> emailOptions,
        ITemplateRenderer renderer)
    {
        _unitOfWork = unitOfWork;
        _emailProcessingService = emailProcessingService;
        _emailQueueService = emailQueueService;
        _workflowService = workflowService;
        _distributedLockService = distributedLockService;
        _logger = logger;
        _renderer = renderer;
        _frontendUrl = configuration["App:FrontendUrl"] ?? throw new InvalidOperationException("App:FrontendUrl 未配置");
        _emailOptions = emailOptions.Value;
    }

    public async Task<EmailIngestionResult> ProcessMessageAsync(MimeMessage message, Mailbox mailbox, string attachmentBasePath)
    {
        var result = new EmailIngestionResult();

        _logger.LogInformation("[ProcessMessage] ===== 邮件接收开始 ===== MailboxId={MailboxId}, MessageId={MessageId}, Subject={Subject}, From={From}, To={To}, InReplyTo={InReplyTo}, Date={Date}, HasAttachments={HasAttachments}",
            mailbox.Id, message.MessageId, message.Subject, message.From?.ToString(), message.To?.ToString(),
            message.InReplyTo, message.Date, message.Attachments.Any());

        var parseResult = _emailProcessingService.ParseEmail(message, mailbox.Id);

        _logger.LogDebug("[ProcessMessage] 邮件解析完成：MessageId={MessageId}, FromEmail={FromEmail}, Subject={Subject}, HasAttachments={HasAttachments}, IsAutoReply={IsAutoReply}, IsBounce={IsBounce}",
            parseResult.MessageId, parseResult.FromEmail, parseResult.Subject, parseResult.HasAttachments, parseResult.IsAutoReply, parseResult.IsBounce);

        if (parseResult.IsAutoReply)
        {
            _logger.LogInformation("[ProcessMessage] 跳过自动回复邮件：MessageId={MessageId}, Subject={Subject}, From={From}",
                parseResult.MessageId, parseResult.Subject, parseResult.FromEmail);
            result.IsAutoReply = true;
            return result;
        }

        if (parseResult.IsBounce && parseResult.BounceInfo != null)
        {
            await HandleBounceAsync(parseResult.BounceInfo, mailbox.Id);
            _logger.LogInformation("[ProcessMessage] 处理退信：OriginalMessageId={OriginalMessageId}, Recipient={Recipient}, Type={BounceType}",
                parseResult.BounceInfo.OriginalMessageId,
                parseResult.BounceInfo.RecipientEmail,
                parseResult.BounceInfo.BounceType);
            result.IsBounce = true;
            return result;
        }

        // 事务内处理会话与附件，返回处理结果（含会话引用和是否新建会话）
        var ingestionContext = await IngestConversationInTransactionAsync(message, mailbox, parseResult, attachmentBasePath);

        if (ingestionContext == null)
        {
            _logger.LogInformation("[ProcessMessage] 邮件处理跳过（重复邮件或参数无效）：MessageId={MessageId}", parseResult.MessageId);
            return result; // 重复邮件或参数无效，已跳过
        }

        result.NewConversationCount = ingestionContext.IsNewConversation ? 1 : 0;
        result.ReplyCount = ingestionContext.IsNewConversation ? 0 : 1;

        _logger.LogInformation("[ProcessMessage] 邮件处理事务完成：MessageId={MessageId}, ConversationId={ConversationId}, Number={Number}, IsNewConversation={IsNew}, NewCount={NewCount}, ReplyCount={ReplyCount}, 线程Body预览={Preview}",
            parseResult.MessageId, ingestionContext.Conversation.Id, ingestionContext.Conversation.Number,
            ingestionContext.IsNewConversation,
            result.NewConversationCount, result.ReplyCount,
            (ingestionContext.Conversation.Threads.Count > 0 && ingestionContext.Conversation.Threads.Last().Body.Length > 200)
                ? ingestionContext.Conversation.Threads.Last().Body.Substring(0, 200)
                : (ingestionContext.Conversation.Threads.Count > 0 ? ingestionContext.Conversation.Threads.Last().Body : ""));

        // 事务提交后触发后置工作流（自动回复、工作流规则）
        await TriggerPostIngestionWorkflowsAsync(ingestionContext.Conversation, mailbox, ingestionContext.IsNewConversation);

        // 联系方式收集：仅新会话触发（提取），老会话完全不收集
        // 从邮件正文（优先签名块）提取姓名/手机/邮箱，写入客户资料（受 ContactCollectionEnabled 开关控制）
        // 开关优先从 SystemOptions DB 读取（前端可配），无值时回退到 appsettings.json 的 Email:ContactCollectionEnabled 基线
        var contactCollectionEnabled = await IsContactCollectionEnabledAsync();
        _logger.LogDebug("[ProcessMessage] 联系方式收集：开关={Enabled}, IsNewConversation={IsNew}, CustomerId={CustomerId}",
            contactCollectionEnabled, ingestionContext.IsNewConversation, ingestionContext.Conversation.CustomerId);

        if (ingestionContext.Conversation.CustomerId.HasValue
            && contactCollectionEnabled
            && ingestionContext.IsNewConversation)
        {
            _logger.LogDebug("[ProcessMessage] 联系方式收集：开始处理，CustomerId={CustomerId}",
                ingestionContext.Conversation.CustomerId.Value);

            // 优先用纯文本正文（保留原始换行结构，便于识别签名块）；无纯文本时回退到 HTML 转纯文本
            string plainText;
            string plainTextSource;
            if (!string.IsNullOrWhiteSpace(parseResult.TextBody))
            {
                plainText = parseResult.TextBody;
                plainTextSource = "TextBody";
            }
            else
            {
                var htmlForConvert = parseResult.HtmlBody ?? string.Empty;
                _logger.LogDebug("[ProcessMessage] 联系方式收集：无纯TextBody，回退到HtmlToPlainText转换。HtmlBody长度={Len}",
                    htmlForConvert.Length);
                plainText = HtmlToPlainText(htmlForConvert);
                plainTextSource = "HtmlBody(转Text)";
            }
            _logger.LogDebug("[ProcessMessage] 联系方式收集：纯文本来源={Source}, 长度={Len}, 前200字符预览={Preview}",
                plainTextSource, plainText.Length,
                plainText.Length > 200 ? plainText.Substring(0, 200) : plainText);

            if (!string.IsNullOrWhiteSpace(plainText))
            {
                await TryExtractContactInfoFromEmailAsync(ingestionContext.Conversation.CustomerId.Value, plainText);
            }
            else
            {
                _logger.LogDebug("[ProcessMessage] 联系方式收集：转换后纯文本为空，跳过提取");
            }
        }
        else if (!contactCollectionEnabled)
        {
            _logger.LogInformation("[ProcessMessage] 联系方式收集：开关已关闭，跳过提取。ConversationId={ConversationId}, CustomerId={CustomerId}",
                ingestionContext.Conversation.Id, ingestionContext.Conversation.CustomerId);
        }

        _logger.LogInformation("[ProcessMessage] ===== 邮件处理完成 ===== MessageId={MessageId}, ConversationId={ConversationId}",
            parseResult.MessageId, ingestionContext.Conversation.Id);

        return result;
    }

    /// <summary>
    /// 读取联系方式收集开关值。
    /// 优先从 SystemOptions DB 读取（前端可配），无值时回退到 appsettings.json 的 Email:ContactCollectionEnabled 基线。
    /// 任何异常（如 DB 未初始化 / SystemOptions 未注入）均安全回退到 appsettings 默认值，不阻塞主收取流程。
    /// </summary>
    private async Task<bool> IsContactCollectionEnabledAsync()
    {
        try
        {
            var repo = _unitOfWork.SystemOptions;
            if (repo == null)
            {
                _logger.LogDebug("[联系方式收集] SystemOptions 仓储未注入，回退到 appsettings 基线：Key={Key}, Baseline={Baseline}",
                    EmailOptionKeys.ContactCollectionEnabled, _emailOptions.ContactCollectionEnabled);
                return _emailOptions.ContactCollectionEnabled;
            }
            var dbValue = await repo.GetValueAsync(EmailOptionKeys.ContactCollectionEnabled);
            if (bool.TryParse(dbValue, out var enabled))
            {
                _logger.LogDebug("[联系方式收集] 开关读取成功（DB）：Key={Key}, DBValue={DbValue}, Enabled={Enabled}",
                    EmailOptionKeys.ContactCollectionEnabled, dbValue, enabled);
                return enabled;
            }
            _logger.LogDebug("[联系方式收集] DB 开关值格式异常或未配置，回退到 appsettings 基线：Key={Key}, DBValue={DbValue}, Baseline={Baseline}",
                EmailOptionKeys.ContactCollectionEnabled, dbValue, _emailOptions.ContactCollectionEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联系方式收集] 读取DB开关失败，回退到 appsettings 基线：Key={Key}, Baseline={Baseline}",
                EmailOptionKeys.ContactCollectionEnabled, _emailOptions.ContactCollectionEnabled);
        }
        return _emailOptions.ContactCollectionEnabled;
    }

    /// <summary>
    /// 从邮件正文（纯文本）中提取联系方式（姓名、手机、邮箱），写入 Customer 对应字段。
    /// 邮件场景优先从底部签名块提取（preferBottom=true），避免误中引用旧邮件中的噪声联系方式。
    /// 覆盖策略：已有字段会被新提取的值直接覆盖（姓名直接覆盖、手机号先清空再添加、邮箱先清空再添加）；
    /// 每个客户最多尝试提取 3 次。
    /// </summary>
    /// <param name="customerId">客户 ID</param>
    /// <param name="text">邮件纯文本正文</param>
    /// <returns>true=成功提取并写入至少一个字段；false=未提取到或已达尝试上限</returns>
    private async Task<bool> TryExtractContactInfoFromEmailAsync(int customerId, string text)
    {
        try
        {
            var safeText = text ?? string.Empty;
            _logger.LogDebug("[TryExtractContactInfo] 开始：CustomerId={CustomerId}, 输入文本长度={Len}, 前150字符预览={Preview}",
                customerId, safeText.Length,
                safeText.Length > 150 ? safeText.Substring(0, 150) : safeText);

            // 获取 tracked 实体（邮件事务已提交，customer 可能已 detach）
            var customer = await _unitOfWork.Customers.GetByIdTrackedAsync(customerId);
            if (customer == null)
            {
                _logger.LogError("[TryExtractContactInfo] 客户不存在：CustomerId={CustomerId}", customerId);
                return false;
            }

            // 跳过条件 1：姓名、手机、邮箱三个字段都已存在，直接跳过
            bool hasName = !string.IsNullOrWhiteSpace(customer.FirstName) || !string.IsNullOrWhiteSpace(customer.LastName);
            bool hasPhone = customer.GetPhones().Count > 0;
            bool hasEmail = customer.Emails != null && customer.Emails.Any(e => !string.IsNullOrWhiteSpace(e.EmailAddress));
            _logger.LogDebug("[TryExtractContactInfo] 客户当前字段状态：CustomerId={CustomerId}, HasName={HasName}(FirstName={FirstName}, LastName={LastName}), HasPhone={HasPhone}(Count={PhoneCount}), HasEmail={HasEmail}(Count={EmailCount})",
                customerId, hasName, customer.FirstName, customer.LastName,
                hasPhone, customer.GetPhones().Count,
                hasEmail, customer.Emails?.Count ?? 0);

            // 覆盖策略：不再因三字段都已存在而跳过，已有字段会被新提取的值直接覆盖
            // 跳过条件：提取尝试次数达到上限（3 次），直接跳过
            if (customer.ContactExtractAttempts >= MaxContactExtractAttempts)
            {
                _logger.LogInformation("[TryExtractContactInfo] 跳过：已达最大尝试次数。CustomerId={CustomerId}, Attempts={Attempts}, Max={Max}",
                    customer.Id, customer.ContactExtractAttempts, MaxContactExtractAttempts);
                return false;
            }
            _logger.LogDebug("[TryExtractContactInfo] 尝试次数：CustomerId={CustomerId}, 当前={Current}, Max={Max}",
                customer.Id, customer.ContactExtractAttempts, MaxContactExtractAttempts);

            // 提取尝试次数 +1
            customer.ContactExtractAttempts++;

            // 邮件场景：preferBottom=true，优先从底部签名块提取
            _logger.LogDebug("[TryExtractContactInfo] 调用 ContactInfoExtractor.Extract（preferBottom=true），输入长度={Len}",
                text?.Length ?? 0);
            var info = ContactInfoExtractor.Extract(text, _logger, preferBottom: true);
            _logger.LogDebug("[TryExtractContactInfo] ContactInfoExtractor.Extract 返回：HasAny={HasAny}, Name={Name}, Phone={Phone}, Email={Email}",
                info.HasAny, info.Name, info.Phone, info.Email);

            if (!info.HasAny)
            {
                _logger.LogDebug("[TryExtractContactInfo] 未提取到任何联系方式，仅递增尝试次数。CustomerId={CustomerId}, NewAttempts={NewAttempts}",
                    customer.Id, customer.ContactExtractAttempts);
                customer.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
                return false;
            }

            bool changed = false;

            // 覆盖策略改为「仅补空」：人工维护的客户资料优先，来信签名不得冲掉已有值。
            // 原实现为覆盖策略（姓名直接覆盖、手机先清空再加、邮箱先清空再写），
            // 导致一封签名不同的邮件就能把人工维护的档案改掉（功能全景总结 #7A）。
            // 1. 姓名：仅在客户尚无姓名时填入
            if (!hasName && !string.IsNullOrWhiteSpace(info.Name))
            {
                var (lastName, firstName) = ChineseNameSplitter.Split(info.Name);
                customer.LastName = lastName;
                customer.FirstName = firstName;
                changed = true;
                _logger.LogInformation("[TryExtractContactInfo] 姓名提取并写入（原为空）：CustomerId={CustomerId}, 原始={RawName}, 拆分后 LastName={LastName}, FirstName={FirstName}",
                    customer.Id, info.Name, lastName, firstName);
            }

            // 2. 手机号：仅在客户尚无手机号时添加，已有手机号保持不变
            if (!hasPhone && !string.IsNullOrWhiteSpace(info.Phone))
            {
                var phones = customer.GetPhones();
                phones.Add(new CustomerPhone { Value = info.Phone, Type = 4 });
                customer.SetPhones(phones);
                changed = true;
                _logger.LogInformation("[TryExtractContactInfo] 手机号提取并写入（原为空）：CustomerId={CustomerId}, Phone={Phone}",
                    customer.Id, info.Phone);
            }

            // 3. 邮箱：仅在客户尚无邮箱时添加，已有邮箱不清空、不替换
            // 保留邮箱全局唯一性检查 IsEmailOwnedByAnyCustomerAsync，避免写入被其他客户占用的地址
            customer.Emails ??= new List<Email>();
            if (!hasEmail && !string.IsNullOrWhiteSpace(info.Email))
            {
                var emailOwned = await _unitOfWork.Customers.IsEmailOwnedByAnyCustomerAsync(info.Email, customer.Id);
                if (emailOwned)
                {
                    _logger.LogWarning("[TryExtractContactInfo] 邮箱跳过：已被其他客户占用。CustomerId={CustomerId}, Email={Email}",
                        customer.Id, info.Email);
                }
                else
                {
                    customer.Emails.Add(new Email
                    {
                        EmailAddress = info.Email,
                        Type = 1,
                        CustomerId = customer.Id
                    });
                    changed = true;
                    _logger.LogInformation("[TryExtractContactInfo] 邮箱提取并写入（原为空）：CustomerId={CustomerId}, Email={Email}",
                        customer.Id, info.Email);
                }
            }

            customer.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();

            if (changed)
            {
                _logger.LogInformation("[TryExtractContactInfo] 提取完成（至少一个字段已写入）：CustomerId={CustomerId}, Name={Name}, Phone={Phone}, Email={Email}, Attempts={Attempts}",
                    customer.Id, info.Name, info.Phone, info.Email, customer.ContactExtractAttempts);
                return true;
            }

            _logger.LogInformation("[TryExtractContactInfo] 提取到字段但均非缺失字段，仅记录尝试。CustomerId={CustomerId}, Attempts={Attempts}, 提取到Name={HasName}/Phone={HasPhone}/Email={HasEmail}",
                customer.Id, customer.ContactExtractAttempts,
                !string.IsNullOrWhiteSpace(info.Name), !string.IsNullOrWhiteSpace(info.Phone), !string.IsNullOrWhiteSpace(info.Email));
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TryExtractContactInfo] 异常：CustomerId={CustomerId}", customerId);
            _unitOfWork.ClearChanges();
            return false;
        }
    }

    /// <summary>
    /// 将 HTML 转为纯文本，用于联系方式提取。
    /// 移除标签、解码 HTML 实体、保留换行。
    /// </summary>
    private static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        // 移除 <style> 和 <script> 块
        html = Regex.Replace(html, @"<(style|script)[^>]*>.*?</\1>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        // <br> 和 </div> 转换行
        html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</div>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</p>", "\n", RegexOptions.IgnoreCase);
        // 移除所有 HTML 标签
        html = Regex.Replace(html, @"<[^>]+>", "");
        // 解码常见 HTML 实体
        html = WebUtility.HtmlDecode(html);
        // 压缩多余空行
        html = Regex.Replace(html, @"\n{3,}", "\n\n");
        return html.Trim();
    }

    /// <summary>
    /// 在事务内完成会话匹配/创建、线程创建、附件保存。
    /// 事务失败时回滚数据库并清理已写入磁盘的孤儿附件文件。
    /// </summary>
    /// <returns>处理上下文；若邮件重复或参数无效则返回 null</returns>
    private async Task<ConversationIngestionContext?> IngestConversationInTransactionAsync(
        MimeMessage message, Mailbox mailbox, EmailParseResult parseResult, string attachmentBasePath)
    {
        // 跟踪事务期间写入磁盘的附件文件路径，用于事务回滚时清理孤儿文件
        var savedAttachmentPaths = new List<string>();

        // TD-06：在进入事务前获取分布式锁，串行化同一邮件（MessageId）的并发摄入，消除 TOCTOU 竞态。
        // 锁必须在事务外获取/释放：DistributedLockService 与 IUnitOfWork 共享同一 DbContext，
        // 若在事务内获取，锁的 INSERT 会并入当前事务，导致后续 Rollback 时把锁一并回滚，锁失效。
        var lockKey = $"email_ingest:{parseResult.MessageId}";
        var lockHandle = await _distributedLockService.TryAcquireAsync(lockKey, timeoutMs: 0);
        if (lockHandle == null)
        {
            _logger.LogInformation("邮件正在被其他实例处理，跳过：MessageId={MessageId}", parseResult.MessageId);
            return null;
        }

        try
        {
            using (var transaction = await _unitOfWork.BeginTransactionAsync())
            {
                try
                {
                _logger.LogDebug("邮件去重检查：MessageId={MessageId}", parseResult.MessageId);
                var existingThread = await _unitOfWork.Threads.FindByMessageIdAsync(parseResult.MessageId);
                if (existingThread != null)
                {
                    _logger.LogInformation("邮件已存在，跳过：MessageId={MessageId}, ExistingThreadId={ThreadId}",
                        parseResult.MessageId, existingThread.Id);
                    await transaction.RollbackAsync();
                    return null;
                }

                var customerEmail = parseResult.FromEmail;
                if (string.IsNullOrEmpty(customerEmail))
                {
                    _logger.LogWarning("无法从邮件提取发件人地址，跳过：MessageId={MessageId}, Subject={Subject}",
                        parseResult.MessageId, parseResult.Subject);
                    await transaction.RollbackAsync();
                    return null;
                }

                _logger.LogDebug("邮件客户匹配：Email={Email}", customerEmail);
                var customer = await _unitOfWork.Customers.GetOrCreateByEmailAsync(customerEmail);
                // GetOrCreateByEmailAsync 契约：成功返回非 null Customer；DB 错误抛异常。
                // 此处若返回 null 属未预期情况（仓储契约被破坏），不能静默跳过伪装为"重复邮件"，
                // 否则上游统计 NewConversationCount/ReplyCount 会出现数据丢失。
                // 抛异常进入下方 catch 块执行 SafeRollbackAsync + CleanupOrphanAttachmentFiles，
                // 由 FetchEmailJob 决定是否重试。
                if (customer == null)
                {
                    throw new InvalidOperationException(
                        $"GetOrCreateByEmailAsync 返回 null，仓储契约被破坏：Email={customerEmail}");
                }
                _logger.LogDebug("邮件客户匹配成功：CustomerId={CustomerId}, Email={Email}", customer.Id, customerEmail);

                _logger.LogDebug("邮件关联会话查找：MailboxId={MailboxId}, InReplyTo={InReplyTo}, References={References}",
                    mailbox.Id, message.InReplyTo, message.References?.ToString());
                var existingConversation = await _emailProcessingService.FindRelatedConversationAsync(message, mailbox.Id, _unitOfWork);

                Conversation conversation;
                ConversationThread thread;
                bool isNewConversation;

                if (existingConversation != null)
                {
                    _logger.LogDebug("邮件匹配到现有会话：ConversationId={ConversationId}", existingConversation.Id);
                    // 已有会话场景：customer.Id 一定 > 0（已从 DB 查询到），
                    // 传 customerId 给 AddReplyToExistingConversationAsync，内部再转 thread.CustomerId
                    (conversation, thread) = await AddReplyToExistingConversationAsync(message, parseResult, customer.Id, existingConversation.Id, customer);
                    isNewConversation = false;
                    _logger.LogInformation("会话 {ConversationId} 收到客户回复，ThreadId={ThreadId}, MessageId={MessageId}",
                        conversation.Id, thread.Id, parseResult.MessageId);
                }
                else
                {
                    _logger.LogDebug("邮件未匹配到现有会话，创建新会话");
                    (conversation, thread) = await CreateNewConversationAsync(message, parseResult, customer, mailbox.Id, customerEmail);
                    isNewConversation = true;
                    _logger.LogInformation("创建新会话：ConversationId={ConversationId}, Number={Number}, Subject={Subject}, ThreadId={ThreadId}",
                        conversation.Id, conversation.Number, conversation.Subject, thread.Id);
                }

                await _unitOfWork.Threads.AddAsync(thread);

                if (isNewConversation)
                {
                    await _unitOfWork.Conversations.AddAsync(conversation);
                }
                // else: conversation 已通过 GetByIdTrackedAsync 获取为追踪实体，SaveChangesAsync 会自动持久化修改

                await _unitOfWork.SaveChangesAsync();
                _logger.LogDebug("邮件线程持久化成功：ThreadId={ThreadId}, MessageId={MessageId}", thread.Id, parseResult.MessageId);

                // 新客户在 SaveChanges 后才获得实际 ID，需回填 CreatedByCustomerId
                if (isNewConversation && conversation.CreatedByCustomerId == 0 && customer.Id > 0)
                {
                    conversation.CreatedByCustomerId = customer.Id;
                    await _unitOfWork.SaveChangesAsync();
                }

                if (parseResult.HasAttachments)
                {
                    _logger.LogInformation("邮件包含附件，开始保存：MessageId={MessageId}, ThreadId={ThreadId}, AttachmentCount={AttachmentCount}",
                        parseResult.MessageId, thread.Id, message.Attachments.Count());
                    await SaveAttachmentsSafelyAsync(message, thread, conversation, mailbox.Id, attachmentBasePath, savedAttachmentPaths);
                }

                await transaction.CommitAsync();
                _logger.LogDebug("邮件事务提交成功：MessageId={MessageId}, ConversationId={ConversationId}",
                    parseResult.MessageId, conversation.Id);

                return new ConversationIngestionContext
                {
                    Conversation = conversation,
                    IsNewConversation = isNewConversation
                };
            }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    // TD-06 幂等兜底：若因罕见竞态绕过了锁导致并发写入重复 MessageId，
                    // 唯一约束冲突时回滚 + 清理跟踪 + 重查，若该消息已存在则视为重复返回 null；否则上报真实异常。
                    await transaction.SafeRollbackAsync();
                    _unitOfWork.ClearChanges();
                    CleanupOrphanAttachmentFiles(savedAttachmentPaths);
                    var duplicate = await _unitOfWork.Threads.FindByMessageIdAsync(parseResult.MessageId);
                    if (duplicate != null)
                    {
                        _logger.LogInformation("邮件并发写入重复，按幂等处理跳过：MessageId={MessageId}, ThreadId={ThreadId}",
                            parseResult.MessageId, duplicate.Id);
                        return null;
                    }
                    _logger.LogError(ex, "邮件事务唯一约束冲突且非重复，上报：MessageId={MessageId}, Subject={Subject}",
                        parseResult.MessageId, parseResult.Subject);
                    throw;
                }
                catch (Exception ex)
                {
                    await transaction.SafeRollbackAsync();
                    CleanupOrphanAttachmentFiles(savedAttachmentPaths);
                    _logger.LogError(ex, "邮件事务处理失败：MessageId={MessageId}, Subject={Subject}, MailboxId={MailboxId}",
                        parseResult.MessageId, parseResult.Subject, mailbox.Id);
                    throw;
                }
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
        MimeMessage message, EmailParseResult parseResult, int customerId, int existingConversationId, Customer? customer = null)
    {
        // FindRelatedConversationAsync 使用 AsNoTracking 返回非追踪实体，
        // 这里通过 GetByIdTrackedAsync 重新获取追踪实体，以便后续属性修改自动持久化
        var conversation = await _unitOfWork.Conversations.GetByIdTrackedAsync(existingConversationId);
        if (conversation == null)
        {
            throw new InvalidOperationException($"关联会话已被删除：ConversationId={existingConversationId}");
        }

        // 回复已有会话：customerId > 0 是常态，但为了统一入口也支持传入导航属性（新客户边缘场景）
        var thread = _emailProcessingService.CreateThreadFromEmail(message, parseResult, customerId, customer);
        thread.ConversationId = conversation.Id;

        if (conversation.Status == ConversationStatus.Closed)
        {
            conversation.Status = ConversationStatus.Active;
        }

        conversation.LastReplyAt = parseResult.Date;
        conversation.LastReplyFrom = SourceVia.Customer;
        conversation.UpdatedAt = parseResult.Date;
        conversation.SetPreview(thread.Body);
        conversation.ThreadsCount++;

        return (conversation, thread);
    }

    /// <summary>
    /// 创建新会话和首条线程，并分配默认文件夹。
    /// </summary>
    private async Task<(Conversation conversation, ConversationThread thread)> CreateNewConversationAsync(
        MimeMessage message, EmailParseResult parseResult, Customer customer, int mailboxId, string customerEmail)
    {
        var conversation = new Conversation
        {
            Number = await _unitOfWork.Conversations.GetNextNumberAsync(),
            MailboxId = mailboxId,
            Customer = customer, // 使用导航属性而非 CustomerId = customer.Id，确保新客户（Id=0）时 EF Core 能正确做 FK fixup
            CustomerEmail = customerEmail,
            Subject = parseResult.Subject,
            Status = ConversationStatus.Active,
            State = ConversationState.Published,
            SourceVia = SourceVia.Customer,
            SourceType = SourceType.Email,
            CreatedAt = parseResult.Date,
            UpdatedAt = parseResult.Date,
            LastReplyAt = parseResult.Date,
            LastReplyFrom = SourceVia.Customer,
            ThreadsCount = 1,
            HasAttachments = parseResult.HasAttachments,
            // CreatedByCustomerId 也通过导航属性同步：新 customer.Id=0 时先不写，
            // SaveChanges 后再回填（调用方已实现此回填逻辑）。
            CreatedByCustomerId = customer.Id > 0 ? customer.Id : null
        };

        conversation.SetPreview(parseResult.HtmlBody ?? parseResult.TextBody);

        // 创建首条线程时传入 customer 导航属性：
        // - 若 customer 为新创建（SaveChanges 前 Id=0），EF Core 会先插入 customer 再 fixup thread.CustomerId；
        // - 传 0 作为 customerId 占位（会被忽略，因为 customer != null）。
        var thread = _emailProcessingService.CreateThreadFromEmail(message, parseResult, customerId: 0, customer: customer);
        thread.IsFirst = true;
        thread.State = Domain.Entities.ThreadState.Active;

        conversation.Threads.Add(thread);

        await AssignFolderAsync(conversation, mailboxId);

        return (conversation, thread);
    }

    /// <summary>
    /// 保存附件到磁盘并关联到线程，同时记录文件路径用于事务回滚时清理。
    /// </summary>
    private async Task SaveAttachmentsSafelyAsync(
        MimeMessage message, ConversationThread thread, Conversation conversation,
        int mailboxId, string attachmentBasePath, List<string> savedAttachmentPaths)
    {
        var attachmentPath = Path.Combine(attachmentBasePath, mailboxId.ToString());
        _logger.LogDebug("邮件附件保存开始：ThreadId={ThreadId}, MailboxId={MailboxId}, AttachmentPath={Path}",
            thread.Id, mailboxId, attachmentPath);

        var attachments = await _emailProcessingService.SaveAttachmentsAsync(message, thread.Id, attachmentPath);
        _logger.LogDebug("邮件附件写入磁盘完成：ThreadId={ThreadId}, Count={Count}", thread.Id, attachments.Count);

        // 记录已写入磁盘的文件路径，用于事务回滚时清理孤儿文件
        foreach (var attachment in attachments)
        {
            var fullPath = Path.Combine(attachmentPath, attachment.StoredFileName);
            savedAttachmentPaths.Add(fullPath);
            _logger.LogDebug("邮件附件已写入磁盘：FileName={FileName}, StoredName={StoredName}, Size={Size}, MimeType={MimeType}, Path={Path}",
                attachment.FileName, attachment.StoredFileName, attachment.Size, attachment.MimeType, fullPath);
        }

        // 显式将附件添加到变更跟踪器，确保 SaveChangesAsync 能正确持久化。
        // 不依赖 thread.Attachments 导航属性（thread 在首次 SaveChanges 后状态可能不稳定）。
        foreach (var attachment in attachments)
        {
            attachment.ThreadId = thread.Id;
            await _unitOfWork.Attachments.AddAsync(attachment);
        }

        thread.HasAttachments = attachments.Count > 0;
        conversation.HasAttachments = conversation.HasAttachments || thread.HasAttachments;

        await _unitOfWork.SaveChangesAsync();

        // T000121: 将正文中的 cid: 内嵌图片引用替换为附件下载 URL，
        // 使会话记录中的内嵌图片能通过已鉴权的附件端点正常加载。
        // 必须先 SaveChanges 生成附件 Id，才能建立 ContentId → 附件 Id 的映射。
        if (attachments.Count > 0 && !string.IsNullOrEmpty(thread.Body))
        {
            var replacedBody = ReplaceEmbeddedImageCidReferences(thread.Body, attachments);
            if (replacedBody != thread.Body)
            {
                thread.Body = replacedBody;
                await _unitOfWork.SaveChangesAsync();
                _logger.LogDebug("邮件正文内嵌图片 cid 引用已替换为附件下载 URL：ThreadId={ThreadId}, Count={Count}",
                    thread.Id, attachments.Count);
            }
        }

        _logger.LogInformation("邮件附件保存成功：ThreadId={ThreadId}, ConversationId={ConversationId}, Count={Count}, ThreadHasAttachments={ThreadHasAttachments}",
            thread.Id, conversation.Id, attachments.Count, thread.HasAttachments);
    }

    /// <summary>
    /// T000121: 将邮件正文中的 &lt;img src="cid:xxx"&gt; 内嵌图片引用替换为附件下载 URL。
    /// 建立 ContentId → 附件 Id 的映射，仅对已保存为附件的内嵌图片做替换；
    /// 未匹配到的 cid 引用保持原样，交由前端渲染时忽略。
    /// </summary>
    private static string ReplaceEmbeddedImageCidReferences(string body, List<Attachment> attachments)
    {
        var cidMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var attachment in attachments)
        {
            if (string.IsNullOrEmpty(attachment.ContentId))
            {
                continue;
            }

            // MimeKit 的 ContentId 通常不含尖括号，但正文中可能以 <...> 形式引用，需归一化以兼容两种写法。
            var normalized = attachment.ContentId.Trim();
            if (normalized.Length > 1 && normalized.StartsWith('<') && normalized.EndsWith('>'))
            {
                normalized = normalized.Substring(1, normalized.Length - 2);
            }

            cidMap[normalized] = attachment.Id;
            if (attachment.ContentId.Trim() != normalized)
            {
                cidMap[attachment.ContentId.Trim()] = attachment.Id;
            }
        }

        if (cidMap.Count == 0)
        {
            return body;
        }

        return Regex.Replace(
            body,
            @"\bsrc\s*=\s*(['""])cid:([^'""]+)\1",
            match =>
            {
                var cidValue = match.Groups[2].Value.Trim();
                if (cidValue.Length > 1 && cidValue.StartsWith('<') && cidValue.EndsWith('>'))
                {
                    cidValue = cidValue.Substring(1, cidValue.Length - 2);
                }

                if (cidMap.TryGetValue(cidValue, out var attachmentId))
                {
                    var quote = match.Groups[1].Value;
                    return $"src={quote}/api/attachments/{attachmentId}/download{quote}";
                }

                return match.Value;
            });
    }

    /// <summary>
    /// 事务回滚后清理已写入磁盘的孤儿附件文件，保持 DB 与文件系统一致性。
    /// </summary>
    private void CleanupOrphanAttachmentFiles(List<string> savedAttachmentPaths)
    {
        foreach (var filePath in savedAttachmentPaths)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogWarning("已清理孤儿附件文件（事务回滚）：{FilePath}", filePath);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "清理孤儿附件文件失败：{FilePath}", filePath);
            }
        }
    }

    /// <summary>
    /// 事务提交后触发后置工作流：自动回复邮件、工作流规则。
    /// 工作流执行失败被隔离捕获，不影响主邮件收取流程。
    /// </summary>
    private async Task TriggerPostIngestionWorkflowsAsync(Conversation conversation, Mailbox mailbox, bool isNewConversation)
    {
        if (isNewConversation)
        {
            await SendAutoReplyAsync(conversation, mailbox);

            try
            {
                await _workflowService.ExecuteForConversationAsync(conversation.Id, WorkflowConst.TriggerNewConversation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发工作流失败（新会话创建）：ConversationId={ConversationId}", conversation.Id);
            }
        }
        else
        {
            try
            {
                await _workflowService.ExecuteForConversationAsync(conversation.Id, WorkflowConst.TriggerNewReply);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发工作流失败（收到新回复）：ConversationId={ConversationId}", conversation.Id);
            }
        }
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

    private async Task HandleBounceAsync(BounceInfo bounceInfo, int mailboxId)
    {
        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            SendLog? sendLog = null;

            if (!string.IsNullOrEmpty(bounceInfo.OriginalMessageId))
            {
                sendLog = await _unitOfWork.SendLogs.FindByMessageIdAsync(bounceInfo.OriginalMessageId);
            }

            if (sendLog == null && !string.IsNullOrEmpty(bounceInfo.RecipientEmail))
            {
                sendLog = await _unitOfWork.SendLogs.FindRecentByEmailAsync(bounceInfo.RecipientEmail, mailboxId);
            }

            if (sendLog != null)
            {
                sendLog.Status = SendLogStatusConst.DeliveryError;
                sendLog.ErrorMessage = BuildErrorMessage(bounceInfo);
                sendLog.AttemptCount++;

                // sendLog 已通过 FindByMessageIdAsync 追踪，无需调用 UpdateAsync

                if (sendLog.ThreadId.HasValue)
                {
                    // sendLog.Thread 已通过 Include 加载且被追踪，连同 Conversation 一起追踪
                    var thread = sendLog.Thread;
                    if (thread != null && thread.Conversation != null)
                    {
                        if (bounceInfo.BounceType == "Permanent")
                        {
                            thread.Conversation.Status = ConversationStatus.Pending;
                            thread.Conversation.UpdatedAt = DateTime.UtcNow;
                        }

                        var bounceThread = new ConversationThread
                        {
                            ConversationId = thread.ConversationId,
                            Type = ThreadType.Note,
                            State = Domain.Entities.ThreadState.Active,
                            Body = BuildBounceNoteBody(bounceInfo),
                            Subject = "退信通知",
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        await _unitOfWork.Threads.AddAsync(bounceThread);
                    }
                }

                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("更新发送日志为退信：SendLogId={SendLogId}, MessageId={MessageId}",
                    sendLog.Id, sendLog.MessageId);
            }
            else
            {
                var newSendLog = new SendLog
                {
                    MessageId = bounceInfo.OriginalMessageId ?? string.Empty,
                    Email = bounceInfo.RecipientEmail ?? string.Empty,
                    MailType = SendLogMailTypeConst.EmailToCustomer,
                    Status = SendLogStatusConst.DeliveryError,
                    ErrorMessage = BuildErrorMessage(bounceInfo),
                    MailboxId = mailboxId,
                    CreatedAt = DateTime.UtcNow,
                    AttemptCount = 1
                };

                await _unitOfWork.SendLogs.AddAsync(newSendLog);
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("创建新的退信发送日志：Recipient={Recipient}", newSendLog.Email);
            }
        }
        catch
        {
            await transaction.SafeRollbackAsync();
            throw;
        }
    }

    private static string BuildErrorMessage(BounceInfo bounceInfo)
    {
        var messages = new List<string>();

        if (!string.IsNullOrEmpty(bounceInfo.BounceType))
        {
            messages.Add($"退信类型: {bounceInfo.BounceType}");
        }
        if (!string.IsNullOrEmpty(bounceInfo.StatusCode))
        {
            messages.Add($"状态码: {bounceInfo.StatusCode}");
        }
        if (!string.IsNullOrEmpty(bounceInfo.Action))
        {
            messages.Add($"操作: {bounceInfo.Action}");
        }
        if (!string.IsNullOrEmpty(bounceInfo.DiagnosticCode))
        {
            messages.Add($"诊断代码: {bounceInfo.DiagnosticCode}");
        }
        if (!string.IsNullOrEmpty(bounceInfo.Description))
        {
            messages.Add($"描述: {bounceInfo.Description}");
        }

        return string.Join("; ", messages);
    }

    private static string BuildBounceNoteBody(BounceInfo bounceInfo)
    {
        var body = "<div style=\"padding: 12px; background-color: #fff5f5; border-left: 4px solid #fc8181; border-radius: 4px;\">";
        body += "<h4 style=\"margin: 0 0 8px 0; color: #c53030;\">邮件退信通知</h4>";
        body += "<ul style=\"margin: 0; padding-left: 20px;\">";

        if (!string.IsNullOrEmpty(bounceInfo.RecipientEmail))
        {
            body += $"<li><strong>收件人:</strong> {bounceInfo.RecipientEmail}</li>";
        }
        if (!string.IsNullOrEmpty(bounceInfo.BounceType))
        {
            body += $"<li><strong>退信类型:</strong> {bounceInfo.BounceType}</li>";
        }
        if (!string.IsNullOrEmpty(bounceInfo.StatusCode))
        {
            body += $"<li><strong>状态码:</strong> {bounceInfo.StatusCode}</li>";
        }
        if (!string.IsNullOrEmpty(bounceInfo.DiagnosticCode))
        {
            body += $"<li><strong>诊断代码:</strong> {bounceInfo.DiagnosticCode}</li>";
        }
        if (!string.IsNullOrEmpty(bounceInfo.Description))
        {
            body += $"<li><strong>详细信息:</strong><br>{bounceInfo.Description}</li>";
        }

        body += "</ul></div>";

        return body;
    }

    private async Task SendAutoReplyAsync(Conversation conversation, Mailbox mailbox)
    {
        try
        {
            if (!mailbox.AutoReplyEnabled || string.IsNullOrEmpty(mailbox.AutoReplyMessage))
            {
                _logger.LogDebug("邮箱未启用自动回复或自动回复内容为空：MailboxId={MailboxId}, ConversationId={ConversationId}",
                    mailbox.Id, conversation.Id);
                return;
            }

            _logger.LogInformation("邮件自动回复开始：ConversationId={ConversationId}, MailboxId={MailboxId}, CustomerEmail={CustomerEmail}",
                conversation.Id, mailbox.Id, conversation.CustomerEmail);

            var subject = string.IsNullOrEmpty(mailbox.AutoReplySubject)
                ? $"自动回复: {conversation.Subject}"
                : mailbox.AutoReplySubject;

            var autoReplyVars = new Dictionary<string, string>
            {
                ["customer_name"] = conversation.CustomerEmail ?? string.Empty,
                ["conversation_id"] = conversation.Number.ToString(),
                ["conversation_url"] = $"{_frontendUrl}/conversations/{conversation.Id}"
            };
            var body = _renderer.Render(mailbox.AutoReplyMessage ?? string.Empty, autoReplyVars, new RenderOptions { HtmlEncode = false });

            var lastCustomerThread = await _unitOfWork.Threads.GetLatestCustomerThreadAsync(conversation.Id);
            string? inReplyTo = null;
            string? references = null;
            string? fsMarker = null;
            if (lastCustomerThread != null)
            {
                inReplyTo = lastCustomerThread.MessageId;
                // RFC 5322 §3.6.4：References 必须包含完整祖先链
                // = 父邮件的 References + 父邮件的 Message-Id
                var parentReferences = EmailHeaderHelper.ExtractReferences(lastCustomerThread.Headers);
                references = EmailHeaderHelper.BuildReferences(parentReferences, lastCustomerThread.MessageId);

                fsMarker = $"{{#FS:{lastCustomerThread.Id}#}}";
            }

            if (!subject.Contains($"[#{conversation.Number}]"))
            {
                subject = $"[#{conversation.Number}] {subject}";
            }

            // 创建系统线程记录用于自动回复，使 EmailQueueService 在发送成功后能将 MessageId 写回 Thread 表。
            // 不复用客户线程，避免覆盖客户原始 MessageId 导致重复邮件检测失效。
            var autoReplyBody = fsMarker != null
                ? body + $"\n<div style=\"display:none !important;visibility:hidden;mso-hide:all;height:0;max-height:0;overflow:hidden;font-size:0;line-height:0;\">{fsMarker}</div>"
                : body;
            var autoReplyThread = new ConversationThread
            {
                ConversationId = conversation.Id,
                CustomerId = conversation.CustomerId,
                Type = ThreadType.Message,
                SourceVia = SourceVia.User,
                SourceType = SourceType.Email,
                From = "system",
                To = conversation.CustomerEmail ?? string.Empty,
                Body = autoReplyBody,
                SendStatus = SendStatus.Pending,
                IsInternal = false,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.Threads.AddAsync(autoReplyThread);
            await _unitOfWork.SaveChangesAsync();

            await _emailQueueService.EnqueueEmailAsync(mailbox.Id, conversation.CustomerEmail ?? string.Empty, subject, body,
                threadId: autoReplyThread.Id, inReplyTo: inReplyTo, references: references,
                customerId: conversation.CustomerId);

            _logger.LogInformation("自动回复邮件已加入队列：ConversationId={ConversationId}, To={To}",
                conversation.Id, conversation.CustomerEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送自动回复邮件失败：ConversationId={ConversationId}", conversation.Id);
        }
    }
}
