using Avalonia.Threading;

namespace DiSerial.App.Services;

// Create dispatcher timers.
public sealed class DispatcherPeriodicTimerFactory : IPeriodicTimerFactory
{
    // Create a periodic UI timer.
    public IPeriodicTimer Create(
        TimeSpan interval, Action tick, TimerPriority priority = TimerPriority.Normal)
        => new DispatcherPeriodicTimer(interval, tick, priority);

    // Run periodic UI callbacks.
    private sealed class DispatcherPeriodicTimer : IPeriodicTimer
    {
        private readonly DispatcherTimer _timer;

        // Configure a periodic UI timer.
        internal DispatcherPeriodicTimer(TimeSpan interval, Action tick, TimerPriority priority)
            => _timer = new DispatcherTimer(
                interval,
                priority == TimerPriority.Background
                    ? DispatcherPriority.Background
                    : DispatcherPriority.Normal,
                (_, _) => tick());

        public bool IsRunning => _timer.IsEnabled;

        // Start periodic UI callbacks.
        public void Start() => _timer.Start();

        // Stop periodic UI callbacks.
        public void Stop() => _timer.Stop();

        // Release owned resources.
        public void Dispose() => _timer.Stop();
    }
}
