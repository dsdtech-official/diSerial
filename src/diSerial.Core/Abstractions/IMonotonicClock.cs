namespace DiSerial.Core.Abstractions;

public interface IMonotonicClock
{
    DateTimeOffset Now { get; }

    TimeSpan Resolution { get; }
}
