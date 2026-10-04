using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using TradePet.Application.Review;
using TradePet.Core.Domain;

namespace TradePet.App.ViewModels.Review;

public sealed partial class ReviewWorkspaceViewModel
{
    private IReadOnlyList<ReviewChartSeries> _performanceCharts = [];
    private ReviewChartSeries? _selectedPerformanceChart;
    private string _dashboardScope = "选择账户并同步历史交易后查看复盘。";
    private string _dashboardBreakdownDimension = "品种";
    private string _dashboardBreakdownSummary = "当前筛选没有完整交易。";
    private IReadOnlyDictionary<string, IReadOnlyList<ReviewDashboardBar>> _dashboardGroups =
        new Dictionary<string, IReadOnlyList<ReviewDashboardBar>>();
    private bool _hasDashboardSelection;
    private string _analysisDimension = "品种";
    public IReadOnlyList<string> AnalysisDimensions { get; } = ["品种", "策略", "形态", "方向", "星期", "入场时段", "自定义时段", "持仓时长", "标签"];
    public ObservableCollection<WorkspaceGroupRow> FilteredGroupPerformance { get; } = [];
    public string AnalysisDimension
    {
        get => _analysisDimension;
        set { if (SetProperty(ref _analysisDimension, value)) UpdateAnalysisGroups(); }
    }
    private void UpdateAnalysisGroups() => FilteredGroupPerformance.ReplaceWith(GroupPerformance.Where(item => item.Dimension == AnalysisDimension));

    public IReadOnlyList<ReviewChartSeries> PerformanceCharts
    {
        get => _performanceCharts;
        private set
        {
            if (SetProperty(ref _performanceCharts, value)) RaisePropertyChanged(nameof(AccountEquityChart));
        }
    }

    public ReviewChartSeries? AccountEquityChart => PerformanceCharts.FirstOrDefault(item => item.Name == "账户净值");

    public ReviewChartSeries? SelectedPerformanceChart
    {
        get => _selectedPerformanceChart;
        set => SetProperty(ref _selectedPerformanceChart, value);
    }

    public string DashboardScope { get => _dashboardScope; private set => SetProperty(ref _dashboardScope, value); }
    public string DashboardBreakdownSummary { get => _dashboardBreakdownSummary; private set => SetProperty(ref _dashboardBreakdownSummary, value); }
    public bool HasDashboardSelection { get => _hasDashboardSelection; private set => SetProperty(ref _hasDashboardSelection, value); }
    public bool HasAnalysisDrilldown => AnalysisDrilldown.Count > 0;
    public IReadOnlyList<string> DashboardBreakdownDimensions { get; } = ["品种", "策略", "形态", "方向", "星期", "入场时段", "持仓时长", "标签"];
    public string DashboardBreakdownDimension
    {
        get => _dashboardBreakdownDimension;
        set { if (SetProperty(ref _dashboardBreakdownDimension, value)) UpdateDashboardBreakdown(); }
    }

    public ObservableCollection<ReviewDashboardBar> DashboardBreakdown { get; } = [];
    public ObservableCollection<ReviewMetricRow> DashboardRiskMetrics { get; } = [];
    public ObservableCollection<ReviewDashboardBar> PnlDistribution { get; } = [];
    public ObservableCollection<ReviewDashboardInsight> DashboardInsights { get; } = [];
    public ObservableCollection<ReviewDashboardMonth> DashboardMonths { get; } = [];

