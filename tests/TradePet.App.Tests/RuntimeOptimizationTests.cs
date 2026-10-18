using System.Collections.Specialized;
using System.Reflection;
using System.Runtime.CompilerServices;
using TradePet.App.Runtime;
using TradePet.App.ViewModels;
using TradePet.Application.Runtime;
using TradePet.Core.Domain;
using TradePet.Core.Session;
using TradePet.Core.Trading;
using Xunit;

namespace TradePet.App.Tests;

public sealed class RuntimeOptimizationTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedRuntimeOperation_CompletesWhileMaintenanceWaits(bool asynchronousAdmission)
    {
        var runtime = EmptyRuntime();
        using var maintenance = new MaintenanceCoordinator();
        Set(runtime, "_maintenance", maintenance);
        var field = typeof(TradePetRuntime).GetField("_runtimeOperation", Flags)!;
        field.SetValue(runtime, Activator.CreateInstance(field.FieldType));
        var enter = typeof(TradePetRuntime).GetMethod("EnterRuntimeOperationAsync", Flags)!
            .CreateDelegate<Func<MaintenanceOperationKind, CancellationToken, ValueTask<IAsyncDisposable>>>(runtime);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var initialMaintenance = asynchronousAdmission ? await maintenance.EnterMaintenanceAsync(timeout.Token) : null;
        var pending = enter(MaintenanceOperationKind.Write, timeout.Token);
        if (initialMaintenance is not null) await initialMaintenance.DisposeAsync();
        var outer = await pending;
        try
        {
            var restore = maintenance.EnterMaintenanceAsync(timeout.Token).AsTask();
            Assert.True(maintenance.IsMaintenancePending);
            await using (await enter(MaintenanceOperationKind.Read, timeout.Token))
                Assert.Equal(1, maintenance.ActiveOperationCount);
            Assert.False(restore.IsCompleted);
            await outer.DisposeAsync();
            await using var maintenanceLease = await restore;
            Assert.Equal(0, maintenance.ActiveOperationCount);
        }
        finally { await outer.DisposeAsync(); }
    }

    [Fact]
    public void DailyCache_UsesCashPnlAndInvalidatesCorrectionsDateAndOffset()
    {
        var runtime = EmptyRuntime();
        var clock = new ServerClock();
        var date = new DateOnly(2026, 10, 18);
        clock.ApplyBridgeHeartbeat(date, 0);
        Set(runtime, "_serverClock", clock);
        var worker = new WorkerSession();
        worker.MarkDealsReceived();
        Set(runtime, "_workerSession", worker);
        Set(runtime, "_dailyHighWaterCalculator", new DailyHighWaterCalculator());
        var at = new DateTimeOffset(2026, 10, 17, 1, 0, 0, TimeSpan.Zero);
        var deals = new Dictionary<long, DealRecord>
        {
            [1] = new(1, 1, 10, "TEST", TradeSide.Buy, DealEntryKind.In, 2m, 100m, 0m, 0m, 0m, 0m, at),
            [2] = new(2, 2, 10, "TEST", TradeSide.Sell, DealEntryKind.Out, 1m, 200m, 100m, 0m, 0m, 0m, at.AddHours(1)),
            [3] = new(3, 3, 10, "TEST", TradeSide.Sell, DealEntryKind.Out, 1m, 90m, -10m, 0m, 0m, 0m, at.AddDays(1)),
        };
        Set(runtime, "_deals", deals);
        Set(runtime, "_trades", new TradeProjector().Project("Broker|1", deals.Values,
            time => DateOnly.FromDateTime(time.UtcDateTime)).ToDictionary(trade => trade.PositionId));
        Set(runtime, "_positions", new Dictionary<long, PositionSnapshot>());
        Set(runtime, "_equitySamples", new List<EquitySample>());
        Set(runtime, "_cashFlows", new Dictionary<long, AccountCashFlow>());
        Assert.Equal(-10m, Call<decimal>(runtime, "CalculateDailyRealizedPnl"));
        Assert.Equal(0m, Call<decimal>(runtime, "CalculateObservedHighWater"));
        var cached = Call<DealRecord[]>(runtime, "AnalyticsDailyDeals");
        Assert.Same(cached, Call<DealRecord[]>(runtime, "AnalyticsDailyDeals"));
        Assert.Single(Call<TradeRecord[]>(runtime, "AnalyticsDailyTrades"));
        deals[3] = deals[3] with { Profit = 20m };
        Call<object?>(runtime, "InvalidateAnalytics");
        Assert.Equal(20m, Call<decimal>(runtime, "CalculateDailyRealizedPnl"));
        Assert.Equal(20m, Call<decimal>(runtime, "CalculateObservedHighWater"));
        clock.ApplyBridgeHeartbeat(date, -2 * 3600);
        Assert.Equal(0m, Call<decimal>(runtime, "CalculateDailyRealizedPnl"));
        clock.ApplyBridgeHeartbeat(date.AddDays(-1), 0);
        Assert.Equal(100m, Call<decimal>(runtime, "CalculateDailyRealizedPnl"));
    }

    [Fact]
    public void DailyTradeCache_PreservesOvernightClosuresAndNewOpenPositions()
    {
        var runtime = EmptyRuntime();
        var date = new DateOnly(2026, 10, 18);
        var clock = new ServerClock();
        clock.ApplyBridgeHeartbeat(date, 0);
        Set(runtime, "_serverClock", clock);
        var at = new DateTimeOffset(2026, 10, 18, 1, 0, 0, TimeSpan.Zero);
        TradeRecord overnight = new("Broker|1", 1, "TEST", TradeSide.Buy, at.AddDays(-1), at,
            date.AddDays(-1), date, 100m, 99m, 1m, 1m, 0m, -1m, true);
        var opened = overnight with { PositionId = 2, OpenedAtUtc = at, OpenServerDate = date,
            ClosedAtUtc = null, CloseServerDate = null, IsComplete = false, RemainingVolume = 1m };
        var historical = overnight with { PositionId = 3, ClosedAtUtc = at.AddDays(-1), CloseServerDate = date.AddDays(-1) };
        var trades = new[] { overnight, opened, historical };
        Set(runtime, "_trades", trades.ToDictionary(trade => trade.PositionId));
        var selected = Call<TradeRecord[]>(runtime, "AnalyticsDailyTrades");
        Assert.Equal(new long[] { 1, 2 }, selected.Select(trade => trade.PositionId));
        var calculator = new DailyStateCalculator();
        var full = calculator.Calculate("Broker|1", date, trades, [], DailyPlanSettings.BalancedDefault, null);
        var cached = calculator.Calculate("Broker|1", date, selected, [], DailyPlanSettings.BalancedDefault, null);
        Assert.Equal(full.State, cached.State);
        Assert.Equal(full.NewFacts, cached.NewFacts);
    }

    [Fact]
    public async Task PositionUpdates_ReuseRowsAndOnlyChangeAffectedValues()
    {
        var runtime = EmptyRuntime();
        var viewModel = new MainViewModel();
        Set(runtime, "_viewModel", viewModel);
        Set(runtime, "_serverClock", new ServerClock());
        var at = DateTimeOffset.UtcNow;
        var first = new PositionSnapshot(1, 1, "TEST", TradeSide.Buy, 1m, 100m, 101m, 1m, 90m, 110m, at, at);
        var second = first with { Ticket = 2, PositionId = 2, OpenedAtUtc = at.AddSeconds(1) };
        Task Update(params PositionSnapshot[] positions) => Call<Task>(runtime, "UpdatePositionUiAsync", [positions]);
        await Update(first, second);
        var firstRow = viewModel.Positions[0];
        var secondRow = viewModel.Positions[1];
        var resets = 0;
        viewModel.Positions.CollectionChanged += (_, args) => { if (args.Action == NotifyCollectionChangedAction.Reset) resets++; };
        var changed = new List<string?>();
        firstRow.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        await Update(first with { Profit = -2m }, second);
        Assert.Same(firstRow, viewModel.Positions[0]);
        Assert.Same(secondRow, viewModel.Positions[1]);
        Assert.Equal("-2", firstRow.Profit);
        Assert.Contains(nameof(PositionRowViewModel.Profit), changed);
        Assert.DoesNotContain(nameof(PositionRowViewModel.Symbol), changed);
        await Update(second);
        Assert.Same(secondRow, Assert.Single(viewModel.Positions));
        Assert.Equal(0, resets);
    }

    private static TradePetRuntime EmptyRuntime() => (TradePetRuntime)RuntimeHelpers.GetUninitializedObject(typeof(TradePetRuntime));
    private static void Set(TradePetRuntime runtime, string name, object value) =>
        typeof(TradePetRuntime).GetField(name, Flags)!.SetValue(runtime, value);
    private static T Call<T>(TradePetRuntime runtime, string name, object[]? args = null) =>
        (T)typeof(TradePetRuntime).GetMethod(name, Flags)!.Invoke(runtime, args)!;
}
