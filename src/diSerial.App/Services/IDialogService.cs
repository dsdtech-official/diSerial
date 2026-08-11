using DiSerial.App.ViewModels.Dialogs;

namespace DiSerial.App.Services;

/// <summary>
/// 对话框服务契约。
///
/// ViewModel 通过本接口弹窗，不直接引用任何 Avalonia 窗口类型 ——
/// 这是保证 ViewModel 可单元测试的关键：测试时注入一个假实现即可。
/// </summary>
public interface IDialogService
{
    /// <summary>显示「新建会话」对话框。用户取消时返回 null。</summary>
    Task<NewSessionResult?> ShowNewSessionDialogAsync(NewSessionDialogViewModel viewModel);

    /// <summary>显示确认对话框，返回用户是否确认。</summary>
    Task<bool> ShowConfirmationAsync(string title, string message, string confirmText, string cancelText);

    /// <summary>显示提示信息。</summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>
    /// 导出选项对话框（C-09 的导出那一步 / M-08）。用户取消时返回 null。
    ///
    /// ⚠️ <b>刻意把「选路径」收进这一个对话框</b>（里面有一行「保存到：[路径][浏览…]」），
    /// 而不是先弹选项、再弹文件框 —— 停止记录之后连弹两次窗是没必要的。
    /// </summary>
    Task<ExportRequest?> ShowExportDialogAsync(ExportDialogViewModel viewModel);

    /// <summary>
    /// 系统的「另存为」对话框。返回用户选定的路径；取消时返回 null。
    ///
    /// ⚠️ 这是 <b>View 层能力</b>（Avalonia 的 <c>IStorageProvider</c> 挂在 TopLevel 上），
    /// 所以走本接口而不是让 ViewModel 直接碰 Avalonia —— 与架构分层一致。
    /// </summary>
    Task<string?> ShowSaveFileDialogAsync(string suggestedFileName, string extension);
}
