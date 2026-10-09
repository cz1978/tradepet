using System.Globalization;
using System.Text;
using TradePet.App.Views;
using TradePet.Application.Runtime;
using TradePet.Core.Domain;
using TradePet.Core.Localization;
using TradePet.Core.Review;

namespace TradePet.App.Runtime;

public sealed partial class TradePetRuntime
{
    private BehaviorActionCard? _behaviorActionCard;
    private int _openingBehaviorAction;
    public Action<BehaviorActionCard>? ShowBehaviorActionCard { get; set; }

    private async Task<bool> FocusExistingPetInputAsync()
    {
        var found = false;
        await OnUiAsync(() =>
        {
            if (_behaviorActionCard is not null) { found = true; ShowBehaviorActionCard?.Invoke(_behaviorActionCard); }
            else if (_quickReviewCard is not null) { found = true; ShowQuickReviewCard?.Invoke(_quickReviewCard); }
            else if (_entryReasonCard is not null) { found = true; ShowEntryReasonCard?.Invoke(_entryReasonCard); }
        });
        return found;
    }

    private void PresentBehaviorAction(BehaviorActionCard card)
    {
        _behaviorActionCard = card;
        card.Completed += response =>
        {
            if (_behaviorActionCard == response) _behaviorActionCard = null;
            _ = TryShowAutomaticPromptAsync();
        };
        ShowBehaviorActionCard?.Invoke(card);
    }