    private void ApplyDashboard(ReviewWorkspaceSnapshot snapshot, ReviewWorkspaceData data)
    {
        HasDashboardSelection = false;
        AnalysisDrilldown.Clear();
        RaisePropertyChanged(nameof(HasAnalysisDrilldown));
        var performance = snapshot.Analytics.Performance;
        var hasTrades = performance.TradeCount > 0;
        var currency = data.Currency;
        DashboardScope = $"{snapshot.Filter.FromServerDate:yyyy-MM-dd} — {snapshot.Filter.ToServerDate:yyyy-MM-dd} · {currency} · {performance.TradeCount} 笔完整交易 · 服务器日";
        Metrics.Clear();
        DashboardRiskMetrics.Clear();
        Metrics.Add(new("净盈亏", hasTrades ? Signed(performance.NetPnl) : "—", $"{currency} · 扣除交易费用", FinancialPalette.For(performance.NetPnl)));
        Metrics.Add(new("交易胜率", performance.WinCount + performance.LossCount > 0 ? $"{performance.WinRate:0.##}%" : "—", $"{performance.WinCount} 盈 / {performance.LossCount} 亏 / {performance.BreakevenCount} 保本"));
        Metrics.Add(new("盈亏因子", performance.ProfitFactor?.ToString("0.##") ?? (performance.WinCount > 0 ? "∞" : "—"), "总盈利 ÷ 总亏损绝对值"));
        Metrics.Add(new("每笔期望值", hasTrades ? Signed(performance.Expectancy) : "—", "每笔平均净盈亏", FinancialPalette.For(performance.Expectancy)));
        DashboardRiskMetrics.Add(new("最大已实现回撤", hasTrades ? performance.MaximumDrawdown.ToString("0.##") : "—", $"{currency} · 累计利润从峰值回落"));
        DashboardRiskMetrics.Add(new("平均盈亏比", performance.AverageWinLossRatio?.ToString("0.##") ?? "—", "平均盈利 ÷ 平均亏损绝对值"));
        DashboardRiskMetrics.Add(new("平均盈利", performance.WinCount > 0 ? Signed(performance.AverageWin) : "—", $"{performance.WinCount} 笔盈利交易", FinancialPalette.Profit));
        DashboardRiskMetrics.Add(new("平均亏损", performance.LossCount > 0 ? Signed(performance.AverageLoss) : "—", $"{performance.LossCount} 笔亏损交易", FinancialPalette.Loss));
        DashboardRiskMetrics.Add(new("交易笔数", performance.TradeCount.ToString(), $"平均持仓 {FormatDuration(performance.AverageHoldingTime)}"));
        DashboardRiskMetrics.Add(new("盈利日占比", hasTrades ? $"{performance.DailyWinRate:0.##}%" : "—", "按完整交易最终平仓日，含保本日"));
        DashboardRiskMetrics.Add(new("最大连盈 / 连亏", hasTrades ? $"{performance.MaximumWinStreak} / {performance.MaximumLossStreak}" : "—", "按最终平仓先后顺序"));
        DashboardRiskMetrics.Add(new("平均实际 R", performance.AverageActualRiskMultiple?.ToString("0.##' R'") ?? "—", $"可靠初始风险覆盖 {snapshot.DataQuality.InitialRiskCoveragePercentage:0.##}%"));

        var selectedName = SelectedPerformanceChart?.Name;
        var curve = snapshot.RealizedCurve.OrderBy(point => point.AtUtc).ToArray();
        ICommand Open(string title, IEnumerable<TradeKey> keys) => new RelayCommand(() =>
        {
            ApplyAnalysisDrilldown(title, keys, data);
            HasDashboardSelection = true;
        });
        DateTimeOffset ServerDate(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue),
            TimeSpan.FromSeconds(snapshot.Filter.ServerUtcOffsetSeconds));
        DateTimeOffset ServerTime(DateTimeOffset at) => at.ToOffset(TimeSpan.FromSeconds(snapshot.Filter.ServerUtcOffsetSeconds));
        var realized = curve.Select(point => new ReviewChartPoint(ServerTime(point.AtUtc), point.Value,
            $"累计净盈亏 {Signed(point.Value)} {currency} · 峰值回撤 {point.Drawdown:0.##}",
            Open($"平仓节点 {ServerTime(point.AtUtc):yyyy-MM-dd HH:mm:ss}", point.Trades))).ToList();
        var drawdown = curve.Select(point => new ReviewChartPoint(ServerTime(point.AtUtc), -point.Drawdown,
            $"已实现回撤 {point.Drawdown:0.##} {currency} · 累计净盈亏 {Signed(point.Value)}",
            Open($"回撤节点 {ServerTime(point.AtUtc):yyyy-MM-dd HH:mm:ss}", point.Trades))).ToList();
        if (curve.Length > 0)
        {
            var baseline = ServerDate(snapshot.Filter.FromServerDate);
            realized.Insert(0, new ReviewChartPoint(baseline, 0m, "区间累计盈亏起点", IsBaseline: true));
            drawdown.Insert(0, new ReviewChartPoint(baseline, 0m, "区间回撤起点", IsBaseline: true));
        }
        var dailyGroups = curve.GroupBy(point => point.ServerDate).OrderBy(group => group.Key).ToArray();
        var daily = new ReviewChartPoint[dailyGroups.Length];
        // A day's P&L is the difference between successive cumulative endpoints.
        var previous = 0m;
        for (var index = 0; index < daily.Length; index++)
        {
            var dayPoints = dailyGroups[index].ToArray();
            var ending = dayPoints[^1].Value;
            var pnl = ending - previous;
            previous = ending;
            daily[index] = new ReviewChartPoint(ServerDate(dailyGroups[index].Key), pnl,
                $"当日完整交易净盈亏 {Signed(pnl)} {currency} · {dayPoints.Length} 笔",
                Open($"每日盈亏 {dailyGroups[index].Key:yyyy-MM-dd}", dayPoints.SelectMany(point => point.Trades)));
        }
        var cash = (snapshot.DailyCash ?? []).OrderBy(point => point.ServerDate).Select(point =>
            new ReviewChartPoint(ServerDate(point.ServerDate), point.CashPnl,
                $"成交现金盈亏 {Signed(point.CashPnl)} {currency} · {point.DealTickets.Count} 笔成交",
                Open($"成交现金盈亏 {point.ServerDate:yyyy-MM-dd}", point.Trades))).ToArray();
        var observed = new List<ReviewChartPoint>();
        var unitized = new List<ReviewChartPoint>();
        var equityPoints = (snapshot.EquityAnalysis?.Points ?? []).OrderBy(point => point.AtUtc).ToArray();
        var observedSegment = 0;
        for (var index = 0; index < equityPoints.Length; index++)
        {
            var point = equityPoints[index];
            if (index > 0 && point.AtUtc - equityPoints[index - 1].AtUtc > TimeSpan.FromMinutes(15)) observedSegment++;
            observed.Add(new ReviewChartPoint(ServerTime(point.AtUtc), point.Equity,
                $"实际净值 {point.Equity:0.##} {currency} · 观测回撤 {point.ObservedDrawdownAmount:0.##}", Segment: observedSegment));
            unitized.Add(new ReviewChartPoint(ServerTime(point.AtUtc), point.UnitizedValue,
                $"单位化净值 {point.UnitizedValue:0.####} · 第 {point.Segment} 段" +
                (point.StartsAfterUnverifiedCashFlow ? " · 资金流边界未核验，本段从 100 重置" : ""),
                Segment: point.Segment + observedSegment * (equityPoints.Length + 1)));
        }
        PerformanceCharts = [
            new("累计净盈亏", "全部筛选完整交易，含费用；平滑曲线经过真实平仓节点，起点为 0。点击节点查看对应交易。", realized, ReferenceValue: 0m),
            new("已实现回撤", "累计净盈亏相对前期峰值的回撤金额；包含起点 0，不代表账户净值回撤。", drawdown, ReferenceValue: 0m, Color: FinancialPalette.Loss),
            new("每日净盈亏", "完整交易按最终平仓服务器日归属；红盈绿亏，柱形从零轴向上或向下。", daily, IsBar: true, ReferenceValue: 0m),
            new("成交现金盈亏", "按成交发生服务器日汇总，包含部分平仓；与完整交易归属日不同。", cash, IsBar: true, ReferenceValue: 0m),
            new("账户净值", "账户级实际采样，包含浮盈亏和出入金；品种、策略筛选不改变账户净值。采样间隔超过 15 分钟时断开。", observed, EmptyMessage: "此期间没有净值采样。保持账户连接后会积累实际净值；历史成交仍可查看累计净盈亏。"),
            new("单位化净值", "账户级采样，每段从 100 开始；排除已核验出入金。未核验边界断开，分段之间不可比较收益率。", unitized, ReferenceValue: 100m,
                EmptyMessage: "此期间没有净值采样，无法计算排除资金流后的净值。")
        ];
        SelectedPerformanceChart = PerformanceCharts.FirstOrDefault(item => item.Name == selectedName) ?? PerformanceCharts[0];

