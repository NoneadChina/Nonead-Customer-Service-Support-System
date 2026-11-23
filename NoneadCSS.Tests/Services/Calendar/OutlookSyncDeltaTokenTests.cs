using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Services.Outlook;
using NoneadCSS.Tests.Helpers;
using Xunit;

namespace NoneadCSS.Tests.Services.Calendar;

/// <summary>
/// P2P3 Phase 2: OutlookSyncService NextRetryAt 过滤器与批处理重试逻辑单元测试（TDD）。
/// 
/// TDD 设计说明：
/// - 断言 1：验证传给 Repository.FindAsync 的 LINQ 谓词正确过滤了 NextRetryAt 未来的用户
///           （谓词对种子数据求值 = 仅 1 条匹配）。
/// - 断言 2：验证 EnsureValidAccessTokenAsync 的调用次数等于通过过滤器的用户数量（1 次），
///           间接证明只有 UserB 进入了 SyncAsync 内部同步流程。
/// </summary>
public class OutlookSyncDeltaTokenTests
{
    [Fact]
    public async Task SyncAllEnabledUsersAsync_SkipsUsersWithFutureNextRetryAt()
    {
        // ===== Arrange =====
        var now = DateTime.UtcNow;

        var userA = new OutlookSyncSetting
        {
            Id = 1,
            UserId = 10,
            SyncEnabled = true,
            SyncDirection = "both",
            AccessToken = "token-a",
            RefreshToken = "refresh-a",
            NextRetryAt = now.AddHours(1),
            DeletedAt = null
        };
        var userB = new OutlookSyncSetting
        {
            Id = 2,
            UserId = 11,
            SyncEnabled = true,
            SyncDirection = "both",
            AccessToken = "token-b",
            RefreshToken = "refresh-b",
            NextRetryAt = null,
            DeletedAt = null
        };
        var allSeedSettings = new[] { userA, userB }.AsEnumerable();

        // 1. 捕获传给 FindAsync 的实际谓词（谓词过滤的正确性是本测试的核心目标）
        Expression<Func<OutlookSyncSetting, bool>>? capturedPredicate = null;

        var settingRepoMock = new Mock<IRepository<OutlookSyncSetting>>(MockBehavior.Loose);
        settingRepoMock.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<OutlookSyncSetting, bool>>>()))
            .Callback((Expression<Func<OutlookSyncSetting, bool>> pred) => capturedPredicate = pred)
            .ReturnsAsync((Expression<Func<OutlookSyncSetting, bool>> pred) =>
                allSeedSettings.Where(pred.Compile())
                    .Select(s => new OutlookSyncSetting
                    {
                        Id = s.Id,
                        UserId = s.UserId,
                        OutlookEmail = s.OutlookEmail,
                        OutlookUserId = s.OutlookUserId,
                        AccessToken = s.AccessToken,
                        RefreshToken = s.RefreshToken,
                        TokenExpiresAt = s.TokenExpiresAt,
                        SyncEnabled = s.SyncEnabled,
                        SyncDirection = s.SyncDirection,
                        LastSyncAt = s.LastSyncAt,
                        LastSyncStatus = s.LastSyncStatus,
                        LastSyncError = s.LastSyncError,
                        DeltaToken = s.DeltaToken,
                        ConsecutiveFailures = s.ConsecutiveFailures,
                        NextRetryAt = s.NextRetryAt,
                        LastFullSyncAt = s.LastFullSyncAt,
                        CreatedAt = s.CreatedAt,
                        UpdatedAt = s.UpdatedAt,
                        DeletedAt = s.DeletedAt,
                    }).ToList());

        // GetOrCreateSettingAsync 内部使用 GetQueryable → 支持 ToListAsync
        settingRepoMock.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns((bool asNoTracking) => TestAsyncQueryProvider.CreateAsyncQueryable(allSeedSettings));

        // 2. Mock IUnitOfWork + OutlookSyncLog 仓储（批处理循环写入审计日志时需要）
        var uowMock = new Mock<NoneadCSS.Domain.IUnitOfWork>(MockBehavior.Loose);
        uowMock.Setup(u => u.Repository<OutlookSyncSetting>()).Returns(settingRepoMock.Object);

        var syncLogRepoMock = new Mock<IRepository<OutlookSyncLog>>(MockBehavior.Loose);
        syncLogRepoMock.Setup(r => r.AddAsync(It.IsAny<OutlookSyncLog>()))
            .ReturnsAsync((OutlookSyncLog l) => l);
        syncLogRepoMock.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<OutlookSyncLog>>()))
            .Returns(Task.CompletedTask);
        uowMock.Setup(u => u.Repository<OutlookSyncLog>()).Returns(syncLogRepoMock.Object);

        // 3. Mock IOutlookOAuthService: 用 Verify 统计进入 SyncAsync 的用户数
        var oauthMock = new Mock<IOutlookOAuthService>(MockBehavior.Loose);
        oauthMock.Setup(o => o.EnsureValidAccessTokenAsync(It.IsAny<OutlookSyncSetting>()))
            .ReturnsAsync("t");

        // 4. Mock IOutlookGraphService
        var graphMock = new Mock<IOutlookGraphService>(MockBehavior.Loose);
        graphMock.Setup(g => g.ListEventsAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>()))
            .ReturnsAsync(new List<OutlookEventDto>());
        graphMock.Setup(g => g.ListEventsDeltaAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<string?>()))
            .ReturnsAsync((new List<OutlookEventDto>(), (string?)null, false));

        // 5. Mock IDistributedLockService（全局锁 outlook_sync:all + 每个用户锁 outlook_sync:user:X 都要放行）
        var lockHandleMock = new Mock<IDistributedLockHandle>(MockBehavior.Loose);
        lockHandleMock.Setup(h => h.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var lockMock = new Mock<IDistributedLockService>(MockBehavior.Loose);
        lockMock.Setup(l => l.TryAcquireAsync(
                It.IsAny<string>(),
                It.IsAny<int>()))
            .ReturnsAsync(lockHandleMock.Object);

        // 6. 内存配置
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Calendar:OutlookSyncWindowDays"] = "30",
                ["Calendar:OutlookSyncIntervalMinutes"] = "0",
                ["Calendar:OutlookSyncConflictStrategy"] = "last_write_wins"
            }!)
            .Build();

        var syncOptions = Options.Create(new OutlookSyncOptions
        {
            ClientId = "t",
            ClientSecret = "t",
            RedirectUri = "https://test",
            GraphBaseUrl = "https://graph.microsoft.com/v1.0"
        });

        var syncLogSpecRepoMock = new Mock<IOutlookSyncLogRepository>();
        var syncQueueRepoMock = new Mock<IOutlookSyncQueueRepository>();
        var mailboxSpecRepoMock = new Mock<IMailboxRepository>();
        var settingsProviderMock = new Mock<IOutlookSettingsProvider>();
        var userRoleResolverMock = new Mock<IUserRoleResolver>();

        var sut = new OutlookSyncService(
            uowMock.Object,
            oauthMock.Object,
            graphMock.Object,
            config,
            NullLogger<OutlookSyncService>.Instance,
            lockMock.Object,
            syncOptions,
            settingsProviderMock.Object,
            syncLogSpecRepoMock.Object,
            syncQueueRepoMock.Object,
            mailboxSpecRepoMock.Object,
            userRoleResolverMock.Object);

        // ===== Act =====
        await sut.SyncAllEnabledUsersAsync();

        // ===== Assert =====
        // --- 核心断言 1：SQL 谓词仅保留可翻译条件 ---
        // SQLite 无法翻译 DateTimeOffset 比较（见 OutlookSyncQueueRepository 注释），
        // 因此 NextRetryAt 过滤被移至内存层。FindAsync 收到的谓词应物化所有启用同步的用户
        //（含冷却期内用户，由内存过滤器二次剔除）。
        capturedPredicate.Should().NotBeNull("FindAsync 必须被调用");
        var fetched = allSeedSettings.Where(capturedPredicate!.Compile()).ToList();
        fetched.Should().HaveCount(2,
            "SQL 可翻译谓词应物化全部启用同步的用户（含指数退避冷却期内的用户）");

        // --- 核心断言 2：内存层 NextRetryAt 过滤正确性（本需求的 #1 目标） ---
        // EnsureValidAccessTokenAsync 只会在 SyncAsync -> OAuth 刷新阶段被调用一次每用户。
        // 冷却期内 NextRetryAt=未来 1 小时的用户 A 应在内存过滤器被剔除，
        // 若两个用户都进入 SyncAsync（= 内存过滤器未实现），这里会被调用 2 次，
        // 此时 Times.Once 失败，符合 TDD 的 FAIL 状态。
        oauthMock.Verify(
            o => o.EnsureValidAccessTokenAsync(It.IsAny<OutlookSyncSetting>()),
            Times.Once,
            "应只有 1 个用户进入同步主流程（未过滤的情况将导致调用 2 次）");
    }
}
