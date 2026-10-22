using TradePet.Core.Domain;

namespace TradePet.Core.Trading;

public sealed record PortfolioRisk(decimal KnownLossAtStops, decimal KnownDrawdownToStops, int CoveredPositions,
    int MissingStops, int MissingValuations, int TotalPositions)
{
    public bool IsComplete => CoveredPositions == TotalPositions;
}

public static class PortfolioRiskCalculator
{
    public static PortfolioRisk Calculate(IReadOnlyCollection<PositionSnapshot> positions)
    {
        decimal loss = 0m, drawdown = 0m;
        var covered = 0; var missingStops = 0; var missingValuations = 0;
        foreach (var position in positions)
        {
            if (position.StopLoss <= 0m) { missingStops++; continue; }
            if (position.InitialRiskAmount is not >= 0m) { missingValuations++; continue; }
            var losingStop = position.Side == TradeSide.Buy ? position.StopLoss < position.EntryPrice : position.StopLoss > position.EntryPrice;
            var stopPnl = losingStop ? -position.InitialRiskAmount.Value : position.InitialRiskAmount.Value;
            loss += Math.Max(0m, -stopPnl);
            drawdown += Math.Max(0m, position.Profit - stopPnl);
            covered++;
        }
        return new(loss, drawdown, covered, missingStops, missingValuations, positions.Count);
    }
}
