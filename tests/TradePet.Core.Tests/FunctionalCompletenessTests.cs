using TradePet.Core.Domain;
using TradePet.Core.Review;
using TradePet.Core.Trading;
using Xunit;

namespace TradePet.Core.Tests;

public sealed class FunctionalCompletenessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 10, 19);
    private static TradeRecord Trade => new("Broker|1", 1, "TEST", TradeSide.Buy, Now, Now.AddMinutes(5),
        Date, Date, 100m, 103m, 1m, 1m, 0m, 3m, true);
    private static StructuredTradePlan Plan => new("plan", "Broker|1", Date, "TEST", TradeSide.Buy,
        100m, 99m, 101m, 98m, 105m, "", "", [], "", true, Now.AddMinutes(-1), Now.AddMinutes(-1));
    private static TradeExcursion Excursion => new("Broker|1", 1, -1m, 4m, 2m, 2.5m, 1.5m,
        Now, Now.AddMinutes(5), 300000, 300000, true, true);

    [Fact]
    public void ExecutionChecks_SeparateEntryProtectionAndUnexplainedExit()
    {
        var sample = new PositionPnlSample(new("Broker|1", 1), Now, 0m, 0m, 0m, 1m, 97m, 105m, 0, "test");
        var check = PlanExecutionChecker.Check(Trade, Plan, [sample], Excursion);
        Assert.Equal(RuleAssessmentStatus.Passed, check.Entry);
        Assert.Equal(RuleAssessmentStatus.Failed, check.InitialStop);
        Assert.Equal(RuleAssessmentStatus.Passed, check.InitialTarget);
        Assert.Equal(RuleAssessmentStatus.Unknown, check.Exit);
        Assert.Equal(RuleAssessmentStatus.Unknown,
            PlanExecutionChecker.Check(Trade, Plan, [sample with { CapturedAtUtc = Now.AddMinutes(1) }], Excursion).InitialStop);
        Assert.Equal(RuleAssessmentStatus.Unknown,
            PlanExecutionChecker.Check(Trade, Plan with { CreatedAtUtc = Now.AddSeconds(1) }, [sample], Excursion).Entry);
    }

    [Fact]
    public void PortfolioRisk_UsesCurrencyAmountsAndReportsUnboundedAndUnknownPositions()
    {
        PositionSnapshot Position(long id, decimal sl, decimal? risk, decimal pnl = 30m) =>
            new(id, id, "TEST", TradeSide.Buy, 1m, 100m, 103m, pnl, sl, 110m, Now, Now, InitialRiskAmount: risk);
        var risk = PortfolioRiskCalculator.Calculate([
            Position(1, 98m, 20m), Position(2, 102m, 20m, 50m), Position(3, 0m, null), Position(4, 98m, null)]);
        Assert.Equal(20m, risk.KnownLossAtStops);
        Assert.Equal(80m, risk.KnownDrawdownToStops);
        Assert.Equal(2, risk.CoveredPositions);
        Assert.Equal(1, risk.MissingStops);
        Assert.Equal(1, risk.MissingValuations);
        Assert.False(risk.IsComplete);
    }

    [Fact]
    public void GoalTarget_AssessesAdherenceWithoutChangingTheFrozenRuleThreshold()
    {
        var policy = BehaviorPolicy.Balanced;
        var goal = new ImprovementGoal("goal", "Broker|1", "Reentry", BehaviorRuleKind.ReentryCount,
            BehaviorGoalMeasurement.PolicyVersion(policy), Date, Date.AddDays(6), 100m, null, "adherence", "", true,
            ImprovementGoalStatus.Active, 1, Now.AddMinutes(-1), Now, ObservationWindowDays: 7);
        var evaluation = new BehaviorEvaluation("eval", "Broker|1", Date, 1, BehaviorRuleKind.ReentryCount,
            3m, null, 2m, BehaviorRiskLevel.Attention, true, "count=3", Now);
        Assert.Equal(GoalObservationStatus.Failed,
            BehaviorGoalMeasurement.Measure(goal, evaluation, Trade, [], policy).Status);
        Assert.Equal(GoalObservationStatus.Failed,
            BehaviorGoalMeasurement.Measure(goal with { TargetValue = 50m }, evaluation, Trade, [], policy).Status);
        var observations = new[] { new GoalObservation("late", "goal", "Broker|1", Date.AddDays(8),
            1, 0, 1, GoalObservationStatus.Failed, "outside window", Now) };
        var progress = Assert.Single(new ReviewWorkspaceCalculator().BuildGoalProgress([goal], observations, Date, Date.AddDays(14)));
        Assert.Equal(0, progress.FailCount);
        Assert.Equal(7, progress.ApplicableDayCount);
        var measured = Assert.Single(new ReviewWorkspaceCalculator().BuildGoalProgress([goal],
            [new("measured", "goal", "Broker|1", Date, 2, 1, 1, GoalObservationStatus.Failed, "", Now)], Date, Date.AddDays(6)));
        Assert.False(measured.MeetsObservedTarget);
        Assert.True((measured with { Goal = goal with { TargetValue = 50m } }).MeetsObservedTarget);
        Assert.Null(progress.MeetsObservedTarget);
    }
}
