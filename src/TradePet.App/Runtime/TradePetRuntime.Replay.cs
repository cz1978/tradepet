using System.IO;
using TradePet.Application.Review;
using TradePet.Application.Runtime;
using TradePet.App.ViewModels.Review;
using TradePet.Core.Domain;
using TradePet.Infrastructure.Mt5;
using TradePet.Infrastructure.Persistence;

namespace TradePet.App.Runtime;

public sealed partial class TradePetRuntime
{
    private readonly SemaphoreSlim _replayLoadGate = new(1, 1);
    private CancellationTokenSource? _replayLoadCancellation;
    private MarketHistoryResult? _secondaryReplayHistory;
    private EditIdentity? _replayOwner;
    private long _replayVersion = -1;
    private string? _secondaryReplayError;

    private async Task LoadTradeReplayAsync()
    {
        var review = _viewModel.ReviewWorkspace;
        var detail = _lastTradeDetail;
        var identity = review.TradeEditorIdentity;
        var session = _accountSessions.Current;
        if (detail is null || identity is null || session is null ||
            detail.Trade.AccountKey != session.AccountKey || !identity.IsForTrade(new(detail.Trade.AccountKey, detail.Trade.PositionId)) ||
            identity.SessionGeneration != session.Generation || (!_offlineReviewOnly && _terminal is null))
        {
            review.ReplayStatus = "请先选择当前账户的一笔交易。";
            return;
        }
        var version = review.ReplayRequestVersion;
        var timeframe = review.ReplayTimeframe;
        var secondaryTimeframe = review.SecondaryReplayTimeframe;
        var dual = review.DualReplayEnabled;
        var precision = review.ReplayPrecision == "Tick" ? MarketDataPrecision.Ticks : MarketDataPrecision.Bars;
        var resume = review.ReplayResumeAtUtc ?? detail.Trade.OpenedAtUtc;
        _replayLoadCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token, session.CancellationToken);
        _replayLoadCancellation = cancellation;
        var token = cancellation.Token;
        bool Current() => !token.IsCancellationRequested && review.ReplayRequestVersion == version &&
            review.IsCurrentTradeEdit(identity) && _accountSessions.IsCurrent(session.AccountKey, session.Generation);
        try
        {
            await _replayLoadGate.WaitAsync(token);
            try
            {
                await using var lease = await EnterRuntimeOperationAsync(MaintenanceOperationKind.Read, token);
                if (!Current()) return;
                var paths = RuntimePaths.Resolve();
                if (!_offlineReviewOnly && _activePlatform != TradingPlatform.Mt4 && !File.Exists(paths.HistoryWorkerScript))
                    throw new InvalidOperationException("历史行情脚本缺失，请重新构建或安装应用。");
                review.ReplayStatus = _offlineReviewOnly ? "正在读取离线行情缓存…" : "正在从所选交易终端读取历史行情…";
                var lookback = Math.Max(ReviewWorkspaceViewModel.TimeframeMinutes(timeframe) * 80,
                    dual ? ReviewWorkspaceViewModel.TimeframeMinutes(secondaryTimeframe) * 24 : 60);
                var from = detail.Trade.OpenedAtUtc.AddMinutes(-lookback);
                var to = (detail.Trade.ClosedAtUtc ?? detail.Trade.OpenedAtUtc.AddHours(4)).AddHours(1);
                IMarketHistorySource client = _offlineReviewOnly ? new OfflineMarketSource(_database) : _activePlatform == TradingPlatform.Mt4
                    ? new TradePet.Infrastructure.Mt4.Mt4MarketHistoryClient(_terminal!.TerminalPath, _terminal.DataDirectory!, _serverUtcOffsetSeconds,
                        TradePetPaths.GetCollectorDataDirectory())
                    : new Mt5HistoryClient(new Mt5HistoryOptions(paths.PythonExecutable, paths.HistoryWorkerScript, _terminal!.TerminalPath, _terminal.TerminalId));
                var service = new TradeReplayService(_database, client);
                async Task<MarketHistoryResult> Load(string period, MarketDataPrecision mode)
                {
                    var request = new MarketHistoryRequest(Guid.NewGuid().ToString("N"), _terminal?.TerminalId ?? "offline",
                        session.AccountKey, detail.Trade.Symbol, period, from, to, mode);
                    return _offlineReviewOnly ? await client.LoadAsync(request, token) : await service.LoadAsync(request, token);
                }
                var history = await Load(timeframe, precision);
                if (!Current()) return;
                MarketHistoryResult? secondary = null;
                string? secondaryError = null;
                if (dual)
                {
                    try { secondary = timeframe == secondaryTimeframe && precision == MarketDataPrecision.Bars ? history : await Load(secondaryTimeframe, MarketDataPrecision.Bars); }
                    catch (Exception exception) when (exception is not OperationCanceledException) { secondaryError = exception.Message; }
                }
                if (!Current()) return;
                var cursor = new DateTimeOffset(Math.Clamp(resume.UtcTicks, from.UtcTicks, to.UtcTicks), TimeSpan.Zero);
                var frame = CreateReplayFrame(cursor, history, detail, review.ShowFullReplayReview);
                var secondaryFrame = secondary is null ? null : CreateReplayFrame(cursor, secondary, detail, review.ShowFullReplayReview);
                await OnUiAsync(() =>
                {
                    if (!Current()) return;
                    _lastReplayHistory = history;
                    _secondaryReplayHistory = secondary;
                    _replayOwner = identity;
                    _replayVersion = version;
                    _secondaryReplayError = secondaryError;
                    review.ApplyReplay(history, frame, (cursor - from).TotalMilliseconds / (to - from).TotalMilliseconds * 100, secondaryFrame);
                    if (secondaryError is not null) review.ReplayStatus += " · 副图加载失败：" + secondaryError;
                });
            }
            finally { _replayLoadGate.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_replayLoadCancellation, cancellation)) _replayLoadCancellation = null; }
    }

    private ReplayFrame CreateReplayFrame(DateTimeOffset cursor, MarketHistoryResult history, TradeDetailData detail, bool reveal)
    {
        var notes = detail.Document is null ? Array.Empty<ReviewEvidenceStamp>()
            : [new ReviewEvidenceStamp(ReviewEvidenceSource.UserBackfill, detail.Trade.OpenedAtUtc,
                detail.Document.UpdatedAtUtc, ReviewTimeBasis.Local, detail.Document.SourceVersion)];
        return _reviewWorkspaceCalculator.BuildReplayFrame(cursor, history.Range, history.Bars, history.Ticks,
            detail.Deals, detail.PnlSamples, detail.Behaviors, notes, detail.Trade, detail.Plan, detail.Document, reveal);
    }

    private async Task SeekTradeReplayAsync(double progress)
    {
        var review = _viewModel.ReviewWorkspace;
        var history = _lastReplayHistory;
        var detail = _lastTradeDetail;
        var owner = _replayOwner;
        var version = _replayVersion;
        bool Current() => owner is not null && review.IsCurrentTradeEdit(owner) && review.ReplayRequestVersion == version &&
            _accountSessions.IsCurrent(owner.AccountKey, owner.SessionGeneration);
        if (history is null || detail is null || !Current() || detail.Trade.AccountKey != owner!.AccountKey ||
            detail.Trade.PositionId.ToString(System.Globalization.CultureInfo.InvariantCulture) != owner.EntityId) return;
        var from = history.Range.RequestedFromUtc;
        var to = history.Range.RequestedToUtc;
        progress = double.IsFinite(progress) ? Math.Clamp(progress, 0, 100) : 0;
        var cursor = from.AddTicks((long)((to - from).Ticks * progress / 100));
        var frame = CreateReplayFrame(cursor, history, detail, review.ShowFullReplayReview);
        var secondary = _secondaryReplayHistory is null ? null : CreateReplayFrame(cursor, _secondaryReplayHistory, detail, review.ShowFullReplayReview);
        await OnUiAsync(() =>
        {
            if (!Current()) return;
            review.ApplyReplay(history, frame, progress, secondary);
            if (_secondaryReplayError is not null) review.ReplayStatus += " · 副图加载失败：" + _secondaryReplayError;
        });
    }
}
