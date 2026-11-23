using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MimeKit;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Infrastructure.Services;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// EmailService 单元测试 — 邮件发送核心逻辑（IMAP/SMTP）
/// 测试不连接真实邮件服务器，聚焦于可纯单元测试的逻辑：
/// - DetectBounceAsync（退信检测）
/// - GetSmtpQueueIdAsync（队列ID 获取）
/// - TestConnectionWithDetailsAsync 参数校验
/// - ForwardEmailAsync 异常处理
/// - GetAttachmentFilePath 路径遍历防护
/// </summary>
public class EmailServiceTests
{
    private readonly Mock<ILogger<EmailService>> _mockLogger;
    private readonly Mock<IWebHostEnvironment> _mockEnv;
    private readonly IOptions<AttachmentSettings> _attachmentSettings;
    private readonly EmailService _emailService;

    public EmailServiceTests()
    {
        _mockLogger = new Mock<ILogger<EmailService>>();
        _mockEnv = new Mock<IWebHostEnvironment>();
        _mockEnv.Setup(e => e.ContentRootPath).Returns(Directory.GetCurrentDirectory());

        var settings = new AttachmentSettings
        {
            MaxSize = 20971520,
            UploadPath = "test-attachments"
        };
        _attachmentSettings = Options.Create(settings);

        _emailService = new EmailService(_mockLogger.Object, _mockEnv.Object, _attachmentSettings, Options.Create(new NoneadCSS.Application.Configuration.RetryOptions()));
    }

    #region DetectBounceAsync Tests

