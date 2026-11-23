namespace NoneadCSS.Application.Exceptions;

/// <summary>
/// 日历事件乐观锁冲突异常（兼容派生类）。
/// 新代码统一使用 ConcurrencyException；保留本异常以便 Calendar 模块内部抛出和外部 catch 继续编译。
/// 中间件通过捕获基类 ConcurrencyException 统一处理 409。
/// </summary>
public class CalendarConcurrencyException : ConcurrencyException
{
    /// <summary>冲突事件 ID（等同于 ConcurrencyException.EntityId）</summary>
    public int EventId => EntityId;

    public CalendarConcurrencyException(int eventId, object? latest = null)
        : base(entityType: "CalendarEvent", entityId: eventId, latest: latest,
            message: $"事件 {eventId} 已被其他用户修改，请刷新后重试")
    {
    }
}
