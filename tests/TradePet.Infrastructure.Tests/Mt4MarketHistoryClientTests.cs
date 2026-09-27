using System.Text.Json;
using System.Text.Json.Nodes;
using TradePet.Application.Review;
using TradePet.Core.Domain;
using TradePet.Infrastructure.Mt4;
using TradePet.Infrastructure.Mt5;
using Xunit;

namespace TradePet.Infrastructure.Tests;

public sealed class Mt4MarketHistoryClientTests
{
    [Fact]
    public async Task RequestResponse_UsesSelectedTerminalAndRemovesOnlyItsRequestFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mt4-market-tests", Guid.NewGuid().ToString("N"));
        var files = Path.Combine(directory, "MQL4", "Files", "TradePet");
        var requestPath = Path.Combine(files, $"market-request-{Request.RequestId}.json");
        var responsePath = Path.Combine(files, $"market-response-{Request.RequestId}.json");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var client = new Mt4MarketHistoryClient(@"C:\BrokerMT4", directory, 10800);
        var task = client.LoadAsync(Request, cancellation.Token);
        try
        {
            while (!File.Exists(requestPath)) await Task.Delay(20, cancellation.Token);
            var command = JsonNode.Parse(await File.ReadAllTextAsync(requestPath, cancellation.Token))!;
            Assert.Equal(Request.ExpectedAccountKey, command["accountKey"]!.GetValue<string>());
            Assert.Equal(Request.FromUtc.ToUnixTimeSeconds(), command["fromEpochUtc"]!.GetValue<long>());
            var unrelated = Path.Combine(files, "market-response-unrelated.json");
            await File.WriteAllTextAsync(unrelated, "keep", cancellation.Token);
            await File.WriteAllTextAsync(responsePath + ".tmp", Response(), cancellation.Token);
            File.Move(responsePath + ".tmp", responsePath);
            Assert.Single((await task).Bars);
            Assert.False(File.Exists(requestPath));
            Assert.False(File.Exists(responsePath));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await task; } catch (OperationCanceledException) { }
            Directory.Delete(directory, true);
        }
    }

    private static readonly DateTimeOffset Start = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly MarketHistoryRequest Request = new(Guid.NewGuid().ToString("N"),
        Mt5TerminalDiscovery.CreateTerminalId(@"C:\BrokerMT4"), "MT4:Broker|42", "EURUSD", "M5",
        Start, Start.AddMinutes(10), MarketDataPrecision.Bars);

    [Fact]
    public void Bars_MapTimezoneAndPricesWithoutClaimingFullCoverageOrInventingTicks()
    {
        var result = Mt4MarketHistoryClient.Parse(Request, Response(), 10800);
        var bar = Assert.Single(result.Bars);
        Assert.Equal(Start, bar.OpenedAtUtc);
        Assert.Equal(1.11m, bar.High);
        Assert.Equal(Request.ExpectedAccountKey, bar.AccountKey);
        Assert.Equal(MarketCoverageStatus.Partial, result.Range.Coverage);
        Assert.Equal("mt4-bars-v1", result.Range.SourceVersion);
        Assert.Empty(result.Ticks);
    }

    [Theory]
    [InlineData("accountKey", "MT4:Other|42")]
    [InlineData("requestId", "old-request")]
    [InlineData("terminalPath", "C:\\OtherMT4")]
    [InlineData("symbol", "XAUUSD")]
    public void Bars_RejectAnotherRequestAccountTerminalOrSymbol(string field, string value)
    {
        var response = JsonNode.Parse(Response())!;
        response[field] = value;
        Assert.Throws<InvalidDataException>(() => Mt4MarketHistoryClient.Parse(Request, response.ToJsonString(), 10800));
    }

    [Fact]
    public void Bars_RejectWrongOffsetOutOfRangeAndImpossibleOhlc()
    {
        Assert.Throws<InvalidDataException>(() => Mt4MarketHistoryClient.Parse(Request, Response(), 7200));
        var json = JsonNode.Parse(Response())!;
        json["bars"]![0]!["high"] = .5m;
        Assert.Throws<InvalidDataException>(() => Mt4MarketHistoryClient.Parse(Request, json.ToJsonString(), 10800));
        json["bars"]![0]!["high"] = 1.11m;
        json["bars"]![0]!["time"] = Start.AddDays(1).ToUnixTimeSeconds() + 10800;
        Assert.Throws<InvalidDataException>(() => Mt4MarketHistoryClient.Parse(Request, json.ToJsonString(), 10800));
    }

    private static string Response() => JsonSerializer.Serialize(new
    {
        requestId = Request.RequestId, accountKey = Request.ExpectedAccountKey, terminalPath = @"C:\BrokerMT4",
        symbol = Request.Symbol, timeframe = Request.Timeframe, serverUtcOffsetSeconds = 10800,
        bars = new[] { new { time = Start.ToUnixTimeSeconds() + 10800, open = 1.1m, high = 1.11m, low = 1.09m, close = 1.105m, tickVolume = 100 } },
    });
}
