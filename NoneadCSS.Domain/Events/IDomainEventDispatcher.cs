namespace NoneadCSS.Domain.Events;

/// <summary>
/// 强类型的领域事件分发器。
/// <para>
/// 接口置于 Domain 层，使 Application 层服务可直接依赖它发布领域事件，
/// 而不必引用 Infrastructure。具体实现位于
/// <c>NoneadCSS.Infrastructure.Services.DomainEventDispatcher</c>（单例注册，见 NoneadCSS.Api/Program.cs），
/// 实现内部为每次分发显式创建异步 Scope 来解析 <c>IListener&lt;TEvent&gt;</c>，
/// 并捕获监听器异常仅记录日志，因此发布失败不会影响调用方的主流程。
/// </para>
/// <para>
/// 调用约定：必须在数据已提交（<c>SaveChangesAsync</c>／事务提交）之后发布，
/// 因为监听器运行在自己的 Scope 和自己的 DbContext 中，读不到未提交的数据。
/// </para>
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// 异步分发领域事件给所有已注册的监听器。
    /// </summary>
    /// <typeparam name="TEvent">事件类型（引用类型）。</typeparam>
    /// <param name="event">事件实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DispatchAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class;
}
