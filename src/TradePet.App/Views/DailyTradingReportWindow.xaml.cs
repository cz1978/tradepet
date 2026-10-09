using System.IO;
using System.Windows;
using System.Windows.Media;
using TradePet.App.Runtime;
using TradePet.Application.Review;
using TradePet.Core.Domain;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace TradePet.App.Views;

public sealed record DailyTradingReport(
    string AccountKey,
    string Currency,
    DateOnly ServerDate,
    bool IsLive,
    decimal RealizedCashPnl,
    decimal Fees,
    int TradeCount,
    int WinCount,
    int LossCount,
    int BreakevenCount,
    decimal? WinRate,
    TradeRecord? BestTrade,
    TradeRecord? WorstTrade,
    int SampledTradeCount,
    int ReliableSampleCount,
    int ReviewedCount,
    int PendingReviewCount,
    int BehaviorAlertCount,
    int CooldownViolationCount,
    string Markdown,
    string? ArchivePath = null,
    DailyReportAnalysis? Analysis = null,
    DailyJournal? Journal = null,
    string DailySourceVersion = "");

public partial class DailyTradingReportWindow : Window
{
    private static readonly WpfBrush ProfitBrush = new SolidColorBrush(WpfColor.FromRgb(77, 198, 163));
    private static readonly WpfBrush LossBrush = new SolidColorBrush(WpfColor.FromRgb(240, 107, 120));
    private static readonly WpfBrush FlatBrush = new SolidColorBrush(WpfColor.FromRgb(242, 245, 244));
    private DailyTradingReport _report;
    private bool _savingDailyReview;

    public DailyTradingReportWindow(DailyTradingReport report)
    {
        InitializeComponent();
        _report = report;
        ApplyReport(report);
    }

    public void ApplyReport(DailyTradingReport report)
    {
        _report = report;
        TitleText.Text = TradePet.Core.Localization.UiText.Translate($"{report.ServerDate:yyyy-MM-dd} 交易日报");
        AccountText.Text = TradePet.Core.Localization.UiText.Translate($"{report.AccountKey} · {report.Currency}");
        StatusText.Text = TradePet.Core.Localization.UiText.Translate(report.IsLive ? "今日实时" : "已结束");
        NetPnlText.Text = TradePet.Core.Localization.UiText.Translate(FormatMoney(report.RealizedCashPnl, report.Currency));
        NetPnlText.Foreground = PnlBrush(report.RealizedCashPnl);
        SummaryText.Text = TradePet.Core.Localization.UiText.Translate(BuildSummary(report));
        TradeCountText.Text = TradePet.Core.Localization.UiText.Translate($"{report.TradeCount} 笔");
        WinRateText.Text = TradePet.Core.Localization.UiText.Translate(report.WinRate is null ? "—" : $"{report.WinRate:0.#}%");
        WinLossText.Text = TradePet.Core.Localization.UiText.Translate($"{report.WinCount} / {report.LossCount}");
        FeesText.Text = TradePet.Core.Localization.UiText.Translate(FormatMoney(report.Fees, report.Currency));
        BestTradeText.Text = TradePet.Core.Localization.UiText.Translate(FormatTrade(report.BestTrade, report.Currency));
        WorstTradeText.Text = TradePet.Core.Localization.UiText.Translate(FormatTrade(report.WorstTrade, report.Currency));
        ProcessText.Text = TradePet.Core.Localization.UiText.Translate($"有持仓采样 {report.SampledTradeCount}/{report.TradeCount} 笔 · 全程采样可靠 {report.ReliableSampleCount}/{report.TradeCount} 笔");
        ReviewText.Text = TradePet.Core.Localization.UiText.Translate($"已自动分析 {report.TradeCount} 笔 · {DailyReviewStatus(report)}");
        TradeReviewText.Text = TradePet.Core.Localization.UiText.Translate($"逐笔复盘（可选）：已保存 {report.ReviewedCount}/{report.TradeCount} 笔");
        DailyReviewStatusText.Text = TradePet.Core.Localization.UiText.Translate(DailyReviewStatus(report));
        DailySummaryBox.Text = report.Journal?.PostMarketSummary ?? TradePet.Core.Localization.UiText.Translate(BuildSummary(report)) + "\n" +
            string.Join("\n", report.Analysis?.Sections.Where(section => section.Title is "风险、回撤与持仓" or "执行纪律与关联结果")
                .Select(section => TradePet.Core.Localization.UiText.Translate(section.Lines.FirstOrDefault() ?? string.Empty)) ?? []);
        DailyActionBox.Text = report.Journal?.NextAction ?? string.Empty;
        DailyActionChoices.ItemsSource = report.Analysis?.Sections.FirstOrDefault(section => section.Title == "下一交易日行动清单")?
            .Lines.Take(3).Select(line => TradePet.Core.Localization.UiText.Translate(line)).ToArray();
        BehaviorText.Text = TradePet.Core.Localization.UiText.Translate(report.BehaviorAlertCount == 0
            ? "没有记录到风险提醒；不代表全天没有风险"
            : $"{report.BehaviorAlertCount} 条风险提醒，其中冷静期触发 {report.CooldownViolationCount} 条（不含正常检查）");
        ArchivePathText.Text = TradePet.Core.Localization.UiText.Translate(string.IsNullOrWhiteSpace(report.ArchivePath)
            ? "Markdown 尚未归档。"
            : $"已归档：{report.ArchivePath}");
        ArchivePathText.ToolTip = report.ArchivePath;
        AnalysisSections.ItemsSource = report.Analysis?.Sections;
        TradeDetailsGrid.ItemsSource = report.Analysis?.Trades;
        FullReportText.Text = report.Markdown;
        TradeDetailHint.Text = TradePet.Core.Localization.UiText.Translate(report.Analysis?.Trades.Count > 0
            ? $"共 {report.Analysis.Trades.Count} 笔完整平仓交易；横向滚动可查看费用、风险和退出原因。金额单位：{report.Currency}。"
            : "没有完整平仓交易；当日成交和未结交易请查看完整日报。");
    }

