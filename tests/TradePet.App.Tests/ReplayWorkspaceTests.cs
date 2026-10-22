using TradePet.Application.Review;
using TradePet.Application.Runtime;
using TradePet.App.ViewModels.Review;
using TradePet.Core.Domain;
using TradePet.Core.Review;
using Xunit;

namespace TradePet.App.Tests;

public sealed class ReplayWorkspaceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PeriodChange_ReloadsPreservingCursorAndDiscardsOldView()
    {
        var model = new ReviewWorkspaceViewModel();
        model.ApplyReplay(History(), Frame(), 25, Frame());
        model.ReplayWindow = new(Start, Start.AddMinutes(5));
        model.ReplayCrosshairUtc = Start;
        model.IsReplayPlaying = true;
        var count = 0;
        model.LoadReplayAsync = () => { count++; return Task.CompletedTask; };
        var previousVersion = model.ReplayRequestVersion;
        model.ReplayTimeframe = "M15";
        Assert.Equal(1, count);
        Assert.True(model.ReplayRequestVersion > previousVersion);
        Assert.Equal(Start.AddMinutes(15), model.ReplayResumeAtUtc);
        Assert.Null(model.ReplayChartFrame);
        Assert.Null(model.SecondaryReplayFrame);
        Assert.Null(model.ReplayWindow);
        Assert.Null(model.ReplayCrosshairUtc);
        Assert.False(model.IsReplayPlaying);
        Assert.Equal(1200, model.ReplayDefaultWindowMinutes);
    }

    [Fact]
    public void DisablingSecondary_ReloadsAndRemovesSecondaryFrame()
    {
        var model = new ReviewWorkspaceViewModel { DualReplayEnabled = true };
        model.ApplyReplay(History(), Frame(), 25, Frame());
        var count = 0;
        model.LoadReplayAsync = () => { count++; return Task.CompletedTask; };
        model.DualReplayEnabled = false;
        Assert.Equal(1, count);
        Assert.Null(model.SecondaryReplayFrame);
        model.SecondaryReplayTimeframe = "M15";
        Assert.Equal(1, count);
        model.ReplayTimeframe = "invalid";
        Assert.Equal("M5", model.ReplayTimeframe);
    }

    [Fact]
    public void Playback_CanPauseImmediatelyAndOldLoopCannotResumeAfterRestart()
    {
        var scheduler = new ManualScheduler();
        var model = new ReviewWorkspaceViewModel(scheduler);
        model.ApplyReplay(History(), Frame(), 25);
        var seekCount = 0;
        model.SeekReplayAsync = _ => { seekCount++; return Task.CompletedTask; };
        model.ToggleReplayCommand.Execute(null);
        Assert.True(model.IsReplayPlaying);
        Assert.True(model.ToggleReplayCommand.CanExecute(null));
        model.ToggleReplayCommand.Execute(null);
        Assert.False(model.IsReplayPlaying);
        model.ToggleReplayCommand.Execute(null);
        scheduler.Delays[0].SetResult();
        Assert.Equal(0, seekCount);
        model.ToggleReplayCommand.Execute(null);
        scheduler.Delays[1].SetResult();
        Assert.False(model.IsReplayPlaying);
        Assert.Equal(0, seekCount);
    }

    [Fact]
    public void EventJump_UsesRequestedRangeAndDoesNotExposeFutureEvents()
    {
        var model = new ReviewWorkspaceViewModel();
        double? seek = null;
        model.SeekReplayAsync = progress => { seek = progress; return Task.CompletedTask; };
        model.ApplyReplay(History(), Frame() with { NavigationEvents = [
            new(Start.AddMinutes(10), "成交", "entry", ReviewEvidenceSource.Mt5Deal),
            new(Start.AddMinutes(20), "行为", "future", ReviewEvidenceSource.LiveObservation)] }, 25);
        var row = Assert.Single(model.ReplayJumpEvents);
        Assert.DoesNotContain("future", row.Label);
        row.JumpCommand.Execute(null);
        Assert.Equal(100d / 6, seek!.Value, 6);
    }

    [Fact]
    public void AccountReset_ClearsChartCursorAndHoldingGroups()
    {
        var model = new ReviewWorkspaceViewModel();
        model.ApplyReplay(History(), Frame(), 25, Frame());
        model.HoldingProcessGroups.Add(new("x", "y", "1/1", "-1", "2", "1"));
        model.ResetAccountState("changed");
        Assert.Null(model.ReplayChartFrame);
        Assert.Null(model.SecondaryReplayFrame);
        Assert.Null(model.ReplayResumeAtUtc);
        Assert.Null(model.ReplayChartFrom);
        Assert.Empty(model.HoldingProcessGroups);
        Assert.Empty(model.ReplayJumpEvents);
    }

    [Fact]
    public void HoldingGroups_ExcludeUnreliableSamplesAndUseServerEntryHour()
    {
        var one = ReviewDashboardTests.Trade(1, 10m) with { OpenedAtUtc = Start };
        var two = ReviewDashboardTests.Trade(2, 100m) with { OpenedAtUtc = Start };
        var fixture = ReviewDashboardTests.Fixture([one, two]);
        var snapshot = fixture.Snapshot with { RiskSamples = [
            new(new(one.AccountKey, 1), one.Symbol, 10m, 1m, null, -20m, 50m, null, true),
            new(new(two.AccountKey, 2), two.Symbol, 100m, 1m, null, -1000m, 9999m, null, false)] };
        var model = new ReviewWorkspaceViewModel { ServerUtcOffsetSeconds = 7200 };
        model.Apply(snapshot, fixture.Data, 1);
        var group = Assert.Single(model.HoldingProcessGroups, row => row.Group == "10:00");
        Assert.Equal("1/2", group.Coverage);
        Assert.Equal("-20", group.AverageMae);
        Assert.Equal("+50", group.AverageMfe);
        Assert.Equal("+40", group.AverageGiveback);
    }

    internal static MarketHistoryResult History() => new(new("r", "t", "Broker|1", "TEST", "M5", Start, Start.AddHours(1),
        Start.AddMinutes(5), Start.AddMinutes(50), MarketDataPrecision.Bars, MarketCoverageStatus.Partial, "1", "", Start), [], []);
    private static ReplayFrame Frame() => new(Start.AddMinutes(15), [], [], [], [], [], [], MarketCoverageStatus.Partial, "");
    private sealed class ManualScheduler : IAsyncScheduler
    {
        public List<TaskCompletionSource> Delays { get; } = [];
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource();
            Delays.Add(completion);
            return completion.Task;
        }
    }
}
