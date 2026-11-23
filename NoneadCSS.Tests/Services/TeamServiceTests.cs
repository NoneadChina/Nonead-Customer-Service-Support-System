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

namespace NoneadCSS.Tests.Services;

/// <summary>
/// TeamService 单元测试
/// 覆盖团队的 CRUD、成员管理、统计概览、按邮箱/用户查询、回收站等场景
/// </summary>
public class TeamServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<ITeamRepository> _mockTeamRepo;
    private readonly Mock<ITeamMemberRepository> _mockMemberRepo;
    private readonly Mock<IUserRepository> _mockUserRepo;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUserAccessor;
    private readonly TeamService _service;

    public TeamServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockTeamRepo = new Mock<ITeamRepository>();
        _mockMemberRepo = new Mock<ITeamMemberRepository>();
        _mockUserRepo = new Mock<IUserRepository>();
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();

        _mockUnitOfWork.Setup(u => u.Teams).Returns(_mockTeamRepo.Object);
        _mockUnitOfWork.Setup(u => u.TeamMembers).Returns(_mockMemberRepo.Object);
        _mockUnitOfWork.Setup(u => u.Users).Returns(_mockUserRepo.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(It.IsAny<int?>()))
            .ReturnsAsync(true);

        _service = new TeamService(_mockUnitOfWork.Object, _mockCurrentUserAccessor.Object);
    }

    /// <summary>
    /// 创建支持异步 LINQ 操作的 Mock Team DbSet
    /// </summary>
    private void SetupTeamQueryable(params Team[] teams)
    {
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(teams.ToList());
        _mockTeamRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(mockDbSet.Object);
    }

    private static Team CreateTeam(int id, string name = "Team", int? mailboxId = null,
        bool isActive = true, int sortOrder = 0, DateTime? deletedAt = null)
    {
        return new Team
        {
            Id = id,
            Name = name,
            Description = $"Desc {id}",
            MailboxId = mailboxId,
            IsActive = isActive,
            SortOrder = sortOrder,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddDays(-1),
            DeletedAt = deletedAt
        };
    }

    private static TeamMember CreateMember(int id, int teamId, int userId,
        TeamMemberRole role = TeamMemberRole.Member, Team? team = null, User? user = null)
    {
        return new TeamMember
        {
            Id = id,
            TeamId = teamId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTime.UtcNow.AddDays(-1),
            Team = team,
            User = user
        };
    }

    private static User CreateUser(int id, string firstName = "John", string lastName = "Doe",
        string email = "john@example.com")
    {
        return new User
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName,
            Email = email
        };
    }

    #region GetPagedAsync

    [Fact]
    public async Task GetPagedAsync_ShouldReturnPagedDtosWithMembers()
    {
        // Arrange
        var teams = new List<Team>
        {
            CreateTeam(1, "Team A"),
            CreateTeam(2, "Team B", mailboxId: 5)
        };

        var members = new List<TeamMember>
        {
            CreateMember(10, 1, 100, TeamMemberRole.Leader),
            CreateMember(11, 1, 101, TeamMemberRole.Member),
            CreateMember(12, 2, 102, TeamMemberRole.Member)
        };

        _mockTeamRepo.Setup(r => r.GetPagedAsync(1, 20, null, null, null))
            .ReturnsAsync((teams, 2));
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.Is<List<int>>(l => l.Contains(1) && l.Contains(2))))
            .ReturnsAsync(members);

        var query = new TeamQuery { Page = 1, PageSize = 20 };

        // Act
        var (resultItems, total) = await _service.GetPagedAsync(query);

        // Assert
        total.Should().Be(2);
        var list = resultItems.ToList();
        list.Should().HaveCount(2);
        list[0].MemberCount.Should().Be(2);
        list[0].LeaderCount.Should().Be(1);
        list[1].MemberCount.Should().Be(1);
        list[1].LeaderCount.Should().Be(0);
    }

    [Fact]
    public async Task GetPagedAsync_WithEmptyResult_ShouldReturnEmptyList()
    {
        // Arrange
        _mockTeamRepo.Setup(r => r.GetPagedAsync(1, 20, null, null, null))
            .ReturnsAsync((new List<Team>(), 0));
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(new List<TeamMember>());

        var query = new TeamQuery { Page = 1, PageSize = 20 };

        // Act
        var (resultItems, total) = await _service.GetPagedAsync(query);

        // Assert
        total.Should().Be(0);
        resultItems.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPagedAsync_WithFilters_ShouldPassFiltersToRepository()
    {
        // Arrange
        var teams = new List<Team> { CreateTeam(1, "Team A", mailboxId: 5, isActive: true) };

        _mockTeamRepo.Setup(r => r.GetPagedAsync(2, 10, 5, true, "Team"))
            .ReturnsAsync((teams, 1));
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(new List<TeamMember>());

        var query = new TeamQuery { Page = 2, PageSize = 10, MailboxId = 5, IsActive = true, Keyword = "Team" };

        // Act
        var (resultItems, total) = await _service.GetPagedAsync(query);

        // Assert
        total.Should().Be(1);
        resultItems.Should().HaveCount(1);
        _mockTeamRepo.Verify(r => r.GetPagedAsync(2, 10, 5, true, "Team"), Times.Once);
    }

    [Fact]
    public async Task GetPagedAsync_WithTeamWithoutMembers_ShouldHaveZeroCount()
    {
        // Arrange
        var teams = new List<Team> { CreateTeam(1, "Team A") };

        _mockTeamRepo.Setup(r => r.GetPagedAsync(1, 20, null, null, null))
            .ReturnsAsync((teams, 1));
        // Return empty list when teamIds contains 1
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.Is<List<int>>(l => l.Contains(1))))
            .ReturnsAsync(new List<TeamMember>());

        var query = new TeamQuery { Page = 1, PageSize = 20 };

        // Act
        var (resultItems, total) = await _service.GetPagedAsync(query);

        // Assert
        var dto = resultItems.First();
        dto.MemberCount.Should().Be(0);
        dto.LeaderCount.Should().Be(0);
    }

    #endregion

    #region GetByIdAsync

    [Fact]
    public async Task GetByIdAsync_WithExisting_ShouldReturnDetailDto()
    {
        // Arrange
        var team = CreateTeam(1, "Team A", mailboxId: 5);
        team.Mailbox = new Mailbox { Id = 5, Name = "Sales Mailbox" };
        var memberUser = CreateUser(100, "Jane", "Smith", "jane@example.com");
        team.Members = new List<TeamMember>
        {
            CreateMember(10, 1, 100, TeamMemberRole.Leader, team, memberUser)
        };

        _mockTeamRepo.Setup(r => r.GetWithMembersAsync(1))
            .ReturnsAsync(team);

        // Act
        var result = await _service.GetByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Team A");
        result.MailboxId.Should().Be(5);
        result.MailboxName.Should().Be("Sales Mailbox");
        result.Members.Should().HaveCount(1);
        result.Members[0].UserName.Should().Be("Jane Smith");
        result.Members[0].UserEmail.Should().Be("jane@example.com");
        result.Members[0].RoleName.Should().Be("团队负责人");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        _mockTeamRepo.Setup(r => r.GetWithMembersAsync(999))
            .ReturnsAsync((Team?)null);

        // Act
        var result = await _service.GetByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithMemberRole_ShouldMapRoleNameCorrectly()
    {
        // Arrange
        var team = CreateTeam(1, "Team A");
        team.Members = new List<TeamMember>
        {
            CreateMember(10, 1, 100, TeamMemberRole.Member)
        };

        _mockTeamRepo.Setup(r => r.GetWithMembersAsync(1))
            .ReturnsAsync(team);

        // Act
        var result = await _service.GetByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Members[0].RoleName.Should().Be("成员");
    }

    [Fact]
    public async Task GetByIdAsync_WithNoMembers_ShouldReturnEmptyMembersList()
    {
        // Arrange
        var team = CreateTeam(1, "Team A");
        team.Members = new List<TeamMember>();

        _mockTeamRepo.Setup(r => r.GetWithMembersAsync(1))
            .ReturnsAsync(team);

        // Act
        var result = await _service.GetByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Members.Should().BeEmpty();
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_ShouldCreateTeamAndReturnDto()
    {
        // Arrange
        var request = new CreateTeamRequest
        {
            Name = "New Team",
            Description = "Description",
            MailboxId = 5,
            IsActive = true,
            SortOrder = 10
        };

        Team? captured = null;
        _mockTeamRepo.Setup(r => r.AddAsync(It.IsAny<Team>()))
            .Callback<Team>(t => captured = t)
            .ReturnsAsync((Team t) => t);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("New Team");
        result.Description.Should().Be("Description");
        result.MailboxId.Should().Be(5);
        result.IsActive.Should().BeTrue();
        result.SortOrder.Should().Be(10);
        result.MemberCount.Should().Be(0);
        result.LeaderCount.Should().Be(0);

        captured.Should().NotBeNull();
        captured!.Name.Should().Be("New Team");
        captured.CreatedAt.Should().NotBe(default);
        captured.UpdatedAt.Should().NotBe(default);

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithNullMailboxId_ShouldCreateGlobalTeam()
    {
        // Arrange
        var request = new CreateTeamRequest
        {
            Name = "Global Team",
            MailboxId = null,
            IsActive = true
        };

        _mockTeamRepo.Setup(r => r.AddAsync(It.IsAny<Team>()))
            .ReturnsAsync((Team t) => t);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.MailboxId.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_WithInactiveStatus_ShouldPersistInactive()
    {
        // Arrange
        var request = new CreateTeamRequest
        {
            Name = "Inactive Team",
            IsActive = false
        };

        Team? captured = null;
        _mockTeamRepo.Setup(r => r.AddAsync(It.IsAny<Team>()))
            .Callback<Team>(t => captured = t)
            .ReturnsAsync((Team t) => t);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.IsActive.Should().BeFalse();
        captured!.IsActive.Should().BeFalse();
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_WithExisting_ShouldUpdateAndReturnDto()
    {
        // Arrange
        var entity = CreateTeam(1, "Old Team", mailboxId: 1);

        SetupTeamQueryable(entity);

        var request = new UpdateTeamRequest
        {
            Name = "New Team",
            Description = "New Desc",
            MailboxId = 2,
            IsActive = false,
            SortOrder = 99
        };

        var existingMembers = new List<TeamMember>
        {
            CreateMember(10, 1, 100, TeamMemberRole.Leader)
        };
        _mockMemberRepo.Setup(r => r.GetByTeamIdAsync(1))
            .ReturnsAsync(existingMembers);

        // Act
        var result = await _service.UpdateAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("New Team");
        result.Description.Should().Be("New Desc");
        result.MailboxId.Should().Be(2);
        result.IsActive.Should().BeFalse();
        result.SortOrder.Should().Be(99);
        result.MemberCount.Should().Be(1);
        result.LeaderCount.Should().Be(1);

        entity.Name.Should().Be("New Team");
        entity.UpdatedAt.Should().NotBe(default);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        SetupTeamQueryable();

        var request = new UpdateTeamRequest
        {
            Name = "X",
            MailboxId = null,
            IsActive = true,
            SortOrder = 0
        };

        // Act
        var result = await _service.UpdateAsync(999, request);

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WithSoftDeleted_ShouldReturnNull()
    {
        // Arrange - 软删除的记录不应被更新
        var entity = CreateTeam(1, "Deleted", deletedAt: DateTime.UtcNow);
        SetupTeamQueryable(entity);

        var request = new UpdateTeamRequest
        {
            Name = "X",
            MailboxId = null,
            IsActive = true,
            SortOrder = 0
        };

        // Act
        var result = await _service.UpdateAsync(1, request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ShouldUseNoTrackingQueryable()
    {
        // Arrange
        var entity = CreateTeam(1, "Old");
        SetupTeamQueryable(entity);

        _mockMemberRepo.Setup(r => r.GetByTeamIdAsync(1))
            .ReturnsAsync(new List<TeamMember>());

        var request = new UpdateTeamRequest
        {
            Name = "New",
            MailboxId = null,
            IsActive = true,
            SortOrder = 0
        };

        // Act
        await _service.UpdateAsync(1, request);

        // Assert - 验证使用了 asNoTracking=false
        _mockTeamRepo.Verify(r => r.GetQueryable(false), Times.Once);
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_WithExisting_ShouldSoftDeleteAndReturnTrue()
    {
        // Arrange
        var entity = CreateTeam(1, "Team A");
        SetupTeamQueryable(entity);

        // Act
        var result = await _service.DeleteAsync(1);

        // Assert
        result.Should().BeTrue();
        entity.DeletedAt.Should().NotBeNull();
        entity.UpdatedAt.Should().NotBe(default);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExisting_ShouldReturnFalse()
    {
        // Arrange
        SetupTeamQueryable();

        // Act
        var result = await _service.DeleteAsync(999);

        // Assert
        result.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WithSoftDeleted_ShouldReturnFalse()
    {
        // Arrange
        var entity = CreateTeam(1, "Deleted", deletedAt: DateTime.UtcNow);
        SetupTeamQueryable(entity);

        // Act
        var result = await _service.DeleteAsync(1);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetOverviewAsync

    [Fact]
    public async Task GetOverviewAsync_ShouldReturnCorrectStats()
    {
        // Arrange
        var stats = new Dictionary<string, int>
        {
            { "total", 10 },
            { "active", 7 },
            { "inactive", 3 },
            { "totalMembers", 25 },
            { "mailboxSpecific", 4 }
        };

        var topTeams = new List<Team>
        {
            CreateTeam(1, "Top1"),
            CreateTeam(2, "Top2")
        };

        var topMembers = new List<TeamMember>
        {
            CreateMember(10, 1, 100, TeamMemberRole.Leader)
        };

        _mockTeamRepo.Setup(r => r.GetStatsAsync())
            .ReturnsAsync(stats);
        _mockTeamRepo.Setup(r => r.GetPagedAsync(1, 5, It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<string>()))
            .ReturnsAsync((topTeams, 2));
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(topMembers);

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Should().NotBeNull();
        result.TotalTeams.Should().Be(10);
        result.ActiveTeams.Should().Be(7);
        result.InactiveTeams.Should().Be(3);
        result.TotalMembers.Should().Be(25);
        result.TeamsPerMailbox.Should().Be(4);
        result.TopTeams.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetOverviewAsync_WithEmptyStats_ShouldReturnZeros()
    {
        // Arrange
        _mockTeamRepo.Setup(r => r.GetStatsAsync())
            .ReturnsAsync(new Dictionary<string, int>());
        _mockTeamRepo.Setup(r => r.GetPagedAsync(1, 5, It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<string>()))
            .ReturnsAsync((new List<Team>(), 0));
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(new List<TeamMember>());

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.TotalTeams.Should().Be(0);
        result.ActiveTeams.Should().Be(0);
        result.InactiveTeams.Should().Be(0);
        result.TotalMembers.Should().Be(0);
        result.TeamsPerMailbox.Should().Be(0);
        result.TopTeams.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverviewAsync_WithTopTeamMembers_ShouldMapMemberCounts()
    {
        // Arrange
        var stats = new Dictionary<string, int> { { "total", 1 } };
        var topTeams = new List<Team> { CreateTeam(1, "Top1") };
        var topMembers = new List<TeamMember>
        {
            CreateMember(10, 1, 100, TeamMemberRole.Leader),
            CreateMember(11, 1, 101, TeamMemberRole.Member)
        };

        _mockTeamRepo.Setup(r => r.GetStatsAsync()).ReturnsAsync(stats);
        _mockTeamRepo.Setup(r => r.GetPagedAsync(1, 5, It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<string>()))
            .ReturnsAsync((topTeams, 1));
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(topMembers);

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.TopTeams[0].MemberCount.Should().Be(2);
        result.TopTeams[0].LeaderCount.Should().Be(1);
    }

    #endregion

    #region GetByMailboxIdAsync

    [Fact]
    public async Task GetByMailboxIdAsync_WithMailboxId_ShouldReturnTeams()
    {
        // Arrange
        var teams = new List<Team>
        {
            CreateTeam(1, "Team1", mailboxId: 5),
            CreateTeam(2, "Team2", mailboxId: 5)
        };
        var members = new List<TeamMember>
        {
            CreateMember(10, 1, 100, TeamMemberRole.Member)
        };

        _mockTeamRepo.Setup(r => r.GetByMailboxIdAsync(5))
            .ReturnsAsync(teams);
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(members);

        // Act
        var result = await _service.GetByMailboxIdAsync(5);

        // Assert
        result.Should().HaveCount(2);
        result[0].MemberCount.Should().Be(1);
        result[1].MemberCount.Should().Be(0);
    }

    [Fact]
    public async Task GetByMailboxIdAsync_WithNull_ShouldReturnGlobalTeams()
    {
        // Arrange
        var teams = new List<Team> { CreateTeam(1, "Global", mailboxId: null) };

        _mockTeamRepo.Setup(r => r.GetByMailboxIdAsync(null))
            .ReturnsAsync(teams);
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(new List<TeamMember>());

        // Act
        var result = await _service.GetByMailboxIdAsync(null);

        // Assert
        result.Should().HaveCount(1);
        result[0].MailboxId.Should().BeNull();
    }

    [Fact]
    public async Task GetByMailboxIdAsync_WithNoTeams_ShouldReturnEmptyList()
    {
        // Arrange
        _mockTeamRepo.Setup(r => r.GetByMailboxIdAsync(99))
            .ReturnsAsync(new List<Team>());
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(new List<TeamMember>());

        // Act
        var result = await _service.GetByMailboxIdAsync(99);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetByUserIdAsync

    [Fact]
    public async Task GetByUserIdAsync_ShouldReturnTeamsForUser()
    {
        // Arrange
        var teams = new List<Team>
        {
            CreateTeam(1, "Team1"),
            CreateTeam(2, "Team2")
        };
        var members = new List<TeamMember>
        {
            CreateMember(10, 1, 42, TeamMemberRole.Leader),
            CreateMember(11, 2, 42, TeamMemberRole.Member)
        };

        _mockTeamRepo.Setup(r => r.GetByUserIdAsync(42))
            .ReturnsAsync(teams);
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(members);

        // Act
        var result = await _service.GetByUserIdAsync(42);

        // Assert
        result.Should().HaveCount(2);
        result[0].MemberCount.Should().Be(1);
        result[0].LeaderCount.Should().Be(1);
        result[1].MemberCount.Should().Be(1);
        result[1].LeaderCount.Should().Be(0);
    }

    [Fact]
    public async Task GetByUserIdAsync_WithNoTeams_ShouldReturnEmptyList()
    {
        // Arrange
        _mockTeamRepo.Setup(r => r.GetByUserIdAsync(99))
            .ReturnsAsync(new List<Team>());
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(new List<TeamMember>());

        // Act
        var result = await _service.GetByUserIdAsync(99);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region AddMemberAsync

    [Fact]
    public async Task AddMemberAsync_ShouldAddMemberAndReturnDto()
    {
        // Arrange
        var team = CreateTeam(1, "Team A");
        var user = CreateUser(100, "John", "Doe", "john@example.com");

        _mockMemberRepo.Setup(r => r.GetByTeamAndUserAsync(1, 100))
            .ReturnsAsync((TeamMember?)null);
        _mockTeamRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(team);
        _mockUserRepo.Setup(r => r.GetByIdAsync(100))
            .ReturnsAsync(user);

        TeamMember? captured = null;
        _mockMemberRepo.Setup(r => r.AddAsync(It.IsAny<TeamMember>()))
            .Callback<TeamMember>(m => { captured = m; m.Id = 999; })
            .ReturnsAsync((TeamMember m) => m);

        var request = new AddTeamMemberRequest { UserId = 100, Role = 2 };

        // Act
        var result = await _service.AddMemberAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(999);
        result.TeamId.Should().Be(1);
        result.TeamName.Should().Be("Team A");
        result.UserId.Should().Be(100);
        result.UserName.Should().Be("John Doe");
        result.UserEmail.Should().Be("john@example.com");
        result.Role.Should().Be(TeamMemberRole.Leader);
        result.RoleName.Should().Be("团队负责人");
        result.JoinedAt.Should().NotBe(default);

        captured.Should().NotBeNull();
        captured!.TeamId.Should().Be(1);
        captured.UserId.Should().Be(100);
        captured.Role.Should().Be(TeamMemberRole.Leader);

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task AddMemberAsync_WithMemberRole_ShouldMapRoleNameCorrectly()
    {
        // Arrange
        var team = CreateTeam(1, "Team A");
        var user = CreateUser(100);

        _mockMemberRepo.Setup(r => r.GetByTeamAndUserAsync(1, 100))
            .ReturnsAsync((TeamMember?)null);
        _mockTeamRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(team);
        _mockUserRepo.Setup(r => r.GetByIdAsync(100))
            .ReturnsAsync(user);
        _mockMemberRepo.Setup(r => r.AddAsync(It.IsAny<TeamMember>()))
            .ReturnsAsync((TeamMember m) => m);

        var request = new AddTeamMemberRequest { UserId = 100, Role = 1 };

        // Act
        var result = await _service.AddMemberAsync(1, request);

        // Assert
        result.Role.Should().Be(TeamMemberRole.Member);
        result.RoleName.Should().Be("成员");
    }

    [Fact]
    public async Task AddMemberAsync_WhenUserAlreadyMember_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var existingMember = CreateMember(10, 1, 100);
        _mockMemberRepo.Setup(r => r.GetByTeamAndUserAsync(1, 100))
            .ReturnsAsync(existingMember);

        var request = new AddTeamMemberRequest { UserId = 100, Role = 1 };

        // Act
        var act = () => _service.AddMemberAsync(1, request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("该用户已在团队中");
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task AddMemberAsync_WhenTeamDoesNotExist_ShouldThrowInvalidOperationException()
    {
        // Arrange
        _mockMemberRepo.Setup(r => r.GetByTeamAndUserAsync(1, 100))
            .ReturnsAsync((TeamMember?)null);
        _mockTeamRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync((Team?)null);

        var request = new AddTeamMemberRequest { UserId = 100, Role = 1 };

        // Act
        var act = () => _service.AddMemberAsync(1, request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("团队不存在");
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task AddMemberAsync_WhenUserDoesNotExist_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var team = CreateTeam(1, "Team A");
        _mockMemberRepo.Setup(r => r.GetByTeamAndUserAsync(1, 100))
            .ReturnsAsync((TeamMember?)null);
        _mockTeamRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(team);
        _mockUserRepo.Setup(r => r.GetByIdAsync(100))
            .ReturnsAsync((User?)null);

        var request = new AddTeamMemberRequest { UserId = 100, Role = 1 };

        // Act
        var act = () => _service.AddMemberAsync(1, request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("用户不存在");
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task AddMemberAsync_ShouldCheckExistenceBeforeTeamLookup()
    {
        // Arrange - 验证调用顺序：先检查成员是否存在
        var existingMember = CreateMember(10, 1, 100);
        _mockMemberRepo.Setup(r => r.GetByTeamAndUserAsync(1, 100))
            .ReturnsAsync(existingMember);

        var request = new AddTeamMemberRequest { UserId = 100, Role = 1 };

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddMemberAsync(1, request));

        // Assert - 由于成员已存在，不应再查询 Team
        _mockTeamRepo.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
        _mockUserRepo.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region UpdateMemberRoleAsync

    [Fact]
    public async Task UpdateMemberRoleAsync_WithExisting_ShouldUpdateAndReturnDto()
    {
        // Arrange
        var member = CreateMember(10, 1, 100, TeamMemberRole.Member);
        var team = CreateTeam(1, "Team A");
        member.Team = team;

        _mockMemberRepo.Setup(r => r.GetByTeamAndUserAsync(1, 100))
            .ReturnsAsync(member);

        // Act
        var result = await _service.UpdateMemberRoleAsync(1, 100, 2);

        // Assert
        result.Should().NotBeNull();
        result!.Role.Should().Be(TeamMemberRole.Leader);
        result.RoleName.Should().Be("团队负责人");
        member.Role.Should().Be(TeamMemberRole.Leader);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateMemberRoleAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        _mockMemberRepo.Setup(r => r.GetByTeamAndUserAsync(1, 100))
            .ReturnsAsync((TeamMember?)null);

        // Act
        var result = await _service.UpdateMemberRoleAsync(1, 100, 2);

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateMemberRoleAsync_DemoteToMember_ShouldUpdateRole()
    {
        // Arrange
        var member = CreateMember(10, 1, 100, TeamMemberRole.Leader);

        _mockMemberRepo.Setup(r => r.GetByTeamAndUserAsync(1, 100))
            .ReturnsAsync(member);

        // Act
        var result = await _service.UpdateMemberRoleAsync(1, 100, 1);

        // Assert
        result.Should().NotBeNull();
        result!.Role.Should().Be(TeamMemberRole.Member);
        result.RoleName.Should().Be("成员");
    }

    #endregion

    #region RemoveMemberAsync

    [Fact]
    public async Task RemoveMemberAsync_WithExisting_ShouldDeleteAndReturnTrue()
    {
        // Arrange
        var member = CreateMember(10, 1, 100);

        _mockMemberRepo.Setup(r => r.GetByTeamAndUserAsync(1, 100))
            .ReturnsAsync(member);

        // Act
        var result = await _service.RemoveMemberAsync(1, 100);

        // Assert
        result.Should().BeTrue();
        _mockMemberRepo.Verify(r => r.DeleteAsync(member), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task RemoveMemberAsync_WithNonExisting_ShouldReturnFalse()
    {
        // Arrange
        _mockMemberRepo.Setup(r => r.GetByTeamAndUserAsync(1, 100))
            .ReturnsAsync((TeamMember?)null);

        // Act
        var result = await _service.RemoveMemberAsync(1, 100);

        // Assert
        result.Should().BeFalse();
        _mockMemberRepo.Verify(r => r.DeleteAsync(It.IsAny<TeamMember>()), Times.Never);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    #endregion

    #region GetMembersAsync

    [Fact]
    public async Task GetMembersAsync_ShouldReturnMemberDtos()
    {
        // Arrange
        var team = CreateTeam(1, "Team A");
        var user1 = CreateUser(100, "Alice", "Wonder", "alice@example.com");
        var user2 = CreateUser(101, "Bob", "Builder", "bob@example.com");
        var members = new List<TeamMember>
        {
            CreateMember(10, 1, 100, TeamMemberRole.Leader, team, user1),
            CreateMember(11, 1, 101, TeamMemberRole.Member, team, user2)
        };

        _mockMemberRepo.Setup(r => r.GetByTeamIdAsync(1))
            .ReturnsAsync(members);

        // Act
        var result = await _service.GetMembersAsync(1);

        // Assert
        result.Should().HaveCount(2);
        result[0].Id.Should().Be(10);
        result[0].TeamId.Should().Be(1);
        result[0].TeamName.Should().Be("Team A");
        result[0].UserId.Should().Be(100);
        result[0].UserName.Should().Be("Alice Wonder");
        result[0].UserEmail.Should().Be("alice@example.com");
        result[0].Role.Should().Be(TeamMemberRole.Leader);
        result[0].RoleName.Should().Be("团队负责人");
        result[0].JoinedAt.Should().NotBe(default);

        result[1].Role.Should().Be(TeamMemberRole.Member);
        result[1].RoleName.Should().Be("成员");
    }

    [Fact]
    public async Task GetMembersAsync_WithNoMembers_ShouldReturnEmptyList()
    {
        // Arrange
        _mockMemberRepo.Setup(r => r.GetByTeamIdAsync(99))
            .ReturnsAsync(new List<TeamMember>());

        // Act
        var result = await _service.GetMembersAsync(99);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMembersAsync_WithNullTeamAndUser_ShouldUseEmptyStrings()
    {
        // Arrange
        var member = CreateMember(10, 1, 100);
        member.Team = null;
        member.User = null;

        _mockMemberRepo.Setup(r => r.GetByTeamIdAsync(1))
            .ReturnsAsync(new List<TeamMember> { member });

        // Act
        var result = await _service.GetMembersAsync(1);

        // Assert
        result.Should().HaveCount(1);
        result[0].TeamName.Should().BeEmpty();
        result[0].UserName.Should().BeEmpty();
        result[0].UserEmail.Should().BeEmpty();
    }

    #endregion

    #region GetTrashAsync

    [Fact]
    public async Task GetTrashAsync_ShouldReturnPagedDeletedItems()
    {
        // Arrange
        var items = new List<Team>
        {
            CreateTeam(1, "Deleted1", deletedAt: DateTime.UtcNow),
            CreateTeam(2, "Deleted2", deletedAt: DateTime.UtcNow)
        };
        var members = new List<TeamMember>
        {
            CreateMember(10, 1, 100, TeamMemberRole.Member)
        };

        _mockTeamRepo.Setup(r => r.GetDeletedAsync(1, 20, null))
            .ReturnsAsync((items, 2));
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(members);

        // Act
        var (resultItems, total) = await _service.GetTrashAsync(1, 20);

        // Assert
        total.Should().Be(2);
        var list = resultItems.ToList();
        list.Should().HaveCount(2);
        list[0].DeletedAt.Should().NotBeNull();
        list[0].MemberCount.Should().Be(1);
        list[1].MemberCount.Should().Be(0);
    }

    [Fact]
    public async Task GetTrashAsync_WithKeyword_ShouldPassKeywordToRepository()
    {
        // Arrange
        _mockTeamRepo.Setup(r => r.GetDeletedAsync(1, 10, "abc"))
            .ReturnsAsync((new List<Team>(), 0));
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(new List<TeamMember>());

        // Act
        var (resultItems, total) = await _service.GetTrashAsync(1, 10, "abc");

        // Assert
        total.Should().Be(0);
        resultItems.Should().BeEmpty();
        _mockTeamRepo.Verify(r => r.GetDeletedAsync(1, 10, "abc"), Times.Once);
    }

    [Fact]
    public async Task GetTrashAsync_WithEmptyResult_ShouldReturnEmpty()
    {
        // Arrange
        _mockTeamRepo.Setup(r => r.GetDeletedAsync(1, 20, null))
            .ReturnsAsync((new List<Team>(), 0));
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(new List<TeamMember>());

        // Act
        var (resultItems, total) = await _service.GetTrashAsync(1, 20);

        // Assert
        total.Should().Be(0);
        resultItems.Should().BeEmpty();
    }

    #endregion

    #region GetTrashCountAsync

    [Fact]
    public async Task GetTrashCountAsync_ShouldReturnCount()
    {
        // Arrange
        _mockTeamRepo.Setup(r => r.CountDeletedAsync())
            .ReturnsAsync(5);

        // Act
        var result = await _service.GetTrashCountAsync();

        // Assert
        result.Should().Be(5);
    }

    [Fact]
    public async Task GetTrashCountAsync_WithNoDeleted_ShouldReturnZero()
    {
        // Arrange
        _mockTeamRepo.Setup(r => r.CountDeletedAsync())
            .ReturnsAsync(0);

        // Act
        var result = await _service.GetTrashCountAsync();

        // Assert
        result.Should().Be(0);
    }

    #endregion

    #region RestoreAsync

    [Fact]
    public async Task RestoreAsync_ShouldDelegateToRepository()
    {
        // Arrange
        _mockTeamRepo.Setup(r => r.RestoreAsync(1))
            .ReturnsAsync(true);

        // Act
        var result = await _service.RestoreAsync(1);

        // Assert
        result.Should().BeTrue();
        _mockTeamRepo.Verify(r => r.RestoreAsync(1), Times.Once);
    }

    [Fact]
    public async Task RestoreAsync_WithNonExisting_ShouldReturnFalse()
    {
        // Arrange
        _mockTeamRepo.Setup(r => r.RestoreAsync(999))
            .ReturnsAsync(false);

        // Act
        var result = await _service.RestoreAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region HardDeleteAsync

    [Fact]
    public async Task HardDeleteAsync_ShouldDelegateToRepository()
    {
        // Arrange
        _mockTeamRepo.Setup(r => r.HardDeleteAsync(1))
            .ReturnsAsync(true);

        // Act
        var result = await _service.HardDeleteAsync(1);

        // Assert
        result.Should().BeTrue();
        _mockTeamRepo.Verify(r => r.HardDeleteAsync(1), Times.Once);
    }

    [Fact]
    public async Task HardDeleteAsync_WithNonExisting_ShouldReturnFalse()
    {
        // Arrange
        _mockTeamRepo.Setup(r => r.HardDeleteAsync(999))
            .ReturnsAsync(false);

        // Act
        var result = await _service.HardDeleteAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region ClearTrashAsync

    [Fact]
    public async Task ClearTrashAsync_ShouldReturnDeletedCount()
    {
        // Arrange
        _mockTeamRepo.Setup(r => r.HardDeleteAllDeletedAsync())
            .ReturnsAsync(4);

        // Act
        var result = await _service.ClearTrashAsync();

        // Assert
        result.Should().Be(4);
        _mockTeamRepo.Verify(r => r.HardDeleteAllDeletedAsync(), Times.Once);
    }

    [Fact]
    public async Task ClearTrashAsync_WithNoDeleted_ShouldReturnZero()
    {
        // Arrange
        _mockTeamRepo.Setup(r => r.HardDeleteAllDeletedAsync())
            .ReturnsAsync(0);

        // Act
        var result = await _service.ClearTrashAsync();

        // Assert
        result.Should().Be(0);
    }

    #endregion

    #region Mapping (Mailbox name and Member role mapping)

    [Fact]
    public async Task GetPagedAsync_WithMailbox_ShouldMapMailboxName()
    {
        // Arrange
        var team = CreateTeam(1, "Team A", mailboxId: 5);
        team.Mailbox = new Mailbox { Id = 5, Name = "Sales" };

        _mockTeamRepo.Setup(r => r.GetPagedAsync(1, 20, null, null, null))
            .ReturnsAsync((new List<Team> { team }, 1));
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(new List<TeamMember>());

        var query = new TeamQuery { Page = 1, PageSize = 20 };

        // Act
        var (resultItems, _) = await _service.GetPagedAsync(query);

        // Assert
        var dto = resultItems.First();
        dto.MailboxName.Should().Be("Sales");
    }

    [Fact]
    public async Task GetPagedAsync_WithoutMailbox_ShouldHaveNullMailboxName()
    {
        // Arrange
        var team = CreateTeam(1, "Team A", mailboxId: null);

        _mockTeamRepo.Setup(r => r.GetPagedAsync(1, 20, null, null, null))
            .ReturnsAsync((new List<Team> { team }, 1));
        _mockMemberRepo.Setup(r => r.GetByTeamIdsAsync(It.IsAny<List<int>>()))
            .ReturnsAsync(new List<TeamMember>());

        var query = new TeamQuery { Page = 1, PageSize = 20 };

        // Act
        var (resultItems, _) = await _service.GetPagedAsync(query);

        // Assert
        var dto = resultItems.First();
        dto.MailboxName.Should().BeNull();
    }

    #endregion
}
