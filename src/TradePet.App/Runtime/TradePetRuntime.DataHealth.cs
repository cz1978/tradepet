using TradePet.Core.Localization;
using TradePet.Core.Trading;

namespace TradePet.App.Runtime;

public sealed partial class TradePetRuntime
{
    private Task UpdateDataHealthAsync()
    {
        var positions = _positions.Values.ToArray();
        var risk = PortfolioRiskCalculator.Calculate(positions);
        var account = _account;
        string Number(decimal? number) => number?.ToString("0.##") ?? UiText.Get("FunctionalUnknown");
        var stale = !_workerConnected || account is null || _timeProvider.GetUtcNow() - account.CapturedAtUtc > TimeSpan.FromSeconds(10);
        var riskText = account is null ? UiText.Get("FunctionalWaitingAccount") :
            string.Format(UiText.Get("FunctionalPortfolioRisk"), risk.KnownLossAtStops, risk.KnownDrawdownToStops, account.Currency,
                risk.CoveredPositions, risk.TotalPositions, risk.MissingStops, risk.MissingValuations) + "\n" +
            string.Format(UiText.Get("FunctionalMargin"), Number(account.UsedMargin), Number(account.FreeMargin),
                Number(account.UsedMargin is > 0m ? account.Equity / account.UsedMargin * 100m : account.MarginLevel));
        if (stale) riskText = UiText.Get("FunctionalStaleRisk") + riskText;
        var initial = positions.Count(item => _excursions.TryGetValue(item.PositionId, out var excursion) && excursion.HasReliableInitialRisk);
        var health = string.Format(UiText.Get("FunctionalHealth"),
            UiText.Get(stale ? "FunctionalStale" : "FunctionalFresh"),
            UiText.Translate(_viewModel.BridgeText),
            UiText.Get(_hasInitialDeals ? "FunctionalHistoryReady" : "FunctionalHistoryPending"), initial, positions.Length);
        return OnUiAsync(() => { _viewModel.PortfolioRiskText = riskText; _viewModel.DataHealthText = health; });
    }
}
