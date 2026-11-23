using NoneadCSS.Domain.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// 强类型的领域事件分发器实现。
/// 接口 <see cref="IDomainEventDispatcher"/> 位于 Domain 层（NoneadCSS.Domain/Events/IDomainEventDispatcher.cs），
/// 以便 Application 层服务可直接依赖它发布事件，实现仍保留在 Infrastructure。
/// </summary>
public class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DomainEventDispatcher> _logger;

    public DomainEventDispatcher(IServiceProvider serviceProvider, ILogger<DomainEventDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class
    {
        var eventName = typeof(TEvent).Name;
        _logger.LogDebug("分发领域事件: {EventName}", eventName);

        try
        {
            // 通过反射查找所有对应的监听器
            var listenerType = typeof(IListener<>).MakeGenericType(typeof(TEvent));

            // 使用显式 Scope 解析 Scoped 监听器，避免 Singleton 服务直接持有 Scoped 实例
            // 防止 DbContext 等 Scoped 服务被提升为 Singleton 导致并发问题
            await using var scope = _serviceProvider.CreateAsyncScope();
            var listeners = scope.ServiceProvider.GetServices(listenerType);

            foreach (var listener in listeners)
            {
                if (listener == null) continue;
                var handleMethod = listener.GetType().GetMethod("HandleAsync");
                if (handleMethod != null)
                {
                    try
                    {
                        var result = handleMethod.Invoke(listener, new object[] { @event!, cancellationToken });
                        if (result is Task task)
                        {
                            await task;
                        }
                    }
                    catch (Exception ex)
                    {
                        // 反射调用抛出的异常会被包装为 TargetInvocationException，需解包获取真实异常
                        var realException = ex is TargetInvocationException tie && tie.InnerException != null
                            ? tie.InnerException
                            : ex;
                        _logger.LogError(realException, "监听器 {Listener} 处理事件 {Event} 失败", listener.GetType().Name, eventName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "分发领域事件 {EventName} 失败", eventName);
        }
    }
}

/// <summary>
/// 强类型监听器接口
/// </summary>
public interface IListener<TEvent> where TEvent : class
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}
