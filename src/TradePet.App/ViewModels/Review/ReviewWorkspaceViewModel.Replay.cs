using System.Collections.ObjectModel;
using System.Windows.Input;
using TradePet.Application.Review;
using TradePet.Core.Domain;
using TradePet.Core.Localization;
using TradePet.Core.Review;

namespace TradePet.App.ViewModels.Review;

public sealed partial class ReviewWorkspaceViewModel
{
    private string _replayTimeframe = "M5", _secondaryReplayTimeframe = "H1";
    private bool _dualReplayEnabled, _isReplayLoading;
    private ReplayFrame? _secondaryReplayFrame;
    private ReplayChartWindow? _replayWindow;
    private DateTimeOffset? _replayCrosshairUtc, _replayEntryUtc, _replayExitUtc;
    private long _replayPlaybackVersion;
    private string _holdingProcessSummary = string.Empty;
    public IReadOnlyList<string> ReplayTimeframeOptions { get; } = ["M1", "M5", "M15", "H1"];
    public long ReplayRequestVersion { get; private set; }
    public DateTimeOffset? ReplayResumeAtUtc { get; private set; }
    public string ReplayTimeframe
    {
        get => _replayTimeframe;
        set { if (ReplayTimeframeOptions.Contains(value) && SetProperty(ref _replayTimeframe, value)) { RaisePropertyChanged(nameof(ReplayDefaultWindowMinutes)); ReplayOptionsChanged(); } }
    }
    public string SecondaryReplayTimeframe
    {
        get => _secondaryReplayTimeframe;
        set { if (ReplayTimeframeOptions.Contains(value) && SetProperty(ref _secondaryReplayTimeframe, value) && DualReplayEnabled) ReplayOptionsChanged(); }
    }
    public bool DualReplayEnabled
    {
        get => _dualReplayEnabled;
        set { if (SetProperty(ref _dualReplayEnabled, value)) ReplayOptionsChanged(); }
    }
    public bool IsReplayLoading { get => _isReplayLoading; private set => SetProperty(ref _isReplayLoading, value); }
    public int ReplayDefaultWindowMinutes => TimeframeMinutes(ReplayTimeframe) * 80;
    public ReplayFrame? SecondaryReplayFrame { get => _secondaryReplayFrame; private set => SetProperty(ref _secondaryReplayFrame, value); }
    public ReplayChartWindow? ReplayWindow { get => _replayWindow; set => SetProperty(ref _replayWindow, value); }
    public DateTimeOffset? ReplayCrosshairUtc { get => _replayCrosshairUtc; set => SetProperty(ref _replayCrosshairUtc, value); }
    public string HoldingProcessSummary { get => _holdingProcessSummary; private set => SetProperty(ref _holdingProcessSummary, value); }
    public ObservableCollection<ReplayJumpRow> ReplayJumpEvents { get; } = [];
    public ObservableCollection<HoldingProcessGroupRow> HoldingProcessGroups { get; } = [];
    public ICommand ResetReplayViewCommand { get; private set; } = null!;
    public ICommand ReplayEntryCommand { get; private set; } = null!;
    public ICommand ReplayExitCommand { get; private set; } = null!;

    private void InitializeReplayTools()
    {
        ResetReplayViewCommand = new RelayCommand(() => { ReplayWindow = null; ReplayCrosshairUtc = null; });
        ReplayEntryCommand = Command(() => JumpReplayAsync(_replayEntryUtc));
        ReplayExitCommand = Command(() => JumpReplayAsync(_replayExitUtc));
    }

    public static int TimeframeMinutes(string timeframe) => timeframe switch { "M1" => 1, "M15" => 15, "H1" => 60, _ => 5 };

    private void ReplayOptionsChanged()
    {
        if (_applyingDetail) return;
        if (ReplayChartFrame is not null || IsReplayLoading) _ = ReloadReplayAsync();
        else ResetReplayPresentation(false);
    }

    private async Task ReloadReplayAsync()
    {
        ResetReplayPresentation(false);
        var version = ReplayRequestVersion;
        IsReplayLoading = true;
        try { await (LoadReplayAsync?.Invoke() ?? Task.CompletedTask); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (version == ReplayRequestVersion) ReplayStatus = UiText.Translate("行情加载失败：") + exception.Message; }
        finally { if (version == ReplayRequestVersion) IsReplayLoading = false; }
    }

