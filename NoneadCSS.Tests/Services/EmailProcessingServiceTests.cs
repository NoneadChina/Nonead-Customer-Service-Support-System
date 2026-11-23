using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Moq;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Services;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// EmailProcessingService 单元测试 — T000057 修复验证
/// 重点验证：
/// 1. ExtractThreadIdFromMessageId 能正确从 reply-{threadId}-{guid}@{domain} 格式中提取 threadId
/// 2. FindRelatedConversationAsync 在 MessageId 精确匹配失败时，能通过提取的 threadId 兜底匹配
/// </summary>
public class EmailProcessingServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IThreadRepository> _mockThreadRepo;
    private readonly Mock<IConversationRepository> _mockConversationRepo;
    private readonly EmailProcessingService _service;

    public EmailProcessingServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockThreadRepo = new Mock<IThreadRepository>();
        _mockConversationRepo = new Mock<IConversationRepository>();
        var mockVirusScanService = new Mock<IVirusScanService>();

        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);

        _service = new EmailProcessingService(
            NullLogger<EmailProcessingService>.Instance,
            mockVirusScanService.Object);
    }

    #region ExtractThreadIdFromMessageId - 私有方法验证（通过 FindRelatedConversationAsync 间接验证）

    [Fact]
    public async Task FindRelatedConversationAsync_InReplyToMatchesFormat_ShouldExtractThreadIdAndFindThread()
    {
        // Arrange - T000057 核心场景：In-Reply-To 格式正确但精确匹配失败
        // 例如网关修改了 MessageId 的大小写或删除了尖括号
        const int threadId = 42;
        const int conversationId = 100;
        var inReplyTo = $"reply-{threadId}-abc123-def456@css.example.com";

        var conversation = new Conversation
        {
            Id = conversationId,
            Subject = "Test Subject",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            Number = 1001
        };
        var thread = new ConversationThread
        {
            Id = threadId,
            ConversationId = conversationId,
            Conversation = conversation,
            MessageId = $"<{inReplyTo}>"  // 存储时带尖括号
        };

        var message = new MimeMessage
        {
            MessageId = "customer-reply-1@mail.example.com",
            InReplyTo = inReplyTo,  // 引用时不带尖括号
            Subject = "Re: Test Subject"
        };
        message.From.Add(new MailboxAddress("Customer", "customer@example.com"));

        // 精确 MessageId 匹配失败（存储带尖括号，引用不带）
        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync(inReplyTo))
            .ReturnsAsync((ConversationThread?)null);

        // 但按 threadId 直接查询可以找到
        _mockThreadRepo.Setup(r => r.GetByIdAsync(threadId))
            .ReturnsAsync(thread);

        // Act
        var result = await _service.FindRelatedConversationAsync(message, 1, _mockUnitOfWork.Object);

        // Assert - 应通过提取的 threadId 兜底匹配找到会话
        result.Should().NotBeNull();
        result!.Id.Should().Be(conversationId);

        _mockThreadRepo.Verify(r => r.FindByMessageIdAsync(inReplyTo), Times.Once);
        _mockThreadRepo.Verify(r => r.GetByIdAsync(threadId), Times.Once);
    }

    [Fact]
    public async Task FindRelatedConversationAsync_ReferenceMatchesFormat_ShouldExtractThreadIdAndFindThread()
    {
        // Arrange - References 中的 MessageId 匹配
        const int threadId = 55;
        const int conversationId = 200;
        var reference = $"reply-{threadId}-xyz789-uvw012@css.example.com";

        var conversation = new Conversation
        {
            Id = conversationId,
            Subject = "Another Subject",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            Number = 2002
        };
        var thread = new ConversationThread
        {
            Id = threadId,
            ConversationId = conversationId,
            Conversation = conversation,
            MessageId = reference
        };

        var message = new MimeMessage
        {
            MessageId = "customer-reply-2@mail.example.com",
            Subject = "Re: Another Subject"
        };
        message.From.Add(new MailboxAddress("Customer", "customer@example.com"));
        message.References.Add("old-thread@mail.example.com");
        message.References.Add(reference);  // 旧引用 + 新引用

        // 精确匹配都失败
        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ConversationThread?)null);

        // 按 threadId 直接查询找到
        _mockThreadRepo.Setup(r => r.GetByIdAsync(threadId))
            .ReturnsAsync(thread);

        // Act
        var result = await _service.FindRelatedConversationAsync(message, 1, _mockUnitOfWork.Object);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(conversationId);
        _mockThreadRepo.Verify(r => r.GetByIdAsync(threadId), Times.Once);
    }

    [Fact]
    public async Task FindRelatedConversationAsync_ExactMessageIdMatch_ShouldNotFallBackToThreadId()
    {
        // Arrange - 精确匹配成功时，不应执行兜底逻辑
        const int threadId = 10;
        const int conversationId = 300;
        var messageId = "exact-match@css.example.com";

        var conversation = new Conversation
        {
            Id = conversationId,
            Subject = "Exact Subject",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            Number = 3003
        };
        var thread = new ConversationThread
        {
            Id = threadId,
            ConversationId = conversationId,
            Conversation = conversation,
            MessageId = messageId
        };

        var message = new MimeMessage
        {
            MessageId = messageId,  // 精确匹配
            Subject = "Exact Subject"
        };
        message.From.Add(new MailboxAddress("Customer", "customer@example.com"));

        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync(messageId))
            .ReturnsAsync(thread);

        // Act
        var result = await _service.FindRelatedConversationAsync(message, 1, _mockUnitOfWork.Object);

        // Assert - 精确匹配成功，不应调用 GetByIdAsync
        result.Should().NotBeNull();
        result!.Id.Should().Be(conversationId);
        _mockThreadRepo.Verify(r => r.FindByMessageIdAsync(messageId), Times.Once);
        _mockThreadRepo.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task FindRelatedConversationAsync_InReplyToFormatChangedCase_ShouldExtractThreadId()
    {
        // Arrange - 网关修改了大小写
        const int threadId = 99;
        const int conversationId = 400;
        var inReplyTo = $"REPLY-{threadId}-ABC123@CSS.EXAMPLE.COM";  // 全大写

        var conversation = new Conversation
        {
            Id = conversationId,
            Subject = "Case Test",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            Number = 4004
        };
        var thread = new ConversationThread
        {
            Id = threadId,
            ConversationId = conversationId,
            Conversation = conversation,
            MessageId = $"<reply-{threadId}-abc123@css.example.com>"  // 原始存储是小写+尖括号
        };

        var message = new MimeMessage
        {
            MessageId = "cust@mail.example.com",
            InReplyTo = inReplyTo,
            Subject = "Re: Case Test"
        };
        message.From.Add(new MailboxAddress("Customer", "customer@example.com"));

        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync(inReplyTo))
            .ReturnsAsync((ConversationThread?)null);  // 大小写不一致导致精确匹配失败

        _mockThreadRepo.Setup(r => r.GetByIdAsync(threadId))
            .ReturnsAsync(thread);

        // Act
        var result = await _service.FindRelatedConversationAsync(message, 1, _mockUnitOfWork.Object);

        // Assert - 正则匹配忽略大小写，应成功提取 threadId
        result.Should().NotBeNull();
        result!.Id.Should().Be(conversationId);
        _mockThreadRepo.Verify(r => r.GetByIdAsync(threadId), Times.Once);
    }

    [Fact]
    public async Task FindRelatedConversationAsync_InReplyToNotReplyFormat_ShouldNotExtractThreadId()
    {
        // Arrange - In-Reply-To 不是 reply- 前缀格式
        const int conversationId = 500;
        var inReplyTo = "random-message-id@mail.example.com";

        var conversation = new Conversation
        {
            Id = conversationId,
            Subject = "Random Format Subject",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            Number = 5005
        };

        var message = new MimeMessage
        {
            MessageId = "cust2@mail.example.com",
            InReplyTo = inReplyTo,
            Subject = "Re: Random Format"
        };
        message.From.Add(new MailboxAddress("Customer", "customer@example.com"));

        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ConversationThread?)null);

        _mockConversationRepo.Setup(r => r.GetByNumberAsync(It.IsAny<int>()))
            .ReturnsAsync((Conversation?)null);

        // 兜底匹配：FindAsync 返回空列表
        _mockConversationRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Conversation, bool>>>()))
            .ReturnsAsync(new List<Conversation>());

        // Act
        var result = await _service.FindRelatedConversationAsync(message, 1, _mockUnitOfWork.Object);

        // Assert - 非 reply- 格式不应触发 threadId 提取，GetByIdAsync 不应被调用
        result.Should().BeNull();
        _mockThreadRepo.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task FindRelatedConversationAsync_InReplyToReplyFormatButThreadNotExist_ShouldReturnNull()
    {
        // Arrange - 格式正确但 thread 不存在
        var inReplyTo = "reply-99999-missing@css.example.com";

        var message = new MimeMessage
        {
            MessageId = "cust3@mail.example.com",
            InReplyTo = inReplyTo,
            Subject = "Re: Missing Thread"
        };
        message.From.Add(new MailboxAddress("Customer", "customer@example.com"));

        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ConversationThread?)null);

        _mockThreadRepo.Setup(r => r.GetByIdAsync(99999))
            .ReturnsAsync((ConversationThread?)null);  // thread 不存在

        _mockConversationRepo.Setup(r => r.GetByNumberAsync(It.IsAny<int>()))
            .ReturnsAsync((Conversation?)null);

        // 兜底匹配：FindAsync 返回空列表
        _mockConversationRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Conversation, bool>>>()))
            .ReturnsAsync(new List<Conversation>());

        // Act
        var result = await _service.FindRelatedConversationAsync(message, 1, _mockUnitOfWork.Object);

        // Assert - thread 不存在，继续走后面的兜底匹配，最终返回 null
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindRelatedConversationAsync_MultipleReferences_ShouldCheckEachForThreadId()
    {
        // Arrange - References 中有多个条目，只有最后一个是 reply- 格式
        const int threadId = 77;
        const int conversationId = 600;

        var conversation = new Conversation
        {
            Id = conversationId,
            Subject = "Multi Ref Subject",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            Number = 6006
        };
        var thread = new ConversationThread
        {
            Id = threadId,
            ConversationId = conversationId,
            Conversation = conversation
        };

        var message = new MimeMessage
        {
            MessageId = "cust4@mail.example.com",
            Subject = "Re: Multi Ref"
        };
        message.From.Add(new MailboxAddress("Customer", "customer@example.com"));
        message.References.Add("ref1@mail.example.com");      // 非 reply 格式
        message.References.Add("ref2@mail.example.com");      // 非 reply 格式
        message.References.Add($"reply-{threadId}-aaa@css.example.com");  // reply 格式

        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ConversationThread?)null);

        _mockThreadRepo.Setup(r => r.GetByIdAsync(threadId))
            .ReturnsAsync(thread);

        // Act
        var result = await _service.FindRelatedConversationAsync(message, 1, _mockUnitOfWork.Object);

        // Assert - 遍历 References 时应找到最后一个 reply- 格式的并提取 threadId
        result.Should().NotBeNull();
        result!.Id.Should().Be(conversationId);
    }

    #endregion

    #region DetectAutoReply - 边界验证（确保正常邮件不被误判）

    [Fact]
    public void DetectAutoReply_NormalCustomerReply_ShouldReturnFalse()
    {
        // Arrange - 正常客户回复邮件
        var message = new MimeMessage
        {
            Subject = "Re: 咨询一下产品功能",
            MessageId = "normal-reply-123@mail.example.com"
        };
        message.From.Add(new MailboxAddress("张三", "customer@example.com"));
        message.To.Add(new MailboxAddress("客服", "support@example.com"));

        // Act
        var result = EmailProcessingService.DetectAutoReply(message);

        // Assert - 正常回复不应被判定为自动回复
        result.Should().BeFalse();
    }

    [Fact]
    public void DetectAutoReply_PrecedenceBulk_ShouldReturnTrue()
    {
        // Arrange - 批量邮件（Precedence: bulk）
        var message = new MimeMessage
        {
            Subject = "促销活动通知",
            MessageId = "bulk-1@mail.example.com"
        };
        message.Headers.Add("Precedence", "bulk");

        // Act
        var result = EmailProcessingService.DetectAutoReply(message);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void DetectAutoReply_PrecedenceList_ShouldReturnTrue()
    {
        // Arrange - 邮件列表回复（Precedence: list）
        var message = new MimeMessage
        {
            Subject = "Re: [List] 讨论话题",
            MessageId = "list-1@mail.example.com"
        };
        message.Headers.Add("Precedence", "list");

        // Act
        var result = EmailProcessingService.DetectAutoReply(message);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void DetectAutoReply_AutoSubmittedAutoReplied_ShouldReturnTrue()
    {
        // Arrange - 自动回复标记
        var message = new MimeMessage
        {
            Subject = "自动回复：感谢您的来信",
            MessageId = "autoreply-1@mail.example.com"
        };
        message.Headers.Add("Auto-Submitted", "auto-replied");

        // Act
        var result = EmailProcessingService.DetectAutoReply(message);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void DetectAutoReply_PrecedenceWithExtraWhitespace_ShouldStillMatch()
    {
        // Arrange - Precedence 值包含空格（正常客户端不会出现，但防御性匹配）
        var message = new MimeMessage
        {
            Subject = "Test",
            MessageId = "ws-1@mail.example.com"
        };
        message.Headers.Add("Precedence", "  bulk  ");  // 含前后空格

        // Act
        var result = EmailProcessingService.DetectAutoReply(message);

        // Assert - Trim 后匹配
        result.Should().BeTrue();
    }

    #endregion
}
