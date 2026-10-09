using TradePet.Application.Review;
using TradePet.App.ViewModels.Review;
using TradePet.Core.Domain;
using TradePet.Core.Review;
using TradePet.Core.Trading;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TradePet.App.Controls;
using TradePet.App.Views;
using Xunit;

namespace TradePet.App.Tests;

public sealed class ReviewDashboardTests
{
    [Fact]
    public void FailedQuery_RemovesStaleStatisticsAndExportsAndCanRecoverOnNextSuccessfulQuery()
    {
        var fixture = Fixture([Trade(1, 20m), Trade(2, -5m)]);
        var viewModel = new ReviewWorkspaceViewModel();
        viewModel.Apply(fixture.Snapshot, fixture.Data, 1);
        viewModel.DailyFacts.Add(new DailyFactRow("旧完成率", "0%", "旧快照"));
        Assert.NotEmpty(viewModel.PerformanceCharts);
        Assert.Equal(2, viewModel.TotalCount);

        viewModel.MarkDataUnavailable("查询失败");
        Assert.Empty(viewModel.PerformanceCharts);
        Assert.Empty(viewModel.CalendarMonthCells);
        Assert.Empty(viewModel.Trades);
        Assert.Equal(0, viewModel.TotalCount);
        Assert.False(viewModel.CanConfirmExport);
        Assert.Equal("读取失败", Assert.Single(viewModel.DailyFacts).Value);
        Assert.Equal("查询失败", viewModel.DashboardScope);

        viewModel.Apply(fixture.Snapshot, fixture.Data, 1);
        Assert.Equal(2, viewModel.TotalCount);
        Assert.NotEmpty(viewModel.PerformanceCharts);
    }

    [Fact]
    public void SavedQuickReview_RefreshesSelectedDailyCompletionWithoutReclickingCalendar()
    {
        var trade = Trade(1, 20m);
        var date = trade.CloseServerDate!.Value;
        var fixture = Fixture([trade]);
        var viewModel = new ReviewWorkspaceViewModel { DailyDate = date.ToString("yyyy-MM-dd") };
        var calculator = new ReviewWorkspaceCalculator();
        var documents = new Dictionary<long, TradeReviewDocument>();
        var initialFacts = calculator.BuildDailyFacts("Broker|1", date, date, [trade], fixture.Data.Deals,
            documents, [], new Dictionary<DateOnly, DailyState>(), 0);
        viewModel.Apply(fixture.Snapshot with { DailyFacts = initialFacts }, fixture.Data, 1);
        Assert.Equal("0%", viewModel.DailyFacts.Single(item => item.Title == "复盘完成率").Value);

        var at = trade.ClosedAtUtc!.Value;
        documents[1] = new TradeReviewDocument(new("Broker|1", 1), ReviewCompletionStatus.Reviewed,
            "", "确认平仓", "", "", "", "快速复盘", "", "", 1, "source", "rule", "source", "rule", at, at, at, true,
            ReportedExitExecution: ExitExecutionSelfReport.NoPreset, ExitEmotion: "怕利润回吐");
        var updatedFacts = calculator.BuildDailyFacts("Broker|1", date, date, [trade], fixture.Data.Deals,
            documents, [], new Dictionary<DateOnly, DailyState>(), 0);
        var updated = fixture.Snapshot with { DailyFacts = updatedFacts, Documents = documents };
        var data = fixture.Data with { Documents = documents };
        viewModel.Apply(updated, data, 1);
        Assert.Equal("100%", viewModel.DailyFacts.Single(item => item.Title == "复盘完成率").Value);
        var detail = new TradeDetailData(trade, [], null, documents[1], null, [], null, null, [], [], [], null, data.Version);
        viewModel.ApplyDetail(detail, 1);
        Assert.Contains("退出执行自报：未预设退出规则", viewModel.ExitSelfReportFacts);
        Assert.Contains("平仓状态自报：怕利润回吐", viewModel.ExitSelfReportFacts);
        viewModel.ApplyDetail(detail with { Document = null }, 1);
        Assert.Empty(viewModel.ExitSelfReportFacts);
        viewModel.MarkDataUnavailable("查询失败");
        viewModel.Apply(updated, data, 1);
        Assert.Equal("100%", viewModel.DailyFacts.Single(item => item.Title == "复盘完成率").Value);
    }

