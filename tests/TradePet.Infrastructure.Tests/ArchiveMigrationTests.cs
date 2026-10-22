using System.IO.Compression;
using Microsoft.Data.Sqlite;
using TradePet.Application.Review;
using TradePet.Core.Domain;
using TradePet.Infrastructure.Mt4;
using TradePet.Infrastructure.Persistence;
using Xunit;

namespace TradePet.Infrastructure.Tests;

public sealed class ArchiveMigrationTests
{
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "tradepet-migration-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MigrationBackup_RestoresWalCommittedCollectorAndReports()
    {
        var root = NewRoot(); Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "tradepet.db");
        var database = new AppDatabase(databasePath); await database.InitializeAsync();
        var attachments = Path.Combine(root, "attachments");
        var collector = Path.Combine(attachments, "_system", "collectors", "MQL4", "Files", "TradePet", "order-archive.db");
        Directory.CreateDirectory(Path.GetDirectoryName(collector)!);
        var report = Path.Combine(attachments, "_system", "reports", "daily.md");
        Directory.CreateDirectory(Path.GetDirectoryName(report)!); await File.WriteAllTextAsync(report, "original report");
        var service = new ReviewBackupService(database, attachments, databasePath);
        string package;
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = collector, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE orders(ticket INTEGER PRIMARY KEY); INSERT INTO orders VALUES(1);";
            await command.ExecuteNonQueryAsync();
            package = await service.CreateAsync(Path.Combine(root, "backups"));
            using var archive = ZipFile.OpenRead(package);
            Assert.DoesNotContain(archive.Entries, item => item.FullName.EndsWith("-wal") || item.FullName.EndsWith("-shm"));
            Assert.Contains(archive.Entries, item => item.FullName.EndsWith("order-archive.db"));
            command.CommandText = "INSERT INTO orders VALUES(2);"; await command.ExecuteNonQueryAsync();
        }
        await File.WriteAllTextAsync(report, "changed report");
        await service.RestoreAsync(package, Path.Combine(root, "backups"));
        Assert.Equal("original report", await File.ReadAllTextAsync(report));
        await using var restored = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = collector, Pooling = false }.ToString());
        await restored.OpenAsync(); await using var count = restored.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM orders;";
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task LegacyArchives_AreMergedWithoutChangingTerminalFilesOrDuplicatingTickets()
    {
        var root = NewRoot(); var managed = Path.Combine(root, "managed");
        async Task<string> CreateLegacy(string name, long ticket)
        {
            var terminal = Path.Combine(root, name);
            var path = Path.Combine(terminal, "MQL4", "Files", "TradePet", "order-archive.db");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await connection.OpenAsync(); await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE orders(account TEXT NOT NULL,ticket INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(account,ticket)); INSERT INTO orders VALUES('Broker|1',$ticket,'{}');";
            command.Parameters.AddWithValue("$ticket", ticket); await command.ExecuteNonQueryAsync();
            return terminal;
        }
        var first = await CreateLegacy("first", 1); var second = await CreateLegacy("second", 2);
        var source = Path.Combine(first, "MQL4", "Files", "TradePet", "order-archive.db");
        var original = await File.ReadAllBytesAsync(source);
        await Mt4ArchiveMigration.ImportAsync([first, second], managed);
        await Mt4ArchiveMigration.ImportAsync([first, second], managed);
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        await using var merged = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(managed, "MQL4", "Files", "TradePet", "order-archive.db"), Pooling = false }.ToString());
        await merged.OpenAsync(); await using var count = merged.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM orders;";
        Assert.Equal(2L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task OfflineArchive_ListsAccountsAndUsesOnlyTheirOwnCachedPrices()
    {
        var root = NewRoot(); Directory.CreateDirectory(root);
        var database = new AppDatabase(Path.Combine(root, "tradepet.db")); await database.InitializeAsync();
        var now = DateTimeOffset.UtcNow; var scope = new AccountScope("Offline", 5);
        await database.UpsertAccountAsync(new(scope, "USD", 1000m, 1000m, 0m, 2, now));
        Assert.Equal(scope, Assert.Single(await database.LoadLocalAccountsAsync()).Scope);
        var request = new MarketHistoryRequest(Guid.NewGuid().ToString("N"), "unavailable-terminal", scope.AccountKey,
            "TEST", "M5", now.AddHours(-1), now, MarketDataPrecision.Bars);
        var range = new MarketDataRange("saved", "old-terminal", scope.AccountKey, "TEST", "M5", request.FromUtc,
            request.ToUtc, request.FromUtc, request.ToUtc, request.Precision, MarketCoverageStatus.Complete, "test", "", now);
        var bar = new MarketBar("old-terminal", scope.AccountKey, "TEST", "M5", now.AddMinutes(-10), 10m, 12m, 9m, 11m, 1, 0, 1);
        await database.SaveMarketDataAsync(range, [bar], []);
        var cached = await database.LoadOfflineMarketDataAsync(request);
        Assert.Single(cached.Bars); Assert.Equal(MarketCoverageStatus.Complete, cached.Range.Coverage);
        Assert.Empty((await database.LoadOfflineMarketDataAsync(request with { ExpectedAccountKey = "Other|9" })).Bars);
        Assert.Equal(MarketCoverageStatus.Partial,
            (await database.LoadOfflineMarketDataAsync(request with { FromUtc = request.FromUtc.AddHours(-1) })).Range.Coverage);
    }
}
