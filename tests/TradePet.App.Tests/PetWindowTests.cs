using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using TradePet.App.Controls;
using TradePet.App.ViewModels;
using TradePet.App.Views;
using TradePet.Application.Review;
using TradePet.Core.Domain;
using Xunit;

namespace TradePet.App.Tests;

[CollectionDefinition("Desktop window tests", DisableParallelization = true)]
public sealed class DesktopWindowTestCollection;

[Collection("Desktop window tests")]
public sealed class PetWindowTests
{
    [Fact]
    public async Task PetWindow_ExitMenuAndTransientUiRespectInteractionAndVisibility()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new App();
            PetWindow? window = null;
            try
            {
                app.InitializeComponent();
                var viewModel = new MainViewModel();
                window = new PetWindow(viewModel) { ShowActivated = false };
                window.Show();
                window.UpdateLayout();
                Assert.Equal(SystemParameters.WorkArea.Right - window.Width - 24, window.Left, 3);
                Assert.Equal(SystemParameters.WorkArea.Bottom - window.Height - 24, window.Top, 3);
                var sprite = (PetSpriteControl)window.FindName("PetSprite");
                var quickCard = (Popup)window.FindName("QuickCardPopup");
                var bubble = (Popup)window.FindName("SpeechBubblePopup");
                var timer = (DispatcherTimer)typeof(PetSpriteControl)
                    .GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sprite)!;

                Assert.Equal(RenderMode.SoftwareOnly,
                    ((HwndTarget)PresentationSource.FromVisual(window).CompositionTarget).RenderMode);
                Assert.True(timer.IsEnabled);
                sprite.SetAnimationPaused(true);
                Assert.False(timer.IsEnabled);
                sprite.SetAnimationPaused(false);
                Assert.True(timer.IsEnabled);

                viewModel.IsBubbleVisible = true;
                Assert.True(bubble.IsOpen);
                window.Hide();
                Assert.False(bubble.IsOpen);
                Assert.False(timer.IsEnabled);
                viewModel.IsBubbleVisible = false;
                viewModel.IsBubbleVisible = true;
                Assert.False(bubble.IsOpen);
                window.Show();
                Assert.True(bubble.IsOpen);
                Assert.True(timer.IsEnabled);

                // A visible alert must not be left behind by autonomous movement.
                viewModel.PetActivity = PetActivity.RunningRight;
                var left = window.Left;
                Invoke(window, "MoveWithActivity");
                Assert.Equal(left, window.Left);

                typeof(PetWindow).GetField("_isDragging", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, true);
                Invoke(window, "UpdateSpeechBubble");
                Assert.False(bubble.IsOpen);
                Invoke(window, "SetQuickCardVisible", true);
                Assert.False(quickCard.IsOpen);
                Invoke(window, "MoveWithActivity");
                Assert.Equal(left, window.Left);
                typeof(PetWindow).GetField("_isDragging", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, false);

                // The quick card is hosted by a separate popup, so the pet window never grows
                // to include a stale card surface before an OS-level drag begins.
                viewModel.IsBubbleVisible = false;
                Invoke(window, "SetQuickCardVisible", true);
                Assert.True(quickCard.IsOpen);
                Assert.InRange(window.Width, sprite.Width + 15, sprite.Width + 17);
                Assert.InRange(window.Height,
                    Math.Max(230, sprite.Height + 28) - 1,
                    Math.Max(230, sprite.Height + 28) + 1);
                Invoke(window, "SetQuickCardVisible", false);
                Assert.False(quickCard.IsOpen);

