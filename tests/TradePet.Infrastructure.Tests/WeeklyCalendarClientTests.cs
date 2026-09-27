using TradePet.Core.Domain;
using TradePet.Infrastructure.Mt4;
using Xunit;

namespace TradePet.Infrastructure.Tests;

public sealed class WeeklyCalendarClientTests
{
    [Fact]
    public void Parse_UsesExplicitTimezoneStableIdsAndPreservesMissingActualValues()
    {
        const string json = """
            [{"title":"CPI y/y","country":"USD","date":"2026-09-27T08:30:00-04:00","impact":"High","forecast":"2.5%","previous":"2.6%"},
             {"title":"Jobs","country":"USD","date":"2026-09-27T09:30:00-04:00","impact":"Medium","forecast":"125K","previous":"<100K"}]
            """;
        var result = WeeklyCalendarClient.Parse(json);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 12, 30, 0, TimeSpan.Zero), result[0].ScheduledAtUtc);
        Assert.Equal(EconomicEventImportance.High, result[0].Importance);
        Assert.Equal("PERCENT", result[0].Unit);
        Assert.Equal(2.5m, result[0].ForecastValue);
        Assert.Equal(125000m, result[1].ForecastValue);
        Assert.Null(result[1].PreviousValue);
        Assert.All(result, item => Assert.Null(item.ActualValue));
        Assert.Equal(result.Select(e => e.ValueId), WeeklyCalendarClient.Parse(json).Select(e => e.ValueId));
    }

    [Fact]
    public void Parse_RejectsMissingTimezoneAndDeduplicatesRepeatedEvents()
    {
        const string item = """{"title":"GDP","country":"GBP","date":"2026-09-27T08:30:00","impact":"Low"}""";
        Assert.Throws<InvalidDataException>(() => WeeklyCalendarClient.Parse("[" + item + "]"));
        var valid = item.Replace("08:30:00", "08:30:00Z", StringComparison.Ordinal);
        Assert.Single(WeeklyCalendarClient.Parse("[" + valid + "," + valid + "]"));
    }
}
