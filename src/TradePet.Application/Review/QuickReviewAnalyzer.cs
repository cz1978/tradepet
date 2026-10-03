using TradePet.Core.Domain;

namespace TradePet.Application.Review;

public sealed record QuickReviewAnalysis(string ExitReason, string Explanation, string Improvement);

public static class QuickReviewAnalyzer
{
    public static string DescribeProtection(TradeRecord trade, IReadOnlyList<PositionPnlSample> observations)
    {
        var samples = observations.Where(sample => sample.TradeKey == new TradeKey(trade.AccountKey, trade.PositionId) &&
                sample.CapturedAtUtc >= trade.OpenedAtUtc && sample.Volume > 0m && sample.AlgorithmVersion == "position-pnl-v1" &&
                (trade.ClosedAtUtc is null || sample.CapturedAtUtc <= trade.ClosedAtUtc))
            .OrderBy(sample => sample.CapturedAtUtc).ToArray();
        if (samples.Length == 0) return "SL/TP：缺少持仓采样，无法还原实际设置。";
        static string Price(decimal? price) => price is > 0m ? price.Value.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture) : "未设置";
        var first = samples[0];
        var last = samples[^1];
        var initial = first.CapturedAtUtc - trade.OpenedAtUtc <= TimeSpan.FromSeconds(5);
        var changes = samples.Zip(samples.Skip(1)).Count(pair =>
            pair.First.StopLoss != pair.Second.StopLoss || pair.First.TakeProfit != pair.Second.TakeProfit);
        return $"{(initial ? "开仓时" : "最早采样，非开仓时")} SL {Price(first.StopLoss)} / TP {Price(first.TakeProfit)}；" +
            $"最后采样 SL {Price(last.StopLoss)} / TP {Price(last.TakeProfit)}；记录中 SL/TP 变更 {changes} 次。";
    }

    public static QuickReviewAnalysis Analyze(TradeDetailData detail)
    {
        var trade = detail.Trade;
        var key = new TradeKey(trade.AccountKey, trade.PositionId);
        var exits = detail.Deals
            .Where(deal => deal.PositionId == trade.PositionId &&
                deal.EntryKind is DealEntryKind.Out or DealEntryKind.InOut or DealEntryKind.OutBy)
            .OrderBy(deal => deal.OccurredAtUtc).ThenBy(deal => deal.Ticket).ToArray();
        var finalExit = exits.LastOrDefault();
        var closedAt = trade.ClosedAtUtc ?? finalExit?.OccurredAtUtc;
        var exitPrice = finalExit?.Price ?? trade.ExitPrice;
        var samples = detail.PnlSamples
            .Where(sample => sample.TradeKey == key && sample.CapturedAtUtc >= trade.OpenedAtUtc &&
                closedAt.HasValue && sample.CapturedAtUtc <= closedAt.Value && sample.Volume > 0m)
            .OrderBy(sample => sample.CapturedAtUtc).ToArray();
        var facts = new List<string>();
        var improvements = new List<string>();
        var duration = closedAt.HasValue ? closedAt.Value - trade.OpenedAtUtc : TimeSpan.Zero;
        var holding = duration.TotalHours >= 1
            ? $"{duration.TotalHours:0.#} 小时"
            : duration.TotalMinutes >= 1 ? $"{duration.TotalMinutes:0.#} 分钟" : $"{Math.Max(0, duration.TotalSeconds):0} 秒";
        facts.Add($"交易概况：净盈亏 {trade.NetPnl:+0.##;-0.##;0}；持仓 {holding}。" +
            (exitPrice.HasValue ? $"入场价 {trade.EntryPrice:0.#####}，最终平仓价 {exitPrice:0.#####}。" : "缺少平仓价格。"));
        if (exits.Length > 1)
            facts.Add($"共 {exits.Length} 笔退出成交；退出价判断使用最后一笔，盈亏使用整笔交易净值。");

        facts.Add(DescribeProtection(trade, samples));
        var lastSample = samples.LastOrDefault();
        var recentSample = lastSample is not null && closedAt.HasValue &&
            closedAt.Value - lastSample.CapturedAtUtc <= TimeSpan.FromSeconds(5);
        decimal? stop = recentSample ? lastSample!.StopLoss : null;
        decimal? target = recentSample ? lastSample!.TakeProfit : null;
        var source = "平仓前持仓记录";

        var reason = trade.NetPnl switch
        {
            > 0.01m => "盈利离场（原因待补充）",
            < -0.01m => "亏损离场（原因待补充）",
            _ => "保本离场（原因待补充）",
        };
        var hitStop = stop is > 0m && exitPrice.HasValue &&
            (trade.Side == TradeSide.Buy ? exitPrice <= stop : exitPrice >= stop);
        var hitTarget = target is > 0m && exitPrice.HasValue &&
            (trade.Side == TradeSide.Buy ? exitPrice >= target : exitPrice <= target);
        var priceExplainsExit = hitStop != hitTarget;
        if (priceExplainsExit)
        {
            var label = hitStop ? "止损" : "止盈";
            reason = $"触及{label}价离场（推测）";
            facts.Add($"退出依据：最终平仓价已达到{source}的{label}价 {(hitStop ? stop : target):0.#####}。价格吻合，尚不能确认实际成交触发原因。");
            improvements.Add($"核对终端成交记录，确认是否由{label}触发，并补充执行偏差。");
        }
        else if (exitPrice.HasValue && (stop is > 0m || target is > 0m))
        {
            facts.Add(hitStop && hitTarget
                ? "退出依据：止损与止盈记录存在冲突，无法据此判断退出原因。"
                : $"退出依据：最终平仓价未触及{source}的止损/止盈价，需补充主动退出的依据。");
            improvements.Add("记录本次实际退出信号，结合当时走势复核退出时机。");
        }
        else
        {
            facts.Add("退出依据：缺少可用的临近平仓止损/止盈记录，无法从盈亏确定实际退出原因。");
            improvements.Add("补充本次实际退出原因，便于结合行情回看执行。");
        }

        var excursion = detail.Excursion;
        var reliable = excursion is not null && excursion.AccountKey == key.AccountKey &&
            excursion.PositionId == key.PositionId && excursion.IsReliable;
        var hasInitialRisk = excursion is not null && excursion.AccountKey == key.AccountKey &&
            excursion.PositionId == key.PositionId && excursion.HasReliableInitialRisk;
        facts.Add(hasInitialRisk
            ? $"初始货币风险 {excursion!.InitialRiskAmount:0.##}；实际 R {trade.NetPnl / excursion.InitialRiskAmount:0.####}（整笔净盈亏 / 初始风险）。"
            : "初始风险与 R：缺少开仓时有效止损及货币风险记录，不能事后补算。");
        decimal? peak = reliable ? excursion!.MaximumPnl : samples.Length > 0 ? samples.Max(sample => sample.NetPnl) : null;
        decimal? trough = reliable ? excursion!.MinimumPnl : samples.Length > 0 ? samples.Min(sample => sample.NetPnl) : null;
        if (peak.HasValue && trough.HasValue)
        {
            facts.Add($"{(reliable ? "持仓过程" : "已采样区间（覆盖不完整）")}：最高净盈亏 {peak:+0.##;-0.##;0}，最低 {trough:+0.##;-0.##;0}。");
            if (peak > 0.01m && peak - trade.NetPnl > 0.01m)
            {
                facts.Add($"从记录盈利高点到平仓回吐 {peak - trade.NetPnl:0.##}（{(peak - trade.NetPnl) / peak:P0}）。" +
                    (trade.NetPnl < -0.01m ? "记录中曾盈利，最终转为亏损。" : string.Empty));
                improvements.Insert(0, "回看盈利高点到退出之间的变化，记录继续持有或退出的依据。");
                if (!priceExplainsExit)
                    reason = trade.NetPnl < -0.01m ? "浮盈回吐后亏损离场（待确认）" : "盈利回吐后离场（待确认）";
            }
            else if (trough < -0.01m && trade.NetPnl - trough > 0.01m)
            {
                facts.Add($"相比记录低点，平仓净盈亏回升 {trade.NetPnl - trough:0.##}。");
                improvements.Insert(0, "回看亏损阶段的持仓依据，核对是否遵守原定风险边界。");
            }
        }
        else
            facts.Add("持仓过程：缺少有效盈亏采样，无法判断浮盈回吐和最大浮亏。");

        foreach (var behavior in detail.Behaviors
            .Where(item => item.AccountKey == key.AccountKey && !item.EvidenceInsufficient &&
                string.IsNullOrWhiteSpace(item.MissingData) && item.TradeLinks.Any(link => link.TradeKey == key) &&
                item.Level is BehaviorRiskLevel.Attention or BehaviorRiskLevel.Critical)
            .OrderByDescending(item => item.Level).ThenByDescending(item => item.EventAtUtc).Take(3))
        {
            facts.Add($"行为提醒：{behavior.Summary}");
            improvements.Add($"复核提醒“{behavior.Summary}”，补充当时的执行依据。");
        }
        return new QuickReviewAnalysis(reason, string.Join(Environment.NewLine, facts),
            string.Join(Environment.NewLine, improvements.Distinct()));
    }
}
