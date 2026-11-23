using NoneadCSS.Application.Common;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using MailKit;
using Microsoft.Extensions.Logging;
using MimeKit;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// 邮件处理服务 - 实现邮件解析、线程匹配、附件提取等核心功能
/// </summary>
public class EmailProcessingService : IEmailProcessingService
{
    private readonly ILogger<EmailProcessingService> _logger;
    private readonly IVirusScanService _virusScanService;

    // 回复分隔符模式（用于裁剪历史回复）
    // T000058: 精简模式，移除过于宽泛的模式（如此致、敬礼、From:.*、发件人: 等），
    // 避免误匹配邮件正文或签名导致内容被错误裁剪。
    // T000059: 放宽行尾匹配（允许 wrote: 后有 HTML 标签），增加 Outlook <b>From:</b> 头块匹配。
    // 注意：TrimReplyContent 中有安全检查，如果裁剪后无可见文本会跳过裁剪，
    // 避免 Gmail 引用块在邮件开头时误裁剪导致内容丢失。
    private static readonly string[] ReplySeparators = new[]
    {
        // 英文邮件客户端
        // Gmail/Outlook: "On [date], [name] wrote:" —— 放宽行尾，允许 wrote: 后有 <br> 等 HTML 标签
        @"^On\s.+\swrote:.*$",
        @"^On\s.+\sat\s.+\swrote:.*$",
        // "-----Original Message-----"
        @"^-{5,}\s*Original\s+Message\s*-{5,}.*$",
        // Outlook 回复头块：From: 用 <b> 标签加粗（<b>From:</b>），避免误匹配签名中的 from 文本
        @"^.*<b>From:</b>.*$",
        // 引用行 "&gt; On [date], [name] wrote:"
        @"^&gt;.+wrote:.*$",

        // 中文邮件客户端
        @"^On.*写道:.*$",                               // "On [date] [name] 写道:"
        @"^在.*写道:.*$",                               // "在 [date] [name] 写道:"
        @"^-{4,}.+原始邮件.+-{4,}.*$",                  // "----原始邮件----"
        @"^-{4,}.+发件人.+-{4,}.*$",                    // "----发件人----"
        @"^-{5,}\s*原始邮件\s*-{5,}.*$",                // "-----原始邮件-----"
        @"^-{5,}\s*联系信息\s*-{5,}.*$",                // "------联系信息------"
        @"^={3,}\s*原始邮件\s*={3,}.*$",                // "===原始邮件==="
        @"^─{3,}\s*原始邮件\s*─{3,}.*$",                // "───原始邮件───"
    };

    public EmailProcessingService(ILogger<EmailProcessingService> logger, IVirusScanService virusScanService)
    {
        _logger = logger;
        _virusScanService = virusScanService;
    }

    /// <summary>
    /// 解析邮件并提取关键信息
    /// </summary>
    public EmailParseResult ParseEmail(MimeMessage message, int mailboxId)
    {
        var result = new EmailParseResult
        {
            MessageId = string.IsNullOrEmpty(message.MessageId) ? GenerateFallbackMessageId(message, mailboxId) : message.MessageId,
            Subject = DecodeSubject(message.Subject),
            Date = message.Date.UtcDateTime,
            HasAttachments = GetAttachmentParts(message).Any(),
            RawHeaders = ExtractRawHeaders(message)
        };

        _logger.LogDebug("[邮件解析] 开始解析邮件：MailboxId={MailboxId}, MessageId={MessageId}, Subject={Subject}",
            mailboxId, result.MessageId, result.Subject);

        // 解析 In-Reply-To
        result.InReplyTo = message.InReplyTo;
        _logger.LogDebug("[邮件解析] In-Reply-To={InReplyTo}", result.InReplyTo);

        // 解析 References
        if (message.References != null)
        {
            result.References = message.References.ToList();
            _logger.LogDebug("[邮件解析] References 链长度={Count}", result.References.Count);
        }

        // 解析发件人
        if (message.From != null && message.From.Count > 0)
        {
            var from = message.From[0] as MailboxAddress;
            if (from != null)
            {
                result.FromEmail = from.Address;
                result.FromName = from.Name;
            }
        }
        _logger.LogDebug("[邮件解析] 发件人：FromEmail={FromEmail}, FromName={FromName}",
            result.FromEmail, result.FromName);

        // 解析收件人
        if (message.To != null)
        {
            result.ToAddresses = message.To.OfType<MailboxAddress>()
                .Select(a => new EmailAddress { Name = a.Name, Address = a.Address })
                .ToList();
        }
        _logger.LogDebug("[邮件解析] 收件人：ToCount={ToCount}, 列表={ToList}",
            result.ToAddresses?.Count ?? 0,
            result.ToAddresses != null ? string.Join(";", result.ToAddresses.Select(a => a.Address)) : "");

        // 解析抄送
        if (message.Cc != null)
        {
            result.CcAddresses = message.Cc.OfType<MailboxAddress>()
                .Select(a => new EmailAddress { Name = a.Name, Address = a.Address })
                .ToList();
        }
        _logger.LogDebug("[邮件解析] 抄送：CcCount={CcCount}", result.CcAddresses?.Count ?? 0);

        // 解析密送
        if (message.Bcc != null)
        {
            result.BccAddresses = message.Bcc.OfType<MailboxAddress>()
                .Select(a => new EmailAddress { Name = a.Name, Address = a.Address })
                .ToList();
        }
        _logger.LogDebug("[邮件解析] 密送：BccCount={BccCount}", result.BccAddresses?.Count ?? 0);

        // 解析正文
        result.HtmlBody = GetHtmlBody(message) ?? string.Empty;
        result.TextBody = GetTextBody(message) ?? string.Empty;
        _logger.LogDebug("[邮件解析] 正文提取结果：HtmlBodyLength={HtmlLen}, TextBodyLength={TextLen}",
            result.HtmlBody.Length, result.TextBody.Length);

        // 附件统计
        var attachmentParts = GetAttachmentParts(message);
        _logger.LogDebug("[邮件解析] 附件：AttachmentCount={Count}, 明细={Details}",
            attachmentParts.Count,
            string.Join("; ", attachmentParts.Select(p => $"{p.ContentType?.MimeType}:{p.ContentDisposition?.FileName ?? p.ContentType?.Name}")));

        // 检测自动回复
        result.IsAutoReply = DetectAutoReply(message);
        _logger.LogDebug("[邮件解析] 自动回复检测：IsAutoReply={IsAutoReply}", result.IsAutoReply);

        // 检测退信并提取详细信息
        var bounceInfo = DetectBounce(message);
        result.IsBounce = bounceInfo != null;
        result.BounceInfo = bounceInfo;
        if (result.IsBounce)
        {
            _logger.LogDebug("[邮件解析] 退信检测：IsBounce=true, Type={Type}, StatusCode={Code}, Recipient={Recipient}, OriginalMessageId={OrigMsgId}",
                bounceInfo?.BounceType, bounceInfo?.StatusCode, bounceInfo?.RecipientEmail, bounceInfo?.OriginalMessageId);
        }

        _logger.LogDebug("[邮件解析] 完成：MessageId={MessageId}", result.MessageId);
        return result;
    }

