using System.Windows;
using System.Windows.Controls;
using TradePet.Application.Review;
using TradePet.Core.Domain;

namespace TradePet.App.Views;

public partial class QuickReviewCard : System.Windows.Controls.UserControl
{
    private bool _completed;
    private bool _saving;
    public QuickReviewCard(TradeDetailData detail, int serverUtcOffsetSeconds = 0)
    {
        InitializeComponent();
        var trade = detail.Trade;
        TradeText.Text = $"{trade.Symbol} · {trade.NetPnl:+0.##;-0.##;0} · {trade.ClosedAtUtc?.ToOffset(TimeSpan.FromSeconds(serverUtcOffsetSeconds)):yyyy-MM-dd HH:mm} 服务器";
        var analysis = QuickReviewAnalyzer.Analyze(detail);
        DocumentRevision = detail.Document?.Revision ?? 0;
        ExitReasonBox.Text = detail.Document?.ExitReason ?? analysis.ExitReason;
        AnalysisText.Text = detail.Document?.Summary ?? analysis.Explanation;
        ImproveBox.Text = detail.Document?.ToImprove ?? analysis.Improvement;
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

    private void ReasonPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string reason }) return;
        ExitReasonBox.Text = reason;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saving) return;
        _saving = true;
        FooterButtons.IsEnabled = false;
        SaveStatusText.Text = "正在保存…";
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
            FooterButtons.IsEnabled = true;
        }
        if (error is not null) { SaveStatusText.Text = error; return; }
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
