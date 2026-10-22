using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TradePet.Core.Domain;
using TradePet.Core.Localization;
using TradePet.Core.Review;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace TradePet.App.Controls;

public sealed class HoldingProcessChart : FrameworkElement
{
    private ReplayFrame? _analyzedFrame;
    private HoldingProcessAnalysis? _analysis;
    private static FrameworkPropertyMetadata Render(object? value = null) => new(value, FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty FrameProperty = DependencyProperty.Register(nameof(Frame), typeof(ReplayFrame), typeof(HoldingProcessChart), Render());
    public static readonly DependencyProperty WindowRangeProperty = DependencyProperty.Register(nameof(WindowRange), typeof(ReplayChartWindow), typeof(HoldingProcessChart), Render());
    public static readonly DependencyProperty CrosshairUtcProperty = DependencyProperty.Register(nameof(CrosshairUtc), typeof(DateTimeOffset?), typeof(HoldingProcessChart), Render());
    public static readonly DependencyProperty DefaultWindowMinutesProperty = DependencyProperty.Register(nameof(DefaultWindowMinutes), typeof(int), typeof(HoldingProcessChart), Render(400));
    public static readonly DependencyProperty FromUtcProperty = DependencyProperty.Register(nameof(FromUtc), typeof(DateTimeOffset?), typeof(HoldingProcessChart), Render());
    public static readonly DependencyProperty ToUtcProperty = DependencyProperty.Register(nameof(ToUtc), typeof(DateTimeOffset?), typeof(HoldingProcessChart), Render());
    public static readonly DependencyProperty ServerOffsetProperty = DependencyProperty.Register(nameof(ServerOffset), typeof(int), typeof(HoldingProcessChart), Render(0));
    public ReplayFrame? Frame { get => (ReplayFrame?)GetValue(FrameProperty); set => SetValue(FrameProperty, value); }
    public ReplayChartWindow? WindowRange { get => (ReplayChartWindow?)GetValue(WindowRangeProperty); set => SetValue(WindowRangeProperty, value); }
    public DateTimeOffset? CrosshairUtc { get => (DateTimeOffset?)GetValue(CrosshairUtcProperty); set => SetValue(CrosshairUtcProperty, value); }
    public int DefaultWindowMinutes { get => (int)GetValue(DefaultWindowMinutesProperty); set => SetValue(DefaultWindowMinutesProperty, value); }
    public DateTimeOffset? FromUtc { get => (DateTimeOffset?)GetValue(FromUtcProperty); set => SetValue(FromUtcProperty, value); }
    public DateTimeOffset? ToUtc { get => (DateTimeOffset?)GetValue(ToUtcProperty); set => SetValue(ToUtcProperty, value); }
    public int ServerOffset { get => (int)GetValue(ServerOffsetProperty); set => SetValue(ServerOffsetProperty, value); }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (ActualWidth < 180 || ActualHeight < 100) return;
        void Label(string text, double x, double y, System.Windows.Media.Brush? color = null)
        {
            drawing.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10, color ?? Brushes.LightGray, VisualTreeHelper.GetDpi(this).PixelsPerDip)
                { MaxTextWidth = Math.Max(1, ActualWidth - x - 4), MaxTextHeight = 18, Trimming = TextTrimming.CharacterEllipsis }, new Point(x, y));
        }
        Label(UiText.Get("ReplayHoldingLegend"), 12, 4);
        if (Frame is not { } frame) { Label(UiText.Get("ReplayHoldingNoSamples"), 12, 30); return; }
        if (!ReferenceEquals(_analyzedFrame, frame))
        {
            _analysis = HoldingProcessAnalysis.FromFrame(frame);
            _analyzedFrame = frame;
        }
        var analysis = _analysis!;
        var from = FromUtc ?? analysis.Samples.FirstOrDefault()?.CapturedAtUtc ?? frame.CursorUtc.AddHours(-1);
        var to = ToUtc ?? frame.CursorUtc;
        var window = (WindowRange ?? new ReplayChartWindow(frame.CursorUtc.AddMinutes(-Math.Max(1, DefaultWindowMinutes)), frame.CursorUtc)).Clamp(from, to);
        var samples = analysis.Samples.Where(sample => sample.CapturedAtUtc >= window.FromUtc && sample.CapturedAtUtc <= window.ToUtc).ToArray();
        if (samples.Length == 0) { Label(UiText.Get("ReplayHoldingNoSamples"), 12, 30); return; }
        var values = samples.SelectMany(sample => new[] { sample.NetPnl, sample.RealizedPnl }).Append(0m).ToList();
        if (analysis.FinalNetPnl is { } final) values.Add(final);
        var low = values.Min(); var high = values.Max();
        var padding = Math.Max(1m, (high - low) * 0.08m); low -= padding; high += padding;
        var plot = new Rect(12, 44, ActualWidth - 110, ActualHeight - 70);
        double X(DateTimeOffset at) => plot.Left + (at - window.FromUtc).TotalSeconds / window.Span.TotalSeconds * plot.Width;
        double Y(decimal value) => plot.Bottom - (double)((value - low) / (high - low)) * plot.Height;
        string Time(DateTimeOffset at) => at.ToOffset(TimeSpan.FromSeconds(ServerOffset)).ToString("MM-dd HH:mm:ss");
        for (var i = 0; i <= 4; i++)
        {
            var amount = low + (high - low) * i / 4;
            drawing.DrawLine(new Pen(Brushes.DimGray, 0.3), new Point(plot.Left, Y(amount)), new Point(plot.Right, Y(amount)));
            Label(amount.ToString("0.##"), plot.Right + 5, Y(amount) - 7);
        }
        drawing.PushClip(new RectangleGeometry(plot));
        drawing.DrawLine(new Pen(Brushes.Gray, 0.8), new Point(plot.Left, Y(0)), new Point(plot.Right, Y(0)));
        if (analysis.FinalNetPnl is { } result)
            drawing.DrawLine(new Pen(Brushes.Gold, 1) { DashStyle = DashStyles.Dash }, new Point(plot.Left, Y(result)), new Point(plot.Right, Y(result)));
        PositionPnlSample? previous = null;
        foreach (var sample in samples)
        {
            if (previous is { } before && sample.GapMilliseconds <= 5000 &&
                before.AlgorithmVersion == "position-pnl-v1" && sample.AlgorithmVersion == "position-pnl-v1" &&
                sample.CapturedAtUtc - before.CapturedAtUtc <= TimeSpan.FromSeconds(5))
            {
                drawing.DrawLine(new Pen(Brushes.LightSkyBlue, 1.5), new Point(X(before.CapturedAtUtc), Y(before.NetPnl)), new Point(X(sample.CapturedAtUtc), Y(sample.NetPnl)));
                // Realized P&L changes only when a fill occurs; show steps, not interpolated profit.
                drawing.DrawLine(new Pen(Brushes.MediumAquamarine, 1), new Point(X(before.CapturedAtUtc), Y(before.RealizedPnl)), new Point(X(sample.CapturedAtUtc), Y(before.RealizedPnl)));
                drawing.DrawLine(new Pen(Brushes.MediumAquamarine, 1), new Point(X(sample.CapturedAtUtc), Y(before.RealizedPnl)), new Point(X(sample.CapturedAtUtc), Y(sample.RealizedPnl)));
            }
            drawing.DrawEllipse(Brushes.LightSkyBlue, null, new Point(X(sample.CapturedAtUtc), Y(sample.NetPnl)), 1.5, 1.5);
            previous = sample;
        }
        if (CrosshairUtc is { } at && at >= window.FromUtc && at <= window.ToUtc)
            drawing.DrawLine(new Pen(Brushes.LightSteelBlue, 0.8) { DashStyle = DashStyles.Dash }, new Point(X(at), plot.Top), new Point(X(at), plot.Bottom));
        drawing.Pop();
        if (CrosshairUtc is { } hover)
        {
            var nearest = samples.LastOrDefault(sample => sample.CapturedAtUtc <= hover && hover - sample.CapturedAtUtc <= TimeSpan.FromSeconds(5));
            Label(nearest is null ? UiText.Get("ReplayHoldingNoSampleAtCursor") : $"{Time(nearest.CapturedAtUtc)} · {string.Format(UiText.Get("ReplayHoldingHover"), nearest.NetPnl, nearest.RealizedPnl)}", 12, 23);
        }
        Label(Time(window.FromUtc), plot.Left, plot.Bottom + 7);
        Label(Time(window.ToUtc), Math.Max(plot.Left, plot.Right - 122), plot.Bottom + 7);
    }
}
