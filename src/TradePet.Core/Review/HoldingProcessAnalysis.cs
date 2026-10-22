using TradePet.Core.Domain;

namespace TradePet.Core.Review;

public sealed record HoldingProcessAnalysis(IReadOnlyList<PositionPnlSample> Samples,
    decimal? ObservedPeak, decimal? ObservedTrough, decimal? FinalNetPnl, decimal? Giveback, bool HasGaps)
{
    public static HoldingProcessAnalysis FromFrame(ReplayFrame frame)
    {
        var samples = frame.VisiblePnlSamples.Where(sample => sample.CapturedAtUtc <= frame.CursorUtc)
            .OrderBy(sample => sample.CapturedAtUtc).ToArray();
        decimal? peak = samples.Length == 0 ? null : samples.Max(sample => sample.NetPnl);
        decimal? trough = samples.Length == 0 ? null : samples.Min(sample => sample.NetPnl);
        var gaps = samples.Any(sample => sample.GapMilliseconds > 5000 || sample.AlgorithmVersion != "position-pnl-v1") ||
            samples.Zip(samples.Skip(1)).Any(pair => pair.Second.CapturedAtUtc - pair.First.CapturedAtUtc > TimeSpan.FromSeconds(5));
        return new(samples, peak, trough, frame.VisibleFinalNetPnl,
            peak is > 0 && frame.VisibleFinalNetPnl is { } final ? Math.Max(0, peak.Value - final) : null, gaps);
    }
}