        _dashboardGroups = new Dictionary<string, IReadOnlyList<ReviewDashboardBar>>
        {
            ["品种"] = Bars("品种", snapshot.Analytics.SymbolPerformance),
            ["策略"] = Bars("策略", snapshot.Analytics.StrategyPerformance),
            ["形态"] = Bars("形态", snapshot.Analytics.SetupPerformance),
            ["方向"] = Bars("方向", snapshot.Analytics.SidePerformance),
            ["星期"] = Bars("星期", snapshot.Analytics.WeekdayPerformance),
            ["入场时段"] = Bars("入场时段", snapshot.Analytics.HourPerformance),
            ["持仓时长"] = Bars("持仓时长", snapshot.Analytics.DurationPerformance),
            ["标签"] = Bars("标签", snapshot.Analytics.TagPerformance)
        };
        UpdateDashboardBreakdown();
        IReadOnlyList<ReviewDashboardBar> Bars(string dimension, IReadOnlyList<GroupMetricRow> groups)
        {
            var maximum = groups.Select(item => Math.Abs(item.NetPnl)).DefaultIfEmpty().Max();
            return groups.OrderByDescending(item => item.NetPnl).Select(item => new ReviewDashboardBar(item.Group,
                Signed(item.NetPnl), $"{item.TradeCount} 笔 · 胜率 {item.WinRate:0.##}% · 期望 {Signed(item.Expectancy)}" +
                (item.HasSmallSampleWarning ? " · 样本少于 30" : ""), maximum == 0m ? 0m : Math.Abs(item.NetPnl) / maximum,
                FinancialPalette.For(item.NetPnl), item.NetPnl < 0m, Open($"{dimension} / {item.Group}", item.Trades ?? []))).ToArray();
        }

