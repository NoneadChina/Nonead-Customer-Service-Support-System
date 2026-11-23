using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// ConversationDraftService 单元测试
/// 覆盖会话草稿的保存（新建/更新）、查询、删除及按用户列表场景
/// </summary>
public class ConversationDraftServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IConversationRepository> _mockConversationRepo;
    private readonly Mock<IThreadRepository> _mockThreadRepo;
    private readonly Mock<IAttachmentRepository> _mockAttachmentRepo;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    private readonly ConversationDraftService _service;

    public ConversationDraftServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockConversationRepo = new Mock<IConversationRepository>();
        _mockThreadRepo = new Mock<IThreadRepository>();
        _mockAttachmentRepo = new Mock<IAttachmentRepository>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);
        _mockUnitOfWork.Setup(u => u.Attachments).Returns(_mockAttachmentRepo.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync())
            .ReturnsAsync(_mockTransaction.Object);

        _service = new ConversationDraftService(_mockUnitOfWork.Object);
    }

    /// <summary>
    /// 构造一个带 Threads 集合的 Conversation，便于测试新建草稿场景
    /// </summary>
    private static Conversation CreateConversation(int id = 1, string subject = "Subject", string customerEmail = "customer@example.com", int? customerId = 10)
    {
        return new Conversation
        {
            Id = id,
            Subject = subject,
            CustomerEmail = customerEmail,
            CustomerId = customerId,
            MailboxId = 1
        };
    }

    private static CreateThreadRequest CreateRequest(
        int conversationId = 1,
        string body = "<p>Body</p>",
        string from = "agent@example.com",
        string? to = null,
        string? cc = null,
        string? bcc = null,
        bool isNote = false,
        List<int>? attachmentIds = null)
    {
        return new CreateThreadRequest
        {
            ConversationId = conversationId,
            Body = body,
            From = from,
            To = to,
            Cc = cc,
            Bcc = bcc,
            IsNote = isNote,
            AttachmentIds = attachmentIds
        };
    }

    #region SaveDraftAsync - Non-existing conversation

    [Fact]
    public async Task SaveDraftAsync_WithNonExistingConversation_ShouldReturnNull()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(999))
            .ReturnsAsync((Conversation?)null);

        var request = CreateRequest(conversationId: 999);

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        result.Should().BeNull();
        _mockThreadRepo.Verify(r => r.GetDraftThreadTrackedAsync(It.IsAny<int>()), Times.Never);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
        _mockUnitOfWork.Verify(u => u.BeginTransactionAsync(), Times.Never);
    }

    #endregion

    #region SaveDraftAsync - Create new draft

    [Fact]
    public async Task SaveDraftAsync_WithExistingConversationAndNoExistingDraft_ShouldCreateNewDraft()
    {
        // Arrange
        var conversation = CreateConversation(id: 1, subject: "Hello", customerEmail: "cust@example.com", customerId: 10);
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        var request = CreateRequest(conversationId: 1, body: "<p>Draft</p>", from: "agent@example.com", to: "cust@example.com");

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 42);

        // Assert
        result.Should().NotBeNull();
        result!.Body.Should().Be("<p>Draft</p>");
        result.From.Should().Be("agent@example.com");
        result.To.Should().Be("cust@example.com");
        result.ConversationId.Should().Be(1);
        result.UserId.Should().Be(42);
        // 默认 IsNote=false -> Type=Message
        result.Type.Should().Be((int)ThreadType.Message);
        result.IsInternal.Should().BeFalse();

        // 验证草稿已添加到 conversation.Threads
        conversation.Threads.Should().HaveCount(1);
        var addedThread = conversation.Threads.First();
        addedThread.State.Should().Be(ThreadState.Draft);
        addedThread.Type.Should().Be(ThreadType.Message);
        addedThread.CustomerId.Should().Be(10);
        addedThread.Subject.Should().Be("Hello");
        addedThread.CreatedAt.Should().NotBe(default);
        addedThread.UpdatedAt.Should().NotBe(default);

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Never);
    }

    [Fact]
    public async Task SaveDraftAsync_WithIsNoteTrue_ShouldCreateNoteTypeDraft()
    {
        // Arrange
        var conversation = CreateConversation();
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        var request = CreateRequest(isNote: true);

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        result.Should().NotBeNull();
        result!.Type.Should().Be((int)ThreadType.Note);
        result.IsInternal.Should().BeTrue();

        conversation.Threads.First().Type.Should().Be(ThreadType.Note);
        conversation.Threads.First().IsInternal.Should().BeTrue();
    }

    [Fact]
    public async Task SaveDraftAsync_WithIsNoteFalse_ShouldCreateMessageTypeDraft()
    {
        // Arrange
        var conversation = CreateConversation();
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        var request = CreateRequest(isNote: false);

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        result.Should().NotBeNull();
        result!.Type.Should().Be((int)ThreadType.Message);
        result.IsInternal.Should().BeFalse();
    }

    [Fact]
    public async Task SaveDraftAsync_WithNullUserId_ShouldCreateDraftWithNullUserId()
    {
        // Arrange
        var conversation = CreateConversation();
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        var request = CreateRequest();

        // Act - 不传 userId，默认为 null
        var result = await _service.SaveDraftAsync(request);

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().BeNull();
        conversation.Threads.First().UserId.Should().BeNull();
    }

    [Fact]
    public async Task SaveDraftAsync_WithNullTo_ShouldFallbackToConversationCustomerEmail()
    {
        // Arrange
        var conversation = CreateConversation(customerEmail: "fallback@example.com");
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        var request = CreateRequest(to: null);

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        result.Should().NotBeNull();
        result!.To.Should().Be("fallback@example.com");
    }

    [Fact]
    public async Task SaveDraftAsync_WithProvidedTo_ShouldUseProvidedTo()
    {
        // Arrange
        var conversation = CreateConversation(customerEmail: "fallback@example.com");
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        var request = CreateRequest(to: "override@example.com");

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        result.Should().NotBeNull();
        result!.To.Should().Be("override@example.com");
    }

    [Fact]
    public async Task SaveDraftAsync_WithNullCcAndBcc_ShouldDefaultToEmptyString()
    {
        // Arrange
        var conversation = CreateConversation();
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        var request = CreateRequest(cc: null, bcc: null);

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        result.Should().NotBeNull();
        result!.Cc.Should().BeEmpty();
        result.Bcc.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveDraftAsync_WithProvidedCcAndBcc_ShouldUseProvidedValues()
    {
        // Arrange
        var conversation = CreateConversation();
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        var request = CreateRequest(cc: "cc@example.com", bcc: "bcc@example.com");

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        result.Should().NotBeNull();
        result!.Cc.Should().Be("cc@example.com");
        result.Bcc.Should().Be("bcc@example.com");
    }

    [Fact]
    public async Task SaveDraftAsync_ShouldUpdateConversationUpdatedAt()
    {
        // Arrange
        var conversation = CreateConversation();
        var originalUpdatedAt = DateTime.UtcNow.AddDays(-5);
        conversation.UpdatedAt = originalUpdatedAt;
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        var request = CreateRequest();

        // Act
        await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        conversation.UpdatedAt.Should().BeAfter(originalUpdatedAt);
    }

    #endregion

    #region SaveDraftAsync - Update existing draft

    [Fact]
    public async Task SaveDraftAsync_WithExistingDraft_ShouldUpdateDraftFields()
    {
        // Arrange
        var conversation = CreateConversation(id: 1, subject: "New Subject", customerEmail: "new@example.com", customerId: 10);
        var existingDraft = new ConversationThread
        {
            Id = 100,
            ConversationId = 1,
            Body = "Old Body",
            Subject = "Old Subject",
            From = "old@example.com",
            To = "old-to@example.com",
            Cc = "old-cc@example.com",
            Bcc = "old-bcc@example.com",
            IsInternal = false,
            State = ThreadState.Draft,
            Type = ThreadType.Message,
            UserId = 42,
            CustomerId = 10,
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        };

        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync(existingDraft);

        var request = CreateRequest(
            conversationId: 1,
            body: "<p>New Body</p>",
            from: "new-from@example.com",
            to: "new-to@example.com",
            cc: "new-cc@example.com",
            bcc: "new-bcc@example.com",
            isNote: true);

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 42);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(100);
        result.Body.Should().Be("<p>New Body</p>");
        result.From.Should().Be("new-from@example.com");
        result.To.Should().Be("new-to@example.com");
        result.Cc.Should().Be("new-cc@example.com");
        result.Bcc.Should().Be("new-bcc@example.com");
        // IsNote=true -> IsInternal=true，但 Type 字段在更新分支不重新设置，应保留原值
        result.IsInternal.Should().BeTrue();

        existingDraft.Body.Should().Be("<p>New Body</p>");
        existingDraft.Subject.Should().Be("New Subject"); // 来自 conversation.Subject
        existingDraft.From.Should().Be("new-from@example.com");
        existingDraft.To.Should().Be("new-to@example.com");
        existingDraft.Cc.Should().Be("new-cc@example.com");
        existingDraft.Bcc.Should().Be("new-bcc@example.com");
        existingDraft.IsInternal.Should().BeTrue();
        existingDraft.UpdatedAt.Should().NotBe(default);

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Never);

        // 更新分支不应将草稿加入 conversation.Threads
        conversation.Threads.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveDraftAsync_WithExistingDraftAndNullTo_ShouldFallbackToConversationCustomerEmail()
    {
        // Arrange
        var conversation = CreateConversation(customerEmail: "fallback@example.com");
        var existingDraft = new ConversationThread
        {
            Id = 100,
            ConversationId = 1,
            Body = "Old",
            To = "old@example.com",
            State = ThreadState.Draft,
            Type = ThreadType.Message
        };

        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync(existingDraft);

        var request = CreateRequest(to: null);

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        result.Should().NotBeNull();
        result!.To.Should().Be("fallback@example.com");
        existingDraft.To.Should().Be("fallback@example.com");
    }

    [Fact]
    public async Task SaveDraftAsync_WithExistingDraftAndNullCcBcc_ShouldDefaultToEmptyString()
    {
        // Arrange
        var conversation = CreateConversation();
        var existingDraft = new ConversationThread
        {
            Id = 100,
            ConversationId = 1,
            Body = "Old",
            Cc = "old-cc@example.com",
            Bcc = "old-bcc@example.com",
            State = ThreadState.Draft,
            Type = ThreadType.Message
        };

        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync(existingDraft);

        var request = CreateRequest(cc: null, bcc: null);

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        result.Should().NotBeNull();
        result!.Cc.Should().BeEmpty();
        result.Bcc.Should().BeEmpty();
        existingDraft.Cc.Should().BeEmpty();
        existingDraft.Bcc.Should().BeEmpty();
    }

    #endregion

    #region SaveDraftAsync - Attachments

    [Fact]
    public async Task SaveDraftAsync_WithAttachmentIds_ShouldAddAttachmentsToThread()
    {
        // Arrange
        var conversation = CreateConversation();
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        var attachments = new List<Attachment>
        {
            new() { Id = 11, FileName = "a.txt", MimeType = "text/plain", Size = 100 },
            new() { Id = 22, FileName = "b.txt", MimeType = "text/plain", Size = 200 }
        };
        _mockAttachmentRepo.Setup(r => r.GetByIdsTrackedAsync(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 11, 22 }))))
            .ReturnsAsync(attachments);

        var request = CreateRequest(attachmentIds: new List<int> { 11, 22 });

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        result.Should().NotBeNull();
        result!.Attachments.Should().HaveCount(2);

        var createdThread = conversation.Threads.First();
        createdThread.Attachments.Should().HaveCount(2);

        // 有附件 -> 应调用两次 SaveChangesAsync
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Exactly(2));
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task SaveDraftAsync_WithAttachments_ShouldSetThreadIdOnEachAttachment()
    {
        // Arrange
        var conversation = CreateConversation();
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        var attachments = new List<Attachment>
        {
            new() { Id = 11, FileName = "a.txt", MimeType = "text/plain", Size = 100 }
        };
        _mockAttachmentRepo.Setup(r => r.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(attachments);

        var request = CreateRequest(attachmentIds: new List<int> { 11 });

        // 捕获新建的 thread Id（通过 UpdateAsync 或 AddAsync 捕获较复杂，这里借助 ToResponse 返回的 Id）
        // 由于 conversation.Threads.Add 不会自动分配 Id，我们需要在 SaveChangesAsync 时手动分配以便断言
        var assignedThreadId = 555;
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .Callback(() =>
            {
                // 模拟 EF Core 在 SaveChanges 时分配主键
                foreach (var t in conversation.Threads)
                {
                    if (t.Id == 0)
                    {
                        t.Id = assignedThreadId;
                    }
                }
            })
            .ReturnsAsync(1);

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        result.Should().NotBeNull();
        result!.Attachments.Should().NotBeNull();
        result.Attachments![0].ThreadId.Should().Be(assignedThreadId);
        conversation.Threads.First().Attachments.First().ThreadId.Should().Be(assignedThreadId);
    }

    [Fact]
    public async Task SaveDraftAsync_WithEmptyAttachmentIds_ShouldNotFetchAttachments()
    {
        // Arrange
        var conversation = CreateConversation();
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        var request = CreateRequest(attachmentIds: new List<int>());

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        result.Should().NotBeNull();
        result!.Attachments.Should().BeEmpty();
        _mockAttachmentRepo.Verify(r => r.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>()), Times.Never);
        // 无附件 -> 仅调用一次 SaveChangesAsync
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task SaveDraftAsync_WithNullAttachmentIds_ShouldNotFetchAttachments()
    {
        // Arrange
        var conversation = CreateConversation();
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        var request = CreateRequest(attachmentIds: null);

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        result.Should().NotBeNull();
        result!.Attachments.Should().BeEmpty();
        _mockAttachmentRepo.Verify(r => r.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>()), Times.Never);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task SaveDraftAsync_WithAttachmentIdsButNoAttachmentsReturned_ShouldNotCallSecondSaveChanges()
    {
        // Arrange - 仓储返回空附件列表
        var conversation = CreateConversation();
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        _mockAttachmentRepo.Setup(r => r.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Attachment>());

        var request = CreateRequest(attachmentIds: new List<int> { 1, 2, 3 });

        // Act
        var result = await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        result.Should().NotBeNull();
        result!.Attachments.Should().BeEmpty();
        // 没有附件 -> 不应进入第二次 SaveChanges 分支
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region SaveDraftAsync - Transaction rollback

    [Fact]
    public async Task SaveDraftAsync_WhenSaveChangesThrows_ShouldRollbackAndRethrow()
    {
        // Arrange
        var conversation = CreateConversation();
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ThrowsAsync(new InvalidOperationException("DB error"));

        var request = CreateRequest();

        // Act
        Func<Task> act = async () => await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("DB error");

        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task SaveDraftAsync_WhenExistingDraft_AndSecondSaveChangesThrows_ShouldRollbackAndRethrow()
    {
        // Arrange - 测试有附件场景下的第二次 SaveChangesAsync 异常
        var conversation = CreateConversation();
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.GetDraftThreadTrackedAsync(1))
            .ReturnsAsync((ConversationThread?)null);

        var attachments = new List<Attachment>
        {
            new() { Id = 11, FileName = "a.txt", MimeType = "text/plain", Size = 100 }
        };
        _mockAttachmentRepo.Setup(r => r.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(attachments);

        var callCount = 0;
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .Callback(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // 第一次 SaveChanges 成功，分配主键
                    foreach (var t in conversation.Threads)
                    {
                        if (t.Id == 0) t.Id = 100;
                    }
                }
                else
                {
                    // 第二次 SaveChanges 抛出异常
                    throw new InvalidOperationException("Second save failed");
                }
            })
            .ReturnsAsync(1);

        var request = CreateRequest(attachmentIds: new List<int> { 11 });

        // Act
        Func<Task> act = async () => await _service.SaveDraftAsync(request, userId: 1);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Second save failed");

        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Never);
    }

    #endregion

    #region GetDraftAsync

    [Fact]
    public async Task GetDraftAsync_WithExistingDraft_ShouldReturnResponse()
    {
        // Arrange
        var draft = new ConversationThread
        {
            Id = 100,
            ConversationId = 1,
            Body = "<p>Draft body</p>",
            From = "agent@example.com",
            To = "cust@example.com",
            Subject = "Subject",
            State = ThreadState.Draft,
            Type = ThreadType.Message,
            UserId = 42,
            CustomerId = 10,
            IsInternal = false,
            CreatedAt = DateTime.UtcNow
        };

        _mockThreadRepo.Setup(r => r.GetDraftThreadAsync(1))
            .ReturnsAsync(draft);

        // Act
        var result = await _service.GetDraftAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(100);
        result.ConversationId.Should().Be(1);
        result.Body.Should().Be("<p>Draft body</p>");
        result.From.Should().Be("agent@example.com");
        result.To.Should().Be("cust@example.com");
        result.Subject.Should().Be("Subject");
        result.UserId.Should().Be(42);
        result.CustomerId.Should().Be(10);
        result.Type.Should().Be((int)ThreadType.Message);
        result.IsInternal.Should().BeFalse();
        result.CreatedAt.Should().NotBe(default);

        _mockThreadRepo.Verify(r => r.GetDraftThreadAsync(1), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task GetDraftAsync_WithNoDraft_ShouldReturnNull()
    {
        // Arrange
        _mockThreadRepo.Setup(r => r.GetDraftThreadAsync(999))
            .ReturnsAsync((ConversationThread?)null);

        // Act
        var result = await _service.GetDraftAsync(999);

        // Assert
        result.Should().BeNull();
        _mockThreadRepo.Verify(r => r.GetDraftThreadAsync(999), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task GetDraftAsync_ShouldNotCallSaveChanges()
    {
        // Arrange
        var draft = new ConversationThread
        {
            Id = 1,
            ConversationId = 1,
            State = ThreadState.Draft,
            Type = ThreadType.Message
        };
        _mockThreadRepo.Setup(r => r.GetDraftThreadAsync(1))
            .ReturnsAsync(draft);

        // Act
        await _service.GetDraftAsync(1);

        // Assert
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
        _mockUnitOfWork.Verify(u => u.BeginTransactionAsync(), Times.Never);
    }

    #endregion

    #region DeleteDraftAsync

    [Fact]
    public async Task DeleteDraftAsync_WithExistingDraft_ShouldDeleteAndReturnTrue()
    {
        // Arrange
        var draft = new ConversationThread
        {
            Id = 100,
            ConversationId = 1,
            State = ThreadState.Draft,
            Type = ThreadType.Message
        };

        _mockThreadRepo.Setup(r => r.GetDraftThreadAsync(1))
            .ReturnsAsync(draft);

        // Act
        var result = await _service.DeleteDraftAsync(1);

        // Assert
        result.Should().BeTrue();
        _mockThreadRepo.Verify(r => r.DeleteAsync(draft), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        // DeleteDraftAsync 不使用事务
        _mockUnitOfWork.Verify(u => u.BeginTransactionAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteDraftAsync_WithNoDraft_ShouldReturnFalse()
    {
        // Arrange
        _mockThreadRepo.Setup(r => r.GetDraftThreadAsync(999))
            .ReturnsAsync((ConversationThread?)null);

        // Act
        var result = await _service.DeleteDraftAsync(999);

        // Assert
        result.Should().BeFalse();
        _mockThreadRepo.Verify(r => r.DeleteAsync(It.IsAny<ConversationThread>()), Times.Never);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    #endregion

    #region GetDraftsByUserIdAsync

    [Fact]
    public async Task GetDraftsByUserIdAsync_WithDrafts_ShouldReturnResponses()
    {
        // Arrange
        var drafts = new List<ConversationThread>
        {
            new()
            {
                Id = 1,
                ConversationId = 10,
                Body = "<p>Body 1</p>",
                From = "agent1@example.com",
                To = "cust1@example.com",
                State = ThreadState.Draft,
                Type = ThreadType.Message,
                UserId = 42,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = 2,
                ConversationId = 20,
                Body = "<p>Body 2</p>",
                From = "agent2@example.com",
                To = "cust2@example.com",
                State = ThreadState.Draft,
                Type = ThreadType.Note,
                UserId = 42,
                IsInternal = true,
                CreatedAt = DateTime.UtcNow
            }
        };

        _mockThreadRepo.Setup(r => r.GetDraftsByUserIdAsync(42))
            .ReturnsAsync(drafts);

        // Act
        var result = await _service.GetDraftsByUserIdAsync(42);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);

        result[0].Id.Should().Be(1);
        result[0].ConversationId.Should().Be(10);
        result[0].Body.Should().Be("<p>Body 1</p>");
        result[0].From.Should().Be("agent1@example.com");
        result[0].To.Should().Be("cust1@example.com");
        result[0].UserId.Should().Be(42);
        result[0].Type.Should().Be((int)ThreadType.Message);
        result[0].IsInternal.Should().BeFalse();
        result[0].CreatedAt.Should().NotBe(default);

        result[1].Id.Should().Be(2);
        result[1].Type.Should().Be((int)ThreadType.Note);
        result[1].IsInternal.Should().BeTrue();

        _mockThreadRepo.Verify(r => r.GetDraftsByUserIdAsync(42), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task GetDraftsByUserIdAsync_WithNoDrafts_ShouldReturnEmptyList()
    {
        // Arrange
        _mockThreadRepo.Setup(r => r.GetDraftsByUserIdAsync(99))
            .ReturnsAsync(new List<ConversationThread>());

        // Act
        var result = await _service.GetDraftsByUserIdAsync(99);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDraftsByUserIdAsync_ShouldNotCallSaveChanges()
    {
        // Arrange
        _mockThreadRepo.Setup(r => r.GetDraftsByUserIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<ConversationThread>());

        // Act
        await _service.GetDraftsByUserIdAsync(1);

        // Assert
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
        _mockUnitOfWork.Verify(u => u.BeginTransactionAsync(), Times.Never);
    }

    #endregion
}
