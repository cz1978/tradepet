using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using TradePet.App.ViewModels;
using TradePet.App.ViewModels.Review;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace TradePet.App.Controls;

/// <summary>Time-based review chart; retained drawing keeps pointer interaction cheap.</summary>
public sealed class ReviewPerformanceChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(nameof(Series),
        typeof(ReviewChartSeries), typeof(ReviewPerformanceChart), new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender, (sender, _) => ((ReviewPerformanceChart)sender).ResetDrawing()));

    private static readonly Typeface Font = new("Microsoft YaHei UI");
    private static readonly Brush Muted = BrushFor("#8D99A8");
    private static readonly Brush Text = BrushFor("#F2F5F4");
    private static readonly Pen GridPen = new(BrushFor("#263142"), 0.5);
    private DrawingGroup? _drawing;
    private Point[] _positions = [];
    private Rect _plot;
    private int _hovered = -1;

    public ReviewPerformanceChart()
    {
        Focusable = true;
        ClipToBounds = true;
        SizeChanged += (_, _) => ResetDrawing();
    }

    public ReviewChartSeries? Series
    {
        get => (ReviewChartSeries?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    private void ResetDrawing()
    {
        _positions = [];
        _hovered = -1;
        ToolTip = null;
        Cursor = Cursors.Arrow;
        if (ActualWidth >= 160 && ActualHeight >= 100) BuildDrawing();
        else _drawing = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (ActualWidth < 160 || ActualHeight < 100) return;
        if (_drawing is null) BuildDrawing();
        context.DrawDrawing(_drawing!);
        if (Series is not { } series || _hovered < 0 || _hovered >= _positions.Length) return;
        var point = _positions[_hovered];
        context.DrawLine(new Pen(Muted, 0.6), new Point(point.X, _plot.Top), new Point(point.X, _plot.Bottom));
        context.DrawEllipse(BrushFor(series.Color), new Pen(Text, 1.5), point, 4, 4);
        var selected = series.Points[_hovered];
        var detail = $"{selected.At:yyyy-MM-dd HH:mm:ss}   {selected.Detail}";
        DrawText(context, detail, new Point(_plot.Left, ActualHeight - 25), Text, 11, _plot.Width);
    }

    private void BuildDrawing()
    {
        _drawing ??= new DrawingGroup();
        using var context = _drawing.Open();
        if (Series is not { Points.Count: > 0 } series)
        {
            DrawText(context, Series?.EmptyMessage ?? "选择账户并同步历史后查看曲线。",
                new Point(20, ActualHeight / 2 - 25), Muted, 12, ActualWidth - 40, 60);
            return;
        }
        _plot = new Rect(76, 15, ActualWidth - 92, ActualHeight - 72);
        var minimum = series.Points.Min(point => point.Value);
        var maximum = series.Points.Max(point => point.Value);
        if (series.ReferenceValue is { } reference)
        {
            minimum = Math.Min(minimum, reference);
            maximum = Math.Max(maximum, reference);
        }
        var padding = maximum == minimum ? Math.Max(1m, Math.Abs(maximum) * 0.02m) : (maximum - minimum) * 0.08m;
        minimum -= padding;
        maximum += padding;
        var start = series.Points[0].At;
        var finish = series.Points[^1].At;
        var elapsed = Math.Max(1d, (finish - start).TotalSeconds);
        var barSpan = Math.Max(1d, (finish - start).TotalDays + 1d);
        double Y(decimal value) => _plot.Bottom - (double)((value - minimum) / (maximum - minimum)) * _plot.Height;
        // Daily bars and their date ticks share the same half-day padding.
        double X(DateTimeOffset at) => series.IsBar ?
            _plot.Left + ((at - start).TotalDays + 0.5d) / barSpan * _plot.Width :
            finish == start ? _plot.Left + _plot.Width / 2 :
            _plot.Left + (at - start).TotalSeconds / elapsed * _plot.Width;
        _positions = series.Points.Select(point => new Point(X(point.At), Y(point.Value))).ToArray();
        for (var tick = 0; tick <= 4; tick++)
        {
            var value = minimum + (maximum - minimum) * tick / 4m;
            var y = Y(value);
            context.DrawLine(GridPen, new Point(_plot.Left, y), new Point(_plot.Right, y));
            DrawText(context, FormatValue(value), new Point(1, y - 8), Muted, 10, 67);
        }
        if (series.ReferenceValue is { } baseline)
        {
            var referencePen = new Pen(Muted, 0.8) { DashStyle = DashStyles.Dash };
            context.DrawLine(referencePen, new Point(_plot.Left, Y(baseline)), new Point(_plot.Right, Y(baseline)));
        }
        var tickCount = Math.Max(1, Math.Min(4, (int)(_plot.Width / 120)));
        if (series.IsBar) tickCount = Math.Min(tickCount, Math.Max(1, (int)(finish - start).TotalDays));
        for (var tick = 0; tick <= tickCount; tick++)
        {
            if (finish == start && tick > 0) break;
            var at = series.IsBar ? start.AddDays(Math.Round((finish - start).TotalDays * tick / tickCount)) :
                start.AddSeconds((finish - start).TotalSeconds * tick / tickCount);
            var label = series.IsBar || (finish - start).TotalDays >= 1 ? at.ToString("MM-dd") : at.ToString("HH:mm");
            var x = X(at);
            DrawText(context, label, new Point(Math.Clamp(x - 20, _plot.Left, _plot.Right - 42), _plot.Bottom + 9), Muted, 10, 50);
        }
        context.PushClip(new RectangleGeometry(_plot));
        if (series.IsBar)
        {
            var width = Math.Clamp(_plot.Width / barSpan * 0.65d, 0.7d, 28d);
            for (var index = 0; index < _positions.Length; index++)
            {
                var at = _positions[index];
                var zero = Y(0m);
                var top = Math.Min(at.Y, zero);
                context.DrawRectangle(BrushFor(FinancialPalette.For(series.Points[index].Value)), null,
                    new Rect(at.X - width / 2d, top, width, Math.Max(1d, Math.Abs(zero - at.Y))));
            }
        }
        else
        {
            var indices = VisibleIndices();
            var visible = indices.ToArray();
            var line = new StreamGeometry();
            using (var geometry = line.Open())
            {
                var previousIndex = -1;
                for (var position = 0; position < visible.Length; position++)
                {
                    var index = visible[position];
                    var at = _positions[index];
                    if (previousIndex < 0 || series.Points[index].Segment != series.Points[previousIndex].Segment)
                        geometry.BeginFigure(at, false, false);
                    else
                    {
                        var previous = _positions[previousIndex];
                        var width = at.X - previous.X;
                        if (width <= 0d) geometry.LineTo(at, true, false);
                        else
                        {
                            var leftSlope = Tangent(position - 1);
                            var rightSlope = Tangent(position);
                            geometry.BezierTo(new Point(previous.X + width / 3d, previous.Y + leftSlope * width / 3d),
                                new Point(at.X - width / 3d, at.Y - rightSlope * width / 3d), at, true, false);
                        }
                    }
                    previousIndex = index;
                }
            }
            // Monotone cubic interpolation smooths joins without inventing peaks or troughs.
            double Tangent(int position)
            {
                var index = visible[position];
                var at = _positions[index];
                var hasLeft = position > 0 && series.Points[visible[position - 1]].Segment == series.Points[index].Segment;
                var hasRight = position + 1 < visible.Length && series.Points[visible[position + 1]].Segment == series.Points[index].Segment;
                var left = hasLeft ? _positions[visible[position - 1]] : at;
                var right = hasRight ? _positions[visible[position + 1]] : at;
                var leftWidth = at.X - left.X;
                var rightWidth = right.X - at.X;
                var leftSlope = leftWidth > 0d ? (at.Y - left.Y) / leftWidth : 0d;
                var rightSlope = rightWidth > 0d ? (right.Y - at.Y) / rightWidth : 0d;
                if (!hasLeft) return rightSlope;
                if (!hasRight) return leftSlope;
                if (leftWidth <= 0d || rightWidth <= 0d || leftSlope * rightSlope <= 0d) return 0d;
                var leftWeight = 2d * rightWidth + leftWidth;
                var rightWeight = rightWidth + 2d * leftWidth;
                return (leftWeight + rightWeight) / (leftWeight / leftSlope + rightWeight / rightSlope);
            }
            line.Freeze();
            context.DrawGeometry(null, new Pen(BrushFor(series.Color), 1.8), line);
            // Isolated observations remain visible even when every sample is separated by a gap.
            foreach (var index in indices)
                if (indices.Count <= 80 || (index == 0 || series.Points[index].Segment != series.Points[index - 1].Segment) &&
                    (index == _positions.Length - 1 || series.Points[index].Segment != series.Points[index + 1].Segment))
                    context.DrawEllipse(BrushFor(series.Color), null, _positions[index], 1.8, 1.8);
        }
        context.Pop();
        DrawText(context, "悬停查看数值 · 点击查看交易 · ← → 切换节点，Enter 打开", new Point(_plot.Left, ActualHeight - 25), Muted, 10, _plot.Width);
    }

    private IReadOnlyList<int> VisibleIndices()
    {
        if (_positions.Length <= _plot.Width * 2d) return Enumerable.Range(0, _positions.Length).ToArray();
        // Keep both extremes and both endpoints in each pixel bucket, including segment boundaries.
        var result = new SortedSet<int> { 0, _positions.Length - 1 };
        var first = 0;
        while (first < _positions.Length)
        {
            var last = first;
            var min = first;
            var max = first;
            var pixel = (int)_positions[first].X;
            while (last + 1 < _positions.Length && (int)_positions[last + 1].X == pixel &&
                   Series!.Points[last + 1].Segment == Series.Points[first].Segment)
            {
                last++;
                if (_positions[last].Y < _positions[min].Y) min = last;
                if (_positions[last].Y > _positions[max].Y) max = last;
            }
            result.Add(first); result.Add(min); result.Add(max); result.Add(last);
            first = last + 1;
        }
        return result.ToArray();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var mouse = e.GetPosition(this);
        if (!_plot.Contains(mouse) || _positions.Length == 0) { SelectPoint(-1); return; }
        var low = 0;
        var high = _positions.Length - 1;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_positions[middle].X < mouse.X) low = middle + 1;
            else high = middle;
        }
        var candidate = low > 0 && Math.Abs(_positions[low - 1].X - mouse.X) < Math.Abs(_positions[low].X - mouse.X) ? low - 1 : low;
        var distance = double.MaxValue;
        var left = candidate;
        while (left > 0 && Math.Abs(_positions[left - 1].X - mouse.X) <= 6d) left--;
        for (var index = left; index < _positions.Length && (index == left || Math.Abs(_positions[index].X - mouse.X) <= 6d); index++)
        {
            var delta = _positions[index] - mouse;
            if (delta.LengthSquared < distance) { distance = delta.LengthSquared; candidate = index; }
        }
        SelectPoint(candidate);
    }

    private void SelectPoint(int index)
    {
        if (_hovered == index) return;
        _hovered = index;
        var point = index >= 0 ? Series?.Points[index] : null;
        ToolTip = point is null ? null : $"{point.At:yyyy-MM-dd HH:mm:ss}\n{point.Detail}" + (point.OpenCommand is null ? "" : "\n点击查看相关交易");
        Cursor = point?.OpenCommand is null ? Cursors.Arrow : Cursors.Hand;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); SelectPoint(-1); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        OpenPoint();
        e.Handled = true;
    }

    private void OpenPoint()
    {
        if (_hovered >= 0 && Series?.Points[_hovered].OpenCommand is { } command && command.CanExecute(null)) command.Execute(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_positions.Length == 0) return;
        if (e.Key == Key.Left || e.Key == Key.Right)
        {
            SelectPoint(Math.Clamp(_hovered < 0 ? 0 : _hovered + (e.Key == Key.Left ? -1 : 1), 0, _positions.Length - 1));
            e.Handled = true;
        }
        else if (e.Key == Key.Enter) { OpenPoint(); e.Handled = true; }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    private void DrawText(DrawingContext context, string value, Point at, Brush brush, double size, double width, double height = 24)
    {
        var text = new FormattedText(TradePet.Core.Localization.UiText.Translate(value), CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, Font, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(1d, width), MaxTextHeight = height, Trimming = TextTrimming.CharacterEllipsis };
        context.DrawText(text, at);
    }

    private static string FormatValue(decimal value) => Math.Abs(value) >= 1_000_000m ? $"{value / 1_000_000m:0.#}M" :
        Math.Abs(value) >= 10_000m ? $"{value / 1_000m:0.#}k" : value.ToString("0.##");

    private static Brush BrushFor(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
