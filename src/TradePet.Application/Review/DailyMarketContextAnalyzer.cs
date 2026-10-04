using System.Globalization;
using System.Text;
using TradePet.Core.Domain;

namespace TradePet.Application.Review;

public static class DailyMarketContextAnalyzer
{
    public static (DailyReportSection Section, string Markdown) Analyze(
        ReviewWorkspaceData data, DateOnly date, int serverUtcOffsetSeconds)
    {
        var offset = TimeSpan.FromSeconds(serverUtcOffsetSeconds);
        var from = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime();
        var to = from.AddDays(1);
        var trades = data.Trades.Where(t => t.AccountKey == data.AccountKey &&
            t.OpenedAtUtc < to && (t.ClosedAtUtc is null || t.ClosedAtUtc >= from)).ToArray();
        var symbols = trades.Select(t => t.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        var lines = new List<string>();
        var markdown = new StringBuilder();
        string Time(DateTimeOffset at) => at.ToOffset(offset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        foreach (var symbol in symbols)
        {
            var history = data.DailyMarketData?.FirstOrDefault(h => h.Range.AccountKey == data.AccountKey &&
                string.Equals(h.Range.Symbol, symbol, StringComparison.OrdinalIgnoreCase) && h.Range.Timeframe == "M5");
            var bars = (history?.Bars ?? []).Where(b => b.AccountKey == data.AccountKey &&
                b.TerminalId == history!.Range.TerminalId && b.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) &&
                b.Timeframe == "M5" && b.OpenedAtUtc >= from && b.OpenedAtUtc < to &&
                b.OpenedAtUtc < history.Range.RequestedToUtc && b.Open > 0 && b.Low > 0 &&
                b.Low <= Math.Min(b.Open, b.Close) && b.High >= Math.Max(b.Open, b.Close))
                .GroupBy(b => b.OpenedAtUtc).Select(g => g.Last()).OrderBy(b => b.OpenedAtUtc).ToArray();
            if (bars.Length == 0)
            {
                lines.Add($"{symbol}：缺少当日 M5 行情，无法判断当日走势和交易所处位置。" +
                    (string.IsNullOrWhiteSpace(history?.Range.Error) ? string.Empty : $" 原因：{history.Range.Error}"));
                continue;
            }
            var high = bars.Max(b => b.High);
            var low = bars.Min(b => b.Low);
            var range = high - low;
            var change = bars[^1].Close - bars[0].Open;
            var closeLocation = range > 0 ? (bars[^1].Close - low) / range * 100m : (decimal?)null;
            var gaps = bars.Zip(bars.Skip(1)).Count(p => p.Second.OpenedAtUtc - p.First.OpenedAtUtc > TimeSpan.FromMinutes(5));
            var coverage = history!.Range.Coverage == MarketCoverageStatus.Complete ? "终端返回完整请求范围" : "请求范围覆盖不完整";
            lines.Add($"{symbol} · M5 {bars.Length} 根 · {Time(bars[0].OpenedAtUtc)} 至 {Time(bars[^1].OpenedAtUtc)}；{coverage}，间隔超过5分钟 {gaps} 处（可能含休市）。");
            lines.Add($"{symbol} · 已覆盖行情 O/H/L/C {N(bars[0].Open)} / {N(high)} / {N(low)} / {N(bars[^1].Close)}；" +
                $"价格变化 {N(change)}（{N(change / bars[0].Open * 100m)}%）；高低差 {N(range)}；末价位于区间 {Maybe(closeLocation)}%。");
            lines.Add($"{symbol} · 最高价所在 M5 开始 {Time(bars.First(b => b.High == high).OpenedAtUtc)}；最低价所在 M5 开始 {Time(bars.First(b => b.Low == low).OpenedAtUtc)}。");
            markdown.AppendLine(TradePet.Core.Localization.UiText.Translate($"### {Cell(symbol)} · 当日 M5 原始行情")).AppendLine();
            markdown.AppendLine(TradePet.Core.Localization.UiText.Translate("价格使用所连终端的 K 线口径；开平仓成交价可能受买卖价差影响。时间为交易服务器时间。末根可能尚未收盘，覆盖不足时不能视为全天 OHLC。")).AppendLine();
            markdown.AppendLine(TradePet.Core.Localization.UiText.Translate("| M5 开始 | 开 | 高 | 低 | 收 | Tick量 | 点差（终端点） |"));
            markdown.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (var bar in bars)
                markdown.AppendLine($"| {Time(bar.OpenedAtUtc)} | {N(bar.Open)} | {N(bar.High)} | {N(bar.Low)} | {N(bar.Close)} | {bar.TickVolume} | {bar.Spread} |");
            markdown.AppendLine().AppendLine(TradePet.Core.Localization.UiText.Translate("交易与行情位置（M5 边界 K 线含持仓窗口外价格；下列价格范围不等同于持仓货币 MAE/MFE）：")).AppendLine();
            foreach (var trade in trades.Where(t => t.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
            {
                var entry = bars.FirstOrDefault(b => b.OpenedAtUtc <= trade.OpenedAtUtc && b.OpenedAtUtc.AddMinutes(5) > trade.OpenedAtUtc);
                var holdingEnd = trade.ClosedAtUtc ?? history.Range.RequestedToUtc;
                var during = bars.Where(b => b.OpenedAtUtc < holdingEnd && b.OpenedAtUtc.AddMinutes(5) > trade.OpenedAtUtc).ToArray();
                var after = trade.ClosedAtUtc is { } closed
                    ? bars.Where(b => b.OpenedAtUtc >= closed && b.OpenedAtUtc < closed.AddHours(1)).ToArray() : [];
                markdown.AppendLine(TradePet.Core.Localization.UiText.Translate($"- #{trade.PositionId} · {(trade.Side == TradeSide.Buy ? "买入" : "卖出")} · 入场 {Time(trade.OpenedAtUtc)} @ {N(trade.EntryPrice)}；" +
                    (entry is null ? "缺少入场所在 M5。" : $"入场所在 M5 O/H/L/C {N(entry.Open)}/{N(entry.High)}/{N(entry.Low)}/{N(entry.Close)}。") +
                    (during.Length == 0 ? " 缺少持仓重叠 M5。" : $" 持仓重叠 M5 价格范围 {N(during.Min(b => b.Low))}–{N(during.Max(b => b.High))}（{during.Length} 根）。") +
                    (after.Length == 0 ? " 缺少平仓后1小时 M5。" : $" 已覆盖平仓后1小时价格范围 {N(after.Min(b => b.Low))}–{N(after.Max(b => b.High))}（{after.Length} 根）；这是事后行情，不能单凭它判定过早平仓。")));
            }
            markdown.AppendLine();
        }
        if (symbols.Length == 0) lines.Add("当日没有关联交易品种，未请求行情。");
        lines.Add("形态和退出时机需结合原始走势与实际退出理由复核；单日数据不能证明某形态、时段或方向长期更优。");
        return (new DailyReportSection("当日行情背景", lines), markdown.ToString());
    }

    private static string N(decimal value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
    private static string Maybe(decimal? value) => value.HasValue ? N(value.Value) : "—";
    private static string Cell(string value) => value.Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>");
}
