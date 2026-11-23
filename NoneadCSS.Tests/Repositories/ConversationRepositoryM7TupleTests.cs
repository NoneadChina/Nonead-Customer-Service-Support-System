using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Infrastructure.Repositories;

namespace NoneadCSS.Tests.Repositories;

/// <summary>
/// M-7（Conversation 仓储分页接口返回元组规约）单元测试。
///
/// M-7 背景：之前 ConversationRepository.GetByUserIdAsync / GetByCustomerIdAsync
/// 返回 Task«IEnumerable«Conversation»»，调用方若要显示分页总数，需要再执行一次独立的 Count 查询，
/// 或在 EF Include 后进行客户端 Count，产生严重性能回归。
///
/// 整改后规约：两方法返回 (IEnumerable«Conversation» Items, int Total) 元组，
/// 并在 *BaseQuery 未 ApplyStandardIncludes 之前*执行 CountAsync，消除 Include 引起的性能退化。
/// 本文件同时补充了 IConversationRepository 接口签名级守卫（防回退到旧签名）。
///
/// 同类仓储（AuditLogRepository.GetByUserIdAsync 同样返回 (Items, Total) 元组，同步增加对照测试）。
/// </summary>
public class ConversationRepositoryM7TupleTests : IAsyncLifetime
{
    private NoneadCSSDbContext _context = null!;
    private ConversationRepository _repo = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseInMemoryDatabase(databaseName: $"ConvRepoM7_{Guid.NewGuid()}")
            .Options;
        _context = new NoneadCSSDbContext(options);
        _repo = new ConversationRepository(_context);

        // 构造 3 个 Customer 共 12 条会话，分配给 2 个 UserId，方便测试分页与总数。
        var customers = new[]
        {
            new Customer { FirstName = "C", LastName = "A" },
            new Customer { FirstName = "C", LastName = "B" },
            new Customer { FirstName = "C", LastName = "C" },
        };
        await _context.Customers.AddRangeAsync(customers);
        await _context.SaveChangesAsync();

        // userId=100: 7 条（其中 1 条 Deleted）
        // userId=200: 5 条（全部 Published）
        var seed = new List<Conversation>();

        for (var i = 0; i < 7; i++)
        {
            seed.Add(new Conversation
            {
                UserId = 100,
                CustomerId = customers[i % customers.Length].Id,
                Subject = $"A-{i}",
                State = (i == 3) ? ConversationState.Deleted : ConversationState.Published,
                Status = (i == 0) ? ConversationStatus.Closed : ConversationStatus.Active,
                CreatedAt = DateTime.UtcNow.AddHours(-i),
                LastReplyAt = DateTime.UtcNow.AddMinutes(-i * 10),
                SourceType = SourceType.Email,
            });
        }
        for (var i = 0; i < 5; i++)
        {
            seed.Add(new Conversation
            {
                UserId = 200,
                CustomerId = customers[i % customers.Length].Id,
                Subject = $"B-{i}",
                State = ConversationState.Published,
                Status = ConversationStatus.Active,
                CreatedAt = DateTime.UtcNow.AddHours(-7 - i),
                LastReplyAt = DateTime.UtcNow.AddMinutes(-(7 + i) * 10),
                SourceType = SourceType.Web,
            });
        }

        await _context.Conversations.AddRangeAsync(seed);
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    #region M-7.1 IConversationRepository 接口签名守卫（防回退）

    [Fact]
    public void ConversationRepository_IConversationRepository_MustExpose_TupleReturn()
    {
        var iface = typeof(NoneadCSS.Domain.Repositories.IConversationRepository);
        var byUserId = iface.GetMethod(nameof(NoneadCSS.Domain.Repositories.IConversationRepository.GetByUserIdAsync));
        var byCustomerId = iface.GetMethod(nameof(NoneadCSS.Domain.Repositories.IConversationRepository.GetByCustomerIdAsync));

        byUserId.Should().NotBeNull();
        byCustomerId.Should().NotBeNull();

        var expectedReturn = typeof(Task<(IEnumerable<Conversation> Items, int Total)>);

        byUserId!.ReturnType.Should().Be(expectedReturn,
            "M-7: IConversationRepository.GetByUserIdAsync 返回类型必须为 Task<(Items, Total)>；" +
            "若被改为 IEnumerable 单项，分页总数调用方将被迫二次查询，属于回归。");
        byCustomerId!.ReturnType.Should().Be(expectedReturn,
            "M-7: IConversationRepository.GetByCustomerIdAsync 返回类型必须为 Task<(Items, Total)>。");

        var concrete = typeof(ConversationRepository).GetMethod(nameof(ConversationRepository.GetByUserIdAsync));
        concrete.Should().NotBeNull();
        concrete!.ReturnType.Should().Be(expectedReturn);
    }

    #endregion

    #region M-7.2 GetByUserIdAsync：Total 不包含 Deleted 且分页正确

