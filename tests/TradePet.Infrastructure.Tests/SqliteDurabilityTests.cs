using Microsoft.Data.Sqlite;
using TradePet.Infrastructure.Persistence;
using Xunit;

namespace TradePet.Infrastructure.Tests;

public sealed class SqliteDurabilityTests
{
    [Fact]
    public async Task NativeEngine_IncludesWalResetCorruptionFix()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        var version = Version.Parse((string)(await command.ExecuteScalarAsync())!);
        Assert.True(version >= new Version(3, 51, 3),
            $"SQLite {version} predates the WAL-reset corruption fix (3.51.3).");
    }

    [Fact]
    public async Task ConcurrentWalWritesCheckpointsAndBackup_PreserveCommittedRowsAfterReopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "tradepet-wal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "workspace.db");
        var connectionString = new SqliteConnectionStringBuilder
            { DataSource = path, Pooling = false, DefaultTimeout = 10 }.ToString();
        try
        {
            var database = new AppDatabase(path);
            await database.InitializeAsync();
            await using var anchor = new SqliteConnection(connectionString);
            await anchor.OpenAsync();
            await using (var setup = anchor.CreateCommand())
            {
                setup.CommandText = "CREATE TABLE durability_rows(id INTEGER PRIMARY KEY, payload TEXT NOT NULL);";
                await setup.ExecuteNonQueryAsync();
            }

            using var start = new ManualResetEventSlim(false);
            var workers = Enumerable.Range(0, 3).Select(worker => Task.Run(async () =>
            {
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA wal_autocheckpoint=1;";
                await command.ExecuteNonQueryAsync();
                start.Wait();
                for (var index = 0; index < 120; index++)
                {
                    if (worker == 2)
                    {
                        command.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
                    }
                    else
                    {
                        command.CommandText = "INSERT INTO durability_rows(id, payload) VALUES($id, $payload);";
                        command.Parameters.Clear();
                        command.Parameters.AddWithValue("$id", worker * 120 + index);
                        command.Parameters.AddWithValue("$payload", new string((char)('a' + worker), 6000) + index);
                    }
                    await command.ExecuteNonQueryAsync();
                }
            })).ToArray();
            start.Set();
            await Task.WhenAll(workers);

            var service = new ReviewBackupService(database, Path.Combine(root, "attachments"), path);
            var package = await service.CreateAsync(Path.Combine(root, "backups"));
            await service.ValidateAsync(package);
            var extracted = Path.Combine(root, "snapshot");
            System.IO.Compression.ZipFile.ExtractToDirectory(package, extracted);
            await AssertCommittedRowsAsync(Path.Combine(extracted, "tradepet.db"));
            await anchor.DisposeAsync();
            SqliteConnection.ClearAllPools();
            await new AppDatabase(path).InitializeWithRecoveryAsync(Path.Combine(root, "startup-backups"));
            await AssertCommittedRowsAsync(path);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AssertCommittedRowsAsync(string path)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        Assert.Equal("ok", await command.ExecuteScalarAsync());
        command.CommandText = "SELECT id, payload FROM durability_rows ORDER BY id;";
        await using var reader = await command.ExecuteReaderAsync();
        for (var id = 0; id < 240; id++)
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(id, reader.GetInt64(0));
            Assert.Equal(new string((char)('a' + id / 120), 6000) + id % 120, reader.GetString(1));
        }
        Assert.False(await reader.ReadAsync());
    }
}
