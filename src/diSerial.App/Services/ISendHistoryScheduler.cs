using DiSerial.Core.Abstractions;

namespace DiSerial.App.Services;

public interface ISendHistoryScheduler
{
    // Queue work without waiting.
    void Post(Action work, string what);

    // Queue background work.
    Task<T> RunAsync<T>(Func<T> work);

    // Queue background work.
    Task RunAsync(Action work);
}
