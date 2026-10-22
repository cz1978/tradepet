using System.IO;
using System.Reflection;
using TradePet.Application.Review;
using TradePet.Application.Runtime;
using TradePet.App.Runtime;
using TradePet.App.ViewModels;
using TradePet.App.ViewModels.Review;
using TradePet.Core.Domain;
using TradePet.Infrastructure.Persistence;
using Xunit;

namespace TradePet.App.Tests;

public sealed class ReplayRuntimeTests
{
    [Fact]
    public async Task OfflineDualCharts_UseSameCursorAndNeverInventMissingHigherPeriodBars()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.LoadAsync();
        var review = fixture.Review;
        Assert.Equal(fixture.Trade.OpenedAtUtc, review.ReplayChartFrame!.CursorUtc);
        Assert.Equal(review.ReplayChartFrame.CursorUtc, review.SecondaryReplayFrame!.CursorUtc);
        Assert.Single(review.ReplayChartFrame.VisibleBars);
        Assert.Single(review.SecondaryReplayFrame.VisibleBars);
        var desired = fixture.Trade.OpenedAtUtc.AddMinutes(15);
        var progress = (desired - review.ReplayChartFrom!.Value).TotalSeconds /
            (review.ReplayChartTo!.Value - review.ReplayChartFrom.Value).TotalSeconds * 100;
        await fixture.SeekAsync(progress);
        Assert.Equal(review.ReplayChartFrame!.CursorUtc, review.SecondaryReplayFrame!.CursorUtc);
        Assert.Equal(2, review.ReplayChartFrame.VisibleBars.Count);
        Assert.Single(review.SecondaryReplayFrame.VisibleBars);
        Assert.Null(review.ReplayChartFrame.VisibleFinalNetPnl);
        await fixture.SeekAsync(100);
        Assert.Equal(20m, review.ReplayChartFrame!.VisibleFinalNetPnl);
    }

    [Fact]
    public async Task OfflineMissingPeriod_ShowsEmptyCoverageWithoutBorrowingAnotherPeriod()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Review.SecondaryReplayTimeframe = "M15";
        await fixture.LoadAsync();
        Assert.NotEmpty(fixture.Review.ReplayChartFrame!.VisibleBars);
        Assert.Empty(fixture.Review.SecondaryReplayFrame!.VisibleBars);
        Assert.Equal(MarketCoverageStatus.Empty, fixture.Review.SecondaryReplayFrame.Coverage);
    }

    [Fact]
    public async Task TradeSwitch_DiscardsPendingLoadAndPreventsOldHistoryFromSeekingNewTrade()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.LoadAsync();
        var gate = fixture.GetField<SemaphoreSlim>("_replayLoadGate");
        await gate.WaitAsync();
        var loading = fixture.LoadAsync();
        try { fixture.SelectTrade(fixture.Trade with { PositionId = 18 }); }
        finally { gate.Release(); }
        await loading;
        await fixture.SeekAsync(100);
        Assert.Null(fixture.Review.ReplayChartFrame);
        Assert.Null(fixture.Review.SecondaryReplayFrame);
    }

    [Fact]
    public async Task AccountSwitch_CancelsPendingLoad()
    {
        await using var fixture = await Fixture.CreateAsync();
        var gate = fixture.GetField<SemaphoreSlim>("_replayLoadGate");
        await gate.WaitAsync();
        var loading = fixture.LoadAsync();
        try { await fixture.Sessions.SwitchAsync("Other|2"); }
        finally { gate.Release(); }
        await loading;
        Assert.Null(fixture.Review.ReplayChartFrame);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        public required TradePetRuntime Runtime { get; init; }
        public required ReviewWorkspaceViewModel Review { get; init; }
        public required AccountSessionCoordinator Sessions { get; init; }
        public required TradeRecord Trade { get; init; }
        public required ReviewDataVersion Version { get; init; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "tradepet-replay-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var databasePath = Path.Combine(root, "replay.db");
            var database = new AppDatabase(databasePath);
            await database.InitializeAsync();
            var start = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
            var account = new AccountSnapshot(new("ReplayTest", 1), "USD", 10000m, 10000m, 0m, 2, start);
            await database.UpsertAccountAsync(account);
            var trade = new TradeRecord(account.Scope.AccountKey, 17, "TEST", TradeSide.Buy, start, start.AddMinutes(30),
                DateOnly.FromDateTime(start.UtcDateTime), DateOnly.FromDateTime(start.UtcDateTime), 100m, 120m, 1m, 1m, 0m, 20m, true);
            await database.SaveTradesAsync([trade]);
            foreach (var (timeframe, minutes) in new[] { ("M5", 5), ("H1", 60) })
            {
                var range = new MarketDataRange(Guid.NewGuid().ToString("N"), "test", trade.AccountKey, "TEST", timeframe,
                    start.AddDays(-1), start.AddHours(2), start.AddMinutes(-minutes), start, MarketDataPrecision.Bars,
                    MarketCoverageStatus.Partial, "test", "", start);
                await database.SaveMarketDataAsync(range, [
                    new("test", trade.AccountKey, "TEST", timeframe, start.AddMinutes(-minutes), 90, 101, 89, 100, 1, 0, 1),
                    new("test", trade.AccountKey, "TEST", timeframe, start, 100, 125, 95, 120, 1, 0, 1)], []);
            }
            var sessions = new AccountSessionCoordinator();
            await sessions.SwitchAsync(trade.AccountKey);
            var scheduler = new SystemAsyncScheduler();
            var model = new MainViewModel(scheduler);
            var attachments = Path.Combine(root, "attachments");
            var runtime = new TradePetRuntime(model, new(database, database, new ReviewPackageWriter(),
                new ReviewAttachmentStore(database, attachments), new ReviewBackupService(database, attachments, databasePath),
                TimeProvider.System, scheduler, sessions, new MaintenanceCoordinator(), Path.Combine(root, "backups")));
            var fixture = new Fixture { Runtime = runtime, Review = model.ReviewWorkspace, Sessions = sessions, Trade = trade,
                Version = new(trade.AccountKey, 1, 1, 1, "rules", "time", start) };
            fixture.SetField("_offlineReviewOnly", true);
            fixture.Review.DualReplayEnabled = true;
            fixture.SelectTrade(trade);
            return fixture;
        }

        public void SelectTrade(TradeRecord trade)
        {
            var detail = new TradeDetailData(trade, [], null, null, null, [], null, null, [], [], [], null, Version);
            SetField("_lastTradeDetail", detail);
            Review.ApplyDetail(detail, Sessions.Current!.Generation);
        }
        public T GetField<T>(string name) => (T)typeof(TradePetRuntime).GetField(name, Flags)!.GetValue(Runtime)!;
        private void SetField(string name, object value) => typeof(TradePetRuntime).GetField(name, Flags)!.SetValue(Runtime, value);
        public Task LoadAsync() => (Task)typeof(TradePetRuntime).GetMethod("LoadTradeReplayAsync", Flags)!.Invoke(Runtime, null)!;
        public Task SeekAsync(double progress) => (Task)typeof(TradePetRuntime).GetMethod("SeekTradeReplayAsync", Flags)!.Invoke(Runtime, [progress])!;
        public ValueTask DisposeAsync() => Runtime.DisposeAsync();
    }
}
