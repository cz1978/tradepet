using System.Windows;
using System.Windows.Controls;
using RadioButton = System.Windows.Controls.RadioButton;
using TradePet.Application.Review;
using TradePet.Core.Domain;

namespace TradePet.App.Views;

public partial class QuickReviewCard : System.Windows.Controls.UserControl
{
    private bool _completed;
    private bool _saving;
    private readonly string _recordedEmotion;
    public QuickReviewCard(TradeDetailData detail, int serverUtcOffsetSeconds = 0)
    {
        InitializeComponent();
        var trade = detail.Trade;
        TradeText.Text = TradePet.Core.Localization.UiText.Translate($"{trade.Symbol} · {trade.NetPnl:+0.##;-0.##;0} · {trade.ClosedAtUtc?.ToOffset(TimeSpan.FromSeconds(serverUtcOffsetSeconds)):yyyy-MM-dd HH:mm} 服务器");
        var analysis = QuickReviewAnalyzer.Analyze(detail);
        DocumentRevision = detail.Document?.Revision ?? 0;
        ExitReasonBox.Text = detail.Document?.ExitReason ?? TradePet.Core.Localization.UiText.Translate(analysis.ExitReason);
        AnalysisText.Text = detail.Document?.Summary ?? TradePet.Core.Localization.UiText.Translate(analysis.Explanation);
        ImproveBox.Text = detail.Document?.ToImprove ?? TradePet.Core.Localization.UiText.Translate(analysis.Improvement);
        _recordedEmotion = detail.Document?.Emotion ?? string.Empty;
        foreach (var choice in ExecutionChoices.Children.OfType<RadioButton>())
            choice.IsChecked = detail.Document?.ReportedExecution is { } reported && (string)choice.Tag == reported.ToString();
        foreach (var choice in EmotionChoices.Children.OfType<RadioButton>())
            choice.IsChecked = (string)choice.Tag == _recordedEmotion ||
                TradePet.Core.Localization.UiText.Translate((string)choice.Tag) == _recordedEmotion;
    }

    public bool SaveRequested { get; private set; }
    public int DocumentRevision { get; }
    public bool RemindLater { get; private set; }
    public bool IsCompleted => _completed;
    public event Action<QuickReviewCard>? Completed;
    public Func<QuickReviewCard, Task<string?>>? SaveReviewAsync { get; set; }
    public Action? ShowSavedReviews { get; set; }
    public string ExitReason => ExitReasonBox.Text.Trim();
    public string Improvement => ImproveBox.Text.Trim();
    public string AnalysisSummary => AnalysisText.Text;
    public PlanExecutionSelfReport? ReportedExecution => Enum.TryParse<PlanExecutionSelfReport>(
        ExecutionChoices.Children.OfType<RadioButton>().FirstOrDefault(item => item.IsChecked == true)?.Tag as string,
        out var value) ? value : null;
    public string Emotion => EmotionChoices.Children.OfType<RadioButton>()
        .FirstOrDefault(item => item.IsChecked == true)?.Tag as string ?? _recordedEmotion;

    private void ReasonPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string reason }) return;
        ExitReasonBox.Text = TradePet.Core.Localization.UiText.Translate(reason);
    }

    private void ExitReasonBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var reason = ExitReasonBox.Text.Trim();
        foreach (var choice in ReasonChoices.Children.OfType<RadioButton>())
        {
            var preset = (string)choice.Tag;
            var inferred = preset switch
            {
                "触发预设止盈价，自动平仓" => "触及止盈价离场（推测）",
                "触发预设止损价，自动平仓" => "触及止损价离场（推测）",
                _ => null,
            };
            choice.IsChecked = Matches(preset) || inferred is not null && Matches(inferred);
        }

        bool Matches(string value) => reason == value ||
            reason == TradePet.Core.Localization.UiText.Translate(value, "en-US");
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saving || _completed) return;
        _saving = true;
        ReviewForm.IsEnabled = false;
        FooterButtons.IsEnabled = false;
        SaveStatusText.Text = TradePet.Core.Localization.UiText.Translate("正在保存…");
        string? error;
        try
        {
            error = SaveReviewAsync is null ? "复盘存储尚未连接，请稍后重试。" : await SaveReviewAsync(this);
        }
        catch (Exception)
        {
            error = "保存失败，内容仍保留在窗口中，请重试。";
        }
        finally
        {
            _saving = false;
            ReviewForm.IsEnabled = true;
            FooterButtons.IsEnabled = true;
        }
        if (error is not null) { SaveStatusText.Text = TradePet.Core.Localization.UiText.Translate(error); return; }
        SaveRequested = true;
        Finish();
    }
    private void SavedReviews_Click(object sender, RoutedEventArgs e) => ShowSavedReviews?.Invoke();
    public void Dismiss() => Finish();
    private void Later_Click(object sender, RoutedEventArgs e) { RemindLater = true; Finish(); }
    private void Skip_Click(object sender, RoutedEventArgs e) => Finish();
    private void Finish()
    {
        if (_completed || _saving) return;
        _completed = true;
        Completed?.Invoke(this);
    }
}