    [Fact]
    public void Dashboard_UsesEntireFilterAndDrillsIntoTheSelectedTrade()
    {
        var trades = Enumerable.Range(1, 45).Select(index => Trade(index, index % 3 == 0 ? -100m : 70m)).ToArray();
        var (snapshot, data) = Fixture(trades);
        var viewModel = new ReviewWorkspaceViewModel();
        viewModel.Apply(snapshot, data, 1);

        Assert.Single(viewModel.Trades); // Only one archive page row was supplied.
        var cumulative = viewModel.PerformanceCharts[0];
        Assert.Equal(46, cumulative.Points.Count);
        Assert.Equal(0m, cumulative.Points[0].Value);
        Assert.Equal(trades.Sum(item => item.NetPnl), cumulative.Points[^1].Value);
        Assert.Equal(trades.Length, viewModel.PnlDistribution.Sum(item => int.Parse(item.Value.Split(' ')[0])));
        Assert.Equal(3, viewModel.DashboardMonths.Count);
        Assert.Equal(4, viewModel.Metrics.Count);
        Assert.Equal(8, viewModel.DashboardRiskMetrics.Count);
        cumulative.Points[31].OpenCommand!.Execute(null);
        Assert.True(viewModel.HasDashboardSelection);
        Assert.Equal(31, Assert.Single(viewModel.AnalysisDrilldown).PositionId);
        viewModel.DashboardBreakdownDimension = "方向";
        Assert.Equal(2, viewModel.DashboardBreakdown.Count);
        viewModel.DashboardBreakdown[0].OpenCommand.Execute(null);
        Assert.Equal("卖出", viewModel.DashboardBreakdown[0].Label);
        Assert.Equal(22, viewModel.AnalysisDrilldown.Count);
    }

    [Fact]
    public void DailyAndDrawdownCharts_PreserveLossesAndStartingZero()
    {
        var trades = new[] { Trade(1, -40m), Trade(2, 80m), Trade(3, -60m), Trade(4, 90m) };
        trades[1] = trades[1] with { CloseServerDate = trades[0].CloseServerDate };
        var (snapshot, data) = Fixture(trades);
        var viewModel = new ReviewWorkspaceViewModel();
        viewModel.Apply(snapshot, data, 1);

        Assert.Equal(new decimal[] { 0m, -40m, 0m, -60m, 0m }, viewModel.PerformanceCharts[1].Points.Select(point => point.Value));
        Assert.Equal(new decimal[] { 40m, -60m, 90m }, viewModel.PerformanceCharts[2].Points.Select(point => point.Value));
        Assert.True(viewModel.PerformanceCharts[2].IsBar);
        Assert.Equal(0m, viewModel.PerformanceCharts[2].ReferenceValue);
        Assert.Equal(trades.Sum(item => item.NetPnl), viewModel.PerformanceCharts[2].Points.Sum(point => point.Value));
    }

    [Fact]
    public void EmptyAndBreakevenSamples_DoNotInventRatesOrInfinity()
    {
        var viewModel = new ReviewWorkspaceViewModel();
        var empty = Fixture([]);
        viewModel.Apply(empty.Snapshot, empty.Data, 1);
        Assert.All(viewModel.PerformanceCharts, series => Assert.Empty(series.Points));
        Assert.Equal("—", viewModel.Metrics[1].Value);
        Assert.Equal("—", viewModel.Metrics[2].Value);
        Assert.Empty(viewModel.PnlDistribution);
        Assert.Single(viewModel.DashboardInsights);

        var flat = Fixture([Trade(1, 0m), Trade(2, 0.01m), Trade(3, -0.01m)]);
        viewModel.Apply(flat.Snapshot, flat.Data, 1);
        Assert.Equal("—", viewModel.Metrics[1].Value);
        Assert.Equal("—", viewModel.Metrics[2].Value);
        Assert.Equal("3 笔", Assert.Single(viewModel.PnlDistribution).Value);
    }

