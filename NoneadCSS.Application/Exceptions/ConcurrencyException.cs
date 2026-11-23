namespace NoneadCSS.Application.Exceptions;

/// <summary>
/// 通用实体乐观锁并发冲突异常。
/// 当更新请求携带的 If-Match（RowVersion base64）与数据库当前版本不一致，
/// 或 EF Core 抛出 DbUpdateConcurrencyException 时由业务层包装抛出。
/// GlobalExceptionMiddleware 统一映射为 HTTP 409 Conflict，并在响应体内附带 latest 快照。
/// 原先绑定在 Calendar 模块的 CalendarConcurrencyException 已被本异常替代，
/// 保留 CalendarConcurrencyException 作为兼容派生类以免外部调用方在 Controller / Service 中 catch 时出现编译错误。
/// </summary>
public class ConcurrencyException : Exception
{
    /// <summary>
    /// 冲突实体类型名（建议使用实体类简单名，如 CalendarEvent、Customer、ChatSession、User、ApiKey 等），
    /// 便于前端区分不同冲突来源并路由到对应刷新逻辑。
    /// </summary>
    public string EntityType { get; }

    /// <summary>冲突实体的主键 ID（目前项目主键统一为 int；后续如出现 Guid 主键可按 ToString() 处理）</summary>
    public int EntityId { get; }

    /// <summary>当前数据库中的最新快照（DTO，已按用户权限脱敏），可为 null（如果实体已被删除）</summary>
    public object? Latest { get; }

    public ConcurrencyException(
        string entityType,
        int entityId,
        object? latest = null,
        string? message = null)
        : base(message ?? $"{entityType} {entityId} 已被其他用户修改，请刷新后重试")
    {
        EntityType = entityType;
        EntityId = entityId;
        Latest = latest;
    }
}
