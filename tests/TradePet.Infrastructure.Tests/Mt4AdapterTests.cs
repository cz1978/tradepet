using System.Text.Json;
using System.Text.Json.Nodes;
using TradePet.Core.Domain;
using TradePet.Core.Protocol;
using TradePet.Infrastructure.Mt4;
using TradePet.Infrastructure.Mt5;
using Xunit;

namespace TradePet.Infrastructure.Tests;

public sealed class Mt4AdapterTests
{
    [Fact]
    public void VersionTwo_RiskUsesBrokerTickValueAndPriceStepAndRequiresValidInputs()
    {
        var now = DateTimeOffset.UtcNow;
        const string terminal = @"C:\BrokerMT4\terminal.exe";
        var json = JsonNode.Parse(Frame(terminal, now))!;
        json["version"] = 2;
        json["positions"]![0]!["tickSize"] = .00001m;
        json["positions"]![0]!["tickValue"] = 1m;
        Mt5SnapshotBatch Map() => Mt5PayloadMapper.MapSnapshot(ProtocolEnvelope.Create("test", 1, "snapshot",
            Mt4FileClient.ReadFrame(json.ToJsonString(), terminal, now).Payload, "MT4:Broker|42"));
        Assert.Equal(400m, Assert.Single(Map().Positions).InitialRiskAmount);
        json["positions"]![0]!["tickValue"] = 0m;
        Assert.Null(Assert.Single(Map().Positions).InitialRiskAmount);
        json["positions"]![0]!["tickValue"] = 1m;
        json["positions"]![0]!["stopLoss"] = 0m;
        Assert.Null(Assert.Single(Map().Positions).InitialRiskAmount);
    }

    private static string Frame(string terminal, DateTimeOffset captured, long sequence = 1, long login = 42) =>
        JsonSerializer.Serialize(new
        {
            version = 1, platform = "mt4", sourceInstanceId = "ea-test", sequence,
            terminalPath = Mt5TerminalDiscovery.GetInstallationDirectory(terminal), connected = true,
            capturedAtUtc = captured, account = new { server = "Broker", login, currency = "USD" },
            balance = 1000m, equity = 989m, floatingPnl = -11m, serverUtcOffsetSeconds = 7200,
            positions = new[] { new { ticket = 51L, positionId = 51L, symbol = "EURUSD", side = "sell",
                volume = 0.2m, entryPrice = 1.1m, currentPrice = 1.101m, profit = -10m, swap = -1m,
                stopLoss = 1.12m, takeProfit = 1.05m, openedAtUtc = captured.AddMinutes(-10) } },
            orders = new[] { new { ticket = 52L, symbol = "EURUSD", type = "buy_limit", volume = 0.1m,
                price = 1.09m, stopLoss = 1.08m, takeProfit = 1.11m, createdAtUtc = captured } },
        }, ProtocolJson.Options);

    [Fact]
    public void Frame_MapsPositionsAndOrdersWithoutMixingMt5AccountsOrInventingHistory()
    {
        var now = DateTimeOffset.UtcNow;
        var terminal = Path.Combine(Path.GetTempPath(), "mt4-broker", "terminal.exe");
        var frame = Mt4FileClient.ReadFrame(Frame(terminal, now), terminal, now);
        var batch = Mt5PayloadMapper.MapSnapshot(ProtocolEnvelope.Create("test", 1, "snapshot", frame.Payload, frame.AccountKey));
        Assert.Equal("MT4:Broker|42", batch.Account.Scope.AccountKey);
        Assert.Equal(frame.AccountKey, batch.Account.Scope.AccountKey);
        Assert.Equal(-1, batch.Account.MarginMode);
        Assert.Equal(-11m, batch.Account.FloatingPnl);
        Assert.Equal(-1m, Assert.Single(batch.Positions).Swap);
        Assert.Null(Assert.Single(batch.Positions).InitialRiskAmount);
        Assert.Equal("buy_limit", Assert.Single(batch.Orders).Type);
    }

    [Theory]
    [InlineData(-11)]
    [InlineData(6)]
    public void Frame_RejectsExpiredOrFutureData(int seconds)
    {
        var terminal = Path.Combine(Path.GetTempPath(), "mt4-broker", "terminal.exe");
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<InvalidDataException>(() => Mt4FileClient.ReadFrame(Frame(terminal, now.AddSeconds(seconds)), terminal, now));
    }