    [Fact]
    public void Histogram_CountsEveryTradeIncludingDecimalBoundaries()
    {
        var values = new decimal[] { -1m, -0.6666666666666666666666666667m, -0.3333333333333333333333333333m, -0.02m, -0.01m, 0m, 0.01m, 0.02m, 0.3333333333333333333333333333m, 0.6666666666666666666666666667m, 1m };
        var fixture = Fixture(values.Select((pnl, index) => Trade(index + 1, pnl)).ToArray());
        var viewModel = new ReviewWorkspaceViewModel();
        viewModel.Apply(fixture.Snapshot, fixture.Data, 1);
        Assert.Equal(values.Length, viewModel.PnlDistribution.Sum(item => int.Parse(item.Value.Split(' ')[0])));
        Assert.Equal("3 笔", viewModel.PnlDistribution[3].Value);
        Assert.All(viewModel.PnlDistribution, row => Assert.InRange(row.Proportion, 0m, 1m));
    }

    [Fact]
    public void AccountCharts_BreakAtCashFlowAndSamplingGapsAndResetWithAccount()
    {
        var fixture = Fixture([Trade(1, 20m)]);
        var at = fixture.Data.Version.UpdatedAtUtc;
        var points = new[] {
            new ReviewEquityPoint(at, new DateOnly(2026, 8, 1), 1000m, 0m, 0m, 1, 100m, 0m, false),
            new ReviewEquityPoint(at.AddMinutes(1), new DateOnly(2026, 8, 1), 1100m, 0m, 0m, 1, 110m, 0m, false),
            new ReviewEquityPoint(at.AddMinutes(2), new DateOnly(2026, 8, 1), 2100m, 0m, 0m, 2, 100m, 0m, true),
            new ReviewEquityPoint(at.AddHours(2), new DateOnly(2026, 8, 1), 2000m, 100m, 4.76m, 2, 95.24m, 4.76m, false)
        };
        var snapshot = fixture.Snapshot with { EquityAnalysis = new ReviewEquityAnalysis(points, 1, 0, 2, 100m, 4.76m, null, false, "资金流缺口") };
        var viewModel = new ReviewWorkspaceViewModel();
        viewModel.Apply(snapshot, fixture.Data, 1);
        var observed = viewModel.PerformanceCharts[4].Points;
        var unitized = viewModel.PerformanceCharts[5].Points;
        Assert.Equal(observed[0].Segment, observed[2].Segment);
        Assert.NotEqual(observed[2].Segment, observed[3].Segment);
        Assert.NotEqual(unitized[1].Segment, unitized[2].Segment);
        Assert.NotEqual(unitized[2].Segment, unitized[3].Segment);
        Assert.All(unitized, point => Assert.Null(point.OpenCommand));
        viewModel.SelectedPerformanceChart = viewModel.PerformanceCharts[5];
        viewModel.Apply(snapshot, fixture.Data, 1);
        Assert.Equal("单位化净值", viewModel.SelectedPerformanceChart!.Name);
        viewModel.ResetAccountState("换账户");
        Assert.Empty(viewModel.PerformanceCharts);
        Assert.Empty(viewModel.DashboardRiskMetrics);
        Assert.Empty(viewModel.DashboardInsights);
        Assert.Null(viewModel.SelectedPerformanceChart);
    }

    [Fact]
    public void PendingReview_UsesFullQualityEvidenceBeyondTheArchivePage()
    {
        var fixture = Fixture([Trade(1, 10m), Trade(2, -20m), Trade(3, 30m)]);
        var quality = fixture.Snapshot.DataQuality with { Issues = [new ReviewDataQualityIssue("review-pending", "待复盘", "", [new TradeKey("Broker|1", 2)], [])] };
        var viewModel = new ReviewWorkspaceViewModel();
        viewModel.Apply(fixture.Snapshot with { DataQuality = quality }, fixture.Data, 1);
        var pending = Assert.Single(viewModel.DashboardInsights, insight => insight.Title == "待完成复盘");
        Assert.StartsWith("1 / 3", pending.Detail);
        pending.OpenCommand!.Execute(null);
        Assert.Equal(2, Assert.Single(viewModel.AnalysisDrilldown).PositionId);
    }

