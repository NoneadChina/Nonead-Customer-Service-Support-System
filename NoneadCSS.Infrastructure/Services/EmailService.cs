using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using MailKit;
using MailKit.Search;
using MimeKit;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using System.Net;
using NoneadCSS.Infrastructure.Logging;

namespace NoneadCSS.Infrastructure.Services;

public class EmailService : IEmailService
{
    // 邮件重试参数：对应 PHP 的 tries=168, 间隔1小时重试
    private const int DefaultMaxRetries = 168;
    private const int DefaultRetryDelaySeconds = 3600;
    private readonly TimeSpan _firstRetryDelay;

    // 统一的超时值（从 IOptions<RetryOptions> 读取，替代硬编码）
    private readonly int _smtpSendTimeoutMs;
    private readonly int _quickOperationTimeoutMs;
    private readonly int _fetchTimeoutMs;

    private readonly ILogger<EmailService> _logger;
    private readonly string _attachmentBasePath;
    private string _lastSmtpQueueId = string.Empty;

    static EmailService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public EmailService(ILogger<EmailService> logger, IWebHostEnvironment env, IOptions<AttachmentSettings> attachmentSettings, IOptions<RetryOptions> retryOptions)
    {
        _logger = logger;
        _attachmentBasePath = Path.Combine(env.ContentRootPath, attachmentSettings.Value.UploadPath);
        _firstRetryDelay = TimeSpan.FromMinutes(retryOptions.Value.EmailFirstRetryDelayMinutes);
        _smtpSendTimeoutMs = retryOptions.Value.EmailSmtpSendTimeoutMs;
        _quickOperationTimeoutMs = retryOptions.Value.EmailQuickOperationTimeoutMs;
        _fetchTimeoutMs = retryOptions.Value.EmailFetchTimeoutMs;
    }

    private string GetAttachmentFilePath(Attachment attachment)
    {
        var storedName = !string.IsNullOrEmpty(attachment.StoredFileName)
            ? attachment.StoredFileName
            : attachment.FileName;

        var cleanFileDir = (attachment.FileDir ?? string.Empty).Trim().Replace('\\', '/');
        var cleanStoredName = Path.GetFileName(storedName) ?? string.Empty;

        var fullPath = Path.GetFullPath(Path.Combine(_attachmentBasePath, cleanFileDir, cleanStoredName));
        var normalizedBasePath = Path.GetFullPath(_attachmentBasePath);

        if (!fullPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("附件路径遍历攻击拦截: FileDir={FileDir}, FileName={FileName}",
                attachment.FileDir, attachment.FileName);
            throw new InvalidOperationException("无效的文件路径");
        }

        return fullPath;
    }

    /// <summary>
    /// 将附件添加到 BodyBuilder，使用原始 FileName 作为附件显示名（而非磁盘上的 StoredFileName GUID）。
    /// 修复 T000056：之前直接使用 bodyBuilder.Attachments.Add(filePath) 会以 StoredFileName（GUID）
    /// 作为附件名，导致客户收到类似 "26720ad7-ef23" 的乱码文件名。
    /// </summary>
    private void AddAttachmentToBodyBuilder(BodyBuilder bodyBuilder, Attachment attachment)
    {
        var filePath = GetAttachmentFilePath(attachment);
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("附件文件不存在：AttachmentId={AttachmentId}, FilePath={FilePath}",
                attachment.Id, filePath);
            return;
        }

        var contentType = ContentType.TryParse(attachment.MimeType, out var parsed)
            ? parsed
            : new ContentType("application", "octet-stream");

