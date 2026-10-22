using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TradePet.Core.Domain;
using TradePet.Core.Localization;
using TradePet.Core.Review;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace TradePet.App.Controls;

public sealed class TradeReplayChart : FrameworkElement
{
    private static FrameworkPropertyMetadata Render(object? value = null) => new(value, FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty FrameProperty = DependencyProperty.Register(nameof(Frame), typeof(ReplayFrame), typeof(TradeReplayChart), Render());
    public static readonly DependencyProperty ServerOffsetProperty = DependencyProperty.Register(nameof(ServerOffset), typeof(int), typeof(TradeReplayChart), Render(0));
    public static readonly DependencyProperty FromUtcProperty = DependencyProperty.Register(nameof(FromUtc), typeof(DateTimeOffset?), typeof(TradeReplayChart), Render());
    public static readonly DependencyProperty ToUtcProperty = DependencyProperty.Register(nameof(ToUtc), typeof(DateTimeOffset?), typeof(TradeReplayChart), Render());
    public static readonly DependencyProperty TimeframeProperty = DependencyProperty.Register(nameof(Timeframe), typeof(string), typeof(TradeReplayChart), Render("M5"));
    public static readonly DependencyProperty DefaultWindowMinutesProperty = DependencyProperty.Register(nameof(DefaultWindowMinutes), typeof(int), typeof(TradeReplayChart), Render(400));
    public static readonly DependencyProperty WindowRangeProperty = DependencyProperty.Register(nameof(WindowRange), typeof(ReplayChartWindow), typeof(TradeReplayChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty CrosshairUtcProperty = DependencyProperty.Register(nameof(CrosshairUtc), typeof(DateTimeOffset?), typeof(TradeReplayChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public ReplayFrame? Frame { get => (ReplayFrame?)GetValue(FrameProperty); set => SetValue(FrameProperty, value); }
    public int ServerOffset { get => (int)GetValue(ServerOffsetProperty); set => SetValue(ServerOffsetProperty, value); }
    public DateTimeOffset? FromUtc { get => (DateTimeOffset?)GetValue(FromUtcProperty); set => SetValue(FromUtcProperty, value); }
    public DateTimeOffset? ToUtc { get => (DateTimeOffset?)GetValue(ToUtcProperty); set => SetValue(ToUtcProperty, value); }
    public string Timeframe { get => (string)GetValue(TimeframeProperty); set => SetValue(TimeframeProperty, value); }
    public int DefaultWindowMinutes { get => (int)GetValue(DefaultWindowMinutesProperty); set => SetValue(DefaultWindowMinutesProperty, value); }
    public ReplayChartWindow? WindowRange { get => (ReplayChartWindow?)GetValue(WindowRangeProperty); set => SetValue(WindowRangeProperty, value); }
    public DateTimeOffset? CrosshairUtc { get => (DateTimeOffset?)GetValue(CrosshairUtcProperty); set => SetValue(CrosshairUtcProperty, value); }
    private Rect Plot => new(12, 48, Math.Max(1, ActualWidth - 110), Math.Max(1, ActualHeight - 78));
    private Point? _pointer;
    private Point? _dragStart;
    private ReplayChartWindow? _dragWindow;

    private ReplayChartWindow Bounds()
    {
        var start = FromUtc ?? Frame?.VisibleBars.FirstOrDefault()?.OpenedAtUtc ?? Frame?.CursorUtc ?? DateTimeOffset.UtcNow;
        var end = ToUtc ?? Frame?.CursorUtc ?? start.AddHours(1);
        return new(start, end <= start ? start.AddMinutes(1) : end);
    }

    private ReplayChartWindow VisibleWindow()
    {
        var bounds = Bounds();
        var end = Frame?.CursorUtc ?? bounds.ToUtc;
        return (WindowRange ?? new ReplayChartWindow(end.AddMinutes(-Math.Max(1, DefaultWindowMinutes)), end)).Clamp(bounds.FromUtc, bounds.ToUtc);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Frame is null || !Plot.Contains(e.GetPosition(this))) return;
        var bounds = Bounds();
        SetCurrentValue(WindowRangeProperty, VisibleWindow().Zoom(e.Delta > 0 ? 0.8 : 1.25,
            (e.GetPosition(this).X - Plot.Left) / Plot.Width, bounds.FromUtc, bounds.ToUtc));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Frame is null || !Plot.Contains(e.GetPosition(this))) return;
        if (e.ClickCount == 2) { SetCurrentValue(WindowRangeProperty, null); e.Handled = true; return; }
        _dragStart = e.GetPosition(this); _dragWindow = VisibleWindow();
        CaptureMouse(); e.Handled = true;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _pointer = e.GetPosition(this);
        if (_dragStart is { } start && _dragWindow is { } window && e.LeftButton == MouseButtonState.Pressed)
        {
            var bounds = Bounds();
            SetCurrentValue(WindowRangeProperty, window.Pan((start.X - _pointer.Value.X) / Plot.Width, bounds.FromUtc, bounds.ToUtc));
        }
        if (Plot.Contains(_pointer.Value))
            SetCurrentValue(CrosshairUtcProperty, VisibleWindow().At((_pointer.Value.X - Plot.Left) / Plot.Width));
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragStart is null) return;
        _dragStart = null; _dragWindow = null; ReleaseMouseCapture(); e.Handled = true;
    }

    protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e)
    { base.OnLostMouseCapture(e); _dragStart = null; _dragWindow = null; }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e); _pointer = null;
        if (!IsMouseCaptured) SetCurrentValue(CrosshairUtcProperty, null);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (ActualWidth < 180 || ActualHeight < 120) return;
        void Label(string text, double x, double y, System.Windows.Media.Brush? color = null)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10, color ?? Brushes.LightGray, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = Math.Max(1, ActualWidth - x - 4), MaxTextHeight = 18, Trimming = TextTrimming.CharacterEllipsis };
            drawing.DrawText(formatted, new Point(x, y));
        }
        if (Frame is not { } frame) { Label(UiText.Get("FunctionalReplayNoPrices"), 12, 12); return; }
        var window = VisibleWindow(); var plot = Plot;
        var minutes = Timeframe switch { "M1" => 1, "M15" => 15, "H1" => 60, _ => 5 };
        var bars = frame.VisibleBars.Where(bar => bar.OpenedAtUtc.AddMinutes(minutes) <= frame.CursorUtc &&
            bar.OpenedAtUtc >= window.FromUtc && bar.OpenedAtUtc <= window.ToUtc).ToArray();
        var ticks = frame.VisibleTicks.Where(tick => tick.OccurredAtUtc >= window.FromUtc && tick.OccurredAtUtc <= window.ToUtc && tick.OccurredAtUtc <= frame.CursorUtc).ToArray();
        var deals = frame.VisibleDeals.Where(deal => deal.OccurredAtUtc >= window.FromUtc && deal.OccurredAtUtc <= window.ToUtc && deal.OccurredAtUtc <= frame.CursorUtc).ToArray();
        var sample = frame.VisiblePnlSamples.LastOrDefault(item => item.CapturedAtUtc <= frame.CursorUtc);
        var stop = sample is null ? frame.VisiblePlan?.StopPrice : sample.StopLoss;
        var target = sample is null ? frame.VisiblePlan?.TargetPrice : sample.TakeProfit;
        var prices = bars.SelectMany(bar => new[] { bar.High, bar.Low }).Concat(ticks.Select(tick => tick.Bid > 0 ? tick.Bid : tick.Last))
            .Concat(deals.Select(deal => deal.Price)).Where(price => price > 0).ToList();
        Label($"{Timeframe} · {frame.Coverage} · {UiText.Translate(frame.Message)}", 12, 5);
        if (prices.Count == 0) { Label(UiText.Get("FunctionalReplayNoPrices"), 12, 26); return; }
        if (stop is > 0) prices.Add(stop.Value);
        if (target is > 0) prices.Add(target.Value);
        var low = prices.Min(); var high = prices.Max();
        var padding = Math.Max((high - low) * 0.08m, Math.Max(Math.Abs(high) * 0.00001m, 0.00000001m));
        low -= padding; high += padding;
        double X(DateTimeOffset at) => plot.Left + (at - window.FromUtc).TotalSeconds / window.Span.TotalSeconds * plot.Width;
        double Y(decimal price) => plot.Bottom - (double)((price - low) / (high - low)) * plot.Height;
        string Time(DateTimeOffset at) => at.ToOffset(TimeSpan.FromSeconds(ServerOffset)).ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        for (var i = 0; i <= 4; i++)
        {
            var price = low + (high - low) * i / 4;
            drawing.DrawLine(new Pen(Brushes.DimGray, 0.3), new Point(plot.Left, Y(price)), new Point(plot.Right, Y(price)));
            Label(price.ToString("0.########"), plot.Right + 5, Y(price) - 7);
        }
        drawing.PushClip(new RectangleGeometry(plot));
        var bodyWidth = Math.Clamp(minutes * 60 / window.Span.TotalSeconds * plot.Width * 0.7, 1, 20);
        foreach (var bar in bars)
        {
            var brush = bar.Close >= bar.Open ? Brushes.MediumAquamarine : Brushes.Salmon;
            var x = X(bar.OpenedAtUtc);
            drawing.DrawLine(new Pen(brush, 1), new Point(x, Y(bar.High)), new Point(x, Y(bar.Low)));
            drawing.DrawRectangle(brush, null, new Rect(x - bodyWidth / 2, Math.Min(Y(bar.Open), Y(bar.Close)), bodyWidth,
                Math.Max(1, Math.Abs(Y(bar.Close) - Y(bar.Open)))));
        }
        MarketTick? previous = null;
        foreach (var tick in ticks)
        {
            var price = tick.Bid > 0 ? tick.Bid : tick.Last;
            if (price <= 0) { previous = null; continue; }
            if (previous is { } before && tick.OccurredAtUtc - before.OccurredAtUtc <= TimeSpan.FromSeconds(5))
                drawing.DrawLine(new Pen(Brushes.LightSkyBlue, 1), new Point(X(before.OccurredAtUtc), Y(before.Bid > 0 ? before.Bid : before.Last)), new Point(X(tick.OccurredAtUtc), Y(price)));
            previous = tick;
        }
        void Level(decimal? price, System.Windows.Media.Brush brush)
        { if (price is > 0) drawing.DrawLine(new Pen(brush, 1) { DashStyle = DashStyles.Dash }, new Point(plot.Left, Y(price.Value)), new Point(plot.Right, Y(price.Value))); }
        Level(stop, Brushes.Salmon); Level(target, Brushes.MediumAquamarine);
        foreach (var deal in deals)
            drawing.DrawEllipse(deal.EntryKind == DealEntryKind.In ? Brushes.Gold : Brushes.Violet, new Pen(Brushes.White, 0.5), new Point(X(deal.OccurredAtUtc), Y(deal.Price)), 4, 4);
        drawing.DrawLine(new Pen(Brushes.White, 1) { DashStyle = DashStyles.Dot }, new Point(X(frame.CursorUtc), plot.Top), new Point(X(frame.CursorUtc), plot.Bottom));
        if (CrosshairUtc is { } at && at >= window.FromUtc && at <= window.ToUtc)
        {
            drawing.DrawLine(new Pen(Brushes.LightSteelBlue, 0.8) { DashStyle = DashStyles.Dash }, new Point(X(at), plot.Top), new Point(X(at), plot.Bottom));
            if (_pointer is { } pointer && plot.Contains(pointer))
                drawing.DrawLine(new Pen(Brushes.LightSteelBlue, 0.8) { DashStyle = DashStyles.Dash }, new Point(plot.Left, pointer.Y), new Point(plot.Right, pointer.Y));
        }
        drawing.Pop();
        if (CrosshairUtc is { } hover)
        {
            var bar = frame.VisibleBars.LastOrDefault(item => item.OpenedAtUtc <= hover && hover < item.OpenedAtUtc.AddMinutes(minutes) && item.OpenedAtUtc.AddMinutes(minutes) <= frame.CursorUtc);
            var description = bar is null ? UiText.Get("ReplayNoClosedBar") : $"O {bar.Open:0.########}  H {bar.High:0.########}  L {bar.Low:0.########}  C {bar.Close:0.########}";
            if (_pointer is { } p && plot.Contains(p)) description += $" · P {low + (decimal)((plot.Bottom - p.Y) / plot.Height) * (high - low):0.########}";
            Label($"{Time(hover)} · {description}", 12, 25);
        }
        Label(Time(window.FromUtc), plot.Left, plot.Bottom + 8);
        Label(Time(window.ToUtc), Math.Max(plot.Left, plot.Right - 122), plot.Bottom + 8);
    }
}
