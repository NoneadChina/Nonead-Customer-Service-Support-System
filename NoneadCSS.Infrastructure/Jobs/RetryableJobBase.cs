using Microsoft.Extensions.Logging;
using Quartz;

namespace NoneadCSS.Infrastructure.Jobs;

public abstract class RetryableJobBase : IJob
{
    private const int MaxRetryCount = 3;

    private static readonly int[] RetryDelays = new[] { 5, 10, 30 };

    protected readonly ILogger _logger;

    protected RetryableJobBase(ILogger logger)
    {
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var jobName = GetType().Name;

        for (int attempt = 0; attempt <= MaxRetryCount; attempt++)
        {
            try
            {
                await ExecuteInternal(context);

                if (attempt > 0)
                {
                    _logger.LogInformation(
                        "{JobName} 在第 {RetryCount} 次重试后执行成功",
                        jobName,
                        attempt);
                }

                return;
            }
            catch (Exception ex)
            {
                if (attempt < MaxRetryCount)
                {
                    var retryCount = attempt + 1;
                    var delaySeconds = RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];

                    _logger.LogWarning(
                        ex,
                        "{JobName} 执行失败（第 {RetryCount}/{MaxRetryCount} 次尝试），将在 {DelaySeconds} 秒后重试",
                        jobName,
                        retryCount,
                        MaxRetryCount,
                        delaySeconds);

                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), context.CancellationToken);
                }
                else
                {
                    _logger.LogError(
                        ex,
                        "{JobName} 执行失败，已达到最大重试次数 {MaxRetryCount}，放弃重试",
                        jobName,
                        MaxRetryCount);

                    throw;
                }
            }
        }
    }

    protected abstract Task ExecuteInternal(IJobExecutionContext context);
}
