using DiSerial.App.ViewModels.Dialogs;

using DiSerial.Core.Abstractions;

namespace DiSerial.App.Services;

public interface IDialogService
{
    // Show the new session dialog.
    Task<NewSessionResult?> ShowNewSessionDialogAsync(NewSessionDialogViewModel viewModel);

    // Request user confirmation.
    Task<bool> ShowConfirmationAsync(string title, string message, string confirmText, string cancelText);

    // Show an informational message.
    Task ShowMessageAsync(string title, string message);

    // Show selectable frame data.
    Task ShowFrameDataAsync(string title, string data);

    // Show export options.
    Task<ExportRequest?> ShowExportDialogAsync(ExportDialogViewModel viewModel);

    // Show the shortcut name dialog.
    Task<string?> ShowShortcutNameDialogAsync(IReadOnlyCollection<string> existingNames);

    // Show the reply rule dialog.
    Task<AutoAnswerRuleDraft?> ShowAutoAnswerRuleDialogAsync(AutoAnswerRule? existing);

    // Show the recording manager.
    Task ShowRecordingManagerAsync(RecordingManagerViewModel viewModel);

    // Show application options.
    Task<AppOptions?> ShowOptionsDialogAsync(OptionsDialogViewModel viewModel);

    // Choose an export file path.
    Task<string?> ShowSaveFileDialogAsync(string suggestedFileName, string extension);
}
