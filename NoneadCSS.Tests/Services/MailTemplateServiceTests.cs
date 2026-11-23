using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Application.Templates;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// MailTemplateService 回归测试
/// 覆盖邮件模板的 CRUD、预览、渲染、内置变量、回收站与静态渲染
/// </summary>
public class MailTemplateServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IMailTemplateRepository> _mockRepo;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUserAccessor;
    private readonly ITemplateRenderer _renderer;
    private readonly MailTemplateService _service;

    public MailTemplateServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockRepo = new Mock<IMailTemplateRepository>();
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();

        _mockUnitOfWork.Setup(u => u.MailTemplates).Returns(_mockRepo.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(It.IsAny<int?>())).ReturnsAsync(true);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        _renderer = new TemplateRenderer();

        var brand = Options.Create(new BrandOptions { FrontendUrl = "https://css.example.com" });
        var config = new Mock<IConfiguration>();
        _service = new MailTemplateService(_mockUnitOfWork.Object, _mockCurrentUserAccessor.Object, _renderer, brand, config.Object);
    }

    /// <summary>
    /// 创建支持异步 LINQ 操作的 Mock MailTemplate DbSet
    /// </summary>
    private void SetupQueryable(params MailTemplate[] items)
    {
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(items.ToList());
        _mockRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(mockDbSet.Object);
    }

    #region GetPagedAsync

    [Fact]
    public async Task GetPagedAsync_ShouldReturnMappedItemsAndTotal()
    {
        // Arrange
        var templates = new List<MailTemplate>
        {
            new() { Id = 1, Name = "模板A", Slug = "a", Type = MailTemplateType.Custom, Subject = "S1", BodyHtml = "<p>h1</p>" },
            new() { Id = 2, Name = "模板B", Slug = "b", Type = MailTemplateType.Welcome, Subject = "S2", BodyHtml = "<p>h2</p>" }
        };
        _mockRepo.Setup(r => r.GetPagedAsync(1, 20, null, null, null, null))
            .ReturnsAsync((templates, 2));

        var query = new MailTemplateQuery { Page = 1, PageSize = 20 };

        // Act
        var (items, total) = await _service.GetPagedAsync(query);

        // Assert
        total.Should().Be(2);
        items.Should().HaveCount(2);
        items.ElementAt(0).Name.Should().Be("模板A");
        items.ElementAt(0).TypeName.Should().Be("自定义模板");
        items.ElementAt(1).TypeName.Should().Be("欢迎邮件");
    }

    [Fact]
    public async Task GetPagedAsync_WithFilters_ShouldPassFiltersToRepository()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetPagedAsync(2, 10, MailTemplateType.Custom, 5, true, "key"))
            .ReturnsAsync((new List<MailTemplate>(), 0));

        var query = new MailTemplateQuery
        {
            Page = 2,
            PageSize = 10,
            Type = MailTemplateType.Custom,
            MailboxId = 5,
            IsActive = true,
            Keyword = "key"
        };

        // Act
        var (items, total) = await _service.GetPagedAsync(query);

        // Assert
        total.Should().Be(0);
        items.Should().BeEmpty();
        _mockRepo.Verify(r => r.GetPagedAsync(2, 10, MailTemplateType.Custom, 5, true, "key"), Times.Once);
    }

    [Fact]
    public async Task GetPagedAsync_WithEmptyResult_ShouldReturnEmptyList()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetPagedAsync(1, 20, null, null, null, null))
            .ReturnsAsync((new List<MailTemplate>(), 0));

        var query = new MailTemplateQuery { Page = 1, PageSize = 20 };

        // Act
        var (items, total) = await _service.GetPagedAsync(query);

        // Assert
        total.Should().Be(0);
        items.Should().BeEmpty();
    }

    #endregion

    #region GetByIdAsync

    [Fact]
    public async Task GetByIdAsync_WithExistingAndAuthorized_ShouldReturnDto()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Name = "模板A", Slug = "a", Type = MailTemplateType.Custom, Subject = "S1", BodyHtml = "<p>h</p>" };
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(template);

        // Act
        var result = await _service.GetByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("模板A");
        _mockCurrentUserAccessor.Verify(x => x.CanAccessMailboxAsync(template.MailboxId), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_WithDeleted_ShouldReturnNull()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Name = "模板A", Slug = "a", DeletedAt = DateTime.UtcNow };
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(template);

        // Act
        var result = await _service.GetByIdAsync(1);

        // Assert
        result.Should().BeNull();
        _mockCurrentUserAccessor.Verify(x => x.CanAccessMailboxAsync(It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((MailTemplate?)null);

        // Act
        var result = await _service.GetByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithNoAccess_ShouldReturnNull()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Name = "模板A", Slug = "a", MailboxId = 3 };
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(template);
        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(3)).ReturnsAsync(false);

        // Act
        var result = await _service.GetByIdAsync(1);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetBySlugAsync

    [Fact]
    public async Task GetBySlugAsync_WithExisting_ShouldReturnDto()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Name = "模板A", Slug = "custom", Type = MailTemplateType.Custom };
        _mockRepo.Setup(r => r.GetBySlugAsync("custom")).ReturnsAsync(template);

        // Act
        var result = await _service.GetBySlugAsync("custom");

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Slug.Should().Be("custom");
    }

    [Fact]
    public async Task GetBySlugAsync_WithDeleted_ShouldReturnNull()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Slug = "custom", DeletedAt = DateTime.UtcNow };
        _mockRepo.Setup(r => r.GetBySlugAsync("custom")).ReturnsAsync(template);

        // Act
        var result = await _service.GetBySlugAsync("custom");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetBySlugAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetBySlugAsync("missing")).ReturnsAsync((MailTemplate?)null);

        // Act
        var result = await _service.GetBySlugAsync("missing");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_WithValidRequest_ShouldCreateAndReturnDto()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetBySlugAsync("welcome")).ReturnsAsync((MailTemplate?)null);
        MailTemplate? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<MailTemplate>()))
            .Callback<MailTemplate>(e => { captured = e; e.Id = 1; })
            .ReturnsAsync((MailTemplate e) => e);

        var request = new CreateMailTemplateRequest
        {
            Name = "欢迎邮件",
            Slug = "welcome",
            Type = MailTemplateType.Welcome,
            Subject = "欢迎 {{user_name}}",
            BodyHtml = "<p>你好 {{user_name}}</p>",
            BodyText = "你好",
            VariablesJson = "[{\"Key\":\"user_name\",\"Label\":\"用户名\"}]",
            IsActive = true,
            MailboxId = null,
            Description = "注册欢迎",
            SortOrder = 1
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("欢迎邮件");
        result.Slug.Should().Be("welcome");
        result.IsSystem.Should().BeFalse();
        result.Variables.Should().HaveCount(1);
        result.Variables[0].Key.Should().Be("user_name");

        captured.Should().NotBeNull();
        captured!.CreatedAt.Should().NotBe(default);
        captured.UpdatedAt.Should().NotBe(default);

        _mockRepo.Verify(r => r.GetBySlugAsync("welcome"), Times.Once);
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<MailTemplate>()), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateSlug_ShouldThrow()
    {
        // Arrange
        var existing = new MailTemplate { Id = 1, Slug = "welcome", IsSystem = true };
        _mockRepo.Setup(r => r.GetBySlugAsync("welcome")).ReturnsAsync(existing);

        var request = new CreateMailTemplateRequest { Slug = "welcome" };

        // Act
        Func<Task> act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("模板标识已存在");
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<MailTemplate>()), Times.Never);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldSanitizeBodyHtml()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetBySlugAsync("x")).ReturnsAsync((MailTemplate?)null);
        MailTemplate? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<MailTemplate>()))
            .Callback<MailTemplate>(e => { captured = e; })
            .ReturnsAsync((MailTemplate e) => e);

        var request = new CreateMailTemplateRequest
        {
            Slug = "x",
            Name = "X",
            BodyHtml = "<script>alert(1)</script><p>安全内容</p>"
        };

        // Act
        await _service.CreateAsync(request);

        // Assert
        captured!.BodyHtml.Should().NotContain("alert");
        captured.BodyHtml.Should().Contain("<p>");
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_WithExisting_ShouldUpdateAndReturnDto()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Name = "旧", Slug = "old", Type = MailTemplateType.Custom, IsSystem = false, MailboxId = 5 };
        SetupQueryable(template);
        _mockRepo.Setup(r => r.GetBySlugAsync("new")).ReturnsAsync((MailTemplate?)null);

        var request = new UpdateMailTemplateRequest
        {
            Name = "新",
            Slug = "new",
            Type = MailTemplateType.Custom,
            Subject = "S",
            BodyHtml = "<p>h</p>",
            BodyText = "t",
            IsActive = true,
            MailboxId = 6,
            SortOrder = 2
        };

        // Act
        var result = await _service.UpdateAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("新");
        result.Slug.Should().Be("new");
        template.MailboxId.Should().Be(6);
        template.UpdatedAt.Should().NotBe(default);

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        SetupQueryable();
        var request = new UpdateMailTemplateRequest { Slug = "new" };

        // Act
        var result = await _service.UpdateAsync(999, request);

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WithNoAccess_ShouldReturnNull()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Slug = "old", IsSystem = false, MailboxId = 3 };
        SetupQueryable(template);
        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(3)).ReturnsAsync(false);

        var request = new UpdateMailTemplateRequest { Slug = "new" };

        // Act
        var result = await _service.UpdateAsync(1, request);

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WhenSystemTemplateAndSlugChanged_ShouldThrow()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Slug = "sys", IsSystem = true, MailboxId = null };
        SetupQueryable(template);

        var request = new UpdateMailTemplateRequest { Slug = "changed" };

        // Act
        Func<Task> act = () => _service.UpdateAsync(1, request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("系统模板的标识不可修改");
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WhenDuplicateSlug_ShouldThrow()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Slug = "old", IsSystem = false };
        SetupQueryable(template);
        var existing = new MailTemplate { Id = 2, Slug = "taken" };
        _mockRepo.Setup(r => r.GetBySlugAsync("taken")).ReturnsAsync(existing);

        var request = new UpdateMailTemplateRequest { Slug = "taken" };

        // Act
        Func<Task> act = () => _service.UpdateAsync(1, request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("模板标识已存在");
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_SystemTemplate_ShouldNotChangeMailboxId()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Slug = "sys", IsSystem = true, MailboxId = null };
        SetupQueryable(template);

        var request = new UpdateMailTemplateRequest
        {
            Slug = "sys",
            Name = "系统",
            Type = MailTemplateType.Custom,
            Subject = "S",
            BodyHtml = "<p>h</p>",
            BodyText = "t",
            IsActive = true,
            MailboxId = 9,
            SortOrder = 0
        };

        // Act
        var result = await _service.UpdateAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        template.MailboxId.Should().BeNull(); // 系统模板不修改 MailboxId，保持原值（null）
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_WithExisting_ShouldSoftDeleteAndReturnTrue()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Slug = "custom", IsSystem = false };
        SetupQueryable(template);

        // Act
        var result = await _service.DeleteAsync(1);

        // Assert
        result.Should().BeTrue();
        template.DeletedAt.Should().NotBeNull();
        template.UpdatedAt.Should().NotBe(default);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExisting_ShouldReturnFalse()
    {
        // Arrange
        SetupQueryable();

        // Act
        var result = await _service.DeleteAsync(999);

        // Assert
        result.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_SystemTemplate_ShouldThrow()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Slug = "sys", IsSystem = true };
        SetupQueryable(template);

        // Act
        Func<Task> act = () => _service.DeleteAsync(1);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("系统模板不可删除");
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WithNoAccess_ShouldReturnFalse()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Slug = "custom", IsSystem = false, MailboxId = 3 };
        SetupQueryable(template);
        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(3)).ReturnsAsync(false);

        // Act
        var result = await _service.DeleteAsync(1);

        // Assert
        result.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    #endregion

    #region GetOverviewAsync

    [Fact]
    public async Task GetOverviewAsync_ShouldReturnCorrectOverview()
    {
        // Arrange
        var stats = new Dictionary<string, int>
        {
            ["total"] = 10,
            ["active"] = 7,
            ["system"] = 3,
            ["custom"] = 7,
            ["type_1"] = 4,
            ["type_7"] = 6
        };
        _mockRepo.Setup(r => r.GetStatsAsync()).ReturnsAsync(stats);

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Should().NotBeNull();
        result.Total.Should().Be(10);
        result.ActiveCount.Should().Be(7);
        result.SystemCount.Should().Be(3);
        result.CustomCount.Should().Be(7);
        result.TypeCounts.Should().HaveCount(Enum.GetValues<MailTemplateType>().Length);
        result.TypeCounts.First(t => t.Type == MailTemplateType.NewConversation).TypeName.Should().Be("新会话通知");
        result.TypeCounts.First(t => t.Type == MailTemplateType.NewConversation).Count.Should().Be(4);
        result.TypeCounts.First(t => t.Type == MailTemplateType.Welcome).Count.Should().Be(6);
    }

    [Fact]
    public async Task GetOverviewAsync_WithEmptyStats_ShouldReturnZeros()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetStatsAsync()).ReturnsAsync(new Dictionary<string, int>());

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Total.Should().Be(0);
        result.ActiveCount.Should().Be(0);
        result.SystemCount.Should().Be(0);
        result.CustomCount.Should().Be(0);
        result.TypeCounts.Should().HaveCount(Enum.GetValues<MailTemplateType>().Length);
        result.TypeCounts.Should().OnlyContain(t => t.Count == 0);
    }

    #endregion

    #region PreviewAsync

    [Fact]
    public async Task PreviewAsync_ShouldRenderAllParts()
    {
        // Arrange
        var request = new PreviewMailTemplateRequest
        {
            Subject = "Hi {{name}}",
            BodyHtml = "<p>Hello {{name}}</p>",
            BodyText = "Plain {{name}}",
            Variables = new Dictionary<string, string> { ["name"] = "Tom" }
        };

        // Act
        var result = await _service.PreviewAsync(request);

        // Assert
        result.Subject.Should().Be("Hi Tom");
        result.BodyHtml.Should().Be("<p>Hello Tom</p>");
        result.BodyText.Should().Be("Plain Tom");
    }

    [Fact]
    public async Task PreviewAsync_ShouldEncodeHtmlBodyValues()
    {
        // Arrange
        var request = new PreviewMailTemplateRequest
        {
            Subject = "Hi {{name}}",
            BodyHtml = "Value: {{name}}",
            BodyText = "Value: {{name}}",
            Variables = new Dictionary<string, string> { ["name"] = "<b>Tom</b>" }
        };

        // Act
        var result = await _service.PreviewAsync(request);

        // Assert
        result.Subject.Should().Be("Hi <b>Tom</b>"); // Text 上下文不编码
        result.BodyHtml.Should().Be("Value: &lt;b&gt;Tom&lt;/b&gt;"); // HTML 上下文编码
        result.BodyText.Should().Be("Value: <b>Tom</b>"); // Text 上下文不编码
    }

    [Fact]
    public async Task PreviewAsync_WithEmptyTemplates_ShouldReturnEmpty()
    {
        // Arrange
        var request = new PreviewMailTemplateRequest
        {
            Subject = string.Empty,
            BodyHtml = string.Empty,
            BodyText = string.Empty,
            Variables = new Dictionary<string, string> { ["name"] = "Tom" }
        };

        // Act
        var result = await _service.PreviewAsync(request);

        // Assert
        result.Subject.Should().BeEmpty();
        result.BodyHtml.Should().BeEmpty();
        result.BodyText.Should().BeEmpty();
    }

    #endregion

    #region PreviewTemplateAsync

    [Fact]
    public async Task PreviewTemplateAsync_WithCustomVariables_ShouldUseCustomVariables()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Type = MailTemplateType.Custom, Subject = "Hi {{name}}", BodyHtml = "<p>{{name}}</p>", BodyText = "{{name}}" };
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(template);
        var request = new MailTemplatePreviewRequest
        {
            CustomVariables = new Dictionary<string, string> { ["name"] = "Alice" }
        };

        // Act
        var result = await _service.PreviewTemplateAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        result!.Subject.Should().Be("Hi Alice");
        result.BodyHtml.Should().Be("<p>Alice</p>");
        result.BodyText.Should().Be("Alice");
    }

    [Fact]
    public async Task PreviewTemplateAsync_WithoutCustomVariables_ShouldUseDefaultMockVariables()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Type = MailTemplateType.NewConversation, Subject = "新会话 {{conversation_number}}", BodyHtml = "<p>{{customer_name}}</p>", BodyText = "{{customer_email}}" };
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(template);
        var request = new MailTemplatePreviewRequest();

        // Act
        var result = await _service.PreviewTemplateAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        result!.Subject.Should().Be("新会话 CNV-20260725-001");
        result.BodyHtml.Should().Be("<p>张三</p>");
        result.BodyText.Should().Be("zhangsan@example.com");
    }

    [Fact]
    public async Task PreviewTemplateAsync_WithDeletedOrMissing_ShouldReturnNull()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((MailTemplate?)null);

        // Act
        var result = await _service.PreviewTemplateAsync(999, new MailTemplatePreviewRequest());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task PreviewTemplateAsync_WithoutFrontendUrl_ShouldThrow()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, Type = MailTemplateType.NewConversation, Subject = "S", BodyHtml = "H", BodyText = "T" };
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(template);

        var brand = Options.Create(new BrandOptions { FrontendUrl = "" });
        var config = new Mock<IConfiguration>();
        var service = new MailTemplateService(_mockUnitOfWork.Object, _mockCurrentUserAccessor.Object, _renderer, brand, config.Object);

        // Act
        Func<Task> act = () => service.PreviewTemplateAsync(1, new MailTemplatePreviewRequest());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*FrontendUrl 未设置*");
    }

    #endregion

    #region RenderTemplateAsync

    [Fact]
    public async Task RenderTemplateAsync_WithExisting_ShouldReturnRenderedHtml()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, BodyHtml = "<p>Hello {{name}}</p>" };
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(template);
        var variables = new Dictionary<string, string> { ["name"] = "Tom" };

        // Act
        var result = await _service.RenderTemplateAsync(1, variables);

        // Assert
        result.Should().Be("<p>Hello Tom</p>");
    }

    [Fact]
    public async Task RenderTemplateAsync_WithMissing_ShouldThrow()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((MailTemplate?)null);

        // Act
        Func<Task> act = () => _service.RenderTemplateAsync(999, new Dictionary<string, string>());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("模板不存在");
    }

    [Fact]
    public async Task RenderTemplateAsync_WithDeleted_ShouldThrow()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, BodyHtml = "H", DeletedAt = DateTime.UtcNow };
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(template);

        // Act
        Func<Task> act = () => _service.RenderTemplateAsync(1, new Dictionary<string, string>());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("模板不存在");
    }

    [Fact]
    public async Task RenderTemplateAsync_WithRawHtmlKeys_ShouldNotEncodeThoseKeys()
    {
        // Arrange
        var template = new MailTemplate { Id = 1, BodyHtml = "{{html}} and {{text}}" };
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(template);
        var variables = new Dictionary<string, string> { ["html"] = "<b>B</b>", ["text"] = "<i>I</i>" };
        var rawHtmlKeys = new HashSet<string> { "html" };

        // Act
        var result = await _service.RenderTemplateAsync(1, variables, rawHtmlKeys);

        // Assert
        result.Should().Be("<b>B</b> and &lt;i&gt;I&lt;/i&gt;");
    }

    #endregion

    #region RenderTemplateFullAsync

    [Fact]
    public async Task RenderTemplateFullAsync_ShouldReturnRenderedResponse()
    {
        // Arrange
        var template = new MailTemplate
        {
            Id = 1,
            Subject = "Hi {{name}}",
            BodyHtml = "<p>{{name}}</p>",
            BodyText = "{{name}}"
        };
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(template);
        var variables = new Dictionary<string, string> { ["name"] = "Tom" };

        // Act
        var result = await _service.RenderTemplateFullAsync(1, variables);

        // Assert
        result.Should().NotBeNull();
        result.Subject.Should().Be("Hi Tom");
        result.BodyHtml.Should().Be("<p>Tom</p>");
        result.BodyText.Should().Be("Tom");
    }

    [Fact]
    public async Task RenderTemplateFullAsync_WithMissing_ShouldThrow()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((MailTemplate?)null);

        // Act
        Func<Task> act = () => _service.RenderTemplateFullAsync(999, new Dictionary<string, string>());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("模板不存在");
    }

    #endregion

    #region GetBuiltInVariablesAsync

    [Fact]
    public async Task GetBuiltInVariablesAsync_ShouldReturnVariables()
    {
        // Act
        var result = await _service.GetBuiltInVariablesAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        result.Should().OnlyContain(v => !string.IsNullOrEmpty(v.Key));
    }

    #endregion

    #region GetTrashAsync / GetTrashCountAsync

    [Fact]
    public async Task GetTrashAsync_ShouldReturnDeletedItems()
    {
        // Arrange
        var deleted = new List<MailTemplate>
        {
            new() { Id = 1, Name = "已删模板", Slug = "d1", Type = MailTemplateType.Custom, DeletedAt = DateTime.UtcNow }
        };
        _mockRepo.Setup(r => r.GetDeletedAsync(1, 20, null)).ReturnsAsync((deleted, 1));

        // Act
        var (items, total) = await _service.GetTrashAsync(1, 20);

        // Assert
        total.Should().Be(1);
        items.Should().HaveCount(1);
        items.ElementAt(0).Name.Should().Be("已删模板");
    }

    [Fact]
    public async Task GetTrashAsync_WithKeyword_ShouldPassKeyword()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetDeletedAsync(1, 20, "kw")).ReturnsAsync((new List<MailTemplate>(), 0));

        // Act
        var (items, total) = await _service.GetTrashAsync(1, 20, "kw");

        // Assert
        total.Should().Be(0);
        _mockRepo.Verify(r => r.GetDeletedAsync(1, 20, "kw"), Times.Once);
    }

    [Fact]
    public async Task GetTrashCountAsync_ShouldReturnCount()
    {
        // Arrange
        _mockRepo.Setup(r => r.CountDeletedAsync()).ReturnsAsync(5);

        // Act
        var result = await _service.GetTrashCountAsync();

        // Assert
        result.Should().Be(5);
    }

    #endregion

    #region RestoreAsync

    [Fact]
    public async Task RestoreAsync_ShouldCallRepository()
    {
        // Arrange
        _mockRepo.Setup(r => r.RestoreAsync(1)).ReturnsAsync(true);

        // Act
        var result = await _service.RestoreAsync(1);

        // Assert
        result.Should().BeTrue();
        _mockRepo.Verify(r => r.RestoreAsync(1), Times.Once);
    }

    [Fact]
    public async Task RestoreAsync_WhenFailed_ShouldReturnFalse()
    {
        // Arrange
        _mockRepo.Setup(r => r.RestoreAsync(999)).ReturnsAsync(false);

        // Act
        var result = await _service.RestoreAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region HardDeleteAsync

    [Fact]
    public async Task HardDeleteAsync_AsSuperAdmin_ShouldDelete()
    {
        // Arrange
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);
        _mockRepo.Setup(r => r.HardDeleteAsync(1)).ReturnsAsync(true);

        // Act
        var result = await _service.HardDeleteAsync(1);

        // Assert
        result.Should().BeTrue();
        _mockRepo.Verify(r => r.HardDeleteAsync(1), Times.Once);
    }

    [Fact]
    public async Task HardDeleteAsync_AsNonSuperAdmin_ShouldReturnFalse()
    {
        // Arrange
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        // Act
        var result = await _service.HardDeleteAsync(1);

        // Assert
        result.Should().BeFalse();
        _mockRepo.Verify(r => r.HardDeleteAsync(It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region ClearTrashAsync

    [Fact]
    public async Task ClearTrashAsync_ShouldReturnDeletedCount()
    {
        // Arrange
        _mockRepo.Setup(r => r.HardDeleteAllDeletedAsync()).ReturnsAsync(8);

        // Act
        var result = await _service.ClearTrashAsync();

        // Assert
        result.Should().Be(8);
        _mockRepo.Verify(r => r.HardDeleteAllDeletedAsync(), Times.Once);
    }

    [Fact]
    public async Task ClearTrashAsync_WithNoDeleted_ShouldReturnZero()
    {
        // Arrange
        _mockRepo.Setup(r => r.HardDeleteAllDeletedAsync()).ReturnsAsync(0);

        // Act
        var result = await _service.ClearTrashAsync();

        // Assert
        result.Should().Be(0);
    }

    #endregion

    #region Static Rendering

    [Fact]
    public void RenderVariablesStatic_ShouldRenderWithTextEncoding()
    {
        // Act
        var result = MailTemplateService.RenderVariablesStatic("Hello {{name}}", new Dictionary<string, string> { ["name"] = "Tom" });

        // Assert
        result.Should().Be("Hello Tom");
    }

    [Fact]
    public void RenderVariablesHtmlStatic_ShouldRenderWithHtmlEncoding()
    {
        // Act
        var result = MailTemplateService.RenderVariablesHtmlStatic("Value: {{name}}", new Dictionary<string, string> { ["name"] = "<b>Tom</b>" });

        // Assert
        result.Should().Be("Value: &lt;b&gt;Tom&lt;/b&gt;");
    }

    [Fact]
    public void RenderVariablesStatic_ShouldCleanUnmatchedPlaceholders()
    {
        // Act
        var result = MailTemplateService.RenderVariablesStatic("Hi {{name}} and {{unknown}}", new Dictionary<string, string> { ["name"] = "Tom" });

        // Assert
        result.Should().Be("Hi Tom and ");
    }

    #endregion
}
