using TradePet.Core.Domain;
using TradePet.Core.Localization;

namespace TradePet.Core.Trading;

public sealed record PlanExecutionCheck(RuleAssessmentStatus Entry, RuleAssessmentStatus InitialStop,
    RuleAssessmentStatus InitialTarget, RuleAssessmentStatus Exit, string Detail);

public static class PlanExecutionChecker
{
    public static PlanExecutionCheck Check(TradeRecord trade, StructuredTradePlan? plan,
        IReadOnlyList<PositionPnlSample> samples, TradeExcursion? excursion, decimal priceStep = 0.00000001m)
    {
        if (plan is null || plan.AccountKey != trade.AccountKey || plan.CreatedAtUtc > trade.OpenedAtUtc)
            return new(RuleAssessmentStatus.Unknown, RuleAssessmentStatus.Unknown, RuleAssessmentStatus.Unknown,
                RuleAssessmentStatus.Unknown, UiText.Get("FunctionalPlanNoEvidence"));
        var tolerance = Math.Max(0.00000001m, priceStep);
        var entry = plan.EntryLow is null || plan.EntryHigh is null ? RuleAssessmentStatus.Unknown :
            trade.EntryPrice >= plan.EntryLow.Value - tolerance && trade.EntryPrice <= plan.EntryHigh.Value + tolerance
                ? RuleAssessmentStatus.Passed : RuleAssessmentStatus.Failed;
        var first = samples.Where(item => item.TradeKey == new TradeKey(trade.AccountKey, trade.PositionId) &&
            item.CapturedAtUtc >= trade.OpenedAtUtc).OrderBy(item => item.CapturedAtUtc).FirstOrDefault();
        var reliable = excursion?.StartedAtOpen == true && first is not null &&
            first.CapturedAtUtc - trade.OpenedAtUtc <= TimeSpan.FromSeconds(3);
        RuleAssessmentStatus Protection(decimal? observed, decimal? planned) => !reliable || planned is null
            ? RuleAssessmentStatus.Unknown : observed is > 0m && Math.Abs(observed.Value - planned.Value) <= tolerance
                ? RuleAssessmentStatus.Passed : RuleAssessmentStatus.Failed;
        var stop = Protection(first?.StopLoss, plan.StopPrice);
        var target = Protection(first?.TakeProfit, plan.TargetPrice);
        var exit = trade.IsComplete && trade.ExitPrice is { } price &&
            (plan.StopPrice is { } sl && Math.Abs(price - sl) <= tolerance ||
             plan.TargetPrice is { } tp && Math.Abs(price - tp) <= tolerance)
                ? RuleAssessmentStatus.Passed : RuleAssessmentStatus.Unknown;
        string Status(RuleAssessmentStatus status) => UiText.Get(status == RuleAssessmentStatus.Passed ? "FunctionalRulePassed" :
            status == RuleAssessmentStatus.Failed ? "FunctionalRuleFailed" : "FunctionalRuleUnknown");
        return new(entry, stop, target, exit,
            string.Format(UiText.Get("FunctionalPlanExecution"), Status(entry), Status(stop), Status(target), Status(exit)));
    }
}
