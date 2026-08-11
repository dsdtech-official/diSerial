namespace DiSerial.App.Services;

/// <summary>How urgently a tick should be delivered relative to other UI work.</summary>
public enum TimerPriority
{
    /// <summary>Deliver as an ordinary UI-thread work item.</summary>
    Normal,

    /// <summary>Deliver only while the UI thread is otherwise idle.</summary>
    Background
}

/// <summary>A repeating timer whose callback runs on the UI thread.</summary>
public interface IPeriodicTimer : IDisposable
{
    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    bool IsRunning { get; }

    void Start();

    void Stop();
}

/// <summary>
/// Creates <see cref="IPeriodicTimer"/> instances.
///
/// <para><b>Why this seam exists.</b> The App test project deliberately does not spin up an
/// Avalonia runtime, so a <c>DispatcherTimer</c> never ticks there. A test asserting
/// "nothing is sent after Stop()" then passes for the wrong reason: nothing was ever sent
/// at all, because the timer was dead from the first line. That exact false green has bitten
/// this project four times -- see docs/03-conventions.md, section 0.6 (4).</para>
///
/// <para>Production resolves <see cref="DispatcherPeriodicTimerFactory"/>; tests inject a fake
/// whose tick they drive by hand.</para>
///
/// <para><b>Requirement on any test fake.</b> Its "fire one tick" helper must invoke the
/// callback <i>regardless of its own running state</i>. If the fake refuses to fire while
/// stopped, then "no send happens after Stop()" turns green because the fake declined --
/// not because the subject under test actually unsubscribed. The assertion would be testing
/// the fake, not the code.</para>
/// </summary>
public interface IPeriodicTimerFactory
{
    /// <summary>
    /// Creates a stopped timer. The caller owns it and must dispose it.
    /// </summary>
    /// <param name="interval">Time between ticks.</param>
    /// <param name="tick">Invoked on the UI thread on every tick.</param>
    /// <param name="priority">Delivery priority relative to other UI work.</param>
    IPeriodicTimer Create(TimeSpan interval, Action tick, TimerPriority priority = TimerPriority.Normal);
}
