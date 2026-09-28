using System.Text.Json;
using TradePet.Core.Domain;
using TradePet.Infrastructure.Mt5;

namespace TradePet.Infrastructure.Mt4;

// MT4 exports orders, not MT5 deals. The two derived ledger entries per ticket
// are internal bookkeeping; the original MT4 ticket remains the position/order ID.
public static class Mt4HistoryMapper
{
    public static Mt5DealBatch Read(string json, Mt4Frame frame, string terminalPath, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("version").GetInt32() != 2 || root.GetProperty("platform").GetString() != "mt4" ||
            root.GetProperty("sourceInstanceId").GetString() != frame.InstanceId ||
            root.GetProperty("accountKey").GetString() != frame.AccountKey ||
            !string.Equals(Mt5TerminalDiscovery.GetInstallationDirectory(root.GetProperty("terminalPath").GetString()!),
                Mt5TerminalDiscovery.GetInstallationDirectory(terminalPath), StringComparison.OrdinalIgnoreCase) ||
            !root.GetProperty("scanComplete").GetBoolean())
            throw new InvalidDataException("MT4 history identity or scan is invalid.");
        var captured = root.GetProperty("capturedAtUtc").GetDateTimeOffset();
        if (now - captured > TimeSpan.FromSeconds(30) || captured - now > TimeSpan.FromSeconds(5))
            throw new InvalidDataException("MT4 history has expired.");
        var offset = root.GetProperty("serverUtcOffsetSeconds").GetInt32();
        if (offset is < -50400 or > 50400 ||
            !frame.Payload.TryGetProperty("serverUtcOffsetSeconds", out var frameOffset) || frameOffset.GetInt32() != offset)
            throw new InvalidDataException("MT4 history clock differs from the live snapshot.");
        if (frame.Payload.TryGetProperty("liveOrderSignature", out var liveSignature) &&
            (!root.TryGetProperty("liveOrderSignature", out var signature) || signature.GetString() != liveSignature.GetString()))
            throw new InvalidDataException("MT4 history does not match current open orders.");
        var orders = root.GetProperty("orders");
        if (orders.GetArrayLength() > 100_000) throw new InvalidDataException("MT4 history exceeds order limit.");
        var tickets = new HashSet<long>();
        var deals = new List<DealRecord>();
        var cashFlows = new List<AccountCashFlow>();
        var specifications = new Dictionary<string, SymbolSpecification>(StringComparer.Ordinal);
        foreach (var order in orders.EnumerateArray())
        {
            var ticket = order.GetProperty("ticket").GetInt64();
            var type = order.GetProperty("type").GetInt32();
            if (ticket <= 0 || ticket > int.MaxValue || !tickets.Add(ticket) || type is < 0 or > 7)
                throw new InvalidDataException("Invalid or duplicate MT4 order ticket/type.");
            DateTimeOffset Time(string name) => DateTimeOffset.FromUnixTimeSeconds(order.GetProperty(name).GetInt64() - offset);
            var opened = Time("openTime");
            var closeTime = order.GetProperty("closeTime").GetInt64();
            var profit = order.GetProperty("profit").GetDecimal();
            var commission = order.GetProperty("commission").GetDecimal();
            var swap = order.GetProperty("swap").GetDecimal();
            if (type is 6 or 7)
            {
                cashFlows.Add(new(frame.AccountKey, ticket, type == 6 ? "balance" : "credit",
                    profit + commission + swap, closeTime > 0 ? Time("closeTime") : opened));
                continue;
            }
            if (type is not (0 or 1)) continue; // Canceled/pending orders are not trades.
            var symbol = order.GetProperty("symbol").GetString() ?? "";
            var volume = order.GetProperty("volume").GetDecimal();
            var entry = order.GetProperty("openPrice").GetDecimal();
            if (string.IsNullOrWhiteSpace(symbol) || volume <= 0 || entry <= 0 ||
                opened > captured.AddSeconds(120) || (closeTime > 0 && (Time("closeTime") < opened || Time("closeTime") > captured.AddSeconds(120))))
                throw new InvalidDataException("Invalid MT4 order values or timestamps.");
            if (order.TryGetProperty("point", out var point) && order.TryGetProperty("tickSize", out var tickSize) &&
                order.TryGetProperty("digits", out var digits))
            {
                var specification = new SymbolSpecification(symbol, point.GetDecimal(), tickSize.GetDecimal(), digits.GetInt32());
                if (specification.PriceStep > 0) specifications[symbol] = specification;
            }
            var side = type == 0 ? TradeSide.Buy : TradeSide.Sell;
            deals.Add(new(ticket * 2, ticket, ticket, symbol, side, DealEntryKind.In,
                volume, entry, 0, 0, 0, 0, opened));
            if (closeTime > 0)
            {
                var exit = order.GetProperty("closePrice").GetDecimal();
                if (exit <= 0) throw new InvalidDataException("Invalid MT4 close price.");
                deals.Add(new(ticket * 2 + 1, ticket, ticket, symbol,
                    side == TradeSide.Buy ? TradeSide.Sell : TradeSide.Buy, DealEntryKind.Out,
                    volume, exit, profit, commission, swap, 0, Time("closeTime")));
            }
        }
        var aliases = Mt4OrderLinks.Resolve(orders);
        if (aliases.Count > 0)
        {
            // One original entry and each real partial exit, just like a hedging position.
            deals = deals.GroupBy(d => aliases.GetValueOrDefault(d.PositionId, d.PositionId)).SelectMany(group =>
            {
                var entries = group.Where(d => d.EntryKind == DealEntryKind.In).ToArray();
                var first = entries.Single(d => d.OrderTicket == group.Key);
                return new[] { first with { Volume = entries.Sum(d => d.Volume) } }
                    .Concat(group.Where(d => d.EntryKind == DealEntryKind.Out).Select(d => d with { PositionId = group.Key }));
            }).ToList();
        }
        // The terminal's history filter cannot be verified programmatically.
        // Never claim complete yearly coverage from a successful export.
        return new(deals, cashFlows, null, null, specifications.Values.ToArray(), Mt4PositionAliases: aliases);
    }
}