        using var fs = File.OpenRead(filePath);
        bodyBuilder.Attachments.Add(attachment.FileName, fs, contentType);
    }

    /// <summary>
    /// 清理邮件头输入中的控制字符（CR/LF/NUL 等），防御 SMTP 头注入。
    /// 即使底层库（MimeKit）会做编码，这里作为深度防御再清理一次。
    /// </summary>
    private static string SanitizeHeaderInput(string? input, int maxLength = 998)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            // 移除 C0 控制字符（0x00-0x1F）和 DEL（0x7F），保留常见空白(\t除外)和可打印字符
            if (c == '\t') continue;
            if (char.IsControl(c)) continue;
            sb.Append(c);
        }

        var result = sb.ToString().Trim();

        // RFC 5322 §2.1.1: 单行头字段按 UTF-8 字节数不超过 998 octets。
        // 注意：中文/emoji 每个字符 3-4 字节，必须按字节截断（而非字符数），避免超字节导致 MTA 截断或拒收。
        if (Encoding.UTF8.GetByteCount(result) > maxLength)
        {
            // 安全按 UTF-8 字节截断：逐个字符追加，直到加下一个字符就超出，保证不拆坏多字节序列
            var utf8 = Encoding.UTF8;
            var trunc = new StringBuilder(maxLength);
            int bytes = 0;
            foreach (var c in result)
            {
                int cb = utf8.GetByteCount(new[] { c });
                if (bytes + cb > maxLength) break;
                trunc.Append(c);
                bytes += cb;
            }
            result = trunc.ToString();
        }

        return result;
    }

    public async Task SendEmailAsync(Mailbox mailbox, string to, string subject, string body, List<Attachment>? attachments = null, CancellationToken cancellationToken = default)
    {
        await SendEmailWithHeadersAsync(mailbox, to, subject, body, attachments, null, null, null, null, null, EmailConversationHistory.None, cancellationToken);
    }

    /// <summary>
    /// T000106：将会话回环标记 {#FS:threadId#} 规范化为单个"隐藏样式 div"追加到正文末尾，
    /// 避免历史路径丢失隐藏样式后向收件人泄漏 "#FS" 明文。实现见 EmailBodyMarker。
    /// </summary>
    private static string EnsureHiddenSessionMarker(string html)
        => EmailBodyMarker.Normalize(html);

    public async Task SendEmailWithHeadersAsync(Mailbox mailbox, string to, string subject, string body,
        List<Attachment>? attachments = null, string? cc = null, string? bcc = null,
        string? inReplyTo = null, string? references = null, string? messageId = null,
        EmailConversationHistory historyType = EmailConversationHistory.None,
        CancellationToken cancellationToken = default)
    {
        var sanitizedFrom = SensitiveDataHelper.Sanitize("email", mailbox.Email) ?? mailbox.Email;
        var sanitizedTo = SensitiveDataHelper.Sanitize("email", to) ?? to;

        _logger.LogInformationSensitive("邮件发送开始：From={From}, To={To}, Subject={Subject}, HasCc={HasCc}, HasBcc={HasBcc}, AttachmentCount={AttachCount}, HasInReplyTo={HasInReplyTo}, HasReferences={HasReferences}, HasMessageId={HasMessageId}, HistoryType={HistoryType}, MailboxId={MailboxId}",
            sanitizedFrom, sanitizedTo, subject, !string.IsNullOrEmpty(cc), !string.IsNullOrEmpty(bcc),
            attachments?.Count ?? 0, !string.IsNullOrEmpty(inReplyTo), !string.IsNullOrEmpty(references),
            !string.IsNullOrEmpty(messageId), historyType, mailbox.Id);
        try
        {
            // T000106：出站前规范化会话回环标记 {#FS:threadId#}，确保其始终以"隐藏样式 div"呈现。
            // 历史路径（工作流模板渲染/满意度/关闭通知等）生成的标记 div 可能在途中丢失
            // display:none 内联样式，变成可见 <div>{#FS:1115#}</div>，收件端看到明文 "#FS"。
            body = EnsureHiddenSessionMarker(body);

            var message = new MimeMessage();
            var safeDisplayName = SanitizeHeaderInput(mailbox.Name, 200);
            var safeSubject = SanitizeHeaderInput(subject, 78);
            message.From.Add(new MailboxAddress(safeDisplayName, mailbox.Email));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = safeSubject;

            if (!string.IsNullOrEmpty(cc))
            {
                foreach (var ccAddr in cc.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)))
                {
                    message.Cc.Add(MailboxAddress.Parse(ccAddr));
                }
            }

            if (!string.IsNullOrEmpty(bcc))
            {
                foreach (var bccAddr in bcc.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)))
                {
                    message.Bcc.Add(MailboxAddress.Parse(bccAddr));
                }
            }

            // 设置 In-Reply-To 头（对应 PHP 的 headers['In-Reply-To']）
            if (!string.IsNullOrEmpty(inReplyTo))
            {
                message.InReplyTo = inReplyTo;
            }

            // 设置 References 头（RFC 5322 §3.6.4：空格分隔的多个 Message-Id）
            if (!string.IsNullOrEmpty(references))
            {
                // references 可能是空格分隔的多个 Message-Id，需逐个添加
                var messageIdParts = references.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var mid in messageIdParts)
                {
                    if (!message.References.Contains(mid))
                    {
                        message.References.Add(mid);
                    }
                }
            }

            // 设置 Message-ID 头
            if (!string.IsNullOrEmpty(messageId))
            {
                message.MessageId = messageId;
            }

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = body,
                // 生成纯文本 alternative，避免部分邮件客户端/网关在没有 text/plain 时直接显示 HTML 源码
                TextBody = HtmlSanitizerHelper.ConvertToPlainText(body)
            };

            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    AddAttachmentToBodyBuilder(bodyBuilder, attachment);
                }
            }

            message.Body = bodyBuilder.ToMessageBody();

            _logger.LogDebugSensitive("邮件 SMTP 连接中：Host={Host}, Port={Port}, Encryption={Encryption}, OauthEnabled={Oauth}",
                mailbox.OutHost, mailbox.OutPort, mailbox.OutEncryption, mailbox.OauthEnabled);

            using var client = new SmtpClient();
            client.Timeout = _smtpSendTimeoutMs;

            await client.ConnectAsync(mailbox.OutHost, mailbox.OutPort, GetSocketOptions(mailbox.OutEncryption), cancellationToken);

            if (!string.IsNullOrEmpty(mailbox.OutUsername))
            {
                if (mailbox.OauthEnabled && !string.IsNullOrEmpty(mailbox.OauthAccessToken))
                {
                    var oauth2 = new SaslMechanismOAuth2(mailbox.OutUsername, mailbox.OauthAccessToken);
                    await client.AuthenticateAsync(oauth2, cancellationToken);
                }
                else
                {
                    await client.AuthenticateAsync(mailbox.OutUsername, mailbox.OutPassword, cancellationToken);
                }
            }

            _lastSmtpQueueId = $"{message.MessageId}-{DateTime.UtcNow.Ticks}";

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformationSensitive("邮件发送成功：To={To}, Subject={Subject}, MessageId={MessageId}, SmtpQueueId={QueueId}",
                sanitizedTo, subject, message.MessageId, _lastSmtpQueueId);
        }
        catch (Exception ex)
        {
            _logger.LogErrorSensitive("邮件发送失败：To={To}, Subject={Subject}, ErrorType={ErrorType}, ErrorMessage={ErrorMessage}",
                sanitizedTo, subject, ex.GetType().Name, ex.Message);
            throw;
        }
    }

    public async Task SendEmailWithRetryAsync(Mailbox mailbox, string to, string subject, string body,
        List<Attachment>? attachments = null, int maxRetries = DefaultMaxRetries, int retryDelaySeconds = DefaultRetryDelaySeconds,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        var sanitizedTo = SensitiveDataHelper.Sanitize("email", to) ?? to;

        _logger.LogInformationSensitive("邮件发送（带重试）开始：To={To}, Subject={Subject}, MaxRetries={MaxRetries}, RetryDelaySeconds={RetryDelay}, AttachmentCount={AttachCount}",
            sanitizedTo, subject, maxRetries, retryDelaySeconds, attachments?.Count ?? 0);

        while (attempt < maxRetries)
        {
            try
            {
                await SendEmailAsync(mailbox, to, subject, body, attachments, cancellationToken);
                _logger.LogInformationSensitive("邮件发送（带重试）成功：To={To}, Subject={Subject}, Attempt={Attempt}/{MaxRetries}",
                    sanitizedTo, subject, attempt + 1, maxRetries);
                return;
            }
            catch (Exception ex) when (attempt < maxRetries - 1)
            {
                attempt++;
                _logger.LogWarningSensitive("邮件发送（带重试）失败，{RetryDelay}秒后重试 ({Attempt}/{MaxRetries}): To={To}, Subject={Subject}, ErrorType={ErrorType}, Error={Error}",
                    retryDelaySeconds, attempt, maxRetries, sanitizedTo, subject, ex.GetType().Name, ex.Message);

                var delay = attempt == 1 ? _firstRetryDelay : TimeSpan.FromSeconds(retryDelaySeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
        _logger.LogErrorSensitive("邮件发送（带重试）最终失败：To={To}, Subject={Subject}, TotalAttempts={Attempts}", sanitizedTo, subject, maxRetries);
        throw new InvalidOperationException($"邮件发送失败，已重试 {maxRetries} 次: To={sanitizedTo}");
    }

    public async Task<List<string>> GetImapFoldersAsync(Mailbox mailbox, CancellationToken cancellationToken = default)
    {
        var folders = new List<string>();
        using var client = new ImapClient();
        client.Timeout = _quickOperationTimeoutMs;
        try
        {
            await client.ConnectAsync(mailbox.InHost, mailbox.InPort, GetSocketOptions(mailbox.InEncryption), cancellationToken);

            if (mailbox.OauthEnabled && !string.IsNullOrEmpty(mailbox.OauthAccessToken))
            {
                var oauth2 = new SaslMechanismOAuth2(mailbox.InUsername, mailbox.OauthAccessToken);
                await client.AuthenticateAsync(oauth2, cancellationToken);
            }
            else
            {
                await client.AuthenticateAsync(mailbox.InUsername, mailbox.InPassword, cancellationToken);
            }

            var imapFolders = client.GetFolders(client.PersonalNamespaces[0]);
            foreach (var folder in imapFolders)
            {
                folders.Add(folder.FullName);
            }

            return folders;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetImapFoldersAsync 失败：Mailbox={MailboxId}, Host={Host}", mailbox.Id, mailbox.InHost);
            return folders;
        }
        finally
        {
            try
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(true, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IMAP 断开连接失败：Mailbox={MailboxId}", mailbox.Id);
            }
        }
    }

    public async Task<bool> ForwardEmailAsync(Mailbox mailbox, Conversation conversation, ConversationThread thread, List<string> toAddresses, CancellationToken cancellationToken = default)
    {
        try
        {
            var subject = "Fwd: " + conversation.Subject;
            var bodyText = thread.Body ?? string.Empty;
            var body = $"---------- Forwarded message ----------\nFrom: {thread.From}\nDate: {thread.CreatedAt:yyyy-MM-dd HH:mm:ss}\nSubject: {conversation.Subject}\n\n{bodyText}";

            foreach (var to in toAddresses)
            {
                await SendEmailAsync(mailbox, to, subject, body, null, cancellationToken);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ForwardEmailAsync failed");
            return false;
        }
    }

    public Task<string> GetSmtpQueueIdAsync()
    {
        return Task.FromResult(_lastSmtpQueueId);
    }

    public Task<List<EmailBounceInfo>> DetectBounceAsync(Mailbox mailbox, MimeMessage message)
    {
        var bounces = new List<EmailBounceInfo>();
        
        // 检测退信邮件特征
        var subject = message.Subject ?? string.Empty;
        var isBounce = subject.Contains("Mail delivery failed", StringComparison.OrdinalIgnoreCase) ||
                       subject.Contains("Undelivered Mail", StringComparison.OrdinalIgnoreCase) ||
                       subject.Contains("Delivery Status Notification", StringComparison.OrdinalIgnoreCase) ||
                       message.From.Mailboxes.Any(m => m.Address.Contains("mailer-daemon"));

        if (isBounce)
        {
            var bodyText = message.TextBody ?? string.Empty;
            // 提取退信信息
            var lines = bodyText.Split('\n');
            string? originalRecipient = null;
            string? diagnosticInfo = null;
            string? bounceType = "hard";

            foreach (var line in lines)
            {
                if (line.Contains("Original-Recipient:") || line.Contains("Final-Recipient:"))
                {
                    originalRecipient = ExtractEmailFromLine(line);
                }
                if (line.Contains("Diagnostic-Code:"))
                {
                    diagnosticInfo = line.Replace("Diagnostic-Code:", "").Trim();
                }
                if (line.Contains("Status:"))
                {
                    if (line.Contains("5."))
                    {
                        bounceType = "hard";
                    }
                    else if (line.Contains("4."))
                    {
                        bounceType = "soft";
                    }
                }
            }

            if (originalRecipient != null)
            {
                bounces.Add(new EmailBounceInfo
                {
                    OriginalRecipient = originalRecipient,
                    BounceType = bounceType,
                    DiagnosticInfo = diagnosticInfo ?? string.Empty
                });
            }
        }

        return Task.FromResult(bounces);
    }

    private string? ExtractEmailFromLine(string line)
    {
        var match = System.Text.RegularExpressions.Regex.Match(line, @"[\w\.-]+@[\w\.-]+");
        return match.Success ? match.Value : null;
    }

    public async Task SendSystemNotificationAsync(Mailbox mailbox, string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = new MimeMessage();
            var safeDisplayName = SanitizeHeaderInput(mailbox.Name, 200);
            var safeSubject = SanitizeHeaderInput(subject, 78);
            message.From.Add(new MailboxAddress(safeDisplayName, mailbox.Email));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = safeSubject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            client.Timeout = _smtpSendTimeoutMs;

            await client.ConnectAsync(mailbox.OutHost, mailbox.OutPort, GetSocketOptions(mailbox.OutEncryption), cancellationToken);

            if (!string.IsNullOrEmpty(mailbox.OutUsername))
            {
                if (mailbox.OauthEnabled && !string.IsNullOrEmpty(mailbox.OauthAccessToken))
                {
                    var oauth2 = new SaslMechanismOAuth2(mailbox.OutUsername, mailbox.OauthAccessToken);
                    await client.AuthenticateAsync(oauth2, cancellationToken);
                }
                else
                {
                    await client.AuthenticateAsync(mailbox.OutUsername, mailbox.OutPassword, cancellationToken);
                }
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            var sanitizedFrom = SensitiveDataHelper.Sanitize("email", mailbox.Email) ?? mailbox.Email;
            var sanitizedTo = SensitiveDataHelper.Sanitize("email", to) ?? to;

            _logger.LogInformationSensitive("系统通知已发送：From={From}, To={To}, Subject={Subject}",
                sanitizedFrom, sanitizedTo, subject);
        }
        catch (Exception ex)
        {
            _logger.LogErrorSensitive("系统通知发送失败：{ErrorMessage}", ex.Message);
            throw;
        }
    }

    public async Task<bool> DeleteEmailAsync(Mailbox mailbox, string messageId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new ImapClient();
            client.Timeout = _quickOperationTimeoutMs;
            await client.ConnectAsync(mailbox.InHost, mailbox.InPort, GetSocketOptions(mailbox.InEncryption), cancellationToken);

            if (mailbox.OauthEnabled && !string.IsNullOrEmpty(mailbox.OauthAccessToken))
            {
                var oauth2 = new SaslMechanismOAuth2(mailbox.InUsername, mailbox.OauthAccessToken);
                await client.AuthenticateAsync(oauth2, cancellationToken);
            }
            else
            {
                await client.AuthenticateAsync(mailbox.InUsername, mailbox.InPassword, cancellationToken);
            }

            await client.Inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
            var uids = await client.Inbox.SearchAsync(MailKit.Search.SearchQuery.HeaderContains("Message-Id", messageId), cancellationToken);
            if (uids.Any())
            {
                await client.Inbox.AddFlagsAsync(uids, MessageFlags.Deleted, true, cancellationToken);
                await client.Inbox.ExpungeAsync(cancellationToken);
                return true;
            }
            await client.DisconnectAsync(true, cancellationToken);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteEmailAsync failed");
            return false;
        }
    }

    public async Task<List<ConversationThread>> FetchEmailsAsync(Mailbox mailbox, CancellationToken cancellationToken = default)
    {
        var threads = new List<ConversationThread>();

        using var client = new ImapClient();
        try
        {
            await client.ConnectAsync(mailbox.InHost, mailbox.InPort, GetSocketOptions(mailbox.InEncryption), cancellationToken);

            if (mailbox.OauthEnabled && !string.IsNullOrEmpty(mailbox.OauthAccessToken))
            {
                var oauth2 = new SaslMechanismOAuth2(mailbox.InUsername, mailbox.OauthAccessToken);
                await client.AuthenticateAsync(oauth2, cancellationToken);
            }
            else
            {
                await client.AuthenticateAsync(mailbox.InUsername, mailbox.InPassword, cancellationToken);
            }

            await client.Inbox.OpenAsync(MailKit.FolderAccess.ReadWrite, cancellationToken);

            var uids = await client.Inbox.SearchAsync(MailKit.Search.SearchQuery.All, cancellationToken);

            foreach (var uid in uids)
            {
                var message = await client.Inbox.GetMessageAsync(uid, cancellationToken);

                var thread = new ConversationThread
                {
                    Type = ThreadType.Customer,
                    Body = GetMessageBody(message),
                    Subject = DecodeEmailSubject(message),
                    From = message.From.ToString(),
                    To = message.To.ToString(),
                    CreatedAt = message.Date.DateTime
                };

                threads.Add(thread);
            }

            return threads;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FetchEmailsAsync 失败：Mailbox={MailboxId}, Host={Host}", mailbox.Id, mailbox.InHost);
            return threads;
        }
        finally
        {
            try
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(true, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IMAP 断开连接失败：Mailbox={MailboxId}", mailbox.Id);
            }
        }
    }

    private static string GetMessageBody(MimeMessage message)
    {
        var htmlBody = GetMessageBodyRaw(message, true);
        if (!string.IsNullOrEmpty(htmlBody))
        {
            return ConvertToUtf8Html(htmlBody);
        }
        
        var textBody = GetMessageBodyRaw(message, false);
        if (!string.IsNullOrEmpty(textBody))
        {
            return ConvertToUtf8Text(textBody);
        }
        
        return string.Empty;
    }

    private static string GetMessageBodyRaw(MimeMessage message, bool html)
    {
        foreach (var part in message.BodyParts)
        {
            if (part is TextPart textPart)
            {
                if ((html && textPart.IsHtml) || (!html && textPart.IsPlain))
                {
                    using var stream = new MemoryStream();
                    textPart.Content.DecodeTo(stream);
                    stream.Position = 0;
                    
                    var charset = textPart.ContentType.Charset ?? string.Empty;
                    if (charset.Equals("gb2312", StringComparison.OrdinalIgnoreCase) ||
                        charset.Equals("gbk", StringComparison.OrdinalIgnoreCase) ||
                        charset.Equals("gb18030", StringComparison.OrdinalIgnoreCase))
                    {
                        var bytes = stream.ToArray();
                        return Encoding.GetEncoding(charset).GetString(bytes);
                    }
                    
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    return reader.ReadToEnd();
                }
            }
        }
        
        return html ? message.HtmlBody : message.TextBody ?? string.Empty;
    }

    /// <summary>
    /// 转换 HTML 中的 charset 为 UTF-8
    /// </summary>
    /// <param name="html">HTML 字符串</param>
    /// <returns>转换后的 HTML 字符串</returns>
    private static string ConvertToUtf8Html(string html)
    {
        try
        {
            html = html.Replace("charset=gb2312", "charset=utf-8", StringComparison.InvariantCultureIgnoreCase)
                       .Replace("charset=gbk", "charset=utf-8", StringComparison.InvariantCultureIgnoreCase)
                       .Replace("charset=gb18030", "charset=utf-8", StringComparison.InvariantCultureIgnoreCase)
                       .Replace("charset=GB2312", "charset=utf-8", StringComparison.InvariantCultureIgnoreCase)
                       .Replace("charset=GBK", "charset=utf-8", StringComparison.InvariantCultureIgnoreCase)
                       .Replace("charset=GB18030", "charset=utf-8", StringComparison.InvariantCultureIgnoreCase);
        }
        catch (ArgumentException)
        {
            // 字符串替换参数无效时跳过，返回原始字符串
        }
        return html;
    }

    /// <summary>
    /// 尝试将文本从 Latin1 转换为 GB2312 编码
    /// </summary>
    /// <param name="text">原始文本</param>
    /// <returns>转换后的文本，失败返回原始文本</returns>
    private static string ConvertToUtf8Text(string text)
    {
        try
        {
            var latin1Bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(text);
            var converted = Encoding.GetEncoding("GB2312").GetString(latin1Bytes);
            if (!converted.Contains('\uFFFD'))
            {
                return converted;
            }
        }
        catch (ArgumentException)
        {
            // 编码不支持时返回原始字符串
        }
        return text;
    }

    private string DecodeEmailSubject(MimeMessage message)
    {
        try
        {
            var subject = message.Subject ?? string.Empty;
            
            if (subject.Contains("=?"))
            {
                var decoded = DecodeRfc2047Subject(subject);
                if (!string.IsNullOrEmpty(decoded))
                {
                    return decoded;
                }
            }
            
            return subject;
        }
        catch (Exception ex)
        {
            _logger.LogError($"DecodeEmailSubject failed: {ex.Message}");
        }
        
        return message.Subject ?? string.Empty;
    }

    /// <summary>
    /// 解码 RFC 2047 编码的邮件主题
    /// </summary>
    /// <param name="subject">原始主题字符串</param>
    /// <returns>解码后的主题</returns>
    private static string DecodeRfc2047Subject(string subject)
    {
        try
        {
            var pattern = @"=\?([^?]+)\?([BbQq])\?([^?]+)\?=";
            var matches = System.Text.RegularExpressions.Regex.Matches(subject, pattern);
            
            if (matches.Count == 0)
            {
                return subject;
            }
            
            var result = subject;
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var charset = match.Groups[1].Value;
                var encoding = match.Groups[2].Value.ToUpper();
                var encodedText = match.Groups[3].Value;
                
                try
                {
                    byte[] bytes;
                    if (encoding == "B")
                    {
                        bytes = Convert.FromBase64String(encodedText);
                    }
                    else
                    {
                        bytes = DecodeQuotedPrintable(encodedText);
                    }
                    
                    Encoding enc;
                    try
                    {
                        enc = Encoding.GetEncoding(charset);
                    }
                    catch (ArgumentException)
                    {
                        // 编码不支持时尝试回退
                        if (charset.ToLower().Contains("gb"))
                        {
                            enc = Encoding.GetEncoding("GB2312");
                        }
                        else
                        {
                            enc = Encoding.UTF8;
                        }
                    }
                    
                    var decoded = enc.GetString(bytes);
                    result = result.Replace(match.Value, decoded);
                }
                catch (FormatException)
                {
                    // Base64 格式错误时跳过此匹配项
                }
                catch (ArgumentException)
                {
                    // 编码转换参数错误时跳过
                }
            }
            
            return result;
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            // 正则匹配超时时返回原始字符串
        }
        return subject;
    }

    private static byte[] DecodeQuotedPrintable(string text)
    {
        var bytes = new List<byte>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '=' && i + 2 < text.Length)
            {
                var hex = text.Substring(i + 1, 2);
                try
                {
                    bytes.Add(Convert.ToByte(hex, 16));
                    i += 2;
                }
                catch
                {
                    bytes.Add((byte)text[i]);
                }
            }
            else
            {
                bytes.Add((byte)text[i]);
            }
        }
        return bytes.ToArray();
    }

    public async Task<bool> TestConnectionAsync(Mailbox mailbox, CancellationToken cancellationToken = default)
    {
        var (success, _) = await TestConnectionWithDetailsAsync(mailbox, cancellationToken);
        return success;
    }

    public async Task<(bool Success, string? ErrorMessage)> TestConnectionWithDetailsAsync(Mailbox mailbox, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(mailbox.InHost))
        {
            if (string.IsNullOrEmpty(mailbox.InUsername))
            {
                return (false, "收件服务器用户名未设置，请编辑邮箱填写用户名");
            }
            if (!mailbox.OauthEnabled && string.IsNullOrEmpty(mailbox.InPassword))
            {
                return (false, "收件服务器密码未设置，请编辑邮箱并输入密码");
            }
            if (mailbox.OauthEnabled && string.IsNullOrEmpty(mailbox.OauthAccessToken))
            {
                return (false, "OAuth访问令牌未设置，请配置OAuth授权");
            }
            try
            {
                using var imapClient = new ImapClient();
                imapClient.Timeout = _quickOperationTimeoutMs;
                await imapClient.ConnectAsync(mailbox.InHost, mailbox.InPort, GetSocketOptions(mailbox.InEncryption), cancellationToken);

                if (mailbox.OauthEnabled && !string.IsNullOrEmpty(mailbox.OauthAccessToken))
                {
                    var oauth2 = new SaslMechanismOAuth2(mailbox.InUsername, mailbox.OauthAccessToken);
                    await imapClient.AuthenticateAsync(oauth2, cancellationToken);
                }
                else
                {
                    await imapClient.AuthenticateAsync(mailbox.InUsername, mailbox.InPassword, cancellationToken);
                }

                await imapClient.DisconnectAsync(true, cancellationToken);
                _logger.LogInformationSensitive("收件服务器连接测试成功：{Host}", mailbox.InHost);
            }
            catch (Exception ex)
            {
                _logger.LogWarningSensitive("收件服务器连接测试失败：{Error}（用户名：{Username}）",
                    ex.Message, mailbox.InUsername);
                return (false, $"收件服务器连接失败：{ex.Message}（用户名：{mailbox.InUsername}）");
            }
        }

        if (!string.IsNullOrEmpty(mailbox.OutHost))
        {
            if (string.IsNullOrEmpty(mailbox.OutUsername))
            {
                return (false, "发件服务器用户名未设置，请编辑邮箱填写用户名");
            }
            if (!mailbox.OauthEnabled && string.IsNullOrEmpty(mailbox.OutPassword))
            {
                return (false, "发件服务器密码未设置，请编辑邮箱并输入密码");
            }
            if (mailbox.OauthEnabled && string.IsNullOrEmpty(mailbox.OauthAccessToken))
            {
                return (false, "OAuth访问令牌未设置，请配置OAuth授权");
            }
            try
            {
                using var smtpClient = new SmtpClient();
                smtpClient.Timeout = _quickOperationTimeoutMs;
                await smtpClient.ConnectAsync(mailbox.OutHost, mailbox.OutPort, GetSocketOptions(mailbox.OutEncryption), cancellationToken);

                if (mailbox.OauthEnabled && !string.IsNullOrEmpty(mailbox.OauthAccessToken))
                {
                    var oauth2 = new SaslMechanismOAuth2(mailbox.OutUsername, mailbox.OauthAccessToken);
                    await smtpClient.AuthenticateAsync(oauth2, cancellationToken);
                }
                else
                {
                    await smtpClient.AuthenticateAsync(mailbox.OutUsername, mailbox.OutPassword, cancellationToken);
                }

                await smtpClient.DisconnectAsync(true, cancellationToken);
                _logger.LogInformationSensitive("发件服务器连接测试成功：{Host}", mailbox.OutHost);
            }
            catch (Exception ex)
            {
                _logger.LogWarningSensitive("发件服务器连接测试失败：{Error}（用户名：{Username}）",
                    ex.Message, mailbox.OutUsername);
                return (false, $"发件服务器连接失败：{ex.Message}（用户名：{mailbox.OutUsername}）");
            }
        }

        return (true, null);
    }

    private static SecureSocketOptions GetSocketOptions(EncryptionType encryption)
    {
        return encryption switch
        {
            EncryptionType.None => SecureSocketOptions.None,
            EncryptionType.Tls => SecureSocketOptions.StartTls,
            EncryptionType.Ssl => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.Auto
        };
    }

    public async Task SaveToSentFolderAsync(Mailbox mailbox, string to, string subject, string body,
        List<Attachment>? attachments = null, string? cc = null, string? bcc = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = new MimeMessage();
            var safeDisplayName = SanitizeHeaderInput(mailbox.Name, 200);
            var safeSubject = SanitizeHeaderInput(subject, 78);
            message.From.Add(new MailboxAddress(safeDisplayName, mailbox.Email));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = safeSubject;

            if (!string.IsNullOrEmpty(cc))
            {
                foreach (var ccAddr in cc.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)))
                {
                    message.Cc.Add(MailboxAddress.Parse(ccAddr));
                }
            }

            if (!string.IsNullOrEmpty(bcc))
            {
                foreach (var bccAddr in bcc.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)))
                {
                    message.Bcc.Add(MailboxAddress.Parse(bccAddr));
                }
            }

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = body,
                // 生成纯文本 alternative，避免部分邮件客户端/网关在没有 text/plain 时直接显示 HTML 源码
                TextBody = HtmlSanitizerHelper.ConvertToPlainText(body)
            };

            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    AddAttachmentToBodyBuilder(bodyBuilder, attachment);
                }
            }

            message.Body = bodyBuilder.ToMessageBody();
            message.Date = DateTime.UtcNow;

            using var client = new ImapClient();
            await client.ConnectAsync(mailbox.InHost, mailbox.InPort, GetSocketOptions(mailbox.InEncryption), cancellationToken);

            if (mailbox.OauthEnabled && !string.IsNullOrEmpty(mailbox.OauthAccessToken))
            {
                var oauth2 = new SaslMechanismOAuth2(mailbox.InUsername, mailbox.OauthAccessToken);
                await client.AuthenticateAsync(oauth2, cancellationToken);
            }
            else
            {
                await client.AuthenticateAsync(mailbox.InUsername, mailbox.InPassword, cancellationToken);
            }

            var sentFolder = FindSentFolder(client);
            if (sentFolder != null)
            {
                await sentFolder.OpenAsync(MailKit.FolderAccess.ReadWrite, cancellationToken);
                await sentFolder.AppendAsync(message, MessageFlags.Seen, cancellationToken);
                _logger.LogInformation("邮件已保存到已发送文件夹: {FolderName}", sentFolder.Name);
            }
            else
            {
                _logger.LogWarning("未找到已发送文件夹，跳过保存");
            }

            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存邮件到已发送文件夹失败");
        }
    }

    /// <summary>
    /// 查找已发送邮件文件夹
    /// </summary>
    /// <param name="client">IMAP 客户端</param>
    /// <returns>已发送文件夹，未找到返回 null</returns>
    private static IMailFolder? FindSentFolder(ImapClient client)
    {
        var sentFolderNames = new[] { "Sent", "Sent Items", "已发送", "已发送邮件", "Sent Mail", "Sent Messages", "SentBox", "发件箱" };
        
        foreach (var name in sentFolderNames)
        {
            try
            {
                var folder = client.GetFolder(name);
                if (folder != null && folder.Exists)
                {
                    return folder;
                }
            }
            catch (FolderNotFoundException)
            {
                // 文件夹不存在，继续尝试下一个
            }
        }

        try
        {
            foreach (var namespaceItem in client.PersonalNamespaces)
            {
                var rootFolder = client.GetFolder(namespaceItem);
                foreach (var folder in rootFolder.GetSubfolders())
                {
                    var folderName = folder.Name.ToLowerInvariant();
                    if (folderName.Contains("sent") || folderName.Contains("已发送") || folderName.Contains("发件"))
                    {
                        return folder;
                    }
                }
            }
        }
        catch (FolderNotFoundException)
        {
            // 命名空间或文件夹不存在，返回 null
        }

        return null;
    }

    public async Task<(List<MimeMessage> Messages, ulong NewUid)> FetchMimeMessagesAsync(Mailbox mailbox, ulong lastUid, CancellationToken cancellationToken = default)
    {
        var messages = new List<MimeMessage>();
        var newUid = lastUid;
        var sanitizedEmail = SensitiveDataHelper.Sanitize("email", mailbox.Email) ?? mailbox.Email;

        using var client = new ImapClient();
        client.Timeout = _fetchTimeoutMs;

        try
        {
            await client.ConnectAsync(mailbox.InHost, mailbox.InPort, GetSocketOptions(mailbox.InEncryption), cancellationToken);

            if (mailbox.OauthEnabled && !string.IsNullOrEmpty(mailbox.OauthAccessToken))
            {
                var oauth2 = new SaslMechanismOAuth2(mailbox.InUsername, mailbox.OauthAccessToken);
                await client.AuthenticateAsync(oauth2, cancellationToken);
            }
            else
            {
                await client.AuthenticateAsync(mailbox.InUsername, mailbox.InPassword, cancellationToken);
            }

            await client.Inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var allUids = await client.Inbox.SearchAsync(SearchQuery.All, cancellationToken);
            if (!allUids.Any())
            {
                _logger.LogInformationSensitive("邮箱 {Mailbox} 中没有邮件", sanitizedEmail);
                await client.DisconnectAsync(true, cancellationToken);
                return (messages, newUid);
            }

            var maxUid = allUids.Max();
            _logger.LogInformationSensitive("邮箱 {Mailbox} 当前最大UID: {MaxUid}, 上次收取UID: {LastUid}",
                sanitizedEmail, maxUid.Id, lastUid);

            if (maxUid.Id <= lastUid)
            {
                _logger.LogInformationSensitive("邮箱 {Mailbox} 没有新邮件", sanitizedEmail);
                await client.DisconnectAsync(true, cancellationToken);
                return (messages, newUid);
            }

            var newUids = allUids.Where(uid => uid.Id > lastUid).OrderBy(uid => uid.Id).ToList();
            _logger.LogInformationSensitive("邮箱 {Mailbox} 发现 {NewCount} 封新邮件 (UID: {Start}-{End})",
                sanitizedEmail, newUids.Count, newUids.First().Id, newUids.Last().Id);

            foreach (var uid in newUids)
            {
                try
                {
                    var message = await client.Inbox.GetMessageAsync(uid, cancellationToken);
                    messages.Add(message);

                    if (uid.Id > newUid)
                    {
                        newUid = uid.Id;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogErrorSensitive("获取邮件失败: UID={Uid}, Error={Error}", uid.Id, ex.Message);
                }
            }

            _logger.LogInformationSensitive("成功从邮箱 {Mailbox} 获取 {Count} 封邮件",
                sanitizedEmail, messages.Count);

            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogErrorSensitive("连接或收取邮箱 {Mailbox} 邮件失败: {Error}",
                sanitizedEmail, ex.Message);
            throw; // 重新抛出异常，让调用方（控制器或Job）能感知到失败
        }

        return (messages, newUid);
    }
}
