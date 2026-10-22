using System.IO;
using System.Reflection;
using TradePet.App.Runtime;
using TradePet.App.ViewModels;
using TradePet.Application.Runtime;
using TradePet.Core.Domain;
using TradePet.Core.Session;
using TradePet.Core.Review;
using TradePet.Infrastructure.Persistence;
using Xunit;

namespace TradePet.App.Tests;

public sealed class FunctionalRepairTests
{
    [Fact]
    public async Task VisibleBehaviorSettings_SaveWithoutRemovedPlanDeviationField()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.ViewModel.SetBehaviorSettings(BehaviorPolicy.Balanced);
        fixture.ViewModel.BehaviorSettings[0].Value = "7";
        await fixture.Runtime.SaveBehaviorSettingsAsync();
        var saved = await fixture.Database.LoadSettingAsync<BehaviorPolicySet>(
            "account:" + fixture.Account.Scope.AccountKey, "behavior-profiles-v1");
        Assert.NotNull(saved);
        Assert.Equal(7, saved.Selected.ReentryThreshold);
        Assert.False(saved.Selected.EnabledRules.PlanDeviationRate);
    }

    [Fact]
    public async Task MissingStops_AreRetriedAndEachPositionIsDelivered()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.AddPosition(101, "FIRST");
        fixture.AddPosition(102, "SECOND");
        await fixture.CallAsync("EvaluateMissingStopLossRemindersAsync", fixture.Account.CapturedAtUtc);
        Assert.Single(fixture.Get<HashSet<long>>("_missingStopLossAlerted"));
        Assert.Contains("FIRST", fixture.ViewModel.BubbleHeadline);
        fixture.Call("DismissActiveAlert");
        await fixture.CallAsync("EvaluateMissingStopLossRemindersAsync", fixture.Account.CapturedAtUtc.AddSeconds(20));
        Assert.Equal(2, fixture.Get<HashSet<long>>("_missingStopLossAlerted").Count);
        Assert.Contains("SECOND", fixture.ViewModel.BubbleHeadline);
    }

    [Fact]
    public async Task SnoozedAlert_IsDiscardedAfterAccountSwitch()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.AddPosition(103, "OLD-ACCOUNT");
        await fixture.CallAsync("EvaluateMissingStopLossRemindersAsync", fixture.Account.CapturedAtUtc);
        fixture.Call("SnoozeAlert");
        await fixture.Scheduler.Scheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var next = fixture.Account with { Scope = new AccountScope("Other", 2) };
        await fixture.Sessions.SwitchAsync(next.Scope.AccountKey);
        fixture.Set("_account", next);
        fixture.Get<Dictionary<long, PositionSnapshot>>("_positions").Clear();
        fixture.Scheduler.FiveMinutes.TrySetResult();
        await fixture.Get<Task>("_snoozedAlertTask").WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.CallAsync("DrainDeferredAlertsAsync");
        Assert.False(fixture.ViewModel.IsBubbleVisible);
    }

    [Fact]
    public async Task GoalNotificationSwitch_ControlsTheGoalReminder()
    {
        await using var fixture = await Fixture.CreateAsync();
        var now = fixture.Account.CapturedAtUtc; var date = DateOnly.FromDateTime(now.UtcDateTime);
        var policy = BehaviorPolicy.Balanced;
        var goal = new ImprovementGoal("goal", fixture.Account.Scope.AccountKey, "GOAL-CAP", BehaviorRuleKind.ReentryCount,
            BehaviorGoalMeasurement.PolicyVersion(policy), date, date.AddDays(6), 100m, null, "adherence", "", false,
            ImprovementGoalStatus.Active, 1, now.AddMinutes(-1), now, ObservationWindowDays: 7);
        var trade = new TradeRecord(fixture.Account.Scope.AccountKey, 5, "TEST", TradeSide.Buy, now, null,
            date, null, 100m, null, 1m, 1m, 1m, 0m, false);
        var evaluation = new BehaviorEvaluation("eval", goal.AccountKey, date, 5, BehaviorRuleKind.ReentryCount,
            3m, null, 2m, BehaviorRiskLevel.Attention, true, "count=3", now);
        var observation = BehaviorGoalMeasurement.Measure(goal, evaluation, trade, [], policy);
        await fixture.CallAsync("NotifyGoalObservationAsync", goal, observation, evaluation, trade, now);
        Assert.False(fixture.ViewModel.IsBubbleVisible);
        await fixture.CallAsync("NotifyGoalObservationAsync", goal with { NotificationEnabled = true }, observation, evaluation, trade, now);
        Assert.True(fixture.ViewModel.IsBubbleVisible);
        Assert.Contains("GOAL-CAP", fixture.ViewModel.BubbleHeadline);
    }

    [Fact]
    public async Task AutomaticBackups_RunOnceDailyRespectDisableAndKeepSevenCopies()
    {
        await using var fixture = await Fixture.CreateAsync();
        var directory = fixture.Get<string>("_automaticBackupDirectory");
        fixture.ViewModel.AutomaticBackupEnabled = false;
        await fixture.CallAsync("CreateAutomaticBackupIfDueAsync", CancellationToken.None);
        Assert.False(Directory.Exists(directory));
        fixture.ViewModel.AutomaticBackupEnabled = true;
        await fixture.CallAsync("CreateAutomaticBackupIfDueAsync", CancellationToken.None);
        await fixture.CallAsync("CreateAutomaticBackupIfDueAsync", CancellationToken.None);
        Assert.Single(Directory.GetFiles(directory, "*.zip"));
        for (var i = 0; i < 8; i++)
        {
            await fixture.Database.SaveSettingAsync("global", "automatic-backup-day-v1", "previous-day");
            await fixture.CallAsync("CreateAutomaticBackupIfDueAsync", CancellationToken.None);
        }
        Assert.Equal(7, Directory.GetFiles(directory, "*.zip").Length);
    }

    [Fact]
    public async Task OfflineAccount_CanQueryAndReplaySavedFactsWithoutTerminal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var now = fixture.Account.CapturedAtUtc; var date = DateOnly.FromDateTime(now.UtcDateTime);
        var trade = new TradeRecord(fixture.Account.Scope.AccountKey, 301, "TEST", TradeSide.Buy,
            now.AddMinutes(-5), now, date, date, 100m, 105m, 1m, 1m, 0m, 5m, true);
        await fixture.Database.UpsertTradeAsync(trade);
        fixture.Set("_offlineReviewOnly", true);
        fixture.Get<WorkerSession>("_workerSession").Disconnect();
        fixture.ViewModel.SelectedReviewPeriod = "自定义";
        var account = Assert.Single(await fixture.Database.LoadLocalAccountsAsync());
        await fixture.CallAsync("SelectOfflineAccountAsync", account);
        Assert.Equal(1, fixture.ViewModel.ReviewWorkspace.TotalCount);
        await fixture.ViewModel.ReviewWorkspace.OpenTradeAsync!(301);
        await fixture.ViewModel.ReviewWorkspace.LoadReplayAsync!();
        Assert.NotNull(fixture.ViewModel.ReviewWorkspace.ReplayChartFrame);
        Assert.False(fixture.Get<WorkerSession>("_workerSession").IsConnected);
    }

    [Fact]
    public async Task ChartObjects_ComposeAPlanDraftWithoutPersistingOrChangingHistoricalPlans()
    {
        await using var fixture = await Fixture.CreateAsync();
        var now = fixture.Account.CapturedAtUtc; var date = DateOnly.FromDateTime(now.UtcDateTime);
        var items = fixture.Get<Dictionary<string, PlanItem>>("_planItems");
        PlanItem Item(string key, PlanCategory category, decimal low, decimal high) =>
            new(key, fixture.Account.Scope.AccountKey, date, key, category, "TEST", low, high, key, true, now);
        items["entry"] = Item("entry", PlanCategory.LongWatchZone, 99m, 101m);
        items["stop"] = Item("stop", PlanCategory.StopReference, 98m, 98m);
        items["target"] = Item("target", PlanCategory.TargetReference, 105m, 105m);
        await fixture.CallAsync("ComposeChartPlanCoreAsync");
        Assert.Equal("TEST", fixture.ViewModel.NewPlanSymbol);
        Assert.Equal(TradeSide.Buy, fixture.ViewModel.NewPlanSide);
        Assert.Equal("98", fixture.ViewModel.NewPlanStop);
        Assert.Empty(await fixture.Database.LoadStructuredTradePlansAsync(fixture.Account.Scope.AccountKey, date, date));
        await fixture.Runtime.CreateStructuredPlanAsync();
        Assert.Single(await fixture.Database.LoadStructuredTradePlansAsync(fixture.Account.Scope.AccountKey, date, date));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        public required AppDatabase Database { get; init; }
        public required MainViewModel ViewModel { get; init; }
        public required TradePetRuntime Runtime { get; init; }
        public required AccountSnapshot Account { get; init; }
        public required AccountSessionCoordinator Sessions { get; init; }
        public required Scheduler Scheduler { get; init; }
        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"tradepet-functional-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var databasePath = Path.Combine(root, "tradepet.db");
            var attachments = Path.Combine(root, "attachments");
            var database = new AppDatabase(databasePath);
            await database.InitializeAsync();
            var scheduler = new Scheduler();
            var sessions = new AccountSessionCoordinator();
            var viewModel = new MainViewModel(scheduler);
            var runtime = new TradePetRuntime(viewModel, new(database, database, new ReviewPackageWriter(),
                new ReviewAttachmentStore(database, attachments), new ReviewBackupService(database, attachments, databasePath), TimeProvider.System,
                scheduler, sessions, new MaintenanceCoordinator(), Path.Combine(root, "automatic")));
            var account = new AccountSnapshot(new("Functional", 1), "USD", 10000m, 9990m, -10m, 2, DateTimeOffset.UtcNow);
            await database.UpsertAccountAsync(account);
            await sessions.SwitchAsync(account.Scope.AccountKey);
            var fixture = new Fixture { Database = database, ViewModel = viewModel, Runtime = runtime,
                Account = account, Sessions = sessions, Scheduler = scheduler };
            fixture.Set("_account", account);
            fixture.Set("_behaviorPolicies", BehaviorPolicySet.CreateDefault(account.Scope.AccountKey));
            fixture.Set("_settings", DailyPlanSettings.BalancedDefault with { StopLossReminderEnabled = true });
            fixture.Get<ServerClock>("_serverClock").ApplyWorkerOffset(0);
            fixture.Get<WorkerSession>("_workerSession").Connect(account.Scope.AccountKey);
            fixture.Get<WorkerSession>("_workerSession").MarkSnapshotReceived(false);
            return fixture;
        }
        public T Get<T>(string name) => (T)typeof(TradePetRuntime).GetField(name, Flags)!.GetValue(Runtime)!;
        public void Set(string name, object value) => typeof(TradePetRuntime).GetField(name, Flags)!.SetValue(Runtime, value);
        public object? Call(string name, params object[] args) => typeof(TradePetRuntime).GetMethod(name, Flags)!.Invoke(Runtime, args);
        public Task CallAsync(string name, params object[] args) => (Task)Call(name, args)!;
        public void AddPosition(long id, string symbol) => Get<Dictionary<long, PositionSnapshot>>("_positions")[id] =
            new(id, id, symbol, TradeSide.Buy, 0.1m, 1m, 1m, -5m, 0m, 0m,
                Account.CapturedAtUtc.AddMinutes(-5), Account.CapturedAtUtc);
        public ValueTask DisposeAsync() => Runtime.DisposeAsync();
    }

    private sealed class Scheduler : IAsyncScheduler
    {
        public TaskCompletionSource Scheduled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FiveMinutes { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            if (delay != TimeSpan.FromMinutes(5)) { await Task.Delay(Timeout.Infinite, cancellationToken); return; }
            Scheduled.TrySetResult();
            await FiveMinutes.Task.WaitAsync(cancellationToken);
        }
    }
}
