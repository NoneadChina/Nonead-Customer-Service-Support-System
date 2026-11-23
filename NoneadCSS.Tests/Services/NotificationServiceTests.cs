using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Tests.Services;

public class NotificationServiceTests : IAsyncLifetime
{
    private NoneadCSSDbContext _context = null!;
    private IUnitOfWork _unitOfWork = null!;
    private Mock<IEmailService> _mockEmailService = null!;
    private Mock<IEmailQueueService> _mockEmailQueueService = null!;
    private Mock<NoneadCSS.Domain.Events.INotificationPublisher> _mockNotificationPublisher = null!;
    private Mock<ILogger<NotificationService>> _mockLogger = null!;
    private NotificationService _notificationService = null!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseInMemoryDatabase(databaseName: $"NotificationTestDb_{Guid.NewGuid()}")
            .Options;

        _context = new NoneadCSSDbContext(options);
        _unitOfWork = new UnitOfWork(_context, NoneadCSS.Tests.Shared.TestServiceProviderBuilder.Build(_context));

        _mockEmailService = new Mock<IEmailService>();
        _mockEmailQueueService = new Mock<IEmailQueueService>();
        _mockNotificationPublisher = new Mock<NoneadCSS.Domain.Events.INotificationPublisher>();
        _mockLogger = new Mock<ILogger<NotificationService>>();

        var brandOptions = Microsoft.Extensions.Options.Options.Create(new BrandOptions
        {
            CompanyName = "拓德科技",
            FrontendUrl = "http://localhost:5173",
            LoginUrl = "http://localhost:5173/login"
        });

        _notificationService = new NotificationService(
            _unitOfWork,
            _mockEmailService.Object,
            _mockEmailQueueService.Object,
            _mockNotificationPublisher.Object,
            _mockLogger.Object,
            new TemplateRenderer(),
            brandOptions);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    #region Template Tests

