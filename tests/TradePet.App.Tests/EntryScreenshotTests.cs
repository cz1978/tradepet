using System.IO;
using System.Reflection;
using TradePet.App.Runtime;
using TradePet.App.ViewModels;
using TradePet.Application.Review;
using TradePet.Application.Runtime;
using TradePet.Core.Domain;
using TradePet.Infrastructure.Persistence;
using Xunit;

namespace TradePet.App.Tests;

public sealed class EntryScreenshotTests
{
    [Fact]
    public async Task ScreenshotIsCopiedToTheCapturedTradeAndSurvivesSourceRemoval()
    {
        await using var fixture = await Fixture.CreateAsync();
        var attachment = await fixture.ImportAsync();
        File.Delete(fixture.Source);

        var reopened = new AppDatabase(fixture.DatabasePath);
        var detail = await reopened.LoadTradeDetailAsync(fixture.Key);
        var saved = Assert.Single(detail!.Attachments);
        Assert.Equal(attachment.Id, saved.Id);
        Assert.Equal(fixture.Key.AccountKey, saved.AccountKey);
        Assert.Equal("trade", saved.OwnerKind);
        Assert.Equal("17", saved.OwnerId);
        Assert.Equal("entry-screenshot-v1", saved.Evidence.SourceVersion);
        Assert.True(File.Exists(fixture.Store.ResolvePath(saved)));
        Assert.Null(detail.EntryReasonNote);
        Assert.Empty((await reopened.LoadTradeDetailAsync(new TradeKey(fixture.Key.AccountKey, 18)))!.Attachments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleCardCannotSaveAfterAnAccountSessionSwitch(bool sameAccount)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Sessions.SwitchAsync(sameAccount ? fixture.Key.AccountKey : "another-account");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ImportAsync());
        Assert.Empty((await fixture.Database.LoadTradeDetailAsync(fixture.Key))!.Attachments);
    }

    [Fact]
    public async Task ReadOnlyStorageAndNonImageFilesDoNotCreateAttachments()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.SetWritable(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ImportAsync());
        fixture.SetWritable(true);
        var textFile = Path.ChangeExtension(fixture.Source, ".txt");
        await File.WriteAllTextAsync(textFile, "not a screenshot");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.ImportAsync(textFile));
        Assert.Empty((await fixture.Database.LoadTradeDetailAsync(fixture.Key))!.Attachments);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        public required TradePetRuntime Runtime { get; init; }
        public required AppDatabase Database { get; init; }
        public required string DatabasePath { get; init; }
        public required ReviewAttachmentStore Store { get; init; }
        public required AccountSessionCoordinator Sessions { get; init; }
        public required TradeKey Key { get; init; }
        public required long Generation { get; init; }
        public required string Source { get; init; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "tradepet-entry-screenshot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var databasePath = Path.Combine(root, "tradepet.db");
            var database = new AppDatabase(databasePath);
            await database.InitializeAsync();
            var account = new AccountSnapshot(new("ScreenshotTest", 1), "USD", 10000m, 10000m, 0m, 2, DateTimeOffset.UtcNow);
            await database.UpsertAccountAsync(account);
            var key = new TradeKey(account.Scope.AccountKey, 17);
            var now = account.CapturedAtUtc;
            var trade = new TradeRecord(key.AccountKey, key.PositionId, "TEST", TradeSide.Buy, now, null,
                DateOnly.FromDateTime(now.UtcDateTime), null, 100m, null, 1m, 1m, 0m, 0m, false);
            await database.SaveTradesAsync([trade, trade with { PositionId = 18 }]);
            var sessions = new AccountSessionCoordinator();
            var session = await sessions.SwitchAsync(key.AccountKey);
            var attachments = Path.Combine(root, "attachments");
            var store = new ReviewAttachmentStore(database, attachments);
            var scheduler = new SystemAsyncScheduler(TimeProvider.System);
            var runtime = new TradePetRuntime(new MainViewModel(scheduler), new(database, database,
                new ReviewPackageWriter(), store, new ReviewBackupService(database, attachments, databasePath),
                TimeProvider.System, scheduler, sessions, new MaintenanceCoordinator(), Path.Combine(root, "backups")));
            var source = Path.Combine(root, "entry.png");
            await File.WriteAllBytesAsync(source, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jF1sAAAAASUVORK5CYII="));
            return new Fixture { Runtime = runtime, Database = database, DatabasePath = databasePath,
                Store = store, Sessions = sessions, Key = key, Generation = session.Generation, Source = source };
        }

        public Task<ReviewAttachment> ImportAsync(string? path = null) =>
            (Task<ReviewAttachment>)typeof(TradePetRuntime).GetMethod("ImportEntryScreenshotAsync", Flags)!
                .Invoke(Runtime, [Key, Generation, path ?? Source, "Entry screenshot test"])!;

        public void SetWritable(bool writable) => typeof(TradePetRuntime).GetField("_persistenceAvailable", Flags)!
            .SetValue(Runtime, writable);

        public ValueTask DisposeAsync() => Runtime.DisposeAsync();
    }
}
