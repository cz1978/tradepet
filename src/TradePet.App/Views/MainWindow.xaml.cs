using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TradePet.App.Runtime;
using TradePet.App.ViewModels;
using TextBox = System.Windows.Controls.TextBox;

namespace TradePet.App.Views;

public partial class MainWindow : Window
{
    private static readonly GuideStep[] GuideSteps =
    [
        new(0, "先确认交易状态", "先确认终端、桥接和账户已连接，再看今日盈亏与持仓。历史同步或采样未就绪时，缺失指标会显示“—”。", TargetName: "GuideConnectionStatus"),
        new(1, "交易前定好边界", "在这组设置里写下今日盈亏目标、交易次数和持仓上限，再保存。规则按当前账户的交易服务器日期生效，只发提醒；具体单位和填写要求可悬停查看。", TargetName: "GuideDailyBoundaries"),
        new(1, "把交易想法记录下来", "手填入场区、止损和目标，保存结构化计划，用于风险倍数和后续交易匹配。也可记录或导入终端图表对象，再选择用途；这两种计划分别保存。", TargetName: "GuideStructuredForm"),
        new(2, "盘中留意风险", "结合桌宠提醒和亏损区域，检查是否重复进入亏损位置、偏离计划或超过今日边界。这里帮助你发现问题，交易仍由你在终端执行。", TargetName: "GuideLossHeader"),
        new(3, "交易后做复盘", "查出要回看的交易，先核对事实，再记录原因和下一次动作，最后从统计中找重复问题。复盘页有单独的简短流程引导。", TargetName: "GuideReviewHeader"),
        new(4, "需要时回看过程", "时间线把开平仓、止损变化和风险提醒串在一起，帮助你还原当时发生了什么，可与单笔复盘一起查看。", TargetName: "GuideTimelineHeader"),
        new(5, "按习惯调整使用方式", "在设置里调整桌宠、提醒和日报并保存。专注模式减少日常闲聊，重要风险提醒仍保留；收起控制台后桌宠会继续监控。", TargetName: "GuideSettingsHeader"),
    ];

    private static readonly GuideStep[] ReviewGuideSteps =
    [
        new(3, "查出要复盘的交易", "选周期、品种和方向后查询；总览里可继续筛选策略和标签。统计按平仓日选取完整交易，未平仓交易不计入；先看数据质量，缺失数据不能当作零。", 0, "GuideReviewRange"),
        new(3, "逐笔还原，再写结论", "从左侧打开一笔交易，核对成交、计划和过程，写总结及下一次动作，再保存草稿。保存至少一项适用的执行评价后可标记已复盘；截图和行情回放按需要补充，日历与日记用于记录当天整体过程。", 2, "ReviewGuideTradeEditor"),
        new(3, "从分析里找重复问题", "按策略、方向或执行情况比较表现，同时核对样本量和风险数据覆盖。确认哪些问题反复出现，再写周期总结和下一周期动作；R、MAE/MFE 或净值采样缺失时保持未知。", 3, "ReviewGuideAnalysis"),
        new(3, "把结论变成下一次行动", "把复盘结论整理成策略规则和一个可检查的改进目标，设定观察窗口，之后回来核对效果。没成交的机会记到“机会记录”；需要留档或分享时，再使用该页的导出和备份。", 4, "ReviewGuideImprovement"),
    ];

    private readonly MainViewModel _viewModel;
    private bool _autoGuideWhenShown;
    private bool _guideActive;
    private int _guideIndex;
    private GuideStep[] _activeGuideSteps = GuideSteps;

