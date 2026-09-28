using System.Globalization;
using Microsoft.Data.Sqlite;
using TradePet.Application.Review;
using TradePet.Core.Domain;

namespace TradePet.Infrastructure.Mt4;

// Actual observed quotes only. MT4 has no CopyTicksRange, so unobserved periods remain gaps.
public sealed class Mt4TickArchive(string dataDirectory)
{
    private async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        var directory = Path.Combine(dataDirectory, "MQL4", "Files", "TradePet");
        Directory.CreateDirectory(directory);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "tick-archive.db"), Pooling = false,
        }.ToString());
        try
        {
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS quotes(account TEXT NOT NULL, instance TEXT NOT NULL, sequence INTEGER NOT NULL,
                    symbol TEXT NOT NULL, server_time INTEGER NOT NULL, bid TEXT NOT NULL, ask TEXT NOT NULL,
                    last TEXT NOT NULL, volume INTEGER NOT NULL, PRIMARY KEY(account,instance,sequence));
                CREATE INDEX IF NOT EXISTS quotes_range ON quotes(account,symbol,server_time,sequence);
                """;
            await command.ExecuteNonQueryAsync(token);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task AppendAsync(Mt4Frame frame, CancellationToken token = default)
    {
        if (!frame.Payload.TryGetProperty("quotes", out var quotes) ||
            !frame.Payload.TryGetProperty("serverUtcOffsetSeconds", out var offset)) return;
        if (quotes.GetArrayLength() > 2048) throw new InvalidDataException("MT4 quote batch exceeds limit.");
        var rows = quotes.EnumerateArray().Select(q => new
        {
            Sequence = q.GetProperty("sequence").GetInt64(), Symbol = q.GetProperty("symbol").GetString() ?? "",
            Time = q.GetProperty("time").GetInt64(), Bid = q.GetProperty("bid").GetDecimal(), Ask = q.GetProperty("ask").GetDecimal(),
            Last = q.GetProperty("last").GetDecimal(), Volume = q.GetProperty("volume").GetInt64(),
        }).ToArray();
        if (rows.Any(q => q.Sequence < 1 || q.Symbol.Length is 0 or > 128 || q.Time <= 0 || q.Bid <= 0 ||
            q.Ask < q.Bid || q.Last < 0 || q.Volume < 0 || q.Time - offset.GetInt32() > frame.CapturedAtUtc.ToUnixTimeSeconds() + 120))
            throw new InvalidDataException("Invalid MT4 quote values.");
        if (rows.Length == 0) return;
        await using var connection = await OpenAsync(token);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO quotes VALUES($account,$instance,$sequence,$symbol,$time,$bid,$ask,$last,$volume);
            """;
        foreach (var key in new[] { "$account", "$instance", "$sequence", "$symbol", "$time", "$bid", "$ask", "$last", "$volume" })
            command.Parameters.Add(new SqliteParameter(key, null));
        foreach (var q in rows)
        {
            command.Parameters["$account"].Value = frame.AccountKey;
            command.Parameters["$instance"].Value = frame.InstanceId;
            command.Parameters["$sequence"].Value = q.Sequence;
            command.Parameters["$symbol"].Value = q.Symbol;
            command.Parameters["$time"].Value = q.Time;
            command.Parameters["$bid"].Value = q.Bid.ToString(CultureInfo.InvariantCulture);
            command.Parameters["$ask"].Value = q.Ask.ToString(CultureInfo.InvariantCulture);
            command.Parameters["$last"].Value = q.Last.ToString(CultureInfo.InvariantCulture);
            command.Parameters["$volume"].Value = q.Volume;
            await command.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
    }

    public async Task<MarketHistoryResult> LoadAsync(MarketHistoryRequest request, int offset, CancellationToken token = default)
    {
        await using var connection = await OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT server_time,bid,ask,last,volume,instance,sequence FROM quotes
            WHERE account=$account AND symbol=$symbol AND server_time BETWEEN $from AND $to
            ORDER BY server_time,instance,sequence LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$account", request.ExpectedAccountKey);
        command.Parameters.AddWithValue("$symbol", request.Symbol);
        command.Parameters.AddWithValue("$from", request.FromUtc.ToUnixTimeSeconds() + offset);
        command.Parameters.AddWithValue("$to", request.ToUtc.ToUnixTimeSeconds() + offset);
        var limit = Math.Clamp(request.MaximumTicks, 1, 100_000);
        command.Parameters.AddWithValue("$limit", limit + 1);
        var ticks = new List<MarketTick>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var utc = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0) - offset);
            decimal Price(int index) => decimal.Parse(reader.GetString(index), CultureInfo.InvariantCulture);
            ticks.Add(new(request.TerminalId, request.ExpectedAccountKey, request.Symbol, utc, utc.ToUnixTimeMilliseconds(),
                Price(1), Price(2), Price(3), reader.GetInt64(4), 0, reader.GetString(5) + ":" + reader.GetInt64(6).ToString("D20", CultureInfo.InvariantCulture)));
        }
        var truncated = ticks.Count > limit;
        if (truncated) ticks.RemoveAt(ticks.Count - 1);
        var message = ticks.Count == 0 ? "该时段尚无 MT4 报价存档；请保持 TradePet 和桥接插件运行以记录后续报价。" :
            "MT4 实际采集报价（服务器时间，秒级精度）；挂图品种采集 Tick，其他打开图表及持仓品种每秒采样。离线、溢出及采集前的缺口不会补造。";
        if (truncated) message += $" 本次显示前 {limit} 条，后续报价未载入。";
        return new(new(request.RequestId, request.TerminalId, request.ExpectedAccountKey, request.Symbol, request.Timeframe,
            request.FromUtc, request.ToUtc, ticks.FirstOrDefault()?.OccurredAtUtc, ticks.LastOrDefault()?.OccurredAtUtc,
            MarketDataPrecision.Ticks, ticks.Count == 0 ? MarketCoverageStatus.Empty : MarketCoverageStatus.Partial,
            "mt4-recorded-quotes-v1", message, DateTimeOffset.UtcNow), [], ticks);
    }
}