                // A click temporarily closes the popup before DragMove determines that the
                // pointer did not move. The second click must toggle the pinned state instead
                // of treating that temporary close as a request to reopen the card.
                Invoke(window, "ToggleQuickCard");
                Assert.True(quickCard.IsOpen);
                Invoke(window, "SetQuickCardVisible", false);
                Assert.False(quickCard.IsOpen);
                Invoke(window, "ToggleQuickCard");
                Assert.False(quickCard.IsOpen);
                Assert.False((bool)typeof(PetWindow)
                    .GetField("_quickCardPinned", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!);

                window.ContextMenu.PlacementTarget = window;
                window.ContextMenu.IsOpen = true;
                window.ContextMenu.UpdateLayout();
                var topLevelItems = window.ContextMenu.Items.OfType<MenuItem>().ToArray();
                Assert.Equal(8, topLevelItems.Length);
                Assert.Contains(topLevelItems, item => ReferenceEquals(item.Command, viewModel.ShowEntryReasonCommand));
                Assert.Contains(topLevelItems, item => ReferenceEquals(item.Command, viewModel.ShowOpportunityCommand));
                Assert.Contains(topLevelItems, item => ReferenceEquals(item.Command, viewModel.ShowWeeklyGoalCommand));
                var reviewTools = Assert.Single(topLevelItems, item => Equals(item.Header, "复盘与工具"));
                Assert.Equal(7, reviewTools.Items.Count);
                var dailyReport = Assert.Single(reviewTools.Items.OfType<MenuItem>(),
                    item => ReferenceEquals(item.Command, viewModel.ShowDailyTradingReportCommand));
                reviewTools.IsSubmenuOpen = true;
                reviewTools.UpdateLayout();
                var submenu = (Popup)reviewTools.Template.FindName("PART_Popup", reviewTools);
                Assert.True(submenu.IsOpen);
                submenu.Child.UpdateLayout();
                Assert.True(dailyReport.IsVisible);
                Assert.NotNull(PresentationSource.FromVisual(dailyReport));
                reviewTools.IsSubmenuOpen = false;
                var displaySettings = Assert.Single(topLevelItems, item => Equals(item.Header, "显示与设置"));
                Assert.Contains(displaySettings.Items.OfType<MenuItem>(),
                    item => ReferenceEquals(item.Command, viewModel.ShowSettingsPageCommand));
                var quickReviewItem = Assert.Single(window.ContextMenu.Items.OfType<MenuItem>(),
                    item => Equals(item.Header, "快速复盘"));
                Assert.Same(viewModel.ShowQuickReviewCommand, quickReviewItem.Command);
                var quickReviewCount = 0;
                viewModel.ShowQuickReviewAsync = () =>
                {
                    quickReviewCount++;
                    return Task.CompletedTask;
                };
                quickReviewItem.Command.Execute(null);
                Assert.Equal(1, quickReviewCount);
                var exitItem = Assert.Single(window.ContextMenu.Items.OfType<MenuItem>(),
                    item => Equals(item.Header, "退出天禄交易助手"));
                Assert.Same(viewModel.ExitCommand, exitItem.Command);
                var exitCount = 0;
                viewModel.ExitApplicationAsync = () =>
                {
                    exitCount++;
                    return Task.CompletedTask;
                };
                exitItem.Command.Execute(null);
                Assert.Equal(1, exitCount);
                window.ContextMenu.IsOpen = false;

                VerifyQuickReview(window);
                VerifyEntryReason(window);
                VerifyBehaviorAction(window);
                window.DisposeTrayIcon();
                Assert.False(quickCard.IsOpen);
                viewModel.IsBubbleVisible = false;
                viewModel.IsBubbleVisible = true;
                sprite.SetAnimationPaused(false);
                Assert.False(timer.IsEnabled);
                Assert.False(bubble.IsOpen);
                VerifySetupFlow();
                VerifyDailyTradingReport();
                // WPF permits one Application per test host; exercise the console on this STA thread.
                VerifyMainWindowGuide();
                ReviewDashboardTests.VerifyView();
                VerifyEnglishMode();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                window?.DisposeTrayIcon();
                window?.Close();
                app.Shutdown();
                completion.TrySetResult();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static void VerifyEnglishMode()
    {
        TradePet.Core.Localization.UiText.Configure("en-US");
        MainWindow? console = null;
        try
        {
            var vm = new MainViewModel();
            var englishPet = new PetWindow(vm);
            try
            {
                var menu = (System.Windows.Forms.ContextMenuStrip)typeof(PetWindow).GetField("_trayMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(englishPet)!;
                var trayItems = menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().ToArray();
                Assert.Equal(9, trayItems.Length);
                Assert.Equal(2, trayItems.Count(item => item.HasDropDownItems));
                Assert.All(trayItems.Concat(trayItems.SelectMany(item => item.DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>())), item =>
                    Assert.DoesNotContain(item.Text ?? string.Empty, c => c is >= '\u3400' and <= '\u9fff'));
                var tray = (System.Windows.Forms.NotifyIcon)typeof(PetWindow).GetField("_trayIcon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(englishPet)!;
                Assert.DoesNotContain(tray.Text, c => c is >= '\u3400' and <= '\u9fff');
            }
            finally { englishPet.DisposeTrayIcon(); englishPet.Close(); }
            var goalName = typeof(TradePet.App.Runtime.TradePetRuntime).GetMethod("PetGoalName", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.All(TradePet.Core.Review.BehaviorGoalMeasurement.SupportedRules(BehaviorPolicy.Balanced), rule =>
                Assert.DoesNotContain((string)goalName.Invoke(null, [rule])!, c => c is >= '\u3400' and <= '\u9fff'));
            Assert.All(vm.ReviewWorkspace.PlaybookRuleDrafts, rule => Assert.DoesNotContain(rule.Name, c => c is >= '\u3400' and <= '\u9fff'));
            vm.ReviewWorkspace.PlaybookRules = "Entry|Check entry|Keep evidence|Critical";
            Assert.True(Assert.Single(vm.ReviewWorkspace.PlaybookRuleDrafts).IsCritical);
            Assert.True(vm.ReviewWorkspace.CanSavePlaybookRules);
            var saved = 0;
            vm.SaveSettingsAsync = () => { Assert.Equal("en-US", vm.UiLanguage); saved++; return Task.CompletedTask; };
            console = new MainWindow(vm) { AllowClose = true, ShowActivated = false, Width = 1100, Height = 850 };
            console.Show();
            VerifyAllConsolePageBindings(console);
            console.ShowPage(6);
            console.UpdateLayout();
            var picker = (ComboBox)console.FindName("UiLanguagePicker");
            picker.SelectedValue = "en-US";
            Assert.Equal("en-US", vm.UiLanguage);
            var save = Descendants(console).OfType<Button>().First(button => ReferenceEquals(button.Command, vm.SaveSettingsCommand));
            Assert.Equal("Save settings", save.Content);
            save.Command.Execute(null);
            Assert.Equal(1, saved);
            Assert.Equal("en-us", console.Language.IetfLanguageTag);

            // English labels must not become the stored filter values.
            console.ShowPage(3);
            console.UpdateLayout();
            var sides = Descendants(console).OfType<ComboBox>().First(combo => ReferenceEquals(combo.ItemsSource, vm.ReviewSideFilterOptions));
            sides.SelectedItem = "买入";
            Assert.Equal("买入", vm.SelectedReviewSideFilter);
            Assert.Equal("Buy", ((TradePet.App.Localization.UiTranslationConverter)App.Current.Resources["UiTranslation"]).Convert(
                sides.SelectedItem, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture));

            // Saved names and check instructions are user content, even when they match a UI label.
            vm.ReviewWorkspace.Playbooks.Add(new TradePet.App.ViewModels.Review.PlaybookRow("保存设置", "v1", 1, "", ""));
            vm.ReviewWorkspace.Goals.Add(new TradePet.App.ViewModels.Review.GoalRow("保存设置", "", "保存设置", "1", "", "", false,
                vm.SaveSettingsCommand, vm.SaveSettingsCommand));
            var workspace = (ReviewWorkspaceView)console.FindName("ReviewWorkspace");
            workspace.SelectGuideTab(4);
            console.UpdateLayout();
            Assert.Equal(3, Descendants(workspace).OfType<TextBlock>().Count(block => block.Text == "保存设置"));

            var now = DateTimeOffset.UtcNow;
            var trade = new TradeRecord("Broker|1", 1, "TEST", TradeSide.Buy, now.AddMinutes(-10), now,
                new(2026, 9, 24), new(2026, 9, 24), 100, 99, 1, 1, 0, -5, true);
            var detail = new TradeDetailData(trade, [], null, null, null, [], null, null, [], [], [], null,
                new ReviewDataVersion("Broker|1", 1, 1, 1, "rule-1", "time-1", now));
            var quick = new QuickReviewCard(detail);
            Assert.DoesNotContain("亏损", quick.ExitReason);
            Assert.Null(quick.ReportedExecution);
            Assert.Empty(quick.Emotion);
            var original = new TradeReviewDocument(new("Broker|1", 1), ReviewCompletionStatus.Reviewed,
                "入场原文", "保存设置", "", "我的改进原文", "下一步", "保存设置", "担心错过", "",
                3, "source", "rule", "source", "rule", now, now, now, IsQuickReview: true,
                ReportedExecution: PlanExecutionSelfReport.Deviated, ReportedExecutionRecordedAtUtc: now);
            var reopened = new QuickReviewCard(detail with { Document = original });
            Assert.Equal(original.ExitReason, reopened.ExitReason);
            Assert.Equal(original.ToImprove, reopened.Improvement);
            Assert.Equal(original.Summary, reopened.AnalysisSummary);
            Assert.Equal(original.ReportedExecution, reopened.ReportedExecution);
            Assert.Equal(original.Emotion, reopened.Emotion);
            var quickSaved = false;
            reopened.SaveReviewAsync = card => { Assert.Equal(original.Summary, card.AnalysisSummary); quickSaved = true; return Task.FromResult<string?>(null); };
            ((Button)reopened.FindName("QuickSaveButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(quickSaved);
            Assert.True(reopened.SaveRequested);
            ReviewDashboardTests.VerifyView();
            var fixture = ReviewDashboardTests.Fixture([ReviewDashboardTests.Trade(1, -33.82m)]);
            var date = fixture.Data.Trades[0].CloseServerDate!.Value;
            var at = fixture.Data.Trades[0].ClosedAtUtc!.Value;
            var note = new TradeReviewDocument(new("Broker|1", 1), ReviewCompletionStatus.Reviewed,
                "", "保存设置", "", "", "保存设置", "我的原文", "", "", 1,
                "source", "rule", "source", "rule", at, at, at, true);
            var documents = new Dictionary<long, TradeReviewDocument> { [1] = note };
            var fact = new TradePet.Core.Review.ReviewWorkspaceCalculator().BuildDailyFacts("Broker|1", date, date,
                fixture.Data.Trades, fixture.Data.Deals, documents, [], new Dictionary<DateOnly, DailyState>(), 0)[date];
            var report = DailyReportAnalyzer.Analyze(fixture.Data with { Documents = documents }, fact, false);
            Assert.Contains("- Next action: 保存设置", report.Markdown);
            Assert.DoesNotContain("## 逐笔风险", report.Markdown);
            Assert.Contains("-33.82", report.Markdown);
        }
        finally { console?.Close(); TradePet.Core.Localization.UiText.Configure("zh-CN"); }

        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            var seen = new HashSet<DependencyObject>();
            var pending = new Stack<DependencyObject>();
            pending.Push(root);
            while (pending.TryPop(out var current))
            {
                if (!seen.Add(current)) continue;
                yield return current;
                foreach (var child in LogicalTreeHelper.GetChildren(current))
                    if (child is DependencyObject dependency) pending.Push(dependency);
                if (current is not System.Windows.Media.Visual) continue;
                for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(current); index++)
                    pending.Push(System.Windows.Media.VisualTreeHelper.GetChild(current, index));
            }
        }
    }

    private static void VerifyMainWindowGuide()
    {
        var viewModel = new MainViewModel();
        var saveCount = 0;
        var console = new MainWindow(viewModel)
        {
            AllowClose = true,
            ShowActivated = false,
            Width = 920,
            Height = 640,
            SaveGuideCompletionAsync = () =>
            {
                saveCount++;
                return Task.CompletedTask;
            },
        };
        try
        {
            console.Show();
            Assert.Null(console.FindName("GuideStructuredForm"));
            Assert.Null(console.FindName("GuideStructuredList"));
            viewModel.DailyTarget = 2m;
            viewModel.DailyTargetUnitIndex = 1;
            Assert.Equal("2%（当日初始余额）", viewModel.DailyTargetDisplay);
            viewModel.DailyTargetUnitIndex = 0;
            console.EnableGuideOnFirstOpen();
            console.UpdateLayout();

            var overlay = (Canvas)console.FindName("GuideOverlay");
            var tabs = (TabControl)console.FindName("MainTabs");
            var next = (Button)console.FindName("GuideNext");
            var previous = (Button)console.FindName("GuidePrevious");
            var skip = (Button)console.FindName("GuideSkip");
            var card = (Border)console.FindName("GuideCard");
            Assert.Equal(Visibility.Visible, overlay.Visibility);
            Assert.False(previous.IsEnabled);
            Assert.Equal(0, tabs.SelectedIndex);

            foreach (var page in new[] { 1, 2, 3, 4, 6, 5 })
            {
                next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                console.UpdateLayout();
                Assert.Equal(page, tabs.SelectedIndex);
                Assert.InRange(Canvas.GetLeft(card), 0, overlay.ActualWidth - card.ActualWidth);
                Assert.InRange(Canvas.GetTop(card), 0, overlay.ActualHeight - card.ActualHeight);
                var target = (FrameworkElement)typeof(MainWindow).GetMethod("GetGuideTarget",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(console, null)!;
                var bounds = target.TransformToAncestor((System.Windows.Media.Visual)console.FindName("MainRoot"))
                    .TransformBounds(new Rect(new Point(), target.RenderSize));
                Assert.True(bounds.Bottom > 0 && bounds.Top < overlay.ActualHeight,
                    $"Guide target is outside the visible console: {target.Name}");
            }

            Assert.Equal("完成", next.Content);
            next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(viewModel.ConsoleGuideCompleted);
            Assert.Equal(1, saveCount);
            Assert.Equal(Visibility.Collapsed, overlay.Visibility);

            var savePlan = (Button)console.FindName("GuideSavePlan");
            var targetInput = (TextBox)console.FindName("DailyTargetInput");
            var validation = (TextBlock)console.FindName("PlanValidationText");
            var planSaveCount = 0;
            viewModel.SaveDailyPlanAsync = () =>
            {
                planSaveCount++;
                return Task.CompletedTask;
            };
            tabs.SelectedIndex = 1;
            targetInput.Text = "-100";
            savePlan.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(0, planSaveCount);
            Assert.Equal(Visibility.Visible, validation.Visibility);
            targetInput.Text = "100";
            var tradeLimitInput = (TextBox)console.FindName("MaximumTradesInput");
            tradeLimitInput.Text = "1.5";
            savePlan.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(0, planSaveCount);
            tradeLimitInput.Text = string.Empty;
            var radiusInput = (TextBox)console.FindName("LossZoneToleranceInput");
            radiusInput.Text = "0";
            savePlan.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(0, planSaveCount);
            radiusInput.Text = "200";
            savePlan.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(1, planSaveCount);
            Assert.Equal(100m, viewModel.DailyTarget);
            Assert.Equal(Visibility.Collapsed, validation.Visibility);

            console.Hide();
            console.Show();
            console.EnableGuideOnFirstOpen();
            console.UpdateLayout();
            Assert.Equal(Visibility.Collapsed, overlay.Visibility);

            var replay = (Button)console.FindName("GuideReplayButton");
            replay.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            console.UpdateLayout();
            Assert.Equal(Visibility.Visible, overlay.Visibility);
            skip.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(1, saveCount);

            ((Button)console.FindName("ReviewGuideButton"))
                .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var workspace = (ReviewWorkspaceView)console.FindName("ReviewWorkspace");
            var workspaceTabs = (TabControl)workspace.FindName("WorkspaceTabs");
            var statusPanel = (FrameworkElement)workspace.FindName("ReviewStatusPanel");
            var expectedTabs = new[] { 0, 2, 3, 4, -1 };
            for (var step = 0; step < expectedTabs.Length; step++)
            {
                console.UpdateLayout();
                Assert.Equal(expectedTabs[step] < 0 ? 6 : 3, tabs.SelectedIndex);
                if (expectedTabs[step] >= 0)
                {
                    Assert.Equal(expectedTabs[step], workspaceTabs.SelectedIndex);
                    Assert.True(statusPanel.IsVisible);
                }
                Assert.InRange(Canvas.GetLeft(card), 0, overlay.ActualWidth - card.ActualWidth);
                Assert.InRange(Canvas.GetTop(card), 0, overlay.ActualHeight - card.ActualHeight);
                var target = (FrameworkElement)typeof(MainWindow).GetMethod("GetGuideTarget",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(console, null)!;
                Assert.True(target.IsVisible, $"Review guide target is hidden: {target.Name}");
                Assert.IsNotType<Button>(target);
                Assert.NotEqual("GuideReviewHeader", target.Name);
                var bounds = target.TransformToAncestor((System.Windows.Media.Visual)console.FindName("MainRoot"))
                    .TransformBounds(new Rect(new Point(), target.RenderSize));
                Assert.True(bounds.Bottom > 0 && bounds.Top < overlay.ActualHeight,
                    $"Review guide target is outside the console: {target.Name}");
                next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            Assert.Equal(Visibility.Collapsed, overlay.Visibility);
            Assert.Equal(1, saveCount);
            var daily = (Button)console.FindName("ReportDailyButton");
            var markdown = (Button)console.FindName("ReportMarkdownButton");
            Assert.True(daily.IsVisible);
            Assert.True(markdown.IsVisible);
            Assert.Same(viewModel.ShowDailyTradingReportCommand, daily.Command);
            Assert.Same(viewModel.ReviewWorkspace.ExportMarkdownCommand, markdown.Command);
            Assert.Null(workspace.FindName("ReviewGuideExport"));
            VerifyAllConsolePageBindings(console);
        }
        finally { console.Close(); }
    }

    private static void VerifyAllConsolePageBindings(MainWindow console)
    {
        var log = new StringWriter();
        var listener = new TextWriterTraceListener(log);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        var count = 0;
        try
        {
            for (var page = 0; page < 7; page++)
            {
                console.ShowPage(page);
                console.UpdateLayout();
                foreach (var button in Buttons(console))
                {
                    if (System.Windows.Data.BindingOperations.GetBindingExpressionBase(button, Button.CommandProperty) is null) continue;
                    Assert.True(button.Command is not null, $"Unbound action on page {page}: {button.Content}");
                    count++;
                }
            }
            Assert.True(count >= 30, $"Insufficient console action coverage: {count}");
            Assert.DoesNotContain("Error:", log.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            listener.Dispose();
        }

        static IEnumerable<Button> Buttons(DependencyObject parent)
        {
            var pending = new Stack<DependencyObject>();
            var seen = new HashSet<DependencyObject>();
            pending.Push(parent);
            while (pending.TryPop(out var current))
            {
                if (!seen.Add(current)) continue;
                if (current is Button button) yield return button;
                foreach (var child in LogicalTreeHelper.GetChildren(current))
                    if (child is DependencyObject dependency) pending.Push(dependency);
                if (current is not System.Windows.Media.Visual) continue;
                for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(current); index++)
                    pending.Push(System.Windows.Media.VisualTreeHelper.GetChild(current, index));
            }
        }
    }

    private static void VerifyQuickReview(PetWindow pet)
    {
        var now = DateTimeOffset.UtcNow;
        var trade = new TradeRecord("Broker|1", 1, "TEST", TradeSide.Buy, now.AddMinutes(-10), now,
            new(2026, 9, 24), new(2026, 9, 24), 100, 99, 1, 1, 0, -5, true);
        var detail = new TradeDetailData(trade, [], null, null, null,
            [new PositionPnlSample(new TradeKey("Broker|1", 1), now.AddSeconds(-1), 0, 20, 20, 1,
                null, null, 1_000, "position-pnl-v1")], null, null, [], [], [], null,
            new ReviewDataVersion("Broker|1", 1, 1, 1, "rule-1", "time-1", now));
        var review = new QuickReviewCard(detail);
        var existing = new TradeReviewDocument(new("Broker|1", 1), ReviewCompletionStatus.Reviewed,
            "入场记录", "保存过的平仓原因", "", "保存过的改进", "下一步", "保存过的总结", "", "",
            3, "source", "rule", "source", "rule", now, now, now, IsQuickReview: true);
        var reopenedCard = new QuickReviewCard(detail with { Document = existing });
        Assert.Equal(3, reopenedCard.DocumentRevision);
        Assert.Equal(existing.ExitReason, reopenedCard.ExitReason);
        Assert.Equal(existing.ToImprove, reopenedCard.Improvement);
        Assert.Equal(existing.Summary, reopenedCard.AnalysisSummary);
        var presetCard = new QuickReviewCard(detail with
        {
            Document = existing with { ExitReason = "主动平仓，接受当前亏损" },
        });
        var reasonChoices = ((WrapPanel)presetCard.FindName("ReasonChoices")).Children.OfType<RadioButton>().ToArray();
        var manualLoss = reasonChoices[3];
        Assert.Same(manualLoss, Assert.Single(reasonChoices, choice => choice.IsChecked == true));
        manualLoss.ApplyTemplate();
        Assert.Equal("#FF1B6655", ((Border)manualLoss.Template.FindName("ChoiceBorder", manualLoss)).Background.ToString());
        reasonChoices[2].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Same(reasonChoices[2], Assert.Single(reasonChoices, choice => choice.IsChecked == true));
        Assert.Equal("主动平仓，兑现盈利", presetCard.ExitReason);
        var reasonBox = (TextBox)presetCard.FindName("ExitReasonBox");
        reasonBox.Text = "触及止损价离场（推测）";
        Assert.Same(reasonChoices[1], Assert.Single(reasonChoices, choice => choice.IsChecked == true));
        Assert.Equal("触及止损价离场（推测）", presetCard.ExitReason);
        reasonBox.Text = TradePet.Core.Localization.UiText.Translate("接近保本时主动平仓", "en-US");
        Assert.Same(reasonChoices[4], Assert.Single(reasonChoices, choice => choice.IsChecked == true));
        reasonBox.Text = "我的自定义退出原因";
        Assert.DoesNotContain(reasonChoices, choice => choice.IsChecked == true);
        try
        {
            var focus = System.Windows.Input.Keyboard.FocusedElement;
            pet.ShowQuickReviewCard(review);
            review.UpdateLayout();
            var popup = (Popup)pet.FindName("ReviewCardPopup");
            Assert.True(popup.IsOpen);
            Assert.Same(pet.FindName("PetSprite"), popup.PlacementTarget);
            Assert.Equal(PlacementMode.Custom, popup.Placement);
            Assert.Same(focus, System.Windows.Input.Keyboard.FocusedElement);
            Assert.Null(review.FindName("PlanBox"));
            Assert.Contains("浮盈回吐", review.ExitReason);
            Assert.Contains("回吐 25", review.AnalysisSummary);
            var attempts = 0;
            var completed = 0;
            review.Completed += _ => completed++;
            review.SaveReviewAsync = _ => Task.FromResult(++attempts == 1 ? "存储失败，请重试" : (string?)null);
            var saveButton = (Button)review.FindName("QuickSaveButton");
            saveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.False(review.SaveRequested);
            Assert.True(popup.IsOpen);
            Assert.False(review.IsCompleted);
            Assert.Equal("存储失败，请重试", ((TextBlock)review.FindName("SaveStatusText")).Text);
            saveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(review.SaveRequested);
            Assert.False(popup.IsOpen);
            Assert.True(review.IsCompleted);
            Assert.Equal(1, completed);
            Assert.False(string.IsNullOrWhiteSpace(review.Improvement));
            ((TextBox)review.FindName("ExitReasonBox")).Text = "主动退出";
            ((TextBox)review.FindName("ImproveBox")).Text = "我的修正";
            Assert.Equal("主动退出", review.ExitReason);
            Assert.Equal("我的修正", review.Improvement);
            Assert.Contains("回吐 25", review.AnalysisSummary);
        }
        finally { review.Dismiss(); }
    }

    private static void VerifyEntryReason(PetWindow pet)
    {
        var now = DateTimeOffset.UtcNow;
        var trade = new TradeRecord("Broker|1", 2, "TEST", TradeSide.Buy, now, null,
            new(2026, 10, 3), null, 100, null, 1, 1, 0, 0, false);
        var card = new EntryReasonCard(trade, 0);
        // Match the runtime subscription order: it may show the next card before the pet's completion handler.
        var next = new EntryReasonCard(trade with { PositionId = 3 }, 0);
        card.Completed += _ => pet.ShowEntryReasonCard(next);
        var focus = System.Windows.Input.Keyboard.FocusedElement;
        pet.ShowEntryReasonCard(card);
        card.UpdateLayout();
        var popup = (Popup)pet.FindName("ReviewCardPopup");
        Assert.True(popup.IsOpen);
        Assert.Same(pet.FindName("PetSprite"), popup.PlacementTarget);
        Assert.Same(focus, System.Windows.Input.Keyboard.FocusedElement);
        var attempts = 0;
        card.SaveReasonAsync = response =>
        {
            Assert.Equal("回踩入场", response.Reason);
            return Task.FromResult(++attempts == 1 ? "失败，请重试" : (string?)null);
        };
        typeof(EntryReasonCard).GetMethod("Reason_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(card, [new Button { Tag = "回踩入场" }, new RoutedEventArgs()]);
        Assert.Equal("回踩入场", card.Reason);
        var save = (Button)card.FindName("RecordReasonButton");
        save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.False(card.IsCompleted);
        Assert.True(popup.IsOpen);
        Assert.Equal("失败，请重试", ((TextBlock)card.FindName("StatusText")).Text);
        save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.True(card.IsCompleted);
        Assert.True(popup.IsOpen);
        Assert.Same(next, ((ContentControl)pet.FindName("ReviewCardHost")).Content);
        save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(2, attempts);
        next.Dismiss();
        Assert.False(popup.IsOpen);
    }

    private static void VerifyBehaviorAction(PetWindow pet)
    {
        var card = new BehaviorActionCard("记录未交易机会", "TEST", "保存机会",
            [new("symbol", "品种", [new("TEST", "TEST")]), new("reason", "原因", [new("风险限制", "风险限制")])]);
        Assert.Null(card.Selection("symbol"));
        Assert.Null(card.Selection("reason"));
        pet.ShowBehaviorActionCard(card);
        var popup = (Popup)pet.FindName("ReviewCardPopup");
        Assert.True(popup.IsOpen);
        Assert.Same(card, ((ContentControl)pet.FindName("ReviewCardHost")).Content);
        var groups = (StackPanel)card.FindName("ChoiceGroups");
        ((RadioButton)((WrapPanel)groups.Children[1]).Children[0]).IsChecked = true;
        ((RadioButton)((WrapPanel)groups.Children[3]).Children[0]).IsChecked = true;
        var attempts = 0;
        card.SaveActionAsync = response =>
        {
            Assert.Equal("TEST", response.Selection("symbol"));
            Assert.Equal("风险限制", response.Selection("reason"));
            return Task.FromResult(++attempts == 1 ? "失败，请重试" : (string?)null);
        };
        var save = (Button)card.FindName("SaveButton");
        save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.False(card.IsCompleted);
        Assert.True(popup.IsOpen);
        Assert.Equal("风险限制", card.Selection("reason"));
        save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.True(card.IsCompleted);
        Assert.False(popup.IsOpen);
        Assert.Equal(2, attempts);

        var rules = TradePet.Core.Review.BehaviorGoalMeasurement.SupportedRules(BehaviorPolicy.Balanced);
        var goalName = typeof(TradePet.App.Runtime.TradePetRuntime).GetMethod("PetGoalName", BindingFlags.Static | BindingFlags.NonPublic)!;
        var summary = string.Join(Environment.NewLine, Enumerable.Repeat("交易小结与目标进度：用于检查展开后仍能查看完整记录。", 14));
        var goalCard = new BehaviorActionCard("交易小结与改进", summary, "设为7日目标",
            [new("rule", "只选一个改进重点", rules.Select(rule => new PetActionChoice(rule.ToString(), (string)goalName.Invoke(null, [rule])!)).ToArray())],
            compactSummary: true);
        pet.ShowBehaviorActionCard(goalCard);
        goalCard.UpdateLayout();
        var goalChoices = ((WrapPanel)((StackPanel)goalCard.FindName("ChoiceGroups")).Children[1]).Children.OfType<RadioButton>().ToArray();
        Assert.Equal(7, goalChoices.Length);
        var details = (Expander)goalCard.FindName("SummaryDetails");
        Assert.Equal(Visibility.Visible, details.Visibility);
        Assert.False(details.IsExpanded);
        Assert.Equal(summary, ((TextBlock)goalCard.FindName("SummaryDetailsText")).Text);
        var scroll = (ScrollViewer)goalCard.FindName("FormScroll");
        var lastChoiceBounds = goalChoices[^1].TransformToAncestor(scroll)
            .TransformBounds(new Rect(0, 0, goalChoices[^1].ActualWidth, goalChoices[^1].ActualHeight));
        Assert.True(lastChoiceBounds.Bottom <= scroll.ViewportHeight, "All goal options should be visible before expanding the summary.");
        goalChoices[0].IsChecked = true;
        goalChoices[^1].IsChecked = true;
        Assert.Same(goalChoices[^1], Assert.Single(goalChoices, choice => choice.IsChecked == true));
        Assert.Equal(BehaviorRuleKind.PriceFixationScore.ToString(), goalCard.Selection("rule"));
        goalCard.SaveActionAsync = response =>
        {
            Assert.Equal(BehaviorRuleKind.PriceFixationScore.ToString(), response.Selection("rule"));
            return Task.FromResult((string?)null);
        };
        ((Button)goalCard.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.True(goalCard.IsCompleted);
        Assert.False(popup.IsOpen);
    }

    private static void VerifyDailyTradingReport()
    {
        var analysis = new DailyReportAnalysis(
            [new("风险、回撤与持仓", ["采样内净值回撤 50 USD；历史未覆盖时段不补算。", "可靠初始风险覆盖 1/2 笔；缺失记录不当作零风险。"]),
             new("下一交易日行动清单", ["补齐 #42 的入场风险与退出原因。"])],
            [new(42, "TEST", "买入", "2026-09-25 10:00:00", "2026-09-25 10:20:00", "0小时 20分 0秒",
                "0.1", "0.2", "12 USD", "-2 USD", "10 USD", "1.2 R", "-5 USD", "20 USD", "8 USD",
                "100% · 可靠", "开仓时 SL 95 / TP 110", "按记录中的退出条件平仓", "结合走势核对退出")], "## 风险分析\n\n完整事实。\n");
        var report = new DailyTradingReport("测试账户", "USD", new(2026, 9, 25), true,
            12, -2, 1, 1, 0, 0, 100, null, null, 1, 0, 0, 1, 19, 5,
            "# 2026-09-25 交易日报\n\n" + analysis.Markdown, Analysis: analysis);
        var window = new DailyTradingReportWindow(report);
        try
        {
            Assert.False(window.Topmost);
            Assert.False(window.ShowActivated);
            Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
            window.Show();
            window.UpdateLayout();
            Assert.Equal(analysis.Sections, ((ItemsControl)window.FindName("AnalysisSections")).ItemsSource);
            var grid = (DataGrid)window.FindName("TradeDetailsGrid");
            Assert.Single(grid.Items);
            Assert.Equal(analysis.Trades[0], grid.Items[0]);
            Assert.Equal(report.Markdown, ((TextBox)window.FindName("FullReportText")).Text);
            Assert.Null(window.FindName("PlanText"));
            Assert.Contains("全程采样可靠", ((TextBlock)window.FindName("ProcessText")).Text);
            Assert.Equal("19 条风险提醒，其中冷静期触发 5 条（不含正常检查）",
                ((TextBlock)window.FindName("BehaviorText")).Text);
            var tabs = (TabControl)window.FindName("ReportTabs");
            for (var index = 0; index < tabs.Items.Count; index++)
            {
                tabs.SelectedIndex = index;
                window.UpdateLayout();
                Assert.True(tabs.ActualHeight > 200);
                if (Environment.GetEnvironmentVariable("TRADEPET_REPORT_PREVIEW_DIR") is { Length: > 0 } directory)
                {
                    System.IO.Directory.CreateDirectory(directory);
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,
                        (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = System.IO.File.Create(System.IO.Path.Combine(directory, $"daily-report-{index}.png"));
                    encoder.Save(stream);
                }
            }
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            window.UpdateLayout();
            Assert.True(tabs.ActualHeight > 200);
        }
        finally { window.Close(); }
    }

    private static void VerifySetupFlow()
    {
        var viewModel = new MainViewModel { SelectedTerminalPath = @"C:\TradePet-test-missing\terminal64.exe" };
        var saved = false;
        var setup = new SetupWindow(viewModel, () => Task.FromResult(saved)) { ShowActivated = false };
        try
        {
            setup.Show();
            setup.UpdateLayout();
            Assert.Equal(@"C:\TradePet-test-missing\terminal64.exe", viewModel.SelectedTerminalPath);
            var platform = (ComboBox)setup.FindName("PlatformPicker");
            platform.SelectedValue = TradePet.Infrastructure.Mt5.TradingPlatform.Mt4;
            Assert.Equal(TradePet.Infrastructure.Mt5.TradingPlatform.Mt4, viewModel.SelectedPlatform);
            Assert.Equal(Visibility.Collapsed, ((StackPanel)setup.FindName("PythonPanel")).Visibility);
            var next = (Button)setup.FindName("NextButton");
            for (var i = 0; i < 3; i++) next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(Visibility.Visible, ((StackPanel)setup.FindName("PreferencesStep")).Visibility);
            next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(next.IsEnabled);
            Assert.Contains("未能保存", ((TextBlock)setup.FindName("Status")).Text);
            saved = true;
            next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.False(next.IsEnabled);
            Assert.Contains("已保存", ((TextBlock)setup.FindName("Status")).Text);
        }
        finally { setup.Close(); }
    }

    private static void Invoke(PetWindow window, string method, params object[] arguments) =>
        typeof(PetWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, arguments);
}
