using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Infrastructure.Repositories;
using NoneadCSS.Infrastructure.Services;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// 审计日志写入隔离性集成测试。
/// 背景（生产日志 2026-09-01 15:20:19）：AuditActionFilter 记录 reply 审计时失败，
/// 异常为 "User cannot be tracked because another instance with the same key value for {'Id'} is already being tracked"。
/// 根因：审计写入与业务动作共用请求级 DbContext，业务动作先跟踪了 User 实例，
/// 审计 SaveChanges 触发 DetectChanges 导航修复时尝试附加同 Id 的第二个 User 实例 → IdentityMap 冲突。
/// 修复：AuditService.LogAsync 改用独立 DI 作用域（全新 DbContext）写入，与请求共享跟踪器完全隔离。
/// 以下用例锁定：即使主上下文存在同 Id 的已跟踪 User 与待冲突导航，审计日志仍能独立成功落库。
/// </summary>
public class AuditServiceIsolationIntegrationTests : IAsyncLifetime
{
    private string _dbFile = null!;
    private ServiceProvider _serviceProvider = null!;
    private NoneadCSSDbContext _mainContext = null!;
    private AuditService _auditService = null!;
    private int _mailboxId;

    public async Task InitializeAsync()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"audit-isolation-{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={_dbFile}";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:Key"] = "test-encryption-key-for-audit-hmac-signer"
            })
            .Build();

        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Development");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<NoneadCSSDbContext>(o => o.UseSqlite(connStr));
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(env.Object);
        services.AddScoped<AuditHmacSigner>();
        services.AddScoped<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<AuditService>();
        _serviceProvider = services.BuildServiceProvider();

        // 主上下文（模拟请求级 DbContext）
        _mainContext = _serviceProvider.GetRequiredService<NoneadCSSDbContext>();
        await _mainContext.Database.EnsureCreatedAsync();

        // Conversations.MailboxId 为必填 FK 列（EnsureCreated schema 含外键约束），
        // 必须先原生插入一条真实 Mailbox，否则插入会话时触发 SQLite Error 19: FOREIGN KEY constraint failed。
        _mailboxId = await InsertMailboxRowAsync();

        _auditService = _serviceProvider.GetRequiredService<AuditService>();
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbFile))
        {
            File.Delete(_dbFile);
        }
    }

    [Fact]
    public async Task LogAsync_WithTrackedUserConflictInMainContext_StillPersistsAuditRow()
    {
        // 1) 主上下文跟踪 User（模拟业务动作已跟踪的实体）
        var user = new User
        {
            FirstName = "客服",
            LastName = "甲",
            Email = "agent.audit@example.com"
        };
        await _mainContext.Users.AddAsync(user);
        await _mainContext.SaveChangesAsync();

        // 会话通过原生 SQL 插入（EF 插入会省略 Version 并发令牌列 → NOT NULL 失败，与生产迁移 schema 差异）
        var conversationId = await InsertConversationForAuditAsync(user.Id);

        // 主上下文加载会话（tracking）：UserId 已设置、User 导航为空
        var conversation = await _mainContext.Conversations.SingleAsync(c => c.Id == conversationId);

        // 2) 人为制造与生产日志相同的冲突状态：
        //    给已跟踪 Conversation 设置"同 Id 的第二个 User 实例"导航 → 下次主上下文 DetectChanges 必抛 IdentityMap 冲突
        conversation.User = new User { Id = user.Id, FirstName = "客服", LastName = "乙", Email = "dup@example.com" };

        // 3) 审计写入（独立 scope）必须不受主上下文污染影响，成功落库且不抛异常
        var act = async () => await _auditService.LogAsync(
            action: "reply",
            entityType: "Conversation",
            entityId: conversation.Id,
            userId: user.Id,
            ipAddress: "127.0.0.1",
            userAgent: "integration-test");

        await act.Should().NotThrowAsync("审计写入已隔离到独立 DbContext，不得受请求共享跟踪器污染（T-审计）");

        // 4) 用全新连接读取审计记录，确认已落库且带 HMAC 签名
        await using var freshContext = new NoneadCSSDbContext(
            new DbContextOptionsBuilder<NoneadCSSDbContext>().UseSqlite($"Data Source={_dbFile}").Options);
        var logs = await freshContext.AuditLogs
            .Where(l => l.EntityType == "Conversation" && l.EntityId == conversation.Id)
            .ToListAsync();
        logs.Should().ContainSingle();
        logs[0].Action.Should().Be("reply");
        logs[0].UserId.Should().Be(user.Id);
        logs[0].IpAddress.Should().Be("127.0.0.1");
        logs[0].HmacSignature.Should().NotBeNullOrWhiteSpace("审计记录必须带 HMAC 防篡改签名");

        // 5) 反向证明：主上下文确实处于"下一次 DetectChanges 即冲突"的污染态（忠实复现生产失败条件）
        var poisonedSave = async () => await _mainContext.SaveChangesAsync();
        await poisonedSave.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 原生 SQL 插入会话（PRAGMA 动态取全部"非空且无默认值"列并补值），
    /// 规避 EF 对 Version 等 ValueGeneratedOnAddOrUpdate 列在 INSERT 时省略导致的 NOT NULL 失败。
    /// </summary>
    private async Task<int> InsertConversationForAuditAsync(int userId)
    {
        var connection = _mainContext.Database.GetDbConnection();
        await _mainContext.Database.OpenConnectionAsync();

        var cols = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Conversations)";
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

        var allCols = cols.Union(new[] { "UserId" }).ToList();
        var colList = string.Join(", ", allCols.Select(c => $"\"{c}\""));
        var placeholders = string.Join(", ", allCols.Select((_, i) => $"$p{i}"));
        await using var insert = (SqliteCommand)connection.CreateCommand();
        insert.CommandText = $"INSERT INTO Conversations ({colList}) VALUES ({placeholders}); SELECT last_insert_rowid();";
        for (var i = 0; i < allCols.Count; i++)
        {
            insert.Parameters.AddWithValue($"$p{i}", ResolveAuditConversationValue(allCols[i], userId, _mailboxId));
        }
        return Convert.ToInt32(await insert.ExecuteScalarAsync());
    }

    private static object ResolveAuditConversationValue(string column, int userId, int mailboxId) => column switch
    {
        "Version" => 1,
        "MailboxId" => mailboxId,
        "UserId" => userId,
        "Number" => 9001,
        "Type" => (int)ConversationType.Chat,
        "SourceType" => (int)SourceType.Email,
        "State" => (int)ConversationState.Published,
        "Status" => (int)ConversationStatus.Active,
        "Priority" => 1,
        "CustomerEmail" => "customer@example.com",
        "Subject" => "审计隔离测试",
        "CreatedAt" or "UpdatedAt" or "LastReplyAt" => "2026-01-01T00:00:00.0000000",
        "ThreadsCount" or "RepliesCount" or "FolderId" or "AssigneeId" or "SlaPolicyId" => 0,
        _ => "audit-it-test"
    };

    /// <summary>
    /// 原生 SQL 插入一条真实 Mailbox（PRAGMA 动态取全部"非空且无默认值"列并补值），
    /// 为 Conversations.MailboxId 外键提供有效引用（同 WeChatCloseRatingLinkIntegrationTests 基建）。
    /// </summary>
    private async Task<int> InsertMailboxRowAsync()
    {
        var connection = _mainContext.Database.GetDbConnection();
        await _mainContext.Database.OpenConnectionAsync();

        var cols = new List<string>();
        await using (var cmd = connection.CreateCommand())
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
        await using var insert = (SqliteCommand)connection.CreateCommand();
        insert.CommandText = $"INSERT INTO Mailboxes ({colList}) VALUES ({placeholders})";
        for (var i = 0; i < cols.Count; i++)
        {
            insert.Parameters.AddWithValue($"$p{i}", ResolveMailboxDummyValue(cols[i]));
        }
        await insert.ExecuteNonQueryAsync();

        await using var idCmd = connection.CreateCommand();
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
        _ => "audit-it-mailbox"
    };

    [Fact]
    public async Task LogAsync_WithoutHttpContext_UsesExplicitUserId()
    {
        // 无 HttpContext（IHttpContextAccessor 默认返回 null）时，显式 userId 必须生效且落库
        var user = new User
        {
            FirstName = "系统",
            LastName = "用户",
            Email = "system.audit@example.com"
        };
        await _mainContext.Users.AddAsync(user);
        await _mainContext.SaveChangesAsync();

        await _auditService.LogAsync(
            action: "update",
            entityType: "Mailbox",
            entityId: 42,
            userId: user.Id);

        await using var freshContext = new NoneadCSSDbContext(
            new DbContextOptionsBuilder<NoneadCSSDbContext>().UseSqlite($"Data Source={_dbFile}").Options);
        var log = await freshContext.AuditLogs
            .FirstOrDefaultAsync(l => l.EntityType == "Mailbox" && l.EntityId == 42);
        log.Should().NotBeNull();
        log!.UserId.Should().Be(user.Id);
        log.Action.Should().Be("update");
    }
}