        // Use all filtered trades, never the visible archive page, for distributions and insights.
        var trades = (snapshot.AllFilteredTrades ?? snapshot.Analytics.Trades)
            .Where(item => item.IsComplete).OrderBy(item => item.ClosedAtUtc).ToArray();
        PnlDistribution.Clear();
        var magnitude = trades.Select(item => Math.Abs(item.NetPnl)).DefaultIfEmpty().Max();
        if (trades.Length > 0)
        {
            var buckets = new List<(string Label, TradeRecord[] Trades, bool Negative)>();
            if (magnitude > 0.01m)
            {
                var step = magnitude / 3m;
                for (var index = 2; index >= 0; index--)
                {
                    var lower = step * index;
                    var upper = index == 2 ? magnitude : step * (index + 1);
                    buckets.Add(($"-{upper:0.##} ～ -{lower:0.##}", trades.Where(item => item.NetPnl < -0.01m &&
                        -item.NetPnl > lower && -item.NetPnl <= upper).ToArray(), true));
                }
                buckets.Add(("保本（±0.01）", trades.Where(item => Math.Abs(item.NetPnl) <= 0.01m).ToArray(), false));
                for (var index = 0; index < 3; index++)
                {
                    var lower = step * index;
                    var upper = index == 2 ? magnitude : step * (index + 1);
                    buckets.Add(($"{lower:0.##} ～ {upper:0.##}", trades.Where(item => item.NetPnl > 0.01m &&
                        item.NetPnl > lower && item.NetPnl <= upper).ToArray(), false));
                }
            }
            else buckets.Add(("保本（±0.01）", trades, false));
            var maximum = buckets.Max(item => item.Trades.Length);
            PnlDistribution.ReplaceWith(buckets.Select(item => new ReviewDashboardBar(item.Label,
                $"{item.Trades.Length} 笔", $"{item.Trades.Length * 100m / trades.Length:0.#}% · 合计 {Signed(item.Trades.Sum(trade => trade.NetPnl))} {currency}",
                maximum == 0 ? 0m : (decimal)item.Trades.Length / maximum,
                item.Label.StartsWith("保本", StringComparison.Ordinal) ? FinancialPalette.Neutral : item.Negative ? FinancialPalette.Loss : FinancialPalette.Profit,
                false, Open($"盈亏分布 {item.Label}", item.Trades.Select(item => new TradeKey(item.AccountKey, item.PositionId))))));
        }
        DashboardMonths.ReplaceWith(trades.Where(item => item.CloseServerDate is not null)
            .GroupBy(item => new DateOnly(item.CloseServerDate!.Value.Year, item.CloseServerDate.Value.Month, 1))
            .OrderBy(group => group.Key).Select(group =>
            {
                var pnl = group.Sum(item => item.NetPnl);
                return new ReviewDashboardMonth(group.Key.ToString("yyyy-MM"), Signed(pnl), $"{group.Count()} 笔完整交易",
                    FinancialPalette.For(pnl), FinancialPalette.BackgroundFor(pnl),
                    Open($"月度盈亏 {group.Key:yyyy-MM}", group.Select(item => new TradeKey(item.AccountKey, item.PositionId))));
            }));

