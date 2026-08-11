using Avalonia.Controls;
using Avalonia.Controls.Templates;
using DiSerial.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DiSerial.App.Composition;

/// <summary>
/// Maps a ViewModel to its View by naming convention:
///   DiSerial.App.ViewModels.Sessions.MonitorSessionViewModel
///     → DiSerial.App.Views.Sessions.MonitorSessionView
///
/// <para>This is the "session type → DataTemplate dispatch" the architecture document
/// describes. Adding a session type means naming the pair by the convention; the main
/// window XAML does not change. That is the main extensibility this project buys by leaning
/// on the XAML DataTemplate mechanism.</para>
///
/// <para>⚠️ <b>About the container lookup in <see cref="Build"/></b> (P2-37, written down
/// 2026-08-05): it resolves Views from DI so that a View can take constructor-injected
/// services, and falls back to the parameterless constructor. <b>No View is registered
/// today</b> — every View in this application has a parameterless constructor and gets its
/// data through its DataContext — so <b>that branch has never once returned non-null</b>,
/// and the fallback is the only path actually taken.</para>
///
/// <para>The comment here used to describe the DI path as the primary one, which read as
/// "Views come from the container" and is simply not true of this codebase. The lookup is
/// kept as a seam, not as a described capability: registering a View in
/// <c>AppServiceCollectionExtensions</c> is what would turn it on, and until something does
/// that, this class builds Views with <see cref="Activator"/>.</para>
/// </summary>
public sealed class ViewLocator(IServiceProvider services) : IDataTemplate
{
    public Control Build(object? param)
    {
        if (param is null) return new TextBlock { Text = "(null)" };

        var viewModelType = param.GetType();
        var viewTypeName = viewModelType.FullName!
            .Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        var viewType = viewModelType.Assembly.GetType(viewTypeName);
        if (viewType is null)
        {
            // Developer diagnostics, in English on purpose: this does reach the screen, but
            // only for the coding error "ViewModel has no matching View". It is not product
            // copy and does not belong in a resource file.
            return new TextBlock { Text = $"View not found: {viewTypeName}" };
        }

        // The GetService branch is a seam with no registrations behind it today -- see the
        // class summary. Activator is the path every View actually takes.
        return services.GetService(viewType) as Control
               ?? (Control)Activator.CreateInstance(viewType)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
