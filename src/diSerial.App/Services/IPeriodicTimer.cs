namespace DiSerial.App.Services;

public enum TimerPriority
{
    Normal,

    Background
}

public interface IPeriodicTimer : IDisposable
{
    bool IsRunning { get; }

    // Start periodic callbacks.
    void Start();

    // Stop periodic callbacks.
    void Stop();
}

public interface IPeriodicTimerFactory
{
    // Create a periodic timer.
    IPeriodicTimer Create(TimeSpan interval, Action tick, TimerPriority priority = TimerPriority.Normal);
}