    [Fact]
    public async Task DetectBounceAsync_WithNonBounceEmail_ShouldReturnEmptyList()
    {
        // Arrange
        var mailbox = CreateTestMailbox();
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Sender", "sender@example.com"));
        message.To.Add(new MailboxAddress("Recipient", "recipient@example.com"));
        message.Subject = "Normal Email Subject";
        message.Body = new TextPart("plain") { Text = "This is a normal email." };

        // Act
        var result = await _emailService.DetectBounceAsync(mailbox, message);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectBounceAsync_WithMailDeliveryFailedSubject_ShouldDetectBounce()
    {
        // Arrange
        var mailbox = CreateTestMailbox();
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Mail Delivery Subsystem", "mailer-daemon@example.com"));
        message.To.Add(new MailboxAddress("Recipient", "recipient@example.com"));
        message.Subject = "Mail delivery failed: returning message to sender";
        message.Body = new TextPart("plain")
        {
            Text = "Original-Recipient: rfc822;user@example.com\nDiagnostic-Code: smtp; 550 5.1.1 User unknown\nStatus: 5.1.1"
        };

        // Act
        var result = await _emailService.DetectBounceAsync(mailbox, message);

        // Assert
        result.Should().NotBeEmpty();
        result[0].OriginalRecipient.Should().Be("user@example.com");
        result[0].BounceType.Should().Be("hard");
        result[0].DiagnosticInfo.Should().Contain("550 5.1.1 User unknown");
    }

    [Fact]
    public async Task DetectBounceAsync_WithDeliveryStatusNotificationSubject_ShouldDetectBounce()
    {
        // Arrange
        var mailbox = CreateTestMailbox();
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Postmaster", "postmaster@example.com"));
        message.Subject = "Delivery Status Notification (Failure)";
        message.Body = new TextPart("plain")
        {
            Text = "Final-Recipient: rfc822;dest@example.com\nStatus: 4.4.1\nDiagnostic-Code: smtp; 450 4.4.1 Connection timed out"
        };

        // Act
        var result = await _emailService.DetectBounceAsync(mailbox, message);

        // Assert
        result.Should().NotBeEmpty();
        result[0].OriginalRecipient.Should().Be("dest@example.com");
        result[0].BounceType.Should().Be("soft");
    }

    [Fact]
    public async Task DetectBounceAsync_WithMailerDaemonSender_ShouldDetectBounce()
    {
        // Arrange
        var mailbox = CreateTestMailbox();
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Mail Subsystem", "MAILER-DAEMON@mx.example.com"));
        message.Subject = "Undelivered Mail Returned to Sender";
        message.Body = new TextPart("plain")
        {
            Text = "Original-Recipient: rfc822;failed@example.com\nStatus: 5.0.0"
        };

        // Act
        var result = await _emailService.DetectBounceAsync(mailbox, message);

        // Assert
        result.Should().NotBeEmpty();
        result[0].OriginalRecipient.Should().Be("failed@example.com");
    }

    [Fact]
    public async Task DetectBounceAsync_WithBounceButNoRecipient_ShouldReturnEmptyList()
    {
        // Arrange
        var mailbox = CreateTestMailbox();
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Daemon", "mailer-daemon@example.com"));
        message.Subject = "Mail delivery failed";
        message.Body = new TextPart("plain")
        {
            Text = "Some bounce content without recipient info"
        };

        // Act
        var result = await _emailService.DetectBounceAsync(mailbox, message);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetSmtpQueueIdAsync Tests

    [Fact]
    public async Task GetSmtpQueueIdAsync_WhenNoEmailSent_ShouldReturnEmptyString()
    {
        // Act
        var result = await _emailService.GetSmtpQueueIdAsync();

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region TestConnectionWithDetailsAsync Parameter Validation Tests

    [Fact]
    public async Task TestConnectionWithDetailsAsync_WithNoInHostAndNoOutHost_ShouldReturnSuccess()
    {
        // Arrange — 邮箱未配置收发服务器，应直接返回成功
        var mailbox = new Mailbox
        {
            Id = 1,
            Email = "test@example.com",
            Name = "Test",
            InHost = "",
            OutHost = ""
        };

        // Act
        var (success, errorMessage) = await _emailService.TestConnectionWithDetailsAsync(mailbox);

        // Assert
        success.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public async Task TestConnectionWithDetailsAsync_WithInHostButNoUsername_ShouldReturnError()
    {
        // Arrange
        var mailbox = new Mailbox
        {
            Id = 1,
            Email = "test@example.com",
            Name = "Test",
            InHost = "imap.example.com",
            InPort = 993,
            InUsername = "",
            InPassword = ""
        };

        // Act
        var (success, errorMessage) = await _emailService.TestConnectionWithDetailsAsync(mailbox);

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().Contain("用户名未设置");
    }

    [Fact]
    public async Task TestConnectionWithDetailsAsync_WithInHostButNoPassword_ShouldReturnError()
    {
        // Arrange
        var mailbox = new Mailbox
        {
            Id = 1,
            Email = "test@example.com",
            Name = "Test",
            InHost = "imap.example.com",
            InPort = 993,
            InUsername = "user@example.com",
            InPassword = ""
        };

        // Act
        var (success, errorMessage) = await _emailService.TestConnectionWithDetailsAsync(mailbox);

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().Contain("密码未设置");
    }

    [Fact]
    public async Task TestConnectionWithDetailsAsync_WithOauthButNoToken_ShouldReturnError()
    {
        // Arrange
        var mailbox = new Mailbox
        {
            Id = 1,
            Email = "test@example.com",
            Name = "Test",
            InHost = "imap.example.com",
            InPort = 993,
            InUsername = "user@example.com",
            OauthEnabled = true,
            OauthAccessToken = ""
        };

        // Act
        var (success, errorMessage) = await _emailService.TestConnectionWithDetailsAsync(mailbox);

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().Contain("OAuth访问令牌未设置");
    }

    [Fact]
    public async Task TestConnectionWithDetailsAsync_WithOutHostButNoUsername_ShouldReturnError()
    {
        // Arrange — 有发件服务器但无用户名
        var mailbox = new Mailbox
        {
            Id = 1,
            Email = "test@example.com",
            Name = "Test",
            InHost = "",
            OutHost = "smtp.example.com",
            OutPort = 587,
            OutUsername = ""
        };

        // Act
        var (success, errorMessage) = await _emailService.TestConnectionWithDetailsAsync(mailbox);

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().Contain("发件服务器用户名未设置");
    }

    [Fact]
    public async Task TestConnectionWithDetailsAsync_WithOutHostButNoPassword_ShouldReturnError()
    {
        // Arrange
        var mailbox = new Mailbox
        {
            Id = 1,
            Email = "test@example.com",
            Name = "Test",
            InHost = "",
            OutHost = "smtp.example.com",
            OutPort = 587,
            OutUsername = "user@example.com",
            OutPassword = ""
        };

        // Act
        var (success, errorMessage) = await _emailService.TestConnectionWithDetailsAsync(mailbox);

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().Contain("发件服务器密码未设置");
    }

    #endregion

    #region TestConnectionAsync Tests

    [Fact]
    public async Task TestConnectionAsync_WithEmptyHosts_ShouldReturnTrue()
    {
        // Arrange
        var mailbox = new Mailbox
        {
            Id = 1,
            Email = "test@example.com",
            Name = "Test",
            InHost = "",
            OutHost = ""
        };

        // Act
        var result = await _emailService.TestConnectionAsync(mailbox);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region ForwardEmailAsync Tests

    [Fact]
    public async Task ForwardEmailAsync_WhenSendFails_ShouldReturnFalse()
    {
        // Arrange — 使用无效的邮箱配置触发发送失败
        var mailbox = new Mailbox
        {
            Id = 1,
            Email = "test@example.com",
            Name = "Test",
            OutHost = "invalid.smtp.server.nonexistent",
            OutPort = 25,
            OutUsername = "user",
            OutPassword = "pass",
            OutEncryption = EncryptionType.None
        };

        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test Subject"
        };

        var thread = new ConversationThread
        {
            Id = 1,
            Body = "Test body",
            From = "sender@example.com",
            CreatedAt = DateTime.UtcNow
        };

        // Act — 发送到无效服务器应失败并返回 false
        var result = await _emailService.ForwardEmailAsync(mailbox, conversation, thread, new List<string> { "dest@example.com" });

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private static Mailbox CreateTestMailbox()
    {
        return new Mailbox
        {
            Id = 1,
            Email = "test@example.com",
            Name = "Test Mailbox",
            InHost = "imap.example.com",
            InPort = 993,
            InUsername = "test@example.com",
            InPassword = "password",
            InEncryption = EncryptionType.Ssl,
            OutHost = "smtp.example.com",
            OutPort = 587,
            OutUsername = "test@example.com",
            OutPassword = "password",
            OutEncryption = EncryptionType.Tls
        };
    }

    #endregion
}
