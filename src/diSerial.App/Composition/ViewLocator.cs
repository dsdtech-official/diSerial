using Avalonia.Controls;
using Avalonia.Controls.Templates;
using DiSerial.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DiSerial.App.Composition;

// Resolve views by model type.
public sealed class ViewLocator(IServiceProvider services) : IDataTemplate
{
    // Create a view for a view model.
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
            return new TextBlock { Text = $"View not found: {viewTypeName}" };
        }

        return services.GetService(viewType) as Control
               ?? (Control)Activator.CreateInstance(viewType)!;
    }

    // Identify supported view models.
    public bool Match(object? data) => data is ViewModelBase;
}
