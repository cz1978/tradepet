using System.Globalization;
using System.Text;
using TradePet.Core.Domain;
using TradePet.Core.Review;

namespace TradePet.Application.Review;

public sealed record DailyReportSection(string Title, IReadOnlyList<string> Lines);

public sealed record DailyReportTradeRow(
    long PositionId, string Symbol, string Direction, string OpenedAt, string ClosedAt,
    string Holding, string OpeningVolume, string MaximumVolume, string NetPnl, string Fees,
    string InitialRisk, string ActualR, string Mae, string Mfe, string Giveback, string Coverage,
    string Protection, string ExitReason, string NextAction);

public sealed record DailyReportAnalysis(
    IReadOnlyList<DailyReportSection> Sections,
    IReadOnlyList<DailyReportTradeRow> Trades,
    string Markdown);

public static class DailyReportAnalyzer
{
    // Old date columns can predate a server clock correction. Use the same
    // timestamps and offset for day membership as for the report's displayed times.
    public static ReviewWorkspaceData NormalizeServerDates(ReviewWorkspaceData data, int serverUtcOffsetSeconds)
    {
        var offset = TimeSpan.FromSeconds(serverUtcOffsetSeconds);
        DateOnly ServerDate(DateTimeOffset at) => DateOnly.FromDateTime(at.ToOffset(offset).DateTime);
        return data with
        {
            Trades = data.Trades.Select(trade => trade with
            {
                OpenServerDate = ServerDate(trade.OpenedAtUtc),
                CloseServerDate = trade.ClosedAtUtc is { } closed ? ServerDate(closed) : null,
            }).ToArray(),
            Behaviors = data.Behaviors.Select(behavior => behavior with
            {
                ServerDate = ServerDate(behavior.EventAtUtc),
            }).ToArray(),
            EquitySamples = data.EquitySamples?.Select(sample => sample with
            {
                ServerDate = ServerDate(sample.CapturedAtUtc),
            }).ToArray(),
        };
    }

