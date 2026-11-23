using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace NoneadCSS.Tests.Helpers;

/// <summary>
/// 支持 EF Core 异步 LINQ 操作的测试辅助类。
/// 用法: var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(new List<User> { ... });
/// </summary>
internal class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
{
    private readonly IQueryProvider _inner;

    public TestAsyncQueryProvider(IQueryProvider inner)
    {
        _inner = inner;
    }

    public IQueryable CreateQuery(Expression expression)
    {
        return new TestAsyncEnumerable<TEntity>(expression);
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        return new TestAsyncEnumerable<TElement>(expression);
    }

    public object? Execute(Expression expression)
    {
        return _inner.Execute(expression);
    }

    public TResult Execute<TResult>(Expression expression)
    {
        return _inner.Execute<TResult>(expression);
    }

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        var resultType = typeof(TResult);

        if (resultType.IsGenericType)
        {
            var genericDef = resultType.GetGenericTypeDefinition();
            var innerType = resultType.GetGenericArguments()[0];

            // 执行同步查询获取结果
            var result = ExecuteSync(expression, innerType);

            if (genericDef == typeof(Task<>))
            {
                var fromResultMethod = typeof(Task)
                    .GetMethod("FromResult")!
                    .MakeGenericMethod(innerType);
                return (TResult)fromResultMethod.Invoke(null, new object?[] { result })!;
            }

            if (genericDef == typeof(ValueTask<>))
            {
                return (TResult)Activator.CreateInstance(resultType, result)!;
            }
        }

        // 默认：直接返回同步执行结果
        return (TResult)_inner.Execute(expression)!;
    }

    private object? ExecuteSync(Expression expression, Type resultType)
    {
        // 直接调用非泛型 Execute 方法
        return _inner.Execute(expression);
    }
}

internal class TestAsyncEnumerable<TEntity> : EnumerableQuery<TEntity>, IAsyncEnumerable<TEntity>, IOrderedQueryable<TEntity>
{
    private readonly IQueryProvider _provider;

    public TestAsyncEnumerable(Expression expression) : base(expression)
    {
        // 通过新建一个独立的 EnumerableQuery 实例获取底层 Provider，
        // 避免访问 this.Provider 时的递归调用
        _provider = new TestAsyncQueryProvider<TEntity>(
            ((IQueryable)new EnumerableQuery<TEntity>(expression)).Provider);
    }

    public TestAsyncEnumerable(IEnumerable<TEntity> enumerable) : base(enumerable)
    {
        _provider = new TestAsyncQueryProvider<TEntity>(
            ((IQueryable)new EnumerableQuery<TEntity>(enumerable)).Provider);
    }

    IQueryProvider IQueryable.Provider => _provider;

    public IAsyncEnumerator<TEntity> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return new TestAsyncEnumerator<TEntity>(this.AsEnumerable().GetEnumerator());
    }
}

internal class TestAsyncEnumerator<TEntity> : IAsyncEnumerator<TEntity>
{
    private readonly IEnumerator<TEntity> _inner;

    public TestAsyncEnumerator(IEnumerator<TEntity> inner)
    {
        _inner = inner;
    }

    public TEntity Current => _inner.Current;

    public ValueTask DisposeAsync()
    {
        _inner.Dispose();
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> MoveNextAsync()
    {
        return new ValueTask<bool>(_inner.MoveNext());
    }
}

/// <summary>
/// 提供创建支持异步操作的 Mock DbSet 的扩展方法
/// </summary>
internal static class TestAsyncQueryProvider
{
    /// <summary>
    /// 创建一个支持 FirstOrDefaultAsync、ToListAsync 等异步操作的 Mock DbSet
    /// </summary>
    public static Mock<DbSet<TEntity>> CreateMockDbSet<TEntity>(IEnumerable<TEntity> data) where TEntity : class
    {
        var queryable = data.AsQueryable();
        var mockSet = new Mock<DbSet<TEntity>>();

        mockSet.As<IAsyncEnumerable<TEntity>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(() => new TestAsyncEnumerator<TEntity>(queryable.GetEnumerator()));

        mockSet.As<IQueryable<TEntity>>()
            .Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<TEntity>(queryable.Provider));

        mockSet.As<IQueryable<TEntity>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mockSet.As<IQueryable<TEntity>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mockSet.As<IQueryable<TEntity>>().Setup(m => m.GetEnumerator()).Returns(() => queryable.GetEnumerator());

        return mockSet;
    }

    /// <summary>
    /// 创建一个支持 FirstOrDefaultAsync、ToListAsync 等异步操作的 IQueryable，
    /// 用于 Mock 仓储的 GetQueryable() 方法返回值。
    /// </summary>
    public static IQueryable<TEntity> CreateAsyncQueryable<TEntity>(IEnumerable<TEntity> data)
    {
        return new TestAsyncEnumerable<TEntity>(data);
    }
}
