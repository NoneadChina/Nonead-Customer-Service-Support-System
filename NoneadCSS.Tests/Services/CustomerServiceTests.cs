using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// CustomerService 单元测试
/// </summary>
public class CustomerServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IAuditService> _mockAuditService;
    private readonly Mock<ICustomerRepository> _mockCustomerRepository;
    private readonly Mock<IConversationRepository> _mockConversationRepository;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;
    private readonly Mock<ILogger<CustomerService>> _mockLogger;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUserAccessor;
    private readonly Mock<IUserRoleResolver> _mockUserRoleResolver;

    private readonly CustomerService _customerService;

    public CustomerServiceTests()
    {
        // 初始化所有 mock 对象
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockAuditService = new Mock<IAuditService>();
        _mockCustomerRepository = new Mock<ICustomerRepository>();
        _mockConversationRepository = new Mock<IConversationRepository>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();
        _mockLogger = new Mock<ILogger<CustomerService>>();
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();
        _mockUserRoleResolver = new Mock<IUserRoleResolver>();

        // 默认超管身份（使硬删除等破坏性流程默认允许；具体测试可按需覆盖）
        _mockCurrentUserAccessor.Setup(a => a.UserId).Returns(1);
        _mockCurrentUserAccessor.Setup(a => a.IsSuperAdmin).Returns(true);
        _mockUserRoleResolver.Setup(r => r.IsSuperAdminAsync(It.IsAny<int>())).ReturnsAsync(true);

        // 配置 UnitOfWork 返回各个 repository
        _mockUnitOfWork.Setup(u => u.Customers).Returns(_mockCustomerRepository.Object);
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepository.Object);

        // 配置事务
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);

        // 配置 SaveChangesAsync（两个重载都设置，覆盖参数化与无参调用）
        _mockUnitOfWork
            .Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);
        _mockUnitOfWork
            .Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        // 创建 CustomerService 实例
        _customerService = new CustomerService(
            _mockUnitOfWork.Object,
            _mockAuditService.Object,
            _mockLogger.Object,
            _mockCurrentUserAccessor.Object,
            _mockUserRoleResolver.Object);
    }

    /// <summary>
    /// 创建支持异步 LINQ 操作的 Mock Customer DbSet
    /// </summary>
    private void SetupCustomerRepositoryQueryable(params Customer[] customers)
    {
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(customers.ToList());
        _mockCustomerRepository
            .Setup(r => r.GetQueryable(false))
            .Returns(mockDbSet.Object);
    }

    #region GetCustomerByIdAsync Tests

    [Fact]
    public async Task GetCustomerByIdAsync_Should_ReturnNull_When_CustomerDoesNotExist()
    {
        // Arrange
        _mockCustomerRepository
            .Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((Customer?)null);

        // Act
        var result = await _customerService.GetCustomerByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCustomerByIdAsync_Should_ReturnCustomer_When_CustomerExists()
    {
        // Arrange
        var customer = new Customer
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
            Company = "Test Corp"
        };
        customer.Emails.Add(new Email { EmailAddress = "john@example.com", Type = 1 });

        _mockCustomerRepository
            .Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(customer);

        _mockConversationRepository
            .Setup(r => r.CountActiveByCustomerIdAsync(1))
            .ReturnsAsync(3);

        // Act
        var result = await _customerService.GetCustomerByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.FirstName.Should().Be("John");
        result.LastName.Should().Be("Doe");
        result.MainEmail.Should().Be("john@example.com");
        result.ConversationsCount.Should().Be(3);
    }

    #endregion

    #region SearchCustomersAsync Tests

    [Fact]
    public async Task SearchCustomersAsync_Should_ReturnPagedResults_When_Searching()
    {
        // Arrange
        var customers = new List<Customer>
        {
            new() { Id = 1, FirstName = "John", LastName = "Doe" },
            new() { Id = 2, FirstName = "Jane", LastName = "Smith" }
        };

        _mockCustomerRepository
            .Setup(r => r.SearchAsync("john", 1, 10))
            .ReturnsAsync((customers, 2));

        _mockConversationRepository
            .Setup(r => r.GetCountByCustomerIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, int>());

        // Act
        var result = await _customerService.SearchCustomersAsync("john", 1, 10);

        // Assert
        result.Should().NotBeNull();
        result.Data.Should().HaveCount(2);
        result.TotalItems.Should().Be(2);
        result.CurrentPage.Should().Be(1);
        result.PageSize.Should().Be(10);
    }

    #endregion

    #region CreateCustomerAsync Tests

    [Fact]
    public async Task CreateCustomerAsync_Should_ReturnError_When_EmailAlreadyExists()
    {
        // Arrange
        var request = new CreateCustomerRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "existing@example.com"
        };

        _mockCustomerRepository
            .Setup(r => r.ExistsByEmailAsync(request.Email))
            .ReturnsAsync(true);

        // Act
        var (customer, errorMessage) = await _customerService.CreateCustomerAsync(request);

        // Assert
        customer.Should().BeNull();
        errorMessage.Should().Be("该邮箱已被使用");
        _mockCustomerRepository.Verify(r => r.AddAsync(It.IsAny<Customer>()), Times.Never);
    }

    [Fact]
    public async Task CreateCustomerAsync_Should_CreateCustomer_When_RequestIsValid()
    {
        // Arrange
        var request = new CreateCustomerRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "new@example.com",
            Company = "Test Corp",
            Country = "US"
        };

        _mockCustomerRepository
            .Setup(r => r.ExistsByEmailAsync(request.Email))
            .ReturnsAsync(false);

        _mockCustomerRepository
            .Setup(r => r.AddAsync(It.IsAny<Customer>()))
            .ReturnsAsync((Customer c) => c);

        // Act
        var (customer, errorMessage) = await _customerService.CreateCustomerAsync(request);

        // Assert
        customer.Should().NotBeNull();
        customer!.FirstName.Should().Be("John");
        customer.LastName.Should().Be("Doe");
        customer.MainEmail.Should().Be("new@example.com");
        errorMessage.Should().BeNull();

        _mockCustomerRepository.Verify(r => r.AddAsync(It.IsAny<Customer>()), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateCustomerAsync_Should_CreateCustomer_When_EmailIsEmpty()
    {
        // Arrange
        var request = new CreateCustomerRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "" // 空邮箱，跳过唯一性检查
        };

        _mockCustomerRepository
            .Setup(r => r.AddAsync(It.IsAny<Customer>()))
            .ReturnsAsync((Customer c) => c);

        // Act
        var (customer, errorMessage) = await _customerService.CreateCustomerAsync(request);

        // Assert
        customer.Should().NotBeNull();
        errorMessage.Should().BeNull();
        _mockCustomerRepository.Verify(r => r.ExistsByEmailAsync(It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region UpdateCustomerAsync Tests

    [Fact]
    public async Task UpdateCustomerAsync_Should_ReturnNull_When_CustomerDoesNotExist()
    {
        // Arrange
        var request = new CreateCustomerRequest
        {
            FirstName = "Updated",
            LastName = "User",
            Email = "updated@example.com"
        };

        SetupCustomerRepositoryQueryable(); // 空查询结果

        // Act
        var (customer, errorMessage) = await _customerService.UpdateCustomerAsync(999, request);

        // Assert
        customer.Should().BeNull();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public async Task UpdateCustomerAsync_Should_ReturnError_When_EmailAlreadyUsedByAnotherCustomer()
    {
        // Arrange
        var customer = new Customer
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe"
        };
        customer.Emails.Add(new Email { EmailAddress = "old@example.com", Type = 1 });

        SetupCustomerRepositoryQueryable(customer);

        _mockCustomerRepository
            .Setup(r => r.ExistsByEmailAsync("taken@example.com", 1))
            .ReturnsAsync(true);

        var request = new CreateCustomerRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "taken@example.com"
        };

        // Act
        var (result, errorMessage) = await _customerService.UpdateCustomerAsync(1, request);

        // Assert
        result.Should().BeNull();
        errorMessage.Should().Be("该邮箱已被使用");
    }

    [Fact]
    public async Task UpdateCustomerAsync_Should_UpdateCustomer_When_RequestIsValid()
    {
        // Arrange
        var customer = new Customer
        {
            Id = 1,
            FirstName = "Old",
            LastName = "Name",
            Company = "Old Corp"
        };
        customer.Emails.Add(new Email { EmailAddress = "old@example.com", Type = 1 });

        SetupCustomerRepositoryQueryable(customer);

        _mockCustomerRepository
            .Setup(r => r.ExistsByEmailAsync("new@example.com", 1))
            .ReturnsAsync(false);

        _mockConversationRepository
            .Setup(r => r.CountActiveByCustomerIdAsync(1))
            .ReturnsAsync(2);

        var request = new CreateCustomerRequest
        {
            FirstName = "New",
            LastName = "Name",
            Email = "new@example.com",
            Company = "New Corp"
        };

        // Act
        var (result, errorMessage) = await _customerService.UpdateCustomerAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        result!.FirstName.Should().Be("New");
        result.LastName.Should().Be("Name");
        result.Company.Should().Be("New Corp");
        result.ConversationsCount.Should().Be(2);
        errorMessage.Should().BeNull();

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region DeleteCustomerAsync Tests

    [Fact]
    public async Task DeleteCustomerAsync_Should_ReturnFalse_When_CustomerDoesNotExist()
    {
        // Arrange
        SetupCustomerRepositoryQueryable(); // 空查询结果

        // Act
        var (success, errorMessage) = await _customerService.DeleteCustomerAsync(999);

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public async Task DeleteCustomerAsync_Should_SoftDeleteCustomer_When_CustomerExists()
    {
        // Arrange
        var customer = new Customer
        {
            Id = 1,
            FirstName = "Delete",
            LastName = "Me"
        };

        SetupCustomerRepositoryQueryable(customer);

        _mockConversationRepository
            .Setup(r => r.SoftDeleteByCustomerIdAsync(1))
            .ReturnsAsync(2);

        // Act
        var (success, errorMessage) = await _customerService.DeleteCustomerAsync(1);

        // Assert
        success.Should().BeTrue();
        errorMessage.Should().BeNull();

        customer.DeletedAt.Should().NotBeNull();
        customer.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteCustomerAsync_Should_LogAudit_When_ConversationsWereSoftDeleted()
    {
        // Arrange
        var customer = new Customer
        {
            Id = 1,
            FirstName = "Delete",
            LastName = "Me"
        };

        SetupCustomerRepositoryQueryable(customer);

        _mockConversationRepository
            .Setup(r => r.SoftDeleteByCustomerIdAsync(1))
            .ReturnsAsync(5); // 5 个会话被软删除，应触发审计日志

        // Act
        var (success, _) = await _customerService.DeleteCustomerAsync(1);

        // Assert
        success.Should().BeTrue();
        _mockAuditService.Verify(
            a => a.LogAsync(
                "bulk_soft_delete",
                "Conversation",
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteCustomerAsync_Should_NotLogAudit_When_NoConversationsDeleted()
    {
        // Arrange
        var customer = new Customer
        {
            Id = 1,
            FirstName = "Delete",
            LastName = "Me"
        };

        SetupCustomerRepositoryQueryable(customer);

        _mockConversationRepository
            .Setup(r => r.SoftDeleteByCustomerIdAsync(1))
            .ReturnsAsync(0); // 没有会话被删除，不应触发审计日志

        // Act
        var (success, _) = await _customerService.DeleteCustomerAsync(1);

        // Assert
        success.Should().BeTrue();
        _mockAuditService.Verify(
            a => a.LogAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
}
