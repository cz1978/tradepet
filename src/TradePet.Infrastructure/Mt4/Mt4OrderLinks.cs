using System.Text.Json;
using System.Text.RegularExpressions;

namespace TradePet.Infrastructure.Mt4;

public static partial class Mt4OrderLinks
{
    // Broker comments are evidence only when both tickets and matching order attributes exist.
    // Never infer a partial close from matching timestamps/prices alone.
    public static IReadOnlyDictionary<long, long> Resolve(JsonElement orders)
    {
        var rows = orders.EnumerateArray().Where(o => o.GetProperty("type").GetInt32() is 0 or 1)
            .ToDictionary(o => o.GetProperty("ticket").GetInt64());
        var candidates = new HashSet<(long Parent, long Child)>();
        foreach (var (ticket, order) in rows)
        foreach (var field in new[] { "comment", "linkComment" })
        {
            if (!order.TryGetProperty(field, out var comment)) continue;
            foreach (Match match in TicketReference().Matches(comment.GetString() ?? ""))
            {
                if (!long.TryParse(match.Groups[2].Value, out var other)) continue;
                var parent = match.Groups[1].Value.Equals("from", StringComparison.OrdinalIgnoreCase) ? other : ticket;
                var child = parent == ticket ? other : ticket;
                if (parent >= child || !rows.TryGetValue(parent, out var before) || !rows.TryGetValue(child, out var after)) continue;
                var closed = before.GetProperty("closeTime").GetInt64();
                var opened = after.GetProperty("openTime").GetInt64();
                if (closed <= 0 || before.GetProperty("type").GetInt32() != after.GetProperty("type").GetInt32() ||
                    before.GetProperty("symbol").GetString() != after.GetProperty("symbol").GetString() ||
                    before.GetProperty("openPrice").GetDecimal() != after.GetProperty("openPrice").GetDecimal() ||
                    (opened != before.GetProperty("openTime").GetInt64() && opened != closed) ||
                    (before.TryGetProperty("magic", out var a) && after.TryGetProperty("magic", out var b) && a.GetInt64() != b.GetInt64())) continue;
                candidates.Add((parent, child));
            }
        }
        var ambiguous = candidates.GroupBy(p => p.Child).Where(g => g.Count() != 1).Select(g => g.Key)
            .Concat(candidates.GroupBy(p => p.Parent).Where(g => g.Count() != 1).Select(g => g.Key)).ToHashSet();
        var parents = candidates.Where(p => !ambiguous.Contains(p.Parent) && !ambiguous.Contains(p.Child))
            .ToDictionary(p => p.Child, p => p.Parent);
        var result = new Dictionary<long, long>();
        foreach (var ticket in parents.Keys.Order())
        {
            var parent = parents[ticket];
            result[ticket] = result.GetValueOrDefault(parent, parent);
        }
        return result;
    }

    [GeneratedRegex(@"(?:^|\s)(from|to)\s+#(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TicketReference();
}
