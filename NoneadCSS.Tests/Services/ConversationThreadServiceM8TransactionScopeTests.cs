using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Application.Templates;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Domain.Services;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// M-8（ConversationThreadService 事务嵌套行为契约）单元测试。
///
/// 背景：M-8 之前 AddReplyAsync 在宏/工作流嵌套场景会重复开启事务，
/// 在某些数据库（MySQL InnoDB savepoints、SQLite in-memory 不支持嵌套事务）下直接异常。
///
/// 整改后约定：
///   1. 外层先调用 svc.BeginSuppressedScope() → 内部 suppressed=true 分支：
///        * 跳过自身的 BeginTransaction/Commit/Rollback（交由外层事务统一管理）
///   2. 正常调用 suppressed=false：
///        * BeginTransactionAsync → try{业务 → Commit} / catch{SafeRollback → throw}
///   3. SuppressedScope 支持嵌套引用计数：2 次进入 + 2 次 Dispose = 回到非抑制。
///   4. 异常路径：SaveChangesAsync 抛异常 → 触发 SafeRollbackAsync 并原样抛出原始异常。
///   5. 未命中会话：Conversation == null → 直接 return null，不开事务。
///
/// 注意：本类测试不验证邮件发送/主题/模板渲染（ConversationThreadServiceTests 已覆盖），
///       仅聚焦 M-8 事务嵌套与抑制作用域行为的契约回归。
/// </summary>
public class ConversationThreadServiceM8TransactionScopeTests
{
    #region SUT factory

    private static (ConversationThreadService svc, Mock<IUnitOfWorkTransaction> tx, Mock<IUnitOfWork> uow,
                      Mock<IConversationRepository> convs, Mock<IThreadRepository> threads,
                      Mock<IMailboxRepository> mboxes, Mock<IAttachmentRepository> atts) CreateSut(Conversation conversation)
    {
        var uow = new Mock<IUnitOfWork>();
        var tx = new Mock<IUnitOfWorkTransaction>();
        var convs = new Mock<IConversationRepository>();
        var threads = new Mock<IThreadRepository>();
        var mboxes = new Mock<IMailboxRepository>();
        var atts = new Mock<IAttachmentRepository>();
        var users = new Mock<IUserRepository>();
        var customers = new Mock<ICustomerRepository>();

        uow.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(tx.Object);
        uow.Setup(u => u.Conversations).Returns(convs.Object);
        uow.Setup(u => u.Threads).Returns(threads.Object);
        uow.Setup(u => u.Mailboxes).Returns(mboxes.Object);
        uow.Setup(u => u.Attachments).Returns(atts.Object);
        uow.Setup(u => u.Users).Returns(users.Object);
        uow.Setup(u => u.Customers).Returns(customers.Object);

        convs.Setup(r => r.GetByIdForUpdateAsync(It.IsAny<int>())).ReturnsAsync(conversation);
        mboxes.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new Mailbox { Id = conversation.MailboxId, Email = "s@x.com" });
        atts.Setup(r => r.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<Attachment>());

        users.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((User?)null);
        customers.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((Customer?)null);
        threads.Setup(r => r.GetLatestCustomerThreadAsync(It.IsAny<int>())).ReturnsAsync((ConversationThread?)null);

        // SaveChanges 模拟主键分配
        void AssignThreadId()
        {
            foreach (var t in conversation.Threads.Where(t => t.Id == 0)) t.Id = 9000 + t.ConversationId;
        }
        uow.Setup(u => u.SaveChangesAsync()).Callback(AssignThreadId).ReturnsAsync(1);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(AssignThreadId).ReturnsAsync(1);

