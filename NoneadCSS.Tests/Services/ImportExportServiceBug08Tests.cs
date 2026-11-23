using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure;
using NoneadCSS.Infrastructure.Persistence;
using System.Linq;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// ImportExportService BUG-08 回归测试 — 软删除标签干扰重复检查
///
/// BUG 描述：
///   Tag 实体实现 ISoftDelete，DbContext 在 OnModelCreating 中通过
///   ApplyGlobalSoftDeleteFilters 自动应用 HasQueryFilter(e => e.DeletedAt == null)。
///   最初 BUG-08 修复使用 GetQueryable().ToListAsync() 预加载所有标签做重复检查，
///   但全局查询过滤器会过滤掉软删除记录，导致：
///     1. 导入环境已存在被软删除的同名标签（DeletedAt != null）
///     2. 重复检查集合里查不到该 key（被全局过滤器排除）
///     3. ImportTag 误判为"不存在"，创建一条新的同名标签
///     4. DB 出现两条同名记录（一条软删除 + 一条活跃），后续查询出现歧义
///
/// 修复方案：在预加载 existingTagKeys 时调用 IgnoreQueryFilters()，
///   绕过全局软删除过滤器，将所有标签（含软删除）加载到重复检查集合。
/// </summary>
public class ImportExportServiceBug08Tests : IAsyncLifetime
{
    private NoneadCSSDbContext _context = null!;
    private UnitOfWork _unitOfWork = null!;
    private ImportExportService _service = null!;
    private Mock<ICurrentUserAccessor> _mockCurrentUserAccessor = null!;
    private Mock<IUserRoleResolver> _mockUserRoleResolver = null!;

