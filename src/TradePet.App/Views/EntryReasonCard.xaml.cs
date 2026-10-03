using System.Windows;
using System.Windows.Controls;
using TradePet.Core.Domain;

namespace TradePet.App.Views;

public partial class EntryReasonCard : System.Windows.Controls.UserControl
{
    private bool _saving;
    public bool IsCompleted { get; private set; }
    public string Reason => ReasonBox.Text.Trim();
    public Func<EntryReasonCard, Task<string?>>? SaveReasonAsync { get; set; }
    public event Action<EntryReasonCard>? Completed;

    public EntryReasonCard(TradeRecord trade, int serverUtcOffsetSeconds)
    {
        InitializeComponent();
        TradeText.Text = $"{trade.Symbol} · {(trade.Side == TradeSide.Buy ? "买入" : "卖出")} · #{trade.PositionId} · {trade.OpenedAtUtc.ToOffset(TimeSpan.FromSeconds(serverUtcOffsetSeconds)):HH:mm:ss} 服务器";
    }
    private void Reason_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string reason }) ReasonBox.Text = reason;
    }
    private void Reason_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ChosenReasonText is not null) ChosenReasonText.Text = Reason;
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saving || IsCompleted) return;
        if (Reason.Length == 0) { StatusText.Text = "请选择原因，或点击跳过。"; return; }
        _saving = true;
        FooterButtons.IsEnabled = false;
        StatusText.Text = "正在记录…";
        string? error;
        try { error = SaveReasonAsync is null ? "本地记录尚未就绪，请稍后重试。" : await SaveReasonAsync(this); }
        catch (Exception) { error = "记录失败，内容仍保留，请重试。"; }
        finally { _saving = false; FooterButtons.IsEnabled = true; }
        if (error is not null) { StatusText.Text = error; return; }
        Dismiss();
    }
    private void Skip_Click(object sender, RoutedEventArgs e) => Dismiss();
    public void Dismiss()
    {
        if (_saving || IsCompleted) return;
        IsCompleted = true;
        Completed?.Invoke(this);
    }
}
