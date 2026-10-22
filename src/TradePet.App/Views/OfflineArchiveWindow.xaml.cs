using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using TradePet.App.Runtime;
using TradePet.App.ViewModels;
using TradePet.Core.Domain;
using TradePet.Core.Localization;

namespace TradePet.App.Views;

public partial class OfflineArchiveWindow : Window
{
    private readonly TradePetRuntime _runtime;
    private bool _closing;
    private bool _allowClose;
    private bool _selecting;
    private LocalAccountArchive? _selectedAccount;

    public OfflineArchiveWindow(MainViewModel model, TradePetRuntime runtime, IReadOnlyList<LocalAccountArchive> accounts)
    {
        _runtime = runtime;
        InitializeComponent();
        DataContext = model;
        Accounts.ItemsSource = accounts;
        Loaded += (_, _) => { if (accounts.Count > 0) Accounts.SelectedIndex = 0; else ArchiveStatus.Text = UiText.Get("FunctionalOfflineNoAccounts"); };
    }

    private async void Account_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_selecting || Accounts.SelectedItem is not LocalAccountArchive account) return;
        _selecting = true;
        Accounts.IsEnabled = false;
        try
        {
            var saved = await _runtime.PrepareForShutdownAsync();
            await _runtime.ResumeAfterCancelledShutdownAsync();
            if (!saved.CanExit)
            {
                ArchiveStatus.Text = UiText.Translate(saved.Message);
                Accounts.SelectedItem = _selectedAccount;
                return;
            }
            await _runtime.SelectOfflineAccountAsync(account);
            _selectedAccount = account;
            ArchiveStatus.Text = UiText.Get(account.TimeSegment is null ? "FunctionalOfflineTimeUnknown" : "FunctionalOfflineHint");
        }
        catch (Exception exception) { ArchiveStatus.Text = exception.Message; }
        finally { Accounts.IsEnabled = true; _selecting = false; }
    }

    internal Task<ShutdownPreparationResult> PrepareForCloseAsync() => _runtime.PrepareForShutdownAsync();
    internal Task ResumeEditsAsync() => _runtime.ResumeAfterCancelledShutdownAsync();

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_allowClose) { base.OnClosing(e); return; }
        e.Cancel = true;
        base.OnClosing(e);
        if (_closing) return;
        _closing = true;
        try
        {
            var result = await _runtime.PrepareForShutdownAsync();
            if (!result.CanExit)
            {
                ArchiveStatus.Text = UiText.Translate(result.Message);
                await _runtime.ResumeAfterCancelledShutdownAsync();
                return;
            }
            await _runtime.DisposeAsync();
            _allowClose = true;
            Close();
        }
        finally { _closing = false; }
    }
}