    [Fact]
    public void Calendar_AlignsWeekdaysNavigatesMonthsAndOpensTheExactDate()
    {
        var fixture = Fixture([Trade(1, 70m), Trade(2, -100m)]);
        var viewModel = new ReviewWorkspaceViewModel();
        viewModel.Apply(fixture.Snapshot, fixture.Data, 1);
        Assert.Equal("2026-10", viewModel.SelectedCalendarMonth);
        viewModel.PreviousCalendarMonthCommand.Execute(null);
        Assert.Equal("2026-09", viewModel.SelectedCalendarMonth);
        viewModel.PreviousCalendarMonthCommand.Execute(null);
        Assert.Equal("2026-08", viewModel.SelectedCalendarMonth);
        Assert.False(viewModel.CanPreviousCalendarMonth);
        Assert.Equal(42, viewModel.CalendarMonthCells.Count);
        Assert.Null(viewModel.CalendarMonthCells[0].Date); // August 1, 2026 is Saturday.
        Assert.Equal(new DateOnly(2026, 8, 1), viewModel.CalendarMonthCells[5].Date);
        var cell = Assert.Single(viewModel.CalendarMonthCells, item => item.Date == fixture.Data.Trades[1].CloseServerDate);
        Assert.Equal(-100m, cell.CashPnl);
        cell.OpenCommand!.Execute(null);
        Assert.Equal(cell.Date!.Value.ToString("yyyy-MM-dd"), viewModel.DailyDate);
        Assert.True(cell.IsSelected);
        viewModel.SelectedCalendarMonth = "2026-10";
        Assert.False(Assert.Single(viewModel.CalendarMonthCells, item => item.Date == new DateOnly(2026, 10, 5)).CanOpen);
        viewModel.ResetAccountState("换账户");
        Assert.Empty(viewModel.CalendarMonthCells);
    }

    [Fact]
    public void StructuredRules_SynchronizeWithExistingSaveAndValidateBeforeSaving()
    {
        var viewModel = new ReviewWorkspaceViewModel();
        Assert.Equal(3, viewModel.PlaybookRuleDrafts.Count);
        viewModel.AddPlaybookRuleCommand.Execute(null);
        Assert.False(viewModel.CanSavePlaybookRules);
        var row = viewModel.PlaybookRuleDrafts[^1];
        row.Name = "确认回踩";
        row.Description = "回踩支撑后才参与";
        row.Section = "管理";
        row.IsCritical = true;
        Assert.True(viewModel.CanSavePlaybookRules);
        Assert.Contains("管理|确认回踩|回踩支撑后才参与|关键", viewModel.PlaybookRules);
        row.Name = "名称|不合法";
        Assert.False(viewModel.CanSavePlaybookRules);
        row.RemoveCommand.Execute(null);
        Assert.True(viewModel.CanSavePlaybookRules);
        Assert.Equal(3, viewModel.PlaybookRuleDrafts.Count);
        viewModel.PlaybookRules = "Risk|止损|开仓前确认风险|关键";
        Assert.Equal("风险", Assert.Single(viewModel.PlaybookRuleDrafts).Section);
        Assert.True(viewModel.CanSavePlaybookRules);
    }

    [Fact]
    public void AnalysisDimensionsAndCampaignSelection_KeepTheOriginalTradeIdentities()
    {
        var fixture = Fixture([Trade(1, 20m), Trade(2, -10m)]);
        var viewModel = new ReviewWorkspaceViewModel();
        viewModel.Apply(fixture.Snapshot, fixture.Data, 1);
        Assert.All(viewModel.FilteredGroupPerformance, row => Assert.Equal("品种", row.Dimension));
        viewModel.AnalysisDimension = "方向";
        Assert.Equal(2, viewModel.FilteredGroupPerformance.Count);
        Assert.All(viewModel.FilteredGroupPerformance, row => Assert.Equal("方向", row.Dimension));
        viewModel.Trades[0].IsSelected = true;
        viewModel.CampaignMembers = "2";
        viewModel.UseSelectedCampaignTradesCommand.Execute(null);
        viewModel.UseSelectedCampaignTradesCommand.Execute(null);
        Assert.Equal("2,1", viewModel.CampaignMembers);
    }

