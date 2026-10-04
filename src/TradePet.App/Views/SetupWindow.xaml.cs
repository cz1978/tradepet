using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TradePet.App.Runtime;
using TradePet.App.ViewModels;
using TradePet.Infrastructure.Mt4;
using TradePet.Infrastructure.Mt5;

namespace TradePet.App.Views;

public partial class SetupWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Func<Task<bool>> _save;
    private readonly CancellationTokenSource _closed = new();
    private int _step;
    private bool _ready;
    private bool _busy;
    private bool _saved;

    public SetupWindow(MainViewModel viewModel, Func<Task<bool>> save)
    {
        _viewModel = viewModel;
        _save = save;
        InitializeComponent();
        if (RuntimePaths.Resolve().PythonIsBundled)
        {
            PythonDescription.Text = TradePet.Core.Localization.UiText.Translate("已内置 MT5 所需的 Python 和依赖，无需另行安装或联网下载。");
            PythonStatus.Text = TradePet.Core.Localization.UiText.Translate("点击“检测 Python”确认内置环境可用。");
            ExternalPythonActions.Visibility = Visibility.Collapsed;
        }
        DataContext = viewModel;
        _ready = true;
        RefreshTerminals();
        UpdateStep();
        Closed += (_, _) => _closed.Cancel();
    }

    private void RefreshTerminals()
    {
        var selected = _viewModel.SelectedTerminalPath;
        var terminals = new Mt5TerminalDiscovery().Discover(selected, _viewModel.SelectedPlatform);
        _viewModel.TerminalOptions.Clear();
        foreach (var terminal in terminals) _viewModel.TerminalOptions.Add(new TerminalOption(terminal.TerminalPath, terminal.Label));
        if (selected is not null && !terminals.Any(item => string.Equals(item.TerminalPath, selected, StringComparison.OrdinalIgnoreCase)))
            _viewModel.TerminalOptions.Add(new TerminalOption(selected, "已保存的终端 · 未找到，请重新选择"));
        _viewModel.SelectedTerminalPath = selected ?? terminals.FirstOrDefault()?.TerminalPath;
        var mt4 = _viewModel.SelectedPlatform == TradingPlatform.Mt4;
        PlatformDescription.Text = TradePet.Core.Localization.UiText.Translate(mt4
            ? "MT4 · 账户、持仓、订单历史复盘、日报、图表计划与亏损区域；经济日历使用公开周历。无需 Python。"
            : "MT5 · 账户与持仓监控；对冲账户支持完整复盘。图表计划和经济日历需要桥接插件。");
        PythonPanel.Visibility = mt4 ? Visibility.Collapsed : Visibility.Visible;
        BridgeInstructions.Text = TradePet.Core.Localization.UiText.Translate(mt4
            ? "安装或升级后，将新版 TradePet / TradePetBridge 重新挂到一个图表，并保持运行。账户历史中右键选择“全部历史”。\n\n首次运行等待报价校时；休市时可在插件参数填写经纪商当前 UTC 偏移分钟数。无需 DLL 或实盘交易权限。"
            : "安装后，在 MT5 导航器的 EA 列表右键刷新，将 TradePet / TradePetBridge 拖到一个图表。\n\n保持终端和图表打开，看到“桥接插件已连接”即完成。");
    }

    private void UpdateStep()
    {
        var panels = new[] { PlatformStep, PrepareStep, VerifyStep, PreferencesStep };
        for (var i = 0; i < panels.Length; i++) panels[i].Visibility = i == _step ? Visibility.Visible : Visibility.Collapsed;
        StepLabel.Text = TradePet.Core.Localization.UiText.Translate($"第 {_step + 1} 步，共 4 步");
        BackButton.IsEnabled = _step > 0;
        NextButton.Content = _step == 3 ? "保存并完成设置" : "下一步";
        StepScroll.ScrollToTop();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        StepScroll.IsEnabled = BackButton.IsEnabled = NextButton.IsEnabled = LaterButton.IsEnabled = false;
        Status.Text = TradePet.Core.Localization.UiText.Translate("正在处理…");
        try { await action(); }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception exception) { AppLog.Write($"Setup failed: {exception}"); Status.Text = TradePet.Core.Localization.UiText.Translate(exception.Message); }
        finally
        {
            _busy = false;
            StepScroll.IsEnabled = !_saved;
            BackButton.IsEnabled = !_saved && _step > 0;
            NextButton.IsEnabled = !_saved;
            LaterButton.IsEnabled = true;
        }
    }

    private void Platform_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (PlatformPicker.SelectedValue is TradingPlatform platform) _viewModel.SelectedPlatform = platform;
        RefreshTerminals();
        InstallStatus.Text = TradePet.Core.Localization.UiText.Translate(ConnectionCheck.Text = TradePet.Core.Localization.UiText.Translate(Status.Text = TradePet.Core.Localization.UiText.Translate(string.Empty)));
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshTerminals();
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var mt4 = _viewModel.SelectedPlatform == TradingPlatform.Mt4;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = TradePet.Core.Localization.UiText.Translate(mt4 ? "MT4 终端|terminal.exe" : "MT5 终端|terminal64.exe"), CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        if (!Mt5TerminalDiscovery.IsTerminalPath(dialog.FileName, _viewModel.SelectedPlatform)) { Status.Text = TradePet.Core.Localization.UiText.Translate("请选择对应平台的终端程序。"); return; }
        _viewModel.SelectedTerminalPath = dialog.FileName;
        RefreshTerminals();
    }
    private void Back_Click(object sender, RoutedEventArgs e) { _step--; Status.Text = TradePet.Core.Localization.UiText.Translate(string.Empty); UpdateStep(); }
    private void Later_Click(object sender, RoutedEventArgs e) => Close();
    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step < 3) { _step++; Status.Text = TradePet.Core.Localization.UiText.Translate(string.Empty); UpdateStep(); return; }
        await RunAsync(async () =>
        {
            if (!await _save()) { Status.Text = TradePet.Core.Localization.UiText.Translate("设置未能保存，请检查数据目录权限后重试。下次启动仍会显示向导。"); return; }
            _saved = true;
            Status.Text = TradePet.Core.Localization.UiText.Translate("设置已保存。若切换了平台 / 终端或修复了环境，请从桌宠右键菜单退出，再重新启动 TradePet。");
            LaterButton.Content = "关闭向导";
        });
    }
    private async void Install_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        var terminal = SetupOperations.RequireTerminal(_viewModel.SelectedTerminalPath, _viewModel.SelectedPlatform);
        InstallStatus.Text = TradePet.Core.Localization.UiText.Translate("已安装到：" + SetupOperations.InstallBridge(terminal));
        Status.Text = TradePet.Core.Localization.UiText.Translate("插件文件已就绪；请按上面的说明挂到图表。");
        return Task.CompletedTask;
    });
    private async void OpenData_Click(object sender, RoutedEventArgs e) => await RunAsync(() =>
    {
        var terminal = SetupOperations.RequireTerminal(_viewModel.SelectedTerminalPath, _viewModel.SelectedPlatform);
        var directory = terminal.DataDirectory ?? throw new InvalidOperationException("尚未找到数据目录，请先启动一次交易终端。");
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        Status.Text = TradePet.Core.Localization.UiText.Translate("已打开终端数据目录。");
        return Task.CompletedTask;
    });
    private async void CheckPython_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        PythonStatus.Text = TradePet.Core.Localization.UiText.Translate(await SetupOperations.CheckPythonAsync(_closed.Token) ? "Python 和 MetaTrader5 依赖已就绪。"
            : RuntimePaths.Resolve().PythonIsBundled ? "内置 Python 环境不完整或无法启动，请退出后重新完整解压发布包。"
            : "依赖未就绪，请点击修复。");
        Status.Text = TradePet.Core.Localization.UiText.Translate(PythonStatus.Text);
    });
    private async void RepairPython_Click(object sender, RoutedEventArgs e) => await RepairAsync(null);
    private async void BrowsePython_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = TradePet.Core.Localization.UiText.Translate("Python 解释器|python.exe"), CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await RepairAsync(dialog.FileName);
    }
    private Task RepairAsync(string? path) => RunAsync(async () =>
    {
        Status.Text = TradePet.Core.Localization.UiText.Translate("正在准备 Python 依赖，可能需要几分钟…");
        var code = await SetupOperations.RepairPythonAsync(path, _closed.Token);
        if (code != 0) throw new InvalidOperationException("依赖修复失败。请检查网络、选择已安装的 64 位 Python 3.13 后重试；详细原因已写入日志。");
        PythonStatus.Text = TradePet.Core.Localization.UiText.Translate(await SetupOperations.CheckPythonAsync(_closed.Token) ? "环境已就绪；请在完成设置后重启 TradePet。" : "修复后检测未通过，请检查 Python 版本。");
        Status.Text = TradePet.Core.Localization.UiText.Translate(PythonStatus.Text);
    });
    private async void CheckConnection_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var terminal = SetupOperations.RequireTerminal(_viewModel.SelectedTerminalPath, _viewModel.SelectedPlatform);
        if (terminal.Platform == TradingPlatform.Mt4)
        {
            if (terminal.DataDirectory is null) throw new InvalidOperationException("请先启动 MT4 并安装插件。");
            var path = Path.Combine(terminal.DataDirectory, "MQL4", "Files", Mt4FileClient.SnapshotFileName);
            var frame = Mt4FileClient.ReadFrame(await File.ReadAllTextAsync(path, _closed.Token), terminal.TerminalPath, DateTimeOffset.UtcNow);
            ConnectionCheck.Text = TradePet.Core.Localization.UiText.Translate(frame.Connected ? "所选 MT4 插件正在输出有效账户数据。保存并重启后开始监控。" : "插件已运行，但 MT4 尚未连接账户服务器。");
        }
        else
        {
            ConnectionCheck.Text = TradePet.Core.Localization.UiText.Translate(terminal.IsRunning ? "所选 MT5 已运行。请结合账户和桥接状态确认连接；切换终端后需重启。" : "所选 MT5 尚未运行，请先启动并登录。");
        }
        Status.Text = TradePet.Core.Localization.UiText.Translate("检查完成。");
    });
}