    /// <summary>
    /// 根据邮件头查找关联的会话（核心线程匹配逻辑）
    /// </summary>
    public async Task<Conversation?> FindRelatedConversationAsync(MimeMessage message, int mailboxId, IUnitOfWork unitOfWork)
    {
        _logger.LogInformation("开始查找关联会话：MessageId={MessageId}, InReplyTo={InReplyTo}, Subject={Subject}",
            message.MessageId, message.InReplyTo, message.Subject);

        // 1. 首先通过 Message-ID 查找已存在的线程
        if (!string.IsNullOrEmpty(message.MessageId))
        {
            var existingThread = await FindThreadByMessageIdAsync(message.MessageId, unitOfWork);
            if (existingThread != null)
            {
                _logger.LogInformation("找到已存在的线程，MessageId={MessageId}，ThreadId={ThreadId}",
                    message.MessageId, existingThread.Id);
                return existingThread.Conversation;
            }
        }

        // 2. 通过 In-Reply-To 查找关联的线程
        if (!string.IsNullOrEmpty(message.InReplyTo))
        {
            var replyToThread = await FindThreadByMessageIdAsync(message.InReplyTo, unitOfWork);
            if (replyToThread != null)
            {
                _logger.LogInformation("通过In-Reply-To找到关联会话，InReplyTo={InReplyTo}，ConversationId={ConversationId}",
                    message.InReplyTo, replyToThread.ConversationId);
                return replyToThread.Conversation;
            }

            // 2b. T000057: 从 In-Reply-To 中提取 ThreadId（客服回复邮件的 MessageId 格式为 reply-{threadId}-{guid}@{domain}）
            // 某些邮件客户端或网关可能修改 MessageId 的格式（如添加/删除尖括号、修改大小写），
            // 导致精确匹配失败。从 MessageId 中提取 ThreadId 后直接查询，作为兜底匹配。
            var threadIdFromMessageId = ExtractThreadIdFromMessageId(message.InReplyTo);
            if (threadIdFromMessageId.HasValue)
            {
                var threadById = await unitOfWork.Threads.GetByIdAsync(threadIdFromMessageId.Value);
                if (threadById != null)
                {
                    _logger.LogInformation("通过In-Reply-To提取ThreadId找到关联会话，InReplyTo={InReplyTo}，ThreadId={ThreadId}，ConversationId={ConversationId}",
                        message.InReplyTo, threadIdFromMessageId.Value, threadById.ConversationId);
                    return threadById.Conversation;
                }
            }
        }
        
        // 3. 通过 References 链查找（从最近的往最早的找）
        if (message.References != null && message.References.Count > 0)
        {
            foreach (var reference in message.References.Reverse())
            {
                var refThread = await FindThreadByMessageIdAsync(reference, unitOfWork);
                if (refThread != null)
                {
                    _logger.LogInformation("通过References找到关联会话，Reference={Reference}，ConversationId={ConversationId}",
                        reference, refThread.ConversationId);
                    return refThread.Conversation;
                }

                // 3b. T000057: 同步骤 2b，从 Reference 中提取 ThreadId
                var threadIdFromRef = ExtractThreadIdFromMessageId(reference);
                if (threadIdFromRef.HasValue)
                {
                    var threadById = await unitOfWork.Threads.GetByIdAsync(threadIdFromRef.Value);
                    if (threadById != null)
                    {
                        _logger.LogInformation("通过References提取ThreadId找到关联会话，Reference={Reference}，ThreadId={ThreadId}，ConversationId={ConversationId}",
                            reference, threadIdFromRef.Value, threadById.ConversationId);
                        return threadById.Conversation;
                    }
                }
            }
        }
        
        // 4. 尝试从正文中提取 NoneadCSS 标记（应对邮件服务商删除 Message-ID 的情况）
        var markerId = ExtractNoneadCSSMarker(message);
        if (markerId.HasValue)
        {
            var markerThread = await unitOfWork.Threads.GetByIdAsync(markerId.Value);
            if (markerThread != null)
            {
                _logger.LogInformation("通过正文标记找到关联会话，ThreadId={ThreadId}，ConversationId={ConversationId}", 
                    markerId.Value, markerThread.ConversationId);
                return markerThread.Conversation;
            }
        }
        
        // 5. 尝试从邮件主题中提取会话 ID（如 [#1234] 格式）
        var subjectConvId = ExtractConversationIdFromSubject(message.Subject);
        if (subjectConvId.HasValue)
        {
            // 主题标记 [#数字] 中的数字是面向用户的会话编号 (Conversation.Number)，
            // 不是数据库主键 Id。必须用 GetByNumberAsync 查询，否则当 Number != Id 时匹配失败。
            var subjectConversation = await unitOfWork.Conversations.GetByNumberAsync(subjectConvId.Value);
            if (subjectConversation != null && subjectConversation.MailboxId == mailboxId)
            {
                _logger.LogInformation("通过主题标记找到关联会话，ConversationNumber={ConversationNumber}, ConversationId={ConversationId}",
                    subjectConvId.Value, subjectConversation.Id);
                return subjectConversation;
            }
        }

        // 6. 主题+发件人兜底匹配
        var normalizedSubject = NormalizeSubject(message.Subject);
        if (!string.IsNullOrEmpty(normalizedSubject) && message.From != null && message.From.Count > 0)
        {
            var fromAddress = (message.From[0] as MailboxAddress)?.Address;
            if (!string.IsNullOrEmpty(fromAddress))
            {
                // 兜底匹配允许命中已关闭会话（转工单/解决后会关闭会话），
                // 由 AddReplyToExistingConversationAsync 负责重开已关闭会话。
                // 限制只匹配最近 30 天内更新的会话，避免匹配到历史已关闭会话。
                var sinceDate = DateTime.UtcNow.AddDays(-30);
                var conversations = await unitOfWork.Conversations.FindAsync(c =>
                    c.MailboxId == mailboxId &&
                    c.CustomerEmail == fromAddress &&
                    c.State == ConversationState.Published &&
                    c.LastReplyAt >= sinceDate);

                var matchedConversation = conversations
                    .Where(c => NormalizeSubject(c.Subject) == normalizedSubject)
                    .OrderByDescending(c => c.LastReplyAt)
                    .FirstOrDefault();

                if (matchedConversation != null)
                {
                    _logger.LogInformation("通过主题+发件人兜底匹配找到关联会话，ConversationId={ConversationId}, Subject={Subject}", 
                        matchedConversation.Id, matchedConversation.Subject);
                    return matchedConversation;
                }
            }
        }
        
        _logger.LogInformation("未找到关联会话，将创建新会话。MessageId={MessageId}, InReplyTo={InReplyTo}, Subject={Subject}",
            message.MessageId, message.InReplyTo, message.Subject);
        return null;
    }