    public static DailyReportAnalysis Analyze(ReviewWorkspaceData data, DailyReviewFacts facts, bool isLive)
    {
        var date = facts.ServerDate;
        var offset = TimeSpan.FromSeconds(facts.ServerUtcOffsetSeconds);
        var completed = data.Trades.Where(t => t.AccountKey == data.AccountKey && t.IsComplete && t.CloseServerDate == date)
            .OrderBy(t => t.ClosedAtUtc).ThenBy(t => t.PositionId).ToArray();
        var selectedIds = completed.Select(t => t.PositionId).ToHashSet();
        var excursions = data.Excursions.Where(p => p.Value.AccountKey == data.AccountKey).ToDictionary(p => p.Key, p => p.Value);
        var calculator = new ReviewWorkspaceCalculator();
        var risk = calculator.BuildRiskSamples(completed, excursions).ToDictionary(p => p.Trade.PositionId);
        var wins = completed.Where(t => t.NetPnl > 0.01m).ToArray();
        var losses = completed.Where(t => t.NetPnl < -0.01m).ToArray();
        var winningPnl = wins.Sum(t => t.NetPnl);
        var losingPnl = losses.Sum(t => t.NetPnl);
        var currency = data.Currency;
        string Money(decimal value) => $"{Number(value)} {currency}".TrimEnd();
        string MaybeMoney(decimal? value) => value.HasValue ? Money(value.Value) : "—（数据不足）";
        string Time(DateTimeOffset? value) => value?.ToOffset(offset).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "—";
        var sections = new List<DailyReportSection>();
        var actions = new List<string>();
        var mt4 = data.AccountKey.StartsWith("MT4:", StringComparison.Ordinal);

        var results = new List<string>
        {
            isLive ? "当日实时快照：统计截至本次生成，尚非最终收盘结果。" : "按报告日服务器日期归集；历史完整性以数据覆盖说明为准。",
            $"当日成交现金净盈亏 {Money(facts.RealizedCashPnl)}；当日完整平仓交易的全生命周期净盈亏 {Money(facts.CompleteTradeNetPnl)}。跨日和部分平仓可导致两者不同。",
            $"完整平仓 {completed.Length} 笔，盈利 {wins.Length}、亏损 {losses.Length}、持平 {completed.Length - wins.Length - losses.Length}；按净盈亏 ±0.01 判定胜负。",
        };
        if (mt4) results.Insert(1, "MT4 按已存档订单计算：有 broker 票号关联证据的部分平仓归入同一持仓，全部平完才计为完整交易；缺少关联证据的订单保持独立。费用按各平仓记录归入平仓日，原始票号保留在成交明细。显示及日期归属以 broker 服务器为准；历史 UTC 按当前偏移换算，跨夏令时的实际持仓时长存在限制。初始风险优先用合约与盈亏币种换算，仅有可靠开仓采样时计算 R。");
        if (completed.Length > 0)
        {
            results.Add($"盈利交易净额合计 {Money(winningPnl)}；亏损交易净额合计 {Money(losingPnl)}；平均每笔 {Money(completed.Average(t => t.NetPnl))}。");
            results.Add($"盈利因子（盈利净额 / 亏损净额绝对值）：{(losses.Length == 0 ? "—（无亏损样本）" : Number(winningPnl / Math.Abs(losingPnl)))}；平均盈利 {MaybeMoney(wins.Length == 0 ? null : wins.Average(t => t.NetPnl))}；平均亏损 {MaybeMoney(losses.Length == 0 ? null : losses.Average(t => t.NetPnl))}。");
            results.Add($"平均盈亏比：{(wins.Length == 0 || losses.Length == 0 ? "—（需同时有盈利和亏损样本）" : Number(wins.Average(t => t.NetPnl) / Math.Abs(losses.Average(t => t.NetPnl))))}；当日最大连赢 {MaxStreak(completed, true)} 笔，最大连亏 {MaxStreak(completed, false)} 笔。");
            var best = completed.MaxBy(t => t.NetPnl)!;
            var worst = completed.MinBy(t => t.NetPnl)!;
            results.Add($"最高净盈亏：{best.Symbol} #{best.PositionId} {Money(best.NetPnl)}；最低净盈亏：{worst.Symbol} #{worst.PositionId} {Money(worst.NetPnl)}。");
        }
        else results.Add("没有完整平仓样本，胜率、盈利因子、平均每笔结果不计算；当日成交现金盈亏仍可能非零。");
        var dayDeals = data.Deals.Where(d => DateOnly.FromDateTime(d.OccurredAtUtc.ToOffset(offset).DateTime) == date).ToArray();
        results.Add(dayDeals.Length == 0 ? "当日费用拆分：无成交记录，不能据此认定没有费用。" :
            $"当日已记录佣金 {Money(dayDeals.Sum(d => d.Commission))}、隔夜费 {Money(dayDeals.Sum(d => d.Swap))}、其他费 {Money(dayDeals.Sum(d => d.Fee))}（保留终端原始正负号）。");
        sections.Add(new("结果与统计口径", results));
        var market = DailyMarketContextAnalyzer.Analyze(data, date, facts.ServerUtcOffsetSeconds);
        sections.Add(market.Section);

        var sources = new List<string>();
        AddGroups("品种", completed.GroupBy(t => t.Symbol));
        AddGroups("方向", completed.GroupBy(t => t.Side == TradeSide.Buy ? "买入" : "卖出"));
        AddGroups("入场时段（服务器，按开仓小时）", completed.GroupBy(t => t.OpenedAtUtc.ToOffset(offset).ToString("HH:00", CultureInfo.InvariantCulture)));
        if (sources.Count == 0) sources.Add("无完整平仓样本，暂不做盈亏来源拆分。");
        sources.Add("以上是样本分组结果，不代表品种、时段或策略导致了盈亏；单日小样本不能证明长期优势。");
        sections.Add(new("盈亏来源拆分", sources));
        void AddGroups(string label, IEnumerable<IGrouping<string, TradeRecord>> groups)
        {
            foreach (var group in groups.OrderByDescending(g => g.Sum(t => t.NetPnl)))
                sources.Add($"{label} · {group.Key}：{group.Count()} 笔，净盈亏 {Money(group.Sum(t => t.NetPnl))}，胜率 {Number(group.Count(t => t.NetPnl > 0.01m) * 100m / group.Count())}%。");
        }

        var equitySamples = (data.EquitySamples ?? []).Where(e => e.AccountKey == data.AccountKey && e.ServerDate == date)
            .OrderBy(e => e.CapturedAtUtc).ToArray();
        var equity = calculator.BuildEquityAnalysis(data.AccountKey, date, date, equitySamples,
            (data.CashFlows ?? []).ToArray(), facts.ServerUtcOffsetSeconds);
        var riskLines = new List<string>();
        var curve = calculator.BuildRealizedCurve(completed);
        riskLines.Add(curve.Count == 0 ? "已平仓交易累计曲线回撤：—（无可排序的平仓记录）。" :
            $"已平仓交易累计净盈亏最大回撤 {Money(curve.Max(p => p.Drawdown))}，以零为起点；不包含持仓浮亏，不等于账户净值回撤。");
        if (equitySamples.Length < 2) riskLines.Add("账户净值采样不足 2 个：无法评估净值回撤，不能当作零回撤。");
        else
        {
            riskLines.Add($"净值采样 {equitySamples.Length} 个，{Time(equitySamples[0].CapturedAtUtc)} 至 {Time(equitySamples[^1].CapturedAtUtc)}；首笔 {Money(equitySamples[0].Equity)}，末笔 {Money(equitySamples[^1].Equity)}。");
            riskLines.Add($"采样内原始净值最大回撤 {MaybeMoney(equity.ObservedMaximumDrawdownAmount)}；可能受出入金影响。经资金流校验的净值回撤比例：{(equity.CashFlowAdjustedMaximumDrawdownPercentage is decimal drawdown ? Number(drawdown) + "%" : "—（资金流覆盖不足）")}。");
            riskLines.Add(equity.Message + " 采样间未记录的波动和时段不作补算，结果不代表全天精确最大回撤。");
        }
        var coveredRisk = risk.Values.Where(r => r.ActualRiskMultiple.HasValue).ToArray();
        riskLines.Add($"可靠初始风险覆盖 {coveredRisk.Length}/{completed.Length} 笔；平均实际 R：{(coveredRisk.Length == 0 ? "—" : Number(coveredRisk.Average(r => r.ActualRiskMultiple!.Value)))}；1R 为开仓时可靠记录的初始货币风险。");
        var validDurations = completed.Where(t => t.ClosedAtUtc >= t.OpenedAtUtc)
            .Select(t => (t.ClosedAtUtc!.Value - t.OpenedAtUtc).TotalSeconds).ToArray();
        riskLines.Add(validDurations.Length == 0 ? "平均 / 最长持仓：—（缺少有效起止时间）。" :
            $"平均持仓 {Duration(TimeSpan.FromSeconds(validDurations.Average()))}；最长 {Duration(TimeSpan.FromSeconds(validDurations.Max()))}；有效时长 {validDurations.Length}/{completed.Length} 笔。");
        if (data.DailyStates?.TryGetValue(date, out var state) == true)
            riskLines.Add($"当日状态记录：浮动盈亏 {Money(state.FloatingPnl)}，总盈亏高水位 {Money(state.HighWaterPnl)}，高水位回吐 {Money(state.Giveback)}；最大敞口 {Number(state.MaximumExposure)} 手（跨品种手数不能视为等值风险）。");
        if (facts.HasReliableTargetMilestone)
            riskLines.Add($"达到日目标：{Time(facts.TargetReachedAtUtc)}；达标后新开并已完成交易 {facts.AfterTargetNewCompleteTradeCount ?? 0} 笔、净盈亏 {MaybeMoney(facts.AfterTargetNewTradeNetPnl)}；仍未完成 {facts.AfterTargetNewOpenTradeCount ?? 0} 笔。");
        sections.Add(new("风险、回撤与持仓", riskLines));

        var discipline = new List<string>();
        var behaviors = data.Behaviors.Where(b => b.AccountKey == data.AccountKey && b.ServerDate == date && b.Rule != BehaviorRuleKind.PlanDeviationRate &&
            b.Level is BehaviorRiskLevel.Attention or BehaviorRiskLevel.Critical).ToArray();
        if (behaviors.Length == 0) discipline.Add("没有记录到需注意或严重级别的行为提醒；这不代表全天没有风险或规则全部通过。");
        foreach (var group in behaviors.GroupBy(b => b.Rule))
        {
            var linkedIds = group.Where(b => !b.EvidenceInsufficient && string.IsNullOrWhiteSpace(b.MissingData))
                .SelectMany(b => b.TradeLinks).Where(l => l.Role == BehaviorTradeRole.Trigger && l.TradeKey.AccountKey == data.AccountKey)
                .Select(l => l.TradeKey.PositionId).Where(selectedIds.Contains).Distinct().ToHashSet();
            discipline.Add($"{RuleName(group.Key)}：{group.Count()} 条提醒，其中证据不足 {group.Count(b => b.EvidenceInsufficient || !string.IsNullOrWhiteSpace(b.MissingData))} 条；关联当日完整交易 {linkedIds.Count} 笔，关联净盈亏 {Money(completed.Where(t => linkedIds.Contains(t.PositionId)).Sum(t => t.NetPnl))}。关联不等于归因，不同规则之间可能重复关联。");
        }
        var assessments = data.Assessments.Where(a => a.TradeKey.AccountKey == data.AccountKey && selectedIds.Contains(a.TradeKey.PositionId)).ToArray();
        discipline.Add($"规则评估：通过 {assessments.Count(a => a.Status == RuleAssessmentStatus.Passed)}，失败 {assessments.Count(a => a.Status == RuleAssessmentStatus.Failed)}，未知 {assessments.Count(a => a.Status == RuleAssessmentStatus.Unknown)}，不适用 {assessments.Count(a => a.Status == RuleAssessmentStatus.NotApplicable)}；无评估记录不视为通过。");
        foreach (var a in assessments.Where(a => a.Status == RuleAssessmentStatus.Failed))
            discipline.Add($"#{a.TradeKey.PositionId} · 规则 {a.RuleId} 未通过；证据：{Present(a.EvidenceReference)}；备注：{Present(a.Notes)}。");
        sections.Add(new("执行纪律与关联结果", discipline));

        var selfReports = new List<string>();
        foreach (var trade in data.Trades.Where(t => t.AccountKey == data.AccountKey &&
                     (t.OpenServerDate == date || t.IsComplete && t.CloseServerDate == date))
                     .OrderBy(t => t.OpenedAtUtc).ThenBy(t => t.PositionId))
        {
            var entry = data.EntryReasonNotes?.GetValueOrDefault(trade.PositionId);
            if (entry?.TradeKey.AccountKey != data.AccountKey) entry = null;
            var document = data.Documents.GetValueOrDefault(trade.PositionId);
            if (document?.TradeKey.AccountKey != data.AccountKey) document = null;
            var fields = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry?.Reason)) fields.Add($"入场原因：{entry.Reason}");
            if (entry?.ReportedExecution is { } entryExecution)
                fields.Add($"开仓执行自报：{QuickReviewAnalyzer.DescribeReportedExecution(entryExecution)}");
            if (!string.IsNullOrWhiteSpace(entry?.Emotion)) fields.Add($"开仓状态自报：{entry.Emotion}");
            if (document?.ReportedExecution is { } reviewExecution)
                fields.Add($"复盘执行自报：{QuickReviewAnalyzer.DescribeReportedExecution(reviewExecution)}");
            if (!string.IsNullOrWhiteSpace(document?.Emotion)) fields.Add($"交易状态自报：{document.Emotion}");
            if (document?.ReportedExitExecution is { } exitExecution)
                fields.Add($"退出执行自报：{QuickReviewAnalyzer.DescribeReportedExitExecution(exitExecution)}");
            if (!string.IsNullOrWhiteSpace(document?.ExitEmotion)) fields.Add($"平仓状态自报：{document.ExitEmotion}");
            if (fields.Count > 0) selfReports.Add($"#{trade.PositionId} · {string.Join("；", fields.Select(TradePet.Core.Localization.UiText.Translate))}。");
        }
        if (selfReports.Count > 0)
        {
            selfReports.Insert(0, "来自宠物弹框及已有复盘记录的用户自报；未填写保持未记录，自报不替代自动规则证据。");
            sections.Add(new("宠物记录与执行自报", selfReports));
        }

        var opportunities = data.Opportunities.Where(item => item.AccountKey == data.AccountKey && item.ServerDate == date).ToArray();
        if (opportunities.Length > 0)
        {
            var lines = new List<string>
            {
                $"当时记录 {opportunities.Count(item => item.Kind == OpportunityRecordKind.ObservedBeforeMove)} 条，主动跳过 {opportunities.Count(item => item.Kind == OpportunityRecordKind.DeliberatelySkipped)} 条，事后发现 {opportunities.Count(item => item.Kind == OpportunityRecordKind.DiscoveredAfterMove)} 条；三类分别统计，不推算未成交盈亏。",
            };
            lines.AddRange(opportunities.OrderBy(item => item.RecordedAtUtc).Select(item =>
                $"{Time(item.RecordedAtUtc)} · {item.Symbol} · {OpportunityKind(item.Kind)} · 原因：{TradePet.Core.Localization.UiText.Translate(item.Reason)}"));
            sections.Add(new("未交易机会", lines));
        }
        var goals = data.Goals.Where(item => item.AccountKey == data.AccountKey && item.StartServerDate <= date &&
            (item.EndServerDate is null || item.EndServerDate >= date)).ToArray();
        if (goals.Length > 0)
        {
            var progress = new ReviewWorkspaceCalculator().BuildGoalProgress(goals, data.GoalObservations, date, date);
            sections.Add(new("改进目标观察", progress.Select(item =>
                $"{item.Goal.Name} · 机会 {item.OpportunityCount} · 通过 {item.PassCount} · 失败 {item.FailCount} · 未知 {item.UnknownObservationCount} · 不适用 {item.NotApplicableObservationCount}；无适用机会不判定达成。").ToArray()));
        }
        var protectionChanges = facts.Timeline.Where(item => item.Kind == "持仓变化（采样发现）").ToArray();
        if (protectionChanges.Length > 0)
            sections.Add(new("持仓变化记录", protectionChanges.Select(item => $"{Time(item.AtUtc)} · {item.Summary}").ToArray()));

        var rows = new List<DailyReportTradeRow>();
        foreach (var trade in completed)
        {
            var sample = risk[trade.PositionId];
            excursions.TryGetValue(trade.PositionId, out var excursion);
            data.Documents.TryGetValue(trade.PositionId, out var document);
            var recordedDeals = data.Deals.Where(d => d.PositionId == trade.PositionId && d.OccurredAtUtc >= trade.OpenedAtUtc &&
                trade.ClosedAtUtc.HasValue && d.OccurredAtUtc <= trade.ClosedAtUtc.Value).ToArray();
            var giveback = sample.Mfe is > 0m ? Math.Max(0m, sample.Mfe.Value - trade.NetPnl) : (decimal?)null;
            var rowAction = !string.IsNullOrWhiteSpace(document?.NextAction) ? document!.NextAction :
                !sample.HasReliableInitialRisk ? "补查开仓时的止损与风险记录；缺失不能直接判定未设止损。" :
                giveback is > 0m ? "结合当时走势复核浮盈回吐及平仓原因。" : "补充实际退出原因和下一次改进动作。";
            rows.Add(new(trade.PositionId, trade.Symbol, trade.Side == TradeSide.Buy ? "买入" : "卖出",
                Time(trade.OpenedAtUtc), Time(trade.ClosedAtUtc),
                trade.ClosedAtUtc >= trade.OpenedAtUtc ? Duration(trade.ClosedAtUtc.Value - trade.OpenedAtUtc) : "—",
                Number(trade.OpeningVolume), Number(trade.MaximumVolume), Money(trade.NetPnl),
                recordedDeals.Length == 0 ? "—（无成交记录）" : Money(recordedDeals.Sum(d => d.Commission + d.Swap + d.Fee)),
                MaybeMoney(sample.InitialRiskAmount), sample.ActualRiskMultiple.HasValue ? Number(sample.ActualRiskMultiple.Value) + " R" : "—",
                MaybeMoney(sample.Mae), MaybeMoney(sample.Mfe), MaybeMoney(giveback),
                excursion is null ? "无采样" : $"{Number(excursion.CoveragePercentage)}% · {(sample.HasReliableExcursion ? "可靠" : "不足，不计算极值")}",
                QuickReviewAnalyzer.DescribeProtection(trade, data.PositionSamples?.GetValueOrDefault(trade.PositionId) ?? []),
                Present(document?.ExitReason, "未记录，不能仅凭盈亏判定平仓原因"), rowAction));
        }

        if (coveredRisk.Length < completed.Length) actions.Add($"{completed.Length - coveredRisk.Length} 笔缺少可靠初始风险：下一次开仓前记录止损、手数与货币风险，已有缺失不事后补造 R 值。");
        foreach (var group in behaviors.Where(b => !b.EvidenceInsufficient && string.IsNullOrWhiteSpace(b.MissingData)).GroupBy(b => b.Rule))
            actions.Add($"针对「{RuleName(group.Key)}」的 {group.Count()} 条提醒，复核 {Time(group.First().EventAtUtc)} 起的关联证据，在下一交易日前写明触发条件和应对动作。");
        var givebackTrades = completed.Where(t => risk[t.PositionId].Mfe is > 0m && risk[t.PositionId].Mfe > t.NetPnl).ToArray();
        if (givebackTrades.Length > 0) actions.Add($"可靠采样中 {givebackTrades.Length} 笔出现浮盈回吐（{Ids(givebackTrades)}）：结合当时走势与已记录原因复核，不直接推断为过早或过晚平仓。");
        foreach (var trade in completed)
            if (data.Documents.TryGetValue(trade.PositionId, out var doc) && !string.IsNullOrWhiteSpace(doc.NextAction))
                actions.Add($"人工记录 · #{trade.PositionId}：{doc.NextAction.Trim()}");
        if (data.DailyJournals.TryGetValue(date, out var journal) && !string.IsNullOrWhiteSpace(journal.NextAction))
            actions.Add($"当日日记中的下一步行动：{journal.NextAction.Trim()}");
        if (actions.Count == 0) actions.Add("现有记录不足以提出具体纠偏动作；下一交易日前检查风险记录与数据采集状态。");
        sections.Add(new("下一交易日行动清单", actions));

        var carry = data.Trades.Where(t => t.AccountKey == data.AccountKey && t.OpenServerDate <= date &&
            (!t.IsComplete || t.CloseServerDate > date)).OrderBy(t => t.OpenedAtUtc).ToArray();
        var quality = new List<string>
        {
            mt4 ? "MT4 历史覆盖未确认：请在终端账户历史中选择全部历史。仅同步终端已加载订单，不能把未导入记录当作零交易。"
                : data.DataGapDates.Contains(date) ? "该交易日标记有历史数据缺口：计数、盈亏和统计可能不完整。" : "未标记历史导入缺口；仍需分别检查净值及逐笔采样覆盖。",
            $"可靠浮盈浮亏覆盖 {risk.Values.Count(r => r.HasReliableExcursion)}/{completed.Length} 笔；缺失或不可靠的 MAE/MFE、回吐显示为 —，不补零。MAE 为最低持仓盈亏（可为负），MFE 为最高持仓盈亏。",
            "逐笔费用为该完整交易已载入成交的费用合计，可能包含前一交易日费用；净盈亏已含费用，不应重复扣减。",
            $"报告日跨日/未结交易记录 {carry.Length} 笔，不计入完整平仓胜率；历史日末手数与浮盈未重建，不拿当前数值代替。",
        };
        quality.AddRange(carry.Select(t => $"未结记录 #{t.PositionId} · {t.Symbol} · {(t.Side == TradeSide.Buy ? "买入" : "卖出")} · 开仓 {Time(t.OpenedAtUtc)}。"));
        quality.Add("退出原因优先保留人工记录；纪律提醒及盈亏关联不能证明心理状态或因果关系。");
        sections.Add(new("数据覆盖与未结交易", quality));

        var markdown = new StringBuilder();
        foreach (var section in sections)
        {
            markdown.AppendLine($"## {TradePet.Core.Localization.UiText.Translate(section.Title)}").AppendLine();
            foreach (var line in section.Lines) markdown.AppendLine($"- {Cell(TradePet.Core.Localization.UiText.Translate(line))}");
            markdown.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(market.Markdown))
            markdown.AppendLine(TradePet.Core.Localization.UiText.Translate("## M5 行情与交易位置")).AppendLine().Append(market.Markdown);
        markdown.AppendLine(TradePet.Core.Localization.UiText.Translate("## 逐笔风险与退出复核")).AppendLine();
        foreach (var row in rows)
        {
            markdown.AppendLine($"### {Cell(row.Symbol)} · #{row.PositionId}").AppendLine();
            markdown.AppendLine(TradePet.Core.Localization.UiText.Translate($"- {row.Direction} · 开仓 {row.OpenedAt} · 平仓 {row.ClosedAt} · 持仓 {row.Holding}"));
            markdown.AppendLine(TradePet.Core.Localization.UiText.Translate($"- 开仓 / 最大手数：{row.OpeningVolume} / {row.MaximumVolume}；净盈亏 {row.NetPnl}；已记录费用 {row.Fees}"));
            markdown.AppendLine(TradePet.Core.Localization.UiText.Translate($"- 初始风险 {row.InitialRisk}；实际 R {row.ActualR}；MAE {row.Mae}；MFE {row.Mfe}；浮盈回吐 {row.Giveback}"));
            markdown.AppendLine(TradePet.Core.Localization.UiText.Translate($"- 极值采样覆盖：{row.Coverage}；{row.Protection}"));
            markdown.AppendLine(TradePet.Core.Localization.UiText.Translate($"- 已记录退出原因：{Cell(row.ExitReason)}"));
            markdown.AppendLine(TradePet.Core.Localization.UiText.Translate($"- 下一步：{Cell(row.NextAction)}")).AppendLine();
            var trade = completed.First(item => item.PositionId == row.PositionId);
            var observations = (data.PositionSamples?.GetValueOrDefault(row.PositionId) ?? [])
                .Where(s => s.TradeKey == new TradeKey(data.AccountKey, row.PositionId) && s.Volume > 0m &&
                    s.AlgorithmVersion == "position-pnl-v1" && s.CapturedAtUtc >= trade.OpenedAtUtc &&
                    s.CapturedAtUtc <= trade.ClosedAtUtc)
                .OrderBy(s => s.CapturedAtUtc).ToArray();
            if (observations.Length > 0)
            {
                var evidence = new List<PositionPnlSample>
                {
                    observations[0], observations[^1], observations.MinBy(s => s.NetPnl)!, observations.MaxBy(s => s.NetPnl)!,
                };
                evidence.AddRange(observations.Zip(observations.Skip(1))
                    .Where(pair => pair.First.StopLoss != pair.Second.StopLoss || pair.First.TakeProfit != pair.Second.TakeProfit)
                    .Select(pair => pair.Second));
                markdown.AppendLine(TradePet.Core.Localization.UiText.Translate("实际持仓记录（首尾、采样内最高/最低净盈亏、所有已记录 SL/TP 变化；局部采样不能代表全程极值）：")).AppendLine();
                markdown.AppendLine(TradePet.Core.Localization.UiText.Translate("| 采样时间（服务器） | 净盈亏 | 持仓手数 | SL | TP |"));
                markdown.AppendLine("|---|---:|---:|---:|---:|");
                foreach (var observation in evidence.Distinct().OrderBy(s => s.CapturedAtUtc))
                    markdown.AppendLine($"| {Time(observation.CapturedAtUtc)} | {Money(observation.NetPnl)} | {Number(observation.Volume)} | {MaybePrice(observation.StopLoss)} | {MaybePrice(observation.TakeProfit)} |");
                markdown.AppendLine();
            }
        }
        if (rows.Count == 0) markdown.AppendLine(TradePet.Core.Localization.UiText.Translate("无完整平仓交易。")).AppendLine();
        return new(sections, rows, markdown.ToString());
    }

    private static int MaxStreak(IEnumerable<TradeRecord> trades, bool winning)
    {
        var current = 0;
        var maximum = 0;
        foreach (var trade in trades)
        {
            current = (winning ? trade.NetPnl > 0.01m : trade.NetPnl < -0.01m) ? current + 1 : 0;
            maximum = Math.Max(maximum, current);
        }
        return maximum;
    }

    private static string Number(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static string MaybePrice(decimal? value) => value is > 0m ? value.Value.ToString("0.#####", CultureInfo.InvariantCulture) : "未设置";
    private static string Duration(TimeSpan value) => $"{(int)value.TotalHours}小时 {value.Minutes}分 {value.Seconds}秒";
    private static string Present(string? value, string fallback = "未记录") => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string Ids(IEnumerable<TradeRecord> trades) => string.Join("、", trades.Select(t => "#" + t.PositionId));
    private static string Cell(string value) => value.Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>");
    private static string OpportunityKind(OpportunityRecordKind kind) => kind switch
    {
        OpportunityRecordKind.ObservedBeforeMove => "当时记录",
        OpportunityRecordKind.DeliberatelySkipped => "主动跳过",
        _ => "事后发现",
    };

    private static string RuleName(BehaviorRuleKind rule) => rule switch
    {
        BehaviorRuleKind.ReentryCount => "重复进场",
        BehaviorRuleKind.LossZonePersistence => "亏损区域执着",
        BehaviorRuleKind.RevengeScore => "报复性交易风险",
        BehaviorRuleKind.OvertradeBurst => "短时过度交易",
        BehaviorRuleKind.PlanDeviationRate => "计划偏离",
        BehaviorRuleKind.ProfitGiveback => "利润回吐",
        BehaviorRuleKind.SizeEscalationAfterLoss => "亏后放大仓位",
        BehaviorRuleKind.CooldownViolation => "冷静期违规",
        _ => rule.ToString(),
    };
}
