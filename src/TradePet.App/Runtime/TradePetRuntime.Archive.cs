using TradePet.App.ViewModels;
using TradePet.App.Views;
using TradePet.Application.Review;
using TradePet.Application.Runtime;
using TradePet.Core.Domain;
using TradePet.Infrastructure.Persistence;

namespace TradePet.App.Runtime;

public sealed partial class TradePetRuntime
{
    private bool _offlineReviewOnly;
    private OfflineArchiveWindow? _offlineArchiveWindow;

    private async Task OpenOfflineArchiveAsync()
    {
        if (_offlineArchiveWindow is not null) { _offlineArchiveWindow.Show(); _offlineArchiveWindow.Activate(); return; }
        var accounts = await _database.LoadLocalAccountsAsync(_cancellation.Token);
        await OnUiAsync(() =>
        {
            var model = new MainViewModel(_scheduler, _timeProvider) { SelectedReviewPeriod = "自定义" };
            var runtime = new TradePetRuntime(model, new(_database, _reviewRepository, _reviewPackageWriter,
                _reviewAttachmentStore, _reviewBackupService, _timeProvider, _scheduler,
                new AccountSessionCoordinator(_cancellation.Token), _maintenance)) { _offlineReviewOnly = true };
            var window = new OfflineArchiveWindow(model, runtime, accounts);
            _offlineArchiveWindow = window;
            window.Closed += (_, _) => _offlineArchiveWindow = null;
            window.Show();
        });
    }

    internal async Task SelectOfflineAccountAsync(LocalAccountArchive archive)
    {
        if (!_offlineReviewOnly) throw new InvalidOperationException("This runtime is connected to live data.");
        await WithStateGateAsync(async () =>
        {
            await _accountSessions.SwitchAsync(archive.Scope.AccountKey, _cancellation.Token);
            _reviewQueryCancellation?.Cancel();
            _tradeDetailCancellation?.Cancel();
            _lastWorkspaceQuery = null; _lastTradeDetail = null; _lastReplayHistory = null;
            _trades.Clear(); _reviewMetadata.Clear(); _excursions.Clear(); _lossZones.Clear(); _lossZoneAttempts.Clear();
            _account = new(archive.Scope, archive.Currency, 0m, 0m, 0m, -1, _timeProvider.GetUtcNow());
            _serverClock.ApplyWorkerOffset(archive.TimeSegment?.UtcOffsetSeconds ?? 0);
            _behaviorPolicies = DisablePlanDeviation(await _database.LoadSettingAsync<BehaviorPolicySet>(
                $"account:{archive.Scope.AccountKey}", BehaviorSettingKey, _cancellation.Token) ??
                BehaviorPolicySet.CreateDefault(archive.Scope.AccountKey));
            _symbolSpecifications.Clear();
            foreach (var specification in await _database.LoadSymbolSpecificationsAsync(archive.Scope.AccountKey, _cancellation.Token))
                _symbolSpecifications[specification.Key] = specification.Value;
            await OnUiAsync(() =>
            {
                _viewModel.ResetReviewForAccountChange("正在读取离线账户档案…");
                _viewModel.ReviewWorkspace.ServerUtcOffsetSeconds = _serverUtcOffsetSeconds;
                _viewModel.ReviewFromDateText = (archive.FirstTradeDate ?? _serverDate.AddDays(-30)).ToString("yyyy-MM-dd");
                _viewModel.ReviewToDateText = (archive.LastTradeDate ?? _serverDate).ToString("yyyy-MM-dd");
            });
        });
        await RefreshReviewAsync();
        var query = Volatile.Read(ref _reviewRefreshTask);
        if (query is not null) await query;
    }

    private sealed class OfflineMarketSource(AppDatabase database) : IMarketHistorySource
    {
        public Task<MarketHistoryResult> LoadAsync(MarketHistoryRequest request, CancellationToken cancellationToken = default) =>
            database.LoadOfflineMarketDataAsync(request, cancellationToken);
    }
}
