using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradePet.Core.Domain;

namespace TradePet.Infrastructure.Mt4;

public sealed class WeeklyCalendarClient(HttpClient httpClient)
{
    public const string FeedUrl = "https://nfs.faireconomy.media/ff_calendar_thisweek.json";

    public async Task<IReadOnlyList<EconomicCalendarEvent>> FetchAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(FeedUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024).WaitAsync(cancellationToken);
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public static IReadOnlyList<EconomicCalendarEvent> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var events = new List<EconomicCalendarEvent>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var title = item.GetProperty("title").GetString() ?? "";
            var currency = item.GetProperty("country").GetString() ?? "";
            var date = item.GetProperty("date").GetString() ?? "";
            // Never interpret a timezone-less provider timestamp as local time.
            if (string.IsNullOrWhiteSpace(title) || currency.Length != 3 ||
                !(date.EndsWith('Z') || (date.Length >= 6 && date[^3] == ':' && date[^6] is '+' or '-')) ||
                !DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
                throw new InvalidDataException("Invalid weekly calendar event or timezone.");
            var impact = item.GetProperty("impact").GetString();
            var importance = impact switch
            {
                "High" => EconomicEventImportance.High,
                "Medium" => EconomicEventImportance.Moderate,
                "Low" => EconomicEventImportance.Low,
                _ => EconomicEventImportance.None,
            };
            var previous = item.TryGetProperty("previous", out var prev) ? prev.GetString() ?? "" : "";
            var forecast = item.TryGetProperty("forecast", out var next) ? next.GetString() ?? "" : "";
            var id = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes($"ff|{currency}|{title}|{at.ToUniversalTime():O}"))) & long.MaxValue;
            events.Add(new(id, id, at.ToUniversalTime(), "", "", currency, title,
                impact == "Holiday" ? EconomicEventType.Holiday : EconomicEventType.Event,
                importance, "weeklySchedule", previous.Contains('%') || forecast.Contains('%') ? "PERCENT" : "",
                "", 4, ParseNumber(previous), null, ParseNumber(forecast), null, "",
                "https://www.forexfactory.com/calendar", $"ff-{id}"));
        }
        return events.DistinctBy(item => item.ValueId).OrderBy(item => item.ScheduledAtUtc).ToArray();
    }

    private static decimal? ParseNumber(string text)
    {
        text = text.Trim().Replace(",", "", StringComparison.Ordinal);
        var multiplier = text.EndsWith('K') ? 1_000m : text.EndsWith('M') ? 1_000_000m : text.EndsWith('B') ? 1_000_000_000m : 1m;
        if (text.EndsWith('%') || multiplier != 1) text = text[..^1];
        return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value) ? value * multiplier : null;
    }
}
