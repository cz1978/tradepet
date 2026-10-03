using TradePet.Application.Review;
using TradePet.Core.Domain;
using Xunit;

namespace TradePet.Application.Tests;

public sealed class DailyMarketContextAnalyzerTests
{
    private static readonly DateOnly Date = new(2026, 9, 25);
    private static readonly DateTimeOffset From = new(2026, 9, 24, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MarketContext_UsesServerDayAndExportsPriceEvidenceWithoutForeignOrInvalidBars()
    {
        var bars = new[]
        {
            Bar(From, 100, 103, 99, 102), Bar(From.AddMinutes(5), 102, 105, 98, 104),
            Bar(From.AddMinutes(-5), 999, 1000, 900, 950),
            Bar(From.AddMinutes(10), 888, 900, 800, 850) with { AccountKey = "other" },
            Bar(From.AddMinutes(15), 100, 99, 101, 100),
        };
        var data = Data() with { DailyMarketData = [History(bars)] };

        var result = DailyMarketContextAnalyzer.Analyze(data, Date, 7200);

        Assert.Contains(result.Section.Lines, line => line.Contains("M5 2 根"));
        Assert.Contains(result.Section.Lines, line => line.Contains("100 / 105 / 98 / 104"));
        Assert.Contains("2026-09-25 00:00", result.Markdown);
        Assert.Contains("入场所在 M5 O/H/L/C 100/103/99/102", result.Markdown);
        Assert.DoesNotContain("999", result.Markdown);
        Assert.DoesNotContain("888", result.Markdown);
        Assert.Contains("不等同于持仓货币 MAE/MFE", result.Markdown);
    }

    [Fact]
    public void MissingMarketContext_ExplainsMissingDataInsteadOfInferringFromTradePnl()
    {
        var data = Data() with
        {
            DailyMarketData = [History([]) with { Range = History([]).Range with { Coverage = MarketCoverageStatus.Failed, Error = "终端历史尚未加载" } }],
        };

        var result = DailyMarketContextAnalyzer.Analyze(data, Date, 7200);

        Assert.Contains(result.Section.Lines, line => line.Contains("缺少当日 M5 行情") && line.Contains("终端历史尚未加载"));
        Assert.Empty(result.Markdown);
        Assert.DoesNotContain(result.Section.Lines, line => line.Contains("上涨") || line.Contains("下跌"));
    }

    [Fact]
    public void PartialMarketContext_LabelsCoverageAndDoesNotFabricateMissingIntervals()
    {
        var history = History([Bar(From, 100, 103, 99, 102), Bar(From.AddHours(1), 102, 105, 98, 104)]);
        var data = Data() with { DailyMarketData = [history with { Range = history.Range with { Coverage = MarketCoverageStatus.Partial } }] };

        var result = DailyMarketContextAnalyzer.Analyze(data, Date, 7200);

        Assert.Contains(result.Section.Lines, line => line.Contains("覆盖不完整") && line.Contains("超过5分钟 1 处"));
        Assert.Contains("覆盖不足时不能视为全天 OHLC", result.Markdown);
        Assert.DoesNotContain("00:30", result.Markdown);
    }

    private static MarketBar Bar(DateTimeOffset at, decimal open, decimal high, decimal low, decimal close) =>
        new("terminal", "account", "TEST", "M5", at, open, high, low, close, 10, 2, 0);

    private static MarketHistoryResult History(IReadOnlyList<MarketBar> bars) =>
        new(new("request", "terminal", "account", "TEST", "M5", From, From.AddDays(1), From, From.AddMinutes(10),
            MarketDataPrecision.Bars, MarketCoverageStatus.Complete, "test", "", From.AddDays(1)), bars, []);

    private static ReviewWorkspaceData Data() => new("account", "USD",
        [new("account", 1, "TEST", TradeSide.Buy, From.AddMinutes(1), From.AddMinutes(6), Date, Date,
            101, 104, 1, 1, 0, 63.36m, true)], [], new Dictionary<long, TradeReviewMetadata>(),
        new Dictionary<long, TradeReviewDocument>(), new Dictionary<long, TradeExcursion>(),
        new Dictionary<DateOnly, DailyJournal>(), [], [], [], [], [], [], [], [], [],
        new("account", 1, 1, 1, "rule", "time", From));
}