    [Fact]
    public async Task CreateTemplateAsync_WithValidRequest_ShouldCreateTemplate()
    {
        var request = new CreateNotificationTemplateRequest
        {
            Name = "Test Template",
            Subject = "Test Subject",
            Body = "Test Body",
            EventType = "new_conversation",
            IsActive = true
        };

        var result = await _notificationService.CreateTemplateAsync(request);

        result.Should().NotBeNull();
        result.Name.Should().Be("Test Template");
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateTemplateAsync_WithDuplicateName_ShouldThrowException()
    {
        await _notificationService.CreateTemplateAsync(new CreateNotificationTemplateRequest
        {
            Name = "Duplicate Template",
            Subject = "Subject",
            Body = "Body",
            EventType = "new_conversation"
        });

        var duplicateRequest = new CreateNotificationTemplateRequest
        {
            Name = "Duplicate Template",
            Subject = "Subject",
            Body = "Body",
            EventType = "new_conversation"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _notificationService.CreateTemplateAsync(duplicateRequest));
    }

    [Fact]
    public async Task GetTemplateByIdAsync_WithExistingId_ShouldReturnTemplate()
    {
        var request = new CreateNotificationTemplateRequest
        {
            Name = "GetById Template",
            Subject = "Subject",
            Body = "Body",
            EventType = "new_conversation"
        };

        var created = await _notificationService.CreateTemplateAsync(request);

        var result = await _notificationService.GetTemplateByIdAsync(created.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("GetById Template");
    }

    [Fact]
    public async Task GetTemplateByIdAsync_WithNonExistingId_ShouldReturnNull()
    {
        var result = await _notificationService.GetTemplateByIdAsync(999);
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateTemplateAsync_WithValidRequest_ShouldUpdateTemplate()
    {
        var request = new CreateNotificationTemplateRequest
        {
            Name = "Update Template",
            Subject = "Old Subject",
            Body = "Old Body",
            EventType = "new_conversation"
        };

        var created = await _notificationService.CreateTemplateAsync(request);

        var updateRequest = new UpdateNotificationTemplateRequest
        {
            Name = "Updated Template",
            Subject = "New Subject",
            Body = "New Body",
            EventType = "new_reply",
            IsActive = false
        };

        var result = await _notificationService.UpdateTemplateAsync(created.Id, updateRequest);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated Template");
        result.Subject.Should().Be("New Subject");
        result.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteTemplateAsync_WithExistingId_ShouldSoftDelete()
    {
        var request = new CreateNotificationTemplateRequest
        {
            Name = "Delete Template",
            Subject = "Subject",
            Body = "Body",
            EventType = "new_conversation"
        };

        var created = await _notificationService.CreateTemplateAsync(request);

        var result = await _notificationService.DeleteTemplateAsync(created.Id);

        result.Should().BeTrue();
        var deletedTemplate = await _context.NotificationTemplates.FindAsync(created.Id);
        deletedTemplate.Should().NotBeNull();
        deletedTemplate!.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ToggleTemplateActiveAsync_WithExistingId_ShouldToggleStatus()
    {
        var request = new CreateNotificationTemplateRequest
        {
            Name = "Toggle Template",
            Subject = "Subject",
            Body = "Body",
            EventType = "new_conversation",
            IsActive = true
        };

        var created = await _notificationService.CreateTemplateAsync(request);

        var result = await _notificationService.ToggleTemplateActiveAsync(created.Id);

        result.Should().NotBeNull();
        result!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DuplicateTemplateAsync_WithExistingId_ShouldCreateCopy()
    {
        var request = new CreateNotificationTemplateRequest
        {
            Name = "Duplicate Source",
            Subject = "Subject",
            Body = "Body",
            EventType = "new_conversation"
        };

        var created = await _notificationService.CreateTemplateAsync(request);

        var result = await _notificationService.DuplicateTemplateAsync(created.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Contain("副本");
        result.Subject.Should().Be("Subject");
        result.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateTemplateVariables_ShouldExtractVariables()
    {
        var result = _notificationService.ValidateTemplateVariables(
            "Hello {customer_name}",
            "Email: {customer_email} - {ticket_id}");

        result.AllVariables.Should().Contain("customer_name");
        result.AllVariables.Should().Contain("customer_email");
        result.AllVariables.Should().Contain("ticket_id");
    }

    #endregion

    #region Rule Tests

    [Fact]
    public async Task CreateRuleAsync_WithValidRequest_ShouldCreateRule()
    {
        var request = new CreateNotificationRuleRequest
        {
            Name = "Test Rule",
            EventType = "new_conversation",
            IsActive = true
        };

        var result = await _notificationService.CreateRuleAsync(request);

        result.Should().NotBeNull();
        result.Name.Should().Be("Test Rule");
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetRuleByIdAsync_WithExistingId_ShouldReturnRule()
    {
        var request = new CreateNotificationRuleRequest
        {
            Name = "GetById Rule",
            EventType = "new_conversation"
        };

        var created = await _notificationService.CreateRuleAsync(request);

        var result = await _notificationService.GetRuleByIdAsync(created.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("GetById Rule");
    }

    [Fact]
    public async Task DeleteRuleAsync_WithExistingId_ShouldSoftDelete()
    {
        var request = new CreateNotificationRuleRequest
        {
            Name = "Delete Rule",
            EventType = "new_conversation"
        };

        var created = await _notificationService.CreateRuleAsync(request);

        var result = await _notificationService.DeleteRuleAsync(created.Id);

        result.Should().BeTrue();
        var deletedRule = await _context.NotificationRules.FindAsync(created.Id);
        deletedRule.Should().NotBeNull();
        deletedRule!.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DuplicateRuleAsync_WithExistingId_ShouldCreateCopy()
    {
        var request = new CreateNotificationRuleRequest
        {
            Name = "Duplicate Rule",
            EventType = "new_conversation"
        };

        var created = await _notificationService.CreateRuleAsync(request);

        var result = await _notificationService.DuplicateRuleAsync(created.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Contain("副本");
        result.IsActive.Should().BeFalse();
    }

    #endregion

    #region RenderTemplate Tests

    [Fact]
    public void RenderTemplate_WithVariables_ShouldReplaceVariables()
    {
        var template = "Hello {name}, your ticket is {ticket_id}";
        var variables = new Dictionary<string, string>
        {
            { "name", "John" },
            { "ticket_id", "TKT-001" }
        };

        var result = _notificationService.RenderTemplate(template, variables);

        result.Should().Be("Hello John, your ticket is TKT-001");
    }

    [Fact]
    public void RenderTemplate_WithMissingVariable_ShouldClearPlaceholder()
    {
        var template = "Hello {name}";
        var variables = new Dictionary<string, string>();

        var result = _notificationService.RenderTemplate(template, variables);

        result.Should().Be("Hello ");
    }

    [Fact]
    public void RenderTemplate_WithXssPayload_ShouldHtmlEncodeValue()
    {
        // Arrange — XSS 注入测试：变量值含 script 标签，应被 HTML 编码
        var template = "<p>{content}</p>";
        var variables = new Dictionary<string, string>
        {
            { "content", "<script>alert('xss')</script>" }
        };

        // Act
        var result = _notificationService.RenderTemplate(template, variables);

        // Assert — HTML 编码后 < > 应变为 &lt; &gt;
        result.Should().Contain("&lt;script&gt;");
        result.Should().NotContain("<script>");
    }

    [Fact]
    public void RenderTemplate_WithNullOrEmptyTemplate_ShouldReturnEmptyString()
    {
        // Act & Assert
        _notificationService.RenderTemplate(null!, new Dictionary<string, string> { { "k", "v" } })
            .Should().BeEmpty();
        _notificationService.RenderTemplate("", new Dictionary<string, string> { { "k", "v" } })
            .Should().BeEmpty();
    }

    [Fact]
    public void RenderTemplate_WithNullVariables_ShouldReturnTemplateAsIs()
    {
        // Arrange
        var template = "Hello {name}";

        // Act
        var result = _notificationService.RenderTemplate(template, null!);

        // Assert — null variables means no replacement, but unmatched placeholders are cleaned
        result.Should().Be("Hello ");
    }

    #endregion

    #region Template Trash Tests

    [Fact]
    public async Task GetTemplateTrashAsync_WithNoDeletedTemplates_ShouldReturnEmptyList()
    {
        // Act
        var (items, total) = await _notificationService.GetTemplateTrashAsync(1, 10, null);

        // Assert
        total.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task RestoreTemplateAsync_WithNonExistingId_ShouldReturnFalse()
    {
        // Act
        var result = await _notificationService.RestoreTemplateAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HardDeleteTemplateAsync_WithNonExistingId_ShouldReturnFalse()
    {
        // Act
        var result = await _notificationService.HardDeleteTemplateAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ClearTemplateTrashAsync_WithNoDeletedTemplates_ShouldReturnZero()
    {
        // Act
        var result = await _notificationService.ClearTemplateTrashAsync();

        // Assert
        result.Should().Be(0);
    }

    #endregion

    #region Template Overview Tests

    [Fact]
    public async Task GetTemplateOverviewAsync_WithNoTemplates_ShouldReturnZeroStats()
    {
        // Act
        var result = await _notificationService.GetTemplateOverviewAsync();

        // Assert
        result.Should().NotBeNull();
        result.Total.Should().Be(0);
    }

    [Fact]
    public async Task GetTemplateOverviewAsync_WithTemplates_ShouldReturnCorrectStats()
    {
        // Arrange
        await _notificationService.CreateTemplateAsync(new CreateNotificationTemplateRequest
        {
            Name = "Active Template",
            Subject = "Subject",
            Body = "Body",
            EventType = "new_conversation",
            IsActive = true
        });
        await _notificationService.CreateTemplateAsync(new CreateNotificationTemplateRequest
        {
            Name = "Inactive Template",
            Subject = "Subject",
            Body = "Body",
            EventType = "new_reply",
            IsActive = false
        });

        // Act
        var result = await _notificationService.GetTemplateOverviewAsync();

        // Assert
        result.Total.Should().Be(2);
    }

    #endregion

    #region Template Paged Query Tests

    [Fact]
    public async Task GetPagedTemplatesAsync_ShouldReturnPagedResults()
    {
        // Arrange
        for (var i = 1; i <= 3; i++)
        {
            await _notificationService.CreateTemplateAsync(new CreateNotificationTemplateRequest
            {
                Name = $"Template {i}",
                Subject = $"Subject {i}",
                Body = $"Body {i}",
                EventType = "new_conversation"
            });
        }

        // Act
        var query = new NotificationTemplateQuery { Page = 1, PageSize = 2 };
        var (items, total) = await _notificationService.GetPagedTemplatesAsync(query);

        // Assert
        total.Should().Be(3);
        items.Should().HaveCount(2);
    }

    #endregion

    #region Rule Trash Tests

    [Fact]
    public async Task GetRuleTrashAsync_WithNoDeletedRules_ShouldReturnEmptyList()
    {
        // Act
        var (items, total) = await _notificationService.GetRuleTrashAsync(1, 10, null);

        // Assert
        total.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task RestoreRuleAsync_WithNonExistingId_ShouldReturnFalse()
    {
        // Act
        var result = await _notificationService.RestoreRuleAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HardDeleteRuleAsync_WithNonExistingId_ShouldReturnFalse()
    {
        // Act
        var result = await _notificationService.HardDeleteRuleAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ClearRuleTrashAsync_WithNoDeletedRules_ShouldReturnZero()
    {
        // Act
        var result = await _notificationService.ClearRuleTrashAsync();

        // Assert
        result.Should().Be(0);
    }

    #endregion

    #region Rule Overview Tests

    [Fact]
    public async Task GetRuleOverviewAsync_WithNoRules_ShouldReturnZeroStats()
    {
        // Act
        var result = await _notificationService.GetRuleOverviewAsync();

        // Assert
        result.Should().NotBeNull();
        result.Total.Should().Be(0);
    }

    [Fact]
    public async Task GetRuleOverviewAsync_WithRules_ShouldReturnCorrectStats()
    {
        // Arrange
        await _notificationService.CreateRuleAsync(new CreateNotificationRuleRequest
        {
            Name = "Active Rule",
            EventType = "new_conversation",
            IsActive = true
        });

        // Act
        var result = await _notificationService.GetRuleOverviewAsync();

        // Assert
        result.Total.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion

    #region Rule Toggle & Update Tests

    [Fact]
    public async Task ToggleRuleActiveAsync_WithExistingRule_ShouldToggleStatus()
    {
        // Arrange
        var created = await _notificationService.CreateRuleAsync(new CreateNotificationRuleRequest
        {
            Name = "Toggle Rule",
            EventType = "new_conversation",
            IsActive = true
        });

        // Act
        var result = await _notificationService.ToggleRuleActiveAsync(created.Id);

        // Assert
        result.Should().NotBeNull();
        result!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ToggleRuleActiveAsync_WithNonExistingId_ShouldReturnNull()
    {
        // Act
        var result = await _notificationService.ToggleRuleActiveAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateRuleAsync_WithValidRequest_ShouldUpdateRule()
    {
        // Arrange
        var created = await _notificationService.CreateRuleAsync(new CreateNotificationRuleRequest
        {
            Name = "Original Rule",
            EventType = "new_conversation",
            IsActive = true
        });

        var updateRequest = new UpdateNotificationRuleRequest
        {
            Name = "Updated Rule",
            EventType = "new_reply",
            IsActive = false
        };

        // Act
        var result = await _notificationService.UpdateRuleAsync(created.Id, updateRequest);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated Rule");
        result.EventType.Should().Be("new_reply");
        result.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateRuleAsync_WithNonExistingId_ShouldReturnNull()
    {
        // Act
        var result = await _notificationService.UpdateRuleAsync(999, new UpdateNotificationRuleRequest());

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Rule Paged Query Tests

    [Fact]
    public async Task GetPagedRulesAsync_ShouldReturnPagedResults()
    {
        // Arrange
        for (var i = 1; i <= 3; i++)
        {
            await _notificationService.CreateRuleAsync(new CreateNotificationRuleRequest
            {
                Name = $"Rule {i}",
                EventType = "new_conversation"
            });
        }

        // Act
        var query = new NotificationRuleQuery { Page = 1, PageSize = 2 };
        var (items, total) = await _notificationService.GetPagedRulesAsync(query);

        // Assert
        total.Should().BeGreaterThanOrEqualTo(3);
        items.Should().HaveCountLessOrEqualTo(2);
    }

    #endregion

    #region GetRulesByEventTypeAsync Tests

    [Fact]
    public async Task GetRulesByEventTypeAsync_ShouldReturnFilteredRules()
    {
        // Arrange
        await _notificationService.CreateRuleAsync(new CreateNotificationRuleRequest
        {
            Name = "New Conv Rule",
            EventType = "new_conversation",
            IsActive = true
        });
        await _notificationService.CreateRuleAsync(new CreateNotificationRuleRequest
        {
            Name = "New Reply Rule",
            EventType = "new_reply",
            IsActive = true
        });

        // Act
        var result = await _notificationService.GetRulesByEventTypeAsync("new_conversation");

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain(r => r.EventType == "new_conversation");
    }

    #endregion
}