using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiSerial.App.Localization;
using DiSerial.App.Services;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;

namespace DiSerial.App.ViewModels.Dialogs;

/// <summary>
/// 导出选项对话框（01-spec 6.4）。
///
/// <b>它同时承担「选项」与「选路径」</b>：里面有一行「保存到：[路径][浏览…]」，
/// 所以停止记录之后**只弹这一个窗**，而不是先选项、再文件框。
///
/// ⚠️ <b>默认文件名带端口与串口参数</b>（<c>diserial-COM1-9600-8N1-20260731-0130.tsv</c>）——
/// 因为批次元信息**刻意不写进文件**（`#` 注释行会被 Excel 与严格的 TSV 解析器
/// 当成数据行）。上下文靠文件名带出去，完整信息留在库里。
/// </summary>
public sealed partial class ExportDialogViewModel : LocalizedViewModelBase
{
    private readonly IDialogService _dialogs;

    /// <summary>
    /// 两路通道的端口名与别名 —— 用户在本对话框里改不了它们，所以刻意<b>不是</b>
    /// <c>[ObservableProperty]</c>：它只是从会话带过来、原样交给导出的上下文。
    /// </summary>
    private readonly ExportChannels _channels;

    /// <summary>见 <see cref="ExportDialogSeed.PreferredDirectory"/>：已确认存在，或为空。</summary>
    private readonly string? _preferredDirectory;

    public ExportDialogViewModel(
        ILocalizationService localization,
        IEnumChoiceProvider enumChoices,
        IDialogService dialogs,
        ExportDialogSeed seed)
        : base(localization)
    {
        _dialogs = dialogs;

        FormatChoices = enumChoices.GetChoices<ExportFormat>();
        DisplayFormatChoices = enumChoices.GetChoices<DisplayFormat>();
        TimestampModeChoices = enumChoices.GetChoices<TimestampMode>();

        DisplayFormat = seed.DisplayFormat;
        TimestampMode = seed.TimestampMode;
        IncludeChannelColumn = seed.IncludeChannelColumn;
        IncludeDeltaColumn = seed.IncludeDeltaColumn;
        SuggestedBaseName = seed.SuggestedBaseName;
        _channels = seed.Channels;
        _preferredDirectory = seed.PreferredDirectory;

        FilePath = BuildDefaultPath();
    }

    public IReadOnlyList<LocalizedEnumItem> FormatChoices { get; }

    public IReadOnlyList<LocalizedEnumItem> DisplayFormatChoices { get; }

    public IReadOnlyList<LocalizedEnumItem> TimestampModeChoices { get; }

    /// <summary>不含扩展名，形如 <c>diserial-COM1-9600-8N1-20260731-0130</c>。</summary>
    public string SuggestedBaseName { get; }

    [ObservableProperty]
    private ExportFormat _format = ExportFormat.Tsv;

    [ObservableProperty]
    private DisplayFormat _displayFormat;

    [ObservableProperty]
    private TimestampMode _timestampMode;

    [ObservableProperty]
    private bool _includeChannelColumn;

    [ObservableProperty]
    private bool _includeDeltaColumn;

    [ObservableProperty]
    private string _filePath = string.Empty;

    public ExportRequest? Result { get; private set; }

    /// <summary>与其他对话框同一套路：VM 请求关闭，由 View 执行。</summary>
    public event EventHandler? CloseRequested;

    /// <summary>换格式时把扩展名跟着改掉 —— 否则会存出名不副实的文件。</summary>
    partial void OnFormatChanged(ExportFormat value) => FilePath = BuildDefaultPath();

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var picked = await _dialogs.ShowSaveFileDialogAsync(
            Path.GetFileName(FilePath), Extension);

        // 用户在文件框里取消 —— 只是不改路径，不关闭本对话框。
        if (!string.IsNullOrWhiteSpace(picked)) FilePath = picked;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = new ExportRequest(FilePath, new ExportOptions
        {
            Format = Format,
            DisplayFormat = DisplayFormat,
            TimestampMode = TimestampMode,
            IncludeChannelColumn = IncludeChannelColumn,
            IncludeDeltaColumn = IncludeDeltaColumn,
            ChannelPortA = _channels.PortA,
            ChannelAliasA = _channels.AliasA,
            ChannelPortB = _channels.PortB,
            ChannelAliasB = _channels.AliasB
        });
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(FilePath);

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private string Extension => Format == ExportFormat.Csv ? ".csv" : ".tsv";

    /// <summary>
    /// 组默认路径。⭐ <b>三级取目录，顺序有意义</b>：
    /// 用户在本次对话框里已经选过的目录 → 上次导出成功的目录（<c>PreferredDirectory</c>，P33）
    /// → 「文档」。
    ///
    /// <para>⚠️ 第一级不是多余的：换导出格式会重算路径（<c>OnFormatChanged</c>），
    /// ⛔ <b>而那时不能把用户刚选的目录丢回去</b> —— 只该换扩展名。</para>
    /// </summary>
    private string BuildDefaultPath()
    {
        var directory = !string.IsNullOrWhiteSpace(FilePath)
            ? Path.GetDirectoryName(FilePath) ?? string.Empty
            : !string.IsNullOrWhiteSpace(_preferredDirectory)
                ? _preferredDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        return Path.Combine(directory, SuggestedBaseName + Extension);
    }

    partial void OnFilePathChanged(string value) => ConfirmCommand.NotifyCanExecuteChanged();
}

/// <summary>
/// 打开导出对话框时的初始值。
///
/// 显示相关的四项**取自当前会话的显示设置** —— 用户多半就想导出「他正看着的样子」，
/// 让他从那里开始改，比从一套固定默认值开始少点几下。
/// </summary>
/// <param name="SuggestedBaseName">不含扩展名的默认文件名。</param>
/// <param name="Channels">两路通道的端口名与别名，终端会话四项全 null。</param>
/// <param name="PreferredDirectory">
/// 对话框打开时落在哪个目录（P33，2026-08-10）。空表示「文档」。
///
/// ⛔ <b>调用方必须传一个<u>已经确认存在</u>的目录</b> —— 本视图模型刻意不碰文件系统，
/// 也不记日志：「上次那个目录还在不在」是一次真实的磁盘查询，
/// 它和它的回落一起留在 <c>SessionViewModel.ResolveExportDirectory</c>，那里有 logger。
/// </param>
public sealed record ExportDialogSeed(
    string SuggestedBaseName,
    DisplayFormat DisplayFormat,
    TimestampMode TimestampMode,
    bool IncludeChannelColumn,
    bool IncludeDeltaColumn,
    ExportChannels Channels = default,
    string? PreferredDirectory = null);

/// <summary>
/// 导出要带的两路通道信息。
///
/// ⚠️ <b>端口名与别名分开传，不预先拼成一个字符串</b> ——
/// 导出文件是机器可读的，<c>Port</c> 与 <c>Alias</c> 各占一列，
/// 解析脚本按列取值，不必再去拆 <c>COM6 · PLC</c> 这种给人看的写法。
/// 界面上的拼接规则在 <c>ChannelViewModel</c>，与本类无关。
/// </summary>
public readonly record struct ExportChannels(
    string? PortA = null,
    string? AliasA = null,
    string? PortB = null,
    string? AliasB = null);

public sealed record ExportRequest(string FilePath, ExportOptions Options);
