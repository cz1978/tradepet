using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TradePet.Infrastructure.Mt5;

namespace TradePet.Infrastructure.Mt4;

// Preserve the broker's original wall-clock values, including orders hidden by a later
// terminal history filter. Re-normalize the entire archive when the broker offset changes.
public sealed class Mt4OrderArchive(string dataDirectory)
{
    public async Task<Mt5DealBatch> MergeAsync(string json, Mt4Frame frame, string terminalPath,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        Mt4HistoryMapper.Read(json, frame, terminalPath, now); // Validate before persisting anything.
        var root = JsonNode.Parse(json)!.AsObject();
        var directory = Path.Combine(dataDirectory, "MQL4", "Files", "TradePet");
        Directory.CreateDirectory(directory);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "order-archive.db"), Pooling = false,
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS orders(account TEXT NOT NULL, ticket INTEGER NOT NULL,
                payload TEXT NOT NULL, PRIMARY KEY(account,ticket));
            """;
        await create.ExecuteNonQueryAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var order in root["orders"]!.AsArray())
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO orders(account,ticket,payload) VALUES($account,$ticket,$payload)
                ON CONFLICT(account,ticket) DO UPDATE SET payload=json_set(excluded.payload,'$.linkComment',
                    CASE WHEN json_extract(orders.payload,'$.comment') LIKE 'from #%'
                           OR json_extract(orders.payload,'$.comment') LIKE 'to #%'
                    THEN json_extract(orders.payload,'$.comment')
                    ELSE coalesce(json_extract(orders.payload,'$.linkComment'),'') END)
                WHERE (json_extract(excluded.payload,'$.closeTime')>0 OR json_extract(orders.payload,'$.closeTime')=0)
                    AND json_remove(excluded.payload,'$.linkComment')<>json_remove(orders.payload,'$.linkComment');
                """;
            insert.Parameters.AddWithValue("$account", frame.AccountKey);
            insert.Parameters.AddWithValue("$ticket", order!["ticket"]!.GetValue<long>());
            insert.Parameters.AddWithValue("$payload", order.ToJsonString());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        var orders = new JsonArray();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT payload FROM orders WHERE account=$account ORDER BY ticket LIMIT 100001;";
            select.Parameters.AddWithValue("$account", frame.AccountKey);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) orders.Add(JsonNode.Parse(reader.GetString(0)));
        }
        root["orders"] = orders;
        var result = Mt4HistoryMapper.Read(root.ToJsonString(), frame, terminalPath, now);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