    public bool AllowClose { get; set; }
    public Func<Task>? SaveGuideCompletionAsync { get; set; }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) QueueAutomaticGuide();
            else CancelGuide();
        };
        MainRoot.SizeChanged += (_, _) => UpdateGuideLayout();
        PreviewKeyDown += (_, e) =>
        {
            if (!_guideActive || e.Key != Key.Escape) return;
            CompleteGuide();
            e.Handled = true;
        };
    }

    public void ShowPage(int pageIndex)
    {
        MainTabs.SelectedIndex = Math.Clamp(pageIndex, 0, MainTabs.Items.Count - 1);
    }

    public void EnableGuideOnFirstOpen()
    {
        _autoGuideWhenShown = !_viewModel.ConsoleGuideCompleted;
        QueueAutomaticGuide();
    }

    private void QueueAutomaticGuide()
    {
        if (!IsVisible || !_autoGuideWhenShown || _guideActive) return;
        if (IsLoaded)
        {
            BeginGuide();
            return;
        }
        Dispatcher.InvokeAsync(() =>
        {
            if (IsVisible && _autoGuideWhenShown && !_viewModel.ConsoleGuideCompleted && !_guideActive)
                BeginGuide();
        }, DispatcherPriority.Loaded);
    }

    private void BeginGuide(bool review = false)
    {
        _activeGuideSteps = review ? ReviewGuideSteps : GuideSteps;
        _autoGuideWhenShown = false;
        _guideActive = true;
        _guideIndex = 0;
        GuideOverlay.Visibility = Visibility.Visible;
        ShowGuideStep();
    }

    private void ShowGuideStep()
    {
        var step = _activeGuideSteps[_guideIndex];
        ShowPage(step.PageIndex);
        if (step.WorkspaceTabIndex is { } tabIndex) ReviewWorkspace.SelectGuideTab(tabIndex);
        GuideProgress.Text = $"{_guideIndex + 1} / {_activeGuideSteps.Length}";
        GuideTitle.Text = step.Title;
        GuideDescription.Text = step.Description;
        GuidePrevious.IsEnabled = _guideIndex > 0;
        GuideNext.Content = _guideIndex == _activeGuideSteps.Length - 1 ? "完成" : "下一步";
        MainRoot.UpdateLayout();
        GetGuideTarget().BringIntoView();
        MainRoot.UpdateLayout();
        UpdateGuideLayout();
        Dispatcher.InvokeAsync(UpdateGuideLayout, DispatcherPriority.Loaded);
    }

    private FrameworkElement GetGuideTarget()
    {
        var name = _activeGuideSteps[_guideIndex].TargetName!;
        return FindName(name) as FrameworkElement
            ?? ReviewWorkspace.FindName(name) as FrameworkElement
            ?? GuideReviewHeader;
    }

    private void UpdateGuideLayout()
    {
        if (!_guideActive || GuideOverlay.ActualWidth <= 0 || GuideOverlay.ActualHeight <= 0) return;
        var target = GetGuideTarget();
        if (!target.IsVisible || target.ActualWidth <= 0 || target.ActualHeight <= 0)
            target = NavigationList.ItemContainerGenerator.ContainerFromIndex(_activeGuideSteps[_guideIndex].PageIndex)
                as FrameworkElement ?? NavigationList;

        Rect bounds;
        try
        {
            bounds = target.TransformToAncestor(MainRoot)
                .TransformBounds(new Rect(new System.Windows.Point(), target.RenderSize));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        bounds.Inflate(5, 5);
        bounds.Intersect(new Rect(0, 0, GuideOverlay.ActualWidth, GuideOverlay.ActualHeight));
        if (bounds.IsEmpty) return;

        var shade = new GeometryGroup { FillRule = FillRule.EvenOdd };
        shade.Children.Add(new RectangleGeometry(new Rect(0, 0, GuideOverlay.ActualWidth, GuideOverlay.ActualHeight)));
        shade.Children.Add(new RectangleGeometry(bounds, 9, 9));
        GuideShade.Data = shade;
        GuideHighlight.Width = bounds.Width;
        GuideHighlight.Height = bounds.Height;
        Canvas.SetLeft(GuideHighlight, bounds.Left);
        Canvas.SetTop(GuideHighlight, bounds.Top);

        GuideCard.Measure(new System.Windows.Size(GuideCard.Width, double.PositiveInfinity));
        var cardHeight = GuideCard.DesiredSize.Height;
        var maxX = Math.Max(12, GuideOverlay.ActualWidth - GuideCard.Width - 12);
        var maxY = Math.Max(12, GuideOverlay.ActualHeight - cardHeight - 12);
        double x;
        double y;
        if (bounds.Right + GuideCard.Width + 24 <= GuideOverlay.ActualWidth)
        {
            x = bounds.Right + 12;
            y = Math.Clamp(bounds.Top, 12, maxY);
        }
        else if (bounds.Left - GuideCard.Width - 24 >= 0)
        {
            x = bounds.Left - GuideCard.Width - 12;
            y = Math.Clamp(bounds.Top, 12, maxY);
        }
        else
        {
            x = Math.Clamp(bounds.Left, 12, maxX);
            y = bounds.Bottom + cardHeight + 24 <= GuideOverlay.ActualHeight
                ? bounds.Bottom + 12
                : Math.Clamp(bounds.Top - cardHeight - 12, 12, maxY);
        }
        Canvas.SetLeft(GuideCard, x);
        Canvas.SetTop(GuideCard, y);
    }

    private void CancelGuide()
    {
        if (!_guideActive) return;
        _guideActive = false;
        GuideOverlay.Visibility = Visibility.Collapsed;
        _autoGuideWhenShown = !_viewModel.ConsoleGuideCompleted;
    }

    private async void CompleteGuide()
    {
        CancelGuide();
        if (_activeGuideSteps == ReviewGuideSteps) return;
        _autoGuideWhenShown = false;
        if (_viewModel.ConsoleGuideCompleted) return;
        _viewModel.ConsoleGuideCompleted = true;
        try
        {
            if (SaveGuideCompletionAsync is not null) await SaveGuideCompletionAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Unable to save console guide completion: {ex}");
            _viewModel.DiagnosticText = "使用引导的完成状态未能保存，下次启动可能再次显示。";
        }
    }

    private void StartGuide_Click(object sender, RoutedEventArgs e) => BeginGuide();
    private void StartReviewGuide_Click(object sender, RoutedEventArgs e) => BeginGuide(review: true);

    private void SavePlan_Click(object sender, RoutedEventArgs e)
    {
        var fields = new[]
        {
            DailyTargetInput, DailyLossInput, MaximumTradesInput, MaximumLotInput,
            LossZoneToleranceInput, StopLossReminderSecondsInput,
        };
        string? error = null;
        if (!PositiveOptional(DailyTargetInput) || !PositiveOptional(DailyLossInput) ||
                 !PositiveOptional(MaximumLotInput))
            error = "盈利目标、亏损上限和持仓上限请填正数；不用的项目请留空。";
        else if (!PositiveOptionalInteger(MaximumTradesInput))
            error = "最多交易次数请填正整数；不用时留空。";
        else if (!PositiveRequired(LossZoneToleranceInput))
            error = "亏损区半径请填大于 0 的价格跳动数。";
        else if (!PositiveRequiredInteger(StopLossReminderSecondsInput))
            error = "无止损提醒的等待时间请填正整数秒。";
        if (error is null)
        {
            foreach (var field in fields)
                field.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (fields.Any(Validation.GetHasError))
                error = "请检查数字格式：金额和手数填数字，交易次数与等待秒数填整数。";
        }

        PlanValidationText.Text = error ?? string.Empty;
        PlanValidationText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        if (error is null && _viewModel.SaveDailyPlanCommand.CanExecute(null))
            _viewModel.SaveDailyPlanCommand.Execute(null);
    }

    private void SaveStructuredPlan_Click(object sender, RoutedEventArgs e)
    {
        var fields = new[]
        {
            StructuredSymbolInput, StructuredReferenceInput, StructuredLowInput,
            StructuredHighInput, StructuredStopInput, StructuredTargetInput,
            StructuredStrategyInput, StructuredSetupInput, StructuredTagsInput, StructuredNotesInput,
        };
        foreach (var field in fields)
            field.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (_viewModel.CreateStructuredPlanCommand.CanExecute(null))
            _viewModel.CreateStructuredPlanCommand.Execute(null);
    }

    private static bool PositiveOptional(TextBox input) =>
        string.IsNullOrWhiteSpace(input.Text) || PositiveRequired(input);

    private static bool PositiveRequired(TextBox input) =>
        decimal.TryParse(input.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) && value > 0m;

    private static bool PositiveOptionalInteger(TextBox input) =>
        string.IsNullOrWhiteSpace(input.Text) || PositiveRequiredInteger(input);

    private static bool PositiveRequiredInteger(TextBox input) =>
        int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value > 0;

    private void PreviousGuide_Click(object sender, RoutedEventArgs e)
    {
        if (_guideIndex == 0) return;
        _guideIndex--;
        ShowGuideStep();
    }

    private void NextGuide_Click(object sender, RoutedEventArgs e)
    {
        if (_guideIndex == _activeGuideSteps.Length - 1) CompleteGuide();
        else
        {
            _guideIndex++;
            ShowGuideStep();
        }
    }

    private void SkipGuide_Click(object sender, RoutedEventArgs e) => CompleteGuide();

    protected override void OnClosing(CancelEventArgs e)
    {
        CancelGuide();
        if (AllowClose)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void HideWindow_Click(object sender, RoutedEventArgs e) => Hide();

    private sealed record GuideStep(int PageIndex, string Title, string Description,
        int? WorkspaceTabIndex = null, string? TargetName = null);
}
