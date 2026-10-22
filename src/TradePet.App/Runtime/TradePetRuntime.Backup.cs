using System.IO;
using TradePet.Core.Localization;
using TradePet.Infrastructure.Mt4;
using TradePet.Infrastructure.Mt5;
using TradePet.Infrastructure.Persistence;

namespace TradePet.App.Runtime;

public sealed partial class TradePetRuntime
{
    private string? _automaticBackupDirectory;
    private string? _archiveMigrationError;
    private async Task InitializeManagedArchivesAsync()
    {
        try { await ImportLegacyArchivesCoreAsync(); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _archiveMigrationError = exception.Message;
            AppLog.Write($"Legacy archive migration failed; original files retained: {exception}");
            await OnUiAsync(() =>
            {
                _viewModel.AutomaticBackupStatus = UiText.Get("FunctionalMigrationFailed");
                _viewModel.DiagnosticText = UiText.Get("FunctionalMigrationFailed");
            });
        }
    }

    private async Task ImportLegacyArchivesCoreAsync()
    {
        await using var lease = await _maintenance.EnterOperationAsync(TradePet.Application.Runtime.MaintenanceOperationKind.File, _cancellation.Token);
        await Mt4ArchiveMigration.ImportAsync(_terminalDiscovery.Discover(_viewModel.SelectedTerminalPath, TradingPlatform.Mt4)
                .Select(item => item.DataDirectory).Where(path => path is not null).Cast<string>(),
            TradePetPaths.GetCollectorDataDirectory(), _cancellation.Token);
        var legacyReports = Path.Combine(TradePetPaths.GetDataDirectory(), "reports");
        if (!Directory.Exists(legacyReports)) return;
        foreach (var file in Directory.EnumerateFiles(legacyReports, "*.md", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(TradePetPaths.GetReportDirectory(), Path.GetRelativePath(legacyReports, file));
            if (File.Exists(destination)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: false);
        }
    }

    private async Task RunAutomaticBackupAsync(CancellationToken cancellationToken)
    {
        await _scheduler.DelayAsync(TimeSpan.FromSeconds(45), cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await CreateAutomaticBackupIfDueAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                AppLog.Write($"Automatic backup failed: {exception}");
                await OnUiAsync(() => _viewModel.AutomaticBackupStatus = UiText.Get("FunctionalBackupFailed"));
            }
            await _scheduler.DelayAsync(TimeSpan.FromHours(1), cancellationToken);
        }
    }

    private async Task CreateAutomaticBackupIfDueAsync(CancellationToken cancellationToken)
    {
        if (!_viewModel.AutomaticBackupEnabled || !_persistenceAvailable || _databaseRecoveryRequired || _archiveMigrationError is not null) return;
        var day = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime).ToString("yyyy-MM-dd");
        var last = await _database.LoadSettingAsync<string>(GlobalScope, "automatic-backup-day-v1", cancellationToken);
        if (last == day) return;
        await using var maintenance = await _maintenance.EnterMaintenanceAsync(cancellationToken);
        var directory = _automaticBackupDirectory ?? Path.Combine(TradePetPaths.GetDatabaseBackupDirectory(), "automatic");
        var destination = await _reviewBackupService.CreateAsync(directory, cancellationToken);
        await _reviewBackupService.ValidateAsync(destination, cancellationToken);
        await _database.SaveSettingAsync(GlobalScope, "automatic-backup-day-v1", day, cancellationToken);
        foreach (var old in new DirectoryInfo(directory).EnumerateFiles("TradePet-backup-*.zip")
                     .OrderByDescending(item => item.LastWriteTimeUtc).Skip(7)) old.Delete();
        await OnUiAsync(() => _viewModel.AutomaticBackupStatus = UiText.Get("FunctionalBackupSuccess") + destination);
    }
}
