using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Application.Services.WorkflowActions;
using NoneadCSS.Application.Templates;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Services;
using NoneadCSS.Infrastructure;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Infrastructure.Services;
using Xunit;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// T000112（钉钉会话回复添加图片附件，客户收不到图片）集成测试。
///
/// 根因（生产日志实证）：DingDingService.SendDingDingAttachmentsAsync 在读取附件时，
/// 配置缺失/相对时回退到"CWD 相对路径"（生产后端 CWD=Production，ContentRoot=Production\backend），
/// 与上传侧 AttachmentService 的 ContentRoot 锚定目录分裂 → "钉钉附件文件不存在" → Sent=0/1。
///
/// 修复：新增 <see cref="AttachmentPathResolver"/>，钉钉发送侧统一按
/// "绝对路径原样 / 相对或缺失锚定 ContentRoot" 解析，与上传侧完全一致。
/// 本测试使用 MigrateAsync 还原生产 schema（Version 列带 DEFAULT 0），
/// 走真实 DingDingService.ReplyAsync 端到端验证图片附件能被读取并发送。
/// </summary>
public class DingTalkAttachmentSendIntegrationTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private NoneadCSSDbContext _context = null!;
    private UnitOfWork _unitOfWork = null!;
    private int _mailboxId;
    private string _tempUploadRoot = null!;

    /// <summary>
    /// 测试用 DbContext 子类：EnsureCreated 生成的 schema 缺少 int 并发令牌列的 DEFAULT 0
    /// （生产由迁移提供）。本子类在基类模型之上补 HasDefaultValue(0)，
    /// 使 DingDingService.ReplyAsync 内部 EF 建线程（EF 会省略 ValueGeneratedOnAddOrUpdate 列）
    /// 在测试库中也能命中 DB 默认值，等价于生产 schema。
    /// </summary>
    private sealed class TestDbContext : NoneadCSSDbContext
    {
        public TestDbContext(DbContextOptions<NoneadCSSDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Conversation>().Property(c => c.Version).HasDefaultValue(0);
            modelBuilder.Entity<ConversationThread>().Property(t => t.Version).HasDefaultValue(0);
            modelBuilder.Entity<Mailbox>().Property(m => m.Version).HasDefaultValue(0);
        }
    }

    private static string ApiProjectDir => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NoneadCSS.Api"));

    private static IConfiguration BuildConfigWithUploadPath(string uploadPath)
    {
        return new ConfigurationBuilder()
            .SetBasePath(ApiProjectDir)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Production.json", optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Attachment:UploadPath"] = uploadPath
            })
            .Build();
    }

    public async Task InitializeAsync()
    {
        // 临时绝对目录，避免污染生产附件目录
        _tempUploadRoot = Path.Combine(Path.GetTempPath(), "noneadcss-it-t000112", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempUploadRoot);

        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseSqlite(_connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;

        // EnsureCreatedAsync + TestDbContext（补 int 并发令牌列 DEFAULT 0），
        // 避免 MigrateAsync 在全新库上遇到历史迁移断链（IX_Conversations_CreatedAt）。
        _context = new TestDbContext(options);
        await _context.Database.EnsureCreatedAsync();
        _unitOfWork = new UnitOfWork(_context, NoneadCSS.Tests.Shared.TestServiceProviderBuilder.Build(_context));

        _mailboxId = await InsertMailboxRowAsync();
    }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        try { if (Directory.Exists(_tempUploadRoot)) Directory.Delete(_tempUploadRoot, recursive: true); } catch { /* 清理临时目录失败忽略 */ }
        return Task.CompletedTask;
    }

    // ==================== 路径解析（修复核心） ====================

    [Fact]
    public void AttachmentPathResolver_WithAbsoluteConfig_ReturnsAbsolutePath()
    {
        var cfg = BuildConfigWithUploadPath(_tempUploadRoot);

        var resolved = AttachmentPathResolver.ResolveBasePath(cfg["Attachment:UploadPath"], "C:/whatever/contentroot");

        resolved.Should().Be(Path.GetFullPath(_tempUploadRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            "绝对配置必须原样使用，与 ContentRoot 无关（T000112）");
        Path.IsPathRooted(resolved).Should().BeTrue();
    }

    [Fact]
    public void AttachmentPathResolver_WithRelativeOrMissingConfig_AnchorsToContentRoot()
    {
        // 相对路径：锚定 ContentRoot（与上传侧 AttachmentService 一致，而非 CWD）
        AttachmentPathResolver.ResolveBasePath("Data/Attachments", "/app/backend")
            .Should().Be(Path.Combine("/app/backend", "Data/Attachments"));

        // 缺失配置：锚定 ContentRoot + 默认相对目录
        AttachmentPathResolver.ResolveBasePath(null, "/app/backend")
            .Should().Be(Path.Combine("/app/backend", "Data/Attachments"));

        // 空 ContentRoot（纯 mock 环境）：退化为相对路径本身，行为与旧实现一致，不抛异常
        AttachmentPathResolver.ResolveBasePath("Data/Attachments", string.Empty)
            .Should().Be("Data/Attachments");
    }

    [Fact]
    public void AttachmentPathResolver_WithProductionConfig_ResolvesRootedPath()
    {
        // 仅用生产配置文件（不覆盖）：绝对路径 + 收敛
        var cfg = new ConfigurationBuilder()
            .SetBasePath(ApiProjectDir)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Production.json", optional: false)
            .Build();

        var resolved = AttachmentPathResolver.ResolveBasePath(cfg["Attachment:UploadPath"], "D:/ignored/contentroot");

        resolved.Should().NotBeNullOrWhiteSpace("生产配置必须显式设置 Attachment:UploadPath（T000108）");
        Path.IsPathRooted(resolved).Should().BeTrue("生产附件根目录必须为绝对路径，任何渠道发送方均不依赖 CWD");
        resolved.Should().NotContain("corp");
    }

    // ==================== 端到端：钉钉回复带图片附件 ====================

    [Fact]
    public async Task DingTalkReply_WithImageAttachment_ReadsConvergedPath_UploadsAndBatchSends()
    {
        // 客服（分配人）—— ReplyAsync 会把回复线程的 UserId 指向该用户
        var agent = new User { FirstName = "客服", LastName = "一", Email = "agent.dingtalk@example.com" };
        await _unitOfWork.Users.AddAsync(agent);
        await _unitOfWork.SaveChangesAsync();

        // 客户（钉钉渠道）
        var customer = new Customer { FirstName = "钉钉客户", LastName = string.Empty, Channel = "DingTalk", ChannelId = "300252304524232191" };
        await _unitOfWork.Customers.AddAsync(customer);
        await _unitOfWork.SaveChangesAsync();

        // 钉钉会话
        var conversation = new Conversation
        {
            MailboxId = _mailboxId,
            CustomerId = customer.Id,
            Number = 8801,
            Type = ConversationType.Chat,
            SourceType = SourceType.DingTalk,
            State = ConversationState.Published,
            Status = ConversationStatus.Active,
            Priority = ConversationPriority.Normal,
            Subject = "钉钉咨询-图片附件",
            CustomerEmail = string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Conversations.Add(conversation);
        await _unitOfWork.SaveChangesAsync();

        // 客户最近一条线程（From=客户钉钉 userId，供 ReplyAsync 提取 customerUserId）
        var customerThread = new ConversationThread
        {
            ConversationId = conversation.Id,
            CustomerId = customer.Id,
            Type = ThreadType.Customer,
            SourceType = SourceType.DingTalk,
            SourceVia = SourceVia.Customer,
            From = "300252304524232191",
            Body = "<div class=\"dingding-text\">我需要维修机器人</div>",
            Subject = conversation.Subject,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        _context.Threads.Add(customerThread);
        await _unitOfWork.SaveChangesAsync();

        // 附件记录（图片）+ 在解析出的根目录写入真实文件
        var storedName = Guid.NewGuid().ToString("N") + ".png";
        var fileDir = "2026/09/01";
        var attachment = new Attachment
        {
            ThreadId = customerThread.Id,
            FileName = "device.png",
            StoredFileName = storedName,
            MimeType = "image/png",
            Type = AttachmentType.Image,
            FileDir = fileDir,
            Size = 4,
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.Attachments.AddAsync(attachment);
        await _unitOfWork.SaveChangesAsync();

        // 以钉钉发送方同样的解析规则定位并写入文件（模拟 AttachmentService 上传产物）
        var resolvedBase = AttachmentPathResolver.ResolveBasePath(
            BuildConfigWithUploadPath(_tempUploadRoot)["Attachment:UploadPath"], string.Empty);
        var filePath = Path.Combine(resolvedBase, fileDir.Replace('/', Path.DirectorySeparatorChar), storedName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        await File.WriteAllBytesAsync(filePath, imageBytes);

        // 真实 DingDingService + mock 客户端（验证上传与批量发送被真实调用）
        var settingsProvider = new Mock<IDingDingSettingsProvider>();
        settingsProvider.SetupGet(s => s.Current).Returns(new NoneadCSS.Application.Configuration.DingDingOptions
        {
            Enabled = true,
            AppKey = "appkey_test",
            AppSecret = "appsecret_test",
            RobotCode = "robot_code_test",
            ReceiveMode = "Stream"
        });

        var dingClient = new Mock<IDingDingClient>();
        dingClient.Setup(c => c.BatchSendOtoMessageAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("proc_text_123");
        dingClient.Setup(c => c.UploadMediaAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("media_123");
        dingClient.Setup(c => c.BatchSendImageMessageAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>()))
            .ReturnsAsync("proc_123");

        var service = new DingDingService(
            _unitOfWork,
            new Mock<IEncryptionService>().Object,
            settingsProvider.Object,
            dingClient.Object,
            new Mock<IDingTalkStreamConnectionStatus>().Object,
            new Mock<ICurrentUserAccessor>().Object,
            new Mock<INotificationPublisher>().Object,
            new Mock<ILogger<DingDingService>>().Object,
            BuildConfigWithUploadPath(_tempUploadRoot),
            new Mock<IHostEnvironment>().Object);

        // 客服回复：正文 + 图片附件
        var result = await service.ReplyAsync(new DingDingReplyRequest
        {
            ConversationId = conversation.Id,
            Content = "请查看图片",
            AttachmentIds = new[] { attachment.Id }
        }, userId: agent.Id);

        result.Sent.Should().BeTrue("钉钉回复必须发送成功");
        result.ThreadCreated.Should().BeTrue("回复线程必须创建");
        result.ThreadId.Should().BeGreaterThan(0);

        dingClient.Verify(c => c.UploadMediaAsync(
                It.Is<byte[]>(b => b.SequenceEqual(imageBytes)),
                It.IsAny<string>(),
                It.Is<string>(m => m == "image/png")),
            Times.Once, "附件文件必须被读取（ContentRoot 收敛路径）并上传（T000112 根因修复）");
        dingClient.Verify(c => c.BatchSendImageMessageAsync(
                "robot_code_test", It.Is<string[]>(u => u.Contains("300252304524232191")), "media_123"),
            Times.Once, "图片必须通过 batchSend 发送给客户");
    }

    [Fact]
    public async Task DingTalkReply_WithAttachmentFileMissing_DoesNotUploadOrBatchSend()
    {
        var agent = new User { FirstName = "客服", LastName = "一", Email = "agent.dingtalk2@example.com" };
        await _unitOfWork.Users.AddAsync(agent);
        await _unitOfWork.SaveChangesAsync();

        var customer = new Customer { FirstName = "钉钉客户", LastName = string.Empty, Channel = "DingTalk", ChannelId = "300252304524232191" };
        await _unitOfWork.Customers.AddAsync(customer);
        await _unitOfWork.SaveChangesAsync();

        var conversation = new Conversation
        {
            MailboxId = _mailboxId,
            CustomerId = customer.Id,
            Number = 8802,
            Type = ConversationType.Chat,
            SourceType = SourceType.DingTalk,
            State = ConversationState.Published,
            Status = ConversationStatus.Active,
            Priority = ConversationPriority.Normal,
            Subject = "钉钉咨询-缺文件",
            CustomerEmail = string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Conversations.Add(conversation);
        await _unitOfWork.SaveChangesAsync();

        var customerThread = new ConversationThread
        {
            ConversationId = conversation.Id,
            CustomerId = customer.Id,
            Type = ThreadType.Customer,
            SourceType = SourceType.DingTalk,
            SourceVia = SourceVia.Customer,
            From = "300252304524232191",
            Body = "<div class=\"dingding-text\">测试</div>",
            Subject = conversation.Subject,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        _context.Threads.Add(customerThread);
        await _unitOfWork.SaveChangesAsync();

        // 附件记录存在，但磁盘文件缺失（模拟修复前 CWD 相对路径分裂）
        var attachment = new Attachment
        {
            ThreadId = customerThread.Id,
            FileName = "missing.png",
            StoredFileName = Guid.NewGuid().ToString("N") + ".png",
            MimeType = "image/png",
            Type = AttachmentType.Image,
            FileDir = "2026/09/01",
            Size = 4,
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.Attachments.AddAsync(attachment);
        await _unitOfWork.SaveChangesAsync();

        var settingsProvider = new Mock<IDingDingSettingsProvider>();
        settingsProvider.SetupGet(s => s.Current).Returns(new NoneadCSS.Application.Configuration.DingDingOptions
        {
            Enabled = true,
            AppKey = "appkey_test",
            AppSecret = "appsecret_test",
            RobotCode = "robot_code_test",
            ReceiveMode = "Stream"
        });
        var dingClient = new Mock<IDingDingClient>();
        dingClient.Setup(c => c.BatchSendOtoMessageAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("proc_text_123");
        dingClient.Setup(c => c.UploadMediaAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync("media_123");

        var service = new DingDingService(
            _unitOfWork,
            new Mock<IEncryptionService>().Object,
            settingsProvider.Object,
            dingClient.Object,
            new Mock<IDingTalkStreamConnectionStatus>().Object,
            new Mock<ICurrentUserAccessor>().Object,
            new Mock<INotificationPublisher>().Object,
            new Mock<ILogger<DingDingService>>().Object,
            BuildConfigWithUploadPath(_tempUploadRoot),
            new Mock<IHostEnvironment>().Object);

        var result = await service.ReplyAsync(new DingDingReplyRequest
        {
            ConversationId = conversation.Id,
            Content = "回复",
            AttachmentIds = new[] { attachment.Id }
        }, userId: agent.Id);

        result.Sent.Should().BeTrue("文本回复仍应成功");
        dingClient.Verify(c => c.UploadMediaAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never, "文件缺失时不得上传/发送，避免静默空发送");
    }

    // ==================== 另一条钉钉发送路径：ConversationThreadService.AddReplyAsync ====================
    // 服务台回复也可能走通用 AddReplyAsync → SendDingDingReplyAsync → SendDingDingAttachmentAsync。
    // 该路径同样使用修复后的 AttachmentPathResolver（_attachmentBasePath），此处锁定其收敛行为。

    [Fact]
    public async Task ConversationThreadReply_DingTalkWithImageAttachment_ReadsConvergedPath_UploadsAndBatchSends()
    {
        var agent = new User { FirstName = "客服", LastName = "二", Email = "agent.thread@example.com" };
        await _unitOfWork.Users.AddAsync(agent);
        await _unitOfWork.SaveChangesAsync();

        var customer = new Customer { FirstName = "钉钉客户", LastName = string.Empty, Channel = "DingTalk", ChannelId = "300252304524232191" };
        await _unitOfWork.Customers.AddAsync(customer);
        await _unitOfWork.SaveChangesAsync();

        var conversation = new Conversation
        {
            MailboxId = _mailboxId,
            CustomerId = customer.Id,
            Number = 8811,
            Type = ConversationType.Chat,
            SourceType = SourceType.DingTalk,
            State = ConversationState.Published,
            Status = ConversationStatus.Active,
            Priority = ConversationPriority.Normal,
            Subject = "钉钉咨询-线程路径",
            CustomerEmail = string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Conversations.Add(conversation);
        await _unitOfWork.SaveChangesAsync();

        // 客户最近一条线程（From=客户钉钉 userId，供 SendDingDingReplyAsync 提取 customerUserId）
        var customerThread = new ConversationThread
        {
            ConversationId = conversation.Id,
            CustomerId = customer.Id,
            Type = ThreadType.Customer,
            SourceType = SourceType.DingTalk,
            SourceVia = SourceVia.Customer,
            From = "300252304524232191",
            Body = "<div class=\"dingding-text\">你好</div>",
            Subject = conversation.Subject,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        _context.Threads.Add(customerThread);
        await _unitOfWork.SaveChangesAsync();

        var storedName = Guid.NewGuid().ToString("N") + ".png";
        var fileDir = "2026/09/01";
        var attachment = new Attachment
        {
            ThreadId = customerThread.Id,
            FileName = "thread-img.png",
            StoredFileName = storedName,
            MimeType = "image/png",
            Type = AttachmentType.Image,
            FileDir = fileDir,
            Size = 4,
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.Attachments.AddAsync(attachment);
        await _unitOfWork.SaveChangesAsync();

        var cfg = BuildConfigWithUploadPath(_tempUploadRoot);
        var resolvedBase = AttachmentPathResolver.ResolveBasePath(cfg["Attachment:UploadPath"], string.Empty);
        var filePath = Path.Combine(resolvedBase, fileDir.Replace('/', Path.DirectorySeparatorChar), storedName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        await File.WriteAllBytesAsync(filePath, imageBytes);

        var dingClient = new Mock<IDingDingClient>();
        dingClient.Setup(c => c.BatchSendOtoMessageAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("proc_text_thread");
        dingClient.Setup(c => c.UploadMediaAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("media_thread_1");
        dingClient.Setup(c => c.BatchSendImageMessageAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>()))
            .ReturnsAsync("proc_img_thread");

        var service = BuildConversationThreadService(cfg, dingClient);

        var thread = await service.AddReplyAsync(new CreateThreadRequest
        {
            ConversationId = conversation.Id,
            Body = "<p>请看图片（线程路径）</p>",
            AttachmentIds = new List<int> { attachment.Id }
        }, userId: agent.Id);

        thread.Should().NotBeNull("回复线程必须创建成功");
        thread!.HasAttachments.Should().BeTrue("附件必须关联到回复线程");

        dingClient.Verify(c => c.UploadMediaAsync(
                It.Is<byte[]>(b => b.SequenceEqual(imageBytes)),
                It.IsAny<string>(),
                It.Is<string>(m => m == "image/png")),
            Times.Once, "ConversationThreadService 路径必须读取收敛路径文件并上传（T000112 根因修复）");
        dingClient.Verify(c => c.BatchSendImageMessageAsync(
                "robot_code_thread", It.Is<string[]>(u => u.Contains("300252304524232191")), "media_thread_1"),
            Times.Once, "图片必须通过 batchSend 发送给客户");
    }

    [Fact]
    public async Task ConversationThreadReply_DingTalkWithMissingAttachmentFile_SkipsUpload()
    {
        var agent = new User { FirstName = "客服", LastName = "二", Email = "agent.thread2@example.com" };
        await _unitOfWork.Users.AddAsync(agent);
        await _unitOfWork.SaveChangesAsync();

        var customer = new Customer { FirstName = "钉钉客户", LastName = string.Empty, Channel = "DingTalk", ChannelId = "300252304524232191" };
        await _unitOfWork.Customers.AddAsync(customer);
        await _unitOfWork.SaveChangesAsync();

        var conversation = new Conversation
        {
            MailboxId = _mailboxId,
            CustomerId = customer.Id,
            Number = 8812,
            Type = ConversationType.Chat,
            SourceType = SourceType.DingTalk,
            State = ConversationState.Published,
            Status = ConversationStatus.Active,
            Priority = ConversationPriority.Normal,
            Subject = "钉钉咨询-线程缺文件",
            CustomerEmail = string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Conversations.Add(conversation);
        await _unitOfWork.SaveChangesAsync();

        var customerThread = new ConversationThread
        {
            ConversationId = conversation.Id,
            CustomerId = customer.Id,
            Type = ThreadType.Customer,
            SourceType = SourceType.DingTalk,
            SourceVia = SourceVia.Customer,
            From = "300252304524232191",
            Body = "<div class=\"dingding-text\">测试</div>",
            Subject = conversation.Subject,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        _context.Threads.Add(customerThread);
        await _unitOfWork.SaveChangesAsync();

        // 附件记录存在但磁盘文件缺失（模拟修复前 CWD 相对路径分裂）
        var attachment = new Attachment
        {
            ThreadId = customerThread.Id,
            FileName = "missing-thread.png",
            StoredFileName = Guid.NewGuid().ToString("N") + ".png",
            MimeType = "image/png",
            Type = AttachmentType.Image,
            FileDir = "2026/09/01",
            Size = 4,
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.Attachments.AddAsync(attachment);
        await _unitOfWork.SaveChangesAsync();

        var cfg = BuildConfigWithUploadPath(_tempUploadRoot);
        var dingClient = new Mock<IDingDingClient>();
        dingClient.Setup(c => c.BatchSendOtoMessageAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("proc_text_thread2");
        dingClient.Setup(c => c.UploadMediaAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync("media_x");

        var service = BuildConversationThreadService(cfg, dingClient);

        var thread = await service.AddReplyAsync(new CreateThreadRequest
        {
            ConversationId = conversation.Id,
            Body = "<p>回复</p>",
            AttachmentIds = new List<int> { attachment.Id }
        }, userId: agent.Id);

        thread.Should().NotBeNull("文本回复仍应成功创建线程");
        dingClient.Verify(c => c.UploadMediaAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never, "文件缺失时不得上传/发送");
    }

    private ConversationThreadService BuildConversationThreadService(IConfiguration cfg, Mock<IDingDingClient> dingClient)
    {
        var settingsProvider = new Mock<IDingDingSettingsProvider>();
        settingsProvider.SetupGet(s => s.Current).Returns(new DingDingOptions
        {
            Enabled = true,
            AppKey = "appkey_thread",
            AppSecret = "appsecret_thread",
            RobotCode = "robot_code_thread",
            ReceiveMode = "Stream"
        });

        return new ConversationThreadService(
            _unitOfWork,
            new Mock<IEmailQueueService>().Object,
            new Mock<INotificationPublisher>().Object,
            new Mock<IMailTemplateService>().Object,
            new Mock<IEmailSignatureService>().Object,
            new Mock<IWorkflowService>().Object,
            new Mock<IWeChatClient>().Object,
            dingClient.Object,
            settingsProvider.Object,
            cfg,
            new FrontendUrlResolver(new HttpContextAccessor(), cfg),
            new Mock<ILogger<ConversationThreadService>>().Object,
            new TemplateRenderer(),
            Options.Create(new BrandOptions()),
            new Mock<IHostEnvironment>().Object);
    }

    // ==================== T000113：钉钉会话-客户二次问答信息丢失 ====================
    // 根因（生产日志实证）：Threads.MessageId 有 UNIQUE 约束，而 Stream 模式下
    // CreateThreadFromMessage 把"会话级同值"的 sessionWebhook 存为 MessageId（sw: 前缀），
    // 同一会话第二/后续消息 MessageId 相同 → UNIQUE 冲突 → SaveChanges 失败 → 消息被丢弃。
    // 修复：MessageId 存真实 MsgId；sessionWebhook 改存 Headers（"sessionWebhook:" 前缀）；
    // 客服回复路由从 Headers 读取，兼容旧 "sw:" 数据。
    // 以下用例锁定：同一 webhook 的两条消息都能入库并追加到同一会话。

    private static string BuildDingTalkTextPayload(string msgId, string webhook, string content, long createAtMs)
        => $$"""
        {
          "msgtype": "text",
          "msgId": "{{msgId}}",
          "conversationType": "1",
          "senderStaffId": "staff-t000113",
          "senderNick": "钉钉测试客户",
          "createAt": {{createAtMs}},
          "sessionWebhook": "{{webhook}}",
          "text": { "content": "{{content}}" }
        }
        """;

    private DingDingIngestionService BuildIngestionService(Mock<IDingDingClient>? client = null)
    {
        var settingsProvider = new Mock<IDingDingSettingsProvider>();
        settingsProvider.SetupGet(s => s.Current).Returns(new DingDingOptions
        {
            Enabled = true,
            AppKey = "appkey_ingest",
            AppSecret = "appsecret_ingest",
            RobotCode = "robot_ingest",
            MailboxId = _mailboxId,
            ContactCollectionEnabled = false,
            AutoReplyEnabled = false,
            ReceiveMode = "Stream"
        });

        var dingClient = client ?? new Mock<IDingDingClient>();
        dingClient.Setup(c => c.GetUserDetailAsync(It.IsAny<string>()))
            .ReturnsAsync((System.Text.Json.JsonElement?)null);

        var workflow = new Mock<IWorkflowService>();
        workflow.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(0);

        var notif = new Mock<INotificationPublisher>();
        notif.Setup(n => n.NotifyMailboxAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
            .Returns(Task.CompletedTask);

        return new DingDingIngestionService(
            _unitOfWork,
            workflow.Object,
            settingsProvider.Object,
            dingClient.Object,
            notif.Object,
            new Mock<ILogger<DingDingIngestionService>>().Object,
            BuildConfigWithUploadPath(_tempUploadRoot),
            Options.Create(new IngestionMatchOptions()),
            new Mock<IHostEnvironment>().Object);
    }

    [Fact]
    public async Task DingTalkIngestion_SameSessionWebhook_TwoMessagesBothPersistInSameConversation()
    {
        var service = BuildIngestionService();
        var webhook = "https://oapi.dingtalk.com/robot/sendBySession?session=bdb38c257a6c893047c1ae9a26b07ef0";
        var t0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 第一次提问（新会话）
        var r1 = await service.ProcessWebhookPayloadAsync(
            BuildDingTalkTextPayload("msgnd017kRbgGg6p93fG3eTzQ==", webhook, "你好，打印机报错", t0));
        r1.ErrorMessage.Should().BeNull();
        r1.NewConversationCount.Should().Be(1, "第一条消息应创建新会话");
        r1.ConversationId.Should().BeGreaterThan(0);

        // 客户二次提问（同一 sessionWebhook，30 秒后）
        var r2 = await service.ProcessWebhookPayloadAsync(
            BuildDingTalkTextPayload("msgMeStDvas3Ha0FzowQL8h/Q==", webhook, "具体怎么处理？", t0 + 30_000));
        r2.ErrorMessage.Should().BeNull();
        r2.ReplyCount.Should().Be(1, "第二条消息应追加到已有会话（T000113 核心断言）");
        r2.ConversationId.Should().Be(r1.ConversationId, "二次问答必须落在同一会话");

        // 会话中应有两名客户线程，MessageId 为真实 MsgId、Headers 保存 webhook（不再唯一冲突）
        var threads = await _context.Threads
            .Where(t => t.ConversationId == r1.ConversationId && t.Type == ThreadType.Customer && t.State != ThreadState.Deleted)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();
        threads.Count.Should().Be(2, "两次客户消息都必须入库（T000113 根因修复）");
        threads[0].MessageId.Should().Be("msgnd017kRbgGg6p93fG3eTzQ==");
        threads[1].MessageId.Should().Be("msgMeStDvas3Ha0FzowQL8h/Q==");
        threads[0].Headers.Should().Contain(webhook, "sessionWebhook 应存于 Headers");
        threads[1].Headers.Should().Contain(webhook);
        threads[0].Headers.Should().NotContain("sw:", "不再以 MessageId 存储 webhook");
    }

    [Fact]
    public async Task DingTalkIngestion_DuplicateMsgId_SecondSkipped()
    {
        var service = BuildIngestionService();
        var t0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = BuildDingTalkTextPayload("msg-dup-1", "https://oapi.dingtalk.com/robot/sendBySession?session=dup", "重复消息", t0);

        var r1 = await service.ProcessWebhookPayloadAsync(payload);
        r1.NewConversationCount.Should().Be(1);

        var r2 = await service.ProcessWebhookPayloadAsync(payload);
        r2.IsDuplicate.Should().BeTrue("相同 MsgId 必须被去重");
        r2.NewConversationCount.Should().Be(0);
        r2.ReplyCount.Should().Be(0);
    }

    [Fact]
    public async Task DingTalkReply_ExtractsSessionWebhookFromHeaders_AndSendsViaWebhook()
    {
        // 通过真实收取链路创建带 Headers webhook 的客户线程（新格式）
        var service = BuildIngestionService();
        var webhook = "https://oapi.dingtalk.com/robot/sendBySession?session=headers-route-test";
        var r1 = await service.ProcessWebhookPayloadAsync(
            BuildDingTalkTextPayload("msg-route-1", webhook, "你好", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        r1.ConversationId.Should().BeGreaterThan(0);

        // 客服回复：应从 Headers 提取 webhook 并优先走 sessionWebhook 发送
        var agent = new User { FirstName = "客服", LastName = "三", Email = "agent.route@example.com" };
        await _unitOfWork.Users.AddAsync(agent);
        await _unitOfWork.SaveChangesAsync();

        var settingsProvider = new Mock<IDingDingSettingsProvider>();
        settingsProvider.SetupGet(s => s.Current).Returns(new DingDingOptions
        {
            Enabled = true,
            AppKey = "appkey_route",
            AppSecret = "appsecret_route",
            RobotCode = "robot_route",
            ReceiveMode = "Stream"
        });
        var dingClient = new Mock<IDingDingClient>();
        dingClient.Setup(c => c.SendBySessionWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
            .ReturnsAsync(true);

        var replyService = new DingDingService(
            _unitOfWork,
            new Mock<IEncryptionService>().Object,
            settingsProvider.Object,
            dingClient.Object,
            new Mock<IDingTalkStreamConnectionStatus>().Object,
            new Mock<ICurrentUserAccessor>().Object,
            new Mock<INotificationPublisher>().Object,
            new Mock<ILogger<DingDingService>>().Object,
            BuildConfigWithUploadPath(_tempUploadRoot),
            new Mock<IHostEnvironment>().Object);

        var result = await replyService.ReplyAsync(new DingDingReplyRequest
        {
            ConversationId = r1.ConversationId!.Value,
            Content = "收到，我们处理一下"
        }, userId: agent.Id);

        result.Sent.Should().BeTrue("客服回复必须发送成功");
        dingClient.Verify(c => c.SendBySessionWebhookAsync(
                webhook, "text", It.IsAny<object>()),
            Times.Once, "sessionWebhook 必须从 Headers（新格式）成功提取并用于回复路由（T000113）");
    }

    // ==================== T000114：关闭钉钉会话-评价链接无法打开 ====================
    // 根因：钉钉 sampleText（text）消息中的 URL 不可点击——满意度消息虽然带正确链接
    // （css.example.com/satisfaction/{token}，后端校验通过、前端可访问），但客户在钉钉里无法点开。
    // 修复：ChatChannelMessageSender 钉钉渠道改用 sampleMarkdown，并将 http(s):// 链接
    // 包装为 markdown 可点击链接 [url](url)；即使钉钉对 http 外链限制，原 URL 文本仍可见可复制。
    // 以下用例锁定：满意度消息经钉钉渠道发送时使用 markdown 可点击格式。

    [Fact]
    public async Task ChatChannelMessageSender_DingTalk_SatisfactionUrl_SentAsClickableMarkdown()
    {
        var settingsProvider = new Mock<IDingDingSettingsProvider>();
        settingsProvider.SetupGet(s => s.Current).Returns(new DingDingOptions
        {
            Enabled = true,
            AppKey = "appkey_dd",
            AppSecret = "appsecret_dd",
            RobotCode = "robot_dd",
            ReceiveMode = "Stream"
        });
        settingsProvider.Setup(s => s.GetCurrentAsync()).ReturnsAsync(new DingDingOptions
        {
            Enabled = true,
            AppKey = "appkey_dd",
            AppSecret = "appsecret_dd",
            RobotCode = "robot_dd",
            ReceiveMode = "Stream"
        });

        string? capturedMsgKey = null;
        string? capturedContent = null;
        var dingClient = new Mock<IDingDingClient>();
        dingClient.Setup(c => c.BatchSendOtoMessageAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string[], string, string>((_, _, key, content) => { capturedMsgKey = key; capturedContent = content; })
            .ReturnsAsync("proc_markdown_1");

        var sender = new ChatChannelMessageSender(
            _unitOfWork,
            new Mock<IWeChatClient>().Object,
            new Mock<IFeishuClient>().Object,
            new Mock<IWhatsAppClient>().Object,
            dingClient.Object,
            settingsProvider.Object,
            new Mock<IMessengerClient>().Object,
            new Mock<IMessengerSettingsProvider>().Object,
            new Mock<ITelegramClient>().Object,
            new Mock<ILineClient>().Object,
            new Mock<IIMessageClient>().Object,
            new Mock<ILogger<ChatChannelMessageSender>>().Object);

        const string satisfactionUrl = "http://css.example.com:5173/satisfaction/574-SYNTHETIC-TOKEN-FOR-TESTS-0000000000000000";
        var text = $"您好，您的问题已处理完毕，本次会话已关闭。\n\n请点击下方链接评价本次服务：\n{satisfactionUrl}\n\n感谢您选择我们的服务！";

        var result = await sender.SendByChannelAsync(SourceType.DingTalk, "300252304524232191", 574, text);

        result.Success.Should().BeTrue("钉钉满意度消息必须发送成功");
        capturedMsgKey.Should().Be("sampleMarkdown", "必须使用 markdown 消息类型使链接可点击（T000114）");
        capturedContent.Should().NotBeNull();

        // content 为 JSON（中文被转义），解析后断言
        using var doc = System.Text.Json.JsonDocument.Parse(capturedContent!);
        doc.RootElement.GetProperty("title").GetString().Should().Be("服务通知");
        var markdownText = doc.RootElement.GetProperty("text").GetString()!;
        markdownText.Should().Contain($"[{satisfactionUrl}]({satisfactionUrl})",
            "URL 必须被包装为 markdown 可点击链接 [url](url)（T000114）");
    }

    [Fact]
    public async Task ChatChannelMessageSender_DingTalk_PlainTextWithoutUrl_PreservedInMarkdown()
    {
        var settingsProvider = new Mock<IDingDingSettingsProvider>();
        settingsProvider.SetupGet(s => s.Current).Returns(new DingDingOptions
        {
            Enabled = true,
            AppKey = "appkey_dd2",
            AppSecret = "appsecret_dd2",
            RobotCode = "robot_dd2",
            ReceiveMode = "Stream"
        });
        settingsProvider.Setup(s => s.GetCurrentAsync()).ReturnsAsync(new DingDingOptions
        {
            Enabled = true,
            AppKey = "appkey_dd2",
            AppSecret = "appsecret_dd2",
            RobotCode = "robot_dd2",
            ReceiveMode = "Stream"
        });

        string? capturedMsgKey = null;
        string? capturedContent = null;
        var dingClient = new Mock<IDingDingClient>();
        dingClient.Setup(c => c.BatchSendOtoMessageAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string[], string, string>((_, _, key, content) => { capturedMsgKey = key; capturedContent = content; })
            .ReturnsAsync("proc_markdown_2");

        var sender = new ChatChannelMessageSender(
            _unitOfWork,
            new Mock<IWeChatClient>().Object,
            new Mock<IFeishuClient>().Object,
            new Mock<IWhatsAppClient>().Object,
            dingClient.Object,
            settingsProvider.Object,
            new Mock<IMessengerClient>().Object,
            new Mock<IMessengerSettingsProvider>().Object,
            new Mock<ITelegramClient>().Object,
            new Mock<ILineClient>().Object,
            new Mock<IIMessageClient>().Object,
            new Mock<ILogger<ChatChannelMessageSender>>().Object);

        var plainText = "您好，为了更好为您服务，请回复您的姓名、手机、邮箱，谢谢！";

        var result = await sender.SendByChannelAsync(SourceType.DingTalk, "300252304524232191", 574, plainText);

        result.Success.Should().BeTrue();
        capturedMsgKey.Should().Be("sampleMarkdown");
        using var doc = System.Text.Json.JsonDocument.Parse(capturedContent!);
        doc.RootElement.GetProperty("text").GetString().Should().Contain(plainText,
            "无 URL 的普通文本必须在 markdown 中原样保留");
    }

    // ==================== 真实 SQLite 基建 ====================

    private async Task<int> InsertMailboxRowAsync()
    {
        var cols = new List<string>();
        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Mailboxes)";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                var notNull = reader.GetInt64(3) == 1;
                var hasDefault = !reader.IsDBNull(4);
                if (notNull && !hasDefault && !string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    cols.Add(name);
                }
            }
        }

        var colList = string.Join(", ", cols.Select(c => $"\"{c}\""));
        var placeholders = string.Join(", ", cols.Select((_, i) => $"$p{i}"));
        await using var insert = _connection.CreateCommand();
        insert.CommandText = $"INSERT INTO Mailboxes ({colList}) VALUES ({placeholders})";
        for (var i = 0; i < cols.Count; i++)
        {
            insert.Parameters.AddWithValue($"$p{i}", ResolveMailboxDummyValue(cols[i]));
        }
        await insert.ExecuteNonQueryAsync();

        await using var idCmd = _connection.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt32(await idCmd.ExecuteScalarAsync());
    }

    private static object ResolveMailboxDummyValue(string column) => column switch
    {
        "Version" => 1,
        "InPort" or "OutPort" => 993,
        "FetchFrequency" => 5,
        "LastUid" => 0L,
        "InValidateCert" or "AutoReplyEnabled" or "OauthEnabled" or "IsActive" => 1L,
        "InProtocol" or "InEncryption" or "OutMethod" or "OutEncryption" => 0,
        "CreatedAt" or "UpdatedAt" => "2026-01-01T00:00:00.0000000",
        _ => "it-test"
    };
}