    /// <summary>
    /// 从邮件创建线程对象
    /// </summary>
    public ConversationThread CreateThreadFromEmail(MimeMessage message, EmailParseResult parseResult, int customerId, Customer? customer = null)
    {
        _logger.LogDebug("[CreateThreadFromEmail] 开始：MessageId={MessageId}, CustomerId={CustomerId}, Subject={Subject}",
            parseResult.MessageId, customerId, parseResult.Subject);

        // 使用 HTML 正文，如果为空则将纯文本转换为 HTML
        // T000051: 纯文本邮件直接存储会导致前端 dangerouslySetInnerHTML 渲染时换行丢失，
        // 需要将纯文本转换为基本 HTML（转义特殊字符 + 换行转 <br> + 段落包装）
        string body;
        string bodySource;
        if (!string.IsNullOrEmpty(parseResult.HtmlBody))
        {
            body = parseResult.HtmlBody;
            bodySource = "HtmlBody";
        }
        else if (!string.IsNullOrEmpty(parseResult.TextBody))
        {
            // 纯文本转 HTML：先 HTML 转义，再处理换行
            var escaped = System.Net.WebUtility.HtmlEncode(parseResult.TextBody);
            body = $"<p>{escaped.Replace("\r\n", "\n").Replace("\n", "<br>\n")}</p>";
            bodySource = "TextBody(转HTML)";
        }
        else
        {
            body = string.Empty;
            bodySource = "Empty";
        }
        _logger.LogDebug("[CreateThreadFromEmail] 正文来源={Source}, 初始长度={Len}, 前200字符预览={Preview}",
            bodySource, body.Length, body.Length > 200 ? body.Substring(0, 200) : body);

        // T000058: 调整处理顺序为 先消毒 → 后裁剪，避免以下问题：
        // 1. TrimReplyContent 中的 HtmlDecode 会导致已编码实体被解码，后续 Sanitize 可能产生双重编码
        //    （如 &lt; 解码为 < 后被 Sanitizer 重新编码为 &lt;，或注释 <!-- 被当作文本处理）
        // 2. Outlook 邮件中的 <!-- /* Font Definitions */ @font-face{...} --> 注释块，
        //    如果先解码再消毒，CSS 代码可能泄漏为可见文本，导致"没有样式的 CSS 代码"问题
        // 3. 先消毒可确保 HTML 结构正确，正则匹配在合法 HTML 上操作更精确
        var beforeSanitizeLen = body.Length;
        body = HtmlSanitizerHelper.Sanitize(body, _logger);
        _logger.LogDebug("[CreateThreadFromEmail] HtmlSanitizerHelper.Sanitize 完成：消毒前长度={Before}, 消毒后长度={After}, 差值={Diff}",
            beforeSanitizeLen, body.Length, body.Length - beforeSanitizeLen);

        // 裁剪历史回复内容（在已消毒的 HTML 上操作）
        var beforeTrimLen = body.Length;
        body = TrimReplyContent(body);
        _logger.LogDebug("[CreateThreadFromEmail] TrimReplyContent 完成：裁剪前长度={Before}, 裁剪后长度={After}, 差值={Diff}, 最终前200字符预览={Preview}",
            beforeTrimLen, body.Length, body.Length - beforeTrimLen,
            body.Length > 200 ? body.Substring(0, 200) : body);

        var thread = new ConversationThread
        {
            Type = ThreadType.Customer,
            State = Domain.Entities.ThreadState.Active,
            Subject = parseResult.Subject,
            Body = body,
            From = FormatAddress(parseResult.FromEmail, parseResult.FromName),
            To = SerializeAddresses(parseResult.ToAddresses),
            Cc = SerializeAddresses(parseResult.CcAddresses),
            Bcc = SerializeAddresses(parseResult.BccAddresses),
            MessageId = parseResult.MessageId,
            Headers = parseResult.RawHeaders,
            HasAttachments = parseResult.HasAttachments,
            IsFirst = false, // 回复的线程不是首条
            SourceVia = SourceVia.Customer,
            SourceType = SourceType.Email,
            // 优先使用导航属性关联 Customer，避免新客户 customerId=0 时 FK 失败；
            // 若 customer 为 null（回复已有会话场景，已有 customerId > 0），则保留旧的显式 ID 赋值；
            // 若 customer 已存在（Id > 0），同时设置 CustomerId 保持一致，避免 NavigationFixer 冲突
            Customer = customer,
            CustomerId = customer == null ? customerId : (customer.Id > 0 ? customer.Id : null),
            CreatedByCustomerId = customer == null ? customerId : (customer.Id > 0 ? customer.Id : null),
            CreatedAt = parseResult.Date,
            UpdatedAt = parseResult.Date
        };

        _logger.LogDebug("[CreateThreadFromEmail] 完成：最终Body长度={Len}", thread.Body.Length);
        return thread;
    }