    [Fact]
    public void Frame_RejectsWrongTerminalAndMissingAccount()
    {
        var terminal = Path.Combine(Path.GetTempPath(), "mt4-broker", "terminal.exe");
        var other = Path.Combine(Path.GetTempPath(), "other-broker", "terminal.exe");
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<InvalidDataException>(() => Mt4FileClient.ReadFrame(Frame(terminal, now), other, now));
        Assert.Throws<InvalidDataException>(() => Mt4FileClient.ReadFrame(Frame(terminal, now, login: 0), terminal, now));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Client_DisconnectsOnBrokenFileAndRecoversForAnotherAccount(bool stale)
    {
        var root = Path.Combine(Path.GetTempPath(), "TradePetMt4Tests", Guid.NewGuid().ToString("N"));
        var terminal = Path.Combine(root, "terminal.exe");
        var path = Path.Combine(root, "MQL4", "Files", Mt4FileClient.SnapshotFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = new Mt4FileClient(terminal, root);
        await File.WriteAllTextAsync(path, Frame(terminal, DateTimeOffset.UtcNow), cancellation.Token);
        var running = client.RunAsync(cancellation.Token);
        try
        {
            Assert.True((await client.Events.ReadAsync(cancellation.Token)).Payload.GetProperty("connected").GetBoolean());
            Assert.Equal("snapshot", (await client.Events.ReadAsync(cancellation.Token)).Kind);
            await File.WriteAllTextAsync(path, stale ? Frame(terminal, DateTimeOffset.UtcNow.AddMinutes(-1)) : "{broken", cancellation.Token);
            Assert.False((await client.Events.ReadAsync(cancellation.Token)).Payload.GetProperty("connected").GetBoolean());
            await File.WriteAllTextAsync(path, Frame(terminal, DateTimeOffset.UtcNow, 2, 88), cancellation.Token);
            var connected = await client.Events.ReadAsync(cancellation.Token);
            Assert.Equal("MT4:Broker|88", connected.AccountKey);
            Assert.Equal("snapshot", (await client.Events.ReadAsync(cancellation.Token)).Kind);
            Assert.False(client.Events.TryRead(out _)); // No synthetic empty history batch.
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await running; } catch (OperationCanceledException) { }
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Discovery_SeparatesMt4AndMt5AndPreservesMissingSavedSelection()
    {
        var root = Path.Combine(Path.GetTempPath(), "TradePetMt4Tests", Guid.NewGuid().ToString("N"));
        var install = Path.Combine(root, "Arbitrary Broker");
        var registered = Path.Combine(root, "registration");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(registered);
        var terminal = Path.Combine(install, "terminal.exe");
        File.WriteAllText(terminal, string.Empty);
        File.WriteAllText(Path.Combine(registered, "origin.txt"), install);
        try
        {
            Assert.Equal(terminal, Assert.Single(Mt5TerminalDiscovery.DiscoverRegisteredPaths(root, TradingPlatform.Mt4)));
            Assert.Empty(Mt5TerminalDiscovery.DiscoverRegisteredPaths(root, TradingPlatform.Mt5));
            Assert.Null(new Mt5TerminalDiscovery().FindPreferred(Path.Combine(root, "missing", "terminal.exe"), TradingPlatform.Mt4));
            Assert.Equal(terminal, new Mt5TerminalDiscovery().FindPreferred(terminal, TradingPlatform.Mt4)?.TerminalPath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void VersionTwo_EnablesHistoryOnlyWithAValidatedClockAndRejectsForeignCharts()
    {
        var now = DateTimeOffset.UtcNow;
        var terminal = @"C:\BrokerMT4\terminal.exe";
        var json = JsonNode.Parse(Frame(terminal, now))!;
        json["version"] = 2;
        var frame = Mt4FileClient.ReadFrame(json.ToJsonString(), terminal, now);
        Assert.True(Mt5PayloadMapper.MapSnapshot(ProtocolEnvelope.Create("test", 1, "snapshot", frame.Payload, frame.AccountKey)).SupportsOrderHistory);
        json.AsObject().Remove("serverUtcOffsetSeconds");
        Assert.False(Mt4FileClient.ReadFrame(json.ToJsonString(), terminal, now).Payload.GetProperty("historyAvailable").GetBoolean());
        json["charts"] = JsonSerializer.SerializeToNode(new { terminalPath = @"C:\OtherMT4", hostChartId = 1L, objects = Array.Empty<object>() });
        Assert.Throws<InvalidDataException>(() => Mt4FileClient.ReadFrame(json.ToJsonString(), terminal, now));
    }

    [Fact]
    public async Task Client_EmitsOrderLedgerAndRefreshesItWithoutChangingOriginalTickets()
    {
        var root = Path.Combine(Path.GetTempPath(), "TradePetMt4Tests", Guid.NewGuid().ToString("N"));
        var terminal = Path.Combine(root, "terminal.exe");
        var path = Path.Combine(root, "MQL4", "Files", Mt4FileClient.SnapshotFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var now = DateTimeOffset.UtcNow;
        var snapshot = JsonNode.Parse(Frame(terminal, now))!;
        snapshot["version"] = 2;
        var history = JsonSerializer.Serialize(new
        {
            version = 2, platform = "mt4", sourceInstanceId = "ea-test", accountKey = "MT4:Broker|42",
            terminalPath = root, capturedAtUtc = now, serverUtcOffsetSeconds = 7200, scanComplete = true,
            orders = new[] { new { ticket = 51L, type = 1, symbol = "EURUSD", volume = .2m,
                openTime = now.AddHours(-2).ToUnixTimeSeconds() + 7200, closeTime = now.AddHours(-1).ToUnixTimeSeconds() + 7200,
                openPrice = 1.1m, closePrice = 1.09m, profit = 20m, commission = -2m, swap = -1m } },
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = new Mt4FileClient(terminal, root);
        await File.WriteAllTextAsync(path, snapshot.ToJsonString(), cancellation.Token);
        await File.WriteAllTextAsync(Path.Combine(root, "MQL4", "Files", Mt4FileClient.HistoryFileName), history, cancellation.Token);
        var running = client.RunAsync(cancellation.Token);
        try
        {
            var envelope = await ReadDeals();
            var batch = Mt5PayloadMapper.MapDealBatch(envelope);
            Assert.Equal("MT4:Broker|42", envelope.AccountKey);
            Assert.True(batch.IsRecovery);
            Assert.Equal(17m, batch.Deals.Sum(d => d.NetPnl));
            Assert.All(batch.Deals, d => Assert.Equal(51L, d.OrderTicket));
            Assert.Null(batch.HistoryProgress);
            await client.RefreshHistoryAsync([2026], 7200, new(2026, 9, 27), cancellation.Token);
            snapshot["sequence"] = 2;
            snapshot["capturedAtUtc"] = DateTimeOffset.UtcNow;
            await File.WriteAllTextAsync(path, snapshot.ToJsonString(), cancellation.Token);
            var refreshed = Mt5PayloadMapper.MapDealBatch(await ReadDeals());
            Assert.Equal(batch.Deals, refreshed.Deals);
            await client.SetLossZonesAsync("MT4:Broker|42", new(2026, 9, 27),
                [new("zone", "EURUSD", 1, 1.1m, 1.2m, 2, 2, -10)], cancellation.Token);
            var zones = JsonSerializer.Deserialize<ProtocolEnvelope>(await File.ReadAllTextAsync(
                Path.Combine(root, "MQL4", "Files", "TradePet", "loss-zones.json"), cancellation.Token), ProtocolJson.Options);
            Assert.Equal("MT4:Broker|42", zones!.AccountKey);
            File.Delete(Path.Combine(root, "MQL4", "Files", Mt4FileClient.HistoryFileName));
            await client.RefreshAsync(7200, new(2026, 9, 27), cancellation.Token);
            snapshot["sequence"] = 3;
            snapshot["capturedAtUtc"] = DateTimeOffset.UtcNow;
            await File.WriteAllTextAsync(path, snapshot.ToJsonString(), cancellation.Token);
            while (true)
            {
                var status = await client.Events.ReadAsync(cancellation.Token);
                if (status.Kind != "history_status") continue;
                Assert.False(status.Payload.GetProperty("ready").GetBoolean());
                break;
            }
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await running; } catch (OperationCanceledException) { }
            Directory.Delete(root, true);
        }

        async Task<ProtocolEnvelope> ReadDeals()
        {
            while (true)
            {
                var item = await client.Events.ReadAsync(cancellation.Token);
                if (item.Kind == "deals") return item;
            }
        }
    }
}