    [Fact]
    public void SavedPlaybook_LoadsIntoRuleEditorAndLinksOpportunityByItsStoredVersion()
    {
        var fixture = Fixture([Trade(1, 20m)]);
        var at = fixture.Data.Version.UpdatedAtUtc;
        var playbook = new PlaybookVersion("pb:v2", "pb", "Broker|1", 2, "突破回踩", "EURUSD", "趋势", "震荡",
            [new PlaybookRule("risk", PlaybookRuleSection.Risk, "先设止损", "首次风险", true, 0)], at, at, true);
        var viewModel = new ReviewWorkspaceViewModel();
        viewModel.Apply(fixture.Snapshot, fixture.Data with { Playbooks = [playbook] }, 1);
        Assert.Single(viewModel.Playbooks).OpenCommand!.Execute(null);
        Assert.Equal("突破回踩", viewModel.PlaybookName);
        Assert.Equal("风险", Assert.Single(viewModel.PlaybookRuleDrafts).Section);
        viewModel.UseLoadedOpportunityPlaybookCommand.Execute(null);
        Assert.Equal("pb:v2", viewModel.OpportunityPlaybook);
        Assert.Equal("突破回踩 · v2", viewModel.OpportunityPlaybookLabel);
        viewModel.ResetAccountState("换账户");
        viewModel.UseLoadedOpportunityPlaybookCommand.Execute(null);
        Assert.Equal("", viewModel.OpportunityPlaybook);
    }

    internal static (ReviewWorkspaceSnapshot Snapshot, ReviewWorkspaceData Data) Fixture(IReadOnlyList<TradeRecord> trades)
    {
        var start = new DateOnly(2026, 8, 1);
        var finish = new DateOnly(2026, 10, 4);
        var version = new ReviewDataVersion("Broker|1", 1, 1, 1, "rules", "time", new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        var calculator = new ReviewWorkspaceCalculator();
        var deals = trades.Select(trade => new DealRecord(trade.PositionId + 1000, trade.PositionId + 2000, trade.PositionId,
            trade.Symbol, trade.Side, DealEntryKind.Out, trade.OpeningVolume, trade.ExitPrice!.Value,
            trade.NetPnl, 0m, 0m, 0m, trade.ClosedAtUtc!.Value)).ToArray();
        var analytics = new ReviewAnalyticsCalculator().Calculate(new ReviewFilter("Broker|1", start, finish), trades);
        var quality = calculator.CalculateDataQuality(trades, new Dictionary<long, TradeExcursion>(),
            new Dictionary<long, TradeReviewDocument>(), new Dictionary<long, TradeReviewMetadata>(), []);
        var snapshot = new ReviewWorkspaceSnapshot(new ReviewWorkspaceFilter("Broker|1", start, finish), analytics,
            trades.Take(1).ToArray(), new Dictionary<long, TradeReviewDocument>(),
            calculator.BuildCalendar("Broker|1", start, finish, trades.ToArray(), deals, new Dictionary<long, TradeReviewDocument>(), new Dictionary<DateOnly, DailyJournal>(), [], 0),
            calculator.BuildRealizedCurve(trades), [], quality, version,
            trades.Count, 1, 1, DailyCash: calculator.BuildDailyCashSeries("Broker|1", start, finish, trades.ToArray(), deals, 0), AllFilteredTrades: trades);
        var data = new ReviewWorkspaceData("Broker|1", "USD", trades, deals, new Dictionary<long, TradeReviewMetadata>(),
            new Dictionary<long, TradeReviewDocument>(), new Dictionary<long, TradeExcursion>(), new Dictionary<DateOnly, DailyJournal>(),
            [], [], [], [], [], [], [], [], [], version);
        return (snapshot, data);
    }

    internal static TradeRecord Trade(int id, decimal pnl)
    {
        var closed = new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero).AddDays(id * 1.4d);
        return new TradeRecord("Broker|1", id, id % 2 == 0 ? "XAUUSD" : "EURUSD", id % 2 == 0 ? TradeSide.Sell : TradeSide.Buy,
            closed.AddHours(-1), closed, DateOnly.FromDateTime(closed.UtcDateTime), DateOnly.FromDateTime(closed.UtcDateTime),
            100m, 110m, 0.1m, 0.1m, 0m, pnl, true);
    }

