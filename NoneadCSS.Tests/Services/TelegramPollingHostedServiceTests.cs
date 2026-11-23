using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NoneadCSS.Infrastructure.Services.Telegram;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// Telegram 轮询 HostedService 的存活口径测试（审计 #10）：
/// 单轮未捕获异常不得让循环退出；退避期间收到停机信号必须立刻退出。
/// 退避阶梯用测试子类注入毫秒级数值，断言不依赖真实 sleep 的长短。
/// </summary>
public class TelegramPollingHostedServiceTests
{
    /// <summary>
    /// 旧写法把 catch 放在 while 之外：CreateScope / GetRequiredService 这类不在 RunOnceAsync 内部 try 里的异常
    /// 会直接结束循环，此后再也不轮询，只能重启进程。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenRoundThrowsUncaught_KeepsPollingWithEscalatingBackoff()
    {
        using var cts = new CancellationTokenSource();
        var scopeFactory = new ThrowingScopeFactory(attempt =>
        {
            // 第三轮打开停机信号：退避结束后 Task.Delay 立即抛 OperationCanceledException ⇒ 正常停止
            if (attempt >= 3)
            {
                cts.Cancel();
            }
        });
        var logger = new RecordingLogger();
        var service = new ProbePollingService(
            new FakeServiceProvider(scopeFactory),
            logger,
            [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(60)]);

        await service.RunAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(10));

        // 关键：异常之后仍在继续尝试（第 3 次是停机清理 TrySetStopped 自己建的 Scope）
        scopeFactory.Attempts.Should().BeGreaterThanOrEqualTo(3);
        logger.Errors.Should().ContainSingle(e => e.Contains("连续失败 1 次"));
        logger.Errors.Should().ContainSingle(e => e.Contains("连续失败 2 次"));
        // 未捕获异常不该再把整条入站链路判死：旧的 Critical 文案已不在循环出口上
        logger.Criticals.Should().BeEmpty();
    }

    /// <summary>停机信号已触发时不得进入退避，也不得再打一轮请求。</summary>
    [Fact]
    public async Task ExecuteAsync_WhenAlreadyCancelled_ExitsWithoutPolling()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var scopeFactory = new ThrowingScopeFactory(_ => { });
        var logger = new RecordingLogger();
        var service = new ProbePollingService(new FakeServiceProvider(scopeFactory), logger, [TimeSpan.FromMilliseconds(1)]);

        await service.RunAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(5));

        logger.Errors.Should().BeEmpty();
        // 只有停机清理那一次建 Scope
        scopeFactory.Attempts.Should().Be(1);
    }

    /// <summary>生产阶梯必须是 5s → 30s → 60s（封顶），测试注入的短阶梯不得掩盖默认值。</summary>
    [Fact]
    public void FailureBackoffLadder_DefaultsToFiveThenThirtyThenSixtySeconds()
    {
        var service = new ProbePollingService(
            new FakeServiceProvider(new ThrowingScopeFactory(_ => { })),
            new RecordingLogger(),
            []);

        service.BaselineLadder.Should().Equal(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1));
    }

    /// <summary>暴露 ExecuteAsync 与默认退避阶梯，并注入短阶梯，避免测试真等 5 秒。</summary>
    private sealed class ProbePollingService : TelegramPollingHostedService
    {
        private readonly IReadOnlyList<TimeSpan> _ladder;

        public ProbePollingService(
            IServiceProvider services,
            ILogger<TelegramPollingHostedService> logger,
            IReadOnlyList<TimeSpan> ladder)
            : base(services, logger)
        {
            _ladder = ladder;
        }

        protected override IReadOnlyList<TimeSpan> FailureBackoffLadder => _ladder;

        public Task RunAsync(CancellationToken stoppingToken) => ExecuteAsync(stoppingToken);

        public IReadOnlyList<TimeSpan> BaselineLadder => base.FailureBackoffLadder;
    }

    /// <summary>CreateScope 一律抛异常，等价于"这一轮还没跑到 try 之内就炸了"（DI 解析失败 / Scope 建不出来）。</summary>
    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        private readonly Action<int> _onAttempt;

        public ThrowingScopeFactory(Action<int> onAttempt)
        {
            _onAttempt = onAttempt;
        }

        public int Attempts { get; private set; }

        public IServiceScope CreateScope()
        {
            Attempts++;
            _onAttempt(Attempts);
            throw new InvalidOperationException("模拟 Scoped 服务解析失败");
        }
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public FakeServiceProvider(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IServiceScopeFactory) ? _scopeFactory : null;
    }

    private sealed class RecordingLogger : ILogger<TelegramPollingHostedService>
    {
        public List<string> Errors { get; } = [];

        public List<string> Criticals { get; } = [];

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
            }
            else if (logLevel >= LogLevel.Critical)
            {
                Criticals.Add(formatter(state, exception));
            }
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