    public Task InitializeAsync()
    {
        // 使用 InMemory + 抑制事务警告（ImportExportService 内部使用事务）
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseInMemoryDatabase(databaseName: $"ImportExportBug08TestDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _context = new NoneadCSSDbContext(options);
        _unitOfWork = new UnitOfWork(_context, Shared.TestServiceProviderBuilder.Build(_context));

        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();
        _mockCurrentUserAccessor.SetupGet(x => x.UserId).Returns(1);
        _mockCurrentUserAccessor.SetupGet(x => x.IsSuperAdmin).Returns(true);
        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(It.IsAny<int?>()))
            .ReturnsAsync(true);
        _mockCurrentUserAccessor.Setup(x => x.GetAccessibleMailboxIdsAsync())
            .ReturnsAsync((int[]?)null);

        _mockUserRoleResolver = new Mock<IUserRoleResolver>();
        _mockUserRoleResolver.Setup(x => x.IsSuperAdminAsync(It.IsAny<int>()))
            .ReturnsAsync(true);

        _service = new ImportExportService(
            _unitOfWork,
            NullLogger<ImportExportService>.Instance,
            _mockCurrentUserAccessor.Object,
            _mockUserRoleResolver.Object);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    /// <summary>
    /// 创建一个软删除的标签（DeletedAt != null），用于模拟 BUG-08 场景。
    /// </summary>
    private async Task<Tag> CreateSoftDeletedTagAsync(string name, int? mailboxId = null)
    {
        var tag = new Tag
        {
            Name = name,
            Color = "#ff0000",
            MailboxId = mailboxId,
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            UpdatedAt = DateTime.UtcNow.AddDays(-1),
            DeletedAt = DateTime.UtcNow.AddDays(-1) // 已软删除
        };
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();
        return tag;
    }

    /// <summary>
    /// 创建一个活跃的标签（DeletedAt = null）。
    /// </summary>
    private async Task<Tag> CreateActiveTagAsync(string name, int? mailboxId = null)
    {
        var tag = new Tag
        {
            Name = name,
            Color = "#00b7ff",
            MailboxId = mailboxId,
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            UpdatedAt = DateTime.UtcNow
        };
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();
        return tag;
    }

    /// <summary>
    /// 构造只包含 tags 段的导入 JSON。
    /// </summary>
    private static string BuildTagsImportJson(params (string name, string color, int? mailboxId)[] tags)
    {
        var items = tags.Select(t => $@"{{""name"":""{t.name}"",""color"":""{t.color}"",{(t.mailboxId.HasValue ? $@"""mailboxId"":{t.mailboxId.Value}," : string.Empty)}""createdAt"":""2026-01-01T00:00:00Z""}}");
        return $@"{{""tags"":[{string.Join(",", items)}]}}";
    }

    /// <summary>
    /// 统计当前数据库中的标签总数（含软删除）。
    /// </summary>
    private async Task<int> CountAllTagsAsync()
    {
        return await _context.Tags.IgnoreQueryFilters().CountAsync();
    }

    /// <summary>
    /// 统计活跃标签数（不含软删除）。
    /// </summary>
    private async Task<int> CountActiveTagsAsync()
    {
        return await _context.Tags.CountAsync();
    }

    #region BUG-08 核心修复：IgnoreQueryFilters 使软删除标签纳入重复检查

    /// <summary>
    /// BUG-08 修复点：导入同名标签（与已存在的软删除标签同名）时，
    /// 必须跳过创建，避免产生重复记录。
    ///
    /// 若未调用 IgnoreQueryFilters()，全局过滤器会排除软删除标签，
    /// 导致重复检查集合中查不到该 key，ImportTag 会误创建新标签。
    /// </summary>
    [Fact]
    public async Task BUG08_ImportTag_WithSameNameAsSoftDeletedTag_SkipsCreation()
    {
        // Arrange — 已存在一个软删除的"紧急"标签
        await CreateSoftDeletedTagAsync("紧急", mailboxId: null);
        var initialTotalTags = await CountAllTagsAsync();
        initialTotalTags.Should().Be(1, "前置条件：DB 中已有 1 条软删除标签");

        // 导入一个同名的"紧急"标签
        var request = new ImportRequest
        {
            Format = "json",
            Data = BuildTagsImportJson(("紧急", "#00b7ff", null))
        };

        // Act
        var response = await _service.ImportDataAsync(request);

        // Assert — 不应创建新标签
        response.Success.Should().BeTrue();
        var totalAfter = await CountAllTagsAsync();
        totalAfter.Should().Be(initialTotalTags,
            because: "已存在同名（含软删除）标签，ImportTag 必须跳过创建。BUG-08 修复目标。");

        // 活跃标签数应保持为 0（原有的同名标签是软删除的）
        var activeAfter = await CountActiveTagsAsync();
        activeAfter.Should().Be(0,
            because: "不应新增活跃标签；原软删除标签不应被导入流程隐式恢复");
    }

    /// <summary>
    /// BUG-08 修复点：导入同名标签（与已存在的活跃标签同名）时，
    /// 必须跳过创建。这是基础回归用例，确保修复未破坏正常去重逻辑。
    /// </summary>
    [Fact]
    public async Task BUG08_ImportTag_WithSameNameAsActiveTag_SkipsCreation()
    {
        // Arrange — 已存在一个活跃的"VIP"标签
        await CreateActiveTagAsync("VIP", mailboxId: null);

        // 导入一个同名的"VIP"标签
        var request = new ImportRequest
        {
            Format = "json",
            Data = BuildTagsImportJson(("VIP", "#ff0000", null))
        };

        // Act
        var response = await _service.ImportDataAsync(request);

        // Assert — 不应创建新标签
        response.Success.Should().BeTrue();
        var activeAfter = await CountActiveTagsAsync();
        activeAfter.Should().Be(1,
            because: "活跃标签数应保持为 1（原有），不应重复创建");

        var totalAfter = await CountAllTagsAsync();
        totalAfter.Should().Be(1, "总标签数应保持为 1");
    }

    /// <summary>
    /// BUG-08 修复点：邮箱作用域隔离 — 不同 mailboxId 下同名标签不视为重复。
    /// 验证 IgnoreQueryFilters 在多邮箱场景下不会错误合并。
    /// </summary>
    [Fact]
    public async Task BUG08_ImportTag_DifferentMailbox_SameName_NotDuplicate()
    {
        // Arrange — mailbox 1 已有活跃标签"退款"
        await CreateActiveTagAsync("退款", mailboxId: 1);
        // mailbox 2 已有软删除标签"退款"
        await CreateSoftDeletedTagAsync("退款", mailboxId: 2);

        // 导入 mailbox 3 的"退款"标签 — 应创建（key 不同）
        var request = new ImportRequest
        {
            Format = "json",
            Data = BuildTagsImportJson(("退款", "#00b7ff", 3))
        };

        // Act
        var response = await _service.ImportDataAsync(request);

        // Assert — 应创建一条新标签
        response.Success.Should().BeTrue();
        var totalAfter = await CountAllTagsAsync();
        totalAfter.Should().Be(3,
            because: "mailboxId=3 的'退款'与现有记录 mailboxId 不同，应创建新标签");
    }

    /// <summary>
    /// BUG-08 修复点：导入一批标签时，混合场景（同名跳过 + 新建 + 跨邮箱新建）正确处理。
    /// </summary>
    [Fact]
    public async Task BUG08_ImportTag_MixedBatch_HandlesDuplicatesAndNewTags()
    {
        // Arrange
        // - 全局作用域：已存在活跃标签"Bug"
        await CreateActiveTagAsync("Bug", mailboxId: null);
        // - mailbox 1：已存在软删除标签"Feature"
        await CreateSoftDeletedTagAsync("Feature", mailboxId: 1);

        // 导入批次：
        //   "Bug" (null mailbox) → 与活跃标签同名，应跳过
        //   "Feature" (mailbox 1) → 与软删除标签同名，应跳过（BUG-08 核心修复点）
        //   "Feature" (mailbox 2) → 不同 mailbox，应创建
        //   "Urgent" (null mailbox) → 全新标签，应创建
        var request = new ImportRequest
        {
            Format = "json",
            Data = BuildTagsImportJson(
                ("Bug", "#ff0000", null),
                ("Feature", "#00ff00", 1),
                ("Feature", "#00ff00", 2),
                ("Urgent", "#ff8800", null))
        };

        // Act
        var response = await _service.ImportDataAsync(request);

        // Assert
        response.Success.Should().BeTrue();
        // 注：ImportedCount 是"处理无异常的条目数"，包含跳过的重复项，
        // 不能反映实际新增数；BUG-08 修复正确性应以 DB 状态为准。
        var totalAfter = await CountAllTagsAsync();
        totalAfter.Should().Be(4,
            because: "原有 2 条（1 活跃 Bug + 1 软删除 Feature）+ 新增 2 条（Feature@mailbox2 + Urgent）= 4 条");

        var activeAfter = await CountActiveTagsAsync();
        activeAfter.Should().Be(3,
            because: "原有 1 条活跃（Bug）+ 新增 2 条活跃（Feature@mailbox2 + Urgent）= 3 条活跃；" +
                     "软删除的 Feature@mailbox1 不应被恢复");

        // 确认没有产生重复 key
        var allTagsAfter = await _context.Tags.IgnoreQueryFilters().ToListAsync();
        var keyCounts = allTagsAfter.GroupBy(t => $"{t.MailboxId}_{t.Name}")
            .ToDictionary(g => g.Key, g => g.Count());
        keyCounts.Values.Should().AllSatisfy(c => c.Should().Be(1,
            because: "任何 key（mailboxId + name）都不应出现重复记录；BUG-08 修复目标"));
    }

    #endregion

    #region BUG-08 边界场景：全局查询过滤器验证

    /// <summary>
    /// BUG-08 修复验证：确认 IgnoreQueryFilters() 的必要性。
    /// 模拟未应用修复（仅用 GetQueryable()）时会漏掉软删除标签的场景，
    /// 作为对照说明修复正确性。
    ///
    /// 此测试不直接调用 ImportExportService，而是验证 Tag 实体的全局过滤器行为：
    ///   - 默认查询排除软删除记录
    ///   - IgnoreQueryFilters() 可绕过过滤器
    /// </summary>
    [Fact]
    public async Task BUG08_GlobalQueryFilter_ExcludesSoftDeletedTags_RequiresIgnoreQueryFilters()
    {
        // Arrange — 创建 1 活跃 + 2 软删除标签
        await CreateActiveTagAsync("Active1", mailboxId: null);
        await CreateSoftDeletedTagAsync("Deleted1", mailboxId: null);
        await CreateSoftDeletedTagAsync("Deleted2", mailboxId: 5);

        // Act & Assert
        // 1) 默认查询（应用全局过滤器）：仅返回 1 条活跃记录
        var defaultQuery = await _unitOfWork.Tags.GetQueryable().ToListAsync();
        defaultQuery.Should().HaveCount(1,
            because: "默认查询应用全局软删除过滤器，仅返回活跃标签");

        // 2) IgnoreQueryFilters：返回全部 3 条记录（活跃 + 软删除）
        var ignoreFilterQuery = await _unitOfWork.Tags.GetQueryable()
            .IgnoreQueryFilters()
            .ToListAsync();
        ignoreFilterQuery.Should().HaveCount(3,
            because: "IgnoreQueryFilters() 必须绕过全局软删除过滤器，返回所有标签（含软删除）。" +
                     "BUG-08 修复依赖此行为来加载完整的重复检查集合。");

        // 3) 确认 IgnoreQueryFilters 包含软删除记录
        ignoreFilterQuery.Should().Contain(t => t.Name == "Deleted1" && t.DeletedAt != null);
        ignoreFilterQuery.Should().Contain(t => t.Name == "Deleted2" && t.DeletedAt != null);
    }

    #endregion

    #region BUG-08 与 BUG-02 协同：导出过滤软删除 + 导入检查软删除

    /// <summary>
    /// BUG-08 与 BUG-02 协同回归：导出应过滤软删除标签（BUG-02），
    /// 导入应识别所有标签含软删除（BUG-08），两者互不冲突。
    ///
    /// 场景：环境 A 导出标签 → 导入环境 B（B 中已有同名软删除标签）
    ///   - 导出数据中不应包含软删除标签（BUG-02）
    ///   - 导入时环境 B 的软删除标签应被识别为重复并跳过（BUG-08）
    /// </summary>
    [Fact]
    public async Task BUG08_ExportFiltersSoftDeleted_ImportDetectsSoftDeleted_Cooperate()
    {
        // Arrange — 环境 A（当前 DB）有 1 活跃 + 1 软删除标签
        await CreateActiveTagAsync("CoopActive", mailboxId: 10);
        await CreateSoftDeletedTagAsync("CoopDeleted", mailboxId: 10);

        // 1) 导出：BUG-02 修复应过滤掉软删除标签
        var exportData = await _service.ExportDataAsync(new ExportRequest
        {
            EntityTypes = ["tags"],
            Format = "json"
        });
        var exportJson = System.Text.Encoding.UTF8.GetString(exportData);
        exportJson.Should().Contain("CoopActive");
        exportJson.Should().NotContain("CoopDeleted",
            because: "BUG-02 修复：导出应过滤软删除标签，导出数据中不应出现 CoopDeleted");

        // 2) 导入：环境 B 已有同名软删除标签（模拟另一环境导入相同数据集）
        //    再次导入同名的"CoopActive"应跳过（与活跃标签同名）
        var importRequest = new ImportRequest
        {
            Format = "json",
            Data = BuildTagsImportJson(("CoopActive", "#00b7ff", 10))
        };
        var importResponse = await _service.ImportDataAsync(importRequest);
        importResponse.Success.Should().BeTrue();
        // 注：ImportedCount 反映"处理无异常的条目数"，含被跳过的重复项，
        // 不能作为新增数的判据；BUG-08 修复正确性以 DB 状态为准。

        // 3) 最终验证：总标签数不变（2 条：1 活跃 + 1 软删除）
        var totalAfter = await CountAllTagsAsync();
        totalAfter.Should().Be(2,
            because: "导入不应新增任何标签；环境保持 1 活跃 + 1 软删除 = 2 条");
    }

    #endregion
}
