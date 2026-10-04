using TradePet.Core.Domain;
using TradePet.Core.Review;
using Xunit;

namespace TradePet.Core.Tests;

public sealed class BehaviorGoalMeasurementTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 10, 2);
    private static readonly BehaviorPolicy Policy = BehaviorPolicy.Balanced;

    [Fact]
    public void NoPriorLoss_IsNotAnApplicablePassedOpportunity()
    {
        var result = BehaviorGoalMeasurement.Measure(Goal(), Evaluation(), Trade(3, null, 0m), [], Policy);
        Assert.Equal(GoalObservationStatus.NotApplicable, result.Status);
        Assert.Equal(0, result.OpportunityCount);
        Assert.Equal(0, result.PassCount);
    }

    [Fact]
    public void EligibleCooldown_IsMeasuredAndPolicyChangeBecomesUnknown()
    {
        var losses = new[] { Trade(1, Now.AddSeconds(-20), -1m), Trade(2, Now.AddSeconds(-10), -2m) };
        var evaluation = Evaluation() with { Triggered = true, Level = BehaviorRiskLevel.Critical };
        var failed = BehaviorGoalMeasurement.Measure(Goal(), evaluation, Trade(3, null, 0m), losses, Policy);
        Assert.Equal(GoalObservationStatus.Failed, failed.Status);
        Assert.Equal(1, failed.OpportunityCount);
        Assert.Equal(1, failed.FailCount);
        var unknown = BehaviorGoalMeasurement.Measure(Goal(), evaluation, Trade(3, null, 0m), losses,
            Policy with { CooldownSeconds = Policy.CooldownSeconds + 1 });
        Assert.Equal(GoalObservationStatus.Unknown, unknown.Status);
        Assert.Equal(0, unknown.PassCount);
        Assert.Equal(0, unknown.FailCount);
        Assert.NotEqual(Goal().RuleVersion, unknown.RuleVersion);
    }

    [Fact]
    public void SizeGoal_ExcludesOtherAccountsAndDoesNotCountAnOrdinaryEntryAsPassed()
    {
        var goal = Goal() with { Rule = BehaviorRuleKind.SizeEscalationAfterLoss };
        var evaluation = Evaluation() with { Rule = BehaviorRuleKind.SizeEscalationAfterLoss };
        var result = BehaviorGoalMeasurement.Measure(goal, evaluation, Trade(3, null, 0m),
            [Trade(1, Now.AddSeconds(-10), -5m) with { AccountKey = "Other|9" }], Policy);
        Assert.Equal(GoalObservationStatus.NotApplicable, result.Status);
        Assert.Equal(0, result.PassCount);
    }

    [Fact]
    public void GoalChoices_IncludeSevenMeasurableRulesAndRespectPolicySwitches()
    {
        var rules = BehaviorGoalMeasurement.SupportedRules(Policy);
        Assert.Equal(7, rules.Count);
        Assert.Contains(BehaviorRuleKind.ReentryCount, rules);
        Assert.Contains(BehaviorRuleKind.LossZonePersistence, rules);
        Assert.Contains(BehaviorRuleKind.RevengeScore, rules);
        Assert.Contains(BehaviorRuleKind.PriceFixationScore, rules);
        Assert.DoesNotContain(BehaviorRuleKind.PlanDeviationRate, rules);
        Assert.DoesNotContain(BehaviorRuleKind.ProfitGiveback, rules);
        Assert.DoesNotContain(BehaviorRuleKind.RevengeScore, BehaviorGoalMeasurement.SupportedRules(
            Policy with { EnabledRules = Policy.EnabledRules with { RevengeScore = false } }));
        var baselineOnly = BehaviorGoalMeasurement.SupportedRules(Policy with { HardLimitEnabled = false });
        Assert.Equal(2, baselineOnly.Count);
        Assert.Contains(BehaviorRuleKind.OvertradeBurst, baselineOnly);
        Assert.Contains(BehaviorRuleKind.PriceFixationScore, baselineOnly);
        Assert.Empty(BehaviorGoalMeasurement.SupportedRules(Policy with { HardLimitEnabled = false, BaselineEnabled = false }));
    }

    [Theory]
    [InlineData(BehaviorRuleKind.ReentryCount)]
    [InlineData(BehaviorRuleKind.LossZonePersistence)]
    public void RepeatGoals_OnlyMeasureRepeatEntriesAndRecordThresholdFailures(BehaviorRuleKind rule)
    {
        var goal = Goal() with { Rule = rule };
        var evaluation = Evaluation() with { Rule = rule, Value = 0m };
        var firstEntry = BehaviorGoalMeasurement.Measure(goal, evaluation, Trade(3, null, 0m), [], Policy);
        Assert.Equal(GoalObservationStatus.NotApplicable, firstEntry.Status);
        Assert.Equal(0, firstEntry.PassCount);
        var repeatEntry = BehaviorGoalMeasurement.Measure(goal, evaluation with { Value = 1m }, Trade(3, null, 0m), [], Policy);
        Assert.Equal(GoalObservationStatus.Passed, repeatEntry.Status);
        var failed = BehaviorGoalMeasurement.Measure(goal, evaluation with { Value = 4m, Triggered = true }, Trade(3, null, 0m), [], Policy);
        Assert.Equal(GoalObservationStatus.Failed, failed.Status);
        Assert.Equal(1, failed.FailCount);
    }

    [Fact]
    public void RapidEntryAfterLossGoal_OnlyMeasuresEntriesFollowingALoss()
    {
        var goal = Goal() with { Rule = BehaviorRuleKind.RevengeScore };
        var evaluation = Evaluation() with { Rule = goal.Rule!.Value };
        var win = Trade(1, Now.AddSeconds(-10), 1m);
        var notApplicable = BehaviorGoalMeasurement.Measure(goal, evaluation, Trade(3, null, 0m), [win], Policy);
        Assert.Equal(GoalObservationStatus.NotApplicable, notApplicable.Status);
        Assert.Equal(0, notApplicable.PassCount);
        var loss = win with { NetPnl = -1m };
        var passed = BehaviorGoalMeasurement.Measure(goal, evaluation, Trade(3, null, 0m), [loss], Policy);
        Assert.Equal(GoalObservationStatus.Passed, passed.Status);
        var failed = BehaviorGoalMeasurement.Measure(goal, evaluation with { Triggered = true }, Trade(3, null, 0m), [loss], Policy);
        Assert.Equal(GoalObservationStatus.Failed, failed.Status);
    }

    [Fact]
    public void ConcentrationGoal_InsufficientSamplesAreUnknownAndFutureEntriesDoNotFillTheGap()
    {
        var goal = Goal() with { Rule = BehaviorRuleKind.PriceFixationScore };
        var evaluation = Evaluation() with { Rule = goal.Rule!.Value, Value = 40m };
        var past = Trade(1, Now.AddSeconds(-10), 1m);
        var future = Trade(2, null, 0m) with { OpenedAtUtc = Now.AddSeconds(10) };
        var unknown = BehaviorGoalMeasurement.Measure(goal, evaluation, Trade(3, null, 0m), [past, future], Policy);
        Assert.Equal(GoalObservationStatus.Unknown, unknown.Status);
        Assert.Equal(0, unknown.PassCount);
        var enough = BehaviorGoalMeasurement.Measure(goal, evaluation, Trade(3, null, 0m),
            [past, future with { OpenedAtUtc = Now.AddSeconds(-20) }], Policy);
        Assert.Equal(GoalObservationStatus.Passed, enough.Status);
    }

    private static ImprovementGoal Goal() => new("goal", "Broker|1", "冷静期", BehaviorRuleKind.CooldownViolation,
        BehaviorGoalMeasurement.PolicyVersion(Policy), Date, Date.AddDays(6), 100m, null, "已知适用开仓", "", true,
        ImprovementGoalStatus.Active, 1, Now, Now);
    private static BehaviorEvaluation Evaluation() => new("evaluation", "Broker|1", Date, 3,
        BehaviorRuleKind.CooldownViolation, 10m, null, 60m, BehaviorRiskLevel.Normal, false, "冷静期证据", Now);
    private static TradeRecord Trade(long id, DateTimeOffset? closed, decimal pnl) => new("Broker|1", id, "TEST",
        TradeSide.Buy, closed?.AddMinutes(-1) ?? Now, closed, Date, closed is null ? null : Date,
        100m, closed is null ? null : 101m, 1m, 1m, closed is null ? 1m : 0m, pnl, closed.HasValue);
}