    public async Task ShowOpportunityAsync()
    {
        if (await FocusExistingPetInputAsync()) return;
        var session = _accountSessions.Current;
        if (!_persistenceAvailable || session is null || !_serverDateAuthoritative)
        {
            await ShowSpeechAsync("暂不能记录机会", "请等待账户连接和服务器时间就绪。", TimeSpan.FromSeconds(5));
            return;
        }
        var symbols = _positions.Values.Select(item => item.Symbol)
            .Concat(_trades.Values.Where(item => item.AccountKey == session.AccountKey).OrderByDescending(item => item.OpenedAtUtc).Select(item => item.Symbol))
            .Concat(_symbolSpecifications.Keys).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (symbols.Length == 0)
        {
            await ShowSpeechAsync("暂无可选品种", "连接终端并载入品种后即可点选记录。", TimeSpan.FromSeconds(5));
            return;
        }
        await OnUiAsync(() =>
        {
            if (!_accountSessions.IsCurrent(session.AccountKey, session.Generation) ||
                _behaviorActionCard is not null || _quickReviewCard is not null || _entryReasonCard is not null) return;
            var card = new BehaviorActionCard("记录未交易机会", "点选后保存。未记录的价格和交易条件保留为空，事后发现单独统计。", "保存机会",
            [
                new("symbol", "品种", symbols.Select(item => new PetActionChoice(item, item)).ToArray()),
                new("kind", "记录类型", [new("ObservedBeforeMove", "当时记录"), new("DeliberatelySkipped", "主动跳过"), new("DiscoveredAfterMove", "事后发现")]),
                new("reason", "未交易原因", [new("条件未齐", "条件未齐"), new("风险限制", "风险限制"), new("遵守冷静期", "遵守冷静期"), new("犹豫未做", "犹豫未做"), new("事后发现", "事后发现")]),
                new("side", "方向（选填）", [new("Buy", "买入"), new("Sell", "卖出")]),
            ]);
            card.SaveActionAsync = async response =>
            {
                var symbol = response.Selection("symbol");
                var reason = response.Selection("reason");
                if (symbol is null || reason is null || !Enum.TryParse<OpportunityRecordKind>(response.Selection("kind"), out var kind))
                    return "请选择品种、记录类型和未交易原因。";
                if (reason == "事后发现" && kind != OpportunityRecordKind.DiscoveredAfterMove)
                    return "事后发现请使用“事后发现”类型。";
                await using var lease = await EnterRuntimeOperationAsync(MaintenanceOperationKind.Write, _cancellation.Token);
                if (!_persistenceAvailable || !_accountSessions.IsCurrent(session.AccountKey, session.Generation))
                    return "账户已切换或存储不可写，内容仍保留。";
                var now = _timeProvider.GetUtcNow();
                var date = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromSeconds(_serverUtcOffsetSeconds)).DateTime);
                var side = Enum.TryParse<TradeSide>(response.Selection("side"), out var direction) ? (TradeSide?)direction : null;
                var result = await _opportunityService.SaveAsync(new($"opportunity-{Guid.NewGuid():N}", session.AccountKey,
                    kind, now, now, date, symbol, side, null, null, null, null, reason, string.Empty, null, 0), 0, _cancellation.Token);
                if (!result.IsSaved) return result.Message;
                return null;
            };
            PresentBehaviorAction(card);
        });
    }

    public async Task ShowWeeklyGoalAsync()
    {
        if (await FocusExistingPetInputAsync() || Interlocked.CompareExchange(ref _openingBehaviorAction, 1, 0) != 0) return;
        try
        {
            var session = _accountSessions.Current;
            if (!_persistenceAvailable || session is null || !_serverDateAuthoritative)
            {
                await ShowSpeechAsync("暂不能生成交易小结", "请等待账户连接和服务器时间就绪。", TimeSpan.FromSeconds(5));
                return;
            }
            var date = _serverDate;
            var from = date.AddDays(-6);
            var policy = _behaviorPolicies?.Selected ?? BehaviorPolicy.Balanced;
            var version = BehaviorGoalMeasurement.PolicyVersion(policy);
            var data = await _reviewRepository.LoadWorkspaceAsync(session.AccountKey, from, date, _cancellation.Token);
            var documents = _reviewWorkspaceCalculator.ProjectReviewStatuses(data.Trades, data.Deals, data.Documents,
                data.Assessments, data.Excursions, data.Version);
            var trades = data.Trades.Where(item => item.AccountKey == session.AccountKey && item.IsComplete &&
                item.CloseServerDate >= from && item.CloseServerDate <= date).ToArray();
            var behaviors = data.Behaviors.Where(item => item.AccountKey == session.AccountKey && !item.EvidenceInsufficient &&
                string.IsNullOrWhiteSpace(item.MissingData) && item.Level is BehaviorRiskLevel.Attention or BehaviorRiskLevel.Critical).ToArray();
            var summary = new StringBuilder();
            summary.AppendLine(UiText.Translate($"{from:yyyy-MM-dd} 至 {date:yyyy-MM-dd} 服务器日"));
            summary.AppendLine(UiText.Translate($"完整平仓 {trades.Length} 笔 · 净盈亏 {trades.Sum(item => item.NetPnl):+0.##;-0.##;0} {data.Currency}"));
            summary.AppendLine(UiText.Translate($"已复盘 {trades.Count(item => documents.GetValueOrDefault(item.PositionId)?.HasCompletedReview == true)} / {trades.Length} 笔；快速复盘保存即完成。"));
            var reported = trades.Select(item => documents.GetValueOrDefault(item.PositionId)).Where(item => item?.ReportedExecution is not null).ToArray();
            var exits = trades.Select(item => documents.GetValueOrDefault(item.PositionId)?.ReportedExitExecution).ToArray();
            summary.AppendLine(UiText.Translate($"退出自报：按规则 {exits.Count(item => item == ExitExecutionSelfReport.Followed)} 笔 · 偏离 {exits.Count(item => item == ExitExecutionSelfReport.Deviated)} 笔 · 未预设 {exits.Count(item => item == ExitExecutionSelfReport.NoPreset)} 笔 · 不确定 {exits.Count(item => item == ExitExecutionSelfReport.Unsure)} 笔；未填写不推断。"));
            if (reported.Length > 0)
                summary.AppendLine(UiText.Translate($"整笔执行自报 {reported.Length} 笔，其中按计划 {reported.Count(item => item!.ReportedExecution == PlanExecutionSelfReport.Followed)} 笔；未自报不推断。"));
            var tradeLookup = trades.ToDictionary(item => new TradeKey(item.AccountKey, item.PositionId));
            foreach (var group in behaviors.GroupBy(item => item.Rule).OrderByDescending(item => item.Count()).Take(3))
            {
                var impact = _reviewWorkspaceCalculator.CalculateBehaviorImpact(group.ToArray(), tradeLookup, group.Key);
                summary.AppendLine(UiText.Translate($"{PetGoalName(group.Key)} · 提醒 {impact.OccurrenceCount} 次 · 关联完整平仓 {impact.UniqueTradeCount} 笔 · 净盈亏 {impact.RelatedNetPnl:+0.##;-0.##;0}"));
            }
            summary.AppendLine(UiText.Translate("关联盈亏按每笔去重，是共同出现的记录，不代表该行为造成的损失。"));
            var active = data.Goals.Where(item => item.AccountKey == session.AccountKey && item.Status == ImprovementGoalStatus.Active &&
                item.StartServerDate <= date && (item.EndServerDate is null || item.EndServerDate >= date)).ToArray();
            foreach (var progress in _reviewWorkspaceCalculator.BuildGoalProgress(active, data.GoalObservations, from, date))
                summary.AppendLine(UiText.Translate($"目标「{progress.Goal.Name}」· 机会 {progress.OpportunityCount} · 通过 {progress.PassCount} · 失败 {progress.FailCount} · 未知 {progress.UnknownObservationCount}"));
            var rules = BehaviorGoalMeasurement.SupportedRules(policy)
                .OrderByDescending(rule => behaviors.Count(item => item.Rule == rule)).ToArray();
            if (rules.Length == 0) summary.AppendLine(UiText.Translate("请先启用行为规则，才能选择自动观察的改进目标。"));
            summary.AppendLine(UiText.Translate("点选一项，作为未来 7 个服务器日的改进目标；无适用机会或证据不足不会记为通过。"));
            await OnUiAsync(() =>
            {
                if (!_accountSessions.IsCurrent(session.AccountKey, session.Generation) ||
                    _behaviorActionCard is not null || _quickReviewCard is not null || _entryReasonCard is not null) return;
                var card = new BehaviorActionCard("交易小结与改进", summary.ToString(), "设为7日目标",
                    [new("rule", "只选一个改进重点", rules.Select(rule => new PetActionChoice(rule.ToString(), PetGoalName(rule))).ToArray())],
                    compactSummary: rules.Length > 0);
                card.SaveActionAsync = async response =>
                {
                    if (!Enum.TryParse<BehaviorRuleKind>(response.Selection("rule"), out var rule) || !rules.Contains(rule)) return "请选择一个改进重点。";
                    await using var lease = await EnterRuntimeOperationAsync(MaintenanceOperationKind.Write, _cancellation.Token);
                    if (!_persistenceAvailable || !_accountSessions.IsCurrent(session.AccountKey, session.Generation))
                        return "账户已切换或存储不可写，内容仍保留。";
                    if (_serverDate != date || BehaviorGoalMeasurement.PolicyVersion(_behaviorPolicies?.Selected ?? BehaviorPolicy.Balanced) != version)
                        return "交易日或规则已更新，请关闭后重新打开小结。";
                    if (_activeImprovementGoals.Concat(data.Goals).Any(item => item.Rule == rule && item.AccountKey == session.AccountKey &&
                        item.RuleVersion == version && item.Status == ImprovementGoalStatus.Active && item.StartServerDate <= date &&
                        (item.EndServerDate is null || item.EndServerDate >= date))) return "该规则已有活动目标，可直接继续观察。";
                    var now = _timeProvider.GetUtcNow();
                    var goal = new ImprovementGoal($"goal-{Guid.NewGuid():N}", session.AccountKey, PetGoalName(rule), rule, version,
                        date, date.AddDays(6), 100m, null, "仅统计规则版本一致的适用开仓；通过率分母为已知通过与失败，未知及不适用单列。",
                        string.Empty, true, ImprovementGoalStatus.Active, 0, now, now, ObservationWindowDays: 7,
                        EndCondition: "7个服务器日结束后复核，未观察到机会不判定达成。");
                    var result = await _improvementService.SaveAsync(goal, 0, _cancellation.Token);
                    if (!result.IsSaved) return result.Message;
                    _activeImprovementGoals.Add(result.Value!);
                    return null;
                };
                PresentBehaviorAction(card);
            });
        }
        finally { Interlocked.Exchange(ref _openingBehaviorAction, 0); }
    }

    private static string PetGoalName(BehaviorRuleKind rule) => UiText.Translate(rule switch
    {
        BehaviorRuleKind.CooldownViolation => "连亏后遵守冷静期",
        BehaviorRuleKind.SizeEscalationAfterLoss => "亏损后不放大手数",
        BehaviorRuleKind.OvertradeBurst => "减少密集开仓",
        BehaviorRuleKind.RevengeScore => "亏损后不急于再开",
        BehaviorRuleKind.LossZonePersistence => "减少亏损区域重复进场",
        BehaviorRuleKind.ReentryCount => "控制同价位重复进场",
        BehaviorRuleKind.PriceFixationScore => "减少同价位集中交易",
        _ => FormatBehaviorRule(rule),
    });
}
