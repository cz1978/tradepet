using System.Security.Cryptography;
using System.Text.Json;
using TradePet.Core.Domain;

namespace TradePet.Core.Review;

public static class BehaviorGoalMeasurement
{
    public static IReadOnlyList<BehaviorRuleKind> SupportedRules(BehaviorPolicy policy) => new[]
    {
        BehaviorRuleKind.CooldownViolation,
        BehaviorRuleKind.SizeEscalationAfterLoss,
        BehaviorRuleKind.OvertradeBurst,
        BehaviorRuleKind.RevengeScore,
        BehaviorRuleKind.LossZonePersistence,
        BehaviorRuleKind.ReentryCount,
        BehaviorRuleKind.PriceFixationScore,
    }.Where(rule => policy.EnabledRules.IsEnabled(rule) && (policy.HardLimitEnabled ||
        policy.BaselineEnabled && rule is BehaviorRuleKind.OvertradeBurst or BehaviorRuleKind.PriceFixationScore)).ToArray();

    public static string PolicyVersion(BehaviorPolicy policy) =>
        $"behavior-policy-v2:{Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(policy)))}";

    public static GoalObservation Measure(ImprovementGoal goal, BehaviorEvaluation evaluation,
        TradeRecord opening, IReadOnlyCollection<TradeRecord> trades, BehaviorPolicy policy)
    {
        var prior = trades.Where(item => item.AccountKey == opening.AccountKey && item.PositionId != opening.PositionId &&
            item.OpenServerDate == evaluation.ServerDate && item.IsComplete && item.ClosedAtUtc <= opening.OpenedAtUtc)
            .OrderBy(item => item.ClosedAtUtc).ThenBy(item => item.PositionId).ToArray();
        var known = goal.AccountKey == opening.AccountKey && evaluation.AccountKey == opening.AccountKey && goal.Rule == evaluation.Rule &&
                    goal.RuleVersion == PolicyVersion(policy) && SupportedRules(policy).Contains(evaluation.Rule) &&
                    evaluation.Level != BehaviorRiskLevel.Observing &&
                    (policy.HardLimitEnabled || policy.BaselineEnabled && evaluation.Baseline is > 0m);
        if (evaluation.Rule == BehaviorRuleKind.PriceFixationScore)
        {
            var sampleCount = trades.Count(item => item.AccountKey == opening.AccountKey &&
                item.OpenServerDate == evaluation.ServerDate && item.PositionId != opening.PositionId &&
                item.OpenedAtUtc <= opening.OpenedAtUtc) + 1;
            known &= sampleCount >= Math.Max(3, policy.PriceFixationMinimumTrades);
        }
        var applicable = evaluation.Rule switch
        {
            BehaviorRuleKind.CooldownViolation => prior.Reverse().TakeWhile(item => item.NetPnl < -0.01m).Count() >= policy.ConsecutiveLossThreshold,
            BehaviorRuleKind.SizeEscalationAfterLoss => prior.LastOrDefault() is { NetPnl: < -0.01m, OpeningVolume: > 0m },
            BehaviorRuleKind.RevengeScore => prior.LastOrDefault() is { NetPnl: < -0.01m },
            BehaviorRuleKind.ReentryCount or BehaviorRuleKind.LossZonePersistence or BehaviorRuleKind.PriceFixationScore => evaluation.Value > 0m,
            _ => true,
        };
        var status = !known ? GoalObservationStatus.Unknown : !applicable ? GoalObservationStatus.NotApplicable :
            evaluation.Triggered ? GoalObservationStatus.Failed : GoalObservationStatus.Passed;
        return new GoalObservation($"{goal.Id}:{evaluation.Id}", goal.Id, goal.AccountKey, evaluation.ServerDate,
            applicable ? 1 : 0, status == GoalObservationStatus.Passed ? 1 : 0, status == GoalObservationStatus.Failed ? 1 : 0,
            status, evaluation.Summary, evaluation.ObservedAtUtc, [evaluation.Id], PolicyVersion(policy));
    }
}
