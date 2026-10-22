using Microsoft.Data.Sqlite;

namespace TradePet.Infrastructure.Mt4;

public static class Mt4ArchiveMigration
{
    // Only reads legacy terminal files. The assistant owns the destination, which travels with its backups.
    public static async Task ImportAsync(IEnumerable<string> terminalDataDirectories, string archiveDataDirectory,
        CancellationToken cancellationToken = default)
    {
        var destinationDirectory = Path.Combine(archiveDataDirectory, "MQL4", "Files", "TradePet");
        Directory.CreateDirectory(destinationDirectory);
        foreach (var dataDirectory in terminalDataDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        foreach (var name in new[] { "order-archive.db", "tick-archive.db" })
        {
            var sourcePath = Path.GetFullPath(Path.Combine(dataDirectory, "MQL4", "Files", "TradePet", name));
            var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, name));
            if (!File.Exists(sourcePath) || sourcePath.Equals(destinationPath, StringComparison.OrdinalIgnoreCase)) continue;
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await source.OpenAsync(cancellationToken);
            await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = destinationPath, Pooling = false }.ToString());
            var existed = File.Exists(destinationPath);
            await destination.OpenAsync(cancellationToken);
            if (!existed) source.BackupDatabase(destination);
            await using var command = destination.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS legacy_imports(source TEXT PRIMARY KEY,stamp TEXT NOT NULL);";
            await command.ExecuteNonQueryAsync(cancellationToken);
            var info = new FileInfo(sourcePath);
            var wal = new FileInfo(sourcePath + "-wal");
            var stamp = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}:{(wal.Exists ? wal.LastWriteTimeUtc.Ticks : 0)}";
            command.CommandText = "SELECT stamp FROM legacy_imports WHERE source=$source;";
            command.Parameters.AddWithValue("$source", sourcePath);
            if (await command.ExecuteScalarAsync(cancellationToken) as string == stamp) continue;
            var snapshotPath = Path.Combine(Path.GetTempPath(), $"tradepet-collector-import-{Guid.NewGuid():N}.db");
            await using (var snapshot = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshotPath, Pooling = false }.ToString()))
            {
                await snapshot.OpenAsync(cancellationToken);
                source.BackupDatabase(snapshot);
            }
            command.CommandText = "ATTACH DATABASE $legacy AS legacy;";
            command.Parameters.AddWithValue("$legacy", snapshotPath);
            await command.ExecuteNonQueryAsync(cancellationToken);
            try
            {
                await using var transaction = (SqliteTransaction)await destination.BeginTransactionAsync(cancellationToken);
                command.Transaction = transaction;
                command.CommandText = name == "order-archive.db"
                    ? "INSERT OR IGNORE INTO orders(account,ticket,payload) SELECT account,ticket,payload FROM legacy.orders;"
                    : "INSERT OR IGNORE INTO quotes(account,instance,sequence,symbol,server_time,bid,ask,last,volume) SELECT account,instance,sequence,symbol,server_time,bid,ask,last,volume FROM legacy.quotes;";
                await command.ExecuteNonQueryAsync(cancellationToken);
                command.CommandText = "INSERT INTO legacy_imports(source,stamp) VALUES($source,$stamp) ON CONFLICT(source) DO UPDATE SET stamp=excluded.stamp;";
                command.Parameters.AddWithValue("$stamp", stamp);
                await command.ExecuteNonQueryAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                command.Transaction = null;
            }
            finally
            {
                command.Transaction = null;
                command.CommandText = "DETACH DATABASE legacy;";
                try { await command.ExecuteNonQueryAsync(CancellationToken.None); }
                finally { if (File.Exists(snapshotPath)) File.Delete(snapshotPath); }
            }
        }
    }
}