    /// <summary>
    /// 保存邮件附件
    /// </summary>
    public async Task<List<Attachment>> SaveAttachmentsAsync(MimeMessage message, int threadId, string attachmentBasePath)
    {
        var attachments = new List<Attachment>();

        var attachmentParts = GetAttachmentParts(message);
        if (attachmentParts.Count == 0)
        {
            return attachments;
        }

        // 确保目录存在
        if (!Directory.Exists(attachmentBasePath))
        {
            Directory.CreateDirectory(attachmentBasePath);
        }

        foreach (var attachment in attachmentParts)
        {
            try
            {
                var fileName = attachment.ContentDisposition?.FileName
                    ?? attachment.ContentType?.Name
                    ?? $"attachment_{Guid.NewGuid():N}";

                // 清理文件名中的非法字符
                fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));

                var mimeType = attachment.ContentType?.MimeType ?? "application/octet-stream";

                // 先将内容读取到内存流中进行病毒扫描（扫描通过后才写入磁盘）
                using var memoryStream = new MemoryStream();
                if (attachment is MessagePart)
                {
                    var rfc822 = (MessagePart)attachment;
                    rfc822.Message.WriteTo(memoryStream);
                }
                else
                {
                    var part = (MimePart)attachment;
                    part.Content.DecodeTo(memoryStream);
                }

                memoryStream.Position = 0;

                // 病毒扫描：在写入磁盘前扫描内存中的内容
                var scanResult = await _virusScanService.ScanAsync(memoryStream, fileName, mimeType);
                if (!scanResult.IsClean)
                {
                    _logger.LogWarning("邮件附件病毒扫描未通过，已跳过: FileName={FileName}, Virus={VirusName}, Message={Message}",
                        fileName, scanResult.VirusName, scanResult.Message);
                    continue;
                }

                // 生成唯一文件名（避免冲突）
                var uniqueFileName = $"{Guid.NewGuid():N}_{fileName}";
                var filePath = Path.Combine(attachmentBasePath, uniqueFileName);

                // 扫描通过后才保存到磁盘
                memoryStream.Position = 0;
                using (var fileStream = File.Create(filePath))
                {
                    await memoryStream.CopyToAsync(fileStream);
                }

                var fileInfo = new FileInfo(filePath);

                var attachmentEntity = new Attachment
                {
                    ThreadId = threadId,
                    FileName = fileName,
                    StoredFileName = uniqueFileName,
                    FileDir = Path.GetFileName(attachmentBasePath),
                    MimeType = mimeType,
                    Size = fileInfo.Length,
                    Type = (AttachmentType)GetAttachmentType(mimeType),
                    Embedded = IsEmbeddedImage(attachment),
                    // T000121: 记录 ContentId，用于将正文中的 cid: 引用映射到附件下载 URL
                    ContentId = attachment.ContentId
                };

                attachments.Add(attachmentEntity);

                _logger.LogInformation("保存附件：{FileName}，大小：{Size}字节", uniqueFileName, fileInfo.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存附件失败");
            }
        }