    [Fact]
    public async Task GetByUserIdAsync_Total_ExcludesDeleted_AndIsIndependentOfPageSize()
    {
        // userId=100: 7 条，其中 1 条 Deleted → 总数 = 6
        var page1 = await _repo.GetByUserIdAsync(userId: 100, page: 1, pageSize: 2);
        var page2 = await _repo.GetByUserIdAsync(userId: 100, page: 2, pageSize: 2);

        page1.Total.Should().Be(6, "UserId=100 应存在 6 条未删除会话（共 7 条，减去 1 条 Deleted）。");
        page2.Total.Should().Be(6, "分页参数不得影响 Total。");
        page1.Items.Should().HaveCount(2);
        page2.Items.Should().HaveCount(2);
        page1.Items.Should().OnlyContain(c => c.State != ConversationState.Deleted);
        page2.Items.Should().OnlyContain(c => c.State != ConversationState.Deleted);
    }

    [Fact]
    public async Task GetByUserIdAsync_LastPage_MayReturnFewerThanPageSize()
    {
        // 6 条 / pageSize=4 → 第 2 页应返回 2 条
        var (items, total) = await _repo.GetByUserIdAsync(userId: 100, page: 2, pageSize: 4);

        total.Should().Be(6);
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByUserIdAsync_OutOfRangePage_ReturnsEmptyList_StableTotal()
    {
        var (items, total) = await _repo.GetByUserIdAsync(userId: 100, page: 999, pageSize: 5);

        total.Should().Be(6);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByUserIdAsync_UnknownUserId_ReturnsZeroTotal_EmptyItems()
    {
        var (items, total) = await _repo.GetByUserIdAsync(userId: -1, page: 1, pageSize: 10);
        total.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByUserIdAsync_Items_OrderedBy_LastReplyAtDesc()
    {
        var (items, _) = await _repo.GetByUserIdAsync(userId: 100, page: 1, pageSize: 100);
        var list = items.ToList();
        var expectedOrder = list
            .OrderByDescending(c => c.LastReplyAt ?? c.CreatedAt)
            .Select(c => c.Id)
            .ToList();
        list.Select(c => c.Id).Should().Equal(expectedOrder,
            "M-7: 排序必须按 LastReplyAt（空时回退 CreatedAt）倒序，保证调用方 UI 列表和 Total 对齐。");
    }

    #endregion

    #region M-7.3 GetByCustomerIdAsync：Total 不包含 Deleted 且分页正确

    [Fact]
    public async Task GetByCustomerIdAsync_Total_StableAcrossPages()
    {
        // 先挑选有会话的 customerId
        var customerId = (await _context.Customers
            .Select(c => new { c.Id, Count = _context.Conversations.Count(x => x.CustomerId == c.Id && x.State != ConversationState.Deleted) })
            .OrderByDescending(x => x.Count)
            .FirstAsync()).Id;

        var p1 = await _repo.GetByCustomerIdAsync(customerId, page: 1, pageSize: 1);
        var p2 = await _repo.GetByCustomerIdAsync(customerId, page: 2, pageSize: 1);

        p1.Total.Should().Be(p2.Total, "GetByCustomerIdAsync Total 在同一 customerId 下必须稳定。");
        p1.Total.Should().BeGreaterThan(0);
        p1.Items.Should().HaveCount(1);
        p2.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetByCustomerIdAsync_UnknownCustomer_ReturnsZero()
    {
        var (items, total) = await _repo.GetByCustomerIdAsync(customerId: -1, page: 1, pageSize: 5);
        total.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByCustomerIdAsync_ExcludesDeletedStates()
    {
        var all = await _context.Conversations.ToListAsync();
        var deleted = all.Single(c => c.State == ConversationState.Deleted);
        var deletedCid = deleted.CustomerId!.Value;
        var (items, total) = await _repo.GetByCustomerIdAsync(deletedCid, page: 1, pageSize: 100);

        var deletedSameCustomer = all.Count(c =>
            c.CustomerId == deletedCid && c.State == ConversationState.Deleted);
        var aliveSameCustomer = all.Count(c =>
            c.CustomerId == deletedCid && c.State != ConversationState.Deleted);

        total.Should().Be(aliveSameCustomer);
        items.Count().Should().Be(aliveSameCustomer);
        if (deletedSameCustomer > 0)
        {
            // 若存在同 CustomerId 下 deleted 的会话，items 中不能包含这些 deleted
            items.Should().OnlyContain(c => c.State != ConversationState.Deleted);
        }
    }

    #endregion

    #region M-7.4 AuditLogRepository.GetByUserIdAsync 同为 M-7 风格，契约回归测试

    [Fact]
    public void AuditLogRepository_GetByUserIdAsync_ReturnType_MustBeTuple()
    {
        var m = typeof(AuditLogRepository).GetMethod(nameof(AuditLogRepository.GetByUserIdAsync));
        m.Should().NotBeNull();

        var expected = typeof(Task<(IEnumerable<AuditLog> Items, int Total)>);
        m!.ReturnType.Should().Be(expected,
            "M-7: AuditLogRepository.GetByUserIdAsync 也被改为 (Items, Total) 元组，调用方依赖此签名，不可回退。");
    }

    #endregion
}
