using System.Text.Json;
using System.Text.Json.Nodes;
using TradePet.Core.Domain;
using TradePet.Core.Protocol;
using TradePet.Core.Trading;
using TradePet.Infrastructure.Mt4;
using Xunit;

namespace TradePet.Infrastructure.Tests;

public sealed class Mt4HistoryMapperTests
{
    private const string Account = "MT4:Broker|42";
    private const string Terminal = @"C:\BrokerMT4\terminal.exe";
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly Mt4Frame Frame = new("ea", 1, Now, true, Account,
        JsonSerializer.SerializeToElement(new { serverUtcOffsetSeconds = 10800 }));

    [Fact]
    public void Orders_ProjectCorrectNetFeesDatesAndKeepCashFlowsAndCanceledOrdersSeparate()
    {
        var closed = Order(51, 0, true) with { Profit = 15, Commission = -2, Swap = -.5m };
        var batch = Mt4HistoryMapper.Read(History(closed, Order(52, 1, false),
            Order(53, 2, true), Order(54, 6, true) with { Profit = 100 }), Frame, Terminal, Now);
        var trades = new TradeProjector().Project(Account, batch.Deals,
            at => DateOnly.FromDateTime(at.ToOffset(TimeSpan.FromHours(3)).DateTime));

        Assert.Equal(2, trades.Count);
        var trade = Assert.Single(trades, t => t.PositionId == 51);
        Assert.Equal(12.5m, trade.NetPnl);
        Assert.True(trade.IsComplete);
        Assert.Equal(Now.AddHours(-1), trade.ClosedAtUtc);
        Assert.False(Assert.Single(trades, t => t.PositionId == 52).IsComplete);
        Assert.Equal(100m, Assert.Single(batch.CashFlows).Amount);
        Assert.Equal(Account, batch.CashFlows[0].AccountKey);
        Assert.Equal(3, batch.Deals.Count);
        Assert.Equal(TradeSide.Sell, Assert.Single(batch.Deals, d => d.PositionId == 51 && d.EntryKind == DealEntryKind.Out).Side);
        Assert.Null(batch.HistoryProgress); // A terminal filter never proves full historical coverage.
        Assert.Null(batch.ServerDate); // Never stamp today's date on all historical closures.
    }

    [Fact]
    public void PartialCloseCorrectionAndRepeatedImportsDoNotDoubleCountOrMergeRemainingTickets()
    {
        var open = Mt4HistoryMapper.Read(History(Order(51, 0, false) with { Volume = .3m }), Frame, Terminal, Now);
        var closed = Mt4HistoryMapper.Read(History(Order(51, 0, true) with { Volume = .1m, Profit = -4 },
            Order(52, 0, false) with { Volume = .2m }), Frame, Terminal, Now);
        var ledger = open.Deals.ToDictionary(d => d.Ticket);
        foreach (var deal in closed.Deals.Concat(closed.Deals)) ledger[deal.Ticket] = deal;
        var trades = new TradeProjector().Project(Account, ledger.Values, at => DateOnly.FromDateTime(at.UtcDateTime));

        Assert.Equal(3, ledger.Count);
        Assert.Equal(-4m, trades.Sum(t => t.NetPnl));
        Assert.Equal(.1m, Assert.Single(trades, t => t.IsComplete).MaximumVolume);
        Assert.Equal(.2m, Assert.Single(trades, t => !t.IsComplete).RemainingVolume);
    }