        return attachments;
    }

    /// <summary>
    /// 生成 Message-ID（用于发送回复）
    /// 格式：reply-{thread_id}-{hash}@{domain}
    /// </summary>
    public string GenerateMessageId(int threadId, string domain)
    {
        // 生成16位校验哈希
        var hashInput = $"{threadId}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
        var hash = Convert.ToBase64String(hashBytes)[..16].Replace('+', '-').Replace('/', '_');
        
        return $"reply-{threadId}-{hash}@{domain}";
    }

    #region 私有辅助方法

    /// <summary>
    /// T000057: 从 MessageId 中提取 ThreadId。
    /// 客服回复邮件的 MessageId 格式为 reply-{threadId}-{guid}@{domain}，
    /// 当邮件网关修改 MessageId 格式导致精确匹配失败时，可通过提取的 ThreadId 直接查找线程。
    /// </summary>
    private static int? ExtractThreadIdFromMessageId(string? messageId)
    {
        if (string.IsNullOrEmpty(messageId))
            return null;

        var match = System.Text.RegularExpressions.Regex.Match(messageId, @"^reply-(\d+)-", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var threadId))
        {
            return threadId;
        }

        return null;
    }

    /// <summary>
    /// 查找线程（通过 MessageId，去除 notify- 前缀）
    /// </summary>
    private async Task<ConversationThread?> FindThreadByMessageIdAsync(string messageId, IUnitOfWork unitOfWork)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            return null;
        }
        
        // 直接匹配
        var thread = await unitOfWork.Threads.FindByMessageIdAsync(messageId);
        if (thread != null)
        {
            return thread;
        }
        
        // 如果是通知邮件的 MessageId（notify-xxx 格式），提取原始 MessageId
        if (messageId.StartsWith("notify-", StringComparison.OrdinalIgnoreCase))
        {
            var originalMessageId = messageId[7..]; // 去除 "notify-" 前缀
            thread = await unitOfWork.Threads.FindByMessageIdAsync(originalMessageId);
            if (thread != null)
            {
                return thread;
            }
        }
        
        return null;
    }

    /// <summary>
    /// 解码邮件主题
    /// </summary>
    private string DecodeSubject(string? subject)
    {
        if (string.IsNullOrEmpty(subject))
        {
            return string.Empty;
        }
        
        // MailKit 会自动解码主题，这里做简单的清理
        return subject.Trim();
    }

    /// <summary>
    /// 提取原始邮件头
    /// </summary>
    private string ExtractRawHeaders(MimeMessage message)
    {
        try
        {
            using var stream = new MemoryStream();
            message.WriteTo(stream);
            stream.Position = 0;
            
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = reader.ReadToEnd();
            
            // 提取邮件头部分（到第一个空行）
            var headerEnd = content.IndexOf("\r\n\r\n");
            if (headerEnd > 0)
            {
                return content[..headerEnd];
            }
            
            return content[..Math.Min(8000, content.Length)];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "提取邮件原始头失败");
            return string.Empty;
        }
    }

    /// <summary>
    /// 生成备用 Message-ID（邮件缺少 Message-ID 头时）
    /// </summary>
    private string GenerateFallbackMessageId(MimeMessage message, int mailboxId)
    {
        var hashInput = $"{mailboxId}-{message.Date.Ticks}-{message.From}";
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        
        return $"generated-{hash}@NoneadCSS.local";
    }

    /// <summary>
    /// 获取 HTML 正文
    /// </summary>
    private string? GetHtmlBody(MimeMessage message)
    {
        var html = message.HtmlBody;
        if (!string.IsNullOrEmpty(html))
        {
            // 转换 GB2312/GBK 编码的 meta charset
            html = html.Replace("charset=gb2312", "charset=utf-8", StringComparison.OrdinalIgnoreCase)
                       .Replace("charset=gbk", "charset=utf-8", StringComparison.OrdinalIgnoreCase)
                       .Replace("charset=gb18030", "charset=utf-8", StringComparison.OrdinalIgnoreCase);
        }
        return html;
    }

    /// <summary>
    /// 获取纯文本正文
    /// </summary>
    private string? GetTextBody(MimeMessage message)
    {
        return message.TextBody;
    }

    /// <summary>
    /// 检测是否为自动回复邮件
    /// </summary>
    public static bool DetectAutoReply(MimeMessage message)
    {
        // T000057: 修复自动回复误判逻辑
        // 之前只要邮件包含 "precedence" 或 "auto-submitted" 头就判定为自动回复，
        // 导致 Precedence: normal 或 Auto-Submitted: no 的正常客户回复被跳过（接收不到新邮件回复）。
        foreach (var header in message.Headers)
        {
            var name = header.Field.ToLowerInvariant();
            var value = header.Value?.ToLowerInvariant().Trim() ?? "";
            
            // x-autoreply：只要存在即为自动回复
            if (name == "x-autoreply")
            {
                return true;
            }
            
            // auto-submitted：值为 no 时表示非自动回复（RFC 3834），其他值（auto-replied/auto-generated）为自动回复
            if (name == "auto-submitted" && value != "no" && !string.IsNullOrEmpty(value))
            {
                return true;
            }
            
            // precedence：只有 bulk/list/junk 才视为自动回复，normal/first-class 等不视为
            if (name == "precedence" && (value == "bulk" || value == "list" || value == "junk"))
            {
                return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// 检测是否为退信并提取详细信息
    /// </summary>
    private BounceInfo? DetectBounce(MimeMessage message)
    {
        var bounceSignals = 0;
        var bounceInfo = new BounceInfo();
        
        // 检查发件人是否为邮件系统（强信号）
        var from = message.From?.ToString()?.ToLowerInvariant() ?? "";
        if (from.Contains("mailer-daemon") || from.Contains("postmaster"))
        {
            bounceSignals += 2;
        }
        
        // 检查 Content-Type 是否为退信报告（强信号）
        var contentType = message.Body?.ContentType?.MimeType?.ToLowerInvariant() ?? "";
        if (contentType.Contains("multipart/report"))
        {
            bounceSignals += 2;
            
            if (message.Body is Multipart multipart)
            {
                foreach (var part in multipart)
                {
                    if (part.ContentType.MimeType == "message/delivery-status")
                    {
                        ExtractDeliveryStatus(part, bounceInfo);
                    }
                    else if (part.ContentType.MimeType == "text/plain" || part.ContentType.MimeType == "text/html")
                    {
                        ExtractBounceDescription(part, bounceInfo);
                    }
                    else if (part is MessagePart)
                    {
                        ExtractOriginalMessageId((MessagePart)part, bounceInfo);
                    }
                }
            }
        }
        
        // 检查 Return-Path 是否为空或为 <>（弱信号）
        var returnPath = message.Headers["Return-Path"];
        if (!string.IsNullOrEmpty(returnPath) && returnPath.Contains("<>"))
        {
            bounceSignals += 1;
        }
        
        // 检查主题是否包含退信相关关键词（中等信号）
        var subject = message.Subject?.ToLowerInvariant() ?? "";
        if (subject.Contains("delivery failed") || subject.Contains("undeliverable") || 
            subject.Contains("退信") || subject.Contains("投递失败") ||
            subject.Contains("mail delivery failed") || subject.Contains("undelivered mail"))
        {
            bounceSignals += 1;
        }
        
        // 检查 X-Failed-Recipients 头（强信号）
        if (!string.IsNullOrEmpty(message.Headers["X-Failed-Recipients"]))
        {
            bounceSignals += 2;
        }
        
        // 需要至少 2 个信号才能识别为退信
        if (bounceSignals < 2)
        {
            return null;
        }
        
        // 从正文提取原始邮件ID
        if (string.IsNullOrEmpty(bounceInfo.OriginalMessageId))
        {
            bounceInfo.OriginalMessageId = ExtractOriginalMessageIdFromBody(message);
        }
        
        // 从正文提取收件人邮箱
        if (string.IsNullOrEmpty(bounceInfo.RecipientEmail))
        {
            bounceInfo.RecipientEmail = ExtractRecipientEmailFromBody(message);
        }
        
        return bounceInfo;
    }

    /// <summary>
    /// 从 message/delivery-status 部分提取退信详情
    /// </summary>
    private void ExtractDeliveryStatus(MimeEntity part, BounceInfo bounceInfo)
    {
        try
        {
            var text = (part as TextPart)?.Text ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                using var stream = new MemoryStream();
                part.WriteTo(stream);
                text = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
            
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("Action:", StringComparison.OrdinalIgnoreCase))
                {
                    bounceInfo.Action = line.Substring(7).Trim();
                }
                else if (line.StartsWith("Status:", StringComparison.OrdinalIgnoreCase))
                {
                    bounceInfo.StatusCode = line.Substring(7).Trim();
                }
                else if (line.StartsWith("Diagnostic-Code:", StringComparison.OrdinalIgnoreCase))
                {
                    bounceInfo.DiagnosticCode = line.Substring(17).Trim();
                }
                else if (line.StartsWith("Final-Recipient:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = line.Substring(17).Trim();
                    var emailMatch = System.Text.RegularExpressions.Regex.Match(value, @"[\w\.-]+@[\w\.-]+\.\w+");
                    if (emailMatch.Success)
                    {
                        bounceInfo.RecipientEmail = emailMatch.Value;
                    }
                }
            }
            
            // 根据状态码判断退信类型
            if (!string.IsNullOrEmpty(bounceInfo.StatusCode))
            {
                var code = bounceInfo.StatusCode.Split('.')[0];
                bounceInfo.BounceType = code switch
                {
                    "4" => "Transient",
                    "5" => "Permanent",
                    _ => "Unknown"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "解析退信投递状态失败");
        }
    }

    /// <summary>
    /// 从正文提取退信描述
    /// </summary>
    private void ExtractBounceDescription(MimeEntity part, BounceInfo bounceInfo)
    {
        try
        {
            var text = (part as TextPart)?.Text ?? string.Empty;
            if (string.IsNullOrEmpty(text)) return;
            
            // 提取关键信息作为描述
            var diagnosticMatch = System.Text.RegularExpressions.Regex.Match(text, @"Diagnostic-Code:\s*(.+)");
            if (diagnosticMatch.Success)
            {
                bounceInfo.DiagnosticCode = diagnosticMatch.Groups[1].Value.Trim();
            }
            
            if (string.IsNullOrEmpty(bounceInfo.Description))
            {
                // 取前500个字符作为描述
                bounceInfo.Description = text.Length > 500 ? text.Substring(0, 500) + "..." : text;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "解析退信描述失败");
        }
    }

    /// <summary>
    /// 从原始邮件部分提取 Message-ID
    /// </summary>
    private void ExtractOriginalMessageId(MessagePart part, BounceInfo bounceInfo)
    {
        try
        {
            if (!string.IsNullOrEmpty(part.Message.MessageId))
            {
                bounceInfo.OriginalMessageId = part.Message.MessageId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "从原始邮件提取 Message-ID 失败");
        }
    }

    /// <summary>
    /// 从正文中提取原始邮件的 Message-ID
    /// </summary>
    private string? ExtractOriginalMessageIdFromBody(MimeMessage message)
    {
        var body = message.TextBody ?? message.HtmlBody ?? string.Empty;
        
        var messageIdMatch = System.Text.RegularExpressions.Regex.Match(body, @"Message-ID:\s*<([^>]+)>");
        if (messageIdMatch.Success)
        {
            return messageIdMatch.Groups[1].Value;
        }
        
        var referenceMatch = System.Text.RegularExpressions.Regex.Match(body, @"In-Reply-To:\s*<([^>]+)>");
        if (referenceMatch.Success)
        {
            return referenceMatch.Groups[1].Value;
        }
        
        return null;
    }

    /// <summary>
    /// 从正文中提取收件人邮箱
    /// </summary>
    private string? ExtractRecipientEmailFromBody(MimeMessage message)
    {
        var body = message.TextBody ?? message.HtmlBody ?? string.Empty;
        
        // 查找"To:"、"Recipient:"等字段后的邮箱
        var toMatch = System.Text.RegularExpressions.Regex.Match(body, @"To:\s*[\w\.-]+@[\w\.-]+\.\w+");
        if (toMatch.Success)
        {
            var emailMatch = System.Text.RegularExpressions.Regex.Match(toMatch.Value, @"[\w\.-]+@[\w\.-]+\.\w+");
            return emailMatch.Success ? emailMatch.Value : null;
        }
        
        return null;
    }

    /// <summary>
    /// 从正文中提取 NoneadCSS 标记（{#FS:thread_id#}）
    /// </summary>
    private int? ExtractNoneadCSSMarker(MimeMessage message)
    {
        var htmlBody = message.HtmlBody ?? string.Empty;
        var textBody = message.TextBody ?? string.Empty;
        var fullBody = htmlBody + " " + textBody;
        
        // 1. 匹配原始格式 {#FS:123#}
        var match = System.Text.RegularExpressions.Regex.Match(fullBody, @"\{#FS:(\d+)#\}");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var threadId))
        {
            _logger.LogInformation("正文标记匹配成功（原始格式），ThreadId={ThreadId}", threadId);
            return threadId;
        }
        
        // 2. 匹配 HTML 编码格式，如 &#123;#FS:123#&#125; 或 %7B#FS:123#%7D
        match = System.Text.RegularExpressions.Regex.Match(fullBody, @"&#123;#FS:(\d+)#&#125;");
        if (match.Success && int.TryParse(match.Groups[1].Value, out threadId))
        {
            _logger.LogInformation("正文标记匹配成功（HTML编码格式），ThreadId={ThreadId}", threadId);
            return threadId;
        }
        
        // 3. 匹配 URL 编码格式
        match = System.Text.RegularExpressions.Regex.Match(fullBody, @"%7B#FS:(\d+)#%7D");
        if (match.Success && int.TryParse(match.Groups[1].Value, out threadId))
        {
            _logger.LogInformation("正文标记匹配成功（URL编码格式），ThreadId={ThreadId}", threadId);
            return threadId;
        }
        
        // 4. 匹配被分割到多行的情况
        var cleanedBody = System.Text.RegularExpressions.Regex.Replace(fullBody, @"\s+", "");
        match = System.Text.RegularExpressions.Regex.Match(cleanedBody, @"\{#FS:(\d+)#\}");
        if (match.Success && int.TryParse(match.Groups[1].Value, out threadId))
        {
            _logger.LogInformation("正文标记匹配成功（去除空白后），ThreadId={ThreadId}", threadId);
            return threadId;
        }
        
        return null;
    }
    
    /// <summary>
    /// 从邮件主题中提取会话 ID（如 [#1234] 格式）
    /// </summary>
    private int? ExtractConversationIdFromSubject(string subject)
    {
        if (string.IsNullOrEmpty(subject))
        {
            return null;
        }
        
        // 匹配 [#1234] 格式
        var match = System.Text.RegularExpressions.Regex.Match(subject, @"\[#(\d+)\]");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var conversationId))
        {
            return conversationId;
        }
        
        return null;
    }

    /// <summary>
    /// 标准化邮件主题（去除 Re:/Fwd: 前缀、去除 [#数字] 标记）
    /// </summary>
    private string NormalizeSubject(string? subject)
    {
        if (string.IsNullOrEmpty(subject))
        {
            return string.Empty;
        }

        var normalized = subject.Trim();

        // 去除 Re:/Fwd: 等前缀（支持中英文，支持多层嵌套）
        var prefixPattern = @"^(?:(?:Re|Fwd|Fw|回复|转发)\s*:\s*)+";
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, prefixPattern, "", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 去除 [#数字] 标记
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\[#\d+\]", "");

        return normalized.Trim();
    }

    /// <summary>
    /// 裁剪历史回复内容（只保留最新回复）
    /// T000058: 重构裁剪逻辑，移除 HtmlDecode 步骤，避免双重编码问题。
    /// T000059: 增加安全检查 —— 如果裁剪后内容无可见文本（只剩 HTML 标签），
    /// 说明匹配到的是 Gmail 等客户端的引用块（在邮件开头），跳过裁剪避免丢失回复内容。
    /// 该方法应在 HtmlSanitizerHelper.Sanitize 之后调用。
    /// </summary>
    private string TrimReplyContent(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            _logger.LogDebug("[TrimReplyContent] 输入为空，直接返回");
            return body;
        }

        _logger.LogDebug("[TrimReplyContent] 开始裁剪：输入长度={Len}, 分隔符模式总数={PatternCount}",
            body.Length, ReplySeparators.Length);

        int patternIndex = 0;
        // 直接在已消毒的 HTML 上匹配引用分隔符
        foreach (var pattern in ReplySeparators)
        {
            patternIndex++;
            try
            {
                _logger.LogDebug("[TrimReplyContent] 尝试模式 [{Idx}/{Total}]: {Pattern}",
                    patternIndex, ReplySeparators.Length, pattern);

                var match = System.Text.RegularExpressions.Regex.Match(body, pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.Multiline);

                if (match.Success)
                {
                    _logger.LogDebug("[TrimReplyContent] 模式匹配成功 [{Idx}/{Total}]：Pattern={Pattern}, MatchIndex={MatchIdx}, MatchLength={MatchLen}, 匹配内容={MatchValue}",
                        patternIndex, ReplySeparators.Length, pattern, match.Index, match.Length,
                        match.Value.Length > 120 ? match.Value.Substring(0, 120) + "..." : match.Value);

                    // 截取到分隔符之前的内容
                    var trimmed = body[..match.Index].Trim();
                    var trimmedLen = trimmed.Length;
                    var originalLen = body.Length;

                    // T000059: 安全检查 —— 如果裁剪后无可见文本（只剩 HTML 标签或空），
                    // 说明匹配到的分隔符在邮件开头（如 Gmail 的引用块在前面），
                    // 跳过此次裁剪，继续尝试下一个模式，避免丢失回复内容
                    var visibleText = System.Text.RegularExpressions.Regex.Replace(trimmed, @"<[^>]+>", string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(visibleText))
                    {
                        _logger.LogDebug("[TrimReplyContent] 安全检查：裁剪后无可见文本（VisibleLength=0），跳过此模式。" +
                            "裁剪前长度={Before}, 裁剪后长度={After}, 预计删除={Diff}. 原因：匹配到的分隔符在邮件开头，可能是引用块而非真实回复分隔符",
                            originalLen, trimmedLen, originalLen - trimmedLen);
                        continue;
                    }

                    _logger.LogInformation("[TrimReplyContent] 执行裁剪成功：Pattern={Pattern}, 裁剪前长度={Before}, 裁剪后长度={After}, 删除={Diff}字符, 可见文本长度={VisibleLen}, 裁剪点在全文位置 {Pct}%",
                        pattern, originalLen, trimmedLen, originalLen - trimmedLen,
                        visibleText.Length,
                        originalLen > 0 ? (int)((double)match.Index / originalLen * 100) : 0);
                    return trimmed;
                }
                else
                {
                    _logger.LogDebug("[TrimReplyContent] 模式未匹配 [{Idx}/{Total}]: {Pattern}",
                        patternIndex, ReplySeparators.Length, pattern);
                }
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException ex)
            {
                _logger.LogWarning(ex, "[TrimReplyContent] 正则匹配超时，跳过模式 [{Idx}]: {Pattern}",
                    patternIndex, pattern);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TrimReplyContent] 正则匹配异常，跳过模式 [{Idx}]: {Pattern}",
                    patternIndex, pattern);
            }
        }

        _logger.LogDebug("[TrimReplyContent] 所有模式均未匹配或均被安全检查跳过，保留原始内容。最终长度={Len}",
            body.Length);
        return body;
    }

    /// <summary>
    /// 格式化邮箱地址
    /// </summary>
    private string FormatAddress(string email, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return email;
        }
        return $"{name} <{email}>";
    }

    /// <summary>
    /// 序列化邮箱地址列表为 JSON
    /// </summary>
    private string SerializeAddresses(List<EmailAddress> addresses)
    {
        if (addresses == null || addresses.Count == 0)
        {
            return string.Empty;
        }
        return JsonSerializer.Serialize(addresses);
    }

    /// <summary>
    /// 获取附件类型
    /// </summary>
    private int GetAttachmentType(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType))
        {
            return 0; // Other
        }
        
        mimeType = mimeType.ToLowerInvariant();
        
        if (mimeType.StartsWith("image/")) return 1; // Image
        if (mimeType.StartsWith("video/")) return 2; // Video
        if (mimeType.StartsWith("audio/")) return 3; // Audio
        if (mimeType.StartsWith("text/")) return 4; // Text
        if (mimeType == "application/pdf") return 5; // PDF
        
        return 0; // Other
    }

    /// <summary>
    /// 判断是否为内嵌图片
    /// </summary>
    private bool IsEmbeddedImage(MimeEntity entity)
    {
        if (entity.ContentDisposition?.Disposition == "inline")
        {
            return true;
        }

        var contentId = entity.ContentId;
        return !string.IsNullOrEmpty(contentId);
    }

    /// <summary>
    /// 提取邮件中的所有附件（包括 Content-Disposition: attachment 和 inline 附件）。
    /// MimeKit 的 message.Attachments 仅返回 attachment 类型的 body part，
    /// 很多邮件客户端（Outlook、Gmail、Mac Mail 等）发送的附件是 inline 类型，
    /// 需要遍历 message.BodyParts 并按以下规则判断是否为附件：
    /// 1. Content-Disposition 为 attachment → 附件
    /// 2. Content-Disposition 为 inline 且有 FileName → 附件（inline 附件）
    /// 3. Content-Type 为 application/octet-stream → 附件
    /// 跳过：text/plain、text/html（邮件正文）、multipart/*（容器）、PGP 签名等
    /// </summary>
    private List<MimeEntity> GetAttachmentParts(MimeMessage message)
    {
        var result = new List<MimeEntity>();

        foreach (var part in message.BodyParts)
        {
            // 跳过 multipart 容器（multipart/mixed、multipart/alternative 等）
            if (part is Multipart)
            {
                continue;
            }

            // 跳过 PGP 签名（application/pgp-signature）
            var partMimeType = part.ContentType?.MimeType?.ToLowerInvariant();
            if (partMimeType == "application/pgp-signature")
            {
                continue;
            }

            var disposition = part.ContentDisposition;
            var mimeType = partMimeType;

            // 跳过邮件正文（text/plain、text/html），除非显式标记为 attachment
            if ((mimeType == "text/plain" || mimeType == "text/html")
                && disposition?.Disposition != "attachment")
            {
                continue;
            }

            // 判断是否为附件
            var isAttachment = disposition?.Disposition == "attachment";
            var isInlineWithFileName = disposition?.Disposition == "inline"
                && !string.IsNullOrEmpty(disposition?.FileName);
            var isOctetStream = mimeType == "application/octet-stream"
                && disposition?.Disposition != null; // 有 Content-Disposition 头
            // T000121: 内嵌图片通常为 inline 且无文件名，仅通过 Content-ID 被 <img src="cid:..."> 引用，
            // 必须将其纳入附件保存，否则正文中的内嵌图片无法显示。
            var isInlineWithContentId = disposition?.Disposition == "inline"
                && !string.IsNullOrEmpty(part.ContentId);

            if (isAttachment || isInlineWithFileName || isOctetStream || isInlineWithContentId)
            {
                result.Add(part);
            }
        }

        return result;
    }

    /// <summary>
    /// 安全删除文件（忽略删除失败，仅记录日志）
    /// </summary>
    private void SafeDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除恶意附件文件失败: {FilePath}", filePath);
        }
    }

    #endregion
}