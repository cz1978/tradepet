using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using TradePet.Application.Review;
using TradePet.Application.Runtime;
using TradePet.App.Runtime;
using TradePet.App.ViewModels.Review;
using TradePet.Core.Domain;
using TradePet.Core.Session;
using TradePet.Core.Trading;
using TradePet.Infrastructure.Persistence;
using Xunit;

namespace TradePet.App.Tests;

public sealed class RuntimeContractTests
{
    [Fact]
    public void ReviewQuery_ResetsPageForChangedFiltersButRetainsExplicitNavigation()
    {
        var main = new TradePet.App.ViewModels.MainViewModel();
        var review = main.ReviewWorkspace;
        var queriedPages = new List<int>();
        review.RefreshAsync = main.RefreshReviewAsync = () =>
        {
            queriedPages.Add(review.Page);
            return Task.CompletedTask;
        };
        review.Page = 8;
        review.RefreshCommand.Execute(null);
        review.NextPageCommand.Execute(null);
        review.PreviousPageCommand.Execute(null);
        review.Page = 5;
        main.RefreshReviewCommand.Execute(null);
        Assert.Equal(new[] { 1, 2, 1, 1 }, queriedPages);
        main.SelectedReviewPeriod = "自定义";
        main.ReviewFromDateText = "2026-02-30";
        main.ReviewToDateText = "2026-09-30";
        main.RefreshReviewCommand.Execute(null);
        Assert.Equal(4, queriedPages.Count);
        Assert.Contains("有效日期", review.StatusText);
        main.ReviewFromDateText = "2026-09-01";
        main.RefreshReviewCommand.Execute(null);
        Assert.Equal(5, queriedPages.Count);
    }

    [Fact]
    public async Task StructuredAndChartPlans_SaveMatchClassifyMoveAndRetainDeletedHistory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tradepet-plans-{Guid.NewGuid():N}.db");
        var database = new AppDatabase(path);
        await database.InitializeAsync();
        var repository = DispatchProxy.Create<IReviewWorkspaceRepository, ThrowingProxy>();
        var dependencies = new TradePetRuntimeDependencies(database, repository,
            DispatchProxy.Create<IReviewPackageWriter, ThrowingProxy>(),
            DispatchProxy.Create<IReviewAttachmentStore, ThrowingProxy>(),
            DispatchProxy.Create<IReviewBackupService, ThrowingProxy>(),
            TimeProvider.System, new ManualAsyncScheduler(),
            new AccountSessionCoordinator(), new MaintenanceCoordinator());
        var viewModel = new TradePet.App.ViewModels.MainViewModel
        {
            NewPlanSymbol = "XAUUSD.s",
            NewPlanSide = TradeSide.Buy,
            NewPlanEntryLow = "99",
            NewPlanEntryHigh = "101",
            NewPlanStop = "98",
            NewPlanTarget = "105",
            NewPlanStrategy = "突破",
            NewPlanTags = "测试,区间",
        };
        await using var runtime = new TradePetRuntime(viewModel, dependencies);
        var date = DateOnly.FromDateTime(DateTime.Now);
        var now = DateTimeOffset.UtcNow;
        var account = new AccountSnapshot(new AccountScope("PlanBroker", 73), "USD", 1_000m,
            1_000m, 0m, 2, now);
        await database.UpsertAccountAsync(account);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(TradePetRuntime).GetField("_account", flags)!.SetValue(runtime, account);
        async Task Call(string method, params object[] args) =>
            await (Task)typeof(TradePetRuntime).GetMethod(method, flags)!.Invoke(runtime, args)!;

        await runtime.CreateStructuredPlanAsync();
        Assert.Contains("参考入场", viewModel.StructuredPlanStatusText);
        Assert.Empty(await database.LoadStructuredTradePlansAsync(account.Scope.AccountKey, date, date));

        viewModel.NewPlanReferenceEntry = "100";
        await runtime.CreateStructuredPlanAsync();
        var plan = Assert.Single(await database.LoadStructuredTradePlansAsync(account.Scope.AccountKey, date, date));
        Assert.Equal(2.5m, plan.PlannedRiskMultiple);
        Assert.Contains("测试", plan.Tags);
        Assert.Contains("区间", plan.Tags);
        var trade = new TradeRecord(account.Scope.AccountKey, 77, "XAUUSD.s", TradeSide.Buy,
            plan.CreatedAtUtc.AddSeconds(1), null, date, null, 100m, null, 0.01m, 0.01m,
            0.01m, 0m, false);
        Assert.Equal(PlanComplianceStatus.Matched,
            new TradePlanMatcher().CreateAutomaticMetadata(trade, [plan], now).ComplianceStatus);

