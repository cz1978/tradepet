using TradePet.Core.Domain;
using TradePet.Core.Trading;
using TradePet.Core.Localization;

namespace TradePet.App.Runtime;

public sealed partial class TradePetRuntime
{
    private readonly Dictionary<string, PendingAlert> _deferredAlerts = new(StringComparer.Ordinal);
    private PendingAlert? _activeAlertContext;
    private Task _snoozedAlertTask = Task.CompletedTask;

    private async Task NotifyGoalObservationAsync(ImprovementGoal goal, GoalObservation observation,
        BehaviorEvaluation evaluation, TradeRecord trade, DateTimeOffset observedAtUtc)
    {
        if (!goal.NotificationEnabled || observation.Status != GoalObservationStatus.Failed) return;
        var fact = new RuleFact(RuleFactKind.Alert, AlertPriority.Important, 25,
            string.Format(UiText.Get("FunctionalGoalReminder"), goal.Name),
            string.Format(UiText.Get("FunctionalGoalReminderDetail"), trade.Symbol, UiText.Translate(evaluation.Summary),
                evaluation.Threshold));
        var alert = _alertComposer.Compose($"goal:{goal.Id}:{trade.PositionId}", [fact], observedAtUtc);
        if (alert is not null) await ShowAlertAsync(alert, PetSignal.Risk);
    }

    private sealed record PendingAlert(CombinedAlert Alert, PetSignal Signal, LossZoneState? Zone,
        decimal? Price, bool Persist, bool Deduplicate, string? AccountKey, long Generation, DateOnly Date);

    private PendingAlert CaptureAlert(CombinedAlert alert, PetSignal signal, LossZoneState? zone,
        decimal? price, bool persist, bool deduplicate) => new(alert, signal, zone, price, persist,
        deduplicate, _account?.Scope.AccountKey, _accountSessions.Current?.Generation ?? -1, _serverDate);

    private bool IsAlertRelevant(PendingAlert pending)
    {
        if (pending.AccountKey != _account?.Scope.AccountKey || pending.Date != _serverDate ||
            pending.Generation != (_accountSessions.Current?.Generation ?? -1) || !_workerConnected || _account is null ||
            _timeProvider.GetUtcNow() - _account.CapturedAtUtc > TimeSpan.FromSeconds(10)) return false;
        if (pending.Alert.Id.StartsWith("goal:", StringComparison.Ordinal))
        {
            var position = _positions.Values.FirstOrDefault(item => pending.Alert.Id.EndsWith($":{item.PositionId}", StringComparison.Ordinal));
            return position is not null && _activeImprovementGoals.Any(goal => goal.NotificationEnabled &&
                pending.Alert.Id.StartsWith($"goal:{goal.Id}:", StringComparison.Ordinal) &&
                AppliesToGoal(goal, goal.Rule ?? BehaviorRuleKind.ReentryCount, _serverDate,
                    position.Symbol));
        }
        if (pending.Alert.Facts.Any(fact => fact.Kind == RuleFactKind.MissingStopLoss))
            return _settings.StopLossReminderEnabled && long.TryParse(pending.Alert.Id.Split(':')[^1], out var positionId) &&
                _positions.Values.Any(position => position.PositionId == positionId && position.StopLoss <= 0m);
        var identity = AlertTradeIdentity.TryExtract(pending.Alert.Id);
        if (identity is not null && long.TryParse(identity, out var tradeId))
            return _positions.Values.Any(position => position.PositionId == tradeId);
        if (pending.Alert.Id.StartsWith("floating-loss:", StringComparison.Ordinal))
            return RefreshPendingAlert(pending) is not null;
        return !pending.Alert.Id.StartsWith("daily:", StringComparison.Ordinal) || RefreshPendingAlert(pending) is not null;
    }

    private async Task DrainDeferredAlertsAsync()
    {
        if (!_workerConnected || _account is null || _timeProvider.GetUtcNow() - _account.CapturedAtUtc > TimeSpan.FromSeconds(10)) return;
        foreach (var pending in _deferredAlerts.Values.OrderByDescending(item => item.Alert.Priority)
                     .ThenBy(item => item.Alert.OccurredAtUtc).ToArray())
        {
            if (!IsAlertRelevant(pending)) { _deferredAlerts.Remove(pending.Alert.Id); continue; }
            if (_petState.Current.ActiveAlertPriority is not null) return;
            _deferredAlerts.Remove(pending.Alert.Id);
            var alert = RefreshPendingAlert(pending);
            if (alert is not null && await ShowAlertAsync(alert, pending.Signal, pending.Zone, pending.Price,
                    pending.Persist, pending.Deduplicate)) return;
        }
    }

    private CombinedAlert? RefreshPendingAlert(PendingAlert pending)
    {
        if (_account is null) return null;
        var now = _timeProvider.GetUtcNow();
        if (pending.Alert.Id.StartsWith("floating-loss:", StringComparison.Ordinal))
        {
            if (!int.TryParse(pending.Alert.Id.Split(':')[^2], out var stage)) return null;
            var level = _floatingLossPolicy.Levels.FirstOrDefault(item => item.Stage == stage && item.Enabled);
            var loss = Math.Max(0m, -_account.FloatingPnl);
            var percentage = _account.Balance > 0m ? loss / _account.Balance * 100m : 0m;
            if (level is null || percentage < level.LossPercentage) return null;
            var fact = new RuleFact(RuleFactKind.Alert, pending.Alert.Priority, 5,
                UiText.Get("FunctionalFloatingReminder"),
                string.Format(UiText.Get("FunctionalFloatingReminderDetail"), loss, _account.Currency, percentage, stage, level.LossPercentage));
            return _alertComposer.Compose(pending.Alert.Id, [fact], now);
        }
        if (pending.Alert.Id.StartsWith("daily:", StringComparison.Ordinal))
        {
            var kinds = pending.Alert.Facts.Select(item => item.Kind).ToHashSet();
            var current = _dailyCalculator.Calculate(_account.Scope.AccountKey, _serverDate, AnalyticsDailyTrades(),
                _positions.Values.ToArray(), EffectiveAlertSettings(), null, _dailyState?.HighWaterPnl,
                CalculateDailyRealizedPnl(), now);
            return _alertComposer.Compose(pending.Alert.Id, current.NewFacts.Where(item => kinds.Contains(item.Kind)), now);
        }
        return pending.Alert;
    }
}
