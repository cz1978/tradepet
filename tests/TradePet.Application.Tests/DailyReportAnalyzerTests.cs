using TradePet.Application.Review;
using TradePet.Core.Domain;
using TradePet.Core.Review;
using Xunit;

namespace TradePet.Application.Tests;

public sealed class DailyReportAnalyzerTests
{
    [Fact]
    public void Report_ExportsActualProtectionChangesAndMarketBarsForReview()
    {
        var trade = Trade(1, 5);
        var initial = new PositionPnlSample(new TradeKey(Account, 1), trade.OpenedAtUtc.AddSeconds(1),
            0, -2, -2, 1, 95, 110, 1_000, "position-pnl-v1");
        var later = initial with { CapturedAtUtc = trade.ClosedAtUtc!.Value.AddSeconds(-1), NetPnl = 20, StopLoss = null };
        var dayStart = new DateTimeOffset(Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var history = new MarketHistoryResult(new("request", "terminal", Account, "TEST", "M5", dayStart,
            dayStart.AddDays(1), At, At.AddMinutes(5), MarketDataPrecision.Bars, MarketCoverageStatus.Partial,
            "test", "", At), [new("terminal", Account, "TEST", "M5", At, 100, 110, 95, 105, 10, 2, 0)], []);
        var data = Data(trade) with
        {
            PositionSamples = new Dictionary<long, IReadOnlyList<PositionPnlSample>> { [1] = [initial, later] },
            DailyMarketData = [history],
        };

        var report = Analyze(data);

        Assert.Contains("SL 95 / TP 110", report.Trades[0].Protection);
        Assert.Contains("SL/TP 变更 1 次", report.Markdown);
        Assert.Contains("实际持仓记录", report.Markdown);
        Assert.Contains("20 USD", report.Markdown);
        Assert.Contains("当日 M5 原始行情", report.Markdown);
        Assert.Contains("局部采样不能代表全程极值", report.Markdown);
        Assert.DoesNotContain("计划执行", report.Markdown);
    }

    private const string Account = "Broker|1";
    private static readonly DateOnly Date = new(2026, 9, 25);
    private static readonly DateTimeOffset At = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Results_SeparateDailyCashFromLifetimePnlAndExcludeOtherAccountsAndDates()
    {
        var trade = Trade(1, 100) with { OpenedAtUtc = At.AddDays(-1), OpenServerDate = Date.AddDays(-1) };
        var data = Data(trade, Trade(2, -40), Trade(3, 500) with { AccountKey = "other" },
            Trade(4, 999) with { OpenedAtUtc = At.AddDays(-1).AddMinutes(-10), ClosedAtUtc = At.AddDays(-1), CloseServerDate = Date.AddDays(-1) },
            Trade(5, 888) with { CloseServerDate = Date.AddDays(1), ClosedAtUtc = At.AddDays(1) },
            Trade(6, 777) with { IsComplete = false, ClosedAtUtc = null, CloseServerDate = null }) with
        {
            Deals = [Deal(11, 1, At.AddDays(-1), 0, -2), Deal(12, 1, At, 105, -3), Deal(13, 2, At, -40, 0)],
        };

        var report = Analyze(data);

        Assert.Equal(new long[] { 1, 2 }, report.Trades.Select(t => t.PositionId));
        Assert.Contains("现金净盈亏 62 USD", report.Markdown);
        Assert.Contains("全生命周期净盈亏 60 USD", report.Markdown);
        Assert.Contains("平均每笔 30 USD", report.Markdown);
        Assert.Contains("盈利因子（盈利净额 / 亏损净额绝对值）：2.5", report.Markdown);
        Assert.Equal("-5 USD", report.Trades[0].Fees);
        Assert.Equal("24小时 0分 0秒", report.Trades[0].Holding);
        Assert.Contains("跨日/未结交易记录 2 笔", report.Markdown);
        Assert.DoesNotContain("500 USD", report.Markdown);
    }

    [Fact]
    public void Risk_UsesOnlyReliableExcursionsAndRecomputesRFromInitialRisk()
    {
        var data = Data(Trade(1, -10), Trade(2, 20), Trade(3, 30)) with
        {
            Excursions = new Dictionary<long, TradeExcursion>
            {
                [1] = Excursion(1) with { ActualRiskMultiple = 999 },
                [2] = Excursion(2) with { MaximumGapMilliseconds = 6000, StartedAtOpen = false },
                [3] = Excursion(3) with { AccountKey = "another-account" },
            },
        };

        var report = Analyze(data);

        Assert.Equal("-1 R", report.Trades[0].ActualR);
        Assert.Equal("-15 USD", report.Trades[0].Mae);
        Assert.Equal("50 USD", report.Trades[0].Mfe);
        Assert.Equal("60 USD", report.Trades[0].Giveback);
        Assert.Equal("—", report.Trades[1].ActualR);
        Assert.Contains("数据不足", report.Trades[1].Mfe);
        Assert.Equal("无采样", report.Trades[2].Coverage);
        Assert.Contains("可靠初始风险覆盖 1/3", report.Markdown);
        Assert.DoesNotContain("999 R", report.Markdown);
    }

    [Fact]
    public void Equity_SeparatesWithdrawalFromCashFlowAdjustedDrawdown()
    {
        var data = Data() with
        {
            EquitySamples =
            [
                new(Account, Date, At, 1000, 1000, 0, false),
                new(Account, Date, At.AddMinutes(1), 1100, 1100, 0, false),
                new(Account, Date, At.AddMinutes(3), 600, 600, 0, false),
            ],
            CashFlows = [new(Account, 1, "withdrawal", -500, At.AddMinutes(2))],
        };

        var report = Analyze(data);

        Assert.Contains("原始净值最大回撤 500 USD", report.Markdown);
        Assert.Contains("经资金流校验的净值回撤比例：0%", report.Markdown);
        Assert.Contains("不代表全天精确最大回撤", report.Markdown);
    }

    [Fact]
    public void EmptyOrSingleSample_DoesNotClaimZeroRiskOrInfiniteProfitFactor()
    {
        var empty = Analyze(Data() with { EquitySamples = [new(Account, Date, At, 1000, 1000, 0, false)] });
        var winOnly = Analyze(Data(Trade(1, 10)));

        Assert.Empty(empty.Trades);
        Assert.Contains("净值采样不足 2 个", empty.Markdown);
        Assert.Contains("不代表全天没有风险", empty.Markdown);
        Assert.Contains("没有完整平仓样本", empty.Markdown);
        Assert.Contains("无亏损样本", winOnly.Markdown);
        Assert.Contains("未记录，不能仅凭盈亏判定平仓原因", winOnly.Markdown);
        Assert.DoesNotContain("Infinity", winOnly.Markdown);
    }

    [Fact]
    public void Discipline_DeduplicatesLinkedPnlAndSeparatesUnsupportedEvidence()
    {
        var alert = new BehaviorOccurrence("b1", Account, Date, BehaviorRuleKind.CooldownViolation,
            "v1", 1, null, 0, BehaviorRiskLevel.Attention, ReviewEvidenceSource.LiveObservation,
            At, At, "test", "", true, "", null,
            [new(new(Account, 1), BehaviorTradeRole.Trigger), new(new("other", 1), BehaviorTradeRole.Trigger)]);
        var data = Data(Trade(1, -10)) with
        {
            Behaviors = [alert, alert with { Id = "b2" }, alert with
            {
                Id = "weak", Rule = BehaviorRuleKind.RevengeScore, MissingData = "缺少基线",
            }],
            Assessments = [new(new("other", 1), "book", "foreign-rule", RuleAssessmentStatus.Failed,
                ReviewEvidenceSource.LiveObservation, "foreign-evidence", "", 1, At)],
        };

        var report = Analyze(data);
        var discipline = report.Sections.Single(s => s.Title == "执行纪律与关联结果");
        var actions = report.Sections.Single(s => s.Title == "下一交易日行动清单");

        Assert.Contains(discipline.Lines, l => l.Contains("2 条提醒") && l.Contains("关联净盈亏 -10 USD"));
        Assert.Contains(discipline.Lines, l => l.Contains("报复性交易风险") && l.Contains("证据不足 1 条"));
        Assert.Contains(actions.Lines, l => l.Contains("冷静期违规"));
        Assert.DoesNotContain(actions.Lines, l => l.Contains("报复性交易风险"));
        Assert.DoesNotContain("foreign-rule", report.Markdown);
    }

    [Fact]
    public void Review_PreservesUserActionsAndMarksHistoricalGaps()
    {
        var doc = new TradeReviewDocument(new(Account, 1), ReviewCompletionStatus.Reviewed,
            "入场理由", "我的退出理由", "", "", "我的下一步|先核对\n再记录", "总结", "", "",
            1, "source", "rule", "source", "rule", At, At);
        var data = Data(Trade(1, 10)) with
        {
            Documents = new Dictionary<long, TradeReviewDocument> { [1] = doc },
            DataGapDates = [Date],
            Metadata = new Dictionary<long, TradeReviewMetadata>
            {
                [1] = new(Account, 1, null, PlanComplianceStatus.ManualOutside, "我的策略", "", [], true, At),
            },
        };

        var report = Analyze(data);

        Assert.Equal(doc.ExitReason, report.Trades[0].ExitReason);
        Assert.Equal(doc.NextAction, report.Trades[0].NextAction);
        Assert.Contains("我的下一步\\|先核对<br>再记录", report.Markdown);
        Assert.Contains("历史数据缺口", report.Markdown);
        Assert.DoesNotContain("计划外交易", report.Markdown);
        Assert.DoesNotContain("未分类", report.Markdown);
        Assert.Contains("SL/TP：缺少持仓采样", report.Markdown);
        Assert.DoesNotContain("1 笔待复盘", report.Markdown);
    }

    [Fact]
    public void ServerDate_UsesOffsetForFeesAndOpeningHour()
    {
        var trade = Trade(1, 10) with { OpenedAtUtc = At.AddHours(-12), ClosedAtUtc = At.AddHours(-11) };
        var data = Data(trade) with { Deals = [Deal(1, 1, trade.ClosedAtUtc!.Value, 12, -2)] };

        var report = Analyze(data, 3 * 3600);

        Assert.Contains("当日已记录佣金 -2 USD", report.Markdown);
        Assert.Contains("开仓 2026-09-25 01:00:00", report.Markdown);
        Assert.Contains("按开仓小时） · 01:00", report.Markdown);
    }

    [Theory]
    [InlineData(10800)]
    [InlineData(-18000)]
    public void ServerDate_CorrectsStaleDatesAcrossMidnightForEveryReportSection(int offset)
    {
        var start = new DateTimeOffset(Date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromSeconds(offset)).ToUniversalTime();
        var today = Trade(1, 10) with
        {
            OpenedAtUtc = start.AddMinutes(1), ClosedAtUtc = start.AddMinutes(10),
            OpenServerDate = Date.AddDays(-1), CloseServerDate = Date.AddDays(-1),
        };
        var tomorrow = Trade(2, 73.48m) with
        {
            OpenedAtUtc = start.AddHours(23), ClosedAtUtc = start.AddDays(1).AddMinutes(37),
        };
        var yesterday = Trade(3, -20) with
        {
            OpenedAtUtc = start.AddHours(-1), ClosedAtUtc = start.AddMinutes(-1),
        };
        var alert = new BehaviorOccurrence("today", Account, Date.AddDays(-1), BehaviorRuleKind.CooldownViolation,
            "v1", 1, null, 0, BehaviorRiskLevel.Critical, ReviewEvidenceSource.LiveObservation,
            start.AddMinutes(2), start.AddMinutes(2), "test", "", true, "", null, []);
        var data = Data(today, tomorrow, yesterday) with
        {
            Deals = [Deal(1, 1, today.ClosedAtUtc!.Value, 10, 0),
                Deal(2, 2, tomorrow.ClosedAtUtc!.Value, 73.48m, 0),
                Deal(3, 3, yesterday.ClosedAtUtc!.Value, -20, 0)],
            Behaviors = [alert,
                alert with { Id = "normal", Level = BehaviorRiskLevel.Normal },
                alert with { Id = "yesterday", ServerDate = Date, EventAtUtc = start.AddMinutes(-1) },
                alert with { Id = "tomorrow", ServerDate = Date, EventAtUtc = start.AddDays(1) }],
            EquitySamples = [new(Account, Date.AddDays(-1), start.AddMinutes(1), 100, 100, 0, false),
                new(Account, Date.AddDays(-1), start.AddMinutes(2), 110, 110, 0, false),
                new(Account, Date, start.AddDays(1), 200, 200, 0, false)],
        };

        var normalized = DailyReportAnalyzer.NormalizeServerDates(data, offset);
        var dailyFacts = new ReviewWorkspaceCalculator().BuildDailyFacts(Account, Date, Date.AddDays(1),
            normalized.Trades, normalized.Deals, normalized.Documents, normalized.Behaviors,
            new Dictionary<DateOnly, DailyState>(), offset);
        var facts = dailyFacts[Date];
        var report = DailyReportAnalyzer.Analyze(normalized, facts, false);

        Assert.Equal(2, facts.OpeningTradeCount);
        Assert.Equal(1, facts.CompleteTradeCount);
        Assert.Equal(10m, facts.RealizedCashPnl);
        Assert.Equal(10m, facts.CompleteTradeNetPnl);
        Assert.Equal(1, facts.CooldownViolationCount);
        Assert.Equal(73.48m, dailyFacts[Date.AddDays(1)].CompleteTradeNetPnl);
        Assert.Equal(73.48m, dailyFacts[Date.AddDays(1)].RealizedCashPnl);
        Assert.Equal(1L, Assert.Single(report.Trades).PositionId);
        Assert.Contains("最高净盈亏：TEST #1 10 USD", report.Markdown);
        Assert.Contains("净值采样 2 个", report.Markdown);
        Assert.Contains("冷静期违规：1 条提醒", report.Markdown);
        Assert.Contains("跨日/未结交易记录 1 笔", report.Markdown);
        Assert.Equal(Date, tomorrow.CloseServerDate); // Source records remain untouched.
        Assert.Equal(Date.AddDays(1), normalized.Trades[1].CloseServerDate);
    }

    private static DailyReportAnalysis Analyze(ReviewWorkspaceData data, int offset = 0)
    {
        data = DailyReportAnalyzer.NormalizeServerDates(data, offset);
        var facts = new ReviewWorkspaceCalculator().BuildDailyFacts(Account, Date, Date,
            data.Trades, data.Deals, data.Documents, data.Behaviors,
            data.DailyStates ?? new Dictionary<DateOnly, DailyState>(), offset)[Date];
        return DailyReportAnalyzer.Analyze(data, facts, true);
    }

    private static TradeRecord Trade(long id, decimal pnl) => new(Account, id, "TEST", TradeSide.Buy,
        At.AddMinutes(-10), At, Date, Date, 100, 101, 1, 1, 0, pnl, true);

    private static DealRecord Deal(long ticket, long position, DateTimeOffset at, decimal pnl, decimal fee) =>
        new(ticket, ticket, position, "TEST", TradeSide.Buy, DealEntryKind.Out, 1, 101, pnl, fee, 0, 0, at);

    private static TradeExcursion Excursion(long id) => new(Account, id, -15, 50, 10, null, null,
        At.AddMinutes(-10), At, 600000, 600000, true, true, 1000, "position-pnl-v1");

    private static ReviewWorkspaceData Data(params TradeRecord[] trades) => new(Account, "USD", trades, [],
        new Dictionary<long, TradeReviewMetadata>(), new Dictionary<long, TradeReviewDocument>(),
        new Dictionary<long, TradeExcursion>(), new Dictionary<DateOnly, DailyJournal>(),
        [], [], [], [], [], [], [], [], [], new(Account, 1, 1, 1, "rule", "time", At));
}