        DashboardInsights.Clear();
        if (trades.Length == 0)
        {
            DashboardInsights.Add(new("开始积累样本", "当前范围没有完整交易。同步历史或扩大日期范围后，曲线、分布和归因会自动出现。"));
            return;
        }
        if (trades.Length < 30) DashboardInsights.Add(new("样本仍少", $"当前 {trades.Length} 笔完整交易；分组结果可作为复盘线索，还不足以确认稳定优势。"));
        var worst = trades.OrderBy(item => item.NetPnl).First();
        var best = trades.OrderByDescending(item => item.NetPnl).First();
        DashboardInsights.Add(new("最大盈利 / 亏损", $"最高 {Signed(best.NetPnl)}，最低 {Signed(worst.NetPnl)} {currency}。打开原始交易核对进出场和过程。",
            Open("最大盈利 / 亏损交易", new[] { best, worst }.Select(item => new TradeKey(item.AccountKey, item.PositionId)).Distinct().ToArray())));
        if (curve.Length > 0)
        {
            var deepest = curve.MaxBy(item => item.Drawdown)!;
            var ending = curve[^1];
            var peakIndex = Array.FindLastIndex(curve, Array.IndexOf(curve, deepest), item => item.Drawdown == 0m);
            var recovery = curve.Skip(Array.IndexOf(curve, deepest) + 1).FirstOrDefault(item => item.Drawdown == 0m);
            DashboardInsights.Add(new("回撤与修复", $"最大回撤 {deepest.Drawdown:0.##}，发生于 {deepest.ServerDate:yyyy-MM-dd}；" +
                (recovery is null && deepest.Drawdown > 0m ? "区间内尚未修复。" : deepest.Drawdown > 0m ? $"{recovery!.ServerDate:yyyy-MM-dd} 恢复前峰值。" : "区间内无已实现回撤。") +
                $" 当前距峰值 {ending.Drawdown:0.##} {currency}。",
                Open("最大回撤期间交易", curve.Skip(peakIndex + 1).Take(Array.IndexOf(curve, deepest) - peakIndex).SelectMany(item => item.Trades))));
        }
        var weakest = snapshot.Analytics.SymbolPerformance.Where(item => item.NetPnl < -0.01m).MinBy(item => item.NetPnl);
        if (weakest is not null) DashboardInsights.Add(new("优先核对亏损来源", $"{weakest.Group} 累计 {Signed(weakest.NetPnl)} {currency}，共 {weakest.TradeCount} 笔；核对策略与执行记录。",
            Open($"亏损来源 / {weakest.Group}", weakest.Trades ?? [])));
        var pendingKeys = snapshot.DataQuality.Issues?.Where(item => item.Code == "review-pending")
            .SelectMany(item => item.Trades).ToHashSet();
        var pending = trades.Where(item => pendingKeys is not null
            ? pendingKeys.Contains(new TradeKey(item.AccountKey, item.PositionId))
            : (snapshot.Documents.GetValueOrDefault(item.PositionId) ?? data.Documents.GetValueOrDefault(item.PositionId))?.Status != ReviewCompletionStatus.Reviewed).ToArray();
        if (pending.Length > 0) DashboardInsights.Add(new("待完成复盘", $"{pending.Length} / {trades.Length} 笔还未完成或需要重审；先查看亏损交易，再补执行评价。",
            Open("待完成复盘", pending.Select(item => new TradeKey(item.AccountKey, item.PositionId)))));
    }

    private void UpdateDashboardBreakdown()
    {
        var rows = _dashboardGroups.GetValueOrDefault(DashboardBreakdownDimension) ?? [];
        DashboardBreakdown.ReplaceWith(rows);
        DashboardBreakdownSummary = rows.Count == 0 ? "当前筛选没有此类分组数据。" :
            $"{rows.Count} 组 · 按净盈亏排序 · 点击查看交易" + (DashboardBreakdownDimension == "标签" ? "；多标签交易会出现在多组，合计不可相加。" : "");
    }

    private void ClearDashboard()
    {
        PerformanceCharts = [];
        SelectedPerformanceChart = null;
        _dashboardGroups = new Dictionary<string, IReadOnlyList<ReviewDashboardBar>>();
        DashboardBreakdown.Clear();
        PnlDistribution.Clear();
        DashboardRiskMetrics.Clear();
        DashboardInsights.Clear();
        DashboardMonths.Clear();
        DashboardScope = "选择账户并同步历史交易后查看复盘。";
        DashboardBreakdownSummary = "当前筛选没有完整交易。";
        HasDashboardSelection = false;
        ClearCalendarDashboard();
        FilteredGroupPerformance.Clear();
    }
}

