using System.Text.Json;
using TradePet.Application.Review;
using TradePet.Core.Domain;
using TradePet.Infrastructure.Mt4;
using TradePet.Infrastructure.Mt5;
using Xunit;

namespace TradePet.Infrastructure.Tests;

public sealed class Mt4TickArchiveTests
{
    [Fact]
    public async Task Quotes_ReplayActualValuesPersistentlyWithDeduplicationIsolationLimitsAndBrokerTime()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mt4-ticks-" + Guid.NewGuid().ToString("N"));
        const string account = "MT4:Broker|42";
        var now = DateTimeOffset.UtcNow;
        var seconds = now.ToUnixTimeSeconds();
        var frame = new Mt4Frame("ea", 1, now, true, account, JsonSerializer.SerializeToElement(new
        {
            serverUtcOffsetSeconds = 10800,
            quotes = new[] {
                new { sequence = 1, symbol = "EURUSD", time = seconds + 10800, bid = 1.1m, ask = 1.11m, last = 0, volume = 0 },
                new { sequence = 2, symbol = "EURUSD", time = seconds + 10800, bid = 1.105m, ask = 1.115m, last = 0, volume = 0 },
            },
        }));
        try
        {
            var archive = new Mt4TickArchive(directory);
            await archive.AppendAsync(frame);
            await archive.AppendAsync(frame);
            var request = new MarketHistoryRequest(Guid.NewGuid().ToString("N"), Mt5TerminalDiscovery.CreateTerminalId(@"C:\BrokerMT4"),
                account, "EURUSD", "M5", now.AddSeconds(-10), now.AddSeconds(10), MarketDataPrecision.Ticks);
            var client = new Mt4MarketHistoryClient(@"C:\BrokerMT4", directory, 10800);
            var result = await client.LoadAsync(request);
            Assert.Equal(2, result.Ticks.Count);
            Assert.Equal(1.105m, result.Ticks[1].Bid);
            Assert.Equal(0, result.Ticks[1].TimeMilliseconds % 1000); // No invented millisecond precision.
            Assert.Equal(MarketCoverageStatus.Partial, result.Range.Coverage);
            Assert.Empty((await client.LoadAsync(request with { ExpectedAccountKey = "MT4:Broker|99" })).Ticks);
            Assert.Empty((await client.LoadAsync(request with { Symbol = "USDJPY" })).Ticks);
            Assert.Empty((await client.LoadAsync(request with { FromUtc = now.AddDays(-2), ToUtc = now.AddDays(-1) })).Ticks);
            Assert.Contains("前 1 条", (await client.LoadAsync(request with { MaximumTicks = 1 })).Range.Error);
            var shifted = await new Mt4MarketHistoryClient(@"C:\BrokerMT4", directory, 7200)
                .LoadAsync(request with { FromUtc = now.AddHours(1).AddSeconds(-10), ToUtc = now.AddHours(1).AddSeconds(10) });
            Assert.Equal(result.Ticks[0].OccurredAtUtc.AddHours(1), shifted.Ticks[0].OccurredAtUtc);
        }
        finally { Directory.Delete(directory, true); }
    }
}