    [Fact]
    public void SameSecondOpenAndCloseRemainCompleteAndCrossMidnightUsesServerDate()
    {
        var close = new DateTimeOffset(2026, 9, 26, 21, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds() + 10800;
        var order = Order(51, 0, true) with { OpenTime = close, CloseTime = close };
        var batch = Mt4HistoryMapper.Read(History(order), Frame, Terminal, Now);
        var trade = Assert.Single(new TradeProjector().Project(Account, batch.Deals,
            at => DateOnly.FromDateTime(at.ToOffset(TimeSpan.FromHours(3)).DateTime)));
        Assert.True(trade.IsComplete);
        Assert.Equal(new DateOnly(2026, 9, 27), trade.CloseServerDate);
    }

    [Theory]
    [InlineData("accountKey", "MT4:Other|42")]
    [InlineData("sourceInstanceId", "old-ea")]
    [InlineData("terminalPath", "C:\\OtherMT4")]
    [InlineData("capturedAtUtc", "2026-09-27T09:59:00Z")]
    public void History_RejectsStaleOrWrongSource(string field, string value)
    {
        var json = JsonNode.Parse(History(Order(51, 0, true)))!;
        json[field] = value;
        Assert.Throws<InvalidDataException>(() => Mt4HistoryMapper.Read(json.ToJsonString(), Frame, Terminal, Now));
    }

    [Fact]
    public void History_RejectsClockMismatchIncompleteScanDuplicatesAndInvalidNumbers()
    {
        var clock = JsonNode.Parse(History(Order(51, 0, true)))!;
        clock["serverUtcOffsetSeconds"] = 7200;
        Assert.Throws<InvalidDataException>(() => Mt4HistoryMapper.Read(clock.ToJsonString(), Frame, Terminal, Now));
        clock["serverUtcOffsetSeconds"] = 10800;
        clock["scanComplete"] = false;
        Assert.Throws<InvalidDataException>(() => Mt4HistoryMapper.Read(clock.ToJsonString(), Frame, Terminal, Now));
        Assert.Throws<InvalidDataException>(() => Mt4HistoryMapper.Read(History(Order(51, 0, true), Order(51, 0, false)), Frame, Terminal, Now));
        Assert.Throws<InvalidDataException>(() => Mt4HistoryMapper.Read(History(Order(51, 0, true) with { Volume = -1 }), Frame, Terminal, Now));
        var updatedFrame = Frame with { Payload = JsonSerializer.SerializeToElement(new { serverUtcOffsetSeconds = 10800, liveOrderSignature = "52:0.2;" }) };
        Assert.Throws<InvalidDataException>(() => Mt4HistoryMapper.Read(History(Order(51, 0, true)), updatedFrame, Terminal, Now));
    }

    private static string History(params RawOrder[] orders) => JsonSerializer.Serialize(new
    {
        version = 2, platform = "mt4", sourceInstanceId = "ea", accountKey = Account,
        terminalPath = @"C:\BrokerMT4", capturedAtUtc = Now, serverUtcOffsetSeconds = 10800,
        scanComplete = true, orders,
    }, ProtocolJson.Options);

    private static RawOrder Order(long ticket, int type, bool closed) => new(ticket, type, "EURUSD", .2m,
        Now.AddHours(-2).ToUnixTimeSeconds() + 10800, closed ? Now.AddHours(-1).ToUnixTimeSeconds() + 10800 : 0,
        1.1m, closed ? 1.101m : 0, 0, 0, 0);

    private sealed record RawOrder(long Ticket, int Type, string Symbol, decimal Volume, long OpenTime,
        long CloseTime, decimal OpenPrice, decimal ClosePrice, decimal Profit, decimal Commission, decimal Swap,
        string Comment = "", int Magic = 0);

    [Fact]
    public void LinkedPartialClosures_ProjectOnePositionAndPreserveEveryExitTicket()
    {
        var batch = Mt4HistoryMapper.Read(History(
            Order(51, 0, true) with { Volume = .1m, Profit = 10, Comment = "to #52" },
            Order(52, 0, true) with { Volume = .1m, Profit = -4, Comment = "from #51 to #53" },
            Order(53, 0, false) with { Volume = .1m, Comment = "from #52" }), Frame, Terminal, Now);
        var trade = Assert.Single(new TradeProjector().Project(Account, batch.Deals, at => DateOnly.FromDateTime(at.UtcDateTime)));
        Assert.Equal(51, trade.PositionId);
        Assert.Equal(.3m, trade.OpeningVolume);
        Assert.Equal(.3m, trade.MaximumVolume);
        Assert.Equal(.1m, trade.RemainingVolume);
        Assert.False(trade.IsComplete);
        Assert.Equal(6m, trade.NetPnl);
        Assert.Equal(new long[] { 51, 52 }, batch.Deals.Where(d => d.EntryKind == DealEntryKind.Out).Select(d => d.OrderTicket).Order());
        Assert.Equal(51, batch.Mt4PositionAliases![53]);
        var before = new PositionSnapshot(51, 51, "EURUSD", TradeSide.Buy, .3m, 1.1m, 1.11m, 10, 1.09m, 1.12m, Now.AddHours(-2), Now);
        var after = before with { Ticket = 53, Volume = .1m, StopLoss = 1.1m };
        var changes = new PositionSnapshotDiffer().Diff([before], [after], Now, usePositionIdentity: true);
        Assert.DoesNotContain(changes, c => c.Kind is TradeDomainEventKind.Opened or TradeDomainEventKind.Closed);
        Assert.Contains(changes, c => c.Kind == TradeDomainEventKind.Reduced);
        Assert.Contains(changes, c => c.Kind == TradeDomainEventKind.StopLossModified);
    }

    [Theory]
    [InlineData("from #51", 1)]
    [InlineData("", 0)]
    public void MissingLinkOrDifferentMagicDoesNotMergeIndependentOrders(string comment, int magic)
    {
        var batch = Mt4HistoryMapper.Read(History(Order(51, 0, true),
            Order(52, 0, false) with { Comment = comment, Magic = magic }), Frame, Terminal, Now);
        Assert.Empty(batch.Mt4PositionAliases!);
    }

    [Fact]
    public async Task Archive_PreservesFilteredHistoryAndLineageAcrossRestartAndBrokerOffsetChange()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mt4-archive-" + Guid.NewGuid().ToString("N"));
        try
        {
            var archive = new Mt4OrderArchive(directory);
            await archive.MergeAsync(History(Order(51, 0, true) with { Volume = .1m, Profit = 10 },
                Order(52, 0, false) with { Volume = .2m, Comment = "from #51" }), Frame, Terminal, Now);
            var updated = await new Mt4OrderArchive(directory).MergeAsync(History(
                Order(52, 0, true) with { Volume = .2m, Profit = -4, Comment = "[sl]" }), Frame, Terminal, Now);
            var trade = Assert.Single(new TradeProjector().Project(Account, updated.Deals, at => DateOnly.FromDateTime(at.UtcDateTime)));
            Assert.True(trade.IsComplete);
            Assert.Equal(6m, trade.NetPnl);
            var narrow = JsonNode.Parse(History())!;
            narrow["serverUtcOffsetSeconds"] = 7200;
            var frame = Frame with { Payload = JsonSerializer.SerializeToElement(new { serverUtcOffsetSeconds = 7200 }) };
            var rebased = await archive.MergeAsync(narrow.ToJsonString(), frame, Terminal, Now);
            Assert.Equal(updated.Deals.Count, rebased.Deals.Count);
            Assert.Equal(updated.Deals[0].OccurredAtUtc.AddHours(1), rebased.Deals[0].OccurredAtUtc);
            Assert.Equal(51, rebased.Mt4PositionAliases![52]);
            var otherJson = JsonNode.Parse(History())!;
            otherJson["accountKey"] = "MT4:Broker|99";
            var other = await archive.MergeAsync(otherJson.ToJsonString(), Frame with { AccountKey = "MT4:Broker|99" }, Terminal, Now);
            Assert.Empty(other.Deals);
        }
        finally { Directory.Delete(directory, true); }
    }
}
