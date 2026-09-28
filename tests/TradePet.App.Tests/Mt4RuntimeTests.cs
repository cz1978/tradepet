using System.IO;
using System.Reflection;
using System.Text.Json;
using TradePet.Application.Review;
using TradePet.Application.Runtime;
using TradePet.App.Runtime;
using TradePet.App.ViewModels;
using TradePet.Core.Domain;
using TradePet.Core.Protocol;
using TradePet.Core.Review;
using TradePet.Infrastructure.Mt4;
using TradePet.Infrastructure.Mt5;
using TradePet.Infrastructure.Persistence;
using Xunit;

namespace TradePet.App.Tests;

public sealed class Mt4RuntimeTests
{
    [Fact]
    public async Task OrderHistory_ReachesPersistentReviewAndReportWithBrokerDatesAndIdempotentTotals()
    {
        var database = new AppDatabase(Path.Combine(Path.GetTempPath(), $"mt4-runtime-{Guid.NewGuid():N}.db"));
        await database.InitializeAsync();
        var viewModel = new MainViewModel();
        var dependencies = new TradePetRuntimeDependencies(database, database,
            DispatchProxy.Create<IReviewPackageWriter, UnusedDependency>(),
            DispatchProxy.Create<IReviewAttachmentStore, UnusedDependency>(),
            DispatchProxy.Create<IReviewBackupService, UnusedDependency>(),
            TimeProvider.System, new SystemAsyncScheduler(), new AccountSessionCoordinator(), new MaintenanceCoordinator());
        await using var runtime = new TradePetRuntime(viewModel, dependencies);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(TradePetRuntime).GetField("_activePlatform", flags)!.SetValue(runtime, TradingPlatform.Mt4);
        async Task Call(string name, object value) => await (Task)typeof(TradePetRuntime).GetMethod(name, flags)!.Invoke(runtime, [value])!;

        var now = DateTimeOffset.UtcNow;
        const int offset = 10800;
        var account = new AccountSnapshot(new("MT4:Broker", 42), "USD", 1012.5m, 1012.5m, 0, -1, now);
        await Call("HandleSnapshotAsync", new Mt5SnapshotBatch(account, [], [], offset, [], SupportsOrderHistory: true));
        Assert.False(viewModel.SupportsTradeHistory); // A live snapshot alone does not prove history is ready.

        var date = DateOnly.FromDateTime(now.AddSeconds(offset).DateTime).AddDays(-1);
        var brokerClose = new DateTimeOffset(date.ToDateTime(new TimeOnly(0, 5)), TimeSpan.FromSeconds(offset));
        var frame = new Mt4Frame("ea", 1, now, true, account.Scope.AccountKey,
            JsonSerializer.SerializeToElement(new { serverUtcOffsetSeconds = offset }));
        var history = JsonSerializer.Serialize(new
        {
            version = 2, platform = "mt4", sourceInstanceId = "ea", accountKey = account.Scope.AccountKey,
            terminalPath = @"C:\BrokerMT4", capturedAtUtc = now, serverUtcOffsetSeconds = offset, scanComplete = true,
            orders = new[] { new { ticket = 51, type = 0, symbol = "EURUSD", volume = .1m,
                openTime = brokerClose.AddMinutes(-10).ToUnixTimeSeconds() + offset,
                closeTime = brokerClose.ToUnixTimeSeconds() + offset,
                openPrice = 1.1m, closePrice = 1.1015m, profit = 15m, commission = -2m, swap = -.5m } },
        });
        var batch = Mt4HistoryMapper.Read(history, frame, @"C:\BrokerMT4", now);
        var envelope = ProtocolEnvelope.Create("test", 1, "deals", new { deals = batch.Deals, cashFlows = batch.CashFlows, isRecovery = true }, account.Scope.AccountKey);
        batch = Mt5PayloadMapper.MapDealBatch(envelope);
        await Call("HandleDealsAsync", batch);
        await Call("HandleDealsAsync", batch);
        Assert.True(viewModel.SupportsTradeHistory);
        Assert.Equal(offset, viewModel.ReviewWorkspace.ServerUtcOffsetSeconds);

        var data = await database.LoadWorkspaceAsync(account.Scope.AccountKey, date.AddDays(-1), date);
        var trade = Assert.Single(data.Trades);
        Assert.Equal(date.AddDays(-1), trade.OpenServerDate);
        Assert.Equal(date, trade.CloseServerDate);
        Assert.Equal(12.5m, trade.NetPnl);
        Assert.Equal(2, data.Deals.Count);
        var facts = new ReviewWorkspaceCalculator().BuildDailyFacts(data.AccountKey, date, date,
            data.Trades, data.Deals, data.Documents, data.Behaviors, new Dictionary<DateOnly, DailyState>(), offset)[date];
        var report = DailyReportAnalyzer.Analyze(DailyReportAnalyzer.NormalizeServerDates(data, offset), facts, false);
        Assert.Equal(12.5m, facts.RealizedCashPnl);
        Assert.Equal(1, facts.CompleteTradeCount);
        Assert.Contains("MT4", report.Markdown);
        Assert.Contains("00:05", report.Markdown);
        Assert.Equal("00:05:00", new TradePet.App.ViewModels.Review.TradeProcessRow(brokerClose.ToUniversalTime(), "平仓", "", "MT4") { ServerUtcOffsetSeconds = offset }.Time[6..]);

        viewModel.IsPlanRecording = true;
        var chart = new ChartObjectSnapshot("mt4-test", 1, "计划线", "EURUSD", "M5", ChartObjectKind.HorizontalLine,
            [new(null, 1.1m)], "计划", 0, now);
        await Call("HandleChartSnapshotAsync", new BridgeChartSnapshot("mt4-test", 1, [chart]));
        var plans = (Dictionary<string, PlanItem>)typeof(TradePetRuntime).GetField("_planItems", flags)!.GetValue(runtime)!;
        var firstPlan = Assert.Single(plans).Value;
        await Call("HandleChartSnapshotAsync", new BridgeChartSnapshot("mt4-test", 1, [chart with { CapturedAtUtc = now.AddSeconds(1) }]));
        Assert.Equal(firstPlan.UpdatedAtUtc, Assert.Single(plans).Value.UpdatedAtUtc);
        await Call("HandleChartSnapshotAsync", new BridgeChartSnapshot("mt4-test", 1,
            [chart with { Anchors = [new(null, 1.2m)], CapturedAtUtc = now.AddSeconds(2) }]));
        Assert.Equal(1.2m, Assert.Single(plans).Value.PriceLow);
        await Call("HandleChartSnapshotAsync", new BridgeChartSnapshot("mt4-test", 1, []));
        Assert.False(Assert.Single(plans).Value.IsActive);

        // Upgrade an old independent ticket projection to a broker-linked partial close.
        var originalEntry = batch.Deals.Single(d => d.EntryKind == DealEntryKind.In);
        var remaining = originalEntry with { Ticket = 104, OrderTicket = 52, PositionId = 52, Volume = .2m };
        await Call("HandleDealsAsync", batch with { Deals = [remaining], IsRecovery = true });
        var linked = batch with
        {
            Deals = [originalEntry with { Volume = .3m }, batch.Deals.Single(d => d.EntryKind == DealEntryKind.Out)],
            Mt4PositionAliases = new Dictionary<long, long> { [52] = 51 }, IsRecovery = true,
        };
        var wire = ProtocolEnvelope.Create("test", 2, "deals", new { deals = linked.Deals, isRecovery = true,
            mt4PositionAliases = linked.Mt4PositionAliases }, account.Scope.AccountKey);
        await Call("HandleDealsAsync", Mt5PayloadMapper.MapDealBatch(wire));
        await Call("HandleDealsAsync", linked);
        var partialData = await database.LoadWorkspaceAsync(account.Scope.AccountKey, date.AddDays(-1), date);
        var partial = Assert.Single(partialData.Trades);
        Assert.Equal(.3m, partial.MaximumVolume);
        Assert.Equal(.2m, partial.RemainingVolume);
        Assert.False(partial.IsComplete);
        Assert.Equal(12.5m, partial.NetPnl);
        Assert.Equal(2, partialData.Deals.Count);

        await Call("HandleSnapshotAsync", new Mt5SnapshotBatch(account with { Scope = new("MT4:Broker", 43) }, [], [], offset, [], true));
        Assert.False(viewModel.SupportsTradeHistory); // Account changes cannot carry over ready totals.
    }

    public class UnusedDependency : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException(targetMethod?.Name);
    }
}