        await Call("ToggleStructuredPlanCoreAsync", plan.Id);
        Assert.False(Assert.Single(await database.LoadStructuredTradePlansAsync(account.Scope.AccountKey, date, date)).IsActive);
        await Call("ToggleStructuredPlanCoreAsync", plan.Id);
        var versions = await database.LoadStructuredTradePlansAsync(account.Scope.AccountKey, date, date);
        Assert.Equal(2, versions.Count);
        Assert.Single(versions, item => item.IsActive);

        var chart = new ChartObjectSnapshot("test-terminal", 7, "avoid-zone", "XAUUSD.s", "M5",
            ChartObjectKind.Rectangle, [new(null, 99m), new(null, 101m)], "避开", 0, now);
        var objects = (Dictionary<string, ChartObjectSnapshot>)typeof(TradePetRuntime)
            .GetField("_chartObjects", flags)!.GetValue(runtime)!;
        await runtime.ImportCurrentChartAsync();
        Assert.Contains("还没收到当前图表", viewModel.PlanChartStatusText);
        typeof(TradePetRuntime).GetField("_hostChartId", flags)!.SetValue(runtime, 7L);
        await runtime.ImportCurrentChartAsync();
        Assert.Contains("没有可导入对象", viewModel.PlanChartStatusText);
        objects[chart.ObjectKey] = chart;
        await runtime.ImportCurrentChartAsync();
        var item = Assert.Single(await database.LoadPlanItemsAsync(account.Scope.AccountKey, date));
        Assert.Equal(99m, item.PriceLow);
        Assert.Equal(101m, item.PriceHigh);
        await Call("SavePlanItemCoreAsync", item with { Category = PlanCategory.NoTradeZone });
        item = Assert.Single(await database.LoadPlanItemsAsync(account.Scope.AccountKey, date));
        Assert.Equal(PlanCategory.NoTradeZone, item.Category);
        Assert.Contains(new LossZoneEngine().EvaluateOpen(trade, date, 1m, [], [], [item], null,
            DailyPlanSettings.BalancedDefault).AllFacts, fact => fact.Kind == RuleFactKind.NoTradePlan);

