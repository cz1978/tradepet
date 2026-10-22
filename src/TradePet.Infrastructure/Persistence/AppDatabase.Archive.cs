using System.Globalization;
using TradePet.Application.Review;
using TradePet.Core.Domain;

namespace TradePet.Infrastructure.Persistence;

public sealed partial class AppDatabase
{
    public async Task<IReadOnlyList<LocalAccountArchive>> LoadLocalAccountsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.server,a.login,a.currency,
                (SELECT MIN(open_server_date) FROM trades t WHERE t.account_key=a.account_key),
                (SELECT MAX(COALESCE(close_server_date,open_server_date)) FROM trades t WHERE t.account_key=a.account_key),
                (SELECT payload_json FROM server_time_segments s WHERE s.account_key=a.account_key ORDER BY from_utc DESC LIMIT 1)
            FROM accounts a ORDER BY last_seen_utc DESC,account_key;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<LocalAccountArchive>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(new(reader.GetString(0), reader.GetInt64(1)), reader.GetString(2),
                reader.IsDBNull(3) ? null : DateOnly.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? null : DateOnly.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                reader.IsDBNull(5) ? null : DeserializeOrNull<ServerTimeSegment>(reader.GetString(5))));
        return result;
    }

    public async Task<MarketHistoryResult> LoadOfflineMarketDataAsync(MarketHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        MarketDataRange? range;
        await using (var connection = await OpenConnectionAsync(cancellationToken))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT payload_json FROM market_data_ranges WHERE account_key=$account AND symbol=$symbol COLLATE NOCASE
                AND timeframe=$timeframe AND precision=$precision AND requested_from_utc < $to AND requested_to_utc > $from
                ORDER BY CASE WHEN requested_from_utc <= $from AND requested_to_utc >= $to THEN 0 ELSE 1 END,
                    updated_at_utc DESC LIMIT 1;
                """;
            command.Parameters.AddWithValue("$account", request.ExpectedAccountKey);
            command.Parameters.AddWithValue("$symbol", request.Symbol);
            command.Parameters.AddWithValue("$timeframe", request.Timeframe);
            command.Parameters.AddWithValue("$precision", request.Precision.ToString());
            command.Parameters.AddWithValue("$from", Format(request.FromUtc));
            command.Parameters.AddWithValue("$to", Format(request.ToUtc));
            range = DeserializeOrNull<MarketDataRange>(await command.ExecuteScalarAsync(cancellationToken) as string);
        }
        if (range is null) return new(new(request.RequestId, "offline", request.ExpectedAccountKey, request.Symbol,
            request.Timeframe, request.FromUtc, request.ToUtc, null, null, request.Precision, MarketCoverageStatus.Empty,
            "offline-cache-v1", "离线档案没有缓存该段行情；仅显示已存成交和证据，不连接终端补取。", DateTimeOffset.UtcNow), [], []);
        var from = request.FromUtc > range.RequestedFromUtc ? request.FromUtc : range.RequestedFromUtc;
        var to = request.ToUtc < range.RequestedToUtc ? request.ToUtc : range.RequestedToUtc;
        var cached = await LoadMarketDataAsync(request.ExpectedAccountKey, range.TerminalId, request.Symbol,
            request.Timeframe, request.Precision, from, to, cancellationToken);
        var complete = range.Coverage == MarketCoverageStatus.Complete && from == request.FromUtc && to == request.ToUtc;
        return new(range with { RequestId = request.RequestId, RequestedFromUtc = request.FromUtc,
            RequestedToUtc = request.ToUtc, Coverage = complete ? MarketCoverageStatus.Complete : MarketCoverageStatus.Partial,
            Error = complete ? "" : "离线行情覆盖不完整，缺口不补造。" }, cached.Bars, cached.Ticks);
    }
}
