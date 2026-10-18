using TradePet.Core.Domain;

namespace TradePet.App.Runtime;

public sealed partial class TradePetRuntime
{
    private (string? Account, DateOnly Date, int Offset)? _analyticsContext;
    private TradeRecord[]? _analyticsTrades;
    private TradeRecord[]? _analyticsDailyTrades;
    private DealRecord[]? _analyticsDailyDeals;
    private decimal? _analyticsRealizedPnl;
    private decimal? _analyticsHistoricalHigh;
    private BehaviorSummary? _analyticsBehavior;
    private BehaviorPolicy? _analyticsBehaviorPolicy;
    private long _analyticsRevision;
    private long _cachedAnalyticsRevision = -1;

    private void InvalidateAnalytics() => Interlocked.Increment(ref _analyticsRevision);

    private void EnsureAnalyticsContext()
    {
        var context = (_account?.Scope.AccountKey, _serverDate, _serverUtcOffsetSeconds);
        var revision = Volatile.Read(ref _analyticsRevision);
        if (_analyticsContext == context && _cachedAnalyticsRevision == revision) return;
        _analyticsTrades = null;
        _analyticsDailyTrades = null;
        _analyticsDailyDeals = null;
        _analyticsRealizedPnl = null;
        _analyticsHistoricalHigh = null;
        _analyticsBehavior = null;
        _analyticsBehaviorPolicy = null;
        _analyticsContext = context;
        _cachedAnalyticsRevision = revision;
    }

    private TradeRecord[] AnalyticsTrades()
    {
        EnsureAnalyticsContext();
        return _analyticsTrades ??= _trades.Values.ToArray();
    }

    private DealRecord[] AnalyticsDailyDeals()
    {
        EnsureAnalyticsContext();
        return _analyticsDailyDeals ??= _deals.Values
            .Where(deal => ResolveServerDate(deal.OccurredAtUtc) == _serverDate).ToArray();
    }

    private TradeRecord[] AnalyticsDailyTrades()
    {
        EnsureAnalyticsContext();
        return _analyticsDailyTrades ??= AnalyticsTrades()
            .Where(trade => trade.OpenServerDate == _serverDate || trade.CloseServerDate == _serverDate).ToArray();
    }
}
