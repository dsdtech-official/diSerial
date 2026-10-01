using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using DiSerial.App.Localization;
using DiSerial.App.ViewModels.Dialogs;
using DiSerial.App.Views.Dialogs;
using DiSerial.Core.Abstractions;

namespace DiSerial.App.Services;

// Present application dialogs.
public sealed class DialogService(
    ILocalizationService localization, IEnumChoiceProvider enumChoices) : IDialogService
{
    private static Window? Owner =>
        Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? PickOwner(desktop.Windows, desktop.MainWindow, static w => w.IsActive)
            : null;

    // Choose a dialog owner.
    internal static T? PickOwner<T>(IReadOnlyList<T> open, T? mainWindow, Func<T, bool> isActive)
        where T : class =>
        open.FirstOrDefault(isActive) ?? mainWindow;

    // Show new session configuration.
    public async Task<NewSessionResult?> ShowNewSessionDialogAsync(NewSessionDialogViewModel viewModel)
    {
        var dialog = new NewSessionDialog { DataContext = viewModel };
        await ShowAsync(dialog);
        return viewModel.Result;
    }

    // Request user confirmation.
    public async Task<bool> ShowConfirmationAsync(
        string title, string message, string confirmText, string cancelText)
    {
        var vm = new ConfirmationDialogViewModel(title, message, confirmText, cancelText);
        await ShowAsync(new ConfirmationDialog { DataContext = vm });
        return vm.IsConfirmed;
    }

    // Show an informational message.
    public Task ShowMessageAsync(string title, string message) =>
        ShowConfirmationAsync(title, message, localization[LocKeys.CommonOk], string.Empty);

    // Show complete frame data.
    public Task ShowFrameDataAsync(string title, string data) =>
        ShowAsync(new FrameDataDialog { DataContext = new FrameDataDialogViewModel(title, data) });

    // Show data export options.
    public async Task<ExportRequest?> ShowExportDialogAsync(ExportDialogViewModel viewModel)
    {
        await ShowAsync(new ExportDialog { DataContext = viewModel });
        return viewModel.Result;
    }

    // Request a shortcut name.
    public async Task<string?> ShowShortcutNameDialogAsync(IReadOnlyCollection<string> existingNames)
    {
        var vm = new ShortcutNameDialogViewModel(localization, existingNames);
        await ShowAsync(new ShortcutNameDialog { DataContext = vm });
        return vm.Result;
    }

    // Show the reply rule dialog.
    public async Task<AutoAnswerRuleDraft?> ShowAutoAnswerRuleDialogAsync(AutoAnswerRule? existing)
    {
        var vm = new AutoAnswerRuleDialogViewModel(localization, enumChoices, existing);
        await ShowAsync(new AutoAnswerRuleDialog { DataContext = vm });
        return vm.Result;
    }

    // Show the recording manager.
    public Task ShowRecordingManagerAsync(RecordingManagerViewModel viewModel) =>
        ShowAsync(new RecordingManagerDialog { DataContext = viewModel });

    // Show application options.
    public async Task<AppOptions?> ShowOptionsDialogAsync(OptionsDialogViewModel viewModel)
    {
        await ShowAsync(new OptionsDialog { DataContext = viewModel });
        return viewModel.Result;
    }

    // Choose an export file path.
    public async Task<string?> ShowSaveFileDialogAsync(string suggestedFileName, string extension)
    {
        if (Owner?.StorageProvider is not { } storage) return null;

        var ext = extension.TrimStart('.');
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedFileName,
            DefaultExtension = ext,
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType(ext.ToUpperInvariant()) { Patterns = [$"*.{ext}"] }
            ]
        });

        return file?.TryGetLocalPath();
    }

    // Show a dialog with its model.
    private static async Task ShowAsync(Window dialog)
    {
        if (Owner is { } owner)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }
    }
}