    public DateOnly ServerDate => _report.ServerDate;
    public DailyTradingReport Report => _report;
    public Func<string, string, Task<ReviewSaveResult<DailyJournal>>>? SaveDailyReviewAsync { get; set; }
    public event EventHandler? OpenReviewRequested;

    private static string DailyReviewStatus(DailyTradingReport report) => report.Journal switch
    {
        null => "日总结未保存",
        { Status: ReviewCompletionStatus.Reviewed } journal when
            (journal.ReviewedSourceVersion ?? journal.SourceVersion) == report.DailySourceVersion => "日总结已完成",
        { Status: ReviewCompletionStatus.Reviewed or ReviewCompletionStatus.NeedsReview } => "日数据更新，日总结需重审",
        _ => "日总结草稿",
    };

    private static string BuildSummary(DailyTradingReport report)
    {
        if (report.TradeCount == 0)
        {
            return report.IsLive ? "今天还没有完整平仓交易。" : "该交易日没有完整平仓交易。";
        }

        var direction = report.RealizedCashPnl switch
        {
            > 0.01m => "当日盈利",
            < -0.01m => "当日亏损",
            _ => "当日基本持平",
        };
        return $"{direction}；{report.WinCount} 胜、{report.LossCount} 负、{report.BreakevenCount} 平。";
    }

    private static string FormatMoney(decimal value, string currency) =>
        $"{value:+0.##;-0.##;0} {currency}";

    private static string FormatTrade(TradeRecord? trade, string currency) => trade is null
        ? "—"
        : $"{trade.Symbol} · {(trade.Side == TradeSide.Buy ? "买入" : "卖出")} · {FormatMoney(trade.NetPnl, currency)}";

    private static WpfBrush PnlBrush(decimal value) => value switch
    {
        > 0.01m => ProfitBrush,
        < -0.01m => LossBrush,
        _ => FlatBrush,
    };

    private void CopyMarkdown_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_report.Markdown);
            ArchivePathText.Text = TradePet.Core.Localization.UiText.Translate("完整 Markdown 已复制，可直接粘贴给 AI。");
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(TradePet.Core.Localization.UiText.Translate(exception.Message), TradePet.Core.Localization.UiText.Translate("复制 Markdown 失败"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveMarkdown_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfSaveFileDialog
        {
            Title = TradePet.Core.Localization.UiText.Translate("保存交易日报"),
            Filter = TradePet.Core.Localization.UiText.Translate("Markdown 文件 (*.md)|*.md|所有文件 (*.*)|*.*"),
            DefaultExt = ".md",
            AddExtension = true,
            FileName = $"{_report.ServerDate:yyyy-MM-dd}-交易日报.md",
            InitialDirectory = string.IsNullOrWhiteSpace(_report.ArchivePath)
                ? null
                : Path.GetDirectoryName(_report.ArchivePath),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _report.Markdown);
            ArchivePathText.Text = TradePet.Core.Localization.UiText.Translate($"已保存：{dialog.FileName}");
            ArchivePathText.ToolTip = dialog.FileName;
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(TradePet.Core.Localization.UiText.Translate(exception.Message), TradePet.Core.Localization.UiText.Translate("保存 Markdown 失败"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenReview_Click(object sender, RoutedEventArgs e) => OpenReviewRequested?.Invoke(this, EventArgs.Empty);
    private void BeginDailyReview_Click(object sender, RoutedEventArgs e)
    {
        ReportTabs.SelectedItem = DailyReviewTab;
        DailyActionBox.Focus();
    }

    private void DailyActionChoices_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DailyActionChoices.SelectedItem is string action) DailyActionBox.Text = action;
    }

    private async void SaveDailyReview_Click(object sender, RoutedEventArgs e)
    {
        if (_savingDailyReview) return;
        if (SaveDailyReviewAsync is null)
        {
            DailyReviewStatusText.Text = TradePet.Core.Localization.UiText.Translate("日总结保存暂不可用，请等待账户和本地复盘库就绪。");
            return;
        }
        _savingDailyReview = true;
        DailyReviewForm.IsEnabled = false;
        try
        {
            var result = await SaveDailyReviewAsync(DailySummaryBox.Text.Trim(), DailyActionBox.Text.Trim());
            if (result.IsSaved)
            {
                _report = _report with { Journal = result.Value! };
                ReviewText.Text = TradePet.Core.Localization.UiText.Translate($"已自动分析 {_report.TradeCount} 笔 · {DailyReviewStatus(_report)}");
                DailyReviewStatusText.Text = TradePet.Core.Localization.UiText.Translate(DailyReviewStatus(_report));
            }
            else DailyReviewStatusText.Text = TradePet.Core.Localization.UiText.Translate(result.Message);
        }
        catch (Exception exception)
        {
            AppLog.Write($"Daily report review save failed: {exception}");
            DailyReviewStatusText.Text = TradePet.Core.Localization.UiText.Translate("日总结保存失败，内容仍保留，请重试。");
        }
        finally
        {
            _savingDailyReview = false;
            DailyReviewForm.IsEnabled = true;
        }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
