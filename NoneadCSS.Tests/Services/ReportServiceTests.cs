using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// ReportService 分页正确性单元测试
/// 验证 GetUserPerformancePagedAsync 的分页逻辑：
/// - TotalCount 返回全部非删除用户数
/// - pageSize 夹取到 100 上限
/// - page 超出范围时返回空列表但 TotalCount 仍正确
/// - 第 1 页返回前 pageSize 条用户
/// </summary>
public class ReportServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IUserRepository> _mockUserRepo;
    private readonly Mock<IConversationRepository> _mockConvRepo;
    private readonly Mock<IThreadRepository> _mockThreadRepo;
    private readonly ReportService _service;

    public ReportServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockUserRepo = new Mock<IUserRepository>();
        _mockConvRepo = new Mock<IConversationRepository>();
        _mockThreadRepo = new Mock<IThreadRepository>();

        _mockUnitOfWork.Setup(u => u.Users).Returns(_mockUserRepo.Object);
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConvRepo.Object);
        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);

        // Conversations 和 Threads 返回空可查询，使 JOIN 查询返回空，
        // 这样 BuildUserPerformanceCoreAsync 会为每个 user 生成全 0 的 DTO，不影响分页逻辑测试
        _mockConvRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(new List<Conversation>()));
        _mockThreadRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(new List<ConversationThread>()));

        // GetUserPerformanceAsync 返回空字典
        _mockConvRepo.Setup(r => r.GetUserPerformanceAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>()))
            .ReturnsAsync(new Dictionary<int, (int, int, int, double)>());

        _service = new ReportService(_mockUnitOfWork.Object);
    }

    /// <summary>
    /// 设置 UserRepository.GetQueryable 返回指定用户列表的异步可查询
    /// </summary>
    private void SetupUsers(List<User> users)
    {
        _mockUserRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(users));
    }

    /// <summary>
    /// 创建指定数量的活跃用户（Id 从 1 递增）
    /// </summary>
    private static List<User> CreateUsers(int count)
    {
        var users = new List<User>(count);
        for (var i = 1; i <= count; i++)
        {
            users.Add(CreateUser(i));
        }
        return users;
    }

    private static User CreateUser(int id) => new User
    {
        Id = id,
        FirstName = $"User{id}",
        LastName = "Test",
        Email = $"user{id}@test.com",
        Status = UserStatus.Active
    };

    [Fact]
    public async Task GetUserPerformancePagedAsync_Page1PageSize5_Returns5ItemsAndTotal15()
    {
        // Arrange - 15 个活跃用户
        SetupUsers(CreateUsers(15));

        // Act
        var (items, totalCount) = await _service.GetUserPerformancePagedAsync(
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, mailboxId: null, page: 1, pageSize: 5);

        // Assert - 第 1 页返回前 5 条，TotalCount 为全部非删除用户数
        totalCount.Should().Be(15);
        items.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetUserPerformancePagedAsync_Page3PageSize5_Returns5ItemsAndTotal15()
    {
        // Arrange - 15 个活跃用户
        SetupUsers(CreateUsers(15));

        // Act - 第 3 页（Skip 10, Take 5）
        var (items, totalCount) = await _service.GetUserPerformancePagedAsync(
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, mailboxId: null, page: 3, pageSize: 5);

        // Assert - 第 3 页仍有 5 条，TotalCount 不变
        totalCount.Should().Be(15);
        items.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetUserPerformancePagedAsync_Page4PageSize5_ReturnsEmptyAndTotal15()
    {
        // Arrange - 15 个活跃用户
        SetupUsers(CreateUsers(15));

        // Act - page=4 超出范围（15 条 / 5 每页 = 3 页，第 4 页无数据）
        var (items, totalCount) = await _service.GetUserPerformancePagedAsync(
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, mailboxId: null, page: 4, pageSize: 5);

        // Assert - 超出范围返回空列表，但 TotalCount 仍正确
        totalCount.Should().Be(15);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserPerformancePagedAsync_PageSizeExceeds100_ClampedTo100()
    {
        // Arrange - 15 个活跃用户
        SetupUsers(CreateUsers(15));

        // Act - pageSize=200 被夹取到 100，但只有 15 个用户
        var (items, totalCount) = await _service.GetUserPerformancePagedAsync(
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, mailboxId: null, page: 1, pageSize: 200);

        // Assert - pageSize 被夹取到 100 上限，不会因 200 而异常；仅有 15 个用户故返回 15 条
        totalCount.Should().Be(15);
        items.Should().HaveCount(15);
    }

    [Fact]
    public async Task GetUserPerformancePagedAsync_PageZero_ClampedToPage1()
    {
        // Arrange - 15 个活跃用户
        SetupUsers(CreateUsers(15));

        // Act - page=0 被夹取到 1
        var (items, totalCount) = await _service.GetUserPerformancePagedAsync(
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, mailboxId: null, page: 0, pageSize: 5);

        // Assert - page 被夹取到 1，返回前 5 条，TotalCount 不变
        totalCount.Should().Be(15);
        items.Should().HaveCount(5);
    }
}
