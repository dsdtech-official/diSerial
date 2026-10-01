using Microsoft.Extensions.Logging;

namespace DiSerial.App.Services;

// Queue send history persistence.
public sealed class BackgroundSendHistoryScheduler(ILoggerFactory loggerFactory)
    : ISendHistoryScheduler
{
    private readonly ILogger _logger = loggerFactory.CreateLogger("SendHistory.Scheduler");
    private readonly Lock _gate = new();

    private Task _tail = Task.CompletedTask;

    // Queue work without waiting.
    public void Post(Action work, string what)
    {
        _ = Enqueue(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Send history: {What} failed", what);
            }

            return true;
        });
    }

    // Queue background work.
    public Task<T> RunAsync<T>(Func<T> work) => Enqueue(work);

    // Queue background work.
    public Task RunAsync(Action work) => Enqueue(() => { work(); return true; });

    // Queue a persistence operation.
    private Task<T> Enqueue<T>(Func<T> item)
    {
        lock (_gate)
        {
            var work = _tail.ContinueWith(
                _ => item(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);

            _tail = work.ContinueWith(
                t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);

            return work;
        }
    }
}