        await Call("HandleChartObjectAsync", chart with
        {
            Anchors = [new(null, 98m), new(null, 102m)],
            CapturedAtUtc = now.AddSeconds(2),
        });
        item = Assert.Single(await database.LoadPlanItemsAsync(account.Scope.AccountKey, date));
        Assert.Equal(98m, item.PriceLow);
        Assert.Equal(102m, item.PriceHigh);
        await Call("HandleChartObjectAsync", chart with { IsDeleted = true, CapturedAtUtc = now.AddSeconds(3) });
        item = Assert.Single(await database.LoadPlanItemsAsync(account.Scope.AccountKey, date));
        Assert.False(item.IsActive);
    }

    [Fact]
    public async Task DailyBoundarySave_PersistsCurrentAccountDayIndependentlyOfReportTime()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tradepet-boundary-{Guid.NewGuid():N}.db");
        var database = new AppDatabase(path);
        await database.InitializeAsync();
        var repository = DispatchProxy.Create<IReviewWorkspaceRepository, ThrowingProxy>();
        var dependencies = new TradePetRuntimeDependencies(database, repository,
            DispatchProxy.Create<IReviewPackageWriter, ThrowingProxy>(),
            DispatchProxy.Create<IReviewAttachmentStore, ThrowingProxy>(),
            DispatchProxy.Create<IReviewBackupService, ThrowingProxy>(),
            TimeProvider.System, new ManualAsyncScheduler(),
            new AccountSessionCoordinator(), new MaintenanceCoordinator());
        var viewModel = new TradePet.App.ViewModels.MainViewModel
        {
            DailyTarget = 100m,
            DailyLoss = 50m,
            MaximumTrades = 3,
            MaximumLot = 0.05m,
            StopLossReminderEnabled = true,
            StopLossReminderSeconds = 45,
            LossZoneTolerance = 150m,
            DailyReportTimeText = "not a time",
        };
        await using var runtime = new TradePetRuntime(viewModel, dependencies);
        var date = DateOnly.FromDateTime(DateTime.Now);
        var capturedAt = DateTimeOffset.UtcNow;
        var account = new AccountSnapshot(new AccountScope("BoundaryBroker", 72), "USD", 1_000m,
            1_000m, 0m, 2, capturedAt);
        await database.UpsertAccountAsync(account);
        var state = new DailyState(account.Scope.AccountKey, date, 0m, 0m, 0m, 0m,
            0, 0, 0, 0, 0m, false, false, false);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(TradePetRuntime).GetField("_account", flags)!.SetValue(runtime, account);
        typeof(TradePetRuntime).GetField("_dailyState", flags)!.SetValue(runtime, state);
        var positions = (Dictionary<long, PositionSnapshot>)typeof(TradePetRuntime)
            .GetField("_positions", flags)!.GetValue(runtime)!;
        positions[42] = new PositionSnapshot(42, 42, "EURUSD", TradeSide.Buy, 0.01m,
            1.10m, 1.10m, 0m, 0m, 0m, capturedAt.AddMinutes(-2), capturedAt);
        var workerSession = (WorkerSession)typeof(TradePetRuntime)
            .GetField("_workerSession", flags)!.GetValue(runtime)!;
        workerSession.Connect(account.Scope.AccountKey);
        workerSession.MarkSnapshotReceived(requiresDealHistory: false);

        await runtime.SaveDailyPlanAsync();

        var saved = await database.LoadTradingDayAsync(account.Scope.AccountKey, date);
        Assert.True(saved.HasValue, viewModel.DiagnosticText);
        Assert.Equal(100m, saved.Value.Settings.DailyTarget);
        Assert.Equal(50m, saved.Value.Settings.DailyLoss);
        Assert.Equal(3, saved.Value.Settings.MaximumTrades);
        Assert.Equal(0.05m, saved.Value.Settings.MaximumLot);
        Assert.True(saved.Value.Settings.StopLossReminderEnabled);
        Assert.Equal(45, saved.Value.Settings.StopLossReminderSeconds);
        Assert.Contains("没有止损", viewModel.BubbleHeadline);
        var calculator = new DailyStateCalculator();
        var target = calculator.Calculate(account.Scope.AccountKey, date, [], [], saved.Value.Settings,
            null, realizedPnlOverride: 100m);
        var loss = calculator.Calculate(account.Scope.AccountKey, date, [], [], saved.Value.Settings,
            null, realizedPnlOverride: -50m);
        Assert.Contains(target.NewFacts, fact => fact.Kind == RuleFactKind.DailyTarget);
        Assert.Contains(loss.NewFacts, fact => fact.Kind == RuleFactKind.DailyLoss);
        var desktop = await database.LoadSettingAsync<System.Text.Json.JsonElement>("global", "desktop");
        Assert.Equal(150m, desktop.GetProperty("lossZoneTolerance").GetDecimal());
    }

    [Fact]
    public async Task ConsoleGuideCompletion_SurvivesRuntimeRestartWithoutSavingOtherSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tradepet-guide-{Guid.NewGuid():N}.db");
        var database = new AppDatabase(path);
        await database.InitializeAsync();
        var repository = DispatchProxy.Create<IReviewWorkspaceRepository, ThrowingProxy>();
        var dependencies = new TradePetRuntimeDependencies(database, repository,
            DispatchProxy.Create<IReviewPackageWriter, ThrowingProxy>(),
            DispatchProxy.Create<IReviewAttachmentStore, ThrowingProxy>(),
            DispatchProxy.Create<IReviewBackupService, ThrowingProxy>(),
            TimeProvider.System, new ManualAsyncScheduler(),
            new AccountSessionCoordinator(), new MaintenanceCoordinator());
        await using (var runtime = new TradePetRuntime(new TradePet.App.ViewModels.MainViewModel(), dependencies))
            await runtime.CompleteConsoleGuideAsync();

        var reopenedDatabase = new AppDatabase(path);
        var reopenedDependencies = dependencies with { Database = reopenedDatabase };
        var viewModel = new TradePet.App.ViewModels.MainViewModel();
        await using var reopenedRuntime = new TradePetRuntime(viewModel, reopenedDependencies);
        await (Task)typeof(TradePetRuntime).GetMethod("LoadDesktopSettingsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reopenedRuntime, null)!;

        Assert.True(viewModel.ConsoleGuideCompleted);
        Assert.Null(await reopenedDatabase.LoadSettingAsync<object>("global", "desktop"));
    }

    [Fact]
    public async Task QuickReview_TradeClosuresAndSnoozeOnlyQueueWithoutOpeningWindows()
    {
        var scheduler = new ManualAsyncScheduler();
        var repository = DispatchProxy.Create<IReviewWorkspaceRepository, ThrowingProxy>();
        var database = new AppDatabase(Path.Combine(Path.GetTempPath(), $"tradepet-quick-review-{Guid.NewGuid():N}.db"));
        var dependencies = new TradePetRuntimeDependencies(database, repository,
            DispatchProxy.Create<IReviewPackageWriter, ThrowingProxy>(),
            DispatchProxy.Create<IReviewAttachmentStore, ThrowingProxy>(),
            DispatchProxy.Create<IReviewBackupService, ThrowingProxy>(),
            TimeProvider.System, scheduler, new AccountSessionCoordinator(), new MaintenanceCoordinator());
        await using var runtime = new TradePetRuntime(
            new TradePet.App.ViewModels.MainViewModel(scheduler), dependencies);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var queue = typeof(TradePetRuntime).GetMethod("QueueQuickReview", flags)!;
        var pending = (ConcurrentDictionary<string, TradeRecord>)typeof(TradePetRuntime)
            .GetField("_pendingQuickReviews", flags)!.GetValue(runtime)!;
        var trade = Detail("account-a", 42).Trade;

        queue.Invoke(runtime, [trade]);
        queue.Invoke(runtime, [trade]);
        queue.Invoke(runtime, [Detail("account-a", 43).Trade]);
        queue.Invoke(runtime, [Detail("account-b", 42).Trade]);
        Assert.Equal(3, pending.Count);

        pending.Clear();
        var reminder = (Task)typeof(TradePetRuntime).GetMethod("RemindQuickReviewLaterAsync", flags)!
            .Invoke(runtime, [trade])!;
        var delay = await scheduler.NextAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromMinutes(10), delay.Delay);
        Assert.Empty(pending);
        delay.Release();
        await reminder.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(trade, Assert.Single(pending).Value);
        Assert.Null(typeof(TradePetRuntime).GetField("_quickReviewWindow", flags)!.GetValue(runtime));
    }

    [Fact]
    public void ExportPreview_DefaultsToAllFilteredNoAttachmentsAndInvalidatesOnScopeOrAccountChange()
    {
        var viewModel = new ReviewWorkspaceViewModel(new ManualAsyncScheduler());
        Assert.Equal("全部筛选结果", viewModel.ExportScope);
        Assert.False(viewModel.CanConfirmExport);
        var option = new ReviewExportAttachmentOption("asset-1", "证据", "screen.png", 4);
        viewModel.ExportAttachmentOptions.Add(option);
        Assert.Empty(viewModel.SelectedExportAttachmentIds);
        option.IsIncluded = true;
        Assert.Equal(["asset-1"], viewModel.SelectedExportAttachmentIds);
        viewModel.ExportPreview = "已冻结预览";
        viewModel.CanConfirmExport = true;

        viewModel.ExportScope = "当前页选中交易";

        Assert.False(viewModel.CanConfirmExport);
        Assert.Empty(viewModel.ExportAttachmentOptions);
        Assert.Contains("默认不包含", viewModel.ExportPreview);

        viewModel.CanConfirmExport = true;
        viewModel.ResetAccountState("账户切换");
        Assert.False(viewModel.CanConfirmExport);
    }

    [Fact]
    public async Task ReviewAutoSave_WaitsForInjectedScheduler()
    {
        var scheduler = new ManualAsyncScheduler();
        var viewModel = new ReviewWorkspaceViewModel(scheduler)
        {
            SelectedPositionId = 42,
        };
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCount = 0;
        viewModel.AutoSaveReviewAsync = () =>
        {
            Interlocked.Increment(ref saveCount);
            saved.TrySetResult();
            return Task.CompletedTask;
        };

        viewModel.EntryReason = "等待可控调度";
        var scheduled = await scheduler.NextAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromMilliseconds(900), scheduled.Delay);
        Assert.Equal(0, Volatile.Read(ref saveCount));

        scheduled.Release();
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, Volatile.Read(ref saveCount));
    }

    [Fact]
    public async Task AccountSwitch_CancelsOldSessionBeforePublishingNextGeneration()
    {
        using var sessions = new AccountSessionCoordinator();
        var first = await sessions.SwitchAsync("account-a");
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = first.CancellationToken.Register(() => cancellationObserved.TrySetResult());

        var second = await sessions.SwitchAsync("account-b");

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(first.CancellationToken.IsCancellationRequested);
        Assert.Equal(first.Generation + 1, second.Generation);
        Assert.True(sessions.IsCurrent("account-b", second.Generation));
        Assert.False(sessions.IsCurrent("account-a", first.Generation));
    }

    [Fact]
    public void SaveReceipt_RejectsOtherAccountAndOlderContent()
    {
        var editorId = Guid.NewGuid();
        var identityA = EditIdentity.ForTrade(new TradeKey("account-a", 42), 7, editorId);
        var firstSnapshot = new EditSnapshot<string>(identityA, 1, "first");
        var firstReceipt = EditSaveReceipt<string>.Saved(firstSnapshot, "stored-first");
        var identityB = EditIdentity.ForTrade(new TradeKey("account-b", 42), 8, Guid.NewGuid());

        Assert.True(firstReceipt.CanAcknowledge(identityA, 1));
        Assert.False(firstReceipt.CanAcknowledge(identityA, 2));
        Assert.False(firstReceipt.CanAcknowledge(identityB, 1));
    }

    [Fact]
    public void AccountEditors_KeepDraftOnlyWithItsTradeKeyAndRejectLateReceipt()
    {
        var viewModel = new ReviewWorkspaceViewModel(new ManualAsyncScheduler());
        var detailA = Detail("imported:account-a", 42);
        viewModel.ApplyDetail(detailA, sessionGeneration: 1);
        viewModel.EntryReason = "A 账户未提交草稿";
        var submissionA = Assert.IsType<TradeReviewEditSubmission>(
            viewModel.CaptureTradeReviewEdit(new TradeReviewBasis("source-a", "rule-a")));

        viewModel.ResetAccountState("当前连接模式不支持账户复盘");
        Assert.Null(viewModel.TradeEditorKey);
        Assert.Null(viewModel.SelectedPositionId);
        Assert.Empty(viewModel.EntryReason);
        Assert.Empty(viewModel.Trades);

        var detailB = Detail("LiveBroker|1001", 42);
        viewModel.ApplyDetail(detailB, sessionGeneration: 2);

        Assert.Equal(string.Empty, viewModel.EntryReason);
        Assert.False(viewModel.HasUnsavedReviewChanges);
        Assert.Equal(new TradeKey("LiveBroker|1001", 42), viewModel.TradeEditorKey);

        var storedA = ReviewDocument(
            submissionA.Snapshot.Content.TradeKey,
            revision: 1,
            "A 已保存版本",
            entryReason: "A 账户未提交草稿");
        var lateReceipt = new EditSaveReceipt<TradeReviewDocument>(
            submissionA.Snapshot.Identity,
            submissionA.Snapshot.ContentSequence,
            EditSaveStatus.Saved,
            storedA,
            string.Empty);
        Assert.False(viewModel.ApplyTradeReviewSaveReceipt(lateReceipt));
        Assert.Equal(string.Empty, viewModel.EntryReason);
        Assert.Equal(new TradeKey("LiveBroker|1001", 42), viewModel.TradeEditorKey);

        viewModel.ResetAccountState("switching");
        viewModel.ApplyDetail(Detail("imported:account-a", 42, storedA), sessionGeneration: 3);
        Assert.Equal("A 账户未提交草稿", viewModel.EntryReason);
        Assert.False(viewModel.HasUnsavedReviewChanges);
        Assert.Equal(new TradeKey("imported:account-a", 42), viewModel.TradeEditorKey);
    }

    [Fact]
    public void SaveReceipt_DoesNotClearEditsMadeWhileSaveWasAwaiting()
    {
        var viewModel = new ReviewWorkspaceViewModel(new ManualAsyncScheduler());
        viewModel.ApplyDetail(Detail("account-a", 9), sessionGeneration: 4);
        viewModel.Summary = "提交中的版本";
        var submitted = Assert.IsType<TradeReviewEditSubmission>(
            viewModel.CaptureTradeReviewEdit(new TradeReviewBasis("source", "rule")));
        viewModel.Summary = "提交后继续输入的新版本";
        var receipt = new EditSaveReceipt<TradeReviewDocument>(
            submitted.Snapshot.Identity,
            submitted.Snapshot.ContentSequence,
            EditSaveStatus.Saved,
            ReviewDocument(submitted.Snapshot.Content.TradeKey, 1, "提交中的版本"),
            string.Empty);

        Assert.False(viewModel.ApplyTradeReviewSaveReceipt(receipt));
        Assert.True(viewModel.HasUnsavedReviewChanges);
        Assert.Equal("提交后继续输入的新版本", viewModel.Summary);
        viewModel.ResetAccountState("cleanup", preserveTradeDraft: false);
    }

    [Fact]
    public void DailyEditor_RefreshAndOldReceiptPreserveNewestDraft()
    {
        var viewModel = new ReviewWorkspaceViewModel(new ManualAsyncScheduler());
        var date = new DateOnly(2026, 9, 10);
        viewModel.ApplyDetail(Detail("account-a", 11), sessionGeneration: 1);
        viewModel.ApplyDaily(date, null, null, "source-1");
        viewModel.PreMarketPlan = "提交中的计划";
        var submittedJournal = DailyJournal("account-a", date, "提交中的计划", revision: 0);
        var submitted = Assert.IsType<EditSnapshot<DailyJournal>>(
            viewModel.CaptureWorkspaceEdit(EditEntityKind.DailyJournal, submittedJournal));

        viewModel.PreMarketPlan = "等待期间继续输入的计划";
        var receipt = new EditSaveReceipt<DailyJournal>(
            submitted.Identity,
            submitted.ContentSequence,
            EditSaveStatus.Saved,
            submittedJournal with { Revision = 1 },
            string.Empty);

        Assert.False(viewModel.ApplyWorkspaceSaveReceipt(EditEntityKind.DailyJournal, receipt));
        Assert.True(viewModel.IsEditorDirty(EditEntityKind.DailyJournal));

        viewModel.ApplyDaily(
            date,
            DailyJournal("account-a", date, "服务器中的旧计划", revision: 1),
            null,
            "source-1");
        Assert.Equal("等待期间继续输入的计划", viewModel.PreMarketPlan);

        var retry = Assert.IsType<EditSnapshot<DailyJournal>>(
            viewModel.CaptureWorkspaceEdit(
                EditEntityKind.DailyJournal,
                DailyJournal("account-a", date, viewModel.PreMarketPlan, revision: 1)));
        var conflict = new EditSaveReceipt<DailyJournal>(
            retry.Identity,
            retry.ContentSequence,
            EditSaveStatus.Conflict,
            null,
            "服务器修订已变化");
        Assert.False(viewModel.ApplyWorkspaceSaveReceipt(EditEntityKind.DailyJournal, conflict));
        Assert.True(viewModel.HasUnsavedWorkspaceChanges);
        Assert.Contains("服务器修订已变化", viewModel.EditorSaveStatus);
        Assert.Equal("等待期间继续输入的计划",
            Assert.Single(viewModel.ExportPendingDrafts(), item => item.Kind == EditEntityKind.DailyJournal)
                .Fields[nameof(ReviewWorkspaceViewModel.PreMarketPlan)]);
        viewModel.ResetAccountState("cleanup", preserveTradeDraft: false);
    }

    [Fact]
    public void GoalDrafts_AreIsolatedAcrossAccountsAndRestoredByEntityKind()
    {
        var viewModel = new ReviewWorkspaceViewModel(new ManualAsyncScheduler());
        viewModel.ApplyDetail(Detail("account-a", 1), sessionGeneration: 1);
        viewModel.NewGoalCommand.Execute(null);
        viewModel.GoalName = "A 的纪律目标";
        viewModel.GoalMeasurement = "每次机会都检查";

        viewModel.ResetAccountState("switch");
        viewModel.ApplyDetail(Detail("account-b", 1), sessionGeneration: 2);
        viewModel.NewGoalCommand.Execute(null);
        Assert.Empty(viewModel.GoalName);
        Assert.False(viewModel.IsEditorDirty(EditEntityKind.ImprovementGoal));

        viewModel.ResetAccountState("switch");
        viewModel.ApplyDetail(Detail("account-a", 1), sessionGeneration: 3);
        viewModel.NewGoalCommand.Execute(null);
        Assert.Equal("A 的纪律目标", viewModel.GoalName);
        Assert.Equal("每次机会都检查", viewModel.GoalMeasurement);
        Assert.True(viewModel.IsEditorDirty(EditEntityKind.ImprovementGoal));
        viewModel.ResetAccountState("cleanup", preserveTradeDraft: false);
    }

    [Fact]
    public async Task WorkspaceEditor_AutoSavesAfterInjectedDelay()
    {
        var scheduler = new ManualAsyncScheduler();
        var viewModel = new ReviewWorkspaceViewModel(scheduler);
        viewModel.ApplyDetail(Detail("account-a", 1), sessionGeneration: 1);
        viewModel.ApplyDaily(new DateOnly(2026, 9, 10), null, null, "source-1");
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.AutoSaveWorkspaceAsync = () =>
        {
            saved.TrySetResult();
            return Task.CompletedTask;
        };

        viewModel.DailyNextAction = "明天只检查一个动作";
        var delay = await scheduler.NextAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromMilliseconds(900), delay.Delay);
        Assert.False(saved.Task.IsCompleted);
        delay.Release();
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.ResetAccountState("cleanup", preserveTradeDraft: false);
    }

    [Fact]
    public void ShutdownEditGuard_FreezesInputAndExportsOnlyPendingDrafts()
    {
        var viewModel = new ReviewWorkspaceViewModel(new ManualAsyncScheduler());
        var date = new DateOnly(2026, 9, 10);
        viewModel.ApplyDetail(Detail("account-a", 1), sessionGeneration: 1);
        viewModel.ApplyDaily(date, null, null, "source-1");
        viewModel.DailyNextAction = "退出前内容";

        viewModel.BeginShutdownEdits();
        viewModel.DailyNextAction = "退出后不应接受";

        Assert.Equal("退出前内容", viewModel.DailyNextAction);
        var draft = Assert.Single(viewModel.ExportPendingDrafts(), item => item.Kind == EditEntityKind.DailyJournal);
        Assert.Equal("退出前内容", draft.Fields[nameof(ReviewWorkspaceViewModel.DailyNextAction)]);

        viewModel.ResumeEdits();
        viewModel.DailyNextAction = "返回程序后可继续";
        Assert.Equal("返回程序后可继续", viewModel.DailyNextAction);
        viewModel.ResetAccountState("cleanup", preserveTradeDraft: false);
    }

    [Fact]
    public void DetailRequestContext_AppliesOnlyLatestMatchingAccountGenerationAndTrade()
    {
        var keyA = new TradeKey("account-a", 42);
        var keyB = new TradeKey("account-b", 42);
        var firstA = new TradeDetailRequestContext("request-a1", 1, keyA);
        var repeatedA = new TradeDetailRequestContext("request-a-repeat", 1, keyA);
        var requestB = new TradeDetailRequestContext("request-b", 2, keyB);
        var latestA = new TradeDetailRequestContext("request-a2", 3, keyA);

        Assert.False(firstA.CanApply(latestA.RequestId, "account-a", 3, keyA));
        Assert.False(firstA.CanApply(repeatedA.RequestId, "account-a", 1, keyA));
        Assert.True(repeatedA.CanApply(repeatedA.RequestId, "account-a", 1, keyA));
        Assert.False(requestB.CanApply(latestA.RequestId, "account-a", 3, keyB));
        Assert.False(latestA.CanApply(latestA.RequestId, "account-a", 3, keyB));
        Assert.True(latestA.CanApply(latestA.RequestId, "account-a", 3, keyA));
    }

    [Fact]
    public async Task Maintenance_WaitsForActiveOperationAndBlocksNewOperation()
    {
        using var maintenance = new MaintenanceCoordinator();
        var active = await maintenance.EnterOperationAsync(MaintenanceOperationKind.Write);
        var maintenanceTask = maintenance.EnterMaintenanceAsync().AsTask();
        Assert.True(SpinWait.SpinUntil(() => maintenance.IsMaintenancePending, TimeSpan.FromSeconds(2)));
        Assert.False(maintenanceTask.IsCompleted);

        var blockedTask = maintenance.EnterOperationAsync(MaintenanceOperationKind.Read).AsTask();
        Assert.False(blockedTask.IsCompleted);

        await active.DisposeAsync();
        var maintenanceLease = await maintenanceTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, maintenance.ActiveOperationCount);
        Assert.False(blockedTask.IsCompleted);

        await maintenanceLease.DisposeAsync();
        var admitted = await blockedTask.WaitAsync(TimeSpan.FromSeconds(2));
        await admitted.DisposeAsync();
    }

    [Fact]
    public async Task Maintenance_DrainsConcurrentQueryReplayAndWriteBeforeAdmittingRestore()
    {
        using var maintenance = new MaintenanceCoordinator();
        var query = await maintenance.EnterOperationAsync(MaintenanceOperationKind.Read);
        var replay = await maintenance.EnterOperationAsync(MaintenanceOperationKind.File);
        var write = await maintenance.EnterOperationAsync(MaintenanceOperationKind.Write);
        var restore = maintenance.EnterMaintenanceAsync().AsTask();
        Assert.True(SpinWait.SpinUntil(() => maintenance.IsMaintenancePending, TimeSpan.FromSeconds(2)));
        var lateRead = maintenance.EnterOperationAsync(MaintenanceOperationKind.Read).AsTask();

        await query.DisposeAsync();
        await replay.DisposeAsync();
        Assert.False(restore.IsCompleted);
        Assert.False(lateRead.IsCompleted);
        await write.DisposeAsync();
        var lease = await restore.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, maintenance.ActiveOperationCount);
        Assert.False(lateRead.IsCompleted);
        await lease.DisposeAsync();
        await (await lateRead.WaitAsync(TimeSpan.FromSeconds(2))).DisposeAsync();
    }

    [Fact]
    public async Task Composition_AcceptsRepositoryAndFileFailureDoublesWithoutStartingMt5()
    {
        var repository = DispatchProxy.Create<IReviewWorkspaceRepository, ThrowingProxy>();
        var packageWriter = DispatchProxy.Create<IReviewPackageWriter, ThrowingProxy>();
        var attachmentStore = DispatchProxy.Create<IReviewAttachmentStore, ThrowingProxy>();
        var backupService = DispatchProxy.Create<IReviewBackupService, ThrowingProxy>();
        using var sessions = new AccountSessionCoordinator();
        using var maintenance = new MaintenanceCoordinator();
        var database = new AppDatabase(Path.Combine(Path.GetTempPath(), $"tradepet-wp02-{Guid.NewGuid():N}.db"));
        var dependencies = new TradePetRuntimeDependencies(
            database,
            repository,
            packageWriter,
            attachmentStore,
            backupService,
            TimeProvider.System,
            new ManualAsyncScheduler(),
            sessions,
            maintenance);

        await using var runtime = new TradePetRuntime(
            new TradePet.App.ViewModels.MainViewModel(dependencies.Scheduler, dependencies.TimeProvider),
            dependencies);

        await Assert.ThrowsAsync<IOException>(() =>
            dependencies.ReviewRepository.LoadReviewDataVersionAsync("account-a"));
        await Assert.ThrowsAsync<IOException>(() =>
            dependencies.ReviewPackageWriter.WriteAsync(new ReviewExportPackage("x.zip", "v1", [])));
        await Assert.ThrowsAsync<IOException>(() =>
            dependencies.ReviewBackupService.ValidateAsync("missing.zip"));
    }

    [Fact]
    public void ReviewWorkspace_UsesInjectedClockForInitialServerDateEditor()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2031, 4, 5, 12, 0, 0, TimeSpan.Zero));

        var viewModel = new ReviewWorkspaceViewModel(new ManualAsyncScheduler(), clock);

        Assert.Equal("2031-04-05", viewModel.DailyDate);
    }

    private sealed class ManualAsyncScheduler : IAsyncScheduler
    {
        private readonly ConcurrentQueue<ScheduledDelay> _scheduled = new();
        private readonly SemaphoreSlim _available = new(0);

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            var scheduled = new ScheduledDelay(delay, cancellationToken);
            _scheduled.Enqueue(scheduled);
            _available.Release();
            return scheduled.Task;
        }

        public async Task<ScheduledDelay> NextAsync(CancellationToken cancellationToken = default)
        {
            await _available.WaitAsync(cancellationToken);
            return _scheduled.TryDequeue(out var scheduled)
                ? scheduled
                : throw new InvalidOperationException("调度信号与队列不一致。");
        }
    }

    private sealed class ScheduledDelay
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ScheduledDelay(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delay = delay;
            cancellationToken.Register(() => _completion.TrySetCanceled(cancellationToken));
        }

        public TimeSpan Delay { get; }
        public Task Task => _completion.Task;
        public void Release() => _completion.TrySetResult();
    }

    private class ThrowingProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new IOException($"Injected failure: {targetMethod?.Name}");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static TradeDetailData Detail(
        string accountKey,
        long positionId,
        TradeReviewDocument? document = null)
    {
        var now = new DateTimeOffset(2026, 9, 10, 2, 0, 0, TimeSpan.Zero);
        var trade = new TradeRecord(
            accountKey,
            positionId,
            "XAUUSD.s",
            TradeSide.Buy,
            now.AddMinutes(-5),
            now,
            new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 10),
            3500m,
            3501m,
            0.01m,
            0.01m,
            0m,
            1m,
            true);
        return new TradeDetailData(
            trade,
            [],
            null,
            document,
            null,
            [],
            null,
            null,
            [],
            [],
            [],
            null,
            new ReviewDataVersion(accountKey, 1, 1, 1, "rule", "time", now));
    }

    private static TradeReviewDocument ReviewDocument(
        TradeKey key,
        int revision,
        string summary,
        string entryReason = "")
    {
        var now = new DateTimeOffset(2026, 9, 10, 2, 0, 0, TimeSpan.Zero);
        return new TradeReviewDocument(
            key,
            ReviewCompletionStatus.Draft,
            entryReason,
            string.Empty,
            string.Empty,
            string.Empty,
            "next",
            summary,
            string.Empty,
            string.Empty,
            revision,
            "source",
            "rule",
            null,
            null,
            now,
            now);
    }

    private static DailyJournal DailyJournal(
        string accountKey,
        DateOnly date,
        string preMarketPlan,
        int revision)
    {
        var now = new DateTimeOffset(2026, 9, 10, 2, 0, 0, TimeSpan.Zero);
        return new DailyJournal(
            accountKey,
            date,
            preMarketPlan,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            "next",
            ReviewCompletionStatus.Draft,
            revision,
            "source-1",
            now,
            now);
    }
}