    // Called on the existing desktop test's STA thread because WPF allows one Application per host.
    internal static void VerifyView()
    {
        VerifyDailyChartAxis();
        var trades = Enumerable.Range(1, 45).Select(index => Trade(index, index % 3 == 0 ? -100m : 70m)).ToArray();
        var fixture = Fixture(trades);
        var viewModel = new ReviewWorkspaceViewModel();
        viewModel.Apply(fixture.Snapshot, fixture.Data, 1);
        var view = new ReviewWorkspaceView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 800, Height = 940, ShowActivated = false,
            Background = new SolidColorBrush(Color.FromRgb(9, 13, 20)) };
        var bindingLog = new StringWriter();
        var listener = new TextWriterTraceListener(bindingLog);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        try
        {
            window.Show();
            Flush();
            var selector = (ComboBox)view.FindName("PerformanceChartSelector");
            var chart = (ReviewPerformanceChart)view.FindName("PerformanceChart");
            Assert.Equal(6, selector.Items.Count);
            Assert.InRange(chart.ActualWidth, 400d, 800d);
            SavePreview(view, "review-overview");
            for (var index = 0; index < selector.Items.Count; index++)
            {
                selector.SelectedIndex = index;
                Flush();
                Assert.Same(viewModel.PerformanceCharts[index], chart.Series);
                var bitmap = Render(chart);
                if (index == 0)
                {
                    var drawing = (DrawingGroup)typeof(ReviewPerformanceChart).GetField("_drawing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(chart)!;
                    var line = Assert.Single(GeometryDrawings(drawing), item => item.Geometry is StreamGeometry);
                    var positions = (Point[])typeof(ReviewPerformanceChart).GetField("_positions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(chart)!;
                    Assert.Equal(positions.Min(point => point.Y), line.Geometry.Bounds.Top, 3);
                    Assert.Equal(positions.Max(point => point.Y), line.Geometry.Bounds.Bottom, 3);
                }
                if (index == 2)
                {
                    SavePreview(chart, "review-daily-chart");
                    SavePreview(view, "review-daily-overview");
                    var redY = ColorMeanY(bitmap, 240, 108, 117);
                    var greenY = ColorMeanY(bitmap, 75, 196, 157);
                    Assert.True(redY < greenY, "盈利柱形应在零轴上方，亏损柱形应在零轴下方。");
                }
            }
            viewModel.SelectedPerformanceChart = viewModel.PerformanceCharts[0];
            viewModel.PerformanceCharts[0].Points[10].OpenCommand!.Execute(null);
            Flush();
            Assert.Single(viewModel.AnalysisDrilldown);
            SavePreview(view, "review-drilldown");
            var scroll = (ScrollViewer)view.FindName("OverviewScroll");
            scroll.ScrollToEnd();
            Flush();
            SavePreview(view, "review-breakdown");
            var tabs = (TabControl)view.FindName("WorkspaceTabs");
            for (var index = 1; index < tabs.Items.Count; index++)
            {
                tabs.SelectedIndex = index;
                Flush();
                if (index == 1) viewModel.SelectedCalendarMonth = "2026-09";
                Flush();
                SavePreview(view, $"review-tab-{index}");
            }
            tabs.SelectedIndex = 0;
            window.Width = 690;
            scroll.ScrollToTop();
            Flush();
            Assert.True(chart.ActualWidth >= 500);
            var empty = Fixture([]);
            viewModel.Apply(empty.Snapshot, empty.Data, 2);
            Flush();
            SavePreview(view, "review-empty");
            listener.Flush();
            Assert.DoesNotContain("Error:", bindingLog.ToString());
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            listener.Dispose();
            window.Close();
        }
        void Flush()
        {
            window.UpdateLayout();
        }
    }

    private static void VerifyDailyChartAxis()
    {
        var start = new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);
        var cases = new[]
        {
            (Days: new[] { 0 }, Labels: new[] { 0 }),
            (Days: new[] { 0, 1, 2 }, Labels: new[] { 0, 1, 2 }),
            (Days: new[] { 0, 3, 7 }, Labels: new[] { 0, 2, 4, 5, 7 })
        };
        foreach (var scenario in cases)
        {
            var chart = new ReviewPerformanceChart
            {
                Width = 640,
                Height = 245,
                Series = new ReviewChartSeries("每日净盈亏", "", scenario.Days.Select(day =>
                    new ReviewChartPoint(start.AddDays(day), day + 1m, "")).ToArray(), IsBar: true, ReferenceValue: 0m)
            };
            chart.Measure(new Size(chart.Width, chart.Height));
            chart.Arrange(new Rect(0, 0, chart.Width, chart.Height));
            chart.UpdateLayout();
            Render(chart);
            var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var drawing = (DrawingGroup)typeof(ReviewPerformanceChart).GetField("_drawing", fields)!.GetValue(chart)!;
            var positions = (Point[])typeof(ReviewPerformanceChart).GetField("_positions", fields)!.GetValue(chart)!;
            var plot = (Rect)typeof(ReviewPerformanceChart).GetField("_plot", fields)!.GetValue(chart)!;
            var labels = GlyphDrawings(drawing).Where(item => item.GlyphRun.BaselineOrigin.Y > plot.Bottom + 9 &&
                item.GlyphRun.BaselineOrigin.Y < chart.ActualHeight - 25).OrderBy(item => item.GlyphRun.BaselineOrigin.X).ToArray();
            Assert.Equal(scenario.Labels.Length, labels.Length);
            for (var index = 0; index < labels.Length; index++)
            {
                var glyph = labels[index].GlyphRun;
                var expectedDate = start.AddDays(scenario.Labels[index]).ToString("MM-dd");
                Assert.Equal(expectedDate.Select(character => glyph.GlyphTypeface.CharacterToGlyphMap[character]), glyph.GlyphIndices);
                var expectedX = positions[0].X;
                if (scenario.Days[^1] > 0)
                    expectedX += (positions[^1].X - positions[0].X) * scenario.Labels[index] / scenario.Days[^1];
                Assert.Equal(Math.Clamp(expectedX - 20, plot.Left, plot.Right - 42), glyph.BaselineOrigin.X, 3);
            }
        }
    }

