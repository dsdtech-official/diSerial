using Avalonia.Controls;

namespace DiSerial.App.Views.Dialogs;

// Bind dialog close requests.
internal static class DialogCloseBinding
{
    // Bind dialog lifecycle events.
    public static void Bind<TViewModel>(Window window, Func<TViewModel, Action> attach)
        where TViewModel : class
    {
        Action? release = null;

        // Rebind dialog model handlers.
        void Rebind(object? sender, EventArgs e)
        {
            release?.Invoke();
            release = window.DataContext is TViewModel vm ? attach(vm) : null;
        }

        // Release dialog model handlers.
        void Released(object? sender, EventArgs e)
        {
            release?.Invoke();
            release = null;
        }

        window.DataContextChanged += Rebind;
        window.Closed += Released;
    }
}
