using Avalonia.Threading;

namespace DiSerial.App.Services;

/// <summary>
/// The production <see cref="IPeriodicTimerFactory"/>, backed by <see cref="DispatcherTimer"/>.
///
/// <para>This is the only type in the App layer that knows a dispatcher is involved. Everything
/// that needs a repeating tick goes through <see cref="IPeriodicTimer"/> instead, so the same
/// code is drivable from tests that have no Avalonia runtime.</para>
/// </summary>
public sealed class DispatcherPeriodicTimerFactory : IPeriodicTimerFactory
{
    public IPeriodicTimer Create(
        TimeSpan interval, Action tick, TimerPriority priority = TimerPriority.Normal)
        => new DispatcherPeriodicTimer(interval, tick, priority);

    private sealed class DispatcherPeriodicTimer : IPeriodicTimer
    {
        private readonly DispatcherTimer _timer;

        internal DispatcherPeriodicTimer(TimeSpan interval, Action tick, TimerPriority priority)
            => _timer = new DispatcherTimer(
                interval,
                priority == TimerPriority.Background
                    ? DispatcherPriority.Background
                    : DispatcherPriority.Normal,
                (_, _) => tick());

        public bool IsRunning => _timer.IsEnabled;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        // DispatcherTimer holds no unmanaged resource; stopping it drops the dispatcher's
        // reference to the callback, which is the leak that actually matters here.
        public void Dispose() => _timer.Stop();
    }
}
