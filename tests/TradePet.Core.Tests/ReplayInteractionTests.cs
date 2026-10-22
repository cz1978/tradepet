using TradePet.Core.Domain;
using TradePet.Core.Review;
using Xunit;

namespace TradePet.Core.Tests;

public sealed class ReplayInteractionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Zoom_KeepsPointerTimeAnchored_AndPanStaysInsideHistory()
    {
        var window = new ReplayChartWindow(Start.AddMinutes(20), Start.AddMinutes(60));
        var zoomed = window.Zoom(0.5, 0.25, Start, Start.AddHours(2));
        Assert.Equal(window.At(0.25), zoomed.At(0.25));
        Assert.Equal(TimeSpan.FromMinutes(20), zoomed.Span);
        var left = window.Pan(-1, Start, Start.AddHours(2));
        Assert.Equal(Start, left.FromUtc);
        Assert.Equal(window.Span, left.Span);
        var right = window.Pan(1, Start, Start.AddHours(1));
        Assert.Equal(Start.AddHours(1), right.ToUtc);
    }

    [Fact]
    public void Window_HandlesEmptyBoundsAndNonFiniteInput()
    {
        var window = new ReplayChartWindow(Start, Start);
        var normalized = window.Clamp(Start, Start);
        Assert.Equal(TimeSpan.FromMinutes(1), normalized.Span);
        Assert.Equal(normalized, window.Zoom(double.NaN, double.PositiveInfinity, Start, Start));
        Assert.Equal(normalized, window.Pan(double.NaN, Start, Start));
        Assert.Equal(Start, normalized.At(double.NaN));
        Assert.Equal(TimeSpan.FromSeconds(10), window.Clamp(Start, Start.AddSeconds(10)).Span);
    }

    [Fact]
    public void Holding_UsesOnlyObservedSamplesAndDoesNotInferMissingFinalResult()
    {
        var analysis = HoldingProcessAnalysis.FromFrame(Frame([Sample(0, -20), Sample(2, 50), Sample(20, 1000)]));
        Assert.Equal(2, analysis.Samples.Count);
        Assert.Equal(-20m, analysis.ObservedTrough);
        Assert.Equal(50m, analysis.ObservedPeak);
        Assert.Null(analysis.FinalNetPnl);
        Assert.Null(analysis.Giveback);
        Assert.False(analysis.HasGaps);
    }

    [Fact]
    public void Holding_NoSamplesRemainsUnknown()
    {
        var analysis = HoldingProcessAnalysis.FromFrame(Frame([]) with { VisibleFinalNetPnl = 25m });
        Assert.Null(analysis.ObservedPeak);
        Assert.Null(analysis.ObservedTrough);
        Assert.Null(analysis.Giveback);
        Assert.Equal(25m, analysis.FinalNetPnl);
    }

    [Fact]
    public void Holding_ObservedExtremesDoNotInventAZeroSample()
    {
        var losing = HoldingProcessAnalysis.FromFrame(Frame([Sample(0, -20), Sample(2, -10)]) with { VisibleFinalNetPnl = -15 });
        Assert.Equal(-10m, losing.ObservedPeak);
        Assert.Null(losing.Giveback);
        var winning = HoldingProcessAnalysis.FromFrame(Frame([Sample(0, 10), Sample(2, 20)]));
        Assert.Equal(10m, winning.ObservedTrough);
    }

    [Theory]
    [InlineData(1, 6000, "position-pnl-v1")]
    [InlineData(9, 0, "position-pnl-v1")]
    [InlineData(1, 0, "legacy-extrema-v1")]
    public void Holding_FlagsReportedTemporalAndLegacyGaps(int seconds, long gap, string algorithm)
    {
        var analysis = HoldingProcessAnalysis.FromFrame(Frame([Sample(0, 30), Sample(seconds, 10) with { GapMilliseconds = gap, AlgorithmVersion = algorithm }])
            with { VisibleFinalNetPnl = 5m });
        Assert.True(analysis.HasGaps);
        Assert.Equal(25m, analysis.Giveback);
    }

    [Fact]
    public void LinkedPeriods_SameCursorNeverShowsUnfinishedHigherTimeframe()
    {
        var calculator = new ReviewWorkspaceCalculator();
        ReplayFrame Build(string timeframe) => calculator.BuildReplayFrame(Start.AddMinutes(15),
            new("r", "t", "a", "TEST", timeframe, Start, Start.AddHours(2), Start, Start, MarketDataPrecision.Bars,
                MarketCoverageStatus.Partial, "1", "", Start),
            [new("t", "a", "TEST", timeframe, Start, 100, 102, 99, 101, 1, 0, 1)], [], [], [], [], []);
        var primary = Build("M5"); var secondary = Build("H1");
        Assert.Equal(primary.CursorUtc, secondary.CursorUtc);
        Assert.Single(primary.VisibleBars);
        Assert.Empty(secondary.VisibleBars);
        Assert.Single(Build("M15").VisibleBars);
    }

    private static PositionPnlSample Sample(int seconds, decimal pnl) => new(new("a", 1), Start.AddSeconds(seconds), 0m, pnl, pnl, 1m, null, null, 0, "position-pnl-v1");
    private static ReplayFrame Frame(IReadOnlyList<PositionPnlSample> samples) => new(Start.AddSeconds(10), [], [], [], samples, [], [], MarketCoverageStatus.Partial, "");
}
