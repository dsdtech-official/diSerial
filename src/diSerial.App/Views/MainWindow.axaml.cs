using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DiSerial.App.Views;

// Host the application views.
public partial class MainWindow : Window
{
    // Initialize the main window.
    public MainWindow() => InitializeComponent();

    // Request application shutdown.
    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();
}
