using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NoneadCSS.Infrastructure.Persistence;

/// <summary>
/// 乐观并发列（<c>Version</c>）的 provider 差异化映射。
/// <para>
/// 背景（2026-11-19，实测）：<c>Mailbox</c>/<c>Conversation</c>/<c>ConversationThread</c> 的
/// <c>Version</c> 在 SQL Server 是真正的 <c>rowversion</c>（由数据库生成），而历史迁移
/// <c>20260726115230_AddPasswordResetTokenIndexes</c> 给 SQLite 建的是
/// <c>INTEGER NOT NULL DEFAULT 0</c>，插入时 EF 省略该列、由库默认值兜底。
/// </para>
/// <para>
/// 但「空库按当前 EF 模型建库」（<see cref="FreshDatabaseBootstrap"/>，即 EnsureCreated）
/// 用的是模型而不是迁移，SQLite 下模型只生成 <c>INTEGER NOT NULL</c>（无默认值），
/// 于是种子数据插入即报 <c>SQLite Error 19: NOT NULL constraint failed: Mailboxes.Version</c>，
/// 进程在 Kestrel 监听前崩掉 —— 前端看到的就是「获取验证码失败」。
/// </para>
/// <para>
/// 处理：SQLite 下退化为「带默认值的并发令牌」（列仍有默认值 0，EF 每次更新都把它带进
/// WHERE 做比对）；SQL Server 保持 rowversion 语义不变。注意 SQLite 下该列不会自动自增，
/// 因此真正的行版本并发检测弱于 SQL Server —— 这是 SQLite 无 rowversion 的固有限制，
/// 已在交付说明里记录，需要强并发语义时请使用 SQL Server 部署。
/// </para>
/// </summary>
public static class VersionPropertyMappingExtensions
{
    public static PropertyBuilder<int> ApplyVersionMapping(this PropertyBuilder<int> property, bool isSqlite)
        => isSqlite
            ? property.IsConcurrencyToken().HasDefaultValue(0)
            : property.IsRowVersion().ValueGeneratedOnAddOrUpdate();

    public static bool IsSqliteProvider(this DbContext context)
        => context.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
