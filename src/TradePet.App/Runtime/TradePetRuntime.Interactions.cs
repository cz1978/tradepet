using System.Collections.Concurrent;
using System.Diagnostics;
using TradePet.App.Views;
using TradePet.Application.Review;
using TradePet.Application.Runtime;
using TradePet.Core.Domain;

namespace TradePet.App.Runtime;

public sealed partial class TradePetRuntime
{
    private readonly ConcurrentDictionary<string, TradeRecord> _pendingEntryReasons = new(StringComparer.Ordinal);
    private EntryReasonCard? _entryReasonCard;
    public Action<EntryReasonCard>? ShowEntryReasonCard { get; set; }
    public Func<bool>? CanShowAutomaticPrompt { get; set; }
    private readonly GitHubReleaseChecker _releaseChecker = new();
    private readonly SemaphoreSlim _updateCheckGate = new(1, 1);
    private string? _lastNotifiedRelease;
    private Uri _releasePage = new(GitHubReleaseChecker.ReleasesUrl);

    private async Task TryShowAutomaticPromptAsync()
    {
        if (_cancellation.IsCancellationRequested) return;
        var canShow = false;
        await OnUiAsync(() => canShow = !_viewModel.IsFocusMode &&
            _quickReviewCard is null && _entryReasonCard is null && _behaviorActionCard is null &&
            Volatile.Read(ref _openingBehaviorAction) == 0 && (CanShowAutomaticPrompt?.Invoke() ?? true));
        if (!canShow) return;
        try
        {
            var accountKey = _account?.Scope.AccountKey;
            if (_viewModel.QuickReviewPromptEnabled && _pendingQuickReviews.Values.Any(trade => trade.AccountKey == accountKey))
                await ShowQuickReviewAsync(showEmptyMessage: false, automatic: true);
            if (_quickReviewCard is null && _viewModel.EntryReasonPromptEnabled)
                await ShowEntryReasonAsync(showEmptyMessage: false);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception exception) { AppLog.Write($"Automatic trading prompt failed: {exception}"); }
    }

    public Task ShowEntryReasonAsync() => ShowEntryReasonAsync(showEmptyMessage: true);

    private async Task ShowEntryReasonAsync(bool showEmptyMessage)
    {
        if (_behaviorActionCard is not null) { await OnUiAsync(() => ShowBehaviorActionCard?.Invoke(_behaviorActionCard)); return; }
        if (_quickReviewCard is not null) { await OnUiAsync(() => ShowQuickReviewCard?.Invoke(_quickReviewCard)); return; }
        if (_entryReasonCard is not null) { await OnUiAsync(() => ShowEntryReasonCard?.Invoke(_entryReasonCard)); return; }
        var session = _accountSessions.Current;
        if (!_persistenceAvailable || session is null || _account?.Scope.AccountKey != session.AccountKey) return;
        if (showEmptyMessage)
            foreach (var trade in _trades.Values.Where(trade => trade.AccountKey == session.AccountKey && !trade.IsComplete))
                _pendingEntryReasons.TryAdd($"{trade.AccountKey}|{trade.PositionId}", trade);
        foreach (var pending in _pendingEntryReasons.Where(item => item.Value.AccountKey == session.AccountKey)
                     .OrderBy(item => item.Value.OpenedAtUtc))
        {
            var trade = pending.Value;
            var key = new TradeKey(trade.AccountKey, trade.PositionId);
            var note = await _database.LoadSettingAsync<TradeEntryReasonNote>(TradeEntryReasonNote.Scope(key.AccountKey),
                TradeEntryReasonNote.SettingKey(key.PositionId), _cancellation.Token);
            if (note?.TradeKey == key && !string.IsNullOrWhiteSpace(note.Reason))
            { _pendingEntryReasons.TryRemove(pending.Key, out _); continue; }
            await OnUiAsync(() =>
            {
                if (!_accountSessions.IsCurrent(session.AccountKey, session.Generation) || _entryReasonCard is not null || _quickReviewCard is not null || _behaviorActionCard is not null ||
                    !showEmptyMessage && !_viewModel.EntryReasonPromptEnabled) return;
                var card = new EntryReasonCard(trade, _serverUtcOffsetSeconds);
                card.SaveReasonAsync = async response =>
                {
                    await using var lease = await EnterRuntimeOperationAsync(MaintenanceOperationKind.Write, _cancellation.Token);
                    if (!_persistenceAvailable || !_accountSessions.IsCurrent(key.AccountKey, session.Generation))
                        return "账户已切换或存储不可写，内容仍保留。";
                    await _database.SaveSettingAsync(TradeEntryReasonNote.Scope(key.AccountKey), TradeEntryReasonNote.SettingKey(key.PositionId),
                        new TradeEntryReasonNote(key, response.Reason, _timeProvider.GetUtcNow(),
                            response.ReportedExecution, response.Emotion), _cancellation.Token);
                    return null;
                };
                _entryReasonCard = card;
                card.Completed += response =>
                {
                    _pendingEntryReasons.TryRemove(pending.Key, out _);
                    _entryReasonCard = null;
                    _ = TryShowAutomaticPromptAsync();
                };
                ShowEntryReasonCard?.Invoke(card);
            });
            return;
        }
        if (showEmptyMessage)
            await ShowSpeechAsync("没有待记录的入场原因", "已记录的原因可在交易档案中查看。", TimeSpan.FromSeconds(5));
    }

