using System.Windows;
using System.Windows.Controls;
using RadioButton = System.Windows.Controls.RadioButton;
using TradePet.Core.Domain;

namespace TradePet.App.Views;

public partial class EntryReasonCard : System.Windows.Controls.UserControl
{
    private bool _saving;
    public bool IsCompleted { get; private set; }
    public bool SkipAllRequested { get; private set; }
    public string Reason => ReasonBox.Text.Trim();
    public PlanExecutionSelfReport? ReportedExecution => Enum.TryParse<PlanExecutionSelfReport>(
        ExecutionChoices.Children.OfType<RadioButton>().FirstOrDefault(item => item.IsChecked == true)?.Tag as string,
        out var value) ? value : null;
    public string Emotion => EmotionChoices.Children.OfType<RadioButton>()
        .FirstOrDefault(item => item.IsChecked == true)?.Tag as string ?? string.Empty;
    public Func<EntryReasonCard, Task<string?>>? SaveReasonAsync { get; set; }
    public Func<bool, Task<ReviewAttachment?>>? AddScreenshotAsync { get; set; }
    private readonly List<ReviewAttachment> _screenshots = [];
    public event Action<EntryReasonCard>? Completed;

    public EntryReasonCard(TradeRecord trade, int serverUtcOffsetSeconds)
    {
        InitializeComponent();
        TradeText.Text = TradePet.Core.Localization.UiText.Translate($"{trade.Symbol} · {(trade.Side == TradeSide.Buy ? "买入" : "卖出")} · #{trade.PositionId} · {trade.OpenedAtUtc.ToOffset(TimeSpan.FromSeconds(serverUtcOffsetSeconds)):HH:mm:ss} 服务器");
    }
    private void Reason_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string reason }) ReasonBox.Text = TradePet.Core.Localization.UiText.Translate(reason);
    }
    private void Reason_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ChosenReasonText is not null) ChosenReasonText.Text = Reason;
    }
    public void SetScreenshots(IEnumerable<ReviewAttachment> attachments)
    {
        _screenshots.Clear();
        _screenshots.AddRange(attachments.Where(item => item.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)));
        ScreenshotsText.Text = string.Join("\n", _screenshots.Select(item => "• " + item.Title));
    }
    private async void PasteScreenshot_Click(object sender, RoutedEventArgs e) => await AddScreenshotAsyncCore(paste: true);
    private async void ImportScreenshot_Click(object sender, RoutedEventArgs e) => await AddScreenshotAsyncCore(paste: false);
    private async Task AddScreenshotAsyncCore(bool paste)
    {
        if (_saving || IsCompleted) return;
        _saving = true;
        EntryForm.IsEnabled = FooterButtons.IsEnabled = false;
        StatusText.Text = TradePet.Core.Localization.UiText.Translate("正在保存截图…");
        try
        {
            if (AddScreenshotAsync is null) throw new InvalidOperationException("本地记录尚未就绪，请稍后重试。");
            var attachment = await AddScreenshotAsync(paste);
            if (attachment is null) { StatusText.Text = string.Empty; return; }
            SetScreenshots(_screenshots.Append(attachment).ToArray());
            StatusText.Text = TradePet.Core.Localization.UiText.Translate("截图已保存到这笔交易的复盘附件。");
        }
        catch (Exception exception)
        {
            TradePet.App.Runtime.AppLog.Write($"Entry screenshot failed: {exception}");
            StatusText.Text = TradePet.Core.Localization.UiText.Translate(exception.Message);
        }
        finally { _saving = false; EntryForm.IsEnabled = FooterButtons.IsEnabled = true; }
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saving || IsCompleted) return;
        if (Reason.Length == 0) { StatusText.Text = TradePet.Core.Localization.UiText.Translate("请选择原因，或点击跳过。"); return; }
        _saving = true;
        EntryForm.IsEnabled = false;
        FooterButtons.IsEnabled = false;
        StatusText.Text = TradePet.Core.Localization.UiText.Translate("正在记录…");
        string? error;
        try { error = SaveReasonAsync is null ? "本地记录尚未就绪，请稍后重试。" : await SaveReasonAsync(this); }
        catch (Exception) { error = "记录失败，内容仍保留，请重试。"; }
        finally { _saving = false; EntryForm.IsEnabled = true; FooterButtons.IsEnabled = true; }
        if (error is not null) { StatusText.Text = TradePet.Core.Localization.UiText.Translate(error); return; }
        Dismiss();
    }
    private void Skip_Click(object sender, RoutedEventArgs e) => Dismiss();
    private void SkipAll_Click(object sender, RoutedEventArgs e)
    {
        if (_saving || IsCompleted) return;
        SkipAllRequested = true;
        Dismiss();
    }
    public void Dismiss()
    {
        if (_saving || IsCompleted) return;
        IsCompleted = true;
        Completed?.Invoke(this);
    }
}