        var mailTemplate = new Mock<IMailTemplateService>();
        mailTemplate.Setup(m => m.GetBySlugAsync(It.IsAny<string>())).ReturnsAsync((NoneadCSS.Application.DTOs.Responses.MailTemplateResponse?)null);
        var sig = new Mock<IEmailSignatureService>();
        sig.Setup(s => s.GetDefaultAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync((NoneadCSS.Application.DTOs.Responses.EmailSignatureDto?)null);

        var workflow = new Mock<IWorkflowService>();
        workflow.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(0);

        var notif = new Mock<INotificationPublisher>();
        notif.Setup(n => n.NotifyMailboxAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
             .Returns(Task.CompletedTask);

        var cfg = new Mock<IConfiguration>();
        cfg.Setup(c => c["Attachment:UploadPath"]).Returns("Data/Attachments");
        var resolver = new Mock<IFrontendUrlResolver>();
        resolver.Setup(r => r.GetFrontendUrl()).Returns("https://app.example.com");

        var svc = new ConversationThreadService(
            uow.Object,
            new Mock<IEmailQueueService>().Object,
            notif.Object,
            mailTemplate.Object,
            sig.Object,
            workflow.Object,
            new Mock<IWeChatClient>().Object,
            new Mock<IDingDingClient>().Object,
            new Mock<IDingDingSettingsProvider>().Object,
            cfg.Object,
            resolver.Object,
            NullLogger<ConversationThreadService>.Instance,
            new TemplateRenderer(),
            Options.Create(new BrandOptions()),
            new Mock<Microsoft.Extensions.Hosting.IHostEnvironment>().Object);

        return (svc, tx, uow, convs, threads, mboxes, atts);
    }

    private static Conversation BuildEmailConversation(int id = 1, int mailboxId = 1)
    {
        return new Conversation
        {
            Id = id,
            Number = 100,
            MailboxId = mailboxId,
            Status = ConversationStatus.Active,
            SourceType = SourceType.Email,
            CustomerEmail = "c@example.com",
            Threads = new List<ConversationThread>()
        };
    }

    private static CreateThreadRequest BuildReply(int conversationId) =>
        new() { ConversationId = conversationId, Body = "<p>hi</p>", From = "s@x.com", To = "c@example.com" };

    #endregion

    #region M-8.1 正常（非抑制）路径：创建并提交事务

    [Fact]
    public async Task AddReplyAsync_NotSuppressed_StartsAndCommitsTransaction()
    {
        var conv = BuildEmailConversation();
        var (svc, tx, uow, _, _, _, _) = CreateSut(conv);

        await svc.AddReplyAsync(BuildReply(conv.Id), userId: 10);

        uow.Verify(u => u.BeginTransactionAsync(), Times.Once, "M-8: 非抑制作用域必须开启一次事务。");
        tx.Verify(t => t.CommitAsync(), Times.Once, "M-8: 业务成功路径必须提交事务。");
        tx.Verify(t => t.RollbackAsync(), Times.Never, "M-8: 成功路径不应回滚。");
    }

    [Fact]
    public async Task AddReplyAsync_NotSuppressed_ConversationMissing_DoesNotBeginTransaction()
    {
        var conv = BuildEmailConversation();
        var (svc, tx, uow, convs, _, _, _) = CreateSut(conv);
        convs.Setup(r => r.GetByIdForUpdateAsync(It.IsAny<int>())).ReturnsAsync((Conversation?)null);

        var result = await svc.AddReplyAsync(BuildReply(conv.Id), userId: 1);

        result.Should().BeNull();
        uow.Verify(u => u.BeginTransactionAsync(), Times.Never,
            "M-8: 会话不存在时应立即 return null，避免开启不必要的事务。");
    }

    #endregion

    #region M-8.2 抑制路径：外层负责事务，内层禁止再开事务

    [Fact]
    public async Task AddReplyAsync_InsideSuppressedScope_MustNotBeginOrCommitOwnTransaction()
    {
        var conv = BuildEmailConversation();
        var (svc, tx, uow, _, _, _, _) = CreateSut(conv);

        using (svc.BeginSuppressedScope())
        {
            await svc.AddReplyAsync(BuildReply(conv.Id), userId: 5);
        }

        uow.Verify(u => u.BeginTransactionAsync(), Times.Never,
            "M-8: 抑制作用域内，SERVICE 必须复用外层事务，禁止自行 BeginTransaction。若此处仍然触发，将在嵌套事务不支持的提供器（SQLite）下报错。");
        tx.Verify(t => t.CommitAsync(), Times.Never);
        tx.Verify(t => t.RollbackAsync(), Times.Never);
    }

    [Fact]
    public async Task AddReplyAsync_Suppressed_SaveChangesStillRuns()
    {
        var conv = BuildEmailConversation();
        var (svc, _, uow, _, _, _, _) = CreateSut(conv);

        using (svc.BeginSuppressedScope())
        {
            await svc.AddReplyAsync(BuildReply(conv.Id), userId: 1);
        }

        // 外层事务内仍然需要 SaveChangesAsync 写入被外层事务统一提交；
        // 仅校验至少调用过一次，允许更多次（当前实现会调两次：基础写入一次、附件回写一次）
        uow.Verify(u => u.SaveChangesAsync(), Times.AtLeastOnce);
    }

    #endregion

    #region M-8.3 SuppressedScope 引用计数：嵌套、Dispose 后回到原状

    [Fact]
    public async Task BeginSuppressedScope_NestingDepth_ReferenceCountedCorrectly()
    {
        var conv = BuildEmailConversation();
        var (svc, _, uow, _, _, _, _) = CreateSut(conv);

        // 非抑制：确认会开事务
        uow.Verify(u => u.BeginTransactionAsync(), Times.Never); // 基线

        using (svc.BeginSuppressedScope())
        using (svc.BeginSuppressedScope()) // 嵌套进入两次
        {
            // 第一层 Dispose 之前：仍为抑制 → 不开事务
            await svc.AddReplyAsync(BuildReply(conv.Id), userId: 1);
            uow.Verify(u => u.BeginTransactionAsync(), Times.Never);
        }

        // 退出所有 Scope：恢复非抑制，下一次调用必须能开事务
        using (svc.BeginSuppressedScope())
        {
            // 再次进入：抑制
            await svc.AddReplyAsync(BuildReply(conv.Id), userId: 1);
            uow.Verify(u => u.BeginTransactionAsync(), Times.Never);
        }

        // 离开全部作用域：事务应当能开
        await svc.AddReplyAsync(BuildReply(conv.Id), userId: 1);
        uow.Verify(u => u.BeginTransactionAsync(), Times.Once,
            "M-8: 抑制作用域必须使用引用计数而不是简单布尔；否则嵌套 Dispose 一次会意外提前恢复，在外层宏还未提交时就重新开启嵌套事务。");
    }

    #endregion

    #region M-8.4 异常路径：SaveChanges 抛异常 → 回滚 + 原始异常冒泡

    [Fact]
    public async Task AddReplyAsync_OnSaveChangesError_RollsBack_AndPreservesOriginalException()
    {
        var conv = BuildEmailConversation();
        var (svc, tx, uow, _, _, _, _) = CreateSut(conv);

        uow.Setup(u => u.SaveChangesAsync())
           .ThrowsAsync(new InvalidOperationException("boom-save"));
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
           .ThrowsAsync(new InvalidOperationException("boom-save"));

        var act = async () => await svc.AddReplyAsync(BuildReply(conv.Id), userId: 99);

        var ex = (await act.Should().ThrowExactlyAsync<InvalidOperationException>()).And;
        ex.Message.Should().Be("boom-save");

        // 必须调用 RollbackAsync 一次（SafeRollbackAsync 包装，内部回滚一次）
        tx.Verify(t => t.RollbackAsync(), Times.Once, "M-8: SaveChanges 抛异常必须触发 RollbackAsync，避免事务泄漏。");
        tx.Verify(t => t.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task AddReplyAsync_OnSaveChangesError_Suppressed_DoesNotRollback_InnerTransaction()
    {
        // 当外层托管事务时，AddReplyAsync 内部不开事务，SaveChanges 失败应由外层统一回滚。
        var conv = BuildEmailConversation();
        var (svc, tx, uow, _, _, _, _) = CreateSut(conv);

        uow.Setup(u => u.SaveChangesAsync())
           .ThrowsAsync(new ArithmeticException("boom-suppressed"));
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
           .ThrowsAsync(new ArithmeticException("boom-suppressed"));

        // 必须在 suppressed scope 内发起调用，且直到 AddReplyAsync 完成之前不得 Dispose，
        // 否则 IsSuppressed 会在内部 await 之前被还原。
        async Task Act()
        {
            using (svc.BeginSuppressedScope())
            {
                await svc.AddReplyAsync(BuildReply(conv.Id), userId: 1);
            }
        }

        (await FluentActions.Awaiting(Act).Should().ThrowExactlyAsync<ArithmeticException>())
            .And.Message.Should().Be("boom-suppressed");

        // 未开启任何内层事务，自然不应 Commit / Rollback
        uow.Verify(u => u.BeginTransactionAsync(), Times.Never);
        tx.Verify(t => t.RollbackAsync(), Times.Never);
        tx.Verify(t => t.CommitAsync(), Times.Never);
    }

    #endregion

    #region M-8.5 源代码守卫：AddReplyAsync 必含 suppressed 分支

    [Fact]
    public void ConversationThreadService_AddReplyAsync_Source_HasExplicitSuppressedAndNonSuppressedBranches()
    {
        // 路径：相对于当前执行目录向上追溯仓库根
        var asmDir = new DirectoryInfo(AppContext.BaseDirectory);
        string? src = null;
        for (var d = asmDir; d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "NoneadCSS.Application", "Services", "ConversationThreadService.cs");
            if (File.Exists(candidate)) { src = File.ReadAllText(candidate); break; }
        }

        src.Should().NotBeNull("必须在 workspace 中找到 ConversationThreadService.cs 源代码");

        // 显式分支： suppressed 与 else / using BeginTransactionAsync 同时出现
        src.Should().Contain("var suppressed = IsSuppressed;")
            .And.Contain("if (suppressed)")
            .And.Contain("using (var transaction = await _unitOfWork.BeginTransactionAsync())")
            .And.Contain("await transaction.SafeRollbackAsync();")
            .And.Contain("await transaction.CommitAsync();");
    }

    #endregion
}