    private async Task RunUpdateMonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_viewModel.UpdateNotificationsEnabled) await CheckForUpdatesAsync(manual: false);
            await _scheduler.DelayAsync(TimeSpan.FromHours(6), cancellationToken);
        }
    }

    public Task CheckForUpdatesAsync() => CheckForUpdatesAsync(manual: true);

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (!await _updateCheckGate.WaitAsync(0, _cancellation.Token)) return;
        try
        {
            await OnUiAsync(() => _viewModel.UpdateStatus = "正在检查 GitHub 发布版本…");
            var result = await _releaseChecker.CheckAsync(_cancellation.Token);
            _releasePage = result.ReleasePage;
            await OnUiAsync(() =>
            {
                _viewModel.UpdateAvailable = result.HasUpdate;
                _viewModel.UpdateStatus = result.HasUpdate
                    ? $"发现新版本 {result.LatestVersion}，可打开发布页查看与下载。"
                    : result.LatestVersion is null ? "未找到可下载的 Windows 发布版本。" : "未发现比当前版本更新的发布。";
            });
            if (!result.HasUpdate) return;
            if (_lastNotifiedRelease is null && _persistenceAvailable)
                _lastNotifiedRelease = await _database.LoadSettingAsync<string>(GlobalScope, "last-notified-release", _cancellation.Token);
            if (!_viewModel.UpdateNotificationsEnabled || _lastNotifiedRelease == result.LatestVersion) return;
            await ShowSpeechAsync($"发现新版本 {result.LatestVersion}", "宠物菜单或设置中心 → 打开发布页，可查看更新和下载。", TimeSpan.FromSeconds(8));
            _lastNotifiedRelease = result.LatestVersion;
            if (_persistenceAvailable)
            {
                await using var lease = await EnterRuntimeOperationAsync(MaintenanceOperationKind.Write, _cancellation.Token);
                await _database.SaveSettingAsync(GlobalScope, "last-notified-release", _lastNotifiedRelease, _cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Write($"GitHub update check failed: {exception}");
            await OnUiAsync(() => _viewModel.UpdateStatus = manual
                ? "检查失败，请确认网络后重试，或直接打开发布页。" : "自动检查未成功，可手动检查或打开发布页。");
        }
        finally { _updateCheckGate.Release(); }
    }

    public void OpenReleasePage() => Process.Start(new ProcessStartInfo(_releasePage.AbsoluteUri) { UseShellExecute = true });
}
