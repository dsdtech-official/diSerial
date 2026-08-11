using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using DiSerial.App.Localization;
using DiSerial.App.ViewModels.Dialogs;
using DiSerial.App.Views.Dialogs;

namespace DiSerial.App.Services;

/// <summary>
/// IDialogService 的 Avalonia 实现。
///
/// 这是唯一允许直接操作窗口的服务 —— 把 UI 框架依赖收敛在此，
/// 使所有 ViewModel 保持对 Avalonia 无感知、可单元测试。
///
/// ⚠️ <b>表现层服务同样要注入 <see cref="ILocalizationService"/>。</b>
/// 早期版本没有注入，写 <c>ShowMessageAsync</c> 时手边没有取文案的途径，
/// 就把按钮文字写成了硬编码的「确定」—— 结果英文界面上的「关于」对话框
/// 显示中文按钮。本地化能力必须铺到 Service 层，不能只铺到 ViewModel 层，
/// 否则每新增一个 Service 都会重复这个错误。
/// </summary>
public sealed class DialogService(ILocalizationService localization) : IDialogService
{
    private static Window? Owner =>
        (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<NewSessionResult?> ShowNewSessionDialogAsync(NewSessionDialogViewModel viewModel)
    {
        var dialog = new NewSessionDialog { DataContext = viewModel };
        await ShowAsync(dialog);
        return viewModel.Result;
    }

    public async Task<bool> ShowConfirmationAsync(
        string title, string message, string confirmText, string cancelText)
    {
        var vm = new ConfirmationDialogViewModel(title, message, confirmText, cancelText);
        await ShowAsync(new ConfirmationDialog { DataContext = vm });
        return vm.IsConfirmed;
    }

    /// <summary>
    /// 纯提示：只有一个确认按钮。按钮文案取自资源，随界面语言变化。
    /// 取消按钮传空字符串 —— <c>ConfirmationDialogViewModel.ShowCancel</c> 据此隐藏它。
    /// </summary>
    public Task ShowMessageAsync(string title, string message) =>
        ShowConfirmationAsync(title, message, localization[LocKeys.CommonOk], string.Empty);

    public async Task<ExportRequest?> ShowExportDialogAsync(ExportDialogViewModel viewModel)
    {
        await ShowAsync(new ExportDialog { DataContext = viewModel });
        return viewModel.Result;
    }

    /// <summary>
    /// 系统「另存为」。用 Avalonia 的 <c>IStorageProvider</c>（挂在 TopLevel 上），
    /// 把这份 UI 框架依赖收敛在本服务里 —— ViewModel 仍然对 Avalonia 无感知。
    /// </summary>
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

    private static async Task ShowAsync(Window dialog)
    {
        // 主窗口尚未就绪时（启动早期、设计器）退回无属主显示，避免抛异常。
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