public sealed record ReviewChartPoint(DateTimeOffset At, decimal Value, string Detail,
    ICommand? OpenCommand = null, int Segment = 0, bool IsBaseline = false);

public sealed record ReviewChartSeries(string Name, string Description, IReadOnlyList<ReviewChartPoint> Points,
    bool IsBar = false, decimal? ReferenceValue = null, string Color = "#8296FF",
    string EmptyMessage = "当前筛选没有完整交易。同步历史或扩大日期范围后查看曲线。")
{
    public string SampleSummary => Points.Count == 0 ? "暂无数据" : $"{Points.Count(item => !item.IsBaseline):N0} 个数据点";
}

public sealed record ReviewDashboardBar(string Label, string Value, string Detail, decimal Proportion,
    string Color, bool IsNegative, ICommand OpenCommand)
{
    public GridLength FilledWidth => new((double)Proportion, GridUnitType.Star);
    public GridLength EmptyWidth => new((double)(1m - Proportion), GridUnitType.Star);
}

public sealed record ReviewDashboardInsight(string Title, string Detail, ICommand? OpenCommand = null)
{
    public bool CanOpen => OpenCommand is not null;
}

public sealed record ReviewDashboardMonth(string Month, string Value, string Detail, string Color, string Background, ICommand OpenCommand);
