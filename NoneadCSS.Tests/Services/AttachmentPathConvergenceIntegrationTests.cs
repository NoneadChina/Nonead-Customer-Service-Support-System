using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Infrastructure.Services;
using System.Text;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// T000108（微信会话附件）集成测试。
///
/// 缺陷根因：附件路径的 CWD/ContentRoot 分裂 —— 上传/下载/邮件服务以 ContentRoot
/// （Production\backend）锚定相对配置 "Data/Attachments"，而微信/飞书/钉钉/WhatsApp
/// 收发服务以进程 CWD（Production）锚定同一配置，导致两个目录各存各的文件：
///   客服发微信附件 → 在上传目录找不到 → "附件文件不存在"；
///   客户发来的微信附件 → 下载时在收取目录找不到 → "无法下载"。
/// 修复：生产配置 Attachment:UploadPath 改为绝对路径（Path.Combine 绝对路径优先，
/// 所有服务收敛到同一基目录）。
///
/// 本测试锁定三条回归防线：
/// 1) 生产配置文件必须为绝对路径（防止改回相对配置）；
/// 2) 绝对路径下，ContentRoot 锚定消费方与原始 Combine 消费方解析到同一文件；
/// 3) 真实上传 → 微信发送端读取路径存在 → 下载端点返回同一字节（两端症状闭环）。
/// </summary>
public class AttachmentPathConvergenceIntegrationTests : IAsyncLifetime
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "NoneadCssTests", "ContentRoot", Guid.NewGuid().ToString("N"));
    private readonly string _canonicalBase = Path.Combine(Path.GetTempPath(), "NoneadCssTests", "Attachments", Guid.NewGuid().ToString("N"));

    private SqliteConnection _connection = null!;
    private NoneadCSSDbContext _context = null!;
    private UnitOfWork _unitOfWork = null!;
    private AttachmentService _service = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_contentRoot);
        Directory.CreateDirectory(_canonicalBase);

        // 真实 SQLite（内存连接保持打开）+ EnsureCreated，同 WorkflowRecipientIntegrationTests
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseSqlite(_connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;
        _context = new NoneadCSSDbContext(options);
        await _context.Database.EnsureCreatedAsync();
        _unitOfWork = new UnitOfWork(_context, NoneadCSS.Tests.Shared.TestServiceProviderBuilder.Build(_context));

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(e => e.ContentRootPath).Returns(_contentRoot);

        var virusMock = new Mock<IVirusScanService>();
        virusMock.Setup(v => v.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new VirusScanResult { IsClean = true });

        // 模拟生产修复后的配置：UploadPath 为绝对路径（T000108）。
        // AttachmentService._basePath = Path.Combine(ContentRoot, 绝对路径) = 绝对路径（绝对优先）。
        _service = new AttachmentService(
            _unitOfWork,
            Options.Create(new AttachmentSettings { UploadPath = _canonicalBase }),
            new Mock<ILogger<AttachmentService>>().Object,
            envMock.Object,
            virusMock.Object);
    }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        TryDeleteDir(_contentRoot);
        TryDeleteDir(_canonicalBase);
        return Task.CompletedTask;
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { /* 测试清理，忽略 */ }
    }

    // ==================== 1) 生产配置回归防线 ====================

    [Fact]
    public void ProductionConfig_AttachmentUploadPath_MustBeAbsolute()
    {
        // 加载真实生产配置（部署时同步到 Production\backend 的那份）
        var apiProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NoneadCSS.Api"));
        var cfg = new ConfigurationBuilder()
            .SetBasePath(apiProject)
            .AddJsonFile("appsettings.Production.json")
            .Build();
        var uploadPath = cfg["Attachment:UploadPath"];

        uploadPath.Should().NotBeNullOrWhiteSpace("生产配置必须显式设置 Attachment:UploadPath");
        Path.IsPathRooted(uploadPath!).Should().BeTrue(
            "T000108 修复要求生产配置为绝对路径，否则微信/飞书等 IM 服务按 CWD 解析会与上传/下载的 ContentRoot 锚定目录分裂");
        uploadPath!.Replace('\\', '/').Should().EndWith("Production/backend/Data/Attachments",
            "绝对基目录应与上传/下载服务锚定的目录一致（当前为后端 ContentRoot 下的 Data/Attachments）");
    }

    // ==================== 2) 绝对路径收敛性 ====================

    [Theory]
    [InlineData(null)]
    [InlineData("2")]
    [InlineData("2026/09/01")]
    public void AbsoluteConfig_RawCombineAndContentRootCombine_ResolveToSameFile(string? fileDir)
    {
        const string fileName = "stored.txt";

        // 原始 Combine 消费方（WeChatService / FeishuService 等：Path.Combine(配置, FileDir, StoredFileName)）
        var raw = Path.GetFullPath(Path.Combine(_canonicalBase, fileDir ?? string.Empty, fileName));
        // ContentRoot 锚定消费方（AttachmentService / EmailService：Path.Combine(ContentRoot, 配置, ...)）
        var contentRootAnchored = Path.GetFullPath(Path.Combine(_contentRoot, _canonicalBase, fileDir ?? string.Empty, fileName));

        raw.Should().Be(contentRootAnchored, "绝对配置下两类消费方必须解析到同一文件，无论进程 CWD 是什么");
        Path.IsPathRooted(raw).Should().BeTrue();
    }

    [Fact]
    public void RelativeDefaultConfig_IsCwdDependent_AndDivergesFromContentRootAnchored()
    {
        // 默认相对配置（appsettings.json 的 "Data/Attachments"）：原始 Combine 结果非根路径，
        // 最终落点由进程 CWD 决定 —— 生产 CWD=Production ≠ ContentRoot=Production\backend 即分裂（T000108 根因）。
        var relative = "Data/Attachments";
        var raw = Path.Combine(relative, "2", "x.txt");

        Path.IsPathRooted(raw).Should().BeFalse("相对配置的 Combine 结果不是根路径，位置依赖 CWD，无法保证收敛");
        Path.GetFullPath(raw).Should().NotBe(
            Path.GetFullPath(Path.Combine(_contentRoot, relative, "2", "x.txt")),
            "当 CWD ≠ ContentRoot 时，相对配置必然产生两个不同目录（本测试的 ContentRoot 为独立临时目录）");
    }

    // ==================== 3) 真实上传 → 微信发送端读取 → 下载 端到端闭环 ====================

    [Fact]
    public async Task UploadedAttachment_WeChatSendPathExists_AndDownloadReturnsSameBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("T000108 attachment content");
        using var ms = new MemoryStream(bytes);
        var file = new FormFile(ms, 0, bytes.Length, "file", "report.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        var response = await _service.UploadAsync(file);

        var record = await _unitOfWork.Attachments.GetByIdAsync(response.Id);
        record.Should().NotBeNull();
        record!.StoredFileName.Should().NotBeNullOrWhiteSpace();
        record.FileDir.Should().NotBeNullOrWhiteSpace();

        // 微信发送端解析路径（WeChatService 同款：原始配置 + FileDir + StoredFileName + 遍历防护）
        var cleanFileDir = (record.FileDir ?? string.Empty).Trim().Replace('\\', '/');
        var cleanStoredName = Path.GetFileName(record.StoredFileName ?? record.FileName) ?? string.Empty;
        var weChatPath = Path.GetFullPath(Path.Combine(_canonicalBase, cleanFileDir, cleanStoredName));
        var normalizedBase = Path.GetFullPath(_canonicalBase);

        weChatPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase).Should().BeTrue("路径遍历防护应放行本次合法路径");
        File.Exists(weChatPath).Should().BeTrue("客服回复微信会话读取刚上传的附件不应再报'附件文件不存在'（T000108 症状 1）");

        // 下载端点读取同一文件（客户侧附件下载路径）。isAdmin 仅用于绕过访问控制（本测试聚焦路径解析）
        var downloaded = await _service.DownloadAsync(response.Id, isAdmin: true);
        downloaded.Content.Should().Equal(bytes, "下载端点应返回与上传完全一致的字节（客户发送的附件可下载，T000108 症状 2）");
    }
}
