using System.Reflection;
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
        }
        finally { console.Close(); }
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