    private void ResetReplayPresentation(bool resetTrade)
    {
        ReplayRequestVersion++;
        IsReplayPlaying = false;
        IsReplayLoading = false;
        ReplayChartFrame = null;
        SecondaryReplayFrame = null;
        ReplayWindow = null;
        ReplayCrosshairUtc = null;
        ReplayJumpEvents.Clear();
        HoldingProcessSummary = string.Empty;
        if (resetTrade)
        {
            ReplayResumeAtUtc = null;
            ReplayChartFrom = ReplayChartTo = null;
            _replayEntryUtc = _replayExitUtc = null;
        }
    }

    private async Task JumpReplayAsync(DateTimeOffset? at)
    {
        if (at is null || ReplayChartFrame is null || _replayEndUtc <= _replayStartUtc || SeekReplayAsync is null) return;
        IsReplayPlaying = false;
        ReplayWindow = null;
        await SeekReplayAsync(Math.Clamp((at.Value - _replayStartUtc).TotalMilliseconds / (_replayEndUtc - _replayStartUtc).TotalMilliseconds * 100, 0, 100));
    }

    private void ApplyReplayTools(ReplayFrame frame, ReplayFrame? secondary)
    {
        SecondaryReplayFrame = secondary;
        ReplayResumeAtUtc = frame.CursorUtc;
        var analysis = HoldingProcessAnalysis.FromFrame(frame);
        string Number(decimal? value) => value is { } amount ? Signed(amount) : "—";
        HoldingProcessSummary = string.Format(UiText.Get("ReplayHoldingSummary"),
            Number(analysis.ObservedPeak), Number(analysis.ObservedTrough), Number(analysis.FinalNetPnl), Number(analysis.Giveback), analysis.Samples.Count);
        if (analysis.HasGaps) HoldingProcessSummary += " · " + UiText.Get("ReplayHoldingGaps");
        var events = (frame.NavigationEvents ?? []).Where(item => item.AtUtc <= frame.CursorUtc && item.Kind is not ("K线完成" or "盈亏采样"))
            .TakeLast(100).Select(item => new ReplayJumpRow($"{BrokerTime(item.AtUtc):MM-dd HH:mm:ss} · {UiText.Translate(item.Kind)} · {UiText.Translate(item.Summary)}",
                Command(() => JumpReplayAsync(item.AtUtc))));
        ReplayJumpEvents.ReplaceWith(events);
    }

    private void ApplyHoldingProcessGroups(ReviewWorkspaceSnapshot snapshot, ReviewWorkspaceData data)
    {
        HoldingProcessGroups.Clear();
        var trades = data.Trades.ToDictionary(trade => trade.PositionId);
        var samples = (snapshot.RiskSamples ?? []).Where(sample => trades.ContainsKey(sample.Trade.PositionId)).ToArray();
        AddGroups(UiText.Get("ReplayStrategyGroup"), sample => data.Metadata.GetValueOrDefault(sample.Trade.PositionId)?.Strategy is { Length: > 0 } strategy ? strategy : UiText.Get("ReplayUnclassified"));
        AddGroups(UiText.Get("ReplayHourGroup"), sample => BrokerTime(trades[sample.Trade.PositionId].OpenedAtUtc).ToString("HH':00'"));
        void AddGroups(string kind, Func<ReviewRiskSample, string> key)
        {
            foreach (var group in samples.GroupBy(key).OrderBy(group => group.Key))
            {
                var covered = group.Where(sample => sample.HasReliableExcursion && sample.Mae.HasValue && sample.Mfe.HasValue).ToArray();
                var closed = covered.Where(sample => trades[sample.Trade.PositionId].ClosedAtUtc is not null && sample.Mfe > 0).ToArray();
                HoldingProcessGroups.Add(new(kind, group.Key, $"{covered.Length}/{group.Count()}",
                    covered.Length == 0 ? "—" : Signed(covered.Average(sample => sample.Mae!.Value)),
                    covered.Length == 0 ? "—" : Signed(covered.Average(sample => sample.Mfe!.Value)),
                    closed.Length == 0 ? "—" : Signed(closed.Average(sample => Math.Max(0, sample.Mfe!.Value - sample.NetPnl)))));
            }
        }
    }
}

public sealed record ReplayJumpRow(string Label, ICommand JumpCommand);
public sealed record HoldingProcessGroupRow(string Kind, string Group, string Coverage, string AverageMae, string AverageMfe, string AverageGiveback);
