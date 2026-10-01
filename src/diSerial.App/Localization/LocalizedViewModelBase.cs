using DiSerial.App.ViewModels;

namespace DiSerial.App.Localization;

// Refresh text on language change.
public abstract class LocalizedViewModelBase : ViewModelBase, IDisposable
{
    private bool _disposed;

    // Subscribe to language changes.
    protected LocalizedViewModelBase(ILocalizationService localization)
    {
        Localization = localization;
        Localization.CultureChanged += OnCultureChanged;
    }

    protected ILocalizationService Localization { get; }

    // Resolve a localized resource.
    protected string L(string key) => Localization[key];

    // Format a localized resource.
    protected string LF(string key, params object?[] args) => Localization.Format(key, args);

    // Refresh localized display text.
    protected virtual void OnCultureChanged() => OnPropertyChanged(string.Empty);

    // Refresh localized display text.
    private void OnCultureChanged(object? sender, EventArgs e) => OnCultureChanged();

    // Release owned subscriptions.
    protected virtual void DisposeCore() { }

    // Release localization bindings.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DisposeCore();
        Localization.CultureChanged -= OnCultureChanged;
        GC.SuppressFinalize(this);
    }
}
