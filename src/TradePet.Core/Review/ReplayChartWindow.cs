namespace TradePet.Core.Review;

public sealed record ReplayChartWindow(DateTimeOffset FromUtc, DateTimeOffset ToUtc)
{
    public TimeSpan Span => ToUtc - FromUtc;
    public DateTimeOffset At(double fraction) => FromUtc.AddTicks((long)(Span.Ticks * Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1)));

    public ReplayChartWindow Clamp(DateTimeOffset minimum, DateTimeOffset maximum)
    {
        if (maximum <= minimum) maximum = minimum.AddMinutes(1);
        var available = (maximum - minimum).Ticks;
        var length = Math.Clamp(Span.Ticks, Math.Min(TimeSpan.TicksPerMinute, available), available);
        var from = Math.Clamp(FromUtc.UtcTicks, minimum.UtcTicks, maximum.UtcTicks - length);
        return new(new DateTimeOffset(from, TimeSpan.Zero), new DateTimeOffset(from + length, TimeSpan.Zero));
    }

    public ReplayChartWindow Zoom(double factor, double anchor, DateTimeOffset minimum, DateTimeOffset maximum)
    {
        if (maximum <= minimum) maximum = minimum.AddMinutes(1);
        var current = Clamp(minimum, maximum);
        anchor = Math.Clamp(double.IsFinite(anchor) ? anchor : 0.5, 0, 1);
        factor = double.IsFinite(factor) ? Math.Clamp(factor, 0.1, 10) : 1;
        var length = (long)Math.Clamp(current.Span.Ticks * factor,
            Math.Min(TimeSpan.TicksPerMinute, (maximum - minimum).Ticks), (maximum - minimum).Ticks);
        var from = Math.Clamp(current.At(anchor).UtcTicks - (long)(length * anchor), minimum.UtcTicks, maximum.UtcTicks - length);
        return new(new DateTimeOffset(from, TimeSpan.Zero), new DateTimeOffset(from + length, TimeSpan.Zero));
    }

    public ReplayChartWindow Pan(double fraction, DateTimeOffset minimum, DateTimeOffset maximum)
    {
        if (maximum <= minimum) maximum = minimum.AddMinutes(1);
        var current = Clamp(minimum, maximum);
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, -1, 1) : 0;
        var from = Math.Clamp(current.FromUtc.UtcTicks + (long)(current.Span.Ticks * fraction), minimum.UtcTicks, maximum.UtcTicks - current.Span.Ticks);
        return new(new DateTimeOffset(from, TimeSpan.Zero), new DateTimeOffset(from + current.Span.Ticks, TimeSpan.Zero));
    }
}