    private static IEnumerable<GlyphRunDrawing> GlyphDrawings(Drawing drawing) => drawing is GlyphRunDrawing glyph
        ? [glyph] : drawing is DrawingGroup group ? group.Children.SelectMany(GlyphDrawings) : [];

    private static RenderTargetBitmap Render(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(9, 13, 20)), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        bitmap.Render(drawing);
        return bitmap;
    }

    private static IEnumerable<GeometryDrawing> GeometryDrawings(Drawing drawing) => drawing is GeometryDrawing geometry
        ? [geometry] : drawing is DrawingGroup group ? group.Children.SelectMany(GeometryDrawings) : [];

    private static double ColorMeanY(RenderTargetBitmap bitmap, byte red, byte green, byte blue)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var count = 0;
        var sum = 0d;
        for (var index = 0; index < pixels.Length; index += 4)
            if (pixels[index] == blue && pixels[index + 1] == green && pixels[index + 2] == red)
            { count++; sum += index / 4 / bitmap.PixelWidth; }
        Assert.True(count > 5, "柱形应该绘制真实颜色的可见像素。");
        return sum / count;
    }

    private static void SavePreview(FrameworkElement element, string name)
    {
        if (Environment.GetEnvironmentVariable("TRADEPET_REVIEW_PREVIEW_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Render(element)));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }
}
