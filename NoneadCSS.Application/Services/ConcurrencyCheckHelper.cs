using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Exceptions;
using System.Runtime.CompilerServices;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 通用乐观锁 If-Match（RowVersion base64）校验助手。
/// 所有模块在执行实体更新前调用 Check 方法；不匹配时抛出通用 ConcurrencyException，
/// 由 GlobalExceptionMiddleware 映射为 409 + entityType/entityId/latest。
/// </summary>
public static class ConcurrencyCheckHelper
{
    /// <summary>
    /// 显式校验 RowVersion。
    /// </summary>
    /// <param name="entity">当前数据库实体</param>
    /// <param name="rowVersion">实体当前 RowVersion（传入 entity.RowVersion 本身即可）</param>
    /// <param name="ifMatch">请求中 If-Match（RowVersion base64 字符串）；为空或非法直接跳过以保持旧客户端兼容</param>
    /// <param name="entityType">实体类型名（建议用简单名，如 ChatSession、Customer、User 等），会写入 409 响应字段</param>
    /// <param name="entityId">实体主键</param>
    /// <param name="latestFactory">最新快照构造工厂，冲突时用来填充 409.latest（已脱敏的 DTO 更安全；若实体本身无敏感字段可直接传实体）</param>
    /// <param name="logger">可选日志；仅冲突与非法值时写日志</param>
    /// <param name="caller">调用方法名，便于日志追踪</param>
    /// <exception cref="ConcurrencyException">版本不一致时抛出</exception>
    public static void Check(
        object entity,
        byte[]? rowVersion,
        string? ifMatch,
        string entityType,
        int entityId,
        Func<object?> latestFactory,
        ILogger? logger = null,
        [CallerMemberName] string caller = "")
    {
        _ = entity; // 保留签名以避免 API 误用：必须确实对已加载的实体做校验

        if (string.IsNullOrEmpty(ifMatch)) return;

        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(ifMatch);
        }
        catch (FormatException)
        {
            logger?.LogWarning(
                "ConcurrencyCheckHelper.Check: 非法 base64 ifMatch={IfMatch} (caller={Caller} entity={EntityType}#{EntityId})",
                ifMatch, caller, entityType, entityId);
            return; // 格式错误不拒绝请求，降级为强制覆盖写，保证老客户端能继续工作
        }

        var current = rowVersion ?? Array.Empty<byte>();
        if (current.AsSpan().SequenceEqual(expected.AsSpan())) return;

        logger?.LogInformation(
            "ConcurrencyCheckHelper.Check: 乐观锁冲突 caller={Caller} entity={EntityType}#{EntityId} expectedLen={ExpectedLen} currentLen={CurrentLen}",
            caller, entityType, entityId, expected.Length, current.Length);

        object? latest = null;
        try
        {
            latest = latestFactory();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "ConcurrencyCheckHelper.Check: latestFactory 抛错，降级为 latest=null");
        }

        throw new ConcurrencyException(entityType, entityId, latest);
    }

    /// <summary>
    /// 显式校验 RowVersion 的异步版本，供 latestFactory 需要异步取数（如联表统计）时避免 sync-over-async 阻塞。
    /// </summary>
    /// <param name="entity">当前数据库实体</param>
    /// <param name="rowVersion">实体当前 RowVersion（传入 entity.RowVersion 本身即可）</param>
    /// <param name="ifMatch">请求中 If-Match（RowVersion base64 字符串）；为空或非法直接跳过以保持旧客户端兼容</param>
    /// <param name="entityType">实体类型名（建议用简单名，如 ChatSession、Customer、User 等），会写入 409 响应字段</param>
    /// <param name="entityId">实体主键</param>
    /// <param name="latestFactory">最新快照构造工厂（异步），冲突时用来填充 409.latest（已脱敏的 DTO 更安全；若实体本身无敏感字段可直接传实体）</param>
    /// <param name="logger">可选日志；仅冲突与非法值时写日志</param>
    /// <param name="caller">调用方法名，便于日志追踪</param>
    /// <exception cref="ConcurrencyException">版本不一致时抛出</exception>
    public static async Task CheckAsync(
        object entity,
        byte[]? rowVersion,
        string? ifMatch,
        string entityType,
        int entityId,
        Func<Task<object?>> latestFactory,
        ILogger? logger = null,
        [CallerMemberName] string caller = "")
    {
        _ = entity;

        if (string.IsNullOrEmpty(ifMatch)) return;

        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(ifMatch);
        }
        catch (FormatException)
        {
            logger?.LogWarning(
                "ConcurrencyCheckHelper.CheckAsync: 非法 base64 ifMatch={IfMatch} (caller={Caller} entity={EntityType}#{EntityId})",
                ifMatch, caller, entityType, entityId);
            return;
        }

        var current = rowVersion ?? Array.Empty<byte>();
        if (current.AsSpan().SequenceEqual(expected.AsSpan())) return;

        logger?.LogInformation(
            "ConcurrencyCheckHelper.CheckAsync: 乐观锁冲突 caller={Caller} entity={EntityType}#{EntityId} expectedLen={ExpectedLen} currentLen={CurrentLen}",
            caller, entityType, entityId, expected.Length, current.Length);

        object? latest = null;
        try
        {
            latest = await latestFactory().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "ConcurrencyCheckHelper.CheckAsync: latestFactory 抛错，降级为 latest=null");
        }

        throw new ConcurrencyException(entityType, entityId, latest);
    }
}
